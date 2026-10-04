using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 모든 배에서 주컴퓨터 · 로봇 · 드론이 실제로 일하나 (--syscheck [--only=Key,Key]).
///   ① 평상시 하루: 로봇이 한 일 · 멈춰 선 로봇 (일하러 나가서 세 시간 넘게 제자리) · 드론 출격 · 주컴퓨터가 켜져 있나
///   ② 주방 불: 주컴퓨터가 알아채고 알리나 · 방재 로봇 출동 · 꺼지기까지
///   ③ 창고 외벽 운석: 구멍이 막히기까지 · 드론 출격 · 주컴퓨터가 알리나
/// 배마다 한 줄 + 문제 목록. 문제가 있으면 1.
/// </summary>
public static partial class Program
{
    private static int RunSysCheck(int seed, string[] args)
    {
        var only = args.FirstOrDefault(a => a.StartsWith("--only="))?[7..].Split(',');
        var keys = ShipCatalog.All.Select(t => t.Key).Where(k => only == null || only.Contains(k)).ToList();
        Console.WriteLine($"주컴퓨터 · 로봇 · 드론 — 모든 배 실제 점검 · 시드 {seed} · {keys.Count}척\n");
        var problems = new List<string>();
        foreach (var key in keys)
        {
            var w = DayOne(seed, key);
            string name = w.Ship.Name;
            var a = w.Automation;
            int robots = w.Robots.Robots.Count, drones = w.Drones.Drones.Count;
            bool online0 = a.Present && a.MainOnline;

            // ① 평상시 하루
            int rj0 = w.Robots.JobsDone, ds0 = w.Drones.Sorties, dj0 = w.Drones.JobsDone;
            var last = w.Robots.Robots.ToDictionary(r => r.Id, r => (pos: r.Position, since: w.Tick, doing: r.Doing));
            var stuck = new Dictionary<int, string>();
            int offlineMin = 0;
            for (int m = 0; m < 24 * 6; m++)
            {
                Run(w, SimTime.Minutes(10));
                if (!a.MainOnline) offlineMin += 10;
                foreach (var r in w.Robots.Robots)
                {
                    var l = last[r.Id];
                    bool moved = (r.Position - l.pos).Length() > 0.6f;
                    if (moved || r.State != RobotState.Active || r.Helping != null) { last[r.Id] = (r.Position, w.Tick, r.Doing); continue; }
                    if (w.Tick - l.since > SimTime.Hours(3) && !stuck.ContainsKey(r.Id))
                        stuck[r.Id] = $"{r.Name}({r.KindName}) {r.Room?.Name ?? "?"} · {r.Doing}";
                }
            }
            int rjobs = w.Robots.JobsDone - rj0, dsort = w.Drones.Sorties - ds0, djobs = w.Drones.JobsDone - dj0;
            int faulty = w.Robots.Robots.Count(r => !r.Operational), dfaulty = w.Drones.Drones.Count(d => !d.Operational);

            // ② 주방 불
            var galley = w.Ship.RoomsOf(RoomType.Galley).FirstOrDefault(r => !r.Detached);
            string fireRes = "주방 없음";
            int compFire = 0, ff0 = w.Robots.FiresFought;
            bool fireOut = true;
            if (galley != null)
            {
                var spot = galley.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - galley.Center).LengthSquared()).First();
                Player.Fire(w, spot);
                long t0 = w.Tick, outAt = -1;
                for (int t = 0; t < SimTime.Hours(4) && outAt < 0; t += SimTime.Minutes(1))
                {
                    Run(w, SimTime.Minutes(1));
                    if (w.Fire.Count == 0) outAt = w.Tick;
                }
                compFire = w.Log.Entries.Where(e => e.Tick >= t0).Count(e => e.Text.Contains("주 컴퓨터") || e.Text.Contains(a.Voice.Call) || e.Text.Contains("[방송]"));
                if (args.Contains("--firedebug")) { Console.WriteLine($"     컴퓨터 이름 '{a.Voice.Call}'"); foreach (var e in w.Log.Entries.Where(e => e.Tick >= t0).Take(40)) Console.WriteLine($"     {SimTime.Clock(e.Tick)} [{e.Kind}] {e.Text}"); }
                fireOut = outAt > 0;
                fireRes = fireOut ? $"{(outAt - t0) / (float)SimTime.Minutes(1):0}분" : "4시간 넘게 탐";
                Run(w, SimTime.Hours(1));
            }
            int fought = w.Robots.FiresFought - ff0;

