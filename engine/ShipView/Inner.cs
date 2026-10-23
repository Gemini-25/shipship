using System.Runtime.CompilerServices;
using System.Text.Json;
using ShipEngine;

namespace ShipView;

/// <summary>
/// 속말 첫판 (docs/MIND.md 4단 — 화면만 · 세계의 값을 읽기만): 그 사람 말투의 머릿속 한 줄.
/// 처지 차례: 위기 (도망 · 공황 · 숨 아낌 · 불 · 구조 · 막기 · 대피) → 막힘 (30분 안 · 그 목표) → 부탁받음 → 욕구 · 일 · 쉼 → 기본.
/// 말투 넷은 성격에서 (수다: 외향 0.65 넘게 · 살가움: 다정 0.62 · 외향 0.45 넘게 · 무뚝뚝: 다정 0.38 밑 또는 내향에 덜 다정 · 그 밖 차분) ·
/// 줄 고르기는 상태 없는 셈 난수 (사람 · 처지 · 시각 한 시간 — 엔진이 쥔 난수 흐름을 건드리지 않는다) · 글 표는 engine/data/thoughts.json.
/// </summary>
public static class Inner
{
    private sealed class Table
    {
        public Dictionary<string, Dictionary<string, string[]>> Situations { get; set; } = new();
        public Dictionary<string, string> Shouts { get; set; } = new();
        public string RobotShout { get; set; } = "";
        public Dictionary<string, string> GoalWords { get; set; } = new();
    }

    private static Table? _t;
    private static Table T => _t ??= JsonSerializer.Deserialize<Table>(DataFiles.Text("thoughts.json"), DataFiles.Json)!;

    /// <summary>표의 모든 속말 줄 (금지어 시험이 자리를 채워 본다).</summary>
    public static IEnumerable<(string situation, string style, string line)> AllLines()
    {
        foreach (var (k, v) in T.Situations) foreach (var (st, lines) in v) foreach (var l in lines) yield return (k, st, l);
    }

    /// <summary>외침 글 (믿음 갈래 → '불이야!' · 로봇은 한 가지).</summary>
    public static string Shout(BeliefKind k, bool robot) => robot ? T.RobotShout : T.Shouts.TryGetValue(k.ToString(), out var s) ? s : "";

    /// <summary>로봇 칩 낱말 (2~3글자).</summary>
    public static string GoalWord(GoalKind g) => T.GoalWords.TryGetValue(g.ToString(), out var s) ? s : "일";

    public static string Style(in AgentCore c) =>
        c.Extra >= 0.65f ? "수다" : c.Agree >= 0.62f && c.Extra >= 0.45f ? "살가움" : c.Agree <= 0.38f || c.Extra <= 0.35f && c.Agree < 0.5f ? "무뚝뚝" : "차분";

    private static readonly ConditionalWeakTable<Mind, Dictionary<int, (long key, string text)>> Held = new();

    /// <summary>속말 한 줄 (자는 이 · 의식 없는 이는 부르는 쪽이 괄호 글을 쓴다).</summary>
    public static string Of(Sim w, Mind m, Agent a)
    {
        ref var c = ref a.C;
        long t = w.Tick;
        var (sit, why) = Situation(w, m, a, t);
        string style = Style(c);
        long hour = t / Time.Hours(1);
        long key = HashCode.Combine(sit, why, (int)c.Goal, c.StepIndex, c.GoalTarget, style, hour, (int)c.AssignedGoal);
        var held = Held.GetOrCreateValue(m);
        if (held.TryGetValue(a.Id, out var h) && h.key == key) return h.text;
        string text = Compose(w, m, a, sit, why, style, hour);
        held[a.Id] = (key, text);
        return text;
    }

