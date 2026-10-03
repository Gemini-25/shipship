using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.8 실제 음식: 수확한 채소와 창고의 재료가 레시피(Dishes.cs)대로 냄비가 된다 —
// 누가 · 얼마나 잘 · 언제 만들었고 · 지금 몇 도이고 · 몇 접시 남았는지. 냄비 하나가 신선도를 가진 묶음이고, 집어 들 때 접시로 나뉜다 (성능).
// 늦게 오는 사람 몫은 이름표를 붙여 덜어 둔다 · 식은 것은 데워 먹는다(전기가 모자라면 차갑게) · 어제 남은 것은 다음 날 수프로 ·
// 채소가 넉넉하면 항아리에 김치 · 절임을 앉혀 며칠 기다린다 · 기항지에서 고향 재료를 산다 ·
// 조리사가 다치면 배우던 사람이 대신 서고, 먹는 사람은 맛이 달라진 걸 알아챈다 · 배급 중에는 나누는 방식에 불만이 나온다.
// 식량 재고(식사 개수)는 그대로 세고, 냄비는 그 식사들이 "무엇이었나"를 붙인다 — 냄비가 없으면 예전처럼 보존식 · 비상식량.
// 더러운 손 · 지저분한 주방(Soil)은 냄비에 균을 넣고(식중독) · 기름진 요리는 화구 앞 바닥에 기름을 튀긴다(배 본체 칸 상태 — 미끄러짐) ·
// 정전된 식당에 이동식 히터가 돌면 그 앞에서 그릇을 데운다 · 주컴퓨터(FoodWatch.cs)가 신선도 · 냉장고 · 식단 · 화구를 지켜본다.

public sealed class Batch
{
    public int Id { get; init; }
    public int Recipe { get; init; }
    public int Cook { get; init; } = -1;
    public string CookName { get; init; } = "";
    public long Cooked { get; init; }
    public long ReadyAt { get; init; }
    public float Quality { get; set; }
    public float Temp { get; set; } = 85f;
    public int Portions { get; set; }
    public int Made { get; init; }
    public float Fresh { get; set; } = 1f;
    public bool InFridge { get; set; }
    public Furniture? Stove { get; init; }
    public Room? Room { get; set; }
    public string? Swap { get; init; }
    public bool Spoiled { get; set; }
    public long SpoiledAt { get; set; } = -1;
    public bool WasReady { get; set; }
    /// <summary>균이 들었다 (더러운 손 · 지저분한 주방) — 먹는 사람은 모른다.</summary>
    public bool Germy { get; set; }
    /// <summary>주컴퓨터가 "먼저 쓰라"고 알렸고 조리사가 받아들였다 · 흘려들었다.</summary>
    public bool Flagged { get; set; }
    public bool FlagIgnored { get; set; }
    /// <summary>만드는 걸 본 사람 (누가 했는지 안다).</summary>
    public List<int> SawCook { get; } = new();
    public DishRecipe Spec => Dishes.Of(Recipe);
    public bool Jar => Spec.Jar;
    public bool Ready(long tick) => tick >= ReadyAt;
}

/// <summary>이름표를 붙여 덜어 둔 접시 (늦게 오는 사람 몫).</summary>
public sealed class Plate
{
    public int Id { get; init; }
    public int Recipe { get; init; }
    public int Cook { get; init; } = -1;
    public float Quality { get; init; }
    public int For { get; init; }
    public int By { get; init; }
    public long SetAt { get; init; }
    public float Temp { get; set; }
    public Furniture Table { get; init; } = null!;
    public bool Found { get; set; }
    public bool Reheating { get; set; }
    /// <summary>먹기 시작할 때의 온도 (데운 뒤) — 다 먹을 즈음 식은 것으로 "식은 끼니"를 세지 않는다 (-1 = 아직).</summary>
    public float ServedTemp { get; set; } = -1f;
    public bool Eaten { get; set; }
    public bool Spoiled { get; set; }
    public DishRecipe Spec => Dishes.Of(Recipe);
}

public sealed class CookingStats
{
    public int Batches, Portions, Served, Prepacked, Reheated, Cold, ColdNoPower, SavedPlates, SavedEaten, TasteNoticed, Substituted, Leftovers;
    public int SourMeals, SpoilPoison, BurntPots, Shared, GermPots, GermMeals, OilSplash, HeaterWarm;
    public int Jars, JarsReady, Sides, HomeMeals, HomeGoodsBought, Spoiled, Scorched, ScorchCaught, ScorchFires, RationGripes, CoverCooks;
    public string Summary() =>
        $"냄비 {Batches}(접시 {Portions}) · 먹은 접시 {Served}(보존식 {Prepacked}) · 데움 {Reheated} · 차갑게 {Cold}(전기 모자람 {ColdNoPower}) · 남겨 둔 접시 {SavedPlates}(찾아 먹음 {SavedEaten})"
        + $" · 맛이 달라진 걸 알아챔 {TasteNoticed} · 대체 재료 {Substituted} · 남은 것으로 {Leftovers} · 항아리 {Jars}(익음 {JarsReady} · 곁들임 {Sides}) · 고향 음식 {HomeMeals}(재료 {HomeGoodsBought})"
        + $" · 상함 {Spoiled}(시큼한 끼니 {SourMeals} · 식중독 {SpoilPoison}) · 균 든 냄비 {GermPots}(균 든 끼니 {GermMeals}) · 바닥 기름 {OilSplash} · 히터 앞에서 데움 {HeaterWarm} · 불에 탄 냄비 {BurntPots} · 맛보기 나눔 {Shared} · 불 위에 남은 냄비 {Scorched}(먼저 내림 {ScorchCaught} · 불이 남 {ScorchFires}) · 배급 불만 {RationGripes} · 대신 선 부엌 {CoverCooks}";
}

public sealed class CookingSystem
{
    private sealed class Palate
    {
        public float Usual = -1f;
        public int Count;
        public long Noticed = -1_000_000;
        public readonly SortedDictionary<int, int> ByCook = new();
        public int UsualCook()
        {
            int best = -1, n = 0;
            foreach (var (k, v) in ByCook) if (v > n) { best = k; n = v; }
            return best;
        }
    }

