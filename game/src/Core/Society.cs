using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v13.4 사회: 배 전체 사기 · 일과표(근무 방침) · 베테랑 · 실수 숨기기 · 야간 당직 · 규칙 위반의 벌 · 새 승무원 수습 · 방침 2차의 생활 쪽 연결.
//
// 사기 0~1: 스트레스 평균 · 최근 죽음 · 먹을 것 · 선장 신뢰 · 파벌의 골 · 여가 · 넘긴 사고 — 하루에 절반쯤 따라간다.
//   높으면 손이 빠르고 덜 다투고 덜 겁먹고 실수를 털어놓는다, 낮으면 반대.
// 일과표: 방침 '근무'가 3교대·2교대면 잠자는 시간을 배 전체로 맞추고, 각자 일과면 사람마다 (밤새는 사람·아침형은 두 시간씩).
// 베테랑: 사고 대응을 열두 번 넘게 겪으면 — 덜 겁먹고(공황), 말에 무게가 실리고(회의), 비상 일이 빠르다.
// 실수 숨기기: 덜 조인 실수를 혼자 알게 되면 털어놓거나 숨긴다 — 규칙을 앞세우면 털어놓고, 벌이 무겁거나 선장이 권위형이거나
//   사기가 낮거나 화가 나 있으면 숨긴다. 숨긴 실수는 나중에 고장으로 드러나고, 조사에서 걸리면 비난과 벌.
// 야간 당직: 밤(0~6시)에 깨어 있는 당직이 배를 돈다 — 데이터선이 끊긴 방의 불도 눈으로 찾는다.
// 규칙 위반: 명령 무시·숨긴 실수가 드러나면 방침대로 경고 · 근무 박탈(여덟 시간) · 넘어간다.
// 새 승무원: 수습 기간이면 사흘 동안 회의에서 표가 없고 위험한 조에 넣지 않는다.

