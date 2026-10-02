using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.13 우주 규모 대재난 (규모: 우주급).
// 긴 예보(센서 · 주 컴퓨터 신뢰도 · 오차) → 대비(사람이 실제로 움직인다: 물벽 · 물자 · 덮개 · 장비 끄기 · 고정 · 봉쇄 · 손 조종 · 대피)
// → 본 사건(기존 효과: 방 방사선 · 외벽 · 설비 고장 · 로봇 · 드론 · 문 · 조명 · 화재 · 온도 · 운석 · 항로 · 센서)
// → 며칠 ~ 몇 주 뒤(삭은 회로 · 방사선 병 · 하늘에 남은 성운 · 그날의 기억 · 관행 · 연대기).
// 세계 ≠ 사람이 아는 것: 예보는 컴퓨터가 믿는 도착 시각(오차)이고, 방송을 들은 사람만 안다 — 못 들은 사람은 동료가 알려 준다.

public enum CosmicPhase { Forecast, Brace, Impact, After, Done }

public enum BraceKind { WaterWall, Supplies, Shutters, PowerDown, Stow, Seal, Fold, Insulate, Pilot, Restart }

/// <summary>대비 일 하나 (사람이 가서 한다).</summary>
public sealed class BraceTask
{
    public int Id { get; init; }
    public BraceKind Kind { get; init; }
    public int RoomId { get; init; } = -1;
    public int FurnitureId { get; init; } = -1;
    public float Hours { get; init; } = 0.2f;
    public string Label { get; init; } = "";
    /// <summary>마지막 순간에만 (주 컴퓨터 끄기 — 그 전엔 예보를 고쳐야 한다).</summary>
    public bool Last { get; init; }
    public int By { get; set; } = -1;
    public long ClaimedAt { get; set; } = -1;
    public bool Done { get; set; }
    public string DoneBy { get; set; } = "";
    public long DoneAt { get; set; } = -1;
}

/// <summary>우주 대재난 하나.</summary>
public sealed class CosmicEvent
{
    public int Id { get; init; }
    public CosmicKind Kind { get; init; }
    public CosmicSpec Spec => CosmicCatalog.Spec(Kind);
    public string Source { get; init; } = "";
    /// <summary>헛예보: 틀어진 센서가 본 것 — 오지 않는다.</summary>
    public bool Ghost { get; init; }
    public long Seen { get; init; }
    public long Arrive { get; set; }
    public float Power { get; set; } = 1f;
    public bool Close { get; set; }
    /// <summary>오는 쪽 (배 가운데에서 본 각도, 라디안).</summary>
    public float Side { get; set; }
    public int TargetRoom { get; set; } = -1;
    public CosmicPhase Phase { get; set; }
    public long PhaseSince { get; set; }

    // ── 예보 (컴퓨터가 믿는 것) ──
    public bool Known { get; set; }
    public long KnownAt { get; set; } = -1;
    public string KnownBy { get; set; } = "";
    public long Predicted { get; set; }
    public float Confidence { get; set; }
    public float ErrorHours { get; set; }
    internal float Offset, Conf0, Err0;
    public long AnnouncedPredicted { get; set; } = -1;
    public int ForecastAct { get; set; } = -1;

    // ── 대비 ──
    public bool EarlyBrace { get; set; }
    public bool PlanVoted { get; set; }
    public int AvoidPlan { get; set; }       // 0 미정 · 1 피한다 · −1 안 피한다
    public string AvoidWhy { get; set; } = "";
    public long BurnAt { get; set; } = -1;
    public long BurnEnd { get; set; } = -1;
    public bool Avoided { get; set; }
    public bool AvoidFailed { get; set; }
    public float FuelSpent { get; set; }
    public bool ShutdownComputer { get; set; }
    public bool SealPlan { get; set; }
    /// <summary>파편이 지나갈 줄의 방 (봉쇄 구획 뒤 — 부딪히기 전에 비운다).</summary>
    public List<int> Evac { get; } = new();
    public bool Sealed { get; set; }
    public List<BraceTask> Tasks { get; } = new();

    // ── 본 사건 ──
    public int StagesStarted { get; set; }
    public int Cause { get; set; } = -1;
    public long End => Arrive + SimTime.Hours(Spec.Span);
    public long AfterUntil { get; set; } = -1;
    internal readonly List<(int id, float dose, bool sheltered)> Snap = new();
    public bool Snapped => Snap.Count > 0;
    /// <summary>방사선이 쏟아지는 동안 한 시간마다: 차폐된 곳에 있었나 (사람 Id → 숨은 시간 · 모든 시간).</summary>
    internal readonly Dictionary<int, (int sh, int tot)> ShelterLog = new();

    // ── 결과 ──
    public int Hits, EmpKills, Glitches, Falls, Fires, Broken, Sick;
    public float GainSheltered, GainExposed;
    public int NSheltered, NExposed;
    public string Grade { get; set; } = "";
    public bool VigilAsked { get; set; }
    public List<string> Notes { get; } = new();

    public float HoursTo(long tick, long t) => (t - tick) / (float)SimTime.TicksPerHour;
    public string PhaseName => Phase switch { CosmicPhase.Forecast => "예보", CosmicPhase.Brace => "대비", CosmicPhase.Impact => "본 사건", CosmicPhase.After => "후유증", _ => "지나감" };
}

/// <summary>지나간 뒤 하늘에 남은 것 (창밖 그림 · 기분).</summary>
public sealed record SkyMark(int EventId, CosmicKind Kind, CosmicRemnant Remnant, long Tick, float Angle, float Dist, float Size, string Name);

public enum CosmicCustomKind { Vigil, Drill, Stow }

/// <summary>대재난이 남긴 관행.</summary>
public sealed class CosmicCustom
{
    public CosmicCustomKind Kind { get; init; }
    public long Born { get; init; }
    public string Origin { get; init; } = "";
    public string? Founder { get; init; }
    public int EventId { get; init; } = -1;
    public HashSet<int> Followers { get; } = new();
    public long NextDay { get; set; }
    public HashSet<int> Kept { get; } = new();
    public int Times { get; set; }
    public static string Name(CosmicCustomKind k) => k switch
    {
        CosmicCustomKind.Vigil => "그날의 밤 (창가에 모여 하늘을 본다)", CosmicCustomKind.Drill => "예보 훈련 (예보가 나면 먼저 움직인다)", _ => "흔들림 대비 정리 (물건을 묶어 둔다)",
    };
}

public sealed class CosmicStats
{
    public int Spawned, Forecasts, Ghosts, BraceDone, Sheltered, Warned, Woken, Avoided, AvoidFailed, Sealed, Strikes, EmpKills, Latent, Vigils, Looks, Votes, Proposals;
}

