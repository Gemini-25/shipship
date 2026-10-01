using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v14.1 질병·몸 상태 1 → 30.
//
// 열병(v12.4 전염병, Disease.cs)에 29가지를 더한다 — 옮는 병 · 환경이 만드는 병 · 긴 항해의 몸 · 마음 · 다친 뒤와 나이.
// 저마다 원인(조건)이 있어 그 조건일 때만 잘 걸리고, 증상(일 손 · 걸음 · 잠 · 스트레스 · 기력 · 체력 · 공황)이 있고,
// 낫는 길이 있다: 쉬면 · 치료 침대에 누우면 · 의무관이 약을 쓰면 · 제대로 먹으면 · 몸을 움직이면 · 곁에 누가 있으면.
// 진단 전에는 "어딘가 아프다"일 뿐 — 의무 자격이 있는 사람이 곁에 오거나 의무실에 가야 무슨 병인지 안다(그래야 약을 쓴다).

public enum AilmentGroup { Contagious, Environment, Voyage, Mind, Body }

public enum Cure { Rest, Bed, Medic, Food, Exercise, Company, None }

public sealed record AilmentSpec(string Id, string Name, AilmentGroup Group, string Cause, string Symptom, float Days, float Peak,
    bool Chronic = false, float Spread = 0f, bool Immunity = false,
    float Rest = 0f, float Stress = 0f, float Work = 0f, float Walk = 0f, float Health = 0f, float Sleep = 0f, float Panic = 0f,
    Cure Cure = Cure.Rest);

/// <summary>한 사람이 앓는 것 하나.</summary>
public sealed class Ailment
{
    public string Id { get; init; } = "";
    public long Since { get; init; }
    public float Peak { get; init; }
    /// <summary>나은 만큼 (날) — 쉬고 · 눕고 · 약을 쓰고 · 먹고 · 움직이고 · 곁에 누가 있으면 쌓인다.</summary>
    public float Healed { get; set; }
    public bool Diagnosed { get; set; }
    public long TreatedAt { get; set; } = -1;
    public int Treatments { get; set; }
    /// <summary>옮긴 사람 (-1: 처음 걸림).</summary>
    public int From { get; init; } = -1;
}

public sealed class AilmentStats
{
    public Dictionary<string, int> Cases { get; } = new();
    public int Recovered, Treated, Diagnosed, Spread, Deaths;
    public int Total => Cases.Values.Sum();
    public override string ToString() => $"앓은 사람 {Total}번({Cases.Count}가지) · 옮음 {Spread} · 진단 {Diagnosed} · 치료 {Treated} · 나음 {Recovered} · 사망 {Deaths}";
}

public sealed class AilmentSystem
{
    // ── 목록 (열병은 Disease.cs — 여기엔 이름만) ──
    public static readonly AilmentSpec Fever = new("fever", "열병", AilmentGroup.Contagious, "옮는 열병 (같은 방)", "열 · 기력이 빠진다 · 쓰러질 수도", 4f, 0.7f,
        Spread: 0.5f, Immunity: true, Rest: 0.06f, Stress: 0.02f, Health: 0.015f, Cure: Cure.Bed);

