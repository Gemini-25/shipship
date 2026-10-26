using ShipEngine;

namespace Lab;

/// <summary>⑪ 물자 — 장부(처음 재고 + 적은 흐름 = 지금 재고) · 산소 발생기 조절 · 사바티에 · 공조 응결 · 작물 거두기. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void LedgerRules()
    {
        if (!Do("물자")) return;
        Console.WriteLine("\n[⑪ 물자 — 장부]");

        // ── 산소 조절: 산소 발생기는 배 산소 분압에 맞춰 돈다 — 사람이 적으면 덜 돌아 물을 아낀다 · 분압은 20.4~21.6kPa ──
        {
            (double duty, double pp, double split) Run(int people)
            {
                var s = ShipWorld.Crewed("Didim", 5, people, 0);
                var net = s.Get<Network>();
                double sum = 0; int n = 0;
                for (long t = 0; t < Time.Hours(6); t++)
                {
                    s.Step();
                    if (t > Time.Hours(2) && t % 250 == 0) for (int d = 0; d < net.Devices.Count; d++) if (net.Devices[d].Def.Role == "o2gen") { sum += net.Duty[d]; n++; }
                }
                return (sum / Math.Max(1, n), net.PpO2, -s.Book.Of(Res.Water, Flow.Split));
            }
            var oneT = Task.Run(() => Run(1)); var six = Run(6); var one = oneT.Result;
            Check("산소 조절: 산소 발생기는 배 산소 분압에 맞춰 돈다 — 1명이면 6명보다 덜 돌아 물을 덜 가르고 · 분압은 20.4~21.6kPa",
                one.duty < six.duty * 0.7 && one.split < six.split * 0.6 && one.pp is > 20.4 and < 21.6 && six.pp is > 20.4 and < 21.6,
                $"1명: 일 몫 {one.duty:0.00} · {one.pp:0.00}kPa · 가른 물 {one.split * 0.018:0.0}L / 6명: {six.duty:0.00} · {six.pp:0.00}kPa · {six.split * 0.018:0.0}L");
        }

        // ── 거두기: 다 자란 작물을 로봇이 거둬 냉장고에 넣는다 — 식량 장부에 '거둠'으로 ──
        {
            var s = ShipWorld.Crewed("Didim", 3, 2, 2);
            var life = s.Get<Life>(); var m = s.Get<Mind>(); var work = s.Get<Work>();
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(life.Crops);
            for (int k = 0; k < span.Length; k++) span[k].Biomass = (float)(Work.HarvestAt * life.CropArea(s.Get<Network>(), k) * 1.5);
            int crops = span.Length;
            var fridge = s.Layout.Furniture.Where(fu => fu.Type == "Fridge").SelectMany(fu => fu.Cells).ToList();
            double fridge0 = fridge.Sum(c => s.Get<Field>().Stored(s.Tables.sFood, c));
            s.Run(Time.Minutes(116));   // 다 거둬 냉장고에 넣은 때(1450틱 · 58분)의 두 배 (예전 3시간)
            double fridge1 = fridge.Sum(c => s.Get<Field>().Stored(s.Tables.sFood, c));
            double got = s.Book.Of(Res.Food, Flow.Harvest);
            bool holding = m.Agents.Any(a => a.C.Holding == m.iHarvest) || Worlds.Items(s).Any(x => x.Item == m.iHarvest);
            Check("거두기: 다 자란 작물을 로봇이 거둬 냉장고에 넣는다 (식량 장부에 '거둠' · 손에 남은 것 없음)",
                got >= crops * 0.5 - 1e-3 && work.Harvested >= crops && !holding && fridge1 > fridge0 + crops * 0.4 && Math.Abs(work.Other(s, (int)Res.Food)) < 1e-9,
                $"거둔 것 {got:0.00}몰 ({work.Harvested}번 / 작물 {crops}) · 냉장고 {fridge0:0.0} → {fridge1:0.0}몰 · 손에 남음 {holding}");
        }
    }
}
