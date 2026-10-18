namespace ShipEngine;

/// <summary>임시 배선 하나: 전기가 남는 방(From)에서 급전선이 죽은 방(To)으로 끌어온 연장선.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Jumper
{
    public int Id, From, To, Cause;
    public long Since, OwnOkSince;   // 깐 때 · 제 급전선이 살아난 때 (0: 아직)
    public float OverSec;            // 정격의 1.5배를 넘게 흐른 초
    public float Amps;
}

/// <summary>
/// ⑥ 임시 배선 — 연장선 (docs/ENGINE.md ⑥ 바퀴 2). 급전선이 젖거나 타서 다시 올릴 수 없는 방에 옆 방 급전선의 전기를 연장선으로 끌어온다.
/// 연장선은 바닥 · 문 칸을 따라 깔리고 (4mm² 구리 · 4.6mΩ/m · 정격 32A), 그 방의 짐이 옆 방 급전선에 얹힌다 (옆 방 차단기가 과부하로 떨어질 수 있다).
/// 정격의 1.5배를 1분 넘게 흐르면 피복이 녹아 · 깔린 칸이 젖거나(20몰) 뜨거우면(470K) 합선 — 옆 방 급전선이 끊긴다.
/// 깔린 문틀의 문은 닫히지 않는다. 제 급전선이 1분 넘게 살아 있으면 걷는다.
/// </summary>
public sealed partial class Network
{
    public const double JumperOhmPerM = 4.6e-3, JumperRatingA = 32;
    public List<Jumper> Jumpers = new();
    private readonly List<int[]> _jPath = new();   // 연장선이 지나는 칸 (From 통풍구 → To 통풍구 · 찍지 않는다 — 되살릴 때 다시 찾는다)
    public int JumperSeq;
    public long JumpersLaid, JumperShorts, JumpersPulled;
    private int[] _feed = Array.Empty<int>();       // 틱마다: 방 → 지금 전기를 주는 급전선
    private double[] _roomLoad = Array.Empty<double>();

    /// <summary>방의 제 급전선이 살아 있나 (차단기가 올라 있다).</summary>
    public bool OwnFeederLive(int room) => room >= 0 && room < RoomFeeder.Length && RoomFeeder[room] >= 0 && Breaker[RoomFeeder[room]] != 0;

    /// <summary>지금 그 방에 전기를 주는 급전선: 제 것이 살아 있으면 제 것 · 아니면 연장선 너머 (옆 방의 제 급전선이 살아 있을 때).</summary>
    public int RoomFeed(int room)
    {
        if (room < 0 || room >= RoomFeeder.Length) return -1;
        if (OwnFeederLive(room)) return RoomFeeder[room];
        foreach (var j in Jumpers) if (j.To == room && OwnFeederLive(j.From)) return RoomFeeder[j.From];
        return RoomFeeder[room];
    }

    private int FeedOf(Device dev) => dev.Room >= 0 && dev.Room < _feed.Length ? _feed[dev.Room] : dev.Feeder;

