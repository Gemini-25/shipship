using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v12.2 인과 사슬: 사고가 무엇에서 시작해 무엇으로 번졌고, 누가 어떻게 되돌렸나.
//
// 두 가지 길로 고리가 생긴다.
//  1) 직접 원인 — 운석·폭발·불처럼 사고를 일으키는 코드가 Because(고리)를 켜 두면, 그 안에서 생긴 고장·끊김·불이 그 고리의 자식이 된다.
//  2) 번진 상태 — 정전·단수·환기 끊김·숨막힘·원자로 정지·쓰러짐은 Update가 상태 변화를 보고 원인을 짚어 잇는다.
// 상태가 풀리면 그 일을 마지막으로 손본 사람과 걸린 시간을 적은 '복구' 고리가 붙는다.
// 시뮬레이션에는 아무것도 되먹이지 않는다 (보는 것만) — 결정론과 무관하다.

public enum CauseKind
{
    Impact,      // 운석·파편
    Explosion,   // 설비 폭발
    Fire,
    Breach,      // 외벽 파공·감압
    Suffocation, // 산소 부족
    Gas,         // 일산화탄소·유독 가스
    Cut,         // 망 토막 끊김 (전력 간선·급수관·덕트)
    Outage,      // 정전
    NoWater,     // 단수
    NoAir,       // 환기 끊김
    Fault,       // 설비 고장
    Stop,        // 핵심 설비 멈춤 (전기·물이 없어서)
    Scram,       // 원자로 긴급 정지
    Casualty,    // 쓰러짐
    Death,
    Detach,      // 방이 떨어져 나감
    Hazard,      // 그 밖의 사고 (관찰자·무작위 사고)
    Recovery,    // 되돌림
    Flood,       // v12.3 침수
    Shock,       // v12.3 감전
    NoData,      // v12.3 데이터선 끊김 (감지기·원격 제어)
    Illness,     // v12.4 전염병 (누가 누구에게 옮겼나)
    Mistake,     // v12.7 사람의 실수 (왜 — 졸려서·자격 없이·다친 팔)
}

public sealed class CauseNode
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public CauseKind Kind { get; init; }
    public string Key { get; init; } = "";
    public string Text { get; set; } = "";
    public int Parent { get; init; } = -1;
    public int Incident { get; init; }
    public int Depth { get; init; }
    public int RoomId { get; init; } = -1;
    public Vector2? At { get; init; }
    /// <summary>상태 (풀려야 끝난다) ↔ 한 번 일어난 사건.</summary>
    public bool Lasting { get; init; }
    public long ResolvedAt { get; set; } = -1;
    public string? ResolvedBy { get; set; }
    public int Repeats { get; set; }
    public List<int> Children { get; } = new();
    public bool Open => Lasting && ResolvedAt < 0;
    public float Hours(long now) => ((ResolvedAt >= 0 ? ResolvedAt : now) - Tick) / (float)SimTime.TicksPerHour;
}

/// <summary>사고 하나: 뿌리 고리에서 뻗은 나무.</summary>
public sealed class CauseIncident
{
    public int Root { get; init; }
    public long Start { get; init; }
    public long End { get; set; } = -1;
    public long LastActivity { get; set; }
    public bool ByObserver { get; init; }
    public List<int> Nodes { get; } = new();
    public int OpenCount { get; set; }
    public int Casualties { get; set; }
    public int Deaths { get; set; }
    public bool Open => End < 0;
    /// <summary>화면에 카드로 띄울 만한가: 번졌거나, 사람이 다쳤거나, 큰 사고로 시작했다.</summary>
    public int Weight(CauseLog log)
    {
        var root = log.Node(Root);
        int w = Nodes.Count - 1 + Casualties * 3 + Deaths * 6;
        if (root.Kind is CauseKind.Impact or CauseKind.Explosion or CauseKind.Fire or CauseKind.Breach or CauseKind.Scram or CauseKind.Detach) w += 2;
        if (ByObserver) w += 2;
        return w;
    }
}

public sealed class CauseLog
{
    private readonly World _w;
    public List<CauseNode> Nodes { get; } = new();
    public List<CauseIncident> Incidents { get; } = new();
    public int Version { get; private set; }

    private readonly Dictionary<int, CauseIncident> _incidentOf = new();   // 뿌리 id → 사고
    private readonly Dictionary<string, int> _open = new();              // 열린 상태 열쇠 → 고리
    private readonly Dictionary<Cell, int> _fireCells = new();            // 불 칸 → 그 방 화재 고리
    private readonly List<(Machine m, Fault f, int node)> _faults = new();
    private readonly Dictionary<int, (int node, long tick)> _roomHit = new(); // 방 → 마지막으로 때린 사고
    private readonly Dictionary<string, (int crew, long tick)> _worked = new(); // 손본 대상 → 마지막 손본 사람
    // 묶음 상태 (정전·단수·환기 끊김은 원인 하나에 방 여럿): 고리 → 아직 풀리지 않은 방들
    private readonly Dictionary<int, HashSet<int>> _groups = new();
    private readonly Dictionary<string, int> _roomState = new();         // "dark:방" → 묶음 고리
    private int _ctx = -1;
    private int _lastFlagship = -1;

