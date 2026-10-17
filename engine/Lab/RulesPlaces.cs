using ShipEngine;

namespace Lab;

/// <summary>⑧ 마음 (바퀴 2) — 장소의 기억: 무서운 일을 겪은 방은 그 사람이 한동안 꺼리고 · 별일 없이 지내면 풀린다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void PlaceRules()
    {
        if (!Do("장소의 기억")) return;
        Console.WriteLine("\n[⑧ 장소의 기억]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 맞은 곳을 꺼린다: 왼쪽 방 책장 곁에서 맞은 이는 그 뒤 세 시간 그 방에서 쉬지 않는다 (먼 오른쪽 방 책장으로 간다)
        //    대조: 같은 사람 · 같은 시드 · 싸움이 없던 세계에서는 곁의 왼쪽 방 책장에서 쉰다 (쉬는 일 뽑기 난수도 같다) · 때린 이는 꺼리지 않는다 ──
        {
            var legend = Worlds.MindLegend; legend['k'] = "Bookshelf";
            var lay = Worlds.Ascii(Worlds.MindShip.Replace("#..p.b....D.............#", "#..p.bk...D.............#")
                                                  .Replace("#.........#.............#", "#.........#..........k..#"), legend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            int left = lay.Room[At(5, 2)];
            (long fights, float dreadY, float dreadX, int restLeft, int rest) Run(bool fight)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int x = m.AddPerson(s, At(5, 2)), y = m.AddPerson(s, At(7, 2));
                m.SetPersonality(x, 0.9f, 0.1f, 0.1f, 0.05f, 0.95f);              // 거칠고 불안한 이
                m.SetPersonality(y, 0.9f, 0.1f, 0.1f, 0.5f, 0.3f);
                s.Run(2);
                foreach (int p in new[] { x, y }) { m[p].C.SkRepair = 0; m[p].C.Bored = 0; }
                if (fight) { so.Grudge[x * so.N + y] = 0.75f; m[x].C.Stress = 0.9f; m[x].C.Anger = 0.9f; } // 끝장까지는 아니다
                for (int k = 0; k < Time.Minutes(20) && so.Fights == 0; k++) { s.Step(); foreach (int p in new[] { x, y }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; m[p].C.Bored = 0; } }
                for (int k = 0; k < Time.Minutes(10); k++) { s.Step(); foreach (int p in new[] { x, y }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; m[p].C.Bored = 0; } }
                float dy = m.Dread(y, left), dx = m.Dread(x, left);
                so.Grudge[x * so.N + y] = 0;                                    // 그 뒤로는 다투지 않는다 (쉬는 자리만 본다)
                int restLeft = 0, rest = 0;
                for (int k = 0; k < Time.Hours(3); k++)
                {
                    s.Step();
                    foreach (int p in new[] { x, y }) { ref var c = ref m[p].C; c.Hunger = 0; c.Sleepy = 0; c.Thirst = 0; c.Lonely = 0; c.Bored = Math.Max(c.Bored, 0.8f); }
                    ref var cy = ref m[y].C;
                    if (cy.Goal == GoalKind.Relax && cy.StepIndex < m[y].Plan.Count && m[y].Plan[cy.StepIndex].Kind == StepKind.Use) { rest++; if (m.RoomOf(cy.Cell) == left) restLeft++; }
                }
                if (Debug) Console.WriteLine($"    {(fight ? "맞은 뒤" : "싸움 없음")}: 싸움 {so.Fights} · 꺼림 맞은 이 {dy:0.00} · 때린 이 {dx:0.00} · 왼쪽 방에서 쉰 몫 {restLeft}/{rest} · {m.Thought(y)}");
                return (so.Fights, dy, dx, restLeft, rest);
            }
            var hit = Run(true); var calm = Run(false);
            Check("맞은 곳을 꺼린다: 왼쪽 방에서 맞은 이는 그 뒤 세 시간 그 방에서 쉬지 않는다 (먼 방 책장으로) · 싸움이 없던 같은 사람은 곁의 그 방 책장에서 쉰다 · 때린 이는 꺼리지 않는다",
                hit.fights > 0 && hit.dreadY > 0.3f && hit.dreadX < 0.1f && hit.rest > 0 && hit.restLeft == 0 && calm.rest > 0 && calm.restLeft * 2 > calm.rest,
                $"꺼림 맞은 이 {hit.dreadY:0.00} · 때린 이 {hit.dreadX:0.00} / 왼쪽 방에서 쉰 몫: 맞은 뒤 {(hit.rest > 0 ? hit.restLeft * 100 / hit.rest : 0)}% · 싸움 없음 {(calm.rest > 0 ? calm.restLeft * 100 / calm.rest : 0)}%");
        }

        // ── 별일 없이 지내면 풀린다: 통로에서 크게 놀란 이 — 그 통로 작업대에서 여섯 시간 일하면 꺼림이 거의 사라지고 · 다른 방에서 지내면 남는다 (노출 · 소거) ──
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            int corridor = lay.Room[At(12, 8)];
            float Run(bool stay)
            {
                var s = Worlds.Full(lay, 8);
                var m = s.Get<Mind>();
                int p = m.AddPerson(s, At(12, 8));
                m.SetPersonality(p, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
                s.Run(1);
                for (int k = 0; k < Time.Minutes(2); k++) { s.Step(); m[p].C.Fear = 0.9f; }   // 크게 놀랐다 (통로에서)
                m[p].C.Fear = 0;
                int bench = lay.Furniture.First(f => f.Type == "Workbench").Id, dock = lay.Furniture.First(f => f.Type == "RobotDock" && lay.Room[f.Cells[0]] != corridor).Id;
                m[p].C.Station = stay ? bench : dock;                            // 통로 작업대 · 아니면 왼쪽 방의 자리
                m[p].C.ShiftHour = (int)Time.HourOfDay(s.Tick);
                float d0 = m.Dread(p, corridor);
                for (int k = 0; k < Time.Hours(6); k++) { s.Step(); ref var c = ref m[p].C; c.Hunger = 0; c.Sleepy = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0; c.Fear = 0; }
                if (Debug) Console.WriteLine($"    {(stay ? "통로에서 일함" : "다른 방에서 일함")}: 꺼림 {d0:0.00} → {m.Dread(p, corridor):0.00} · {m.Thought(p)} 방 {m.RoomOf(m[p].C.Cell)}");
                return m.Dread(p, corridor);
            }
            float stayed = Run(true), away = Run(false);
            Check("별일 없이 지내면 풀린다: 통로에서 크게 놀란 이가 그 통로에서 여섯 시간 일하면 꺼림이 거의 사라지고 · 다른 방에서 지내면 오래 남는다 (반감기 사흘 남짓)",
                stayed < 0.1f && away > 0.35f, $"통로에서 지냄 {stayed:0.00} · 다른 방에서 지냄 {away:0.00}");
        }
    }
}
