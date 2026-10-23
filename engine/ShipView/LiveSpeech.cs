using ShipEngine;

namespace ShipView;

/// <summary>머리 위 풍선 하나 (칸 · 가운데 위): 사람 · 로봇 · 글 · 속말인가 · 흐림 (0~1).</summary>
public readonly record struct Bubble(int Id, double X, double Y, string Text, bool Inner, float Alpha);

/// <summary>
/// 입말 풍선 · 속말 풍선 (docs/MIND.md 4단 — 화면만 · 세계를 읽기만): 사람이 소리 내어 한 말 (사회의 말 기록) · 위급할 때의 외침 (사실 '외쳤다')을 그 머리 위에 화면 3~6초 붙잡아 둔다.
/// 하루 3분 (화면 1초 200틱)에서도 읽히게 게임 시간이 아니라 화면 시간으로 붙든다 · 화면 안 다섯까지 (고른 이 먼저) · 자는 이 · 의식 없는 이 · 숨진 이의 말은 띄우지 않는다.
/// 속말 풍선 (설정 — 끔 · 고른 이 · 화면 가운데 셋 · 같은 생각은 한 번)은 카드의 '생각:'과 같은 글을 생각 구름에.
/// </summary>
public sealed partial class LiveShow
{
    /// <summary>입말 풍선 (기본 켬) · 속말 풍선 (0 끔 · 1 고른 이 · 2 화면 안 셋까지).</summary>
    public bool SpeechBubbles = true;
    public int ThoughtBubbles;
    public const int BubbleMax = 5, ThoughtMax = 3;

    private readonly Dictionary<int, (string text, double left, double total)> _said = new();
    private readonly Dictionary<int, double> _shoutAt = new();
    private long _sayTick = long.MinValue; private int _sayAt; private long _warnCursor = -1;
    private Sim? _speechWorld;

    /// <summary>새 말 · 외침을 받아 사람마다 붙잡는다 (화면 초) — 첫 장 · 다시보기를 시작하거나 끝내면 세계 끝에서 다시 센다 (지난 말이 한꺼번에 쏟아지지 않게).</summary>
    private void WatchSpeech(double realSec)
    {
        var w = World;
        var so = w.Find<Society>();
        if (!ReferenceEquals(w, _speechWorld)) { _speechWorld = w; ResetSpeech(); return; }
        foreach (var id in _said.Keys.ToList())
        {
            var s = _said[id];
            s.left -= Paused ? 0 : realSec;
            if (s.left <= 0) _said.Remove(id); else _said[id] = s;
        }
        if (so != null)
        {
            var list = so.Sayings;
            int start = list.Count;
            int sameTick = 0;
            while (start > 0 && list[start - 1].Tick > _sayTick) start--;
            // 마지막으로 본 틱의 것들: 그 틱에서 이미 본 수만큼 건너뛴다
            int at = start;
            while (at > 0 && list[at - 1].Tick == _sayTick) at--;
            for (int i = at; i < start; i++) sameTick++;
            for (int i = at + Math.Min(_sayAt, sameTick); i < list.Count; i++)
            {
                var sy = list[i];
                string text = Speech.Clean(w.Causes.Text(sy.Line));
                if (text.Length > 0) Hold(sy.Speaker, text);
            }
            if (list.Count > 0)
            {
                long last = list[^1].Tick;
                int n = 0; for (int i = list.Count - 1; i >= 0 && list[i].Tick == last; i--) n++;
                _sayTick = last; _sayAt = n;
            }
        }
        // 외침 (사실 '외쳤다' — 말한 이 · 믿음 갈래): 같은 이는 화면 10초에 하나
        var mind = w.Find<Mind>();
        foreach (var f in w.Facts.Since(_warnCursor))
        {
            if (f.Kind != FactKind.Warned || mind == null || f.Actor < 0 || f.Actor >= mind.Agents.Count) continue;
            if (_shoutAt.TryGetValue(f.Actor, out var at) && _anim - at < 10) continue;
            string shout = Inner.Shout((BeliefKind)f.Other, !mind.Agents[f.Actor].IsPerson);
            if (shout.Length == 0) continue;
            _shoutAt[f.Actor] = _anim;
            Hold(f.Actor, shout);
        }
        _warnCursor = w.Facts.Total;
    }

    private void Hold(int who, string text)
    {
        // 먼저 한 말이 1.5초 안이면 두 마디로 잇는다
        if (_said.TryGetValue(who, out var old) && old.total - old.left < 1.5 && !old.text.Contains('\n') && old.text != text) text = old.text + "\n" + text;
        double sec = Math.Clamp(2.5 + 0.08 * text.Length, 3, 6);
        _said[who] = (Speech.Cut2(text), sec, sec);
    }

