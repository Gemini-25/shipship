namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 (깊이 바퀴 2) — 사람에게 맡기기 · 자기 상태.
/// <para><b>사람에게 맡기기</b>: 로봇이 하나도 없으면 (함장 방침 — 기본은 1분 기다린 뒤) 사람 하나에게 부탁한다 — 깨어 있고 · 가깝고 · 그 일 솜씨가 있는 이부터
/// (자는 이는 30칸만큼 뒤로 · 거절한 이는 빼고). 받을지는 그 사람이 정한다 (⑧ Mind.Ask). 거절하면 15초 뒤 다른 이에게 ·
/// 두 번째부터는 급하다고 말한다 (사람이 쓰러졌다 · 불 · 공기가 빠진다 — "로봇이 없습니다, 급합니다").</para>
/// <para><b>자기 상태</b>: 제 방 공기 온도(빨아들이는 공기)로 안다 — 40℃ 넘으면 느려진다 (센서 읽기 세 배 드물게 · 37℃ 아래로 풀린다) · 60℃ 넘으면 멈춘다 (45℃ 아래로 식으면 다시 켠다) ·
/// 맡기는 차례는 늘 급한 것부터 · 급한 일에는 덜 급한 일 하던 로봇도 데려온다 · 붙잡은 사건이 다섯을 넘거나 느려졌으면 앞일 그려 보기를 멈춘다. 일지에 제 상태를 적는다.</para>
/// </summary>
public sealed partial class ShipMind
{
    public long Asked, AskAccepted, AskDeclined, Throttles, HeatStops, Overloads, Preempts;
    public byte Throttled, HeatStopped, Overloaded;
    public float IntakeK;   // 주컴퓨터가 빨아들이는 공기 온도 (제 방 공기 · K)
    private int _effectiveRead = ReadEvery;

