using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>막힌 곳 하나 (깊이 고침 DF3) — 막힘을 사건이 아니라 자리에 붙여 기억한다: 문 · 연장선(받는 방 ← 끌어온 방) · 계획(방 · 걸음의 꼴).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Spot
{
    public byte Kind;          // 1 문 · 2 연장선 · 3 계획 · 4 방 (갈 길이 없다)
    public int Obj, Obj2;      // 문: 문 번호 · -1 / 연장선: 받는 방 · 끌어온 방 (짐이 넘쳤으면 -1 — 길과 상관없다) / 계획: 방 · 걸음의 꼴 지문 / 방: 방 번호 · 막힌 몸
    public SnagKind Snag;      // 문: 막힌 까닭
    public byte Why;           // 연장선이 탄 까닭: 1 짐이 넘쳤다 · 2 깔린 바닥이 젖었다 · 3 피복이 뜨거웠다
    public byte Times;         // 같은 까닭으로 막힌 번 (풀린 뒤에 다시 막히면 1부터)
    public float Amps;         // 연장선: 탈 때 흐른 전류
    public long Tick;          // 마지막으로 막힌 때
}

/// <summary>
/// 깊이 고침 DF3 — 막힌 곳 (docs/ENGINE_DEPTH_REVIEW.md §4-1 · docs/MIND.md 8단 당김). 막힘 · 실패의 기억을 사건이 아니라 문 · 방 · 연장선 · 계획에 붙인다 —
/// 사건 이름이 바뀌어도 (불 → 샌다 → 빠졌다 → 쓰러졌다 → 심정지) 이어진다.
/// <list type="bullet">
/// <item>문: 맡긴 이(로봇 · 부탁을 받은 사람)가 문에서 막혀 돌려준 까닭 — ④가 같은 까닭을 말하는 동안 (안쪽 문 한 시간 · 바깥문 10분까지) 그 문을 지나야만 닿는 곳에는
///   로봇도 사람도 보내지 않는다 (일지에 한 번). 같은 방에 새 사건이 서면 그 문을 지나야 하는 방법(안에서 막기 · 바깥에서 막기)은 해 보지 않고 곧장 다음 안으로.</item>
/// <item>연장선: 탄 연장선의 까닭 (⑥이 남긴다) — 짐이 넘쳐 녹았으면 다시 잇기 전에 그 방 짐을 연장선 정격 안으로 줄이고 (덜 급한 것부터 끈다 · 제 급전선이 살아나면 다시 켠다) ·
///   줄여도 또 녹으면 연장선을 그만둔다 · 깔린 바닥이 젖거나 뜨거워 탔으면 한 시간 동안 그 방에서는 끌어오지 않는다 (다른 방에서 · 없으면 기다린다).</item>
/// <item>계획: 지어낸 계획을 다 마쳤는데 같은 사건이 그대로면 그 계획의 꼴을 센다 — 두 번째면 그 계획의 구멍 막기 걸음은 통하지 않은 것으로 적는다 (같은 계획을 세 번 세우지 않는다) ·
///   미리 돌려 본 세계에서 안 된 걸음은 10분 막힌 것으로 (그 세계의 까닭은 이 세계의 문이 말해 주지 않는다).</item>
/// <item>방: 서로 다른 맡긴 이 둘이 '갈 길이 없다'로 막힌 방 (막힌 몸이 서 있던 자리에서 문 · 벽 그림으로도 그 방에 닿지 않을 때만 적는다 — 몸을 함께) — 한 시간 동안 그 방 일(샘 · 빠짐은 기본 절차서의 몫)에
///   막힌 그 몸들과 문 · 벽 그림으로 닿지 않는 몸은 보내지 않는다 (같은 까닭 두 번이면 다른 길 · 셋째를 보내 A → B → C → A가 되던 것) · 막힌 적 없이 닿는 몸은 보낸다 (갇힌 몸의 사정으로 그 방을 막지 않는다).</item>
/// </list>
/// </summary>
public sealed partial class ShipMind
{
    public List<Spot> Spots = new();
    public long SpotsNoted, CutSkips, LineSheds, PlansNoEffect;
    /// <summary>[장치] 1: 연장선 정격 안으로 짐을 줄이려고 껐다 (제 급전선이 살아나면 다시 켠다).</summary>
    public byte[] LineShed = Array.Empty<byte>();
    private long _burnSeq;                           // 읽은 연장선 탄 기록 (⑥의 차례 번호)
    private readonly List<int> _cutQ = new();
    private bool[] _cutSeen = Array.Empty<bool>(), _cutGoal = Array.Empty<bool>(), _cutDoor = Array.Empty<bool>();

