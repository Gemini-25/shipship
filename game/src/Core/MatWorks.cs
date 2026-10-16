using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 재료 관리 — 주 컴퓨터가 맡는다 (회의 없이):
//  가) 거두는 힘을 배 크기에 맞춘다: 채집 장치 수를 설비 수에 맞추고, 모자라면 증설 공사를 올린다.
//  나) 모자란 원료를 겨눠서 캔다: 원료마다 "며칠 치 남았나"를 보고, 가장 급한 원료 쪽으로 채집 장치의 체 · 채굴 드론을 돌린다.
//      돌이 많은 구간에서는 작은 바위 · 혜성 조각을 끌어오고, 반나절 천천히 지나며 더 거둔다 (짧은 들름 — 주 컴퓨터 혼자).
//      그래도 바닥이면 원정을 꺼낸다 (원정은 회의로).
//  다) 배 안에서 되살린다: 고철 녹이기(정제기라면 어디서나) · 갈아 낸 부품 뜯기 · 음식 찌꺼기 → 탄소 · 내쉰 이산화탄소 → 탄소 (탄소 회수기).

public sealed class MaterialStats
{
    public int CollectorsBuilt, ReclaimersBuilt, Sieved, Aimed, Tows, Lingers, Stripped, Salvaged, ExpeditionAsks, AutoRefined;
    public float CarbonFromAir, CarbonFromWaste;
    public string Line() =>
        $"채집 장치 증설 {CollectorsBuilt} · 탄소 회수기 {ReclaimersBuilt} · 체 바꿔 거름 {Sieved} · 겨눠 캠 {Aimed} · 바위 끌어옴 {Tows} · 천천히 지남 {Lingers}"
        + $" · 헌 부품 뜯음 {Stripped}(건짐 {Salvaged}) · 공기에서 탄소 {CarbonFromAir:0.0} · 찌꺼기에서 탄소 {CarbonFromWaste:0.0} · 원정 요청 {ExpeditionAsks} · 컴퓨터가 정제 {AutoRefined}";
}

