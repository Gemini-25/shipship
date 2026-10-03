using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.2 몸짓: 손 · 시선 · 자세가 그 순간의 사정을 드러낸다 (이름표 없이도 누가 무엇을 하는지 보인다).
//  · 양손 짐을 들고 손이 필요한 문(카드 · 지문 · 고장 난 감지기 · 정전) 앞: 곁에 사람이 있으면 "문 좀 잡아 줄래?", 없으면 짐을 내려놓고 열었다가 다시 든다.
//  · 뜨거운 잔을 들고 가다 갑자기 멈추면(누가 튀어나옴 · 비켜서기 · 문 앞) 잔을 내민 채 버틴다 — 흘리면 바닥이 젖고 "앗, 뜨거!".
//  · 캄캄한 방에서 일하는데 작업등이 멀면 등을 일하는 자리 곁으로 옮긴다.
//  · 누가 말을 걸면 고개부터 돌리고, 손에 든 걸 정리한 뒤에 대답한다.
//  · 쓰러진 사람 곁을 지나면 무릎을 꿇고 숨을 살피고, 소리쳐 사람을 부른다 (들은 사람은 안다).
//  · 신입은 계기와 선배 얼굴을 번갈아 본다 — 선배가 끄덕여 주면 손이 덜 떨린다.
//  · 잘 쓰는 팔을 다쳤으면 공구를 다른 손으로 바꿔 쥔다.
//  · 전에 고친 부위가 또 나가면 그 부위부터 손이 간다 (수리가 빠르다).
// 버릇 · 흥얼거림 · 끊긴 대화는 GestureTics.cs. 그림은 View/ShipViewGestures.

public enum Mien : byte
{
    None, SetDown, AskDoor, HoldDoor, CupSteady, CupSpill, LampCarry, HeadTurn, WrapUp, KneelBy,
    GlanceGauge, GlanceSenior, Nod, SwapGrip, KnownSpot, Resume, Hum, SingAlong, Notice, CallHelp,
}

/// <summary>버릇 (한 사람당 둘 · 스트레스가 쌓이면 하나가 바뀐다).</summary>
public enum Tic : byte { PenSpin, LegBounce, NailBite, Whistle, HairTouch, KnuckleCrack, Hum, TapFingers, RubNeck, CollarTug, LipBite, Stretch }

public sealed class MannerState
{
    public int Id { get; init; }
    public Mien M { get; internal set; }
    public long MSince { get; internal set; } = -1;
    public long MUntil { get; internal set; } = -1;
    /// <summary>시선 (칸 좌표) · 쳐다보는 사람.</summary>
    public Vector2? LookAt { get; internal set; }
    public int LookCrew { get; internal set; } = -1;
    /// <summary>손이 가는 곳 · 내려놓은 짐 · 옮기기 전 등 자리.</summary>
    public Vector2? Spot { get; internal set; }
    /// <summary>이때까지 걸음을 멈춘다 (짐을 내려놓음 · 잔을 바로잡음 · 무릎).</summary>
    public long HoldStep { get; internal set; } = -1;
    public bool LeftHanded { get; init; }
    public Tic[] Tics { get; } = new Tic[2];
    public Tic? StressTic { get; internal set; }
    public long StressSince { get; internal set; } = -1;
    public Tic Doing { get; internal set; }
    public long TicUntil { get; internal set; } = -1;
    public int Tune { get; init; }
    public long WrapFrom { get; internal set; } = -1;
    public long WrapUntil { get; internal set; } = -1;
    public int GlanceAt { get; internal set; } = -1;
    public long GlanceUntil { get; internal set; } = -1;
    internal long NextTic, HighSince = -1, LowSince = -1, CupNext, CupCheck, LastStepAt = -10, NextLine, AddrAt = -1, KnownUntil = -1, TicAdvised = -1_000_000;
    internal int DoorDone = -1, KnownMachine = -1, Glances;
    internal long DoorAt = -1;
    internal string KnownPart = "";
    internal float LastSpeed;
    internal bool HasCup;
    internal Vector2 LastPos;
    internal Toil? LastToil;
    internal string? PendingReply;
    internal long PendingAt = -1;
    internal int PendingTo = -1;
    internal readonly Dictionary<int, int> TuneHeard = new();
    internal readonly Dictionary<int, int> SawTic = new();
    internal readonly Dictionary<int, long> SaidTic = new();
    internal readonly Dictionary<int, long> Knelt = new();
    internal readonly Dictionary<int, long> Nodded = new();
    public bool Ticcing(long now) => now < TicUntil;
    public Tic? Current(long now) => now < TicUntil ? Doing : null;
}

