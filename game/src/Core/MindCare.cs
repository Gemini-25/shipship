using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 마음 돌봄: 스트레스 · 우울 · 불면 · 외상 후 스트레스 · 진통제 의존 · 깊은 상처 · 향수병을 돌보고 낫게 한다.
//  주컴퓨터가 한 시간마다 사람마다 마음을 살핀다 (점수 → 단계).
//   1 지침: 쉬라고 권한다 (쉼 · 취미 · 오락실 점수가 오른다).
//   2 마음 병: 말벗 로봇을 보낸다 — 곁에 앉아 이야기를 듣고 · 고향 소리를 틀고 · 숨 고르기를 같이 한다 (말벗이 없으면 간호 · 의사 로봇이 대신, 덜 듣는다).
//   3 깊음 (심하거나 · 사흘 넘게 나아지지 않거나 · 로봇이 못 오면): 사람이 이어받는다 — 상담 자격자 → 의무관 → 가장 친한 동료.
//  불면은 잠들기 전 숨 고르기 · 수면 안대 같은 손쉬운 처방으로 하루 잠을 덜 설친다.
//  돌봄을 받은 사람은 '곁에 누가 있어야' 낫는 병이 빨리 낫는다 (AilmentSystem.Heal).
//  환자가 늘면 휴게실 하나를 오락실로 고치고(개조) · 오락기 · 가상 고향 의자를 들이고 · 말벗 로봇을 더 짜 들인다.

/// <summary>한 사람의 마음 돌봄 기록.</summary>
public sealed class MindCase
{
    public int Crew { get; init; }
    /// <summary>1 지침 · 2 마음 병 · 3 깊음 (사람이 이어받는다).</summary>
    public int Level { get; set; }
    public string Why { get; set; } = "";
    public long Since { get; init; }
    public float Start { get; init; }
    public float Score { get; set; }
    public float Best { get; set; } = 9f;
    public long LastRobot { get; set; } = -1;
    public long LastHuman { get; set; } = -1;
    public long LastNudge { get; set; } = -1;
    public long LastPlay { get; set; } = -1;
    public long LastAid { get; set; } = -1;
    public float RobotEff { get; set; }
    public bool Counselor { get; set; }
    public int RobotSessions { get; set; }
    public int HumanSessions { get; set; }
    public int Helper { get; set; } = -1;
    public long HelperUntil { get; set; } = -1;
    public string? LastBy { get; set; }
}

public sealed class MindCareStats
{
    public int Opened, Closed, Nudges, RobotSessions, StandIns, HumanSessions, Escalations, Plays, HomeViews, SleepAids, Cured, Arcades, BotsWanted;
    public string Line() => $"돌봄 {Opened}명 (끝 {Closed}) · 쉼 권함 {Nudges} · 말벗 로봇 {RobotSessions} (대신 {StandIns}) · 사람 상담 {HumanSessions} (넘김 {Escalations}) · 오락실 {Plays}시간 · 고향 풍경 {HomeViews} · 잠 처방 {SleepAids} · 나은 마음 병 {Cured} · 오락실 {Arcades}";
}

public sealed class MindCareSystem
{
    /// <summary>시험: 끄면 예전처럼 (말벗 로봇 없음 · 돌봄 없음).</summary>
    public static bool Off;

    private readonly World _w;
    private long _nextHour;
    public Dictionary<int, MindCase> Cases { get; } = new();
    /// <summary>사람 돌봄이 → 맡은 환자.</summary>
    public Dictionary<int, int> Assigned { get; } = new();
    public MindCareStats Stats { get; } = new();
    /// <summary>지친 사람이 이 시각까지는 쉬기를 권받았다.</summary>
    public Dictionary<int, long> RestUntil { get; } = new();
    private int _crowdedHours;

    public MindCareSystem(World w) => _w = w;

    public static bool MindAilment(Ailment a) => AilmentSystem.Spec(a.Id).Group == AilmentGroup.Mind;

