using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v19 해법 조합: 공통 동작 여섯 (막기 · 고정하기 · 옮기기 · 연결하기 · 절연하기 · 냉각하기) × 물건의 성질.
//   표(Ways.cs)에 손으로 적은 갈래 말고도, 물건마다 재질 · 무게 · 납작함 · 담긴 것에서 성질을 재고 문제에 필요한 동작과 맞으면
//   그 물건으로 하는 갈래가 저절로 생긴다 — 가루 포대로 파공을 막는다 · 고무 매트를 깔아 화물을 세운다 · 러그에 눕혀 끌고 간다 ·
//   공구함으로 배관을 두드려 신호를 잇는다 · 플라스틱 상자로 튀는 선을 덮는다 · 얼음 덩어리를 불에 던져 넣는다.
//   새 물건 = ArticleSpecs 에 한 줄이면 그 성질대로 쓰인다. 처음 통하면 본 사람들이 "저걸 저렇게 쓰네?" (WaysSystem.Finish),
//   두 번 넘게 통하면 이 배의 관행이 된다.

public enum Verb : byte { Block, Fix, Move, Connect, Insulate, Cool }

public static class WaysCombo
{
    public static string Name(Verb v) => v switch
    {
        Verb.Block => "막기", Verb.Fix => "고정하기", Verb.Move => "옮기기", Verb.Connect => "연결하기", Verb.Insulate => "절연하기", _ => "냉각하기",
    };

    /// <summary>물건 하나의 성질 (0~1): 들러붙음 · 새지 않음 · 버팀 · 미끄럼 잡기 · 끌 판 · 소리 잇기 · 전기 막기 · 몸 덥히기 · 차가움 · 물 · 잘 탐.</summary>
    public readonly record struct Feel(float Conform, float Tight, float Rigid, float Grip, float Sled, float Ring, float Shield, float Warm, float Cold, float Water, float Burn, float Soak);

    public static Feel Of(ArticleKind k)
    {
        var a = ArticleSpecs.Of(k);
        var m = Materials.Of(a.Mat);
        float heavy = Math.Clamp(a.Mass / 8f, 0f, 1f);
        bool liquid = a.Holds == Material.Liquid;
        bool covers = a.Flat || a.Bulk > 0;
        return new Feel(
            Conform: 1f - m.Hard,
            Tight: 1f - m.Absorb,
            Rigid: m.Hard * (0.4f + 0.6f * heavy) * (Matter.Sharp(a.Mat) ? 0.3f : 1f), // 깨지는 건 괴어도 못 버틴다
            Grip: a.Flat ? a.Mu : 0f, // 깔아서 미끄럼을 잡는다
            Sled: a.Flat && a.Tough >= 300f ? 0.5f + 0.5f * (1f - heavy) : 0f, // 눕혀 끌 만큼 질기고 납작하다
            Ring: m.Conduct * m.Hard, // 두드리면 배관을 타고 소리가 간다
            Shield: liquid || m.Conduct > 0.3f || a.Mat == Material.Ice || a.Electronic ? 0f : m.Insulate * (covers ? 1f : 0.5f), // 덮을 만큼 넓고 전기가 안 통한다 (얼음은 녹으면 통한다)
            Warm: liquid || a.Electronic || a.Mat is Material.Powder or Material.Food || m.Hard > 0.35f ? 0f : m.Insulate * (1f - m.Hard), // 두르고 깔 수 있는 부드러운 단열재
            Cold: a.Mat == Material.Ice ? 1f : liquid && a.Contents >= 2f ? 0.45f : 0f,
            Water: liquid && a.Contents >= 2f ? Math.Clamp(a.Contents / 10f, 0f, 1f) : a.Mat == Material.Ice ? 0.8f : 0f, // 한 모금으로는 안 된다
            Burn: m.Burn,
            Soak: m.Absorb * (1f - m.Hard)); // 문틈에 밀어 넣으면 연기를 머금는다
    }

    /// <summary>문제 하나에 쓰는 동작 하나: 실행 규칙 · 그림 · 시간 · 위험 · 품질 식 · 이름 · 효과.</summary>
    private sealed record Use(Snag Snag, Verb Verb, WayFx Fx, float Minutes, float Risk, Func<ArticleKind, Feel, float> Q, Func<string, string> Name,
        Func<ArticleKind, Feel, float, WayGen>? Gen = null, string Side = "", string Later = "", Skill Skill = Skill.Mechanics, float SkillMin = 0f);

