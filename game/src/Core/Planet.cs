using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 행성: 요소를 조합해 만드는 행성 + 이름 붙은 특별 행성. 생물은 없다 — 환경만으로 극한.
//   요소: 온도 · 대기 / 지표 · 중력 / 날씨 / 땅 · 하늘 현상과 그 밖의 것(자기장 · 고리 · 위성 · 조석 고정 · 수정 지대 · 유독 먼지 · 정전기 …).
//   난이도(이야기꾼 단계)가 높을수록 험한 조합이 잦다. 원거리 관측은 흐리다 — 아는 값(KnownDanger · KnownReward)과 실제 값이 다르고, 숨은 현상(Hidden)은 내려가 봐야 안다.

public enum PlanetClimate : byte { Frozen, Cold, Mild, Hot, Scorched, Airless }
public enum PlanetSurface : byte { Rock, Dunes, IceSheet, Shallows, Lava, Crystal, Mud, Ash }
public enum PlanetWeather : byte { Calm, Wind, DustStorm, Rain, AcidRain, Thunder, Typhoon, Blizzard, HeatWave }
public enum PlanetFeature : byte { Quakes, Geysers, Vents, MeteorRain, Aurora, Tides, Sinkholes, Static, NoMagnet, StrongMagnet, Rings, Moons, TidalLock, CrystalField, ToxicDust }

public sealed class Planet
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>항성 목록 이름 (예: 케플러-418 c).</summary>
    public string Code { get; init; } = "";
    /// <summary>이름 붙은 특별 행성의 열쇠 (보통 행성은 "").</summary>
    public string Key { get; init; } = "";
    public int Seed { get; init; }
    public PlanetClimate Climate { get; init; }
    public PlanetSurface Surface { get; init; }
    public float Gravity { get; init; } = 1f;
    /// <summary>한낮 평균 기온 (°C).</summary>
    public float TempC { get; init; }
    /// <summary>낮밤 기온 차 (°C).</summary>
    public float Swing { get; init; }
    /// <summary>대기압 (kPa — 0이면 대기 없음).</summary>
    public float Pressure { get; init; }
    /// <summary>날씨 (같은 것이 여러 번 들어가면 그만큼 잦다).</summary>
    public List<PlanetWeather> Weathers { get; } = new();
    public List<PlanetFeature> Features { get; } = new();
    /// <summary>멀리서는 못 본 현상 (내려가면 드러난다).</summary>
    public List<PlanetFeature> Hidden { get; } = new();
    /// <summary>자원 풍부함 0~1.</summary>
    public Dictionary<ItemKind, float> Rich { get; } = new();
    public float Danger { get; set; }
    public float Reward { get; set; }
    public float KnownDanger { get; set; }
    public float KnownReward { get; set; }
    public float Certainty { get; set; }
    /// <summary>임무 기간 (일).</summary>
    public float StayDays { get; init; }
    public string Hex { get; init; } = "#888888";
    public string Hex2 { get; init; } = "#444444";
    public string Line { get; set; } = "";
    public float CompScore { get; set; }
    public string CompNote { get; set; } = "";

    public bool Has(PlanetFeature f) => Features.Contains(f);
    public bool Special => Key.Length > 0;
    public string Title => Special ? Name : $"{Name} ({Code})";
}

