using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.20 ④ 미리 돌려 보고 고른다 — 사고 때만, 세계를 복제하지 않는 가벼운 모형으로 조치 후보 2~3개를 몇 분 앞까지 견준다.
//  파공: 지금 봉쇄 / 2분 기다려 대피 후 봉쇄 / 사람 보내 막기 — 새는 속도(구멍 넓이 · 부피) · 사람마다 문까지 걸음 · 옆방 부피 · 실링폼 · 막을 사람의 걸음.
//  불: 소화조에 더 맡김 / 질식 소화 / 진공 소화 — 불 칸 · 번지는 속도 · 가스 · 배기 밸브 · 안에 있다고 믿는 사람.
//  정전: 배터리로 버팀 / 비필수 몰아주기 / 보조 발전기 원격 기동 + 몰아주기 (ComputerTriage가 묻는다).
//  점수 = 사람 무게 × 위험한 사람(기대값) + 배 무게 × 배 손실 — 무게는 방침(감압 격벽) × 컴퓨터 성격(사람/배 기울기 · 신중/과감).
//  정확도가 낮으면 예측에 잡음이 낀다 (가끔 틀린 안을 고른다). 방침 밖의 안은 견주기만 하고 고르지 않는다.
//  고른 안 · 견준 안 · 이유를 타임라인에 남기고, 몇 분 뒤 실제 결과(쓰러진 사람 · 기압 · 불)로 채점한다 → 성격이 자란다.
//  겹친 사고: 위험한 사람 수 × 위험까지 남은 시간으로 순서를 매기고, 동시 처리 수(품질)를 넘는 사고는 기다린다.

public sealed class ForeseeOption
{
    public string Name { get; init; } = "";
    public string Key { get; init; } = "";
    /// <summary>위험한 사람 (기대값 · 명).</summary>
    public float People { get; set; }
    /// <summary>배 손실 (공기 · 설비 · 전력 — 배 전체 대비 정규화).</summary>
    public float Ship { get; set; }
    /// <summary>끝날 때까지 (분).</summary>
    public float Minutes { get; set; }
    public string Note { get; set; } = "";
    public bool Allowed { get; set; } = true;
    public string Blocked { get; set; } = "";
    public float Score { get; set; }
}

public sealed class ForeseeDecision
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public string Kind { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public string Title { get; init; } = "";
    public List<ForeseeOption> Options { get; } = new();
    public int Chosen { get; set; }
    public string Reason { get; set; } = "";
    public float PeopleW { get; init; }
    public float ShipW { get; init; }
    /// <summary>겹친 사고 중 몇 번째였나 (1부터 · 0 혼자).</summary>
    public int Rank { get; init; }
    public string By { get; init; } = "";
    public string Result { get; set; } = "";
    /// <summary>0 아직 · 1 맞았다 · −1 틀렸다 · 2 보류.</summary>
    public int Score { get; set; }
    public long GradeAt { get; set; }
    public int DownBefore { get; init; }
    public float PressureBefore { get; init; }
    internal Func<World, ForeseeDecision, (int, string)?>? Grader { get; set; }
    public ForeseeOption Pick => Options[Math.Clamp(Chosen, 0, Options.Count - 1)];
}

