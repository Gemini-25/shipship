using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ② 확인할 방법을 고른다 — "모르니까 못 한다"가 아니라 "알아내려고 움직인다".
//  냉각 펌프 유량이 줄면 원인 셋을 함께 의심한다: 유량계가 틀어졌다(SensorCal) · 펌프가 닳았다(마모 · 상태) · 관이 막혔거나 밸브가 덜 열렸다.
//  확인 방법마다 값(분) · 위험 · 무엇을 가려내나가 다르다: 예비 센서 대조(노심 열 수지 — 1분) · 짧은 시험 운전(전류 · 진동 — 4분 · 노심이 달아오르면 보류) ·
//  사람이 가서 보기(순찰 점검 — 오래 걸리지만 확실) · 펌프를 멈춰 영점 보기(위험 — 원자로가 돌면 보류하고 사람에게).
//  가장 적게 들여 가장 많이 가려내는 것부터 (기대 정보 ÷ 값). 오래된 측정은 확신하지 않는다 (마지막으로 잰 지 오래면 결과를 흐리게 읽는다).
//  결론을 내고 손을 쓴 뒤 증상이 남으면 그 진단의 신뢰를 깎는다 (다음엔 그 원인을 덜 서둘러 믿는다).
//  결과는 실제 상태에서 나오지만 판독에는 잡음이 있다 (주 코어 정확도) — 가끔 틀린다.

public sealed class ProbeHypo
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public float P { get; set; }
}

public sealed class ProbeTry
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public long Tick { get; init; }
    public long Until { get; set; }
    public string Result { get; set; } = "";
    public bool Held { get; set; }
    public float Gain { get; init; }
    public float Cost { get; init; }
    public int Crew { get; set; } = -1;
}

public sealed class ProbeCase
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public int Furn { get; init; }
    public int RoomId { get; init; } = -1;
    public string Symptom { get; init; } = "";
    public List<ProbeHypo> H { get; } = new();
    public List<ProbeTry> Tries { get; } = new();
    public ProbeTry? Now { get; set; }
    /// <summary>확인 중 · 손씀 · 맞음 · 틀림 · 모름.</summary>
    public string State { get; set; } = "확인 중";
    public string Conclusion { get; set; } = "";
    public string Truth { get; set; } = "";
    public string Action { get; set; } = "";
    public long VerifyAt { get; set; }
    public long Changed { get; set; }
    public ProbeHypo Lead => H.OrderByDescending(h => h.P).ThenBy(h => h.Key).First();
}