public sealed class GestNote
{
    public long Tick { get; init; }
    public int Crew { get; init; }
    public Mien Mien { get; init; }
    public int Other { get; init; } = -1;
    public string Line { get; init; } = "";
}

public sealed class GestureStats
{
    public readonly int[] ByMien = new int[20];
    public readonly int[] ByTic = new int[12];
    public int SetDowns, Asked, Steadied, Spills, LampMoves, Addressed, WrapUps, Answered, AnsweredAfterWrap, Kneels, Calls, Glances, Nods, Swaps, KnownSpots, Familiar;
    public int Tics, StressTics, CalmAgain, Noticed, Opened, Denied, Annoyed, Hums, SingAlong, TuneLearned, Cut, CutAtDoor, Resumed, Forgot, TicAdvice;
    public string Summary() =>
        $"몸짓 {ByMien.Sum()}(종류 {ByMien.Count(x => x > 0)}) · 짐 내려놓기 {SetDowns} · 문 부탁 {Asked} · 잔 버팀 {Steadied}(흘림 {Spills}) · 등 옮기기 {LampMoves} · 말 걸림 {Addressed}(정리 {WrapUps} · 대답 {Answered}/정리 뒤 {AnsweredAfterWrap}) · " +
        $"무릎 {Kneels}(부름 {Calls}) · 번갈아 보기 {Glances}(끄덕임 {Nods}) · 손 바꿈 {Swaps} · 익숙한 부위 {KnownSpots}(빠른 수리 {Familiar}) · 버릇 {Tics}(종류 {ByTic.Count(x => x > 0)} · 스트레스로 바뀜 {StressTics} · 돌아옴 {CalmAgain}) · " +
        $"알아챔 {Noticed}(털어놓음 {Opened} · 발뺌 {Denied}) · 거슬림 {Annoyed} · 흥얼 {Hums}(따라 부름 {SingAlong} · 배움 {TuneLearned}) · 끊긴 대화 {Cut}(문 앞 {CutAtDoor} · 이어 감 {Resumed} · 잊음 {Forgot}) · 컴퓨터 {TicAdvice}";
}

