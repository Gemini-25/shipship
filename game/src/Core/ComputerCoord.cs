using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v19 계획 조율: 여러 계획이 같은 사람 · 부품 · 통로 · 전력 · 배터리를 바랄 때 — 조율판 한 장에 모아 급한 순서로 나눈다.
//  선행 조건: 앞 일이 끝나야 뒤 일 — 감출력은 낮춘 출력으로 수술 · 생명유지를 버티는지 본 뒤에 (모자라면 배터리로 메우거나 수술이 끝날 때까지 기다린다) ·
//             통로를 닫는 일(격벽 용접 · 방 봉쇄 · 문 수리)은 부상자 이송이 지나간 뒤에.
//  예약 · 반환: 잡은 부품 · 배터리는 일이 끝나거나 접히면 돌아가고, 양보했던 일이 바로 다시 연다.
//  우선 사용: 더 급한 일이 덜 급한 일의 사람 · 부품 · 배터리를 먼저 쓴다 (밀린 일은 까닭을 남기고 기다린다) —
//             원자로가 멎은 동안 배터리는 생명유지 → 재기동 몫 → 급한 일 하는 드론 순 · 남지 않으면 덜 급한 드론은 불러들여 세워 둔다.
//  인계: 더 급한 일로 불려 간 사람은 하던 일에 어디까지 했는지 메모를 남긴다 — 이어받는 사람이 더듬지 않는다.
//  2분마다 (계획 갱신 뒤) · 결정이 바뀔 때만 말한다.

public sealed class CoordClaim
{
    /// <summary>사람 · 부품 · 통로 · 전력 · 배터리.</summary>
    public string Kind { get; init; } = "";
    public string What { get; init; } = "";
    /// <summary>어느 계획 · 일이 잡았나.</summary>
    public string Owner { get; init; } = "";
    public float Pri { get; init; }
    public float Amount { get; init; }
    public string Unit { get; init; } = "";
    /// <summary>잡음 · 기다림 · 밀림.</summary>
    public string State { get; set; } = "잡음";
    public string Why { get; set; } = "";
}

public sealed class PlanCoordinator
{
    private readonly World _w;
    private long _next;
    public List<CoordClaim> Claims { get; } = new();
    public List<(long tick, string text)> Decisions { get; } = new();
    private readonly HashSet<int> _noCharge = new();
    private readonly HashSet<int> _recalled = new();
    private readonly Dictionary<int, string> _handover = new();
    private readonly HashSet<int> _passBlocked = new();
    private readonly Dictionary<int, long> _preempted = new();
    private string _batteryKey = "";
    private string _derateKey = "";
    public string Line { get; private set; } = "";
    public int Preempts, HandoverUsed, PartWaits, PartReturns, PassageWaits, ChargeHolds, Recalls, DerateWaits, DerateCovered, DerateOk;

    public PlanCoordinator(World w) => _w = w;

    /// <summary>드론 충전을 미뤘나 (원자로가 멎은 동안 배터리를 재기동 · 생명유지에 남긴다).</summary>
    public bool HoldCharge(Drone d) => _noCharge.Contains(d.Id);

    /// <summary>인계 메모를 꺼낸다 (이어받는 사람이 읽고 지운다).</summary>
    public string? TakeHandover(int orderId)
    {
        if (!_handover.Remove(orderId, out var note)) return null;
        HandoverUsed++;
        return note;
    }

    public string? Handover(int orderId) => _handover.TryGetValue(orderId, out var n) ? n : null;

    private void Decide(string key, string text)
    {
        var w = _w;
        Decisions.Add((w.Tick, text));
        if (Decisions.Count > 30) Decisions.RemoveAt(0);
        w.Automation.Reason(key, text, SimTime.Minutes(30));
    }

    public void Update()
    {
        var w = _w;
        var a = w.Automation;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(2);
        Claims.Clear();
        if (!a.CoreOnline) { Line = ""; return; }
        Battery();
        Parts();
        People();
        Passages();
        Handovers();
        var waiting = Claims.Where(c => c.State != "잡음").ToList();
        Line = waiting.Count == 0 ? "" : $"조율 — {string.Join(" · ", waiting.Take(2).Select(c => $"{c.What}: {c.Why}"))}";
    }

