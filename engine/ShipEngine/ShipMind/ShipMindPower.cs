namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 정전과 연장선 (⑥ 바퀴 2 · docs/ENGINE.md ⑥ 임시 배선).
/// 생명 유지 설비(산소 발생기 · 이산화탄소 제거기 · 냉각 펌프 · 주컴퓨터)가 있는 방이 배에 전기가 있는데도 2분 넘게 어둡고 — 그 방 급전선을 다시 올릴 수 없으면
/// (길이 젖었다 · 뜨겁다 · 눌어붙어 손으로 끊었다) '생명 설비 정전' 사건 → 로봇(없으면 사람에게 부탁)이 전기가 남는 옆 방에서 연장선을 끌어온다.
/// 봉쇄할 방의 문틀에 연장선이 깔려 있으면 뽑는다 — 봉쇄가 먼저다.
/// </summary>
public sealed partial class ShipMind
{
    public long[] RoomDarkSince = Array.Empty<long>();
    public long JumpersCut;

    private bool LifeRoom(int r)
    {
        if (_net == null) return false;
        foreach (var d in _net.Devices) if (d.Room == r && d.Def.Role is "o2gen" or "scrubber" or "pump" or "computer") return true;
        return false;
    }

    private List<(int room, int cell, int cause)> PowerLostRooms(Sim sim)
    {
        var lost = new List<(int, int, int)>();
        if (_net == null) return lost;
        if (RoomDarkSince.Length != L.Rooms.Count) RoomDarkSince = new long[L.Rooms.Count];
        long t = sim.Tick;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int f = _net.RoomFeeder[r];
            bool dark = f >= 0 && _net.Supplied > 0 && !_net.RoomPowered(r) && LifeRoom(r);
            if (!dark) { RoomDarkSince[r] = 0; continue; }
            if (RoomDarkSince[r] == 0) RoomDarkSince[r] = t;
            if (t - RoomDarkSince[r] < Time.Minutes(2)) continue;
            bool resettable = Policy.ResetBreakers != 0 && _net.Breaker[f] == 0 && _net.BreakerStuck[f] == 0 && !_net.PathFaulty(f);
            if (resettable) continue;   // 차단기를 다시 올리는 쪽이 먼저
            int donor = _net.BestDonorRoom(r);
            if (donor < 0) continue;
            lost.Add((r, _net.Vent[r], _net.FaultCause(f)));
        }
        return lost;
    }

    /// <summary>봉쇄하는 방의 문틀에 깔린 연장선을 뽑는다.</summary>
    private void UnplugForSeal(Sim sim, int room, int cause)
    {
        if (_net == null || room < 0 || room >= _roomDoors.Length) return;
        foreach (int d in _roomDoors[room])
        {
            int k = _net.JumperAtDoor(L.Doors[d].Cell);
            if (k < 0) continue;
            int to = _net.Jumpers[k].To;
            _net.Unplug(sim, k, $"{Ko.Eul(RoomName(room))} 봉쇄하려고 뽑았다", cause);
            JumpersCut++;
            Say(sim, $"{RoomName(room)} 봉쇄 — 문틀의 연장선을 뽑았다 ({Ko.Eun(RoomName(to))} 다시 어둡다)");
        }
    }
}
