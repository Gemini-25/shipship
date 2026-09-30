using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

/// <summary>
/// 구조 연결부 하나 (v8). 방의 외판을 우주선 골격(용골)에 잇는 볼트·브래킷 묶음으로, 방의 외벽 칸에 붙어 있다.
/// 운석이 끊고, 끊긴 만큼 남은 연결부에 하중이 몰려 피로로 끊어진다. 다 끊어지면 방이 떨어져 나간다.
/// 손상은 밖에서 봐야 안다 (검사 드론·EVA). 승무원이 아는 값(Known)과 실제 값(Strength)이 다를 수 있다.
/// </summary>
public sealed class Joint
{
    public int Id { get; init; }

    /// <summary>방 안에서의 번호 (1부터).</summary>
    public int Index { get; init; }
    public Room Room { get; set; } = null!;

    /// <summary>연결부가 박힌 외벽 칸.</summary>
    public Cell Cell { get; init; }

    /// <summary>바깥(우주) 쪽 방향. EVA·드론이 붙어 일하는 자리는 Cell + Out.</summary>
    public Cell Out { get; init; }

    public float Strength { get; set; } = 1f;

    /// <summary>고칠 수 있는 최대 강도. 다시 이을 때마다 조금씩 내려간다 (피로).</summary>
    public float MaxStrength { get; set; } = 1f;

    /// <summary>승무원이 알고 있는 강도 (검사해야 갱신된다).</summary>
    public float Known { get; set; } = 1f;
    public long SeenAt { get; set; }

    /// <summary>건설 드론이 덧댄 임시 트러스.</summary>
    public bool Truss { get; init; }

    /// <summary>사출 준비로 풀어 놓았다 (하중을 받지 않는다).</summary>
    public bool Released { get; set; }

    public int Repairs { get; set; }
    public int Breaks { get; set; }
    public List<Mark> Marks { get; } = new();

    public bool Broken => Strength <= 0.02f || Released;
    public bool KnownBroken => Known <= 0.02f || Released;
    public Cell Spot => Cell + Out;
    public string Label => $"{Room.Name} {(Truss ? "임시 트러스" : "연결부")} {Index}";
}

public enum FragmentState { Adrift, Towed, Moored, Lost }

/// <summary>
/// 떨어져 나간 구획. 격자에서 빠져나와 우주를 떠다닌다 (화면에서는 원래 자리에서 밀려나며 돈다).
/// 견인 드론이 끌어와 임시 도킹하면 다시 붙일 수 있고, 너무 멀어지면 잃는다.
/// </summary>
public sealed class Fragment
{
    public int Id { get; init; }
    public Room Room { get; init; } = null!;

    /// <summary>원래 자리에서 밀려난 거리 (칸).</summary>
    public Vector2 Offset { get; set; }
    public Vector2 PreviousOffset { get; set; }
    public Vector2 Velocity { get; set; }

    /// <summary>회전 (라디안)과 회전 속도 (라디안/시간).</summary>
    public float Angle { get; set; }
    public float PreviousAngle { get; set; }
    public float Spin { get; set; }

    public FragmentState State { get; set; }
    public long Since { get; init; }
    public long StateSince { get; set; }

    /// <summary>사출한 것인지 (아니면 뜯겨 나감).</summary>
    public bool Jettisoned { get; init; }
    public string Cause { get; init; } = "";

    /// <summary>되찾기로 결정했다.</summary>
    public bool RetrieveApproved { get; set; }

    /// <summary>끌고 오는 드론.</summary>
    public Drone? Tug { get; set; }

    /// <summary>떨어질 때 함께 나간 벽과 문 (다시 붙이면 되돌린다).</summary>
    internal List<(Cell cell, WallState wall)> Walls { get; } = new();
    internal List<(Door door, WallState plug)> Plugs { get; } = new();
    internal List<Door> CarriedDoors { get; } = new();

    public IReadOnlyList<(Cell cell, WallState wall)> WallCells => Walls;
    public IEnumerable<Door> Doors => CarriedDoors;

    public float Distance => Offset.Length();

    /// <summary>화면 중심 (조각의 현재 자리).</summary>
    public Vector2 Center => Room.Center + Offset;

    /// <summary>원래 격자의 한 점이 지금 조각 위에서 어디에 있는지 (조각과 함께 밀리고 돈다).</summary>
    public Vector2 Carry(Vector2 p)
    {
        var d = p - Room.Center;
        float c = MathF.Cos(Angle), s = MathF.Sin(Angle);
        return Center + new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
    }
}

public enum JettisonStage { Evacuate, Salvage, Power, Pipes, Vent, Bulkheads, Release, Eject, Done }

/// <summary>
/// 구획 사출 절차 (리뷰어 안 12): 대피 → 회수 → 전력 → 배관 → 환기 → 격벽 → 연결 해제 → 사출.
/// 단계마다 뜯겨 나갈 때의 피해 하나를 막는다. 끝내기 전에 뜯겨 나가면 해 둔 만큼만 덜 다친다.
/// </summary>
public sealed class JettisonPlan
{
    public JettisonStage Stage { get; set; }
    public long Started { get; init; }
    public long StageSince { get; set; }
    public string Reason { get; init; } = "";
    public CrewMember? Decider { get; init; }

    /// <summary>불 때문에 버리는지 (불타는 방은 회수하러 못 들어간다).</summary>
    public bool Fire { get; init; }

    /// <summary>드론·EVA 없이 폭발 볼트로 끊는다 (이웃 벽이 조금 상한다).</summary>
    public bool Bolts { get; set; }

    public int Salvaged { get; set; }

    public static string StageName(JettisonStage s) => s switch
    {
        JettisonStage.Evacuate => "대피",
        JettisonStage.Salvage => "물품 회수",
        JettisonStage.Power => "전력 차단",
        JettisonStage.Pipes => "배관 차단",
        JettisonStage.Vent => "환기 차단",
        JettisonStage.Bulkheads => "격벽 용접",
        JettisonStage.Release => "연결 해제",
        JettisonStage.Eject => "사출",
        _ => "끝",
    };
}

