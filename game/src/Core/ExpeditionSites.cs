using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.12 원정 목적지 8종 · 원정 일지 사건 표 50 (결정론 — 원정마다 자기 난수).
// 센서가 찾은 목적지는 "알려진 값"(거리 · 위험 · 예상 수확 · 확실성)과 "실제 값"(Truth · TrueRisk)이 다르다 — 세계 ≠ 사람이 아는 것.
// 사건은 한 줄짜리 이벤트가 아니라 원정대의 구성(솜씨 · 성격 · 관계 · 부상) · 장비 · 날씨 · 해적 · 목적지 종류가 무게를 바꾼다.

public enum SiteKind { Asteroid, IceComet, Wreck, Station, Moon, Debris, Container, Signal }

/// <summary>재료 갈래 여섯 (바닥나면 배가 멈춘다).</summary>
public enum MatCat { Repair, Structure, Parts, Fuel, Water, Food }

public sealed record SiteSpec(SiteKind Kind, string Name, string Hex, float DistMin, float DistMax, float Risk, float Yield, float Certainty,
    (ItemKind k, float w)[] Loot, float Survivor, float Relic, float Info, bool Cutter, string Look, string[] Places, LegKind[] Likes);

/// <summary>센서가 찾은 목적지 하나.</summary>
public sealed class Site
{
    public int Id { get; init; }
    public SiteKind Kind { get; init; }
    public string Name { get; init; } = "";
    /// <summary>편도 거리 (일).</summary>
    public float Dist { get; init; }
    /// <summary>알려진 위험 0~1.</summary>
    public float Risk { get; set; }
    /// <summary>알려진 예상 수확 (개).</summary>
    public float Yield { get; set; }
    /// <summary>확실성 0~1 (센서가 얼마나 또렷이 봤나).</summary>
    public float Certainty { get; set; }
    /// <summary>실제 수확 배율 (아무도 모른다).</summary>
    public float Truth { get; init; }
    /// <summary>실제 위험 (아무도 모른다).</summary>
    public float TrueRisk { get; init; }
    public long Found { get; init; }
    public long Expires { get; set; }
    public string By { get; init; } = "";
    public int Seed { get; init; }
    /// <summary>주 컴퓨터의 평가 (점수 · 한 줄).</summary>
    public float CompScore { get; set; }
    public string CompNote { get; set; } = "";
    public bool Taken { get; set; }
    public SiteSpec Spec => ExpeditionSites.Spec(Kind);
}

public static class ExpeditionSites
{
    private static (ItemKind, float)[] L(params (ItemKind, float)[] x) => x;
    private static LegKind[] K(params LegKind[] k) => k;