    public CauseLog(World w) => _w = w;

    public CauseNode Node(int id) => Nodes[id];
    public CauseIncident? IncidentOf(int nodeId) => nodeId >= 0 && nodeId < Nodes.Count && _incidentOf.TryGetValue(Nodes[nodeId].Incident, out var i) ? i : null;
    public int Context => _ctx;

    /// <summary>다음 뿌리는 관찰자가 일으킨 사고다 (한 번 쓰면 꺼진다).</summary>
    public bool ObserverNext { get; set; }
    public bool ConsumeObserver() { bool o = ObserverNext; ObserverNext = false; return o; }

    /// <summary>관찰자가 설비를 고장 냈다: 잔고장이라도 사고로 적는다.</summary>
    public Scope Observed()
    {
        _forceFaults = true;
        ObserverNext = true;
        return new Scope(this, _ctx, observed: true);
    }
    private bool _forceFaults;
    public int OpenNode(string key) => _open.TryGetValue(key, out var id) ? id : -1;

    // ─────────────────────────────── 원인 문맥 ───────────────────────────────

    public readonly struct Scope : IDisposable
    {
        private readonly CauseLog _log;
        private readonly int _prev;
        private readonly bool _observed;
        public Scope(CauseLog log, int prev, bool observed = false) { _log = log; _prev = prev; _observed = observed; }
        public void Dispose()
        {
            if (_log == null) return;
            _log._ctx = _prev;
            if (_observed) { _log._forceFaults = false; _log.ObserverNext = false; }
        }
    }

    /// <summary>이 안에서 생긴 피해는 node의 자식이 된다.</summary>
    public Scope Because(int node)
    {
        var s = new Scope(this, _ctx);
        if (node >= 0) _ctx = node;
        return s;
    }

    // ─────────────────────────────── 고리 만들기 ───────────────────────────────

    /// <summary>새 사고의 뿌리.</summary>
    public int Root(CauseKind kind, string text, Room? room, Vector2? at, string? key = null, bool lasting = false, bool observer = false)
    {
        var n = Make(kind, key ?? "", text, room, at, -1, lasting);
        var inc = new CauseIncident { Root = n.Id, Start = _w.Tick, LastActivity = _w.Tick, ByObserver = observer };
        inc.Nodes.Add(n.Id);
        if (n.Open) inc.OpenCount++;
        Incidents.Add(inc);
        _incidentOf[n.Id] = inc;
        if (room != null && kind is CauseKind.Impact or CauseKind.Explosion or CauseKind.Hazard) _roomHit[room.Id] = (n.Id, _w.Tick);
        return n.Id;
    }

    /// <summary>
    /// 번진 것. parent를 안 주면 지금 문맥(Because)이 부모, 문맥도 없으면 새 사고의 뿌리가 된다.
    /// 같은 열쇠의 상태가 아직 열려 있으면 새로 만들지 않고 되풀이로 센다.
    /// </summary>
    public int Effect(CauseKind kind, string key, string text, Room? room, Vector2? at, int parent = -1, bool lasting = true)
    {
        if (key.Length > 0 && _open.TryGetValue(key, out var existing)) { Nodes[existing].Repeats++; return existing; }
        if (parent < 0) parent = _ctx;
        if (parent >= Nodes.Count) parent = -1; // 되감기 등으로 사라진 고리
        if (parent < 0) return Root(kind, text, room, at, key, lasting);
        var n = Make(kind, key, text, room, at, parent, lasting);
        return n.Id;
    }

    private CauseNode Make(CauseKind kind, string key, string text, Room? room, Vector2? at, int parent, bool lasting)
    {
        var p = parent >= 0 ? Nodes[parent] : null;
        var n = new CauseNode
        {
            Id = Nodes.Count, Tick = _w.Tick, Kind = kind, Key = key, Text = text, Parent = parent,
            Incident = p?.Incident ?? Nodes.Count, Depth = (p?.Depth ?? -1) + 1,
            RoomId = room?.Id ?? -1, At = at ?? room?.Center, Lasting = lasting,
        };
        Nodes.Add(n);
        if (p != null)
        {
            p.Children.Add(n.Id);
            if (_incidentOf.TryGetValue(n.Incident, out var inc))
            {
                inc.Nodes.Add(n.Id);
                inc.LastActivity = _w.Tick;
                if (n.Open) inc.OpenCount++;
                if (kind == CauseKind.Casualty) inc.Casualties++;
                if (kind == CauseKind.Death) inc.Deaths++;
                if (inc.End >= 0 && n.Open) inc.End = -1; // 끝난 줄 알았던 사고가 다시 번졌다
            }
        }
        if (lasting && key.Length > 0) _open[key] = n.Id;
        Version++;
        return n;
    }