    // ───────────────────────────── 배터리: 생명유지 → 재기동 → 드론 ─────────────────────────────

    private void Battery()
    {
        var w = _w;
        var a = w.Automation;
        var p = w.Power;
        if (p.ReactorOnline || p.BatteryCapacity <= 0f)
        {
            if (_noCharge.Count > 0) Decide("coord:battery", "원자로가 다시 돈다 — 미뤘던 드론 충전을 푼다");
            _noCharge.Clear();
            _recalled.Clear();
            _batteryKey = "";
            return;
        }
        float have = p.BatteryCharge;
        float essential = 0f;
        foreach (var m in w.Ship.Machines) if (m.Powered && m.Spec.PowerDraw > 0f && (PowerTriage.Rank(w, m) >= 9 || w.Surgery.Holds(m))) essential += m.Demand;
        float life = MathF.Max(1f, essential); // 한 시간 몫
        float restart = a.Reserve.KeepKwh;
        Claims.Add(new CoordClaim { Kind = "배터리", What = "생명유지 · 수술 한 시간", Owner = "배", Pri = 1f, Amount = life, Unit = "kWh" });
        if (restart > 0f) Claims.Add(new CoordClaim { Kind = "배터리", What = "원자로 재기동 몫", Owner = "복구 계획", Pri = 0.9f, Amount = restart, Unit = "kWh" });
        float left = have - life - restart;
        var drones = new List<(Drone d, float need, float pri)>();
        foreach (var d in w.Drones.Drones)
        {
            if (!d.Operational || d.State == DroneState.Docked && d.Battery >= 0.95f) continue;
            float kw = d.Dock.Machine?.Spec.PowerDraw ?? 1.5f;
            float need = MathF.Max(0.1f, (1f - d.Battery) / DroneSystem.ChargePerHour * MathF.Max(0.5f, kw));
            float pri = d.Order is { Urgency: >= 0.8f } ? 0.8f : d.State != DroneState.Docked ? 0.45f : 0.3f;
            drones.Add((d, need, pri));
        }
        drones.Sort((x, y) => y.pri != x.pri ? y.pri.CompareTo(x.pri) : x.d.Id.CompareTo(y.d.Id));
        int granted = 0;
        var held = new List<string>();
        foreach (var (d, need, pri) in drones)
        {
            var claim = new CoordClaim { Kind = "배터리", What = $"{d.Name} 충전", Owner = d.Order?.Title ?? "드론", Pri = pri, Amount = need, Unit = "kWh" };
            Claims.Add(claim);
            if (left >= need) { left -= need; granted++; _noCharge.Remove(d.Id); continue; }
            claim.State = "밀림";
            claim.Why = "재기동 · 생명유지 몫이 먼저";
            if (_noCharge.Add(d.Id)) ChargeHolds++;
            held.Add(d.Name);
            // 덜 급한 일로 나가 있으면 일찍 불러들인다 (돌아와도 충전을 못 하니 멀리 가 있을 까닭이 없다)
            if (pri < 0.8f && d.State is DroneState.Outbound or DroneState.Working && _recalled.Add(d.Id))
            {
                w.Drones.Recall(d, "배터리를 재기동 몫으로 남기려 불러들인다");
                Recalls++;
            }
        }
        string key = $"{granted}/{drones.Count}";
        if (drones.Count > 0 && key != _batteryKey)
        {
            _batteryKey = key;
            Decide("coord:battery", $"배터리 {have:0}kWh 나눔 — 생명유지 · 수술 {life:0} · 재기동 {restart:0} · 드론 충전 {granted}/{drones.Count}대"
                + (held.Count > 0 ? $" ({string.Join("·", held)}는 돌아와 세워 둔다 — 원자로가 돌면 채운다)" : ""));
        }
    }

    // ───────────────────────────── 감출력: 수술 · 생명유지를 버티나 (선행 조건) ─────────────────────────────