    public static readonly SiteSpec[] All =
    {
        new(SiteKind.Asteroid, "소행성", "#b8946a", 0.4f, 1.4f, 0.25f, 11f, 0.65f,
            L((ItemKind.MetalOre, 0.48f), (ItemKind.Silicate, 0.22f), (ItemKind.Carbon, 0.16f), (ItemKind.Ice, 0.08f), (ItemKind.Rare, 0.06f)),
            0f, 0.12f, 0.1f, false, "울퉁불퉁한 감자 모양 바위 · 금속 줄무늬",
            new[] { "2291 바위솔", "소행성 굽은등", "K-17 쇳덩이", "4410 두꺼비", "작은 회색 섬" }, K(LegKind.AsteroidBelt, LegKind.IceRing, LegKind.Narrows)),
        new(SiteKind.IceComet, "얼음 혜성", "#a8e4ff", 0.6f, 1.8f, 0.35f, 13f, 0.55f,
            L((ItemKind.Ice, 0.74f), (ItemKind.Carbon, 0.16f), (ItemKind.Silicate, 0.1f)),
            0f, 0.08f, 0.15f, false, "푸른 얼음 덩이 · 가스 꼬리 · 분출구",
            new[] { "혜성 서리꽃", "C/2071 하얀 꼬리", "얼음 고래", "푸른 수염 혜성" }, K(LegKind.CometTrail, LegKind.IceRing, LegKind.DeepVoid)),
        new(SiteKind.Wreck, "난파선", "#9aa3b5", 0.5f, 2f, 0.42f, 10f, 0.6f,
            L((ItemKind.Plate, 0.17f), (ItemKind.Cable, 0.13f), (ItemKind.Electronics, 0.1f), (ItemKind.Structure, 0.1f), (ItemKind.Motor, 0.06f), (ItemKind.Pump, 0.05f),
              (ItemKind.Bearing, 0.06f), (ItemKind.Sensor, 0.05f), (ItemKind.Valve, 0.05f), (ItemKind.Gasket, 0.05f), (ItemKind.Fuel, 0.06f), (ItemKind.Ration, 0.06f), (ItemKind.MedKit, 0.06f)),
            0.14f, 0.3f, 0.35f, true, "부러진 선체 · 찢긴 격벽 · 꺼진 항해등",
            new[] { "화물선 '물총새'", "채굴선 '곡괭이 3호'", "옛 순찰선 '파수'", "실종선 '해무'", "여객선 '은방울'" }, K(LegKind.Derelict, LegKind.Graveyard, LegKind.TradeLane)),
        new(SiteKind.Station, "버려진 정거장", "#c7b2ff", 1.2f, 2.6f, 0.45f, 12f, 0.55f,
            L((ItemKind.Electronics, 0.14f), (ItemKind.Sensor, 0.08f), (ItemKind.Ration, 0.14f), (ItemKind.MedKit, 0.1f), (ItemKind.Sealant, 0.1f), (ItemKind.Structure, 0.1f),
              (ItemKind.Filter, 0.1f), (ItemKind.Fuel, 0.08f), (ItemKind.PowerController, 0.06f), (ItemKind.Plate, 0.1f)),
            0.16f, 0.4f, 0.5f, true, "바퀴 모양 고리 · 꺼진 태양 날개 · 열린 도킹 문",
            new[] { "중계 정거장 '디딤돌'", "기상 관측소 '바람개비'", "옛 연구 기지 '등불'", "보급 정거장 '곳간'" }, K(LegKind.Lagrange, LegKind.PatrolLane, LegKind.GasGiant)),
        new(SiteKind.Moon, "위성 표면", "#d9c9a3", 1f, 2.2f, 0.32f, 15f, 0.7f,
            L((ItemKind.Ice, 0.32f), (ItemKind.MetalOre, 0.3f), (ItemKind.Silicate, 0.26f), (ItemKind.Rare, 0.06f), (ItemKind.Carbon, 0.06f)),
            0f, 0.15f, 0.2f, false, "곰보 크레이터 · 낮은 지평선 · 미끄러운 표토",
            new[] { "작은 위성 미투리", "얼음 위성 서리", "갈색 위성 누룽지", "위성 반달" }, K(LegKind.GasGiant, LegKind.Perihelion, LegKind.MagneticField)),
        new(SiteKind.Debris, "잔해 구름", "#8f9aa8", 0.2f, 0.9f, 0.5f, 9f, 0.6f,
            L((ItemKind.Plate, 0.3f), (ItemKind.MetalOre, 0.28f), (ItemKind.Cable, 0.14f), (ItemKind.Structure, 0.16f), (ItemKind.Fuse, 0.12f)),
            0.02f, 0.1f, 0.1f, false, "흩어진 파편 · 빙글 도는 판 · 반짝임",
            new[] { "충돌 잔해 띠", "옛 위성 부스러기", "부서진 부표 무리" }, K(LegKind.Graveyard, LegKind.Narrows, LegKind.AsteroidBelt)),
        new(SiteKind.Container, "떠도는 컨테이너", "#e8b45a", 0.2f, 0.9f, 0.12f, 8f, 0.8f,
            L((ItemKind.Ration, 0.24f), (ItemKind.Filter, 0.1f), (ItemKind.Lubricant, 0.08f), (ItemKind.Sealant, 0.1f), (ItemKind.MedKit, 0.08f), (ItemKind.Fuel, 0.12f),
              (ItemKind.Seed, 0.08f), (ItemKind.Nutrient, 0.08f), (ItemKind.Coffee, 0.06f), (ItemKind.Plate, 0.06f)),
            0.03f, 0.18f, 0.12f, false, "줄무늬 화물 상자 · 깜빡이는 표지등",
            new[] { "주인 없는 화물 상자 7호", "떠도는 냉장 컨테이너", "끈 끊어진 화물 묶음" }, K(LegKind.TradeLane, LegKind.Cruise, LegKind.PatrolLane)),
        new(SiteKind.Signal, "낯선 신호", "#7cf0c8", 1f, 2.4f, 0.55f, 9f, 0.25f,
            L((ItemKind.Electronics, 0.18f), (ItemKind.Rare, 0.1f), (ItemKind.Sensor, 0.1f), (ItemKind.Plate, 0.14f), (ItemKind.Ration, 0.12f), (ItemKind.MedKit, 0.1f), (ItemKind.Ice, 0.12f), (ItemKind.Structure, 0.14f)),
            0.2f, 0.45f, 0.6f, false, "맥박치는 고리 · 무늬가 도는 전파",
            new[] { "되풀이되는 신호 원", "숫자를 읽는 목소리", "침묵 사이의 박동" }, K(LegKind.DeepVoid, LegKind.Pulsar, LegKind.Nebula)),
    };

    public static SiteSpec Spec(SiteKind k) => All[(int)k];

    public static string CatName(MatCat c) => c switch
    {
        MatCat.Repair => "수리재", MatCat.Structure => "구조재", MatCat.Parts => "부품", MatCat.Fuel => "연료", MatCat.Water => "물·얼음", _ => "식량",
    };

    /// <summary>이 물건이 어느 재료 갈래를 채우나.</summary>
    public static MatCat? CatOf(ItemKind k)
    {
        switch (k)
        {
            case ItemKind.Plate: case ItemKind.Cable: case ItemKind.Sealant: case ItemKind.Fuse: case ItemKind.Electronics: case ItemKind.MetalOre: case ItemKind.Silicate: case ItemKind.Carbon: return MatCat.Repair;
            case ItemKind.Structure: return MatCat.Structure;
            case ItemKind.Fuel: return MatCat.Fuel;
            case ItemKind.Ice: return MatCat.Water;
            case ItemKind.Ration: case ItemKind.Meal: case ItemKind.Produce: return MatCat.Food;
        }
        return ItemKinds.Tier(k) is ItemTier.General or ItemTier.Advanced ? MatCat.Parts : null;
    }