    /// <summary>상태가 풀렸다: 누가 손봤는지 적고 '복구' 고리를 붙인다.</summary>
    public void Resolve(int id, string what, string? workKey = null, string? by = null)
    {
        var n = Nodes[id];
        if (!n.Open) return;
        n.ResolvedAt = _w.Tick;
        if (by == null) by = WorkedBy(workKey, SimTime.Hours(4));
        else if (by.Length == 0) by = null;
        n.ResolvedBy = by;
        if (n.Key.Length > 0 && _open.TryGetValue(n.Key, out var cur) && cur == id) _open.Remove(n.Key);
        float h = n.Hours(_w.Tick);
        string dur = h < 1f ? $"{MathF.Max(1f, h * 60f):0}분" : h < 48f ? $"{h:0.#}시간" : $"{h / 24f:0.#}일";
        var r = Make(CauseKind.Recovery, "", $"{what}" + (by != null ? $" — {by}" : "") + $" ({dur} 만에)", _w.Ship.Rooms.FirstOrDefault(x => x.Id == n.RoomId), n.At, id, false);
        if (_incidentOf.TryGetValue(n.Incident, out var inc))
        {
            inc.OpenCount = Math.Max(0, inc.OpenCount - 1);
            inc.LastActivity = _w.Tick;
        }
        _ = r;
    }

    /// <summary>걸려다 만 사고 (아무 일도 없었다): 목록에서 지운다.</summary>
    public void Discard(int root)
    {
        if (!_incidentOf.TryGetValue(root, out var inc) || inc.Nodes.Count > 1) return;
        Incidents.Remove(inc);
        _incidentOf.Remove(root);
        Version++;
    }

    private readonly List<(int node, Func<bool> done, string what, string? work)> _until = new();

    /// <summary>뿌리를 '상태'로 둔다: done이 참이 될 때까지 열려 있다 (배관 파손 → 고칠 때까지).</summary>
    public void Until(int node, Func<bool> done, string what, string? workKey = null)
    {
        _until.Add((node, done, what, workKey));
        if (_incidentOf.TryGetValue(Nodes[node].Incident, out var inc)) inc.OpenCount++;
    }

    /// <summary>설비가 터진·멈춘 까닭으로 삼을 만한 열린 고리: 그 설비의 고장 → 멈춤 → 방의 정전·환기 끊김·단수 → 방의 불 → 방을 때린 사고.</summary>
    public int ParentFor(Machine m)
    {
        foreach (var (fm, _, node) in _faults) if (fm == m && Nodes[node].Open) return node;
        int s = OpenNode($"stop:{m.Body.Id}");
        if (s >= 0) return s;
        var room = m.Body.Room;
        foreach (var k in new[] { "dark", "noair", "nowater" }) { int g = RoomState(k, room); if (g >= 0) return g; }
        int f = OpenNode($"fire:{room.Id}");
        if (f >= 0) return f;
        return HitOf(room);
    }

    /// <summary>v14.1 그 방에서 지금 열린 사고 고리 (공기 끊김·정전·단수 → 불 → 방을 때린 사고) — 없으면 -1.</summary>
    public int ParentFor(Room? room)
    {
        if (room == null) return -1;
        foreach (var k in new[] { "noair", "dark", "nowater" }) { int g = RoomState(k, room); if (g >= 0) return g; }
        int f = OpenNode($"fire:{room.Id}");
        return f >= 0 ? f : HitOf(room);
    }

    // ─────────────────────────────── 직접 원인의 갈고리 ───────────────────────────────

    /// <summary>운석·폭발이 방을 때렸다 (그 방의 감압·쓰러짐을 이 사고로 잇는다).</summary>
    public void Hit(Room? room, int node)
    {
        if (room != null && node >= 0) _roomHit[room.Id] = (node, _w.Tick);
    }

    /// <summary>불이 붙었다: 문맥 → 번져 온 칸 → 새 사고 순으로 부모를 찾는다. 방마다 화재 고리 하나.</summary>
    internal void OnIgnite(Cell c, Cell? from)
    {
        var room = _w.Ship.RoomAt(c);
        if (room == null) return;
        string key = $"fire:{room.Id}";
        int node;
        if (_open.TryGetValue(key, out var existing)) node = existing;
        else
        {
            int parent = _ctx >= 0 ? _ctx : from is Cell f && _fireCells.TryGetValue(f, out var fn) ? fn : -1;
            string text = parent >= 0 && from is Cell f2 && _w.Ship.RoomAt(f2) is Room fr && fr != room ? $"{room.Name}로 불이 번졌다" : $"{room.Name} 화재";
            node = Effect(CauseKind.Fire, key, text, room, c.Center, parent);
        }
        _fireCells[c] = node;
    }

