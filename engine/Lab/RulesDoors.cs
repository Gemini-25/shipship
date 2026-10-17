using ShipEngine;

namespace Lab;

/// <summary>열어 달라던 문 — 지나가지 않고 떠나면 요청을 거둔다 · 주컴퓨터는 아무도 없는 열린 바깥문을 닫는다 (화살호 시드 11: 바깥문이 일곱 시간 열려 세 사람이 숨졌다).</summary>
public static partial class Program
{
    private static void DoorRequestRules()
    {
        if (!Do("문 요청")) return;
        Console.WriteLine("\n[문 요청 — 열어 달라던 문]");

        // 바깥문 앞의 로봇에게 바깥을 살피게 했다가 (밸브가 열리는 중) 다른 일을 맡긴다
        (float target, float open, double lost, int closed) Run(bool computer)
        {
            var s = ShipWorld.Create("Hwasal", 11);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var st = s.Get<Structure>(); var net = s.Get<Network>();
            var L2 = s.Layout;
            if (!computer) net.Age(net.Devices.First(d => d.Type == "MainComputer").Id, Part.Electric, 1.0);
            var ext = L2.Doors.First(dd => dd.External);
            int ex = L2.X(ext.Cell), ey = L2.Y(ext.Cell);
            var around = new[] { (ex + 1, ey), (ex - 1, ey), (ex, ey + 1), (ex, ey - 1) }.Where(p => L2.In(p.Item1, p.Item2)).Select(p => L2.Index(p.Item1, p.Item2)).ToList();
            int outCell = around.First(c => L2.Kind[c] == CellKind.Outside), inCell = around.First(c => L2.Room[c] >= 0);
            int r = m.AddRobot(s, inCell, AgentKind.Robot, inCell);
            s.Run(5);
            m.Assign(s, r, GoalKind.Investigate, outCell, new Belief { Kind = BeliefKind.Breach, Cell = outCell, Subject = -1, Conf = 1 }, s.Tick + Time.Minutes(30));
            for (int k = 0; k < Time.Minutes(2) && st.DoorTarget[ext.Id] <= 0; k++) s.Step();
            s.Run(10);                                                    // 밸브가 쉬익거리는 중
            // 다른 일: 배 안쪽 먼 곳을 살핀다 (바깥문은 지나가지 않는다)
            int far = L2.Rooms.Where(rm => rm.Cells.Length > 0 && rm.Id != L2.Room[inCell]).OrderByDescending(rm => m.Dist(inCell, rm.Cells[0])).First().Cells[0];
            m[r].Beliefs.Clear();                                          // 바깥이 멀쩡하다는 소식 (사건이 끝났다)
            m.Assign(s, r, GoalKind.Investigate, far, new Belief { Kind = BeliefKind.Smoke, Cell = far, Subject = -1, Conf = 1 }, s.Tick + Time.Minutes(30));
            double mol0 = L2.Rooms.Sum(rm => rm.Cells.Sum(c => f.Moles(c)));
            bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
            for (int k = 0; k < Time.Minutes(1); k++)
            {
                s.Step();
                if (Debug && k % 5 == 0) Console.WriteLine($"    {k} 로봇 {L2.X(m[r].C.Cell)},{L2.Y(m[r].C.Cell)} {m.Thought(r)} · 기다리는 문 {m[r].C.WaitDoor} 연 문 {m[r].C.OpenedDoor} · 바깥문 {ext.Id} 목표 {st.DoorTarget[ext.Id]:0.0} 열림 {f.DoorOpen[ext.Id]:0.0} 밸브 {f.DoorBleed[ext.Id]} · 길 {m[r].Path.Count - m[r].PathIdx}");
            }
            float target = st.DoorTarget[ext.Id];
            s.Run(Time.Minutes(20));
            double mol1 = L2.Rooms.Sum(rm => rm.Cells.Sum(c => f.Moles(c)));
            return (target, f.DoorOpen[ext.Id], mol0 - mol1, (int)s.Get<ShipMind>().OuterClosed);
        }
        var alone = Run(false);
        Check("문 요청: 바깥문을 열어 달라던 로봇이 다른 일로 떠나면 요청을 거둔다 → 바깥문이 열리지 않는다 (주컴퓨터가 멈췄어도)",
            alone.target == 0 && alone.open == 0 && alone.lost < 200,
            $"떠난 뒤 1분: 바깥문 목표 {alone.target:0.0} · 20분 뒤 열림 {alone.open:0.0} · 빠진 공기 {alone.lost:0}몰");

        // 주컴퓨터: 누가 열어 두고 간 바깥문 — 곁에 아무도 없으면 닫는다
        {
            var s = ShipWorld.Create("Hwasal", 11);
            var f = s.Get<Field>(); var st = s.Get<Structure>(); var sm = s.Get<ShipMind>();
            var ext = s.Layout.Doors.First(dd => dd.External);
            s.Run(5);
            st.RequestDoor(ext.Id, 1); f.SetDoor(ext.Id, 1);              // 열린 채 남았다
            long closedAt = -1;
            for (int k = 0; k < Time.Minutes(5) && closedAt < 0; k++) { s.Step(); if (f.DoorOpen[ext.Id] <= 0) closedAt = k; }
            Check("바깥문 지키기: 열린 채 아무도 없는 바깥문을 주컴퓨터가 닫는다 (선외에 나간 이 · 기다리는 이가 없을 때)",
                closedAt >= 0 && sm.OuterClosed >= 1 && sm.Log.Any(x => x.text.Contains("바깥문")),
                closedAt >= 0 ? $"{Time.ToSeconds(closedAt):0}초 만에 닫음 · \"{sm.Log.LastOrDefault(x => x.text.Contains("바깥문")).text}\"" : "5분 동안 열린 채");
        }
    }
}
