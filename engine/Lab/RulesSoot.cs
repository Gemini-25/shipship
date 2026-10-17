using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>② 물질 — 그을음이 겉면에 내려앉는다 (바닥 · 천장 · 벽 넓이만큼) · 그림에 드러난다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void SootRules()
    {
        if (!Do("그을음")) return;
        Console.WriteLine("\n[② 그을음]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 불 뒤 그을음이 남는다: 휴게실 의자에 불 → 한 시간 뒤 그 방 벽에 그을음이 묻고 (먼 방 벽보다 훨씬 짙게) · 그림에서 그을린 방이 어둡다 ──
        {
            var s = ShipWorld.Create("Didim", 9);
            var f = s.Get<Field>(); var L = s.Layout; var ch = s.Get<Chemistry>();
            var room = CommonRoom(L);
            int fire = room.Cells.First(c => L.FurnitureAt[c] >= 0);
            s.Run(5);
            ch.Ignite(fire, 3e6, s.Causes.Add(CauseKind.Story, "의자 밑에서 불씨가 튀었다", s.Tick));
            s.Run(Time.Hours(1));
            List<int> WallsOf(RoomDef r)
            {
                var w = new HashSet<int>(); var nb = new int[4];
                foreach (int c in r.Cells) { L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Wall) w.Add(j); }
                return w.OrderBy(j => j).ToList();
            }
            var near = WallsOf(room);
            int fx = L.X(fire), fy = L.Y(fire);
            var far = L.Rooms.Where(r => r.Cells.Length > 4).OrderByDescending(r => r.Cells.Min(c => Math.Abs(L.X(c) - fx) + Math.Abs(L.Y(c) - fy))).First();
            double nearG = near.Average(j => f.SootGPerM2(j)), farG = WallsOf(far).Average(j => f.SootGPerM2(j));
            double floorG = room.Cells.Average(c => f.SootGPerM2(c));

            // 그림: 같은 세계에서 그을음만 걷어 낸 복제와 견준다 (빛 · 연기 · 불은 같다)
            int x0 = room.Cells.Min(L.X) - 1, y0 = room.Cells.Min(L.Y) - 1, w = room.Cells.Max(L.X) - x0 + 2, h = room.Cells.Max(L.Y) - y0 + 2;
            var clean = s.Clone(); var cf = clean.Get<Field>();
            for (int i = 0; i < cf.Cells; i++) cf.TakeStore(i, cf.T.sSoot, cf.Stored(cf.T.sSoot, i));
            double Bright(Sim w0) { var img = new Painter(L).Frame(w0, x0, y0, w, h, 0); double sum = 0; for (int y = 0; y < img.H; y++) for (int x = 0; x < img.W; x++) { var p = img.Get(x, y); sum += p.R + p.G + p.B; } return sum / (3.0 * img.W * img.H); }
            double sooty = Bright(s), bare = Bright(clean);
            if (Debug) Console.WriteLine($"    {room.Name}: 벽 {near.Count}칸 · 바닥 {floorG:0.00} · 벽 {nearG:0.00} · 먼 방({far.Name}) 벽 {farG:0.000} g/m² · 밝기 {sooty:0.0} / {bare:0.0}");
            Check("불 뒤 그을음이 남는다: 휴게실 의자에 불 → 한 시간 뒤 그 방 벽 · 바닥에 그을음 (먼 방 벽보다 다섯 배 넘게) · 그림에서 그을린 방이 어둡다",
                nearG > 0.05 && nearG > farG * 5 && floorG > 0.05 && sooty < bare * 0.97,
                $"{room.Name} 벽 {nearG:0.00} · 바닥 {floorG:0.00} g/m² · 먼 방({far.Name}) 벽 {farG:0.000} · 그림 밝기 {sooty:0} / 그을음 걷으면 {bare:0}");
        }
    }
}
