namespace ShipEngine;

/// <summary>
/// ⑩ 우주 날씨 대비 (깊이 바퀴 3 · 기준 평가가 찾은 결함 — 파편 폭풍 앞 47분 동안 주컴퓨터가 아무것도 하지 않아 파편 하나에 배 전체가 새고 셋이 숨졌다).
/// <para><b>파편 구름</b> (먼지 구름은 외판을 뚫지 못해 뺀다): 15분 안에 닿거나 이미 속에 있으면 — 배 안 문을 모두 닫아 칸을 나누고(뚫린 방만 빠지게)
/// 구름이 오는 쪽 외판에서 가장 먼 방(그 쪽 외판이 적고 · 바람 아래쪽으로 깊은 방)을 대피실로 고른다.</para>
/// <para><b>양성자 폭풍</b>: X선 조짐(폭발 수십 분 전)부터 폭풍이 그칠 때까지 — 방사선이 가장 덜 드는 방(칸마다 선량의 평균)으로. 문은 닫지 않는다 (몇 시간 — 공조가 돈다).</para>
/// <para>대피실이 새거나 진공이면 다시 고른다 · 위험이 3분 넘게 없으면 푼다. 따를지는 사람마다 (⑧ <see cref="Mind.Shelter"/>).
/// 실제: ISS는 파편 접근 경보(conjunction)에 모듈 사이 해치를 닫고 귀환선에서 기다린다 · 태양 양성자 폭풍에는 가장 두꺼운 곳으로 (NASA 비행 규칙).</para>
/// </summary>
public sealed partial class ShipMind
{
    public const double ShelterLeadMin = 15;
    public byte ShelterOn;                 // 1 파편 · 2 방사선
    public int ShelterRoomAt = -1;
    public long ShelterFrom, ShelterCalm, Shelters, ShelterDoorsShut;
    private int _shelterCause;
    private int _flareMeasured;   // 선량계가 폭풍을 잰 조짐 (그 뒤로는 선량이 정한다)

    private void Weather(Sim sim)
    {
        if (sim.Tick % 5 != 0 || _mind == null || _out == null) return;
        byte kind = 0; int cause = 0, cloud = -1; double eta = 0, ux = 0, uy = 0; string what = "";
        double half = Math.Max(L.W, L.H) / 2.0;
        foreach (var c in _out.Clouds)
        {
            if (_sense == null || !_sense.Tracked(TrackKind.Cloud, c.Id)) continue;   // ⑥ 센서가 잡은 구름만 (잡힌 뒤로는 레이더가 거리 · 빠르기를 잰다)
            if (_out.DebrisMaxKg(c.Kind) < 0.01) continue;           // 먼지 구름 — 외판을 뚫지 못한다
            double dist = Math.Sqrt(c.X * c.X + c.Y * c.Y), sp = Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy);
            double gap = dist - c.Radius - half;
            bool coming = c.X * c.Vx + c.Y * c.Vy < 0;
            if (gap >= 0 && (!coming || gap / Math.Max(1, sp) > ShelterLeadMin * 60)) continue;
            kind = 1; cause = c.Cause; cloud = c.Id; eta = Math.Max(0, gap) / Math.Max(1, sp); ux = c.Vx / Math.Max(1, sp); uy = c.Vy / Math.Max(1, sp); what = _out.DebrisName(c.Kind);
            break;
        }
        // 양성자 폭풍: X선 감시가 조짐을 잡았거나(⑥) · 배 안 선량계가 바깥 방사선이 평소의 두 배를 넘은 것을 잰다 (조짐을 못 잡았으면 폭풍이 와서야)
        // 끝은 선량계로만 (docs/MIND.md 3단 덧붙임 — 끝나는 때를 미리 알지 않는다): 폭풍을 한 번 잰 뒤로는 조짐이 아니라 선량이 정한다 · 조짐만 잡고 폭풍이 오지 않으면 조짐 뒤 (앞섬 + 30분)에 거둔다
        bool storm = _out.DoseOut > 2 * _out.CalmDose + 0.05;
        long first = _sense != null && _out.FlareCause != 0 ? _sense.FirstOf(TrackKind.Flare, _out.FlareCause) : -1;
        if (storm && first >= 0) _flareMeasured = _out.FlareCause;
        double lead = _out.StarDef.WarnMin * 60;
        bool warned = first >= 0 && _flareMeasured != _out.FlareCause && (sim.Tick - first) * Time.SecondsPerTick < lead + 30 * 60;
        if (kind == 0 && (warned || storm))
        {
            kind = 2; cause = warned ? _sense!.CauseOf(TrackKind.Flare, _out.FlareCause) : _out.FlareCause;
            eta = storm ? 0 : Math.Max(0, lead - (sim.Tick - first) * Time.SecondsPerTick);   // 조짐에서 폭풍까지 흔히 걸리는 앞섬 (X선 → 양성자)
        }

