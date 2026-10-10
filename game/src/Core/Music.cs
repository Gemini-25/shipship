using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.6 음악 — ① 상황 음악(평시 · 긴장 · 위기 · 추모: 화면 밖 관찰자의 귀, 배의 상태에서 정한다 · 머뭇거림을 둬서 깜빡이지 않게)
// ② 배 안에서 트는 음악: 승무원이 쉬는 시간에 방 스피커로 노래를 튼다 → 소리 전파(Hearing)를 그대로 타고 벽 · 문 너머로 먹먹하게 퍼진다.
// 서로 맞물림:
//  · 음악 × 기분: 좋아하는 갈래를 들으면 긴장이 풀리고, 싫어하는 갈래는 거슬린다 (사람마다 취향).
//  · 음악 × 잠: 잠든 사람에게 벽 너머로 넘어간 소리 → 깬다 → 주 컴퓨터가 그 스피커를 줄인다 / 깬 사람이 줄여 달라고 한다 (튼 사람에게 서운함).
//  · 음악 × 경보: 긴장 · 위기가 되면 주 컴퓨터가 방송이 묻히지 않게 선내 음악을 끈다 (컴퓨터가 없으면 음악이 계속 — 방송이 묻힌다).
//  · 음악 × 스피커 · 전기: 스피커가 고장 났거나 정전된 방에선 멎는다.
//  · 음악 × 죽음: 누가 죽으면 다음 저녁 식당에서 추모곡 — 들은 사람은 함께 슬퍼하고 마음의 짐이 조금 준다.
//  · 음악 × 숨은 재능(v17.9): 좋아하는 노래가 흐르는 방에 사람이 모이면 숨겨 온 노래 솜씨가 드러난다.

public enum MusicMood : byte { Calm, Mourning, Tension, Crisis }

/// <summary>방 스피커로 트는 노래 한 곡(묶음).</summary>
public sealed class CabinTune
{
    public int Id { get; init; }
    public int Room { get; init; }
    public Vector2 At { get; init; }
    public byte Genre { get; init; }
    /// <summary>튼 사람 (-1 = 주 컴퓨터).</summary>
    public int By { get; init; } = -1;
    public long Since { get; init; }
    public long Until { get; set; }
    public float Level { get; set; } = 0.55f;
    public bool Lowered { get; set; }
    public bool Memorial { get; init; }
    /// <summary>소리 주인 번호 (사람 번호와 겹치지 않게).</summary>
    public int Owner => -1000 - Id;
    public float Loud => Lowered ? Level * 0.45f : Level;
}

public sealed class MusicStats
{
    public int MoodChanges, Played, HeardMinutes, MuffledMinutes, Enjoyed, Grated, Woken, Complaints, Lowered, ComputerMuted, ComputerLowered, Memorials, SpeakerStops, Talents;
    public readonly int[] MoodMinutes = new int[4];
    public string Summary() =>
        $"분위기 바뀜 {MoodChanges} (평시 {MoodMinutes[0]}분 · 추모 {MoodMinutes[1]} · 긴장 {MoodMinutes[2]} · 위기 {MoodMinutes[3]}) · 튼 노래 {Played} · 들은 분 {HeardMinutes}(먹먹 {MuffledMinutes}) · " +
        $"즐김 {Enjoyed} · 거슬림 {Grated} · 깸 {Woken} · 줄여 달라 {Complaints} · 줄임 {Lowered}(컴퓨터 {ComputerLowered}) · 컴퓨터가 끔 {ComputerMuted} · 추모곡 {Memorials} · 스피커로 멎음 {SpeakerStops} · 노래로 드러난 재능 {Talents}";
}

public sealed class MusicSystem
{
    public static readonly string[] Genres = { "잔잔한 피아노", "옛 노래", "재즈", "전자음", "민요", "합창" };
    public static string MoodName(MusicMood m) => m switch { MusicMood.Tension => "긴장", MusicMood.Crisis => "위기", MusicMood.Mourning => "추모", _ => "평시" };

