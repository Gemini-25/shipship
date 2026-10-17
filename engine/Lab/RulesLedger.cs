using ShipEngine;

namespace Lab;

/// <summary>⑪ 물자 — 장부(처음 재고 + 적은 흐름 = 지금 재고) · 산소 발생기 조절 · 사바티에 · 공조 응결 · 작물 거두기. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void LedgerRules()
    {
        if (!Do("물자")) return;
        Console.WriteLine("\n[⑪ 물자 — 장부]");
        double Rh(Sim s)
        {
            var f = s.Get<Field>(); double sum = 0; int n = 0;
            foreach (var room in s.Layout.Rooms) foreach (int c in room.Cells)
                if (f.Pressure(c) > 50) { sum += f.Partial(f.T.gH2O, c) / Water.SatKPa(f.Temp(c)); n++; }
            return n > 0 ? sum / n : 0;
        }

        // ── 장부: 케스트럴 하루 (6명 · 로봇 2) — 처음 재고 + 적은 흐름 = 지금 재고 · 며칠 버티나 ──
        var day = ShipWorld.Crewed("Kestrel", 3, 6, 2);
        day.Run(Time.Days(1));
        {
            var w = day.Get<Work>(); var b = day.Book;
            double worst = 0; string worstName = "";
            for (int k = 0; k < b.Kinds; k++)
            {
                if (k == (int)Res.O2) continue; // 산소는 아래에서 (공기는 새어 나갈 수 있다)
                double o = Math.Abs(w.Other(day, k)) / Math.Max(1, Math.Abs(w.Stock(k)));
                if (o > worst) { worst = o; worstName = k < Book.Bulk ? ((Res)k).ToString() : day.Get<Mind>().ItemNames[k - Book.Bulk]; }
            }
            double o2Other = w.Other(day, (int)Res.O2), breathed = -b.Of(Res.O2, Flow.Breathed);
            var (foodDays, _) = w.DaysLeft(day, (int)Res.Food);
            var (waterDays, waterNoMake) = w.DaysLeft(day, (int)Res.Water);
            Check("물자 장부: 케스트럴 하루 — 처음 재고 + 적은 흐름 = 지금 재고 (식량 · 물 · 물건 정확히 · 산소는 숨의 1% 안) · 며칠 버티나를 센다",
                worst < 1e-9 && Math.Abs(o2Other) < breathed * 0.01 && foodDays is > 3 and < 100 && waterDays > waterNoMake,
                $"가장 큰 어긋남 {worst:0e0} ({worstName}) · 산소 {o2Other:0.0}몰 / 숨 {breathed:0}몰 · 식량 {foodDays:0.0}일 · 물 {waterDays:0}일 (되찾지 못하면 {waterNoMake:0.0}일)");
        }

        // ── 사바티에: 갈라 낸 수소와 걸러 모은 이산화탄소로 가른 물의 절반 가까이를 되찾는다 (ISS — 수소가 모자라 절반을 넘지 못한다) ──
        {
            var b = day.Book;
            double split = -b.Of(Res.Water, Flow.Split), back = b.Of(Res.Water, Flow.Sabatier);
            Check("사바티에: 산소를 만들려고 가른 물의 40~50%를 이산화탄소 + 수소에서 되찾는다",
                split > 0 && back / split is > 0.4 and <= 0.5, $"가른 물 {split * 0.018:0.0}L · 되찾음 {back * 0.018:0.0}L ({back / Math.Max(1e-9, split):0.00}) · 버린 메탄 {day.Get<Network>().VentedCH4 * 0.016:0.00}kg");
        }

        // ── 공조 응결: 제습기가 없는 배도 공조 열교환기가 숨 · 땀의 습기를 물로 모은다 → 습도 60% 아래 ──
        {
            double rh = Rh(day), cond = day.Book.Of(Res.Water, Flow.Condensed);
            Check("공조 응결: 제습기 없는 케스트럴 — 열교환기가 하루 몇 L의 습기를 물탱크로 되찾고 · 방 습도는 60% 아래",
                cond * 0.018 > 3 && rh < 0.6, $"모은 물 {cond * 0.018:0.0}L/일 · 평균 습도 {rh * 100:0}%");
        }

        // ── 산소 조절: 산소 발생기는 배 산소 분압에 맞춰 돈다 — 사람이 적으면 덜 돌아 물을 아낀다 · 분압은 20.4~21.6kPa ──
        {
            (double duty, double pp, double split) Run(int people)
            {
                var s = ShipWorld.Crewed("Kestrel", 5, people, 0);
                var net = s.Get<Network>();
                double sum = 0; int n = 0;
                for (long t = 0; t < Time.Hours(12); t++)
                {
                    s.Step();
                    if (t > Time.Hours(2) && t % 250 == 0) for (int d = 0; d < net.Devices.Count; d++) if (net.Devices[d].Def.Role == "o2gen") { sum += net.Duty[d]; n++; }
                }
                return (sum / Math.Max(1, n), net.PpO2, -s.Book.Of(Res.Water, Flow.Split));
            }
            var one = Run(1); var six = Run(6);
            Check("산소 조절: 산소 발생기는 배 산소 분압에 맞춰 돈다 — 1명이면 6명보다 덜 돌아 물을 덜 가르고 · 분압은 20.4~21.6kPa",
                one.duty < six.duty * 0.7 && one.split < six.split * 0.6 && one.pp is > 20.4 and < 21.6 && six.pp is > 20.4 and < 21.6,
                $"1명: 일 몫 {one.duty:0.00} · {one.pp:0.00}kPa · 가른 물 {one.split * 0.018:0.0}L / 6명: {six.duty:0.00} · {six.pp:0.00}kPa · {six.split * 0.018:0.0}L");
        }

        // ── 거두기: 다 자란 작물을 로봇이 거둬 냉장고에 넣는다 — 식량 장부에 '거둠'으로 ──
        {
            var s = ShipWorld.Crewed("Kestrel", 3, 2, 2);
            var life = s.Get<Life>(); var m = s.Get<Mind>(); var work = s.Get<Work>();
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(life.Crops);
            for (int k = 0; k < span.Length; k++) span[k].Biomass = 0.5f;
            int crops = span.Length;
            var fridge = s.Layout.Furniture.Where(fu => fu.Type == "Fridge").SelectMany(fu => fu.Cells).ToList();
            double fridge0 = fridge.Sum(c => s.Get<Field>().Stored(s.Tables.sFood, c));
            s.Run(Time.Hours(3));
            double fridge1 = fridge.Sum(c => s.Get<Field>().Stored(s.Tables.sFood, c));
            double got = s.Book.Of(Res.Food, Flow.Harvest);
            bool holding = m.Agents.Any(a => a.C.Holding == m.iHarvest) || m.Loose.Any(x => x.Item == m.iHarvest);
            Check("거두기: 다 자란 작물을 로봇이 거둬 냉장고에 넣는다 (식량 장부에 '거둠' · 손에 남은 것 없음)",
                got >= crops * 0.5 - 1e-3 && work.Harvested >= crops && !holding && fridge1 > fridge0 + crops * 0.4 && Math.Abs(work.Other(s, (int)Res.Food)) < 1e-9,
                $"거둔 것 {got:0.00}몰 ({work.Harvested}번 / 작물 {crops}) · 냉장고 {fridge0:0.0} → {fridge1:0.0}몰 · 손에 남음 {holding}");
        }
    }
}
