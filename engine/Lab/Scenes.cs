using ShipEngine;

namespace Lab;

/// <summary>관찰 장면 — 작은 장면을 돌려 칸 지도 그림으로 남긴다 (build/scenes/이름/틱.png).</summary>
public static partial class Program
{
    private static void Scene(string name, string outDir)
    {
        string dir = Path.Combine(outDir, name);
        Directory.CreateDirectory(dir);
        Sim s; int every, count;
        switch (name)
        {
            case "breach":
            {
                var l = Worlds.Ascii(Worlds.TwoRooms);
                s = Worlds.FieldOnly(l, 1, f =>
                {
                    f.SetDoor(0, 1f);
                    f.AddGas(Worlds.At(l, 3, 2), f.T.gSoot, 3, 300); // 연기가 조금 있던 방
                    f.SetHole(Worlds.At(l, 24, 3), 0.08f);
                });
                every = 5; count = 24;
                break;
            }
            case "ship":
            {
                var key = Layout.ShipKeys().First();
                var l = Layout.Ship(key, Tables.Default);
                s = Worlds.FieldOnly(l, 1);
                every = 1; count = 1;
                break;
            }
            case "fire":
            {
                var l = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
                int a = Worlds.At(l, 3, 3);
                s = Worlds.WithChem(l, 1, (f, c) => c.Ignite(a, 3e6, 0));
                var ff = s.Get<Field>(); var cc = s.Get<Chemistry>();
                for (int k = 0; k < 600; k++)
                {
                    if (k < 6 || k % 50 == 0)
                        Console.WriteLine($"  틱 {s.Tick}: 겉면 {ff.Tf[a]:0}K · 기체 {ff.Temp(a):0}K · 바닥 {ff.Ts[a]:0}K · 불 {cc.Burning[a]} · {cc.Hrr[a]:0}kW · 산소 {ff.Partial(ff.T.gO2, a):0.0}kPa · 고분자 {ff.Stored(ff.T.sPoly, a):0}몰 · 옆 겉면 {ff.Tf[a + 1]:0}K");
                    s.Step();
                }
                every = 25; count = 30;
                break;
            }
            case "meteor":
            {
                s = ShipWorld.Crewed("Didim", 4, 6, 3);
                s.Run(Time.Hours(2));
                int seed = s.Causes.Add(CauseKind.Story, "운석 하나가 배를 향해 온다", s.Tick);
                s.Get<Outside>().AddRock(1.5, 6000, -1, 0.15, 2, 25_000, seed);
                every = 6; count = 60;
                break;
            }
            default: Console.WriteLine($"모르는 장면: {name}"); return;
        }
        var trace = Environment.GetEnvironmentVariable("TRACE");
        var tm = s.Find<Mind>();
        for (int k = 0; k < count; k++)
        {
            Map.Save(s, Path.Combine(dir, $"{k:000}.png"));
            for (int i = 0; i < every; i++)
            {
                s.Step();
                if (trace != null && tm != null && int.TryParse(trace, out int ti) && ti < tm.Agents.Count)
                {
                    var a = tm.Agents[ti];
                    int r = tm.RoomOf(a.C.Cell);
                    Console.WriteLine($"    [{tm.NameOf(ti)}] {Time.Clock(s.Tick)} {(r >= 0 ? s.Layout.Rooms[r].Name : "문")} {tm.Thought(ti)} · {s.Get<Field>().Pressure(a.C.Cell):0}kPa · 믿음 {string.Join(",", a.Beliefs.Where(b => b.Conf >= 0.3f).Select(b => $"{Mind.BeliefName(b.Kind)}@{(tm.RoomOf(b.Cell) >= 0 ? s.Layout.Rooms[tm.RoomOf(b.Cell)].Name : "?")}:{b.Conf:0.0}{b.Src.ToString()[0]}"))}");
                }
            }
        }
        Console.WriteLine($"  {name}: {count}장 → {dir}");
        if (Environment.GetEnvironmentVariable("FACTS") == "1")
        {
            var mind = s.Find<Mind>();
            foreach (var f in s.Facts.Since(0))
            {
                if (f.Kind is FactKind.LoudSound or FactKind.Woke) continue;
                string who = f.Actor >= 0 && mind != null && f.Actor < mind.Agents.Count ? mind.NameOf(f.Actor) + " " : "";
                Console.WriteLine($"    {Time.Clock(f.Tick)} {f.Kind} {who}칸 {f.Cell} 크기 {f.Size:0.##} · {s.Causes.Story(f.Cause)}");
            }
            var sm = s.Find<ShipMind>();
            if (sm != null) foreach (var (t, txt) in sm.Log) Console.WriteLine($"    [주컴퓨터] {Time.Clock(t)} {txt}");
            if (mind != null) foreach (var a in mind.Agents) Console.WriteLine($"    {mind.NameOf(a.Id)}: {mind.Thought(a.Id)} · {(a.IsPerson ? (s.Get<Life>()[a.C.Body].Alive != 0 ? "삶" : "죽음") : "")}");
        }
    }
}

