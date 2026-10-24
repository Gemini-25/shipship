using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;
using ShipView;

namespace Lab;

/// <summary>
/// 넓은 보기 화가 규칙 (docs/GRAPHICS.md §3 T0 세밀도 단계 · 6단 60프레임): 칸당 16 · 8도트 그림이 32도트 그림을 줄인 것과 칸마다 같다
/// (사람 · 로봇 · 문 · 불 칸 포함) · 세계가 바뀐 덩어리는 다시 굽는다 · 관전 화면은 보는 칸 수로 도트를 고른다. 그림은 세계를 읽기만.
/// </summary>
public static partial class Program
{
    private static void FarRules()
    {
        if (!Do("넓은 보기")) return;
        Console.WriteLine("\n[넓은 보기 — 칸당 16 · 8도트: 굽은 덩어리 + 장마다 문 · 사람 · 불 · 빛 (그림은 세계를 읽기만)]");
        var lay = Layout.Ship("Didim", Tables.Default);
        int beds = lay.Furniture.Count(fu => fu.Type == "Bed"), docks = lay.Furniture.Count(fu => fu.Type == "RobotDock");
        var s = ShipWorld.Crewed("Didim", 5, Math.Max(2, beds), docks, 0);
        s.Run(Time.Hours(1));   // 사람이 일어나 돌아다닌다
        var mind = s.Get<Mind>();
        var lamp = s.Get<Light>();
        for (int r = 0; r < lamp.Lamp.Length; r++) lamp.Lamp[r] = r % 3 == 0 ? 0 : r % 3 == 1 ? 90 : 300;   // 방마다 등이 다르다 (꺼짐 · 밤 · 낮)
        var f = s.Get<Field>();
        // 불 하나 (불꽃 · 불빛 · 연기): 가장 큰 방 가운데 빈 바닥
        var big = lay.Rooms.OrderByDescending(r => r.Cells.Length).First();
        int fire = big.Cells.Where(c => lay.FurnitureAt[c] < 0).OrderBy(c => Math.Abs(lay.X(c) - big.Cells.Average(lay.X)) + Math.Abs(lay.Y(c) - big.Cells.Average(lay.Y))).First();
        s.Get<Chemistry>().Ignite(fire, 3e6, s.Causes.Add(CauseKind.Setup, "넓은 보기 불씨", s.Tick));
        s.Run(20);
        // 문 하나는 닫고 하나는 연다 (문짝은 장마다 그린다)
        if (lay.Doors.Count >= 2) { f.SetDoor(0, 0f); f.SetDoor(1, 1f); }

        // 칸마다 평균 빛깔 차이 (0~255): a는 D도트 그림 · b는 32도트 그림을 같은 크기로 줄인 것
        static double[] CellDiff(Img a, Img b, int w, int h, int D)
        {
            var o = new double[w * h];
            for (int cy = 0; cy < h; cy++)
                for (int cx = 0; cx < w; cx++)
                {
                    double ar = 0, ag = 0, ab = 0, br = 0, bg = 0, bb = 0;
                    for (int y = 0; y < D; y++)
                        for (int x = 0; x < D; x++)
                        {
                            var p = a.Get(cx * D + x, cy * D + y); var q = b.Get(cx * D + x, cy * D + y);
                            ar += p.R; ag += p.G; ab += p.B; br += q.R; bg += q.G; bb += q.B;
                        }
                    o[cy * w + cx] = (Math.Abs(ar - br) + Math.Abs(ag - bg) + Math.Abs(ab - bb)) / (3.0 * D * D);
                }
            return o;
        }
        static Img Shrink(Img src, int k)
        {
            var o = new Img(src.W / k, src.H / k);
            for (int y = 0; y < o.H; y++)
                for (int x = 0; x < o.W; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int yy = 0; yy < k; yy++) for (int xx = 0; xx < k; xx++) { var c = src.Get(x * k + xx, y * k + yy); r += c.R; g += c.G; b += c.B; }
                    o.Set(x, y, new Rgba((byte)(r / (k * k)), (byte)(g / (k * k)), (byte)(b / (k * k))));
                }
            return o;
        }
        static double Mean(IEnumerable<double> v) { var a = v.ToArray(); return a.Length == 0 ? 0 : a.Average(); }
        static double P95(double[] v) { var a = v.OrderBy(x => x).ToArray(); return a.Length == 0 ? 0 : a[(int)(a.Length * 0.95)]; }