    public static readonly AilmentSpec[] All =
    {
        // 옮는 병
        new("cold", "감기", AilmentGroup.Contagious, "옮는다 · 가끔 저절로", "콧물 · 기침 · 조금 지친다", 3f, 0.35f, Spread: 0.18f, Immunity: true, Rest: 0.02f, Stress: 0.01f, Work: 0.1f),
        new("gastro", "장염", AilmentGroup.Contagious, "옮는다 · 식당 위생", "배탈 · 기력이 빠진다", 2f, 0.55f, Spread: 0.2f, Immunity: true, Rest: 0.05f, Stress: 0.02f, Work: 0.25f, Cure: Cure.Bed),
        new("pinkeye", "결막염", AilmentGroup.Contagious, "옮는다 (손·수건)", "눈이 붓고 가렵다 — 손이 둔하다", 4f, 0.3f, Spread: 0.15f, Immunity: true, Work: 0.15f, Stress: 0.01f, Cure: Cure.Medic),
        new("fungus", "피부 곰팡이", AilmentGroup.Contagious, "축축한 방 · 옮는다", "가렵다 · 신경이 곤두선다", 6f, 0.25f, Spread: 0.06f, Stress: 0.015f, Work: 0.05f, Cure: Cure.Medic),
        new("pneumonia", "폐렴", AilmentGroup.Contagious, "감기·열병 끝에 몸이 약하면 · 폐를 다치면", "숨이 차고 열 — 오래 두면 위험하다", 6f, 0.75f, Rest: 0.07f, Stress: 0.02f, Work: 0.35f, Walk: 0.25f, Health: 0.04f, Cure: Cure.Medic),
        // 환경이 만드는 병
        new("moldlung", "곰팡이 기관지염", AilmentGroup.Environment, "물이 고인 방에 오래", "마른기침 · 숨이 가쁘다", 5f, 0.4f, Rest: 0.03f, Work: 0.15f, Walk: 0.1f, Cure: Cure.Medic),
        new("foodpoison", "식중독", AilmentGroup.Environment, "조리 위생 자격 없는 부엌 · 배급", "구토 · 배탈", 1.5f, 0.6f, Rest: 0.06f, Stress: 0.03f, Work: 0.35f, Health: 0.01f, Cure: Cure.Bed),
        new("spacesick", "우주 멀미", AilmentGroup.Environment, "출항 첫 이틀", "어지럽고 메스껍다", 1.5f, 0.4f, Immunity: true, Stress: 0.02f, Work: 0.2f, Walk: 0.15f),
        new("bends", "감압병", AilmentGroup.Environment, "공기가 갑자기 빠진 방 · 선외 작업 뒤", "관절이 쑤시고 저리다", 2f, 0.6f, Rest: 0.04f, Stress: 0.02f, Work: 0.2f, Walk: 0.3f, Health: 0.01f, Cure: Cure.Bed),
        new("radiation", "방사선 병", AilmentGroup.Environment, "누적 피폭 1Sv 넘음", "구역질 · 기력이 바닥난다 — 오래 두면 위험하다", 10f, 0.7f, Chronic: true, Rest: 0.06f, Stress: 0.02f, Work: 0.3f, Health: 0.03f, Cure: Cure.Bed),
        new("hypothermia", "저체온증", AilmentGroup.Environment, "8도 아래 방", "떨림 · 손이 굳는다 — 위험하다", 1f, 0.7f, Rest: 0.08f, Work: 0.4f, Walk: 0.3f, Health: 0.08f, Cure: Cure.Bed),
        new("heatstroke", "열사병", AilmentGroup.Environment, "40도 넘는 방", "어지럽고 쓰러질 듯 — 위험하다", 1f, 0.7f, Rest: 0.1f, Work: 0.4f, Walk: 0.3f, Health: 0.08f, Cure: Cure.Bed),
        new("dehydration", "탈수", AilmentGroup.Environment, "물이 바닥났다 · 엄격한 물 배급", "두통 · 지친다", 1.5f, 0.5f, Rest: 0.05f, Stress: 0.02f, Work: 0.2f, Health: 0.01f, Cure: Cure.Food),
        new("cobrain", "일산화탄소 후유증", AilmentGroup.Environment, "일산화탄소를 들이마심", "두통 · 깜빡깜빡한다", 3f, 0.45f, Stress: 0.01f, Work: 0.25f, Health: 0.02f, Cure: Cure.Bed),
        new("chemburn", "화학 화상", AilmentGroup.Environment, "유독 가스에 맨몸", "살갗과 눈이 쓰라리다", 4f, 0.5f, Stress: 0.03f, Work: 0.25f, Health: 0.01f, Cure: Cure.Medic),
        new("co2ache", "이산화탄소 두통", AilmentGroup.Environment, "답답한 공기 (CO₂ 1.5% 넘음)", "머리가 지끈거린다", 0.5f, 0.35f, Stress: 0.03f, Work: 0.15f),
        // 긴 항해의 몸
        new("boneloss", "골밀도 저하", AilmentGroup.Voyage, "스무 날 넘게 운동 부족", "뼈가 약해진다 — 걸음이 굼뜨다", 6f, 0.4f, Chronic: true, Work: 0.05f, Walk: 0.15f, Cure: Cure.Exercise),
        new("atrophy", "근육 위축", AilmentGroup.Voyage, "몸이 굳었다 (체력 단련 30% 아래)", "힘이 없다", 5f, 0.4f, Chronic: true, Work: 0.15f, Walk: 0.15f, Cure: Cure.Exercise),
        new("scurvy", "괴혈병", AilmentGroup.Voyage, "신선한 채소 없이 엿새", "잇몸이 붓고 상처가 더디 낫는다", 4f, 0.45f, Chronic: true, Rest: 0.03f, Stress: 0.01f, Work: 0.15f, Health: 0.005f, Cure: Cure.Food),
        new("malnutrition", "영양실조", AilmentGroup.Voyage, "오래 굶주림", "기운이 없다", 5f, 0.5f, Chronic: true, Rest: 0.04f, Work: 0.25f, Walk: 0.1f, Health: 0.01f, Cure: Cure.Food),
        new("vision", "시력 저하", AilmentGroup.Voyage, "서른 날 넘는 무중력·인공 중력 생활", "가까운 글씨가 흐리다 — 세밀한 일이 굼뜨다", 30f, 0.3f, Chronic: true, Work: 0.08f, Cure: Cure.None),
        new("kidneystone", "신장 결석", AilmentGroup.Voyage, "탈수 · 뼈가 약해짐", "옆구리가 끊어질 듯 아프다", 2f, 0.7f, Rest: 0.04f, Stress: 0.06f, Work: 0.4f, Walk: 0.3f, Cure: Cure.Medic),
        new("backpain", "요통", AilmentGroup.Body, "무거운 짐 · 나이", "허리가 쑤신다", 4f, 0.35f, Stress: 0.015f, Work: 0.15f, Walk: 0.1f),
        new("toothache", "치통", AilmentGroup.Body, "가끔 저절로", "이가 욱신거린다 — 약을 써야 낫는다", 5f, 0.4f, Chronic: true, Stress: 0.04f, Work: 0.1f, Sleep: 0.15f, Cure: Cure.Medic),
        new("woundinf", "상처 감염", AilmentGroup.Body, "크게 다친 뒤 열두 시간 넘게 치료 못 받음", "상처가 곪고 열 — 오래 두면 위험하다", 5f, 0.7f, Chronic: true, Rest: 0.05f, Stress: 0.02f, Work: 0.2f, Health: 0.04f, Cure: Cure.Medic),
        new("arthritis", "관절염", AilmentGroup.Body, "쉰다섯 넘은 나이", "관절이 뻣뻣하다", 20f, 0.3f, Chronic: true, Work: 0.1f, Walk: 0.15f, Cure: Cure.None),
        // 마음
        new("insomnia", "불면증", AilmentGroup.Mind, "하루 넘게 높은 스트레스", "잠들지 못한다 — 자도 덜 쉰다", 4f, 0.5f, Chronic: true, Stress: 0.01f, Sleep: 0.4f, Cure: Cure.Company),
        new("depression", "우울증", AilmentGroup.Mind, "낮은 사기 · 슬픔 · 깊은 상처", "아무것도 하기 싫다 — 손이 느리다", 8f, 0.5f, Chronic: true, Stress: 0.02f, Work: 0.25f, Sleep: 0.15f, Cure: Cure.Company),
        new("ptsd", "외상 후 스트레스", AilmentGroup.Mind, "아주 무서운 일을 겪음 (긴장 60% 넘음)", "악몽 · 작은 일에도 놀란다 — 공황이 잦다", 10f, 0.5f, Chronic: true, Stress: 0.02f, Sleep: 0.2f, Panic: 1.2f, Cure: Cure.Company),
    };