    /// <summary>계획의 감출력 걸음이 부른다: null = 낮춰도 된다 · 아니면 기다리는 까닭.</summary>
    public string? DerateWait(float cap)
    {
        var w = _w;
        var a = w.Automation;
        var p = w.Power;
        bool surgery = w.Surgery.Cases.Any(k => k.State is CaseState.Prep or CaseState.Operating);
        if (!surgery && !a.Reserve.MedHold || !p.ReactorOnline) return null;
        float protect = 0f;
        foreach (var m in w.Ship.Machines)
            if (m.Powered && m.Spec.PowerDraw > 0f && (w.Surgery.Holds(m) || a.Reserve.Holds(m) || PowerTriage.Rank(w, m) >= 9)) protect += m.Demand; // 지금 쓰는 만큼
        float now = MathF.Max(0.05f, a.ReactorCap);
        float supply = p.ReactorLimit / now * MathF.Min(now, cap);
        string what = surgery ? "수술 · 생명유지" : "의무실 · 생명유지";
        string key;
        if (supply >= protect)
        {
            key = "ok";
            if (key != _derateKey) { DerateOk++; Decide("coord:derate", $"출력을 {cap * 100:0}%로 낮춰도 {supply:0}kW — {what} {protect:0}kW는 버틴다 (나머지 설비는 차례로 내린다)"); }
            _derateKey = key;
            return null;
        }
        float gap = protect - supply;
        float spare = p.BatteryCharge - a.Reserve.KeepKwh;
        if (spare >= gap * 1.5f)
        {
            key = "battery";
            if (key != _derateKey) { DerateCovered++; Decide("coord:derate", $"출력을 {cap * 100:0}%로 낮추면 {what}에 {gap:0.#}kW가 모자란다 — 배터리 {gap * 1.5f:0}kWh로 메우며 고친다 (남는 {spare:0}kWh)"); }
            _derateKey = key;
            return null;
        }
        if (p.ReactorTemperature > PowerGrid.OverheatScramC - 30f) { _derateKey = "hot"; return null; } // 노심이 먼저 — 기다릴 수 없다 (수술 기기는 몰아주기가 맨 먼저 지킨다)
        key = "wait";
        if (key != _derateKey) { DerateWaits++; Decide("coord:derate", $"{what} {protect:0}kW — 출력을 {cap * 100:0}%로 낮추면 {supply:0}kW · 배터리 여유도 {spare:0}kWh뿐 → 수술이 끝날 때까지 감출력을 미룬다"); }
        _derateKey = key;
        Claims.Add(new CoordClaim { Kind = "전력", What = "감출력", Owner = "냉각 복구", Pri = 0.7f, Amount = gap, Unit = "kW", State = "기다림", Why = "수술이 끝난 뒤" });
        return $"수술이 끝날 때까지 — 낮춘 출력 {supply:0}kW로는 {what} {protect:0}kW를 못 버틴다";
    }

    // ───────────────────────────── 부품: 모자라면 급한 수리부터 · 돌아오면 다시 ─────────────────────────────

    private readonly Dictionary<int, ItemKind> _partDenied = new();

    private static bool Needs(WorkOrder x, ItemKind kind)
    {
        if (x.Closed || x.Kind != WorkKind.Repair || x.Furniture?.Machine is not Machine m) return false;
        foreach (var f in m.Faults) foreach (var (k, _) in f.Materials) if (k == kind) return true;
        return false;
    }

    /// <summary>o보다 급하고 같은 부품을 바라는, 지금 쓸 수 있는 수리 (같은 설비 · 막힌 일은 빼고).</summary>
    private int Higher(WorkOrder o, ItemKind kind, out WorkOrder? top)
    {
        var w = _w;
        top = null;
        int n = 0;
        foreach (var x in w.Board.Open)
        {
            if (x == o || x.Closed || x.Furniture == o.Furniture || x.Urgency <= o.Urgency + 0.05f || !Needs(x, kind)) continue;
            if (x.Assignee == null && x.BlockedUntil > w.Tick) continue; // 다른 까닭으로 막힌 일은 부품을 쓰지 못한다 — 몫을 잡아 두면 둘 다 멈춘다
            n++;
            if (top == null || x.Urgency > top.Urgency) top = x;
        }
        return n;
    }