    /// <summary>목적지 종류마다 어떤 재료 갈래를 채우나 (제안할 때 본다).</summary>
    public static float Fills(SiteKind kind, MatCat cat)
    {
        float s = 0f;
        foreach (var (k, wt) in Spec(kind).Loot) if (CatOf(k) == cat) s += wt;
        if (cat == MatCat.Fuel && kind is SiteKind.IceComet or SiteKind.Moon) s += 0.25f; // 얼음을 녹여 추진제로
        return s;
    }

    public static ItemKind PickLoot(SiteKind kind, Rng r)
    {
        var loot = Spec(kind).Loot;
        float x = r.Float() * loot.Sum(l => l.w);
        foreach (var (k, wt) in loot) { x -= wt; if (x <= 0f) return k; }
        return loot[^1].k;
    }

    // ═══════════════════════════ 원정 일지 사건 50 ═══════════════════════════

    public sealed record Ev(string Id, string Name, SiteKind[]? Kinds, float Weight, Func<ExpCtx, float>? Weigh, Func<ExpCtx, bool> Run);

    private static SiteKind[] S(params SiteKind[] k) => k;

    public static readonly Ev[] Events =
    {
        // ── 어디서나 ──
        new("steady", "순조로운 작업", null, 1.4f, x => 1f + 0.6f * x.Skill, x => x.Line($"{x.Lead.Name}의 손발이 척척 맞았다 — {x.Haul(x.R.Range(2, 5))}", 1)),
        new("rhythm", "손발이 맞는다", null, 0.8f, x => x.Team.Count >= 2 ? 1f + x.BestPair : 0f, x =>
        {
            var (a, b) = x.Pair(true);
            if (a == null || b == null) return false;
            x.Bond(a, b, 0.06f);
            return x.Line($"{Ko.WaGwa(a.Name)} {b.Name}, 말없이도 손이 맞았다 — {x.Haul(x.R.Range(2, 4))}", 1);
        }),
        new("quarrel", "말다툼", null, 0.7f, x => x.Team.Count >= 2 ? MathF.Max(0f, -x.WorstPair) * 4f + 0.15f : 0f, x =>
        {
            var (a, b) = x.Pair(false);
            if (a == null || b == null) return false;
            x.Bond(a, b, -0.07f);
            x.W.Relations.Remember(a, b, RelationReason.BlamedMe, $"원정 중 {b.Name}에게 탓을 들었다");
            return x.Line($"{Ko.WaGwa(a.Name)} {b.Name}, 생명줄 매는 순서로 언성을 높였다 — 반나절을 서로 말없이 일했다 · {x.Haul(1)}", -1);
        }),
        new("tether", "생명줄이 엉켰다", null, 0.8f, null, x => { x.Delay(0.15f); x.StressAll(0.03f); return x.Line("생명줄이 엉켜 한참을 풀었다 — 작업이 밀렸다", 0); }),
        new("scratch", "우주복 긁힘", null, 1f, x => 0.6f + x.Risk, x =>
        {
            var m = x.Any();
            x.Suit(m, x.R.Range(0.06f, 0.14f));
            return x.Line($"{m.Name}의 우주복이 바위에 긁혔다 — 표면 흠집 ({x.SuitStage(m)})", 0);
        }),
        new("tear", "우주복 찢김", null, 0.6f, x => 0.3f + x.Risk * 1.5f, x =>
        {
            var m = x.Any();
            if (x.Use("패치"))
            {
                x.Suit(m, 0.2f);
                return x.Line($"{m.Name}의 우주복이 찢겼다 — 패치로 막았다 (패치 하나 씀 · {x.SuitStage(m)})", -1);
            }
            x.Suit(m, 0.45f);
            x.Hurt(m, 0.12f, "원정 중 감압 (우주복 찢김)");
            return x.Line($"{m.Name}의 우주복이 찢겼는데 패치가 없었다 — 손으로 움켜쥐고 버텼다 ({x.SuitStage(m)} · 귀가 먹먹하다)", -1);
        }),
        new("o2", "산소가 빨리 준다", null, 0.6f, x => 0.4f + x.Risk, x =>
        {
            if (x.Use("산소통")) return x.Line("산소가 예상보다 빨리 줄었다 — 예비 산소통을 열었다", 0);
            x.Shorten(0.4f);
            return x.Line("예비 산소통이 없다 — 일을 줄이고 서둘러 돌아선다", -1);
        }),
        new("toollost", "공구를 놓쳤다", null, 0.6f, x => 0.4f + 0.6f * (1f - x.Skill), x =>
        {
            var m = x.Any();
            x.Lose("공구");
            return x.Line($"{Ko.IGa(m.Name)} 렌치를 놓쳤다 — 빙글빙글 돌며 멀어졌다 (공구 하나 잃음)", -1);
        }),
        new("sprain", "발목을 접질렸다", null, 0.6f, x => 0.4f + x.Fatigue, x =>
        {
            var m = x.Any();
            x.Hurt(m, 0.08f, "원정 중 넘어짐");
            return x.Line($"{Ko.IGa(m.Name)} 발판에서 미끄러져 발목을 접질렸다", -1);
        }),
        new("cut", "날카로운 모서리", null, 0.6f, x => 0.3f + x.Risk, x =>
        {
            var m = x.Any();
            bool kit = x.Use("구급 키트");
            x.Hurt(m, kit ? 0.08f : 0.16f, "원정 중 날카로운 파편");
            return x.Line($"{m.Name}의 팔이 날카로운 모서리에 베였다 — " + (kit ? "구급 키트로 꿰맸다" : "구급 키트가 없어 천으로 동였다"), -1);
        }),
        new("rest", "하루를 쉬었다", null, 0.5f, x => x.Fatigue * 2f, x => { x.Delay(0.4f); x.Tired(-0.2f); return x.Line("다들 지쳐서 바위 그늘에서 하루를 쉬었다", 0); }),
        new("joke", "농담", null, 0.7f, x => x.Team.Count >= 2 ? 0.5f + x.Social : 0f, x =>
        {
            var m = x.Team.OrderByDescending(c => c.Traits.Sociability).First();
            x.StressAll(-0.05f);
            foreach (var o in x.Team) if (o != m) x.Bond(o, m, 0.03f);
            return x.Line($"{m.Name}의 농담에 다들 헬멧 안에서 웃었다", 1);
        }),
        new("homesick", "배가 그립다", null, 0.5f, x => 0.4f + (1f - x.Calm), x =>
        {
            var m = x.Team.OrderBy(c => c.Traits.Calm).First();
            x.Stress(m, 0.06f);
            x.Diary(m, "배의 불빛이 점처럼 보인다. 돌아가면 따뜻한 국부터 먹고 싶다.");
            return x.Line($"{Ko.IGa(m.Name)} 자꾸 배 쪽 하늘을 올려다봤다", 0);
        }),
        new("stars", "별", null, 0.6f, null, x =>
        {
            var m = x.Any();
            x.Stress(m, -0.06f);
            x.Diary(m, "배 밖에서 보는 별은 창으로 보던 것과 다르다.");
            return x.Line($"{Ko.IGa(m.Name)} 작업을 멈추고 한참 별을 봤다", 1);
        }),
        new("bigfind", "큰 덩어리", null, 0.6f, x => 0.5f + x.Site.Truth * 0.5f, x => x.Line($"생각보다 큰 덩어리를 찾았다 — {x.Haul(x.R.Range(4, 7))}", 1)),
        new("falselead", "헛걸음", null, 0.6f, x => 1.4f - x.Site.Certainty, x => { x.Delay(0.25f); return x.Line("센서가 가리킨 곳에 아무것도 없었다 — 헛걸음 (센서 반사가 거짓말을 했다)", -1); }),
        new("teach", "가르쳐 줬다", null, 0.6f, x => x.Team.Count >= 2 ? 1f : 0f, x =>
        {
            var t = x.Team.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
            var s = x.Team.Where(c => c != t).OrderBy(c => c.RawSkill(Skill.Mechanics)).FirstOrDefault();
            if (s == null || t.RawSkill(Skill.Mechanics) - s.RawSkill(Skill.Mechanics) < 0.1f) return false;
            s.Practice(Skill.Mechanics, 0.04f);
            x.W.Relations.Remember(s, t, RelationReason.TaughtMe, $"원정에서 {t.Name}에게 절단 요령을 배웠다");
            return x.Line($"{Ko.IGa(t.Name)} {s.Name}에게 무중력에서 자르는 요령을 가르쳤다", 1);
        }),
        new("save", "붙잡았다", null, 0.45f, x => x.Team.Count >= 2 ? 0.3f + x.Risk : 0f, x =>
        {
            var a = x.Any();
            var b = x.Team.Where(c => c != a).OrderByDescending(c => c.Traits.Bravery).FirstOrDefault();
            if (b == null) return false;
            x.Hurt(a, 0.05f, "원정 중 떠밀림");
            x.Bond(a, b, 0.12f);
            x.W.Relations.Remember(a, b, RelationReason.SavedMe, $"원정에서 떠내려가던 나를 {Ko.IGa(b.Name)} 붙잡았다");
            x.T.Saves.Add((b.Id, a.Id));
            return x.Line($"{a.Name}의 생명줄 고리가 풀렸다 — {Ko.IGa(b.Name)} 몸을 날려 붙잡았다", -1);
        }),
        new("drift", "표류", null, 0.25f, x => x.Risk * 1.2f + (x.Has("드론") ? -0.1f : 0.2f), x =>
        {
            var m = x.Team.OrderBy(c => c.Traits.Calm).First();
            if (x.Has("드론"))
            {
                x.Stress(m, 0.1f);
                return x.Line($"{Ko.IGa(m.Name)} 생명줄이 끊겨 떠내려갔다 — 드론이 쫓아가 끌고 왔다 (식은땀)", -1);
            }
            if (x.Team.Count >= 2 && x.R.Chance(0.75f))
            {
                var b = x.Team.Where(c => c != m).OrderByDescending(c => c.Traits.Bravery).First();
                x.Bond(m, b, 0.15f);
                x.W.Relations.Remember(m, b, RelationReason.SavedMe, $"원정에서 표류하던 나를 {Ko.IGa(b.Name)} 건져 왔다");
                x.T.Saves.Add((b.Id, m.Id));
                x.Delay(0.3f);
                return x.Line($"{Ko.IGa(m.Name)} 표류했다 — {Ko.IGa(b.Name)} 추진팩으로 쫓아가 건져 왔다", -1);
            }
            x.Missing(m, "생명줄이 끊겨 표류 — 무전이 점점 약해졌다");
            return x.Line($"{Ko.IGa(m.Name)} 생명줄이 끊겨 떠내려갔다 — 무전이 점점 약해지다 끊겼다", -2);
        }),
        new("dronehelp", "드론이 끌었다", null, 0.7f, x => x.Has("드론") ? 1f : 0f, x => x.Line($"드론이 무거운 덩어리를 끌어왔다 — {x.Haul(x.R.Range(3, 5))}", 1)),
        new("dronelost", "드론을 잃었다", null, 0.3f, x => x.Has("드론") ? 0.4f + x.Risk : 0f, x =>
        {
            x.Lose("드론");
            x.T.DroneLost = true;
            return x.Line("드론의 추진기가 멎었다 — 빙글빙글 돌며 어둠 속으로 사라졌다 (드론 잃음)", -1);
        }),
        new("cutterjam", "절단기가 멎었다", null, 0.4f, x => x.Has("절단기") ? 0.6f : 0f, x => { x.Lose("절단기"); return x.Line("절단기 날이 부러졌다 — 남은 일은 손으로 뜯었다 (수확이 준다)", -1); }),
        new("storm", "태양 폭풍", null, 0.2f, x => x.W.Ambience.StormPower > 0.3f ? 8f : 0.3f, x =>
        {
            x.Delay(0.5f);
            foreach (var c in x.Team) c.Dose += 0.25f;
            x.StressAll(0.06f);
            return x.Line("태양 폭풍 — 바위 그늘에 몸을 붙이고 반나절을 기다렸다 (방사선)", -1);
        }),
        new("micrometeor", "미세 운석", null, 0.35f, x => x.Risk, x =>
        {
            var m = x.Any();
            x.Suit(m, 0.25f);
            x.Hurt(m, 0.14f, "원정 중 미세 운석 파편");
            return x.Line($"미세 운석이 쏟아졌다 — {m.Name}의 어깨를 스쳤다 ({x.SuitStage(m)})", -2);
        }),
        new("hungry", "식량이 모자라다", null, 0.4f, x => x.Count("식량") < x.Team.Count ? 2f : 0.1f, x =>
        {
            if (x.Use("식량")) return x.Line("식량을 아껴 먹었다 — 반 봉지씩 나눴다", 0);
            foreach (var c in x.Team) c.Needs.Food = MathF.Max(0f, c.Needs.Food - 0.3f);
            x.StressAll(0.05f);
            return x.Line("식량이 떨어졌다 — 빈속으로 일했다", -1);
        }),
        new("fever", "몸살", null, 0.3f, x => 0.3f + x.Fatigue, x =>
        {
            var m = x.Team.OrderBy(c => c.Needs.Rest).First();
            x.T.Sick.Add(m.Id);
            return x.Line($"{Ko.IGa(m.Name)} 으슬으슬 떨었다 — 몸살 기운", -1);
        }),
        new("pirate", "해적선 그림자", null, 0.2f, x => x.W.Expedition.Halted ? 1.5f * x.W.Expedition.OutsideMul("해적") : 0.2f, x =>
        {
            x.Delay(0.4f);
            x.StressAll(0.08f);
            x.T.PirateSeen = true;
            return x.Line("해적선 그림자가 지나갔다 — 불을 다 끄고 숨죽였다 (무전도 끊었다)", -1);
        }),
        new("static", "무전이 끊겼다", null, 0.35f, null, x => { x.T.RadioBlackout = x.W.Tick + SimTime.Hours(20); return x.Line("무전기가 지직거리기만 한다 — 배에 소식을 못 전했다", -1); }),
        new("watch", "번갈아 불침번", null, 0.5f, x => x.Team.Count >= 2 ? 1f : 0f, x =>
        {
            for (int i = 0; i + 1 < x.Team.Count; i++) x.Bond(x.Team[i], x.Team[i + 1], 0.04f);
            return x.Line("밤에는 번갈아 불침번을 섰다 — 서로의 숨소리를 들으며", 1);
        }),
        new("alone", "혼자 남은 밤", null, 0.6f, x => x.Team.Count == 1 ? 1.5f : 0f, x =>
        {
            var m = x.Team[0];
            x.Stress(m, 0.1f);
            x.Diary(m, "혼자다. 무전기 불빛만 깜빡인다.");
            return x.Line($"{Ko.EunNeun(m.Name)} 혼자서 밤을 새웠다 — 무전기만 붙들고", -1);
        }),

        // ── 소행성 ──
        new("spin", "빙글 도는 바위", S(SiteKind.Asteroid), 1f, null, x => { x.Delay(0.2f); return x.Line($"바위가 빙글 돈다 — 그늘과 햇빛이 번갈아 오가 닻을 다시 박았다 · {x.Haul(2)}", 0); }),
        new("dust", "먼지 구름", S(SiteKind.Asteroid, SiteKind.Moon), 0.8f, null, x => { x.T.Dusty += 0.3f; return x.Line($"먼지가 피어올라 헬멧이 뿌옇다 — 손으로 더듬어 캤다 · {x.Haul(2)}", 0); }),
        new("vein", "희귀 광맥", S(SiteKind.Asteroid, SiteKind.Moon, SiteKind.Debris), 0.5f, x => x.Site.Truth, x =>
        {
            int n = x.R.Range(1, 3);
            x.Add(ItemKind.Rare, n);
            return x.Line($"반짝이는 광맥 — 희귀 소재 {n}개를 떼어 냈다", 2);
        }),
        // ── 얼음 혜성 ──
        new("geyser", "가스 분출", S(SiteKind.IceComet), 1f, x => 0.6f + x.Risk, x =>
        {
            if (x.R.Chance(0.5f + 0.3f * x.Calm))
            {
                x.Add(ItemKind.Ice, 5);
                return x.Line("분출구가 터졌다 — 미리 비켜서 뿜어 나온 얼음을 그물로 받았다 (얼음 5)", 1);
            }
            var m = x.Any();
            x.Suit(m, 0.2f);
            x.Hurt(m, 0.1f, "원정 중 가스 분출에 튕김");
            return x.Line($"분출구가 발밑에서 터졌다 — {Ko.IGa(m.Name)} 튕겨 나갔다 ({x.SuitStage(m)})", -1);
        }),
        new("clearice", "맑은 얼음", S(SiteKind.IceComet, SiteKind.Moon), 0.9f, null, x => { x.Add(ItemKind.Ice, 4); return x.Line("투명한 얼음층 — 녹이면 바로 마실 물 (얼음 4)", 1); }),
        // ── 난파선 ──
        new("logbook", "항해 일지", S(SiteKind.Wreck, SiteKind.Station), 0.8f, null, x =>
        {
            x.T.Infos++;
            return x.Line("함교에서 항해 일지를 찾았다 — 이 근처 다른 난파선 좌표가 적혀 있다", 2);
        }),
        new("remains", "유해", S(SiteKind.Wreck, SiteKind.Station), 0.6f, null, x =>
        {
            x.StressAll(0.07f);
            foreach (var c in x.Team.Where(c => Life.Has(c, Habit.Superstitious))) x.Stress(c, 0.08f);
            x.T.Remains = true;
            return x.Line("승무원 유해를 찾았다 — 이름표를 떼어 왔다 (돌아가면 이름을 불러 주자)", -1);
        }),
        new("survivor", "살아 있는 사람", S(SiteKind.Wreck, SiteKind.Station, SiteKind.Signal), 0.3f, x => x.Site.Spec.Survivor * 6f, x =>
        {
            if (x.T.Survivors >= 1) return false;
            x.T.Survivors++;
            x.StressAll(-0.08f);
            return x.Line("동면 캡슐 하나가 아직 돌고 있었다 — 안에 사람이 살아 있다! 데려간다", 2);
        }),
        new("usedparts", "쓸 만한 부품", S(SiteKind.Wreck, SiteKind.Station), 1f, x => x.Has("절단기") ? 1.3f : 0.6f, x =>
        {
            var k = x.R.Pick(new[] { ItemKind.Motor, ItemKind.Pump, ItemKind.Bearing, ItemKind.Sensor, ItemKind.Valve, ItemKind.Gasket, ItemKind.Relay });
            x.Add(k, 1);
            x.T.UsedParts.Add(k);
            return x.Line($"기관실에서 {Ko.EulReul(ItemKinds.Name(k))} 떼어 냈다 — 검사 전엔 믿을 수 없다 (중고)", 1);
        }),
        new("leak", "연료 누출", S(SiteKind.Wreck), 0.4f, x => x.Risk, x =>
        {
            var m = x.Any();
            x.Hurt(m, 0.12f, "원정 중 연료 증기 화상");
            x.Add(ItemKind.Fuel, 1);
            return x.Line($"난파선 연료관이 새고 있었다 — {Ko.IGa(m.Name)} 증기에 데었다 · 남은 연료통 하나는 건졌다", -1);
        }),
        new("hull", "통째 떼어 온 판", S(SiteKind.Wreck, SiteKind.Debris), 0.7f, x => x.Has("절단기") ? 1.5f : 0.3f, x =>
        {
            x.Add(ItemKind.Plate, 3); x.Add(ItemKind.Structure, 1);
            return x.Line("절단기로 선체 판을 통째 떼어 왔다 (금속판 3 · 구조재 1)", 1);
        }),
        // ── 정거장 ──
        new("cache", "비상 창고", S(SiteKind.Station), 0.8f, null, x =>
        {
            x.Add(ItemKind.Ration, 3); x.Add(ItemKind.MedKit, 1);
            return x.Line("잠긴 비상 창고를 열었다 — 비상식량 3 · 구급 키트 1", 1);
        }),
        new("relic", "옛 기념물", S(SiteKind.Station, SiteKind.Wreck, SiteKind.Signal, SiteKind.Container), 0.5f, x => x.Site.Spec.Relic * 3f * TechWeb.Mul(x.W, "exp.relic"), x => // v16.14 잔해 탐지기
        {
            if (x.T.Relic != null) return false;
            x.T.Relic = x.Site.Kind switch { SiteKind.Station => "정거장 개소 기념패", SiteKind.Wreck => $"{x.Site.Name} 명판", SiteKind.Container => "누군가의 엽서 묶음", _ => "신호기 부속 한 조각" };
            return x.Line($"{Ko.EulReul(x.T.Relic)} 챙겼다 — 배에 걸어 두자", 2);
        }),
        new("power", "전원이 살아났다", S(SiteKind.Station), 0.4f, x => x.Skill, x =>
        {
            x.T.Infos++;
            x.Add(ItemKind.Electronics, 2);
            return x.Line("정거장 예비 전원을 살렸다 — 기록 장치를 통째 내려받았다 (연구 · 전자재 2)", 2);
        }),
        // ── 위성 ──
        new("lowg", "낮은 중력", S(SiteKind.Moon, SiteKind.Asteroid), 0.7f, x => x.Team.Count >= 2 ? 1f : 0.3f, x =>
        {
            x.StressAll(-0.04f);
            return x.Line("낮은 중력에서 껑충껑충 — 누가 먼저 넘어지나 내기를 했다", 1);
        }),
        new("regolith", "표토", S(SiteKind.Moon), 0.8f, null, x => { x.T.Dusty += 0.4f; return x.Line($"고운 표토가 우주복 이음매마다 끼었다 · {x.Haul(3)}", 0); }),
        // ── 잔해 구름 ──
        new("fastdebris", "빠른 파편", S(SiteKind.Debris), 1f, x => 0.5f + x.Risk, x =>
        {
            var m = x.Any();
            x.Suit(m, 0.3f);
            return x.Line($"빠른 파편이 {m.Name}의 헬멧 바이저를 긁고 지나갔다 ({x.SuitStage(m)})", -1);
        }),
        // ── 컨테이너 ──
        new("sealed", "봉인 상자", S(SiteKind.Container), 1f, null, x =>
        {
            var k = x.R.Pick(new[] { ItemKind.Coffee, ItemKind.Spice, ItemKind.Ration, ItemKind.Soap, ItemKind.Bandage, ItemKind.Vitamin });
            x.Add(k, 3);
            return x.Line($"봉인을 뜯으니 {ItemKinds.Name(k)} 3 — 이런 것도 귀하다", 1);
        }),
        new("seeds", "종자", S(SiteKind.Container, SiteKind.Station), 0.5f, null, x => { x.Add(ItemKind.Seed, 3); return x.Line("얼어붙은 종자 꾸러미 — 재배대에 심어 보자 (종자 3)", 1); }),
        // ── 낯선 신호 ──
        new("truth", "신호의 정체", S(SiteKind.Signal), 1f, null, x =>
        {
            x.T.Infos++;
            return x.Line("신호의 정체 — 수십 년 전 실종선의 자동 표지였다 · 녹음된 마지막 교신을 받아 적었다", 2);
        }),
        new("eerie", "소름", S(SiteKind.Signal, SiteKind.Wreck), 0.6f, null, x =>
        {
            foreach (var c in x.Team) x.Stress(c, Life.Has(c, Habit.Superstitious) ? 0.12f : 0.04f);
            return x.Line("무전에 우리 목소리가 몇 초 늦게 되돌아왔다 — 다들 소름이 돋았다", -1);
        }),
        new("shuttlefind", "쓸 만한 셔틀", S(SiteKind.Wreck, SiteKind.Station), 0.12f, null, x =>
        {
            if (x.W.Expedition.HasShuttle || x.T.ShuttleFound) return false;
            x.T.ShuttleFound = true;
            return x.Line("격납고에 멀쩡한 작은 셔틀이 한 대 — 몰고 간다! 다음엔 이걸로 나간다", 2);
        }),
        // ── 셔틀 ──
        new("shuttledent", "착륙 다리", null, 0.4f, x => x.T.Shuttle ? 0.6f + x.Risk : 0f, x =>
        {
            x.T.ShuttleDamage += 0.15f;
            return x.Line("셔틀 착륙 다리가 바위에 걸려 휘었다 — 돌아가서 펴야 한다", -1);
        }),
        new("shuttlehold", "셔틀 짐칸", null, 0.6f, x => x.T.Shuttle ? 1f : 0f, x => x.Line($"셔틀 짐칸에 한가득 실었다 — {x.Haul(x.R.Range(4, 7))}", 1)),
    };