    private sealed class Serving
    {
        public int Recipe = -1;
        public bool Reheat, Cold, Heater;
    }

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 211));
    public List<Batch> Batches { get; } = new();
    public List<Plate> Plates { get; } = new();
    public CookingStats Stats { get; } = new();
    /// <summary>기항지에서 산 고향 재료 (레시피마다 몫).</summary>
    public int[] HomeGoods { get; } = new int[Dishes.All.Length];
    /// <summary>시험용: 다음에 앉힐 냄비를 정해 둔다.</summary>
    public string? ForceNext { get; set; }

    private readonly Dictionary<int, int> _planned = new();
    private readonly HashSet<int> _forced = new();
    private readonly Dictionary<int, (Furniture stove, int recipe, int cook, long start)> _cooking = new();
    private readonly Dictionary<int, (Furniture stove, long since, int cook)> _burner = new();
    private readonly Dictionary<int, long> _lastAte = new();
    private readonly Dictionary<int, long> _homeTold = new(); // 제 고향 음식을 했다는 말을 들은 사람 · 때
    private readonly Dictionary<int, Palate> _palate = new();
    private readonly Dictionary<int, Serving> _eating = new();
    private readonly Dictionary<int, long> _coldSaid = new();
    private readonly List<(int saver, int forWhom, int batch, long until)> _requests = new();
    private readonly List<int> _recent = new();
    private int _nextId, _head = -1, _apprentice = -1;
    private long _cooksAt = -1;
    private float _acc;
    private long _lastGripe = -1_000_000;

    /// <summary>주컴퓨터의 음식 감시 (신선도 · 냉장고 · 식단 · 화구).</summary>
    public FoodWatch Watch { get; }

    public CookingSystem(World w) { _w = w; Watch = new FoodWatch(w); }

    // ── 누가 부엌을 맡나 ──

    private void RefreshCooks()
    {
        if (_cooksAt == _w.Tick) return;
        _cooksAt = _w.Tick;
        CrewMember? head = null, appr = null;
        foreach (var c in _w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            if (head == null || Better(c, head)) { appr = head; head = c; }
            else if (appr == null || Better(c, appr)) appr = c;
        }
        // 배우던 사람은 쉽게 바뀌지 않는다 (곁에서 보며 배워 온 사람 — 훨씬 잘하는 사람이 나타나야)
        if (CrewOf(_apprentice) is CrewMember old && !old.Dead && old != head && appr != null && appr != old
            && appr.RawSkill(Skill.Cooking) < old.RawSkill(Skill.Cooking) + 0.45f) appr = old;
        _head = head?.Id ?? -1;
        _apprentice = appr?.Id ?? -1;
        static bool Better(CrewMember a, CrewMember b)
        {
            int ra = a.Role == CrewRole.Cook ? 1 : 0, rb = b.Role == CrewRole.Cook ? 1 : 0;
            if (ra != rb) return ra > rb;
            float sa = a.RawSkill(Skill.Cooking), sb = b.RawSkill(Skill.Cooking);
            return sa != sb ? sa > sb : a.Id < b.Id;
        }
    }

    private CrewMember? CrewOf(int id) => id >= 0 && id < _w.Crew.Count ? _w.Crew[id] : null;
    /// <summary>배의 조리사 (조리 담당 · 없으면 가장 잘하는 사람).</summary>
    public CrewMember? HeadCook { get { if (_head < 0) RefreshCooks(); return CrewOf(_head); } }
    /// <summary>조리사 곁에서 배우던 사람 (다음으로 잘하는 사람).</summary>
    public CrewMember? Apprentice { get { if (_head < 0) RefreshCooks(); return CrewOf(_apprentice); } }
    public static bool LaidUp(CrewMember c) => c.Dead || c.Down || c.CareBed != null || c.Vitals.Injury >= 0.3f;

    /// <summary>조리 일감의 끌림 보정: 다친 조리사는 부엌에 서지 않고, 배우던 사람이 대신 선다.</summary>
    public float CookBias(CrewMember c)
    {
        var head = HeadCook;
        if (head == null) return 0f;
        if (c == head && LaidUp(c)) return -0.4f;
        if (!LaidUp(head)) return 0f;
        if (c.Id == _apprentice) return 0.45f;
        // 배우던 사람이 깨어 있으면 다른 사람은 한 발 물러선다 (그 사람이 부엌을 잇는다)
        return Apprentice is CrewMember ap && ap != c && !LaidUp(ap) && ap.IsAwake ? -0.15f : 0f;
    }

    // ── 무엇을 만드나 ──

    private int Stock(ItemKind k) => _w.Ship.CountStored(k);
    private static bool HasOven(Room r) => r.Furniture.Any(f => f.Type == FurnitureType.Oven && (f.Machine == null || f.Machine.Efficiency > 0.1f));

    private Batch? OldLeftover()
    {
        Batch? best = null;
        foreach (var b in Batches)
            if (!b.Jar && !b.Spoiled && b.Portions >= 2 && b.Flagged && (best == null || b.Fresh < best.Fresh)) best = b; // 주컴퓨터가 먼저 쓰라고 한 냄비
        if (best != null) return best;
        foreach (var b in Batches)
            if (!b.Jar && !b.Spoiled && b.Portions >= 2 && _w.Tick - b.Cooked > SimTime.Hours(14) && (best == null || b.Cooked < best.Cooked)) best = b;
        return best;
    }

    private Batch? ReadyJar(string? id = null)
    {
        foreach (var b in Batches)
            if (b.Jar && !b.Spoiled && b.Portions > 0 && b.Ready(_w.Tick) && (id == null || b.Spec.Id == id)) return b;
        return null;
    }

    private int Choose(Furniture stove, CrewMember? cook)
    {
        if (ForceNext != null && Dishes.IndexOf(ForceNext) is int f and >= 0) { ForceNext = null; return f; }
        int hour = (int)SimTime.HourOfDay(_w.Tick);
        bool morning = hour >= 5 && hour < 10;
        var left = OldLeftover();
        if (left is { Flagged: true })
        {
            // 주컴퓨터가 먼저 쓰라고 한 냄비 — 받아들인 조리사는 남은 것 요리로 돌린다
            var opts = new List<int>();
            for (int i = 0; i < Dishes.All.Length; i++) if (Dishes.All[i].Uses(Ingredient.Leftover)) opts.Add(i);
            if (opts.Count > 0) { int pickL = opts[R.Range(0, opts.Count)]; Watch.Chosen(pickL); return pickL; }
        }
        var home = cook != null ? Dishes.HomeDish(_w, cook) : null;
        int spice = Stock(ItemKind.Spice), ration = Stock(ItemKind.Ration), coffee = Stock(ItemKind.Coffee) + Stock(ItemKind.TeaLeaf);
        var wts = new float[Dishes.All.Length];
        float sum = 0f;
        for (int i = 0; i < Dishes.All.Length; i++)
        {
            var r = Dishes.All[i];
            if (r.Jar) continue;
            float wt = 1f;
            if (r.Uses(Ingredient.Leftover)) wt = left != null ? 7f : 0f;
            if (r.Uses(Ingredient.HomeGoods)) wt = HomeGoods[i] > 0 ? 6f : 0f;
            if (r.Uses(Ingredient.Kimchi)) wt *= ReadyJar("kimchi") != null ? 3f : 0.15f;
            if (r.Uses(Ingredient.Ration)) wt *= ration >= 4 ? 0.35f : 0f;
            if (r.Uses(Ingredient.Spice) && spice == 0) wt *= 0.4f;
            if (r.Uses(Ingredient.Coffee) && coffee == 0) wt *= 0.3f;
            if (morning) wt *= r.Kind is DishKind.Bread or DishKind.Pan or DishKind.Porridge or DishKind.Cake ? 3f : 0.6f;
            else if (r.Kind is DishKind.Soup or DishKind.Stew or DishKind.Noodle) wt *= 1.5f;
            if (r == home) wt *= 1.5f; // 조리사는 제 고향 음식을 자주 한다
            wt *= Watch.MenuWeight(i); // 주컴퓨터 식단
            if (_recent.Contains(i)) wt *= 0.25f;
            wts[i] = wt;
            sum += wt;
        }
        if (sum <= 0f) return 0;
        float pick = R.Float() * sum;
        for (int i = 0; i < wts.Length; i++)
        {
            pick -= wts[i];
            if (wts[i] > 0f && pick <= 0f) { Watch.Chosen(i); return i; }
        }
        return 0;
    }

    /// <summary>조리 일감을 짤 때: 무엇을 만들지 정하고, 레시피의 조리 시간 배율을 돌려준다.</summary>
    public float HoursMul(Furniture stove, CrewMember c)
    {
        // 정해 둔 것(시험 · 사건)은 일감을 여러 번 짜도 그대로
        bool forced = ForceNext != null && Dishes.IndexOf(ForceNext) >= 0;
        int ri = !forced && _forced.Contains(stove.Id) && _planned.TryGetValue(stove.Id, out var keep) ? keep : Choose(stove, c);
        if (forced) _forced.Add(stove.Id);
        _planned[stove.Id] = ri;
        var r = Dishes.Of(ri);
        float h = r.Hours * (r.Station == CookStation.Oven && !HasOven(stove.Room) ? 1.1f : 1f);
        return h / FoodChain.CookHours * TechWeb.Mul(_w, "cook.hours"); // v16.14 압력 조리
    }

    /// <summary>지금 이 화구에서 만드는 것 (없으면 null).</summary>
    public DishRecipe? CookingAt(Furniture stove) => _cooking.TryGetValue(stove.Id, out var x) ? Dishes.Of(x.recipe) : null;

    private bool UseIngredient(Ingredient what, int n, int recipe)
    {
        switch (what)
        {
            case Ingredient.Leftover: return OldLeftover() != null;
            case Ingredient.HomeGoods:
                if (HomeGoods[recipe] < n) return false;
                HomeGoods[recipe] -= n;
                return true;
            case Ingredient.Kimchi:
                if (ReadyJar("kimchi") is not Batch jar) return false;
                jar.Portions -= Math.Min(jar.Portions, n * 2);
                return true;
            default:
                if (Dishes.Item(what) is not ItemKind k || !Life.Take(_w, k, n)) return false;
                if (k == ItemKind.Ration) _w.FoodSources.Note(FoodSrc.Stored, n); // v16.22 저장 식량을 섞어 쓴다
                return true;
        }
    }

    /// <summary>조리 일감이 끝났을 때 (Chores 조리 훅): 냄비 하나가 생긴다.</summary>
    public void OnCooked(CrewMember cm, Furniture stove, int cooked)
    {
        var w = _w;
        int ri = ForceNext != null && Dishes.IndexOf(ForceNext) >= 0 ? Choose(stove, cm) // 시험 · 사건이 정해 둔 것이 먼저
            : _planned.TryGetValue(stove.Id, out var p) ? p : _cooking.TryGetValue(stove.Id, out var cn) ? cn.recipe : Choose(stove, cm);
        _planned.Remove(stove.Id);
        _forced.Remove(stove.Id);
        _cooking.Remove(stove.Id);
        var r = Dishes.Of(ri);
        float q = 0.32f + 0.58f * cm.SkillLevel(Skill.Cooking) + R.Range(-0.03f, 0.03f);
        var swaps = new List<string>(2);
        bool leftover = false;
        foreach (var (what, n) in r.Needs)
        {
            if (n <= 0) continue;
            if (UseIngredient(what, n, ri))
            {
                if (what == Ingredient.HomeGoods) q += 0.05f;
                if (what == Ingredient.Leftover) leftover = true;
                continue;
            }
            var (sub, pen) = Dishes.Substitute(what);
            if (sub is Ingredient s && s != Ingredient.Veg && UseIngredient(s, 1, ri)) swaps.Add($"{Dishes.Name(what)} 대신 {Dishes.Name(s)}");
            else swaps.Add($"{Dishes.Name(what)} 없이");
            q -= pen;
            Stats.Substituted++;
        }
        if (r.Station == CookStation.Oven && !HasOven(stove.Room)) { q -= 0.05f; swaps.Add("오븐이 없어 팬에"); }
        int absorbed = 0;
        if (leftover && OldLeftover() is Batch old)
        {
            absorbed = old.Portions;
            Batches.Remove(old);
            Stats.Leftovers++;
            Watch.Used(cm, old, r);
        }
        var b = new Batch
        {
            Id = ++_nextId, Recipe = ri, Cook = cm.Id, CookName = cm.Name, Cooked = w.Tick, ReadyAt = w.Tick,
            Quality = Curve.Clamp01(q), Temp = r.Hot ? 88f : 55f, Portions = cooked + absorbed, Made = cooked + absorbed,
            Stove = stove, Room = stove.Room, Swap = swaps.Count > 0 ? string.Join(" · ", swaps) : null,
        };
        foreach (var o in w.Crew)
            if (!o.Dead && o.Room == stove.Room && o.IsAwake) b.SawCook.Add(o.Id);
        Germs(cm, stove, b);
        Grease(cm, stove, r);
        Batches.Add(b);
        _recent.Add(ri);
        if (_recent.Count > 3) _recent.RemoveAt(0);
        Stats.Batches++;
        Stats.Portions += cooked;
        w.Log.Add(w.Tick, LogKind.Work, $"오늘은 {r.Name}" + (absorbed > 0 ? $" (어제 남은 {absorbed}인분을 넣고)" : "") + (b.Swap != null ? $" — {b.Swap}" : ""), cm.Id);
        TellHome(cm, r);
        var head = HeadCook;
        if (head != null && head != cm && LaidUp(head))
        {
            Stats.CoverCooks++;
            Life.Diary(w, cm, Persona.Say(cm, $"{Ko.IGa(head.Name)} 다쳐서 내가 부엌에 섰다. {Ko.EulReul(r.Name)} 했는데 어떨지"));
        }
        MaybeStartJar(cm);
    }

    /// <summary>고향 음식을 했다: 그 음식이 고향 맛인 사람에게 알린다 (곁에 있으면 말로 · 멀면 주컴퓨터 개인 메시지로) — 들은 사람은 배가 덜 고파도 한 그릇 먹으러 온다.</summary>
    private void TellHome(CrewMember cook, DishRecipe r)
    {
        if (!r.Home || r.Jar) return;
        var w = _w;
        bool board = w.Automation.Present && w.Automation.MainOnline;
        foreach (var o in w.Crew)
        {
            if (o.Dead || o == cook || o.Outside || Dishes.HomeDish(w, o) != r) continue;
            if (o.Room == cook.Room && o.IsAwake) cook.Say(w, Persona.Say(cook, $"{o.Name}, {r.Name} 했어 — 고향 음식"));
            else if (board) w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, o.Id, "식사", $"{cook.Name}: {r.Name} 했어요 — 고향 음식 · 식기 전에 와요"));
            else continue; // 말을 전할 길이 없다 (주컴퓨터가 꺼졌다) — 모른다
            _homeTold[o.Id] = w.Tick;
        }
    }

    /// <summary>EatActivity 훅: 제 고향 음식을 했다는 말을 들었고 그 냄비가 아직 있으면 — 배가 덜 고파도 한 그릇 (점수 더함).</summary>
    public float HomeCraving(CrewMember c)
    {
        if (_homeTold.Count == 0 || !_homeTold.TryGetValue(c.Id, out var t) || _w.Tick - t > SimTime.Hours(10) || c.Needs.Hunger < 0.12f) return 0f;
        var mine = Dishes.HomeDish(_w, c);
        foreach (var b in Batches)
            if (b.Spec == mine && !b.Jar && !b.Spoiled && b.Portions > 0 && b.Fresh >= 0.45f) return 0.5f + 0.4f * c.Needs.Hunger;
        return 0f;
    }

    /// <summary>균: 더러운 손(Soil이 방금 센 몫)이나 지저분한 주방 바닥 · 조리대의 균이 냄비에 든다 — 그 냄비에서 나간 끼니가 탈을 낸다.</summary>
    private void Germs(CrewMember cm, Furniture stove, Batch b)
    {
        var w = _w;
        if (cm.CarryTaint > 0) b.Germy = true;
        float bio = w.Soil.RoomSoil(stove.Room)[(int)SoilKind.Bio];
        if (bio > 0.25f && R.Chance(MathF.Min(1f, bio * 1.3f)))
        {
            b.Germy = true;
            // 보관함에 들어간 그 냄비의 몇 끼에 균 (Hazards가 먹은 사람을 앓게 하고, 추적되면 버린다)
            var box = w.Ship.Containers.Where(f => f.Storage!.Count(ItemKind.Meal) > f.Storage.Tainted).OrderBy(f => f.Type == FurnitureType.Fridge ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();
            box?.Storage!.Taint(Math.Max(1, (int)(b.Portions * bio * 0.5f)));
            MarkLog.Add(stove.Room.Marks, w.Tick, $"지저분한 조리대에서 {b.Spec.Name}");
        }
        if (b.Germy) Stats.GermPots++;
    }

    /// <summary>기름진 요리(전 · 볶음): 화구 앞 바닥에 기름이 튄다 — 솜씨가 없을수록 많이. 닦기 전엔 미끄럽고(배 본체) 방이 기름지다(Soil · 냄새).</summary>
    private void Grease(CrewMember cm, Furniture stove, DishRecipe r)
    {
        var w = _w;
        if (!r.Oily || stove.UseSpots.Count == 0) return;
        var spot = stove.UseSpots[0];
        float add = 0.12f + 0.22f * (1f - cm.SkillLevel(Skill.Cooking));
        w.Body.RaiseMark(spot, CellMark.Oil, MathF.Min(1f, w.Body.Mark(spot, CellMark.Oil) + add), $"{cm.Name}의 {r.Name} 기름 튐");
        var soil = w.Soil.RoomSoil(stove.Room);
        soil[(int)SoilKind.Oil] = MathF.Min(1f, soil[(int)SoilKind.Oil] + 0.04f);
        Stats.OilSplash++;
    }

    /// <summary>채소가 넉넉하면 항아리를 하나 앉힌다 (김치 · 절임 · 식혜 — 며칠 기다리는 음식).</summary>
    private void MaybeStartJar(CrewMember cm)
    {
        var w = _w;
        if (Batches.Any(b => b.Jar && !b.Spoiled && (!b.Ready(w.Tick) || b.Portions > 3))) return;
        if (Stock(ItemKind.Produce) < 16 || !R.Chance(0.35f * w.FoodSources.JarMul)) return; // v16.22 수경이 멎으면 남는 채소를 절여 둔다
        var options = new List<int>();
        for (int i = 0; i < Dishes.All.Length; i++)
            if (Dishes.All[i].Jar && (!Dishes.All[i].Uses(Ingredient.HomeGoods) || HomeGoods[i] > 0)) options.Add(i);
        if (options.Count == 0) return;
        int ri = options[R.Range(0, options.Count)];
        var r = Dishes.Of(ri);
        float q = 0.4f + 0.5f * cm.SkillLevel(Skill.Cooking);
        foreach (var (what, n) in r.Needs)
            if (n > 0 && !UseIngredient(what, n, ri)) q -= Dishes.Substitute(what).penalty;
        var fridge = w.Ship.FurnitureOf(FurnitureType.Fridge).FirstOrDefault();
        Batches.Add(new Batch
        {
            Id = ++_nextId, Recipe = ri, Cook = cm.Id, CookName = cm.Name, Cooked = w.Tick, ReadyAt = w.Tick + (long)r.Days * SimTime.TicksPerDay,
            Quality = Curve.Clamp01(q), Temp = 8f, Portions = 10, Made = 10, InFridge = true, Stove = fridge, Room = fridge?.Room ?? cm.Room,
        });
        Stats.Jars++;
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(cm.Name)} {r.Name} {Ko.EulReul(Dishes.Vessel(r))} 앉혔다 — {r.Days}일 뒤에 먹는다", cm.Id);
    }

    // ── 먹기 ──

    private Batch? PickBatch(CrewMember cm)
    {
        Batch? hot = null, old = null, home = null;
        var mine = Dishes.HomeDish(_w, cm);
        foreach (var b in Batches)
        {
            if (b.Jar || b.Portions <= 0 || b.Spoiled) continue;
            if (b.Spec == mine && b.Fresh >= 0.45f && (home == null || b.Cooked < home.Cooked)) home = b; // 고향 음식 냄비가 있으면 그것부터 뜬다
            if (!b.InFridge) { if (hot == null || b.Cooked < hot.Cooked) hot = b; } // 먼저 만든 냄비부터 비운다 (식었으면 데워서)
            else if (old == null || b.Cooked < old.Cooked) old = b;
        }
        return home ?? hot ?? old;
    }

    public bool PowerShort => _w.Power.Brownout || _w.Power.DeficitSince >= 0;

    /// <summary>정전 · 저출력: 근처(그 방 · 식당)에 도는 이동식 히터가 있으면 그 앞에 그릇을 놓고 데운다.</summary>
    public bool HeaterNear(Room? near)
    {
        var p = _w.Portable;
        foreach (var d in p.Devices)
        {
            if (d.Kind != PortableKind.Heater || !d.Running || !d.Placed || p.RoomOf(d) is not Room r) continue;
            if (r == near || r.Type == RoomType.Mess) return true;
        }
        return false;
    }

    /// <summary>데울 수 있나: 전기가 넉넉하고, 근처(그 방 · 주방)에 도는 화구 · 배식기 · 오븐이 있다 (아니면 이동식 히터 앞).</summary>
    public bool CanReheat(Room? near)
    {
        if (PowerShort) return HeaterNear(near);
        static bool Heater(Furniture f) => f.Type is FurnitureType.Stove or FurnitureType.MealDispenser or FurnitureType.Oven
                                           && f.Room.Powered && f.Machine is { } m && m.Efficiency > 0.2f;
        if (near != null && near.Furniture.Any(Heater)) return true;
        return _w.Ship.RoomsOf(RoomType.Galley).Any(r => r.Furniture.Any(Heater));
    }

    /// <summary>식사 훅 (EatActivity): 받아 든 식사가 어느 냄비의 몇 도짜리 접시인지 — 맛 · 온도 · 고향 맛 · 맛 차이.</summary>
    public void Serve(CrewMember cm, Furniture box, ItemKind kind)
    {
        var w = _w;
        _lastAte[cm.Id] = w.Tick;
        if (kind != ItemKind.Meal) { _eating.Remove(cm.Id); return; }
        var b = PickBatch(cm);
        if (b == null) { Stats.Prepacked++; _eating[cm.Id] = new Serving(); return; } // 냄비가 없으면 예전처럼 보존식
        b.Portions--;
        Stats.Served++;
        var r = b.Spec;
        bool need = r.Hot && b.Temp < 45f;
        bool reheat = need && CanReheat(box.Room);
        bool cold = need && !reheat;
        if (reheat && PowerShort) HeaterWarm(cm, r);
        // 균이 든 끼니였다 (방금 EatActivity가 탈 날 시각을 정했다) — 그 냄비에 균이 있었다
        if (cm.PoisonSource == box && cm.PoisonAt == w.Tick + SimTime.Minutes(40)) { b.Germy = true; Stats.GermMeals++; }
        float q = Taste(cm, b.Quality, b.Fresh, cold);
        _eating[cm.Id] = new Serving { Recipe = b.Recipe, Reheat = reheat, Cold = cold, Heater = reheat && PowerShort };
        if (cold) ColdNote(cm, r);
        Spoiling(cm, b, box);
        Side(cm);
        Home(cm, r, b.Cook, b.CookName);
        Notice(cm, b.Cook, b.CookName, b.SawCook.Contains(cm.Id), r, q);
    }

    /// <summary>신선도가 떨어진 냄비(냉장고가 멈췄던 · 오래 둔): 시큼한 맛 — 운이 나쁘면 식중독 (Hazards가 앓게 한다).</summary>
    private void Spoiling(CrewMember cm, Batch b, Furniture box)
    {
        if (b.Fresh >= 0.45f) return;
        Stats.SourMeals++;
        if (cm.PoisonAt < 0 && R.Chance(0.25f + 2f * (0.45f - b.Fresh)))
        {
            cm.PoisonAt = _w.Tick + SimTime.Minutes(40);
            cm.PoisonSource = box;
            Stats.SpoilPoison++;
        }
        Life.Diary(_w, cm, Persona.Say(cm, $"{Ko.EunNeun(b.Spec.Name)} 좀 시큼했다. 냉장고에 오래 있었나"));
    }

    private void HeaterWarm(CrewMember cm, DishRecipe r)
    {
        Stats.HeaterWarm++;
        if (_coldSaid.TryGetValue(cm.Id, out var t) && _w.Tick - t < SimTime.Hours(12)) return;
        _coldSaid[cm.Id] = _w.Tick;
        cm.Say(_w, Persona.Say(cm, $"히터 앞에 {Ko.EulReul(r.Name)} 잠깐 올려 둬야지"));
        _w.Log.Add(_w.Tick, LogKind.Life, $"전기가 모자란 식당 — {Ko.IGa(cm.Name)} 이동식 히터 앞에서 식은 {Ko.EulReul(r.Name)} 데웠다", cm.Id);
    }

    private float Taste(CrewMember cm, float quality, float fresh, bool cold)
    {
        float q = quality * (0.6f + 0.4f * fresh) - (cold ? 0.06f : 0f);
        cm.Needs.Stress = Math.Clamp(cm.Needs.Stress - 0.05f * (q - 0.45f), 0f, 1f);
        return q;
    }

    private void ColdNote(CrewMember cm, DishRecipe r)
    {
        Stats.Cold++;
        if (!PowerShort) return;
        Stats.ColdNoPower++;
        if (_coldSaid.TryGetValue(cm.Id, out var t) && _w.Tick - t < SimTime.TicksPerDay) return;
        _coldSaid[cm.Id] = _w.Tick;
        cm.Say(_w, Persona.Say(cm, $"전기 아껴야지 — 식은 {Ko.EulReul(r.Name)} 그냥 먹는다"));
        _w.Log.Add(_w.Tick, LogKind.Life, $"{Ko.IGa(cm.Name)} 전기가 모자라 식은 {Ko.EulReul(r.Name)} 데우지 않고 먹었다", cm.Id);
    }

    private void Side(CrewMember cm)
    {
        if (ReadyJar() is not Batch jar) return;
        jar.Portions--;
        Stats.Sides++;
        cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.01f);
        if (jar.Portions == jar.Made - 1)
            Life.Diary(_w, cm, Persona.Say(cm, $"{Ko.IGa(jar.CookName)} 앉혀 둔 {Ko.IGa(jar.Spec.Name)} 잘 익었다"));
    }

    private void Home(CrewMember cm, DishRecipe r, int cook, string cookName)
    {
        if (!r.Home || Dishes.HomeDish(_w, cm) != r) return;
        Stats.HomeMeals++;
        _homeTold.Remove(cm.Id);
        cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.06f);
        var by = cook != cm.Id ? CrewOf(cook) : null;
        Life.Diary(_w, cm, Persona.Say(cm, $"{r.Name} — 고향 맛이다" + (by != null ? $". {Ko.IGa(cookName)} 해 줬다" : "")));
        if (by != null && !by.Dead) _w.Relations.Remember(cm, by, RelationReason.GaveMeGift, $"고향 음식 {Ko.EulReul(r.Name)} 해 줬다");
    }

    private Palate PalateOf(CrewMember c)
    {
        if (!_palate.TryGetValue(c.Id, out var p)) _palate[c.Id] = p = new Palate();
        return p;
    }

    /// <summary>맛이 달라진 걸 알아챈다 — 늘 먹던 사람의 솜씨와 다르면. 누가 했는지는 본 사람만 안다.</summary>
    private void Notice(CrewMember cm, int cook, string cookName, bool saw, DishRecipe r, float q)
    {
        var w = _w;
        if (cook < 0) return;
        var pal = PalateOf(cm);
        int usual = pal.UsualCook();
        if (pal.Count >= 2 && usual >= 0 && cook != usual && cook != cm.Id && usual != cm.Id && MathF.Abs(q - pal.Usual) >= 0.08f && w.Tick - pal.Noticed > SimTime.Hours(8))
        {
            pal.Noticed = w.Tick;
            Stats.TasteNoticed++;
            var u = CrewOf(usual);
            string un = u?.Name ?? "조리사";
            bool better = q > pal.Usual;
            string text = saw ? $"오늘 {Ko.EunNeun(r.Name)} {cookName} 솜씨다 — {un} 맛이 아니다" + (better ? ". 이것도 괜찮다" : "")
                : u != null && LaidUp(u) ? $"{r.Name} 맛이 다르다. {Ko.IGa(un)} 다쳤다더니 다른 사람이 했나 보다"
                : $"{r.Name} 맛이 평소와 다르다 — 누가 했을까";
            Life.Diary(w, cm, Persona.Say(cm, text));
            cm.Say(w, Persona.Say(cm, better ? "어? 오늘 맛있네" : "어… 맛이 좀 다르네"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(cm.Name)} {r.Name} 맛이 달라진 걸 알아챘다 (늘 {un} · 오늘 {cookName})", cm.Id);
        }
        pal.Usual = pal.Usual < 0f ? q : pal.Usual + (q - pal.Usual) * 0.3f;
        pal.Count++;
        pal.ByCook[cook] = pal.ByCook.GetValueOrDefault(cook) + 1;
    }

    /// <summary>EatActivity가 받아 든 뒤에 붙인다: 식었으면 3분 데우고(전기가 넉넉할 때), 아니면 바로 넘어간다.</summary>
    public IEnumerable<Toil> ReheatToils() => new Toil[]
    {
        new WaitToil(SimTime.Minutes(3), Pose.Standing) { DoneWhen = (cm, w) => !w.Cooking.NeedsReheat(cm) },
        new DoToil((cm, w) => { w.Cooking.FinishReheat(cm); return true; }),
    };

    public bool NeedsReheat(CrewMember c) => _eating.TryGetValue(c.Id, out var s) && s.Reheat;
    /// <summary>지금 먹는 것이 식은 채인가 (화면 · 말).</summary>
    public bool EatingCold(CrewMember c) => _eating.TryGetValue(c.Id, out var s) && s.Cold;
    /// <summary>정전 · 저출력에 이동식 히터 앞에서 데운 그릇인가 (화면).</summary>
    public bool EatingByHeater(CrewMember c) => _eating.TryGetValue(c.Id, out var s) && s.Heater;
    public DishRecipe? EatingNow(CrewMember c) => _eating.TryGetValue(c.Id, out var s) && s.Recipe >= 0 ? Dishes.Of(s.Recipe) : null;

    private void FinishReheat(CrewMember c)
    {
        if (!_eating.TryGetValue(c.Id, out var s) || !s.Reheat) return;
        s.Reheat = false;
        Stats.Reheated++;
    }

    // ── 남겨 둔 접시 ──

    public (CrewMember forWhom, Batch batch)? RequestFor(CrewMember saver)
    {
        foreach (var r in _requests)
            if (r.saver == saver.Id && CrewOf(r.forWhom) is CrewMember x && Batches.FirstOrDefault(b => b.Id == r.batch) is Batch b && b.Portions > 0)
                return (x, b);
        return null;
    }

    /// <summary>시험 · 사건용: {saver}에게 {forWhom} 몫을 덜어 두게 한다.</summary>
    public void AskToSave(CrewMember saver, CrewMember forWhom, Batch b)
    {
        _requests.RemoveAll(r => r.forWhom == forWhom.Id);
        _requests.Add((saver.Id, forWhom.Id, b.Id, _w.Tick + SimTime.Hours(1)));
    }

    public Plate? PlateFor(CrewMember c)
    {
        foreach (var p in Plates) if (p.For == c.Id && !p.Eaten && !p.Spoiled) return p;
        return null;
    }

    /// <summary>접시를 놓을 식탁 (식당 → 없으면 주방 화구 곁).</summary>
    public Furniture? TableFor(Batch b) =>
        _w.Ship.RoomsOf(RoomType.Mess).Where(r => !r.OffLimits).SelectMany(r => r.Furniture).FirstOrDefault(f => f.Type == FurnitureType.Table) ?? b.Stove;

    public bool SetAside(CrewMember saver, CrewMember forWhom, Batch b, Furniture table)
    {
        var w = _w;
        _requests.RemoveAll(r => r.forWhom == forWhom.Id);
        if (b.Portions <= 0 || b.Spoiled || PlateFor(forWhom) != null) return false;
        if (!ItemsV15.Use(w, ItemKind.Meal)) return false; // 재고에서 한 그릇 덜어 낸다 (남들이 먹지 않게)
        b.Portions--;
        Plates.Add(new Plate
        {
            Id = ++_nextId, Recipe = b.Recipe, Cook = b.Cook, Quality = b.Quality * (0.6f + 0.4f * b.Fresh), For = forWhom.Id, By = saver.Id,
            SetAt = w.Tick, Temp = b.Temp, Table = table,
        });
        Stats.SavedPlates++;
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(saver.Name)} {forWhom.Name} 몫으로 {b.Spec.Name} 한 접시를 덜어 이름표를 붙여 뒀다", saver.Id);
        return true;
    }

    /// <summary>식탁에서 접시를 보고 이름표를 읽는다 — 그때 처음 안다.</summary>
    public void FindPlate(CrewMember c, Plate p)
    {
        var w = _w;
        var r = p.Spec;
        bool cold = r.Hot && p.Temp < 45f;
        p.Reheating = cold && CanReheat(p.Table.Room); // 이미 이름표를 본 접시에 (불려 갔다가) 다시 와도 식었으면 데운다
        if (p.Found) return;
        p.Found = true;
        var by = CrewOf(p.By);
        c.Say(w, Persona.Say(c, $"어? '{c.Name} 몫 — {by?.Name}'"));
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {(cold ? "식은 " : "")}{Ko.EulReul(r.Name)} 먹으려다 이름표를 봤다 — {Ko.IGa(by?.Name ?? "누군가")} 남겨 둔 제 몫", c.Id);
    }

    public void FinishPlateReheat(Plate p)
    {
        if (!p.Reheating) { p.ServedTemp = p.Temp; return; }
        p.Reheating = false;
        p.Temp = 65f;
        p.ServedTemp = p.Temp;
        Stats.Reheated++;
    }

    public void EatPlate(CrewMember c, Plate p)
    {
        var w = _w;
        if (p.Eaten) return;
        p.Eaten = true;
        _lastAte[c.Id] = w.Tick;
        Stats.SavedEaten++;
        var r = p.Spec;
        bool cold = r.Hot && (p.ServedTemp >= 0f ? p.ServedTemp : p.Temp) < 45f; // 먹기 시작할 때의 온도 (스무 분 먹는 사이 식은 건 셈하지 않는다)
        if (cold) ColdNote(c, r);
        float q = Taste(c, p.Quality, 1f, cold);
        var by = CrewOf(p.By);
        if (by != null && !by.Dead)
        {
            w.Relations.Remember(c, by, RelationReason.GaveMeGift, $"늦게 온 나를 위해 {Ko.EulReul(r.Name)} 남겨 뒀다");
            c.ChangeAffinity(by, 0.06f);
            w.Brain2.Emotions.Feel(c, Feeling.Joy, cold ? 0.08f : 0.14f, $"{Ko.IGa(by.Name)} 남겨 둔 {r.Name}", by); // v16 통합 (음식 × 두뇌): 이름표 붙은 접시는 기쁨 — 감정이 판단을 기울인다
            Life.Diary(w, c, Persona.Say(c, $"늦게 끝나고 가 보니 {(cold ? "식은 " : "")}{r.Name} 한 접시에 이름표가 붙어 있었다 — '{c.Name} 몫, {by.Name}'." + (cold ? " 식었어도 고마웠다" : " 고마웠다")));
        }
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
        Home(c, r, p.Cook, CrewOf(p.Cook)?.Name ?? "");
        Notice(c, p.Cook, CrewOf(p.Cook)?.Name ?? "", p.By == p.Cook, r, q);
    }

    // ── 불 위에 남은 냄비 (탄 냄새 → 불) ──

    /// <summary>급히 불려 나간 조리사가 냄비를 화구 위에 둔 채 나갔다 (시험에서도 부른다).</summary>
    public void LeaveOnBurner(Furniture stove, CrewMember? cook)
    {
        if (_burner.ContainsKey(stove.Id)) return;
        _burner[stove.Id] = (stove, _w.Tick, cook?.Id ?? -1);
        Stats.Scorched++;
        _w.Log.Add(_w.Tick, LogKind.Life, $"{stove.Room.Name} 화구 위에 냄비가 그대로 남았다" + (cook != null ? $" ({Ko.IGa(cook.Name)} 급히 나가며)" : ""), cook?.Id ?? -1);
    }

    /// <summary>이 방에서 타고 있는 냄비의 화구 (없으면 null).</summary>
    public Furniture? ScorchingIn(Room room)
    {
        foreach (var (_, x) in _burner) if (x.stove.Room == room) return x.stove;
        return null;
    }

    /// <summary>타는 정도 0~1 (40분이면 불이 붙는다).</summary>
    public float Scorch(Furniture stove) =>
        _burner.TryGetValue(stove.Id, out var x) ? MathF.Min(1f, (_w.Tick - x.since) / (float)SimTime.Minutes(40)) : 0f;

    public bool TurnOff(Furniture stove, CrewMember by)
    {
        var w = _w;
        if (!_burner.Remove(stove.Id, out var x)) return false;
        Stats.ScorchCaught++;
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(by.Name)} 탄 냄새를 맡고 와서 화구 위에 남은 냄비를 내렸다 — 불이 나기 전에", stove.Room, new[] { by }, log: true, crewLog: by.Id);
        Life.Diary(w, by, Persona.Say(by, "타는 냄새가 나서 가 보니 냄비가 불 위에 그대로였다. 내려놓았다"));
        if (CrewOf(x.cook) is CrewMember cook && cook != by && !cook.Dead)
            w.Relations.Remember(cook, by, RelationReason.CoveredMyMistake, "불 위에 두고 나간 냄비를 내려 줬다");
        return true;
    }

    // ── 시스템 틱 ──

    public void Update(float dt)
    {
        if (_burner.Count > 0) Watch.BurnerTick(); // 주컴퓨터 화구 감시 (1분마다)
        _acc += dt;
        if (_acc < 0.1f) return;
        float h = _acc;
        _acc = 0f;
        RefreshCooks();
        Stoves(h);
        Burners();
        Pots(h);
        PlatesTick(h);
        Requests();
        Gripes();
        Watch.Update();
    }

    private void Stoves(float h)
    {
        var w = _w;
        foreach (var st in w.Ship.FurnitureOf(FurnitureType.Stove))
        {
            if (st.Machine is not Machine m) continue;
            if (m.Active)
            {
                if (!_cooking.ContainsKey(st.Id))
                {
                    var cook = w.Crew.FirstOrDefault(c => !c.Dead && c.Job?.Order is { Kind: WorkKind.Cook } o && o.Target.Furniture == st);
                    if (cook != null) _cooking[st.Id] = (st, _planned.TryGetValue(st.Id, out var p) ? p : Choose(st, cook), cook.Id, w.Tick);
                }
                // 배우던 사람: 조리사 곁에서 보면 조금씩 는다
                if (CrewOf(_apprentice) is CrewMember ap && ap.Room == st.Room && ap.IsAwake && _cooking.TryGetValue(st.Id, out var cn) && cn.cook == _head)
                    ap.Practice(Skill.Cooking, 0.01f * h);
            }
            else if (_cooking.TryGetValue(st.Id, out var cn))
            {
                var cook = CrewOf(cn.cook);
                if (cook != null && cook.Job?.Order is { Kind: WorkKind.Cook } o && o.Target.Furniture == st) continue; // 아직 그 일 (마무리 중)
                _cooking.Remove(st.Id);
                bool rushed = cook == null || cook.Down || cook.Job?.Urgent == true || Crisis.Acting(w);
                if (rushed && R.Chance(0.4f)) LeaveOnBurner(st, cook);
            }
        }
    }

    private void Burners()
    {
        var w = _w;
        if (_burner.Count == 0) return;
        foreach (var id in _burner.Keys.OrderBy(k => k).ToList())
        {
            var x = _burner[id];
            if (x.stove.Room.Detached || x.stove.Stowed) { _burner.Remove(id); continue; }
            if (w.Tick - x.since < SimTime.Minutes(40)) continue;
            _burner.Remove(id);
            Stats.ScorchFires++;
            foreach (var c in x.stove.Cells.Concat(x.stove.UseSpots))
                if (w.Fire.Ignite(c, 0.35f)) break;
            w.History.Add(w, HistoryKind.Damage, $"{x.stove.Room.Name} 화구 위에 남은 냄비가 타다가 불이 붙었다", x.stove.Room, log: true);
        }
    }

    /// <summary>통합6 그 냄비가 든 냉장고가 도는가 (다른 방 냉장고가 돈다고 이 냄비가 차갑지는 않다) — 그 방에 냉장고가 없으면 아무 냉장고나.</summary>
    private bool FridgeOkFor(Batch b, bool any)
    {
        bool here = false;
        foreach (var f in _w.Ship.FurnitureOf(FurnitureType.Fridge))
        {
            if (f.Room != b.Room) continue;
            here = true;
            if (f.Machine == null || f.Machine.Efficiency > 0.2f) return true;
        }
        return !here && any;
    }

    private bool FridgeOk()
    {
        foreach (var f in _w.Ship.FurnitureOf(FurnitureType.Fridge))
            if (f.Machine == null || f.Machine.Efficiency > 0.2f) return true;
        return false;
    }

    private void Pots(float h)
    {
        var w = _w;
        if (Batches.Count == 0) return;
        bool fridge = FridgeOk();
        int stock = w.Ship.CountStored(ItemKind.Meal);
        int sum = 0;
        foreach (var b in Batches) if (!b.Jar && !b.Spoiled) sum += b.Portions;
        // 다른 데서 꺼내 간 식사(나눠 준 것 · 판 것)만큼 오래된 냄비부터 줄인다
        for (int i = 0; i < Batches.Count && sum > stock; i++)
        {
            var b = Batches[i];
            if (b.Jar || b.Spoiled) continue;
            int cut = Math.Min(b.Portions, sum - stock);
            b.Portions -= cut;
            sum -= cut;
        }
        for (int i = Batches.Count - 1; i >= 0; i--)
        {
            var b = Batches[i];
            if (!b.InFridge && b.Room != null && w.Fire.Count > 0 && w.Fire.CountIn(b.Room) > 0)
            {
                // 불이 난 방의 냄비는 탄다 (식사 재고는 불이 태운 만큼 Fire가 센다)
                Stats.BurntPots++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{b.Room.Name} 불에 {b.Spec.Name} {Ko.IGa(Dishes.Vessel(b.Spec))} 탔다", b.Cook);
                Batches.RemoveAt(i);
                continue;
            }
            if (!b.Jar && !b.InFridge && w.Tick - b.Cooked > SimTime.Hours(2.5f) && fridge)
            {
                b.InFridge = true; // 남은 냄비는 냉장고로
                b.Room = w.Ship.FurnitureOf(FurnitureType.Fridge).FirstOrDefault()?.Room ?? b.Room;
            }
            bool cold = b.InFridge && FridgeOkFor(b, fridge);
            float amb = cold ? 4f : b.Room?.Air.Temperature ?? 20f;
            b.Temp += (amb - b.Temp) * (1f - MathF.Exp(-h / 0.7f));
            float rot = b.Jar ? (b.Ready(w.Tick) ? 0.003f : 0.001f) : cold ? 0.004f : 0.05f;
            b.Fresh = MathF.Max(0f, b.Fresh - rot * h * TechWeb.Mul(_w, "food.rot")); // v16.14 진공 포장
            if (b.Jar && !b.WasReady && b.Ready(w.Tick))
            {
                b.WasReady = true;
                Stats.JarsReady++;
                w.FoodSources.Note(FoodSrc.Ferment, b.Portions); // v16.22 발효 음식이 익었다
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(b.CookName)} 앉혀 둔 {Ko.IGa(b.Spec.Name)} 익었다", b.Cook);
            }
            if (!b.Spoiled && b.Fresh < 0.2f)
            {
                b.Spoiled = true;
                b.SpoiledAt = w.Tick;
                Stats.Spoiled++;
                if (!b.Jar && b.Portions > 0) Life.Take(w, ItemKind.Meal, Math.Min(b.Portions, w.Ship.CountStored(ItemKind.Meal)));
                w.Log.Add(w.Tick, LogKind.Warning, $"{b.Spec.Name} {Ko.IGa(Dishes.Vessel(b.Spec))} 상했다 ({b.CookName} · {SimTime.Day(b.Cooked)}일째)" + (fridge ? "" : " — 냉장고가 멈췄다"));
            }
            bool gone = b.Spoiled ? w.Tick - b.SpoiledAt > SimTime.Hours(3) : b.Portions <= 0 && (!b.Jar || b.Ready(w.Tick));
            if (gone && b.Spoiled && b.Room != null) Garbage(b.Room);
            if (gone) Batches.RemoveAt(i);
        }
    }

    /// <summary>상한 냄비를 버린다 (냄새에 불평한 사람이 치운다).</summary>
    public bool Discard(Room room, CrewMember by)
    {
        var b = Batches.FirstOrDefault(x => x.Spoiled && x.Room == room);
        if (b == null) return false;
        Batches.Remove(b);
        Garbage(room);
        by.Soil.Hands[(int)SoilKind.Bio] = MathF.Min(1f, by.Soil.Hands[(int)SoilKind.Bio] + 0.3f); // 버린 손에 균 — 씻어야 한다
        _w.Log.Add(_w.Tick, LogKind.Life, $"{Ko.IGa(by.Name)} 상한 {Ko.EulReul(b.Spec.Name)} 내다 버렸다", by.Id);
        return true;
    }

    /// <summary>상한 음식을 버린 자리: 방에 균이 남는다 (Soil — 청소 · 손 씻기 · 냄새로 이어진다).</summary>
    private void Garbage(Room room)
    {
        var soil = _w.Soil.RoomSoil(room);
        soil[(int)SoilKind.Bio] = MathF.Min(1f, soil[(int)SoilKind.Bio] + 0.15f);
    }

    private void PlatesTick(float h)
    {
        var w = _w;
        for (int i = Plates.Count - 1; i >= 0; i--)
        {
            var p = Plates[i];
            float amb = p.Table.Room.Air.Temperature;
            if (!p.Reheating) p.Temp += (amb - p.Temp) * (1f - MathF.Exp(-h / 0.5f));
            if (!p.Eaten && !p.Spoiled && w.Tick - p.SetAt > SimTime.Hours(16))
            {
                p.Spoiled = true;
                w.Log.Add(w.Tick, LogKind.Life, $"{CrewOf(p.For)?.Name} 몫으로 남겨 둔 {Ko.IGa(p.Spec.Name)} 끝내 주인을 못 찾고 상했다");
            }
            if (p.Eaten && w.Tick - p.SetAt > SimTime.Hours(1) || p.Spoiled && w.Tick - p.SetAt > SimTime.Hours(18)) Plates.RemoveAt(i);
        }
    }

    private static bool Free(CrewMember o) => !o.Dead && o.CanAct && o.IsAwake && !o.IsChild && o.Job?.Urgent != true;

    /// <summary>늦게 오는 사람: 끼니 냄비가 나온 뒤에도 일하느라 오지 않은 사람 — 부엌 · 식당에 있는 사람이 몫을 덜어 둔다.</summary>
    private void Requests()
    {
        var w = _w;
        _requests.RemoveAll(r => w.Tick > r.until);
        foreach (var b in Batches)
        {
            if (b.Jar || b.Spoiled || b.InFridge || b.Portions < 2) continue;
            long age = w.Tick - b.Cooked;
            if (age < SimTime.Minutes(30) || age > SimTime.Hours(2.5f)) continue;
            int made = Plates.Count(p => p.SetAt >= b.Cooked) + _requests.Count;
            foreach (var x in w.Crew)
            {
                if (made >= 2) break;
                if (x.Dead || x.IsChild || !x.IsAwake || x.Needs.Hunger < 0.2f) continue;
                if (_lastAte.TryGetValue(x.Id, out var ate) && ate >= b.Cooked - SimTime.Minutes(30)) continue;
                if (x.Room?.Type is RoomType.Galley or RoomType.Mess) continue;
                if (!(x.Pose == Pose.Working || x.Outside || x.Job?.Order != null)) continue;
                if (PlateFor(x) != null || _requests.Any(r => r.forWhom == x.Id)) continue;
                CrewMember? saver = null;
                if (CrewOf(b.Cook) is CrewMember ck && ck != x && Free(ck) && ck.Room?.Type is RoomType.Galley or RoomType.Mess && !_requests.Any(r => r.saver == ck.Id)) saver = ck;
                else
                    foreach (var o in w.Crew)
                        if (o != x && Free(o) && o.Room?.Type is RoomType.Galley or RoomType.Mess && o.AffinityTo(x) > -0.1f && !_requests.Any(r => r.saver == o.Id)
                            && (saver == null || o.AffinityTo(x) > saver.AffinityTo(x))) saver = o;
                if (saver == null) continue;
                _requests.Add((saver.Id, x.Id, b.Id, w.Tick + SimTime.Hours(1)));
                made++;
            }
        }
    }

    /// <summary>배급 중: 나누는 방식(방침)에 대한 불만 · 의견이 나온다.</summary>
    private void Gripes()
    {
        var w = _w;
        if (!w.Food.Rationing || w.Tick - _lastGripe < SimTime.Hours(4)) return;
        int pol = w.Policies["rations"];
        bool Working(CrewMember c) => c.Job?.Activity is ChoresActivity || ChoresActivity.OnShiftStatic(c, w);
        bool Hurt(CrewMember c) => c.Vitals.Injury > 0.2f || c.Vitals.Health < 0.6f || c.Ailments.Count > 0;
        var who = w.Crew.Where(c => !c.Dead && !c.IsChild && c.IsAwake && c.CanAct && c.Needs.Hunger > 0.35f && pol switch
        {
            0 => Working(c), 1 => !Working(c) || Hurt(c), _ => !Hurt(c) && Working(c),
        }).ToList();
        if (who.Count == 0) return;
        _lastGripe = w.Tick;
        var c = who[R.Range(0, who.Count)];
        string say = pol switch
        {
            0 => "똑같이 나누는 건 좋은데, 하루 종일 일하는 사람은 더 먹어야 하지 않나",
            1 => c.Vitals.Injury > 0.2f ? "다친 사람은 일을 못 하니 덜 먹으라는 건가" : "비번이면 굶으라는 건가",
            _ => "아픈 사람 먼저인 건 알지만, 일하는 사람도 배가 고프다",
        };
        Stats.RationGripes++;
        c.Say(w, Persona.Say(c, say));
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
        Life.Diary(w, c, Persona.Say(c, $"배급 이야기 — {say}"));
        w.Log.Add(w.Tick, LogKind.Life, $"배급 불만 — {Ko.IGa(c.Name)} \"{say}\"", c.Id);
        // 곁에 있던 사람의 의견: 같은 처지면 맞장구, 아니면 맞선다
        var o = w.Crew.FirstOrDefault(x => x != c && !x.Dead && x.IsAwake && x.Room == c.Room && !x.IsChild);
        if (o == null) return;
        bool same = Working(o) == Working(c);
        o.ChangeAffinity(c, same ? 0.01f : -0.02f);
        o.Say(w, Persona.Say(o, same ? "맞아, 나도 그렇게 생각해" : "다들 힘들어. 정한 대로 하자"));
    }

    // ── 기항지 ──

    /// <summary>기항지 훅 (Voyage.Trade): 고향 음식에 드는 재료를 산다 (사람마다).</summary>
    public void OnPort(string port, List<string> lines)
    {
        var w = _w;
        int bought = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || bought >= 3) continue;
            var r = Dishes.HomeDish(w, c);
            if (!r.Uses(Ingredient.HomeGoods)) continue;
            int i = Dishes.IndexOf(r.Id);
            if (HomeGoods[i] >= 2 || w.Voyage.Credits < 3f) continue;
            w.Voyage.Credits -= 2f;
            HomeGoods[i] += 3;
            Stats.HomeGoodsBought += 3;
            bought++;
            Life.Diary(w, c, Persona.Say(c, $"{port}에서 {r.Name} 재료를 샀다. 오랜만에 고향 맛을 보겠다"));
        }
        if (bought > 0) lines.Add($"고향 재료 {bought}가지");
    }

    // ── 냄새 (Smell.cs가 읽는다) ──

    public void AddSmells(SmellSystem s)
    {
        var w = _w;
        foreach (var (_, x) in _cooking)
        {
            var r = Dishes.Of(x.recipe);
            s.Emit(x.stove.Room, r.Smell, r.Smell == SmellKind.Bread ? 0.9f : 0.65f);
        }
        foreach (var (_, x) in _burner) s.Emit(x.stove.Room, SmellKind.Burnt, 0.45f + 0.55f * Scorch(x.stove));
        foreach (var b in Batches)
        {
            if (b.Room == null) continue;
            if (b.Spoiled) s.Emit(b.Room, SmellKind.Foul, b.InFridge ? 0.4f : 0.65f);
            else if (!b.Jar && !b.InFridge && b.Temp > 45f && w.Tick - b.Cooked < SimTime.Hours(1)) s.Emit(b.Room, b.Spec.Smell, 0.3f);
        }
        foreach (var p in Plates) if (p.Spoiled) s.Emit(p.Table.Room, SmellKind.Foul, 0.3f);
    }

    /// <summary>이 방에서 갓 만든 (아직 따뜻한) 냄비.</summary>
    public Batch? FreshIn(Room room, SmellKind k)
    {
        foreach (var b in Batches)
            if (!b.Jar && !b.Spoiled && !b.InFridge && b.Room == room && b.Spec.Smell == k && _w.Tick - b.Cooked < SimTime.Hours(1.5f)) return b;
        return null;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Batches.Count); I(Plates.Count); I(_nextId); I(_burner.Count); I(_cooking.Count);
        foreach (var b in Batches) { I(b.Recipe); I(b.Cook); I(b.Portions); F(b.Quality); F(b.Temp); F(b.Fresh); I(b.InFridge ? 1 : 0); I(b.Spoiled ? 1 : 0); }
        foreach (var p in Plates) { I(p.For); I(p.By); F(p.Temp); I(p.Eaten ? 1 : 0); I(p.Found ? 1 : 0); }
        foreach (var g in HomeGoods) I(g);
        I(Stats.Served); I(Stats.TasteNoticed); I(Stats.SavedPlates); I(Stats.Scorched); I(Stats.RationGripes);
        I(Stats.GermPots); I(Stats.OilSplash); I(Stats.HeaterWarm); I(_homeTold.Count);
        foreach (var b in Batches) I((b.Germy ? 1 : 0) + (b.Flagged ? 2 : 0));
        Watch.Hash(I);
    }
}

