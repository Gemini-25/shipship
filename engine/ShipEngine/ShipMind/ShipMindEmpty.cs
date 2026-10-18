namespace ShipEngine;

/// <summary>
/// ⑩ → ④ 비워진 방은 닫아 둔다 (기준 평가 2차 — 살핌호 시드 11). 구멍을 막은 뒤 비워진(진공) 방의 문을 누가 열려 하면 전동 문은
/// 압력 맞춤 밸브로 배 공기를 그 방에 흘려 넣는다 — 예비 공기가 남아 있으면 주컴퓨터가 곧 다시 채우니 괜찮지만,
/// 예비가 그 방을 채울 만큼(1.5배)도 없으면 배의 숨 쉴 공기를 나눠 버리는 것이다 (76kPa이던 배가 방 하나를 열자 68kPa로).
/// → 그런 방은 문 밸브를 잠가 둔다 (2분마다 다시 본다 · 사람이든 로봇이든 안에 있으면 내보내야 하니 잠그지 않는다 · 바깥문이 있는 에어락은 빼고).
/// 들어가려던 이는 문이 안 열리는 것을 알고 다른 길을 찾는다 (⑧) · 그 방의 '공기가 빠졌다'에는 로봇을 안으로 보내지 않고 에어락으로 나가 외판을 바깥에서 살피게 한다. 예비가 다시 넉넉해지면 푼다.
/// 실제: 1997년 미르의 스펙트르 모듈은 충돌로 뚫린 뒤 해치를 닫은 채 다시 채우지 않았다 (전선은 특별한 해치 덮개로 빼냈다).
/// <para>둘째 까닭 — 벽이 못 버틴다 (판단 엔진 · 살핌호 시드 11 6일: 파편 폭풍에 관측실 벽이 다섯 칸 길게 찢겼고 · 로봇이 일곱 시간 동안 봉합 꾸러미로 덮은 뒤
/// 다시 채우자 금이 벽을 따라 다시 달려 찢어졌다 — 그렇게 두 번 채우며 예비 공기 6만 몰을 잃었다): 하중 · 금 센서로 다시 채우면 벽이 버틸지 셈해(④ TearRatio)
/// 덧대 받쳐도 못 버티면 채우지 않고 닫아 둔다 (그 방 일에 로봇을 보내지 않는다 · 제대로 고칠 때까지 · 2분마다 다시 본다).</para>
/// </summary>
public sealed partial class ShipMind
{
    public long EmptyHolds;                              // 비워진 방을 닫아 둔 번
    private byte[] _emptyHeld = Array.Empty<byte>();