    public static int EventCount => Events.Length;
}

/// <summary>원정 일지 사건 하나를 굴리는 자리.</summary>
public sealed class ExpCtx
{
    public World W { get; }
    public Trip T { get; }
    public Rng R { get; }
    public Site Site => T.Site;
    public List<CrewMember> Team { get; }
    public string? Text { get; private set; }
    public int Tone { get; private set; }

    public ExpCtx(World w, Trip t, Rng r, List<CrewMember> team) { W = w; T = t; R = r; Team = team; }

    public CrewMember Lead => Team.FirstOrDefault(c => c.Id == T.Leader) ?? Team[0];
    public CrewMember Any() => Team[R.Range(0, Team.Count)];
    public float Risk => T.RiskNow(W);
    public float Skill => Team.Count == 0 ? 0f : Team.Average(c => MathF.Max(c.SkillLevel(Core.Skill.Mechanics), c.SkillLevel(Core.Skill.Engineering)));
    public float Calm => Team.Count == 0 ? 0.5f : Team.Average(c => c.Traits.Calm);
    public float Social => Team.Count == 0 ? 0.5f : Team.Average(c => c.Traits.Sociability);
    public float Fatigue => Team.Count == 0 ? 0f : Team.Average(c => 1f - c.Needs.Rest);
    public float BestPair => Pairs().Select(p => p.a.AffinityTo(p.b)).DefaultIfEmpty(0f).Max();
    public float WorstPair => Pairs().Select(p => p.a.AffinityTo(p.b)).DefaultIfEmpty(0f).Min();
    private IEnumerable<(CrewMember a, CrewMember b)> Pairs()
    {
        for (int i = 0; i < Team.Count; i++)
            for (int j = i + 1; j < Team.Count; j++) yield return (Team[i], Team[j]);
    }
    public (CrewMember?, CrewMember?) Pair(bool best)
    {
        var ps = Pairs().ToList();
        if (ps.Count == 0) return (null, null);
        var p = best ? ps.OrderByDescending(q => q.a.AffinityTo(q.b) + q.b.AffinityTo(q.a)).First() : ps.OrderBy(q => q.a.AffinityTo(q.b) + q.b.AffinityTo(q.a)).First();
        return (p.a, p.b);
    }

