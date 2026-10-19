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
            int Count(Img img, int c, int dy, Func<Rgba, bool> pick)
            {
                int n = 0;
                for (int yy = 0; yy < Painter.T; yy++) for (int xx = 0; xx < Painter.T; xx++) if (pick(img.Get((L.X(c) - x0) * Painter.T + xx, (L.Y(c) - y0 + dy) * Painter.T + yy))) n++;
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
    }
}
