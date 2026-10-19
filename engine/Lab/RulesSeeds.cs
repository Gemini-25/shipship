using ShipEngine;

namespace Lab;

/// <summary>켜 둔 조리대 — 사람이 잊은 조리대가 불이 되고 · 원인 사슬의 뿌리가 그 사람 · 주컴퓨터 · 불 끈 이가 전원을 내린다 (⑥ · ② · ⑩ · ⑨). docs/DEPTH.md</summary>
public static partial class Program
{
    private static void SeedRules()
    {
        if (!Do("켜 둔 조리대")) return;
        Console.WriteLine("\n[켜 둔 조리대]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // 차림: 누가 국 냄비를 올린 조리대를 켜 둔 채 자리를 떴다 (잊은 것은 그 사람의 판단 — ⑧ 밥 짓기 · 이 시험은 그 뒤 세상의 일을 본다)
        (Device stove, int who, int cause) LeftOn(Sim s)
        {
            var net = s.Get<Network>(); var m = s.Get<Mind>();
            var stove = net.Devices.First(d => d.Def.Role == "stove" && net.On[d.Id] == 0 && !net.IsBroken(d.Id, Part.Electric));
            int who = m.Agents.First(a => a.IsPerson).Id;
            int c0 = s.Causes.Add(CauseKind.Person, $"{Ko.I(m.NameOf(who))} {Ko.Eul(stove.Name)} 켜 둔 채 자리를 떴다", s.Tick, stove.Cell, 0, 0, who);
            net.SetOn(stove.Id, true); net.OnCause[stove.Id] = c0; net.PanWater[stove.Id] = 0.4f * 55.5f;   // 국 냄비 하나 (물 0.4L)
            return (stove, who, c0);
        }

        // ── 사람의 실수: 조리대를 켜 둔 채 자리를 떴다 → 그 위 음식이 눌어 불 — 불의 원인 사슬 뿌리가 그 사람 (주컴퓨터가 멈춰 연기를 못 본다) ──
        {
            var s = ShipWorld.Crewed("Didim", 9, 6, 2);
            var net = s.Get<Network>(); var ch = s.Get<Chemistry>(); var m = s.Get<Mind>(); var so = s.Get<Society>(); var f = s.Get<Field>();
            net.Age(net.Devices.First(d => d.Type == "MainComputer").Id, Part.Electric, 1.0);
            s.Run(Time.Minutes(10));
            var (stoveDev, culprit, c0) = LeftOn(s);
            long lit = -1;
            for (long t = 0; t < Time.Hours(8) && lit < 0; t++)
            {
                s.Step(); if (ch.Ignitions > 0) lit = t;
                if (Debug && t % 1500 == 0) Console.WriteLine($"    {t / 1500}시간 · 조리대 켬 {net.On[stoveDev.Id]} 공급 {net.Supply[stoveDev.Id]:0.00} · 겉면 {f.Tf[stoveDev.Cell]:0}K · 공기 {f.Temp(stoveDev.Cell):0}K · 음식 {f.Stored(f.T.sFood, stoveDev.Cell):0.0}몰");
            }
            int fire = 0;
            for (int c = 0; c < ch.FireCause.Length && fire == 0; c++) if (ch.FireCause[c] != 0) fire = ch.FireCause[c];
            int root = so.Culprit(s, fire, -1);
            if (Debug) Console.WriteLine($"    불 {(lit >= 0 ? Time.Clock(lit) : "없음")} · {s.Causes.Story(fire)}");
            Check("사람의 실수: 켜 둔 조리대가 그 위 음식을 눌려 불을 낸다 — 불의 원인 사슬 뿌리가 켜 둔 사람",
                lit > 0 && root == culprit && culprit >= 0,
                $"{s.Causes.Text(c0)} → 불 {(lit > 0 ? $"{lit / 1500.0:0.0}시간 뒤" : "안 남")} · 뿌리 {(root >= 0 ? m.NameOf(root) : "-")}");
        }

        // ── 켜 둔 조리대: 주컴퓨터가 눌는 연기를 먼저 보고 조리대 전원을 내린다 → 불이 붙지 않는다 / 주컴퓨터가 멈췄으면 불이 나고 · 불을 끈 이가 조리대 전원도 내린다 → 다시 붙지 않는다 ──
        {
            (long lit, long litAfter, byte onAfter, bool cut, string how) Stove(bool computer)
            {
                var s = ShipWorld.Crewed("Didim", 9, 6, 2);
                var net = s.Get<Network>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>();
                if (!computer) net.Age(net.Devices.First(d => d.Type == "MainComputer").Id, Part.Electric, 1.0);
                s.Run(Time.Minutes(10));
                var (stove, _, _) = LeftOn(s);
                if (!computer)
                {
                    // 주방에 로봇 하나 (소화기 든) — 주컴퓨터 없이도 제 눈으로 불을 보고 끈다
                    int spot = s.Layout.Rooms[s.Layout.Room[stove.Cell]].Cells.Where(c => s.Layout.Kind[c] == CellKind.Floor && s.Layout.FurnitureAt[c] < 0).OrderBy(c => m.Dist(c, stove.Cell)).Skip(3).First();
                    m.AddRobot(s, spot, AgentKind.Robot, spot);
                }
                long outAt = -1;
                for (long t = 0; t < Time.Hours(4) && outAt < 0; t++) { s.Step(); if (ch.Ignitions > 0 && ch.BurningCount == 0 || computer && net.On[stove.Id] == 0) outAt = s.Tick; }
                s.Run(Time.Minutes(3));                                       // 끈 이가 손을 털 때까지
                long lit = ch.Ignitions;
                byte onAfter = net.On[stove.Id];
                var ext = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Extinguished);
                string how = ext.Kind == FactKind.Extinguished ? s.Causes.Text(ext.Cause) : sm.Log.LastOrDefault(x => x.text.Contains("전원을 내렸다")).text ?? "";
                s.Run(Time.Hours(1));
                if (Debug) foreach (var x in s.Facts.Since(0).Where(x => x.Kind is FactKind.Ignition or FactKind.FireOut or FactKind.Extinguished)) Console.WriteLine($"    {(computer ? "주컴퓨터" : "멈춤")} {Time.Clock(x.Tick)} {x.Kind} · {s.Causes.Story(x.Cause)}");
                return (lit, ch.Ignitions, onAfter, sm.StovesCut > 0, how);
            }
            var watchedT = Task.Run(() => Stove(true)); var alone = Stove(false); var watched = watchedT.Result;
            Check("켜 둔 조리대: 주컴퓨터가 눌는 연기를 먼저 알아채 조리대 전원을 내린다 → 불이 붙지 않는다",
                watched.lit == 0 && watched.litAfter == 0 && watched.onAfter == 0 && watched.cut, $"불붙음 {watched.lit} · 조리대 {(watched.onAfter != 0 ? "켜짐" : "꺼짐")} · \"{watched.how}\"");
            Check("켜 둔 조리대 (주컴퓨터가 멈췄다): 불이 나고 · 불을 끈 이가 조리대 전원도 내린다 → 한 시간 동안 다시 붙지 않는다",
                alone.lit >= 1 && alone.onAfter == 0 && alone.litAfter == alone.lit && alone.how.Contains("조리대"),
                $"불붙음 {alone.lit} → {alone.litAfter} · 그 뒤 조리대 {(alone.onAfter != 0 ? "켜짐" : "꺼짐")} · \"{alone.how}\"");
        }
    }
}
