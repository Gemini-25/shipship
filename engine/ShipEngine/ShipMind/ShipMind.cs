using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>주컴퓨터가 알아챈 일의 종류.</summary>
public enum IncidentKind : byte { None, Fire, Leak, Vacuum, CO, BadAir, PersonDown, DeviceBroken, ElectricFault, PowerShort, ReactorScram, Hot, Strain }

/// <summary>주컴퓨터가 붙잡고 있는 사건 하나.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Incident
{
    public IncidentKind Kind;
    public byte Active;      // 1: 아직
    public byte Sealed;      // 1: 봉쇄했다
    public byte Clear;       // 연속으로 멀쩡하게 읽힌 횟수
    public int Room;
    public int Cell;
    public int Subject;      // 사람(몸) · 장치 · 급전선
    public long Since, LastAlarm, Ended;
    public float Level;
    public int Cause;
    public int RobotA, RobotB;   // 맡긴 로봇 (-1)
}

/// <summary>주컴퓨터가 돌려 본 미래 하나 (안 하나의 결과).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Imagined
{
    public long Tick;          // 상상한 때
    public int Room;
    public byte Option;        // 0: 기다린다 · 1: 지금 봉쇄
    public byte Chosen;        // 1: 이 안을 골랐다
    public int Dead, Down;     // 그 미래에서 죽은 · 쓰러진 사람
    public float LostKPa;      // 다른 방들이 잃은 기압 합
    public float Score;        // 낮을수록 좋다
}

/// <summary>함장 방침 — 주컴퓨터가 따른다 (함장이 바꿀 수 있다).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ShipPolicy
{
    public byte AutoSeal;          // 1: 위험한 방을 봉쇄한다
    public float SealWaitMin;      // 사람이 안에 있으면 봉쇄 전에 기다리는 시간 (분) — 0이면 곧바로 (사람이 갇힐 수 있다)
    public byte ShedLoads;         // 1: 전력이 모자라면 덜 중요한 장치를 끈다
    public byte ResetBreakers;     // 1: 합선 없이 떨어진 차단기를 다시 올린다
    public static ShipPolicy Default => new() { AutoSeal = 1, SealWaitMin = 1.5f, ShedLoads = 1, ResetBreakers = 1 };
}

/// <summary>
/// ⑩ 주컴퓨터 (docs/ENGINE.md §4 ⑩). 센서로만 안다 — 전원이 들어온 방의 공기 센서(기압 · 산소 · 이산화탄소 · 일산화탄소 · 그을음 · 온도) ·
/// 망의 장치 상태 · 차단기 · 사람 표(위치 · 의식). 자기 몸(주컴퓨터 장치)이 멈추면 아무것도 모르고 아무것도 못 한다.
/// <para>하는 일: 경보와 방송(③ 소리로 — 들은 사람은 믿음을 얻는다 · 잠든 사람이 깬다) · 로봇에게 일 맡기기(가장 가까운 로봇 · 로봇이 먼저) ·
/// 위험한 방 봉쇄(문 닫기 · 함장 방침의 기다림) · 연기 방 통풍구 닫기 · 전력 모자라면 덜 중요한 장치 끄기 · 보조 발전기 · 차단기 다시 올리기.</para>
/// </summary>
public sealed class ShipMind : IEngine
{
    public string Name => "주컴퓨터";
    public Phase Phase => Phase.Sense;
    public int Every => 1;
    public int Offset => 0;

    public const int ReadEvery = 5;            // 센서 읽기 (12초)
    public const float AlarmDb = 85;

    private readonly Field F;
    private readonly Layout L;
    private readonly Tables T;
    private Mind? _mind;
    private Network? _net;
    private Structure? _st;
    private Life? _life;
    private Chemistry? _chem;

    public ShipPolicy Policy = ShipPolicy.Default;
    public List<Incident> Incidents = new();
    public readonly List<(long tick, string text)> Log = new();
    public bool Online;
    public float[] RoomKPa, RoomKPaPrev, RoomO2, RoomSoot, RoomSootPrev, RoomCO, RoomCO2, RoomTemp;
    public int[] RoomHot;                       // 방에서 가장 뜨거운 칸
    public float[] RoomMoles;                   // [방 × 6]: 지난 1분(읽기 6번)의 기체량 — 새는지는 기체량이 줄어드는 것으로 (온도 · 문 여닫이로 기압이 흔들리는 것과 가른다)
    private int _reads;
    public byte[] RoomSeen;                     // 1: 이번에 센서가 읽혔다 (전원)
    public byte[] Shed;                         // 장치마다 1: 전력 때문에 꺼 둠
    public long[] BreakerResetAt;               // 급전선마다 마지막으로 다시 올린 틱
    public long Alarms, Assignments, Seals, Sheds, Resets;
    private int _computer = -2;

