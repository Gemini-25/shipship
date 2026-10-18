namespace ShipEngine;

/// <summary>막힌 까닭 (⑧ 판단 엔진 — 사람 · 로봇이 남기고 주컴퓨터가 읽는다).</summary>
public enum SnagKind : byte { None, Far, NoPath, DoorJammed, DoorUnpowered, DoorHeld, DoorVacuum, DoorPressure, DoorStuckBefore, NoWayOut, NoItem, Taken, Other }

/// <summary>
/// ⑧ 판단 엔진 (가) 막힘 — 일이 막히면 까닭 · 자리 · 대상을 남긴다 (docs/ENGINE.md 판단 엔진).
/// <para>문이 안 열렸으면 그 까닭(④ Structure.WhyShut: 끼임 · 전기 없음 · 주컴퓨터가 잠근 밸브 · 너머가 우주로 트임 · 기압 차) ·
/// 길이 없으면 안 열린다고 믿는 문을 지날 수 있다고 치고 다시 찾아 그 길을 막은 첫 문과 그 까닭 · 물건이 없으면 무엇이.
/// 문 · 길 · 물건의 막힘은 원인 마디 '…가 …에서 막혔다 — 까닭'과 사실 Blocked로 (같은 까닭 · 같은 대상 · 같은 목표는 10분에 한 번) —
/// 주컴퓨터는 맡긴 로봇의 막힘을 읽어 같은 방법을 되풀이하지 않는다.</para>
/// </summary>
public sealed partial class Mind
{
    public long Snags;

    public static string SnagText(SnagKind k) => k switch
    {
        SnagKind.Far => "멀다", SnagKind.NoPath => "갈 길이 없다", SnagKind.DoorJammed => "문이 끼었다", SnagKind.DoorUnpowered => "문에 전기가 없어 기압 차를 이기지 못한다",
        SnagKind.DoorHeld => "주컴퓨터가 문의 압력 맞춤 밸브를 잠가 두었다", SnagKind.DoorVacuum => "문 너머가 우주로 트여 기압을 맞출 수 없다",
        SnagKind.DoorPressure => "기압 차로 문이 열리지 않는다", SnagKind.DoorStuckBefore => "지난번에 안 열린 문밖에 길이 없다", SnagKind.NoWayOut => "바깥으로 나갈 문이 없다", SnagKind.NoItem => "쓸 물건이 없다", SnagKind.Taken => "남이 먼저 했다", _ => "일이 틀어졌다",
    };

    /// <summary>문 · 길 · 물건의 막힘인가 (원인 마디 · 사실로 남길 만한).</summary>
    public static bool Telling(SnagKind k) => k is SnagKind.NoPath or SnagKind.DoorJammed or SnagKind.DoorUnpowered or SnagKind.DoorHeld or SnagKind.DoorVacuum or SnagKind.DoorPressure
        or SnagKind.DoorStuckBefore or SnagKind.NoWayOut or SnagKind.NoItem;

    public static bool DoorSnag(SnagKind k) => k is SnagKind.DoorJammed or SnagKind.DoorUnpowered or SnagKind.DoorHeld or SnagKind.DoorVacuum or SnagKind.DoorPressure;

    /// <summary>그 문이 왜 안 열리나 (④가 안다 — 열릴 수 있으면 None).</summary>
    private SnagKind WhyDoor(int d) => (_st?.WhyShut(d) ?? DoorShut.None) switch
    {
        DoorShut.Jammed => SnagKind.DoorJammed, DoorShut.Unpowered => SnagKind.DoorUnpowered, DoorShut.Held => SnagKind.DoorHeld,
        DoorShut.VacuumBeyond => SnagKind.DoorVacuum, DoorShut.Pressure => SnagKind.DoorPressure, _ => SnagKind.None,
    };