// ─────────────────────────────── 행동 ───────────────────────────────

/// <summary>늦게 오는 사람 몫을 냄비에서 덜어 이름표를 붙여 식탁에 둔다.</summary>
public sealed class SetAsidePlateActivity : Activity
{
    public override string Id => "setaside";
    public override string Label => "접시 남겨 두기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Cooking.RequestFor(c) is not var (x, _)) return (0f, "—");
        return (0.92f, $"{x.Name} 몫을 남겨 둔다"); // 통합8 부탁받은 한 그릇은 국이 다 떨어지기 전에 바로 (0.72로는 배우던 걸 못 놓아 남은 게 없었다)
    }

    private static Cell? Beside(Furniture f, World w, DistanceField dist)
    {
        Cell? best = null;
        foreach (var s in f.UseSpots) if (dist.Reachable(s) && (best == null || dist.Get(s) < dist.Get(best.Value))) best = s;
        if (best != null) return best;
        foreach (var c in f.Cells)
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (w.Ship.IsWalkable(n) && dist.Reachable(n) && (best == null || dist.Get(n) < dist.Get(best.Value))) best = n;
            }
        return best;
    }

    internal static Cell? Spot(Furniture f, World w, DistanceField dist) => Beside(f, w, dist);

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Cooking.RequestFor(c) is not var (x, b)) return null;
        var table = w.Cooking.TableFor(b);
        if (table == null || Spot(table, w, dist) is not Cell at) return null;
        var toils = Plans.DropOff(c, w, dist);
        if (b.Stove != null && Spot(b.Stove, w, dist) is Cell pot)
        {
            toils.Add(new GotoToil(pot));
            toils.Add(new WaitToil(SimTime.Minutes(1.5f), Pose.Working, b.Stove.Center));
        }
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, table.Center));
        toils.Add(new DoToil((cm, world) => world.Cooking.SetAside(cm, x, b, table)));
        return new Job(this, "접시 남겨 두기", toils) { LogText = $"{x.Name} 몫을 덜어 둔다", TargetRoom = table.Room };
    }
}

