using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ⑧ 성격을 셋으로 (ComputerCharacter — 신중/과감 · 사람/배 — 위에):
//  ① 말투: 위기엔 짧아진다 · 한가할 때 맞히면 가끔 한마디 농담 · 승무원이 부르는 설비 별명을 그 사람에게 쓴다.
//  ② 협업 방식(사람마다): 숙련자에겐 핵심 수치만 · 신입에겐 순서와 이유 · 지친 사람에겐 짧게 · 자주 의심한 사람에겐 근거와 불확실성부터 ·
//     기술자가 설비에 붙인 별명을 알아듣는다 · 사람이 바로잡아 준 것을 기억한다(그 사람 말은 더 믿는다 — ② 확인할 방법).
//  ③ 판단 선호: 여유를 얼마나 남길지(MarginPref — 아슬아슬하면 늘고, 넉넉하면 아주 조금 준다) · 생활 불편을 얼마나 감수할지(ComfortPref —
//     불평이 쌓이면 줄고 효율 문화면 는다). 안전 제한은 선호가 아니다 (FixSteps.SafetyFloorC 아래로 안 내려간다).
//  근거부터 들은 의심 많은 사람은 조금씩 믿게 되고(신뢰 → 부탁을 더 따른다), 지친 사람은 긴 설명에 짜증 내지 않는다.

public sealed class CrewStyle
{
    public int CrewId { get; init; }
    public Dictionary<FurnitureType, string> Nicknames { get; } = new();
    public HashSet<FurnitureType> Understood { get; } = new();
    public List<(long tick, string text)> Corrections { get; } = new();
    public string LastStyle { get; set; } = "";
    public long LastEvidence { get; set; } = -1;
}