public sealed class SocietySystem
{
    private readonly World _w;
    private readonly Rng _rng;
    public SocietySystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 7333 + 71));
    }

    // ── 사기 ──
    public float Morale { get; private set; } = 0.65f;
    public string MoraleWhy { get; private set; } = "";
    /// <summary>두 시간마다 사기 (최근 일주일).</summary>
    public List<float> MoraleHistory { get; } = new();
    private long _nextSample;
    private int _band = 1;
    public int Veterans, Hidden, Confessed, Revealed, Suspensions, Warnings, Patrols;

    public static string MoraleName(float m) => m >= 0.75f ? "높다" : m >= 0.5f ? "보통" : m >= 0.3f ? "가라앉았다" : "바닥이다";

    /// <summary>사기가 손에 미치는 몫 (0.92 ~ 1.08).</summary>
    public float WorkFactor => 0.92f + 0.16f * Morale;

    private readonly Dictionary<int, long> _suspended = new();
    private readonly Dictionary<int, long> _joined = new();
    private readonly HashSet<int> _veteran = new();
    private readonly Dictionary<int, float> _baseBedtime = new();
    private int _shiftMode = -1, _shiftCrew = -1;

    /// <summary>근무 박탈 중 (규칙 위반의 벌 — 급한 일 말고는 하지 않는다).</summary>
    public bool Suspended(CrewMember c) => _suspended.TryGetValue(c.Id, out var t) && _w.Tick < t;
    /// <summary>수습 중 (회의 표 없음 · 위험한 조에 넣지 않는다).</summary>
    public bool OnProbation(CrewMember c) => _w.Policies["newcrew"] == 1 && _joined.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.TicksPerDay * 3;
    public bool IsVeteran(CrewMember c) => _veteran.Contains(c.Id);
    /// <summary>v14.4 새로 탄 지 사흘이 안 됐다 (구조 · 기항지 · 시험).</summary>
    public bool Recent(CrewMember c) => _joined.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.TicksPerDay * 3;
    public void MarkJoined(CrewMember c) => _joined[c.Id] = _w.Tick;

    public void Update(float dt)
    {
        var w = _w;
        // 새로 탄 사람 (구조 · 기항지 · 태어남)
        // (처음 갱신 때 배에 있던 사람은 처음부터 탄 사람 — 게임 시계는 07:00부터라 "한 시간 안"으로는 못 가린다: v14.4 고침)
        bool founding = _joined.Count == 0;
        foreach (var c in w.Crew)
            if (!_joined.ContainsKey(c.Id)) _joined[c.Id] = founding ? -SimTime.TicksPerDay * 10 : w.Tick;
        Shifts();
        Veterans_();
        MoraleUpdate(dt);
        Leisure(dt);
    }

    // ── 일과표 ──

    private void Shifts()
    {
        var w = _w;
        int mode = w.Policies["shifts"];
        foreach (var c in w.Crew) if (!_baseBedtime.ContainsKey(c.Id)) _baseBedtime[c.Id] = c.Schedule.SleepStart;
        int alive = w.Crew.Count(c => !c.Dead);
        if (mode == _shiftMode && alive == _shiftCrew) return; // 방침이나 사람 수가 바뀔 때만 다시 짠다
        bool changed = _shiftMode >= 0 && mode != _shiftMode;
        _shiftMode = mode;
        _shiftCrew = alive;
        foreach (var c in w.Crew.Where(c => !c.Dead))
        {
            float bed = Bedtime(c, mode);
            if (MathF.Abs(c.Schedule.SleepStart - bed) > 0.01f) c.Schedule = Schedule.FromBedtime(bed);
        }
        if (changed)
            w.History.Add(w, HistoryKind.Decision, $"일과표를 다시 짰다 — {w.Policies.Option("shifts")}", null, log: true);
    }

    private float Bedtime(CrewMember c, int mode)
    {
        var adults = _w.Crew.Where(x => !x.Dead && !x.IsChild).OrderBy(x => x.Id).ToList();
        int rank = Math.Max(0, adults.IndexOf(c));
        float base_ = _baseBedtime.TryGetValue(c.Id, out var b) ? b : c.Schedule.SleepStart;
        if (c.IsChild || mode == 2) return c.IsChild && mode != 2 ? 21f : c.IsChild ? base_ : SimTime.Wrap(base_ + Persona.Add(c, h => h.BedShift)); // v14.0 올빼미·아침형
        return mode switch
        {
            0 => new[] { 23f, 7f, 15f }[rank % 3],
            1 => new[] { 23f, 11f }[rank % 2],
            _ => base_,
        };
    }

    // ── 베테랑 ──

    private void Veterans_()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild || _veteran.Contains(c.Id) || c.Stats.Emergencies < 12) continue;
            _veteran.Add(c.Id);
            Veterans++;
            w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} 베테랑이 되었다 — 사고 대응 {c.Stats.Emergencies}번 (덜 겁먹고, 말에 무게가 실리고, 비상 일이 빠르다)", c.Room, new[] { c }, log: true);
            Life.Diary(w, c, "이제 웬만한 일에는 손이 먼저 나간다.");
        }
    }

    // ── 사기 ──

    private void MoraleUpdate(float dt)
    {
        var w = _w;
        var alive = w.Crew.Where(c => !c.Dead && !c.IsChild).ToList();
        if (alive.Count == 0) return;
        var terms = new List<(float v, string why)>();
        float stress = alive.Average(c => c.Needs.Stress);
        terms.Add((-0.45f * stress, "모두 지쳐 있다"));
        int recent = w.Crew.Count(c => c.Dead && w.Tick - c.DiedAt < SimTime.TicksPerDay * 3);
        if (recent > 0) terms.Add((-0.12f * MathF.Min(3, recent), $"사흘 사이 {recent}명을 잃었다"));
        if (w.Food.Rationing) terms.Add((-0.08f, "배급 중"));
        if (w.Policies["water"] == 2) terms.Add((-0.04f, "물이 엄격하다"));
        terms.Add((0.15f * (w.Command.Trust - 0.5f), w.Command.Trust >= 0.5f ? "선장을 믿는다" : "선장을 못 믿는다"));
        float rift = w.Meetings.Rifts().Select(r => r.tension).DefaultIfEmpty(0f).Max();
        if (rift > 0.1f) terms.Add((-0.15f * rift, "파벌 사이가 벌어졌다"));
        int lp = w.Policies["leisure"];
        if (lp != 1) terms.Add((lp == 2 ? 0.05f : -0.05f, lp == 2 ? "쉴 때는 쉰다" : "일 먼저"));
        if (w.Policies["privacy"] == 1) terms.Add((0.03f, "제 공간이 있다"));
        if (w.Meetings.Praises > 0) terms.Add((MathF.Min(0.08f, 0.02f * w.Meetings.Praises), "사람을 잃지 않고 넘긴 사고"));
        float target = Math.Clamp(0.78f + terms.Sum(t => t.v), 0f, 1f);
        Morale += (target - Morale) * MathF.Min(1f, dt / 24f * 0.7f);
        var worst = terms.Where(t => t.v < 0f).OrderBy(t => t.v).FirstOrDefault();
        var best = terms.Where(t => t.v > 0f).OrderByDescending(t => t.v).FirstOrDefault();
        MoraleWhy = Morale < 0.5f ? worst.why ?? "" : best.why ?? "";
        if (w.Tick >= _nextSample)
        {
            _nextSample = w.Tick + SimTime.Hours(2);
            MoraleHistory.Add(Morale);
            if (MoraleHistory.Count > 84) MoraleHistory.RemoveAt(0);
        }
        int band = Morale >= 0.75f ? 2 : Morale >= 0.4f ? 1 : 0;
        if (band != _band)
        {
            if (band < _band || band == 2)
                w.History.Add(w, HistoryKind.Decision, $"배의 사기가 {MoraleName(Morale)} ({Morale * 100:0}%) — {MoraleWhy}", log: true);
            _band = band;
        }
    }

    private void Leisure(float dt)
    {
        var w = _w;
        int lp = w.Policies["leisure"];
        bool privacy = w.Policies["privacy"] == 1;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake) continue;
            if (lp == 0) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.002f * dt);
            if (privacy) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.0015f * dt);
        }
    }

    /// <summary>휴식·여가 방침이 쉬는 일의 점수에 곱하는 몫.</summary>
    public float LeisureFactor => _w.Policies["leisure"] switch { 0 => 0.7f, 2 => 1.3f, _ => 1f };

    // ── 야간 당직 ──

    /// <summary>지금 야간 당직인가 (밤 0~6시, 방침 1명·2명 — 깨어 있는 사람 중 근무 시간인 사람부터).</summary>
    public bool OnNightWatch(CrewMember c)
    {
        var w = _w;
        int n = w.Policies["nightwatch"] switch { 0 => 1, 1 => 2, _ => 0 };
        if (n == 0 || c.IsChild || !c.CanAct) return false;
        float hour = SimTime.HourOfDay(w.Tick);
        if (hour >= 6f) return false;
        var watch = w.Crew.Where(x => x.CanAct && !x.IsChild && !SimTime.InWindow(hour, x.Schedule.SleepStart, x.Schedule.SleepLength))
            .OrderByDescending(x => ChoresActivity.OnShiftStatic(x, w)).ThenBy(x => x.Id).Take(n);
        return watch.Contains(c);
    }

    // ── 실수 숨기기 ──

    /// <summary>덜 조인 실수를 털어놓을지 숨길지 (true = 숨긴다).</summary>
    public bool WillHide(CrewMember c)
    {
        var w = _w;
        float p = 0.25f;
        p *= c.Value switch { CrewValue.Rules => 0.4f, CrewValue.Safety => 0.7f, CrewValue.Freedom => 1.3f, _ => 1f };
        p *= w.Policies["violations"] switch { 1 => 1.6f, 2 => 0.6f, _ => 1f };
        if (w.Command.Style == CaptainStyle.Authoritarian) p *= 1.4f;
        p *= 1f + c.Mind.Anger;
        p *= 1.4f - 0.8f * Morale;
        p *= 1f - 0.4f * c.Traits.Diligence;
        if (c.Fears.Contains(Fear.Failure)) p *= 1.5f; // v14.0 실패가 두려운 사람은 더 숨긴다
        return _rng.Chance(MathF.Min(0.85f, p));
    }

    public void Confess(CrewMember c, string what)
    {
        var w = _w;
        Confessed++;
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
        w.Log.Add(w.Tick, LogKind.Work, $"실수를 털어놓았다 — {what} · 다시 손봤다", c.Id);
        Life.Diary(w, c, $"{what}. 말하고 다시 손봤다.");
        Punish(c, $"실수를 털어놓았다 ({what})", light: true);
    }

    public void Hide(CrewMember c, string what)
    {
        Hidden++;
        Life.Diary(_w, c, $"{what}. 아무에게도 말하지 않았다.");
    }

    /// <summary>숨긴 실수가 고장으로 드러났다 — 조사에서 걸리면 비난과 벌.</summary>
    public void Surfaced(CrewMember? c, Machine m, string why)
    {
        var w = _w;
        if (c == null || c.Dead) return;
        float find = 0.35f + (w.Automation.Level >= 4 ? 0.25f : 0f) + (w.Automation.MainOnline ? 0.1f : 0f);
        if (!_rng.Chance(find)) return;
        Revealed++;
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.12f);
        MindSystem.Anger(c, 0.15f);
        foreach (var o in w.Crew.Where(o => !o.Dead && o != c)) o.ChangeAffinity(c, -0.05f);
        w.History.Add(w, HistoryKind.Decision, $"숨긴 실수가 드러났다 — {Ko.IGa(c.Name)} {Ko.EulReul(m.Name)} 손보다 덜 조이고 말하지 않았다 ({why})", m.Body.Room, new[] { c }, log: true);
        bool covered = w.Relations.OnMistakeRevealed(c); // v14.4 가까운 사람이 감싸 주면 벌이 가볍다
        Punish(c, "숨긴 실수", light: covered);
    }

    /// <summary>규칙 위반의 벌 (방침): 경고 · 근무 박탈 · 넘어간다. 털어놓은 실수는 가볍게.</summary>
    public void Punish(CrewMember c, string what, bool light)
    {
        var w = _w;
        int v = w.Policies["violations"];
        if (v == 2 || light && v == 0) return;
        if (v == 0 || light)
        {
            Warnings++;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f);
            w.Log.Add(w.Tick, LogKind.Life, $"경고를 받았다 — {what}", c.Id);
            return;
        }
        Suspensions++;
        _suspended[c.Id] = w.Tick + SimTime.Hours(8);
        MindSystem.Anger(c, 0.25f);
        w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(c.Name)} 여덟 시간 근무에서 빠졌다 — {what} (방침: 규칙 위반은 근무 박탈)", c.Room, new[] { c }, log: true);
    }
}

