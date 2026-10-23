using ShipEngine;

namespace Lab;

/// <summary>미량 기체 정화 세 단 (docs/ENGINE.md ⑥ · 검토 ④-1): 숯이 차면 주컴퓨터가 거르개 갈기를 맡긴다 · 배에 퍼진 독한 기체를 몇 시간 안에 걷는다.</summary>
public static partial class Program
{
    private static void ScrubRules()
    {
        if (!Do("정화")) return;
        Console.WriteLine("\n[⑥ 미량 기체 정화]");
        // ── 숯이 차면: 찬 몫만큼 거르개가 닳은 것으로 쳐서 주컴퓨터의 예방 정비가 로봇에게 갈기를 맡긴다 → 갈면 다시 거른다 ──
        {
            var legend = Worlds.MindLegend; legend['x'] = "Scrubber";
            var lay = Worlds.Ascii(Worlds.MindShipComputer.Replace("#.........#.............#\n#..p.b....D", "#....x....#.............#\n#..p.b....D"), legend);
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var net = s.Get<Network>(); var f = s.Get<Field>();
            int dock = Worlds.At(lay, 3, 7);
            m.AddRobot(s, dock, AgentKind.Robot, dock);
            int dev = net.Devices.First(d => d.Def.Role == "scrubber").Id;
            net.FillBed(dev, 0.9);
            s.Run(Time.Hours(2));
            double fill = net.CarbonFill(dev);
            var room = lay.Rooms[lay.Room[net.Devices[dev].Cell]].Cells.Where(c => f.Moles(c) > 1).ToArray();
            foreach (int c in room) f.AddGas(c, f.T.gNH3, 0.3 / room.Length, f.Temp(c));
            double nh0 = f.Total(f.T.gNH3);
            s.Run(Time.Minutes(20));
            double left = f.Total(f.T.gNH3) / nh0;
            var why = s.Facts.Since(0).Where(x => x.Kind == FactKind.Repaired).Select(x => s.Causes.Text(x.Cause)).FirstOrDefault(t => t.Contains("거르개")) ?? "";
            Check("숯이 차면 (90%) 주컴퓨터가 거르개 갈기를 로봇에게 맡기고 · 갈면 다시 거른다 (암모니아 20분에 반 넘게)",
                fill < 0.1 && left < 0.5 && why != "", $"숯 90% → {fill:P0} · 암모니아 남은 몫 {left:P0} · {why}");
        }
        // ── 배에 퍼진 독한 기체: 디딤호 침실에 시안화수소 · 염화수소 · 메탄 — 덕트가 정화기로 돌려 몇 시간 안에 걷힌다 (예전엔 30시간 넘게 그대로) ──
        {
            var s = ShipWorld.Crewed("Didim", 3, 2, 0);
            var f = s.Get<Field>();
            s.Run(30);
            var bed = s.Layout.Furniture.First(x => x.Type == "Bed").Cells[0];
            var cells = s.Layout.Rooms[s.Layout.Room[bed]].Cells.Where(c => f.Moles(c) > 1).ToArray();
            foreach (int c in cells) { f.AddGas(c, f.T.gHCN, 0.5 / cells.Length, f.Temp(c)); f.AddGas(c, f.T.gHCl, 1.0 / cells.Length, f.Temp(c)); f.AddGas(c, f.T.gCH4, 2.0 / cells.Length, f.Temp(c)); }
            double hcn0 = f.Total(f.T.gHCN), hcl0 = f.Total(f.T.gHCl), ch40 = f.Total(f.T.gCH4);
            s.Run(Time.Hours(1));
            double hcn = f.Total(f.T.gHCN) / hcn0, hcl = f.Total(f.T.gHCl) / hcl0, ch4 = f.Total(f.T.gCH4) / ch40;
            Check("배에 퍼진 독한 기체 (시안화수소 · 염화수소 · 메탄)가 1시간에 반 넘게 걷힌다 — 덕트가 정화기의 숯 · 흡착제 · 촉매로 돌린다",
                hcn < 0.5 && hcl < 0.5 && ch4 < 0.5, $"남은 몫 시안화수소 {hcn:P0} · 염화수소 {hcl:P0} · 메탄 {ch4:P0}");
        }
    }
}