    /// <summary>사람이 할 수 있는 일인가 (드론 · 로봇만의 일 — 차단기 손으로 끊기 · 바깥 둘러보기 — 은 아니다).</summary>
    private static bool PeopleCan(IncidentKind k, GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Repair or GoalKind.Brace or GoalKind.Treat
        || g == GoalKind.Investigate && k is IncidentKind.Leak or IncidentKind.Vacuum or IncidentKind.SensorLost;

    private void AskPerson(Sim sim, ref Incident x, GoalKind g, int target)
    {
        if (_mind == null || !PeopleCan(x.Kind, g)) return;
        long t = sim.Tick;
        if (Policy.AskPeople == 1 && t - x.Since < Time.Minutes(1)) return;            // 로봇이 먼저 — 1분은 기다린다
        var life = sim.Find<Life>();
        if (life == null) return;
        if (x.Person >= 0 && _mind.Agents[x.Person].C.AssignedUntil > t && _mind.Agents[x.Person].C.AssignedGoal == g && life[_mind.Agents[x.Person].C.Body].Alive != 0) return; // 받아 준 이가 하고 있다
        if (t - x.AskedAt < (x.Decl1 >= 0 && x.Person < 0 ? Time.Minutes(0.25) : Time.Minutes(1))) return; // 거절하면 15초 뒤 다음 사람에게 · 받아 준 이가 그만두면 1분 뒤 다시
        int best = -1; double bc = double.MaxValue;
        foreach (var a in _mind.Agents)
        {
            if (!a.IsPerson || a.Id == x.Decl1 || a.Id == x.Decl2) continue;
            ref var b = ref life[a.C.Body];
            if (b.Alive == 0 || b.Conscious == 0 || x.Kind == IncidentKind.PersonDown && a.C.Body == x.Subject) continue;
            if (a.C.AssignedUntil > t && a.C.AssignedGoal != GoalKind.Idle) continue;   // 이미 다른 일을 맡았다
            if (g == GoalKind.Treat && a.C.Body == x.Subject) continue;   // 다친 이에게 저를 돌보라 하지 않는다
            float skill = g switch { GoalKind.FightFire => a.C.SkFire, GoalKind.Rescue or GoalKind.Treat => a.C.SkMedic, GoalKind.Repair or GoalKind.Brace => a.C.SkRepair, _ => a.C.SkSeal };
            double cost = _mind.Dist(a.C.Cell, x.Cell >= 0 ? x.Cell : a.C.Cell) * (1.5 - skill) + (b.Asleep != 0 ? 30 : 0);
            if (cost < bc) { bc = cost; best = a.Id; }
        }
        if (best < 0) return;
        bool urgent = x.Asks > 0 && x.Kind is IncidentKind.PersonDown or IncidentKind.Fire or IncidentKind.Vacuum or IncidentKind.Leak;
        string name = _mind.NameOf(best);
        Say(sim, $"{name}님, {RoomName(x.Room)} {KindName(x.Kind)} — {Mind.JobName(g)} 주시겠어요? {(urgent ? "로봇이 없습니다, 급합니다" : "로봇이 모두 바쁩니다")}");
        bool yes = _mind.Ask(sim, best, g, target, BeliefOf(x), t + Time.Minutes(30), urgent, out string reply);
        x.AskedAt = t; x.Asks++; Asked++;
        if (reply.Length > 0) Say(sim, $"{name}: \"{reply}\"");
        if (yes) { x.Person = best; AskAccepted++; sim.Facts.Emit(t, FactKind.ComputerAssign, x.Cell, 1, x.Cause, best, (int)x.Kind); }
        else { AskDeclined++; x.Decl2 = x.Decl1; x.Decl1 = best; }
    }

    /// <summary>제 몸 살피기: 제 방 공기 온도(빨아들이는 공기 — 제 칸은 제 열로 늘 뜨겁다)로 느려지거나 멈춘다 (식으면 다시 켠다) — 10초마다 · Online을 정한 뒤 부른다.</summary>
    private void SelfCheck(Sim sim)
    {
        if (_net == null || _computer < 0 || sim.Tick % ReadEvery != 0) return;
        int cell = _net.Devices[_computer].Cell, room = L.Room[cell];
        double temp = F.Temp(cell);
        if (room >= 0)
        {
            double n = 0, nt = 0;
            foreach (int c in L.Rooms[room].Cells) if (F.Pressure(c) > 5) { double m = F.Moles(c); n += m; nt += m * F.Temp(c); }
            if (n > 0) temp = nt / n;
        }
        IntakeK = (float)temp;
        if (HeatStopped == 0 && temp > 333)
        {
            HeatStopped = 1; HeatStops++;
            int c = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 과열로 멈췄다 — 방 공기 {temp - 273:0}℃", sim.Tick, cell, _chem?.HeatCauseAt(cell) ?? 0);
            Say(sim, "주컴퓨터가 과열로 멈췄다 — 식으면 다시 켠다");
            sim.Facts.Emit(sim.Tick, FactKind.ComputerDown, cell, (float)temp, c);
        }
        else if (HeatStopped != 0 && temp < 318) { HeatStopped = 0; Say(sim, "식어서 다시 켰다 — 놓친 것부터 읽는다"); }
        // 40℃ 넘으면 느려지고 · 37℃ 아래로 내려가야 풀린다 (문턱에서 깜빡이지 않게)
        byte th = (byte)(HeatStopped == 0 && (temp > 313 || Throttled != 0 && temp > 310) ? 1 : 0);
        if (th != Throttled) { Throttled = th; if (th != 0) { Throttles++; Say(sim, $"주컴퓨터 방이 뜨겁다 ({temp - 273:0}℃) — 느려진다"); } }
        _effectiveRead = Throttled != 0 ? ReadEvery * 3 : ReadEvery;
    }

    /// <summary>급한 차례 (작을수록 급하다): 쓰러진 사람 · 불 · 공기 빠짐 · 샘 · 다친 사람 · 전기 사고 · 버팀 · 고장 · 센서 · 그 밖.</summary>
    internal static int Rank(IncidentKind k) => k switch
    {
        IncidentKind.PersonDown => 0, IncidentKind.Fire => 1, IncidentKind.Vacuum => 2, IncidentKind.Leak => 3, IncidentKind.Injured => 4,
        IncidentKind.ElectricFault => 5, IncidentKind.Strain or IncidentKind.DoorJam => 6, IncidentKind.CO or IncidentKind.Toxic => 7,
        IncidentKind.DeviceBroken or IncidentKind.SensorLost => 8, _ => 9,
    };

    private int[] _order = new int[16];

    /// <summary>맡기는 차례를 _order에 채운다 (급한 것부터 · 같으면 먼저 난 것) — 돌려주는 것은 그 수.
    /// 붙잡은 사건이 다섯을 넘으면 '몰렸다' — 앞일 그려 보기(복제 세계)를 멈추고 방침대로만 한다 (아폴로 11호 1202 경보: 덜 급한 일을 버리고 급한 일만 돌렸다).</summary>
    private int Triage(Sim sim)
    {
        int n = 0;
        if (_order.Length < Incidents.Count) _order = new int[Incidents.Count * 2];
        for (int i = 0; i < Incidents.Count; i++)
        {
            if (Incidents[i].Active == 0) continue;
            int r = Rank(Incidents[i].Kind), j = n++;
            while (j > 0 && Rank(Incidents[_order[j - 1]].Kind) > r) { _order[j] = _order[j - 1]; j--; }   // 안정 삽입 정렬 — 같은 급은 먼저 난 것이 앞
            _order[j] = i;
        }
        byte over = (byte)(n > 5 ? 1 : 0);
        if (over != Overloaded) { Overloaded = over; if (over != 0) { Overloads++; Say(sim, $"사건이 몰렸다 ({n}건) — 앞일을 그려 보지 않고 급한 것부터 맡긴다"); } }
        return n;
    }

    private readonly HashSet<int> _feverSaid = new();   // 열을 알린 몸 (열이 내리면 지운다)

    /// <summary>사람 표의 체온: 37.6℃를 넘으면 한 번 "열이 있습니다 — 쉬세요" (37.2℃ 아래로 내리면 다시 알릴 수 있다).</summary>
    private void FeverWatch(Sim sim, int agent, in Body b)
    {
        int body = _mind!.Agents[agent].C.Body;
        double c = b.CoreK - 273.15;
        if (c > 37.6 && b.Conscious != 0 && _feverSaid.Add(body))
            Say(sim, $"{_mind.NameOf(agent)}님 열이 있습니다 ({c:0.0}℃) — 쉬세요{(b.Ill == 2 ? " · 옮을 수 있으니 다른 분은 가까이 가지 마세요" : b.Infect > 0 ? " · 상처를 보이세요" : "")}");
        else if (c < 37.2) _feverSaid.Remove(body);
    }

    /// <summary>그 로봇이 지금 가 있는 사건 (없으면 -1).</summary>
    private int OwnerOf(Sim sim, int robot)
    {
        for (int i = 0; i < Incidents.Count; i++)
        {
            var y = Incidents[i];
            if (y.Active != 0 && (y.RobotA == robot || y.RobotB == robot) && _mind!.Agents[robot].C.AssignedUntil > sim.Tick) return i;
        }
        return -1;
    }

    /// <summary>일지 끝에 붙이는 제 상태 한 줄 (없으면 빈 글).</summary>
    private string SelfLine(long hT, long hH, long hO, long hA, long hY, long hN)
    {
        var parts = new List<string>();
        if (Preempts > _jP) parts.Add($"급한 일로 로봇을 돌림 {Preempts - _jP}번");
        if (Throttles > hT) parts.Add($"과열로 느려짐 {Throttles - hT}번");
        if (HeatStops > hH) parts.Add($"과열로 멈춤 {HeatStops - hH}번");
        if (Overloads > hO) parts.Add($"사건이 몰림 {Overloads - hO}번");
        if (Asked > hA) parts.Add($"사람에게 부탁 {Asked - hA}번 (받음 {AskAccepted - hY} · 거절 {AskDeclined - hN})");
        return parts.Count > 0 ? "주컴퓨터: " + string.Join(" · ", parts) : "";
    }

    private void SaveCrew(StateWriter w)
    {
        w.I64(Asked); w.I64(AskAccepted); w.I64(AskDeclined); w.I64(Throttles); w.I64(HeatStops); w.I64(Overloads); w.I64(Preempts);
        w.Arr(new[] { Throttled, HeatStopped, Overloaded }); w.I32(_effectiveRead); w.F32(IntakeK);
        w.Arr(new[] { _jT, _jH, _jO, _jA, _jY, _jN, _jP }); w.Arr(_feverSaid.OrderBy(x => x).ToArray());
    }
    private void LoadCrew(StateReader r)
    {
        Asked = r.I64(); AskAccepted = r.I64(); AskDeclined = r.I64(); Throttles = r.I64(); HeatStops = r.I64(); Overloads = r.I64(); Preempts = r.I64();
        var s = r.Arr<byte>(); Throttled = s[0]; HeatStopped = s[1]; Overloaded = s[2]; _effectiveRead = r.I32(); IntakeK = r.F32();
        var j = r.Arr<long>(); _jT = j[0]; _jH = j[1]; _jO = j[2]; _jA = j[3]; _jY = j[4]; _jN = j[5]; _jP = j[6]; _feverSaid.Clear(); foreach (int x in r.Arr<int>()) _feverSaid.Add(x);
    }
    private long _jT, _jH, _jO, _jA, _jY, _jN, _jP;   // 지난 일지 때의 셈 (그 뒤만 적는다)
}