    /// <summary>좋아하는 갈래 · 싫어하는 갈래 (사람마다 다르다).</summary>
    public static byte Likes(CrewMember c) => (byte)((c.Id * 7 + 3) % 6);
    public static byte Dislikes(CrewMember c) { int d = (c.Id * 5 + 1) % 6; return (byte)(d == Likes(c) ? (d + 2) % 6 : d); }

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 4111));
    private long _next;
    private int _nextId = 1;
    private MusicMood _want;
    private long _wantSince;
    private int _deaths;
    private long _memorialAt = -1;

    public MusicMood Mood { get; private set; }
    public string MoodWhy { get; private set; } = "조용한 항해";
    public long MoodSince { get; private set; }
    public List<CabinTune> Playing { get; } = new();
    public List<(long tick, MusicMood mood)> History { get; } = new();
    public MusicStats Stats { get; } = new();
    /// <summary>사람마다 마지막으로 노래를 튼 시각.</summary>
    private readonly Dictionary<int, long> _lastPlayed = new();
    /// <summary>지금 음악을 듣는 사람 → (곡 번호, 크기, 먹먹한가) — 화면 · 시험.</summary>
    public Dictionary<int, (int tune, float level, bool muffled)> Listening { get; } = new();

    public MusicSystem(World w) => _w = w;

    public long LastPlayed(CrewMember c) => _lastPlayed.TryGetValue(c.Id, out var t) ? t : -1;
    public CabinTune? TuneIn(Room r) => Playing.FirstOrDefault(t => t.Room == r.Id);

    /// <summary>방 스피커 자리 (윗벽 아래 바닥 — 가운데에 가까운 칸).</summary>
    public static Cell? SpeakerCell(Ship ship, Room r)
    {
        Cell? best = null;
        float bestD = float.MaxValue;
        foreach (var c in r.Cells)
        {
            if (!ship.IsWalkable(c)) continue;
            float d = (c.Y - r.Cells.Min(x => x.Y)) * 10f + MathF.Abs(c.Center.X - r.Center.X);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>음악이 나올 수 있는 방: 스피커가 살아 있고 전기가 들어온다.</summary>
    public bool CanPlay(Room r) => r.Powered && !r.Detached && _w.Automation.Speak.SpeakerWorks(r);

    /// <summary>노래를 튼다 (사람 · 컴퓨터).</summary>
    public CabinTune? Start(Room r, byte genre, CrewMember? by, float minutes, bool memorial = false)
    {
        if (!CanPlay(r) || SpeakerCell(_w.Ship, r) is not Cell at) return null;
        Playing.RemoveAll(t => t.Room == r.Id);
        var t = new CabinTune { Id = _nextId++, Room = r.Id, At = at.Center, Genre = genre, By = by?.Id ?? -1, Since = _w.Tick, Until = _w.Tick + SimTime.Minutes(minutes), Memorial = memorial };
        Playing.Add(t);
        Stats.Played++;
        if (by != null) _lastPlayed[by.Id] = _w.Tick;
        return t;
    }

    public void Stop(CabinTune t, string? why)
    {
        Playing.Remove(t);
        if (why != null) _w.Log.Add(_w.Tick, LogKind.Life, why, t.By);
    }

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(1);
        UpdateMood();
        Stats.MoodMinutes[(int)Mood]++;
        Memorials();
        var a = w.Automation;
        bool computer = a.Present && a.CoreOnline;
        // 경보: 컴퓨터가 방송이 묻히지 않게 끈다
        if (Mood >= MusicMood.Tension && Playing.Count > 0 && computer)
        {
            foreach (var t in Playing.ToList()) Stop(t, null);
            Stats.ComputerMuted++;
            w.Log.Add(w.Tick, LogKind.Ship, "주 컴퓨터가 방송이 묻히지 않게 선내 음악을 껐다");
        }
        // 곡마다: 끝 · 스피커 · 전기 → 소리를 낸다
        foreach (var t in Playing.ToList())
        {
            var room = w.Ship.Rooms[t.Room];
            if (w.Tick >= t.Until) { Stop(t, null); continue; }
            if (!CanPlay(room)) { Stats.SpeakerStops++; Stop(t, $"{room.Name} 스피커가 꺼져 음악이 멎었다"); continue; }
            w.Hearing.EmitAt(Noise.Music, room, t.At, t.Loud, t.Owner, t.Genre);
        }
        Listen(computer);
    }

    // ── 상황 음악 ──

    private MusicMood Target(out string why)
    {
        var w = _w;
        long now = w.Tick;
        bool critAlert = false, warnAlert = false;
        for (int i = w.Alerts.Count - 1; i >= 0 && i >= w.Alerts.Count - 40; i--)
        {
            var al = w.Alerts[i];
            if (now - al.Tick > SimTime.Minutes(20)) break;
            if (al.Level == AlertLevel.Critical && now - al.Tick < SimTime.Minutes(10)) critAlert = true;
            if (al.Level == AlertLevel.Warning) warnAlert = true;
        }
        var top = w.Scale.Top;
        if (critAlert || w.Fire.Count >= 3 || top != null && top.Now >= IncidentScale.Ship) { why = critAlert ? "치명 경보" : w.Fire.Count >= 3 ? "번지는 불" : "배 전체가 걸린 사고"; return MusicMood.Crisis; }
        if (warnAlert || w.Fire.Count > 0 || w.Sensors.Incoming.Count > 0 || w.Hazards.StormActive || top != null && top.Now >= IncidentScale.System)
        { why = w.Fire.Count > 0 ? "불" : w.Sensors.Incoming.Count > 0 ? "다가오는 운석" : w.Hazards.StormActive ? "태양 폭풍" : warnAlert ? "경고" : "설비 사고"; return MusicMood.Tension; }
        long died = -1;
        foreach (var c in w.Crew) if (c.Dead && c.DiedAt > died) died = c.DiedAt;
        if (died >= 0 && now - died < SimTime.Hours(30)) { why = "잃은 사람"; return MusicMood.Mourning; }
        why = "조용한 항해";
        return MusicMood.Calm;
    }

    private void UpdateMood()
    {
        var w = _w;
        var want = Target(out var why);
        if (want != _want) { _want = want; _wantSince = w.Tick; }
        // 머뭇거림: 더 무거워질 땐 바로(위기) · 조금 뒤(긴장), 가벼워질 땐 한참 있다가
        long hold = want == MusicMood.Crisis ? 0 : want > Mood ? SimTime.Minutes(2) : SimTime.Minutes(12);
        if (want != Mood && w.Tick - _wantSince >= hold)
        {
            Mood = want;
            MoodWhy = why;
            MoodSince = w.Tick;
            Stats.MoodChanges++;
            History.Add((w.Tick, want));
            if (History.Count > 24) History.RemoveAt(0);
        }
        else if (want == Mood) MoodWhy = why;
    }

    // ── 추모곡 ──

    private void Memorials()
    {
        var w = _w;
        int deaths = 0;
        foreach (var c in w.Crew) if (c.Dead) deaths++;
        if (deaths > _deaths)
        {
            _deaths = deaths;
            // 다음 저녁 19시 (이미 지났으면 다음 날)
            long day = w.Tick / SimTime.TicksPerDay;
            long at = day * SimTime.TicksPerDay + SimTime.Hours(19);
            if (at < w.Tick + SimTime.Hours(2)) at += SimTime.TicksPerDay;
            _memorialAt = at;
        }
        if (_memorialAt < 0 || w.Tick < _memorialAt || Mood >= MusicMood.Tension) return;
        _memorialAt = -1;
        var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Chapel or RoomType.Lounge && CanPlay(r))
            .OrderBy(r => r.Type == RoomType.Chapel ? 0 : r.Type == RoomType.Mess ? 1 : 2).FirstOrDefault();
        if (room == null) return;
        var dead = w.Crew.Where(c => c.Dead).OrderByDescending(c => c.DiedAt).First();
        var friend = w.Crew.Where(c => !c.Dead && !c.Away).OrderByDescending(c => c.AffinityTo(dead)).FirstOrDefault();
        bool byFriend = friend != null && friend.AffinityTo(dead) > 0.3f;
        if (Start(room, 5, byFriend ? friend : null, 60f, memorial: true) == null) return;
        Stats.Memorials++;
        w.Log.Add(w.Tick, LogKind.Life, byFriend ? $"{room.Name}에서 {Ko.EulReul(dead.Name)} 기리는 노래를 틀었다" : $"주 컴퓨터가 {room.Name}에서 {dead.Name}의 추모곡을 틀었다", byFriend ? friend!.Id : -1);
        if (w.Automation.Present && w.Automation.CoreOnline) w.Automation.Speak.Announce($"오늘 저녁 {room.Name}에서 {Ko.EulReul(dead.Name)} 기억합니다", room, 0);
    }

    // ── 듣기: 기분 · 잠 · 불평 · 재능 ──

    private void Listen(bool computer)
    {
        var w = _w;
        Listening.Clear();
        if (Playing.Count == 0) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Room is not Room room) continue;
            // 이 방에서 들리는 가장 큰 곡
            CabinTune? best = null;
            float lv = 0f;
            bool muffled = false;
            foreach (var h in w.Hearing.In(room))
            {
                if (h.Src >= w.Hearing.Sources.Count) continue;
                var s = w.Hearing.Sources[h.Src];
                if (s.Kind != Noise.Music || h.Masked || h.Level <= lv) continue;
                var t = Playing.FirstOrDefault(x => x.Owner == s.Owner);
                if (t == null) continue;
                best = t; lv = h.Level; muffled = h.Muffled;
            }
            if (best == null) continue;
            float heard = lv * w.Hearing.Sense(c, Noise.Music) * (muffled ? 0.6f : 1f);
            if (heard < 0.04f) continue;
            Listening[c.Id] = (best.Id, heard, muffled);
            Stats.HeardMinutes++;
            if (muffled) Stats.MuffledMinutes++;
            if (c.Pose == Pose.Sleeping)
            {
                if (heard < 0.06f || R.Float() > heard * 0.5f) continue;
                Stats.Woken++;
                c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.02f);
                if (!best.Lowered)
                {
                    best.Lowered = true;
                    Stats.Lowered++;
                    var src = w.Ship.Rooms[best.Room];
                    if (computer)
                    {
                        Stats.ComputerLowered++;
                        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터가 {room.Name}에서 자는 {Ko.EulReul(c.Name)} 생각해 {src.Name} 스피커 소리를 줄였다");
                    }
                    else
                    {
                        Stats.Complaints++;
                        w.Log.Add(w.Tick, LogKind.Life, $"벽 너머 {src.Name} 노래에 잠을 깨 소리를 줄여 달라고 했다", c.Id);
                        if (best.By >= 0 && w.Crew.FirstOrDefault(x => x.Id == best.By) is CrewMember by && by != c)
                            w.Brain2.Emotions.Feel(c, Feeling.Anger, 0.15f, "음악 소리에 잠을 깼다", by);
                    }
                }
                continue;
            }
            const float perMin = 1f / 60f;
            if (best.Memorial)
            {
                if (R.Chance(0.05f)) w.Brain2.Emotions.Feel(c, Feeling.Sadness, 0.2f, "추모곡을 들었다");
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f * perMin);
                continue;
            }
            if (best.Genre == Likes(c))
            {
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.06f * heard * perMin * 6f);
                if (R.Chance(0.04f)) { Stats.Enjoyed++; w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.15f, $"좋아하는 {Genres[best.Genre]}가 흘렀다"); }
                if (!muffled && w.Curios.OnMusic(c, best)) Stats.Talents++;
            }
            else if (best.Genre == Dislikes(c) && heard > 0.2f)
            {
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f * heard * perMin * 6f);
                if (R.Chance(0.03f))
                {
                    Stats.Grated++;
                    if (!best.Lowered && !muffled && R.Chance(0.4f))
                    {
                        best.Lowered = true;
                        Stats.Lowered++;
                        Stats.Complaints++;
                        w.Log.Add(w.Tick, LogKind.Life, $"{Genres[best.Genre]}가 거슬려 소리를 줄였다", c.Id);
                    }
                }
            }
        }
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I((int)Mood); I(Stats.MoodChanges); I(Stats.Played); I(Stats.HeardMinutes); I(Stats.Woken); I(Stats.Lowered); I(Stats.ComputerMuted); I(Stats.Memorials);
        foreach (var t in Playing) { I(t.Id); I(t.Room); I(t.Until); F(t.Level); I(t.Lowered ? 1 : 0); }
    }
}

