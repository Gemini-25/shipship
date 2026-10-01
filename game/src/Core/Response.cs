using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v13.0 중앙 컴퓨터의 대응 수순 (기능 모듈).
//
// 화재 대응 수순 (기본 모듈): 감지 → 댐퍼 닫기 → 자동 소화(있으면) → 소화조 → 그래도 안 되면
//   질식 소화(불활성 가스로 산소를 밀어낸다 — 공기는 지키지만 가스가 한정) → 진공 소화(배기 밸브로 공기를 바깥으로 — 확실하지만 공기·작물을 잃는다)
//   → 다시 가압 (공기 탱크) → 격벽 해제. 누가 안에 있으면 방침대로: 금지 / 빈 방만 / 카운트다운 뒤 / 컴퓨터 판단.
// 공기 구역 관리 (기본 모듈): 여러 방이 한꺼번에 새면 지킬 구역(생명유지실이 있는 이어진 방들)을 정해 사람을 모으고,
//   공기 탱크가 모자라면 그 구역만 채우며, 아무도 못 막는 바깥 방은 방침(구역 포기 시점)대로 일찍 포기한다.
// 나머지 모듈(생체 감시 · 자원 배분 · 대피 안내 · 선제 조치 · 교훈 반영)은 연구·개조로 단다 (v13.1~).

public enum ComputerModule { FireResponse, AirZones, BioMonitor, ResourceAlloc, EvacGuide, Preempt, Lessons }

public sealed class FireCase
{
    public int RoomId { get; init; }
    public long Since { get; init; }
    /// <summary>0 지켜본다(소화조) · 1 대피 기다림 · 2 실행 · 3 다시 가압.</summary>
    public int Stage { get; set; }
    /// <summary>"inert" 질식 · "vacuum" 진공.</summary>
    public string Method { get; set; } = "";
    public long ExecAt { get; set; } = -1;
    public long PlannedAt { get; set; } = -1;
    public long StartedAt { get; set; } = -1;
    public long OutSince { get; set; } = -1;
    public bool TriedInert { get; set; }
    public string Status { get; set; } = "소화조가 끈다";
    public int Node { get; set; } = -1;
}

public sealed partial class AutomationSystem
{
    /// <summary>진공 소화: 시간당 배기 비율 (지수) — 2~3분이면 30kPa 아래로.</summary>
    public const float PurgeRate = 30f;

    /// <summary>질식 소화: 산소를 밀어내는 속도 (kPa/시간) — 2분 남짓이면 6kPa 아래로.</summary>
    public const float InertRate = 480f;

    private readonly HashSet<ComputerModule> _modules = new() { ComputerModule.FireResponse, ComputerModule.AirZones };
    public IReadOnlyCollection<ComputerModule> Modules => _modules;
    public bool Has(ComputerModule m) => _modules.Contains(m);
    public void Install(ComputerModule m)
    {
        if (!_modules.Add(m)) return;
        _world.History.Add(_world, HistoryKind.Decision, $"주 컴퓨터에 {ModuleName(m)} 모듈을 달았다 — {ModuleNote(m)}", null, log: true);
    }
    public void Remove(ComputerModule m) => _modules.Remove(m);

    public static string ModuleName(ComputerModule m) => m switch
    {
        ComputerModule.FireResponse => "화재 대응 수순",
        ComputerModule.AirZones => "공기 구역 관리",
        ComputerModule.BioMonitor => "생체 감시",
        ComputerModule.ResourceAlloc => "자원 배분",
        ComputerModule.EvacGuide => "대피 안내",
        ComputerModule.Preempt => "선제 조치",
        _ => "교훈 반영",
    };

    public static string ModuleNote(ComputerModule m) => m switch
    {
        ComputerModule.FireResponse => "소화조로 안 되면 질식 소화 → 진공 소화 → 다시 가압 (방침대로)",
        ComputerModule.AirZones => "여러 방이 새면 지킬 구역을 정해 사람을 모으고 그 구역부터 채운다",
        ComputerModule.BioMonitor => "승무원 위치·혈중 산소를 본다 — 데이터선이 끊긴 방도",
        ComputerModule.ResourceAlloc => "우주복·소화기·구급상자를 역할에 맞게 나눈다",
        ComputerModule.EvacGuide => "조명으로 대피 길을 그리고 문을 연다",
        ComputerModule.Preempt => "연쇄를 예측해 미리 끊는다 (보수적 — 헛정지가 잦다)",
        _ => "지난 사고의 교훈을 다음 수순에 넣는다",
    };

    // ── 불활성 가스 ──