/// <summary>v13.4 야간 당직: 밤에 배를 한 바퀴 돈다 (몇 방을 들러 살핀다).</summary>
public sealed class PatrolActivity : Activity
{
    public override string Id => "patrol";
    public override string Label => "야간 순찰";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist) =>
        w.Society.OnNightWatch(c) && !c.Outside ? (0.62f + 0.1f * c.Traits.Diligence, "야간 당직 — 배를 돈다") : (0f, "—");

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var rooms = w.Ship.LiveRooms.Where(r => !r.OffLimits && !r.Abandoned && r.Type != RoomType.Corridor && r.Cells.Any(dist.Reachable)).ToList();
        if (rooms.Count == 0) return null;
        var toils = new List<Toil>();
        int start = (int)(w.Tick / SimTime.Minutes(30) + c.Id) % rooms.Count;
        for (int k = 0; k < Math.Min(3, rooms.Count); k++)
        {
            var r = rooms[(start + k * 3) % rooms.Count];
            var spot = r.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x)).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell s) continue;
            toils.Add(new GotoToil(s));
            toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Standing, r.Center));
        }
        if (toils.Count == 0) return null;
        w.Society.Patrols++;
        return new Job(this, "야간 순찰", toils) { LogText = "야간 당직 — 배를 한 바퀴 돈다", LogKind = LogKind.Life, InterruptMargin = 0.2f };
    }
}
