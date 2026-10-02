using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.19 페일세이프 설계 — 고장 나면 안전한 쪽으로 멈춘다. 컴퓨터도 전기도 없어도 버티는 물리 장치만:
//  · 두 갈래 급전: 필수 방(생명유지 · 의무 · 함교/주 컴퓨터 · 냉각 · 원자로 · 배전)은 주 회로 + 다른 회로(자동 절체) + 예비 배선 모선 —
//    주 회로 차단기가 떨어져도 저절로 다른 갈래로 넘어간다. 간선도 두 길: 문을 지나는 간선 + 선체 속 보조 간선(RingMain을 기본 설비로).
//  · 구획화: 차압 문(양쪽 기압 차가 크면 문이 스스로 닫혀 걸린다 — 압력이 문을 눌러 막는다) · 압력 경계(필수 방 · 구획 격벽 문은 더 작은 차에도) ·
//    역류 방지 댐퍼(새는 방의 환기구가 저절로 닫힌다) · 큰 방의 비상 칸막이(큰 구멍이면 펼쳐져 방 공기가 한꺼번에 빠지지 않는다).
//  · 장갑: 원자로 · 배전 · 주 컴퓨터실 벽은 두꺼운 외판과 골조 (충격 피해 절반) — 배치는 v16.22가 안쪽으로 옮긴다.
//  · 부하 차단 계전기: 원자로가 멈춰 배터리만으로 버틸 때 편의 → 지원 설비부터 내린다 (필수도 표 · 컴퓨터 없이도).
// 승무원: 저절로 닫힌 차압 문을 본 사람은 저쪽이 샌다고 믿고(믿음) 무서워 피한다 · 불이 꺼지지 않은 필수 방에서 마음이 놓인다.
// 주 컴퓨터: 페일세이프가 버틴 것을 읽고 판단 근거로 남긴다 (구획이 버틴다 · 다른 갈래로 산다 · 휜 문틀은 사람을 보내야 한다).
// 난수는 쓰지 않는다. 돌 때는 Id 순서 → 같은 시드면 같은 결과.

public sealed class FailsafeEvent
{
    public long Tick { get; init; }
    public string Kind { get; init; } = ""; // latch · unlatch · fail · damper · partition · ats · shed · ring
    public int Room { get; init; } = -1;
    public int Door { get; init; } = -1;
    public string Text { get; init; } = "";
}

public sealed class FailsafeSystem
{
    /// <summary>보통 문이 스스로 닫히는 기압 차 (kPa).</summary>
    public const float LatchKpa = 12f;
    /// <summary>압력 경계 문 (필수 방 · 구획 격벽 문).</summary>
    public const float RatedLatchKpa = 6f;
    /// <summary>이만큼 맞춰지면 풀린다.</summary>
    public const float ReleaseKpa = 3f;
    /// <summary>비상 칸막이가 있는 방 크기 (칸).</summary>
    public const int PartitionCells = 40;
    /// <summary>비상 칸막이를 펼치면 빠지는 속도.</summary>
    public const float PartitionLeak = 0.4f;

    private readonly World _w;
    private bool _seeded;
    private long _nextSeed;
    private readonly SortedDictionary<int, long> _latched = new();
    private readonly HashSet<int> _rated = new();
    private readonly HashSet<int> _failNoted = new();
    private readonly HashSet<int> _seededRooms = new();
    private readonly Dictionary<int, long> _partitionCalm = new();
    private readonly HashSet<int> _alt = new(), _altPrev = new();
    private long _altTick = -1;
    private int _shedLevel;
    private long _nextRing;

    public int Latches, Unlatches, LatchFails, DamperCloses, PartitionDeploys, Transfers, RingRooms, ArmorWalls, Partitions, ShedEvents, RingCarried, Calmed, Believed;
    /// <summary>지금 계전기가 내린 단계 (0 없음 · 1 편의 · 2 지원까지).</summary>
    public int ShedLevel => _shedLevel;
    public int ShedNow { get; private set; }
    public List<FailsafeEvent> Events { get; } = new();

    public FailsafeSystem(World w) => _w = w;

