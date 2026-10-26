using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>물체 하나 (docs/PHYSICS.md §2-2 — 찍는다 · Id 차례): 바닥의 물건 · 뒤 커밋에서 가구 · 조각 · 문짝 · 몸.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Prop
{
    public int Id;                 // 바뀌지 않는 번호 (1부터 — 원인 · 그림 캐시 · 줍기 · 일감 대상)
    public byte Kind;              // 0 물건 · 1 가구 · 2 조각 · 3 문짝 · 4 판 조각 · 5 사람 몸 · 6 로봇 몸
    public ushort Form;            // 도트 꼴 (꼴 종류 번호)
    public int Piece;              // 0 온전 · n = Masks[n−1] (조각 가면)
    public short What;             // 물건 번호 (사람 표 물건 차례) · 가구 번호 · 몸 번호 · 조각이면 부모의 What
    public byte Mat;               // 겉 재질 (바닥 마찰 · 반발)
    public int Parent;             // 조각이 나온 물체 Id (0 없음)
    public int Host;               // 실린 몸: −1 우주 · 그 밖에는 그 칸의 몸 (몸이 없으면 0)
    public int On;                 // 받침: 0 바닥 · f+1 가구 윗면 · −(id+1) 물체 위
    public double X, Y, Th;        // 무게중심 (도면 m — 칸 가운데는 칸 + 0.5) · 자세 (감지 않는다 — 몇 바퀴 돌았나 안다)
    public double Vx, Vy, W;       // 바닥에 대한 빠르기 · 각속도
    public float Z, Vz;            // 밑면 높이 (바닥 0 · 탁자 위 0.75) · 오르내림
    public float Kg, Inertia;      // 꼴(가면)에서 · 담긴 물 포함
    public byte Pose, State;       // 0 섬 · 1~4 누움 (+x −x +y −y) / 0 놓임 · 1 미끄러짐 · 2 넘어지는 중 · 3 떨어지는 중 · 4 떠 있음 · 5 우주 · 6 구멍을 막음 · 7 문틀에 낌
    public sbyte TiltDir;          // 넘어지는 쪽
    public float Tilt, TiltW;      // 넘어지는 각 (0 → π/2) · 각속도
    public float Roll;             // 구른 각 (그림의 띠)
    public float G;                // 지난 틱 바닥 중력 (깨우는 입력)
    public byte Asleep, Flags;     // Flags: 1 꾸밈 (제자리가 있다) · 2 둥글다 · 4 깨지기 쉬움 (가루 그림)
    public int PlugAt;             // ③ 빨려 붙어 막은 구멍 칸 + 1 (0 없음)
    public float Charge, Liquid;   // 남은 양 (소화기 물 · 우주복 산소 …) · 담긴 물 (몰)
    public float Hurt;             // 쌓인 맞음 (J/kg — 깨짐 피로)
    public uint Dent0, Dent1;      // 찌그러짐 둘 (u · v · 반지름 도트 8비트씩)
    public int Cause;              // 마지막으로 크게 민 원인
    public float PushImp;          // 그 밂 (틱마다 반)
    public int Dropper;            // 놓은 이 (꼬리표 + 1 · 로봇은 음수 그대로 · 0 없음 — 놓은 틱부터 두 틱 동안 그 원판과 닿지 않는다 — ④가 한 틱 늦게 따라가는 걸음까지)
    public float Still;            // 고요하게 지난 초
    public long Since;             // 놓인 · 마지막으로 선 틱
    public int Spare0, Spare1;     // 판을 이 단에서 한 번만 올리려고 남긴 칸
}

/// <summary>
/// 조각 가면 (꼴 안 상자 · 무게중심 · 비트 시작 — 비트는 <see cref="Structure.MaskBits"/>: 상자의 줄마다 ulong 하나 (꼴 x 비트) · 부위 (255 기둥 통째 · 아니면 그 부위의 층만 — 이음매로 갈린 조각: PH8)).
/// 꼴은 늘 깨지기 전 온전한 물건의 꼴 (물건 · 누움) 틀이다 — 조각이 다시 깨지면 그 가면의 부분 집합.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PropMask
{
    public ushort Form;
    public byte X0, Y0, W, H;
    public float Cx, Cy;
    public int Bit;
    public byte Part;
}

