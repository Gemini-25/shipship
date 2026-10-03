using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.8 모든 변화에 누군가 반응한다.
//  · 방이 바뀌면(캄캄해짐 · 더워짐 · 추워짐 · 연기 · 진동) 그 방 사람들이 차례로 알아챈다 — 말 한 줄 + 몸짓 + (필요하면) 짧은 행동.
//  · 걸어가다 앞 칸의 물기 · 기름 · 서리를 보면 조심 걸음(배 본체의 조심 걷기와 같은 규칙), 유리 조각을 보면 몸을 틀어 돌아간다.
//  · 낯선 소리(전조가 붙은 설비) → 쳐다봄 → 궁금한 사람은 가서 귀를 대 본다 (찾으면 정비 전조로 이어진다).
//  · 냄새 · 연기 · 경보 · 컴퓨터 방송 · 아침 브리핑 · 새로 걸린 소품 · 우는 사람 · 남의 이상한 몸짓에도 사람마다 다르게 반응한다.
//  · 같은 문제를 사람마다 다르게 푼다 (ReactWays.cs): 성격 · 솜씨 · 가진 것 · 해 본 것 · 이미 남이 고른 것.
//  · 말은 지금을 담는다 (ReactTalk.cs): 예보 · 브리핑 · 회의 결정 · 재판 · 최근 사고 · 배 상태 — 최근 한 말은 되풀이하지 않는다.
// 대부분 몇 초짜리라 가볍다: 한 사람을 2분마다 한 번 보고, 방이 바뀌면 그 방 사람만 앞당긴다.

/// <summary>무엇에 반응했나.</summary>
public enum Stir : byte { Heat, Cold, Dark, Wet, Glass, Sound, Smell, Smoke, Odd, Shake, Alarm, Voice, Novel, Cry, Chat, Back }

/// <summary>화면에 그릴 몸짓 (각자 다른 그림 — View/ShipViewReact).</summary>
public enum Gesture : byte
{
    None, WipeBrow, FanSelf, ShedJacket, Shiver, HugSelf, Wrap, Torch, Screen, FeelWall, Tiptoe, Sidestep,
    Look, Listen, CoverNose, Sniff, Cough, Brace, CoverEars, Admire, Comfort, Stare, Call, RubHands, Jog,
    Huddle, Cry, Talk, Shrug, Point, Window, Nod, Cup, Kneel,
}

/// <summary>한 사람의 반응 상태 (몸짓 · 지금 쓰는 방법 · 겉옷 · 담요 · 손전등 · 최근 한 말).</summary>
public sealed class ReactState
{
    public int Id { get; init; }
    public Gesture G { get; internal set; }
    public long GSince { get; internal set; } = -1;
    public long GUntil { get; internal set; } = -1;
    /// <summary>쳐다보는 곳 (칸 좌표).</summary>
    public Vector2? LookAt { get; internal set; }
    public int LookCrew { get; internal set; } = -1;
    /// <summary>지금 쓰는 방법 (손전등 · 담요 …)과 무엇 때문인가.</summary>
    public string? Way { get; internal set; }
    public Stir WayFor { get; internal set; }
    public long WaySince { get; internal set; } = -1;
    public string? WayWhy { get; internal set; }
    /// <summary>겉옷을 벗어 허리에 묶었다.</summary>
    public bool JacketOff { get; internal set; }
    public long JacketSince { get; internal set; } = -1;
    /// <summary>담요를 둘렀다 (내 담요 번호).</summary>
    public bool Wrapped { get; internal set; }
    public int Blanket { get; internal set; } = -1;
    public bool TorchOn { get; internal set; }
    /// <summary>화면용 땀 · 떨림 단계 0~3.</summary>
    public byte Sweat { get; internal set; }
    public byte Shiver { get; internal set; }
    /// <summary>손에 든 따뜻한 · 시원한 잔 (이때까지).</summary>
    public long CupUntil { get; internal set; } = -1;
    internal long Next;
    internal readonly long[] Last = new long[16];
    internal readonly bool[] Ep = new bool[16];
    internal readonly List<string> Keys = new();
    internal readonly List<string> Texts = new();
    internal readonly HashSet<int> SeenProps = new();
    internal ReactAct? Pending;
    internal int LastAlarm;
    internal int LastHeard = -1;
    internal int LastBrief = -1;
    internal long OddUntil = -1;
    internal int Detours;
    internal string? OddWhat;
    public bool Moving(World w) => G != Gesture.None && w.Tick < GUntil;
}

