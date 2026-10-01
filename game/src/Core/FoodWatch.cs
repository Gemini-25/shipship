using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.8 주컴퓨터가 읽는 음식 · 냄새.
// 컴퓨터는 냄새를 못 맡는다 — 냉장고 전력 · 고장, 냄비 기록(누가 · 언제 · 몇 그릇), 화구 전력, 사람이 외친 "탄 냄새"를 읽는다.
//   신선도: 냉장 냄비가 상하기 전에 조리사에게 "먼저 쓰라"고 알린다 → 조리사(컴퓨터를 믿는 만큼)가 남은 것 수프 · 볶음밥으로 돌린다.
//   냉장고 정지: 냄비가 데워지기 시작하면 경보 · 다섯 칸 기록 (수리 · 전력 우선 요청).
//   식단: 식단 계획 모듈이 있으면 그날 식단을 항아리 · 냄비 · 재고 · 고향 재료로 짠다 → 조리사가 대개 따른다.
//   탄 냄새 ↔ 감지기: 사람 코가 먼저 찾은 냄비 · 불을 감지기 상태와 견주어 적고, 배운 뒤로는 화구 자리 비움을 지켜보다 가까운 사람을 부른다.
public sealed class FoodWatch
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 4517 + 97));
    private long _next, _nextBurner;
    private long _fridgeAlert = -1_000_000;
    private readonly Dictionary<int, long> _called = new();
    /// <summary>화구 자리 비움 감시 (사람 코가 먼저 잡은 일을 겪고 켠다).</summary>
    public bool BurnerWatch { get; private set; }
    /// <summary>오늘 식단 (레시피 번호) · 짠 날.</summary>
    public List<int> Menu { get; } = new();
    public int MenuDay { get; private set; } = -1;
    public int FreshWarnings, FreshFollowed, FreshIgnored, FridgeAlerts, MenuDays, MenuFollowed, Compared, BurnerCalls;

    public FoodWatch(World w) => _w = w;

    public string Summary() =>
        $"신선도 알림 {FreshWarnings}(따름 {FreshFollowed} · 흘려들음 {FreshIgnored}) · 냉장고 경보 {FridgeAlerts} · 식단 {MenuDays}일(따름 {MenuFollowed}) · 탄내↔감지기 비교 {Compared} · 화구 확인 부름 {BurnerCalls}" + (BurnerWatch ? " · 화구 감시 켬" : "");

    private bool Online => _w.Automation.Present ? _w.Automation.MainOnline : false;

    public void Update()
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(5);
        if (!Online) return;
        Fridges();
        Fresh();
        MakeMenu();
    }

    /// <summary>화구 감시는 1분마다 (켜 둔 채 남은 화구가 있을 때만 — Cooking이 매 시스템 틱 부른다).</summary>
    public void BurnerTick()
    {
        var w = _w;
        if (!BurnerWatch || w.Tick < _nextBurner || !Online) return;
        _nextBurner = w.Tick + SimTime.Minutes(1);
        Burners();
    }

    private static bool FridgeDown(Furniture f) => f.Machine is Machine m && (m.Stopped || m.Efficiency < 0.2f);

    // ── 냉장고 정지 ──

    private void Fridges()
    {
        var w = _w;
        var ck = w.Cooking;
        Furniture? down = null;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.Fridge)) if (FridgeDown(f)) { down = f; break; }
        if (down == null || w.Tick - _fridgeAlert < SimTime.Hours(6)) return;
        int pots = ck.Batches.Count(b => b.InFridge && !b.Spoiled && (b.Portions > 0 || b.Jar));
        if (pots == 0) return;
        float worst = ck.Batches.Where(b => b.InFridge && !b.Spoiled).Select(b => b.Fresh).DefaultIfEmpty(1f).Min();
        float hours = MathF.Max(0f, (worst - 0.2f) / 0.05f);
        _fridgeAlert = w.Tick;
        FridgeAlerts++;
        string why = down.Machine is { Powered: false } ? "전원 없음" : "고장";
        w.RaiseAlert($"냉장고 정지({why}) — 냄비 {pots}개가 데워진다 · 약 {hours:0}시간 안에 상한다", down.Room, AlertLevel.Warning, false);
        w.Automation.Book.Add(ActKind.Advice, down.Room, $"{down.Room.Name} 냉장고 {why} · 냄비 {pots}개 · 가장 낮은 신선도 {worst * 100:0}%",
            $"예측: {hours:0}시간 뒤 상함 — 상한 냄비는 시큼하고 식중독을 부른다", "조치: 식단을 냉장 냄비부터로 바꿈", "요청: 냉장고 수리 · 저출력이면 냉장고 전력 우선", "food:fridge", SimTime.Hours(6), 120f);
        // 냉장 냄비를 모두 "먼저 쓸 것"으로
        foreach (var b in ck.Batches) if (b.InFridge && !b.Spoiled && !b.Jar && b.Portions > 0) Flag(b, "냉장고가 멈췄다");
    }

    // ── 신선도 ──

    private void Fresh()
    {
        foreach (var b in _w.Cooking.Batches)
            if (!b.Jar && !b.Spoiled && !b.Flagged && !b.FlagIgnored && b.Portions >= 2 && b.Fresh < 0.6f) Flag(b, $"신선도 {b.Fresh * 100:0}%");
    }

    private void Flag(Batch b, string why)
    {
        var w = _w;
        if (b.Flagged || b.FlagIgnored) return; // 흘려들은 냄비는 다시 조르지 않는다
        var ck = w.Cooking;
        var cook = ck.HeadCook is CrewMember h && !CookingSystem.LaidUp(h) ? h : ck.Apprentice;
        if (cook == null) return;
        FreshWarnings++;
        // 조리사가 컴퓨터를 믿는 만큼 따른다 — 안 믿으면 흘려듣는다 (냄비는 그대로 상해 간다)
        float trust = w.Automation.Trusts.Of(cook);
        bool follow = R.Chance(0.35f + 0.6f * trust);
        b.Flagged = follow;
        b.FlagIgnored = !follow;
        if (follow) FreshFollowed++; else FreshIgnored++;
        float hours = MathF.Max(0f, (b.Fresh - 0.2f) / (b.InFridge ? 0.004f : 0.05f));
        string text = $"{b.Spec.Name}({b.CookName} · {SimTime.Day(b.Cooked)}일째 · {b.Portions}그릇) {why} — 다음 끼니에 먼저 쓰세요";
        w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, cook.Id, "식단", text));
        w.Automation.Book.Add(ActKind.Advice, b.Room, $"{b.Spec.Name} 냄비 {why} · {b.Portions}그릇 남음", $"예측: 약 {hours:0}시간 뒤 상함",
            "조치: 식단 — 남은 것 수프 · 볶음밥으로 먼저", $"요청: {cook.Name} — 다음 조리에 이 냄비부터", "food:fresh:" + b.Id, SimTime.TicksPerDay, 240f);
        w.Log.Add(w.Tick, LogKind.Life, follow ? $"주컴퓨터 알림 — {Ko.IGa(cook.Name)} 다음 끼니에 {Ko.EulReul(b.Spec.Name)} 먼저 쓰기로 했다 ({why})"
            : $"주컴퓨터 알림 — {Ko.EunNeun(cook.Name)} {b.Spec.Name} 신선도 알림을 흘려들었다", cook.Id);
    }

    /// <summary>조리사가 알림 받은 냄비를 새 냄비에 넣었다 (OnCooked가 부른다).</summary>
    public void Used(CrewMember cook, Batch old, DishRecipe into)
    {
        if (!old.Flagged) return;
        _w.Automation.Book.Add(ActKind.Advice, old.Room, $"{Ko.IGa(cook.Name)} 알림 받은 {Ko.EulReul(old.Spec.Name)} {into.Name}에 넣었다", "상하기 전에 썼다", "", "", "food:used:" + old.Id, SimTime.Hours(1), 1f);
    }

    // ── 식단 ──

    private void MakeMenu()
    {
        var w = _w;
        var ck = w.Cooking;
        int day = SimTime.Day(w.Tick);
        if (day == MenuDay || SimTime.HourOfDay(w.Tick) < 5f || !w.Automation.Active(ComputerModule.MealPlan)) return;
        MenuDay = day;
        Menu.Clear();
        void Add(string id) { int i = Dishes.IndexOf(id); if (i >= 0 && !Menu.Contains(i) && Menu.Count < 3) Menu.Add(i); }
        if (ck.Batches.Any(b => !b.Jar && !b.Spoiled && b.Portions >= 2 && (b.Flagged || w.Tick - b.Cooked > SimTime.Hours(14)))) Add("leftsoup");
        if (ck.Batches.Any(b => b.Jar && !b.Spoiled && b.Spec.Id == "kimchi" && b.Ready(w.Tick) && b.Portions > 2)) Add("kimchistew");
        for (int i = 0; i < Dishes.All.Length && Menu.Count < 3; i++)
            if (ck.HomeGoods[i] > 0 && !Dishes.All[i].Jar) Add(Dishes.All[i].Id);
        int produce = w.Ship.CountStored(ItemKind.Produce), rations = w.Ship.CountStored(ItemKind.Ration);
        if (produce < 8 && rations >= 6) Add("budae");
        Add(produce > 12 ? "bread" : "vegsoup");
        Add("stew");
        MenuDays++;
        var names = string.Join(" · ", Menu.Select(i => Dishes.Of(i).Name));
        w.Automation.Book.Add(ActKind.Advice, w.Ship.RoomsOf(RoomType.Galley).FirstOrDefault(), $"냄비 {ck.Batches.Count(b => !b.Jar && b.Portions > 0)} · 항아리 {ck.Batches.Count(b => b.Jar)} · 채소 {produce} · 비상식량 {rations}",
            "상하기 전에 먼저 · 있는 재료로", $"조치: 오늘 식단 — {names}", "", "food:menu:" + day, SimTime.TicksPerDay, 600f);
        if (ck.HeadCook is CrewMember cook) w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, cook.Id, "식단", $"오늘 식단: {names}"));
    }

    /// <summary>조리사가 고를 때: 오늘 식단에 있으면 더 끌린다.</summary>
    public float MenuWeight(int recipe) => Menu.Count > 0 && MenuDay == SimTime.Day(_w.Tick) && Menu.Contains(recipe) ? 3f : 1f;

    public void Chosen(int recipe)
    {
        if (MenuWeight(recipe) > 1f) MenuFollowed++;
    }

    // ── 탄 냄새 ↔ 감지기 ──

    /// <summary>사람이 탄 냄새로 먼저 찾았다 (불 위의 냄비 · 감지기가 놓친 불): 감지기 상태와 견주어 적고, 화구 감시를 켠다.</summary>
    public void Compare(Room room, CrewMember who, string found)
    {
        var w = _w;
        if (!Online) return;
        Compared++;
        bool detector = room.Powered && w.Automation.AlarmsIn(room);
        string judge = !room.Powered || room.BreakerOff ? "감지기 꺼짐(전원 없음) — 사람 코만 알았다"
            : detector ? "감지기는 조용했다 — 불꽃 감지기는 연기 · 탄내를 먼저 못 맡는다" : "이 방은 경보가 연결돼 있지 않다";
        w.Automation.Book.Add(ActKind.Advice, room, $"{who.Name} 탄 냄새 신고 → {found}", judge,
            BurnerWatch ? "" : "조치: 화구 자리 비움 감시를 켰다 (화구 전력이 계속인데 곁에 사람이 없으면 부른다)",
            !room.Powered || room.BreakerOff ? $"요청: {room.Name} 분전함 · 감지기 전원 확인" : "", "food:smell:" + room.Id, SimTime.Hours(2), 30f);
        w.Log.Add(w.Tick, LogKind.Life, $"주컴퓨터: {who.Name}의 탄 냄새 신고와 감지기를 견줬다 — {judge}", who.Id);
        BurnerWatch = true;
    }

    /// <summary>화면: 주컴퓨터가 이 화구 때문에 사람을 부른 참이다.</summary>
    public bool Calling(Furniture st) => _called.TryGetValue(st.Id, out var t) && _w.Tick - t < SimTime.Minutes(20) && _w.Cooking.Scorch(st) > 0f;

    /// <summary>시험용: 감시를 미리 켜 둔다.</summary>
    public void Learn() => BurnerWatch = true;

    private void Burners()
    {
        var w = _w;
        var ck = w.Cooking;
        foreach (var st in w.Ship.FurnitureOf(FurnitureType.Stove))
        {
            float sc = ck.Scorch(st);
            if (sc < 0.05f || !st.Room.Powered) continue; // 2분쯤: 조리 일이 끝났는데 화구 전력이 계속 나가고
            if (Attended(st)) continue; // 화구 앞에 깨어 있는 사람이 있다 (같은 방 구석에서 자거나 먹는 건 '곁'이 아니다)
            if (_called.TryGetValue(st.Id, out var t) && w.Tick - t < SimTime.Minutes(20)) continue;
            CrewMember? best = null;
            float bd = float.MaxValue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.IsChild || !c.IsAwake || !c.CanAct || c.Outside || c.Job?.Urgent == true) continue;
                float d = (c.Position - st.Center).LengthSquared();
                if (d < bd) { bd = d; best = c; }
            }
            if (best == null) continue;
            _called[st.Id] = w.Tick;
            BurnerCalls++;
            w.Smells.Ask(best, st.Room);
            if (best.Job?.Urgent != true && best.Job?.Activity is not CheckSmellActivity) best.Interrupt(w); // 호출: 하던 걸 내려놓고 간다
            w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, best.Id, "확인", $"{st.Room.Name} 화구가 켜진 채 곁에 아무도 없습니다 — 확인해 주세요"));
            w.Automation.Book.Add(ActKind.Advice, st.Room, $"{st.Room.Name} 화구 전력 계속 {sc * 40f:0}분 · 곁에 사람 없음", "예측: 냄비가 타서 불이 날 수 있다 (사람 코가 먼저 잡았던 일)",
                "조치: 가장 가까운 사람에게 확인 요청", $"요청: {best.Name} — 화구 확인", "food:burner:" + st.Id, SimTime.Minutes(20), 30f);
            best.Say(w, Persona.Say(best, "주방 화구? 가 볼게"));
        }
    }

    /// <summary>화구 앞(두 칸 안)에 깨어 있는 사람이 있나.</summary>
    private bool Attended(Furniture st)
    {
        foreach (var c in _w.Crew)
            if (!c.Dead && c.IsAwake && !c.Down && c.Room == st.Room && (c.Position - st.Center).LengthSquared() <= 2.5f * 2.5f) return true;
        return false;
    }

    public void Hash(Action<long> I)
    {
        I(FreshWarnings); I(FridgeAlerts); I(MenuDays); I(Compared); I(BurnerCalls); I(BurnerWatch ? 1 : 0);
        foreach (var m in Menu) I(m);
    }
}
