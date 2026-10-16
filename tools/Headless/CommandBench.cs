using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v23 주컴퓨터 개편 0단계: 측정 장면 (--cmdbench[=장면]) — 개편 전후로 같은 조건에서 견준다.
//   한밤(첫날이 끝난 자정 · 다들 자는 때)에 사고를 일으키고 2시간을 본다.
//   불 · 파공 · 부상자 · 동시 다발 · 센서 고장 중 불 · 주컴퓨터 정지 중 불.
//   탐지(작업 목록 · 주컴퓨터 믿음 · 호출) · 첫 도착(그 일을 맡은 손이 그 자리에) · 해결 · 사람 피해 · 대응한 손 · 곁에서 평소 일을 한 로봇.
public static partial class Program
{
    private sealed class BenchCase
    {
        public string Name = "";
        public Room? Room;
        public CrewMember? Victim;
        public Cell? Wall;
        public long Start, Detect = -1, Arrive = -1, Resolve = -1;
        public int PeakFire, PeakHands;
        public readonly HashSet<int> Idle = new();
        public bool Fire => Name.Contains("불");
        public bool Breach => Name.Contains("파공");
        public bool Hurt => Name.Contains("부상");
    }

    private static bool CrisisKind(WorkKind k) => k is WorkKind.Extinguish or WorkKind.SealBreach or WorkKind.RepairHull or WorkKind.Rescue or WorkKind.Treat
        or WorkKind.SealOffRoom or WorkKind.OperateDamper or WorkKind.CloseValve or WorkKind.ResetBreaker or WorkKind.IsolatePower or WorkKind.WeldBulkhead;

    private static int RunCommandBench(string[] args)
    {
        string only = args.FirstOrDefault(a => a.StartsWith("--cmdbench="))?.Split('=', 2)[1] ?? "";
        var ships = (Environment.GetEnvironmentVariable("CB_SHIPS") ?? "Hanbit,Mirinae").Split(',');
        var seeds = (Environment.GetEnvironmentVariable("CB_SEEDS") ?? "3,7,11").Split(',').Select(int.Parse).ToArray();
        string[] scenes = { "불", "파공", "부상", "동시", "센서 고장 + 불", "주컴퓨터 정지 + 불" };
        Console.WriteLine("주컴퓨터 대응 측정 (v23 0단계) — 한밤 사고 · 2시간\n");
        Console.WriteLine("장면 | 배 | 시드 | 사고 | 탐지 | 첫 도착 | 해결 | 사람 피해 | 사망 | 대응 최대 | 곁의 평소 일 로봇 | 최대 불칸");
        var sum = new Dictionary<string, List<double[]>>();
        foreach (var scene in scenes.Where(s => only == "" || s.Contains(only)))
            foreach (var ship in ships)
                foreach (int seed in seeds)
                {
                    var w = DayOne(seed, ship);
                    var cases = Inject(w, scene);
                    float h0 = w.Crew.Where(c => !c.Dead).Sum(c => c.Vitals.Health);
                    int dead0 = w.Crew.Count(c => c.Dead);
                    for (int m = 0; m < 120; m++)
                    {
                        for (int t = 0; t < SimTime.Minutes(1); t++) { w.Step(); foreach (var bc in cases) Watch(w, bc); }
                        foreach (var bc in cases) Minute(w, bc);
                        if (Environment.GetEnvironmentVariable("CB_DEBUG") == "1" && m % 3 == 0)
                            foreach (var bc in cases)
                            {
                                var room = bc.Room!;
                                Console.WriteLine($"  {m}분 {bc.Name} {room.Name}: 불 {w.Fire.CountIn(room)} · 연기 {room.Air.Smoke:0.00} · 산소 {room.Air.O2:0.0} · 기압 {room.Air.Pressure:0} · 믿음 불 {w.Automation.Belief.Of(room).Fire}");
                                foreach (var r in w.Robots.Robots.Where(r => (r.Room == room || r.CaseId >= 0) && !r.Wrecked)) Console.WriteLine($"     로봇 {r.Name}@{r.Room?.Name} {r.State} {r.Doing} · 일 {r.Order?.Kind} · 건 {r.CaseId} · 전지 {r.Battery * 100:0}% · 짐 {r.Cargo} · 생각 {r.Mind.Why}");
                                foreach (var c in w.Crew.Where(c => c.Room == room)) Console.WriteLine($"     사람 {c.Name} {c.Job?.Label} · 일 {c.Job?.Order?.Kind}");
                                foreach (var o in w.Board.OpenUnsorted.Where(o => o.Target.Room == room)) Console.WriteLine($"     [일] {o.Title} {o.Kind} 맡음 {o.Assignee?.Name}{o.Robot?.Name}{o.Drone?.Name} 보류 {o.BlockedReason}");
                            }
                    }
                    if (Environment.GetEnvironmentVariable("CB_DEBUG") == "1")
                    {
                        foreach (var e in w.Log.Entries.Where(e => e.Text.Contains("지휘 건") || e.Text.Contains("지휘를"))) Console.WriteLine($"   [지휘] {SimTime.Clock(e.Tick)} {e.Text}");
                        Console.WriteLine($"   {w.Commander.Stats.Line()}");
                    }
                    float harm = h0 - w.Crew.Where(c => !c.Dead).Sum(c => c.Vitals.Health);
                    int dead = w.Crew.Count(c => c.Dead) - dead0;
                    foreach (var bc in cases)
                    {
                        string M(long t) => t < 0 ? "—" : $"{(t - bc.Start) / (float)SimTime.Minutes(1):0}분";
                        Console.WriteLine($"{scene} | {ship} | {seed} | {bc.Name} | {M(bc.Detect)} | {M(bc.Arrive)} | {M(bc.Resolve)} | {harm * 100:0}% | {dead} | {bc.PeakHands} | {bc.Idle.Count} | {bc.PeakFire}");
                        string key = $"{scene} · {bc.Name}";
                        if (!sum.TryGetValue(key, out var l)) sum[key] = l = new();
                        double Mn(long t) => t < 0 ? 120 : (t - bc.Start) / (double)SimTime.Minutes(1);
                        l.Add(new[] { Mn(bc.Detect), Mn(bc.Arrive), Mn(bc.Resolve), harm * 100, dead, bc.PeakHands, bc.Idle.Count, bc.Resolve < 0 ? 1 : 0 });
                    }
                }
        Console.WriteLine("\n요약 (평균 · 못 한 것은 120분으로 셈)");
        Console.WriteLine("사고 | 탐지 | 첫 도착 | 해결 | 사람 피해 | 사망 | 대응 최대 | 곁의 평소 일 로봇 | 못 끝냄");
        foreach (var (k, l) in sum)
        {
            double A(int i) => l.Average(x => x[i]);
            Console.WriteLine($"{k} | {A(0):0.0}분 | {A(1):0.0}분 | {A(2):0.0}분 | {A(3):0}% | {l.Sum(x => x[4]):0} | {A(5):0.0} | {A(6):0.0} | {l.Sum(x => x[7]):0}/{l.Count}");
        }
        return 0;
    }