    private void ResetSpeech()
    {
        _said.Clear(); _shoutAt.Clear();
        var w = World;
        var so = w.Find<Society>();
        if (so != null && so.Sayings.Count > 0)
        {
            long last = so.Sayings[^1].Tick; int n = 0;
            for (int i = so.Sayings.Count - 1; i >= 0 && so.Sayings[i].Tick == last; i--) n++;
            _sayTick = last; _sayAt = n;
        }
        else { _sayTick = long.MinValue; _sayAt = 0; }
        _warnCursor = w.Facts.Total;
    }

    /// <summary>이 장의 풍선: 화면 안 · 깨어 있는 이 · 입말 다섯까지 (고른 이 먼저 · 남은 시간이 긴 것) + 속말 (설정).</summary>
    public Bubble[] Bubbles()
    {
        var w = World; var mind = w.Find<Mind>(); var life = w.Find<Life>();
        if (mind == null) return Array.Empty<Bubble>();
        var (l, t, cw, ch) = Camera();
        bool Awake(int id)
        {
            if (id < 0 || id >= mind.Agents.Count) return false;
            var a = mind.Agents[id];
            if (!a.IsPerson) return a.C.Up != 0;
            if (life == null) return true;
            var b = life.Bodies[a.C.Body];
            return b.Alive != 0 && b.Conscious != 0 && b.Asleep == 0;
        }
        bool Seen(double x, double y) => !double.IsNaN(x) && x >= l && x <= l + cw && y >= t && y <= t + ch;
        var list = new List<Bubble>();
        if (SpeechBubbles)
            foreach (var (id, s) in _said.OrderByDescending(kv => kv.Key == Selected).ThenByDescending(kv => kv.Value.left))
            {
                if (list.Count >= BubbleMax || !Awake(id)) continue;
                var (x, y) = DrawnAt(id);
                if (!Seen(x, y)) continue;
                float alpha = (float)Math.Clamp(Math.Min(s.left, s.total - s.left + 0.1) / 0.3, 0, 1);
                list.Add(new Bubble(id, x, y - (mind.Agents[id].IsPerson ? 0.75 : 0.6), s.text, false, alpha));
            }
        if (ThoughtBubbles > 0)
        {
            var who = new List<int>();
            if (ThoughtBubbles == 1) { if (Selected >= 0) who.Add(Selected); }
            else
            {
                double mx = l + cw / 2, my = t + ch / 2;
                who.AddRange(mind.Agents.Where(a => a.IsPerson && Awake(a.Id)).Select(a => (a.Id, p: DrawnAt(a.Id))).Where(q => Seen(q.p.x, q.p.y))
                    .OrderBy(q => (q.p.x - mx) * (q.p.x - mx) + (q.p.y - my) * (q.p.y - my)).Take(ThoughtMax).Select(q => q.Id));
            }
            foreach (int id in who)
            {
                if (!mind.Agents[id].IsPerson || !Awake(id) || list.Any(b => b.Id == id && !b.Inner)) continue;
                var (x, y) = DrawnAt(id);
                if (!Seen(x, y)) continue;
                string text = Speech.Cut2(Inner.Of(w, mind, mind.Agents[id]));
                if (id != Selected && list.Any(b => b.Inner && b.Text == text)) continue;   // 같은 생각을 여럿이 하면 한 번만 (고른 이는 늘)
                list.Add(new Bubble(id, x, y - 0.75, text, true, 1));
            }
        }
        return list.ToArray();
    }
}

/// <summary>풍선 글 다듬기: 회의 '이름: "말"' → '말' · 두 마디까지 (한 마디 18자 · 넘치면 …).</summary>
public static class Speech
{
    public const int ClauseMax = 18;

    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int q = s.IndexOf(": \"", StringComparison.Ordinal);
        if (q > 0 && q < 12 && s.EndsWith("\"")) s = s[(q + 3)..^1];
        else if (s.Length > 1 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        // 원인 글의 요약 꼴 ('…와 …이 이야기했다')은 말이 아니다
        if (s.EndsWith("이야기했다") || s.EndsWith("이야기를 나눴다")) return "";
        return TextLint.Clean(s);
    }

    public static string Cut2(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var parts = new List<string>();
        foreach (var line in s.Split('\n'))
        {
            string rest = line.Trim();
            while (rest.Length > 0 && parts.Count < 2)
            {
                int cut = -1;
                foreach (var sep in new[] { " — ", ". ", "? ", "! ", ", " })
                {
                    int k = rest.IndexOf(sep, StringComparison.Ordinal);
                    if (k > 0 && k <= ClauseMax + 4 && (cut < 0 || k < cut)) cut = k + (sep == " — " ? 0 : 1);
                }
                string part = cut > 0 ? rest[..cut].Trim() : rest;
                if (part.Length > ClauseMax + 6) part = part[..(ClauseMax + 5)].TrimEnd() + "…";
                parts.Add(part);
                rest = cut > 0 ? rest[cut..].TrimStart(' ', '—').Trim() : "";
            }
            if (parts.Count >= 2) break;
        }
        return string.Join("\n", parts);
    }
}
