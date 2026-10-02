using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ⑨ 지휘 탭이 읽는 판 — 지금 계획을 사람이 읽는 말로 (Core에서 만들고 화면은 그리기만 · 시험도 이걸 읽는다).
//  현재 목표 · 확인된 사실(센서 · 승무원 보고 · 계산을 구분) · 불확실한 것(몇 분 전 값) · 진행 중 계획 단계 · 다음 실행 조건 ·
//  예상과 여유(범위) · 대체 계획 · 최근 수정 이유 · 예약 · 끊긴 구역 · 자기 상태.
//  설비 하나: 왜 꺼져 있나 / 언제 켜지나 / 무엇을 기다리나.

public sealed class ReadLine
{
    public string Text { get; init; } = "";
    /// <summary>센서 · 보고 · 예측 · 계산 (모양으로 구분해 그린다).</summary>
    public string Kind { get; init; } = "센서";
    /// <summary>몇 분 전 값 (−1 = 지금).</summary>
    public float Age { get; init; } = -1f;
}

public sealed class PlanReadout
{
    public FixPlan? Plan;
    public string Goal = "";
    public List<ReadLine> Facts { get; } = new();
    public List<ReadLine> Unknowns { get; } = new();
    public List<ReadLine> Forecasts { get; } = new();
    public string Next = "";
    public string Expect = "";
    public string Fallback = "";
    public string Revised = "";
    public long RevisedAt = -1;
    public string Reserve = "";
    public string Zones = "";
    public string Self = "";
    public string Review = "";
    public string Manner = "";
    public int Open;
}

public static class ComputerReadout
{
    public static PlanReadout Read(World w)
    {
        var a = w.Automation;
        var r = new PlanReadout();
        var book = a.RecoveryOrNull;
        var p = book?.Lead;
        r.Plan = p;
        r.Open = book?.Plans.Count(x => x.Open) ?? 0;
        var pw = w.Power;
        long now = w.Tick;
        float Age(long t) => (now - t) / (float)SimTime.Minutes(1);
        if (p != null)
        {
            r.Goal = p.Goal + (r.Open > 1 ? $" (그 밖 {r.Open - 1}건)" : "");
            var s = p.Step;
            r.Next = s == null ? "끝을 확인하는 중" : s.Waiting != "" ? $"{s.Name}: {s.Waiting}" : s.State == FixState.Run ? $"{s.Name} 하는 중 — {s.Note}" : $"{Ko.EulReul(s.Name)} 시작할 차례";
            float left = p.Steps.Skip(p.Cur).Sum(x => x.Min) - (s?.Took(now) ?? 0f), leftMax = p.Steps.Skip(p.Cur).Sum(x => x.Max) - (s?.Took(now) ?? 0f);
            r.Expect = $"남은 시간 {MathF.Max(0f, left):0}~{MathF.Max(1f, leftMax):0}분" + (p.Problem == "냉각" && p.PeakMax > 0f ? $" · 노심 최고 {p.PeakMin:0}~{p.PeakMax:0}℃ (긴급 정지 {FixSteps.ScramC(w):0}℃ — 여유 {FixSteps.ScramC(w) - p.PeakMax:0}℃)" : "");
            r.Fallback = p.Fallback;
            if (p.Revisions.Count > 0) { r.Revised = p.Revisions[^1].why; r.RevisedAt = p.Revisions[^1].tick; }
            if (p.Problem == "냉각")
            {
                r.Facts.Add(new ReadLine { Text = $"냉각 {pw.EffectiveCooling:0}kW · 원자로 출력 {pw.ReactorOutput:0}kW", Kind = "센서" });
                r.Facts.Add(new ReadLine { Text = $"노심 {pw.ReactorTemperature:0}℃", Kind = "센서", Age = pw.Reactor != null ? Age(pw.Reactor.LastReading) : -1f });
                if (w.Ship.Furniture.FirstOrDefault(f => f.Id == p.TargetId)?.Machine is Machine pm)
                    r.Facts.Add(new ReadLine { Text = pm.Faults.Count > 0 ? $"{pm.Body.Name}: {string.Join(" · ", pm.Faults.Select(f => f.Name))}" : $"{pm.Body.Name}: 고장 표시 없음 (유량 {a.Probe.Reading(pm.Body) * 100:0}%)", Kind = "센서", Age = Age(pm.LastReading) });
            }
            foreach (var st in p.Steps.Where(x => x.Act.Kind == FixKind.Hands && x.Crew >= 0 && x.State != FixState.Wait))
                if (w.Crew.FirstOrDefault(c => c.Id == st.Crew) is CrewMember c)
                    r.Facts.Add(new ReadLine { Text = $"{c.Name}: {(c.Job?.Order?.Id == st.Order ? "맡아서 하는 중" : st.State == FixState.Done ? "끝냈다" : "아직 안 왔다")}", Kind = "보고", Age = st.Start >= 0 ? Age(st.Start) : -1f });
            var range = p.Steps.Skip(p.Cur).FirstOrDefault(x => x.Max - x.Min >= 10f);
            if (range != null) r.Unknowns.Add(new ReadLine { Text = $"{range.Name}에 걸릴 시간 {range.Min:0}~{range.Max:0}분 (사람 · 부품에 달렸다)", Kind = "계산" });
            if (p.PeakMax > 0f) r.Forecasts.Add(new ReadLine { Text = $"노심 최고 {p.PeakMin:0}~{p.PeakMax:0}℃", Kind = "예측" });
        }
        else r.Goal = "평시 — 지켜보는 중";
        // 확인할 방법 (유량 등)
        if (a.ProbeOrNull is ComputerProbe pr)
            foreach (var c in pr.Cases.Where(c => c.State == "확인 중").Take(2))
            {
                r.Unknowns.Add(new ReadLine { Text = $"{c.Symptom} — {string.Join(" · ", c.H.OrderByDescending(h => h.P).Select(h => $"{h.Name} {h.P * 100:0}%"))}", Kind = "계산" });
                if (c.Now != null) r.Facts.Add(new ReadLine { Text = $"확인 중: {c.Now.Name}", Kind = c.Now.Key == "crew" ? "보고" : "센서", Age = Age(c.Now.Tick) });
                foreach (var t in c.Tries.Where(t => t.Result != "" && !t.Held).TakeLast(2)) r.Facts.Add(new ReadLine { Text = $"{t.Name}: {t.Result}", Kind = t.Key == "crew" ? "보고" : "센서", Age = Age(t.Tick) });
                foreach (var t in c.Tries.Where(t => t.Held).Take(1)) r.Unknowns.Add(new ReadLine { Text = $"보류: {t.Name} — {t.Result}", Kind = "계산" });
            }
        // 못 보는 방 · 오래된 값
        foreach (var z in a.Zones.Alone.Take(3))
            if (w.Ship.Rooms.FirstOrDefault(x => x.Id == z.RoomId) is Room zr)
                r.Unknowns.Add(new ReadLine { Text = $"{zr.Name}: 소식 없음 — 마지막 방침({z.Policy})대로 버틴다고 본다", Kind = "계산", Age = Age(z.Since) });
        // 배터리 예측 (믿는 용량)
        float drain = -pw.BatteryFlow;
        if (drain > 0.3f)
        {
            float mins = a.Review.BatteryMinutes(drain);
            r.Forecasts.Add(new ReadLine { Text = $"배터리 {pw.BatteryPercent * 100:0}% · {drain:0.#}kW씩 — 바닥까지 {mins:0}분" + (a.Review.Values.BatterySamples > 0 ? $" (이 배에서 잰 용량 {a.Review.Values.BatteryFactor * 100:0}%)" : " (이름표 용량)"), Kind = "예측" });
        }
        r.Reserve = a.ReserveOrNull?.Line ?? "";
        r.Zones = a.Zones.Reports.Count > 0 && now - a.Zones.Reports[^1].tick < SimTime.Hours(2) ? a.Zones.Reports[^1].text : "";
        var self = a.SelfOrNull;
        r.Self = self == null ? "" : self.Status != "정상" ? self.Status : self.Notes.Count > 0 && now - self.Notes[^1].tick < SimTime.Hours(1) ? self.Notes[^1].text : "";
        var rv = a.Review.Reviews.LastOrDefault();
        r.Review = rv == null ? "" : $"{rv.Title.Split(" — ")[0]}: {string.Join(" · ", rv.Lines.Take(2))}";
        r.Manner = $"{a.Manner.Line} · {a.Character.Temper}/{a.Character.Tilt}";
        return r;
    }

