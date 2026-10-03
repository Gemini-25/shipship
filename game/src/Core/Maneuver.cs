using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.0 기동 · 충격과 고정.
//  예고된 기동: 주컴퓨터가 운석까지 남은 시간을 재서 점화를 몇 분 늦추고(사람들이 붙잡을 틈) 방송한다 →
//   사람마다 다르게 준비한다 (하던 일 · 솜씨 · 성격 · 습관 · 겪은 횟수): 냄비 집게 · 이동식 히터 끄기 · 들고 있던 짐 내려놓기 ·
//   제 컵 쥐기 · 선반 걸쇠 · 카트 끈 · 깨지기 쉬운 것 묶기 → 앉거나 손잡이를 잡는다. 자는 사람 침대 끈은 컴퓨터가 당긴다(권한이 있으면).
//  그 순간에만 판정한다 (성능): 무게 · 마찰 · 고정 · 보관 상태로 선반 물건이 떨어지고 · 카트가 구르고 · 히터가 넘어지고 ·
//   냄비가 쏟아지고 · 식탁 컵이 떨어지고 · 바닥 물건이 미끄러진다 (ObjectPhysics 같은 규칙) · 서 있던 사람은 넘어진다(가끔 다친다).
//  예고 없는 충격(운석 · 큰 충격파)은 준비 없이 같은 판정을 받는다 — 고정해 둔 것만 남는다.
//  뒤처리: 떨어진 것 주워 제자리 · 깨진 조각은 빗자루로(밥 먹은 사람이) · 냄비 집게 풀고 데우다 만 수프는 이름표 접시로 ·
//   고정 점검(헐거워진 걸쇠) · 꺼 둔 히터 다시 켜기 · 넘어진 히터 · 굴러간 카트 제자리.
//  여러 번 잃고 나면 배에 관행이 생긴다 (쓰고 난 건 걸쇠 · 끈 · 자기 전 침대 끈) → 다음 충격에 덜 떨어진다.
//  화물 무게중심이 한쪽으로 쏠리면 기동할 때 배가 돌아 추력이 준다 — 컴퓨터가 계산해 옮기자고 하고, 사람들이 상자를 옮긴다.

public enum ManeuverKind : byte { Evasion, Course, Impact, Quake }
public enum PrepKind : byte { ClampPot, HeaterOff, SetDown, HoldCup, LatchShelf, StrapCart, TieDown, Brace }
public enum FallenKind : byte { Can, Bowl, Book, Bottle, Box, Tool, PillJar, Plate }
public enum SplashKind : byte { Soup, Water, Pills }

/// <summary>한 번의 기동 또는 충격.</summary>
public sealed class Maneuver
{
    public int Id { get; init; }
    public ManeuverKind Kind { get; init; }
    public long Start { get; init; }
    public long Ignite { get; internal set; }
    public long End { get; internal set; }
    /// <summary>배가 받는 가속 (G).</summary>
    public float G { get; init; }
    /// <summary>배가 밀리는 쪽 (물건은 반대로 쏠린다).</summary>
    public Vector2 Dir { get; init; }
    public string Why { get; init; } = "";
    public bool Announced { get; internal set; }
    public bool Hit { get; internal set; }
    public bool Over { get; internal set; }
    public List<int> Knew { get; } = new();
    public int Fell, Hurt, Dropped, Broken, Rolled, Tipped, Spilled, Held, Braced, Safe, Thrown, Slid, CupsFell, Torn, Fast;
    public List<int> Rooms { get; } = new();
    public string Talk => Kind switch { ManeuverKind.Evasion => "회피 기동", ManeuverKind.Course => "항로 연소", ManeuverKind.Impact => "충격", _ => "흔들림" };
}

/// <summary>준비 한 가지 (누가 무엇을).</summary>
public sealed class PrepTask
{
    public PrepKind Kind { get; init; }
    public int Target { get; init; } = -1;
    public Cell Spot { get; init; }
    public Vector2 Face { get; init; }
    public bool Done { get; set; }
}

