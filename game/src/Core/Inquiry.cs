using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.7 자기 실수를 숨긴다 · 블랙박스 · 사고 조사.
//  실수: 설비를 손본 뒤 — 공구를 안에 두고 나옴 · 밸브를 반대로 돌림 · 점검표 한 줄 건너뜀 · 규격이 다른 부품 · 경고등을 확인 없이 끔.
//   대개는 스스로 알아채고 되돌린다(꼼꼼할수록 · 재발 방지 규칙이 있으면). 못 알아채면 몇 시간 뒤 사고가 된다 (합선 불 · 과열 · 멎음 · 튄 조각).
//   사고의 첫 고리에는 "본 것"만 적힌다 (누구 탓인지는 아무도 모른다 — 본인만 안다).
//  털어놓나 숨기나: 가치관 · 벌이 무서운 정도(방침 · 함장) · 실패가 두려움 · 다친 사람이 있나 · 함장과의 사이 · 화 · 사기.
//   숨기는 방법: 흔적 치우기(사고 자리에 혼자 가서 렌치 · 깨진 부품을 치운다) · 거짓말 · 남 탓(같은 설비를 만진 사람 · 미운 사람) ·
//   블랙박스 지우기(권한 · 솜씨 — 지운 자리는 빈 구간으로 남고, 지운 시각 · 권한 · 단말이 단서).
//  죄책감: 잠을 설치고 그 설비 꿈을 꾼다(v17.5 꿈거리) · 일기 · 말수가 준다(v17.8 남의 눈에 띔) · 날이 갈수록 무거워져 스스로 털어놓기도.
//  누가 아나: 그 자리에서 본 사람 · 치우는 걸 본 사람 · 단말 앞에 있던 걸 본 사람 · 주 컴퓨터(블랙박스를 읽는다).
//  주 컴퓨터: 사고 몇 시간 뒤 마지막 정비 기록을 짚어 당사자에게 먼저 말하라고 권한다(사생활 방침이면 본인에게만) · 빈 구간을 찾아내 알린다 ·
//   재발 방지 규칙이 생기면 그 일이 끝날 때마다 짚어 준다.
//  사고 조사: 큰 사고(사망 · 배 전체 · 계통 · 실수에서 번진 불 · 다친 사람) 뒤 안건이 저절로 올라온다 → InquiryHearing.cs.

public enum SlipKind : byte { ToolLeft, WrongValve, SkippedStep, WrongPart, SilencedAlarm }
public enum CoverWay : byte { None, Confess, Tidy, Lie, Blame, Wipe }
public enum CoverTaskKind : byte { None, Tidy, Wipe, Confess }

public sealed record SlipSpec(SlipKind Kind, string What, string Symptom, string Trace, string Rule, FaultKind Fault, bool Tidy, string Dream, string Remind);

public sealed class Slip
{
    public int Id { get; init; }
    public SlipKind Kind { get; init; }
    public int Who { get; init; }
    public int Machine { get; init; }
    public int RoomId { get; init; }
    public long Tick { get; init; }
    public long Due { get; set; }
    public string Why { get; init; } = "";
    public CoverWay? Forced { get; init; }
    public List<int> Saw { get; } = new();
    public bool Fixed { get; set; }
    public bool Bit { get; set; }
    public long BitAt { get; set; } = -1;
    public int Node { get; set; } = -1;
    public bool Hidden { get; set; }
    public List<CoverWay> Ways { get; } = new();
    public int Scapegoat { get; set; } = -1;
    public bool TraceLeft { get; set; } = true;
    public int TidySeenBy { get; set; } = -1;
    public int Wipe { get; set; } = -1;
    public int WipeSeenBy { get; set; } = -1;
    public float Guilt { get; set; }
    public bool Confessed { get; set; }
    public long ConfessedAt { get; set; } = -1;
    public bool Nudged { get; set; }
    public bool Fire { get; set; }
    public bool Revealed { get; set; }
    public long Settled { get; set; } = -1;
    public long LastDream { get; set; } = -1;
    public string MachineName { get; init; } = "";
    public bool Open => Bit && !Confessed && !Revealed && Settled < 0;
}

public sealed class InquiryCase
{
    public int Id { get; init; }
    public int Root { get; init; }
    public string Title { get; init; } = "";
    public long Start { get; init; }
    public long Opened { get; init; }
    public List<int> Rooms { get; } = new();
    public int Slip { get; set; } = -1;
    public int Motion { get; set; } = -1;
    public bool Heard { get; set; }
    public Finding? Finding { get; set; }
}