    /// <summary>마음 점수 (0 괜찮음 ~ 1.5): 긴장 · 마음 병 · 깊은 상처 · 향수병 · 슬픔.</summary>
    public float Score(CrewMember c)
    {
        var w = _w;
        float s = 0.6f * c.Needs.Stress;
        foreach (var a in c.Ailments) if (MindAilment(a)) s += 0.3f + 0.4f * w.Ailments.Severity(a);
        s += 0.5f * c.Memory.Trauma;
        s += 0.6f * MathF.Max(0f, w.Homeland.Of(c) - 0.5f);
        if (c.GriefUntil > w.Tick) s += 0.15f;
        return MathF.Min(1.5f, s);
    }

    private (int level, string why) Judge(CrewMember c, MindCase? old)
    {
        var w = _w;
        Ailment? worst = null;
        float sev = 0f;
        foreach (var a in c.Ailments)
            if (MindAilment(a)) { float v = w.Ailments.Severity(a); if (worst == null || v > sev) { worst = a; sev = v; } }
        float home = w.Homeland.Of(c);
        string why = worst != null ? AilmentSystem.Spec(worst.Id).Name
            : c.Memory.Trauma >= 0.4f ? $"깊은 상처 ({c.Memory.TraumaCause ?? "무서운 일"})"
            : home >= 0.7f ? "향수병"
            : c.GriefUntil > w.Tick ? "슬픔"
            : "지침";
        int level = worst != null || c.Memory.Trauma >= 0.4f || home >= 0.7f || c.GriefUntil > w.Tick ? 2 : c.Needs.Stress >= 0.55f ? 1 : 0;
        if (level == 2)
        {
            bool deep = sev >= 0.55f || worst?.Id == "ptsd" || c.Memory.Trauma >= 0.6f;
            bool stuck = old != null && old.Level >= 2 && w.Tick - old.Since > SimTime.Hours(72) && old.Score > old.Start * 0.85f;
            bool noBot = !Bots().Any();
            if (deep || stuck || noBot) level = 3;
            if (stuck && old!.Level == 2) why += " — 사흘째 그대로";
            else if (noBot && !deep) why += " — 로봇이 갈 수 없다";
        }
        return (level, why);
    }

