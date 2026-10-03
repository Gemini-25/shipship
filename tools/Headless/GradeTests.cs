using System;
using System.Linq;
using ShipSim.Core;

// 부상 등급 (경상 · 중상 · 위중) — 등급이 오르고 · 기록 · 방송 · 치료 순서 · 고비를 넘김
public static partial class Program
{
    private static int RunGradeTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"부상 등급 점검 · 시드 {seed}\n");
        try
        {
            var w = DayOne(seed, "Hanbit");
            var crew = w.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild && !c.Away && !c.Outside).OrderBy(c => c.Id).Take(3).ToList();
            var (a, b, d) = (crew[0], crew[1], crew[2]);
            int cast0 = w.Grades.Broadcasts;

            // 1) 경상: 손가락을 조금 베였다 — 방송 없음
            NeedsSystem.AddInjury(a.Vitals, 0.08f, "작업 중 베임");
            Run(w, SimTime.Minutes(3));
            Check("경상 — 조금 베인 사람은 경상 (스스로 감는다 · 방송하지 않는다)", w.Grades.Now(a) == InjuryGrade.Minor && w.Grades.Broadcasts == cast0,
                $"{a.Name}: {InjuryGradeSystem.Name(w.Grades.Now(a))} · 방송 {w.Grades.Broadcasts - cast0}");

            // 2) 중상: 넓게 데었다 (화상 쇼크 — 아직 급하지는 않다)
            NeedsSystem.AddInjury(b.Vitals, 0.2f, "증기 화상");
            Run(w, SimTime.Minutes(3));
            var gb = w.Grades.Now(b);
            Check("중상 — 넓게 덴 사람은 중상 (치료를 받아야 낫는다) · 기록에 등급과 부위", gb == InjuryGrade.Serious && w.Log.Entries.Any(e => e.Text.StartsWith($"{b.Name} — 중상")),
                $"{b.Name}: {InjuryGradeSystem.Name(gb)} · {w.Grades.Detail(b)} · 외상 {w.Casualty.Of(b)?.Kind.ToString() ?? "-"}");

            // 3) 위중: 심장이 섰다 — 주컴퓨터가 위중부터 방송
            w.Casualty.Inflict(d, TraumaKind.Arrest, 0.6f, "시험 감전");
            Run(w, SimTime.Minutes(2));
            var gd = w.Grades.Now(d);
            bool cast = w.Grades.Broadcasts > cast0;
            Check("위중 — 심정지는 위중 · 주컴퓨터가 다친 사람을 위중부터 알린다", gd == InjuryGrade.Critical && cast && w.Log.Entries.Any(e => e.Text.Contains("[방송]") && e.Text.Contains("위중")),
                $"{d.Name}: {InjuryGradeSystem.Name(gd)} · 방송 {w.Grades.Broadcasts - cast0} · \"{w.Log.Entries.Where(e => e.Text.Contains("다친 사람")).Select(e => e.Text).LastOrDefault()}\"");

            // 4) 치료 순서: 위중(피가 멎지 않는다)한 사람의 치료 일감이 중상(크게 다쳤지만 급하지 않다)보다 앞선다
            {
                var w3 = DayOne(seed, "Hanbit");
                var pp = w3.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild && !c.Away && !c.Outside).OrderBy(c => c.Id).Take(2).ToList();
                var (crit, ser) = (pp[0], pp[1]);
                NeedsSystem.AddInjury(ser.Vitals, 0.35f, "식중독");
                w3.Casualty.Inflict(crit, TraumaKind.Bleed, 0.12f, "시험 베임");
                NeedsSystem.AddInjury(crit.Vitals, 0.1f, "시험 베임");
                Run(w3, SimTime.Minutes(2));
                w3.Board.RequestScan();
                Run(w3, SimTime.Minutes(1));
                var oc = w3.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Treat && o.Target.Crew == crit);
                var os = w3.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Treat && o.Target.Crew == ser);
                Check("치료 순서 — 위중한 사람이 먼저 (치료 일감의 급함 · 일감에 등급이 적힌다)",
                    w3.Grades.Now(crit) == InjuryGrade.Critical && w3.Grades.Now(ser) == InjuryGrade.Serious && oc != null && os != null && oc.Urgency > os.Urgency && oc.Detail.StartsWith("위중") && os.Detail.StartsWith("중상"),
                    $"{InjuryGradeSystem.Name(w3.Grades.Now(crit))} {oc?.Urgency:0.00} \"{oc?.Detail}\" / {InjuryGradeSystem.Name(w3.Grades.Now(ser))} {os?.Urgency:0.00} \"{os?.Detail}\"");
            }

            // 5) 연대기 · 곁의 사람
            Check("연대기 — 중상 · 위중은 역사에 남는다", w.History.Events.Any(h => h.Text.Contains(d.Name) && h.Text.Contains("위중")) && w.History.Events.Any(h => h.Text.Contains(b.Name) && h.Text.Contains("크게 다쳤다")),
                string.Join(" / ", w.History.Events.Where(h => h.Text.Contains("위중") || h.Text.Contains("크게 다쳤다")).TakeLast(2).Select(h => h.Text)));

            // 6) 고비를 넘김: 피가 멎어 간다 (급하지 않은 출혈) → 위중에서 내려온다
            var w2 = DayOne(seed, "Hanbit");
            var e2 = w2.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild && !c.Away && !c.Outside).OrderBy(c => c.Id).First();
            w2.Casualty.Inflict(e2, TraumaKind.Bleed, 0.12f, "시험 베임");
            Run(w2, SimTime.Minutes(2));
            var g0 = w2.Grades.Now(e2);
            if (w2.Casualty.Of(e2) is Trauma t) t.Rate = 0.03f;
            Run(w2, SimTime.Minutes(2));
            Check("고비를 넘김 — 위중에서 내려오면 기록 · 연대기에 남는다", g0 == InjuryGrade.Critical && w2.Grades.Now(e2) < InjuryGrade.Critical && w2.Grades.Eased >= 1 && w2.Log.Entries.Any(x => x.Text.Contains("고비를 넘겼다")),
                $"{e2.Name}: {InjuryGradeSystem.Name(g0)} → {InjuryGradeSystem.Name(w2.Grades.Now(e2))} · 넘김 {w2.Grades.Eased}");

            // 7) 결정론
            uint H() { var x = DayOne(seed, "Hanbit"); var c = x.Crew.First(k => !k.Dead); NeedsSystem.AddInjury(c.Vitals, 0.3f, "운석 파편"); Run(x, SimTime.Hours(2)); return SaveGame.StateHash(x); }
            uint h1 = H(), h2 = H();
            Check("결정론 — 같은 시드 같은 다침이면 같은 배", h1 == h2, $"{h1:x8} / {h2:x8}");
        }
        catch (Exception ex)
        {
            Check("예외 없음", false, ex.ToString());
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 부상 등급 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
