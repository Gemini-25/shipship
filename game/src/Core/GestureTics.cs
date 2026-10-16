using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.2 버릇 · 흥얼거림 · 끊긴 대화.
//  · 버릇: 한 사람당 둘 (펜 돌리기 · 다리 떨기 · 휘파람 · 머리 만지기 · 손가락 꺾기 · 흥얼거림 · 톡톡 · 목 주무르기 · 깃 잡아당기기 · 기지개).
//    스트레스가 오래 쌓이면 하나가 바뀐다(손톱 물기 · 입술 깨물기 · 다리 떨기 · 머리 쥐어뜯기) — 함께 지내는 사람이 두어 번 보면 알아챈다
//    ("너 요즘 손톱 물더라") — 털어놓으면 덜고, 발뺌하면 서운하다. 마음이 풀린 지 한참 되면 원래 버릇으로 돌아온다.
//    휘파람 · 톡톡은 소리가 난다 — 깐깐한 사람은 그만하라 하고, 흥얼거림은 그 노래를 아는 사람이 따라 부른다 (모르는 사람은 듣다가 배운다).
//    주 컴퓨터는 카메라가 닿는 방에서 바뀐 버릇을 읽고 의무관에게 귀띔한다.
//  · 대화가 문 앞에서 끊기면(한 사람이 불려 가 문을 나섬) 다음에 마주쳤을 때 "아까 하던 얘기 말인데…"로 이어 간다 (반나절 지나면 잊는다).

public sealed partial class GestureSystem
{
    private static readonly Tic[] Calm = { Tic.PenSpin, Tic.LegBounce, Tic.Whistle, Tic.HairTouch, Tic.KnuckleCrack, Tic.Hum, Tic.TapFingers, Tic.RubNeck, Tic.CollarTug, Tic.Stretch };
    private static readonly Tic[] Stressed = { Tic.NailBite, Tic.LipBite, Tic.LegBounce, Tic.HairTouch };
    private static readonly string[] TopicPool = { "고향 바다 이야기", "다음 기항지 이야기", "지난번 고장 이야기", "그 책 결말", "어제 꾼 꿈", "창밖 그 별 이야기", "아이 이름 짓는 이야기", "새 당번표 이야기" };

    public static string TicName(Tic t) => t switch
    {
        Tic.PenSpin => "펜 돌리기", Tic.LegBounce => "다리 떨기", Tic.NailBite => "손톱 물기", Tic.Whistle => "휘파람", Tic.HairTouch => "머리 만지기",
        Tic.KnuckleCrack => "손가락 꺾기", Tic.Hum => "흥얼거림", Tic.TapFingers => "손가락 톡톡", Tic.RubNeck => "목 주무르기", Tic.CollarTug => "깃 잡아당기기",
        Tic.LipBite => "입술 깨물기", _ => "기지개",
    };

    private void PickTics(CrewMember c, MannerState s, uint h)
    {
        Tic a = Calm[h % (uint)Calm.Length];
        if (Life.Has(c, Habit.Hummer)) a = Tic.Hum;
        else if (Life.Has(c, Habit.Fidgety)) a = Tic.LegBounce;
        else if (Life.Has(c, Habit.Tinkerer)) a = Tic.PenSpin;
        Tic b = Calm[(h / 7 + 3) % (uint)Calm.Length];
        if (b == a) b = Calm[((h / 7 + 4) % (uint)Calm.Length)];
        s.Tics[0] = a; s.Tics[1] = b;
    }

