namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 인공 중력 (바퀴 3 (나)): 제 장치라 늘 안다. 망가지지 않았는데 꺼진 발생기는 다시 켜고 (고장은 장치 고장 사건으로 로봇이 고친다 · 전기는 차단기를 다시 올린다) ·
/// 배의 중력이 무너지면 '중력이 사라집니다 — 손잡이를 잡으세요' · 발생기가 다시 돌기 시작하면 '중력이 돌아옵니다 — 바닥 쪽을 잡으세요'를 사람이 있는 방마다 알린다
/// (들은 이는 바닥 쪽을 잡아 덜 넘어진다 — ⑧ 믿음 '중력이 돌아온다').
/// </summary>
public sealed partial class ShipMind
{
    /// <summary>0: 중력이 있다 · 1: 무너졌다고 알렸다 · 2: 돌아온다고 알렸다.</summary>
    private byte _gravPhase;
    public long GravityRestarts;

    private void GravityWatch(Sim sim)
    {
        if (_net == null || _mind == null || !_net.HasGravity || sim.Tick % 2 != 0) return;
        float g = _net.BodyG.Length > 0 ? _net.BodyG[0] : 1;
        bool rising = false;
        foreach (var d in _net.Devices)
        {
            if (d.Def.Role != "gravity" || _net.BodyGCause.Length == 0) continue;
            if (_net.On[d.Id] == 0 && !_net.IsBroken(d.Id, Part.Electric) && !_net.IsBroken(d.Id, Part.Mechanical))
            {
                _net.SetOn(d.Id, true); GravityRestarts++;
                Say(sim, $"{d.Name}가 꺼져 있다 — 다시 켰다");
            }
            if (_net.Working(d.Id) && _net.GravField.Length > d.Id && _net.GravField[d.Id] < 1) rising = true;
        }
        if (g < 0.5f && _gravPhase == 0)
        {
            _gravPhase = 1;
            Say(sim, "중력이 사라집니다 — 손잡이를 잡으세요");
            ShowersZeroG(sim, true);   // 샤워 칸 물을 잠그고 불 (5단 C26)
        }
        if (g >= 0.5f) ShowersZeroG(sim, false);   // 돌아오면 비트 2만 끈다
        if (_gravPhase == 1 && rising)
        {
            _gravPhase = 2;
            Say(sim, "중력이 돌아옵니다 — 바닥 쪽을 잡으세요");
            int cause = _net.BodyGCause[0];
            var rooms = new SortedSet<int>();
            foreach (var a in _mind.Agents) if (a.IsPerson && a.C.Cell >= 0 && L.Room[a.C.Cell] >= 0) rooms.Add(L.Room[a.C.Cell]);
            foreach (int r in rooms)
                if (r < _roomVent.Length && _roomVent[r] >= 0 && F.Pressure(_roomVent[r]) > 5)
                    _mind.Announce(sim, _roomVent[r], AlarmDb, new Belief { Kind = BeliefKind.GravityBack, Cell = _roomVent[r], Subject = -1, Conf = 1f, Seen = sim.Tick, Cause = cause });
        }
        if (g >= 0.99f) _gravPhase = 0;
    }

    /// <summary>
    /// 무중력 샤워 칸 (5단 C26): 무너지면 열린 샤워 칸 밸브를 모두 잠그고 (온수기도) 모든 샤워 칸에 사용 금지 불 비트 2 · 방송 '무중력 — 샤워 칸 물을 잠갔습니다 · 물수건을 쓰세요' ·
    /// 0.5g 넘게 돌아오면 비트 2만 끈다 (다른 까닭의 불은 그대로). 아는 것: 중력 센서 · 밸브 (제 조절).
    /// </summary>
    private void ShowersZeroG(Sim sim, bool on)
    {
        var mind = _mind!;
        bool any = false;
        foreach (var fu in L.Furniture)
        {
            if (fu.Type != "Shower" || fu.Cells.Length == 0) continue;
            if (!on) { if ((mind.WashSign.Length > fu.Id ? mind.WashSign[fu.Id] & 4 : 0) != 0) { mind.SignWash(fu.Id, 2, false); any = true; } continue; }
            int d = WashDevice(fu.Id);
            if (d >= 0 && _net!.ValveOpen(d)) { _net.SetValve(d, false); _net.SetOn(d, false); }
            mind.SignWash(fu.Id, 2, true); any = true;
        }
        if (on && any) Broadcast(sim, "무중력 — 샤워 칸 물을 잠갔습니다 · 물수건을 쓰세요");
        else if (!on && any) Say(sim, "중력이 돌아와 샤워 칸 불을 껐다 (무중력 몫만)");
    }

    private void SaveGravity(StateWriter w) { w.Tag("중력"); w.I32(_gravPhase); w.I64(GravityRestarts); }
    private void LoadGravity(StateReader r) { r.Tag("중력"); _gravPhase = (byte)r.I32(); GravityRestarts = r.I64(); }
}
