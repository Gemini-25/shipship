using ShipEngine;

namespace Lab;

/// <summary>⑫ 이야기꾼 — 함장이 고르는 위험도 · 사람 사이 씨앗(실수 · 묵은 앙금). docs/DEPTH.md</summary>
public static partial class Program
{
    private static void SeedRules()
    {
        if (!Do("위험도")) return;
        Console.WriteLine("\n[⑫ 위험도 · 사람 사이 씨앗]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 가혹함은 같은 원인을 키운다: 같은 배 · 같은 순간 · 같은 난수에서 가혹함만 2와 7 — 운석이 더 무겁고 · 긴장 목표가 높고 · 쉬는 틈이 짧다 ──
        {
            (float rock, double target, double pending) At(byte level)
            {
                var s = ShipWorld.Crewed("Hwasal", 8, 3, 1);
                var st = s.Get<Storyteller>(); st.Level = level;
                s.Run(5);
                st.Plant(s, SeedKind.Rock);
                return (st.Seeds[^1].Size, st.Target(s.Tick), st.Pending);
            }
            var lo = At(2); var hi = At(7);
            Check("가혹함은 같은 원인을 키운다: 같은 순간 · 같은 난수에서 가혹함 7의 운석은 2보다 훨씬 무겁고 · 이야기꾼이 바라는 긴장도 높다",
                hi.rock > lo.rock * 3 && hi.target > lo.target * 3 && hi.pending > lo.pending * 3,
                $"운석 {lo.rock:0.00} → {hi.rock:0.00}kg · 긴장 목표 {lo.target:0.0} → {hi.target:0.0} · 터질 긴장 {lo.pending:0.0} → {hi.pending:0.0}");
        }

        // ── 가혹함: 함장이 고른 만큼 — 같은 배 · 같은 성격, 가혹함 2와 7의 18시간 (작은 배 화살호 — 이틀을 기다리지 않아도 갈린다) ──
        {
            (int seeds, double tension, int events) Run(byte level)
            {
                var s = ShipWorld.Crewed("Hwasal", 8, 3, 1);
                var st = s.Get<Storyteller>(); st.Enabled = true; st.Style = StoryStyle.Balanced; st.Level = level;
                double sum = 0; int n = 0;
                for (long t = 0; t < Time.Hours(18); t++) { s.Step(); if (t % 250 == 0) { sum += st.Tension; n++; } }
                int events = (int)(s.Facts.CountOf(FactKind.Ignition) + s.Facts.CountOf(FactKind.MachineFail) + s.Facts.CountOf(FactKind.WallBreach) + s.Facts.CountOf(FactKind.Hurt) + s.Facts.CountOf(FactKind.Impact));
                return (st.Seeds.Count, sum / n, events);
            }
            var loT = Task.Run(() => Run(2)); var hi = Run(7); var lo = loT.Result;
            Check("위험도: 함장이 고른 가혹함만큼 — 가혹함 7은 2보다 원인을 더 자주 심고 · 긴장이 높고 · 일이 더 벌어진다",
                // 가혹하면 일찍 큰 일이 터져 긴장이 높은 동안 이야기꾼이 쉰다 (긴장이 목표를 넘으면) — 그래도 더 적게 심지는 않는다
                hi.seeds > lo.seeds && hi.tension > lo.tension * 2 && hi.events > lo.events,
                $"가혹함 2: 씨앗 {lo.seeds} · 긴장 {lo.tension:0.0} · 일 {lo.events} / 가혹함 7: 씨앗 {hi.seeds} · 긴장 {hi.tension:0.0} · 일 {hi.events} · {Storyteller.LevelName(2)} / {Storyteller.LevelName(7)}");
        }

        // ── 함장의 판: 관전 화면에서 이야기꾼 성격 · 가혹함을 바꾼다 (1~8 안) · 바꾼 것이 잠깐 뜬다 ──
        {
            var show = ShipView.LiveShow.Crewed("Didim", 3);
            var st = show.Sim.Get<Storyteller>();
            var style0 = st.Style;
            show.CycleStyle();
            bool changed = st.Style != style0;
            for (int k = 0; k < 10; k++) show.Harsher(+1);
            byte top = st.Level;
            for (int k = 0; k < 10; k++) show.Harsher(-1);
            var fr = show.Render();
            Check("함장의 판: 관전 화면에서 이야기꾼 성격을 돌리고 · 가혹함을 1~8 안에서 올리고 내린다 · 바꾼 것이 화면에 잠깐 뜬다",
                changed && top == 8 && st.Level == 1 && fr.Notice.Contains("가혹함 1") && fr.Storyteller.Contains(Storyteller.StyleName(st.Style)),
                $"{fr.Notice} · 가장 높이 {top}");
        }

        // ── 사람의 실수: 조리대를 켜 둔 채 자리를 떴다 → 그 위 음식이 눌어 불 — 불의 원인 사슬 뿌리가 그 사람 (주컴퓨터가 멈춰 연기를 못 본다) ──
        {
            var s = ShipWorld.Crewed("Didim", 9, 6, 2);
            var st = s.Get<Storyteller>(); var net = s.Get<Network>(); var ch = s.Get<Chemistry>(); var m = s.Get<Mind>(); var so = s.Get<Society>();
            net.Age(net.Devices.First(d => d.Type == "MainComputer").Id, Part.Electric, 1.0);
            s.Run(Time.Minutes(10));
            bool planted = st.Plant(s, SeedKind.Mistake);
            var seed = st.Seeds[^1];
            int culprit = s.Causes[seed.Cause].Actor;
            long lit = -1;
            var stoveDev = net.Devices.First(d => d.Cell == s.Causes[seed.Cause].Cell || d.Def.Role == "stove" && net.On[d.Id] != 0);
            var f = s.Get<Field>();
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
                planted && lit > 0 && root == culprit && culprit >= 0,
                $"{s.Causes.Text(seed.Cause)} → 불 {(lit > 0 ? $"{lit / 1500.0:0.0}시간 뒤" : "안 남")} · 뿌리 {(root >= 0 ? m.NameOf(root) : "-")}");
        }

        // ── 켜 둔 조리대: 주컴퓨터가 눌는 연기를 먼저 보고 조리대 전원을 내린다 → 불이 붙지 않는다 / 주컴퓨터가 멈췄으면 불이 나고 · 불을 끈 이가 조리대 전원도 내린다 → 다시 붙지 않는다 ──
        {
            (long lit, long litAfter, byte onAfter, bool cut, string how) Stove(bool computer)
            {
                var s = ShipWorld.Crewed("Didim", 9, 6, 2);
                var st = s.Get<Storyteller>(); var net = s.Get<Network>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>();
                if (!computer) net.Age(net.Devices.First(d => d.Type == "MainComputer").Id, Part.Electric, 1.0);
                s.Run(Time.Minutes(10));
                st.Plant(s, SeedKind.Mistake);
                var stove = net.Devices.First(d => d.Def.Role == "stove" && net.On[d.Id] != 0);
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
            var watched = Stove(true); var alone = Stove(false);
            Check("켜 둔 조리대: 주컴퓨터가 눌는 연기를 먼저 알아채 조리대 전원을 내린다 → 불이 붙지 않는다",
                watched.lit == 0 && watched.litAfter == 0 && watched.onAfter == 0 && watched.cut, $"불붙음 {watched.lit} · 조리대 {(watched.onAfter != 0 ? "켜짐" : "꺼짐")} · \"{watched.how}\"");
            Check("켜 둔 조리대 (주컴퓨터가 멈췄다): 불이 나고 · 불을 끈 이가 조리대 전원도 내린다 → 한 시간 동안 다시 붙지 않는다",
                alone.lit >= 1 && alone.onAfter == 0 && alone.litAfter == alone.lit && alone.how.Contains("조리대"),
                $"불붙음 {alone.lit} → {alone.litAfter} · 그 뒤 조리대 {(alone.onAfter != 0 ? "켜짐" : "꺼짐")} · \"{alone.how}\"");
        }

        // ── 묵은 앙금: 거친 두 사람 사이에 원한의 씨앗만 심는다 — 기억 · 원한이 남고 그 뒤는 그들이 정한다 ──
        {
            var s = ShipWorld.Crewed("Didim", 10, 6, 2);
            var st = s.Get<Storyteller>(); var so = s.Get<Society>(); var m = s.Get<Mind>();
            s.Run(Time.Minutes(10));
            bool planted = st.Plant(s, SeedKind.OldGrudge);
            var seed = st.Seeds[^1];
            int a2 = s.Causes[seed.Cause].Actor;
            int a1 = so.Memories.FirstOrDefault(x => x.Cause == seed.Cause && x.About == a2).Owner;
            Check("묵은 앙금: 두 사람 사이에 원한의 씨앗 — 원한 · 기억이 남는다 (따질지 · 풀지는 그들이)",
                planted && so.GrudgeOf(a1, a2) >= 0.3f && so.Memories.Any(x => x.Owner == a1 && x.Topic == Topic.Blame && x.About == a2),
                $"{s.Causes.Text(seed.Cause)} · 원한 {so.GrudgeOf(a1, a2):0.00} / {so.GrudgeOf(a2, a1):0.00}");
        }
    }
}