            // ③ 창고 외벽 운석 (창고 · 화물칸 · 정비실 순)
            var outer = new[] { RoomType.Storage, RoomType.Cargo, RoomType.Workshop, RoomType.Hydroponics }
                .SelectMany(t => w.Ship.LiveRooms.Where(r => r.Kind == t)).FirstOrDefault();
            string holeRes = "대상 없음";
            int dsort2 = 0, compHole = 0;
            bool sealedOk = true;
            if (outer != null)
            {
                int dsB = w.Drones.Sorties;
                var target = Scenarios.OuterTarget(w, outer);
                Player.Meteor(w, target, 1f);
                long t0 = w.Tick, hitAt = -1, shutAt = -1;
                for (int t = 0; t < SimTime.Hours(14) && shutAt < 0; t += SimTime.Minutes(5))
                {
                    Run(w, SimTime.Minutes(5));
                    int open = w.Ship.Walls.Count(kv => Hull.EffectiveBreach(kv.Value) > 0.05f && Cell.Dirs4.Any(d => w.Ship.RoomAt(kv.Key + d) is Room rr && !rr.Detached)); // 방으로 새는 구멍만 (방에 안 닿은 모서리 긁힘은 빼고)
                    if (hitAt < 0 && open > 0) hitAt = w.Tick;
                    if (hitAt >= 0 && open == 0) shutAt = w.Tick;
                }
                dsort2 = w.Drones.Sorties - dsB;
                if (args.Contains("--holedebug") && shutAt < 0)
                {
                    foreach (var kv in w.Ship.Walls.Where(kv => Hull.EffectiveBreach(kv.Value) > 0.05f))
                        Console.WriteLine($"     열린 벽 {kv.Key} · 구멍 {kv.Value.Breach:0.00} · 뼈대 잃음 {kv.Value.FrameLost} · 땜 {kv.Value.Patched} · 방 {string.Join("/", Cell.Dirs4.Select(d => w.Ship.RoomAt(kv.Key + d)?.Name).Where(n => n != null).Distinct())}");
                    foreach (var o in w.Board.Open.Where(o => o.Kind.ToString() is var k && (k.Contains("Hull") || k.Contains("Breach") || k.Contains("Weld") || k.Contains("Patch") || k.Contains("Eva"))))
                        Console.WriteLine($"     일감 {o.Kind} · {o.Detail} · 사람 {(o.Assignee?.Name ?? "-")} · 드론 {(o.Drone?.Name ?? "-")} · 막힘 {(o.BlockedUntil > w.Tick ? "예" : "아니오")}");
                    foreach (var d in w.Drones.Drones) Console.WriteLine($"     드론 {d.Name} {d.KindName} {d.State} · {d.Doing} · 배터리 {d.Battery:0.00}");
                    foreach (var e in w.Log.Entries.Where(e => e.Tick >= t0).Where(e => e.Text.Contains("구멍") || e.Text.Contains("외벽") || e.Text.Contains("용접") || e.Text.Contains("드론")).TakeLast(15)) Console.WriteLine($"     {SimTime.Clock(e.Tick)} {e.Text}");
                }
                compHole = w.Log.Entries.Where(e => e.Tick >= t0).Count(e => e.Text.Contains("주 컴퓨터") || e.Text.Contains(a.Voice.Call) || e.Text.Contains("[방송]"));
                sealedOk = hitAt < 0 || shutAt > 0;
                holeRes = hitAt < 0 ? "구멍 안 남" : shutAt > 0 ? $"{(shutAt - hitAt) / (float)SimTime.Minutes(1):0}분에 막음" : "14시간 넘게 열림";
            }
            int dead = w.Crew.Count(c => c.Dead);

            Console.WriteLine($"{name,-6} 로봇 {robots,2} · 드론 {drones,2} · 주컴퓨터 {(online0 ? "켜짐" : "없음/꺼짐")}{(offlineMin > 0 ? $"(꺼진 {offlineMin}분)" : "")} | " +
                              $"하루 로봇 일 {rjobs,3} · 멈춰 선 로봇 {stuck.Count} · 고장 {faulty} · 드론 출격 {dsort,2}/일 {djobs,2} · 드론 고장 {dfaulty} | " +
                              $"불 {fireRes} (컴퓨터 {compFire} · 로봇 {fought}) | 운석 {holeRes} (드론 {dsort2} · 컴퓨터 {compHole}) · 사망 {dead}");
            foreach (var s in stuck.Values) Console.WriteLine($"     멈춤: {s}");
            void P(bool bad, string what) { if (bad) problems.Add($"{name}: {what}"); }
            P(!online0, "주컴퓨터 없음/꺼짐");
            P(robots == 0, "로봇 없음");
            P(drones == 0, "드론 없음");
            P(robots > 0 && rjobs == 0, "로봇이 하루 동안 한 일이 없다");
            P(stuck.Count > 0, $"멈춰 선 로봇 {stuck.Count}");
            P(!fireOut, "불이 안 꺼진다");
            P(galley != null && compFire == 0, "불에 주컴퓨터가 아무 말이 없다");
            P(!sealedOk, "외벽 구멍이 안 막힌다");
            P(outer != null && compHole == 0, "운석에 주컴퓨터가 아무 말이 없다");
        }
        Console.WriteLine(problems.Count == 0 ? "\n✔ 문제 없음" : $"\n✘ 문제 {problems.Count}\n  " + string.Join("\n  ", problems));
        return problems.Count == 0 ? 0 : 1;
    }
}
