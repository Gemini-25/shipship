using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipView;

/// <summary>
/// 새 화면의 그리기 — 새 엔진의 세계를 옛 게임의 도트 화가로 한 장에 그린다 (Godot 없이 · 칸 하나 = 32도트).
/// 세계를 바꾸지 않고 읽기만 한다. 게임(Godot)은 이 그림을 텍스처로 올리고, 실험실은 PNG로 뽑아 눈으로 본다.
/// 층: 우주 → 바닥 → 벽 · 남쪽 앞면 → 문 → 외판 구멍 → 설비 → 물 · 진공 · 열 · 어둠 → 사람 · 로봇 → 불 · 연기.
/// </summary>
/// <summary>겹쳐 보기 (화면 설정 — 기본은 눈에 보이는 것만): 공기(산소 · 진공 · 일산화탄소) · 열 · 연기 · 사슬 선.</summary>
public enum Overlay : byte { None, Air, Heat, Smoke, Chain, Lines }

// ── 그림 캐시 갈래 표: _cache 열쇠의 맨 윗 바이트 (Key(갈래, …) · 직접 열쇠는 (갈래L << 56) ^ …) ──
//   갈래가 겹치면 먼저 든 남의 그림을 꺼낸다 (예전엔 드론도 9 — 바닥 소화기(물건 0) 아이콘과 멈춘 드론 장 0이 한 열쇠였다). 새 그림은 15부터.
//   갈래 안에서도 같다: 열쇠에서 잘라 낸 값 (& 7 · & 0xFFF)은 그림도 잘라 낸 그 값으로 그린다 (예전 로봇 — 열쇠 Id & 7 · 그림 Id 전체).
//   (아직 남은 것: 1 바닥의 꾸밈 — ArtFloors.Details의 긁힘 · 흙 알갱이 · 이끼는 칸 자리 전체를 보는데 열쇠는 x & 7 · y & 7)
//    1 바닥 (100 + 문턱) · 2 벽 윗면 · 3 벽 앞면 · 4 문짝 (PainterFar DoorTile)
//    5 설비 몸 · 화장실 (9001 중력 발생기 · 9100~ 화장실 몸 · 9200~ 물줄기 · 커튼 · 김) · 6 물 · 7 연기
//    8 로봇 (Id & 0xFFF) · 9 (비었다 — 바닥 물건은 PH4a부터 도트 꼴을 쌓아 그린다: 제 캐시 DotStackCache) · 10 옛 설비 빛 (OldGlow — DotMachines.Life) · 11 외판 바깥 면
//   12 새 가구 몸 (직접 열쇠 — 설비 고리) · 13 새 가구 빛 (직접 열쇠 — PainterFar NewGlow) · 14 드론 (DrawBot)
public sealed partial class Painter
{
    public const int T = DotTiles.T;

    /// <summary>겹쳐 보기 — 세계는 그대로 읽기만 한다.</summary>
    public Overlay Show;
    /// <summary>사슬 선: 원인 사슬의 마디 자리를 차례로 잇는다 (Show == Chain일 때).</summary>
    public IReadOnlyList<int>? ChainCells;

    private readonly Layout L;
    private readonly Finish[] _finish;
    private int[]? _drain;
    /// <summary>새 설비 → 닮은 옛 도트 그림 (제 그림이 생기기 전까지).</summary>
    private static readonly Dictionary<string, string> Look = new()
    {
        ["FoodLocker"] = "SupplyCache", ["GrowRack"] = "PlantRack", ["LargeReactorCore"] = "ReactorCore", ["LargeCoolantPump"] = "CoolantPump",
        ["Shuttle"] = "DockClampPanel", ["Rover"] = "MaintCart", ["CargoCrate"] = "SupplyCache", ["OreBin"] = "Compactor",
    };
    private readonly FurnitureType?[] _fixType;
    private readonly byte[] _washKind;   // 화장실 · 샤워실 설비의 제 그림 (PainterWash.cs)
    private readonly int[] _washFix;     // 화장실 설비 번호들 (넓은 보기의 장마다 덧그림)
    private readonly bool _hasSink;
    private readonly bool[] _newArt;     // 새 가구 그림 (ArtFurniture — 상위 15종)
    private readonly Facing[] _facing;   // 가구가 보는 쪽 (벽에 닿은 쪽이 뒤 — 도면에서 한 번 · Layout.Facing)
    private readonly (int x, int y, int w, int h)[] _fixBox;
    private readonly Dictionary<long, Img> _cache = new();
    private int[]? _fixOrder;
    private readonly Dictionary<int, PersonLook> _looks = new();

    /// <summary>사람 · 로봇을 칸 자리에서 몇 픽셀 옮겨 그린다 (관전 화면이 틱 사이 걸음을 메운다 — 없으면 칸 그대로).</summary>
    public Func<Agent, (int dx, int dy)>? Nudge;

