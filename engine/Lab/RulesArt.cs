using ShipEngine;
using ShipSim.Art;
using ShipView;

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
}
