using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.8 레시피 표: 재료(수확한 채소 · 향신료 · 비상식량 · 커피 · 찻잎 · 남은 음식 · 기항지에서 산 고향 재료 · 익은 김치) →
// 이름 · 조리 시간 · 필요 설비(화구 · 오븐 · 항아리) · 뜨겁게 먹는 것인지 · 고향 음식인지 · 며칠 기다리는 음식인지.
// 재료가 없으면 대체 재료로 (맛이 조금 떨어진다). 그릇 모양과 김은 화면(ShipViewFood)이 요리 갈래마다 다르게 그린다.

public enum DishKind { Soup, Stew, Rice, Noodle, Bread, Pan, Dumpling, Porridge, Cake, Side, Ferment, Pickle }

public enum Ingredient { Veg, Spice, Ration, Coffee, Tea, Leftover, HomeGoods, Kimchi }

public enum CookStation { Stove, Oven, Jar }

public sealed record DishRecipe(string Id, string Name, DishKind Kind, (Ingredient what, int n)[] Needs, float Hours, CookStation Station,
    bool Hot, bool Home = false, int Days = 0, string Note = "")
{
    /// <summary>며칠 기다리는 음식 (발효 · 절임) — 냉장고 항아리에서 익는다.</summary>
    public bool Jar => Days > 0;
    /// <summary>굽는 냄새인가 (빵 · 전 · 케이크) — 아니면 끓이고 볶는 냄새.</summary>
    public SmellKind Smell => Kind is DishKind.Bread or DishKind.Cake or DishKind.Pan ? SmellKind.Bread : SmellKind.Cooking;
    public bool Uses(Ingredient i) => Needs.Any(x => x.what == i);
    /// <summary>기름진 요리 (전 · 볶음) — 화구 앞 바닥에 기름이 튄다.</summary>
    public bool Oily => Kind == DishKind.Pan || Id is "friedrice" or "japchae" or "budae";
}

public static class Dishes
{
    private static (Ingredient, int)[] N(params (Ingredient, int)[] x) => x;
    private const Ingredient V = Ingredient.Veg, S = Ingredient.Spice, R = Ingredient.Ration, C = Ingredient.Coffee, T = Ingredient.Tea,
        L = Ingredient.Leftover, H = Ingredient.HomeGoods, K = Ingredient.Kimchi;

    /// <summary>레시피 26가지. 채소 4개는 조리 일감이 늘 가져온다 — 여기 적힌 것은 그 밖에 더 드는 것.</summary>
    public static readonly DishRecipe[] All =
    {
        // ── 끓이는 것 ──
        new("vegsoup", "채소 수프", DishKind.Soup, N((V, 0)), 0.6f, CookStation.Stove, true, Note: "무엇이든 넣고 끓인다"),
        new("doenjang", "된장국", DishKind.Soup, N((V, 0), (H, 1)), 0.6f, CookStation.Stove, true, Home: true, Note: "기항지에서 산 된장"),
        new("seaweed", "미역국", DishKind.Soup, N((V, 0), (H, 1)), 0.7f, CookStation.Stove, true, Home: true, Note: "생일이 아니어도"),
        new("beansprout", "콩나물국", DishKind.Soup, N((V, 0)), 0.5f, CookStation.Stove, true, Home: true, Note: "재배실 콩나물"),
        new("leftsoup", "남은 것 수프", DishKind.Soup, N((L, 1)), 0.5f, CookStation.Stove, true, Note: "어제 남은 것을 다시 끓인다"),
        new("kimchistew", "김치찌개", DishKind.Stew, N((K, 1), (S, 1)), 0.7f, CookStation.Stove, true, Home: true, Note: "잘 익은 김치가 있어야 제맛"),
        new("budae", "부대찌개", DishKind.Stew, N((R, 2), (S, 1)), 0.6f, CookStation.Stove, true, Note: "비상식량 통조림을 털어 넣는다"),
        new("stew", "채소 스튜", DishKind.Stew, N((V, 0)), 0.9f, CookStation.Stove, true, Note: "오래 끓일수록"),
        new("curry", "카레", DishKind.Stew, N((S, 1)), 0.8f, CookStation.Stove, true, Note: "향신료가 모자라면 싱겁다"),
        new("sujebi", "수제비", DishKind.Noodle, N((V, 0)), 0.6f, CookStation.Stove, true, Home: true, Note: "반죽을 손으로 뜯는다"),
        new("ramen", "비상식량 라면", DishKind.Noodle, N((R, 1)), 0.4f, CookStation.Stove, true, Note: "급할 때"),
        new("porridge", "비상식량 죽", DishKind.Porridge, N((R, 1)), 0.4f, CookStation.Stove, true, Note: "아픈 사람에게"),
        // ── 볶고 비비는 것 ──
        new("friedrice", "남은 반찬 볶음밥", DishKind.Rice, N((L, 1)), 0.4f, CookStation.Stove, true, Note: "남은 것을 볶는다"),
        new("bibim", "비빔밥", DishKind.Rice, N((S, 1)), 0.5f, CookStation.Stove, false, Note: "식어도 괜찮다"),
        new("chazuke", "찻물 밥", DishKind.Rice, N((T, 1)), 0.4f, CookStation.Stove, true, Note: "찻잎을 우려 붓는다"),
        new("japchae", "잡채", DishKind.Rice, N((S, 1), (H, 1)), 0.7f, CookStation.Stove, false, Home: true, Note: "명절 음식"),
        new("dumpling", "만두", DishKind.Dumpling, N((S, 1)), 0.9f, CookStation.Stove, true, Note: "다 같이 빚으면 빠르다"),
        // ── 굽는 것 (냄새가 멀리 간다) ──
        new("bread", "갓 구운 빵", DishKind.Bread, N((V, 0)), 0.9f, CookStation.Oven, false, Note: "오븐이 없으면 팬에"),
        new("barleybread", "고향 보리빵", DishKind.Bread, N((H, 1)), 1f, CookStation.Oven, false, Home: true, Note: "고향 보리 가루"),
        new("pancake", "채소 전", DishKind.Pan, N((V, 0)), 0.5f, CookStation.Stove, true, Note: "기름 냄새"),
        new("hotcake", "팬케이크", DishKind.Pan, N((V, 0)), 0.5f, CookStation.Stove, true, Note: "아침에"),
        new("coffeecake", "커피 케이크", DishKind.Cake, N((C, 1)), 0.9f, CookStation.Oven, false, Note: "커피가 없으면 차로"),
        new("tteok", "떡", DishKind.Cake, N((H, 1)), 0.8f, CookStation.Stove, false, Home: true, Note: "쪄서 친다"),
        // ── 며칠 기다리는 것 (항아리) ──
        new("kimchi", "김치", DishKind.Ferment, N((V, 3), (S, 1)), 0.5f, CookStation.Jar, false, Home: true, Days: 3, Note: "사흘 익힌다"),
        new("pickle", "채소 절임", DishKind.Pickle, N((V, 2)), 0.3f, CookStation.Jar, false, Days: 2, Note: "이틀 절인다"),
        new("sikhye", "식혜", DishKind.Ferment, N((V, 2), (H, 1)), 0.4f, CookStation.Jar, false, Home: true, Days: 1, Note: "하루 삭힌다"),
    };

