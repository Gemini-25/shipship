using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ShipSim.Core;

/// <summary>조정할 수 있는 수치 하나.</summary>
public sealed class TuningEntry
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public float Default { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public Func<float> Get { get; init; } = () => 0f;
    public Action<float> Set { get; init; } = _ => { };
}

/// <summary>
/// v10.4 밸런스 수치 파일. 게임 밖(`tuning.cfg`, 한 줄에 `열쇠 = 값`)이나 설정 창에서 고친다.
/// 새 항해를 시작할 때의 값은 저장 파일 머리에(`tune 열쇠 값`), 항해 중에 바꾼 값은 관찰자 기록으로 남아 되감기·불러오기가 같은 역사를 흘린다.
/// </summary>
public static class Tuning
{
    public static float ResearchPerBenchDay = 3f;
    public static float ResearchPerExpertDay = 0.6f;

    /// <summary>v11.3: 하루 연구가 이만큼을 넘으면 넘는 몫은 <see cref="ResearchBeyond"/>만 — 사람이 는다고 그만큼 빨라지지 않는다.</summary>
    public static float ResearchKnee = 9f;
    public static float ResearchBeyond = 0.45f;

    /// <summary>v11.0: 닳아서 날 고장 중 전조부터 내는 몫.</summary>
    public static float OmenShare = 0.65f;

    public static readonly TuningEntry[] Entries =
    {
        E("collect.rate", "채집 속도 (효율 1·밀도 1에서 시간당 원료)", 0.15f, 0.01f, 2f, () => CollectionSystem.RatePerHour, v => CollectionSystem.RatePerHour = v),
        E("water.recycler", "정수기 한 대의 물 (L/시간)", 3.4f, 0.5f, 20f, () => WaterSystem.RecyclerLitersPerHour, v => WaterSystem.RecyclerLitersPerHour = v),
        E("water.crew", "한 사람이 마시는 물 (L/시간)", 0.12f, 0f, 1f, () => WaterSystem.CrewLitersPerHour, v => WaterSystem.CrewLitersPerHour = v),
        E("water.bed", "재배대 하나가 먹는 물 (L/시간)", 0.45f, 0f, 3f, () => WaterSystem.BedLitersPerHour, v => WaterSystem.BedLitersPerHour = v),
        E("o2.generator", "산소 발생기 한 대의 몫", 110f, 10f, 500f, () => Atmosphere.GeneratorCapacity, v => Atmosphere.GeneratorCapacity = v),
        E("reactor.kw", "원자로 3×3 한 기의 최대 출력 (kW)", 48f, 10f, 300f, () => PowerGrid.ReactorMaxKw, v => PowerGrid.ReactorMaxKw = v),
        E("cooling.branch", "냉각 펌프(분기) 하나가 식히는 열 (kW)", 30f, 5f, 200f, () => PipeNetwork.PerBranchKw, v => PipeNetwork.PerBranchKw = v),
        E("aux.kw", "보조 발전기 출력 (kW)", 9f, 1f, 60f, () => PowerGrid.AuxKw, v => PowerGrid.AuxKw = v),
        E("food.grow_hours", "작물이 다 자라는 시간", 60f, 5f, 400f, () => FoodChain.GrowHours, v => FoodChain.GrowHours = v),
        E("food.yield", "재배대 한 번 수확량 (채소)", 14f, 1f, 100f, () => FoodChain.HarvestYield, v => FoodChain.HarvestYield = (int)MathF.Round(v)),
        E("research.bench", "작업대 하나가 하루에 쌓는 연구", 3f, 0f, 100f, () => ResearchPerBenchDay, v => ResearchPerBenchDay = v),
        E("research.expert", "솜씨 좋은 사람 하나가 하루에 쌓는 연구", 0.6f, 0f, 20f, () => ResearchPerExpertDay, v => ResearchPerExpertDay = v),
        E("research.knee", "하루 연구가 이만큼을 넘으면 체감 (큰 배)", 9f, 1f, 100f, () => ResearchKnee, v => ResearchKnee = v),
        E("research.beyond", "체감 뒤 몫 (0~1)", 0.45f, 0f, 1f, () => ResearchBeyond, v => ResearchBeyond = v),
        E("meteor.approach", "운석이 날아오는 시간 (분)", 6f, 0.5f, 30f, () => SensorSystem.ApproachMinutes, v => SensorSystem.ApproachMinutes = v),
        E("evolution.peace", "개조를 궁리하기 전 평화 (시간)", 12f, 0f, 200f, () => Evolution.PeaceHours, v => Evolution.PeaceHours = v),
        E("evolution.gap", "개조와 개조 사이 (시간)", 16f, 0f, 200f, () => Evolution.GapHours, v => Evolution.GapHours = v),
        E("omen.share", "닳아서 날 고장 중 전조부터 내는 몫 (0~1)", 0.65f, 0f, 1f, () => OmenShare, v => OmenShare = v),
        E("incident.days", "무작위 사고 평균 간격 (일, 0이면 끔)", 0f, 0f, 30f, () => HazardSystem.RandomDays, v => HazardSystem.RandomDays = v),
    };

    private static TuningEntry E(string key, string label, float def, float min, float max, Func<float> get, Action<float> set) =>
        new() { Key = key, Label = label, Default = def, Min = min, Max = max, Get = get, Set = set };

    public static TuningEntry? Find(string key) => Entries.FirstOrDefault(e => e.Key == key);

    public static void ResetDefaults()
    {
        foreach (var e in Entries) e.Set(e.Default);
    }

    public static bool Apply(string key, float value)
    {
        if (Find(key) is not TuningEntry e || float.IsNaN(value)) return false;
        e.Set(Math.Clamp(value, e.Min, e.Max));
        return true;
    }

    /// <summary>기본값과 다른 것만 (저장 파일 머리에 쓴다).</summary>
    public static IEnumerable<(string key, float value)> NonDefault() =>
        Entries.Where(e => MathF.Abs(e.Get() - e.Default) > 1e-6f).Select(e => (e.Key, e.Get()));

    /// <summary>`열쇠 = 값` 줄들을 읽는다 (# 뒤는 설명). 모르는 열쇠는 건너뛴다.</summary>
    public static int Load(string text)
    {
        int n = 0;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (float.TryParse(line[(eq + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && Apply(line[..eq].Trim(), v)) n++;
        }
        return n;
    }

    public static string Write()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ShipSim 밸런스 수치 (v10.4). `열쇠 = 값`. 새 항해부터 쓰인다 (설정 창에서 바꾸면 지금 항해에도 기록되어 적용된다).");
        foreach (var e in Entries)
            sb.AppendLine($"{e.Key} = {e.Get().ToString("0.###", CultureInfo.InvariantCulture)}    # {e.Label} (기본 {e.Default.ToString("0.###", CultureInfo.InvariantCulture)})");
        return sb.ToString();
    }
}