    /// <summary>그 방에서 가장 최근의 그 종류 고리 (풀렸어도).</summary>
    public int RecentNode(int roomId, CauseKind kind)
    {
        for (int i = Nodes.Count - 1; i >= 0 && _w.Tick - Nodes[i].Tick < SimTime.Hours(12); i--)
            if (Nodes[i].Kind == kind && (Nodes[i].RoomId == roomId || kind == CauseKind.Outage && _groups.TryGetValue(i, out var set) && set.Contains(roomId))) return i;
        return -1;
    }

    public int FireNodeAt(Cell c) => _fireCells.TryGetValue(c, out var n) && _w.Fire.At(c) > 0f ? n : -1;

    /// <summary>설비가 고장 났다: 사고 속이거나 핵심 설비일 때만 고리로 (평소의 잔고장은 정비 일감일 뿐).</summary>
    internal void OnFault(Machine m, Fault f)
    {
        bool circuit = f.Circuit >= 0;
        if (_ctx < 0 && !m.Spec.Critical && !circuit && !_forceFaults) return;
        string what = circuit ? $"{PowerGrid.CircuitName(f.Circuit)} 회로 {f.Spec.Name}" : $"{m.Name} {f.Spec.Name}";
        int node = Effect(CauseKind.Fault, $"fault:{m.Body.Id}:{f.Kind}:{f.Circuit}", what, m.Body.Room, m.Body.Center);
        _faults.Add((m, f, node));
    }

    internal void OnCut(NetLink l)
    {
        var mid = l.Cells.Count > 0 ? l.Cells[l.Cells.Count / 2].Center : l.Room.Center;
        int parent = _ctx;
        if (parent < 0) foreach (var c in l.Cells) { int fn = FireNodeAt(c); if (fn >= 0) { parent = fn; break; } }
        l.Node = Effect(CauseKind.Cut, $"cut:{l.Id}", $"{l.Room.Name} {UtilityNet.Name(l.Kind)} 끊김" + (l.Cause != null ? $" ({l.Cause})" : ""), l.Room, mid, parent);
    }

    /// <summary>일을 마쳤다: 복구를 누가 했는지 짚을 때 쓴다.</summary>
    public void Worked(WorkOrder o, CrewMember c)
    {
        var t = (c.Id, _w.Tick);
        if (o.Kind == WorkKind.RepairNet && o.Circuit >= 0) _worked[$"link:{o.Circuit}"] = t;
        if (o.Target.Furniture is Furniture f) _worked[$"machine:{f.Id}"] = t;
        if (o.Target.Crew is CrewMember x) _worked[$"crew:{x.Id}"] = t;
        if (o.Target.CurrentRoom is Room r) _worked[$"room:{r.Id}"] = t;
        if (o.Kind is WorkKind.RestartReactor) _worked["reactor"] = t;
    }

    // ─────────────────────────────── 번진 상태 살피기 ───────────────────────────────