    /// <summary>복제 세계 안의 주컴퓨터 (상상 속에서는 다시 상상하지 않는다).</summary>
    public bool Imagining;
    public const int ImagineTicks = 125;       // 5분 앞 (짧으면 '아직 쓰러지기만 한' 미래가 더 좋아 보인다)
    /// <summary>마지막으로 상상한 것 (화면의 '주컴퓨터의 상상' 겹침 · 연출).</summary>
    public readonly List<Imagined> Imaginations = new();
    private readonly int[][] _roomDoors;
    private readonly int[] _roomVent;

    public ShipMind(Field f)
    {
        F = f; L = f.L; T = f.T;
        int nr = L.Rooms.Count;
        RoomKPa = new float[nr]; RoomKPaPrev = new float[nr]; RoomO2 = new float[nr]; RoomSoot = new float[nr]; RoomSootPrev = new float[nr]; RoomCO = new float[nr]; RoomCO2 = new float[nr]; RoomTemp = new float[nr];
        RoomHot = new int[nr]; RoomSeen = new byte[nr]; RoomMoles = new float[nr * 6];
        _roomDoors = new int[nr][];
        _roomVent = new int[nr];
        Span<int> nb = stackalloc int[4];
        foreach (var r in L.Rooms)
        {
            var doors = new SortedSet<int>();
            foreach (int c in r.Cells) { L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Door) doors.Add(L.DoorAt[j]); }
            _roomDoors[r.Id] = doors.ToArray();
            if (r.Cells.Length == 0) { _roomVent[r.Id] = -1; continue; }
            double cx = r.Cells.Average(c => (double)L.X(c)), cy = r.Cells.Average(c => (double)L.Y(c));
            _roomVent[r.Id] = r.Cells.OrderBy(c => (L.X(c) - cx) * (L.X(c) - cx) + (L.Y(c) - cy) * (L.Y(c) - cy)).ThenBy(c => c).First();
        }
        Shed = Array.Empty<byte>();
        BreakerResetAt = Array.Empty<long>();
    }

    private void Bind(Sim sim)
    {
        _mind ??= sim.Find<Mind>(); _net ??= sim.Find<Network>(); _st ??= sim.Find<Structure>(); _life ??= sim.Find<Life>(); _chem ??= sim.Find<Chemistry>();
        if (_net != null && Shed.Length != _net.Devices.Count) Shed = new byte[_net.Devices.Count];
        if (_net != null && BreakerResetAt.Length != _net.Feeders.Count) BreakerResetAt = Enumerable.Repeat(long.MinValue / 2, _net.Feeders.Count).ToArray();
        if (_computer == -2) _computer = _net?.Devices.FirstOrDefault(d => d.Type == "MainComputer")?.Id ?? -1;
    }

    public string RoomName(int r) => r >= 0 && r < L.Rooms.Count ? L.Rooms[r].Name : "배";

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Step(Sim sim, double dt)
    {
        Bind(sim);
        bool was = Online;
        Online = _net != null && _computer >= 0 && _net.Working(_computer);
        if (was && !Online) { Say(sim, "주컴퓨터가 멈췄다"); sim.Facts.Emit(sim.Tick, FactKind.ComputerDown, _computer >= 0 ? _net!.Devices[_computer].Cell : -1); }
        if (!Online) return;
        if (sim.Tick % ReadEvery == 0) { ReadSensors(); Detect(sim); }
        Act(sim);
        Power(sim);
    }

    private void Say(Sim sim, string text)
    {
        Log.Add((sim.Tick, text));
        if (Log.Count > 300) Log.RemoveRange(0, Log.Count - 300);
    }

    // ───────────────────────── 센서 ─────────────────────────

    private void ReadSensors()
    {
        _reads++;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            RoomKPaPrev[r] = RoomKPa[r]; RoomSootPrev[r] = RoomSoot[r];
            var cells = L.Rooms[r].Cells;
            if (cells.Length == 0 || _net == null || !_net.RoomPowered(r)) { RoomSeen[r] = 0; continue; }
            RoomSeen[r] = 1;
            double p = 0, o2 = double.MaxValue, soot = 0, co = 0, co2 = 0, temp = 0;
            int hot = cells[0];
            foreach (int c in cells)
            {
                p += F.Pressure(c);
                o2 = Math.Min(o2, F.Partial(T.gO2, c));
                soot = Math.Max(soot, F.Ppm(T.gSoot, c));
                co = Math.Max(co, F.Ppm(T.gCO, c));
                co2 = Math.Max(co2, F.Ppm(T.gCO2, c));
                double tc = F.Pressure(c) > 5 ? F.Temp(c) : 0; // 거의 진공이면 공기 온도계가 못 잰다
                if (tc > temp) { temp = tc; hot = c; }
            }
            double n = 0;
            foreach (int c in cells) n += F.Moles(c);
            RoomMoles[r * 6 + _reads % 6] = (float)n;
            RoomKPa[r] = (float)(p / cells.Length); RoomO2[r] = (float)o2; RoomSoot[r] = (float)soot; RoomCO[r] = (float)co; RoomCO2[r] = (float)co2; RoomTemp[r] = (float)temp;
            RoomHot[r] = hot;
        }
    }

    private int Find(IncidentKind k, int room, int subject = -1)
    {
        for (int i = 0; i < Incidents.Count; i++)
        {
            var x = Incidents[i];
            if (x.Active != 0 && x.Kind == k && x.Room == room && (subject < 0 || x.Subject == subject)) return i;
        }
        return -1;
    }

    private void Raise(Sim sim, IncidentKind k, int room, int cell, float level, int subject = -1, int cause = 0)
    {
        int i = Find(k, room, subject);
        if (i >= 0)
        {
            var x = Incidents[i];
            x.Clear = 0; x.Level = level; if (cell >= 0) x.Cell = cell;
            Incidents[i] = x;
            return;
        }
        // 끝난 지 3분이 안 된 같은 사건은 되살린다 (새로 방송하지 않는다 — 문 여닫이로 판단이 깜빡이지 않게)
        for (int j = Incidents.Count - 1; j >= 0; j--)
        {
            var y = Incidents[j];
            if (y.Active == 0 && y.Kind == k && y.Room == room && (subject < 0 || y.Subject == subject) && sim.Tick - y.Ended < Time.Minutes(3))
            {
                y.Active = 1; y.Clear = 0; y.Level = level; if (cell >= 0) y.Cell = cell;
                Incidents[j] = y;
                return;
            }
        }
        Incidents.Add(new Incident { Kind = k, Active = 1, Room = room, Cell = cell, Subject = subject, Since = sim.Tick, LastAlarm = long.MinValue / 2, Level = level, Cause = cause, RobotA = -1, RobotB = -1 });
        Say(sim, $"{RoomName(room)}: {KindName(k)}");
    }

    public static string KindName(IncidentKind k) => k switch
    {
        IncidentKind.Fire => "불", IncidentKind.Leak => "공기가 샌다", IncidentKind.Vacuum => "공기가 빠졌다", IncidentKind.CO => "일산화탄소",
        IncidentKind.BadAir => "공기가 나쁘다", IncidentKind.PersonDown => "사람이 쓰러졌다", IncidentKind.DeviceBroken => "장치 고장",
        IncidentKind.ElectricFault => "전기 합선", IncidentKind.PowerShort => "전력이 모자란다", IncidentKind.ReactorScram => "원자로가 멈췄다", IncidentKind.Hot => "너무 덥다", IncidentKind.Strain => "문이 압력에 휜다", _ => "",
    };

    private void Detect(Sim sim)
    {
        var seen = new HashSet<long>();
        void Mark(IncidentKind k, int room, int subject = -1) => seen.Add(((long)k << 48) | ((long)(room + 1) << 24) | (uint)(subject + 1));
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (RoomSeen[r] == 0) continue;
            // 불: 연기가 늘고 있거나 · 불꽃만큼 뜨거우면(420K) — 꺼진 뒤 남은 연기만으로는 아니고, 냉각이 끊겨 더운 방도 아니다
            if (RoomTemp[r] > 420 || RoomSoot[r] > 15 && RoomSoot[r] > RoomSootPrev[r] * 1.05f + 1) { Raise(sim, IncidentKind.Fire, r, RoomHot[r], RoomTemp[r], -1, FireCause(RoomHot[r])); Mark(IncidentKind.Fire, r); }
            else if (RoomTemp[r] > 318) { Raise(sim, IncidentKind.Hot, r, RoomHot[r], RoomTemp[r]); Mark(IncidentKind.Hot, r); }
            float dp = RoomKPa[r] - RoomKPaPrev[r];
            if (RoomKPa[r] < 40) { Raise(sim, IncidentKind.Vacuum, r, _roomVent[r], RoomKPa[r], -1, BreachCause(r)); Mark(IncidentKind.Vacuum, r); }
            // 샘: 12초에 기체가 2% 넘게 줄거나 · 1분 내내 줄어 4% 넘게 · 80kPa 아래에서 아직 줄고 있으면 — 온도 · 문 여닫이로 흔들리는 기압 · 막고 난 뒤 낮은 기압은 아니다
            else if (Losing(r) || RoomKPa[r] < 80 && RoomKPaPrev[r] - RoomKPa[r] > 0.2f) { Raise(sim, IncidentKind.Leak, r, LeakSpot(r), -dp, -1, BreachCause(r)); Mark(IncidentKind.Leak, r); }
            if (RoomCO[r] > 150) { Raise(sim, IncidentKind.CO, r, _roomVent[r], RoomCO[r]); Mark(IncidentKind.CO, r); }
            if (RoomCO2[r] > 8000 || RoomO2[r] < 15 && RoomKPa[r] > 40) { Raise(sim, IncidentKind.BadAir, r, _roomVent[r], RoomO2[r]); Mark(IncidentKind.BadAir, r); }
        }
        // 사람 표: 의식을 잃은 사람 (방에 전원이 있어야 표가 읽힌다)
        if (_mind != null && _life != null)
            foreach (var a in _mind.Agents)
            {
                if (!a.IsPerson) continue;
                var b = _life.Bodies[a.C.Body];
                int r = _mind.RoomOf(b.Cell);
                if (r < 0 || RoomSeen[r] == 0 || b.Alive == 0 || b.Conscious != 0) continue;
                Raise(sim, IncidentKind.PersonDown, r, b.Cell, b.Hypoxia, a.C.Body, b.HurtCause);
                Mark(IncidentKind.PersonDown, r, a.C.Body);
            }
        // 장치 · 전기
        if (_net != null)
        {
            foreach (var d in _net.Devices)
            {
                bool broken = false;
                for (int p = 0; p < 4; p++) if (_net.Broken[d.Id * 4 + p] != 0) broken = true;
                if (!broken) continue;
                Raise(sim, IncidentKind.DeviceBroken, d.Room, d.Cell, 1, d.Id);
                Mark(IncidentKind.DeviceBroken, d.Room, d.Id);
            }
            for (int fd = 0; fd < _net.Feeders.Count; fd++)
                if (_net.FaultCell[fd] >= 0) { Raise(sim, IncidentKind.ElectricFault, _net.Feeders[fd].Room, _net.FaultCell[fd], 1, fd); Mark(IncidentKind.ElectricFault, _net.Feeders[fd].Room, fd); }
            if (_net.ReactorScram) { Raise(sim, IncidentKind.ReactorScram, -1, -1, 1, -1, _net.ScramCause); Mark(IncidentKind.ReactorScram, -1); }
        }
        // 휘는 문 · 외판 (하중 센서 — 그 곁 방에 전원이 있어야 읽힌다): 정격의 60%를 넘으면 로봇을 보내 덧대 받친다
        if (_st != null)
            foreach (var door in L.Doors)
            {
                int dc = door.Cell;
                if (_st.Stress[dc] < 0.6f || _st.Braced[dc] != 0) continue;
                int r = HighSideRoom(dc);
                if (r < 0 || RoomSeen[r] == 0) continue;
                Raise(sim, IncidentKind.Strain, r, dc, _st.Stress[dc], dc, _st.DamageCause[dc]);
                Mark(IncidentKind.Strain, r, dc);
            }
        // 다시 멀쩡하게 두 번 읽히면 끝
        for (int i = 0; i < Incidents.Count; i++)
        {
            var x = Incidents[i];
            if (x.Active == 0) continue;
            if (x.Room >= 0 && x.Room < RoomSeen.Length && RoomSeen[x.Room] == 0 && x.Kind is not (IncidentKind.DeviceBroken or IncidentKind.ElectricFault)) continue; // 못 보는 방은 그대로 둔다
            long key = ((long)x.Kind << 48) | ((long)(x.Room + 1) << 24) | (uint)((x.Kind is IncidentKind.PersonDown or IncidentKind.DeviceBroken or IncidentKind.ElectricFault or IncidentKind.Strain ? x.Subject : -1) + 1);
            if (seen.Contains(key)) continue;
            // 끝: 5번(1분) 연속 멀쩡 · 위험한 사건은 적어도 2분은 붙잡는다
            int need = Dangerous(x.Kind) ? 5 : 2;
            if (++x.Clear >= need && (!Dangerous(x.Kind) || sim.Tick - x.Since >= Time.Minutes(2))) { x.Active = 0; x.Ended = sim.Tick; End(sim, ref x); }
            Incidents[i] = x;
        }
        if (Incidents.Count > 200) Incidents.RemoveAll(x => x.Active == 0 && x.Ended < sim.Tick - Time.Hours(6));
    }

    /// <summary>샌다면 어디로: 닫힌 문 너머 방(또는 바깥)과 기압 차가 20kPa 넘는 문 — 그 문틈. 모르면 방 가운데.</summary>
    private int LeakSpot(int r)
    {
        int best = _roomVent[r]; float bdp = 20;
        Span<int> nb = stackalloc int[4];
        foreach (int d in _roomDoors[r])
        {
            if (!F.DoorGapOpen(d)) continue;
            int dc = L.Doors[d].Cell;
            L.Neighbors(dc, nb);
            foreach (int j in nb)
            {
                if (j < 0) continue;
                float po;
                if (L.Kind[j] == CellKind.Outside) po = 0;
                else if (L.Room[j] >= 0 && L.Room[j] != r && RoomSeen[L.Room[j]] != 0) po = RoomKPa[L.Room[j]];
                else continue;
                if (RoomKPa[r] - po > bdp) { bdp = RoomKPa[r] - po; best = dc; }
            }
        }
        return best;
    }

    /// <summary>칸 곁의 방 중 기압이 높은 쪽 (센서 값).</summary>
    private int HighSideRoom(int cell)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(cell, nb);
        int best = -1; float bp = -1;
        foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && RoomKPa[L.Room[j]] > bp) { bp = RoomKPa[L.Room[j]]; best = L.Room[j]; }
        return best;
    }

    private bool Losing(int r)
    {
        if (_reads < 7) return false;
        float now = RoomMoles[r * 6 + _reads % 6], prev = RoomMoles[r * 6 + (_reads + 5) % 6], old = RoomMoles[r * 6 + (_reads + 1) % 6];
        if (prev <= 0 || old <= 0) return false;
        bool fast = (prev - now) / prev > 0.02f;
        bool slow = (old - now) / old > 0.04f && now < prev;
        return fast || slow;
    }

    /// <summary>그 방 둘레 구멍의 원인 (원인 사슬용 — 주컴퓨터가 구멍 자리를 아는 것은 아니다).</summary>
    private int BreachCause(int room)
    {
        if (_st == null) return 0;
        Span<int> nb = stackalloc int[4];
        foreach (int c in L.Rooms[room].Cells)
        {
            L.Neighbors(c, nb);
            foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Wall && F.Hole[j] > 0 && _st.DamageCause[j] != 0) return _st.DamageCause[j];
        }
        return 0;
    }

    private int FireCause(int cell)
    {
        if (_chem == null) return 0;
        if (_chem.FireCause[cell] != 0) return _chem.FireCause[cell];
        int r = L.Room[cell];
        if (r >= 0) foreach (int c in L.Rooms[r].Cells) if (_chem.Burning[c] != 0) return _chem.FireCause[c];
        return 0;
    }

    private void End(Sim sim, ref Incident x)
    {
        Say(sim, $"{RoomName(x.Room)}: {KindName(x.Kind)} — 끝");
        if (x.Kind is IncidentKind.Fire or IncidentKind.CO && _net != null && x.Room >= 0 && Find(IncidentKind.Fire, x.Room) < 0 && Find(IncidentKind.CO, x.Room) < 0) _net.SetDamper(x.Room, true);
        foreach (int rb in new[] { x.RobotA, x.RobotB })
            if (rb >= 0 && _mind != null) _mind.Agents[rb].C.AssignedUntil = 0;
    }

    // ───────────────────────── 하기 ─────────────────────────

    private static bool Dangerous(IncidentKind k) => k is IncidentKind.Fire or IncidentKind.Leak or IncidentKind.Vacuum or IncidentKind.CO or IncidentKind.BadAir;

    private void Act(Sim sim)
    {
        long t = sim.Tick;
        for (int i = 0; i < Incidents.Count; i++)
        {
            var x = Incidents[i];
            if (x.Active == 0) continue;
            // 경보 · 방송: 처음 2분은 10틱마다, 그 뒤로는 1분마다 — 그 방과 문으로 이어진 방
            long gap = t - x.Since < Time.Minutes(2) ? 10 : Time.Minutes(1);
            if (Dangerous(x.Kind) && t - x.LastAlarm >= gap)
            {
                Alarm(sim, x);
                x.LastAlarm = t;
            }
            // 연기 · 독한 공기가 덕트로 번지지 않게 그 방 통풍구를 닫는다
            if (x.Kind is IncidentKind.Fire or IncidentKind.CO && x.Room >= 0 && _net != null && _net.Damper[x.Room] != 0) { _net.SetDamper(x.Room, false); Say(sim, $"{RoomName(x.Room)} 통풍구를 닫았다"); }
            // 로봇에게 맡긴다
            AssignRobots(sim, ref x);
            // 봉쇄
            if (Policy.AutoSeal != 0 && x.Kind is IncidentKind.Fire or IncidentKind.Leak or IncidentKind.Vacuum && x.Room >= 0) SealRoom(sim, ref x);
            Incidents[i] = x;
        }
    }

    private Belief BeliefOf(in Incident x) => x.Kind switch
    {
        IncidentKind.Fire => new Belief { Kind = BeliefKind.Fire, Cell = x.Cell, Subject = -1, Conf = 0.9f, Cause = x.Cause },
        IncidentKind.Leak => new Belief { Kind = BeliefKind.Leak, Cell = x.Cell, Subject = -1, Conf = 0.9f, Cause = x.Cause },
        IncidentKind.Vacuum => new Belief { Kind = BeliefKind.Vacuum, Cell = x.Cell, Subject = -1, Conf = 1f, Cause = x.Cause },
        IncidentKind.CO => new Belief { Kind = BeliefKind.BadAir, Cell = x.Cell, Subject = -1, Conf = Math.Clamp(x.Level / 600f, 0.4f, 1f), Cause = x.Cause },
        IncidentKind.BadAir => new Belief { Kind = BeliefKind.BadAir, Cell = x.Cell, Subject = -1, Conf = 0.8f, Cause = x.Cause },
        IncidentKind.Hot => new Belief { Kind = BeliefKind.Hot, Cell = x.Cell, Subject = -1, Conf = 0.8f, Cause = x.Cause },
        IncidentKind.PersonDown => new Belief { Kind = BeliefKind.Injured, Cell = x.Cell, Subject = x.Subject, Conf = 1f, Cause = x.Cause },
        IncidentKind.DeviceBroken => new Belief { Kind = BeliefKind.Broken, Cell = x.Cell, Subject = x.Subject, Conf = 1f, Cause = x.Cause },
        IncidentKind.ElectricFault => new Belief { Kind = BeliefKind.Spark, Cell = x.Cell, Subject = -1, Conf = 1f, Cause = x.Cause },
        IncidentKind.Strain => new Belief { Kind = BeliefKind.Strain, Cell = x.Cell, Subject = -1, Conf = Math.Clamp(x.Level, 0.6f, 1f), Cause = x.Cause },
        _ => default,
    };

    private void Alarm(Sim sim, in Incident x)
    {
        if (_mind == null || x.Room < 0) return;
        var b = BeliefOf(x);
        if (b.Kind == BeliefKind.None) return;
        var rooms = new SortedSet<int> { x.Room };
        foreach (int d in _roomDoors[x.Room])
        {
            Span<int> nb = stackalloc int[4];
            L.Neighbors(L.Doors[d].Cell, nb);
            foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) rooms.Add(L.Room[j]);
        }
        foreach (int r in rooms)
            if (r < RoomSeen.Length && RoomSeen[r] != 0 && _roomVent[r] >= 0 && F.Pressure(_roomVent[r]) > 5)
                _mind.Announce(sim, _roomVent[r], AlarmDb, b);
        Alarms++;
        if (x.LastAlarm < 0) sim.Facts.Emit(sim.Tick, FactKind.ComputerAlarm, x.Cell, x.Level, x.Cause, -1, (int)x.Kind);
    }

    private void AssignRobots(Sim sim, ref Incident x)
    {
        if (_mind == null) return;
        GoalKind g; int target; bool drone;
        switch (x.Kind)
        {
            case IncidentKind.Fire: g = GoalKind.FightFire; target = x.Cell; drone = true; break;
            case IncidentKind.Leak: case IncidentKind.Vacuum: g = GoalKind.Investigate; target = x.Cell; drone = true; break;
            case IncidentKind.PersonDown: g = GoalKind.Rescue; target = x.Subject; drone = false; break;
            case IncidentKind.DeviceBroken: g = GoalKind.Repair; target = x.Subject; drone = false; break;
            case IncidentKind.ElectricFault: g = GoalKind.Investigate; target = x.Cell; drone = true; break;
            case IncidentKind.Strain: g = GoalKind.Brace; target = x.Cell; drone = false; break;
            default: return;
        }
        int need = x.Kind == IncidentKind.Fire ? 2 : 1;
        bool Busy(int rb) => rb >= 0 && _mind.Agents[rb].C.Up != 0 && _mind.Agents[rb].C.AssignedUntil > sim.Tick;
        int have = (Busy(x.RobotA) ? 1 : 0) + (Busy(x.RobotB) ? 1 : 0);
        while (have < need)
        {
            int best = -1, bd = int.MaxValue;
            foreach (var a in _mind.Agents)
            {
                if (a.IsPerson || a.C.Up == 0 || a.C.Battery < 0.2f || (!drone && a.C.Kind == AgentKind.Drone)) continue;
                if (a.C.AssignedUntil > sim.Tick) continue; // 이미 맡은 일이 있다
                if (a.Id == x.RobotA || a.Id == x.RobotB) continue;
                int d = _mind.Dist(a.C.Cell, x.Cell >= 0 ? x.Cell : a.C.Cell);
                if (d < bd) { bd = d; best = a.Id; }
            }
            if (best < 0) break;
            _mind.Assign(sim, best, g, target, BeliefOf(x), sim.Tick + Time.Minutes(30));
            if (!Busy(x.RobotA)) x.RobotA = best; else x.RobotB = best;
            have++;
            Assignments++;
            Say(sim, $"{_mind.NameOf(best)} → {RoomName(x.Room)} {KindName(x.Kind)}");
            sim.Facts.Emit(sim.Tick, FactKind.ComputerAssign, x.Cell, 0, x.Cause, best, (int)x.Kind);
        }
    }

    /// <summary>봉쇄: 그 방의 문을 닫는다 — 안에 사람이 있으면 함장 방침만큼 기다린다 (그래도 남아 있으면 가둔다). 연기 방은 통풍구도 닫는다.</summary>
    private void SealRoom(Sim sim, ref Incident x)
    {
        if (_st == null || _mind == null) return;
        long t = sim.Tick;
        bool peopleIn = false;
        foreach (var a in _mind.Agents)
            if (a.IsPerson && _life!.Bodies[a.C.Body].Alive != 0 && _mind.RoomOf(a.C.Cell) == x.Room) { peopleIn = true; break; }
        bool waitOver = t - x.Since >= Time.Minutes(Policy.SealWaitMin);
        if (x.Sealed == 0)
        {
            // 사람이 안에 있다: 기다릴까 · 지금 닫을까 — 세계를 복제해 2분 앞을 돌려 보고 고른다 (한 번만)
            int xr = x.Room; long xs = x.Since;
            if (peopleIn && !waitOver && !Imagining && x.Clear == 0 && !Imaginations.Any(v => v.Room == xr && v.Tick >= Math.Min(xs, t - Time.Minutes(5))))
            {
                var (wait, now) = Imagine(sim, x.Room);
                bool sealNow = now.Score < wait.Score - 1;
                wait.Chosen = (byte)(sealNow ? 0 : 1); now.Chosen = (byte)(sealNow ? 1 : 0);
                Imaginations.Add(wait); Imaginations.Add(now);
                if (Imaginations.Count > 40) Imaginations.RemoveRange(0, Imaginations.Count - 40);
                Say(sim, $"{RoomName(x.Room)}: 5분 뒤 — 기다리면 죽음 {wait.Dead} · 쓰러짐 {wait.Down} / 지금 닫으면 죽음 {now.Dead} · 쓰러짐 {now.Down} → {(sealNow ? "지금 닫는다" : "기다린다")}");
                if (sealNow) waitOver = true;
            }
            else if (Imaginations.Any(v => v.Room == xr && v.Tick >= Math.Min(xs, t - Time.Minutes(5)) && v.Option == 1 && v.Chosen == 1)) waitOver = true;
            if (peopleIn && !waitOver) return;
            x.Sealed = 1;
            Seals++;
            int cause = sim.Causes.Add(CauseKind.Computer, peopleIn ? $"주컴퓨터가 {Ko.Eul(RoomName(x.Room))} 봉쇄했다 — 안에 사람이 있었다" : $"주컴퓨터가 {Ko.Eul(RoomName(x.Room))} 봉쇄했다", t, x.Cell, x.Cause);
            sim.Facts.Emit(t, FactKind.ComputerSeal, x.Cell, peopleIn ? 1 : 0, cause, -1, x.Room);
            Say(sim, $"{RoomName(x.Room)} 봉쇄{(peopleIn ? " (사람이 안에 있다)" : "")}");
        }
        // 열린 문은 곁에 아무도 없을 때 다시 닫는다 (로봇 · 사람은 지나다닐 수 있다)
        if (t % 10 != 0) return;
        foreach (int d in _roomDoors[x.Room])
        {
            if (F.DoorOpen[d] <= 0 || _st.DoorTarget[d] <= 0) continue;
            int dc = L.Doors[d].Cell;
            bool near = false;
            foreach (var a in _mind.Agents) if (a.C.Up != 0 && _mind.Dist(a.C.Cell, dc) <= 2) { near = true; break; }
            if (!near) _st.RequestDoor(d, 0);
        }
    }

    // ───────────────────────── 상상 (복제 세계) ─────────────────────────

    /// <summary>세계를 복제해 두 안(기다림 · 지금 봉쇄)을 각각 2분 돌려 본다. 복제 안의 주컴퓨터는 상상하지 않고 규칙대로만 한다.</summary>
    private (Imagined wait, Imagined now) Imagine(Sim sim, int room)
    {
        Imagined Run(byte option)
        {
            var w = sim.Clone();
            var cm = w.Get<ShipMind>();
            cm.Imagining = true;
            if (option == 1)
            {
                // 지금 봉쇄: 그 방 문을 모두 닫는다 (몸이 걸린 문은 닫히지 않는다)
                var st = w.Get<Structure>();
                foreach (int d in _roomDoors[room]) st.RequestDoor(d, 0);
                for (int i = 0; i < cm.Incidents.Count; i++) { var y = cm.Incidents[i]; if (y.Room == room && y.Active != 0) { y.Sealed = 1; cm.Incidents[i] = y; } }
            }
            else cm.Policy.SealWaitMin = 1e6f; // 기다림: 이 2분 동안은 닫지 않는다
            var f0 = w.Get<Field>();
            var before = new double[L.Rooms.Count];
            for (int r = 0; r < L.Rooms.Count; r++) before[r] = L.Rooms[r].Cells.Length > 0 ? f0.Pressure(L.Rooms[r].Cells[0]) : 0;
            w.Run(ImagineTicks);
            var life = w.Get<Life>(); var mind = w.Get<Mind>();
            int dead = 0, down = 0;
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson) continue;
                var b = life.Bodies[a.C.Body];
                if (b.Alive == 0) dead++; else if (b.Conscious == 0 || b.Hypoxia > 0.3f) down++;
            }
            double lost = 0;
            for (int r = 0; r < L.Rooms.Count; r++)
                if (r != room && L.Rooms[r].Cells.Length > 0) lost += Math.Max(0, before[r] - f0.Pressure(L.Rooms[r].Cells[0]));
            return new Imagined { Tick = sim.Tick, Room = room, Option = option, Dead = dead, Down = down, LostKPa = (float)lost, Score = (float)(dead * 100 + down * 30 + lost * 0.5) };
        }
        return (Run(0), Run(1));
    }

    // ───────────────────────── 전력 ─────────────────────────

    private static int ShedRank(string role) => role switch { "industry" => 0, "appliance" => 1, "stove" => 2, "grow" => 3, "charger" => 4, _ => -1 };

    private void Power(Sim sim)
    {
        if (_net == null || sim.Tick % ReadEvery != 0) return;
        long t = sim.Tick;
        // 차단기: 합선 없이 떨어졌으면 30초 뒤 다시 (10분에 한 번까지)
        if (Policy.ResetBreakers != 0)
            for (int fd = 0; fd < _net.Feeders.Count; fd++)
                if (_net.Breaker[fd] == 0 && _net.FaultCell[fd] < 0 && t - BreakerResetAt[fd] > Time.Minutes(10))
                {
                    BreakerResetAt[fd] = t;
                    _net.ResetBreaker(fd);
                    Resets++;
                    sim.Facts.Emit(t, FactKind.BreakerReset, _net.Feeders[fd].Path.Length > 0 ? _net.Feeders[fd].Path[0] : -1, 0, 0, -1, fd);
                    Say(sim, $"{RoomName(_net.Feeders[fd].Room)} 차단기를 다시 올렸다");
                }
        // 원자로가 멈췄으면 보조 발전기
        if (_net.ReactorScram)
            foreach (var d in _net.Devices) if (d.Def.Role == "aux" && _net.On[d.Id] == 0) { _net.SetOn(d.Id, true); Say(sim, "보조 발전기를 켰다"); }
        // 전력이 모자라면 덜 중요한 것부터 끈다 · 넉넉해지면 되켠다
        if (Policy.ShedLoads == 0) return;
        double gap = _net.Demanded - _net.Supplied;
        if (gap > 0.05 * Math.Max(1, _net.Demanded))
        {
            var order = _net.Devices.Where(d => _net.On[d.Id] != 0 && ShedRank(d.Def.Role) >= 0).OrderBy(d => ShedRank(d.Def.Role)).ThenBy(d => d.Id);
            foreach (var d in order)
            {
                if (gap <= 0) break;
                _net.SetOn(d.Id, false); Shed[d.Id] = 1; gap -= d.Def.Kw; Sheds++;
                sim.Facts.Emit(t, FactKind.LoadShed, d.Cell, (float)d.Def.Kw, 0, -1, d.Id);
            }
            Say(sim, "전력이 모자라 덜 중요한 장치를 껐다");
        }
        else if (_net.Supplied - _net.Demanded > 5)
        {
            foreach (var d in _net.Devices)
                if (Shed[d.Id] != 0 && _net.Supplied - _net.Demanded > d.Def.Kw + 2) { _net.SetOn(d.Id, true); Shed[d.Id] = 0; break; } // 하나씩
        }
    }

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.List(new List<ShipPolicy> { Policy });
        w.List(Incidents);
        w.I32(Log.Count); foreach (var (t, s) in Log) { w.I64(t); w.Str(s); }
        w.Bool(Online);
        w.Arr(RoomKPa); w.Arr(RoomKPaPrev); w.Arr(RoomO2); w.Arr(RoomSoot); w.Arr(RoomSootPrev); w.Arr(RoomCO); w.Arr(RoomCO2); w.Arr(RoomTemp); w.Arr(RoomHot); w.Arr(RoomSeen); w.Arr(RoomMoles); w.I32(_reads);
        w.Arr(Shed); w.Arr(BreakerResetAt); w.List(Imaginations);
        w.I64(Alarms); w.I64(Assignments); w.I64(Seals); w.I64(Sheds); w.I64(Resets);
    }

    public void Load(StateReader r)
    {
        Policy = r.List<ShipPolicy>()[0];
        Incidents = r.List<Incident>();
        Log.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { long t = r.I64(); Log.Add((t, r.Str() ?? "")); }
        Online = r.Bool();
        r.ArrInto(RoomKPa); r.ArrInto(RoomKPaPrev); r.ArrInto(RoomO2); r.ArrInto(RoomSoot); r.ArrInto(RoomSootPrev); r.ArrInto(RoomCO); r.ArrInto(RoomCO2); r.ArrInto(RoomTemp); r.ArrInto(RoomHot); r.ArrInto(RoomSeen); r.ArrInto(RoomMoles); _reads = r.I32();
        Shed = r.Arr<byte>(); BreakerResetAt = r.Arr<long>(); Imaginations.Clear(); Imaginations.AddRange(r.List<Imagined>());
        Alarms = r.I64(); Assignments = r.I64(); Seals = r.I64(); Sheds = r.I64(); Resets = r.I64();
    }
}
