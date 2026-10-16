using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.8 말이 지금을 담는다.
//  · 사람마다 말투가 다르다: 차분한 사람 · 걱정 많은 사람 · 농담하는 사람 · 투덜이 · 보통 (성격 · 버릇).
//  · 말수가 적은 사람은 몸짓만 한다 (대답은 한다).
//  · 한 말은 기억한다: 최근에 내가 한 말 · 이 방에서 방금 남이 한 말은 되도록 다시 하지 않는다.
//  · 지금 이야기를 붙인다: 우주 날씨 예보 · 아침 브리핑 · 회의 결정 · 재판 · 최근 사고 · 들킨 일(밀주 소동 …) · 배터리 ·
//    아까 누가 어떻게 버텼는지 (같이 겪은 사람이 기억한다). 같은 이야기를 같은 사람이 되풀이하지 않는다.
//  · 수다 떠는 두 사람(ChatActivity)은 몇 분마다 지금 이야기를 주고받는다 — 한 사람이 꺼내면 상대가 제 말투로 받는다.

public sealed partial class ReactSystem
{
    private const int MemLines = 14, MemKeys = 10;
    private readonly Dictionary<int, List<(long t, string line)>> _roomSaid = new();
    private readonly HashSet<string> _usedTopics = new();
    private readonly Dictionary<long, long> _pairNext = new();
    private List<Topic> _topics = new();
    private long _topicsAt = -1;

    /// <summary>지금 이야기 하나: 열쇠(같은 사람이 되풀이하지 않게) · 어울리는 반응 · 사람마다 다른 한 줄.</summary>
    private sealed record Topic(string Key, int Mask, long Tick, Func<CrewMember, string?> Line, Func<CrewMember, CrewMember, string?>? Reply = null);

    private static int M(params Stir[] ks) { int m = 0; foreach (var k in ks) m |= 1 << (int)k; return m; }

    /// <summary>말투: 0 차분 · 1 걱정 · 2 농담 · 3 투덜 · 4 보통.</summary>
    private static int Mood(CrewMember c)
    {
        if (Life.Has(c, Habit.Joker) || Life.Has(c, Habit.Prankster) || Life.Has(c, Habit.Cheerful)) return 2;
        if (Life.Has(c, Habit.Grumbler) || Life.Has(c, Habit.ShortTempered) || Life.Has(c, Habit.Pessimist)) return 3;
        if (Life.Has(c, Habit.Worrier) || c.Traits.Calm < 0.32f) return 1;
        if (Life.Has(c, Habit.Serious) || Life.Has(c, Habit.Methodical) || Life.Has(c, Habit.Patient) || c.Traits.Calm > 0.68f) return 0;
        return 4;
    }

    // ───────────────────────────── 말하기 ─────────────────────────────

    internal void Speak(CrewMember c, ReactState s, Stir k, string[] pool, Room? room, string topic, bool quiet = false, string[]? wayLine = null, bool reply = false)
    {
        var w = _w;
        float talk = reply ? 1f : 0.55f + 0.4f * c.Traits.Sociability + (Life.Has(c, Habit.Talker) ? 0.2f : 0f) - (Life.Has(c, Habit.Loner) ? 0.25f : 0f) - (quiet ? 0.15f : 0f);
        if (!c.IsAwake || c.Down || pool.Length == 0 && (wayLine == null || wayLine.Length == 0) || !R.Chance(Math.Clamp(talk, 0.25f, 1f)))
        {
            Stats.Silent++;
            Notes.Add(new ReactNote(w.Tick, c.Id, k, s.Tmp ?? s.Way ?? "", s.G, "", room?.Id ?? -1));
            return;
        }
        var first = wayLine != null && wayLine.Length > 0 && (pool.Length == 0 || R.Chance(0.7f)) ? wayLine : pool;
        var second = first == pool ? wayLine : pool;
        string? line = Fresh(s, first, room) ?? (second != null ? Fresh(s, second, room) : null);
        bool repeat = false;
        if (line == null)
        {
            // 할 말을 다 했다: 반은 입을 다물고, 반은 같은 말을 한다
            if (!reply && R.Chance(0.5f)) { Stats.Silent++; return; }
            line = first.Length > 0 ? first[R.Range(0, first.Length)] : second![R.Range(0, second!.Length)];
            repeat = true;
        }
        if (!reply && TopicFor(c, s, k) is Topic tp && tp.Line(c) is string tl && !HeardHere(room, tl))
        {
            line = line.EndsWith('?') || line.EndsWith('!') || line.EndsWith('…') ? $"{line} {tl}" : line.Contains(" — ") || tl.Contains(" — ") ? $"{line}. {tl}" : $"{line} — {tl}";
            Took(s, tp);
            Stats.Topical++;
        }
        Say(c, s, k, line, room, repeat);
    }

    private void Say(CrewMember c, ReactState s, Stir k, string line, Room? room, bool repeat)
    {
        var w = _w;
        s.Texts.Add(line);
        if (s.Texts.Count > MemLines) s.Texts.RemoveAt(0);
        if (room != null)
        {
            if (!_roomSaid.TryGetValue(room.Id, out var rl)) _roomSaid[room.Id] = rl = new List<(long, string)>();
            rl.Add((w.Tick, line));
            if (rl.Count > 8) rl.RemoveAt(0);
        }
        c.Say(w, Persona.Say(c, line));
        Stats.Lines++;
        if (repeat) Stats.Repeats++;
        Notes.Add(new ReactNote(w.Tick, c.Id, k, s.Tmp ?? s.Way ?? "", s.G, line, room?.Id ?? -1));
    }

    /// <summary>내가 최근에 하지 않았고, 이 방에서 방금 남이 하지 않은 말.</summary>
    private string? Fresh(ReactState s, string[] pool, Room? room)
    {
        if (pool.Length == 0) return null;
        List<(long t, string line)>? rl = null;
        if (room != null) _roomSaid.TryGetValue(room.Id, out rl);
        int start = R.Range(0, pool.Length);
        for (int i = 0; i < pool.Length; i++)
        {
            var x = pool[(start + i) % pool.Length];
            if (s.Texts.Contains(x)) continue;
            bool heard = false;
            if (rl != null) foreach (var (t, l) in rl) if (l == x && _w.Tick - t < SimTime.Minutes(30)) { heard = true; break; }
            if (!heard) return x;
        }
        return null;
    }

    /// <summary>회의 · 안건 이름을 사람이 입에 올리는 꼴로: "구조: 구조자 안전부터 → 무조건" → "구조자 안전부터".</summary>
    private static string? Spoken(string title)
    {
        string t = title;
        int i = t.IndexOf(':');
        if (i >= 0) t = t[(i + 1)..];
        int j = t.IndexOf('→');
        if (j >= 0) t = t[..j];
        int k = t.IndexOf('(');
        if (k > 0) t = t[..k];
        t = t.Trim();
        return t.Length < 2 ? null : t;
    }