    /// <summary>시스템 틱마다: 새로 생긴 나쁜 상태를 원인에 잇고, 풀린 상태에 복구를 붙인다.</summary>
    public void Update()
    {
        var w = _w;
        var ship = w.Ship;

        // 1) 풀린 것부터 (아래에서 새로 생긴 것의 부모를 찾을 때 이미 풀린 고리를 고르지 않게)
        foreach (var (key, id) in _open.ToList())
        {
            var n = Nodes[id];
            if (!n.Open) { _open.Remove(key); continue; }
            var room = n.RoomId >= 0 ? ship.Rooms.FirstOrDefault(r => r.Id == n.RoomId) : null;
            switch (n.Kind)
            {
                case CauseKind.Fire when room == null || w.Fire.CountIn(room) == 0:
                {
                    // 사람이 껐나, 공기가 빠져 꺼졌나, 저절로 사그라들었나
                    string? by = WorkedBy(room != null ? $"room:{room.Id}" : null, SimTime.Minutes(20));
                    string how = room == null || room.Detached ? "불이 진공에 꺼졌다"
                        : by != null ? "불을 껐다"
                        : room.Air.Pressure < 40f ? "공기가 빠져 불이 꺼졌다"
                        : room.Suppression ? "자동 소화 장치가 껐다"
                        : "불이 사그라들었다";
                    Resolve(id, how, by: by ?? "");
                    break;
                }
                case CauseKind.Breach when room == null || !room.Leaking || room.Detached:
                    Resolve(id, room != null && room.Detached ? "방이 떨어져 나갔다" : "구멍을 막았다", room != null ? $"room:{room.Id}" : null);
                    break;
                case CauseKind.Suffocation when room == null || room.Air.O2 >= 17f || room.Detached:
                    Resolve(id, "숨 쉴 공기가 돌아왔다", room != null ? $"room:{room.Id}" : null, by: null);
                    break;
                case CauseKind.Flood when room == null || MoistureSystem.Depth(room) < 0.04f || room.Detached:
                    Resolve(id, "물을 다 퍼냈다", room != null ? $"room:{room.Id}" : null);
                    break;
                case CauseKind.Gas when room == null || (room.Air.CO < 0.15f && room.Air.Toxin < 0.1f):
                    Resolve(id, "가스가 걷혔다", room != null ? $"room:{room.Id}" : null);
                    break;
                case CauseKind.Cut when key.StartsWith("cut:") && w.Net.Links.FirstOrDefault(l => $"cut:{l.Id}" == key) is NetLink l && !l.Cut:
                    Resolve(id, l.Temp ? "임시로 다시 이었다" : "다시 이었다", $"link:{l.Id}");
                    break;
                case CauseKind.Scram when w.Power.ReactorOnline:
                    Resolve(id, "원자로 재가동", "reactor");
                    break;
                case CauseKind.Casualty when key.StartsWith("down:") && int.TryParse(key[5..], out var cid) && cid < w.Crew.Count && (!w.Crew[cid].Down || w.Crew[cid].Dead):
                    if (!w.Crew[cid].Dead) Resolve(id, $"{w.Crew[cid].Name} 깨어났다", $"crew:{cid}");
                    else { Nodes[id].ResolvedAt = w.Tick; _open.Remove(key); }
                    break;
                case CauseKind.Stop when key.StartsWith("stop:") && ship.Machines.FirstOrDefault(m => $"stop:{m.Body.Id}" == key) is Machine sm && !MachineDown(sm):
                    Resolve(id, $"{sm.Name} 다시 돈다", $"machine:{sm.Body.Id}");
                    break;
                case CauseKind.Stop when key.StartsWith("stop:") && !ship.Machines.Any(m => $"stop:{m.Body.Id}" == key):
                    Resolve(id, "설비가 없어졌다");
                    break;
            }
        }
        for (int i = _faults.Count - 1; i >= 0; i--)
        {
            var (m, f, node) = _faults[i];
            if (m.Faults.Contains(f) && !m.Body.Room.Detached) continue;
            _faults.RemoveAt(i);
            if (Nodes[node].Open && !_faults.Any(x => x.node == node))
                Resolve(node, m.Body.Room.Detached ? "방과 함께 떨어져 나갔다" : "고쳤다", $"machine:{m.Body.Id}");
        }
        UpdateGroups();
        for (int i = _until.Count - 1; i >= 0; i--)
        {
            var (node, done, what, work) = _until[i];
            bool finished;
            try { finished = done(); } catch { finished = true; }
            if (!finished) continue;
            _until.RemoveAt(i);
            var n = Nodes[node];
            if (n.ResolvedAt >= 0) continue;
            // 뿌리는 Lasting이 아니라서 Resolve 대신 직접 닫는다
            n.ResolvedAt = w.Tick;
            n.ResolvedBy = work != null && _worked.TryGetValue(work, out var wk) && w.Tick - wk.tick < SimTime.Hours(4) && wk.crew < w.Crew.Count ? w.Crew[wk.crew].Name : null;
            Make(CauseKind.Recovery, "", what + (n.ResolvedBy != null ? $" — {n.ResolvedBy}" : ""), ship.Rooms.FirstOrDefault(r => r.Id == n.RoomId), n.At, node, false);
            if (_incidentOf.TryGetValue(n.Incident, out var inc)) { inc.OpenCount = Math.Max(0, inc.OpenCount - 1); inc.LastActivity = w.Tick; }
        }

        // 2) 새로 생긴 나쁜 상태
        foreach (var room in ship.LiveRooms)
        {
            if (room.Detached) continue;
            bool watched = !room.Abandoned;
            // 감압
            if (room.Leaking && watched && !_open.ContainsKey($"leak:{room.Id}"))
            {
                int p = HitOf(room);
                if (p < 0) p = RecentBlowNear(room);
                if (p < 0) p = OpenNode($"fire:{room.Id}");
                Effect(CauseKind.Breach, $"leak:{room.Id}", $"{room.Name} 감압", room, null, p);
            }
            // 숨막힘 (사람이 있거나 드나드는 방)
            if (watched && room.Air.O2 < 15f && !_open.ContainsKey($"o2:{room.Id}"))
            {
                int p = OpenNode($"leak:{room.Id}");
                if (p < 0) p = OpenNode($"fire:{room.Id}");
                if (p < 0) p = RoomState("noair", room);
                if (p < 0) p = RoomState("dark", room);
                if (p < 0) p = HitOf(room);
                Effect(CauseKind.Suffocation, $"o2:{room.Id}", $"{room.Name} 산소 부족 ({room.Air.O2:0}kPa)", room, null, p);
            }
            // 가스
            if (watched && (room.Air.CO >= 0.3f || room.Air.Toxin >= 0.2f) && !_open.ContainsKey($"gas:{room.Id}"))
            {
                int p = OpenNode($"fire:{room.Id}");
                if (p < 0) p = HitOf(room);
                if (p < 0) p = RoomState("noair", room);
                Effect(CauseKind.Gas, $"gas:{room.Id}", $"{room.Name} " + (room.Air.CO >= 0.3f ? "일산화탄소" : "유독 가스"), room, null, p);
            }
            // v12.3 침수
            if (watched && MoistureSystem.Depth(room) > 0.12f && !_open.ContainsKey($"flood:{room.Id}"))
            {
                int p = -1;
                for (int i = Incidents.Count - 1; i >= 0 && p < 0; i--)
                    if (Incidents[i].Open && Nodes[Incidents[i].Root] is { Kind: CauseKind.Hazard } r0 && r0.RoomId == room.Id) p = r0.Id;
                if (p < 0) p = HitOf(room);
                if (p < 0) p = RecentBlowNear(room);
                if (p < 0) foreach (var (m, _, node) in _faults) if (m.Body.Room == room && Nodes[node].Open) { p = node; break; }
                string why = p < 0 && room.Humidity > 0.72f ? " — 결로" : p < 0 && MoistureSystem.LeakSource(room) ? " — 설비 관 이음에서 샌다" : "";
                Effect(CauseKind.Flood, $"flood:{room.Id}", $"{room.Name} 침수 ({MoistureSystem.DepthCm(room):0}cm){why}", room, null, p);
            }
            // 정전 · 단수 · 환기 끊김 (원인 하나에 방 여럿을 묶는다)
            if (!room.Powered && !_roomState.ContainsKey($"dark:{room.Id}")) Join("dark", room, InferDark(room));
            if (!room.WaterLinked && UtilityNet.NeedsWater(room) && !_roomState.ContainsKey($"nowater:{room.Id}")) Join("nowater", room, InferCut(room, NetKind.Water));
            if (!room.DuctLinked && !_roomState.ContainsKey($"noair:{room.Id}")) Join("noair", room, InferCut(room, NetKind.Air));
            if (!room.DataLinked && !_roomState.ContainsKey($"nodata:{room.Id}")) Join("nodata", room, InferCut(room, NetKind.Data));
        }

        // 원자로 긴급 정지
        var reactor = w.Power.Reactor;
        if (reactor != null && !w.Power.ReactorOnline && !_open.ContainsKey("scram") && !reactor.Body.Room.Detached)
            Effect(CauseKind.Scram, "scram", "원자로 긴급 정지", reactor.Body.Room, reactor.Body.Center, InferScram());

        // 핵심 설비가 전기·물이 없어 멈췄다 (고장 고리가 따로 있으면 그걸로 충분)
        foreach (var m in ship.Machines)
        {
            if (!m.Spec.Critical || m.Body.Room.Detached || m.Body.Room.Abandoned || m.Body.Stowed) continue;
            if (m.Body.Type == FurnitureType.ReactorCore) continue;
            string key = $"stop:{m.Body.Id}";
            if (_open.ContainsKey(key) || !MachineDown(m) || _faults.Any(x => x.m == m && Nodes[x.node].Open)) continue;
            int p = RoomState("dark", m.Body.Room);
            if (p < 0 && Procedures.Plumbed(m.Body.Type)) p = RoomState("nowater", m.Body.Room);
            if (p < 0) continue; // 누가 일부러 내려 둔 것 (절전·저출력 운영)
            Effect(CauseKind.Stop, key, $"{m.Name} 멈춤", m.Body.Room, m.Body.Center, p);
        }

        // 사람
        foreach (var c in w.Crew)
        {
            string down = $"down:{c.Id}";
            if (c.Down && !c.Dead && !_open.ContainsKey(down))
                Effect(CauseKind.Casualty, down, $"{c.Name} 쓰러짐" + (c.Vitals.InjuryCause is string ic ? $" ({ic})" : ""), c.Room, c.Position, InferCasualty(c));
            if (c.Dead && !_deaths.Contains(c.Id))
            {
                _deaths.Add(c.Id);
                int p = _open.TryGetValue(down, out var dn) ? dn : InferCasualty(c);
                Effect(CauseKind.Death, "", $"{c.Name} 사망" + (c.Vitals.InjuryCause is string dc ? $" ({dc})" : ""), c.Room, c.Position, p, lasting: false);
            }
        }

        // 사고가 끝났나
        foreach (var inc in Incidents)
        {
            if (inc.End >= 0 || inc.OpenCount > 0) continue;
            if (inc.Nodes.Any(id => Nodes[id].Open) || _until.Any(u => Nodes[u.node].Incident == inc.Root))
            { inc.OpenCount = inc.Nodes.Count(id => Nodes[id].Open) + _until.Count(u => Nodes[u.node].Incident == inc.Root); continue; }
            if (w.Tick - inc.LastActivity >= SimTime.Minutes(10)) { inc.End = w.Tick; Version++; }
        }
        if (Incidents.Count > 600) Incidents.RemoveAll(i => i.End >= 0 && w.Tick - i.End > SimTime.TicksPerDay * 20);
    }