    private static readonly Use[] Uses =
    {
        // 막기 — 구멍 · 새는 곳 · 문틈
        new(Snag.Breach, Verb.Block, WayFx.Plug, 4, 0.3f, (k, f) => Big(k) ? WaysRules.PlugQuality(ArticleSpecs.Of(k).Mat, WaysRules.Bulk(k), 0.2f) + (ArticleSpecs.Of(k).Mass >= 10f ? 0.08f : 0f) : 0f,
            n => $"{Ko.EulReul(n)} 구멍에 대고 버틴다", Side: "오래 못 간다", Later: "실링폼으로 다시 막는다"),
        new(Snag.Leak, Verb.Block, WayFx.Generic, 5, 0.05f, (k, f) => f.Conform < 0.6f || f.Tight < 0.4f || ArticleSpecs.Of(k).Holds != Material.None || ArticleSpecs.Of(k).Mat == Material.Food ? 0f : (0.55f * f.Conform + 0.45f * f.Tight) * (ArticleSpecs.Of(k).Flat ? 1f : 0.8f),
            n => $"{Ko.EuRo(n)} 새는 곳을 감싸 조인다", (k, f, q) => new WayGen(Flood: 1f - 0.6f * q), Side: "조금씩 스민다", Later: "배관을 제대로 땜질한다"),
        new(Snag.Smoke, Verb.Block, WayFx.Air, 2, 0.05f, (k, f) => Gap(k, f),
            n => $"{Ko.EuRo(n)} 문틈을 막는다", (k, f, q) => new WayGen(Close: true, Smoke: 1f - 0.2f * q), Side: "그 방은 더 탁해진다"),
        new(Snag.ToxicGas, Verb.Block, WayFx.Air, 2, 0.1f, (k, f) => Gap(k, f),
            n => $"{Ko.EuRo(n)} 문틈을 막아 가둔다", (k, f, q) => new WayGen(Close: true, Toxin: 1f - 0.2f * q), Side: "안에 남은 사람은 못 나온다"),
        // 고정하기 — 미끄러지는 짐
        new(Snag.CargoLoose, Verb.Fix, WayFx.Wedge, 3, 0.2f, (k, f) => ArticleSpecs.Of(k).Mu < 0.3f ? 0f : MathF.Max(0.9f * f.Rigid, 0.85f * f.Grip),
            n => $"{Ko.EulReul(n)} 짐 밑에 괴어 세운다", Side: "괸 물건이 눌린다", Later: "제 고정끈으로 바꾼다"),
        // 옮기기 — 쓰러진 사람
        new(Snag.Injured, Verb.Move, WayFx.Cart, 6, 0.08f, (k, f) => f.Sled,
            n => $"{Ko.EuRo(n)} 눕혀 끌고 간다", Side: "바닥에 쓸려 흔들린다"),
        // 연결하기 — 끊긴 소리길
        new(Snag.CommsDown, Verb.Connect, WayFx.Signal, 3, 0f, (k, f) => f.Ring,
            n => $"{Ko.EuRo(n)} 배관을 두드려 신호를 잇는다"),
        // 절연하기 — 튀는 배선 · 추위
        new(Snag.Spark, Verb.Insulate, WayFx.Generic, 6, 0.2f, (k, f) => f.Shield * (1f - 0.35f * f.Burn),
            n => $"{Ko.EuRo(n)} 튀는 선을 덮어 누른다", Side: "덮은 물건이 그을린다", Later: "차단기를 내리고 제대로 감는다", Skill: Skill.Electrical, SkillMin: 0.15f),
        new(Snag.Cold, Verb.Insulate, WayFx.Generic, 3, 0f, (k, f) => f.Warm,
            n => $"{Ko.EulReul(n)} 두르고 깔고 앉는다", (k, f, q) => new WayGen(Stress: -0.04f * q)),
        // 냉각하기 — 달아오른 설비 · 불
        new(Snag.Overheat, Verb.Cool, WayFx.Generic, 5, 0.1f, (k, f) => f.Cold,
            n => $"{Ko.EuRo(n)} 달아오른 설비를 식힌다", (k, f, q) => new WayGen(Temp: -6f * q), Side: "바닥이 젖는다"),
        new(Snag.Fire, Verb.Cool, WayFx.Douse, 3, 0.3f, (k, f) => f.Water * (f.Cold > 0.9f ? 0.85f : 1f),
            n => $"{Ko.EulReul(n)} 불에 던져 넣는다", Side: "바닥이 젖는다 · 전기 불이면 감전"),
    };

