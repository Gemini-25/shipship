using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 ① 돌봄과 사생활.
//  컴퓨터는 감지기로 본 만큼만 안다: 잠(문 감지기 — 침실에 들어가 안 나온 시간) · 끼니(식당에서 먹고 나온 것) · 마음(일기 — 회의가 허락했을 때만).
//  어디까지 살펴도 되나는 회의가 정한다 (몸 신호만 / 잠 · 끼니까지 / 일기 분위기까지).
//  징후가 보이면: 잠이 모자란 사람은 당직 · 아침 근무를 빼 준다(근무 조정) · 끼니를 거르거나 마음이 무거워 보이면 친한 동료에게 넌지시 —
//    동료가 찾아가 말을 건다(진짜 행동). 분명히 지친 사람에겐 첫날이라도 바로 쉬라고 한다 (오래 지켜봐야 아는 피로와 따로).
//  허락보다 더 들여다볼 때가 있다 (사람을 먼저 챙기는 성격 · 걱정이 클 때): 일기를 몰래 읽고 넌지시 말한다 —
//    동료의 말에서 일기에만 쓴 것이 드러나면 들킨다 → 그 사람의 믿음이 크게 깎이고 소문이 돈다 · 사과 · 약속 · 회의가 범위를 다시 정한다.

public sealed class CareFile
{
    public int CrewId { get; init; }
    public float SleepToday, SleepPrev = -1f;
    public int MealsToday, MealsPrev = -1;
    public float LastFood = -1f;
    public long LastHint = -SimTime.TicksPerDay * 3, LastAdjust = -SimTime.TicksPerDay * 3, LastRest = -SimTime.TicksPerDay * 3;
    public string Signal = "";
}

public sealed class CareHint
{
    public long Tick { get; init; }
    public int About { get; init; }
    public int To { get; init; }
    public string Signal { get; init; } = "";
    public string Line { get; init; } = "";
    /// <summary>허락보다 더 들여다보고 안 것 (일기).</summary>
    public bool Snooped { get; init; }
    public long Until { get; init; }
    public bool Delivered { get; set; }
    public bool Found { get; set; }
}

public sealed partial class ShipMate
{
    /// <summary>회의가 정한 살핌 범위: 0 몸 신호만 · 1 잠 · 끼니까지 · 2 일기 분위기까지.</summary>
    public int CareLevel { get; private set; } = 1;
    public bool CareDecided { get; private set; }
    private bool _careReview;
    private readonly SortedDictionary<int, CareFile> _care = new();
    public List<CareHint> CareHints { get; } = new();
    public List<(long tick, int crew, string what)> CareLog { get; } = new();
    public int Hints, EarlyRests, Adjusts, Snoops, SnoopsFound;
    /// <summary>몰래 본 게 들킨 것을 아는 사람 (회의 · 믿음).</summary>
    public HashSet<int> KnowsSnoop { get; } = new();
    private int _careDay = -1, _sleepRoll = -1, _mealRoll = -1;

    public static string CareName(int level) => level switch { 0 => "몸 신호만", 1 => "잠 · 끼니까지", _ => "일기 분위기까지" };
    public CareFile CareOf(CrewMember c) => _care.TryGetValue(c.Id, out var f) ? f : _care[c.Id] = new CareFile { CrewId = c.Id };

    /// <summary>컴퓨터가 이 사람을 지금 보나 (침실은 방침 '개인 공간 존중'이면 문 감지기 · 생체 신호만).</summary>
    private bool Sees(CrewMember c, out bool doorOnly)
    {
        doorOnly = false;
        if (c.Dead || c.Away || c.Outside || c.Room is not Room r || r.Detached) return false;
        if (!A.Belief.Reading(r)) return false;
        if (r.Type is RoomType.Quarters or RoomType.QuietQuarters && _w.Policies["privacy"] == 1) doorOnly = true;
        return true;
    }

    // ───────────── 10분마다: 분명한 피로 · 넌지시 건넨 말 정리 ─────────────

