namespace ShipEngine;

/// <summary>
/// ⑨ → ⑧ 회의 · 판결이 마음에 닿는 곳 (깊이 바퀴 3). 사회(⑨)가 정하고 마음은 지킨다:
/// <para><b>모이기</b>: 부름을 듣고 갈 마음이 든 이(제 판단 — MindGatherings)는 식당으로 간다 — 그 방 밖의 일상은 0.3배로 미룬다 · 위기는 그대로 앞선다 · 자는 이는 깨우지 않는다.</para>
/// <para><b>벌 당번</b> (사흘): 쉬기 · 어울리기 · 운동 0.3배 · 일 · 만들기 · 정비 · 청소 · 거두기 · 나르기 1.4배.
/// <b>근신</b> (하루): 제 침실 밖 공용 방의 일상 0.3배 (먹고 마시기는 그대로).
/// <b>배급 거부</b> (사흘 — 반란에 찬성한 이): 배식기 몫을 넘어 먹고 · 보관함을 드러내 놓고 연다 (몰래가 아니다 — 본 이를 꺼리지 않는다).</para>
/// </summary>
public sealed partial class Mind
{
    private byte[] _gatherWho = Array.Empty<byte>();
    private long[] _penance = Array.Empty<long>(), _confine = Array.Empty<long>(), _defy = Array.Empty<long>();
    public long Penances, Confinements, Defiances;

    private void CouncilArrays()
    {
        int n = Agents.Count;
        if (_penance.Length >= n) return;
        Array.Resize(ref _penance, n); Array.Resize(ref _confine, n); Array.Resize(ref _defy, n); Array.Resize(ref _gatherWho, n);
    }

    /// <summary>⑨가 요청: 벌 당번 · 근신 · 배급 거부 (그때까지).</summary>
    public void Penance(int agent, long until) { CouncilArrays(); if (agent >= 0 && agent < _penance.Length) { _penance[agent] = until; Penances++; } }
    public void Confine(int agent, long until) { CouncilArrays(); if (agent >= 0 && agent < _confine.Length) { _confine[agent] = until; Confinements++; } }
    public void DefyRation(int agent, long until) { CouncilArrays(); if (agent >= 0 && agent < _defy.Length) { _defy[agent] = until; Defiances++; } }
    public bool Defying(int agent, long now) => agent >= 0 && agent < _defy.Length && _defy[agent] > now;
    public bool Penanced(int agent, long now) => agent >= 0 && agent < _penance.Length && _penance[agent] > now;
    public bool Confined(int agent, long now) => agent >= 0 && agent < _confine.Length && _confine[agent] > now;

    /// <summary>식당: 식당으로 지은 방 → 배식기가 있는 방 → 먹을 것이 있는 방 (없으면 가장 넓은 방).
    /// 먹을 것의 첫 설비로만 고르던 때는 디딤호에서 냉장고가 먼저인 주방이 뽑혀 회의 · 추모가 주방에서 열렸다 — 첫날 불이 난 주방을 위험하다고 믿는 이들이 오지 않았다.</summary>
    public int MessRoom()
    {
        for (int r = 0; r < L.Rooms.Count; r++) if (L.Rooms[r].Type == "Mess") return r;
        foreach (var fu in L.Furniture) if (fu.Type == "MealDispenser" && fu.Room >= 0) return fu.Room;
        foreach (int fu in _food) { int r = RoomOf(L.Furniture[fu].Cells[0]); if (r >= 0) return r; }
        int best = -1, bn = 0;
        for (int r = 0; r < L.Rooms.Count; r++) if (L.Rooms[r].Cells.Length > bn) { bn = L.Rooms[r].Cells.Length; best = r; }
        return best;
    }

    private bool Gathering(Agent a) => a.IsPerson && GatherRoom >= 0 && GatherUntil > _nowTick && a.Id < _gatherWho.Length && _gatherWho[a.Id] != 0;

    private void MeetingOption(Agent a, int here)
    {
        if (!Gathering(a) || BelievedRoomBad(a, GatherRoom)) return;
        Add(GoalKind.Meeting, GatherRoom, GatherCell, 2.4f, GatherCause);   // 방 안에서도 남는다 — 들어서는 순간 사라지면 문 앞에서 들락거린다 (검토 ③-2)
    }

    /// <summary>회의 · 판결이 고르기 점수에 닿는 곱.</summary>
    private float CouncilBoost(Agent a, GoalKind g, int cell)
    {
        if (!a.IsPerson || a.Id >= _penance.Length) return 1;
        float k = 1;
        if (Gathering(a) && Everyday(g) && cell >= 0 && RoomOf(cell) != GatherRoom) k *= 0.3f;
        if (Sheltering(a) && Everyday(g) && cell >= 0 && RoomOf(cell) != ShelterRoom) k *= 0.3f;   // 대피 중엔 대피실 밖 일상을 덜 고른다 (회의와 같다)
        if (_penance[a.Id] > _nowTick)
        {
            if (g is GoalKind.Relax or GoalKind.Socialize or GoalKind.Exercise) k *= 0.3f;
            else if (g is GoalKind.Work or GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock) k *= 1.4f;
        }
        if (_confine[a.Id] > _nowTick && Everyday(g) && g is not (GoalKind.Eat or GoalKind.Drink or GoalKind.Sleep) && cell >= 0 && RoomOf(cell) != RoomOf(a.C.Bed >= 0 ? a.C.Bed : a.C.Cell)) k *= 0.3f;
        return k;
    }


    private void SaveCouncil(StateWriter w)
    {
        w.I64(_meetSeen); w.Arr(_gatherWho);
        w.Arr(_penance); w.Arr(_confine); w.Arr(_defy); w.I64(Penances); w.I64(Confinements); w.I64(Defiances);
    }
    private void LoadCouncil(StateReader r)
    {
        _meetSeen = r.I64(); _gatherWho = r.Arr<byte>();
        _penance = r.Arr<long>(); _confine = r.Arr<long>(); _defy = r.Arr<long>(); Penances = r.I64(); Confinements = r.I64(); Defiances = r.I64();
    }
}