    private static List<BenchCase> Inject(World w, string scene)
    {
        var list = new List<BenchCase>();
        var ship = w.Ship;
        // 사람이 없는 방 (사람 곁이면 사람이 먼저 본다 — 주컴퓨터 · 로봇이 찾아내는지를 본다)
        Room Quiet(params RoomType[] prefer)
        {
            foreach (var t in prefer)
                foreach (var r in ship.RoomsOf(t))
                    if (!r.Detached && w.Crew.All(c => c.Room != r)) return r;
            return ship.LiveRooms.Where(r => w.Crew.All(c => c.Room != r) && r.Cells.Count(ship.IsOpenFloor) >= 6).OrderBy(r => r.Id).First();
        }
        void Fire()
        {
            var room = Quiet(RoomType.Storage, RoomType.Workshop);
            foreach (var c in room.Cells.Where(ship.IsOpenFloor).Take(5)) w.Fire.Ignite(c, 0.9f);
            list.Add(new BenchCase { Name = "불", Room = room, Start = w.Tick });
        }
        void Breach()
        {
            var room = ship.LiveRooms.Where(r => ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r) && w.Crew.All(c => c.Room != r)).OrderBy(r => r.Id).First();
            var wall = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == room).Select(kv => kv.Key).OrderBy(c => c.X).ThenBy(c => c.Y).First();
            Hull.Damage(ship, wall, 1.2f); // 뚫린 구멍 (공기가 샌다)
            list.Add(new BenchCase { Name = "파공", Room = room, Wall = wall, Start = w.Tick });
        }
        void Hurt()
        {
            var v = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Outside && c.Room != null).OrderByDescending(c => w.Crew.Count(o => o != c && o.Room == c.Room) == 0).ThenBy(c => c.Id).First();
            w.Casualty.Inflict(v, TraumaKind.Bleed, 0.25f, "시험 — 넘어져 크게 베였다");
            list.Add(new BenchCase { Name = "부상", Victim = v, Room = v.Room, Start = w.Tick });
        }
        switch (scene)
        {
            case "불": Fire(); break;
            case "파공": Breach(); break;
            case "부상": Hurt(); break;
            case "동시": Fire(); Breach(); Hurt(); break;
            case "센서 고장 + 불":
            {
                Fire();
                var room = list[0].Room!;
                w.Automation.Belief.Break(room, SensorFault.Stuck, "시험 — 감지기 값 멈춤");
                foreach (var nb in room.Doors.Select(d => d.RoomA == room ? d.RoomB : d.RoomA).Where(r => r != null)) w.Automation.Belief.Break(nb!, SensorFault.Stuck, "시험 — 감지기 값 멈춤");
                break;
            }
            case "주컴퓨터 정지 + 불":
                foreach (var f in ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(f.Machine!, FaultKind.Wrecked);
                Fire();
                break;
        }
        return list;
    }

    /// <summary>한 틱마다: 탐지 · 첫 도착 · 해결.</summary>
    private static void Watch(World w, BenchCase bc)
    {
        var room = bc.Room!;
        bool Related(WorkOrder o) => CrisisKind(o.Kind) && (o.Target.Room == room || bc.Victim != null && o.Target.Crew == bc.Victim);
        if (bc.Detect < 0)
        {
            bool known = w.Board.OpenUnsorted.Any(Related)
                || bc.Fire && w.Automation.Belief.Of(room).Fire
                || bc.Hurt && w.Casualty.Of(bc.Victim!) is { Paged: true };
            if (known) bc.Detect = w.Tick;
        }
        if (bc.Arrive < 0)
        {
            Vector2 at = bc.Victim?.Position ?? room.Center;
            // 다친 사람: 누구든 곁(1.6칸)에 닿으면 (의료 로봇은 작업 목록 일 없이 움직인다) · 불: 그 방에서 불을 끄는 손 · 파공: 봉합하는 손
            bool here = bc.Hurt
                ? w.Robots.Robots.Any(r => !r.Wrecked && r.State == RobotState.Active && Vector2.Distance(r.Position, at) < 1.6f) || w.Crew.Any(c => !c.Dead && c != bc.Victim && !c.Down && Vector2.Distance(c.Position, at) < 1.6f)
                : w.Robots.Robots.Any(r => !r.Wrecked && r.Room == room && r.State == RobotState.Active && (r.FightingFire && bc.Fire || r.Order is WorkOrder o && Related(o)))
                  || w.Drones.Drones.Any(d => d.Order is WorkOrder o && Related(o) && d.State == DroneState.Working)
                  || w.Crew.Any(c => !c.Dead && c.Room == room && c.Job?.Order is WorkOrder o && Related(o));
            if (here) bc.Arrive = w.Tick;
        }
        if (bc.Resolve < 0)
        {
            bool done = bc.Fire ? w.Fire.CountIn(room) == 0
                : bc.Breach ? (w.Ship.WallAt(bc.Wall!.Value) is not WallState ws || Hull.EffectiveBreach(ws) <= 0f) && !w.Board.OpenUnsorted.Any(o => o.Kind == WorkKind.SealBreach && o.Target.Room == room)
                : bc.Victim!.Dead || w.Casualty.Of(bc.Victim) == null;
            if (done) bc.Resolve = w.Tick;
        }
    }

    /// <summary>1분마다: 불 크기 · 대응한 손 · 곁(12칸)에서 평소 일을 한 로봇 (첫 30분).</summary>
    private static void Minute(World w, BenchCase bc)
    {
        var room = bc.Room!;
        if (bc.Fire) bc.PeakFire = Math.Max(bc.PeakFire, w.Ship.LiveRooms.Sum(r => w.Fire.CountIn(r)));
        bool Related(WorkOrder o) => CrisisKind(o.Kind) && (o.Target.Room == room || bc.Victim != null && o.Target.Crew == bc.Victim);
        int hands = w.Robots.Robots.Count(r => r.Order is WorkOrder o && Related(o)) + w.Drones.Drones.Count(d => d.Order is WorkOrder o && Related(o))
            + w.Crew.Count(c => !c.Dead && c.Job?.Order is WorkOrder o && Related(o));
        bc.PeakHands = Math.Max(bc.PeakHands, hands);
        if (bc.Resolve < 0 && w.Tick - bc.Start < SimTime.Minutes(30))
        {
            Vector2 at = bc.Victim?.Position ?? room.Center;
            foreach (var r in w.Robots.Robots)
                if (!r.Wrecked && r.State == RobotState.Active && Vector2.Distance(r.Position, at) < 12f && r.Order is WorkOrder o && !CrisisKind(o.Kind)) bc.Idle.Add(r.Id);
        }
    }
}
