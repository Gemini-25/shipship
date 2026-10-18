namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 숨 쉴 공기 (긴급): 사람이 있는 방의 산소가 옅고(16kPa 아래) 그 방은 새지 않으면 (그 방에 샘 · 진공 사건이 없고 · 지난 읽기보다 기압이 줄지 않았다)
/// 예비 공기를 그 방 통풍구로 바로 넣는다 — 배 어딘가 샌다고 예비를 잠가 두었어도 (ISS 감압 절차: 새는 곳을 떼어 두고 안전한 곳부터 다시 채운다).
/// 문이 모두 닫힌 방만 (열린 문으로는 넣는 만큼 빠져나간다) · 넣는 빠르기: 칸마다 0.06몰/초 (서른 칸 방이면 12분에 산소 분압이 두 배 가까이).
/// 방이 산소 19kPa · 95kPa에 닿거나 새기 시작하면 그친다 · 5분 넣어도 기압이 2kPa도 안 오르면 어딘가 새는 것 — 30분 쉰다.
/// </summary>
public sealed partial class ShipMind
{
    public long EmergencyAir;
    public const float EmergencyO2 = 16f;
    private long[] _pumpSince = Array.Empty<long>(), _pumpHold = Array.Empty<long>();
    private float[] _pumpKPa0 = Array.Empty<float>();

    private void EmergencyRefill(Sim sim)
    {
        if (_net == null || _mind == null || _life == null) return;
        if (_net.RoomAir.Length != L.Rooms.Count) _net.RoomAir = new double[L.Rooms.Count];
        if (_pumpSince.Length != L.Rooms.Count) { _pumpSince = new long[L.Rooms.Count]; _pumpHold = new long[L.Rooms.Count]; _pumpKPa0 = new float[L.Rooms.Count]; }
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            double was = _net.RoomAir[r];
            _net.RoomAir[r] = 0;
            if (RoomSeen[r] == 0 || _net.AirReserve <= 0 || _roomVent[r] < 0) continue;
            bool people = false;
            foreach (var a in _mind.Agents)
                if (a.IsPerson && _life.Bodies[a.C.Body].Alive != 0 && _mind.RoomOf(a.C.Cell) == r) { people = true; break; }
            if (!people) continue;
            bool low = RoomO2[r] < (was > 0 ? 19 : EmergencyO2) && RoomKPa[r] < 95;
            if (!low || _tick < _pumpHold[r]) continue;
            bool leakHere = Incidents.Any(x => x.Active != 0 && x.Room == r && x.Kind is IncidentKind.Leak or IncidentKind.Vacuum);
            bool closed = true;
            foreach (int d in _roomDoors[r]) if (F.DoorOpen[d] > 0 || F.DoorBleed[d] != 0) { closed = false; break; }
            if (leakHere || !closed || RoomKPa[r] < RoomKPaPrev[r] - 0.2f) continue;
            if (was <= 0) { _pumpSince[r] = _tick; _pumpKPa0[r] = RoomKPa[r]; }
            else if (_tick - _pumpSince[r] >= Time.Minutes(5))
            {
                if (RoomKPa[r] - _pumpKPa0[r] < 2) { _pumpHold[r] = _tick + Time.Minutes(30); Say(sim, $"{RoomName(r)}에 공기를 넣어도 기압이 오르지 않는다 — 어딘가 샌다 · 30분 쉰다"); continue; }
                _pumpSince[r] = _tick; _pumpKPa0[r] = RoomKPa[r];
            }
            _net.RoomAir[r] = L.Rooms[r].Cells.Length * 0.06;
            if (was <= 0)
            {
                EmergencyAir++;
                Say(sim, $"{RoomName(r)}에 사람이 있다 · 산소 {RoomO2[r]:0.0}kPa — 예비 공기를 그 방에 바로 넣는다");
            }
        }
    }

    private void SaveAir(StateWriter w) { w.I64(EmergencyAir); w.Arr(_pumpSince); w.Arr(_pumpHold); w.Arr(_pumpKPa0); }
    private void LoadAir(StateReader r) { EmergencyAir = r.I64(); _pumpSince = r.Arr<long>(); _pumpHold = r.Arr<long>(); _pumpKPa0 = r.Arr<float>(); }
}