/// <summary>물체가 무엇을 맞혔다 (꼬리표 · 칸 · 에너지 · 질량 · 빠르기 · 날카로움 · 원인 · 때) — ⑦ · ⑥이 제 꼬리표로 읽고 비운다.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PropHit
{
    public int Tag, Cell;
    public float E, Kg, Speed;
    public byte Sharp;
    public int Cause;
    public long Tick;
}

/// <summary>
/// 물체가 맞힐 수 있는 이 (⑧이 틱 끝마다 알린다 — 열어 보지 않는 꼬리표의 원판: 사람이면 ⑦ 몸 번호 · 로봇이면 −1−로봇 번호 · 칸 · 반지름 · 무게 · 키 · 누웠나 ·
/// 그 틱에 지나간 칸 길 (처음 칸부터 지금 칸까지 ≤ 8 — 길 묶음의 PathAt부터 PathN개 · 1이면 서 있었다) · 하던 일의 원인 (찬 것의 원인이 잇는다) ·
/// 집으려는 물체 Id (0 없음 — 그것은 차지 않는다: 곁에서 손을 뻗는다)).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PropActor
{
    public int Tag, Cell;
    public float R, Kg, H;
    public byte Low;
    public short PathAt;
    public byte PathN;
    public int Cause, Target;
}

/// <summary>
/// ④ 물체 장부 (docs/PHYSICS.md PH3a): 바닥에 놓인 물건은 구조에 있다 (마음이 쥐던 목록을 옮겼다 — 차례는 놓은 차례 그대로).
/// 놓기 · 집기 · 칸 옮김 · 막음은 위 엔진의 요청이다 — 바람 · 점화 · 덜컹임에 미끄러지는 셈은 아직 ⑧의 다리가 같은 자리에서 하고 칸 옮김만 여기에 요청한다 (PH4가 걷는다).
/// 칸은 찍지 않고 자리(칸 가운데)에서 늘 센다.
/// </summary>
public sealed partial class Structure
{
    /// <summary>물체 (Id 차례 — 놓은 차례 · 지우면 남은 것의 차례는 그대로).</summary>
    public readonly List<Prop> Props = new();
    /// <summary>조각 가면 · 그 비트 (64×64까지 — 가면마다 64 ulong).</summary>
    public readonly List<PropMask> Masks = new();
    public readonly List<ulong> MaskBits = new();
    /// <summary>칸마다 가루 (kg — 유리 · 세라믹이 깨진 것) · 그 재질.</summary>
    public float[] Grit = Array.Empty<float>();
    public byte[] GritMat = Array.Empty<byte>();
    /// <summary>가구마다 제자리 (1) · 볼트가 버티는 몫 단 (0~10 — 처음 10 · ⑥이 맞춘다) · 그 물체 Id (없으면 −1).</summary>
    public byte[] FurnPlaced = Array.Empty<byte>(), FurnAnchor = Array.Empty<byte>();
    public int[] FurnProp = Array.Empty<int>();
    /// <summary>물체가 맞힌 것 (⑦ · ⑥이 읽고 비운다).</summary>
    public readonly List<PropHit> PropHits = new();
    /// <summary>장부: 우주로 간 운동량 · 각운동량 · 자리 바로잡음이 바꾼 각운동량 · 몸이 없는 배에 준 몫 · 깨진 질량 · 가루.</summary>
    public double PropPxGone, PropPyGone, PropLGone, FixL, ToHostPx, ToHostPy, ToHostL, BrokenKg, GritKg;
    /// <summary>다음 물체 Id (1부터 — 0은 없음).</summary>
    public int NextPropId = 1;
    /// <summary>셈: 물체 걸음 · 튐 · 깨짐 · 넘어짐 · 뜯김 · 구름 · 섬 놓침.</summary>
    public long PropSteps, Bounces, Shattered, Toppled, TornLoose, Rolled, IslandMiss;

    private void PropArrays()
    {
        Grit = new float[L.N]; GritMat = new byte[L.N];
        int n = L.Furniture.Count;
        FurnPlaced = new byte[n]; Array.Fill(FurnPlaced, (byte)1);
        FurnAnchor = new byte[n]; Array.Fill(FurnAnchor, (byte)10);
        FurnProp = new int[n]; Array.Fill(FurnProp, -1);
        FurnAt = new int[n]; Array.Fill(FurnAt, -1);   // PH10 — 연료 · 열용량은 도면의 제자리 칸들에
        DoorLeafOut = new byte[L.Doors.Count];
    }

