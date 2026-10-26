using ShipEngine;

namespace Lab;

/// <summary>일상 물리 — 쏟음 (5단 C10 — docs/STAGE5.md): 마시기는 월드 요청 하나 (물탱크에서 빼고 · 쏟음을 셈한다) · 냄비 쏟음 · 젖은 바닥 미끄러짐 (C12).</summary>
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

        // (다) C12 젖은 바닥 미끄러짐: 복도 한 줄 — 지린 물 (400몰)이 고인 칸을 B가 목말라 지나가면 (몫 1) 넘어진다 — 사실 '넘어졌다' 하나 · 원인 '미끄러져' ← '지렸다' · 타박 · Slips 1 · Falls 0
        //     같은 차림 무중력 칸 · 로봇은 미끄러지지 않는다
        {
            var lay = Worlds.Ascii("\n##############\n#m..........e#\n##############", new Dictionary<char, string> { ['m'] = "MealDispenser", ['e'] = "Bed" });
            (int fell, string story, float blunt, long slips, long falls) Slip(bool zeroG, bool robot)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
                int wet = Worlds.At(lay, 6, 1);
                int b = robot ? m.AddRobot(s, Worlds.At(lay, 11, 1)) : m.AddPerson(s, Worlds.At(lay, 11, 1));
                s.Run(2);
                f.AddStore(wet, f.T.sWater, 400, s.Causes.Add(CauseKind.Biology, "참지 못하고 옷에 지렸다 (방광이 찼다)", s.Tick, wet));   // A가 그 칸에서 지렸다 (C8 원인)
                if (zeroG) for (int c = 0; c < lay.N; c++) f.Grav[c] = 0.1f;
                life.FloorSlipChance = 1; life.FloorSlipMax = 1;
                float blunt0 = robot ? 0 : life[m[b].C.Body].Blunt;
                long slips0 = life.Slips, falls0 = m.Falls, fact0 = s.Facts.Since(0).Count(x => x.Kind == FactKind.Fell);
                for (int k = 0; k < (int)Time.Minutes(10) && life.Slips == slips0; k++)
                {
                    if (!robot) { ref var c = ref m[b].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0; }
                    s.Step();
                }
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"      물 {string.Join(" ", Enumerable.Range(1, 12).Select(x => f.Stored(f.T.sWater, Worlds.At(lay, x, 1)).ToString("0")))} · B 칸 ({lay.X(m[b].C.Cell)}) 목표 {m[b].C.Goal} · 마심 {m.Drinks}");
                var fell = s.Facts.Since(0).Where(x => x.Kind == FactKind.Fell).Skip((int)fact0).ToList();
                return (fell.Count, fell.Count > 0 ? s.Causes.Story(fell[0].Cause) : "", robot ? 0 : life[m[b].C.Body].Blunt - blunt0, life.Slips - slips0, m.Falls - falls0);
            }
            var one = Slip(false, false); var low = Slip(true, false); var bot = Slip(false, true);
            Check("미끄러짐 (C12): 복도에서 지린 물이 고인 칸을 B가 목말라 지나가면 (몫 1) 미끄러져 넘어진다 — 사실 '넘어졌다' 하나 · 원인 '젖은 바닥에 미끄러져' ← '지렸다' · 타박 · 미끄러짐 1 · 넘어짐 (점화 · 덜컹) 0 / 무중력 칸 · 로봇은 0",
                one.fell == 1 && one.story.Contains("젖은 바닥에 미끄러져") && one.story.Contains("지렸다") && one.blunt > 0 && one.slips == 1 && one.falls == 0 && low.slips == 0 && low.fell == 0 && bot.slips == 0,
                $"넘어짐 사실 {one.fell} · '{one.story}' · 타박 +{one.blunt:0.00} · 미끄러짐 {one.slips} · 넘어짐 셈 {one.falls} / 무중력 {low.slips} · 로봇 {bot.slips}");
        }
    }
}