    /// <summary>막힌 곳을 적는다 (같은 곳 · 같은 까닭이면 번을 센다 — 이미 풀린 곳이면 1부터 다시 · 같은 막힘을 두 번 읽으면 그대로).</summary>
    private void NoteSpot(byte kind, int obj, int obj2, SnagKind k, byte why, float amps, long t)
    {
        for (int i = 0; i < Spots.Count; i++)
        {
            var s = Spots[i];
            if (s.Kind != kind || s.Obj != obj || s.Obj2 != obj2 || s.Snag != k || s.Why != why) continue;
            if (s.Tick == t) return;
            s.Times = !Standing(s) ? (byte)1 : s.Times < 255 ? (byte)(s.Times + 1) : s.Times;   // 여섯 시간 전에 녹은 일 하나와 지금의 녹음은 '두 번'이 아니다
            s.Tick = t; s.Amps = amps;
            Spots[i] = s;
            return;
        }
        Spots.Add(new Spot { Kind = kind, Obj = obj, Obj2 = obj2, Snag = k, Why = why, Amps = amps, Times = 1, Tick = t });
        if (Spots.Count > 48) Spots.RemoveAt(0);
        SpotsNoted++;
    }

    /// <summary>막힌 곳이 아직 그대로인가: 문은 ④가 같은 까닭을 말하는 동안 (안쪽 문 한 시간 · 바깥문 10분 — 에어락은 비우면 열린다) · 탄 연장선은 짐 여섯 시간 · 길 한 시간 · 계획 30분.</summary>
    private bool Standing(in Spot s)
    {
        long age = _tick - s.Tick;
        return s.Kind switch
        {
            1 => _st != null && s.Obj >= 0 && s.Obj < L.Doors.Count && age < (L.Doors[s.Obj].External ? Time.Minutes(10) : Time.Hours(1)) && Mind.SnagOf(_st.WhyShut(s.Obj)) == s.Snag,
            2 => age < Time.Hours(s.Why == 1 ? 6 : 1),
            3 => age < Time.Minutes(30),
            4 => age < Time.Hours(1),
            _ => false,
        };
    }

    private Spot DoorSpot(int d)
    {
        Spot best = default;
        foreach (var s in Spots) if (s.Kind == 1 && s.Obj == d && Standing(s) && s.Tick >= best.Tick) best = s;
        return best;
    }

    /// <summary>
    /// from에서 to로 가는 길이 막힌 곳(아직 그대로인 문)에 끊겼나 — 끊은 문 (방 사이는 문으로 · 바깥은 바깥문으로, 몸이 외판을 걸을 수 있을 때) · -1: 닿는다
    /// (막힌 곳이 없어도 닿지 않으면 이 기억의 몫이 아니다 — -1).
    /// </summary>
    private int CutBy(int from, int to, bool eva)
    {
        if (Spots.Count == 0 || from < 0 || to < 0 || _st == null) return -1;
        if (_cutDoor.Length != L.Doors.Count) _cutDoor = new bool[L.Doors.Count];
        Array.Clear(_cutDoor);
        bool any = false;
        foreach (var s in Spots) if (s.Kind == 1 && Standing(s)) { _cutDoor[s.Obj] = true; any = true; }
        if (!any || Reaches(from, to, eva, true, out _)) return -1;
        if (!Reaches(from, to, eva, false, out _)) return -1;
        Reaches(from, to, eva, true, out int cut);
        return cut;
    }

