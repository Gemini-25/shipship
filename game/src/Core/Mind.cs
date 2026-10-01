using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v13.3 판단·인지·감정: 사람은 모든 것을 알지 못하고, 겁에 질리고, 화가 나고, 영웅이 되기도 한다.
//
// 목표 계층: 생존 > 맡은 역할(조) > 일 > 생활. 지금 하는 일이 어느 층인지 보인다. 새로 알게 되면 그 자리에서 다시 판단한다.
// 아는 것의 차이: 사고마다 누가 아는지 — 직접 봤다(그 방·옆방·가까이) · 경보(데이터선이 닿은 방의 사고를 컴퓨터가 배 전체에 알린다,
//   생체 감시 모듈이면 쓰러진 사람은 데이터선 없이도) · 무전(지휘자가 조를 붙이며 알린다, 본 사람이 지휘자에게 보고한다) · 소문(같은 방에 아는 사람이 있으면).
//   모르는 사고의 일은 하지 않는다 — 데이터선이 끊긴 방의 불은 누가 지나가다 볼 때까지 아무도 모른다.
// 감정 → 행동: 공황(위험 · 눈앞의 죽음 — 침착하지 않을수록, 지쳤을수록, 난이도가 높을수록: 느긋이면 잠깐 달아나고, 어려움·가혹이면 얼어붙기도 한다) ·
//   분노(비난 · 말다툼 · 진 표결 — 명령을 덜 듣는다) · 영웅심(용감한 사람이 가까운 사람이 쓰러진 걸 알면 우주복 없이도 뛰어든다).
// 명령 반응: 지휘를 따르는 정도 = 규칙/자유 가치관 · 지휘자와의 관계 · 선장(또는 컴퓨터) 신뢰 − 분노. 낮으면 맡은 조를 두고 딴일을 한다 (명령 무시).
// 컴퓨터 신뢰: 소화 수순이 사람을 잃지 않고 끝나면 오르고, 수순 중 사람이 죽거나 격벽이 안 닫혀 틀어지면 떨어진다 → 별명.

public enum KnowSource { Seen, Alarm, Radio, Rumor }
public enum GoalTier { Survival, Role, Work, Life }

public sealed class MindState
{
    /// <summary>아는 사고: 열쇠(fire:방 · breach:방 · down:사람) → 어떻게 · 언제.</summary>
    public Dictionary<string, (KnowSource src, long tick, string what)> Knows { get; } = new();
    public float Anger { get; set; }
    public long PanicUntil { get; set; } = -1;
    public bool Frozen { get; set; }
    public long HeroUntil { get; set; } = -1;
    public int HeroFor { get; set; } = -1;
    public GoalTier Goal { get; set; } = GoalTier.Life;
    public string GoalWhy { get; set; } = "";
    public int Ignored { get; set; }
    public long LastIgnored { get; set; } = -1;
    public int Panics { get; set; }

    public bool Panicking(long tick) => PanicUntil > tick;
    public bool Heroic(long tick) => HeroUntil > tick;
}

public sealed class MindSystem
{
    private readonly World _w;
    private readonly Rng _rng;
    private readonly Dictionary<int, long> _downSince = new();
    public int Panics, Freezes, Flees, Heroics, Ignores, Rumors, Radios, Alarms, Sightings;
    public int FearPanics; // v14.0 두려움 때문에
    private string _nick = "";