    private readonly HashSet<int> _deaths = new();

    /// <summary>그 대상을 최근에 손본 사람.</summary>
    private string? WorkedBy(string? key, long within) =>
        key != null && _worked.TryGetValue(key, out var wk) && _w.Tick - wk.tick < within && wk.crew < _w.Crew.Count ? _w.Crew[wk.crew].Name : null;

    /// <summary>방금(2분 안에) 가까이서 터진 운석·폭발 (옆방 벽까지 흔든 것).</summary>
    private int RecentBlowNear(Room room)
    {
        for (int i = Nodes.Count - 1; i >= 0; i--)
        {
            var n = Nodes[i];
            if (_w.Tick - n.Tick > SimTime.Minutes(2)) break;
            if (n.Kind is not (CauseKind.Impact or CauseKind.Explosion) || n.At is not Vector2 at) continue;
            if ((at - room.Center).Length() < 9f) return n.Id;
        }
        return -1;
    }

    private static bool MachineDown(Machine m) => m.Stopped || !m.Powered || m.Efficiency < 0.05f;

    private int HitOf(Room room) =>
        _roomHit.TryGetValue(room.Id, out var h) && _w.Tick - h.tick < SimTime.Hours(1) ? h.node : -1;

    private int RoomState(string kind, Room room) => _roomState.TryGetValue($"{kind}:{room.Id}", out var id) && Nodes[id].Open ? id : -1;