public static class PlanetGen
{
    public static string ClimateName(PlanetClimate c) => c switch
    {
        PlanetClimate.Frozen => "얼어붙은", PlanetClimate.Cold => "서늘한", PlanetClimate.Mild => "온화한", PlanetClimate.Hot => "뜨거운",
        PlanetClimate.Scorched => "타는 듯한", _ => "대기 없는",
    };
    public static string SurfaceName(PlanetSurface s) => s switch
    {
        PlanetSurface.Rock => "바위 들판", PlanetSurface.Dunes => "모래 언덕", PlanetSurface.IceSheet => "얼음판", PlanetSurface.Shallows => "얕은 바다와 섬",
        PlanetSurface.Lava => "용암 들판", PlanetSurface.Crystal => "수정 골짜기", PlanetSurface.Mud => "진흙 늪", _ => "화산재 벌판",
    };
    public static string WeatherName(PlanetWeather w) => w switch
    {
        PlanetWeather.Calm => "맑음", PlanetWeather.Wind => "강풍", PlanetWeather.DustStorm => "모래 폭풍", PlanetWeather.Rain => "비",
        PlanetWeather.AcidRain => "산성비", PlanetWeather.Thunder => "뇌우", PlanetWeather.Typhoon => "태풍", PlanetWeather.Blizzard => "눈보라", _ => "폭염",
    };
    public static string FeatureName(PlanetFeature f) => f switch
    {
        PlanetFeature.Quakes => "잦은 지진", PlanetFeature.Geysers => "간헐천", PlanetFeature.Vents => "화산 분기공", PlanetFeature.MeteorRain => "유성 비",
        PlanetFeature.Aurora => "오로라", PlanetFeature.Tides => "큰 밀물", PlanetFeature.Sinkholes => "꺼지는 땅", PlanetFeature.Static => "정전기",
        PlanetFeature.NoMagnet => "자기장 없음 (방사선)", PlanetFeature.StrongMagnet => "강한 자기장", PlanetFeature.Rings => "고리", PlanetFeature.Moons => "위성 여럿",
        PlanetFeature.TidalLock => "조석 고정 (늘 낮 · 늘 밤)", PlanetFeature.CrystalField => "수정 지대", _ => "유독 먼지",
    };

    private static float WeatherDanger(PlanetWeather w) => w switch
    {
        PlanetWeather.Wind => 0.05f, PlanetWeather.DustStorm => 0.12f, PlanetWeather.Rain => 0.03f, PlanetWeather.AcidRain => 0.18f, PlanetWeather.Thunder => 0.2f,
        PlanetWeather.Typhoon => 0.3f, PlanetWeather.Blizzard => 0.2f, PlanetWeather.HeatWave => 0.18f, _ => 0f,
    };
    private static float FeatureDanger(PlanetFeature f) => f switch
    {
        PlanetFeature.Quakes => 0.12f, PlanetFeature.Geysers => 0.06f, PlanetFeature.Vents => 0.1f, PlanetFeature.MeteorRain => 0.15f, PlanetFeature.Tides => 0.06f,
        PlanetFeature.Sinkholes => 0.1f, PlanetFeature.Static => 0.1f, PlanetFeature.NoMagnet => 0.12f, PlanetFeature.StrongMagnet => 0.03f, PlanetFeature.Rings => 0.02f,
        PlanetFeature.TidalLock => 0.08f, PlanetFeature.CrystalField => 0.05f, PlanetFeature.ToxicDust => 0.1f, _ => 0f,
    };

    /// <summary>실제 위험 0~1 (기후 · 지표 · 중력 · 날씨 · 현상).</summary>
    public static float DangerOf(Planet p)
    {
        float d = p.Climate switch { PlanetClimate.Frozen => 0.35f, PlanetClimate.Cold => 0.15f, PlanetClimate.Mild => 0.05f, PlanetClimate.Hot => 0.3f, PlanetClimate.Scorched => 0.55f, _ => 0.25f };
        d += p.Surface switch { PlanetSurface.Shallows => 0.15f, PlanetSurface.Lava => 0.3f, PlanetSurface.Crystal => 0.1f, PlanetSurface.Mud => 0.1f, PlanetSurface.Ash => 0.15f, PlanetSurface.Rock => 0f, _ => 0.05f };
        d += MathF.Abs(p.Gravity - 1f) * 0.25f + MathF.Max(0f, p.Gravity - 1.3f) * 0.3f;
        d += p.Weathers.Distinct().Sum(WeatherDanger) * 0.7f;
        d += p.Features.Sum(FeatureDanger);
        if (p.Pressure > 1000f) d += 0.15f;
        return Math.Clamp(d * 0.62f, 0f, 1f);
    }

    /// <summary>실제 보상 0~1 (자원).</summary>
    public static float RewardOf(Planet p)
    {
        float r = 0f;
        foreach (var (k, v) in p.Rich) r += v * k switch { ItemKind.Rare => 3f, ItemKind.Fuel => 1.2f, ItemKind.MetalOre => 1f, ItemKind.Ice => 1f, ItemKind.Carbon => 0.7f, _ => 0.5f };
        if (p.Special) r += 0.6f;
        return Math.Clamp(r / 4.2f, 0f, 1f);
    }