    public MindSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 5167 + 29));
    }

    public static string SourceName(KnowSource s) => s switch { KnowSource.Seen => "직접 봄", KnowSource.Alarm => "경보", KnowSource.Radio => "무전", _ => "소문" };
    public static string GoalName(GoalTier g) => g switch { GoalTier.Survival => "생존", GoalTier.Role => "맡은 역할", GoalTier.Work => "일", _ => "생활" };

    /// <summary>이 일이 걸린 사고 (모르면 하지 않는 일).</summary>
    public static string? Key(WorkOrder o) => o.Kind switch
    {
        WorkKind.Extinguish => o.Target.CurrentRoom is Room r ? $"fire:{r.Id}" : null,
        WorkKind.SealBreach => o.Target.CurrentRoom is Room r2 && !o.External ? $"breach:{r2.Id}" : null,
        WorkKind.Rescue => o.Target.Crew is CrewMember p ? $"down:{p.Id}" : null,
        _ => null,
    };

    /// <summary>이 사람이 이 일의 사고를 아는가 (모르는 사고의 일은 하지 않는다).</summary>
    public bool Aware(CrewMember c, WorkOrder o) => Key(o) is not string k || c.Mind.Knows.ContainsKey(k) || o.Assignee == c;

    /// <summary>컴퓨터가 배 전체에 알릴 수 있는 방 (데이터선이 닿고 주 컴퓨터나 예비 제어기가 돈다).</summary>
    public bool AlarmReaches(Room? r)
    {
        var a = _w.Automation;
        if (r == null) return false;
        if (!a.Present) return true; // 컴퓨터가 없는 옛 배: 경보 벨만으로 (예전처럼)
        return (a.MainOnline || a.BackupActive) && r.DataLinked;
    }

    /// <summary>지휘하는 쪽(사람 지휘자 · 컴퓨터)이 이 일의 사고를 아는가.</summary>
    public bool CommandKnows(WorkOrder o)
    {
        if (Key(o) is not string k) return true;
        var cmd = _w.Command;
        if (cmd.ComputerCommands) return Incidents().Any(i => i.key == k && i.alarm);
        return cmd.Commander is not CrewMember boss || boss.Mind.Knows.ContainsKey(k);
    }

    private List<(string key, Room? room, string what, bool alarm, CrewMember? person)> _incidents = new();
    private long _incTick = -1;

    /// <summary>지금 있는 사고와 경보가 닿는지.</summary>
    public List<(string key, Room? room, string what, bool alarm, CrewMember? person)> Incidents()
    {
        var w = _w;
        if (_incTick == w.Tick) return _incidents;
        _incTick = w.Tick;
        var list = new List<(string, Room?, string, bool, CrewMember?)>();
        foreach (var (room, _, _) in w.Fire.KnownFires()) list.Add(($"fire:{room.Id}", room, $"{room.Name} 불", AlarmReaches(room), null));
        foreach (var room in w.Ship.LiveRooms.Where(r => r.Leaking && !r.Abandoned)) list.Add(($"breach:{room.Id}", room, $"{room.Name} 구멍", AlarmReaches(room), null));
        bool bio = w.Automation.Has(ComputerModule.BioMonitor) && w.Automation.MainOnline;
        foreach (var c in w.Crew.Where(c => c.Down && !c.Dead && c.CareBed == null && c.CarriedBy == null))
            list.Add(($"down:{c.Id}", c.Room, c.Outside ? $"{c.Name} 선체 밖" : $"{c.Name} 쓰러짐",
                bio || (c.Room == null ? w.Automation.MainOnline || !w.Automation.Present : AlarmReaches(c.Room)), c)); // 선체 밖·문간은 외부 감지기·카메라
        _incidents = list;
        return list;
    }

    private bool Sees(CrewMember c, Room? room, CrewMember? person)
    {
        if (c.Room == null || room == null) return person != null && c.Outside && person.Outside && (c.Position - person.Position).LengthSquared() < 36f;
        if (person != null && (c.Position - person.Position).LengthSquared() < 16f) return true;
        if (c.Room == room) return true;
        // 열린 문 너머 옆방 (연기·불빛·경보 벨)
        return room.Doors.Any(d => (d.RoomA == c.Room || d.RoomB == c.Room) && (d.Openness > 0.3f || person == null));
    }

    private void Learn(CrewMember c, string key, KnowSource src, string what)
    {
        var w = _w;
        c.Mind.Knows[key] = (src, w.Tick, what);
        c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1); // 알게 된 그 자리에서 다시 판단한다
        switch (src)
        {
            case KnowSource.Seen: Sightings++; break;
            case KnowSource.Alarm: Alarms++; break;
            case KnowSource.Radio:
                Radios++;
                w.Log.Add(w.Tick, LogKind.Life, $"무전으로 들었다 — {what}", c.Id);
                break;
            case KnowSource.Rumor:
                Rumors++;
                w.Log.Add(w.Tick, LogKind.Life, $"옆 사람에게 들었다 — {what}", c.Id);
                break;
        }
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        var incidents = Incidents();
        var cmd = w.Command;
        foreach (var inc in incidents)
        {
            // 본 사람이 지휘자에게 보고한다 (30초 뒤)
            bool reported = w.Crew.Any(x => x.Mind.Knows.TryGetValue(inc.key, out var k) && k.src == KnowSource.Seen && w.Tick - k.tick >= SimTime.Minutes(0.5f) && x.CanAct);
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Down || c.IsChild || c.Mind.Knows.ContainsKey(inc.key) || inc.person == c) continue;
                KnowSource? src = null;
                if (c.IsAwake && Sees(c, inc.room, inc.person)) src = KnowSource.Seen;
                else if (inc.alarm && (c.IsAwake || !c.DeepAsleep)) src = KnowSource.Alarm;
                else if (cmd.Active && (cmd.Commander == c || cmd.CaptainId == c.Id) && reported) src = KnowSource.Radio;
                else if (cmd.Active && cmd.TeamOf(c) is Team t && TeamIncident(t) == inc.key) src = KnowSource.Radio;
                else if (c.IsAwake && c.Room != null && _rng.Chance(0.6f)
                         && w.Crew.Any(x => x != c && x.Room == c.Room && x.IsAwake && x.Mind.Knows.TryGetValue(inc.key, out var k) && w.Tick - k.tick >= SimTime.Minutes(0.3f)))
                    src = KnowSource.Rumor;
                if (src is KnowSource s) Learn(c, inc.key, s, inc.what);
            }
        }
        // 끝난 사고는 잊는다
        var live = incidents.Select(i => i.key).ToHashSet();
        foreach (var c in w.Crew)
            foreach (var k in c.Mind.Knows.Keys.Where(k => !live.Contains(k)).ToList()) c.Mind.Knows.Remove(k);

        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            Emotions(c, dt);
            Goal(c);
            Obey(c);
        }
        Nickname();
    }

    private static string TeamIncident(Team t) => t.Kind switch
    {
        TeamKind.Fire => $"fire:{t.Room?.Id}",
        TeamKind.Breach => $"breach:{t.Room?.Id}",
        TeamKind.Rescue => t.Key.Replace("Rescue:", "down:"),
        _ => "",
    };

    // ───────────────────────────── 감정 ─────────────────────────────

    /// <summary>난이도에 따른 공황의 세기 (느긋 0.15 ~ 가혹 1.8).</summary>
    public static float PanicScale => Storyteller.Level switch { 1 => 0.15f, 2 => 0.4f, 3 => 0.8f, 4 => 1.3f, _ => 1.8f };

    /// <summary>지금 이 사람이 공황에 빠질 확률 (한 시간에) — 위험한 자리 · 쓰러지는 걸 봤다 · v14.0 두려움 · 습관.</summary>
    public float PanicRate(CrewMember c, out Fear? fear)
    {
        var w = _w;
        var m = c.Mind;
        fear = null;
        float danger = EvacuateActivity.DangerHere(c, w);
        bool witness = m.Knows.Any(k => k.Key.StartsWith("down:") && k.Value.src == KnowSource.Seen && w.Tick - k.Value.tick < SimTime.Minutes(3));
        float trigger = (danger > 0.5f ? 10f : danger >= 0.3f ? 3f : 0f) + (witness ? 6f : 0f);
        // v14.0 두려움이 건드려지면 (불·물·가스·진공…) 공황이 잦다 · 습관 (걱정·미신은 잦고, 낙천가·겁 없음은 드물다)
        // (어둠·좁은 곳·기계·지휘·병 같은 오래가는 두려움은 위험할 때만 겹친다 — 평소엔 마음만 무겁다)
        if (c.Fears.Count > 0 && Persona.Triggered(w, c) is Fear fr && (trigger > 0f || Persona.Acute(fr)))
        {
            trigger = trigger > 0f ? trigger * 1.5f + 4f : 3f;
            fear = fr;
        }
        if (trigger <= 0f) return 0f;
        if (c.Habits.Count > 0) trigger *= Persona.Mul(c, h => h.Panic);
        float veteran = MathF.Min(0.6f, c.Stats.Emergencies * 0.03f);
        return PanicScale * trigger * (1f + c.Fx.Panic) * MathF.Pow(1f - c.Traits.Calm, 1.5f) * (0.3f + c.Needs.Stress) * (1f - veteran) * (1f - 0.5f * c.Traits.Bravery) * (1.5f - w.Society.Morale);
    }

    private void Emotions(CrewMember c, float dt)
    {
        var w = _w;
        var m = c.Mind;
        m.Anger = MathF.Max(0f, m.Anger - 0.25f * dt / 24f);
        if (c.Down || !c.IsAwake || c.Outside) return;
        // v14.0 오래가는 두려움 (어둠·좁은 곳·기계…) — 평소엔 마음만 무겁다
        if (c.Fears.Count > 0 && Persona.Triggered(w, c) is Fear cf && !Persona.Acute(cf)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f * dt);
        // 공황
        if (!m.Panicking(w.Tick) && m.PanicUntil < w.Tick - SimTime.Minutes(20))
        {
            float p = PanicRate(c, out var fear);
            if (p > 0f)
            {
                if (_rng.Chance(p * dt))
                {
                    m.Panics++;
                    Panics++;
                    // 어려움·가혹에서 겁 많은 사람은 얼어붙는다 (위험한 자리에서 꼼짝 못 한다)
                    m.Frozen = Storyteller.Level >= 4 && c.Traits.Bravery < 0.45f && _rng.Chance(0.6f);
                    float minutes = m.Frozen ? 1f + 1.5f * _rng.Float() : 2f + 4f * _rng.Float() * MathF.Min(1f, PanicScale);
                    m.PanicUntil = w.Tick + SimTime.Minutes(minutes);
                    if (m.Frozen) Freezes++; else Flees++;
                    c.EndJob(w, ToilStatus.Interrupted);
                    c.NextThinkTick = w.Tick;
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.08f);
                    w.Log.Add(w.Tick, LogKind.Warning, (m.Frozen ? "공황 — 얼어붙어 꼼짝 못 한다" : "공황 — 하던 일을 두고 정신없이 달아난다") + (fear is Fear ff ? $" ({Persona.Of(ff).Name}을(를) 무서워한다)" : ""), c.Id);
                    if (fear is Fear f2) { FearPanics++; Life.Diary(w, c, Persona.Say(c, $"{Persona.Of(f2).Name}... 몸이 말을 듣지 않았다")); }
                    MarkLog.Add(c.Memory.Marks, w.Tick, m.Frozen ? "공황에 얼어붙었다" : "공황에 달아났다");
                }
            }
        }
        // 영웅심: 용감한 사람이 가까운 사람이 쓰러진 걸 알았다
        if (!m.Heroic(w.Tick) && c.Traits.Bravery > 0.6f && !m.Panicking(w.Tick))
        {
            foreach (var (key, _) in m.Knows)
            {
                if (!key.StartsWith("down:") || !int.TryParse(key[5..], out int id)) continue;
                var p = w.Crew.FirstOrDefault(x => x.Id == id);
                if (p == null || p.Dead || c.AffinityTo(p) < 0.35f) continue;
                if (p.Room != null && Atmosphere.Danger(p.Room) < 0.3f && !p.Room.Leaking && w.Fire.CountIn(p.Room) == 0) continue; // (방 밖·선체 밖이면 위험한 곳)
                m.HeroUntil = w.Tick + SimTime.Minutes(20);
                m.HeroFor = id;
                Heroics++;
                c.NextThinkTick = w.Tick;
                w.Log.Add(w.Tick, LogKind.Warning, $"영웅심 — {Ko.EulReul(p.Name)} 구하러 뛰어든다", c.Id);
                break;
            }
        }
    }

    /// <summary>분노를 더한다 (비난 · 말다툼 · 진 표결).</summary>
    public static void Anger(CrewMember c, float amount) => c.Mind.Anger = MathF.Min(1f, c.Mind.Anger + amount);

    // ───────────────────────────── 목표 계층 · 명령 반응 ─────────────────────────────

    private void Goal(CrewMember c)
    {
        var w = _w;
        var job = c.Job;
        var (g, why) = job?.Activity switch
        {
            PanicActivity => (GoalTier.Survival, c.Mind.Frozen ? "공황 — 얼어붙었다" : "공황 — 달아난다"),
            EvacuateActivity or ShelterActivity or RefillSuitActivity or RecoverActivity => (GoalTier.Survival, job!.Label),
            EatActivity when c.Needs.Hunger > 0.93f => (GoalTier.Survival, "굶주림"),
            ChoresActivity when job!.Order is WorkOrder o && w.Command.TeamOf(c) is Team t && t.Kind != TeamKind.Reserve
                                && (CommandSystem.Group(o.Kind) == t.Kind || o.Kind == WorkKind.SafetyWatch) => (GoalTier.Role, $"{CommandSystem.TeamName(t.Kind)} — {o.Title}"),
            ChoresActivity or DutyActivity or MeetingActivity => (GoalTier.Work, job!.Label),
            null => (GoalTier.Life, "쉬는 중"),
            _ => (GoalTier.Life, job!.Label),
        };
        c.Mind.Goal = g;
        c.Mind.GoalWhy = why;
    }

    /// <summary>지휘를 따르는 정도 0~1.2: 규칙/자유 · 지휘자와의 관계 · 신뢰 − 분노.</summary>
    public float Obedience(CrewMember c)
    {
        var cmd = _w.Command;
        float trust = cmd.ComputerCommands ? cmd.ComputerTrust : cmd.Trust;
        float rel = cmd.Commander is CrewMember boss && boss != c ? c.AffinityTo(boss) : 0f;
        float o = 0.75f + (c.Value == CrewValue.Rules ? 0.15f : c.Value == CrewValue.Freedom ? -0.2f : 0f) + 0.3f * rel + 0.6f * (trust - 0.55f) - 0.7f * c.Mind.Anger;
        if (c.Mind.Panicking(_w.Tick)) o = 0f;
        return Math.Clamp(o, 0.1f, 1.2f);
    }

    /// <summary>조를 맡았는데 딴 급한 일을 하고 있다 — 명령 무시 (따르는 정도가 낮을 때).</summary>
    private void Obey(CrewMember c)
    {
        var w = _w;
        var cmd = w.Command;
        if (!cmd.Active || cmd.TeamOf(c) is not Team t || t.Kind == TeamKind.Reserve || c.Job?.Order is not WorkOrder o) return;
        if (o.Urgency < 0.85f || CommandSystem.Group(o.Kind) == t.Kind || o.Kind == WorkKind.SafetyWatch || t.Watcher == c.Id) return;
        if (Obedience(c) >= 0.45f || w.Tick - c.Mind.LastIgnored < SimTime.Minutes(30)) return;
        c.Mind.Ignored++;
        c.Mind.LastIgnored = w.Tick;
        Ignores++;
        if (!cmd.ComputerCommands) cmd.Trust = MathF.Max(0f, cmd.Trust - 0.01f);
        w.Log.Add(w.Tick, LogKind.Warning, $"명령 무시 — {CommandSystem.TeamName(t.Kind)}을 두고 {Ko.EulReul(o.Title)} 한다 ({cmd.CommanderName}의 지시보다 제 판단)", c.Id);
        if (c.Mind.Ignored == 1) w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(c.Name)} {cmd.CommanderName}의 지시를 무시했다 — {CommandSystem.TeamName(t.Kind)} 대신 {o.Title}", c.Room, new[] { c });
        if (c.Mind.Ignored >= 2) w.Society.Punish(c, "명령 무시", light: false); // v13.4 규칙 위반
        if (cmd.Commander is CrewMember boss && boss != c) w.Relations.Remember(boss, c, RelationReason.IgnoredMyWarning, $"내 지시를 무시하고 {Ko.EulReul(o.Title)} 했다"); // v14.4
    }

    // ───────────────────────────── 컴퓨터 신뢰 ─────────────────────────────

    public void ComputerResult(float delta, string why)
    {
        var cmd = _w.Command;
        float before = cmd.ComputerTrust;
        cmd.ComputerTrust = Math.Clamp(cmd.ComputerTrust + delta, 0.05f, 0.98f);
        if (MathF.Abs(delta) >= 0.05f)
            _w.Log.Add(_w.Tick, LogKind.Ship, $"컴퓨터 신뢰 {before * 100:0}% → {cmd.ComputerTrust * 100:0}% — {why}");
    }

    /// <summary>승무원이 주 컴퓨터를 부르는 별명.</summary>
    public string ComputerNick
    {
        get
        {
            float t = _w.Command.ComputerTrust;
            return t >= 0.85f ? "믿음직한 V" : t >= 0.7f ? "든든한 V" : t >= 0.45f ? "" : t >= 0.3f ? "말 많은 V" : "양치기 V";
        }
    }

    private void Nickname()
    {
        string n = ComputerNick;
        if (n == _nick) return;
        bool first = _nick == "" && n == "";
        _nick = n;
        if (first) return;
        _w.History.Add(_w, HistoryKind.Decision, n == "" ? "승무원들이 주 컴퓨터를 별명 없이 부르게 됐다" : $"승무원들이 주 컴퓨터를 '{n}'(이)라 부르기 시작했다 (신뢰 {_w.Command.ComputerTrust * 100:0}%)", log: true);
    }
}

