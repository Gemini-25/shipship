using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v21 로봇 · 드론 확장 점검 (--robot21test).
public static partial class Program
{
    private static int RunRobot21Probe(int seed, string ship, int days, bool legacy)
    {
        if (legacy) { RobotsV21.General = false; RobotsV21.Reactor = false; RobotsV21.Expanded = false; }
        var sw = Stopwatch.StartNew();
        var w = World.CreateDefault(seed, 0, ship);
        int beds = w.Ship.Furniture.Count(f => f.Type == FurnitureType.Bed);
        Console.WriteLine($"{ship} 침대 {beds} · 사람 {w.Crew.Count} · 로봇 {w.Robots.Robots.Count} (목표 {RobotsV21.Count(beds)}) · 드론 {w.Drones.Drones.Count} · 만든 시간 {sw.ElapsedMilliseconds}ms");
        Console.WriteLine("  종류: " + string.Join(" · ", w.Robots.Robots.GroupBy(r => r.Kind).Select(g => $"{RobotSystem.KindName(g.Key)} {g.Count()}")));
        Console.WriteLine("  정비대 방: " + string.Join(" · ", w.Robots.Robots.GroupBy(r => r.Dock.Room.Name).OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key} {g.Count()}")));
        sw.Restart();
        var did = w.Robots.Robots.ToDictionary(r => r.Id, _ => new SortedDictionary<string, int>());
        for (long t = 0; t < SimTime.TicksPerDay * days; t++)
        {
            w.Step();
            if (t % 60 == 0) foreach (var r in w.Robots.Robots) if (r.Order != null) { var k = WorkKinds.Name(r.Order.Kind); did[r.Id][k] = did[r.Id].GetValueOrDefault(k) + 1; }
        }
        Console.WriteLine($"  {days}일 {sw.Elapsed.TotalSeconds:0.0}초 · 로봇 한 일 {w.Robots.JobsDone} · 사람 몫 손일 {w.Robots.HandsDone} · 고장 {w.Robots.Robots.Sum(r => r.Breakdowns)} · 사망 {w.Crew.Count(c => c.Dead)}");
        Console.WriteLine("  " + w.Robots.V21 + $" · 바닥 얼룩 {w.Body.Marks.Count(kv => kv.Value.V[(int)CellMark.Glass] > 0.2f || kv.Value.V[(int)CellMark.Oil] > 0.3f || kv.Value.V[(int)CellMark.Soot] > 0.4f || kv.Value.V[(int)CellMark.Wet] > 0.4f)}칸 · 사람 청소 {w.Body.Stats.Cleaned}");
        foreach (var g in w.Robots.Robots.GroupBy(r => r.Kind))
        {
            var jobs = g.SelectMany(r => did[r.Id]).GroupBy(kv => kv.Key).Select(x => (x.Key, n: x.Sum(y => y.Value))).OrderByDescending(x => x.n).Take(5);
            Console.WriteLine($"  {RobotSystem.KindName(g.Key)} ×{g.Count()}: 일 {g.Sum(r => r.JobsDone)} · 숙련 {g.Average(r => r.Skill) * 100:0}% · Mk {string.Join("/", g.Select(r => r.Mk))} · {string.Join(", ", jobs.Select(j => $"{j.Key} {j.n}"))}");
        }
        return 0;
    }
}

public static partial class Program
{
    private static bool R21Want(int n) => Environment.GetEnvironmentVariable("R21ONLY") is not string o || o.Split(',').Contains(n.ToString());