    /// <summary>물체의 칸 (자리의 floor — 도면 밖이면 −1).</summary>
    public int CellOf(in Prop p)
    {
        if (p.State == 5 && p.Host == -1) return -1;   // 세상 틀로 난다 — 배의 칸이 아니다 (PH11)
        double x = Math.Floor(p.X), y = Math.Floor(p.Y);
        return x >= 0 && y >= 0 && x < L.W && y < L.H ? L.Index((int)x, (int)y) : -1;
    }

    /// <summary>그 Id의 물체 자리 (목록 차례 — 없으면 −1). Id 차례로 쌓이니 반으로 나눠 찾는다.</summary>
    public int IndexOf(int id)
    {
        int lo = 0, hi = Props.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1, v = Props[mid].Id;
            if (v == id) return mid;
            if (v < id) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>
    /// 요청: 물건을 놓는다 (그 칸 가운데 · 남은 양 그대로) — 새 물체 Id. 높이 z (밑면 m — 0이면 손 높이에서 내려놓듯 그 아래 받침 (탁자 윗면 · 바닥)에 선다) ·
    /// 받침보다 높으면 그 높이에서 떨어진다 (PH7). 담긴 물 liquid (몰 — 컵 · 물병: 깨지거나 넘어지면 그 칸 바닥으로 쏟는다 · PH8).
    /// </summary>
    public int PutItem(int item, float charge, int cell, float z = 0, float liquid = 0)
    {
        if (item < 0 || cell < 0 || cell >= L.N) return -1;
        int id = NextPropId++;
        double x = L.X(cell) + 0.5, y = L.Y(cell) + 0.5, zs = SupportAt(x, y, Math.Max(z, HandZ), out int on, out _);
        var p = new Prop { Id = id, Kind = 0, What = (short)item, X = x, Y = y, Z = (float)Math.Max(z, zs), On = on, Charge = charge, Liquid = Math.Max(0, liquid), G = F.Grav[cell] };
        if (z > zs + ZEps) { FillBody(ref p); p.State = 3; p.On = 0; }
        Props.Add(p);
        return id;
    }

    /// <summary>요청: 물건을 집는다 (목록에서 뺀다 — 움직이는 것은 못 집는다). 없거나 못 집으면 거짓 → 부른 이가 다른 길.</summary>
    public bool TakeItem(int id, int tag = 0)
    {
        int i = IndexOf(id);
        if (i < 0 || Props[i].Kind != 0 || Props[i].State is not (0 or 6)) return false;
        Props.RemoveAt(i);
        return true;
    }

    /// <summary>그 칸에 놓인 물건 (Id 차례).</summary>
    public IEnumerable<int> ItemsAt(int cell)
    {
        foreach (var p in Props) if (p.Kind == 0 && CellOf(p) == cell) yield return p.Id;
    }

    /// <summary>요청 (시험 차림 — 빨려 붙음 · 떨어짐): 구멍을 막았다 (구멍 칸 + 1 · 0이면 떨어졌다).</summary>
    public void SetItemPlug(int id, int plugAt)
    {
        int i = IndexOf(id);
        if (i < 0) return;
        var p = Props[i];
        p.PlugAt = plugAt; p.State = (byte)(plugAt != 0 ? 6 : 0);
        Props[i] = p;
    }

    // ───────────────────────── 맞힐 수 있는 이 · 맞음 장부 (PH4b) ─────────────────────────

    private PropActor[] _actors = new PropActor[16];   // 찍는다 — ⑧이 틱 끝마다 쓰고 ④가 다음 틱에 읽는다
    private int _nActors;
    public const float ActorR = 0.25f;
    /// <summary>이번 틱 걷는 원판 수 (보기 · 시험).</summary>
    public int ActorCount => _nActors;

    /// <summary>
    /// 요청 (⑧ Mind.Step 끝): 물체가 맞힐 수 있는 이들 · 그 틱에 걸은 길 (paths — 원판마다 PathAt부터 PathN칸) — ④는 다음 틱 물체 걸음에 그 길을 같은 시간표로 따라간다
    /// (④가 ⑧보다 먼저 돈다 · 화면도 걸음을 한 틱 늦게 메운다). 찍는다 (되살린 세계의 첫 틱도 같은 원판). ④는 꼬리표를 열어 보지 않는다.
    /// </summary>
    public void SetActors(ReadOnlySpan<PropActor> actors, ReadOnlySpan<int> paths = default)
    {
        if (_actors.Length < actors.Length) _actors = new PropActor[actors.Length * 2];
        actors.CopyTo(_actors);
        _nActors = actors.Length;
        if (_actorPath.Length < paths.Length) _actorPath = new int[paths.Length * 2];
        paths.CopyTo(_actorPath);
        _nActorPath = paths.Length;
        for (int a = 0; a < _nActors; a++)
            if (_actors[a].PathN > 0 && _actors[a].PathAt + _actors[a].PathN > _nActorPath) _actors[a].PathN = 0;   // 길 묶음 밖이면 서 있었다로
    }

    /// <summary>요청 (⑧ 다리 — 칸으로 옮기는 날림 · PH4d가 걷는다): 물체가 그 꼬리표를 맞혔다 (에너지 J · 빠르기 · 원인) — ⑦ · ⑥이 읽는다.</summary>
    public void AddPropHit(long tick, int tag, int cell, double e, double kg, double speed, int cause) =>
        PropHits.Add(new PropHit { Tag = tag, Cell = cell, E = (float)e, Kg = (float)kg, Speed = (float)speed, Cause = cause, Tick = tick });

    /// <summary>물체의 이름 (물건 표 — 원인 글).</summary>
    public string PropName(in Prop p)
    {
        _itemNames ??= T.People.Items.Keys.ToArray();
        bool furn = p.Kind == 1 || p.Kind == 2 && (p.Flags & FurnPieceFlag) != 0;   // 가구 · 가구 조각 (PH10 — What = 가구 번호)
        string name = p.Kind == 5 ? "사람" : p.Kind == 6 ? "로봇" : p.Kind == 4 ? "판 조각" : p.Kind == 3 ? "문짝" : furn ? (p.What >= 0 && p.What < L.Furniture.Count ? T.Equip(L.Furniture[p.What].Type).Name : "설비")
            : p.What >= 0 && p.What < _itemNames.Length ? _itemNames[p.What] : "물건";
        return p.Kind == 2 ? name + " 조각" : name;
    }

    // ───────────────────────── 문짝 (PH4b — §3-6) ─────────────────────────

    public const double DoorJamKg = 10, LeafForce = 150;   // 이보다 무거운 것이 물리면 문이 낀다 · 문짝 판이 미는 힘 (N)

    /// <summary>
    /// 문 칸의 문짝 줄: 미는 축 (true면 x — 좌우가 벽 · 남북으로 지난다) · 문짝이 들어오는 쪽 끝 (기둥 안쪽 · m) · 문틈 폭 · 문짝 띠 (칸 — 다른 축).
    /// 문짝은 낮은 쪽 기둥에서 나와 높은 쪽 기둥까지 닫힌다 — 끝 = 낮은 끝 + (1 − 열림) × 폭.
    /// </summary>
    private (bool alongX, double lo, double w, double band) Leaf(int door)
    {
        int c = L.Doors[door].Cell, axis = DoorAxis(c);
        bool alongX = axis == 1 || axis == 0 && L.Doors[door].Vertical;
        return (alongX, (alongX ? L.X(c) : L.Y(c)) + 4 * DotForm.Dot, 24 * DotForm.Dot, alongX ? L.Y(c) : L.X(c));
    }

    /// <summary>
    /// 문짝이 닫혀 갈 수 있는 끝 (0 닫힘 ~ 1 열림): 문 칸 띠에 몸이 든 물체마다 문짝 쪽 끝에서 멈춘다 — 가장 일찍 막는 것이 붙든 물체 (같으면 작은 Id).
    /// 찍힌 상태로만 센다 (꼴 표본 — 껍질 꼭짓점 · 테두리).
    /// </summary>
    private float LeafFloor(int door, out int heldBy)
    {
        heldBy = -1;
        if (Props.Count == 0) return 0;
        var (alongX, lo, w, band) = Leaf(door);
        int c = L.Doors[door].Cell;
        double mx = L.X(c) + 0.5, my = L.Y(c) + 0.5;
        float floor = 0;
        foreach (var p in Props)
        {
            if (p.State == 5 || Math.Abs(p.X - mx) > 2 || Math.Abs(p.Y - my) > 2) continue;
            var sm = SamplesOf(PropForm(p));
            if (Math.Abs(p.X - mx) > 0.5 + sm.reach || Math.Abs(p.Y - my) > 0.5 + sm.reach) continue;
            double cs = Math.Cos(p.Th), sn = Math.Sin(p.Th), a = double.MaxValue;
            for (int k = 0; k < sm.x.Length; k++)
            {
                double x = p.X + cs * sm.x[k] - sn * sm.y[k], y = p.Y + sn * sm.x[k] + cs * sm.y[k];
                double along = alongX ? x : y, across = alongX ? y : x;
                if (across < band || across >= band + 1) continue;
                a = Math.Min(a, along);
            }
            if (a == double.MaxValue) continue;
            float need = (float)Math.Clamp(1 - (a - lo) / w, 0, 1);
            if (need > floor) { floor = need; heldBy = p.Id; }
        }
        return floor;
    }

    /// <summary>그 문의 닫히는 문짝을 붙든 물체 (Id — 문 칸 띠에 든 것 가운데 가장 일찍 막는 것 · 없으면 −1). ⑧ · ⑩이 보이면 안다.</summary>
    public int DoorHeldBy(int door)
    {
        float f = LeafFloor(door, out int id);
        return f > 0 ? id : -1;
    }

    /// <summary>
    /// 닫히는 문짝이 물체에 닿았다 (구조 걸음 — 문): 10kg 넘으면 문이 낀다 (물체는 문틀에 낌 · 원인 '문틀에 끼었다') ·
    /// 가벼우면 문짝 판이 문 줄 밖으로 밀어낸다 (물체 가운데가 있는 쪽 — 그 몸이 띠를 벗어나 마찰로 설 만큼 · 150N × 틱 넘게는 못 민다).
    /// </summary>
    private void LeafHit(Sim sim, int door, int id, double dt)
    {
        int i = IndexOf(id);
        if (i < 0) return;
        var p = Props[i];
        if (p.Kg <= 0) FillBody(ref p);
        int c = L.Doors[door].Cell;
        if (p.Kg > DoorJamKg)
        {
            p.State = 7; p.Vx = p.Vy = p.W = 0; p.Still = 0; p.Since = _now;
            Props[i] = p;
            int jc = sim.Causes.Add(CauseKind.Machine, $"{Ko.I(PropName(p))} 닫히는 문짝에 물려 문틀에 끼었다", sim.Tick, c, p.Cause);
            JamDoor(sim, door, jc);
            return;
        }
        var (alongX, _, _, band) = Leaf(door);
        var sm = SamplesOf(PropForm(p));
        double cs = Math.Cos(p.Th), sn = Math.Sin(p.Th), lo = double.MaxValue, hi = double.MinValue;
        for (int k = 0; k < sm.x.Length; k++)
        {
            double across = alongX ? p.Y + sn * sm.x[k] + cs * sm.y[k] : p.X + cs * sm.x[k] - sn * sm.y[k];
            lo = Math.Min(lo, across); hi = Math.Max(hi, across);
        }
        double mid = alongX ? p.Y : p.X;
        int dir = mid >= band + 0.5 ? 1 : -1;
        double d = (dir > 0 ? band + 1 - lo : hi - band) + DotForm.Dot;   // 띠를 벗어날 만큼
        int cell = CellOf(p);
        double g = cell >= 0 ? Gravity * F.Grav[cell] : 0;
        double mu = T.Materials[Math.Min(p.Mat, T.Materials.Count - 1)].Def.Mu * (cell >= 0 && F.Stored(T.sWater, cell) > WetMol ? WetShare : 1);
        double need = g * mu > 0 ? Math.Sqrt(2 * mu * g * d) : d / dt, now = (alongX ? p.Vy : p.Vx) * dir;
        double j = Math.Min(LeafForce * dt, p.Kg * Math.Max(0, need - now));
        Props[i] = p;
        if (j > 0) Shove(id, alongX ? 0 : j * dir, alongX ? j * dir : 0);
    }

    /// <summary>요청 (끼인 문을 바로잡는 이): 문틀에 걸린 물체를 옆 바닥 칸 가운데로 치운다 (놓임 — 그 문 칸에 든 것은 기압이 낮지 않은 옆 바닥으로 · 걸친 것은 제 칸 가운데로).</summary>
    public void ClearDoorProps(int door, long tick)
    {
        int dc = L.Doors[door].Cell;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(dc, nb);
        int floor = -1;
        foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && L.Kind[j] == CellKind.Floor && F.Pressure(j) >= F.Pressure(dc) - 1) { floor = j; break; }
        if (floor < 0) foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && L.Kind[j] == CellKind.Floor) { floor = j; break; }
        var (alongX, _, _, band) = Leaf(door);
        double mx = L.X(dc) + 0.5, my = L.Y(dc) + 0.5;
        for (int k = 0; k < Props.Count; k++)
        {
            var p = Props[k];
            if (p.State is 5 or 6 || Math.Abs(p.X - mx) > 1.5 || Math.Abs(p.Y - my) > 1.5) continue;
            int pc = CellOf(p);
            bool inDoor = pc == dc, held = p.State == 7;
            if (!inDoor && !held) continue;
            int to = inDoor ? floor : pc;
            if (to < 0) continue;
            p.X = L.X(to) + 0.5; p.Y = L.Y(to) + 0.5; p.Vx = p.Vy = p.W = 0; p.State = 0; p.Still = 0; p.Since = tick;
            Props[k] = p;
        }
    }

    private void SaveProps(StateWriter w)
    {
        w.Tag("물체"); w.List(Props); w.List(Masks); w.List(MaskBits);
        w.Arr(Grit); w.Arr(GritMat); w.Arr(FurnPlaced); w.Arr(FurnAnchor); w.Arr(FurnProp); w.Arr(FurnAt); w.Arr(DoorLeafOut); w.List(PropHits);
        w.F64(PropPxGone); w.F64(PropPyGone); w.F64(PropLGone); w.F64(FixL); w.F64(ToHostPx); w.F64(ToHostPy); w.F64(ToHostL); w.F64(BrokenKg); w.F64(GritKg); w.F64(SweptKg); w.I64(SweptPieces); w.I64(Righted);
        w.I32(NextPropId); w.I64(PropSteps); w.I64(Bounces); w.I64(Shattered); w.I64(Toppled); w.I64(TornLoose); w.I64(Rolled); w.I64(IslandMiss);
        SaveActors(w);
    }

    private void LoadProps(StateReader r)
    {
        r.Tag("물체"); Props.Clear(); Props.AddRange(r.List<Prop>()); _bodyPropsAt = -1; Masks.Clear(); Masks.AddRange(r.List<PropMask>()); MaskBits.Clear(); MaskBits.AddRange(r.List<ulong>());
        r.ArrInto(Grit); r.ArrInto(GritMat); r.ArrInto(FurnPlaced); r.ArrInto(FurnAnchor); r.ArrInto(FurnProp); r.ArrInto(FurnAt); r.ArrInto(DoorLeafOut); PropHits.Clear(); PropHits.AddRange(r.List<PropHit>());
        PropPxGone = r.F64(); PropPyGone = r.F64(); PropLGone = r.F64(); FixL = r.F64(); ToHostPx = r.F64(); ToHostPy = r.F64(); ToHostL = r.F64(); BrokenKg = r.F64(); GritKg = r.F64(); SweptKg = r.F64(); SweptPieces = r.I64(); Righted = r.I64();
        NextPropId = r.I32(); PropSteps = r.I64(); Bounces = r.I64(); Shattered = r.I64(); Toppled = r.I64(); TornLoose = r.I64(); Rolled = r.I64(); IslandMiss = r.I64();
        LoadActors(r);
        _pieceForms.Clear();   // 조각 가면 번호 → 꼴 (다른 세계의 가면일 수 있다)
        _plugStale = true;   // 물체 막음 칸은 장의 PlugProp에서 다시 세운다
        Array.Fill(_mouthTick, long.MinValue);   // 구멍 입 틱 안 캐시도 잊는다 (같은 틱 번호의 다른 세계)
        RestoreFurnMass();   // 뜯긴 가구의 칸 질량 (칸 질량은 찍지 않는다 — 도면 + 제자리 표에서 · PH10)
    }
}
