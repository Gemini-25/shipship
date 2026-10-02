using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ⑦ 사고 뒤 자기 판단 검토 — 예상 · 선택 · 실제를 맞대 보고 "이 배의 실제 값"을 고친다.
//  배터리 실제 용량(이름표가 아니라 — 낡은 배터리는 덜 든다) · 사람별 작업 시간 · 문이 닫히는 시간 · 임시로 고친 부품이 버틴 시간 ·
//  자주 틀리는 계기 · 잘 통한 수순과 실패한 수순.
//  운 좋게 성공한 위험한 선택은 정답으로 배우지 않는다 (위험했던 계획의 성공은 "운"으로 따로 센다 — 선호를 바꾸지 않는다).
//  안전 제한(FixSteps.SafetyFloorC)은 그대로 두고, 값과 선호만 조금씩.
//  사고 검토는 계획이 끝날 때 한 번 · 배터리는 방전이 이어질 때만 (긴 간격 · 가볍게). 달아오르면(⑥) 검토를 미뤘다가 식으면 한다.

public sealed class ShipValues
{
    /// <summary>배터리 실제 용량 ÷ 이름표 용량 (처음엔 이름표를 믿는다).</summary>
    public float BatteryFactor { get; set; } = 1f;
    public int BatterySamples { get; set; }
    /// <summary>격벽 문이 닫히는 데 걸리는 시간 (초).</summary>
    public float DoorCloseSec { get; set; } = 30f;
    public int DoorSamples { get; set; }
    /// <summary>사람별 작업 속도 (실제 ÷ 예상 — 1보다 크면 더 걸린다).</summary>
    public Dictionary<int, float> Pace { get; } = new();
    public Dictionary<int, int> PaceSamples { get; } = new();
    /// <summary>설비 종류별 고친 뒤 다시 멎기까지 (분) — 임시 부품 수명.</summary>
    public Dictionary<FurnitureType, float> RefailMinutes { get; } = new();
    /// <summary>자주 틀리는 계기 (설비 Id → 틀린 횟수).</summary>
    public Dictionary<int, int> FlakySensors { get; } = new();
    /// <summary>수순별 결과: 문제:수순 → (잘 통함 · 실패 · 운 좋음).</summary>
    public Dictionary<string, (int ok, int fail, int lucky)> Sequences { get; } = new();

    public float PaceOf(int crewId) => Pace.TryGetValue(crewId, out var p) ? p : 1f;
    public float Flaky(int furnId) => FlakySensors.TryGetValue(furnId, out var n) ? MathF.Min(1f, 0.25f * n) : 0f;
    public void FlakyNote(int furnId) => FlakySensors[furnId] = FlakySensors.GetValueOrDefault(furnId) + 1;
}

public sealed class AfterReview
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public int PlanId { get; init; }
    public string Title { get; init; } = "";
    public bool Lucky { get; init; }
    public bool Miss { get; init; }
    public List<string> Lines { get; } = new();
}

public sealed class ComputerReview
{
    private readonly World _w;
    private int _next = 1;
    private long _tickNext;
    public ShipValues Values { get; } = new();
    public List<AfterReview> Reviews { get; } = new();
    private readonly List<FixPlan> _pending = new();
    private readonly Dictionary<int, float> _peak = new();
    public int Lucky, Misses, Learned, PlanReviews;
    /// <summary>예측이 크게 빗나간 횟수 (연속) — 셋이면 ⑥이 비교를 뗀다.</summary>
    public int MissStreak { get; set; }
    // 배터리 방전 추적: 시작 % · 시작 틱 · 실제로 나간 에너지 · 그때 예측한 분
    private float _bStart = -1f, _bOut, _bPred, _bDrain;
    private long _bTick, _bQuiet;
    public float LastPredMin { get; private set; } = -1f;
    public float LastActualMin { get; private set; } = -1f;
    public string BatteryLine { get; private set; } = "";

    public ComputerReview(World w) => _w = w;

    // ───────────── 배터리: 컴퓨터가 믿는 용량 ─────────────