        var p = new Painter(lay);
        int W = lay.W, H = lay.H;
        double bakeMs = Painter.FarBakeMs;
        Painter.FarBakeMs = 0; Painter.FarBakeMax = 1;   // 시험은 시간 대신 한 장에 덩어리 하나로 (기계가 바빠도 같은 그림)
        Painter.NoPools = true;   // 넓은 보기엔 빛 웅덩이가 없다 (방 평균은 그대로 — '텍스처 빛 웅덩이')
        var full = p.Frame(s, 0, 0, W, H, 7);
        Painter.NoPools = false;
        var agentCells = mind.Agents.Where(a => a.C.CarriedBy < 0).Select(a => a.C.Cell).Distinct().ToArray();
        var doorCells = lay.Doors.Select(d => d.Cell).ToArray();
        var fireCells = new[] { fire };

        // 1 · 같은 그림: 16 · 8도트 그림은 32도트 그림을 줄인 것과 칸마다 비슷하다 — 빛 · 사람 · 문 · 불을 장마다 그리므로 그 칸도
        foreach (int D in new[] { 16, 8 })
        {
            var far = p.FrameFar(s, 0, 0, W, H, 7, D);
            var diff = CellDiff(far, Shrink(full, Painter.T / D), W, H, D);
            double all = diff.Average(), p95 = P95(diff), ag = Mean(agentCells.Select(c => diff[lay.Y(c) * W + lay.X(c)])), dr = Mean(doorCells.Select(c => diff[lay.Y(c) * W + lay.X(c)])), fi = Mean(fireCells.Select(c => diff[lay.Y(c) * W + lay.X(c)]));
            Check($"넓은 보기 {D}도트: 32도트 그림을 줄인 것과 칸마다 비슷하다 — 빛 · 사람 {agentCells.Length} · 문 {doorCells.Length} · 불 칸까지 (칸 평균 빛깔 차이)",
                far.W >= W * D && far.H >= H * D && all <= 6 && p95 <= 16 && ag <= 14 && dr <= 14 && fi <= 18,
                $"그림 {far.W}×{far.H} · 모든 칸 {all:0.0} · 95% {p95:0.0} · 사람 칸 {ag:0.0} · 문 칸 {dr:0.0} · 불 칸 {fi:0.0}");
        }