    private void KeepEmptyShut(Sim sim)
    {
        if (_net == null || _mind == null) return;
        var st = sim.Find<Structure>();
        if (st == null) return;
        if (_emptyHeld.Length != L.Rooms.Count) _emptyHeld = new byte[L.Rooms.Count];
        // 배 공기 (숨 쉬는 쪽 · 40kPa 넘는 방) — 칸당 · kPa당 몰
        double kpaCells = 0, moles = 0; int cells = 0;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int n = L.Rooms[r].Cells.Length;
            if (RoomSeen[r] == 0 || n == 0 || RoomKPa[r] <= 40) continue;
            kpaCells += RoomKPa[r] * n; cells += n; moles += RoomMoles[r * 6 + _reads % 6];
        }
        if (cells == 0 || kpaCells <= 0) return;
        double shipKPa = kpaCells / cells, perCellKPa = moles / kpaCells;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int n = L.Rooms[r].Cells.Length;
            bool hold = false, wreck = false;
            double need = 0;
            if (RoomSeen[r] != 0 && n > 0 && RoomKPa[r] < 30 && !_roomDoors[r].Any(d => L.Doors[d].External))
            {
                need = (shipKPa - RoomKPa[r]) * n * perCellKPa;                     // 그 방을 배 기압까지 채울 몰
                bool inside = _mind.Agents.Any(a => a.C.Up != 0 && a.C.Cell >= 0 && L.Room[a.C.Cell] == r);
                wreck = _emptyHeld[r] == 2 && (WallRisk(r, null) >= WreckAt || RoomHoleArea(r, out _) > 0);   // 벽이 못 버틴다 (채우기 걸음이 정한 것 — 막혀 있고 버틸 때까지)
                hold = wreck || !inside && _net.AirReserve < need * 1.5;
            }
            if (hold) foreach (int d in _roomDoors[r]) st.HoldBleed(d, sim.Tick + Time.Minutes(2));
            byte want = (byte)(wreck ? 2 : hold ? 1 : 0);
            if (want == _emptyHeld[r]) continue;
            byte was = _emptyHeld[r];
            _emptyHeld[r] = want;
            if (!hold) { Say(sim, was == 2 ? $"{RoomName(r)} 벽이 이제 버틴다 — 문 밸브를 푼다" : $"{RoomName(r)} 문 밸브를 푼다 — 예비 공기가 넉넉해졌다"); continue; }
            if (wreck) continue;
            EmptyHolds++;
            int cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 비워진 {Ko.Eul(RoomName(r))} 닫아 두었다 — 채울 예비 공기가 모자라다 ({need:0}몰이 드는데 {_net.AirReserve:0}몰)",
                sim.Tick, _roomVent[r], Incidents.LastOrDefault(x => x.Room == r && x.Kind == IncidentKind.Vacuum).Cause);
            sim.Facts.Emit(sim.Tick, FactKind.Decision, _roomVent[r], (float)need, cause, -1, r);
            Say(sim, $"{Ko.Eun(RoomName(r))} 비워진 채 닫아 둡니다 — 다시 채울 예비 공기가 모자랍니다 · 문을 열면 배 공기가 그리로 나뉩니다");
        }
    }

    /// <summary>
    /// 그 방은 다시 채우면 벽이 찢어진다 (덧대 받쳐도) — 비워진 채 닫아 둔다 (채우기 걸음이 정한다 · 제대로 고칠 때까지).
    /// 생각 사슬 '생각: … 벽이 길게 찢겼다 — 받쳐도 기압을 못 버틴다 → 다시 채우지 않고 닫아 둔다'.
    /// </summary>
    private void HoldWreck(Sim sim, int r, double risk, int parent)
    {
        if (r < 0 || r >= L.Rooms.Count) return;
        if (_emptyHeld.Length != L.Rooms.Count) _emptyHeld = new byte[L.Rooms.Count];
        if (_emptyHeld[r] == 2) return;
        _emptyHeld[r] = 2;
        EmptyHolds++; Wrecks++;
        if (_st != null) foreach (int d in _roomDoors[r]) _st.HoldBleed(d, sim.Tick + Time.Minutes(2));
        int cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {Ko.Eul(RoomName(r))} 다시 채우지 않고 닫아 두었다 — 벽의 금이 길어 덧대 받쳐도 기압을 못 버틴다 (찢어짐에 {risk:P0})",
            sim.Tick, _roomVent[r], parent);
        sim.Facts.Emit(sim.Tick, FactKind.Decision, _roomVent[r], (float)risk, cause, -1, r);
        Say(sim, $"생각: {RoomName(r)} 벽에 금이 길게 났다 — 덧대 받쳐도 기압을 다시 넣으면 찢어진다 (찢어짐에 {risk:P0}) → 다시 채우지 않고 비워진 채 닫아 둡니다 (제대로 고칠 때까지)");
    }

    /// <summary>그 방은 벽이 못 버텨 닫아 두었다 (그 방 진공 일에 로봇을 보내지 않는다).</summary>
    private bool Wrecked(int r) => r >= 0 && r < _emptyHeld.Length && _emptyHeld[r] == 2;
    public long Wrecks;

    /// <summary>벽 칸의 곁 방 (바닥이 맞닿은 첫 방 — 모서리면 대각선).</summary>
    private int WallRoom(int c)
    {
        int x0 = L.X(c), y0 = L.Y(c);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (L.In(x0 + dx, y0 + dy) && L.Room[L.Index(x0 + dx, y0 + dy)] is int rr && rr >= 0) return rr;
        return -1;
    }

    /// <summary>그 방을 비워진 채 닫아 두었나.</summary>
    private bool EmptyHeld(int r) => r >= 0 && r < _emptyHeld.Length && _emptyHeld[r] != 0;

    private void SaveEmpty(StateWriter w) { w.I64(EmptyHolds); w.Arr(_emptyHeld); w.I64(Wrecks); }
    private void LoadEmpty(StateReader r) { EmptyHolds = r.I64(); _emptyHeld = r.Arr<byte>(); Wrecks = r.I64(); }
}
