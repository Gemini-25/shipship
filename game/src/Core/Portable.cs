using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.7 이동식 장비: 작업등 · 히터 · 선풍기 · 공기청정기 · 이동식 배터리 · 양수기(+호스) · 카트.
// 창고에 두었다가 정전 · 조명 고장 · 침수 · 추위 · 연기 같은 때 사람이 꺼내 와 꽂고(방 콘센트 = 그 방 회로 · 옆 방 콘센트까지 케이블 · 이동식 배터리) 켠다.
// 같은 회로에 여럿 꽂으면 차단기가 떨어진다 (사람들은 회로 용량을 모른다 — 한 번 겪은 회로만 조심한다). 전압이 떨어진 방에서는 히터가 약하고 등이 흐리다.
// 다 쓰면 창고에 돌려놓는다 — 잊고 두기도 한다. 오래 같은 자리에 둔 장비는 정식 시설로 굳어진다(개조 제안).
// 작업등은 일하는 사람의 몸에 가리면 옮긴다 (등 · 사람 · 작업 위치로 판정). 먼저 쓰는 사람이 있으면 기다리거나 다른 것을 가져온다.
// 광원(작업등 · 히터 열선 · 표시등)은 위치 · 세기 · 색을 공개한다 — 다음 단계의 2D 조명이 이 장비를 광원으로 쓴다.

public enum PortableKind { WorkLamp, Heater, Fan, Purifier, Battery, Pump, Cart }

/// <summary>어디서 전기를 받나: 없음(내장 전지) · 방 콘센트(그 방 회로) · 이동식 배터리.</summary>
public enum PortablePlug { None, Outlet, Battery }

public enum PortableTask { Setup, Feed, Retrieve, Unplug, Fix }

public sealed record PortableSpec(PortableKind Kind, string Name, float Kw, float CellKwh, int Weight, float LightRadius, Vector3 LightColor, float Glow, PortableKind? Alt);

public static class PortableSpecs
{
    /// <summary>한 사람이 손에 드는 무게 (넘으면 카트를 밀고 가거나 두 번 나른다).</summary>
    public const int HandLimit = 3;
    public const int CartLimit = 9;

    public static readonly PortableSpec[] All =
    {
        new(PortableKind.WorkLamp, "작업등", 0.06f, 0.5f, 1, 4.5f, new Vector3(1f, 0.93f, 0.78f), 1f, null),
        new(PortableKind.Heater, "히터", 1.5f, 0f, 2, 1.6f, new Vector3(1f, 0.45f, 0.18f), 0.3f, null),
        new(PortableKind.Fan, "선풍기", 0.08f, 0.2f, 1, 0f, Vector3.Zero, 0f, PortableKind.Purifier),
        new(PortableKind.Purifier, "공기청정기", 0.25f, 0f, 2, 0.7f, new Vector3(0.45f, 0.8f, 1f), 0.1f, PortableKind.Fan),
        new(PortableKind.Battery, "이동식 배터리", 0f, 3f, 2, 0.5f, new Vector3(0.4f, 1f, 0.5f), 0.06f, null),
        new(PortableKind.Pump, "양수기", 0.6f, 0f, 3, 0f, Vector3.Zero, 0f, null),
        new(PortableKind.Cart, "카트", 0f, 0f, 0, 0f, Vector3.Zero, 0f, null),
    };

    public static PortableSpec Of(PortableKind k) => All[(int)k];
    public static string Name(PortableKind k) => Of(k).Name;

    /// <summary>오래 두면 굳어지는 정식 시설.</summary>
    public static FurnitureType? Fixture(PortableKind k) => k switch
    {
        PortableKind.WorkLamp => FurnitureType.EmergencyLight,
        PortableKind.Purifier or PortableKind.Fan => FurnitureType.AirPurifier,
        PortableKind.Pump => FurnitureType.Dehumidifier,
        _ => null,
    };
}

public sealed class PortableDevice
{
    public int Id { get; init; }
    public PortableKind Kind { get; init; }
    public PortableSpec Spec => PortableSpecs.Of(Kind);
    public string Name => Spec.Name;

    /// <summary>놓인 칸 (들고 가는 중이면 마지막으로 놓였던 칸).</summary>
    public Cell At { get; internal set; }
    /// <summary>창고의 제자리.</summary>
    public Cell Home { get; internal set; }
    public bool Stored { get; internal set; } = true;
    public bool Lost { get; internal set; }
    /// <summary>들고(밀고) 가는 사람.</summary>
    public CrewMember? HeldBy { get; internal set; }
    /// <summary>쓰는 사람 (작업등이 비추는 사람).</summary>
    public CrewMember? User { get; internal set; }
    public int InstalledBy { get; internal set; } = -1;
    public PortablePlug Plug { get; internal set; }
    /// <summary>꽂은 콘센트의 방 (그 방의 회로를 쓴다 · 옆 방이면 문으로 케이블을 끌어왔다).</summary>
    public Room? Outlet { get; internal set; }
    /// <summary>연결한 이동식 배터리.</summary>
    public PortableDevice? Source { get; internal set; }
    /// <summary>케이블이 닿는 곳 (콘센트 · 배터리) — 화면이 선을 긋는다.</summary>
    public Vector2? CableTo { get; internal set; }
    /// <summary>양수기 호스 끝 (문 너머 배수구).</summary>
    public Vector2? HoseTo { get; internal set; }
    /// <summary>전지 잔량 (kWh) — 이동식 배터리 · 작업등 · 선풍기의 내장 전지.</summary>
    public float Charge { get; internal set; }
    public float Capacity => Spec.CellKwh;
    public float ChargeFrac => Capacity > 0f ? Charge / Capacity : 0f;
    /// <summary>스위치를 켰다.</summary>
    public bool On { get; internal set; }
    /// <summary>실제로 돈다 (켰고 · 전기가 오고 · 멀쩡하다).</summary>
    public bool Running { get; internal set; }
    public bool Broken { get; internal set; }
    public bool Charging { get; internal set; }
    public long PlacedSince { get; internal set; } = -1;
    /// <summary>무슨 일로 꺼냈나 (요구 열쇠) · 그 까닭.</summary>
    public string? Purpose { get; internal set; }
    public string? Why { get; internal set; }
    /// <summary>쓰임이 끝난 때 (-1 = 아직 쓴다).</summary>
    public long DoneSince { get; internal set; } = -1;
    public bool WillForget { get; internal set; }
    public bool Forgotten { get; internal set; }
    public int ClaimedBy { get; internal set; } = -1;
    /// <summary>작업등이 비추는 곳 · 몸에 가렸나.</summary>
    public Vector2 Aim { get; internal set; }
    public bool Shadowed { get; internal set; }
    internal float ShadowHours;
    /// <summary>물에 잠긴 케이블 이음매에 스민 물 (1이 되면 누전).</summary>
    public float Soak { get; internal set; }
    /// <summary>히터 열선에 앉은 먼지 (창고에 오래 두면 쌓이고, 켜면 타는 냄새를 내며 탄다).</summary>
    public float Dust { get; internal set; } = 1f;
    public float Hours { get; internal set; }
    /// <summary>실려 가는 카트.</summary>
    public PortableDevice? OnCart { get; internal set; }
    /// <summary>방 콘센트 전압 몫 (전압이 떨어지면 히터가 약하고 등이 흐리다).</summary>
    public float Supply { get; internal set; } = 1f;

    public bool Placed => !Stored && HeldBy == null && !Lost;

    /// <summary>지금 위치 (들고 가면 그 사람 곁).</summary>
    public Vector2 Position => HeldBy is CrewMember h
        ? h.Position + (Kind == PortableKind.Cart ? new Vector2(0.45f, 0.1f) : OnCart != null ? new Vector2(0.45f, -0.05f + 0.08f * (Id % 3)) : new Vector2(0.28f, 0.12f))
        : At.Center;

    // ── 광원 (다음 단계의 2D 조명이 읽는다) ──
    public Vector2 LightPos => Position;
    public float LightRadius => Spec.LightRadius * (0.55f + 0.45f * LightIntensity);
    /// <summary>0~1 (꺼지면 0 · 전압 · 잔량에 따라 흐려진다).</summary>
    public float LightIntensity { get; internal set; }
    public Vector3 LightColor => Spec.LightColor;
    /// <summary>작업등은 비추는 쪽이 있다 (Aim 쪽으로 넓은 원뿔).</summary>
    public bool Directional => Kind == PortableKind.WorkLamp && Aim != Vector2.Zero;
}

public sealed class PortableStats
{
    public int HeaterFires, WetTrips, CableShocks, BatteryFires, CartSnags;
    public int Setups, Feeds, Returned, Forgotten, Found, Waits, Swaps, Trips, Unplugged, Rerouted, Avoided, LampMoves, Breakdowns, Wet, Fixed, Dropped, CartTrips, AisleCarts, LampMeals, Gatherings, Settled;
    public float PumpedL, HeatKwh, LampHours;
    /// <summary>주 컴퓨터: 과부하 예측 방송 · 듣고 뽑음 · 빈 방 히터 경고 · 껐음 · 창고 빈 자리 알림 · 찾아 돌려놓음. 냄새: 히터 먼지 · 뜨거운 콘센트. 배 본체 · 음식.</summary>
    public int ComputerWarns, HeededWarns, HeaterWarns, HeaterOffs, InventoryCalls, InventoryFound, DustSniffs, HotOutlets, HoseWets, WarmPlates;
    public string LinkSummary() =>
        $"컴퓨터 과부하 예측 {ComputerWarns}(듣고 뽑음 {HeededWarns}) · 빈 방 히터 경고 {HeaterWarns}(껐음 {HeaterOffs}) · 창고 빈 자리 알림 {InventoryCalls}(찾음 {InventoryFound}) · " +
        $"먼지 타는 냄새 {DustSniffs} · 뜨거운 콘센트 {HotOutlets} · 호스 물 {HoseWets} · 히터에 데운 접시 {WarmPlates}";
    public string Summary() =>
        $"꺼내 설치 {Setups}(카트로 {CartTrips}) · 배터리 갖다 댐 {Feeds} · 회수 {Returned} · 잊고 둠 {Forgotten}(찾음 {Found}) · 기다림 {Waits} · 다른 것으로 {Swaps} · " +
        $"차단기 {Trips}(뽑음 {Unplugged} · 옆 방 콘센트로 {Rerouted} · 조심 {Avoided}) · 작업등 옮김 {LampMoves} · 고장 {Breakdowns}(젖어서 {Wet}) · 고침 {Fixed} · " +
        $"내려놓고 감 {Dropped} · 통로의 카트 {AisleCarts}(걸려 늦음 {CartSnags}) · 히터 불 {HeaterFires} · 젖은 케이블 누전 {WetTrips} · 감전 {CableShocks} · 배터리 불 {BatteryFires} · 등불 아래 식사 {LampMeals}(모임 {Gatherings}) · 퍼냄 {PumpedL:0}L · 히터 {HeatKwh:0.0}kWh · 작업등 {LampHours:0.0}시간 · 정식 시설로 {Settled}";
}

/// <summary>장비가 필요한 곳 (방마다 몇 분마다 다시 본다).</summary>
public sealed class PortableNeed
{
    public PortableTask Task { get; init; }
    public PortableKind Kind { get; init; }
    public Room Room { get; init; } = null!;
    public Cell Spot { get; init; }
    public Vector2 Aim { get; init; }
    public bool Battery { get; init; }
    public string Why { get; init; } = "";
    public float Urgency { get; init; }
    public string Key { get; init; } = "";
    public CrewMember? For { get; init; }
    public PortableDevice? Device { get; init; }
}