        // 2 · 다시 굽기: 벽이 녹슬고 · 외판이 뚫리고 · 선반이 비면 다음 장에 바로 보인다 (덩어리 도장이 바뀐다) · 같은 세계 같은 그림
        {
            var st = s.Get<Structure>();
            int ext = Array.IndexOf(mind.ItemNames, "소화기");   // 선반 그림은 소화기 · 봉합 꾸러미 수를 읽는다
            // 앞이 보이는 선반 하나 (뒷판이 보이는 선반은 안의 물건이 안 보인다): 32도트로 소화기 있음 · 없음이 다른 것
            int shelf = -1;
            for (int i = 0; i < lay.Furniture.Count && shelf < 0 && ext >= 0; i++)
            {
                if (lay.Furniture[i].Type != "Shelf") continue;
                int bx = lay.Furniture[i].Cells.Min(lay.X), by = lay.Furniture[i].Cells.Min(lay.Y), bw = lay.Furniture[i].Cells.Max(lay.X) - bx + 1, bh = lay.Furniture[i].Cells.Max(lay.Y) - by + 2;
                int keep = mind.Stock[i * mind.ItemCount + ext];
                mind.Stock[i * mind.ItemCount + ext] = 3; var on = p.Frame(s, bx, by - 1, bw, bh, 7).Px.ToArray();
                mind.Stock[i * mind.ItemCount + ext] = 0; var off = p.Frame(s, bx, by - 1, bw, bh, 7).Px.ToArray();
                mind.Stock[i * mind.ItemCount + ext] = keep;
                if (!on.AsSpan().SequenceEqual(off)) { shelf = i; mind.Stock[i * mind.ItemCount + ext] = 3; }
            }
            for (int n = 0; n < 5; n++) p.FrameFar(s, 0, 0, W, H, 7, 16);   // 바뀐 덩어리를 다 굽고 (한 장에 하나)
            var before = p.FrameFar(s, 0, 0, W, H, 7, 16);
            var again = p.FrameFar(s, 0, 0, W, H, 7, 16);
            bool same = before.Px.AsSpan(0, W * 16 * H * 16 * 4).SequenceEqual(again.Px.AsSpan(0, W * 16 * H * 16 * 4));
            var beforeCopy = new Img(before.W, before.H); Array.Copy(before.Px, beforeCopy.Px, before.Px.Length);
            var changed = new List<int>();
            int hull = Enumerable.Range(0, lay.N).First(c => lay.Kind[c] == CellKind.Wall && lay.Hull[c] && lay.In(lay.X(c), lay.Y(c) + 1) && lay.Kind[lay.Index(lay.X(c), lay.Y(c) + 1)] == CellKind.Floor);
            f.Hole[hull] = 3f; st.Sub[hull] = 0; changed.Add(hull);   // 통째로 뚫린 외판 — 우주가 보인다
            int inner = Enumerable.Range(0, lay.N).First(c => lay.Kind[c] == CellKind.Wall && !lay.Hull[c] && c != hull && lay.In(lay.X(c), lay.Y(c) + 1) && lay.Kind[lay.Index(lay.X(c), lay.Y(c) + 1)] == CellKind.Floor);
            st.Corrode[inner] = 0.9f; st.Fatigue[inner] = 0.9f; changed.Add(inner);
            if (shelf >= 0) mind.Stock[shelf * mind.ItemCount + ext] = 0;
            // 관전 화면은 한 장에 1ms까지만 다시 굽는다 (시험은 덩어리 하나) — 바뀐 덩어리가 여럿이면 몇 장에 걸쳐 (차례를 돌린다): 다섯 장 안에 모두
            var after = p.FrameFar(s, 0, 0, W, H, 7, 16);
            for (int n = 0; n < 4; n++) after = p.FrameFar(s, 0, 0, W, H, 7, 16);
            Painter.NoPools = true;
            var full2 = p.Frame(s, 0, 0, W, H, 7);
            Painter.NoPools = false;
            var diff = CellDiff(after, Shrink(full2, 2), W, H, 16);
            var moved = CellDiff(after, beforeCopy, W, H, 16);
            double Moved(int c) => moved[lay.Y(c) * W + lay.X(c)];
            double sh = shelf >= 0 ? lay.Furniture[shelf].Cells.Max(Moved) : 0;
            double ch = changed.Concat(shelf >= 0 ? lay.Furniture[shelf].Cells : Array.Empty<int>()).Max(c => diff[lay.Y(c) * W + lay.X(c)]);
            Check("넓은 보기 다시 굽기: 외판 구멍 · 녹 · 실금 · 빈 선반이 몇 장 안에 보인다 (바뀐 덩어리만 · 한 장에 1ms까지 — 시험은 덩어리 하나) · 같은 세계는 같은 그림",
                same && Moved(hull) >= 15 && Moved(inner) >= 2 && shelf >= 0 && sh >= 1 && ch <= 12,
                $"같은 그림 {same} · 앞 장과 차이: 뚫린 외판 {Moved(hull):0.0} · 녹 · 실금 {Moved(inner):0.0} · 소화기를 뺀 선반 {(shelf >= 0 ? sh.ToString("0.0") : "없음")} · 32도트와 차이 가장 큰 칸 {ch:0.0}");
        }