public sealed class Finding
{
    public int Case { get; init; }
    public int Root { get; init; }
    public long Tick { get; init; }
    public int Blamed { get; set; } = -1;
    /// <summary>결론이 세계의 진실과 맞나 (사람들은 모른다).</summary>
    public bool Right { get; set; } = true;
    public bool BoxRead { get; set; }
    public bool GapSeen { get; set; }
    public bool Hid { get; set; }
    public bool Framed { get; set; }
    public string Text { get; set; } = "";
    public string Rule { get; set; } = "";
    public Penalty? Verdict { get; set; }
    public List<string> Basis { get; } = new();
}

public readonly record struct CoverTask(CoverTaskKind Kind, int Slip, int RoomId, Cell At, int Other, long Since);

public sealed class InquiryStats
{
    public int Slips, SelfFixed, Bites, Fires, Hidden, Confessed, LateConfessed, Tidied, Lies, Blames, Wipes, Nudges, GapsSeen,
        Cases, Hearings, Revealed, Wrong, Unresolved, Forgiven, Punished, Rules, Reminders, Dreams, Witnessed;
}

public sealed partial class InquirySystem
{
    private readonly World _w;
    public static bool Off { get; set; }
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 431));
    public InquirySystem(World w) => _w = w;

    public InquiryStats Stats { get; } = new();
    public List<Slip> Slips { get; } = new();
    public List<InquiryCase> Cases { get; } = new();
    /// <summary>재발 방지 규칙 (조사가 정한 것 · 언제부터).</summary>
    public SortedDictionary<SlipKind, long> Rules { get; } = new();
    private readonly SortedDictionary<int, CoverTask> _tasks = new();
    private readonly HashSet<int> _seenRoots = new();
    private long _next, _nextHour, _nextCase;
    private int _slipId, _caseId;
    public static long UpdateTicks;

    public static readonly SlipSpec[] Specs =
    {
        new(SlipKind.ToolLeft, "정비하고 렌치를 설비 안에 두고 나왔다", "{0} 안에서 불꽃이 튀었다", "타 버린 렌치 — 손잡이에 {1} 이름이 새겨져 있다",
            "정비 뒤에는 공구 수를 센다", FaultKind.ShortCircuit, true, "{0} 뚜껑을 닫는데 안에서 쇳소리가 난다", "공구 수를 세고 나오세요"),
        new(SlipKind.WrongValve, "냉각 밸브를 반대로 돌려 놓았다", "{0}이 과열돼 멎었다", "밸브 손잡이가 반대로 돌아가 있다",
            "밸브는 돌리기 전에 짚어 소리 내 말한다", FaultKind.CompressorFail, true, "밸브를 돌리는데 손잡이가 끝없이 돈다", "밸브 방향을 한 번 더 보세요"),
        new(SlipKind.SkippedStep, "점검표 한 줄을 건너뛰고 끝났다고 적었다", "{0}이 갑자기 멎었다", "점검표에 서명은 있는데 필터는 그대로다",
            "정비 끝 서명은 두 사람이 한다", FaultKind.FilterClogged, false, "점검표 칸이 끝없이 늘어난다", "점검표를 함께 확인할 사람을 부르세요"),
        new(SlipKind.WrongPart, "규격이 다른 부품을 끼웠다", "{0} 부품이 깨져 튀었다", "깨진 부품 — 규격 표시가 다르다",
            "부품 꼬리표는 둘이 확인한다", FaultKind.BearingWear, true, "끼운 부품이 손안에서 자꾸 커진다", "부품 꼬리표를 둘이 확인하세요"),
        new(SlipKind.SilencedAlarm, "경고등을 확인하지 않고 꺼 버렸다", "{0} 경고등이 꺼진 채 멎었다", "",
            "경보는 끄기 전에 까닭을 적는다", FaultKind.ControlFault, false, "꺼 버린 경고등이 어둠 속에서 혼자 깜박인다", "경보를 끄기 전에 까닭을 적어 주세요"),
    };
    public static SlipSpec Spec(SlipKind k) => Specs[(int)k];

    private CrewMember? P(int id) => id >= 0 && id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);
    private static bool Adult(CrewMember c) => !c.Dead && !c.IsChild && c.Profiled;
    private Machine? MachineOf(int fid) => _w.Ship.Furniture.FirstOrDefault(f => f.Id == fid)?.Machine;
    public Slip? Get(int id) => id >= 0 && id < Slips.Count && Slips[id].Id == id ? Slips[id] : Slips.FirstOrDefault(s => s.Id == id);
    public CoverTask? TaskOf(CrewMember c) => _tasks.TryGetValue(c.Id, out var t) ? t : null;
    public bool Ruled(SlipKind k) => Rules.ContainsKey(k);

    // ───────────────────────────── 일을 마친 뒤 ─────────────────────────────

    /// <summary>설비 일을 마쳤다 (CrewLife.AfterWork 훅): 블랙박스에 단말 기록 · 드물게 숨은 실수가 남는다.</summary>
    public void Worked(CrewMember c, WorkOrder o)
    {
        var w = _w;
        if (Off) return;
        var m = o.Target.Furniture?.Machine;
        if (m == null || o.Kind is not (WorkKind.Repair or WorkKind.Maintain or WorkKind.PreventiveCheck or WorkKind.Upgrade)) return;
        bool maint = o.Kind is WorkKind.Maintain or WorkKind.PreventiveCheck;
        var (p, why) = w.Life.MistakeOdds(c, o);
        p *= 0.3f;
        // 고를 실수 (정비면 건너뜀 · 경보 끔 · 공구 · 수리면 부품 · 공구 · 밸브)
        float r = R.Float();
        var kind = maint ? (r < 0.4f ? SlipKind.SkippedStep : r < 0.65f ? SlipKind.SilencedAlarm : SlipKind.ToolLeft)
                         : (r < 0.4f ? SlipKind.WrongPart : r < 0.75f ? SlipKind.ToolLeft : SlipKind.WrongValve);
        if (Ruled(kind)) p *= 0.35f;
        bool slip = R.Chance(p);
        w.Blackbox.Note(BoxKind.Work, m.Body.Room, c, m.Body.Id, (byte)(slip && kind == SlipKind.SkippedStep ? 1 : 0));
        if (Ruled(kind) && w.Automation.Present && w.Automation.MainOnline)
        {
            // 재발 방지 규칙: 주 컴퓨터가 그 일이 끝날 때마다 짚어 준다 → 실수를 스스로 알아채기 쉬워진다
            var sp0 = Spec(kind);
            if (w.Automation.Book.Add(ActKind.Advice, m.Body.Room, $"{m.Name} 정비 마침 — {c.Name} 단말", $"판단: 조사에서 정한 규칙 — {sp0.Rule}", "조치: 확인 칸을 띄움",
                $"요청: {sp0.Remind}", $"rule:{kind}:{m.Body.Id}", SimTime.Hours(6)) != null) Stats.Reminders++;
        }
        if (!slip) return;
        if (kind == SlipKind.SilencedAlarm) w.Blackbox.Note(BoxKind.Silence, m.Body.Room, c, m.Body.Id);
        if (kind == SlipKind.WrongValve) w.Blackbox.Note(BoxKind.Valve, m.Body.Room, c, m.Body.Id, 1);
        Make(c, m, kind, why);
    }

    /// <summary>실수 하나 (시험에서도 쓴다).</summary>
    public Slip Make(CrewMember c, Machine m, SlipKind kind, string why, float dueHours = -1f, CoverWay? force = null)
    {
        var w = _w;
        var s = new Slip
        {
            Id = _slipId++, Kind = kind, Who = c.Id, Machine = m.Body.Id, RoomId = m.Body.Room.Id, Tick = w.Tick, Why = why, MachineName = m.Name, Forced = force,
            Due = w.Tick + (dueHours >= 0f ? SimTime.Hours(dueHours) : SimTime.Hours(R.Range(2f, 9f))),
        };
        foreach (var o in w.Crew) if (o != c && !o.Dead && o.IsAwake && o.Room == m.Body.Room) s.Saw.Add(o.Id);
        Slips.Add(s);
        Stats.Slips++;
        if (dueHours < 0f)
        {
            // 대개는 스스로 알아챈다 (꼼꼼할수록 · 규칙이 있으면 · 컴퓨터가 짚어 주면)
            float notice = 0.2f + 0.45f * c.Traits.Diligence + (Ruled(kind) ? 0.25f : 0f) - (c.Needs.Rest < 0.25f ? 0.15f : 0f);
            if (R.Chance(Math.Clamp(notice, 0.05f, 0.9f)))
            {
                s.Fixed = true;
                Stats.SelfFixed++;
                string back = kind switch
                {
                    SlipKind.ToolLeft => "렌치가 하나 모자란 게 생각나 다시 열어 꺼냈다",
                    SlipKind.WrongValve => "밸브 방향이 마음에 걸려 다시 가서 바로 돌렸다",
                    SlipKind.SkippedStep => "건너뛴 점검표 한 줄이 생각나 다시 가서 마저 했다",
                    SlipKind.WrongPart => "끼운 부품 꼬리표가 다른 걸 보고 다시 갈았다",
                    _ => "꺼 버린 경고등이 마음에 걸려 다시 가서 까닭을 봤다",
                };
                w.Log.Add(w.Tick, LogKind.Work, $"{m.Name} — {back}", c.Id);
                return s;
            }
        }
        return s;
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _next) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _next = w.Tick + SimTime.Minutes(1);
        for (int i = 0; i < Slips.Count; i++)
        {
            var s = Slips[i];
            if (!s.Fixed && !s.Bit && w.Tick >= s.Due) Bite(s);
        }
        if (_tasks.Count > 0) Expire();
        if (w.Tick >= _nextHour)
        {
            _nextHour = w.Tick + SimTime.Hours(1);
            Conscience();
            ComputerReads();
            Cases_();
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>숨은 실수가 사고가 된다. 첫 고리에는 본 것만 적힌다.</summary>
    private void Bite(Slip s)
    {
        var w = _w;
        var m = MachineOf(s.Machine);
        if (m == null || m.Body.Room.Detached || w.Tick - s.Tick > SimTime.TicksPerDay * 2) { s.Fixed = true; return; }
        if (m.Faults.Count > 0) { s.Due = w.Tick + SimTime.Hours(2); return; }
        var sp = Spec(s.Kind);
        s.Bit = true;
        s.BitAt = w.Tick;
        Stats.Bites++;
        var room = m.Body.Room;
        int node = w.Causes.Root(CauseKind.Fault, string.Format(sp.Symptom, m.Name), room, m.Body.Center, observer: false);
        s.Node = node;
        using (w.Causes.Because(node))
        {
            w.Machines.Break(m, sp.Fault);
            if (s.Kind == SlipKind.ToolLeft && m.Body.Cells.Count > 0 && R.Chance(0.6f) && w.Fire.Ignite(m.Body.Cells[0], 0.5f)) { s.Fire = true; Stats.Fires++; }
            if (s.Kind == SlipKind.WrongPart)
                foreach (var o in w.Crew)
                    if (!o.Dead && o.Room == room && (o.Position - m.Body.Center).LengthSquared() < 9f) NeedsSystem.AddInjury(o.Vitals, 0.08f, $"{m.Name}에서 튄 부품 조각");
            if (s.Kind == SlipKind.SilencedAlarm) m.Condition = MathF.Max(0.1f, m.Condition - 0.25f);
        }
        Realize(s);
    }

    /// <summary>"내 탓이다" — 털어놓나 숨기나 (숨기면 어떻게).</summary>
    private void Realize(Slip s)
    {
        var w = _w;
        var c = P(s.Who);
        if (c == null || c.Dead) { s.Settled = w.Tick; return; }
        var sp = Spec(s.Kind);
        var inc = w.Causes.IncidentOf(s.Node);
        bool hurt = inc != null && inc.Casualties > 0 || s.Kind == SlipKind.WrongPart || s.Fire;
        s.Guilt = 0.2f + 0.3f * c.Traits.Diligence + (hurt ? 0.15f : 0f) + (c.Value is CrewValue.Rules or CrewValue.People ? 0.1f : 0f);
        if (s.Forced == CoverWay.Confess || s.Forced == null && R.Chance(1f - HideOdds(c, hurt))) { Confess(s, c, early: true); return; }
        Hide(s, c, s.Forced);
    }

    /// <summary>숨길 확률: 가치관 · 벌이 무서운 정도 · 실패가 두려움 · 화 · 사기 · 함장과의 사이 · 다친 사람.</summary>
    public float HideOdds(CrewMember c, bool hurt)
    {
        var w = _w;
        float p = 0.4f;
        p *= c.Value switch { CrewValue.Rules => 0.4f, CrewValue.Safety => 0.6f, CrewValue.People => 0.9f, CrewValue.Freedom => 1.3f, _ => 1.1f };
        p *= w.Policies["violations"] switch { 1 => 1.5f, 2 => 0.6f, _ => 1f };
        if (w.Command.Style == CaptainStyle.Authoritarian) p *= 1.4f;
        if (w.Command.Captain is CrewMember cap && cap != c) p *= cap.AffinityTo(c) > 0.35f ? 0.7f : cap.AffinityTo(c) < -0.2f ? 1.3f : 1f;
        p *= 1f + c.Mind.Anger;
        p *= 1.4f - 0.8f * w.Society.Morale;
        p *= 1f - 0.4f * c.Traits.Diligence;
        if (c.Fears.Contains(Fear.Failure)) p *= 1.5f;
        if (hurt) p *= 1.25f;
        return Math.Clamp(p, 0.05f, 0.9f);
    }

    /// <summary>숨긴다: 거짓말은 기본 · 흔적 치우기 · 남 탓 · 블랙박스 지우기.</summary>
    public void Hide(Slip s, CrewMember c, CoverWay? force = null)
    {
        var w = _w;
        var sp = Spec(s.Kind);
        s.Hidden = true;
        Stats.Hidden++;
        s.Ways.Add(CoverWay.Lie);
        Stats.Lies++;
        Life.Diary(w, c, $"{sp.What}. 아무에게도 말하지 않았다.");
        var room = w.Ship.Rooms[s.RoomId];
        if ((force == null || force == CoverWay.Tidy) && sp.Tidy && s.TraceLeft && (force != null || R.Chance(0.55f + 0.2f * (1f - c.Traits.Diligence))))
        {
            var spot = MachineOf(s.Machine) is Machine mm && mm.Body.UseSpots.Count > 0 ? mm.Body.UseSpots[0] : room.Cells[0];
            _tasks[c.Id] = new CoverTask(CoverTaskKind.Tidy, s.Id, s.RoomId, spot, -1, w.Tick);
        }
        else if ((force == null || force == CoverWay.Wipe) && (force != null || w.Blackbox.CanWipe(c) && R.Chance(0.3f + 0.4f * c.SkillLevel(Skill.Electrical) - (c.Value == CrewValue.Rules ? 0.25f : 0f)))
                 && w.Blackbox.Terminal() is Room term)
        {
            var spot = term.Cells.Where(x => w.Ship.IsWalkable(x)).OrderBy(x => x.Y).ThenBy(x => x.X).DefaultIfEmpty(term.Cells[0]).First();
            _tasks[c.Id] = new CoverTask(CoverTaskKind.Wipe, s.Id, term.Id, spot, -1, w.Tick);
        }
        // 남 탓: 같은 설비를 만진 사람 · 그 자리에 있던 사람 · 미운 사람
        if (force == null || force == CoverWay.Blame)
        {
            var goat = Scapegoat(s, c);
            float blame = 0.12f + 0.35f * c.Mind.Anger + (c.Value is CrewValue.Freedom or CrewValue.Efficiency ? 0.15f : 0f) - 0.3f * c.Traits.Diligence
                          + (goat != null && c.AffinityTo(goat) < -0.2f ? 0.2f : 0f);
            if (goat != null && (force == CoverWay.Blame || R.Chance(Math.Clamp(blame, 0f, 0.7f)))) Blame(s, c, goat);
        }
    }

    private CrewMember? Scapegoat(Slip s, CrewMember c)
    {
        var w = _w;
        var cands = new List<(CrewMember who, float v)>();
        var box = w.Blackbox.Read(s.Tick - SimTime.Hours(36), s.Tick - 1, e => e.Kind == BoxKind.Work && e.Ref == s.Machine && e.Who != c.Id);
        foreach (var o in w.Crew)
        {
            if (o == c || !Adult(o)) continue;
            float v = -c.AffinityTo(o);
            if (box != null && box.Any(e => e.Who == o.Id)) v += 0.8f;
            if (s.Saw.Contains(o.Id)) v += 0.3f;
            if (o.Role == c.Role) v += 0.2f;
            if (v > 0.25f) cands.Add((o, v));
        }
        return cands.OrderByDescending(x => x.v).ThenBy(x => x.who.Id).Select(x => x.who).FirstOrDefault();
    }

    private void Blame(Slip s, CrewMember c, CrewMember goat)
    {
        var w = _w;
        s.Scapegoat = goat.Id;
        s.Ways.Add(CoverWay.Blame);
        Stats.Blames++;
        // 말이 돈다: 그 방에서 들은 사람은 그 사람을 조금 다르게 본다 · 당한 사람은 억울하다
        foreach (var o in w.Crew)
            if (o != c && o != goat && !o.Dead && o.IsAwake && o.Room == c.Room) o.ChangeAffinity(goat, -0.03f);
        goat.Needs.Stress = MathF.Min(1f, goat.Needs.Stress + 0.05f);
        w.Log.Add(w.Tick, LogKind.Life, $"\"{s.MachineName} 마지막으로 만진 건 {goat.Name}일 거다\" — {Ko.IGa(c.Name)} 말했다", c.Id);
        Life.Diary(w, goat, $"{s.MachineName} 일이 내 탓이라는 말이 돈다. 나는 아니다.");
        Life.Diary(w, c, $"{goat.Name} 이름을 댔다. 입이 먼저 나갔다.");
    }

    /// <summary>털어놓는다 (바로 · 나중에 죄책감에 · 조사 자리에서).</summary>
    public void Confess(Slip s, CrewMember c, bool early, CrewMember? to = null)
    {
        var w = _w;
        var sp = Spec(s.Kind);
        if (s.Confessed) return;
        s.Confessed = true;
        s.ConfessedAt = w.Tick;
        _tasks.Remove(c.Id);
        if (early && !s.Hidden) Stats.Confessed++; else Stats.LateConfessed++;
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
        string toWhom = to != null ? $"{to.Name}에게 " : "";
        w.Log.Add(w.Tick, LogKind.Work, $"{toWhom}실수를 털어놓았다 — {s.MachineName}: {sp.What}", c.Id);
        Life.Diary(w, c, s.Hidden ? $"며칠을 끌어안고 있다가 {toWhom}말했다. {sp.What}. 오히려 숨이 쉬어진다." : $"{sp.What}. 바로 말했다.");
        foreach (var o in w.Crew)
        {
            if (o == c || !Adult(o)) continue;
            o.ChangeAffinity(c, s.Hidden ? -0.015f : -0.005f);
            if (!s.Hidden && (o.Value is CrewValue.Rules or CrewValue.People || o == to) && (o.Room == c.Room || o == to)) w.Relations.Remember(o, c, RelationReason.OwnedUp, "제 실수를 먼저 털어놓았다");
        }
        if (to != null) w.Relations.Remember(to, c, RelationReason.OwnedUp, s.Hidden ? "숨겼던 실수를 나에게 털어놓았다" : "제 실수를 먼저 털어놓았다");
        w.Info.CredAdd(c, s.Hidden ? -0.02f : 0.03f);
        // 털어놓은 사람의 말을 들은 남 탓 대상은 풀린다
        if (P(s.Scapegoat) is CrewMember goat)
        {
            goat.Needs.Stress = MathF.Max(0f, goat.Needs.Stress - 0.04f);
            w.Relations.Remember(goat, c, RelationReason.BlamedMe, "제 실수를 나에게 돌렸다가 털어놓았다");
        }
        w.History.Add(w, HistoryKind.Memory, $"{Ko.IGa(c.Name)} {s.MachineName} 일을 털어놓았다 — {sp.What}" + (s.Hidden ? " (한동안 숨겼다)" : ""), w.Ship.Rooms[s.RoomId], new[] { c });
        w.Society.Punish(c, $"실수를 털어놓았다 ({sp.What})", light: true);
    }

    // ───────────────────────────── 죄책감 ─────────────────────────────

    /// <summary>한 시간마다: 숨긴 사람은 잠 · 일기 · 말수에서 티가 나고, 무거워지면 스스로 털어놓는다.</summary>
    private void Conscience()
    {
        var w = _w;
        foreach (var s in Slips)
        {
            if (!s.Open || !s.Hidden) continue;
            var c = P(s.Who);
            if (c == null || c.Dead) { s.Settled = w.Tick; continue; }
            var sp = Spec(s.Kind);
            var inc = w.Causes.IncidentOf(s.Node);
            bool hurt = inc != null && (inc.Casualties > 0 || inc.Deaths > 0);
            bool framed = s.Scapegoat >= 0 && Cases.Any(k => k.Slip == s.Id && k.Finding?.Blamed == s.Scapegoat);
            s.Guilt = MathF.Min(1.5f, s.Guilt + 0.012f + 0.02f * c.Traits.Diligence + (c.Value is CrewValue.Rules or CrewValue.People ? 0.01f : 0f) + (hurt ? 0.015f : 0f) + (framed ? 0.04f : 0f));
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.006f * s.Guilt);
            // 꿈거리 (밤마다 한 번) · 말수가 준다 · 일기
            if (w.Tick - s.LastDream > SimTime.TicksPerDay)
            {
                s.LastDream = w.Tick;
                w.After.Impress(c, s.Kind == SlipKind.ToolLeft ? DreamKind.Fire : DreamKind.Alarm, 0.45f + 0.4f * MathF.Min(1f, s.Guilt),
                    string.Format(sp.Dream, s.MachineName), -1, s.RoomId, $"slip:{s.Id}");
                Stats.Dreams++;
                w.React.MarkOdd(c, "말수가 줄고 눈을 피한다", 30f);
                Life.Diary(w, c, framed ? $"{P(s.Scapegoat)?.Name ?? "그 사람"}이 내 대신 벌을 받았다. 밥이 넘어가지 않는다." : $"{s.MachineName} 앞을 지날 때마다 숨이 막힌다.");
            }
            float conf = s.Guilt * 0.05f * (s.Nudged ? 2f : 1f) * (c.Value == CrewValue.Rules ? 1.5f : 1f) * (framed ? 2f : 1f);
            if (!_tasks.ContainsKey(c.Id) && c.CanAct && R.Chance(MathF.Min(0.5f, conf)))
            {
                var to = w.Command.Captain is CrewMember cap && cap != c ? cap
                    : w.Crew.Where(o => o != c && Adult(o) && o.CanAct).OrderByDescending(o => c.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
                if (to != null) _tasks[c.Id] = new CoverTask(CoverTaskKind.Confess, s.Id, -1, default, to.Id, w.Tick);
                else Confess(s, c, early: false);
            }
        }
    }

    // ───────────────────────────── 주 컴퓨터 ─────────────────────────────

    /// <summary>주 컴퓨터가 블랙박스를 읽는다: 사고 몇 시간 뒤 마지막 정비 기록을 짚고(먼저 말하라고) · 빈 구간을 찾아낸다.</summary>
    private void ComputerReads()
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline || !w.Blackbox.Present || w.Blackbox.Wrecked) return;
        int privacy = w.Policies["privacy"];
        foreach (var s in Slips)
        {
            if (!s.Open || s.Nudged || w.Tick - s.BitAt < SimTime.Hours(1)) continue;
            var c = P(s.Who);
            if (c == null || c.Dead) continue;
            var rec = w.Blackbox.Read(s.Tick - SimTime.Minutes(5), s.Tick + SimTime.Minutes(5), e => e.Ref == s.Machine && e.Who == c.Id && e.Kind is BoxKind.Work or BoxKind.Silence or BoxKind.Valve);
            if (rec == null || rec.Count == 0) continue;
            s.Nudged = true;
            Stats.Nudges++;
            var room = w.Ship.Rooms[s.RoomId];
            if (privacy == 1)
                w.Log.Add(w.Tick, LogKind.Life, $"주 컴퓨터가 {c.Name}에게만 말했다 — \"{s.MachineName} 마지막 정비 기록이 {c.Name} 단말입니다. 조사 전에 먼저 말씀하시면 좋겠습니다\"", c.Id);
            else
                a.Book.Add(ActKind.Advice, room, $"블랙박스: {s.MachineName} 마지막 정비 {SimTime.Clock(rec[^1].Tick)} — {c.Name} 단말 · {SimTime.Clock(s.BitAt)} 경보",
                    "판단: 정비 몇 시간 만에 멎었다 — 그때 무슨 일이 있었는지는 손본 사람이 가장 잘 안다", "조치: 그 앞뒤 기록을 따로 떼어 보관함",
                    $"요청: {c.Name}님, 조사 전에 그날 일을 먼저 말씀해 주시면 좋겠습니다", $"slip:nudge:{s.Id}", SimTime.TicksPerDay);
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f);
            s.Guilt += 0.15f;
        }
        foreach (var g in w.Blackbox.Wipes)
        {
            if (g.Noticed >= 0 || g.At < w.Blackbox.FreshSince) continue;
            g.Noticed = w.Tick;
            Stats.GapsSeen++;
            w.Blackbox.Stats.GapsSeen++;
            var near = w.Blackbox.WhoWasIn(g.Terminal, g.At);
            a.Book.Add(ActKind.Advice, w.Blackbox.Room, $"블랙박스: {w.Blackbox.GapLine(g)} · 지운 줄 {g.Removed}",
                "판단: 고장이 아니라 단말에서 손으로 지웠다 — 봉인 기록은 지울 수 없다",
                $"조치: 그 시각 {w.Blackbox.RoomName(g.Terminal)} 위치 기록을 따로 보관함 ({near.Count}명)", "요청: 지운 분은 조사 전에 말씀해 주세요", $"box:gap:{g.Id}", SimTime.TicksPerDay);
            if (Get(g.Slip) is Slip s && P(s.Who) is CrewMember c) { c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.06f); s.Guilt += 0.1f; }
        }
    }

    // ───────────────────────────── 숨기는 몸짓 (CoverActivity) ─────────────────────────────

    private void Expire()
    {
        var w = _w;
        foreach (var id in _tasks.Keys.ToList())
        {
            var t = _tasks[id];
            var s = Get(t.Slip);
            if (s == null || s.Confessed || s.Revealed || w.Tick - t.Since > SimTime.TicksPerDay || P(id) is not CrewMember c || c.Dead) _tasks.Remove(id);
        }
    }

    /// <summary>흔적을 치웠다 (사고 자리에 혼자 · 남이 보면 그 사람이 기억한다).</summary>
    public void DidTidy(CrewMember c)
    {
        var w = _w;
        if (!_tasks.TryGetValue(c.Id, out var t) || Get(t.Slip) is not Slip s) return;
        _tasks.Remove(c.Id);
        s.TraceLeft = false;
        s.Ways.Add(CoverWay.Tidy);
        Stats.Tidied++;
        var seen = w.Crew.Where(o => o != c && !o.Dead && o.IsAwake && o.Room == c.Room).OrderBy(o => o.Id).FirstOrDefault();
        if (seen != null)
        {
            s.TidySeenBy = seen.Id;
            Stats.Witnessed++;
            w.React.MarkOdd(c, $"{s.MachineName} 안을 혼자 뒤진다", 10f);
            Life.Diary(w, seen, $"사고가 난 {s.MachineName} 안을 {Ko.IGa(c.Name)} 혼자 뒤지고 있었다. 뭘 찾았을까.");
        }
        Life.Diary(w, c, s.Kind switch { SlipKind.ToolLeft => "타 버린 렌치를 꺼내 공구함 맨 밑에 넣었다.", SlipKind.WrongPart => "깨진 부품 조각을 주워 재활용 통에 넣었다.", _ => "밸브를 몰래 바로 돌려 놓았다." });
    }

    /// <summary>블랙박스 구간을 지웠다 (단말 앞 · 남이 보면 기억한다).</summary>
    public void DidWipe(CrewMember c)
    {
        var w = _w;
        if (!_tasks.TryGetValue(c.Id, out var t) || Get(t.Slip) is not Slip s || c.Room is not Room term) return;
        _tasks.Remove(c.Id);
        bool tidy = c.SkillLevel(Skill.Electrical) >= 0.7f;
        var g = w.Blackbox.Wipe(c, s.Tick - SimTime.Minutes(40), s.Tick + SimTime.Minutes(10), s.RoomId, term, s.Id, tidy);
        if (g == null) return;
        s.Wipe = g.Id;
        s.Ways.Add(CoverWay.Wipe);
        Stats.Wipes++;
        var seen = w.Crew.Where(o => o != c && !o.Dead && o.IsAwake && o.Room == term).OrderBy(o => o.Id).FirstOrDefault();
        if (seen != null) { s.WipeSeenBy = seen.Id; Stats.Witnessed++; Life.Diary(w, seen, $"{Ko.IGa(c.Name)} {term.Name} 단말 앞에 오래 서 있었다. 화면을 내가 보자 꺼 버렸다."); }
        Life.Diary(w, c, "단말에서 그 시각 기록을 지웠다. 손이 떨렸다.");
    }

    public void DidConfess(CrewMember c)
    {
        if (!_tasks.TryGetValue(c.Id, out var t) || Get(t.Slip) is not Slip s) return;
        Confess(s, c, early: false, P(t.Other));
    }

    /// <summary>시험 · 장면: 할 일을 바로 마친다.</summary>
    public void Finish(CrewMember c)
    {
        if (TaskOf(c) is not CoverTask t) return;
        if (t.Kind == CoverTaskKind.Tidy) DidTidy(c);
        else if (t.Kind == CoverTaskKind.Wipe) DidWipe(c);
        else if (t.Kind == CoverTaskKind.Confess) DidConfess(c);
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Slips.Count); I(Cases.Count); I(Rules.Count); I(_tasks.Count);
        I(Stats.Bites); I(Stats.Hidden); I(Stats.Confessed); I(Stats.Revealed); I(Stats.Wipes); I(Stats.Blames);
        foreach (var s in Slips) { I(s.Bit ? 1 : 0); I(s.Confessed ? 1 : 0); F(s.Guilt); }
    }
}