/// <summary>반응에서 이어지는 짧은 행동 (ReactActivity 가 한다).</summary>
public enum ReactKind : byte { Device, Blanket, Window, Screen, Near, Jog, Check, TalkTo, Comfort, Admire, Move, Fix, Cup }

public sealed class ReactAct
{
    public ReactKind Kind { get; init; }
    public Stir For { get; init; }
    public Cell To { get; init; }
    public Vector2 Face { get; init; }
    public int Target { get; init; } = -1;
    public string Label { get; init; } = "";
    public string? Done { get; init; }
    public string Way { get; init; } = "";
    public float Score { get; init; }
    public long Until { get; init; }
    public PortableKind Device { get; init; }
}

/// <summary>반응 한 줄 기록 (시험 · 화면 · 일기).</summary>
public readonly record struct ReactNote(long Tick, int Crew, Stir Stir, string Way, Gesture Gesture, string Line, int Room);

public sealed class ReactStats
{
    public readonly int[] ByStir = new int[16];
    public int Lines, Repeats, Silent, Acts, ActsDone, Careful, Reckless, Sidesteps, Checks, Found, Talks, Replies, Comforts, Admired,
        Detours, Jackets, JacketsBack, Wraps, Torches, Devices, Fixes, Huddles, Jogs, Cups, Chats, Topical, Topics, Back, Odd, Stares, Cries, Advice, Scans;
    public string Summary() =>
        $"반응 {ByStir.Sum()} (더위 {ByStir[0]} · 추위 {ByStir[1]} · 어둠 {ByStir[2]} · 젖은 바닥 {ByStir[3]} · 유리 {ByStir[4]} · 소리 {ByStir[5]} · 냄새 {ByStir[6]} · 연기 {ByStir[7]} · " +
        $"이상한 몸짓 {ByStir[8]} · 진동 {ByStir[9]} · 경보 {ByStir[10]} · 방송 {ByStir[11]} · 새 물건 {ByStir[12]} · 울음 {ByStir[13]} · 수다 {ByStir[14]} · 돌아옴 {ByStir[15]}) · " +
        $"말 {Lines}(되풀이 {Repeats} · 말없이 {Silent} · 지금 이야기 {Topical}) · 행동 {Acts}(끝냄 {ActsDone}) · 조심 걸음 {Careful}(무시 {Reckless}) · 돌아감 {Sidesteps} · " +
        $"옆 칸으로 {Detours} · 소리 확인 {Checks}(찾음 {Found}) · 말 걸기 {Talks}(대답 {Replies}) · 위로 {Comforts} · 구경 {Admired} · 겉옷 {Jackets}(다시 {JacketsBack}) · 담요 {Wraps} · 손전등 {Torches} · " +
        $"장비 {Devices} · 조명 수리 {Fixes} · 붙기 {Huddles} · 제자리 뛰기 {Jogs} · 잔 {Cups} · 수다 {Chats} · 이야깃거리 {Topics} · 컴퓨터 권고 {Advice} · 감지기 재확인 {Scans}";
}

