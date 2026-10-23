using ShipSim.Art;

namespace ShipView;

/// <summary>바닥 마감 (고정 꾸밈 — 방 종류로 고른다 · 세계의 재질은 일곱 척 모두 강철이다): docs/GRAPHICS.md 텍스처 T4.</summary>
public enum Finish : byte { Metal, Corridor, Cargo, Grate, Rubber, TileWarm, TileCool, CarpetBrick, CarpetOlive, CarpetTeal, Soil, Diamond }

/// <summary>
/// 새 바닥 (T4): 칸 32도트 · 모두 2×2칸 큰 조각 + 8칸 주기의 낮은 주파 얼룩 ±3% → 되풀이 주기가 8칸 넘게 (칸 자리 x · y의 8 나머지로 굽는다).
/// 금속판 (64×32 판을 벽돌처럼 엇갈려 · 볼트 · 판마다 ±1단) · 통로 (가장자리 노랑 선) · 화물칸 (마름모 철판 + 노랑 모서리 표시) · 격자 (살 2 · 간격 6 · 들보 · 아래로 비치는 관) ·
/// 고무 (64도트 매트 · 은은한 마름모 돌기) · 타일 (16도트 · 줄눈 1 · 타일마다 ±1단 · 배수구) · 카펫 (디더 결 · 방 둘레 테두리 무늬) · 흙 매트 (배수 구멍 · 흙 알갱이 · 이음의 이끼) · 미끄럼 막이 (8도트 마름모 · 둘레 사선 띠).
/// 색은 낮은 채도 · 값 위계 (바닥 45~120 — 사람 120~230이 먼저 보이게).
/// </summary>
public static class ArtFloors
{
    public const int T = 32;
    private static Rgba H(string s) => Rgba.Hex(s);

    public static Finish FinishFor(string type) => type switch
    {
        "Quarters" or "PrivateCabins" or "QuietQuarters" or "WaterWallCabin" => Finish.CarpetBrick,
        "Lounge" or "Theater" or "MeetingRoom" or "Archive" => Finish.CarpetOlive,
        "Chapel" or "Meditation" or "School" or "Observatory" => Finish.CarpetTeal,
        "Galley" or "Mess" or "Washroom" or "Laundry" or "WaterPlant" => Finish.TileWarm,
        "Medbay" or "Triage" or "Morgue" or "Lab" or "Decon" or "Quarantine" or "QuarantineLock" or "Freezer" or "Hyperbaric" or "SeedVault" => Finish.TileCool,
        "Hydroponics" or "AlgaeLab" or "ProteinFarm" or "MushroomFarm" => Finish.Soil,
        "Bridge" or "BackupBridge" or "Comms" or "Navigation" or "ComputerRoom" or "ServerRoom" or "ElectronicsLab" or "Power" or "Substation" or "BatteryRoom" or "FuelCell" or "Calibration" or "CraneControl" or "Gym" => Finish.Rubber,
        "Engine" or "Reactor" => Finish.Diamond,
        "Cooling" or "PumpRoom" or "HvacRoom" or "LifeSupport" or "Recycling" or "GasStorage" or "PropellantTank" or "HeatStorage" or "Crusher" => Finish.Grate,
        "Corridor" => Finish.Corridor,
        "Cargo" or "Hangar" or "OreHold" or "DroneBay" => Finish.Cargo,
        _ => Finish.Metal,
    };