    /// <summary>구멍을 덮을 만큼 큰가 (깔개 · 통로를 막을 덩치) — 전자기기는 대지 않는다.</summary>
    private static bool Big(ArticleKind k) { var a = ArticleSpecs.Of(k); return !a.Electronic && (a.Flat || a.Bulk >= 4); }

    /// <summary>문틈에 밀어 넣어 연기를 머금는 것 (종이 · 천 · 가루 포대) — 너무 무겁거나 먹을 것은 아니다.</summary>
    private static float Gap(ArticleKind k, Feel f) { var a = ArticleSpecs.Of(k); return a.Mat == Material.Food || a.Electronic ? 0f : f.Soak + 0.3f * f.Conform - (a.Mass > 25f ? 0.3f : 0f); }

    /// <summary>표에 없는 조합 갈래 (Id 가운데 ".c." — 생활 물건으로 즉석에서 한다).</summary>
    public static bool Is(Way w) => w.Id.Contains(".c.");

    /// <summary>조합 갈래의 동작.</summary>
    public static Verb? VerbOf(Way w) => Is(w) ? Uses.FirstOrDefault(u => u.Snag == w.Snag && u.Fx == w.Fx)?.Verb : null;

    /// <summary>모든 물건 × 문제마다 쓰는 동작 — 품질이 넉넉하고 손으로 적은 갈래에 없는 물건만.</summary>
    public static Way[] Generate(IReadOnlyList<Way> hand)
    {
        var list = new List<Way>();
        foreach (var u in Uses)
        {
            var written = new HashSet<ArticleKind>(hand.Where(h => h.Snag == u.Snag).SelectMany(h => h.Things));
            foreach (var k in Enum.GetValues<ArticleKind>())
            {
                if (k == ArticleKind.Shards || written.Contains(k)) continue;
                var f = Of(k);
                float q = Math.Clamp(u.Q(k, f), 0f, 1f);
                if (q < 0.4f) continue;
                var spec = ArticleSpecs.Of(k);
                list.Add(new Way($"{WaysTable.Key(u.Snag)}.c.{k.ToString().ToLowerInvariant()}", u.Snag, u.Name(spec.Name), WayBy.Crew, u.Fx, u.Minutes,
                    0.25f + 0.45f * q, // 즉석 변통은 규정 길보다 덜 믿는다
                    Math.Clamp(u.Risk + (u.Verb is Verb.Insulate or Verb.Cool ? 0.2f * f.Burn : 0f), 0f, 0.8f), Look(u, spec))
                {
                    Things = new[] { k },
                    Gen = u.Gen?.Invoke(k, f, q) ?? default,
                    Side = u.Side, Later = u.Later, Skill = u.Skill, SkillMin = u.SkillMin,
                    Line = $"{spec.Name} — {Name(u.Verb)}에 쓸 만하다",
                });
            }
        }
        return list.ToArray();
    }

    private static WayLook Look(Use u, ArticleSpec a) => u.Verb switch
    {
        Verb.Block when u.Snag == Snag.Breach => a.Mat == Material.Rubber ? WayLook.MatPlug : WayLook.CratePlug,
        Verb.Block => a.Mat == Material.Rubber ? WayLook.MatPlug : WayLook.Towel,
        Verb.Fix => WayLook.Wedge,
        Verb.Move => WayLook.Stretcher,
        Verb.Connect => WayLook.Knock,
        Verb.Insulate when u.Snag == Snag.Cold => WayLook.Blanket,
        Verb.Insulate => a.Mat == Material.Rubber ? WayLook.MatPlug : WayLook.Towel,
        _ => u.Snag == Snag.Fire ? WayLook.Bucket : WayLook.ColdStash,
    };
}
