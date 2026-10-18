namespace ShipEngine;

/// <summary>
/// ⑩ → ⑧ 대피 (우주 날씨). 주컴퓨터가 파편 구름 · 양성자 폭풍을 앞두고 한 방으로 모이라고 방송한다 — 따를지는 사람마다 정한다
/// (따를 마음 = 0.3 + 주컴퓨터를 믿는 정도 × 0.5 + 성실 × 0.25 + 구멍의 교훈 × 0.3 (파편일 때) — 방송마다 한 번 정한다 · 믿음 0.5 · 성실 0.5면 0.68).
/// 따르는 이는 대피실 밖의 일상(일 · 쉬기 · 밥 짓기 …)을 미루고 그리로 가고 · 졸리면 그 방 바닥에서 잔다 · 자던 이는 경보에 깬다.
/// 흘려듣는 이는 하던 일을 한다 (원인 마디 — '대피 방송을 흘려들었다'). 본능(공기 · 불 · 열)은 그대로 앞선다 — 대피실이 위험해지면 나온다.
/// 실제: ISS는 파편이 다가오면 모듈 사이 해치를 닫고 귀환선에 들어가 기다린다 (2021년 11월 위성 요격 시험) · 태양 양성자 폭풍에는 가장 두꺼운 곳(승무원 칸 · 물 벽)으로.
/// </summary>
public sealed partial class Mind
{
    public int ShelterRoom = -1, ShelterCell = -1, ShelterCause;
    public long ShelterUntil, ShelterSince;
    public byte ShelterKind;               // 1 파편 · 2 방사선
    public long SheltersHeeded, SheltersIgnored;
    private byte[] _heed = Array.Empty<byte>();

    /// <summary>⑩이 요청: 그 방으로 모이라는 방송 (room −1이면 푼다). 새 방송이면 사람마다 따를지 정하고 자던 이를 깨운다.</summary>
    public void Shelter(Sim sim, int room, int cell, long until, int cause, byte kind)
    {
        if (room < 0) { ShelterRoom = -1; ShelterCell = -1; ShelterUntil = 0; return; }
        bool fresh = ShelterRoom < 0 || ShelterUntil <= sim.Tick || ShelterKind != kind;
        ShelterRoom = room; ShelterCell = cell; ShelterUntil = until; ShelterCause = cause; ShelterKind = kind;
        if (!fresh) return;
        ShelterSince = sim.Tick;
        if (_heed.Length < Agents.Count) Array.Resize(ref _heed, Agents.Count);
        foreach (var a in Agents)
        {
            if (!a.IsPerson || _life == null) continue;
            ref var body = ref _life[a.C.Body];
            if (body.Alive == 0) continue;
            float trust = _soc?.Trust(a.Id) ?? 0.5f;
            float will = 0.3f + 0.5f * trust + 0.25f * a.C.Diligent + (kind == 1 ? 0.3f * a.C.LessonBreach : 0);   // 훈련된 절차 — 대개 따른다
            bool yes = Rng.Unit(sim.Seed, Rng.Key("대피"), a.Id, sim.Tick) < Math.Clamp(will, 0.05f, 0.97f);
            _heed[a.Id] = (byte)(yes ? 1 : 0);
            if (yes) SheltersHeeded++;
            else
            {
                SheltersIgnored++;
                int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 대피 방송을 흘려들었다", sim.Tick, a.C.Cell, cause, 0, a.Id);
                sim.Facts.Emit(sim.Tick, FactKind.Decision, a.C.Cell, -1, c, a.Id, (int)GoalKind.Shelter);
            }
            if (body.Asleep != 0 && body.Conscious != 0) Wake(sim, a, "대피 경보에 깼다");
        }
    }

    /// <summary>지금 대피 방송을 따르고 있다.</summary>
    private bool Sheltering(Agent a) => a.IsPerson && ShelterRoom >= 0 && ShelterUntil > _nowTick && a.Id < _heed.Length && _heed[a.Id] != 0;

    /// <summary>대피실로 가는 갈래: 밖에 있으면 (그 방이 위험하다고 믿지 않으면).</summary>
    private void ShelterOption(Agent a, int here)
    {
        if (!Sheltering(a) || RoomOf(here) == ShelterRoom || BelievedRoomBad(a, ShelterRoom)) return;
        Add(GoalKind.Shelter, ShelterRoom, ShelterCell, 4f, ShelterCause);
    }

    /// <summary>대피실 안의 빈 바닥 칸 (모인 사람끼리 겹치지 않게 — 가운데에서 가까운 차례).</summary>
    private int ShelterSpot(Agent a, int center)
    {
        if (ShelterRoom < 0) return center;
        int best = center; int bd = int.MaxValue;
        foreach (int c in L.Rooms[ShelterRoom].Cells)
        {
            if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0) continue;
            bool taken = false;
            foreach (var o in Agents) if (o != a && (o.C.Cell == c || o.C.Goal == GoalKind.Shelter && o.C.GoalCell == c)) { taken = true; break; }
            if (taken) continue;
            int d = Manhattan(c, center);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    private void SaveShelter(StateWriter w)
    {
        w.I32(ShelterRoom); w.I32(ShelterCell); w.I32(ShelterCause); w.I64(ShelterUntil); w.I64(ShelterSince); w.Byte(ShelterKind);
        w.I64(SheltersHeeded); w.I64(SheltersIgnored); w.Arr(_heed);
    }
    private void LoadShelter(StateReader r)
    {
        ShelterRoom = r.I32(); ShelterCell = r.I32(); ShelterCause = r.I32(); ShelterUntil = r.I64(); ShelterSince = r.I64(); ShelterKind = r.Byte();
        SheltersHeeded = r.I64(); SheltersIgnored = r.I64(); _heed = r.Arr<byte>();
    }
}
