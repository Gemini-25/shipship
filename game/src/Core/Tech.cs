using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>설비 한 단계 (v10.5 테크 티어): 이름, 출력·전력·마모·고장 배율, 필요한 연구, 올리는 재료.</summary>
public sealed record TechTier(int Tier, string Name, float Output, float Power, float Wear, float Faults, int Research,
    (ItemKind kind, int count)[] Cost, string Note);

/// <summary>
/// v10.5 설비 테크 트리. 모든 주요 설비에 3~4단계가 있다 (원자로: 핵분열로 → 개량 핵분열로 → 소형 핵융합로 → 대형 핵융합로).
/// - 연구는 쌓인다: 작업대(등급만큼)와 솜씨 좋은 승무원이 날마다 연구를 쌓고, 연구가 문턱을 넘으면 그 단계가 "풀린다".
/// - 풀린 단계로 올리는 건 개조다: 재료(높은 단계는 희귀 소재)를 들여 회의로 정한다 (겪은 일이 무엇을 먼저 올릴지 정한다).
/// - 공짜 강화가 아니다: 높은 단계일수록 출력은 크지만 전기를 더 먹거나, 더 빨리 닳거나, 더 자주·크게 고장 난다.
/// 수치는 모두 첫 추정이다 (tuning.cfg로 연구 속도를 바꿀 수 있다).
/// </summary>
public static class Tech
{
    private static (ItemKind, int)[] C(params (ItemKind, int)[] c) => c;