    // 재질마다 색 단 (0 가장 어둠)
    private static readonly Rgba[] Steel = { H("#22262e"), H("#2c3139"), H("#363c46"), H("#3f4651"), H("#49505c"), H("#58606d"), H("#6c7583") };
    private static readonly Rgba[] Pit = { H("#07080b"), H("#0c0e12"), H("#121519") };
    private static readonly Rgba[] Bar = { H("#262b33"), H("#343a44"), H("#434a56"), H("#57606d") };
    private static readonly Rgba[] Rub = { H("#1a1e1d"), H("#212625"), H("#282e2d"), H("#303736"), H("#3a4241") };
    private static readonly Rgba[] Warm = { H("#5d574c"), H("#78715f"), H("#8c8572"), H("#9a927e"), H("#a59d89"), H("#b3ab97") };
    private static readonly Rgba[] Cool = { H("#525b60"), H("#6b767c"), H("#7d898f"), H("#8a969c"), H("#95a1a7"), H("#a5b0b5") };
    private static readonly Rgba[] Brick = { H("#3a221f"), H("#4a2c27"), H("#56332d"), H("#623b33"), H("#70463c") };
    private static readonly Rgba[] Olive = { H("#2f3122"), H("#3a3d2a"), H("#454932"), H("#51563b"), H("#5e6446") };
    private static readonly Rgba[] Teal = { H("#1e3233"), H("#253d3e"), H("#2d4849"), H("#355455"), H("#406264") };
    private static readonly Rgba[] Mat = { H("#1d1a15"), H("#25211a"), H("#2e2920"), H("#383126") };
    private static readonly Rgba[] Dirt = { H("#3b2c1e"), H("#4a3826"), H("#5b452f") };
    private static readonly Rgba Moss = H("#3d5a2e"), MossLight = H("#557a3e"), Line = H("#8a7638"), LineDark = H("#5f5226"), Hazard = H("#9a7f2a"), HazardDark = H("#26221a");

