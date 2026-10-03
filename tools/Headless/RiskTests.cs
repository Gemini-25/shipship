using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>통합5 위험 수준 (사용자 결정): 보통 재해는 가끔 사망 · 배 전체급은 큰 피해 · 우주급만 진짜 생존 위기 (대비한 배는 버틴다).</summary>
public static partial class Program
{
    private static int RunRiskTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"위험 수준 시험 (시드 {seed})");
        bool dbg = Environment.GetEnvironmentVariable("RISK_DEBUG") == "1";

        // ── 1) 우주급: 대비 못 한 배(예보 없음 · 몇십 분 전) ↔ 대비한 배(열 시간 전 예보) — 같은 별의 재난 ──
        //    통합6 초신성은 섬광 뒤 열여덟 시간 뒤에 방사선이 닿아, 컴퓨터를 고친 배는 그새 대비한다 — 보이지도 들리지도 않는 우주선 소나기로 본다 (계기 없이는 토하고서야 안다)
        var Kind = Environment.GetEnvironmentVariable("RISK_KIND") is string rk && Enum.TryParse<CosmicKind>(rk, out var kk) ? kk : CosmicKind.CosmicRayShower;
        {
            (int dead, int sick, float maxDose, string who) Nova(bool prepared)
            {
                var w = DayOne(seed, "Mirinae");
                w.CrewCanDie = true;
                if (!prepared) foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked); // 예보도 방송도 없다
                if (!prepared && w.Sensors.Array?.Machine is Machine sa) w.Machines.Break(sa, FaultKind.Wrecked); // 통합6 센서도 부서졌다 — 통신실 화면에도 안 잡힌다 (몸으로 겪는다)
                var e = w.Cosmic.Force(Kind, prepared ? 10f : 0.5f, close: true, side: 0f);
                for (int i = 0; i < 6 * 24 * 3 && e.Phase < CosmicPhase.After; i++)
                {
                    float d0 = w.Crew.Sum(c => c.Dose);
                    Run(w, SimTime.Minutes(10));
                    if (dbg && w.Crew.Sum(c => c.Dose) > d0 + 0.05f)
                        Console.WriteLine($"     [{(prepared ? "대비" : "못 함")} {SimTime.Clock(w.Tick)} 쬠 {e.KnownBy} 컴퓨터 {w.Automation.MainOnline} {e.Phase}] " + string.Join(" | ", w.Crew.Where(c => c.Dose > 0.3f).Select(c => $"{c.Name} {c.Dose:0.0}Sv {c.Room?.Name}({c.Room?.Radiation:0.00}) {c.Job?.Label} {c.Pose}")));
                }
                for (int i = 0; i < 6 * 24 * 3; i++)
                {
                    Run(w, SimTime.Minutes(10));
                    if (dbg && i % 36 == 0) Console.WriteLine($"     [{(prepared ? "대비" : "못 함")} {SimTime.Clock(w.Tick)}] " + string.Join(" | ", w.Crew.Select(c => $"{c.Name} {c.Dose:0.0}Sv {(c.Dead ? "숨짐" : $"{c.Vitals.Health * 100:0}% {c.Room?.Name}")}")));
                }
                return (w.Crew.Count(c => c.Dead), w.Crew.Count(c => c.Dose >= 2f), w.Crew.Max(c => c.Dose),
                    string.Join(", ", w.Crew.Select(c => $"{c.Name} {c.Dose:0.0}Sv{(c.Dead ? "†" : "")}")));
            }
            var ready = Nova(true);
            var bare = Nova(false);
            Console.WriteLine($"  {Kind} — 대비한 배: 사망 {ready.dead} · 2Sv 넘음 {ready.sick} ({ready.who})");
            Console.WriteLine($"  {Kind} — 대비 못 한 배: 사망 {bare.dead} · 2Sv 넘음 {bare.sick} ({bare.who})");
            // 통합6 계기 없이는 토하고서야(2Sv 남짓) 안다 — 대비 못 한 배는 절반 넘게 방사선 병(토함 · 피가 줄어듦)을 앓거나 누가 숨진다
            Check("우주급 — 대비 못 한 배는 진짜 생존 위기 (크게 쬔 사람 · 숨진 사람)", bare.dead >= 1 || bare.sick * 2 >= 6,
                $"사망 {bare.dead} · 2Sv 넘음 {bare.sick} · 최대 {bare.maxDose:0.0}Sv");
            Check("우주급 — 대비한 배는 버틴다 (못 한 배보다 덜 죽고 덜 쬔다)", ready.dead < Math.Max(1, bare.dead) && ready.maxDose < bare.maxDose,
                $"사망 {ready.dead} ↔ {bare.dead} · 최대 {ready.maxDose:0.0} ↔ {bare.maxDose:0.0}Sv");
        }

        // ── 2) 보통 재해: 운이 나쁘거나 늦으면 — 혼자 실험하다 축전기 방전에 심장이 멎는다 (곁에 사람이 있으면 가슴을 눌러 살린다) ──
        {
            (CrewMember v, World w) Arrest(bool partner)
            {
                var w = DayOne(seed, "Hanbit");
                w.CrewCanDie = true;
                if (!partner) foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked); // 컴퓨터도 생체 신호를 못 본다
                var lab = w.Ship.Rooms.Where(r => r.Type != RoomType.Corridor && !r.Detached && r.Cells.Count(w.Ship.IsOpenFloor) >= 4)
                    .OrderBy(r => w.Crew.Count(c => c.Room == r)).ThenByDescending(r => r.Cells.Count).ThenBy(r => r.Id).First();
                var spots = lab.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
                var v = w.Crew.Where(c => c.CanAct && !c.IsChild && c.Role != CrewRole.Medic).OrderBy(c => c.Id).First();
                Stay(w, v, spots[0], Pose.Working);
                if (partner && w.Crew.Where(c => c != v && c.CanAct && !c.IsChild).OrderByDescending(c => c.Role == CrewRole.Medic).ThenBy(c => c.Id).First() is CrewMember p)
                    Stay(w, p, spots.OrderBy(c => Math.Abs(c.X - spots[0].X) + Math.Abs(c.Y - spots[0].Y)).Skip(1).First(), Pose.Working);
                else foreach (var o in w.Crew.Where(o => o != v && o.Room == lab)) Stay(w, o, w.Ship.RoomsOf(RoomType.Corridor).First().Cells.First(w.Ship.IsOpenFloor), Pose.Standing);
                NeedsSystem.AddInjury(v.Vitals, 0.3f, "축전기 방전 감전");
                w.Casualty.Inflict(v, TraumaKind.Arrest, 0.6f, "축전기 방전 감전");
                for (int i = 0; i < 60 && !v.Dead; i++) Run(w, SimTime.Minutes(1));
                return (v, w);
            }
            var (a, wa) = Arrest(false);
            var death = wa.History.Events.LastOrDefault(h => h.Text.Contains(a.Name) && h.Text.Contains("숨졌다"));
            Check("보통 재해 — 혼자 있다 심장이 멎으면 숨진다 (까닭이 연대기 · 기억에 남는다)", a.Dead && death != null && (death.Text.Contains("혼자") || death.Text.Contains("늦었다")),
                $"{a.Name} {(a.Dead ? "숨짐" : "살았다")} · \"{death?.Text}\"");
            var (b, wb) = Arrest(true);
            Check("보통 재해 — 곁에 사람이 있으면 가슴을 눌러 살린다", !b.Dead && wb.Casualty.Revived > 0,
                $"{b.Name} {(b.Dead ? "숨짐" : "살았다")} · 되살림 {wb.Casualty.Revived}");
        }

        // ── 3) 넓게 덴 몸: 곁의 사람이 식혀 감싸도 수액(의무관 · 의무실)이 있어야 멎는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            w.CrewCanDie = true;
            var v = w.Crew.Where(c => c.CanAct && !c.IsChild && c.Role != CrewRole.Medic && c.SkillLevel(Skill.Medicine) < 0.5f).OrderBy(c => c.Id).First();
            var lay = w.Crew.Where(c => c != v && c.CanAct && !c.IsChild && c.Role != CrewRole.Medic && c.SkillLevel(Skill.Medicine) < 0.5f).OrderBy(c => c.Id).First();
            var room = w.Ship.RoomsOf(RoomType.Mess).First();
            var spots = room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
            Stay(w, v, spots[0], Pose.Sitting);
            Stay(w, lay, spots[1], Pose.Working);
            NeedsSystem.AddInjury(v.Vitals, 0.4f, "불길에 덴 화상");
            w.Casualty.Inflict(v, TraumaKind.BurnShock, 0.2f, "불길에 덴 화상");
            Run(w, SimTime.Minutes(8));
            var t = w.Casualty.Of(v);
            bool slowed = t != null && t.Rate < 0.15f && w.Log.Entries.Any(e => e.Text.Contains("식혀 감쌌다 — 넓게 데어"));
            Check("보통 재해 — 넓게 덴 사람은 곁의 사람이 식혀 감싸도 멎지 않는다 (늦출 뿐)", slowed, t == null ? "벌써 멎었다: " + string.Join(" / ", w.Casualty.Done.Select(d => d.Outcome)) : $"남은 속도 {t.Rate:0.000}/시");
            for (int i = 0; i < 24 && w.Casualty.Of(v) != null && !v.Dead; i++) Run(w, SimTime.Minutes(10));
            var done = w.Casualty.Done.LastOrDefault(d => d.CrewId == v.Id);
            Check("보통 재해 — 의무관 · 의무실이 손을 쓰면 화상 쇼크가 멎는다", !v.Dead && done != null && done.Outcome != "숨졌다",
                $"{v.Name} {(v.Dead ? "숨짐" : "살았다")} · {done?.Outcome}");
        }

        Console.WriteLine(_fails == 0 ? "✔ 위험 수준 시험 통과" : $"✘ 실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