    private IEnumerable<CrewMember> Aboard => _w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away);

    /// <summary>말벗 일을 맡을 수 있는 로봇 (말벗 로봇 먼저, 없으면 간호 · 의사 로봇이 대신).</summary>
    private IEnumerable<Robot> Bots() =>
        _w.Robots.Robots.Where(r => !r.Wrecked && r.State != RobotState.Lost && r.Kind is RobotKind.Companion or RobotKind.Nurse or RobotKind.Medic);

    public int Companions => _w.Robots.Robots.Count(r => r.Kind == RobotKind.Companion && !r.Wrecked && r.State != RobotState.Lost);

    private bool Emergency => _w.Fire.Count > 0 || _w.Commander.OpenCases.Any();

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        foreach (var c in Aboard.ToList())
        {
            Cases.TryGetValue(c.Id, out var old);
            var (level, why) = Judge(c, old);
            float score = Score(c);
            if (level == 0)
            {
                if (old != null) Close(c, old);
                continue;
            }
            if (old == null)
            {
                old = new MindCase { Crew = c.Id, Since = w.Tick, Start = score, Level = level, Why = why };
                Cases[c.Id] = old;
                Stats.Opened++;
                if (level == 3) { Stats.Escalations++; w.Automation.Reason($"mind3:{c.Id}", $"{c.Name}: {why} — 로봇만으로는 모자라다, 사람에게 부탁한다", SimTime.Hours(12)); }
                else if (level >= 2) w.Automation.Reason($"mind:{c.Id}", $"{Ko.IGa(c.Name)} 마음이 지쳤다 ({why}) — 말벗 로봇을 보낸다", SimTime.Hours(12));
            }
            else if (level == 3 && old.Level < 3)
            {
                Stats.Escalations++;
                w.Automation.Reason($"mind3:{c.Id}", $"{c.Name}: {why} — 로봇만으로는 모자라다, 사람에게 부탁한다", SimTime.Hours(12));
            }
            old.Level = level;
            old.Why = why;
            old.Score = score;
            old.Best = MathF.Min(old.Best, score);
            Play(c, old);
            if (level >= 1) Nudge(c, old);
            if (level >= 2) SendRobot(c, old);
            if (level >= 3) AskHuman(c, old);
            if (c.Ailments.Any(a => a.Id == "insomnia")) SleepAid(c, old);
        }
        foreach (var id in Cases.Keys.Where(id => w.Crew.FirstOrDefault(x => x.Id == id) is not CrewMember x || x.Dead || x.LeftShip).ToList()) Cases.Remove(id);
        foreach (var (h, p) in Assigned.ToList())
            if (!Cases.TryGetValue(p, out var cs) || cs.Level < 3 || w.Tick > cs.HelperUntil) Assigned.Remove(h);
        int heavy = Cases.Values.Count(x => x.Level >= 2);
        _crowdedHours = heavy > Math.Max(1, Companions) * 3 ? _crowdedHours + 1 : 0;
    }

    private void Close(CrewMember c, MindCase cs)
    {
        Cases.Remove(c.Id);
        RestUntil.Remove(c.Id);
        Stats.Closed++;
        if (cs.Level >= 2 && cs.RobotSessions + cs.HumanSessions > 0)
        {
            string by = cs.HumanSessions > 0 && cs.LastBy != null ? cs.LastBy : "말벗 로봇";
            _w.Log.Add(_w.Tick, LogKind.Life, $"{c.Name}: 마음이 가벼워졌다 ({cs.Why} — {by} 덕분에)", c.Id);
            Life.Diary(_w, c, $"요즘은 숨이 좀 쉬어진다. {Ko.IGa(by)} 곁에 있어 줬다.");
        }
    }

    // ─────────────────────────────── 1 쉼 권함 ───────────────────────────────

    private void Nudge(CrewMember c, MindCase cs)
    {
        var w = _w;
        if (Emergency || !c.IsAwake) return;
        RestUntil[c.Id] = w.Tick + SimTime.Hours(2);
        if (cs.LastNudge >= 0 && w.Tick - cs.LastNudge < SimTime.Hours(12)) return;
        cs.LastNudge = w.Tick;
        Stats.Nudges++;
        string place = HasArcade ? "오락실" : "휴게실";
        w.Automation.Reason($"rest:{c.Id}", $"{c.Name} 씨, 긴장이 높습니다 ({c.Needs.Stress * 100:0}%) — 두 시간쯤 {place}에서 쉬세요", SimTime.Hours(12));
    }

    /// <summary>쉬기를 권받은 사람: 쉼 · 취미 점수가 오른다 (Brain).</summary>
    public bool Resting(CrewMember c) => !Off && RestUntil.TryGetValue(c.Id, out var t) && t > _w.Tick;

    // ─────────────────────────────── 2 말벗 로봇 ───────────────────────────────

    private void SendRobot(CrewMember c, MindCase cs)
    {
        var w = _w;
        long gap = SimTime.Hours(cs.Level >= 3 ? 6 : 8);
        if (cs.LastRobot >= 0 && w.Tick - cs.LastRobot < gap) return;
        if (Emergency || !c.IsAwake || c.Outside || c.Room == null || c.Down || c.CarriedBy != null) return;
        if (w.Robots.Robots.Any(r => r.Steps != null && r.Doing.StartsWith("말벗", StringComparison.Ordinal) && r.Doing.Contains(c.Name))) return;
        if (w.Robots.SendCompanion(c, out var bot, out bool standIn))
        {
            cs.LastRobot = w.Tick; // 가는 중에도 다시 보내지 않는다
            if (standIn) Stats.StandIns++;
            bot.Mind.Say(standIn ? $"{c.Name} — 말벗 로봇이 없어 내가 간다" : $"{Ko.EulReul(c.Name)} 보러 간다 ({cs.Why})", w.Tick);
        }
    }

    /// <summary>로봇이 곁에 앉아 있는 동안 1분마다.</summary>
    internal void RobotMinute(Robot r, CrewMember c, float eff)
    {
        float k = eff * (c.Pose == Pose.Working ? 0.5f : 1f);
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.004f * k);
        c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.003f * k);
    }

    /// <summary>로봇이 곁에 앉아 다 듣고 일어난다.</summary>
    internal void RobotDone(Robot r, CrewMember c, float eff, int minutes)
    {
        var w = _w;
        if (minutes < 5) return;
        float k = eff * Math.Min(1f, minutes / 40f);
        c.Memory.Trauma = MathF.Max(0f, c.Memory.Trauma - 0.012f * k);
        w.Homeland.Ease(c, 0.04f * k);
        Stats.RobotSessions++;
        string what = Talk(c, out string diary);
        if (Cases.TryGetValue(c.Id, out var cs)) { cs.LastRobot = w.Tick; cs.RobotSessions++; cs.RobotEff = eff; }
        r.Mind.Say($"{c.Name} 곁에서 {what}", w.Tick);
        MarkLog.Add(r.Marks, w.Tick, $"{c.Name} 곁에서 {what} ({minutes}분)");
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(r.Name)} {c.Name} 곁에서 {what}", c.Id);
        Life.Diary(w, c, diary.Replace("{bot}", r.Name));
    }

    private string Talk(CrewMember c, out string diary)
    {
        var w = _w;
        if (c.Ailments.Any(a => a.Id == "insomnia")) { diary = "{bot}" + "가 잠들기 전에 숨 고르기를 같이 해 줬다."; return "잠들기 전 숨 고르기를 같이 했다"; }
        if (c.Ailments.Any(a => a.Id == "ptsd") || c.Memory.Trauma >= 0.4f) { diary = $"{c.Memory.TraumaCause ?? "그날"} 이야기를 " + "{bot}" + "에게 했다. 끝까지 들어 줬다."; return "그날 이야기를 끝까지 들었다"; }
        if (w.Homeland.Of(c) >= 0.6f) { diary = "{bot}" + $"가 {w.Homeland.HomeOf(c)}의 소리를 틀어 줬다. 눈을 감으니 거기 있는 것 같았다."; return $"{w.Homeland.HomeOf(c)}의 소리를 틀어 줬다"; }
        if (c.Ailments.Any(a => a.Id == "pkdep")) { diary = "약 생각이 날 때마다 " + "{bot}" + "가 다른 이야기로 돌려 줬다."; return "약 생각을 다른 이야기로 돌렸다"; }
        diary = "{bot}" + "가 곁에 앉아 이야기를 들어 줬다. 별 말은 없었지만 좀 나았다.";
        return "이야기를 들었다";
    }

    // ─────────────────────────────── 3 사람이 이어받는다 ───────────────────────────────

    private void AskHuman(CrewMember c, MindCase cs)
    {
        var w = _w;
        if (cs.LastHuman >= 0 && w.Tick - cs.LastHuman < SimTime.Hours(10)) return;
        if (Assigned.ContainsValue(c.Id)) return;
        bool Ok(CrewMember h) => h != c && h.CanAct && !h.Outside && !h.IsChild && h.Vitals.Health > 0.6f && !Assigned.ContainsKey(h.Id)
                                 && !(Cases.TryGetValue(h.Id, out var hc) && hc.Level >= 2);
        var pool = Aboard.Where(Ok).ToList();
        var helper = pool.Where(h => h.Quals.Contains(Qual.Counseling)).OrderByDescending(h => c.AffinityTo(h)).ThenBy(h => h.Id).FirstOrDefault()
                     ?? pool.Where(h => h.Role == CrewRole.Medic).OrderByDescending(h => c.AffinityTo(h)).ThenBy(h => h.Id).FirstOrDefault()
                     ?? pool.Where(h => c.AffinityTo(h) > 0.25f).OrderByDescending(h => c.AffinityTo(h)).ThenBy(h => h.Id).FirstOrDefault();
        if (helper == null) return;
        Assigned[helper.Id] = c.Id;
        cs.Helper = helper.Id;
        cs.HelperUntil = w.Tick + SimTime.Hours(10);
        cs.Counselor = helper.Quals.Contains(Qual.Counseling);
        string role = cs.Counselor ? "상담 자격자" : helper.Role == CrewRole.Medic ? "의무관" : "가장 가까운 동료";
        w.Automation.Reason($"ask:{c.Id}", $"{helper.Name} 씨({role}), {c.Name} 씨와 이야기를 나눠 주세요 — {cs.Why}", SimTime.Hours(10));
    }

    /// <summary>이 사람이 지금 마음을 살펴 줄 환자 (주컴퓨터 부탁).</summary>
    public CrewMember? PatientOf(CrewMember helper)
    {
        if (Off || !Assigned.TryGetValue(helper.Id, out var pid)) return null;
        var p = _w.Crew.FirstOrDefault(x => x.Id == pid);
        return p == null || p.Dead || !p.IsAwake || p.Outside || p.Room == null || p.CarriedBy != null ? null : p;
    }

    internal void HumanDone(CrewMember h, CrewMember p)
    {
        var w = _w;
        bool pro = h.Quals.Contains(Qual.Counseling);
        float k = pro ? 1f : h.Role == CrewRole.Medic ? 0.75f : 0.6f;
        p.Needs.Stress = MathF.Max(0f, p.Needs.Stress - 0.14f * k);
        p.Needs.Social = MathF.Min(1f, p.Needs.Social + 0.25f);
        p.Memory.Trauma = MathF.Max(0f, p.Memory.Trauma - 0.035f * k);
        w.Homeland.Ease(p, 0.06f * k);
        p.ChangeAffinity(h, 0.05f);
        h.ChangeAffinity(p, 0.04f);
        h.Needs.Stress = MathF.Min(1f, h.Needs.Stress + (pro ? 0.01f : 0.03f)); // 남의 무게를 듣는 것도 힘이 든다
        Stats.HumanSessions++;
        Assigned.Remove(h.Id);
        if (Cases.TryGetValue(p.Id, out var cs)) { cs.LastHuman = w.Tick; cs.HumanSessions++; cs.LastBy = h.Name; cs.Counselor = pro; cs.Helper = -1; }
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(h.Name)} {p.Name}와 오래 이야기를 나눴다{(pro ? " (상담)" : "")}", p.Id);
        Life.Diary(w, p, pro ? $"{Ko.IGa(h.Name)} 상담을 해 줬다. 처음으로 다 털어놨다." : $"{Ko.IGa(h.Name)} 찾아와 같이 앉아 있었다. 고마웠다.");
        Life.Diary(w, h, $"{p.Name}의 이야기를 들었다. 마음이 무겁지만 같이 버티기로 했다.");
    }

    // ─────────────────────────────── 불면 처방 ───────────────────────────────

    private void SleepAid(CrewMember c, MindCase cs)
    {
        var w = _w;
        if (cs.LastAid >= 0 && w.Tick - cs.LastAid < SimTime.Hours(24)) return;
        float hour = SimTime.HourOfDay(w.Tick);
        if (hour < 20f && hour > 4f) return; // 밤에만
        cs.LastAid = w.Tick;
        Stats.SleepAids++;
        w.Automation.Reason($"sleep:{c.Id}", $"{c.Name} 씨 — 오늘 밤은 침실 조명을 일찍 낮추고 안대와 백색 소음을 켜 둡니다", SimTime.Hours(24));
    }

    /// <summary>불면 처방을 받은 밤: 잠을 덜 설친다 (AilmentSystem.Fx).</summary>
    public bool Aided(CrewMember c) => !Off && Cases.TryGetValue(c.Id, out var cs) && cs.LastAid >= 0 && _w.Tick - cs.LastAid < SimTime.Hours(14);

    // ─────────────────────────────── 오락실 ───────────────────────────────

    public bool HasArcade => _w.Ship.Rooms.Any(r => r.Kind == RoomType.Arcade && !r.Detached && !r.Abandoned);

    /// <summary>한 시간마다: 오락실에서 쉬고 있는 사람 (가상 고향 의자가 있으면 향수병이 풀린다).</summary>
    private void Play(CrewMember c, MindCase cs)
    {
        var w = _w;
        if (c.Room?.Kind != RoomType.Arcade || c.Job?.Activity is not (RelaxActivity or HobbyActivity or ChatActivity)) return;
        cs.LastPlay = w.Tick;
        Stats.Plays++;
        if (ModulesV15.Has(c.Room, FurnitureType.VrChair))
        {
            Stats.HomeViews++;
            w.Homeland.Ease(c, 0.03f);
            c.Memory.Trauma = MathF.Max(0f, c.Memory.Trauma - 0.004f);
            if (w.Homeland.Of(c) > 0.4f && cs.LastPlay - cs.Since < SimTime.Hours(1)) Life.Diary(w, c, $"가상 고향 의자에서 {w.Homeland.HomeOf(c)}의 골목을 걸었다.");
        }
    }

    /// <summary>'곁에 누가 있어야' 낫는 병에 더하는 하루 회복 (상담 · 말벗 로봇 · 오락실 · 잠 처방 — 열두 시간 안).</summary>
    public float CureGain(CrewMember c, Ailment a)
    {
        if (Off || !Cases.TryGetValue(c.Id, out var cs)) return 0f;
        long now = _w.Tick, win = SimTime.Hours(12);
        float g = 0f;
        if (cs.LastHuman >= 0 && now - cs.LastHuman < win) g += cs.Counselor ? 1.6f : 1f;
        if (cs.LastRobot >= 0 && now - cs.LastRobot < win && cs.RobotSessions > 0) g += 0.8f * cs.RobotEff;
        if (cs.LastPlay >= 0 && now - cs.LastPlay < win) g += 0.4f;
        if (a.Id == "insomnia" && Aided(c)) g += 0.8f;
        g += _w.Pets.ComfortGain(c); // 반려동물 곁
        return g;
    }

    /// <summary>오락실 개조 후보 점수 (Evolution 방 용도 변경): 마음이 지친 사람이 둘 넘거나 다들 긴장이 높다.</summary>
    public (float score, string why) ArcadeWant()
    {
        if (Off || HasArcade) return (0f, "");
        int heavy = Cases.Values.Count(x => x.Level >= 2);
        float stress = Aboard.Select(c => c.Needs.Stress).DefaultIfEmpty(0f).Average();
        if (heavy >= 2) return (0.45f + 0.12f * heavy, $"마음이 지친 사람 {heavy}명");
        if (stress > 0.5f) return (0.35f + stress - 0.5f, $"다들 긴장이 높다 ({stress * 100:0}%)");
        return (0f, "");
    }

    /// <summary>오락실 설비 (오락기 · 가상 고향 의자): 오락실이 생기면.</summary>
    public (float, string) ArcadeNeed(FurnitureType t)
    {
        if (Off || !HasArcade) return (0f, "");
        return t == FurnitureType.VrChair && Aboard.Any(c => _w.Homeland.Of(c) >= 0.5f) ? (0.55f, "고향을 그리워하는 사람이 있다") : (0.45f, "오락실을 채운다");
    }

    /// <summary>말벗 로봇이 아직 없을 때의 개조 후보 (RobotsV15).</summary>
    public (float, string) Need()
    {
        if (Off) return (0f, "");
        int heavy = Cases.Values.Count(x => x.Level >= 2);
        return heavy >= 1 ? (0.4f + 0.1f * heavy, $"마음이 지친 사람 {heavy}명") : (0f, "");
    }

    /// <summary>말벗 로봇이 있어도 모자랄 때 (환자가 로봇 한 대에 셋 넘게 · 이틀째): 한 대 더 짜 들인다.</summary>
    public IEnumerable<UpgradePlan> Candidates()
    {
        var w = _w;
        if (Off || Companions == 0 || _crowdedHours < 48 || Companions >= 4) yield break;
        if (RobotsV15.Bot(RobotKind.Companion) is not { } bot || RobotsV15.DockSpot(w, bot.Base) is not { } spot) yield break;
        int heavy = Cases.Values.Count(x => x.Level >= 2);
        Stats.BotsWanted++;
        yield return new UpgradePlan(UpgradeKind.Robot, WorkTarget.AtCell(spot.at, spot.room), 0.5f + 0.05f * heavy, Skill.Electrical,
            $"마음이 지친 사람 {heavy}명 — 말벗 로봇 {Companions}대로는 모자라다 → {spot.room.Name}에 {bot.Name}", bot.Cost, 0.3f, RobotsV15.Code(RobotKind.Companion));
    }

    // ─────────────────────────────── 카드 ───────────────────────────────

    public string? CardLine(CrewMember c)
    {
        if (Off || !Cases.TryGetValue(c.Id, out var cs)) return null;
        string lvl = cs.Level switch { 1 => "지침 — 쉬라고 권함", 2 => "말벗 로봇이 돌봄", _ => "사람이 이어받음" };
        var parts = new List<string> { $"마음 돌봄: {cs.Why} · {lvl}" };
        if (cs.RobotSessions > 0) parts.Add($"말벗 {cs.RobotSessions}번");
        if (cs.HumanSessions > 0) parts.Add($"상담 {cs.HumanSessions}번{(cs.LastBy != null ? $" ({cs.LastBy})" : "")}");
        if (cs.Helper >= 0 && _w.Crew.FirstOrDefault(x => x.Id == cs.Helper) is CrewMember h) parts.Add($"{h.Name}에게 부탁함");
        if (cs.LastPlay >= 0) parts.Add("오락실");
        return string.Join(" · ", parts);
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Cases.Count);
        foreach (var cs in Cases.Values.OrderBy(x => x.Crew)) { I(cs.Crew); I(cs.Level); I(cs.RobotSessions); I(cs.HumanSessions); F(cs.Score); }
        I(Stats.RobotSessions); I(Stats.HumanSessions); I(Stats.Plays);
    }
}

