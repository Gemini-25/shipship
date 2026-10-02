using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.8 시대별 기술: 여섯 시대 · 열세 분야. 연구 점수가 시대 문턱을 넘으면 그 시대 기술을 고를 수 있고,
// 무엇을 먼저 연구할지는 회의가 정한다 (겪은 일 → 그 분야). 기술마다 효과가 있고, 새 기술은 새 위험도 데려온다.
// 설비 단계(Tech, v10.5)와 따로 간다: 설비 단계는 "더 좋은 기계", 시대 기술은 "배가 할 줄 아는 것".

public enum TechField { Power, Cooling, Life, Food, Hull, Medical, Computing, Robotics, Propulsion, Sensors, Fabrication, Habitat, Defense }

public sealed record EraTech(string Id, int Era, TechField Field, string Name, float Cost, string Effect, string Risk, string? RiskKey = null, float RiskMul = 1f);

public sealed class EraSystem
{
    private readonly World _w;
    public HashSet<string> Known { get; } = new();
    /// <summary>익힌 순서.</summary>
    public List<string> Order { get; } = new();
    public string? Project { get; private set; }
    public float Progress { get; private set; }
    public string ProjectWhy { get; private set; } = "";
    private float _lastResearch;

    public static readonly (int era, string name, float research)[] Eras =
    {
        (1, "근지구 시대", 0f), (2, "태양계 시대", 50f), (3, "핵융합 시대", 130f), (4, "성간 준비 시대", 260f), (5, "탈지구 공학 시대", 450f), (6, "초공간 시대", 700f),
    };

    public static readonly EraTech[] All = Base().Concat(ErasV15.Rows.Select(r => r.Tech)).ToArray(); // v15.5 18 → 70 (ErasV15.cs)
    private static EraTech[] Base() => new EraTech[]
    {
        // 1 근지구
        new("fireproof", 1, TechField.Habitat, "불연 내장재", 28.8f, "불이 잘 안 붙는다 (발화 −30%)", "없음"),
        new("triage", 1, TechField.Medical, "응급 분류법", 24f, "치료가 빨라진다 (부상 회복 +25%)", "없음"),
        new("checklist", 1, TechField.Computing, "점검표 문화", 24f, "실수가 준다 (−30%)", "없음"),
        // 2 태양계
        new("genecrops", 2, TechField.Food, "유전자 개량 작물", 40f, "작물이 빨리 자란다 (+20%)", "병충해가 한 번에 번진다", nameof(HazardKind.CropBlight), 1.5f),
        new("magshield", 2, TechField.Defense, "자기장 차폐", 48f, "방사선 −50%", "차폐 코일 과부하 — 전력 서지가 잦다", nameof(HazardKind.PowerSurge), 1.3f),
        new("smartgrid", 2, TechField.Power, "지능형 배전", 40f, "자동화 등급 +1", "컴퓨터 오판단이 잦다", nameof(HazardKind.ComputerMisjudge), 1.4f),
        new("dronenet", 2, TechField.Robotics, "드론 편대", 40f, "외부 설비 수리가 빨라진다", "로봇 오작동이 잦다", nameof(HazardKind.RobotMalfunction), 1.3f),
        // 3 핵융합
        new("fusiondrive", 3, TechField.Propulsion, "핵융합 추진", 72f, "표류해도 반은 간다 · 추진제 −40%", "엔진실 과열", nameof(HazardKind.Overheat), 1.3f),
        new("selfseal", 3, TechField.Hull, "자가 봉합 선체", 72f, "작은 균열은 저절로 닫힌다 (균열 −40%)", "없음"),
        new("biofilter", 3, TechField.Life, "생물 여과", 64f, "물 오염 −50% · 산소 여유", "균이 퍼지기 쉽다", nameof(HazardKind.Epidemic), 1.3f),
        new("quantumsense", 3, TechField.Sensors, "양자 센서", 64f, "운석을 더 일찍 본다 (+30%)", "없음"),
        // 4 성간 준비
        new("gravity", 4, TechField.Habitat, "인공 중력", 96f, "근육이 덜 빠진다 (체력 단련 감소 −70%)", "원심 진동 — 옆방이 시끄럽다"),
        new("nanorepair", 4, TechField.Fabrication, "나노 수리", 112f, "설비가 덜 닳는다 (−25%)", "나노 오염 — 설비가 가끔 이유 없이 상한다", "break", 1.3f),
        new("cryomed", 4, TechField.Medical, "저온 의료", 96f, "죽어 가는 사람을 얼려 붙잡는다 (쓰러진 사람 체력 손실 −50%)", "없음"),
        // 5 탈지구 공학
        new("aicaptain", 5, TechField.Computing, "AI 부함장", 144f, "자동화 등급 +1 · 회의를 돕는다", "AI 판단 충돌 — 컴퓨터 오판단", nameof(HazardKind.ComputerMisjudge), 1.6f),
        new("closedloop", 5, TechField.Life, "완전 폐쇄 생태계", 160f, "물·공기를 거의 잃지 않는다", "생태계 붕괴 — 한 번 무너지면 크게", nameof(HazardKind.CropBlight), 1.4f),
        // 6 초공간
        new("warpbubble", 6, TechField.Propulsion, "공간 왜곡 거품", 240f, "항해가 두 배 빠르다", "거품 붕괴 — 큰 운석", "bigmeteor", 1.5f),
        new("forcefield", 6, TechField.Defense, "역장", 240f, "운석 피해 −50%", "역장 과부하 — 정전", nameof(HazardKind.PowerSurge), 1.5f),
    };