    private static T Pick<T>(Rng r, params (T v, float w)[] opts)
    {
        float sum = opts.Sum(o => MathF.Max(0f, o.w));
        float x = r.Range(0f, sum);
        foreach (var (v, wt) in opts) { if (wt <= 0f) continue; x -= wt; if (x <= 0f) return v; }
        return opts.Last(o => o.w > 0f).v;
    }

    /// <summary>요소 조합으로 행성 하나 (level 1~8 — 높을수록 험하다). 가끔 이름 붙은 특별 행성.</summary>
    public static Planet Make(Rng r, int id, int level, HashSet<string>? seenSpecial = null)
    {
        float h = Math.Clamp((level - 1) / 7f, 0f, 1f);
        if (r.Chance(0.08f + 0.015f * level))
        {
            var left = Specials.Where(s => seenSpecial == null || !seenSpecial.Contains(s.Key)).ToList();
            if (left.Count > 0) return MakeSpecial(r, id, left[r.Range(0, left.Count)]);
        }
        var climate = Pick(r, (PlanetClimate.Frozen, 1f + h), (PlanetClimate.Cold, 2f), (PlanetClimate.Mild, 2.2f - 1.4f * h), (PlanetClimate.Hot, 1.5f + 0.5f * h), (PlanetClimate.Scorched, 0.4f + 1.6f * h), (PlanetClimate.Airless, 1.5f));
        var surface = climate switch
        {
            PlanetClimate.Frozen => Pick(r, (PlanetSurface.IceSheet, 3f), (PlanetSurface.Rock, 2f), (PlanetSurface.Crystal, 1f)),
            PlanetClimate.Cold => Pick(r, (PlanetSurface.Rock, 2f), (PlanetSurface.IceSheet, 2f), (PlanetSurface.Dunes, 1f), (PlanetSurface.Crystal, 1f), (PlanetSurface.Mud, 0.5f)),
            PlanetClimate.Mild => Pick(r, (PlanetSurface.Rock, 2f), (PlanetSurface.Shallows, 2f), (PlanetSurface.Mud, 1.5f), (PlanetSurface.Dunes, 1f), (PlanetSurface.Crystal, 0.5f)),
            PlanetClimate.Hot => Pick(r, (PlanetSurface.Dunes, 2.5f), (PlanetSurface.Rock, 1.5f), (PlanetSurface.Ash, 1f), (PlanetSurface.Shallows, 0.8f), (PlanetSurface.Lava, 0.7f)),
            PlanetClimate.Scorched => Pick(r, (PlanetSurface.Lava, 2f), (PlanetSurface.Ash, 2f), (PlanetSurface.Rock, 1f)),
            _ => Pick(r, (PlanetSurface.Rock, 3f), (PlanetSurface.Dunes, 1f), (PlanetSurface.IceSheet, 1f), (PlanetSurface.Crystal, 1f)),
        };
        float temp = climate switch
        {
            PlanetClimate.Frozen => r.Range(-170f, -90f), PlanetClimate.Cold => r.Range(-60f, -10f), PlanetClimate.Mild => r.Range(0f, 30f),
            PlanetClimate.Hot => r.Range(45f, 110f), PlanetClimate.Scorched => r.Range(250f, 460f), _ => r.Range(-120f, 60f),
        };
        float pressure = climate switch
        {
            PlanetClimate.Airless => 0f, PlanetClimate.Frozen => r.Range(1f, 60f), PlanetClimate.Cold => r.Range(20f, 120f), PlanetClimate.Mild => r.Range(60f, 150f),
            PlanetClimate.Hot => r.Range(80f, 300f), _ => r.Range(300f, 9000f),
        };
        float gravity = climate == PlanetClimate.Airless ? r.Range(0.15f, 0.7f) : r.Range(0.35f, 1.4f + 0.5f * h);
        var p = new Planet
        {
            Id = id, Seed = r.Range(1, int.MaxValue), Climate = climate, Surface = surface, Gravity = gravity, TempC = temp, Pressure = pressure,
            Swing = climate == PlanetClimate.Airless ? r.Range(120f, 260f) : pressure < 30f ? r.Range(40f, 90f) : r.Range(6f, 30f),
            StayDays = r.Range(1.5f, 3.5f),
            Name = NameOf(r, climate, surface), Code = CodeOf(r),
            Hex = SurfaceHex(surface, climate), Hex2 = SkyHex(climate, pressure),
        };
        // 날씨: 기후 · 지표에 맞는 것 가운데 둘~넷
        var pool = climate switch
        {
            PlanetClimate.Airless => new (PlanetWeather, float)[] { (PlanetWeather.Calm, 1f) },
            PlanetClimate.Frozen => new[] { (PlanetWeather.Calm, 2f), (PlanetWeather.Wind, 2f), (PlanetWeather.Blizzard, 2f) },
            PlanetClimate.Cold => new[] { (PlanetWeather.Calm, 2f), (PlanetWeather.Wind, 2f), (PlanetWeather.Blizzard, 1f), (PlanetWeather.DustStorm, surface == PlanetSurface.Dunes ? 2f : 0.6f) },
            PlanetClimate.Mild => new[] { (PlanetWeather.Calm, 2f), (PlanetWeather.Wind, 1f), (PlanetWeather.Rain, 2f), (PlanetWeather.Thunder, 1.5f), (PlanetWeather.Typhoon, surface == PlanetSurface.Shallows ? 1.4f : 0.3f) },
            PlanetClimate.Hot => new[] { (PlanetWeather.Calm, 1.5f), (PlanetWeather.Wind, 1f), (PlanetWeather.DustStorm, 2f), (PlanetWeather.HeatWave, 1.5f), (PlanetWeather.Thunder, 1f), (PlanetWeather.AcidRain, 0.5f) },
            _ => new[] { (PlanetWeather.Calm, 1f), (PlanetWeather.HeatWave, 2f), (PlanetWeather.AcidRain, 1.5f), (PlanetWeather.Thunder, 1f), (PlanetWeather.DustStorm, 1f) },
        };
        int nw = Math.Min(pool.Length, 2 + r.Range(0, 3));
        var opts = pool.ToList();
        p.Weathers.Add(PlanetWeather.Calm);
        while (p.Weathers.Distinct().Count() < nw && opts.Count > 0)
        {
            var wv = Pick(r, opts.ToArray());
            opts.RemoveAll(o => o.Item1.Equals(wv));
            if (!p.Weathers.Contains(wv)) p.Weathers.Add(wv);
        }
        // 험할수록 사나운 날씨가 잦다
        foreach (var wv in p.Weathers.Where(x => x != PlanetWeather.Calm).ToList()) if (r.Chance(0.3f + 0.5f * h)) p.Weathers.Add(wv);
        if (h < 0.3f) p.Weathers.Add(PlanetWeather.Calm);
        // 현상 · 그 밖의 것
        var fpool = new List<(PlanetFeature, float)> { (PlanetFeature.Quakes, 1f), (PlanetFeature.Rings, 0.6f), (PlanetFeature.Moons, 1f), (PlanetFeature.TidalLock, 0.5f) };
        if (climate is PlanetClimate.Frozen or PlanetClimate.Cold || surface is PlanetSurface.IceSheet or PlanetSurface.Shallows) fpool.Add((PlanetFeature.Geysers, 1f));
        if (climate is PlanetClimate.Hot or PlanetClimate.Scorched || surface is PlanetSurface.Lava or PlanetSurface.Ash) fpool.Add((PlanetFeature.Vents, 1.4f));
        if (pressure < 30f) fpool.Add((PlanetFeature.MeteorRain, 1.2f));
        if (surface == PlanetSurface.Shallows) fpool.Add((PlanetFeature.Tides, 1.4f));
        if (surface is PlanetSurface.Dunes or PlanetSurface.Mud or PlanetSurface.IceSheet) fpool.Add((PlanetFeature.Sinkholes, 0.9f));
        if (surface is PlanetSurface.Dunes || climate == PlanetClimate.Airless || p.Weathers.Contains(PlanetWeather.DustStorm)) fpool.Add((PlanetFeature.Static, 1f));
        fpool.Add((PlanetFeature.NoMagnet, 0.8f + 0.6f * h));
        fpool.Add((PlanetFeature.StrongMagnet, 0.8f));
        if (surface == PlanetSurface.Crystal) fpool.Add((PlanetFeature.CrystalField, 2f)); else fpool.Add((PlanetFeature.CrystalField, 0.3f));
        if (surface is PlanetSurface.Ash or PlanetSurface.Dunes || climate == PlanetClimate.Hot) fpool.Add((PlanetFeature.ToxicDust, 1f));
        int nf = 1 + r.Range(0, 3) + (h > 0.6f ? 1 : 0);
        for (int i = 0; i < nf && fpool.Count > 0; i++)
        {
            var f = Pick(r, fpool.ToArray());
            fpool.RemoveAll(o => o.Item1 == f);
            if (f == PlanetFeature.NoMagnet) fpool.RemoveAll(o => o.Item1 is PlanetFeature.StrongMagnet or PlanetFeature.Aurora);
            if (f == PlanetFeature.StrongMagnet) { fpool.RemoveAll(o => o.Item1 == PlanetFeature.NoMagnet); if (pressure > 5f) fpool.Add((PlanetFeature.Aurora, 2f)); }
            if (f == PlanetFeature.Moons && surface == PlanetSurface.Shallows && !fpool.Any(o => o.Item1 == PlanetFeature.Tides) && !p.Features.Contains(PlanetFeature.Tides)) fpool.Add((PlanetFeature.Tides, 1f));
            p.Features.Add(f);
        }
        // 자원
        void Add(ItemKind k, float v) { if (v > 0.02f) p.Rich[k] = Math.Clamp(p.Rich.GetValueOrDefault(k) + v, 0f, 1f); }
        Add(ItemKind.MetalOre, surface switch { PlanetSurface.Rock => 0.5f, PlanetSurface.Lava => 0.4f, PlanetSurface.Ash or PlanetSurface.Crystal => 0.3f, PlanetSurface.IceSheet => 0.1f, _ => 0.2f } + (p.Has(PlanetFeature.Rings) ? 0.2f : 0f));
        Add(ItemKind.Ice, surface switch { PlanetSurface.IceSheet => 0.9f, PlanetSurface.Shallows => 0.6f, PlanetSurface.Mud => 0.3f, _ => 0f } + climate switch { PlanetClimate.Frozen => 0.4f, PlanetClimate.Cold => 0.2f, _ => 0f } + (p.Has(PlanetFeature.Geysers) ? 0.3f : 0f));
        Add(ItemKind.Silicate, surface switch { PlanetSurface.Dunes => 0.7f, PlanetSurface.Crystal => 0.5f, PlanetSurface.Rock or PlanetSurface.Ash => 0.4f, _ => 0.15f });
        Add(ItemKind.Carbon, surface switch { PlanetSurface.Mud => 0.6f, PlanetSurface.Ash => 0.5f, PlanetSurface.Lava => 0.3f, _ => 0.05f } + (p.Has(PlanetFeature.Vents) ? 0.2f : 0f));
        Add(ItemKind.Rare, surface switch { PlanetSurface.Crystal => 0.4f, PlanetSurface.Lava => 0.2f, _ => 0.04f } + (p.Has(PlanetFeature.CrystalField) ? 0.4f : 0f) + (p.Has(PlanetFeature.Vents) ? 0.1f : 0f));
        Add(ItemKind.Fuel, (surface is PlanetSurface.IceSheet || climate == PlanetClimate.Frozen ? 0.4f : 0f) + (surface == PlanetSurface.Mud ? 0.3f : 0f) + (p.Has(PlanetFeature.Vents) ? 0.2f : 0f));
        foreach (var k in p.Rich.Keys.ToList()) p.Rich[k] = Math.Clamp(p.Rich[k] * r.Range(0.6f, 1.4f), 0f, 1f);
        Finish(p);
        return p;
    }

