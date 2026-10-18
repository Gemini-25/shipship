namespace ShipEngine;

/// <summary>
/// ⑥ 센서 무선 — 선이 끊겨도 전기가 있는 방의 센서는 무선으로 닿는다 (docs/ENGINE.md ⑥ 센서망).
/// 방마다 센서 묶음에 무선 마디가 있다 — 옆 방 마디를 거쳐(그물) 선이 살아 있는 방까지 닿으면 그 방이 선으로 넘겨준다.
/// 전파는 문으로만 건넌다 (선체 벽 · 격벽은 금속이라 막는다): 열린 문 1 · 닫힌 보통 문 2 (금속 문이 막고 틈으로 샌다) · 닫힌 차압 문 · 에어락 문은 못 건넌다 (밀봉).
/// 예산 3 — 열린 문 셋 · 닫힌 문 하나와 열린 문 하나까지. 거치는 방도 전기가 있어야 한다 (마디가 꺼지면 그물이 끊긴다).
/// 무선은 느리다 — 읽기 세 번에 한 번만 새 값이 온다 (36초 · 무선 센서 그물은 전지와 대역을 아끼려 30초~1분마다 보낸다).
/// 실제: ISS는 유선 자료 버스(MIL-STD-1553)에 더해 무선 센서(구조 진동 · 새는 곳 · RFID 재고)를 쓰고 · 모듈 사이 해치가 열려 있어야 잘 닿는다.
/// </summary>
public sealed partial class Network
{
    public const int RadioBudget = 3, RadioOpenDoor = 1, RadioClosedDoor = 2;
    private int[][] _doorRooms = Array.Empty<int[]>();   // [문] 양쪽 방 (도면에서 한 번)
    private int[] _radioHops = Array.Empty<int>();
    private long _radioTick = -1;

    private void BuildRadio()
    {
        _doorRooms = new int[L.Doors.Count][];
        Span<int> nb = stackalloc int[4];
        foreach (var d in L.Doors)
        {
            var rooms = new List<int>(2);
            L.Neighbors(d.Cell, nb);
            foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && !rooms.Contains(L.Room[j])) rooms.Add(L.Room[j]);
            _doorRooms[d.Id] = rooms.ToArray();
        }
        _radioHops = new int[L.Rooms.Count];
    }

    /// <summary>그 방의 센서가 선이든 무선이든 주컴퓨터에 닿나: 0 못 닿는다 · 1 선 · 2 무선 (그 틱에 한 번 다시 잰다 — 찍지 않는 값).</summary>
    public byte SensorLink(long tick, int room)
    {
        if (room < 0 || room >= L.Rooms.Count || !RoomPowered(room)) return 0;
        if (RoomLinked(room)) return 1;
        if (_radioTick != tick) { _radioTick = tick; MeasureRadio(); }
        return (byte)(_radioHops[room] <= RadioBudget ? 2 : 0);
    }

    /// <summary>선이 살아 있고 전기가 있는 방에서부터 문을 건너 예산을 쓴다 (작은 정수 비용 — 예산만큼 되풀이하면 끝난다).</summary>
    private void MeasureRadio()
    {
        int n = L.Rooms.Count;
        if (_radioHops.Length != n) BuildRadio();
        for (int r = 0; r < n; r++) _radioHops[r] = RoomPowered(r) && RoomLinked(r) && L.Rooms[r].Cells.Length > 0 ? 0 : int.MaxValue;
        for (int pass = 0; pass <= RadioBudget; pass++)
        {
            bool changed = false;
            foreach (var d in L.Doors)
            {
                var rs = _doorRooms[d.Id];
                if (rs.Length != 2) continue;
                bool open = F.DoorOpen[d.Id] > 0;
                if (!open && d.Kind != "normal") continue;   // 닫힌 차압 문 · 에어락 문: 밀봉 — 전파가 못 건넌다
                int cost = open ? RadioOpenDoor : RadioClosedDoor;
                for (int k = 0; k < 2; k++)
                {
                    int a = rs[k], b = rs[1 - k];
                    if (_radioHops[a] == int.MaxValue || !RoomPowered(b)) continue;
                    int v = _radioHops[a] + cost;
                    if (v < _radioHops[b] && v <= RadioBudget) { _radioHops[b] = v; changed = true; }
                }
            }
            if (!changed) break;
        }
    }
}
