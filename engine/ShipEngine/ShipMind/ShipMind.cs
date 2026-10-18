using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>주컴퓨터가 알아챈 일의 종류.</summary>
public enum IncidentKind : byte { None, Fire, Leak, Vacuum, CO, BadAir, PersonDown, DeviceBroken, ElectricFault, PowerShort, ReactorScram, Hot, Strain, Toxic, DoorJam, SensorLost, Injured, PowerLost, LineCut, PipeHunt }

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
    public byte Tries;           // 로봇을 몇 번 보냈나 (빠진 방: 바깥 둘러보기 ↔ 안으로 들어가 보기를 번갈아)
    public int Person;           // 부탁을 받아 준 사람 (-1)
    public long AskedAt;         // 마지막으로 사람에게 부탁한 때
    public int Decl1, Decl2;     // 거절한 사람 (최근 둘 · -1)
    public byte Asks;            // 사람에게 부탁한 번
    // ⑩ 판단 엔진 — 맡긴 로봇이 막힌 까닭 (가장 최근 · 문 · 길 · 물건)과 마지막으로 로봇을 보낸 때
    public SnagKind Snag;
    public GoalKind SnagGoal;
    public int SnagCell, SnagSubject;
    public long SnagTick, SentAt;
    public byte Snagged;         // 막힌 채 다시 보낸 번 (같은 까닭)
    public int Id;               // 사건 번호 (해 본 방법 · 지어낸 계획이 가리킨다 — 목록에서 지워져도 그대로)
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
    public byte AskPeople;         // 사람에게 일을 부탁한다 — 0: 하지 않는다 · 1: 로봇이 없을 때만 (1분 기다린 뒤) · 2: 로봇과 함께 곧바로
    public float RationBelow;      // 보통으로 먹으면 이 날 수를 못 버티면 배급 (0이면 하지 않는다) — 1.5배를 넘게 버티면 푼다
    public float RationShare;      // 배급 때 식량 하루 몫 (보통의 몇 몫)
    public byte Dodge;             // ⑤ 1: 레이더에 잡힌 운석을 비킨다 (사람이 넘어질 값과 견준다)
    public byte AnswerDistress;    // ⑤ 조난 신호 — 0: 가지 않는다 · 1: 추진제가 되면 (도착 몫을 남기고) · 2: 언제나
    public static ShipPolicy Default => new() { AutoSeal = 1, SealWaitMin = 1.5f, ShedLoads = 1, ResetBreakers = 1, AskPeople = 1, RationBelow = 30, RationShare = 0.75f, Dodge = 1, AnswerDistress = 1 };
}

/// <summary>
/// ⑩ 주컴퓨터 (docs/ENGINE.md §4 ⑩). 센서로만 안다 — 전원이 들어온 방의 공기 센서(기압 · 산소 · 이산화탄소 · 일산화탄소 · 그을음 · 온도) ·
/// 망의 장치 상태 · 차단기 · 사람 표(위치 · 의식). 자기 몸(주컴퓨터 장치)이 멈추면 아무것도 모르고 아무것도 못 한다.
/// <para>하는 일: 경보와 방송(③ 소리로 — 들은 사람은 믿음을 얻는다 · 잠든 사람이 깬다) · 로봇에게 일 맡기기(가장 가까운 로봇 · 로봇이 먼저) ·
/// 위험한 방 봉쇄(문 닫기 · 함장 방침의 기다림) · 연기 방 통풍구 닫기 · 전력 모자라면 덜 중요한 장치 끄기 · 보조 발전기 · 차단기 다시 올리기.</para>
/// </summary>
public sealed partial class ShipMind : IEngine
{
    public string Name => "주컴퓨터";
    public Phase Phase => Phase.Sense;
    public int Every => 1;
    public int Offset => 0;

    public const int ReadEvery = 5;            // 센서 읽기 (12초)
    public const int RadioEvery = 3;           // 무선으로만 닿는 방은 읽기 세 번에 한 번 (36초)
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
    public byte[] RoomCO2Hold = Array.Empty<byte>();   // 이산화탄소가 한계를 넘어 머문 읽기 수 (6이면 1분 — 경보)
    public int[] RoomHot;                       // 방에서 가장 뜨거운 칸
    public float[] RoomAirT;                    // 방 공기 온도 (몰 가중 평균 — 벽의 온도계가 읽는 값 · '너무 덥다'는 이것으로. 가장 뜨거운 칸은 불을 찾는 데)
    public float[] RoomNH3, RoomO3;             // 방에서 가장 짙은 암모니아 · 오존 (ppm)
    public float[] RoomHCN, RoomHCl;            // ② 사고 화학: 방에서 가장 짙은 시안화수소 · 염화수소 (ppm — ISS의 CSA-CP처럼 공기 센서 묶음이 잰다)
    public int[] RoomToxCell;                   // 독한 기체가 가장 짙은 칸
    private long _tick;
    public long LastCycle = long.MinValue / 2;
    /// <summary>예비 공기를 풀지 않을 때까지 (배가 새는 줄 알았다) · 그렇게 참은 번수.</summary>
    private long _refillHold = long.MinValue / 2, _refillSince;
    private long[] _extOpenSince = Array.Empty<long>();   // 바깥문이 열린 (밸브가 열린) 때 — 0: 닫힘
    public long RefillHeld;  // 에어락 바깥 문이 마지막으로 열렸거나 밸브로 비운 때 (그 공기는 일부러 내보낸 것)
    public float[] DoorMoles = new float[6];    // 문 칸들에 든 기체 (읽기 6번) — 문이 열리면 그 칸이 공기를 채운다 (잃은 것이 아니다)
    public byte[] DoorWasOpen;                  // 문마다: 지난 읽기 뒤로 한 번이라도 열렸거나 밸브가 열렸다 (읽기 사이에 열렸다 닫힌 문도 놓치지 않게)
    public int[] GaveAirAt;                     // 방마다: 열린 문 · 밸브로 더 낮은 기압의 방에 공기를 주던 마지막 읽기 번호 (그동안 줄어든 것은 샘이 아니다)
    public float[] RoomMoles;                   // [방 × 6]: 지난 1분(읽기 6번)의 기체량 — 새는지는 기체량이 줄어드는 것으로 (온도 · 문 여닫이로 기압이 흔들리는 것과 가른다)
    private int _reads;
    private byte[] _stale = Array.Empty<byte>();   // 이번 읽기에 무선 방의 새 값이 오지 않았다 (그 틱에 다시 정한다 — 찍지 않는다)
    /// <summary>배 전체 기체량 — 1분마다 10개 (느린 샘: 10분에 0.5% 넘게 줄면).</summary>
    public float[] ShipMolesHist = new float[10];
    public byte[] RoomSeen;                     // 1: 이번에 센서가 읽혔다 (전원)
    public byte[] Shed;                         // 장치마다 1: 전력 때문에 꺼 둠
    public long[] BreakerResetAt;               // 급전선마다 마지막으로 다시 올린 틱
    public long Alarms, Assignments, Seals, Sheds, Resets, Exhausts, OuterClosed, StovesCut;
    private int _computer = -2;