    /// <summary>연장선이 지날 칸: From 통풍구에서 To 통풍구까지 바닥 · 문 칸 (가장 짧은 길 · 없으면 null).</summary>
    public int[]? JumperPath(int from, int to)
    {
        if (from < 0 || to < 0 || from >= Vent.Length || to >= Vent.Length || Vent[from] < 0 || Vent[to] < 0) return null;
        int s = Vent[from], e = Vent[to];
        var prev = new Dictionary<int, int> { [s] = -1 };
        var q = new Queue<int>(); q.Enqueue(s);
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            if (c == e) break;
            L.Neighbors(c, nb);
            foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && !prev.ContainsKey(j)) { prev[j] = c; q.Enqueue(j); }
        }
        if (!prev.ContainsKey(e)) return null;
        var path = new List<int>();
        for (int c = e; c >= 0; c = prev[c]) path.Add(c);
        path.Reverse();
        return path.ToArray();
    }

    /// <summary>연장선을 깐다 (사람 · 로봇이 다 깔았을 때 — ⑧이 요청).</summary>
    public bool LayJumper(Sim sim, int from, int to, int cause)
    {
        var path = JumperPath(from, to);
        if (path == null || Jumpers.Exists(j => j.To == to)) return false;
        int c = sim.Causes.Add(CauseKind.Electric, $"{L.Rooms[from].Name}에서 {Ko.Euro(L.Rooms[to].Name)} 연장선을 끌어왔다 ({path.Length}m)", sim.Tick, Vent[to], cause);
        Jumpers.Add(new Jumper { Id = ++JumperSeq, From = from, To = to, Cause = c, Since = sim.Tick });
        _jPath.Add(path);
        JumpersLaid++;
        sim.Facts.Emit(sim.Tick, FactKind.Jumper, Vent[to], path.Length, c, other: 1);
        return true;
    }

    /// <summary>연장선을 뽑는다 (봉쇄 · 다 썼다 · 탔다).</summary>
    public void Unplug(Sim sim, int k, string why, int parent)
    {
        if (k < 0 || k >= Jumpers.Count) return;
        var j = Jumpers[k];
        int c = sim.Causes.Add(CauseKind.Electric, $"{L.Rooms[j.To].Name} 연장선 — {why}", sim.Tick, Vent[j.To], parent != 0 ? parent : j.Cause);
        sim.Facts.Emit(sim.Tick, FactKind.Jumper, Vent[j.To], 0, c, other: 0);
        Jumpers.RemoveAt(k); _jPath.RemoveAt(k);
        JumpersPulled++;
    }

    /// <summary>그 문 칸에 연장선이 깔렸나 (깔린 연장선 번호 · -1).</summary>
    public int JumperAtDoor(int doorCell)
    {
        for (int k = 0; k < _jPath.Count; k++) if (Array.IndexOf(_jPath[k], doorCell) >= 0) return k;
        return -1;
    }

    /// <summary>전력 계산 앞: 방마다 지금 전기를 주는 급전선.</summary>
    private void FeedRooms()
    {
        if (_feed.Length != L.Rooms.Count) { _feed = new int[L.Rooms.Count]; _roomLoad = new double[L.Rooms.Count]; }
        for (int r = 0; r < _feed.Length; r++) _feed[r] = Jumpers.Count == 0 ? RoomFeeder[r] : RoomFeed(r);
        Array.Clear(_roomLoad);
    }

    /// <summary>연장선마다: 흐르는 전류 · 전선 열 · 젖은 칸 · 뜨거운 칸 · 정격 넘김 → 합선 · 문틀을 막는다 · 제 급전선이 살아나면 걷는다.</summary>
    private void JumperStep(Sim sim, double dt)
    {
        if (Jumpers.Count == 0) return;
        var st = sim.Find<Structure>();
        for (int k = Jumpers.Count - 1; k >= 0; k--)
        {
            var j = Jumpers[k];
            var path = _jPath[k];
            // 제 급전선이 1분 넘게 살아 있으면 걷는다
            if (OwnFeederLive(j.To)) { if (j.OwnOkSince == 0) j.OwnOkSince = sim.Tick; }
            else j.OwnOkSince = 0;
            if (j.OwnOkSince != 0 && sim.Tick - j.OwnOkSince > Time.Minutes(1)) { Jumpers[k] = j; Unplug(sim, k, "제 급전선이 살아나 걷었다", 0); continue; }
            bool carrying = !OwnFeederLive(j.To) && OwnFeederLive(j.From);
            double amps = carrying ? _roomLoad[j.To] * FeederFrac(RoomFeeder[j.From]) * 1000 / Volts : 0;
            j.Amps = (float)amps;
            // 문틀에 깔린 연장선 — 그 문은 닫히지 않는다
            if (st != null) foreach (int c in path) if (L.Kind[c] == CellKind.Door) st.RequestDoor(L.DoorAt[c], 1);
            int shortAt = -1; string why = "";
            if (carrying)
            {
                double w = amps * amps * JumperOhmPerM;
                if (w > 0.5) foreach (int c in path) F.AddSurfaceHeat(c, w * dt);
                if (amps > JumperRatingA * 1.5) j.OverSec += (float)dt; else j.OverSec = 0;
                if (j.OverSec > 60) { shortAt = path[path.Length / 2]; why = $"정격을 넘게 흘러 ({amps:0}A) 피복이 녹았다"; }
                if (shortAt < 0)
                    foreach (int c in path)
                    {
                        if (F.Stored(T.sWater, c) > 20) { shortAt = c; why = "연장선이 깔린 바닥이 젖었다"; break; }
                        if (F.Tf[c] > 470) { shortAt = c; why = "연장선 피복이 뜨거워 녹았다"; break; }
                    }
            }
            Jumpers[k] = j;
            if (shortAt >= 0)
            {
                // 연장선 합선 — 옆 방 급전선이 그 자리에서 끊긴다 (불꽃은 전력 계산이 낸다)
                int donor = RoomFeeder[j.From];
                int pc = sim.Causes.Add(CauseKind.Electric, why, sim.Tick, shortAt, j.Cause, SpillNear(shortAt));
                int cause = sim.Causes.Add(CauseKind.Electric, "연장선 합선", sim.Tick, shortAt, pc);
                if (donor >= 0) { FaultCell[donor] = shortAt; _faultCause[donor] = cause; }
                Shorts++; JumperShorts++;
                sim.Facts.Emit(sim.Tick, FactKind.Short, shortAt, 0, cause, other: donor);
                Unplug(sim, k, "합선으로 타 버렸다", cause);
            }
        }
    }

    private void SaveJumpers(StateWriter w) { w.List(Jumpers); w.I32(JumperSeq); w.I64(JumpersLaid); w.I64(JumperShorts); w.I64(JumpersPulled); }

    private void LoadJumpers(StateReader r)
    {
        Jumpers = r.List<Jumper>(); JumperSeq = r.I32(); JumpersLaid = r.I64(); JumperShorts = r.I64(); JumpersPulled = r.I64();
        _jPath.Clear();
        foreach (var j in Jumpers) _jPath.Add(JumperPath(j.From, j.To) ?? Array.Empty<int>());
    }
}