        if (kind != 0)
        {
            ShelterCalm = sim.Tick;
            if (ShelterOn != kind || ShelterRoomAt < 0 || !ShelterRoomOk(ShelterRoomAt))
            {
                int room = kind == 1 ? DebrisShelter(ux, uy) : RadiationShelter();
                if (room < 0) return;
                bool fresh = ShelterOn != kind;
                ShelterOn = kind; ShelterRoomAt = room;
                if (fresh) { ShelterFrom = sim.Tick; Shelters++; Guessed(new Guess { Tick = sim.Tick, Kind = GuessKind.Shelter, Room = room, Obj = kind == 1 ? cloud : _out.FlareCause, Incident = -1, A = (float)eta, Verdict = kind }); }   // 화면용 예측 고리: 닿기까지 초
                string rn = Ko.Euro(RoomName(room));
                string text = kind == 1
                    ? (fresh ? $"주컴퓨터가 대피를 알렸다 — {Ko.I(what)} {eta / 60:0}분 뒤 닿는다 · 배 안 문을 닫고 {rn}" : $"주컴퓨터가 대피실을 {rn} 옮겼다 — 앞 대피실이 위험하다")
                    : (fresh ? $"주컴퓨터가 대피를 알렸다 — 양성자 폭풍 · 방사선이 가장 덜 드는 {rn}" : $"주컴퓨터가 대피실을 {rn} 옮겼다");
                _shelterCause = sim.Causes.Add(CauseKind.Computer, text, sim.Tick, ShelterCenter(room), cause);
                Say(sim, kind == 1
                    ? (fresh ? $"{Ko.I(what)} {Math.Max(1, eta / 60):0}분 뒤 닿습니다 — 배 안 문을 모두 닫습니다 · 모두 {rn} 모여 주세요" : $"대피실을 {rn} 옮깁니다")
                    : (fresh ? $"양성자 폭풍이 옵니다 — 방사선이 가장 덜 드는 {rn} 모여 주세요" : $"대피실을 {rn} 옮깁니다"));
                sim.Facts.Emit(sim.Tick, FactKind.Shelter, ShelterCenter(room), kind, _shelterCause, -1, room);
            }
            _mind.Shelter(sim, ShelterRoomAt, ShelterCenter(ShelterRoomAt), sim.Tick + Time.Minutes(10), _shelterCause, kind);
            if (kind == 1) ShutInnerDoors();
        }
        else if (ShelterOn != 0 && sim.Tick - ShelterCalm > Time.Minutes(3))
        {
            Say(sim, ShelterOn == 1 ? "파편 구름을 벗어났습니다 — 대피를 풉니다" : "양성자 폭풍이 잦아들었습니다 — 대피를 풉니다");
            sim.Facts.Emit(sim.Tick, FactKind.Shelter, ShelterRoomAt >= 0 ? ShelterCenter(ShelterRoomAt) : -1, -1, _shelterCause, -1, ShelterRoomAt);
            ShelterOn = 0; ShelterRoomAt = -1;
            _mind.Shelter(sim, -1, -1, 0, 0, 0);
        }
    }

    /// <summary>대피실로 쓸 수 있나: 보이고 · 기압이 있고 · 새거나 빠지는 중이 아니다 · 에어락이 아니다.</summary>
    private bool ShelterRoomOk(int r)
    {
        if (r < 0 || r >= L.Rooms.Count || RoomSeen[r] == 0 || RoomKPa[r] < 80 || L.Rooms[r].Cells.Length < 4) return false;
        if (_st != null) foreach (int d in _st.RoomDoors(r)) if (L.Doors[d].External) return false;
        foreach (var x in Incidents) if (x.Active != 0 && x.Room == r && x.Kind is IncidentKind.Leak or IncidentKind.Vacuum or IncidentKind.Fire) return false;
        return true;
    }

    /// <summary>파편이 오는 쪽(−u) 외판이 가장 적고 · 바람 아래쪽(+u)으로 가장 깊은 방. 사람 수보다 바닥이 좁으면 뒤로.</summary>
    private int DebrisShelter(double ux, double uy)
    {
        int people = _mind!.Agents.Count(a => a.IsPerson && _life != null && _life.Bodies[a.C.Body].Alive != 0);
        double cx = L.W / 2.0, cy = L.H / 2.0;
        int best = -1; double bs = double.MaxValue;
        Span<int> nb = stackalloc int[4];
        var walls = new HashSet<int>();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (!ShelterRoomOk(r)) continue;
            walls.Clear();
            double proj = 0; int floor = 0;
            var cells = L.Rooms[r].Cells;
            foreach (int c in cells)
            {
                proj += (L.X(c) + 0.5 - cx) * ux + (L.Y(c) + 0.5 - cy) * uy;
                if (L.Kind[c] == CellKind.Floor && L.FurnitureAt[c] < 0) floor++;
                L.Neighbors(c, nb);
                foreach (int w in nb)
                {
                    if (w < 0 || L.Kind[w] != CellKind.Wall || walls.Contains(w)) continue;
                    // 그 벽 너머가 우주이고 · 그 쪽이 파편이 오는 쪽이면 (바깥 방향 · u < 0)
                    int dx = L.X(w) - L.X(c), dy = L.Y(w) - L.Y(c);
                    int ox = L.X(w) + dx, oy = L.Y(w) + dy;
                    bool space = !L.In(ox, oy) || L.Kind[L.Index(ox, oy)] == CellKind.Outside;
                    if (space && dx * ux + dy * uy < -0.3) walls.Add(w);
                }
            }
            double score = walls.Count * 4 - proj / Math.Max(1, cells.Length) + (floor < people ? 50 : 0);
            if (score < bs) { bs = score; best = r; }
        }
        return best;
    }

    /// <summary>방사선이 가장 덜 드는 방 (칸마다 선량의 평균 — 벽 · 설비 · 물이 두꺼운 곳).</summary>
    private int RadiationShelter()
    {
        int best = -1; double bs = double.MaxValue;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (!ShelterRoomOk(r) || _out!.Dose == null) continue;
            double sum = 0; var cells = L.Rooms[r].Cells;
            foreach (int c in cells) sum += _out.Dose[c];
            double mean = sum / cells.Length;
            if (mean < bs) { bs = mean; best = r; }
        }
        return best;
    }

    /// <summary>대피실 가운데에서 가장 가까운 빈 바닥 칸.</summary>
    private int ShelterCenter(int r)
    {
        var cells = L.Rooms[r].Cells;
        double mx = 0, my = 0;
        foreach (int c in cells) { mx += L.X(c); my += L.Y(c); }
        mx /= cells.Length; my /= cells.Length;
        int best = cells[0]; double bd = double.MaxValue;
        foreach (int c in cells)
        {
            if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0) continue;
            double d = Math.Abs(L.X(c) - mx) + Math.Abs(L.Y(c) - my);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    /// <summary>배 안 문을 닫는다 (바깥문 · 끼인 문 · 쓰는 중인 문은 두고 — 지나는 이는 열고 지나 닫는다).</summary>
    private void ShutInnerDoors()
    {
        if (_st == null || _mind == null) return;
        foreach (var d in L.Doors)
        {
            if (d.External || _st.DoorJam[d.Id] != 0 || F.DoorOpen[d.Id] <= 0 && _st.DoorTarget[d.Id] <= 0) continue;
            if (DoorInUse(d.Id)) continue;   // 쓰는 중인 문 (봉쇄와 같은 잣대 — 5단 C3: 곁에서 충전하는 로봇 · 가만히 선 이는 아니다)
            if (_st.DoorTarget[d.Id] > 0) ShelterDoorsShut++;
            _st.RequestDoor(d.Id, 0);
        }
    }

    private void SaveWeather(StateWriter w)
    {
        w.Byte(ShelterOn); w.I32(ShelterRoomAt); w.I64(ShelterFrom); w.I64(ShelterCalm); w.I64(Shelters); w.I64(ShelterDoorsShut); w.I32(_shelterCause); w.I32(_flareMeasured);
    }
    private void LoadWeather(StateReader r)
    {
        ShelterOn = r.Byte(); ShelterRoomAt = r.I32(); ShelterFrom = r.I64(); ShelterCalm = r.I64(); Shelters = r.I64(); ShelterDoorsShut = r.I64(); _shelterCause = r.I32(); _flareMeasured = r.I32();
    }
}
