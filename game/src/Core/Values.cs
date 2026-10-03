using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.15 가치관 · 결정에 대한 마음 · 딜레마 · 늦게 돌아오는 결과.
//
// 가치관 네 축 (사람마다 −1 … +1): 안전(+) ↔ 효율(−) · 공동체(+) ↔ 개인(−) · 규칙(+) ↔ 융통(−) · 동정(+) ↔ 냉정(−).
//   처음 값: 큰 가치관(CrewValue) · 성격(용기 · 사교 · 성실 · 침착) · 경력 · 습관 · 두려움 · 사람마다 조금씩 다른 결.
//   천천히 바뀐다: 겪은 일(잃은 사람 · 구해 준 사람 · 배고픔 · 딜레마의 결과가 몇 주 뒤 돌아온 일) · 가까운 사람을 닮아 감(하루에 아주 조금).
// 결정에 대한 마음: 선장 · 회의 · 주 컴퓨터가 정한 일(딜레마 · 방침 · 위기 때 지시)을 사람마다 가치관대로 좋아하거나 싫어한다.
//   쌓이면 → 선장 · 컴퓨터 · 회의에 대한 마음 · 배에 대한 충성 · 반발 → 하선 요청 · 불신임 안건(v18.18) · 반란 모의(v18.14) · 카드의 한 줄.
// 딜레마 표(ValueDilemmas.cs) · 정하는 과정(ValueDilemmaRun.cs) · 결정 장부와 늦게 돌아오는 결과(ValueLedger.cs).
// 주 컴퓨터: 딜레마마다 기록과 셈을 내놓고(회의에서는 표 없이 말만) · 급할 때 권한이 있으면 자기 성격(신중 · 사람 우선)대로 정하고 ·
//   반발이 쌓이면 선장에게 귀띔하고 · 장부의 결과가 돌아오면 자기 성격이 바뀐다.

public enum Axis : byte { Safety, Commune, Rule, Mercy }

public sealed class Stand
{
    public long Tick { get; init; }
    public int Verdict { get; init; } = -1;
    /// <summary>정한 쪽: 사람 번호(선장 · 대신 정한 사람) · −1 주 컴퓨터 · −2 회의.</summary>
    public int By { get; init; } = -2;
    public bool Liked { get; init; }
    public float S { get; init; }
    public string Title { get; init; } = "";
    public string Why { get; init; } = "";
}

/// <summary>한 사람의 가치관과 결정에 대한 마음.</summary>
public sealed class Outlook
{
    public int Id { get; init; }
    public float[] V { get; } = new float[4];
    /// <summary>선장 · 주 컴퓨터 · 회의가 정해 온 일에 대한 마음 (−1 … +1).</summary>
    public float Captain { get; set; }
    public float Computer { get; set; }
    public float Council { get; set; }
    /// <summary>이 배에 대한 마음 (0 … 1).</summary>
    public float Loyalty { get; set; } = 0.62f;
    /// <summary>마음에 걸리는 일 (0 … 1) — 정한 쪽에 섰는데 결과가 나쁘게 돌아왔다.</summary>
    public float Conscience { get; set; }
    public string? ConscienceWhy { get; set; }
    public int Likes { get; set; }
    public int Dislikes { get; set; }
    public long LeaveAsked { get; set; } = -1;
    public string? LeaveWhy { get; set; }
    public bool Left { get; set; }
    public long Plotted { get; set; } = -1;
    public long Confided { get; set; } = -1;
    public List<Stand> Recent { get; } = new();
    public List<(long tick, Axis axis, float d, string why)> Shifts { get; } = new();
    public Stand? Last => Recent.Count > 0 ? Recent[^1] : null;
}

public sealed class ValueStats
{
    public int Reactions, Likes, Dislikes, Dilemmas, ByCaptain, ByComputer, ByCouncil, Returns, LeaveAsks, Departed, Confidence, Plots, Shifts, Warnings;
    public string Line() => $"반응 {Reactions}(좋아함 {Likes} · 싫어함 {Dislikes}) · 딜레마 {Dilemmas}(선장 {ByCaptain} · 컴퓨터 {ByComputer} · 회의 {ByCouncil}) · 돌아온 결과 {Returns} · 하선 요청 {LeaveAsks}/{Departed} · 불신임 {Confidence} · 모의 {Plots} · 가치관 변화 {Shifts}";
}

