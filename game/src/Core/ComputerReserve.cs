using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ③ 자원 예약 운영 — 지금 꺼도 되는 것과 나중에 반드시 켤 것을 함께 계산한다.
//  전력: 이송 중인 부상자(누가 업고 오는 중 · 쓰러진 채 의무실 밖) → 의무실 전기를 남긴다 (전력 몰아주기가 치료 침대를 끄지 않는다).
//  배터리: 원자로가 꺼져 있으면 재기동에 쓸 몫을 남긴다 ("45분 뒤 재기동 여유를 남기며 운영 중") — 몰아주기 계산이 그만큼 덜 쓴다.
//  부품: 핵심 설비(냉각 펌프 · 산소 발생기 · 물 재생기)의 다음 고장에 쓸 마지막 부품 — 급하지 않은 수리가 가져가지 않게 묶는다.
//  인력: 계획이 곧 부를 사람은 잡일 부탁을 막고(교대 · 피로) · 너무 지친 사람은 쉬라고 한다.
//  산소 · 물: 재가압 · 우주복 · 냉각수 보충 몫을 적어 둔다 (계획 · 화면이 읽는다).
//  장비: 들것 · 우주복이 겹치는 일을 적는다.
//  2분마다 · 방 단위 (사람 · 설비 전부를 매 틱 돌지 않는다).

public sealed class Reservation
{
    /// <summary>전력 · 배터리 · 부품 · 인력 · 산소 · 물 · 장비.</summary>
    public string Kind { get; init; } = "";
    public string What { get; init; } = "";
    public float Amount { get; init; }
    public string Unit { get; init; } = "";
    public string Why { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public int CrewId { get; init; } = -1;
    public long Since { get; set; }
}

public sealed class ComputerReserve
{
    private readonly World _w;
    private long _next;
    public List<Reservation> Now { get; } = new();
    private readonly HashSet<int> _holdMachines = new();
    private readonly HashSet<int> _holdCrew = new();
    private readonly Dictionary<string, long> _since = new();
    /// <summary>몰아주기가 손대지 않을 배터리 (kWh).</summary>
    public float KeepKwh { get; private set; }
    public bool MedHold { get; private set; }
    public string Line { get; private set; } = "";
    public int MedHolds, RestartHolds, PartHolds, CrewHolds, RestAsks, Blocks;

    public ComputerReserve(World w) => _w = w;

    /// <summary>전력 몰아주기 훅: 예약된 설비는 끄지 않는다.</summary>
    public bool Holds(Machine m) => _holdMachines.Contains(m.Body.Id);
    public bool HoldsCrew(CrewMember c) => _holdCrew.Contains(c.Id);

