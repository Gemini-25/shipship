using ShipEngine;

namespace Lab;

/// <summary>에어락 차례: 안에서 남이 바깥문 쪽으로 공기를 빼는 동안 밖에서 안쪽 문을 열어 달라 하지 않는다 —
/// 두 문 밸브가 번갈아 열리면 배 공기가 에어락을 지나 우주로 샌다 (디딤호 시드 11: 로봇 둘이 30분마다 이렇게 엇갈려 12시간 동안 예비 공기를 다 버렸다).</summary>
public static partial class Program
{
    private static void AirlockRules()
    {
        if (!Do("에어락 차례")) return;
        Console.WriteLine("\n[에어락 차례]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var s = ShipWorld.Create("Hwasal", 11);
        var m = s.Get<Mind>(); var f = s.Get<Field>(); var st = s.Get<Structure>();
        var L2 = s.Layout;
        var ext = L2.Doors.First(dd => dd.External);
        int ex = L2.X(ext.Cell), ey = L2.Y(ext.Cell);
        var around = new[] { (ex + 1, ey), (ex - 1, ey), (ex, ey + 1), (ex, ey - 1) }.Where(p => L2.In(p.Item1, p.Item2)).Select(p => L2.Index(p.Item1, p.Item2)).ToList();
        int outCell = around.First(c => L2.Kind[c] == CellKind.Outside), inCell = around.First(c => L2.Room[c] >= 0);
        int lockRoom = L2.Room[inCell];
        int inner = st.RoomDoors(lockRoom).First(e => !L2.Doors[e].External);
        // 안쪽 문 너머 배 쪽 칸
        Span<int> nb = stackalloc int[4]; L2.Neighbors(L2.Doors[inner].Cell, nb);
        int shipSide = -1; foreach (int j in nb) if (j >= 0 && L2.Room[j] >= 0 && L2.Room[j] != lockRoom) shipSide = j;
        int ra = m.AddRobot(s, inCell, AgentKind.Robot, inCell);
        int rb = m.AddRobot(s, shipSide, AgentKind.Robot, shipSide);
        s.Run(5);
        // 살필 곳: 에어락에서 외판을 따라 8칸쯤 떨어진 바깥 칸 (나가야만 보인다)
        int far = Enumerable.Range(0, L2.N).Where(c => L2.Kind[c] == CellKind.Outside && Math.Abs(L2.X(c) - ex) + Math.Abs(L2.Y(c) - ey) is >= 7 and <= 10)
            .OrderBy(c => Math.Abs(L2.X(c) - ex) + Math.Abs(L2.Y(c) - ey)).ThenBy(c => c).First();
        var why = new Belief { Kind = BeliefKind.Breach, Cell = far, Subject = -1, Conf = 1 };
        m.Assign(s, ra, GoalKind.Investigate, far, why, s.Tick + Time.Minutes(30));
        s.Run(20);                                                                   // A가 바깥문 앞에서 밸브를 연 뒤
        m.Assign(s, rb, GoalKind.Investigate, far, why, s.Tick + Time.Minutes(30));   // B도 나가려 한다
        double ship0 = L2.Rooms[L2.Room[shipSide]].Cells.Sum(c => f.Moles(c));
        long outAt = -1; bool bothValves = false;
        for (int k = 0; k < Time.Minutes(8) && outAt < 0; k++)
        {
            s.Step();
            if (f.DoorBleed[inner] != 0 && m[ra].C.WaitDoor == ext.Id) bothValves = true;   // A가 바깥문을 기다리는 동안 안쪽 밸브가 열렸다 (에어락이 다시 찬다)
            if (L2.Kind[m[ra].C.Cell] == CellKind.Outside) outAt = k;
            if (Debug && k % 25 == 0) Console.WriteLine($"    {k / 25}분 에어락 {f.Pressure(inCell):0.0}kPa · 바깥 밸브 {f.DoorBleed[ext.Id]} 목표 {st.DoorTarget[ext.Id]:0.0} · 안쪽 밸브 {f.DoorBleed[inner]} 열림 {f.DoorOpen[inner]:0.0} 목표 {st.DoorTarget[inner]:0.0} · A 칸 {(m[ra].C.Cell == inCell ? "에어락" : L2.Room[m[ra].C.Cell].ToString())} 기다림 {m[ra].C.WaitDoor}/{m[ra].C.WaitTicks} · B 기다림 {m[rb].C.WaitDoor}/{m[rb].C.WaitTicks} 칸 {L2.Room[m[rb].C.Cell]} · A 목표 {m[ra].C.Goal}@{(m[ra].C.GoalCell >= 0 ? L2.Kind[m[ra].C.GoalCell] + "(" + L2.X(m[ra].C.GoalCell) + "," + L2.Y(m[ra].C.GoalCell) + ") 응력 " + st.Stress[m[ra].C.GoalCell].ToString("0.00") + " 바깥벽 " + L2.Hull[m[ra].C.GoalCell] : "-")}");
        }
        double lost = ship0 - L2.Rooms[L2.Room[shipSide]].Cells.Sum(c => f.Moles(c));
        // 비운 에어락의 안쪽 벽: 압력 경계라 끼익거리지 않는다 (받치러 가지 않는다)
        float maxStress = 0;
        Span<int> nb2 = stackalloc int[4];
        foreach (int c in L2.Rooms[lockRoom].Cells) { L2.Neighbors(c, nb2); foreach (int j in nb2) if (j >= 0 && L2.Kind[j] == CellKind.Wall && !L2.Hull[j]) maxStress = Math.Max(maxStress, st.Stress[j]); }
        if (Debug) Console.WriteLine($"    A {m.Thought(ra)} · '{m[ra].LastFail}' / B {m.Thought(rb)} · '{m[rb].LastFail}' · 에어락 {f.Pressure(inCell):0.0}kPa");
        Check("에어락 차례: 안에서 남이 바깥문 쪽으로 공기를 빼는 동안 밖의 로봇은 안쪽 문을 열어 달라 하지 않고 기다린다 → 먼저 들어간 로봇이 나간다 · A가 기다리는 동안 안쪽 밸브가 열리지 않는다 · 비운 에어락의 안쪽 벽은 압력 경계라 끼익거리지 않는다 (받치러 돌아서지 않는다)",
            outAt >= 0 && !bothValves && maxStress < 0.4f,
            outAt >= 0 ? $"{Time.ToSeconds(outAt) / 60:0.0}분 만에 나감 · 안쪽 밸브 엇갈림 {bothValves} · 비운 에어락 안쪽 벽 응력 최고 {maxStress:0.00} · 옆 방 공기 {lost:0}몰 줄어듦" : $"8분 동안 못 나감 · 안쪽 밸브 엇갈림 {bothValves} · 에어락 {f.Pressure(inCell):0.0}kPa · 옆 방 공기 {lost:0}몰 줄어듦");
    }
}