    /// <summary>일지 — 하루 한 번(JournalHour시) 지난 일지 뒤로 주컴퓨터가 안 것만 적는다. 줄마다 하나 · 첫 줄은 한 줄 요약.</summary>
    public readonly List<(long tick, string text)> Journal = new();
    public int JournalHour = 22, MorningHour = 7;
    public long LastJournal;                    // 지난 일지를 쓴 틱 (그 뒤의 일을 적는다)
    private long _journalAssign;                // 지난 일지 때까지 로봇에게 맡긴 일 수
    public readonly List<int> ReportedDead = new(); // 일지에 이미 적은 숨을 거둔 사람 (몸 번호)
    /// <summary>마지막 아침 방송 (화면 자막).</summary>
    public string Morning = "";

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
        RoomKPa = new float[nr]; RoomKPaPrev = new float[nr]; RoomO2 = new float[nr]; RoomSoot = new float[nr]; RoomSootPrev = new float[nr]; RoomCO = new float[nr]; RoomCO2 = new float[nr]; RoomTemp = new float[nr]; RoomCO2Hold = new byte[nr];
        RoomHot = new int[nr]; RoomAirT = new float[nr]; GaveAirAt = Enumerable.Repeat(-100, nr).ToArray(); DoorWasOpen = new byte[L.Doors.Count]; RoomNH3 = new float[nr]; RoomO3 = new float[nr]; RoomHCN = new float[nr]; RoomHCl = new float[nr]; RoomToxCell = new int[nr]; RoomSeen = new byte[nr]; _stale = new byte[nr]; RoomMoles = new float[nr * 6];
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
        _mind ??= sim.Find<Mind>(); _net ??= sim.Find<Network>(); _out ??= sim.Find<Outside>(); _st ??= sim.Find<Structure>(); _life ??= sim.Find<Life>(); _chem ??= sim.Find<Chemistry>();
        if (_net != null && Shed.Length != _net.Devices.Count) Shed = new byte[_net.Devices.Count];
        if (_net != null && BreakerResetAt.Length != _net.Feeders.Count) BreakerResetAt = Enumerable.Repeat(long.MinValue / 2, _net.Feeders.Count).ToArray();
        if (_computer == -2) _computer = _net?.Devices.FirstOrDefault(d => d.Type == "MainComputer")?.Id ?? -1;
    }

    /// <summary>맡긴 로봇의 막힘을 새로 읽은 번 (관찰용).</summary>
    public long SnagsRead;
    public string RoomName(int r) => r >= 0 && r < L.Rooms.Count ? L.Rooms[r].Name : "배";

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Step(Sim sim, double dt)
    {
        Bind(sim);
        bool was = Online;
        bool alive = _net != null && _computer >= 0 && _net.Working(_computer);
        if (alive && !Imagining) SelfCheck(sim);                  // 제 몸 온도 — 뜨거우면 느려지고 · 넘으면 멈춘다 (멈춰 있어도 식는 것은 안다)
        Online = alive && HeatStopped == 0;
        if (was && !Online) { Say(sim, "주컴퓨터가 멈췄다"); if (_net != null) Array.Clear(_net.RoomAir); sim.Facts.Emit(sim.Tick, FactKind.ComputerDown, _computer >= 0 ? _net!.Devices[_computer].Cell : -1); }
        LogStep(sim, Online);   // 정비 기록 · 물음에 답하기 (멈춰 있으면 적지 못한다)
        if (!Online) return;
        long t = Prof.Start();
        foreach (var d in L.Doors) if (F.DoorOpen[d.Id] > 0f || F.DoorBleed[d.Id] != 0) DoorWasOpen[d.Id] = 1;
        if (sim.Tick % _effectiveRead == 0)
        {
            _tick = sim.Tick; ReadSensors(); t = Prof.Lap("주컴퓨터:센서", t);
            Detect(sim); t = Prof.Lap("주컴퓨터:알아채기", t);
            Refill(sim); EmergencyRefill(sim); KeepEmptyShut(sim); t = Prof.Lap("주컴퓨터:공기 채우기", t);
        }
        Act(sim); t = Prof.Lap("주컴퓨터:대응", t);
        RunPlans(sim); t = Prof.Lap("주컴퓨터:계획", t);  // ⑩ 판단 엔진: 지어낸 계획을 걸음마다
        Power(sim); t = Prof.Lap("주컴퓨터:전력", t);
        Supply(sim); t = Prof.Lap("주컴퓨터:배급", t);   // ⑪ 배급 · 보관함 무게
        Helm(sim); t = Prof.Lap("주컴퓨터:조타", t);     // ⑤ 조타: 정해진 점화 · 운석 비키기 · 다른 배
        Weather(sim); t = Prof.Lap("주컴퓨터:날씨", t);  // ⑤ 우주 날씨 대비: 파편 구름 · 양성자 폭풍 — 문을 닫고 대피실로
        long tod = sim.Tick % Time.TicksPerDay; // 멈춰 있던 때에는 일지도 방송도 없다 (다음 일지가 그 사이를 함께 적는다)
        if (tod == Time.Hours(JournalHour) % Time.TicksPerDay) WriteJournal(sim);
        if (tod == Time.Hours(MorningHour) % Time.TicksPerDay) MorningBroadcast(sim);
        // 주간 비상 훈련 (이레마다 10시 — ISS 승무원의 정기 비상 훈련처럼): 깨어 있는 이가 불 끄기 · 구멍 막기 · 응급 처치 절차를 되짚는다 (⑧에 요청)
        if (tod == Time.Hours(10) && sim.Tick / Time.TicksPerDay % 7 == 6 && _mind != null && Incidents.All(x => x.Active == 0))
        {
            _mind.Drill(sim);
            Say(sim, "주간 비상 훈련 — 불 끄기 · 구멍 막기 · 응급 처치 절차를 되짚습니다");
        }
    }

    private static readonly bool PlanLog = Environment.GetEnvironmentVariable("PLAN_LOG") == "1";

    private void Say(Sim sim, string text)
    {
        if (PlanLog && !Imagining && (text.StartsWith("생각") || text.StartsWith("계획") || text.StartsWith("미리"))) Console.WriteLine($"      ({sim.Seed}) {Time.Clock(sim.Tick)} {text}");
        Log.Add((sim.Tick, text));
        if (Log.Count > 300) Log.RemoveRange(0, Log.Count - 300);
    }

    // ───────────────────────── 센서 ─────────────────────────

    private void ReadSensors()
    {
        _reads++;
        if (_reads % 5 == 0)
        {
            Array.Copy(ShipMolesHist, 1, ShipMolesHist, 0, ShipMolesHist.Length - 1);
            float total = 0;
            for (int r = 0; r < L.Rooms.Count; r++) total += RoomMoles[r * 6 + (_reads - 1) % 6];
            total += DoorMoles[(_reads - 1) % 6];
            ShipMolesHist[^1] = total;
        }
        double doors = 0;
        foreach (var d in L.Doors) doors += F.Moles(d.Cell);
        // 에어락 순환 (일부러 내보낸 공기): 바깥문이 열렸거나 밸브가 열렸다 — 15분 넘게 그대로면 순환이 아니다 (끼여 열린 바깥문 · 비워지지 않는 방 — 느린 샘을 가리지 않게)
        if (_extOpenSince.Length != L.Doors.Count) _extOpenSince = new long[L.Doors.Count];
        foreach (var d in L.Doors)
        {
            if (!d.External) continue;
            if (F.DoorOpen[d.Id] > 0 || F.DoorBleed[d.Id] != 0)
            {
                if (_extOpenSince[d.Id] == 0) _extOpenSince[d.Id] = _tick;
                if (_tick - _extOpenSince[d.Id] < Time.Minutes(15)) LastCycle = _tick;
            }
            else _extOpenSince[d.Id] = 0;
        }
        DoorMoles[_reads % 6] = (float)doors;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            RoomKPaPrev[r] = RoomKPa[r]; RoomSootPrev[r] = RoomSoot[r];
            var cells = L.Rooms[r].Cells;
            byte link = cells.Length == 0 || _net == null ? (byte)0 : _net.SensorLink(_tick, r);
            if (link == 0) { RoomSeen[r] = 0; continue; } // 전원이 없거나 · 센서 선이 끊기고 무선도 닿지 않는 방은 못 본다
            // 무선으로만 닿는 방: 읽기 세 번에 한 번 새 값 (그 사이는 지난 값 그대로 — 기압 변화는 그만큼 늦게 · 한꺼번에 보인다)
            bool fresh = link == 1 || RoomSeen[r] != 2 || (_reads + r) % RadioEvery == 0;
            RoomSeen[r] = link;
            _stale[r] = (byte)(fresh ? 0 : 1);
            if (!fresh) { RoomMoles[r * 6 + _reads % 6] = RoomMoles[r * 6 + (_reads + 5) % 6]; continue; }
            double p = 0, o2 = double.MaxValue, soot = 0, co = 0, co2 = 0, temp = 0, nh3 = 0, o3 = 0, hcn = 0, hcl = 0, worst = 0;
            int hot = cells[0], tox = cells[0];
            double n = 0, nt = 0, na = 0;
            foreach (int c in cells)
            {
                double m = F.Read(c, out double gc, out double pc);   // 칸마다 한 번 (Pressure · Partial · Ppm · Temp와 같은 셈)
                double Ppm(int g) => m > 1e-9 ? F.Gas(g, c) / m * 1e6 : 0;
                p += pc;
                o2 = Math.Min(o2, m > 1e-12 ? pc * F.Gas(T.gO2, c) / m : 0);
                soot = Math.Max(soot, Ppm(T.gSoot));
                co = Math.Max(co, Ppm(T.gCO));
                co2 += Ppm(T.gCO2) * m;   // 이산화탄소는 방 평균 (몰 가중) — 벽의 센서는 숨 한 모금의 짙은 칸이 아니라 방 공기를 읽는다
                double a = Ppm(T.gNH3), oz = Ppm(T.gO3), cn = Ppm(T.gHCN), cl = Ppm(T.gHCl);
                nh3 = Math.Max(nh3, a); o3 = Math.Max(o3, oz); hcn = Math.Max(hcn, cn); hcl = Math.Max(hcl, cl);
                double bad = Tox(a, oz, cn, cl);
                if (bad > worst) { worst = bad; tox = c; }
                double tg = F.Temp(c, gc);
                double tc = pc > 5 ? tg : 0; // 거의 진공이면 공기 온도계가 못 잰다
                if (tc > temp) { temp = tc; hot = c; }
                n += m; if (pc > 5) { nt += m * tg; na += m; }
            }
            RoomMoles[r * 6 + _reads % 6] = (float)n;
            RoomAirT[r] = na > 0 ? (float)(nt / na) : 0;
            RoomKPa[r] = (float)(p / cells.Length); RoomO2[r] = (float)o2; RoomSoot[r] = (float)soot; RoomCO[r] = (float)co; RoomCO2[r] = (float)(n > 0 ? co2 / n : 0); RoomTemp[r] = (float)temp;
            // 이산화탄소가 5000ppm(ISS 행동 한계 약 4mmHg)을 넘어 머문 읽기 수 — 4000 아래로 내려가야 풀린다 (달리는 사람 곁에서 잠깐 짙어졌다 옅어지는 것으로 경보가 깜빡이지 않게)
            if (RoomCO2[r] > 5000) { if (RoomCO2Hold[r] < 255) RoomCO2Hold[r]++; } else if (RoomCO2[r] < 4000) RoomCO2Hold[r] = 0;
            RoomHot[r] = hot; RoomNH3[r] = (float)nh3; RoomO3[r] = (float)o3; RoomHCN[r] = (float)hcn; RoomHCl[r] = (float)hcl; RoomToxCell[r] = tox;
        }
        // 열린 문 · 압력 맞춤 밸브로 더 낮은 방과 이어진 방: 공기를 그리로 주는 중 (새는 방의 문을 열면 옆 방 기압이 한꺼번에 떨어진다 — 그 방의 구멍이 아니다)
        Span<int> nb = stackalloc int[4];
        foreach (var d in L.Doors)
        {
            if (DoorWasOpen[d.Id] == 0 && F.DoorOpen[d.Id] <= 0f && F.DoorBleed[d.Id] == 0) continue;
            DoorWasOpen[d.Id] = 0;
            L.Neighbors(d.Cell, nb);
            foreach (int a in nb)
                foreach (int b in nb)
                {
                    if (a < 0 || b < 0) continue;
                    int ra = L.Room[a], rb = L.Room[b];
                    if (ra < 0 || rb < 0 || ra == rb || RoomSeen[ra] == 0 || RoomSeen[rb] == 0) continue;
                    // 아직 높거나 · 이번 읽기 사이에 이쪽은 내려가고 저쪽은 올라갔다 (벌써 맞춰졌다)
                    if (RoomKPa[ra] > RoomKPa[rb] + 0.3f || RoomKPaPrev[ra] - RoomKPa[ra] > 0.3f && RoomKPa[rb] - RoomKPaPrev[rb] > 0.3f) GaveAirAt[ra] = _reads;
                }
        }
    }

    public int Find(IncidentKind k, int room, int subject = -1)
    {
        for (int i = 0; i < Incidents.Count; i++)
        {
            var x = Incidents[i];
            if (x.Active != 0 && x.Kind == k && x.Room == room && (subject < 0 || x.Subject == subject)) return i;
        }
        return -1;
    }

    private void Raise(Sim sim, IncidentKind k, int room, int cell, float level, int subject = -1, int cause = 0, string? detail = null)
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
        Incidents.Add(new Incident { Id = ++IncidentSeq, Kind = k, Active = 1, Room = room, Cell = cell, Subject = subject, Since = sim.Tick, LastAlarm = long.MinValue / 2, Level = level, Cause = cause, RobotA = -1, RobotB = -1, Person = -1, Decl1 = -1, Decl2 = -1, AskedAt = long.MinValue / 2 });
        Say(sim, detail == null ? $"{RoomName(room)}: {KindName(k)}" : $"{RoomName(room)}: {KindName(k)} — {detail}");
    }

    /// <summary>독한 정도 (암모니아 ppm에 견준 값): 오존 ×100 · 시안화수소 ×10 · 염화수소 ×7 (각 기체의 해로운 문턱 — 암모니아 35 · 오존 0.3 · 시안화수소 4.7 · 염화수소 5 — 에 맞춘 무게).</summary>
    private static double Tox(double nh3, double o3, double hcn, double hcl) => Math.Max(Math.Max(nh3, o3 * 100), Math.Max(hcn * 10, hcl * 7));

    /// <summary>방송에 붙일 기체 이름과 짙기 — 가장 독한 것 하나 ('시안화수소 80ppm — 숨을 참고 나오세요').</summary>
    private string ToxDetail(int r)
    {
        double nh3 = RoomNH3[r], o3 = RoomO3[r] * 100, hcn = RoomHCN[r] * 10, hcl = RoomHCl[r] * 7;
        if (hcn >= nh3 && hcn >= o3 && hcn >= hcl) return $"시안화수소 {RoomHCN[r]:0}ppm — 숨을 참고 나오세요";
        if (hcl >= nh3 && hcl >= o3) return $"염화수소 {RoomHCl[r]:0}ppm — 입을 가리고 나오세요";
        if (o3 > nh3) return $"오존 {RoomO3[r]:0.0}ppm";
        return $"암모니아 {RoomNH3[r]:0}ppm";
    }

    public static string KindName(IncidentKind k) => k switch
    {
        IncidentKind.Fire => "불", IncidentKind.Leak => "공기가 샌다", IncidentKind.Vacuum => "공기가 빠졌다", IncidentKind.CO => "일산화탄소",
        IncidentKind.BadAir => "공기가 나쁘다", IncidentKind.PersonDown => "사람이 쓰러졌다", IncidentKind.DeviceBroken => "장치 고장",
        IncidentKind.ElectricFault => "전기 합선", IncidentKind.PowerShort => "전력이 모자란다", IncidentKind.ReactorScram => "원자로가 멈췄다", IncidentKind.Hot => "너무 덥다", IncidentKind.Strain => "문이 압력에 휜다", IncidentKind.Toxic => "독한 기체", IncidentKind.DoorJam => "문이 끼었다", IncidentKind.SensorLost => "센서가 끊겼다", IncidentKind.Injured => "다친 사람", IncidentKind.PowerLost => "생명 설비 정전", IncidentKind.LineCut => "선이 끊겼다", IncidentKind.PipeHunt => "관이 샌다", _ => "",
    };

    private void Detect(Sim sim)
    {
        var seen = new HashSet<long>();
        void Mark(IncidentKind k, int room, int subject = -1) => seen.Add(((long)k << 48) | ((long)(room + 1) << 24) | (uint)(subject + 1));
        // 배 전체 산소 (사람이 쓰는 방의 가운뎃값) — 배 전체가 함께 묽어진 것은 방마다가 아니라 배의 일 하나다
        float shipO2 = ShipO2();
        if (shipO2 < 15) { Raise(sim, IncidentKind.BadAir, -1, -1, shipO2); Mark(IncidentKind.BadAir, -1); }
        // 센서 선이 끊긴 방: 그 방을 못 본다는 것 자체가 사건이다 — 가장 가까운 끊긴 자리로 로봇을 보내 잇는다
        if (_net != null)
            for (int r = 0; r < L.Rooms.Count; r++)
                if (!_net.RoomLinked(r) && _net.RoomPowered(r))
                {
                    int cut = _net.DataCutCell[r];
                    Raise(sim, IncidentKind.SensorLost, r, cut, RoomSeen[r] == 2 ? 0.5f : 1, -1, _net.DataBrokenCause[cut]);   // 무선으로 보이면 0.5 (느리게라도 본다 — 덜 급하다)
                    Mark(IncidentKind.SensorLost, r);
                }
        // 끊긴 전선 · 관 (⑥ 바퀴 3 — 반사 측정으로 자리를 안다 · 불 · 구멍 자리의 샘도): 방마다 주컴퓨터에서 가장 가까운 끊긴 칸으로 로봇을 보내 잇는다
        if (_net != null)
            for (int r = 0; r < L.Rooms.Count; r++)
                if (_net.LineFixCell(r, _computer >= 0 ? _net.Devices[_computer].Cell : -1) is int lc && lc >= 0)
                {
                    Raise(sim, IncidentKind.LineCut, r, lc, 1, -1, _net.LineCauseAt(lc));
                    Mark(IncidentKind.LineCut, r);
                }
        // 자리를 모르는 관 샘 (⑥ 바퀴 3 (라)): 밸브로 좁히고 그 방으로 로봇을 보낸다
        PipeHunt(sim, (k, r, sub) => Mark(k, r, sub));
        var leaking = new List<(int room, float drop)>();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (RoomSeen[r] == 0) continue;
            if (Cycling(r)) continue; // 막 순환한 에어락: 비우면 빠지고 채우면 압축열로 달아오른다 — 사고가 아니다
            // 불: 연기가 늘고 있거나 · 불꽃만큼 뜨거우면(420K) — 꺼진 뒤 남은 연기만으로는 아니고, 냉각이 끊겨 더운 방도 아니다
            if (RoomTemp[r] > 420 && RoomO2[r] > 5 || RoomSoot[r] > 15 && RoomSoot[r] > RoomSootPrev[r] * 1.05f + 1) { Raise(sim, IncidentKind.Fire, r, RoomHot[r], RoomTemp[r], -1, FireCause(RoomHot[r]) is int fc && fc != 0 ? fc : NearFireCause(r)); Mark(IncidentKind.Fire, r); }
            else if (RoomAirT[r] > 318) { Raise(sim, IncidentKind.Hot, r, RoomHot[r], RoomTemp[r], -1, NearFireCause(r) is int hc && hc != 0 ? hc : _chem?.HeatCauseAt(RoomHot[r]) ?? 0); Mark(IncidentKind.Hot, r); }
            float dp = RoomKPa[r] - RoomKPaPrev[r];
            if (IsolatedAirlock(r) || Deliberate(r)) { }   // 안쪽 문이 모두 닫힌 에어락은 비어 있어도 된다 (순환 중) · 지어낸 계획이 일부러 비우는 방 (임시 에어락)
            else if (RoomKPa[r] < 40) { Raise(sim, IncidentKind.Vacuum, r, _roomVent[r], RoomKPa[r], -1, LeakCause(r)); Mark(IncidentKind.Vacuum, r); }
            // 샘: 12초에 기체가 2% 넘게 줄거나 · 1분 내내 줄어 4% 넘게 · 80kPa 아래에서 아직 줄고 있으면 — 온도 · 문 여닫이로 흔들리는 기압 · 막고 난 뒤 낮은 기압은 아니다
            else if (Losing(r) || RoomKPa[r] < 80 && RoomKPaPrev[r] - RoomKPa[r] > 0.2f && !Moved(r, 1)) leaking.Add((r, -dp));
            if (RoomCO[r] > 150) { Raise(sim, IncidentKind.CO, r, _roomVent[r], RoomCO[r], -1, NearFireCause(r)); Mark(IncidentKind.CO, r); }
            // 독한 기체: 암모니아 35ppm(15분 노출 한계) · 오존 0.3ppm 넘으면 — 그 방 안에 새는 냉매 고리가 있으면 그 샘이 원인
            if (RoomNH3[r] > 35 || RoomO3[r] > 0.3f || RoomHCN[r] > 10 || RoomHCl[r] > 5)
            {
                int tc = ToxicCause(r);
                Raise(sim, IncidentKind.Toxic, r, RoomToxCell[r], (float)Tox(RoomNH3[r], RoomO3[r], RoomHCN[r], RoomHCl[r]), -1, tc != 0 ? tc : NearFireCause(r), ToxDetail(r));
                Mark(IncidentKind.Toxic, r);
            }
            if (RoomCO2Hold[r] >= 6 || RoomO2[r] < 15 && RoomKPa[r] > 40 && RoomO2[r] < shipO2 - 1.5f) { Raise(sim, IncidentKind.BadAir, r, _roomVent[r], RoomO2[r], -1, NearFireCause(r)); Mark(IncidentKind.BadAir, r); }
        }
        // 샘: 문(열렸거나 · 닫힌 문의 틈)으로 이어진 새는 방들은 한 구멍으로 함께 빠지는 것 — 그중 기압이 가장 낮은 방(구멍에 가장 가깝다) 하나만 사건으로 (방마다 봉쇄 · 로봇을 보내지 않게)
        var losing = leaking.Select(x => x.room).ToHashSet();
        foreach (var grp in leaking.GroupBy(x => AirGroup(x.room, losing)))
        {
            var (r, drop) = grp.OrderBy(x => RoomKPa[x.room]).ThenBy(x => x.room).First();
            Raise(sim, IncidentKind.Leak, r, LeakSpot(r), drop, -1, LeakCause(r)); Mark(IncidentKind.Leak, r);
        }
        // 느린 샘: 배 전체 기체가 10분에 0.5% 넘게 줄고 있다 — 어디서인지 모르면 기압이 가장 낮은 방(새는 곳에 가깝다)부터 살핀다
        if (sim.Tick - LastCycle > Time.Minutes(11) && ShipMolesHist[0] > 0 && (ShipMolesHist[0] - ShipMolesHist[^1]) / ShipMolesHist[0] > 0.005f && ShipMolesHist[^2] > ShipMolesHist[^1])
        {
            // 이미 아는 샘이 있으면 배 전체가 주는 것은 그 샘 때문이다 — 그 사건을 이어 가고 새로 짚지 않는다
            // (새는 방의 문을 열어 기압을 맞춘 뒤 옆 방이 조금 더 낮아지자 그 방을 또 '샌다'로 짚었다)
            // — 다 빠진 방(5kPa 아래)의 샘은 더 잃을 공기가 없다: 그래도 배가 준다면 그 방으로 새어 드는 다른 길이 있다 (뚫린 칸막이 · 문틈 — ④ 충돌이 칸막이를 꿰뚫었다)
            bool known = false;
            foreach (var x in Incidents) if (x.Kind == IncidentKind.Leak && x.Active != 0) { Mark(IncidentKind.Leak, x.Room); if (RoomKPa[x.Room] > 5) known = true; }
            int low = -1; float lp = float.MaxValue;
            if (!known)
                for (int r = 0; r < L.Rooms.Count; r++)
                    if (RoomSeen[r] != 0 && L.Rooms[r].Cells.Length > 0 && RoomKPa[r] > 5 && RoomKPa[r] < lp) { lp = RoomKPa[r]; low = r; }
            if (low >= 0 && Find(IncidentKind.Leak, low) < 0) Raise(sim, IncidentKind.Leak, low, LeakSpot(low), lp, -1, LeakCause(low));
            if (low >= 0) Mark(IncidentKind.Leak, low);
        }
        // 다 빠져 봉쇄한 방으로 새어 드는 길 (방마다 단 초음파 샘 찾개 — 쉬익의 자리): 다른 구역에서 그 방으로 공기가 들어오면 높은 쪽 방의 그 틈이 샘이다
        // (④ 충돌이 칸막이를 꿰뚫었다 — 빠진 방은 봉쇄했는데 옆 통로가 뚫린 칸막이로 하루 내내 새어 배 전체가 숨이 막혔다)
        foreach (var x in Incidents.ToArray())
        {
            if (x.Kind != IncidentKind.Vacuum || x.Active == 0 || x.Room < 0) continue;
            int v = _roomVent[x.Room];
            if (v < 0 || F.RegionOf.Length <= v || F.RegionOf[v] < 0) continue;
            int reg = F.RegionOf[v];
            foreach (var k in F.Links)
            {
                if (k.RB < 0 || k.Via < 0 || k.Kind == LinkKind.DoorAjar || k.RA != reg && k.RB != reg) continue;
                double inflow = k.RA == reg ? -k.Flow : k.Flow;
                if (inflow < 0.3) continue;
                int hi = k.RA == reg ? k.B : k.A, hr = hi >= 0 ? L.Room[hi] : -1;
                if (hr < 0 || hr == x.Room || RoomSeen[hr] == 0 || Deliberate(hr)) continue;   // 계획이 일부러 비우는 방(임시 에어락)은 샘이 아니다
                Raise(sim, IncidentKind.Leak, hr, k.Via, RoomKPa[hr], -1, _st != null && _st.DamageCause[k.Via] != 0 ? _st.DamageCause[k.Via] : x.Cause);   // 이미 있으면 그 자리로 고친다
                Mark(IncidentKind.Leak, hr);
            }
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
        // 사람 표: 돌봄이 필요한 몸 — 덮지 않은 큰 상처 · 흐르는 피 · 곪은 상처 (안전한 방에서도 · ⑦ 깊이 바퀴 2) · 열이 나는 이는 쉬라고 한 번 알린다
        if (_mind != null && _life != null)
            foreach (var a in _mind.Agents)
            {
                if (!a.IsPerson) continue;
                var b = _life.Bodies[a.C.Body];
                int r = _mind.RoomOf(b.Cell);
                if (r < 0 || RoomSeen[r] == 0 || b.Alive == 0) continue;
                if (Life.NeedsCare(b)) { Raise(sim, IncidentKind.Injured, r, b.Cell, Math.Max(b.Burn, Math.Max(b.Blunt, b.Infect)), a.C.Body, b.InfectCause != 0 && b.Infect > 0 ? b.InfectCause : b.HurtCause); Mark(IncidentKind.Injured, r, a.C.Body); }
                FeverWatch(sim, a.Id, b);
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
        // 끼인 문 (문 센서 — 곁 방에 전원이 있어야 읽힌다): 파편 · 압력에 틀어져 여닫히지 않는다 — 로봇을 보내 비틀어 바로잡는다
        if (_st != null)
            for (int d = 0; d < L.Doors.Count; d++)
            {
                if (_st.DoorJam[d] == 0) continue;
                int dc = L.Doors[d].Cell;
                int r = HighSideRoom(dc);
                if (r < 0 || RoomSeen[r] == 0) continue;
                Raise(sim, IncidentKind.DoorJam, r, dc, (float)_st.DoorDeltaP(d), d, _st.DamageCause[dc]);
                Mark(IncidentKind.DoorJam, r, d);
            }
        // ⑥ 생명 설비 정전 (급전선을 다시 올릴 수 없다 — 연장선)
        foreach (var (r, cell, cause) in PowerLostRooms(sim)) { Raise(sim, IncidentKind.PowerLost, r, cell, 1, -1, cause); Mark(IncidentKind.PowerLost, r); }
        // 다시 멀쩡하게 두 번 읽히면 끝
        for (int i = 0; i < Incidents.Count; i++)
        {
            var x = Incidents[i];
            if (x.Active == 0) continue;
            if (x.Room >= 0 && x.Room < RoomSeen.Length && (RoomSeen[x.Room] == 0 || _stale[x.Room] != 0) && x.Kind is not (IncidentKind.DeviceBroken or IncidentKind.ElectricFault or IncidentKind.DoorJam or IncidentKind.SensorLost or IncidentKind.PowerLost or IncidentKind.LineCut or IncidentKind.PipeHunt)) continue; // 못 보는 방은 그대로 둔다
            long key = ((long)x.Kind << 48) | ((long)(x.Room + 1) << 24) | (uint)((x.Kind is IncidentKind.PersonDown or IncidentKind.DeviceBroken or IncidentKind.ElectricFault or IncidentKind.Strain or IncidentKind.DoorJam or IncidentKind.PipeHunt ? x.Subject : -1) + 1);
            if (seen.Contains(key)) continue;
            // 끝: 5번(1분) 연속 멀쩡 · 위험한 사건은 적어도 2분은 붙잡는다
            int need = Dangerous(x.Kind) ? 5 : 2;
            if (++x.Clear >= need && (!Dangerous(x.Kind) || sim.Tick - x.Since >= Time.Minutes(2))) { x.Active = 0; x.Ended = sim.Tick; Incidents[i] = x; End(sim, ref x); } // 끝난 것을 먼저 적어야 End가 '그 방에 다른 사건이 남았나'를 바르게 본다
            Incidents[i] = x;
        }
        if (Incidents.Count > 200) Incidents.RemoveAll(x => x.Active == 0 && x.Ended < Math.Min(sim.Tick - Time.Hours(6), LastJournal)); // 아직 일지에 안 적은 것은 남긴다
    }

    /// <summary>샌다면 어디로: 닫힌 문 너머 방(또는 바깥)과 기압 차가 20kPa 넘는 문 — 그 문틈. 모르면 방 가운데.</summary>
    private int LeakSpot(int r)
    {
        // 방마다 단 초음파 샘 찾개 (ISS 휴대용 초음파 샘 찾개처럼 — 쉬익의 자리): 이 방에서 빠져나가는 가장 센 틈 (벽의 금 · 구멍 · 바닥 판)
        if (RoomSeen[r] != 0 && _roomVent[r] >= 0 && F.RegionOf.Length > _roomVent[r] && F.RegionOf[_roomVent[r]] is int reg && reg >= 0)
        {
            int via = -1; double flow = 0.5;
            foreach (var k in F.Links)
            {
                if (k.Kind is LinkKind.DoorGap or LinkKind.DoorAjar || k.Via < 0) continue;
                double out_ = k.RA == reg ? k.Flow : k.RB == reg ? -k.Flow : 0;   // 이 방에서 나가는 쪽 (몰/초)
                if (out_ > flow) { flow = out_; via = k.Via; }
            }
            if (via >= 0) return via;
        }
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

    /// <summary>다른 사건의 뿌리라 무엇보다 먼저인 일: 차단기가 눌어붙어 계속 타는 합선.</summary>
    private bool Root(in Incident x) => x.Kind == IncidentKind.ElectricFault && _net != null && _net.IsStuckFault(x.Subject);

    /// <summary>사람이 쓰는 방(읽히고 · 40kPa 넘는 · 떨어진 에어락이 아닌)의 산소 분압 가운뎃값 — 없으면 21.</summary>
    public float ShipO2()
    {
        var v = new List<float>();
        for (int r = 0; r < L.Rooms.Count; r++) if (RoomSeen[r] != 0 && RoomKPa[r] > 40 && !IsolatedAirlock(r)) v.Add(RoomO2[r]);
        if (v.Count == 0) return 21;
        v.Sort();
        return v[v.Count / 2];
    }

    /// <summary>
    /// 다시 채우기: 큰 감압 뒤 배 기압(사람이 쓰는 방 평균)이 90kPa 아래이고 새는 곳이 없으면 예비 공기를 푼다 (세 시간쯤에 22kPa · 96kPa에서 잠근다).
    /// 새는 중에는 풀지 않는다 — 우주로 버리는 셈이다.
    /// </summary>
    private void Refill(Sim sim)
    {
        if (_net == null) return;
        double sum = 0; int n = 0;
        for (int r = 0; r < L.Rooms.Count; r++)
            if (RoomSeen[r] != 0 && RoomKPa[r] > 40 && !IsolatedAirlock(r)) { sum += RoomKPa[r]; n++; }
        // 배 전체가 40kPa 아래로 옅어졌다 (진공은 아니다): 그 방들로 잰다 — 고치기 전에는 잴 방이 없어 예비가 넉넉해도 다시 채우지 않았다
        if (n == 0)
            for (int r = 0; r < L.Rooms.Count; r++)
                if (RoomSeen[r] != 0 && RoomKPa[r] > 5 && !IsolatedAirlock(r)) { sum += RoomKPa[r]; n++; }
        bool leaking = Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Leak);
        // 배 전체 기체의 흐름: 10분 넘게 풀었는데 푼 만큼의 30%도 늘지 않았다 — 어딘가 샌다 (모르는 샘에 예비를 버리지 않는다 · 한 시간 잠근다) /
        // 풀기 전: 지난 2분에 0.1% 넘게 줄고 있으면 이번에는 풀지 않는다 (사고 직후 · 아직 새는 중)
        float h0 = ShipMolesHist[0], h1 = ShipMolesHist[^1], h2 = ShipMolesHist[^3];
        if (_net.AirRelease > 0 && h0 > 0 && _tick - _refillSince >= Time.Minutes(10) && _tick - LastCycle > Time.Minutes(11))
        {
            double released = _net.AirRelease * Time.SecondsPerTick * 25 * (ShipMolesHist.Length - 1);
            if (h1 - h0 < released * 0.3)
            {
                _refillHold = _tick + Time.Hours(1); RefillHeld++;
                Say(sim, "예비 공기를 풀어도 배 기압이 오르지 않는다 — 어딘가 샌다 · 예비를 잠근다");
            }
        }
        bool falling = _net.AirRelease <= 0 && h2 > 0 && h1 - h2 < -h2 * 0.001;
        bool want = n > 0 && sum / n < (_net.AirRelease > 0 ? 96 : 90) && !leaking && _net.AirReserve > 0 && _tick >= _refillHold && !falling;
        int inside = L.Rooms.Sum(rm => rm.Cells.Length);
        double rate = want ? inside * 0.002 : 0;
        if ((rate > 0) != (_net.AirRelease > 0))
            Say(sim, rate > 0 ? $"예비 공기를 푼다 — 배 기압 {sum / n:0}kPa · 남은 예비 {_net.AirReserve / Math.Max(1, inside * Field.CellVolume * 101_325 / (Field.R * 293.15)) * 100:0}%" : "예비 공기를 잠갔다");
        if (rate > 0 && _net.AirRelease <= 0) _refillSince = _tick;
        _net.AirRelease = rate;
    }

    /// <summary>에어락을 막 순환했다 (10분 안 · 바깥 문이 있는 방).</summary>
    private bool Cycling(int r)
    {
        if (_tick - LastCycle > Time.Minutes(10)) return false;
        foreach (int d in _roomDoors[r]) if (L.Doors[d].External) return true;
        return false;
    }

    /// <summary>에어락: 바깥 문이 있는 방 — 안쪽 문이 모두 닫혀 있으면 배와 떨어져 있다.</summary>
    private bool IsolatedAirlock(int r)
    {
        bool external = false;
        foreach (int d in _roomDoors[r])
        {
            if (L.Doors[d].External) external = true;
            else if (F.DoorOpen[d] > 0) return false;
        }
        return external;
    }

    /// <summary>방 외판 바깥 칸 가운데 방 한가운데에서 가장 가까운 것 (없으면 -1 — 외판에 닿지 않은 방).</summary>
    private int OutsideOf(int r)
    {
        if (_mind == null) return -1;
        int best = -1, bd = int.MaxValue, center = _roomVent[r];
        Span<int> nb = stackalloc int[4], nb2 = stackalloc int[4];
        foreach (int c in L.Rooms[r].Cells)
        {
            L.Neighbors(c, nb);
            foreach (int w in nb)
            {
                if (w < 0 || L.Kind[w] != CellKind.Wall || !L.Hull[w]) continue;
                L.Neighbors(w, nb2);
                foreach (int o in nb2)
                    if (_mind.HullOut(o) && _mind.Dist(o, center) < bd) { bd = _mind.Dist(o, center); best = o; }
            }
        }
        return best;
    }

    private bool Losing(int r)
    {
        if (_reads < 7) return false;
        if (_reads - GaveAirAt[r] < 6) return false; // 지난 1분 안에 더 낮은 방에 공기를 줬다 — 줄어든 것은 옮겨 간 것

        float now = RoomMoles[r * 6 + _reads % 6], prev = RoomMoles[r * 6 + (_reads + 5) % 6], old = RoomMoles[r * 6 + (_reads + 1) % 6];
        if (prev <= 0 || old <= 0) return false;
        bool fast = (prev - now) / prev > 0.02f && !Moved(r, 1);
        bool slow = (old - now) / old > 0.04f && now < prev && !Moved(r, 5);
        return fast || slow;
    }

    /// <summary>
    /// 옮겨 갔을 뿐인가: 그 방이 back번 읽는 사이 잃은 기체를 배 전체(읽히는 방들)는 30%도 잃지 않았다 — 열린 문으로 옆 방에 간 것이지 새는 것이 아니다.
    /// (큰 사고 뒤 배 전체가 80kPa 아래로 낮아진 채 문을 여닫으면, 이것을 샘으로 보고 방마다 봉쇄 · 해제를 몇 분마다 되풀이했다.)
    /// 또는 다른 방들이 잃은 만큼의 70% 넘게 받았다 (에어락이 새는 중에 로봇이 그 문을 열자 옆 방이 '샌다'가 됐다).
    /// </summary>
    private bool Moved(int r, int back)
    {
        if (_reads < back + 1) return false;
        int now = _reads % 6, then = (_reads + 6 - back) % 6;
        float lost = RoomMoles[r * 6 + then] - RoomMoles[r * 6 + now];
        if (lost <= 0) return false;
        float shipThen = DoorMoles[then], shipNow = DoorMoles[now], gained = Math.Max(0, DoorMoles[now] - DoorMoles[then]);
        for (int k = 0; k < L.Rooms.Count; k++)
            if (RoomSeen[k] != 0)
            {
                shipThen += RoomMoles[k * 6 + then]; shipNow += RoomMoles[k * 6 + now];
                if (k != r) gained += Math.Max(0, RoomMoles[k * 6 + now] - RoomMoles[k * 6 + then]);
            }
        // 배 전체가 거의 잃지 않았거나 · 다른 방들이 그만큼 받았다 (다른 곳이 새는 중에도 — 새는 방의 문을 열면 옆 방 공기가 그리로 몰린다)
        return shipThen - shipNow < 0.3f * lost || gained >= 0.7f * lost;
    }

    /// <summary>그 방 둘레 구멍의 원인 (원인 사슬용 — 주컴퓨터가 구멍 자리를 아는 것은 아니다).</summary>
    private int BreachCause(int room)
    {
        if (_st == null) return 0;
        Span<int> nb = stackalloc int[4];
        foreach (int c in L.Rooms[room].Cells)
        {
            if (F.Hole[c] > 0 && _st.DamageCause[c] != 0) return _st.DamageCause[c];   // 바닥 · 천장 판이 뚫린 칸 (④)
            L.Neighbors(c, nb);
            foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Wall && F.Hole[j] > 0 && _st.DamageCause[j] != 0) return _st.DamageCause[j];
        }
        return 0;
    }

    /// <summary>공기가 오가는 문(열렸거나 · 닫힌 문의 틈)으로 이어진, 함께 새는 방들 가운데 가장 작은 방 번호 — 같은 값이면 한 묶음.</summary>
    private int AirGroup(int room, HashSet<int> members)
    {
        int best = room;
        var seen = new HashSet<int> { room };
        var q = new Queue<int>(); q.Enqueue(room);
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0)
        {
            int r = q.Dequeue();
            best = Math.Min(best, r);
            foreach (int d in _roomDoors[r])
            {
                if (F.DoorOpen[d] <= 0f && !F.DoorGapOpen(d)) continue;
                L.Neighbors(L.Doors[d].Cell, nb);
                foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && members.Contains(L.Room[j]) && seen.Add(L.Room[j])) q.Enqueue(L.Room[j]);
            }
        }
        return best;
    }

    /// <summary>샘의 원인: 그 방 둘레 구멍 — 없으면 새는 문(틈) 너머의 원인 (옆 방을 뚫은 것).</summary>
    private int LeakCause(int room)
    {
        int c = BreachCause(room);
        if (c != 0 || _st == null) return c;
        int spot = LeakSpot(room);
        return spot >= 0 && L.Kind[spot] == CellKind.Door ? _st.LowSideCause(spot) : 0;
    }

    /// <summary>방이나 문으로 이어진 옆 방에서 타는 불의 원인 (일산화탄소 · 더위 · 나쁜 공기 · 연기로 읽힌 불을 그 불에 잇는다) — 없으면 아는 가장 최근의 불 · 그것도 없으면 0.</summary>
    private int NearFireCause(int room)
    {
        if (_chem == null || room < 0) return 0;
        foreach (int c in L.Rooms[room].Cells) if (_chem.Burning[c] != 0 && _chem.FireCause[c] != 0) return _chem.FireCause[c];
        Span<int> nb = stackalloc int[4];
        foreach (int d in _roomDoors[room])
        {
            L.Neighbors(L.Doors[d].Cell, nb);
            foreach (int j in nb)
                if (j >= 0 && L.Room[j] >= 0 && L.Room[j] != room)
                    foreach (int c in L.Rooms[L.Room[j]].Cells) if (_chem.Burning[c] != 0 && _chem.FireCause[c] != 0) return _chem.FireCause[c];
        }
        // 타는 곳이 없으면: 주컴퓨터가 아는 가장 최근의 불 (아직이거나 10분 안에 끝난) — 그 연기 · 열기가 퍼진 것
        int best = 0; long since = long.MinValue;
        foreach (var x in Incidents)
            if (x.Kind == IncidentKind.Fire && x.Cause != 0 && (x.Active != 0 || _tick - x.Ended < Time.Minutes(10)) && x.Since > since) { since = x.Since; best = x.Cause; }
        return best;
    }

    private int FireCause(int cell)
    {
        if (_chem == null) return 0;
        if (_chem.FireCause[cell] != 0) return _chem.FireCause[cell];
        int r = L.Room[cell];
        if (r >= 0) foreach (int c in L.Rooms[r].Cells) if (_chem.Burning[c] != 0) return _chem.FireCause[c];
        return 0;
    }

    /// <summary>독한 기체의 원인: 그 방에서 냉매가 새는 장치 (없으면 0).</summary>
    private int ToxicCause(int room)
    {
        if (_net == null) return 0;
        foreach (var d in _net.Devices) if (d.Room == room && _net.SealLeakCause[d.Id] != 0) return _net.SealLeakCause[d.Id];
        return 0;
    }

    private void End(Sim sim, ref Incident x)
    {
        Say(sim, $"{RoomName(x.Room)}: {KindName(x.Kind)} — 끝");
        if (x.Kind is IncidentKind.Fire or IncidentKind.CO or IncidentKind.Toxic && _net != null && x.Room >= 0 && Find(IncidentKind.Fire, x.Room) < 0 && Find(IncidentKind.CO, x.Room) < 0 && Find(IncidentKind.Toxic, x.Room) < 0)
        {
            _net.SetDamper(x.Room, true);
            if (_net.Exhaust[x.Room] != 0) { _net.SetExhaust(x.Room, false); Say(sim, $"{RoomName(x.Room)} 정화 배기를 멈추고 통풍구를 열었다"); }
        }
        foreach (int rb in new[] { x.RobotA, x.RobotB })
            if (rb >= 0 && _mind != null) _mind.Agents[rb].C.AssignedUntil = 0;
    }

    // ───────────────────────── 하기 ─────────────────────────

    private static bool Dangerous(IncidentKind k) => k is IncidentKind.Fire or IncidentKind.Leak or IncidentKind.Vacuum or IncidentKind.CO or IncidentKind.BadAir or IncidentKind.Toxic;

    private void Act(Sim sim)
    {
        long t = sim.Tick;
        if (t % ReadEvery == 0) WatchOuterDoors(sim);
        int nOrder = Triage(sim);    // 맡기는 차례: 급한 것부터 (사건이 몰렸는지도 본다)
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
            // 불이 난 방의 조리대 전원을 내린다 (켜 둔 조리대가 불씨면 끄지 않는 한 다시 붙는다)
            if (x.Kind == IncidentKind.Fire && x.Room >= 0 && _net != null)
                foreach (var dv in _net.Devices)
                    if (dv.Def.Role == "stove" && _net.On[dv.Id] != 0 && L.Room[dv.Cell] == x.Room)
                    {
                        int cut = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {RoomName(x.Room)} {dv.Name} 전원을 내렸다", sim.Tick, dv.Cell, _net.OnCause[dv.Id] != 0 ? _net.OnCause[dv.Id] : x.Cause);
                        sim.Facts.Emit(sim.Tick, FactKind.Switched, dv.Cell, 0, cut, -1, dv.Id);
                        _net.SetOn(dv.Id, false); StovesCut++; Say(sim, $"{RoomName(x.Room)} {dv.Name} 전원을 내렸다 — 불이 났다");
                    }
            // 연기 · 독한 공기가 덕트로 번지지 않게 그 방 통풍구를 닫는다
            if (x.Kind is IncidentKind.Fire or IncidentKind.CO or IncidentKind.Toxic && x.Room >= 0 && _net != null && _net.Damper[x.Room] != 0) { _net.SetDamper(x.Room, false); Say(sim, $"{RoomName(x.Room)} 통풍구를 닫았다"); }
            // 독한 기체는 그 방 덕트를 정화기로만 돌려 그 자리에서 거른다 (불이 난 방은 아니다 — 공기를 더 대 주지 않게)
            if (x.Kind is IncidentKind.CO or IncidentKind.Toxic && x.Room >= 0 && _net != null && _net.Exhaust[x.Room] == 0 && Find(IncidentKind.Fire, x.Room) < 0)
            {
                _net.SetExhaust(x.Room, true);
                Exhausts++;
                Say(sim, _net.ScrubberWorking() ? $"{RoomName(x.Room)} 정화 배기 — 덕트를 정화기로만 돌린다" : $"{RoomName(x.Room)} 정화 배기 — 정화기가 서 있어 걸러지지 않는다");
            }
            // 봉쇄
            if (Policy.AutoSeal != 0 && x.Kind is IncidentKind.Fire or IncidentKind.Leak or IncidentKind.Vacuum or IncidentKind.Toxic && x.Room >= 0) SealRoom(sim, ref x);
            Incidents[i] = x;
        }
        // 로봇에게 맡긴다 — 급한 것부터 (같은 급이면 먼저 난 것): 불 · 쓰러진 사람이 먼저 로봇을 얻는다
        for (int k = 0; k < nOrder; k++)
        {
            int i = _order[k];
            var x = Incidents[i];
            if (x.Active == 0) continue;
            AssignRobots(sim, ref x);
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
        IncidentKind.Toxic => new Belief { Kind = BeliefKind.BadAir, Cell = x.Cell, Subject = -1, Conf = Math.Clamp(x.Level / 300f, 0.5f, 1f), Cause = x.Cause },
        IncidentKind.Hot => new Belief { Kind = BeliefKind.Hot, Cell = x.Cell, Subject = -1, Conf = 0.8f, Cause = x.Cause },
        IncidentKind.PersonDown => new Belief { Kind = BeliefKind.Injured, Cell = x.Cell, Subject = x.Subject, Conf = 1f, Cause = x.Cause },
        IncidentKind.DeviceBroken => new Belief { Kind = BeliefKind.Broken, Cell = x.Cell, Subject = x.Subject, Conf = 1f, Cause = x.Cause },
        IncidentKind.ElectricFault => new Belief { Kind = BeliefKind.Spark, Cell = x.Cell, Subject = -1, Conf = 1f, Cause = x.Cause },
        IncidentKind.Strain => new Belief { Kind = BeliefKind.Strain, Cell = x.Cell, Subject = -1, Conf = Math.Clamp(x.Level, 0.6f, 1f), Cause = x.Cause },
        IncidentKind.DoorJam => new Belief { Kind = BeliefKind.Strain, Cell = x.Cell, Subject = -1, Conf = 1f, Cause = x.Cause },
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
        ReadSnags(sim, ref x);
        switch (x.Kind)
        {
            case IncidentKind.Fire: g = GoalKind.FightFire; target = x.Cell; drone = true; break;
            case IncidentKind.Leak: case IncidentKind.Vacuum:
            {
                // ⑩ 판단 엔진: 기본 절차서 — 안에서 막기 · 바깥에서 막기 (빠진 방은 바깥부터 — 문은 기압 차로 안 열린다 · 열면 배 공기까지 빠진다 · 비워진 채 닫아 둔 방은 바깥만 ·
                //   샘은 안부터 — 안에서 못 찾으면 번갈아 외판을 바깥에서 둘러본다). 막힌 까닭이 그대로인 방법은 다시 고르지 않는다 — 다 막히면 지어낸다.
                if (HasPlan(x.Id)) return;   // 지어낸 계획이 맡고 있다
                if (x.Kind == IncidentKind.Vacuum && Wrecked(x.Room)) return;   // 벽이 못 버텨 닫아 둔 방 — 막아도 채울 수 없다 (제대로 고칠 때까지)
                if (x.Kind == IncidentKind.Vacuum && WallRisk(x.Room, null) is double wr && wr >= WreckAt) { HoldWreck(sim, x.Room, wr, x.Cause); return; }   // 막기 전에 본다 — 막아도 채울 수 없으면 막지 않는다
                if (x.Kind == IncidentKind.Vacuum && PlanRobot(sim, x) < 0 && _mind.Agents.Any(a => !a.IsPerson && a.C.Up != 0 && a.C.Kind != AgentKind.Drone) == false)
                {
                    // 로봇이 없는 배: 기본 절차서의 방법을 해 볼 몸이 없다 — 사람에게 부탁해 그 사람과 함께 궁리한다 (풀이기는 안에서 막을 수 있으면 그것을 고른다)
                    int xid0 = x.Id;
                    if (!Attempts.Any(a => a.Incident == xid0 && a.Act == 254 && StillBlocked(a))) { int pb = PlanPerson(sim, ref x); if (pb >= 0) Devise(sim, ref x, pb); }
                    return;
                }
                // 판단 엔진 (바): 매뉴얼에 이 꼴의 방법이 있고 기본 절차서가 막힐 것이 센서로 보이면 — 해 보지 않고 바로 그 방법으로 궁리한다
                int xidM = x.Id;
                if (ManualFor(sim, x) is int mk && mk != 0 && !Attempts.Any(a => a.Incident == xidM && a.Act is 255 or 254))
                {
                    int rb = PlanRobot(sim, x, take: true);
                    bool robots = _mind.Agents.Any(a => !a.IsPerson && a.C.Up != 0 && a.C.Kind != AgentKind.Drone);
                    if (rb < 0 && !robots) rb = PlanPerson(sim, ref x);   // 사람에게 부탁하는 것은 로봇이 없는 배에서만 (로봇이 있으면 풀려날 때까지 기다린다)
                    if (rb >= 0) { Devise(sim, ref x, rb, mk); if (HasPlan(x.Id)) return; }
                    else if (robots) return;
                }
                int o = OutsideOf(x.Room);
                bool outFirst = x.Kind == IncidentKind.Vacuum ? x.Tries % 2 == 0 || EmptyHeld(x.Room) : x.Tries % 2 == 1;
                byte pick = 0;
                foreach (byte m in outFirst ? new byte[] { 2, 1 } : new byte[] { 1, 2 })
                {
                    if (m == 2 && o < 0 || m == 1 && x.Kind == IncidentKind.Vacuum && EmptyHeld(x.Room)) continue;
                    if (MethodBlocked(x.Id, m, out _)) continue;
                    pick = m; break;
                }
                if (pick == 0)
                {
                    int xid = x.Id;
                    bool any = Attempts.Any(a => a.Incident == xid && a.Act == 255);   // 해 본 방법이 하나도 없으면 (닫아 둔 방에 외판도 없다) — 바로 궁리한다
                    if (!Attempts.Any(a => a.Incident == xid && a.Act == 254 && StillBlocked(a)))
                    {
                        int rb = PlanRobot(sim, x);
                        if (rb < 0) rb = PlanPerson(sim, ref x);   // 로봇이 없으면 사람에게 부탁한다 (함장 방침 · 받을지는 사람마다)
                        if (rb >= 0) Devise(sim, ref x, rb);
                        else if (!any) return;
                    }
                    return;
                }
                if (pick == 2) { g = GoalKind.Investigate; target = o; drone = false; }
                else { g = GoalKind.Investigate; target = x.Cell; drone = true; }
                break;
            }
            case IncidentKind.PersonDown: g = GoalKind.Rescue; target = x.Subject; drone = false; break;
            case IncidentKind.Injured: g = GoalKind.Treat; target = x.Subject; drone = false; break;
            case IncidentKind.DeviceBroken: g = GoalKind.Repair; target = x.Subject; drone = false; break;
            case IncidentKind.ElectricFault when _net != null && _net.IsStuckFault(x.Subject):
                // 차단기가 눌어붙었다 — 멀리서는 끊을 수 없다: 로봇이 배전반에서 손으로 끊는다
                g = GoalKind.Repair; target = _net.Feeders[x.Subject].Panel; drone = false; break;
            case IncidentKind.ElectricFault: g = GoalKind.Investigate; target = x.Cell; drone = true; break;
            case IncidentKind.Strain: g = GoalKind.Brace; target = x.Cell; drone = false; break;
            case IncidentKind.DoorJam: g = GoalKind.Brace; target = x.Cell; drone = false; break;
            case IncidentKind.SensorLost: g = GoalKind.Investigate; target = x.Cell; drone = false; break; // 끊긴 자리로 가서 잇는다 (가는 길에 그 방도 본다)
            case IncidentKind.LineCut: g = GoalKind.Investigate; target = x.Cell; drone = false; break;    // 끊긴 전선 · 관 자리로 가서 잇고 죈다
            case IncidentKind.PipeHunt: g = GoalKind.Investigate; target = x.Cell; drone = false; break;   // 새는 관을 따라 칸마다 살핀다
            case IncidentKind.PowerLost: g = GoalKind.Rewire; target = x.Room; drone = false; break;     // ⑥ 옆 방에서 연장선을 끌어온다
            default: return;
        }
        int need = x.Kind == IncidentKind.Fire ? 2 : 1;
        bool Busy(int rb) => rb >= 0 && _mind.Agents[rb].C.Up != 0 && _mind.Agents[rb].C.AssignedUntil > sim.Tick;
        int have = (Busy(x.RobotA) ? 1 : 0) + (Busy(x.RobotB) ? 1 : 0);
        while (have < need)
        {
            int best = -1, bd = int.MaxValue, bestFrom = -1;
            foreach (var a in _mind.Agents)
            {
                if (a.IsPerson || a.C.Up == 0 || a.C.Battery < 0.2f || (!drone && a.C.Kind == AgentKind.Drone)) continue;
                int from = -1;
                if (a.C.AssignedUntil > sim.Tick && !Root(x) && a.C.AssignedGoal is not (GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock)) // 일상 일(⑪)은 놓고 온다
                {
                    // 이미 맡은 일이 있다 (불의 뿌리 — 눌어붙은 합선 — 는 하던 일을 놓고 간다) · 급한 일(쓰러진 사람 · 불 · 공기 빠짐 · 샘)에는 덜 급한 사건 일을 하던 로봇도 데려온다 (쉬는 로봇이 없을 때만)
                    from = Rank(x.Kind) <= 3 ? OwnerOf(sim, a.Id) : -1;
                    if (from < 0 || Rank(Incidents[from].Kind) <= Rank(x.Kind)) continue;
                }
                if (a.Id == x.RobotA && Busy(x.RobotA) || a.Id == x.RobotB && Busy(x.RobotB)) continue; // 이 사건에 가 있는 로봇 (일을 마쳤으면 다시 보낼 수 있다)
                int d = _mind.Dist(a.C.Cell, x.Cell >= 0 ? x.Cell : a.C.Cell) + (from >= 0 ? 100000 : 0);
                if (d < bd) { bd = d; best = a.Id; bestFrom = from; }
            }
            if (best < 0) break;
            if (bestFrom >= 0)
            {
                var y = Incidents[bestFrom];
                if (y.RobotA == best) y.RobotA = -1; else y.RobotB = -1;
                Incidents[bestFrom] = y;
                Preempts++;
                Say(sim, $"{_mind.NameOf(best)}: {RoomName(y.Room)} {KindName(y.Kind)} 일은 미루고 → {RoomName(x.Room)} {KindName(x.Kind)} (더 급하다)");
            }
            var belief = g == GoalKind.Repair && x.Kind == IncidentKind.ElectricFault && _net != null
                ? new Belief { Kind = BeliefKind.Broken, Cell = _net.Devices[target].Cell, Subject = target, Conf = 1f, Cause = x.Cause }
                : BeliefOf(x);
            _mind.Assign(sim, best, g, target, belief, sim.Tick + Time.Minutes(30));
            if (!Busy(x.RobotA)) x.RobotA = best; else x.RobotB = best;
            x.SentAt = sim.Tick;
            have++;
            x.Tries++;
            Assignments++;
            Say(sim, $"{_mind.NameOf(best)} → {RoomName(x.Room)} {KindName(x.Kind)}");
            sim.Facts.Emit(sim.Tick, FactKind.ComputerAssign, x.Cell, 0, x.Cause, best, (int)x.Kind);
        }
        // 로봇이 모자라면 사람에게 부탁한다 (함장 방침 — 기본은 로봇이 하나도 없을 때 1분 기다린 뒤)
        if (have < need && Policy.AskPeople != 0 && (Policy.AskPeople == 2 || have == 0)) AskPerson(sim, ref x, g, target);
    }

    /// <summary>
    /// ⑩ 판단 엔진: 맡긴 로봇이 막혔나 — 로봇이 남긴 막힘(까닭 · 자리 · 대상)을 사건에 옮겨 적고 일지에 쓴다 (보낸 뒤의 문 · 길 · 물건 막힘만).
    /// 같은 까닭으로 막힌 채 다시 보내면 그 번을 센다 — 대안을 고르는 바탕 (docs/ENGINE.md 판단 엔진).
    /// </summary>
    private void ReadSnags(Sim sim, ref Incident x)
    {
        if (_mind == null) return;
        foreach (int rb in new[] { x.RobotA, x.RobotB })
        {
            if (rb < 0 || rb >= _mind.Agents.Count) continue;
            ref var c = ref _mind.Agents[rb].C;
            if (!Mind.Telling(c.Snag) || c.SnagTick < x.SentAt || c.SnagTick <= x.SnagTick) continue;
            bool again = x.Snag == c.Snag && x.SnagSubject == c.SnagSubject;
            x.Snag = c.Snag; x.SnagGoal = c.SnagGoal; x.SnagCell = c.SnagCell; x.SnagSubject = c.SnagSubject; x.SnagTick = c.SnagTick;
            if (x.Kind is IncidentKind.Vacuum or IncidentKind.Leak && !HasPlan(x.Id))   // 기본 절차서의 방법(안에서 · 바깥에서)이 막혔다 — 까닭과 함께 적는다 (막힐 때 그 로봇이 하던 목표의 대상으로 가른다)
                NoteTried(x.Id, (byte)(c.Snag == SnagKind.NoWayOut || c.SnagTarget >= 0 && c.SnagTarget < L.N && L.Kind[c.SnagTarget] == CellKind.Outside ? 2 : 1), 255, -1, c.Snag, c.SnagSubject, c.SnagTick);
            if (again) { if (x.Snagged < 255) x.Snagged++; continue; }
            x.Snagged = 1; SnagsRead++;
            Say(sim, $"{_mind.NameOf(rb)}: {RoomName(x.Room)} {KindName(x.Kind)} 일이 막혔다 — {Mind.SnagText(c.Snag)}");
        }
    }

    /// <summary>지어낸 계획을 맡을 로봇: 멈추지 않았고 배터리가 있는 로봇 가운데 이 사건에 가 있거나 · 쉬고 있거나 · 일상 일을 하는 가장 가까운 것 (드론은 아니다).</summary>
    private int PlanRobot(Sim sim, in Incident x, bool take = false)
    {
        if (_mind == null) return -1;
        int best = -1, bd = int.MaxValue, bestFrom = -1;
        foreach (var a in _mind.Agents)
        {
            if (a.IsPerson || a.C.Up == 0 || a.C.Battery < 0.2f || a.C.Kind == AgentKind.Drone) continue;
            bool mine = a.Id == x.RobotA || a.Id == x.RobotB;
            int from = -1;
            if (!mine && a.C.AssignedUntil > sim.Tick && a.C.AssignedGoal is not (GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock))
            {
                // 덜 급한 사건 일을 하던 로봇은 데려올 수 있다 (급한 차례 — 로봇을 맡기는 때와 같은 셈 · take일 때만)
                from = take && Rank(x.Kind) <= 3 ? OwnerOf(sim, a.Id) : -1;
                if (from < 0 || Rank(Incidents[from].Kind) <= Rank(x.Kind)) continue;
            }
            if (PlanSteps.Any(s => s.Robot == a.Id)) continue;
            int d = _mind.Dist(a.C.Cell, x.Cell >= 0 ? x.Cell : a.C.Cell) - (mine ? 1000 : 0) + (from >= 0 ? 100000 : 0);
            if (d < bd) { bd = d; best = a.Id; bestFrom = from; }
        }
        if (best >= 0 && bestFrom >= 0)
        {
            var y = Incidents[bestFrom];
            if (y.RobotA == best) y.RobotA = -1; else y.RobotB = -1;
            Incidents[bestFrom] = y;
            Preempts++;
            Say(sim, $"{_mind.NameOf(best)}: {RoomName(y.Room)} {KindName(y.Kind)} 일은 미루고 → {RoomName(x.Room)} {KindName(x.Kind)} (더 급하다)");
        }
        return best;
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
            // 사람이 안에 있다: 기다릴까 · 지금 닫을까 — 세계를 복제해 2분 앞을 돌려 보고 고른다 (한 번만 · 사건이 몰렸거나 느려졌으면 그려 보지 않고 방침대로)
            int xr = x.Room; long xs = x.Since;
            if (peopleIn && !waitOver && !Imagining && Overloaded == 0 && Throttled == 0 && x.Clear == 0 && !Imaginations.Any(v => v.Room == xr && v.Tick >= Math.Min(xs, t - Time.Minutes(5))))
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
            UnplugForSeal(sim, x.Room, cause);   // ⑥ 문틀의 연장선은 뽑는다 (그 방은 다시 어둡다)
        }
        // 열린 문은 지나는 이가 없을 때 다시 닫는다 (로봇 · 사람은 지나다닐 수 있다) — 문 칸에 선 이 · 문을 기다리는 이 · 곁 2칸에서 걷는 이만 (문 대각선 충전독에서 충전하는 로봇이 봉쇄한 방 문을 내내 열어 두던 결함)
        if (t % 10 != 0) return;
        foreach (int d in _roomDoors[x.Room])
        {
            if (F.DoorOpen[d] <= 0 || _st.DoorTarget[d] <= 0) continue;
            int dc = L.Doors[d].Cell;
            bool near = false;
            foreach (var a in _mind.Agents) if (a.C.Up != 0 && (a.C.Cell == dc || a.C.WaitDoor == d || _mind.Dist(a.C.Cell, dc) <= 2 && _mind.Walking(a))) { near = true; break; }
            if (!near) _st.RequestDoor(d, 0);
        }
    }

    /// <summary>바깥문 지키기: 열려 있는데(열라는 요청이 남았는데) 곁 3칸에 아무도 없고 · 열어 달라고 기다리는 이도 · 선외에 나간 이도 없으면 닫는다 —
    /// 열린 바깥문은 에어락 안쪽 문틈으로 배 공기를 조금씩 내보낸다 (밤새면 배 하나가 빈다).</summary>
    private void WatchOuterDoors(Sim sim)
    {
        if (_st == null || _mind == null) return;
        foreach (var d in L.Doors)
        {
            if (!d.External || F.DoorOpen[d.Id] <= 0 && _st.DoorTarget[d.Id] <= 0 || _st.DoorJam[d.Id] != 0) continue;
            bool someone = false;
            foreach (var a in _mind.Agents)
                if (a.C.WaitDoor == d.Id || _mind.Dist(a.C.Cell, d.Cell) <= 3 || L.Kind[a.C.Cell] == CellKind.Outside) { someone = true; break; }
            if (someone) continue;
            _st.RequestDoor(d.Id, 0);
            int room = -1;
            Span<int> nb = stackalloc int[4];
            L.Neighbors(d.Cell, nb);
            foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) room = L.Room[j];
            Say(sim, $"{RoomName(room)} 바깥문이 열린 채 아무도 없어 닫았다");
            OuterClosed++;
        }
    }

    // ───────────────────────── 상상 (복제 세계) ─────────────────────────

    /// <summary>세계를 복제해 두 안(기다림 · 지금 봉쇄)을 각각 2분 돌려 본다. 복제 안의 주컴퓨터는 상상하지 않고 규칙대로만 한다.</summary>
    private (Imagined wait, Imagined now) Imagine(Sim sim, int room)
    {
        Imagined Run(byte option, Sim w)
        {
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
        // 두 복제 세계는 서로 닿지 않는다 — 함께 돌린다 (복제는 차례로: 원래 세계를 동시에 읽지 않게). 결과는 순서와 상관없이 같다.
        var w0 = sim.Clone(); var w1 = sim.Clone();
        Imagined a = default, b = default;
        if (Par.Threads <= 1) { a = Run(0, w0); b = Run(1, w1); }
        else Parallel.Invoke(() => a = Run(0, w0), () => b = Run(1, w1));
        return (a, b);
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
                if (_net.Breaker[fd] == 0 && _net.FaultCell[fd] < 0 && t - BreakerResetAt[fd] > Time.Minutes(10) && !_net.PathFaulty(fd)) // 길이 아직 젖었거나 탔으면 올려도 또 합선 — 닦은 뒤에
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
            int shed = 0;
            foreach (var d in order)
            {
                if (gap <= 0) break;
                _net.SetOn(d.Id, false); Shed[d.Id] = 1; gap -= d.Def.Kw; Sheds++; shed++;
                sim.Facts.Emit(t, FactKind.LoadShed, d.Cell, (float)d.Def.Kw, 0, -1, d.Id);
            }
            if (shed > 0) Say(sim, "전력이 모자라 덜 중요한 장치를 껐다");   // 끌 것이 없으면 말하지 않는다 (전지가 바닥난 배에서 틱마다 되풀이했다)
        }
        else if (_net.Supplied - _net.Demanded > 5)
        {
            foreach (var d in _net.Devices)
                if (Shed[d.Id] != 0 && _net.Supplied - _net.Demanded > d.Def.Kw + 2) { _net.SetOn(d.Id, true); Shed[d.Id] = 0; break; } // 하나씩
        }
    }

    // ───────────────────────── 일지 · 아침 방송 ─────────────────────────

    private static string DaysText(double d) => double.IsInfinity(d) ? "줄지 않음" : d > 999 ? "999일 넘게" : $"{d:0.#}일 치";

    /// <summary>배 상태 한 줄 (주컴퓨터가 읽은 것): 산소 · 전력 · 식량 · 물.</summary>
    public string ShipLine(Sim sim, bool power)
    {
        var parts = new List<string> { $"산소 {ShipO2():0.0}kPa" };
        if (power && _net != null) parts.Add($"전력 {_net.Supplied:0}kW 내고 {_net.Demanded:0}kW 씀");
        if (sim.Find<Work>() is Work w && sim.Book.Started)
        {
            parts.Add($"식량 {DaysText(w.DaysLeft(sim, (int)Res.Food).days)}");
            parts.Add($"물 {DaysText(w.DaysLeft(sim, (int)Res.Water).days)}");
        }
        return string.Join(" · ", parts);
    }

    /// <summary>못 보는 방 (전원 · 센서 선) · 무선으로만 보는 방 (선이 끊겨 느리게) — 없으면 빈 줄.</summary>
    public string BlindLine()
    {
        var blind = new List<string>(); var radio = new List<string>();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (L.Rooms[r].Cells.Length == 0) continue;
            if (RoomSeen[r] == 0) blind.Add($"{RoomName(r)}({(_net != null && !_net.RoomPowered(r) ? "전원" : "센서 선")})");
            else if (RoomSeen[r] == 2) radio.Add(RoomName(r));
        }
        var parts = new List<string>();
        if (blind.Count > 0) parts.Add("못 보는 방: " + string.Join(" · ", blind));
        if (radio.Count > 0) parts.Add("무선으로만 보는 방: " + string.Join(" · ", radio));
        return string.Join(" / ", parts);
    }

    /// <summary>
    /// 일지 한 장 — 지난 일지 뒤로 주컴퓨터가 안 것만: 사건(때 · 방 · 몇 분 만에 끝났나) · 쓰러진 사람 · 숨을 거둔 사람 ·
    /// 배 산소 · 전력 · 식량 · 물 며칠 치 · 못 보는 방 · 로봇에게 맡긴 일. 큰 일이 없으면 그렇다고 적는다.
    /// </summary>
    private void WriteJournal(Sim sim)
    {
        long now = sim.Tick;
        var lines = new List<string>();
        var fresh = Incidents.Where(x => x.Since >= LastJournal && x.Since < now).OrderBy(x => x.Since).ToList();
        var carried = Incidents.Where(x => x.Since < LastJournal && x.Active != 0).ToList();
        var downs = fresh.Where(x => x.Kind == IncidentKind.PersonDown).Select(x => x.Subject).Distinct().ToList();
        var dead = new List<string>();
        if (_mind != null && _life != null)
            foreach (var a in _mind.Agents)
            {
                if (!a.IsPerson || ReportedDead.Contains(a.C.Body)) continue;
                var b = _life.Bodies[a.C.Body];
                int r = _mind.RoomOf(b.Cell);
                if (b.Alive != 0 || r < 0 || RoomSeen[r] == 0) continue; // 사람 표가 읽히는 방에서만 안다
                dead.Add(_mind.NameOf(a.Id)); ReportedDead.Add(a.C.Body);
            }
        // 같은 뿌리에서 번진 사건(불 → 일산화탄소 · 더위 · 옆 방의 불)은 한 줄로
        var groups = new List<List<Incident>>();
        var byRoot = new Dictionary<int, List<Incident>>();
        foreach (var x in fresh)
        {
            if (x.Kind == IncidentKind.PersonDown) continue;
            int root = x.Cause != 0 ? sim.Causes.Root(x.Cause) : 0;
            if (root != 0 && byRoot.TryGetValue(root, out var g)) { g.Add(x); continue; }
            var ng = new List<Incident> { x }; groups.Add(ng);
            if (root != 0) byRoot[root] = ng;
        }
        int count = groups.Count + (downs.Count > 0 ? 1 : 0);
        if (count == 0 && carried.Count == 0 && dead.Count == 0) lines.Add(RationFoodOn != 0 || RationWaterOn != 0 ? "큰 사고 없이 지나갔다 — 배급 중" : "큰 일 없이 지나갔다");
        else
        {
            var heads = groups.Select(g => $"{RoomName(g[0].Room)} {KindName(g[0].Kind)}").Distinct().Take(3).ToList();
            if (downs.Count > 0 && heads.Count < 3) heads.Add($"쓰러진 사람 {downs.Count}명");
            lines.Add(count > 0 ? $"사건 {count}건 — {string.Join(" · ", heads)}" : "새 사건 없이 지난 일이 이어진다");
        }
        string Clock(long t) { long m = t % Time.TicksPerDay * 60 / Time.TicksPerHour; return $"{m / 60:00}:{m % 60:00}"; }
        int shown = 0;
        foreach (var g in groups)
        {
            if (shown++ == 8) { lines.Add($"그 밖에 {groups.Count - 8}건"); break; }
            var head = g.Where(x => x.Kind == g[0].Kind).ToList();   // 처음 안 일 (불) — 나머지는 거기서 번진 것
            var still = g.Where(x => x.Active != 0 && x.Kind != g[0].Kind).Select(x => KindName(x.Kind)).Distinct().ToList();
            string took = head.Any(x => x.Active != 0) ? "아직 이어진다"
                : $"{Math.Max(1, (head.Max(x => x.Ended) - g[0].Since) * 60 / Time.TicksPerHour)}분 만에 끝났다" + (still.Count > 0 ? $" · 아직: {string.Join(" · ", still)}" : "");
            string rooms = string.Join(" · ", g.Select(x => RoomName(x.Room)).Distinct());
            string kinds = string.Join(" · ", g.Select(x => KindName(x.Kind)).Distinct());
            lines.Add($"{Clock(g[0].Since)} {rooms}: {kinds} — {took}");
        }
        foreach (var x in carried.Take(4)) lines.Add($"지난 일지부터 {RoomName(x.Room)}: {KindName(x.Kind)} — 아직 이어진다");
        if (downs.Count > 0 && _mind != null)
            lines.Add($"쓰러진 사람 {downs.Count}명: " + string.Join(" · ", downs.Select(body => _mind.Agents.FirstOrDefault(a => a.IsPerson && a.C.Body == body) is { } a ? _mind.NameOf(a.Id) : "?")));
        if (dead.Count > 0) lines.Add("숨을 거둔 사람: " + string.Join(" · ", dead));
        lines.Add("배: " + ShipLine(sim, true));
        lines.AddRange(SupplyLines());
        string self = SelfLine(_jT, _jH, _jO, _jA, _jY, _jN);
        if (self.Length > 0) lines.Add(self);
        _jT = Throttles; _jH = HeatStops; _jO = Overloads; _jA = Asked; _jY = AskAccepted; _jN = AskDeclined; _jP = Preempts;
        string blind = BlindLine();
        if (blind.Length > 0) lines.Add(blind);
        long jobs = Assignments - _journalAssign;
        if (jobs > 0) lines.Add($"로봇에게 맡긴 일 {jobs}건");
        Journal.Add((now, string.Join("\n", lines)));
        if (Journal.Count > 60) Journal.RemoveAt(0);
        LastJournal = now; _journalAssign = Assignments;
        Say(sim, "일지: " + lines[0]);
        sim.Facts.Emit(now, FactKind.ComputerJournal, _computer >= 0 && _net != null ? _net.Devices[_computer].Cell : -1, count);
    }

    /// <summary>아침 방송 — 지난 일지 한 줄 + 오늘의 배 (산소 · 식량 · 물 · 못 보는 방).</summary>
    private void MorningBroadcast(Sim sim)
    {
        var parts = new List<string> { "좋은 아침입니다." };
        if (Journal.Count > 0) parts.Add($"지난 일지 — {Journal[^1].text.Split('\n')[0]}.");
        parts.Add($"오늘 배: {ShipLine(sim, false)}.");
        string blind = BlindLine();
        if (blind.Length > 0) parts.Add(blind + ".");
        Morning = string.Join(" ", parts);
        Say(sim, "아침 방송: " + Morning);
        sim.Facts.Emit(sim.Tick, FactKind.ComputerBroadcast, _computer >= 0 && _net != null ? _net.Devices[_computer].Cell : -1);
    }

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.List(new List<ShipPolicy> { Policy });
        w.List(Incidents);
        w.I32(Log.Count); foreach (var (t, s) in Log) { w.I64(t); w.Str(s); }
        w.Bool(Online);
        w.Arr(RoomKPa); w.Arr(RoomKPaPrev); w.Arr(RoomO2); w.Arr(RoomSoot); w.Arr(RoomSootPrev); w.Arr(RoomCO); w.Arr(RoomCO2); w.Arr(RoomTemp); w.Arr(RoomHot); w.Arr(RoomNH3); w.Arr(RoomO3); w.Arr(RoomHCN); w.Arr(RoomHCl); w.Arr(RoomToxCell); w.Arr(RoomSeen); w.Arr(RoomMoles); w.I32(_reads); w.Arr(ShipMolesHist); w.Arr(DoorMoles); w.I64(LastCycle); w.I64(_refillHold); w.I64(RefillHeld); w.I64(_refillSince); w.Arr(_extOpenSince);
        w.Arr(Shed); w.Arr(BreakerResetAt); w.List(Imaginations);
        w.I64(Alarms); w.I64(Assignments); w.I64(Seals); w.I64(Sheds); w.I64(Resets); w.I64(Exhausts); w.I64(OuterClosed); w.I64(StovesCut); SaveCrew(w); SaveSupply(w);
        w.I32(Journal.Count); foreach (var (t, s) in Journal) { w.I64(t); w.Str(s); }
        w.I32(JournalHour); w.I32(MorningHour); w.I64(LastJournal); w.I64(_journalAssign); w.Arr(ReportedDead.ToArray()); w.Str(Morning); w.Arr(RoomAirT); w.Arr(GaveAirAt); w.Arr(DoorWasOpen); w.Arr(RoomCO2Hold);
        SaveHelm(w); SaveLog(w); SaveAir(w); SaveWeather(w); SaveEmpty(w); SavePlan(w); SaveHunt(w);
    }

    public void Load(StateReader r)
    {
        Policy = r.List<ShipPolicy>()[0];
        Incidents = r.List<Incident>();
        Log.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { long t = r.I64(); Log.Add((t, r.Str() ?? "")); }
        Online = r.Bool();
        r.ArrInto(RoomKPa); r.ArrInto(RoomKPaPrev); r.ArrInto(RoomO2); r.ArrInto(RoomSoot); r.ArrInto(RoomSootPrev); r.ArrInto(RoomCO); r.ArrInto(RoomCO2); r.ArrInto(RoomTemp); r.ArrInto(RoomHot); r.ArrInto(RoomNH3); r.ArrInto(RoomO3); r.ArrInto(RoomHCN); r.ArrInto(RoomHCl); r.ArrInto(RoomToxCell); r.ArrInto(RoomSeen); r.ArrInto(RoomMoles); _reads = r.I32(); r.ArrInto(ShipMolesHist); r.ArrInto(DoorMoles); LastCycle = r.I64(); _refillHold = r.I64(); RefillHeld = r.I64(); _refillSince = r.I64(); _extOpenSince = r.Arr<long>();
        Shed = r.Arr<byte>(); BreakerResetAt = r.Arr<long>(); Imaginations.Clear(); Imaginations.AddRange(r.List<Imagined>());
        Alarms = r.I64(); Assignments = r.I64(); Seals = r.I64(); Sheds = r.I64(); Resets = r.I64(); Exhausts = r.I64(); OuterClosed = r.I64(); StovesCut = r.I64(); LoadCrew(r); LoadSupply(r);
        Journal.Clear(); int nj = r.I32(); for (int i = 0; i < nj; i++) { long t = r.I64(); Journal.Add((t, r.Str() ?? "")); }
        JournalHour = r.I32(); MorningHour = r.I32(); LastJournal = r.I64(); _journalAssign = r.I64(); ReportedDead.Clear(); ReportedDead.AddRange(r.Arr<int>()); Morning = r.Str() ?? ""; r.ArrInto(RoomAirT); r.ArrInto(GaveAirAt); r.ArrInto(DoorWasOpen); r.ArrInto(RoomCO2Hold);
        LoadHelm(r); LoadLog(r); LoadAir(r); LoadWeather(r); LoadEmpty(r); LoadPlan(r); LoadHunt(r);
    }
}