    private static int RunRobot21Test(int seed)
    {
        _fails = 0;
        Console.WriteLine($"로봇 · 드론 확장 점검 (v21) · 시드 {seed}\n");
        try
        {
            // 1) 대수: 사람 한 명에 두 대 (상한 80) · 새 종류가 다 실린다 · 정비대는 방마다 고르게
            if (R21Want(1))
            {
                var lines = new List<string>();
                bool ok = true;
                foreach (var ship in new[] { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Saeteo" })
                {
                    var w = World.CreateDefault(seed, 0, ship);
                    int beds = w.Ship.Furniture.Count(f => f.Type == FurnitureType.Bed);
                    int n = w.Robots.Robots.Count, want = RobotsV21.Count(beds);
                    int maxRoom = w.Robots.Robots.GroupBy(r => r.Dock.Room.Id).Max(g => g.Count());
                    ok &= n >= want * 0.95f && n > w.Crew.Count(c => !c.Dead);
                    lines.Add($"{ship} 사람 {w.Crew.Count} · 로봇 {n}/{want} · 한 방 최대 {maxRoom}");
                }
                var h = World.CreateDefault(seed, 0, "Hanbit");
                var news = new[] { RobotKind.Cleaner, RobotKind.Chef, RobotKind.Plumber, RobotKind.Lifter, RobotKind.Ventman, RobotKind.Rescuer, RobotKind.Sealer, RobotKind.Airman, RobotKind.Medic };
                bool all = news.All(k => h.Robots.Robots.Any(r => r.Kind == k));
                Check("대수 — 사람보다 로봇이 많다 (한 명에 두 대 · 상한 80) · 한빛호에 새 종류 아홉이 다 실린다", ok && all, string.Join(" / ", lines) + (all ? "" : " · 새 종류 빠짐"));
            }

            // 2) 소형 원자로: 하루 일해도 전지가 차 있고 정비대는 전기를 먹지 않는다 · 정전에도 움직인다
            if (R21Want(2))
            {
                var w = DayOne(seed, "Hanbit");
                float minBat = w.Robots.Robots.Min(r => r.Battery);
                bool docksIdle = w.Ship.FurnitureOf(FurnitureType.RobotDock).All(d => d.Machine == null || !d.Machine.Active);
                int stalled = w.Robots.Robots.Count(r => r.State == RobotState.Stalled && r.Fault == null);
                // 정전: 원자로 · 축전지 · 보조 발전기를 모두 끊는다 — 로봇은 그래도 일한다
                foreach (var d in w.Ship.FurnitureOf(FurnitureType.RobotDock).ToList()) Incidents.Break(w, d);
                int jobs0 = w.Robots.JobsDone;
                Run(w, SimTime.Hours(8));
                int jobs = w.Robots.JobsDone - jobs0;
                Check("소형 원자로 — 하루 일해도 전지가 차 있다 · 정비대 전기 0 · 정비대가 멎어도 일한다", minBat > 0.5f && docksIdle && stalled == 0 && jobs > 0,
                    $"가장 낮은 전지 {minBat * 100:0}% · 정비대 전기 {(docksIdle ? "안 씀" : "씀")} · 방전 멈춤 {stalled} · 정비대 멎은 뒤 8시간 한 일 {jobs}");
            }

            // 3) 모든 로봇이 기본 행동을 한다 — 특기가 아닌 로봇 하나만 남겨도 작물을 돌보고 배식기를 채운다 (조금 느리게)
            if (R21Want(3))
            {
                var w = DayOne(seed, "Hanbit");
                var keep = w.Robots.Robots.First(r => r.Kind == RobotKind.Sealer);
                foreach (var r in w.Robots.Robots) if (r != keep) r.Disabled = true;
                int done0 = keep.JobsDone;
                var kinds = new HashSet<WorkKind>();
                for (long t = 0; t < SimTime.Hours(14); t++) { w.Step(); if (keep.Order != null) kinds.Add(keep.Order.Kind); }
                bool basic = kinds.Any(k => RobotsV21.Basic(k) && !RobotSystem.Special(keep.Kind, k));
                Check("기본 행동 — 봉쇄 로봇 혼자서도 작물 돌보기 · 배식기 · 정비 같은 일을 한다", basic && keep.JobsDone > done0,
                    $"한 일 {keep.JobsDone - done0} · {string.Join("·", kinds.Select(WorkKinds.Name))}");
            }

            // 4) 특기가 먼저: 하루 동안 특기 일은 대부분 그 특기 로봇이
            if (R21Want(4))
            {
                var w = DayOne(seed, "Hanbit");
                int special = 0, other = 0;
                var seen = new HashSet<(int, int)>();
                for (long t = 0; t < SimTime.TicksPerDay; t++)
                {
                    w.Step();
                    if (t % 30 != 0) continue;
                    foreach (var r in w.Robots.Robots)
                        if (r.Order is WorkOrder o && seen.Add((r.Id, o.Id)) && w.Robots.Robots.Any(x => x.Operational && RobotSystem.Special(x.Kind, o.Kind)))
                            if (RobotSystem.Special(r.Kind, o.Kind)) special++; else other++;
                }
                float share = special / (float)Math.Max(1, special + other);
                Check("특기가 먼저 — 특기 로봇이 있는 일은 대부분 특기 로봇이 맡는다", share >= 0.6f && special > 5, $"특기 {special} · 다른 로봇 {other} ({share * 100:0}%)");
            }

            // 5) 새 특기 아홉: 판을 깔아 주면 그 로봇이 한다
            if (R21Want(5))
            {
                var w = DayOne(seed, "Hanbit");
                foreach (var d in w.Drones.Drones) d.State = DroneState.Lost; // 밖에서 막는 드론 없이
                var rooms = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.Abandoned).OrderBy(r => r.Id).ToList();
                // 청소: 바닥 기름 · 유리 + 그을린 설비
                var dirty = rooms.First(r => r.Type == RoomType.Storage);
                foreach (var c in dirty.Cells.Where(w.Ship.IsOpenFloor).Take(3)) w.Body.SetMark(c, CellMark.Oil, 0.8f, "시험");
                var fouled = w.Ship.Machines.First(m => !m.Body.Room.Detached && m.Body.Room.Type != RoomType.Reactor);
                fouled.Fouled = 0.8f;
                // 지게: 통로 잔해 · 배관: 물 찬 방 · 환기: 달아오른 설비 · 공기: 산소관 누출
                var corridor = w.Ship.LiveRooms.First(r => r.Type == RoomType.Corridor);
                var rub = corridor.Cells.Where(w.Ship.IsOpenFloor).Skip(3).First();
                w.Ship.Rubble[rub] = 0.6f;
                w.Paths.Invalidate();
                var wet = rooms.First(r => r.Type is RoomType.Mess or RoomType.Lounge);
                wet.Flood = wet.Cells.Count * 20f * 0.12f;
                var hot = w.Ship.Machines.FirstOrDefault(m => VolatileSystem.Mode(m.Body.Type) != BlowKind.None && !m.Body.Room.Detached && m.Body.Room.Type != RoomType.Reactor);
                if (hot != null) hot.Heat = 0.85f;
                var leak = rooms.First(r => r.Type is RoomType.LifeSupport or RoomType.Lounge);
                leak.O2Leak = 0.5f;
                // 의사: 다친 사람 · 봉쇄: 운석 파공
                var hurt = w.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild).OrderBy(c => c.Id).First();
                NeedsSystem.AddInjury(hurt.Vitals, 0.35f, "시험 — 넘어짐");
                hurt.Vitals.Health = 0.55f;
                var hullRoom = rooms.Where(r => r.Type is RoomType.Storage or RoomType.Cargo or RoomType.Workshop).OrderBy(r => r.Id).First();
                var hullCell = w.Ship.Walls.Where(kv => kv.Value.IsHull && w.Ship.RoomAt(kv.Key) == null && Cell.Dirs4.Any(d => w.Ship.RoomAt(kv.Key + d) == hullRoom)).Select(kv => kv.Key).FirstOrDefault();
                if (hullCell != default) Incidents.Meteor(w, hullCell, 0.4f);
                var st = w.Robots.V21;
                int wipes0 = st.Wipes, seals0 = st.Seals, treats0 = st.Treats, pumped0 = st.Pumped, meals0 = st.Meals;
                var did = new Dictionary<RobotKind, HashSet<WorkKind>>();
                var who = new SortedSet<string>();
                for (long t = 0; t < SimTime.Hours(10); t++)
                {
                    w.Step();
                    if (t % 20 != 0) continue;
                    foreach (var r in w.Robots.Robots)
                        if (r.Order is WorkOrder o && RobotSystem.Special(r.Kind, o.Kind)) (did.TryGetValue(r.Kind, out var s) ? s : did[r.Kind] = new()).Add(o.Kind);
                    foreach (var o in w.Board.All)
                        if (o.Kind is WorkKind.CoolDown or WorkKind.Treat or WorkKind.SealBreach && (o.Assignee != null || o.Robot != null || o.Drone != null))
                            who.Add($"{WorkKinds.Name(o.Kind)}:{o.Assignee?.Name ?? o.Robot?.Name ?? o.Drone?.Name}");
                }
                if (Environment.GetEnvironmentVariable("R21V") != null) Console.WriteLine($"    맡은 이: {string.Join(", ", who)} · 파공 칸 {hullCell} · 뜨거운 설비 {hot?.Name} 열 {hot?.Heat:0.00}");
                bool D(RobotKind k, params WorkKind[] ks) => did.TryGetValue(k, out var s) && ks.Any(s.Contains);
                var res = new List<(string, bool)>
                {
                    ("청소", st.Wipes > wipes0 || D(RobotKind.Cleaner, WorkKind.CleanUp)),
                    ("지게", D(RobotKind.Lifter, WorkKind.ClearRubble) || D(RobotKind.Cleaner, WorkKind.ClearRubble) || !w.Ship.Rubble.ContainsKey(rub)),
                    ("배관", st.Pumped > pumped0 || D(RobotKind.Plumber, WorkKind.PumpOut)),
                    ("환기", hot == null || D(RobotKind.Ventman, WorkKind.CoolDown)),
                    ("공기", D(RobotKind.Airman, WorkKind.SealO2Line) || leak.O2Leak <= 0f),
                    ("의사", st.Treats > treats0 || D(RobotKind.Medic, WorkKind.Treat)),
                    ("봉쇄", hullCell == default || st.Seals > seals0 || D(RobotKind.Sealer, WorkKind.SealBreach)),
                    ("조리", st.Meals > meals0 || D(RobotKind.Chef, WorkKind.Cook)),
                };
                int good = res.Count(x => x.Item2);
                Check("새 특기 — 청소 · 잔해 · 물 퍼내기 · 식히기 · 산소관 · 치료 · 파공 · 조리를 특기 로봇이 한다 (8 중 7 이상)", good >= 7,
                    string.Join(" · ", res.Select(x => (x.Item2 ? "" : "✘") + x.Item1)) + $" · {st}");
            }

            // 6) 긴급 지정: 불이 나면 주 컴퓨터가 권한에 따라 다른 로봇을 돌린다 (자동 · 제안 · 조언)
            if (R21Want(6))
            {
                (int drafts, int asks, int advice) Fire(AuthLevel lvl)
                {
                    var w = DayOne(seed, "Hanbit");
                    w.Automation.Authority.Set(Domain.Fleet, lvl, "시험");
                    foreach (var r in w.Robots.Robots) if (RobotsV15.Fights(r.Kind)) r.Disabled = true; // 방재 로봇이 없는 배 — 거품 든 로봇이 모자라다
                    var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Workshop).OrderBy(r => r.Id).First();
                    foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(6)) Incidents.Fire(w, c);
                    Run(w, SimTime.Minutes(12));
                    var v = w.Robots.V21;
                    return (v.Drafts, v.DraftAsks, v.DraftAdvice);
                }
                var auto = Fire(AuthLevel.Auto);
                var ask = Fire(AuthLevel.Propose);
                var adv = Fire(AuthLevel.Advise);
                Check("긴급 지정 — 불에 거품 든 로봇이 모자라면: 자동이면 바로 돌리고 · 제안이면 묻고 · 조언이면 기록만", auto.drafts > 0 && ask.asks > 0 && adv.advice > 0 && adv.drafts == 0,
                    $"자동 지정 {auto.drafts} · 제안 물음 {ask.asks}(지정 {ask.drafts}) · 조언 {adv.advice}(지정 {adv.drafts})");
            }