        // 4 · 문 칸 얼룩 (검토가 찾음): 닫힌 문 칸의 그을음은 문짝 위에 — 32도트는 문짝을 먼저 그리고 그을음 · 쏟은 액체를 얹는다 (예전 넓은 보기는 구운 얼룩 위에 문짝을 덮어 가렸다)
        {
            Painter.FarBakeMax = 0;
            for (int d = 0; d < lay.Doors.Count; d++) f.SetDoor(d, 0f);
            foreach (var d in lay.Doors) f.Store[f.T.sSoot * f.Cells + d.Cell] = 0.5;   // 3g/㎡
            p.FrameFar(s, 0, 0, W, H, 7, 16);
            var far = p.FrameFar(s, 0, 0, W, H, 7, 16);
            Painter.NoPools = true; var full4 = p.Frame(s, 0, 0, W, H, 7); Painter.NoPools = false;
            var diff = CellDiff(far, Shrink(full4, 2), W, H, 16);
            double dr = Mean(doorCells.Select(c => diff[lay.Y(c) * W + lay.X(c)])), worst = doorCells.Max(c => diff[lay.Y(c) * W + lay.X(c)]);
            Check("넓은 보기 문 칸 얼룩: 닫힌 문 칸의 그을음은 문짝 위에 (32도트와 같은 차례) — 문 칸 평균 빛깔 차이 ≤ 8",
                dr <= 8 && worst <= 16, $"문 {doorCells.Length}칸 평균 {dr:0.0} · 가장 큰 {worst:0.0}");
            foreach (var d in lay.Doors) f.Store[f.T.sSoot * f.Cells + d.Cell] = 0;
        }

        // 5 · 공황 표시 (검토가 찾음): 머리 위 느낌표는 빛을 받지 않는다 — 불이 꺼진 방에서도 32도트처럼 밝다 (예전 넓은 보기는 빛에 곱해 꺼진 방에서 안 보였다)
        {
            var who = mind.Agents.First(a => a.IsPerson && a.C.CarriedBy < 0 && life(a) && lay.Room[a.C.Cell] >= 0);
            bool life(Agent a) => s.Get<Life>().Bodies[a.C.Body].Alive != 0;
            byte was = who.C.Mode; who.C.Mode = 2;
            var lampWas = lamp.Lamp.ToArray();
            Array.Fill(lamp.Lamp, 0f);
            var far = p.FrameFar(s, 0, 0, W, H, 7, 16);
            var full5 = Shrink(p.Frame(s, 0, 0, W, H, 7), 2);
            int cx = lay.X(who.C.Cell), cy = lay.Y(who.C.Cell);
            double MaxY(Img im) { double m = 0; for (int y = (cy - 1) * 16; y < (cy + 1) * 16; y++) for (int x = cx * 16; x < cx * 16 + 16; x++) { var q = im.Get(x, y); m = Math.Max(m, 0.299 * q.R + 0.587 * q.G + 0.114 * q.B); } return m; }
            double mf = MaxY(far), mk = MaxY(full5);
            Check("넓은 보기 공황 표시: 꺼진 방에서도 머리 위 느낌표가 32도트처럼 밝다 (빛을 곱한 뒤에 그린다)", mk > 100 && mf >= mk * 0.7,
                $"가장 밝은 점: 16도트 {mf:0} · 32도트를 줄인 것 {mk:0}");
            who.C.Mode = was; Array.Copy(lampWas, lamp.Lamp, lampWas.Length);
        }

        // 6 · 8도트 '선과 관' (검토가 찾음): 냉각관 · 물관이 같은 칸에 나란하면 둘 다 보인다 (예전엔 8도트에서 비킴이 같아 하나가 덮었다)
        {
            var net = s.Get<Network>();
            int both = Enumerable.Range(0, lay.N).FirstOrDefault(c => (net.LineAt[c] & 6) == 6 && net.LineDir[c * 4 + 1] == net.LineDir[c * 4 + 2] && net.LineDir[c * 4 + 1] != 0, -1);
            if (both >= 0)
            {
                p.Show = Overlay.Lines;
                var far = p.FrameFar(s, 0, 0, W, H, 7, 8);
                p.Show = Overlay.None;
                int cx = lay.X(both), cy = lay.Y(both), cyan = 0, blue = 0;
                for (int y = cy * 8; y < cy * 8 + 8; y++) for (int x = cx * 8; x < cx * 8 + 8; x++) { var q = far.Get(x, y); if (q.G > 150 && q.B > 170) cyan++; else if (q.B > 150 && q.G < 130 && q.R < 110) blue++; }
                Check("넓은 보기 8도트 선과 관: 한 칸에 나란한 냉각관 (하늘) · 물관 (파랑)이 둘 다 보인다 (고치기 전엔 겹쳐 1점씩)", cyan >= 4 && blue >= 4, $"하늘 {cyan}점 · 파랑 {blue}점");
            }
            else Console.WriteLine("  (냉각관 · 물관이 나란한 칸이 없다 — 8도트 선과 관 건너뜀)");
        }