/// <summary>칸 지도 그리기: 벽 · 바닥 · 연기 · 물 · 열 · 압력 · 바람.</summary>
public static class Map
{
    public const int Px = 8;

    public static void Save(Sim s, string path, bool overlay = true)
    {
        var f = s.Get<Field>();
        var chem = s.Find<Chemistry>();
        var l = f.L;
        int w = l.W * Px, h = l.H * Px;
        var img = new uint[w * h];
        for (int i = 0; i < l.N; i++)
        {
            int cx = l.X(i), cy = l.Y(i);
            uint c;
            switch (l.Kind[i])
            {
                case CellKind.Outside: c = Png.Rgba(0.02, 0.02, 0.05); break;
                case CellKind.Wall:
                    c = f.Hole[i] > 0 ? Png.Rgba(0.9, 0.3, 0.1) : l.Hull[i] ? Png.Rgba(0.45, 0.47, 0.52) : Png.Rgba(0.35, 0.36, 0.4);
                    break;
                case CellKind.Door:
                    c = f.DoorOpen[l.DoorAt[i]] >= 0.5f ? Png.Rgba(0.3, 0.5, 0.35) : Png.Rgba(0.7, 0.6, 0.2);
                    break;
                default:
                {
                    double p = f.Pressure(i) / 101.3;
                    double t = f.Temp(i);
                    double smoke = Math.Min(1, f.Ppm(f.T.gSoot, i) / 20000);
                    double water = Math.Min(1, f.Stored(f.T.sWater, i) / 2000);
                    double r = 0.12 + 0.1 * p, g = 0.13 + 0.1 * p, b = 0.15 + 0.12 * p;
                    if (overlay)
                    {
                        double hot = Math.Clamp((t - 300) / 500, 0, 1), cold = Math.Clamp((280 - t) / 100, 0, 1);
                        r += 0.8 * hot; g += 0.3 * hot; b += 0.4 * cold;
                    }
                    r = r * (1 - smoke) + 0.05 * smoke; g = g * (1 - smoke) + 0.04 * smoke; b = b * (1 - smoke) + 0.04 * smoke;
                    b += 0.5 * water; g += 0.15 * water;
                    if (l.FurnitureAt[i] >= 0) { r += 0.08; g += 0.08; b += 0.06; }
                    if (chem != null && chem.Hrr[i] > 1) { double fl = Math.Min(1, chem.Hrr[i] / 200); r = 0.6 + 0.4 * fl; g = 0.25 + 0.45 * fl; b = 0.05; }
                    double sc = Math.Clamp((f.Tf[i] - 400) / 500, 0, 1);
                    if (sc > 0 && (chem == null || chem.Hrr[i] <= 1)) { r += 0.5 * sc; g += 0.15 * sc; }
                    c = Png.Rgba(r, g, b);
                    break;
                }
            }
            for (int y = 0; y < Px; y++) for (int x = 0; x < Px; x++) img[(cy * Px + y) * w + cx * Px + x] = c;
            // 바람 화살 (점 두 개)
            double wx = f.WindX[i], wy = f.WindY[i], sp = Math.Sqrt(wx * wx + wy * wy);
            if (overlay && sp > 0.5)
            {
                double ux = wx / sp, uy = wy / sp, len = Math.Min(Px / 2 - 1, 1 + Math.Log10(1 + sp) * 2);
                for (double k = 0; k <= len; k += 0.5)
                {
                    int px = cx * Px + Px / 2 + (int)(ux * k), py = cy * Px + Px / 2 + (int)(uy * k);
                    if (px >= 0 && py >= 0 && px < w && py < h) img[py * w + px] = Png.Rgba(0.9, 0.95, 1);
                }
            }
        }
        var lifeE = s.Find<Life>();
        var mindE = s.Find<Mind>();
        if (mindE != null)
            foreach (var a in mindE.Agents)
            {
                if (a.IsPerson) continue;
                int bx = l.X(a.C.Cell) * Px, by = l.Y(a.C.Cell) * Px;
                uint col = Png.Rgba(0.4, 0.7, 1.0);
                for (int yy = 2; yy < Px - 2; yy++) for (int xx = 2; xx < Px - 2; xx++) if ((xx + yy) % 2 == 0) img[(by + yy) * w + bx + xx] = col;
            }
        if (lifeE != null)
            foreach (var b in lifeE.Bodies)
            {
                if (b.Cell < 0) continue;
                int bx = l.X(b.Cell) * Px, by = l.Y(b.Cell) * Px;
                uint col = b.Alive == 0 ? Png.Rgba(0.4, 0.1, 0.1) : b.Conscious == 0 ? Png.Rgba(0.9, 0.7, 0.2) : Png.Rgba(0.3, 0.9, 0.5);
                for (int yy = 2; yy < Px - 2; yy++) for (int xx = 2; xx < Px - 2; xx++) img[(by + yy) * w + bx + xx] = col;
            }
        Png.Write(path, w, h, img);
    }
}