    /// <summary>이름표 용량 (kWh).</summary>
    public float Nameplate() => _w.Ship.FurnitureOf(FurnitureType.Battery).Where(f => !f.Machine!.Parked).Sum(PowerGrid.BatteryKwh);
    /// <summary>컴퓨터가 믿는 남은 kWh · 전체 (계기 % × 이름표 × 배운 비율).</summary>
    public (float kwh, float cap) BatteryBelief()
    {
        float cap = Nameplate() * Values.BatteryFactor;
        return (_w.Power.BatteryPercent * cap, cap);
    }
    /// <summary>이 소모량이면 배터리가 바닥까지 몇 분 (믿는 용량으로).</summary>
    public float BatteryMinutes(float drainKw) => drainKw <= 0.05f ? float.PositiveInfinity : BatteryBelief().kwh / drainKw * 60f;

    private void TrackBattery()
    {
        var w = _w;
        var p = w.Power;
        float drain = -p.BatteryFlow;
        if (_bStart < 0f)
        {
            if (drain > 0.5f && p.BatteryPercent > 0.25f && Nameplate() > 0f)
            {
                _bStart = p.BatteryPercent; _bTick = w.Tick; _bOut = 0f; _bDrain = drain; _bQuiet = 0;
                _bPred = 0.15f * Nameplate() * Values.BatteryFactor / drain * 60f; // 15%p 떨어지는 데 몇 분
                BatteryLine = $"배터리 {p.BatteryPercent * 100:0}% · {drain:0.#}kW씩 — 15%p 떨어지는 데 {_bPred:0}분으로 본다";
            }
            return;
        }
        _bOut += MathF.Max(0f, drain) / 60f;
        if (drain < 0.2f) { if (++_bQuiet > 10) _bStart = -1f; return; }
        _bQuiet = 0;
        if (w.Tick - _bTick > SimTime.Hours(8)) { _bStart = -1f; return; }
        if (p.BatteryPercent > _bStart - 0.15f) return;
        // 다 떨어졌다: 실제 에너지 ÷ 떨어진 % = 실제 용량
        float actualMin = (w.Tick - _bTick) / (float)SimTime.Minutes(1);
        float drop = _bStart - p.BatteryPercent;
        float implied = _bOut / MathF.Max(0.01f, drop);
        float ratio = Math.Clamp(implied / MathF.Max(1f, Nameplate()), 0.08f, 1.3f);
        float before = Values.BatteryFactor;
        // 같은 소모량으로 고쳐 본 예측 (평균 소모 기준)
        float avgDrain = _bOut / MathF.Max(0.01f, actualMin / 60f);
        float predAvg = 0.15f * Nameplate() * before / MathF.Max(0.05f, avgDrain) * 60f;
        Values.BatteryFactor = Math.Clamp(Values.BatterySamples == 0 ? ratio : before * 0.4f + ratio * 0.6f, 0.08f, 1.2f); // 처음 잰 값은 그대로 믿는다
        Values.BatterySamples++;
        LastPredMin = predAvg;
        LastActualMin = actualMin;
        Learned++;
        bool miss = MathF.Abs(actualMin - predAvg) > 0.2f * predAvg;
        BatteryLine = $"배터리 {predAvg:0}분 예상 → {actualMin:0}분" + (miss ? $" — {(ratio < 0.9f ? "낡은 배터리" : "배터리")} 실제 용량 반영 (이름표의 {Values.BatteryFactor * 100:0}%)" : " — 예상대로");
        if (miss) w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {BatteryLine}");
        Add(new AfterReview { Id = _next++, Tick = w.Tick, Title = "배터리 예측", Miss = miss }, BatteryLine);
        _bStart = -1f;
    }

    // ───────────── 계획이 끝났다 ─────────────

    /// <summary>FixBook.Finish 훅.</summary>
    public void Plan(FixPlan p)
    {
        if (_w.Automation.SelfWatch.Thin("검토")) { _pending.Add(p); return; } // 달아오르면 미룬다
        Review(p);
    }

