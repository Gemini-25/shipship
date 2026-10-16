using System;
using System.Collections.Generic;

namespace ShipSim.Core;

// v16.4 재질 × 원소 표 한 장 (젤다의 화학 엔진).
// 물건 · 설비 · 바닥 · 벽 · 옷 · 음식이 모두 재질을 갖고, 원소(불 · 열 · 냉기 · 물 · 전기 · 산소 · 연기 · 방사선 · 진동 · 압력)를 만나면
// 이 표의 한 칸으로 반응한다 — 금속 + 전기 = 전도 · 고무 = 절연 · 젖은 천은 안 탄다 · 유리 + 급한 온도 변화 = 깨짐 ·
// 플라스틱 + 열 = 녹고 유독 연기 · 종이 + 물 = 망가짐 · 얼음 + 열 = 물 · 산소 + 불 = 폭발적.
// 흩어져 있던 규칙(Moisture 누전 · Fire 확산 · Body 칸 상태 · Soil 손때)은 이 표의 함수를 부른다 (값은 그대로).
// 바닥재 · 벽 층은 가까운 "바탕 재질"로 표를 읽는다 (격자 · 금속판 · 외판 = 금속, 타일 = 사기, 카펫 · 단열재 = 천, 패널 · 칸막이 = 플라스틱).
// 새 재질은 Materials.cs enum 끝 + Base() 한 줄 + 표 한 줄. 빈칸은 시험(--mattertest)이 잡는다.

public enum Element : byte { Fire, Heat, Cold, Water, Electric, Oxygen, Smoke, Radiation, Vibration, Pressure }

public enum Reaction : byte
{
    Inert,      // 그대로
    Resist,     // 견딘다 (뜨거워질 뿐 안 탄다)
    Conduct,    // 전한다 (전기 · 열)
    Insulate,   // 막는다 (전기 · 열)
    Burn,       // 탄다
    Char,       // 오래 달궈지면 그을다 연기
    Melt,       // 녹는다 (유독 연기)
    Thaw,       // 녹아 물이 된다
    Freeze,     // 언다
    Brittle,    // 차가우면 부서지기 쉽다
    Crack,      // 금이 간다 (급한 온도 변화 · 떨림)
    Shatter,    // 깨진다 (압력 · 충격)
    Absorb,     // 머금는다
    Ruin,       // 망가진다
    Repel,      // 겉만 젖는다 (닦으면 그만 · 미끄럽다)
    Rust,       // 녹슨다 · 삭는다
    Quench,     // 불을 끈다 · 약하게 한다
    Feed,       // 불을 키운다
    Explode,    // 폭발적
    Stain,      // 그을음이 앉는다
    Taint,      // 냄새 · 독이 밴다
    Spoil,      // 상한다
    Cook,       // 익는다
    Degrade,    // 서서히 삭는다
    Shield,     // 막아 준다 (방사선)
    Rattle,     // 덜컹거리며 움직인다
    Damp,       // 떨림 · 충격을 먹는다
    Scatter,    // 흩날린다
    Boil,       // 끓어 날아간다 (기압이 낮으면)
    Spill,      // 쏟아져 흐른다
    Dent,       // 찌그러진다
    Evaporate,  // 마른다 · 날아간다
    Condense,   // 맺힌다 (이슬 · 서리)
    Expand,     // 부푼다
    Short,      // 합선 · 불안정 (전자기기)
}

/// <summary>표 한 칸: 반응 · 빠르기(배율) · 한 줄 설명 · 바뀌는 재질(얼음 → 액체 같은).</summary>
public readonly record struct MatterRule(Reaction R, float Rate, string Text, Material Into = Material.None)
{
    public bool Defined => Text.Length > 0;
}

public static class Matter
{
    public static readonly Element[] Elements = Enum.GetValues<Element>();
    public static readonly Material[] AllMaterials = Enum.GetValues<Material>();
    public const int ElementCount = 10;

    public static string Name(Element e) => e switch
    {
        Element.Fire => "불", Element.Heat => "열", Element.Cold => "냉기", Element.Water => "물", Element.Electric => "전기",
        Element.Oxygen => "산소", Element.Smoke => "연기", Element.Radiation => "방사선", Element.Vibration => "진동", _ => "압력",
    };