/// <summary>사람 돌봄이: 주컴퓨터가 부탁한 사람 곁에 가서 오래 이야기를 나눈다 (환자는 그동안 자리에 앉는다).</summary>
public sealed class CareActivity : Activity
{
    public override string Id => "mindcare";
    public override string Label => "마음 살피기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (MindCareSystem.Off || w.MindCare.PatientOf(c) is not CrewMember p) return (0f, "—");
        if (c.Down || c.Outside || c.Needs.Food < 0.25f || c.Needs.Rest < 0.2f || !dist.Reachable(p.Cell)) return (0f, "—");
        return (0.7f + 0.15f * c.AffinityTo(p), $"주컴퓨터 부탁 — {Ko.EulReul(p.Name)} 살핀다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.MindCare.PatientOf(c) is not CrewMember p) return null;
        var spot = Cell.Dirs8.Select(d => p.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell s) return null;
        var toils = new List<Toil>
        {
            new GotoToil(s),
            new DoToil((cm, world) =>
            {
                if (p.Dead || (p.Position - cm.Position).LengthSquared() > 9f) return false;
                if (p.Job?.Order is not { Urgency: >= 0.5f }) { p.HoldUntil = world.Tick + SimTime.Minutes(40); p.HoldWhy = $"{cm.Name}와 이야기한다"; }
                return true;
            }),
            new WaitToil(SimTime.Minutes(40), Pose.Sitting, p.Position) { DoneWhen = (cm, world) => p.Dead || (p.Position - cm.Position).LengthSquared() > 9f },
            new DoToil((cm, world) =>
            {
                if (p.Dead || (p.Position - cm.Position).LengthSquared() > 9f) return false;
                world.MindCare.HumanDone(cm, p);
                return true;
            }),
        };
        return new Job(this, "마음 살피기", toils) { LogText = $"{Ko.EulReul(p.Name)} 찾아가 이야기를 나눈다", LogKind = LogKind.Life, TargetRoom = p.Room };
    }
}

