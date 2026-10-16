using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v13.4 사회: 방침 2차(46개) · 사기 · 일과표 · 베테랑 · 실수 숨기기 · 규칙 위반 · 야간 당직 · 수습
public static partial class Program
{
    private static int RunSocietyTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"사회 점검 (v13.4) · 시드 {seed}\n");
        try
        {
            // 1) 방침 50개 (분야별 · v16 에 화재 출입 통제 · 컴퓨터 제안 · 원정 결정 · 원정 목적지 넷이 늘었다) — 첫 출항 회의가 2차 방침도 가치관대로 정한다
            {
                var areas = PolicySystem.All.GroupBy(p => p.Area).Select(g => $"{g.Key} {g.Count()}");
                var cultures = new List<string>();
                int second = 0;
                foreach (var (ship, s) in new[] { ("Mirinae", seed), ("Hanbit", seed + 1), ("Mirinae", seed + 2), ("Kestrel", seed + 3), ("Hanbit", seed + 4) })
                {
                    var w = World.CreateDefault(s, 0, ship);
                    Run(w, SimTime.Minutes(2));
                    var changed = PolicySystem.All.Skip(26).Where(p => w.Policies[p.Id] != p.Default).Select(p => $"{p.Name} {w.Policies.Option(p.Id)}").ToList();
                    second += changed.Count;
                    cultures.Add($"{ship}/{s} {w.Meetings.Culture}: {(changed.Count > 0 ? string.Join(" · ", changed) : "2차 방침은 처음 값")}");
                }
                Check("방침 50개 — 첫 출항 회의가 2차 방침(생활·사회·세대선·항해·원정)도 가치관대로 정한다", PolicySystem.All.Length == 50 && second > 0,
                    string.Join(" · ", areas) + "\n      " + string.Join("\n      ", cultures));
            }
            // 2) 일과표: 근무 방침이 잠자는 시간을 배 전체로 맞춘다
            {
                var w = DayOne(seed, "Hanbit");
                string Beds() => string.Join(",", w.Crew.Where(c => !c.Dead && !c.IsChild).Select(c => $"{c.Schedule.SleepStart:0}").Distinct().OrderBy(x => x));
                string free = Beds();
                w.Policies.Set("shifts", 0, "시험"); Run(w, SimTime.Minutes(1)); string three = Beds();
                w.Policies.Set("shifts", 1, "시험"); Run(w, SimTime.Minutes(1)); string two = Beds();
                w.Policies.Set("shifts", 2, "시험"); Run(w, SimTime.Minutes(1)); string back = Beds();
                Check("일과표 — 3교대·2교대면 잠자는 시간을 맞추고, 각자 일과면 제 시간으로", three == "15,23,7" && two == "11,23" && back == free,
                    $"각자 {free} → 3교대 {three} → 2교대 {two} → 각자 {back}");
            }
            // 3) 사기: 사람을 잃고 지치면 떨어진다 — 손이 느려진다
            {
                var w = DayOne(seed, "Mirinae");
                Run(w, SimTime.Hours(12));
                float m0 = w.Society.Morale, f0 = w.Society.WorkFactor;
                w.CrewCanDie = true;
                var gone = w.Crew.Where(c => c.CanAct).Take(2).ToList();
                foreach (var c in gone) { c.Vitals.Health = 0f; c.Down = true; }
                foreach (var c in w.Crew) c.Needs.Stress = MathF.Max(c.Needs.Stress, 0.7f);
                Run(w, SimTime.Hours(24));
                float m1 = w.Society.Morale;
                Check("사기 — 사람을 잃고 지치면 떨어지고, 손이 느려진다", m1 < m0 - 0.1f && w.Society.WorkFactor < f0,
                    $"사기 {m0 * 100:0}% → {m1 * 100:0}% ({w.Society.MoraleWhy}) · 손 {f0:0.00} → {w.Society.WorkFactor:0.00} · 기록 {w.Society.MoraleHistory.Count}점");
            }
            // 4) 베테랑: 사고 대응을 열두 번 넘게 겪으면
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => x.CanAct);
                float p0 = w.Meetings.Persuasion(c, 0.5f);
                c.Stats.Emergencies = 14;
                Run(w, SimTime.Minutes(1));
                var line = w.History.Events.LastOrDefault(e => e.Text.Contains("베테랑"));
                Check("베테랑 — 사고 대응 열두 번이 넘으면 베테랑 (말에 무게가 실리고 비상 일이 빠르다)", w.Society.IsVeteran(c) && line != null && w.Meetings.Persuasion(c, 0.5f) > p0,
                    $"{line?.Text} · 설득력 {p0:0.00} → {w.Meetings.Persuasion(c, 0.5f):0.00}");
            }
            // 5) 실수 숨기기: 규칙을 앞세우는 사람은 털어놓고, 자유를 앞세우는 사람은 숨긴다 — 벌이 무거우면 더 숨긴다
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Crew.First(x => x.CanAct);
                var b = w.Crew.Last(x => x.CanAct);
                a.Value = CrewValue.Rules; b.Value = CrewValue.Freedom;
                int Count(CrewMember c, int violations) { w.Policies.Set("violations", violations, "시험"); int n = 0; for (int i = 0; i < 400; i++) if (w.Society.WillHide(c)) n++; return n; }
                int rules = Count(a, 0), freedom = Count(b, 0), harsh = Count(b, 1);
                Check("실수 숨기기 — 규칙파는 털어놓고 자유파는 숨긴다, 벌이 무거우면 더 숨긴다", rules < freedom && harsh > freedom,
                    $"400번 중 숨김: 규칙 {rules} · 자유 {freedom} · 자유+근무 박탈 방침 {harsh}");
            }
            // 6) 숨긴 실수가 드러나면 벌 (규칙 위반: 근무 박탈)
            {
                var w = DayOne(seed, "Mirinae");
                w.Policies.Set("violations", 1, "시험");
                var c = w.Crew.First(x => x.CanAct);
                var m = w.Ship.Machines.First();
                for (int i = 0; i < 20 && w.Society.Revealed == 0; i++) w.Society.Surfaced(c, m, "시험");
                var o = w.Board.Open.FirstOrDefault(x => x.Urgency < 0.9f);
                float appeal = o != null ? ChoresActivity.Appeal(c, w, o, w.Paths.Flood(c.Cell, c.PathProfile), out _) : -1f;
                Check("규칙 위반 — 숨긴 실수가 드러나면 비난과 근무 박탈 (급하지 않은 일은 하지 않는다)", w.Society.Revealed >= 1 && w.Society.Suspended(c) && appeal < 0f,
                    $"드러남 {w.Society.Revealed} · 근무 박탈 {(w.Society.Suspended(c) ? "예" : "아니오")} · 급하지 않은 일 끌림 {appeal:0.00} · {w.History.Events.LastOrDefault(e => e.Text.Contains("숨긴 실수"))?.Text}");
            }
            // 7) 야간 당직: 밤에 당직이 배를 돈다 (방침 1명) ↔ 컴퓨터에 맡기면 아무도 돌지 않는다
            {
                int Patrol(int policy)
                {
                    var w = DayOne(seed, "Mirinae");
                    w.Policies.Set("nightwatch", policy, "시험");
                    int n = 0;
                    for (int h = 0; h < 24; h++)
                    {
                        Run(w, SimTime.Hours(1));
                        n += w.Crew.Count(c => c.Job?.Activity is PatrolActivity);
                    }
                    return w.Society.Patrols;
                }
                int one = Patrol(0), none = Patrol(2);
                Check("야간 당직 — 밤에 당직이 배를 돈다 (컴퓨터에 맡기면 돌지 않는다)", one > 0 && none == 0, $"1명: 순찰 {one}번 · 컴퓨터에 맡김: {none}번");
            }
            // 8) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 사회가 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 사회 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
