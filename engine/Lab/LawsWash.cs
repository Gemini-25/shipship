using ShipEngine;

namespace Lab;

/// <summary>화장실 법칙 (docs/WASH.md (다)): 변기에 비운 대변은 오물 통 · 변기에 걸린 것 · 비운 곳 · 든 통 어딘가에 있다 · 오줌의 물은 오수 아니면 바닥에 (사라지지 않는다).</summary>
public static partial class Program
{
    private static void WashLaws()
    {
        if (!Do("변기")) return;
        Console.WriteLine("\n[화장실 — 대변 · 오줌 보존]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['r'] = "RobotDock", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer",
            ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['o'] = "Composter" };
        var lay = Worlds.Ascii(WashCareShip, legend);
        var s = Worlds.Full(lay, 21);
        var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
        s.Run(3);
        int body = life.AddBody(Worlds.At(lay, 2, 1));
        int[] toilets = lay.Furniture.Where(x => x.Type == "Toilet").Select(x => x.Id).ToArray();
        int bin = lay.Furniture.First(x => x.Type == "Composter").Id;
        double Floor() { double w = 0; for (int i = 0; i < lay.N; i++) w += f.Stored(f.T.sWater, i); return w; }
        double Stool() { double kg = life.WasteCarried; for (int fu = 0; fu < lay.Furniture.Count; fu++) kg += life.Tank[fu] + life.Bowl[fu] + life.Heap[fu]; return kg; }
        double waste0 = net.WasteWater, floor0 = Floor(), stool0 = Stool(), stoolIn = 0, urineIn = 0, held = 0;
        bool emptied = true;
        var rng = Rng.Stream(21, "변기 법칙");
        for (int k = 0; k < 400; k++)
        {
            int fu = toilets[k % toilets.Length];
            ref var b = ref life[body];
            b.Bladder = rng.Range(5.0, 30.0); b.Bowel = rng.Range(0.0, 0.4);
            urineIn += b.Bladder;
            double before = b.Bowel;
            life.Flush(s, body, fu, lay.Furniture[fu].Cells[0]);
            stoolIn += before - life[body].Bowel;
            emptied &= life[body].Bladder == 0 && (before <= 0.02 || life[body].Bowel == 0);   // 덜 찬 장(0.02kg 아래)은 몸에 남는다 — 그 밖엔 다 비운다
            if (k % 23 == 7) life.Clog(fu, 0);
            if (k % 11 == 5) life.Unclog(fu);
            if (k % 37 == 20) held += life.LiftTank(fu);
            if (k % 61 == 40) { life.Dump(bin, (float)held); held = 0; }   // 손의 눈금은 float — 끝자리는 월드가 쥔다
            life[body].Bowel = 0;
        }
        double stoolNow = Stool() - stool0, urineNow = net.WasteWater - waste0 + Floor() - floor0;
        Check("변기 (대변): 비운 대변 = 오물 통 + 변기에 걸린 것 + 비운 곳 + 든 통 (막힘 · 뚫기 · 통 들기 · 쏟기를 거쳐도)",
            emptied && Math.Abs(stoolNow - stoolIn) < 1e-9 && life.Overflows > 0,
            $"들어간 {stoolIn:0.000}kg · 있는 곳 합 {stoolNow:0.000}kg · 막힘 {life.Clogs} · 넘침 {life.Overflows}");
        Check("변기 (오줌): 비운 오줌의 물 = 오수 + 넘쳐 바닥에 고인 물", Math.Abs(urineNow - urineIn) < 1e-6 * urineIn,
            $"비운 {urineIn:0.0}몰 · 오수 +{net.WasteWater - waste0:0.0} · 바닥 +{Floor() - floor0:0.0}몰");
    }
}
