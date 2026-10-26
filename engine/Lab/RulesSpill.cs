using ShipEngine;

namespace Lab;

/// <summary>일상 물리 — 쏟음 (5단 C10 — docs/STAGE5.md): 마시기는 월드 요청 하나 (물탱크에서 빼고 · 쏟음을 셈한다) · 냄비 쏟음.</summary>
public static partial class Program
{
    private static void SpillRules()
    {
        if (!Do("일상 물리")) return;
        Console.WriteLine("\n[일상 물리 — 쏟음 (C10)]");

        // (가) 컵: 몫 1 · 목마른 이가 물을 마시면 — 장부 마셨다 + 쏟았다 = 이번에 뺀 물 · 바닥 물이 쏟은 만큼 · 사실 '쏟았다' · 원인 '컵을 쏟았다'
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, Worlds.At(lay, 3, 7));
            s.Run(2);
            life.CupFumble = 1;
            double Floor() { double w = 0; for (int c = 0; c < lay.N; c++) w += f.Stored(f.T.sWater, c); return w; }
            long d0 = m.Drinks; double drunk0 = m.WaterDrunk;
            double bd0 = s.Book.Of(Res.Water, Flow.Drunk), bs0 = s.Book.Of(Res.Water, Flow.Spilled), floor0 = Floor();
            for (int k = 0; k < (int)Time.Minutes(20) && m.Drinks == d0; k++)
            {
                ref var c = ref m[p].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0;
                s.Step();
            }
            double drunk = m.WaterDrunk - drunk0, bookDrunk = bd0 - s.Book.Of(Res.Water, Flow.Drunk), bookSpill = bs0 - s.Book.Of(Res.Water, Flow.Spilled), floor = Floor() - floor0;
            var fact = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.Spilled);
            string why = fact.Kind == FactKind.Spilled ? s.Causes.Text(fact.Cause) : "";
            double taken = bookDrunk + bookSpill;
            Check("쏟음 (C10) 컵: 몫 1 · 목마른 이가 물을 마시면 장부 '마셨다' + '쏟았다' = 이번에 뺀 물 (쏟은 몫 40~100%) · 바닥 물이 쏟은 만큼 · 사실 '쏟았다' (그 몸 · 쏟은 몰) · 원인 '컵을 쏟았다'",
                m.Drinks == d0 + 1 && bookSpill >= 0.4 * taken - 1e-9 && bookSpill <= taken + 1e-9 && Math.Abs(bookDrunk - drunk) < 1e-9 && Math.Abs(floor - bookSpill) < 0.01 * bookSpill + 1e-6
                && fact.Kind == FactKind.Spilled && fact.Actor == m[p].C.Body && Math.Abs(fact.Size - bookSpill) < 1e-3 && why.Contains("컵을 쏟았다"),
                $"마심 {m.Drinks - d0} · 장부 마셨다 {bookDrunk:0.0} · 쏟았다 {bookSpill:0.0}몰 · 바닥 물 +{floor:0.00} · 사실 {(fact.Kind == FactKind.Spilled ? $"{fact.Size:0.0}몰 몸 {fact.Actor}" : "없음")} · '{why}'");
        }

        // (나) 냄비: 몫 1 · 80℃ 냄비를 쏟으면 냄비 물이 바닥으로 (몫만큼 · 물의 합은 같다) · 쏟은 이가 덴다 (원인 '냄비를 쏟았다') — 30℃ 냄비는 데지 않는다
        {
            var legend = Worlds.MindLegend; legend['k'] = "Stove";
            var lay = Worlds.Ascii(Worlds.MindShip.Replace("#m.r..........w.....m...#", "#m.r..........w.....mk..#"), legend);
            (double moved, double floor, float burn, string why) Pan(float panK)
            {
                var s = Worlds.Full(lay, 1);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
                s.Run(2);
                var stove = net.Devices.First(d => d.Def.Role == "stove");
                int p = m.AddPerson(s, stove.Cell), body = m[p].C.Body;
                net.FillPan(s, stove.Id);
                net.PanK[stove.Id] = panK;
                life.PanFumble = 1;
                double Floor() { double w = 0; for (int c = 0; c < lay.N; c++) w += f.Stored(f.T.sWater, c); return w; }
                double pan0 = net.PanWater[stove.Id], floor0 = Floor(); float burn0 = life[body].Burn;
                life.HandlePan(s, body, stove.Id, 0, 0, 0, p, m.NameOf(p));
                var fact = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.Spilled);
                return (pan0 - net.PanWater[stove.Id], Floor() - floor0, life[body].Burn - burn0, fact.Kind == FactKind.Spilled ? s.Causes.Text(fact.Cause) : "");
            }
            var hot = Pan(353.15f); var warm = Pan(303.15f);
            Check("쏟음 (C10) 냄비: 몫 1 · 80℃ 냄비를 쏟으면 냄비 물 40~100%가 바닥으로 (빠진 만큼 바닥에 — 합이 같다) · 쏟은 이가 덴다 · 원인 '냄비를 쏟았다' / 30℃ 냄비는 쏟아도 데지 않는다",
                hot.moved >= 0.4 * Network.PanFill - 1e-3 && Math.Abs(hot.floor - hot.moved) < 1e-6 && hot.burn > 0 && hot.why.Contains("냄비를 쏟았다") && warm.moved > 0 && warm.burn == 0,
                $"80℃: 쏟음 {hot.moved:0.0}몰 · 바닥 +{hot.floor:0.0} · 데임 {hot.burn:0.000} · '{hot.why}' / 30℃: 쏟음 {warm.moved:0.0} · 데임 {warm.burn:0.000}");
        }
    }
}
