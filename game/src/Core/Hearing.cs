using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.2 소리를 시뮬레이션 정보로: 어디서 · 무엇이 · 얼마나 크게 · 어떤 간격으로 나는가.
//  · 소리 나는 것: 재질별 발소리(격자 쩔걱 · 타일 또각 · 카펫 사박 · 고무 · 금속판) · 느슨한 패널 떨림 · 낡은 베어링의 주기 잡음 ·
//    새는 쉭 소리 · 배선 타닥 · 환기 팬 · 물방울 · 우주복 호흡 · 무전 · 기온이 바뀔 때 선체 삐걱 · 문 닫히는 쿵 · 흥얼거림 · 휘파람 · 톡톡 · 말소리 · 노래.
//  · 퍼짐: 방 연결망을 따라 — 열린 문은 잘 넘고, 닫힌 문은 먹먹하게(대개 못 듣는다), 벽은 층(칸막이 · 패널 · 단열재 · 외판)만큼 막는다.
//    팬 소리가 작은 소리를 가린다 — 정전으로 팬이 멎으면 그제야 물방울 · 삐걱이 들린다.
//  · 사람의 귀: 같은 소리 정보로 듣는다 (잠들었으면 · 헬멧을 썼으면 · 귀를 막았으면 덜). 들은 것은 믿음 · 반응이 된다:
//    낯선 기계 소리(전조) → 쳐다보고 가서 귀를 대 본다(v17.8 반응) · 팬이 멎음 → 정전이다 · 물방울 → 어디 새나 ·
//    옆방 발소리 → 아는 사람이면 누군지 안다 · 늘 나던 소리는 익숙해지고, 거슬리던 소리가 수리로 사라지면 알아챈다.
//  · 주 컴퓨터: 데이터선이 닿는 방의 진동 마이크로 같은 소리를 듣는다 — 오래가는 주기 잡음을 정비 권고로 올린다.
// 소리는 1분마다 다시 모은다(가볍게). 화면은 이 정보를 소리 표시 그림으로 그린다 (View/ShipViewHearing).

public enum Noise : byte { Step, Rattle, Bearing, Hiss, Crackle, Fan, Drip, Breath, Radio, Creak, DoorShut, Hum, Whistle, Tap, Voice, Sing }

/// <summary>소리 하나: 종류 · 방 · 자리 · 크기(0~1, 그 방 안) · 주인(사람 · 설비 · 벽 · 문 번호) · 재질/세부 · 주기(초, 0 = 고른 소리).</summary>
public sealed class NoiseSource
{
    public Noise Kind { get; init; }
    public int Room { get; init; }
    public Vector2 At { get; init; }
    public float Level { get; init; }
    public int Owner { get; init; } = -1;
    public byte Sub { get; init; }
    public float Period { get; init; }
    public long Key => ((long)Kind << 40) | ((long)(Owner + 1) << 8) | Sub;
    public bool Steady => Kind is Noise.Rattle or Noise.Bearing or Noise.Hiss or Noise.Crackle or Noise.Fan or Noise.Drip;
}

/// <summary>한 방에서 들리는 소리: 어느 소리 · 크기 · 문/벽 너머라 먹먹한가 · 팬 소리에 가렸나.</summary>
public readonly record struct HeardNoise(int Src, float Level, bool Muffled, bool Masked);

public sealed class HearNote
{
    public long Tick { get; init; }
    public int Crew { get; init; }
    public Noise Kind { get; init; }
    public int Room { get; init; }
    public string What { get; init; } = "";
    public string Line { get; init; } = "";
}

public sealed class HearingStats
{
    public int Minutes, Sources, Heard, Muffled, Masked, Strange, Quieted, FanStops, Drips, StepsKnown, Creaks, Radio, Annoyed, Habituated, Advice, Doors;
    public readonly int[] ByKind = new int[16];
    public readonly int[] HeardKind = new int[16];
    public string Summary() =>
        $"소리 {Sources}(종류 {ByKind.Count(x => x > 0)}) · 들음 {Heard}(종류 {HeardKind.Count(x => x > 0)} · 먹먹 {Muffled} · 가려짐 {Masked}) · 낯선 소리 {Strange} · 조용해짐 {Quieted} · 팬 멎음 {FanStops} · " +
        $"물방울 {Drips} · 발소리로 앎 {StepsKnown} · 삐걱 {Creaks} · 무전 {Radio} · 거슬림 {Annoyed} · 익숙해짐 {Habituated} · 컴퓨터 권고 {Advice} · 문 닫힘 {Doors}";
}