    public IEnumerable<int> LatchedDoors => _latched.Keys;
    public bool Latched(Door d) => _latched.ContainsKey(d.Id);
    public bool Rated(Door d) => _rated.Contains(d.Id);
    /// <summary>지금 다른 갈래(주 회로가 아닌 회로)로 전기를 받는 방.</summary>
    public bool OnAlt(Room r) => _alt.Contains(r.Id);

    private void Note(string kind, string text, Room? room = null, Door? door = null)
    {
        Events.Add(new FailsafeEvent { Tick = _w.Tick, Kind = kind, Room = room?.Id ?? -1, Door = door?.Id ?? -1, Text = text });
        if (Events.Count > 120) Events.RemoveAt(0);
    }

    // ─────────────────────────────── 처음 설비 ───────────────────────────────

    private void Seed()
    {
        _seeded = true;
        var ship = _w.Ship;
        // 장갑 벽: 원자로 · 배전 · 주 컴퓨터실 (그 방에 닿은 벽 — 외벽이든 칸막이벽이든)
        foreach (var (cell, wall) in ship.Walls)
        {
            float best = 1f;
            foreach (var d in Cell.Dirs4)
                if (ship.RoomAt(cell + d) is Room r && !r.Detached) best = MathF.Min(best, ArmorOf(r));
            if (best < 0.999f && wall.Armor > best) { wall.Armor = best; ArmorWalls++; }
        }
        SeedRooms();
    }

    /// <summary>장갑 배율: 원자로 · 배전 · 주 컴퓨터 0.5 · 생명유지 0.75.</summary>
    public static float ArmorOf(Room r)
    {
        if (r.Type is RoomType.Reactor or RoomType.Power or RoomType.ServerRoom or RoomType.Substation or RoomType.BatteryRoom
            || r.Furniture.Any(f => f.Type is FurnitureType.MainComputer or FurnitureType.ReactorCore or FurnitureType.PowerPanel)) return 0.5f;
        return r.Type == RoomType.LifeSupport ? 0.75f : 1f;
    }

    /// <summary>필수 방마다 두 갈래 급전 · 보조 간선 · 압력 경계 문, 큰 방은 비상 칸막이 (새로 붙은 방도 한 시간마다).</summary>
    private void SeedRooms()
    {
        var w = _w;
        var ship = w.Ship;
        var src = w.Net.SourceRoom(NetKind.Power);
        foreach (var r in ship.Rooms.OrderBy(r => r.Id))
        {
            if (r.Detached || r.Merged || _seededRooms.Contains(r.Id)) continue;
            _seededRooms.Add(r.Id);
            if (r.Volume >= PartitionCells && r.Partition == 0) { r.Partition = 1; Partitions++; }
            if (!Essentials.DualFeed(r)) continue;
            if (r.AltCircuit < 0) r.AltCircuit = r.Circuit == 0 ? 2 : 0;
            foreach (var d in r.Doors) if (!d.IsExternal) _rated.Add(d.Id);
            if (src != null && r != src && !w.Net.Rings.Any(x => x.kind == NetKind.Power && (x.from == r.Id || x.to == r.Id)))
            {
                w.Net.Rings.Add((NetKind.Power, src.Id, r.Id)); // 다음 망 짜기에서 선체 속 보조 간선이 생긴다
                RingRooms++;
            }
        }
        foreach (var d in ship.Doors) if (d.Bulkhead) _rated.Add(d.Id);
    }

    // ─────────────────────────────── 두 갈래 급전 (전력망이 부른다) ───────────────────────────────

    /// <summary>
    /// 이 방이 실제로 전기를 받는 회로: 주 회로 → 다른 갈래(자동 절체) → 예비 배선 모선(살아 있는 아무 회로). 못 받으면 주 회로.
    /// 필수 방만 둘째 · 셋째 길이 있다. 예전 값(Legacy)이면 늘 주 회로.
    /// </summary>
    public int Feed(Room r, bool[] fed)
    {
        if (fed[r.Circuit] || r.AltCircuit < 0 || Durability.Legacy) return r.Circuit;
        if (_altTick != _w.Tick) { _altPrev.Clear(); _altPrev.UnionWith(_alt); _alt.Clear(); _altTick = _w.Tick; }
        int c = fed[r.AltCircuit] ? r.AltCircuit : -1;
        for (int i = 0; c < 0 && i < fed.Length; i++) if (fed[i]) c = i;
        if (c < 0) return r.Circuit;
        _alt.Add(r.Id);
        return c;
    }