    public EraSystem(World w) => _w = w;

    public bool Has(string id) => Known.Contains(id);

    public int Era => Eras.Where(e => _w.Research >= e.research).Select(e => e.era).DefaultIfEmpty(1).Max();
    public static string EraName(int era) => Eras.First(e => e.era == era).name;

    public IEnumerable<EraTech> Available => TechWeb.Every.Where(t => t.Era <= Era && !Known.Contains(t.Id) && _w.TechWeb.Open(t)); // v16.14 선행 · 조건 · 갈림길 (시대 규칙과 함께)
    public static EraTech? Find(string? id) => TechWeb.Find(id); // v16.14 새 기술 41까지
    public void Boost(float amount) => Progress += amount; // v16.14 실험이 연구를 민다
    internal void Begin(string id, string why) { Project = id; Progress = 0f; ProjectWhy = why; } // v16.14 시험 · 화면

    /// <summary>새 기술이 데려온 위험: 사고 무게 배율.</summary>
    public float RiskMul(string key)
    {
        float m = 1f;
        // 좋아지는 쪽: 자가 봉합 선체는 균열을, 생물 여과는 물 오염을 줄인다
        if (key == nameof(HazardKind.HullCrack) && Known.Contains("selfseal")) m *= 0.6f;
        if (key == nameof(HazardKind.WaterContamination) && Known.Contains("biofilter")) m *= 0.5f;
        if (key == "fire" && Known.Contains("fireproof")) m *= 0.7f;
        foreach (var t in All)
            if (t.RiskKey == key && Known.Contains(t.Id)) m *= t.RiskMul;
        m *= ErasV15.Mul(_w, key); // v15.5 새 기술이 줄이는 사고
        m *= _w.TechWeb.RiskMul(key); // v16.14 새 기술 41 · 부작용 연쇄 · 서툰 이틀
        return m;
    }

