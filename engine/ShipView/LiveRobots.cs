using ShipEngine;

namespace ShipView;

/// <summary>로봇 칩 하나: 사람 번호 · 칩 글 (R1 · D1 — 칩에만 영문) · 하는 일 낱말 (2~3글자) · 형편 (0 멀쩡 · 1 전지 적음 · 상함 · 2 못 움직임 · 전지 바닥 · 3 멈춤) · 풀이 글.</summary>
public readonly record struct RobotChip(int Id, string Tag, string Word, int Level, string Tip);

/// <summary>말 창 한 줄: 글 · 무게 (0 보통 · 1 맡김 · 막힘 · 2 봉쇄 · 막힌 곳) · 흐림.</summary>
public readonly record struct TalkLine(string Text, int Level, float Alpha);

/// <summary>
/// 로봇 칩 · 로봇 카드 · 주컴퓨터 말 창 (docs/MIND.md 4단 — 화면만 · 판단 일지와 주컴퓨터 일지를 읽기만).
/// 로봇 카드: '하는 일 ▸ 방법' · 맡은 까닭 (주컴퓨터가 맡겼으면 그 까닭) · 최근 판단 셋.
/// 말 창: 주컴퓨터가 한 말을 세 줄까지 · 한 줄은 화면 2초 넘게 (꽉 차면 0.6초에 한 줄씩 민다) · 밀린 줄은 여섯까지 (가벼운 것 · 오래된 것부터 버린다) · 12초 동안 새 말이 없으면 접는다.
/// </summary>
public sealed partial class LiveShow
{
    public const int TalkRows = 3, TalkQueue = 6;
    public const double TalkHold = 2.0, TalkStep = 0.6, TalkFold = 12.0;
    private readonly List<(string text, int level)> _talkWait = new();
    private readonly List<(string text, int level, double age)> _talkShown = new();
    private long _noteSeen; private double _talkIdle; private Sim? _talkWorld;

    /// <summary>칩: 로봇 · 드론 (번호 차례).</summary>
    public RobotChip[] RobotBar()
    {
        var w = World; var mind = w.Find<Mind>(); var net = w.Find<Network>();
        if (mind == null) return Array.Empty<RobotChip>();
        var o = new List<RobotChip>();
        foreach (var a in mind.Agents)
        {
            if (a.IsPerson) continue;
            string tag = (a.C.Kind == AgentKind.Drone ? "D" : "R") + (a.C.Name + 1);
            float battery = a.C.Battery, legs = 0, core = 0; bool hands = true;
            if (net != null && a.C.Mech >= 0 && a.C.Mech < net.Robots.Count) { var rb = net.Robots[a.C.Mech]; battery = rb.Battery; legs = rb.Legs; core = rb.Core; hands = Network.RobotHands(rb); }
            int level = a.C.Up == 0 || core >= 1 ? 3 : legs >= 1 || battery < 0.15f ? 2 : battery < 0.35f || !hands || legs > 0.3f || a.C.Damage > 0.3f ? 1 : 0;
            string word = level == 3 ? "멈춤" : Waiting(a, battery) ? "대기" : Inner.GoalWord(a.C.Goal);
            o.Add(new RobotChip(a.Id, tag, word, level, $"{mind.NameOf(a.Id)} — {mind.Thought(a.Id)} · 전지 {battery * 100:0}% · 누르면 따라간다"));
        }
        return o.ToArray();
    }

    /// <summary>독에서 다 찬 채 기다리는 중 (목표는 '충전'이지만 채울 것이 없다).</summary>
    private static bool Waiting(Agent a, float battery) => a.C.Goal == GoalKind.Charge && battery >= 0.95f;