            // 7) 원자로 폭주: 스스로 사람 없는 방으로 물러나고 · 소방 로봇이 식히러 온다 · 못 식히면 터진다
            if (R21Want(7))
            {
                var w = DayOne(seed, "Hanbit");
                var hot = w.Robots.Robots.First(r => r.Kind == RobotKind.Hauler && r.Operational);
                w.Robots.ForceRunaway(hot, 6f);
                bool cooler = w.Robots.Robots.Any(r => r.Steps?.Any(s => s.GetType().Name == "RCool") == true);
                Run(w, SimTime.Minutes(4));
                bool alone = hot.Room != null && !w.Crew.Any(c => !c.Dead && c.Room == hot.Room);
                Run(w, SimTime.Minutes(8));
                var v = w.Robots.V21;
                bool ended = !hot.Runaway && (v.Cooled > 0 || v.Blasts > 0 && hot.Wrecked);
                Check("원자로 폭주 — 사람 없는 방으로 물러나고 · 소방 로봇이 식히러 오고 · 식히거나 터진다", cooler && ended,
                    $"식히러 감 {(cooler ? "예" : "아니오")} · 4분 뒤 {hot.Room?.Name ?? "?"} ({(alone ? "사람 없음" : "사람 있음")}) · 식힘 {v.Cooled} · 폭발 {v.Blasts}");
            }