    /// <summary>비상 칸막이가 펼쳐진 큰 방은 천천히 빠진다.</summary>
    public float LeakMul(Room r) => !Durability.Legacy && r.Partition == 2 ? PartitionLeak : 1f;

    // ─────────────────────────────── 부하 차단 계전기 (전력망이 부른다) ───────────────────────────────

    /// <summary>원자로가 멈춰 배터리로 버틴다: 60% 아래면 편의 설비, 30% 아래면 지원 설비까지 내린다 (원자로가 돌아오거나 70% 넘게 차면 푼다).</summary>
    public void Park()
    {
        if (Durability.Legacy) return;
        var p = _w.Power;
        bool onBattery = !p.ReactorOnline || p.LowPowerMode || p.BatteryFlow < -1f && p.BatteryPercent < 0.6f; // 원자로가 멈췄거나 모자라 배터리가 빠진다
        float b = p.BatteryPercent;
        int want = !onBattery ? (b > 0.7f || p.ReactorRamp >= 1f ? 0 : _shedLevel) : b < 0.3f || _shedLevel >= 1 && p.ShedCount > 0 ? 2 : b < 0.6f || p.ShedCount > 0 ? Math.Max(1, _shedLevel) : _shedLevel; // 배터리가 바닥나거나 한 번에 낼 세기가 모자라면 (방이 꺼지기 전에)
        if (!onBattery && p.ReactorRamp >= 1f && p.BatteryFlow >= 0f) want = 0;
        if (want != _shedLevel)
        {
            int before = _shedLevel;
            _shedLevel = want;
            ShedEvents++;
            string text = want == 0 ? "부하 차단 계전기 해제 — 주 전력이 돌아와 내렸던 설비를 다시 켠다"
                : want == 1 ? $"부하 차단 계전기 1단 — 배터리 {b * 100:0}% · 급하지 않은 설비부터 내린다 (생명유지 · 주 컴퓨터는 그대로)"
                : $"부하 차단 계전기 2단 — 배터리 {b * 100:0}% · 작업 설비까지 내린다";
            _w.Log.Add(_w.Tick, want > before ? LogKind.Warning : LogKind.Ship, text);
            Note("shed", text);
            _w.Automation.Reason("fs:shed", $"원자로가 멈춰 배터리로 버틴다 — 계전기가 {(want == 0 ? "다시 올렸다" : want == 1 ? "급하지 않은 설비를 내렸다" : "작업 설비까지 내렸다")} · 남은 배터리 {b * 100:0}%", SimTime.Minutes(30));
        }
        ShedNow = 0;
        if (_shedLevel == 0) return;
        foreach (var m in _w.Ship.Machines)
        {
            if (m.Spec.PowerDraw <= 0f || m.Body.Room.Detached) continue;
            var e = Essentials.Of(m.Body.Type);
            if (e == Essential.Comfort || _shedLevel >= 2 && e == Essential.Support) { m.Parked = true; ShedNow++; }
        }
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        if (Durability.Legacy) return;
        var w = _w;
        if (!_seeded) Seed();
        if (w.Tick >= _nextSeed) { _nextSeed = w.Tick + SimTime.Hours(1); SeedRooms(); }
        Doors();
        Dampers();
        PartitionsTick();
        AltTick();
        if (w.Tick >= _nextRing) { _nextRing = w.Tick + SimTime.Minutes(10); RingTick(); }
        Review();
    }

    private bool _wasOnline = true;
    private long _offSince = -1;
    public int Reviews;