public sealed class CosmicSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7643 + 389));

    /// <summary>항해당 저절로 생기는 횟수 배율 (0이면 끔). 무작위 사고가 꺼져 있으면 저절로는 없다.</summary>
    public static float Rate = 1f;

    public List<CosmicEvent> Events { get; } = new();
    public List<SkyMark> Sky { get; } = new();
    public List<CosmicCustom> Customs { get; } = new();
    public CosmicStats Stats { get; } = new();
    /// <summary>예보가 맞고 틀린 경험 (다음 예보가 나아진다).</summary>
    public int Learned { get; private set; }

    private int _voyage = -1;
    private readonly List<long> _plan = new();
    /// <summary>이번 항해에 올 때 (시험 · 화면).</summary>
    public List<long> Planned => _plan;
    public int PlannedVoyage => _voyage;
    private int _next = 1, _taskNext = 1;
    private float[] _rad = Array.Empty<float>();
    private readonly List<int> _safed = new();          // 꺼 둔 설비 (Furniture.Id, 넣은 순서)
    private readonly Dictionary<int, float> _water = new(); // 물벽 (방 Id → 0~1)
    private readonly HashSet<int> _stowed = new(), _supplied = new(), _shut = new(), _insulated = new();
    private bool _folded;
    private int _pilot = -1;
    private float _waterUsed;
    private readonly List<(long tick, int furniture, string why)> _latent = new();
    private readonly List<(long tick, int ev, float size, bool hostile)> _launch = new();
    private readonly Dictionary<int, int> _warnClaim = new(); // 알려 줄 사람 → 알리러 가는 사람

    public float OutsideRad { get; private set; }
    public float SensorMul { get; private set; } = 1f;
    public bool NoEva { get; private set; }
    /// <summary>화면: 지금 흔들림 · 섬광 세기 (읽기만).</summary>
    public float ShakeNow { get; private set; }
    public float FlashNow { get; private set; }

    public CosmicSystem(World w) => _w = w;

    // ───────────────────────────── 읽기 (화면 · 다른 시스템) ─────────────────────────────

    public IEnumerable<CosmicEvent> Active => Events.Where(e => e.Phase != CosmicPhase.Done);
    /// <summary>가장 급한 것 (본 사건 → 대비 → 예보 → 후유증).</summary>
    public CosmicEvent? Main => Events.Where(e => e.Phase != CosmicPhase.Done && (e.Known || e.Phase >= CosmicPhase.Impact))
        .OrderBy(e => e.Phase switch { CosmicPhase.Impact => 0, CosmicPhase.Brace => 1, CosmicPhase.Forecast => 2, _ => 3 }).ThenBy(e => e.Predicted).FirstOrDefault();
    public float Radiation(Room r) => r.Id < _rad.Length ? _rad[r.Id] : 0f;
    public float Water(Room r) => _water.TryGetValue(r.Id, out var v) ? v : 0f;
    public bool Stowed(Room r) => _stowed.Contains(r.Id) || Customs.Any(c => c.Kind == CosmicCustomKind.Stow && c.Followers.Count >= 2) && r.Furniture.Any(f => f.Type is FurnitureType.Bed or FurnitureType.Cot);
    public bool Supplied(Room r) => _supplied.Contains(r.Id);
    public bool Insulated(Room r) => _insulated.Contains(r.Id);
    public bool Folded => _folded;
    public bool Safed(Furniture f) => _safed.Contains(f.Id);
    public IReadOnlyList<int> SafedIds => _safed;
    /// <summary>ShipBody: 이 방 관측창 덮개를 내려 둔다.</summary>
    public bool Shut(int roomId) => _shut.Contains(roomId);
    // 누가 무엇을 아나 (Mind.Knows는 선내 사고만 들고 있다가 지운다 — 대재난은 여기서 따로)
    private readonly Dictionary<long, (KnowSource src, long tick, string how)> _know = new();
    private readonly HashSet<string> _looked = new();
    private readonly HashSet<int> _went = new(); // 대재난을 겪은 사람
    private static long KK(int crew, int ev) => (long)crew * 100000 + ev;
    public bool Knows(CrewMember c, CosmicEvent e) => _know.ContainsKey(KK(c.Id, e.Id));
    public (KnowSource src, long tick, string how)? Knowing(CrewMember c, CosmicEvent e) => _know.TryGetValue(KK(c.Id, e.Id), out var k) ? k : null;
    public bool Looked(CrewMember c, string key) => _looked.Contains($"{c.Id}:{key}");
    internal void MarkLooked(CrewMember c, string key) => _looked.Add($"{c.Id}:{key}");
    public bool WentThrough(CrewMember c) => _went.Contains(c.Id);
    public CosmicCustom? CustomOf(CosmicCustomKind k) => Customs.FirstOrDefault(c => c.Kind == k);
    public bool Follows(CrewMember c, CosmicCustomKind k) => CustomOf(k) is CosmicCustom cu && cu.Followers.Contains(c.Id);

    /// <summary>얼마나 쬐는 방인가 0~1 (바깥 노출 · 물벽 · 차폐).</summary>
    public float RelExposure(Room r)
    {
        float e = _w.Ambience.Exposure(r) * (1f - 0.6f * Water(r));
        if ((RoomCatalog.Tags(r.Kind) & RoomTag.Shielded) != 0) e *= 0.15f;
        e *= TechWeb.Mul(_w, "cosmic.expose"); // v16.14 폭풍 대피 차폐
        return e;
    }

    /// <summary>숨을 곳 (차폐 → 물벽 → 배 안쪽).</summary>
    public List<Room> Refuges()
    {
        var live = _w.Ship.Rooms.Where(r => !r.Detached && !r.OffLimits && !r.Leaking && !r.Abandoned && r.Type != RoomType.Corridor).ToList();
        var list = live.Where(r => (RoomCatalog.Tags(r.Kind) & RoomTag.Shielded) != 0).OrderBy(r => r.Id).ToList();
        list.AddRange(live.Where(r => !list.Contains(r) && Water(r) >= 0.4f).OrderBy(r => r.Id));
        list.AddRange(live.Where(r => !list.Contains(r)).OrderBy(r => _w.Ambience.Exposure(r)).ThenBy(r => r.Id).Take(2));
        return list;
    }

    private Vector2 ShipCenter()
    {
        var rooms = _w.Ship.Rooms.Where(r => !r.Detached).ToList();
        if (rooms.Count == 0) return Vector2.Zero;
        var s = Vector2.Zero;
        foreach (var r in rooms) s += r.Center;
        return s / rooms.Count;
    }

    private static Vector2 Dir(CosmicEvent e) => new(MathF.Cos(e.Side), MathF.Sin(e.Side));

    /// <summary>이 방이 오는 쪽을 얼마나 마주 보나 0~1.</summary>
    public float Facing(Room r, CosmicEvent e, Vector2 center)
    {
        var d = r.Center - center;
        if (d.LengthSquared() < 0.5f) return 0f;
        return Math.Clamp(Vector2.Dot(Vector2.Normalize(d), Dir(e)), 0f, 1f);
    }

    /// <summary>실제 세기 (비켰으면 약하다 · 가까울 때만의 단계).</summary>
    private float Eff(CosmicEvent e, CosmicStage s)
    {
        if (s.CloseOnly && !e.Close) return 0f;
        if (!e.Avoided) return e.Power * s.Power;
        bool contact = (s.Fx & (CosmicFx.Strike | CosmicFx.Debris | CosmicFx.Hostile | CosmicFx.Tidal | CosmicFx.Shock)) != 0;
        return contact ? 0f : e.Power * s.Power * 0.25f;
    }

    private long StageAt(CosmicEvent e, int i) => e.Arrive + SimTime.Hours(e.Spec.Stages[i].At);
    private long StageEnd(CosmicEvent e, int i) => e.Arrive + SimTime.Hours(e.Spec.Stages[i].At + e.Spec.Stages[i].Hours);
    public bool StageActive(CosmicEvent e, int i) => !e.Ghost && _w.Tick >= StageAt(e, i) && _w.Tick < StageEnd(e, i);

    /// <summary>지금 이 효과가 얼마나 세게 걸려 있나 (화면 · 시험).</summary>
    public float FxNow(CosmicEvent e, CosmicFx fx)
    {
        float p = 0f;
        for (int i = 0; i < e.Spec.Stages.Length; i++)
            if ((e.Spec.Stages[i].Fx & fx) != 0 && StageActive(e, i)) p = MathF.Max(p, Eff(e, e.Spec.Stages[i]));
        return p;
    }

    /// <summary>첫 해로운 단계의 실제 · 예보 시각.</summary>
    private int FirstHarmStage(CosmicEvent e)
    {
        var st = e.Spec.Stages;
        for (int i = 0; i < st.Length; i++)
            if ((st[i].Fx & ~(CosmicFx.Light | CosmicFx.Blind)) != 0 && (!st[i].CloseOnly || e.Close)) return i;
        return 0;
    }
    public long PredictedHarm(CosmicEvent e) => e.Predicted + SimTime.Hours(e.Spec.Stages[FirstHarmStage(e)].At);
    /// <summary>다음 (또는 지금) 해로운 단계의 예보 시각 — 사람은 이것을 보고 숨는다.</summary>
    public long NextHarm(CosmicEvent e)
    {
        var st = e.Spec.Stages;
        for (int i = 0; i < st.Length; i++)
        {
            if ((st[i].Fx & ~(CosmicFx.Light | CosmicFx.Blind | CosmicFx.Nav)) == 0 || st[i].CloseOnly && !e.Close) continue;
            if (_w.Tick < StageEnd(e, i)) return Math.Max(e.Predicted + SimTime.Hours(st[i].At), e.Phase == CosmicPhase.Impact ? StageAt(e, i) : 0);
        }
        return long.MaxValue;
    }

    public float BraceLead(CosmicEvent e)
    {
        float lead = Math.Clamp((e.Arrive - e.Seen) / (float)SimTime.TicksPerHour * 0.4f, 1.5f, 12f);
        if (e.EarlyBrace) lead *= 1.5f;
        if (CustomOf(CosmicCustomKind.Drill) is { Followers.Count: >= 2 }) lead *= 1.4f; // 관행: 예보 훈련
        return lead;
    }

    // ───────────────────────────── 생기기 ─────────────────────────────

    /// <summary>강제 발생 (시험 · 관찰자): 몇 시간 뒤 도착하는 대재난을 건다. 예보는 지금 센서·컴퓨터 사정대로.</summary>
    public CosmicEvent Force(CosmicKind k, float? leadHours = null, bool close = true, string source = "강제", bool ghost = false, float? side = null)
    {
        var spec = CosmicCatalog.Spec(k);
        float lead = leadHours ?? R.Range(spec.LeadMin, spec.LeadMax);
        var e = new CosmicEvent
        {
            Id = _next++, Kind = k, Source = source, Ghost = ghost, Seen = _w.Tick,
            Arrive = _w.Tick + SimTime.Hours(lead), Power = close ? R.Range(0.95f, 1.1f) : R.Range(0.7f, 0.95f), Close = close,
            Side = side ?? R.Range(0f, MathF.PI * 2f), PhaseSince = _w.Tick,
        };
        e.Predicted = e.Arrive;
        if (spec.Has(CosmicFx.Strike))
        {
            var center = ShipCenter();
            var hull = _w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor && _w.Ambience.Exposure(r) > 0.5f).ToList();
            var pick = hull.OrderByDescending(r => Facing(r, e, center)).ThenBy(r => r.Id).FirstOrDefault();
            e.TargetRoom = pick?.Id ?? -1;
        }
        Events.Add(e);
        Stats.Spawned++;
        if (ghost) Stats.Ghosts++;
        Detect(e);
        return e;
    }

    private CosmicKind PickKind()
    {
        var leg = _w.Voyage.Current.Kind;
        float total = 0f;
        foreach (var s in CosmicCatalog.All) total += s.Weight * CosmicCatalog.LegMul(leg, s.Kind);
        float u = R.Float() * total;
        foreach (var s in CosmicCatalog.All)
        {
            u -= s.Weight * CosmicCatalog.LegMul(leg, s.Kind);
            if (u <= 0f) return s.Kind;
        }
        return CosmicCatalog.All[^1].Kind;
    }

    /// <summary>항해마다 0~2번: 항해가 바뀌면 언제 올지 정해 둔다 (첫 항해는 길들이는 엿새 뒤부터).</summary>
    private void Schedule()
    {
        var w = _w;
        int vn = w.Voyage.Number;
        if (vn != _voyage)
        {
            _voyage = vn;
            _plan.Clear();
            float u = R.Float();
            int n = u < 0.35f ? 0 : u < 0.82f ? 1 : 2;
            float total = MathF.Max(4f, w.Voyage.TotalDays);
            for (int i = 0; i < n; i++)
            {
                long at = w.Tick + SimTime.Hours(24f * total * R.Range(0.15f, 0.85f));
                if (vn == 1) at = Math.Max(at, SimTime.TicksPerDay * 8 + SimTime.Hours(R.Range(0f, 48f)));
                _plan.Add(at);
            }
            _plan.Sort();
        }
        if (_plan.Count == 0 || w.Tick < _plan[0]) return;
        _plan.RemoveAt(0);
        if (Rate <= 0f || !OutsideSystem.HazardsOn || Active.Any(e => e.Phase <= CosmicPhase.Impact)) return;
        if (Rate < 1f && !R.Chance(Rate)) return;
        var k = PickKind();
        // 틀어진 센서는 없는 것을 본다 (헛예보)
        bool ghost = w.Sensors.Array?.Machine is Machine sm && sm.SensorCal < 0.6f && R.Chance(0.3f);
        Force(k, null, R.Chance(0.4f), "항로", ghost);
    }

    // ───────────────────────────── 예보 (주 컴퓨터) ─────────────────────────────

    private CrewMember? Boss()
    {
        var cmd = _w.Command;
        return cmd.Active && cmd.Commander is CrewMember c0 && c0.CanAct ? c0 : cmd.Captain is CrewMember cap && cap.CanAct ? cap : _w.Crew.Where(c => c.CanAct && !c.IsChild).OrderByDescending(CommandSystem.Leadership).FirstOrDefault();
    }

    private bool ComputerUp => _w.Automation.Present && _w.Automation.MainOnline;

    /// <summary>배가 알아챈다: 컴퓨터가 센서로 (신뢰도 · 오차), 아니면 통신실 사람이 화면을 읽어서, 아니면 모른다.</summary>
    private void Detect(CosmicEvent e)
    {
        var w = _w;
        if (e.Known || e.Phase >= CosmicPhase.Impact) return;
        var spec = e.Spec;
        float q = w.Sensors.Quality;
        float lead = e.HoursTo(w.Tick, e.Arrive);
        bool comp = ComputerUp && w.Sensors.Online;
        var op = w.Sensors.Operator;
        if (!comp && (op == null || !w.Sensors.Online)) return; // 아직 아무도 못 봤다
        float conf = comp ? 0.42f + 0.38f * Math.Clamp(q, 0f, 1.2f) : 0.22f + 0.25f * Math.Clamp(q, 0f, 1f);
        conf += 0.05f * Math.Min(4, Learned) + (w.Eras.Has("quantumsense") ? 0.08f : 0f);
        conf = Math.Clamp(conf * TechWeb.Mul(w, "cosmic.conf"), 0.15f, 0.92f); // v16.14 중력파 조기 경보 · 신호기 코어
        float err = MathF.Max(0.4f, lead * 0.35f * (1f - conf));
        e.Conf0 = conf; e.Err0 = err;
        e.Offset = R.Range(-1f, 1f) * err;
        e.Confidence = conf; e.ErrorHours = err;
        e.Predicted = e.Arrive + SimTime.Hours(e.Offset);
        e.Known = true;
        e.KnownAt = w.Tick;
        e.KnownBy = comp ? w.Automation.Voice.Call : $"{op!.Name}(통신실)";
        Stats.Forecasts++;
        float hrs = e.HoursTo(w.Tick, e.Predicted);
        string text = $"예보 — {spec.Name}: 약 {hrs:0.#}시간 뒤 (신뢰도 {conf * 100:0}% · 오차 ±{err:0.#}시간) · {spec.Detect}";
        w.History.Add(w, HistoryKind.Milestone, text + $" · 우주급", log: true);
        w.RaiseAlert(text, null, AlertLevel.Warning, shipWide: true);
        if (comp)
        {
            int id = e.Id;
            var act = w.Automation.Book.Add(ActKind.Advice, null, $"{spec.Detect} — {spec.Name}",
                $"예측: {hrs:0.#}시간 뒤 · 신뢰도 {conf * 100:0}% · 오차 ±{err:0.#}시간", "예보 · 대비 계획을 짠다", spec.Brace, "cosmic:" + id, 0, MathF.Max(5f, hrs * 60f),
                (world, a) => GradeForecast(world, id));
            if (act != null) e.ForecastAct = act.Id;
            Broadcast(e, $"예보 — {spec.Name}. 약 {hrs:0}시간 뒤, 신뢰도 {conf * 100:0}퍼센트. {spec.Brace}", 1);
        }
        else
        {
            Learn(op!, e, KnowSource.Seen, "화면으로 봤다");
            MarkLog.Add(op!.Memory.Marks, w.Tick, $"{spec.Name}을(를) 레이더 화면으로 먼저 봤다");
        }
        e.AnnouncedPredicted = e.Predicted;
        // 피할 수 있는 것: 컴퓨터가 항로 변경을 제안한다 (아니면 지휘하는 사람이 정한다)
        if (spec.Avoid && !e.Ghost) ProposeAvoid(e);
        // 전자기 펄스가 세면: 컴퓨터가 스스로 꺼지자고 제안한다
        if (spec.Has(CosmicFx.Emp) && spec.Stages.Any(s => (s.Fx & CosmicFx.Emp) != 0 && s.Power >= 0.9f) && comp)
        {
            var p = w.Automation.Asks.Propose("cosmic:shutdown:" + e.Id, "cosmic-shutdown", null, $"주 컴퓨터를 내려 두자 ({spec.Name})",
                $"전자기 펄스 예보 {spec.Stages.Max(s => s.Power) * 100:0}% — 켜 둔 회로는 탄다", "마지막 한 시간 전에 내리고, 지나가면 사람이 다시 켠다 (그동안 자동화 없음)", 30f, null);
            Stats.Proposals++;
            _ = p;
        }
    }

    private (int, string)? GradeForecast(World w, int id)
    {
        var e = Events.FirstOrDefault(x => x.Id == id);
        if (e == null) return (2, "알 수 없다");
        if (e.Ghost) return e.Phase == CosmicPhase.Done ? (-1, "헛예보 — 아무것도 오지 않았다") : null;
        if (w.Tick < e.Arrive) return null;
        float off = MathF.Abs(e.HoursTo(e.Predicted, e.Arrive));
        return off <= e.ErrorHours * 1.2f + 0.5f ? (1, $"맞았다 — 예보와 {off:0.#}시간 차이") : (-1, $"빗나갔다 — {off:0.#}시간 어긋났다");
    }

    private void Broadcast(CosmicEvent e, string text, int prio)
    {
        var w = _w;
        var b = w.Automation.Speak.Announce(w.Automation.Voice.Style(text), null, prio);
        if (b == null) return;
        foreach (var id in b.HeardBy)
            if (w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c) Learn(c, e, KnowSource.Radio, "방송");
    }

    private void Learn(CrewMember c, CosmicEvent e, KnowSource src, string how)
    {
        if (c.Dead || Knows(c, e)) return;
        _know[KK(c.Id, e.Id)] = (src, _w.Tick, how);
        _went.Add(c.Id);
        c.NextThinkTick = Math.Min(c.NextThinkTick, _w.Tick + 1);
    }

    /// <summary>한 시간마다: 예보가 다가갈수록 맞아진다 (센서 · 컴퓨터가 살아 있을 때만). 크게 바뀌면 다시 알린다.</summary>
    private void Refine(CosmicEvent e)
    {
        var w = _w;
        if (!e.Known || e.Phase >= CosmicPhase.Impact) return;
        bool live = ComputerUp && w.Sensors.Online && w.Sensors.Quality > 0.15f;
        if (!live) { e.Confidence = MathF.Max(0.1f, e.Confidence - 0.01f); return; }
        float span = MathF.Max(1f, e.Arrive - e.Seen);
        float f = Math.Clamp((e.Arrive - w.Tick) / span, 0f, 1f);
        e.Confidence = e.Conf0 + (0.97f - e.Conf0) * (1f - f);
        e.ErrorHours = MathF.Max(0.15f, e.Err0 * f);
        if (!e.Ghost) e.Predicted = e.Arrive + SimTime.Hours(e.Offset * f);
        if (MathF.Abs(e.HoursTo(e.AnnouncedPredicted, e.Predicted)) >= 1.5f)
        {
            e.AnnouncedPredicted = e.Predicted;
            Broadcast(e, $"예보 고침 — {e.Spec.Name}, {e.HoursTo(w.Tick, e.Predicted):0.#}시간 뒤 (신뢰도 {e.Confidence * 100:0}퍼센트)", 1);
        }
    }

    // ───────────────────────────── 결정: 피할까 · 버릴까 ─────────────────────────────

    public float AvoidCost(CosmicEvent e) => e.Spec.AvoidFuel * 25f * MathF.Max(0.3f, _w.Propulsion.Scale);

    private void ProposeAvoid(CosmicEvent e)
    {
        var w = _w;
        float cost = AvoidCost(e);
        if (ComputerUp)
        {
            w.Automation.Asks.Propose("cosmic:avoid:" + e.Id, "cosmic-avoid", null, $"항로를 바꿔 {e.Spec.Name} 비키기 (추진제 {cost:0}kg)",
                $"{e.Spec.Detect} · 신뢰도 {e.Confidence * 100:0}% · 남은 추진제 {w.Propulsion.Propellant:0}kg",
                e.Spec.Has(CosmicFx.Strike) ? "비키면 맞지 않는다 — 못 비키면 그 구획을 비우고 봉쇄" : "비키면 훨씬 약하게 지나간다", 30f, null);
            Stats.Proposals++;
        }
    }

    private void DecideAvoid(CosmicEvent e)
    {
        var w = _w;
        if (e.AvoidPlan != 0 || !e.Spec.Avoid || e.Ghost || !e.Known) return;
        float cost = AvoidCost(e);
        bool yes;
        string why;
        var p = w.Automation.Asks.Latest("cosmic:avoid:" + e.Id);
        if (p != null)
        {
            if (p.State == ProposalState.Pending) return;
            yes = p.Accepted;
            why = $"컴퓨터 제안 {(yes ? "받음" : "거절")} ({p.DecidedBy})";
        }
        else
        {
            // 컴퓨터 없이: 지휘하는 사람이 가치관대로 (부딪히는 것이면 거의 피한다)
            var boss = Boss();
            float want = e.Spec.Has(CosmicFx.Strike) ? 0.85f : 0.5f;
            if (boss != null) want += boss.Value switch { CrewValue.Safety => 0.2f, CrewValue.Efficiency => -0.25f, CrewValue.People => 0.1f, _ => 0f };
            yes = want >= 0.5f;
            why = boss != null ? $"{boss.Name} 결정 ({MeetingSystem.ValueName(boss.Value)})" : "정할 사람이 없다";
            if (boss != null) w.Meetings.Record($"{e.Spec.Name} — {(yes ? "항로를 바꾼다" : "그대로 간다")}", "cosmic:avoid", -1, boss, yes ? new List<CrewMember> { boss } : new(), yes ? new() : new List<CrewMember> { boss }, e.Spec.Name);
        }
        if (yes && w.Propulsion.Propellant < cost) { yes = false; why += $" · 추진제가 모자란다 ({w.Propulsion.Propellant:0}/{cost:0}kg)"; }
        if (yes && (w.Propulsion.Thrust < 0.15f || w.Propulsion.Control().quality <= 0f)) { yes = false; why += " · 엔진이나 조종할 사람이 없다"; }
        e.AvoidPlan = yes ? 1 : -1;
        e.AvoidWhy = why;
        if (yes)
        {
            e.BurnAt = w.Tick + SimTime.Minutes(30);
            e.BurnEnd = e.BurnAt + SimTime.Hours(1f);
            w.Log.Add(w.Tick, LogKind.Ship, $"{e.Spec.Name} — 항로를 바꾼다: 30분 뒤 한 시간 연소 (추진제 {cost:0}kg) · {why}");
        }
        else
        {
            w.Log.Add(w.Tick, LogKind.Warning, $"{e.Spec.Name} — 그대로 간다 · {why}");
            if (e.Spec.Has(CosmicFx.Strike)) PlanSeal(e, "비키지 않기로 했다");
        }
    }

    private void PlanSeal(CosmicEvent e, string why)
    {
        var w = _w;
        if (e.SealPlan || e.TargetRoom < 0 || e.TargetRoom >= w.Ship.Rooms.Count) return;
        e.SealPlan = true;
        var room = w.Ship.Rooms[e.TargetRoom];
        // 파편이 안쪽으로 뻗는 줄: 그 구획 뒤로 열 칸 남짓 — 그 방들도 비운다
        var inward = -Dir(e);
        var side = new Vector2(-inward.Y, inward.X);
        for (float k = 0f; k <= 11f; k += 0.5f)
            for (int l = -1; l <= 1; l++)
            {
                var p = room.Center + inward * k + side * l;
                if (w.Ship.RoomAt(Cell.FromPosition(p)) is Room r && r != room && !e.Evac.Contains(r.Id)) e.Evac.Add(r.Id);
            }
        w.Automation.Book.Add(ActKind.Advice, room, $"{e.Spec.Name} 충돌 예상 구획 — {room.Name}", $"{why} — 그 구획을 비우고 봉쇄하면 옆으로 번지지 않는다", "봉쇄 계획", $"{room.Name}에서 나오고 격벽을 막아 달라", "cosmic:seal:" + e.Id, 0, 30f);
        Broadcast(e, $"{room.Name} 구역을 비워 달라 — {e.Spec.Name} 충돌 예상. 비면 봉쇄한다", 2);
        w.RaiseAlert($"{e.Spec.Name} — {room.Name}을(를) 비우고 봉쇄한다 ({why})", room, AlertLevel.Critical, shipWide: true);
        if (e.Phase == CosmicPhase.Brace) AddTask(e, BraceKind.Seal, room.Id, -1, 0.35f, $"{room.Name} 비우고 봉쇄");
    }

    private void Burn(CosmicEvent e)
    {
        var w = _w;
        if (e.BurnAt < 0) return;
        if (w.Tick >= e.BurnAt && w.Tick < e.BurnEnd) { w.Propulsion.CourseBurn = true; return; }
        if (w.Tick < e.BurnEnd) return;
        w.Propulsion.CourseBurn = false;
        e.BurnAt = -1;
        float cost = AvoidCost(e);
        w.Propulsion.Propellant = MathF.Max(0f, w.Propulsion.Propellant - cost);
        e.FuelSpent = cost;
        foreach (var m in w.Propulsion.Engines) m.Wear = MathF.Min(1f, m.Wear + 0.03f);
        var (control, by, who) = w.Propulsion.Control();
        float leadLeft = e.HoursTo(w.Tick, e.Arrive);
        float p = Math.Clamp(control * MathF.Min(1f, w.Propulsion.Thrust / 0.6f) * (0.55f + 0.45f * Math.Clamp(leadLeft / 6f, 0f, 1f)) + 0.15f, 0f, 0.97f);
        if (R.Chance(p))
        {
            e.Avoided = true;
            Stats.Avoided++;
            w.History.Add(w, HistoryKind.Response, $"{e.Spec.Name} — 항로를 바꿔 비켰다 ({by} · 추진제 {cost:0}kg)", log: true);
            w.RaiseAlert($"{e.Spec.Name} — 항로 변경 성공 ({by})", null, AlertLevel.Notice, shipWide: true);
            if (who != null) MarkLog.Add(who.Memory.Marks, w.Tick, $"{e.Spec.Name}을(를) 비키려 엔진을 몰았다");
            if (e.Sealed) Unseal(e, "비켰다");
        }
        else
        {
            e.AvoidFailed = true;
            Stats.AvoidFailed++;
            e.Power *= 0.75f;
            w.Log.Add(w.Tick, LogKind.Warning, $"{e.Spec.Name} — 다 비키지 못했다 ({by} · 성공 확률 {p * 100:0}%)");
            if (e.Spec.Has(CosmicFx.Strike)) PlanSeal(e, "연소로 다 비키지 못했다");
        }
    }

    private void Unseal(CosmicEvent e, string why)
    {
        var w = _w;
        if (e.TargetRoom < 0) return;
        var room = w.Ship.Rooms[e.TargetRoom];
        if (!room.Abandoned || room.Leaking) return;
        room.Abandoned = false;
        room.Lockdown = false;
        room.VentOpen = true;
        foreach (var d in room.Doors) if (!d.IsExternal && !d.Welded) d.Locked = false;
        w.Log.Add(w.Tick, LogKind.Ship, $"{room.Name} 봉쇄를 풀었다 — {why}");
    }

    // ───────────────────────────── 대비 ─────────────────────────────

    private void AddTask(CosmicEvent e, BraceKind k, int room, int furniture, float hours, string label, bool last = false)
    {
        if (e.Tasks.Any(t => t.Kind == k && t.RoomId == room && t.FurnitureId == furniture && !t.Done)) return;
        e.Tasks.Add(new BraceTask { Id = _taskNext++, Kind = k, RoomId = room, FurnitureId = furniture, Hours = hours, Label = label, Last = last });
    }

    private static bool Sensitive(Furniture f) => Hazards.IsElectronic(f) || f.Type is FurnitureType.Fabricator or FurnitureType.NavComputer or FurnitureType.DiagnosticScanner
        or FurnitureType.SolderStation or FurnitureType.CalibrationRig or FurnitureType.Projector or FurnitureType.ThermalCamera;

    /// <summary>대비 계획 (주 컴퓨터가 짠다 — 컴퓨터가 없으면 지휘하는 사람이 같은 목록을 부른다).</summary>
    private void BuildPlan(CosmicEvent e)
    {
        var w = _w;
        var ship = w.Ship;
        var spec = e.Spec;
        var refuges = Refuges();
        bool rad = spec.Has(CosmicFx.Radiation) || spec.Has(CosmicFx.Heat) && spec.Has(CosmicFx.Plasma);
        if (rad && refuges.Count > 0)
        {
            foreach (var r in refuges.Take(e.EarlyBrace ? 2 : 1)) AddTask(e, BraceKind.WaterWall, r.Id, -1, 0.4f, $"{r.Name} 물벽 채우기 (물주머니)");
            AddTask(e, BraceKind.Supplies, refuges[0].Id, -1, 0.3f, $"{refuges[0].Name}에 물 · 먹을 것 · 약 옮기기");
        }
        // 관측창 덮개: 컴퓨터가 있으면 한꺼번에 내린다, 없으면 사람이 방마다
        if (spec.Has(CosmicFx.Light) || rad || spec.Has(CosmicFx.Debris) || spec.Has(CosmicFx.Plasma))
        {
            var windows = ship.Rooms.Where(r => !r.Detached && w.Body.WindowsOf(r) > 0).ToList();
            if (ComputerUp)
            {
                foreach (var r in windows) _shut.Add(r.Id);
                if (windows.Count > 0) w.Automation.Book.Add(ActKind.Advice, null, $"{spec.Name} 대비", "관측창으로 빛 · 입자가 든다", $"관측창 덮개 {windows.Count}곳을 내렸다", "", "cosmic:shut:" + e.Id, 0, 10f);
            }
            else foreach (var r in windows.Take(4)) AddTask(e, BraceKind.Shutters, r.Id, -1, 0.12f, $"{r.Name} 관측창 덮개 손으로 내리기");
        }
        if (spec.Has(CosmicFx.Emp) || spec.Has(CosmicFx.Radiation) && spec.Stages.Any(s => s.Power >= 0.8f))
        {
            bool needEyes = spec.Has(CosmicFx.Debris) || spec.Has(CosmicFx.Strike) || spec.Has(CosmicFx.Hostile);
            int n = 0;
            foreach (var f in ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached && f.Machine != null && Sensitive(f)).OrderBy(f => f.Id))
            {
                if (f.Type == FurnitureType.MainComputer && !e.ShutdownComputer) continue;
                if (f.Type == FurnitureType.Console && f.Room.Type == RoomType.Bridge) continue; // 조타는 남긴다
                if (f.Type == FurnitureType.SensorArray && needEyes) continue; // 날아드는 것을 봐야 한다
                if (n++ >= 7 && f.Type != FurnitureType.MainComputer) continue;
                AddTask(e, BraceKind.PowerDown, f.Room.Id, f.Id, 0.08f, $"{f.Label} 끄기", last: f.Type is FurnitureType.MainComputer or FurnitureType.SensorArray); // 눈과 머리는 마지막에
            }
        }
        if (spec.Has(CosmicFx.Shock) || spec.Has(CosmicFx.Quake) || spec.Has(CosmicFx.Tidal) || spec.Has(CosmicFx.Strike))
            foreach (var r in ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor && r.Furniture.Count >= 2)
                         .OrderByDescending(r => w.Belongings.All.Count(b => b.At is Cell bc && ship.RoomAt(bc) == r) + (r.Type is RoomType.Mess or RoomType.Galley or RoomType.Medbay or RoomType.Quarters ? 3 : 0)).ThenBy(r => r.Id).Take(5))
                AddTask(e, BraceKind.Stow, r.Id, -1, 0.2f, $"{r.Name} 물건 묶어 두기");
        if (spec.Has(CosmicFx.Plasma) || spec.Has(CosmicFx.Debris) || spec.Has(CosmicFx.Hostile))
            if (w.Sensors.CommsRoom is Room comms) AddTask(e, BraceKind.Fold, comms.Id, -1, 0.25f, "바깥 설비 접기 (안테나 · 태양 날개)");
        if (spec.Has(CosmicFx.Heat) || spec.Has(CosmicFx.Cold))
            foreach (var r in ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor && w.Ambience.Exposure(r) > 0.6f && r.Furniture.Any(f => f.Type is FurnitureType.Bed or FurnitureType.Cot or FurnitureType.GrowBed)).OrderBy(r => r.Id).Take(3))
                AddTask(e, BraceKind.Insulate, r.Id, -1, 0.2f, spec.Has(CosmicFx.Cold) ? $"{r.Name} 보온 (담요 · 단열판)" : $"{r.Name} 열 막기 (단열판)");
        if (spec.Has(CosmicFx.Nav) || spec.Has(CosmicFx.Blind) && spec.Has(CosmicFx.Debris))
            if (ship.RoomsOf(RoomType.Bridge).FirstOrDefault() is Room br) AddTask(e, BraceKind.Pilot, br.Id, -1, 0.3f, "손 조종 준비 (항법 먹통 대비)");
        if (e.SealPlan && e.TargetRoom >= 0) AddTask(e, BraceKind.Seal, e.TargetRoom, -1, 0.35f, $"{ship.Rooms[e.TargetRoom].Name} 비우고 봉쇄");
    }

    /// <summary>지금 맡을 수 있는 대비 일.</summary>
    public bool Open(CosmicEvent e, BraceTask t)
    {
        if (t.Done || t.By >= 0) return false;
        if (t.Kind == BraceKind.Restart) return e.Phase == CosmicPhase.After || e.Phase == CosmicPhase.Done;
        if (e.Phase != CosmicPhase.Brace && !(e.Phase == CosmicPhase.Impact && _w.Tick < NextHarm(e))) return false;
        if (t.Last && _w.Tick < PredictedHarm(e) - SimTime.Hours(1f)) return false;
        if (t.Kind == BraceKind.Seal && (e.Avoided || e.Sealed)) return false;
        // 비우고 봉쇄할 구획 안의 일은 하지 않는다 (들어가면 곧 비우라고 나와야 하고, 들어간 사람이 봉쇄를 막는다) — 파편 줄의 방은 마지막 두 시간
        if (t.Kind != BraceKind.Seal && e.SealPlan && !e.Avoided && t.RoomId >= 0
            && (t.RoomId == e.TargetRoom || e.Evac.Contains(t.RoomId) && _w.Tick >= e.Arrive - SimTime.Hours(2f))) return false;
        return true;
    }

    /// <summary>대비 일을 마쳤다 — 실제 효과.</summary>
    internal bool Complete(CosmicEvent e, BraceTask t, CrewMember c)
    {
        var w = _w;
        var room = t.RoomId >= 0 && t.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[t.RoomId] : null;
        switch (t.Kind)
        {
            case BraceKind.WaterWall:
            {
                if (room == null) return false;
                float take = MathF.Min(60f, MathF.Max(0f, w.Water.Level - 60f)); // 마실 물은 남긴다
                w.Water.Level -= take;
                _waterUsed += take;
                _water[room.Id] = MathF.Min(1f, Water(room) + take / 80f);
                MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 물주머니로 물벽을 쳤다 ({take:0}L)");
                break;
            }
            case BraceKind.Supplies: if (room != null) _supplied.Add(room.Id); break;
            case BraceKind.Shutters: if (room != null) _shut.Add(room.Id); break;
            case BraceKind.PowerDown:
            {
                var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == t.FurnitureId);
                if (f == null) return false;
                if (!_safed.Contains(f.Id)) _safed.Add(f.Id);
                if (f.Machine != null) MarkLog.Add(f.Machine.Marks, w.Tick, $"{e.Spec.Name} 대비로 껐다 ({c.Name})");
                if (f.Type == FurnitureType.MainComputer) w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(c.Name)} 주 컴퓨터를 내렸다 — {e.Spec.Name}이 지나갈 때까지 손으로", f.Room, new[] { c }, log: true);
                break;
            }
            case BraceKind.Restart:
            {
                _safed.Remove(t.FurnitureId);
                var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == t.FurnitureId);
                if (f?.Machine != null) MarkLog.Add(f.Machine.Marks, w.Tick, $"다시 켰다 ({c.Name})");
                if (f?.Type == FurnitureType.MainComputer) w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(c.Name)} 주 컴퓨터를 다시 켰다", f.Room, new[] { c }, log: true);
                break;
            }
            case BraceKind.Stow: if (room != null) { _stowed.Add(room.Id); MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 흔들림 대비로 물건을 묶었다"); } break;
            case BraceKind.Fold: _folded = true; break;
            case BraceKind.Insulate: if (room != null) _insulated.Add(room.Id); break;
            case BraceKind.Pilot: _pilot = c.Id; break;
            case BraceKind.Seal:
            {
                if (room == null) return false;
                if (w.Crew.Any(x => !x.Dead && x.Room == room)) return false; // 안에 사람이 있다 — 나올 때까지
                room.Abandoned = true;
                room.AbandonedSince = w.Tick;
                room.AbandonReason = $"{e.Spec.Name} 충돌 대비";
                room.VentOpen = false;
                room.Lockdown = true;
                foreach (var d in room.Doors) if (!d.IsExternal) d.Locked = true;
                e.Sealed = true;
                Stats.Sealed++;
                MarkLog.Add(room.Marks, w.Tick, $"{e.Spec.Name} 충돌 대비로 비우고 봉쇄 ({c.Name})");
                w.History.Add(w, HistoryKind.Adaptation, $"{Ko.EulReul(room.Name)} 비우고 봉쇄했다 — {e.Spec.Name}이 그쪽으로 온다", room, new[] { c }, log: true);
                break;
            }
        }
        t.Done = true;
        t.DoneBy = c.Name;
        t.DoneAt = w.Tick;
        Stats.BraceDone++;
        c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.03f); // 대비도 일이다
        return true;
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        Schedule();
        bool hourly = w.Tick % SimTime.TicksPerHour < World.SystemInterval;
        for (int i = 0; i < Events.Count; i++)
        {
            var e = Events[i];
            if (e.Phase == CosmicPhase.Done) continue;
            Step(e, dt, hourly);
        }
        Fields();
        Launches();
        Latent();
        if (hourly) { Hourly(); Customary(); }
        // 끝난 대재난의 대비 흔적은 걷는다 (덮개 · 고정 · 보온)
        if (!Active.Any(e => e.Phase <= CosmicPhase.Impact))
        {
            if (_shut.Count > 0 || _stowed.Count > 0 || _insulated.Count > 0 || _folded || _pilot >= 0 || _supplied.Count > 0) { _shut.Clear(); _stowed.Clear(); _insulated.Clear(); _supplied.Clear(); _folded = false; _pilot = -1; }
            if (_water.Count > 0 && hourly)
            {
                // 물벽 물은 천천히 탱크로 돌려 붓는다 (조금은 버린다)
                foreach (var k in _water.Keys.OrderBy(x => x).ToList()) { _water[k] -= 0.1f; if (_water[k] <= 0f) _water.Remove(k); }
                float back = MathF.Min(_waterUsed, 12f);
                _waterUsed -= back;
                w.Water.Level = MathF.Min(w.Water.Capacity, w.Water.Level + back * 0.85f);
            }
        }
    }

    private void Step(CosmicEvent e, float dt, bool hourly)
    {
        var w = _w;
        if (!e.Known && hourly) Detect(e);
        if (hourly) Refine(e);
        DecideAvoid(e);
        Burn(e);
        switch (e.Phase)
        {
            case CosmicPhase.Forecast:
                if (e.Ghost && e.Known && w.Tick >= e.Predicted + SimTime.Hours(e.ErrorHours + 1f)) { GhostEnd(e); return; }
                if (!e.Ghost && w.Tick >= e.Arrive) { BeginImpact(e); return; }
                if (e.Known && w.Tick >= e.Predicted - SimTime.Hours(BraceLead(e))) BeginBrace(e);
                break;
            case CosmicPhase.Brace:
                if (e.Ghost && w.Tick >= e.Predicted + SimTime.Hours(e.ErrorHours + 1f)) { GhostEnd(e); return; }
                if (!e.Ghost && w.Tick >= e.Arrive) { BeginImpact(e); return; }
                ShelterCall(e);
                break;
            case CosmicPhase.Impact:
                ShelterCall(e);
                Impact(e, dt);
                if (w.Tick >= e.End) Aftermath(e);
                break;
            case CosmicPhase.After:
                if (w.Tick >= e.PhaseSince + SimTime.Hours(12f)) // 아무도 다시 켜지 않으면 예비 제어기가 켠다
                    foreach (var t in e.Tasks.Where(t => t.Kind == BraceKind.Restart && !t.Done && t.By < 0)) { _safed.Remove(t.FurnitureId); t.Done = true; t.DoneBy = "예비 제어기"; }
                if (w.Tick >= e.AfterUntil) Close(e);
                break;
        }
        // 맡았다가 손 놓은 일은 다시 내놓는다
        foreach (var t in e.Tasks)
            if (!t.Done && t.By >= 0 && (w.Crew.FirstOrDefault(c => c.Id == t.By) is not CrewMember c || c.Dead || c.Job?.Activity is not CosmicBraceActivity || w.Tick - t.ClaimedAt > SimTime.Hours(3f)))
                t.By = -1;
    }

    /// <summary>숨을 시간: 해로운 단계 한 시간 전에 컴퓨터가 크게 부른다 (자는 사람도 깬다 · 선외 작업 중지).</summary>
    private void ShelterCall(CosmicEvent e)
    {
        var w = _w;
        long harm = NextHarm(e);
        if (harm == long.MaxValue || w.Tick < harm - SimTime.Hours(1f) || w.Tick >= harm) return;
        string key = $"shelter-call:{harm}";
        if (e.Notes.Contains(key)) return;
        e.Notes.Add(key);
        Broadcast(e, $"{e.Spec.Name} 한 시간 전 — {(CosmicCrew.NeedsShelter(e.Spec) ? "대피소로" : "몸을 고정하라")}. 선외 작업 중지", 2);
        foreach (var c in w.Crew.Where(c => !c.Dead && c.Outside && Knows(c, e))) c.Interrupt(w);
    }

    private void BeginBrace(CosmicEvent e)
    {
        var w = _w;
        e.Phase = CosmicPhase.Brace;
        e.PhaseSince = w.Tick;
        BuildPlan(e);
        // 컴퓨터의 꺼짐 제안을 받았으면 주 컴퓨터 끄기를 마지막 일로
        if (w.Automation.Asks.Latest("cosmic:shutdown:" + e.Id) is Proposal sp && sp.Accepted && !e.ShutdownComputer)
        {
            e.ShutdownComputer = true;
            if (w.Automation.Computer?.Body is Furniture mc) AddTask(e, BraceKind.PowerDown, mc.Room.Id, mc.Id, 0.08f, "주 컴퓨터 내리기 (마지막에)", last: true);
        }
        string plan = string.Join(" · ", e.Tasks.GroupBy(t => t.Kind).Select(g => $"{KindName(g.Key)} {g.Count()}"));
        w.History.Add(w, HistoryKind.Decision, $"{e.Spec.Name} 대비 시작 — {plan}", log: true);
        w.Automation.Book.Add(ActKind.Advice, null, $"{e.Spec.Name} {e.HoursTo(w.Tick, e.Predicted):0.#}시간 전", "대비할 시간", $"대비 계획: {plan}", "맡을 수 있는 사람은 대비 일을", "cosmic:plan:" + e.Id, 0, 20f);
        Broadcast(e, $"대비 시작 — {e.Spec.Name}. {plan}", 2);
        foreach (var c in w.Crew.Where(c => !c.Dead && c.Outside && Knows(c, e))) c.Interrupt(w); // 선외 작업 중단
    }

    public static string KindName(BraceKind k) => k switch
    {
        BraceKind.WaterWall => "물벽", BraceKind.Supplies => "물자", BraceKind.Shutters => "덮개", BraceKind.PowerDown => "장비 끄기", BraceKind.Stow => "고정",
        BraceKind.Seal => "봉쇄", BraceKind.Fold => "바깥 설비 접기", BraceKind.Insulate => "보온", BraceKind.Pilot => "손 조종", _ => "다시 켜기",
    };

    private void GhostEnd(CosmicEvent e)
    {
        var w = _w;
        e.Phase = CosmicPhase.Done;
        e.PhaseSince = w.Tick;
        e.Grade = "헛예보";
        int braced = e.Tasks.Count(t => t.Done);
        w.History.Add(w, HistoryKind.Lesson, $"{e.Spec.Name} 예보는 헛것이었다 — 틀어진 센서가 본 것 (대비 일 {braced}개가 헛수고)", log: true);
        foreach (var c in w.Crew.Where(c => !c.Dead && Knows(c, e)))
            w.Automation.Trusts.Change(c, -0.06f - (braced > 0 ? 0.02f : 0f), $"{e.Spec.Name} 예보가 헛것이었다", quiet: true);
        Learned++;
        foreach (var t in e.Tasks.Where(t => t.Kind == BraceKind.PowerDown && t.Done)) _safed.Remove(t.FurnitureId);
        if (e.Sealed) Unseal(e, "헛예보");
    }

    private void BeginImpact(CosmicEvent e)
    {
        var w = _w;
        e.Phase = CosmicPhase.Impact;
        e.PhaseSince = w.Tick;
        var room = e.TargetRoom >= 0 ? w.Ship.Rooms[e.TargetRoom] : null;
        e.Cause = w.Causes.Root(CauseKind.Hazard, e.Spec.Name, room, null, lasting: true);
        w.History.NoteCause(w, e.Spec.Name);
        bool surprise = !e.Known;
        if (surprise) { e.Known = true; e.KnownAt = w.Tick; e.KnownBy = "몸으로"; e.Predicted = e.Arrive; }
        foreach (var c in w.Crew.Where(c => !c.Dead)) Learn(c, e, KnowSource.Seen, "겪었다");
    }

    private bool Sheltered(CrewMember c) => !c.Outside && c.Room is Room r && RelExposure(r) <= 0.32f;

    private void Impact(CosmicEvent e, float dt)
    {
        var w = _w;
        var st = e.Spec.Stages;
        using var _ = w.Causes.Because(e.Cause);
        for (int i = 0; i < st.Length; i++)
        {
            bool active = StageActive(e, i);
            if (active && (e.StagesStarted & (1 << i)) == 0)
            {
                e.StagesStarted |= 1 << i;
                BeginStage(e, i);
            }
            if (active) StageTick(e, st[i], Eff(e, st[i]), dt);
        }
    }

    private void BeginStage(CosmicEvent e, int i)
    {
        var w = _w;
        var s = e.Spec.Stages[i];
        float p = Eff(e, s);
        if (p <= 0f && !(e.Avoided && (s.Fx & CosmicFx.Strike) != 0)) return;
        string text = e.Avoided && p < 0.3f ? $"{e.Spec.Name} — 비켜 간 자리에서 약하게: {s.Text}" : $"{e.Spec.Name} — {s.Text}";
        w.RaiseAlert(text, e.TargetRoom >= 0 && (s.Fx & CosmicFx.Strike) != 0 ? w.Ship.Rooms[e.TargetRoom] : null, p >= 0.5f ? AlertLevel.Critical : AlertLevel.Warning, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, text + " (우주급)", crew: w.Crew.Where(c => !c.Dead));
        // 방사선이 처음 닿는 순간: 누가 어디 있었나 (대피소 vs 바깥 쪽)
        if ((s.Fx & CosmicFx.Radiation) != 0 && !e.Snapped)
            foreach (var c in w.Crew.Where(c => !c.Dead)) e.Snap.Add((c.Id, c.Dose, Sheltered(c)));
        var center = ShipCenter();
        if ((s.Fx & CosmicFx.Light) != 0)
        {
            Flash(e, p);
            long harm = NextHarm(e);
            if (harm != long.MaxValue && harm - w.Tick > SimTime.Hours(2f))
            {
                w.Automation.Book.Add(ActKind.Advice, null, $"{e.Spec.Name} 섬광", $"예측: {e.HoursTo(w.Tick, harm):0}시간 뒤 다음 파도", "다시 예보", "그 전에 대비를 마치고 숨을 곳으로", "cosmic:wave:" + e.Id, 0, 10f);
                Broadcast(e, $"섬광을 봤다 — {e.Spec.Name}. {e.HoursTo(w.Tick, harm):0}시간 뒤 방사선 파도가 온다", 2);
            }
        }
        if ((s.Fx & CosmicFx.Shock) != 0) Shock(e, p, center);
        if ((s.Fx & CosmicFx.Emp) != 0) Emp(e, p);
        if ((s.Fx & CosmicFx.Strike) != 0) Strike(e, p, center);
        if ((s.Fx & (CosmicFx.Debris | CosmicFx.Hostile)) != 0 && p > 0f)
        {
            bool hostile = (s.Fx & CosmicFx.Hostile) != 0;
            int n = Math.Min(14, (int)MathF.Round((hostile ? 2f : 2.6f) * s.Hours * p) + 1);
            for (int k = 0; k < n; k++)
                _launch.Add((w.Tick + SimTime.Hours(R.Range(0f, s.Hours)), e.Id, hostile ? R.Range(0.3f, 0.7f) : R.Range(0.15f, 0.55f) * (0.6f + 0.6f * p), hostile));
            _launch.Sort((a, b) => a.tick.CompareTo(b.tick));
        }
        if ((s.Fx & CosmicFx.Quake) != 0) foreach (var c in w.Crew.Where(c => !c.Dead)) c.Jolt(w);
    }

    /// <summary>섬광: 열린 관측창 곁 사람이 눈이 멀 듯 놀라고, 센서가 잠깐 탄다.</summary>
    private void Flash(CosmicEvent e, float p)
    {
        var w = _w;
        FlashNow = MathF.Max(FlashNow, p);
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Outside && c.Room != null))
        {
            var r = c.Room!;
            bool window = w.Body.WindowsOf(r) > 0 && !_shut.Contains(r.Id);
            if (!window) continue;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.12f * p);
            Memory.Frighten(w, c, r, 0.12f * p, $"{e.Spec.Name} 섬광");
            MarkLog.Add(c.Memory.Marks, w.Tick, $"창밖이 하얗게 탔다 — {e.Spec.Name}");
            c.Jolt(w);
        }
        foreach (var c in w.Crew.Where(c => !c.Dead && c.Outside))
        {
            NeedsSystem.AddInjury(c.Vitals, 0.05f * p, $"{e.Spec.Name} 섬광 (선체 밖)");
            Memory.Shake(w, c, 0.08f * p, $"선체 밖에서 {e.Spec.Name} 섬광을 봤다");
        }
        if (w.Sensors.Array?.Machine is Machine sm && !_safed.Contains(sm.Body.Id)) sm.SensorCal = MathF.Max(0.3f, sm.SensorCal - 0.25f * p);
    }

    /// <summary>충격파: 마주 보는 외벽 · 넘어짐 · 떨어져 깨지는 물건 · 불씨 · 바깥 설비.</summary>
    private void Shock(CosmicEvent e, float p, Vector2 center)
    {
        var w = _w;
        var ship = w.Ship;
        ShakeNow = MathF.Max(ShakeNow, p);
        foreach (var (cell, wall) in ship.Walls.ToList())
        {
            if (!wall.IsHull) continue;
            var d = cell.Center - center;
            if (d.LengthSquared() < 0.5f || Vector2.Dot(Vector2.Normalize(d), Dir(e)) < 0.55f) continue;
            Hull.Damage(ship, cell, 0.1f * p * R.Range(0.6f, 1.2f));
        }
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Outside && c.Room != null))
        {
            c.Jolt(w);
            float chance = (c.Pose is Pose.Sitting or Pose.Sleeping ? 0.08f : 0.4f) * p * (Stowed(c.Room!) ? 0.5f : 1f);
            if (!R.Chance(chance)) continue;
            float dmg = R.Range(0.05f, 0.3f) * p * (Stowed(c.Room!) ? 0.6f : 1f); // v16.24 벽 · 설비 모서리에 내동댕이 — 묶지 않은 방은 날아온 물건까지
            c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg * 0.8f);
            NeedsSystem.AddInjury(c.Vitals, dmg, $"{e.Spec.Name} 충격에 넘어짐");
            Memory.Shake(w, c, 0.05f, $"{e.Spec.Name} 충격에 넘어졌다");
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{e.Spec.Name} 충격에 넘어졌다");
            w.Body.Stats.Falls++;
            e.Falls++;
        }
        foreach (var r in ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor).OrderBy(r => r.Id))
        {
            bool tied = Stowed(r);
            // 묶지 않은 방: 선반 물건이 떨어져 깨진다 (유리 조각 → 맨발로 걷다 베인다)
            if (!tied && r.Furniture.Count >= 2 && R.Chance(0.5f * p))
            {
                var spot = r.Cells.Where(ship.IsOpenFloor).OrderBy(x => x.X * 31 + x.Y).Skip(R.Range(0, Math.Max(1, r.Cells.Count / 2))).FirstOrDefault();
                if (spot != default) { w.Body.RaiseMark(spot, CellMark.Glass, 0.6f, $"{e.Spec.Name} 충격에 떨어져 깨졌다"); e.Broken++; }
            }
            foreach (var b in w.Belongings.All.Where(b => b.At is Cell bc && ship.RoomAt(bc) == r))
                if (!tied && R.Chance(0.35f * p)) { b.Condition = MathF.Max(0f, b.Condition - 0.25f); MarkLog.Add(b.Marks, w.Tick, $"{e.Spec.Name} 충격에 떨어졌다"); e.Broken++; }
            // 불씨: 전기 설비가 있는 묶지 않은 방
            if (!tied && R.Chance(0.05f * p) && r.Furniture.FirstOrDefault(f => f.Machine is Machine m && m.Spec.PowerDraw > 0f) is Furniture hot)
            {
                var at = hot.UseSpots.FirstOrDefault(ship.IsOpenFloor);
                if (at != default && Incidents.Fire(w, at)) e.Fires++;
            }
        }
        foreach (var f in w.Exterior.All) w.Exterior.Damage(f, 0.12f * p * (_folded ? 0.3f : 1f), e.Spec.Name);
    }

    /// <summary>전자기 펄스: 켜 둔 전자 장비 · 컴퓨터 · 로봇 · 드론 · 문 구동기 · 조명 · 스피커. 꺼 둔 것은 산다.</summary>
    private void Emp(CosmicEvent e, float p)
    {
        var w = _w;
        var ship = w.Ship;
        FlashNow = MathF.Max(FlashNow, 0.5f * p);
        foreach (var f in ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached && f.Machine != null && Sensitive(f) && f.Type != FurnitureType.MainComputer).OrderBy(f => f.Id).ToList())
        {
            if (_safed.Contains(f.Id)) continue;
            var m = f.Machine!;
            if (m.Faults.Count > 0 || !R.Chance(0.55f * p)) continue;
            if (w.Machines.Break(m, FaultKind.ControlFault) != null) { e.EmpKills++; Stats.EmpKills++; MarkLog.Add(m.Marks, w.Tick, $"{e.Spec.Name} 펄스에 제어부가 탔다"); }
        }
        foreach (var bot in w.Robots.Robots.OrderBy(r => r.Id).ToList())
        {
            bool sheltered = bot.State == RobotState.Docked && _safed.Contains(bot.Dock.Id);
            if (!sheltered && R.Chance(0.45f * p) && w.Hazards.RobotHaywire(bot) != null) e.EmpKills++;
        }
        foreach (var d in w.Drones.Drones)
            if (d.State != DroneState.Docked && !d.Faulty && R.Chance(0.6f * p)) { d.Faulty = true; MarkLog.Add(d.Marks, w.Tick, $"{e.Spec.Name} 펄스에 고장"); e.EmpKills++; }
        foreach (var d in ship.Doors.Where(d => !d.Removed && !d.IsExternal && d.Powered && !d.MotorBroken).OrderBy(d => d.Id).ToList())
            if (R.Chance(0.1f * p)) w.Fixtures.BreakDoor(d, $"{e.Spec.Name} 펄스 — 구동기가 탔다");
        foreach (var r in ship.Rooms.Where(r => !r.Detached && !r.LightsOut).OrderBy(r => r.Id).ToList())
            if (R.Chance(0.2f * p)) w.Hazards.Lights(r);
        // 주 컴퓨터: 꺼 두지 않았으면 재부팅 (아주 세면 저장장치가 탄다) · 감지기 값이 멈춘다 · 스피커가 탄다
        var au = w.Automation;
        bool compSafe = au.Computer?.Body is Furniture cf && _safed.Contains(cf.Id);
        if (au.Present && !compSafe)
        {
            if (p >= 0.85f && R.Chance(0.35f * p)) w.Hazards.Computer();
            else au.Reboot($"{e.Spec.Name} 전자기 펄스", 8f + 30f * p);
            foreach (var r in ship.Rooms.Where(r => !r.Detached).OrderBy(r => r.Id).Where(_ => R.Chance(0.3f * p)).Take(4).ToList())
                au.Belief.Break(r, SensorFault.Stuck, $"{e.Spec.Name} 펄스");
            foreach (var r in ship.Rooms.Where(r => !r.Detached).OrderBy(r => r.Id).Where(_ => R.Chance(0.15f * p)).Take(3).ToList())
                au.Speak.BreakSpeaker(r, $"{e.Spec.Name} 펄스", 12f);
        }
        foreach (var m in ship.Machines) if (!_safed.Contains(m.Body.Id)) m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.2f * p);
    }

    /// <summary>거대한 것 하나가 부딪힌다: 비켰으면 스쳐 가고, 아니면 그 구획에 몇 번 (봉쇄했으면 옆으로 번지지 않는다).</summary>
    private void Strike(CosmicEvent e, float p, Vector2 center)
    {
        var w = _w;
        ShakeNow = MathF.Max(ShakeNow, 1f);
        if (e.Avoided)
        {
            w.History.Add(w, HistoryKind.Response, $"{e.Spec.Name} — 바로 곁을 스쳐 지나갔다 (항로를 바꾼 덕)", log: true);
            return;
        }
        if (e.TargetRoom < 0) return;
        var room = w.Ship.Rooms[e.TargetRoom];
        var cells = room.Cells.OrderByDescending(c => Vector2.Dot(c.Center - center, Dir(e))).ThenBy(c => c.X * 1000 + c.Y).ToList();
        if (cells.Count == 0) return;
        float[] sizes = { 1.6f * p, 1.0f * p, 0.7f * p };
        for (int k = 0; k < sizes.Length; k++)
        {
            var at = cells[Math.Min(cells.Count - 1, k * Math.Max(1, cells.Count / 4))];
            if (Incidents.Meteor(w, at, sizes[k], e.Known ? WarnLevel.Sensor : WarnLevel.None, e.HoursTo(e.KnownAt, e.Arrive) * 60f) != null) e.Hits++;
        }
        Stats.Strikes++;
        foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == room)) Memory.Frighten(w, c, room, 0.5f, $"{e.Spec.Name}이(가) 들이받았다");
        w.History.Add(w, HistoryKind.Damage, $"{e.Spec.Name}이(가) {Ko.EulReul(room.Name)} 들이받았다" + (e.Sealed ? " — 비우고 봉쇄해 둔 구획이라 다친 사람은 없다" : ""), room, log: true);
    }

    /// <summary>단계가 걸려 있는 동안 (시스템 틱마다).</summary>
    private void StageTick(CosmicEvent e, CosmicStage s, float p, float dt)
    {
        var w = _w;
        if (p <= 0f) return;
        var ship = w.Ship;
        var fx = s.Fx;
        if ((fx & (CosmicFx.Heat | CosmicFx.Cold)) != 0)
        {
            float sign = (fx & CosmicFx.Heat) != 0 ? 1f : -1f;
            foreach (var r in ship.Rooms)
            {
                if (r.Detached) continue;
                float ex = w.Ambience.Exposure(r);
                if (ex < 0.5f) continue;
                r.Air.Temperature += sign * 4f * p * ex * dt * (Insulated(r) ? 0.35f : 1f) * 8f / MathF.Max(4f, r.Volume);
                if (sign > 0f) foreach (var f in r.Furniture) if (f.Machine is Machine m) m.Heat = MathF.Min(1f, m.Heat + 0.04f * p * ex * dt);
            }
        }
        if ((fx & CosmicFx.Tidal) != 0)
        {
            foreach (var m in ship.Machines) m.Wear = MathF.Min(1f, m.Wear + 0.008f * p * dt);
            foreach (var c in w.Crew) if (!c.Dead) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f * p * dt); // 멀미
            ShakeNow = MathF.Max(ShakeNow, 0.25f * p);
        }
        if ((fx & CosmicFx.Quake) != 0)
        {
            ShakeNow = MathF.Max(ShakeNow, 0.45f * p);
            if (R.Chance(0.05f * p))
                foreach (var c in w.Crew.Where(c => !c.Dead && !c.Outside && c.Room != null && c.Pose is Pose.Walking or Pose.Working))
                    if (R.Chance(0.08f * (Stowed(c.Room!) ? 0.5f : 1f))) { NeedsSystem.AddInjury(c.Vitals, 0.03f, $"{e.Spec.Name} 진동에 넘어짐"); w.Body.Stats.Falls++; e.Falls++; }
        }
        if ((fx & CosmicFx.Nav) != 0) w.Voyage.Shift(-dt / 24f * 0.6f * p * (_pilot >= 0 ? 0.4f : 1f));
        if ((fx & CosmicFx.Plasma) != 0)
            foreach (var f in w.Exterior.All) if (R.Chance(0.02f)) w.Exterior.Damage(f, 0.05f * p * (_folded ? 0.3f : 1f), e.Spec.Name);
    }

    /// <summary>방마다 방사선 · 바깥 · 센서 배율 (Ambience · Sensors가 읽는다).</summary>
    private void Fields()
    {
        var w = _w;
        var rooms = w.Ship.Rooms;
        if (_rad.Length != rooms.Count) _rad = new float[rooms.Count];
        else Array.Clear(_rad);
        OutsideRad = 0f;
        float sensor = 1f;
        bool noEva = false;
        ShakeNow *= 0.8f;
        FlashNow *= 0.85f;
        Vector2? center = null;
        foreach (var e in Events)
        {
            if (e.Phase is CosmicPhase.Done or CosmicPhase.After) continue;
            if (e.Phase >= CosmicPhase.Brace && e.Known) noEva = true;
            if (e.Phase != CosmicPhase.Impact) continue;
            noEva = true;
            float rp = FxNow(e, CosmicFx.Radiation);
            if (rp > 0f)
            {
                center ??= ShipCenter();
                for (int i = 0; i < rooms.Count; i++)
                {
                    var r = rooms[i];
                    if (r.Detached) continue;
                    float v = 0.75f * rp * w.Ambience.Exposure(r) * (0.6f + 0.4f * Facing(r, e, center.Value)) * (1f - 0.6f * Water(r));
                    if (v > _rad[i]) _rad[i] = v;
                }
                OutsideRad = MathF.Max(OutsideRad, 0.9f * rp);
            }
            float bp = FxNow(e, CosmicFx.Blind);
            if (bp > 0f) sensor = MathF.Min(sensor, MathF.Max(0.08f, 0.6f - 0.55f * bp));
            if (FxNow(e, CosmicFx.Light) > 0f) sensor = MathF.Min(sensor, 0.3f);
        }
        SensorMul = sensor;
        NoEva = noEva;
    }

    private void Launches()
    {
        var w = _w;
        while (_launch.Count > 0 && _launch[0].tick <= w.Tick)
        {
            var (_, ev, size, hostile) = _launch[0];
            _launch.RemoveAt(0);
            var e = Events.FirstOrDefault(x => x.Id == ev);
            if (e == null || e.Avoided) continue;
            var center = ShipCenter();
            var hull = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor && w.Ambience.Exposure(r) > 0.5f).OrderByDescending(r => Facing(r, e, center) + R.Float() * 0.6f).ThenBy(r => r.Id).ToList();
            if (hull.Count == 0) continue;
            var room = hull[0];
            var target = room.Cells[R.Range(0, room.Cells.Count)];
            if (w.Sensors.Launch(target, size) != null) e.Hits++;
        }
    }

    /// <summary>삭은 회로: 며칠 뒤에야 고장이 드러난다.</summary>
    private void Latent()
    {
        var w = _w;
        while (_latent.Count > 0 && _latent[0].tick <= w.Tick)
        {
            var (_, fid, why) = _latent[0];
            _latent.RemoveAt(0);
            var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == fid);
            if (f?.Machine is not Machine m || f.Room.Detached || m.Faults.Count > 0) continue;
            if (w.Machines.Break(m) is Fault fault)
            {
                Stats.Latent++;
                MarkLog.Add(m.Marks, w.Tick, $"{why} 뒤로 삭은 회로 — {fault.Spec.Name}");
                w.Log.Add(w.Tick, LogKind.Warning, $"{m.Name} 고장 — {why} 때 삭은 회로가 이제야 드러났다");
            }
        }
    }

    /// <summary>한 시간마다: 두려움 · 서로 알리기 · 대피소에서 함께 · 작물 · 전자 장비 · 하늘을 보는 기분.</summary>
    private void Hourly()
    {
        var w = _w;
        var live = w.Crew.Where(c => !c.Dead).ToList();
        foreach (var e in Events)
        {
            if (e.Phase is CosmicPhase.Done) continue;
            if (e.Phase <= CosmicPhase.Brace && e.Known)
            {
                long harm = NextHarm(e);
                float hours = harm == long.MaxValue ? 99f : e.HoursTo(w.Tick, harm);
                float urg = Math.Clamp(1f - hours / 12f, 0f, 1f);
                foreach (var c in live)
                {
                    if (!Knows(c, e)) continue;
                    // 두려움: 다가올수록 (겁 많은 사람일수록) — 컴퓨터를 믿는 사람은 덜 (계획이 있다)
                    float trust = w.Automation.Trusts.Of(c);
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f * urg * (1f - c.Traits.Bravery) * (1.2f - 0.5f * trust));
                }
                // 같은 방에 있으면 말로 전해진다 (세계 ≠ 아는 것)
                foreach (var c in live)
                {
                    if (Knows(c, e) || c.Room == null) continue;
                    var teller = live.FirstOrDefault(o => o != c && o.Room == c.Room && o.IsAwake && Knows(o, e));
                    if (teller != null && c.IsAwake && R.Chance(0.6f)) Learn(c, e, KnowSource.Rumor, $"{teller.Name}에게 들었다");
                }
            }
            if (e.Phase == CosmicPhase.Impact)
            {
                // 함께 숨은 사람들: 좁은 방에서 가까워진다 · 너무 붐비면 짜증 · 물자를 옮겨 뒀으면 먹고 마신다
                foreach (var g in live.Where(c => !c.Outside && c.Room != null && Sheltered(c)).GroupBy(c => c.Room!.Id).OrderBy(g => g.Key))
                {
                    var list = g.OrderBy(c => c.Id).ToList();
                    var room = list[0].Room!;
                    int cap = Math.Max(2, room.Cells.Count / 3);
                    for (int a = 0; a < list.Count; a++)
                    {
                        var c = list[a];
                        if (Supplied(room)) { c.Needs.Food = MathF.Min(1f, c.Needs.Food + 0.05f); c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f); }
                        if (list.Count > cap) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.015f * (list.Count - cap));
                        c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.05f);
                        for (int b = a + 1; b < Math.Min(list.Count, 7); b++) { c.ChangeAffinity(list[b], 0.012f); list[b].ChangeAffinity(c, 0.012f); }
                    }
                }
                float rp = FxNow(e, CosmicFx.Radiation);
                if (rp > 0f)
                {
                    foreach (var c in live)
                    {
                        var (sh, tot) = e.ShelterLog.TryGetValue(c.Id, out var v) ? v : (0, 0);
                        e.ShelterLog[c.Id] = (sh + (Sheltered(c) ? 1 : 0), tot + 1);
                    }
                    // 전자 장비가 튄다 (꺼 둔 것은 빼고) · 작물이 탄다
                    var pool = w.Ship.Machines.Where(m => Sensitive(m.Body) && m.Faults.Count == 0 && !_safed.Contains(m.Body.Id)).OrderBy(m => m.Body.Id).ToList();
                    if (pool.Count > 0 && R.Chance(0.3f * rp))
                    {
                        var gm = pool[R.Range(0, pool.Count)];
                        if (w.Machines.Break(gm) is Fault) { e.Glitches++; MarkLog.Add(gm.Marks, w.Tick, $"{e.Spec.Name} 방사선에 튀었다"); }
                    }
                    foreach (var f in w.Ship.FurnitureOf(FurnitureType.GrowBed))
                    {
                        float r = Radiation(f.Room) * ((RoomCatalog.Tags(f.Room.Kind) & RoomTag.Shielded) != 0 ? 0.15f : 1f);
                        if (r < 0.2f || f.Machine?.Crop is not CropState crop) continue;
                        crop.Growth = MathF.Max(0f, crop.Growth - 0.06f * r);
                        crop.Care = MathF.Max(0.2f, crop.Care - 0.05f * r);
                        if (R.Chance(0.2f)) MarkLog.Add(f.Machine.Marks, w.Tick, $"{e.Spec.Name} 방사선에 잎이 탔다");
                    }
                }
            }
        }
        // 하늘에 남은 것을 창으로 보면 마음이 놓인다 (그날을 겪은 사람은 조금 아리다)
        if (Sky.Count > 0)
        {
            foreach (var c in live)
            {
                if (c.Outside || c.Room is not Room r || !c.IsAwake || w.Body.WindowsOf(r) == 0 || _shut.Contains(r.Id)) continue;
                bool was = _went.Contains(c.Id);
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - (was ? 0.004f : 0.012f) * Math.Min(3, Sky.Count));
            }
        }
    }

    private void Aftermath(CosmicEvent e)
    {
        var w = _w;
        e.Phase = CosmicPhase.After;
        e.PhaseSince = w.Tick;
        e.AfterUntil = w.Tick + SimTime.Hours(24f * R.Range(10f, 20f));
        var live = w.Crew.Where(c => !c.Dead).ToList();
        // 대피소 vs 바깥 쪽: 실제로 얼마나 더 쬐었나
        foreach (var (id, dose, sh0) in e.Snap)
        {
            if (w.Crew.FirstOrDefault(c => c.Id == id) is not CrewMember c) continue;
            bool sh = e.ShelterLog.TryGetValue(id, out var lg) && lg.tot > 0 ? lg.sh * 2 >= lg.tot : sh0;
            float gain = MathF.Max(0f, c.Dose - dose);
            if (sh) { e.GainSheltered += gain; e.NSheltered++; } else { e.GainExposed += gain; e.NExposed++; }
            if (c.Dose > 1f) e.Sick++;
        }
        if (e.NSheltered > 0) e.GainSheltered /= e.NSheltered;
        if (e.NExposed > 0) e.GainExposed /= e.NExposed;
        // 하늘이 바뀐다
        if (e.Spec.Remnant != CosmicRemnant.None && !e.Ghost)
            Sky.Add(new SkyMark(e.Id, e.Kind, e.Spec.Remnant, w.Tick, e.Side, R.Range(0.25f, 0.45f), e.Close ? R.Range(0.9f, 1.3f) : R.Range(0.6f, 0.9f), $"{e.Spec.Name}의 {CosmicCatalog.RemnantName(e.Spec.Remnant)}"));
        // 예보 채점 → 사람마다 컴퓨터를 믿는 정도
        float off = MathF.Abs(e.HoursTo(e.Predicted, e.Arrive));
        bool right = e.KnownBy != "몸으로" && off <= e.ErrorHours * 1.2f + 0.5f;
        e.Grade = e.KnownBy == "몸으로" ? "예보 없음" : right ? $"예보가 맞았다 ({off:0.#}시간 차이)" : $"예보가 빗나갔다 ({off:0.#}시간)";
        Learned++;
        if (e.KnownBy != "몸으로")
            foreach (var c in live.Where(c => Knows(c, e)))
                w.Automation.Trusts.Change(c, right ? 0.05f : -0.06f, $"{e.Spec.Name} 예보가 {(right ? "맞았다" : "빗나갔다")}", quiet: true);
        // 기억: 그날 · 쬔 만큼 남는 긴장 · 함께 숨은 사람
        foreach (var c in live)
        {
            MarkLog.Add(c.Memory.Marks, w.Tick, $"그날 — {e.Spec.Name}");
            float gain = e.Snap.FirstOrDefault(s => s.id == c.Id) is var sn && sn.id == c.Id ? MathF.Max(0f, c.Dose - sn.dose) : 0f;
            if (gain > 0.4f) Memory.Shake(w, c, MathF.Min(0.12f, 0.05f * gain), $"{e.Spec.Name} 방사선을 쬐었다");
            else Memory.Steady(w, c, 0.04f);
        }
        // 삭은 회로: 며칠 뒤 드러난다
        if (e.Spec.Has(CosmicFx.Radiation) || e.Spec.Has(CosmicFx.Emp))
        {
            var pool = w.Ship.Furniture.Where(f => f.Machine != null && !f.Room.Detached && Sensitive(f) && !_safed.Contains(f.Id)).OrderBy(f => f.Id).ToList();
            int n = Math.Min(pool.Count, 1 + R.Range(0, 3));
            for (int i = 0; i < n; i++) _latent.Add((w.Tick + SimTime.Hours(24f * R.Range(1f, 8f)), pool[R.Range(0, pool.Count)].Id, e.Spec.Name));
            _latent.Sort((a, b) => a.tick.CompareTo(b.tick));
        }
        // 꺼 둔 것을 다시 켤 일
        foreach (var t in e.Tasks.Where(t => t.Kind == BraceKind.PowerDown && t.Done).ToList())
            if (w.Ship.Furniture.FirstOrDefault(f => f.Id == t.FurnitureId) is Furniture f)
                AddTask(e, BraceKind.Restart, f.Room.Id, f.Id, 0.08f, $"{f.Label} 다시 켜기");
        if (e.Sealed && e.Hits == 0) Unseal(e, "부딪히지 않았다");
        // 관행: 숨은 사람이 훨씬 덜 쬐었으면 예보 훈련 · 넘어지고 깨진 게 많으면 묶어 두는 습관
        if (e.NExposed > 0 && e.NSheltered > 0 && e.GainExposed > e.GainSheltered * 2f + 0.2f && CustomOf(CosmicCustomKind.Drill) == null)
        {
            var founder = live.Where(c => e.Snap.Any(s => s.id == c.Id && s.sheltered)).OrderByDescending(c => c.Traits.Bravery).ThenBy(c => c.Id).FirstOrDefault();
            Found(CosmicCustomKind.Drill, e, founder, $"{e.Spec.Name} 때 숨은 사람은 {e.GainSheltered:0.0}Sv, 바깥 쪽은 {e.GainExposed:0.0}Sv", live.Where(c => Knows(c, e)));
        }
        if (e.Falls + e.Broken >= 3 && CustomOf(CosmicCustomKind.Stow) == null)
        {
            var founder = live.OrderByDescending(c => c.Traits.Diligence).ThenBy(c => c.Id).FirstOrDefault();
            Found(CosmicCustomKind.Stow, e, founder, $"{e.Spec.Name} 충격에 {e.Falls}명이 넘어지고 {e.Broken}개가 깨졌다", live.Where(c => c.Room != null && !Sheltered(c)));
        }
        string tally = $"맞은 것 {e.Hits} · 펄스로 탄 것 {e.EmpKills} · 튄 것 {e.Glitches} · 넘어짐 {e.Falls} · 깨짐 {e.Broken} · 불 {e.Fires}"
                       + (e.NSheltered + e.NExposed > 0 ? $" · 피폭 대피소 {e.GainSheltered:0.00} / 바깥 쪽 {e.GainExposed:0.00}Sv" : "") + (e.Avoided ? " · 항로를 바꿔 비켰다" : "");
        w.History.Add(w, HistoryKind.Recovery, $"{e.Spec.Name}이(가) 지나갔다 — {tally} · {e.Grade}", log: true);
        w.Automation.Book.Add(ActKind.Advice, null, $"{e.Spec.Name} 지나감", e.Grade, "피해를 세고 다시 켤 것을 알린다", e.Tasks.Any(t => t.Kind == BraceKind.Restart) ? "꺼 둔 장비를 다시 켜 달라" : "", "cosmic:after:" + e.Id, 0, 10f);
        Broadcast(e, $"{e.Spec.Name}이 지나갔다. {e.Grade}", 1);
    }

    private void Close(CosmicEvent e)
    {
        var w = _w;
        e.Phase = CosmicPhase.Done;
        e.PhaseSince = w.Tick;
        w.History.Add(w, HistoryKind.Milestone, $"{e.Spec.Name}의 날이 연대기에 남았다" + (Sky.Any(s => s.EventId == e.Id) ? $" — 창밖엔 {Sky.First(s => s.EventId == e.Id).Name}" : ""), log: true);
    }

    // ───────────────────────────── 관행 ─────────────────────────────

    private void Found(CosmicCustomKind k, CosmicEvent e, CrewMember? founder, string origin, IEnumerable<CrewMember> followers)
    {
        var w = _w;
        var cu = new CosmicCustom { Kind = k, Born = w.Tick, Origin = origin, Founder = founder?.Name, EventId = e.Id, NextDay = (w.Tick / SimTime.TicksPerDay + 7) * SimTime.TicksPerDay };
        if (founder != null) cu.Followers.Add(founder.Id);
        foreach (var c in followers.OrderBy(c => c.Id)) cu.Followers.Add(c.Id);
        Customs.Add(cu);
        w.History.Add(w, HistoryKind.Lesson, $"관행이 생겼다 — {CosmicCustom.Name(k)}: {origin}" + (founder != null ? $" ({founder.Name}이(가) 시작)" : ""), log: true);
        if (founder != null) Life.Diary(w, founder, Persona.Say(founder, $"{CosmicCustom.Name(k)} — 다시는 그날처럼 되지 않게"));
    }

    /// <summary>그날의 밤: 날이 오면 저녁에 창가로 — 따르는 사람이 곁에 있으면 따라 한다.</summary>
    private void Customary()
    {
        var w = _w;
        foreach (var cu in Customs)
        {
            if (cu.Kind != CosmicCustomKind.Vigil) continue;
            if (w.Tick >= cu.NextDay + SimTime.TicksPerDay) { cu.NextDay += SimTime.Hours(24f * 10f); cu.Kept.Clear(); }
        }
        // 사건이 지나고 일주일: 회의가 없었으면 겪은 사람 하나가 시작한다
        foreach (var e in Events)
        {
            if (e.Phase < CosmicPhase.After || e.VigilAsked || w.Tick < e.PhaseSince + SimTime.Hours(24f * 7f)) continue;
            if (Sky.All(s => s.EventId != e.Id) && e.Sick == 0) { e.VigilAsked = true; continue; }
            e.VigilAsked = true;
            if (CustomOf(CosmicCustomKind.Vigil) != null) continue;
            var live = w.Crew.Where(c => !c.Dead && Knows(c, e)).ToList();
            var founder = live.OrderByDescending(c => c.Memory.Trauma).ThenByDescending(c => c.Dose).ThenBy(c => c.Id).FirstOrDefault();
            Found(CosmicCustomKind.Vigil, e, founder, $"{e.Spec.Name}의 날을 잊지 말자", live.Where(c => c.Dose > 0.3f || c.Memory.Trauma > 0.05f).Take(3));
        }
    }

    public bool IsVigilDay => CustomOf(CosmicCustomKind.Vigil) is CosmicCustom cu && _w.Tick >= cu.NextDay && _w.Tick < cu.NextDay + SimTime.TicksPerDay;

    internal void KeepVigil(CrewMember c)
    {
        if (CustomOf(CosmicCustomKind.Vigil) is not CosmicCustom cu) return;
        if (cu.Kept.Add(c.Id) && cu.Kept.Count == 1) cu.Times++;
        Stats.Vigils++;
        // 곁에 있던 사람이 따라 한다
        foreach (var o in _w.Crew.Where(o => !o.Dead && o != c && o.Room == c.Room && !cu.Followers.Contains(o.Id)).OrderBy(o => o.Id).ToList())
            if (R.Chance(0.5f)) { cu.Followers.Add(o.Id); MarkLog.Add(o.Memory.Marks, _w.Tick, $"{Ko.WaGwa(c.Name)} 함께 그날의 밤을 지켰다"); }
    }

    // ───────────────────────────── 회의 훅 ─────────────────────────────

    /// <summary>정기 회의 안건: 다가오는 대재난의 대비 계획 · 지나간 날을 기리는 관행.</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> voters)
    {
        var w = _w;
        if (voters.Count < 2) return;
        foreach (var e in Events)
        {
            if (e.Known && !e.PlanVoted && e.Phase <= CosmicPhase.Brace && !e.Ghost || e.Known && e.Ghost && !e.PlanVoted && e.Phase <= CosmicPhase.Brace)
            {
                e.PlanVoted = true;
                var item = new AgendaItem { Title = $"대비 계획 — {e.Spec.Name} ({e.HoursTo(w.Tick, e.Predicted):0}시간 뒤 · 신뢰도 {e.Confidence * 100:0}%)", Topic = "cosmic:plan", Evidence = e.Spec.Detect };
                if (ComputerUp) { item.Computer = $"예보 신뢰도 {e.Confidence * 100:0}% · 오차 ±{e.ErrorHours:0.#}시간 — 일찍 넉넉히 대비하길 권한다"; item.ComputerSign = 1; }
                var yes = new List<CrewMember>();
                var no = new List<CrewMember>();
                foreach (var c in voters)
                {
                    float trust = w.Automation.Trusts.Of(c);
                    float s = 0.6f * trust + 0.25f * (1f - c.Traits.Bravery) + c.Value switch { CrewValue.Safety => 0.2f, CrewValue.People => 0.1f, CrewValue.Efficiency => -0.15f, CrewValue.Freedom => -0.1f, _ => 0f };
                    if (Follows(c, CosmicCustomKind.Drill)) s += 0.25f;
                    bool v = s >= 0.5f;
                    (v ? yes : no).Add(c);
                    string why = v ? (trust >= 0.6f ? "컴퓨터 예보를 믿는다" : "겁이 난다 — 넉넉히") : (trust < 0.45f ? "예보를 다 믿진 못한다" : "일손이 아깝다");
                    item.Votes.Add((c.Id, v, why));
                    if (item.Speeches.Count < 3) item.Speeches.Add(new Speech { Who = c.Id, For = v, Text = Persona.Say(c, why) });
                }
                item.Yes = yes.Count;
                item.No = no.Count;
                item.Passed = yes.Count > no.Count;
                e.EarlyBrace = item.Passed;
                item.Outcome = item.Passed ? "일찍 넉넉히 대비한다 (물벽 둘 · 대비 시작을 당긴다)" : "예보가 가까워지면 그때 대비한다";
                rec.Items.Add(item);
                Stats.Votes++;
                w.Meetings.Record(item.Title, "cosmic:plan", -1, null, yes, no, e.Spec.Name);
                w.Meetings.Split(yes, no);
            }
            else if (e.Phase == CosmicPhase.After && !e.VigilAsked && CustomOf(CosmicCustomKind.Vigil) == null && (Sky.Any(s => s.EventId == e.Id) || e.Sick > 0))
            {
                e.VigilAsked = true;
                var item = new AgendaItem { Title = $"그날을 기리자 — {e.Spec.Name}", Topic = "cosmic:vigil" };
                var yes = voters.Where(c => c.Memory.Trauma > 0.03f || c.Value is CrewValue.People or CrewValue.Rules || c.Dose > 0.3f).ToList();
                var no = voters.Except(yes).ToList();
                foreach (var c in voters) item.Votes.Add((c.Id, yes.Contains(c), yes.Contains(c) ? "그날을 잊으면 안 된다" : "지난 일이다"));
                item.Yes = yes.Count; item.No = no.Count; item.Passed = yes.Count >= no.Count;
                item.Outcome = item.Passed ? "열흘마다 저녁에 창가에 모인다" : "각자 기억한다";
                rec.Items.Add(item);
                Stats.Votes++;
                if (item.Passed) Found(CosmicCustomKind.Vigil, e, yes.OrderByDescending(c => c.Memory.Trauma).ThenBy(c => c.Id).FirstOrDefault(), $"{e.Spec.Name}의 날을 기리자 (회의)", yes);
            }
        }
    }

    // ───────────────────────────── 서로 알리기 ─────────────────────────────

    /// <summary>시험: 이 사람은 모른다고 되돌린다.</summary>
    internal void Forget(CrewMember c, CosmicEvent e) => _know.Remove(KK(c.Id, e.Id));

    internal bool WarnClaimed(CrewMember target, CrewMember me) => _warnClaim.TryGetValue(target.Id, out var by) && by != me.Id;
    internal void ClaimWarn(CrewMember target, CrewMember by) => _warnClaim[target.Id] = by.Id;
    internal void ReleaseWarn(CrewMember target) => _warnClaim.Remove(target.Id);

    /// <summary>알려 줬다 (자는 사람은 깨운다): 알게 되고, 둘이 가까워진다.</summary>
    internal void Tell(CrewMember who, CosmicEvent e, CrewMember by, bool wake)
    {
        var w = _w;
        Learn(who, e, KnowSource.Rumor, $"{by.Name}이(가) 알려 줬다");
        if (wake || who.Pose == Pose.Sleeping) { who.Jolt(w); who.Interrupt(w); Stats.Woken++; }
        Stats.Warned++;
        who.ChangeAffinity(by, 0.05f);
        by.ChangeAffinity(who, 0.03f);
        MarkLog.Add(who.Memory.Marks, w.Tick, wake ? $"{Ko.IGa(by.Name)} 깨워 줬다 — {e.Spec.Name}" : $"{Ko.IGa(by.Name)} {e.Spec.Name}을(를) 알려 줬다");
        w.Log.Add(w.Tick, LogKind.Life, wake ? $"{Ko.EulReul(who.Name)} 깨워 {e.Spec.Name}을(를) 알렸다" : $"{who.Name}에게 {e.Spec.Name}을(를) 알렸다", by.Id);
    }

    /// <summary>창밖으로 봤다 — 본 사람은 안다 (방송을 못 들었어도).</summary>
    internal void SawIt(CrewMember c, CosmicEvent e) => Learn(c, e, KnowSource.Seen, "창밖으로 봤다");

    /// <summary>Power: 꺼 둔 설비는 전기를 받지 않는다.</summary>
    public void Park()
    {
        if (_safed.Count == 0) return;
        foreach (var f in _w.Ship.Furniture)
            if (f.Machine != null && _safed.Contains(f.Id)) f.Machine.Parked = true;
    }

    /// <summary>지문.</summary>
    public void Hash(Action<long> I, Action<float> F)
    {
        I(Events.Count); I(Sky.Count); I(Customs.Count); I(Learned); I(_know.Count); I(_looked.Count); I(_safed.Count); I(_launch.Count); I(_latent.Count);
        foreach (var e in Events) { I((int)e.Kind); I((int)e.Phase); I(e.Predicted % 1000003); F(e.Confidence); I(e.Tasks.Count(t => t.Done)); I(e.Hits + e.EmpKills * 7 + e.Falls * 13); I(e.Avoided ? 1 : 0); I(e.Sealed ? 1 : 0); }
        foreach (var cu in Customs) { I((int)cu.Kind); I(cu.Followers.Count); I(cu.Times); }
    }
}
