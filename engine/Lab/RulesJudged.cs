using ShipEngine;

namespace Lab;

/// <summary>판단 일지 (docs/MIND.md 4단 — 관찰용 · 쓰기만 · 찍지 않는다): 로봇 · 사람 여덟 칸 · 주컴퓨터 512칸 — 화면의 로봇 카드 '최근 판단' · 말 창의 재료.</summary>
public static partial class Program
{
    private static void JudgedRules()
    {
        if (!Do("판단 일지")) return;
        Console.WriteLine("\n[판단 일지 — 맡음 · 세움 · 해냄 · 막힘 · 주컴퓨터가 맡긴 까닭]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 불을 맡은 로봇: 맡음(주컴퓨터) → 세움(불 끄기 · 첫 손 걸음) → 해냄 · 막힘 · 그만둠 가운데 하나가 차례로 · 주컴퓨터 일지에 그 로봇 · 그 사건의 맡김과 까닭 · 되살린 세계는 빈 채로 ──
        {
            var s = Worlds.Full(lay, 41);
            var m = s.Get<Mind>(); var cm = s.Get<ShipMind>(); var ch = s.Get<Chemistry>();
            m.Journal = true; cm.NoteOn = true;
            int r = m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            m.AddPerson(s, At(20, 8));
            s.Run(12);
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Setup, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            s.Run(Time.Minutes(30));
            var seq = new List<Judgment>();
            for (int k = (m[r].Judged?.Count ?? 0) - 1; k >= 0; k--) seq.Add(m[r].Judged!.Newest(k));
            int iA = seq.FindIndex(j => j.Out == JudgeOut.Assigned && j.Goal == GoalKind.FightFire && j.By == -2);
            int iS = seq.FindIndex(Math.Max(0, iA), j => j.Out == JudgeOut.Set && j.Goal == GoalKind.FightFire && j.By == -2 && j.Method != 255);
            int iE = iS >= 0 ? seq.FindIndex(iS, j => j.Out is JudgeOut.Done or JudgeOut.Blocked or JudgeOut.Dropped && j.Goal == GoalKind.FightFire) : -1;
            var fire = cm.Incidents.FirstOrDefault(x => x.Kind == IncidentKind.Fire);
            bool note = false; byte why = 0;
            for (int k = 0; k < (cm.Notes?.Count ?? 0); k++) { var n = cm.Notes!.Newest(k); if (n.Kind == NoteKind.Assign && n.Robot == r && n.Incident == fire.Id) { note = true; why = n.Reason; } }
            var twin = s.Clone(); int cloned = twin.Get<Mind>()[r].Judged?.Count ?? 0; twin.Run(Time.Minutes(5)); cloned += twin.Get<Mind>().Agents.Sum(x => x.Judged?.Count ?? 0) + (twin.Get<ShipMind>().Notes?.Count ?? 0);
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    불 {ch.BurningCount} · 끔 {m.Extinguished} · 그만둔 때 {string.Join(",", seq.Where(j => j.Out == JudgeOut.Dropped).Select(j => $"{Time.Clock(j.Tick)}→{(GoalKind)j.Method}"))} · 사건 {string.Join(",", cm.Incidents.Select(x => $"{x.Kind}:{x.Active}"))}");
            Check("판단 일지: 불을 맡은 로봇의 일지에 맡음(주컴퓨터) → 세움(불 끄기 · 첫 손 걸음) → 해냄 · 막힘 · 그만둠 가운데 하나가 차례로 · 주컴퓨터 일지에 그 로봇 · 그 사건의 맡김과 까닭 · 되살린 세계는 빈 채로 (이어 돌려도 쓰지 않는다)",
                iA >= 0 && iS > iA && iE > iS && note && ShipMind.ReasonText(why) != "" && cloned == 0,
                $"차례 {string.Join(" → ", seq.Select(j => $"{j.Out}:{j.Goal}"))} · 주컴퓨터 맡김 {note} ({ShipMind.ReasonText(why)}) · 되살린 세계 {cloned}칸");
        }

        // ── 못 세움은 한 칸: 같은 (목표 · 대상)을 되풀이해 못 세우면 때만 고친다 · 여덟 칸을 넘으면 가장 오래된 것부터 밀린다 ──
        {
            var ring = new Ring<Judgment>(8);
            for (int i = 0; i < 11; i++) ring.Add(new Judgment { Tick = i, Out = JudgeOut.Set, Goal = GoalKind.Eat });
            bool roll = ring.Count == 8 && ring.Newest(0).Tick == 10 && ring.Newest(7).Tick == 3;
            var s = Worlds.Full(lay, 42);
            var m = s.Get<Mind>(); m.Journal = true;
            int p = m.AddPerson(s, At(20, 8));
            s.Run(5);
            foreach (var fu in s.Layout.Furniture.Where(f => f.Type is "MealDispenser")) foreach (int c in fu.Cells) s.Get<Field>().TakeStore(c, s.Tables.sFood, 1e9);   // 먹을 것이 없다
            m[p].C.Hunger = 1;
                        s.Run(Time.Minutes(20));
            var jr = m[p].Judged; int cant = 0; for (int k = 0; k < (jr?.Count ?? 0); k++) if (jr!.Newest(k).Out == JudgeOut.CantPlan) cant++;
            var kinds = new List<string>(); for (int k = (jr?.Count ?? 0) - 1; k >= 0; k--) { var j = jr!.Newest(k); kinds.Add($"{j.Out}:{j.Goal}"); }
            Check("판단 일지: 같은 (목표 · 대상)을 되풀이해 못 세우면 한 칸 (때만 고친다) · 여덟 칸을 넘으면 가장 오래된 것부터 밀린다",
                roll && cant <= 2 && (jr?.Count ?? 0) <= 8, $"고리 밀림 {roll} · 20분 동안 못 세움 칸 {cant} · [{string.Join(" ", kinds)}]");
        }
    }
}
