using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

public enum PipeKind { Coolant, Water }

/// <summary>배관 구간의 역할 (v9).</summary>
public enum PipeRole
{
    HotLeg,     // 원자로 → 냉각실 분기점 (고온관, 하나뿐)
    Branch,     // 분기점 → 냉각 펌프 → 방열판 (펌프마다 하나, 나란히)
    ColdLeg,    // 방열판 → 원자로 (귀환관, 하나뿐, 선체 위쪽 벽을 따라)
    WaterMain,  // 정수기 → 수경재배실 (급수 본관)
    WaterFeed,  // 정수기 → 냉각실 보충구 (냉각수 보충관)
}

/// <summary>
/// 배관 한 구간 (v9). 벽과 바닥 밑을 지나는 선이라 칸을 막지 않지만, 운석·방 분리에 상하고,
/// 상하면 새고(냉각수는 뜨거운 증기로), 심하면 끊어져 흐르지 않는다.
/// 구간마다 격리 밸브가 있어 잠그면 새는 것은 멈추지만 그 구간으로는 흐르지 않는다.
/// </summary>
public sealed class PipeSegment
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public PipeKind Kind { get; init; }
    public PipeRole Role { get; init; }

    /// <summary>냉각 분기면 몇 번 펌프의 것인지 (0부터).</summary>
    public int Branch { get; init; } = -1;
    public Furniture? Pump { get; init; }

    /// <summary>지나는 칸 (벽·바닥). 그림과 운석 피해에 쓴다.</summary>
    public List<Cell> Path { get; } = new();

    /// <summary>선체 바깥의 방열판 칸 (냉각 분기만).</summary>
    public List<Cell> Radiator { get; } = new();

    /// <summary>격리 밸브 자리 (사람이 서서 돌리는 바닥 칸).</summary>
    public Cell ValveCell { get; set; }
    public Room? ValveRoom { get; set; }

    /// <summary>밸브를 잠갔다 (흐르지 않고 새지도 않는다).</summary>
    public bool Closed { get; set; }
    public long ClosedSince { get; set; }

    /// <summary>승무원이 본관을 잠그기로 정했다 (원자로를 세운다).</summary>
    public bool IsolateApproved { get; set; }

    /// <summary>고칠 재료가 없어 새는 채로 다시 열기로 정했다 (냉각수를 부어 가며 원자로를 돌린다).</summary>
    public bool LimpApproved { get; set; }

    /// <summary>임시 밀봉한 관을 새 관으로 갈기로 했다 (분기는 잠시 잠그고, 본관은 회의로 원자로를 세우고). v9.2</summary>
    public bool PlannedReplace { get; set; }

    /// <summary>임시 밀봉이 압력에 다시 터진 횟수.</summary>
    public int PatchFails { get; set; }

    /// <summary>관의 상태 0~1. 0.75 아래면 새고, 0.25 아래면 끊어져 흐르지 않는다.</summary>
    public float Integrity { get; set; } = 1f;

    /// <summary>갈아 끼울 때마다 조금씩 떨어진다 (땜질 ≠ 새것).</summary>
    public float MaxIntegrity { get; set; } = 1f;

    /// <summary>방열판 상태 0~1 (냉각 분기만). 밖에서 고친다.</summary>
    public float RadiatorCondition { get; set; } = 1f;

    /// <summary>실링폼 클램프로 임시로 막았다 (새지 않지만 압력에 다시 터질 수 있다).</summary>
    public bool Patched { get; set; }
    public float PatchQuality { get; set; }
    internal float BeforePatch { get; set; }

    /// <summary>터진 자리.</summary>
    public Cell LeakAt { get; set; }

    /// <summary>우회 배관 (임시, 0이면 없음): 끊어진 구간을 돌아 흐르게 한다. 흐름은 그만큼만.</summary>
    public float Bypass { get; set; }
    public long BypassSince { get; set; }

    public int Breaks { get; set; }
    public int Repairs { get; set; }
    public int Patches { get; set; }
    public int Bypasses { get; set; }
    public List<Mark> Marks { get; } = new();

    public bool IsCoolant => Kind == PipeKind.Coolant;

    /// <summary>끊어졌다 (흐르지 않는다, 크게 샌다).</summary>
    public bool Severed => Integrity < 0.25f && !Patched;

    /// <summary>제대로 된 관이면 새지 않는다.</summary>
    public bool Sound => Integrity >= 0.75f || Patched;

    /// <summary>압력이 걸렸을 때 새는 양 (L/시간).</summary>
    public float LeakRate
    {
        get
        {
            if (Patched || Integrity >= 0.75f) return 0f;
            float full = IsCoolant ? 60f : 30f;
            if (Integrity < 0.25f) return full;
            return full * 0.65f * (0.75f - Integrity) / 0.5f;
        }
    }

    /// <summary>지금 새고 있는지 (밸브를 잠갔으면 안 샌다).</summary>
    public bool Leaking => !Closed && LeakRate > 0.01f;

    /// <summary>이 구간으로 흐를 수 있는 몫 0~1 (우회 배관 포함).</summary>
    public float Flow => !Closed && !Severed ? 1f : Bypass;

    public string StateText =>
        Severed ? (Closed ? "끊어짐 · 잠금" : "끊어짐") :
        Closed ? (Bypass > 0f ? "잠금 · 우회 중" : PlannedReplace ? "잠금 · 교체 중" : "잠금") :
        Patched ? (PlannedReplace ? "임시 밀봉 · 교체 예정" : "임시 밀봉") :
        Integrity < 0.75f ? "샘" :
        Integrity < 0.95f ? "낡음" : MaxIntegrity >= 1.3f ? "보강" : "정상";
}

