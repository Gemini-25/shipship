using ShipEngine;
using ShipSim.Art;
using ShipView;
using ShipSim.Core;

namespace Lab;

/// <summary>텍스처 규칙 (docs/GRAPHICS.md 텍스처 — 함장 요청 10-23): 벽 앞면 24 · 벽 너머 빛 없음 · 닫힌 문은 서리가 아니다 · 선체 모서리 사선 · 선반 채움 · 담요 주인 색 · 바닥 되풀이 · 같은 세계 같은 그림. 그림은 세계를 읽기만.</summary>
public static partial class Program
{
    private const string ArtShip = @"
###########
#.........#
#.h.......#
#.........#
#####D#####
#.........#
#..e......#
#.........#
###########";

    private static void ArtRules()
    {
        if (!Do("텍스처")) return;
        Console.WriteLine("\n[텍스처 — 벽 · 문 · 선체 · 바닥 · 가구 (그림은 세계를 읽기만)]");
        var lay = Worlds.Ascii(ArtShip, new Dictionary<char, string> { ['h'] = "Shelf", ['e'] = "Bed" });
        int At(int x, int y) => Worlds.At(lay, x, y);
        const int T = Painter.T;
        static double Luma(Rgba c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        // 도면 글자 자리 (ax, ay) → 그림 칸 (도면은 둘레 한 칸을 더 둔다 — Worlds.At과 같다)
        double Mean(Img im, int ax, int ay, int r0, int r1, Func<Rgba, double> f, int c0 = 2, int c1 = T - 2)
        { double s = 0; int n = 0; for (int y = r0; y < r1; y++) for (int x = c0; x < c1; x++) { s += f(im.Get((ax + 1) * T + x, (ay + 1) * T + y)); n++; } return s / n; }
        Rgba Px(Img im, int ax, int ay, int x, int y) => im.Get((ax + 1) * T + x, (ay + 1) * T + y);

        // 1 · 벽 앞면 24: 벽 칸 위 18줄은 어두운 뚜껑 · 그 아래 14줄 + 바닥 칸 위 10줄이 앞면 (모서리 빛 · 방 종류 띠 · 굽도리)
        {
            var s = Worlds.FieldOnly(lay, 1);
            var p = new Painter(lay) { Lighting = false };
            var img = p.Frame(s, 0, 0, lay.W, lay.H, 0);
            double cap = Mean(img, 2, 4, 3, 15, Luma), face = Mean(img, 2, 4, 21, 31, Luma), below = Mean(img, 2, 5, 0, 8, Luma), floor = Mean(img, 2, 5, 14, 30, Luma);
            var lip = Px(img, 2, 4, 10, ArtWalls.CapH);
            Check("텍스처 벽: 앞면이 24도트로 선다 — 벽 칸 위 18줄은 어두운 뚜껑 · 아래 14줄과 바닥 칸 위 10줄이 앞면 (모서리 빛줄 · 굽도리) · 그 아래는 바닥",
                face > cap + 15 && Luma(lip) > face + 20 && Math.Abs(below - floor) > 3,
                $"뚜껑 밝기 {cap:0} · 앞면 {face:0} · 모서리 빛 {Luma(lip):0} · 바닥 칸 위 10줄 {below:0} / 바닥 {floor:0}");
        }
        // 2 · 벽 너머 빛 없음: 북쪽 방만 켜면 가로 벽 앞면 (남쪽을 본다)은 꺼진 남쪽 방만큼 어둡다 · 남쪽 방을 켜면 밝아진다
        {
            var s = Worlds.Full(lay, 3);
            var light = s.Get<Light>();
            int north = lay.Room[At(3, 2)], south = lay.Room[At(3, 6)];
            var p = new Painter(lay);
            double FaceLit(float n, float so)
            {
                light.Lamp[north] = n; light.Lamp[south] = so;
                var img = p.Frame(s, 0, 0, lay.W, lay.H, 0);
                return Mean(img, 2, 4, ArtWalls.CapH + 2, T - 1, Luma);
            }
            double dark = FaceLit(0, 0), northOnly = FaceLit(300, 0), southOnly = FaceLit(0, 300);
            Check("텍스처 빛: 벽 앞면은 마주한 남쪽 방의 빛을 받는다 — 북쪽 방만 켜도 앞면은 어두운 채 (벽 너머로 빛이 새지 않는다) · 남쪽 방을 켜면 밝다",
                northOnly <= dark * 1.15 + 3 && southOnly > dark * 1.8,
                $"둘 다 끔 {dark:0} · 북쪽만 {northOnly:0} · 남쪽만 {southOnly:0}");
        }
        // 3 · 닫힌 문은 서리가 아니다 (닫힌 문 칸은 기체 칸이 아니라 기압 0으로 읽혀 늘 서리 빛이었다 — 하늘색 문) · 열면 그 칸 기압대로
        {
            var s = Worlds.FieldOnly(lay, 1, f => f.SetDoor(0, 0f));
            var p = new Painter(lay) { Lighting = false };
            int dx = 5, dy = 4;   // 도면 자리 (그림은 Mean이 한 칸 옮긴다)
            var img = p.Frame(s, 0, 0, lay.W, lay.H, 0);
            double blue = Mean(img, dx, dy, ArtWalls.CapH + 4, T, c => c.B - (c.R + c.G) / 2.0, 6, T - 6);
            Check("텍스처 문: 닫힌 문 칸에 진공 서리 빛을 덮지 않는다 (예전엔 늘 하늘색 문)", blue < 28, $"문짝 푸른 기 {blue:0}");
        }
        // 4 · 선체 볼록 모서리는 사선으로 깎인다 (깎인 자리는 우주) · 아래가 우주면 바깥 면
        {
            var s = Worlds.FieldOnly(lay, 1);
            var p = new Painter(lay) { Lighting = false };
            var img = p.Frame(s, 0, 0, lay.W, lay.H, 0);
            double cut = Luma(Px(img, 0, 0, 2, 2)), inner = Luma(Px(img, 0, 0, 24, 24));
            double skin = Mean(img, 4, 9, 1, ArtWalls.SkinH - 1, Luma), space = Mean(img, 4, 9, ArtWalls.SkinH + 4, T - 2, Luma);
            Check("텍스처 선체: 볼록 모서리는 사선으로 깎이고 (깎인 자리는 우주) · 아래가 우주인 외판 밑엔 바깥 면이 보인다",
                cut < 12 && inner > cut + 10 && skin > space + 5, $"깎인 자리 밝기 {cut:0} · 안쪽 {inner:0} · 바깥 면 {skin:0} / 우주 {space:0} (선체 칸 {lay.Hull[At(0, 0)]})");
        }
        // 5 · 선반 채움 (세계 값 Mind.Stock): 소화기가 있으면 빨간 통 · 꺼내면 그 칸이 빈다 / 담요 = 주인의 색
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>();
            int shelf = lay.FurnitureAt[At(2, 2)], iExt = Array.IndexOf(m.ItemNames, "소화기");
            var lt = s.Get<Light>();
            for (int r = 0; r < lay.Rooms.Count; r++) lt.Lamp[r] = 300;   // 전기 없는 시험 배 — 화면 조명만 켠다 (그림은 세계를 읽기만)
            var p = new Painter(lay);
            int Red()
            {
                var img = p.Frame(s, 0, 0, lay.W, lay.H, 0);
                int n = 0;
                for (int y = 2 * T; y < 4 * T; y++) for (int x = 3 * T; x < 4 * T; x++) { var c = img.Get(x, y); if (c.R > 150 && c.G < 80 && c.B < 70) n++; }
                return n;
            }
            m.Stock[shelf * m.ItemCount + iExt] = 1; int full = Red();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") { var dbg = p.Frame(s, 0, 0, lay.W, lay.H, 0); Png.WriteRgba("build/shots/rule-art.png", dbg.W, dbg.H, dbg.Px); Console.WriteLine($"    선반 {shelf} ({lay.Furniture[shelf].Type}) 칸 {string.Join(",", lay.Furniture[shelf].Cells.Select(c => $"{lay.X(c)},{lay.Y(c)}"))} · 소화기 번호 {iExt}"); }
            m.Stock[shelf * m.ItemCount + iExt] = 0; int empty = Red();
            int bed = At(3, 6);
            int who = m.AddPerson(s, At(6, 6));
            Rgba Blanket() { var img = p.Frame(s, 0, 0, lay.W, lay.H, 0); return Px(img, 3, 6, 20, 22); }
            m[who].C.Bed = -1; var none = Blanket();
            m[who].C.Bed = bed; var mine = Blanket();
            Check("텍스처 가구: 선반의 소화기는 세계의 물건 수대로 (있으면 빨간 통 · 꺼내면 빈 칸) · 침대 담요는 주인의 색 (주인이 없으면 회청)",
                full > 10 && empty == 0 && !none.Equals(mine), $"빨간 도트 {full} → 꺼낸 뒤 {empty} · 담요 주인 없음 {none} · 주인 {mine}");
        }
        // 6 · 바닥 되풀이: 같은 방 8×8칸에 다른 타일이 많다 (2×2칸 큰 조각 + 8칸 주기 얼룩 — 예전엔 칸마다 네 가지)
        {
            var big = Worlds.Ascii(@"
##################
#................#
#................#
#................#
#................#
#................#
#................#
#................#
#................#
##################");
            var s = Worlds.FieldOnly(big, 1);
            var p = new Painter(big) { Lighting = false };
            var img = p.Frame(s, 0, 0, big.W, big.H, 0);
            var seen = new HashSet<ulong>();
            for (int cy = 2; cy < 10; cy++) for (int cx = 1; cx < 9; cx++)
            {
                ulong h = 1469598103934665603UL;
                for (int y = 12; y < T; y++) for (int x = 0; x < T; x++) { var c = Px(img, cx, cy, x, y); h = (h ^ (uint)(c.R | c.G << 8 | c.B << 16)) * 1099511628211UL; }
                seen.Add(h);
            }
            Check("텍스처 바닥: 한 방 8×8칸에 서로 다른 바닥 칸이 많다 (되풀이 주기 8칸 — 예전엔 네 가지가 되풀이)", seen.Count >= 40, $"다른 칸 {seen.Count}/64");
        }
        // 8 · 빛 웅덩이 (T5): 웅덩이를 넣어도 방 평균은 세계 조명 그대로 (±3%) · 낮은 은은하게 · 등이 낮은 밤 · 비상등은 짙게 · 등이 꺼지면 웅덩이 없음
        {
            var big = Worlds.Ascii(@"
##################
#................#
#................#
#................#
#................#
#................#
#................#
#................#
#................#
##################");
            var s = Worlds.Full(big, 9);
            var lt = s.Get<Light>(); var net = s.Get<Network>();
            var p = new Painter(big);
            int room = big.Room[Worlds.At(big, 5, 5)];
            (double mean, double lo, double hi) Floor(float lamp, bool pools)
            {
                lt.Lamp[room] = lamp; Painter.NoPools = !pools;
                var img = p.Frame(s, 0, 0, big.W, big.H, 0);
                Painter.NoPools = false;
                var cellMeans = new List<double>(); double all = 0; int n = 0;
                for (int cy = 1; cy < 9; cy++) for (int cx = 1; cx < 17; cx++)   // 방 바닥 모두 (평균) · 벽 그늘 밖 안쪽 칸 (대비)
                {
                    double v = Mean(img, cx, cy, 0, T, Luma, 0, T);
                    all += v; n++;
                    if (cy >= 3 && cx >= 2 && cx < 16) cellMeans.Add(Mean(img, cx, cy, 10, 30, Luma));
                }
                return (all / n, cellMeans.Min(), cellMeans.Max());
            }
            var day = Floor(300, true); var dayFlat = Floor(300, false);
            var night = Floor(90, true); var nightFlat = Floor(90, false);
            var off = Floor(0, true); var offFlat = Floor(0, false);
            double keepDay = day.mean / dayFlat.mean, keepNight = night.mean / nightFlat.mean;
            double spreadDay = day.hi / day.lo, spreadNight = night.hi / night.lo, flat = dayFlat.hi / dayFlat.lo;
            Check("텍스처 빛 웅덩이: 등 자리에서 웅덩이가 지고 그 사이는 어둡다 — 방 평균은 세계 조명 그대로 (±3%) · 등이 낮은 밤은 낮보다 대비가 크다 · 등이 꺼지면 웅덩이 없음",
                Math.Abs(keepDay - 1) <= 0.03 && Math.Abs(keepNight - 1) <= 0.03 && spreadDay > flat * 1.2 && spreadNight > spreadDay * 1.15 && Math.Abs(off.mean - offFlat.mean) < 0.5,
                $"평균 유지 낮 {keepDay:0.000} · 밤 {keepNight:0.000} · 칸 밝기 최대/최소 고른 빛 {flat:0.00} · 낮 {spreadDay:0.00} · 밤 {spreadNight:0.00} · 꺼짐 {off.mean:0.0}/{offFlat.mean:0.0} · 등 자리 {p.PoolSpots.Count(q => q.room == room)}");
        }
        // 9 · 발밑 그림자 (T9): 선 사람 발밑에 그늘 타원 (바닥보다 어둡다)
        {
            var s = Worlds.Full(lay, 11);
            var m = s.Get<Mind>(); var lt = s.Get<Light>();
            for (int r = 0; r < lay.Rooms.Count; r++) lt.Lamp[r] = 300;
            int who = m.AddPerson(s, At(6, 2));
            var p = new Painter(lay);
            var img = p.Frame(s, 0, 0, lay.W, lay.H, 0);
            var cell = m[who].C.Cell;
            int ax = lay.X(cell) - 1, ay = lay.Y(cell) - 1;
            double feet = Luma(Px(img, ax, ay, 9, T - 3)), beside = Luma(Px(img, ax + 1, ay, 9, T - 3));
            Check("텍스처 사람: 선 사람 발밑엔 그늘 타원 (곁 바닥보다 어둡다 — 사람이 바닥에 선다)", feet < beside * 0.85, $"발밑 {feet:0} · 곁 바닥 {beside:0}");
        }
        // 10 · 앞 가림 (T9): 우주복 보관함 바로 북쪽에 선 사람은 보관함 윗부분에 가린다 (예전엔 사람이 늘 설비 위에)
        {
            var occ = Worlds.Ascii(@"
#########
#.......#
#.......#
#....L..#
#.......#
#########", new Dictionary<char, string> { ['L'] = "SuitLocker" });
            var s = Worlds.Full(occ, 13);
            var m = s.Get<Mind>(); var lt = s.Get<Light>();
            for (int r = 0; r < occ.Rooms.Count; r++) lt.Lamp[r] = 300;
            var p = new Painter(occ);
            var empty = p.Frame(s, 0, 0, occ.W, occ.H, 0); var e = new Img(empty.W, empty.H); Array.Copy(empty.Px, e.Px, e.Px.Length);
            int who = m.AddPerson(s, Worlds.At(occ, 5, 2));
            var with = p.Frame(s, 0, 0, occ.W, occ.H, 0);
            int lx = 6, ly = 4, rise = ArtFurniture.Rise("SuitLocker", 1, 1, Facing.S);   // 그림 칸 (도면은 둘레 한 칸을 더 둔다) — 보관함 (6, 4) · 사람 (6, 3)
            int same = 0, diff = 0, above = 0;
            for (int y = T - rise + 1; y < T; y++)
                for (int x = 6; x < T - 6; x++)
                {
                    bool eq = e.Get(lx * T + x, (ly - 1) * T + y).Equals(with.Get(lx * T + x, (ly - 1) * T + y));
                    if (eq) same++; else diff++;
                }
            for (int y = 2; y < T - rise - 2; y++) for (int x = 10; x < 22; x++) if (!e.Get(lx * T + x, (ly - 1) * T + y).Equals(with.Get(lx * T + x, (ly - 1) * T + y))) above++;
            Check("텍스처 앞 가림: 키 큰 설비 바로 뒤(북쪽)에 선 사람은 설비의 솟은 윗부분에 가린다 · 그 위로는 사람이 보인다",
                diff == 0 && same > 50 && above > 20, $"겹친 곳 같음 {same} · 다름 {diff} · 그 위 사람 도트 {above} (사람 칸 {occ.X(m[who].C.Cell)},{occ.Y(m[who].C.Cell)})");
        }
        // 11 · 옛 그림 색 맞추기 (T2): 새로 그리지 않은 설비는 채도를 낮추고 (큰 면은 낮은 채도) · 그늘은 푸른 보라로 · 투명은 그대로
        {
            static double Sat(Rgba c) { int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx == 0 ? 0 : (mx - mn) / (double)mx; }
            double s0 = 0, s1 = 0; int n = 0, alphaBad = 0, darkWarm0 = 0, darkWarm1 = 0;
            foreach (var ft in new[] { FurnitureType.Seat, FurnitureType.SupplyCache, FurnitureType.MealDispenser, FurnitureType.Workbench, FurnitureType.Scrubber })
            {
                var old = DotMachines.Body(ft, 1, 1, -1, 1, 0, 3); var neu = ArtPalette.Unify(old);
                for (int y = 0; y < old.H; y++) for (int x = 0; x < old.W; x++)
                {
                    var a = old.Get(x, y); var b = neu.Get(x, y);
                    if (a.A != b.A) alphaBad++;
                    if (a.A == 0) continue;
                    s0 += Sat(a); s1 += Sat(b); n++;
                    if (a.Luma < 40 && a.R > a.B) darkWarm0++;
                    if (b.Luma < 40 && b.R > b.B) darkWarm1++;
                }
            }
            Check("텍스처 색 맞추기: 옛 도트 설비 그림은 채도를 낮추고 (평균 15% 넘게) · 어두운 도트는 푸른 보라 쪽으로 (붉은 기 도는 그늘이 준다) · 투명은 그대로",
                s1 < s0 * 0.85 && alphaBad == 0 && darkWarm1 <= darkWarm0, $"평균 채도 {s0 / n:0.000} → {s1 / n:0.000} · 붉은 그늘 {darkWarm0} → {darkWarm1} · 알파 다름 {alphaBad}");
        }
        // 7 · 같은 세계 같은 그림 · 그림은 세계를 바꾸지 않는다
        {
            var s = Worlds.Full(lay, 7);
            s.Run(30);
            var before = s.Save();
            var a = new Painter(lay).Frame(s, 0, 0, lay.W, lay.H, 3);
            var b = new Painter(lay).Frame(s, 0, 0, lay.W, lay.H, 3);
            Check("텍스처: 같은 세계 · 같은 장이면 새 화가 둘이 바이트까지 같은 그림 · 그리기 전후 세계가 같다",
                a.Px.AsSpan().SequenceEqual(b.Px) && before.AsSpan().SequenceEqual(s.Save()), $"{a.W}×{a.H}");
        }
    }

    /// <summary>
    /// 그림 열쇠 (물리 세계 PH0a — '화면' 묶음): 한 화가의 그림 캐시는 열쇠 맨 윗 바이트(갈래 — Painter.cs 머리의 표)로 나뉜다.
    /// 바닥 물건 아이콘(9) · 드론(14) · 가구 빛(13)이 같은 장에 있어도 서로의 그림을 꺼내 쓰지 않는다 (예전엔 드론도 9 — 소화기(물건 0)와 멈춘 드론 장 0이 한 열쇠).
    /// </summary>
    private static void ArtKeyRules()
    {
        Console.WriteLine("\n[그림 열쇠 — 물건 아이콘 · 드론 · 가구 빛]");
        var lay = Worlds.Ascii(@"
###########
#.........#
#.c.......#
#.........#
#.....p.b.#
###########", new Dictionary<char, string> { ['c'] = "Console", ['b'] = "Battery", ['p'] = "PowerPanel" });
        int At(int x, int y) => Worlds.At(lay, x, y);
        const int T = Painter.T;
        int itemCell = At(2, 3), droneCell = At(6, 1);
        var s = Worlds.Full(lay, 21);
        s.Run(3);   // 배전반이 조종대에 전기를 보낸다 (화면 빛) — 드론은 그 뒤에 태운다 (일감 없이 멈춘 장 0)
        var m = s.Get<Mind>();
        int dr = m.AddRobot(s, droneCell, AgentKind.Drone, droneCell);
        void Drop() { if (m.Loose.Count == 0) m.Loose.Add(new LooseItem { Item = (byte)m.iExt, Cell = itemCell, Charge = 500 }); }
        var want = DotBots.Drone(DotDroneKind.Repair, DotBotState.Ok, 0);
        // 드론 그림 자리 (DrawBot과 같다 — 칸 가운데 · 8도트 떠 있다)의 불투명 도트가 드론 그림 그대로인가
        (int bad, int n) Drone(Img im)
        {
            int ox = lay.X(droneCell) * T + T / 2 - want.W / 2, oy = lay.Y(droneCell) * T + T / 2 - want.H / 2 - 8, bad = 0, n = 0;
            for (int y = 0; y < want.H; y++) for (int x = 0; x < want.W; x++)
            {
                var w = want.Get(x, y);
                if (w.A != 255) continue;
                n++;
                if (!im.Get(ox + x, oy + y).Equals(w)) bad++;
            }
            return (bad, n);
        }
        static int Diff(Img a, Img b) { int d = 0; for (int i = 0; i < a.Px.Length; i += 4) if (a.Px[i] != b.Px[i] || a.Px[i + 1] != b.Px[i + 1] || a.Px[i + 2] != b.Px[i + 2]) d++; return d; }

        // 1 · 아이콘을 먼저 그린 화가가 드론을 그려도 드론 그림 / 드론을 먼저 그린 화가가 소화기를 그려도 소화기 그림 (두 화가의 장이 바이트까지 같다)
        Drop();
        var iconFirst = new Painter(lay) { Lighting = false };
        var a1 = iconFirst.Frame(s, 0, 0, lay.W, lay.H, 0);
        var (bad, n) = Drone(a1);
        m.Loose.Clear();
        var droneFirst = new Painter(lay) { Lighting = false };
        droneFirst.Frame(s, 0, 0, lay.W, lay.H, 0);
        Drop();
        var b1 = droneFirst.Frame(s, 0, 0, lay.W, lay.H, 0);
        int swap = Diff(a1, b1);
        if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") { Png.WriteRgba("build/shots/rule-artkey-a.png", a1.W, a1.H, a1.Px); Png.WriteRgba("build/shots/rule-artkey-b.png", b1.W, b1.H, b1.Px); }
        Check("그림 열쇠 — 물건 아이콘 · 드론: 바닥 소화기(물건 0) 아이콘을 먼저 그린 화가가 멈춘 수리 드론(Id 4의 배수 · 장 0)을 그려도 드론 그림이다 · 드론을 먼저 그린 화가의 소화기도 제 그림 (두 장이 같다)",
            m.iExt == 0 && m[dr].Id % 4 == 0 && n > 20 && bad == 0 && swap == 0,
            $"소화기 물건 {m.iExt} · 드론 Id {m[dr].Id} · 드론 자리 다른 도트 {bad}/{n} · 그린 차례를 바꾼 두 장의 다른 도트 {swap}");

        // 2 · 넓은 보기 (16도트 · 빛 켬): 한 장에 드론은 빛보다 먼저 · 32도트 장은 가구 빛이 드론보다 먼저 캐시에 든다 — 차례가 달라도 같은 장 (갈래가 겹치면 한쪽이 남의 그림)
        {
            var farOnly = new Painter(lay);
            var c1 = farOnly.FrameFar(s, 0, 0, lay.W, lay.H, 0, 16);
            var nearFirst = new Painter(lay);
            nearFirst.Frame(s, 0, 0, lay.W, lay.H, 0);
            var c2 = nearFirst.FrameFar(s, 0, 0, lay.W, lay.H, 0, 16);
            int far = Diff(c1, c2);
            // 조종대 화면 빛이 그 장에 있다 (빛을 곱한 뒤 얹는 푸른 화면 · 줄)
            int con = lay.Furniture.FindIndex(fu => fu.Type == "Console"), screen = 0, D = 16;
            var box = lay.Furniture[con].Cells;
            int bx0 = box.Min(lay.X) * D, by0 = box.Min(lay.Y) * D - D, bx1 = (box.Max(lay.X) + 1) * D, by1 = (box.Max(lay.Y) + 1) * D;
            for (int y = Math.Max(0, by0); y < by1; y++) for (int x = bx0; x < bx1; x++) { var q = c1.Get(x, y); if (q.B > 150 && q.G > 120 && q.B > q.R + 40) screen++; }
            // 드론이 그 장에 있다 (드론을 뺀 장과 드론 칸 둘레가 다르다)
            int dCell = m[dr].C.Cell;
            m[dr].C.Cell = At(8, 1);
            var c0 = new Painter(lay).FrameFar(s, 0, 0, lay.W, lay.H, 0, 16);
            m[dr].C.Cell = dCell;
            // 바닥 물건이 없는 장을 처음 그린 화가의 드론 (아이콘이 캐시에 든 적이 없다)과 같은 드론
            m.Loose.Clear();
            var c3 = new Painter(lay).FrameFar(s, 0, 0, lay.W, lay.H, 0, 16);
            Drop();
            int drone = 0, own = 0;
            for (int y = (lay.Y(droneCell) - 1) * D; y < (lay.Y(droneCell) + 1) * D; y++) for (int x = lay.X(droneCell) * D; x < (lay.X(droneCell) + 1) * D; x++)
            {
                if (!c0.Get(x, y).Equals(c1.Get(x, y))) drone++;
                if (!c3.Get(x, y).Equals(c1.Get(x, y))) own++;
            }
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") { Png.WriteRgba("build/shots/rule-artkey-far.png", c1.W, c1.H, c1.Px); }
            Check("그림 열쇠 — 드론 · 가구 빛 (넓은 보기): 조종대 화면 빛 · 소화기 아이콘 · 드론이 한 장에 있어도 모두 제 그림 (캐시에 든 차례를 바꾼 두 장이 같다 · 드론은 물건 없이 그린 드론 그대로)",
                far == 0 && screen > 4 && drone > 20 && own == 0, $"차례를 바꾼 두 장의 다른 도트 {far} · 조종대 화면 빛 도트 {screen} · 드론 자리 도트 {drone} · 물건 없이 그린 드론과 다른 도트 {own}");
        }
        m.Loose.Clear();
    }
}