    private static (string sit, string why) Situation(Sim w, Mind m, Agent a, long t)
    {
        ref var c = ref a.C;
        if (c.Mode == 1) return ("도망", "");
        if (c.Mode == 2) return ("공황", "");
        if (c.Mode == 3) return ("숨아낌", "");
        switch (c.Goal)
        {
            case GoalKind.FightFire: return ("불", "");
            case GoalKind.Rescue or GoalKind.Cpr: return ("구조", "");
            case GoalKind.Patch: return ("막기", "");
            case GoalKind.Flee or GoalKind.Evacuate: return ("도망", "");
            case GoalKind.Panic: return ("공황", "");
            case GoalKind.Shelter: return ("대피", "");
        }
        // 막힘: 30분 안 · 그 목표에서 (지금 목표이거나 막힌 뒤 막 바꾼 목표) — 판단 일지의 까닭 글이 있으면 그것 (샤워 칸 물 없음 같은 글)
        string why = "";
        if (a.Judged is { Count: > 0 } jr)
        {
            var j = jr.Newest(0);
            if (j.Out is JudgeOut.Blocked or JudgeOut.Turned && t - j.Tick < Time.Minutes(30)) why = j.Note is { Length: > 0 } n ? TextLint.Clean(n) : Mind.SnagText(j.Why);
        }
        if (why.Length == 0 && c.Snag != SnagKind.None && t - c.SnagTick < Time.Minutes(30) && (c.SnagGoal == c.Goal || t - c.GoalSince < Time.Minutes(5)))
            why = Mind.SnagText(c.Snag);
        if (why.Length > 0) return ("막힘", why);
        if (c.AssignedGoal == c.Goal && c.AssignedUntil > t && c.Goal != GoalKind.Idle) return ("부탁받음", "");
        return (c.Goal switch
        {
            GoalKind.Drink => "마심", GoalKind.Eat or GoalKind.Cook => "먹음", GoalKind.Sleep => "잠", GoalKind.Work => "일", GoalKind.Relax => "쉼",
            GoalKind.Socialize => "어울림", GoalKind.Toilet => "화장실", GoalKind.Wash => "씻기", GoalKind.Exercise => "운동", _ => "기본",
        }, "");
    }

    private static string Compose(Sim w, Mind m, Agent a, string sit, string why, string style, long hour)
    {
        if (!T.Situations.TryGetValue(sit, out var bySt) || !bySt.TryGetValue(style, out var lines) || lines.Length == 0) return Mind.GoalName(a.C.Goal);
        string place = Place(w, a);
        string job = Mind.GoalName(a.C.Goal);
        string hobby = a.C.Goal == GoalKind.Relax ? Mind.HobbyName((Hobby)a.C.Hobby) : "";
        // 채울 수 없는 자리가 든 줄은 빼고 고른다
        var ok = lines.Where(l => (place.Length > 0 || !l.Contains("{곳")) && (hobby.Length > 0 || !l.Contains("{취미}")) && (job.Length > 0 || !l.Contains("{일}"))).ToArray();
        if (ok.Length == 0) return job.Length > 0 ? job : "…";
        int si = Array.IndexOf(T.Situations.Keys.ToArray(), sit);
        double u = Rng.Unit(w.Seed, Rng.Key("속말"), a.Id, si * 24 + hour);
        string line = ok[Math.Min(ok.Length - 1, (int)(u * ok.Length))];
        return line.Replace("{곳으로}", Ko.Euro(place)).Replace("{곳에}", place + "에").Replace("{곳}", place)
            .Replace("{일}", job).Replace("{까닭}", why).Replace("{취미}", hobby);
    }

    /// <summary>가는 곳의 방 이름 (계획의 마지막 가기 걸음 · 없으면 목표 자리 — 통로 · 이름 없는 방이면 빈 글).</summary>
    private static string Place(Sim w, Agent a)
    {
        var L = w.Layout;
        int cell = -1;
        for (int i = a.Plan.Count - 1; i >= 0; i--) if (a.Plan[i].Kind == StepKind.GoTo && a.Plan[i].Cell >= 0) { cell = a.Plan[i].Cell; break; }
        if (cell < 0) cell = a.C.GoalCell;
        if (cell < 0 || cell >= L.N) return "";
        int r = L.Room[cell];
        if (r < 0 || L.Rooms[r].Type == "Corridor") return "";
        string n = L.Rooms[r].Name;
        return n.Length > 0 && !n.Any(char.IsDigit) ? n : "";
    }
}
