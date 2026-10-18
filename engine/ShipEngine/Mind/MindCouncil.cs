namespace ShipEngine;

/// <summary>
/// ⑨ → ⑧ 회의 · 판결이 마음에 닿는 곳 (깊이 바퀴 3). 사회(⑨)가 정하고 마음은 지킨다:
/// <para><b>모이기</b>: 회의에 갈 마음이 있는 이(⑨가 고른다)는 식당으로 간다 — 그 방 밖의 일상은 0.3배로 미룬다 · 위기는 그대로 앞선다 · 자는 이는 깨우지 않는다.</para>
/// <para><b>벌 당번</b> (사흘): 쉬기 · 어울리기 · 운동 0.3배 · 일 · 만들기 · 정비 · 청소 · 거두기 · 나르기 1.4배.
/// <b>근신</b> (하루): 제 침실 밖 공용 방의 일상 0.3배 (먹고 마시기는 그대로).
/// <b>배급 거부</b> (사흘 — 반란에 찬성한 이): 배식기 몫을 넘어 먹고 · 보관함을 드러내 놓고 연다 (몰래가 아니다 — 본 이를 꺼리지 않는다).</para>
/// </summary>
public sealed partial class Mind
{
    public int GatherRoom = -1, GatherCell = -1, GatherCause;
    public long GatherUntil;
    private byte[] _gatherWho = Array.Empty<byte>();
    private long[] _penance = Array.Empty<long>(), _confine = Array.Empty<long>(), _defy = Array.Empty<long>();
    public long Penances, Confinements, Defiances;

    private void CouncilArrays()
    {
        int n = Agents.Count;
        if (_penance.Length >= n) return;
        Array.Resize(ref _penance, n); Array.Resize(ref _confine, n); Array.Resize(ref _defy, n); Array.Resize(ref _gatherWho, n);
    }

    /// <summary>⑨가 요청: 그 방으로 모인다 (who[a] = 1인 이만 · room −1이면 푼다).</summary>
    public void Gather(int room, int cell, long until, int cause, byte[]? who)
    {
        CouncilArrays();
        GatherRoom = room; GatherCell = cell; GatherUntil = until; GatherCause = cause;
        Array.Clear(_gatherWho);
        if (room >= 0 && who != null) Array.Copy(who, _gatherWho, Math.Min(who.Length, _gatherWho.Length));
    }
    /// <summary>⑨가 요청: 벌 당번 · 근신 · 배급 거부 (그때까지).</summary>
    public void Penance(int agent, long until) { CouncilArrays(); if (agent >= 0 && agent < _penance.Length) { _penance[agent] = until; Penances++; } }
    public void Confine(int agent, long until) { CouncilArrays(); if (agent >= 0 && agent < _confine.Length) { _confine[agent] = until; Confinements++; } }
    public void DefyRation(int agent, long until) { CouncilArrays(); if (agent >= 0 && agent < _defy.Length) { _defy[agent] = until; Defiances++; } }
    public bool Defying(int agent, long now) => agent >= 0 && agent < _defy.Length && _defy[agent] > now;
    public bool Penanced(int agent, long now) => agent >= 0 && agent < _penance.Length && _penance[agent] > now;
    public bool Confined(int agent, long now) => agent >= 0 && agent < _confine.Length && _confine[agent] > now;

    /// <summary>식당: 배식기 · 냉장고가 있는 방 (없으면 가장 넓은 방).</summary>
    public int MessRoom()
    {
        foreach (int fu in _food) { int r = RoomOf(L.Furniture[fu].Cells[0]); if (r >= 0) return r; }
        int best = -1, bn = 0;
        for (int r = 0; r < L.Rooms.Count; r++) if (L.Rooms[r].Cells.Length > bn) { bn = L.Rooms[r].Cells.Length; best = r; }
        return best;
    }

    private bool Gathering(Agent a) => a.IsPerson && GatherRoom >= 0 && GatherUntil > _nowTick && a.Id < _gatherWho.Length && _gatherWho[a.Id] != 0;

    private void MeetingOption(Agent a, int here)
    {
        if (!Gathering(a) || RoomOf(here) == GatherRoom || BelievedRoomBad(a, GatherRoom)) return;
        Add(GoalKind.Meeting, GatherRoom, GatherCell, 2.4f, GatherCause);
    }

    /// <summary>회의 · 판결이 고르기 점수에 닿는 곱.</summary>
    private float CouncilBoost(Agent a, GoalKind g, int cell)
    {
        if (!a.IsPerson || a.Id >= _penance.Length) return 1;
        float k = 1;
        if (Gathering(a) && Everyday(g) && cell >= 0 && RoomOf(cell) != GatherRoom) k *= 0.3f;
        if (_penance[a.Id] > _nowTick)
        {
            if (g is GoalKind.Relax or GoalKind.Socialize or GoalKind.Exercise) k *= 0.3f;
            else if (g is GoalKind.Work or GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock) k *= 1.4f;
        }
        if (_confine[a.Id] > _nowTick && Everyday(g) && g is not (GoalKind.Eat or GoalKind.Drink or GoalKind.Sleep) && cell >= 0 && RoomOf(cell) != RoomOf(a.C.Bed >= 0 ? a.C.Bed : a.C.Cell)) k *= 0.3f;
        return k;
    }

    /// <summary>그 방 안의 빈 바닥 칸 (같은 목표로 가는 이와 겹치지 않게 — 가운데에서 가까운 차례).</summary>
    private int SpotIn(Agent a, int room, int center, GoalKind goal)
    {
        if (room < 0) return center;
        int best = center; int bd = int.MaxValue;
        foreach (int c in L.Rooms[room].Cells)
        {
            if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0) continue;
            bool taken = false;
            foreach (var o in Agents) if (o != a && (o.C.Cell == c || o.C.Goal == goal && o.C.GoalCell == c)) { taken = true; break; }
            if (taken) continue;
            int d = Manhattan(c, center);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    private void SaveCouncil(StateWriter w)
    {
        w.I32(GatherRoom); w.I32(GatherCell); w.I32(GatherCause); w.I64(GatherUntil); w.Arr(_gatherWho);
        w.Arr(_penance); w.Arr(_confine); w.Arr(_defy); w.I64(Penances); w.I64(Confinements); w.I64(Defiances);
    }
    private void LoadCouncil(StateReader r)
    {
        GatherRoom = r.I32(); GatherCell = r.I32(); GatherCause = r.I32(); GatherUntil = r.I64(); _gatherWho = r.Arr<byte>();
        _penance = r.Arr<long>(); _confine = r.Arr<long>(); _defy = r.Arr<long>(); Penances = r.I64(); Confinements = r.I64(); Defiances = r.I64();
    }
}