    /// <summary>로봇 카드의 판단 줄: '하는 일 ▸ 방법' · 맡은 까닭 · 최근 판단 (셋까지 — 일지가 비면 주컴퓨터 기록에서 그 로봇 줄).</summary>
    private void RobotCard(Sim w, Mind mind, Agent a, List<string> lines)
    {
        bool waiting = Waiting(a, a.C.Battery);
        string doing = waiting ? "독에서 기다린다" : Mind.GoalName(a.C.Goal), thought = waiting ? "" : mind.Thought(a.Id);
        int dash = thought.IndexOf(" — ", StringComparison.Ordinal);
        lines.Add("하는 일: " + doing + (dash > 0 ? " ▸ " + thought[(dash + 3)..] : ""));
        var cm = w.Find<ShipMind>();
        var jr = a.Judged;
        string assigned = AssignedWhy(w, a);
        if (assigned.Length > 0) lines.Add("맡은 까닭: " + assigned);
        var recent = new List<string>();
        for (int k = 0; k < (jr?.Count ?? 0) && recent.Count < 3; k++)
        {
            ref var j = ref jr!.Newest(k);
            if (j.Out == JudgeOut.Set && k + 1 < jr.Count && jr.Newest(k + 1).Out == JudgeOut.Assigned && jr.Newest(k + 1).Goal == j.Goal) continue;   // 맡음 바로 뒤의 세움은 한 줄로
            recent.Add(JudgedLine(j));
        }
        if (recent.Count == 0 && cm != null)   // 일지가 비었으면 (켜기 전 · 되살린 세계) 주컴퓨터 기록에서 그 로봇 줄
        {
            string name = mind.NameOf(a.Id);
            for (int i = cm.Log.Count - 1; i >= 0 && recent.Count < 3; i--)
                if (cm.Log[i].text.StartsWith(name + ":") || cm.Log[i].text.StartsWith(name + " →")) recent.Add($"{Hm(cm.Log[i].tick)} {TextLint.Clean(cm.Log[i].text[name.Length..].TrimStart(':', ' '))}");
        }
        if (recent.Count > 0) lines.Add("최근 판단: " + string.Join(" / ", recent));
    }

    /// <summary>맡은 까닭 (로봇 카드 · 자세한 판 '하는 일'이 같은 글): 지금 목표를 주컴퓨터가 맡겼나 (일지의 '맡음') → 주컴퓨터 일지의 그 로봇 맡김 줄의 까닭 · 아니면 스스로 (빈 목표면 빈 글).</summary>
    private static string AssignedWhy(Sim w, Agent a)
    {
        if (a.C.Goal == GoalKind.Idle) return "";
        var cm = w.Find<ShipMind>();
        var jr = a.Judged;
        long assignedAt = long.MinValue;
        for (int k = 0; k < (jr?.Count ?? 0); k++)
        {
            ref var j = ref jr!.Newest(k);
            if (j.Out == JudgeOut.Assigned && j.Goal == a.C.Goal) { assignedAt = j.Tick; break; }
            if (j.Out is JudgeOut.Set or JudgeOut.Dropped && j.Goal != a.C.Goal) break;
        }
        string why = "";
        if (assignedAt != long.MinValue && cm?.Notes is { } notes)
            for (int k = 0; k < notes.Count; k++)
            {
                ref var n = ref notes.Newest(k);
                if (n.Tick < assignedAt - 1) break;
                if (n.Robot == a.Id && n.Kind is NoteKind.Assign or NoteKind.Defer) { why = ShipMind.ReasonText(n.Reason); break; }
            }
        return assignedAt != long.MinValue ? "주컴퓨터가 맡겼다" + (why.Length > 0 ? " — " + why : "") : Waiting(a, a.C.Battery) ? "스스로 — 맡은 일이 없다" : a.C.Goal == GoalKind.Charge ? "스스로 — 전지가 줄었다" : "스스로 골랐다";
    }

    private static string Hm(long t) { long m = t % Time.TicksPerDay * 60 / Time.TicksPerHour; return $"{m / 60:00}:{m % 60:00}"; }

    private static string JudgedLine(in Judgment j)
    {
        string g = Mind.GoalName(j.Goal);
        string what = j.Out switch
        {
            JudgeOut.Assigned => $"맡음 — {g}",
            JudgeOut.Set => $"시작 — {g}",
            JudgeOut.Done => $"마침 — {g}",
            JudgeOut.Blocked => $"막힘 — {g}: {j.Note ?? Mind.SnagText(j.Why)}",
            JudgeOut.Dropped => $"그만둠 — {g} → {Mind.GoalName((GoalKind)j.Method)}",
            JudgeOut.CantPlan => $"못 세움 — {g}{(j.Why != SnagKind.None ? ": " + Mind.SnagText(j.Why) : "")}",
            JudgeOut.Released => $"놓임 — {g}{(j.Why != SnagKind.None ? ": " + Mind.SnagText(j.Why) : "")}",
            JudgeOut.Turned => $"돌아섬 — {j.Note ?? g}",
            JudgeOut.Hesitate => $"망설임 — {g}",
            JudgeOut.Defer => $"미룸 — {g}",
            _ => $"잊음 — {g}",
        };
        return $"{Hm(j.Tick)} {TextLint.Clean(what)}";
    }

