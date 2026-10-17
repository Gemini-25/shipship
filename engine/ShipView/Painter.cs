using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipView;

/// <summary>
/// 새 화면의 그리기 — 새 엔진의 세계를 옛 게임의 도트 화가로 한 장에 그린다 (Godot 없이 · 칸 하나 = 32도트).
/// 세계를 바꾸지 않고 읽기만 한다. 게임(Godot)은 이 그림을 텍스처로 올리고, 실험실은 PNG로 뽑아 눈으로 본다.
/// 층: 우주 → 바닥 → 벽 · 남쪽 앞면 → 문 → 외판 구멍 → 설비 → 물 · 진공 · 열 · 어둠 → 사람 · 로봇 → 불 · 연기.
/// </summary>
public sealed class Painter
{
    public const int T = DotTiles.T;

    private readonly Layout L;
    private readonly DotFloor[] _roomFloor;
    /// <summary>새 설비 → 닮은 옛 도트 그림 (제 그림이 생기기 전까지).</summary>
    private static readonly Dictionary<string, string> Look = new()
    {
        ["FoodLocker"] = "SupplyCache", ["GrowRack"] = "PlantRack", ["LargeReactorCore"] = "ReactorCore", ["LargeCoolantPump"] = "CoolantPump",
        ["Shuttle"] = "DockClampPanel", ["Rover"] = "MaintCart", ["CargoCrate"] = "SupplyCache", ["OreBin"] = "Compactor",
    };
    private readonly FurnitureType?[] _fixType;
    private readonly (int x, int y, int w, int h)[] _fixBox;
    private readonly Dictionary<long, Img> _cache = new();
    private readonly Dictionary<int, PersonLook> _looks = new();

    /// <summary>사람 · 로봇을 칸 자리에서 몇 픽셀 옮겨 그린다 (관전 화면이 틱 사이 걸음을 메운다 — 없으면 칸 그대로).</summary>
    public Func<Agent, (int dx, int dy)>? Nudge;