/// <summary>말벗 로봇: 사람 곁에 붙어 앉아 이야기를 듣는다 (사람이 움직이면 따라간다 · 비상이 나면 일어난다).</summary>
internal sealed class RCompanion : RobotStep
{
    private readonly CrewMember _c;
    private readonly float _eff;
    private readonly int _minutes;
    private long _sat, _start = -1, _repath, _begun = -1;
    private int _done;
    public RCompanion(CrewMember c, float eff, int minutes) { _c = c; _eff = eff; _minutes = minutes; }
    public override float? Progress => _done / (float)_minutes;
    public override bool Moving => _start < 0;

    public override void Begin(Robot r, World w) => Toward(r, w, _c);

    /// <summary>사람 곁 빈칸으로 (앉은 의자 · 침대 칸은 로봇이 못 들어간다 — 둘레 두 칸 안에서 가까운 곳).</summary>
    private static bool Toward(Robot r, World w, CrewMember c)
    {
        if (w.Ship.IsWalkable(c.Cell) && RobotSystem.SetDestination(r, w, c.Cell)) return true;
        foreach (var n in Cell.Dirs8.Select(d => c.Cell + d).Concat(Cell.Dirs8.Select(d => c.Cell + d + d)))
            if (w.Ship.IsWalkable(n) && RobotSystem.SetDestination(r, w, n)) return true;
        return false;
    }