    private static readonly Dictionary<FurnitureType, TechTier[]> Table = new()
    {
        [FurnitureType.ReactorCore] = new[]
        {
            new TechTier(1, "핵분열로", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "개량 핵분열로", 1.35f, 1f, 0.95f, 1.05f, 30, C((ItemKind.ReactorControl, 1), (ItemKind.Electronics, 3), (ItemKind.Plate, 4)), "출력 +35%"),
            new TechTier(3, "소형 핵융합로", 2f, 1f, 1.15f, 1.4f, 90, C((ItemKind.ReactorControl, 2), (ItemKind.Rare, 3), (ItemKind.Electronics, 4), (ItemKind.Plate, 6)), "출력 두 배 · 플라스마가 흔들리면 더 크게 선다"),
            new TechTier(4, "대형 핵융합로", 2.8f, 1f, 1.3f, 1.7f, 180, C((ItemKind.ReactorControl, 2), (ItemKind.Rare, 6), (ItemKind.Electronics, 6), (ItemKind.Structure, 4)), "출력 2.8배 · 가장 까다롭다"),
        },
        [FurnitureType.CoolantPump] = new[]
        {
            new TechTier(1, "기계식 펌프", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "고압 펌프", 1.3f, 1.2f, 1f, 1f, 20, C((ItemKind.Pump, 1), (ItemKind.Plate, 2)), "냉각 +30% · 전기 +20%"),
            new TechTier(3, "초전도 펌프", 1.8f, 0.9f, 0.85f, 1.2f, 70, C((ItemKind.Rare, 1), (ItemKind.Pump, 1), (ItemKind.Electronics, 2)), "냉각 +80% · 전자 고장이 잦다"),
        },
        [FurnitureType.OxygenGenerator] = new[]
        {
            new TechTier(1, "전해조", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "사바티에 반응기", 1.35f, 1.1f, 1f, 1f, 30, C((ItemKind.PowerController, 1), (ItemKind.Filter, 2), (ItemKind.Plate, 2)), "산소 +35%"),
            new TechTier(3, "조류 광합성조", 1.8f, 0.7f, 1f, 1.3f, 90, C((ItemKind.Rare, 1), (ItemKind.Filter, 4), (ItemKind.Plate, 3)), "산소 +80% · 전기 −30% · 조류가 죽으면 멈춘다"),
        },
        [FurnitureType.WaterRecycler] = new[]
        {
            new TechTier(1, "막 여과기", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "증기 압축 증류기", 1.4f, 1.2f, 1f, 1f, 25, C((ItemKind.Pump, 1), (ItemKind.Filter, 2), (ItemKind.Plate, 2)), "물 +40%"),
            new TechTier(3, "폐쇄 순환 정수기", 2f, 1.3f, 1.1f, 1.1f, 80, C((ItemKind.Rare, 1), (ItemKind.Pump, 1), (ItemKind.Electronics, 2)), "물 두 배"),
        },
        [FurnitureType.GrowBed] = new[]
        {
            new TechTier(1, "수경 재배대", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "LED 수경 재배대", 1.35f, 1.4f, 1f, 1f, 20, C((ItemKind.Cable, 2), (ItemKind.Electronics, 1)), "생장 +35% · 전기 +40%"),
            new TechTier(3, "에어로포닉스", 1.8f, 1.6f, 1f, 1.25f, 70, C((ItemKind.Pump, 1), (ItemKind.Electronics, 2), (ItemKind.Sealant, 1)), "생장 +80% · 분무기가 막히면 마른다"),
        },
        [FurnitureType.Battery] = new[]
        {
            new TechTier(1, "리튬 배터리", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "고체 전해질 배터리", 1.5f, 1f, 0.9f, 0.9f, 30, C((ItemKind.PowerController, 1), (ItemKind.Electronics, 2)), "용량 +50%"),
            new TechTier(3, "초전도 저장고", 2.5f, 1f, 1f, 1.3f, 100, C((ItemKind.Rare, 2), (ItemKind.PowerController, 1), (ItemKind.Electronics, 3)), "용량 2.5배 · 냉각이 끊기면 샌다"),
        },
        [FurnitureType.Workbench] = new[]
        {
            new TechTier(1, "수공구 작업대", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "CNC 공작기", 1.3f, 1.5f, 1f, 1f, 25, C((ItemKind.Motor, 1), (ItemKind.Electronics, 2), (ItemKind.Plate, 2)), "제작·연구 +30%"),
            new TechTier(3, "적층 제조기", 1.7f, 2f, 1.1f, 1.1f, 80, C((ItemKind.Rare, 1), (ItemKind.Electronics, 3), (ItemKind.Motor, 1)), "제작·연구 +70%"),
        },
        [FurnitureType.Refinery] = new[]
        {
            new TechTier(1, "전기로", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "플라스마 정련로", 1.6f, 1.4f, 1.1f, 1.1f, 50, C((ItemKind.PowerController, 1), (ItemKind.Plate, 3), (ItemKind.Rare, 1)), "정련 +60%"),
        },
        [FurnitureType.Collector] = new[]
        {
            new TechTier(1, "채집 팔", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "자기 수집기", 1.6f, 1.3f, 1f, 1f, 40, C((ItemKind.Motor, 1), (ItemKind.Sensor, 1), (ItemKind.Electronics, 1)), "채집 +60%"),
            new TechTier(3, "드론 수집망", 2.4f, 1.6f, 1.1f, 1.2f, 110, C((ItemKind.Rare, 2), (ItemKind.Sensor, 2), (ItemKind.Electronics, 2)), "채집 2.4배"),
        },
        [FurnitureType.AuxGenerator] = new[]
        {
            new TechTier(1, "디젤 발전기", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "연료전지", 1.5f, 1f, 0.9f, 1f, 40, C((ItemKind.PowerController, 1), (ItemKind.Plate, 2)), "출력 +50%"),
        },
        [FurnitureType.MainComputer] = new[]
        {
            new TechTier(1, "주 컴퓨터", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "분산 제어 컴퓨터", 1f, 1.2f, 0.9f, 0.7f, 60, C((ItemKind.Electronics, 4), (ItemKind.Sensor, 1)), "고장 −30%"),
        },
        [FurnitureType.SensorArray] = new[]
        {
            new TechTier(1, "레이더", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "위상 배열 레이더", 1.4f, 1.3f, 1f, 1f, 40, C((ItemKind.Sensor, 2), (ItemKind.Electronics, 2)), "경보 +40% 먼저"),
        },
        [FurnitureType.EngineCore] = new[]
        {
            new TechTier(1, "화학 추진기", 1f, 1f, 1f, 1f, 0, C(), "기본"),
            new TechTier(2, "이온 추진기", 1.5f, 1.3f, 0.9f, 1f, 60, C((ItemKind.Electronics, 3), (ItemKind.Motor, 1), (ItemKind.Plate, 2)), "추력 +50%"),
        },
    };