    public static string Name(Reaction r) => r switch
    {
        Reaction.Inert => "그대로", Reaction.Resist => "견딘다", Reaction.Conduct => "전도", Reaction.Insulate => "절연", Reaction.Burn => "탄다",
        Reaction.Char => "그을린다", Reaction.Melt => "녹는다", Reaction.Thaw => "녹아 물", Reaction.Freeze => "언다", Reaction.Brittle => "부서지기 쉽다",
        Reaction.Crack => "금", Reaction.Shatter => "깨진다", Reaction.Absorb => "머금는다", Reaction.Ruin => "망가진다", Reaction.Repel => "겉만 젖는다",
        Reaction.Rust => "녹슨다", Reaction.Quench => "끈다", Reaction.Feed => "불을 키운다", Reaction.Explode => "폭발적", Reaction.Stain => "그을음",
        Reaction.Taint => "밴다", Reaction.Spoil => "상한다", Reaction.Cook => "익는다", Reaction.Degrade => "삭는다", Reaction.Shield => "막는다",
        Reaction.Rattle => "덜컹", Reaction.Damp => "먹는다", Reaction.Scatter => "흩날린다", Reaction.Boil => "끓는다", Reaction.Spill => "쏟아진다",
        Reaction.Dent => "찌그러진다", Reaction.Evaporate => "마른다", Reaction.Condense => "맺힌다", Reaction.Expand => "부푼다", _ => "합선",
    };

    /// <summary>바닥재 · 벽 층을 표의 바탕 재질로 (같은 규칙을 쓴다).</summary>
    public static Material Base(Material m) => m switch
    {
        Material.Grate or Material.MetalPlate or Material.HullPlate => Material.Metal,
        Material.Tile => Material.Ceramic,
        Material.Carpet or Material.Insulation => Material.Fabric,
        Material.Panel or Material.Partition => Material.Plastic,
        _ => m,
    };

    // ───────────────────────────── 표 한 장 ─────────────────────────────

    private static readonly MatterRule[,] Table = BuildTable();
    private static readonly MatterRule[,] Pairs = BuildPairs();
    private static readonly MatterRule[] Chips = BuildElectronic();

    private static MatterRule R(Reaction r, float rate, string text, Material into = Material.None) => new(r, rate, text, into);