    /// <summary>방금 이 방에서 누가 같은 이야기를 꺼냈나 (같은 말을 줄줄이 하지 않는다).</summary>
    private bool HeardHere(Room? room, string tl)
    {
        if (room == null || !_roomSaid.TryGetValue(room.Id, out var rl)) return false;
        foreach (var (t, l) in rl) if (_w.Tick - t < SimTime.Minutes(30) && l.EndsWith(tl)) return true;
        return false;
    }

    private void Took(ReactState s, Topic tp)
    {
        s.Keys.Add(tp.Key);
        if (s.Keys.Count > MemKeys) s.Keys.RemoveAt(0);
        if (_usedTopics.Add(tp.Key)) Stats.Topics++;
    }

    /// <summary>이 반응에 붙일 지금 이야기 (없으면 null): 성격이 말 많을수록 · 큰 일일수록 잘 붙인다.</summary>
    private Topic? TopicFor(CrewMember c, ReactState s, Stir k)
    {
        if (k is Stir.Glass or Stir.Wet or Stir.Cry or Stir.Novel or Stir.Odd) return null;
        float p = k == Stir.Chat ? 1f : 0.3f + 0.25f * c.Traits.Sociability + (Life.Has(c, Habit.Talker) ? 0.15f : 0f);
        if (!R.Chance(p)) return null;
        var list = Topics();
        // 가장 최근 이야기 셋 가운데 하나 (맨 앞이 잘 나오지만 늘 같은 이야기만 하지는 않는다)
        Topic? a = null, b = null, d = null;
        foreach (var tp in list)
        {
            if ((tp.Mask & (1 << (int)k)) == 0 || s.Keys.Contains(tp.Key) || tp.Line(c) == null) continue;
            if (a == null || tp.Tick > a.Tick) { d = b; b = a; a = tp; }
            else if (b == null || tp.Tick > b.Tick) { d = b; b = tp; }
            else if (d == null || tp.Tick > d.Tick) d = tp;
        }
        if (a == null || b == null) return a;
        float r = R.Float();
        return r < 0.5f ? a : r < 0.8f || d == null ? b : d;
    }

    // ───────────────────────────── 지금 이야기 ─────────────────────────────