/// <summary>겹친 사고 한 건 (순서표).</summary>
public sealed class IncidentRank
{
    public string Key { get; init; } = "";
    public string Kind { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public float People { get; init; }
    public float Minutes { get; init; }
    public float Score { get; init; }
    public int Order { get; set; }
    public bool Attended { get; set; }
}

/// <summary>파공 대응 결과 (Hull이 따른다).</summary>
public sealed record BreachPlan(string Key, long WaitTicks, CrewMember? Patcher, ForeseeDecision Decision);

public sealed class ComputerForesee
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 401));
    private int _next = 1;
    public List<ForeseeDecision> Timeline { get; } = new();
    public List<IncidentRank> Queue { get; } = new();
    public int Decisions, Right, Wrong, Deferred, Overlaps;
    public string QueueLine { get; private set; } = "";
    private string _queueSig = "";
    private readonly Dictionary<int, (long until, int crewId, int decision)> _holdOpen = new();
    private long _gradeNext;

    public ComputerForesee(World w) => _w = w;

    /// <summary>연산 부하 (단위): 최근 10분 안에 돌린 예측 + 순서표.</summary>
    public float Load
    {
        get
        {
            int recent = 0;
            for (int i = Timeline.Count - 1; i >= 0 && _w.Tick - Timeline[i].Tick < SimTime.Minutes(10); i--) recent++;
            return 0.6f * recent + 0.3f * Queue.Count;
        }
    }

    public string? NowLine
    {
        get
        {
            var d = Timeline.LastOrDefault();
            if (d == null || _w.Tick - d.Tick > SimTime.Minutes(4)) return null;
            return $"{d.Title} — {d.Options.Count}가지를 견줘 봤다: {d.Pick.Name} · {d.Reason}";
        }
    }

    // ───────────── 무게 (방침 × 성격) ─────────────

    /// <summary>사람 무게 · 배 무게: 방침(감압 격벽 — 배 우선이면 배)에 성격 기울기가 얹힌다.</summary>
    public (float people, float ship) Weights()
    {
        var a = _w.Automation;
        var ch = a.Character;
        float p = a.ShipFirst ? 1.5f : 10f, s = a.ShipFirst ? 6f : 1.5f;
        p *= 1f + 0.35f * ch.PeopleTilt;
        s *= 1f - 0.3f * ch.PeopleTilt;
        return (p, s);
    }

    /// <summary>예측 잡음 (정확도가 낮을수록 크게 · 결정론).</summary>
    private float Noise() => 1f + (R.Float() * 2f - 1f) * (1f - _w.Automation.Core.Accuracy) * 0.6f;

    private float ShipVolume()
    {
        float v = 0f;
        foreach (var r in _w.Ship.LiveRooms) v += r.Volume;
        return MathF.Max(1f, v);
    }

    /// <summary>문으로 이어진 옆방들 (밖 · 떨어진 방 · 이미 잠긴 문 빼고).</summary>
    private List<Room> Neighbors(Room room)
    {
        var list = new List<Room>();
        foreach (var d in room.Doors)
        {
            if (d.IsExternal || d.Removed || d.Locked || d.Welded) continue;
            var o = d.RoomA == room ? d.RoomB : d.RoomA;
            if (o != null && o != room && !o.Detached && !list.Contains(o)) list.Add(o);
        }
        return list;
    }

    /// <summary>그 사람이 방에서 나가는 데 (분): 가장 가까운 안쪽 문까지 걸음 (다치면 느리다 · 쓰러지면 못 나간다).</summary>
    private float ExitMinutes(CrewMember c, Room room)
    {
        if (c.Down || c.Dead || !c.CanAct) return 99f;
        float best = 99f;
        foreach (var d in room.Doors)
        {
            if (d.IsExternal || d.Removed || d.Welded) continue;
            float dist = MathF.Abs(d.Cell.X - c.Cell.X) + MathF.Abs(d.Cell.Y - c.Cell.Y);
            best = MathF.Min(best, dist / 60f);
        }
        if (c.Pose == Pose.Sleeping) best += 0.5f; // 깨서 나가는 틈
        return best * (1f + c.Vitals.Injury) * Noise() + 0.15f;
    }

    /// <summary>기압이 위험(60kPa)에 닿을 때까지 (분): p(t) = p0 · e^(−새는 양 · t / 부피).</summary>
    private static float DangerMinutes(float p0, float leak, float volume, float danger = 60f)
    {
        if (leak <= 0f || p0 <= danger) return p0 <= danger ? 0f : 999f;
        return volume / leak * MathF.Log(p0 / danger) * 60f;
    }

    private static float PressureAfter(float p0, float leak, float volume, float minutes) => p0 * MathF.Exp(-leak * minutes / 60f / MathF.Max(1f, volume));

    // ───────────── 파공 ─────────────

    /// <summary>파공(감압) — 안에 사람이 있을 때: 세 안을 견주고 고른다. 주 코어가 없으면 null (예비 코어는 방침대로만).</summary>
    public BreachPlan? Breach(Room room, List<CrewMember> inside)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.MainOnline || a.Level < 4 || AutomationSystem.Ship20Off) return null;
        var (pw, sw) = Weights();
        var ch = a.Character;
        float leak = MathF.Max(1f, room.Air.Leak);
        float p0 = room.Air.Pressure;
        var nb = Neighbors(room);
        float vn = 0f; int nbPeople = 0;
        foreach (var o in nb) { vn += o.Volume; nbPeople += BeliefSystemPeople(o); }
        float vc = room.Volume + vn;
        float shipV = ShipVolume();
        int unsuited = 0;
        var exits = new List<float>();
        foreach (var c in inside) { if (c.Suit is { Oxygen: > 0.1f }) continue; unsuited++; exits.Add(ExitMinutes(c, room)); }
        // A 지금 봉쇄: 안 사람은 갇힌다 — 그 방 공기만 잃는다
        float tA = DangerMinutes(p0, leak, room.Volume);
        var A = new ForeseeOption { Key = "seal", Name = "지금 닫기", Minutes = 0f };
        A.People = 0f;
        foreach (var t in exits) A.People += t < 0.05f ? 0f : tA < 5f ? 0.85f : 0.55f; // 갇히면 구조 전에 쓰러진다 (늦게 빠지면 버틸 틈이 있다)
        A.Ship = room.Volume * p0 / (101f * shipV) * 10f;
        A.Note = $"안 {unsuited}명 갇힘 · {tA:0.#}분이면 60kPa";
        // B 2분 기다려 대피 후 봉쇄: 다 나오면 일찍 닫는다 — 그동안 옆방 공기까지 빠진다
        float waitMin = MathF.Min(2f, exits.Count > 0 ? exits.Max() + 0.2f : 0.3f);
        float tB = DangerMinutes(p0, leak, vc);
        var B = new ForeseeOption { Key = "wait", Name = "2분 기다렸다 닫기", Minutes = waitMin };
        foreach (var t in exits) B.People += t <= MathF.Min(waitMin, tB) ? 0.02f : 0.85f;
        float pB = PressureAfter(p0, leak, vc, waitMin);
        if (pB < 70f) B.People += 0.1f * nbPeople; // 옆방 사람도 숨이 가빠진다
        B.Ship = (room.Volume * p0 + vn * (p0 - pB)) / (101f * shipV) * 10f;
        B.Note = $"{waitMin:0.#}분 · 옆방 {nb.Count}곳 {pB:0}kPa까지";
        // C 사람 보내 막기: 문을 열어 둔 채 실링폼을 든 사람이 가서 막는다 (안 사람은 빠져나온다)
        var C = new ForeseeOption { Key = "patch", Name = "사람 보내 막기" };
        CrewMember? patcher = null;
        int sealant = w.Ship.CountStored(ItemKind.Sealant);
        if (sealant <= 0) { C.Allowed = false; C.Blocked = "실링폼이 없다"; C.People = 9f; C.Ship = 9f; }
        else
        {
            float bestEta = 99f;
            foreach (var c in w.Crew)
            {
                if (c.Dead || !c.CanAct || c.Outside || c.Away || c.Room == null || c.IsChild) continue;
                float d = MathF.Abs(c.Cell.X - room.Center.X) + MathF.Abs(c.Cell.Y - room.Center.Y);
                float eta = d / 60f + (c.Pose == Pose.Sleeping ? 1f : 0f) + 4f * (1.2f - c.SkillLevel(Skill.Mechanics));
                if (eta < bestEta || eta == bestEta && patcher != null && c.Id < patcher.Id) { bestEta = eta; patcher = c; }
            }
            if (patcher == null) { C.Allowed = false; C.Blocked = "보낼 사람이 없다"; C.People = 9f; C.Ship = 9f; }
            else
            {
                bestEta *= Noise() * (1f + 0.4f * MathF.Max(0f, ch.Caution)); // 신중하면 손이 늦을 것까지 본다
                C.Minutes = bestEta;
                float pC = PressureAfter(p0, leak, vc, bestEta);
                C.People = (pC < 60f ? 0.5f + 0.25f * nbPeople : pC < 75f ? 0.15f : 0.03f) + (patcher.Suit is { Oxygen: > 0.1f } ? 0f : pC < 40f ? 0.5f : 0f);
                foreach (var t in exits) C.People += t <= MathF.Min(bestEta, DangerMinutes(p0, leak, vc)) ? 0.02f : 0.6f;
                C.Ship = vc * (p0 - pC) / (101f * shipV) * 10f * 0.6f; // 막고 나면 방을 잃지 않는다
                C.Note = $"{patcher.Name} {bestEta:0.#}분 · 그때 {pC:0}kPa";
            }
        }
        // 방침: 배 우선이면 바로 닫는다 (기다림 · 열어 둠은 견주기만)
        if (a.ShipFirst) { B.Allowed = false; B.Blocked = "회의가 정한 대로 바로 닫는다"; C.Allowed &= false; if (C.Blocked == "") C.Blocked = "회의가 정한 대로 바로 닫는다"; }
        var options = new List<ForeseeOption> { A, B, C };
        var dec = Choose("파공", room, $"{room.Name} 구멍 · 안에 {inside.Count}명", options, pw, sw);
        var pick = dec.Pick;
        Room? exec = room;
        if (pick.Key == "wait")
            return new BreachPlan("wait", SimTime.Minutes(MathF.Max(0.5f, pick.Minutes + 0.2f)), null, dec);
        if (pick.Key == "patch" && patcher != null)
        {
            long until = w.Tick + SimTime.Minutes(pick.Minutes + 1.5f);
            _holdOpen[room.Id] = (until, patcher.Id, dec.Id);
            if (w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.SealBreach && o.Target.CurrentRoom == room) is WorkOrder seal)
                a.Command.Assign(patcher, seal, $"{room.Name} 구멍 — 문은 열어 둡니다 · {pick.Minutes:0.#}분 안에 실링폼으로 막아 주십시오 (못 막으면 닫습니다)", crisis: true, decision: dec.Id);
            else
            {
                a.Command.Line(CmdTarget.Crew, patcher.Id, room, $"{patcher.Name}: {room.Name} 구멍 막기", "실링폼을 들고 가 달라 — 문은 열어 둔다", 1f, pick.Minutes + 2f, dec.Id, -1, "보냄");
                if (patcher.Pose == Pose.Sleeping) a.Command.Wake(patcher, $"{room.Name} 구멍을 막아 달라");
                w.Board.RequestScan();
            }
            return new BreachPlan("patch", until - w.Tick, patcher, dec);
        }
        return new BreachPlan("seal", 0, null, dec);
    }

    private int BeliefSystemPeople(Room r) => _w.Automation.Belief.PeopleIn(r) ?? 0;

    /// <summary>Hull 훅: 사람을 보내 막는 동안 문을 열어 둔다 (기한이 지나거나 막히면 푼다).</summary>
    public bool HoldOpen(Room room)
    {
        if (_holdOpen.Count == 0 || !_holdOpen.TryGetValue(room.Id, out var h)) return false;
        if (_w.Tick > h.until || !room.Leaking) { _holdOpen.Remove(room.Id); return false; }
        return true;
    }

    // ───────────── 불 ─────────────

    /// <summary>불: 소화조에 더 맡김 / 질식 / 진공 — 고른 수단 ("crew"면 더 맡긴다 · null이면 쓸 수단이 없다 · 모형을 못 쓰면 fallback).</summary>
    public string? Fire(Room room, FireCase fc, int cells, float minutes, bool inertOk, bool vacOk, bool crewOnIt, int crewCount, Func<string?> fallback)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.MainOnline || a.Level < 4 || AutomationSystem.Ship20Off) return fallback();
        if (Timeline.LastOrDefault(d => d.Kind == "불" && d.RoomId == room.Id) is ForeseeDecision last && last.Pick.Key == "crew" && w.Tick - last.Tick < SimTime.Minutes(a.Core.Horizon / 2f))
            return "crew"; // 방금 "더 맡긴다"로 정했다 — 예측 거리의 반만큼 기다렸다 다시 본다
        var (pw, sw) = Weights();
        float shipV = ShipVolume();
        int believed = a.Belief.PeopleIn(room) ?? 0;
        // 생체 감시가 그 방을 보면 쓰러진 사람을 안다 (아니면 다 걸어 나갈 거라 믿는다 — 틀릴 수 있다)
        bool bio = a.Active(ComputerModule.BioMonitor) && room.DataLinked && a.Belief.SeesPeople(room);
        int down = 0;
        if (bio) foreach (var c in w.Crew) if (!c.Dead && c.Down && c.Room == room) down++;
        int awake = Math.Max(0, believed - down);
        // 사람 위험: 회의 방침이 무게를 정한다 — 빈 방만이면 비기 전엔 안 한다(늦어질 뿐) · 카운트다운이면 회의가 받아들인 위험 · 컴퓨터 판단이면 온전히
        float Lethal(int policy, float mul) => policy == 1 ? 0f : (down * 0.9f * (policy == 2 ? 0.3f : 1f) + awake * 0.03f) * mul;
        float Delay(int policy) => policy == 1 && believed > 0 ? 5f : 0f;
        float grow = room.Air.O2 > 15f ? 0.12f : 0.04f; // 분당 번지는 비율
        float Damage(float c, float min) => c * min * 0.02f;
        var opts = new List<ForeseeOption>();
        // 소화조에 더 맡김 (쓰러진 사람은 연기 속에 그대로 — 소화조가 끄면서 데리고 나온다)
        if (crewOnIt)
        {
            float rate = MathF.Max(0.3f, 0.8f * crewCount) * Noise();
            float net = rate - grow * cells;
            float t = MathF.Min(30f, net > 0.05f ? cells / net : 30f);
            opts.Add(new ForeseeOption { Key = "crew", Name = "소화조에 더 맡기기", Minutes = t, People = 0.06f * crewCount + (t > 12f ? 0.1f * crewCount : 0f) + down * MathF.Min(0.6f, t / 20f), Ship = Damage(cells + grow * cells * t, t), Note = $"소화조 {crewCount}명 · 분당 {rate:0.#}칸" });
        }
        // 질식 소화
        {
            int pol = _w.Policies["inertfire"];
            float pi = Math.Clamp(1f - (cells - 4f) / 8f, 0.2f, 0.95f) * (inertOk ? 1f : 0f);
            float t = 0.5f + 2.5f + (1f - pi) * 15f + Delay(pol);
            var o = new ForeseeOption { Key = "inert", Name = "질식 소화", Minutes = t * Noise(), People = Lethal(pol, 0.8f), Ship = Damage(cells, t) + 0.05f, Note = $"성공 {pi * 100:0}% · 가스 {(a.InertCapacity > 0 ? a.InertGas / a.InertCapacity * 100 : 100):0}%" };
            if (!inertOk) { o.Allowed = false; o.Blocked = pol <= 0 ? "회의가 금했다" : a.InertGas < 17f * room.Volume * 0.6f ? "불활성 가스가 모자라다" : "이미 써 봤다"; }
            opts.Add(o);
        }
        // 진공 소화 (공기를 잃는다 — 탱크로 다시 채워야 한다)
        {
            int pol = _w.Policies["vacuumfire"];
            float t = 2f + 3f + Delay(pol);
            var o = new ForeseeOption { Key = "vacuum", Name = "진공 소화", Minutes = t * Noise(), People = Lethal(pol, 1f), Ship = Damage(cells, t) + room.Volume / shipV * 10f + 0.4f, Note = "공기 · 작물을 잃는다 · 확실하다" }; // 다시 채울 공기 · 얼어붙는 짐
            if (!vacOk) { o.Allowed = false; o.Blocked = pol <= 0 ? "회의가 금했다" : "공기를 뺄 밸브가 없다"; }
            opts.Add(o);
        }
        if (opts.All(o => !o.Allowed)) return fallback();
        var dec = Choose("불", room, $"{room.Name} 불 {cells}칸 · {minutes:0}분", opts, pw, sw);
        return dec.Pick.Key switch { "inert" => "inert", "vacuum" => "vacuum", _ => "crew" };
    }

    // ───────────── 정전 (트리아지가 묻는다) ─────────────

    /// <summary>정전 위기: 버팀 / 몰아주기 / 보조 발전기 + 몰아주기 — 필수 회로가 꺼지기까지와 복구까지를 견준다.</summary>
    public ForeseeDecision Power(float drainKw, float parkKw, float auxKw, float auxFail, float restoreHours, int crew)
    {
        var w = _w;
        var p = w.Power;
        var (pw, sw) = Weights();
        float charge = p.BatteryCharge;
        float H(float drain) => drain <= 0.05f ? 99f : charge / drain;
        float Risk(float hours) => restoreHours >= 98f ? Math.Clamp(1f - hours / 24f, 0f, 1f) * crew * 0.3f : Math.Clamp((restoreHours - hours) / MathF.Max(0.5f, restoreHours), 0f, 1f) * crew * 0.3f;
        float h0 = H(drainKw) * Noise(), h1 = H(drainKw - parkKw) * Noise(), h2 = H(drainKw - parkKw - auxKw * (1f - auxFail)) * Noise();
        var opts = new List<ForeseeOption>
        {
            new() { Key = "hold", Name = "배터리로 버틴다", Minutes = h0 * 60f, People = Risk(h0), Ship = 0f, Note = $"필수까지 {h0:0.#}시간" },
            new() { Key = "park", Name = "급하지 않은 설비를 끄고 생명유지 쪽으로", Minutes = h1 * 60f, People = Risk(h1), Ship = parkKw * 0.01f, Note = $"{parkKw:0.#}kW 끔 → {h1:0.#}시간", Allowed = parkKw > 0.1f, Blocked = parkKw > 0.1f ? "" : "끌 것이 없다" },
            new() { Key = "aux", Name = "보조 발전기를 켜고 생명유지 쪽으로", Minutes = h2 * 60f, People = Risk(h2), Ship = parkKw * 0.01f + 0.05f, Note = $"{auxKw:0.#}kW · 실패 {auxFail * 100:0}% → {h2:0.#}시간", Allowed = auxKw > 0.1f, Blocked = auxKw > 0.1f ? "" : "보조 발전기를 여기서 켤 수 없다" },
        };
        return Choose("정전", w.Power.Reactor?.Body.Room, $"전기가 모자라다 — 배터리 {p.BatteryPercent * 100:0}%에서 {drainKw:0.#}kW씩 빠진다" + (restoreHours < 98f ? $" · 원자로까지 {restoreHours:0.#}시간" : " · 원자로를 언제 켤지 모른다"), opts, pw, sw);
    }

    /// <summary>차단기: 바로 올림 / 원인을 끊고 올림 / 올리지 않고 사람에게 — 다시 떨어질 확률로 견준다.</summary>
    public ForeseeDecision Breaker(int circuit, string cause, bool cutPossible, float retripNow, float retripCut, bool sameCause)
    {
        var (pw, sw) = Weights();
        var ch = _w.Automation.Character;
        float dark = 0.3f; // 회로 하나 정전의 무게
        var opts = new List<ForeseeOption>
        {
            new() { Key = "reset", Name = "바로 올린다", People = 0f, Ship = dark * retripNow * 3f + 0.05f, Minutes = 0.2f, Note = $"다시 떨어질 {retripNow * 100:0}%", Allowed = !sameCause, Blocked = sameCause ? "같은 이유로 이미 한 번 떨어졌다" : "" },
            new() { Key = "cut", Name = "원인부터 끊고 올린다", People = 0f, Ship = dark * retripCut * 3f + 0.08f, Minutes = 0.5f, Note = $"다시 떨어질 {retripCut * 100:0}%", Allowed = cutPossible && !sameCause, Blocked = !cutPossible ? "여기서 끊을 수 있는 원인이 아니다" : sameCause ? "같은 이유로 또 떨어졌다 — 손으로 빼야 한다" : "" },
            new() { Key = "hands", Name = "올리지 않고 사람에게 맡기기", People = 0f, Ship = dark * (1.2f + 0.3f * MathF.Max(0f, -ch.Caution)) + 0.1f, Minutes = 15f, Note = "손으로 원인을 빼고 올린다 (그동안 정전)" },
        };
        return Choose("차단기", null, $"{PowerGrid.CircuitName(circuit)} 회로 차단기가 떨어졌다 — {cause}", opts, pw, sw);
    }

    // ───────────── 고르기 · 기록 ─────────────

    private ForeseeDecision Choose(string kind, Room? room, string title, List<ForeseeOption> opts, float pw, float sw)
    {
        var w = _w;
        var a = w.Automation;
        var ch = a.Character;
        foreach (var o in opts)
        {
            // 신중하면 불확실한(오래 걸리는) 안에 벌점 · 과감하면 덜
            float unsure = 1f + 0.15f * ch.Caution * MathF.Min(1f, o.Minutes / 10f);
            o.Score = (pw * o.People + sw * o.Ship) * unsure;
        }
        int best = -1;
        for (int i = 0; i < opts.Count; i++)
            if (opts[i].Allowed && (best < 0 || opts[i].Score < opts[best].Score - 1e-5f)) best = i;
        if (best < 0) best = 0;
        var pick = opts[best];
        var second = opts.Where(o => o != pick).OrderBy(o => o.Allowed ? 0 : 1).ThenBy(o => o.Score).FirstOrDefault();
        string reason = second == null ? "다른 안이 없다"
            : !second.Allowed ? $"{second.Name} — {second.Blocked}"
            : pick.People + 0.05f < second.People ? $"다칠 사람이 더 적다 ({pick.People:0.#}명 · {second.Name}이면 {second.People:0.#}명)"
            : pick.Ship + 0.02f < second.Ship ? $"잃는 공기 · 설비가 더 적다 ({second.Name}보다)"
            : $"견줘 보니 조금 낫다 ({second.Name}보다)";
        var rank = room != null ? Queue.FirstOrDefault(q => q.RoomId == room.Id) : null;
        var d = new ForeseeDecision
        {
            Id = _next++, Tick = w.Tick, Kind = kind, RoomId = room?.Id ?? -1, Title = title, Chosen = best, Reason = reason, PeopleW = pw, ShipW = sw,
            Rank = Queue.Count >= 2 ? rank?.Order ?? 0 : 0, By = a.Operator?.Name ?? (a.MainOnline ? "주 코어" : "예비 코어"),
            GradeAt = w.Tick + SimTime.Minutes(kind == "정전" ? 60f : kind == "차단기" ? 30f : 8f), DownBefore = ComputerLogBook.DownIn(w, room), PressureBefore = room?.Air.Pressure ?? 0f,
        };
        d.Options.AddRange(opts);
        Timeline.Add(d);
        if (Timeline.Count > 80) Timeline.RemoveAt(0);
        Decisions++;
        string cmp = string.Join(" / ", opts.Select(o => $"{o.Name} {(o.Allowed ? $"(다칠 사람 {o.People:0.#} · 잃는 것 {o.Ship:0.##})" : $"({o.Blocked})")}"));
        a.Book.Add(ActKind.Forecast, room, title, $"{opts.Count}가지를 {a.Core.Horizon:0}분 앞까지 견줘 봤다: {cmp}", $"{pick.Name} — {reason}", "", $"fs:{d.Id}", 0, kind == "정전" ? 60f : 10f,
            (world, act) => d.Score != 0 ? (d.Score, d.Result) : ((int, string)?)null);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {title} — {string.Join(" / ", opts.Select(o => o.Name))}를 견줘 봤습니다. {pick.Name} ({reason})");
        return d;
    }

    // ───────────── 겹친 사고 순서 ─────────────

    /// <summary>시스템 틱마다(가볍게 · 방 단위): 사고를 모아 위험한 사람 수 × 위험까지 남은 시간으로 순서를 매긴다.</summary>
    public void Rank()
    {
        var w = _w;
        var a = w.Automation;
        Queue.Clear();
        if (!a.CoreOnline || AutomationSystem.Ship20Off) return;
        var bel = a.Belief;
        foreach (var fc in a.FireCases)
        {
            var r = w.Ship.Rooms[fc.RoomId];
            int people = (bel.PeopleIn(r) ?? 1) + Neighbors(r).Sum(o => bel.PeopleIn(o) ?? 0) / 2;
            int cells = w.Fire.CountIn(r);
            float min = 3f + 20f / MathF.Max(1f, cells);
            Queue.Add(new IncidentRank { Key = "fire:" + r.Id, Kind = "불", RoomId = r.Id, People = people, Minutes = min, Score = (people + 0.3f) / MathF.Max(0.5f, min) });
        }
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Leaking)
            {
                int people = (bel.PeopleIn(r) ?? 1) + Neighbors(r).Sum(o => bel.PeopleIn(o) ?? 0);
                float min = DangerMinutes(r.Air.Pressure, MathF.Max(1f, r.Air.Leak), r.Volume + (r.Lockdown ? 0f : Neighbors(r).Sum(o => o.Volume)));
                Queue.Add(new IncidentRank { Key = "breach:" + r.Id, Kind = "파공", RoomId = r.Id, People = people, Minutes = MathF.Min(60f, min), Score = (people + 0.3f) / MathF.Max(0.5f, MathF.Min(60f, min)) });
            }
            else if (MoistureSystem.Depth(r) > 0.1f && !r.BreakerOff)
            {
                int people = bel.PeopleIn(r) ?? 0;
                Queue.Add(new IncidentRank { Key = "flood:" + r.Id, Kind = "침수", RoomId = r.Id, People = people, Minutes = 10f, Score = (people + 0.1f) / 10f });
            }
            else if (r.Air.CO > 0.08f || r.Air.Toxin > 0.1f)
            {
                int people = bel.PeopleIn(r) ?? 0;
                Queue.Add(new IncidentRank { Key = "toxic:" + r.Id, Kind = "독한 공기", RoomId = r.Id, People = people, Minutes = 6f, Score = (people + 0.1f) / 6f });
            }
        }
        var p = w.Power;
        if (!p.ReactorOnline && p.BatteryFlow < -0.3f)
        {
            float h = p.BatteryCharge / MathF.Max(0.1f, -p.BatteryFlow);
            int crew = w.Crew.Count(c => !c.Dead && !c.Away);
            Queue.Add(new IncidentRank { Key = "power", Kind = "정전", RoomId = p.Reactor?.Body.Room.Id ?? -1, People = crew, Minutes = MathF.Min(600f, h * 60f), Score = crew / MathF.Max(0.5f, MathF.Min(600f, h * 60f)) });
        }
        if (Queue.Count == 0) { _queueSig = ""; QueueLine = ""; return; }
        Queue.Sort((x, y) => y.Score != x.Score ? y.Score.CompareTo(x.Score) : string.CompareOrdinal(x.Key, y.Key));
        int k = a.MainOnline ? a.Core.Concurrency : 1; // 예비 코어는 하나씩
        for (int i = 0; i < Queue.Count; i++) { Queue[i].Order = i + 1; Queue[i].Attended = i < k; }
        if (Queue.Count < 2) { _queueSig = ""; QueueLine = ""; return; }
        string sig = string.Join(",", Queue.Select(q => q.Key));
        if (sig == _queueSig) return;
        _queueSig = sig;
        Overlaps++;
        QueueLine = string.Join(" → ", Queue.Take(5).Select(q => $"{q.Order}. {(q.RoomId >= 0 ? w.Ship.Rooms[q.RoomId].Name + " " : "")}{q.Kind}({q.People:0}명 · {q.Minutes:0}분){(q.Attended ? "" : " 기다림")}"));
        int waiting = Queue.Count(q => !q.Attended);
        a.Book.Add(ActKind.Forecast, null, $"사고 {Queue.Count}건이 겹쳤다", $"사람이 많고 급한 곳부터 — 한 번에 {k}곳", QueueLine, waiting > 0 ? "기다리는 곳은 가까운 사람이 먼저 봐 달라" : "", "queue:" + sig, SimTime.Minutes(5), 15f);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 사고 {Queue.Count}건이 겹쳤습니다 — {QueueLine}");
    }

    /// <summary>그 사고를 지금 다루나 (동시 처리 수 안). 순서표에 없으면 다룬다.</summary>
    public bool Attending(string key)
    {
        if (Queue.Count < 2) return true;
        var q = Queue.FirstOrDefault(x => x.Key == key);
        if (q == null || q.Attended) return true;
        Deferred++;
        return false;
    }

    // ───────────── 채점 ─────────────

    /// <summary>1분마다: 기한이 된 결정을 실제 결과로 채점한다 (쓰러진 사람 · 기압 · 불 · 정전) → 성격이 자란다.</summary>
    public void Update()
    {
        var w = _w;
        if (w.Tick < _gradeNext) return;
        _gradeNext = w.Tick + SimTime.Minutes(1);
        foreach (var d in Timeline)
        {
            if (d.Score != 0 || w.Tick < d.GradeAt) continue;
            var room = d.RoomId >= 0 && d.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[d.RoomId] : null;
            int down = ComputerLogBook.DownIn(w, room) - d.DownBefore;
            (int s, string why) = d.Kind switch
            {
                "파공" => down > 0 ? (-1, $"틀렸다 — {down}명이 쓰러졌다") : room != null && room.Leaking && !room.Lockdown && room.Air.Pressure < 50f ? (-1, "틀렸다 — 아직 새고 열려 있다") : (1, $"맞았다 — 쓰러진 사람 없이 ({room?.Air.Pressure ?? 0:0}kPa)"),
                "불" => down > 0 ? (-1, $"틀렸다 — {down}명이 쓰러졌다") : room != null && w.Fire.CountIn(room) > 8 ? (-1, $"틀렸다 — 불이 {w.Fire.CountIn(room)}칸으로 커졌다") : (1, room != null && w.Fire.CountIn(room) == 0 ? "맞았다 — 꺼졌다" : "맞았다 — 잡혀 간다"),
                "정전" => w.Power.BatteryPercent > 0.02f || w.Power.ReactorOnline || w.Power.AuxRunning ? (1, $"맞았다 — 필수 회로가 버텼다 (배터리 {w.Power.BatteryPercent * 100:0}%)") : (-1, "틀렸다 — 배터리가 바닥났다"),
                "차단기" => (2, "트리아지가 따로 채점한다"),
                _ => (2, ""),
            };
            if (d.Grader?.Invoke(w, d) is (int gs, string gw)) { s = gs; why = gw; }
            d.Score = s;
            d.Result = why;
            if (s == 1) Right++;
            else if (s == -1) Wrong++;
            w.Automation.Character.Graded(d);
        }
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Decisions); I(Right); I(Wrong); I(Deferred); I(Overlaps); I(Queue.Count); I(_holdOpen.Count);
        foreach (var d in Timeline) { I(d.Id); I(d.Chosen); I(d.Score); }
    }
}

public sealed partial class AutomationSystem
{
    private ComputerForesee? _foresee;
    /// <summary>v16.20 미리 돌려 보고 고르기 · 겹친 사고 순서 · 타임라인.</summary>
    public ComputerForesee Foresee => _foresee ??= new ComputerForesee(_world);
}