    private static void Finish(Planet p)
    {
        p.Danger = DangerOf(p);
        p.Reward = RewardOf(p);
        if (p.Line.Length == 0)
        {
            var bits = new List<string> { $"{ClimateName(p.Climate)} {SurfaceName(p.Surface)}", $"{p.TempC:0}°C", p.Pressure <= 0.5f ? "대기 없음" : p.Pressure > 1000f ? $"짓누르는 대기 {p.Pressure / 101f:0}기압" : $"대기 {p.Pressure:0}kPa", $"중력 {p.Gravity:0.0}G" };
            var rough = p.Weathers.Where(x => x != PlanetWeather.Calm).Distinct().Select(WeatherName).ToList();
            if (rough.Count > 0) bits.Add(string.Join("·", rough));
            p.Line = string.Join(" · ", bits);
        }
    }

    private static readonly string[] Codes = { "케플러", "글리제", "로스", "울프", "트라피스트", "라칸유", "HD", "TOI", "타우", "에리다니" };
    private static string CodeOf(Rng r) => $"{Codes[r.Range(0, Codes.Length)]}-{r.Range(12, 999)} {(char)('b' + r.Range(0, 5))}";

    private static string NameOf(Rng r, PlanetClimate c, PlanetSurface s)
    {
        string[] adj = c switch
        {
            PlanetClimate.Frozen => new[] { "얼어붙은", "하얀", "잠든", "시린" }, PlanetClimate.Cold => new[] { "서늘한", "잿빛", "고요한", "푸른" },
            PlanetClimate.Mild => new[] { "젖은", "푸른", "부드러운", "안개 낀" }, PlanetClimate.Hot => new[] { "붉은", "마른", "뜨거운", "성난" },
            PlanetClimate.Scorched => new[] { "타는", "끓는", "검붉은", "숨 막히는" }, _ => new[] { "벌거벗은", "말없는", "곰보", "창백한" },
        };
        string noun = s switch
        {
            PlanetSurface.Rock => "바위별", PlanetSurface.Dunes => "모래별", PlanetSurface.IceSheet => "얼음별", PlanetSurface.Shallows => "물별",
            PlanetSurface.Lava => "불별", PlanetSurface.Crystal => "수정별", PlanetSurface.Mud => "늪별", _ => "재별",
        };
        return $"{adj[r.Range(0, adj.Length)]} {noun}";
    }

