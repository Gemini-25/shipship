using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.12 재료 탐사 원정.
// 재료가 바닥나면(수리재 · 구조재 · 부품 · 연료 · 물/얼음 · 식량) 배가 멈춘다 — 엔진을 끄고 일정이 밀린다.
// 주 컴퓨터는 소모 속도로 바닥날 날을 예측해 미리 알리고, 센서가 찾은 목적지를 평가해 권하고, 위험을 경고한다 (믿는 만큼 먹힌다).
// 승무원은 스스로 알아채고 꺼내고 · 자원하고 · 반대한다 (목표 · 감정 · 관계). 회의 · 함장 · 관찰자(방침)가 정한다.
// 원정대(1~4명)는 장비를 재고에서 챙겨 에어락/셔틀 격납고로 걸어 나가 배에서 사라진다 — "60초!"처럼 원정 자체는 보여 주지 않고
// 일지 몇 줄 · 띄엄띄엄 무전(들은 사람만) · 남은 사람의 걱정과 일손 부족 · 귀환(재료 · 부상 · 우주복 손상 · 잃은 장비 · 실종 · 발견)
// · 함께 고생한 사이 · 식탁 이야기 · 추모 · 연대기 · 칭호로 앞뒤를 촘촘하게 보여 준다.

public enum TripPhase { Gathering, Away, Back, Done }

public sealed class TripMember
{
    public int Id { get; init; }
    public string Job { get; set; } = "";
    public bool Volunteer { get; init; }
    public bool Boarded { get; set; }
    public bool Missing { get; set; }
    public string? MissingWhy { get; set; }
    public bool Dead { get; set; }
    /// <summary>우주복 손상 0~1 (v16.11 우주복 단계와 합칠 때 잇는다).</summary>
    public float SuitDamage { get; set; }
    public float Hurt { get; set; }
    public List<string> HurtWhy { get; } = new();
    public int CoveredBy { get; set; } = -1;
    public static string Stage(float d) => d < 0.05f ? "멀쩡" : d < 0.2f ? "긁힘" : d < 0.45f ? "찢김" : d < 0.7f ? "미세 누출" : "큰 파공";
}

public sealed record JournalLine(long Tick, int Day, string Text, int Tone);

public sealed class ExpeditionRadioCall
{
    public long Tick { get; init; }
    public int Day { get; init; }
    public string Text { get; init; } = "";
    public bool Lost { get; init; }
    public List<int> HeardBy { get; } = new();
    public List<int> Rumor { get; } = new();
}

public sealed class Trip
{
    public int Id { get; init; }
    public Site Site { get; init; } = null!;
    public List<TripMember> Members { get; } = new();
    public int Leader { get; set; } = -1;
    public Dictionary<string, int> Gear { get; } = new();
    public Dictionary<string, int> Used { get; } = new();
    public List<string> Lost { get; } = new();
    public int DroneId { get; set; } = -1;
    public ItemKind ToolKind { get; set; } = ItemKind.Clamp;
    public bool Shuttle { get; set; }
    public bool DroneLost { get; set; }
    public long Called { get; init; }
    public long Deadline { get; set; }
    public long Departed { get; set; } = -1;
    public long ReturnAt { get; set; } = -1;
    public long Planned { get; set; }
    public long Returned { get; set; } = -1;
    public TripPhase Phase { get; set; }
    public List<JournalLine> Journal { get; } = new();
    public List<ExpeditionRadioCall> Radio { get; } = new();
    public Dictionary<ItemKind, int> Loot { get; } = new();
    public List<ItemKind> UsedParts { get; } = new();
    public int Survivors { get; set; }
    public List<int> Joined { get; } = new();
    public int Infos { get; set; }
    public string? Relic { get; set; }
    public bool ShuttleFound { get; set; }
    public float ShuttleDamage { get; set; }
    public bool Remains { get; set; }
    public bool PirateSeen { get; set; }
    public long RadioBlackout { get; set; } = -1;
    public float Dusty { get; set; }
    public float Hardship { get; set; }
    public List<int> Sick { get; } = new();
    public List<(int saver, int saved)> Saves { get; } = new();
    public string Source { get; init; } = "";
    public string Why { get; init; } = "";
    public int RoomId { get; set; } = -1;
    public Cell At { get; set; }
    public bool StoryTold { get; set; }
    public string Story { get; set; } = "";
    public int Listeners { get; set; }
    public long NextEvent { get; set; }
    public long NextRadio { get; set; }
    public int Day { get; set; }
    public float CompEstimate { get; set; } = -1f;
    public string CompWarn { get; set; } = "";
    public bool StormWarned { get; set; }
    public int OpenAtStart { get; set; }
    /// <summary>배에 있던 우주복 (부를 때 · 떠난 뒤) — 빈 걸이.</summary>
    public int SuitsAtCall { get; set; }
    public int SuitsLeft { get; set; } = -1;
    public bool Arrived { get; set; }
    public bool Headed { get; set; }
    public int LootTotal => Loot.Values.Sum();
    public IEnumerable<TripMember> Out => Members.Where(m => m.Boarded);
    public float RiskNow(World w) => Math.Clamp(Site.TrueRisk * (w.Ambience.StormPower > 0.3f && !StormWarned ? 1.5f : 1f) * (Gear.GetValueOrDefault("구급 키트") > 0 ? 0.9f : 1.1f) * TechWeb.Mul(w, "exp.risk"), 0f, 1f); // v16.14 원정 지도 공유
}

public sealed class ExpProposal
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public Site Site { get; set; } = null!;
    public List<int> Team { get; set; } = new();
    public string Source { get; init; } = "";
    public int ProposerId { get; init; } = -1;
    public string Why { get; init; } = "";
    public MatCat? For { get; init; }
    public bool Halted { get; set; }
    public List<(int who, string why)> Volunteers { get; } = new();
    public List<(int who, string why)> Objectors { get; } = new();
    public string? Computer { get; set; }
    public int ComputerSign { get; set; }
    public int? ComputerPick { get; set; }
    public string State { get; set; } = "기다림";
    public string DecidedBy { get; set; } = "";
    public string DecideWhy { get; set; } = "";
    public long DecidedAt { get; set; } = -1;
    public int Yes { get; set; }
    public int No { get; set; }
    public bool Open => State == "기다림";
}

public sealed class ExpStats
{
    public int Halts, Resumed, Proposals, Approved, Rejected, Trips, Returned, Injuries, Missing, Deaths, Survivors, Relics, Infos, Stories, RadioCalls, RadioHeard,
        RadioLost, Volunteers, Objections, Forecasts, Warnings, Covered, Hauled, Prepped, Greeted, RadioWaits, Worries, PirateNudges;
    public float DelayDays;
    public int LootTotal;
}