    public bool Line(string text, int tone) { Text = text; Tone = tone; return true; }
    public bool Has(string gear) => T.Gear.TryGetValue(gear, out var n) && n > 0;
    public int Count(string gear) => T.Gear.TryGetValue(gear, out var n) ? n : 0;
    public bool Use(string gear) { if (!Has(gear)) return false; T.Gear[gear]--; T.Used[gear] = T.Used.GetValueOrDefault(gear) + 1; return true; }
    public void Lose(string gear) { if (!Has(gear)) return; T.Gear[gear]--; T.Lost.Add(gear); }
    public void Delay(float days) => T.ReturnAt += SimTime.Hours(days * 24f);
    public void Shorten(float days) => T.ReturnAt = Math.Max(W.Tick + SimTime.Hours(6), T.ReturnAt - SimTime.Hours(days * 24f));
    public void Add(ItemKind k, int n) { if (n > 0) T.Loot[k] = T.Loot.GetValueOrDefault(k) + n; }

    /// <summary>목적지에서 캔다 (실제 배율 · 솜씨 · 절단기 · 셔틀이 몫을 바꾼다).</summary>
    public string Haul(int n)
    {
        float mul = Site.Truth * (0.7f + 0.6f * Skill) * (Site.Spec.Cutter && !Has("절단기") ? 0.6f : 1f) * (T.Shuttle ? 1.5f : 1f) * (1f + 0.1f * (Team.Count - 1));
        int got = Math.Max(0, (int)MathF.Round(n * mul));
        var names = new Dictionary<ItemKind, int>();
        for (int i = 0; i < got; i++)
        {
            var k = ExpeditionSites.PickLoot(Site.Kind, R);
            names[k] = names.GetValueOrDefault(k) + 1;
            Add(k, 1);
        }
        return got == 0 ? "건진 것 없음" : string.Join(" · ", names.OrderBy(kv => (int)kv.Key).Select(kv => $"{ItemKinds.Name(kv.Key)} {kv.Value}"));
    }