    /// <summary>주컴퓨터가 새로 한 말을 받아 말 창에 (화면 초) — 세계가 바뀌면 그 끝에서 다시 센다.</summary>
    private void WatchTalk(double realSec)
    {
        var sm = Sim.Find<ShipMind>();
        if (!ReferenceEquals(Sim, _talkWorld)) { _talkWorld = Sim; _talkWait.Clear(); _talkShown.Clear(); _noteSeen = sm?.Notes?.Added ?? 0; _talkIdle = 0; return; }
        if (!Paused)
        {
            for (int i = 0; i < _talkShown.Count; i++) _talkShown[i] = (_talkShown[i].text, _talkShown[i].level, _talkShown[i].age + realSec);
            _talkIdle += realSec;
        }
        if (sm?.Notes is { } notes && notes.Added > _noteSeen)
        {
            int fresh = (int)Math.Min(notes.Added - _noteSeen, notes.Count);
            for (int k = fresh - 1; k >= 0; k--)
            {
                ref var n = ref notes.Newest(k);
                string text = n.Text ?? "";
                if (text.StartsWith("일지: ")) continue;   // 일지는 알림으로 따로 띄운다
                text = TextLint.Clean(text);
                if (text.Length > 46) text = text[..45].TrimEnd() + "…";
                int level = n.Kind is NoteKind.Seal or NoteKind.Stuck ? 2 : n.Kind is NoteKind.Assign or NoteKind.Defer or NoteKind.Release or NoteKind.Block or NoteKind.Job or NoteKind.Ask ? 1 : 0;
                _talkWait.Add(($"{Hm(n.Tick)} {text}", level));
            }
            _noteSeen = notes.Added;
            Promote();
            while (_talkWait.Count > TalkQueue)   // 밀린 줄은 여섯까지: 가벼운 것 · 오래된 것부터 버린다
            {
                int drop = 0;
                for (int i = 1; i < _talkWait.Count; i++) if (_talkWait[i].level < _talkWait[drop].level) drop = i;
                _talkWait.RemoveAt(drop);
            }
        }
        Promote();
        if (_talkIdle >= TalkFold) _talkShown.Clear();
    }

    /// <summary>빈 줄이 있으면 곧바로 · 꽉 찼으면 가장 오래 보인 줄이 2초를 넘었을 때 한 줄씩 (줄 사이 0.6초 — 셋이 한꺼번에 바뀌지 않게) 밀린 줄을 올린다.</summary>
    private void Promote()
    {
        while (_talkWait.Count > 0 && (_talkShown.Count < TalkRows || _talkShown[0].age >= TalkHold && _talkShown[^1].age >= TalkStep))
        {
            if (_talkShown.Count >= TalkRows) _talkShown.RemoveAt(0);
            _talkShown.Add((_talkWait[0].text, _talkWait[0].level, 0));
            _talkWait.RemoveAt(0);
            _talkIdle = 0;
        }
    }

    /// <summary>이 장의 말 창 줄 (위가 오래된 것 · 갓 들어온 줄은 0.3초 동안 밝아진다).</summary>
    public TalkLine[] Talk()
    {
        if (_talkShown.Count == 0) return Array.Empty<TalkLine>();
        var o = new TalkLine[_talkShown.Count];
        float fade = (float)Math.Clamp((TalkFold - _talkIdle) / 0.5, 0, 1);
        for (int i = 0; i < o.Length; i++) o[i] = new TalkLine(_talkShown[i].text, _talkShown[i].level, Math.Min(fade, (float)Math.Clamp(_talkShown[i].age / 0.3 + 0.2, 0, 1)));
        return o;
    }
}