/// <summary>
/// 구조 (v8). 방마다의 연결부, 하중과 피로, 떨어져 나감(격자 수술)과 다시 붙임, 떠다니는 조각.
/// "방 분리"는 이벤트가 아니라 연결부가 다 끊어진 결과다.
/// </summary>
public sealed class StructureSystem
{
    /// <summary>이만큼 멀어지면 시야 밖으로 사라진다 (견인할 수 없다).</summary>
    public const float LostRange = 60f;

    private readonly World _world;
    public List<Joint> Joints { get; } = new();
    public List<Fragment> Fragments { get; } = new();
    public Vector2 ShipCenter { get; }

    /// <summary>격자가 바뀔 때마다 오른다 (화면이 정적 레이어를 다시 그린다).</summary>
    public int Version { get; private set; }

    /// <summary>v10.2: 설계가 바뀌었다 (화면이 다시 그린다).</summary>
    public void Touch() => Version++;

    // 누적 (역사·지표)
    public int JointBreaks { get; set; }
    public int Detachments { get; set; }
    public int Jettisons { get; set; }
    public int Retrieved { get; set; }
    public int RoomsLost { get; set; }

    /// <summary>환기관이 우주로 열려 있다 (떨어져 나간 방의 댐퍼가 열린 채). 그동안 전기가 있는 댐퍼는 모두 닫는다 (환기망 격리).</summary>
    public bool DuctOpen { get; private set; }

    /// <summary>방마다 하중 (1 넘으면 연결부가 피로로 상한다). 화면·작업 목록용.</summary>
    public Dictionary<int, float> Stress { get; } = new();

    /// <summary>운석을 맞은 뒤 아직 밖에서 보지 못한 방 → 맞은 틱.</summary>
    public Dictionary<int, long> Unseen { get; } = new();

    public StructureSystem(World world)
    {
        _world = world;
        var ship = world.Ship;
        float vol = 0f;
        var c = Vector2.Zero;
        foreach (var r in ship.Rooms) { c += r.Center * r.Volume; vol += r.Volume; }
        ShipCenter = vol > 0 ? c / vol : Vector2.Zero;
        foreach (var r in ship.Rooms) BuildJoints(r);
    }

