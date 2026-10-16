using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.3 선내 메신저: 사건마다 짧은 채팅 (농담 · 불평 · 사진 · 회의 시간 변경 · 찾는 물건 · 해명 · 사과).
//  · 손목 단말로 틈틈이 본다 — 자는 사람 · 선외에 나간 사람 · 손이 바쁜 사람 · 단말을 침실에 두고 나온 사람은 못 본다.
//  · 안 읽은 사람은 모른다: 회의가 당겨진 걸 모르고 늦게 오고, 찾는 물건 답장을 못 봐 계속 찾는다.
//  · 말투는 사람마다 (농담꾼은 "ㅋㅋ" · 원칙파는 존댓말 · 투덜이는 "하…" · 말 많은 사람은 느낌표).
//  · 주 컴퓨터는 읽음 표시를 본다: 회의가 시작됐는데 변경을 못 본 사람은 그 방 스피커로 부른다.

public enum ChatKind : byte { Talk, Joke, Gripe, Photo, Notice, Meeting, Ask, Answer, Thanks, Sorry, Accuse, Explain, Computer }

public sealed class ChatMsg
{
    public int Id { get; init; }
    public long Tick { get; init; }
    /// <summary>쓴 사람 (−1 = 주 컴퓨터).</summary>
    public int Author { get; init; } = -1;
    public ChatKind Kind { get; init; }
    public string Text { get; init; } = "";
    /// <summary>사진 (PhotoInfo.Id) · 물건 (Belonging.Id) · 사건 (CupCase.Id) · 다룬 사람.</summary>
    public int Photo { get; init; } = -1;
    public int Thing { get; init; } = -1;
    public int Case { get; init; } = -1;
    public int About { get; init; } = -1;
    public int Reply { get; init; } = -1;
}

/// <summary>회의 시간이 바뀐 날 (메신저로만 알렸다).</summary>
public sealed class MeetingMove
{
    public long Day { get; init; }
    public float From { get; init; }
    public float To { get; init; }
    public int Msg { get; init; }
    public int Author { get; init; }
    public string Why { get; init; } = "";
    /// <summary>모르고 늦게 온 사람 · 컴퓨터가 불러 준 사람 · 아무도 없는 회의실에 온 사람.</summary>
    public List<int> Late { get; } = new();
    public List<int> Paged { get; } = new();
    public List<int> Missed { get; } = new();
    public long GatherStart { get; set; } = -1;
}