/// <summary>떨어진 물건 (주워 제자리에 둘 때까지 바닥에).</summary>
public sealed class FallenThing
{
    public int Id { get; init; }
    public FallenKind Kind { get; init; }
    public Cell At { get; init; }
    public Vector2 Off { get; init; }
    public float Angle { get; init; }
    public bool Broken { get; init; }
    public int From { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    public long Tick { get; init; }
    public int ClaimedBy { get; set; } = -1;
}

/// <summary>쏟은 자국 (수프 · 물 · 알약).</summary>
public sealed class Splash
{
    public SplashKind Kind { get; init; }
    public Cell At { get; init; }
    public Vector2 Dir { get; init; }
    public long Tick { get; init; }
    public float Size { get; init; }
}

/// <summary>넘어진 사람 (몇 분 동안 바닥에 주저앉아 있다).</summary>
public sealed class Tumble
{
    public int Crew { get; init; }
    public long Tick { get; init; }
    public Vector2 At { get; init; }
    public Vector2 Dir { get; init; }
    public bool Hurt { get; init; }
    public bool FromBed { get; init; }
    public bool Up { get; set; }
}

/// <summary>깨진 조각 자리 (빗자루로 쓸 때까지).</summary>
public sealed class ShardSpot
{
    public Cell At { get; init; }
    public int RoomId { get; init; }
    public long Tick { get; init; }
    public string What { get; init; } = "";
    public int Cup { get; init; } = -1;
    public int ClaimedBy { get; set; } = -1;
}

/// <summary>데우는 냄비 (야간 근무자 몫 같은).</summary>
public sealed class WarmPot
{
    public int Stove { get; init; }
    public int Batch { get; init; }
    public int For { get; init; }
    public int Cook { get; init; }
    public long Since { get; init; }
    public bool Stopped { get; set; }
    /// <summary>기동 때 집게를 물려 지켜 낸 냄비 (누가).</summary>
    public bool Kept { get; set; }
    public int KeptBy { get; set; } = -1;
    /// <summary>덜어 둔 접시 (먹고 나면 목록에서 빠지니 직접 붙든다).</summary>
    internal Plate? Dish { get; set; }
    public int Plate { get; set; } = -1;
}

public sealed class ManeuverStats
{
    public int Planned, Announced, Unwarned, Shocks, Moments, Heard, Told, Holds;
    public int Clamped, HeatersOff, SetDown, CupsHeld, Latched, Strapped, TiedDown, Sat, Gripped, BunksAuto, BunksHabit;
    public int Fell, Hurt, SleepersSafe, SleepersThrown, Dropped, Broken, Rolled, Tipped, PotsHeld, PotsSpilled, CupsFell, CupsBroken, Slid, HeldFast, Torn;
    public int Picked, Swept, Unclamped, PlatesKept, Thanked, Checked, Refixed, HeatersBack, Righted, HabitLatches, Rebalanced, TrimWarns, Reports, Customs;
    public string Line() =>
        $"기동 {Planned}(방송 {Announced} · 예고 없음 {Unwarned} · 충격 {Shocks} · 점화 늦춤 {Holds}) · 들음 {Heard}(옆 사람이 알림 {Told}) · " +
        $"준비: 냄비 {Clamped} · 히터 끔 {HeatersOff} · 짐 내림 {SetDown} · 컵 쥠 {CupsHeld} · 걸쇠 {Latched} · 카트 끈 {Strapped} · 묶음 {TiedDown} · 앉음 {Sat} · 손잡이 {Gripped} · 침대 끈 {BunksAuto}/{BunksHabit} · " +
        $"순간: 넘어짐 {Fell}(다침 {Hurt}) · 잠든 사람 안전 {SleepersSafe}/떨어짐 {SleepersThrown} · 떨어진 것 {Dropped}(깨짐 {Broken}) · 카트 {Rolled} · 히터 {Tipped} · 냄비 {PotsHeld}/{PotsSpilled} · 컵 {CupsFell}({CupsBroken}) · 미끄럼 {Slid} · 버팀 {HeldFast} · 뜯김 {Torn} · " +
        $"뒤: 주움 {Picked} · 쓸어 냄 {Swept} · 집게 풂 {Unclamped}(접시 {PlatesKept} · 고맙다 {Thanked}) · 점검 {Checked}(조임 {Refixed}) · 히터 다시 {HeatersBack} · 제자리 {Righted} · 걸쇠 습관 {HabitLatches} · 화물 옮김 {Rebalanced}(경고 {TrimWarns}) · 관행 {Customs}";
}

public sealed partial class ManeuverSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 211));
    public ManeuverStats Stats { get; } = new();
    /// <summary>시험: 끄면 아무것도 하지 않는다 (성능 비교).</summary>
    public static bool Off;
    public static long UpdateTicks;

    public List<Maneuver> Recent { get; } = new();
    public Maneuver? Pending { get; private set; }
    public List<FallenThing> Fallen { get; } = new();
    public List<Splash> Splashes { get; } = new();
    public List<Tumble> Tumbles { get; } = new();
    public List<ShardSpot> Shards { get; } = new();
    public List<WarmPot> Warming { get; } = new();
    /// <summary>걸쇠를 건 선반 · 끈을 맨 카트 · 집게를 문 냄비(화구) · 당긴 침대 끈 (설비 · 장비 id).</summary>
    public SortedSet<int> Latched { get; } = new();
    public SortedSet<int> Strapped { get; } = new();
    public SortedSet<int> Clamped { get; } = new();
    public SortedSet<int> Bunks { get; } = new();
    /// <summary>넘어진 이동식 히터 · 굴러간 카트 (장비 id → 굴러가기 전 자리).</summary>
    public SortedDictionary<int, Cell> Tipped { get; } = new();
    public SortedDictionary<int, Cell> Rolled { get; } = new();
    /// <summary>준비하며 꺼 둔 히터 (장비 id → 끈 사람).</summary>
    public SortedDictionary<int, int> HeaterOffBy { get; } = new();
    /// <summary>집게를 문 사람 (화구 id → 사람).</summary>
    public SortedDictionary<int, int> ClampedBy { get; } = new();
    /// <summary>헐거워진 걸쇠 · 끈 (점검이 찾아 조인다).</summary>
    public SortedSet<int> Loose { get; } = new();
    /// <summary>사람마다: 준비할 일 · 손잡이 잡은 곳 · 겪은 횟수.</summary>
    internal readonly Dictionary<int, List<PrepTask>> Preps = new();
    public SortedDictionary<int, (Cell at, Cell wall)> Grips { get; } = new();
    public SortedDictionary<int, int> Felt { get; } = new();
    /// <summary>빗자루: 걸어 두는 자리 · 들고 있는 사람.</summary>
    public Cell? BroomHome { get; private set; }
    public int BroomBy { get; internal set; } = -1;
    /// <summary>걸쇠 점검할 방 (기동 뒤).</summary>
    public SortedSet<int> CheckRooms { get; } = new();
    internal readonly SortedDictionary<int, int> _claims = new(); // 대상 키 → 사람
    public int LossEvents { get; private set; }
    public long LastLoss { get; private set; } = -1;
    // 화물 무게중심
    public float TrimMul { get; private set; } = 1f;
    public float Imbalance { get; private set; }
    public Vector2 CargoCenter { get; private set; }
    public Vector2 ShipCenter { get; private set; }
    public float CargoKg { get; private set; }
    public (int from, int to, int n)? Rebalance { get; private set; }
    public string LastHold { get; private set; } = "";
    private long _nextTrim, _nextWarm, _nextCustom;
    private int _next = 1, _nextThing = 1;
    private Burn? _burn;
    private bool _course;

    public ManeuverSystem(World w) => _w = w;

    private CrewMember? CrewOf(int id) => id >= 0 && id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);
    internal static bool Shelfish(FurnitureType t) => t is FurnitureType.Shelf or FurnitureType.Bookshelf or FurnitureType.ToolWall or FurnitureType.SupplyCache or FurnitureType.Fridge;
    internal static bool Bedish(FurnitureType t) => t is FurnitureType.Bed or FurnitureType.Cot or FurnitureType.MedBed;
    private static int Key(int kind, int id) => kind * 100000 + id;
    internal bool Claimed(int kind, int id, CrewMember c) => _claims.TryGetValue(Key(kind, id), out var by) && by != c.Id;
    internal void Claim(int kind, int id, CrewMember c) => _claims[Key(kind, id)] = c.Id;
    internal void Unclaim(int kind, int id) => _claims.Remove(Key(kind, id));
    public bool Active => Pending is { Over: false };
    public bool Bracing(CrewMember c) => Grips.ContainsKey(c.Id);
    public Tumble? Down(CrewMember c) { for (int i = Tumbles.Count - 1; i >= 0; i--) if (Tumbles[i].Crew == c.Id && !Tumbles[i].Up) return Tumbles[i]; return null; }

    // ───────────────────────────── 매 틱 (값싼 확인) ─────────────────────────────

    /// <summary>매 틱: 엔진 연소가 새로 걸렸나 · 점화 · 끝.</summary>
    public void Step()
    {
        if (Off) return;
        var w = _w;
        var b = w.Propulsion.Current;
        if (b != null && !ReferenceEquals(b, _burn)) { _burn = b; FromBurn(b); }
        else if (b == null) _burn = null;
        bool cb = w.Propulsion.CourseBurn;
        if (cb && !_course && Pending == null) Begin(ManeuverKind.Course, 0f, 0.07f, "항로를 바꾸려 엔진을 태운다", 60f);
        _course = cb;
        if (Pending is not Maneuver m) return;
        if (!m.Hit && w.Tick >= m.Ignite) Moment(m, crew: true);
        if (m.Hit && !m.Over && w.Tick >= m.End) Finish(m);
    }

    /// <summary>Propulsion 훅: 컴퓨터가 조종하면 점화를 늦춰 사람들이 붙잡을 틈을 준다 (운석보다 1분 남짓 먼저 끝나게).</summary>
    public float Hold(float spin, float lead, string by)
    {
        if (Off || !by.StartsWith("자동 조종")) return spin;
        float room = lead - PropulsionSystem.BurnMinutes - 0.8f;
        float hold = MathF.Min(3f, room);
        if (hold <= spin + 0.2f) return spin;
        LastHold = $"운석까지 {lead:0.#}분 — 점화를 {hold:0.#}분 뒤로 미뤄도 {lead - hold - PropulsionSystem.BurnMinutes:0.#}분 남는다 · 그 사이 붙잡게 한다";
        Stats.Holds++;
        return hold;
    }

    private void FromBurn(Burn b)
    {
        if (!b.Evasion || Pending is { Over: false }) return;
        float g = Math.Clamp(0.22f + 0.3f * b.Thrust, 0.2f, 0.6f);
        var m = Begin(ManeuverKind.Evasion, (b.Ignite - _w.Tick) / (float)SimTime.Minutes(1), g, b.ControlBy, (b.End - b.Ignite) / (float)SimTime.Minutes(1));
        m.Ignite = b.Ignite;
        m.End = b.End;
    }

    /// <summary>기동을 잡는다 (분 뒤 점화 · G · 연소 분). 예고할 틈이 있으면 방송하고 준비를 나눈다.</summary>
    public Maneuver Begin(ManeuverKind kind, float minutes, float g, string why, float burnMinutes = PropulsionSystem.BurnMinutes)
    {
        var w = _w;
        var dirs = new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) };
        var m = new Maneuver
        {
            Id = _next++, Kind = kind, Start = w.Tick, G = g, Dir = dirs[R.Range(0, 4)], Why = why,
            Ignite = w.Tick + SimTime.Minutes(MathF.Max(0f, minutes)), End = w.Tick + SimTime.Minutes(MathF.Max(0f, minutes) + MathF.Max(0.2f, burnMinutes)),
        };
        Pending = m;
        Recent.Add(m);
        if (Recent.Count > 20) Recent.RemoveAt(0);
        Stats.Planned++;
        if (minutes >= 0.35f && kind != ManeuverKind.Course) Warn(m, minutes);
        else if (kind != ManeuverKind.Course) Stats.Unwarned++;
        if (minutes <= 0f) Moment(m, crew: true);
        return m;
    }

    /// <summary>예고 없는 충격 (운석 · 충격파): 그 자리에서 판정한다. crew=false면 사람은 다른 쪽(우주 대재난)이 이미 넘어뜨렸다.</summary>
    public void Shock(float g, Vector2 dir, Room? near, string why, bool warned, bool crew = true)
    {
        if (Off || g < 0.12f) return;
        var w = _w;
        if (dir.LengthSquared() < 1e-4f) dir = Vector2.UnitX;
        var m = new Maneuver { Id = _next++, Kind = crew ? ManeuverKind.Impact : ManeuverKind.Quake, Start = w.Tick, Ignite = w.Tick, End = w.Tick + SimTime.Minutes(0.5f), G = MathF.Min(1.2f, g), Dir = Vector2.Normalize(dir), Why = why };
        Recent.Add(m);
        if (Recent.Count > 20) Recent.RemoveAt(0);
        Stats.Shocks++;
        if (!warned) Stats.Unwarned++;
        Moment(m, crew);
        // 기동이 걸려 있지 않으면 곧바로 뒤처리 (기동 중이면 기동이 끝날 때 함께)
        if (Pending is not { Over: false }) { Pending = m; Finish(m); }
    }

    // ───────────────────────────── 예고 · 준비 나누기 ─────────────────────────────

    private void Warn(Maneuver m, float minutes)
    {
        var w = _w;
        string when = minutes >= 1.5f ? $"{MathF.Round(minutes):0}분 뒤" : minutes >= 0.75f ? "1분 뒤" : "30초 뒤";
        var risks = new List<string>();
        if (w.Ship.FurnitureOf(FurnitureType.Stove).Any(PotOn)) risks.Add("불 위 냄비는 고정");
        if (w.Portable.Devices.Any(d => d.Kind == PortableKind.Heater && d.Placed && d.On)) risks.Add("이동식 히터는 끄고");
        risks.Add("선반을 잠그고");
        string trim = TrimMul < 0.93f ? $". 화물이 한쪽으로 쏠려 배가 둔하다 (추력 {TrimMul * 100:0}퍼센트)" : "";
        string text = $"{when} {m.Talk}. {string.Join(", ", risks)}, 자리에 앉거나 손잡이를 잡아 주십시오{trim}";
        var b = w.Automation.Speak.Announce(w.Automation.Voice.Style(text), null, minutes < 1.5f ? 2 : 1);
        bool auto = w.Automation.CoreOnline && w.Automation.Authority.Level(Domain.Crisis) == AuthLevel.Auto;
        if (b != null)
        {
            m.Announced = true;
            Stats.Announced++;
            foreach (var id in b.HeardBy) if (!m.Knew.Contains(id)) m.Knew.Add(id);
            Stats.Heard += b.HeardBy.Count;
            if (LastHold.Length > 0 && m.Kind == ManeuverKind.Evasion)
                w.Automation.Book.Add(ActKind.Plan, null, LastHold, "점화를 미루면 비킬 거리는 같고, 사람들이 앉을 틈이 생긴다", $"{when} 점화 · 방송", "", "maneuver:hold", SimTime.Minutes(2));
        }
        // 못 들은 사람: 같은 방에서 들은 사람이 소리친다
        foreach (var c in w.Crew)
        {
            if (!Awake(c) || m.Knew.Contains(c.Id) || c.Room is not Room r) continue;
            var teller = w.Crew.FirstOrDefault(o => o != c && m.Knew.Contains(o.Id) && o.Room == r && Awake(o));
            if (teller == null) continue;
            m.Knew.Add(c.Id);
            Stats.Told++;
            teller.Say(w, Persona.Say(teller, R.Chance(0.5f) ? $"{c.Name}, 기동이래 — 붙잡아!" : "다들 뭐 하나 잡아요, 곧 밀려요!"));
        }
        // 자는 사람 침대 끈: 컴퓨터가 당긴다 (권한이 있고 그 방에 전기가 오면)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Pose != Pose.Sleeping || BedUnder(c) is not Furniture bed || Bunks.Contains(bed.Id)) continue;
            if (auto && bed.Room.Powered)
            {
                Bunks.Add(bed.Id);
                Stats.BunksAuto++;
                MarkLog.Add(bed.Room.Marks, w.Tick, $"{m.Talk} 전에 컴퓨터가 {c.Name}의 침대 끈을 당겼다");
            }
        }
        // 불 위 냄비는 그 냄비를 보던 사람 몫으로 먼저 잡아 둔다 (데우던 조리사 · 끓이던 사람)
        foreach (var st in w.Ship.FurnitureOf(FurnitureType.Stove).Where(PotOn).OrderBy(f => f.Id))
        {
            var mine = Warming.Where(x => x.Stove == st.Id && !x.Stopped).Select(x => CrewOf(x.Cook)).FirstOrDefault()
                       ?? w.Crew.FirstOrDefault(c => c.Job?.Order is { Kind: WorkKind.Cook } o && o.Target.Furniture == st);
            if (mine != null && m.Knew.Contains(mine.Id) && Awake(mine) && mine.Room == st.Room) Claim(1, st.Id, mine);
        }
        // 준비 나누기 (사람마다 다르게)
        foreach (var c in w.Crew.OrderBy(c => c.Id))
            if (m.Knew.Contains(c.Id) && Awake(c) && c.CanAct && !c.Outside && c.CarriedBy == null && c.Room != null) PlanPrep(c, m, minutes);
    }

    private static bool Awake(CrewMember c) => !c.Dead && !c.Down && !c.Away && c.IsAwake;

    /// <summary>불 위에 냄비가 있나 (끓이는 중 · 데우는 중 · 불 위에 남은 것).</summary>
    public bool PotOn(Furniture st) =>
        st.Type == FurnitureType.Stove && !st.Room.Detached && (_w.Cooking.CookingAt(st) != null || Warming.Any(x => x.Stove == st.Id && !x.Stopped) || _w.Cooking.ScorchingIn(st.Room) == st);

    private void PlanPrep(CrewMember c, Maneuver m, float minutes)
    {
        var w = _w;
        var room = c.Room!;
        var list = new List<PrepTask>();
        bool messy = Life.Has(c, Habit.Messy) || Life.Has(c, Habit.Hasty);
        bool careful = Life.Has(c, Habit.NeatFreak) || Life.Has(c, Habit.Methodical) || Life.Has(c, Habit.Perfectionist) || c.Traits.Diligence > 0.65f
                       || Felt.GetValueOrDefault(c.Id) >= 2 || w.Culture.Follows(c, CustomKind.StowAway);
        bool scared = c.Traits.Bravery < 0.25f || Life.Has(c, Habit.Worrier);
        float budget = minutes * 2.2f; // 걸을 수 있는 칸 (3분 ≈ 7칸 · 일하는 시간 빼고)
        if (c.Job?.Activity is ManeuverActivity) c.EndJob(w, ToilStatus.Interrupted); // 뒤처리하던 것도 내려놓는다
        if (c.IsChild) { Preps[c.Id] = new List<PrepTask> { Brace(c) }; c.Interrupt(w); return; }
        // 1) 들고 있던 무거운 짐부터 내려놓는다
        bool heavy = c.Carrying is { Count: >= 2 } || c.CarryingPerson != null || w.Portable.Devices.Any(d => d.HeldBy == c && d.Spec.Weight >= 2);
        if (heavy) { list.Add(new PrepTask { Kind = PrepKind.SetDown, Spot = c.Cell, Face = c.Position }); budget -= 0.5f; }
        // 2) 불 위 냄비: 하던 사람이 먼저, 아니면 요리할 줄 아는 사람
        foreach (var st in room.Furniture.Where(f => f.Type == FurnitureType.Stove).OrderBy(f => f.Id))
        {
            if (!PotOn(st) || Clamped.Contains(st.Id) || Claimed(1, st.Id, c)) continue;
            bool mine = Warming.Any(x => x.Stove == st.Id && x.Cook == c.Id) || c.Job?.Order is { Kind: WorkKind.Cook } o && o.Target.Furniture == st || w.Cooking.HeadCook == c;
            if (!mine && c.SkillLevel(Skill.Cooking) < 0.3f) continue;
            if (Near(st, c) is not Cell s) continue;
            Claim(1, st.Id, c);
            list.Insert(mine ? 0 : list.Count, new PrepTask { Kind = PrepKind.ClampPot, Target = st.Id, Spot = s, Face = st.Center });
            budget -= Dist(c, s) + 1f;
            break;
        }
        // 3) 켜 둔 이동식 히터 (깜빡하는 사람은 지나친다 · 냄비를 맡은 사람은 곁에 다른 사람이 있으면 맡긴다)
        bool potHands = list.Any(x => x.Kind == PrepKind.ClampPot) && w.Crew.Any(o => o != c && o.Room == room && m.Knew.Contains(o.Id) && Awake(o) && o.CanAct && !o.IsChild);
        if (!potHands && !(Life.Has(c, Habit.Forgetful) && R.Chance(0.6f)))
            foreach (var d in w.Portable.Devices.OrderBy(d => d.Id))
            {
                if (d.Kind != PortableKind.Heater || !d.Placed || !d.On || Claimed(2, d.Id, c) || w.Ship.RoomAt(d.At) != room) continue;
                var s = Beside(d.At, c);
                if (s is not Cell sc || Dist(c, sc) > budget) continue;
                Claim(2, d.Id, c);
                list.Add(new PrepTask { Kind = PrepKind.HeaterOff, Target = d.Id, Spot = sc, Face = d.At.Center });
                budget -= Dist(c, sc) + 0.5f;
                break;
            }
        // 4) 식탁 위 제 컵 (커피 없이 못 사는 사람은 남의 컵도)
        foreach (var (id, tc) in w.Info.OnTable)
        {
            if (w.Ship.RoomAt(tc.Table) != room || Claimed(3, id, c)) continue;
            bool own = w.Belongings.Get(id)?.Owner == c.Id || tc.User == c.Id;
            if (!own && !Life.Has(c, Habit.CoffeeAddict) && !careful) continue;
            if (Beside(tc.Spot, c) is not Cell s || Dist(c, s) > budget) continue;
            Claim(3, id, c);
            list.Add(new PrepTask { Kind = PrepKind.HoldCup, Target = id, Spot = s, Face = tc.Spot.Center });
            budget -= Dist(c, s) + 0.3f;
            if (!careful) break;
        }
        // 5) 선반 걸쇠 · 카트 끈 · 깨지기 쉬운 것 (꼼꼼한 사람 · 겪어 본 사람 · 그 방이 일터인 사람)
        if (!messy && !scared && !potHands)
        {
            bool workplace = room.Furniture.Any(f => Shelfish(f.Type)) && (c.Job?.TargetRoom == room || c.Job?.Target?.Room == room);
            int shelves = careful ? 3 : workplace || c.Traits.Diligence > 0.45f ? 1 : 0;
            foreach (var f in room.Furniture.Where(f => Shelfish(f.Type)).OrderBy(f => (f.Center - c.Position).LengthSquared()).ThenBy(f => f.Id))
            {
                if (shelves <= 0 || budget < 1f) break;
                if (Latched.Contains(f.Id) || Claimed(4, f.Id, c) || Near(f, c) is not Cell s) continue;
                Claim(4, f.Id, c);
                list.Add(new PrepTask { Kind = PrepKind.LatchShelf, Target = f.Id, Spot = s, Face = f.Center });
                budget -= Dist(c, s) + 0.6f;
                shelves--;
            }
            if (careful || c.SkillLevel(Skill.Mechanics) > 0.4f)
                foreach (var d in w.Portable.Devices.OrderBy(d => d.Id))
                {
                    if (d.Kind != PortableKind.Cart || !d.Placed || Strapped.Contains(d.Id) || Claimed(5, d.Id, c) || w.Ship.RoomAt(d.At) != room) continue;
                    if (Beside(d.At, c) is not Cell s || Dist(c, s) > budget) continue;
                    Claim(5, d.Id, c);
                    list.Add(new PrepTask { Kind = PrepKind.StrapCart, Target = d.Id, Spot = s, Face = d.At.Center });
                    budget -= Dist(c, s) + 0.8f;
                    break;
                }
            if (careful)
            {
                int n = 0;
                foreach (var t in w.Matter.Things)
                {
                    if (n >= 2 || !t.Loose || t.Stage != BreakStage.Intact || t.Kind is not (ArticleKind.Mug or ArticleKind.GlassJar or ArticleKind.Radio or ArticleKind.WaterJug or ArticleKind.OilCan)) continue;
                    if (w.Ship.RoomAt(t.At) != room || Claimed(6, t.Id, c) || Beside(t.At, c) is not Cell s || Dist(c, s) > budget) continue;
                    Claim(6, t.Id, c);
                    list.Add(new PrepTask { Kind = PrepKind.TieDown, Target = t.Id, Spot = s, Face = t.At.Center });
                    budget -= Dist(c, s) + 0.4f;
                    n++;
                }
            }
        }
        list.Add(Brace(c));
        Preps[c.Id] = list;
        c.Interrupt(w);
    }

    private PrepTask Brace(CrewMember c) => new() { Kind = PrepKind.Brace, Spot = c.Cell, Face = c.Position };
    private static float Dist(CrewMember c, Cell s) => (s.Center - c.Position).Length();

    /// <summary>설비 곁 설 자리 (가까운 것).</summary>
    internal Cell? Near(Furniture f, CrewMember c) =>
        f.UseSpots.Where(s => _w.Ship.IsWalkable(s)).OrderBy(s => (s.Center - c.Position).LengthSquared()).ThenBy(s => s.X * 1000 + s.Y).Cast<Cell?>().FirstOrDefault()
        ?? Beside(f.Cells[0], c);

    /// <summary>그 칸이나 바로 옆 걸을 수 있는 칸.</summary>
    internal Cell? Beside(Cell at, CrewMember c)
    {
        if (_w.Ship.IsOpenFloor(at)) return at;
        Cell? best = null;
        float bd = float.MaxValue;
        foreach (var d in Cell.Dirs4)
        {
            var n = at + d;
            if (!_w.Ship.IsOpenFloor(n)) continue;
            float dd = (n.Center - c.Position).LengthSquared();
            if (dd < bd) { bd = dd; best = n; }
        }
        return best;
    }

    /// <summary>자는 사람 밑의 침대.</summary>
    public Furniture? BedUnder(CrewMember c)
    {
        if (c.Room is not Room r) return null;
        foreach (var f in r.Furniture)
            if (Bedish(f.Type) && (f.Cells.Contains(c.Cell) || (f.Center - c.Position).LengthSquared() < 1.3f)) return f;
        return null;
    }

    // ───────────────────────────── 그 순간 ─────────────────────────────

    private void Moment(Maneuver m, bool crew)
    {
        var w = _w;
        if (m.Hit) return;
        m.Hit = true;
        Stats.Moments++;
        float g = m.G;
        var push = -m.Dir; // 물건 · 사람은 배가 밀리는 반대로 쏠린다
        string why = m.Kind switch
        {
            ManeuverKind.Evasion => "회피 기동에 배가 옆으로 밀릴 때", ManeuverKind.Course => "엔진을 켤 때", ManeuverKind.Impact => $"{m.Why}에 배가 덜컹할 때", _ => $"{m.Why}에 배가 흔들릴 때",
        };
        // 1) 선반: 걸쇠를 안 건 선반에서 떨어진다 (가득할수록 · 셀수록)
        if (g >= 0.2f)
            foreach (var f in w.Ship.Furniture.Where(f => Shelfish(f.Type) && !f.Stowed && !f.Room.Detached && !f.Room.Abandoned).OrderBy(f => f.Id).ToList())
            {
                if (Latched.Contains(f.Id))
                {
                    m.Fast++; Stats.HeldFast++;
                    if (g > 0.75f && R.Chance(0.3f * g)) { Loose.Add(f.Id); m.Torn++; Stats.Torn++; } // 걸쇠는 버텼지만 헐거워졌다
                    continue;
                }
                float fill = f.Storage is Inventory inv && inv.Capacity > 0 ? Math.Clamp(inv.Total / (float)inv.Capacity, 0.2f, 1f) : 0.6f;
                int k = Math.Min(3, (int)(g * 2.5f * fill + R.Float() * 0.8f)); // 선반 턱이 웬만한 건 잡는다
                for (int i = 0; i < k; i++) Drop(m, f, push, why);
                if (!m.Rooms.Contains(f.Room.Id)) m.Rooms.Add(f.Room.Id);
            }
        // 2) 냄비: 집게를 물렸으면 버티고, 아니면 쏟아진다
        foreach (var st in w.Ship.FurnitureOf(FurnitureType.Stove).Where(PotOn).OrderBy(f => f.Id).ToList())
        {
            if (Clamped.Contains(st.Id)) { m.Held++; Stats.PotsHeld++; continue; }
            if (g < 0.18f) continue;
            SpillPot(m, st, push);
        }
        // 3) 이동식 장비: 끈 없는 카트는 구르고, 켜 둔 히터는 넘어진다
        foreach (var d in w.Portable.Devices.OrderBy(d => d.Id).ToList())
        {
            if (!d.Placed || d.Lost) continue;
            if (d.Kind == PortableKind.Cart)
            {
                if (Strapped.Contains(d.Id)) { m.Fast++; Stats.HeldFast++; continue; }
                if (g >= 0.15f) RollCart(m, d, push);
            }
            else if (d.Kind == PortableKind.Heater && !Tipped.ContainsKey(d.Id) && (d.On ? g >= 0.22f : g >= 0.5f)) TipHeater(m, d, push);
        }
        // 4) 식탁 위 컵: 마찰을 넘으면 미끄러져 떨어진다 (가장자리에 둔 건 더 쉽게)
        foreach (var (id, tc) in w.Info.OnTable.ToList())
        {
            if (w.Belongings.Get(id) is not Belonging cup) continue;
            if (HeldCups.Contains(id)) { m.Held++; continue; } // 손에 쥐고 버텼다
            float mu = 0.35f * (tc.Left ? 0.75f : 1.15f);
            if (g <= mu) continue;
            w.Info.OnTable.Remove(id);
            m.CupsFell++; Stats.CupsFell++;
            var room = w.Ship.RoomAt(tc.Spot);
            // 금속 · 타일 바닥이면 거의 깨지고, 러그 · 고무 매트 위면 대개 멀쩡하다
            bool soft = w.Body.FloorAt(tc.Spot) is Material.Fabric or Material.Rubber || w.Matter.RugAt(tc.Spot) != null || w.Matter.MatAt(tc.Spot);
            if (R.Chance(soft ? 0.2f : 0.86f + 0.1f * g))
            {
                w.Info.Break(cup, tc.Spot, why);
                m.Broken++; Stats.CupsBroken++; Stats.Broken++;
                Shards.Add(new ShardSpot { At = tc.Spot, RoomId = room?.Id ?? -1, Tick = w.Tick, What = cup.Name, Cup = id });
            }
            else
            {
                cup.At = tc.Spot;
                MarkLog.Add(cup.Marks, w.Tick, $"{why} 식탁에서 떨어졌지만 멀쩡했다");
            }
            if (room != null && !m.Rooms.Contains(room.Id)) m.Rooms.Add(room.Id);
        }
        // 5) 바닥 물건: 마찰을 넘는 것만 미끄러진다 (고정한 것은 버틴다 — 너무 세면 끈이 뜯긴다)
        foreach (var t in w.Matter.Things.ToList())
        {
            if (t.CarriedBy >= 0 || t.Hung) continue;
            if (t.Fixed) { if (g * t.Mass * ObjectPhysics.G * 0.6f > t.Spec.Tough * 0.5f) { m.Torn++; Stats.Torn++; Loose.Add(-1 - t.Id); } else { m.Fast++; Stats.HeldFast++; } }
            float mu = ObjectPhysics.Friction(w, t) * w.Matter.Gravity;
            if (g <= mu) continue;
            if (w.Matter.PushThing(t, t.Mass * (g - mu) * ObjectPhysics.G * 0.6f, push, why)) { m.Slid++; Stats.Slid++; }
        }
        // 6) 사람
        if (crew)
            foreach (var c in w.Crew.OrderBy(c => c.Id).ToList())
            {
                if (c.Dead || c.Outside || c.Away || c.CarriedBy != null || c.Room == null || c.Down) continue;
                if (c.Pose == Pose.Sleeping) { Sleeper(m, c, push); continue; }
                if (c.Pose == Pose.Sitting || Grips.ContainsKey(c.Id)) { m.Braced++; continue; }
                float slip = w.Body.Mark(c.Cell, CellMark.Wet) * 0.5f + w.Body.Mark(c.Cell, CellMark.Oil) + w.Body.Mark(c.Cell, CellMark.Frost) * 0.6f;
                bool heavy = !Lowered.Contains(c.Id) && (c.Carrying is { Count: >= 2 } || c.CarryingPerson != null || w.Portable.Devices.Any(d => d.HeldBy == c && d.Spec.Weight >= 2));
                float steady = 0.25f * c.SkillLevel(Skill.Piloting) + 0.15f * MathF.Min(3, Felt.GetValueOrDefault(c.Id)) / 3f + (c.Pose == Pose.Working ? 0.1f : 0f) - (c.Age > 60f ? 0.15f : 0f) - 0.3f * c.Vitals.Injury;
                float p = Math.Clamp((g - 0.15f) * 1.5f * (1f + slip) * (heavy ? 1.5f : 1f) * (1f - steady) * (m.Announced && m.Knew.Contains(c.Id) ? 0.8f : 1.2f), 0f, 0.95f);
                if (!R.Chance(p)) { m.Safe++; continue; }
                Fall(m, c, push, heavy, slip, why, fromBed: false);
            }
        if (m.Dropped + m.Broken + m.Fell + m.Spilled + m.Rolled + m.Tipped + m.CupsFell >= 2) { LossEvents++; LastLoss = w.Tick; }
        foreach (var c in w.Crew)
            if (!c.Dead && c.IsAwake && c.Room != null && m.Rooms.Contains(c.Room.Id) && (m.Dropped + m.CupsFell + m.Rolled) > 0) Felt[c.Id] = Felt.GetValueOrDefault(c.Id) + 1;
    }

    private static readonly FallenKind[] GalleyDrop = { FallenKind.Can, FallenKind.Bowl, FallenKind.Plate, FallenKind.Bottle };

    private void Drop(Maneuver m, Furniture f, Vector2 push, string why)
    {
        var w = _w;
        FallenKind kind = f.Type switch
        {
            FurnitureType.Bookshelf => FallenKind.Book,
            FurnitureType.ToolWall => FallenKind.Tool,
            FurnitureType.Fridge => FallenKind.Bottle,
            FurnitureType.SupplyCache => FallenKind.Box,
            _ => f.Room.Type switch
            {
                RoomType.Galley or RoomType.Mess => GalleyDrop[R.Range(0, GalleyDrop.Length)],
                RoomType.Medbay => FallenKind.PillJar,
                RoomType.Workshop or RoomType.Engine => FallenKind.Tool,
                RoomType.Quarters or RoomType.Lounge => R.Chance(0.6f) ? FallenKind.Book : FallenKind.Bowl,
                _ => R.Chance(0.6f) ? FallenKind.Box : FallenKind.Can,
            },
        };
        // 떨어진 자리: 선반 앞 칸에서 쏠린 쪽으로 한두 칸
        Cell at = f.UseSpots.FirstOrDefault(s => w.Ship.IsOpenFloor(s));
        if (at == default) { var c0 = f.Cells[0]; foreach (var d in Cell.Dirs4) if (w.Ship.IsOpenFloor(c0 + d)) { at = c0 + d; break; } }
        if (at == default) return;
        int steps = R.Range(0, 2);
        for (int i = 0; i < steps; i++)
        {
            var n = Cell.FromPosition(at.Center + push);
            if (!w.Ship.IsOpenFloor(n) || w.Ship.RoomAt(n) != f.Room) break;
            at = n;
        }
        bool fragile = kind is FallenKind.Bowl or FallenKind.Bottle or FallenKind.Plate;
        bool broken = fragile && R.Chance(0.45f + 0.4f * m.G);
        var t = new FallenThing
        {
            Id = _nextThing++, Kind = kind, At = at, From = f.Id, RoomId = f.Room.Id, Tick = w.Tick, Broken = broken,
            Off = new Vector2(R.Range(-0.32f, 0.32f), R.Range(-0.32f, 0.32f)), Angle = R.Range(-3.1f, 3.1f),
        };
        Fallen.Add(t);
        if (Fallen.Count > 120) Fallen.RemoveAt(0);
        m.Dropped++; Stats.Dropped++;
        if (broken)
        {
            m.Broken++; Stats.Broken++;
            string what = Name(kind);
            w.Body.RaiseMark(at, CellMark.Glass, 0.55f, $"깨진 {what} 조각");
            Shards.Add(new ShardSpot { At = at, RoomId = f.Room.Id, Tick = w.Tick, What = what });
            if (kind == FallenKind.Bottle) { w.Body.RaiseMark(at, CellMark.Wet, 0.4f, $"깨진 {what}에서 쏟아졌다"); Splashes.Add(new Splash { Kind = SplashKind.Water, At = at, Dir = push, Tick = w.Tick, Size = 0.6f }); }
        }
        else if (kind == FallenKind.PillJar && R.Chance(0.5f)) Splashes.Add(new Splash { Kind = SplashKind.Pills, At = at, Dir = push, Tick = w.Tick, Size = 0.5f });
        if (Splashes.Count > 60) Splashes.RemoveAt(0);
        MarkLog.Add(f.Room.Marks, w.Tick, $"{why} {f.Label}에서 {Name(kind)}{(broken ? "이 떨어져 깨졌다" : "이 떨어졌다")}");
    }

    public static string Name(FallenKind k) => k switch
    {
        FallenKind.Can => "통조림", FallenKind.Bowl => "사발", FallenKind.Book => "책", FallenKind.Bottle => "병", FallenKind.Box => "상자",
        FallenKind.Tool => "공구", FallenKind.PillJar => "약통", _ => "접시",
    };

    private void SpillPot(Maneuver m, Furniture st, Vector2 push)
    {
        var w = _w;
        m.Spilled++; Stats.PotsSpilled++;
        var spot = st.UseSpots.FirstOrDefault(s => w.Ship.IsWalkable(s));
        if (spot == default) spot = st.Cells[0];
        w.Body.RaiseMark(spot, CellMark.Wet, 0.55f, "쏟아진 국물");
        Splashes.Add(new Splash { Kind = SplashKind.Soup, At = spot, Dir = push, Tick = w.Tick, Size = 0.9f });
        var warm = Warming.FirstOrDefault(x => x.Stove == st.Id && !x.Stopped);
        var batch = warm != null ? w.Cooking.Batches.FirstOrDefault(b => b.Id == warm.Batch) : w.Cooking.Batches.LastOrDefault(b => b.Stove == st && b.Portions > 0 && !b.Jar);
        if (batch != null) batch.Portions = Math.Max(0, batch.Portions - (warm != null ? batch.Portions : 2));
        if (warm != null) warm.Stopped = true;
        foreach (var c in w.Crew.OrderBy(c => c.Id))
        {
            if (c.Dead || c.Room != st.Room || c.Pose == Pose.Sitting || Grips.ContainsKey(c.Id) || (c.Position - st.Center).Length() > 1.6f) continue;
            NeedsSystem.AddInjury(c.Vitals, 0.04f, "뜨거운 국물에 데었다");
            c.Say(w, Persona.Say(c, "앗 뜨거!"));
            MarkLog.Add(c.Memory.Marks, w.Tick, "배가 밀릴 때 냄비가 쏟아져 국물에 데었다");
            break;
        }
        w.Log.Add(w.Tick, LogKind.Warning, $"{st.Room.Name} 화구 위 냄비가 쏟아졌다 — 바닥이 국물 범벅");
        if (!m.Rooms.Contains(st.Room.Id)) m.Rooms.Add(st.Room.Id);
    }

    private void RollCart(Maneuver m, PortableDevice d, Vector2 push)
    {
        var w = _w;
        var from = d.At;
        var room = w.Ship.RoomAt(from);
        var at = from;
        int n = Math.Max(1, (int)(m.G * 6f));
        for (int i = 0; i < n; i++)
        {
            var nx = Cell.FromPosition(at.Center + push);
            if (!w.Ship.IsOpenFloor(nx) || w.Portable.Occupied(nx)) break;
            var hit = w.Crew.FirstOrDefault(c => !c.Dead && c.Cell == nx && c.Pose != Pose.Sitting && !Grips.ContainsKey(c.Id));
            if (hit != null)
            {
                NeedsSystem.AddInjury(hit.Vitals, 0.03f, "굴러온 카트에 부딪혔다");
                hit.Say(w, Persona.Say(hit, "윽 — 카트!"));
                m.Hurt++; Stats.Hurt++;
                break;
            }
            at = nx;
        }
        if (at == from) return;
        if (!Rolled.ContainsKey(d.Id)) Rolled[d.Id] = from;
        d.At = at;
        m.Rolled++; Stats.Rolled++;
        if (room != null) { MarkLog.Add(room.Marks, w.Tick, $"끈 없는 카트가 {(at.Center - from.Center).Length():0}칸 굴러갔다"); if (!m.Rooms.Contains(room.Id)) m.Rooms.Add(room.Id); }
    }

    private void TipHeater(Maneuver m, PortableDevice d, Vector2 push)
    {
        var w = _w;
        Tipped[d.Id] = d.At;
        m.Tipped++; Stats.Tipped++;
        var room = w.Ship.RoomAt(d.At);
        if (d.On && d.Running)
        {
            // 열선이 바닥에 닿는다: 그을음 · 탄내 · 드물게 불씨 (넘어지면 꺼지는 장치가 없는 옛 히터)
            w.Body.RaiseMark(d.At, CellMark.Soot, 0.5f, "넘어진 히터가 바닥을 그을렸다");
            if (R.Chance(0.12f * m.G)) w.Fire.Ignite(d.At, 0.2f);
            w.Log.Add(w.Tick, LogKind.Warning, $"{room?.Name ?? "선내"} 켜 둔 이동식 히터가 넘어졌다 — 바닥이 그을린다");
        }
        d.On = false;
        if (room != null && !m.Rooms.Contains(room.Id)) m.Rooms.Add(room.Id);
    }

    private void Sleeper(Maneuver m, CrewMember c, Vector2 push)
    {
        var w = _w;
        var bed = BedUnder(c);
        if (bed != null && Bunks.Contains(bed.Id)) { m.Safe++; Stats.SleepersSafe++; MarkLog.Add(c.Memory.Marks, w.Tick, "자는 사이 배가 밀렸지만 침대 끈이 잡아 줬다"); return; }
        if (m.G < 0.3f) { if (m.G > 0.2f) c.Jolt(w); return; }
        if (!R.Chance(Math.Clamp((m.G - 0.25f) * 1.4f, 0f, 0.9f))) { c.Jolt(w); return; }
        Stats.SleepersThrown++; m.Thrown++;
        Fall(m, c, push, false, 0f, "자다가 배가 밀릴 때", fromBed: true);
    }

    private void Fall(Maneuver m, CrewMember c, Vector2 push, bool heavy, float slip, string why, bool fromBed)
    {
        var w = _w;
        bool hurt = R.Chance(0.15f + (heavy ? 0.25f : 0f) + (m.G > 0.6f ? 0.2f : 0f) + (c.Age > 60f ? 0.15f : 0f) + slip * 0.2f);
        if (hurt)
        {
            float dmg = R.Range(0.03f, 0.1f) * (0.6f + m.G);
            NeedsSystem.AddInjury(c.Vitals, dmg, fromBed ? "자다가 침대에서 떨어졌다" : heavy ? "짐을 든 채 넘어졌다" : "배가 밀릴 때 넘어졌다");
            Memory.Shake(w, c, 0.04f, "배가 밀릴 때 넘어져 다쳤다");
            m.Hurt++; Stats.Hurt++;
        }
        var at = c.Position + push * 0.35f;
        Tumbles.Add(new Tumble { Crew = c.Id, Tick = w.Tick, At = at, Dir = push, Hurt = hurt, FromBed = fromBed });
        if (Tumbles.Count > 40) Tumbles.RemoveAt(0);
        m.Fell++; Stats.Fell++;
        w.Body.Stats.Falls++;
        MarkLog.Add(c.Memory.Marks, w.Tick, fromBed ? "자다가 배가 밀려 침대에서 굴러떨어졌다" : $"{why} 넘어졌다" + (heavy ? " (짐을 들고 있었다)" : ""));
        c.Say(w, Persona.Say(c, fromBed ? "어어 — 뭐야, 바닥이야?" : hurt ? "아야…" : R.Chance(0.5f) ? "으악!" : "어이쿠"));
        c.Jolt(w);
        Felt[c.Id] = Felt.GetValueOrDefault(c.Id) + 1;
        Preps.Remove(c.Id);
        Grips.Remove(c.Id);
        Seated.Remove(c.Id);
        if (c.Job?.Activity is ManeuverActivity) c.EndJob(w, ToilStatus.Interrupted);
        c.Interrupt(w);
    }

    // ───────────────────────────── 끝 · 뒤처리 ─────────────────────────────

    private void Finish(Maneuver m)
    {
        var w = _w;
        m.Over = true;
        if (Pending == m) Pending = null;
        Grips.Clear();
        Preps.Clear();
        Seated.Clear(); HeldCups.Clear(); Lowered.Clear();
        foreach (var k in _claims.Where(kv => kv.Key / 100000 <= 6).Select(kv => kv.Key).ToList()) _claims.Remove(k);
        foreach (var id in m.Rooms) CheckRooms.Add(id);
        if (Latched.Count + Strapped.Count > 0)
            foreach (var f in w.Ship.Furniture.Where(f => Latched.Contains(f.Id))) CheckRooms.Add(f.Room.Id);
        // 컴퓨터: 무엇이 어디서 떨어졌나 (방마다) — 조각 조심 · 다친 사람
        if (m.Kind != ManeuverKind.Course && w.Automation.CoreOnline)
        {
            var parts = new List<string>();
            foreach (var rid in m.Rooms.OrderBy(x => x).Take(3))
            {
                var room = w.Ship.Rooms[rid];
                int fell = Fallen.Count(f => f.RoomId == rid && f.Tick >= m.Ignite && !f.Broken);
                int glass = Shards.Count(s => s.RoomId == rid && s.Tick >= m.Ignite);
                if (fell + glass == 0) continue;
                parts.Add($"{room.Name} {fell + glass}개" + (glass > 0 ? $" (깨진 것 {glass} — 조각 조심)" : ""));
            }
            string hurt = m.Hurt > 0 ? $" · 다친 사람 {m.Hurt}명 — 의무실로" : m.Fell > 0 ? $" · 넘어진 사람 {m.Fell}명, 다친 데는 없다고 한다" : "";
            string text = parts.Count > 0 ? $"{m.Talk} 끝. 떨어진 것: {string.Join(", ", parts)}{hurt}" : $"{m.Talk} 끝. 떨어진 것 없다{hurt}";
            if (m.Kind != ManeuverKind.Quake || parts.Count > 0)
            {
                w.Automation.Speak.Announce(w.Automation.Voice.Style(text), null, 0);
                Stats.Reports++;
            }
            if (m.Dropped + m.Broken + m.Fell >= 2)
                w.Automation.Book.Add(ActKind.Check, null, $"{m.Talk} 뒤 — 떨어짐 {m.Dropped} · 깨짐 {m.Broken} · 넘어짐 {m.Fell} · 버틴 고정 {m.Fast}",
                    m.Announced ? "준비할 틈이 있었는데도 걸쇠를 안 건 선반이 있었다" : "예고 없이 맞았다 — 평소에 고정해 둔 것만 남았다",
                    "떨어진 곳을 알렸다", "다음엔 그 선반부터 잠가 달라", "maneuver:after", SimTime.Minutes(5));
        }
        if (LossEvents >= 3 && w.Culture.Of(CustomKind.StowAway) == null)
        {
            var founder = Felt.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => CrewOf(kv.Key)).FirstOrDefault(c => c != null && !c.Dead);
            w.Culture.Adopt(CustomKind.StowAway, $"기동 · 충격 {LossEvents}번에 그릇이 깨지고 사람이 넘어졌다", founder?.Name);
            Stats.Customs++;
            if (founder != null) founder.Say(w, Persona.Say(founder, "이제부터 쓰고 나면 걸쇠 걸고, 카트는 끈 묶어 두자. 또 주워 담기 싫어"));
        }
        if (Recent.Count > 0 && m.Kind == ManeuverKind.Evasion)
            foreach (var c in w.Crew.Where(c => m.Knew.Contains(c.Id) && !c.Dead).OrderBy(c => c.Id))
                Felt[c.Id] = Felt.GetValueOrDefault(c.Id) + (m.Dropped + m.Fell > 0 ? 1 : 0);
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var w = _w;
        // 침대 끈: 습관이 있는 사람은 자기 전에 스스로 채운다 · 깨면 푼다
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            if (c.Pose == Pose.Sleeping)
            {
                if (BedUnder(c) is Furniture bed && !Bunks.Contains(bed.Id) && StrapHabit(c)) { Bunks.Add(bed.Id); Stats.BunksHabit++; }
            }
        }
        if (Bunks.Count > 0)
            foreach (var id in Bunks.ToList())
            {
                var bed = w.Ship.Furniture.FirstOrDefault(f => f.Id == id);
                if (bed == null || !w.Crew.Any(c => !c.Dead && c.Pose == Pose.Sleeping && (bed.Center - c.Position).LengthSquared() < 1.3f)) Bunks.Remove(id);
            }
        // 끈을 맨 카트를 누가 끌고 가면 푼다 · 꺼 둔 히터가 다시 켜졌으면 잊는다
        if (Strapped.Count > 0) foreach (var d in w.Portable.Devices) if (Strapped.Contains(d.Id) && !d.Placed) Strapped.Remove(d.Id);
        if (HeaterOffBy.Count > 0) foreach (var d in w.Portable.Devices) if (HeaterOffBy.ContainsKey(d.Id) && (d.On || !d.Placed)) HeaterOffBy.Remove(d.Id);
        if (Tipped.Count > 0) foreach (var d in w.Portable.Devices) if (Tipped.ContainsKey(d.Id) && !d.Placed) Tipped.Remove(d.Id);
        if (Rolled.Count > 0) foreach (var d in w.Portable.Devices) if (Rolled.ContainsKey(d.Id) && !d.Placed) Rolled.Remove(d.Id);
        // 넘어진 사람은 몇 분 뒤 일어난다 · 오래된 자국은 지운다
        foreach (var t in Tumbles) if (!t.Up && w.Tick - t.Tick > SimTime.Minutes(6)) t.Up = true;
        Tumbles.RemoveAll(t => w.Tick - t.Tick > SimTime.Hours(1));
        Splashes.RemoveAll(s => s.Kind == SplashKind.Soup ? w.Body.Mark(s.At, CellMark.Wet) < 0.05f && w.Tick - s.Tick > SimTime.Minutes(30) : w.Tick - s.Tick > SimTime.Hours(s.Kind == SplashKind.Pills ? 6 : 4));
        Shards.RemoveAll(s => w.Body.Mark(s.At, CellMark.Glass) < 0.05f || w.Tick - s.Tick > SimTime.TicksPerDay * 2);
        // 걸쇠: 관행이 없으면 몇 시간 뒤 쓰면서 풀린다
        if (Latched.Count > 0 && w.Culture.Of(CustomKind.StowAway) == null && Pending == null && w.Tick % SimTime.Hours(1) < World.SystemInterval)
            foreach (var id in Latched.ToList()) if (R.Chance(0.15f)) Latched.Remove(id);
        Warm(dt);
        Thanks();
        if (w.Tick >= _nextTrim) { _nextTrim = w.Tick + SimTime.Hours(1); Trim(); Broom(); }
        if (w.Tick >= _nextWarm) { _nextWarm = w.Tick + SimTime.Hours(1); NightSoup(); }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    private bool StrapHabit(CrewMember c) =>
        _w.Culture.Follows(c, CustomKind.StowAway) || Life.Has(c, Habit.Worrier) || Life.Has(c, Habit.Methodical) || Felt.GetValueOrDefault(c.Id) >= 3;

    // ── 데우는 냄비 (야간 근무자 몫) ──

    /// <summary>조리사가 누구 몫 수프를 화구에 올려 데운다.</summary>
    public WarmPot? Warm(CrewMember cook, Furniture stove, CrewMember forWhom, Batch b)
    {
        if (Warming.Any(x => x.Stove == stove.Id && !x.Stopped)) return null;
        var x = new WarmPot { Stove = stove.Id, Batch = b.Id, For = forWhom.Id, Cook = cook.Id, Since = _w.Tick };
        Warming.Add(x);
        if (Warming.Count > 10) Warming.RemoveAt(0);
        _w.Log.Add(_w.Tick, LogKind.Life, $"{Ko.IGa(cook.Name)} {forWhom.Name} 몫 {Ko.EulReul(b.Spec.Name)} 화구에 올려 데운다", cook.Id);
        return x;
    }

    private void Warm(float dt)
    {
        var w = _w;
        foreach (var x in Warming)
        {
            if (x.Stopped || x.Plate >= 0) continue;
            var b = w.Cooking.Batches.FirstOrDefault(y => y.Id == x.Batch);
            var st = w.Ship.Furniture.FirstOrDefault(f => f.Id == x.Stove);
            if (b == null || st == null || b.Portions <= 0) { x.Stopped = true; continue; }
            if (Clamped.Contains(st.Id)) continue; // 집게를 물리고 불을 껐다 — 식어 간다
            var cook = CrewOf(x.Cook);
            bool tending = cook != null && !cook.Dead && cook.Room == st.Room && cook.IsAwake;
            if (tending && st.Room.Powered) b.Temp = MathF.Min(78f, b.Temp + 90f * dt);
            if (tending && b.Temp >= 70f && w.Cooking.PlateFor(CrewOf(x.For)!) == null && st.Room == cook!.Room && Pending == null && w.Tick - x.Since > SimTime.Minutes(10))
                Serve(x, cook, b);
        }
        Warming.RemoveAll(x => x.Stopped && w.Tick - x.Since > SimTime.Hours(12));
    }

    /// <summary>다 데웠거나(기동 뒤 식었거나) 이름표를 붙여 덜어 둔다.</summary>
    internal bool Serve(WarmPot x, CrewMember cook, Batch b)
    {
        var w = _w;
        if (CrewOf(x.For) is not CrewMember who || who.Dead) { x.Stopped = true; return false; }
        if (w.Cooking.TableFor(b) is not Furniture table) return false;
        if (!w.Cooking.SetAside(cook, who, b, table)) { x.Stopped = true; return false; }
        x.Dish = w.Cooking.Plates[^1];
        x.Plate = x.Dish.Id;
        x.Stopped = true;
        return true;
    }

    private void Thanks()
    {
        var w = _w;
        foreach (var x in Warming)
        {
            if (x.Plate < 0 || !x.Kept) continue;
            var p = x.Dish;
            if (p == null || !p.Found) continue; // 이름표를 읽는 순간 안다
            var who = CrewOf(x.For);
            var cook = CrewOf(x.KeptBy >= 0 ? x.KeptBy : x.Cook);
            x.Plate = -2;
            if (who == null || cook == null || who.Dead || cook.Dead) continue;
            Stats.Thanked++;
            w.Relations.Remember(who, cook, RelationReason.SavedMyThing, "기동 경보 속에서도 내 몫 수프 냄비를 붙잡아 뒀다");
            who.ChangeAffinity(cook, 0.05f);
            string line = Persona.Say(who, $"{cook.Name}, 기동 때 냄비 붙잡아 줬다며. 식었어도 이게 어디야 — 고마워");
            if (cook.Room == who.Room && cook.IsAwake) { who.Say(w, line); cook.Say(w, Persona.Say(cook, "쏟았으면 오늘 밤 굶을 뻔했지")); }
            else w.Info.Chat.Post(who, ChatKind.Thanks, line);
            Life.Diary(w, who, Persona.Say(who, $"이름표 붙은 식은 수프. 회피 기동 때 {Ko.IGa(cook.Name)} 집게를 물려 지켜 낸 냄비였다고 한다"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(who.Name)} 기동 중에 제 몫 수프를 지켜 준 {cook.Name}에게 고맙다고 했다", who.Id);
        }
    }

    /// <summary>밤: 당직이 끼니를 거르고 있으면 조리사가 남은 수프를 데워 둔다.</summary>
    private void NightSoup()
    {
        var w = _w;
        float h = SimTime.HourOfDay(w.Tick);
        if (h is > 4f and < 21f || Pending != null || Warming.Any(x => !x.Stopped)) return;
        var cook = w.Cooking.HeadCook;
        if (cook == null || cook.Dead || !cook.CanAct || !cook.IsAwake || cook.Job?.Urgent == true) return;
        var watch = w.Crew.Where(c => !c.Dead && c != cook && c.IsAwake && c.Needs.Food < 0.45f && c.Job?.Activity?.Id is "duty" or "patrol").OrderBy(c => c.Id).FirstOrDefault();
        if (watch == null || w.Cooking.PlateFor(watch) != null) return;
        var b = w.Cooking.Batches.Where(x => !x.Jar && !x.Spoiled && x.Portions > 0 && x.Spec.Hot).OrderByDescending(x => x.Cooked).FirstOrDefault();
        var st = w.Ship.FurnitureOf(FurnitureType.Stove).FirstOrDefault(f => !f.Room.Detached && !f.Room.OffLimits && f.Room.Powered);
        if (b == null || st == null) return;
        Warm(cook, st, watch, b);
    }

    // ── 화물 무게중심 ──

    private static float Weight(ItemKind k) => k switch { ItemKind.Plate => 18f, ItemKind.Meal => 6f, _ => 9f };

    /// <summary>한 시간마다: 화물 무게중심이 배 가운데에서 얼마나 벗어났나 → 기동할 때 추력 배율.</summary>
    public void Trim()
    {
        var w = _w;
        var cells = 0; var sum = Vector2.Zero;
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) continue;
            foreach (var c in r.Cells) { sum += c.Center; cells++; minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X); minY = Math.Min(minY, c.Y); maxY = Math.Max(maxY, c.Y); }
        }
        if (cells == 0) return;
        ShipCenter = sum / cells;
        float kg = 0f; var mom = Vector2.Zero;
        foreach (var f in w.Ship.Containers.OrderBy(f => f.Id))
        {
            float m = 0f;
            foreach (var (k, n) in f.Storage!.Contents) m += Weight(k) * n;
            kg += m; mom += f.Center * m;
        }
        CargoKg = kg;
        if (kg < 1f) { Imbalance = 0f; TrimMul = 1f; return; }
        CargoCenter = mom / kg;
        float half = MathF.Max(3f, 0.5f * MathF.Max(maxX - minX + 1, maxY - minY + 1));
        float dry = 45f * cells;
        float off = (CargoCenter - ShipCenter).Length();
        Imbalance = Math.Clamp(kg / (kg + dry) * off / half * 8f, 0f, 1f);
        TrimMul = 1f - 0.4f * Math.Clamp((Imbalance - 0.12f) / 0.5f, 0f, 1f); // 조금 쏠린 건 조종이 잡는다
        Rebalance = null;
        if (TrimMul < 0.94f) Advise();
    }

    private void Advise()
    {
        var w = _w;
        var axis = CargoCenter - ShipCenter;
        if (axis.LengthSquared() < 0.01f) return;
        var dir = Vector2.Normalize(axis);
        var boxes = w.Ship.Containers.Where(f => f.Storage!.Total > 0 && !f.Room.OffLimits).OrderByDescending(f => Vector2.Dot(f.Center - ShipCenter, dir)).ThenBy(f => f.Id).ToList();
        var light = w.Ship.Containers.Where(f => f.Storage!.Free > 4 && !f.Room.OffLimits).OrderBy(f => Vector2.Dot(f.Center - ShipCenter, dir)).ThenBy(f => f.Id).FirstOrDefault();
        var heavy = boxes.FirstOrDefault();
        if (heavy == null || light == null || heavy == light || Vector2.Dot(light.Center - ShipCenter, dir) >= 0f) return;
        int n = Math.Min(8, heavy.Storage!.Total);
        Rebalance = (heavy.Id, light.Id, n);
        string side = MathF.Abs(dir.X) > MathF.Abs(dir.Y) ? (dir.X > 0 ? "오른쪽" : "왼쪽") : (dir.Y > 0 ? "아래쪽" : "위쪽");
        var act = w.Automation.Book.Add(ActKind.Advice, heavy.Room, $"화물 무게중심이 배 가운데에서 {side}으로 {(CargoCenter - ShipCenter).Length():0.0}칸 (화물 {CargoKg:0}kg)",
            $"기동하면 배가 돌아 추력이 {(1f - TrimMul) * 100:0}% 준다 — 운석을 다 못 비킬 수 있다", $"{heavy.Room.Name} 짐 {n}개를 {light.Room.Name}로 옮기자고 했다", "짐 옮기기", "maneuver:trim", SimTime.Hours(8));
        if (act != null) Stats.TrimWarns++;
    }

    /// <summary>짐을 옮겼다 (사람이 날라 왔다).</summary>
    internal int MoveCargo(Furniture from, Furniture to, int n)
    {
        int moved = 0;
        foreach (var (k, cnt) in from.Storage!.Contents.ToList())
        {
            if (moved >= n) break;
            int take = from.Storage.Take(k, Math.Min(cnt, n - moved));
            int put = to.Storage!.Add(k, take);
            if (put < take) from.Storage.Add(k, take - put);
            moved += put;
        }
        if (moved > 0) { Stats.Rebalanced++; Rebalance = null; Trim(); }
        return moved;
    }

    /// <summary>ShipBody 훅: 식당 · 주방의 기동 조각은 밥 먹은 사람이 빗자루로 쓴다 (네 시간 안엔 손보기 목록에서 뺀다).</summary>
    public bool Leaves(Cell c)
    {
        if (Shards.Count == 0) return false;
        foreach (var s in Shards)
            if (s.At == c && _w.Tick - s.Tick < SimTime.Hours(4) && _w.Ship.RoomAt(c)?.Type is RoomType.Mess or RoomType.Galley) return true;
        return false;
    }

    /// <summary>빗자루 걸어 두는 곳 (식당 벽 · 없으면 주방).</summary>
    public Cell? Broom()
    {
        if (BroomHome is Cell h) return h;
        var w = _w;
        var room = w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault(r => !r.Detached) ?? w.Ship.RoomsOf(RoomType.Galley).FirstOrDefault(r => !r.Detached);
        if (room == null) return null;
        foreach (var c in room.Cells.OrderBy(c => c.Y).ThenByDescending(c => c.X))
            if (w.Ship.IsOpenFloor(c) && Cell.Dirs4.Any(d => w.Ship.Grid.Kind(c + d) == TileKind.Wall) && !room.Doors.Any(d => Math.Abs(d.Cell.X - c.X) + Math.Abs(d.Cell.Y - c.Y) <= 1))
            { BroomHome = c; return c; }
        return null;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Recent.Count); I(Pending?.Id ?? -1); I(Fallen.Count); I(Shards.Count); I(Splashes.Count); I(Tumbles.Count);
        I(Latched.Count); I(Strapped.Count); I(Clamped.Count); I(Bunks.Count); I(Tipped.Count); I(Rolled.Count); I(LossEvents); I(Warming.Count);
        I(Stats.Dropped); I(Stats.Fell); I(Stats.Picked); I(Stats.Swept); I(Stats.Rebalanced); F(TrimMul); I(BroomBy);
        foreach (var t in Fallen) { I(t.At.X); I(t.At.Y); I((int)t.Kind); }
    }
}

