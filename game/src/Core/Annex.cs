using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.10 증축 — 선체 바깥에 방을 새로 붙인다.
//
// 격자를 키우는 법: 아래(+Y)로만 줄을 덧붙인다 (ShipGrid.GrowRows). 칸 번호 = Y·너비 + X 라 아래에 줄을 붙이면 기존 칸의 번호와 좌표가
//   하나도 바뀌지 않는다 — 칸 번호를 쥔 배열(배 본체 3층 · 길찾기 캐시)은 뒤만 늘리고(BodySystem.GrowCells · Pathfinder.Grow),
//   번호를 키로 쓴 사전(칸 상태 · 원소 장 · 엎지른 것 · 기록 키 "hatch:번호")과 좌표를 쥔 목록(방 · 가구 · 문 · 벽 · 사람 · 배관 · 연결부)은 손댈 것이 없다.
//   스스로 크기를 보는 배열(움직임 · 물건 · 원소 · 폭발)은 다음 틱에 저절로 맞춘다. 옆으로 넓히면 모든 번호가, 위 · 왼쪽은 모든 좌표가 바뀐다 → 쓰지 않는다.
//   위 · 옆 · 오목한 곳은 처음부터 있던 빈칸(여백 3칸) 안에서만 — 실제로는 아래쪽 외벽이 증축 자리다.
//   결정은 승무원 제안 → 회의(또는 관찰자의 시나리오 기록) → 결정론 시뮬레이션이라, 저장(시드 + 기록)을 불러오거나 되감아도 같은 틱에 같은 줄이 붙는다.
// 왜: 간이침대에서 자는 사람(인원 증가 · 침실을 잃음) · 창고가 꽉 참 · 관찰자 지시 → 승무원이 안건을 낸다 (낸 사람이 있다).
// 주 컴퓨터: 자리 후보를 견주고(무게 중심이 덜 흔들리는 곳) · 무게 → 가속 · 연료 · 전력 · 공조 부하 · 자재(모자라면 원정 권고) · 공기(손이 가는 시간 · 날수)
//   · 위험(선외 노출) · 공정 순서를 다섯 칸 기록에 남긴다 — 회의 표에 신뢰만큼 무게가 실린다. 공사 중에는 운석 · 폭풍 예보에 선외 공사를 멈추고,
//   가압 전 기밀 시험 카드를 내고, 밤에는 소음 공사를 멈추고, 먼지가 가라앉기 전 용접을 말린다.
// 공정: ① 선외 골조(EVA · 드론 용접) ② 외판 ③ 기밀 시험 · 가압(옆 방 공기를 나눠 받는다) → 격자에 벽 · 바닥 · 방이 생기고 외벽에 문을 딴다
//   ④ 배선 · 배관(점검 뚜껑 · 테이프 → 첫 점등) ⑤ 내장 · 설비(비닐 막 · 먼지 · 침대) ⑥ 개통(모여서 축하 · 이름 · 연대기 · 옮겨 가 잔다).
// 위험: 운석이 골조 · 외판을 휜다(재작업) · 기밀 시험을 건너뛰면 약한 이음이 나중에 터진다(공사 구역 감압) · 소음이 옆 방 잠을 깨운다 · 먼지 + 용접 불꽃 = 분진 폭발.
// 결과: 무게 → 추력(가속) 감소 · 회피 · 전이 연료 증가 · 방 계통 전력 · 공기 부피 · 새 패널은 밝다가 낡는다 · 개통일 · 지은 사람 · 이름이 연대기에.

public enum AnnexStage { Frame, Plating, Pressure, Utilities, FitOut, Opening, Done }

/// <summary>증축 자리: 붙는 외벽 줄(Y0) 아래로 안쪽 칸 (X0..X1 × Depth) · 새 벽(옆 · 바닥) · 외벽에 낼 문.</summary>
public sealed class AnnexSite
{
    public int AttachRoom { get; set; } = -1;
    public int X0 { get; init; }
    public int X1 { get; init; }
    public int Y0 { get; init; }
    public int Depth { get; init; }
    public Cell Door { get; set; }
    /// <summary>격자를 몇 줄 늘려야 하나 (바깥 벽 아래로 우주 세 줄 — 선외 작업 칸).</summary>
    public int Grow { get; set; }
    public List<Cell> Inside { get; } = new();
    public List<Cell> Shell { get; } = new();
    public int Width => X1 - X0 + 1;
    public Vector2 Center => new((X0 + X1 + 1) * 0.5f, Y0 + 1 + Depth * 0.5f);
    public bool Covers(Cell c) => c.X >= X0 - 1 && c.X <= X1 + 1 && c.Y > Y0 && c.Y <= Y0 + Depth + 1;
    public Cell DoorInner => Door + new Cell(0, -1);
    public Cell DoorOuter => Door + new Cell(0, 1);
}

/// <summary>승무원이 낸 증축 안건 하나 — 회의를 거쳐 공정을 밟는다.</summary>
public sealed class AnnexPlan
{
    public int Id { get; init; }
    public int Proposer { get; init; } = -1;
    public RoomType Use { get; init; } = RoomType.Quarters;
    /// <summary>들일 설비 수 (침대 · 선반).</summary>
    public int Fixtures { get; init; }
    public string Title { get; set; } = "";
    public string Why { get; init; } = "";
    /// <summary>인원 변화 · 겪은 일 · 불편 · 지시.</summary>
    public string Source { get; init; } = "";
    public long Proposed { get; init; }
    /// <summary>제안 · 공사 · 개통 · 부결 · 중단.</summary>
    public string State { get; set; } = "제안";
    public AnnexStage Stage { get; set; }
    public long Decided { get; set; } = -1;
    public long StageSince { get; set; } = -1;
    public AnnexSite Site { get; set; } = null!;
    /// <summary>낸 사람이 바란 자리 (컴퓨터가 다른 자리를 권했을 때).</summary>
    public AnnexSite? Wanted { get; set; }
    public List<int> For { get; } = new();
    public List<int> Against { get; } = new();
    // ── 주 컴퓨터 ──
    public float Risk, Hours, Days, MassT, ThrustLoss, FuelMul = 1f, PowerKw, AirPct, Shift, WantedShift;
    public string Advice = "", Order = "", Shortage = "";
    public int Sign;
    public List<(ItemKind kind, int need, int have)> Need { get; } = new();
    public int CardId = -1;
    /// <summary>기밀 시험을 끝까지 했나 (null: 아직 정하지 않음).</summary>
    public bool? FullTest;
    // ── 공정 ──
    public float[] Frame = Array.Empty<float>(), Plate = Array.Empty<float>(), PlateQ = Array.Empty<float>(), Fit = Array.Empty<float>();
    /// <summary>그 칸에 붙어 일하는 사람 (−1 없음).</summary>
    public int[] BusyBy = Array.Empty<int>();
    /// <summary>운석에 휜 골조 (다시 세울 때까지 — 화면).</summary>
    public bool[] Bent = Array.Empty<bool>();
    public int[] FitBy = Array.Empty<int>();
    public float Pressure, Utilities, Sheet;
    public int PressureBy = -1, WireBy = -1, SheetBy = -1;
    public List<Cell> FixtureSpots { get; } = new();
    public int RoomId = -1, DoorId = -1;
    public long Enclosed = -1, FirstLight = -1, OpenUntil = -1, Opened = -1;
    public string? Name;
    public string NameSource = "";
    public int MeteorHits, Reworks, Leaks, Seams, DustBlasts, DroneSorties, Woken, Halts;
    /// <summary>사람 → 손댄 시간 (지은 사람 · 이름 · 연대기).</summary>
    public Dictionary<int, float> Hands { get; } = new();
    public List<int> Celebrated { get; } = new();
    public List<int> MovedIn { get; } = new();
    public string UseName => Use == RoomType.Storage ? "창고" : "침실";
    public string FixtureName => Use == RoomType.Storage ? "선반" : "침대";
}

public sealed class AnnexStats
{
    public int Proposed, Passed, Rejected, Opened, Stopped, Grown, MeteorHits, Reworks, Leaks, Seams, DustBlasts, DroneWelds, EvaTrips, Woken, MovedIn,
        Celebrated, Advices, Halts, Members, Plates, Fixtures, Waits, DustWaits;
}