    public void Update()
    {
        var w = _w;
        var a = w.Automation;
        if (FixBook.Off || w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(2);
        Now.Clear();
        _holdMachines.Clear();
        _holdCrew.Clear();
        KeepKwh = 0f;
        bool med = false;
        if (!a.CoreOnline) { MedHold = false; Line = ""; return; }
        var p = w.Power;

        // ── 전력: 이송 중인 부상자 → 의무실 ──
        var medbay = w.Ship.RoomsOf(RoomType.Medbay).FirstOrDefault();
        if (medbay != null)
        {
            var coming = w.Crew.Where(c => !c.Dead && (c.CarriedBy != null || c.Down && c.Room != medbay && c.CareBed == null)).ToList();
            if (coming.Count > 0)
            {
                med = true;
                float kw = 0f;
                foreach (var f in w.Ship.RoomsOf(RoomType.Medbay).SelectMany(r => r.Furniture).Concat(w.Ship.FurnitureOf(FurnitureType.MedBed)))
                    if (f.Machine is Machine m && m.Spec.PowerDraw > 0f && _holdMachines.Add(f.Id)) kw += m.Spec.PowerDraw;
                var who = coming.OrderBy(c => c.Id).First();
                Add("전력", $"{medbay.Name} 치료 침대 · 기기", kw, "kW", coming.Count == 1 ? (who.CarriedBy != null ? $"{Ko.EulReul(who.Name)} 업고 오는 중" : $"{Ko.IGa(who.Name)} 쓰러져 있다") : $"부상자 {coming.Count}명이 오는 중", medbay.Id);
                if (!_since.ContainsKey("med")) { MedHolds++; w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {a.Manner.Speak($"{medbay.Name} 전기를 남겨 둡니다 — {(who.CarriedBy != null ? $"{Ko.EulReul(who.Name)} 옮기는 중" : $"{Ko.IGa(who.Name)} 쓰러져 있다")}")}"); }
                Mark("med");
            }
            else _since.Remove("med");
        }
        MedHold = med;

        // ── 배터리: 원자로 재기동 여유 ──
        if (!p.ReactorOnline && p.BatteryCapacity > 0f)
        {
            // 재기동: 제어봉 · 냉각 펌프 · 기동 — 필수 부하로 45분 + 기동 몫
            float essential = MathF.Max(1.5f, w.Ship.Machines.Where(m => m.Powered && PowerTriage.Rank(w, m) >= 9).Sum(m => m.Demand));
            KeepKwh = MathF.Min(p.BatteryCapacity * 0.35f, essential * 0.75f + 4f);
            Add("배터리", "원자로 재기동 여유", KeepKwh, "kWh", $"45분 뒤 재기동 — 냉각 펌프 · 제어봉 · 필수 회로", -1);
            if (!_since.ContainsKey("restart")) RestartHolds++;
            Mark("restart");
        }
        else _since.Remove("restart");

        // ── 부품: 핵심 설비의 다음 고장에 쓸 마지막 하나 ──
        foreach (var (type, part) in new[] { (FurnitureType.CoolantPump, ItemKind.Pump), (FurnitureType.OxygenGenerator, ItemKind.Electronics), (FurnitureType.WaterRecycler, ItemKind.Filter) })
        {
            if (!w.Ship.FurnitureOf(type).Any()) continue;
            int have = w.Ship.CountStored(part);
            if (have != 1) continue;
            Add("부품", $"{ItemKinds.Name(part)} 마지막 1개", 1f, "개", $"{FurnitureTypes.Name(type)} 다음 고장 몫", -1);
            Mark("part:" + part);
            // 급하지 않은 수리 · 제작이 그 부품을 가져가려 하면 미룬다
            foreach (var o in w.Board.Open.ToList())
            {
                if (o.Closed || o.Assignee != null || o.Kind != WorkKind.Repair || o.Furniture?.Type == type || o.Urgency >= 0.9f) continue;
                if (o.Furniture?.Machine is not Machine om || !om.Faults.Any(f => f.Materials.Any(x => x.kind == part))) continue;
                if (o.BlockedUntil > w.Tick) continue;
                w.Board.Block(o, $"마지막 {Ko.EunNeun(ItemKinds.Name(part))} {FurnitureTypes.Name(type)} 몫으로 남긴다", 2f);
                Blocks++;
                PartHolds++;
            }
        }

        // ── 인력: 계획이 곧 부를 사람 · 지친 사람 ──
        foreach (var plan in a.Recovery.Plans)
        {
            if (!plan.Open) continue;
            foreach (var s in plan.Steps)
            {
                if (s.Act.Kind != FixKind.Hands || s.State is FixState.Done or FixState.Failed or FixState.Skipped || s.Crew < 0) continue;
                var c = w.Crew.FirstOrDefault(x => x.Id == s.Crew);
                if (c == null || c.Dead) continue;
                _holdCrew.Add(c.Id);
                Add("인력", c.Name, 1f, "명", $"{plan.Goal.Split(" — ")[0]} {s.Name} 몫" + (c.Needs.Rest < 0.3f ? " (지쳤다 — 끝나면 바로 쉬게)" : ""), -1, c.Id);
                if (!_since.ContainsKey("crew:" + c.Id)) CrewHolds++;
                Mark("crew:" + c.Id);
            }
        }
        // 계획이 돌 때: 지친 기술자는 지금 쉬게 해 다음 교대를 남긴다 (한 명이 다 하지 않게)
        if (a.Recovery.Plans.Any(x => x.Open))
            foreach (var c in w.Crew)
            {
                if (c.Dead || !c.CanAct || _holdCrew.Contains(c.Id) || c.Needs.Rest > 0.2f || a.CrewModel.RestAsked(c)) continue;
                if (c.SkillLevel(Skill.Mechanics) < 0.5f && c.SkillLevel(Skill.Electrical) < 0.5f) continue;
                a.CrewModel.AskRest(c, "다음 교대에 손이 필요하다 — 지금 쉬어 두십시오", 4f);
                RestAsks++;
            }

        // ── 작업대: 부품을 기다리는 계획이 있으면 만들 자리를 남긴다 (몰아주기가 끄지 않는다) ──
        if (a.Recovery.Plans.Any(x => x.Open && x.Step?.Act is PartStep))
            foreach (var f in w.Ship.FurnitureOf(FurnitureType.Fabricator).Concat(w.Ship.FurnitureOf(FurnitureType.Workbench)))
                if (f.Machine is Machine fm && fm.Spec.PowerDraw > 0f && _holdMachines.Add(f.Id))
                {
                    Add("전력", $"{f.Room.Name} {f.Name}", fm.Spec.PowerDraw, "kW", "고장 난 설비에 들어갈 부품을 만들 자리", f.Room.Id);
                    break;
                }

        // ── 산소 · 물 · 장비 (계획이 읽고 화면에 보인다) ──
        int leaking = w.Ship.LiveRooms.Count(r => r.Leaking);
        if (leaking > 0) Add("산소", "재가압 몫", leaking * 6f, "kg", $"새는 방 {leaking}곳을 막은 뒤 다시 채운다", -1);
        if (w.Piping.Built && w.Piping.CoolantFraction < 0.85f)
            Add("물", "냉각수 보충", (1f - w.Piping.CoolantFraction) * PipeNetwork.CoolantMax, "L", "냉각 루프가 모자란다 — 마실 물보다 먼저 쓰지는 않는다", -1);
        int carried = w.Crew.Count(c => c.CarriedBy != null);
        int suited = w.Crew.Count(c => !c.Dead && c.Suit != null);
        if (carried > 0 || suited > 0) Add("장비", carried > 0 ? "들것 · 업는 사람" : "우주복", carried + suited, "개", carried > 0 ? $"부상자 {carried}명 이송" : $"우주복 {suited}벌이 나가 있다", -1);

        Line = Now.Count == 0 ? "" : KeepKwh > 0f ? $"45분 뒤 재기동 여유 {KeepKwh:0}kWh를 남기며 운영 중" : med ? $"{medbay?.Name ?? "의무실"} 전기를 남기며 운영 중" : $"{Now[0].What} 예약 — {Now[0].Why}";
    }

    private void Add(string kind, string what, float amount, string unit, string why, int room, int crew = -1)
    {
        string key = kind + ":" + what;
        Now.Add(new Reservation { Kind = kind, What = what, Amount = amount, Unit = unit, Why = why, RoomId = room, CrewId = crew, Since = _since.TryGetValue(key, out var s) ? s : _w.Tick });
        _since.TryAdd(key, _w.Tick);
    }

    private void Mark(string key) => _since.TryAdd(key, _w.Tick);

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Now.Count); F(KeepKwh); I(MedHold ? 1 : 0); I(MedHolds); I(RestartHolds); I(PartHolds); I(CrewHolds); I(RestAsks); I(Blocks);
    }
}

public sealed partial class AutomationSystem
{
    private ComputerReserve? _reserve;
    /// <summary>v16.26 ③ 자원 예약 운영.</summary>
    public ComputerReserve Reserve => _reserve ??= new ComputerReserve(_world);
    internal ComputerReserve? ReserveOrNull => _reserve;
}
