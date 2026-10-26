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
            foreach (var p in s.Get<Structure>().Props) w += p.Liquid;   // 컵 · 물병에 든 물 (PH8 — 깨지거나 넘어지면 바닥으로)
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

        // 쏟음 (5단 C10): 목마른 이가 컵을 쏟아도 (몫 1) 탱크 · 오수 · 바닥 물 · 공기의 김 · 컵 물의 합이 줄어든 만큼이 마신 물이다 (쏟은 물은 바닥에 남는다)
        {
            var s3 = Worlds.Full(Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend), 1);
            var n3 = s3.Get<Network>(); var f3 = s3.Get<Field>(); var m3 = s3.Get<Mind>(); var l3 = s3.Get<Life>();
            var lay3 = s3.Layout;
            int p3 = m3.AddPerson(s3, Worlds.At(lay3, 3, 7));
            s3.Run(2);
            l3.CupFumble = 1;
            double W3()
            {
                double w = n3.WaterTank + n3.WasteWater + f3.Total(f3.T.gH2O);
                for (int c = 0; c < lay3.N; c++) w += f3.Stored(f3.T.sWater, c) + f3.Drops[f3.T.sWater * lay3.N + c] + 6 * f3.Stored(f3.T.sFood, c);
                foreach (var d in n3.Devices) w += n3.PanWater[d.Id];
                foreach (var p in s3.Get<Structure>().Props) w += p.Liquid;
                return w;
            }
            long d0 = m3.Drinks;
            double drunk0 = m3.WaterDrunk, sp0 = l3.Spills, v0 = 0, v1 = 0;
            for (int k = 0; k < (int)Time.Minutes(20) && m3.Drinks == d0; k++)
            {
                ref var c = ref m3[p3].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0;
                v0 = W3(); s3.Step(); v1 = W3();
            }
            double drunk = m3.WaterDrunk - drunk0;
            Check("물 보존: 컵을 쏟아도 (5단 C10 · 몫 1) 그 틱에 물의 합이 줄어든 만큼이 마신 물이다 — 쏟은 물은 바닥에 남는다",
                m3.Drinks == d0 + 1 && l3.Spills == sp0 + 1 && Math.Abs((v0 - v1) - drunk) < 1e-6 * v0,
                $"마신 물 {drunk:0.0}몰 · 합 {v0:0.000} → {v1:0.000} (줄어듦 {v0 - v1:0.000}) · 쏟음 {l3.Spills - sp0}");
        }

        // 쓴 소화기 채우기 (docs/MIND.md 3단 — 손): 채우는 물은 물탱크에서 · 장부 '소화기에 담았다' · 탱크 + 소화기 물의 합은 같다 · 이산화탄소는 모아 둔 것에서 · 산소는 방 공기에서
        {
            var s2 = Worlds.Full(Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend), 1);
            var n2 = s2.Get<Network>(); var f2 = s2.Get<Field>();
            s2.Run(3);
            double tank = n2.WaterTank, b0 = s2.Book.Of(Res.Water, Flow.Refill);
            double got = n2.FillExtinguisher(s2, 400);
            double co0 = n2.CO2Store = 50; double gotCo2 = n2.FillCO2(155);
            int cell = Worlds.At(s2.Layout, 3, 2);
            double o2Room = s2.Layout.Rooms[s2.Layout.Room[cell]].Cells.Sum(c => f2.Gas(s2.Tables.gO2, c));
            double gotO2 = n2.FillO2(s2, cell, 60);
            double o2After = s2.Layout.Rooms[s2.Layout.Room[cell]].Cells.Sum(c => f2.Gas(s2.Tables.gO2, c));
            Check("소화기 채우기: 물은 물탱크에서 (400몰 · 장부 '소화기에 담았다') · 이산화탄소는 모아 둔 만큼만 (50몰) · 산소는 그 방 공기에서 (방 산소의 5분의 1까지)",
                Math.Abs(got - 400) < 1e-9 && Math.Abs(tank - n2.WaterTank - got) < 1e-9 && Math.Abs(s2.Book.Of(Res.Water, Flow.Refill) - b0 + got) < 1e-9
                && Math.Abs(gotCo2 - co0) < 1e-12 && n2.CO2Store == 0 && gotO2 > 0 && gotO2 <= 0.2 * o2Room + 1e-9 && Math.Abs(o2Room - o2After - gotO2) < 1e-6,
                $"물 {got:0}몰 · 탱크 {tank:0}→{n2.WaterTank:0} · 이산화탄소 {gotCo2:0}몰 · 산소 {gotO2:0.0}몰 (방 {o2Room:0}몰)");
        }
    }
}