public sealed partial class ReactSystem
{
    /// <summary>견줘 보기 · 성능 측정용 (끄면 아무도 반응하지 않는다).</summary>
    public static bool Off { get; set; } = Environment.GetEnvironmentVariable("SHIPSIM_REACT_OFF") != null;
    /// <summary>한 사람을 다시 보는 간격 (틱).</summary>
    public const int LookEvery = 50;
    /// <summary>몸짓이 보이는 시간.</summary>
    public static readonly int Short = SimTime.Minutes(3), Long = SimTime.Minutes(6);

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 431));
    private readonly Dictionary<int, ReactState> _st = new();
    public ReactStats Stats { get; } = new();
    public List<ReactNote> Notes { get; } = new();

    // 방 상태 (바뀌면 그 방 사람을 앞당긴다)
    private bool[] _dark = Array.Empty<bool>(), _smoke = Array.Empty<bool>(), _shake = Array.Empty<bool>();
    private sbyte[] _temp = Array.Empty<sbyte>();
    private readonly Dictionary<int, List<CrewMember>> _here = new();
    /// <summary>방마다 이번 일에 사람들이 이미 고른 방법 (같은 걸 덜 고른다).</summary>
    private readonly Dictionary<long, (List<string> ways, long tick)> _taken = new();
    /// <summary>낯선 소리를 내는 설비 (방 번호 → 설비).</summary>
    private readonly Dictionary<int, Machine> _noisy = new();
    private long _nextNoisy;
    private int _alarmSerial;
    private CrisisLevel _lastLevel;

    public ReactSystem(World w) => _w = w;

    public ReactState Of(CrewMember c)
    {
        if (!_st.TryGetValue(c.Id, out var s)) { s = new ReactState { Id = c.Id, Next = _w.Tick + 7 + (c.Id * 13) % LookEvery }; _st[c.Id] = s; }
        return s;
    }
    public ReactState? Peek(CrewMember c) => _st.TryGetValue(c.Id, out var s) ? s : null;

    /// <summary>화면: 지금 보이는 몸짓 (없으면 None).</summary>
    public Gesture GestureOf(CrewMember c) => _st.TryGetValue(c.Id, out var s) && _w.Tick < s.GUntil ? s.G : Gesture.None;

    /// <summary>화면: 몸을 돌려 쳐다보는 쪽 (걷는 중이 아닐 때만).</summary>
    public Vector2? FaceAt(CrewMember c)
    {
        if (c.IsMoving || !_st.TryGetValue(c.Id, out var s) || _w.Tick >= s.GUntil || s.LookAt is not Vector2 at) return null;
        var d = at - c.Position;
        return d.LengthSquared() < 0.01f ? null : Vector2.Normalize(d);
    }

    /// <summary>다른 시스템이 남의 눈에 띄는 행동을 알린다 (장난 · 몰래 하는 일 · 혼잣말 …) — 곁의 사람이 쳐다보고 말을 건다.</summary>
    public void MarkOdd(CrewMember who, string what, float minutes = 10f)
    {
        var s = Of(who);
        s.OddUntil = _w.Tick + SimTime.Minutes(minutes);
        s.OddWhat = what;
    }

    /// <summary>캄캄할 때 실수 배율 (손전등 · 단말 불빛 · 창가 · 남의 불빛 곁): Portable.DarkMistake 가 곱한다.</summary>
    public float DarkMul(CrewMember c)
    {
        if (Off || !_st.TryGetValue(c.Id, out var s) || s.Way == null || s.WayFor != Stir.Dark) return 1f;
        return s.TorchOn ? 0.35f : s.Way is "screen" or "window" or "follow" or "wrist" ? 0.7f : 1f;
    }

    /// <summary>몸에 열이 차는 배율 (겉옷 · 선풍기 앞 · 시원한 잔): Perils.Heat 가 곱한다.</summary>
    public float HeatMul(CrewMember c)
    {
        if (Off || !_st.TryGetValue(c.Id, out var s)) return 1f;
        float m = 1f;
        if (s.JacketOff) m *= 0.85f;
        if (s.Way == "fan" && s.WayFor == Stir.Heat) m *= 0.75f;
        if (_w.Tick < s.CupUntil && s.WayFor == Stir.Heat) m *= 0.9f;
        return m;
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        var ship = w.Ship;
        int n = ship.Rooms.Count;
        if (_dark.Length != n) { _dark = new bool[n]; _smoke = new bool[n]; _shake = new bool[n]; _temp = new sbyte[n]; }
        foreach (var l in _here.Values) l.Clear();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Room is not Room r) continue;
            if (!_here.TryGetValue(r.Id, out var l)) _here[r.Id] = l = new List<CrewMember>();
            l.Add(c);
        }
        // 방이 바뀌었다 → 그 방 사람을 앞당긴다 (한 사람씩 몇 초 간격으로)
        for (int i = 0; i < n; i++)
        {
            var r = ship.Rooms[i];
            if (r.Detached) continue;
            bool dark = r.Dark || PortableSystem.Unlit(r);
            bool smoke = r.Air.Smoke > 0.06f;
            bool shake = r.Vibration > 0.45f;
            float t = r.Air.Temperature;
            sbyte band = (sbyte)(t > 27.5f ? 1 : t < 16f ? -1 : _temp[i] == 1 && t > 25.5f ? 1 : _temp[i] == -1 && t < 18f ? -1 : 0);
            bool changed = dark != _dark[i] || smoke && !_smoke[i] || shake && !_shake[i] || band != _temp[i];
            if (!dark && _dark[i]) Forget(i, Stir.Dark);
            if (band != -1 && _temp[i] == -1) Forget(i, Stir.Cold);
            if (band != 1 && _temp[i] == 1) Forget(i, Stir.Heat);
            _dark[i] = dark; _smoke[i] = smoke; _shake[i] = shake; _temp[i] = band;
            if (!changed || !_here.TryGetValue(i, out var ppl)) continue;
            int k = 0;
            foreach (var c in ppl) { var s = Of(c); s.Next = Math.Min(s.Next, w.Tick + 4 + 9 * k++); }
        }
        // 경보가 새로 울렸다
        var lvl = Crisis.Level(w);
        if (lvl > _lastLevel && lvl >= CrisisLevel.Alert) _alarmSerial++;
        _lastLevel = lvl;
        if (w.Tick >= _nextNoisy) { _nextNoisy = w.Tick + SimTime.Minutes(10); ScanNoisy(); }

        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            var s = Of(c);
            if (c.Room is Room room && !c.Outside) Body(c, s, room, dt);
            if (c.IsMoving && c.CanAct && c.Path != null && w.Body.Marks.Count > 0 && (w.Tick + c.Id) % 3 == 0) Floor(c, s);
            if (w.Tick < s.Next) continue;
            s.Next = w.Tick + LookEvery + (c.Id * 7 + (int)(w.Tick / LookEvery)) % 11;
            if (!c.CanAct || !c.IsAwake || c.Outside || c.Room is not Room r2 || c.Suit != null || c.IsChild && c.Age < 5f) continue;
            Look(c, s, r2);
        }
        Chat();
        Mind();
        if (Notes.Count > 400) Notes.RemoveRange(0, Notes.Count - 300);
    }

    private void Forget(int room, Stir s) => _taken.Remove(room * 32L + (int)s);

    /// <summary>몸: 땀 · 떨림 단계 · 편해진 만큼 스트레스가 풀린다 · 일이 끝나면 담요 · 겉옷 · 손전등을 정리한다.</summary>
    private void Body(CrewMember c, ReactState s, Room room, float dt)
    {
        var w = _w;
        float t = room.Air.Temperature;
        bool exert = c.IsMoving && (c.Dashing || c.Job?.Urgent == true) || c.Pose == Pose.Working || c.Job?.Activity is JogActivity;
        if (s.Way == "jog" && w.Tick < s.GUntil) exert = true;
        int sweat = t > 33f ? 3 : t > 29f ? 2 : t > 26.5f ? 1 : 0;
        if (exert && t > 23f) sweat++;
        if (s.JacketOff && sweat > 0) sweat--;
        s.Sweat = (byte)Math.Clamp(sweat, 0, 3);
        int shiver = t < 7f ? 3 : t < 11f ? 2 : t < 15f ? 1 : 0;
        if (s.Wrapped || s.Way is "heater" or "huddle" && s.WayFor == Stir.Cold || exert) shiver--;
        s.Shiver = (byte)Math.Clamp(shiver, 0, 3);
        // 버티는 방법이 있으면 마음이 덜 쓰인다
        bool coping = s.Way != null && (s.WayFor is Stir.Heat or Stir.Cold or Stir.Dark);
        if (coping && (s.Sweat > 0 || s.Shiver > 0 || s.WayFor == Stir.Dark && _dark.Length > room.Id && _dark[room.Id]))
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.025f * dt);
        if (s.Way == "huddle" && s.LookCrew >= 0 && w.Tick - s.WaySince < SimTime.Hours(1) && Crew(s.LookCrew) is CrewMember o && o.Room == room && (o.Position - c.Position).LengthSquared() < 2.5f)
        {
            c.ChangeAffinity(o, 0.03f * dt);
            o.ChangeAffinity(c, 0.03f * dt);
        }
        // 끝: 따뜻해졌다 · 시원해졌다 · 불이 들어왔다
        if (s.Way != null && s.WayFor == Stir.Dark && _dark.Length > room.Id && !_dark[room.Id] && !(room.Dark || PortableSystem.Unlit(room))) EndWay(c, s, room, Stir.Dark);
        else if (s.Way != null && s.WayFor == Stir.Cold && t > 18.5f) EndWay(c, s, room, Stir.Cold);
        else if (s.Way != null && s.WayFor == Stir.Heat && t < 25f) EndWay(c, s, room, Stir.Heat);
        else if (s.Way != null && w.Tick - s.WaySince > SimTime.Hours(6)) { s.Way = null; s.TorchOn = false; }
        if (s.JacketOff && t < 23.5f && w.Tick - s.JacketSince > SimTime.Minutes(20) && c.CanAct && !c.IsMoving)
        {
            s.JacketOff = false;
            Stats.JacketsBack++;
            Gest(s, Gesture.ShedJacket, Short);
        }
        if (s.Wrapped && (t > 21f || w.Tick - s.WaySince > SimTime.Hours(8)) && c.CanAct) Unwrap(c, s, room);
        if (c.Outside || c.Suit != null) s.TorchOn = false;
    }

    private void EndWay(CrewMember c, ReactState s, Room room, Stir stir)
    {
        var w = _w;
        s.Way = null;
        s.WayWhy = null;
        s.TorchOn = false;
        s.Ep[(int)stir] = false;
        if (!c.CanAct || !c.IsAwake || w.Tick - s.Last[(int)Stir.Back] < SimTime.Minutes(30)) return;
        s.Last[(int)Stir.Back] = w.Tick;
        if (!R.Chance(stir == Stir.Dark ? 0.55f : 0.3f)) return;
        Stats.Back++;
        Stats.ByStir[(int)Stir.Back]++;
        var g = stir == Stir.Dark ? Gesture.Look : stir == Stir.Cold ? Gesture.RubHands : Gesture.WipeBrow;
        Gest(s, g, Short);
        if (stir == Stir.Dark) s.LookAt = room.Center + new Vector2(0f, -0.1f);
        Speak(c, s, Stir.Back, LinesBack(c, stir), room, "");
    }

    private void Unwrap(CrewMember c, ReactState s, Room room)
    {
        var w = _w;
        s.Wrapped = false;
        var b = s.Blanket >= 0 ? w.Belongings.All.FirstOrDefault(x => x.Id == s.Blanket) : null;
        s.Blanket = -1;
        if (b == null || b.Holder != c.Id) return;
        b.Holder = -1;
        // 내 방이면 침대에 개어 두고, 아니면 의자에 걸쳐 둔다 (나중에 챙긴다 — 그 밤 잠자리가 덜 포근하다)
        bool home = c.Bed?.Room == room;
        b.At = home ? null : c.Cell;
        if (!home) w.Log.Add(w.Tick, LogKind.Life, $"{room.Name}에 {Ko.EulReul(b.Name)} 걸쳐 두었다 — 몸이 풀렸다", c.Id);
    }

    // ───────────────────────────── 알아채기 ─────────────────────────────

    private bool Ready(ReactState s, Stir k, float minutes) => _w.Tick - s.Last[(int)k] >= SimTime.Minutes(minutes) || s.Last[(int)k] == 0;

    /// <summary>걸어가는 앞 칸: 물기 · 기름 · 서리 → 조심 걸음 · 유리 → 돌아간다.</summary>
    private void Floor(CrewMember c, ReactState s)
    {
        var w = _w;
        var path = c.Path!;
        var body = w.Body;
        var grid = w.Ship.Grid;
        for (int j = c.PathIndex; j < Math.Min(path.Count, c.PathIndex + 4); j++)
        {
            var cell = path[j];
            if (body.MarksAt(cell) is not CellState ms) continue;
            if (ms.V[(int)CellMark.Glass] > 0.2f && ms.V[(int)CellMark.Tape] < 0.5f)
            {
                // 몸을 틀어 옆 칸으로 돌아간다 (앞뒤 칸과 이어지는 유리 없는 칸) — 좁아서 못 돌면 살살 밟고 간다
                var side = Detour(path, j, j > c.PathIndex ? path[j - 1] : c.Cell);
                if (side is Cell sc) { path[j] = sc; s.Detours++; Stats.Detours++; }
                else if (c.Room != null && !w.Body.Cautious(c, c.Room)) { Stats.Careful++; w.Body.Careful(c, c.Room, 0.3f, "유리 조각 위를 살살 밟고 간다"); Gest(s, Gesture.Tiptoe, Long); }
                if (!Ready(s, Stir.Glass, 15f)) return;
                s.Last[(int)Stir.Glass] = w.Tick;
                Stats.ByStir[(int)Stir.Glass]++;
                Stats.Sidesteps++;
                Gest(s, Gesture.Sidestep, Short);
                s.LookAt = cell.Center;
                // 곁에 이쪽으로 오는 사람이 있으면 손짓으로 알린다
                CrewMember? warn = null;
                if (c.Room != null && _here.TryGetValue(c.Room.Id, out var ppl))
                    foreach (var o in ppl)
                        if (o != c && o.IsMoving && o.CanAct && (o.Position - cell.Center).LengthSquared() < 9f) { warn = o; break; }
                if (warn != null) { Gest(s, Gesture.Point, Short); Gest(Of(warn), Gesture.Sidestep, Short); }
                Speak(c, s, Stir.Glass, warn != null ? Pool(c, Stir.Glass, true).Select(x => $"{warn.Name}, {x}").ToArray() : Pool(c, Stir.Glass, false), c.Room, "glass");
                if (side == null) Gest(s, Gesture.Tiptoe, Long);
                return;
            }
            float slip = body.SlipAt(grid.Index(cell));
            if (slip > 0.35f && (ms.V[(int)CellMark.Wet] > 0.15f || ms.V[(int)CellMark.Oil] > 0.2f || ms.V[(int)CellMark.Frost] > 0.2f))
            {
                if (!Ready(s, Stir.Wet, 10f)) return;
                s.Last[(int)Stir.Wet] = w.Tick;
                Stats.ByStir[(int)Stir.Wet]++;
                var room = w.Ship.RoomAt(cell) ?? c.Room;
                bool reckless = Life.Has(c, Habit.Daredevil) || Life.Has(c, Habit.Hasty) && c.Traits.Calm < 0.6f || c.Job?.Urgent == true && c.Traits.Bravery > 0.6f;
                if (reckless)
                {
                    Stats.Reckless++;
                    Gest(s, Gesture.Shrug, Short);
                    Speak(c, s, Stir.Wet, Life.Has(c, Habit.Daredevil) ? new[] { "이 정도야 뭐", "미끄러지면 미끄러지는 거지", "물 좀 있다고 돌아가?" } : new[] { "바쁜데 뭐 — 그냥 간다", "조심하면 되지" }, room, "wet");
                    return;
                }
                Stats.Careful++;
                Gest(s, Gesture.Tiptoe, Long);
                s.LookAt = cell.Center;
                string what = ms.V[(int)CellMark.Oil] > 0.2f ? "기름" : ms.V[(int)CellMark.Frost] > 0.2f ? "서리" : "물기";
                if (room != null) w.Body.Careful(c, room, 0.5f, $"바닥 {what}를 보고 조심조심 걷는다");
                Speak(c, s, Stir.Wet, WetLines(c, what), room, what);
                return;
            }
        }
    }

    private Cell? Detour(List<Cell> path, int j, Cell from)
    {
        var body = _w.Body;
        var ship = _w.Ship;
        var prev = from;
        var next = j + 1 < path.Count ? path[j + 1] : path[j];
        var g = path[j];
        foreach (var d in Cell.Dirs8)
        {
            var cand = g + d;
            if (cand == prev || cand == next || !ship.IsWalkable(cand) || !ship.IsOpenFloor(cand)) continue;
            if (Math.Max(Math.Abs(cand.X - prev.X), Math.Abs(cand.Y - prev.Y)) > 1 || Math.Max(Math.Abs(cand.X - next.X), Math.Abs(cand.Y - next.Y)) > 1) continue;
            if (body.MarksAt(cand) is CellState cs && cs.V[(int)CellMark.Glass] > 0.1f) continue;
            return cand;
        }
        return null;
    }

    /// <summary>한 사람이 둘러본다: 가장 센 변화 하나에 반응한다.</summary>
    private void Look(CrewMember c, ReactState s, Room room)
    {
        var w = _w;
        bool busy = c.Job?.Urgent == true || c.Job?.Activity is PanicActivity or EvacuateActivity or TakeCoverActivity || c.Down;
        float best = 0f;
        Stir pick = Stir.Chat;
        object? arg = null;
        void Cand(Stir k, float v, object? a = null) { if (v > best) { best = v; pick = k; arg = a; } }
        _here.TryGetValue(room.Id, out var ppl);
        ppl ??= new List<CrewMember>();

        // 경보 (위기 중에도)
        if (_alarmSerial != s.LastAlarm && Crisis.Level(w) >= CrisisLevel.Alert) Cand(Stir.Alarm, 0.95f);
        bool dark = room.Dark || PortableSystem.Unlit(room);
        if (dark && !s.Ep[(int)Stir.Dark]) Cand(Stir.Dark, 0.9f);
        float t = room.Air.Temperature;
        if (t < 16f && !s.Ep[(int)Stir.Cold] && Ready(s, Stir.Cold, 40f)) Cand(Stir.Cold, 0.55f + (16f - t) * 0.03f);
        if (t > 27.5f && !s.Ep[(int)Stir.Heat] && Ready(s, Stir.Heat, 40f)) Cand(Stir.Heat, 0.5f + (t - 27.5f) * 0.03f);
        if (busy) { if (pick == Stir.Alarm) { s.LastAlarm = _alarmSerial; React(c, s, room, pick, arg, ppl, true); } return; }
        if (room.Air.Smoke > 0.06f && Ready(s, Stir.Smoke, 20f)) Cand(Stir.Smoke, 0.6f + MathF.Min(0.3f, room.Air.Smoke));
        if (room.Vibration > 0.45f && Ready(s, Stir.Shake, 45f)) Cand(Stir.Shake, 0.45f + 0.3f * room.Vibration);
        if (Ready(s, Stir.Sound, 90f) && (_noisy.TryGetValue(room.Id, out var nm) || NoisyNext(room, out nm)) && nm.Omen is { Known: false }) Cand(Stir.Sound, 0.5f + 0.2f * c.Traits.Diligence, nm);
        if (Ready(s, Stir.Smell, 60f) && w.Smells.Dominant(room, out float sv) is SmellKind sk && sv > SmellSystem.Threshold(sk) * 2.5f * SmellSystem.Nose(c)) Cand(Stir.Smell, 0.35f + MathF.Min(0.2f, sv * 0.2f), sk);
        // 방송 · 아침 브리핑
        if (w.Automation.Present)
        {
            var pa = w.Automation.Speak;
            if (pa.Recent.Count > 0 && pa.Recent[^1] is Broadcast b && b.Id != s.LastHeard && w.Tick - b.Tick < SimTime.Minutes(4) && b.HeardBy.Contains(c.Id)) Cand(Stir.Voice, 0.42f + 0.15f * b.Priority, b);
            if (!ShipMate.Off && w.Automation.Mate.LastBriefing is Briefing bf && bf.Day != s.LastBrief && w.Tick - bf.Tick < SimTime.Minutes(30) && bf.Heard.Contains(c.Id)) Cand(Stir.Voice, 0.5f, bf);
        }
        // 새로 생긴 소품
        if (Ready(s, Stir.Novel, 30f))
            foreach (var p in w.Props.Placed)
                if (p.RoomId == room.Id && p.Maker != c.Id && w.Tick - p.Placed < SimTime.Hours(72) && w.Tick - p.Placed > SimTime.Minutes(5) && !s.SeenProps.Contains(p.Id)) { s.SeenProps.Add(p.Id); Cand(Stir.Novel, 0.4f + (w.Props.Likes(c, p) ? 0.15f : 0f), p); break; }
        // 곁의 사람: 우는 사람 · 이상한 몸짓
        foreach (var o in ppl)
        {
            if (o == c || !o.CanAct) continue;
            if ((o.Position - c.Position).LengthSquared() > 36f) continue;
            if (Crying(o) && Ready(s, Stir.Cry, 90f)) Cand(Stir.Cry, 0.45f + 0.4f * MathF.Max(0f, c.AffinityTo(o)) + 0.2f * c.Traits.Sociability, o);
            else if (OddLook(o) is string what && Ready(s, Stir.Odd, 45f)) Cand(Stir.Odd, 0.3f + 0.25f * c.Traits.Sociability + (Life.Has(c, Habit.Gazer) || Life.Has(c, Habit.Talker) ? 0.15f : 0f), (o, what));
        }
        if (Crying(c) && s.G != Gesture.Cry) Gest(s, Gesture.Cry, Long);
        if (best <= 0f) return;
        if (pick == Stir.Alarm) s.LastAlarm = _alarmSerial;
        React(c, s, room, pick, arg, ppl, false);
    }

    /// <summary>우는 사람: 슬픔이 크고 깨어 있고 일하는 중이 아니다.</summary>
    public bool Crying(CrewMember o) => BrainSystem.Enabled && o.IsAwake && !o.IsMoving && o.Pose != Pose.Working && _w.Brain2.Emotions.Get(o, Feeling.Sadness) > 0.62f;

    /// <summary>남의 눈에 띄는 몸짓 (무엇을 하는 것처럼 보이나).</summary>
    private string? OddLook(CrewMember o)
    {
        var w = _w;
        if (_st.TryGetValue(o.Id, out var os))
        {
            if (w.Tick < os.OddUntil && os.OddWhat != null) return os.OddWhat;
            if (w.Tick < os.GUntil)
                switch (os.G)
                {
                    case Gesture.Jog: return "제자리에서 뛴다";
                    case Gesture.FeelWall: return "벽을 더듬는다";
                    case Gesture.Huddle: return "누구한테 바짝 붙어 있다";
                    case Gesture.Brace: return "벽을 붙잡고 있다";
                    case Gesture.CoverEars: return "귀를 막고 있다";
                }
        }
        if (o.Pose == Pose.Sleeping && o.IsMoving) return "자면서 걷는다";
        foreach (var sc in w.Scenes.Scenes)
            if (sc.Open && sc.Kind == SceneKind.Sleepwalk && sc.Host == o.Id) return "자면서 걷는다";
        return null;
    }

    /// <summary>방마다 낯선 소리를 내는 설비 (아무도 모르는 전조 — 덜컹 · 쉭 · 타닥 · 삐).</summary>
    private void ScanNoisy()
    {
        _noisy.Clear();
        foreach (var m in _w.Ship.Machines)
            if (m.Omen is { Known: false } && m.Body.Room is Room r && !_noisy.ContainsKey(r.Id)) _noisy[r.Id] = m;
    }

    /// <summary>옆방 소리 (문이 열려 있으면 들린다).</summary>
    private bool NoisyNext(Room room, out Machine m)
    {
        m = null!;
        if (_noisy.Count == 0) return false;
        foreach (var (nb, door) in _w.Ambience.Neighbors(room))
            if (door && _noisy.TryGetValue(nb.Id, out var x)) { m = x; return true; }
        return false;
    }

    private CrewMember? Crew(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }

    internal void Gest(ReactState s, Gesture g, int ticks)
    {
        s.G = g;
        s.GSince = _w.Tick;
        s.GUntil = _w.Tick + ticks;
        if (g is not (Gesture.Look or Gesture.Stare or Gesture.Listen or Gesture.Admire or Gesture.Sidestep or Gesture.Tiptoe or Gesture.Point or Gesture.Comfort or Gesture.Talk or Gesture.Window or Gesture.Sniff)) { s.LookAt = null; s.LookCrew = -1; }
    }

    // ───────────────────────────── 시험 · 지문 ─────────────────────────────

    public IEnumerable<ReactNote> NotesOf(Stir k) => Notes.Where(n => n.Stir == k);

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Stats.Lines); I(Stats.Acts); I(Stats.ActsDone); I(Stats.Careful); I(Stats.Checks); I(Stats.Found); I(Stats.Talks); I(Stats.Chats); I(_alarmSerial);
        foreach (var c in _w.Crew)
            if (_st.TryGetValue(c.Id, out var s)) { I(s.Next); I((int)s.G); I(s.JacketOff ? 1 : 0); I(s.Wrapped ? 1 : 0); I(s.TorchOn ? 1 : 0); I(s.Way?.Length ?? -1); }
    }
}