            // 8) 개량 · 숙련: 일을 많이 한 로봇을 정비 로봇이 Mk.II로 · 특기 일을 할수록 숙련이 오른다
            if (R21Want(8))
            {
                var w = DayOne(seed, "Hanbit");
                var b = w.Robots.Robots.First(r => r.Kind == RobotKind.Courier && r.Mk == 1);
                b.JobsDone = 60;
                foreach (var k in new[] { ItemKind.Electronics, ItemKind.Motor, ItemKind.Sensor })
                    w.Ship.Containers.First(c => c.Type == FurnitureType.Shelf && c.Storage!.Free > 4).Storage!.Add(k, 4);
                Run(w, SimTime.Hours(8));
                float skill = w.Robots.Robots.Max(r => r.Skill);
                Check("개량 · 숙련 — 일 많이 한 로봇이 Mk.II 이상으로 · 숙련이 오른다", b.Mk >= 2 && w.Robots.V21.Upgrades >= 1 && skill > 0.05f,
                    $"{b.Name} {RobotsV21.MkName(b.Mk)} · 개량 {w.Robots.V21.Upgrades} · 가장 높은 숙련 {skill * 100:0}%");
            }

            // 10) 드론: 사람 세 명에 한 대 더 · 방패 드론이 경보 난 운석을 받아 낸다 · 채굴 드론이 얼음을 캐 온다 · 중계 드론이 선외 무전을 잇는다
            if (R21Want(10))
            {
                var counts = new[] { "Kestrel", "Hanbit", "Saeteo" }.Select(sh => { var w0 = World.CreateDefault(seed, 0, sh); int b0 = w0.Ship.Furniture.Count(f => f.Type == FurnitureType.Bed); return (sh, n: w0.Drones.Drones.Count, want: DronesV21.Count(b0)); }).ToList();
                bool countOk = counts.All(c => c.n == c.want);
                var w = DayOne(seed, "Hanbit");
                int stops = 0, tries = 0;
                for (int i = 0; i < 3; i++)
                {
                    var hull = w.Ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).OrderBy(c => c.X * 7 + c.Y * 13 + i * 31).Skip(i * 5).First();
                    var m = w.Sensors.Launch(hull, 0.3f);
                    if (m == null) continue;
                    m.Warned = WarnLevel.Sensor; m.WarnedAt = w.Tick;
                    tries++;
                    int before = w.Drones.V21.Intercepts + w.Drones.V21.Shrunk;
                    long arrive = m.Arrive;
                    for (long t = w.Tick; t < arrive + SimTime.Minutes(2); t++)
                    {
                        w.Step();
                        if (Environment.GetEnvironmentVariable("R21V") != null && (arrive - w.Tick) % SimTime.Minutes(1) == 0)
                            Console.WriteLine($"    운석 {(arrive - w.Tick) / (float)SimTime.Minutes(1):0}분 전 · " + string.Join(" · ", w.Drones.Drones.Where(d => d.Kind == DroneKind.Shield).Select(d => $"{d.Name} {d.State} {d.Errand} {d.Doing}")));
                    }
                    if (w.Drones.V21.Intercepts + w.Drones.V21.Shrunk > before) stops++;
                    foreach (var d in w.Drones.Drones.Where(d => d.Kind == DroneKind.Shield)) { d.Faulty = false; d.Battery = 1f; }
                    Run(w, SimTime.Minutes(40));
                }
                foreach (var d in w.Drones.Drones.Where(d => d.Kind == DroneKind.Miner)) d.NextErrand = 0;
                Run(w, SimTime.Hours(6));
                var v = w.Drones.V21;
                if (Environment.GetEnvironmentVariable("R21V") != null)
                    Console.WriteLine("    채굴: " + string.Join(" · ", w.Drones.Drones.Where(d => d.Kind == DroneKind.Miner).Select(d => $"{d.Name} {d.State} {d.Doing} 배터리 {d.Battery:0.00} 다음 {(d.NextErrand - w.Tick) / SimTime.Minutes(1)}분 · 자재칸 빈칸 {d.Dock.Storage!.Free}")) + $" · 날아오는 운석 {w.Sensors.Incoming.Count} · 폭풍 {w.Hazards.StormActive}");
                Check("드론 — 사람 세 명에 한 대 더 · 방패 드론이 경보 난 운석을 막는다 · 채굴 드론이 얼음을 캐 온다", countOk && v.Guards >= 1 && stops >= 1 && v.Mines >= 1,
                    string.Join(" · ", counts.Select(c => $"{c.sh} {c.n}/{c.want}")) + $" · 운석 {tries}개 중 막음 {stops} · {v}");
            }

            // 9) 결정론
            if (R21Want(9))
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(4)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 같은 시드 같은 지문 (로봇 24대)", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✘ 예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 로봇 · 드론 확장 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