    private static string SurfaceHex(PlanetSurface s, PlanetClimate c) => s switch
    {
        PlanetSurface.Rock => c == PlanetClimate.Hot ? "#9a6a4a" : "#7c7468", PlanetSurface.Dunes => "#c9a46a", PlanetSurface.IceSheet => "#cfe6f2", PlanetSurface.Shallows => "#3f7f95",
        PlanetSurface.Lava => "#3a2622", PlanetSurface.Crystal => "#8fb7d8", PlanetSurface.Mud => "#5a5238", _ => "#4c4844",
    };
    private static string SkyHex(PlanetClimate c, float pressure) => pressure <= 0.5f ? "#05060a" : c switch
    {
        PlanetClimate.Frozen => "#a9c4d8", PlanetClimate.Cold => "#7f97ae", PlanetClimate.Mild => "#7fb0d8", PlanetClimate.Hot => "#d8a070", PlanetClimate.Scorched => "#c8873a", _ => "#05060a",
    };

    // ── 이름 붙은 특별 행성 ──

    public sealed record SpecialSpec(string Key, string Name, string Line, PlanetClimate Climate, PlanetSurface Surface, float Gravity, float TempC, float Pressure,
        PlanetWeather[] Weathers, PlanetFeature[] Features, (ItemKind k, float v)[] Rich, string Hex, string Hex2);

