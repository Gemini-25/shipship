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
        // 아무도 지나가지 않는 주방: 문을 잠그고 안에 있던 사람은 식당으로 (DF5 — 지나다 켜 둔 조리대를 본 이 · 로봇은 끈다 · 이 시험은 그 뒤 세상의 일을 본다)
        List<int> Unwatched(Sim s, Device stove, double hours)
        {
            var L = s.Layout; var m = s.Get<Mind>(); var st = s.Get<Structure>();
            int room = L.Room[stove.Cell], mess = L.Rooms.First(r => r.Type == "Mess").Cells.First(c => L.Kind[c] == CellKind.Floor && L.FurnitureAt[c] < 0);
            var locked = new List<int>();
            Span<int> nb = stackalloc int[4];
            for (int d = 0; d < L.Doors.Count; d++)
            {
                L.Neighbors(L.Doors[d].Cell, nb);
                foreach (int j in nb) if (j >= 0 && L.Room[j] == room) { st.LockDoor(d, s.Tick + Time.Hours(hours)); locked.Add(d); break; }
            }
            foreach (var a in m.Agents.Where(a => a.IsPerson && L.Room[a.C.Cell] == room)) { a.C.Cell = mess; s.Get<Life>().Move(a.C.Body, mess); a.Plan.Clear(); a.Path.Clear(); }
            return locked;
        }

        // ── 사람의 실수: 조리대를 켜 둔 채 자리를 떴다 → 그 위 음식이 눌어 불 — 불의 원인 사슬 뿌리가 그 사람 (주컴퓨터를 꺼 연기를 못 보고 · 아무도 주방을 지나지 않는다) ──
        {
            var s = ShipWorld.Crewed("Didim", 9, 6, 2);
            var net = s.Get<Network>(); var ch = s.Get<Chemistry>(); var m = s.Get<Mind>(); var so = s.Get<Society>(); var f = s.Get<Field>();
            net.SetOn(net.Devices.First(d => d.Type == "MainComputer").Id, false);   // 예전 Age(1.0)는 로봇이 곧 고쳐 주컴퓨터가 다시 돌았다
            s.Run(Time.Minutes(10));
            var (stoveDev, culprit, c0) = LeftOn(s);
            Unwatched(s, stoveDev, 8);
            long lit = -1;
            for (long t = 0; t < Time.Hours(8) && lit < 0; t++)
            {
                s.Step(); if (ch.Ignitions > 0) lit = t;
                if (Debug && t % 1500 == 0) Console.WriteLine($"    {t / 1500}시간 · 조리대 켬 {net.On[stoveDev.Id]} 공급 {net.Supply[stoveDev.Id]:0.00} · 겉면 {f.Tf[stoveDev.Cell]:0}K · 공기 {f.Temp(stoveDev.Cell):0}K · 음식 {f.Stored(f.T.sFood, stoveDev.Cell):0.0}몰");
            }
            var wk = s.Get<Work>();
            double foodOff = wk.Other(s, (int)Res.Food), foodNow = wk.Stock((int)Res.Food);
            int fire = 0;
            for (int c = 0; c < ch.FireCause.Length && fire == 0; c++) if (ch.FireCause[c] != 0) fire = ch.FireCause[c];
            int root = so.Culprit(s, fire, -1);
            if (Debug) Console.WriteLine($"    불 {(lit >= 0 ? Time.Clock(lit) : "없음")} · {s.Causes.Story(fire)}");
            Check("사람의 실수: 켜 둔 조리대가 그 위 음식을 눌려 불을 낸다 — 불의 원인 사슬 뿌리가 켜 둔 사람",
                lit > 0 && root == culprit && culprit >= 0,
                $"{s.Causes.Text(c0)} → 불 {(lit > 0 ? $"{lit / 1500.0:0.0}시간 뒤" : "안 남")} · 뿌리 {(root >= 0 ? m.NameOf(root) : "-")}");
            Check("눌은 음식 · 탄 음식도 장부에 적힌다: 켜 둔 조리대의 음식이 눌어 불이 난 뒤에도 처음 재고 + 적은 흐름 = 지금 식량 (예전엔 눌어 없어진 몫을 적지 않아 0.24몰 어긋났다)",
                lit > 0 && Math.Abs(foodOff) <= 1e-9 * Math.Max(1, foodNow), $"어긋남 {foodOff:0.0000000}몰 / 식량 {foodNow:0}몰 · 탄 것 {-s.Book.Of(Res.Food, Flow.Burned):0.000}몰");
        }

        // ── 켜 둔 조리대: 주컴퓨터가 눌는 연기를 먼저 보고 조리대 전원을 내린다 → 불이 붙지 않는다 / 주컴퓨터가 멈췄으면 불이 나고 · 불을 끈 이가 조리대 전원도 내린다 → 다시 붙지 않는다 ──
        {
            (long lit, long litAfter, byte onAfter, bool cut, string how) Stove(bool computer)
            {
                var s = ShipWorld.Crewed("Didim", 9, 6, 2);
                var net = s.Get<Network>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var st = s.Get<Structure>();
                if (!computer) net.SetOn(net.Devices.First(d => d.Type == "MainComputer").Id, false);
                s.Run(Time.Minutes(10));
                var (stove, _, _) = LeftOn(s);
                var locked = Unwatched(s, stove, 4);   // 주컴퓨터의 카메라만 본다 (돌면) — 멈췄으면 불이 날 때까지 아무도 모른다
                long outAt = -1; bool came = false;
                for (long t = 0; t < Time.Hours(4) && outAt < 0; t++)
                {
                    s.Step();
                    if (!computer && !came && ch.Ignitions > 0)
                    {
                        // 불이 나면 문을 풀고 주방에 로봇 하나 — 주컴퓨터 없이도 제 눈으로 불을 보고 끈다
                        came = true;
                        foreach (int d in locked) st.UnlockDoor(d);
                        int spot = s.Layout.Rooms[s.Layout.Room[stove.Cell]].Cells.Where(c => s.Layout.Kind[c] == CellKind.Floor && s.Layout.FurnitureAt[c] < 0).OrderBy(c => m.Dist(c, stove.Cell)).Skip(3).First();
                        m.AddRobot(s, spot, AgentKind.Robot, spot);
                    }
                    if (ch.Ignitions > 0 && ch.BurningCount == 0 || computer && net.On[stove.Id] == 0) outAt = s.Tick;
                }
                s.Run(Time.Minutes(3));                                       // 끈 이가 손을 털 때까지
                long lit = ch.Ignitions;
                byte onAfter = net.On[stove.Id];
                var ext = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Extinguished);
                string how = ext.Kind == FactKind.Extinguished ? s.Causes.Text(ext.Cause) : sm.Log.LastOrDefault(x => x.text.Contains("전원을 내렸다")).text ?? "";
                var off = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Switched && x.Other == stove.Id && ext.Kind == FactKind.Extinguished && x.Actor == ext.Actor);
                if (off.Kind == FactKind.Switched) how += $" · {s.Causes.Text(off.Cause)}";   // 불 곁에 닿자 손을 뻗어 먼저 끈 조리대 (DF5)
                s.Run(Time.Hours(1));
                if (Debug) foreach (var x in s.Facts.Since(0).Where(x => x.Kind is FactKind.Ignition or FactKind.FireOut or FactKind.Extinguished)) Console.WriteLine($"    {(computer ? "주컴퓨터" : "멈춤")} {Time.Clock(x.Tick)} {x.Kind} · {s.Causes.Story(x.Cause)}");
                return (lit, ch.Ignitions, onAfter, sm.StovesCut > 0, how);
            }
            var watchedT = Task.Run(() => Stove(true)); var alone = Stove(false); var watched = watchedT.Result;
            Check("켜 둔 조리대: 주컴퓨터가 카메라로 쓰는 이 없이 켜진 조리대를 알아채 (15분 — DF5 · 예전엔 눌는 연기) 전원을 내린다 → 불이 붙지 않는다",
                watched.lit == 0 && watched.litAfter == 0 && watched.onAfter == 0 && watched.cut, $"불붙음 {watched.lit} · 조리대 {(watched.onAfter != 0 ? "켜짐" : "꺼짐")} · \"{watched.how}\"");
            Check("켜 둔 조리대 (주컴퓨터가 멈췄다): 불이 나고 · 불을 끈 이가 조리대 전원도 내린다 → 한 시간 동안 다시 붙지 않는다",
                alone.lit >= 1 && alone.onAfter == 0 && alone.litAfter == alone.lit && alone.how.Contains("조리대"),
                $"불붙음 {alone.lit} → {alone.litAfter} · 그 뒤 조리대 {(alone.onAfter != 0 ? "켜짐" : "꺼짐")} · \"{alone.how}\"");
        }
    }
}
