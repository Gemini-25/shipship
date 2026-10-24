using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>재질 흔적 규칙 시험 (그래픽 결정 5) — 녹 · 실금 · 쏟아진 액체는 세계의 값에서만 · 값이 줄면(수리 · 청소) 흔적도 사라진다.</summary>
public static partial class Program
{
    // 곰팡이 시험 배 (5단 A3 검토): 변기 T · 세면대 k · 샤워 칸 S · 배터리 b · 배전반 p 바로 위가 가구 없는 바닥 (솟은 몸이 위 칸으로 올라온다) ·
    // 가로 벽 문 D (위아래 바닥 — 문짝이 아래 칸 위 10줄에 드리운다)
    private const string MoldShip = @"
###########
#.........#
#.T.k.S.b.#
#.........#
####D######
#.........#
#.p.......#
###########";

    private static void TraceRules()
    {
        if (!Do("재질 흔적")) return;
        Console.WriteLine("\n[재질 흔적]");
        {
            var s = ShipWorld.Create("Didim", 3);
            s.Run(3);
            var L = s.Layout; var st = s.Get<Structure>(); var f = s.Get<Field>(); var p = new Painter(L);
            var room = L.Rooms.First(r => r.Type == "Mess");
            // 방 위쪽 벽 (아래가 바닥) 하나 · 가구 없는 바닥 하나
            int wall = room.Cells.Where(c => L.FurnitureAt[c] < 0).Select(c => c - L.W).First(j => j >= 0 && L.Kind[j] == CellKind.Wall && !L.Hull[j]);
            int floor = room.Cells.First(c => L.FurnitureAt[c] < 0 && L.Kind[c] == CellKind.Floor && c - L.W != wall);
            int x0 = Math.Max(0, Math.Min(L.X(wall), L.X(floor)) - 2), y0 = Math.Max(0, Math.Min(L.Y(wall), L.Y(floor)) - 2);
            // dy 0: 벽 뚜껑 (위 18줄) · dy 1: 앞면 (벽 칸 아래 14줄 + 바닥 칸 위 10줄 — 텍스처 T3)
            int Count(Img img, int c, int dy, Func<Rgba, bool> pick)
            {
                int n = 0, from = dy == 0 ? 0 : ArtWalls.CapH, to = dy == 0 ? ArtWalls.CapH : ArtWalls.CapH + ArtWalls.FaceH;
                for (int yy = from; yy < to; yy++) for (int xx = 0; xx < Painter.T; xx++) if (pick(img.Get((L.X(c) - x0) * Painter.T + xx, (L.Y(c) - y0) * Painter.T + yy))) n++;
                return n;
            }
            static bool Rusty(Rgba q) => q.R > q.G + 25 && q.G > q.B && q.R > 60;
            static bool Teal(Rgba q) => q.G > q.R + 25 && q.B > q.R + 10;
            Img Shot() { var im = p.Frame(s, x0, y0, 10, 10, 0); var copy = new Img(im.W, im.H); Array.Copy(im.Px, copy.Px, im.Px.Length); return copy; }
            p.Lighting = false;   // 빛깔만 견준다
            var clean = Shot();
            int rust0 = Count(clean, wall, 0, Rusty), drip0 = Count(clean, wall, 1, Rusty), pool0 = Count(clean, floor, 0, Teal);
            st.Corrode[wall] = 0.4f; st.Fatigue[wall] = 0.8f; f.AddStore(floor, f.T.sNH3l, 30);
            var worn = Shot();
            int rust1 = Count(worn, wall, 0, Rusty), drip1 = Count(worn, wall, 1, Rusty), pool1 = Count(worn, floor, 0, Teal);
            int crack = 0; { int dark = 0; for (int yy = 0; yy < Painter.T; yy++) for (int xx = 0; xx < Painter.T; xx++) { var a = clean.Get((L.X(wall) - x0) * Painter.T + xx, (L.Y(wall) - y0) * Painter.T + yy); var b = worn.Get((L.X(wall) - x0) * Painter.T + xx, (L.Y(wall) - y0) * Painter.T + yy); if (!Rusty(b) && a.R + a.G + a.B - (b.R + b.G + b.B) > 30) dark++; } crack = dark; }
            st.Corrode[wall] = 0; st.Fatigue[wall] = 0; f.TakeStore(floor, f.T.sNH3l, f.Stored(f.T.sNH3l, floor));
            var fixedImg = Shot();
            int rust2 = Count(fixedImg, wall, 0, Rusty), pool2 = Count(fixedImg, floor, 0, Teal);
            Check("재질 흔적: 녹(부식 0.4)은 벽 윗면에 얼룩 · 앞면에 흘러내린 자국 · 실금(피로 0.8)은 어두운 선 · 쏟아진 암모니아는 청록 웅덩이 · 고치고 닦으면 사라진다",
                rust0 == 0 && rust1 > 30 && drip1 > drip0 + 5 && crack > 10 && pool0 == 0 && pool1 > 40 && rust2 == 0 && pool2 == 0,
                $"녹 {rust0} → {rust1} → {rust2} · 흘러내림 {drip0} → {drip1} · 실금 {crack} · 웅덩이 {pool0} → {pool1} → {pool2}");
        }

        // ── 곰팡이 얼룩 (5단 A3 — Life.Mold): 가구 없는 바닥에 검푸른 점 · 닦으면(0) 처음 그림 · 화장실 설비 칸은 줄눈 · 가장자리 4도트에만 · 다른 가구 칸은 건너뛴다 · 넓은 보기 16도트는 다시 구운 덩어리에 ──
        {
            var s = ShipWorld.Create("Didim", 3);
            s.Run(3);
            var L = s.Layout; var life = s.Get<Life>(); var m = s.Get<Mind>();
            var p = new Painter(L) { Lighting = false };   // 빛깔만 견준다
            const int T = Painter.T;
            bool Clear(int c) => !m.Agents.Any(a => a.C.Cell == c || a.C.Cell == c + L.W);   // 사람 · 로봇이 칸을 가리지 않는다 (아래 칸의 사람도 위로 솟는다)
            var room = L.Rooms.First(r => r.Type == "Mess");
            var bare = room.Cells.Where(c => L.FurnitureAt[c] < 0 && L.Kind[c] == CellKind.Floor && Clear(c)).ToList();
            int floor = bare.FirstOrDefault(c => L.X(c) % Painter.Chunk is >= 1 and <= 6 && L.Y(c) % Painter.Chunk is >= 2 and <= 6, bare[0]);   // 덩어리 하나의 도장에만 드는 칸 (넓은 보기가 두 장 안에 다시 굽는다)
            int other = room.Cells.First(c => L.FurnitureAt[c] >= 0 && L.Furniture[L.FurnitureAt[c]].Type is not ("Toilet" or "Sink" or "Shower") && Clear(c));
            int toilet = L.Furniture.First(fu => fu.Type == "Toilet" && fu.Cells.Length > 0 && Clear(fu.Cells[0])).Cells[0];
            Img Shot(int c) { int x0 = Math.Max(0, L.X(c) - 2), y0 = Math.Max(0, L.Y(c) - 2); var im = p.Frame(s, x0, y0, 5, 5, 0); var copy = new Img(im.W, im.H); Array.Copy(im.Px, copy.Px, im.Px.Length); return copy; }
            // 그 칸의 도트 (칸 안 자리 xx · yy)마다 견준다
            int Count(Img a, Img b, int c, Func<int, int, Rgba, Rgba, bool> pick)
            {
                int x0 = Math.Max(0, L.X(c) - 2), y0 = Math.Max(0, L.Y(c) - 2), n = 0;
                for (int yy = 0; yy < T; yy++)
                    for (int xx = 0; xx < T; xx++)
                    {
                        int gx = (L.X(c) - x0) * T + xx, gy = (L.Y(c) - y0) * T + yy;
                        if (pick(xx, yy, a.Get(gx, gy), b.Get(gx, gy))) n++;
                    }
                return n;
            }
            static int Sum(Rgba q) => q.R + q.G + q.B;
            static bool Moldy(int xx, int yy, Rgba q0, Rgba q1) => Sum(q0) - Sum(q1) > 60 && q1.G >= q1.R;   // 기준보다 어둡고 푸르다
            static bool Changed(int xx, int yy, Rgba q0, Rgba q1) => q0 != q1;
            static bool Edge(int xx, int yy) => xx < 4 || xx >= T - 4 || yy < 4 || yy >= T - 4 || xx is 15 or 16 || yy is 15 or 16;   // 가장자리 4도트 · 가운데 줄눈 (16도트 타일)
            var b0 = Shot(floor); var o0 = Shot(other);
            p.FrontLog = new();   // 변기 몸 그림의 불투명한 도트 (A3 검토 — 가장자리 점이 흡입 장치 · 테 위에 앉았다)
            var t0 = Shot(toilet);
            var tBody = new HashSet<(int, int)>();
            foreach (var (fimg, fx, fy) in p.FrontLog)
                for (int yy = 0; yy < fimg.H; yy++) for (int xx = 0; xx < fimg.W; xx++) if (fimg.Get(xx, yy).A == 255) tBody.Add((fx + xx, fy + yy));
            p.FrontLog = null;
            int tx = (L.X(toilet) - Math.Max(0, L.X(toilet) - 2)) * T, ty = (L.Y(toilet) - Math.Max(0, L.Y(toilet) - 2)) * T;   // 변기 칸의 그림판 자리
            life.Mold[floor] = 0.5f; life.Mold[other] = 0.5f; life.Mold[toilet] = 0.5f;
            var b1 = Shot(floor); var o1 = Shot(other); var t1 = Shot(toilet);
            int moldy = Count(b0, b1, floor, Moldy), otherCh = Count(o0, o1, other, Changed);
            int edgeIn = Count(t0, t1, toilet, (xx, yy, q0, q1) => q0 != q1 && Edge(xx, yy)), edgeOut = Count(t0, t1, toilet, (xx, yy, q0, q1) => q0 != q1 && !Edge(xx, yy));
            int onBody = Count(t0, t1, toilet, (xx, yy, q0, q1) => q0 != q1 && tBody.Contains((tx + xx, ty + yy)));
            life.Mold[floor] = 0; life.Mold[other] = 0; life.Mold[toilet] = 0;
            var b2 = Shot(floor); var t2 = Shot(toilet);
            int back = Count(b0, b2, floor, Changed) + Count(t0, t2, toilet, Changed);
            Check("곰팡이 얼룩 (Mold 0.5): 가구 없는 바닥 칸에 어둡고 푸른 점 30 넘게 · 다른 가구 칸은 그대로 · 변기 칸은 줄눈 · 가장자리 4도트에만 (바뀐 점 가운데 변기 몸 점 0) · 닦으면(0) 처음 그림 그대로",
                moldy > 30 && otherCh == 0 && edgeIn > 0 && edgeOut == 0 && onBody == 0 && back == 0,
                $"바닥 검푸른 점 {moldy} · 다른 가구 칸 바뀐 점 {otherCh} · 변기 칸 줄눈 · 가장자리 {edgeIn} (그 가운데 변기 몸 위 {onBody}) · 그 밖 {edgeOut} · 닦은 뒤 바뀐 점 {back}");

            // 넓은 보기 16도트: 곰팡이 단이 덩어리 도장에 든다 → 다시 구운 덩어리에 같은 칸이 어두워진다 (한 장에 하나씩 굽는다 · 두 장)
            int keep = Painter.FarBakeMax; Painter.FarBakeMax = 1;
            try
            {
                var pf = new Painter(L) { Lighting = false };
                int fx0 = Math.Max(0, L.X(floor) - 4), fy0 = Math.Max(0, L.Y(floor) - 4);
                Img Far() { var im = pf.FrameFar(s, fx0, fy0, 9, 9, 0, 16); var copy = new Img(im.W, im.H); Array.Copy(im.Px, copy.Px, im.Px.Length); return copy; }
                int Dark(Img a, Img b) { int n = 0; for (int yy = 0; yy < 16; yy++) for (int xx = 0; xx < 16; xx++) { int gx = (L.X(floor) - fx0) * 16 + xx, gy = (L.Y(floor) - fy0) * 16 + yy; if (Sum(a.Get(gx, gy)) - Sum(b.Get(gx, gy)) > 40) n++; } return n; }
                var f0 = Far();
                life.Mold[floor] = 0.5f;
                Far(); var f1 = Far();
                life.Mold[floor] = 0;
                Far(); var f2 = Far();
                int dark1 = Dark(f0, f1), dark2 = Dark(f0, f2);
                Check("곰팡이 얼룩 넓은 보기 16도트: 곰팡이 단이 덩어리 도장에 들어 다시 구우면 같은 칸이 어두워진다 · 닦으면 처음 그림",
                    dark1 > 8 && dark2 == 0, $"어두워진 점 {dark1} · 닦은 뒤 {dark2} (16×16 중)");
            }
            finally { Painter.FarBakeMax = keep; }
        }

        // ── 곰팡이는 바닥에 핀다 (5단 A3 검토): 앞에 선 것이 가린다 — 제 칸 설비 몸 · 아래 줄 설비가 위 칸으로 솟은 몸 (변기 6 · 세면대 10 · 샤워 칸 14도트 · 배터리 · 배전반) ·
        //    가로 벽 문짝이 아래 칸에 드리운 줄 (32 · 16도트 같은 차례) · 문 칸은 문짝 위에 (32도트는 칸 덮개 · 넓은 보기는 DoorStain이 장마다) ──
        {
            var legend = new Dictionary<char, string> { ['T'] = "Toilet", ['k'] = "Sink", ['S'] = "Shower", ['b'] = "Battery", ['p'] = "PowerPanel" };
            var lay = Worlds.Ascii(MoldShip, legend);
            var s = Worlds.Full(lay, 3);
            s.Run(6);   // 빛이 한 번 잰다 (5틱마다)
            var life = s.Get<Life>(); var f = s.Get<Field>();
            int W = lay.W, H = lay.H, GW = W * Painter.T;
            const int T = Painter.T;
            var p = new Painter(lay) { Lighting = false, FrontLog = new() };   // 빛깔만 견준다
            double bakeMs = Painter.FarBakeMs; int bakeMax = Painter.FarBakeMax;
            Painter.FarBakeMs = 0; Painter.FarBakeMax = 0;   // 넓은 보기는 도장이 바뀐 덩어리를 그 장에 모두 다시 굽는다
            try
            {
                static Img Copy(Img im) { var o = new Img(im.W, im.H); Array.Copy(im.Px, o.Px, im.Px.Length); return o; }
                Img Shot() { p.FrontLog!.Clear(); return Copy(p.Frame(s, 0, 0, W, H, 0)); }
                Img Far() { p.FrameFar(s, 0, 0, W, H, 0, 16); return Copy(p.FrameFar(s, 0, 0, W, H, 0, 16)); }
                static int Sum(Rgba q) => q.R + q.G + q.B;
                // 칸 c의 도트 (칸 안 줄 r0..r1 · D는 칸당 도트) 가운데 고른 점 수
                int Cell(Img a, Img b, int c, int D, int r0, int r1, Func<Rgba, Rgba, bool> pick)
                {
                    int n = 0;
                    for (int yy = r0; yy < r1; yy++) for (int xx = 0; xx < D; xx++) if (pick(a.Get(lay.X(c) * D + xx, lay.Y(c) * D + yy), b.Get(lay.X(c) * D + xx, lay.Y(c) * D + yy))) n++;
                    return n;
                }
                static bool Changed(Rgba q0, Rgba q1) => q0 != q1;
                static bool Moldy(Rgba q0, Rgba q1) => Sum(q0) - Sum(q1) > 60 && q1.G >= q1.R;   // 기준보다 어둡고 푸르다
                static bool Dark(Rgba q0, Rgba q1) => Sum(q0) - Sum(q1) > 40;

                var a0 = Shot();
                var body = new bool[GW * H * T];   // 솟은 설비 몸의 불투명한 도트 (배 32도트 자리)
                int bodies = p.FrontLog!.Count;
                foreach (var (fimg, fx, fy) in p.FrontLog!)
                    for (int yy = 0; yy < fimg.H; yy++) for (int xx = 0; xx < fimg.W; xx++)
                        if ((uint)(fx + xx) < (uint)GW && (uint)(fy + yy) < (uint)(H * T) && fimg.Get(xx, yy).A == 255) body[(fy + yy) * GW + fx + xx] = true;
                var fixes = lay.Furniture.Where(fu => fu.Cells.Length > 0 && legend.ContainsValue(fu.Type)).ToList();
                int risen = 0;   // 위 칸으로 솟은 몸 도트 (설비 바로 위 바닥 칸 안)
                foreach (var fu in fixes) { int u = fu.Cells[0] - W; for (int yy = 0; yy < T; yy++) for (int xx = 0; xx < T; xx++) if (body[(lay.Y(u) * T + yy) * GW + lay.X(u) * T + xx]) risen++; }
                var f0 = Far();
                var floors = Enumerable.Range(0, lay.N).Where(c => lay.Kind[c] == CellKind.Floor).ToArray();
                foreach (int c in floors) life.Mold[c] = 1;
                var a1 = Shot(); var f1 = Far();
                foreach (int c in floors) life.Mold[c] = 0;
                int onBody = 0, seen = 0, onBody16 = 0, seen16 = 0;
                for (int gy = 0; gy < H * T; gy++)
                    for (int gx = 0; gx < GW; gx++)
                    {
                        if (a0.Get(gx, gy) == a1.Get(gx, gy)) continue;
                        if (body[gy * GW + gx]) onBody++; else seen++;
                    }
                for (int gy = 0; gy < H * 16; gy++)   // 16도트: 32도트 네 점이 모두 몸인 도트
                    for (int gx = 0; gx < W * 16; gx++)
                    {
                        if (f0.Get(gx, gy) == f1.Get(gx, gy)) continue;
                        bool all = body[2 * gy * GW + 2 * gx] && body[2 * gy * GW + 2 * gx + 1] && body[(2 * gy + 1) * GW + 2 * gx] && body[(2 * gy + 1) * GW + 2 * gx + 1];
                        if (all) onBody16++; else seen16++;
                    }
                // 위 칸마다 (설비 바로 위 바닥): 몸이 덮지 않은 곳에는 곰팡이가 보인다
                int aboveSeen = fixes.Min(fu => { int u = fu.Cells[0] - W, n = 0; for (int yy = 0; yy < T; yy++) for (int xx = 0; xx < T; xx++) { int gx = lay.X(u) * T + xx, gy = lay.Y(u) * T + yy; if (!body[gy * GW + gx] && a0.Get(gx, gy) != a1.Get(gx, gy)) n++; } return n; });
                Check("곰팡이는 바닥에 핀다 (바닥 칸 모두 곰팡이 1): 제 칸 · 아래 줄 설비가 위 칸으로 솟은 몸 (변기 · 세면대 · 샤워 칸 · 배터리 · 배전반)의 불투명한 점은 그대로 · 몸이 덮지 않은 곳에 핀다 (32 · 16도트)",
                    bodies >= fixes.Count && risen > 300 && onBody == 0 && onBody16 == 0 && seen > 2000 && seen16 > 400 && aboveSeen > 20,
                    $"솟은 몸 {bodies} (위 칸 안 몸 도트 {risen}) · 몸 위 바뀐 점 32도트 {onBody} · 16도트 {onBody16} · 그 밖 곰팡이 점 {seen} · {seen16} · 설비 바로 위 칸마다 몸 밖 곰팡이 점 가장 적은 {aboveSeen}");

                // 문 칸 (가로 벽 문 — 위아래 바닥): 곰팡이 0.5 → 문짝 위에 어둡고 푸른 점 (32도트 칸 덮개 · 16도트 DoorStain) · 0 → 처음 그림
                var door = lay.Doors.First(d => d.Vertical && lay.Kind[d.Cell + W] == CellKind.Floor);
                int dc = door.Cell, below = dc + W;
                bool closed = f.DoorOpen[door.Id] < 0.05f;   // 닫힌 문짝은 드리운 줄 (아래 칸 위 FaceLow줄)의 칸 너비를 다 덮는다
                life.Mold[dc] = 0.5f; var d1 = Shot(); var df1 = Far();
                life.Mold[dc] = 0; var d2 = Shot(); var df2 = Far();
                int dNear = Cell(a0, d1, dc, T, 0, T, Moldy), dFar = Cell(f0, df1, dc, 16, 0, 16, Dark), dBack = Cell(a0, d2, dc, T, 0, T, Changed) + Cell(f0, df2, dc, 16, 0, 16, Changed);
                Check("곰팡이 문 칸: 곰팡이 0.5 → 문짝 위에 어둡고 푸른 점 (32도트) · 16도트도 어두워진다 (넓은 보기는 장마다) · 0 → 처음 그림",
                    dNear > 20 && dFar > 4 && dBack == 0, $"32도트 검푸른 점 {dNear} · 16도트 어두워진 점 {dFar} · 닦은 뒤 바뀐 점 {dBack}");
                // 가로 벽 문짝이 아래 칸에 드리운 줄: 아래 칸 곰팡이는 문짝 뒤 (그 줄은 그대로) · 그 밑에는 핀다 — 32도트와 16도트가 같다
                life.Mold[below] = 0.5f; var h1 = Shot(); var hf1 = Far();
                life.Mold[below] = 0; var h2 = Far();
                int hang = Cell(a0, h1, below, T, 0, ArtWalls.FaceLow, Changed), under = Cell(a0, h1, below, T, ArtWalls.FaceLow, T, Moldy);
                int hangF = Cell(f0, hf1, below, 16, 0, ArtWalls.FaceLow / 2, Changed), underF = Cell(f0, hf1, below, 16, ArtWalls.FaceLow / 2, 16, Dark);
                Check("곰팡이 문 아래 칸: 가로 벽 문짝이 드리운 줄 (위 10줄)은 그 칸의 곰팡이를 가린다 · 그 밑에는 핀다 (32 · 16도트 같은 차례)",
                    closed && hang == 0 && under > 20 && hangF == 0 && underF > 4 && Cell(f0, h2, below, 16, 0, 16, Changed) == 0,
                    $"닫힌 문 {closed} · 드리운 줄 바뀐 점 32도트 {hang} · 16도트 {hangF} · 그 밑 검푸른 점 {under} · 어두워진 점 {underF}");
                // 넓은 보기 문 칸의 구운 곰팡이 판 (예산 고침): 곰팡이만 있는 문 칸은 곰팡이 칸마다 한 번 구운 판을 줄여 얹는다 —
                // 장마다 32도트 판을 지우고 섞어 줄이던 길 (Painter.NoMoldTile)과 같은 그림 (곰팡이 단 여섯 · 16 · 8도트 · 그림 전체)
                Img Far8() { p.FrameFar(s, 0, 0, W, H, 0, 8); return Copy(p.FrameFar(s, 0, 0, W, H, 0, 8)); }
                static bool SameImg(Img a, Img b) => a.Px.AsSpan().SequenceEqual(b.Px);
                var e0 = Far8();
                bool sameTile = true; int tileDots = 0;
                foreach (float m in new[] { 0.05f, 0.1f, 0.25f, 0.5f, 0.8f, 1f })
                {
                    life.Mold[dc] = m;
                    var t16 = Far(); var t8 = Far8();
                    Painter.NoMoldTile = true;
                    var o16 = Far(); var o8 = Far8();
                    Painter.NoMoldTile = false;
                    sameTile &= SameImg(t16, o16) && SameImg(t8, o8);
                    tileDots += Cell(f0, t16, dc, 16, 0, 16, Changed) + Cell(e0, t8, dc, 8, 0, 8, Changed);
                }
                life.Mold[dc] = 0;
                Check("곰팡이 문 칸 넓은 보기: 구운 곰팡이 판을 줄여 얹은 그림이 장마다 섞어 줄인 그림과 같다 (곰팡이 0.05 ~ 1 · 16 · 8도트 · 그림 전체)",
                    sameTile && tileDots > 30, $"같다 {sameTile} · 문 칸 바뀐 점 {tileDots}");
            }
            finally { Painter.FarBakeMs = bakeMs; Painter.FarBakeMax = bakeMax; }
        }
    }
}
