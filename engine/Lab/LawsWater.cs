using ShipEngine;

namespace Lab;

/// <summary>물 보존 (검토 ④-3): 냄비에 담는 물은 물탱크에서 온다 — 끓어 김이 되고 바닥에 맺혀도 · 다 졸아 음식이 눌어도 물의 합은 같다 (예전엔 냄비 물이 없는 데서 생겼다 · 하루 5L).</summary>
public static partial class Program
{
    private static void WaterLaws()
    {
        if (!Do("물")) return;
        Console.WriteLine("\n[물 보존 — 냄비]");
        var legend = Worlds.MindLegend; legend['k'] = "Stove";
        var lay = Worlds.Ascii(Worlds.MindShip.Replace("#m.r..........w.....m...#", "#m.r..........w.....mk..#"), legend);
        var s = Worlds.Full(lay, 1);
        var net = s.Get<Network>(); var f = s.Get<Field>();
        s.Run(3);
        var stove = net.Devices.First(d => d.Def.Role == "stove");
        double Water()
        {
            double w = net.WaterTank + net.WasteWater + f.Total(f.T.gH2O);
            for (int c = 0; c < lay.N; c++) w += f.Stored(f.T.sWater, c) + 6 * f.Stored(f.T.sFood, c);   // 음식(포도당)은 눌거나 썩으면 물 6몰이 된다 — 그만큼 물로 센다
            foreach (var d in net.Devices) w += net.PanWater[d.Id];
            return w;
        }
        double w0 = Water(), tank0 = net.WaterTank, book0 = s.Book.BulkFlow[(int)Res.Water * Book.Flows + (int)Flow.Cooking];
        double took = net.FillPan(s, stove.Id);
        double afterFill = Water(), pan = net.PanWater[stove.Id];
        net.SetOn(stove.Id, true);
        s.Run(Time.Minutes(40));
        net.SetOn(stove.Id, false);
        s.Run(Time.Minutes(5));
        double w1 = Water(), boiled = pan - net.PanWater[stove.Id];
        double book = s.Book.BulkFlow[(int)Res.Water * Book.Flows + (int)Flow.Cooking] - book0;
        Check("냄비 물은 물탱크에서 온다 (1L) · 장부에 '냄비에 담았다'로 적힌다", took > 55 && Math.Abs(tank0 - net.WaterTank - took) < 1e-9 && Math.Abs(book + took) < 1e-9 && Math.Abs(afterFill - w0) < 1e-9,
            $"담음 {took:0.0}몰 · 탱크 {tank0:0.0}→{net.WaterTank:0.0} · 장부 {book:0.0}");
        Check("물 보존: 탱크 · 오수 · 냄비 · 공기의 김 · 바닥 물 (+ 음식 속 물 6배)의 합이 끓이고 졸아 눌어도 같다 (조리대 40분)", boiled > 1 && Math.Abs(w1 - w0) < 1e-6 * w0,
            $"졸아든 물 {boiled:0.0}몰 · 합 {w0:0.000} → {w1:0.000}몰 ({w1 - w0:+0.0e0;-0.0e0;0})");
    }
}
