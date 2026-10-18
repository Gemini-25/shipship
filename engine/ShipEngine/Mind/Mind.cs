namespace ShipEngine;

/// <summary>
/// ⑧ 마음 — 사람 · 로봇 · 드론 (docs/ENGINE.md §4 ⑧). 매 틱.
/// <para>감각 → 믿음 → 본능 → 욕구 · 감정 → 목표(점수) → 계획(단계) → 동작(요청). 아래 층의 상태만 읽고, 바꿀 때는 요청한다
/// (문 열고 닫기 · 몸 옮기기 · 먹고 마시기 · 물 뿌리기 · 구멍 막기 · 고치기).</para>
/// <para>마음은 세계를 직접 모른다: 보고 · 듣고 · 냄새 맡고 · 몸으로 느끼고 · 말로 들은 것만 믿음이 된다 (본능만 몸이 느끼는 실제 값을 쓴다).</para>
/// </summary>
public sealed partial class Mind : IEngine
{
    public string Name => "마음";
    public Phase Phase => Phase.Decide;
    public int Every => 1;
    public int Offset => 0;

    public const int MaxBeliefs = 24;
    public const double BodyWater0 = 42 * 55.5;   // 몸의 물 (몰) — ⑦ 생명이 처음 주는 값
    public const float WakeThirst = 0.8f;         // 이만큼 목마르면 자다가 깬다 (몸의 물 1.6% 모자람 — 0.7L)
    public const double MealMol = 1.15;           // 한 끼 (양분 몰 — 3.2MJ · 약 770kcal: 하루 3.6끼로 BVAD 11.8MJ)

    private readonly Field F;
    private readonly Layout L;
    private readonly Tables T;
    private readonly PeopleTable P;
    private Life? _life;
    private Network? _net;
    private Structure? _st;
    private Society? _soc;
    private Work? _work;
    private Chemistry? _chem;
    private Sound? _snd;
    private Light? _light;

    public readonly List<Agent> Agents = new();
    /// <summary>설비 × 물건 → 남은 개수.</summary>
    public int[] Stock;
    public List<LooseItem> Loose = new();
    public readonly string[] ItemNames;
    public readonly string[] RoleNames;
    public readonly int ItemCount;
    public readonly byte iExt, iRepair, iPatch, iSuit, iMask, iMedkit, iHarvest, iRation;
    /// <summary>고치기 단계의 '부품' 자리에 넣는 특별 값: 배전반에서 급전선을 손으로 끊기.</summary>
    public const byte PullFeederArg = 254;

    // 말: 소리 사건 번호 → 그 말에 실린 믿음 (몇 틱만)
    private readonly List<(int ev, long tick, int speaker, Belief b)> _said = new();
    // 일감 표시: (목표, 대상) → (맡은 수, 로봇이 있나)
    private readonly Dictionary<long, (int n, bool robot)> _claims = new();

    // 도면에서 미리 뽑은 것
    private readonly int[][] _roomWalls;      // 방 둘레의 벽 칸 (구멍 보기)
    private readonly bool[] _hullOut;         // 외판 바깥을 걸을 수 있는 칸
    /// <summary>외판 바깥을 걸을 수 있나 (선외 작업): 로봇 — 진공 · 추위를 견딘다.</summary>
    public static bool Eva(Agent a) => a.C.Kind == AgentKind.Robot;
    public bool HullOut(int cell) => cell >= 0 && cell < _hullOut.Length && _hullOut[cell];
    private readonly int[][] _roomDoors;      // 방 둘레의 문 칸
    private readonly int[] _food, _water, _beds, _social, _relax, _gym, _docks, _droneDocks, _lockers, _medBeds;
    private readonly double[] _halfLife;      // 믿음 종류 → 반감기 (초)

    public long Shouts, Panics, Extinguished, Rescued, Patched, Repaired, FledCount, Meals, Drinks, Exercised;
    /// <summary>소리 (바퀴 2): 바탕 소음에 가려 못 들은 소리 · '기계 소리가 이상하다'를 알아챈 수 · 그 소리를 듣고 부품을 미리 간 수.</summary>
    public long Masked, GrindHeard, EarMaintained;
    /// <summary>이상한 기계 소리 알림 (최근 16개 · 찍는다) — 일감판이 읽는다.</summary>
    public readonly List<GrindReport> GrindReports = new();
    /// <summary>스스로 갈 솜씨 (이보다 덜하면 듣고도 알리기만 한다).</summary>
    public const float GrindSkill = 0.3f;
    public double FoodEaten, WaterDrunk;   // 몰 (장부)