    /// <summary>배 전체의 지금 이야기 (10분마다 다시 모은다).</summary>
    private List<Topic> Topics()
    {
        var w = _w;
        if (_topicsAt >= 0 && w.Tick - _topicsAt < SimTime.Minutes(10)) return _topics;
        _topicsAt = w.Tick;
        var o = new List<Topic>();
        long day2 = SimTime.TicksPerDay * 2L;
        int all = M(Stir.Chat, Stir.Voice);
        // 우주 날씨 예보
        if (w.Automation.Present && !ShipMate.Off && w.Automation.Mate.LastSky is SkyForecast f && w.Tick - f.Tick < SimTime.Hours(12))
        {
            int ps = (int)MathF.Round(f.PStorm * 100), pm = (int)MathF.Round(f.PShower * 100);
            if (f.PStorm >= 0.25f)
                o.Add(new Topic($"sky:{f.Tick}", all | M(Stir.Dark, Stir.Shake, Stir.Alarm, Stir.Cold), f.Tick,
                    c => Mood(c) switch
                    {
                        1 => $"예보에 태양 폭풍 {ps}%라던데… 그것 때문인가",
                        2 => $"태양 폭풍 {ps}%래 — 오늘은 햇볕 쬐러 나가지 말자",
                        3 => $"폭풍 {ps}%라더니 이거야?",
                        _ => $"오늘 태양 폭풍 {ps}% 예보였지",
                    },
                    (a, b) => Mood(a) == 1 ? "대피소 점검은 해 뒀대" : "선외 작업은 미뤘대 — 다행이지"));
            else if (f.PShower >= 0.25f)
                o.Add(new Topic($"sky:{f.Tick}", all | M(Stir.Shake, Stir.Alarm, Stir.Sound), f.Tick,
                    c => Mood(c) == 2 ? $"운석우 {pm}%래 — 별똥별 구경이나 할까" : $"예보에 운석우 {pm}%라던데",
                    (a, b) => Mood(a) == 1 ? "창가엔 가지 말자" : "외판이 버텨 주겠지"));
            else
                o.Add(new Topic($"sky:{f.Tick}", all, f.Tick, c => Mood(c) == 3 ? "날씨가 맑으면 뭐 해, 나갈 일도 없는데" : "오늘 바깥 날씨는 맑대",
                    (a, b) => Mood(a) == 2 ? "산책이라도 나갈까 — 농담이야" : "조용한 날이 제일이지"));
            // 어제 예보가 빗나갔다
            for (int i = w.Automation.Mate.Forecasts.Count - 2; i >= 0 && i >= w.Automation.Mate.Forecasts.Count - 6; i--)
            {
                var pf = w.Automation.Mate.Forecasts[i];
                if (!pf.Judged || pf.Hit || w.Tick - pf.JudgedAt > SimTime.Hours(20)) continue;
                o.Add(new Topic($"skymiss:{pf.Tick}", all, pf.JudgedAt, c => pf.Held ? "어제 예보 때문에 선외 작업만 미뤘잖아 — 아무 일도 없었는데" : "어제 예보는 빗나갔더라",
                    (a, b) => Mood(a) == 0 ? "그래도 조심해서 나쁠 건 없지" : "컴퓨터도 틀릴 때가 있네"));
                break;
            }
        }
        // 아침 브리핑
        if (w.Automation.Present && !ShipMate.Off && w.Automation.Mate.LastBriefing is Briefing bf && w.Tick - bf.Tick < SimTime.Hours(14))
            for (int i = 0; i < bf.Lines.Count; i++)
            {
                var (kind, text) = bf.Lines[i];
                if (kind == "날씨" || text.Length > 46) continue;
                // 아무 일 없다던 방송: 일이 터졌을 때만 꺼낸다 ("손볼 데 없다더니")
                if (text.Contains("없음"))
                {
                    if (kind is not ("정비" or "주의")) continue;
                    bool fix = kind == "정비";
                    o.Add(new Topic($"brief:{bf.Day}:{i}", fix ? M(Stir.Sound, Stir.Shake) : M(Stir.Alarm, Stir.Smoke, Stir.Dark), bf.Tick,
                        c => Mood(c) switch
                        {
                            3 => fix ? "아침엔 손볼 데 하나 없다더니, 이거 봐" : "아침엔 조심할 데 없다더니, 이거 봐",
                            2 => fix ? "아침 방송은 손볼 데 없다던데 — 방송이 거짓말을 하네" : "아침 방송은 오늘 조용하댔는데",
                            1 => fix ? "아침엔 정비할 데 없다고 했잖아…" : "아침엔 아무 일 없을 거라더니…",
                            _ => fix ? "아침 방송엔 정비할 데가 없었는데" : "아침 방송엔 조심할 데가 없었는데",
                        },
                        (a, b) => Mood(a) == 0 ? "방송도 모르는 게 있지" : "그러게 말이야"));
                    continue;
                }
                int mask = all | (kind == "정비" ? M(Stir.Sound, Stir.Shake) : kind == "주의" ? M(Stir.Alarm, Stir.Smoke) : 0);
                string t = text;
                o.Add(new Topic($"brief:{bf.Day}:{i}", mask, bf.Tick, c => Mood(c) == 3 ? $"아침 방송에서 그러던데 — {t}. 또 일이네" : $"아침 방송에서 그러던데 — {t}",
                    (a, b) => Mood(a) == 0 ? "그럼 오늘 안에 해 두자" : "나도 들었어"));
            }
        // 회의 결정
        foreach (var d in w.Meetings.Decisions)
        {
            if (w.Tick - d.Tick > day2 || Spoken(d.Title) is not string tt) continue;
            string ro = Ko.EuRo(tt)[tt.Length..];
            bool power = tt.Contains("전기") || tt.Contains("전력") || tt.Contains("조명") || tt.Contains("배터리") || tt.Contains("절전");
            bool heat = tt.Contains("온도") || tt.Contains("난방") || tt.Contains("냉방") || tt.Contains("냉각");
            int mask = all | (power ? M(Stir.Dark, Stir.Cold, Stir.Heat) : 0) | (heat ? M(Stir.Cold, Stir.Heat) : 0);
            var yes = d.Yes; var no = d.No;
            o.Add(new Topic($"dec:{d.Tick}", mask, d.Tick,
                c => no.Contains(c.Id) ? $"회의에서 '{tt}' 정한 거, 난 지금도 반대야" : yes.Contains(c.Id) ? $"회의에서 '{tt}'{ro} 정했잖아 — 잘한 것 같아" : $"회의에서 '{tt}'{ro} 정했대",
                (a, b) => no.Contains(a.Id) ? "난 반대했었어" : yes.Contains(a.Id) ? "정했으면 지켜야지" : "난 그 회의에 없었어"));
        }
        // 재판 · 서명 안건
        foreach (var mo in w.Motions.All)
        {
            if (mo.Decided < 0 || w.Tick - mo.Decided > day2) continue;
            var target = mo.Target >= 0 ? Crew(mo.Target) : null;
            if (mo.Sitting == SittingKind.Trial && target != null)
            {
                string v = mo.Verdict switch { Penalty.Forgive => "없던 일로 했대", Penalty.Warning => "경고로 끝났대", Penalty.ExtraDuty => "벌 근무래", Penalty.RationCut => "배급이 줄었대", _ => "특권을 뺐겼대" };
                string tn = target.Name; int tid = target.Id;
                o.Add(new Topic($"trial:{mo.Id}", all | M(Stir.Odd), mo.Decided,
                    c => c.Id == tid ? "재판 얘기는 그만하자" : Mood(c) == 3 ? $"{tn} 재판 봤어? {v} — 너무 가볍지" : $"{tn} 재판 — {v}",
                    (a, b) => a.Id == tid ? "…그 얘긴 그만해" : a.AffinityTo(target) > 0.3f ? $"{tn}도 힘들었을 거야" : "자업자득이지"));
            }
            else if (Spoken(mo.Title) is string tt && tt.Length < 30)
            {
                bool pass = mo.Passed; int who = mo.Proposer;
                o.Add(new Topic($"mo:{mo.Id}", all, mo.Decided,
                    c => c.Id == who ? (pass ? $"'{tt}' 통과됐어 — 서명해 준 사람들 덕이야" : $"'{tt}'는 떨어졌어… 다음에 다시 내야지") : pass ? $"'{tt}' 통과됐대" : $"'{tt}'는 떨어졌대",
                    (a, b) => pass ? "이제 좀 달라지려나" : "아쉽게 됐네"));
            }
        }
        // 최근 사고
        foreach (var ep in w.History.Episodes)
        {
            if (ep.End < 0 || w.Tick - ep.End > day2 || ep.Cause.Length == 0 || ep.Cause.Length > 28) continue;
            string rn = ep.RoomId >= 0 && ep.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[ep.RoomId].Name : "";
            string cause = ep.Cause;
            bool fire = cause.Contains("불") || cause.Contains("화재");
            bool power = cause.Contains("정전") || cause.Contains("전기") || cause.Contains("차단");
            int mask = all | (fire ? M(Stir.Smoke, Stir.Smell, Stir.Alarm) : power ? M(Stir.Dark, Stir.Alarm) : M(Stir.Alarm, Stir.Shake));
            var resp = ep.Responders;
            string where = rn.Length > 0 ? $"{rn} " : "";
            o.Add(new Topic($"ep:{ep.Id}", mask, ep.End,
                c => resp.ContainsKey(c.Id) ? (Mood(c) == 1 ? $"{where}{cause} 때 생각나 — 아직도 손이 떨려" : $"{where}{cause} 때 내가 거기 있었잖아") :
                     Mood(c) == 2 ? $"{where}{cause} 뒤로 다들 예민하더라 — 나만 빼고" : $"{where}{cause} 뒤로 다들 예민해",
                (a, b) => resp.ContainsKey(a.Id) ? "그때 정신없었지" : resp.ContainsKey(b.Id) ? "넌 거기 있었잖아 — 고생했어" : "다시는 없었으면 좋겠다"));
        }
        // 들킨 일 (밀주 소동 · 장난 · 몰래 하던 일)
        foreach (var sc in w.Schemes.All)
        {
            if (sc.FoundAt < 0 || w.Tick - sc.FoundAt > day2) continue;
            var lead = Crew(sc.Lead);
            if (lead == null) continue;
            string nm = sc.Spec.Name, ln = lead.Name; int lid = lead.Id;
            bool booze = sc.Spec.Key == "moonshine";
            var scc = sc;
            o.Add(new Topic($"sch:{sc.Id}", all | M(Stir.Odd, Stir.Smell), sc.FoundAt,
                c => !scc.Knew(c.Id) ? null : c.Id == lid ? (booze ? "밀주 얘기는 이제 그만 좀 해" : $"{nm}… 그 얘기 꺼내지 마") :
                     booze ? (Mood(c) == 2 ? $"{ln} 밀주 소동 들었어? 한 잔쯤 남았으려나" : $"{ln} 밀주 소동 들었어?") : $"{ln}이 {nm} 하다 들켰다며",
                (a, b) => a.Id == lid ? "…다 지난 일이야" : Mood(a) == 3 ? "규칙은 규칙이지" : Mood(a) == 2 ? "난 재밌던데" : "그럴 수도 있지 뭐"));
        }
        // 배 상태: 전기
        if (w.Power.BatteryPercent < 0.35f || w.Power.DeficitSince >= 0)
        {
            int pct = (int)MathF.Round(w.Power.BatteryPercent * 100);
            o.Add(new Topic($"pow:{w.Day}:{pct / 10}", all | M(Stir.Dark, Stir.Cold, Stir.Heat), w.Tick,
                c => Mood(c) == 1 ? $"배터리가 {pct}%밖에 안 남았대 — 이러다 다 꺼지는 거 아냐" : w.Power.DeficitSince >= 0 ? "전기가 모자라서 그래 — 급한 데부터 돌린대" : $"배터리 {pct}%래",
                (a, b) => Mood(a) == 0 ? "급한 데부터 돌리겠지" : "아껴 써야겠다"));
        }
        // 배 상태: 고장 난 기계가 여럿
        int broken = 0;
        foreach (var m in w.Ship.Machines) if (m.Faults.Count > 0) broken++;
        if (broken >= 2)
        {
            int nb = broken;
            o.Add(new Topic($"brk:{w.Day}:{nb}", all | M(Stir.Sound, Stir.Shake, Stir.Dark), w.Tick - SimTime.Hours(2),
                c => c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician ? (Mood(c) == 3 ? $"고장 난 게 {nb}개야 — 손이 열 개라도 모자라" : $"손볼 게 {nb}개 밀려 있어") : $"요즘 고장 난 게 {nb}개라던데",
                (a, b) => a.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician ? "하나씩 하면 돼" : Mood(a) == 1 ? "이 배 괜찮은 거 맞지?" : "정비하는 사람들 고생이네"));
        }
        // 관계: 요즘 다툰 사람 · 붙어 다니는 둘 (오래된 이야기라 다른 이야깃거리가 없을 때 나온다)
        int qn = 0, pn = 0;
        foreach (var x in w.Crew)
        {
            if (x.Dead || x.Away) continue;
            if (qn < 3 && w.Tick - x.Quarrel < SimTime.TicksPerDay && x.Quarrel > 0)
            {
                string xn = x.Name; int xid = x.Id; var xx = x;
                o.Add(new Topic($"qr:{xid}:{x.Quarrel}", all | M(Stir.Odd), x.Quarrel,
                    c => c.Id == xid ? (Mood(c) == 3 ? "어제 일은 생각하기도 싫어" : "어제 말다툼은 내가 좀 심했어") :
                         c.AffinityTo(xx) > 0.3f ? $"{Ko.IGa(xn)} 요즘 누구랑 다퉜다던데 — 괜찮나 몰라" : Mood(c) == 3 ? $"{Ko.IGa(xn)} 또 싸웠대" : $"{Ko.IGa(xn)} 누구랑 말다툼했다며",
                    (a, b) => a.Id == xid ? "…다 풀었어" : a.AffinityTo(xx) > 0.3f ? "내가 한번 얘기해 볼게" : "가만 둬, 알아서 풀겠지"));
                qn++;
            }
            if (pn < 2 && x.Partner is int pid && pid > x.Id && Crew(pid) is CrewMember y && !y.Dead)
            {
                string an = x.Name, bn = y.Name; int aid = x.Id, bid = y.Id;
                o.Add(new Topic($"pair:{aid}:{bid}:{w.Day}", all, w.Tick - SimTime.Hours(20),
                    c => c.Id == aid || c.Id == bid ? null : Mood(c) == 2 ? $"{Ko.WaGwa(an)} {bn} 또 붙어 있더라 — 보기 좋네" : Mood(c) == 3 ? $"{Ko.WaGwa(an)} {bn}, 일할 때는 좀 떨어져 있지" : $"{Ko.WaGwa(an)} {bn} 요즘 사이좋더라",
                    (a, b) => a.Id == aid || a.Id == bid ? "…우리 얘기야?" : Mood(a) == 3 ? "일이나 하지" : "부럽다"));
                pn++;
            }
        }
        // 아까 누가 어떻게 버텼나 (같이 겪은 사람의 기억)
        int seen = 0;
        for (int i = Notes.Count - 1; i >= 0 && seen < 6; i--)
        {
            var n = Notes[i];
            if (w.Tick - n.Tick > SimTime.Hours(12)) break;
            if (n.Stir is not (Stir.Dark or Stir.Cold or Stir.Heat) || WayDesc(n.Way, n.Stir) is not string wd || Crew(n.Crew) is not CrewMember who) continue;
            seen++;
            string when = n.Stir == Stir.Dark ? "아까 캄캄할 때" : n.Stir == Stir.Cold ? "아까 추울 때" : "아까 더울 때";
            int wid = who.Id, rid = n.Room;
            string wn = who.Name, way = n.Way;
            o.Add(new Topic($"mem:{wid}:{way}:{n.Stir}", M(Stir.Chat) | 1 << (int)n.Stir, n.Tick,
                c => c.Id == wid || !WasThere(c.Id, rid, n.Tick) ? null : $"{when} {Ko.EunNeun(wn)} {wd}",
                (a, b) => a.Id == wid ? WayDefend(way) : Mood(a) == 2 ? "역시 그 사람답네" : "나도 봤어"));
        }
        _topics = o;
        return o;
    }