    /// <summary>주컴퓨터 복구 계획이 이끄는 수리 (계획이 이미 차례를 정했다).</summary>
    private bool Planned(WorkOrder o) => _w.Automation.Recovery.Open.Any(p => p.Steps.Any(st => st.Order == o.Id));

    /// <summary>수리 계획이 부품을 꺼내기 전에 묻는다: 남은 부품이 더 급한 수리 몫이면 까닭 (부분 수리로 버티거나 기다린다) · 아니면 null.
    /// 핵심 설비 · 복구 계획이 이끄는 수리는 막지 않는다 (냉각 펌프 열 대가 한꺼번에 서자 계획의 펌프 수리까지 서로 몫을 미루다 원자로가 멎었다).</summary>
    public string? PartHeldFor(WorkOrder o, ItemKind kind)
    {
        var w = _w;
        if (!w.Automation.CoreOnline) return null;
        if (o.Furniture?.Machine?.Spec.Critical == true || Planned(o)) return null;
        int stock = w.Ship.CountStored(kind);
        int higher = Higher(o, kind, out var top);
        if (higher == 0 || higher < stock) return null;
        string why = $"{ItemKinds.Name(kind)} {stock}개 — {top!.Title}(급함 {top.Urgency:0.##}) 몫";
        if (!_partDenied.ContainsKey(o.Id))
        {
            PartWaits++;
            Decide($"coord:part:{o.Id}", $"{ItemKinds.Name(kind)} {stock}개를 {Ko.IGa(o.Title)}(급함 {o.Urgency:0.##}) 바라지만 {top.Title}에 먼저 — 돌아오면 다시 연다");
        }
        _partDenied[o.Id] = kind;
        return why;
    }

    private void Parts()
    {
        var w = _w;
        foreach (var (id, kind) in _partDenied.ToList())
        {
            var o = w.Board.Open.FirstOrDefault(x => x.Id == id && !x.Closed);
            if (o == null || !Needs(o, kind)) { _partDenied.Remove(id); continue; }
            int stock = w.Ship.CountStored(kind);
            int higher = Higher(o, kind, out _);
            Claims.Add(new CoordClaim { Kind = "부품", What = ItemKinds.Name(kind), Owner = o.Title, Pri = o.Urgency, Amount = 1, Unit = "개", State = higher >= stock || stock == 0 ? "기다림" : "잡음", Why = higher > 0 ? "더 급한 수리 몫" : "" });
            if (higher > 0 && higher >= stock || stock == 0) continue;
            // 더 급한 수리가 끝났거나 접혔다 — 부품이 돌아왔다
            _partDenied.Remove(id);
            PartReturns++;
            if (o.BlockedReason?.Contains("몫") == true) { o.BlockedUntil = 0; o.BlockedReason = null; }
            Decide($"coord:part:{o.Id}", $"{ItemKinds.Name(kind)}이 돌아왔다 ({stock}개) — 미뤘던 {Ko.EulReul(o.Title)} 다시 연다");
            w.Board.RequestScan();
        }
    }

    // ───────────────────────────── 사람: 더 급한 일에 맞는 손이 없으면 덜 급한 일에서 부른다 (인계 메모) ─────────────────────────────
    //   다친 사람을 돌보던 손(치료 · 구조 · 재활)은 부르지 않는다 — 환자를 두고 가면 덜 서툰 손이 이어받는다