    /// <summary>
    /// 바닥 한 칸. cx · cy: 칸 자리 (8 나머지로 무늬가 이어진다) · edge: 방 둘레 (1 북 · 2 동 · 4 남 · 8 서 — 그쪽이 같은 방 바닥이 아니다) · drain: 이 칸에 배수구 (방마다 하나).
    /// </summary>
    public static Img Floor(Finish f, int cx, int cy, int edge, bool drain)
    {
        var o = new Img(T, T);
        int gx0 = (cx & 7) * T, gy0 = (cy & 7) * T;
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                int gx = gx0 + x, gy = gy0 + y;
                o.Set(x, y, Dot(f, gx, gy, x, y, edge));
            }
        Details(o, f, cx, cy, edge, drain);
        // 낮은 주파 얼룩 ±3% (256도트 주기 — 8칸)
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float k = 1 + 0.03f * (Blotch(gx0 + x, gy0 + y) * 2 - 1);
                o.Set(x, y, o.Get(x, y).Scale(k));
            }
        return o;
    }

    /// <summary>문 칸 바닥: 금속판 + 통로를 가로지르는 4도트 문턱 (격벽 · 바깥 문은 사선 띠). across: 지나는 쪽이 위아래 (가로 벽의 문).</summary>
    public static Img Threshold(bool across, int kind)
    {
        var o = Floor(Finish.Metal, 3, 3, 0, false);
        for (int i = 0; i < T; i++)
            for (int k = 0; k < 4; k++)
            {
                int x = across ? i : 14 + k, y = across ? 14 + k : i;
                var col = k == 0 ? Steel[6] : k == 3 ? Steel[0] : Steel[3];
                if (kind != 0 && k is 1 or 2) col = ((i / 3) & 1) == 0 ? Hazard : HazardDark;
                o.Set(x, y, col);
            }
        return o;
    }

    private static Rgba Dot(Finish f, int gx, int gy, int x, int y, int edge)
    {
        switch (f)
        {
            case Finish.Grate:
            {
                bool beam = gx % 64 < 3 || gy % 64 < 3;
                if (beam) return gx % 64 == 0 || gy % 64 == 0 ? Bar[3] : Bar[1];
                bool bar = gx % 8 < 2, cross = gy % 16 == 8;
                if (bar) return gx % 8 == 0 ? Bar[2] : Bar[0];
                if (cross) return Bar[1];
                // 구덩이 아래로 곧게 지나는 관 (128도트마다 · 비친다)
                int py = gy % 128;
                if (py is >= 40 and < 47) return py == 40 ? H("#2e3640") : py == 46 ? Pit[0] : H("#1d232b");
                return Pit[(gx + gy) % 5 == 0 ? 2 : 1];
            }
            case Finish.Rubber:
            {
                if (gx % 64 == 0) return Rub[0];
                if (gx % 64 == 1) return Rub[3];
                int sx = gx % 6, sy = gy % 6, row = gy / 6;
                int ox = (row & 1) * 3;
                if ((gx + ox) % 6 == 2 && sy == 2) return Rub[3];
                if ((gx + ox) % 6 == 3 && sy == 3) return Rub[1];
                return Rub[2];
            }
            case Finish.TileWarm or Finish.TileCool:
            {
                var c = f == Finish.TileWarm ? Warm : Cool;
                if (gx % 16 == 0 || gy % 16 == 0) return c[0];
                if (gx % 16 == 1 || gy % 16 == 1) return c[4];
                int t = (int)(DotHash.U(gx / 16, gy / 16, 17) % 5);   // 타일마다 은은하게 (±4%)
                return t == 0 ? c[2].Scale(0.96f) : t == 4 ? c[2].Scale(1.04f) : c[2];
            }
            case Finish.CarpetBrick or Finish.CarpetOlive or Finish.CarpetTeal:
            {
                var c = f == Finish.CarpetBrick ? Brick : f == Finish.CarpetOlive ? Olive : Teal;
                // 방 둘레에서 3도트 안쪽 2도트 테두리 무늬
                int d = 99;
                if ((edge & 1) != 0) d = Math.Min(d, y); if ((edge & 4) != 0) d = Math.Min(d, T - 1 - y);
                if ((edge & 8) != 0) d = Math.Min(d, x); if ((edge & 2) != 0) d = Math.Min(d, T - 1 - x);
                if (d is 3 or 4) return ((gx + gy) & 3) == 0 ? c[2] : c[4];
                if (d < 3) return c[1];
                bool weave = ((gx + gy * 3) & 3) == 0 || ((gx * 3 - gy) & 7) == 0;
                return weave ? c[3] : DotHash.F(gx, gy, 29) < 0.04f ? c[1] : c[2];
            }
            case Finish.Soil:
            {
                if (gx % 32 == 0 || gy % 32 == 0) return Mat[0];
                if (gx % 8 == 4 && gy % 8 == 4) return Pit[1];   // 배수 구멍
                return (gx / 2 + gy / 2) % 3 == 0 ? Mat[2] : Mat[1];
            }
            case Finish.Diamond:
            {
                int sx = gx % 8, sy = gy % 8;
                if ((sx == 3 && sy == 2) || (sx == 4 && sy == 3)) return Steel[5];
                if ((sx == 4 && sy == 4) || (sx == 3 && sy == 5)) return Steel[1];
                return Steel[3];
            }
            case Finish.Cargo:
            {
                int sx = gx % 8, sy = gy % 8, row = gy / 8;
                int ox = (row & 1) * 4;
                if ((gx + ox) % 8 == 3 && sy == 3) return Steel[5];
                if ((gx + ox) % 8 == 4 && sy == 4) return Steel[1];
                if (gx % 64 == 0 || gy % 64 == 0) return Steel[0];
                return Steel[2];
            }
            default:   // 금속판 · 통로: 64×32 판을 벽돌처럼 (줄마다 32도트 어긋남)
            {
                int row = gy / 32, off = (row & 1) * 32;
                int px = (gx + off) % 64, py = gy % 32, col = (gx + off) / 64;
                if (py == 0 || px == 0) return Steel[0];
                if (py == 1 || px == 1) return Steel[5];
                int tone = (int)(DotHash.U(col, row, 23) % 3);
                if ((px == 4 || px == 60) && (py == 4 || py == 28)) return Steel[6];   // 볼트
                if ((px == 5 || px == 61) && (py == 5 || py == 29)) return Steel[1];
                return Steel[2 + tone];
            }
        }
    }

    /// <summary>고정 꾸밈: 통로 노랑 선 · 화물칸 모서리 표시 · 흙 알갱이 · 이끼 · 금속 긁힘 · 미끄럼 막이 둘레 띠 · 배수구.</summary>
    private static void Details(Img o, Finish f, int cx, int cy, int edge, bool drain)
    {
        uint u = DotHash.U(cx, cy, 31);
        switch (f)
        {
            case Finish.Corridor:
                // 벽 쪽 가장자리 노랑 선 (2도트 · 벽에서 3도트)
                for (int i = 0; i < T; i++)
                {
                    if ((edge & 1) != 0) { o.Set(i, 11, Line); o.Set(i, 12, LineDark); }   // 북쪽은 벽 앞면 아래로 (10도트 가림)
                    if ((edge & 4) != 0) { o.Set(i, T - 5, Line); o.Set(i, T - 4, LineDark); }
                    if ((edge & 8) != 0) { o.Set(3, i, Line); o.Set(4, i, LineDark); }
                    if ((edge & 2) != 0) { o.Set(T - 5, i, Line); o.Set(T - 4, i, LineDark); }
                }
                goto case Finish.Metal;
            case Finish.Metal:
                for (int k = 0; k < (int)(u % 3); k++)   // 긁힘 (드물게)
                {
                    uint v = DotHash.U(cx, cy, 37 + k);
                    int x = 3 + (int)(v % (T - 10)), y = 3 + (int)((v >> 8) % (T - 8)), len = 2 + (int)((v >> 16) % 4);
                    for (int j = 0; j < len; j++) o.Set(x + j, y + (j >> 1), Steel[(v >> 24) % 2 == 0 ? 5 : 1]);
                }
                break;
            case Finish.Cargo:
                if ((cx & 1) == 0 && (cy & 1) == 0)   // 2×2칸마다 짐 자리 모서리 표시 (노랑 꺾쇠)
                    foreach (var (ax, ay, dx, dy) in new[] { (2, 2, 1, 1) })
                        for (int j = 0; j < 6; j++) { o.Set(ax + j * dx, ay, Hazard); o.Set(ax, ay + j * dy, Hazard); }
                break;
            case Finish.Soil:
                for (int k = 0; k < 10; k++)   // 흙 알갱이 무리
                {
                    uint v = DotHash.U(cx, cy, 41 + k);
                    int x = 1 + (int)(v % (T - 3)), y = 1 + (int)((v >> 8) % (T - 3));
                    o.Set(x, y, Dirt[(v >> 16) % 3]); if ((v >> 20) % 2 == 0) o.Set(x + 1, y, Dirt[1]);
                }
                for (int i = 0; i < T; i += 1)   // 이음의 이끼
                {
                    if (DotHash.F(cx * 97 + i, cy, 43) < 0.18f) o.Set(i, 1, Moss);
                    if (DotHash.F(cx, cy * 97 + i, 47) < 0.18f) o.Set(1, i, MossLight);
                }
                break;
            case Finish.Diamond:
                for (int i = 0; i < T; i++)   // 방 둘레 사선 띠 (낮춘 노랑 · 검정)
                    for (int k = 0; k < 3; k++)
                    {
                        Rgba Hz(int a, int b) => ((a + b) / 3 & 1) == 0 ? Hazard : HazardDark;
                        if ((edge & 1) != 0) o.Set(i, 11 + k, Hz(i, k));
                        if ((edge & 4) != 0) o.Set(i, T - 3 + k, Hz(i, k));
                        if ((edge & 8) != 0) o.Set(k, i, Hz(i, k));
                        if ((edge & 2) != 0) o.Set(T - 3 + k, i, Hz(i, k));
                    }
                break;
        }
        if (drain)
        {
            var dark = H("#3a3e44"); var slot = H("#25282d"); var rim = H("#9aa3ab");
            for (int y = 12; y < 20; y++) for (int x = 12; x < 20; x++) o.Set(x, y, (y & 1) == 0 ? slot : dark);
            for (int i = 12; i < 20; i++) { o.Set(i, 11, rim); o.Set(11, i, rim); o.Set(i, 20, slot); o.Set(20, i, slot); }
        }
    }

    /// <summary>낮은 주파 값 (0~1 · 256도트마다 되풀이): 64도트 격자 해시를 겹선형으로 잇는다.</summary>
    private static float Blotch(int gx, int gy)
    {
        float fx = gx / 64f, fy = gy / 64f;
        int ix = (int)fx, iy = (int)fy;
        float tx = fx - ix, ty = fy - iy;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float N(int a, int b) => DotHash.F(a & 3, b & 3, 53);
        float a0 = N(ix, iy) + (N(ix + 1, iy) - N(ix, iy)) * tx;
        float a1 = N(ix, iy + 1) + (N(ix + 1, iy + 1) - N(ix, iy + 1)) * tx;
        return a0 + (a1 - a0) * ty;
    }
}