public sealed class ComputerManner
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 9001 + 557));
    private readonly Dictionary<int, CrewStyle> _crew = new();
    private long _next;
    private int _grumbles = -1, _scrams = -1;
    /// <summary>여유를 얼마나 남길지 0~1 (0.5에서 시작).</summary>
    public float MarginPref { get; private set; } = 0.5f;
    /// <summary>생활 불편을 얼마나 감수할지 0~1 (1 = 많이 감수).</summary>
    public float ComfortPref { get; private set; } = 0.5f;
    public int Experts, Novices, Tired, Doubters, Nicks, CorrectionsHeard, Jokes;
    public List<(long tick, string text)> Shifts { get; } = new();

    public ComputerManner(World w) => _w = w;

    public CrewStyle Of(CrewMember c) => _crew.TryGetValue(c.Id, out var s) ? s : _crew[c.Id] = new CrewStyle { CrewId = c.Id };
    public int Corrections(CrewMember c) => _crew.TryGetValue(c.Id, out var s) ? s.Corrections.Count : 0;

    // ───────────── ① 말투 ─────────────

    /// <summary>컴퓨터의 말 (끝맺음은 ComputerVoice.Style): 위기엔 짧게.</summary>
    public string Speak(string text)
    {
        var w = _w;
        if (Crisis.Level(w) >= CrisisLevel.Emergency)
        {
            int i = text.IndexOf(" · ", StringComparison.Ordinal);
            if (i > 12) text = text[..i];
        }
        return text;
    }

    /// <summary>한가할 때 맞히면 가끔 한마디 (신뢰가 높을 때만).</summary>
    public string Joke(string about)
    {
        var w = _w;
        if (Crisis.Level(w) >= CrisisLevel.Alert || w.Automation.Trusts.Average() < 0.6f || !R.Chance(0.3f)) return "";
        Jokes++;
        string[] lines = { $"오늘은 {about}가 저보다 고생했습니다", "다들 손이 빨라 제 계산이 머쓱합니다", $"{about}에게 커피라도 한 잔 주고 싶군요" };
        return R.Pick(lines);
    }

    // ───────────── ② 협업 방식 ─────────────

    private static readonly Dictionary<FurnitureType, string[]> NickTable = new()
    {
        [FurnitureType.CoolantPump] = new[] { "늙은 심장", "물레방아", "고집쟁이" },
        [FurnitureType.ReactorCore] = new[] { "난로", "큰 솥" },
        [FurnitureType.OxygenGenerator] = new[] { "허파", "숨통" },
        [FurnitureType.WaterRecycler] = new[] { "콩팥", "정수 영감" },
        [FurnitureType.PowerPanel] = new[] { "두꺼비집", "배전 할매" },
        [FurnitureType.Battery] = new[] { "도시락", "곳간" },
        [FurnitureType.HeatExchanger] = new[] { "라디에이터", "땀샘" },
    };

    /// <summary>그 사람이 그 설비를 부르는 이름 (알아들었으면 별명).</summary>
    public string Nick(CrewMember c, FurnitureType t)
    {
        if (_crew.TryGetValue(c.Id, out var s) && s.Understood.Contains(t) && s.Nicknames.TryGetValue(t, out var n)) return $"'{n}'";
        return FurnitureTypes.Name(t);
    }

    /// <summary>그 사람이 그 설비를 손봤다: 기술자는 설비에 별명을 붙여 부른다 — 컴퓨터가 알아듣는다.</summary>
    public void Learn(CrewMember c, FurnitureType t)
    {
        if (!NickTable.TryGetValue(t, out var names) || c.SkillLevel(Skill.Mechanics) < 0.45f && c.SkillLevel(Skill.Electrical) < 0.45f && c.SkillLevel(Skill.Engineering) < 0.45f) return;
        var s = Of(c);
        if (!s.Nicknames.ContainsKey(t)) s.Nicknames[t] = names[(c.Id * 31 + (int)t) % names.Length];
        if (s.Understood.Add(t))
        {
            Nicks++;
            _w.Log.Add(_w.Tick, LogKind.Ship, $"{_w.Automation.Voice.Call}: {Ko.EunNeun(c.Name)} {Ko.EulReul(FurnitureTypes.Name(t))} '{s.Nicknames[t]}'(이)라 부른다 — 알아듣고, {c.Name}에게는 그렇게 부르겠습니다");
        }
    }

    /// <summary>사람이 바로잡아 줬다 — 기억한다 (다음엔 그 사람 말을 더 믿는다).</summary>
    public void Corrected(CrewMember c, FurnitureType t, string text)
    {
        var s = Of(c);
        s.Corrections.Add((_w.Tick, text));
        if (s.Corrections.Count > 8) s.Corrections.RemoveAt(0);
        CorrectionsHeard++;
        _w.Automation.Trusts.Change(c, 0.02f, "주 컴퓨터가 제 말을 받아들였다", quiet: true);
        _w.Log.Add(_w.Tick, LogKind.Ship, $"{_w.Automation.Voice.Call}: {Ko.IGa(c.Name)} 가서 본 대로 — {text}. 기억해 두겠습니다");
    }

    /// <summary>이 사람에게 맞는 설명 방식.</summary>
    public string StyleFor(CrewMember c, Skill skill)
    {
        var a = _w.Automation;
        if (c.Needs.Rest < 0.3f) return "짧게";
        if (a.Trusts.Of(c) < 0.45f) return "근거부터";
        float sk = c.SkillLevel(skill);
        if (sk >= 0.6f) return "수치만";
        if (sk <= 0.35f) return "순서와 이유";
        return "보통";
    }

    /// <summary>계획의 손 걸음을 사람에게 부탁하는 말 (사람마다 다르게).</summary>
    public string Brief(CrewMember c, Machine? m, FixPlan p, FixStep s)
    {
        var w = _w;
        var a = w.Automation;
        var skill = m?.Spec.Skill ?? Skill.Mechanics;
        string style = StyleFor(c, skill);
        var cs = Of(c);
        cs.LastStyle = style;
        string what = m != null ? Nick(c, m.Body.Type) : "격벽 문";
        string task = m != null ? $"{what} 수리" : "격벽 문을 지렛대로 닫기";
        var fault = m?.Faults.Where(f => f.Kind != FaultKind.BreakerTrip).OrderBy(f => f.OutputFactor).FirstOrDefault();
        string part = fault?.Part is ItemKind pk ? ItemKinds.Name(pk) : "";
        var pw = w.Power;
        string numbers = p.Problem == "냉각" ? $"냉각 {pw.EffectiveCooling:0}kW · 노심 {pw.ReactorTemperature:0}℃ · 출력 {a.ReactorCap * 100:0}%" : m != null ? $"{m.Name} {m.Efficiency * 100:0}%" : "";
        string recall = cs.Corrections.Count > 0 && R.Chance(0.5f) ? $" (지난번 바로잡아 준 것 기억합니다: {cs.Corrections[^1].text})" : "";
        string text;
        switch (style)
        {
            case "짧게": Tired++; text = $"{task}." + (part != "" ? $" {part} 챙겨서." : ""); break;
            case "근거부터":
                Doubters++;
                text = $"근거: {numbers} · 확실하지 않은 것: 걸리는 시간 {s.Min:0}~{s.Max:0}분 → {task}를 부탁합니다";
                if (cs.LastEvidence < 0 || w.Tick - cs.LastEvidence > SimTime.TicksPerDay) { cs.LastEvidence = w.Tick; a.Trusts.Change(c, 0.015f, "주 컴퓨터가 근거부터 보여 줬다", quiet: true); }
                break;
            case "수치만": Experts++; text = $"{task} — {numbers} · {s.Min:0}~{s.Max:0}분"; break;
            case "순서와 이유":
                Novices++;
                text = $"{task}: 1) {(part != "" ? $"창고에서 {Ko.EulReul(part)} 챙긴다" : "공구를 챙긴다")} 2) 전원을 잠그고 손댄다 3) 끝나면 알려 준다 — 시험 운전은 제가 합니다. 까닭: {p.Goal.Split(" — ").Last()} — 늦을수록 배터리를 쓴다";
                break;
            default: text = $"{task}를 부탁합니다 — {p.Goal.Split(" — ").Last()} ({s.Min:0}~{s.Max:0}분)"; break;
        }
        return text + recall;
    }

    /// <summary>가서 봐 달라는 부탁 (확인할 방법 — 사람이 가서 보기).</summary>
    public string Ask(CrewMember c, Furniture f, ProbeCase pc)
    {
        string style = StyleFor(c, Skill.Mechanics);
        string what = Nick(c, f.Type);
        return style switch
        {
            "짧게" => $"{what} 한번 봐 주세요. 유량이 낮습니다.",
            "근거부터" => $"근거: {pc.Symptom} · 원인 후보 {string.Join(" · ", pc.H.OrderByDescending(h => h.P).Select(h => $"{h.Name} {h.P * 100:0}%"))} → 가서 봐 주십시오",
            "수치만" => $"{what}: {pc.Symptom} · {pc.Lead.Name} {pc.Lead.P * 100:0}% — 확인 부탁",
            "순서와 이유" => $"{what}에 가서 1) 계기 숫자 2) 펌프 소리 · 떨림 3) 밸브가 다 열렸는지를 봐 주세요. 까닭: 유량이 줄었는데 원인이 셋 중 하나라서",
            _ => $"{what} 유량이 줄었습니다 — 가서 보고 알려 주세요",
        };
    }

    // ───────────── ③ 판단 선호 ─────────────

    public void NudgeMargin(float d, string why)
    {
        float before = MarginPref;
        MarginPref = Math.Clamp(MarginPref + d, 0f, 1f);
        if (MathF.Abs(MarginPref - before) < 0.005f) return;
        Shifts.Add((_w.Tick, $"여유 {(d > 0 ? "더" : "덜")} — {why}"));
        if (Shifts.Count > 20) Shifts.RemoveAt(0);
        if (d >= 0.05f) _w.Log.Add(_w.Tick, LogKind.Ship, $"{_w.Automation.Voice.Call}: {why} — 다음부터는 여유를 더 남기겠습니다");
    }

    private void NudgeComfort(float d, string why)
    {
        float before = ComfortPref;
        ComfortPref = Math.Clamp(ComfortPref + d, 0f, 1f);
        if (MathF.Abs(ComfortPref - before) < 0.005f) return;
        Shifts.Add((_w.Tick, $"불편 감수 {(d > 0 ? "더" : "덜")} — {why}"));
        if (Shifts.Count > 20) Shifts.RemoveAt(0);
    }

    /// <summary>한 시간마다: 긴급 정지(여유 ↑) · 생활 설비를 끈 데 대한 불평(불편 감수 ↓) · 배 문화.</summary>
    public void Update()
    {
        var w = _w;
        if (FixBook.Off || w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(1);
        var a = w.Automation;
        int scrams = w.History.Scrams;
        if (_scrams >= 0 && scrams > _scrams) NudgeMargin(0.15f, "원자로가 긴급 정지했다");
        _scrams = scrams;
        int g = a.CrewModel.Grumbles;
        if (_grumbles >= 0 && g - _grumbles >= 3) NudgeComfort(-0.05f, $"불평 {g - _grumbles}건 — 생활 설비를 덜 끈다");
        _grumbles = g;
        string cu = w.Meetings.Culture ?? "";
        if (SimTime.HourOfDay(w.Tick) < 1f)
        {
            if (cu.StartsWith("효율")) NudgeComfort(0.02f, "효율을 앞세우는 배");
            else if (cu.StartsWith("사람")) NudgeComfort(-0.02f, "사람을 앞세우는 배");
        }
    }

    public string Line => $"여유 {(MarginPref > 0.65f ? "넉넉히" : MarginPref < 0.35f ? "빠듯하게" : "보통")} · 생활 불편 {(ComfortPref > 0.65f ? "감수" : ComfortPref < 0.35f ? "피함" : "조금")}";

    internal void Hash(Action<long> I, Action<float> F)
    {
        F(MarginPref); F(ComfortPref); I(Experts); I(Novices); I(Tired); I(Doubters); I(Nicks); I(CorrectionsHeard); I(Jokes);
    }
}

public sealed partial class AutomationSystem
{
    private ComputerManner? _manner;
    /// <summary>v16.26 ⑧ 성격 셋 — 말투 · 협업 방식 · 판단 선호.</summary>
    public ComputerManner Manner => _manner ??= new ComputerManner(_world);
}