public sealed class MaterialSystem
{
    /// <summary>재고를 보는 원료 (희귀 소재는 따로 — 드물어서 겨눌 수 없다).</summary>
    public static readonly ItemKind[] Raw = { ItemKind.MetalOre, ItemKind.Carbon, ItemKind.Silicate, ItemKind.Ice };
    /// <summary>시험 · 개발용: 재료 관리를 끈다 (예전 흐름으로 맞춘 시험).</summary>
    public static bool Off;

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 9173 + 41));
    /// <summary>원료마다 하루에 줄어드는 양 (지수 평균 · 늘면 음수).</summary>
    private readonly float[] _drain = new float[4];
    private readonly int[] _last = { -1, -1, -1, -1 };
    /// <summary>만들 것이 모자란데 이 원료가 없어 못 만든 마지막 때.</summary>
    private readonly long[] _lacked = { -1, -1, -1, -1 };
    /// <summary>바닥난 채로 모자랐던 날 (이어서).</summary>
    private readonly int[] _shortDays = new int[4];
    /// <summary>닷새 치도 안 남은 날 (이어서) — 채집 장치를 더 달 만큼.</summary>
    private readonly int[] _tightDays = new int[4];
    private long _nextDay, _nextHour;
    private float _airCarbon;
    private long _lingerUntil = -1;
    private int _lingerLeg = -1;
    private long _nextTow;
    private long _nextAsk;
    public MaterialStats Stats { get; } = new();

    public MaterialSystem(World w)
    {
        _w = w;
        _nextDay = SimTime.TicksPerDay;
    }

    private static int Idx(ItemKind k) => k switch { ItemKind.MetalOre => 0, ItemKind.Carbon => 1, ItemKind.Silicate => 2, ItemKind.Ice => 3, _ => -1 };
    private int Stock(ItemKind k) => _w.Ship.CountStored(k);

    /// <summary>만들어야 할 것이 있는데 이 원료가 없어 못 만들었다 (정제 · 제작 목록이 알린다).</summary>
    public void Lacked(ItemKind k) { int i = Idx(k); if (i >= 0) _lacked[i] = _w.Tick; }
    private bool Lacking(int i) => _lacked[i] >= 0 && _w.Tick - _lacked[i] < SimTime.Hours(12);

    /// <summary>하루에 줄어드는 양 (늘고 있으면 0).</summary>
    public float Drain(ItemKind k) { int i = Idx(k); return i < 0 ? 0f : MathF.Max(0f, _drain[i]); }

    /// <summary>며칠 치 남았나 (줄지 않으면 99).</summary>
    public float DaysLeft(ItemKind k)
    {
        float d = Drain(k);
        return d < 0.15f ? 99f : Stock(k) / d;
    }

    /// <summary>얼마나 급한가 0~1: 며칠 치 남았나 · 바닥 · 그 원료가 없어 못 만든 것이 있나.</summary>
    public float Urgency(ItemKind k)
    {
        int i = Idx(k);
        if (i < 0) return 0f;
        int s = Stock(k);
        float d = DaysLeft(k);
        float u = d < 2f ? 1f : d < 5f ? 0.75f : d < 10f ? 0.45f : d < 20f ? 0.2f : 0f;
        int floor = k == ItemKind.MetalOre ? 6 : 3;
        if (s < floor) u = MathF.Max(u, 0.5f);
        if (Lacking(i)) u = MathF.Max(u, s == 0 ? 1f : 0.8f);
        if (k == ItemKind.Ice && s < 4 && (_w.Air.Reserve < 0.6f * _w.Air.ReserveCapacity || _w.Water.Level < 0.4f * _w.Water.Capacity)) u = MathF.Max(u, 0.7f);
        return u;
    }

    /// <summary>가장 급한 원료 (그만큼 급한 것이 없으면 null).</summary>
    public ItemKind? Urgent(float min = 0.4f)
    {
        ItemKind? best = null;
        float bu = min - 0.001f;
        foreach (var k in Raw)
        {
            float u = Urgency(k);
            if (u > bu) { bu = u; best = k; }
        }
        return best;
    }

    /// <summary>한 줄 (주 컴퓨터가 말하는 투): "탄소 사흘 치 · 금속 12일 치".</summary>
    public string Brief() => string.Join(" · ", Raw.Select(k => $"{ItemKinds.Name(k)} {Stock(k)}" + (DaysLeft(k) < 30f ? $"({DaysLeft(k):0}일 치)" : "")));

    /// <summary>바닥난 채로 이틀 넘게 모자란 원료 (원정을 꺼낼 만큼).</summary>
    public ItemKind? Starved()
    {
        for (int i = 0; i < Raw.Length - 1; i++) // 얼음은 물 · 공기 쪽 원정이 따로 본다
            if (_shortDays[i] >= 2) return Raw[i];
        return null;
    }

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        if (w.Tick >= _nextDay)
        {
            _nextDay += SimTime.TicksPerDay;
            for (int i = 0; i < Raw.Length; i++)
            {
                int now = Stock(Raw[i]);
                if (_last[i] >= 0) _drain[i] = 0.6f * _drain[i] + 0.4f * (_last[i] - now);
                _last[i] = now;
                _shortDays[i] = now <= 1 && Lacking(i) ? _shortDays[i] + 1 : 0;
                _tightDays[i] = Urgency(Raw[i]) >= 0.75f ? _tightDays[i] + 1 : 0; // 닷새 치도 안 남은 날
            }
        }
        AirCarbon();
        AutoRefine();
        Linger();
    }

    /// <summary>쌓아 둘 목표의 배 크기 배율: 설비 60대에 1 (큰 배는 금속판 · 케이블 · 구조재 · 전자재 · 퓨즈를 더 쌓는다 — 30명 배가 12개 아래로 떨어질 때까지 정제하지 않던 것).</summary>
    public static float StockScale(World w, ItemKind product) => product is ItemKind.Plate or ItemKind.Cable or ItemKind.Structure or ItemKind.Electronics or ItemKind.Fuse && !Off
        ? Math.Clamp(w.Ship.Machines.Count() / 60f, 1f, 4f) : 1f;

    private readonly Dictionary<int, float> _autoAcc = new();
    private readonly Dictionary<int, long> _autoSaid = new();

    /// <summary>
    /// 사람 · 로봇이 쓰지 않는 정제기는 주 컴퓨터가 혼자 천천히 돌린다 (사람이 하면 세 배 빠르다):
    /// 원료가 넉넉하고(그 재료의 두 배 넘게) 수리재가 목표 아래일 때만 — 가장 모자란 것부터.
    /// (원료는 수백 개 쌓였는데 사람이 다른 일에 밀려 정제기가 놀고 금속판이 바닥나던 것)
    /// </summary>
    private void AutoRefine()
    {
        var w = _w;
        if (!w.Automation.Present || !w.Automation.MainOnline) return;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.Refinery).OrderBy(f => f.Id))
        {
            if (f.Room.Detached || f.Room.Abandoned || f.Room.OffLimits || f.Machine is not Machine m || m.Efficiency <= 0f || w.Fire.CountIn(f.Room) > 0) continue;
            if (w.Board.OpenUnsorted.Any(o => o.Target.Furniture == f && (o.Assignee != null || o.Robot != null))) { _autoAcc[f.Id] = 0f; continue; } // 누가 쓰고 있다
            float acc = _autoAcc.GetValueOrDefault(f.Id) + 0.35f * m.Efficiency;
            Recipe? pick = null;
            float worst = 0f;
            foreach (var r in Recipes.All)
            {
                if (r.Station != Station.Refinery || r.FromProduce || !w.History.Doctrine.Knows(r)) continue;
                int target = (int)MathF.Ceiling(w.History.Doctrine.Target(r) * StockScale(w, r.Product));
                int have = w.Board.Have(r.Product);
                if (have >= target) continue;
                if (!r.Inputs.All(x => w.Ship.CountStored(x.kind) >= x.count * 2 + (ItemKinds.Tier(x.kind) == ItemTier.Raw ? 2 : Evolution.Reserve(x.kind, w)))) continue;
                // 선체 · 배관을 막는 금속판 · 구조재 · 실링폼이 먼저 (금속이 빠듯할 때 케이블만 49개 쌓고 금속판이 3개로 바닥나던 것)
                float weight = r.Product switch { ItemKind.Plate or ItemKind.Sealant => 1.4f, ItemKind.Structure => 1.2f, ItemKind.Fuse => 0.7f, _ => 1f };
                float lack = (1f - have / (float)Math.Max(1, target)) * weight;
                if (lack > worst) { worst = lack; pick = r; }
            }
            if (pick == null) { _autoAcc[f.Id] = MathF.Min(acc, 1f); continue; }
            if (acc < pick.Hours) { _autoAcc[f.Id] = acc; continue; }
            foreach (var (k, n) in pick.Inputs) Life.Take(w, k, n);
            int put = VoyageV15.Put(w, pick.Product, pick.Yield);
            _autoAcc[f.Id] = acc - pick.Hours;
            Stats.AutoRefined += put;
            w.Adapt.Refined += put;
            m.Wear = MathF.Min(1f, m.Wear + 0.003f);
            if (!_autoSaid.TryGetValue(f.Id, out var said) || w.Tick - said >= SimTime.Hours(8))
            {
                _autoSaid[f.Id] = w.Tick;
                MarkLog.Add(m.Marks, w.Tick, $"주 컴퓨터가 혼자 돌렸다 — {ItemKinds.Name(pick.Product)}");
                w.Log.Add(w.Tick, LogKind.Work, $"주 컴퓨터: 노는 {f.Room.Name} 정제기를 혼자 돌린다 — {ItemKinds.Name(pick.Product)} {w.Board.Have(pick.Product)}개 (원료가 넉넉하다)");
            }
        }
    }

    // ═══════════════════════ 다) 배 안에서 되살린다 ═══════════════════════

    /// <summary>탄소 회수기: 생명유지가 걸러 낸 이산화탄소에서 탄소를 뽑는다 (사람 하나가 하루 0.12개쯤 · 한 대가 하루 2개까지 · 전기를 많이 먹는다).</summary>
    private void AirCarbon()
    {
        var w = _w;
        float cap = 0f;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.CarbonReclaimer))
            if (!f.Room.Abandoned && !f.Room.Detached && f.Machine is Machine m && m.Faults.Count == 0 && m.Powered) cap += 2f / 24f * m.Efficiency;
        if (cap <= 0f) return;
        float people = w.Air.CO2Scrubbed / Atmosphere.BreathCO2;
        float got = MathF.Min(cap, MathF.Max(0f, people) * 0.12f / 24f);
        _airCarbon += got;
        Stats.CarbonFromAir += got;
        if (_airCarbon < 1f) return;
        int n = (int)_airCarbon;
        int put = VoyageV15.Put(w, ItemKind.Carbon, n);
        _airCarbon -= put > 0 ? put : 0f;
        if (_airCarbon > 3f) _airCarbon = 3f; // 둘 데가 없으면 흘려보낸다
        if (put > 0)
        {
            var at = w.Ship.FurnitureOf(FurnitureType.CarbonReclaimer).OrderBy(f => f.Id).First();
            MarkLog.Add(at.Machine!.Marks, w.Tick, $"내쉰 숨에서 탄소 {put}");
        }
    }

    /// <summary>갈아 낸 헌 부품을 뜯는다: 몸체는 고철로 (정제기에서 금속 원료로) · 가끔 쓸 만한 속을 건진다.</summary>
    public void Stripped(ItemKind part, CrewMember c, Machine m)
    {
        var w = _w;
        if (Off) return;
        float scrap = part switch
        {
            ItemKind.Pump or ItemKind.Motor or ItemKind.ReactorControl => 1f,
            ItemKind.Bearing or ItemKind.PowerController => 0.5f,
            ItemKind.Sensor => 0.3f,
            _ => ItemKinds.Tier(part) is ItemTier.General or ItemTier.Advanced ? 0.4f : 0f,
        };
        if (scrap <= 0f) return;
        w.Scrap.AddScrap(scrap);
        Stats.Stripped++;
        (ItemKind kind, float chance)? inner = part switch
        {
            ItemKind.Motor => (ItemKind.Cable, 0.45f),       // 감긴 구리선
            ItemKind.Pump => (ItemKind.Plate, 0.25f),        // 멀쩡한 하우징 판
            ItemKind.PowerController => (ItemKind.Electronics, 0.35f),
            ItemKind.Sensor => (ItemKind.Electronics, 0.25f),
            ItemKind.ReactorControl => (ItemKind.Electronics, 0.6f),
            _ => null,
        };
        string got = $"고철 {scrap:0.#}";
        if (inner is (ItemKind k, float p) && R.Chance(p * (0.6f + 0.6f * c.SkillLevel(Skill.Mechanics))) && VoyageV15.Put(w, k, 1) > 0)
        {
            Stats.Salvaged++;
            got += $" · {ItemKinds.Name(k)} 1";
        }
        MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: 헌 {ItemKinds.Name(part)}을 뜯었다 ({got})");
    }

    // ═══════════════════════ 나) 모자란 원료를 겨눈다 ═══════════════════════

    /// <summary>채집 장치의 체: 급한 원료가 있으면 그쪽을 더 거른다 (열에 셋은 그 원료로 · 먼지에 그 원료가 섞여 있는 만큼).</summary>
    public ItemKind Sieve(ItemKind picked)
    {
        if (Off || !_w.Automation.Present || !_w.Automation.MainOnline) return picked;
        // 넘치는 원료(60개 넘게 · 줄지 않는다)는 거르지 않고 흘려보낸다 — 그 자리에 급한 원료 · 없으면 금속을 거른다 (규산만 130개 쌓이고 금속이 바닥나던 것)
        if (Idx(picked) >= 0 && picked != ItemKind.MetalOre && Stock(picked) >= 60 && DaysLeft(picked) >= 60f)
        {
            Stats.Sieved++;
            return Urgent(0.3f) is ItemKind u && u != picked ? u : ItemKind.MetalOre;
        }
        if (Urgent(0.45f) is not ItemKind want || want == picked || want == ItemKind.Ice && picked != ItemKind.MetalOre) return picked;
        if (!R.Chance(0.3f)) return picked;
        Stats.Sieved++;
        return want;
    }

    /// <summary>채굴 드론이 무엇을 노리나 (급한 원료 · 없으면 null).</summary>
    public ItemKind? MineFor()
    {
        if (Off) return null;
        var k = Urgent(0.4f);
        if (k == ItemKind.Silicate && Urgency(ItemKind.Silicate) < 0.75f) return null; // 규산은 먼지 띠에 드물다 — 아주 급할 때만
        return k;
    }

    public void Aimed() => Stats.Aimed++;

    /// <summary>바위 · 혜성 조각을 끌어올 만한 구간인가 (그리고 무엇이 나오나).</summary>
    public (string what, ItemKind a, ItemKind b)? TowSource()
    {
        var k = _w.Voyage.Current.Kind;
        return k switch
        {
            LegKind.AsteroidBelt or LegKind.Graveyard => ("작은 바위", ItemKind.MetalOre, ItemKind.Silicate),
            LegKind.CometTrail or LegKind.IceRing => ("혜성 조각", ItemKind.Ice, ItemKind.Carbon),
            _ when _w.Voyage.MiningMul > 1f => ("떠도는 바위", ItemKind.MetalOre, ItemKind.Carbon),
            _ => null,
        };
    }

    /// <summary>끌어오기를 할까: 돌이 많은 구간 · 급한 원료가 그 돌에 들어 있다 · 하루에 한 번.</summary>
    public bool WantTow()
    {
        if (Off || _w.Tick < _nextTow || TowSource() is not { } src) return false;
        var want = Urgent(0.45f);
        if (want != src.a && want != src.b) return false;
        _nextTow = _w.Tick + SimTime.TicksPerDay;
        return true;
    }

    /// <summary>끌어온 바위를 쪼갰다 (넣은 것 한 줄).</summary>
    public string Towed(Drone d)
    {
        var w = _w;
        if (TowSource() is not { } src) return "";
        Stats.Tows++;
        int a = 4 + R.Range(0, 4), b = 1 + R.Range(0, 3);
        int pa = VoyageV15.Put(w, src.a, a), pb = VoyageV15.Put(w, src.b, b);
        string got = $"{ItemKinds.Name(src.a)} {pa}" + (pb > 0 ? $" · {ItemKinds.Name(src.b)} {pb}" : "");
        w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: {Ko.EulReul(src.what)} 끌어와 거치대 곁에서 쪼갰다 — {got}");
        w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(d.Name)} {Ko.EulReul(src.what)} 끌어와 {got}을 얻었다", null, log: false);
        return got;
    }

    /// <summary>짧은 들름: 돌이 많은 구간에서 급한 원료가 있으면 반나절 천천히 지난다 (구간마다 한 번 · 주 컴퓨터 혼자 정한다).</summary>
    private void Linger()
    {
        var w = _w;
        if (!w.Automation.Present || !w.Automation.MainOnline || w.Voyage.MiningMul <= 1f || _lingerLeg == w.Voyage.Index) return;
        if (w.Voyage.Current.Kind == LegKind.Port || w.Hazards.StormActive) return;
        if (Urgent(0.6f) is not ItemKind k) return;
        _lingerLeg = w.Voyage.Index;
        _lingerUntil = w.Tick + SimTime.Hours(12);
        Stats.Lingers++;
        string text = $"{ItemKinds.Name(k)}가 {DaysLeft(k):0}일 치밖에 없습니다 — {w.Voyage.Current.Name}에서 반나절 천천히 지나며 더 거두겠습니다";
        w.Automation.Speak.Announce(text, null, 1);
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {text}");
    }

    /// <summary>천천히 지나는 중 (항해 속도 배율).</summary>
    public float SpeedMul => _lingerUntil > _w.Tick && _w.Voyage.Index == _lingerLeg ? 0.4f : 1f;

    /// <summary>원정을 꺼낼 만큼 바닥인가 (하루 한 번만 묻는다).</summary>
    public bool AskExpedition()
    {
        if (Off || Starved() is null || _w.Tick < _nextAsk) return false;
        _nextAsk = _w.Tick + SimTime.TicksPerDay;
        Stats.ExpeditionAsks++;
        return true;
    }

    // ═══════════════════════ 가) 거두는 힘을 배 크기에 맞춘다 ═══════════════════════

    /// <summary>이 배 크기에 맞는 채집 장치 수 (설비 60대에 한 대쯤 · 원료가 사흘 넘게 바닥이면 둘 더까지).</summary>
    public int CollectorsWanted()
    {
        int machines = _w.Ship.Machines.Count();
        int want = Math.Clamp((machines + 30) / 60, 1, 6);
        int starving = 0;
        for (int i = 0; i < 2; i++) starving = Math.Max(starving, Math.Max(_shortDays[i], _tightDays[i])); // 금속 · 탄소 (규산은 드물게 쓴다)
        if (starving >= 3) want += starving >= 6 ? 2 : 1;
        return want;
    }

    /// <summary>탄소 회수기를 달까 (탄소가 급하고 · 전기가 남고 · 아직 없거나 사람이 많으면).</summary>
    public bool ReclaimerWanted()
    {
        var w = _w;
        int have = w.Ship.FurnitureOf(FurnitureType.CarbonReclaimer).Count();
        int max = w.Aboard >= 20 ? 2 : 1;
        return have < max && Urgency(ItemKind.Carbon) >= 0.45f && Evolution.PowerRoom(w, 2.5f);
    }

    /// <summary>채집 장치 자리: 정비실 · 창고 · 재활용실 · 파쇄실 · 화물칸의 빈 두 칸 — 선체 벽에 붙은 곳 (채집 팔이 바깥으로 나간다).</summary>
    public static (Cell[] cells, Room room)? CollectorSpot(World w)
    {
        foreach (var type in new[] { RoomType.Workshop, RoomType.Recycling, RoomType.Crusher, RoomType.Cargo, RoomType.Storage })
        foreach (var room in w.Ship.RoomsOf(type).OrderBy(r => r.Id))
        {
            if (room.Abandoned || room.Leaking || room.OffLimits || room.Detached) continue;
            var free = new HashSet<Cell>(Adaptation.FreeCells(w, room));
            foreach (var a in free.OrderBy(c => c.Y).ThenBy(c => c.X))
            foreach (var d in new[] { new Cell(1, 0), new Cell(0, 1) })
            {
                var b = a + d;
                if (!free.Contains(b)) continue;
                var pair = new[] { a, b };
                if (!pair.Any(c => Cell.Dirs4.Any(n => w.Ship.WallAt(c + n) is WallState ws && ws.IsHull))) continue;
                if (!Adaptation.SafeToBlock(w, room, pair)) continue;
                if (!pair.SelectMany(c => Cell.Dirs4.Select(n => c + n)).Any(n => !pair.Contains(n) && w.Ship.IsOpenFloor(n) && w.Ship.RoomAt(n) == room)) continue;
                return (pair, room);
            }
        }
        return null;
    }

    /// <summary>재료 설비 값.</summary>
    public static (ItemKind kind, int count)[] Cost(FurnitureType t) => t == FurnitureType.Collector
        ? new[] { (ItemKind.Plate, 3), (ItemKind.Motor, 1), (ItemKind.Cable, 2) }
        : Modules.Cost(t);

    /// <summary>재료 설비 공사를 마쳤다: 채집 장치(선체 벽가 두 칸) · 탄소 회수기(생명유지실).</summary>
    public static bool Build(World w, FurnitureType t, CrewMember cm, WorkTarget target, out Room? room, out string text)
    {
        room = null;
        text = "";
        if (t == FurnitureType.Collector)
        {
            if (CollectorSpot(w) is not { } cs) return false;
            var f = w.Ship.AddFurniture(FurnitureType.Collector, cs.cells);
            f.Machine!.Condition = 1f;
            f.Machine.Wear = 0f;
            MarkLog.Add(f.Machine.Marks, w.Tick, $"{cm.Name}: 거두는 힘을 늘리려 달았다");
            MarkLog.Add(cs.room.Marks, w.Tick, "채집 장치 증설");
            w.Paths.Invalidate();
            w.Structure.Touch();
            w.Mats.Stats.CollectorsBuilt++;
            room = cs.room;
            int n = w.Ship.FurnitureOf(FurnitureType.Collector).Count();
            text = $"{Ko.IGa(cm.Name)} {room.Name} 선체 벽에 채집 장치를 하나 더 달았다 — 배 크기에 견줘 거두는 힘이 모자랐다 (이제 {n}대 · {w.Mats.Brief()})";
            return true;
        }
        room = target.Room ?? w.Ship.RoomsOf(RoomType.LifeSupport).FirstOrDefault(r => !r.Abandoned && !r.Leaking);
        if (room == null || !Modules.Install(w, room, t, cm, target.Cell)) return false;
        w.Mats.Stats.ReclaimersBuilt++;
        text = $"{Ko.IGa(cm.Name)} {room.Name}에 탄소 회수기를 달았다 — 사람이 내쉰 이산화탄소에서 탄소를 뽑는다 (탄소 {w.Ship.CountStored(ItemKind.Carbon)}개 · 전기를 많이 먹는다)";
        return true;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var x in _drain) F(x);
        foreach (var x in _shortDays) I(x);
        F(_airCarbon); I(_lingerUntil); I(Stats.Tows); I(Stats.Sieved); I(Stats.CollectorsBuilt);
    }
}