    /// <summary>방 사이 걸음 (문 · 바깥문): from의 자리에서 to의 자리(toRoom ≥ 0이면 그 방)에 닿나 — 막힌 곳의 문을 건너뛸 때 처음 만난 그 문 (cut).</summary>
    private bool Reaches(int from, int to, bool eva, bool skipCut, out int cut, int toRoom = -1)
    {
        cut = -1;
        int R = L.Rooms.Count;
        if (_cutSeen.Length != R + 1) { _cutSeen = new bool[R + 1]; _cutGoal = new bool[R + 1]; }
        Array.Clear(_cutSeen); Array.Clear(_cutGoal);
        _cutQ.Clear();
        if (toRoom >= 0) _cutGoal[toRoom] = true;
        else foreach (int n in Places(to, eva)) _cutGoal[n] = true;
        foreach (int n in Places(from, eva)) if (!_cutSeen[n]) { _cutSeen[n] = true; _cutQ.Add(n); }
        for (int qi = 0; qi < _cutQ.Count; qi++)
        {
            int n = _cutQ[qi];
            if (_cutGoal[n]) return true;
            var doors = n == R ? ExternalDoors() : _roomDoors[n];
            foreach (int d in doors)
            {
                if (skipCut && _cutDoor[d]) { if (cut < 0) cut = d; continue; }
                foreach (int o in DoorRooms(d)) if (o != n && !_cutSeen[o]) { _cutSeen[o] = true; _cutQ.Add(o); }
                if (L.Doors[d].External && eva && n != R && !_cutSeen[R]) { _cutSeen[R] = true; _cutQ.Add(R); }
            }
        }
        return false;
    }

    private int[]? _extDoors;
    private int[] ExternalDoors() => _extDoors ??= L.Doors.Where(d => d.External).Select(d => d.Id).ToArray();

    /// <summary>그 칸의 자리: 방 · 문 칸이면 양쪽 방 · 선체 바깥이면 바깥 (L.Rooms.Count).</summary>
    private List<int> Places(int cell, bool eva)
    {
        var res = new List<int>(2);
        if (cell < 0 || cell >= L.N) return res;
        if (L.Room[cell] >= 0) res.Add(L.Room[cell]);
        else if (L.Kind[cell] == CellKind.Door) { int d = L.DoorAt[cell]; res.AddRange(DoorRooms(d)); if (L.Doors[d].External && eva) res.Add(L.Rooms.Count); }
        else if (L.Kind[cell] == CellKind.Outside && eva) res.Add(L.Rooms.Count);
        return res;
    }

    /// <summary>서로 다른 맡긴 이 둘이 '갈 길이 없다'로 막힌 방인가 (한 시간 — 같은 몸이 거듭 막힌 것은 하나로 센다).</summary>
    private bool NoWayTo(int room)
    {
        if (room < 0) return false;
        int first = int.MinValue;
        foreach (var s in Spots)
            if (s.Kind == 4 && s.Obj == room && Standing(s)) { if (first == int.MinValue) first = s.Obj2; else if (s.Obj2 != first) return true; }
        return false;
    }

    /// <summary>그 몸을 그 방 일에 보내지 않나 ('갈 길이 없다'로 막힌 방): 거기서 막힌 몸이거나 · 문 · 벽 그림으로도 닿지 않는다 (막힌 적 없이 닿는 몸은 보낸다).</summary>
    private bool NoWayFor(int room, Agent a)
    {
        foreach (var s in Spots) if (s.Kind == 4 && s.Obj == room && s.Obj2 == a.Id && Standing(s)) return true;
        return !LaidOutTo(a.C.Cell, room, Mind.Eva(a));
    }