    private void Tics(CrewMember c, MannerState s)
    {
        var w = _w;
        long now = w.Tick;
        float st = c.Needs.Stress;
        // 스트레스가 오래 쌓이면 버릇이 바뀐다 · 오래 풀려 있으면 돌아온다
        if (st >= 0.6f) { if (s.HighSince < 0) s.HighSince = now; s.LowSince = -1; }
        else if (st < 0.35f) { if (s.LowSince < 0) s.LowSince = now; s.HighSince = -1; }
        else { s.HighSince = -1; s.LowSince = -1; }
        if (s.StressTic == null && s.HighSince >= 0 && now - s.HighSince >= SimTime.Hours(2))
        {
            var t = Stressed[(int)((uint)(c.Id * 31 + w.Seed) % (uint)Stressed.Length)];
            if (t == s.Tics[0] || t == s.Tics[1]) t = t == Tic.NailBite ? Tic.LipBite : Tic.NailBite;
            s.StressTic = t; s.StressSince = now;
            Stats.StressTics++;
            s.SawTic.Clear();
            Life.Diary(w, c, $"요즘 자꾸 {TicDiary(t)}");
        }
        else if (s.StressTic != null && s.LowSince >= 0 && now - s.LowSince >= SimTime.Hours(12))
        {
            s.StressTic = null; s.StressSince = -1;
            Stats.CalmAgain++;
        }
        // 지금 버릇을 하는 중이면: 소리 · 남의 눈
        if (s.Ticcing(now))
        {
            var d = s.Doing;
            if (d == Tic.Hum) { w.Hearing.Emit(Noise.Hum, c, 0.25f, (byte)s.Tune); Stats.Hums++; }
            else if (d == Tic.Whistle) w.Hearing.Emit(Noise.Whistle, c, 0.3f, (byte)s.Tune);
            else if (d is Tic.TapFingers or Tic.PenSpin or Tic.KnuckleCrack || d == Tic.LegBounce && c.Pose == Pose.Sitting) w.Hearing.Emit(Noise.Tap, c, d == Tic.KnuckleCrack ? 0.16f : 0.12f, (byte)d);
            Watchers(c, s, d);
            return;
        }
        if (now < s.NextTic) return;
        // 할 만할 때만: 서서 · 앉아서 · 일하며 · 기다리며 (뛰거나 위급하면 아니다)
        bool idle = c.Pose is Pose.Sitting or Pose.Standing or Pose.Working && !c.IsMoving && c.Job?.Urgent != true && c.Suit == null;
        float perHour = (Life.Has(c, Habit.Fidgety) ? 4f : 2.2f) * (1f + 1.5f * st);
        s.NextTic = now + (int)(SimTime.Hours(1) / perHour * R.Range(0.5f, 1.5f));
        if (!idle) return;
        Tic pick = s.StressTic is Tic stt && R.Chance(0.7f) ? stt : s.Tics[R.Range(0, 2)];
        if (pick == Tic.Hum && c.Pose == Pose.Working && c.Job?.Order is { Urgency: >= 0.7f }) pick = s.Tics[0] == Tic.Hum ? s.Tics[1] : s.Tics[0];
        s.Doing = pick;
        s.TicUntil = now + (pick is Tic.Hum or Tic.Whistle ? SimTime.Minutes(R.Range(3f, 6f)) : SimTime.Minutes(R.Range(1f, 2.5f)));
        Stats.Tics++;
        Stats.ByTic[(int)pick]++;
        if (s.StressTic == null || pick != s.StressTic) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.003f); // 손이 바쁘면 조금 가라앉는다
        Notes.Add(new GestNote { Tick = now, Crew = c.Id, Mien = Mien.None, Other = 100 + (int)pick });
    }

    private static string TicDiary(Tic t) => t switch
    {
        Tic.NailBite => "손톱을 물게 된다", Tic.LipBite => "입술을 깨문다", Tic.LegBounce => "다리를 떤다", Tic.HairTouch => "머리를 쥐어뜯는다", _ => "손이 가만있지 않는다",
    };

    /// <summary>둘레 사람: 바뀐 버릇을 알아챔 · 소리 나는 버릇에 짜증 · 흥얼거림을 따라 부름 / 배움.</summary>
    private void Watchers(CrewMember c, MannerState s, Tic d)
    {
        var w = _w;
        long now = w.Tick;
        if (c.Room is not Room room) return;
        foreach (var o in w.Crew)
        {
            if (o == c || !o.IsAwake || o.Down || o.Dead || o.IsChild && o.Age < 6f || o.Room is not Room orr) continue;
            var os = Of(o);
            bool same = orr == room && (o.Position - c.Position).LengthSquared() < 49f;
            // 흥얼거림 · 휘파람: 들리면 (옆방도 열린 문 너머로)
            if (d is Tic.Hum or Tic.Whistle)
            {
                float lv = same ? 0.25f * w.Hearing.Sense(o, Noise.Hum) : w.Hearing.LevelOf(o, d == Tic.Hum ? Noise.Hum : Noise.Whistle, c.Id);
                if (lv < HearingSystem.Audible) continue;
                bool knows = os.Tune == s.Tune || os.TuneHeard.TryGetValue(s.Tune, out var heard) && heard >= 3 || o.Hobbies.Contains(Hobby.Singing) && os.TuneHeard.ContainsKey(s.Tune);
                if (knows && d == Tic.Hum && !os.Ticcing(now) && o.Job?.Urgent != true && (o.Job?.Order == null || o.Job.Order.Urgency < 0.6f) && os.M != Mien.SingAlong
                    && R.Chance(0.35f + 0.4f * o.Traits.Sociability - (Life.Has(o, Habit.Loner) ? 0.25f : 0f)))
                {
                    os.LookCrew = c.Id;
                    Set(o, os, Mien.SingAlong, (int)Math.Max(10, s.TicUntil - now), Pick(new[] { $"라라라~ {Tunes[s.Tune]} 그거지?", $"{Tunes[s.Tune]}! 나도 그거 알아", "라~ 랄라~" }), c.Id);
                    var cs = Of(c);
                    if (cs.M != Mien.Hum || now >= cs.MUntil) Set(c, cs, Mien.Hum, (int)Math.Max(10, s.TicUntil - now), "", o.Id);
                    w.Hearing.Emit(Noise.Sing, o, 0.35f, (byte)s.Tune);
                    o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.04f); c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.04f);
                    o.ChangeAffinity(c, 0.02f); c.ChangeAffinity(o, 0.02f);
                    o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.01f);
                    Stats.SingAlong++;
                    continue;
                }
                if (!knows)
                {
                    int n = os.TuneHeard.TryGetValue(s.Tune, out var h2) ? h2 + 1 : 1;
                    os.TuneHeard[s.Tune] = n;
                    if (n == 3) Stats.TuneLearned++;
                }
                // 깐깐한 사람: 일할 때 휘파람은 거슬린다
                if (same && d == Tic.Whistle && (Life.Has(o, Habit.Grumbler) || Life.Has(o, Habit.ShortTempered) || Life.Has(o, Habit.Serious)) && Annoy(o, os, c, s, "그 휘파람 좀…")) return;
                continue;
            }
            if (!same) continue;
            if (d is Tic.TapFingers or Tic.KnuckleCrack or Tic.PenSpin && o.Pose == Pose.Working && (Life.Has(o, Habit.Grumbler) || Life.Has(o, Habit.NeatFreak) || Life.Has(o, Habit.ShortTempered)))
            {
                if (Annoy(o, os, c, s, d == Tic.KnuckleCrack ? "손가락 꺾는 소리 좀 그만" : "톡톡거리지 좀 마")) return;
                continue;
            }
            // 바뀐 버릇을 알아챈다
            if (s.StressTic is not Tic stt || d != stt) continue;
            if (o.AffinityTo(c) < 0.1f && o.Partner != c.Id && o.Role != c.Role) continue;
            int seen = os.SawTic.TryGetValue(c.Id, out var sv) ? sv + 1 : 1;
            os.SawTic[c.Id] = seen;
            if (seen < 2 || os.SaidTic.TryGetValue(c.Id, out var said) && now - said < SimTime.Hours(48)) continue;
            os.SaidTic[c.Id] = now;
            os.LookCrew = c.Id;
            string line = stt switch
            {
                Tic.NailBite => Pick(new[] { "너 요즘 손톱 물더라", "손톱 그만 물어 — 무슨 일 있어?", "요즘 손톱이 남아나질 않네" }),
                Tic.LipBite => Pick(new[] { "입술 그만 깨물어 — 요즘 무슨 일 있어?", "너 요즘 입술을 자꾸 깨물더라" }),
                Tic.LegBounce => Pick(new[] { "너 요즘 다리를 계속 떨더라", "다리 좀… 요즘 많이 불안해?" }),
                _ => Pick(new[] { "요즘 머리를 자꾸 쥐어뜯네 — 괜찮아?", "너 요즘 머리 엄청 만진다" }),
            };
            Set(o, os, Mien.Notice, 25, line, c.Id);
            Stats.Noticed++;
            Life.Diary(w, o, $"{Ko.IGa(c.Name)} 요즘 {TicDiary(stt)}. 무슨 일이 있나");
            bool open = c.Traits.Sociability >= 0.4f && !Life.Has(c, Habit.ShortTempered) && c.AffinityTo(o) >= 0f;
            s.PendingReply = open ? Pick(new[] { "…티 나? 요즘 좀 그래", "응, 요즘 잠을 못 자서", "고마워, 말해 줘서" }) : Pick(new[] { "내가 언제?", "별거 아니야", "신경 쓰지 마" });
            s.PendingAt = now + 20; s.PendingTo = o.Id;
            s.TicUntil = now + 2; // 들켜서 손을 내린다
            if (open) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f); c.ChangeAffinity(o, 0.04f); o.ChangeAffinity(c, 0.03f); Stats.Opened++; }
            else { o.ChangeAffinity(c, -0.01f); Stats.Denied++; }
            return;
        }
        // 주 컴퓨터: 카메라가 닿는 방에서 바뀐 버릇이 잦으면 의무관에게
        if (s.StressTic is Tic tt && d == tt && room.DataLinked && now - s.TicAdvised > SimTime.Hours(72) && now - s.StressSince > SimTime.Hours(4))
        {
            var a = w.Automation;
            if (!a.Present || !a.CoreOnline) return;
            s.TicAdvised = now;
            if (a.Book.Add(ActKind.Advice, room, $"{c.Name} — 요즘 {TicName(tt)}가 잦다", "긴장이 오래간다", "의무 기록에 남김", "잠깐 이야기를 나눠 보세요", $"tic:{c.Id}", SimTime.Hours(72), 120f,
                    (world, act) => Peek(c)?.StressTic == null ? (1, "풀렸다") : null) != null)
                Stats.TicAdvice++;
        }
    }

    private bool Annoy(CrewMember o, MannerState os, CrewMember c, MannerState s, string line)
    {
        if (os.SaidTic.TryGetValue(-c.Id - 1, out var t) && _w.Tick - t < SimTime.Hours(6)) return false;
        os.SaidTic[-c.Id - 1] = _w.Tick;
        os.LookCrew = c.Id;
        Set(o, os, Mien.Notice, 15, line, c.Id);
        s.TicUntil = _w.Tick + 1;
        o.ChangeAffinity(c, -0.01f); c.ChangeAffinity(o, -0.008f);
        Stats.Annoyed++;
        return true;
    }

    // ───────────────────────────── 끊긴 대화 ─────────────────────────────

    private sealed class Chat { public int A, B; public long Since, Seen; public int RoomA = -1; }
    public sealed class ChatThread { public int A, B; public long Cut; public bool AtDoor; public string Topic = ""; public bool Done; }
    private readonly Dictionary<long, Chat> _chats = new();
    public List<ChatThread> Cuts { get; } = new();
    private readonly List<long> _ended = new();

    private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    private void Chats()
    {
        var w = _w;
        long now = w.Tick;
        foreach (var c in w.Crew)
        {
            if (c.TalkingTo is not CrewMember p || p.Dead || c.Id > p.Id && p.TalkingTo == c) continue;
            long k = PairKey(c.Id, p.Id);
            if (!_chats.TryGetValue(k, out var ch)) _chats[k] = ch = new Chat { A = Math.Min(c.Id, p.Id), B = Math.Max(c.Id, p.Id), Since = now, RoomA = c.Room?.Id ?? -1 };
            ch.Seen = now;
        }
        _ended.Clear();
        foreach (var (k, ch) in _chats) if (ch.Seen < now) _ended.Add(k);
        if (_ended.Count == 0) return;
        _ended.Sort();
        foreach (var k in _ended)
        {
            var ch = _chats[k];
            _chats.Remove(k);
            if (Person(ch.A) is not CrewMember a || Person(ch.B) is not CrewMember b || a.Dead || b.Dead) continue;
            long dur = now - ch.Since;
            bool apart = a.Room != b.Room || (a.Position - b.Position).LengthSquared() > 9f;
            if (dur >= SimTime.Minutes(12) || !apart || !a.IsAwake || !b.IsAwake) continue;
            // 한 사람이 불려 가 문을 나섰다 — 이야기가 끊겼다
            bool door = a.Room != b.Room || NearDoor(a) || NearDoor(b);
            var th = new ChatThread { A = a.Id, B = b.Id, Cut = now, AtDoor = door, Topic = TopicPool[R.Range(0, TopicPool.Length)] };
            if (R.Chance(0.3f)) th.Topic = $"{w.Relations.Favorite(a)} 이야기";
            Cuts.Add(th);
            Stats.Cut++;
            if (door) Stats.CutAtDoor++;
            var leaver = a.IsMoving ? a : b;
            var stay = leaver == a ? b : a;
            if (R.Chance(0.6f)) stay.Say(w, Persona.Say(stay, Pick(new[] { "어, 그럼 이따가!", "얘기 다 안 끝났는데…", "가 봐 — 나중에 마저 해" })));
            Notes.Add(new GestNote { Tick = now, Crew = leaver.Id, Mien = Mien.None, Other = stay.Id, Line = $"끊김:{th.Topic}" });
        }
        if (_chats.Count > 200) _chats.Clear();
    }

    private bool NearDoor(CrewMember c)
    {
        foreach (var d in Cell.Dirs8) if (_w.Ship.DoorAt(c.Cell + d) != null) return true;
        return _w.Ship.DoorAt(c.Cell) != null;
    }

    private void ResumeTalks()
    {
        var w = _w;
        long now = w.Tick;
        for (int i = Cuts.Count - 1; i >= 0; i--)
        {
            var th = Cuts[i];
            if (th.Done) { if (now - th.Cut > SimTime.Hours(24)) Cuts.RemoveAt(i); continue; }
            if (now - th.Cut > SimTime.Hours(12)) { th.Done = true; Stats.Forgot++; continue; }
            if (now - th.Cut < SimTime.Minutes(10)) continue;
            if (Person(th.A) is not CrewMember a || Person(th.B) is not CrewMember b || a.Dead || b.Dead) { th.Done = true; continue; }
            if (a.Room != b.Room || !a.IsAwake || !b.IsAwake || a.Down || b.Down || (a.Position - b.Position).LengthSquared() > 25f) continue;
            if (a.Job?.Urgent == true || b.Job?.Urgent == true || Crisis.Level(w) >= CrisisLevel.Emergency) continue;
            // 덜 바쁜 사람이 먼저 꺼낸다
            var sp = a.Pose == Pose.Working && b.Pose != Pose.Working ? b : a;
            var ot = sp == a ? b : a;
            var ss = Of(sp);
            ss.LookCrew = ot.Id;
            Set(sp, ss, Mien.Resume, 40, Pick(new[] { $"아까 하던 얘기 말인데 — {th.Topic}…", $"참, 아까 그 {th.Topic} 말이야", "아까 하다 만 얘기 있잖아…" }), ot.Id);
            var os = Of(ot);
            os.LookCrew = sp.Id;
            os.PendingReply = Pick(new[] { "아, 맞다! 그래서 어떻게 됐어?", "그거 끝까지 들어야지", "어디까지 했더라?" });
            os.PendingAt = now + 15; os.PendingTo = sp.Id;
            sp.Needs.Social = MathF.Min(1f, sp.Needs.Social + 0.03f); ot.Needs.Social = MathF.Min(1f, ot.Needs.Social + 0.03f);
            sp.ChangeAffinity(ot, 0.015f); ot.ChangeAffinity(sp, 0.015f);
            th.Done = true;
            Stats.Resumed++;
        }
    }
}