    public float InertGas { get; set; } = -1f;
    public float InertCapacity { get; private set; }

    private void EnsureInert()
    {
        if (InertCapacity > 0f) return;
        var rooms = _world.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).ToList();
        float avg = rooms.Count > 0 ? (float)rooms.Average(r => r.Volume) : 30f;
        int extra = _world.Ship.Rooms.Count(r => r.Kind == RoomType.SuppressionRoom);
        InertCapacity = 17f * avg * (2.5f + 4f * extra); // 보통 방 두세 번 (소화 설비실마다 네 번 더)
        if (InertGas < 0f) InertGas = InertCapacity;
    }

    // ── 화재 대응 수순 ──

    public List<FireCase> FireCases { get; } = new();
    public int Smothered, Vacuumed, ResponseWaits;

    /// <summary>소화 대응이 댐퍼를 쥐고 있다 (실행 중이거나 질식 가스가 아직 빠지지 않았다).</summary>
    public bool KeepDamperShut(Room r) => r.ResponseHold && FireCases.FirstOrDefault(f => f.RoomId == r.Id) is { Stage: 1 or 2 };

    private static bool CriticalRoom(Room r) =>
        r.Type is RoomType.Reactor or RoomType.Power or RoomType.LifeSupport or RoomType.Cooling or RoomType.Bridge
        || r.Furniture.Any(f => f.Machine is Machine m && m.Spec.Critical);

    /// <summary>바깥으로 뺄 수 있는 방 (외벽이 있다 — 배기 밸브).</summary>
    private bool CanVent(Room r) => _world.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(_world.Ship, kv.Key) == r);

    /// <summary>안에 누가 있나 — 데이터선이 이어진 방은 문 감지기로 안다 (생체 감시면 끊겨도). 모르면 null.</summary>
    private int? Occupants(Room r)
    {
        if (!r.DataLinked && !Has(ComputerModule.BioMonitor)) return null;
        return _world.Crew.Count(c => !c.Dead && c.Room == r && !c.Outside);
    }

    private void FireResponse()
    {
        var w = _world;
        var fires = w.Fire.KnownFires().ToList();
        // 새 불
        foreach (var (room, _, _) in fires)
            if (!FireCases.Any(f => f.RoomId == room.Id))
                FireCases.Add(new FireCase { RoomId = room.Id, Since = w.Tick });

        foreach (var fc in FireCases.ToList())
        {
            var room = w.Ship.Rooms[fc.RoomId];
            int cells = room.Detached ? 0 : w.Fire.CountIn(room);
            bool burning = cells > 0;
            float minutes = (w.Tick - fc.Since) / (float)SimTime.Minutes(1);

            switch (fc.Stage)
            {
                case 0: // 소화조가 끈다 — 안 되면 다음 수단
                {
                    if (!burning) { FireCases.Remove(fc); continue; }
                    bool critical = CriticalRoom(room);
                    bool crewOnIt = w.Board.Open.Any(o => o.Kind == WorkKind.Extinguish && o.Target.CurrentRoom == room && o.Assignee != null);
                    float grace = critical ? 3f : 8f;
                    if (!crewOnIt) grace = MathF.Min(grace, 4f);
                    bool escalate = cells >= 6 || minutes >= grace && (cells >= 2 || !crewOnIt);
                    if (Level < 4) escalate &= minutes >= grace * 1.5f; // 추론이 안 되면 늦게 (보수적으로) 올린다
                    if (!escalate) { fc.Status = crewOnIt ? $"소화조가 끈다 ({cells}칸 · {minutes:0}분)" : $"소화조를 기다린다 ({cells}칸)"; break; }
                    // v13.2 방침(컴퓨터 자동 실행): 전부가 아니면 소화 수순은 하지 않는다
                    if (w.Policies["autoscope"] < 2) { fc.Status = $"자동 실행 범위 밖 — 소화조에 맡긴다 ({cells}칸)"; break; }
                    var method = PickMethod(room, fc);
                    if (method == null) { fc.Status = $"쓸 수단이 없다 — 소화조에 맡긴다 ({cells}칸)"; break; }
                    Plan(fc, room, method, cells, minutes);
                    break;
                }
                case 1: // 대피를 기다린다 (카운트다운이면 시간이 되면 실행)
                {
                    if (!burning) { Release(fc, room, "불이 먼저 꺼졌다"); continue; }
                    int? inside = Occupants(room);
                    var policy = Policy(fc.Method);
                    bool countdown = policy >= 2;
                    bool empty = inside == 0;
                    bool settled = w.Tick - fc.PlannedAt >= SimTime.Minutes(0.3f); // 경보가 돌고 문이 닫힐 틈
                    bool go = countdown ? w.Tick >= fc.ExecAt || empty && settled : empty && settled;
                    // 컴퓨터 판단(진공): 정신이 있는 사람이 아직 안에 있고 핵심 방이 아니면 3분 더 기다린다
                    if (go && fc.Method == "vacuum" && policy == 3 && !empty && !CriticalRoom(room)
                        && w.Crew.Any(c => !c.Dead && !c.Down && c.Room == room) && w.Tick < fc.ExecAt + SimTime.Minutes(3)) go = false;
                    if (go) Execute(fc, room);
                    else
                    {
                        fc.Status = inside is int n ? $"{(fc.Method == "vacuum" ? "진공" : "질식")} 소화 — 안에 {n}명 · 대피 기다림" : "안을 알 수 없다 (데이터선) — 기다림";
                        if (!countdown && w.Tick > fc.ExecAt + SimTime.Minutes(10)) { ResponseWaits++; Release(fc, room, "10분을 기다려도 방이 비지 않았다 — 소화조에 맡긴다"); }
                    }
                    break;
                }
                case 2: // 실행 중 — 꺼지면 복구, 질식으로 안 되면 진공으로
                {
                    if (!burning)
                    {
                        if (fc.OutSince < 0) fc.OutSince = w.Tick;
                        if (w.Tick - fc.OutSince >= SimTime.Minutes(2)) Restore(fc, room);
                        else fc.Status = "불이 꺼졌다 — 다시 붙지 않는지 본다";
                        break;
                    }
                    fc.OutSince = -1;
                    float run = (w.Tick - fc.StartedAt) / (float)SimTime.Minutes(1);
                    if (fc.Method == "inert" && (run > 10f || InertGas < 1f))
                    {
                        room.Inerting = false;
                        if (Policy("vacuum") > 0 && CanVent(room) && Level >= 3)
                        {
                            Reason($"fire2:{room.Id}", $"{room.Name} 질식 소화로 꺼지지 않는다 ({(InertGas < 1f ? "가스가 떨어졌다" : $"{run:0}분")}) → 진공 소화로 바꾼다", SimTime.Minutes(30));
                            fc.Method = "vacuum";
                            Execute(fc, room);
                        }
                        else Release(fc, room, "질식 소화로 꺼지지 않았다 — 소화조에 맡긴다");
                        break;
                    }
                    fc.Status = fc.Method == "vacuum" ? $"진공 소화 중 — {room.Air.Pressure:0}kPa" : $"질식 소화 중 — 산소 {room.Air.O2:0.0}kPa";
                    break;
                }
                case 3: // 다시 가압 — 숨 쉴 만해지면 격벽을 푼다
                {
                    if (burning) { fc.Stage = 0; room.ResponseHold = false; room.Flushing = false; continue; }
                    bool ok = room.Air.Pressure > 88f && room.Air.O2 > 17f || w.Tick - fc.OutSince > SimTime.Hours(3);
                    fc.Status = $"다시 가압 — {room.Air.Pressure:0}kPa · 산소 {room.Air.O2:0.0}";
                    if (!ok) break;
                    room.ResponseHold = false;
                    room.Flushing = false;
                    room.EvacuateBy = -1;
                    FireCases.Remove(fc);
                    w.Log.Add(w.Tick, LogKind.Ship, $"{room.Name} 소화 대응 끝 — 숨 쉴 수 있다 · 격벽 해제");
                    break;
                }
            }
        }
    }

    private int Policy(string method) => _world.Policies[method == "vacuum" ? "vacuumfire" : "inertfire"];

    private string? PickMethod(Room room, FireCase fc)
    {
        var w = _world;
        EnsureInert();
        float need = 17f * room.Volume;
        bool inertOk = Policy("inert") > 0 && InertGas >= need * 0.6f && !fc.TriedInert;
        bool vacOk = Policy("vacuum") > 0 && CanVent(room);
        int cells = w.Fire.CountIn(room);
        // 큰 불이거나 가스가 모자라면 진공부터, 아니면 공기를 지키는 질식부터
        if (vacOk && (cells >= 8 || !inertOk)) return "vacuum";
        if (inertOk) return "inert";
        return vacOk ? "vacuum" : null;
    }

    private void Plan(FireCase fc, Room room, string method, int cells, float minutes)
    {
        var w = _world;
        fc.Method = method;
        fc.Stage = 1;
        int policy = Policy(method);
        bool countdown = policy >= 2;
        long wait = method == "vacuum" ? SimTime.Minutes(2) : SimTime.Minutes(0.5f);
        fc.ExecAt = w.Tick + wait;
        fc.PlannedAt = w.Tick;
        room.ResponseHold = true;
        room.EvacuateBy = fc.ExecAt;
        // 소화조를 물린다 — 안에 있는 사람은 나가야 한다
        foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.Extinguish && o.Target.CurrentRoom == room).ToList()) w.Board.Close(o);
        foreach (var c in w.Crew.Where(c => !c.Dead && c.Job?.Order is { Kind: WorkKind.Extinguish } eo && eo.Target.CurrentRoom == room).ToList())
        {
            c.EndJob(w, ToilStatus.Interrupted);
            c.NextThinkTick = w.Tick;
        }
        string name = method == "vacuum" ? "진공 소화" : "질식 소화";
        string rule = $"방침: {w.Policies.Option(method == "vacuum" ? "vacuumfire" : "inertfire")}";
        int fire = w.Causes.FireNodeAt(w.Fire.KnownFires().FirstOrDefault(f => f.room == room).hottest);
        using (w.Causes.Because(fire))
            fc.Node = w.Causes.Effect(CauseKind.Recovery, $"resp:{room.Id}:{method}", $"{room.Name} {name} 준비 ({rule})", room, null);
        Reason($"fire1:{room.Id}", $"{room.Name} 불 {cells}칸 · {minutes:0}분 — 소화조로 잡히지 않는다 → {name} ({(method == "vacuum" ? "공기를 바깥으로 뺀다" : "불활성 가스로 산소를 밀어낸다")}) · {rule}", SimTime.Minutes(30));
        w.RaiseAlert(countdown ? $"{room.Name} {name} — {wait / (float)SimTime.Minutes(1):0.#}분 뒤 · 모두 나가라" : $"{room.Name} {name} 준비 — 방이 비면 시작한다 · 모두 나가라", room, AlertLevel.Critical, shipWide: true);
        w.Board.RequestScan();
    }

    private void Execute(FireCase fc, Room room)
    {
        var w = _world;
        // 격벽을 닫는다 — 닫히지 않는 문이 있으면 진공은 옆방 공기까지 뺀다 (못 한다), 질식은 새도 한다
        bool sealed_ = true;
        foreach (var d in room.Doors)
        {
            if (d.IsExternal || d.Removed) continue;
            if (d.Powered && DoorsIn(room)) d.Locked = true;
            else if (d.Openness > 0.1f) sealed_ = false;
        }
        if (fc.Method == "vacuum" && !sealed_)
        {
            if (Policy("inert") > 0 && !fc.TriedInert && InertGas > 17f * room.Volume * 0.6f) fc.Method = "inert";
            else { Release(fc, room, "문이 닫히지 않아 진공 소화를 못 한다 (옆방 공기까지 빠진다)"); return; }
        }
        room.Lockdown = true;
        room.ResponseHold = true;
        room.EvacuateBy = -1;
        fc.Stage = 2;
        fc.StartedAt = w.Tick;
        fc.OutSince = -1;
        if (fc.Method == "vacuum") { room.Purging = true; room.Inerting = false; Vacuumed++; }
        else { room.Inerting = true; fc.TriedInert = true; Smothered++; }
        string name = fc.Method == "vacuum" ? "진공 소화" : "질식 소화";
        int inside = w.Crew.Count(c => !c.Dead && c.Room == room);
        if (inside > 0) _world.Hull.Trapped[room.Id] = w.Tick;
        using (w.Causes.Because(fc.Node))
            w.Causes.Effect(CauseKind.Recovery, "", $"{room.Name} {name} 시작" + (inside > 0 ? $" — 안에 {inside}명" : ""), room, null);
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터: {room.Name} {name}" + (inside > 0 ? $" — 안에 {string.Join("·", w.Crew.Where(c => !c.Dead && c.Room == room).Select(c => c.Name))}이(가) 남은 채" : ""), room, log: true);
        w.RaiseAlert($"{room.Name} {name} 시작", room, AlertLevel.Critical, shipWide: true);
    }

    private void Restore(FireCase fc, Room room)
    {
        var w = _world;
        string name = fc.Method == "vacuum" ? "진공 소화" : "질식 소화";
        room.Purging = false;
        room.Inerting = false;
        room.Flushing = fc.Method == "inert"; // 질식 소화 뒤엔 탱크 공기로 갈아 낸다
        fc.Stage = 3;
        using (w.Causes.Because(fc.Node))
            w.Causes.Effect(CauseKind.Recovery, "", $"{room.Name} {name}로 껐다 — 다시 가압", room, null);
        Reason($"fire3:{room.Id}", $"{room.Name} {name}로 불을 껐다 · 다시 가압한다 (공기 탱크 {w.Air.Reserve / w.Air.ReserveCapacity * 100:0}%)", SimTime.Minutes(30));
        w.History.Add(w, HistoryKind.Decision, $"{room.Name} 불을 {name}로 껐다", room, log: true);
    }

    private void Release(FireCase fc, Room room, string why)
    {
        var w = _world;
        room.Purging = false;
        room.Inerting = false;
        room.Flushing = false;
        room.ResponseHold = false;
        room.EvacuateBy = -1;
        fc.Stage = 0;
        fc.Method = "";
        fc.Status = why;
        Reason($"firex:{room.Id}", $"{room.Name} 소화 대응 — {why}", SimTime.Minutes(30));
        w.Board.RequestScan();
    }

    // ── 공기 구역 관리 ──

    public bool ZoneActive { get; private set; }
    public HashSet<int> Zone { get; } = new();
    public string ZoneNote { get; private set; } = "";
    public int ZonesDeclared;

    /// <summary>지킬 구역 안의 방인가 (구역이 없으면 모두).</summary>
    public bool InZone(Room r) => !ZoneActive || Zone.Contains(r.Id);

    private void AirZones()
    {
        var w = _world;
        var live = w.Ship.LiveRooms.Where(r => !r.Abandoned).ToList();
        var leaking = live.Where(r => r.Leaking || r.Air.Pressure < 70f).ToList();
        bool trouble = leaking.Count >= 2 || leaking.Count >= 1 && w.Air.Reserve < w.Air.ReserveCapacity * 0.4f;
        if (!trouble)
        {
            if (ZoneActive)
            {
                ZoneActive = false;
                Zone.Clear();
                ZoneNote = "";
                w.Log.Add(w.Tick, LogKind.Ship, "주 컴퓨터: 공기 구역 해제 — 새는 방이 없다");
            }
            return;
        }
        // 새지 않는 방끼리 문으로 이어진 덩어리 — 생명유지실이 있는 덩어리, 없으면 가장 큰 것
        var ok = live.Where(r => !leaking.Contains(r) && r.Air.Pressure >= 70f).ToHashSet();
        var groups = new List<List<Room>>();
        var seen = new HashSet<Room>();
        foreach (var start in ok)
        {
            if (!seen.Add(start)) continue;
            var g = new List<Room>();
            var q = new Queue<Room>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var r = q.Dequeue();
                g.Add(r);
                foreach (var d in r.Doors)
                {
                    if (d.IsExternal || d.Removed) continue;
                    var o = d.RoomA == r ? d.RoomB : d.RoomA;
                    if (o != null && ok.Contains(o) && seen.Add(o)) q.Enqueue(o);
                }
            }
            groups.Add(g);
        }
        var best = groups.OrderByDescending(g => g.Any(r => r.Type == RoomType.LifeSupport) ? 1 : 0).ThenByDescending(g => g.Sum(r => r.Volume)).FirstOrDefault();
        if (best == null) return;
        var ids = best.Select(r => r.Id).ToHashSet();
        bool changed = !ZoneActive || !ids.SetEquals(Zone);
        Zone.Clear();
        foreach (var id in ids) Zone.Add(id);
        ZoneActive = true;
        if (!changed) return;
        ZonesDeclared++;
        string names = string.Join("·", best.Where(r => r.Type != RoomType.Corridor).Take(5).Select(r => r.Name)) + (best.Count(r => r.Type != RoomType.Corridor) > 5 ? " …" : "");
        ZoneNote = $"지킬 구역: {names} · 새는 방 {leaking.Count}";
        Reason("zone", $"새는 방 {leaking.Count}곳 — 지킬 구역을 정한다: {names} · 사람은 이리 모이고, 공기 탱크는 이 구역부터 · 아무도 못 막는 바깥 방은 {w.Policies.Option("zoneabandon")} 포기 (방침)", SimTime.Minutes(20));
        w.RaiseAlert($"공기 구역 — {names}로 모여라", null, AlertLevel.Warning, shipWide: true);
    }

    /// <summary>시스템 틱마다 (Think 안에서): 주 컴퓨터가 돌면 모듈을 돌린다.</summary>
    internal void Respond(float dt)
    {
        if (!MainOnline) { ZoneActive = false; return; }
        EnsureInert();
        if (Has(ComputerModule.FireResponse) && Level >= 3) FireResponse();
        if (Has(ComputerModule.AirZones) && Level >= 3) AirZones();
    }
}