    /// <summary>문 · 벽 그림으로 from의 자리에서 그 방에 닿나 (문이 막혔는지는 보지 않는다 — 갈 길이 없는 것이 몸의 사정인지 방의 사정인지 가른다).</summary>
    private bool LaidOutTo(int from, int room, bool eva) => from < 0 || room < 0 || Reaches(from, -1, eva, false, out _, room);

    /// <summary>'갈 길이 없다'로 막힌 방에 보낼 몸이 없다 — 일지에 한 번 (그 사건마다).</summary>
    private void TellNoWay(Sim sim, ref Incident x)
    {
        CutSkips++;
        if (x.Cut != 0) return;
        x.Cut = 1;
        Say(sim, $"막힌 곳: {RoomName(x.Room)} {IncidentName(x)} — 맡긴 이 둘이 같은 까닭으로 막혔다 ({Mind.SnagText(SnagKind.NoPath)}) · 그 둘은 더 보내지 않고 닿는 이를 기다린다", NoteKind.Stuck, -1, x.Id);
    }

    /// <summary>맡길 몸이 모두 막힌 곳에 끊겼다 — 일지에 한 번 (그 사건마다).</summary>
    private void TellCut(Sim sim, ref Incident x, int d)
    {
        CutSkips++;
        if (x.Cut != 0) return;
        x.Cut = 1;
        Say(sim, $"막힌 곳: {RoomName(x.Room)} {IncidentName(x)} — 가는 길의 {DoorName(d)}이 막혀 있다 ({Mind.SnagText(DoorSpot(d).Snag)}) · 그 문으로는 보내지 않는다", NoteKind.Stuck, -1, x.Id);
    }

    /// <summary>같은 방의 앞 사건이 막힌 문: 그 문을 지나야 하는 방법은 해 보지 않고 막힌 것으로 적는다 (곧장 다음 안) — 닿는 로봇이 하나라도 있으면 그대로.</summary>
    private void InheritCut(Sim sim, ref Incident x, byte method, int cell, bool drone)
    {
        if (_mind == null || cell < 0 || MethodBlocked(x.Id, method, out _)) return;
        int cut = -1;
        foreach (var a in _mind.Agents)
        {
            if (a.IsPerson || a.C.Up == 0 || a.C.Battery < 0.2f || !drone && a.C.Kind == AgentKind.Drone) continue;
            int d = CutBy(a.C.Cell, cell, Mind.Eva(a));
            if (d < 0) return;
            if (cut < 0) cut = d;
        }
        if (cut < 0) return;
        var s = DoorSpot(cut);
        NoteTried(x.Id, method, 255, -1, s.Snag, cut, _tick);
        CutSkips++;
        Say(sim, $"생각: {RoomName(x.Room)} {IncidentName(x)} — {MethodName(method)}는 {DoorName(cut)}이 막혀 있어 ({Mind.SnagText(s.Snag)}) 해 보지 않는다 → 다음 안", NoteKind.Block, -1, x.Id);
    }

    // ───────────────────────── 연장선 ─────────────────────────

    private static string BurnText(byte why, float amps) => why switch
    {
        1 => $"짐이 연장선 정격을 넘었다 ({amps:0}A > {Network.JumperRatingA:0}A)", 2 => "깔린 바닥이 젖었다", _ => "피복이 뜨거워 녹았다",
    };

    /// <summary>⑥이 남긴 탄 연장선 기록을 읽어 막힌 곳으로 적는다 (일지에 한 줄).</summary>
    private void ReadBurns(Sim sim)
    {
        if (_net == null) return;
        foreach (var b in _net.Burns)
        {
            if (b.Seq <= _burnSeq) continue;
            _burnSeq = b.Seq;
            NoteSpot(2, b.To, b.Why == 1 ? -1 : b.From, SnagKind.None, b.Why, b.Amps, b.Tick);
            Say(sim, $"막힌 곳: {RoomName(b.To)} 연장선이 탔다 — {BurnText(b.Why, b.Amps)} ({RoomName(b.From)}에서 끌어온 길){(b.Why == 1 ? " · 다시 이을 때는 짐을 줄인다" : $" · 한 시간 동안 {RoomName(b.From)}에서는 끌어오지 않는다")}", NoteKind.Stuck);
        }
    }

