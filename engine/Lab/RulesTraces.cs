using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>재질 흔적 규칙 시험 (그래픽 결정 5) — 녹 · 실금 · 쏟아진 액체는 세계의 값에서만 · 값이 줄면(수리 · 청소) 흔적도 사라진다.</summary>
public static partial class Program
{
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
            var b0 = Shot(floor); var o0 = Shot(other); var t0 = Shot(toilet);
            life.Mold[floor] = 0.5f; life.Mold[other] = 0.5f; life.Mold[toilet] = 0.5f;
            var b1 = Shot(floor); var o1 = Shot(other); var t1 = Shot(toilet);
            int moldy = Count(b0, b1, floor, Moldy), otherCh = Count(o0, o1, other, Changed);
            int edgeIn = Count(t0, t1, toilet, (xx, yy, q0, q1) => q0 != q1 && Edge(xx, yy)), edgeOut = Count(t0, t1, toilet, (xx, yy, q0, q1) => q0 != q1 && !Edge(xx, yy));
            life.Mold[floor] = 0; life.Mold[other] = 0; life.Mold[toilet] = 0;
            var b2 = Shot(floor); var t2 = Shot(toilet);
            int back = Count(b0, b2, floor, Changed) + Count(t0, t2, toilet, Changed);
            Check("곰팡이 얼룩 (Mold 0.5): 가구 없는 바닥 칸에 어둡고 푸른 점 30 넘게 · 다른 가구 칸은 그대로 · 변기 칸은 줄눈 · 가장자리 4도트에만 · 닦으면(0) 처음 그림 그대로",
                moldy > 30 && otherCh == 0 && edgeIn > 0 && edgeOut == 0 && back == 0,
                $"바닥 검푸른 점 {moldy} · 다른 가구 칸 바뀐 점 {otherCh} · 변기 칸 줄눈 · 가장자리 {edgeIn} · 그 밖 {edgeOut} · 닦은 뒤 바뀐 점 {back}");

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
    }
}