    public Mind(Field f)
    {
        F = f; L = f.L; T = f.T; P = T.People;
        ItemNames = P.Items.Keys.ToArray();
        ItemCount = ItemNames.Length;
        RoleNames = P.Roles.Keys.ToArray();
        byte Item(string n) { int i = Array.IndexOf(ItemNames, n); if (i < 0) throw new InvalidDataException($"사람 표에 물건 '{n}'이 없다"); return (byte)i; }
        iExt = Item("소화기"); iRepair = Item("수리 꾸러미"); iPatch = Item("봉합 꾸러미"); iSuit = Item("우주복"); iMask = Item("산소 마스크"); iMedkit = Item("구급 상자"); iHarvest = Item("수확물"); iRation = Item("식량 상자");

        Stock = new int[L.Furniture.Count * ItemCount];
        foreach (var fu in L.Furniture)
            if (P.Stock.TryGetValue(fu.Type, out var st))
                foreach (var (name, n) in st) Stock[fu.Id * ItemCount + Array.IndexOf(ItemNames, name)] = n;

        _roomWalls = new int[L.Rooms.Count][];
        _roomDoors = new int[L.Rooms.Count][];
        Span<int> nb = stackalloc int[4];
        foreach (var r in L.Rooms)
        {
            var walls = new SortedSet<int>(); var doors = new SortedSet<int>();
            foreach (int c in r.Cells)
            {
                L.Neighbors(c, nb);
                foreach (int j in nb)
                {
                    if (j < 0) continue;
                    if (L.Kind[j] == CellKind.Wall) walls.Add(j);
                    else if (L.Kind[j] == CellKind.Door) doors.Add(j);
                }
            }
            _roomWalls[r.Id] = walls.ToArray(); _roomDoors[r.Id] = doors.ToArray();
        }
        // 외판 바깥: 외판 벽 · 바깥 문과 맞닿은(대각선 포함) 바깥 칸 — 로봇이 자석 발로 걸을 수 있다
        _hullOut = new bool[L.N];
        for (int i = 0; i < L.N; i++)
        {
            if (L.Kind[i] != CellKind.Outside) continue;
            int x = L.X(i), y = L.Y(i);
            for (int dy = -1; dy <= 1 && !_hullOut[i]; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!L.In(x + dx, y + dy)) continue;
                    int j = L.Index(x + dx, y + dy);
                    if (L.Kind[j] == CellKind.Wall && L.Hull[j] || L.Kind[j] == CellKind.Door && L.DoorAt[j] >= 0 && L.Doors[L.DoorAt[j]].External) { _hullOut[i] = true; break; }
                }
        }

        int[] Furn(Func<FurnitureDef, bool> pick) => L.Furniture.Where(fu => fu.Cells.Length > 0 && pick(fu)).Select(fu => fu.Id).ToArray();
        string RoomType(FurnitureDef fu) => fu.Room >= 0 ? L.Rooms[fu.Room].Type : "";
        _food = Furn(fu => fu.Type is "Fridge" or "MealDispenser");
        _water = Furn(fu => fu.Type is "MealDispenser" or "WaterRecycler" or "CoffeeMachine" or "IceMaker" or "GreywaterFilter");
        _beds = Furn(fu => fu.Type is "Bed");
        _social = Furn(fu => fu.Type is "Seat" or "Table" && RoomType(fu) is "Mess" or "Lounge" or "Galley" or "MeetingRoom");
        _gym = Furn(fu => fu.Type is "Treadmill");
        _relax = Furn(fu => fu.Type is "Seat" or "GameTable" or "Treadmill" or "Projector" or "Bookshelf" or "Aquarium" && RoomType(fu) is "Lounge" or "Gym" or "Theater" or "Observatory" or "Garden" or "Chapel");
        if (_relax.Length == 0) _relax = _social;
        _docks = Furn(fu => fu.Type is "RobotDock");
        _droneDocks = Furn(fu => fu.Type is "DroneDock");
        _lockers = Furn(fu => fu.Type is "SuitLocker");
        _medBeds = Furn(fu => fu.Type is "MedBed");

        _halfLife = new double[Enum.GetValues<BeliefKind>().Length];
        for (int k = 0; k < _halfLife.Length; k++) _halfLife[k] = 600;
        void HL(BeliefKind k, string name) { if (P.HalfLife.TryGetValue(name, out var m)) _halfLife[(int)k] = m * 60; }
        HL(BeliefKind.Fire, "불"); HL(BeliefKind.Smoke, "연기"); HL(BeliefKind.Breach, "구멍"); HL(BeliefKind.Vacuum, "진공");
        HL(BeliefKind.Injured, "다친 사람"); HL(BeliefKind.Dead, "죽은 사람"); HL(BeliefKind.Broken, "고장"); HL(BeliefKind.Bang, "쾅");
        HL(BeliefKind.Strain, "끼익"); HL(BeliefKind.Leak, "쉬익"); HL(BeliefKind.SmokeSmell, "타는 냄새"); HL(BeliefKind.BadAir, "나쁜 공기");
        HL(BeliefKind.Water, "물"); HL(BeliefKind.Spark, "합선"); HL(BeliefKind.StuckDoor, "안 열리는 문"); HL(BeliefKind.Help, "도와 달라");
        HL(BeliefKind.Grind, "기계 소리");

