using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>통합5 방사선 병 간호: 수액 · 골수 주사 · 수혈 · 격리 · 곁을 지킴 → "손썼지만 못 살림"과 "손써서 살림"이 갈린다.</summary>
public static partial class Program
{
    private static int RunRadCareTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"방사선 병 간호 시험 (시드 {seed})");

        (World w, CrewMember p) Scene(float dose, bool off)
        {
            RadCareSystem.Off = off;
            var w = DayOne(seed, "Hanbit");
            w.CrewCanDie = true;
            var p = w.Crew.Where(c => c.CanAct && !c.IsChild && c.Role != CrewRole.Medic && !c.Quals.Contains(Qual.Medic)).OrderBy(c => c.Id).First();
            p.Dose = dose;
            return (w, p);
        }

        // ── 1) 8.5Sv — 의무관이 수액 · 골수 주사, 피를 줄 사람이 곁에 와 수혈 → 고비를 넘긴다 ──
        {
            var (w, p) = Scene(8.5f, false);
            var rc = w.RadCare;
            long t0 = w.Tick;
            bool sawCare = false, sawDonor = false;
            for (int i = 0; i < 48 * 6 && !p.Dead; i++)
            {
                Run(w, SimTime.Minutes(10));
                if (w.Crew.Any(c => c.Job?.Activity is RadCareActivity)) sawCare = true;
                if (w.Crew.Any(c => c.Job?.Activity is GiveBloodActivity)) sawDonor = true;
            }
            RadCareSystem.Off = false;
            var pt = rc.Of(p);
            var advice = w.Automation.Book.Acts.FirstOrDefault(a => a.Key == $"radcare:{p.Id}");
            Check("주 컴퓨터가 피폭 기록을 읽고 필요한 처치(수혈)와 피를 줄 사람을 알린다", advice != null && advice.Act.Contains("수혈"),
                advice != null ? $"{advice.Observe} → {advice.Act}" : "없음");
            Check("의무관이 구급 키트를 들고 가 수액 · 골수 주사를 놓는다", sawCare && rc.FluidsGiven > 0 && rc.StimsGiven > 0,
                $"간호 {sawCare} · 수액 {rc.FluidsGiven} · 골수 주사 {rc.StimsGiven} · {string.Join(" / ", w.Log.Entries.Where(e => e.Text.Contains("방사선 병 처치")).Take(2).Select(e => e.Text))}");
            var donor = pt?.LastDonor >= 0 ? w.Crew.First(c => c.Id == pt.LastDonor) : null;
            Check("피폭이 적은 사람이 곁에 와 앉고 → 피를 나눠 준다 (기억 · 고마움 · 한동안 쉰다)", sawDonor && rc.Transfusions > 0 && donor != null
                && donor.Memory.Marks.Any(m => m.Text.Contains("피를 나눠")) && w.Relations.Of(p, donor).Any(m => m.Reason == RelationReason.SavedMe),
                $"곁에 앉음 {sawDonor} · 수혈 {rc.Transfusions} · 준 사람 {donor?.Name ?? "-"} · 부름 {rc.DonorCalls}");
            Check("손을 쓰면 8.5Sv도 고비를 넘긴다 (기록 · 연대기)", !p.Dead && pt?.Stable == true && w.History.Events.Any(h => h.Text.Contains("고비를 넘겼다")),
                $"{p.Name} {(p.Dead ? "숨짐" : "살았다")} · 체력 {p.Vitals.Health * 100:0}% · 고비 {pt?.Stable} · {(w.Tick - t0) / SimTime.TicksPerHour}시간");

            // 견줌: 손쓰지 않으면
            var (w0, p0) = Scene(8.5f, true);
            for (int i = 0; i < 48 * 6 && !p0.Dead; i++) Run(w0, SimTime.Minutes(10));
            RadCareSystem.Off = false;
            Check("간호가 없으면 같은 피폭이 더 위험하다 (예전과 견줌)", p0.Dead || p0.Vitals.Health < p.Vitals.Health - 0.1f,
                $"간호 없음: {(p0.Dead ? $"숨짐 {SimTime.Clock(p0.DiedAt)}" : $"체력 {p0.Vitals.Health * 100:0}%")} · 간호: {(p.Dead ? "숨짐" : $"체력 {p.Vitals.Health * 100:0}%")}");
        }

        // ── 2) 14Sv — 손을 써도 몇 시간 늦출 뿐 · 곁을 지킨다 → "받았지만 버티지 못했다" ──
        {
            var (w, p) = Scene(14f, false);
            var rc = w.RadCare;
            int nearAtEnd = 0;
            for (int i = 0; i < 24 * 6 && !p.Dead; i++)
            {
                Run(w, SimTime.Minutes(10));
                if (!p.Dead && p.Vitals.Health < 0.2f)
                    nearAtEnd = w.Crew.Count(c => c != p && !c.Dead && c.Job?.Activity is RadCareActivity && (c.Position - p.Position).Length() < 3f);
            }
            var death = w.History.Events.LastOrDefault(h => h.Text.Contains(p.Name) && h.Text.Contains("숨졌다"));
            Check("12Sv 넘으면 손을 써도 숨진다 — 연대기에 \"받았지만 버티지 못했다\"", p.Dead && death != null && death.Text.Contains("받았지만") && rc.LostAnyway > 0,
                $"{(p.Dead ? $"숨짐 {SimTime.Clock(p.DiedAt)}" : "살았다")} · \"{death?.Text}\"");
            Check("숨이 넘어갈 때 의무관이 곁을 지킨다 (손을 놓지 않는다)", rc.Vigils > 0 || nearAtEnd > 0, $"곁 지킴 {rc.Vigils} · 마지막에 곁 {nearAtEnd}명");
            Check("주 컴퓨터는 손쓸 길이 거의 없다고 말한다", w.Automation.Book.Acts.Any(a => a.Key.StartsWith("radcare") && a.Act.Contains("곁을 지켜")),
                string.Join(" / ", w.Automation.Book.Acts.Where(a => a.Key.StartsWith("radcare")).Select(a => a.Act)));
        }

        // ── 3) 골수가 무너진 몸 · 북적이는 방: 컴퓨터가 문병을 줄이라고 한다 · 격리 설비가 있는 의무실은 격리 ──
        {
            var (w, p) = Scene(8f, false);
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            var cells = mess.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
            var others = w.Crew.Where(c => c != p && c.CanAct && !c.IsChild).Take(3).ToList();
            Teleport(w, p, cells[0]);
            for (int i = 0; i < others.Count; i++) Teleport(w, others[i], cells[2 + i]);
            Run(w, SimTime.Minutes(12));
            var pt = w.RadCare.Of(p);
            bool crowd = w.Automation.Book.Acts.Any(a => a.Key == $"radcrowd:{p.Id}" && a.Act.Contains("문병"));
            bool iso = pt != null && w.RadCare.Isolated(p, pt);
            Check("북적이는 방의 환자 — 컴퓨터가 문병을 줄이고 격리하라고 한다 (격리 아님)", crowd && !iso,
                $"권고 {crowd} · 격리 {iso} · {mess.Name} 곁 {mess.Cells.Count(c => w.Crew.Any(x => x.Cell == c))}명");
        }

        // ── 결정론 ──
        uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); w.CrewCanDie = true; Run(w, SimTime.Hours(2)); w.Crew[1].Dose = 8.5f; Run(w, SimTime.Hours(10)); return SaveGame.StateHash(w); }
        Check("결정론 — 같은 시드면 같은 간호", H() == H(), "");
        Console.WriteLine(_fails == 0 ? "✔ 방사선 병 간호 시험 통과" : $"✘ 실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
