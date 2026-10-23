using ShipEngine;

namespace Lab;

/// <summary>미량 기체 정화 세 단 (docs/ENGINE.md ⑥ · 검토 ④-1): 활성탄 · 촉매 산화 · 산성 기체 흡착 — 원소는 장과 거르개 안에 그대로 · 붙잡히는 곳이 맞다 · 다 찬 숯은 못 거른다.</summary>
public static partial class Program
{
    private const string ScrubShip = @"
###########
#.........#
#..s...p..#
#......b..#
###########";

    private static void ScrubLaws()
    {
        if (!Do("정화")) return;
        Console.WriteLine("\n[⑥ 미량 기체 정화 세 단]");
        var legend = new Dictionary<char, string> { ['s'] = "Scrubber", ['p'] = "PowerPanel", ['b'] = "Battery" };
        var lay = Worlds.Ascii(ScrubShip, legend);
        (Sim s, Network net, Field f, int dev) Room()
        {
            var s = Worlds.Full(lay, 1);
            var net = s.Get<Network>(); var f = s.Get<Field>();
            s.Run(3);
            int dev = net.Devices.First(d => d.Def.Role == "scrubber").Id;
            int[] gases = { f.T.gNH3, f.T.gHCN, f.T.gAroma, f.T.gO3, f.T.gCO, f.T.gH2, f.T.gCH4, f.T.gHCl, f.T.gHF };
            double[] mol = { 0.5, 0.5, 0.2, 0.05, 1.0, 1.0, 0.5, 0.5, 0.3 };
            var cells = lay.Rooms[0].Cells.Where(c => f.Moles(c) > 1).ToArray();
            for (int k = 0; k < gases.Length; k++) foreach (int c in cells) f.AddGas(c, gases[k], mol[k] / cells.Length, f.Temp(c));
            return (s, net, f, dev);
        }
        // 원소: 장 + 거르개 안 + 갈아 낸 거르개 + 이산화탄소 통
        double[] Elements(Sim s, Network net, Field f)
        {
            var el = f.ElementTotals();
            var held = net.ScrubHeld();
            held[f.T.Gases[f.T.gCO2].Index] += net.CO2Store + net.VentedCO2;
            var e2 = f.T.ElementsOf(held);
            for (int i = 0; i < el.Length; i++) el[i] += e2[i];
            return el;
        }
        {
            var (s, net, f, dev) = Room();
            var e0 = Elements(s, net, f);
            double o20 = f.Total(f.T.gO2), en0 = f.EnergyLedger();
            double[] before = { f.Total(f.T.gNH3), f.Total(f.T.gHCN), f.Total(f.T.gAroma), f.Total(f.T.gO3), f.Total(f.T.gCO), f.Total(f.T.gH2), f.Total(f.T.gCH4), f.Total(f.T.gHCl), f.Total(f.T.gHF) };
            s.Run(Time.Minutes(30));
            var e1 = Elements(s, net, f);
            double worst = e0.Zip(e1).Max(p => Math.Abs(p.First - p.Second) / Math.Max(1, Math.Abs(p.First)));
            Check("정화 세 단: 원소 총량(장 + 숯 · 흡착제 안 + 이산화탄소 통)이 같다 (기체 아홉 · 30분)", worst < 1e-9, $"가장 큰 차이 {worst:0.0e0}");
            double Left(int g, int k) => f.Total(g) / before[k];
            bool gone = Left(f.T.gNH3, 0) < 0.05 && Left(f.T.gHCN, 1) < 0.05 && Left(f.T.gAroma, 2) < 0.05 && Left(f.T.gO3, 3) < 0.05
                && Left(f.T.gCO, 4) < 0.05 && Left(f.T.gH2, 5) < 0.05 && Left(f.T.gCH4, 6) < 0.05 && Left(f.T.gHCl, 7) < 0.05 && Left(f.T.gHF, 8) < 0.05;
            var held = net.ScrubHeld();
            double In(int g) => held[f.T.Gases[g].Index];
            bool where = In(f.T.gNH3) > 0.45 && In(f.T.gHCN) > 0.45 && In(f.T.gAroma) > 0.18 && In(f.T.gHCl) > 0.45 && In(f.T.gHF) > 0.27
                && In(f.T.gCO) == 0 && In(f.T.gH2) == 0 && In(f.T.gCH4) == 0 && In(f.T.gO3) == 0;
            Check("정화 세 단: 아홉 기체가 30분에 95% 넘게 걷힌다 — 숯(암모니아 · 시안화수소 · 음식 향) · 흡착제(염화수소 · 플루오린화수소)에 붙잡히고 · 일산화탄소 · 수소 · 메탄은 타서 · 오존은 풀려 남지 않는다",
                gone && where, $"남은 몫 {string.Join(" ", new[] { f.T.gNH3, f.T.gHCN, f.T.gAroma, f.T.gO3, f.T.gCO, f.T.gH2, f.T.gCH4, f.T.gHCl, f.T.gHF }.Select((g, k) => $"{Left(g, k):P0}"))} · 숯 {net.CarbonFill(dev):P0} · 흡착제 {net.SorbentFill(dev):P0}");
            // 산소: 일산화탄소 1 → ½ · 수소 1 → ½ · 메탄 1 → 2를 쓰고 · 오존 1 → 1½을 돌려준다 (사람 · 불이 없는 방)
            double o2Use = (before[4] - f.Total(f.T.gCO)) * 0.5 + (before[5] - f.Total(f.T.gH2)) * 0.5 + (before[6] - f.Total(f.T.gCH4)) * 2 - (before[3] - f.Total(f.T.gO3)) * 1.5;
            Check("촉매 산화는 산소를 반응식대로 쓴다", Math.Abs(o20 - f.Total(f.T.gO2) - o2Use) < 1e-6, $"쓴 산소 {o20 - f.Total(f.T.gO2):0.0000} · 반응식 {o2Use:0.0000}몰");
        }
        // 다 찬 숯은 못 거른다 (거르개를 갈면 다시)
        {
            var (s, net, f, dev) = Room();
            net.FillBed(dev, 1.0);   // 숯 · 흡착제가 다 찼다
            double nh0 = f.Total(f.T.gNH3), hcl0 = f.Total(f.T.gHCl);
            s.Run(Time.Minutes(10));
            double nhFull = f.Total(f.T.gNH3) / nh0, hclFull = f.Total(f.T.gHCl) / hcl0;
            int fk = dev * 4 + (int)Part.Filter;
            double worn = net.Wear[fk] / net.Devices[dev].Life(Part.Filter);
            net.Repair(dev, Part.Filter);   // 거르개를 간다
            s.Run(Time.Minutes(10));
            double nhNew = f.Total(f.T.gNH3) / nh0;
            Check("다 찬 숯 · 흡착제는 거르지 못한다 · 거르개를 갈면 다시 거른다 · 찬 몫만큼 거르개가 닳은 것으로 친다",
                nhFull > 0.99 && hclFull > 0.99 && worn >= 0.99 && nhNew < 0.3 && net.CarbonFill(dev) < 0.1,
                $"찬 채 10분 암모니아 {nhFull:P0} · 염화수소 {hclFull:P0} · 거르개 닳음 {worn:P0} → 간 뒤 10분 {nhNew:P0}");
        }
    }
}