    private void People()
    {
        var w = _w;
        var a = w.Automation;
        if (!a.MainOnline) return;
        foreach (var o in w.Board.Open.Where(o => !o.Closed && o.Assignee == null && o.Robot == null && o.Drone == null && o.Urgency >= 0.85f && o.BlockedUntil <= w.Tick && !o.External && w.Tick - o.Posted > SimTime.Minutes(6))
                     .OrderByDescending(o => o.Urgency).ThenBy(o => o.Id).Take(2).ToList())
        {
            float skill = MathF.Max(o.MinSkill, 0.45f);
            bool Fit(CrewMember c) => c.CanAct && c.IsAwake && !c.Outside && !c.IsChild && c.Suit == null && c.SkillLevel(o.Skill) >= skill;
            if (w.Crew.Any(c => Fit(c) && c.Job?.Order == null && c.Job?.Urgent != true)) continue; // 손이 빈 사람이 있다 — 게시판이 맡긴다
            var cand = w.Crew.Where(c => Fit(c) && c.Job?.Order is WorkOrder cur && cur != o && !cur.Closed && cur.Urgency < o.Urgency - 0.3f && c.Job.Urgent != true
                                         && cur.Kind is not (WorkKind.Treat or WorkKind.Rescue or WorkKind.Rehab)
                                         && !(_preempted.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(1)))
                .OrderByDescending(c => c.SkillLevel(o.Skill)).ThenBy(c => c.Id).FirstOrDefault();
            if (cand == null) continue;
            var cur = cand.Job!.Order!;
            int pct = (int)(cur.Progress * 100f);
            string note = $"{cand.Name}: {cur.Title} {pct}%까지 했다 — 펼친 공구 · 뗀 부품은 그 자리에 · 남은 건 {100 - pct}%";
            _handover[cur.Id] = note;
            _preempted[cand.Id] = w.Tick;
            Preempts++;
            Claims.Add(new CoordClaim { Kind = "사람", What = cand.Name, Owner = o.Title, Pri = o.Urgency, Amount = 1, Unit = "명", State = "잡음", Why = $"{cur.Title}에서 불러옴" });
            Claims.Add(new CoordClaim { Kind = "사람", What = cand.Name, Owner = cur.Title, Pri = cur.Urgency, Amount = 1, Unit = "명", State = "밀림", Why = $"{o.Title}이 더 급하다 — 인계 메모" });
            a.CrewModel.Ask(cand, o, $"{o.Title} — 지금 맞는 손이 당신뿐입니다 · 하던 {cur.Title}은 메모를 남겨 두세요", 2f, crisis: true);
            cand.Say(w, Persona.Say(cand, $"알았어 — {cur.Title}은 메모 남겨 둘게"));
            w.Log.Add(w.Tick, LogKind.Work, $"인계 메모 — {note}", cand.Id);
            cand.Interrupt(w);
            Decide($"coord:pre:{o.Id}", $"{o.Title}에 {Skills.Name(o.Skill)} 손이 필요한데 빈 사람이 없다 — {Ko.EulReul(cand.Name)} {cur.Title}(급함 {cur.Urgency:0.##})에서 부른다 (급함 {o.Urgency:0.##}) · 하던 일은 인계 메모로 넘긴다");
        }
    }

    // ───────────────────────────── 인계: 하던 일을 두고 더 급한 일로 간 사람은 메모를 남긴다 ─────────────────────────────

    private readonly Dictionary<int, (int crew, float progress)> _working = new();
    public int Notes;

