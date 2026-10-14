using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v19 열: 방마다 열 장부 — 드는 열(사람 · 돌아가는 설비 · 주 컴퓨터 · 원자로에서 새는 열) · 선체로 빠지는 열(단열하면 덜) · 공조가 빼거나 넣는 열.
/// 덥히는 건 원자로 냉각수의 남는 열과 벽 패널로 (전기가 든 방이면) · 식히는 건 공조 덕트로 (환기되는 방만) — 식히는 데는 전기가 든다(방 계통 전력에 얹힌다).
/// 그래서 연산이 늘면 컴퓨터실이 달아오르고 → 공조 전기가 늘고 → 전력이 빠듯하면 다른 설비가 꺼진다 · 추위를 막으려 단열한 방은 기계 열이 갇힌다.
/// 평소(전기 · 환기 · 공조 여유)에는 예전처럼 21℃ 근처, 전기가 끊긴 방은 예전처럼 여덟 시간쯤에 걸쳐 식는다.
/// </summary>
public sealed partial class Atmosphere
{
    /// <summary>사람 한 명의 열 (kW).</summary>
    public const float PersonKw = 0.1f;
    /// <summary>칸 하나의 열용량 (kWh/℃ — 공기 · 벽 · 가구).</summary>
    public const float HeatPerCell = 0.03f;
    /// <summary>외벽 한 칸으로 선체 밖에 빠지는 열 (kW/℃) — 바깥쪽 방이 전기가 끊기면 예전처럼 여덟 시간쯤에 걸쳐 식는다.</summary>
    public const float HullWallKw = 0.0135f;
    /// <summary>방과 방 사이 벽 한 칸으로 건너가는 열 (kW/℃) — 얇은 칸막이는 두 배.</summary>
    public const float InnerWallKw = 0.006f;
    /// <summary>예전 값 (원자로실 새는 열 · 기관실 크기 어림에만): 칸마다 0.0036.</summary>
    private const float HullLossPerCell = 0.0036f;
    /// <summary>선체 · 골조의 온도 (전기가 끊긴 방이 결국 닿는 온도).</summary>
    public const float ShellC = 6f;
    /// <summary>공조가 칸마다 빼거나 넣을 수 있는 열 (kW).</summary>
    public const float HvacPerCell = 0.12f;
    /// <summary>냉방 효율: 밖(방열판)으로 버리는 열 · 컴퓨터실 전용 냉각기는 3kW에 전기 1kW · 배 안에서 옮기기만 하는 열(더운 방 → 찬 방)은 8kW에 1kW (송풍기 · 펌프).</summary>
    public const float CoolCop = 3f, MoveCop = 8f;
    // ── 벽: 방마다 외벽 열전도 · 방 사이 벽 열전도 (한 시간마다 · 떨어져 나간 방이 생기면 다시) ──
    private float[] _hullG = Array.Empty<float>();
    [NotSaved] private float[] _wallFlow = Array.Empty<float>();
    private readonly System.Collections.Generic.List<(int a, int b, float g)> _links = new();
    private long _wallsAt = -1;
    private int _wallsKey = -1;

    /// <summary>임시 단열재로 감싼 방: 벽으로 드나드는 열이 1/3.</summary>
    private static float Wrap(World w, Room r) => r.Insulated || w.Cosmic.Insulated(r) ? 0.35f : 1f;

    /// <summary>이 방의 외벽 열전도 (kW/℃ · 0이면 외벽이 없는 안쪽 방).</summary>
    public float HullConductance(Room r) { Walls(_world); return r.Id < _hullG.Length ? _hullG[r.Id] : 0f; }