    /// <summary>방의 외벽을 둘레 순서로 늘어놓고 고르게 연결부를 박는다 (외벽 네댓 칸에 하나, 방마다 2~6개).</summary>
    private void BuildJoints(Room room)
    {
        var ship = _world.Ship;
        var floor = new HashSet<Cell>(room.Cells);
        var hull = new List<Cell>();
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull) continue;
            if (Cell.Dirs4.Any(d => floor.Contains(cell + d))) hull.Add(cell);
        }
        if (hull.Count == 0) { room.DesignJoints = 0; return; }
        var center = room.Center;
        hull.Sort((a, b) => Angle(a.Center - center).CompareTo(Angle(b.Center - center)));
        int n = Math.Clamp((int)MathF.Round(hull.Count / 4.5f), 2, 6);
        n = Math.Min(n, hull.Count);
        for (int i = 0; i < n; i++)
        {
            var cell = hull[(int)((i + 0.5f) * hull.Count / n)];
            var j = new Joint { Id = Joints.Count, Index = i + 1, Room = room, Cell = cell, Out = OutwardOf(ship, cell), SeenAt = _world.Tick };
            Joints.Add(j);
            room.Joints.Add(j);
        }
        room.DesignJoints = n;
    }

    private static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X);

    /// <summary>임시 트러스를 박을 외벽: 이 방의 외벽 중 남은 연결부에서 가장 먼 곳.</summary>
    public Cell? TrussSpot(Room room)
    {
        var ship = _world.Ship;
        var floor = new HashSet<Cell>(room.Cells);
        var used = room.Joints.Select(j => j.Cell).ToHashSet();
        var holding = room.Joints.Where(j => !j.Broken).Select(j => j.Cell).ToList();
        Cell? best = null;
        float bestD = -1f;
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull || wall.FrameLost || used.Contains(cell)) continue;
            if (!Cell.Dirs4.Any(d => floor.Contains(cell + d))) continue;
            if (!Cell.Dirs4.Any(d => ship.Grid.Kind(cell + d) == TileKind.Void)) continue;
            float d = holding.Count == 0 ? 1f : holding.Min(h => (h.Center - cell.Center).LengthSquared());
            if (d > bestD) { bestD = d; best = cell; }
        }
        return best;
    }

    /// <summary>임시 트러스를 덧댄다: 하중을 나눠 지는 연결부가 하나 는다 (새것만은 못하다).</summary>
    public Joint AddTruss(Room room, Cell cell) => AddJoint(room, cell, 0.7f, 0.75f, truss: true);

    /// <summary>연결부를 하나 더 박는다 (임시 트러스 또는 개조로 증설한 연결부).</summary>
    public Joint AddJoint(Room room, Cell cell, float strength, float max, bool truss)
    {
        var j = new Joint
        {
            Id = Joints.Count, Index = room.Joints.Count + 1, Room = room, Cell = cell, Out = OutwardOf(_world.Ship, cell),
            Truss = truss, Strength = strength, MaxStrength = max, Known = strength, SeenAt = _world.Tick,
        };
        Joints.Add(j);
        room.Joints.Add(j);
        Version++;
        return j;
    }

    /// <summary>하중 때문에(운석이 아니라) 끊어졌는데 승무원은 멀쩡한 줄 알았던 연결부 수 (교훈: 더 자주 검사한다).</summary>
    public int SurpriseBreaks { get; set; }

    private static Cell OutwardOf(Ship ship, Cell c)
    {
        foreach (var d in Cell.Dirs4) if (ship.Grid.Kind(c + d) == TileKind.Void) return d;
        foreach (var d in Cell.Dirs8) if (ship.Grid.Kind(c + d) == TileKind.Void) return d;
        return new Cell(0, -1);
    }

    /// <summary>하중을 받는 연결부 강도 합.</summary>
    public static float Capacity(Room r) => r.Joints.Where(j => !j.Broken).Sum(j => j.Strength);

    /// <summary>승무원이 아는 강도 합.</summary>
    public static float KnownCapacity(Room r) => r.Joints.Where(j => !j.KnownBroken).Sum(j => j.Known);

    public static int FrameLost(World w, Room r) => w.Structure.FramesLost.GetValueOrDefault(r.Id);

    /// <summary>방마다 골조를 잃은 외벽 수 (시스템 틱마다 한 번 센다).</summary>
    public Dictionary<int, int> FramesLost { get; } = new();

    private void CountFrames()
    {
        FramesLost.Clear();
        foreach (var (cell, wall) in _world.Ship.Walls)
            if (wall.IsHull && wall.FrameLost && Hull.InsideRoom(_world.Ship, cell) is Room r)
                FramesLost[r.Id] = FramesLost.GetValueOrDefault(r.Id) + 1;
    }

    /// <summary>하중: 설계 연결부 절반이 버티는 무게를 남은 연결부가 나눠 진다. 1 넘으면 피로가 쌓인다.</summary>
    public static float StressOf(float capacity, int design, int frameLost) =>
        design <= 0 ? 0f : design * 0.45f / MathF.Max(0.01f, capacity) + 0.06f * frameLost;

    /// <summary>
    /// 하중이 이대로면 몇 시간 뒤 떨어져 나갈지 (아는 값 또는 실제 값으로 앞으로 돌려 본다: 약한 연결부가 끊어지면 나머지가 더 빨리 상한다).
    /// 버티면 무한대.
    /// </summary>
    public static float HoursToFailure(Room r, bool known, int frameLost)
    {
        if (r.DesignJoints <= 0) return float.PositiveInfinity;
        var s = r.Joints.Where(j => known ? !j.KnownBroken : !j.Broken).Select(j => known ? j.Known : j.Strength).ToArray();
        float snap = SnapAt(r);
        const float step = 0.25f;
        for (float t = 0f; t < 96f; t += step)
        {
            float cap = 0f;
            foreach (var v in s) if (v > 0.02f) cap += v;
            if (cap < snap) return t;
            float stress = StressOf(cap, r.DesignJoints, frameLost) * (r.Docked ? 0.5f : 1f);
            if (stress <= 1f) return float.PositiveInfinity;
            for (int i = 0; i < s.Length; i++)
                if (s[i] > 0.02f) s[i] -= FatigueRate(stress, s[i]) * step;
        }
        return float.PositiveInfinity;
    }

    /// <summary>이 아래로 떨어지면 남은 연결부가 한꺼번에 뜯긴다.</summary>
    public static float SnapAt(Room r) => r.Docked ? 0.25f : 0.12f * r.DesignJoints;

    public static float FatigueRate(float stress, float strength) =>
        stress <= 1f ? 0f : 0.03f * (stress - 1f) * (stress - 1f) * (1.5f - strength);

    // ─────────────────────────────── 매 시스템 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _world;
        CountFrames();
        bool duct = w.Ship.Rooms.Any(r => r.Detached && r.VentOpen);
        if (duct != DuctOpen)
        {
            DuctOpen = duct;
            if (duct) w.RaiseAlert("환기관이 우주로 열렸다 — 환기망 격리 (댐퍼 전부 폐쇄)", null, AlertLevel.Critical, shipWide: true);
            else w.Log.Add(w.Tick, LogKind.Ship, "환기관을 막았다 — 환기망 격리 해제");
        }
        foreach (var room in w.Ship.Rooms)
        {
            if (room.Detached) Stress.Remove(room.Id);
            if (room.Detached || room.DesignJoints <= 0) continue;
            int frames = FrameLost(w, room);
            float cap = Capacity(room);
            // 임시 도킹 중인 방은 계류줄이 하중을 나눠 진다 (그래도 오래 두면 끊어진다)
            float stress = StressOf(cap, room.DesignJoints, frames) * (room.Docked ? 0.5f : 1f);
            Stress[room.Id] = stress;
            float snap = SnapAt(room);

            bool released = room.Jettison?.Stage >= JettisonStage.Release;
            if (room.Joints.All(j => j.Broken) || cap < snap)
            {
                if (released) continue; // 사출 스위치를 기다린다
                Detach(room, room.Jettison != null ? $"사출 준비 중에 ({JettisonPlan.StageName(room.Jettison.Stage)} 단계) 연결부가 버티지 못했다"
                    : "연결부가 모두 끊어졌다", controlled: false);
                continue;
            }

            // 피로: 남은 연결부가 하중을 나눠 지다 약한 것부터 끊어진다
            if (stress > 1f)
                foreach (var j in room.Joints)
                {
                    if (j.Broken) continue;
                    j.Strength -= FatigueRate(stress, j.Strength) * dt;
                    if (j.Strength <= 0.02f) Break(j, "하중이 몰려");
                }

            // 삐걱거림: 방 안에 있는 사람은 크게 상한 연결부를 소리로 안다 (정확하지는 않다)
            if (w.Rng.Chance(0.6f * dt))
                foreach (var j in room.Joints)
                {
                    if (j.Broken || j.Strength > 0.45f || j.Known - j.Strength < 0.15f) continue;
                    var who = w.Crew.FirstOrDefault(c => c.CanAct && c.IsAwake && c.Room == room);
                    if (who == null) break;
                    j.Known = MathF.Round(j.Strength * 5f) / 5f;
                    j.SeenAt = w.Tick;
                    w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} 벽 너머에서 삐걱거리는 소리가 난다 — {Ko.IGa(j.Label)} 상했다", who.Id);
                    MarkLog.Add(j.Marks, w.Tick, $"{Ko.IGa(who.Name)} 삐걱거림을 들었다");
                    w.Board.RequestScan();
                }
        }

        // 떠다니는 조각
        foreach (var f in Fragments)
        {
            f.PreviousOffset = f.Offset;
            f.PreviousAngle = f.Angle;
            if (f.State == FragmentState.Adrift)
            {
                f.Offset += f.Velocity * dt;
                f.Angle += f.Spin * dt;
                if (f.Distance > LostRange)
                {
                    f.State = FragmentState.Lost;
                    f.StateSince = w.Tick;
                    RoomsLost++;
                    var aboard = w.Drones.Drones.Where(d => d.State == DroneState.Docked && d.Dock.Room == f.Room).ToList();
                    w.History.Add(w, HistoryKind.Structure, $"떨어져 나간 {Ko.IGa(f.Room.Name)} 시야 밖으로 떠내려갔다 — 잃었다" +
                                  (aboard.Count > 0 ? $" (거치대에 묶인 드론 {aboard.Count}대와 함께)" : ""), f.Room, log: true);
                    foreach (var d in aboard) w.Drones.LoseWith(d, f);
                    // 조각에 탄 사람: 사망이 켜져 있으면 함께 잃고, 아니면 마지막 순간 뛰어내려 배 쪽으로 (구조를 기다린다)
                    foreach (var c in w.Crew.Where(c => c.Aboard == f))
                    {
                        c.Aboard = null;
                        if (w.CrewCanDie && !c.Dead) { c.Vitals.Health = 0f; c.Vitals.InjuryCause = $"{f.Room.Name}과(와) 함께 표류"; }
                        else
                        {
                            c.Position = NearestEdge(w.Ship.Rooms.Where(r => !r.Detached).Select(r => r.Center).OrderBy(p2 => (p2 - c.Position).LengthSquared()).First()).Center;
                            c.PreviousPosition = c.Position;
                            w.Log.Add(w.Tick, LogKind.Warning, $"떠내려가는 {f.Room.Name}에서 뛰어내려 배 쪽으로 — 구조를 기다린다", c.Id);
                        }
                    }
                    foreach (var r in w.Robots.Robots.Where(r => r.Aboard == f)) r.Aboard = null;
                    MarkLog.Add(f.Room.Marks, w.Tick, "시야 밖으로 떠내려갔다");
                    w.Board.RequestScan();
                }
            }
            else if (f.State == FragmentState.Moored)
            {
                // 계류 중: 천천히 제자리로 돌려 맞춘다
                f.Offset *= MathF.Max(0f, 1f - 3f * dt);
                f.Angle *= MathF.Max(0f, 1f - 3f * dt);
            }
        }
    }

    /// <summary>연결부가 끊어졌다 (소리가 커서 모두 안다).</summary>
    public void Break(Joint j, string why)
    {
        var w = _world;
        if (why != "운석" && j.Known >= 0.6f) SurpriseBreaks++;
        j.Strength = 0f;
        j.Known = 0f;
        j.SeenAt = w.Tick;
        j.Breaks++;
        JointBreaks++;
        var room = j.Room;
        int left = room.Joints.Count(x => !x.Broken);
        MarkLog.Add(j.Marks, w.Tick, $"끊어졌다 ({why})");
        MarkLog.Add(room.Marks, w.Tick, $"{j.Label} 끊어짐 ({left}/{room.Joints.Count} 남음)");
        w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(j.Label)} 끊어졌다 ({why}) — 남은 연결부 {left}/{room.Joints.Count}", room, at: j.Cell);
        w.RaiseAlert($"{j.Label} 끊어짐 · 남은 연결부 {left}/{room.Joints.Count}", room, left <= 1 ? AlertLevel.Critical : AlertLevel.Warning, shipWide: true);
        foreach (var c in w.Crew)
            if (!c.Dead && c.Room == room) Memory.Frighten(w, c, room, 0.15f, "연결부가 끊어지는 소리를 들었다");
        w.Board.RequestScan();
    }

    /// <summary>운석이 박혔다: 가까운 연결부가 상한다 (밖에서 보기 전에는 얼마나 상했는지 모른다).</summary>
    public void OnImpact(Cell entry, float size)
    {
        var w = _world;
        float radius = 1.5f + 3f * size;
        var hitRooms = new HashSet<Room>();
        foreach (var j in Joints)
        {
            if (j.Room.Detached || j.Broken) continue;
            float d = (j.Cell.Center - entry.Center).Length();
            if (d > radius) continue;
            float falloff = MathF.Pow(1f - d / (radius + 0.5f), 1.5f);
            // 연결부는 한 번에 끊어지지 않는다: 한 방에 큰 운석(1)이면 0.57, 거대 운석(1.5)이면 0.7까지 (약해진 것은 이 한 방에 끊어진다)
            float cap = MathF.Min(0.7f, 0.3f + 0.27f * size);
            float dmg = MathF.Min(cap, (0.15f + 0.75f * size) * falloff * w.Rng.Range(0.7f, 1.1f));
            j.Strength = MathF.Max(0f, j.Strength - dmg);
            hitRooms.Add(j.Room);
            if (j.Strength <= 0.02f) Break(j, "운석");
        }
        // 충격은 방 전체 골격을 흔든다
        var inside = Hull.InsideRoom(w.Ship, entry);
        if (inside != null && inside.DesignJoints > 0)
        {
            hitRooms.Add(inside);
            foreach (var j in inside.Joints)
                if (!j.Broken) j.Strength = MathF.Max(0.03f, j.Strength - 0.03f * size * size);
        }
        foreach (var r in hitRooms) Unseen[r.Id] = w.Tick;
    }

    /// <summary>검사: 밖에서 본 연결부의 실제 상태를 안다.</summary>
    public void Inspect(Room room, CrewMember? who = null, Drone? drone = null)
    {
        var w = _world;
        int bad = 0;
        foreach (var j in room.Joints)
        {
            if (Math.Abs(j.Known - j.Strength) > 0.1f && j.Strength < 0.8f) bad++;
            j.Known = j.Strength;
            j.SeenAt = w.Tick;
        }
        Unseen.Remove(room.Id);
        if (bad > 0)
        {
            string by = drone != null ? drone.Name : who != null ? who.Name + "(EVA)" : "검사";
            w.History.Add(w, HistoryKind.Response, $"{by}: {room.Name} 연결부 {bad}곳이 상한 것을 찾았다 — 가장 약한 곳 {room.Joints.Where(j => !j.Broken).Select(j => j.Strength).DefaultIfEmpty(0f).Min() * 100:0}%",
                room, who != null ? new[] { who } : null);
            w.Board.RequestScan();
        }
    }

    // ─────────────────────────────── 떨어져 나감 ───────────────────────────────

    /// <summary>
    /// 방이 떨어져 나간다. 격자에서 바닥·벽·문을 빼고 조각으로 만든다.
    /// 이웃과 나눠 쓰던 벽은 우주선에 남아 새 외벽이 된다 — 통제된 사출이면 멀쩡히, 뜯겨 나가면 찢어져서.
    /// 전력·배관·환기를 끊어 두지 않았으면 그만큼 대가를 치른다 (단락, 물, 공기).
    /// </summary>
    public Fragment Detach(Room room, string cause, bool controlled)
    {
        var w = _world;
        var ship = w.Ship;
        var grid = ship.Grid;
        var plan = room.Jettison;
        bool bolts = plan?.Bolts == true;
        var floor = new HashSet<Cell>(room.Cells);
        var frag = new Fragment { Id = Fragments.Count + 1, Room = room, Since = w.Tick, StateSince = w.Tick, Jettisoned = controlled, Cause = cause };
        var notes = new List<string>();

        // 0) 불은 진공에서 꺼진다
        w.Fire.ClearRoom(room);

        // 1) 문: 이웃으로 이어지던 문은 벽으로 막힌다 (용접해 둔 격벽은 멀쩡, 닫혀 있으면 금이 가고, 열려 있으면 뚫린다)
        int torn = 0;
        foreach (var d in room.Doors.ToList())
        {
            if (d.Removed) continue;
            var other = d.RoomA == room ? d.RoomB : d.RoomA;
            // v10.3: 열려 있었는지는 문을 떼기 전에 본다 (전에는 0으로 만든 뒤에 봐서, 열린 문도 늘 닫힌 문처럼 계산됐다)
            float wasOpen = d.JammedOpen ? 1f : d.Openness;
            ship.UnmapDoor(d);
            d.Removed = true;
            d.Openness = 0f;
            if (other == null)
            {
                grid.SetKind(d.Cell, TileKind.Void);
                frag.CarriedDoors.Add(d);
                continue;
            }
            other.Doors.Remove(d);
            // 닫혀 있던 문은 격벽 노릇을 한다 (잠겨 있었으면 더 단단하다). 열려 있었으면 문틀째 뜯겨 구멍이 된다
            float integrity = d.Welded ? 0.95f
                : wasOpen < 0.1f ? (controlled ? 0.85f : MathF.Min(0.9f, w.Rng.Range(0.35f, 0.75f) + (d.Locked ? 0.15f : 0f)))
                : controlled ? 0.6f : w.Rng.Range(0f, 0.12f);
            var plug = new WallState { IsHull = true, Integrity = integrity, MaxIntegrity = MathF.Max(0.5f, integrity), Frame = integrity < 0.05f ? 0.5f : 1f };
            plug.Breach = Hull.BreachFromIntegrity(plug.Integrity);
            if (plug.Breach > 0f) torn++;
            MarkLog.Add(plug.Marks, w.Tick, d.Welded ? "용접한 격벽 (사출)" : $"{room.Name}이 떨어져 나가며 문이 벽이 됐다");
            grid.SetKind(d.Cell, TileKind.Wall);
            ship.AddWall(d.Cell, plug);
            frag.Plugs.Add((d, plug));
        }

        // 2) 벽: 이웃 방에 닿은 벽은 남고, 이 방만 두르던 벽은 조각과 함께 나간다
        var near = new List<Cell>();
        foreach (var c in floor)
            foreach (var d in Cell.Dirs8)
            {
                var n = c + d;
                if (ship.WallAt(n) != null && !near.Contains(n)) near.Add(n);
            }
        var stays = new List<Cell>();
        foreach (var cell in near)
        {
            bool keep = false;
            foreach (var d in Cell.Dirs8)
            {
                var n = cell + d;
                var k = grid.Kind(n);
                if (k == TileKind.Floor && ship.RoomAt(n) is Room r2 && r2 != room) { keep = true; break; }
                if (k == TileKind.Door && ship.DoorAt(n) is Door dd && !room.Doors.Contains(dd)) { keep = true; break; }
            }
            if (keep) { stays.Add(cell); continue; }
            frag.Walls.Add((cell, ship.WallAt(cell)!));
            ship.RemoveWall(cell);
            grid.SetKind(cell, TileKind.Void);
        }

        // 3) 바닥과 가구
        foreach (var c in floor)
        {
            grid.SetKind(c, TileKind.Void);
            grid.SetRoomId(c, -1);
            grid.SetFurnitureId(c, -1);
        }
        foreach (var f in room.Furniture)
        {
            if (f.ReservedBy != null) f.ReservedBy = null;
            if (f.Machine != null) f.Machine.Powered = false;
        }

        // 4) 남은 벽 중 우주에 닿게 된 것은 외벽이 된다. 뜯겨 나갔으면 찢어진다.
        foreach (var cell in stays)
        {
            var wall = ship.WallAt(cell)!;
            bool hull = Cell.Dirs8.Any(d => grid.Kind(cell + d) == TileKind.Void);
            if (!hull) continue;
            bool wasHull = wall.IsHull;
            wall.IsHull = true;
            if (wasHull) continue;
            float tear = controlled ? (bolts ? w.Rng.Range(0.05f, 0.35f) : w.Rng.Range(0f, 0.08f)) : w.Rng.Range(0.15f, 0.65f);
            wall.Integrity = MathF.Max(0f, wall.Integrity - tear);
            if (!controlled && w.Rng.Chance(0.06f)) wall.Frame = MathF.Max(0f, wall.Frame - w.Rng.Range(0.3f, 0.8f));
            wall.Breach = wall.FrameLost ? 1f : Hull.BreachFromIntegrity(wall.Integrity);
            if (wall.Breach > 0f) torn++;
            MarkLog.Add(wall.Marks, w.Tick, controlled ? $"{room.Name} 사출 뒤 외벽이 됐다" : $"{Ko.IGa(room.Name)} 뜯겨 나가며 찢겼다");
        }
        foreach (var (cell, wall) in ship.Walls)
            if (!wall.IsHull && Cell.Dirs8.Any(d => grid.Kind(cell + d) == TileKind.Void)) wall.IsHull = true;

        // 5) 연결부는 다 끊어진 채로 조각에 남는다
        foreach (var j in room.Joints)
        {
            // 한꺼번에 뜯긴 연결부도 끊어진 것으로 센다 (풀어 둔 것은 빼고)
            if (!controlled && !j.Released && j.Strength > 0.02f) { j.Breaks++; JointBreaks++; }
            j.Strength = 0f; j.Known = 0f;
        }

        // 6) 사람: 안에 있던 사람은 조각에 탄 채 떠내려간다 (우주복이 있거나 쓰러져 있거나 손잡이를 붙잡았다),
        //    아니면 찢어진 틈으로 배 쪽 가장자리에 내동댕이쳐진다 (우주복이 없으면 진공)
        var flung = new List<CrewMember>();
        var aboardList = new List<CrewMember>();
        foreach (var c in w.Crew)
        {
            if (c.CarriedBy != null || c.Aboard != null) continue;
            if (!floor.Contains(c.Cell) && c.Room != room) continue;
            c.EndJob(w, ToilStatus.Interrupted);
            bool hold = c.Suit != null || c.Down || c.Dead || w.Rng.Chance(0.4f);
            if (hold)
            {
                c.Aboard = frag;
                c.AboardAt = c.Position;
                c.Room = null;
                c.Path = null;
                aboardList.Add(c);
                if (c.Dead) continue;
                float hit = w.Rng.Range(0.05f, 0.15f) * (c.Suit != null ? 0.5f : 1f);
                c.Vitals.Health = MathF.Max(0.05f, c.Vitals.Health - hit);
                NeedsSystem.AddInjury(c.Vitals, hit, "구획 분리");
                Memory.Frighten(w, c, room, 0.7f, $"{Ko.WaGwa(room.Name)} 함께 떠내려갔다");
                Memory.Shake(w, c, 0.2f, $"{Ko.WaGwa(room.Name)} 함께 우주로 떠내려갔다");
                MarkLog.Add(c.Memory.Marks, w.Tick, $"{Ko.WaGwa(room.Name)} 함께 떠내려갔다" + (c.Suit == null ? " (우주복 없이)" : ""));
                continue;
            }
            var to = NearestEdge(c.Position);
            c.Position = to.Center;
            c.PreviousPosition = c.Position;
            c.Room = null;
            c.Path = null;
            flung.Add(c);
            if (c.Dead) continue;
            float dmg = w.Rng.Range(0.08f, 0.2f) * (c.Suit != null ? 0.5f : 1f);
            c.Vitals.Health = MathF.Max(0.05f, c.Vitals.Health - dmg);
            NeedsSystem.AddInjury(c.Vitals, dmg, "구획 분리");
            c.Interrupt(w);
            Memory.Frighten(w, c, room, 0.6f, $"{Ko.IGa(room.Name)} 떨어져 나갈 때 거기 있었다");
            Memory.Shake(w, c, 0.15f, $"{Ko.WaGwa(room.Name)} 함께 떨어져 나갈 뻔했다");
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{Ko.IGa(room.Name)} 떨어져 나갈 때 밖으로 튕겨 나갔다" + (c.Suit == null ? " (우주복 없이)" : ""));
            if (c.CarryingPerson is CrewMember p) { p.Position = c.Position; p.PreviousPosition = c.Position; }
        }

        // 드론: 거치대가 이 방에 있으면 묶인 채 함께 떠간다 (돌아올 곳이 없어진 드론은 밖에서 떠돈다)
        int dronesAboard = 0;
        foreach (var d in w.Drones.Drones)
        {
            if (d.Dock.Room != room || d.State is DroneState.Lost) continue;
            if (d.State == DroneState.Docked)
            {
                dronesAboard++;
                d.Doing = "떨어져 나간 거치대에 묶여 있다";
                MarkLog.Add(d.Marks, w.Tick, $"거치대에 묶인 채 {Ko.WaGwa(room.Name)} 함께 떨어져 나갔다");
            }
        }
        if (dronesAboard > 0) notes.Add($"드론 {dronesAboard}대가 거치대째 함께");
        if (aboardList.Count > 0) notes.Add($"{string.Join("·", aboardList.Select(c => c.Name))}이(가) 조각에 탄 채 떠내려간다");
        // 로봇도 조각에 실려 간다
        foreach (var r in w.Robots.Robots.Where(r => r.Aboard == null && (floor.Contains(Cell.FromPosition(r.Position)) || r.Room == room)))
        {
            r.Aboard = frag;
            r.AboardAt = r.Position;
            MarkLog.Add(r.Marks, w.Tick, $"{Ko.WaGwa(room.Name)} 함께 떠내려갔다");
        }

        // v9: 방을 지나던 관은 끊어진다 (사출 준비로 밸브를 잠갔으면 새지 않는다)
        w.Piping.OnDetach(room, isolated: room.PipesCut || controlled);

        // 7) 끊지 못한 것들의 대가
        if (!room.PowerCut)
        {
            var panel = ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine;
            if (panel != null && !panel.Faults.Any(f => f.Circuit == room.Circuit))
            {
                // 뜯긴 전선: 대개 차단기가 먼저 떨어지지만, 때로는 단락 (퓨즈를 갈아야 한다)
                var kind = w.Rng.Chance(0.4f) ? FaultKind.ShortCircuit : FaultKind.BreakerTrip;
                panel.Faults.Add(new Fault { Kind = kind, Since = w.Tick, Circuit = room.Circuit });
                panel.FaultCount++;
                w.History.CircuitFaults++;
                MarkLog.Add(panel.Marks, w.Tick, $"{room.Name} 전선이 뜯기며 {PowerGrid.CircuitName(room.Circuit)} 회로 {Faults.Spec(kind).Name}");
                notes.Add($"뜯긴 전선에 {PowerGrid.CircuitName(room.Circuit)} 회로 {Faults.Spec(kind).Name}");
            }
        }
        if (room.HasPipes && !room.PipesCut)
        {
            float lost = MathF.Min(w.Water.Level, w.Water.Capacity * w.Rng.Range(0.2f, 0.35f));
            w.Water.Level -= lost;
            notes.Add($"배관이 뜯겨 물 {lost:0}L를 잃었다");
        }
        if (room.VentOpen && !room.VentSealed) notes.Add("환기관이 우주로 열렸다");
        if (torn > 0) notes.Add($"이웃 벽 {torn}곳이 찢어졌다");

        // 8) 방 상태: 우주선 밖
        room.Detached = true;
        room.Fragment = frag;
        room.Abandoned = true;
        room.AbandonedSince = w.Tick;
        room.Lockdown = false;
        room.Powered = false;
        room.Air.O2 = 0f; room.Air.N2 = 0f; room.Air.CO2 = 0f; room.Air.Smoke = 0f; room.Air.Toxin = 0f; room.Air.Leak = 0f;
        room.BreachArea = 0f;
        room.Unbreathable = true;
        room.Detachments++;
        if (controlled) room.Jettisons++;
        if (plan != null) plan.Stage = JettisonStage.Done;
        room.Jettison = null;

        // 9) 떠나는 방향: 우주선 중심에서 멀어지는 쪽 (사출은 빠르게)
        var dir = room.Center - ShipCenter;
        dir = dir.LengthSquared() < 0.01f ? new Vector2(0, -1) : Vector2.Normalize(dir);
        float a = w.Rng.Range(-0.35f, 0.35f);
        dir = new Vector2(dir.X * MathF.Cos(a) - dir.Y * MathF.Sin(a), dir.X * MathF.Sin(a) + dir.Y * MathF.Cos(a));
        frag.Velocity = dir * (controlled ? w.Rng.Range(4f, 6f) : w.Rng.Range(1.2f, 3f));
        frag.Spin = w.Rng.Range(-0.25f, 0.25f) * (controlled ? 0.5f : 1f);
        Fragments.Add(frag);
        Detachments++;
        if (controlled) Jettisons++;

        w.Paths.Invalidate();
        w.Board.RequestScan();
        Version++;

        // 10) 역사
        var h = w.History;
        string who = flung.Count > 0 ? $" · {string.Join("·", flung.Select(c => c.Name))} 밖으로 튕겨 나감" : "";
        string tail = notes.Count > 0 ? " · " + string.Join(" · ", notes) : "";
        if (controlled)
        {
            h.Add(w, HistoryKind.Structure, $"{Ko.EulReul(room.Name)} 사출했다 — {cause}{(bolts ? " (폭발 볼트)" : "")}{who}{tail}", room, flung, log: true);
            MarkLog.Add(room.Marks, w.Tick, $"사출 — {cause}");
            h.Lost($"{room.Name} 사출");
            w.RaiseAlert($"{room.Name} 사출", null, AlertLevel.Warning, shipWide: true);
        }
        else
        {
            h.Add(w, HistoryKind.Structure, $"{Ko.IGa(room.Name)} 떨어져 나갔다 — {cause}{who}{tail}", room, flung, log: true);
            MarkLog.Add(room.Marks, w.Tick, $"떨어져 나갔다 — {cause}");
            h.Lost($"{room.Name} 분리");
            w.RaiseAlert($"{Ko.IGa(room.Name)} 떨어져 나갔다!{tail}", null, AlertLevel.Critical, shipWide: true);
        }
        // 모두에게 충격: 배가 찢어지는 것을 느꼈다
        foreach (var c in w.Crew)
        {
            if (c.Dead || flung.Contains(c)) continue;
            Memory.Shake(w, c, controlled ? 0.02f : 0.06f, controlled ? $"{room.Name} 사출" : $"{Ko.IGa(room.Name)} 떨어져 나간 일");
            if (!controlled) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.15f);
            if (c.Room != null && c.Room.Doors.Any(d => frag.Plugs.Any(p => p.door == d)) || room.Doors.Any(d => d.RoomA == c.Room || d.RoomB == c.Room))
                Memory.Frighten(w, c, c.Room, controlled ? 0.05f : 0.2f, $"바로 옆 {Ko.IGa(room.Name)} 떨어져 나갔다");
        }
        return frag;
    }

    /// <summary>튕겨 나간 사람이 붙잡는 곳: 우주선 가장자리의 가까운 우주 칸.</summary>
    /// <summary>조각 위의 한 점이 지금 어디에 있나 (조각의 원래 좌표 → 떠내려간 자리).</summary>
    public static Vector2 OnFragment(Fragment f, Vector2 local)
    {
        var rel = local - f.Room.Center;
        float cs = MathF.Cos(f.Angle), sn = MathF.Sin(f.Angle);
        return f.Room.Center + new Vector2(rel.X * cs - rel.Y * sn, rel.X * sn + rel.Y * cs) + f.Offset;
    }

    private Cell NearestEdge(Vector2 p)
    {
        var grid = _world.Ship.Grid;
        Cell best = Cell.FromPosition(p);
        float bestD = float.MaxValue;
        for (int i = 0; i < grid.CellCount; i++)
        {
            var c = grid.CellAt(i);
            if (grid.Kind(c) != TileKind.Void) continue;
            if (!Cell.Dirs4.Any(d => grid.Kind(c + d) is TileKind.Wall)) continue;
            float d2 = (c.Center - p).LengthSquared();
            if (d2 < bestD) { bestD = d2; best = c; }
        }
        return best;
    }

    // ─────────────────────────────── 다시 붙임 ───────────────────────────────

    /// <summary>
    /// 임시 도킹: 끌어온 조각을 원래 자리에 다시 끼운다. 방은 진공 상태로 "포기한 구획"처럼 돌아오고,
    /// 전력·배관·환기는 아직 이어지지 않았다 (재연결 작업이 따로 필요하다).
    /// </summary>
    public bool Reattach(Fragment f)
    {
        var w = _world;
        var ship = w.Ship;
        var grid = ship.Grid;
        var room = f.Room;
        // 자리가 비어 있어야 한다 (누가 그 자리에 떠 있으면 비켜 줄 때까지)
        foreach (var c in room.Cells)
            if (grid.Kind(c) != TileKind.Void) return false;

        foreach (var (cell, wall) in f.Walls)
        {
            grid.SetKind(cell, TileKind.Wall);
            ship.AddWall(cell, wall);
        }
        foreach (var c in room.Cells)
        {
            grid.SetKind(c, TileKind.Floor);
            grid.SetRoomId(c, room.Id);
        }
        foreach (var fu in room.Furniture)
            foreach (var c in fu.Cells) grid.SetFurnitureId(c, fu.Id);
        foreach (var (door, plug) in f.Plugs)
        {
            ship.RemoveWall(door.Cell);
            grid.SetKind(door.Cell, TileKind.Door);
            door.Removed = false;
            door.Locked = true;
            door.Welded = false;
            ship.MapDoor(door);
            var other = door.RoomA == room ? door.RoomB : door.RoomA;
            if (other != null && !other.Doors.Contains(door)) other.Doors.Add(door);
        }
        foreach (var door in f.CarriedDoors)
        {
            grid.SetKind(door.Cell, TileKind.Door);
            door.Removed = false;
            door.Locked = true;
            ship.MapDoor(door);
        }
        // 외벽 여부 다시 따지기
        foreach (var (cell, wall) in ship.Walls)
            wall.IsHull = Cell.Dirs8.Any(d => grid.Kind(cell + d) == TileKind.Void);

        room.Detached = false;
        room.Fragment = null;
        room.Docked = true;
        room.Abandoned = true;
        room.AbandonedSince = w.Tick;
        room.AbandonReason = "다시 붙였지만 아직 이어지지 않았다";
        room.Lockdown = true;
        room.VentOpen = false;
        room.PowerCut = true;
        room.PipesCut = room.HasPipes;
        room.VentSealed = true;
        room.Retrievals++;
        foreach (var d in w.Drones.Drones)
            if (d.Dock.Room == room && d.State == DroneState.Docked)
            {
                d.Doing = "대기 (거치대에 전기가 없다)";
                MarkLog.Add(d.Marks, w.Tick, $"{Ko.WaGwa(room.Name)} 함께 돌아왔다");
            }
        // 조각에 탔던 사람·로봇이 함께 돌아온다
        foreach (var c in w.Crew.Where(c => c.Aboard == f))
        {
            c.Aboard = null;
            c.Position = c.AboardAt;
            c.PreviousPosition = c.Position;
            c.Room = room;
            c.Interrupt(w);
            if (!c.Dead) w.History.Add(w, HistoryKind.Structure, $"{Ko.IGa(c.Name)} {Ko.WaGwa(room.Name)} 함께 돌아왔다", room, new[] { c }, log: true);
        }
        foreach (var r in w.Robots.Robots.Where(r => r.Aboard == f)) { r.Aboard = null; r.Position = r.AboardAt; r.PreviousPosition = r.Position; }
        f.State = FragmentState.Lost; // 목록에서 빠진다 (방으로 돌아왔다)
        Fragments.Remove(f);
        Retrieved++;
        Version++;

        w.Paths.Invalidate();
        w.Board.RequestScan();
        float hours = (w.Tick - f.Since) / (float)SimTime.TicksPerHour;
        w.History.Add(w, HistoryKind.Structure, $"{Ko.EulReul(room.Name)} {hours:0}시간 만에 다시 붙였다 (임시 도킹) — 진공 · 전력·배관·환기 끊김", room, log: true);
        MarkLog.Add(room.Marks, w.Tick, "견인해 와서 다시 붙였다 (임시 도킹)");
        return true;
    }

    /// <summary>얼마나 망가졌나 0~1 (뚫린 외벽, 골조를 잃은 벽, 파손된 설비).</summary>
    public static float WreckScore(World w, Room room)
    {
        IEnumerable<(Cell, WallState)> walls = room.Fragment != null ? room.Fragment.WallCells
            : w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).Select(kv => (kv.Key, kv.Value));
        int n = 0, holes = 0, frames = 0;
        foreach (var (_, wall) in walls)
        {
            n++;
            if (wall.Integrity < 0.1f) holes++;
            if (wall.FrameLost) frames++;
        }
        var machines = room.Furniture.Where(f => f.Machine != null).Select(f => f.Machine!).ToList();
        float wrecked = machines.Count == 0 ? 0f : machines.Count(m => m.Has(FaultKind.Wrecked) || m.Condition < 0.2f) / (float)machines.Count;
        float s = n == 0 ? 0f : 0.6f * holes / n + 1.2f * frames / n;
        return Math.Clamp(s + 0.35f * wrecked, 0f, 1f);
    }
}