    private static readonly TechTier[] Basic = { new(1, "", 1f, 1f, 1f, 1f, 0, Array.Empty<(ItemKind, int)>(), "") };

    public static IReadOnlyList<TechTier> Tiers(FurnitureType t) => Table.TryGetValue(t, out var l) ? l : Basic;
    public static IEnumerable<FurnitureType> Types => Table.Keys;
    public static bool HasTree(FurnitureType t) => Table.ContainsKey(t);

    public static TechTier Of(Machine m)
    {
        var l = Tiers(m.Body.Type);
        return l[Math.Clamp(m.Tier - 1, 0, l.Count - 1)];
    }

    public static string TierName(Machine m) => Of(m).Name.Length > 0 ? Of(m).Name : m.Name;

    /// <summary>연구로 풀린 가장 높은 단계.</summary>
    public static int Unlocked(World w, FurnitureType t) =>
        Tiers(t).Where(x => x.Research <= w.Research && TechWeb.TierOk(w, t, x.Tier)).Select(x => x.Tier).DefaultIfEmpty(1).Max(); // v16.14 핵융합로 설계 · 갈림길

    /// <summary>다음 단계 (없으면 null).</summary>
    public static TechTier? Next(Machine m)
    {
        var l = Tiers(m.Body.Type);
        return m.Tier < l.Count ? l[m.Tier] : null;
    }

    public static string Roman(int tier) => tier switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => tier.ToString() };

    /// <summary>연구가 문턱을 넘어 새 단계가 풀리면 알린다 (연대기에 남는다).</summary>
    public static void NoteUnlocks(World w, float before)
    {
        foreach (var (type, tiers) in Table)
            foreach (var t in tiers)
                if (t.Research > before && t.Research <= w.Research)
                {
                    w.Log.Add(w.Tick, LogKind.Ship, $"연구 {w.Research:0}점 — {t.Name} 설계가 풀렸다 ({FurnitureTypes.Name(type)} {Roman(t.Tier)}단계 · {t.Note})");
                    w.History.Add(w, HistoryKind.Milestone, $"연구가 {t.Research}점에 닿아 {t.Name} 설계가 풀렸다");
                }
    }

    /// <summary>하루치 연구: 작업대(등급·효율만큼) + 솜씨 좋은(0.6 넘는) 사람. 설정 파일로 바꿀 수 있다.
    /// v11.3: 하루 9점을 넘는 몫은 45%만 (30인 배가 열흘이면 모든 설계를 풀던 것 — 같은 문제를 여럿이 푸는 셈이다). 4·6인 배는 그대로.</summary>
    public static float ResearchPerHour(World w)
    {
        float benches = w.Ship.FurnitureOf(FurnitureType.Workbench).Where(f => !f.Room.Abandoned).Sum(f => f.Machine!.Efficiency * Of(f.Machine).Output);
        int experts = w.Crew.Count(c => !c.Dead && !c.Down && c.SkillLevels.Max() >= 0.6f);
        float perDay = benches * Tuning.ResearchPerBenchDay + experts * Tuning.ResearchPerExpertDay + Modules.Bonus(w, FurnitureType.Fabricator);
        if (perDay > Tuning.ResearchKnee) perDay = Tuning.ResearchKnee + (perDay - Tuning.ResearchKnee) * Tuning.ResearchBeyond;
        return perDay / 24f;
    }
}