public sealed class ComputerProbe
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 8111 + 1777));
    private int _next = 1;
    private long _tickNext;
    public List<ProbeCase> Cases { get; } = new();
    /// <summary>진단마다 신뢰 (틀리면 깎인다 · 맞으면 조금 오른다) — 사전 확률에 곱한다.</summary>
    public Dictionary<string, float> Trust { get; } = new() { ["meter"] = 1f, ["pump"] = 1f, ["line"] = 1f };
    private readonly HashSet<int> _backup = new();
    private readonly Dictionary<int, long> _quiet = new();
    public int Opened, Checks, Held, Right, Wrong, CrewChecks, StaleReads, Concluded;

    public ComputerProbe(World w) => _w = w;

    public static string Name(string key) => key switch { "meter" => "유량계가 틀어졌다", "pump" => "펌프가 닳았다", "line" => "관이 막혔거나 밸브가 덜 열렸다", _ => key };

    // ───────────── 유량 (실제 · 읽은 값) ─────────────

    private PipeSegment? Branch(Furniture f) => _w.Piping.Built ? _w.Piping.Branches.FirstOrDefault(b => b.Pump == f) : null;
    private static float Intrinsic(Machine m) => m.FaultFactor * (1f - 0.25f * m.Wear * m.Wear) * (0.6f + 0.4f * m.Condition) * (1f - 0.35f * m.Fouled);
    private float LineFactor(Furniture f, Machine m) => (Branch(f)?.Flow ?? 1f) * (m.Line < 0.6f ? MathF.Max(0.3f, m.Line) : 1f) * (_w.Piping.Built ? _w.Piping.CoolantFactor : 1f);

    /// <summary>실제 유량 (기대치 대비 0~1).</summary>
    public float TrueFlow(Furniture f) => f.Machine is Machine m && (m.Powered || m.Spec.PowerDraw <= 0f) ? Math.Clamp(Intrinsic(m) * LineFactor(f, m) * _w.Automation.PumpDrive(f), 0f, 1.5f) : 0f;
    /// <summary>컴퓨터가 읽는 유량: 유량계 교정만큼 틀어진다 (예비 센서로 넘겼으면 실제 값).</summary>
    public float Reading(Furniture f) => TrueFlow(f) * (_backup.Contains(f.Id) || f.Machine == null ? 1f : f.Machine.SensorCal);
    public float Expected(Furniture f) => _w.Automation.PumpDrive(f);
    /// <summary>실제 원인 (가장 큰 몫).</summary>
    public string Truth(Furniture f)
    {
        var m = f.Machine!;
        float gm = 1f - (_backup.Contains(f.Id) ? 1f : m.SensorCal), gp = 1f - Intrinsic(m), gl = 1f - LineFactor(f, m);
        return gm >= gp && gm >= gl ? "meter" : gp >= gl ? "pump" : "line";
    }

    /// <summary>시험 운전이 낮게 나왔다 — 같은 펌프를 다시 의심한다 (계획 쪽에서).</summary>
    public void Suspect(Furniture f, string why) { _quiet.Remove(f.Id); }

    // ───────────── 한 틱 (1분마다) ─────────────

    public void Update()
    {
        var w = _w;
        var a = w.Automation;
        if (FixBook.Off || w.Tick < _tickNext) return;
        _tickNext = w.Tick + SimTime.Minutes(1);
        if (!a.CoreOnline) return;
        // 증상: 고장 표시 없이 유량만 줄었다 (고장은 계획 쪽이 맡는다)
        if (Cases.Count(c => c.State == "확인 중") < 2)
            foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoolantPump))
            {
                var m = f.Machine!;
                if (FixSteps.Broken(m) || !m.Powered || f.Room.Detached || _quiet.TryGetValue(f.Id, out var q) && q > w.Tick) continue;
                if (Cases.Any(c => c.Furn == f.Id && c.State is "확인 중" or "손씀")) continue;
                if (a.Recovery.Plans.Any(p => p.Open && p.TargetId == f.Id && p.Step?.Act is TestRunStep && p.Step.State == FixState.Run)) continue;
                float read = Reading(f), want = Expected(f);
                if (read >= 0.8f * want) continue;
                if (a.Core.Reach(f.Room) == 0) { StaleReads++; continue; } // 못 읽는 값은 증상으로 치지 않는다
                Open(f, read, want);
            }
        foreach (var c in Cases.Where(c => c.State is "확인 중" or "손씀").ToList()) Step(c);
        if (Cases.Count > 30) Cases.RemoveAt(0);
    }

    private void Open(Furniture f, float read, float want)
    {
        var w = _w;
        var a = w.Automation;
        var m = f.Machine!;
        var c = new ProbeCase { Id = _next++, Tick = w.Tick, Furn = f.Id, RoomId = f.Room.Id, Symptom = $"{f.Name} 유량 {read * 100:0}% (기대 {want * 100:0}%)", Changed = w.Tick };
        // 사전 확률: 진단 신뢰 × 이 배에서 자주 틀린 계기 × 정비한 지 얼마나 됐나
        float flaky = a.Review.Values.Flaky(f.Id);
        float since = (w.Tick - m.LastServiced) / (float)SimTime.TicksPerDay;
        c.H.Add(new ProbeHypo { Key = "meter", Name = Name("meter"), P = Trust["meter"] * (1f + 0.5f * flaky) });
        c.H.Add(new ProbeHypo { Key = "pump", Name = Name("pump"), P = Trust["pump"] * (0.8f + 0.1f * MathF.Min(4f, since)) });
        c.H.Add(new ProbeHypo { Key = "line", Name = Name("line"), P = Trust["line"] * 0.9f });
        Norm(c);
        c.Truth = Truth(f);
        Cases.Add(c);
        Opened++;
        a.Book.Add(ActKind.Check, f.Room, c.Symptom, $"원인 셋을 함께 의심한다: {string.Join(" · ", c.H.Select(h => $"{h.Name} {h.P * 100:0}%"))}", "가장 싸게 가려낼 확인부터 한다", "", $"probe:{c.Id}", 0, 60f,
            (world, act) => c.State == "맞음" ? (1, $"맞았다 — {Name(c.Conclusion)}") : c.State == "틀림" ? (-1, $"틀렸다 — 실제로는 {Name(c.Truth)}") : ((int, string)?)null);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {a.Manner.Speak($"{c.Symptom} — 원인을 아직 모릅니다. 확인해 보겠습니다")}");
    }

    private static void Norm(ProbeCase c)
    {
        float s = c.H.Sum(h => h.P);
        foreach (var h in c.H) h.P = s > 0f ? h.P / s : 1f / c.H.Count;
    }

    // 확인 방법: (키, 이름, 값(분), 가설별 "양성" 가능도). 사람이 가서 보기는 따로 (범주로 답한다).
    private static readonly (string key, string name, float cost, float meter, float pump, float line)[] Checks0 =
    {
        ("backup", "예비 센서 대조", 1f, 0.1f, 0.9f, 0.9f), // 양성 = 실제로 열이 덜 빠진다
        ("test", "짧은 시험 운전", 4f, 0.1f, 0.9f, 0.15f), // 양성 = 펌프 전류 · 진동이 이상하다
        ("stop", "펌프를 잠깐 멈춰 영점 보기", 3f, 0.95f, 0.1f, 0.1f), // 양성 = 멈췄는데도 계기가 엉뚱하다 (위험)
    };

    private float Entropy(IEnumerable<float> ps) => -ps.Where(p => p > 1e-5f).Sum(p => p * MathF.Log(p, 2f));

    private float Gain(ProbeCase c, float lm, float lp, float ll, float rel)
    {
        float[] L = { 0.5f + (lm - 0.5f) * rel, 0.5f + (lp - 0.5f) * rel, 0.5f + (ll - 0.5f) * rel };
        float[] P = c.H.Select(h => h.P).ToArray();
        float h0 = Entropy(P);
        float pPos = 0f;
        for (int i = 0; i < 3; i++) pPos += P[i] * L[i];
        float hPos = Entropy(P.Select((p, i) => pPos > 0f ? p * L[i] / pPos : 0f));
        float hNeg = Entropy(P.Select((p, i) => pPos < 1f ? p * (1f - L[i]) / (1f - pPos) : 0f));
        return h0 - (pPos * hPos + (1f - pPos) * hNeg);
    }

    /// <summary>판독 신뢰: 주 코어 정확도 × 마지막으로 잰 지 얼마나 됐나 (오래된 값은 흐리게).</summary>
    private float Rel(Furniture f, string key)
    {
        var a = _w.Automation;
        float acc = 0.75f + 0.25f * Math.Clamp((a.Core.Accuracy - 0.5f) / 0.47f, 0f, 1f);
        var r = key == "backup" ? _w.Power.Reactor : f.Machine;
        float age = r == null ? 99f : (_w.Tick - r.LastReading) / (float)SimTime.Minutes(1);
        float stale = age > 15f ? 0.5f : age > 5f ? 0.8f : 1f;
        if (stale < 1f) StaleReads++;
        if (key == "backup" && _w.Power.Reactor?.SensorCal is float sc) stale *= 0.5f + 0.5f * sc;
        return acc * stale;
    }

    /// <summary>위험한 확인인가 (보류하고 사람이 안전하게).</summary>
    private string? Danger(Furniture f, string key)
    {
        var p = _w.Power;
        if (key == "stop" && p.ReactorOnline) return "원자로가 돌고 있다 — 펌프를 멈추는 확인은 보류";
        if (key == "test" && p.ReactorOnline && p.ReactorTemperature > PowerGrid.OverheatWarnC - 30f) return $"노심 {p.ReactorTemperature:0}℃ — 시험 운전은 보류";
        if (key == "test" && (FixBook.WorkLock(_w, f.Machine!) || _w.Automation.Core.Reach(f.Room) < 2)) return "원격으로 돌릴 수 없다";
        return null;
    }

    private void Step(ProbeCase c)
    {
        var w = _w;
        var a = w.Automation;
        var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == c.Furn);
        if (f?.Machine is not Machine m || f.Room.Detached) { c.State = "모름"; return; }
        if (c.State == "손씀") { Verify(c, f); return; }
        if (FixSteps.Broken(m)) { c.State = "모름"; c.Conclusion = "고장"; c.Action = "고장 표시가 떴다 — 계획 쪽으로 넘긴다"; return; }
        // 진행 중인 확인
        if (c.Now is ProbeTry t)
        {
            if (t.Key == "crew") { if (!CrewDone(c, t, f)) return; }
            else
            {
                if (w.Tick < t.Until) return;
                if (t.Key == "test") a.Recovery.SetDrive(f, 1f);
                var row = Checks0.First(x => x.key == t.Key);
                float rel = Rel(f, t.Key);
                float lm = 0.5f + (row.meter - 0.5f) * rel, lp = 0.5f + (row.pump - 0.5f) * rel, ll = 0.5f + (row.line - 0.5f) * rel;
                float pTrue = c.Truth == "meter" ? lm : c.Truth == "pump" ? lp : ll;
                bool pos = R.Chance(pTrue);
                foreach (var h in c.H) h.P *= pos ? (h.Key == "meter" ? lm : h.Key == "pump" ? lp : ll) : 1f - (h.Key == "meter" ? lm : h.Key == "pump" ? lp : ll);
                Norm(c);
                t.Result = t.Key switch
                {
                    "backup" => pos ? "노심 열이 덜 빠진다 — 실제로 유량이 줄었다" : "노심 열은 그대로 빠진다 — 계기 쪽이 의심스럽다",
                    "test" => pos ? "전류 · 진동이 이상하다 — 펌프 쪽" : "펌프는 멀쩡히 돈다 — 관 쪽",
                    _ => pos ? "멈췄는데도 계기가 엉뚱하다" : "영점은 맞다",
                };
            }
            c.Now = null;
            c.Changed = w.Tick;
            w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {t.Name} — {t.Result} ({string.Join(" · ", c.H.OrderByDescending(h => h.P).Select(h => $"{h.Name} {h.P * 100:0}%"))})");
        }
        // 충분히 가려졌나
        if (c.Lead.P >= (c.Lead.Key == "meter" ? 0.7f : 0.75f)) { Conclude(c, f); return; } // 되돌리기 쉬운 손(예비 센서로 바꿔 읽기)은 조금 덜 확실해도 쓴다
        // 다음 확인 고르기: 기대 정보 ÷ (값 + 위험)
        var done = c.Tries.Select(x => x.Key).ToHashSet();
        string? bestKey = null;
        float best = 0f, bestGain = 0f, bestCost = 0f;
        foreach (var row in Checks0)
        {
            if (done.Contains(row.key)) continue;
            if (Danger(f, row.key) is string why)
            {
                if (!c.Tries.Any(x => x.Key == row.key && x.Held)) { c.Tries.Add(new ProbeTry { Key = row.key, Name = row.name, Tick = w.Tick, Held = true, Result = why }); Held++; }
                continue;
            }
            float g = Gain(c, row.meter, row.pump, row.line, Rel(f, row.key));
            float v = g / (row.cost + 1f);
            if (v > best) { best = v; bestKey = row.key; bestGain = g; bestCost = row.cost; }
        }
        // 사람이 가서 보기: 오래 걸리지만 셋을 다 가린다 (정정해 준 적 있는 사람이면 더 믿는다)
        if (!done.Contains("crew"))
        {
            float crewCost = 25f + (a.Recovery.Plans.Any(p => p.Open && p.Problem == "냉각") ? 10f : 0f); // 걸어가는 시간 + 하던 일을 놓는 값
            float g = Entropy(c.H.Select(h => h.P)) * 0.85f;
            if (g / (crewCost + 1f) > best || bestKey == null) { best = g / (crewCost + 1f); bestKey = "crew"; bestGain = g; bestCost = crewCost; }
        }
        if (bestKey == null || bestGain < 0.02f) { Conclude(c, f); return; }
        StartTry(c, f, bestKey, bestGain, bestCost);
    }

    private void StartTry(ProbeCase c, Furniture f, string key, float gain, float cost)
    {
        var w = _w;
        var a = w.Automation;
        string name = key == "crew" ? "사람이 가서 보기" : Checks0.First(x => x.key == key).name;
        var t = new ProbeTry { Key = key, Name = name, Tick = w.Tick, Until = w.Tick + SimTime.Minutes(cost), Gain = gain, Cost = cost };
        c.Tries.Add(t);
        c.Now = t;
        c.Changed = w.Tick;
        Checks++;
        if (key == "test") { a.Recovery.SetDrive(f, 1.15f); a.Command.Line(CmdTarget.Machine, f.Id, f.Room, $"{f.Name} 짧은 시험 운전", "전류 · 진동을 본다", 0.5f, 5f, state: "하는 중"); }
        if (key == "crew")
        {
            CrewChecks++;
            t.Until = w.Tick + SimTime.Minutes(45);
            var who = FixSteps.Hand(w, f.Machine!, null);
            if (who != null) { t.Crew = who.Id; a.Apps.Messages.Add(new PersonalMessage(w.Tick, who.Id, "부탁", a.Manner.Ask(who, f, c))); }
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {c.Symptom} — {name}로 가려 본다 (값 {cost:0}분)");
    }

    /// <summary>사람이 가서 보기: 순찰 점검 일감이 닫히면 그 사람의 말로 가린다 (정정해 준 사람은 더 믿는다).</summary>
    private bool CrewDone(ProbeCase c, ProbeTry t, Furniture f)
    {
        var w = _w;
        var a = w.Automation;
        var o = w.Board.All.FirstOrDefault(x => !x.Closed && x.Kind == WorkKind.PreventiveCheck && x.Target.Kind == TargetKind.Room && x.Target.Room == f.Room);
        if (o?.Assignee is CrewMember on) t.Crew = on.Id;
        bool came = o == null && w.Tick - t.Tick > SimTime.Minutes(3) && t.Crew >= 0 && w.Crew.FirstOrDefault(x => x.Id == t.Crew) is CrewMember cm0 && cm0.Room == f.Room
                    || o == null && w.Tick - t.Tick > SimTime.Minutes(6);
        if (!came && w.Tick < t.Until) return false;
        var who = w.Crew.FirstOrDefault(x => x.Id == t.Crew);
        if (!came || who == null) { t.Result = "아무도 못 갔다"; c.Tries.Add(new ProbeTry { Key = "crew2", Name = "사람이 가서 보기", Tick = w.Tick, Held = true, Result = "다음에" }); return true; }
        float rel = Math.Clamp(0.7f + 0.25f * who.SkillLevel(Skill.Mechanics) + 0.05f * a.Manner.Corrections(who), 0.6f, 0.97f);
        string said = R.Chance(rel) ? c.Truth : c.H.Where(h => h.Key != c.Truth).OrderBy(h => h.Key).ToList()[R.Range(0, 2)].Key;
        foreach (var h in c.H) h.P *= h.Key == said ? rel : (1f - rel) / 2f;
        Norm(c);
        t.Result = $"{who.Name}: \"{Name(said)}\"";
        // 컴퓨터가 앞세운 원인과 다르게 봤고 그게 맞으면 — 정정으로 기억한다 (⑧)
        if (said == c.Truth) a.Manner.Corrected(who, f.Type, $"{f.Name}: {Name(said)}");
        return true;
    }

    /// <summary>결론 → 손을 쓴다: 유량계면 예비 센서로 넘기고 교정 · 펌프면 정비 계획 · 관이면 사람에게 밸브 · 관을.</summary>
    private void Conclude(ProbeCase c, Furniture f)
    {
        var w = _w;
        var a = w.Automation;
        var lead = c.Lead;
        c.Conclusion = lead.Key;
        c.State = "손씀";
        c.VerifyAt = w.Tick + SimTime.Minutes(lead.Key == "meter" ? 10f : 90f);
        c.Changed = w.Tick;
        Concluded++;
        switch (lead.Key)
        {
            case "meter":
                _backup.Add(f.Id);
                c.Action = "예비 센서 값으로 바꿔 읽고 유량계 교정을 부탁했다";
                a.Review.Values.FlakyNote(f.Id);
                break;
            case "pump":
                c.Action = "펌프 정비를 계획에 넣었다";
                if (f.Machine is Machine m && m.Faults.Count == 0) { c.VerifyAt = w.Tick + SimTime.Minutes(240f); }
                break;
            default:
                c.Action = "밸브 · 관을 손으로 봐 달라 했다";
                break;
        }
        string sure = lead.P >= 0.9f ? "거의 확실" : "아마";
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {a.Manner.Speak($"{c.Symptom} — {sure} {lead.Name} ({lead.P * 100:0}%) · {c.Action}")}");
    }

    /// <summary>손쓴 뒤: 증상이 남으면 그 진단은 틀렸다 — 신뢰를 깎는다.</summary>
    private void Verify(ProbeCase c, Furniture f)
    {
        var w = _w;
        if (w.Tick < c.VerifyAt && !(c.Conclusion == "pump" && f.Machine!.LastServiced > c.Tick)) return;
        float read = Reading(f);
        bool ok = read >= 0.8f * Expected(f) && c.Conclusion == Truth(f) || c.Conclusion == c.Truth && (c.Conclusion != "pump" || f.Machine!.LastServiced > c.Tick);
        if (!ok && c.Conclusion == "pump" && f.Machine!.LastServiced <= c.Tick && w.Tick < c.VerifyAt + SimTime.Hours(6)) return; // 정비를 아직 안 했다 — 더 기다린다
        c.State = c.Conclusion == c.Truth ? "맞음" : "틀림";
        c.Changed = w.Tick;
        if (c.State == "맞음") { Right++; Trust[c.Conclusion] = MathF.Min(1.3f, Trust[c.Conclusion] + 0.05f); }
        else
        {
            Wrong++;
            Trust[c.Conclusion] = MathF.Max(0.4f, Trust[c.Conclusion] - 0.15f);
            _backup.Remove(f.Id);
            w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {f.Name} — {Name(c.Conclusion)}로 봤는데 틀렸습니다. 실제로는 {Name(c.Truth)}. 다음엔 그 판단을 덜 서두르겠습니다");
        }
        if (c.Conclusion == "meter" && f.Machine is Machine m && m.SensorCal >= 0.95f) _backup.Remove(f.Id); // 교정됐으면 다시 제 계기로
        _quiet[f.Id] = w.Tick + SimTime.Minutes(30);
    }

    /// <summary>WorkBoard 훅: 사람이 가서 보기 · 관 의심은 순찰 점검 일감으로.</summary>
    internal void Post(Action<WorkTarget, float, string> post)
    {
        foreach (var c in Cases)
        {
            bool crew = c.State == "확인 중" && c.Now?.Key == "crew" || c.State == "손씀" && c.Conclusion == "line";
            if (!crew || _w.Ship.Rooms.FirstOrDefault(r => r.Id == c.RoomId) is not Room room || room.Detached) continue;
            post(WorkTarget.OfRoom(room), 0.8f, $"주 컴퓨터: {c.Symptom} — 가서 보고 알려 달라");
        }
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Opened); I(Checks); I(Held); I(Right); I(Wrong); I(CrewChecks); I(Concluded); I(_backup.Count);
        foreach (var kv in Trust.OrderBy(k => k.Key)) F(kv.Value);
        foreach (var c in Cases) { I(c.Id); I(c.Tries.Count); I(c.State.Length); }
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v16.26 ② 확인할 방법 — 사람이 가서 보기 (ComputerProbe).</summary>
    private void ScanProbe(Poster post) =>
        _world.Automation.ProbeOrNull?.Post((t, u, d) => post(WorkKind.PreventiveCheck, t, u, Skill.Mechanics, d));
}

public sealed partial class AutomationSystem
{
    private ComputerProbe? _probe;
    /// <summary>v16.26 ② 확인할 방법을 고른다.</summary>
    public ComputerProbe Probe => _probe ??= new ComputerProbe(_world);
    internal ComputerProbe? ProbeOrNull => _probe;
}
