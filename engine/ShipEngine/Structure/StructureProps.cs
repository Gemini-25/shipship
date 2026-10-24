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
    public int Dropper;            // 놓은 이 꼬리표 (놓은 뒤 1초 동안 그 원판과 닿지 않는다)
    public float Still;            // 고요하게 지난 초
    public long Since;             // 놓인 · 마지막으로 선 틱
    public int Spare0, Spare1;     // 판을 이 단에서 한 번만 올리려고 남긴 칸
}

/// <summary>조각 가면 (꼴 안 상자 · 무게중심 · 비트 시작 — 비트는 <see cref="Structure.MaskBits"/>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PropMask
{
    public ushort Form;
    public byte X0, Y0, W, H;
    public float Cx, Cy;
    public int Bit;
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
    }

    /// <summary>물체의 칸 (자리의 floor — 도면 밖이면 −1).</summary>
    public int CellOf(in Prop p)
    {
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

    /// <summary>요청: 바닥에 물건을 놓는다 (그 칸 가운데 · 남은 양 그대로) — 새 물체 Id.</summary>
    public int PutItem(int item, float charge, int cell, float z = 0)
    {
        if (item < 0 || cell < 0 || cell >= L.N) return -1;
        int id = NextPropId++;
        Props.Add(new Prop { Id = id, Kind = 0, What = (short)item, X = L.X(cell) + 0.5, Y = L.Y(cell) + 0.5, Z = z, Charge = charge });
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

    /// <summary>요청 (⑧ 다리 — 미끄러짐 · 날림 · 문틀 치우기): 물건을 그 칸 가운데로 옮긴다 · 칸 −1이면 구멍 밖 우주로 (목록에서 뺀다).</summary>
    public void MoveItemCell(int id, int cell)
    {
        int i = IndexOf(id);
        if (i < 0) return;
        if (cell < 0) { Props.RemoveAt(i); return; }
        var p = Props[i];
        p.X = L.X(cell) + 0.5; p.Y = L.Y(cell) + 0.5;
        Props[i] = p;
    }

    /// <summary>요청 (⑧ 다리 — 빨려 붙음 · 떨어짐): 구멍을 막았다 (구멍 칸 + 1 · 0이면 떨어졌다).</summary>
    public void SetItemPlug(int id, int plugAt)
    {
        int i = IndexOf(id);
        if (i < 0) return;
        var p = Props[i];
        p.PlugAt = plugAt; p.State = (byte)(plugAt != 0 ? 6 : 0);
        Props[i] = p;
    }

    private void SaveProps(StateWriter w)
    {
        w.Tag("물체"); w.List(Props); w.List(Masks); w.List(MaskBits);
        w.Arr(Grit); w.Arr(GritMat); w.Arr(FurnPlaced); w.Arr(FurnAnchor); w.Arr(FurnProp); w.List(PropHits);
        w.F64(PropPxGone); w.F64(PropPyGone); w.F64(PropLGone); w.F64(FixL); w.F64(ToHostPx); w.F64(ToHostPy); w.F64(ToHostL); w.F64(BrokenKg); w.F64(GritKg);
        w.I32(NextPropId); w.I64(PropSteps); w.I64(Bounces); w.I64(Shattered); w.I64(Toppled); w.I64(TornLoose); w.I64(Rolled); w.I64(IslandMiss);
    }

    private void LoadProps(StateReader r)
    {
        r.Tag("물체"); Props.Clear(); Props.AddRange(r.List<Prop>()); Masks.Clear(); Masks.AddRange(r.List<PropMask>()); MaskBits.Clear(); MaskBits.AddRange(r.List<ulong>());
        r.ArrInto(Grit); r.ArrInto(GritMat); r.ArrInto(FurnPlaced); r.ArrInto(FurnAnchor); r.ArrInto(FurnProp); PropHits.Clear(); PropHits.AddRange(r.List<PropHit>());
        PropPxGone = r.F64(); PropPyGone = r.F64(); PropLGone = r.F64(); FixL = r.F64(); ToHostPx = r.F64(); ToHostPy = r.F64(); ToHostL = r.F64(); BrokenKg = r.F64(); GritKg = r.F64();
        NextPropId = r.I32(); PropSteps = r.I64(); Bounces = r.I64(); Shattered = r.I64(); Toppled = r.I64(); TornLoose = r.I64(); Rolled = r.I64(); IslandMiss = r.I64();
    }
}