    /// <summary>정전의 원인: 끊긴 전력 간선 → 원자로 정지 → 회로 고장 → 배전실이 꺼짐.</summary>
    private int InferDark(Room room)
    {
        if (room.BreakerOff) { int f = OpenNode($"flood:{room.Id}"); if (f >= 0) return f; }
        if (!room.PowerLinked) { int c = InferCut(room, NetKind.Power); if (c >= 0) return c; }
        if (OpenNode("scram") is int s && s >= 0) return s;
        foreach (var (m, f, node) in _faults)
            if (m.Body.Type == FurnitureType.PowerPanel && f.Circuit == room.Circuit && Nodes[node].Open) return node;
        foreach (var (m, f, node) in _faults)
            if (m.Body.Type == FurnitureType.PowerPanel && f.Circuit < 0 && Nodes[node].Open) return node;
        return -1;
    }

    /// <summary>망이 끊겨 못 받는 방: 그 방을 하류에 둔 끊긴 토막 (가장 먼저 끊긴 것).</summary>
    private int InferCut(Room room, NetKind kind)
    {
        int best = -1;
        foreach (var l in _w.Net.Links)
        {
            if (l.Kind != kind || !l.Cut || l.Node < 0 || l.Node >= Nodes.Count || !Nodes[l.Node].Open) continue;
            if (best >= 0 && l.Node >= best) continue;
            if (_w.Net.Downstream(l).Contains(room)) best = l.Node;
        }
        if (best < 0)
            foreach (var l in _w.Net.Links)
                if (l.Kind == kind && l.Cut && l.Node >= 0 && l.Node < Nodes.Count && Nodes[l.Node].Open && (best < 0 || l.Node < best)) best = l.Node;
        return best;
    }

    /// <summary>원자로가 멈춘 까닭: 냉각 펌프의 고장·멈춤 → 냉각실 정전·단수 → 그 밖의 열린 사고.</summary>
    private int InferScram()
    {
        foreach (var m in _w.Ship.FurnitureOf(FurnitureType.CoolantPump).Select(f => f.Machine!))
        {
            foreach (var (fm, _, node) in _faults) if (fm == m && Nodes[node].Open) return node;
            if (OpenNode($"stop:{m.Body.Id}") is int s && s >= 0) return s;
            int d = RoomState("dark", m.Body.Room);
            if (d >= 0) return d;
            int nw = RoomState("nowater", m.Body.Room);
            if (nw >= 0) return nw;
        }
        foreach (var (m, _, node) in _faults) if (m.Body.Type == FurnitureType.ReactorCore && Nodes[node].Open) return node;
        return LatestOpenRoot();
    }