/// <summary>
/// 배관망 (v9): 냉각 루프와 급수관.
///
/// 원자로 → 고온관 → 냉각실 분기점 → (펌프 #1 → 방열판 #1) ∥ (펌프 #2 → 방열판 #2) → 귀환관 → 원자로.
/// 고온관·귀환관은 하나뿐이라 끊어지면 냉각이 통째로 멈추고, 분기는 둘이라 하나를 잠가도 절반은 돈다.
/// 냉각수는 새면 줄고, 줄면 펌프가 헛돌다(공동 현상) 닳고, 30% 아래면 냉각이 멈춘다 → 원자로 긴급 정지.
/// 냉각수는 정수 탱크의 물로 보충한다 (보충관이 끊기면 물통으로 나른다) → 물 ↔ 정수기(전기) ↔ 원자로(냉각수)의 고리.
/// 비상수단: 끊어진 구간을 잠그고 금속판·실링폼으로 우회 배관을 깐다 (흐름 70%, 흔적으로 남는다).
/// 급수 본관이 끊기면 수경재배대에 물이 가지 않는다.
/// </summary>
public sealed class PipeNetwork
{
    public const float CoolantMax = 120f;
    public static float PerBranchKw = 30f;

    private readonly World _world;
    public List<PipeSegment> Segments { get; } = new();
    public PipeSegment? HotLeg { get; private set; }
    public PipeSegment? ColdLeg { get; private set; }
    public List<PipeSegment> Branches { get; } = new();
    public PipeSegment? WaterMain { get; private set; }
    public PipeSegment? WaterFeed { get; private set; }

    /// <summary>냉각실 보충구 (냉각수를 붓는 자리).</summary>
    public Cell FillPort { get; private set; }

    /// <summary>냉각 루프에 든 냉각수 (L).</summary>
    public float Coolant { get; set; } = CoolantMax;

    public float CoolantFraction => Coolant / CoolantMax;

    /// <summary>냉각수가 모자라면 펌프가 헛돈다: 75% 이상이면 온전히, 30% 아래면 전혀.</summary>
    public float CoolantFactor => Math.Clamp((CoolantFraction - 0.3f) / 0.45f, 0f, 1f);

    public float CoolingKw { get; private set; }
    public int FlowingBranches { get; private set; }
    public bool MainFlow { get; private set; } = true;
    public bool Built => HotLeg != null && ColdLeg != null && Branches.Count > 0;

    public float CoolantLost { get; set; }
    public float WaterLost { get; set; }
    public float CoolantAdded { get; set; }
    public int Bursts { get; set; }
    public int BypassesLaid { get; set; }
    public int Version { get; private set; }

    private readonly HashSet<int> _announced = new();

    public PipeNetwork(World world)
    {
        _world = world;
        Build();
    }

    // ─────────────────────────────── 배치 ───────────────────────────────