    private void Handovers()
    {
        var w = _w;
        var seen = new HashSet<int>();
        foreach (var o in w.Board.Open)
        {
            if (o.Closed) continue;
            if (o.Assignee is CrewMember c) { _working[o.Id] = (c.Id, o.Progress); seen.Add(o.Id); continue; }
            if (!_working.Remove(o.Id, out var was) || _handover.ContainsKey(o.Id)) continue;
            if (o.Progress < 0.05f || o.Progress > 0.95f || was.crew < 0 || was.crew >= w.Crew.Count) continue;
            var who = w.Crew[was.crew];
            if (who.Dead) continue;
            // 더 급한 일로 불려 간 사람만 적어 두고 간다 — 끼니 · 잠 · 쓰러짐으로 자리를 비우면 메모가 없다 (이어받는 사람이 예전처럼 더듬는다)
            //   (모든 자리 비움에 메모를 남기자 배 전체에서 남의 작업 잇기가 빨라져 배운 사람 · 서툰 사람의 차이가 흐려졌다)
            if (who.Job?.Order is not WorkOrder more || more == o || more.Urgency <= o.Urgency + 0.2f) continue;
            int pct = (int)(o.Progress * 100f);
            _handover[o.Id] = $"{who.Name}({more.Title}로 갔다): {o.Title} {pct}%까지 — 펼친 공구 · 뗀 부품은 그 자리에 · 남은 건 {100 - pct}%";
            Notes++;
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(o.Title)} 두고 {more.Title}로 간다 — 인계 메모를 남겼다 ({pct}%)", who.Id);
        }
        foreach (var id in _working.Keys.ToList()) if (!seen.Contains(id) && !w.Board.Open.Any(o => o.Id == id && !o.Closed)) _working.Remove(id);
        foreach (var id in _handover.Keys.ToList()) if (!w.Board.Open.Any(o => o.Id == id && !o.Closed)) _handover.Remove(id);
    }

    // ───────────────────────────── 통로: 부상자 이송이 지나간 뒤에 닫는다 ─────────────────────────────

    private static bool ClosesPassage(WorkKind k) => k is WorkKind.WeldBulkhead or WorkKind.SealOffRoom or WorkKind.RepairDoor;

    private void Passages()
    {
        var w = _w;
        var carriers = w.Crew.Where(c => !c.Dead && c.CarryingPerson != null && c.Room != null).ToList();
        if (carriers.Count == 0) { _passBlocked.Clear(); return; }
        var medbay = w.Ship.RoomsOf(RoomType.Medbay).FirstOrDefault(r => !r.Detached);
        var route = new HashSet<int>();
        foreach (var c in carriers) foreach (var id in Route(c.Room!, medbay)) route.Add(id);
        foreach (var o in w.Board.Open)
        {
            if (o.Closed || o.Assignee != null || o.Robot != null || !ClosesPassage(o.Kind)) continue;
            bool on = o.Target.Door is Door d ? d.RoomA != null && route.Contains(d.RoomA.Id) || d.RoomB != null && route.Contains(d.RoomB.Id) : o.Target.Room is Room r && route.Contains(r.Id);
            if (!on) continue;
            if (o.Kind == WorkKind.SealOffRoom && o.Target.Room is Room sr && sr.Air.Pressure < 50f) continue; // 공기가 다 빠져 가면 기다릴 수 없다
            w.Board.Block(o, "부상자 이송이 지나간 뒤에 닫는다", 0.1f);
            Claims.Add(new CoordClaim { Kind = "통로", What = o.Target.Room?.Name ?? o.Title, Owner = "부상자 이송", Pri = 0.95f, Amount = 1, Unit = "곳" });
            Claims.Add(new CoordClaim { Kind = "통로", What = o.Target.Room?.Name ?? o.Title, Owner = o.Title, Pri = o.Urgency, Amount = 1, Unit = "곳", State = "기다림", Why = "이송이 지나간 뒤" });
            if (_passBlocked.Add(o.Id))
            {
                PassageWaits++;
                Decide($"coord:pass:{o.Id}", $"{carriers[0].Name}이 부상자를 업고 {(medbay?.Name ?? "의무실")}로 가는 길 — {Ko.EulReul(o.Title)} 지나간 뒤에 한다");
            }
        }
    }

    /// <summary>방 하나에서 의무실까지 문으로 이어진 가장 짧은 방 줄 (방 번호).</summary>
    private List<int> Route(Room from, Room? to)
    {
        var w = _w;
        var res = new List<int> { from.Id };
        if (to == null || to == from) return res;
        var prev = new Dictionary<int, int> { [from.Id] = -1 };
        var q = new Queue<Room>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var r = q.Dequeue();
            if (r == to) break;
            foreach (var (n, door) in w.Ambience.Neighbors(r))
                if (door && !n.Detached && prev.TryAdd(n.Id, r.Id)) q.Enqueue(n);
        }
        if (!prev.ContainsKey(to.Id)) return res;
        res.Clear();
        for (int id = to.Id; id >= 0; id = prev[id]) res.Add(id);
        return res;
    }
}

public sealed partial class AutomationSystem
{
    private PlanCoordinator? _coord;
    /// <summary>v19 계획 조율: 사람 · 부품 · 통로 · 전력 · 배터리를 여러 계획 사이에 나눈다.</summary>
    public PlanCoordinator Coord => _coord ??= new PlanCoordinator(_world);
    internal PlanCoordinator? CoordOrNull => _coord;
}