/// <summary>
/// ⑥ 떼어 쓰기 (docs/ENGINE.md ⑥ 바퀴 2): 수리 꾸러미가 없으면 덜 중요한 장치에서 같은 갈래 부품을 떼어 고친다.
/// 떼어 낸 장치는 그 부품이 빠져 멈추고 ('떼어 냄' 표시 — 새 꾸러미가 생기기 전에는 다시 떼어 고치지 않는다) · 고친 장치에는 헌 부품이 들어간다 (닳은 만큼 그대로).
/// </summary>
public sealed partial class Network
{
    public byte[] Stripped = Array.Empty<byte>();   // [장치] 1: 부품을 떼어 갔다
    public long PartsSalvaged;

    /// <summary>떼어 쓸 수 있는 장치의 차례 (작을수록 먼저 · -1: 떼지 않는다 — 생명 유지 · 전기 · 주컴퓨터).</summary>
    public static int DonorRank(string role) => role switch
    {
        "appliance" => 0, "lamp" => 1, "console" => 2, "charger" => 3, "industry" => 4, "stove" => 5, "fridge" => 6, "grow" => 7, _ => -1,
    };

    /// <summary>장치 dev의 부품 part를 고칠 때 떼어 올 장치 (덜 중요한 것 · 같은 차례면 가까운 것 · -1 없음).</summary>
    public int Donor(int dev, Part part, int nearCell)
    {
        if (Stripped.Length != Devices.Count) Stripped = new byte[Devices.Count];
        int best = -1; long bs = long.MaxValue;
        foreach (var d in Devices)
        {
            if (d.Id == dev || !d.Has(part) || Broken[d.Id * 4 + (int)part] != 0) continue;
            int rank = d.Type == "MedBed" ? -1 : DonorRank(d.Def.Role);   // 의료 침대는 떼지 않는다
            if (rank < 0 || DonorRank(Devices[dev].Def.Role) is int mine && mine >= 0 && mine <= rank) continue;   // 덜 중요한 것에서만
            long score = rank * 100_000L + Math.Abs(L.X(d.Cell) - L.X(nearCell)) + Math.Abs(L.Y(d.Cell) - L.Y(nearCell));
            if (score < bs) { bs = score; best = d.Id; }
        }
        return best;
    }

    /// <summary>부품을 떼어 낸다: 그 장치는 그 부품이 빠져 멈춘다 · 떼어 낸 부품의 닳은 시간을 돌려준다.</summary>
    public float TakePart(int d, Part p)
    {
        if (Stripped.Length != Devices.Count) Stripped = new byte[Devices.Count];
        int k = d * 4 + (int)p;
        float worn = WearFraction(d, p);
        Broken[k] = 1; Stripped[d] = 1; PartsSalvaged++;
        return worn;
    }

    /// <summary>헌 부품을 끼웠다: 수명을 떼어 온 부품이 쓴 몫만큼 쓴 채로 (0~1 — 새 자리의 숨은 한계에 견줘).</summary>
    public void SetWear(int d, Part p, float worn)
    {
        int k = d * 4 + (int)p;
        if (_limit[k] != float.MaxValue) Wear[k] = _limit[k] * Math.Clamp(worn, 0, 0.99f);
    }

    public bool IsStripped(int d) => d >= 0 && d < Stripped.Length && Stripped[d] != 0;

    /// <summary>연장선을 끌어올 방: 제 급전선이 살아 있고 · 정격에 여유가 있고 · 길이 닿는 가장 가까운 방 (-1 없음).</summary>
    public int BestDonorRoom(int room)
    {
        if (room < 0 || room >= L.Rooms.Count) return -1;
        double needA = 0;
        foreach (var d in Devices) if (d.Room == room && On[d.Id] != 0 && d.Def.Role is not ("reactor" or "aux" or "battery")) needA += d.Def.Kw * 1000 / Volts;
        int best = -1; double bs = double.MaxValue;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (r == room || !OwnFeederLive(r) || Vent[r] < 0) continue;
            var path = JumperPath(r, room);
            if (path == null || path.Length > 60) continue;
            int f = RoomFeeder[r];
            double spare = Feeders[f].RatingA - Amps[f];
            double score = path.Length + (spare < needA ? 1000 : 0);   // 여유가 모자라면 마지막에 (그래도 없으면 그리로 — 과부하를 무릅쓴다)
            if (score < bs) { bs = score; best = r; }
        }
        return best;
    }
}