/// <summary>v13.3 공황: 얼어붙거나(그 자리에 굳는다) 달아난다(먼 안전한 방으로). 끝나면 제정신이 든다.</summary>
public sealed class PanicActivity : Activity
{
    public override string Id => "panic";
    public override string Label => "공황";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist) =>
        c.Mind.Panicking(w.Tick) && !c.Down && !c.Outside ? (5f, c.Mind.Frozen ? "공황 — 얼어붙었다" : "공황 — 달아난다") : (0f, "—");

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        long until = c.Mind.PanicUntil;
        int left = (int)Math.Max(SimTime.Minutes(0.5f), until - w.Tick);
        var toils = new List<Toil>();
        if (!c.Mind.Frozen)
        {
            // 위험에서 먼, 안전한 방의 아무 칸으로 (가장 가까운 곳이 아니라 — 정신없이)
            var far = w.Ship.Rooms.Where(r => !r.Detached && r != c.Room && Atmosphere.Danger(r) <= 0.1f && !r.Leaking && w.Fire.CountIn(r) == 0)
                .SelectMany(r => r.Cells).Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c))
                .OrderByDescending(x => dist.Get(x)).Take(12).ToList();
            if (far.Count > 0) toils.Add(new GotoToil(far[(int)(w.Tick / 7 % far.Count)]));
        }
        toils.Add(new WaitToil(left, c.Mind.Frozen ? Pose.Standing : Pose.Sitting, null) { DoneWhen = (cm, world) => !cm.Mind.Panicking(world.Tick) });
        return new Job(this, c.Mind.Frozen ? "얼어붙음" : "달아남", toils)
        {
            LogText = c.Mind.Frozen ? "공황에 얼어붙었다" : "공황에 달아난다",
            InterruptMargin = 3f,
        };
    }
}
