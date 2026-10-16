using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15 일상 사건 70: 사고가 아닌 날에도 — 누가 · 왜 · 무엇이 바뀌었고 · 누가 기억하나
public static partial class Program
{
    private static int RunDailyTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"일상 사건 점검 (v15) · 시드 {seed}\n");
        try
        {
            // 1) 저절로: 며칠 지내면 여러 갈래의 일상 사건이 일어난다
            foreach (var ship in new[] { "Mirinae", "Hanbit" })
            {
                var w = DayOne(seed, ship);
                int mem0 = w.Relations.All.Count, diary0 = w.Crew.Sum(c => c.Diary.Count);
                Run(w, SimTime.TicksPerDay * 4);
                var st = w.Daily.Stats;
                int groups = st.ByGroup.Count(n => n > 0);
                Check($"저절로 ({ship}) — 나흘 동안 여러 갈래의 일상이 일어나고 기억이 쌓인다", st.Fired >= 15 && groups >= 6 && w.Relations.All.Count > mem0 && w.Crew.Sum(c => c.Diary.Count) > diary0,
                    $"{st.Summary()} · 관계 기억 {mem0} → {w.Relations.All.Count}");
                foreach (var (t, id, text) in w.Daily.Recent.Take(6)) Console.WriteLine($"      {SimTime.Day(t)}일 {SimTime.Clock(t)} [{id}] {text}");
            }

            // 2) 하나하나: 70가지가 조건이 맞으면 저마다 일어난다 (여러 배·시각·상태에서 시도)
            {
                var fired = new HashSet<string>();
                var why = new Dictionary<string, string>();
                for (int k = 0; k < 4; k++)
                {
                    var w = DayOne(seed + k * 101, k % 2 == 0 ? "Mirinae" : "Hanbit");
                    // 조건이 드문 것들: 오래 지낸 배 · 사고 한 번 · 사람을 잃음 · 낡은 설비 · 빌린 물건
                    Run(w, SimTime.Hours(6 + k * 5));
                    if (k >= 2) { w.CrewCanDie = true; w.Crew.Where(c => !c.IsChild).OrderBy(c => c.Id).Skip(3).First().Vitals.Health = 0f; Run(w, SimTime.Hours(1)); w.CrewCanDie = false; }
                    foreach (var m in w.Ship.Machines.Take(6)) { m.Wear = 0.6f; m.SensorCal = 0.6f; for (int i = 0; i < 3; i++) MarkLog.Add(m.Marks, w.Tick, "시험"); }
                    foreach (var b in w.Belongings.All.Take(4)) b.Condition = 0.5f;
                    foreach (var c in w.Crew) c.Needs.Stress = 0.6f;
                    w.Soil.RoomSoil(w.Ship.LiveRooms.First())[(int)SoilKind.Oil] = 0.4f;
                    w.History.Add(w, HistoryKind.Incident, "시험 화재", w.Ship.LiveRooms.First());
                    for (int h = 0; h < 30; h++)
                    {
                        Run(w, SimTime.Hours(1));
                        foreach (var spec in DailySystem.Catalog)
                        {
                            if (fired.Contains(spec.Id)) continue;
                            if (w.Daily.Fire(null, spec.Id) != null) fired.Add(spec.Id);
                        }
                    }
                }
                var missing = DailySystem.Catalog.Where(s => !fired.Contains(s.Id)).Select(s => $"{s.Id}({s.Name})").ToList();
                Check("하나하나 — 70가지 일상이 조건이 맞으면 저마다 일어난다", DailySystem.Catalog.Length == 70 && fired.Count >= 62,
                    $"{fired.Count}/70 일어남" + (missing.Count > 0 ? $" · 안 일어난 것: {string.Join(", ", missing)}" : ""));
            }

            // 3) 위기 중엔 일상이 멈춘다
            {
                var w = DayOne(seed, "Mirinae");
                var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Lounge).OrderBy(r => r.Id).First();
                w.Fire.Ignite(room.Cells.First(c => w.Ship.IsWalkable(c)), 1f);
                int before = w.Daily.Stats.Fired, during = 0;
                for (int m = 0; m < 120; m++)
                {
                    int f0 = w.Daily.Stats.Fired;
                    Run(w, SimTime.Minutes(1));
                    if (Crisis.Acting(w) && w.Daily.Stats.Fired > f0) during++;
                }
                Check("위기 중엔 — 불이 난 동안 일상 사건은 일어나지 않는다", during == 0, $"위기 중 일어난 일상 {during}");
            }

            // 4) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 일상 사건이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 일상 사건 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