public sealed partial class PortableSystem
{
    /// <summary>한 회로의 콘센트가 견디는 이동식 장비 부하 (넘으면 몇 분 뒤 차단기가 떨어진다).</summary>
    public const float OutletCapKw = 3.2f;
    public const float BatteryMaxKw = 1.8f;
    public const float ChargeKw = 0.4f;
    /// <summary>켜 둔 히터가 불을 낼 빈도 (시간당 · 침구 곁 ×4 · 아무도 없으면 ×2.5) · 물에 잠긴 케이블의 누전 · 감전 배율 (조정 · 시험용).</summary>
    public static float HeaterFireRate = 0.006f, WetCableRate = 1f;

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7451 + 167));
    private int _nextId;
    private long _nextScan;
    private readonly float[] _over = new float[PowerGrid.CircuitCount], _hot = new float[PowerGrid.CircuitCount];
    /// <summary>차단기 열동 배율: 시간당 (넘친 비율)² × 이것 — 4.5kW(140%)면 8분쯤 · 3.6kW(113%)면 한 시간 넘게 버틴다.</summary>
    public const float BreakerHeat = 47f;
    private readonly bool[] _learned = new bool[PowerGrid.CircuitCount];
    private readonly List<Room> _lit = new();
    private readonly Dictionary<string, int> _claims = new();
    private readonly Dictionary<string, List<PortableDevice>> _claimed = new();
    private readonly Dictionary<string, long> _waited = new();
    private readonly Dictionary<int, Job> _meal = new();
    private readonly Dictionary<int, long> _gatherLog = new();
    private readonly Dictionary<int, int> _staying = new(), _awake = new();
    private readonly Dictionary<int, (long tick, PortableChoice? choice)> _choice = new();
    private readonly HashSet<Cell> _cartCells = new();
    private readonly Dictionary<int, float> _noise = new();
    private readonly HashSet<int> _clean = new();
    private readonly HashSet<(int crew, int cart)> _snagged = new();
    /// <summary>히터에서 불이 난 적이 있다 (침구 곁에 두지 않는다) · 젖은 케이블에 혼난 적이 있다 (물 찬 방엔 배터리로).</summary>
    private bool _heaterCaution, _wetLesson;

    public List<PortableDevice> Devices { get; } = new();
    public List<PortableNeed> Needs { get; } = new();
    public PortableStats Stats { get; } = new();
    /// <summary>회로마다 지금 이동식 장비가 끄는 전력 (kW).</summary>
    public float[] CircuitLoad { get; } = new float[PowerGrid.CircuitCount];
    /// <summary>한 번 차단기가 떨어져 조심하는 회로.</summary>
    public bool Learned(int circuit) => circuit >= 0 && circuit < _learned.Length && _learned[circuit];

    public PortableSystem(World w)
    {
        _w = w;
        Seed();
    }

    // ───────────────────────── 시작 장비 ─────────────────────────

    /// <summary>배 크기(침대 수)에 맞게 창고에 몇 개씩.</summary>
    private void Seed()
    {
        var ship = _w.Ship;
        int beds = ship.FurnitureOf(FurnitureType.Bed).Count() + ship.FurnitureOf(FurnitureType.Cot).Count();
        var rooms = ship.RoomsOf(RoomType.Storage).ToList();
        if (rooms.Count == 0) rooms = ship.RoomsOf(RoomType.Workshop).ToList();
        if (rooms.Count == 0) rooms = ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.Cells.Count > 0).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).Take(1).ToList();
        if (rooms.Count == 0) return;
        var spots = rooms.Select(StoreCells).ToList();
        var plan = new (PortableKind k, int n)[]
        {
            (PortableKind.WorkLamp, 2 + beds / 6), (PortableKind.Battery, 1 + beds / 8), (PortableKind.Heater, 1 + beds / 8),
            (PortableKind.Fan, 1 + beds / 12), (PortableKind.Purifier, 1 + beds / 15), (PortableKind.Pump, 1 + beds / 16), (PortableKind.Cart, 1 + beds / 20),
        };
        int i = 0;
        foreach (var (k, n) in plan)
            for (int j = 0; j < n; j++, i++)
            {
                var list = spots[i % spots.Count];
                var cell = list.Count > 0 ? list[(i / spots.Count) % list.Count] : rooms[i % rooms.Count].Cells[0];
                Add(k, cell);
            }
    }

    /// <summary>창고에서 장비를 둘 칸: 벽에 붙은 빈 바닥부터 (문 앞은 비운다).</summary>
    private List<Cell> StoreCells(Room r)
    {
        var ship = _w.Ship;
        var doors = new HashSet<Cell>(r.Doors.Select(d => d.Cell));
        return r.Cells.Where(c => ship.IsOpenFloor(c) && !Cell.Dirs4.Any(d => doors.Contains(c + d)))
            .OrderBy(c => Cell.Dirs4.Count(d => ship.IsOpenFloor(c + d))).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
    }

    /// <summary>장비 하나를 창고 칸에 들인다 (시작 장비 · 보급 · 시험).</summary>
    public PortableDevice Add(PortableKind kind, Cell at)
    {
        var d = new PortableDevice { Id = _nextId++, Kind = kind, At = at, Home = at };
        d.Charge = d.Capacity;
        Devices.Add(d);
        return d;
    }

    // ───────────────────────── 판정 도우미 ─────────────────────────

    /// <summary>천장 조명 · 비상등으로는 캄캄한 방 (이동식 등을 빼고).</summary>
    public static bool Unlit(Room r) => (!r.Powered || r.LightsOut) && !ModulesV15.FixedLit(r);

    /// <summary>그 방 콘센트를 쓸 수 있나 (전기가 오고 · 분전함이 올라가 있고 · 물이 깊지 않다).</summary>
    public static bool OutletOk(Room r) => !r.Detached && r.Powered && !r.BreakerOff && MoistureSystem.Depth(r) < 0.1f;

    public Room? RoomOf(PortableDevice d) => d.Lost ? null : d.HeldBy?.Room ?? _w.Ship.RoomAt(d.At);

    private bool Busy(PortableDevice d) => d.HeldBy != null || d.ClaimedBy >= 0 || d.Placed && d.Purpose != null && d.DoneSince < 0;

    private bool Available(PortableDevice d, CrewMember c) =>
        !d.Lost && !d.Broken && d.HeldBy == null && (d.ClaimedBy < 0 || d.ClaimedBy == c.Id) && (d.Stored || d.Purpose == null || d.DoneSince >= 0);

    private bool RunningIn(Room r, PortableKind k) => Devices.Any(d => d.Kind == k && d.Placed && d.On && !d.Broken && RoomOf(d) == r);
    private bool PlacedIn(Room r, PortableKind k) => Devices.Any(d => d.Kind == k && d.Placed && d.On && RoomOf(d) == r);

    /// <summary>방에 놓인 장비가 있는 칸.</summary>
    public bool Occupied(Cell c) => Devices.Any(d => d.Placed && d.At == c);

    /// <summary>카트가 통로를 차지한 칸 (경로 비용이 필요하면 여기를 본다).</summary>
    public IEnumerable<Cell> CartCells => Devices.Where(d => d.Kind == PortableKind.Cart && d.Placed).Select(d => d.At);

    /// <summary>방 안에서 장비를 내려놓을 빈 칸 (가까운 곳부터 · 문 앞 · 다른 장비 자리 빼고).</summary>
    private Cell? FreeSpot(Room r, Vector2 near, Cell? not = null)
    {
        var ship = _w.Ship;
        Cell? best = null;
        float bd = float.MaxValue;
        foreach (var c in r.Cells)
        {
            if (!ship.IsOpenFloor(c) || c == not || Occupied(c)) continue;
            float d = (c.Center - near).LengthSquared();
            if (r.Doors.Any(x => Math.Abs(x.Cell.X - c.X) + Math.Abs(x.Cell.Y - c.Y) <= 1)) d += 6f;
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    /// <summary>히터 자리: 불을 겪은 배는 침구 · 의자 곁을 피한다.</summary>
    private Cell? HeaterSpot(Room r)
    {
        if (!_heaterCaution) return CenterSpot(r);
        var near = r.Center;
        return r.Cells.Where(c => _w.Ship.IsOpenFloor(c) && !Occupied(c) && !Flammable(c)).OrderBy(c => (c.Center - near).LengthSquared()).ThenBy(c => c.Y).ThenBy(c => c.X).Cast<Cell?>().FirstOrDefault() ?? CenterSpot(r);
    }

    private Cell? CenterSpot(Room r)
    {
        var table = r.Furniture.Where(f => f.Type == FurnitureType.Table && !f.Stowed).OrderBy(f => f.Id).FirstOrDefault();
        return FreeSpot(r, table?.Center ?? r.Center);
    }

    /// <summary>쓸 콘센트: 그 방 → 문으로 이어진 옆 방 (케이블을 끌어온다).</summary>
    private Room? OutletFor(Room r, float kw)
    {
        bool Fits(Room x) => !_learned[x.Circuit] || ProjectedKw(x.Circuit) + kw <= OutletCapKw;
        if (OutletOk(r) && Fits(r)) return r;
        foreach (var d in r.Doors.OrderBy(d => d.Cell.Y).ThenBy(d => d.Cell.X))
        {
            var o = d.RoomA == r ? d.RoomB : d.RoomA;
            if (o != null && OutletOk(o) && Fits(o)) return o;
        }
        if (OutletOk(r) && !Fits(r)) Stats.Avoided++;
        return null;
    }

    /// <summary>켜 둔 채 콘센트에 꽂힌 장비가 그 회로에 걸 부하 (전기가 끊겨 있어도 — 다시 올리면 걸린다).</summary>
    public float ProjectedKw(int circuit)
    {
        float kw = 0f;
        foreach (var d in Devices)
            if (d.Placed && d.On && !d.Broken && d.Plug == PortablePlug.Outlet && d.Outlet?.Circuit == circuit) kw += d.Spec.Kw;
        return kw;
    }

    /// <summary>케이블 끝: 콘센트가 있는 방의 장비에서 가장 가까운 벽 쪽 칸.</summary>
    private Vector2 OutletPoint(Room outlet, Vector2 from)
    {
        var ship = _w.Ship;
        Vector2 best = outlet.Center;
        float bd = float.MaxValue;
        foreach (var c in outlet.Cells)
            foreach (var dir in Cell.Dirs4)
            {
                if (ship.Grid.Kind(c + dir) != TileKind.Wall) continue;
                var p = c.Center + new Vector2(dir.X, dir.Y) * 0.42f;
                float d = (p - from).LengthSquared();
                if (d < bd) { bd = d; best = p; }
            }
        return best;
    }

    /// <summary>일하는 사람이 손대는 곳.</summary>
    public static Vector2? WorkPoint(CrewMember c)
    {
        if (c.Job is not Job j) return null;
        if (j.Target is Furniture f) return f.Center;
        if (j.Order?.Target is WorkTarget t) return t.Furniture?.Center ?? t.Cell.Center;
        return null;
    }

    /// <summary>몸이 등과 작업 위치 사이에 있나 (등 → 작업 위치 선분에 몸이 반 칸 안으로 걸친다).</summary>
    public static bool ShadowOf(Vector2 lamp, Vector2 work, Vector2 body)
    {
        var v = work - lamp;
        float len2 = v.LengthSquared();
        if (len2 < 0.04f || (work - body).LengthSquared() < 0.09f) return false;
        float t = Vector2.Dot(body - lamp, v) / len2;
        if (t <= 0.08f || t >= 1f) return false;
        return (body - (lamp + v * t)).LengthSquared() < 0.45f * 0.45f;
    }

    // ───────────────────────── 시스템 틱 ─────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        // 0) 들고 가던 사람이 일을 놓쳤으면 그 자리에 내려놓는다 (짐과 진척이 그 자리에 남는다)
        foreach (var d in Devices)
            if (d.HeldBy is CrewMember h && (h.Dead || h.Job?.Activity is not PortableActivity)) Drop(d, h);
        // 떨어져 나간 방의 장비는 잃는다
        foreach (var d in Devices)
            if (!d.Lost && d.HeldBy == null && w.Ship.RoomAt(d.At) is { Detached: true }) { d.Lost = true; d.On = false; d.Running = false; }

        // 1) 전원 · 작동
        Array.Clear(CircuitLoad);
        bool smart = SmartChargeBudget(); // v16.20 주 컴퓨터가 충전을 회로 여유만큼만 차례로 (차단기를 올린 뒤 한꺼번에 몰리지 않게)
        foreach (var d in Devices)
        {
            d.Charging = false;
            var room = RoomOf(d);
            if (d.Lost || room == null) { d.Running = false; d.LightIntensity = 0f; continue; }
            // 창고(또는 꺼 둔 채 꽂힌 곳)에서 전지를 채운다
            if (d.Capacity > 0f && d.Charge < d.Capacity && d.HeldBy == null && !d.Broken && (d.Stored || !d.On) && OutletOk(room) && (!smart || ChargeSlot(room)))
            {
                d.Charge = MathF.Min(d.Capacity, d.Charge + ChargeKw * dt);
                d.Charging = true;
                CircuitLoad[room.Circuit] += ChargeKw;
            }
            bool run = false;
            float supply = 1f;
            if (d.Placed && d.On && !d.Broken && d.Spec.Kw > 0f)
            {
                switch (d.Plug)
                {
                    case PortablePlug.Outlet when d.Outlet is Room o && OutletOk(o):
                        run = true;
                        supply = Math.Clamp(o.PowerFlow, 0.3f, 1f);
                        CircuitLoad[o.Circuit] += d.Spec.Kw;
                        break;
                    case PortablePlug.Battery when d.Source is { Broken: false, Placed: true, On: true } s && s.Charge > 0f:
                        run = true;
                        s.Charge = MathF.Max(0f, s.Charge - d.Spec.Kw * dt * TechWeb.Mul(_w, "portable.drain")); // v16.14 저전력 작업등 · 휴대 셀
                        if (s.Charge <= 0f) { w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name}의 이동식 배터리가 다 됐다 — {Ko.IGa(d.Name)} 꺼진다"); }
                        break;
                }
                // 내장 전지로 버틴다 (작업등 · 선풍기)
                if (!run && d.Charge > 0f)
                {
                    run = true;
                    d.Charge = MathF.Max(0f, d.Charge - d.Spec.Kw * dt * TechWeb.Mul(_w, "portable.drain")); // v16.14
                }
            }
            d.Running = run;
            d.Supply = supply;
            if (run) d.Hours += dt;
            float low = d.Plug == PortablePlug.Battery && d.Source != null ? d.Source.ChargeFrac : d.Plug == PortablePlug.None ? d.ChargeFrac : 1f;
            d.LightIntensity = !run ? (d.Kind == PortableKind.Battery && d.Placed && d.On ? 0.3f : 0f)
                : d.Spec.Glow * supply * (low < 0.1f ? 0.55f : 1f);
            if (!run) continue;

            // 2) 효과
            switch (d.Kind)
            {
                case PortableKind.WorkLamp:
                    Stats.LampHours += dt;
                    break;
                case PortableKind.Heater:
                {
                    float kw = d.Spec.Kw * supply * supply; // 전압이 떨어지면 열이 제곱으로 준다
                    if (room.Air.Temperature < 26f) room.Air.Temperature = MathF.Min(26f, room.Air.Temperature + kw * 1.6f * 20f * dt / MathF.Max(8f, room.Volume));
                    Stats.HeatKwh += kw * dt;
                    // 열 × 물: 바닥 물이 빨리 마르고 습도가 내려간다
                    if (room.Flood > 0f) room.Flood = MathF.Max(0f, room.Flood - 3f * kw * dt);
                    room.Humidity = MathF.Max(0.2f, room.Humidity - 0.05f * kw * dt);
                    // 열 × 불: 침구 · 의자 곁에 둔 히터, 아무도 없는 방에 켜 둔 히터는 불을 낸다
                    bool nearSoft = Flammable(d.At);
                    bool alone = _awake.GetValueOrDefault(room.Id) == 0;
                    if (R.Chance(HeaterFireRate * (nearSoft ? 4f : 1f) * (alone ? 2.5f : 1f) * MathF.Max(0.3f, w.Body.SpreadMul(d.At)) * dt) && w.Fire.Ignite(d.At, 0.25f)) // 바닥재: 카펫 · 고무는 잘 탄다 · 젖은 바닥은 덜
                    {
                        Stats.HeaterFires++;
                        _heaterCaution = true;
                        w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name}에 {(alone ? "아무도 없이 " : "")}켜 둔 히터{(nearSoft ? " 곁의 침구" : "")}에서 불이 붙었다", d.InstalledBy);
                        MarkLog.Add(room.Marks, w.Tick, $"히터에서 불{(d.Forgotten ? " (잊고 켜 둔 것)" : "")}");
                    }
                    break;
                }
                case PortableKind.Fan:
                    if (room.Air.Temperature > 23f) room.Air.Temperature -= 1.4f * dt;
                    room.Air.Smoke = MathF.Max(0f, room.Air.Smoke * (1f - 0.35f * dt));
                    if (!Atmosphere.Vented(room)) room.Air.CO2 = MathF.Max(0.04f, room.Air.CO2 - 0.25f * dt);
                    break;
                case PortableKind.Purifier:
                    room.Air.Smoke = MathF.Max(0f, room.Air.Smoke * (1f - 0.8f * supply * dt));
                    room.Air.Toxin = MathF.Max(0f, room.Air.Toxin * (1f - 0.3f * supply * dt));
                    room.Smell = MathF.Max(0f, room.Smell - 0.4f * supply * dt);
                    break;
                case PortableKind.Pump:
                    if (room.Flood > 0f)
                    {
                        float take = MathF.Min(room.Flood, 360f * supply * dt); // 시간당 360L (양동이 둘쯤의 몫 — 사람이 퍼내는 것과 함께)
                        room.Flood -= take;
                        float back = take * 0.8f;
                        w.Water.Level = MathF.Min(w.Water.Capacity, w.Water.Level + back);
                        w.Moisture.Stats.Pumped += take;
                        w.Moisture.Stats.Recovered += back;
                        Stats.PumpedL += take;
                    }
                    break;
            }

            // 3) 고장: 오래 돌면 · 물에 젖으면 (양수기 · 카트 빼고 — 전자기기는 젖으면 불안정하다)
            bool wet = d.Kind != PortableKind.Pump && MoistureSystem.Depth(room) > 0.1f;
            if (R.Chance((wet ? 0.35f : 0.003f) * dt)) Break(d, room, wet);
        }

        // 물 × 전기: 바닥 물에 잠긴 케이블 (콘센트에서 끌어온 선) — 누전으로 차단기가 떨어지고, 물에 선 사람이 감전된다
        foreach (var d in Devices)
        {
            if (!d.Placed || !d.On || d.Plug != PortablePlug.Outlet || d.Outlet is not Room o || !OutletOk(o) || RoomOf(d) is not Room dr) continue;
            // 방 바닥 물 · 그 칸의 물웅덩이(배 본체 칸 상태 — 양수기 호스가 흘린 물 · 녹은 서리)
            float depth = MathF.Max(MoistureSystem.Depth(dr), 0.08f * w.Body.Mark(d.At, CellMark.Wet));
            if (depth < 0.06f) { d.Soak = MathF.Max(0f, d.Soak - dt); continue; }
            d.Soak += WetCableRate * depth * dt; // 이음매에 물이 스며든다 (오래 잠길수록)
            if (d.Soak >= 1f)
            {
                d.Soak = 0f;
                Stats.WetTrips++;
                _wetLesson = true;
                Trip(o.Circuit, CircuitLoad[o.Circuit], $"{dr.Name} 바닥 물에 잠긴 {d.Name} 케이블에서 누전");
            }
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || c.Room != dr || c.Suit != null || (c.Position - d.At.Center).LengthSquared() > 4f || !R.Chance(0.8f * WetCableRate * depth * dt)) continue;
                float dmg = 0.06f + 0.12f * depth;
                c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
                NeedsSystem.AddInjury(c.Vitals, dmg * 0.8f, "감전");
                c.Interrupt(w);
                Stats.CableShocks++;
                _wetLesson = true;
                w.Log.Add(w.Tick, LogKind.Warning, $"물에 잠긴 {d.Name} 케이블 곁에서 감전됐다 (체력 {c.Vitals.Health * 100:0}%)", c.Id);
                MarkLog.Add(c.Memory.Marks, w.Tick, $"{dr.Name}에서 {d.Name} 케이블에 감전");
            }
        }

        // 불 × 장비: 불길에 닿은 장비는 망가지고, 채워진 배터리는 터지듯 타오른다
        if (w.Fire.Count > 0)
            foreach (var d in Devices)
            {
                if (d.Lost || d.HeldBy != null || d.Broken || d.Kind == PortableKind.Cart || !w.Fire.AnyWithin(d.At, 0.9f)) continue;
                var fr = RoomOf(d);
                Break(d, fr ?? w.Ship.Rooms[0], false, "불길에");
                if (d.Kind == PortableKind.Battery && d.Charge > 0.3f * d.Capacity && w.Fire.Ignite(d.At, 0.5f))
                {
                    Stats.BatteryFires++;
                    d.Charge = 0f;
                    w.Log.Add(w.Tick, LogKind.Warning, $"{fr?.Name ?? "?"}의 이동식 배터리가 불길에 달아 터지듯 타올랐다");
                }
            }

        // 카트가 선 칸 · 장비 소리 (움직임 · 인접성 시스템이 본다)
        _cartCells.Clear();
        _noise.Clear();
        _clean.Clear();
        foreach (var d in Devices)
        {
            if (d.Kind == PortableKind.Cart && d.Placed) _cartCells.Add(d.At);
            if (d.Running && d.Placed && RoomOf(d) is Room nr)
            {
                float n = d.Kind switch { PortableKind.Pump => nr.Flood > 1f ? 0.45f : 0.3f, /* 물이 없으면 헛도는 소리 */ PortableKind.Fan => 0.22f, PortableKind.Purifier => 0.18f, PortableKind.Heater => 0.12f, _ => 0f };
                if (n > _noise.GetValueOrDefault(nr.Id)) _noise[nr.Id] = n;
                if (d.Kind == PortableKind.Purifier) _clean.Add(nr.Id);
            }
        }

        // 이동식 배터리 과부하: 한 배터리에 너무 많이 물리면 보호 회로가 끊는다
        foreach (var b in Devices)
        {
            if (b.Kind != PortableKind.Battery || !b.Placed || !b.On) continue;
            float kw = 0f;
            foreach (var d in Devices) if (d.Source == b && d.Running && d.Plug == PortablePlug.Battery) kw += d.Spec.Kw;
            if (kw <= BatteryMaxKw) continue;
            b.On = false;
            w.Log.Add(w.Tick, LogKind.Warning, $"{RoomOf(b)?.Name ?? "?"}의 이동식 배터리가 과부하({kw:0.0}kW)로 꺼졌다 — 한 배터리에 너무 많이 물렸다");
        }

        // 4) 회로 과부하 → 차단기 (몇 분 동안 넘치면)
        for (int i = 0; i < PowerGrid.CircuitCount; i++)
        {
            float load = CircuitLoad[i];
            // 멀티탭 · 플러그는 넘친 만큼 곧 달아오르고(냄새 · 컴퓨터 계측) · 차단기(열동식)는 넘친 정도의 제곱으로 데워진다 — 조금 넘치면 한참 버티고, 많이 넘치면 몇 분
            float r = load / OutletCapKw - 1f;
            if (r > 0f) { _hot[i] = MathF.Min(1.5f, _hot[i] + dt * r * 20f); _over[i] += dt * r * r * BreakerHeat; }
            else { _hot[i] = MathF.Max(0f, _hot[i] - dt * 2f); _over[i] = MathF.Max(0f, _over[i] - dt * 2f); }
            if (_over[i] >= 1f) Trip(i, load);
        }
        Links(dt); // 주 컴퓨터(과부하 예측 · 빈 방 히터 · 창고 빈 자리) · 배 본체(호스 물 · 카트 바퀴) · 히터 먼지

        // 5) 이동식 조명이 켜진 방 (Room.Dark가 본다)
        foreach (var r in _lit) r.PortableLit = 0;
        _lit.Clear();
        foreach (var d in Devices)
            if (d.Kind == PortableKind.WorkLamp && d.Running && d.Placed && RoomOf(d) is Room lr)
            {
                if (lr.PortableLit == 0) _lit.Add(lr);
                lr.PortableLit++;
            }

        // 6) 작업등: 일하는 사람 몸에 가리면 옮긴다
        foreach (var d in Devices)
        {
            if (d.Kind != PortableKind.WorkLamp || !d.Running || !d.Placed) continue;
            var lroom = RoomOf(d);
            if (d.User is CrewMember u && (u.Dead || u.Room != lroom)) d.User = null;
            if (d.User == null)
                foreach (var c in w.Crew) // 등 곁에서 일을 시작한 사람이 쓴다
                    if (c.CanAct && c.Room == lroom && c.Job?.Current is WorkToil && (c.Position - d.At.Center).LengthSquared() < d.Spec.LightRadius * d.Spec.LightRadius
                        && !Devices.Any(o => o != d && o.User == c)) { d.User = c; break; }
            if (d.User is not CrewMember user || user.Job?.Current is not WorkToil || WorkPoint(user) is not Vector2 work) { d.Shadowed = false; d.ShadowHours = 0f; continue; }
            d.Aim = work;
            d.Shadowed = ShadowOf(d.At.Center, work, user.Position);
            if (!d.Shadowed) { d.ShadowHours = 0f; continue; }
            d.ShadowHours += dt;
            if (d.ShadowHours >= 0.05f) MoveLamp(d, user, work); // 3분쯤 그림자 속에서 일하다 알아챈다
        }

        // 7) 등불 아래 식사: 정전된 방에 이동식 등 하나를 켜 놓고 모여 먹는다
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Job is not { Activity: EatActivity } job || c.Room is not Room er || er.PortableLit == 0 || !Unlit(er) || c.Pose != Pose.Sitting && c.Pose != Pose.Standing || c.IsMoving) continue;
            if (_meal.TryGetValue(c.Id, out var seen) && seen == job) continue;
            if (job.Current is not WaitToil) continue;
            _meal[c.Id] = job;
            Stats.LampMeals++;
            WarmPlate(c, er); // 음식: 전기가 없어 식은 접시는 켜 둔 히터 곁에 대 데운다
            int with = w.Crew.Count(o => o != c && !o.Dead && o.Room == er && o.Job?.Activity is EatActivity);
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f * MathF.Min(4, with));
            if (with > 0) c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.1f);
            if (with >= 1 && (!_gatherLog.TryGetValue(er.Id, out var last) || w.Tick - last > SimTime.Hours(3)))
            {
                _gatherLog[er.Id] = w.Tick;
                Stats.Gatherings++;
                var lamp = Devices.First(d => d.Kind == PortableKind.WorkLamp && d.Running && RoomOf(d) == er);
                w.Log.Add(w.Tick, LogKind.Life, $"정전된 {er.Name} — {(lamp.Plug == PortablePlug.Battery ? "이동식 배터리에 물린 " : "")}작업등 하나를 켜 놓고 {with + 1}명이 모여 먹는다", c.Id);
                MarkLog.Add(er.Marks, w.Tick, $"정전 — 작업등 하나 아래 모여 식사 ({with + 1}명)");
            }
        }

        // 8) 몇 분마다: 쓰임이 끝났나 · 잊음 · 요구 다시 보기
        if (w.Tick >= _nextScan)
        {
            _nextScan = w.Tick + World.SystemInterval * 4;
            Scan();
        }
    }

    private void Break(PortableDevice d, Room room, bool wet, string? why = null)
    {
        d.Broken = true;
        d.Running = false;
        d.On = false;
        d.LightIntensity = 0f;
        Stats.Breakdowns++;
        if (wet) Stats.Wet++;
        _w.Log.Add(_w.Tick, LogKind.Warning, wet ? $"{room.Name} 바닥 물에 젖은 {Ko.IGa(d.Name)} 고장 났다" : why != null ? $"{room.Name}의 {Ko.IGa(d.Name)} {why} 망가졌다" : $"{room.Name}의 {Ko.IGa(d.Name)} 오래 돌다 고장 났다");
        MarkLog.Add(room.Marks, _w.Tick, $"{d.Name} 고장{(wet ? " (물에 젖음)" : why != null ? $" ({why})" : "")}");
    }

    private void Trip(int circuit, float load, string? why = null)
    {
        var w = _w;
        _over[circuit] = 0f;
        _hot[circuit] = 0f;
        _tripAt[circuit] = w.Tick;
        if (w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is not Machine panel || panel.Faults.Any(f => f.Circuit == circuit)) return;
        var fault = new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = circuit };
        panel.Faults.Add(fault);
        panel.FaultCount++;
        w.Causes.OnFault(panel, fault);
        Stats.Trips++;
        _learned[circuit] = true;
        var on = Devices.Where(d => d.Placed && d.On && d.Plug == PortablePlug.Outlet && d.Outlet?.Circuit == circuit).ToList();
        string what = string.Join(" · ", on.GroupBy(d => d.Kind).OrderBy(g => g.Key).Select(g => g.Count() > 1 ? $"{g.First().Name} {g.Count()}대" : g.First().Name));
        w.Log.Add(w.Tick, LogKind.Warning, why != null ? $"{why} — {PowerGrid.CircuitName(circuit)} 회로 차단기가 떨어졌다" : $"{PowerGrid.CircuitName(circuit)} 회로 차단기가 떨어졌다 — 이동식 장비를 한 회로에 너무 많이 꽂았다 ({what} · {load:0.0}kW)");
        foreach (var r in on.Select(d => d.Outlet!).Distinct()) MarkLog.Add(r.Marks, w.Tick, $"{PowerGrid.CircuitName(circuit)} 회로 차단기 트립 — {why ?? what}");
        w.Board.RequestScan();
    }

    /// <summary>작업등이 몸에 가렸다: 일하던 사람이 몸을 비켜 등을 옆으로 옮긴다.</summary>
    private void MoveLamp(PortableDevice d, CrewMember user, Vector2 work)
    {
        var ship = _w.Ship;
        var room = RoomOf(d);
        if (room == null) return;
        Cell? best = null;
        float bs = float.MaxValue;
        foreach (var c in room.Cells)
        {
            if (!ship.IsWalkable(c) || c == user.Cell || c != d.At && Occupied(c)) continue;
            float dist = (c.Center - work).Length();
            if (dist > 2.6f || dist < 0.6f) continue;
            float s = dist + (ShadowOf(c.Center, work, user.Position) ? 100f : 0f) + ((c.Center - user.Position).Length() < 1.1f ? 0f : 0.3f);
            if (s < bs) { bs = s; best = c; }
        }
        d.ShadowHours = 0f;
        if (best is not Cell to || bs >= 100f || to == d.At) return;
        d.At = to;
        d.Shadowed = false;
        Stats.LampMoves++;
        _w.Log.Add(_w.Tick, LogKind.Work, "작업등이 몸에 가려 손이 안 보인다 — 등을 옆으로 옮겼다", user.Id);
    }

    /// <summary>들고 가던 장비를 그 자리에 내려놓는다 (중단 · 쓰러짐).</summary>
    internal void Drop(PortableDevice d, CrewMember c)
    {
        if (d.HeldBy != c) return;
        var at = c.Cell;
        if (_w.Ship.RoomAt(at) == null) // 문간 · 방 밖 칸에 내려놓으면 아무도 다시 못 찾는다 — 바로 옆 방 바닥에
            foreach (var dir in Cell.Dirs8)
                if (_w.Ship.RoomAt(at + dir) is Room nr && !nr.Detached && _w.Ship.IsWalkable(at + dir)) { at += dir; break; }
        d.HeldBy = null;
        d.OnCart = null;
        d.At = at;
        d.Stored = false;
        d.On = false;
        d.Purpose = null;
        d.DoneSince = _w.Tick;
        d.ClaimedBy = -1;
        Stats.Dropped++;
        _w.Log.Add(_w.Tick, LogKind.Life, $"{Ko.EulReul(d.Name)} 그 자리에 내려놓고 갔다 ({_w.Ship.RoomAt(at)?.Name ?? "통로"})", c.Id);
    }

    // ───────────────────────── 요구 보기 (몇 분마다) ─────────────────────────

    private bool StillNeeded(PortableDevice d)
    {
        if (d.Purpose is not string p) return false;
        var room = RoomOf(d);
        if (room == null) return false;
        if (PortableSpecs.Fixture(d.Kind) is FurnitureType ft && ModulesV15.Has(room, ft)) return false; // 정식 시설이 생겼다
        int stay = _staying.GetValueOrDefault(room.Id), awake = _awake.GetValueOrDefault(room.Id);
        string head = p.Split(':')[0];
        switch (head)
        {
            case "dark": return Unlit(room) && (Gathering(room) || awake > 0);
            case "work":
            {
                int id = int.Parse(p.Split(':')[2]);
                var c = _w.Crew.FirstOrDefault(x => x.Id == id);
                return Unlit(room) && c is { Dead: false } && c.Room == room && c.Job?.Order != null;
            }
            case "cold": return room.Air.Temperature < 19f && stay > 0;
            case "flood": return MoistureSystem.Depth(room) > 0.02f;
            case "air": return room.Air.Smoke > 0.03f || room.Air.Toxin > 0.02f || room.Smell > 0.3f;
            case "hot": return room.Air.Temperature > 25f && stay > 0;
            case "src": return Devices.Any(x => x.Source == d && x.Placed && x.On && x.DoneSince < 0);
            case "hold": return true; // 그대로 두라고 했다
            case "cart": return Devices.Any(x => x != d && x.Placed && x.DoneSince < 0 && x.Purpose != null && RoomOf(x) == room);
            default: return false;
        }
    }

    private static bool Gathering(Room r) => r.Type is RoomType.Mess or RoomType.Galley or RoomType.Lounge;

    private void Scan()
    {
        var w = _w;
        var ship = w.Ship;
        _staying.Clear();
        _awake.Clear();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room is not Room r || c.IsMoving) continue;
            _staying[r.Id] = _staying.GetValueOrDefault(r.Id) + 1;
            if (c.IsAwake && c.CanAct) _awake[r.Id] = _awake.GetValueOrDefault(r.Id) + 1;
        }
        // 맡은 사람이 그 일을 놓았으면 자리를 푼다
        foreach (var d in Devices)
            if (d.ClaimedBy >= 0 && w.Crew.FirstOrDefault(c => c.Id == d.ClaimedBy) is not { Job.Activity: PortableActivity }) d.ClaimedBy = -1;
        foreach (var key in _claims.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
            if (w.Crew.FirstOrDefault(c => c.Id == _claims[key]) is not { Job.Activity: PortableActivity }) { _claims.Remove(key); _claimed.Remove(key); }

        // 쓰임이 끝났나 · 잊음
        foreach (var d in Devices)
        {
            if (!d.Placed || d.Lost) continue;
            if (d.DoneSince < 0 && PortableSpecs.Fixture(d.Kind) is FurnitureType ft && RoomOf(d) is Room fr && ModulesV15.Has(fr, ft))
            {
                Stats.Settled++;
                w.Log.Add(w.Tick, LogKind.Ship, $"{fr.Name}에 정식 {Ko.IGa(Modules.Name(ft))} 생겼다 — 오래 두고 쓰던 {Ko.EunNeun(d.Name)} 창고로");
            }
            if (StillNeeded(d)) { d.DoneSince = -1; continue; }
            if (d.DoneSince < 0) d.DoneSince = w.Tick;
            if (w.Tick - d.DoneSince < SimTime.Minutes(30) || d.Purpose == null) continue;
            if (d.WillForget && !d.Forgotten && d.Kind != PortableKind.Cart)
            {
                d.Forgotten = true;
                Stats.Forgotten++;
                var room = RoomOf(d);
                w.Log.Add(w.Tick, LogKind.Life, $"{room?.Name ?? "?"}에 {Ko.EulReul(d.Name)} 두고 잊었다" + (d.On && d.Running ? " (켜 둔 채)" : ""), d.InstalledBy);
                if (room != null) MarkLog.Add(room.Marks, w.Tick, $"잊고 둔 {d.Name}");
            }
            // 잊은 장비도 이틀쯤 지나면 누군가 알아챈다
            if (d.Forgotten && w.Tick - d.DoneSince > SimTime.TicksPerDay * 2L && R.Chance(0.02f))
            {
                d.Forgotten = false;
                d.WillForget = false;
                Stats.Found++;
                w.Log.Add(w.Tick, LogKind.Life, $"{RoomOf(d)?.Name ?? "?"}에 남아 있던 {Ko.EulReul(d.Name)} 누군가 알아챘다 — 창고에 돌려놓는다");
            }
        }

        Needs.Clear();
        // 방마다: 캄캄함 · 추위 · 물 · 연기 · 더위
        foreach (var room in ship.LiveRooms)
        {
            if (room.OffLimits || room.Abandoned || room.Cells.Count == 0 || room.Air.Pressure < 50f) continue;
            int stay = _staying.GetValueOrDefault(room.Id), awake = _awake.GetValueOrDefault(room.Id);
            if (Unlit(room) && !PlacedIn(room, PortableKind.WorkLamp) && (Gathering(room) || awake > 0 && room.Type != RoomType.Corridor) && CenterSpot(room) is Cell ds)
                Need(PortableTask.Setup, PortableKind.WorkLamp, room, ds, !OutletOk(room), room.Powered ? "조명이 나갔다" : "정전 — 캄캄하다",
                    (Gathering(room) ? 0.5f : 0.38f) + 0.03f * Math.Min(5, awake), $"dark:{room.Id}");
            if (room.Air.Temperature < 14f && stay > 0 && !PlacedIn(room, PortableKind.Heater) && HeaterSpot(room) is Cell hs)
                Need(PortableTask.Setup, PortableKind.Heater, room, hs, OutletFor(room, PortableSpecs.Of(PortableKind.Heater).Kw) == null, $"춥다 ({room.Air.Temperature:0}℃)",
                    MathF.Min(0.75f, 0.35f + (14f - room.Air.Temperature) * 0.03f), $"cold:{room.Id}");
            float depth = MoistureSystem.Depth(room);
            if (depth > 0.06f && w.Moisture.Noticed(room) && !PlacedIn(room, PortableKind.Pump) && CenterSpot(room) is Cell ps)
                Need(PortableTask.Setup, PortableKind.Pump, room, ps, _wetLesson || OutletFor(room, PortableSpecs.Of(PortableKind.Pump).Kw) == null,
                    $"바닥에 물 {MoistureSystem.DepthCm(room):0}cm", MathF.Min(0.85f, 0.45f + depth * 0.6f), $"flood:{room.Id}");
            if ((room.Air.Smoke > 0.12f || room.Air.Toxin > 0.05f || room.Smell > 0.6f) && w.Fire.CountIn(room) == 0 && (stay > 0 || !Atmosphere.Vented(room))
                && !PlacedIn(room, PortableKind.Purifier) && !PlacedIn(room, PortableKind.Fan) && CenterSpot(room) is Cell ast)
                Need(PortableTask.Setup, PortableKind.Purifier, room, ast, !OutletOk(room), room.Air.Smoke > 0.12f ? "연기가 남았다" : room.Air.Toxin > 0.05f ? "가스가 남았다" : "냄새가 고였다",
                    0.35f + MathF.Min(0.3f, room.Air.Smoke + room.Air.Toxin), $"air:{room.Id}");
            if (room.Air.Temperature > 28f && stay > 0 && !PlacedIn(room, PortableKind.Fan) && CenterSpot(room) is Cell fs)
                Need(PortableTask.Setup, PortableKind.Fan, room, fs, !OutletOk(room), $"덥다 ({room.Air.Temperature:0}℃)", 0.33f, $"hot:{room.Id}");
        }
        // 캄캄한 방에서 일하는 사람: 작업등을 곁에 (등에서 멀거나 등이 없으면)
        foreach (var c in w.Crew)
        {
            if (!c.CanAct || c.Suit != null || c.Job?.Current is not WorkToil || c.Room is not Room r || !Unlit(r) || r.OffLimits) continue;
            if (WorkPoint(c) is not Vector2 work) continue;
            if (Devices.Any(d => d.Kind == PortableKind.WorkLamp && d.Placed && d.On && RoomOf(d) == r && (d.At.Center - c.Position).LengthSquared() < d.Spec.LightRadius * d.Spec.LightRadius)) continue;
            // 사람들은 곁에 내려놓는다 (몸 뒤일 수도 — 가리면 옮긴다)
            if (FreeSpot(r, c.Position, c.Cell) is not Cell ls) continue;
            Need(PortableTask.Setup, PortableKind.WorkLamp, r, ls, !OutletOk(r), $"{c.Name}의 손이 안 보인다 (캄캄한 {r.Name})", 0.5f, $"work:{r.Id}:{c.Id}", c, work);
        }
        foreach (var d in Devices)
        {
            if (d.Lost || d.HeldBy != null) continue;
            var room = RoomOf(d);
            if (room == null) continue;
            // 전기가 없는 장비 · 배터리가 바닥나는 장비 → 배터리를 갖다 댄다
            if (d.Placed && d.On && !d.Broken && d.DoneSince < 0 && d.Kind is not (PortableKind.Battery or PortableKind.Cart) && Starving(d)
                && FreeSpot(room, d.At.Center, d.At) is Cell fs)
                Need(PortableTask.Feed, PortableKind.Battery, room, fs, true, $"{room.Name}의 {Ko.EunNeun(d.Name)} 전기가 없다", 0.45f, $"feed:{d.Id}", device: d);
            // 다 쓴 장비 → 회수 (잊은 것 빼고)
            else if (d.Placed && d.DoneSince >= 0 && !d.Forgotten && w.Tick - d.DoneSince >= SimTime.Minutes(30) && d.ClaimedBy < 0
                     && !(d.Kind == PortableKind.Battery && Devices.Any(x => x.Source == d && x.Placed && x.On)))
            {
                bool aisle = d.Kind == PortableKind.Cart && room.Type == RoomType.Corridor;
                if (aisle && Crisis.Acting(w)) { Need(PortableTask.Retrieve, d.Kind, room, d.At, false, $"비상 — {room.Name}의 카트를 치운다 (대피로)", 0.75f, $"ret:{d.Id}", device: d); continue; }
                bool recall = Recalled(d); // 주 컴퓨터가 창고 빈 자리를 알렸다
                Need(PortableTask.Retrieve, d.Kind, room, d.At, false, aisle ? $"{room.Name}의 카트가 통로를 막는다" : recall ? $"{room.Name}에 두고 잊은 {d.Name} — 주 컴퓨터가 창고 빈 자리를 알렸다" : $"{room.Name}에 둔 {d.Name} — 다 썼다",
                    aisle ? 0.42f : (d.On ? 0.34f : 0.3f) + (recall ? 0.16f : 0f), $"ret:{d.Id}", device: d);
            }
            // 고장 난 장비 → 고친다
            if (d.Broken && d.ClaimedBy < 0)
                Need(PortableTask.Fix, d.Kind, room, d.At, false, $"고장 난 {d.Name}", 0.24f, $"fix:{d.Id}", device: d);
        }
        // 한 회로에 너무 많이 꽂았다 → 큰 것부터 하나씩 뽑거나 옆 방 콘센트로
        for (int i = 0; i < PowerGrid.CircuitCount; i++)
        {
            float kw = ProjectedKw(i);
            bool warned = Warned(i) && !_learned[i]; // 차단기는 아직 — 주 컴퓨터 방송 · 뜨거운 콘센트로 안다
            if (kw <= OutletCapKw || !_learned[i] && !Warned(i)) continue;
            var asked = AskedUnplug(i); // v16.20 주 컴퓨터가 차단기를 붙잡고 콕 집어 부탁한 사람
            foreach (var d in Devices.Where(d => d.Placed && d.On && d.Plug == PortablePlug.Outlet && d.Outlet?.Circuit == i).OrderByDescending(d => d.Spec.Kw).ThenByDescending(d => d.PlacedSince).ThenBy(d => d.Id))
            {
                if (kw <= OutletCapKw) break;
                kw -= d.Spec.Kw;
                if (RoomOf(d) is Room r)
                    Need(PortableTask.Unplug, d.Kind, r, d.At, false, asked != null ? $"주 컴퓨터 지시 — {PowerGrid.CircuitName(i)} 회로 차단기가 같은 이유로 또 떨어졌다, 하나 빼 달라" : warned ? $"{PowerGrid.CircuitName(i)} 회로 과부하 — {_warnWhy[i]}" : $"{PowerGrid.CircuitName(i)} 회로에 너무 많이 꽂았다 (차단기가 떨어졌다)",
                        asked != null ? 0.85f : warned ? 0.8f : 0.7f, $"unplug:{d.Id}", who: asked, device: d);
            }
        }
        ComputerNeeds(); // 주 컴퓨터가 짚은 빈 방 히터
        Needs.Sort((a, b) => b.Urgency.CompareTo(a.Urgency));
        _choice.Clear();
    }

    /// <summary>전기가 없거나 곧 떨어진다 (차단기가 떨어진 회로는 곧 올린다 — 기다린다 · 히터를 배터리로 돌리지는 않는다).</summary>
    private bool Starving(PortableDevice d) => d.Plug switch
    {
        PortablePlug.None => d.Capacity <= 0f || d.ChargeFrac < 0.15f,
        PortablePlug.Battery => d.Source is not { } s || s.ChargeFrac < 0.15f || !s.On || s.Broken,
        _ => d.Outlet is Room o && !OutletOk(o) && !Tripped(o.Circuit) && d.Kind != PortableKind.Heater,
    };

    private bool Tripped(int circuit) =>
        _w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine m && m.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == circuit);

    private void Need(PortableTask task, PortableKind kind, Room room, Cell spot, bool battery, string why, float urgency, string key, CrewMember? who = null, Vector2? aim = null, PortableDevice? device = null)
        => Needs.Add(new PortableNeed { Task = task, Kind = kind, Room = room, Spot = spot, Battery = battery, Why = why, Urgency = urgency, Key = key, For = who, Aim = aim ?? Vector2.Zero, Device = device });

    // ───────────────────────── 고르기 (승무원 판단) ─────────────────────────

    internal PortableChoice? Choose(CrewMember c, DistanceField dist)
    {
        if (_choice.TryGetValue(c.Id, out var memo) && memo.tick == _w.Tick) return memo.choice;
        PortableChoice? best = null;
        foreach (var n in Needs)
        {
            if (_claims.TryGetValue(n.Key, out var who) && who != c.Id) continue;
            if (!dist.Reachable(n.Spot)) continue;
            var ch = Resolve(n, c, dist);
            if (ch == null) continue;
            if (best == null || ch.Score > best.Score) best = ch;
        }
        _choice[c.Id] = (_w.Tick, best);
        return best;
    }

    private PortableDevice? Nearest(PortableKind k, CrewMember c, DistanceField dist, Func<PortableDevice, bool>? ok = null)
    {
        PortableDevice? best = null;
        int bd = int.MaxValue;
        foreach (var d in Devices)
        {
            if (d.Kind != k || !Available(d, c) || ok != null && !ok(d)) continue;
            if (!dist.Reachable(d.At)) continue;
            int g = dist.Get(d.At);
            if (g < bd) { bd = g; best = d; }
        }
        return best;
    }

    private PortableChoice? Resolve(PortableNeed n, CrewMember c, DistanceField dist)
    {
        float far = MathF.Min(0.15f, dist.Get(n.Spot) / 4000f);
        float skill = 0.04f * c.SkillLevel(Skill.Electrical);
        bool crisis = Crisis.Acting(_w);
        switch (n.Task)
        {
            case PortableTask.Retrieve or PortableTask.Unplug or PortableTask.Fix:
            {
                var d = n.Device!;
                if (d.HeldBy != null || d.ClaimedBy >= 0 && d.ClaimedBy != c.Id || !dist.Reachable(d.At)) return null;
                float s = n.Urgency - far + (n.Task == PortableTask.Fix ? 0.1f * c.SkillLevel(Skill.Electrical) - 0.05f : skill);
                if (n.Task == PortableTask.Unplug && n.For != null) s += n.For == c ? 0.2f + 0.3f * _w.Automation.Trusts.Of(c) : -0.2f; // v16.20 콕 집힌 사람이 간다 (여럿이 몰려가지 않게) · v16.26 컴퓨터를 믿을수록 밥 · 구경보다 먼저
                if (crisis && n.Task != PortableTask.Unplug && n.Urgency < 0.7f) s *= 0.3f;
                return new PortableChoice(n, d, null, null, null, s, n.Why);
            }
            case PortableTask.Feed:
            {
                var b = Nearest(PortableKind.Battery, c, dist, x => x.ChargeFrac > 0.3f);
                if (b == null) return null;
                return new PortableChoice(n, b, null, null, null, n.Urgency - far + skill, n.Why + " → 이동식 배터리를 갖다 댄다");
            }
            default:
            {
                var main = Nearest(n.Kind, c, dist);
                string why = n.Why;
                bool swap = false;
                if (main == null && PortableSpecs.Of(n.Kind).Alt is PortableKind alt && (main = Nearest(alt, c, dist)) != null)
                {
                    swap = true;
                    why += $" · {Ko.EunNeun(PortableSpecs.Name(n.Kind))} 다 쓰는 중이라 {Ko.EulReul(PortableSpecs.Name(alt))}";
                }
                if (main == null)
                {
                    // 먼저 쓰는 사람이 있다: 그 곁에서 잠깐 기다린다 (한 번 기다렸으면 한동안 다른 일)
                    var busy = Devices.Where(d => d.Kind == n.Kind && !d.Lost && !d.Broken && Busy(d) && dist.Reachable(d.HeldBy?.Cell ?? d.At)).OrderBy(d => dist.Get(d.At)).FirstOrDefault();
                    if (busy == null || _waited.TryGetValue(n.Key, out var wt) && _w.Tick - wt < SimTime.Hours(1)) return null;
                    string user = busy.User?.Name ?? busy.HeldBy?.Name ?? (busy.InstalledBy >= 0 ? _w.Crew.FirstOrDefault(x => x.Id == busy.InstalledBy)?.Name : null) ?? "누군가";
                    return new PortableChoice(n, null, null, null, busy, (n.Urgency - far) * 0.85f, $"{n.Why} · {Ko.EunNeun(PortableSpecs.Name(n.Kind))} {Ko.IGa(user)} 쓰는 중 — 기다린다");
                }
                PortableDevice? bat = null, cart = null;
                if (n.Battery && main.Kind != PortableKind.Battery)
                    bat = Nearest(PortableKind.Battery, c, dist, x => x.ChargeFrac > 0.3f && x != main);
                int weight = main.Spec.Weight + (bat?.Spec.Weight ?? 0);
                if (weight > PortableSpecs.HandLimit)
                {
                    var mroom = _w.Ship.RoomAt(main.At);
                    cart = Nearest(PortableKind.Cart, c, dist, x => _w.Ship.RoomAt(x.At) == mroom);
                    if (cart == null) bat = null; // 카트가 없으면 본체만 먼저 (배터리는 다음에)
                }
                float score = n.Urgency - far + skill;
                if (crisis && n.Kind is PortableKind.Fan or PortableKind.Purifier) score *= 0.5f;
                return new PortableChoice(n, main, bat, cart, null, score, why) { Swap = swap };
            }
        }
    }

    internal void Claim(string key, CrewMember c, params PortableDevice?[] ds)
    {
        _claims[key] = c.Id;
        var list = new List<PortableDevice>();
        foreach (var d in ds) if (d != null) { d.ClaimedBy = c.Id; list.Add(d); }
        _claimed[key] = list;
    }

    internal void Release(string key, CrewMember c)
    {
        if (!_claims.TryGetValue(key, out var who) || who != c.Id) return;
        _claims.Remove(key);
        if (_claimed.Remove(key, out var list))
            foreach (var d in list) if (d.ClaimedBy == c.Id && d.HeldBy != c) d.ClaimedBy = -1;
        _choice.Remove(c.Id);
    }

    internal void MarkWaited(string key) { _waited[key] = _w.Tick; Stats.Waits++; }

    /// <summary>하는 중인 장비 일의 무게 — 맡은 요구는 목록에서 빠지므로 (안 그러면 가는 길에 아무 일에나 밀려 들고 가던 장비를 복도에 내려놓는다).</summary>
    private readonly Dictionary<int, (float score, string why)> _doing = new();
    internal void Doing(CrewMember c, float score, string why) => _doing[c.Id] = (score, why);
    internal void Done(CrewMember c) => _doing.Remove(c.Id);
    internal (float score, string why)? DoingScore(CrewMember c)
    {
        if (!_doing.TryGetValue(c.Id, out var d)) return null;
        // 손에 든 장비가 있으면 마저 갖다 놓는다 (조금 더 버틴다)
        foreach (var x in Devices) if (x.HeldBy == c) return (d.score + 0.12f, d.why);
        return d;
    }

    internal bool PickUp(PortableDevice d, CrewMember c, PortableDevice? cart)
    {
        if (d.Lost || d.HeldBy != null && d.HeldBy != c || d.ClaimedBy >= 0 && d.ClaimedBy != c.Id) return false;
        if (!d.Stored && d.Placed && (d.At.Center - c.Position).LengthSquared() > 2.5f) return false;
        // 다른 곳에 꽂혀 있던 것: 끄고 뽑는다 (그 배터리에 물린 장비도 놓는다)
        foreach (var x in Devices) if (x.Source == d) { x.Source = null; if (x.Plug == PortablePlug.Battery) { x.Plug = PortablePlug.None; x.CableTo = null; } }
        d.On = false;
        d.Running = false;
        d.Plug = PortablePlug.None;
        d.Outlet = null;
        d.Source = null;
        d.CableTo = null;
        d.HoseTo = null;
        d.User = null;
        d.Stored = false;
        d.Forgotten = false;
        d.HeldBy = c;
        d.ClaimedBy = c.Id;
        d.OnCart = cart != null && cart != d && cart.HeldBy == c ? cart : null;
        d.LightIntensity = 0f;
        return true;
    }

    /// <summary>들고 온 장비를 놓고 꽂고 켠다.</summary>
    internal bool Install(CrewMember c, PortableNeed n, PortableDevice main, PortableDevice? bat, PortableDevice? cart)
    {
        var w = _w;
        if (main.HeldBy != c) return false;
        var room = n.Room;
        var spot = !Occupied(n.Spot) ? n.Spot : FreeSpot(room, n.Spot.Center) ?? c.Cell;
        Place(main, spot, c);
        bool withBat = bat?.HeldBy == c;
        if (withBat)
        {
            var bs = FreeSpot(room, spot.Center, spot) ?? spot;
            Place(bat!, bs, c);
            bat!.On = true;
            bat.Purpose = $"src:{main.Id}";
        }
        bool withCart = cart?.HeldBy == c;
        if (withCart)
        {
            var cs = FreeSpot(room, spot.Center, spot) ?? spot;
            Place(cart!, cs, c);
            cart!.Purpose = "cart";
            Stats.CartTrips++;
            if (room.Type == RoomType.Corridor) Stats.AisleCarts++;
        }
        main.Purpose = n.Key;
        main.Why = n.Why;
        main.User = n.For;
        main.Aim = n.Aim;
        main.WillForget = R.Chance(ForgetChance(c));
        Connect(main, room, withBat ? bat : null);
        main.On = true;
        if (main.Kind == PortableKind.Pump)
        {
            var door = room.Doors.OrderBy(d => (d.Cell.Center - spot.Center).LengthSquared()).ThenBy(d => d.Id).FirstOrDefault();
            if (door != null) main.HoseTo = door.Cell.Center;
        }
        Stats.Setups++;
        string names = withBat ? $"{Ko.WaGwa("이동식 배터리")} {main.Name}" : main.Name;
        string plug = main.Plug switch
        {
            PortablePlug.Battery => "배터리에 물렸다",
            PortablePlug.Outlet when main.Outlet != room => $"옆 {main.Outlet!.Name} 콘센트까지 케이블을 끌어왔다",
            PortablePlug.Outlet => $"{PowerGrid.CircuitName(room.Circuit)} 회로 콘센트",
            _ => main.Capacity > 0f ? "내장 전지로" : "꽂을 데가 없다 — 배터리를 기다린다",
        };
        w.Log.Add(w.Tick, LogKind.Work, $"{room.Name} {n.Why} — {Ko.EulReul(names)} 가져와 켰다 ({plug}){(withCart ? " · 카트로 날랐다" : "")}", c.Id);
        MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: {names} 설치 ({n.Why})");
        return true;
    }

    private void Place(PortableDevice d, Cell at, CrewMember c)
    {
        d.HeldBy = null;
        d.OnCart = null;
        d.At = at;
        d.Stored = false;
        d.ClaimedBy = -1;
        d.PlacedSince = _w.Tick;
        d.DoneSince = -1;
        d.Forgotten = false;
        d.InstalledBy = c.Id;
    }

    /// <summary>전기 잇기: 배터리가 곁에 있으면 배터리 → 그 방(또는 옆 방) 콘센트 → 내장 전지.</summary>
    private void Connect(PortableDevice d, Room room, PortableDevice? bat)
    {
        d.Plug = PortablePlug.None;
        d.Outlet = null;
        d.Source = null;
        d.CableTo = null;
        if (d.Spec.Kw <= 0f) return;
        if (bat != null)
        {
            d.Plug = PortablePlug.Battery;
            d.Source = bat;
            d.CableTo = bat.At.Center;
            return;
        }
        if (OutletFor(room, d.Spec.Kw) is Room o)
        {
            d.Plug = PortablePlug.Outlet;
            d.Outlet = o;
            d.CableTo = OutletPoint(o, d.At.Center);
        }
    }

    /// <summary>다 쓰고 제자리에 두지 않을 가능성: 깜빡하는 버릇 · 피곤 · 바쁨.</summary>
    private float ForgetChance(CrewMember c) =>
        Math.Clamp(0.18f + (c.Habits.Contains(Habit.Forgetful) ? 0.3f : 0f) + (c.Habits.Contains(Habit.Messy) ? 0.15f : 0f) + (c.Needs.Rest < 0.3f ? 0.1f : 0f) - 0.15f * c.Traits.Diligence, 0.03f, 0.7f);

    internal bool Feed(CrewMember c, PortableNeed n, PortableDevice bat)
    {
        var d = n.Device!;
        if (bat.HeldBy != c || !d.Placed) return false;
        var room = RoomOf(d) ?? n.Room;
        var spot = FreeSpot(room, d.At.Center, d.At) ?? c.Cell;
        var old = d.Source;
        Place(bat, spot, c);
        bat.On = true;
        bat.Purpose = $"src:{d.Id}";
        d.Plug = PortablePlug.Battery;
        d.Source = bat;
        d.CableTo = bat.At.Center;
        d.On = true;
        Stats.Feeds++;
        if (old != null) old.Purpose = null; // 다 쓴 배터리는 창고로 (충전)
        _w.Log.Add(_w.Tick, LogKind.Work, $"{room.Name}의 {Ko.EulReul(d.Name)} 이동식 배터리에 물렸다" + (old != null ? " (다 된 배터리는 바꿔 놓았다)" : ""), c.Id);
        return true;
    }

    /// <summary>다 쓴 장비를 집어 든다 (그 배터리 · 같은 방의 카트도).</summary>
    internal List<PortableDevice> Collect(CrewMember c, PortableDevice d)
    {
        var got = new List<PortableDevice>();
        var room = RoomOf(d);
        var cart = d.Kind == PortableKind.Cart ? d : Devices.FirstOrDefault(x => x.Kind == PortableKind.Cart && x.Placed && x.DoneSince >= 0 && x.ClaimedBy < 0 && RoomOf(x) == room);
        if (cart != null && cart != d && !PickUp(cart, c, null)) cart = null;
        if (cart != null && cart != d) got.Add(cart);
        if (!PickUp(d, c, cart)) return got;
        got.Add(d);
        int weight = d.Spec.Weight;
        foreach (var x in Devices.Where(x => x != d && x.Placed && x.DoneSince >= 0 && x.ClaimedBy < 0 && x.Kind != PortableKind.Cart && RoomOf(x) == room && !x.Forgotten).OrderBy(x => x.Id).ToList())
        {
            int limit = cart != null ? PortableSpecs.CartLimit : PortableSpecs.HandLimit;
            if (weight + x.Spec.Weight > limit || (x.At.Center - c.Position).LengthSquared() > 9f) continue;
            if (!PickUp(x, c, cart)) continue;
            weight += x.Spec.Weight;
            got.Add(x);
        }
        return got;
    }

    /// <summary>창고 제자리에 내려놓는다.</summary>
    internal void Stow(CrewMember c, List<PortableDevice> got)
    {
        foreach (var d in got)
        {
            if (d.HeldBy != c) continue;
            d.HeldBy = null;
            d.OnCart = null;
            d.At = d.Home;
            d.Stored = true;
            d.On = false;
            d.Running = false;
            d.Purpose = null;
            d.Why = null;
            d.User = null;
            d.DoneSince = -1;
            d.PlacedSince = -1;
            d.Forgotten = false;
            d.WillForget = false;
            d.ClaimedBy = -1;
            d.CableTo = null;
            d.HoseTo = null;
            _recalled.Remove(d.Id);
            Stats.Returned++;
        }
        var names = got.Where(d => d.Stored).GroupBy(d => d.Kind).OrderBy(g => g.Key).Select(g => g.First().Name).ToList();
        if (names.Count > 0) _w.Log.Add(_w.Tick, LogKind.Work, $"다 쓴 {Ko.EulReul(string.Join(" · ", names))} 창고 제자리에 돌려놓았다", c.Id);
    }

    internal void Unplug(CrewMember c, PortableDevice d)
    {
        var w = _w;
        int oc = d.Plug == PortablePlug.Outlet && d.Outlet != null ? d.Outlet.Circuit : -1;
        bool heedOver = oc >= 0 && ProjectedKw(oc) > OutletCapKw && Warned(oc) && !_learned[oc]; // 과부하 경고(방송 · 뜨거운 콘센트)를 듣고 차단기 전에
        if (TurnOffFlagged(c, d)) { if (heedOver) { Stats.Unplugged++; Stats.HeededWarns++; } return; } // 주 컴퓨터가 짚은 빈 방 히터: 끈다 (그 회로 과부하도 풀린다)
        if (!d.Placed || d.Plug != PortablePlug.Outlet || d.Outlet is not Room was) return;
        int circuit = was.Circuit;
        bool heed = Warned(circuit) && !_learned[circuit];
        var room = RoomOf(d) ?? was;
        // 다른 회로의 옆 방 콘센트에 여유가 있으면 그리로 옮겨 꽂는다
        Room? alt = null;
        foreach (var o in new[] { room }.Concat(room.Doors.OrderBy(x => x.Id).Select(x => x.RoomA == room ? x.RoomB : x.RoomA)))
            if (o != null && o.Circuit != circuit && OutletOk(o) && ProjectedKw(o.Circuit) + d.Spec.Kw <= OutletCapKw) { alt = o; break; }
        if (alt != null)
        {
            d.Outlet = alt;
            d.CableTo = OutletPoint(alt, d.At.Center);
            Stats.Rerouted++;
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(d.Name)} {alt.Name} 콘센트({PowerGrid.CircuitName(alt.Circuit)} 회로)로 옮겨 꽂았다 — {PowerGrid.CircuitName(circuit)} 회로에 너무 많았다", c.Id);
        }
        else
        {
            d.On = false;
            d.Plug = PortablePlug.None;
            d.Outlet = null;
            d.CableTo = null;
            d.Purpose = null;
            w.Log.Add(w.Tick, LogKind.Work, heed ? $"{Ko.EulReul(d.Name)} 뽑아 뒀다 — {PowerGrid.CircuitName(circuit)} 회로 과부하 ({_warnWhy[circuit]}) · 차단기가 떨어지기 전에" : $"{Ko.EulReul(d.Name)} 뽑아 뒀다 — {PowerGrid.CircuitName(circuit)} 회로에 너무 많이 꽂혀 차단기가 떨어졌다", c.Id);
        }
        Stats.Unplugged++;
        if (heed) Stats.HeededWarns++;
        MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: {d.Name} 뽑음 ({PowerGrid.CircuitName(circuit)} 회로 과부하)");
        // v16.20 주 컴퓨터가 콕 집어 부탁한 사람은 온 김에 한도 아래로 내려갈 때까지 같은 방 것을 마저 뺀다
        if (AskedUnplug(circuit) == c && ProjectedKw(circuit) > OutletCapKw)
            foreach (var x in Devices.Where(x => x != d && x.Placed && x.On && x.HeldBy == null && x.Plug == PortablePlug.Outlet && x.Outlet?.Circuit == circuit && RoomOf(x) == room).OrderByDescending(x => x.Spec.Kw).ThenBy(x => x.Id).ToList())
            {
                if (ProjectedKw(circuit) <= OutletCapKw) break;
                if (x.Outlet?.Circuit == circuit && x.On) Unplug(c, x);
            }
    }

    internal void Fix(CrewMember c, PortableDevice d)
    {
        if (!d.Broken) return;
        d.Broken = false;
        d.Hours = 0f;
        Stats.Fixed++;
        _w.Log.Add(_w.Tick, LogKind.Work, $"고장 난 {Ko.EulReul(d.Name)} 고쳤다", c.Id);
    }

    // ───────────────────────── 다른 시스템이 묻는 것 ─────────────────────────

    /// <summary>타기 쉬운 것(침대 · 간이침대 · 의자 · 선반)이 곁에 있는 칸.</summary>
    private bool Flammable(Cell at)
    {
        foreach (var dir in Cell.Dirs8)
            if (_w.Ship.FurnitureAt(at + dir) is { Type: FurnitureType.Bed or FurnitureType.Cot or FurnitureType.Seat or FurnitureType.Shelf }) return true;
        return _w.Ship.FurnitureAt(at) is { Type: FurnitureType.Bed or FurnitureType.Cot };
    }

    /// <summary>방에 퍼지는 장비 소리 0~1 (양수기 · 선풍기 · 청정기 · 히터 팬 — 잠을 깨운다).</summary>
    public float NoiseIn(Room r) => _noise.Count == 0 ? 0f : _noise.GetValueOrDefault(r.Id);

    /// <summary>공기청정기가 도는 방은 냄새가 덜 고인다.</summary>
    public float SmellMul(Room r) => _clean.Count > 0 && _clean.Contains(r.Id) ? 0.4f : 1f;

    /// <summary>걷는 속도: 통로에 선 카트를 비켜 간다 (뛰어 달아나던 사람은 걸려 늦는다).</summary>
    public float SqueezeMul(CrewMember c, List<Cell> path)
    {
        if (_cartCells.Count == 0 || c.PathIndex >= path.Count) return 1f;
        var next = path[c.PathIndex];
        if (!_cartCells.Contains(next) && !_cartCells.Contains(c.Cell)) return 1f;
        bool fleeing = c.Dashing || c.Job?.Activity is EvacuateActivity || c.Job?.Urgent == true;
        if (fleeing && _cartCells.Contains(next))
        {
            var cart = Devices.FirstOrDefault(d => d.Kind == PortableKind.Cart && d.Placed && d.At == next);
            if (cart != null && _snagged.Add((c.Id, cart.Id)))
            {
                Stats.CartSnags++;
                _w.Log.Add(_w.Tick, LogKind.Warning, $"{_w.Ship.RoomAt(next)?.Name ?? "통로"}에 세워 둔 카트에 걸려 늦었다", c.Id);
                if (_w.Ship.RoomAt(next) is Room rr) MarkLog.Add(rr.Marks, _w.Tick, $"{c.Name}: 카트에 걸림 (급한 길)");
            }
        }
        return fleeing ? 0.4f : 0.6f;
    }

    /// <summary>회로마다 이동식 장비가 끄는 전력 (배전반 수요에 더한다).</summary>
    public float CircuitKw(int circuit) => circuit >= 0 && circuit < CircuitLoad.Length ? CircuitLoad[circuit] : 0f;

    private PortableDevice? LampFor(CrewMember c, Room r)
    {
        PortableDevice? best = null;
        float bd = float.MaxValue;
        foreach (var d in Devices)
        {
            if (d.Kind != PortableKind.WorkLamp || !d.Running || !d.Placed || _w.Ship.RoomAt(d.At) != r) continue;
            float dd = (d.At.Center - c.Position).LengthSquared();
            if (d.User == c) dd -= 100f;
            if (dd < bd) { bd = dd; best = d; }
        }
        return best;
    }

    /// <summary>작업 속도: 천장 불이 없는 방에서 이동식 등에 기대 일할 때 (등에서 멀면 캄캄하고 · 몸에 가리면 손이 안 보인다).</summary>
    public float LampWorkMul(CrewMember c)
    {
        if (c.Suit != null || c.Room is not Room r || r.PortableLit == 0 || !Unlit(r)) return 1f;
        var lamp = LampFor(c, r);
        if (lamp == null || (lamp.At.Center - c.Position).Length() > lamp.Spec.LightRadius) return 0.8f;
        if (lamp.User == c && lamp.Shadowed) return 0.88f;
        return lamp.LightIntensity < 0.6f ? 0.92f : 1f;
    }

    /// <summary>실수 배율: 캄캄하면 · 등에서 멀면 · 몸에 가리면.</summary>
    public float DarkMistake(CrewMember c)
    {
        if (c.Suit != null || c.Room is not Room r) return 1f;
        if (r.Dark) return 1.5f;
        float k = LampWorkMul(c);
        return k <= 0.8f ? 1.4f : k < 0.9f ? 1.25f : 1f;
    }

    /// <summary>방 살펴보기 한 줄.</summary>
    public string? RoomLine(Room room)
    {
        var parts = new List<string>();
        foreach (var d in Devices.Where(d => d.Placed && RoomOf(d) == room).OrderBy(d => d.Kind).ThenBy(d => d.Id))
        {
            string state = d.Broken ? "고장" : d.Running ? "켜짐" : d.On ? "전기 없음" : "꺼짐";
            string power = d.Kind == PortableKind.Battery ? $" {d.ChargeFrac * 100:0}%"
                : d.Plug == PortablePlug.Battery && d.Source != null ? $" · 배터리 {d.Source.ChargeFrac * 100:0}%"
                : d.Plug == PortablePlug.Outlet && d.Outlet != null ? $" · {PowerGrid.CircuitName(d.Outlet.Circuit)} 회로{(d.Outlet != room ? $"({d.Outlet.Name})" : "")}"
                : d.Capacity > 0f && d.Running ? $" · 내장 {d.ChargeFrac * 100:0}%" : "";
            string extra = d.Kind == PortableKind.Cart ? (room.Type == RoomType.Corridor ? " · 통로를 막는다" : "") : d.Shadowed ? " · 몸에 가림" : d.Forgotten ? " · 잊고 둠" : "";
            if (d.Soak > 0.3f) extra += " · 케이블이 젖었다";
            if (Flagged(d)) extra += " · 주 컴퓨터가 끄라 함";
            if (d.Kind == PortableKind.Heater && d.Running && d.Dust > 0.05f) extra += " · 먼지 타는 냄새";
            parts.Add(d.Kind == PortableKind.Cart ? $"카트{extra}" : $"{d.Name} {state}{power}{extra}");
        }
        foreach (var d in Devices.Where(d => d.HeldBy != null && d.HeldBy.Room == room)) parts.Add($"{Ko.IGa(d.HeldBy!.Name)} {d.Name} 나르는 중");
        if (!room.Detached && room.Circuit >= 0 && room.Circuit < _hot.Length && OutletRoom(room.Circuit) == room && _hot[room.Circuit] > 0.1f) // 멀티탭이 달아오른다 (화면은 세계를 그대로 — 사람들이 아는지는 따로)
            parts.Add($"멀티탭이 달아오른다 ({CircuitLoad[room.Circuit]:0.0}kW{(TripMinutes(room.Circuit) < 90f ? $" · 차단기까지 {TripMinutes(room.Circuit):0}분" : "")})");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>오래 같은 자리에 둔 장비 → 정식 시설로 (개조 제안).</summary>
    public static IEnumerable<UpgradePlan> Candidates(World w)
    {
        var seen = new HashSet<(int, FurnitureType)>();
        foreach (var d in w.Portable.Devices)
        {
            if (!d.Placed || PortableSpecs.Fixture(d.Kind) is not FurnitureType ft) continue;
            float days = (w.Tick - d.PlacedSince) / (float)SimTime.TicksPerDay;
            if (days < 3f || w.Portable.RoomOf(d) is not Room room || room.OffLimits || ModulesV15.Has(room, ft) || room.Furniture.Any(f => f.Type == ft)) continue;
            // 등이 서 있던 자리에 단다 (길을 막으면 방의 다른 빈칸)
            var spot = w.Ship.IsOpenFloor(d.At) && Adaptation.SafeToBlock(w, room, d.At) ? d.At : Modules.Spot(w, room);
            if (!seen.Add((room.Id, ft)) || spot is not Cell at) continue;
            yield return new UpgradePlan(UpgradeKind.Module, WorkTarget.AtCell(at, room), MathF.Min(0.8f, 0.35f + 0.05f * days), Skill.Electrical,
                $"{room.Name}에 {Ko.EulReul(d.Name)} {days:0}일째 두고 쓴다{(d.Forgotten ? " (잊고 둔 채)" : "")} → 정식 {Ko.EulReul(Modules.Name(ft))} 단다 ({string.Join(" + ", Modules.Cost(ft).Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"))})",
                Modules.Cost(ft), 0.3f, (int)ft);
        }
    }

    /// <summary>시험용: 장비를 그 자리에 놓고 켠다 (사람 손을 거친 것처럼).</summary>
    public void PlaceNow(PortableDevice d, Cell at, CrewMember? by, string purpose, Room? outlet = null, PortableDevice? battery = null, CrewMember? user = null, Vector2? aim = null)
    {
        d.HeldBy = null;
        d.At = at;
        d.Stored = false;
        d.PlacedSince = _w.Tick;
        d.DoneSince = -1;
        d.Purpose = purpose;
        d.InstalledBy = by?.Id ?? -1;
        d.User = user;
        d.Aim = aim ?? Vector2.Zero;
        d.On = true;
        d.Plug = PortablePlug.None;
        d.Source = null;
        d.Outlet = null;
        if (battery != null) { d.Plug = PortablePlug.Battery; d.Source = battery; d.CableTo = battery.At.Center; }
        else if (outlet != null) { d.Plug = PortablePlug.Outlet; d.Outlet = outlet; d.CableTo = OutletPoint(outlet, at.Center); }
    }
}