    /// <summary>설비 하나: 왜 꺼져 있나 / 언제 켜지나 / 무엇을 기다리나.</summary>
    public static (string why, string when, string wait) WhyOff(World w, Furniture f)
    {
        var a = w.Automation;
        var m = f.Machine;
        if (m == null) return ("설비가 아니다", "—", "—");
        var plan = a.RecoveryOrNull?.Plans.LastOrDefault(p => p.Open && p.TargetId == f.Id);
        var step = plan?.Step;
        string? planWhen = plan == null ? null : $"계획대로면 {plan.Steps.Skip(plan.Cur).Sum(x => x.Min):0}~{plan.Steps.Skip(plan.Cur).Sum(x => x.Max):0}분 뒤";
        if (FixSteps.Broken(m))
            return ($"고장 — {string.Join(" · ", m.Faults.Select(x => x.Name))}", planWhen ?? "수리가 끝나면", step != null ? $"{step.Name}: {(step.Waiting != "" ? step.Waiting : step.Note)}" : "수리할 사람");
        if (m.LockedOut) return ("정비 절차로 전원을 잠갔다 (사람이 작업 중)", "작업이 끝나면 — 그 전엔 원격으로 켜지 않는다", "작업자가 잠금을 푸는 것");
        if (m.Parked) return ($"전력 몰아주기로 꺼 둠 ({PowerTriage.TierName(PowerTriage.Rank(w, m))})", "원자로가 넉넉하고 배터리가 절반을 넘으면 순위대로", $"여유 전력 {m.Spec.PowerDraw * Grades.Power(m.Grade):0.#}kW");
        if (!m.Powered) return ($"전기가 없다 ({PowerGrid.CircuitName(f.Room.Circuit)} 회로)", "회로가 살아나면", f.Room.BreakerOff ? "분전함을 올리는 것" : "차단기 · 배전");
        float d = a.PumpDrive(f);
        if (MathF.Abs(d - 1f) > 0.01f) return ($"계획에 따라 세기 {d * 100:0}%", planWhen ?? "계획이 끝나면 100%로", step?.Name ?? "—");
        if (a.ReserveOrNull?.Holds(m) == true) return ("돌고 있다 — 예약해 두었다 (끄지 않는다)", "—", a.Reserve.Now.FirstOrDefault(x => x.Kind == "전력")?.Why ?? "—");
        return (m.Active ? $"돌고 있다 ({m.Efficiency * 100:0}%)" : "대기 중", "—", "—");
    }
}