    /// <summary>그때 그 방에서 같이 겪었나 (그 무렵 같은 방에서 반응한 기록).</summary>
    private bool WasThere(int crew, int room, long tick)
    {
        for (int i = Notes.Count - 1; i >= 0; i--)
        {
            var n = Notes[i];
            if (n.Tick < tick - SimTime.Minutes(40)) break;
            if (n.Crew == crew && n.Room == room && n.Tick <= tick + SimTime.Minutes(40)) return true;
        }
        return false;
    }

    private static string? WayDesc(string way, Stir k) => way switch
    {
        "torch" => "손전등부터 켜더라",
        "lamp" => "창고에서 작업등을 끌고 왔어",
        "fix" => "전등을 직접 손보더라",
        "breaker" => "곧장 차단기 보러 가더라",
        "window" => "창가로 가서 별빛에 기대 있더라",
        "screen" => "콘솔 화면 앞에 붙어 있더라",
        "wrist" => "손목 단말 불빛으로 버티더라",
        "follow" => "남의 손전등 옆에 꼭 붙어 다니더라",
        "feel" => "벽을 짚고 다니더라",
        "wait" => "꼼짝도 안 하고 서 있더라",
        "blanket" => "담요를 둘둘 말고 있더라",
        "heater" => "히터 앞에서 안 떠나더라",
        "huddle" => "누구 옆에 딱 붙어 있더라",
        "jog" => "제자리에서 뛰더라",
        "cup" => k == Stir.Cold ? "뜨거운 차부터 타더라" : "찬물을 연거푸 마시더라",
        "jacket" => "겉옷부터 벗어 던지더라",
        "fan" => "선풍기 앞에서 안 떠나더라",
        "fanself" => "손부채질만 하더라",
        "warm_room" => "따뜻한 방으로 옮기더라",
        "cool_room" => "시원한 방으로 피하더라",
        _ => null,
    };