    private static MatterRule[,] BuildTable()
    {
        var t = new MatterRule[AllMaterials.Length, ElementCount];
        void Row(Material m, params MatterRule[] rules) { for (int e = 0; e < rules.Length; e++) t[(int)m, e] = rules[e]; }
        //                 불                                         열                                              냉기                                          물                                             전기                                        산소                                     연기                                    방사선                                    진동                                         압력
        Row(Material.None, R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"),
            R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"), R(Reaction.Inert, 0, "없음"));
        Row(Material.Metal,
            R(Reaction.Resist, 0.1f, "뜨거워질 뿐 타지 않는다 — 곁의 것을 달군다"), R(Reaction.Conduct, 1f, "열을 그대로 전한다 — 만지면 덴다"),
            R(Reaction.Condense, 0.6f, "차가운 금속에 이슬 · 서리가 맺혀 미끄럽다"), R(Reaction.Rust, 0.05f, "겉만 젖지만 오래 두면 녹슨다"),
            R(Reaction.Conduct, 1f, "전기가 통한다 — 젖은 금속 바닥은 감전 길"), R(Reaction.Inert, 0f, "산소에 그대로 (천천히 녹슨다)"),
            R(Reaction.Stain, 0.3f, "그을음이 앉는다 (닦으면 된다)"), R(Reaction.Shield, 0.6f, "방사선을 막아 준다"),
            R(Reaction.Rattle, 0.4f, "덜컹거리며 조금씩 움직인다"), R(Reaction.Dent, 0.3f, "충격파에 찌그러진다"));
        Row(Material.Plastic,
            R(Reaction.Melt, 0.8f, "녹으며 타고 유독 연기를 낸다"), R(Reaction.Melt, 0.5f, "달궈지면 녹아 흘러내리고 유독 연기"),
            R(Reaction.Brittle, 0.5f, "얼면 잘 깨진다"), R(Reaction.Repel, 0.1f, "겉만 젖는다"),
            R(Reaction.Insulate, 0.8f, "전기를 막는다"), R(Reaction.Feed, 0.5f, "짙은 산소에서 잘 탄다"),
            R(Reaction.Stain, 0.4f, "그을음이 앉는다"), R(Reaction.Degrade, 0.3f, "방사선에 누렇게 삭는다"),
            R(Reaction.Rattle, 0.5f, "덜컹거리며 움직인다"), R(Reaction.Crack, 0.4f, "충격파에 금이 간다"));
        Row(Material.Fabric,
            R(Reaction.Burn, 1f, "잘 탄다 — 젖은 천은 안 탄다"), R(Reaction.Char, 0.7f, "젖어 있으면 먼저 마르고, 마른 뒤 달궈지면 그을다 연기"),
            R(Reaction.Freeze, 0.4f, "젖은 천은 뻣뻣하게 언다"), R(Reaction.Absorb, 1f, "물을 머금는다 — 넘치면 아래가 젖는다"),
            R(Reaction.Insulate, 0.7f, "마른 천은 전기를 막는다 (젖으면 통한다)"), R(Reaction.Feed, 0.9f, "짙은 산소에서 순식간에 탄다"),
            R(Reaction.Taint, 0.8f, "연기 냄새가 밴다"), R(Reaction.Degrade, 0.2f, "방사선에 삭는다"),
            R(Reaction.Damp, 0.8f, "떨림을 먹는다"), R(Reaction.Scatter, 0.6f, "바람에 날린다"));
        Row(Material.Paper,
            R(Reaction.Burn, 1.3f, "순식간에 탄다"), R(Reaction.Char, 0.9f, "달궈지면 누렇게 그을다 연기"),
            R(Reaction.Inert, 0f, "추위엔 그대로"), R(Reaction.Ruin, 1f, "젖으면 망가진다 (글씨가 번진다)"),
            R(Reaction.Insulate, 0.5f, "마른 종이는 전기를 막는다"), R(Reaction.Feed, 1f, "짙은 산소에서 폭발하듯 탄다"),
            R(Reaction.Stain, 0.6f, "그을음이 앉는다"), R(Reaction.Degrade, 0.2f, "방사선에 바랜다"),
            R(Reaction.Scatter, 0.7f, "떨림에 흩어진다"), R(Reaction.Scatter, 1f, "바람에 흩날린다"));
        Row(Material.Wood,
            R(Reaction.Burn, 0.8f, "잘 탄다"), R(Reaction.Char, 0.5f, "오래 달궈지면 그을린다"),
            R(Reaction.Inert, 0f, "추위엔 그대로"), R(Reaction.Absorb, 0.4f, "물을 먹고 뒤틀린다"),
            R(Reaction.Insulate, 0.7f, "전기를 막는다"), R(Reaction.Feed, 0.7f, "짙은 산소에서 잘 탄다"),
            R(Reaction.Stain, 0.5f, "그을음이 앉는다"), R(Reaction.Inert, 0f, "방사선엔 그대로"),
            R(Reaction.Rattle, 0.3f, "덜컹거린다"), R(Reaction.Crack, 0.3f, "충격파에 갈라진다"));
        Row(Material.Rubber,
            R(Reaction.Burn, 0.6f, "검은 유독 연기를 내며 탄다"), R(Reaction.Melt, 0.3f, "달궈지면 말랑해지고 고무 타는 냄새"),
            R(Reaction.Brittle, 0.6f, "얼면 딱딱하게 갈라진다"), R(Reaction.Repel, 0.05f, "물을 튕긴다"),
            R(Reaction.Insulate, 1f, "전기를 막는다 — 젖어도"), R(Reaction.Feed, 0.4f, "짙은 산소에서 탄다"),
            R(Reaction.Stain, 0.3f, "그을음이 앉는다"), R(Reaction.Degrade, 0.4f, "방사선에 굳어 갈라진다"),
            R(Reaction.Damp, 1f, "떨림을 먹는다"), R(Reaction.Damp, 0.7f, "휘었다 돌아온다"));
        Row(Material.Glass,
            R(Reaction.Crack, 1f, "불길 곁에서 금이 간다"), R(Reaction.Crack, 0.8f, "급한 온도 변화에 깨진다"),
            R(Reaction.Crack, 0.8f, "급히 식으면 깨진다"), R(Reaction.Repel, 0.05f, "겉만 젖는다"),
            R(Reaction.Insulate, 0.9f, "전기를 막는다"), R(Reaction.Inert, 0f, "산소에 그대로"),
            R(Reaction.Stain, 0.5f, "그을음에 흐려진다"), R(Reaction.Degrade, 0.3f, "방사선에 누렇게 흐려진다"),
            R(Reaction.Crack, 0.4f, "떨림에 금이 번진다"), R(Reaction.Shatter, 1f, "충격파에 산산조각"));
        Row(Material.Ceramic,
            R(Reaction.Resist, 0f, "불에 견딘다"), R(Reaction.Resist, 0f, "열에 견딘다"),
            R(Reaction.Crack, 0.4f, "급히 식으면 금이 간다"), R(Reaction.Repel, 0.1f, "겉만 젖는다 (줄눈은 머금는다)"),
            R(Reaction.Insulate, 0.7f, "마르면 전기를 막는다 (젖은 줄눈은 통한다)"), R(Reaction.Inert, 0f, "산소에 그대로"),
            R(Reaction.Stain, 0.3f, "그을음이 앉는다"), R(Reaction.Inert, 0f, "방사선엔 그대로"),
            R(Reaction.Rattle, 0.5f, "달그락거리다 떨어지면 깨진다"), R(Reaction.Shatter, 0.8f, "충격파에 깨진다"));
        Row(Material.Food,
            R(Reaction.Burn, 0.5f, "까맣게 탄다"), R(Reaction.Cook, 1f, "익는다 — 오래면 탄다"),
            R(Reaction.Freeze, 0.5f, "얼면 오래 간다"), R(Reaction.Spoil, 0.8f, "젖으면 불어 상한다"),
            R(Reaction.Inert, 0f, "전기엔 그대로"), R(Reaction.Spoil, 0.1f, "공기에 천천히 상한다"),
            R(Reaction.Taint, 0.8f, "연기 냄새가 밴다"), R(Reaction.Spoil, 0.6f, "방사선에 상한다"),
            R(Reaction.Rattle, 0.3f, "흔들려 쏟아진다"), R(Reaction.Scatter, 0.6f, "충격파에 흩어진다"));
        Row(Material.Liquid,
            R(Reaction.Quench, 1f, "불을 끈다 — 김이 오른다"), R(Reaction.Evaporate, 1f, "마른다 · 김이 된다"),
            R(Reaction.Freeze, 1f, "언다 — 서리 · 얼음이 되어 미끄럽다", Material.Ice), R(Reaction.Spill, 1f, "섞여 번진다"),
            R(Reaction.Conduct, 0.8f, "전기가 통한다 — 고인 물은 감전 길"), R(Reaction.Inert, 0f, "산소에 그대로"),
            R(Reaction.Taint, 0.4f, "그을음이 녹아 더러워진다"), R(Reaction.Shield, 0.5f, "방사선을 막아 준다"),
            R(Reaction.Spill, 0.8f, "출렁여 넘친다"), R(Reaction.Boil, 1f, "기압이 떨어지면 끓어 날아간다"));
        Row(Material.Gas,
            R(Reaction.Explode, 1f, "불꽃에 터진다"), R(Reaction.Expand, 0.6f, "달궈지면 부풀어 통이 터진다"),
            R(Reaction.Condense, 0.4f, "식으면 맺힌다"), R(Reaction.Inert, 0f, "물엔 그대로"),
            R(Reaction.Explode, 1f, "전기 불꽃에 터진다"), R(Reaction.Feed, 1f, "산소와 섞이면 폭발 혼합"),
            R(Reaction.Inert, 0f, "연기와 섞일 뿐"), R(Reaction.Inert, 0f, "방사선엔 그대로"),
            R(Reaction.Inert, 0f, "떨림엔 그대로"), R(Reaction.Scatter, 1f, "기압 차로 흘러 퍼진다"));
        Row(Material.Ice,
            R(Reaction.Thaw, 1.5f, "불에 녹아 물이 된다", Material.Liquid), R(Reaction.Thaw, 1f, "열에 녹아 물이 된다", Material.Liquid),
            R(Reaction.Inert, 0f, "그대로 언 채"), R(Reaction.Freeze, 0.5f, "닿은 물도 언다", Material.Ice),
            R(Reaction.Insulate, 0.5f, "얼음은 전기를 덜 통한다"), R(Reaction.Inert, 0f, "산소에 그대로"),
            R(Reaction.Stain, 0.3f, "그을음이 낀다"), R(Reaction.Inert, 0f, "방사선엔 그대로"),
            R(Reaction.Crack, 0.4f, "떨림에 금이 간다"), R(Reaction.Crack, 0.6f, "충격파에 갈라진다"));
        Row(Material.Oil,
            R(Reaction.Burn, 1.2f, "불이 번진다 — 물을 부으면 튄다"), R(Reaction.Char, 0.6f, "달궈지면 연기를 내다 불붙는다"),
            R(Reaction.Inert, 0f, "식으면 끈적일 뿐"), R(Reaction.Spill, 0.7f, "물 위로 번진다"),
            R(Reaction.Insulate, 0.8f, "전기를 막는다"), R(Reaction.Feed, 0.8f, "짙은 산소에서 잘 탄다"),
            R(Reaction.Inert, 0f, "연기엔 그대로"), R(Reaction.Inert, 0f, "방사선엔 그대로"),
            R(Reaction.Spill, 0.6f, "흔들려 흘러나온다"), R(Reaction.Spill, 0.8f, "충격파에 튄다"));
        Row(Material.Powder,
            R(Reaction.Explode, 1f, "흩날린 가루에 불이 닿으면 분진 폭발"), R(Reaction.Char, 0.4f, "달궈지면 눌어붙는다"),
            R(Reaction.Inert, 0f, "추위엔 그대로"), R(Reaction.Ruin, 0.8f, "젖으면 뭉쳐 못 쓴다"),
            R(Reaction.Explode, 0.8f, "흩날린 가루에 전기 불꽃이 튀면 터진다"), R(Reaction.Feed, 0.8f, "짙은 산소에서 더 터지기 쉽다"),
            R(Reaction.Inert, 0f, "연기엔 그대로"), R(Reaction.Inert, 0f, "방사선엔 그대로"),
            R(Reaction.Scatter, 0.8f, "흔들리면 피어오른다"), R(Reaction.Scatter, 1f, "충격파에 흩날린다"));
        // 바닥재 · 벽 층: 바탕 재질의 줄을 그대로 쓴다 (같은 규칙이 여러 물건에)
        foreach (var m in AllMaterials)
        {
            var b = Base(m);
            if (b == m) continue;
            for (int e = 0; e < ElementCount; e++) t[(int)m, e] = t[(int)b, e];
        }
        return t;
    }

    /// <summary>원소끼리 (대칭): 산소 + 불 = 폭발적 · 물 + 전기 = 누전 · 열 + 냉기 = 급한 온도 변화 …</summary>
    private static MatterRule[,] BuildPairs()
    {
        var p = new MatterRule[ElementCount, ElementCount];
        void Set(Element a, Element b, MatterRule r) { p[(int)a, (int)b] = r; p[(int)b, (int)a] = r; }
        for (int i = 0; i < ElementCount; i++)
            for (int j = 0; j < ElementCount; j++)
                p[i, j] = R(Reaction.Inert, 0f, i == j ? "같은 원소" : "서로 그대로");
        Set(Element.Fire, Element.Heat, R(Reaction.Feed, 1f, "열이 불을 키운다"));
        Set(Element.Fire, Element.Cold, R(Reaction.Quench, 0.5f, "식히면 약해진다"));
        Set(Element.Fire, Element.Water, R(Reaction.Quench, 1f, "물이 불을 끈다 — 김"));
        Set(Element.Fire, Element.Electric, R(Reaction.Feed, 0.6f, "전기 불꽃이 불을 붙인다"));
        Set(Element.Fire, Element.Oxygen, R(Reaction.Explode, 1.5f, "짙은 산소에서 불은 폭발적이다"));
        Set(Element.Fire, Element.Pressure, R(Reaction.Quench, 1f, "기압이 떨어지면 꺼진다"));
        Set(Element.Heat, Element.Cold, R(Reaction.Crack, 1f, "급한 온도 변화 — 유리 · 사기에 금"));
        Set(Element.Heat, Element.Water, R(Reaction.Evaporate, 1f, "김이 오르며 마른다"));
        Set(Element.Heat, Element.Electric, R(Reaction.Feed, 0.4f, "달아오른 전선이 불을 낸다"));
        Set(Element.Heat, Element.Oxygen, R(Reaction.Feed, 0.5f, "짙은 산소는 발화점을 낮춘다"));
        Set(Element.Heat, Element.Smoke, R(Reaction.Expand, 0.6f, "뜨거운 연기는 천장으로 퍼진다"));
        Set(Element.Heat, Element.Pressure, R(Reaction.Expand, 0.5f, "달궈진 공기는 부푼다"));
        Set(Element.Cold, Element.Water, R(Reaction.Freeze, 1f, "언다 — 서리 · 미끄럼"));
        Set(Element.Cold, Element.Smoke, R(Reaction.Condense, 0.3f, "식은 연기는 가라앉는다"));
        Set(Element.Cold, Element.Vibration, R(Reaction.Brittle, 0.5f, "언 것은 떨림에 깨진다"));
        Set(Element.Water, Element.Electric, R(Reaction.Conduct, 3f, "누전 · 감전 — 고인 물은 전기 길")); // Moisture: 누전 3 × 깊이
        Set(Element.Water, Element.Oxygen, R(Reaction.Rust, 0.1f, "물과 산소에 쇠가 녹슨다"));
        Set(Element.Water, Element.Smoke, R(Reaction.Taint, 0.4f, "연기가 물에 녹아 씻긴다"));
        Set(Element.Water, Element.Radiation, R(Reaction.Shield, 0.5f, "물은 방사선을 막는다"));
        Set(Element.Water, Element.Vibration, R(Reaction.Spill, 0.8f, "흔들리면 넘친다"));
        Set(Element.Water, Element.Pressure, R(Reaction.Boil, 1f, "진공에서 끓어 날아간다"));
        Set(Element.Electric, Element.Oxygen, R(Reaction.Explode, 1.2f, "짙은 산소에 불꽃 — 폭발적"));
        Set(Element.Electric, Element.Radiation, R(Reaction.Degrade, 0.3f, "방사선이 회로를 흔든다"));
        Set(Element.Electric, Element.Vibration, R(Reaction.Short, 0.3f, "떨림에 접촉이 끊겼다 붙었다"));
        Set(Element.Smoke, Element.Pressure, R(Reaction.Scatter, 1f, "기압 차로 연기가 흐른다"));
        Set(Element.Vibration, Element.Pressure, R(Reaction.Rattle, 0.6f, "충격파가 흔든다"));
        return p;
    }

    /// <summary>전자기기(물건 · 설비에 붙은 성질): 물 · 열 · 방사선 · 떨림에 불안정하다.</summary>
    private static MatterRule[] BuildElectronic() => new[]
    {
        R(Reaction.Burn, 0.6f, "회로가 타며 유독 연기"), R(Reaction.Short, 0.4f, "달아오르면 불안정하다"), R(Reaction.Inert, 0f, "추위엔 그대로 (배터리는 약해진다)"),
        R(Reaction.Short, 1f, "젖으면 합선 · 오작동"), R(Reaction.Short, 0.6f, "과전압에 탄다"), R(Reaction.Inert, 0f, "산소엔 그대로"),
        R(Reaction.Short, 0.2f, "연기 그을음이 회로에 앉는다"), R(Reaction.Short, 0.5f, "방사선에 오작동"), R(Reaction.Short, 0.3f, "떨림에 접촉 불량"),
        R(Reaction.Crack, 0.4f, "충격파에 기판이 갈라진다"),
    };

    public static MatterRule Rule(Material m, Element e) => Table[(int)m, (int)e];
    public static MatterRule Pair(Element a, Element b) => Pairs[(int)a, (int)b];
    public static MatterRule Electronic(Element e) => Chips[(int)e];
    public static Reaction React(Material m, Element e) => Table[(int)m, (int)e].R;

    // ───────────────────────────── 상태가 붙은 규칙 (보이는 대로 동작) ─────────────────────────────

    /// <summary>물을 머금는 재질 (천 · 종이 · 나무 · 음식 · 가루).</summary>
    public static bool Absorbent(Material m) => React(m, Element.Water) is Reaction.Absorb or Reaction.Ruin or Reaction.Spoil;

    /// <summary>불이 붙는 정도 0~1: 재질 × 젖음(머금은 물이 먼저 끓는다) × 산소.</summary>
    public static float Ignitability(Material m, float wet, float o2 = 21f)
    {
        var r = React(m, Element.Fire);
        if (r is not (Reaction.Burn or Reaction.Melt or Reaction.Explode or Reaction.Feed)) return 0f;
        float b = Materials.Of(m).Burn;
        float damp = Absorbent(m) ? 0.95f : 0.4f;
        float o = o2 <= 21f ? MathF.Max(0f, o2 / 21f) : MathF.Pow(o2 / 21f, 1.5f);
        return Math.Clamp(b * (1f - damp * Math.Clamp(wet, 0f, 1f)) * o, 0f, 2f);
    }

    /// <summary>전기가 통하는 정도 0~1: 재질 + 젖음 (천 · 종이는 젖으면 통하고, 고무는 젖어도 막는다).</summary>
    public static float Conductivity(Material m, float wet)
    {
        var b = Base(m);
        float dry = Materials.Of(m).Conduct;
        float film = b switch
        {
            Material.Rubber => 0.03f, Material.Plastic or Material.Glass or Material.Oil => 0.3f, Material.Ice => 0.25f,
            Material.Ceramic => 0.6f, Material.Fabric or Material.Paper or Material.Wood or Material.Powder => 0.8f, Material.Food => 0.7f,
            Material.Liquid => 0.8f, Material.Metal => 1f, _ => 0.6f,
        };
        return Math.Clamp(MathF.Max(dry, film * Math.Clamp(wet, 0f, 1f)), 0f, 1f);
    }

    /// <summary>v16.3 Body.SpreadMul의 값: 바닥재가 타는 정도 · 젖은 바닥은 덜 (불 확산 배율).</summary>
    public static float FireSpreadMul(Material floor, float wet) => (0.9f + 0.5f * Materials.Of(floor).Burn) * (1f - 0.6f * wet);

    /// <summary>바닥 물 + 살아 있는 전기 (Moisture): 누전 빠르기 = 표의 물 × 전기 × 깊이.</summary>
    public static float ShortRate(float depth) => Pair(Element.Water, Element.Electric).Rate * depth;
    /// <summary>물에 잠긴 설비 불꽃 (전기 화재) · 물에 선 사람 감전 — 같은 칸의 몫.</summary>
    public const float SparkRate = 0.25f, ShockRate = 1.6f;

    /// <summary>젖은 바닥에서 감전되는 배율 (바닥재 표 · 고무는 막는다): 타일 · 금속판 · 카펫 1 · 고무 바닥 ≈ 0.08.</summary>
    public static float ShockMul(Material floor) => Math.Clamp(Conductivity(floor, 1f) / 0.6f, 0.05f, 1f);

    /// <summary>손때가 묻어 남는 정도 (손잡이 · 공구 · 장갑 — 천은 머금고 금속은 덜).</summary>
    public static float Hold(Material m) => 0.2f + 0.7f * Materials.Of(m).Absorb + (Base(m) == Material.Rubber ? 0.15f : 0f);

    /// <summary>그을기 시작하는 온도 (℃ — 게임 값): 천 · 종이 · 나무 · 음식 · 기름 · 가루.</summary>
    public static float CharPoint(Material m) => Base(m) switch
    {
        Material.Paper => 90f, Material.Fabric => 95f, Material.Food => 110f, Material.Oil => 120f, Material.Wood => 140f, Material.Powder => 130f, _ => 9999f,
    };

    /// <summary>녹기 시작하는 온도 (℃): 플라스틱 · 고무 · 얼음.</summary>
    public static float MeltPoint(Material m) => Base(m) switch
    {
        Material.Plastic => 120f, Material.Rubber => 170f, Material.Ice => 0f, _ => 9999f,
    };

    /// <summary>타거나 녹을 때 나는 연기의 독 (플라스틱 · 고무 · 전자기기).</summary>
    public static float ToxicSmoke(Material m, bool electronic = false) => (Base(m) switch { Material.Plastic => 0.6f, Material.Rubber => 0.8f, _ => 0f }) + (electronic ? 0.3f : 0f);

    /// <summary>급한 온도 변화에 견디는 폭 (℃/분): 유리가 가장 약하다.</summary>
    public static float ShockTolerance(Material m) => Base(m) switch
    {
        Material.Glass => 45f, Material.Ceramic => 90f, Material.Ice => 60f, Material.Plastic => 150f, _ => 9999f,
    };

    /// <summary>깨지는 재질 (금 → 깨짐 → 조각).</summary>
    public static bool Breakable(Material m) => React(m, Element.Pressure) is Reaction.Shatter or Reaction.Crack;

    /// <summary>조각이 날카로운가 (유리 · 사기 · 얼음 — 칸 상태 "유리"가 되어 위험물 · 청소 대상).</summary>
    public static bool Sharp(Material m) => Base(m) is Material.Glass or Material.Ceramic;

    /// <summary>표의 문장 한 줄 (화면 · 기록).</summary>
    public static string Say(Material m, Element e) => $"{Materials.Name(m)} + {Name(e)} = {Name(Rule(m, e).R)} ({Rule(m, e).Text})";

    /// <summary>시험 · 화면: 표 전체를 훑어 빈칸 · 모순을 센다.</summary>
    public static List<string> Audit()
    {
        var bad = new List<string>();
        foreach (var m in AllMaterials)
        {
            var spec = Materials.Of(m);
            for (int e = 0; e < ElementCount; e++)
            {
                var r = Table[(int)m, e];
                if (!r.Defined) { bad.Add($"빈칸 {Materials.Name(m)} × {Name((Element)e)}"); continue; }
                if (r.R is Reaction.Thaw or Reaction.Freeze && Base(m) is not (Material.Fabric or Material.Food) && r.Into == Material.None) bad.Add($"{Materials.Name(m)} × {Name((Element)e)}: 바뀌는 재질이 없다");
            }
            if (m == Material.None) continue;
            var el = React(m, Element.Electric);
            if (el == Reaction.Conduct && spec.Conduct < 0.5f) bad.Add($"모순 {Materials.Name(m)}: 전도인데 Conduct {spec.Conduct}");
            if (el == Reaction.Insulate && spec.Conduct > 0.2f) bad.Add($"모순 {Materials.Name(m)}: 절연인데 Conduct {spec.Conduct}");
            var fi = React(m, Element.Fire);
            bool burns = fi is Reaction.Burn or Reaction.Melt or Reaction.Explode or Reaction.Feed;
            if (burns && spec.Burn < 0.2f) bad.Add($"모순 {Materials.Name(m)}: 탄다는데 Burn {spec.Burn}");
            if (fi is Reaction.Resist or Reaction.Crack or Reaction.Quench or Reaction.Thaw && spec.Burn > 0.05f) bad.Add($"모순 {Materials.Name(m)}: 안 탄다는데 Burn {spec.Burn}");
            var wa = React(m, Element.Water);
            if (wa is Reaction.Absorb or Reaction.Ruin && spec.Absorb < 0.25f) bad.Add($"모순 {Materials.Name(m)}: 머금는다는데 Absorb {spec.Absorb}");
            if (wa == Reaction.Repel && spec.Absorb >= 0.25f && Base(m) != Material.Fabric) bad.Add($"모순 {Materials.Name(m)}: 튕긴다는데 Absorb {spec.Absorb}");
            if (CharPoint(m) < 9999f && !burns && fi != Reaction.Burn) bad.Add($"모순 {Materials.Name(m)}: 그을는 온도가 있는데 안 탄다");
            if (MeltPoint(m) < 9999f && React(m, Element.Heat) is not (Reaction.Melt or Reaction.Thaw)) bad.Add($"모순 {Materials.Name(m)}: 녹는 온도가 있는데 열에 안 녹는다");
            if (Breakable(m) && spec.Hard < 0.3f && Base(m) != Material.Plastic) bad.Add($"모순 {Materials.Name(m)}: 깨진다는데 무르다");
        }
        for (int i = 0; i < ElementCount; i++)
            for (int j = 0; j < ElementCount; j++)
            {
                if (!Pairs[i, j].Defined) bad.Add($"빈칸 원소 {Name((Element)i)} × {Name((Element)j)}");
                if (Pairs[i, j] != Pairs[j, i]) bad.Add($"비대칭 {Name((Element)i)} × {Name((Element)j)}");
            }
        for (int e = 0; e < ElementCount; e++) if (!Chips[e].Defined) bad.Add($"빈칸 전자기기 × {Name((Element)e)}");
        // 상태 규칙: 젖은 천은 안 탄다 · 고무는 젖어도 막는다 · 젖은 천은 통한다 · 금속은 통한다
        if (Ignitability(Material.Fabric, 1f) > 0.15f || Ignitability(Material.Fabric, 0f) < 0.5f) bad.Add("모순: 젖은 천이 탄다 / 마른 천이 안 탄다");
        if (Conductivity(Material.Rubber, 1f) > 0.1f) bad.Add("모순: 젖은 고무가 전기를 통한다");
        if (Conductivity(Material.Fabric, 1f) < 0.5f || Conductivity(Material.Fabric, 0f) > 0.1f) bad.Add("모순: 젖은 천 · 마른 천의 전도");
        if (Conductivity(Material.Metal, 0f) < 0.9f) bad.Add("모순: 금속이 전기를 막는다");
        return bad;
    }
}
