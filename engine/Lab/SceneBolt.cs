using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>
/// 관찰 장면 '볼트' (docs/PHYSICS.md PH10 · `Lab --scene unbolt` · `--scene props-blast`):
/// unbolt: 화살호 냉각 펌프 둘 — 하나는 볼트가 풀렸고 (단 2) · 하나는 조였다 (단 10). 다른 배와 부딪혀 4m/s 덜컹 — 풀린 펌프는 뜯겨 미끄러지고 꺼진다 · 조인 펌프는 그대로 돈다.
/// 두 펌프를 한 둘레에 — 덜컹 앞 · 첫 틱 1/4 · 1/2 · 끝 · 셋째 틱 · 30틱.
/// props-blast: 탁자 (위에 컵 · 접시) · 벽 선반이 있는 방 — 닫힌 문 바로 앞에서 배터리가 터진다: 탁자는 밀리고 · 선반은 뜯기고 · 컵은 깨지고 · 문짝은 폭풍의 반대쪽 방으로 날아간다.
/// </summary>
public static partial class Program
{
    private static void UnboltScene(string dir)
    {
        var s = ShipWorld.Create("Hwasal", 3);
        var st = s.Get<Structure>(); var net = s.Get<Network>(); var L = s.Layout;
        var pumps = Enumerable.Range(0, net.Devices.Count).Where(d => net.Devices[d].Type == "CoolantPump").ToList();
        int loose = pumps[0], tight = pumps[1];
        int fl = net.Devices[loose].Furniture, ft = net.Devices[tight].Furniture;
        s.Run(30);
        net.Loose[loose] = 1.2f; net.Loose[tight] = 1.2f; net.Tighten(tight);
        s.Run(2);
        st.EnsureBody();
        st.Track = new MotionTrack();
        // 두 펌프를 함께 담는 둘레 (양옆 두 칸 · 위아래 두 칸)
        var both = L.Furniture[fl].Cells.Concat(L.Furniture[ft].Cells).ToList();
        int x0 = Math.Max(0, both.Min(L.X) - 2), y0 = Math.Max(0, both.Min(L.Y) - 2);
        int cw = Math.Min(L.W - x0, both.Max(L.X) + 3 - x0), ch = Math.Min(L.H - y0, both.Max(L.Y) + 3 - y0);
        const int zoom = 2, gap = 6, cols = 3;
        var painter = new Painter(L);
        (int tick, double f)[] at = { (0, 1), (1, 0.25), (1, 0.5), (1, 1), (3, 1), (30, 1) };
        int fw = cw * Painter.T * zoom, fh = ch * Painter.T * zoom;
        var sheet = new Img(cols * (fw + gap) - gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        Console.WriteLine($"  unbolt: 화살호 냉각 펌프 ({L.X(L.Furniture[fl].Cells[0])}, {L.Y(L.Furniture[fl].Cells[0])}) 볼트 단 {st.FurnAnchor[fl]} · ({L.X(L.Furniture[ft].Cells[0])}, {L.Y(L.Furniture[ft].Cells[0])}) 단 {st.FurnAnchor[ft]} · 돈다 {net.Working(loose)} · {net.Working(tight)}");
        int done = 0;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k].tick)
            {
                if (done == 0) { var o = st.Bodies[0]; st.Push(0, o.X, o.Y, o.Mass * 4, 0, s.Causes.Add(CauseKind.Setup, "다른 배와 부딪혔다", s.Tick)); }
                s.Step(); done++;
            }
            var img = done == 0 ? painter.Frame(s, x0, y0, cw, ch, k).Scale(zoom) : SceneFrame(painter, s, at[k].f, x0, y0, cw, ch, done * 8 + k, zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            int pid = st.FurnProp[fl], i = pid >= 0 ? st.IndexOf(pid) : -1;
            string where = i >= 0 ? $"물체 ({st.Props[i].X:0.00}, {st.Props[i].Y:0.00}) · {Math.Sqrt(st.Props[i].Vx * st.Props[i].Vx + st.Props[i].Vy * st.Props[i].Vy):0.00}m/s · 누움 {st.Props[i].Pose}" : $"칸 {st.FurnAt[fl]}";
            if (k == 1 && pid >= 0) { var keys = st.Track!.Of(MotionTrack.WhoProp, pid); if (keys.Length > 0) Console.WriteLine($"  unbolt 뜯긴 틱 첫 열쇠틀: ({keys[0].X:0.00}, {keys[0].Y:0.00}) · 빠르기 ({keys[0].Vx:0.00}, {keys[0].Vy:0.00})m/s"); }
            Console.WriteLine($"  unbolt {done}틱 + {at[k].f:0.00}: 풀린 펌프 붙음 {st.FurnPlaced[fl]} · {where} · 돈다 {net.Working(loose)} / 조인 펌프 붙음 {st.FurnPlaced[ft]} · 돈다 {net.Working(tight)}");
        }
        foreach (var x in s.Facts.Since(0).Where(x => x.Kind == FactKind.TornLoose)) Console.WriteLine($"  unbolt 사실: {s.Causes.Story(x.Cause)}");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "unbolt.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  unbolt: {dir}/unbolt.png ({sheet.W}×{sheet.H}) — 오른쪽 풀린 펌프 (뜯겨 왼쪽으로 미끄러진다) · 왼쪽 조인 펌프: 덜컹 앞 · 첫 틱 1/4 · 1/2 · 끝 · 셋째 틱 · 30틱");
    }

    private static void PropsBlastScene(string dir)
    {
        // 왼쪽 방: 탁자 (위에 컵 · 접시) · 벽 선반 · 닫힌 문 너머 오른쪽 방 (두 방 다 1기압) — 문 바로 앞에서 배터리가 터진다
        const string art = "\n######################\n#ss.......#..........#\n#.........#..........#\n#..tt.....D..........#\n#..tt.....#..........#\n#.........#..........#\n######################";
        var L = Worlds.Ascii(art, new Dictionary<char, string> { ['s'] = "Shelf", ['t'] = "Table" });
        var s = FieldStructure(L, 5);
        var st = s.Get<Structure>(); var f = s.Get<Field>();
        int dc = L.Doors[0].Cell;
        f.SetDoor(0, 0); st.RequestDoor(0, 0);
        int decor = st.Decorate(s);
        s.Step();
        int boom = L.Index(L.X(dc) - 1, L.Y(dc));
        // 문 칸에 첨두 95kPa (문은 120kPa를 버틴다 — 벽 · 문 칸은 거리 넷으로 센다 · 두 배로 되튀어 1.5배 넘게 밀리면 문짝이 떨어져 난다)
        double w3 = 1;
        { double lo = 0.01, hi = 5; for (int k = 0; k < 60; k++) { double mid = (lo + hi) / 2; if (Structure.BlastKPa(4 / mid) < 95) lo = mid; else hi = mid; } w3 = (lo + hi) / 2; }
        double joules = w3 * w3 * w3 * Structure.TntJ;
        st.Track = new MotionTrack();
        var painter = new Painter(L);
        const int zoom = 1, gap = 6, cols = 2;
        int x0 = 0, y0 = 0, cw = L.W, ch = L.H;
        (int tick, double f)[] at = { (0, 1), (1, 0.25), (1, 0.5), (1, 1), (2, 1), (6, 1) };
        int fw = cw * Painter.T * zoom, fh = ch * Painter.T * zoom;
        var sheet = new Img(cols * (fw + gap) - gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        var furn = Enumerable.Range(0, L.Furniture.Count).ToList();
        Console.WriteLine($"  props-blast: 가구 {string.Join(" · ", furn.Select(x => $"{L.Furniture[x].Type} {st.FurnitureKg(x):0}kg"))} · 꾸밈 {decor} · 터지는 칸 ({L.X(boom)}, {L.Y(boom)}) {joules / 1e6:0.00}MJ");
        int done = 0;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k].tick)
            {
                if (done == 0) st.Blast(s, boom, joules, s.Causes.Add(CauseKind.Setup, "배터리가 터졌다", s.Tick));
                s.Step(); done++;
                if (done == 1)
                    foreach (var q in st.Props.Where(q => q.Kind is 1 or 3))
                    {
                        var keys = st.Track.Of(MotionTrack.WhoProp, q.Id);
                        if (keys.Length > 0) Console.WriteLine($"  props-blast 첫 틱: {st.PropName(q)} 처음 빠르기 ({keys[0].Vx:0.0}, {keys[0].Vy:0.0})m/s");
                    }
            }
            var img = done == 0 ? painter.Frame(s, x0, y0, cw, ch, k).Scale(zoom) : SceneFrame(painter, s, at[k].f, x0, y0, cw, ch, done * 8 + k, zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            int off = furn.Count(x => st.FurnPlaced[x] == 0), broken = furn.Count(x => st.FurnBroken[x] != 0);
            Console.WriteLine($"  props-blast {done}틱 + {at[k].f:0.00}: 제자리 떠난 가구 {off} (부서짐 {broken}) · 문짝 {st.Props.Count(p => p.Kind == 3)} · 조각 {st.Props.Count(p => p.Kind == 2)} · 깨짐 {st.Shattered} · 가루 {st.GritKg * 1000:0}g · 깨어 있음 {st.Props.Count(p => Structure.Awake(p))} · 날아간 문 {st.DoorsBlown}");
        }
        foreach (var x in furn) Console.WriteLine($"  props-blast {L.Furniture[x].Type}: 제자리 {st.FurnPlaced[x]} · 부서짐 {st.FurnBroken[x]} · 놓인 칸 {st.FurnAt[x]}{(st.FurnAt[x] >= 0 ? $" ({L.X(st.FurnAt[x])}, {L.Y(st.FurnAt[x])})" : "")}");
        foreach (var q in st.Props.Where(q => q.Kind == 3)) { var lf = st.PropForm(q); Console.WriteLine($"  props-blast 문짝: ({q.X:0.00}, {q.Y:0.00}) {q.Kg:0}kg · 누움 {q.Pose} · 각 {q.Th:0.00} · 꼴 {lf.W}×{lf.D}×{lf.H}"); }
        foreach (var x in s.Facts.Since(0).Where(x => x.Kind is FactKind.TornLoose or FactKind.DoorBlown or FactKind.Shattered).Take(12)) Console.WriteLine($"  props-blast 사실 {x.Kind}: {s.Causes.Text(x.Cause)}");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "props-blast.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  props-blast: {dir}/props-blast.png ({sheet.W}×{sheet.H}) — 탁자 · 선반 · 문 곁 폭발: 앞 · 첫 틱 1/4 · 1/2 · 끝 · 둘째 틱 · 여섯째 틱");
    }
}