public sealed partial class AnnexSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7349 + 211));
    public List<AnnexPlan> Plans { get; } = new();
    public AnnexStats Stats { get; } = new();
    /// <summary>화면: 격자 · 방 · 공정이 바뀐 횟수.</summary>
    public int Version { get; private set; }
    /// <summary>시험용: 안건을 저절로 내지 않는다.</summary>
    public static bool ThinkOff { get; set; }
    /// <summary>관찰자 지시 (시나리오 기록 — 다음 생각 때 안건이 된다).</summary>
    public bool Directive { get; set; }
    private int _next = 1;
    private long _nextThink = SimTime.Hours(20), _homelessSince = -1;
    private readonly Dictionary<string, long> _cool = new();
    private readonly Dictionary<int, long> _woke = new();
    private float _mass0 = -1f;
    /// <summary>개통한 증축이 더한 무게 (t).</summary>
    public float AddedMassT { get; private set; }
    /// <summary>운석 · 폭풍 예보 · 비상: 선외 공사를 멈췄다.</summary>
    public bool EvaHalted { get; private set; }
    public string HaltWhy { get; private set; } = "";
    /// <summary>밤 — 주 컴퓨터가 소음 공사를 멈췄다.</summary>
    public bool QuietHours { get; private set; }
    /// <summary>지금 손을 대고 있는 사람 + 드론 (소음 · 화면).</summary>
    public int Working { get; private set; }

    public AnnexSystem(World w) => _w = w;

    public AnnexPlan? Active { get { foreach (var p in Plans) if (p.State == "공사") return p; return null; } }
    public AnnexPlan? Pending { get { foreach (var p in Plans) if (p.State == "제안") return p; return null; } }
    private CrewMember? Crew(int id) => id >= 0 && id < _w.Crew.Count ? _w.Crew[id] : null;
    private Room? RoomOf(int id) => id >= 0 && id < _w.Ship.Rooms.Count ? _w.Ship.Rooms[id] : null;

    /// <summary>배의 무게 배율 (추력 ÷ · 연료 ×) — 개통한 증축만큼.</summary>
    public float MassMul => _mass0 > 0f ? 1f + AddedMassT / _mass0 : 1f;
    public float FuelMul => MassMul;

    /// <summary>그 방이 증축으로 생겼나 (개통했거나 공사 중).</summary>
    public AnnexPlan? PlanOf(Room r)
    {
        foreach (var p in Plans) if (p.RoomId == r.Id && p.State is "공사" or "개통") return p;
        return null;
    }

    /// <summary>새 패널의 밝기 1 → 0 (개통 뒤 열흘 남짓에 걸쳐 낡는다 — 화면).</summary>
    public float Fresh(Room r)
    {
        if (PlanOf(r) is not AnnexPlan p || p.Enclosed < 0) return 0f;
        float days = (_w.Tick - p.Enclosed) / (float)SimTime.TicksPerDay;
        return MathF.Exp(-days / 12f);
    }

    // ─────────────────────────────── 무게 ───────────────────────────────

    /// <summary>배 무게 (t): 바닥 칸 1.5t · 벽 칸 2t · 설비 무게.</summary>
    public float ShipMassT()
    {
        var ship = _w.Ship;
        float m = 0f;
        foreach (var r in ship.Rooms) if (!r.Detached) m += r.Cells.Count * 1.5f;
        foreach (var _ in ship.Walls) m += 2f;
        foreach (var f in ship.Furniture) if (!f.Stowed && !f.Room.Detached) m += RoomPlanSystem.Weight(f) / 1000f;
        return m;
    }

    private void EnsureMass() { if (_mass0 < 0f) _mass0 = ShipMassT(); }

    /// <summary>무게 중심 (칸 좌표).</summary>
    private Vector2 MassCenter()
    {
        var ship = _w.Ship;
        var sum = Vector2.Zero;
        float m = 0f;
        foreach (var r in ship.Rooms) if (!r.Detached) { sum += r.Center * r.Cells.Count * 1.5f; m += r.Cells.Count * 1.5f; }
        return m > 0f ? sum / m : Vector2.Zero;
    }

    private static float SiteMassT(AnnexSite s, int fixtures) => s.Inside.Count * 1.5f + s.Shell.Count * 2f + fixtures * 0.05f;

    /// <summary>그 자리에 지으면 무게 중심이 얼마나 옮겨 가나 (칸).</summary>
    private float ShiftOf(AnnexSite s, int fixtures)
    {
        EnsureMass();
        float m = SiteMassT(s, fixtures), M = _mass0 + AddedMassT;
        var c = MassCenter();
        return ((s.Center - c) * m / (M + m)).Length();
    }

    // ─────────────────────────────── 자리 찾기 ───────────────────────────────

    private static bool Hullish(RoomType t) => t is RoomType.Engine or RoomType.Reactor or RoomType.Cooling or RoomType.Airlock or RoomType.Power;

    /// <summary>증축할 수 있는 자리들 (붙는 방의 아래 외벽). 쓰임에 맞는 방부터.</summary>
    public List<AnnexSite> Sites(RoomType use, int fixtures)
    {
        var ship = _w.Ship;
        var list = new List<AnnexSite>();
        int wantW = use == RoomType.Storage ? Math.Clamp(fixtures * 2 + 1, 4, 6) : Math.Clamp(fixtures * 2 + 1, 4, 8);
        foreach (var r in ship.Rooms)
        {
            if (r.Detached || r.OffLimits || r.Abandoned || r.Cells.Count == 0 || Hullish(r.Kind)) continue;
            int y = r.MaxY + 1;
            var doors = r.Cells.Where(c => c.Y == r.MaxY && ship.IsOpenFloor(c) && ship.IsWalkable(c)).Select(c => c.X).OrderBy(x => Math.Abs(2 * x - r.MinX - r.MaxX)).ThenBy(x => x).ToList();
            AnnexSite? found = null;
            for (int wdt = wantW; wdt >= 3 && found == null; wdt--)
            for (int depth = 3; depth >= 2 && found == null; depth--)
                foreach (int dx in doors)
                {
                    for (int k = 0; k < wdt && found == null; k++)
                    {
                        int x0 = dx - (wdt - 1) / 2 + (k % 2 == 0 ? k / 2 : -(k + 1) / 2);
                        if (dx < x0 || dx > x0 + wdt - 1) continue;
                        found = Fit(r, x0, x0 + wdt - 1, y, depth, dx);
                    }
                    if (found != null) break;
                }
            if (found != null) list.Add(found);
        }
        int Pref(Room r) => use == RoomType.Storage
            ? (r.Kind == RoomType.Storage ? 3 : r.Kind == RoomType.Corridor ? 2 : r.Kind == RoomType.Workshop ? 1 : 0)
            : (RoomUseSystem.Actual(r) == RoomType.Quarters || r.Kind == RoomType.Quarters ? 3 : r.Kind == RoomType.Corridor ? 2 : r.Kind is RoomType.Lounge or RoomType.Mess ? 1 : 0);
        return list.OrderByDescending(s => Pref(ship.Rooms[s.AttachRoom])).ThenByDescending(s => s.Width).ThenBy(s => s.AttachRoom).ToList();
    }

    private AnnexSite? Fit(Room r, int x0, int x1, int y, int depth, int doorX, AnnexPlan? self = null)
    {
        var ship = _w.Ship;
        var g = ship.Grid;
        bool Void(int x, int yy) => g.Kind(new Cell(x, yy)) == TileKind.Void && !_w.Dock.Blocks(new Cell(x, yy)); // v18.5 붙은 배 자리는 비켜
        // 붙는 외벽: 선체 벽 (문 · 해치가 아닌)
        for (int x = x0 - 1; x <= x1 + 1; x++)
        {
            var c = new Cell(x, y);
            if (g.Kind(c) != TileKind.Wall || ship.WallAt(c) is not { IsHull: true } ws || ws.Breach > 0f || ws.FrameLost) return null;
        }
        if (ship.RoomAt(new Cell(doorX, y - 1)) != r) return null;
        // 자리: 우주 (옆으로 한 칸 여유 · 아래로 한 칸 더)
        for (int yy = y + 1; yy <= y + depth + 2; yy++)
        for (int x = x0 - 2; x <= x1 + 2; x++)
            if (!Void(x, yy)) return null;
        // 해치 · 바깥 설비 · 방열판을 막지 않게
        foreach (var d in ship.Doors)
            if (d.IsExternal && !d.Removed && d.Cell.X >= x0 - 3 && d.Cell.X <= x1 + 3 && d.Cell.Y >= y - 1 && d.Cell.Y <= y + depth + 2) return null;
        foreach (var f in _w.Exterior.All)
            if (f.Anchor.Y == y && f.Out.Y > 0 && f.Anchor.X >= x0 - 2 && f.Anchor.X <= x1 + 2) return null;
        foreach (var b in _w.Piping.Branches)
            foreach (var rc in b.Radiator)
                if (rc.X >= x0 - 2 && rc.X <= x1 + 2 && rc.Y >= y && rc.Y <= y + depth + 2) return null;
        foreach (var p in Plans)
            if (p != self && p.State is "공사" or "제안" && p.Site != null && Math.Abs(p.Site.Y0 - y) <= depth + 2 && p.Site.X1 + 2 >= x0 - 2 && p.Site.X0 - 2 <= x1 + 2) return null;
        var s = new AnnexSite { AttachRoom = r.Id, X0 = x0, X1 = x1, Y0 = y, Depth = depth, Door = new Cell(doorX, y) };
        for (int yy = y + 1; yy <= y + depth; yy++)
        for (int x = x0; x <= x1; x++) s.Inside.Add(new Cell(x, yy));
        // 새 벽: 왼쪽 위→아래, 바닥 왼→오, 오른쪽 아래→위 (골조가 둘레를 따라 차오른다)
        for (int yy = y + 1; yy <= y + depth + 1; yy++) s.Shell.Add(new Cell(x0 - 1, yy));
        for (int x = x0; x <= x1; x++) s.Shell.Add(new Cell(x, y + depth + 1));
        for (int yy = y + depth + 1; yy >= y + 1; yy--) s.Shell.Add(new Cell(x1 + 1, yy));
        int bottom = y + depth + 1;
        s.Grow = Math.Max(0, bottom + 4 - g.Height);
        return s;
    }

    /// <summary>주 컴퓨터의 자리 고르기: 쓰임에 맞고 무게 중심이 덜 흔들리는 곳.</summary>
    private AnnexSite? BestSite(List<AnnexSite> sites, int fixtures, RoomType use)
    {
        AnnexSite? best = null;
        float bs = float.MaxValue;
        foreach (var s in sites)
        {
            var r = _w.Ship.Rooms[s.AttachRoom];
            bool fits = use == RoomType.Storage ? r.Kind is RoomType.Storage or RoomType.Corridor or RoomType.Workshop
                : RoomUseSystem.Actual(r) == RoomType.Quarters || r.Kind is RoomType.Quarters or RoomType.Corridor or RoomType.Lounge or RoomType.Mess;
            float score = ShiftOf(s, fixtures) * 10f + (fits ? 0f : 1.5f) - 0.05f * s.Width;
            if (score < bs - 1e-4f) { bs = score; best = s; }
        }
        return best;
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick >= _nextThink)
        {
            _nextThink = w.Tick + SimTime.Hours(1);
            if (!ThinkOff || Directive) Think();
            // 약한 이음은 개통 뒤에도 며칠 동안 터질 수 있다
            foreach (var q in Plans) if (q.Enclosed >= 0 && q.State is "공사" or "개통" && w.Tick - q.Enclosed < SimTime.TicksPerDay * 5) Seams(q);
        }
        var p = Active;
        if (p == null) { Working = 0; EvaHalted = false; QuietHours = false; return; }
        if (!Valid(p)) { Stop(p, "붙일 방을 쓸 수 없게 됐다"); return; }
        UpdateHalt(p);
        if (w.Tick % 300 == 0) { Tend(); RefreshDoor(p); }
        DroneWork(p, dt);
        int n = 0;
        foreach (var kv in _doing) if (kv.Value.plan == p.Id && kv.Value.kind != AnnexJob.Celebrate && Crew(kv.Key) is CrewMember wc && wc.Pose == Pose.Working) n++;
        Working = n + _drone.Count;
        CheckStage(p);
        if (w.Tick % SimTime.Minutes(10) < World.SystemInterval) Noise(p);
        if (w.Tick % SimTime.TicksPerHour < World.SystemInterval) EnsureSpace(p);
    }

    /// <summary>문 자리 다시 보기: 공사 중에 위 방이 칸막이로 나뉘거나 길이 막히면 다른 칸으로 (붙는 방도 따라 바뀐다).</summary>
    private void RefreshDoor(AnnexPlan p)
    {
        var w = _w;
        var ship = w.Ship;
        var s = p.Site;
        if (p.Enclosed >= 0) return;
        if (ship.IsWalkable(s.DoorInner) && ship.RoomAt(s.DoorInner) is Room r0 && !r0.OffLimits && ship.WallAt(s.Door) is { IsHull: true })
        {
            if (s.AttachRoom != r0.Id) { s.AttachRoom = r0.Id; Version++; }
            return;
        }
        foreach (int x in Enumerable.Range(s.X0, s.Width).OrderBy(x => Math.Abs(x - s.Door.X)).ThenBy(x => x))
        {
            var inner = new Cell(x, s.Y0 - 1);
            if (!ship.IsOpenFloor(inner) || !ship.IsWalkable(inner) || ship.RoomAt(inner) is not Room r || r.OffLimits) continue;
            if (ship.WallAt(new Cell(x, s.Y0)) is not { IsHull: true }) continue;
            s.Door = new Cell(x, s.Y0);
            s.AttachRoom = r.Id;
            Version++;
            w.Log.Add(w.Tick, LogKind.Ship, $"증축 문 자리를 옮긴다 — {r.Name} 쪽 ({x}칸 · 원래 자리가 막혔다)");
            return;
        }
    }

    private bool Valid(AnnexPlan p)
    {
        var r = RoomOf(p.Site.AttachRoom);
        return r != null && !r.Detached && r.Jettison == null;
    }

    /// <summary>선외 공사를 멈출 일: 다가오는 운석 경보 · 우주 폭풍 · 비상.</summary>
    private void UpdateHalt(AnnexPlan p)
    {
        var w = _w;
        string why = "";
        if (w.Sensors.Incoming.Any(m => m.Warned != WarnLevel.None)) why = "운석 경보";
        else if (w.Ambience.StormPower > 0.3f) why = "우주 폭풍";
        else if (Crisis.Level(w) >= CrisisLevel.Emergency) why = "비상";
        bool halt = why != "";
        if (halt && !EvaHalted && p.Stage <= AnnexStage.Plating)
        {
            p.Halts++;
            Stats.Halts++;
            var room = RoomOf(p.Site.AttachRoom);
            w.Automation.Book.Add(ActKind.Advice, room, $"증축 선외 공사 중 {why}", "밖에 있는 사람 · 드론이 맞는다", "선외 공사 중지 · 에어락으로",
                "골조에서 손을 떼고 들어오세요", "annex:halt", SimTime.Minutes(20));
            if (w.Crew.Any(c => !c.Dead && c.Outside && _doing.ContainsKey(c.Id)))
                w.Automation.Speak.Announce(w.Automation.Voice.Style($"증축 선외 작업 중지 — {why}. 에어락으로 돌아오라"), room, 2);
        }
        EvaHalted = halt;
        HaltWhy = why;
        float hour = SimTime.HourOfDay(w.Tick);
        QuietHours = w.Automation.Present && w.Automation.MainOnline && (hour >= 22f || hour < 6f);
    }

    /// <summary>프레임 칸을 선외 작업 칸으로 (조각이 떠난 자리를 지우다 함께 지워졌으면 다시).</summary>
    private void EnsureSpace(AnnexPlan p)
    {
        if (p.Stage > AnnexStage.Pressure || p.Enclosed >= 0) return;
        var paths = _w.Paths;
        bool add = false;
        foreach (var c in p.Site.Inside.Concat(p.Site.Shell)) if (paths.MooredCells.Add(c)) add = true;
        if (add) paths.Invalidate();
    }

    // ─────────────────────────────── 제안 ───────────────────────────────

    private List<CrewMember> Adults() => _w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Away).ToList();

    /// <summary>제 침대가 없는 사람 (간이침대 · 침대 없음 · 침대가 쓸 수 없는 방에).</summary>
    public List<CrewMember> Homeless() => _w.Crew.Where(c => !c.Dead && !c.Away
        && (c.Bed == null || c.Bed.Stowed || c.Bed.Type == FurnitureType.Cot || c.Bed.Room.OffLimits || c.Bed.Room.Abandoned)).ToList();

    /// <summary>창고 선반이 얼마나 찼나 (0~1).</summary>
    public float StorageFill()
    {
        int have = 0, cap = 0;
        foreach (var f in _w.Ship.Furniture)
            if (f.Type == FurnitureType.Shelf && f.Storage != null && !f.Stowed && !f.Room.Detached) { have += f.Storage.Total; cap += f.Storage.Capacity; }
        return cap > 0 ? have / (float)cap : 0f;
    }

    private void Think()
    {
        var w = _w;
        if (w.Tick < SimTime.TicksPerDay / 2 && !Directive) return;
        if (Crisis.Level(w) >= CrisisLevel.Emergency || w.Command.Active) return;
        if (Active != null || Pending != null) return;
        var adults = Adults();
        if (adults.Count < 2) return;
        var homeless = Homeless();
        if (homeless.Count > 0) { if (_homelessSince < 0) _homelessSince = w.Tick; }
        else _homelessSince = -1;
        AnnexPlan? draft = null;
        if (homeless.Count > 0 && (w.Tick - _homelessSince >= SimTime.Hours(6) || Directive)) draft = QuartersDraft(homeless, adults);
        else if (StorageFill() > 0.9f) draft = StorageDraft(adults);
        else if (Directive) draft = QuartersDraft(homeless, adults);
        if (draft == null) return;
        string key = $"{draft.Use}";
        if (!Directive && _cool.TryGetValue(key, out var until) && w.Tick < until) return;
        Directive = false;
        Propose(draft);
    }

    private AnnexPlan? QuartersDraft(List<CrewMember> homeless, List<CrewMember> adults)
    {
        var w = _w;
        int alive = w.Crew.Count(c => !c.Dead);
        int beds = w.Ship.Furniture.Count(f => !f.Stowed && !f.Room.Detached && f.Type == FurnitureType.Bed);
        int n = Math.Clamp(homeless.Count, 2, 4);
        var by = homeless.Where(c => !c.IsChild && !c.Away).OrderByDescending(c => c.Traits.Diligence + 0.5f * c.Traits.Sociability).ThenBy(c => c.Id).FirstOrDefault()
                 ?? adults.OrderByDescending(c => c.Role == CrewRole.Engineer ? 1 : 0).ThenByDescending(c => c.Traits.Diligence).ThenBy(c => c.Id).First();
        var sites = Sites(RoomType.Quarters, n);
        if (sites.Count == 0) { NoSite("침실"); return null; }
        // 낸 사람은 제 침실(또는 가장 북적이는 침실) 아래를 바란다
        var mine = by.HomeBed?.Room ?? (by.Bed is { Type: FurnitureType.Bed } b ? b.Room : null);
        var wanted = sites.FirstOrDefault(s => mine != null && s.AttachRoom == mine.Id) ?? sites[0];
        string src = alive > w.StartCrew ? "인원 변화" : homeless.Any(c => c.HomeBed != null) ? "겪은 일" : Directive ? "지시" : "불편";
        string why = homeless.Count == 0 ? "관찰자 지시 — 침실을 하나 더"
            : $"{homeless.Count}명이 간이침대 · 바닥에서 잔다 (침대 {beds} · 사람 {alive})" + (alive > w.StartCrew ? $" · 처음 {w.StartCrew}명에서 늘었다" : "");
        return new AnnexPlan
        {
            Proposer = by.Id, Use = RoomType.Quarters, Fixtures = n, Site = wanted, Proposed = w.Tick, Source = src, Why = why,
            Title = $"{w.Ship.Rooms[wanted.AttachRoom].Name} 아래 증축 — 침실 (침대 {n})",
        };
    }

    private AnnexPlan? StorageDraft(List<CrewMember> adults)
    {
        var w = _w;
        var by = adults.OrderByDescending(c => c.Habits.Contains(Habit.Hoarder) ? 1 : 0).ThenByDescending(c => c.Role is CrewRole.Technician ? 1 : 0)
            .ThenByDescending(c => c.Traits.Diligence).ThenBy(c => c.Id).First();
        var sites = Sites(RoomType.Storage, 2);
        if (sites.Count == 0) { NoSite("창고"); return null; }
        return new AnnexPlan
        {
            Proposer = by.Id, Use = RoomType.Storage, Fixtures = 2, Site = sites[0], Proposed = w.Tick, Source = "불편",
            Why = $"선반이 {StorageFill() * 100:0}% 찼다 — 놓을 데가 없다", Title = $"{w.Ship.Rooms[sites[0].AttachRoom].Name} 아래 증축 — 창고",
        };
    }

    private void NoSite(string what)
    {
        var w = _w;
        _cool[what] = w.Tick + SimTime.TicksPerDay;
        w.Automation.Book.Add(ActKind.Advice, null, $"{what} 증축 자리를 찾음", "아래쪽 외벽에 빈 우주가 없다 (해치 · 방열판 · 바깥 설비 · 다른 공사)", "증축 보류",
            "다른 방을 나눠 쓰세요", "annex:nosite", SimTime.TicksPerDay);
    }

    /// <summary>안건을 올린다 (컴퓨터가 조언을 붙인다 → 다음 정기 회의).</summary>
    public AnnexPlan Propose(AnnexPlan d)
    {
        var w = _w;
        var p = new AnnexPlan
        {
            Id = _next++, Proposer = d.Proposer, Use = d.Use, Fixtures = d.Fixtures, Site = d.Site, Proposed = w.Tick, Source = d.Source, Why = d.Why, Title = d.Title,
        };
        Plans.Add(p);
        if (Plans.Count > 12) Plans.RemoveAt(0);
        Stats.Proposed++;
        Advise(p);
        if (Crew(p.Proposer) is CrewMember by)
        {
            w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(by.Name)} 증축 안건을 냈다: {p.Title} — {p.Why} ({p.Source})" + (p.Advice != "" ? $" · 주 컴퓨터: {p.Advice}" : ""), by.Id);
            by.Say(w, Persona.Say(by, Pitch(p)));
            Life.Diary(w, by, Persona.Say(by, $"회의에 증축 안건을 냈다 — {p.Title}. {p.Why}."));
        }
        Version++;
        return p;
    }

    private string Pitch(AnnexPlan p) => p.Use == RoomType.Storage ? "선반이 꽉 찼다 — 밖에 창고를 하나 붙이자" : "간이침대는 이제 그만 — 밖에 침실을 하나 붙이자";

    // ─────────────────────────────── 주 컴퓨터의 조언 ───────────────────────────────

    private static ItemKind[] Kinds => new[] { ItemKind.Structure, ItemKind.Plate, ItemKind.Sealant, ItemKind.Cable, ItemKind.Hose };

    public static int NeedOf(AnnexPlan p, ItemKind k) => k switch
    {
        ItemKind.Structure => (p.Site.Shell.Count + 3) / 4 + (p.Use == RoomType.Quarters ? p.Fixtures : 0),
        ItemKind.Plate => (p.Site.Shell.Count + 1) / 2 + (p.Use == RoomType.Storage ? p.Fixtures : 0),
        ItemKind.Sealant => 1,
        ItemKind.Cable => 2,
        ItemKind.Hose => 1,
        _ => 0,
    };

    /// <summary>자리 · 무게 · 가속 · 연료 · 전력 · 공조 · 자재 · 공기 · 위험 · 공정 순서 → 다섯 칸 기록.</summary>
    private void Advise(AnnexPlan p)
    {
        var w = _w;
        EnsureMass();
        var sites = Sites(p.Use, p.Fixtures);
        var best = BestSite(sites, p.Fixtures, p.Use);
        bool present = w.Automation.Present && w.Automation.MainOnline;
        p.WantedShift = ShiftOf(p.Site, p.Fixtures);
        string where = "";
        if (present && best != null && best.AttachRoom != p.Site.AttachRoom && ShiftOf(best, p.Fixtures) < p.WantedShift - 0.02f)
        {
            p.Wanted = p.Site;
            p.Site = best;
            p.Title = $"{w.Ship.Rooms[best.AttachRoom].Name} 아래 증축 — {p.UseName}" + (p.Use == RoomType.Quarters ? $" (침대 {p.Fixtures})" : "");
            where = $" · 자리: {w.Ship.Rooms[p.Wanted.AttachRoom].Name} 아래보다 {w.Ship.Rooms[best.AttachRoom].Name} 아래가 무게 중심이 덜 흔들린다 ({p.WantedShift:0.00} → {ShiftOf(best, p.Fixtures):0.00}칸)";
        }
        var s = p.Site;
        var room = w.Ship.Rooms[s.AttachRoom];
        p.Shift = ShiftOf(s, p.Fixtures);
        p.MassT = SiteMassT(s, p.Fixtures);
        float M = _mass0 + AddedMassT;
        p.ThrustLoss = 1f - M / (M + p.MassT);
        p.FuelMul = (M + p.MassT) / M;
        p.PowerKw = PowerGrid.RoomSystemsKw + (p.Use == RoomType.Quarters ? 0.04f * p.Fixtures : 0.02f);
        int vol = w.Ship.Rooms.Where(r => !r.Detached).Sum(r => r.Volume);
        p.AirPct = vol > 0 ? s.Inside.Count * 100f / vol : 0f;
        p.Need.Clear();
        var shortList = new List<string>();
        foreach (var k in Kinds)
        {
            int need = NeedOf(p, k), have = w.Ship.CountStored(k);
            p.Need.Add((k, need, have));
            if (have < need) shortList.Add($"{ItemKinds.Name(k)} {need - have}");
        }
        p.Shortage = string.Join(" · ", shortList);
        float evaH = s.Shell.Count * (FrameHours + PlateHours);
        p.Hours = evaH + TestHours + WireHours + SheetHours + p.Fixtures * FitHours;
        p.Days = MathF.Max(0.5f, p.Hours / 7f + (shortList.Count > 0 ? 2f : 0f));
        var crew = Adults();
        float skill = crew.Count == 0 ? 0f : crew.Max(c => c.SkillLevel(Skill.Mechanics) * 0.6f + c.SkillLevel(Skill.Engineering) * 0.4f);
        float zone = PropulsionSystem.ZoneDensity(w.Propulsion.Zone);
        p.Risk = Math.Clamp(0.04f + evaH * 0.012f * zone + (shortList.Count > 0 ? 0.05f : 0f) + (w.Drones.Drones.Any(d => d.Operational) ? -0.02f : 0.03f) - 0.08f * skill, 0.02f, 0.9f);
        p.Order = $"1) 선외 골조 (EVA{(w.Drones.Drones.Any(d => d.Operational) ? " · 드론 용접" : "")}) → 2) 외판 → 3) 기밀 시험 · 가압 → 4) 배선 · 배관 → 5) 내장 · {p.FixtureName} {p.Fixtures} → 6) 개통";
        int homeless = Homeless().Count;
        p.Sign = p.Risk > 0.45f || w.Propulsion.Zone == ZoneKind.Debris ? -1 : (homeless >= 2 || p.Use == RoomType.Storage) && p.ThrustLoss < 0.05f && shortList.Count == 0 ? 1 : 0;
        string judge = $"자리 {room.Name} 아래 {s.Width}×{s.Depth}칸{(s.Grow > 0 ? $" (격자 +{s.Grow}줄)" : "")} · 무게 +{p.MassT:0}t → 가속 −{p.ThrustLoss * 100:0.#}% · 회피·전이 연료 +{(p.FuelMul - 1f) * 100:0.#}%"
                       + $" · 전력 +{p.PowerKw:0.##}kW · 공조 부피 +{p.AirPct:0.#}% · 공사 {p.Hours:0.#}시간(약 {p.Days:0.#}일) · 위험 {p.Risk * 100:0}% (선외 {evaH:0.#}시간)"
                       + (p.Shortage != "" ? $" · 모자람: {p.Shortage} — 원정 권고" : " · 자재 있음") + where;
        if (!present) return; // 컴퓨터가 멎었다 — 조언 없이 회의로
        var act = w.Automation.Book.Add(ActKind.Advice, room, $"{Crew(p.Proposer)?.Name ?? "?"}의 증축 안건: {p.Title}", judge, "공정 순서: " + p.Order,
            p.Sign < 0 ? "다시 생각해 주세요" : "회의에서 정해 주세요");
        if (act == null) return;
        Stats.Advices++;
        p.Advice = judge + (p.Sign > 0 ? " · 권한다" : p.Sign < 0 ? " · 권하지 않는다" : "") + $" · 순서: {p.Order}";
    }

    public const float FrameHours = 0.35f, PlateHours = 0.25f, TestHours = 0.8f, QuickTestHours = 0.3f, WireHours = 1.4f, SheetHours = 0.15f, FitHours = 0.6f;

    // ─────────────────────────────── 회의 ───────────────────────────────

    /// <summary>이 사람이 이 증축 안건에 대해 처음 드는 생각.</summary>
    public (float s, string why) Opinion(CrewMember c, AnnexPlan p)
    {
        var w = _w;
        if (c.Id == p.Proposer) return (0.9f, Pitch(p));
        float s = 0.05f;
        string why = "배가 넓어진다";
        if (Crew(p.Proposer) is CrewMember by) s += 0.35f * c.AffinityTo(by);
        bool cot = c.Bed == null || c.Bed.Type == FurnitureType.Cot || c.Bed.Stowed;
        if (p.Use == RoomType.Quarters)
        {
            if (cot) { s += 0.6f; why = "간이침대에서 허리가 아프다 — 내 침대가 생긴다"; }
            else if (c.Bed?.Room is Room q && w.Crew.Count(o => !o.Dead && o.Bed?.Room == q) >= 6) { s += 0.18f; why = "침실이 북적인다"; }
        }
        else if (c.Habits.Contains(Habit.Hoarder)) { s += 0.3f; why = "물건 둘 데가 생긴다"; }
        if (c.Value == CrewValue.Safety) { s -= 1.0f * p.Risk; if (p.Risk > 0.2f) why = $"선외 공사는 위험하다 (위험 {p.Risk * 100:0}%)"; }
        if (c.Role is CrewRole.Pilot or CrewRole.Engineer && p.ThrustLoss > 0.01f) { s -= 4f * p.ThrustLoss; if (!cot) why = $"배가 무거워진다 (가속 −{p.ThrustLoss * 100:0.#}%)"; }
        if (w.EvaRisk.RefusesFor(c, false)) { s -= 0.15f; if (!cot) why = "밖에 매달리는 일은 싫다"; }
        if (p.Shortage != "" && c.Value == CrewValue.Efficiency) { s -= 0.2f; why = $"자재가 모자라다 ({p.Shortage})"; }
        if (p.Advice != "" && p.Sign != 0)
        {
            float trust = w.Automation.Trusts.Of(c);
            s += 0.25f * p.Sign * trust;
            if (p.Sign < 0 && trust > 0.6f && s <= 0f) why = $"컴퓨터가 위험하다고 한다 (위험 {p.Risk * 100:0}%)";
        }
        if (c.Value == CrewValue.Freedom) s += 0.05f;
        return (s, why);
    }

    /// <summary>정기 회의: 올라온 증축 안건을 토론 · 표결한다.</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        var w = _w;
        var p = Pending;
        if (p == null) return;
        var voters = attendees.Where(c => !w.Society.OnProbation(c)).ToList();
        if (voters.Count < 2) voters = attendees;
        if (voters.Count < 2) return;
        if (!Valid(p) || Fit(w.Ship.Rooms[p.Site.AttachRoom], p.Site.X0, p.Site.X1, p.Site.Y0, p.Site.Depth, p.Site.Door.X, p) is not AnnexSite fresh)
        {
            p.State = "중단"; Stats.Stopped++; Version++;
            return;
        }
        p.Site = fresh;
        var item = new AgendaItem
        {
            Title = $"증축: {p.Title}", Topic = "annex:" + p.Id, Evidence = $"{p.Why} ({p.Source})",
            Computer = p.Advice != "" ? $"주 컴퓨터: {p.Advice}" : null, ComputerSign = p.Sign,
        };
        var (yes, no) = w.Meetings.Debate(voters, c => Opinion(c, p), c => c.SkillLevel(Skill.Mechanics) * 0.6f + c.SkillLevel(Skill.Engineering) * 0.4f, item, chair);
        bool pass = yes.Count > no.Count || yes.Count == no.Count && Opinion(chair, p).s > 0f;
        item.Passed = pass;
        item.Outcome = pass ? "짓기로 했다" : "짓지 않기로 했다";
        rec.Items.Add(item);
        p.For.AddRange(yes.Select(c => c.Id));
        p.Against.AddRange(no.Select(c => c.Id));
        p.Decided = w.Tick;
        var room = w.Ship.Rooms[p.Site.AttachRoom];
        w.Meetings.Record(item.Title, "annex", room.Id, chair, yes, no, "선외 공사");
        w.Meetings.Split(yes, no);
        w.History.Add(w, HistoryKind.Decision, $"회의: {p.Title}? 찬성 {yes.Count} · 반대 {no.Count} → {(pass ? "짓는다" : "안 짓는다")}"
            + (item.FlippedBy != null ? $" ({item.FlippedBy}의 설득으로 뒤집혔다)" : "") + (p.Advice != "" ? $" · 컴퓨터 위험 {p.Risk * 100:0}% · 가속 −{p.ThrustLoss * 100:0.#}%" : ""),
            room, voters, log: true);
        if (pass) { Stats.Passed++; Start(p); }
        else
        {
            p.State = "부결";
            Stats.Rejected++;
            _cool[$"{p.Use}"] = w.Tick + SimTime.TicksPerDay * 3;
            if (Crew(p.Proposer) is CrewMember by) by.Needs.Stress = MathF.Min(1f, by.Needs.Stress + 0.04f);
        }
        Version++;
    }

    // ─────────────────────────────── 공사 시작 ───────────────────────────────

    /// <summary>회의가 정했다: 격자를 키우고(필요하면) · 선외 작업 칸을 열고 · 골조부터.</summary>
    public void Start(AnnexPlan p)
    {
        var w = _w;
        var s = p.Site;
        var room = w.Ship.Rooms[s.AttachRoom];
        p.State = "공사";
        p.Stage = AnnexStage.Frame;
        p.StageSince = w.Tick;
        if (p.Decided < 0) p.Decided = w.Tick;
        if (s.Grow > 0) GrowGrid(s.Grow);
        int n = s.Shell.Count;
        p.Frame = new float[n]; p.Plate = new float[n]; p.PlateQ = new float[n];
        p.BusyBy = Enumerable.Repeat(-1, n).ToArray();
        p.Bent = new bool[n];
        // 설비 자리: 먼 줄을 따라 한 칸 걸러 (문 아래 줄은 비운다 — 드나드는 길)
        p.FixtureSpots.Clear();
        int fy = s.Y0 + s.Depth;
        for (int x = s.X0; x <= s.X1 && p.FixtureSpots.Count < p.Fixtures; x += 2)
        {
            if (x == s.Door.X) { x--; continue; }
            p.FixtureSpots.Add(new Cell(x, fy));
        }
        if (p.FixtureSpots.Count < p.Fixtures)
            for (int x = s.X1; x >= s.X0 && p.FixtureSpots.Count < p.Fixtures; x--)
            {
                var c = new Cell(x, fy);
                if (x != s.Door.X && !p.FixtureSpots.Contains(c) && !p.FixtureSpots.Any(f => Math.Abs(f.X - x) <= 0)) p.FixtureSpots.Add(c);
            }
        p.Fit = new float[p.FixtureSpots.Count];
        p.FitBy = Enumerable.Repeat(-1, p.FixtureSpots.Count).ToArray();
        foreach (var c in s.Inside.Concat(s.Shell)) w.Paths.MooredCells.Add(c);
        w.Paths.Invalidate();
        w.Log.Add(w.Tick, LogKind.Ship, $"증축 공사 시작: {p.Title} — {p.Order}" + (p.Advice != "" ? $" (컴퓨터 예상 {p.Hours:0.#}시간 · 약 {p.Days:0.#}일)" : ""));
        if (w.Automation.Present && w.Automation.MainOnline)
            w.Automation.Speak.Announce(w.Automation.Voice.Style($"증축 공사 — {room.Name} 아래 선외 작업 구역을 열었다. 골조부터"), room, 1);
        MarkLog.Add(room.Marks, w.Tick, $"아래 외벽에 증축 공사 (회의)");
        // 자재가 모자라면 원정을 청한다 (회의가 이미 받았으니 바로 보낸다)
        if (p.Shortage != "")
        {
            bool structure = p.Need.Any(x => x.kind == ItemKind.Structure && x.have < x.need);
            if (w.Expedition.ComputerRequest(structure ? MatCat.Structure : MatCat.Repair, approved: true))
                w.Log.Add(w.Tick, LogKind.Ship, $"증축 자재가 모자라다 ({p.Shortage}) — 주 컴퓨터가 원정을 청했다");
        }
        Version++;
    }

    /// <summary>격자를 아래로 늘리고 칸 배열을 쥔 계통을 맞춘다 (칸 번호는 그대로).</summary>
    public void GrowGrid(int rows)
    {
        var w = _w;
        var g = w.Ship.Grid;
        g.GrowRows(rows);
        w.Body.GrowCells();
        w.Paths.Grow();
        w.Structure.Touch();
        Stats.Grown += rows;
        Version++;
        w.Log.Add(w.Tick, LogKind.Ship, $"증축 — 배의 격자를 아래로 {rows}줄 늘렸다 ({g.Width}×{g.Height})");
    }

    private void Stop(AnnexPlan p, string why)
    {
        var w = _w;
        p.State = "중단";
        Stats.Stopped++;
        foreach (var c in p.Site.Inside.Concat(p.Site.Shell)) w.Paths.MooredCells.Remove(c);
        w.Paths.Invalidate();
        foreach (var d in w.Drones.Drones) if (_drone.Remove(d.Id) && d.State == DroneState.Working) d.WorkDone = d.WorkNeeded;
        w.Log.Add(w.Tick, LogKind.Ship, $"증축 공사 중단: {p.Title} — {why}");
        Version++;
    }

    // ─────────────────────────────── 공정 ───────────────────────────────

    private void CheckStage(AnnexPlan p)
    {
        var w = _w;
        switch (p.Stage)
        {
            case AnnexStage.Frame when p.Frame.All(f => f >= 1f):
                Next(p, AnnexStage.Plating, "골조가 다 섰다 — 외판을 붙인다 (아래 → 옆)");
                break;
            case AnnexStage.Plating when p.Frame.All(f => f >= 1f) && p.Plate.All(f => f >= 1f):
                Next(p, AnnexStage.Pressure, "외판을 다 붙였다 — 기밀 시험 · 가압");
                TestCard(p);
                break;
            case AnnexStage.Pressure when p.FullTest == null && p.CardId >= 0:
            {
                var pr = w.Automation.Asks.All.FirstOrDefault(x => x.Id == p.CardId);
                if (pr == null) p.FullTest = true;
                else if (pr.State != ProposalState.Pending) p.FullTest = pr.Accepted;
                break;
            }
            case AnnexStage.FitOut when p.Sheet >= 1f && p.Fit.All(f => f >= 1f):
                p.OpenUntil = w.Tick + SimTime.Hours(1.2f);
                Next(p, AnnexStage.Opening, $"내장 · {p.FixtureName}까지 끝났다 — 개통식 (한 시간)");
                if (w.Automation.Present && w.Automation.MainOnline)
                    w.Automation.Speak.Announce(w.Automation.Voice.Style($"증축한 {Ko.EulReul(p.UseName)} 연다 — 손 비는 사람은 모이자"), RoomOf(p.RoomId), 1);
                break;
            case AnnexStage.Opening when w.Tick >= p.OpenUntil:
                // 통합8 둘만 와서 문을 열 수는 없다 — 셋(배에 그만큼 있으면)이 모일 때까지 몇 시간 더 기다린다 (자는 시간 · 일 사이에 겹치면 다음 교대가 들른다)
                int able = w.Crew.Count(c => !c.Dead && c.CanAct && !c.Away);
                if (p.Celebrated.Count < Math.Min(3, able) && w.Tick < p.OpenUntil + SimTime.Hours(6)) break;
                Open(p);
                break;
        }
    }

    private void Next(AnnexPlan p, AnnexStage to, string text)
    {
        var w = _w;
        p.Stage = to;
        p.StageSince = w.Tick;
        Version++;
        w.Log.Add(w.Tick, LogKind.Ship, $"증축 {(int)to + 1}단계 — {text}");
    }

    /// <summary>가압 전 기밀 시험 카드: 끝까지 할지 (받으면 약한 이음을 다 찾는다 — 가압이 늦어진다).</summary>
    private void TestCard(AnnexPlan p)
    {
        var w = _w;
        var room = RoomOf(p.Site.AttachRoom);
        int weak = p.PlateQ.Count(q => q < 0.55f);
        if (!(w.Automation.Present && w.Automation.MainOnline))
        {
            // 컴퓨터가 없으면 사람끼리 정한다: 안전을 따지는 사람이 지휘하면 끝까지, 급하면 대충
            var boss = w.Command.Captain;
            p.FullTest = boss == null || boss.Value == CrewValue.Safety || Homeless().Count < 2;
            return;
        }
        var card = w.Automation.Asks.Propose("annex:" + p.Id, "annextest", room, "증축 기밀 시험 — 끝까지",
            $"외판 {p.Plate.Length}장 · 약한 이음 의심 {weak}곳 (용접 품질) · 운석 {p.MeteorHits}번", $"비눗물 · 압력 유지 30분 → 약한 이음을 찾아 다시 용접 (가압이 {(TestHours - QuickTestHours) * 60:0}분 늦어진다)",
            25f, null,
            (world, pr) => { p.FullTest = true; },
            (world, pr) => p.Seams > 0 ? (p.FullTest == true ? -1 : 1, p.FullTest == true ? "틀렸다 — 끝까지 시험했는데도 이음이 터졌다" : "맞았다 — 건너뛴 이음이 터졌다")
                : p.Opened >= 0 && world.Tick - p.Opened > SimTime.TicksPerDay ? (p.FullTest == true ? 1 : -1, p.FullTest == true ? "맞았다 — 시험한 이음은 멀쩡하다" : "틀렸다 — 건너뛰어도 괜찮았다") : null);
        p.CardId = card.Id;
    }

    /// <summary>기밀 시험 결과: 약한 이음이 있으면 외판으로 되돌아가고, 없으면 가압 · 문을 딴다.</summary>
    internal void FinishTest(CrewMember c, AnnexPlan p)
    {
        var w = _w;
        if (p.Stage != AnnexStage.Pressure || p.Pressure < 1f) return;
        float bar = p.FullTest == true ? 0.55f : 0.35f;
        var leaks = Enumerable.Range(0, p.Plate.Length).Where(i => p.PlateQ[i] < bar || p.Plate[i] < 1f || p.Frame[i] < 1f).ToList();
        if (leaks.Count > 0)
        {
            foreach (int i in leaks) { p.Plate[i] = MathF.Min(p.Plate[i], 0.5f); p.PlateQ[i] = 0f; }
            p.Leaks += leaks.Count;
            p.Reworks += leaks.Count;
            Stats.Leaks += leaks.Count;
            p.Pressure = 0f;
            Next(p, AnnexStage.Plating, $"기밀 시험 — 이음 {leaks.Count}곳에서 비눗물 거품 · 다시 용접");
            w.Automation.Book.Add(ActKind.Advice, RoomOf(p.Site.AttachRoom), $"증축 기밀 시험: {c.Name}", $"이음 {leaks.Count}곳 샌다 — 이대로 가압하면 공사 구역이 감압된다",
                "외판 재용접", "새는 이음부터", "annex:leak:" + p.Id, SimTime.Minutes(30));
            c.Say(w, Persona.Say(c, "거품이 올라온다 — 여기 다시 용접해야겠다"));
            return;
        }
        Enclose(p, c);
    }

    // ─────────────────────────────── 격자에 방이 생긴다 ───────────────────────────────

    /// <summary>가압: 새 벽 · 바닥 · 방 · 문을 격자에 넣는다. 옆 방 공기를 나눠 받는다.</summary>
    internal Room Enclose(AnnexPlan p, CrewMember? by)
    {
        var w = _w;
        var ship = w.Ship;
        var g = ship.Grid;
        var s = p.Site;
        var R0 = ship.Rooms[s.AttachRoom];
        foreach (var c in s.Inside.Concat(s.Shell)) w.Paths.MooredCells.Remove(c);
        // 선외에 떠 있던 사람이 자리에 있으면 한 칸 밖으로
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.Outside || !s.Covers(c.Cell)) continue;
            var to = new Cell(c.Cell.X, s.Y0 + s.Depth + 2);
            c.Position = to.Center; c.PreviousPosition = c.Position;
        }
        // 1) 새 벽 (외판 품질 = 벽의 건전성)
        for (int i = 0; i < s.Shell.Count; i++)
        {
            var cell = s.Shell[i];
            g.SetKind(cell, TileKind.Wall);
            float q = Math.Clamp(p.PlateQ[i], 0.2f, 1f);
            var ws = new WallState { IsHull = true, Integrity = q < 0.45f ? 0.4f : 0.75f + 0.25f * q, MaxIntegrity = q < 0.45f ? 0.6f : 1f };
            MarkLog.Add(ws.Marks, w.Tick, q < 0.45f ? "증축 외판 — 약한 이음" : "증축 외판");
            ship.AddWall(cell, ws);
        }
        // 2) 방
        var room = new Room { Id = ship.Rooms.Count, Type = p.Use, NameOverride = $"증축 {p.UseName}" };
        ship.Rooms.Add(room);
        foreach (var cell in s.Inside)
        {
            g.SetKind(cell, TileKind.Floor);
            g.SetRoomId(cell, room.Id);
            room.Include(cell);
        }
        float vr = R0.Volume, vn = room.Volume, k = vr / (vr + vn);
        room.Air.O2 = R0.Air.O2 * k; room.Air.N2 = R0.Air.N2 * k; room.Air.CO2 = R0.Air.CO2 * k;
        R0.Air.O2 *= k; R0.Air.N2 *= k; R0.Air.CO2 *= k;
        room.Air.Temperature = R0.Air.Temperature - 6f; // 새 외판은 차다
        room.Circuit = R0.Circuit;
        room.PowerCut = true; room.Powered = false; room.PipesCut = true;
        room.Purpose = "증축 공사 중";
        // 3) 외벽에 문을 딴다
        var dc = s.Door;
        g.SetKind(dc, TileKind.Door);
        ship.RemoveWall(dc);
        var door = new Door { Id = ship.Doors.Count, Cell = dc, ConnectsVertically = true, IsExternal = false, RoomA = R0, RoomB = room };
        R0.Doors.Add(door);
        room.Doors.Add(door);
        ship.AddDoor(door);
        p.DoorId = door.Id;
        // 4) 선체 벽 다시 매기기: 새 방에 덮인 옛 외벽은 이제 안벽
        for (int y = s.Y0 - 1; y <= s.Y0 + s.Depth + 2; y++)
        for (int x = s.X0 - 2; x <= s.X1 + 2; x++)
        {
            var c = new Cell(x, y);
            if (ship.WallAt(c) is not WallState ws) continue;
            bool hull = Cell.Dirs8.Any(d => g.Kind(c + d) == TileKind.Void);
            if (ws.IsHull == hull) continue;
            ws.IsHull = hull;
            if (!hull) { ws.Breach = 0f; ws.Patched = false; }
        }
        // 5) 연결부: 새 벽에 둘씩 셋씩 (골조가 휘었던 칸은 약하다)
        float fq = p.Frame.Length > 0 ? p.Frame.Average() : 1f;
        var spots = new[] { s.Shell[s.Depth / 2], s.Shell[s.Depth + 1 + s.Width / 2], s.Shell[s.Shell.Count - 1 - s.Depth / 2] };
        foreach (var c in spots.Distinct()) w.Structure.AddJoint(room, c, 0.85f + 0.15f * fq - 0.05f * p.MeteorHits, 1f, truss: false);
        room.DesignJoints = Math.Max(1, room.Joints.Count);
        // 6) 계통 맞추기 (칸막이와 같은 순서)
        ShipBuilder.AssignDamperSpot(ship, room);
        ShipBuilder.AssignDamperSpot(ship, R0);
        ship.AssignCompartments();
        foreach (var c in w.Crew)
        {
            c.Memory.GrowRooms(ship.Rooms.Count);
            c.Memory.Fear[room.Id] = c.Memory.Fear[R0.Id] * 0.5f;
        }
        p.RoomId = room.Id;
        p.Enclosed = w.Tick;
        p.Pressure = 1f;
        w.Paths.Invalidate();
        w.Structure.Touch();
        w.Board.RequestScan();
        Version++;
        MarkLog.Add(room.Marks, w.Tick, $"가압 · 문을 땄다 ({by?.Name ?? "?"})");
        w.History.Add(w, HistoryKind.Adaptation, $"증축 — {R0.Name} 아래에 새 방이 생겼다: 가압하고 외벽에 문을 땄다 ({s.Width}×{s.Depth}칸)", room, by != null ? new[] { by } : null, log: true);
        Next(p, AnnexStage.Utilities, "가압 · 문 따기 끝 — 배선 · 배관을 잇는다 (아직 어둡다)");
        return room;
    }

    /// <summary>배선 · 배관을 이었다: 첫 점등.</summary>
    internal void FinishWire(CrewMember c, AnnexPlan p)
    {
        var w = _w;
        if (p.Stage != AnnexStage.Utilities || p.Utilities < 1f || RoomOf(p.RoomId) is not Room room) return;
        bool cable = ItemsV15.Use(w, ItemKind.Cable) & ItemsV15.Use(w, ItemKind.Cable);
        bool hose = ItemsV15.Use(w, ItemKind.Hose) || ItemsV15.Use(w, ItemKind.Sealant);
        room.PowerCut = false;
        room.PipesCut = false;
        room.Purpose = "증축 내장 중";
        p.FirstLight = w.Tick;
        foreach (var h in w.Body.Hatches.Where(h => room.Cells.Contains(h.Cell)).ToList()) w.Body.CloseHatch(h);
        w.History.Add(w, HistoryKind.Adaptation, $"증축한 방에 첫 불이 들어왔다 — {Ko.IGa(c.Name)} 배선 · 배관을 이었다" + (cable ? "" : " (케이블이 모자라 임시로 이어 붙였다)") + (hose ? "" : " · 배관은 나중에"),
            room, new[] { c }, log: true);
        c.Say(w, Persona.Say(c, "불 들어온다!"));
        foreach (var o in w.Crew)
            if (!o.Dead && o != c && (o.Room == room || o.Room?.Id == p.Site.AttachRoom))
            {
                o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.03f);
                Life.Diary(w, o, Persona.Say(o, "새 방에 처음 불이 들어오는 걸 봤다."));
            }
        Next(p, AnnexStage.FitOut, $"첫 점등 — 내장 · {p.FixtureName} {p.Fixtures}");
    }

    /// <summary>설비 하나를 들였다 (침대 · 선반).</summary>
    internal bool FinishFixture(CrewMember c, AnnexPlan p, int i)
    {
        var w = _w;
        if (RoomOf(p.RoomId) is not Room room || i < 0 || i >= p.Fit.Length || p.Fit[i] < 1f) return false;
        var cell = p.FixtureSpots[i];
        if (w.Ship.FurnitureAt(cell) != null) return true;
        if (!w.Ship.IsOpenFloor(cell) || w.Ship.RoomAt(cell) != room) { p.Fit[i] = 0.95f; return false; }
        foreach (var o in w.Crew)
            if (!o.Dead && o.Cell == cell && !o.Outside)
            {
                var free = room.Cells.Where(x => x != cell && w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - o.Position).LengthSquared()).ThenBy(x => x.Y).ThenBy(x => x.X).FirstOrDefault();
                if (free != default) { o.Position = free.Center; o.PreviousPosition = o.Position; }
            }
        Furniture f;
        if (p.Use == RoomType.Storage)
        {
            if (!ItemsV15.Use(w, ItemKind.Plate)) ItemsV15.Use(w, ItemKind.Structure);
            f = w.Ship.AddFurniture(FurnitureType.Shelf, cell);
            f.Storage = new Inventory(60, ItemKinds.Shelved);
        }
        else
        {
            if (!ItemsV15.Use(w, ItemKind.Structure)) ItemsV15.Use(w, ItemKind.Plate);
            f = w.Ship.AddFurniture(FurnitureType.Bed, cell);
        }
        f.Label = $"새 {f.Name} #{i + 1}";
        w.Paths.Invalidate();
        Stats.Fixtures++;
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 증축한 {Ko.EulReul(room.Name)} {f.Label} 들였다", c.Id);
        return true;
    }

    // ─────────────────────────────── 개통 ───────────────────────────────

    private void Open(AnnexPlan p)
    {
        var w = _w;
        if (RoomOf(p.RoomId) is not Room room) { Stop(p, "방이 없다"); return; }
        var builders = p.Hands.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => Crew(kv.Key)).Where(c => c != null).Cast<CrewMember>().ToList();
        var by = Crew(p.Proposer);
        // 이름: 사건(운석을 맞고도 섰다) · 사람(가장 오래 매달린 사람) · 쓰임
        var top = builders.FirstOrDefault(c => !c.Dead);
        if (p.MeteorHits > 0 && (by == null || by.Traits.Bravery >= 0.45f)) { p.Name = $"운석 맞은 별채"; p.NameSource = "사건"; }
        else if (top != null && (by == null || by.Traits.Sociability >= 0.4f || by.Habits.Contains(Habit.Joker))) { p.Name = $"{top.Name}네 별채"; p.NameSource = "사람"; }
        else { p.Name = p.Use == RoomType.Storage ? "새 창고" : "새 침실"; p.NameSource = "쓰임"; }
        if (w.Ship.Rooms.Any(r => r != room && r.CustomName == p.Name)) p.Name += $" {p.Id}";
        room.CustomName = p.Name;
        room.Purpose = null;
        p.State = "개통";
        p.Stage = AnnexStage.Done;
        p.Opened = w.Tick;
        EnsureMass();
        float before = MassMul;
        AddedMassT += SiteMassT(p.Site, p.Fixtures);
        Stats.Opened++;
        Version++;
        float days = (w.Tick - p.Decided) / (float)SimTime.TicksPerDay;
        string names = string.Join("·", builders.Take(4).Select(c => c.Name));
        MarkLog.Add(room.Marks, w.Tick, $"개통 '{p.Name}' — 지은 사람 {names}");
        w.History.Add(w, HistoryKind.Milestone, $"증축 개통 — '{p.Name}' ({p.UseName} · {p.FixtureName} {p.Fixtures}) · {SimTime.Day(w.Tick)}일 · 지은 사람 {names} · 공사 {days:0.#}일"
            + (p.MeteorHits > 0 ? $" · 운석 {p.MeteorHits}번 맞고도" : "") + (p.Leaks > 0 ? $" · 샌 이음 {p.Leaks}곳 다시 용접" : "") + $" · 배 무게 +{SiteMassT(p.Site, p.Fixtures):0}t",
            room, builders, log: true);
        // 주 컴퓨터: 바뀐 배를 다시 잰다
        w.Automation.Book.Add(ActKind.Advice, room, $"증축 개통: {p.Name}", $"배 무게 ×{MassMul:0.000} → 가속 −{(1f - before / MassMul) * 100:0.#}% · 회피 연료 {w.Propulsion.EvadeCost:0.#}kg · 방 계통 전력 +{p.PowerKw:0.##}kW",
            "추진 · 연료 계획을 새 무게로", "참고하세요", "annex:open:" + p.Id, 0);
        // 사람: 낸 사람 · 지은 사람 · 반대한 사람
        if (by != null && !by.Dead)
        {
            by.Needs.Stress = MathF.Max(0f, by.Needs.Stress - 0.06f);
            Life.Diary(w, by, Persona.Say(by, $"내가 낸 증축이 열렸다 — '{p.Name}'."));
        }
        foreach (var a in builders)
        foreach (var b in builders)
            if (a != b && !a.Dead && !b.Dead) a.ChangeAffinity(b, 0.012f);
        foreach (var id in p.Against)
            if (Crew(id) is CrewMember o && !o.Dead) Life.Diary(w, o, Persona.Say(o, $"증축이 열렸다. 배가 무거워진 건 마음에 걸린다."));
        MoveIn(p, room);
    }

    /// <summary>새 침대로 옮겨 간다: 간이침대 · 침대 없는 사람부터, 남으면 북적이는 침실의 조용한 사람.</summary>
    private void MoveIn(AnnexPlan p, Room room)
    {
        var w = _w;
        if (p.Use != RoomType.Quarters) return;
        var beds = room.Furniture.Where(f => f.Type == FurnitureType.Bed && f.Owner == null && !f.Stowed).OrderBy(f => f.Id).ToList();
        var movers = Homeless().OrderBy(c => c.Bed == null ? 0 : 1).ThenBy(c => c.Id).ToList();
        foreach (var q in w.Ship.Rooms.Where(r => !r.Detached && r != room && r.Type == RoomType.Quarters).OrderByDescending(r => w.Crew.Count(c => !c.Dead && c.Bed?.Room == r)).ThenBy(r => r.Id))
        {
            if (w.Crew.Count(c => !c.Dead && c.Bed?.Room == q) < 6) continue;
            movers.AddRange(w.Crew.Where(c => !c.Dead && !c.Away && c.Bed?.Room == q && !movers.Contains(c)).OrderBy(c => c.Traits.Sociability).ThenBy(c => c.Id));
        }
        int i = 0;
        foreach (var bed in beds)
        {
            if (i >= movers.Count) break;
            var c = movers[i++];
            var old = c.Bed;
            if (old != null && old.Owner == c)
            {
                old.Owner = null;
                if (old.Type == FurnitureType.Cot) w.Ship.Stow(old);
            }
            c.Bed = bed;
            bed.Owner = c;
            p.MovedIn.Add(c.Id);
            Stats.MovedIn++;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - (old?.Type == FurnitureType.Cot || old == null ? 0.06f : 0.02f));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} '{p.Name}'의 {Ko.EulReul(bed.Label)} 제 침대로 삼았다" + (old?.Type == FurnitureType.Cot ? " — 간이침대를 접었다" : ""), c.Id);
            Life.Diary(w, c, Persona.Say(c, old?.Type == FurnitureType.Cot || old == null ? $"오늘부터 '{p.Name}'에서 잔다. 간이침대와 작별이다. 새 패널 냄새가 난다." : $"'{p.Name}'로 옮겼다. 조용하다."));
        }
        w.Paths.Invalidate();
    }

    // ─────────────────────────────── 위험 ───────────────────────────────

    /// <summary>운석: 공사 중인 골조 · 외판이 휜다 (밖에 매달린 사람은 EvaRisk가 따로 맞는다).</summary>
    public void OnImpact(Cell target, float size)
    {
        var w = _w;
        var p = Active;
        if (p == null || p.Enclosed >= 0 || p.Stage > AnnexStage.Pressure) return;
        float radius = 1.5f + 2f * size;
        int bent = 0;
        float lost = 0f;
        for (int i = 0; i < p.Frame.Length; i++)
        {
            float d = (p.Site.Shell[i].Center - target.Center).Length();
            if (d > radius) continue;
            float dmg = MathF.Min(1f, (0.35f + 0.9f * size) * (1f - d / (radius + 0.5f)) * R.Range(0.8f, 1.1f));
            if (p.Plate[i] > 0f) { p.Plate[i] = MathF.Max(0f, p.Plate[i] - dmg * 1.2f); p.PlateQ[i] *= 0.7f; }
            if (p.Frame[i] > 0f)
            {
                float before = p.Frame[i];
                p.Frame[i] = MathF.Max(0f, p.Frame[i] - dmg);
                lost += before - p.Frame[i];
                p.Bent[i] = true;
                bent++;
            }
        }
        if (bent == 0) return;
        p.MeteorHits++;
        p.Reworks += bent;
        Stats.MeteorHits++;
        Stats.Reworks += bent;
        if (p.Stage == AnnexStage.Pressure) { p.Pressure = 0f; Next(p, AnnexStage.Plating, "운석에 외판이 찢겼다 — 기밀 시험을 다시"); }
        var room = RoomOf(p.Site.AttachRoom);
        Version++;
        w.Log.Add(w.Tick, LogKind.Warning, $"운석이 증축 골조를 쳤다 — {bent}칸 휨 (재작업 약 {lost * FrameHours:0.#}시간)");
        w.History.Add(w, HistoryKind.Damage, $"공사 중인 증축 골조에 운석 — {bent}칸이 휘었다", room, at: target);
        w.Automation.Book.Add(ActKind.Advice, room, $"증축 골조 {bent}칸 휨 (운석 {size:0.0})", $"휜 골조에 외판을 붙이면 샌다 · 재작업 {lost * FrameHours:0.#}시간 · 공정 {(p.Days + lost * FrameHours / 7f):0.#}일로",
            "휜 골조부터 다시 세운다", "휜 칸부터", "annex:hit:" + p.Id, SimTime.Minutes(5));
        if (Crew(p.Proposer) is CrewMember by && !by.Dead) by.Needs.Stress = MathF.Min(1f, by.Needs.Stress + 0.05f);
        foreach (var kv in p.Hands.OrderBy(k => k.Key))
            if (Crew(kv.Key) is CrewMember b && !b.Dead && kv.Value > 0.3f) Life.Diary(w, b, Persona.Say(b, "애써 세운 골조가 운석에 휘었다."));
    }

    /// <summary>기밀 시험을 건너뛴 약한 이음이 터진다 (공사 구역 감압 — 뒤는 선체 · 공기 계통이 굴린다).</summary>
    private void Seams(AnnexPlan p)
    {
        var w = _w;
        if (p.Enclosed < 0) return;
        for (int i = 0; i < p.PlateQ.Length; i++)
        {
            if (p.PlateQ[i] >= 0.45f) continue;
            if (!R.Chance(0.12f)) continue;
            var cell = p.Site.Shell[i];
            p.PlateQ[i] = 1f;
            if (w.Ship.WallAt(cell) is not WallState ws) continue;
            Hull.Damage(w.Ship, cell, 0.3f);
            p.Seams++;
            Stats.Seams++;
            var room = RoomOf(p.RoomId);
            MarkLog.Add(ws.Marks, w.Tick, "약한 이음이 터졌다");
            w.RaiseAlert($"증축한 {room?.Name ?? "방"} 이음이 터졌다 — 공사 구역 감압", room, AlertLevel.Critical, shipWide: true);
            w.History.Add(w, HistoryKind.Damage, $"증축 이음이 터졌다 — {(p.FullTest == false ? "기밀 시험을 건너뛴 이음" : "시험이 놓친 이음")}", room, at: cell, log: true);
            w.Board.RequestScan();
            Version++;
            break;
        }
    }

    /// <summary>소음: 공사 중 옆 방에서 자는 사람이 깬다 (주 컴퓨터가 있으면 밤에는 시끄러운 공사를 멈춘다).</summary>
    private void Noise(AnnexPlan p)
    {
        var w = _w;
        if (Working <= 0 || !Noisy(p)) return;
        var near = NoiseRooms(p);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Pose != Pose.Sleeping || c.Room == null || !near.Contains(c.Room.Id)) continue;
            if (_woke.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(4)) continue;
            _woke[c.Id] = w.Tick;
            c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.05f);
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
            p.Woken++;
            Stats.Woken++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 증축 공사 소리(용접 · 망치)에 잠을 설쳤다", c.Id);
            Life.Diary(w, c, Persona.Say(c, "벽 너머 용접 소리에 깼다. 언제 끝나나."));
            w.Automation.Book.Add(ActKind.Advice, c.Room, $"{c.Room.Name}에서 자는 사람 · 증축 공사 소음", "잠을 깨운다", "밤에는 시끄러운 공사를 멈춘다",
                "낮에 몰아서", "annex:noise", SimTime.Hours(6));
        }
    }

    /// <summary>소리가 나는 공정인가 (골조 · 외판 용접 · 내장 자르기).</summary>
    public static bool Noisy(AnnexPlan p) => p.Stage is AnnexStage.Frame or AnnexStage.Plating or AnnexStage.FitOut;

    /// <summary>공사 소리가 닿는 방 (붙는 방 · 그 방과 문으로 이어진 방 · 증축한 방).</summary>
    public HashSet<int> NoiseRooms(AnnexPlan p)
    {
        var set = new HashSet<int> { p.Site.AttachRoom };
        if (p.RoomId >= 0) set.Add(p.RoomId);
        if (RoomOf(p.Site.AttachRoom) is Room r)
            foreach (var d in r.Doors)
                if (!d.IsExternal && !d.Removed) { if (d.RoomA != null) set.Add(d.RoomA.Id); if (d.RoomB != null) set.Add(d.RoomB.Id); }
        return set;
    }

    /// <summary>Ambience: 공사 소음 (붙는 방이 가장 크다).</summary>
    public float NoiseIn(Room room)
    {
        var p = Active;
        if (p == null || Working <= 0 || !Noisy(p)) return 0f;
        if (room.Id == p.Site.AttachRoom) return 0.55f;
        if (room.Id == p.RoomId) return 0.7f;
        return 0f;
    }

    /// <summary>지시 · 시험: 그 쓰임으로 바로 안건을 내고 회의 없이 짓기 시작한다 (자리가 없으면 null).</summary>
    public AnnexPlan? Order(RoomType use, int fixtures)
    {
        var w = _w;
        var by = Adults().OrderByDescending(c => c.Traits.Diligence).ThenBy(c => c.Id).FirstOrDefault();
        var sites = Sites(use, fixtures);
        if (by == null || sites.Count == 0) return null;
        var p = Propose(new AnnexPlan
        {
            Proposer = by.Id, Use = use, Fixtures = fixtures, Site = sites[0], Proposed = w.Tick, Source = "지시", Why = "관찰자 지시",
            Title = $"{w.Ship.Rooms[sites[0].AttachRoom].Name} 아래 증축 — {(use == RoomType.Storage ? "창고" : "침실")}",
        });
        Start(p);
        return p.State == "공사" ? p : null;
    }

    /// <summary>시험: 공정을 한꺼번에 끝낸다 (골조 · 외판 → 가압 · 문 → 배선 → 설비 → 개통).</summary>
    public Room? BuildNow(AnnexPlan p)
    {
        var w = _w;
        var by = Crew(p.Proposer) ?? Adults().FirstOrDefault();
        if (by == null) return null;
        if (p.State == "제안") Start(p);
        if (p.State != "공사") return null;
        for (int i = 0; i < p.Frame.Length; i++) { p.Frame[i] = 1f; p.Plate[i] = 1f; p.PlateQ[i] = MathF.Max(p.PlateQ[i], 0.8f); p.Bent[i] = false; }
        p.Stage = AnnexStage.Pressure; p.FullTest = true; p.Pressure = 1f;
        FinishTest(by, p);
        if (p.Stage != AnnexStage.Utilities) return null;
        p.Utilities = 1f;
        FinishWire(by, p);
        p.Sheet = 1f;
        for (int i = 0; i < p.Fit.Length; i++) { p.Fit[i] = 1f; FinishFixture(by, p, i); }
        Open(p);
        return RoomOf(p.RoomId);
    }

    /// <summary>시나리오 "crowded": 빈 침대는 예전에 뜯어 화물칸으로 썼고, 구조한 두 사람이 탄다 → 간이침대에서 잔다.</summary>
    public static Room? Crowd(World w)
    {
        var ship = w.Ship;
        foreach (var f in ship.Furniture.Where(f => f.Type == FurnitureType.Bed && f.Owner == null && !f.Stowed).ToList()) ship.Stow(f);
        var hatch = DroneSystem.Hatch(w);
        var at = hatch != null ? Cell.Dirs4.Select(d => hatch.Cell + d).FirstOrDefault(c => ship.RoomAt(c) != null && ship.IsWalkable(c)) : default;
        if (at == default) at = ship.LiveRooms.First(r => r.Type == RoomType.Corridor).Cells.First(ship.IsWalkable);
        for (int i = 0; i < 2; i++) w.AddSurvivor(at);
        w.Paths.Invalidate();
        w.Log.Add(w.Tick, LogKind.Ship, "구조한 두 사람이 탔다 — 빈 침대는 예전에 뜯어 화물칸으로 썼다 (간이침대)");
        return ship.RoomsOf(RoomType.Quarters).FirstOrDefault();
    }

    // ─────────────────────────────── 지문 ───────────────────────────────

    private static long Str(string? s)
    {
        if (s == null) return 0;
        uint h = 2166136261;
        foreach (char ch in s) { h ^= ch; h *= 16777619; }
        return h;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        var s = Stats;
        I(s.Proposed); I(s.Passed); I(s.Rejected); I(s.Opened); I(s.Grown); I(s.MeteorHits); I(s.Leaks); I(s.Seams); I(s.DroneWelds); I(s.EvaTrips); I(s.Woken); I(s.MovedIn); I(s.Members); I(s.Fixtures);
        foreach (var p in Plans)
        {
            I(Str(p.State)); I((int)p.Stage); I(p.RoomId); I(p.Site.AttachRoom); I(p.Site.X0); I(p.Site.Depth); F(p.Pressure); F(p.Utilities); F(p.Sheet); I(Str(p.Name));
            foreach (var v in p.Frame) F(v);
            foreach (var v in p.Plate) F(v);
            foreach (var v in p.Fit) F(v);
        }
        F(AddedMassT); I(_w.Ship.Grid.Height);
    }
}