public sealed class ExpeditionSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7877 + 4099));

    public ExpStats Stats { get; } = new();
    public bool Halted { get; private set; }
    public string HaltWhy { get; private set; } = "";
    public MatCat? HaltCat { get; private set; }
    public long HaltedSince { get; private set; } = -1;
    public string HaltBy { get; private set; } = "";
    private readonly long[] _bottomSince = { -1, -1, -1, -1, -1, -1 };
    public List<Site> Sites { get; } = new();
    private int _nextSite = 1, _nextTrip = 1, _nextProp = 1;
    private long _nextHour, _nextScan, _nextPropose, _seenOutside;
    public ExpProposal? Pending { get; private set; }
    public List<ExpProposal> Proposals { get; } = new();
    public Trip? Current { get; private set; }
    public Trip? StoryTrip { get; private set; }
    public List<Trip> Past { get; } = new();
    public List<(ItemKind kind, int count)> Spoils { get; } = new();
    public int SpoilsRoom { get; private set; } = -1;
    public Cell SpoilsAt { get; private set; }
    public bool ShuttleOwned { get; set; }
    public float ShuttleWear { get; private set; }
    public Dictionary<int, long> LastNews { get; } = new();
    public Dictionary<int, float> Worry { get; } = new();
    private readonly Dictionary<int, int> _went = new(), _led = new(), _saved = new();
    private readonly HashSet<int> _prepped = new();
    private readonly List<(long at, int trip, int crew)> _late = new();
    private int _pendingInfos;
    // 주 컴퓨터 예측
    public float[] Rate { get; } = new float[6];
    private readonly float[] _lastStock = new float[6];
    private long _lastSample = -1;
    private readonly long[] _forecastAt = { -1, -1, -1, -1, -1, -1 };
    public List<(long tick, MatCat cat, float days, string text)> Forecasts { get; } = new();
    public List<(long tick, string text)> Warnings { get; } = new();

    public ExpeditionSystem(World w) => _w = w;

    public const float ShuttleCost = 14f;
    public int Went(CrewMember c) => _went.GetValueOrDefault(c.Id);
    public int Led(CrewMember c) => _led.GetValueOrDefault(c.Id);
    public int Saved(CrewMember c) => _saved.GetValueOrDefault(c.Id);
    public bool Away(CrewMember c) => c.Away;
    public bool HasShuttle => ShuttleOwned || _w.Ship.RoomsOf(RoomType.ShuttleBay).Any(r => !r.Abandoned) || _w.Ship.RoomsOf(RoomType.DockingBay).Any(r => !r.Abandoned);
    public bool ShuttleOut => Current is { Shuttle: true, Phase: TripPhase.Away };

    /// <summary>멈춘 배는 회피가 늦다 — 식은 엔진을 다시 데우는 시간 (분).</summary>
    public float ColdStartMinutes => Halted ? 1.2f : 0f;

    /// <summary>바깥 사건 무게: 멈춘 배는 해적의 눈에 띈다.</summary>
    public float OutsideMul(string group) => Halted && group == "해적" ? 2.5f : 1f;

    // ═══════════════════════════════ 재고 ═══════════════════════════════

    private int Count(ItemKind k) => _w.Ship.CountStored(k);
    private int Alive => _w.Crew.Count(c => !c.Dead);

    public float Stock(MatCat cat)
    {
        var w = _w;
        switch (cat)
        {
            case MatCat.Repair: return Count(ItemKind.Plate) + Count(ItemKind.Cable) + Count(ItemKind.Sealant) + Count(ItemKind.Fuse) + Count(ItemKind.Electronics);
            case MatCat.Structure: return Count(ItemKind.Structure);
            case MatCat.Parts:
            {
                int n = 0;
                foreach (var k in ItemKinds.All) if (ItemKinds.Tier(k) is ItemTier.General or ItemTier.Advanced) n += Count(k);
                return n;
            }
            case MatCat.Fuel: return w.Propulsion.Capacity <= 0f ? 100f : w.Propulsion.Propellant / w.Propulsion.Capacity * 100f;
            case MatCat.Water: return w.Water.Level + Count(ItemKind.Ice) * Recipes.IceWater;
            default: return Count(ItemKind.Meal) + Count(ItemKind.Ration) + Count(ItemKind.Produce);
        }
    }

    /// <summary>바닥 (배가 멈출 만큼).</summary>
    public bool Bottom(MatCat cat)
    {
        float s = Stock(cat);
        return cat switch
        {
            MatCat.Repair => s <= 0f,
            MatCat.Structure => s <= 0f && Count(ItemKind.Plate) < 2 && Count(ItemKind.MetalOre) < 3,
            MatCat.Parts => s <= 0f,
            MatCat.Fuel => s < 12f,
            MatCat.Water => _w.Water.Level < 25f && Count(ItemKind.Ice) == 0,
            _ => s < Math.Max(1, Alive),
        };
    }

    /// <summary>모자라다 (원정을 꺼낼 만큼).</summary>
    public bool Low(MatCat cat)
    {
        float s = Stock(cat);
        return cat switch
        {
            MatCat.Repair => s < 4f,
            MatCat.Structure => s < 1f,
            MatCat.Parts => s < 2f,
            MatCat.Fuel => s < 30f,
            MatCat.Water => _w.Water.Level < 110f && Count(ItemKind.Ice) < 2,
            _ => s < 3 * Math.Max(1, Alive),
        };
    }

    public static readonly MatCat[] Cats = { MatCat.Repair, MatCat.Structure, MatCat.Parts, MatCat.Fuel, MatCat.Water, MatCat.Food };

    // ═══════════════════════════════ 틱 ═══════════════════════════════

    public void Update(float dt)
    {
        var w = _w;
        if (Current is Trip t)
        {
            foreach (var m in t.Members)
                if (m.Boarded && t.Phase is TripPhase.Gathering or TripPhase.Away && w.Crew[m.Id] is { Away: true } c) Park(c);
            Step(t);
        }
        if (_late.Count > 0) LateReturns();
        if (w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.TicksPerHour;
        Hourly();
    }

    private void Hourly()
    {
        var w = _w;
        if (w.Tick < SimTime.TicksPerDay / 2) { Sample(); return; }
        Sample();
        CheckHalt();
        if (w.Tick >= _nextScan) Scan();
        Forecast();
        if (Pending == null && Current == null) Propose();
        if (Pending != null) DecideOutside();
        if (Current is { Phase: TripPhase.Gathering or TripPhase.Away }) { Worries(); Shortfall(); }
        if (Halted)
        {
            Stats.DelayDays += 1f / 24f;
            if (HaltCat == MatCat.Fuel && Count(ItemKind.Ice) > 0 && Life.Take(w, ItemKind.Ice, 1))
                w.Propulsion.Propellant = MathF.Min(w.Propulsion.Capacity, w.Propulsion.Propellant + 5f); // 얼음을 녹여 추진제로 (전기분해)
        }
    }

    /// <summary>소모 속도 (주 컴퓨터가 쓴다): 여섯 시간마다 재고를 재고, 준 만큼을 하루 소모로 부드럽게.</summary>
    private void Sample()
    {
        var w = _w;
        if (_lastSample >= 0 && w.Tick - _lastSample < SimTime.Hours(6)) return;
        for (int i = 0; i < 6; i++)
        {
            float s = Stock(Cats[i]);
            if (_lastSample >= 0)
            {
                float perDay = MathF.Max(0f, _lastStock[i] - s) * (SimTime.TicksPerDay / (float)(w.Tick - _lastSample));
                Rate[i] = Rate[i] <= 0f ? perDay : Rate[i] * 0.7f + perDay * 0.3f;
            }
            _lastStock[i] = s;
        }
        _lastSample = w.Tick;
    }

    public float DaysLeft(MatCat cat)
    {
        float r = Rate[(int)cat];
        float s = Stock(cat) - (cat == MatCat.Fuel ? 12f : cat == MatCat.Water ? 25f : cat == MatCat.Food ? Math.Max(1, Alive) : 0f);
        return r <= 0.01f ? 99f : MathF.Max(0f, s) / r;
    }

    // ═══════════════════════════════ 정지 · 재개 ═══════════════════════════════

    private bool CanHalt => _w.Voyage.Current.Kind != LegKind.Port && !_w.Voyage.Drifting;

    private void CheckHalt()
    {
        var w = _w;
        MatCat? worst = null;
        foreach (var cat in Cats)
        {
            int i = (int)cat;
            if (Bottom(cat)) { if (_bottomSince[i] < 0) _bottomSince[i] = w.Tick; if (w.Tick - _bottomSince[i] >= SimTime.Hours(6)) worst ??= cat; }
            else _bottomSince[i] = -1;
        }
        bool anyBottom = Cats.Any(Bottom);
        if (Halted && HaltCat == null && (worst ?? Cats.Where(Bottom).Cast<MatCat?>().FirstOrDefault()) is MatCat cw) // 기다리는 사이 바닥났다 — 이미 멈춘 배라 기다리지 않고 까닭을 바꾼다
        {
            HaltCat = cw;
            HaltWhy = $"{Ko.IGa(ExpeditionSites.CatName(cw))} 바닥났다 ({StockText(cw)}) · 원정대를 기다린다";
            w.History.Add(w, HistoryKind.Decision, $"엔진을 끈 채로 — {HaltWhy} · 원정대가 가져올 것에 배가 걸렸다", null, null, log: true);
        }
        if (!Halted && worst is MatCat c && CanHalt) Halt(c);
        else if (Halted && !anyBottom && Current == null) Resume("재료가 다시 찼다");
        else if (Halted && Current == null && Pending == null && Spoils.Count == 0 && w.Tick - HaltedSince > SimTime.TicksPerDay * 4 && w.Policies["expedition"] != 1)
            Resume("나흘을 기다려도 길이 없다 — 재료 없이 다시 간다"); // 멈춘 채 말라 죽을 수는 없다
    }

    private void Halt(MatCat cat)
    {
        var w = _w;
        Halted = true;
        HaltCat = cat;
        HaltedSince = w.Tick;
        Stats.Halts++;
        var cap = w.Command.Captain is CrewMember cp && cp.CanAct ? cp : Council.Decider(w, false);
        HaltBy = w.Automation.Present && w.Automation.MainOnline ? "주 컴퓨터" : cap?.Name ?? "배";
        HaltWhy = $"{Ko.IGa(ExpeditionSites.CatName(cat))} 바닥났다 ({StockText(cat)})";
        w.RaiseAlert($"배가 멈췄다 — {HaltWhy} · 엔진을 끄고 원정을 궁리한다", null, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, $"엔진을 껐다 — {HaltWhy} · 재료 없이 가면 고장 하나에 배를 잃는다 ({HaltBy})", null, cap != null ? new[] { cap } : null, log: true);
        if (w.Automation.Present && w.Automation.MainOnline)
        {
            w.Automation.Book.Add(ActKind.Advice, null, $"{ExpeditionSites.CatName(cat)} 재고 {StockText(cat)}", "예측: 다음 고장을 못 고친다", "엔진 정지 · 원정 후보 평가", "회의에서 원정을 정하라", "exp:halt", SimTime.TicksPerDay, 60f * 24f,
                (world, act) => world.Expedition.Halted && world.Expedition.Current == null ? ((int, string)?)null : (1, "맞았다 — 멈춘 사이 원정을 꾸렸다"));
            w.Automation.Speak.Announce(w.Automation.Voice.Style($"엔진 정지 — {HaltWhy}. 원정 후보를 띄운다"), null, 1);
        }
        // 다들 안다: 엔진 소리가 끊긴 배
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.IsChild) continue;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f + 0.03f * (1f - c.Traits.Calm));
            if ((c.Id + Stats.Halts) % 3 == 0) Life.Diary(w, c, Persona.Say(c, "엔진 소리가 멎었다. 배가 이렇게 조용한 줄 몰랐다."));
        }
        _nextScan = w.Tick; // 곧바로 센서를 돌린다
        _nextPropose = w.Tick;
        if (Pending != null) Pending.Halted = true; // 미뤄 두던 제안이 급해졌다
    }

    /// <summary>재료가 바닥나지 않았어도 원정대를 보내려면 배를 세운다.</summary>
    private void Hold(Site site, MatCat? forCat = null)
    {
        var w = _w;
        // 이미 바닥난 재료가 있으면 그게 엔진을 끈 까닭이다 — 원정대를 기다리는 것은 그다음 (연대기 · 경보 · 사유가 같은 말을 한다)
        MatCat? bottom = forCat is MatCat fc && Bottom(fc) ? fc : Cats.Where(Bottom).Cast<MatCat?>().FirstOrDefault();
        if (bottom is MatCat b && CanHalt)
        {
            Halt(b);
            HaltWhy += $" · 원정대를 기다린다 — {site.Name}";
            return;
        }
        Halted = true;
        HaltCat = null;
        HaltedSince = w.Tick;
        HaltBy = "원정";
        HaltWhy = $"원정대를 기다린다 — {site.Name}";
        Stats.Halts++;
        w.History.Add(w, HistoryKind.Decision, $"엔진을 껐다 — {HaltWhy} (원정대를 두고 갈 수는 없다)", null, null, log: true);
    }

    private void Resume(string why)
    {
        var w = _w;
        float days = (w.Tick - HaltedSince) / (float)SimTime.TicksPerDay;
        Halted = false;
        HaltCat = null;
        Stats.Resumed++;
        // 비용: 식은 엔진을 다시 데우고 속도를 다시 붙이는 연소 (추진제 · 엔진 마모)
        float burn = MathF.Min(w.Propulsion.Propellant, 3f * w.Propulsion.Scale);
        w.Propulsion.Propellant -= burn;
        foreach (var m in w.Propulsion.Engines) m.Wear = MathF.Min(1f, m.Wear + 0.02f);
        why += $" · 재점화 연소 추진제 {burn:0}kg";
        w.History.Add(w, HistoryKind.Milestone, $"엔진을 다시 켰다 — {why} · 멈춘 {days:0.0}일만큼 일정이 밀렸다", null, null, log: true);
        w.RaiseAlert($"엔진 재점화 — {why} ({days:0.0}일 늦었다)", null, AlertLevel.Notice, shipWide: true);
        foreach (var c in w.Crew) if (!c.Dead && !c.Away) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.04f);
    }

    public string StockText(MatCat cat) => cat switch
    {
        MatCat.Fuel => $"추진제 {Stock(cat):0}%",
        MatCat.Water => $"물 {_w.Water.Level:0}L · 얼음 {Count(ItemKind.Ice)}",
        _ => $"{Stock(cat):0}개",
    };

    // ═══════════════════════════════ 센서: 목적지 찾기 ═══════════════════════════════

    private void Scan()
    {
        var w = _w;
        _nextScan = w.Tick + SimTime.Hours(Halted ? 3f : 8f);
        Sites.RemoveAll(s => !s.Taken && s.Expires <= w.Tick);
        if (w.Voyage.Current.Kind == LegKind.Port) return;
        float q = Math.Clamp(w.Sensors.Quality, 0f, 1.4f);
        // 바깥 소식에서 (조난 신호 · 표류 화물 · 얼음덩이 · 잔해 구름 · 길 잃은 탐사선)
        foreach (var (tick, id, text) in w.Outside.Recent)
        {
            if (tick <= _seenOutside) continue;
            SiteKind? k = id switch
            {
                "mayday" or "oldsos" or "numbers" => SiteKind.Signal, "cargopod" or "seedcrate" or "fueldrum" => SiteKind.Container, "icechunk" => SiteKind.IceComet,
                "junk" => SiteKind.Debris, "probe" or "emptypod" or "raided" => SiteKind.Wreck, "lighthouse" or "buoy" => SiteKind.Station, _ => null,
            };
            if (k is SiteKind sk && Sites.Count < 8) Add(sk, q, $"바깥 소식 — {text}", 0.25f);
        }
        if (w.Outside.Recent.Count > 0) _seenOutside = Math.Max(_seenOutside, w.Outside.Recent[^1].tick);
        // 난파선 일지가 가리킨 곳
        while (_pendingInfos > 0 && Sites.Count < 8) { _pendingInfos--; Add(R.Chance(0.6f) ? SiteKind.Wreck : SiteKind.Station, q, "항해 일지가 가리킨 좌표", 0.35f); }
        int target = (Halted ? 5 : 3) + (q > 0.8f ? 1 : 0);
        if (w.Sensors.Array == null || q <= 0.05f) target = Math.Min(target, 1); // 센서가 죽었다 — 창밖으로 보이는 것 하나
        int tries = 0;
        while (Sites.Count(s => !s.Taken) < target && tries++ < 10)
        {
            var leg = w.Voyage.Current.Kind;
            float total = 0f;
            var weights = ExpeditionSites.All.Select(s => s.Likes.Contains(leg) ? 3f : s.Kind == SiteKind.Signal ? 0.5f : 1f).ToArray();
            total = weights.Sum();
            float x = R.Float() * total;
            int pick = 0;
            for (; pick < weights.Length - 1; pick++) { x -= weights[pick]; if (x <= 0f) break; }
            Add((SiteKind)pick, q, w.Sensors.Array != null ? "장거리 센서" : "창밖 관측", 0f);
        }
        Evaluate();
    }

    private Site Add(SiteKind kind, float q, string by, float certBonus)
    {
        var w = _w;
        var spec = ExpeditionSites.Spec(kind);
        float cert = Math.Clamp(spec.Certainty * (0.55f + 0.5f * q) + certBonus + R.Range(-0.12f, 0.12f), 0.08f, 0.95f);
        // 실제 값: 확실성이 낮을수록 알려진 값과 멀다 (센서 반사가 거짓말을 한다)
        float spread = 1.15f - cert;
        float truth = Math.Clamp(1f + R.Range(-1f, 1f) * spread, 0.15f, 2.2f);
        float risk = Math.Clamp(spec.Risk * R.Range(0.75f, 1.25f), 0.03f, 0.9f);
        float trueRisk = Math.Clamp(risk * (1f + R.Range(-0.6f, 0.8f) * spread), 0.02f, 0.95f);
        var s = new Site
        {
            Id = _nextSite++, Kind = kind, Name = spec.Places[R.Range(0, spec.Places.Length)], Dist = MathF.Round(R.Range(spec.DistMin, spec.DistMax) * 10f) / 10f,
            Risk = risk, Yield = MathF.Round(spec.Yield * R.Range(0.75f, 1.25f)), Certainty = cert, Truth = truth, TrueRisk = trueRisk,
            Found = w.Tick, Expires = w.Tick + SimTime.Hours(R.Range(40f, 110f)), By = by, Seed = R.Range(0, 100000),
        };
        Sites.Add(s);
        if (Sites.Count > 10) Sites.Remove(Sites.First(x => !x.Taken));
        return s;
    }

    /// <summary>주 컴퓨터가 후보를 평가한다 (알려진 값만 — 실제는 모른다). 멎었으면 평가가 없다.</summary>
    private void Evaluate()
    {
        var w = _w;
        bool on = w.Automation.Present && w.Automation.MainOnline;
        foreach (var s in Sites)
        {
            if (!on) { s.CompScore = 0f; s.CompNote = ""; continue; }
            float fill = HaltCat is MatCat hc ? ExpeditionSites.Fills(s.Kind, hc) : Cats.Where(Low).Select(c => ExpeditionSites.Fills(s.Kind, c)).DefaultIfEmpty(0f).Max();
            s.CompScore = s.Yield * (0.4f + 0.6f * s.Certainty) * (0.6f + fill) - s.Risk * 14f - s.Dist * 2.5f;
            var notes = new List<string>();
            if (fill > 0.25f) notes.Add($"{Ko.EulReul((HaltCat is MatCat c0 ? ExpeditionSites.CatName(c0) : "모자란 재료"))} 채운다");
            if (s.Certainty < 0.4f) notes.Add("확실성 낮음 — 센서 반사가 흐리다");
            if (s.Risk > 0.45f) notes.Add("위험 높음");
            if (s.Spec.Cutter && Count(ItemKind.CellPack) == 0) notes.Add("절단기 배터리 없음");
            if (w.Ambience.StormPower > 0.2f) notes.Add("태양 폭풍 중");
            if (Halted && OutsideSystem.HazardsOn && w.Voyage.Current.Kind is LegKind.TradeLane or LegKind.Graveyard or LegKind.DeepVoid or LegKind.Narrows or LegKind.Cruise) notes.Add("멈춘 배 — 해적 주의");
            if (s.Dist > 1.8f) notes.Add("멀다");
            s.CompNote = notes.Count > 0 ? string.Join(" · ", notes) : "무난";
        }
    }

    public Site? ComputerPick => _w.Automation.Present && _w.Automation.MainOnline ? Sites.Where(s => !s.Taken).OrderByDescending(s => s.CompScore).ThenBy(s => s.Id).FirstOrDefault() : null;

    // ═══════════════════════════════ 주 컴퓨터: 예측 · 경고 ═══════════════════════════════

    private void Forecast()
    {
        var w = _w;
        if (!w.Automation.Present || !w.Automation.MainOnline || Halted) return;
        foreach (var cat in Cats)
        {
            int i = (int)cat;
            float d = DaysLeft(cat);
            if (d > 3f || Bottom(cat) || _forecastAt[i] >= 0 && w.Tick - _forecastAt[i] < SimTime.TicksPerDay * 2) continue;
            if (Rate[i] <= 0.01f) continue;
            _forecastAt[i] = w.Tick;
            if (_nextScan > w.Tick + SimTime.Hours(1)) _nextScan = w.Tick;
            Scan();
            var pick = ComputerPick;
            string rec = pick != null ? $" · 권하는 곳: {pick.Name}({pick.Spec.Name} · 편도 {pick.Dist:0.0}일 · 위험 {pick.Risk * 100:0}% · 확실성 {pick.Certainty * 100:0}%)" : " · 쓸 만한 후보가 아직 없다";
            string text = $"예측 — {Ko.IGa(ExpeditionSites.CatName(cat))} {d:0.0}일 안에 바닥난다 (하루 {Rate[i]:0.0}{(cat == MatCat.Fuel ? "%" : cat == MatCat.Water ? "L" : "개")} 소모){rec}";
            Forecasts.Add((w.Tick, cat, d, text));
            if (Forecasts.Count > 30) Forecasts.RemoveAt(0);
            Stats.Forecasts++;
            w.Automation.Speak.Announce(w.Automation.Voice.Style(text), null, 1);
            long at = w.Tick;
            w.Automation.Book.Add(ActKind.Advice, null, $"{ExpeditionSites.CatName(cat)} 재고 {StockText(cat)} · 하루 {Rate[i]:0.0} 소모", $"예측: {d:0.0}일 안에 바닥", pick != null ? $"원정 후보 평가 — {pick.Name}" : "원정 후보 탐색",
                "원정을 회의에 올려라", "exp:fc:" + cat, SimTime.TicksPerDay * 2, MathF.Max(60f, d * 24f * 60f + 360f),
                (world, act) => world.Expedition.Bottom(cat) || world.Expedition.Past.Any(p => p.Called >= at) || world.Expedition.Current != null ? (1, "맞았다 — 예측대로 모자랐다") : (-1, "틀렸다 — 아직 남아 있다"));
            if (pick != null) Warn(pick, "예측");
        }
    }

    /// <summary>위험 경고 (방송 · 다섯 칸 기록).</summary>
    private void Warn(Site s, string when, Trip? t = null)
    {
        var w = _w;
        if (!w.Automation.Present || !w.Automation.MainOnline) return;
        var why = new List<string>();
        if (s.Risk > 0.4f) why.Add($"위험 {s.Risk * 100:0}%");
        if (s.Certainty < 0.4f) why.Add("센서가 흐리다 — 가 봐야 안다");
        if (t != null && t.Gear.GetValueOrDefault("구급 키트") == 0) why.Add("구급 키트 없이 나간다");
        if (t != null && w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit)) <= 1) why.Add("배에 남는 우주복이 하나뿐");
        if (w.Ambience.StormPower > 0.2f) why.Add("태양 폭풍 중");
        bool serious = why.Count > 0;
        if (!serious && t == null) return;
        if (!serious) why.Add($"위험 {s.Risk * 100:0}% · 확실성 {s.Certainty * 100:0}% · 정해 둔 무전 시간을 지켜라"); // 출발 전 점검: 큰 위험이 없어도 한 줄
        string text = $"{(serious ? "경고" : "주의")} — {s.Name}: " + string.Join(" · ", why);
        Warnings.Add((w.Tick, text));
        if (Warnings.Count > 30) Warnings.RemoveAt(0);
        Stats.Warnings++;
        if (t != null) t.CompWarn = text;
        w.Automation.Speak.Announce(w.Automation.Voice.Style(text), null, 1);
        w.Automation.Book.Add(ActKind.Advice, null, $"{when}: {s.Name}({s.Spec.Name})", string.Join(" · ", why), "위험 경고", "장비를 더 챙기거나 다른 곳으로", "exp:warn:" + s.Id, SimTime.Hours(12), 5f);
    }

    // ═══════════════════════════════ 제안 ═══════════════════════════════

    private static bool Notices(CrewMember c, MatCat cat) => cat switch
    {
        MatCat.Repair or MatCat.Structure or MatCat.Parts => c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician,
        MatCat.Fuel => c.Role is CrewRole.Pilot or CrewRole.Engineer,
        MatCat.Water => c.Role is CrewRole.Botanist or CrewRole.Engineer or CrewRole.Technician,
        _ => c.Role is CrewRole.Cook or CrewRole.Botanist or CrewRole.Medic,
    };

    private List<CrewMember> Adults() => _w.Crew.Where(c => !c.Dead && !c.Away && !c.IsChild && c.CanAct).OrderBy(c => c.Id).ToList();

    private void Propose()
    {
        var w = _w;
        if (w.Tick < _nextPropose || Spoils.Count > 0 || w.Policies["expedition"] == 2 || w.Voyage.Current.Kind == LegKind.Port) return;
        var adults = Adults();
        if (adults.Count < 2) return;
        MatCat? need = Halted ? HaltCat : _asked ?? Cats.Where(Low).Cast<MatCat?>().FirstOrDefault(); // v16.16 컴퓨터 계획자가 청한 재료
        if (need is not MatCat cat) return;
        if (Sites.All(s => s.Taken)) { if (w.Tick >= _nextScan - SimTime.Hours(2)) Scan(); if (Sites.All(s => s.Taken)) return; }
        Evaluate();
        // 누가 알아챘나: 그 재료를 손에 쥐는 사람 (멈췄으면 모두)
        var noticers = adults.Where(c => Halted || Notices(c, cat)).ToList();
        var comp = ComputerPick;
        bool compSays = comp != null && (Halted || _asked == cat || Forecasts.Any(f => f.cat == cat && w.Tick - f.tick < SimTime.TicksPerDay * 2));
        // 좋은 목표가 보이면 멈추지 않아도 꺼낸다 (가깝고 · 확실하고 · 덜 위험하고 · 모자란 것을 채운다)
        Site? Good(CrewMember? judge) => Sites.Where(s => !s.Taken && (Halted || s.Certainty >= 0.55f && s.Risk <= 0.38f && s.Dist <= 1.1f && ExpeditionSites.Fills(s.Kind, cat) >= 0.25f))
            .OrderByDescending(s => s.Yield * (0.3f + 0.7f * s.Certainty) * (0.5f + ExpeditionSites.Fills(s.Kind, cat)) - s.Risk * (judge == null ? 12f : 22f - 18f * judge.Traits.Bravery) - s.Dist * 2f)
            .ThenBy(s => s.Id).FirstOrDefault();
        CrewMember? proposer = noticers.OrderByDescending(c => Initiative(c, cat)).ThenBy(c => c.Id).FirstOrDefault(c => Initiative(c, cat) > 0.5f);
        Site? site = proposer != null ? Good(proposer) : compSays ? (Halted || _asked == cat ? comp : Good(null)) : null;
        // 관찰자가 고른 후보 (방침 "원정 목적지")
        int pickNo = w.Policies["expsite"];
        var open = Sites.Where(s => !s.Taken).ToList();
        if (pickNo > 0 && pickNo <= open.Count) site = open[pickNo - 1];
        if (site == null) return;
        _nextPropose = w.Tick + SimTime.Hours(Halted ? 8f : 60f);
        string source = proposer != null ? proposer.Name : "주 컴퓨터";
        string why = Halted ? $"배가 멈췄다 — {HaltWhy}" : $"{Ko.IGa(ExpeditionSites.CatName(cat))} 모자라다 ({StockText(cat)}) · 가까운 곳에 {site.Spec.Name}";
        var p = new ExpProposal { Id = _nextProp++, Tick = w.Tick, Site = site, Source = source, ProposerId = proposer?.Id ?? -1, Why = why, For = cat, Halted = Halted };
        ComputerSay(p);
        // 승무원: 자원 · 반대 (목표 · 감정 · 관계)
        foreach (var c in adults)
        {
            var (vs, vwhy) = VolunteerScore(c, site, cat);
            if (vs > 0.62f) { p.Volunteers.Add((c.Id, vwhy)); Stats.Volunteers++; }
        }
        foreach (var c in adults)
        {
            var (os, owhy) = ObjectScore(c, p);
            if (os > 0.55f && !p.Volunteers.Any(v => v.who == c.Id)) { p.Objectors.Add((c.Id, owhy)); Stats.Objections++; }
        }
        p.Team = PickTeam(site, p.Volunteers.Select(v => v.who).ToList());
        if (p.Team.Count == 0) { w.Log.Add(w.Tick, LogKind.Ship, $"원정 — {site.Name}에 보낼 사람이 없다 (다쳤거나 · 앓거나 · 지쳤거나 · 우주복이 모자라다)"); return; }
        Pending = p;
        Proposals.Add(p);
        if (Proposals.Count > 20) Proposals.RemoveAt(0);
        Stats.Proposals++;
        string team = string.Join("·", p.Team.Select(id => w.Crew[id].Name));
        string head = proposer != null ? $"{Ko.IGa(proposer.Name)} 원정을 꺼냈다" : "주 컴퓨터가 원정을 제안했다";
        w.Log.Add(w.Tick, LogKind.Ship, $"{head} — {site.Name}({site.Spec.Name} · 편도 {site.Dist:0.0}일 · 위험 {site.Risk * 100:0}% · 예상 {site.Yield:0}개 · 확실성 {site.Certainty * 100:0}%) · {why} · 원정대 {team}"
            + (p.Volunteers.Count > 0 ? $" · 자원 {string.Join("·", p.Volunteers.Select(v => w.Crew[v.who].Name))}" : "")
            + (p.Objectors.Count > 0 ? $" · 반대 {string.Join("·", p.Objectors.Select(v => w.Crew[v.who].Name))}" : ""), proposer?.Id ?? -1);
        if (proposer != null)
        {
            proposer.Say(w, Persona.Say(proposer, Halted ? $"이대로 떠 있을 순 없어. {site.Name}에 가 보자" : $"{Ko.IGa(ExpeditionSites.CatName(cat))} 떨어져 가. {Ko.IGa(site.Name)} 가까워"));
            Life.Diary(w, proposer, Persona.Say(proposer, $"{site.Name}에 가자고 했다. 다들 내 얼굴만 봤다."));
        }
        foreach (var (id, vwhy) in p.Volunteers) Life.Diary(w, w.Crew[id], Persona.Say(w.Crew[id], $"원정에 손을 들었다 — {vwhy}"));
        foreach (var (id, owhy) in p.Objectors) { var o = w.Crew[id]; o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.03f); Life.Diary(w, o, Persona.Say(o, $"원정은 반대다 — {owhy}")); }
        if (comp != null) Warn(site, "제안");
        if (_asked == cat) { bool ok = _askedOk; _asked = null; _askedOk = false; if (ok) Close(p, true, "회의 (주 컴퓨터 안건)", "회의가 먼저 정했다"); } // v16.16
    }

    private MatCat? _asked;
    private bool _askedOk;

    /// <summary>v16.16 주컴퓨터 계획자가 원정을 청한다 (회의가 이미 받았으면 바로 보낸다).</summary>
    public bool ComputerRequest(MatCat cat, bool approved)
    {
        if (Current != null || Pending != null || Spoils.Count > 0 || _w.Policies["expedition"] == 2) return false;
        _asked = cat; _askedOk = approved; _nextPropose = _w.Tick;
        return true;
    }

    /// <summary>주 컴퓨터의 말 (믿는 만큼 먹힌다): 권한다 · 괜찮다 · 다른 곳이 낫다.</summary>
    private void ComputerSay(ExpProposal p)
    {
        var comp = ComputerPick;
        var site = p.Site;
        if (comp == null) { p.ComputerPick = null; p.ComputerSign = 0; p.Computer = null; return; }
        p.ComputerPick = comp.Id;
        if (comp == site) { p.ComputerSign = 1; p.Computer = $"주 컴퓨터: {Ko.EulReul(site.Name)} 권한다 — {site.CompNote}"; }
        else if (site.Risk > comp.Risk + 0.12f || site.CompScore < comp.CompScore - 4f) { p.ComputerSign = -1; p.Computer = $"주 컴퓨터: {site.Name}보다 {Ko.IGa(comp.Name)} 낫다 — {comp.CompNote} (그곳은 {site.CompNote})"; }
        else { p.ComputerSign = 0; p.Computer = $"주 컴퓨터: {site.Name}도 괜찮다 — {site.CompNote}"; }
    }

    /// <summary>먼저 꺼내는 사람: 대담하고 성실하고 말이 많고 — 그 재료를 손에 쥐는 사람.</summary>
    public float Initiative(CrewMember c, MatCat cat) =>
        0.35f * c.Traits.Bravery + 0.25f * c.Traits.Diligence + 0.2f * c.Traits.Sociability + (Notices(c, cat) ? 0.2f : 0f) + (Halted ? 0.1f : 0f)
        + (c.Value == CrewValue.Efficiency ? 0.08f : c.Value == CrewValue.Safety ? -0.08f : 0f) - c.Memory.Trauma * 0.5f - c.Needs.Stress * 0.15f
        + _w.Brain2.Goals.Urge(c, "trip"); // v16.15 계획이 막혀 "원정을 꺼내 보자"는 중기 목표

    /// <summary>자원하나: 용기 · 솜씨 · 내 일에 필요한 재료 · 같이 가고픈 사람 − 겁 · 부상 · 병 · 피로 · 두고 갈 사람.</summary>
    public (float, string) VolunteerScore(CrewMember c, Site site, MatCat cat)
    {
        var w = _w;
        var terms = new List<(float v, string why)>
        {
            (0.4f * c.Traits.Bravery, "겁이 없다"),
            (0.25f * MathF.Max(c.SkillLevel(Skill.Mechanics), c.SkillLevel(Skill.Engineering)), "손이 익다"),
            (0.15f * (1f - c.Needs.Stress), "마음이 가볍다"),
            (Notices(c, cat) ? 0.18f : 0f, $"내 일에 쓸 {Ko.IGa(ExpeditionSites.CatName(cat))} 바닥났다"),
            (Life.Has(c, Habit.Daredevil) ? 0.15f : 0f, "모험이 좋다"),
            (c.Value == CrewValue.Efficiency ? 0.08f : c.Value == CrewValue.Freedom ? 0.06f : 0f, c.Value == CrewValue.Freedom ? "답답한 배를 벗어나고 싶다" : "배가 서 있는 게 아깝다"),
            (Went(c) > 0 ? 0.08f : 0f, "전에도 다녀왔다"),
            (-0.5f * c.Memory.Trauma, ""), (-0.8f * c.Vitals.Injury, ""), (-0.6f * c.Fx.Worst, ""), (-0.3f * c.Needs.Fatigue, ""),
            (c.Partner is int pid && w.Crew[pid] is { Dead: false } && !Halted ? -0.08f : 0f, ""),
            (Life.Has(c, Habit.Homesick) || Life.Has(c, Habit.Worrier) ? -0.08f : 0f, ""),
            (site.Risk > 0.45f ? -0.12f * (1f - c.Traits.Bravery) : 0f, ""),
        };
        float s = terms.Sum(t => t.v) + 0.12f;
        var best = terms.Where(t => t.v > 0f && t.why != "").OrderByDescending(t => t.v).FirstOrDefault();
        return (s, best.why ?? "");
    }

    /// <summary>반대하나: 안전 · 겁 · 데인 기억 · 보낼 사람이 소중하다 · 컴퓨터가 말린다(믿는 만큼).</summary>
    public (float, string) ObjectScore(CrewMember c, ExpProposal p)
    {
        var w = _w;
        var terms = new List<(float v, string why)>
        {
            (c.Value == CrewValue.Safety ? 0.25f : c.Value == CrewValue.People ? 0.12f : 0f, c.Value == CrewValue.Safety ? "안전이 먼저다" : "사람이 먼저다"),
            (0.3f * (1f - c.Traits.Bravery), "무섭다"),
            (0.6f * c.Memory.Trauma, c.Memory.TraumaCause != null ? $"{Ko.EulReul(c.Memory.TraumaCause)} 겪고 나니" : "데인 적이 있다"),
            (0.4f * p.Site.Risk, $"{Ko.EunNeun(p.Site.Name)} 위험하다"),
            (p.Site.Certainty < 0.35f ? 0.15f : 0f, "가 봐야 아는 곳에 사람을 보낼 순 없다"),
            (p.ComputerSign < 0 ? 0.3f * w.Automation.Trusts.Of(c) : 0f, "컴퓨터도 말린다"),
            (Halted ? -0.25f : 0f, ""),
            (Life.Has(c, Habit.Superstitious) && p.Site.Kind is SiteKind.Signal or SiteKind.Wreck ? 0.15f : 0f, "그런 데는 부정 탄다"),
        };
        foreach (var id in p.Team)
        {
            var m = w.Crew[id];
            if (m == c) continue;
            if (c.Partner == id) terms.Add((0.35f, $"{Ko.EulReul(m.Name)} 보내고 싶지 않다"));
            else if (c.AffinityTo(m) > 0.5f) terms.Add((0.15f, $"{Ko.IGa(m.Name)} 걱정된다"));
        }
        float s = terms.Sum(t => t.v) - 0.1f;
        var best = terms.Where(t => t.v > 0f && t.why != "").OrderByDescending(t => t.v).FirstOrDefault();
        return (s, best.why ?? "");
    }

    /// <summary>나갈 수 있는 몸 (원정대 후보).</summary>
    public bool Fit(CrewMember c) => !c.Dead && !c.Away && c.CanAct && !c.IsChild && c.Vitals.Injury < 0.35f && c.Fx.Worst < 0.3f && c.Needs.Rest > 0.2f
                                     && c.InfectedAt < 0 && !_w.Society.OnProbation(c) && c.Vitals.Health > 0.55f;

    private int SuitsAboard() => _w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit)) + _w.Crew.Count(c => !c.Dead && !c.Away && c.Suit != null);

    /// <summary>원정대 1~4명: 자원한 사람부터, 솜씨 · 체력 · 성격으로, 사이가 나쁜 둘은 되도록 떼어 놓고, 배에 일손을 남긴다.</summary>
    private List<int> PickTeam(Site site, List<int> volunteers)
    {
        var w = _w;
        var adults = Adults();
        var cap = w.Command.Captain;
        float Score(CrewMember c) => (volunteers.Contains(c.Id) ? 0.6f : 0f) + 0.35f * MathF.Max(c.SkillLevel(Skill.Mechanics), c.SkillLevel(Skill.Engineering))
                                     + 0.2f * c.Traits.Bravery + 0.15f * c.Needs.Rest + 0.1f * c.Traits.Calm - (c == cap ? 0.5f : 0f) - 0.4f * c.Memory.Trauma;
        var pool = adults.Where(Fit).OrderByDescending(Score).ThenBy(c => c.Id).ToList();
        int size = 2 + (site.Yield >= 12f || site.Risk >= 0.4f ? 1 : 0) + (HasShuttle ? 1 : 0);
        size = Math.Min(size, Math.Min(4, adults.Count - Math.Max(2, (int)MathF.Ceiling(adults.Count * 0.4f))));
        size = Math.Min(size, SuitsAboard() - 1); // 우주복 한 벌은 배에 남긴다 (봉합 · 구조할 사람 몫)
        if (adults.Count <= 3) size = Math.Min(size, 1);
        size = Math.Max(size, adults.Count >= 2 && SuitsAboard() >= 2 ? 1 : 0);
        var team = new List<CrewMember>();
        foreach (var c in pool)
        {
            if (team.Count >= size) break;
            if (team.Any(t => c.AffinityTo(t) < -0.35f || t.AffinityTo(c) < -0.35f) && pool.Count - pool.IndexOf(c) > size - team.Count) continue;
            team.Add(c);
        }
        return team.Select(c => c.Id).ToList();
    }

    // ═══════════════════════════════ 결정 ═══════════════════════════════

    /// <summary>회의 밖의 결정: 관찰자 방침 · 멈춘 배에서 오래 기다린 제안은 함장이 혼자 정한다 · 좋은 목표는 미뤄 두다 놓친다.</summary>
    private void DecideOutside()
    {
        var w = _w;
        var p = Pending!;
        int order = w.Policies["expedition"];
        if (order == 2) { Close(p, false, "관찰자", "보내지 않는다 (방침)"); return; }
        if (order == 1) { Close(p, true, "관찰자", "당장 보낸다 (방침)"); return; }
        // 관찰자가 원정 창에서 다른 후보를 골랐다 (방침 "원정 목적지")
        int pickNo = w.Policies["expsite"];
        var open = Sites.Where(s => !s.Taken).ToList();
        if (pickNo > 0 && pickNo <= open.Count && open[pickNo - 1] != p.Site)
        {
            var ns = open[pickNo - 1];
            p.Site = ns;
            Evaluate();
            ComputerSay(p);
            p.Team = PickTeam(ns, p.Volunteers.Select(v => v.who).ToList());
            w.Log.Add(w.Tick, LogKind.Ship, $"관찰자가 원정 목적지를 바꿨다 — {ns.Name}({ns.Spec.Name}) · 원정대 {string.Join("·", p.Team.Select(id => w.Crew[id].Name))}");
            if (ns.Risk > 0.4f || ns.Certainty < 0.4f) Warn(ns, "목적지 변경");
        }
        if (!Sites.Contains(p.Site) || p.Site.Expires <= w.Tick && !p.Site.Taken) { Close(p, false, "배", $"{Ko.IGa(p.Site.Name)} 센서에서 사라졌다"); return; }
        long waited = w.Tick - p.Tick;
        if (p.Halted && waited >= SimTime.Hours(6) && !w.Meetings.Gathering && w.Meetings.Session == null)
        {
            var cap = w.Command.Captain is CrewMember cp && cp.CanAct ? cp : Council.Decider(w, false);
            if (cap == null) return;
            var (yes, why) = Opinion(cap, p);
            Close(p, yes, cap.Name, $"함장 판단 — {why}");
            w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(cap.Name)} 혼자 정했다 — 원정 {p.Site.Name}: {(yes ? "보낸다" : "안 보낸다")} ({why})", null, new[] { cap }, log: true);
        }
        else if (!p.Halted && waited >= SimTime.Hours(40)) Close(p, false, "배", "회의에 오르지 못하고 지나갔다");
    }

    /// <summary>한 사람의 찬반과 까닭.</summary>
    public (bool yes, string why) Opinion(CrewMember v, ExpProposal p)
    {
        var w = _w;
        var terms = new List<(float v, string why)>
        {
            (p.Halted ? 0.35f : 0.05f, p.Halted ? "멈춘 채로는 못 버틴다" : "모자란 재료를 채울 때다"),
            ((v.Traits.Bravery - 0.5f) * 0.5f, v.Traits.Bravery > 0.5f ? "해 볼 만하다" : "겁이 난다"),
            (v.Value switch { CrewValue.Efficiency => 0.15f, CrewValue.Freedom => 0.05f, CrewValue.Safety => -0.2f, _ => 0f }, v.Value == CrewValue.Safety ? "안전이 먼저다" : "배가 서 있는 게 아깝다"),
            (-p.Site.Risk * 0.6f, $"{Ko.EunNeun(p.Site.Name)} 위험하다"),
            (p.Site.Certainty * 0.2f - 0.1f, p.Site.Certainty > 0.5f ? "센서가 또렷이 봤다" : "가 봐야 안다"),
            (-v.Memory.Trauma * 0.5f, "데인 기억"),
        };
        if (p.ComputerSign != 0) terms.Add((p.ComputerSign * 0.3f * (w.Automation.Trusts.Of(v) - 0.3f) / 0.7f, p.ComputerSign > 0 ? "컴퓨터가 권한다" : "컴퓨터가 말린다"));
        if (p.Volunteers.Any(x => x.who == v.Id)) terms.Add((0.5f, "내가 가겠다"));
        if (p.Objectors.FirstOrDefault(x => x.who == v.Id) is { who: >= 0 } ob && p.Objectors.Any(x => x.who == v.Id)) terms.Add((-0.5f, ob.why));
        if (p.ProposerId >= 0 && p.ProposerId != v.Id) terms.Add((0.25f * v.AffinityTo(w.Crew[p.ProposerId]), $"{w.Crew[p.ProposerId].Name}의 말이라면"));
        foreach (var id in p.Team) if (v.Partner == id && id != v.Id) terms.Add((-0.25f, $"{Ko.EulReul(w.Crew[id].Name)} 보내기 싫다"));
        float s = terms.Sum(t => t.v);
        var top = (s >= 0f ? terms.Where(t => t.v > 0f).OrderByDescending(t => t.v) : terms.Where(t => t.v < 0f).OrderBy(t => t.v)).FirstOrDefault();
        return (s >= 0f, top.why ?? (s >= 0f ? "찬성" : "반대"));
    }

    /// <summary>회의 안건 (정기 회의 · 사후 검토가 모였을 때).</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        var w = _w;
        if (Pending is not ExpProposal p || attendees.Count < 2) return;
        var item = new AgendaItem
        {
            Title = $"원정: {p.Site.Name}({p.Site.Spec.Name} · 편도 {p.Site.Dist:0.0}일 · 위험 {p.Site.Risk * 100:0}%) — {string.Join("·", p.Team.Select(id => w.Crew[id].Name))}",
            Topic = "expedition", Evidence = p.Why, Computer = p.Computer, ComputerSign = p.ComputerSign,
        };
        if (p.ProposerId >= 0 && attendees.Any(c => c.Id == p.ProposerId))
            item.Speeches.Add(new Speech { Who = p.ProposerId, For = true, Text = Persona.Say(w.Crew[p.ProposerId], p.Halted ? "떠 있기만 하면 다 같이 마른다. 가서 가져오자" : $"{Ko.IGa(p.Site.Name)} 가깝다. 지금 아니면 놓친다") });
        foreach (var (id, vwhy) in p.Volunteers.Take(2)) if (attendees.Any(c => c.Id == id)) item.Speeches.Add(new Speech { Who = id, For = true, Text = Persona.Say(w.Crew[id], $"내가 가겠다 — {vwhy}") });
        foreach (var (id, owhy) in p.Objectors.Take(2)) if (attendees.Any(c => c.Id == id)) item.Speeches.Add(new Speech { Who = id, For = false, Text = Persona.Say(w.Crew[id], $"반대다 — {owhy}") });
        int yes = 0, no = 0;
        var yesList = new List<CrewMember>();
        var noList = new List<CrewMember>();
        foreach (var v in attendees)
        {
            var (y, why) = Opinion(v, p);
            item.Votes.Add((v.Id, y, why));
            if (y) { yes++; yesList.Add(v); } else { no++; noList.Add(v); }
        }
        bool pass = yes > no || yes == no && Opinion(chair, p).yes;
        item.Yes = yes; item.No = no; item.Passed = pass; item.Outcome = pass ? "보낸다" : "보내지 않는다";
        rec.Items.Add(item);
        p.Yes = yes; p.No = no;
        Close(p, pass, $"회의({chair.Name} 의장)", $"찬성 {yes} · 반대 {no}");
        w.History.Add(w, HistoryKind.Decision, $"회의: 원정 {p.Site.Name}? 찬성 {yes} · 반대 {no} → {(pass ? "보낸다" : "그대로 둔다")}" + (p.Computer != null ? $" · {p.Computer}" : ""), null, attendees, log: true);
        // 진 쪽의 마음: 반대했는데 보낸다 — 보낼 사람의 짝은 속이 탄다 · 자원했는데 막혔다
        foreach (var o in noList) if (pass) { o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.03f); if (p.ProposerId >= 0 && p.ProposerId != o.Id) o.ChangeAffinity(w.Crew[p.ProposerId], -0.02f); }
        if (!pass) foreach (var (id, _) in p.Volunteers) w.Crew[id].Needs.Stress = MathF.Min(1f, w.Crew[id].Needs.Stress + 0.03f);
    }

    private void Close(ExpProposal p, bool yes, string by, string why)
    {
        var w = _w;
        p.State = yes ? "보낸다" : "안 보낸다";
        p.DecidedBy = by;
        p.DecideWhy = why;
        p.DecidedAt = w.Tick;
        Pending = null;
        if (yes) { Stats.Approved++; Launch(p); }
        else
        {
            Stats.Rejected++;
            w.Log.Add(w.Tick, LogKind.Ship, $"원정 {p.Site.Name} — 안 보낸다 ({by} · {why})");
            _nextPropose = w.Tick + SimTime.Hours(p.Halted ? 12f : 72f);
        }
    }

    // ═══════════════════════════════ 출발 ═══════════════════════════════

    private void Launch(ExpProposal p)
    {
        var w = _w;
        var site = p.Site;
        var team = p.Team.Select(id => w.Crew[id]).Where(Fit).ToList();
        if (team.Count == 0) { w.Log.Add(w.Tick, LogKind.Warning, $"원정 {site.Name} — 정했지만 나설 사람이 없다"); return; }
        site.Taken = true;
        var t = new Trip { Id = _nextTrip++, Site = site, Called = w.Tick, Source = p.Source, Why = p.Why, Phase = TripPhase.Gathering, Deadline = w.Tick + SimTime.Hours(4) };
        foreach (var c in team) t.Members.Add(new TripMember { Id = c.Id, Volunteer = p.Volunteers.Any(v => v.who == c.Id) });
        var lead = team.OrderByDescending(c => CommandSystem.Leadership(c) + (p.Volunteers.Any(v => v.who == c.Id) ? 0.1f : 0f)).ThenBy(c => c.Id).First();
        t.Leader = lead.Id;
        foreach (var m in t.Members)
        {
            var c = w.Crew[m.Id];
            m.Job = c == lead ? "대장" : c.Role == CrewRole.Medic ? "의무" : c.SkillLevel(Skill.Mechanics) > 0.5f ? "절단" : c.Role == CrewRole.Pilot ? "조종" : "운반";
        }
        // 셔틀 · 드론 · 장비를 재고에서
        t.Shuttle = HasShuttle && ShuttleWear < 0.8f && w.Propulsion.Propellant >= ShuttleCost + 12f;
        float days = site.Dist * 2f * (t.Shuttle ? 0.6f : 1f) + 0.6f + 0.15f * site.Yield / Math.Max(1, team.Count);
        int nd = (int)MathF.Ceiling(days);
        TakeGear(t, "산소통", team.Count * nd + 1, n => { float air = n * 60f; if (w.Air.Reserve < air + 2000f) return 0; w.Air.Reserve -= air; return n; });
        TakeGear(t, "패치", 2, n => Take(ItemKind.Sealant, n, keep: 2));
        TakeGear(t, "공구", Math.Max(1, team.Count / 2), n => { int a = Take(ItemKind.Clamp, n, keep: 0); if (a > 0) return a; t.ToolKind = ItemKind.Tape; return Take(ItemKind.Tape, n, keep: 1); });
        if (site.Spec.Cutter || site.Kind is SiteKind.Debris) TakeGear(t, "절단기", 1, n => w.Ship.FurnitureOf(FurnitureType.Workbench).Any() ? Take(ItemKind.CellPack, n, keep: 0) : 0);
        TakeGear(t, "식량", team.Count * nd, n => { int got = Take(ItemKind.Ration, n, keep: Math.Max(1, Alive - team.Count)); return got + Take(ItemKind.Meal, n - got, keep: Alive - team.Count); });
        TakeGear(t, "구급 키트", 1, n => Take(ItemKind.MedKit, n, keep: 1));
        // 마실 물: 원정 날수만큼 탱크에서 물통에 (본선은 원정대 몫을 마시지 않는다 · 남은 물은 돌아와 되붓는다)
        TakeGear(t, "마실 물", (int)MathF.Ceiling(team.Count * nd * 24f * WaterSystem.CrewLitersPerHour), n => { float give = MathF.Min(n, MathF.Max(0f, w.Water.Level - 25f)); w.Water.Level -= give; return (int)give; });
        if (site.Dist <= 1.6f && w.Drones.Drones.Where(d => d.Operational && d.State == DroneState.Docked && d.Battery > 0.6f).OrderBy(d => d.Kind is DroneKind.Tow or DroneKind.Tug or DroneKind.Scout ? 0 : 1).ThenBy(d => d.Id).FirstOrDefault() is Drone dr)
        {
            t.DroneId = dr.Id;
            t.Gear["드론"] = 1;
        }
        if (t.Shuttle) t.Gear["셔틀"] = 1;
        // 장비 무게: 셔틀이면 일손이 덜 든다
        t.Planned = SimTime.Hours(days * 24f);
        t.OpenAtStart = w.Board.OpenUnsorted.Count();
        t.SuitsAtCall = SuitsAboard();
        // 출발 자리: 셔틀이면 격납고, 아니면 에어락
        var (room, at) = DepartSpot(t.Shuttle);
        t.RoomId = room?.Id ?? -1;
        t.At = at;
        Current = t;
        Stats.Trips++;
        if (!Halted) Hold(site, p.For); // 원정대를 두고 갈 수는 없다 — 배를 세우고 기다린다
        // 일손: 떠나는 사람의 역할을 아무도 안 맡으면 가장 비슷한 사람이 근무를 대신 선다 (당직이 밀린다)
        foreach (var m in t.Members) Cover(t, m, days);
        string names = string.Join("·", team.Select(c => c.Name));
        w.RaiseAlert($"원정대 준비 — {names} → {site.Name}({site.Spec.Name}) · {days:0.0}일 예정 · {(t.Shuttle ? "셔틀" : "에어락")}으로 나간다", room, AlertLevel.Notice, shipWide: true);
        w.Log.Add(w.Tick, LogKind.Ship, $"원정 장비 — {GearText(t)}");
        foreach (var c in team) { c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1 + c.Id % 4); MarkLog.Add(c.Memory.Marks, w.Tick, $"원정대로 뽑혔다 — {site.Name}"); }
        t.CompEstimate = site.Yield;
        Warn(site, "출발 전", t);
    }

    private int Take(ItemKind k, int n, int keep)
    {
        int can = Math.Max(0, Math.Min(n, Count(k) - keep));
        if (can <= 0) return 0;
        return Life.Take(_w, k, can) ? can : 0;
    }

    private static void TakeGear(Trip t, string name, int want, Func<int, int> take)
    {
        if (want <= 0) return;
        int got = take(want);
        if (got > 0) t.Gear[name] = got;
    }

    public static string GearText(Trip t) => t.Gear.Count == 0 ? "맨몸" : string.Join(" · ", t.Gear.Where(kv => kv.Value > 0).Select(kv => kv.Value > 1 ? $"{kv.Key} {kv.Value}" : kv.Key));

    private (Room?, Cell) DepartSpot(bool shuttle)
    {
        var w = _w;
        var ship = w.Ship;
        Room? room = shuttle ? ship.RoomsOf(RoomType.ShuttleBay).Concat(ship.RoomsOf(RoomType.DockingBay)).FirstOrDefault(r => !r.Abandoned) : null;
        room ??= w.Comms.Airlock ?? ship.RoomsOf(RoomType.EvaPrep).FirstOrDefault(r => !r.Abandoned);
        if (room == null) return (null, ship.Rooms.Where(r => !r.Detached).SelectMany(r => r.Cells).First(ship.IsOpenFloor));
        var hatch = ship.Doors.FirstOrDefault(d => d.IsExternal && !d.Removed && (d.RoomA == room || d.RoomB == room));
        var cells = room.Cells.Where(ship.IsOpenFloor).ToList();
        if (cells.Count == 0) cells = room.Cells.ToList();
        var at = hatch != null ? cells.OrderBy(x => Math.Abs(x.X - hatch.Cell.X) + Math.Abs(x.Y - hatch.Cell.Y)).ThenBy(x => x.X).ThenBy(x => x.Y).First() : cells[cells.Count / 2];
        return (room, at);
    }

    /// <summary>근무를 대신 선다: 같은 역할이 배에 없으면 그 일에 가장 가까운 솜씨의 사람이 원정 기간 동안 일과를 늘린다.</summary>
    private void Cover(Trip t, TripMember m, float days)
    {
        var w = _w;
        var c = w.Crew[m.Id];
        var going = t.Members.Select(x => x.Id).ToHashSet();
        var stay = w.Crew.Where(o => !o.Dead && !o.Away && !o.IsChild && o.CanAct && !going.Contains(o.Id)).ToList();
        if (stay.Count == 0) return;
        var skill = c.Role switch
        {
            CrewRole.Engineer => Skill.Engineering, CrewRole.Electrician => Skill.Electrical, CrewRole.Technician => Skill.Mechanics, CrewRole.Medic => Skill.Medicine,
            CrewRole.Botanist => Skill.Botany, CrewRole.Cook => Skill.Cooking, _ => Skill.Piloting,
        };
        // 같은 역할이 남아 있으면 그 사람이 두 사람 몫을 (교대가 밀린다), 없으면 솜씨가 가장 가까운 사람이
        var sub = stay.Where(o => !t.Members.Any(x => x.CoveredBy == o.Id)).OrderByDescending(o => o.Role == c.Role ? 1 : 0).ThenByDescending(o => o.RawSkill(skill)).ThenBy(o => o.Id).FirstOrDefault()
                  ?? stay.OrderByDescending(o => o.Role == c.Role ? 1 : 0).ThenByDescending(o => o.RawSkill(skill)).ThenBy(o => o.Id).First();
        m.CoveredBy = sub.Id;
        sub.CoveringUntil = Math.Max(sub.CoveringUntil, w.Tick + SimTime.Hours(days * 24f + 6f));
        Stats.Covered++;
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 원정에 가는 동안 {CrewRoles.Name(c.Role)} 근무를 {(sub.Role == c.Role ? "혼자 두 몫으로" : "대신")} 선다 — 당직이 밀렸다", sub.Id);
        Life.Diary(w, sub, Persona.Say(sub, $"{c.Name} 몫까지 서야 한다. 며칠은 잠이 모자라겠다."));
    }

    // ═══════════════════════════════ 원정대가 배를 나선다 ═══════════════════════════════

    private static readonly Vector2 ParkBase = new(-80f, -80f);

    private static void Park(CrewMember c)
    {
        c.Position = ParkBase + new Vector2(c.Id * 1.5f, 0f);
        c.PreviousPosition = c.Position;
        c.Room = null;
        c.Outside = false;
        c.Path = null;
        c.Destination = null;
        c.TalkingTo = null;
    }

    /// <summary>원정대 한 사람이 에어락/격납고에서 배를 나선다 (활동의 마지막 단계).</summary>
    internal bool Board(CrewMember c)
    {
        var w = _w;
        if (Current is not Trip t || t.Phase != TripPhase.Gathering || t.Members.FirstOrDefault(m => m.Id == c.Id) is not TripMember m || m.Boarded) return false;
        m.Boarded = true;
        if (c.Carrying is ItemStack held) { VoyageV15.Put(w, held.Kind, held.Count); c.Carrying = null; }
        if (c.Suit == null)
        {
            // 보관함에서 꺼내 입는다 (빈 걸이가 남는다) — 없으면 예비 우주복이 없어 못 간다
            var locker = w.Ship.FurnitureOf(FurnitureType.SuitLocker).Where(f => f.Storage!.Count(ItemKind.Suit) > 0).OrderBy(f => f.Room == c.Room ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();
            if (locker == null) { m.Boarded = false; w.Log.Add(w.Tick, LogKind.Warning, "원정 — 입을 우주복이 없다", c.Id); return false; }
            locker.Storage!.Take(ItemKind.Suit, 1);
            c.Suit = new SuitState();
        }
        c.Suit.Oxygen = SuitState.TankHours;
        if (!t.Shuttle) w.CycleAirlock(); // 에어락 한 번 — 공기가 조금 나간다
        c.Away = true;
        Park(c);
        if (w.Ship.RoomAt(t.At) is Room r) MarkLog.Add(r.Marks, w.Tick, $"{Ko.IGa(c.Name)} 원정을 나섰다");
        w.Log.Add(w.Tick, LogKind.Ship, $"{(t.Shuttle ? "셔틀에 올랐다" : "에어락을 나섰다")} — 원정 {t.Site.Name}", c.Id);
        if (t.Members.All(x => x.Boarded)) Depart(t);
        return true;
    }

    private void Depart(Trip t)
    {
        var w = _w;
        // 끝내 못 나선 사람은 남는다
        foreach (var m in t.Members.Where(x => !x.Boarded).ToList())
        {
            var c = w.Crew[m.Id];
            w.Log.Add(w.Tick, LogKind.Ship, $"원정 — {Ko.EunNeun(c.Name)} 끝내 나서지 못해 남는다", c.Id);
            c.CoveringUntil = -1;
            t.Members.Remove(m);
        }
        if (t.Members.Count == 0) { Cancel(t, "아무도 나서지 못했다"); return; }
        if (t.Leader < 0 || t.Members.All(m => m.Id != t.Leader)) t.Leader = t.Members[0].Id;
        t.Phase = TripPhase.Away;
        t.Departed = w.Tick;
        t.SuitsLeft = SuitsAboard();
        t.ReturnAt = w.Tick + t.Planned;
        t.NextEvent = w.Tick + SimTime.Hours(6f + R.Range(0f, 4f));
        t.NextRadio = NextRadioTime(w.Tick + SimTime.Hours(3));
        if (t.Shuttle) w.Propulsion.Propellant = MathF.Max(0f, w.Propulsion.Propellant - ShuttleCost);
        if (t.DroneId >= 0 && w.Drones.Drones.FirstOrDefault(d => d.Id == t.DroneId) is Drone dr && dr.State == DroneState.Docked && dr.Operational)
        {
            dr.State = DroneState.Lost; // 원정에 따라 나갔다 (거치대가 빈다)
            dr.OnTrip = true;
            dr.StateSince = w.Tick;
            dr.Doing = $"원정 — {t.Site.Name}";
        }
        else { t.DroneId = -1; t.Gear.Remove("드론"); }
        var team = t.Members.Select(m => w.Crew[m.Id]).ToList();
        foreach (var c in w.Crew) if (!c.Dead && !c.Away) LastNews[c.Id] = w.Tick;
        Journal(t, $"출발 — {string.Join("·", team.Select(c => c.Name))} · {t.Site.Name}까지 편도 {t.Site.Dist:0.0}일 · {(t.Shuttle ? "셔틀" : "추진팩과 생명줄")}", 0);
        w.History.Add(w, HistoryKind.Milestone, $"원정대 출발 — {string.Join("·", team.Select(c => c.Name))} → {t.Site.Name}({t.Site.Spec.Name}) · {t.Planned / (float)SimTime.TicksPerDay:0.0}일 예정 · 장비 {GearText(t)}" + (t.CompWarn != "" ? $" · {t.CompWarn}" : ""),
            w.Ship.RoomAt(t.At), team, log: true);
        // 배웅: 짝 · 가까운 사람은 마음이 무겁다
        foreach (var o in w.Crew)
        {
            if (o.Dead || o.Away || o.IsChild) continue;
            var close = team.Where(c => o.Partner == c.Id || o.AffinityTo(c) > 0.45f).ToList();
            if (close.Count == 0) continue;
            o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.04f * close.Count);
            Life.Diary(w, o, Persona.Say(o, $"{Ko.IGa(string.Join("·", close.Select(c => c.Name)))} 떠났다. 무전 시간을 손꼽아 기다린다."));
        }
    }

    private void Cancel(Trip t, string why)
    {
        var w = _w;
        foreach (var m in t.Members) { var c = w.Crew[m.Id]; if (c.Away) Unpark(c, t.At); c.CoveringUntil = -1; }
        foreach (var o in w.Crew) if (o.CoveringUntil > w.Tick && t.Members.Any(m => m.CoveredBy == o.Id)) o.CoveringUntil = -1;
        ReturnGear(t);
        t.Phase = TripPhase.Done;
        t.Site.Taken = false;
        Current = null;
        w.Log.Add(w.Tick, LogKind.Warning, $"원정 {t.Site.Name} — 취소 ({why})");
        _nextPropose = w.Tick + SimTime.Hours(6);
    }

    private long NextRadioTime(long from)
    {
        // 정해 둔 무전 시간: 아침 8시 · 저녁 8시
        long day = from / SimTime.TicksPerDay * SimTime.TicksPerDay;
        foreach (long cand in new[] { day + SimTime.Hours(8), day + SimTime.Hours(20), day + SimTime.TicksPerDay + SimTime.Hours(8) })
            if (cand >= from) return cand;
        return from + SimTime.Hours(12);
    }

    // ═══════════════════════════════ 원정 중 ═══════════════════════════════

    private void Step(Trip t)
    {
        var w = _w;
        if (t.Phase == TripPhase.Gathering)
        {
            if (w.Tick >= t.Deadline && t.Members.Any(m => m.Boarded)) Depart(t);
            else if (w.Tick >= t.Deadline + SimTime.Hours(8)) Cancel(t, "모이지 못했다");
            return;
        }
        if (t.Phase != TripPhase.Away) return;
        var team = t.Members.Where(m => !m.Missing).Select(m => w.Crew[m.Id]).ToList();
        if (w.Tick >= t.NextEvent && team.Count > 0)
        {
            t.NextEvent = w.Tick + SimTime.Hours(R.Range(9f, 15f));
            float sinceDepart = (w.Tick - t.Departed) / (float)SimTime.TicksPerDay;
            float outDays = t.Site.Dist * (t.Shuttle ? 0.6f : 1f);
            if (!t.Arrived && sinceDepart >= outDays * 0.9f) { t.Arrived = true; Journal(t, $"{t.Site.Name} 도착 — {t.Site.Spec.Look}", 0); }
            else if (t.Arrived && !t.Headed && w.Tick >= t.ReturnAt - SimTime.Hours(outDays * 24f))
            {
                t.Headed = true;
                // 며칠 동안의 꾸준한 작업 (사건이 아닌 날의 몫) — 실제 배율 · 솜씨 · 장비 · 셔틀이 몫을 바꾼다
                var ctx = new ExpCtx(w, t, new Rng(unchecked(w.Seed * 389 + t.Id * 6007)), team);
                ctx.Haul((int)MathF.Round(t.Site.Yield * 0.55f));
                Journal(t, $"짐을 싣고 돌아선다 — {LootText(t)}", 0);
            }
            else if (t.Arrived) RollEvent(t, team);
            else Journal(t, Transit(t), 0);
        }
        // 태양 폭풍: 컴퓨터가 무전으로 먼저 알려 주면 그늘로 숨는다
        if (w.Ambience.StormPower > 0.3f && !t.StormWarned && w.Automation.Present && w.Automation.MainOnline && RadioWorks(t))
        {
            t.StormWarned = true;
            Journal(t, "배에서 무전 — 주 컴퓨터가 태양 폭풍을 먼저 알렸다 · 바로 그늘로 숨었다", 1);
            w.Automation.Book.Add(ActKind.Advice, null, $"태양 폭풍 {w.Ambience.StormPower * 100:0}% · 원정대 밖에 있음", "원정대가 맞는다", "무전 중계 — 그늘로", "", "exp:storm", SimTime.Hours(6), 5f);
        }
        if (w.Tick >= t.NextRadio) Radio(t, team);
        if (w.Tick >= t.ReturnAt) Return(t);
    }

    private static readonly string[] Walk =
    {
        "추진팩을 아껴 쓰며 생명줄 줄을 지어 간다", "배의 불빛이 점점 작아진다 — 아무도 뒤돌아보지 않았다", "앞사람 헬멧 등만 보고 간다 — 숨소리만 들린다",
        "별자리로 방향을 다시 잡았다", "추진팩 연료를 셈하며 잠깐 쉬었다", "생명줄을 서로 한 번씩 당겨 확인했다",
    };
    private static readonly string[] Fly =
    {
        "셔틀이 조용히 미끄러진다 — 창밖으로 배가 작아진다", "셔틀 조종석에서 번갈아 졸았다", "셔틀 계기판 불빛 아래 지도를 다시 봤다", "셔틀이 자세를 바로잡느라 작게 분사했다",
    };

    private string Transit(Trip t)
    {
        var pool = t.Shuttle ? Fly : Walk;
        return pool[(t.Journal.Count + t.Id) % pool.Length];
    }

    private void RollEvent(Trip t, List<CrewMember> team)
    {
        var w = _w;
        var r = new Rng(unchecked(w.Seed * 131 + t.Id * 7919 + t.Journal.Count * 104729));
        var ctx = new ExpCtx(w, t, r, team);
        var pool = ExpeditionSites.Events.Where(e => e.Kinds == null || e.Kinds.Contains(t.Site.Kind)).ToList();
        for (int tries = 0; tries < 8 && pool.Count > 0; tries++)
        {
            float Weigh(ExpeditionSites.Ev e) => MathF.Max(0f, e.Weight * (e.Weigh?.Invoke(ctx) ?? 1f)) * (t.Journal.Any(j => j.Text.StartsWith("·" + e.Id)) ? 0.3f : 1f);
            float total = pool.Sum(Weigh);
            if (total <= 0f) break;
            float x = r.Float() * total;
            var ev = pool[^1];
            foreach (var e in pool) { x -= Weigh(e); if (x <= 0f) { ev = e; break; } }
            if (!ev.Run(ctx) || ctx.Text == null) { pool.Remove(ev); continue; }
            Journal(t, ctx.Text, ctx.Tone, ev.Id);
            break;
        }
    }

    private void Journal(Trip t, string text, int tone, string? id = null)
    {
        var w = _w;
        int day = t.Departed < 0 ? 0 : (int)((w.Tick - t.Departed) / SimTime.TicksPerDay) + 1;
        t.Journal.Add(new JournalLine(w.Tick, day, text, tone));
        if (id != null) _evSeen.Add(id);
        w.Log.Add(w.Tick, LogKind.Life, $"[원정 일지 {day}일째] {text}");
    }

    private readonly HashSet<string> _evSeen = new();
    public int EventsSeen => _evSeen.Count;

    public string LootText(Trip t) => t.Loot.Count == 0 ? "건진 것 없음" : string.Join(" · ", t.Loot.OrderBy(kv => (int)kv.Key).Select(kv => $"{ItemKinds.Name(kv.Key)} {kv.Value}"));

    /// <summary>무전이 되나: 통신실 콘솔 · 폭풍 · 끊긴 무전기.</summary>
    public bool RadioWorks(Trip t) => _w.Comms.Console != null && _w.Ambience.StormPower < 0.55f && _w.Tick >= t.RadioBlackout;

    /// <summary>정해 둔 무전 시간: 통신실 · 함교에 깨어 있는 사람만 듣는다. 들은 사람이 옆 사람에게 전한다.</summary>
    private void Radio(Trip t, List<CrewMember> team)
    {
        var w = _w;
        t.NextRadio = NextRadioTime(w.Tick + SimTime.Hours(1));
        int day = (int)((w.Tick - t.Departed) / SimTime.TicksPerDay) + 1;
        bool works = RadioWorks(t) && team.Count > 0;
        string text;
        if (!works) text = _w.Comms.Console == null ? "통신 콘솔이 죽어 있다 — 정해 둔 시간이 그냥 지나갔다" : "정해 둔 무전 시간 — 잡음뿐";
        else
        {
            var lead = team.FirstOrDefault(c => c.Id == t.Leader) ?? team[0];
            var last = t.Journal.LastOrDefault();
            int hurt = t.Members.Count(m => m.Hurt > 0.05f && !m.Missing);
            int lost = t.Members.Count(m => m.Missing);
            text = $"{lead.Name}: " + (lost > 0 ? $"{Ko.EulReul(string.Join("·", t.Members.Where(m => m.Missing).Select(m => w.Crew[m.Id].Name)))} 잃어버렸다. 찾고 있다" : last != null ? last.Text.Split(" — ")[0] : "다들 무사하다")
                   + (hurt > 0 && lost == 0 ? $" · 다친 사람 {hurt}" : lost == 0 && hurt == 0 ? " · 다들 무사하다" : "") + $" · 지금까지 {t.LootTotal}개";
        }
        var call = new ExpeditionRadioCall { Tick = w.Tick, Day = day, Text = text, Lost = !works };
        if (works)
        {
            var rooms = new HashSet<int>();
            if (w.Sensors.CommsRoom is Room cr) rooms.Add(cr.Id);
            foreach (var b in w.Ship.RoomsOf(RoomType.Bridge)) rooms.Add(b.Id);
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Away || !c.IsAwake || c.Room == null || !rooms.Contains(c.Room.Id)) continue;
                call.HeardBy.Add(c.Id);
                Heard(c, t, call);
            }
            Stats.RadioHeard += call.HeardBy.Count;
        }
        else Stats.RadioLost++;
        t.Radio.Add(call);
        Stats.RadioCalls++;
        w.Log.Add(w.Tick, LogKind.Ship, $"[원정 무전] {text}" + (works ? $" (들은 사람 {call.HeardBy.Count})" : ""));
    }

    private void Heard(CrewMember c, Trip t, ExpeditionRadioCall call)
    {
        var w = _w;
        LastNews[c.Id] = call.Tick;
        bool bad = t.Members.Any(m => m.Missing) || call.Text.Contains("다친");
        c.Needs.Stress = Math.Clamp(c.Needs.Stress + (bad ? 0.05f : -0.05f) * (0.5f + Concern(c, t)), 0f, 1f);
        if (Concern(c, t) > 0.4f) Life.Diary(w, c, Persona.Say(c, bad ? $"무전을 들었다. {call.Text.Split(':')[0]}의 목소리가 떨렸다." : "무전을 들었다. 목소리를 들으니 살 것 같다."));
    }

    /// <summary>이 사람이 원정대를 얼마나 걱정하나 (짝 · 친한 사이 · 전우).</summary>
    public float Concern(CrewMember c, Trip t)
    {
        float s = 0f;
        foreach (var m in t.Members)
        {
            if (m.Id == c.Id) continue;
            var o = _w.Crew[m.Id];
            s += MathF.Max(0f, c.AffinityTo(o)) + (c.Partner == o.Id ? 0.6f : 0f) + (Memory.AreComrades(c, o) ? 0.2f : 0f);
        }
        return s + (Life.Has(c, Habit.Worrier) ? 0.3f : 0f);
    }

    /// <summary>남은 사람의 걱정: 소식을 못 들은 시간만큼 커진다 (한 시간마다). 들은 사람이 옆 사람에게 전한다.</summary>
    private void Worries()
    {
        var w = _w;
        var t = Current!;
        var last = t.Radio.LastOrDefault(r => !r.Lost);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.IsChild) continue;
            // 소문: 같은 방에 들은 사람이 깨어 있으면 전해 듣는다
            if (last != null && LastNews.GetValueOrDefault(c.Id, -1) < last.Tick && c.IsAwake && c.Room != null
                && w.Crew.Any(o => o != c && !o.Away && o.IsAwake && o.Room == c.Room && LastNews.GetValueOrDefault(o.Id, -1) >= last.Tick) && R.Chance(0.5f))
            {
                LastNews[c.Id] = last.Tick;
                last.Rumor.Add(c.Id);
            }
            if (t.Phase != TripPhase.Away) continue;
            float concern = Concern(c, t);
            float since = (w.Tick - Math.Max(LastNews.GetValueOrDefault(c.Id, t.Departed), t.Departed)) / (float)SimTime.TicksPerHour;
            float worry = concern * Math.Clamp(since / 24f, 0f, 2f) + (t.Members.Any(m => m.Missing) ? 0.4f : 0f);
            Worry[c.Id] = worry;
            if (worry <= 0.05f) continue;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.004f * worry);
            if (worry > 0.6f && (w.Tick / SimTime.TicksPerHour + c.Id) % 24 == 0)
            {
                Stats.Worries++;
                var who = t.Members.Select(m => w.Crew[m.Id]).OrderByDescending(o => c.AffinityTo(o) + (c.Partner == o.Id ? 1f : 0f)).First();
                Life.Diary(w, c, Persona.Say(c, since > 20f ? $"{who.Name} 소식이 없다. 무전기 앞을 떠날 수가 없다." : $"{Ko.EunNeun(who.Name)} 지금쯤 뭘 하고 있을까."));
                w.Log.Add(w.Tick, LogKind.Life, $"원정대 걱정 — {who.Name} 소식을 {since:0}시간째 못 들었다", c.Id);
            }
        }
    }

    /// <summary>일손 부족: 밀린 일이 쌓이고, 대신 서는 사람이 지친다.</summary>
    private void Shortfall()
    {
        var w = _w;
        var t = Current!;
        foreach (var m in t.Members)
            if (m.CoveredBy >= 0 && w.Crew[m.CoveredBy] is { Dead: false, Away: false } sub && sub.CoveringUntil > w.Tick)
                sub.Needs.Rest = MathF.Max(0.05f, sub.Needs.Rest - 0.008f);
        if ((w.Tick / SimTime.TicksPerHour) % 12 == 0 && t.Phase == TripPhase.Away)
        {
            int open = w.Board.OpenUnsorted.Count();
            if (open > t.OpenAtStart + 2) w.Log.Add(w.Tick, LogKind.Ship, $"일손이 모자라다 — 원정 나간 사이 밀린 일 {open}건 (떠날 때 {t.OpenAtStart}건)");
        }
    }

    // ═══════════════════════════════ 귀환 ═══════════════════════════════

    private void Unpark(CrewMember c, Cell at)
    {
        var w = _w;
        var cell = at;
        if (!w.Ship.IsWalkable(cell))
            cell = (w.Comms.Airlock?.Cells ?? w.Ship.Rooms.Where(r => !r.Detached).SelectMany(r => r.Cells)).FirstOrDefault(w.Ship.IsOpenFloor);
        c.Away = false;
        c.Position = cell.Center + new Vector2((c.Id % 3 - 1) * 0.2f, 0f);
        c.PreviousPosition = c.Position;
        c.Room = w.Ship.RoomAt(cell);
        c.Outside = false;
        c.NextThinkTick = w.Tick + 1;
    }

    private void Return(Trip t)
    {
        var w = _w;
        t.Phase = TripPhase.Back;
        t.Returned = w.Tick;
        var (room0, at0) = (w.Ship.RoomAt(t.At), t.At);
        if (room0 == null || room0.Detached) { var (r1, a1) = DepartSpot(false); room0 = r1; at0 = a1; t.At = a1; t.RoomId = r1?.Id ?? -1; }
        var home = new List<CrewMember>();
        var lost = new List<CrewMember>();
        float days = (t.Returned - t.Departed) / (float)SimTime.TicksPerDay;
        foreach (var m in t.Members)
        {
            var c = w.Crew[m.Id];
            if (m.Missing) { lost.Add(c); continue; }
            Unpark(c, at0);
            home.Add(c);
            if (!t.Shuttle) w.CycleAirlock();
            c.Needs.Rest = MathF.Min(c.Needs.Rest, 0.3f - 0.05f * MathF.Min(3f, days));
            c.Needs.Food = MathF.Max(0.1f, MathF.Min(c.Needs.Food, t.Used.GetValueOrDefault("식량") >= t.Members.Count ? 0.6f : 0.3f));
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.2f);
            c.Soil.SuitDust = MathF.Min(1f, c.Soil.SuitDust + 0.3f + t.Dusty);
            c.Soil.Clothes[(int)SoilKind.Dust] = MathF.Min(1f, c.Soil.Clothes[(int)SoilKind.Dust] + 0.2f + 0.5f * t.Dusty);
            if (c.Suit != null) { c.Suit.Leak = 1f + 1.5f * m.SuitDamage; c.Suit.Oxygen = MathF.Max(0.3f, SuitState.TankHours * (1f - m.SuitDamage)); }
            if (m.SuitDamage >= 0.95f && c.Suit != null) { c.Suit = null; t.Lost.Add("우주복"); } // 큰 파공 — 버리고 왔다
            if (t.Sick.Contains(c.Id)) w.Ailments.Catch(c, "cold", null, $"원정 {t.Site.Name}에서 몸살");
            c.Practice(Skill.Mechanics, 0.03f + 0.01f * days);
            if (t.Shuttle) c.Practice(Skill.Piloting, 0.02f);
            _went[c.Id] = _went.GetValueOrDefault(c.Id) + 1;
            MarkLog.Add(c.Memory.Marks, w.Tick, $"원정에서 돌아왔다 — {t.Site.Name} {days:0.0}일" + (m.Hurt > 0.05f ? " · 다쳤다" : ""));
            Life.Diary(w, c, Persona.Say(c, m.Hurt > 0.1f ? $"{t.Site.Name}에서 다쳐서 왔다. 그래도 살아 돌아왔다." : $"{t.Site.Name}에서 돌아왔다. 배 안 공기 냄새가 이렇게 좋을 줄이야."));
            if (m.Hurt > 0.05f) Stats.Injuries++;
        }
        if (home.FirstOrDefault(c => c.Id == t.Leader) is CrewMember ld) _led[ld.Id] = _led.GetValueOrDefault(ld.Id) + 1;
        foreach (var (saver, _) in t.Saves) _saved[saver] = _saved.GetValueOrDefault(saver) + 1;
        // 함께 고생한 사이
        for (int i = 0; i < home.Count; i++)
            for (int j = i + 1; j < home.Count; j++)
            {
                var a = home[i];
                var b = home[j];
                float d = 0.06f + MathF.Min(0.15f, t.Hardship * 0.3f) + 0.02f * days;
                a.ChangeAffinity(b, d);
                b.ChangeAffinity(a, d);
                int n = a.Memory.Shared.GetValueOrDefault(b.Id) + 1;
                a.Memory.Shared[b.Id] = n;
                b.Memory.Shared[a.Id] = n;
                w.Relations.Remember(a, b, RelationReason.SharedHardship, $"{t.Site.Name} 원정에서 {days:0}일을 함께 버텼다");
                w.Relations.Remember(b, a, RelationReason.SharedHardship, $"{t.Site.Name} 원정에서 {days:0}일을 함께 버텼다");
                if (!a.Memory.Comrades.Contains(b.Id) && n >= 2 && a.AffinityTo(b) >= 0.35f && b.AffinityTo(a) >= 0.35f)
                {
                    a.Memory.Comrades.Add(b.Id);
                    b.Memory.Comrades.Add(a.Id);
                    w.History.Add(w, HistoryKind.Bond, $"{Ko.WaGwa(a.Name)} {Ko.EunNeun(b.Name)} 이제 전우다 — 원정을 함께 다녀왔다", crew: new[] { a, b }, log: true);
                }
            }
        // 내 일을 대신 서 준 사람
        foreach (var m in t.Members)
        {
            if (m.CoveredBy < 0 || m.Missing) continue;
            var sub = w.Crew[m.CoveredBy];
            var c = w.Crew[m.Id];
            if (sub.Dead) continue;
            sub.CoveringUntil = Math.Min(sub.CoveringUntil, w.Tick);
            w.Relations.Remember(c, sub, RelationReason.DidMyShift, $"원정 간 사이 내 {CrewRoles.Name(c.Role)} 일을 대신 서 줬다");
            c.ChangeAffinity(sub, 0.06f);
        }
        // 전리품: 에어락 · 격납고 바닥에 쌓인다 (다들 날라야 창고에 들어간다)
        foreach (var (k, n) in t.Loot.OrderBy(kv => (int)kv.Key)) if (n > 0) Spoils.Add((k, n));
        w.FoodSources.Expedition(t.Loot); // v16.22 원정에서 가져온 먹을 것
        SpoilsRoom = room0?.Id ?? -1;
        SpoilsAt = PileCell(room0, at0);
        Stats.LootTotal += t.LootTotal;
        w.UsedParts.AddRange(t.UsedParts); // 떼어 온 중고 부품 (검사하지 않고 쓰면 재조립 불량이 잦다)
        // 남은 장비는 돌려놓는다
        ReturnGear(t);
        if (t.DroneId >= 0 && w.Drones.Drones.FirstOrDefault(d => d.Id == t.DroneId) is Drone dr)
        {
            dr.OnTrip = false; // 돌아왔거나, 원정지에 두고 와서 이제 정말 잃었다
            if (!t.DroneLost) { dr.State = DroneState.Docked; dr.StateSince = w.Tick; dr.Position = dr.DockPosition; dr.Battery = 0.25f; dr.Condition = MathF.Max(0.2f, dr.Condition - 0.15f); dr.Doing = "대기"; dr.Sorties++; }
            else
            {
                dr.Doing = $"원정에서 잃었다 — {t.Site.Name}";
                MarkLog.Add(dr.Marks, w.Tick, $"{t.Site.Name} 원정에서 돌아오지 못했다");
                foreach (var c in home) Life.Diary(w, c, Persona.Say(c, $"{Ko.EulReul(dr.Name)} 두고 왔다. 그 녀석이 끌어 준 덕에 살았는데."));
            }
        }
        if (t.Shuttle) ShuttleWear = MathF.Min(1f, ShuttleWear + 0.05f + t.ShuttleDamage);
        if (t.ShuttleFound) ShuttleOwned = true;
        // 발견: 생존자 · 유물 · 정보
        for (int i = 0; i < t.Survivors && w.Crew.Count < World.MaxCrew; i++)
        {
            var nc = w.AddSurvivor(at0);
            nc.Joined = $"{t.Site.Name}({t.Site.Spec.Name})에서 원정대가 데려왔다";
            t.Joined.Add(nc.Id);
            Stats.Survivors++;
            foreach (var c in home) { nc.ChangeAffinity(c, 0.3f); w.Relations.Remember(nc, c, RelationReason.SavedMe, $"{t.Site.Name}의 캡슐에서 나를 꺼내 줬다"); }
        }
        if (t.Relic != null)
        {
            Stats.Relics++;
            string id = t.Site.Kind is SiteKind.Wreck or SiteKind.Station ? "derelictplaque" : t.Site.Kind is SiteKind.Container ? "postcards" : "meteorite";
            var spec = Props.Get(id);
            var where = w.Ship.LiveRooms.Where(r => !r.Abandoned && Props.Fits(spec.Place, r)).OrderBy(r => r.Id).FirstOrDefault();
            if (where != null) w.Props.Place(spec, where, home.FirstOrDefault(), $"원정 {t.Site.Name}에서 가져왔다", t.Relic);
        }
        if (t.Infos > 0) { Stats.Infos += t.Infos; w.Research += 4f * t.Infos; _pendingInfos += t.Infos; _nextScan = w.Tick; }
        // 주 컴퓨터 예측과 견준다: 맞으면 믿음이 오르고 틀리면 깎인다
        if (t.CompEstimate > 0f && w.Automation.Present)
        {
            float ratio = t.LootTotal / MathF.Max(1f, t.CompEstimate);
            bool right = ratio >= 0.6f && ratio <= 1.6f;
            foreach (var c in home) w.Automation.Trusts.Change(c, right ? 0.03f : -0.04f, right ? $"원정 수확이 컴퓨터 예상과 맞았다 ({t.LootTotal}/{t.CompEstimate:0})" : $"원정 수확이 컴퓨터 예상과 딴판이었다 ({t.LootTotal}/{t.CompEstimate:0})", quiet: true);
        }
        // 실종: 사람을 잃었다 — 죽을 수 있는 배면 이틀 뒤 사망 처리, 아니면 며칠 뒤 기적처럼 돌아온다
        foreach (var c in lost)
        {
            Stats.Missing++;
            _late.Add((w.Tick + (w.CrewCanDie ? SimTime.TicksPerDay * 2 : SimTime.Hours(R.Range(20f, 40f))), t.Id, c.Id));
            foreach (var o in w.Crew) if (!o.Dead && !o.Away && o != c) o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.08f + 0.2f * MathF.Max(0f, o.AffinityTo(c)));
        }
        t.Story = Highlight(t);
        StoryTrip = t;
        Current = null;
        Past.Add(t);
        if (Past.Count > 20) Past.RemoveAt(0);
        Stats.Returned++;
        foreach (var c in w.Crew) if (!c.Dead && !c.Away) { LastNews[c.Id] = w.Tick; Worry.Remove(c.Id); }
        string who = string.Join("·", home.Select(c => c.Name));
        string tail = (lost.Count > 0 ? $" · 실종 {string.Join("·", lost.Select(c => c.Name))}" : "") + (t.Joined.Count > 0 ? $" · 생존자 {t.Joined.Count}" : "") + (t.Relic != null ? $" · {t.Relic}" : "")
                      + (t.Lost.Count > 0 ? $" · 잃은 장비 {string.Join("·", t.Lost)}" : "");
        Journal(t, $"귀환 — {LootText(t)}" + tail, lost.Count > 0 ? -2 : 1);
        w.RaiseAlert($"원정대 귀환 — {who} · {t.Site.Name} {days:0.0}일 · 재료 {t.LootTotal}개{tail}", room0, lost.Count > 0 ? AlertLevel.Critical : AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Milestone, $"원정대 귀환 — {t.Site.Name}({t.Site.Spec.Name}) {days:0.0}일 · {LootText(t)} · 부상 {t.Members.Count(m => m.Hurt > 0.05f)} · 우주복 손상 {string.Join("/", t.Members.Select(m => TripMember.Stage(m.SuitDamage)))}{tail}",
            room0, home, log: true);
        if (room0 != null) MarkLog.Add(room0.Marks, w.Tick, $"원정대가 돌아왔다 — {t.Site.Name}");
    }

    private Cell PileCell(Room? room, Cell at)
    {
        var w = _w;
        if (room == null) return at;
        return room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => -(Math.Abs(x.X - at.X) + Math.Abs(x.Y - at.Y))).ThenBy(x => x.X).ThenBy(x => x.Y).FirstOrDefault(at);
    }

    private void ReturnGear(Trip t)
    {
        var w = _w;
        void Back(string gear, ItemKind k) { int n = t.Gear.GetValueOrDefault(gear); if (n > 0) VoyageV15.Put(w, k, n); }
        Back("패치", ItemKind.Sealant);
        Back("공구", t.ToolKind);
        Back("절단기", ItemKind.CellPack);
        Back("식량", ItemKind.Ration);
        Back("구급 키트", ItemKind.MedKit);
        // 마실 물: 나가 있던 동안 마신 만큼 빼고 탱크에 되붓는다
        int water = t.Gear.GetValueOrDefault("마실 물");
        if (water > 0)
        {
            float hours = t.Departed >= 0 ? (w.Tick - t.Departed) / (float)SimTime.TicksPerHour : 0f;
            float drank = t.Members.Count(m => m.Boarded) * hours * WaterSystem.CrewLitersPerHour;
            w.Water.Level = MathF.Min(w.Water.Capacity, w.Water.Level + MathF.Max(0f, water - drank));
        }
        int o2 = t.Gear.GetValueOrDefault("산소통");
        if (o2 > 0) w.Air.Reserve = MathF.Min(w.Air.ReserveCapacity, w.Air.Reserve + o2 * 60f * (t.Phase == TripPhase.Back ? 0.5f : 1f));
    }

    /// <summary>식탁에서 할 이야기 한 줄 (가장 크게 흔든 일).</summary>
    private static string Highlight(Trip t)
    {
        var best = t.Journal.Where(j => !j.Text.StartsWith("출발") && !j.Text.StartsWith("귀환")).OrderByDescending(j => Math.Abs(j.Tone)).ThenBy(j => j.Tick).FirstOrDefault();
        return best?.Text ?? $"{t.Site.Name}까지 다녀왔다";
    }

    private void LateReturns()
    {
        var w = _w;
        for (int i = _late.Count - 1; i >= 0; i--)
        {
            var (at, tripId, id) = _late[i];
            if (w.Tick < at) continue;
            _late.RemoveAt(i);
            var c = w.Crew[id];
            var t = Past.FirstOrDefault(p => p.Id == tripId);
            if (c.Dead || !c.Away) continue;
            if (w.CrewCanDie)
            {
                c.Away = false;
                c.Vitals.InjuryCause = $"원정 {t?.Site.Name ?? ""}에서 실종";
                if (t?.Members.FirstOrDefault(m => m.Id == id) is TripMember tm) tm.Dead = true;
                Stats.Deaths++;
                w.KillAway(c);
                w.History.Add(w, HistoryKind.Death, $"{Ko.IGa(c.Name)} 끝내 돌아오지 않았다 — 원정 {t?.Site.Name}에서 실종 · 빈 침대와 빈 우주복 걸이가 남았다", null, new[] { c }, log: true);
                continue;
            }
            var (room, cell) = DepartSpot(false);
            Unpark(c, cell);
            c.Suit ??= new SuitState();
            c.Suit.Oxygen = 0.2f;
            NeedsSystem.AddInjury(c.Vitals, 0.25f, "원정 중 표류");
            c.Vitals.Health = MathF.Max(0.3f, c.Vitals.Health - 0.3f);
            c.Needs.Rest = 0.1f; c.Needs.Food = 0.15f;
            c.Memory.Trauma = MathF.Min(0.35f, c.Memory.Trauma + 0.15f);
            c.Memory.TraumaCause ??= "원정에서 표류";
            if (t?.Members.FirstOrDefault(m => m.Id == id) is TripMember tm2) tm2.Missing = false;
            Stats.Missing = Math.Max(0, Stats.Missing - 1);
            foreach (var o in w.Crew) if (!o.Dead && !o.Away && o != c) { o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.1f); LastNews[o.Id] = w.Tick; }
            w.RaiseAlert($"{Ko.IGa(c.Name)} 돌아왔다! — 표류하다 추진팩을 아껴 배까지 왔다 (다쳤다 · 의무실로)", room, AlertLevel.Critical, shipWide: true);
            w.History.Add(w, HistoryKind.Milestone, $"{Ko.IGa(c.Name)} 실종 {(w.Tick - (t?.Returned ?? w.Tick)) / (float)SimTime.TicksPerHour:0}시간 만에 돌아왔다 — 원정 {t?.Site.Name}에서 표류", room, new[] { c }, log: true);
            Life.Diary(w, c, Persona.Say(c, "별을 보며 방향을 잡았다. 배의 불빛이 그렇게 반가울 수가 없었다."));
        }
    }

    // ═══════════════════════════════ 전리품 · 식탁 이야기 ═══════════════════════════════

    /// <summary>전리품 더미에서 한 묶음을 집는다 (나르는 사람).</summary>
    internal ItemStack? TakeSpoils(ItemKind kind, int max)
    {
        int i = Spoils.FindIndex(sp => sp.kind == kind);
        if (i < 0 || max <= 0) return null;
        var (k, n) = Spoils[i];
        int take = Math.Min(max, n);
        if (take >= n) Spoils.RemoveAt(i); else Spoils[i] = (k, n - take);
        return new ItemStack(k, take);
    }

    /// <summary>다음에 나를 것: 바닥난 갈래를 채우는 것부터.</summary>
    public ItemKind? NextHaul => Spoils.Count == 0 ? null : Spoils.OrderByDescending(sp => ExpeditionSites.CatOf(sp.kind) is MatCat mc && (HaltCat == mc || Low(mc)) ? 1 : 0).First().kind;

    internal void Hauled(CrewMember c, ItemStack s)
    {
        Stats.Hauled += s.Count;
        if (Spoils.Count == 0) _w.Log.Add(_w.Tick, LogKind.Work, "원정 전리품을 다 날랐다 — 에어락 바닥이 비었다", c.Id);
    }

    public int SpoilsCount => Spoils.Sum(s => s.count);

    public bool StoryTime => StoryTrip is { StoryTold: false } && SimTime.HourOfDay(_w.Tick) is >= 17f and < 22.5f && _w.Tick - StoryTrip.Returned >= SimTime.Hours(2);

    /// <summary>식탁 이야기: 원정대 한 사람이 식탁에서 이야기한다 — 같은 방에서 들은 사람은 가까워지고 마음이 풀린다.</summary>
    internal bool Tell(CrewMember teller)
    {
        var w = _w;
        if (StoryTrip is not Trip t || t.StoryTold || teller.Room == null) return false;
        var listeners = w.Crew.Where(o => o != teller && !o.Dead && !o.Away && o.IsAwake && o.Room == teller.Room).ToList();
        if (listeners.Count == 0) return false;
        t.StoryTold = true;
        t.Listeners = listeners.Count;
        Stats.Stories++;
        var dead = t.Members.Where(m => m.Dead || m.Missing).Select(m => w.Crew[m.Id]).ToList();
        bool sad = dead.Count > 0;
        foreach (var o in listeners)
        {
            o.ChangeAffinity(teller, 0.03f);
            teller.ChangeAffinity(o, 0.02f);
            o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.2f);
            o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - (sad ? 0.03f : 0.06f));
            if (t.Members.Any(m => m.Id == o.Id)) o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.05f); // 같이 다녀온 사람: 맞장구
        }
        teller.Needs.Social = MathF.Min(1f, teller.Needs.Social + 0.3f);
        teller.Needs.Stress = MathF.Max(0f, teller.Needs.Stress - 0.08f);
        foreach (var o in listeners.Where(o => (o.Id + t.Id) % 2 == 0).Take(3))
            Life.Diary(w, o, Persona.Say(o, sad ? $"저녁에 {Ko.IGa(teller.Name)} {string.Join("·", dead.Select(d => d.Name))} 이야기를 했다. 아무도 숟가락을 들지 못했다." : $"저녁에 {teller.Name}의 원정 이야기를 들었다 — {t.Story.Split(" — ")[0]}"));
        if (sad) foreach (var d in dead) Life.Diary(w, teller, Persona.Say(teller, $"{d.Name}의 자리를 비워 두고 이야기했다."));
        w.Log.Add(w.Tick, LogKind.Life, $"식탁에서 원정 이야기 — \"{t.Story}\" (들은 사람 {listeners.Count})", teller.Id);
        w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(teller.Name)} 식탁에서 {t.Site.Name} 원정 이야기를 했다 — \"{t.Story}\" · 들은 사람 {listeners.Count}" + (sad ? $" · {string.Join("·", dead.Select(d => d.Name))}의 빈자리" : ""), teller.Room, listeners.Append(teller), log: true);
        if (t.Remains) Life.Diary(w, teller, Persona.Say(teller, $"{t.Site.Name}에서 떼어 온 이름표를 식탁에 올려 두고 이름을 불렀다."));
        return true;
    }

    internal void Prepped(CrewMember c, Furniture locker)
    {
        _prepped.Add(c.Id);
        locker.Checked = _w.Tick;
        Stats.Prepped++;
        _w.Log.Add(_w.Tick, LogKind.Work, "원정에 나설 생각으로 우주복을 미리 점검했다 — 밸브 · 이음매 · 산소통", c.Id);
    }

    public bool IsPrepped(CrewMember c) => _prepped.Contains(c.Id);

    // ═══════════════════════════════ 화면 · 지문 ═══════════════════════════════

    public TripMember? MemberOf(CrewMember c) => Current?.Members.FirstOrDefault(m => m.Id == c.Id);

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Halted ? 1 : 0); I(HaltedSince); F(Stats.DelayDays); I(Stats.Halts); I(Stats.Trips); I(Stats.Returned); I(Stats.Proposals); I(Stats.Approved); I(Stats.Rejected);
        I(Stats.RadioCalls); I(Stats.RadioHeard); I(Stats.Stories); I(Stats.Hauled); I(Stats.Forecasts); I(Stats.Warnings); I(Stats.LootTotal); I(Stats.Missing); I(Stats.Survivors);
        I(Sites.Count); foreach (var s in Sites) { I(s.Id); I((int)s.Kind); F(s.Truth); F(s.Certainty); }
        I(Spoils.Count); foreach (var (k, n) in Spoils) { I((int)k); I(n); }
        if (Current is Trip t) { I(t.Id); I((int)t.Phase); I(t.ReturnAt); I(t.Journal.Count); I(t.LootTotal); foreach (var m in t.Members) { I(m.Id); I(m.Boarded ? 1 : 0); F(m.SuitDamage); F(m.Hurt); } }
        if (Pending is ExpProposal p) { I(p.Id); I(p.Site.Id); foreach (var id in p.Team) I(id); }
        I(ShuttleOwned ? 1 : 0); F(ShuttleWear);
    }
}

