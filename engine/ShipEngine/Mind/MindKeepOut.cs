namespace ShipEngine;

/// <summary>
/// ⑩ → ⑧ 출입 금지 — 주컴퓨터가 '그 방에서 나가고 들어가지 말라'고 방송한다 (판단 엔진의 계획: 임시 에어락으로 쓸 방 · 공기를 뺄 방).
/// 따를지는 사람마다 (따를 마음 = 0.45 + 주컴퓨터를 믿는 정도 × 0.4 + 성실 × 0.2 — 명령마다 한 번 정한다) · 따르는 이는 그 방을 나와 가장 가까운 안전한 방으로 가고 · 그 방으로는 길을 잡지 않는다.
/// 흘려듣는 이는 하던 일을 한다 (원인 마디 '…가 나가 달라는 방송을 흘려들었다') — 주컴퓨터는 사람이 남아 있는 방의 공기는 빼지 않는다.
/// '곧 공기를 뺀다'는 믿음으로 알리면 눈으로 공기가 멀쩡한 것을 보고 지워 버린다 — 그래서 믿음이 아니라 명령이다.
/// </summary>
public sealed partial class Mind
{
    private long[] _keepOutUntil = Array.Empty<long>(), _keepOutSince = Array.Empty<long>();
    private int[] _keepOutCause = Array.Empty<int>();
    private ulong _keepSeed;
    public long KeepOutsHeeded, KeepOutsIgnored;

    private void EnsureKeepOut()
    {
        if (_keepOutUntil.Length == L.Rooms.Count) return;
        _keepOutUntil = new long[L.Rooms.Count]; _keepOutSince = new long[L.Rooms.Count]; _keepOutCause = new int[L.Rooms.Count];
    }

    /// <summary>⑩이 요청: 그 방에서 나가고 들어가지 말라 (until까지 — 이어서 부르면 늘린다). 새 명령이면 그 방 사람마다 따를지 정한다.</summary>
    public void KeepOut(Sim sim, int room, long until, int cause)
    {
        if (room < 0 || room >= L.Rooms.Count) return;
        EnsureKeepOut();
        _keepSeed = sim.Seed;
        bool fresh = _keepOutUntil[room] <= sim.Tick;
        _keepOutUntil[room] = Math.Max(_keepOutUntil[room], until); _keepOutCause[room] = cause;
        if (!fresh) return;
        _keepOutSince[room] = sim.Tick;
        foreach (var a in Agents)
        {
            if (!a.IsPerson || _life == null || _life[a.C.Body].Alive == 0 || RoomOf(a.C.Cell) != room) continue;
            if (Heeds(a, room)) { KeepOutsHeeded++; continue; }
            KeepOutsIgnored++;
            int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 나가 달라는 방송을 흘려들었다", sim.Tick, a.C.Cell, cause, 0, a.Id);
            sim.Facts.Emit(sim.Tick, FactKind.Decision, a.C.Cell, -1, c, a.Id, (int)GoalKind.Evacuate);
        }
    }

    /// <summary>⑩이 요청: 출입 금지를 푼다.</summary>
    public void ClearKeepOut(int room) { if (room >= 0 && room < _keepOutUntil.Length) _keepOutUntil[room] = 0; }

    /// <summary>그 방은 출입 금지인가 (방송이 살아 있다 — 따르는 이에게만).</summary>
    public bool KeepingOut(int room) => room >= 0 && room < _keepOutUntil.Length && _keepOutUntil[room] > _nowTick;

    private bool Heeds(Agent a, int room)
    {
        float trust = _soc?.Trust(a.Id) ?? 0.5f;
        float will = 0.45f + 0.4f * trust + 0.2f * a.C.Diligent;
        return Rng.Unit(_keepSeed, Rng.Key("나가 달라"), a.Id, room * 7919L + _keepOutSince[room]) < Math.Clamp(will, 0.05f, 0.98f);
    }

    /// <summary>이 사람에게 그 방은 출입 금지다 (명령을 따르는 이).</summary>
    private bool KeptOut(Agent a, int room) => a.IsPerson && KeepingOut(room) && Heeds(a, room);

    /// <summary>출입 금지 방 안에 있으면 나간다 (따르는 이).</summary>
    private void KeepOutOption(Agent a, int here)
    {
        int room = RoomOf(here);
        if (room >= 0 && KeptOut(a, room)) Add(GoalKind.Evacuate, room, here, 6f, _keepOutCause[room]);
    }

    private void SaveKeepOut(StateWriter w) { w.Arr(_keepOutUntil); w.Arr(_keepOutSince); w.Arr(_keepOutCause); w.U64(_keepSeed); w.I64(KeepOutsHeeded); w.I64(KeepOutsIgnored); }
    private void LoadKeepOut(StateReader r) { _keepOutUntil = r.Arr<long>(); _keepOutSince = r.Arr<long>(); _keepOutCause = r.Arr<int>(); _keepSeed = r.U64(); KeepOutsHeeded = r.I64(); KeepOutsIgnored = r.I64(); }
}
