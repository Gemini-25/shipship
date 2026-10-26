using ShipEngine;

namespace Lab;

/// <summary>규칙 시험 — 5단 '화장실 사고' (docs/STAGE5.md C16 ~): 온수기 조절기 고장 · 물 온도 …</summary>
public static partial class Program
{
    private static void WashAccidentRules()
    {
        if (!Do("화장실 사고")) return;
        Console.WriteLine("\n[화장실 사고 — 조절기 고장 (C16)]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery" };
        var lay = Worlds.Ascii(WashShip, legend);

        // C16 조절기 고장 (월드): 밸브 · 온수기를 켜고 2분 — ① 접점 붙음 → 물 325 ± 1K · 부하 3kW · 김이 오른다 / ② 열린 채 굳음 → 293K · 부하 0 · 김 0 /
        //     ③ 고장 사이 0.02시간으로 10분 → 저절로 고장 · 원인 글 '조절기' · 사실 '조절기 고장' / ④ 전기 부품을 고치면 조절기도 새것 → 313K
        {
            (double k, double demand, double steam, Sim s, int dev) Run(int fault)
            {
                var s = Worlds.Full(lay, 3);
                var net = s.Get<Network>(); var f = s.Get<Field>();
                var shower = lay.Furniture.First(x => x.Type == "Shower");
                int dev = net.Devices.First(d => d.Furniture == shower.Id).Id, sc = shower.Cells[0];
                s.Run(2);
                if (fault > 0) net.BreakThermostat(s, dev, fault == 1, s.Causes.Add(CauseKind.Setup, "시험: 온수기 조절기가 고장 났다", s.Tick, sc));
                double h0 = f.Gas(f.T.gH2O, sc);
                net.SetValve(dev, true); net.SetOn(dev, true);
                s.Run(Time.Minutes(2));
                return (net.ShowerWaterK(dev), net.Demanded, f.Gas(f.T.gH2O, sc) - h0, s, dev);
            }
            var stuck = Run(1); var open = Run(2); var ok = Run(0);
            Check("화장실 사고 (C16) 조절기: 밸브 · 온수기를 켜고 2분 — 접점 붙음은 325 ± 1K · 부하 3kW (멀쩡할 때와 같다) · 김이 오른다 / 열린 채 굳음은 293K · 부하 3kW 적다 · 김이 없다 (방 습기가 저절로 오르내리는 몫만) / 멀쩡하면 313K",
                Math.Abs(stuck.k - 325) <= 1 && stuck.steam > 0 && Math.Abs(stuck.demand - ok.demand) < 0.01 && Math.Abs(open.k - 293.15) < 0.5 && Math.Abs(ok.demand - open.demand - 3) < 0.05 && open.steam < 0.05 * stuck.steam && Math.Abs(ok.k - 313) < 0.5,
                $"붙음 {stuck.k:0.0}K · 김 +{stuck.steam:0.00}몰 · 부하 {stuck.demand:0.00}kW / 열림 {open.k:0.0}K · 김 +{open.steam:0.0000} · 부하 {open.demand:0.00}kW / 멀쩡 {ok.k:0.0}K · 부하 {ok.demand:0.00}kW");

            var s3 = Worlds.Full(lay, 3);
            var n3 = s3.Get<Network>();
            var sh3 = lay.Furniture.First(x => x.Type == "Shower");
            int d3 = n3.Devices.First(d => d.Furniture == sh3.Id).Id;
            s3.Run(2);
            n3.ThermoMtbfH = 0.02;
            n3.SetValve(d3, true); n3.SetOn(d3, true);
            for (int k = 0; k < (int)Time.Minutes(10) && (n3.ThermoFault.Length <= d3 || n3.ThermoFault[d3] == 0); k++) s3.Step();
            byte fault = n3.ThermoFault.Length > d3 ? n3.ThermoFault[d3] : (byte)0;
            string why = fault != 0 ? s3.Causes.Text(n3.ThermoCause[d3]) : "";
            bool fact = s3.Facts.Since(0).Any(x => x.Kind == FactKind.ThermoStuck && x.Other == d3);
            n3.Repair(d3, Part.Electric);
            n3.SetValve(d3, true); n3.SetOn(d3, true);
            double fixedK = n3.ShowerWaterK(d3);
            Check("화장실 사고 (C16) 조절기: 고장 사이 0.02시간이면 데우며 흐르는 10분 안에 저절로 고장 난다 (원인 글 '조절기' · 사실 '조절기 고장') · 전기 부품을 고치면 조절기도 새것 (313K)",
                fault is 1 or 2 && why.Contains("조절기") && fact && n3.ThermoFault[d3] == 0 && Math.Abs(fixedK - 313) < 0.5,
                $"고장 {fault} · '{why}' · 사실 {fact} · 고친 뒤 {n3.ThermoFault[d3]} · {fixedK:0.0}K");
        }
    }
}