        _g = new float[L.N]; _from = new int[L.N]; _stamp = new int[L.N];
    }

    // ───────────────────────── 사람 · 로봇 넣기 ─────────────────────────

    public string NameOf(int agent)
    {
        var a = Agents[agent];
        if (a.C.Kind == AgentKind.Robot) return $"로봇 {a.C.Name + 1}";
        if (a.C.Kind == AgentKind.Drone) return $"드론 {a.C.Name + 1}";
        int n = a.C.Name;
        return n < P.Names.Count ? P.Names[n] : $"{P.Names[n % P.Names.Count]} {n / P.Names.Count + 1}";
    }

    /// <summary>사람 하나를 칸 cell에 (몸은 ⑦ 생명에). 성격 · 솜씨는 시드에서.</summary>
    public int AddPerson(Sim sim, int cell, int role = -1, int bed = -1)
    {
        Bind(sim);
        int id = Agents.Count;
        int people = Agents.Count(x => x.IsPerson);
        var rng = Rng.Stream(sim.Seed, "사람", people);
        var c = new AgentCore
        {
            Kind = AgentKind.Person, Up = 1, Name = people, Body = _life!.AddBody(cell), Cell = cell, Bed = bed, Station = -1, Dock = -1,
            Role = (byte)(role >= 0 ? role : people % Math.Max(1, RoleNames.Length)),
            ShiftHour = (people % 3) * 8,
            Hunger = (float)rng.Range(0.0, 0.5), Sleepy = (float)rng.Range(0.0, 0.4), Lonely = (float)rng.Range(0.0, 0.5), Bored = (float)rng.Range(0.0, 0.5),
            Brave = rng.UnitF(), Calm = rng.UnitF(), Kindness = rng.UnitF(), Diligent = rng.UnitF(), Sociable = rng.UnitF(), Curious = rng.UnitF(),
            SkRepair = 0.2f + 0.2f * rng.UnitF(), SkMedic = 0.1f + 0.2f * rng.UnitF(), SkFire = 0.3f + 0.2f * rng.UnitF(), SkSeal = 0.2f + 0.2f * rng.UnitF(),
            SkCook = 0.3f * rng.UnitF(), SkPilot = 0.2f * rng.UnitF(), SkFarm = 0.2f * rng.UnitF(), SkScience = 0.2f * rng.UnitF(),
            Battery = 1, Holding = 255, Carrying = -1, CarriedBy = -1, WaitDoor = -1, OpenedDoor = -1, GoalTarget = -1, GoalCell = -1,
            LastKPa = (float)F.Pressure(cell), HeardUpTo = _snd?.LastEvent ?? 0, LastShout = -1_000_000,
        };
        if (c.Role < RoleNames.Length)
            foreach (var (sk, v) in P.Roles[RoleNames[c.Role]].Skills) SetSkill(ref c, sk, (float)Math.Max(GetSkill(c, sk), v));
        // ⑧ 성격 다섯 축 — 따로 갈래 낸 난수에서 (다른 난수 흐름은 그대로)
        var pr = Rng.Stream(sim.Seed, "성격", people);
        float Trait() { double u1 = Math.Max(1e-12, pr.Unit()), u2 = pr.Unit(); return (float)Math.Clamp(0.5 + TraitSd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2), 0.02, 0.98); }
        c.Open = Trait(); c.Consc = Trait(); c.Extra = Trait(); c.Agree = Trait(); c.Neuro = Trait();
        float braveNoise = Trait() - 0.5f;
        c.Unfit = pr.UnitF() * 0.8f;
        Derive(ref c, braveNoise);
        c.Chrono = ChronoOf(sim.Seed, people, c.Consc, c.Open);
        var a = new Agent { Id = id, C = c };
        Agents.Add(a);
        a.C.Station = PickStation(a);
        return id;
    }

    public const double TraitSd = 0.17;

    /// <summary>⑧ 다섯 축 → 여섯 버릇: 호기심 = 열림 · 성실 = 성실 · 사교 = 외향 · 친절 = 친화 · 침착 = 1 − 신경성 · 용기 = 외향(+) · 신경성(−)에 조금의 제 몫 (감각 추구 연구).</summary>
    private static void Derive(ref AgentCore c, float braveNoise)
    {
        c.Curious = c.Open; c.Diligent = c.Consc; c.Sociable = c.Extra; c.Kindness = c.Agree; c.Calm = 1 - c.Neuro;
        c.Brave = Math.Clamp(0.5f + 0.45f * (c.Extra - 0.5f) - 0.6f * (c.Neuro - 0.5f) + 0.5f * braveNoise, 0.02f, 0.98f);
    }

    /// <summary>성격을 정해 준다 (시험 · 승무원 명단) — 여섯 버릇도 다시 낸다 (용기의 제 몫은 0).</summary>
    public void SetPersonality(int agent, float open, float consc, float extra, float agree, float neuro)
    {
        ref var c = ref Agents[agent].C;
        c.Open = open; c.Consc = consc; c.Extra = extra; c.Agree = agree; c.Neuro = neuro;
        Derive(ref c, 0);
    }

    public int AddRobot(Sim sim, int cell, AgentKind kind = AgentKind.Robot, int dock = -1)
    {
        Bind(sim);
        int id = Agents.Count;
        int n = Agents.Count(x => x.C.Kind == kind);
        var a = new Agent
        {
            Id = id,
            C = new AgentCore
            {
                Kind = kind, Up = 1, Name = n, Body = -1, Cell = cell, Bed = -1, Station = -1, Dock = dock >= 0 ? dock : cell,
                Role = 255, Brave = 1, Calm = 1, Kindness = 1, Diligent = 1, Curious = 1,
                SkRepair = 0.7f, SkFire = 0.8f, SkSeal = 0.8f, SkMedic = 0.3f,
                Battery = 1, Holding = 255, Carrying = -1, CarriedBy = -1, WaitDoor = -1, OpenedDoor = -1, GoalTarget = -1, GoalCell = -1,
                LastKPa = (float)F.Pressure(cell), HeardUpTo = _snd?.LastEvent ?? 0, LastShout = -1_000_000,
            }
        };
        Agents.Add(a);
        return id;
    }

    /// <summary>배에 사람 · 로봇 · 드론을 태운다: 사람은 침대마다(많아야 people), 로봇은 충전독마다, 드론은 드론독마다.</summary>
    public void Crew(Sim sim, int people, int robots, int drones)
    {
        Bind(sim);
        var beds = _beds.Select(b => L.Furniture[b].Cells[0]).ToList();
        var free = Enumerable.Range(0, L.N).Where(i => L.Kind[i] == CellKind.Floor && L.FurnitureAt[i] < 0).ToList();
        var rng = Rng.Stream(sim.Seed, "자리");
        for (int k = 0; k < people; k++)
        {
            int bed = k < beds.Count ? beds[k] : -1;
            int cell = bed;
            if (cell < 0 && free.Count > 0) { int i = rng.Range(0, free.Count); cell = free[i]; free.RemoveAt(i); } // 침대가 모자라면 아무 빈 바닥 (잠은 바닥에서)
            AddPerson(sim, cell >= 0 ? cell : FirstFloor(), -1, bed);
        }
        for (int k = 0; k < Math.Min(robots, _docks.Length); k++) { int c = L.Furniture[_docks[k]].Cells[0]; AddRobot(sim, c, AgentKind.Robot, c); }
        for (int k = 0; k < Math.Min(drones, _droneDocks.Length); k++) { int c = L.Furniture[_droneDocks[k]].Cells[0]; AddRobot(sim, c, AgentKind.Drone, c); }
    }

    private int FirstFloor() { for (int i = 0; i < L.N; i++) if (L.Kind[i] == CellKind.Floor && L.FurnitureAt[i] < 0) return i; return 0; }

    private int PickStation(Agent a)
    {
        if (a.C.Role >= RoleNames.Length) return -1;
        var role = P.Roles[RoleNames[a.C.Role]];
        int best = -1, bd = int.MaxValue;
        foreach (var fu in L.Furniture)
        {
            if (fu.Cells.Length == 0 || fu.Room < 0 || !role.Devices.Contains(fu.Type) || !role.Rooms.Contains(L.Rooms[fu.Room].Type)) continue;
            int d = Dist(a.C.Cell, fu.Cells[0]) + Agents.Count(o => o != a && o.C.Station == fu.Id) * 40; // 한 설비에 몰리지 않게
            if (d < bd) { bd = d; best = fu.Id; }
        }
        return best;
    }

    private static float GetSkill(in AgentCore c, string sk) => sk switch
    {
        "고치기" => c.SkRepair, "치료" => c.SkMedic, "불끄기" => c.SkFire, "봉합" => c.SkSeal, "조리" => c.SkCook, "조종" => c.SkPilot, "기르기" => c.SkFarm, "연구" => c.SkScience, _ => 0,
    };

    private static void SetSkill(ref AgentCore c, string sk, float v)
    {
        switch (sk)
        {
            case "고치기": c.SkRepair = v; break; case "치료": c.SkMedic = v; break; case "불끄기": c.SkFire = v; break; case "봉합": c.SkSeal = v; break;
            case "조리": c.SkCook = v; break; case "조종": c.SkPilot = v; break; case "기르기": c.SkFarm = v; break; case "연구": c.SkScience = v; break;
        }
    }

    // ───────────────────────── 주컴퓨터가 쓰는 요청 ─────────────────────────

    /// <summary>말한 이 번호: 주컴퓨터.</summary>
    public const int Computer = -2;

    /// <summary>방송 · 경보: 칸 cell의 스피커에서 소리가 나고, 들은 사람은 믿음을 '화면(방송)' 출처로 얻는다.</summary>
    public int Announce(Sim sim, int cell, float db, Belief b)
    {
        Bind(sim);
        if (_snd == null) return -1;
        db = Math.Max(db, Math.Min(105, _snd.Ambient(cell) + 15));   // 스피커는 바탕 소음보다 15dB 크게 (NFPA 72 · ISO 7731)
        int ev = _snd.Emit(sim, cell, db, SoundKind.Alarm, b.Cause, Computer);
        _said.Add((ev, sim.Tick, Computer, b));
        return ev;
    }

    /// <summary>
    /// 외치는 크기 (1m · dB) — 롬바르 효과: 사람은 바탕 소음이 48dB를 넘는 몫의 반만큼 더 크게 (75 → 많아야 90 — 외침 76~84dB @1m · 기울기 0.5dB/dB) ·
    /// 로봇의 안내는 바탕 소음 + 15dB (80 ~ 95).
    /// </summary>
    public float VoiceDb(int cell, bool person)
    {
        float n = _snd?.Ambient(cell) ?? 0;
        return person ? Math.Min(90, 75 + 0.5f * Math.Max(0, n - 48)) : Math.Clamp(n + 15, 80, 95);
    }

    /// <summary>로봇 · 사람에게 일을 맡긴다: 믿음을 넣고 그 목표에 점수를 더한다 (until 틱까지).</summary>
    /// <summary>⑪ 일감판이 일을 맡긴다 (믿음 없이).</summary>
    public void AssignJob(int agent, GoalKind goal, int target, int arg, long until)
    {
        var a = Agents[agent];
        a.C.AssignedGoal = goal; a.C.AssignedTarget = target; a.C.AssignedArg = arg; a.C.AssignedUntil = until;
    }

    public int ItemIndex(string name) => Array.IndexOf(ItemNames, name);

    /// <summary>손에 든 식량 (수확물 · 식량 상자) — 물건이 아니라 식량으로 센다.</summary>
    public bool IsFood(int item) => item == iHarvest || item == iRation;

    /// <summary>배에 있는 그 물건 수 (설비 안 + 손에 든 것).</summary>
    public int TotalOf(int item)
    {
        if (item < 0) return 0;
        int n = 0;
        for (int fu = 0; fu < L.Furniture.Count; fu++) n += Stock[fu * ItemCount + item];
        foreach (var a in Agents) if (a.C.Holding == item) n++;
        return n;
    }

    /// <summary>가장 가까운 설비에서 하나 꺼내 쓴다 (없으면 거짓).</summary>
    public bool TakeOne(int from, int item, Sim? sim = null)
    {
        int fu = NearestStock(from, (byte)item);
        if (fu < 0) return false;
        Stock[fu * ItemCount + item]--;
        sim?.Book.AddItem(item, Flow.Used, -1);
        return true;
    }

    /// <summary>배에 있는 그 물건 수 — 설비 안 · 손에 든 것 · 바닥에 떨어진 것 (⑪ 장부의 재고).</summary>
    public int CountAll(int item)
    {
        int n = TotalOf(item);
        foreach (var x in Loose) if (x.Item == item) n++;
        return n;
    }

    public void Assign(Sim sim, int agent, GoalKind goal, int target, Belief b, long until)
    {
        var a = Agents[agent];
        Learn(a, sim.Tick, b.Kind, b.Cell, b.Subject, b.Conf, BeliefSource.Screen, b.Cause, b.Size);
        a.C.AssignedGoal = goal; a.C.AssignedTarget = target; a.C.AssignedUntil = until;
        a.Avoid.RemoveAll(v => v.Goal == goal && v.Target == target); // 새로 맡긴 일: '거기는 봤다'를 접고 다시 간다
    }

    // ───────────────────────── 읽기 (화면 · 연출 · 시험) ─────────────────────────

    public Agent this[int i] => Agents[i];
    public bool Alive(Agent a) => a.IsPerson ? _life![a.C.Body].Alive != 0 : a.C.Damage < 1;
    public ref Body BodyOf(Agent a) => ref _life![a.C.Body];

    public static string GoalName(GoalKind g) => g switch
    {
        GoalKind.Idle => "쉬는 중", GoalKind.Flee => "몸을 피한다", GoalKind.Panic => "어쩔 줄 모른다", GoalKind.Down => "쓰러져 있다",
        GoalKind.Sleep => "잔다", GoalKind.Eat => "먹는다", GoalKind.Drink => "물을 마신다", GoalKind.Work => "일한다", GoalKind.Relax => "쉰다",
        GoalKind.Socialize => "어울린다", GoalKind.FightFire => "불을 끈다", GoalKind.Rescue => "사람을 구한다", GoalKind.Patch => "구멍을 막는다",
        GoalKind.Repair => "고친다", GoalKind.Investigate => "살피러 간다", GoalKind.GetSuit => "우주복을 입는다", GoalKind.Charge => "충전한다", GoalKind.Evacuate => "방을 빠져나간다", GoalKind.Brace => "버티는 문을 받친다",
        GoalKind.Craft => "물건을 만든다", GoalKind.Maintain => "부품을 미리 간다", GoalKind.Clean => "곰팡이를 닦는다", GoalKind.Harvest => "작물을 거둔다", GoalKind.Exercise => "운동한다", GoalKind.Brawl => "몸싸움 중", GoalKind.Restock => "식량을 주방으로 나른다", GoalKind.Cook => "밥을 짓는다", _ => "",
    };

    public static string BeliefName(BeliefKind k) => k switch
    {
        BeliefKind.Fire => "불", BeliefKind.Smoke => "연기", BeliefKind.Breach => "구멍", BeliefKind.Vacuum => "공기가 빠짐", BeliefKind.Injured => "쓰러진 사람",
        BeliefKind.Dead => "죽은 사람", BeliefKind.Broken => "멈춘 장치", BeliefKind.Bang => "쾅 소리", BeliefKind.Strain => "끼익 소리", BeliefKind.Leak => "새는 소리",
        BeliefKind.SmokeSmell => "타는 냄새", BeliefKind.BadAir => "이상한 공기", BeliefKind.Water => "고인 물", BeliefKind.Spark => "불꽃", BeliefKind.StuckDoor => "안 열리는 문",
        BeliefKind.Help => "비명", BeliefKind.Cold => "추위", BeliefKind.Hot => "더위", BeliefKind.Grind => "이상한 기계 소리", _ => "",
    };

    /// <summary>머릿속 한 줄 (화면의 생각 칸).</summary>
    public string Thought(int agent)
    {
        var a = Agents[agent];
        string s = a.C.Goal == GoalKind.Relax && a.IsPerson ? HobbyName((Hobby)a.C.Hobby) : GoalName(a.C.Goal);
        if (a.Plan.Count > 0 && a.C.StepIndex < a.Plan.Count)
        {
            var st = a.Plan[a.C.StepIndex];
            string step = st.Kind switch
            {
                StepKind.GoTo => "가는 중", StepKind.Take => $"{ItemNames[st.Arg]} 꺼내는 중", StepKind.Spray => "물을 뿌리는 중", StepKind.Seal => "막는 중", StepKind.Brace => "덧대는 중", StepKind.Craft => "만드는 중", StepKind.Maintain => "가는 중(정비)", StepKind.Clean => "닦는 중", StepKind.Harvest => "거두는 중", StepKind.Stow => "갈무리하는 중", StepKind.TakeFood => "식량 상자 꺼내는 중",
                StepKind.Fix => "고치는 중", StepKind.PickUp => "업는 중", StepKind.Drop => "내려놓는 중", StepKind.Wear => "입는 중", StepKind.Look => "살펴보는 중", _ => "",
            };
            if (step.Length > 0) s += " — " + step;
        }
        return s;
    }

    // ───────────────────────── 한 틱 ─────────────────────────

    private void Bind(Sim sim)
    {
        _life ??= sim.Find<Life>(); _net ??= sim.Find<Network>(); _st ??= sim.Find<Structure>();
        _chem ??= sim.Find<Chemistry>(); _snd ??= sim.Find<Sound>(); _light ??= sim.Find<Light>(); _soc ??= sim.Find<Society>(); _work ??= sim.Find<Work>();
    }

    public void Step(Sim sim, double dt)
    {
        Bind(sim);
        RationDay(sim.Tick);
        Fling(sim, dt); // 감압 바람에 밀리는 물건 (사람 · 로봇이 없어도)
        if (Agents.Count == 0) { HoldDoorsByItems(); return; }
        F.RebuildNow();
        IndexBodies();
        foreach (var a in Agents) UpdateUp(sim, a);
        Inertia(sim);   // ⑤ 점화 — 갑자기 오른 가속에 선 사람이 넘어진다
        foreach (var a in Agents) Sense(sim, a, dt);
        _said.RemoveAll(x => x.tick < sim.Tick - Sound.KeepTicks - 1);
        BuildClaims();
        if (sim.Tick % 25 == 0) { Learn(sim); Places(sim); }
        foreach (var a in Agents)
        {
            if (a.C.Up != 0 && a.IsPerson) Instinct(sim, a);
            bool think = a.C.Up != 0 && a.C.Mode == 0 && ((sim.Tick + a.Id) % 2 == 0 || a.Plan.Count == 0 || a.C.StepIndex >= a.Plan.Count);
            if (think) Decide(sim, a);
            WithdrawDoor(a);
            WithdrawCook(sim, a);
            if (a.C.Up != 0 || IsSleeping(a)) Advance(sim, a, dt);
            Needs(sim, a, dt);
        }
        BlockDoors();
    }

    /// <summary>열어 달라던 문을 지나가지 않게 됐으면 (계획이 바뀌었다 · 쓰러졌다) 요청을 거둔다 — 남이 그 문을 기다리지 않으면.
    /// 안 거두면 아무도 지나가지 않는 문이 열린 채 남는다 (바깥문이면 배 공기가 에어락 안쪽 문틈으로 밤새 빠진다).</summary>
    private void WithdrawDoor(Agent a)
    {
        ref var c = ref a.C;
        int d = c.WaitDoor;
        if (d < 0 || _st == null) return;
        int dc = L.Doors[d].Cell;
        if (c.Up != 0 && a.PathIdx < a.Path.Count && a.Path.IndexOf(dc, a.PathIdx) >= 0) return; // 아직 그 문으로 간다
        c.WaitDoor = -1; c.WaitTicks = 0;
        ReleaseDoor(a, d);
    }

    /// <summary>a가 열어 달라던 문 d의 요청을 거둔다 — 남이 그 문을 기다리지 않고 · 문 칸에 아무도 없으면.</summary>
    private void ReleaseDoor(Agent a, int d)
    {
        if (d < 0 || _st == null) return;
        foreach (var o in Agents) if (o != a && (o.C.WaitDoor == d || o.C.Cell == L.Doors[d].Cell)) return;
        _st.RequestDoor(d, 0);
    }

    /// <summary>문 칸에 몸(사람 · 쓰러진 몸 · 로봇)이 있으면 문이 닫히지 않는다 — 몸에 걸린다.</summary>
    private void BlockDoors()
    {
        if (_st == null) return;
        void Hold(int cell)
        {
            if (cell < 0 || L.Kind[cell] != CellKind.Door) return;
            int d = L.DoorAt[cell];
            if (_st.DoorTarget[d] < 0.5f && _st.DoorJam[d] == 0) _st.RequestDoor(d, 1);
        }
        if (_life != null) foreach (var b in _life.Bodies) Hold(b.Cell);
        foreach (var a in Agents) if (!a.IsPerson) Hold(a.C.Cell);
        HoldDoorsByItems();
    }

    /// <summary>움직일 수 있나: 사람은 살아 있고 의식이 있고 깨어 있어야 · 로봇은 멀쩡하고 전지가 있어야.</summary>
    private void UpdateUp(Sim sim, Agent a)
    {
        if (a.IsPerson)
        {
            ref var b = ref _life![a.C.Body];
            a.C.Cell = b.Cell;
            bool up = b.Alive != 0 && b.Conscious != 0 && b.Asleep == 0;
            if (a.C.Up != 0 && !up && b.Asleep == 0) Fall(sim, a);
            a.C.Up = (byte)(up ? 1 : 0);
            if (b.Alive == 0 || b.Conscious == 0) { a.C.Goal = GoalKind.Down; a.C.Mode = 0; }
        }
        else
        {
            bool up = a.C.Damage < 1 && a.C.Battery > 0;
            if (a.C.Up != 0 && !up) Fall(sim, a);
            a.C.Up = (byte)(up ? 1 : 0);
        }
    }

    /// <summary>쓰러졌다 · 멈췄다: 업던 사람을 내려놓고 계획을 버린다 (든 물건은 그 자리에 떨어진다).</summary>
    private void Fall(Sim sim, Agent a)
    {
        if (a.C.Carrying >= 0) DropCarried(sim, a, false);
        if (a.C.Holding != 255) { Loose.Add(new LooseItem { Item = a.C.Holding, Cell = a.C.Cell, Charge = a.C.HoldCharge }); a.C.Holding = 255; a.C.HoldCharge = 0; }
        a.Plan.Clear(); a.Path.Clear(); a.C.StepIndex = 0; a.C.Goal = GoalKind.Down; a.C.Mode = 0;
        if (a.C.OpenedDoor >= 0) a.C.OpenedDoor = -1;
    }

    private bool IsSleeping(Agent a) => a.IsPerson && _life![a.C.Body].Asleep != 0 && _life[a.C.Body].Alive != 0 && _life[a.C.Body].Conscious != 0;

    // ───────────────────────── 욕구 · 감정 ─────────────────────────

    private void Needs(Sim sim, Agent a, double dt)
    {
        ref var c = ref a.C;
        double h = dt / 3600.0;
        if (!a.IsPerson)
        {
            if (c.Up == 0) return;
            bool docked = c.Goal == GoalKind.Charge && c.Cell == c.Dock && DockWorking(c.Dock);
            if (docked) c.Battery = (float)Math.Min(1, c.Battery + 0.4 * h);
            else c.Battery = (float)Math.Max(0, c.Battery - (c.Speed > 0.1f || c.Goal is GoalKind.FightFire or GoalKind.Patch or GoalKind.Repair or GoalKind.Rescue ? 0.08 : 0.02) * h);
            // 열에 상한다 (겉면 600K · 공기 450K 위)
            if (F.HasSurface(c.Cell) && F.Tf[c.Cell] > 600 || F.Temp(c.Cell) > 450) c.Damage = (float)Math.Min(1, c.Damage + dt / 600);
            return;
        }
        ref var b = ref _life![c.Body];
        if (b.Alive == 0) return;
        if (c.Careful > 0) c.Careful *= (float)Math.Exp(-dt * 0.6931 / (7 * 86400)); // 배운 조심은 이레에 반쯤 흐려진다
        bool asleep = b.Asleep != 0;
        c.Hunger = (float)Math.Min(1, c.Hunger + h / Need("hunger", 8) * (asleep ? 0.5 : 1)); // 자는 동안은 천천히
        c.Thirst = (float)Math.Clamp((BodyWater0 - b.Water) / (0.02 * BodyWater0), 0, 1);
        if (asleep) { SleepQuality(a, h); c.Sleepy = (float)Math.Max(0, c.Sleepy - h / 8.0 * 1.6 * (1 - 0.6 * c.SleepLoss)); } // 16시간 깨어 있던 졸림을 5시간 남짓 · 꽉 찬 졸림은 8시간 가까이 — 얕은 잠은 덜 풀린다 (⑦)
        else c.Sleepy = (float)Math.Min(1, c.Sleepy + h / Need("sleep", 16) * TireFactor(a)); // 앓거나 몸무게를 잃으면 빨리 지친다 (⑦)
        int others = CountNear(a, 6);
        if (others > 0 && !asleep) c.Lonely = (float)Math.Max(0, c.Lonely - h * Math.Min(2, others) / 1.5);
        else c.Lonely = (float)Math.Min(1, c.Lonely + h / (Need("social", 10) * (1.5 - c.Extra))); // 외향일수록 사람이 빨리 그립다 (6~14시간)
        bool resting = c.Goal is GoalKind.Relax or GoalKind.Socialize && c.StepIndex < a.Plan.Count && a.Plan[c.StepIndex].Kind == StepKind.Use; // 가는 중이 아니라 쉬는 · 어울리는 중
        if (resting) c.Bored = (float)Math.Max(0, c.Bored - h);
        else if (!asleep) c.Bored = (float)Math.Min(1, c.Bored + h / Need("fun", 8) * (c.Goal == GoalKind.Work ? 1.5 - c.Diligent : 1) * (0.6 + 0.8 * c.Open)); // 열린 사람은 새것이 빨리 고프다
        if (!asleep && c.Goal != GoalKind.Exercise) c.Unfit = (float)Math.Min(1, c.Unfit + h / 16); // 깨어 있는 하루면 1
        // 감정: 두려움은 몇 분이면 가라앉는다 (침착할수록 빨리) · 긴장은 굶주림 · 졸림 · 두려움에 쌓이고 잠 · 쉼에 풀린다
        c.Fear = (float)Math.Max(0, c.Fear - dt / 600 * (0.5 + c.Calm));
        c.Joy = (float)Math.Max(0, c.Joy - h / 4);
        c.Sad = (float)Math.Max(0, c.Sad - h / 48);
        c.Anger = (float)Math.Max(0, c.Anger - h / 6);
        double load = (Math.Max(0, c.Hunger - 0.7) + Math.Max(0, c.Thirst - 0.7) + Math.Max(0, c.Sleepy - 0.7) + c.Fear * 0.5 + b.Pain * 0.5) * (0.6 + 0.8 * c.Neuro); // 신경성이 높으면 같은 일에도 더 긴장한다
        double ease = asleep || c.Goal is GoalKind.Relax or GoalKind.Socialize ? 0.3 : 0.05;
        c.Stress = (float)Math.Clamp(c.Stress + h * (load - ease), 0, 1);
        RationHunger(a, h, asleep);   // ⑪ 배급: 몫을 다 먹고도 배고프다 — 참는다
    }

    private double Need(string k, double dflt) => P.Needs.TryGetValue(k, out var v) ? v : dflt;

    private int CountNear(Agent a, int r)
    {
        int n = 0;
        foreach (var o in Agents)
            if (o != a && o.IsPerson && o.C.Up != 0 && Dist(o.C.Cell, a.C.Cell) <= r && RoomOf(o.C.Cell) == RoomOf(a.C.Cell)) n++;
        return n;
    }

    private bool DockWorking(int cell)
    {
        if (_net == null) return true;
        int fu = L.FurnitureAt[cell];
        foreach (var d in _net.Devices) if (d.Furniture == fu) return _net.Working(d.Id);
        return true;
    }

    // ───────────────────────── 칸 도우미 ─────────────────────────

    public int Dist(int a, int b) => Math.Max(Math.Abs(L.X(a) - L.X(b)), Math.Abs(L.Y(a) - L.Y(b)));
    private int Manhattan(int a, int b) => Math.Abs(L.X(a) - L.X(b)) + Math.Abs(L.Y(a) - L.Y(b));

    /// <summary>칸의 방 (문 칸이면 이웃한 방 중 처음 것).</summary>
    public int RoomOf(int c)
    {
        if (c < 0) return -1;
        if (L.Room[c] >= 0) return L.Room[c];
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) return L.Room[j];
        return -1;
    }

    // 몸 칸 색인 (매 틱 다시): 칸 → 몸들
    private int[] _bodyHead = Array.Empty<int>(), _bodyNext = Array.Empty<int>();
    private int[] _bodyAgent = Array.Empty<int>();
    private void IndexBodies()
    {
        if (_bodyHead.Length != L.N) _bodyHead = new int[L.N];
        Array.Fill(_bodyHead, -1);
        int n = _life?.Bodies.Count ?? 0;
        if (_bodyNext.Length < n) { _bodyNext = new int[n * 2]; _bodyAgent = new int[n * 2]; }
        Array.Fill(_bodyAgent, -1, 0, n);
        for (int i = 0; i < n; i++)
        {
            int c = _life!.Bodies[i].Cell;
            if (c < 0) continue;
            _bodyNext[i] = _bodyHead[c]; _bodyHead[c] = i;
        }
        foreach (var a in Agents) if (a.IsPerson) _bodyAgent[a.C.Body] = a.Id;
    }

    public int AgentOfBody(int body) => body >= 0 && body < _bodyAgent.Length ? _bodyAgent[body] : -1;

    // ───────────────────────── 일감 표시 ─────────────────────────

    private static long ClaimKey(GoalKind g, int target) => ((long)g << 40) | (uint)(target + 1);

    private void BuildClaims()
    {
        _claims.Clear();
        foreach (var a in Agents)
            if (a.C.Up != 0 && IsTask(a.C.Goal)) Claim(a, a.C.Goal, a.C.GoalTarget, +1);
    }

    private void Claim(Agent a, GoalKind g, int target, int d)
    {
        long k = ClaimKey(g, target);
        _claims.TryGetValue(k, out var v);
        v.n += d;
        if (d > 0 && !a.IsPerson) v.robot = true;
        _claims[k] = v;
    }

    private static bool IsTask(GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Repair or GoalKind.Investigate;

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.I32(Agents.Count);
        foreach (var a in Agents)
        {
            w.List(new List<AgentCore> { a.C });
            w.List(a.Beliefs); w.List(a.Plan); w.List(a.Path); w.I32(a.PathIdx); w.List(a.Avoid); w.List(a.Doubts);
        }
        w.Arr(Stock); w.List(Loose);
        w.I32(_said.Count);
        foreach (var (ev, tick, sp, b) in _said) { w.I32(ev); w.I64(tick); w.I32(sp); w.List(new List<Belief> { b }); }
        w.I64(Shouts); w.I64(Panics); w.I64(Extinguished); w.I64(Rescued); w.I64(Patched); w.I64(Repaired); w.I64(FledCount); w.I64(Meals); w.I64(Drinks); w.I64(Exercised); w.F64(FoodEaten); w.F64(WaterDrunk); w.F64(WaterSprayed);
        w.I64(Cooked); w.I64(Forgot); w.I64(_learnCursor); w.I64(AteTogether); w.I64(Followed);
        w.Arr(HobbyCount); SavePlaces(w); SaveRation(w);
        w.I64(Masked); w.I64(GrindHeard); w.I64(EarMaintained); w.List(GrindReports);
        w.F64(_lastG); w.I64(Falls); w.I64(BracedOnBurn);
    }

    public void Load(StateReader r)
    {
        Agents.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++)
        {
            var a = new Agent { Id = i, C = r.List<AgentCore>()[0] };
            a.Beliefs = r.List<Belief>(); a.Plan = r.List<PlanStep>(); a.Path = r.List<int>(); a.PathIdx = r.I32(); a.Avoid = r.List<AvoidEntry>(); a.Doubts = r.List<Doubt>();
            Agents.Add(a);
        }
        r.ArrInto(Stock); Loose = r.List<LooseItem>();
        _said.Clear();
        int m = r.I32();
        for (int i = 0; i < m; i++) { int ev = r.I32(); long t = r.I64(); int sp = r.I32(); var b = r.List<Belief>()[0]; _said.Add((ev, t, sp, b)); }
        Shouts = r.I64(); Panics = r.I64(); Extinguished = r.I64(); Rescued = r.I64(); Patched = r.I64(); Repaired = r.I64(); FledCount = r.I64(); Meals = r.I64(); Drinks = r.I64(); Exercised = r.I64(); FoodEaten = r.F64(); WaterDrunk = r.F64(); WaterSprayed = r.F64();
        Cooked = r.I64(); Forgot = r.I64(); _learnCursor = r.I64(); AteTogether = r.I64(); Followed = r.I64();
        HobbyCount = r.Arr<long>(); LoadPlaces(r); LoadRation(r);
        Masked = r.I64(); GrindHeard = r.I64(); EarMaintained = r.I64(); GrindReports.Clear(); GrindReports.AddRange(r.List<GrindReport>());
        _lastG = r.F64(); Falls = r.I64(); BracedOnBurn = r.I64();
    }
}