    private void Build()
    {
        var ship = _world.Ship;
        var reactor = ship.Furniture.FirstOrDefault(f => f.Type == FurnitureType.ReactorCore);
        var pumps = ship.Furniture.Where(f => f.Type == FurnitureType.CoolantPump).OrderBy(f => f.Cells.Min(c => c.X)).ToList();
        if (reactor == null || pumps.Count == 0) return;
        var cooling = pumps[0].Room;
        int id = 0;
        Cell Mid(Furniture f) => new((f.Cells.Min(c => c.X) + f.Cells.Max(c => c.X)) / 2, (f.Cells.Min(c => c.Y) + f.Cells.Max(c => c.Y)) / 2);
        int rMaxX = reactor.Cells.Max(c => c.X), rMinY = reactor.Cells.Min(c => c.Y), rMaxY = reactor.Cells.Max(c => c.Y);
        var rMid = Mid(reactor);
        int pBottom = pumps.Max(p => p.Cells.Max(c => c.Y));
        int pTop = pumps.Min(p => p.Cells.Min(c => c.Y));
        int manX = pumps.Count > 1 ? (pumps[0].Cells.Max(c => c.X) + pumps[1].Cells.Min(c => c.X) + 1) / 2 : pumps[0].Cells.Max(c => c.X) + 1;
        var manifold = new Cell(manX, Math.Min(cooling.MaxY, pBottom + 2));

        // 고온관: 원자로 오른쪽 → 벽을 지나 → 분기점
        var hot = new PipeSegment { Id = id++, Name = "냉각 고온관", Kind = PipeKind.Coolant, Role = PipeRole.HotLeg };
        hot.Path.AddRange(Route(new Cell(rMaxX + 1, Math.Min(rMaxY, rMid.Y + 1)), manifold));
        hot.ValveCell = NearestFloor(manifold, cooling) ?? manifold;
        hot.ValveRoom = cooling;
        Segments.Add(hot);
        HotLeg = hot;

        // 분기: 분기점 → 펌프 → 위쪽 외벽 → 방열판 (선체 바깥)
        int wallY = pTop - 1;
        for (int i = 0; i < pumps.Count; i++)
        {
            var p = pumps[i];
            var pm = Mid(p);
            var b = new PipeSegment { Id = id++, Name = $"{i + 1}번 냉각 루프", Kind = PipeKind.Coolant, Role = PipeRole.Branch, Branch = i, Pump = p };
            var below = new Cell(pm.X, p.Cells.Max(c => c.Y) + 1);
            b.Path.AddRange(Route(manifold, below));
            for (int y = p.Cells.Max(c => c.Y); y >= wallY; y--) b.Path.Add(new Cell(pm.X, y));
            for (int x = p.Cells.Min(c => c.X) - (i == 0 ? 1 : 0); x <= p.Cells.Max(c => c.X) + (i == pumps.Count - 1 ? 1 : 0); x++)
                b.Radiator.Add(new Cell(x, wallY - 1));
            b.ValveCell = NearestFloor(below, cooling) ?? below;
            b.ValveRoom = cooling;
            Segments.Add(b);
            Branches.Add(b);
        }

        // 귀환관: 방열판 → 위쪽 외벽을 따라 → 원자로 윗면
        var cold = new PipeSegment { Id = id++, Name = "냉각 귀환관", Kind = PipeKind.Coolant, Role = PipeRole.ColdLeg };
        int startX = pumps[0].Cells.Min(c => c.X) - 1;
        for (int x = startX; x >= rMid.X; x--) cold.Path.Add(new Cell(x, wallY));
        for (int y = wallY + 1; y < rMinY; y++) cold.Path.Add(new Cell(rMid.X, y));
        var reactorRoom = reactor.Room;
        cold.ValveCell = NearestFloor(new Cell(rMid.X + 1, rMinY - 1), reactorRoom) ?? new Cell(rMid.X, rMinY - 1);
        cold.ValveRoom = reactorRoom;
        Segments.Add(cold);
        ColdLeg = cold;

        // 급수: 정수기 → 수경재배실, 정수기 → 냉각실 보충구
        var recycler = ship.Furniture.FirstOrDefault(f => f.Type == FurnitureType.WaterRecycler);
        var hydro = ship.Rooms.FirstOrDefault(r => r.Type == RoomType.Hydroponics);
        if (recycler != null && hydro != null)
        {
            var life = recycler.Room;
            int y = recycler.Cells.Max(c => c.Y) + 1;
            var main = new PipeSegment { Id = id++, Name = "급수 본관", Kind = PipeKind.Water, Role = PipeRole.WaterMain };
            main.Path.AddRange(Route(new Cell(recycler.Cells.Max(c => c.X), y), new Cell(hydro.MaxX, y)));
            main.ValveCell = NearestFloor(new Cell(recycler.Cells.Max(c => c.X), y), life) ?? recycler.UseSpots.First();
            main.ValveRoom = life;
            Segments.Add(main);
            WaterMain = main;

            // 보충관: 정수기 위 → 통로 → 냉각실 바닥 (문 칸은 피한다)
            int fx = recycler.Cells.Min(c => c.X);
            var port = FillPortCell(cooling, ship);
            var feed = new PipeSegment { Id = id++, Name = "냉각수 보충관", Kind = PipeKind.Water, Role = PipeRole.WaterFeed };
            int corridorY = cooling.MaxY + 2; // 냉각실 아래 벽 바로 밑 통로
            var up = new List<Cell>();
            for (int yy = recycler.Cells.Min(c => c.Y); yy >= corridorY; yy--) up.Add(new Cell(fx, yy));
            feed.Path.AddRange(up);
            feed.Path.AddRange(Route(new Cell(fx, corridorY), new Cell(port.X, corridorY)).Skip(1));
            for (int yy = corridorY - 1; yy >= port.Y; yy--) feed.Path.Add(new Cell(port.X, yy));
            feed.ValveCell = NearestFloor(new Cell(fx - 1, recycler.Cells.Min(c => c.Y)), life) ?? recycler.UseSpots.First();
            feed.ValveRoom = life;
            Segments.Add(feed);
            WaterFeed = feed;
            FillPort = port;
        }
        else FillPort = NearestFloor(manifold, cooling) ?? manifold;
        foreach (var s in Segments) s.LeakAt = s.Path[s.Path.Count / 2];
    }

