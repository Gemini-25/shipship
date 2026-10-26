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
        double Stool() { double kg = life.WasteCarried; for (int fu = 0; fu < lay.Furniture.Count; fu++) kg += life.Tank[fu] + life.Bowl[fu] + life.Heap[fu]; return kg; }   // 썩어 나간 몫은 따로 (C19)
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
        // 썩어 나간 몫 (5단 C19): 하루 두면 통 · 걸린 것 · 비운 곳의 오물이 썩어 줄고 · 줄어든 만큼이 '썩어 나간 오물'이다
        {
            double before = Stool(), rot0 = life.WasteRotKg;
            s.Run(Time.Hours(24));
            double rotted = life.WasteRotKg - rot0;
            Check("변기 (대변): 하루 두면 오물이 썩어 준다 — 오물 통 + 걸린 것 + 비운 곳 + 든 통 + 썩어 나간 몫 = 처음 (5단 C19)",
                rotted > 0 && Math.Abs(Stool() + rotted - before) < 1e-9 * Math.Max(1, before),
                $"처음 {before:0.000}kg · 지금 {Stool():0.000} + 썩어 나간 {rotted:0.0000}kg");
        }
        WasteRotLaws();
    }

    /// <summary>
    /// 법칙 — 썩음 (5단 C19): 닫힌 방 (사람 · 망 없는 장 + 생명)의 오물 통 · 걸린 것 · 퇴비통을 큰 dt로 몇 번 직접 썩힌다 —
    /// 썩어 나간 포도당 (몰)의 탄소 · 수소 · 산소가 늘어난 메탄 · 이산화탄소 · 수증기 · 수소 · 악취와 줄어든 산소에 그대로 (1e-9).
    /// </summary>
    private static void WasteRotLaws()
    {
        var legend = new Dictionary<char, string> { ['T'] = "Toilet", ['o'] = "Composter" };
        var lay = Worlds.Ascii("\n#######\n#T...o#\n#.....#\n#######", legend);
        var s = new Sim(new SimSetup(lay, 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
        var f = s.Get<Field>(); var life = s.Get<Life>();
        f.FillAir();
        var T = f.T;
        int toilet = lay.Furniture.First(x => x.Type == "Toilet").Id, bin = lay.Furniture.First(x => x.Type == "Composter").Id;
        life.Tank[toilet] = 20; life.Bowl[toilet] = 0.5; life.Clog(toilet, 0); life.Heap[bin] = 10;
        foreach (var fu in lay.Furniture) if (fu.Type == "Toilet") life.Grime[fu.Id] = 0;
        double Sum(int g) => Enumerable.Range(0, lay.N).Sum(c => f.Gas(g, c));
        double ch4 = Sum(T.gCH4), co2 = Sum(T.gCO2), h2o = Sum(T.gH2O), h2 = Sum(T.gH2), st = Sum(T.gStench), o2 = Sum(T.gO2), rot0 = life.WasteRotKg;
        for (int k = 0; k < 5; k++) life.WasteRot(s, 86400);
        double glu = (life.WasteRotKg - rot0) * Life.WasteOrganic / Life.GlucoseKg;
        double dch4 = Sum(T.gCH4) - ch4, dco2 = Sum(T.gCO2) - co2, dh2o = Sum(T.gH2O) - h2o, dh2 = Sum(T.gH2) - h2, dst = Sum(T.gStench) - st, do2 = o2 - Sum(T.gO2);
        double C = 6 * glu - (dch4 + dco2 + 4 * dst), H = 12 * glu - (4 * dch4 + 2 * dh2o + 2 * dh2 + 8 * dst), O = 6 * glu + 2 * do2 - (2 * dco2 + dh2o + 2 * dst);
        Check("썩음 (5단 C19): 닫힌 방의 오물 통 · 걸린 것 · 퇴비통을 하루씩 다섯 번 — 썩어 나간 포도당의 탄소 · 수소 · 산소 = 늘어난 메탄 · 이산화탄소 · 수증기 · 수소 · 악취 + 줄어든 산소 (1e-9)",
            glu > 0.1 && dch4 > 0 && dst > 0 && do2 > 0 && Math.Abs(C) < 1e-9 * glu * 12 && Math.Abs(H) < 1e-9 * glu * 24 && Math.Abs(O) < 1e-9 * glu * 24,
            $"썩은 포도당 {glu:0.000}몰 · 메탄 +{dch4:0.000} · 이산화탄소 +{dco2:0.000} · 악취 +{dst:0.0000} · 산소 −{do2:0.000} · 어긋남 C {C:0.0e0} · H {H:0.0e0} · O {O:0.0e0}");
    }
}