    private void Review(FixPlan p)
    {
        var w = _w;
        var a = w.Automation;
        PlanReviews++;
        float took = p.Elapsed(w.Tick);
        float watch = p.Steps.Where(s => s.Act is WatchStep && s.State == FixState.Done).Sum(s => s.Took(w.Tick));
        float active = MathF.Max(1f, took - watch);
        float lo = p.Min - p.Steps.Where(s => s.Act is WatchStep).Sum(s => s.Min), hi = p.Max - p.Steps.Where(s => s.Act is WatchStep).Sum(s => s.Max);
        bool miss = p.State == "성공" && (active > hi * 1.5f + 10f || active < lo * 0.4f);
        if (miss) { Misses++; MissStreak++; } else if (p.State == "성공") MissStreak = 0;
        float peak = _peak.TryGetValue(p.Id, out var pk) ? pk : 0f;
        _peak.Remove(p.Id);
        bool risky = p.Risk >= 0.3f;
        bool lucky = p.State == "성공" && risky;
        string key = $"{p.Problem}:{p.OptionKey}";
        var (ok, fail, lk) = Values.Sequences.GetValueOrDefault(key);
        if (lucky) { Lucky++; lk++; }
        else if (p.State == "성공") ok++;
        else fail++;
        Values.Sequences[key] = (ok, fail, lk);
        var r = new AfterReview { Id = _next++, Tick = w.Tick, PlanId = p.Id, Title = p.Goal, Lucky = lucky, Miss = miss };
        r.Lines.Add($"예상 {lo:0}~{hi:0}분 → 실제 {active:0}분" + (miss ? " — 크게 빗나갔다" : ""));
        r.Lines.Add($"고른 수순: {p.Name} (견준 것 {p.Compared.Count}가지{(p.Replans > 0 ? $" · 고쳐 짬 {p.Replans}번" : "")})");
        if (p.Problem == "냉각" && peak > 0f) r.Lines.Add($"노심 최고: 예상 {p.PeakMin:0}~{p.PeakMax:0}℃ → 실제 {peak:0}℃");
        foreach (var s in p.Steps.Where(s => s.State == FixState.Failed && s.Note != "")) r.Lines.Add($"{s.Name}: {s.Note}");
        if (lucky) r.Lines.Add($"운이 좋았다 — 위험이 컸던 수순({p.Name})이 통했을 뿐이다. 정답으로 배우지 않는다");
        else if (p.State == "성공" && ok >= 3 && fail == 0) r.Lines.Add($"{p.Name} — 이 배에서 {ok}번 안정적으로 통했다");
        if (p.State != "성공") r.Lines.Add($"이 수순은 {fail}번 실패 — 다음엔 덜 고른다");
        // 여유 선호 (⑧ 판단 선호): 아슬아슬했으면 여유를 더 남긴다 — 안전 제한은 건드리지 않는다
        if (p.Problem == "냉각" && peak > 0f && peak > FixSteps.ScramC(w) - FixSteps.SafetyFloorC - 8f) { a.Manner.NudgeMargin(+0.1f, $"{p.Goal}에서 노심이 {peak:0}℃까지 올랐다"); r.Lines.Add("아슬아슬했다 — 다음엔 여유를 더 남긴다"); }
        else if (p.State == "성공" && !risky && p.Problem == "냉각" && peak > 0f && peak < FixSteps.ScramC(w) - 60f) a.Manner.NudgeMargin(-0.02f, "여유가 넉넉히 남았다");
        Add(r, null);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call} 돌아봄: {p.Goal.Split(" — ")[0]} — {string.Join(" · ", r.Lines.Take(3))}");
        if (lucky) w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터가 돌아봤다 — {Ko.EunNeun(p.Name)} 위험했는데 운이 좋았다. 다음에도 그렇게 하지는 않겠다고 적었다", w.Ship.Rooms.FirstOrDefault(x => x.Id == p.RoomId));
    }

    /// <summary>수순 선호 (점수에 더한다 — 작을수록 좋다): 여러 번 안정적으로 통한 것만 조금 앞세우고, 실패한 것은 뒤로. 운 좋은 성공은 세지 않는다.</summary>
    public float Prefer(string problem, string key)
    {
        if (!Values.Sequences.TryGetValue($"{problem}:{key}", out var s)) return 0f;
        float bonus = s.ok >= 3 ? -0.3f * MathF.Min(1f, (s.ok - 2f * s.fail) / 5f) : 0f;
        return bonus + 0.6f * s.fail / (s.ok + s.fail + 1f);
    }

    /// <summary>걸음이 실패했다 (어떤 걸음이 무엇 때문에).</summary>
    public void StepFailed(FixPlan p, FixStep s) { }

    /// <summary>사람이 고쳤다: 예상 대비 걸린 시간 → 그 사람의 작업 속도.</summary>
    public void Work(CrewMember c, Machine m, float took, float min, float max)
    {
        float mid = MathF.Max(5f, (min + max) * 0.5f);
        float ratio = Math.Clamp(took / mid, 0.4f, 2.5f);
        float before = Values.PaceOf(c.Id);
        int n = Values.PaceSamples.GetValueOrDefault(c.Id);
        Values.Pace[c.Id] = Math.Clamp(before * (n == 0 ? 0.4f : 0.6f) + before * ratio * (n == 0 ? 0.6f : 0.4f), 0.5f, 2.5f);
        Values.PaceSamples[c.Id] = n + 1;
        Learned++;
        _w.Automation.Manner.Learn(c, m.Body.Type);
    }

    /// <summary>고친 설비가 다시 멎었다 (임시 부품 수명).</summary>
    public void Refail(Machine m, float minutes)
    {
        float before = Values.RefailMinutes.TryGetValue(m.Body.Type, out var v) ? v : minutes;
        Values.RefailMinutes[m.Body.Type] = before * 0.5f + minutes * 0.5f;
        Learned++;
    }

    /// <summary>격벽 문이 닫히는 데 걸린 시간 (초).</summary>
    public void DoorClosed(float sec)
    {
        if (sec <= 0f || sec > 600f) return;
        Values.DoorCloseSec = Values.DoorSamples == 0 ? sec : Values.DoorCloseSec * 0.7f + sec * 0.3f;
        Values.DoorSamples++;
    }

    private void Add(AfterReview r, string? line)
    {
        if (line != null) r.Lines.Add(line);
        Reviews.Add(r);
        if (Reviews.Count > 30) Reviews.RemoveAt(0);
    }

    /// <summary>1분마다: 배터리 방전 추적 · 열린 냉각 계획의 노심 최고 온도 · 미뤄 둔 검토.</summary>
    public void Update()
    {
        var w = _w;
        if (FixBook.Off || w.Tick < _tickNext) return;
        _tickNext = w.Tick + SimTime.Minutes(1);
        TrackBattery();
        if (w.Automation.RecoveryOrNull is FixBook fb)
            foreach (var p in fb.Plans)
                if (p.Open && p.Problem == "냉각") _peak[p.Id] = MathF.Max(_peak.GetValueOrDefault(p.Id), w.Power.ReactorTemperature);
        if (_pending.Count > 0 && !w.Automation.SelfWatch.Thin("검토")) { foreach (var p in _pending) Review(p); _pending.Clear(); }
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        F(Values.BatteryFactor); I(Values.BatterySamples); F(Values.DoorCloseSec); I(Lucky); I(Misses); I(Learned); I(PlanReviews); I(MissStreak);
        foreach (var kv in Values.Pace.OrderBy(k => k.Key)) F(kv.Value);
        foreach (var kv in Values.Sequences.OrderBy(k => k.Key, StringComparer.Ordinal)) { I(kv.Value.ok); I(kv.Value.fail); I(kv.Value.lucky); }
    }
}

public sealed partial class AutomationSystem
{
    private ComputerReview? _review;
    /// <summary>v16.26 ⑦ 사고 뒤 자기 판단 검토 · 이 배의 실제 값.</summary>
    public ComputerReview Review => _review ??= new ComputerReview(_world);
}
