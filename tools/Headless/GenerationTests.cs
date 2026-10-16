using System;
using System.Linq;
using ShipSim.Core;

// v23 세대 점검 (--generationtest): 조용한 달 넘기기 — 나이 · 솜씨 · 설비 마모 · 기억 · 짝 · 아이 탄생 · 열네 살 성인 · 예순다섯 은퇴 · 노환
//   · 조용한 긴 순항 구간 한가운데에서 저절로 · 사고 중엔 넘기지 않음 · 저장.
public static partial class Program
{
    private static int RunGenerationTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"세대 점검 (v23) · 시드 {seed}\n");
        try
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            GenerationSystem.QuietOff = true;
            PlanetSystem.Off = true; PurposeSystem.Off = true;
            w.CrewCanDie = true;
            Run(w, SimTime.Hours(2));
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var g = w.Generation;

            // 1) 나이 · 솜씨 · 설비 · 기억
            float age0 = crew[0].Age, wear0 = w.Ship.Machines.Average(m => m.Wear);
            float skill0 = crew[0].SkillLevels.Max();
            var span = g.SkipMonths(6, "순항");
            Check("조용한 여섯 달 — 모두 반 살 · 손에 붙은 솜씨 · 설비가 낡고 · 사람마다 한 줄 기억", MathF.Abs(crew[0].Age - age0 - 0.5f) < 0.01f && crew[0].SkillLevels.Max() > skill0 && w.Ship.Machines.Average(m => m.Wear) > wear0 && crew.All(c => c.Memory.Marks.Any(m => m.Text.StartsWith("조용한 여섯 달"))),
                $"나이 {age0:0.0} → {crew[0].Age:0.0} · 솜씨 {skill0:0.00} → {crew[0].SkillLevels.Max():0.00} · 마모 {wear0:0.000} → {w.Ship.Machines.Average(m => m.Wear):0.000} · {crew[0].Memory.Marks.Last().Text}");

            // 2) 짝 · 아이
            var a = crew.First(c => c.Age is >= 20f and <= 40f);
            var b = crew.First(c => c != a && c.Age is >= 20f and <= 40f);
            a.Partner = null; b.Partner = null;
            a.ChangeAffinity(b, 1f); b.ChangeAffinity(a, 1f);
            int paired = 0, born = 0;
            for (int i = 0; i < 6 && born == 0; i++) { var s = g.SkipMonths(6, "순항"); paired += s.Paired; born += s.Born; }
            var child = w.Crew.FirstOrDefault(c => c.BornAboard && !c.Dead);
            Check("가까운 둘은 짝이 되고 · 아이가 태어난다", (a.Partner == b.Id || paired > 0) && born >= 1 && child != null && child.IsChild,
                $"{a.Name}-{b.Name} 짝 {(a.Partner == b.Id ? "됨" : "아님")} · 태어난 아이 {born} · {child?.Name} {child?.Age:0.0}세");

            // 3) 열네 살
            if (child != null)
            {
                child.Age = 13.8f;
                var s = g.SkipMonths(3, "순항");
                Check("열네 살이 되면 일을 배우기 시작한다", !child.IsChild && s.Grown >= 1 && w.History.Events.Any(e => e.Text.Contains("열네 살")), $"{child.Name} {child.Age:0.0}세 · {s.Lines.FirstOrDefault(l => l.Contains("열네 살"))}");
            }

            // 4) 예순다섯 은퇴 · 5) 노환
            var old = crew.First(c => c != a && c != b);
            old.Age = 64.8f;
            w.Policies.Set("elders", 0, "시험");
            var r = g.SkipMonths(3, "순항");
            Check("예순다섯 — 은퇴 (방침이 은퇴면 급한 일 말고는 쉰다)", r.Retired >= 1 && w.History.Events.Any(e => e.Text.Contains("은퇴했다")), r.Lines.FirstOrDefault(l => l.Contains("예순다섯")) ?? "없음");
            var elder = crew.First(c => c != a && c != b && c != old);
            elder.Age = 95f;
            for (int i = 0; i < 8 && !elder.Dead; i++) { g.SkipMonths(6, "순항"); Run(w, 30); }
            Check("아흔이 넘으면 언젠가 조용히 눈을 감는다 (노환)", elder.Dead || elder.Vitals.Health <= 0f, $"{elder.Name} {elder.Age:0}세 · {(elder.Dead ? "떠남" : "살아 있음")} · 원인 {elder.Vitals.InjuryCause}");

            // 6) 저절로: 조용한 긴 순항 구간 한가운데
            var q = World.CreateDefault(seed + 1, 0, "Hanbit");
            PlanetSystem.Off = true;
            GenerationSystem.QuietOff = false;
            Run(q, SimTime.TicksPerDay * 12 + SimTime.Hours(1));
            q.Policies.Set("expedition", 2, "시험"); // 조용한 구간 — 증축 자재가 모자라 원정이 나가면 배가 이틀을 멈춰 서서 '한가운데'가 오지 않는다 (그건 원정 장면)
            q.Voyage.Force(LegKind.DeepVoid, enter: false, days: 4f);
            int spans0 = q.Generation.Spans.Count;
            for (int h = 0; h < 72 && q.Generation.Spans.Count == spans0; h++)
            {
                Run(q, SimTime.Hours(1));
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "quiet" && h % 3 == 0)
                    Console.WriteLine($"      [조용] {h}h 구간 {q.Voyage.Current.Kind} {q.Voyage.Progress:0.00}/{q.Voyage.Current.Days:0.0} · 지휘 건 {q.Commander.Cases.Count(k => k.Open)} · 우주 {q.Cosmic.Main != null} · 원정 {q.Expedition.Current?.Site?.Name} · 새는 방 {q.Ship.LiveRooms.Count(r => r.Leaking)} · 불 {q.Ship.LiveRooms.Count(r => q.Fire.CountIn(r) > 0)} · 체력 최저 {q.Crew.Where(c => !c.Dead && !c.LeftShip).Select(c => c.Vitals.Health).DefaultIfEmpty(1f).Min():0.00} · 건 {string.Join(",", q.Commander.Cases.Where(k => k.Open).Select(k => k.Kind))} · 모자람 {string.Join(",", ExpeditionSystem.Cats.Where(c => q.Expedition.Low(c)))} · 추진제 {q.Propulsion.Propellant:0}/{q.Propulsion.Capacity:0} · 물 {q.Water.Level:0} · 얼음 {q.Ship.CountStored(ItemKind.Ice)} · 전력 {q.Power.Demand:0}/{q.Power.ReactorRated:0}kW 보조 {q.Power.AuxRunning} · 공조 {q.Air.HvacElectricKw:0.0}kW 난방 {q.Air.HeatingKw:0}kW · 제안 {string.Join(" / ", q.Expedition.Proposals.TakeLast(2).Select(pp => $"{pp.Source} · {pp.Why} · {pp.For} · {pp.State} · {pp.DecidedBy}"))}");
            }
            if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "quiet")
                foreach (var e in q.Log.Entries.Where(e => e.Text.Contains("원정") || e.Text.Contains("안건")).TakeLast(12)) Console.WriteLine($"      [원정 기록] {SimTime.Clock(e.Tick)} {e.Text}");
            var auto = q.Generation.Spans.LastOrDefault();
            Check("조용한 긴 구간 한가운데에서 저절로 몇 달을 넘긴다", q.Generation.Spans.Count > spans0 && auto != null && auto.Months >= 2 && q.Log.Entries.Any(e => e.Text.StartsWith("조용한")),
                auto == null ? "넘기지 않음" : $"{auto.Leg} · {auto.Months}달 · 배의 달력 {q.Generation.Calendar} · {q.Log.Entries.Last(e => e.Text.StartsWith("조용한")).Text}");
            // 사고 중엔 넘기지 않는다
            var f = World.CreateDefault(seed + 2, 0, "Hanbit");
            PlanetSystem.Off = true;
            Run(f, SimTime.TicksPerDay * 12 + SimTime.Hours(1));
            f.Voyage.Force(LegKind.DeepVoid, enter: false, days: 4f);
            var room = f.Ship.LiveRooms.Where(x => f.Crew.All(c => c.Room != x)).OrderByDescending(x => x.Cells.Count).First();
            int skips = f.Generation.Spans.Count;
            for (int h = 0; h < 30; h++) { if (f.Fire.CountIn(room) == 0) foreach (var c in room.Cells.Where(f.Ship.IsOpenFloor).Take(2)) f.Fire.Ignite(c, 0.9f); Run(f, SimTime.Hours(1)); }
            Check("불이 나 있으면 넘기지 않는다", f.Generation.Spans.Count == skips, $"넘긴 수 {f.Generation.Spans.Count - skips}");
            GenerationSystem.QuietOff = true;

            // 7) 저장
            var w2 = StateSave.Read(StateSave.Write(w));
            Check("저장 · 되살림 — 넘긴 달 · 기록", w2.Generation.Months == g.Months && w2.Generation.Spans.Count == g.Spans.Count && w2.Generation.LastSkip == g.LastSkip, $"{w2.Generation.Months}달 · {w2.Generation.Spans.Count}번");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        GenerationSystem.QuietOff = false; PlanetSystem.Off = false; PurposeSystem.Off = false;
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