    public Painter(Layout layout)
    {
        L = layout;
        _finish = L.Rooms.Select(r => ArtFloors.FinishFor(r.Type)).ToArray();
        _fixType = new FurnitureType?[L.Furniture.Count];
        _washKind = L.Furniture.Select(f => (byte)WashKind(f.Type)).ToArray();
        _washFix = Enumerable.Range(0, L.Furniture.Count).Where(i => _washKind[i] != 0 && L.Furniture[i].Cells.Length > 0).ToArray();
        _hasSink = _washFix.Any(i => _washKind[i] == 2);
        _newArt = L.Furniture.Select(f => ArtFurniture.Has(f.Type)).ToArray();
        _facing = L.Facing;
        _fixBox = new (int, int, int, int)[L.Furniture.Count];
        for (int i = 0; i < L.Furniture.Count; i++)
        {
            var fu = L.Furniture[i];
            _fixType[i] = Enum.TryParse<FurnitureType>(Look.GetValueOrDefault(fu.Type, fu.Type), out var t) ? t : null;
            if (fu.Cells.Length == 0) continue;
            int x0 = fu.Cells.Min(L.X), y0 = fu.Cells.Min(L.Y), x1 = fu.Cells.Max(L.X), y1 = fu.Cells.Max(L.Y);
            _fixBox[i] = (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }
    }

    // ── 인공 중력 발생기 (제 그림) — 받침 위 고리 · 가운데 축 · 돌아가는 장의 빛 (세기만큼 밝고 · 망가지면 붉은 불꽃) ──
    private const int GravRise = 8;

    private static Img GravBody(int bw, int bh)
    {
        int w = bw * T, h = bh * T + GravRise;
        var g = new Img(w, h);
        g.Fill(1, 3, w - 2, h - 4, Rgba.Hex("#262b33"));            // 받침
        g.Fill(1, 3, w - 2, 2, Rgba.Hex("#4c5562"));                 // 윗면 테
        g.Fill(1, h - 3, w - 2, 2, Rgba.Hex("#16191e"));             // 아랫면 그늘
        for (int x = 4; x < w - 4; x += 6) g.Fill(x, h - 6, 3, 2, Rgba.Hex("#3a414c"));   // 받침 통풍 틈
        double cx = (w - 1) / 2.0, cy = (h - 2) / 2.0 + 1, rx = w * 0.40, ry = (h - 6) * 0.42;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double d = Math.Sqrt(Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2));
                if (d > 0.80 && d <= 1.0) g.Set(x, y, Rgba.Hex(y < cy ? "#7b8794" : "#55606c"));   // 고리 (위쪽이 빛을 받는다)
                else if (d > 0.70 && d <= 0.80) g.Set(x, y, Rgba.Hex("#1a1e24"));                     // 고리 안 홈
                else if (d <= 0.28) g.Set(x, y, Rgba.Hex(d < 0.16 ? "#3c4652" : "#2c333c"));         // 가운데 축
            }
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0) })   // 축을 받치는 살
            for (int k = 0; k < (int)(rx * 0.45); k++) { int x = (int)(cx + dx * (rx * 0.28 + k)); g.Set(x, (int)cy, Rgba.Hex("#46505b")); g.Set(x, (int)cy + 1, Rgba.Hex("#2f363f")); }
        return g;
    }

    private static void GravGlow(Img img, int px, int py, int bw, int bh, float field, FixState state, int frame)
    {
        int w = bw * T, h = bh * T + GravRise;
        double cx = (w - 1) / 2.0, cy = (h - 2) / 2.0 + 1, rx = w * 0.40 * 0.75, ry = (h - 6) * 0.42 * 0.75;
        const int seg = 12;
        for (int k = 0; k < seg; k++)   // 고리 안 홈을 도는 장의 빛 — 세기만큼 많이 · 밝게
        {
            double a = 2 * Math.PI * k / seg;
            int x = px + (int)Math.Round(cx + Math.Cos(a) * rx), y = py + (int)Math.Round(cy + Math.Sin(a) * ry);
            if (state == FixState.Broken)
            {
                if ((frame * 7 + k * 5) % 11 == 0) { img.Fill(x - 1, y - 1, 3, 3, Rgba.Hex("#ff5a2e"), 0.9f); img.Blend(x, y, Rgba.Hex("#ffe08a"), 1f); }   // 튀는 불꽃
                continue;
            }
            int lead = (frame + k) % seg;   // 도는 빛
            float on = state == FixState.On ? field : 0.15f * field;
            float glow = on * (lead < 4 ? 1f - lead * 0.2f : 0.25f);
            if (glow < 0.05f) continue;
            img.Fill(x - 1, y - 1, 3, 2, Rgba.Hex("#3fd9ff"), Math.Min(1f, glow));
            if (lead == 0) img.Blend(x, y - 1, Rgba.Hex("#e6fdff"), Math.Min(1f, on));
        }
        if (state == FixState.On && field > 0.05f) img.Fill(px + (int)cx - 2, py + (int)cy - 1, 4, 3, Rgba.Hex("#7fe9ff"), 0.35f + 0.5f * field);   // 가운데 축의 빛
    }

    /// <summary>방 종류 → 바닥 (옛 게임 Materials.FloorFor와 같은 갈래).</summary>
    public static DotFloor FloorFor(string type) => type switch
    {
        "Engine" or "Reactor" or "Cooling" or "PumpRoom" or "HvacRoom" or "Crusher" or "PropellantTank" or "HeatStorage" or "GasStorage" or "Recycling" => DotFloor.Grate,
        "Power" or "Substation" or "BatteryRoom" or "FuelCell" or "ServerRoom" or "ElectronicsLab" or "Bridge" or "BackupBridge" or "Comms"
            or "Navigation" or "CraneControl" or "Calibration" or "ComputerRoom" => DotFloor.Rubber,
        "Galley" or "Medbay" or "Hydroponics" or "Washroom" or "Laundry" or "WaterPlant" or "Decon" or "Quarantine" or "QuarantineLock" or "Triage" or "Morgue"
            or "Lab" or "AlgaeLab" or "ProteinFarm" or "Freezer" or "Hyperbaric" or "Mess" or "SeedVault" or "MushroomFarm" => DotFloor.Tile,
        "Quarters" or "PrivateCabins" or "QuietQuarters" or "Lounge" or "Chapel" or "Meditation" or "Theater" or "School" or "MeetingRoom"
            or "Archive" or "WaterWallCabin" or "Observatory" => DotFloor.Carpet,
        _ => DotFloor.Metal,
    };

    private Img Cached(long key, Func<Img> make)
    {
        if (!_cache.TryGetValue(key, out var img)) _cache[key] = img = make();
        return img;
    }

    /// <summary>상태를 넘기는 꼴 (static 람다) — 칸마다 부르는 곳에서 갇힌 변수 묶음 · 대리자를 장마다 새로 잡지 않는다.</summary>
    private Img?[] _tileOf = Array.Empty<Img?>();
    private MoldDot[]?[] _moldOf = Array.Empty<MoldDot[]?>();   // 칸마다 바닥 타일과 함께 찍을 곰팡이 점 목록 (5단 A3)

    private Img Cached<TS>(long key, TS st, Func<TS, Img> make)
    {
        if (!_cache.TryGetValue(key, out var img))
        {
            _cache[key] = img = make(st);
            bool opaque = true;
            for (int i = 3; i < img.Px.Length && opaque; i += 4) opaque = img.Px[i] == 255;
            img.Opaque = opaque;   // 바닥 · 벽 위 같은 꽉 찬 타일은 줄째로 찍는다
        }
        return img;
    }

    private static long Key(int kind, int a = 0, int b = 0, int c = 0, int d = 0) =>
        ((long)kind << 56) ^ ((long)(a & 0xFFFF) << 40) ^ ((long)(b & 0xFFFF) << 24) ^ ((long)(c & 0xFFF) << 12) ^ (d & 0xFFF);

    private bool Solid(int x, int y) => L.In(x, y) && L.Kind[L.Index(x, y)] is CellKind.Wall or CellKind.Door && Mine(L.Index(x, y));
    private CellKind KindAt(int x, int y) => L.In(x, y) && Mine(L.Index(x, y)) ? L.Kind[L.Index(x, y)] : CellKind.Outside;

    private DotWall WallOf(int c)
    {
        if (L.Hull[c]) return DotWall.Hull;
        // 두 방 사이 한 겹 벽은 칸막이
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        int rooms = 0, last = -1;
        foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && L.Room[j] != last) { rooms++; last = L.Room[j]; }
        return rooms >= 2 ? DotWall.Partition : DotWall.Panel;
    }

    private int _iExt = -2, _iSeal, _iSuit, _iMask;
    private int[]? _bedOwner; private long _bedOwnerAt = long.MinValue;
    /// <summary>가구 그림에 넘기는 세계 값: 물건 수 (Mind.Stock) · 담요 주인 (그 침대 칸을 제 침대로 둔 사람).</summary>
    private FurnFill FillOf(int fu, Mind? mind)
    {
        var o = new FurnFill { Owner = -1 };
        if (mind == null) return o;
        if (_iExt == -2) { var n = mind.ItemNames; _iExt = Array.IndexOf(n, "소화기"); _iSeal = Array.IndexOf(n, "봉합 꾸러미"); _iSuit = Array.IndexOf(n, "우주복"); _iMask = Array.IndexOf(n, "산소 마스크"); }
        int b = fu * mind.ItemCount;
        byte S(int item, int cap) => item < 0 ? (byte)0 : (byte)Math.Clamp(mind.Stock[b + item], 0, cap);
        o.Ext = S(_iExt, 7); o.Seal = S(_iSeal, 7); o.Suits = S(_iSuit, 7); o.Masks = S(_iMask, 15);
        if (L.Furniture[fu].Type == "Bed")
        {
            if (_bedOwner == null || _bedOwnerAt != _frames)   // 장마다 한 번 (같은 틱에 주인을 바꿔도 따라간다)
            {
                _bedOwner ??= new int[L.Furniture.Count];
                Array.Fill(_bedOwner, -1);
                foreach (var a in mind.Agents) if (a.IsPerson && a.C.Bed >= 0 && a.C.Bed < L.N && L.FurnitureAt[a.C.Bed] >= 0) _bedOwner[L.FurnitureAt[a.C.Bed]] = a.C.Name;
                _bedOwnerAt = _frames;
            }
            o.Owner = _bedOwner[fu];
        }
        return o;
    }

    /// <summary>바깥(우주)인 이웃 여덟 (1 북 · 2 북동 · 4 동 · 8 남동 · 16 남 · 32 남서 · 64 서 · 128 북서 — 도면 밖도 바깥).</summary>
    private int Out8(int x, int y)
    {
        int m = 0, bit = 1;
        foreach (var (dx, dy) in Dirs8)
        {
            if (!L.In(x + dx, y + dy) || L.Kind[L.Index(x + dx, y + dy)] == CellKind.Outside) m |= bit;
            bit <<= 1;
        }
        return m;
    }
    private static readonly (int dx, int dy)[] Dirs8 = { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) };

    /// <summary>방 둘레: 1 북 · 2 동 · 4 남 · 8 서 — 그쪽 이웃이 같은 방 바닥이 아니다.</summary>
    private int EdgeMask(int x, int y, int room)
    {
        int m = 0, bit = 1;
        foreach (var (dx, dy) in Dirs4)
        {
            int nx = x + dx, ny = y + dy;
            if (!L.In(nx, ny) || L.Kind[L.Index(nx, ny)] != CellKind.Floor || L.Room[L.Index(nx, ny)] != room) m |= bit;
            bit <<= 1;
        }
        return m;
    }
    private static readonly (int dx, int dy)[] Dirs4 = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    /// <summary>타일 방의 배수구 자리 (방마다 하나 — 설비가 없는 칸 가운데 해시가 가장 작은 칸 · 고정 꾸밈).</summary>
    private int DrainOf(int room)
    {
        if (_drain == null)
        {
            _drain = Enumerable.Repeat(-1, L.Rooms.Count).ToArray();
            for (int r = 0; r < L.Rooms.Count; r++)
            {
                if (_finish[r] is not (Finish.TileWarm or Finish.TileCool)) continue;
                uint best = uint.MaxValue;
                foreach (int c in L.Rooms[r].Cells)
                {
                    if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0) continue;
                    uint u = DotHash.U(c, r, 59);
                    if (u < best) { best = u; _drain[r] = c; }
                }
            }
        }
        return _drain[room];
    }

    private int[]? _band;
    private int BandOfRoom(int room) => (_band ??= L.Rooms.Select(r => ArtWalls.BandOf(r.Type)).ToArray())[room];

    /// <summary>앞면 꾸밈 (고정 꾸밈 — 칸 자리 해시): 방마다 북쪽 벽 4칸에 띠등 하나 · 기관 · 생명 유지 방엔 관 · 함교 · 서버엔 전선 · 가끔 환기구 (외판 안쪽은 등 없이 늑골).</summary>
    private int FaceDeco(int x, int y, DotWall wk, int room)
    {
        int band = room >= 0 ? BandOfRoom(room) : 0;
        uint u = DotHash.U(x, y, 5);
        if (wk != DotWall.Hull && room >= 0 && (x + room * 3) % 4 == 0) return 1;
        if (band is 5 or 7 && u % 3 == 0) return 2;
        if (band is 4 or 9 && u % 3 == 0) return 3;
        if (u % 7 == 0) return 4;
        return 0;
    }

    private double _skyTh, _skyX, _skyY;
    /// <summary>관전 화면이 틱 사이를 메운 몸마다의 배 틀 자세 (없으면 지금 자세) · 배의 자세 (별 — NaN이면 지금).</summary>
    public (double cs, double sn, double tx, double ty)[]? Poses;
    public double SkyTh = double.NaN;
    /// <summary>관전 화면이 장마다 넣는다: 틱 사이 비율 (0 지난 틱 끝 ~ 1 지금 — 부스러기를 그 때 자리에) · 그 때 배(몸 0) 무게중심의 세상 자리 (NaN이면 지금 — 각은 SkyTh).</summary>
    public double TickFrac = 1, ShipX = double.NaN, ShipY = double.NaN;
    /// <summary>관전 화면이 장마다 넣는다: 그릴 운석 · 구름 맞음과 잔상 세기 (1 → 0 — 화면 시간 0.25초). null이면 지난 틱의 맞음 가운데 틱 사이 비율이 지난 것.</summary>
    public List<(Struck hit, float glow)>? Strikes;

    /// <summary>화면 한 장: 칸 (x0, y0)부터 w × h 칸. frame은 불꽃 · 걷기 같은 움직임의 장 번호.
    /// into: 다시 쓸 그림 (w × h 칸이 들어가면 — 관전 화면이 매 장 5MB를 새로 만들지 않게. 처음에 통째로 칠한다 · 남는 곳은 우주색).</summary>
    private long _frames;
    public Img Frame(Sim s, int x0, int y0, int w, int h, int frame, Img? into = null)
    {
        _tick = s.Tick; _frames++;
        var st = s.Find<Structure>();
        // 배가 돌면 별도 돈다 (화면은 배를 따라 돈다 — 별은 무한히 멀어 배의 자세만큼만 거꾸로 돈다 · 배의 무게중심을 축으로)
        (_skyTh, _skyX, _skyY) = st != null && st.Bodies.Count > 0 ? (double.IsNaN(SkyTh) ? st.Bodies[0].Th : SkyTh, st.Bodies[0].Lx, st.Bodies[0].Ly) : (0, 0, 0);
        if (st == null || st.Bodies.Count < 2) { _only = -1; _stc = null; var one = Draw(s, x0, y0, w, h, frame, into, false); DrawStrikes(one, s, st, x0, y0); return one; }
        // ④ 칸 강체: 배를 그리고 · 떨어져 나간 조각마다 제 칸만 따로 그려 자세대로 돌려 붙인다 (가까운 도트 — 도트 그림이 뭉개지지 않게)
        _stc = st; _only = 0;
        var img = Draw(s, x0, y0, w, h, frame, into, false);
        for (int b = 1; b < st.Bodies.Count; b++)
        {
            var o = st.Bodies[b];
            if (o.Mass <= 0) continue;
            int bx0 = o.MinX - 1, by0 = o.MinY - 2, bw = o.MaxX - o.MinX + 3, bh = o.MaxY - o.MinY + 4;
            var (cs, sn, tx, ty) = Poses != null && b < Poses.Length ? Poses[b] : st.ToShipFrame(b);
            double qx0 = double.MaxValue, qy0 = double.MaxValue, qx1 = double.MinValue, qy1 = double.MinValue;
            foreach (var (px, py) in new[] { (bx0, by0), (bx0 + bw, by0), (bx0, by0 + bh), (bx0 + bw, by0 + bh) })
            {
                double qx = cs * px - sn * py + tx, qy = sn * px + cs * py + ty;
                qx0 = Math.Min(qx0, qx); qy0 = Math.Min(qy0, qy); qx1 = Math.Max(qx1, qx); qy1 = Math.Max(qy1, qy);
            }
            if (qx1 < x0 || qy1 < y0 || qx0 > x0 + w || qy0 > y0 + h) continue;   // 화면 밖
            _only = b;
            var part = Draw(s, bx0, by0, bw, bh, frame, null, true);
            Place(img, part, bx0, by0, x0, y0, cs, sn, tx, ty, qx0, qy0, qx1, qy1);
        }
        _only = -1; _stc = null;
        DrawStrikes(img, s, st, x0, y0);
        return img;
    }

    /// <summary>
    /// 운석 · 구름 맞음 (월드 보기 기록 Outside.Struck): 들어온 쪽 화면 끝 (구름 파편은 1.5m)에서 맞은 칸까지 1~2도트 줄 — 맞은 곳 흰빛에서 들어온 쪽으로 주황 · 옅게.
    /// 맨 위에 빛 없이 (스스로 빛난다). Strikes가 없으면 (시험 · 사진) 지난 틱의 맞음 가운데 틱 사이 비율이 그 때를 지난 것.
    /// </summary>
    private void DrawStrikes(Img img, Sim s, Structure? st, int x0, int y0, int dots = T)
    {
        var list = Strikes;
        if (list == null)
        {
            var o = s.Find<Outside>();
            if (o == null || o.Struck.Count == 0 || o.StruckTick != _tick - 1) return;
            _strikesNow.Clear();
            foreach (var h in o.Struck) if (h.Sub / 255.0 <= TickFrac + 1e-9) _strikesNow.Add((h, 1f));
            list = _strikesNow;
        }
        if (list.Count == 0) return;
        var hot = Rgba.Hex("#fff6e0"); var tail = Rgba.Hex("#ff9a3c");
        foreach (var (h, glow) in list)
        {
            if (glow <= 0 || h.Cell < 0 || h.Cell >= L.N) continue;
            var (hx, hy) = st != null ? st.ShipFrameOf(h.Cell) : (L.X(h.Cell) + 0.5, L.Y(h.Cell) + 0.5);
            double px = (hx - x0) * dots, py = (hy - y0) * dots, ux = h.Ux, uy = h.Uy;
            double len = h.Cloud ? 1.5 * dots : ToEdge(px, py, -ux, -uy, img.W, img.H);   // 도트
            bool wide = h.Kg >= 0.1, across = Math.Abs(uy) > Math.Abs(ux);
            int n = (int)Math.Ceiling(len);
            for (int k = 0; k <= n; k++)
            {
                double t = k / Math.Max(1.0, len);   // 0 맞은 곳 ~ 1 끝
                var c = Rgba.Lerp(hot, tail, (float)Math.Min(1, t * 3));
                float a = glow * (float)(1 - 0.7 * t);
                int X = (int)Math.Floor(px - ux * k), Y = (int)Math.Floor(py - uy * k);
                img.Blend(X, Y, c, a);
                if (wide) img.Blend(X + (across ? 1 : 0), Y + (across ? 0 : 1), c, a);
            }
        }
    }
    private readonly List<(Struck hit, float glow)> _strikesNow = new();

    /// <summary>(px, py)에서 (dx, dy) 쪽으로 그림 끝까지 도트 (그림 밖이면 0).</summary>
    private static double ToEdge(double px, double py, double dx, double dy, int w, int h)
    {
        double t = 4000;
        if (dx > 1e-9) t = Math.Min(t, (w - px) / dx); else if (dx < -1e-9) t = Math.Min(t, -px / dx);
        if (dy > 1e-9) t = Math.Min(t, (h - py) / dy); else if (dy < -1e-9) t = Math.Min(t, -py / dy);
        return Math.Max(0, t);
    }

    /// <summary>
    /// 부스러기 알갱이 (배 틀 자리 · 무게만큼 2~6도트 판 · 제 돎만큼 돌려 · 날아가는 쪽 반대로 짧은 꼬리) — 빛을 받기 전에 그린다 (불 끈 방에서는 어둡다).
    /// 자리는 틱 사이 비율 TickFrac의 때: 지금 자리에서 (1 − f) · 2.4초만큼 뒤로 (진공을 곧게 난다) · 배 틀은 그 때의 배 자세 (ShipX · ShipY · SkyTh).
    /// 이번 틱에 났고 뒤로 늘인 자리가 난 몸의 바닥 · 설비 칸이면 그 장에는 그리지 않는다 — 충돌 잔걸음에 난 것은 아직 나지 않았다 (틱 처음에 난 구멍 · 운석 · 갈라짐의 것은 외판 · 끊긴 칸에서 시작한다).
    /// </summary>
    private void Shards(Img img, Sim s, Structure st, int x0, int y0, int dots = T)
    {
        if (st.Shards.Count == 0) return;
        // 지난 틱에 바깥에서 맞은 것 (운석 · 구름 — 바깥이 먼저 돌아 그 부스러기는 틱 처음 맞은 칸에서 한 틱을 다 난다): 맞은 때 (Sub) 전에는 그리지 않고 ·
        // 뒤에는 남은 틱 사이에 맞은 칸 곁 → 틱 끝 자리를 곧게 (틱 사이 비율을 (f − Sub) / (1 − Sub)로 — 다음 틱과 이어진다)
        var outs = s.Find<Outside>();
        var struck = outs != null && outs.Struck.Count > 0 && outs.StruckTick == _tick - 1 ? outs.Struck : null;
        var body = Rgba.Hex("#9aa2b0"); var dark = Rgba.Hex("#3a404c");
        var b0 = st.Bodies.Count > 0 ? st.Bodies[0] : default;
        double f = Math.Clamp(TickFrac, 0, 1), back = (1 - f) * Time.SecondsPerTick;
        double th = double.IsNaN(SkyTh) ? b0.Th : SkyTh, ox = double.IsNaN(ShipX) ? b0.X : ShipX, oy = double.IsNaN(ShipY) ? b0.Y : ShipY;
        double cs = Math.Cos(-th), sn = Math.Sin(-th);
        foreach (var sh in st.Shards)
        {
            double sback = back;
            bool fresh = back > 0 && sh.Born == _tick - 1, hit = false;
            if (fresh && struck != null)
            {
                // 틱 처음 자리 (배 틀 — 배에 대해 한 틱 난 만큼 뒤로)가 맞은 칸 가운데에서 2m 안이면 그 맞음의 부스러기
                var (ex, ey) = st.LocalOf(0, sh.X, sh.Y);
                double rvx = sh.Vx - b0.Vx, rvy = sh.Vy - b0.Vy, c0 = Math.Cos(-b0.Th), s0 = Math.Sin(-b0.Th);
                ex -= (c0 * rvx - s0 * rvy) * Time.SecondsPerTick; ey -= (s0 * rvx + c0 * rvy) * Time.SecondsPerTick;
                double best = 4, sub = 0;   // 거리²
                foreach (var k in struck)
                {
                    var (kx, ky) = st.ShipFrameOf(k.Cell);
                    double d2 = (kx - ex) * (kx - ex) + (ky - ey) * (ky - ey);
                    if (d2 < best) { best = d2; sub = k.Sub / 255.0; hit = true; }
                }
                if (hit && f < sub) continue;
                if (hit) sback = sub >= 0.999 ? 0 : (1 - (f - sub) / (1 - sub)) * Time.SecondsPerTick;
            }
            double wx = sh.X - sh.Vx * sback - ox, wy = sh.Y - sh.Vy * sback - oy;
            double sx = b0.Lx + cs * wx - sn * wy, sy = b0.Ly + sn * wx + cs * wy;
            int px = (int)Math.Round((sx - x0) * dots), py = (int)Math.Round((sy - y0) * dots);
            if (px < -8 || py < -8 || px > img.W + 8 || py > img.H + 8) continue;
            if (fresh && !hit && NotYet(st, sh, sx, sy)) continue;
            int size = Math.Max(1, Math.Clamp((int)Math.Round(Math.Sqrt(sh.Kg) / 3), 2, 6) * dots / T);
            // 배에서 본 빠르기 쪽 반대로 꼬리
            double vx = sh.Vx - b0.Vx, vy = sh.Vy - b0.Vy, lvx = cs * vx - sn * vy, lvy = sn * vx + cs * vy, vl = Math.Sqrt(lvx * lvx + lvy * lvy);
            double step = 2.0 * dots / T;
            if (vl > 0.5) for (int k = 1; k <= 4; k++) img.Blend(px - (int)(lvx / vl * k * step), py - (int)(lvy / vl * k * step), body, 0.5f - k * 0.1f);
            // 판의 각 (배 틀): 알갱이마다 해시 각 (빠르기 · 난 때 — 날며 바뀌지 않는다) + 제 돎 ω · 나이 (ω = Spin / (Kg · 0.25²/6) — 25cm 판의 보기 어림) − 배 각
            long hv = BitConverter.DoubleToInt64Bits(sh.Vx) ^ BitConverter.DoubleToInt64Bits(sh.Vy) * 31;
            double age = (_tick - 1 - sh.Born + f) * Time.SecondsPerTick - (back - sback), spin = sh.Kg > 0 ? sh.Spin / (sh.Kg * 0.25 * 0.25 / 6) : 0;
            double ang = DotHash.F((int)hv, (int)(hv >> 32), (int)sh.Born) * 2 * Math.PI + spin * age - th;
            var plate = PlateOf(size, ang);
            foreach (var (dx, dy) in plate) img.Blend(px + dx + 1, py + dy + 1, dark, 0.6f);
            foreach (var (dx, dy) in plate) img.Blend(px + dx, py + dy, body);
        }
    }

    // 돌린 판 꼴 (한 변 1~6도트 · 각 단 16 — 네모는 90°마다 같다 · 단 0 = 돌지 않은 네모): 그리는 자리 (px, py)에서의 도트 자리 — 처음 쓸 때 한 번 짓는다
    private static readonly (int dx, int dy)[]?[] _plates = new (int, int)[]?[7 * 16];
    private static (int dx, int dy)[] PlateOf(int size, double ang)
    {
        int step = (int)(((long)Math.Floor(ang / (Math.PI / 2) * 16) % 16 + 16) % 16), k = size * 16 + step;
        if (_plates[k] is { } got) return got;
        // 가운데: 짝수 변은 (px, py) 도트 모서리 · 홀수 변은 그 도트 가운데 (예전 네모와 같다) — 도트 가운데가 판 안이면 칠한다
        double half = size / 2.0 - 1e-6, a = step * (Math.PI / 2) / 16, ca = Math.Cos(a), sa = Math.Sin(a), hc = (size & 1) * 0.5;
        int r = (int)Math.Ceiling(half * 1.4143) + 1;
        var list = new List<(int, int)>();
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                double x = dx + 0.5 - hc, y = dy + 0.5 - hc, u = ca * x + sa * y, v = -sa * x + ca * y;
                if (Math.Abs(u) < half && Math.Abs(v) < half) list.Add((dx, dy));
            }
        return _plates[k] = list.ToArray();
    }

    /// <summary>뒤로 늘인 배 틀 자리 (sx, sy)가 알갱이가 난 몸의 바닥 · 설비 칸인가 (그 몸의 그리는 자세로 — 몸 0은 배 틀 그대로).</summary>
    private bool NotYet(Structure st, in Shard sh, double sx, double sy)
    {
        int b = sh.From > 0 && sh.From < st.Bodies.Count ? sh.From : 0;
        double qx = sx, qy = sy;
        if (b > 0)
        {
            var (cs, sn, tx, ty) = Poses != null && b < Poses.Length ? Poses[b] : st.ToShipFrame(b);
            qx = cs * (sx - tx) + sn * (sy - ty); qy = -sn * (sx - tx) + cs * (sy - ty);
        }
        int x = (int)Math.Floor(qx), y = (int)Math.Floor(qy);
        if (!L.In(x, y)) return false;
        int c = L.Index(x, y);
        return st.BodyOf(c) == b && (L.Kind[c] == CellKind.Floor || L.FurnitureAt[c] >= 0);
    }

    /// <summary>조각 그림을 배 틀에 붙인다: 화면 도트마다 거꾸로 돌려 조각 그림의 도트를 찾는다 (배 틀 q = R·p + t).</summary>
    private static void Place(Img img, Img part, int bx0, int by0, int x0, int y0, double cs, double sn, double tx, double ty, double qx0, double qy0, double qx1, double qy1, int dots = T)
    {
        // 그림판 도트 (dots — 넓은 보기는 16 · 8)마다 거꾸로 돌려 32도트 조각 그림의 도트를 찾는다
        int X0 = Math.Max(0, (int)Math.Floor((qx0 - x0) * dots)), Y0 = Math.Max(0, (int)Math.Floor((qy0 - y0) * dots));
        int X1 = Math.Min(img.W, (int)Math.Ceiling((qx1 - x0) * dots)), Y1 = Math.Min(img.H, (int)Math.Ceiling((qy1 - y0) * dots));
        for (int Y = Y0; Y < Y1; Y++)
            for (int X = X0; X < X1; X++)
            {
                double qx = x0 + (X + 0.5) / dots - tx, qy = y0 + (Y + 0.5) / dots - ty;
                double px = cs * qx + sn * qy, py = -sn * qx + cs * qy;
                int u = (int)Math.Floor((px - bx0) * T), v = (int)Math.Floor((py - by0) * T);
                if ((uint)u >= (uint)part.W || (uint)v >= (uint)part.H) continue;
                var c = part.Get(u, v);
                if (c.A != 0) img.Blend(X, Y, c);
            }
    }

    // 지금 그리는 몸 (-1: 몸이 없다 — 다 그린다 · 0: 배 · 1~: 그 조각만)
    private int _only = -1;
    private Structure? _stc;
    private bool Mine(int c) => _only < 0 || _stc!.BodyOf(c) == _only;

    /// <summary>단계별 그리기 시간 (ms 합 — 실험실에서만 켠다 · null이면 재지 않는다).</summary>
    public static Dictionary<string, double>? Prof;
    /// <summary>단계별 새로 잡은 바이트 (Prof와 함께 켠다).</summary>
    public static Dictionary<string, long>? ProfAlloc;
    [ThreadStatic] private static long _lapAlloc;
    private static long Lap(string name, long t0)
    {
        if (Prof == null) return 0;
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        Prof[name] = Prof.GetValueOrDefault(name) + (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (ProfAlloc != null) { long a = GC.GetAllocatedBytesForCurrentThread(); if (_lapAlloc != 0) ProfAlloc[name] = ProfAlloc.GetValueOrDefault(name) + a - _lapAlloc; _lapAlloc = GC.GetAllocatedBytesForCurrentThread(); }
        return System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private Img Draw(Sim s, int x0, int y0, int w, int h, int frame, Img? into, bool part)
    {
        var img = into != null && into.W >= w * T && into.H >= h * T ? into : new Img(w * T, h * T);   // 넉넉한 그림판이면 왼쪽 위에 그린다 (관전 화면 — 다시 쓰는 그림판)
        var f = s.Get<Field>();
        var net = s.Find<Network>();
        var st = s.Find<Structure>();
        var chem = s.Find<Chemistry>();
        var light = s.Find<Light>();
        var mind = s.Find<Mind>();
        var life = s.Find<Life>();
        int Px(int x) => (x - x0) * T;
        int Py(int y) => (y - y0) * T;
        _emissive.Clear(); _front.Clear();
        GatherFacts(s);
        long lt = Prof != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (ProfAlloc != null) _lapAlloc = GC.GetAllocatedBytesForCurrentThread();
        lt = Lap("준비", lt);

        // ── 우주 ──
        var space = Rgba.Hex("#05070c");
        if (!part) Parallel.For(0, h, r => FillOpaque(img, 0, r * T, w * T, T, space));   // 그릴 칸만 (넉넉히 잡은 그림판의 나머지는 화면에 나가지 않는다) · 칸 줄마다 여러 코어에서
        if (!part && !_bake && _skyTh == 0)   // 굽는 덩어리엔 별이 없다 (넓은 보기가 장마다 — 배가 돌면 별도 돈다)
            for (int y = y0 - 1; y <= y0 + h; y++)
                for (int x = x0 - 1; x <= x0 + w; x++)
                {
                    uint u = DotHash.U(x, y, 7);
                    if (u % 9 != 0) continue;
                    int sx = Px(x) + (int)(u >> 8) % T, sy = Py(y) + (int)(u >> 16) % T;
                    byte v = (byte)(90 + (u >> 24) % 140);
                    img.Set(sx, sy, new Rgba(v, v, (byte)Math.Min(255, v + 20)));
                }
        else if (!part && !_bake)
        {
            // 하늘 틀 = 배 틀을 배의 자세만큼 돌린 것 — 하늘 칸마다 같은 해시의 별을 화면으로 되돌려 찍는다 (자세가 0이면 위와 같은 그림)
            double cs = Math.Cos(_skyTh), sn = Math.Sin(_skyTh);
            double qx0 = double.MaxValue, qy0 = double.MaxValue, qx1 = double.MinValue, qy1 = double.MinValue;
            foreach (var (px, py) in new[] { (x0 - 1.0, y0 - 1.0), (x0 + w + 1.0, y0 - 1.0), (x0 - 1.0, y0 + h + 1.0), (x0 + w + 1.0, y0 + h + 1.0) })
            {
                double qx = cs * (px - _skyX) - sn * (py - _skyY) + _skyX, qy = sn * (px - _skyX) + cs * (py - _skyY) + _skyY;
                qx0 = Math.Min(qx0, qx); qy0 = Math.Min(qy0, qy); qx1 = Math.Max(qx1, qx); qy1 = Math.Max(qy1, qy);
            }
            for (int y = (int)Math.Floor(qy0); y <= (int)Math.Ceiling(qy1); y++)
                for (int x = (int)Math.Floor(qx0); x <= (int)Math.Ceiling(qx1); x++)
                {
                    uint u = DotHash.U(x, y, 7);
                    if (u % 9 != 0) continue;
                    double kx = x + (double)((u >> 8) % T) / T - _skyX, ky = y + (double)((u >> 16) % T) / T - _skyY;
                    double bx = cs * kx + sn * ky + _skyX, by = -sn * kx + cs * ky + _skyY;
                    int sx = (int)Math.Floor((bx - x0) * T), sy = (int)Math.Floor((by - y0) * T);
                    if (sx < 0 || sy < 0 || sx >= img.W || sy >= img.H) continue;
                    byte v = (byte)(90 + (u >> 24) % 140);
                    img.Set(sx, sy, new Rgba(v, v, (byte)Math.Min(255, v + 20)));
                }
        }

        lt = Lap("우주", lt);
        // ── 바닥 · 벽 · 문 ── 칸마다 타일을 차례로 고르고 (타일 캐시) · 찍기는 칸 줄마다 여러 코어에서 (타일은 꼭 한 칸 — 서로 겹쳐 쓰지 않는다)
        // 바닥에 핀 곰팡이 (5단 A3 — 단마다 구운 점 목록 · 칸 안에만)도 바닥 타일과 함께 찍는다: 벽 앞면 · 문짝 · 설비 몸 (제 칸 · 아래 줄에서 솟은 것)이 그 앞에 선다 (A3 검토)
        if (_tileOf.Length < w * h) { _tileOf = new Img?[w * h]; _moldOf = new MoldDot[]?[w * h]; }
        var tileOf = _tileOf; var moldOf = _moldOf;
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                ref var tile = ref tileOf[(y - y0) * w + x - x0];
                tile = null; moldOf[(y - y0) * w + x - x0] = null;
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (!Mine(c)) continue;
                switch (L.Kind[c])
                {
                    case CellKind.Floor:
                    {
                        int room = L.Room[c];
                        var fin = room >= 0 ? _finish[room] : Finish.Metal;
                        int edge = EdgeMask(x, y, room);
                        bool drain = room >= 0 && DrainOf(room) == c;
                        tile = Cached(Key(1, (int)fin, (x & 7) | (y & 7) << 3 | edge << 6 | (drain ? 1 : 0) << 10), (fin, x, y, edge, drain), static q => ArtFloors.Floor(q.fin, q.x, q.y, q.edge, q.drain));
                        moldOf[(y - y0) * w + x - x0] = MoldMarksAt(life, c);
                        break;
                    }
                    case CellKind.Door:
                    {
                        int did = L.DoorAt[c];
                        bool across = did >= 0 && L.Doors[did].Vertical;
                        int dk = did < 0 ? 0 : L.Doors[did].External ? 1 : L.Doors[did].Kind == "normal" ? 0 : 2;
                        tile = Cached(Key(1, 100 + dk, across ? 1 : 0), (across, dk), static q => ArtFloors.Threshold(q.across, q.dk));
                        break;
                    }
                    case CellKind.Wall:
                    {
                        if (F(f, c).hole >= 2f && (st == null || st.Sub[c] == 0)) break; // 통째로 뚫린 벽 — 우주가 보인다 (남은 테두리 잔칸은 아래에서 가린다)
                        var wk = WallOf(c);
                        int mask = (Solid(x, y - 1) ? 1 : 0) | (Solid(x + 1, y) ? 2 : 0) | (Solid(x, y + 1) ? 4 : 0) | (Solid(x - 1, y) ? 8 : 0);
                        bool faceBelow = KindAt(x, y + 1) == CellKind.Floor;
                        int out8 = wk == DotWall.Hull ? Out8(x, y) : 0;
                        int wv = (int)(DotHash.U(x, y, 43) & 7);
                        tile = Cached(Key(2, (int)wk, mask | (faceBelow ? 16 : 0) | wv << 5, out8), (wk, mask, faceBelow, out8, wv), static q => ArtWalls.Cap(q.wk, q.mask, q.faceBelow, q.out8, q.wv));
                        break;
                    }
                }
            }
        Parallel.For(0, h, r =>
        {
            for (int k = 0; k < w; k++)
            {
                if (tileOf[r * w + k] is { } tile) Put(img, tile, k * T, r * T);
                if (moldOf[r * w + k] is { } mold) MoldPut(img, mold, k * T, r * T);
            }
        });
        lt = Lap("바닥 · 벽 · 문", lt);
        // ── 잔칸 (④ 칸 강체 (라)): 판 구멍은 우주 (덧댄 것은 덧판) · 찢긴 가장자리는 밝은 금속 테 · 우주가 된 사이 칸에 남은 잔칸은 붙은 몸과 함께 ──
        if (st != null && st.Sub.Length == L.N)
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    if (!L.In(x, y)) continue;
                    int c = L.Index(x, y);
                    ushort m = st.Sub[c];
                    if (m == Structure.SubFull || L.Kind[c] is not (CellKind.Wall or CellKind.Floor or CellKind.Door)) continue;
                    if (f.Severed[c] != 0)
                    {
                        if (m != 0) TornEdge(img, st, c, Px(x), Py(y));
                        continue;
                    }
                    if (!Mine(c)) continue;
                    SubMask(img, m, Px(x), Py(y), f.Hole[c] < Structure.SubMinArea, part, space);
                }
        lt = Lap("잔칸", lt);

        // 벽 남쪽 앞면 (24도트 — 벽 칸 아래 14 + 바닥 칸 위 10): 아래 칸이 바닥이면
        for (int y = y0 - 1; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (KindAt(x, y) != CellKind.Wall || KindAt(x, y + 1) != CellKind.Floor) continue;
                int c = L.Index(x, y);
                if (F(f, c).hole >= 2f || !Mine(c)) continue;
                var wk = WallOf(c);
                bool openW = !Solid(x - 1, y) || KindAt(x - 1, y + 1) != CellKind.Floor;
                bool openE = !Solid(x + 1, y) || KindAt(x + 1, y + 1) != CellKind.Floor;
                int below = L.Index(x, y + 1), room = L.Room[below];
                int deco = FaceDeco(x, y, wk, room), band = room >= 0 ? BandOfRoom(room) : 0;
                int seam = (x % 3 + 3) % 3 switch { 0 => 0, 1 => 16, _ => -1 };   // 48도트 판 — 칸 자리로 이어진다
                int fv = (int)(DotHash.U(x, y, 47) & 3);
                Put(img, Cached(Key(3, (int)wk, (openW ? 1 : 0) | (openE ? 2 : 0) | deco << 2 | (seam + 1) << 6, band, fv), (wk, openW, openE, deco, band, seam, fv), static q => ArtWalls.Face(q.wk, q.openW, q.openE, q.deco, q.band, q.seam, q.fv)), Px(x), Py(y) + ArtWalls.CapH);
                if (deco == 1 && Lighting) FaceLamp(Px(x), Py(y) + ArtWalls.CapH, room, net, light);
            }
        // 선체 바깥 면: 아래가 우주인 외판 칸 아래에 (볼록 모서리 쪽은 깎인 만큼 짧게)
        if (!part)
            for (int y = y0 - 1; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    if (KindAt(x, y) != CellKind.Wall || !L.In(x, y + 1) || L.Kind[L.Index(x, y + 1)] != CellKind.Outside) continue;
                    int c = L.Index(x, y);
                    if (!L.Hull[c] || F(f, c).hole >= 2f || !Mine(c)) continue;
                    int o8 = Out8(x, y);
                    bool cutW = (o8 & 64) != 0, cutE = (o8 & 4) != 0;
                    Put(img, Cached(Key(11, cutW ? 1 : 0, cutE ? 1 : 0), (cutW, cutE), static q => ArtWalls.HullSkin(q.cutW, q.cutE)), Px(x), Py(y + 1));
                }
        lt = Lap("벽 앞면", lt);
        // 벽에 묻은 그을음 (② — 불 곁의 벽이 검게): 윗면 · 남쪽 앞면
        for (int y = y0 - 1; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (KindAt(x, y) != CellKind.Wall) continue;
                int c = L.Index(x, y);
                double sg = f.SootGPerM2(c);
                if (sg <= 0.05 || F(f, c).hole >= 2f || !Mine(c)) continue;
                if (_bake) sg = SootStep(sg);   // 굽는 덩어리는 그을음 단으로 (불 동안 다시 굽기가 몰리지 않게)
                bool face = KindAt(x, y + 1) == CellKind.Floor;
                if (y >= y0) SootStain(img, Px(x), Py(y), T, face ? ArtWalls.CapH : T, sg, c);
                if (face) SootStain(img, Px(x), Py(y) + ArtWalls.CapH, T, ArtWalls.FaceH, sg, c + 7);
            }
        lt = Lap("벽 그을음", lt);
        if (st != null) WallTraces(img, st, x0, y0, w, h);   // 녹 · 실금 (④ 부식 · 피로)
        lt = Lap("녹 · 실금", lt);
        foreach (var d in L.Doors)
        {
            if (_bake) break;   // 문짝은 넓은 보기가 장마다 (굽는 덩어리엔 문턱만)
            int x = L.X(d.Cell), y = L.Y(d.Cell);
            if (x < x0 || y < y0 || x >= x0 + w || y >= y0 + h || !Mine(d.Cell)) continue;
            Put(img, DoorTile(d, f, st), Px(x), Py(y));
            if (f.DoorBleed[d.Id] != 0) Hiss(img, Px(x) + T / 2, Py(y) + T / 2, frame); // 압력 맞춤 밸브
        }

        lt = Lap("문", lt);
        // ── 외판 구멍 · 금 ── (굽는 덩어리는 둘레 한 칸까지 — 이웃 칸의 금이 덩어리 경계를 넘어온다)
        int hb = _bake ? 1 : 0;
        for (int y = y0 - hb; y < y0 + h + hb; y++)
            for (int x = x0 - hb; x < x0 + w + hb; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] != CellKind.Wall || f.Hole[c] <= 0 || !Mine(c)) continue;
                float a = f.Hole[c];
                if (a >= 2f) continue;
                float r = Math.Clamp(MathF.Sqrt(a / 0.01f) * 8f, 2f, 14f);
                float cx = Px(x) + T / 2 + (DotHash.F(c, 1) - 0.5f) * 8, cy = Py(y) + T / 2 + (DotHash.F(c, 2) - 0.5f) * 8;
                if (st == null || st.Sub[c] == Structure.SubFull)   // 잔칸이 파였으면 그 모양이 구멍이다
                {
                    img.Oval(cx, cy, r + 2, r + 1.5f, Rgba.Hex("#5a6576"), 0.9f);
                    img.Oval(cx, cy, r, r * 0.8f, space);
                    for (int k = 0; k < 4; k++) // 금
                    {
                        float ang = DotHash.F(c, 10 + k) * 6.283f, len = r + 3 + DotHash.F(c, 20 + k) * 6;
                        for (float t = r; t < len; t += 0.7f) img.Set((int)(cx + MathF.Cos(ang) * t), (int)(cy + MathF.Sin(ang) * t), Rgba.Hex("#0c0f14"));
                    }
                }
                // 아직 공기가 새면: 바깥쪽으로 하얀 줄기 (안쪽 기압이 높을수록 길게)
                double inside = 0; int ox = 0, oy = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (!L.In(nx, ny)) { ox = nx - x; oy = ny - y; continue; }
                    int j = L.Index(nx, ny);
                    if (L.Kind[j] == CellKind.Outside) { ox = nx - x; oy = ny - y; }
                    else if (L.Kind[j] == CellKind.Floor) inside = Math.Max(inside, f.Pressure(j));
                }
                if (inside > 5 && (ox != 0 || oy != 0) && !_bake)
                {
                    float reach = (float)Math.Min(30, 6 + inside / 4);
                    for (int k = 0; k < 14; k++)
                    {
                        float tt = DotHash.F(c, frame * 16 + k) * reach, side = (DotHash.F(c, frame * 16 + k + 7) - 0.5f) * (2 + tt * 0.5f);
                        int px2 = (int)(cx + ox * (r + tt) + oy * side), py2 = (int)(cy + oy * (r + tt) + ox * side);
                        img.Blend(px2, py2, Rgba.Hex("#e8f4ff"), 0.85f - tt / reach * 0.7f);
                    }
                }
            }

        lt = Lap("외판 구멍 · 금", lt);
        // ── 설비 (아래쪽 것이 앞) ──
        _fixOrder ??= Enumerable.Range(0, L.Furniture.Count).Where(i => L.Furniture[i].Cells.Length > 0)
            .OrderBy(i => _fixBox[i].y + _fixBox[i].h).ThenBy(i => i).ToArray();   // 설비는 움직이지 않는다 — 한 번만 줄 세운다
        if (!_bake) SinkUsers(mind);   // 세면대를 쓰는 이 (장마다 한 번)
        foreach (int i in _fixOrder)
        {
            var (bx, by, bw, bh) = _fixBox[i];
            if (bx + bw < x0 || by + bh < y0 || bx > x0 + w || by > y0 + h + 1 || !Mine(L.Furniture[i].Cells[0])) continue;
            if (L.Furniture[i].Type == "GravityGenerator")   // 제 그림 (옛 도트에 없는 설비)
            {
                int gdev = DeviceOf(net, i);
                var gs = gdev < 0 ? FixState.On : net!.IsBroken(gdev, Part.Electric) || net.IsBroken(gdev, Part.Mechanical) ? FixState.Broken : net.Working(gdev) ? FixState.On : FixState.Off;
                float field = gdev >= 0 && net!.GravField.Length > gdev ? net.GravField[gdev] : 1f;
                int gx = Px(bx), gy = Py(by) - GravRise;
                Put(img, Cached(Key(5, 9001, bw, bh, 0), (bw, bh), static q => Shadowed(GravBody(q.bw, q.bh), GravRise)), gx, gy);
                int gbw = bw, gbh = bh;   // 이 칸 안에서만 붙잡는다 (설비마다 갇힌 변수 묶음을 잡지 않게)
                if (Lighting) _emissive.Add(im => GravGlow(im, gx, gy, gbw, gbh, field, gs, frame));
                else GravGlow(img, gx, gy, gbw, gbh, field, gs, frame);
                continue;
            }
            if (_newArt[i])   // 새 가구 그림 (T6 — 상위 15종 · 세계 값: 선반 · 로커 · 함의 물건 수 · 담요 주인)
            {
                string type = L.Furniture[i].Type;
                var face = _facing[i];
                var fill = FillOf(i, mind);
                int nrise = ArtFurniture.Rise(type, bw, bh, face);
                long key = (12L << 56) ^ ((long)i << 36) ^ (fill.Key << 8) ^ ((long)face << 4);
                var nbody = Cached(key, (type, bw, bh, face, fill, i), static q => Shadowed(ArtFurniture.Body(q.type, q.bw, q.bh, q.face, q.fill, q.i), Math.Max(1, ArtFurniture.Rise(q.type, q.bw, q.bh, q.face))));
                Put(img, nbody, Px(bx), Py(by) - nrise);
                if (nrise >= 6 && type != "Bed") _front.Add((nbody, Px(bx), Py(by) - nrise, by + bh - 1));
                int ndev = DeviceOf(net, i);
                int stIdx = NewGlowState(net, i);
                if (stIdx != 0)
                {
                    var g = NewGlow(type, bw, bh, face, stIdx, frame);
                    if (g.W > 1) { int gx = Px(bx), gy = Py(by) - nrise; if (Lighting) _emissive.Add(im => Put(im, g, gx, gy)); else Put(img, g, gx, gy); }
                }
                else if (ndev >= 0) img.Fill(Px(bx), Py(by) - nrise, bw * T, bh * T + nrise, Rgba.Hex("#000308"), 0.3f);
                continue;
            }
            if (_washKind[i] != 0)   // 화장실 설비 (5단 A1): 몸은 상태대로 (변기 때 3단 × 막힘 — 굽는다) · 물줄기 · '사용 금지' 불은 장마다
            {
                int wk = _washKind[i], wr = WashRise(wk);
                var wbody = WashBodyImg(life, i);
                int wx = Px(bx), wy = Py(by) - wr;
                Put(img, wbody, wx, wy);
                _front.Add((wbody, wx, wy, by + bh - 1));   // 앞 가림 (솟은 몸 — 변기 6 · 세면대 10 · 샤워 칸 14도트)
                if (!_bake) WashOver(img, net, mind, i, wk, wx, wy, frame);
                continue;
            }
            var ft = _fixType[i] ?? FurnitureType.Shelf;
            int rise = DotMachines.RiseOf(ft);
            var body = Cached(Key(5, (int)ft, bw, bh, i), (ft, bw, bh, i, rise), static q => Shadowed(ArtPalette.Unify(DotMachines.Body(q.ft, q.bw, q.bh, -1, 1, 0, q.i)), q.rise));   // 옛 그림은 새 색표 쪽으로 (T2)
            int px = Px(bx), py = Py(by) - rise;
            Put(img, body, px, py);
            if (rise >= 6 && ft is not (FurnitureType.Bed or FurnitureType.Seat or FurnitureType.CrashSeat or FurnitureType.MedBed)) _front.Add((body, px, py, by + bh - 1));
            var dev = DeviceOf(net, i);
            var state = dev < 0 ? FixState.On : net!.IsBroken(dev, Part.Electric) || net.IsBroken(dev, Part.Mechanical) ? FixState.Broken : net.Working(dev) ? FixState.On : FixState.Off;
            if (state != FixState.Off)
            {
                var glow = OldGlow(ft, bw, bh, state, frame);   // 같은 종류 · 크기 · 상태 · 장이면 같은 그림
                int gpx = px, gpy = py;   // 켜진 설비만 붙잡는다 (설비마다 갇힌 변수 묶음을 잡지 않게)
                if (Lighting) _emissive.Add(im => Put(im, glow, gpx, gpy));   // 화면 · 불빛은 스스로 빛난다
                else Put(img, glow, px, py);
            }
            else img.Fill(px, py, bw * T, bh * T + rise, Rgba.Hex("#000308"), 0.3f);
        }

        lt = Lap("설비", lt);
        // ── 바닥에 놓인 · 날아온 물건 (④ 물체 장부 — 보이는 도트가 곧 몸: 도트 꼴을 자리 X · Y · 자세 Th대로 쌓아 그린다 · 틱 사이 메움은 PH5) ──
        if (st != null && !_bake)
        {
            _stacks.BeginFrame();
            _samePlace.Clear();
            foreach (var it in st.Props)
            {
                if (it.Kind != 0) continue;
                int c = st.CellOf(it);
                if (c < 0 || !Mine(c)) continue;
                int x = L.X(c), y = L.Y(c);
                if (x < x0 || y < y0 || x >= x0 + w || y >= y0 + h) continue;
                var d = _stacks.Get(PropLook(s.Tables, it), PropStep(it.Th));
                var (nx, ny) = SamePlace(it);
                Put(img, d.Img, (int)Math.Round((it.X - x0) * T - d.Ax) + nx, (int)Math.Round((it.Y - y0) * T - d.Ay) + ny);
            }
        }

        lt = Lap("바닥에 놓인 · 날아온 물건", lt);
        // ── 칸 덮개: 물 · 진공 · 열 · 어둠 ──
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] is not (CellKind.Floor or CellKind.Door) || !Mine(c)) continue;
                if (_bake && L.Kind[c] == CellKind.Door) continue;   // 문 칸의 얼룩은 넓은 보기가 장마다 문짝 위에 (32도트와 같은 차례)
                double sootG = f.SootGPerM2(c);
                if (sootG > 0.05) SootStain(img, Px(x), Py(y), T, T, _bake ? SootStep(sootG) : sootG, c); // 바닥에 앉은 그을음
                if (L.Kind[c] == CellKind.Door) MoldOn(img, life, c, Px(x), Py(y));   // 문 칸 곰팡이는 문짝 위에 (5단 A3 — 바닥 칸은 바닥 타일과 함께 · 굽는 덩어리는 문 칸을 건너뛴다)
                FloorLiquids(img, f, c, Px(x), Py(y), frame);   // 냉매 · 전해액 웅덩이
                if (_bake) continue;   // 물 · 서리 · 열 · 어둠은 넓은 보기가 장마다
                double water = f.Stored(f.T.sWater, c);
                if (water > 2)
                {
                    if (L.FurnitureAt[c] >= 0 && _washKind[L.FurnitureAt[c]] != 0) WashWater(img, life, c, Px(x), Py(y), water, frame);   // 화장실 설비 칸: 설비를 덮지 않고 발치에 (많이 고이면 설비가 물에 선다)
                    else Put(img, Cached(Key(6, frame & 3, c & 7), (f4: frame & 3, c8: c & 7), static q => DotFx.WaterTile(q.f4, q.c8)), Px(x), Py(y), false, (float)Math.Min(0.85, water / 60));
                }
                if (L.Kind[c] == CellKind.Door && L.DoorAt[c] >= 0 && f.DoorOpen[L.DoorAt[c]] < 0.05f) continue;   // 닫힌 문 칸은 기체 칸이 아니다 (기압 0으로 읽혀 늘 서리로 덮였다 — 하늘색 문)
                double kpa = f.Pressure(c);
                if (kpa < 40) img.Fill(Px(x), Py(y), T, T, Rgba.Hex("#8fd0ff"), (float)(0.42 * (1 - kpa / 40))); // 진공: 서리 (어두워진 새 바닥에서도 읽히게 0.36 → 0.42)
                double temp = f.Temp(c);
                if (temp > 330 && kpa > 5) img.Fill(Px(x), Py(y), T, T, Rgba.Hex("#ff5a1e"), (float)Math.Min(0.35, (temp - 330) / 400));
                float dark = 0;
                if (light != null && !Lighting) { float lux = light.Lux[c]; dark = lux >= 60 ? 0 : (float)Math.Min(0.72, 0.72 * (1 - lux / 60)); }
                if (dark > 0.02f) img.Fill(Px(x), Py(y), T, T, Rgba.Hex("#02040a"), dark);
            }

        lt = Lap("칸 덮개", lt);
        // ── 사람 · 로봇 (아래쪽이 앞) ──
        if (_bake) return img;   // 굽는 덩어리는 여기까지 (사람 · 물방울 · 연기 · 빛 · 불은 넓은 보기가 장마다 — _front는 앞 가림에 남긴다)
        if (mind != null && life != null)
        {
            foreach (var a in mind.Agents.OrderBy(a => L.Y(a.C.Cell)).ThenBy(a => a.Id))
            {
                int cell = a.C.Cell;
                int x = L.X(cell), y = L.Y(cell);
                if (x < x0 - 1 || y < y0 - 1 || x > x0 + w || y > y0 + h + 1 || !Mine(cell)) continue;
                var (nx, ny) = Nudge?.Invoke(a) ?? (0, 0);
                if (a.IsPerson)
                {
                    DrawPerson(img, mind, life, a, Px(x) + nx, Py(y) + ny, frame);
                    if (ShowerIn(a) is int sh and >= 0) Put(img, CurtainImg(), Px(_fixBox[sh].x), Py(_fixBox[sh].y) - WashRise(3));   // 씻는 동안 닫힌 커튼 (그 사람 바로 뒤에 — 막대 위로 머리만)
                }
                else DrawBot(img, a, Px(x) + nx, Py(y) + ny, frame);
                // 앞 가림 (T9): 이 사람 · 로봇보다 아래 줄에 밑이 있는 솟은 설비가 그 그림과 겹치면 겹친 곳만 다시 얹는다
                int sx0 = Px(x) + nx, sy0 = Py(y) + ny + T - 46, sx1 = sx0 + T, sy1 = Py(y) + ny + T;
                foreach (var (fimg, fx, fy, baseRow) in _front)
                {
                    if (baseRow <= y || fx >= sx1 || fy >= sy1 || fx + fimg.W <= sx0 || fy + fimg.H <= sy0) continue;
                    PutClip(img, fimg, fx, fy, sx0, sy0, sx1, sy1);
                }
            }
        }
        if (FrontLog != null) foreach (var (fimg, fx, fy, _) in _front) FrontLog.Add((fimg, fx, fy));
        _front.Clear();

        lt = Lap("사람 · 로봇", lt);
        // ── 떠 있는 물방울 (무중력 — 사람 위 공중에 떠돈다 · 많을수록 크고 많다) ──
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (!Mine(c)) continue;
                double dr = f.Floating(f.T.sWater, c);
                if (dr < 3) continue;
                int n = (int)Math.Min(5, 1 + dr / 25);
                for (int k = 0; k < n; k++)
                {
                    uint hsh = (uint)(c * 73856093) ^ (uint)(k * 19349663);
                    int bob = ((frame / 6 + k) % 4) switch { 0 => 0, 1 => 1, 2 => 0, _ => -1 };   // 천천히 흔들린다
                    int r = dr > 60 && k % 2 == 0 ? 7 : dr > 20 ? 5 : 4;   // 둥근 물방울 (큰 덩어리는 출렁인다)
                    int px = Px(x) + 1 + (int)(hsh % (uint)(T - r - 2)), py = Py(y) + 1 + (int)(hsh / 7 % (uint)(T - r - 2)) + bob;
                    int wob = (frame / 4 + k) % 2;   // 무중력 물 덩어리는 늘 출렁인다
                    img.Fill(px + 1, py + wob, r - 2, r - wob, Rgba.Hex("#3e8fd0"), 0.85f);
                    img.Fill(px, py + 1, r, r - 2, Rgba.Hex("#3e8fd0"), 0.85f);
                    img.Fill(px + 1, py + 1, r - 2, r - 3, Rgba.Hex("#7cc6f4"), 0.9f);
                    img.Fill(px + 1, py + 1, 2, 1, Rgba.Hex("#f0fbff"), 0.95f);   // 빛 받은 자리
                }
            }

        lt = Lap("떠 있는 물방울", lt);
        // ── 김 · 습한 방의 안개 · 거울 김 서림 (5단 A2 — 세계 습도를 읽기만 · 장마다 · 사람 위에) ──
        WashAir(img, f, net, x0, y0, w, h, frame);
        lt = Lap("김 · 안개", lt);
        // ── 새는 공기 줄기 (칸 바람이 빠른 칸만) ──
        AirJets(img, f, x0, y0, w, h, frame);
        lt = Lap("새는 공기 줄기", lt);
        // ── 연기 (빛을 받는다 — 불 곁은 붉게 · 정전된 방은 어둡게): 뭉게 연기는 아랫층 · 천장 아래 짙은 층은 엷은 막 ──
        for (int y = y0; y < y0 + h + 1; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (!Mine(c)) continue;
                if (!Lighting && chem != null && chem.Burning[c] != 0) FireAt(img, chem, c, Px(x), Py(y), frame);
                double soot = PuffPpm(f, c);
                if (soot > 20)
                {
                    float al = (float)Math.Min(0.8, soot / 500);
                    var puff = Cached(Key(7, c & 15), c & 15, static k => DotFx.SmokeSprite(T + 12, k));
                    int dx = (int)((DotHash.F(c, frame / 4) - 0.5f) * 6);
                    Put(img, puff, Px(x) - 6 + dx, Py(y) - 10, false, al);
                }
            }
        SmokeVeil(img, f, x0, y0, w, h, frame);
        lt = Lap("연기", lt);
        // ── 부스러기 알갱이 (배 그림에만 — 빛을 받는다) ──
        if (!part && st != null) Shards(img, s, st, x0, y0);
        lt = Lap("부스러기", lt);
        // ── 빛: 곱하고 · 스스로 빛나는 것 · 불꽃 (맨 앞) ──
        if (Lighting)
        {
            BuildLight(s, f, net, light, chem, st, x0, y0, w, h, frame);
            WallLight();
            ApplyLight(img, w, h);
            SmokeScatter(img, f, x0, y0, w, h, frame);   // 연기가 빛을 받아 빛난다
            foreach (var e in _emissive) e(img);
            if (chem != null)
                for (int y = y0; y < y0 + h + 1; y++)
                    for (int x = x0; x < x0 + w; x++)
                    {
                        if (!L.In(x, y)) continue;
                        int c = L.Index(x, y);
                        if (Mine(c) && chem.Burning[c] != 0) FireAt(img, chem, c, Px(x), Py(y), frame);
                    }
        }
        lt = Lap("빛", lt);
        HeatHaze(img, f, x0, y0, w, h, frame);          // 아지랑이 (불꽃까지 함께 일렁인다)
        FxEmissive(img, s, net, x0, y0, w, h, frame);   // 합선 불꽃 · 충격파 · 방사선 잡음
        lt = Lap("아지랑이 · 효과", lt);
        if (Show != Overlay.None) DrawOverlay(img, f, net, x0, y0, w, h);
        Lap("겹쳐 보기", lt);
        return img;
    }

    /// <summary>불꽃 한 칸 (세기만큼 크게) — 조명을 켰으면 둘레에 빛 번짐.</summary>
    private void FireAt(Img img, Chemistry chem, int c, int px, int py, int frame)
    {
        float power = Math.Clamp(chem.Hrr[c] / 40f, 0.25f, 1f);
        if (Lighting) img.Glow(px + T / 2, py + T / 2, 10 + power * 10, Ramps.Get(Ramps.Fire, 5), 0.10f + 0.12f * power);
        Put(img, DotFx.FireTile(frame + c, power, c), px, py + T - 40);
    }

    private static (float hole, int _) F(Field f, int c) => (f.Hole[c], 0);

    private const int SubPx = T / Structure.SubN;

    /// <summary>판의 빈 잔칸: 우주(조각 그림이면 투명) · 덧댔으면 덧판 · 남은 잔칸과 맞닿은 가장자리는 찢긴 금속 테.</summary>
    private static void SubMask(Img img, ushort m, int px, int py, bool patched, bool part, Rgba space)
    {
        var plate = Rgba.Hex("#6e6656"); var rivet = Rgba.Hex("#3b362c"); var rim = Rgba.Hex("#a3adbd");
        for (int i = 0; i < 16; i++)
        {
            if ((m >> i & 1) != 0) continue;
            int sx = i % Structure.SubN, sy = i / Structure.SubN, qx = px + sx * SubPx, qy = py + sy * SubPx;
            if (patched) { img.Fill(qx, qy, SubPx, SubPx, plate); img.Set(qx + 1, qy + 1, rivet); img.Set(qx + SubPx - 2, qy + SubPx - 2, rivet); continue; }
            if (part) { for (int yy = 0; yy < SubPx; yy++) for (int xx = 0; xx < SubPx; xx++) img.Set(qx + xx, qy + yy, new Rgba(0, 0, 0, 0)); }
            else img.Fill(qx, qy, SubPx, SubPx, space);
        }
        if (patched) return;
        // 찢긴 테: 남은 잔칸의 빈 쪽 가장자리를 밝게
        for (int i = 0; i < 16; i++)
        {
            if ((m >> i & 1) == 0) continue;
            int sx = i % Structure.SubN, sy = i / Structure.SubN, qx = px + sx * SubPx, qy = py + sy * SubPx;
            if (sx > 0 && (m >> (i - 1) & 1) == 0) for (int k = 0; k < SubPx; k++) img.Blend(qx, qy + k, rim, 0.85f);
            if (sx < Structure.SubN - 1 && (m >> (i + 1) & 1) == 0) for (int k = 0; k < SubPx; k++) img.Blend(qx + SubPx - 1, qy + k, rim, 0.85f);
            if (sy > 0 && (m >> (i - Structure.SubN) & 1) == 0) for (int k = 0; k < SubPx; k++) img.Blend(qx + k, qy, rim, 0.85f);
            if (sy < Structure.SubN - 1 && (m >> (i + Structure.SubN) & 1) == 0) for (int k = 0; k < SubPx; k++) img.Blend(qx + k, qy + SubPx - 1, rim, 0.85f);
        }
    }

    /// <summary>우주가 된 사이 칸에 남은 잔칸: 지금 그리는 몸에 붙은 것만 찢긴 철판 조각으로.</summary>
    private void TornEdge(Img img, Structure st, int c, int px, int py)
    {
        ushort m = st.Sub[c];
        var plate = Rgba.Hex("#4a5262"); var rim = Rgba.Hex("#a3adbd");
        for (int i = 0; i < 16; i++)
        {
            if ((m >> i & 1) == 0) continue;
            int sx = i % Structure.SubN, sy = i / Structure.SubN;
            int owner = st.TornOwner(c, sx, sy);
            if (owner < 0 || _only >= 0 && owner != _only) continue;
            int qx = px + sx * SubPx, qy = py + sy * SubPx;
            img.Fill(qx, qy, SubPx, SubPx, plate);
            img.Fill(qx, qy, SubPx, 1, rim, 0.7f);
            img.Fill(qx, qy + SubPx - 1, SubPx, 1, Rgba.Hex("#1a1e26"), 0.8f);
        }
    }

    // ── 물체 그림 (PH4a — 옛 바닥 물건 아이콘 LooseIcon을 걷었다) ──
    private readonly DotStackCache _stacks = new();
    private readonly Dictionary<(double, double), int> _samePlace = new();
    private string[]? _itemNames;

    /// <summary>물체의 도트 꼴 (물건: 사람 표의 그 물건 · 누움 — 세계와 같은 꼴 · 표의 캐시는 스레드에 안전하다).</summary>
    private DotForm PropLook(Tables t, in Prop p)
    {
        _itemNames ??= t.People.Items.Keys.ToArray();
        return DotForge.Make(t, p.What >= 0 && p.What < _itemNames.Length ? _itemNames[p.What] : "_default", lie: p.Pose);
    }

    /// <summary>자세 → 쌓아 그리기 각 단 (64단 · 감지 않은 각도 그대로).</summary>
    private static int PropStep(double th) => (int)Math.Round(th / (2 * Math.PI) * DotStack.Steps) & (DotStack.Steps - 1);

    /// <summary>꼭 같은 자리에 놓인 것 (같은 칸 가운데에 내려놓은 물건들)은 조금씩 비켜 그린다 (그림만 — 세계 자리는 그대로).</summary>
    private (int x, int y) SamePlace(in Prop p)
    {
        int n = _samePlace.GetValueOrDefault((p.X, p.Y));
        _samePlace[(p.X, p.Y)] = n + 1;
        return n == 0 ? (0, 0) : ((n % 3 - 1) * 7, n / 3 * 6 + (n % 3 == 1 ? 3 : 0));
    }

    /// <summary>그을음 얼룩: 묻은 양(g/m²)만큼 검게 — 2×2 점마다 조금씩 달리 (고르게 칠한 판이 아니라 얼룩으로 · 0.5g/m²에 3할 · 2g/m²에 6할).</summary>
    private static void SootStain(Img img, int px, int py, int w, int h, double gPerM2, int seed)
    {
        float a = (float)(0.8 * (1 - Math.Exp(-gPerM2 / 1.1)));
        var ink = Rgba.Hex("#15110d");
        for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                float k = a * (0.55f + 0.9f * DotHash.F(seed, x * 31 + y, 3));
                if (k >= 0.02f) img.Fill(px + x, py + y, 2, Math.Min(2, h - y), ink, Math.Min(0.9f, k));
            }
    }

    private static Img Shadowed(Img body, int rise)
    {
        if (rise <= 0) return body;
        var o = new Img(body.W + 3, body.H + 2);
        o.DrawShadowOf(body, 0, 0, 0.3f);
        o.Draw(body, 0, 0);
        return o;
    }

    /// <summary>이 장에 그린 솟은 설비 (그림 · 자리 · 밑 줄) — 앞 가림에 쓴다.</summary>
    private readonly List<(Img img, int x, int y, int baseRow)> _front = new();
    /// <summary>시험용: null이 아니면 32도트 장마다 솟은 설비 몸 (그림 · 그림판 자리)을 모은다 (앞에 선 몸이 덮은 자리가 그대로인지 견준다).</summary>
    public List<(Img img, int x, int y)>? FrontLog;

    /// <summary>그림의 (cx0, cy0)~(cx1, cy1) 안쪽만 얹는다 (투명 도트는 건너뛴다).</summary>
    private static void PutClip(Img dst, Img src, int dx, int dy, int cx0, int cy0, int cx1, int cy1)
    {
        int x0 = Math.Max(Math.Max(0, cx0), dx), y0 = Math.Max(Math.Max(0, cy0), dy);
        int x1 = Math.Min(Math.Min(dst.W, cx1), dx + src.W), y1 = Math.Min(Math.Min(dst.H, cy1), dy + src.H);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = src.Get(x - dx, y - dy);
                if (c.A == 255) dst.Set(x, y, c);
                else if (c.A > 0) dst.Blend(x, y, c);
            }
    }

    private int[]? _devOf;
    private int DeviceOf(Network? net, int furniture)
    {
        if (net == null) return -1;
        if (_devOf == null)
        {
            _devOf = Enumerable.Repeat(-1, L.Furniture.Count).ToArray();
            foreach (var d in net.Devices) if (d.Furniture >= 0 && d.Furniture < _devOf.Length) _devOf[d.Furniture] = d.Id;
        }
        return _devOf[furniture];
    }

    private static void Hiss(Img img, int cx, int cy, int frame)
    {
        for (int k = 0; k < 6; k++)
        {
            float a = DotHash.F(k, frame) * 6.283f, r = 4 + DotHash.F(frame, k) * 10;
            img.Blend((int)(cx + MathF.Cos(a) * r), (int)(cy + MathF.Sin(a) * r), Rgba.Hex("#dff4ff"), 0.7f);
        }
    }

    // ───────────────────────── 사람 ─────────────────────────

    private static readonly string[] RoleOrder = { "기관사", "의무관", "조리사", "조종사", "농부", "과학자", "정비사" };

    private PersonLook LookOf(Mind mind, Agent a)
    {
        if (_looks.TryGetValue(a.Id, out var l)) return l;
        int seed = a.C.Name * 31 + a.Id;
        uint u = DotHash.U(seed, 3);
        string role = a.C.Role < mind.RoleNames.Length ? mind.RoleNames[a.C.Role] : "";
        var (cloth, badge) = role switch
        {
            "기관사" => (Ramps.ClothOrange, 1),
            "의무관" => (Ramps.ClothWhite, 2),
            "조종사" => (Ramps.ClothBlue, 3),
            "정비사" => (Ramps.ClothTeal, 4),
            "농부" => (Ramps.ClothGreen, 5),
            "과학자" => (Ramps.ClothPurple, 6),
            "조리사" => (Ramps.ClothBeige, 0),
            _ => (Ramps.ClothNavy, 0),
        };
        bool female = (u & 1) != 0;
        var hairs = female
            ? new[] { DotHair.Bob, DotHair.Long, DotHair.Ponytail, DotHair.Bun, DotHair.Braid, DotHair.Crop }
            : new[] { DotHair.Buzz, DotHair.Crop, DotHair.Side, DotHair.Curly, DotHair.Receding };
        l = new PersonLook
        {
            Female = female,
            Skin = (int)(u >> 1) % 6,
            HairRamp = Ramps.Hair[(int)(u >> 4) % Ramps.Hair.Length],
            Hair = hairs[(int)(u >> 8) % hairs.Length],
            Cloth = cloth,
            Accent = Ramps.Accent[(int)(u >> 12) % Ramps.Accent.Length],
            Badge = badge,
            Build = (DotBuild)((u >> 16) % 4),
            Height = (int)((u >> 18) % 5) - 2,
            Glasses = (u >> 21) % 6 == 0,
            Stubble = !female && (u >> 23) % 4 == 0,
            Seed = seed,
        };
        Diversify(l, u);
        _looks[a.Id] = l;
        return l;
    }

    private long _tick;   // 그리는 순간의 틱 (고개를 돌린 동안인지)

    /// <summary>바라보는 쪽: 걸으면 가는 쪽 · 서 있으면 소리 난 쪽으로 고개를 돌린 동안 그 쪽 (판단이 정한 것 — 소리 단계 S4) · 아니면 앞(아래).</summary>
    public static DotFacing FacingOf(Layout L, Agent a, long tick)
    {
        if (a.PathIdx < a.Path.Count)
        {
            int n = a.Path[a.PathIdx];
            int dx = L.X(n) - L.X(a.C.Cell), dy = L.Y(n) - L.Y(a.C.Cell);
            if (dx != 0) return dx > 0 ? DotFacing.E : DotFacing.W;
            if (dy != 0) return dy > 0 ? DotFacing.S : DotFacing.N;
        }
        if (a.C.LookUntil >= tick && (uint)a.C.LookAt < (uint)L.N && a.C.LookAt != a.C.Cell)
        {
            int dx = L.X(a.C.LookAt) - L.X(a.C.Cell), dy = L.Y(a.C.LookAt) - L.Y(a.C.Cell);
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx > 0 ? DotFacing.E : DotFacing.W;
            return dy > 0 ? DotFacing.S : DotFacing.N;
        }
        return DotFacing.S;
    }

    private void DrawPerson(Img img, Mind mind, Life life, Agent a, int px, int py, int frame)
    {
        ref var c = ref a.C;
        if (c.CarriedBy >= 0) return; // 업힌 사람은 업은 이와 함께
        var b = life.Bodies[c.Body];
        var l = LookOf(mind, a);
        l.Suit = b.Suit != 0;
        l.Dead = b.Alive == 0;
        bool lying = b.Alive == 0 || b.Conscious == 0 || b.Asleep != 0;
        l.EyesClosed = lying;
        l.Hurt = HurtOf(b);
        int stain = StainOf(b);   // 옷의 얼룩 · 지린 자국 (5단 A3 — ⑦ 더러움 · 지림)
        l.Face = lying ? DotFace.Calm : FaceOf(c, b);
        l.Held = c.Holding == 255 ? DotHeld.None : mind.ItemNames[c.Holding] switch
        {
            "소화기" => DotHeld.Extinguisher,
            "수리 꾸러미" => DotHeld.Wrench,
            "봉합 꾸러미" => DotHeld.Box,
            "구급 상자" or "제세동기" => DotHeld.MedKit,
            "수확물" => DotHeld.Plate,
            "식량 상자" => DotHeld.Box,
            _ => DotHeld.None,
        };
        // 무중력: 떠 있는 사람은 바닥에서 떠올라 천천히 오르내리고 · 바닥에 옅은 그림자
        int lift = 0;
        if (c.Floated != 0)
        {
            lift = 6 + ((frame / 5 + a.Id) % 4) switch { 0 => 0, 1 => 1, 2 => 2, _ => 1 };
            img.Oval(px + T / 2, py + T - 4, 5, 1.6f, FootShade, 0.22f);   // 떠 있으면 옅고 작게
        }
        else if (b.Low != 0 && !lying) img.Oval(px + T / 2, py + T / 2 + 9, 9, 2.4f, FootShade, 0.34f);
        else if (lying) img.Oval(px + T / 2, py + T / 2 + 5, 12, 3.2f, FootShade, 0.32f);
        else img.Oval(px + T / 2, py + T - 3, 6.5f, 2.1f, FootShade, 0.36f);   // 발밑 그림자 (T9 — 늘: 사람이 바닥에 선다)
        if (lying)
        {
            // 누운 사람: 옆으로 돌린 그림 (머리가 왼쪽)
            l.Motion = DotMotion.None;
            var lie = PersonImg(a.Id, l, DotFacing.S, DotPose.Stand, 0, true, false, stain);
            int breathe = b.Alive != 0 && ((frame + a.Id) & 7) >= 5 ? 1 : 0;   // 자는 사람은 숨을 쉰다 (가슴이 오르내린다)
            Put(img, lie, px + T / 2 - lie.W / 2, py + T / 2 - lie.H / 2 - lift - breathe);
            return;
        }
        if (b.Low != 0 && c.Floated == 0 && c.Carrying < 0)
        {
            // 기는 사람 (③ 낮은 머리 — 연기 · 열 아래로 몸을 낮췄다): 엎드려 가는 쪽으로 · 걸으면 팔다리가 번갈아
            bool goes = a.PathIdx < a.Path.Count;
            var face0 = FacingOf(L, a, _tick);
            l.Cycle8 = true; l.Motion = DotMotion.None;
            var crawl = PersonImg(a.Id, l, face0 == DotFacing.W ? DotFacing.W : DotFacing.E, goes ? DotPose.Walk : DotPose.Stand, goes ? (frame + a.Id) & 7 : 0, false, true, stain);
            Put(img, crawl, px + T / 2 - crawl.W / 2, py + T / 2 - crawl.H / 2 + 4);
            return;
        }
        var step = c.StepIndex < a.Plan.Count ? a.Plan[c.StepIndex].Kind : StepKind.None;
        bool moving = a.PathIdx < a.Path.Count && step == StepKind.GoTo;
        bool pressing = step == StepKind.Use && c.Goal == GoalKind.Cpr;   // ⑦ 가슴 압박: 무릎 꿇고 누른다 (두 그림마다 한 번 내려앉는다)
        DotPose pose = c.Mode == 2 ? DotPose.Cower
            : c.Carrying >= 0 ? DotPose.Carry
            : moving ? DotPose.Walk
            : pressing ? DotPose.Kneel
            : step == StepKind.Use && c.Goal is GoalKind.Treat or GoalKind.Surgery ? DotPose.Work   // ⑦ 돌보기 · 수술
            : step is StepKind.Spray or StepKind.Seal or StepKind.Fix or StepKind.Brace or StepKind.Craft or StepKind.Maintain or StepKind.Clean ? DotPose.Work
            : step == StepKind.Use && c.Goal is GoalKind.Eat or GoalKind.Relax or GoalKind.Socialize or GoalKind.Work ? DotPose.Sit
            : DotPose.Stand;
        var face = FacingOf(L, a, _tick);
        if (c.Floated != 0 && pose is DotPose.Walk or DotPose.Sit) pose = DotPose.Stand;   // 떠서는 걷지도 앉지도 않는다 (손잡이를 잡고 민다)
        // 생활 동작 (그래픽 결정 10 — 하는 일에서): 먹고 마시면 손이 입으로 · 고치면 공구를 돌리고 · 말하면 손짓 · 앉아서 졸면 고개를 떨군다
        l.Cycle8 = true;
        l.Motion = MotionOf(c, step, pose);
        if (l.Motion == DotMotion.Drink && c.Goal == GoalKind.Drink && l.Held == DotHeld.None) l.Held = DotHeld.Cup;
        if (l.Motion == DotMotion.Tool && l.Held == DotHeld.None) l.Held = DotHeld.Wrench;
        if (l.Motion == DotMotion.Doze) { l.EyesClosed = true; l.Face = DotFace.Calm; }
        int fr = pose == DotPose.Walk || l.Motion != DotMotion.None ? (frame + a.Id) & 7 : 0;
        var dot = PersonImg(a.Id, l, face, pose, fr, false, false, stain);
        int ox = px + T / 2 - DotPeople.Cx, oy = py + T - DotPeople.Foot - 2 - lift + (pressing ? (frame + a.Id) & 1 : 0);
        Put(img, dot, ox, oy);
        if (c.Carrying >= 0)
        {
            // 업은 사람: 누운 몸을 어깨 위에
            int vb = c.Carrying;
            var va = mind.Agents.FirstOrDefault(o => o.IsPerson && o.C.Body == vb);
            if (va != null)
            {
                var vl = LookOf(mind, va);
                vl.EyesClosed = true; vl.Dead = life.Bodies[vb].Alive == 0; vl.Suit = life.Bodies[vb].Suit != 0;
                vl.Hurt = HurtOf(life.Bodies[vb]); vl.Face = DotFace.Calm;
                var lie = PersonImg(va.Id, vl, DotFacing.S, DotPose.Stand, 0, true, false, StainOf(life.Bodies[vb]));
                Put(img, lie, px + T / 2 - lie.W / 2, oy + 6);
            }
        }
        if (c.Mode == 2)   // 공황: 머리 위 느낌표 (표시 — 빛을 받지 않는다)
        {
            int mx = px + T / 2, my = oy - 4;
            if (Lighting) _emissive.Add(im => Mark(im, mx, my, Rgba.Hex("#ffd146")));
            else Mark(img, mx, my, Rgba.Hex("#ffd146"));
        }
    }

    private static readonly Rgba FootShade = Rgba.Hex("#16142a");   // 그늘 색 (푸른 보라 — 검정 대신)

    private static void Mark(Img img, int cx, int top, Rgba col)
    {
        img.Fill(cx - 1, top, 2, 5, col);
        img.Fill(cx - 1, top + 6, 2, 2, col);
    }

    // ───────────────────────── 겹쳐 보기 ─────────────────────────

    private void DrawOverlay(Img img, Field f, Network? net, int x0, int y0, int w, int h, int t = T)
    {
        int Px(int x) => (x - x0) * t;
        int Py(int y) => (y - y0) * t;
        var T2 = f.T;
        if (Show == Overlay.Lines) { if (net != null) DrawLines(img, net, x0, y0, w, h, t); return; }
        if (Show != Overlay.Chain)
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    if (!L.In(x, y)) continue;
                    int c = L.Index(x, y);
                    if (L.Kind[c] == CellKind.Wall || !Mine(c)) continue;
                    double kpa = f.Pressure(c);
                    switch (Show)
                    {
                        case Overlay.Air:
                        {
                            // 진공은 파랗게 · 산소가 옅을수록 붉게 (19kPa 아래) · 일산화탄소는 보랏빛
                            if (kpa < 5) { img.Fill(Px(x), Py(y), t, t, Rgba.Hex("#2f55ff"), 0.45f); break; }
                            double o2 = f.Partial(T2.gO2, c), co = f.Ppm(T2.gCO, c);
                            if (o2 < 19) img.Fill(Px(x), Py(y), t, t, Rgba.Hex("#ff3b30"), (float)Math.Min(0.55, 0.12 + (19 - o2) / 9 * 0.43));
                            if (co > 100) img.Fill(Px(x), Py(y), t, t, Rgba.Hex("#b347ff"), (float)Math.Min(0.5, co / 1500));
                            break;
                        }
                        case Overlay.Heat:
                        {
                            double tc = f.Temp(c) - 273.15;
                            if (tc > 30) img.Fill(Px(x), Py(y), t, t, tc > 60 ? Rgba.Hex("#ff2a1a") : Rgba.Hex("#ff9a1f"), (float)Math.Min(0.6, (tc - 30) / 80 + 0.12));
                            else if (tc < 12) img.Fill(Px(x), Py(y), t, t, Rgba.Hex("#53c8ff"), (float)Math.Min(0.5, (12 - tc) / 30 + 0.1));
                            break;
                        }
                        case Overlay.Smoke:
                        {
                            double soot = f.Ppm(T2.gSoot, c);
                            if (soot > 5) img.Fill(Px(x), Py(y), t, t, Rgba.Hex("#8a8f99"), (float)Math.Min(0.7, 0.1 + soot / 600));
                            break;
                        }
                    }
                }
        if (Show == Overlay.Chain && ChainCells != null && ChainCells.Count > 0)
        {
            var col = Rgba.Hex("#5ff0ff");
            (int, int) Mid(int c) => (Px(L.X(c)) + t / 2, Py(L.Y(c)) + t / 2);
            for (int i = 1; i < ChainCells.Count; i++) Line(img, Mid(ChainCells[i - 1]), Mid(ChainCells[i]), col);
            for (int i = 0; i < ChainCells.Count; i++)
            {
                var (mx, my) = Mid(ChainCells[i]);
                int r = Math.Max(1, (i == ChainCells.Count - 1 ? 5 : 3) * t / T);
                img.Fill(mx - r, my - r, 2 * r, 2 * r, i == ChainCells.Count - 1 ? Rgba.Hex("#ffffff") : col, 0.9f);
            }
        }
    }

    /// <summary>겹쳐 보기 '선과 관' (⑥ 바퀴 3): 전선 노랑 · 냉각관 하늘 · 물관 파랑 · 센서 선 초록 — 갈래마다 칸 안에서 조금씩 비켜 나란히 · 끊긴 칸 빨간 X · 새는 칸 보라.</summary>
    private void DrawLines(Img img, Network net, int x0, int y0, int w, int h, int t = T)
    {
        var cols = new[] { Rgba.Hex("#ffd23f"), Rgba.Hex("#6fe3ff"), Rgba.Hex("#3a7bff"), Rgba.Hex("#4fd17a") };
        var red = Rgba.Hex("#ff3b30"); var leak = Rgba.Hex("#c04dff");
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (net.LineAt[c] == 0 || !Mine(c)) continue;
                int px = (x - x0) * t, py = (y - y0) * t;
                for (int k = 0; k < 4; k++)
                {
                    if ((net.LineAt[c] & (1 << k)) == 0) continue;
                    int off = (k * 2 - 3) * Math.Max(1, t / 16), mx = px + t / 2 + off, my = py + t / 2 + off;   // 32 · 16도트는 예전과 같고 8도트에서도 넷이 갈린다
                    int dirs = net.LineDir[c * 4 + k];
                    var col = cols[k];
                    int q = Math.Max(1, 3 * t / T), q2 = Math.Max(1, 4 * t / T);   // 선 굵기 · 점 (넓은 보기는 도트만큼 가늘게)
                    if (dirs == 0) img.Fill(mx - q2 / 2, my - q2 / 2, q2, q2, col, 0.9f);
                    if ((dirs & 1) != 0) img.Fill(mx - q / 3, py, q, my - py + 1, col, 0.85f);
                    if ((dirs & 4) != 0) img.Fill(mx - q / 3, my, q, py + t - my, col, 0.85f);
                    if ((dirs & 8) != 0) img.Fill(px, my - q / 3, mx - px + 1, q, col, 0.85f);
                    if ((dirs & 2) != 0) img.Fill(mx, my - q / 3, px + t - mx, q, col, 0.85f);
                }
                bool cut = net.LineCut[c] != 0 || net.DataCutAt(c);
                if (cut)
                {
                    int a = t / 4;
                    Line(img, (px + a, py + a), (px + t - a, py + t - a), red);
                    Line(img, (px + t - a, py + a), (px + a, py + t - a), red);
                }
                if (net.LeakAt(c, LineKind.Coolant) > 0 || net.LeakAt(c, LineKind.Water) > 0) img.Fill(px + t / 2 - 5 * t / T, py + t / 2 - 5 * t / T, 10 * t / T, 10 * t / T, leak, 0.8f);
            }
    }

    private static void Line(Img img, (int x, int y) a, (int x, int y) b, Rgba col)
    {
        int dx = Math.Abs(b.x - a.x), dy = -Math.Abs(b.y - a.y), sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, err = dx + dy;
        int x = a.x, y = a.y;
        for (int k = 0; k < 20000; k++)
        {
            if (x >= 0 && y >= 0 && x < img.W - 1 && y < img.H - 1) { img.Blend(x, y, col, 0.85f); img.Blend(x + 1, y, col, 0.85f); img.Blend(x, y + 1, col, 0.85f); }
            if (x == b.x && y == b.y) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    // ───────────────────────── 로봇 · 드론 ─────────────────────────

    private void DrawBot(Img img, Agent a, int px, int py, int frame)
    {
        ref var c = ref a.C;
        var step = c.StepIndex < a.Plan.Count ? a.Plan[c.StepIndex].Kind : StepKind.None;
        var state = c.Damage >= 1 ? DotBotState.Broken : c.Battery < 0.03f ? DotBotState.Off
            : step is StepKind.Spray or StepKind.Seal or StepKind.Fix or StepKind.Brace or StepKind.Craft or StepKind.Maintain or StepKind.Clean or StepKind.PickUp ? DotBotState.Working
            : DotBotState.Ok;
        if (c.Kind == AgentKind.Drone)
        {
            img.Oval(px + T / 2, py + T - 4, 5, 1.5f, FootShade, 0.2f);   // 떠 있는 드론의 옅은 그림자
            var dr = Cached(Key(14, (int)state, frame & 3, a.Id & 3), (state, f4: frame & 3), static q => DotBots.Drone(DotDroneKind.Repair, q.state, q.f4));
            Put(img, dr, px + T / 2 - dr.W / 2, py + T / 2 - dr.H / 2 - 8);
            return;
        }
        var kind = (DotBotKind)(a.Id % 4);
        int seed = a.Id & 0xFFF;   // 그을음 · 불꽃의 씨앗 — 열쇠와 그림이 같은 값 (예전엔 열쇠 Id & 7 · 그림 Id 전체라 Id가 8 차이 나는 로봇이 먼저 그린 로봇의 그림을 꺼냈다)
        var bot = Cached(Key(8, (int)kind, (int)state, frame & 3, seed), (kind, state, f4: frame & 3, seed), static q => DotBots.Robot(q.kind, q.state, q.f4, q.seed));
        img.Oval(px + T / 2, py + T - 2, 7.5f, 2.2f, FootShade, 0.38f);   // 바퀴 · 다리 밑 그림자
        Put(img, bot, px + T / 2 - DotBots.Cx, py + T - DotBots.Foot - 1);
    }
}
