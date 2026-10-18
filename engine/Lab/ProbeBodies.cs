using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>④ 칸 강체 관찰: `--bodies [분] [hole|nohole] [burn g]` — 아령 배의 통로를 끊고 (오른쪽 방 윗벽 끝에 구멍) 조각이 뿜으며 도는 것을 30초마다 찍는다 · 그림은 build/shots/bodies-*.png.</summary>
public static partial class Program
{
    private static void ProbeBodies(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--bodies").Skip(1).ToArray();
        int minutes = rest.Length > 0 && int.TryParse(rest[0], out int m) ? m : 5;
        bool hole = !rest.Contains("nohole");
        double burn = rest.Contains("burn") ? double.Parse(rest[Array.IndexOf(rest, "burn") + 1]) : 0;
        var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
        var s = Worlds.Full(lay, 2);
        s.Get<Mind>().Crew(s, 2, 0, 0);
        var st = s.Get<Structure>(); var f = s.Get<Field>();
        s.Run(Time.Minutes(1));
        int cause = s.Causes.Add(CauseKind.Story, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
        for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
        if (hole) st.Destroy(s, Worlds.At(lay, 18, 0), s.Causes.Add(CauseKind.Story, "파편이 오른쪽 방 윗벽을 뚫었다", s.Tick));
        var p = new Painter(lay);
        Directory.CreateDirectory("build/shots");
        void Shot(string name)
        {
            var img = p.Frame(s, -12, -14, lay.W + 24, lay.H + 28, (int)s.Tick);
            Png.WriteRgba($"build/shots/bodies-{name}.png", img.W, img.H, img.Px);
        }
        Shot("0");
        for (int i = 1; i <= 9; i++) { s.Step(); if (i % 3 == 0) Shot($"t{i}"); }
        int room = lay.Room[Worlds.At(lay, 15, 3)];
        for (int k = 1; k <= minutes * 2; k++)
        {
            for (int i = 0; i < 13; i++)
            {
                if (burn > 0) { st.ShipAx = burn * 9.81; st.ShipAy = 0; }
                s.Step();
            }
            if (st.Bodies.Count < 2) { Console.WriteLine($"{k * 30}초: 몸 없음 (조각 {st.Fragments.Count})"); continue; }
            var b = st.Bodies[1]; var b0 = st.Bodies[0];
            var (mp, _, l, ke) = st.BodyMomentum();
            Console.WriteLine($"{s.Tick * Time.SecondsPerTick / 60,5:0.0}분 · 조각 {b.Mass / 1000:0.0}t 거리 {st.Fragments[0].Dist:0.0}m 빠르기 {Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy):0.00}m/s · 자세 {b.Th * 180 / Math.PI:0}° 돎 {b.W * 60 / (2 * Math.PI):0.00}바퀴/분 · " +
                $"배 {b0.Mass / 1000:0.0}t v {Math.Sqrt(b0.Vx * b0.Vx + b0.Vy * b0.Vy):0.000} · 방 {f.Pressure(lay.Rooms[room].Cells[0]):0.0}kPa · 기체 운동량 {Math.Sqrt(st.GasPx * st.GasPx + st.GasPy * st.GasPy) / 1000:0.0}kN·s · 몸 운동량+기체 {(mp + st.GasPx - st.BurnPx):0.000} · 잔걸음 {st.BodySteps}");
            if (k == 2 || k == minutes || k == minutes * 2) Shot($"{k}");
        }
        foreach (var x in s.Facts.Since(0).Where(x => x.Kind is FactKind.Tumble or FactKind.ShipSplit)) Console.WriteLine($"  [{FactNames.Of(x.Kind)}] {s.Causes.Story(x.Cause)}");
        var people = s.Get<Mind>().Agents.Where(a => a.IsPerson).Select(a => $"{a.Id}: 몸 {st.BodyOf(a.C.Cell)} · 배 틀 {st.ShipFrameOf(a.C.Cell).x:0.0},{st.ShipFrameOf(a.C.Cell).y:0.0}");
        Console.WriteLine("  사람 " + string.Join(" / ", people));
    }
}