        // 7 · 조각 (검토가 찾음): 선체 조각이 떨어져 나가도 넓은 보기 길 그대로 — 배 몸만 굽고 조각만 32도트로 그려 줄여 얹는다 (예전엔 끝까지 32도트 한 장을 새로 잡아 줄였다)
        {
            var dl = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var ds = Worlds.Full(dl, 2);
            ds.Get<Mind>().Crew(ds, 2, 0, 0);
            var dst = ds.Get<Structure>();
            ds.Run(Time.Minutes(1));
            int cause = ds.Causes.Add(CauseKind.Setup, "커다란 운석이 통로를 꿰뚫었다", ds.Tick);
            for (int y = 2; y <= 4; y++) dst.Destroy(ds, Worlds.At(dl, 10, y), cause);
            ds.Run(40);
            var dp = new Painter(dl);
            int RW = 100, RH = 60, rx = dl.W / 2 - RW / 2, ry = dl.H / 2 - RH / 2;
            var canvas = dp.FrameFar(ds, rx, ry, RW, RH, 7, 16);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var far = dp.FrameFar(ds, rx, ry, RW, RH, 7, 16, canvas);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            Painter.NoPools = true; var full7 = dp.Frame(ds, rx, ry, RW, RH, 7); Painter.NoPools = false;
            var diff = CellDiff(far, Shrink(full7, 2), RW, RH, 16);
            Check("넓은 보기 조각: 떨어져 나간 조각이 있어도 넓은 보기 길로 (한 장 새로 잡기 < 6MB) · 32도트를 줄인 것과 칸마다 비슷하다",
                dst.Bodies.Count >= 2 && alloc < 6 << 20 && diff.Average() <= 4 && P95(diff) <= 12,
                $"몸 {dst.Bodies.Count} · 한 장 새로 잡기 {alloc / 1024}KB · 모든 칸 {diff.Average():0.0} · 95% {P95(diff):0.0}");
        }