    /// <summary>연장선을 맡기기 전에 그 방의 막힌 곳을 본다 — 끌어올 방 (+1 · 0: 막힌 곳이 없다 — 맡은 이가 고른다) · 짐이 넘쳐 녹았으면 짐을 줄인다 · 그만둘 때 false.</summary>
    private bool RewireArg(Sim sim, ref Incident x, out int arg)
    {
        arg = 0;
        if (_net == null) return true;
        int room = x.Room;
        Spot over = default;
        var avoid = new List<int>();
        foreach (var s in Spots)
            if (s.Kind == 2 && s.Obj == room && Standing(s))
            {
                if (s.Why == 1) over = s;
                else if (s.Obj2 >= 0 && !avoid.Contains(s.Obj2)) avoid.Add(s.Obj2);
            }
        if (over.Kind == 0 && avoid.Count == 0) return true;
        if (over.Kind != 0 && _net.RoomNeedA(room) > Network.JumperRatingA)
        {
            if (over.Times >= 2 || !ShedForJumper(sim, x, room))
            {
                if (x.Cut == 0) { x.Cut = 1; Say(sim, $"막힌 곳: {RoomName(room)} {IncidentName(x)} — {(over.Times >= 2 ? "짐을 줄여도 연장선이 또 녹았다" : "꼭 켜 둘 설비만으로도 연장선 정격을 넘는다")} · 연장선을 그만둔다 (제 급전선을 고쳐야 한다)", NoteKind.Stuck, -1, x.Id); }
                return false;
            }
        }
        int donor = _net.BestDonorRoom(room, avoid);
        if (donor < 0)
        {
            if (x.Cut == 0) { x.Cut = 1; Say(sim, $"막힌 곳: {RoomName(room)} {IncidentName(x)} — 연장선을 끌어올 다른 방이 없다 ({string.Join(" · ", avoid.Select(RoomName))}에서 끈 길은 탔다) · 기다린다", NoteKind.Stuck, -1, x.Id); }
            return false;
        }
        arg = donor + 1;
        return true;
    }

    /// <summary>꼭 켜 둘 차례 (작을수록 먼저 켜 둔다): 주컴퓨터 · 산소 · 이산화탄소 · 냉각 펌프 · 공조 · 제습 · 물 재생 · 센서 · 조명 · 그다음 살림.</summary>
    private static int KeepRank(string role) => role switch
    {
        "computer" => 0, "o2gen" => 1, "scrubber" => 2, "pump" => 3, "hvac" => 4, "dehumid" => 5, "recycler" => 6, "sensor" => 7, "lamp" => 8, "panel" => 9,
        "fridge" => 10, "grow" => 11, "charger" => 12, "console" => 13, _ => 14,
    };

    /// <summary>짐 줄이기: 그 방 장치를 꼭 켜 둘 차례로 연장선 정격 안에 담고 나머지는 끈다 (제 급전선이 살아나면 다시 켠다) — 가장 먼저 켜 둘 것도 못 담으면 false.</summary>
    private bool ShedForJumper(Sim sim, in Incident x, int room)
    {
        if (_net == null) return false;
        if (LineShed.Length != _net.Devices.Count) LineShed = new byte[_net.Devices.Count];
        double budget = Network.JumperRatingA * Network.Volts / 1000, kept = 0, before = 0;
        var off = new List<string>();
        bool first = true;
        foreach (var d in _net.Devices.Where(d => d.Room == room && _net.On[d.Id] != 0 && Network.LoadOnJumper(d.Def.Role)).OrderBy(d => KeepRank(d.Def.Role)).ThenBy(d => d.Id))
        {
            before += d.Def.Kw;
            if (kept + d.Def.Kw <= budget) { kept += d.Def.Kw; first = false; continue; }
            if (first) return false;
            _net.SetOn(d.Id, false); LineShed[d.Id] = 1; LineSheds++;
            sim.Facts.Emit(sim.Tick, FactKind.LoadShed, d.Cell, (float)d.Def.Kw, 0, -1, d.Id);
            off.Add(d.Name);
        }
        if (off.Count > 0) Say(sim, $"{RoomName(room)} 짐을 줄인다 — 연장선 정격 {Network.JumperRatingA:0}A 안으로 ({string.Join(" · ", off)} 끔 · {before * 1000 / Network.Volts:0}A → {kept * 1000 / Network.Volts:0}A)", NoteKind.Power, -1, x.Id);
        return true;
    }