    public TripMember M(CrewMember c) => T.Members.First(m => m.Id == c.Id);
    public void Suit(CrewMember c, float dmg) { var m = M(c); m.SuitDamage = MathF.Min(1f, m.SuitDamage + dmg); }
    public string SuitStage(CrewMember c) => TripMember.Stage(M(c).SuitDamage);
    public void Hurt(CrewMember c, float amount, string cause)
    {
        var m = M(c);
        m.Hurt += amount;
        m.HurtWhy.Add(cause);
        NeedsSystem.AddInjury(c.Vitals, amount, cause);
        c.Vitals.Health = MathF.Max(0.15f, c.Vitals.Health - amount * 0.8f);
    }
    public void Missing(CrewMember c, string why) { var m = M(c); m.Missing = true; m.MissingWhy = why; Team.Remove(c); }
    public void Bond(CrewMember a, CrewMember b, float d) { a.ChangeAffinity(b, d); b.ChangeAffinity(a, d); if (d > 0f) T.Hardship += d; }
    public void Stress(CrewMember c, float d) => c.Needs.Stress = Math.Clamp(c.Needs.Stress + d, 0f, 1f);
    public void StressAll(float d) { foreach (var c in Team) Stress(c, d); }
    public void Tired(float d) { foreach (var c in Team) c.Needs.Rest = Math.Clamp(c.Needs.Rest - d, 0.05f, 1f); }
    public void Diary(CrewMember c, string text) => Life.Diary(W, c, Persona.Say(c, text));
}