    /// <summary>보충구: 냉각실 아래쪽 벽 가까운 바닥 칸 중 문 앞이 아닌 곳.</summary>
    private static Cell FillPortCell(Room cooling, Ship ship)
    {
        var doors = new HashSet<Cell>(ship.Doors.Select(d => d.Cell));
        foreach (var c in cooling.Cells.OrderByDescending(c => c.Y).ThenByDescending(c => c.X))
        {
            if (!ship.IsWalkable(c)) continue;
            if (Cell.Dirs4.Any(d => doors.Contains(c + d))) continue;
            if (doors.Contains(c + new Cell(0, 1))) continue;
            return c;
        }
        return cooling.DamperSpot;
    }

    /// <summary>가로 먼저, 세로 나중의 ㄱ자 길 (양 끝 포함).</summary>
    private static List<Cell> Route(Cell a, Cell b)
    {
        var path = new List<Cell>();
        int x = a.X, y = a.Y;
        path.Add(a);
        while (x != b.X) { x += Math.Sign(b.X - x); path.Add(new Cell(x, y)); }
        while (y != b.Y) { y += Math.Sign(b.Y - y); path.Add(new Cell(x, y)); }
        return path;
    }

    private Cell? NearestFloor(Cell near, Room room)
    {
        var ship = _world.Ship;
        Cell? best = null;
        float bd = float.MaxValue;
        foreach (var c in room.Cells)
        {
            if (!ship.IsWalkable(c)) continue;
            float d = (c.Center - near.Center).LengthSquared();
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    // ─────────────────────────────── 조회 ───────────────────────────────

    /// <summary>그 방을 지나는 구간 (밸브가 그 방에 있거나 길이 그 방을 지난다).</summary>
    public IEnumerable<PipeSegment> In(Room room) =>
        Segments.Where(s => s.ValveRoom == room || s.Path.Any(c => _world.Ship.RoomAt(c) == room));

    /// <summary>그 점에서 가장 가까운 구간 (maxDist 칸 안).</summary>
    public PipeSegment? Nearest(Vector2 p, float maxDist, out Cell at)
    {
        PipeSegment? best = null;
        at = default;
        float bd = maxDist * maxDist;
        foreach (var s in Segments)
            foreach (var c in s.Path.Concat(s.Radiator))
            {
                float d = (c.Center - p).LengthSquared();
                if (d < bd) { bd = d; best = s; at = c; }
            }
        return best;
    }

    /// <summary>이 구간을 잠그면 냉각이 통째로 멈추는지 (본관이거나, 흐르는 분기가 이것뿐).</summary>
    public bool WouldStopCooling(PipeSegment s)
    {
        if (!s.IsCoolant || !Built) return false;
        if (s.Role is PipeRole.HotLeg or PipeRole.ColdLeg) return true;
        return !Branches.Any(b => b != s && b.Flow > 0f && b.Pump?.Machine is Machine m && m.Efficiency > 0.05f && !b.Pump.Room.Detached);
    }

    /// <summary>수경재배실에 물이 가는지.</summary>
    public bool WaterTo(Room room)
    {
        if (!room.WaterLinked && UtilityNet.NeedsWater(room)) return false; // 급수망이 끊겼다 (단수)
        if (WaterMain == null) return true;
        // v10.3: 재배대가 있는 방은 어디든 급수 본관에서 물을 끌어 쓴다 (제2 재배실의 재배대도 본관이 끊기면 마른다)
        if (room.Type != RoomType.Hydroponics && !room.Furniture.Any(f => f.Type == FurnitureType.GrowBed)) return true;
        return WaterMain.Flow > 0f;
    }

    /// <summary>보충관으로 냉각수를 부을 수 있는지 (아니면 물통으로 나른다).</summary>
    public bool FeedLine => WaterFeed == null || WaterFeed.Flow > 0f;

    /// <summary>그 칸 가까이 뜨거운 증기가 뿜어져 나오는지 (0~1).</summary>
    public float SteamAt(Cell c)
    {
        float s = 0f;
        foreach (var seg in Segments)
        {
            if (!seg.IsCoolant || !seg.Leaking || seg.LeakRate < 3f || _pressure < 0.1f) continue;
            float d = (seg.LeakAt.Center - c.Center).Length();
            if (d > 2.2f) continue;
            s = MathF.Max(s, (1f - d / 2.4f) * MathF.Min(1f, seg.LeakRate / 30f) * _pressure);
        }
        return s;
    }

    private float _pressure = 1f;

    /// <summary>냉각수 압력 (펌프가 돌면 1, 멈추면 조금).</summary>
    public float Pressure => _pressure;

    // ─────────────────────────────── 냉각 ───────────────────────────────

    /// <summary>
    /// 냉각 능력 (kW): 고온관·귀환관이 흐르고, 분기마다 펌프 효율 × 흐름 × 방열판, 냉각수가 모자라면 헛돈다.
    /// 배관망이 없는 배(시험용)는 예전처럼 펌프 합.
    /// </summary>
    public float ComputeCooling()
    {
        var ship = _world.Ship;
        if (!Built)
        {
            CoolingKw = ship.FurnitureOf(FurnitureType.CoolantPump).Sum(f => f.Machine!.Efficiency * f.Machine.Rating * PerBranchKw);
            return CoolingKw;
        }
        float main = MathF.Min(HotLeg!.Flow, ColdLeg!.Flow);
        MainFlow = main > 0f;
        float kw = 0f;
        int flowing = 0;
        foreach (var b in Branches)
        {
            if (b.Pump is not Furniture pf || pf.Room.Detached || pf.Machine is not Machine m) continue;
            float f = b.Flow * m.Efficiency;
            if (f <= 0f) continue;
            flowing++;
            kw += f * PerBranchKw * m.Rating * (0.35f + 0.65f * b.RadiatorCondition);
        }
        FlowingBranches = main > 0f ? flowing : 0;
        CoolingKw = kw * main * CoolantFactor * (1f + Modules.Bonus(_world, FurnitureType.HeatExchanger)); // v10.6 열교환 모듈
        return CoolingKw;
    }

    // ─────────────────────────────── 매 시스템 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _world;
        if (Segments.Count == 0) return;
        var ship = w.Ship;

        // 압력: 냉각 펌프가 하나라도 돌면 가득, 아니면 자연 순환만큼
        bool pumping = Branches.Any(b => b.Pump?.Machine is Machine m && m.Efficiency > 0.05f && !b.Pump.Room.Detached);
        _pressure = pumping ? 1f : 0.25f;

        foreach (var s in Segments)
        {
            // 임시 밀봉은 압력에 다시 터질 수 있다 (v9.2 조정: 품질 70%면 평균 엿새, 50%면 사흘, 90%면 두 주쯤)
            float q = 1f - s.PatchQuality;
            if (s.Patched && !s.Closed && w.Rng.Chance((0.002f + 0.03f * q * MathF.Sqrt(q)) * dt * (s.IsCoolant ? _pressure : 0.5f)))
            {
                s.Patched = false;
                s.Integrity = s.BeforePatch;
                s.PatchFails++;
                MarkLog.Add(s.Marks, w.Tick, "임시 밀봉이 터졌다");
                w.RaiseAlert($"{s.Name} 임시 밀봉이 다시 터졌다", ship.RoomAt(s.LeakAt), AlertLevel.Warning, shipWide: s.IsCoolant);
                w.History.Add(w, HistoryKind.Damage, $"{s.Name} 임시 밀봉이 다시 터졌다", ship.RoomAt(s.LeakAt), at: s.LeakAt);
                w.Board.RequestScan();
            }
            // 우회 배관도 오래 버티지는 못한다
            if (s.Bypass > 0f && s.Closed && w.Rng.Chance(0.006f * dt))
            {
                s.Bypass = MathF.Max(0f, s.Bypass - 0.25f);
                MarkLog.Add(s.Marks, w.Tick, s.Bypass > 0f ? "우회 배관 이음매가 샌다" : "우회 배관이 못 쓰게 됐다");
                w.RaiseAlert($"{s.Name} 우회 배관이 {(s.Bypass > 0f ? "새기 시작했다" : "못 쓰게 됐다")}", ship.RoomAt(s.ValveCell), AlertLevel.Warning, shipWide: s.IsCoolant);
                w.Board.RequestScan();
            }

            if (!s.Leaking) continue;
            float rate = s.LeakRate * (s.IsCoolant ? _pressure : (w.Water.Level > 1f ? 1f : 0f));
            if (rate <= 0f) continue;
            float lost = rate * dt;
            var room = ship.RoomAt(s.LeakAt) ?? ship.RoomAt(s.ValveCell);
            if (s.IsCoolant)
            {
                lost = MathF.Min(lost, Coolant);
                Coolant -= lost;
                CoolantLost += lost;
                // 뜨거운 냉각수는 증기가 되어 방을 데운다
                if (room != null && !room.Detached) room.Air.Temperature = MathF.Min(70f, room.Air.Temperature + rate / 40f * 10f * dt);
            }
            else
            {
                lost = MathF.Min(lost, w.Water.Level);
                w.Water.Level -= lost;
                WaterLost += lost;
            }
            if (lost > 0.5f && _announced.Add(s.Id))
            {
                w.RaiseAlert($"{s.Name} {(s.Severed ? "끊어짐" : "누수")} — {(s.IsCoolant ? "냉각수" : "물")}가 샌다 ({s.LeakRate:0}L/시간)",
                    room, s.IsCoolant ? AlertLevel.Critical : AlertLevel.Warning, shipWide: true);
            }
        }
        foreach (var s in Segments) if (!s.Leaking) _announced.Remove(s.Id);

        // 냉각수가 모자라면 펌프가 헛돌며 닳는다 (공동 현상)
        float cf = CoolantFactor;
        if (cf < 1f)
            foreach (var b in Branches)
                if (b.Pump?.Machine is Machine m && m.Efficiency > 0.05f && b.Flow > 0f)
                    m.Wear = MathF.Min(1f, m.Wear + 0.04f * (1f - cf) * dt);

        // 증기: 새는 곳 가까이 있으면 데인다 (우주복을 입었으면 덜)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside) continue;
            float steam = SteamAt(c.Cell);
            if (steam <= 0.05f) continue;
            float dmg = 0.35f * steam * dt * (c.Suit != null ? 0.3f : 1f);
            c.Vitals.Health = MathF.Max(0.05f, c.Vitals.Health - dmg);
            NeedsSystem.AddInjury(c.Vitals, dmg, "증기 화상");
        }
    }