    public override ToilStatus Tick(Robot r, World w)
    {
        var c = _c;
        bool gone = c.Dead || c.Outside || c.Room == null || c.CarriedBy != null || !c.IsAwake || c.Away;
        bool alarm = w.Fire.Count > 0 || w.Commander.OpenCases.Any();
        if (gone || alarm)
        {
            if (_done > 0) w.MindCare.RobotDone(r, c, _eff, _done);
            if (alarm && !gone) r.Mind.Say($"비상 — {c.Name} 곁에서 일어난다", w.Tick);
            if (c.HoldWhy != null && c.HoldWhy.StartsWith(r.Name, StringComparison.Ordinal)) c.HoldUntil = -1;
            return ToilStatus.Succeeded;
        }
        float d = (c.Position - r.Position).Length();
        if (_begun < 0) _begun = w.Tick;
        if (d > 2.3f)
        {
            if (r.Path == null || w.Tick >= _repath) { _repath = w.Tick + SimTime.Minutes(2); if (!Toward(r, w, c)) r.Path = null; } // 길이 막히면 잠깐 기다렸다 다시
            if (r.Path != null) RobotSystem.Move(r, w);
            if (_start >= 0 ? w.Tick - _sat > SimTime.Minutes(20) : w.Tick - _begun > SimTime.Minutes(45)) return _done > 0 ? Finish(r, w) : ToilStatus.Failed; // 자꾸 자리를 뜨면 다음에
            return ToilStatus.Running;
        }
        r.Path = null;
        var to = c.Position - r.Position;
        if (to.LengthSquared() > 0.01f) r.Facing = Vector2.Normalize(to);
        if (_start < 0)
        {
            _start = w.Tick;
            r.Doing = $"말벗 — {c.Name} 곁에 앉았다";
            if (c.Job?.Order is not { Urgency: >= 0.5f } && c.Pose != Pose.Working) { c.HoldUntil = w.Tick + SimTime.Minutes(_minutes); c.HoldWhy = $"{r.Name}와 이야기한다"; }
        }
        _sat = w.Tick;
        if ((w.Tick - _start) % SimTime.Minutes(1) == 0 && w.Tick > _start)
        {
            _done++;
            w.MindCare.RobotMinute(r, c, _eff);
        }
        return _done >= _minutes ? Finish(r, w) : ToilStatus.Running;
    }