    public Painter(Layout layout)
    {
        L = layout;
        _roomFloor = L.Rooms.Select(r => FloorFor(r.Type)).ToArray();
        _fixType = new FurnitureType?[L.Furniture.Count];
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

    /// <summary>방 종류 → 바닥 (옛 게임 Materials.FloorFor와 같은 갈래).</summary>
    public static DotFloor FloorFor(string type) => type switch
    {
        "Engine" or "Reactor" or "Cooling" or "PumpRoom" or "HvacRoom" or "Crusher" or "PropellantTank" or "HeatStorage" or "GasStorage" or "Recycling" => DotFloor.Grate,
        "Power" or "Substation" or "BatteryRoom" or "FuelCell" or "ServerRoom" or "ElectronicsLab" or "Bridge" or "BackupBridge" or "Comms"
            or "Navigation" or "CraneControl" or "Calibration" or "ComputerRoom" => DotFloor.Rubber,
        "Galley" or "Medbay" or "Hydroponics" or "Laundry" or "WaterPlant" or "Decon" or "Quarantine" or "QuarantineLock" or "Triage" or "Morgue"
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

    private static long Key(int kind, int a = 0, int b = 0, int c = 0, int d = 0) =>
        ((long)kind << 56) ^ ((long)(a & 0xFFFF) << 40) ^ ((long)(b & 0xFFFF) << 24) ^ ((long)(c & 0xFFF) << 12) ^ (d & 0xFFF);

    private bool Solid(int x, int y) => L.In(x, y) && L.Kind[L.Index(x, y)] is CellKind.Wall or CellKind.Door;
    private CellKind KindAt(int x, int y) => L.In(x, y) ? L.Kind[L.Index(x, y)] : CellKind.Outside;

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

    /// <summary>화면 한 장: 칸 (x0, y0)부터 w × h 칸. frame은 불꽃 · 걷기 같은 움직임의 장 번호.
    /// into: 다시 쓸 그림 (크기가 맞으면 — 관전 화면이 매 장 5MB를 새로 만들지 않게. 처음에 통째로 칠한다).</summary>
    public Img Frame(Sim s, int x0, int y0, int w, int h, int frame, Img? into = null)
    {
        var img = into != null && into.W == w * T && into.H == h * T ? into : new Img(w * T, h * T);
        var f = s.Get<Field>();
        var net = s.Find<Network>();
        var st = s.Find<Structure>();
        var chem = s.Find<Chemistry>();
        var light = s.Find<Light>();
        var mind = s.Find<Mind>();
        var life = s.Find<Life>();
        int Px(int x) => (x - x0) * T;
        int Py(int y) => (y - y0) * T;

        // ── 우주 ──
        var space = Rgba.Hex("#05070c");
        img.Fill(0, 0, img.W, img.H, space);
        for (int y = y0 - 1; y <= y0 + h; y++)
            for (int x = x0 - 1; x <= x0 + w; x++)
            {
                uint u = DotHash.U(x, y, 7);
                if (u % 9 != 0) continue;
                int sx = Px(x) + (int)(u >> 8) % T, sy = Py(y) + (int)(u >> 16) % T;
                byte v = (byte)(90 + (u >> 24) % 140);
                img.Set(sx, sy, new Rgba(v, v, (byte)Math.Min(255, v + 20)));
            }

        // ── 바닥 · 벽 · 문 ──
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                int variant = (int)(DotHash.U(x, y, 41) % DotTiles.Variants);
                switch (L.Kind[c])
                {
                    case CellKind.Floor:
                    {
                        var fl = L.Room[c] >= 0 ? _roomFloor[L.Room[c]] : DotFloor.Metal;
                        img.Draw(Cached(Key(1, (int)fl, variant), () => DotTiles.Floor(fl, variant)), Px(x), Py(y));
                        break;
                    }
                    case CellKind.Door:
                        img.Draw(Cached(Key(1, (int)DotFloor.Metal, variant), () => DotTiles.Floor(DotFloor.Metal, variant)), Px(x), Py(y));
                        break;
                    case CellKind.Wall:
                    {
                        if (F(f, c).hole >= 2f) break; // 통째로 뚫린 벽 — 우주가 보인다
                        var wk = WallOf(c);
                        int mask = (Solid(x, y - 1) ? 1 : 0) | (Solid(x + 1, y) ? 2 : 0) | (Solid(x, y + 1) ? 4 : 0) | (Solid(x - 1, y) ? 8 : 0);
                        img.Draw(Cached(Key(2, (int)wk, mask), () => DotTiles.WallTop(wk, mask)), Px(x), Py(y));
                        break;
                    }
                }
            }
        // 벽 남쪽 앞면: 아래 칸이 바닥이면 그 칸 위쪽에
        for (int y = y0 - 1; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (KindAt(x, y) != CellKind.Wall || KindAt(x, y + 1) != CellKind.Floor) continue;
                int c = L.Index(x, y);
                if (F(f, c).hole >= 2f) continue;
                var wk = WallOf(c);
                bool openW = !Solid(x - 1, y) || KindAt(x - 1, y + 1) != CellKind.Floor;
                bool openE = !Solid(x + 1, y) || KindAt(x + 1, y + 1) != CellKind.Floor;
                bool lamp = DotHash.U(x, y, 5) % 7 == 0;
                img.Draw(Cached(Key(3, (int)wk, openW ? 1 : 0, openE ? 1 : 0, lamp ? 1 : 0), () => DotTiles.WallFace(wk, openW, openE, lamp)), Px(x), Py(y + 1));
            }
        // 벽에 묻은 그을음 (② — 불 곁의 벽이 검게): 윗면 · 남쪽 앞면
        for (int y = y0 - 1; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (KindAt(x, y) != CellKind.Wall) continue;
                int c = L.Index(x, y);
                double sg = f.SootGPerM2(c);
                if (sg <= 0.05 || F(f, c).hole >= 2f) continue;
                if (y >= y0) SootStain(img, Px(x), Py(y), T, T, sg, c);
                if (KindAt(x, y + 1) == CellKind.Floor) SootStain(img, Px(x), Py(y + 1), T, DotTiles.Face, sg, c + 7);
            }
        foreach (var d in L.Doors)
        {
            int x = L.X(d.Cell), y = L.Y(d.Cell);
            if (x < x0 || y < y0 || x >= x0 + w || y >= y0 + h) continue;
            float open = f.DoorOpen[d.Id];
            int step = Math.Clamp((int)MathF.Round(open * 6f), 0, 6);
            int kind = d.External ? 1 : d.Kind == "normal" ? 0 : 2;
            bool jam = st != null && st.DoorJam[d.Id] != 0;
            int lightKind = st != null && st.DoorPowered[d.Id] == 0 ? 2 : jam ? 3 : open > 0.05f ? 1 : 0;
            img.Draw(Cached(Key(4, d.Vertical ? 1 : 0, step, kind, lightKind), () => DotTiles.Door(d.Vertical, step, kind, lightKind)), Px(x), Py(y));
            if (f.DoorBleed[d.Id] != 0) Hiss(img, Px(x) + T / 2, Py(y) + T / 2, frame); // 압력 맞춤 밸브
        }

        // ── 외판 구멍 · 금 ──
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] != CellKind.Wall || f.Hole[c] <= 0) continue;
                float a = f.Hole[c];
                if (a >= 2f) continue;
                float r = Math.Clamp(MathF.Sqrt(a / 0.01f) * 8f, 2f, 14f);
                float cx = Px(x) + T / 2 + (DotHash.F(c, 1) - 0.5f) * 8, cy = Py(y) + T / 2 + (DotHash.F(c, 2) - 0.5f) * 8;
                img.Oval(cx, cy, r + 2, r + 1.5f, Rgba.Hex("#5a6576"), 0.9f);
                img.Oval(cx, cy, r, r * 0.8f, space);
                for (int k = 0; k < 4; k++) // 금
                {
                    float ang = DotHash.F(c, 10 + k) * 6.283f, len = r + 3 + DotHash.F(c, 20 + k) * 6;
                    for (float t = r; t < len; t += 0.7f) img.Set((int)(cx + MathF.Cos(ang) * t), (int)(cy + MathF.Sin(ang) * t), Rgba.Hex("#0c0f14"));
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
                if (inside > 5 && (ox != 0 || oy != 0))
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

        // ── 설비 (아래쪽 것이 앞) ──
        var order = Enumerable.Range(0, L.Furniture.Count).Where(i => L.Furniture[i].Cells.Length > 0)
            .OrderBy(i => _fixBox[i].y + _fixBox[i].h).ThenBy(i => i);
        foreach (int i in order)
        {
            var (bx, by, bw, bh) = _fixBox[i];
            if (bx + bw < x0 || by + bh < y0 || bx > x0 + w || by > y0 + h + 1) continue;
            var ft = _fixType[i] ?? FurnitureType.Shelf;
            int rise = DotMachines.RiseOf(ft);
            var body = Cached(Key(5, (int)ft, bw, bh, i), () => Shadowed(DotMachines.Body(ft, bw, bh, -1, 1, 0, i), rise));
            int px = Px(bx), py = Py(by) - rise;
            img.Draw(body, px, py);
            var dev = DeviceOf(net, i);
            var state = dev < 0 ? FixState.On : net!.IsBroken(dev, Part.Electric) || net.IsBroken(dev, Part.Mechanical) ? FixState.Broken : net.Working(dev) ? FixState.On : FixState.Off;
            if (state != FixState.Off) img.Draw(DotMachines.Life(ft, bw, bh, state, frame & 7, 1f), px, py);
            else img.Fill(px, py, bw * T, bh * T + rise, Rgba.Hex("#000308"), 0.3f);
        }

        // ── 바닥에 놓인 · 날아온 물건 (④ — 문틀에 걸린 것은 문 위에) ──
        if (mind != null)
        {
            var count = new Dictionary<int, int>();
            foreach (var it in mind.Loose)
            {
                int c = it.Cell;
                if (c < 0) continue;
                int x = L.X(c), y = L.Y(c);
                if (x < x0 || y < y0 || x >= x0 + w || y >= y0 + h) continue;
                int n = count.GetValueOrDefault(c); count[c] = n + 1;
                var icon = Cached(Key(9, it.Item), () => LooseIcon(mind.ItemNames[it.Item]));
                img.Draw(icon, Px(x) + T / 2 - icon.W / 2 + (n % 3 - 1) * 7, Py(y) + T / 2 - icon.H / 2 + n / 3 * 6);
            }
        }

        // ── 칸 덮개: 물 · 진공 · 열 · 어둠 ──
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] is not (CellKind.Floor or CellKind.Door)) continue;
                double sootG = f.SootGPerM2(c);
                if (sootG > 0.05) SootStain(img, Px(x), Py(y), T, T, sootG, c); // 바닥에 앉은 그을음
                double water = f.Stored(f.T.sWater, c);
                if (water > 2) img.Draw(Cached(Key(6, frame & 3, c & 7), () => DotFx.WaterTile(frame & 3, c & 7)), Px(x), Py(y), false, (float)Math.Min(0.85, water / 60));
                double kpa = f.Pressure(c);
                if (kpa < 40) img.Fill(Px(x), Py(y), T, T, Rgba.Hex("#8fd0ff"), (float)(0.36 * (1 - kpa / 40))); // 진공: 서리
                double temp = f.Temp(c);
                if (temp > 330 && kpa > 5) img.Fill(Px(x), Py(y), T, T, Rgba.Hex("#ff5a1e"), (float)Math.Min(0.35, (temp - 330) / 400));
                float dark = 0;
                if (light != null) { float lux = light.Lux[c]; dark = lux >= 60 ? 0 : (float)Math.Min(0.72, 0.72 * (1 - lux / 60)); }
                if (dark > 0.02f) img.Fill(Px(x), Py(y), T, T, Rgba.Hex("#02040a"), dark);
            }

        // ── 사람 · 로봇 (아래쪽이 앞) ──
        if (mind != null && life != null)
        {
            foreach (var a in mind.Agents.OrderBy(a => L.Y(a.C.Cell)).ThenBy(a => a.Id))
            {
                int cell = a.C.Cell;
                int x = L.X(cell), y = L.Y(cell);
                if (x < x0 - 1 || y < y0 - 1 || x > x0 + w || y > y0 + h + 1) continue;
                var (nx, ny) = Nudge?.Invoke(a) ?? (0, 0);
                if (a.IsPerson) DrawPerson(img, mind, life, a, Px(x) + nx, Py(y) + ny, frame);
                else DrawBot(img, a, Px(x) + nx, Py(y) + ny, frame);
            }
        }

        // ── 불 · 연기 (맨 앞) ──
        for (int y = y0; y < y0 + h + 1; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (chem != null && chem.Burning[c] != 0)
                {
                    float power = Math.Clamp(chem.Hrr[c] / 40f, 0.25f, 1f);
                    img.Draw(DotFx.FireTile(frame + c, power, c), Px(x), Py(y) + T - 40);
                }
                double soot = f.Ppm(f.T.gSoot, c);
                if (soot > 20)
                {
                    float al = (float)Math.Min(0.8, soot / 500);
                    var puff = Cached(Key(7, c & 15), () => DotFx.SmokeSprite(T + 12, c & 15));
                    int dx = (int)((DotHash.F(c, frame / 4) - 0.5f) * 6);
                    img.Draw(puff, Px(x) - 6 + dx, Py(y) - 10, false, al);
                }
            }
        return img;
    }

    private static (float hole, int _) F(Field f, int c) => (f.Hole[c], 0);

    /// <summary>바닥 물건 그림 (작은 도트 — 무게만큼 크게): 상자 · 꾸러미 · 소화기 · 우주복 · 원료 덩이.</summary>
    private static Img LooseIcon(string name)
    {
        var (w, h, body, edge) = name switch
        {
            "식량 상자" => (16, 11, "#9a7346", "#5d4428"),
            "원료" => (13, 10, "#7d838c", "#4a4f57"),
            "우주복" => (14, 14, "#e6e8ec", "#8f96a3"),
            "소화기" => (7, 13, "#d13b2f", "#7a1d16"),
            "수리 꾸러미" => (11, 8, "#5b6f8f", "#2f3b4f"),
            "봉합 꾸러미" => (9, 6, "#e0b33a", "#8a6a17"),
            "구급 상자" => (10, 8, "#f2f2f2", "#9aa0a8"),
            "산소 마스크" => (9, 7, "#9ad0e8", "#4f7d91"),
            "수확물" => (9, 7, "#6fae4b", "#3d6b26"),
            _ => (9, 7, "#a0a6ae", "#5a6068"),
        };
        var o = new Img(w + 2, h + 2);
        o.Fill(1, 2, w, h, Rgba.Hex("#000000"), 0.35f);                  // 그림자
        o.Fill(0, 0, w, h, Rgba.Hex(edge));
        o.Fill(1, 1, w - 2, h - 2, Rgba.Hex(body));
        o.Fill(1, 1, w - 2, 1, Rgba.Hex("#ffffff"), 0.25f);              // 윗면 빛
        if (name == "구급 상자") { o.Fill(w / 2 - 1, 2, 2, h - 4, Rgba.Hex("#d13b2f")); o.Fill(2, h / 2 - 1, w - 4, 2, Rgba.Hex("#d13b2f")); }
        if (name == "식량 상자") o.Fill(1, h / 2, w - 2, 1, Rgba.Hex(edge));
        return o;
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
        _looks[a.Id] = l;
        return l;
    }

    private static DotFacing FacingOf(Layout L, Agent a)
    {
        if (a.PathIdx < a.Path.Count)
        {
            int n = a.Path[a.PathIdx];
            int dx = L.X(n) - L.X(a.C.Cell), dy = L.Y(n) - L.Y(a.C.Cell);
            if (dx != 0) return dx > 0 ? DotFacing.E : DotFacing.W;
            if (dy != 0) return dy > 0 ? DotFacing.S : DotFacing.N;
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
        l.Held = c.Holding == 255 ? DotHeld.None : mind.ItemNames[c.Holding] switch
        {
            "소화기" => DotHeld.Extinguisher,
            "수리 꾸러미" => DotHeld.Wrench,
            "봉합 꾸러미" => DotHeld.Box,
            "구급 상자" => DotHeld.MedKit,
            "수확물" => DotHeld.Plate,
            "식량 상자" => DotHeld.Box,
            _ => DotHeld.None,
        };
        if (lying)
        {
            // 누운 사람: 옆으로 돌린 그림 (머리가 왼쪽)
            var lie = DotPeople.Draw(l, DotFacing.S, DotPose.Stand, 0).Rotate(cw: false).ToImg();
            img.Draw(lie, px + T / 2 - lie.W / 2, py + T / 2 - lie.H / 2);
            return;
        }
        var step = c.StepIndex < a.Plan.Count ? a.Plan[c.StepIndex].Kind : StepKind.None;
        bool moving = a.PathIdx < a.Path.Count && step == StepKind.GoTo;
        DotPose pose = c.Mode == 2 ? DotPose.Cower
            : c.Carrying >= 0 ? DotPose.Carry
            : moving ? DotPose.Walk
            : step is StepKind.Spray or StepKind.Seal or StepKind.Fix or StepKind.Brace or StepKind.Craft or StepKind.Maintain or StepKind.Clean ? DotPose.Work
            : step == StepKind.Use && c.Goal is GoalKind.Eat or GoalKind.Relax or GoalKind.Socialize or GoalKind.Work ? DotPose.Sit
            : DotPose.Stand;
        var face = FacingOf(L, a);
        var dot = DotPeople.Draw(l, face, pose, moving ? (frame + a.Id) & 3 : 0).ToImg();
        int ox = px + T / 2 - DotPeople.Cx, oy = py + T - DotPeople.Foot - 2;
        img.Draw(dot, ox, oy);
        if (c.Carrying >= 0)
        {
            // 업은 사람: 누운 몸을 어깨 위에
            int vb = c.Carrying;
            var va = mind.Agents.FirstOrDefault(o => o.IsPerson && o.C.Body == vb);
            if (va != null)
            {
                var vl = LookOf(mind, va);
                vl.EyesClosed = true; vl.Dead = life.Bodies[vb].Alive == 0; vl.Suit = life.Bodies[vb].Suit != 0;
                var lie = DotPeople.Draw(vl, DotFacing.S, DotPose.Stand, 0).Rotate(cw: false).ToImg();
                img.Draw(lie, px + T / 2 - lie.W / 2, oy + 6);
            }
        }
        if (c.Mode == 2) Mark(img, px + T / 2, oy - 4, Rgba.Hex("#ffd146")); // 공황: 머리 위 느낌표
    }

    private static void Mark(Img img, int cx, int top, Rgba col)
    {
        img.Fill(cx - 1, top, 2, 5, col);
        img.Fill(cx - 1, top + 6, 2, 2, col);
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
            var dr = Cached(Key(9, (int)state, frame & 3, a.Id & 3), () => DotBots.Drone(DotDroneKind.Repair, state, frame & 3));
            img.Draw(dr, px + T / 2 - dr.W / 2, py + T / 2 - dr.H / 2 - 8);
            return;
        }
        var kind = (DotBotKind)(a.Id % 4);
        var bot = Cached(Key(8, (int)kind, (int)state, frame & 3, a.Id & 7), () => DotBots.Robot(kind, state, frame & 3, a.Id));
        img.Draw(bot, px + T / 2 - DotBots.Cx, py + T - DotBots.Foot - 1);
    }
}