    /// <summary>주 컴퓨터가 멎었다 다시 돌면: 멎은 동안 저절로 버틴 일(문 · 댐퍼 · 칸막이 · 회로)을 읽고 정리한다.</summary>
    private void Review()
    {
        var w = _w;
        bool on = w.Automation.MainOnline;
        if (!on && _wasOnline) _offSince = w.Tick;
        if (on && !_wasOnline && _offSince >= 0)
        {
            var seen = Events.Where(e => e.Tick >= _offSince).ToList();
            if (seen.Count > 0)
            {
                int latch = seen.Count(e => e.Kind == "latch"), damp = seen.Count(e => e.Kind == "damper"), part = seen.Count(e => e.Kind == "partition"), ats = seen.Count(e => e.Kind == "ats"), fail = seen.Count(e => e.Kind == "fail");
                var parts = new List<string>();
                if (latch > 0) parts.Add($"차압 문 {latch}개가 저절로 닫혔다");
                if (damp > 0) parts.Add($"댐퍼 {damp}개가 저절로 닫혔다");
                if (part > 0) parts.Add($"칸막이 {part}개를 펼쳤다");
                if (ats > 0) parts.Add($"{ats}개 방이 예비 회로로 넘어갔다");
                if (fail > 0) parts.Add($"문 {fail}개는 못 닫혔다 — 그쪽부터 살핀다");
                if (parts.Count > 0)
                {
                    Reviews++;
                    w.Automation.Reason("fs:review", $"멎어 있던 동안 배가 스스로 버틴 일 — {string.Join(" · ", parts)}", SimTime.Minutes(30));
                }
            }
        }
        _wasOnline = on;
    }

    private static float P(Room? r) => r == null || r.Detached ? 0f : r.Air.Pressure;

    /// <summary>차압 문: 기압 차가 크면 문이 스스로 닫혀 걸린다 (전기 · 컴퓨터 없이). 맞춰지면 풀린다.</summary>
    private void Doors()
    {
        var w = _w;
        Dictionary<int, (Room low, List<Room> highs)>? burst = null;
        foreach (var d in w.Ship.Doors)
        {
            if (d.Removed || d.IsExternal || d.RoomA is not Room a || d.RoomB is not Room b) continue;
            float dp = MathF.Abs(P(a) - P(b));
            bool latched = _latched.ContainsKey(d.Id);
            bool rated = _rated.Contains(d.Id);
            float thr = rated ? RatedLatchKpa : LatchKpa;
            if (!latched)
            {
                if (dp < 3f || d.Welded) continue;
                var low = P(a) < P(b) ? a : b;
                var high = low == a ? b : a;
                // 낮은 쪽이 우주로 새고 있으면 작은 차에도 · 끌려가 빠진 방이면 65kPa 아래(압력 경계는 80)부터 —
                // 조금 빠진 통로 때문에 온 배의 문을 닫아 걸지 않게
                bool venting = low.Leaking || low.Detached;
                bool need = venting ? dp >= (rated ? 3f : 5f) : dp >= thr && P(low) < (rated ? 80f : 65f);
                if (!need) continue;
                if (d.JammedOpen || d.Blocked || d.Bent > 0.3f)
                {
                    if (_failNoted.Add(d.Id))
                    {
                        LatchFails++;
                        string why = d.Blocked ? "잔해가 끼어" : d.JammedOpen ? "열에 휘어 열린 채 걸려" : "문틀이 휘어";
                        string text = $"{a.Name}·{b.Name} 사이 차압 문이 {why} 닫히지 않는다 — {low.Name} 쪽으로 공기가 빠진다";
                        w.RaiseAlert(text, high, AlertLevel.Critical, shipWide: true);
                        MarkLog.Add(high.Marks, w.Tick, $"차압 문이 못 닫혔다 ({why})");
                        Note("fail", text, high, d);
                        w.Automation.Reason("fs:fail:" + d.Id, $"{text} · 사람을 보내 막아야 한다 (문을 손으로 닫거나 잔해를 치운다)", SimTime.Minutes(20));
                    }
                    continue;
                }
                _latched[d.Id] = w.Tick;
                _failNoted.Remove(d.Id);
                d.Locked = true;
                d.Openness = MathF.Min(d.Openness, 0.25f); // 압력에 떠밀려 쾅 닫힌다
                Latches++;
                Note("latch", $"{low.Name} 감압 {P(low):0}kPa — {high.Name} 쪽 차압 문이 저절로 닫혔다", low, d);
                MarkLog.Add(high.Marks, w.Tick, $"차압 문이 저절로 닫혔다 ({low.Name} 감압)");
                burst ??= new();
                if (!burst.TryGetValue(low.Id, out var e)) burst[low.Id] = e = (low, new List<Room>());
                if (!e.highs.Contains(high)) e.highs.Add(high);
                Witness(d, low, high);
            }
            else
            {
                bool lock2 = a.Lockdown || b.Lockdown || a.Abandoned || b.Abandoned || d.Welded;
                if (dp < ReleaseKpa && !lock2)
                {
                    _latched.Remove(d.Id);
                    d.Locked = false;
                    Unlatches++;
                    Note("unlatch", $"{a.Name}·{b.Name} 기압이 맞춰져 차압 문이 풀렸다", a, d);
                }
                else if (dp < ReleaseKpa) _latched.Remove(d.Id); // 격벽 잠금이 이어받았다
                else if (!d.Locked && dp >= thr) d.Locked = true; // 누가 격벽을 풀었지만 아직 차가 크다 — 다시 걸린다
            }
        }
        if (burst == null) return;
        // 방 하나에 한 줄로 (문 여럿이 한꺼번에 닫혀도)
        foreach (var (low, highs) in burst.Values.OrderBy(x => x.low.Id))
        {
            string names = highs.Count <= 3 ? string.Join("·", highs.Select(h => h.Name)) : $"{highs[0].Name} 등 {highs.Count}곳";
            string text = $"{low.Name} 감압 {P(low):0}kPa — {names} 쪽 차압 문이 저절로 닫혔다";
            w.Log.Add(w.Tick, LogKind.Ship, text);
            w.Automation.Reason("fs:latch:" + low.Id, $"{low.Name} 감압 — 차압 문이 저절로 닫혀 옆 구획이 버틴다 · {highs[0].Name} {P(highs[0]):0}kPa 유지 · 구멍을 막으면 기압이 맞춰져 풀린다", SimTime.Minutes(30));
        }
    }