public sealed class ShipChat
{
    private readonly World _w;
    private readonly InfoSystem _info;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6577 + 911));
    private int _next = 1;

    public List<ChatMsg> All { get; } = new();
    /// <summary>마지막으로 읽은 글 번호 (사람마다).</summary>
    public SortedDictionary<int, int> LastRead { get; } = new();
    private readonly SortedDictionary<int, long> _nextCheck = new();
    /// <summary>단말을 침실에 두고 나온 사람 (언제까지).</summary>
    public SortedDictionary<int, long> TabletLeft { get; } = new();
    /// <summary>화면용: 지금 단말을 들여다보는 사람 (언제까지).</summary>
    public SortedDictionary<int, long> Reading { get; } = new();
    public MeetingMove? Move { get; private set; }
    public List<MeetingMove> Moves { get; } = new();
    public int Posts, Reads, Unseen, Pages, LateArrivals;

    public ShipChat(World w, InfoSystem info) { _w = w; _info = info; }

    public ChatMsg? Get(int id) { for (int i = All.Count - 1; i >= 0; i--) if (All[i].Id == id) return All[i]; return null; }
    public int Read(CrewMember c) => LastRead.TryGetValue(c.Id, out var v) ? v : 0;
    public bool HasRead(CrewMember c, int msg) => Read(c) >= msg;
    public int Unread(CrewMember c) { int r = Read(c), n = 0; for (int i = All.Count - 1; i >= 0 && All[i].Id > r; i--) if (All[i].Author != c.Id) n++; return n; }
    public int ReadCount(ChatMsg m) => _w.Crew.Count(c => !c.Dead && !c.IsChild && (HasRead(c, m.Id) || m.Author == c.Id));
    public bool HasTablet(CrewMember c) => !(TabletLeft.TryGetValue(c.Id, out var t) && t > _w.Tick) || c.Room == c.Bed?.Room;

    // ───────────────────────────── 쓰기 ─────────────────────────────

    public ChatMsg Post(CrewMember? author, ChatKind kind, string text, int photo = -1, int thing = -1, int @case = -1, int about = -1, int reply = -1)
    {
        var m = new ChatMsg { Id = _next++, Tick = _w.Tick, Author = author?.Id ?? -1, Kind = kind, Text = text, Photo = photo, Thing = thing, Case = @case, About = about, Reply = reply };
        All.Add(m);
        if (All.Count > 240) All.RemoveAt(0);
        Posts++;
        if (author != null) { LastRead[author.Id] = Math.Max(Read(author), m.Id); Reading[author.Id] = _w.Tick + SimTime.Minutes(2); }
        return m;
    }

    /// <summary>말버릇: 사람마다 채팅 말투가 다르다 (반말 · 존댓말 둘 다 받아 고른다).</summary>
    public static string Voice(CrewMember c, string casual, string polite)
    {
        int style = Style(c);
        return style switch
        {
            0 => casual + " ㅋㅋ",
            1 => polite,
            2 => casual + "!!",
            3 => casual.TrimEnd('.', '!', '?') + "…",
            4 => "하… " + casual,
            5 => casual.TrimEnd('.') + "~",
            6 => polite + " ^^",
            _ => casual,
        };
    }

    public static int Style(CrewMember c)
    {
        if (Life.Has(c, Habit.Joker) || Life.Has(c, Habit.Prankster)) return 0;
        if (c.Value == CrewValue.Rules || Life.Has(c, Habit.Serious) || Life.Has(c, Habit.Methodical)) return 1;
        if (Life.Has(c, Habit.Talker)) return 2;
        if (Life.Has(c, Habit.Loner) || Life.Has(c, Habit.Pessimist)) return 3;
        if (Life.Has(c, Habit.Grumbler)) return 4;
        if (Life.Has(c, Habit.Optimist) || Life.Has(c, Habit.Cheerful)) return 5;
        return (c.Id * 7 + c.Quirk) % 4 == 0 ? 6 : 7;
    }

    // ───────────────────────────── 읽기 ─────────────────────────────

    /// <summary>얼마마다 단말을 보나 (분): 말 많은 사람은 자주 · 혼자 있기 좋아하는 사람은 가끔.</summary>
    private float Every(CrewMember c)
    {
        float m = 45f - 30f * c.Traits.Sociability;
        if (Life.Has(c, Habit.Talker)) m *= 0.5f;
        if (Life.Has(c, Habit.Loner)) m *= 2f;
        if (Life.Has(c, Habit.Serious) || Life.Has(c, Habit.Methodical)) m *= 1.3f;
        return Math.Clamp(m, 8f, 110f);
    }

    /// <summary>지금 볼 수 있나: 자는 중 · 선외 · 쓰러짐 · 급한 일 · 손이 바쁜 일 · 단말을 두고 나옴.</summary>
    public string? CannotRead(CrewMember c)
    {
        if (c.Dead || c.Away) return "없다";
        if (!c.IsAwake) return "자는 중";
        if (c.Outside) return "선외";
        if (c.Down) return "쓰러짐";
        if (!HasTablet(c)) return "단말을 침실에 두고 나왔다";
        if (c.Job is Job j && (j.Urgent || j.Order != null && c.Pose == Pose.Working)) return "손이 바쁘다";
        return null;
    }

    public void Update()
    {
        var w = _w;
        long now = w.Tick;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild && c.Age < 8f) continue;
            if (_nextCheck.TryGetValue(c.Id, out var t) && t > now) continue;
            _nextCheck[c.Id] = now + SimTime.Minutes(Every(c) * (0.7f + 0.6f * R.Float()));
            if (Read(c) >= _next - 1) continue;
            if (CannotRead(c) != null) { Unseen++; continue; }
            Check(c);
        }
    }

    /// <summary>단말을 연다: 안 읽은 글을 다 읽는다 (읽은 글이 행동을 바꾼다).</summary>
    public void Check(CrewMember c, string? how = null)
    {
        int r = Read(c);
        if (r >= _next - 1) return;
        LastRead[c.Id] = _next - 1;
        Reading[c.Id] = _w.Tick + SimTime.Minutes(2);
        Reads++;
        int end = All.Count; // 읽다가 답을 달면 글이 늘어난다 — 연 때까지 있던 글만
        for (int i = 0; i < end && i < All.Count; i++)
            if (All[i].Id > r && All[i].Author != c.Id) _info.OnRead(c, All[i]);
        if (how != null) _w.Log.Add(_w.Tick, LogKind.Life, how, c.Id);
    }

    /// <summary>단말을 침실에 두고 나온다 (깜빡하는 사람 — 시험에서도 쓴다).</summary>
    public void LeaveTablet(CrewMember c, float hours)
    {
        TabletLeft[c.Id] = _w.Tick + SimTime.Hours(hours);
        _w.Log.Add(_w.Tick, LogKind.Life, "손목 단말을 침실에 두고 나왔다", c.Id);
    }

    // ───────────────────────────── 회의 시간 ─────────────────────────────

    /// <summary>오늘 회의 시각 (메신저로 옮겼으면 옮긴 시각).</summary>
    public float MeetingHour(float baseHour)
    {
        var m = Move;
        return m != null && m.Day == _w.Tick / SimTime.TicksPerDay ? m.To : baseHour;
    }

    /// <summary>회의를 옮긴다 (메신저로만 알린다).</summary>
    public MeetingMove? MoveMeeting(CrewMember author, float to, string why)
    {
        var w = _w;
        long day = w.Tick / SimTime.TicksPerDay;
        float from = MeetingHour(w.Meetings.Hour);
        if (MathF.Abs(to - from) < 0.25f || to < SimTime.HourOfDay(w.Tick) + 0.5f || from < SimTime.HourOfDay(w.Tick) + 0.5f) return null; // 지난 시각 · 이미 모이는 중
        bool earlier = to < from;
        string hh = $"{(int)to}시{(to % 1f > 0.01f ? " 반" : "")}";
        var msg = Post(author, ChatKind.Meeting, Voice(author, $"오늘 회의 {hh}로 {(earlier ? "당길게" : "미룰게")}. {why}", $"오늘 회의는 {hh}로 {(earlier ? "당깁니다" : "미룹니다")}. {why}"));
        Move = new MeetingMove { Day = day, From = from, To = to, Msg = msg.Id, Author = author.Id, Why = why };
        Moves.Add(Move);
        if (Moves.Count > 20) Moves.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Life, $"메신저에 오늘 회의를 {hh}로 {(earlier ? "당긴다" : "미룬다")}고 올렸다", author.Id);
        return Move;
    }

    /// <summary>회의가 옮겨진 걸 모르나 (오늘 옮겼고 · 그 글을 안 읽었고 · 말로도 못 들었다).</summary>
    public bool Unaware(CrewMember c)
    {
        var m = Move;
        if (m == null || m.Day != _w.Tick / SimTime.TicksPerDay || c.Id == m.Author) return false;
        return !HasRead(c, m.Msg);
    }

    /// <summary>MeetingActivity: 아직 갈 때가 아니라고 안다 (당겨진 걸 모르는 사람은 원래 시각까지).</summary>
    public bool NotYet(CrewMember c)
    {
        if (!Unaware(c)) return false;
        var m = Move!;
        return m.To < m.From && SimTime.HourOfDay(_w.Tick) < m.From;
    }

    /// <summary>말로 듣는다 (같은 방 사람 · 컴퓨터 스피커): 그 글을 읽은 것과 같다.</summary>
    public void Tell(CrewMember c, string how)
    {
        if (Move is not MeetingMove m || !Unaware(c)) return;
        LastRead[c.Id] = Math.Max(Read(c), m.Msg);
        c.NextThinkTick = Math.Min(c.NextThinkTick, _w.Tick + 1);
        _w.Log.Add(_w.Tick, LogKind.Life, how, c.Id);
    }

    public void Hash(Action<long> I)
    {
        I(_next); I(Posts); I(Reads);
        foreach (var (k, v) in LastRead) { I(k); I(v); }
        if (Move != null) { I(Move.Day); I((long)(Move.To * 4)); I(Move.Late.Count); I(Move.Paged.Count); }
    }
}
