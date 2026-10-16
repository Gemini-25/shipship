using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>새 화면 규칙 시험 — 그리기는 세계를 읽기만 하고, 세계의 상태가 그림에 드러난다.</summary>
public static partial class Program
{
    private static void ViewRules()
    {
        if (!Do("화면")) return;
        Console.WriteLine("\n[화면]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var s = Worlds.Full(lay, 3);
        var m = s.Get<Mind>();
        m.AddPerson(s, At(15, 2)); m.AddRobot(s, At(20, 7), AgentKind.Robot, At(20, 7));
        s.Run(10);
        s.Get<Chemistry>().Ignite(At(3, 5), 3e6, s.Causes.Add(CauseKind.Story, "침대 불씨", s.Tick));
        var f = s.Get<Field>();
        foreach (int c in lay.Rooms[lay.Room[At(17, 2)]].Cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * 0.95);
        s.Run(20);
        var before = s.Save();
        var p = new Painter(lay);
        var img = p.Frame(s, 0, 0, lay.W, lay.H, (int)s.Tick);
        bool same = before.AsSpan().SequenceEqual(s.Save());
        // 불 칸 둘레에 불꽃 색 (붉고 밝은 도트) · 빠진 방은 서리 빛(파랗게)
        int fire = 0;
        var chem = s.Get<Chemistry>();
        int burning = Enumerable.Range(0, lay.N).FirstOrDefault(c => chem.Burning[c] != 0, -1);
        int fx = (burning >= 0 ? lay.X(burning) : 0) * Painter.T, fy = (burning >= 0 ? lay.Y(burning) : 0) * Painter.T;
        for (int y = fy - 10; y < fy + Painter.T; y++)
            for (int x = fx; x < fx + Painter.T; x++)
            {
                var c = img.Get(x, y);
                if (c.R > 200 && c.G > 80 && c.B < 90) fire++;
            }
        // 같은 바닥(보통 방)끼리 견준다: 빠진 오른쪽 방 · 멀쩡한 아래 통로 — 칸 안쪽의 평균 파란 값
        double Blue(int cell) { double b = 0; int n = 0; for (int y = 6; y < 26; y++) for (int x = 6; x < 26; x++) { b += img.Get(lay.X(cell) * Painter.T + x, lay.Y(cell) * Painter.T + y).B; n++; } return b / n; }
        double vac = Blue(At(19, 4)), ok = Blue(At(19, 8));
        if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
        {
            var ch = s.Get<Chemistry>();
            for (int c = 0; c < lay.N; c++) if (ch.Burning[c] != 0) Console.WriteLine($"    불 ({lay.X(c)},{lay.Y(c)}) {ch.Hrr[c]:0}kW");
            Png.WriteRgba("build/shots/rule-view.png", img.W, img.H, img.Px);
        }
        Check("새 화면: 그리기는 세계를 바꾸지 않는다 (그리기 전후 바이트까지 같다) · 불 칸에 불꽃 · 빠진 방은 서리 빛", same && burning >= 0 && fire > 15 && vac > ok + 15,
            $"같음 {same} · 불꽃 도트 {fire} · 파란 값 빠진 방 {vac:0} / 통로 {ok:0} · {img.W}×{img.H}");
    }
}