    /// <summary>문이 쾅 닫히는 것을 본 사람: 저쪽이 샌다고 믿고 그 방을 무서워한다 · 같은 방에 있던 사람은 버텨 준 배에 마음이 놓인다.</summary>
    private void Witness(Door d, Room low, Room high)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Room != high && c.Room != low) continue;
            if ((c.Position - d.Cell.Center).LengthSquared() > 64f && c.Room != low) continue;
            w.Brain2.Beliefs.Learn(c, Topic.Breach, low.Id, 1, BeliefSource.Seen, 0.85f);
            Believed++;
            if (c.Room == high)
            {
                Memory.Frighten(w, c, low, 0.12f, "차압 문이 저절로 쾅 닫혔다 — 저쪽은 공기가 빠진다");
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.04f); // 문이 막아 줬다
                Calmed++;
            }
        }
    }

    /// <summary>역류 방지 댐퍼: 새는 방의 환기구는 전기 · 컴퓨터 없이도 닫힌다 (다른 방 공기가 덕트로 빨려 나가지 않게).</summary>
    private void Dampers()
    {
        var w = _w;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached || !r.VentOpen || r.DamperJammed || r.DamperStuck) continue;
            if (!(r.Leaking && (r.LeakArea >= 0.03f || r.Air.Pressure < 85f))) continue;
            r.VentOpen = false;
            DamperCloses++;
            string text = $"{r.Name} 역류 방지 댐퍼가 저절로 닫혔다 (감압)";
            w.Log.Add(w.Tick, LogKind.Ship, text);
            Note("damper", text, r);
        }
    }

    /// <summary>큰 방의 비상 칸막이: 큰 구멍이면 펼쳐진다 · 막고 30분 뒤 접는다.</summary>
    private void PartitionsTick()
    {
        var w = _w;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Partition == 0 || r.Detached) continue;
            if (r.Partition == 1 && r.LeakArea >= 0.15f)
            {
                r.Partition = 2;
                PartitionDeploys++;
                _partitionCalm.Remove(r.Id);
                string text = $"{r.Name} 비상 칸막이가 펼쳐졌다 — 구멍 쪽 칸만 빠르게 빠진다";
                w.RaiseAlert(text, r, AlertLevel.Warning, shipWide: false);
                MarkLog.Add(r.Marks, w.Tick, "비상 칸막이를 펼쳤다");
                Note("partition", text, r);
                w.Automation.Reason("fs:part:" + r.Id, $"{text} · 안의 사람은 칸막이 반대쪽 문으로", SimTime.Minutes(30));
                foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == r && c.IsAwake)) Memory.Frighten(w, c, r, 0.15f, "비상 칸막이가 눈앞에서 펼쳐졌다");
            }
            else if (r.Partition == 2)
            {
                if (r.Leaking) { _partitionCalm.Remove(r.Id); continue; }
                if (!_partitionCalm.TryGetValue(r.Id, out var since)) { _partitionCalm[r.Id] = w.Tick; continue; }
                if (w.Tick - since < SimTime.Minutes(30)) continue;
                r.Partition = 1;
                _partitionCalm.Remove(r.Id);
                w.Log.Add(w.Tick, LogKind.Ship, $"{r.Name} 비상 칸막이를 접었다");
            }
        }
    }

    /// <summary>두 갈래 급전이 넘어간 방을 적는다 (주 컴퓨터가 읽고 · 방 안 사람은 불이 안 꺼져 덜 놀란다).</summary>
    private void AltTick()
    {
        var w = _w;
        if (_altTick < 0) return;
        bool stale = _altTick != w.Tick && w.Tick - _altTick > World.SystemInterval; // 이번 틱엔 아무도 넘어가지 않았다
        if (stale) { _altPrev.Clear(); _altPrev.UnionWith(_alt); _alt.Clear(); _altTick = w.Tick; }
        foreach (var id in _alt.OrderBy(x => x))
        {
            if (_altPrev.Contains(id)) continue;
            var r = w.Ship.Rooms.FirstOrDefault(x => x.Id == id);
            if (r == null) continue;
            Transfers++;
            string text = $"{r.Name} — {PowerGrid.CircuitName(r.Circuit)} 회로가 떨어져 예비 회로로 넘겨 받았다 · 불은 그대로";
            w.Log.Add(w.Tick, LogKind.Ship, text);
            MarkLog.Add(r.Marks, w.Tick, $"{PowerGrid.CircuitName(r.Circuit)} 회로가 떨어져 예비 회로로 넘겨 받았다");
            Note("ats", text, r);
            w.Automation.Reason("fs:ats:" + r.Id, $"{text} · 생명유지 · 컴퓨터는 산다 — 차단기는 원인부터 보고 올린다", SimTime.Minutes(30));
            foreach (var c in w.Crew)
                if (!c.Dead && c.Room == r && c.IsAwake) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f); Calmed++; }
        }
    }

    /// <summary>문을 지나는 간선이 끊겼는데 보조 간선으로 전기를 받는 필수 방을 센다.</summary>
    private void RingTick()
    {
        var w = _w;
        foreach (var (k, from, to) in w.Net.Rings)
        {
            if (k != NetKind.Power) continue;
            var r = w.Ship.Rooms.FirstOrDefault(x => x.Id == to);
            if (r == null || r.Detached || !r.PowerLinked) continue;
            bool doorCut = w.Net.Links.Where(l => l.Kind == NetKind.Power && l.Door != null && (l.Door.RoomA == r || l.Door.RoomB == r)).All(l => l.Cut);
            if (!doorCut || r.Doors.Count == 0) continue;
            RingCarried++;
            string text = $"{r.Name} 문 쪽 간선이 모두 끊겼지만 선체 속 보조 간선으로 전기가 든다";
            if (!Events.Any(e => e.Kind == "ring" && e.Room == r.Id && w.Tick - e.Tick < SimTime.Hours(2)))
            {
                w.Log.Add(w.Tick, LogKind.Ship, text);
                Note("ring", text, r);
                w.Automation.Reason("fs:ring:" + r.Id, text + " · 간선 수리는 급하지 않다", SimTime.Hours(1));
            }
        }
    }

    /// <summary>지문.</summary>
    public void Hash(Action<long> I)
    {
        I(_latched.Count); I(Latches); I(Unlatches); I(LatchFails); I(DamperCloses); I(PartitionDeploys); I(Transfers); I(RingRooms); I(ShedEvents); I(_shedLevel);
        foreach (var (id, t) in _latched) { I(id); I(t % 1000003); }
    }
}