    /// <summary>막힘을 남긴다 — 까닭 · 자리 · 대상 · 때 · 그때의 목표 (같은 까닭 · 대상 · 목표가 10분 안이면 때만 고친다).</summary>
    private void NoteSnag(Sim sim, Agent a, SnagKind k, int cell, int subject)
    {
        if (k == SnagKind.None) return;
        ref var c = ref a.C;
        long t = sim.Tick;
        bool same = c.Snag == k && c.SnagSubject == subject && c.SnagGoal == c.Goal && c.SnagTarget == c.GoalTarget && t - c.SnagTick < Time.Minutes(10);
        c.Snag = k; c.SnagCell = cell; c.SnagSubject = subject; c.SnagTick = t; c.SnagGoal = c.Goal; c.SnagTarget = c.GoalTarget;
        if (same || !Telling(k)) return;
        Snags++;
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} {PlaceOf(cell)}에서 막혔다 — {SnagText(k)}", t, cell, c.GoalCause, 0, a.Id);
        sim.Facts.Emit(t, FactKind.Blocked, cell, (float)k, cause, a.Id, subject);
    }

    /// <summary>그 칸의 자리 이름 (방 · 문 · 벽이면 곁의 방).</summary>
    private string PlaceOf(int cell)
    {
        if (cell < 0 || cell >= L.N) return "어딘가";
        int r = RoomOf(cell);
        if (r >= 0) return L.Rooms[r].Name;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(cell, nb);
        string part = L.Kind[cell] == CellKind.Door ? "문" : "벽";
        if (L.Kind[cell] is CellKind.Door or CellKind.Wall) foreach (int j in nb) if (j >= 0 && RoomOf(j) >= 0) return $"{L.Rooms[RoomOf(j)].Name} {part}";
        return L.IsSpace(cell) ? "통로" : L.Kind[cell] == CellKind.Wall ? "벽 속" : "선체 바깥";
    }

    /// <summary>길이 없을 때 왜: 안 열린다고 믿는 문을 지날 수 있다고 치고 다시 찾아 — 그 길에서 지금 안 열리는 첫 문과 그 까닭.
    /// <para>그 문이 이제는 열릴 수 있고 그것을 알 수 있으면 (로봇은 주컴퓨터를 거쳐 문의 상태를 안다 · 사람은 그 문이 보이면) 낡은 믿음을 버리고 None — 다시 길을 찾는다.
    /// 알 수 없으면 '지난번에 안 열린 문밖에 길이 없다' · 그런 길도 없고 가려는 곳이 선체 바깥이면 '바깥으로 나갈 문이 없다' · 아니면 '갈 길이 없다'.</para></summary>
    private (SnagKind kind, int cell, int door) WhyNoPath(Agent a, int from, int to, int near)
    {
        if (!FindPath(a, from, to, near, throughStuck: true))
        {
            a.Path.Clear(); a.PathIdx = 0;
            return (L.Kind[to] == CellKind.Outside && L.IsSpace(from) ? SnagKind.NoWayOut : SnagKind.NoPath, to, -1);
        }
        int room = RoomOf(from), stale = -1, staleCell = -1;
        bool stuckSeen = false;
        foreach (int n in a.Path)
        {
            if (L.Kind[n] != CellKind.Door) continue;
            int d = L.DoorAt[n];
            if (d < 0 || F.DoorPassable(d)) continue;
            var k = WhyDoor(d);
            if (k != SnagKind.None) { a.Path.Clear(); a.PathIdx = 0; return (k, n, d); }
            if (Believes(a, BeliefKind.StuckDoor, n, 0) <= 0.3f) continue;
            if (a.IsPerson && (room < 0 || !Visible(a, n, room, 8))) { if (stale < 0) { stale = d; staleCell = n; } continue; }
            Forget(a, b => b.Kind == BeliefKind.StuckDoor && b.Subject == d); // 이제는 열린다 — 낡은 믿음
            stuckSeen = true;
        }
        a.Path.Clear(); a.PathIdx = 0;
        if (stuckSeen) return (SnagKind.None, -1, -1);
        return stale >= 0 ? (SnagKind.DoorStuckBefore, staleCell, stale) : (SnagKind.NoPath, to, -1);
    }
}
