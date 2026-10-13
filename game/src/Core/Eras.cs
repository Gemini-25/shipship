using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 기술 체계 개편: 시대 일곱 × 줄기 여덟 (TechTree.cs). 아래 옛 설명은 v12.8 그대로 — 옛 기술 표(All)는 흡수된 아이디로 남는다.
// v12.8 시대별 기술: 여섯 시대 · 열세 분야. 연구 점수가 시대 문턱을 넘으면 그 시대 기술을 고를 수 있고,
// 무엇을 먼저 연구할지는 회의가 정한다 (겪은 일 → 그 분야). 기술마다 효과가 있고, 새 기술은 새 위험도 데려온다.
// 설비 단계(Tech, v10.5)와 따로 간다: 설비 단계는 "더 좋은 기계", 시대 기술은 "배가 할 줄 아는 것".

public enum TechField { Power, Cooling, Life, Food, Hull, Medical, Computing, Robotics, Propulsion, Sensors, Fabrication, Habitat, Defense }

public sealed record EraTech(string Id, int Era, TechField Field, string Name, float Cost, string Effect, string Risk, string? RiskKey = null, float RiskMul = 1f);

public sealed class EraSystem
{
    private readonly World _w;
    /// <summary>익힌 것: 새 기술표 아이디(p1 · l2 …) + 그 안에 담긴 옛 기술 아이디 + 갈림길에서 고른 갈래 (옛 아이디로 효과를 읽는 곳이 그대로 돈다).</summary>
    public HashSet<string> Known { get; } = new();
    /// <summary>익힌 순서 (새 기술표 아이디).</summary>
    public List<string> Order { get; } = new();
    public string? Project { get; private set; }
    public float Progress { get; private set; }
    public string ProjectWhy { get; private set; } = "";
    /// <summary>함장이 "이것부터" 고른 기술 (익히면 풀린다).</summary>
    public string? Priority { get; private set; }
    /// <summary>지금 시대 (한 번 오르면 내려가지 않는다 — 잃은 기술은 다시 배운다).</summary>
    public int Reached { get; private set; } = 1;
    /// <summary>마지막 시대의 여덟 기술을 모두 익혔다 — 항해의 목표.</summary>
    public bool Complete { get; private set; }
    /// <summary>하다 만 연구 (다른 것으로 바꿔도 쌓은 것은 남는다).</summary>
    public Dictionary<string, float> Partial { get; } = new();
    /// <summary>갈림길에서 기우는 갈래 (익히기 전엔 함장이 뒤집을 수 있다): 갈림길 → 0 · 1.</summary>
    public Dictionary<string, int> Picks { get; } = new();
    public Dictionary<string, string> PickWhy { get; } = new();
    /// <summary>잊은 기술 (아는 사람이 모두 떠났다) — 다시 배우면 반값.</summary>
    public HashSet<string> Lost { get; } = new();
    /// <summary>아는 사람이 없는 날 수.</summary>
    public Dictionary<string, int> Orphan { get; } = new();
    /// <summary>익힌 순간들 (화면 카드 · 연대기). 시대가 오른 순간도 "era:N"으로.</summary>
    public List<TechUnlock> Unlocks { get; } = new();
    /// <summary>겪은 일에서 배운 것 (줄기마다) — 그 줄기 연구를 시작하면 진척으로 들어간다.</summary>
    public float[] Insight { get; } = new float[8];
    public EraStats Stats { get; } = new();
    /// <summary>기술 하나의 값 배율 (설정 파일 · 시험). 보통 배에서 작은 기술은 엿새 남짓, 시대 하나는 한두 달.</summary>
    public static float CostScale = 2.0f;
    /// <summary>배 크기 배율: 사람이 많으면 연구도 빠르니 값도 조금 크다 ((사람/8)^0.35 — 첫 날 정한다).</summary>
    public float SizeMul { get; private set; } = 1f;
    /// <summary>기술을 지키는 솜씨: 살아 있는 사람 가운데 이만큼 하는 사람이 없으면 날이 센다.</summary>
    public static float KeepSkill = 0.3f;
    public static int ForgetDays = 10;
    /// <summary>겪은 일 하나가 그 줄기 연구에 보태는 값 (한 시간에 둘까지).</summary>
    public static float LessonValue = 1.5f;
    private readonly int[] _lessons = new int[8];
    private bool _lessonInit;
    private float _lastResearch;
    private long _nextHour, _nextDay = -1;
    private int _treeSeen = -1;
    private readonly Dictionary<string, float> _treeMul = new();