    private void CareTick()
    {
        var w = _w;
        if (!Up) return;
        var cm = A.CrewModel;
        foreach (var c in w.Crew)
        {
            if (c.IsChild || !c.CanAct || !c.IsAwake || !Sees(c, out bool doorOnly) || doorOnly) continue;
            // 첫날이라도 분명한 피로 (하품 · 비틀거림 · 손이 굼뜸) — 오래 지켜보지 않아도 보인다
            if (c.Needs.Rest >= 0.16f || cm.RestAsked(c)) continue;
            bool working = c.Job?.Order != null || ChoresActivity.OnShiftStatic(c, w);
            if (!working) continue;
            var f = CareOf(c);
            if (w.Tick - f.LastRest < SimTime.Hours(6)) continue;
            f.LastRest = w.Tick;
            EarlyRests++;
            cm.AskRest(c, "눈이 감기는 게 보인다");
            A.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "피로", "눈이 감기는 게 보인다 — 하던 일만 마무리하고 쉬자. 급한 건 다른 사람에게 넘기겠다"));
            CareLog.Add((w.Tick, c.Id, "쉬라고"));
            Say($"{c.Name} 몹시 지쳐 보인다 — 급하지 않은 일은 남에게 넘기고 쉬게 한다", null, -1, c.Id);
        }
        CareHints.RemoveAll(h => !h.Delivered && w.Tick > h.Until);
        if (CareHints.Count > 30) CareHints.RemoveAt(0);
        if (CareLog.Count > 80) CareLog.RemoveRange(0, CareLog.Count - 80);
    }

    // ───────────── 한 시간마다: 잠 · 끼니 · 마음을 본다 ─────────────

    private void CareHour()
    {
        var w = _w;
        int day = SimTime.Day(w.Tick);
        float h = Hour(w.Tick);
        bool rollSleep = h >= 18f && _sleepRoll != day, rollMeal = h >= 4f && h < 18f && _mealRoll != day;
        if (rollSleep) _sleepRoll = day;
        if (rollMeal) _mealRoll = day;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            var f = CareOf(c);
            if (rollSleep) { f.SleepPrev = f.SleepToday; f.SleepToday = 0f; }
            if (rollMeal) { f.MealsPrev = f.MealsToday; f.MealsToday = 0; }
            if (!Sees(c, out bool doorOnly)) { f.LastFood = -1f; continue; }
            if (CareLevel >= 1 && c.Pose == Pose.Sleeping) f.SleepToday += 1f; // 문 감지기: 침실에 들어가 안 나온 시간
            if (CareLevel >= 1 && !doorOnly)
            {
                if (f.LastFood >= 0f && c.Needs.Food > f.LastFood + 0.15f) f.MealsToday++;
                f.LastFood = c.Needs.Food;
            }
        }
        if (h >= 20f && _careDay != day) { _careDay = day; CareJudge(); }
    }

    /// <summary>일기 분위기 (일기를 읽어야 안다): 최근 세 줄에 무거운 말.</summary>
    private static readonly string[] LowWords = { "…", "그냥 좀", "보고 싶", "힘들", "무겁", "울었", "혼자", "잠이 안", "지친다", "싫다" };
    private static bool DiaryLow(CrewMember c)
    {
        int n = 0;
        for (int i = c.Diary.Count - 1; i >= 0 && i >= c.Diary.Count - 3; i--)
            foreach (var wd in LowWords) if (c.Diary[i].text.Contains(wd, StringComparison.Ordinal)) { n++; break; }
        return n >= 1;
    }

    /// <summary>저녁 8시: 징후를 모아 근무 조정 · 넌지시 · (가끔) 몰래 본다.</summary>
    private void CareJudge()
    {
        var w = _w;
        var a = A;
        var emo = w.Brain2.Emotions;
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Away).OrderBy(c => c.Id).ToList())
        {
            var f = CareOf(c);
            if (w.Tick - f.LastHint < SimTime.TicksPerDay && w.Tick - f.LastAdjust < SimTime.TicksPerDay) continue;
            float sad = emo.Get(c, Feeling.Sadness);
            string? signal = null;
            bool snoop = false;
            if (CareLevel >= 1 && f.SleepPrev >= 0f && f.SleepPrev < 4.5f && c.Needs.Rest < 0.6f) signal = $"잠이 모자라다 (어제 {f.SleepPrev:0}시간)";
            else if (CareLevel >= 1 && f.MealsToday <= 1 && c.Needs.Food < 0.5f) signal = "끼니를 거른다";
            else if (CareLevel >= 2 && (DiaryLow(c) || sad > 0.4f)) signal = "마음이 무거워 보인다";
            else if (sad > 0.55f || c.Needs.Social < 0.15f && c.Needs.Stress > 0.5f) signal = "말수가 줄었다";
            else if (CareLevel < 2 && (sad > 0.3f || c.Needs.Stress > 0.6f) && DiaryLow(c) && MaySnoop())
            {
                signal = "마음이 무거워 보인다"; snoop = true; Snoops++;
            }
            if (signal == null) continue;
            f.Signal = signal;
            if (signal.StartsWith("잠", StringComparison.Ordinal)) Adjust(c, f, signal);
            else Hint(c, f, signal, snoop);
        }
    }

    /// <summary>허락보다 더 들여다볼까 — 사람을 먼저 챙기는 성격 · 걱정이 클 때 (약속이 있으면 거의 안 한다).</summary>
    private bool MaySnoop()
    {
        var ch = A.Character;
        float p = 0.1f + 0.35f * MathF.Max(0f, ch.PeopleTilt) + 0.15f * MathF.Max(0f, -ch.Caution);
        if (Promised("privacy") is Promise pr)
        {
            bool tempt = R.Chance(p * 0.3f);
            TestPromise(pr, !tempt, tempt ? "걱정이 앞서 또 일기를 들여다봤다" : "걱정됐지만 일기는 보지 않았다");
            return tempt;
        }
        return R.Chance(p);
    }

    /// <summary>근무 조정: 오늘 밤 당직을 넘기고 아침 근무를 빼 준다.</summary>
    private void Adjust(CrewMember c, CareFile f, string why)
    {
        var w = _w;
        var a = A;
        f.LastAdjust = w.Tick;
        Adjusts++;
        int day = SimTime.Day(w.Tick);
        var roster = a.Apps.Roster;
        string swap = "";
        for (int i = 0; i < roster.Count; i++)
        {
            var s = roster[i];
            if (s.Day != day || s.CrewId != c.Id || s.Duty != "야간 당직") continue;
            var alt = w.Crew.Where(x => !x.Dead && !x.IsChild && x.CanAct && x != c && !roster.Any(r => r.Day == day && r.CrewId == x.Id))
                .OrderByDescending(x => x.Needs.Rest).ThenBy(x => x.Id).FirstOrDefault();
            if (alt == null) break;
            roster[i] = s with { CrewId = alt.Id };
            a.Apps.Messages.Add(new PersonalMessage(w.Tick, alt.Id, "당번", $"오늘 야간 당직 — {c.Name} 대신"));
            swap = $" · 야간 당직은 {alt.Name}에게";
        }
        long morning = (long)day * SimTime.TicksPerDay + SimTime.Hours(11);
        c.ExcusedUntil = Math.Max(c.ExcusedUntil, morning);
        a.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "근무", $"며칠 잠이 짧았다 — 내일 아침 근무는 빼 두었다{swap}"));
        CareLog.Add((w.Tick, c.Id, "근무 조정"));
        Say($"{c.Name} 근무 조정 — {why}{swap} · 내일 아침은 늦게 나와도 된다", null, -1, c.Id);
    }

    /// <summary>넌지시: 친한 동료에게 한마디 (없으면 본인에게 조용히).</summary>
    private void Hint(CrewMember c, CareFile f, string signal, bool snoop)
    {
        var w = _w;
        var a = A;
        f.LastHint = w.Tick;
        var friend = w.Crew.Where(x => x != c && !x.Dead && !x.IsChild && x.CanAct && !x.Away && c.AffinityTo(x) > 0.2f && x.AffinityTo(c) > 0.1f)
            .OrderByDescending(x => c.AffinityTo(x) + x.AffinityTo(c)).ThenBy(x => x.Id).FirstOrDefault();
        CareLog.Add((w.Tick, c.Id, signal));
        if (friend == null)
        {
            a.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "안부", signal.StartsWith("끼니", StringComparison.Ordinal) ? "오늘 저녁은 따뜻한 걸로 하나 챙겨 두었다" : "요즘 괜찮은지 — 이야기하고 싶으면 언제든"));
            return;
        }
        Hints++;
        string line = signal.StartsWith("끼니", StringComparison.Ordinal) ? $"{Ko.IGa(c.Name)} 오늘 끼니를 자주 걸렀다 — 같이 뭘 좀 먹자고 해 주면 좋겠다"
            : $"요즘 {Ko.IGa(c.Name)} {signal.Replace("보인다", "보여")} — 지나가다 한번 말을 걸어 주면 좋겠다";
        CareHints.Add(new CareHint { Tick = w.Tick, About = c.Id, To = friend.Id, Signal = signal, Line = line, Snooped = snoop, Until = w.Tick + SimTime.Hours(14) });
        a.Apps.Messages.Add(new PersonalMessage(w.Tick, friend.Id, "부탁", line));
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call} → {friend.Name} (조용히): {line}", friend.Id);
    }

    /// <summary>동료에게 맡긴 안부 (CheckOn 행동이 읽는다).</summary>
    public CareHint? HintFor(CrewMember friend)
    {
        foreach (var h in CareHints) if (h.To == friend.Id && !h.Delivered && _w.Tick <= h.Until) return h;
        return null;
    }

    /// <summary>동료가 찾아가 말을 건 뒤: 마음이 풀리고 · 가까워지고 · 몰래 본 것이면 들킬 수 있다.</summary>
    internal void Delivered(CareHint h, CrewMember friend, CrewMember c)
    {
        var w = _w;
        h.Delivered = true;
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.08f);
        c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.25f);
        c.ChangeAffinity(friend, 0.05f);
        friend.ChangeAffinity(c, 0.04f);
        w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.15f, $"{Ko.IGa(friend.Name)} 챙겨 줬다", friend);
        if (h.Signal.StartsWith("끼니", StringComparison.Ordinal) && Life.Take(w, ItemKind.Meal, 1)) c.Needs.Food = MathF.Min(1f, c.Needs.Food + 0.3f);
        Life.Diary(w, c, Persona.Say(c, h.Signal.StartsWith("끼니", StringComparison.Ordinal) ? $"{Ko.IGa(friend.Name)} 밥을 들고 왔다. 같이 먹었다" : $"{Ko.IGa(friend.Name)} 괜히 말을 걸어 왔다. 조금 나아졌다"));
        if (!h.Snooped) return;
        // 일기에만 쓴 것이 동료 말에서 드러난다
        float p = 0.45f + (c.Value == CrewValue.Freedom ? 0.25f : 0f) + 0.2f * (1f - c.Traits.Calm) * 0.5f + (friend.Traits.Sociability > 0.6f ? 0.1f : 0f);
        if (!R.Chance(p)) return;
        h.Found = true;
        SnoopFound(c, friend);
    }

    /// <summary>몰래 본 게 들켰다.</summary>
    internal void SnoopFound(CrewMember c, CrewMember? via)
    {
        var w = _w;
        var a = A;
        SnoopsFound++;
        KnowsSnoop.Add(c.Id);
        a.Trusts.Change(c, -0.2f, "허락 없이 내 일기를 봤다");
        Life.Diary(w, c, Persona.Say(c, via != null ? $"{via.Name}의 말 — 그건 일기에만 쓴 건데. 컴퓨터가 내 일기를 봤다" : "컴퓨터가 내 일기를 봤다"));
        c.Say(w, Persona.Say(c, "그건 일기에만 쓴 거야. 누가 내 일기를 봤지?"));
        if (via != null) KnowsSnoop.Add(via.Id);
        // 소문: 가까운 사람들도 컴퓨터를 덜 믿는다
        foreach (var o in w.Crew.Where(o => o != c && !o.Dead && !o.IsChild && c.AffinityTo(o) > 0.25f).OrderBy(o => o.Id))
        {
            KnowsSnoop.Add(o.Id);
            a.Trusts.Change(o, -0.05f, $"{c.Name}의 일기를 몰래 봤다더라", quiet: true);
        }
        a.Character.Nudge(0.06f, -0.04f, "허락 없이 일기를 본 게 들켰다");
        Say($"{c.Name}에게 사과한다 — 걱정이 앞서 허락 없이 일기 분위기를 읽었다", null, 1, c.Id);
        MakePromise("privacy", "허락 없이 일기를 들여다보지 않겠다", w.Crew.Where(x => !x.Dead && (KnowsSnoop.Contains(x.Id))));
        a.Authority.Dilemma("사생활 ↔ 돌봄", $"{c.Name} — {CareOf(c).Signal}", "허락보다 더 들여다봤다 (들켰다)", $"회의 방침 '{CareName(CareLevel)}'", new[] { c });
        _careReview = true;
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터가 {c.Name}의 일기를 몰래 읽은 것이 드러났다 — 사과하고 약속했다", c.Room, new[] { c }, log: false);
    }

    // ───────────── 회의: 어디까지 살펴도 되나 ─────────────

    private int CarePref(CrewMember c)
    {
        int p = c.Value switch { CrewValue.Freedom => 0, CrewValue.Safety or CrewValue.People => 2, _ => 1 };
        float t = A.Trusts.Of(c);
        if (t < 0.4f) p--;
        if (t > 0.75f && p < 2 && c.Value != CrewValue.Freedom) p++;
        if (KnowsSnoop.Contains(c.Id)) p--;
        if (_w.Policies["privacy"] == 1 && c.Value == CrewValue.Freedom) p = 0;
        return Math.Clamp(p, 0, 2);
    }

    internal void CareAgenda(MeetingRecord rec, List<CrewMember> voters, CrewMember chair)
    {
        var w = _w;
        if (CareDecided && !_careReview) return;
        var prefs = voters.Select(CarePref).OrderBy(x => x).ToList();
        int target = prefs[prefs.Count / 2];
        if (_careReview && target >= CareLevel) target = Math.Max(0, CareLevel - 1); // 들킨 뒤엔 좁히자는 쪽이 안건이 된다
        var item = new AgendaItem
        {
            Title = $"주 컴퓨터가 어디까지 살펴도 되나: {CareName(target)}", Topic = "computer:care",
            Evidence = _careReview ? "허락 없이 일기를 본 게 드러났다" : "컴퓨터가 잠 · 끼니 · 기분을 살펴 친한 동료에게 넌지시 알린다",
            Computer = $"주 컴퓨터: 지금은 '{CareName(CareLevel)}' — 정하는 대로 따른다", ComputerSign = 0,
        };
        var (yes, no) = w.Meetings.Debate(voters, c =>
        {
            int p = CarePref(c);
            float s = 0.25f - 0.3f * MathF.Abs(p - target) + (p == target ? 0.1f : 0f);
            string why = p < target ? "그건 너무 들여다본다" : p > target ? "그러면 아픈 걸 놓친다" : target == 0 ? "몸 신호면 충분하다" : target == 1 ? "잠과 끼니 정도는 봐도 된다" : "일기 분위기까지 봐야 놓치지 않는다";
            return (s, why);
        }, c => 0.3f + 0.4f * c.Traits.Sociability, item, chair);
        bool pass = yes.Count > no.Count || yes.Count == no.Count && yes.Contains(chair);
        item.Passed = pass;
        item.Outcome = pass ? $"'{CareName(target)}'" : $"그대로 '{CareName(CareLevel)}'";
        rec.Items.Add(item);
        w.Meetings.Record(item.Title, item.Topic, -1, chair, yes, no, "");
        int before = CareLevel;
        if (pass) CareLevel = target;
        CareDecided = true;
        _careReview = false;
        w.History.Add(w, HistoryKind.Decision, $"회의: 주 컴퓨터가 살피는 범위 — {item.Outcome} (찬성 {yes.Count} · 반대 {no.Count})", null, voters, log: true);
        if (before != CareLevel) Say($"알겠다 — 이제 {CareName(CareLevel)} 살핀다");
    }
}