internal sealed record PortableChoice(PortableNeed Need, PortableDevice? Main, PortableDevice? Battery, PortableDevice? Cart, PortableDevice? Busy, float Score, string Reason)
{
    public bool Swap { get; init; }
}

/// <summary>이동식 장비: 창고에서 꺼내 와 설치 · 배터리 갖다 대기 · 다 쓰면 회수 · 뽑기 · 고치기 · 먼저 쓰는 사람을 기다리기.</summary>
public sealed class PortableActivity : Activity
{
    public override string Id => "portable";
    public override string Label => "이동식 장비";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild) return (0f, "—");
        if (c.Job?.Activity == this && w.Portable.DoingScore(c) is var (ds, dwhy)) return (ds, dwhy); // 하던 일 (맡은 요구는 목록에서 빠졌다)
        if (w.Portable.Needs.Count == 0) return (0f, "—");
        if (w.Portable.Choose(c, dist) is not PortableChoice ch) return (0f, "할 일 없음");
        float s = ch.Score;
        if (c.Pose == Pose.Sleeping) s *= ch.Need.Task == PortableTask.Unplug || ch.Need.Kind == PortableKind.Pump ? 0.6f : 0.3f;
        return (MathF.Max(0f, s), ch.Reason);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var ps = w.Portable;
        if (ps.Choose(c, dist) is not PortableChoice ch) return null;
        var n = ch.Need;
        var toils = Plans.DropOff(c, w, dist);
        string label, log;
        switch (n.Task)
        {
            case PortableTask.Setup when ch.Busy is PortableDevice busy:
            {
                // 먼저 쓰는 사람이 있다 — 곁에서 기다린다 (풀리면 다음 판단에 가져간다)
                var near = busy.HeldBy?.Cell ?? busy.At;
                ps.MarkWaited(n.Key);
                toils.Add(new GotoToil(near));
                toils.Add(new WaitToil(SimTime.Minutes(20), Pose.Standing, busy.Position) { DoneWhen = (_, _) => !busy.Placed || busy.DoneSince >= 0 || busy.Broken });
                return new Job(this, $"{busy.Name} 기다리기", toils) { LogText = ch.Reason, LogKind = LogKind.Life, TargetRoom = n.Room };
            }
            case PortableTask.Setup:
            {
                var main = ch.Main!;
                var bat = ch.Battery;
                var cart = ch.Cart;
                ps.Claim(n.Key, c, main, bat, cart);
                if (ch.Swap) ps.Stats.Swaps++;
                if (cart != null)
                {
                    toils.Add(new GotoToil(cart.At));
                    toils.Add(new DoToil((cm, world) => world.Portable.PickUp(cart, cm, null)));
                }
                foreach (var d in new[] { main, bat })
                {
                    if (d == null) continue;
                    var dd = d;
                    toils.Add(new GotoToil(dd.At));
                    toils.Add(new DoToil((cm, world) => world.Portable.PickUp(dd, cm, cart?.HeldBy == cm ? cart : null) || dd == bat && Skip(world, cm, bat)));
                }
                toils.Add(new GotoToil(n.Spot));
                toils.Add(new WorkToil(main.Kind == PortableKind.Pump ? 0.15f : 0.08f, Skill.Electrical, n.Room.Center));
                toils.Add(new DoToil((cm, world) => world.Portable.Install(cm, n, main, bat, cart)));
                string names = bat != null ? $"{Ko.WaGwa("이동식 배터리")} {main.Name}" : main.Name;
                label = $"{main.Name} 설치";
                log = $"{n.Room.Name} {n.Why} — {Ko.EulReul(names)} 가지러 간다{(cart != null ? " (카트를 밀고)" : "")}";
                break;
            }
            case PortableTask.Feed:
            {
                var bat = ch.Main!;
                var d = n.Device!;
                ps.Claim(n.Key, c, bat);
                toils.Add(new GotoToil(bat.At));
                toils.Add(new DoToil((cm, world) => world.Portable.PickUp(bat, cm, null)));
                toils.Add(new GotoToil(n.Spot));
                toils.Add(new WorkToil(0.04f, Skill.Electrical, d.At.Center));
                toils.Add(new DoToil((cm, world) => world.Portable.Feed(cm, n, bat)));
                label = "배터리 갖다 대기";
                log = $"{n.Why} — 이동식 배터리를 가져간다";
                break;
            }
            case PortableTask.Retrieve:
            {
                var d = ch.Main!;
                ps.Claim(n.Key, c, d);
                List<PortableDevice>? got = null;
                toils.Add(new GotoToil(d.At));
                toils.Add(new WorkToil(0.03f, Skill.Mechanics, d.At.Center));
                toils.Add(new DoToil((cm, world) => (got = world.Portable.Collect(cm, d)).Count > 0));
                toils.Add(new GotoToilLate(_ => d.Home));
                toils.Add(new DoToil((cm, world) => { world.Portable.Stow(cm, got!); return true; }));
                label = $"{d.Name} 회수";
                log = $"{n.Why} — 창고에 돌려놓으러 간다";
                break;
            }
            case PortableTask.Unplug:
            {
                var d = ch.Main!;
                ps.Claim(n.Key, c, d);
                toils.Add(new GotoToil(d.At));
                toils.Add(new WorkToil(0.03f, Skill.Electrical, d.At.Center));
                toils.Add(new DoToil((cm, world) => { world.Portable.Unplug(cm, d); return true; }));
                label = $"{d.Name} 뽑기";
                log = $"{n.Why} — {Ko.EulReul(d.Name)} 뽑으러 간다";
                break;
            }
            default:
            {
                var d = ch.Main!;
                ps.Claim(n.Key, c, d);
                toils.Add(new GotoToil(d.At));
                toils.Add(new WorkToil(0.4f, Skill.Electrical, d.At.Center));
                toils.Add(new DoToil((cm, world) => { world.Portable.Fix(cm, d); return true; }));
                label = $"{d.Name} 고치기";
                log = $"고장 난 {Ko.EulReul(d.Name)} 고치러 간다";
                break;
            }
        }
        string key = n.Key;
        ps.Doing(c, ch.Score, ch.Reason);
        return new Job(this, label, toils)
        {
            LogText = log,
            LogKind = LogKind.Work,
            AlwaysLog = true,
            TargetRoom = n.Room,
            InterruptMargin = n.Task == PortableTask.Unplug ? 0.25f : 0.15f,
            Urgent = n.Task == PortableTask.Unplug && n.For == c, // v16.20 주 컴퓨터가 콕 집어 부탁했다 — 뛰어간다
            OnFinished = (cm, world, status) =>
            {
                // 못 끝냈으면 들고 있던 장비는 그 자리에 남는다
                if (status != ToilStatus.Succeeded)
                    foreach (var d in world.Portable.Devices.Where(x => x.HeldBy == cm).ToList()) world.Portable.Drop(d, cm);
                world.Portable.Release(key, cm);
                world.Portable.Done(cm);
            },
        };
    }

    /// <summary>배터리를 다른 사람이 먼저 가져갔으면 본체만 들고 간다.</summary>
    private static bool Skip(World w, CrewMember c, PortableDevice? bat)
    {
        if (bat != null && bat.ClaimedBy == c.Id && bat.HeldBy == null) bat.ClaimedBy = -1;
        return true;
    }
}