    // ─────────────────────────────── 손상 ───────────────────────────────

    /// <summary>구간이 상했다 (운석 파편, 터짐, 방 분리).</summary>
    public void Damage(PipeSegment s, float dmg, Cell at, string cause)
    {
        var w = _world;
        bool wasSound = s.Sound;
        if (s.Patched) { s.Patched = false; s.Integrity = MathF.Min(s.Integrity, s.BeforePatch); }
        s.Integrity = MathF.Max(0f, s.Integrity - dmg);
        s.LeakAt = at;
        Version++;
        if (s.Integrity < 0.75f && wasSound)
        {
            s.Breaks++;
            Bursts++;
            MarkLog.Add(s.Marks, w.Tick, $"{cause} — {(s.Severed ? "끊어졌다" : "새기 시작했다")} ({s.Integrity * 100:0}%)");
            var room = w.Ship.RoomAt(at) ?? w.Ship.RoomAt(s.ValveCell);
            w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(s.Name)} {(s.Severed ? "끊어졌다" : "터졌다")} ({cause})", room, at: at);
            if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{s.Name} {(s.Severed ? "끊어짐" : "누수")} ({cause})");
        }
        w.Board.RequestScan();
    }

    /// <summary>방열판이 상했다 (선체 밖).</summary>
    public void DamageRadiator(PipeSegment b, float dmg, string cause)
    {
        var w = _world;
        float before = b.RadiatorCondition;
        b.RadiatorCondition = MathF.Max(0f, b.RadiatorCondition - dmg);
        if (before >= 0.7f && b.RadiatorCondition < 0.7f)
        {
            MarkLog.Add(b.Marks, w.Tick, $"방열판 손상 ({cause}, {b.RadiatorCondition * 100:0}%)");
            w.History.Add(w, HistoryKind.Damage, $"{b.Pump?.Label ?? b.Name} 방열판이 부서졌다 ({cause}) — 열을 덜 버린다", b.Pump?.Room, at: b.Radiator[0]);
        }
        Version++;
        w.Board.RequestScan();
    }

    /// <summary>운석: 반경 안의 관과 방열판을 상하게 한다.</summary>
    public void OnImpact(Cell entry, float size)
    {
        var w = _world;
        float radius = 1f + 2.5f * size;
        foreach (var s in Segments)
        {
            Cell? near = null;
            float bd = float.MaxValue;
            foreach (var c in s.Path)
            {
                float d = (c.Center - entry.Center).Length();
                if (d < bd) { bd = d; near = c; }
            }
            if (near is Cell at && bd <= radius)
            {
                float falloff = MathF.Pow(1f - bd / (radius + 0.5f), 1.3f);
                float dmg = MathF.Min(0.85f, (0.25f + 0.6f * size) * falloff * w.Rng.Range(0.6f, 1.1f));
                if (dmg > 0.05f) Damage(s, dmg, at, "운석 파편");
            }
            if (s.Radiator.Count > 0)
            {
                float rd = s.Radiator.Min(c => (c.Center - entry.Center).Length());
                if (rd <= radius + 1f)
                {
                    float falloff = MathF.Pow(1f - rd / (radius + 1.5f), 1.2f);
                    DamageRadiator(s, MathF.Min(0.9f, (0.3f + 0.7f * size) * falloff * w.Rng.Range(0.6f, 1.1f)), "운석");
                }
            }
        }
    }

    /// <summary>방이 떨어져 나갔다: 그 방을 지나는 관은 끊어진다 (사출 준비로 밸브를 잠갔으면 새지 않는다).</summary>
    public void OnDetach(Room room, bool isolated)
    {
        var ship = _world.Ship;
        foreach (var s in Segments)
        {
            var inRoom = s.Path.Where(c => room.Cells.Contains(c)).ToList();
            if (inRoom.Count == 0 && s.ValveRoom != room) continue;
            var at = inRoom.Count > 0 ? inRoom[0] : s.ValveCell;
            if (isolated && !s.Closed) { s.Closed = true; s.ClosedSince = _world.Tick; }
            Damage(s, 1f, at, $"{room.Name} 분리");
        }
    }

    /// <summary>관찰자가 배관을 터뜨린다 (사고 도구).</summary>
    public PipeSegment? Burst(Cell near, float severity)
    {
        var s = Nearest(near.Center, 4f, out var at);
        if (s == null) return null;
        Damage(s, MathF.Min(1f, 0.45f + 0.5f * severity), at, "배관 파손");
        return s;
    }

    // ─────────────────────────────── 고치기 ───────────────────────────────

    public void CloseValve(PipeSegment s, CrewMember? who)
    {
        var w = _world;
        s.Closed = true;
        s.ClosedSince = w.Tick;
        s.IsolateApproved = false;
        s.LimpApproved = false;
        Version++;
        MarkLog.Add(s.Marks, w.Tick, $"{(who != null ? Ko.IGa(who.Name) + " " : "")}밸브를 잠갔다");
        w.Log.Add(w.Tick, LogKind.Work, $"{s.Name} 밸브를 잠갔다" + (s.IsCoolant && (s.Role is PipeRole.HotLeg or PipeRole.ColdLeg) ? " — 냉각 루프가 멈춘다" : ""), who?.Id ?? -1);
        w.Board.RequestScan();
    }

    public void OpenValve(PipeSegment s, CrewMember? who)
    {
        var w = _world;
        s.Closed = false;
        Version++;
        bool limp = s.LimpApproved && !s.Sound;
        MarkLog.Add(s.Marks, w.Tick, $"{(who != null ? Ko.IGa(who.Name) + " " : "")}밸브를 다시 열었다" + (limp ? " (새는 채로)" : ""));
        w.Log.Add(w.Tick, LogKind.Work, $"{s.Name} 밸브를 다시 열었다" + (limp ? " — 새는 채로, 냉각수를 부어 가며 돌린다" : ""), who?.Id ?? -1);
        if (limp) w.History.Add(w, HistoryKind.Adaptation, $"{Ko.EulReul(s.Name)} 새는 채로 다시 열었다 — 고칠 재료가 없어 냉각수를 부어 가며 원자로를 돌린다",
            s.ValveRoom, who != null ? new[] { who } : null, s.ValveCell);
        w.Board.RequestScan();
    }

    public void Patch(PipeSegment s, float skill, CrewMember who)
    {
        var w = _world;
        s.BeforePatch = s.Integrity;
        s.Patched = true;
        s.PatchQuality = Math.Clamp(0.5f + 0.45f * skill + w.Rng.Range(-0.1f, 0.1f), 0.3f, 0.98f);
        s.Patches++;
        Version++;
        MarkLog.Add(s.Marks, w.Tick, $"{Ko.IGa(who.Name)} 실링폼 클램프로 임시 밀봉 (품질 {s.PatchQuality * 100:0}%)");
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(who.Name)} {Ko.EulReul(s.Name)} 실링폼 클램프로 임시로 막았다", w.Ship.RoomAt(s.LeakAt), new[] { who }, s.LeakAt);
        w.Board.RequestScan();
    }

    public void Replace(PipeSegment s, float skill, CrewMember who)
    {
        var w = _world;
        s.MaxIntegrity = MathF.Max(0.6f, s.MaxIntegrity - 0.05f);
        s.Integrity = s.MaxIntegrity * (0.9f + 0.1f * skill);
        bool planned = s.PlannedReplace && s.Patched;
        s.Patched = false;
        s.PlannedReplace = false;
        bool hadBypass = s.Bypass > 0f;
        s.Bypass = 0f;
        s.Repairs++;
        Version++;
        MarkLog.Add(s.Marks, w.Tick, $"{Ko.IGa(who.Name)} 관을 갈아 끼웠다" + (hadBypass ? " (우회 배관 철거)" : planned ? " (임시 밀봉을 걷어 냈다)" : ""));
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(who.Name)} {Ko.EulReul(s.Name)} 새 관으로 갈아 끼웠다" + (hadBypass ? " — 우회 배관을 걷어 냈다" : planned ? " — 임시 밀봉을 걷어 냈다 (계획 교체)" : ""),
            w.Ship.RoomAt(s.LeakAt) ?? s.ValveRoom, new[] { who }, s.LeakAt);
        w.Board.RequestScan();
    }

    public void LayBypass(PipeSegment s, float skill, CrewMember who)
    {
        var w = _world;
        if (!s.Closed) { s.Closed = true; s.ClosedSince = w.Tick; }
        s.Bypass = Math.Clamp(0.6f + 0.15f * skill, 0.5f, 0.75f);
        s.BypassSince = w.Tick;
        s.Bypasses++;
        BypassesLaid++;
        Version++;
        MarkLog.Add(s.Marks, w.Tick, $"{Ko.IGa(who.Name)} 우회 배관을 깔았다 (흐름 {s.Bypass * 100:0}%)");
        w.History.Add(w, HistoryKind.Adaptation, $"{Ko.IGa(who.Name)} 끊어진 {Ko.EulReul(s.Name)} 잠그고 우회 배관을 깔았다 (흐름 {s.Bypass * 100:0}%)",
            s.ValveRoom, new[] { who }, s.ValveCell);
        w.Board.RequestScan();
    }

    public void RepairRadiator(PipeSegment b, float skill, string by)
    {
        var w = _world;
        b.RadiatorCondition = MathF.Max(b.RadiatorCondition, 0.85f + 0.12f * skill);
        Version++;
        MarkLog.Add(b.Marks, w.Tick, $"{by}: 방열판을 고쳤다");
        w.History.Add(w, HistoryKind.Response, $"{by}: {b.Pump?.Label ?? b.Name} 방열판을 고쳤다", b.Pump?.Room, at: b.Radiator[0]);
        w.Board.RequestScan();
    }

    /// <summary>냉각수 보충 (정수 탱크에서).</summary>
    public float Refill(float liters)
    {
        var w = _world;
        float add = MathF.Min(liters, MathF.Min(w.Water.Level, CoolantMax - Coolant));
        if (add <= 0f) return 0f;
        w.Water.Level -= add;
        Coolant += add;
        CoolantAdded += add;
        return add;
    }
}