    public static readonly SpecialSpec[] Specials =
    {
        new("thunder", "번개숲 우레", "수정 기둥 숲에 번개가 쉬지 않고 떨어진다 — 희귀 소재가 번개에 녹아 엉긴다", PlanetClimate.Mild, PlanetSurface.Crystal, 0.9f, 22f, 110f,
            new[] { PlanetWeather.Thunder, PlanetWeather.Thunder, PlanetWeather.Thunder, PlanetWeather.Rain, PlanetWeather.Calm }, new[] { PlanetFeature.Static, PlanetFeature.StrongMagnet, PlanetFeature.CrystalField },
            new[] { (ItemKind.Rare, 0.9f), (ItemKind.Silicate, 0.5f), (ItemKind.MetalOre, 0.3f) }, "#6f8fb0", "#3a4560"),
        new("cauldron", "끓는 바다 가마솥", "얕은 바다가 늘 끓는다 — 김이 하늘을 덮고 간헐천이 섬을 때린다", PlanetClimate.Hot, PlanetSurface.Shallows, 1.1f, 96f, 180f,
            new[] { PlanetWeather.HeatWave, PlanetWeather.HeatWave, PlanetWeather.AcidRain, PlanetWeather.Calm }, new[] { PlanetFeature.Geysers, PlanetFeature.Tides, PlanetFeature.Moons },
            new[] { (ItemKind.Ice, 0.9f), (ItemKind.Carbon, 0.4f), (ItemKind.Fuel, 0.3f) }, "#4f8f86", "#cfd2c8"),
        new("frost", "서리밭 고요", "바람 한 점 없는 얼음판 — 너무 추워 쇠가 유리처럼 깨진다", PlanetClimate.Frozen, PlanetSurface.IceSheet, 0.7f, -190f, 12f,
            new[] { PlanetWeather.Calm, PlanetWeather.Calm, PlanetWeather.Calm, PlanetWeather.Blizzard }, new[] { PlanetFeature.Geysers, PlanetFeature.TidalLock },
            new[] { (ItemKind.Ice, 1f), (ItemKind.Fuel, 0.7f) }, "#dceef8", "#9fb9d0"),
        new("rust", "녹슨별 철비", "쇳가루 섞인 산성비가 내린다 — 땅이 온통 쇳덩이라 캐면 캘수록 나온다", PlanetClimate.Hot, PlanetSurface.Ash, 1.3f, 70f, 240f,
            new[] { PlanetWeather.AcidRain, PlanetWeather.AcidRain, PlanetWeather.Wind, PlanetWeather.Calm }, new[] { PlanetFeature.Vents, PlanetFeature.ToxicDust },
            new[] { (ItemKind.MetalOre, 1f), (ItemKind.Carbon, 0.4f) }, "#8a4a2c", "#b0703a"),
        new("singing", "울림사막", "모래 언덕이 바람에 운다 — 밤마다 땅이 꺼지고 모래 폭풍이 정전기를 끌고 온다", PlanetClimate.Hot, PlanetSurface.Dunes, 0.8f, 52f, 70f,
            new[] { PlanetWeather.DustStorm, PlanetWeather.DustStorm, PlanetWeather.Wind, PlanetWeather.Calm }, new[] { PlanetFeature.Sinkholes, PlanetFeature.Static, PlanetFeature.ToxicDust },
            new[] { (ItemKind.Silicate, 1f), (ItemKind.MetalOre, 0.3f) }, "#d8b070", "#e0b888"),
        new("halfworld", "반쪽별", "한쪽은 영원한 낮 · 한쪽은 영원한 밤 — 그 경계 띠에만 내릴 수 있다", PlanetClimate.Airless, PlanetSurface.Rock, 0.6f, -20f, 0f,
            new[] { PlanetWeather.Calm }, new[] { PlanetFeature.TidalLock, PlanetFeature.NoMagnet, PlanetFeature.MeteorRain, PlanetFeature.Static },
            new[] { (ItemKind.MetalOre, 0.6f), (ItemKind.Ice, 0.5f), (ItemKind.Rare, 0.3f) }, "#6a6a70", "#05060a"),
        new("crystal", "수정 골짜기 빛고을", "골짜기마다 사람 키만 한 수정이 솟았다 — 땅이 흔들리면 수정이 무너진다", PlanetClimate.Cold, PlanetSurface.Crystal, 1.0f, -35f, 60f,
            new[] { PlanetWeather.Calm, PlanetWeather.Wind, PlanetWeather.Calm }, new[] { PlanetFeature.CrystalField, PlanetFeature.Quakes, PlanetFeature.Aurora, PlanetFeature.StrongMagnet },
            new[] { (ItemKind.Rare, 1f), (ItemKind.Silicate, 0.7f) }, "#9cc8e8", "#2a3a58"),
        new("ringshade", "고리아래 그늘별", "하늘을 가로지르는 고리 그림자 아래 — 고리 조각이 유성 비로 쏟아진다", PlanetClimate.Cold, PlanetSurface.Rock, 0.9f, -50f, 8f,
            new[] { PlanetWeather.Calm, PlanetWeather.Calm, PlanetWeather.Wind }, new[] { PlanetFeature.Rings, PlanetFeature.MeteorRain, PlanetFeature.Moons },
            new[] { (ItemKind.MetalOre, 0.8f), (ItemKind.Ice, 0.5f), (ItemKind.Rare, 0.2f) }, "#7a7670", "#1a1e2a"),
        new("breath", "숨골 화산", "땅이 숨을 쉬듯 분기공이 열렸다 닫힌다 — 화산재와 지진, 그리고 탄소", PlanetClimate.Hot, PlanetSurface.Lava, 1.2f, 88f, 160f,
            new[] { PlanetWeather.HeatWave, PlanetWeather.Thunder, PlanetWeather.Calm }, new[] { PlanetFeature.Vents, PlanetFeature.Quakes, PlanetFeature.ToxicDust },
            new[] { (ItemKind.Carbon, 0.9f), (ItemKind.Rare, 0.4f), (ItemKind.Fuel, 0.4f), (ItemKind.MetalOre, 0.4f) }, "#3a2622", "#8a5030"),
        new("eye", "소용돌이 태풍의 눈", "바다 한가운데 섬 — 태풍이 쉬지 않고 돌고, 위성 셋이 밀물을 끌어 올린다", PlanetClimate.Mild, PlanetSurface.Shallows, 1.0f, 24f, 130f,
            new[] { PlanetWeather.Typhoon, PlanetWeather.Typhoon, PlanetWeather.Rain, PlanetWeather.Thunder, PlanetWeather.Calm }, new[] { PlanetFeature.Tides, PlanetFeature.Moons },
            new[] { (ItemKind.Ice, 0.8f), (ItemKind.Carbon, 0.3f), (ItemKind.MetalOre, 0.3f) }, "#3a7a95", "#5a6a7a"),
    };