    /// <summary>회의: 다음 연구를 고른다 — 겪은 일이 많은 분야를 먼저.</summary>
    private void Choose()
    {
        var w = _w;
        var options = Available.Where(t => !_w.TechWeb.Undecided(t)).ToList(); // v16.14 갈림길은 회의가 먼저 정한다
        if (options.Count == 0) { Project = null; return; }
        var h = w.History;
        float Need(EraTech t) => t.Field switch
        {
            TechField.Habitat => 0.2f * h.Fires + (w.Crew.Any(c => !c.Dead) ? 0.5f * (1f - w.Crew.Where(c => !c.Dead).Average(c => c.Fitness)) : 0f),
            TechField.Medical => 0.3f * w.Crew.Count(c => !c.Dead && c.Vitals.Injury > 0.2f) + 0.5f * h.Deaths,
            TechField.Computing => 0.1f * w.Life.Stats.Mistakes + (w.Automation.MainOnline ? 0f : 0.5f),
            TechField.Food => 0.02f * Evolution.StarvedHours(w),
            TechField.Defense => 0.4f * w.Crew.Count(c => c.Dose > 0.3f) + 0.1f * h.Meteors,
            TechField.Power => 0.3f * h.Scrams + 0.2f * w.Net.Stats.Blackouts,
            TechField.Robotics => 0.2f * w.Exterior.Repairs,
            TechField.Propulsion => w.Voyage.Drifting ? 1f : 0.2f,
            TechField.Hull => 0.15f * h.Meteors + 0.2f * h.Breaches,
            TechField.Life => 0.2f * w.Disease.Stats.Infections,
            TechField.Sensors => 0.1f * h.Meteors,
            TechField.Fabrication => 0.02f * w.Ship.Machines.Sum(m => m.FaultCount),
            _ => 0f,
        };
        // v13.4 방침(연구 방향): 안전 · 효율 · 탐사 쪽 분야를 먼저
        int dir = w.Policies["research"];
        float Lean(EraTech t) => dir switch
        {
            1 => t.Field is TechField.Habitat or TechField.Medical or TechField.Hull or TechField.Defense or TechField.Life ? 0.6f : 0f,
            2 => t.Field is TechField.Power or TechField.Fabrication or TechField.Computing or TechField.Food ? 0.6f : 0f,
            3 => t.Field is TechField.Propulsion or TechField.Sensors or TechField.Robotics ? 0.7f : 0f,
            _ => 0f,
        };
        var rec = _w.TechWeb.Advise(options, Need); // v16.14 주 컴퓨터가 근거와 함께 추천
        var pick = options.OrderByDescending(t => Need(t) + Lean(t) - 0.01f * _w.TechWeb.CostOf(t) + _w.TechWeb.Bias(t, rec)).ThenBy(t => t.Id, StringComparer.Ordinal).First();
        Project = pick.Id;
        Progress = 0f;
        ProjectWhy = (Need(pick) > 0.3f ? $"겪은 일 때문에 ({Fields(pick.Field)})" : Lean(pick) > 0f ? $"연구 방침 '{w.Policies.Option("research")}'" : TechWeb.Node(pick.Id).Trial ? "배에 둔 유물이 궁금하다" : "다음 차례") + _w.TechWeb.Followed(pick, rec);
        w.History.Add(w, HistoryKind.Decision, $"회의: 다음 연구는 {pick.Name} — {pick.Effect} ({ProjectWhy})", null, log: true);
    }

    public static string Fields(TechField f) => f switch
    {
        TechField.Power => "동력", TechField.Cooling => "냉각", TechField.Life => "생명유지", TechField.Food => "식량", TechField.Hull => "선체",
        TechField.Medical => "의료", TechField.Computing => "컴퓨터", TechField.Robotics => "로봇", TechField.Propulsion => "추진",
        TechField.Sensors => "센서", TechField.Fabrication => "제작", TechField.Habitat => "거주", _ => "방어",
    };

    /// <summary>시스템 틱: 쌓인 연구의 일부가 지금 연구로 들어간다.</summary>
    public void Update()
    {
        var w = _w;
        float gained = MathF.Max(0f, w.Research - _lastResearch);
        _lastResearch = w.Research;
        if (Project == null) { Choose(); if (Project == null) return; }
        var t = Find(Project)!;
        Progress += gained * _w.TechWeb.FlowMul(t); // v16.14 역설계는 실험으로
        if (Progress < _w.TechWeb.CostOf(t)) return; // v16.14 버린 갈림길을 다시 꺼내면 두 배
        Known.Add(t.Id);
        Order.Add(t.Id);
        Project = null;
        w.RaiseAlert($"새 기술 — {t.Name}: {t.Effect}" + (t.Risk != "없음" ? $" · 위험: {t.Risk}" : ""), null, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, $"{t.Name}을(를) 익혔다 ({EraName(t.Era)}) — {t.Effect}", null, log: true);
        // v13.0 컴퓨터는 처음부터 V — 기술은 기능 모듈을 단다
        if (t.Id == "smartgrid") w.Automation.Install(ComputerModule.Preempt, "지능형 배전"); // v16.20 이미 있는 모듈 — 등급이 오른다
        if (t.Id == "aicaptain") { w.Automation.Install(ComputerModule.BioMonitor, "AI 부함장"); w.Automation.Install(ComputerModule.EvacGuide, "AI 부함장"); }
        w.TechWeb.OnLearned(t); // v16.14 갈림길 · 부작용 연쇄 · 조합 · 실험한 사람
    }
}