public sealed partial class ValueSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 4513));
    public static bool Off;
    public static long UpdateTicks;

    private readonly SortedDictionary<int, Outlook> _o = new();
    public ValueStats Stats { get; } = new();
    private long _next, _nextSlow, _nextDay;
    private int _policySeen;
    private long _decisionSeen = -1, _motionSeen = -1;
    private int _decisionSeenN, _motionSeenN, _motionCount;

    /// <summary>같은 틱에 여럿이 생겨도 하나도 빠뜨리지 않고 새것만 (틱 순서 · 같은 틱 안에서는 순번).</summary>
    private static bool Fresh(long tick, ref long seen, ref int seenN, ref int atSeen)
    {
        if (tick < seen) return false;
        if (tick == seen) { atSeen++; if (atSeen <= seenN) return false; seenN++; return true; }
        seen = tick; seenN = 1; atSeen = 1;
        return true;
    }

    public ValueSystem(World w) => _w = w;

    private CrewMember? P(int id) => id >= 0 && id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);
    internal static bool Adult(CrewMember c) => !c.Dead && !c.IsChild && !c.Away;

    public static string AxisName(Axis a, bool plus) => a switch
    {
        Axis.Safety => plus ? "안전" : "효율", Axis.Commune => plus ? "공동체" : "개인",
        Axis.Rule => plus ? "규칙" : "융통", _ => plus ? "동정" : "냉정",
    };

    // ───────────────────────────── 가치관 ─────────────────────────────

    public Outlook? Peek(CrewMember c) => _o.TryGetValue(c.Id, out var o) ? o : null;

    public Outlook Of(CrewMember c)
    {
        if (_o.TryGetValue(c.Id, out var o)) return o;
        o = new Outlook { Id = c.Id };
        Seed(c, o);
        _o[c.Id] = o;
        return o;
    }

    /// <summary>처음 값: 큰 가치관 · 성격 · 경력 · 습관 · 두려움 · 사람마다의 결 (따로 굴리는 난수 — 세계 난수를 건드리지 않는다).</summary>
    private void Seed(CrewMember c, Outlook o)
    {
        var r = new Rng(unchecked(_w.Seed * 104729 + c.Id * 7331 + 17));
        var t = c.Traits;
        float s = 0f, k = 0f, u = 0f, m = 0f;
        switch (c.Value)
        {
            case CrewValue.Safety: s += 0.5f; u += 0.1f; break;
            case CrewValue.Efficiency: s -= 0.5f; m -= 0.15f; break;
            case CrewValue.People: m += 0.45f; k += 0.3f; break;
            case CrewValue.Rules: u += 0.55f; k += 0.1f; break;
            default: u -= 0.5f; k -= 0.3f; break;
        }
        s += 0.4f * (0.5f - t.Bravery);
        k += 0.45f * (t.Sociability - 0.5f);
        u += 0.35f * (t.Diligence - 0.5f);
        m -= 0.25f * (t.Calm - 0.5f);
        switch (c.Background)
        {
            case Background.Nurse or Background.Paramedic or Background.MedStudent or Background.Veterinarian or Background.Psychologist: m += 0.3f; break;
            case Background.Monk: m += 0.3f; k += 0.2f; break;
            case Background.Teacher: k += 0.2f; m += 0.1f; break;
            case Background.Soldier or Background.Police: u += 0.3f; m -= 0.15f; break;
            case Background.Lawyer or Background.Accountant: u += 0.3f; k -= 0.1f; break;
            case Background.SafetyInspector or Background.Firefighter: s += 0.3f; u += 0.1f; break;
            case Background.Miner or Background.CargoPilot or Background.TruckDriver: s -= 0.2f; k += 0.1f; break;
            case Background.DroneRacer or Background.Climber or Background.Athlete: s -= 0.3f; k -= 0.1f; break;
            case Background.Artist or Background.Musician or Background.Writer: u -= 0.25f; k -= 0.1f; m += 0.1f; break;
            case Background.Chef or Background.Baker or Background.Gardener or Background.FarmResearcher: k += 0.2f; break;
            case Background.Programmer or Background.SysAdmin or Background.Physicist or Background.Chemist: m -= 0.15f; u += 0.1f; break;
            case Background.Reporter: u -= 0.15f; k += 0.1f; break;
            case Background.Diver: s += 0.2f; break;
        }
        foreach (var h in c.Habits)
            switch (h)
            {
                case Habit.Generous: m += 0.2f; k += 0.15f; break;
                case Habit.Hoarder: k -= 0.25f; break;
                case Habit.Loner: k -= 0.2f; break;
                case Habit.Leader: k += 0.15f; break;
                case Habit.Daredevil: s -= 0.3f; break;
                case Habit.Worrier or Habit.Pessimist: s += 0.2f; break;
                case Habit.Methodical or Habit.Perfectionist: u += 0.2f; break;
                case Habit.Joker or Habit.Prankster: u -= 0.15f; break;
                case Habit.ShortTempered: m -= 0.15f; break;
                case Habit.Patient: m += 0.1f; break;
                case Habit.Cheerful: k += 0.1f; break;
                case Habit.Grumbler: k -= 0.1f; break;
            }
        s += 0.08f * c.Fears.Count;
        o.V[0] = Math.Clamp(s + r.Range(-0.15f, 0.15f), -1f, 1f);
        o.V[1] = Math.Clamp(k + r.Range(-0.15f, 0.15f), -1f, 1f);
        o.V[2] = Math.Clamp(u + r.Range(-0.15f, 0.15f), -1f, 1f);
        o.V[3] = Math.Clamp(m + r.Range(-0.15f, 0.15f), -1f, 1f);
        o.Loyalty = Math.Clamp(0.55f + 0.2f * (t.Diligence - 0.5f) + 0.1f * o.V[1] + r.Range(-0.05f, 0.05f), 0.3f, 0.85f);
    }

    /// <summary>가치관을 민다 (천천히 — 한 번에 많이 움직이지 않는다).</summary>
    public void Shift(CrewMember c, Axis a, float d, string why)
    {
        var o = Of(c);
        float before = o.V[(int)a];
        o.V[(int)a] = Math.Clamp(before + d * (1f - 0.5f * MathF.Abs(before)), -1f, 1f);
        if (MathF.Abs(o.V[(int)a] - before) < 0.01f) return;
        o.Shifts.Add((_w.Tick, a, o.V[(int)a] - before, why));
        if (o.Shifts.Count > 12) o.Shifts.RemoveAt(0);
        Stats.Shifts++;
    }

    /// <summary>이 사람이 이 방향(+ 안전 · 공동체 · 규칙 · 동정)을 얼마나 반기나 (−1 … +1 남짓).</summary>
    public float Lean(CrewMember c, float[] vec)
    {
        var o = Of(c);
        float s = 0f, n = 0f;
        for (int i = 0; i < 4; i++) { s += o.V[i] * vec[i]; n += MathF.Abs(vec[i]); }
        return n <= 0f ? 0f : s / MathF.Max(1f, n * 0.75f);
    }

    /// <summary>가장 크게 걸리는 축과 그 사람 쪽 (말로 옮길 때).</summary>
    public (Axis axis, bool plus, float w) Strongest(CrewMember c, float[] vec)
    {
        var o = Of(c);
        int best = 0; float bw = -1f;
        for (int i = 0; i < 4; i++) { float x = MathF.Abs(o.V[i] * vec[i]); if (x > bw) { bw = x; best = i; } }
        return ((Axis)best, o.V[best] >= 0f, bw);
    }

    /// <summary>사람이 실제로 할 말: 그 사람 쪽 가치관으로 (찬성 · 반대).</summary>
    public static string Voice(Axis a, bool plus) => (a, plus) switch
    {
        (Axis.Safety, true) => "위험을 늘릴 수는 없다",
        (Axis.Safety, false) => "망설이다 더 잃는다",
        (Axis.Commune, true) => "다 같이 사는 배다",
        (Axis.Commune, false) => "각자 제 몫은 제가 지켜야 한다",
        (Axis.Rule, true) => "정해 둔 대로 해야 뒤탈이 없다",
        (Axis.Rule, false) => "규칙보다 지금 사정이 먼저다",
        (Axis.Mercy, true) => "사람을 두고 볼 수는 없다",
        _ => "마음은 아파도 셈은 해야 한다",
    };

    /// <summary>카드에 쓰는 이 사람의 생각 (가장 짙은 두 축).</summary>
    public static string Belief(Axis a, bool plus) => (a, plus) switch
    {
        (Axis.Safety, true) => "위험은 피하고 본다",
        (Axis.Safety, false) => "일이 되게 하는 게 먼저다",
        (Axis.Commune, true) => "배는 다 같이 사는 곳이라 여긴다",
        (Axis.Commune, false) => "제 몫은 제가 챙긴다",
        (Axis.Rule, true) => "정한 규칙은 지켜야 한다고 믿는다",
        (Axis.Rule, false) => "규칙보다 그때그때 사정을 본다",
        (Axis.Mercy, true) => "사람이 먼저라고 믿는다",
        _ => "냉정하게 셈하는 편이다",
    };

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        if (w.Tick < _next) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _next = w.Tick + SimTime.Minutes(10);
        foreach (var c in w.Crew) if (!c.Dead && !_o.ContainsKey(c.Id)) Of(c);
        Watch();
        RunDilemmas();
        if (w.Tick >= _nextSlow)
        {
            _nextSlow = w.Tick + SimTime.Hours(2);
            Consequences();
            Triggers();
            LedgerTick();
        }
        if (w.Tick >= _nextDay)
        {
            _nextDay = w.Tick + SimTime.TicksPerDay;
            Drift();
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>하루에 한 번: 겪은 일과 가까운 사람이 가치관을 아주 조금씩 민다 · 결정에 대한 마음은 조금씩 식는다.</summary>
    private void Drift()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || Peek(c) is not Outlook o) continue;
            o.Captain *= 0.93f; o.Computer *= 0.93f; o.Council *= 0.93f;
            o.Conscience = MathF.Max(0f, o.Conscience - 0.03f);
            o.Loyalty += (0.6f - o.Loyalty) * 0.03f;
            if (c.GriefUntil > w.Tick) Shift(c, Axis.Safety, 0.03f, "가까운 사람을 잃었다");
            if (c.Needs.Food < 0.2f) Shift(c, Axis.Commune, -0.02f, "배가 고프니 제 몫부터 챙기게 된다");
            if (c.Stats.Rescues > 0 && c.Stats.Rescues % 3 == 0) Shift(c, Axis.Commune, 0.02f, "사람을 건져 냈다");
            // 가까운 사람을 닮아 간다
            float wsum = 0f; var pull = new float[4];
            foreach (var kv in c.Affinity)
            {
                if (kv.Value < 0.4f || P(kv.Key) is not CrewMember f || f.Dead || Peek(f) is not Outlook fo) continue;
                for (int i = 0; i < 4; i++) pull[i] += kv.Value * (fo.V[i] - o.V[i]);
                wsum += kv.Value;
            }
            if (wsum > 0f) for (int i = 0; i < 4; i++) o.V[i] = Math.Clamp(o.V[i] + 0.01f * pull[i] / wsum, -1f, 1f);
        }
    }

    // ───────────────────────────── 결정에 대한 반응 ─────────────────────────────

    /// <summary>정한 쪽의 이름 (말로).</summary>
    public string ByName(int by) => by == -1 ? "주 컴퓨터" : by == -2 ? "회의" : P(by) is CrewMember c ? (c.Id == _w.Command.CaptainId ? $"선장 {c.Name}" : c.Name) : "누군가";

    /// <summary>한 결정에 사람마다 반응한다: vec = 고른 쪽이 기우는 방향 · subject = 걸린 사람(+1 그 사람에게 좋은 결정 · −1 나쁜 결정).</summary>
    public void React(int verdict, int by, string title, float[] vec, float weight, int subject = -1, int subjectSide = 0, IReadOnlyCollection<int>? voters = null, Func<CrewMember, float>? extra = null)
    {
        var w = _w;
        var decider = by >= 0 ? P(by) : null;
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || c.Id == by) continue;
            var o = Of(c);
            float s = Lean(c, vec) * weight;
            if (subject >= 0 && subjectSide != 0 && P(subject) is CrewMember sj)
                s += subjectSide * (c.Id == subject ? 0.9f : 0.5f * c.AffinityTo(sj));
            if (extra != null) s += extra(c);
            if (MathF.Abs(s) < 0.12f) continue;
            bool liked = s > 0f;
            var (ax, plus, _) = Strongest(c, vec);
            string why = c.Id == subject ? (liked ? "나를 봐줬다" : "나를 내놓았다") : Voice(ax, plus);
            o.Recent.Add(new Stand { Tick = w.Tick, Verdict = verdict, By = by, Liked = liked, S = s, Title = title, Why = why });
            if (o.Recent.Count > 6) o.Recent.RemoveAt(0);
            Stats.Reactions++;
            if (liked) { o.Likes++; Stats.Likes++; } else { o.Dislikes++; Stats.Dislikes++; }
            float d = Math.Clamp(s, -1f, 1f);
            switch (by)
            {
                case -1:
                    o.Computer = Math.Clamp(o.Computer + 0.3f * d, -1f, 1f);
                    if (w.Automation.Present) w.Automation.Trusts.Change(c, 0.05f * d, liked ? $"{title} — 컴퓨터가 잘 정했다" : $"{title} — 컴퓨터가 사람을 셈으로만 본다", quiet: true);
                    break;
                case -2:
                    o.Council = Math.Clamp(o.Council + 0.2f * d, -1f, 1f);
                    break;
                default:
                    o.Captain = Math.Clamp(o.Captain + 0.3f * d, -1f, 1f);
                    if (decider != null)
                    {
                        c.ChangeAffinity(decider, 0.04f * d);
                        if (!liked && voters != null && voters.Contains(c.Id) && s < -0.35f)
                            w.Relations.Remember(c, decider, RelationReason.IgnoredMyWarning, $"{title} — 내 말을 듣고도 반대로 정했다");
                    }
                    break;
            }
            o.Loyalty = Math.Clamp(o.Loyalty + (liked ? 0.025f : -0.04f) * MathF.Min(1f, MathF.Abs(d) * 1.5f), 0f, 1f);
            if (!liked && s < -0.3f)
            {
                MindSystem.Anger(c, 0.05f + 0.1f * MathF.Min(1f, -s));
                w.Brain2.Emotions.Feel(c, Feeling.Anger, 0.1f * MathF.Min(1f, -s), title, decider);
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
            }
            else if (liked && s > 0.3f) w.Brain2.Emotions.Feel(c, Feeling.Pride, 0.06f, title, decider);
            if (MathF.Abs(s) > 0.4f)
                Life.Diary(w, c, Persona.Say(c, liked ? $"{title} — {Ko.IGa(ByName(by))} 잘 정했다. {why}" : $"{title} — 그렇게 정하면 안 됐다. {why}"));
        }
    }

    /// <summary>다른 곳에서 정한 일(방침 · 위기 때 지시 · 회의 안건)을 읽어 사람마다 반응한다.</summary>
    private void Watch()
    {
        var w = _w;
        // 방침: 바라던 쪽으로 바뀌면 좋아하고, 지키고 싶던 쪽이 바뀌면 싫어한다
        for (int pi = _policySeen; pi < w.Policies.Changes.Count; pi++)
        {
            var ch = w.Policies.Changes[pi];
            _policySeen = pi + 1;
            if (_skipPolicy == ch.Id + ":" + ch.To) { _skipPolicy = ""; continue; }
            if (ch.Why.Contains("관찰자") || ch.Why.Contains("시험")) continue;
            int by = ch.Why.Contains("주 컴퓨터") ? -1 : ch.Yes + ch.No > 0 || ch.Why.Contains("회의") || w.Command.CaptainId < 0 ? -2 : w.Command.CaptainId;
            var spec = PolicySystem.Spec(ch.Id);
            string title = $"'{spec.Options[Math.Clamp(ch.To, 0, spec.Options.Length - 1)]}' 쪽으로 바꾼 {spec.Name} 방침";
            React(-1, by, title, Zero, 0f, extra: c =>
            {
                int pref = PolicySystem.Preferred(c, ch.Id);
                return pref == ch.To ? 0.18f : pref == ch.From ? -0.18f : 0f;
            });
        }
        // 위기 때 지시 (문 닫기 · 버리기 · 배급 · 항로) — 선장 · 지휘자가 정한 것
        int kd = 0;
        foreach (var d in w.Meetings.Decisions)
        {
            if (!Fresh(d.Tick, ref _decisionSeen, ref _decisionSeenN, ref kd)) continue;
            if (!d.Topic.StartsWith("order:") && d.Topic != "cosmic:avoid") continue;
            var vec = OrderVec(d.Topic);
            if (vec == null) continue;
            React(-1, d.Decider >= 0 ? d.Decider : -1, d.Title, vec, 0.6f, voters: d.Yes.Concat(d.No).ToList());
        }
        // 회의 안건: 진 쪽은 회의에 서운하고, 이긴 쪽은 회의를 믿는다 (앙금은 회의 쪽이 따로 남긴다)
        int km = 0, decided = w.Motions.Stats.Passed + w.Motions.Stats.Failed;
        if (decided == _motionCount) return;
        _motionCount = decided;
        foreach (var m in w.Motions.All.Where(x => x.Decided >= 0).OrderBy(x => x.Decided).ThenBy(x => x.Id))
        {
            if (!Fresh(m.Decided, ref _motionSeen, ref _motionSeenN, ref km)) continue;
            foreach (var kv in m.Final)
                if (P(kv.Key) is CrewMember c && !c.Dead && MathF.Abs(kv.Value) > 0.25f)
                {
                    var o = Of(c);
                    bool won = (kv.Value > 0f) == m.Passed;
                    o.Council = Math.Clamp(o.Council + (won ? 0.06f : -0.08f), -1f, 1f);
                }
            // 재판의 벌: 규칙을 중히 여기는 사람은 엄한 벌을, 동정이 깊은 사람은 너그러운 결정을 반긴다
            if (m.Kind == MotionKind.Accusation && P(m.Target) is CrewMember acc && OfMotion(m) == null)
            {
                bool harsh = m.Passed && m.Verdict is Penalty.RationCut or Penalty.Privilege or Penalty.ExtraDuty;
                var vec = harsh ? new[] { 0.2f, 0.2f, 0.9f, -0.7f } : new[] { 0f, 0.2f, -0.6f, 0.8f };
                React(-1, -2, $"{acc.Name} 재판 — {(m.Passed ? MotionSystem.PenaltyName(m.Verdict) : "죄를 묻지 않았다")}", vec, 0.6f, acc.Id, harsh ? -1 : 1);
                if (!harsh && Peek(acc) is Outlook ao) Shift(acc, Axis.Mercy, 0.04f, "용서받았다");
                else if (harsh) Shift(acc, Axis.Rule, -0.04f, "벌을 받았다");
            }
        }
    }

    private string _skipPolicy = "";
    private static readonly float[] Zero = new float[4];

    /// <summary>위기 때 지시가 기우는 쪽 (안전 · 공동체 · 규칙 · 동정).</summary>
    private static float[]? OrderVec(string topic) => topic switch
    {
        "order:SealOffRoom" => new[] { 1f, 0.4f, 0.4f, -0.6f },
        "order:ReopenRoom" or "order:RestoreRoom" or "order:Retrieve" => new[] { -0.6f, 0.2f, 0f, 0.4f },
        "order:Jettison" => new[] { 0.8f, 0.3f, 0.2f, -0.2f },
        "order:Cannibalize" => new[] { -0.4f, 0f, -0.6f, 0f },
        "order:Ration" => new[] { 0.4f, 0.4f, 0.4f, -0.4f },
        "order:AnswerSignal" => new[] { -0.5f, 0.2f, 0f, 1f },
        "order:Distress" => new[] { 0.6f, 0.2f, 0f, 0.3f },
        "order:LimpMain" => new[] { -1f, 0f, -0.3f, 0f },
        "order:ShedLoad" or "order:Brownout" => new[] { 0.5f, 0.3f, 0.2f, -0.3f },
        "order:ChangeCourse" or "cosmic:avoid" => new[] { 0.8f, 0.1f, 0f, 0.2f },
        _ => null,
    };

    // ───────────────────────────── 쌓인 마음의 결과 ─────────────────────────────

    /// <summary>두 시간마다: 쌓인 반발 → 선장에게 직접 말함 · 불신임 안건 · 반란 모의 · 하선 요청 / 컴퓨터가 분위기를 선장에게 귀띔.</summary>
    private void Consequences()
    {
        var w = _w;
        var cap = w.Command.Captain;
        var live = w.Crew.Where(c => Adult(c) && c.CanAct && Peek(c) != null).ToList();
        if (live.Count == 0) return;
        // 불신임: 선장 결정에 크게 실망한 사람이 셋 이상 (또는 둘 + 배 전체 신임이 낮음)
        if (cap != null && !MotionSystem.Off && !w.Motions.ConfidencePending && w.Tick - _confidenceAt > SimTime.TicksPerDay * 3)
        {
            var sore = live.Where(c => c != cap && Of(c).Captain < -0.4f && Of(c).Dislikes >= 2).OrderBy(c => Of(c).Captain).ThenBy(c => c.Id).ToList();
            if (sore.Count >= 3 || sore.Count >= 2 && w.Command.Trust < 0.45f)
            {
                var lead = sore[0];
                var last = Of(lead).Recent.LastOrDefault(r => !r.Liked && r.By == cap.Id);
                string why = last != null ? $"요즘 선장이 정하는 일마다 마음에 안 든다 — {last.Title}" : "요즘 선장이 정하는 일마다 마음에 안 든다";
                var m = w.Motions.Propose(lead, MotionKind.Confidence, SittingKind.Election, $"선장 {cap.Name} 불신임", why, target: cap.Id);
                foreach (var o in sore.Skip(1)) w.Motions.Cosign(m, o.Id);
                _confidenceAt = w.Tick;
                Stats.Confidence++;
            }
        }
        foreach (var c in live)
        {
            var o = Of(c);
            if (cap != null && c != cap)
            {
                // 선장에게 직접 털어놓는다 (사이가 나쁘지 않으면)
                if (o.Captain < -0.35f && o.Confided < 0 && c.AffinityTo(cap) > -0.3f && c.Traits.Sociability > 0.4f)
                {
                    o.Confided = w.Tick;
                    var last = o.Recent.LastOrDefault(r => !r.Liked);
                    w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, $"{cap.Name}에게 털어놓았다 — 요즘 정하시는 일이 마음에 안 든다{(last != null ? $" ({last.Title})" : "")}"), c.Id);
                    Life.Diary(w, cap, $"{Ko.IGa(c.Name)} 내 결정이 마음에 안 든다고 했다. 할 말은 하는 사람이다.");
                    cap.Needs.Stress = MathF.Min(1f, cap.Needs.Stress + 0.03f);
                    // 선장의 가치관이 그 사람 쪽으로 아주 조금 (듣는 선장이면)
                    if (w.Command.Style == CaptainStyle.Consultative && last != null && Peek(c) is Outlook co)
                    {
                        int ax = 0; float bw = 0f;
                        for (int i = 0; i < 4; i++) if (MathF.Abs(co.V[i]) > bw) { bw = MathF.Abs(co.V[i]); ax = i; }
                        Shift(cap, (Axis)ax, 0.04f * MathF.Sign(co.V[ax]), $"{c.Name}의 말을 들었다");
                    }
                }
                // 반란 모의 (v18.14): 크게 실망하고 · 배에 대한 마음이 바닥이고 · 규칙에 덜 묶인 사람
                if (o.Captain < -0.6f && o.Loyalty < 0.3f && o.V[2] < 0.15f && o.Plotted < 0 && SchemeTable.Get("mutiny_plot") is SchemeSpec mp
                    && !w.Schemes.All.Any(s => s.Active && s.Spec.Key == "mutiny_plot"))
                {
                    var allies = live.Where(x => x != c && x != cap && Of(x).Captain < -0.4f && c.AffinityTo(x) > 0.1f).OrderBy(x => Of(x).Captain).ThenBy(x => x.Id).Take(2).ToArray();
                    if (allies.Length > 0)
                    {
                        o.Plotted = w.Tick;
                        w.Schemes.Start(mp, c, cap.Id, allies);
                        Stats.Plots++;
                    }
                }
            }
            // 하선 요청: 배에 대한 마음이 바닥이고 · 제 몫을 먼저 생각하는 사람
            if (o.LeaveAsked < 0 && !o.Left && o.Loyalty < 0.2f && o.V[1] < 0.2f && o.Dislikes >= 3)
            {
                o.LeaveAsked = w.Tick;
                var last = o.Recent.LastOrDefault(r => !r.Liked);
                o.LeaveWhy = last != null ? $"{last.Title} 이후로 이 배에 정이 떨어졌다" : "이 배와는 맞지 않는다";
                Stats.LeaveAsks++;
                w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, $"다음 기항지에서 내리겠다고 했다 — {o.LeaveWhy}"), c.Id);
                Life.Diary(w, c, Persona.Say(c, $"다음 기항지에서 내리겠다고 말했다. {o.LeaveWhy}."));
                w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} 다음 기항지에서 내리겠다고 했다 — {o.LeaveWhy}", c.Room, new[] { c });
                if (cap != null && cap != c) RaiseLeave(c);
            }
        }
        // 주 컴퓨터: 선장 결정에 실망한 사람이 늘면 선장에게 귀띔한다 (사흘에 한 번)
        if (cap != null && w.Automation.Present && w.Automation.CoreOnline && w.Tick - _warnedAt > SimTime.TicksPerDay * 3)
        {
            int sore = live.Count(c => c != cap && Of(c).Captain < -0.3f);
            if (sore >= Math.Max(2, live.Count / 4))
            {
                _warnedAt = w.Tick;
                Stats.Warnings++;
                var a = w.Automation;
                w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {a.Manner.Speak($"{cap.Name} 선장님, 최근 결정에 대해 승무원 {sore}명이 불편한 말을 남겼습니다 — 다음 회의 전에 들어 보시길 권합니다")}");
                if (w.Command.Style == CaptainStyle.Authoritarian) cap.Needs.Stress = MathF.Min(1f, cap.Needs.Stress + 0.04f);
                else _listenUntil = w.Tick + SimTime.TicksPerDay * 2; // 듣는 선장: 다음 결정에서 사람들 말을 더 무겁게 듣는다
            }
        }
    }

    private long _confidenceAt = -1_000_000, _warnedAt = -1_000_000, _listenUntil = -1;
    /// <summary>컴퓨터가 귀띔한 뒤: 선장이 다음 결정에서 사람들 말을 더 듣는다.</summary>
    public bool Listening => _w.Tick < _listenUntil;

    // ───────────────────────────── 카드의 한 줄 ─────────────────────────────

    /// <summary>승무원 카드에 쓰는 한 줄 (그 사람의 말투로 — 수치 없이).</summary>
    public string? CardLine(CrewMember c)
    {
        if (Off || Peek(c) is not Outlook o) return null;
        var w = _w;
        if (o.Left) return null;
        if (o.LeaveAsked >= 0) return $"다음 기항지에서 내리겠다고 했다 — {o.LeaveWhy}";
        bool isCap = c.Id == w.Command.CaptainId;
        if (!isCap && o.Captain < -0.4f) return o.Last is { Liked: false } l && w.Tick - l.Tick < SimTime.TicksPerDay * 3 ? $"요즘 선장 결정이 마음에 안 든다 — {l.Title}" : "요즘 선장 결정이 마음에 안 든다";
        if (o.Computer < -0.4f) return "요즘 컴퓨터가 정하는 일이 못 미덥다";
        if (o.Conscience > 0.25f && o.ConscienceWhy != null) return $"{o.ConscienceWhy} 일이 마음에 걸린다";
        if (o.Last is Stand r && w.Tick - r.Tick < SimTime.TicksPerDay)
            return r.Liked ? $"{r.Title} — 잘 정했다고 생각한다" : $"{r.Title} — 그렇게 정하면 안 됐다고 생각한다";
        if (!isCap && o.Captain > 0.4f) return "요즘 선장이 잘 이끈다고 생각한다";
        if (o.Council < -0.35f) return "회의에서는 늘 저쪽 뜻대로 된다고 느낀다";
        var order = Enumerable.Range(0, 4).OrderByDescending(i => MathF.Abs(o.V[i])).ToList();
        string a = Belief((Axis)order[0], o.V[order[0]] >= 0f);
        return MathF.Abs(o.V[order[1]]) > 0.35f ? $"{a} · {Belief((Axis)order[1], o.V[order[1]] >= 0f)}" : a;
    }

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var kv in _o)
        {
            var o = kv.Value;
            I(kv.Key); for (int i = 0; i < 4; i++) F(o.V[i]);
            F(o.Captain); F(o.Computer); F(o.Council); F(o.Loyalty); F(o.Conscience); I(o.Likes); I(o.Dislikes); I(o.LeaveAsked); I(o.Left ? 1 : 0);
        }
        I(Dilemmas.Count); foreach (var d in Dilemmas) { I(d.Id); I(d.Stage); I(d.ChoseA is bool b ? (b ? 1 : 2) : 0); I(d.By); }
        I(Ledger.Count); foreach (var v in Ledger) { I(v.Id); I(v.ReturnAt); I(v.Returned ? 1 : 0); }
        I(R.Draws);
    }
}