        // 8 · 실금 문턱 · 덩어리 경계 (검토가 찾음): 피로가 문턱(0.3)을 막 넘으면 다음 장에 실금이 보이고 (도장 단이 문턱과 맞는다) · 덩어리 경계 곁 칸으로 뻗은 실금이 경계에서 잘리지 않는다
        //     실금은 어두운 뚜껑 위라 '32도트에서 바뀐 칸이 넓은 보기에서도 바뀌었나'로 본다
        {
            Painter.FarBakeMax = 0;
            var st = s.Get<Structure>();
            p.Lighting = false;   // 빛을 뺀 그림으로 (어두운 벽은 빛을 곱하면 실금 차이가 한 단 밑으로 사라진다 — 빛은 1 · 2에서 본다)
            int Changed(Img a, Img b, int c, int D) { int n = 0; for (int y = 0; y < D; y++) for (int x = 0; x < D; x++) if (a.Get(lay.X(c) * D + x, lay.Y(c) * D + y) != b.Get(lay.X(c) * D + x, lay.Y(c) * D + y)) n++; return n; }
            Img Far() { var im = p.FrameFar(s, 0, 0, W, H, 7, 16); var o = new Img(im.W, im.H); Array.Copy(im.Px, o.Px, im.Px.Length); return o; }
            // 경계: 경계 칸의 실금이 32도트에서 옆 칸(다음 덩어리)으로 뻗는 칸을 찾는다
            int seam = -1, next = -1, fullN = 0, farN = 0;
            foreach (int c in Enumerable.Range(0, lay.N).Where(c => lay.Kind[c] == CellKind.Wall && lay.X(c) % Painter.Chunk == Painter.Chunk - 1 && lay.In(lay.X(c) + 1, lay.Y(c)) && lay.Kind[lay.Index(lay.X(c) + 1, lay.Y(c))] == CellKind.Wall))
            {
                int nx = lay.Index(lay.X(c) + 1, lay.Y(c));
                st.Fatigue[c] = 0; var f0 = p.Frame(s, 0, 0, W, H, 7); var g0 = Far();
                st.Fatigue[c] = 0.95f; var f1 = p.Frame(s, 0, 0, W, H, 7); var g1 = Far();
                st.Fatigue[c] = 0;
                int fn = Changed(f0, f1, nx, 32);
                if (fn == 0) continue;
                seam = c; next = nx; fullN = fn; farN = Changed(g0, g1, nx, 16);
                break;
            }
            // 문턱: 0.29 → 0.32 (예전 도장 단 (int)(피로 × 14)는 둘 다 4 — 다시 굽지 않아 실금이 안 보였다)
            int wall = Enumerable.Range(0, lay.N).First(c => lay.Kind[c] == CellKind.Wall && !lay.Hull[c] && lay.In(lay.X(c), lay.Y(c) + 1) && lay.Kind[lay.Index(lay.X(c), lay.Y(c) + 1)] == CellKind.Floor);
            st.Fatigue[wall] = 0.29f; var t0 = p.Frame(s, 0, 0, W, H, 7); var h0 = Far();
            st.Fatigue[wall] = 0.32f; var t1 = p.Frame(s, 0, 0, W, H, 7); var h1 = Far();
            st.Fatigue[wall] = 0;
            int fullT = Changed(t0, t1, wall, 32), farT = Changed(h0, h1, wall, 16);
            Check("넓은 보기 실금: 피로가 문턱을 막 넘으면 다음 장에 보이고 (도장 단 = 굽기 단) · 덩어리 경계 곁 칸으로 뻗은 실금이 잘리지 않는다",
                seam >= 0 && farN > 0 && fullT > 0 && farT > 0,
                $"경계 곁 칸 바뀐 도트: 32도트 {fullN} · 16도트 {farN}{(seam < 0 ? " (경계로 뻗는 칸 없음)" : "")} / 문턱 넘은 칸: 32도트 {fullT} · 16도트 {farT}");
            p.Lighting = true;
        }

        Painter.FarBakeMs = bakeMs; Painter.FarBakeMax = 0;

        // 3 · 도트 고르기: 관전 화면은 보는 칸 수로 고른다 (60칸까지 32 · 120칸까지 16 · 넘으면 8 · 경계에서 3칸 머뭇거린다) · 잘라 볼 곳도 그 도트로
        {
            var show = new LiveShow(ShipWorld.Crewed("Didim", 3, 2), "시험호");
            (int dot, bool ok) Look(double vw)
            {
                show.ViewW = vw;
                var fr = show.Render();
                show.Recycle(fr.Img);
                int dot = (int)Math.Round(fr.SrcW / fr.CamW);
                return (dot, fr.Dot == dot && fr.SrcX + fr.SrcW <= fr.Img.W + 0.5f && fr.SrcY + fr.SrcH <= fr.Img.H + 0.5f);
            }
            var seq = new[] { 40.0, 59, 62, 64, 59, 56, 100, 124, 118, 115, 50 };
            var got = seq.Select(Look).ToArray();
            // 62는 32 그대로 (63을 넘어야 16) · 59는 16 그대로 (57 밑이어야 32) · 124 → 8 · 118은 8 그대로 · 115 → 16
            var want = new[] { 32, 32, 32, 16, 16, 32, 16, 8, 8, 16, 32 };
            Check("넓은 보기 고르기: 보는 칸 수로 도트를 고른다 (60칸까지 32 · 120칸까지 16 · 넘으면 8 · 경계 ±3칸은 지금 도트 그대로) · 잘라 볼 곳도 그 도트로",
                got.Select(g => g.dot).SequenceEqual(want) && got.All(g => g.ok),
                $"{string.Join(" · ", seq.Zip(got, (v, g) => $"{v}칸 {g.dot}{(g.ok ? "" : "✘")}"))}");
        }
    }
}