    /// <summary>열병 포함 30가지.</summary>
    public static IEnumerable<AilmentSpec> Catalog => All.Prepend(Fever);

    private static readonly Dictionary<string, AilmentSpec> ById = All.ToDictionary(a => a.Id);
    public static AilmentSpec Spec(string id) => ById.TryGetValue(id, out var s) ? s : Fever;

    private readonly World _w;
    private readonly Rng _rng;
    private float _clock;
    private float _noFresh; // 신선한 채소·식사가 없던 시간
    private readonly Dictionary<int, (long tick, bool eva)> _exposed = new();
    private readonly Dictionary<(int, string), long> _cooldown = new();
    public AilmentStats Stats { get; } = new();

    /// <summary>시험: 병을 끈다 (다른 것만 견줄 때).</summary>
    public bool Disabled { get; set; }

    public AilmentSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 6967 + 53));
    }

    public static string GroupName(AilmentGroup g) => g switch
    {
        AilmentGroup.Contagious => "옮는 병", AilmentGroup.Environment => "환경", AilmentGroup.Voyage => "긴 항해", AilmentGroup.Mind => "마음", _ => "몸",
    };

    public static string CureName(Cure c) => c switch
    {
        Cure.Rest => "쉬면 낫는다", Cure.Bed => "치료 침대에 누워야", Cure.Medic => "의무관이 약을 써야", Cure.Food => "제대로 먹고 마셔야",
        Cure.Exercise => "몸을 움직여야", Cure.Company => "곁에 누가 있어야", _ => "낫지 않는다 (견딘다)",
    };

    // ───────────────────────────── 앓는 정도 ─────────────────────────────

    /// <summary>앓는 정도 0~1. 옮는 병·급한 병은 오르다 내리고, 오래가는 병은 나은 만큼만 준다.</summary>
    public float Severity(Ailment a)
    {
        var s = Spec(a.Id);
        float days = (_w.Tick - a.Since) / (float)SimTime.TicksPerDay;
        float onset = MathF.Min(1f, days / 0.25f);
        if (s.Chronic) return MathF.Max(0f, a.Peak * onset * (1f - a.Healed / s.Days));
        float t = (days + a.Healed) / s.Days;
        if (t >= 1f) return 0f;
        return a.Peak * onset * MathF.Sin(MathF.PI * MathF.Max(0.08f, t));
    }

    public bool Done(Ailment a)
    {
        var s = Spec(a.Id);
        if (s.Chronic) return a.Healed >= s.Days;
        float days = (_w.Tick - a.Since) / (float)SimTime.TicksPerDay;
        return days + a.Healed >= s.Days;
    }

    public float Worst(CrewMember c) => c.Ailments.Count == 0 ? 0f : c.Ailments.Max(Severity);

    /// <summary>앓는 것 한 줄 (진단 전이면 "어딘가 아프다").</summary>
    public string Line(CrewMember c) =>
        string.Join(" · ", c.Ailments.Select(a => a.Diagnosed ? $"{Spec(a.Id).Name} {Severity(a) * 100:0}%" : $"어딘가 아프다 {Severity(a) * 100:0}%"));

    // ───────────────────────────── 걸린다 ─────────────────────────────

    public bool Has(CrewMember c, string id) => c.Ailments.Any(a => a.Id == id);

    /// <summary>앓기 시작한다 (옮았으면 from).</summary>
    public Ailment? Catch(CrewMember c, string id, CrewMember? from = null, string? why = null)
    {
        var w = _w;
        var s = Spec(id);
        if (c.Dead || Has(c, id) || c.AilmentImmune.Contains(id)) return null;
        if (_cooldown.TryGetValue((c.Id, id), out var until) && w.Tick < until) return null;
        var a = new Ailment { Id = id, Since = w.Tick, Peak = MathF.Min(1f, s.Peak * (0.75f + 0.5f * _rng.Float()) * (1.15f - 0.3f * c.Vitals.Health)), From = from?.Id ?? -1 };
        c.Ailments.Add(a);
        Stats.Cases[id] = Stats.Cases.GetValueOrDefault(id) + 1;
        if (from != null) Stats.Spread++;
        // 의무 자격이 있는 사람이 자기 병은 바로 안다
        if (c.Quals.Contains(Qual.Medic)) a.Diagnosed = true;
        string text = from != null ? $"{Ko.IGa(c.Name)} {from.Name}에게서 {Ko.EulReul(s.Name)} 옮았다" : $"{c.Name} — {s.Name}" + (why != null ? $" ({why})" : "");
        if (s.Health > 0f || s.Peak >= 0.6f) w.Log.Add(w.Tick, LogKind.Warning, a.Diagnosed ? text : $"{c.Name} — 몸이 좋지 않다 ({s.Symptom.Split(" — ")[0]})", c.Id);
        else w.Log.Add(w.Tick, LogKind.Life, a.Diagnosed ? text : $"{c.Name} — 어딘가 아프다 ({s.Symptom.Split(" — ")[0]})", c.Id);
        Life.Diary(w, c, a.Diagnosed ? $"{s.Name}에 걸렸다." : $"몸이 이상하다. {s.Symptom.Split(" — ")[0]}.");
        // 인과 사슬: 사고의 결과일 때만 잇는다 (옮긴 사람의 고리 · 그 방의 불·정전·공기 끊김·운석) — 저절로 생긴 병은 새 사고가 아니다
        int parent = from != null ? w.Causes.OpenNode($"ail:{from.Id}:{id}") : s.Group == AilmentGroup.Environment ? w.Causes.ParentFor(c.Room) : -1;
        if (parent >= 0)
            w.Causes.Effect(CauseKind.Illness, $"ail:{c.Id}:{id}", from != null ? $"{c.Name} {s.Name} ({from.Name}에게서)" : $"{c.Name} {s.Name} — {why ?? s.Cause}", c.Room, c.Position, parent);
        return a;
    }

    /// <summary>지금 이 사람이 무엇에 걸릴 수 있는지와 그 확률 (한 시간에) — 시험·설명서에도 쓴다.</summary>
    public List<(string id, float perHour, string why)> Risks(CrewMember c)
    {
        var w = _w;
        var list = new List<(string, float, string)>();
        if (c.Dead) return list;
        var r = c.Room;
        bool suited = c.Suit is { Oxygen: > 0.05f };
        float day = w.Tick / (float)SimTime.TicksPerDay;
        bool cleanKitchen = w.Crew.Any(x => !x.Dead && x.Quals.Contains(Qual.FoodSafety));
        void Add(string id, float p, string why) { if (p > 0f) list.Add((id, p, why)); }
        // 옮는 병 · 저절로
        Add("cold", 0.0003f, "가끔 저절로");
        Add("gastro", 0.0002f * (cleanKitchen ? 1f : 2.5f), cleanKitchen ? "가끔" : "부엌 위생");
        Add("pinkeye", 0.00015f, "가끔");
        if (r != null && MoistureSystem.Depth(r) > 0.03f) Add("fungus", 0.01f, $"{r.Name}에 물이 고였다");
        if ((DiseaseSystem.Sick(c) || Has(c, "cold")) && c.Vitals.Health < 0.7f) Add("pneumonia", 0.01f, "앓던 몸이 약해져");
        if (c.Vitals.Wounds.Any(x => x.Part == BodyPart.Lungs && !x.Lost)) Add("pneumonia", 0.004f, "다친 폐");
        // 환경
        if (r != null && MoistureSystem.Depth(r) > 0.06f) Add("moldlung", 0.006f, $"{r.Name}의 곰팡이");
        if (c.IsAwake) Add("foodpoison", 0.0002f * (cleanKitchen ? 1f : 3f) * (w.Food.Rationing ? 1.5f : 1f), cleanKitchen ? "상한 음식" : "조리 위생 자격이 있는 사람이 없다");
        if (day < 2f && !c.BornAboard) Add("spacesick", 0.025f * (1.2f - c.Traits.Calm), "출항 첫 이틀");
        if (c.Dose > 1f) Add("radiation", 0.2f, $"피폭 {c.Dose:0.0}Sv");
        if (r != null && !suited && !c.Outside)
        {
            if (r.Air.Temperature < 8f) Add("hypothermia", 0.3f, $"{r.Name} {r.Air.Temperature:0}°C");
            if (r.Air.Temperature > 40f) Add("heatstroke", 0.3f, $"{r.Name} {r.Air.Temperature:0}°C");
            if (r.Air.CO > 0.15f) Add("cobrain", 0.4f, "일산화탄소");
            if (r.Air.Toxin > 0.3f) Add("chemburn", 0.3f, "유독 가스");
            if (r.Air.CO2 > 1.5f) Add("co2ache", 0.2f, $"CO₂ {r.Air.CO2:0.0}%");
        }
        if (w.Water.Level < 5f) Add("dehydration", 0.05f, "물이 바닥났다");
        else if (w.Policies["water"] == 2) Add("dehydration", 0.0015f, "엄격한 물 배급");
        // 긴 항해
        if (day > 20f && c.Fitness < 0.4f) Add("boneloss", 0.004f, $"운동 부족 (체력 단련 {c.Fitness * 100:0}%)");
        if (c.Fitness < 0.3f) Add("atrophy", 0.004f, $"몸이 굳었다 (체력 단련 {c.Fitness * 100:0}%)");
        if (_noFresh > 6f * 24f) Add("scurvy", 0.01f, $"신선한 채소 없이 {_noFresh / 24f:0}일");
        if (c.Needs.Food < 0.15f) Add("malnutrition", 0.01f, "굶주림");
        if (day > 30f) Add("vision", 0.0003f, "오랜 항해");
        Add("kidneystone", 0.00004f + (Has(c, "dehydration") ? 0.003f : 0f) + (Has(c, "boneloss") ? 0.002f : 0f), Has(c, "dehydration") ? "탈수" : Has(c, "boneloss") ? "뼈가 약해졌다" : "가끔");
        // 몸
        Add("backpain", (c.Carrying != null || c.CarryingPerson != null ? (c.Age > 40f ? 0.003f : 0.001f) : 0f) + 0.00008f, c.Carrying != null || c.CarryingPerson != null ? "무거운 짐" : "가끔");
        Add("toothache", 0.00015f, "가끔");
        if (c.Vitals.Injury > 0.25f && w.Tick - c.Vitals.TreatedTick > SimTime.Hours(12)) Add("woundinf", 0.02f, $"상처를 {(w.Tick - c.Vitals.TreatedTick) / (float)SimTime.TicksPerHour:0}시간 못 돌봤다");
        if (c.Age >= 55f) Add("arthritis", 0.001f * (c.Age - 50f) / 10f, $"나이 {c.Age:0}");
        // 마음
        if (c.Needs.Stress > 0.7f) Add("insomnia", 0.008f, $"스트레스 {c.Needs.Stress * 100:0}%");
        bool low = w.Society.Morale < 0.35f, grief = c.GriefUntil > w.Tick, scar = c.Memory.Trauma > 0.5f;
        if (low || grief || scar) Add("depression", 0.003f * (Life.Has(c, Habit.Homesick) ? 1.5f : 1f) * (Life.Has(c, Habit.Optimist) || Life.Has(c, Habit.Cheerful) ? 0.5f : 1f),
            grief ? "슬픔" : scar ? "깊은 상처" : $"사기 {w.Society.Morale * 100:0}%");
        if (c.Memory.Trauma > 0.6f) Add("ptsd", 0.004f, c.Memory.TraumaCause ?? "무서운 일");
        return list;
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Disabled) return;
        bool fresh = w.Ship.CountStored(ItemKind.Produce) + w.Ship.CountStored(ItemKind.Meal) > 0;
        _noFresh = fresh ? 0f : _noFresh + dt;
        _clock += dt;
        bool roll = _clock >= 1f / 6f; // 십 분마다 걸리는지 본다
        float rollDt = _clock;
        if (roll) _clock = 0f;

        foreach (var c in w.Crew)
        {
            if (c.Dead)
            {
                if (c.Ailments.Count > 0) { if (c.Ailments.Any(a => Spec(a.Id).Health > 0f && Severity(a) > 0.5f)) Stats.Deaths++; c.Ailments.Clear(); c.Fx = default; }
                continue;
            }
            Exposure(c);
            if (roll)
                foreach (var (id, p, why) in Risks(c))
                    if (_rng.Chance(MathF.Min(0.9f, p * rollDt))) Catch(c, id, null, why);
            if (c.Ailments.Count == 0) { c.Fx = default; continue; }
            Symptoms(c, dt);
            Heal(c, dt);
            Spread(c, dt);
            // 진단: 의무 자격이 있는 사람이 곁에 있거나, 의무실에 함께 있으면
            if (roll && !c.Outside && c.Ailments.Any(a => !a.Diagnosed && Severity(a) > 0.1f)
                && w.Crew.FirstOrDefault(o => o != c && !o.Dead && o.IsAwake && o.Quals.Contains(Qual.Medic)
                    && ((o.Position - c.Position).LengthSquared() < 9f || o.Room == c.Room && c.Room?.Type == RoomType.Medbay)) is CrewMember doc)
                Diagnose(doc, c);
        }
    }

    /// <summary>감압병: 공기가 갑자기 빠진 방에 맨몸으로 있었거나, 선외 작업을 마치고 들어왔다.</summary>
    private void Exposure(CrewMember c)
    {
        var w = _w;
        bool now = c.Outside || c.Room != null && c.Room.Air.Pressure < 60f && c.Suit == null;
        if (now) { if (!_exposed.ContainsKey(c.Id)) _exposed[c.Id] = (w.Tick, c.Outside); return; }
        if (!_exposed.Remove(c.Id, out var e)) return;
        float minutes = (w.Tick - e.tick) / (float)SimTime.TicksPerHour * 60f;
        float p = e.eva ? 0.06f + 0.002f * minutes : 0.35f;
        if (_rng.Chance(MathF.Min(0.6f, p))) Catch(c, "bends", null, e.eva ? $"선외 작업 {minutes:0}분 뒤" : "공기가 빠진 방에 있었다");
    }

    private void Symptoms(CrewMember c, float dt)
    {
        var w = _w;
        float work = 1f, walk = 1f, sleep = 0f, panic = 0f, bed = 0f, worst = 0f;
        foreach (var a in c.Ailments)
        {
            var s = Spec(a.Id);
            float sev = Severity(a);
            worst = MathF.Max(worst, sev);
            work *= 1f - s.Work * sev;
            walk *= 1f - s.Walk * sev;
            sleep = MathF.Max(sleep, s.Sleep * sev / MathF.Max(0.01f, s.Peak));
            panic += s.Panic * sev / MathF.Max(0.01f, s.Peak);
            // 누워야 하는 병: 침대로 낫는 병 · 약으로 낫는 위험한 병은 심할 때만 (약이 없다고 영영 눕지는 않는다)
            if (s.Cure == Cure.Bed || s.Cure == Cure.Medic && s.Health > 0f && sev > 0.55f) bed = MathF.Max(bed, sev);
            c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - s.Rest * sev * dt);
            if (s.Stress > 0f) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + s.Stress * sev * dt);
            // 위험한 병: 가장 아플 때 침대에 눕지 않으면 몸이 버티지 못한다
            if (s.Health > 0f && sev > 0.45f && !InBed(c))
            {
                c.Vitals.Health = MathF.Max(0f, c.Vitals.Health - s.Health * (sev - 0.35f) * 2f * dt);
                if (_rng.Chance(0.3f * dt)) NeedsSystem.AddInjury(c.Vitals, 0.02f * sev, s.Name); // 의무실로 가게 한다 (죽으면 원인이 남는다)
            }
        }
        c.Fx = new AilmentFx(work, walk, MathF.Min(0.6f, sleep), panic, bed, worst);
    }

    public static bool InBed(CrewMember c) => c.CareBed != null || c.Job?.Activity is RecoverActivity && c.Pose == Pose.Sleeping;

    private void Heal(CrewMember c, float dt)
    {
        var w = _w;
        float day = dt / 24f;
        for (int i = c.Ailments.Count - 1; i >= 0; i--)
        {
            var a = c.Ailments[i];
            var s = Spec(a.Id);
            float gain = 0f;
            bool bedRest = InBed(c);
            switch (s.Cure)
            {
                case Cure.Rest: gain = c.Pose == Pose.Sleeping || bedRest ? 0.3f : 0f; break;
                case Cure.Bed: gain = bedRest ? 1f : c.Pose == Pose.Sleeping ? 0.25f : 0f; break;
                case Cure.Medic: gain = a.TreatedAt >= 0 && w.Tick - a.TreatedAt < SimTime.Hours(24) ? (s.Chronic ? 1.5f : 1f) : bedRest ? 0.3f : 0f; break; // 약이 없어도 누워 있으면 아주 천천히
                case Cure.Food:
                    bool ate = c.Needs.Food > 0.45f && (a.Id != "scurvy" || _noFresh < 1f) && (a.Id != "dehydration" || w.Water.Level > 10f && w.Policies["water"] < 2);
                    gain = ate ? 1f : 0f;
                    break;
                case Cure.Exercise: gain = c.Fitness > 0.5f ? (c.Fitness - 0.4f) * 3f : 0f; break;
                case Cure.Company:
                    bool friend = w.Crew.Any(o => o != c && !o.Dead && o.IsAwake && (o.Position - c.Position).LengthSquared() < 9f && (o.Quals.Contains(Qual.Counseling) || c.AffinityTo(o) > 0.3f));
                    gain = (friend ? 1.5f : 0f) + (w.Society.Morale > 0.6f ? 0.3f : 0f) + (c.Needs.Stress < 0.3f ? 0.3f : 0f);
                    break;
            }
            if (s.Chronic) a.Healed += gain * day;
            else a.Healed += gain * day; // 급한 병은 시간이 가면 낫고, 쉬면 더 빨리
            if (!Done(a)) continue;
            c.Ailments.RemoveAt(i);
            Stats.Recovered++;
            if (s.Immunity) c.AilmentImmune.Add(a.Id);
            _cooldown[(c.Id, a.Id)] = w.Tick + SimTime.Hours(s.Chronic ? 24 * 5 : 24 * 2);
            int n = w.Causes.OpenNode($"ail:{c.Id}:{a.Id}");
            if (n >= 0) w.Causes.Resolve(n, $"{c.Name} {s.Name} 나았다", $"crew:{c.Id}");
            w.Log.Add(w.Tick, LogKind.Life, $"{s.Name} — 다 나았다", c.Id);
            Life.Diary(w, c, $"{s.Name}이(가) 나았다.");
        }
    }

    /// <summary>옮는 병: 같은 방, 우주복은 막는다, 환기·격리실·치료 침대가 줄인다.</summary>
    private void Spread(CrewMember c, float dt)
    {
        var w = _w;
        if (c.Outside || c.Room == null) return;
        foreach (var a in c.Ailments)
        {
            var s = Spec(a.Id);
            if (s.Spread <= 0f) continue;
            float sev = Severity(a);
            if (sev < 0.1f) continue;
            float room = (Atmosphere.Vented(c.Room) ? 0.6f : 1.3f) * (InBed(c) ? 0.3f : 1f)
                         * (c.Room.Type == RoomType.Medbay ? (Facilities.Factor(c.Room, "quarantine") >= 1f ? 0.12f : 0.6f) : 1f)
                         * (w.History.Doctrine.Quarantine ? 0.5f : 1f);
            foreach (var d in w.Crew)
            {
                if (d == c || d.Dead || d.Outside || d.Room != c.Room || d.Suit != null || Has(d, a.Id) || d.AilmentImmune.Contains(a.Id)) continue;
                if (_rng.Chance(s.Spread * sev * room * dt)) Catch(d, a.Id, c);
            }
        }
    }

    // ───────────────────────────── 진단 · 치료 ─────────────────────────────

    /// <summary>의무 자격이 있는 사람이 곁에 오거나 의무실에 있으면 무슨 병인지 안다.</summary>
    public void Diagnose(CrewMember doctor, CrewMember patient)
    {
        var w = _w;
        if (!doctor.Quals.Contains(Qual.Medic)) return;
        foreach (var a in patient.Ailments.Where(a => !a.Diagnosed))
        {
            a.Diagnosed = true;
            Stats.Diagnosed++;
            var s = Spec(a.Id);
            w.Log.Add(w.Tick, LogKind.Life, $"진단: {patient.Name} — {s.Name} ({CureName(s.Cure)})", doctor.Id);
        }
    }

    /// <summary>약을 써야 낫는 병이 있고 (진단됐고), 하루 안에 약을 안 썼다.</summary>
    public Ailment? NeedsMedic(CrewMember c) =>
        c.Ailments.Where(a => a.Diagnosed && Spec(a.Id).Cure == Cure.Medic && (a.TreatedAt < 0 || _w.Tick - a.TreatedAt > SimTime.Hours(20)) && Severity(a) > 0.12f)
            .OrderByDescending(Severity).FirstOrDefault();

    /// <summary>진단 안 된 병이 있다 (의무관이 봐 줘야 한다).</summary>
    public bool Undiagnosed(CrewMember c) => c.Ailments.Any(a => !a.Diagnosed && Severity(a) > 0.15f);

    /// <summary>치료(구급 키트) — 진단하고, 약을 쓴다.</summary>
    /// <summary>v15.1 약 없이 진단만 (구급 키트가 없는 응급 처치).</summary>
    public void DiagnoseOnly(CrewMember doctor, CrewMember patient) => Diagnose(doctor, patient);

    public void Treated(CrewMember patient, CrewMember doctor)
    {
        var w = _w;
        Diagnose(doctor, patient);
        foreach (var a in patient.Ailments.Where(a => a.Diagnosed && Spec(a.Id).Cure is Cure.Medic or Cure.Bed))
        {
            a.TreatedAt = w.Tick;
            a.Treatments++;
            if (Spec(a.Id).Cure == Cure.Bed) a.Healed += 0.3f;
            Stats.Treated++;
        }
    }
}

/// <summary>v14.1 앓는 것의 효과 (매 틱 갱신 — 일 손 · 걸음 · 잠 · 공황 · 누워야 하는 정도 · 가장 아픈 정도).</summary>
public readonly record struct AilmentFx(float Work, float Walk, float Sleep, float Panic, float Bed, float Worst)
{
    public float WorkMul => Work <= 0f ? 1f : Work;
    public float WalkMul => Walk <= 0f ? 1f : Walk;
}