    private ToilStatus Finish(Robot r, World w)
    {
        w.MindCare.RobotDone(r, _c, _eff, _done);
        return ToilStatus.Succeeded;
    }
}

public sealed partial class RobotSystem
{
    /// <summary>v23 말벗: 쉬는 말벗 로봇을 보낸다 (없으면 쉬는 간호 · 의사 로봇이 대신 — 덜 듣는다).</summary>
    internal bool SendCompanion(CrewMember c, out Robot bot, out bool standIn)
    {
        bot = null!;
        standIn = false;
        var w = _world;
        Robot? pick = Robots.Where(r => r.Kind == RobotKind.Companion && Free(r)).OrderBy(r => Far(r.Position, c.Position)).ThenBy(r => r.Id).FirstOrDefault();
        if (pick == null && !Robots.Any(r => r.Kind == RobotKind.Companion && r.Operational && !r.Wrecked))
        {
            pick = Robots.Where(r => r.Kind is RobotKind.Nurse or RobotKind.Medic && Free(r)).OrderBy(r => Far(r.Position, c.Position)).ThenBy(r => r.Id).FirstOrDefault();
            standIn = pick != null;
        }
        if (pick == null) return false;
        float eff = standIn ? 0.6f : 1f;
        var steps = new List<RobotStep> { new RCompanion(c, eff, 40) };
        Begin(pick, steps, $"말벗 — {Ko.EulReul(c.Name)} 보러 간다");
        FleetSystem.Stage(pick, steps, null);
        bot = pick;
        return true;
    }
}