/// <summary>한 사람의 귀: 익숙해진 소리 · 거슬리는 정도 · 최근에 알아챈 것.</summary>
public sealed class EarState
{
    public int Id { get; init; }
    /// <summary>이어지는 소리를 들은 분 (소리 열쇠 → 분).</summary>
    internal readonly Dictionary<long, int> Minutes = new();
    internal readonly Dictionary<long, long> Noticed = new();
    public float Annoy { get; internal set; }
    internal long NextStep, NextLine;
    /// <summary>지금 무엇을 듣고 있나 (화면 · 시험): 종류 · 소리 번호.</summary>
    public Noise? Last { get; internal set; }
    public long LastAt { get; internal set; } = -1;
}

public sealed partial class HearingSystem
{
    public static bool Off { get; set; } = Environment.GetEnvironmentVariable("SHIPSIM_HEARING_OFF") != null;
    /// <summary>들리는 문턱 (그 방 안 크기).</summary>
    public const float Audible = 0.1f;
    /// <summary>며칠 듣고 나면 익숙해진다 (분).</summary>
    public const int HabitMinutes = 60 * 40;
    public static readonly int Every = SimTime.Minutes(1);

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7451 + 293));
    public List<NoiseSource> Sources { get; } = new();
    public HearingStats Stats { get; } = new();
    public List<HearNote> Notes { get; } = new();
    private readonly List<NoiseSource> _emitted = new();
    private List<HeardNoise>[] _room = Array.Empty<List<HeardNoise>>();
    private float[] _fan = Array.Empty<float>();
    private float[] _tempAvg = Array.Empty<float>();
    private bool[] _hullRoom = Array.Empty<bool>();
    private float[] _doorOpen = Array.Empty<float>();
    private readonly Dictionary<int, EarState> _ears = new();
    // 방 연결망: 방 → (이웃 방, 문 번호들, 벽 통과율)
    private List<(int other, List<Door> doors, float wall)>[] _nb = Array.Empty<List<(int, List<Door>, float)>>();
    private int _nbRooms = -1;
    private long _nbAt = -1, _next;
    // 이어지는 소리: 열쇠 → (종류 · 방 · 주인 · 처음 · 마지막 · 들어 온 사람)
    private sealed class Steady { public Noise Kind; public int Room, Owner; public long Since, Seen; public readonly List<int> Heard = new(); public bool Advised; }
    private readonly Dictionary<long, Steady> _steady = new();
    private readonly List<long> _gone = new();

    public HearingSystem(World w) => _w = w;

    public EarState Ear(CrewMember c)
    {
        if (!_ears.TryGetValue(c.Id, out var e)) _ears[c.Id] = e = new EarState { Id = c.Id };
        return e;
    }
    public EarState? PeekEar(CrewMember c) => _ears.TryGetValue(c.Id, out var e) ? e : null;

    /// <summary>다른 시스템이 짧은 소리를 낸다 (흥얼거림 · 휘파람 · 톡톡 · 노래): 다음 1분 모음에 들어간다.</summary>
    public void Emit(Noise k, CrewMember c, float level, byte sub = 0)
    {
        if (Off || c.Room is not Room r) return;
        _emitted.Add(new NoiseSource { Kind = k, Room = r.Id, At = c.Position, Level = level, Owner = c.Id, Sub = sub });
    }

    /// <summary>이 방에서 지금 들리는 소리 (지난 1분).</summary>
    public IReadOnlyList<HeardNoise> In(Room r) => r.Id < _room.Length ? _room[r.Id] : Array.Empty<HeardNoise>();

    /// <summary>방 안의 팬 소리 크기 (작은 소리를 가린다).</summary>
    public float FanIn(Room r) => r.Id < _fan.Length ? _fan[r.Id] : 0f;

    /// <summary>이웃한 두 방 사이로 소리가 넘는 정도 (문이 열렸으면 크게, 닫혔으면 먹먹하게, 벽은 층만큼) · 먹먹한가.</summary>
    public float Pass(Room from, Room to) => Pass(from.Id, to.Id, out _);

    private float Pass(int a, int b, out bool muffled)
    {
        muffled = true;
        Rebuild();
        if (a >= _nb.Length) return 0f;
        foreach (var (o, doors, wall) in _nb[a])
        {
            if (o != b) continue;
            float best = wall;
            foreach (var d in doors)
            {
                if (d.Removed) { if (0.8f > best) { best = 0.8f; muffled = false; } continue; }
                float p = d.Openness >= 0.5f ? 0.55f : d.Welded || d.Bulkhead ? 0.04f : 0.1f;
                if (p > best) { best = p; muffled = d.Openness < 0.5f; }
            }
            return best;
        }
        return 0f;
    }

    /// <summary>사람의 귀 (잠 · 헬멧 · 귀 막기 · 나이).</summary>
    public float Sense(CrewMember c, Noise k)
    {
        if (c.Dead || c.Away || c.Down) return 0f;
        float s = 1f;
        if (c.Pose == Pose.Sleeping) s *= c.DeepAsleep ? 0.25f : 0.5f;
        if (c.Suit != null && k is not (Noise.Radio or Noise.Breath)) s *= 0.35f;
        if (c.Suit == null && k == Noise.Radio && c.Room?.Type is not (RoomType.Bridge or RoomType.Comms)) s *= 0.5f;
        if (c.Age >= 60f) s *= 0.8f;
        if (_w.React.GestureOf(c) == Gesture.CoverEars) s *= 0.2f;
        return s;
    }

    /// <summary>이 사람이 지금 그 사람의 소리(흥얼거림 · 말)를 듣는가.</summary>
    public float LevelOf(CrewMember listener, Noise k, int owner)
    {
        if (listener.Room is not Room r || r.Id >= _room.Length) return 0f;
        float best = 0f;
        foreach (var h in _room[r.Id])
        {
            var s = Sources[h.Src];
            if (s.Kind == k && s.Owner == owner && !h.Masked && h.Level > best) best = h.Level;
        }
        return best * Sense(listener, k);
    }

    /// <summary>이 사람이 듣는 낯선 기계 소리 (전조가 붙었는데 아직 아무도 모르는 설비) — v17.8 반응이 쳐다보고 확인하러 간다.</summary>
    public bool Strange(CrewMember c, out Machine m)
    {
        m = null!;
        if (Off || c.Room is not Room r || r.Id >= _room.Length) return false;
        float best = 0f;
        foreach (var h in _room[r.Id])
        {
            var s = Sources[h.Src];
            if (s.Kind is not (Noise.Bearing or Noise.Rattle or Noise.Hiss or Noise.Crackle) || h.Masked) continue;
            float lv = h.Level * Sense(c, s.Kind) * (h.Muffled ? 0.6f : 1f);
            if (lv < Audible || lv <= best) continue;
            if (MachineOf(s.Owner) is not Machine x || x.Omen is not { Known: false }) continue;
            if (Ear(c).Minutes.TryGetValue(s.Key, out var mins) && mins >= HabitMinutes) continue;
            best = lv; m = x;
        }
        if (m != null) Stats.Strange++;
        return m != null;
    }

    private Machine? MachineOf(int furnitureId)
    {
        foreach (var m in _machines) if (m.Body.Id == furnitureId) return m;
        return null;
    }
    private readonly List<Machine> _machines = new();

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off || _w.Tick < _next) return;
        _next = _w.Tick + Every;
        Stats.Minutes++;
        Collect();
        Propagate();
        Listen();
        Vanished();
        Computer();
    }

    private void Rebuild()
    {
        var ship = _w.Ship;
        int n = ship.Rooms.Count;
        if (_nbRooms == n && _w.Tick - _nbAt < SimTime.Hours(6)) return;
        _nbRooms = n; _nbAt = _w.Tick;
        _nb = new List<(int, List<Door>, float)>[n];
        for (int i = 0; i < n; i++) _nb[i] = new();
        _hullRoom = new bool[n];
        var g = ship.Grid;
        var walls = new Dictionary<long, (float sum, int count)>();
        var near = new List<int>(4);
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) != TileKind.Wall) continue;
            near.Clear();
            foreach (var d in Cell.Dirs4)
            {
                int id = g.RoomId(c + d);
                if (id >= 0 && id < n && !near.Contains(id)) near.Add(id);
            }
            var wb = _w.Body.WallAt(c);
            if (wb is { Hull: true }) foreach (var id in near) _hullRoom[id] = true;
            if (near.Count < 2) continue;
            float pass = 0.4f * (1f - (wb?.SoundBlock ?? 0.8f));
            for (int a = 0; a < near.Count; a++)
                for (int b = a + 1; b < near.Count; b++)
                {
                    long key = near[a] < near[b] ? ((long)near[a] << 20) | (uint)near[b] : ((long)near[b] << 20) | (uint)near[a];
                    walls[key] = walls.TryGetValue(key, out var x) ? (x.sum + pass, x.count + 1) : (pass, 1);
                }
        }
        var doors = new Dictionary<long, List<Door>>();
        foreach (var d in ship.Doors)
        {
            if (d.RoomA is not Room ra || d.RoomB is not Room rb || ra.Id >= n || rb.Id >= n || ra == rb) continue;
            long key = ra.Id < rb.Id ? ((long)ra.Id << 20) | (uint)rb.Id : ((long)rb.Id << 20) | (uint)ra.Id;
            if (!doors.TryGetValue(key, out var l)) doors[key] = l = new List<Door>();
            l.Add(d);
        }
        foreach (var key in walls.Keys.Concat(doors.Keys).Distinct().OrderBy(k => k))
        {
            int a = (int)(key >> 20), b = (int)(key & 0xfffff);
            float wall = walls.TryGetValue(key, out var x) ? x.sum / x.count : 0f;
            var dl = doors.TryGetValue(key, out var l) ? l : new List<Door>();
            _nb[a].Add((b, dl, wall));
            _nb[b].Add((a, dl, wall));
        }
    }

    /// <summary>1) 지금 나는 소리를 모은다.</summary>
    private void Collect()
    {
        var w = _w;
        var ship = w.Ship;
        long now = w.Tick;
        Rebuild();
        Sources.Clear();
        int n = ship.Rooms.Count;
        if (_room.Length != n)
        {
            _room = new List<HeardNoise>[n];
            for (int i = 0; i < n; i++) _room[i] = new List<HeardNoise>();
            _fan = new float[n];
            _tempAvg = new float[n];
            for (int i = 0; i < n; i++) _tempAvg[i] = ship.Rooms[i].Air.Temperature;
        }
        // 설비: 도는 동안만 소리가 난다 (멈추면 · 정전이면 조용하다)
        _machines.Clear();
        _machines.AddRange(ship.Machines);
        foreach (var m in _machines)
        {
            if (m.Body.Room is not Room r || r.Detached || !m.Powered || !m.Active || m.Parked) continue;
            var at = m.Body.Center;
            float per = 0.8f + (m.Body.Id * 37 % 23) / 10f; // 설비마다 다른 간격 (초)
            if (m.Omen is Omen o)
            {
                float sev = Math.Clamp((now - o.Since) / (float)Math.Max(1, o.Due - o.Since), 0f, 1f);
                var k = o.Kind switch
                {
                    OmenKind.Vibration => o.Cause == OmenCause.LooseMount ? Noise.Rattle : Noise.Bearing,
                    OmenKind.Pressure => Noise.Hiss,
                    OmenKind.Heat => Noise.Crackle,
                    _ => (Noise?)null,
                };
                if (k is Noise nk) Add(nk, r, at, 0.5f + 0.35f * sev, m.Body.Id, (byte)o.Cause, nk is Noise.Bearing or Noise.Rattle ? per : 0f);
            }
            else if (m.Faults.Any(f => f.Kind == FaultKind.BearingWear)) Add(Noise.Bearing, r, at, 0.75f, m.Body.Id, 1, per * 0.7f);
            else if (WornBearing(m) is float age) Add(Noise.Bearing, r, at, 0.3f + 0.4f * Math.Clamp((age - 0.85f) / 0.3f, 0f, 1f), m.Body.Id, 2, per * 1.3f);
        }
        // 방: 환기 팬 · 물방울 · 선체 삐걱 · 흔들리는 방의 패널
        for (int i = 0; i < n; i++)
        {
            var r = ship.Rooms[i];
            if (r.Detached || r.Cells.Count == 0) continue;
            var c = r.Center;
            if (r.Powered && r.DuctLinked && r.AirFlow > 0.2f && !r.BreakerOff)
                Add(Noise.Fan, r, c, r.Type == RoomType.HvacRoom ? 0.5f : 0.22f, -1 - i, 0, 0f);
            if (r.Flood > 0.5f || r.Humidity > 0.85f || r.Air.Leak > 0.3f && r.Humidity > 0.6f)
                Add(Noise.Drip, r, c + new Vector2(((i * 7) % 5 - 2) * 0.4f, ((i * 3) % 5 - 2) * 0.4f), r.Flood > 20f ? 0.16f : 0.12f, -1 - i, 0, 1.5f);
            float t = r.Air.Temperature;
            float avg = _tempAvg[i];
            _tempAvg[i] = avg + (t - avg) * 0.02f; // 한 시간쯤 늦게 따라간다
            if (i < _hullRoom.Length && _hullRoom[i] && MathF.Abs(t - avg) > 2.5f && R.Chance(0.12f))
            {
                Add(Noise.Creak, r, HullSpot(r), 0.3f, -1 - i, (byte)(t < avg ? 1 : 0), 0f);
            }
            if (r.Vibration >= 0.3f && LoosePanel(r) is Vector2 pv) Add(Noise.Rattle, r, pv, 0.25f + 0.3f * r.Vibration, -1000 - i, 9, 0.5f);
        }
        // 문이 닫힌다 (지난 1분 사이)
        if (_doorOpen.Length != ship.Doors.Count) { _doorOpen = new float[ship.Doors.Count]; for (int i = 0; i < _doorOpen.Length; i++) _doorOpen[i] = ship.Doors[i].Openness; }
        for (int i = 0; i < ship.Doors.Count; i++)
        {
            var d = ship.Doors[i];
            if (_doorOpen[i] >= 0.5f && d.Openness < 0.2f && (d.RoomA ?? d.RoomB) is Room dr)
            {
                Add(Noise.DoorShut, dr, d.Cell.Center, d.Bulkhead ? 0.55f : 0.3f, d.Id, (byte)(d.Bulkhead ? 1 : 0), 0f);
                Stats.Doors++;
            }
            _doorOpen[i] = d.Openness;
        }
        // 사람: 발소리(바닥 재질) · 우주복 호흡 · 무전 · 말소리
        Room? radioRoom = null;
        foreach (var r in ship.Rooms) if (!r.Detached && r.Type is RoomType.Comms or RoomType.Bridge) { radioRoom = r; if (r.Type == RoomType.Comms) break; }
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            bool speaking = c.SaidUntil - now > SimTime.Minutes(3);
            if (c.Outside)
            {
                if (speaking && radioRoom != null) { Add(Noise.Radio, radioRoom, radioRoom.Center, 0.35f, c.Id, 0, 0f); Stats.Radio++; }
                continue;
            }
            if (c.Room is not Room cr) continue;
            if (c.IsMoving)
            {
                var mat = w.Body.FloorAt(c.Cell);
                float loud = Materials.Of(mat).Loud;
                float k = c.Job?.Urgent == true || c.Dashing ? 0.75f : c.Gait.Quiet(w) ? 0.2f : 0.45f;
                if (c.Suit != null) k *= 1.2f;
                Add(Noise.Step, cr, c.Position, loud * k, c.Id, (byte)mat, 0f);
            }
            if (c.Suit != null) Add(Noise.Breath, cr, c.Position, c.Needs.Stress > 0.6f || c.IsMoving ? 0.18f : 0.12f, c.Id, (byte)(c.Needs.Stress > 0.6f ? 1 : 0), c.Needs.Stress > 0.6f ? 1.4f : 3f);
            if (speaking) Add(c.Suit != null ? Noise.Radio : Noise.Voice, cr, c.Position, c.Job?.Urgent == true ? 0.6f : 0.3f, c.Id, 0, 0f);
        }
        foreach (var e in _emitted) { Sources.Add(e); Stats.ByKind[(int)e.Kind]++; }
        _emitted.Clear();
        Stats.Sources += Sources.Count;
        // 이어지는 소리 장부
        foreach (var s in Sources)
        {
            if (!s.Steady) continue;
            if (!_steady.TryGetValue(s.Key, out var st)) _steady[s.Key] = st = new Steady { Kind = s.Kind, Room = s.Room, Owner = s.Owner, Since = now };
            st.Seen = now;
        }
    }

    private void Add(Noise k, Room r, Vector2 at, float level, int owner, byte sub, float period)
    {
        Sources.Add(new NoiseSource { Kind = k, Room = r.Id, At = at, Level = Math.Clamp(level, 0f, 1f), Owner = owner, Sub = sub, Period = period });
        Stats.ByKind[(int)k]++;
    }

    /// <summary>낡은 베어링 (부품 수명의 85% 넘음).</summary>
    private float? WornBearing(Machine m)
    {
        foreach (var p in _w.Parts.Of(m))
            if (p.Kind == ItemKind.Bearing && p.Age > 0.85f) return p.Age;
        return null;
    }

    private Vector2? LoosePanel(Room r)
    {
        foreach (var wb in _w.Body.WallList)
            if (wb.Room == r.Id && (wb.PanelOff || wb.PanelForgot)) return wb.Cell.Center;
        return null;
    }

    private Vector2 HullSpot(Room r)
    {
        foreach (var wb in _w.Body.WallList)
            if (wb.Hull && wb.Room == r.Id) return wb.Cell.Center;
        return r.Center;
    }

    /// <summary>2) 방 연결망을 따라 퍼뜨린다 (두 칸까지, 문턱 아래면 멈춘다) · 팬 소리가 작은 소리를 가린다.</summary>
    private void Propagate()
    {
        int n = _room.Length;
        for (int i = 0; i < n; i++) { _room[i].Clear(); _fan[i] = 0f; }
        for (int si = 0; si < Sources.Count; si++)
        {
            var s = Sources[si];
            if (s.Room < 0 || s.Room >= n) continue;
            _room[s.Room].Add(new HeardNoise(si, s.Level, false, false));
            if (s.Kind == Noise.Fan) { _fan[s.Room] = MathF.Max(_fan[s.Room], s.Level); continue; } // 팬은 제 방의 바탕 소리
            if (s.Kind == Noise.Breath || s.Room >= _nb.Length) continue; // 헬멧 속 숨소리는 곁에서만
            foreach (var (o, _, _) in _nb[s.Room])
            {
                float p1 = Pass(s.Room, o, out bool m1);
                float l1 = s.Level * p1;
                if (l1 < Audible * 0.5f) continue;
                Merge(o, si, l1, m1);
                if (o >= _nb.Length) continue;
                foreach (var (o2, _, _) in _nb[o])
                {
                    if (o2 == s.Room) continue;
                    float l2 = l1 * Pass(o, o2, out bool m2);
                    if (l2 >= Audible * 0.5f) Merge(o2, si, l2, m1 || m2);
                }
            }
        }
        // 가리기: 팬이 도는 방에선 작은 소리(물방울 · 삐걱 · 톡톡 · 숨소리)가 묻힌다
        for (int i = 0; i < n; i++)
        {
            float f = _fan[i];
            if (f <= 0f) continue;
            var l = _room[i];
            for (int j = 0; j < l.Count; j++)
            {
                var h = l[j];
                var k = Sources[h.Src].Kind;
                if (k is Noise.Drip or Noise.Creak or Noise.Tap or Noise.Breath && h.Level < f * 0.9f + 0.02f) { l[j] = h with { Masked = true }; Stats.Masked++; }
            }
        }
    }

    private void Merge(int room, int si, float level, bool muffled)
    {
        var l = _room[room];
        for (int j = 0; j < l.Count; j++)
            if (l[j].Src == si)
            {
                if (level > l[j].Level) l[j] = new HeardNoise(si, level, muffled, false);
                return;
            }
        l.Add(new HeardNoise(si, level, muffled, false));
    }
}