/// <summary>늦게 온 사람이 식탁의 접시를 보고 — 이름표를 읽고 — (데울 수 있으면 데워) 먹는다.</summary>
public sealed class SavedPlateActivity : Activity
{
    public override string Id => "savedplate";
    public override string Label => "늦은 끼니";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Down || w.Cooking.PlateFor(c) is not Plate p) return (0f, "—");
        float hunger = c.Needs.Hunger;
        if (hunger < 0.3f) return (0f, "아직 배부름");
        float s = Curve.Smooth(hunger, 0.3f, 0.9f) * 1.1f + 0.3f;
        if (c.Pose == Pose.Sleeping && hunger < 0.85f) s *= 0.5f;
        return (s, "배고픔 — 늦은 끼니");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Cooking.PlateFor(c) is not Plate p || SetAsidePlateActivity.Spot(p.Table, w, dist) is not Cell at) return null;
        int eat = SimTime.Minutes(20);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Minutes(0.5f), Pose.Standing, p.Table.Center));
        toils.Add(new DoToil((cm, world) => { world.Cooking.FindPlate(cm, p); return !p.Eaten && !p.Spoiled; }));
        toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Standing) { DoneWhen = (_, _) => !p.Reheating });
        toils.Add(new DoToil((_, world) => { world.Cooking.FinishPlateReheat(p); return true; }));
        toils.Add(new WaitToil(eat + SimTime.Minutes(5), Pose.Sitting, p.Table.Center, minTicks: SimTime.Minutes(12))
        {
            EveryTick = (cm, _) => cm.Needs.Food += 0.95f / eat,
            DoneWhen = (cm, _) => cm.Needs.Food >= 0.99f,
        });
        toils.Add(new DoToil((cm, world) => { world.Cooking.EatPlate(cm, p); return true; }));
        return new Job(this, "늦은 끼니", toils) { LogText = $"늦은 끼니 — {Ko.EuRo(p.Table.Room.Name)}", TargetRoom = p.Table.Room, InterruptMargin = 0.35f };
    }
}