    private void Walls(World w)
    {
        var ship = w.Ship;
        int detached = 0;
        foreach (var r in ship.Rooms) if (r.Detached) detached++;
        int key = ship.Rooms.Count * 1000 + detached + w.Body.WallList.Count * 7;
        if (_hullG != null && key == _wallsKey && w.Tick - _wallsAt < SimTime.Hours(1) && _hullG.Length == ship.Rooms.Count) return; // 불러온 직후엔 배열이 비어 있다 (다시 센다)
        _wallsKey = key;
        _wallsAt = w.Tick;
        _hullG = new float[ship.Rooms.Count];
        var pair = new System.Collections.Generic.Dictionary<long, float>();
        Span<int> near = stackalloc int[4];
        foreach (var wb in w.Body.WallList)
        {
            int n = 0;
            foreach (var d in Cell.Dirs4)
            {
                var r = ship.RoomAt(wb.Cell + d);
                if (r == null || r.Detached) continue;
                bool dup = false;
                for (int i = 0; i < n; i++) if (near[i] == r.Id) dup = true;
                if (!dup && n < 4) near[n++] = r.Id;
            }
            if (n == 0) continue;
            float ins = 1f + 2f * (1f - Math.Clamp(wb.Insulation, 0f, 1f)); // 단열재가 상하면 세 배까지
            if (wb.Hull || n == 1)
            {
                for (int i = 0; i < n; i++) _hullG[near[i]] += HullWallKw * ins / n;
                continue;
            }
            float g = InnerWallKw * (wb.Thin ? 2f : 1f);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    long k = (long)Math.Min(near[i], near[j]) * 100000 + Math.Max(near[i], near[j]);
                    pair[k] = pair.GetValueOrDefault(k) + g;
                }
        }
        _links.Clear();
        foreach (var kv in pair.OrderBy(kv => kv.Key)) _links.Add(((int)(kv.Key / 100000), (int)(kv.Key % 100000), kv.Value));
    }

    /// <summary>방마다 공조가 설계된 몫 (설비를 다 돌리고 자리마다 사람이 찬 열 − 선체로 빠지는 열) — 방 계통 전력(0.25kW)에 들어 있다. 넘는 만큼만 전기가 더 든다.</summary>
    private readonly System.Collections.Generic.Dictionary<int, (int n, float kw)> _design = new();

    private float Design(Room r, Room? comp)
    {
        int n = r.Furniture.Count;
        if (_design.TryGetValue(r.Id, out var d) && d.n == n) return d.kw;
        float kw = 0f;
        int places = 0;
        foreach (var f in r.Furniture)
        {
            if (f.Type is FurnitureType.Bed or FurnitureType.Seat or FurnitureType.Cot) places++;
            var m = f.Machine;
            if (m == null || m.Spec.PowerDraw <= 0f) continue;
            kw += m.Spec.PowerDraw * Grades.Power(m.Grade) * Tech.Of(m).Power * HeatShare(f.Type);
        }
        int vol = Math.Max(4, r.Volume);
        kw += places * PersonKw;
        if (r.Type == RoomType.Reactor) kw += HullLossPerCell * vol * 21f;
        if (r == comp) kw += 1.6f;
        kw = MathF.Max(0f, kw - (r.Id < _hullG.Length ? _hullG[r.Id] : 0f) * (Setpoint(r) - ShellC));
        _design[r.Id] = (n, kw);
        return kw;
    }
    /// <summary>배 전체: 밖으로 버려야 하는 열 (식힌 열이 덥힌 열보다 많을 때 · kW).</summary>
    public float RejectKw { get; private set; }
    /// <summary>공조가 맞추는 빠르기 (시간당).</summary>
    public const float HvacRate = 1.2f;

    /// <summary>배 전체: 공조가 뺀 열 · 넣은 열 · 냉방 전기 (kW).</summary>
    public float CoolingKw { get; private set; }
    public float HeatingKw { get; private set; }
    public float HvacElectricKw { get; private set; }

    /// <summary>공조가 맞추는 온도: 원자로실 · 기관실은 덥게 둔다 (식히는 전기를 아낀다).</summary>
    public static float Setpoint(Room r) => r.Type == RoomType.Reactor ? 27f : r.Type == RoomType.Engine ? 24f : 21f;

    /// <summary>돌아가는 설비가 쓰는 전기 가운데 방에 열로 남는 몫.</summary>
    private static float HeatShare(FurnitureType t) => t switch
    {
        FurnitureType.ReactorCore or FurnitureType.EngineCore or FurnitureType.MainComputer => 0f, // 냉각수 고리 · 분사로 (원자로실 새는 열은 따로) · 주 컴퓨터는 연산만큼 따로
        FurnitureType.Fridge or FurnitureType.BloodFridge or FurnitureType.OrganCooler or FurnitureType.IceMaker => 1.4f, // 안의 열까지 뒤로 뿜는다
        FurnitureType.GrowBed or FurnitureType.PlantWall or FurnitureType.PlantRack => 0.5f, // 빛의 일부는 잎에
        FurnitureType.Battery or FurnitureType.CapacitorBank => 0.1f,
        FurnitureType.CoolantPump => 0.2f, // 펌프 열은 대부분 냉각수에 실려 방열판으로
        FurnitureType.OxygenGenerator => 0.6f, // 전해 — 일부는 산소 · 수소에
        _ => 0.85f,
    };

    private void UpdateHeat(float dtHours)
    {
        var w = _world;
        var ship = w.Ship;
        foreach (var r in ship.Rooms) r.HeatIn = 0f;
        foreach (var c in w.Crew)
            if (!c.Dead && !c.Outside && c.Room is Room cr) cr.HeatIn += PersonKw;
        foreach (var m in ship.Machines)
        {
            if (!m.Powered || m.Spec.PowerDraw <= 0f || m.Body.Room.Detached || m.Body.Stowed) continue;
            m.Body.Room.HeatIn += m.Demand * HeatShare(m.Body.Type);
        }
        // 주 컴퓨터: 연산이 많으면 더 (예비 전원으로 돌 때도 · 안전 모드 · 절전이면 덜)
        if (w.Automation.ComputerBody is Furniture cb && !cb.Room.Detached) cb.Room.HeatIn += w.Automation.HeatKw();
        // 원자로가 냉각 능력에 견줘 세게 돌면 원자로실로 열이 샌다 · 기관은 분사 때 기관실이 달아오른다
        float reactorRatio = w.Power.CoolingCapacity > 0.1f ? w.Power.ReactorOutput / w.Power.CoolingCapacity : 1f;
        bool cooking = false;
        foreach (var s in ship.FurnitureOf(FurnitureType.Stove)) if (s.Machine!.Active && s.Machine.Powered) { cooking = true; break; }

        float cool = 0f, heat = 0f, elec = 0f;
        var comp = w.Automation.ComputerBody is Furniture cf && !cf.Room.Detached ? cf.Room : null;
        Walls(w);
        // 방 사이 벽으로 건너가는 열 (지금 온도로)
        _wallFlow = _wallFlow != null && _wallFlow.Length == ship.Rooms.Count ? _wallFlow : new float[ship.Rooms.Count];
        Array.Clear(_wallFlow);
        foreach (var (ia, ib, g) in _links)
        {
            Room ra = ship.Rooms[ia], rb = ship.Rooms[ib];
            float q = g * Wrap(w, ra) * Wrap(w, rb) * (ra.Air.Temperature - rb.Air.Temperature); // 단열한 방은 벽마다 감쌌다
            _wallFlow[ia] -= q;
            _wallFlow[ib] += q;
        }
        float shellC = w.Planet?.ShellC ?? ShellC, hullMul = w.Planet?.HullHeatMul ?? 1f; // (배를 만드는 동안엔 행성이 아직 없다)
        foreach (var r in ship.Rooms)
        {
            if (r.Detached) { r.HvacHeat = 0f; r.HvacKw = 0f; continue; }
            int vol = Math.Max(4, r.Volume);
            float cap = HeatPerCell * vol;
            if (r.Type == RoomType.Reactor) r.HeatIn += HullLossPerCell * vol * (21f + 20f * MathF.Max(0f, reactorRatio - 0.7f)) * (w.Power.ReactorOnline ? 1f : 0.2f);
            if (r.Type == RoomType.Engine && w.Propulsion.Burning) r.HeatIn += HullLossPerCell * vol * 6f;
            if (r.Type == RoomType.Galley && cooking) r.HeatIn += 0.5f;
            float t = r.Air.Temperature;
            float loss = _hullG[r.Id] * Wrap(w, r) * hullMul * (t - shellC); // v23 행성: 바깥 기온 · 바람
            float net = r.HeatIn - loss + _wallFlow[r.Id];
            // 공조: 덥히기는 전기가 든 방(벽 패널 · 냉각수 남는 열) · 식히기는 환기되는 방(덕트)
            float want = net + HvacRate * cap * (t - Setpoint(r));
            float hv = 0f;
            float lim = MathF.Max(HvacPerCell * vol * (r == comp ? 3f : 1f), Design(r, comp) * 1.25f); // 컴퓨터실은 전용 냉각기 · 기계 방은 설비 열에 맞춰 (25% 여유)
            if (want > 0f && Vented(r) && !r.Leaking) hv = MathF.Min(want, r.HvacShed ? MathF.Min(lim, Design(r, comp)) : lim); // v23 끊긴 냉방 전기 — 설계 몫까지만
            else if (want < 0f && r.Powered) hv = MathF.Max(want, -lim);
            r.HvacHeat = hv;
            if (hv > 0f) { if (r != comp) cool += hv; } else heat -= hv;
            r.Air.Temperature = t + (net - hv) / cap * dtHours;
        }
        // 냉방 전기: 더운 방에서 뺀 열은 찬 방을 덥히는 데 돌려쓴다 (옮기는 값만) — 남는 열만 방열판으로 버린다 (냉각기 값) · 컴퓨터실은 전용 냉각기
        float reject = MathF.Max(0f, cool - heat);
        foreach (var r in ship.Rooms)
        {
            float hv = r.HvacHeat;
            float over = hv <= 0f || r.Detached ? 0f : MathF.Max(0f, hv - Design(r, comp)); // 설계 몫을 넘는 열
            r.HvacKw = over <= 0f ? 0f : r == comp ? over / CoolCop : over / MoveCop + (cool > 0f ? reject * hv / cool / CoolCop : 0f);
            elec += r.HvacKw;
        }
        CoolingKw = cool + (comp?.HvacHeat > 0f ? comp.HvacHeat : 0f);
        HeatingKw = heat;
        RejectKw = reject;
        HvacElectricKw = elec;
    }
}