    /// <summary>제 급전선이 살아난 방: 짐을 줄이려고 껐던 장치를 다시 켠다.</summary>
    private void RestoreLineShed(Sim sim)
    {
        if (_net == null || LineShed.Length != _net.Devices.Count) return;
        foreach (var d in _net.Devices)
        {
            if (LineShed[d.Id] == 0 || !_net.OwnFeederLive(d.Room)) continue;
            LineShed[d.Id] = 0;
            _net.SetOn(d.Id, true);
            Say(sim, $"{RoomName(d.Room)} 제 급전선이 살아나 {Ko.Eul(d.Name)} 다시 켰다", NoteKind.Power);
        }
    }

    // ───────────────────────── 계획 ─────────────────────────

    /// <summary>계획의 꼴 지문 (걸음 · 대상 차례).</summary>
    private static int PlanShape(List<PlanStepRec> steps)
    {
        unchecked
        {
            int h = 23;
            foreach (var s in steps) h = h * 31 + s.Act * 7919 + s.Obj;
            return h == 0 ? 1 : h;
        }
    }

    /// <summary>계획을 다 마쳤는데 같은 사건이 그대로다 — 그 꼴을 세고 · 두 번째면 그 계획의 구멍 막기 걸음을 통하지 않은 것으로 적는다 (같은 계획을 세 번 세우지 않는다).</summary>
    private void NoEffect(Sim sim, in Incident x, List<PlanStepRec> steps)
    {
        if (steps.Count == 0) return;
        int shape = PlanShape(steps);
        NoteSpot(3, x.Room, shape, SnagKind.None, 0, 0, _tick);
        byte times = 0;
        foreach (var s in Spots) if (s.Kind == 3 && s.Obj == x.Room && s.Obj2 == shape && Standing(s)) times = s.Times;
        if (times < 2) return;
        PlansNoEffect++;
        var patched = new List<string>();
        foreach (var st in steps)
        {
            if (st.Act != (byte)ActKind.Patch) continue;
            NoteTried(x.Id, 0, st.Act, st.Obj, SnagKind.Other, -1, _tick);
            patched.Add(RoomName(st.Obj));
        }
        Say(sim, $"생각: {RoomName(x.Room)} {IncidentName(x)} — 같은 계획을 두 번 다 했는데 그대로다{(patched.Count > 0 ? $" ({string.Join(" · ", patched)} 구멍 막기는 통하지 않는다 — 막을 구멍이 보이지 않는다)" : "")} → 다른 안", NoteKind.Plan, -1, x.Id);
    }

    private void SaveSpots(StateWriter w) { w.List(Spots); w.I64(SpotsNoted); w.I64(CutSkips); w.I64(LineSheds); w.I64(PlansNoEffect); w.Arr(LineShed); w.I64(_burnSeq); }

    private void LoadSpots(StateReader r) { Spots = r.List<Spot>(); SpotsNoted = r.I64(); CutSkips = r.I64(); LineSheds = r.I64(); PlansNoEffect = r.I64(); LineShed = r.Arr<byte>(); _burnSeq = r.I64(); }
}
