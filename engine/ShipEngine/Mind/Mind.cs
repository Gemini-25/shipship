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
    public readonly string[] ItemNames;
    public readonly string[] RoleNames;
    public readonly int ItemCount;
    public readonly byte iExt, iRepair, iPatch, iSuit, iMask, iMedkit, iHarvest, iRation, iSalvage, iCO2, iAed;
    public readonly byte iOpioid, iApap, iAbx, iAntidote, iEpi;   // ⑦ 약품고 (의료 침대)
    public readonly byte iRegen, iArtHeart, iArtLung, iHypnotic;             // ⑦ (라-2) 재생 젤 (의료 침대) · 인공 심장 · 인공 폐 (진단 장치)
    public readonly byte iChip, iScrap, iScrapElec, iFrame;                  // ⑪ 공급 사슬 — 전자 소자 · 헌 부품 · 헌 전기 부품 · 외장 틀
    public readonly byte iWaste;                                             // 화장실 (다) — 오물 통 (든 무게는 HoldCharge kg)
    public readonly byte iBlanket;                                           // 꾸밈의 방화 담요 (함장 승인)
    /// <summary>⑪ 부품 갈래마다 갈아 끼울 부품 (전기 · 기계 · 봉함 · 거르개 — <see cref="Part"/> 차례).</summary>
    public readonly byte[] iSpare;
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
    public long Masked, GrindHeard, EarMaintained, MasksWorn;
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
        iExt = Item("소화기"); iRepair = Item("수리 꾸러미"); iPatch = Item("봉합 꾸러미"); iSuit = Item("우주복"); iMask = Item("산소 마스크"); iMedkit = Item("구급 상자"); iHarvest = Item("수확물"); iRation = Item("식량 상자"); iSalvage = Item("떼어 낸 부품"); iCO2 = Item("이산화탄소 소화기"); iAed = Item("제세동기");
        iOpioid = Item("진통제"); iApap = Item("해열제"); iAbx = Item("항생제"); iAntidote = Item("해독제"); iEpi = Item("아드레날린");
        iRegen = Item("재생 젤"); iArtHeart = Item("인공 심장"); iArtLung = Item("인공 폐"); iHypnotic = Item("수면제");
        iChip = Item("전자 소자"); iScrap = Item("헌 부품"); iScrapElec = Item("헌 전기 부품"); iFrame = Item("외장 틀"); iWaste = Item("오물 통");
        iBlanket = Item("방화 담요");
        iSpare = new[] { Item("전기 부품"), Item("기계 부품"), Item("봉함재"), Item("거르개") };

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
        WashSetup();   // 화장실 · 샤워실

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
            Battery = 1, Holding = 255, HeldItem = 255, Carrying = -1, CarriedBy = -1, WaitDoor = -1, OpenedDoor = -1, GoalTarget = -1, GoalCell = -1,
            LastKPa = (float)F.Pressure(cell), HeardUpTo = _snd?.LastEvent ?? 0, LastShout = -1_000_000, ShockRoom = -1, Mech = -1,
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
        c.Neuro0 = c.Neuro; c.Brave0 = c.Brave; c.Agree0 = c.Agree;
        c.Chrono = ChronoOf(sim.Seed, people, c.Consc, c.Open);
        c.Aim = (byte)PickAim(sim.Seed, people, c);   // ⑧ 인생 목표
        SetKnowledge(sim.Seed, people, ref c);          // ⑧ 판단 엔진 — 분야를 아는 정도
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
        c.Neuro0 = c.Neuro; c.Brave0 = c.Brave; c.Agree0 = c.Agree;
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
                SkRepair = 0.7f, SkFire = 0.8f, SkSeal = 0.8f, SkMedic = 0.3f, Spares = RobotSpares,
                KnPressure = 0.5f, KnElectric = 0.5f, KnChem = 0.3f, KnBody = 0.3f, KnStruct = 0.6f,   // 몸에 든 기본 절차 (주컴퓨터와 끊겼을 때 · 이어져 있으면 주컴퓨터의 지식을 따른다)
                Battery = 1, Holding = 255, HeldItem = 255, Carrying = -1, CarriedBy = -1, WaitDoor = -1, OpenedDoor = -1, GoalTarget = -1, GoalCell = -1,
                LastKPa = (float)F.Pressure(cell), HeardUpTo = _snd?.LastEvent ?? 0, LastShout = -1_000_000,
                Mech = _net?.AddRobot(cell) ?? -1,   // ⑥ 몸은 세상에 (전지 · 상함은 망이 계산한다)
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

    /// <summary>⑧ 판단 엔진: 맡은 일의 분야 시작값(사람 표 roles.knows · 없으면 0.1) ± 사람마다 0.1 + 열림(호기심)에 조금 — 따로 갈래 낸 난수 (다른 난수 흐름은 그대로).</summary>
    private void SetKnowledge(ulong seed, int person, ref AgentCore c)
    {
        var rng = Rng.Stream(seed, "앎", person);
        var role = c.Role < RoleNames.Length ? P.Roles[RoleNames[c.Role]] : null;
        float open = c.Open;
        float K(string d) => (float)Math.Clamp((role != null && role.Knows.TryGetValue(d, out var v) ? v : 0.1) + 0.2 * (rng.Unit() - 0.5) + 0.1 * (open - 0.5), 0, 1);
        c.KnPressure = K("기압"); c.KnElectric = K("전기"); c.KnChem = K("화학"); c.KnBody = K("몸"); c.KnStruct = K("구조");
    }

    /// <summary>그 사람(로봇)이 아는 원리 — 로봇은 주컴퓨터와 이어져 있으면 주컴퓨터의 지식(전부).</summary>
    public Know KnowOf(Agent a, bool computerLinked = false)
    {
        if (!a.IsPerson && computerLinked) return Know.All;
        ref var c = ref a.C;
        Span<float> lv = stackalloc float[] { 1f, c.KnPressure, c.KnElectric, c.KnChem, c.KnBody, c.KnStruct };
        return KnowledgeTable.Load().KnowOf(lv);
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
    /// <summary>판단 일지 칸 수 (사람 · 로봇마다 — 자세한 판에서 몇 분이면 밀리던 여덟 칸을 32로: docs/UI2.md §7 결함 2 · 136명 × 32칸 약 200KB · 찍지 않는다).</summary>
    public const int JudgedKeep = 32;

    /// <summary>판단 일지를 쓰나 (세계마다 — 기본 끔 · 찍지 않는다: 되살린 세계 · 복제 · 상상 · 앞선 세계는 쓰지 않는다 · 화면과 시험이 산 세계에서 켠다).</summary>
    public bool Journal;

    /// <summary>판단 일지 한 칸 (쓰기만 — 켠 세계에서만 · 고리는 첫 쓰기에 잡는다).</summary>
    private void Jot(Agent a, in Judgment j)
    {
        if (!Journal) return;
        (a.Judged ??= new Ring<Judgment>(JudgedKeep)).Add(j);
    }

    /// <summary>판단 일지 (쓰기만): 주컴퓨터가 이 로봇을 그 일에서 놓아줬다.</summary>
    public void NoteReleased(int agent, long t, GoalKind goal, int cell)
    {
        if (agent < 0 || agent >= Agents.Count) return;
        var a = Agents[agent];
        Jot(a, new Judgment { Tick = t, Goal = goal, Target = a.C.AssignedTarget, Cell = cell, Method = 255, Held = a.C.Holding, Out = JudgeOut.Released, Why = a.C.Snag, By = -2 });
    }

    public void AssignJob(int agent, GoalKind goal, int target, int arg, long until)
    {
        var a = Agents[agent];
        a.C.AssignedGoal = goal; a.C.AssignedTarget = target; a.C.AssignedArg = arg; a.C.AssignedUntil = until;
        Jot(a, new Judgment { Tick = _nowTick, Goal = goal, Target = target, Cell = -1, Method = 255, Held = a.C.Holding, Out = JudgeOut.Assigned, By = -2 });   // 판단 일지 (쓰기만)
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
        if (_st != null) foreach (var x in _st.Props) if (x.Kind == 0 && x.What == item && x.State != 5) n++;   // 바닥에 놓인 것 (④ 물체 장부)
        return n;
    }

    /// <summary>그 방 설비에 그 물건이 남아 있나 (우주복 보관함 · 작업대 …) — 가장 가까운 그 설비 (없으면 −1).</summary>
    public int StockIn(int room, byte item)
    {
        for (int fu = 0; fu < L.Furniture.Count; fu++)
            if (L.Furniture[fu].Room == room && L.Furniture[fu].Cells.Length > 0 && Stock[fu * ItemCount + item] > 0) return fu;
        return -1;
    }

    /// <summary>그 방 설비들에 든 그 물건 수.</summary>
    public int CountIn(int room, byte item)
    {
        int n = 0;
        for (int fu = 0; fu < L.Furniture.Count; fu++) if (L.Furniture[fu].Room == room) n += Stock[fu * ItemCount + item];
        return n;
    }

    /// <summary>⑩ 판단 엔진: 지어낸 계획의 한 걸음으로 맡긴 일 (AssignedArg) — 로봇은 제 판단보다 계획을 따른다 (배터리가 바닥나는 때만 빼고).</summary>
    public const int PlanBound = -7;

    public void Assign(Sim sim, int agent, GoalKind goal, int target, Belief b, long until, int arg = 0)
    {
        var a = Agents[agent];
        if (b.Kind != BeliefKind.None) Learn(a, sim.Tick, b.Kind, b.Cell, b.Subject, b.Conf, BeliefSource.Screen, b.Cause, b.Size);
        a.C.AssignedGoal = goal; a.C.AssignedTarget = target; a.C.AssignedUntil = until;
        if (arg != 0 || a.C.AssignedArg == PlanBound || goal == GoalKind.Rewire) a.C.AssignedArg = arg;   // 연장선: 끌어올 방 (+1 · 0이면 맡은 이가 고른다 — 앞서 맡은 일의 덧붙임이 남지 않게)
        Jot(a, new Judgment { Tick = sim.Tick, Goal = goal, Target = target, Cell = b.Cell, Method = 255, Held = a.C.Holding, Out = JudgeOut.Assigned, By = -2 });   // 판단 일지 (쓰기만)
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
        GoalKind.Craft => "물건을 만든다", GoalKind.Maintain => "부품을 미리 간다", GoalKind.Clean => "곰팡이를 닦는다", GoalKind.Harvest => "작물을 거둔다", GoalKind.Exercise => "운동한다", GoalKind.Brawl => "몸싸움 중", GoalKind.Restock => "식량을 주방으로 나른다", GoalKind.Cook => "밥을 짓는다", GoalKind.Shelter => "대피실로 간다", GoalKind.Meeting => "회의에 간다", GoalKind.Improvise => "제 궁리대로 해 본다", GoalKind.Cpr => "가슴을 누른다", GoalKind.Surgery => "수술한다", GoalKind.Attack => "주저앉아 숨을 고른다", GoalKind.Withdraw => "침대에 틀어박혀 있다", GoalKind.Counsel => "이야기를 들어 준다", GoalKind.Haul => "헌 부품을 정제기로 나른다", GoalKind.Tighten => "볼트를 조인다", GoalKind.Toilet => "화장실에 간다", GoalKind.Wash => "씻는다", GoalKind.ToiletCare => "변기를 손질한다", GoalKind.Stash => "든 것을 제자리에 둔다", GoalKind.Refill => "쓴 것을 채운다", GoalKind.Gather => "떨어진 것을 줍는다", _ => "",
    };

    public static string BeliefName(BeliefKind k) => k switch
    {
        BeliefKind.Fire => "불", BeliefKind.Smoke => "연기", BeliefKind.Breach => "구멍", BeliefKind.Vacuum => "공기가 빠짐", BeliefKind.Injured => "쓰러진 사람",
        BeliefKind.Dead => "죽은 사람", BeliefKind.Broken => "멈춘 장치", BeliefKind.Bang => "쾅 소리", BeliefKind.Strain => "끼익 소리", BeliefKind.Leak => "새는 소리",
        BeliefKind.SmokeSmell => "타는 냄새", BeliefKind.BadAir => "이상한 공기", BeliefKind.Water => "고인 물", BeliefKind.Spark => "불꽃", BeliefKind.StuckDoor => "안 열리는 문",
        BeliefKind.Help => "비명", BeliefKind.Cold => "추위", BeliefKind.Hot => "더위", BeliefKind.Grind => "이상한 기계 소리", BeliefKind.GravityBack => "중력이 돌아온다", _ => "",
    };

    /// <summary>머릿속 한 줄 (화면의 생각 칸).</summary>
    public string Thought(int agent)
    {
        var a = Agents[agent];
        string s = a.C.Goal == GoalKind.Relax && a.IsPerson ? HobbyNameAt((Hobby)a.C.Hobby, RelaxSpot(a)) : GoalName(a.C.Goal);
        if (a.Plan.Count > 0 && a.C.StepIndex < a.Plan.Count)
        {
            string step = StepText(a, a.Plan[a.C.StepIndex]);
            if (step.Length > 0) s += " — " + step;
        }
        return s;
    }

    /// <summary>계획 걸음 하나의 글 ('가는 중' · '소화기 꺼내는 중' … — 머릿속 한 줄과 자세한 판의 계획 걸음이 같은 글을 쓴다 · 그 걸음을 든 것으로 읽는다).</summary>
    public string StepText(Agent a, in PlanStep st)
    {
        return st.Kind switch
        {
            StepKind.GoTo => "가는 중", StepKind.Take => $"{ItemNames[st.Arg]} 꺼내는 중", StepKind.Spray => a.C.Holding == iBlanket ? "담요로 덮는 중" : a.C.Holding == iCO2 ? "소화기를 뿜는 중" : "물을 뿌리는 중", StepKind.Seal => "막는 중", StepKind.Brace => "덧대는 중", StepKind.Craft => "만드는 중", StepKind.Maintain => "가는 중(정비)", StepKind.Tighten => "조이는 중", StepKind.Clean => "닦는 중", StepKind.Harvest => "거두는 중", StepKind.Stow => "갈무리하는 중", StepKind.TakeFood => "식량 상자 꺼내는 중",
            StepKind.Sanitize => st.Arg switch { 0 => "변기를 닦는 중", 1 => "막힌 변기를 뚫는 중", 2 => "오물 통을 드는 중", 4 => "내려놓은 오물 통을 줍는 중", _ => "오물 통을 비우는 중" }, StepKind.Fix => "고치는 중", StepKind.PickUp => "업는 중", StepKind.Drop => "내려놓는 중", StepKind.Wear => "입는 중", StepKind.Look => "살펴보는 중",
            StepKind.Put => st.Arg >= 1 ? "제자리에 넣는 중" : "넣는 중", StepKind.Lay => "내려놓는 중", StepKind.Gather => "줍는 중",
            StepKind.Refill => st.Arg == iExt ? "물을 채우는 중" : st.Arg == iCO2 ? "이산화탄소를 채우는 중" : "산소를 채우는 중", _ => "",
        };
    }

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Bind(Sim sim)
    {
        _out ??= sim.Find<Outside>();
        _life ??= sim.Find<Life>(); _net ??= sim.Find<Network>(); _st ??= sim.Find<Structure>();
        _chem ??= sim.Find<Chemistry>(); _snd ??= sim.Find<Sound>(); _light ??= sim.Find<Light>(); _soc ??= sim.Find<Society>(); _work ??= sim.Find<Work>();
    }

    /// <summary>마음 안 단계별 시간 (실험실 --profile — 켜 두면 잰다 · 생각 · 걸음은 사람 · 로봇마다 모아서).</summary>
    public static Dictionary<string, double>? Prof;
    /// <summary>마음 안 단계별 새로 잡은 바이트 (Prof와 함께 · null이 아니면 잰다).</summary>
    public static Dictionary<string, long>? ProfAlloc;
    private static long _lapAlloc;
    private static long Lap(string name, long t0)
    {
        if (Prof == null || Sim.InWorker) return 0;   // 계산 일꾼은 정적 표를 쓰지 않는다 (docs/ENGINE.md §3-10)
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        Prof[name] = Prof.GetValueOrDefault(name) + (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (ProfAlloc != null) { long a = GC.GetTotalAllocatedBytes(true); if (_lapAlloc != 0) ProfAlloc[name] = ProfAlloc.GetValueOrDefault(name) + a - _lapAlloc; _lapAlloc = a; return System.Diagnostics.Stopwatch.GetTimestamp(); }
        return t1;
    }

    /// <summary>조사용 (Prof가 켜져 있을 때만): 사람 · 로봇마다 목표 갈래로 모은 시간 — '걸음:FightFire' 꼴.</summary>
    private static void LapWho(string what, Agent a, long t0)
    {
        if (Sim.InWorker) return;   // 계산 일꾼은 정적 표를 쓰지 않는다
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (ms < 0.2) return;
        string k = $"{what}:{a.C.Kind}#{a.Id}:{a.C.Goal}";
        Prof![k] = Prof.GetValueOrDefault(k) + ms;
    }

    public void Step(Sim sim, double dt)
    {
        long t = Prof != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (ProfAlloc != null) _lapAlloc = GC.GetTotalAllocatedBytes(true);
        Bind(sim);
        ClearOldActs(sim.Tick);   // 오랜 사회적 행동은 사회가 읽었다
        RationDay(sim.Tick);
        if (Agents.Count == 0) { ApplyPlugs(); PutActors(); return; }
        BeginWalks();
        F.RebuildNow();
        t = Lap("준비:장 다시", t);
        IndexBodies();
        HearCall(sim);  // 판단 이관 (가): 새 모임의 부름을 듣고 저마다 갈지 정한다
        Drag(sim, dt);  // ③ 국소 현상 — 감압 돌풍에 끌려가는 사람 · 구멍에 빨려 붙은 몸과 물건
        t = Lap("준비:끌림", t);
        foreach (var a in Agents) UpdateUp(sim, a);
        t = Lap("준비:움직일 수 있나", t);
        Inertia(sim);   // ⑤ 점화 — 갑자기 오른 가속에 선 사람이 넘어진다
        BodyJolt(sim);  // ④ 칸 강체 — 몸이 덜컹이면 선 사람이 넘어진다
        Floating(sim, dt);   // 인공 중력 — 떠오르고 · 익숙해지고 · 중력이 돌아오면 떨어진다
        t = Lap("준비:관성 · 중력", t);
        foreach (var a in Agents) Sense(sim, a, dt);
        t = Lap("감각", t);
        _said.RemoveAll(x => x.tick < sim.Tick - Sound.KeepTicks - 1);
        BuildClaims();
        if (sim.Tick % 25 == 0) { Learn(sim); Places(sim); AimStep(sim); LessonStep(sim); WearyStep(sim); HeartStep(sim); LetterStep(sim); AheadStep(sim); }
        FlashbackQueue(sim);   // ⑧ 그날이 되살아났다 (이번 틱에 새로 믿게 된 위험)
        t = Lap("배우기 · 자리", t);
        foreach (var a in Agents)
        {
            if (a.C.Up != 0 && a.IsPerson) Instinct(sim, a);
            bool think = a.C.Up != 0 && a.C.Mode == 0 && ((sim.Tick + a.Id) % 2 == 0 || a.Plan.Count == 0 || a.C.StepIndex >= a.Plan.Count);
            if (think) Decide(sim, a);
            if (Prof != null) LapWho("생각", a, t);
            t = Lap("본능 · 생각", t);
            WithdrawDoor(a);
            WithdrawCook(sim, a);
            WithdrawShower(sim, a);   // 씻다 말고 나가면 물을 잠그거나 · 틀어 둔 채
            HeedWashCall(a, sim.Tick);   // 주컴퓨터가 다른 화장실이 비었다고 하면 옮긴다
            string? sk = Prof != null && a.C.StepIndex < a.Plan.Count ? a.Plan[a.C.StepIndex].Kind.ToString() : null;
            if (a.C.Up != 0 || IsSleeping(a)) Advance(sim, a, dt);
            if (Prof != null) LapWho("걸음 " + sk, a, t);
            t = Lap("걸음 · 일", t);
            Needs(sim, a, dt);
            t = Lap("몸의 필요", t);
        }
        BlockDoors();
        Lap("문 붙잡기", t);
        TalkStep(sim);     // 판단 이관 (나): 말 걸기 · 이야기 · 대답 · 별명
        FeudStep(sim);     // 판단 이관 (다): 편들기 · 말리기 · 덤비기 · 주먹 · 헤어지기 · 몰래 방해
        SecretStep(sim);   // 판단 이관 (라): 털어놓기 · 숨기기 · 둘러대기 · 들은 말 · 기록 묻기
        GatherStep(sim);   // 판단 이관 (가): 부르기 · 말하기 · 미루기 (사회가 같은 틱에 결과를 계산한다)
        EndWalks();
        PutActors();
    }

    private PropActor[] _actorBuf = new PropActor[16];
    private int[] _actorPathBuf = new int[64];
    // 이번 걸음에 지나간 칸 (사람 · 로봇마다 처음 칸부터 ≤ 8 — 찍지 않는다: 걸음마다 처음 칸에서 다시 잰다)
    public const int WalkMax = 8;
    private int[] _walk = Array.Empty<int>();
    private byte[] _walkN = Array.Empty<byte>();

    /// <summary>걸음 처음: 저마다 지금 칸에서 길을 다시 잰다.</summary>
    private void BeginWalks()
    {
        if (_walkN.Length < Agents.Count) { _walk = new int[WalkMax * (Agents.Count + 8)]; _walkN = new byte[Agents.Count + 8]; }
        for (int i = 0; i < Agents.Count; i++) { _walk[WalkMax * i] = Agents[i].C.Cell; _walkN[i] = 1; }
    }

    /// <summary>걸음 끝: 저마다 이번 걸음의 마지막 칸 쪽 (바라보는 쪽 — 걷지 않았으면 그대로) · 걸은 칸 수를 남긴다 (찍는다 — 다음 틱 걸음보다 먼저 쓰러지거나 놓으면 그것을 본다).</summary>
    private void EndWalks()
    {
        for (int i = 0; i < Agents.Count; i++)
        {
            ref var c = ref Agents[i].C;
            var w = WalkOf(i, Agents[i].IsPerson ? _life![c.Body].Cell : c.Cell);
            c.WalkCells = (byte)(w.Length >= 2 ? w.Length - 1 : 0);
            if (w.Length >= 2) { c.FaceX = (sbyte)Math.Clamp(L.X(w[^1]) - L.X(w[^2]), -1, 1); c.FaceY = (sbyte)Math.Clamp(L.Y(w[^1]) - L.Y(w[^2]), -1, 1); }
        }
    }

    /// <summary>그 사람 · 로봇이 한 칸 옮겼다 (길에 더한다 — 넘치면 오래된 칸부터 덜어 마지막 여덟 칸).</summary>
    private void Walked(int agent, int cell)
    {
        if ((uint)agent >= (uint)_walkN.Length || _walkN[agent] == 0) return;
        int at = WalkMax * agent, n = _walkN[agent];
        if (n == WalkMax) { Array.Copy(_walk, at + 1, _walk, at, WalkMax - 1); n--; }
        _walk[at + n] = cell; _walkN[agent] = (byte)(n + 1);
    }

    /// <summary>이번 걸음 길 (그 사람 · 로봇이 지금 칸에서 끝나는 길만 — 업혀 가거나 끌려가 길 밖으로 옮겨졌으면 비었다).</summary>
    private ReadOnlySpan<int> WalkOf(int agent, int cell)
    {
        if ((uint)agent >= (uint)_walkN.Length || _walkN[agent] < 2) return default;
        int at = WalkMax * agent, n = _walkN[agent];
        return _walk[at + n - 1] == cell ? _walk.AsSpan(at, n) : default;
    }

    /// <summary>
    /// 걷던 빠르기 (m/s — 걸음 길의 마지막 칸 쪽 · 칸 수만큼): 쓰러지며 떨군 것이 그 빠르기로 날아간다.
    /// 이번 틱에 걸었으면 이번 걸음 길 · 아직 걷기 전이면 지난 틱 걸음 (쓰러짐 · 점화 · 덜컹임 · 떠오름은 걸음보다 먼저다) · 이번 틱에 업혀 가거나 끌려 옮겨졌으면 0.
    /// </summary>
    private (double vx, double vy) WalkVelocity(Agent a)
    {
        var w = WalkOf(a.Id, a.C.Cell);
        if (w.Length >= 2)
        {
            int c0 = w[^2], c1 = w[^1];
            double k = (w.Length - 1) / Time.SecondsPerTick;
            return ((L.X(c1) - L.X(c0)) * k, (L.Y(c1) - L.Y(c0)) * k);
        }
        if ((uint)a.Id >= (uint)_walkN.Length || _walkN[a.Id] != 1 || _walk[WalkMax * a.Id] != a.C.Cell) return (0, 0);
        double kl = a.C.WalkCells / Time.SecondsPerTick;
        return (a.C.FaceX * kl, a.C.FaceY * kl);
    }

    /// <summary>
    /// ④에 산 몸 · 걷는 로봇을 원판으로 알린다 (Mind.Step 끝 — 사람은 몸 번호 · 로봇은 −1−로봇 몸 번호 · 칸 · 누웠나 · 이번 걸음에 지나간 칸 · 하던 일의 원인):
    /// ④가 다음 틱 물체 걸음에 그 길을 같은 시간표로 따라가 날아든 물체가 맞히고 · 걷던 이가 바닥 상자를 찬다 (드론은 떠 있어 바닥 물건에 닿지 않는다).
    /// </summary>
    private void PutActors()
    {
        if (_st == null) return;
        int n = 0, np = 0;
        void Add(in PropActor actor, ReadOnlySpan<int> walk)
        {
            if (n == _actorBuf.Length) Array.Resize(ref _actorBuf, n * 2);
            var x = actor;
            if (walk.Length >= 2)
            {
                if (np + walk.Length > _actorPathBuf.Length) Array.Resize(ref _actorPathBuf, Math.Max(2 * _actorPathBuf.Length, np + walk.Length));
                walk.CopyTo(_actorPathBuf.AsSpan(np));
                x.PathAt = (short)np; x.PathN = (byte)walk.Length; np += walk.Length;
            }
            _actorBuf[n++] = x;
        }
        if (_life != null)
            for (int b = 0; b < _life.Bodies.Count; b++)
            {
                ref var body = ref _life[b];
                if (body.Alive == 0 || body.Cell < 0) continue;
                if (_st.RagdollOf(b) >= 0) continue;   // 몸 물체로 ④가 셈한다 (PH14 — 원판이 제 몸에 부딪히지 않게)
                byte low = (byte)(body.Conscious == 0 || body.Asleep != 0 ? 1 : 0);
                int ag = AgentOfBody(b);
                Add(new PropActor { Tag = b, Cell = body.Cell, R = Structure.ActorR, Kg = (float)PersonKg, H = low != 0 ? 0.4f : 1.7f, Low = low, Cause = ag >= 0 ? Agents[ag].C.GoalCause : 0, Target = ag >= 0 ? GatherTarget(Agents[ag]) : 0 },
                    ag >= 0 ? WalkOf(ag, body.Cell) : default);
            }
        foreach (var a in Agents)
        {
            if (a.C.Kind != AgentKind.Robot || a.C.Mech < 0 || a.C.Damage >= 1 || a.C.Cell < 0 || a.C.Cell >= L.N || _st.RagdollOf(-1 - a.C.Mech) >= 0) continue;
            Add(new PropActor { Tag = -1 - a.C.Mech, Cell = a.C.Cell, R = Structure.ActorR, Kg = RobotKg, H = 1.2f, Cause = a.C.GoalCause, Target = GatherTarget(a) }, WalkOf(a.Id, a.C.Cell));
        }
        _st.SetActors(_actorBuf.AsSpan(0, n), _actorPathBuf.AsSpan(0, np));
    }

    /// <summary>지금 계획 걸음이 줍기면 그 물체 Id (④가 집으려는 이의 원판으로 그것을 차지 않는다 — 곁에서 손을 뻗는다 · 0 없음).</summary>
    private static int GatherTarget(Agent a) => a.C.StepIndex >= 0 && a.C.StepIndex < a.Plan.Count && a.Plan[a.C.StepIndex].Kind == StepKind.Gather ? a.Plan[a.C.StepIndex].Target : 0;

    /// <summary>바닥을 걷는 로봇 몸의 무게 (kg — 원판이 받는 몫은 ④가 셈하지 않는다: 맞음의 장부에만).</summary>
    public const float RobotKg = 120;

    /// <summary>그 사람 · 로봇의 원판 꼬리표 (④ — 사람은 몸 번호 · 로봇은 −1−로봇 몸 번호 · 드론은 없다 −1… 바닥에 닿지 않는다).</summary>
    private int ActorTag(Agent a) => a.IsPerson ? a.C.Body : a.C.Kind == AgentKind.Robot && a.C.Mech >= 0 ? -1 - a.C.Mech : int.MinValue;

    /// <summary>
    /// 든 것을 바닥에 둔다 (④ 요청 — 원판 바깥 · 걷던 쪽 (서 있었으면 바라보던 쪽) 앞부터): 내려놓기는 그 자리에 (빠르기 0) · 쓰러지며 떨굼은 걷던 빠르기로 · 놓은 이와는 잠깐 닿지 않는다. 물체 Id.
    /// </summary>
    private int PutDown(Agent a, byte item, float charge, bool thrown)
    {
        int cell = a.C.Cell;
        if (_st == null || cell < 0 || cell >= L.N) return -1;
        var (vx, vy) = WalkVelocity(a);
        int tag = ActorTag(a);
        if (tag == int.MinValue) return _st.PutItem(item, charge, cell);   // 드론은 떠서 든다 — 그 칸에
        bool walking = vx != 0 || vy != 0;
        double fx = walking ? vx : a.C.FaceX, fy = walking ? vy : a.C.FaceY;   // 앞 = 걷던 쪽 · 서 있었으면 바라보던 쪽 (④가 길이를 맞춘다)
        if (fx == 0 && fy == 0) fy = 1;   // 아직 걸은 적이 없다 — 남쪽
        return _st.DropItem(item, charge, L.X(cell) + 0.5, L.Y(cell) + 0.5, thrown ? 1 : 0, thrown ? vx : 0, thrown ? vy : 0, a.C.GoalCause, tag, fx, fy);
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
    }

    /// <summary>움직일 수 있나: 사람은 살아 있고 의식이 있고 깨어 있어야 · 로봇은 멀쩡하고 전지가 있어야.</summary>
    private void UpdateUp(Sim sim, Agent a)
    {
        if (a.IsPerson)
        {
            ref var b = ref _life![a.C.Body];
            a.C.Cell = b.Cell;
            bool up = b.Alive != 0 && b.Conscious != 0 && b.Asleep == 0 && a.C.Adrift == 0 && (_st == null || _st.RagdollOf(a.C.Body) < 0);   // ③ 우주로 떠내려간 이 · 바람에 발이 떨어져 끌려가는 이 (④ 몸 물체 — PH14)는 아무것도 할 수 없다
            if (a.C.Up != 0 && !up && b.Asleep == 0) Fall(sim, a);
            a.C.Up = (byte)(up ? 1 : 0);
            // ③ 국소 현상: 누운 이 · 자는 이는 머리가 낮다 (업힌 이는 어깨 높이) · 깨어 있는 이는 머리 높이에 연기 · 열기가 차면 몸을 낮춰 기어간다 (본능)
            b.Low = (byte)(a.C.CarriedBy < 0 && (!up || b.Cell >= 0 && b.Cell < L.N && F.Grav[b.Cell] >= FloatG && (SmokeBad(a, b.Cell) || F.HeadTemp(b.Cell, false) > CrouchK)) ? 1 : 0);
            if (b.Alive == 0 || b.Conscious == 0) { a.C.Goal = GoalKind.Down; a.C.Mode = 0; }
        }
        else
        {
            FeelMech(sim, a);   // ⑥ 몸을 느낀다 — 전지 · 상함 (판단 이관 (사))
            bool up = a.C.Damage < 1 && a.C.Battery > 0 && (_st == null || a.C.Mech < 0 || _st.RagdollOf(-1 - a.C.Mech) < 0);   // 바람에 밀려 미끄러지는 동안 (④ 몸 물체)은 제 힘이 없다
            if (a.C.Up != 0 && !up) Fall(sim, a);
            a.C.Up = (byte)(up ? 1 : 0);
        }
    }

    /// <summary>쓰러졌다 · 멈췄다: 업던 사람을 내려놓고 계획을 버린다 (든 물건은 그 자리에 떨어진다).</summary>
    private void Fall(Sim sim, Agent a)
    {
        if (a.C.Carrying >= 0) DropCarried(sim, a, false);
        DropHeld(a, true);   // 쓰러지며 놓친다 (걷던 쪽으로)
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
            // ⑥ 몸에 요청만: 어디 있나 · 얼마나 힘쓰나 · 충전독에 붙었나 — 전지 · 열 · 충격 · 물 · 전기 · 진공은 망이 계산한다 (판단 이관 (사))
            if (_net != null && c.Mech >= 0 && c.Mech < _net.Robots.Count)
            {
                ref var rb = ref _net.Robot(c.Mech);
                rb.Cell = c.Cell;
                rb.Load = (byte)(c.Up != 0 && (c.Speed > 0.1f || c.Goal is GoalKind.FightFire or GoalKind.Patch or GoalKind.Repair or GoalKind.Rescue) ? 1 : 0);
                rb.Docked = (byte)(c.Up != 0 && c.Goal == GoalKind.Charge && c.Cell == c.GoalTarget ? 1 : 0);
            }
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
        WearyNeeds(sim, a, h, asleep, others);   // ⑧ 고립의 피로
        if (!asleep && c.Goal != GoalKind.Exercise) c.Unfit = (float)Math.Min(1, c.Unfit + h / 16); // 깨어 있는 하루면 1
        // 감정: 두려움은 몇 분이면 가라앉는다 (침착할수록 빨리) · 긴장은 굶주림 · 졸림 · 두려움에 쌓이고 잠 · 쉼에 풀린다
        if (c.Fear > c.FearMax) c.FearMax = c.Fear;   // ⑧ 충격은 그 1분의 가장 큰 겁으로
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

    // ───────────────────────── 칸 도우미 ─────────────────────────

    /// <summary>걷는 중인가 (지금 단계가 칸으로 가기).</summary>
    public bool Walking(Agent a) => a.C.StepIndex < a.Plan.Count && a.Plan[a.C.StepIndex].Kind == StepKind.GoTo;
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

    private static bool IsTask(GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Repair or GoalKind.Investigate or GoalKind.Cpr or GoalKind.Surgery or GoalKind.Counsel;

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.I32(Agents.Count);
        foreach (var a in Agents)
        {
            w.List(new List<AgentCore> { a.C });
            w.List(a.Beliefs); w.List(a.Plan); w.List(a.Path); w.I32(a.PathIdx); w.List(a.Avoid); w.List(a.Doubts); w.List(a.Own);
        }
        w.Arr(Stock);
        w.I32(_said.Count);
        foreach (var (ev, tick, sp, b) in _said) { w.I32(ev); w.I64(tick); w.I32(sp); w.List(new List<Belief> { b }); }
        w.I64(Shouts); w.I64(Panics); w.I64(Extinguished); w.I64(Rescued); w.I64(Patched); w.I64(Repaired); w.I64(FledCount); w.I64(Meals); w.I64(Drinks); w.I64(Exercised); w.F64(FoodEaten); w.F64(WaterDrunk); w.F64(WaterSprayed);
        w.I64(Cooked); w.I64(Forgot); w.I64(_learnCursor); w.I64(AteTogether); w.I64(Followed);
        w.Arr(HobbyCount); SavePlaces(w); SaveRation(w);
        w.I64(Masked); w.I64(GrindHeard); w.I64(EarMaintained); w.I64(MasksWorn); w.List(GrindReports);
        w.F64(_lastG); w.I64(Falls); w.I64(BracedOnBurn);
        SaveAims(w); SaveLessons(w); SaveShelter(w); SaveCouncil(w); SaveActs(w); SaveSecrets(w); SaveWeary(w); SaveKeepOut(w); SavePonder(w); SaveHeart(w); SaveCustoms(w); SaveLetters(w); SaveAhead(w); SaveWash(w); SaveHands(w);
    }

    public void Load(StateReader r)
    {
        Agents.Clear();
        _plugsStale = true;   // 몸 막음 칸은 장의 PlugBody에서 다시 세운다
        int n = r.I32();
        for (int i = 0; i < n; i++)
        {
            var a = new Agent { Id = i, C = r.List<AgentCore>()[0] };
            a.Beliefs = r.List<Belief>(); a.Plan = r.List<PlanStep>(); a.Path = r.List<int>(); a.PathIdx = r.I32(); a.Avoid = r.List<AvoidEntry>(); a.Doubts = r.List<Doubt>(); a.Own = r.List<PlanStep>();
            Agents.Add(a);
        }
        r.ArrInto(Stock);
        _said.Clear();
        int m = r.I32();
        for (int i = 0; i < m; i++) { int ev = r.I32(); long t = r.I64(); int sp = r.I32(); var b = r.List<Belief>()[0]; _said.Add((ev, t, sp, b)); }
        Shouts = r.I64(); Panics = r.I64(); Extinguished = r.I64(); Rescued = r.I64(); Patched = r.I64(); Repaired = r.I64(); FledCount = r.I64(); Meals = r.I64(); Drinks = r.I64(); Exercised = r.I64(); FoodEaten = r.F64(); WaterDrunk = r.F64(); WaterSprayed = r.F64();
        Cooked = r.I64(); Forgot = r.I64(); _learnCursor = r.I64(); AteTogether = r.I64(); Followed = r.I64();
        HobbyCount = r.Arr<long>(); LoadPlaces(r); LoadRation(r);
        Masked = r.I64(); GrindHeard = r.I64(); EarMaintained = r.I64(); MasksWorn = r.I64(); GrindReports.Clear(); GrindReports.AddRange(r.List<GrindReport>());
        _lastG = r.F64(); Falls = r.I64(); BracedOnBurn = r.I64();
        LoadAims(r); LoadLessons(r); LoadShelter(r); LoadCouncil(r); LoadActs(r); LoadSecrets(r); LoadWeary(r); LoadKeepOut(r); LoadPonder(r); LoadHeart(r); LoadCustoms(r); LoadLetters(r); LoadAhead(r); LoadWash(r); LoadHands(r);
    }
}