    private static Planet MakeSpecial(Rng r, int id, SpecialSpec s)
    {
        var p = new Planet
        {
            Id = id, Key = s.Key, Name = s.Name, Code = CodeOf(r), Seed = r.Range(1, int.MaxValue), Climate = s.Climate, Surface = s.Surface, Gravity = s.Gravity, TempC = s.TempC,
            Pressure = s.Pressure, Swing = s.Pressure < 30f ? 150f : 15f, StayDays = r.Range(2f, 3.5f), Hex = s.Hex, Hex2 = s.Hex2, Line = s.Line,
        };
        p.Weathers.AddRange(s.Weathers);
        p.Features.AddRange(s.Features);
        foreach (var (k, v) in s.Rich) p.Rich[k] = v;
        Finish(p);
        return p;
    }

    /// <summary>원거리 관측: 확실성만큼 또렷하다 — 아는 위험 · 보상에 오차, 못 본 현상은 숨는다.</summary>
    public static void Observe(Planet p, Rng r, float sensor)
    {
        p.Certainty = Math.Clamp(r.Range(0.35f, 0.75f) * sensor + 0.1f, 0.2f, 0.95f);
        float err = 1f - p.Certainty;
        p.KnownDanger = Math.Clamp(p.Danger + r.Range(-0.35f, 0.25f) * err, 0.02f, 1f); // 멀리서는 대개 덜 험해 보인다
        p.KnownReward = Math.Clamp(p.Reward + r.Range(-0.25f, 0.35f) * err, 0.02f, 1f);
        p.Hidden.Clear();
        foreach (var f in p.Features)
            if (f is not (PlanetFeature.Rings or PlanetFeature.Moons) && r.Chance(err * 0.8f)) p.Hidden.Add(f);
    }
}