    private static string WayDefend(string way) => way switch
    {
        "wait" or "follow" => "…캄캄한 거 싫단 말이야",
        "jog" => "뛰니까 금방 따뜻해지던데",
        "fix" or "lamp" or "torch" => "누군가는 불을 켜야지",
        "huddle" => "붙어 있으면 따뜻하잖아",
        _ => "그게 제일 빨랐어",
    };

    // ───────────────────────────── 수다: 지금 이야기를 주고받는다 ─────────────────────────────

    private void Chat()
    {
        var w = _w;
        if (w.Tick % 30 != 0) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.TalkingTo is not CrewMember o || c.Id > o.Id || o.TalkingTo != c || c.Job?.Activity is not ChatActivity || !c.IsAwake || !o.IsAwake) continue;
            if ((o.Position - c.Position).LengthSquared() > 7f) continue;
            long pk = c.Id * 4096L + o.Id;
            if (_pairNext.TryGetValue(pk, out long nx) && w.Tick < nx) continue;
            _pairNext[pk] = w.Tick + SimTime.Minutes(20) + (c.Id * 17 + o.Id * 5) % SimTime.Minutes(8);
            // 먼저 꺼내는 사람: 말 많은 쪽
            var (a, b) = c.Traits.Sociability + (Life.Has(c, Habit.Talker) ? 0.3f : 0f) >= o.Traits.Sociability + (Life.Has(o, Habit.Talker) ? 0.3f : 0f) ? (c, o) : (o, c);
            var sa = Of(a);
            if (TopicFor(a, sa, Stir.Chat) is not Topic tp || tp.Line(a) is not string line) continue;
            Took(sa, tp);
            Stats.Chats++;
            Stats.ByStir[(int)Stir.Chat]++;
            Stats.Topical++;
            Gest(sa, Gesture.Talk, Short);
            sa.LookAt = b.Position; sa.LookCrew = b.Id;
            Say(a, sa, Stir.Chat, line, a.Room, false);
            var sb = Of(b);
            Took(sb, tp);
            string rep = tp.Reply?.Invoke(b, a) ?? ChatReply(b);
            Gest(sb, Mood(b) == 2 ? Gesture.Shrug : Gesture.Nod, Short);
            sb.LookAt = a.Position; sb.LookCrew = a.Id;
            Say(b, sb, Stir.Chat, rep, b.Room, false);
            Stats.Replies++;
            a.ChangeAffinity(b, 0.01f);
            b.ChangeAffinity(a, 0.01f);
        }
        if (_pairNext.Count > 400) _pairNext.Clear();
    }

    private static string ChatReply(CrewMember b) => Mood(b) switch
    {
        0 => "그러게, 두고 봐야지",
        1 => "난 그 얘기 들으면 잠이 안 와",
        2 => "하하, 그러게 말이야",
        3 => "그러니까 내 말이",
        _ => "응, 나도 들었어",
    };

    // ───────────────────────────── 반응마다 말 ─────────────────────────────

    private static readonly Dictionary<Stir, string[][]> Table = new()
    {
        [Stir.Dark] = new[]
        {
            new[] { "불이 나갔네", "정전이다 — 다들 그 자리에", "캄캄하다. 침착하자" },
            new[] { "어, 어? 왜 꺼져?", "아무것도 안 보여…", "누구 거기 있어?" },
            new[] { "누가 불 껐어? 장난치지 마", "이제 별 보기 딱 좋네", "숨바꼭질하기 좋겠다" },
            new[] { "또 나갔어?", "아 진짜, 하필 지금", "이 배는 하루도 조용할 날이 없네" },
            new[] { "불이 나갔다", "캄캄하네", "뭐야, 정전이야?" },
        },
        [Stir.Cold] = new[]
        {
            new[] { "온도가 꽤 떨어졌네", "춥다 — 뭐라도 걸쳐야겠다", "공조가 밀리나 보다" },
            new[] { "손이 곱아…", "이러다 감기 걸리겠어", "왜 이렇게 추워졌지?" },
            new[] { "입김 나온다 — 겨울 왔네", "냉장고 안에 들어온 줄", "펭귄 되겠다" },
            new[] { "추워 죽겠네", "난방 누가 줄였어?", "또 이래" },
            new[] { "으, 춥다", "쌀쌀하네", "여기 왜 이렇게 추워" },
        },
        [Stir.Heat] = new[]
        {
            new[] { "온도가 꽤 올랐네", "덥다 — 물 좀 마셔야겠다", "냉각이 밀리나 보다" },
            new[] { "숨이 막혀…", "이러다 쓰러지겠어", "왜 이렇게 더워졌지?" },
            new[] { "찜질방 개장했네", "여기서 계란 익겠다", "사우나 공짜네" },
            new[] { "더워 죽겠네", "냉방 누가 껐어?", "땀이 줄줄 흐른다" },
            new[] { "덥다", "후, 덥네", "여기 왜 이렇게 더워" },
        },
        [Stir.Smoke] = new[]
        {
            new[] { "연기다 — 어디서 나는지 보자", "환기가 안 되나 보다", "입 가려" },
            new[] { "연기…! 불 난 거 아냐?", "콜록 — 숨 막혀", "빨리 나가자" },
            new[] { "누가 생선 태웠어?", "콜록 — 매캐하다", "바비큐 파티라도 했나" },
            new[] { "또 뭘 태운 거야", "콜록, 환기 좀 해", "냄새 지독하네" },
            new[] { "연기가 차 있네", "콜록", "매캐하다" },
        },
        [Stir.Shake] = new[]
        {
            new[] { "흔들린다 — 뭘 붙잡아", "진동이 크네", "엔진 쪽인가" },
            new[] { "뭐야, 뭐가 부딪쳤어?", "흔들려…!", "이거 괜찮은 거 맞아?" },
            new[] { "놀이기구 탔네", "배가 딸꾹질한다", "어이쿠" },
            new[] { "또 흔들려", "커피 쏟을 뻔했잖아", "누가 운전해, 이거" },
            new[] { "흔들린다", "어이쿠", "뭐지?" },
        },
        [Stir.Alarm] = new[]
        {
            new[] { "경보다 — 다들 들었지", "자리로 가자", "침착하게 움직이자" },
            new[] { "무슨 경보야? 무슨 일이야?", "또 울려…", "제발 별일 아니길" },
            new[] { "또 울리네 — 이번엔 진짜일까", "점심 종인가", "경보 소리는 언제 들어도 별로야" },
            new[] { "또 경보야?", "귀 아파", "이번엔 뭐야" },
            new[] { "경보다", "무슨 일이지?", "들었어?" },
        },
        [Stir.Glass] = new[]
        {
            new[] { "유리 조각 — 돌아가자", "바닥에 유리, 조심", "밟으면 큰일 나" },
            new[] { "유리…! 밟을 뻔했어", "누가 깨뜨렸지? 다칠 뻔했잖아", "으, 발 조심" },
            new[] { "맨발 금지 구역", "보물이 반짝이네 — 유리였네", "피해 가자" },
            new[] { "누가 깨 놓고 안 치웠어?", "유리를 왜 이대로 둬", "치우는 사람 따로 있나" },
            new[] { "유리 조각이네", "돌아가야겠다", "조심" },
        },
    };

    private static readonly string[][] GlassWarn =
    {
        new[] { "거기 유리 조각 있어, 돌아가", "멈춰 — 바닥에 유리" },
        new[] { "조심해! 유리!", "거기 밟지 마!" },
        new[] { "거기 지뢰밭이야 — 유리", "발밑 조심, 반짝이는 거 유리야" },
        new[] { "유리 있다니까, 돌아가", "거기 유리 — 좀 보고 다녀" },
        new[] { "조심해, 유리 조각", "거기 유리 있어" },
    };

    private string[] Pool(CrewMember c, Stir k, bool flag)
    {
        int m = Mood(c);
        if (k == Stir.Glass && flag) return GlassWarn[m];
        if (k == Stir.Smoke && flag) return m == 1 ? new[] { "불이야! 불 났어!", "연기 — 불이다, 빨리!" } : new[] { "불이다 — 소화기!", "연기 나는 데 불 있어" };
        return Table.TryGetValue(k, out var t) ? t[m] : Array.Empty<string>();
    }

    private string[] WetLines(CrewMember c, string what) => Mood(c) switch
    {
        0 => new[] { $"바닥에 {what} — 천천히", "미끄럽다, 한 발씩", $"{what} 닦을 사람 불러야겠다" },
        1 => new[] { "미끄러질 것 같아…", $"으, {what}… 넘어지면 어떡해", "벽 잡고 가야지" },
        2 => new[] { "스케이트장 개장했네", "아이스 댄스 한 판?", "엉덩방아 찧기 딱 좋네" },
        3 => new[] { $"누가 {what} 흘려 놓고 그냥 갔어", "닦지도 않고 뭐 하는 거야", "넘어지면 누가 책임져" },
        _ => new[] { $"바닥에 {what}", "미끄럽겠다", "조심조심" },
    };

    private string[] OpenLines(CrewMember c, Stir k, Room room) => Pool(c, k, false);

    /// <summary>연기 · 흔들림 · 냄새에 이어지는 행동의 한마디 (말투마다).</summary>
    private string[] ActLines(CrewMember c, string id) => (id, Mood(c)) switch
    {
        ("smoke_seek", 1) => new[] { "어디서 나는 거지… 가 봐야겠어", "연기가 저쪽에서 와 — 확인만 할게" },
        ("smoke_seek", 2) => new[] { "누가 고기 굽나? 가 보자", "연기 따라가면 범인이 나오겠지" },
        ("smoke_seek", 3) => new[] { "또 어디서 태워 먹었어 — 가 본다", "아무도 안 보면 내가 봐야지" },
        ("smoke_seek", _) => new[] { "연기가 저쪽에서 들어와 — 보고 올게", "어디서 나는지 확인하자", "문 쪽에서 들어오네, 가 볼게" },
        ("smoke_leave", 1) => new[] { "숨 막혀… 나가 있을래", "여기 있으면 안 될 것 같아" },
        ("smoke_leave", 2) => new[] { "훈제되기 전에 나간다", "난 공기 좋은 데로 피신" },
        ("smoke_leave", 3) => new[] { "이래서야 숨을 쉬겠어? 나간다", "환기 안 해? 난 나가 있을게" },
        ("smoke_leave", _) => new[] { "옆방으로 나가 있자", "공기 맑은 데로 가자", "여기선 숨쉬기 힘들다" },
        ("crouch", 2) => new[] { "어이쿠, 놀이기구도 아니고", "바닥이랑 친해지는 중" },
        ("crouch", _) => new[] { "뭐야, 왜 흔들려…", "붙잡아! 뭐라도 붙잡아", "몸 낮춰" },
        ("shrug", 2) => new[] { "오, 마사지 기능인가", "배가 기지개 켜나 봐" },
        ("shrug", 3) => new[] { "또 흔들리네, 지겹다", "이 배는 하루도 조용할 날이 없어" },
        ("shrug", _) => new[] { "이 정도야 뭐", "좀 흔들렸네", "별일 아니야" },
        ("shake_check", 1) => new[] { "저 기계에서 나는 떨림 같은데… 봐야겠어", "베어링 아니야? 확인해 보자" },
        ("shake_check", _) => new[] { "이건 기계 떨림이야 — 가 볼게", "어디서 떠는지 손 대 보면 알지", "축이 흔들리는 소리 같은데" },
        ("follow_nose", 2) => new[] { "냄새가 날 부른다 — 간다", "코가 먼저 가네" },
        ("follow_nose", 3) => new[] { "냄새만 풍기고 안 주면 반칙이지", "배고파 죽겠는데 — 가 봐야지" },
        ("follow_nose", _) => new[] { "냄새 따라 가 볼까", "주방에 뭐 있나 보러 가야지", "한 입만 얻어먹자" },
        ("burnt_check", _) => new[] { "탄내 — 주방 불 꺼졌나 봐야겠다", "뭐 올려놓고 나온 사람 없어? 가 볼게", "타는 냄새는 그냥 두면 안 돼" },
        ("foul_leave", 3) => new[] { "이 냄새 맡으면서는 못 있어", "누가 치우기 전엔 안 들어온다" },
        ("foul_leave", _) => new[] { "잠깐 나가 있을게", "숨 좀 쉬고 올게", "코가 아파서 못 있겠다" },
        _ => Array.Empty<string>(),
    };

    private string[] WayLines(CrewMember c, Stir k, string id, ReactAct? act) => id switch
    {
        "torch" => new[] { "손전등 — 여기 있다", "잠깐, 손전등 켤게", "늘 차고 다니길 잘했지" },
        "lamp" => new[] { "창고에 작업등 있어 — 가져올게", "작업등 가져올게, 그 자리에 있어", "등 하나 끌고 올게" },
        "fix" => new[] { "전등만 나간 거야 — 내가 볼게", "등 접속부 같아, 금방 해", "이건 고칠 수 있어" },
        "breaker" => new[] { "차단기 내려갔을 거야 — 보고 올게", "배전반부터 보자", "차단기 쪽이야" },
        "window" => new[] { "창가로 가자, 별빛이라도 들어", "창 쪽은 그래도 보여", "별빛이 생각보다 밝네" },
        "screen" => new[] { "콘솔 화면은 켜져 있네", "화면 불빛이라도 있어", "콘솔 앞이 제일 밝다" },
        "wrist" => new[] { "손목 단말 불빛이면 돼", "단말 화면 켜면 조금 보여", "이거라도 켜자" },
        "follow" => new[] { "그 불빛 옆에 있을게", "같이 가자, 놓치지 마", "손전등 있는 사람 옆이 최고지" },
        "feel" => new[] { "벽 짚고 가면 돼", "길은 손이 기억해", "천천히, 벽 따라" },
        "wait" => new[] { "움직이지 말자 — 부딪친다", "들어올 때까지 가만히 있을래", "…빨리 들어와라" },
        "blanket" => new[] { "담요 가져와야겠다", "내 담요 어디 뒀더라", "담요 두르면 좀 낫겠지" },
        "heater" => new[] { "히터 앞으로 가자", "히터 돌고 있네 — 살았다", "따뜻한 데로" },
        "heater_fetch" => new[] { "창고에 히터 있지? 가져올게", "히터 하나 꺼내 오자", "히터 끌고 올게" },
        "jog" => new[] { "몸이라도 움직이자", "뛰면 금방 따뜻해져", "제자리 뛰기 백 번!" },
        "huddle" => new[] { "좀 붙어 있자", "옆에 앉아도 돼?", "붙어 있으면 따뜻해" },
        "cup" => k == Stir.Cold ? new[] { "뜨거운 차 한 잔 해야겠다", "따뜻한 거 마시자", "커피라도 타야지" } : new[] { "찬물 한 잔 마셔야겠다", "물 좀 마시고 올게", "시원한 거 없나" },
        "warm_room" or "cool_room" => new[] { $"{(act?.Label ?? "옆방으로 옮긴다").Replace("으로 옮긴다", "")} 쪽으로 가자", "여기보다 옆방이 낫겠다", "자리 옮기자" },
        "hug" => new[] { "팔짱 끼고 버티지 뭐", "조금만 참자", "으으" },
        "jacket" => new[] { "겉옷 좀 벗어야겠다", "못 참겠다, 벗자", "윗도리 벗고 할래" },
        "fan" => new[] { "선풍기 앞이 명당이네", "선풍기 돌고 있다 — 살았다", "바람 좀 쐬자" },
        "fan_fetch" => new[] { "창고에 선풍기 있지? 가져올게", "선풍기 하나 꺼내 오자", "선풍기 끌고 올게" },
        "fanself" => new[] { "부채라도 있으면", "손부채라도 해야지", "후 — 바람 좀" },
        "wipe" => new[] { "땀 좀 닦고", "땀 범벅이네", "후" },
        _ => Array.Empty<string>(),
    };

    private string[] LinesBack(CrewMember c, Stir k) => k switch
    {
        Stir.Dark => Mood(c) == 2 ? new[] { "빛이 있으라!", "들어왔다 — 박수!" } : Mood(c) == 3 ? new[] { "이제야 들어오네", "진작 좀 들어오지" } : new[] { "들어왔다", "휴, 이제 좀 보이네", "불 들어오니까 살겠다" },
        Stir.Cold => new[] { "이제 좀 살 만하네", "손가락이 풀린다", "따뜻해졌다" },
        _ => new[] { "좀 시원해졌다", "이제야 숨이 쉬어지네", "살 것 같다" },
    };

    private string[] SoundLines(CrewMember c, Machine m, OmenKind kind, bool nextRoom)
    {
        string snd = kind switch { OmenKind.Vibration => "덜컹거리는", OmenKind.Heat => "타닥거리는", OmenKind.Pressure => "쉭쉭 새는", _ => "삑삑거리는" };
        string where = nextRoom ? "옆방에서 " : "";
        return Mood(c) switch
        {
            0 => new[] { $"{where}{snd} 소리 — {m.Name} 쪽이다", $"{m.Name} 소리가 평소랑 달라", "저 소리, 적어 둬야겠다" },
            1 => new[] { $"{where}저 {snd} 소리 들려? 전에도 났었어", $"{m.Name}… 저러다 터지는 거 아냐?", "무슨 소리야, 저거" },
            2 => new[] { $"{m.Name}가 배고프다고 우네", $"{where}누가 {snd} 노래를 부르네", "배가 코를 고나" },
            3 => new[] { $"{where}또 {snd} 소리", $"{m.Name} 저거 언제 고쳐", "시끄러워 죽겠네" },
            _ => new[] { $"{where}{snd} 소리 안 들려?", $"{m.Name} 쪽에서 소리가 나", "무슨 소리지?" },
        };
    }

    private string[] SmellLines(CrewMember c, SmellKind k) => k switch
    {
        SmellKind.Cooking => Mood(c) == 2 ? new[] { "냄새가 나를 부른다", "오늘 메뉴 뭐야?" } : new[] { "냄새 좋다 — 뭐 하지?", "배고파지네", "주방에서 뭐 하나 봐" },
        SmellKind.Bread => new[] { "빵 굽는 냄새!", "갓 구운 빵 냄새다", "이 냄새는 못 참지" },
        SmellKind.Burnt => Mood(c) == 1 ? new[] { "타는 냄새…! 어디서 나?", "불 아니야?" } : new[] { "어디 타는 냄새 안 나?", "뭐 탔나 봐", "탄내가 나" },
        SmellKind.Foul => Mood(c) == 3 ? new[] { "윽, 누가 이거 안 치워?", "냄새 때문에 못 있겠네" } : new[] { "윽, 이게 무슨 냄새야", "코가 썩겠다", "환기 좀 하자" },
        _ => new[] { "커피 냄새 — 한 잔 해야겠다", "누가 커피 내렸네", "커피 향 좋다" },
    };

    private string[] VoiceLines(CrewMember c, string text, int priority)
    {
        string head = text.Split(new[] { " — ", ". ", " · " }, StringSplitOptions.None)[0];
        // 인사 · 안내 말은 따라 하지 않는다
        if (head.Length > 20 || head.Contains('(') || head.Contains(':'))
            return priority >= 2 ? (Mood(c) == 1 ? new[] { "또 무슨 일이야?", "빨리 움직이자" } : new[] { "들었지? 가자", "방송 들었어?" })
                : Mood(c) switch { 2 => new[] { "컴퓨터가 또 잔소리하네", "네네, 알겠습니다" }, 3 => new[] { "또 방송이야", "알았다고" }, _ => new[] { "응, 알았어", "방송 들었어?", "그렇대" } };
        if (priority < 2 && (head.Contains("좋은 아침") || head.Contains("안녕") || head.Contains("수고") || head.Length < 6))
            return Mood(c) switch { 2 => new[] { "컴퓨터도 아침 인사를 하네", "네, 좋은 아침이에요" }, 3 => new[] { "아침부터 방송이야", "알았다고" }, _ => new[] { "응, 좋은 아침", "방송 들었어?" } };
        if (priority >= 2)
            return Mood(c) == 1 ? new[] { $"'{head}'래 — 어떡해", "또 무슨 일이야?", "빨리 움직이자" } : new[] { $"'{head}'래 — 움직이자", "들었지? 가자", "방송 들었어?" };
        return Mood(c) switch
        {
            2 => new[] { "컴퓨터가 또 잔소리하네", $"'{head}' — 알겠습니다, 선생님" },
            3 => new[] { "또 방송이야", $"'{head}'… 알았다고" },
            _ => new[] { $"'{head}'래", "응, 알았어", "방송 들었어?" },
        };
    }

    private string[] BriefLines(CrewMember c, Briefing bf)
    {
        var o = new List<string>();
        static string First(string t, string prefix) => (t.StartsWith(prefix) ? t[prefix.Length..] : t).Split(" · ")[0].Split(" — ")[0].Trim();
        foreach (var (kind, text) in bf.Lines)
        {
            if (kind == "날씨") o.Add(text.Contains("맑음") ? "오늘 날씨는 맑대" : "오늘 바깥 날씨가 안 좋대");
            else if (kind == "정비") o.Add(text.Contains("없음") ? "오늘은 손볼 게 없대" : text.StartsWith("오늘 정비") ? $"오늘 손볼 거 — {First(text, "오늘 정비: ")}" : "정비표가 바뀌었대");
            else if (kind == "주의" && !text.Contains("없음")) o.Add($"오늘은 {First(text, "주의할 곳: ")} 조심하래");
            else if (kind == "물자" && text.Split(" — ")[0] is string sp && sp.EndsWith("다")) o.Add(Mood(c) == 1 ? $"{sp[..^1]}대… 괜찮을까" : $"{sp[..^1]}대");
        }
        o.Add(Mood(c) == 3 ? "아침부터 할 일이 많네" : Mood(c) == 2 ? "오늘도 좋은 아침이라네" : "오늘 할 일 정리됐네");
        return o.ToArray();
    }

    private string[] NovelLines(CrewMember c, string name, CrewMember? maker, bool likes)
    {
        if (likes)
            return maker != null && maker != c ? new[] { $"{Ko.IGa(maker.Name)} 만든 거야? 솜씨 좋네", $"{name} 걸렸네 — 예쁘다", "이 방이 좀 사람 사는 데 같네" }
                : new[] { $"{name} 생겼네 — 좋다", $"누가 {name} 걸었네", "분위기 좋아졌다" };
        return Mood(c) == 3 ? new[] { $"{name}… 이걸 왜 여기다", "취향 참 독특하네" } : new[] { $"{name} 생겼네", "뭐가 새로 걸렸네", "음, 그렇구나" };
    }

    private string[] OddMutter(CrewMember c, CrewMember o, string what) => Mood(c) switch
    {
        2 => new[] { $"{Ko.EunNeun(o.Name)} 또 시작이네", $"{o.Name} 오늘 컨디션 좋은가 봐" },
        1 => new[] { $"{o.Name} 괜찮은 건가…", $"{Ko.EunNeun(o.Name)} 왜 저러지" },
        _ => new[] { $"…{Ko.EunNeun(o.Name)} 뭐 하는 거지", "못 본 척하자", $"{o.Name} 좀 이상하네" },
    };

    private string[] AskLines(CrewMember c, CrewMember o, string what)
    {
        string q = what switch
        {
            "제자리에서 뛴다" => "왜 제자리에서 뛰어?",
            "벽을 더듬는다" => "벽은 왜 더듬어?",
            "누구한테 바짝 붙어 있다" => "둘이 왜 그렇게 붙어 있어?",
            "벽을 붙잡고 있다" => "괜찮아? 벽은 왜 붙잡고 있어",
            "귀를 막고 있다" => "귀는 왜 막고 있어? 괜찮아?",
            "자면서 걷는다" => "…자는 거야? 방은 저쪽이야",
            _ => "거기서 뭐 해?",
        };
        return new[] { $"{o.Name}, {q}", q, $"{o.Name}? {q}" };
    }

    private string[] ReplyLines(CrewMember o, CrewMember c, string what) => what switch
    {
        "제자리에서 뛴다" => new[] { "추워서 그래 — 너도 해 봐", "몸 좀 데우는 중이야" },
        "벽을 더듬는다" => new[] { "캄캄해서 벽 짚고 다녀", "안 보여서 그래" },
        "누구한테 바짝 붙어 있다" => new[] { "추워서 그래 — 붙어 있으면 따뜻해", "왜, 부러워?" },
        "벽을 붙잡고 있다" => new[] { "흔들려서 — 넌 괜찮아?", "어지러워서 좀" },
        "귀를 막고 있다" => new[] { "경보 소리가 너무 커서", "머리가 울려서" },
        "자면서 걷는다" => new[] { "…어? 내가 왜 여기 있지?", "음… 꿈꿨나 봐" },
        _ => Mood(o) == 2 ? new[] { "비밀이야", "알면 다쳐" } : new[] { "아무것도 아니야", "그냥 좀" },
    };

    private string[] ComfortLines(CrewMember c, CrewMember o) => Mood(c) switch
    {
        2 => new[] { $"{o.Name}, 차 한 잔 할래? 내가 쏠게", "울지 마, 나까지 울잖아" },
        0 => new[] { $"{o.Name}, 여기 있을게", "천천히 해. 말 안 해도 돼" },
        _ => new[] { $"{o.Name}, 괜찮아?", "울어도 돼", "옆에 있을게" },
    };
}
