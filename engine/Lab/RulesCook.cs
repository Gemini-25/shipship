using ShipEngine;

namespace Lab;

/// <summary>⑧ 마음 (바퀴 2) — 요리 · 깜빡함 · 실수에서 배우기: 조리대를 켜고 끄는 것은 사람의 일이고, 잊는 것은 그 사람의 상태에서 나온다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void CookRules()
    {
        if (!Do("요리")) return;
        Console.WriteLine("\n[⑧ 요리 · 깜빡함 · 배우기]");
        // 마음 시험 배 + 통로의 배식기 곁에 조리대
        var legend = Worlds.MindLegend; legend['k'] = "Stove";
        var lay = Worlds.Ascii(Worlds.MindShip.Replace("#m.r..........w.....m...#", "#m.r..........w.....mk..#"), legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        ulong seed = 1;
        while (Rng.Unit(seed, Rng.Key("요리"), 0, 0) >= 0.85) seed++;      // 첫 사람(0번)이 이 끼니에 밥을 지을 마음이 드는 시드

        // ── 요리: 끼니때 요리를 좋아하는 이가 조리대를 켜고 · 밥을 짓고 · 끄고 · 먹는다 (음식 냄새가 퍼진다) ──
        {
            var s = Worlds.Full(lay, seed);
            var m = s.Get<Mind>(); var net = s.Get<Network>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, At(17, 8));
            m.SetPersonality(p, 0.8f, 0.9f, 0.5f, 0.5f, 0.3f);
            s.Run(1);
            m[p].C.SkCook = 1; m[p].C.Hunger = 0.75f; m[p].C.Sleepy = 0; m[p].C.Thirst = 0;
            var stove = net.Devices.First(d => d.Def.Role == "stove");
            bool cooked = false, wasOn = false; double aroma = 0; long meals0 = m.Meals;
            for (int k = 0; k < Time.Hours(1) && !(cooked && m.Meals > meals0); k++)
            {
                s.Step(); m[p].C.Sleepy = 0;
                cooked |= m[p].C.Goal == GoalKind.Cook;
                wasOn |= net.On[stove.Id] != 0;
                aroma = Math.Max(aroma, f.Ppm(f.T.gAroma, stove.Cell));
            }
            s.Run(5);
            if (Debug) Console.WriteLine($"    요리 {cooked} · 켬 {wasOn} · 지금 {net.On[stove.Id]} · 끼니 {m.Meals - meals0} · 냄새 {aroma:0.0}ppm · {m.Thought(p)}");
            Check("요리: 끼니때 요리를 좋아하는 이가 조리대를 켜고 밥을 지어 · 끄고 · 먹는다 (음식 냄새가 퍼진다)",
                cooked && wasOn && net.On[stove.Id] == 0 && m.Meals > meals0 && aroma > 1 && m.Cooked == 1 && m.Forgot == 0,
                $"밥 짓기 {m.Cooked} · 조리대 켬 {wasOn} → 지금 {(net.On[stove.Id] != 0 ? "켜짐" : "꺼짐")} · 끼니 {m.Meals - meals0} · 냄새 {aroma:0.0}ppm");
        }

        // ── 하다 말고: 밥을 짓다 급한 소리(같은 방에 구멍)를 들으면 — 덜 성실한 이는 조리대를 켜 둔 채 달려 나가고 · 지난 실수에서 배운 성실한 이는 끄고 간다 ──
        //    켜 둔 조리대는 졸고 눌어 연기를 내고 → 주컴퓨터가 전원을 내린다 → 켜 둔 이는 배운다 (조심이 오른다)
        {
            (bool on, string why, float carefulAfter, bool cut) Run(float consc, float careful)
            {
                var s = Worlds.Full(Worlds.Ascii(Worlds.MindShipComputer.Replace("#m.r..........w.....m...#", "#m.r..........w.....mk..#"), legend), seed);
                var m = s.Get<Mind>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
                int p = m.AddPerson(s, At(17, 8));
                m.SetPersonality(p, 0.8f, consc, 0.5f, 0.5f, 0.3f);
                s.Run(1);
                m[p].C.SkCook = 1; m[p].C.Hunger = 0.75f; m[p].C.Sleepy = 0; m[p].C.Thirst = 0; m[p].C.Careful = careful;
                m[p].C.Brave = 1; m[p].C.SkFire = 1;
                var stove = net.Devices.First(d => d.Def.Role == "stove");
                for (int k = 0; k < Time.Minutes(20) && net.On[stove.Id] == 0; k++) { s.Step(); m[p].C.Sleepy = 0; }
                s.Run(Time.Minutes(3));                                       // 짓는 중
                // 통로 벽에 작은 구멍 — 쉬익 소리에 빠져나간다 (2분 뒤 누가 막은 것으로 둔다)
                var f = s.Get<Field>();
                int hole = At(12, 9);
                f.SetHole(hole, 0.0004f);
                for (int k = 0; k < Time.Minutes(2); k++) { s.Step(); if (Debug && k % 10 == 0) Console.WriteLine($"      {k} {m.Thought(p)} · 믿음 {string.Join(" ", m[p].Beliefs.Select(b => $"{b.Kind}/{b.Conf:0.00}"))}"); }
                f.SetHole(hole, 0);
                bool on = net.On[stove.Id] != 0;
                var mis = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Mistake && x.Actor == p);
                string why = mis.Kind == FactKind.Mistake ? s.Causes.Text(mis.Cause) : "";
                // 빠져나간 이가 조리대가 있는 통로를 벗어나면 그 통로 문을 한 시간 잠근다 — 돌아와 제가 켜 둔 조리대를 보고 끄지 않게 (DF5: 본 이는 끈다 · 이 시험은 주컴퓨터가 끈 뒤의 배움을 본다)
                int hall = m.RoomOf(stove.Cell);
                for (int k = 0; k < Time.Minutes(5) && on && (m.RoomOf(m[p].C.Cell) == hall || m.RoomOf(m[p].C.Cell) < 0); k++) s.Step();
                if (on && m.RoomOf(m[p].C.Cell) != hall)
                    for (int d = 0; d < s.Layout.Doors.Count; d++)
                    {
                        Span<int> nb = stackalloc int[4];
                        s.Layout.Neighbors(s.Layout.Doors[d].Cell, nb);
                        foreach (int j in nb) if (j >= 0 && s.Layout.Room[j] == hall) { s.Get<Structure>().LockDoor(d, s.Tick + Time.Hours(1)); break; }
                    }
                for (int k = 0; k < Time.Minutes(60) && net.On[stove.Id] != 0; k++) s.Step();
                s.Run(Time.Minutes(2));
                if (Debug) Console.WriteLine($"    성실 {consc} · 조심 {careful}: 켜짐 {on} · \"{why}\" · 지금 {net.On[stove.Id]} · 조심 {m[p].C.Careful:0.00} · 전원 내림 {sm.StovesCut} · {m.Thought(p)}");
                return (on, why, m[p].C.Careful, sm.StovesCut > 0);
            }
            var careless = Run(0.05f, 0); var careful = Run(0.95f, 0.9f);
            Check("하다 말고: 밥을 짓다 같은 방 벽에서 쉬익 소리가 나면 덜 성실한 이는 조리대를 켜 둔 채 달려 나가고 · 배운 성실한 이는 끄고 간다",
                careless.on && careless.why.Contains("켜 둔 채") && careless.why.Contains("급한") && !careful.on && careful.why == "",
                $"덜 성실: {(careless.on ? "켜 둔 채" : "끄고")} \"{careless.why}\" / 배운 성실: {(careful.on ? "켜 둔 채" : "끄고")}");
            Check("실수에서 배운다: 켜 둔 조리대를 주컴퓨터가 껐다 (쓰는 이 없이 15분 — DF5 · 눌는 연기) → 켜 둔 이의 조심이 오른다 (다음엔 덜 잊는다)",
                careless.cut && careless.carefulAfter >= 0.29f,
                $"전원 내림 {careless.cut} · 조심 0 → {careless.carefulAfter:0.00}");
        }
    }
}