    private static readonly Dictionary<string, int> _index = All.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);

    public static int IndexOf(string id) => _index.TryGetValue(id, out var i) ? i : -1;
    public static DishRecipe Of(int i) => All[Math.Clamp(i, 0, All.Length - 1)];

    public static string Name(Ingredient i) => i switch
    {
        Ingredient.Veg => "채소", Ingredient.Spice => "향신료", Ingredient.Ration => "비상식량", Ingredient.Coffee => "커피", Ingredient.Tea => "찻잎",
        Ingredient.Leftover => "남은 음식", Ingredient.HomeGoods => "고향 재료", _ => "익은 김치",
    };

    /// <summary>배의 창고에서 꺼내 쓰는 물건 (남은 음식 · 고향 재료 · 김치는 조리 쪽이 따로 센다).</summary>
    public static ItemKind? Item(Ingredient i) => i switch
    {
        Ingredient.Veg => ItemKind.Produce, Ingredient.Spice => ItemKind.Spice, Ingredient.Ration => ItemKind.Ration,
        Ingredient.Coffee => ItemKind.Coffee, Ingredient.Tea => ItemKind.TeaLeaf, _ => null,
    };

    /// <summary>모자라면 무엇으로 바꾸나 · 맛이 얼마나 떨어지나 (null이면 그냥 빼고 한다).</summary>
    public static (Ingredient? sub, float penalty) Substitute(Ingredient i) => i switch
    {
        Ingredient.Spice => (null, 0.08f),
        Ingredient.Coffee => (Ingredient.Tea, 0.05f),
        Ingredient.Tea => (Ingredient.Coffee, 0.05f),
        Ingredient.HomeGoods => (Ingredient.Spice, 0.1f),
        Ingredient.Kimchi => (Ingredient.Spice, 0.08f),
        Ingredient.Ration => (Ingredient.Veg, 0.04f),
        Ingredient.Leftover => (Ingredient.Veg, 0.02f),
        _ => (null, 0.05f),
    };

    /// <summary>그릇 이름 (화면 · 기록).</summary>
    public static string Vessel(DishRecipe r) => r.Kind switch
    {
        DishKind.Soup => "국 냄비", DishKind.Stew => "뚝배기", DishKind.Rice => "웍", DishKind.Noodle => "면 냄비", DishKind.Porridge => "죽 냄비",
        DishKind.Bread or DishKind.Cake => "오븐 판", DishKind.Pan => "프라이팬", DishKind.Dumpling => "찜기",
        DishKind.Ferment => "항아리", _ => "유리병",
    };

    private static readonly DishRecipe[] _homes = All.Where(r => r.Home && !r.Jar).ToArray();

    /// <summary>이 사람의 고향 음식 (사람마다 정해져 있다).</summary>
    public static DishRecipe HomeDish(World w, CrewMember c) =>
        _homes[(int)((uint)(c.Id * 2246822519u + (uint)w.Seed * 3266489917u + 374761393u) % (uint)_homes.Length)];

    /// <summary>레시피 표 한 줄 (화면 · 기록).</summary>
    public static string Line(DishRecipe r) =>
        $"{r.Name} · {string.Join("+", r.Needs.Where(x => x.n > 0).Select(x => Name(x.what)).Prepend("채소"))} · {r.Hours * 60:0}분"
        + (r.Station == CookStation.Oven ? " · 오븐" : r.Station == CookStation.Jar ? $" · 항아리 {r.Days}일" : " · 화구")
        + (r.Hot ? " · 뜨겁게" : "") + (r.Home ? " · 고향 음식" : "");
}