/// <summary>v16.12 원정과 그 앞뒤의 행동: 우주복 미리 점검 · 출발(우주복 입고 에어락으로) · 무전 기다리기 · 마중 · 전리품 나르기 · 식탁 이야기.</summary>
public sealed class ExpeditionActivity : Activity
{
    public override string Id => "expedition";
    public override string Label => "원정";

    private enum Task { None, Prep, Depart, Radio, Greet, Haul, Story }

    /// <summary>전리품을 들고 창고로 가는 중 (들어 올린 뒤에는 더미가 비어도 끝까지 간다 — 자기가 든 것 때문에 일을 놓지 않는다).</summary>
    private static bool Hauling(CrewMember c) => c.Job?.Activity is ExpeditionActivity && c.Job.Label == "전리품 나르기" && c.Carrying != null;

    private static Task Pick(CrewMember c, World w, out float score, out string why)
    {
        var x = w.Expedition;
        score = 0f; why = "—";
        if (c.IsChild && c.Age < 10f) return Task.None;
        if (Hauling(c)) { score = 0.7f; why = $"원정 전리품을 창고로 — {c.Carrying}"; return Task.Haul; }
        // 비상(냉각 · 전기 · 쓰러진 사람)은 재료가 바닥난 배의 흔한 모습이다 — 원정이 바로 그 길이라 출발 · 나르기는 한다. 불 · 생존 위기면 모두 미룬다
        bool crisis = Crisis.Acting(w);
        bool dire = crisis && (Crisis.Level(w) == CrisisLevel.Survival || w.Fire.Count > 0);
        float calm = crisis ? 0.8f : 1f;
        if (x.Current is Trip t)
        {
            if (t.Phase == TripPhase.Gathering && x.MemberOf(c) is { Boarded: false })
            {
                if (dire) { why = "불 · 생존 위기 — 출발을 미룬다"; return Task.None; }
                score = 0.96f; why = $"원정 출발 — {t.Site.Name}"; return Task.Depart;
            }
            if (dire) return Task.None;
            if (t.Phase == TripPhase.Away && !c.IsChild)
            {
                long toReturn = t.ReturnAt - w.Tick;
                float concern = x.Concern(c, t);
                if (toReturn >= 0 && toReturn < SimTime.Hours(1.5f) && concern > 0.35f) { score = (0.58f + 0.1f * MathF.Min(1f, concern)) * calm; why = "원정대가 곧 온다 — 에어락으로 마중"; return Task.Greet; }
                long toRadio = t.NextRadio - w.Tick;
                float worry = x.Worry.GetValueOrDefault(c.Id);
                if (toRadio >= 0 && toRadio < SimTime.Minutes(50) && (worry > 0.25f || concern > 0.6f) && w.Comms.Console != null)
                { score = (0.5f + 0.25f * MathF.Min(1f, worry + concern * 0.3f)) * calm; why = $"무전 시간 — {t.Site.Name} 소식을 기다린다"; return Task.Radio; }
            }
        }
        if (dire) return Task.None;
        // 급한 재료: 바닥난 갈래를 채우는 전리품은 비상 중에도 먼저 나른다
        bool urgent = x.Spoils.Any(sp => ExpeditionSites.CatOf(sp.kind) is MatCat mc && (x.HaltCat == mc || x.Low(mc)));
        if (x.Spoils.Count > 0 && urgent && !c.IsChild && c.Carrying == null && c.Needs.Fatigue < 0.9f)
        { score = 0.66f + 0.1f * c.Traits.Diligence; why = $"급한 재료 — 원정 전리품을 창고로 ({x.SpoilsCount}개)"; return Task.Haul; }
        if (!crisis && x.Pending is ExpProposal p && p.Volunteers.Any(v => v.who == c.Id) && !x.IsPrepped(c) && !c.IsChild && c.CanAct)
        { score = 0.42f + 0.1f * c.Traits.Diligence; why = "원정에 나설 생각 — 우주복을 미리 점검한다"; return Task.Prep; }
        if (x.StoryTime && !c.IsChild && c.Needs.Hunger < 0.95f)
        {
            bool teller = x.StoryTrip!.Members.Any(m => m.Id == c.Id && !m.Missing);
            score = (teller ? 0.66f : 0.5f + 0.1f * c.Traits.Sociability) * calm; why = teller ? "저녁 식탁 — 원정 이야기를 한다" : "저녁 식탁 — 원정 이야기를 듣는다"; return Task.Story;
        }
        if (x.Spoils.Count > 0 && !c.IsChild && c.Carrying == null && c.Needs.Fatigue < 0.85f)
        {
            score = (0.4f + (x.Halted ? 0.12f : 0f) + 0.08f * c.Traits.Diligence) * calm; why = $"원정 전리품 나르기 ({x.SpoilsCount}개)"; return Task.Haul;
        }
        return Task.None;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Expedition.Current == null && w.Expedition.Pending == null && w.Expedition.Spoils.Count == 0 && w.Expedition.StoryTrip is not { StoryTold: false } && !Hauling(c)) return (0f, "—");
        Pick(c, w, out float s, out string why);
        return (s, why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var x = w.Expedition;
        var task = Pick(c, w, out _, out _);
        switch (task)
        {
            case Task.Depart:
            {
                var t = x.Current!;
                var toils = new List<Toil>();
                toils.AddRange(Plans.DropOff(c, w, dist));
                if (c.Suit == null)
                {
                    var (locker, ls) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.SuitLocker && f.Storage!.Count(ItemKind.Suit) > 0);
                    if (locker != null)
                    {
                        toils.Add(new GotoToil(ls));
                        toils.Add(new WaitToil(SimTime.Minutes(5), Pose.Working, locker.Center));
                        toils.Add(new DoToil((cm, world) =>
                        {
                            if (cm.Suit != null) return true;
                            if (locker.Storage!.Take(ItemKind.Suit, 1) == 0) return true; // 남이 먼저 가져갔다 — 나설 때 다른 걸이에서
                            cm.Suit = new SuitState();
                            world.Log.Add(world.Tick, LogKind.Work, "원정용 우주복을 꺼내 입었다 — 걸이가 빈다", cm.Id);
                            return true;
                        }) { DonsSuit = true });
                    }
                }
                var spot = Spot(w, dist, c, t.At, t.RoomId);
                if (spot is not Cell at) return null;
                toils.Add(new GotoToil(at));
                toils.Add(new WaitToil(SimTime.Minutes(8), Pose.Working) { DoneWhen = (cm, world) => world.Expedition.Current is Trip tt && tt.Members.Where(m => !m.Boarded).All(m => world.Crew[m.Id].Room == cm.Room || m.Id == cm.Id) });
                toils.Add(new DoToil((cm, world) => world.Expedition.Board(cm)));
                return new Job(this, $"원정 출발 — {t.Site.Name}", toils) { LogText = $"원정 준비 — 우주복을 입고 {(t.Shuttle ? "셔틀 격납고" : "에어락")}으로", LogKind = LogKind.Ship, InterruptMargin = 0.4f, Urgent = true };
            }
            case Task.Prep:
            {
                var (locker, ls) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.SuitLocker);
                if (locker == null) return null;
                return new Job(this, "우주복 점검", new List<Toil>
                {
                    new GotoToil(ls),
                    new WaitToil(SimTime.Minutes(12), Pose.Working, locker.Center),
                    new DoToil((cm, world) => { world.Expedition.Prepped(cm, locker); return true; }),
                }) { LogText = "원정 준비 — 우주복 보관함으로", LogKind = LogKind.Work };
            }
            case Task.Radio:
            {
                var con = w.Comms.Console;
                if (con == null) return null;
                var spot = con.UseSpots.Where(dist.Reachable).OrderBy(s => w.IsSpotTaken(s, c) ? 1 : 0).ThenBy(dist.Get).Cast<Cell?>().FirstOrDefault()
                           ?? Spot(w, dist, c, con.UseSpots.FirstOrDefault(), con.Room.Id);
                if (spot is not Cell at) return null;
                long radio = x.Current!.NextRadio;
                x.Stats.RadioWaits++;
                return new Job(this, "무전 기다리기", new List<Toil>
                {
                    new GotoToil(at),
                    new WaitToil(SimTime.Minutes(70), Pose.Sitting, con.Center) { DoneWhen = (cm, world) => world.Tick > radio + SimTime.Minutes(5) },
                }) { LogText = "무전 시간 — 통신실에서 원정대 소식을 기다린다", LogKind = LogKind.Life };
            }
            case Task.Greet:
            {
                var t = x.Current!;
                var spot = Spot(w, dist, c, t.At, t.RoomId);
                if (spot is not Cell at) return null;
                x.Stats.Greeted++;
                return new Job(this, "원정대 마중", new List<Toil>
                {
                    new GotoToil(at),
                    new WaitToil(SimTime.Minutes(90), Pose.Standing) { DoneWhen = (cm, world) => world.Expedition.Current == null },
                }) { LogText = "원정대가 곧 온다 — 마중 나간다", LogKind = LogKind.Life };
            }
            case Task.Haul:
            {
                if (x.NextHaul is not ItemKind kind) return null;
                var pile = Spot(w, dist, c, x.SpoilsAt, x.SpoilsRoom);
                if (pile is not Cell pc) return null;
                var (box, bs) = Plans.NearestContainer(w, dist, c, f => f.Type is not (FurnitureType.SuitLocker or FurnitureType.DroneDock) && f.Storage!.Accepts(kind) && f.Storage.Free > 0);
                if (box == null) return null;
                return new Job(this, "전리품 나르기", new List<Toil>
                {
                    new GotoToil(pc),
                    new WaitToil(SimTime.Minutes(2), Pose.Working),
                    new DoToil((cm, world) =>
                    {
                        if (cm.Carrying != null) return true;
                        if (world.Expedition.TakeSpoils(kind, Math.Min(5, box.Storage!.Free)) is not ItemStack s) return false;
                        cm.Carrying = s;
                        return true;
                    }),
                    new GotoToil(bs),
                    new DoToil((cm, world) =>
                    {
                        if (cm.Carrying is not ItemStack s) return true;
                        int put = box.Storage!.Add(s.Kind, s.Count);
                        if (put < s.Count) VoyageV15.Put(world, s.Kind, s.Count - put);
                        cm.Carrying = null;
                        world.Expedition.Hauled(cm, s);
                        return true;
                    }),
                }) { LogText = "원정 전리품을 창고로 나른다", LogKind = LogKind.Work };
            }
            case Task.Story:
            {
                var mess = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Galley or RoomType.Lounge && !r.Abandoned).OrderBy(r => r.Type == RoomType.Mess ? 0 : r.Type == RoomType.Lounge ? 1 : 2).ThenBy(r => r.Id).FirstOrDefault();
                if (mess == null) return null;
                var seat = mess.Cells.Where(s => w.Ship.IsWalkable(s) && dist.Reachable(s) && !w.IsSpotTaken(s, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
                if (seat is not Cell at) return null;
                bool teller = x.StoryTrip!.Members.Any(m => m.Id == c.Id && !m.Missing);
                return new Job(this, teller ? "원정 이야기" : "원정 이야기 듣기", new List<Toil>
                {
                    new GotoToil(at),
                    new WaitToil(SimTime.Minutes(teller ? 30 : 45), Pose.Sitting) { DoneWhen = (cm, world) => !teller && world.Expedition.StoryTrip is { StoryTold: true } },
                    new DoToil((cm, world) => { if (teller) world.Expedition.Tell(cm); return true; }),
                }) { LogText = teller ? "저녁 식탁 — 원정 이야기를 꺼낸다" : "저녁 식탁 — 원정 이야기를 들으러", LogKind = LogKind.Life };
            }
        }
        return null;
    }

    /// <summary>그 방 안에서 설 칸 (가깝고 · 비어 있고 · 닿는 곳).</summary>
    private static Cell? Spot(World w, DistanceField dist, CrewMember c, Cell want, int roomId)
    {
        if (dist.Reachable(want) && w.Ship.IsWalkable(want) && !w.IsSpotTaken(want, c)) return want;
        var room = roomId >= 0 && roomId < w.Ship.Rooms.Count ? w.Ship.Rooms[roomId] : w.Ship.RoomAt(want);
        var cells = room?.Cells ?? Enumerable.Empty<Cell>();
        return cells.Where(s => w.Ship.IsWalkable(s) && dist.Reachable(s) && !w.IsSpotTaken(s, c))
            .OrderBy(s => Math.Abs(s.X - want.X) + Math.Abs(s.Y - want.Y)).ThenBy(s => s.X).ThenBy(s => s.Y).Cast<Cell?>().FirstOrDefault()
            ?? (dist.Reachable(want) ? want : null);
    }
}