    /// <summary>시대 일곱 (옛 화면 · 시험이 읽는 모양 — 연구 문턱은 없다: 시대는 여덟 기술로 오른다).</summary>
    public static readonly (int era, string name, float research)[] Eras = Enumerable.Range(1, TechTree.Eras).Select(i => (i, TechTree.EraName(i), 0f)).ToArray();

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
        new("aicaptain", 5, TechField.Computing, "부함장 보조 컴퓨터", 144f, "자동화 등급 +1 · 회의를 돕는다", "보조 컴퓨터와 판단이 엇갈림 — 컴퓨터 오판단", nameof(HazardKind.ComputerMisjudge), 1.6f),
        new("closedloop", 5, TechField.Life, "완전 폐쇄 생태계", 160f, "물·공기를 거의 잃지 않는다", "생태계 붕괴 — 한 번 무너지면 크게", nameof(HazardKind.CropBlight), 1.4f),
        // 6 초공간
        new("warpbubble", 6, TechField.Propulsion, "공간 왜곡 거품", 240f, "항해가 두 배 빠르다", "거품 붕괴 — 큰 운석", "bigmeteor", 1.5f),
        new("forcefield", 6, TechField.Defense, "역장", 240f, "운석 피해 −50%", "역장 과부하 — 정전", nameof(HazardKind.PowerSurge), 1.5f),
    };


    public EraSystem(World w) => _w = w;

    public bool Has(string id) => Known.Contains(id);
    public int Era => Reached;
    public static string EraName(int era) => TechTree.EraName(era);
    public static EraTech? Find(string? id) => TechWeb.Find(id);
    public void Boost(float amount) => Progress += amount; // v16.14 실험이 연구를 민다
    /// <summary>시험 · 화면: 이 기술을 연구한다 (옛 아이디면 그것을 담은 새 기술).</summary>
    internal void Begin(string id, string why) { if ((TechTree.Find(id) ?? TechTree.OwnerOf(id)) is TreeTech t) Start(t, why); }

    /// <summary>시험: 지금 바로 익힌다 (값을 치른 셈).</summary>
    internal void LearnNow(string id, bool quiet = false) { if ((TechTree.Find(id) ?? TechTree.OwnerOf(id)) is TreeTech t && !Known.Contains(t.Id)) Learn(t, quiet); }

    public TreeTech? ProjectTech => TechTree.Find(Project);
    public float CostOf(TreeTech t) => t.Cost * CostScale * SizeMul * (Lost.Contains(t.Id) ? 0.5f : 1f);
    public float Fraction => ProjectTech is TreeTech pt ? Math.Clamp(Progress / CostOf(pt), 0f, 1f) : 0f;
    public int LearnedIn(int era) { int n = 0; foreach (var t in TechTree.Main) if (t.Era == era && Known.Contains(t.Id)) n++; return n; }
    public int MainKnown { get { int n = 0; foreach (var t in TechTree.Main) if (Known.Contains(t.Id)) n++; return n; } }
    public int SideKnown { get { int n = 0; foreach (var t in TechTree.SideTechs) if (Known.Contains(t.Id)) n++; return n; } }

    /// <summary>지금 연구할 수 있는 것: 이 시대까지의 본 기술(잊은 것 포함) + 조건이 풀린 곁가지.</summary>
    public IEnumerable<TreeTech> Available
    {
        get
        {
            foreach (var t in TechTree.Main) if (t.Era <= Reached && !Known.Contains(t.Id)) yield return t;
            foreach (var t in TechTree.SideTechs) if (!Known.Contains(t.Id) && SideOpen(t)) yield return t;
        }
    }

    /// <summary>곁가지가 보이나: 숨은 조합은 두 재료를 다 알 때, 겪어야 열리는 것은 그 일을 겪었을 때.</summary>
    public bool SideOpen(TreeTech t)
    {
        if (t.Combo is { } c) { foreach (var o in c) if (!Known.Contains(o)) return false; return true; }
        return t.GateOld is string g && TechWeb.Find(g) is EraTech e && TechWeb.Node(g).Gate != null && _w.TechWeb is TechWebSystem tw && tw.GateMet(e);
    }

    /// <summary>한 칸의 상태 글 (화면).</summary>
    public string Status(TreeTech t) =>
        Known.Contains(t.Id) ? "익힘"
        : Project == t.Id ? $"연구 중 {Fraction * 100f:0}%"
        : Lost.Contains(t.Id) ? "잊음 — 다시 배우면 반값"
        : t.Side ? (SideOpen(t) ? "보임 — 연구할 수 있다" : "?")
        : t.Era > Reached ? $"{TechTree.Roman(t.Era)} 시대에 열린다"
        : Partial.TryGetValue(t.Id, out var p) && p > 0f ? $"열림 · {p / CostOf(t) * 100f:0}% 해 둠" : "열림";

    /// <summary>새 기술표의 효과 배율 (시대 6~7 · 행성 · 원자로 · 산소 …). 익힌 수가 바뀔 때만 다시 곱한다.</summary>
    public float TreeMul(string key)
    {
        if (Known.Count != _treeSeen)
        {
            _treeSeen = Known.Count;
            _treeMul.Clear();
            foreach (var t in TechTree.Main)
                if (t.Fx != null && Known.Contains(t.Id))
                    foreach (var (k, m) in t.Fx) _treeMul[k] = (_treeMul.TryGetValue(k, out var v) ? v : 1f) * m;
        }
        return _treeMul.TryGetValue(key, out var x) ? x : 1f;
    }

    /// <summary>이 설비 단계를 가져온 기술 이름 (설치 공사 글).</summary>
    public string Installer(FurnitureType type, int tier)
    {
        foreach (var t in TechTree.Main)
            if (Known.Contains(t.Id))
                foreach (var (ft, tr) in t.Lifts)
                    if (ft == type && tr >= tier) return t.Name;
        return "새 설계";
    }

    /// <summary>이 설비를 어느 단계까지 지을 줄 아나 (익힌 기술의 설비 단계 · 갈림길에서 버린 쪽은 막힌다).</summary>
    public int TierFor(FurnitureType type)
    {
        int best = 1;
        foreach (var t in TechTree.Main)
            if (Known.Contains(t.Id))
                foreach (var (ft, tier) in t.Lifts)
                    if (ft == type && tier > best) best = tier;
        while (best > 1 && !TechWeb.TierOk(_w, type, best)) best--;
        return best;
    }

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


    // ─────────────────────────────── 고르기 ───────────────────────────────

    /// <summary>이 줄기가 지금 얼마나 급한가 (겪은 일) + 한 마디.</summary>
    public (float v, string why) Need(Branch b)
    {
        var w = _w;
        var h = w.History;
        int alive = 0, hurt = 0;
        foreach (var c in w.Crew) { if (c.Dead || c.LeftShip) continue; alive++; if (c.Vitals.Injury > 0.2f) hurt++; }
        switch (b)
        {
            case Branch.Power:
            {
                float tight = w.Power.Demand > 0.85f * w.Power.ReactorRated ? 0.5f : 0f;
                float v = 0.3f * h.Scrams + 0.2f * w.Net.Stats.Blackouts + tight;
                return (v, h.Scrams > 0 ? $"원자로가 {h.Scrams}번 섰다" : w.Net.Stats.Blackouts > 0 ? $"정전 {w.Net.Stats.Blackouts}번" : "전기가 빠듯하다");
            }
            case Branch.Life:
            {
                float air = w.Air.Reserve < 0.6f * w.Air.ReserveCapacity ? 0.5f : 0f;
                float water = w.Water.Level < 0.5f * w.Water.Capacity ? 0.5f : 0f;
                float v = 0.2f * w.Disease.Stats.Infections + 0.2f * h.Fires + air + water;
                return (v, water > 0f ? "물이 빠듯하다" : air > 0f ? "공기 탱크가 줄었다" : h.Fires > 0 ? $"불이 {h.Fires}번 났다" : $"병이 {w.Disease.Stats.Infections}번 돌았다");
            }
            case Branch.Food:
            {
                float starved = Evolution.StarvedHours(w);
                return (0.02f * starved, $"굶주린 {starved:0}사람·시간");
            }
            case Branch.Medical: return (0.3f * hurt + 0.5f * h.Deaths, h.Deaths > 0 ? $"{h.Deaths}명을 잃었다" : $"다친 사람 {hurt}");
            case Branch.Hull:
            {
                int dosed = 0;
                foreach (var c in w.Crew) if (!c.Dead && c.Dose > 0.3f) dosed++;
                return (0.15f * h.Meteors + 0.2f * h.Breaches + 0.3f * dosed, h.Breaches > 0 ? $"선체가 {h.Breaches}번 뚫렸다" : dosed > 0 ? $"방사선을 맞은 사람 {dosed}" : $"운석 {h.Meteors}번");
            }
            case Branch.Machines:
            {
                int faults = TechWeb.Incidents(w, "break");
                float v = 0.1f * w.Life.Stats.Mistakes + (w.Automation.MainOnline ? 0f : 0.5f) + 0.02f * faults;
                return (v, !w.Automation.MainOnline ? "주 컴퓨터가 꺼졌었다" : $"고장 {faults}번 · 실수 {w.Life.Stats.Mistakes}번");
            }
            case Branch.Navigation: return (w.Voyage.Drifting ? 1f : 0.2f, w.Voyage.Drifting ? "엔진이 멎어 떠돈다" : "갈 길이 멀다");
            default:
                return ((w.Planet.Landed ? 0.6f : 0f) + 0.1f * w.Expedition.Stats.Trips, w.Planet.Landed ? "행성에 내려앉았다" : $"원정 {w.Expedition.Stats.Trips}번");
        }
    }

    private float Lean(Branch b) => _w.Policies["research"] switch // v13.4 방침(연구 방향): 안전 · 효율 · 탐사
    {
        1 => b is Branch.Medical or Branch.Hull or Branch.Life ? 0.6f : 0f,
        2 => b is Branch.Power or Branch.Machines or Branch.Food ? 0.6f : 0f,
        3 => b is Branch.Navigation or Branch.Planet ? 0.7f : 0f,
        _ => 0f,
    };

    /// <summary>회의(주 컴퓨터가 있으면 그 권고로): 다음 연구를 고른다 — 함장 우선 지시가 먼저, 그다음 겪은 일 · 방침 · 잊은 것 · 배운 것.</summary>
    private void Choose()
    {
        var w = _w;
        var opts = Available.ToList();
        if (opts.Count == 0) { Project = null; return; }
        if (Priority != null)
        {
            foreach (var o in opts) if (o.Id == Priority) { Start(o, "함장 우선 지시"); return; }
            Priority = null;
        }
        TreeTech? best = null;
        float bestS = float.MinValue;
        string bestWhy = "";
        foreach (var t in opts)
        {
            var (need, needWhy) = Need(t.Branch);
            float lean = Lean(t.Branch), cost = CostOf(t);
            float ins = MathF.Min(0.5f, Insight[(int)t.Branch] / MathF.Max(1f, cost));
            float part = Partial.TryGetValue(t.Id, out var pp) ? 0.5f * pp / cost : 0f;
            bool lost = Lost.Contains(t.Id);
            float s = need + lean + ins + part + (lost ? 0.6f : 0f) - (t.Side ? 0.35f : 0f) - 0.004f * cost; // 곁가지는 겪은 일이 클 때만 앞선다 (시대를 막지 않게)
            string why = lost ? "잊은 기술을 되찾는다" : need > 0.3f ? needWhy : lean > 0f ? $"연구 방침 '{w.Policies.Option("research")}'"
                : ins > 0.05f ? "겪은 일에서 배운 것이 있다" : part > 0f ? "하다 만 연구" : t.Side ? "숨은 기술이 보였다" : "다음 차례 — 가장 빨리 끝난다";
            if (s > bestS || s == bestS && best != null && string.CompareOrdinal(t.Id, best.Id) < 0) { best = t; bestS = s; bestWhy = why; }
        }
        string by = w.Automation.Present && w.Automation.MainOnline ? "주 컴퓨터 권고 · 회의" : "회의";
        Start(best!, $"{by} — {bestWhy}");
    }

    private void Start(TreeTech t, string why)
    {
        var w = _w;
        if (Project != null && Project != t.Id && Progress > 0f) Partial[Project] = Progress;
        Project = t.Id;
        Progress = Partial.TryGetValue(t.Id, out var p) ? p : 0f;
        Partial.Remove(t.Id);
        int b = (int)t.Branch;
        float take = MathF.Min(Insight[b], 0.5f * CostOf(t));
        if (take > 0f) { Progress += take; Insight[b] -= take; }
        ProjectWhy = why;
        if (t.Fork is { } f && !Picks.ContainsKey(f.fork)) PickFork(t);
        Stats.Starts++;
        w.History.Add(w, HistoryKind.Decision, $"다음 연구 — {t.Name} ({TechTree.BranchName(t.Branch)}) · {why}" + (take >= 1f ? $" · 겪은 일에서 배운 것 +{take:0}" : ""), null, log: true);
    }

    /// <summary>함장: 이것부터 연구하라 (탭에서 누름). 연구할 수 없는 것이면 false. null이면 지시를 거둔다.</summary>
    public bool SetPriority(string? id)
    {
        var w = _w;
        if (id == null) { Priority = null; return true; }
        var t = TechTree.Find(id);
        if (t == null || Known.Contains(t.Id)) return false;
        bool open = false;
        foreach (var a in Available) if (a.Id == id) { open = true; break; }
        if (!open) return false;
        Priority = id;
        Stats.Orders++;
        w.Log.Add(w.Tick, LogKind.Ship, $"함장: {Ko.EulReul(t.Name)} 먼저 연구하라");
        if (Project != id) Start(t, "함장 우선 지시");
        return true;
    }

    // ─────────────────────────────── 갈림길 ───────────────────────────────

    private void PickFork(TreeTech t)
    {
        var (fid, q, a, b) = t.Fork!.Value;
        var (side, why) = ForkLean(fid);
        Picks[fid] = side;
        PickWhy[fid] = why;
        _w.Log.Add(_w.Tick, LogKind.Ship, $"갈림길 — {q}: {(side == 0 ? a.Name : b.Name)} 쪽으로 기운다 ({why})");
    }

    /// <summary>함장이 갈래를 뒤집는다 (그 기술을 익히기 전까지).</summary>
    public bool FlipFork(string fid)
    {
        var owner = TechTree.Main.FirstOrDefault(m => m.Fork?.fork == fid);
        if (owner == null || Known.Contains(owner.Id)) return false;
        int side = Picks.TryGetValue(fid, out var s) ? 1 - s : 1 - ForkLean(fid).side;
        Picks[fid] = side;
        PickWhy[fid] = "함장이 골랐다";
        var (_, q, a, b) = owner.Fork!.Value;
        _w.Log.Add(_w.Tick, LogKind.Ship, $"함장: {q} — {(side == 0 ? a.Name : b.Name)}로 간다");
        return true;
    }

    /// <summary>갈래와 그 이유 (이 배의 형편 — 정해진 규칙이라 다시 돌려도 같다).</summary>
    public (int side, string why) ForkLean(string fid)
    {
        var w = _w;
        int alive = 0;
        float soc = 0f;
        foreach (var c in w.Crew) { if (c.Dead || c.LeftShip) continue; alive++; soc += c.Traits.Sociability; }
        soc = alive > 0 ? soc / alive : 0.5f;
        return fid switch
        {
            "oxygen" => w.Ship.RoomsOf(RoomType.Hydroponics).Any() ? (1, "수경 재배실이 있다 — 조류 키울 자리") : (0, "재배실이 없다 — 약품이 손쉽다"),
            "armor" => w.History.Meteors >= 2 ? (0, $"운석을 {w.History.Meteors}번 맞았다 — 두껍게") : (1, "운석이 드물었다 — 쏘아 맞힌다"),
            "brain" => alive < 15 ? (0, $"사람이 {alive}명 — 한 머리가 낫다") : (1, $"사람이 {alive}명 — 손이 여럿"),
            "repair" => alive >= 12 ? (1, $"일손이 {alive}명 — 쇠 팔을 입힌다") : (0, $"일손이 {alive}명뿐 — 설비가 스스로 아물어야"),
            "radiation" => w.Water.Capacity >= 800f ? (0, $"물탱크가 크다 ({w.Water.Capacity:0}L)") : (1, $"물탱크가 작다 ({w.Water.Capacity:0}L) — 자기장으로"),
            "farming" => w.Ship.RoomsOf(RoomType.Garden).Any() ? (1, "정원이 있다 — 흙을 안다") : (0, "흙이 없다 — 양액으로"),
            "pests" => w.Eco.Cat is ShipCat cat ? (0, $"배에 고양이 {Ko.IGa(cat.Name)} 있다") : (1, "고양이가 없다 — 통을 잠근다"),
            "float" => soc >= 0.5f ? (0, $"서로 챙기는 사람들 (어울림 {soc:0.00}) — 끈을 맨다") : (1, $"제각각인 사람들 (어울림 {soc:0.00}) — 자석 밑창"),
            _ => (0, ""),
        };
    }

    // ─────────────────────────────── 익히기 ───────────────────────────────

    private static readonly HashSet<string> HigherBetter = new()
    {
        "grow", "heal", "repair", "craft", "research", "water", "treat", "cook", "o2", "reactor", "speed", "mining", "omen", "lab.speed", "exp.relic", "cosmic.conf",
    };

    public static string KeyLabel(string k) => k switch
    {
        "grow" => "작물 생장", "heal" => "회복", "repair" => "수리 속도", "craft" => "제작 속도", "wear" => "설비 닳음", "omen" => "사고 낌새 알아챔",
        "research" => "연구", "fault" => "고장", "water" => "물 되살림", "treat" => "치료", "cook" => "조리", "o2" => "산소", "reactor" => "원자로 출력",
        "speed" => "항해 속도", "env" => "행성 환경 부상", "landing" => "불시착", "mining" => "채굴",
        _ => TechWeb.KeyName(k),
    };

    /// <summary>이 기술이 바꾸는 수치들 (카드의 전후 숫자).</summary>
    private static List<string> FxKeys(TreeTech t, IEnumerable<string> adds)
    {
        var keys = new List<string>();
        void K(string k) { if (!keys.Contains(k)) keys.Add(k); }
        foreach (var id in adds)
        {
            if (TechWeb.RowOf(id) is TechWeb.Row r) foreach (var (k, _) in r.Fx) K(k);
            foreach (var v in ErasV15.Rows) if (v.Tech.Id == id) foreach (var (k, _) in v.Fx) K(k);
            if (TechWeb.Find(id) is EraTech e && e.RiskKey != null) K(e.RiskKey);
            if (id == "selfseal") K(nameof(HazardKind.HullCrack));
            if (id == "biofilter") K(nameof(HazardKind.WaterContamination));
            if (id == "fireproof") K("fire");
        }
        if (t.Fx != null) foreach (var (k, _) in t.Fx) K(k);
        return keys;
    }

    private float Value(string k) => RiskMul(k);

    private void Learn(TreeTech t, bool quiet = false)
    {
        var w = _w;
        int side = -1;
        if (t.Fork is { } f)
        {
            if (!Picks.ContainsKey(f.fork)) PickFork(t);
            side = Picks[f.fork];
        }
        var adds = new List<string>(t.Absorbs);
        if (t.Fork is { } f2) adds.Add(side == 0 ? f2.a.Id : f2.b.Id);
        var keys = quiet ? new List<string>() : FxKeys(t, adds);
        var before = keys.Select(Value).ToArray();
        var tierBefore = t.Lifts.Select(l => TierFor(l.type)).ToArray();

        Known.Add(t.Id);
        foreach (var id in adds) Known.Add(id);
        Order.Add(t.Id);
        bool relearn = Lost.Remove(t.Id);
        Orphan.Remove(t.Id);
        if (Project == t.Id) { Project = null; Progress = 0f; }
        Partial.Remove(t.Id);
        if (Priority == t.Id) Priority = null;
        Stats.Learned++;
        if (relearn) Stats.Relearned++;

        // 갈림길: 고른 갈래가 배의 얼굴이 된다 (TechWeb 갈림길 상태 · 별명)
        if (t.Fork is { } f3)
            foreach (var tf in TechWeb.Forks)
                if (tf.Id == f3.fork && w.TechWeb.Side(tf.Id) < 0)
                {
                    if (quiet) w.TechWeb.DecideQuiet(tf, side, PickWhy.GetValueOrDefault(tf.Id, ""));
                    else w.TechWeb.Decide(tf, side, PickWhy.GetValueOrDefault(tf.Id, ""), Picks.ContainsKey(tf.Id) && PickWhy.GetValueOrDefault(tf.Id) == "함장이 골랐다" ? "함장" : "회의", 0, 0);
                }
        // v13.0 컴퓨터는 처음부터 V — 기술은 기능 모듈을 단다
        if (adds.Contains("smartgrid")) w.Automation.Install(ComputerModule.Preempt, "지능형 배전");
        if (adds.Contains("aicaptain")) { w.Automation.Install(ComputerModule.BioMonitor, "부함장 보조 컴퓨터"); w.Automation.Install(ComputerModule.EvacGuide, "부함장 보조 컴퓨터"); }
        if (adds.Contains("centralcpu")) w.Automation.Install(ComputerModule.Foresight, "중앙 컴퓨터");
        if (adds.Contains("distctrl")) w.Automation.Install(ComputerModule.PowerShare, "분산 제어");
        if (quiet) { CheckEra(true); return; }

        // 카드: 말 + 전후 숫자 + 설치할 설비
        var lines = new List<string>();
        var tones = new List<int>();
        for (int i = 0; i < keys.Count; i++)
        {
            float a = before[i], b = Value(keys[i]);
            if (MathF.Abs(a - b) < 0.005f) continue;
            bool good = HigherBetter.Contains(keys[i]) ? b > a : b < a;
            lines.Add($"{KeyLabel(keys[i])} {a * 100f:0}% → {b * 100f:0}%");
            tones.Add(good ? 1 : 2);
        }
        foreach (var id in adds)
            if (TechWeb.Find(id) is EraTech e && TechWeb.RowOf(id) == null && !ErasV15.Rows.Any(r => r.Tech.Id == id) && !lines.Contains(e.Effect))
            { lines.Add(e.Effect); tones.Add(0); if (e.Risk != "없음") { lines.Add("새 위험: " + e.Risk); tones.Add(2); } }
        var installs = new List<string>();
        for (int i = 0; i < t.Lifts.Length; i++)
        {
            var (type, tier) = t.Lifts[i];
            int now = TierFor(type);
            if (now <= tierBefore[i]) continue;
            var tiers = Tech.Tiers(type);
            var from = tiers[Math.Clamp(tierBefore[i] - 1, 0, tiers.Count - 1)];
            var to = tiers[Math.Clamp(now - 1, 0, tiers.Count - 1)];
            int n = 0;
            foreach (var fu in w.Ship.FurnitureOf(type)) if (fu.Machine is Machine m && m.Tier < now) n++;
            installs.Add($"{FurnitureTypes.Name(type)} {from.Name} → {to.Name} ({to.Note})" + (n > 0 ? $" · {n}대 공사 기다림" : ""));
            lines.Add($"{FurnitureTypes.Name(type)} 힘 {from.Output * 100f:0}% → {to.Output * 100f:0}%");
            tones.Add(1);
        }
        string pick = t.Fork is { } f4 ? $"{(side == 0 ? f4.a.Name : f4.b.Name)} — {(side == 0 ? f4.a.Says : f4.b.Says)} ({PickWhy.GetValueOrDefault(f4.fork, "")})" : "";
        Unlocks.Add(new TechUnlock(t.Id, w.Tick, t.Side ? TechTree.SideEra(t) : t.Era, lines.ToArray(), tones.ToArray(), installs.ToArray(), pick, relearn));
        if (Unlocks.Count > 80) Unlocks.RemoveAt(0);
        string says = t.Says.Length > 0 ? t.Says : (TechWeb.Find(t.Absorbs.FirstOrDefault()) is EraTech se ? se.Effect : "");
        w.RaiseAlert($"새 기술 — {t.Name}" + (says.Length > 0 ? $": {says}" : "") + (relearn ? " (되찾음)" : ""), null, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, $"{Ko.EulReul(t.Name)} {(relearn ? "되찾았다" : "익혔다")} ({TechTree.Roman(Math.Max(1, t.Era))} · {TechTree.BranchName(t.Branch)})" + (says.Length > 0 ? $" — {says}" : "") + (pick.Length > 0 ? $" · 갈래: {pick}" : ""), null, log: true);
        // 옛 그물: 부작용 연쇄 · 서툰 이틀 · 실험한 사람 · 조건 다시 보기
        if (TechWeb.Find(t.Id) is EraTech me) w.TechWeb.OnLearned(me);
        foreach (var id in adds) if (TechWeb.Find(id) is EraTech old) w.TechWeb.OnLearned(old);
        CheckEra(false);
    }

    private void CheckEra(bool quiet)
    {
        var w = _w;
        while (LearnedIn(Reached) >= 8)
        {
            if (Reached >= TechTree.Eras)
            {
                if (Complete) break;
                Complete = true;
                if (!quiet)
                {
                    Unlocks.Add(new TechUnlock("era:goal", w.Tick, Reached, new[] { "일곱 시대의 기술을 모두 익혔다 — 이 배는 스스로 살아가는 배가 되었다" }, new[] { 1 }, Array.Empty<string>(), "", false));
                    w.RaiseAlert($"항해의 목표 — {TechTree.EraName(Reached)}의 여덟 기술을 모두 익혔다", null, AlertLevel.Notice, shipWide: true);
                    w.History.Add(w, HistoryKind.Milestone, $"항해의 목표에 닿았다 — 일곱 시대의 기술을 모두 익힌 배", null, log: true);
                }
                break;
            }
            Reached++;
            Stats.EraUps++;
            if (quiet) continue;
            Unlocks.Add(new TechUnlock("era:" + Reached, w.Tick, Reached, new[] { $"{TechTree.Roman(Reached)} {TechTree.EraName(Reached)} 시대가 열렸다 — 새 기술 여덟이 보인다" }, new[] { 1 }, Array.Empty<string>(), "", false));
            w.RaiseAlert($"새 시대 — {TechTree.Roman(Reached)} {TechTree.EraName(Reached)}", null, AlertLevel.Notice, shipWide: true);
            w.History.Add(w, HistoryKind.Milestone, $"새 시대 — {TechTree.Roman(Reached)} {TechTree.EraName(Reached)} (지난 시대의 여덟 기술을 모두 익혔다)", null, log: true);
        }
    }

    /// <summary>배가 생길 때: 크기만큼 앞선 시대에서 시작한다 (그 앞 시대의 기술을 조용히 익히고, 설비는 그 단계로 지어져 있다).</summary>
    public void StartAt(int era)
    {
        era = Math.Clamp(era, 1, TechTree.Eras);
        foreach (var t in TechTree.Main)
            if (t.Era < era && !Known.Contains(t.Id)) Learn(t, quiet: true);
        Stats.StartEra = Reached;
        _lastResearch = _w.Research;
    }

    // ─────────────────────────────── 겪은 일 · 잊기 ───────────────────────────────

    private int LessonCount(Branch b)
    {
        var w = _w;
        var h = w.History;
        return b switch
        {
            Branch.Power => h.Scrams + w.Net.Stats.Blackouts,
            Branch.Life => h.Fires + w.Disease.Stats.Infections,
            Branch.Food => (int)(Evolution.StarvedHours(w) / 24f),
            Branch.Medical => 2 * h.Deaths + w.Body.Stats.Falls,
            Branch.Hull => h.Meteors + h.Breaches,
            Branch.Machines => TechWeb.Incidents(w, "break") / 3,
            Branch.Navigation => 2 * w.Voyage.Number + w.Voyage.Index,
            _ => w.Expedition.Stats.Trips + w.Planet.Stats.Landings,
        };
    }

    /// <summary>한 시간마다: 새로 겪은 일 하나가 그 줄기 연구에 보탠다 (지금 하는 연구면 바로, 아니면 쌓아 둔다).</summary>
    private void Lessons()
    {
        var w = _w;
        for (int i = 0; i < 8; i++)
        {
            int n = LessonCount((Branch)i);
            int d = _lessonInit ? n - _lessons[i] : 0;
            _lessons[i] = n;
            if (d <= 0) continue;
            float add = LessonValue * MathF.Min(d, 2);
            Stats.Lessons += d;
            if (ProjectTech is TreeTech pt && (int)pt.Branch == i) Progress += add;
            else Insight[i] = MathF.Min(0.6f * TechTree.EraCost(Reached) * CostScale * SizeMul, Insight[i] + add);
        }
        _lessonInit = true;
    }

    /// <summary>하루마다: 아는 사람이 없는 기술은 날이 센다 — 열흘(기록 사본이 있으면 스무 날)이면 잊는다.</summary>
    private void ForgetCheck()
    {
        var w = _w;
        bool anyone = false;
        foreach (var c in w.Crew) if (!c.Dead && !c.LeftShip && !c.IsChild) { anyone = true; break; }
        if (!anyone) return;
        int grace = ForgetDays * (Known.Contains("vaultcopy") && w.Automation.MainOnline ? 2 : 1);
        foreach (var t in TechTree.All)
        {
            if (!Known.Contains(t.Id)) continue;
            float best = 0f;
            CrewMember? who = null;
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.LeftShip || c.IsChild) continue;
                float s = c.RawSkill(t.Keeper);
                if (s > best) { best = s; who = c; }
            }
            if (best >= KeepSkill) { Orphan.Remove(t.Id); continue; }
            int d = (Orphan.TryGetValue(t.Id, out var od) ? od : 0) + 1;
            Orphan[t.Id] = d;
            if (d == 3) Stats.Warnings++;
            if (d == 3)
                w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.EulReul(t.Name)} 제대로 아는 사람이 없다 ({Skills.Name(t.Keeper)} {best:0.00}" + (who != null ? $" · {who.Name}" : "") + $") — {grace - d}일 안에 익힌 사람이 없으면 잊는다");
            if (d >= grace) Forget(t, $"{Skills.Name(t.Keeper)}을 아는 사람이 모두 떠났다");
        }
    }

    /// <summary>기술을 잊는다: 안에 담긴 옛 기술도 함께 — 이미 지은 설비는 남지만 더 짓지 못한다. 다시 배우면 반값.</summary>
    public void Forget(TreeTech t, string why)
    {
        var w = _w;
        if (!Known.Remove(t.Id)) return;
        foreach (var id in t.Absorbs) Known.Remove(id);
        if (t.Fork is { } f) { Known.Remove(f.a.Id); Known.Remove(f.b.Id); }
        Lost.Add(t.Id);
        Orphan.Remove(t.Id);
        Stats.Forgotten++;
        w.RaiseAlert($"기술을 잊었다 — {t.Name} ({why})", null, AlertLevel.Warning, shipWide: true);
        w.History.Add(w, HistoryKind.Lesson, $"{Ko.EulReul(t.Name)} 잊었다 — {why}. 지어 둔 설비는 남았지만 새로 짓거나 고칠 줄 아는 사람이 없다", null, log: true);
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    /// <summary>시스템 틱: 쌓인 연구가 지금 연구로 들어간다.</summary>
    public void Update()
    {
        var w = _w;
        float gained = MathF.Max(0f, w.Research - _lastResearch);
        _lastResearch = w.Research;
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.TicksPerHour; Lessons(); }
        if (w.Tick >= _nextDay)
        {
            if (_nextDay >= 0) ForgetCheck();
            else SizeMul = Math.Clamp(MathF.Pow(MathF.Max(1, w.Crew.Count(c => !c.Dead && !c.LeftShip)) / 8f, 0.35f), 0.7f, 1.6f); // 첫 날: 배 크기로 값을 정한다
            _nextDay = w.Tick + SimTime.TicksPerDay;
        }
        if (Project != null && Known.Contains(Project)) Project = null;
        if (Project == null) { Choose(); if (Project == null) return; }
        var t = TechTree.Find(Project)!;
        Progress += gained;
        if (Progress >= CostOf(t)) Learn(t);
    }

    public static string Fields(TechField f) => f switch
    {
        TechField.Power => "동력", TechField.Cooling => "냉각", TechField.Life => "생명유지", TechField.Food => "식량", TechField.Hull => "선체",
        TechField.Medical => "의료", TechField.Computing => "컴퓨터", TechField.Robotics => "로봇", TechField.Propulsion => "추진",
        TechField.Sensors => "센서", TechField.Fabrication => "제작", TechField.Habitat => "거주", _ => "방어",
    };

}

/// <summary>새 기술을 익힌 순간 (화면 카드 · 연대기): 바뀐 수치(전 → 후) · 설치할 설비 · 고른 갈래.</summary>
public sealed record TechUnlock(string Id, long Tick, int Era, string[] Lines, int[] Tones, string[] Installs, string Pick, bool Relearn);

public sealed class EraStats
{
    public int Learned, Relearned, Forgotten, EraUps, Starts, Orders, Lessons, StartEra, Warnings;
    public override string ToString() => $"익힘 {Learned} (되찾음 {Relearned}) · 잊음 {Forgotten} · 시대 오름 {EraUps} · 시작 시대 {StartEra} · 함장 지시 {Orders} · 겪은 일 {Lessons}";
}