/// <summary>v17.6 쉬는 시간에 방 스피커로 노래를 튼다 (휴게실 · 식당 · 체육관 · 제 침실) — 듣고 앉아 쉰다.</summary>
public sealed class MusicActivity : Activity
{
    public override string Id => "music";
    public override string Label => "음악";

    private static Room? Pick(CrewMember c, World w, DistanceField dist)
    {
        Room? best = null;
        float bestV = float.MinValue;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.OffLimits || !w.Music.CanPlay(r) || w.Music.TuneIn(r) != null) continue;
            float v = r.Type switch { RoomType.Lounge => 1f, RoomType.Mess => 0.6f, RoomType.Gym => 0.7f, RoomType.Galley => 0.4f, RoomType.Quarters => 0.3f, _ => -1f };
            if (v < 0f || MusicSystem.SpeakerCell(w.Ship, r) is not Cell at || !dist.Reachable(at)) continue;
            // 곁방에 자는 사람이 있으면 피한다 (벽 너머로 넘어간다)
            foreach (var o in w.Crew) if (o.Pose == Pose.Sleeping && o.Room is Room or && (or == r || w.Hearing.Pass(r, or) > 0.05f)) v -= 0.6f;
            v -= dist.Get(at) / 400f;
            if (v > bestV) { bestV = v; best = r; }
        }
        return bestV > 0f ? best : null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var m = w.Music;
        if (m.Mood >= MusicMood.Tension || m.Mood == MusicMood.Mourning || m.Playing.Count >= 2 || c.IsChild && c.Age < 8f) return (0f, "—");
        if (OnShift(c, w) || Bedtime(c, w)) return (0f, "—");
        long last = m.LastPlayed(c);
        if (last >= 0 && w.Tick - last < SimTime.Hours(20)) return (0f, "오늘은 이미 틀었다");
        float hour = Hour(w);
        var s = c.Schedule;
        float workEnd = s.WorkStart + s.WorkLength;
        float score = 0.03f + 0.22f * c.Needs.Stress + (c.Traits.Sociability - 0.5f) * 0.08f;
        if (SimTime.InWindow(hour, workEnd, SimTime.HoursFromTo(workEnd, s.SleepStart))) score += 0.1f;
        return (MathF.Max(0f, score), $"{MusicSystem.Genres[MusicSystem.Likes(c)]}가 듣고 싶다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Pick(c, w, dist) is not Room room || MusicSystem.SpeakerCell(w.Ship, room) is not Cell at) return null;
        byte genre = MusicSystem.Likes(c);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) =>
        {
            var t = world.Music.Start(room, genre, cm, w.Rng.Range(50f, 90f));
            if (t == null) return false;
            world.Log.Add(world.Tick, LogKind.Life, $"{room.Name} 스피커로 {MusicSystem.Genres[genre]}를 틀었다", cm.Id);
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Minutes(w.Rng.Range(30f, 60f)), Pose.Sitting, room.Center, minTicks: SimTime.Minutes(15))
        {
            DoneWhen = (cm, world) => world.Music.TuneIn(room) == null || world.Music.Mood >= MusicMood.Tension,
        });
        return new Job(this, "음악", toils) { LogText = $"{room.Name}에서 노래를 듣는다", TargetRoom = room };
    }
}