public sealed partial class GestureSystem
{
    public static bool Off { get; set; } = Environment.GetEnvironmentVariable("SHIPSIM_GESTURE_OFF") != null;
    public static readonly string[] Tunes = { "「별 건너는 배」", "「항구의 새벽」", "「바람개비 돌아라」", "「붉은 모래 언덕」", "「얼음 강 왈츠」", "「어머니의 정원」", "「돌아오는 길」", "「작은 등불」" };

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6421 + 977));
    private readonly Dictionary<int, MannerState> _st = new();
    public GestureStats Stats { get; } = new();
    public List<GestNote> Notes { get; } = new();
    private readonly List<CrewMember> _downed = new();
    private readonly Dictionary<(int, int, ItemKind?), int> _fixed = new();

    public GestureSystem(World w) => _w = w;

    public MannerState Of(CrewMember c)
    {
        if (_st.TryGetValue(c.Id, out var s)) return s;
        uint h = unchecked((uint)(c.Id * 2654435761u) ^ (uint)(_w.Seed * 40503));
        s = new MannerState { Id = c.Id, LeftHanded = h % 10 == 3, Tune = ((int)c.Background * 3 + (c.Id % 4 == 0 ? c.Id : 0)) % Tunes.Length, NextTic = _w.Tick + 30 + (c.Id * 17) % 400 };
        PickTics(c, s, h);
        _st[c.Id] = s;
        return s;
    }
    public MannerState? Peek(CrewMember c) => _st.TryGetValue(c.Id, out var s) ? s : null;

    /// <summary>화면 · 인형: 지금 보이는 몸짓.</summary>
    public Mien MienOf(CrewMember c)
    {
        if (!_st.TryGetValue(c.Id, out var s)) return Mien.None;
        long now = _w.Tick;
        if (s.M != Mien.None && now < s.MUntil) return s.M;
        if (now >= s.WrapFrom && now < s.WrapUntil) return Mien.WrapUp;
        if (now < s.GlanceUntil && s.GlanceAt >= 0) return (now / 14 + c.Id) % 2 == 0 ? Mien.GlanceGauge : Mien.GlanceSenior;
        return Mien.None;
    }

    /// <summary>인형: 무릎 꿇은 자세.</summary>
    public bool Kneeling(CrewMember c) => _st.TryGetValue(c.Id, out var s) && s.M == Mien.KneelBy && _w.Tick < s.MUntil;

    /// <summary>화면: 시선 (고개를 돌리는 쪽).</summary>
    public Vector2? GazeOf(CrewMember c)
    {
        if (!_st.TryGetValue(c.Id, out var s)) return null;
        var m = MienOf(c);
        if (m == Mien.None) return null;
        if (m == Mien.GlanceSenior && Person(s.GlanceAt) is CrewMember sr) return sr.Position;
        if (m == Mien.GlanceGauge) return c.Position + c.Facing;
        if (s.LookCrew >= 0 && Person(s.LookCrew) is CrewMember o) return o.Position;
        return s.LookAt;
    }

    private void Set(CrewMember c, MannerState s, Mien m, int ticks, string line = "", int other = -1, bool hold = false)
    {
        s.M = m; s.MSince = _w.Tick; s.MUntil = _w.Tick + ticks;
        if (hold) s.HoldStep = s.MUntil;
        Stats.ByMien[(int)m]++;
        if (line.Length > 0 && c.IsAwake) c.Say(_w, Persona.Say(c, line));
        Notes.Add(new GestNote { Tick = _w.Tick, Crew = c.Id, Mien = m, Other = other, Line = line });
        if (Notes.Count > 600) Notes.RemoveRange(0, 150);
    }

    private string Pick(string[] pool) => pool[R.Range(0, pool.Length)];

    private CrewMember? Person(int id)
    {
        if (id < 0) return null;
        foreach (var c in _w.Crew) if (c.Id == id) return c;
        return null;
    }

    // ───────────────────────────── 걸음 (Locomotion.Step이 곱한다) ─────────────────────────────

    /// <summary>걸음 배율: 짐을 내려놓는 중 · 잔을 바로잡는 중 · 무릎 꿇은 중이면 0 · 양손 짐으로 손이 필요한 문 앞에 서면 내려놓거나 부탁한다.</summary>
    public float StepMul(CrewMember c, List<Cell> path)
    {
        if (Off) return 1f;
        long now = _w.Tick;
        _st.TryGetValue(c.Id, out var s);
        if (s != null) { Cup(c, s); if (s.HoldStep > now) return 0f; }
        if (_downed.Count > 0 && KneelNear(c, s ??= Of(c))) return 0f;
        if (c.Carrying == null || c.CarryingPerson != null || c.PathIndex >= path.Count || c.Outside) return 1f;
        Door? door = null;
        for (int k = c.PathIndex; k < Math.Min(path.Count, c.PathIndex + 2); k++)
            if (_w.Ship.DoorAt(path[k]) is Door d) { door = d; break; }
        if (door == null || door.Openness >= 0.5f || !NeedsHand(door)) return 1f;
        s ??= Of(c);
        if (s.DoorDone == door.Id && now - s.DoorAt < SimTime.Minutes(10)) return 1f;
        if (!Puppet.Of(_w, c).TwoHands) return 1f;
        s.DoorDone = door.Id; s.DoorAt = now;
        var beyond = door.RoomA == c.Room ? door.RoomB : door.RoomA;
        CrewMember? helper = null;
        float bd = 20f;
        foreach (var o in _w.Crew)
        {
            if (o == c || !o.CanAct || !o.IsAwake || o.IsChild || o.Outside || o.IsMoving || o.Carrying != null || o.CarryingPerson != null || o.Job?.Urgent == true || o.Room == null || o.Room != c.Room && o.Room != beyond && (o.Position - door.Cell.Center).LengthSquared() > 4f) continue;
            float d2 = (o.Position - door.Cell.Center).LengthSquared();
            if (d2 < bd) { bd = d2; helper = o; }
        }
        s.LookAt = door.Cell.Center; s.LookCrew = -1;
        if (helper != null)
        {
            Set(c, s, Mien.AskDoor, 8, Pick(new[] { $"{helper.Name}, 문 좀 잡아 줄래?", "손이 없어서 — 문 좀!", "미안, 문 좀 열어 줘" }), helper.Id, hold: false);
            s.HoldStep = now + 3;
            var hs = Of(helper);
            hs.LookAt = door.Cell.Center; hs.LookCrew = -1;
            Set(helper, hs, Mien.HoldDoor, 30, "", c.Id);
            helper.Facing = Vector2.Normalize(door.Cell.Center - helper.Position + new Vector2(0.0001f, 0f));
            hs.PendingReply = Pick(new[] { "응, 잡았어", "들어가", "조심해 — 모서리" }); hs.PendingAt = now + 2; hs.PendingTo = c.Id;
            if (helper.Room == c.Room || (helper.Position - door.Cell.Center).LengthSquared() < 4f) door.Request();
            Stats.Asked++;
            c.ChangeAffinity(helper, 0.004f); helper.ChangeAffinity(c, 0.002f);
        }
        else
        {
            Set(c, s, Mien.SetDown, 7, R.Chance(0.3f) ? Pick(new[] { "잠깐 — 내려놓고", "끙…", "손이 모자라네" }) : "");
            s.HoldStep = now + 7;
            s.Spot = c.Position + c.Facing * 0.45f;
            Stats.SetDowns++;
        }
        return 0f;
    }

    private bool NeedsHand(Door d) =>
        d.Locked || !d.Powered || d.MotorBroken || _w.Body.DoorOf(d) is DoorBody db && (db.Lock != LockKind.None || db.SensorBroken);

    // ───────────────────────────── 말 걸기 (React · 대화가 부른다) ─────────────────────────────

    /// <summary>누가 말을 걸었다: 고개부터 돌리고, 하던 걸 정리하고 (그다음 대답).</summary>
    public void Addressed(CrewMember o, CrewMember by)
    {
        if (Off || o.Dead || !o.IsAwake) return;
        var s = Of(o);
        long now = _w.Tick;
        if (now - s.AddrAt < SimTime.Minutes(2)) return;
        s.AddrAt = now;
        Stats.Addressed++;
        s.LookCrew = by.Id; s.LookAt = by.Position;
        bool urgent = o.Job?.Urgent == true;
        int wrap = urgent ? 0 : o.Pose == Pose.Working ? 26 + (int)(14 * o.Traits.Diligence) : o.Job?.Activity is EatActivity ? 10 : Puppet.HeldOf(_w, o) != HeldThing.None ? 12 : 0;
        Set(o, s, Mien.HeadTurn, 5, "", by.Id);
        if (wrap > 0)
        {
            s.WrapFrom = now + 5; s.WrapUntil = now + 5 + wrap;
            Stats.WrapUps++;
            Stats.ByMien[(int)Mien.WrapUp]++;
            if (o.Pose == Pose.Working && R.Chance(0.5f)) { s.PendingReply = Pick(new[] { "잠깐만 — 이것만 조이고", "응, 잠깐", "하던 거 마저 하고" }); s.PendingAt = now + 6; s.PendingTo = by.Id; }
        }
    }

    /// <summary>대답했다 (정리를 다 했는지 센다).</summary>
    public void Answered(CrewMember o, CrewMember by)
    {
        if (Off || !_st.TryGetValue(o.Id, out var s) || s.AddrAt < 0) return;
        Stats.Answered++;
        if (_w.Tick >= s.WrapUntil) Stats.AnsweredAfterWrap++;
        s.LookCrew = by.Id;
    }

    // ───────────────────────────── 익숙한 부위 (Chores 수리 시간이 곱한다) ─────────────────────────────

    /// <summary>전에 이 설비의 같은 부위를 고쳐 본 사람은 그 부위부터 손이 간다 (반복 고장이면 시간 0.75배).</summary>
    public float Familiar(CrewMember c, Machine m, FaultKind k)
    {
        if (Off) return 1f;
        var part = _w.Parts.Owner(m, k);
        var key = (c.Id, m.Body.Id, part?.Kind);
        bool known = _fixed.TryGetValue(key, out var n) && n > 0 && (part == null || part.Failures >= 1);
        _fixed[key] = n + 1;
        if (!known) return 1f;
        var s = Of(c);
        s.KnownMachine = m.Body.Id;
        s.KnownPart = part?.Spec.Name ?? Faults.Spec(k).Name;
        s.KnownUntil = _w.Tick + SimTime.Hours(4);
        Stats.Familiar++;
        return 0.75f;
    }

    // ───────────────────────────── 틱 ─────────────────────────────

    /// <summary>성능 측정 (Stopwatch 틱).</summary>
    public static long UpdateTicks;

    public void Update(float dt)
    {
        if (Off) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Tick();
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    // 시스템 틱은 15틱마다 온다 (World.SystemInterval): 사람마다 30틱에 한 번 본다. 잔 · 문은 걸음마다(StepMul).
    private void Tick()
    {
        var w = _w;
        long now = w.Tick;
        _downed.Clear();
        foreach (var c in w.Crew) if (c.Down && !c.Dead && !c.Away) _downed.Add(c);
        if (_downed.Count > 0) Kneel();
        long half = now / World.SystemInterval;
        if (half % 2 == 0) { Chats(); ResumeTalks(); }
        foreach (var c in w.Crew)
        {
            if ((c.Id + half) % 2 != 0 || c.Dead || c.Away) continue;
            Look(c);
        }
    }

    private bool HoldsCup(CrewMember c) =>
        Puppet.HeldOf(_w, c) == HeldThing.Cup || _w.React.Peek(c) is ReactState rs && _w.Tick < rs.CupUntil;

    /// <summary>뜨거운 잔을 들고 가다 갑자기 멈춤 → 버팀 · 흘림 (걸음마다 — 지난 걸음은 갔는데 이번엔 못 갔다).</summary>
    private void Cup(CrewMember c, MannerState s)
    {
        long now = _w.Tick;
        if (now >= s.CupCheck) { s.CupCheck = now + 20; s.HasCup = HoldsCup(c); }
        float speed = (c.Position - s.LastPos).Length();
        bool stopped = s.LastSpeed > 0.05f && speed < 0.005f && s.LastStepAt == now - 1;
        s.LastSpeed = s.LastStepAt == now - 1 ? speed : 0f;
        s.LastPos = c.Position;
        s.LastStepAt = now;
        if (!s.HasCup || !stopped || now < s.CupNext) return;
        {
            {
                s.CupNext = now + SimTime.Minutes(4);
                float spill = 0.2f + 0.3f * (1f - c.Traits.Calm) + (Life.Has(c, Habit.Hasty) ? 0.2f : 0f) + (c.Job?.Urgent == true ? 0.2f : 0f) + 0.3f * c.Vitals.Injury - (Life.Has(c, Habit.Methodical) ? 0.15f : 0f);
                if (R.Chance(spill))
                {
                    Set(c, s, Mien.CupSpill, 30, Pick(new[] { "앗, 뜨거!", "아 — 흘렸다", "뜨, 뜨거워!" }), -1, hold: true);
                    s.HoldStep = now + 4;
                    s.Spot = c.Position + c.Facing * 0.5f;
                    _w.Body.RaiseMark(c.Cell, CellMark.Wet, 0.5f, "엎지른 커피");
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
                    c.Soil.Clothes[(int)SoilKind.Bio] = MathF.Min(1f, c.Soil.Clothes[(int)SoilKind.Bio] + 0.08f);
                    Stats.Spills++;
                }
                else
                {
                    Set(c, s, Mien.CupSteady, 18, R.Chance(0.25f) ? Pick(new[] { "어이쿠", "휴 — 안 흘렸다", "조심 좀!" }) : "");
                    s.HoldStep = now + 2;
                    Stats.Steadied++;
                }
            }
        }
    }

    /// <summary>쓰러진 사람 곁을 지나면 무릎을 꿇고 숨을 살피고 사람을 부른다.</summary>
    private void Kneel()
    {
        var w = _w;
        long now = w.Tick;
        foreach (var d in _downed)
        {
            if (d.Room is not Room room || d.CarriedBy != null) continue;
            foreach (var o in w.Crew)
            {
                if (o == d || !o.CanAct || !o.IsAwake || o.Room != room || o.Outside || o.CarryingPerson != null || o.IsChild && o.Age < 8f) continue;
                if ((o.Position - d.Position).LengthSquared() > 4.8f) continue;
                var s = Of(o);
                if (s.Knelt.TryGetValue(d.Id, out var t) && now - t < SimTime.Hours(2)) continue;
                if (o.Job?.Urgent == true && o.Job.TargetRoom != room || o.IsMoving) continue; // 걷는 사람은 걸음마다 본다
                KneelBy(o, s, d, room, 0);
            }
        }
    }

    /// <summary>걷다가 쓰러진 사람 바로 곁에 닿았다 (걸음마다 본다): 무릎을 꿇고 숨부터 살핀다 — 들것을 가져온 사람도 잠깐.</summary>
    private bool KneelNear(CrewMember o, MannerState s)
    {
        if (o.CarryingPerson != null || o.Outside || o.Room is not Room room || !o.IsAwake) return false;
        foreach (var d in _downed)
        {
            if (d.Room != room || d.CarriedBy != null || d.Dead || (o.Position - d.Position).LengthSquared() > 2.9f) continue;
            if (s.Knelt.TryGetValue(d.Id, out var t) && _w.Tick - t < SimTime.Hours(2)) continue;
            KneelBy(o, s, d, room, o.Job?.Urgent == true ? 15 : 0);
            return true;
        }
        return false;
    }

    private void KneelBy(CrewMember o, MannerState s, CrewMember d, Room room, int ticks)
    {
        var w = _w;
        s.Knelt[d.Id] = w.Tick;
        s.LookCrew = d.Id; s.LookAt = d.Position; s.Spot = d.Position;
        bool medic = o.Role == CrewRole.Medic || o.Quals.Contains(Qual.Medic);
        Set(o, s, Mien.KneelBy, ticks > 0 ? ticks : medic ? 60 : 35, Pick(medic ? new[] { $"{d.Name}, 들려요? 숨은 쉰다", "맥 짚어 볼게", "움직이지 마세요" } : new[] { $"{d.Name}! 내 말 들려?", "숨은 쉬어 — 정신 차려", "괜찮아? 눈 좀 떠 봐" }), d.Id, hold: true);
        o.Facing = Vector2.Normalize(d.Position - o.Position + new Vector2(0.0001f, 0f));
        w.Brain2.Beliefs.Learn(o, Topic.Down, d.Id, 1, BeliefSource.Seen, 1f);
        Stats.Kneels++;
        if (!medic) Call(o, d, room);
    }

    /// <summary>"사람 쓰러졌어!" — 소리가 닿는 사람은 안다 (같은 방 · 열린 문 너머).</summary>
    private void Call(CrewMember o, CrewMember d, Room room)
    {
        var w = _w;
        w.Hearing.Emit(Noise.Voice, o, 0.7f, 1);
        foreach (var x in w.Crew)
        {
            if (x == o || x == d || !x.IsAwake || x.Room is not Room xr) continue;
            float pass = xr == room ? 1f : w.Hearing.Pass(room, xr);
            if (0.7f * pass * w.Hearing.Sense(x, Noise.Voice) < HearingSystem.Audible) continue;
            w.Brain2.Beliefs.Learn(x, Topic.Down, d.Id, 1, BeliefSource.Told, 0.8f, o.Id);
            Stats.Calls++;
        }
    }

    /// <summary>한 사람을 1분마다: 일하는 손 · 시선 · 버릇 · 흥얼거림 · 알아채기.</summary>
    private void Look(CrewMember c)
    {
        var w = _w;
        long now = w.Tick;
        var s = Of(c);
        if (s.PendingReply != null && now >= s.PendingAt)
        {
            if (c.IsAwake) c.Say(w, Persona.Say(c, s.PendingReply));
            s.PendingReply = null;
        }
        if (!c.IsAwake || c.Down) return;
        var cur = c.Job?.Current;
        bool working = cur is WorkToil && c.Pose == Pose.Working;
        // 공구를 다른 손으로 (잘 쓰는 팔을 다쳤다)
        if (working && cur != s.LastToil)
        {
            var arm = Puppet.Arm(c, s.LeftHanded ? BodyPart.LeftArm : BodyPart.RightArm);
            if (arm is ArmState.Hurt or ArmState.Lost)
            {
                Set(c, s, Mien.SwapGrip, 40, now >= s.NextLine ? Pick(new[] { s.LeftHanded ? "오른손으로 해야겠다" : "왼손으로 해야겠다", "이쪽 손은 영 어색하네", "아야 — 손 바꿔야지" }) : "");
                s.NextLine = now + SimTime.Hours(4);
                Stats.Swaps++;
            }
        }
        s.LastToil = cur;
        // 익숙한 부위부터
        if (working && s.KnownMachine >= 0 && now < s.KnownUntil && w.Ship.Machines.FirstOrDefault(m => m.Body.Id == s.KnownMachine) is Machine km && (km.Body.Center - c.Position).LengthSquared() < 9f)
        {
            int side = (km.Body.Id * 7) % 4;
            s.Spot = km.Body.Center + new Vector2(side switch { 0 => 0.35f, 1 => -0.35f, _ => 0f }, side switch { 2 => 0.35f, 3 => -0.35f, _ => 0f });
            Set(c, s, Mien.KnownSpot, 45, Pick(new[] { $"또 {s.KnownPart}네 — 여기부터 보자", $"지난번에도 {s.KnownPart}였지", "어디 보자… 역시 여기" }), km.Body.Id);
            s.KnownMachine = -1;
            Stats.KnownSpots++;
        }
        else if (now >= s.KnownUntil) s.KnownMachine = -1;
        if (working) { Lamp(c, s); Glance(c, s); }
        else s.GlanceUntil = -1;
        Tics(c, s);
    }

    /// <summary>캄캄한 방에서 일하는데 작업등이 멀다 → 등을 일하는 자리 곁으로.</summary>
    private void Lamp(CrewMember c, MannerState s)
    {
        var w = _w;
        if (c.Room is not Room room || room.Powered && !room.LightsOut || w.Tick < s.MUntil && s.M == Mien.LampCarry) return; // 천장 조명이 꺼졌다 (비상등만으론 손이 안 보인다)
        var work = c.Position + c.Facing * 0.8f;
        foreach (var d in w.Portable.Devices)
        {
            if (d.Kind != PortableKind.WorkLamp || !d.Placed || !d.Running || d.User != null && d.User != c || w.Portable.RoomOf(d) != room) continue;
            float far = (d.At.Center - work).Length();
            if (far <= d.Spec.LightRadius * 0.6f || far > 9f) continue;
            Cell? best = null;
            float bs = float.MaxValue;
            var wc = Cell.FromPosition(work);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    var x = new Cell(wc.X + dx, wc.Y + dy);
                    if (x == c.Cell || w.Ship.RoomAt(x) != room || !w.Ship.IsWalkable(x) || w.Ship.FurnitureAt(x) != null) continue;
                    float sc = (x.Center - work).Length() + 0.4f * MathF.Abs((x.Center - c.Position).Length() - 1f);
                    if (sc < bs) { bs = sc; best = x; }
                }
            if (best is not Cell to) return;
            s.Spot = d.At.Center;
            d.At = to;
            d.Aim = work;
            d.User = c;
            Set(c, s, Mien.LampCarry, 40, Pick(new[] { "안 보여서 원 — 등 좀 이쪽으로", "불 좀 가까이 대고", "이제 좀 보이네" }));
            Stats.LampMoves++;
            w.Portable.Stats.LampMoves++;
            return;
        }
    }

    /// <summary>신입: 계기와 선배 얼굴을 번갈아 본다 · 선배가 끄덕이면 손이 덜 떨린다.</summary>
    private void Glance(CrewMember c, MannerState s)
    {
        var w = _w;
        if (c.Job?.Current is not WorkToil wt) return;
        var skill = wt.Skill;
        bool rookie = w.Relations.IsNewcomer(c) || c.SkillLevel(skill) < 0.3f;
        if (!rookie) { s.GlanceUntil = -1; return; }
        CrewMember? senior = null;
        float bd = 64f;
        foreach (var o in w.Crew)
        {
            if (o == c || !o.IsAwake || o.Down || o.IsChild || o.Room != c.Room || o.SkillLevel(skill) < 0.55f) continue;
            float d2 = (o.Position - c.Position).LengthSquared();
            if (d2 < bd) { bd = d2; senior = o; }
        }
        if (senior == null) { s.GlanceUntil = -1; return; }
        if (s.GlanceAt != senior.Id || _w.Tick >= s.GlanceUntil) { s.GlanceAt = senior.Id; s.Glances = 0; Stats.ByMien[(int)Mien.GlanceGauge]++; Stats.ByMien[(int)Mien.GlanceSenior]++; }
        s.GlanceUntil = _w.Tick + 50;
        s.Glances++;
        Stats.Glances += 2;
        if (s.Glances % 3 != 0) return;
        var ss = Of(senior);
        if (ss.Nodded.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(1)) return;
        ss.Nodded[c.Id] = _w.Tick;
        ss.LookCrew = c.Id; ss.LookAt = c.Position;
        Set(senior, ss, Mien.Nod, 15, R.Chance(0.6f) ? Pick(new[] { "그렇지, 그대로", "잘하고 있어", "천천히 — 맞아" }) : "", c.Id);
        c.Practice(skill, 0.01f);
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f);
        c.ChangeAffinity(senior, 0.006f);
        Stats.Nods++;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Stats.ByMien.Sum()); I(Stats.Tics); I(Stats.StressTics); I(Stats.Noticed); I(Stats.SingAlong); I(Stats.Resumed); I(Stats.Cut); I(Stats.Spills); I(Stats.Kneels); I(Stats.Familiar);
        foreach (var c in _w.Crew)
            if (_st.TryGetValue(c.Id, out var s)) { I((int)s.M); I(s.TicUntil); I(s.StressTic.HasValue ? (int)s.StressTic.Value : -1); }
    }
}