    /// <summary>쓰러진 까닭: 그 방의 불·산소 부족·가스·감압 → 방금 그 방을 때린 사고 → 열린 사고.</summary>
    private int InferCasualty(CrewMember c)
    {
        var room = c.Room;
        if (room != null)
        {
            string cause = c.Vitals.InjuryCause ?? "";
            if (cause.Contains("불") || cause.Contains("화상")) { int f = OpenNode($"fire:{room.Id}"); if (f >= 0) return f; }
            foreach (var k in new[] { "o2", "gas", "fire", "leak" })
            {
                int id = OpenNode($"{k}:{room.Id}");
                if (id >= 0) return id;
            }
            int hit = HitOf(room);
            if (hit >= 0) return hit;
        }
        return LatestOpenRoot();
    }

    private int LatestOpenRoot()
    {
        for (int i = Incidents.Count - 1; i >= 0; i--)
            if (Incidents[i].Open && _w.Tick - Incidents[i].LastActivity < SimTime.Hours(6)) return Incidents[i].Root;
        return -1;
    }

    // ── 묶음 상태: 원인 하나에 방 여럿 (정전 16곳을 고리 16개가 아니라 하나로) ──

    private void Join(string kind, Room room, int parent)
    {
        if (parent < 0) return; // 원인이 없는 정전 (절전·손으로 내림)은 사고가 아니다
        string gkey = $"{kind}@{parent}";
        if (!_open.TryGetValue(gkey, out var id))
        {
            var ck = kind == "dark" ? CauseKind.Outage : kind == "nowater" ? CauseKind.NoWater : kind == "nodata" ? CauseKind.NoData : CauseKind.NoAir;
            id = Effect(ck, gkey, "", room, null, parent);
            _groups[id] = new HashSet<int>();
        }
        _groups[id].Add(room.Id);
        _roomState[$"{kind}:{room.Id}"] = id;
        Nodes[id].Text = GroupText(kind, id);
    }

    private string GroupText(string kind, int id)
    {
        var names = _groups[id].Select(r => _w.Ship.Rooms.FirstOrDefault(x => x.Id == r)?.Name ?? "?").ToList();
        string what = kind == "dark" ? "정전" : kind == "nowater" ? "단수" : kind == "nodata" ? "감지기·원격 제어 끊김" : "환기 끊김";
        string list = names.Count <= 3 ? string.Join("·", names) : $"{string.Join("·", names.Take(3))} 외 {names.Count - 3}곳";
        return $"{what} — {list}";
    }

    private void UpdateGroups()
    {
        foreach (var (key, id) in _roomState.ToList())
        {
            int colon = key.IndexOf(':');
            string kind = key[..colon];
            int roomId = int.Parse(key[(colon + 1)..]);
            var room = _w.Ship.Rooms.FirstOrDefault(r => r.Id == roomId);
            bool still = room != null && !room.Detached && (kind == "dark" ? !room.Powered : kind == "nowater" ? !room.WaterLinked : kind == "nodata" ? !room.DataLinked : !room.DuctLinked);
            if (still && Nodes[id].Open) continue;
            _roomState.Remove(key);
            if (_groups.TryGetValue(id, out var set))
            {
                set.Remove(roomId);
                if (set.Count == 0 && Nodes[id].Open)
                {
                    var parent = Nodes[id].Parent;
                    string work = parent >= 0 && Nodes[parent].Key.StartsWith("cut:") ? $"link:{Nodes[parent].Key[4..]}"
                        : parent >= 0 && Nodes[parent].Kind == CauseKind.Scram ? "reactor" : "";
                    Resolve(id, kind == "dark" ? "전기가 다시 들어왔다" : kind == "nowater" ? "물이 다시 들어왔다" : kind == "nodata" ? "감지기 값이 다시 들어온다" : "환기가 다시 돈다", work.Length > 0 ? work : null);
                }
            }
        }
    }

    // ─────────────────────────────── 읽기 ───────────────────────────────

    /// <summary>화면에 띄울 사고 (열린 것 먼저, 무거운 것 먼저).</summary>
    public IEnumerable<CauseIncident> Notable(int minWeight = 2) =>
        Incidents.Where(i => i.Weight(this) >= minWeight).OrderByDescending(i => i.Open).ThenByDescending(i => i.Start);

    /// <summary>한 사고를 들여쓰기한 줄들로 (시험·기록용).</summary>
    public IEnumerable<string> Tree(CauseIncident inc)
    {
        var stack = new Stack<int>();
        stack.Push(inc.Root);
        while (stack.Count > 0)
        {
            var n = Nodes[stack.Pop()];
            string state = n.Kind == CauseKind.Recovery ? "↺ " : n.Open ? "● " : n.Lasting ? "○ " : "· ";
            string rep = n.Repeats > 0 ? $" ×{n.Repeats + 1}" : "";
            yield return $"{new string(' ', n.Depth * 2)}{state}{SimTime.Clock(n.Tick)} {n.Text}{rep}";
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
    }
}
