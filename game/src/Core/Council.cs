using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

public enum DecisionState { None, Pending, Approved, Rejected }

/// <summary>
/// 결정 주체 (v7). 구획 포기·절전·부품 뜯기·방 용도 변경·개조는 "비상 규정"이 자동으로 하는 게 아니라 사람이 정한다.
/// - 급할 때(압박 0.9 이상)는 지휘 순서대로 깨어 있는 사람(기관장 → 항법사 → 정비사 → …)이 혼자 판단한다.
/// - 급하지 않으면 깨어 있는 사람들이 회의해서 다수결로 정한다 (동수면 지휘자 뜻대로).
/// - 각자 성격·공포·근무지에 따라 찬반이 갈린다: 대담한 기관장은 구획 포기를 미루고, 거기서 죽을 뻔한 사람은 재개방에 반대한다.
/// - 부결되면 한동안 미뤄지고, 상황이 계속되면 압박이 커져 결국 받아들여진다. 반대했던 사람은 서운함이 남는다.
/// 이 모든 게 연대기에 남는다 (누가 정했고, 누가 반대했나).
/// </summary>
public static partial class Council
{
    private static readonly CrewRole[] Chain =
        { CrewRole.Engineer, CrewRole.Pilot, CrewRole.Technician, CrewRole.Electrician, CrewRole.Medic, CrewRole.Botanist, CrewRole.Cook };

    public static bool Needs(WorkKind k) =>
        k is WorkKind.SealOffRoom or WorkKind.ReopenRoom or WorkKind.Cannibalize or WorkKind.ShedLoad or WorkKind.RepurposeRoom or WorkKind.Recycle
            or WorkKind.BuildWorkshop or WorkKind.Upgrade
            or WorkKind.Jettison or WorkKind.Retrieve or WorkKind.RestoreRoom // v8
            or WorkKind.IsolateMain or WorkKind.LimpMain // v9
            or WorkKind.PlanRepipe // v9.2
            or WorkKind.Brownout // v9.3
            or WorkKind.ChangeCourse // v11.2
            or WorkKind.Ration // v10.11
            or WorkKind.Distress or WorkKind.AnswerSignal; // v11.2

    /// <summary>손대도 되는 일인지 (결정이 필요 없거나, 승인됐다).</summary>
    public static bool Cleared(WorkOrder o) => !Needs(o.Kind) || o.Decision == DecisionState.Approved;

    /// <summary>지휘 순서대로 결정할 사람. 급하면 자는 사람도 깨운다.</summary>
    public static CrewMember? Decider(World w, bool urgent)
    {
        foreach (var role in Chain)
            foreach (var c in w.Crew)
                if (c.Role == role && c.CanAct && (c.IsAwake || urgent)) return c;
        return w.Crew.FirstOrDefault(c => c.CanAct);
    }

    /// <summary>v13.2 긴급 판단: 선장이 혼자 정한다 (선장이 못 하면 지휘 순서대로).</summary>
    public static CrewMember? Judge(World w, bool urgent) =>
        w.Command.Captain is CrewMember cap && cap.CanAct && (cap.IsAwake || urgent) ? cap : Decider(w, urgent);

    private static bool Stake(CrewMember c, Room? r) =>
        r != null && (c.Stations.Contains(r.Type) || c.Bed?.Room == r || c.HomeBed?.Room == r);

    private static string StakeName(CrewMember c, Room r) =>
        c.Bed?.Room == r || c.HomeBed?.Room == r ? "내 침실" : "내 근무지";

    /// <summary>상황이 얼마나 등을 떠미는지 (0.5 = 반반, 1 이상 = 거의 선택의 여지가 없다).</summary>
    public static float Pressure(World w, WorkOrder o)
    {
        float p;
        switch (o.Kind)
        {
            case WorkKind.SealOffRoom:
            {
                var r = o.Target.Room!;
                float hours = r.Leaking ? (w.Tick - r.LeakingSince) / (float)SimTime.TicksPerHour : 0f;
                p = 0.55f + (o.Detail.Contains("실링폼") ? 0.35f : 0.15f) + MathF.Min(0.3f, hours / 10f)
                    + (w.Air.Reserve < w.Air.ReserveCapacity * 0.3f ? 0.15f : 0f);
                break;
            }
            case WorkKind.ReopenRoom:
            {
                var r = o.Target.Room!;
                bool homes = w.Crew.Any(c => !c.Dead && c.HomeBed?.Room == r);
                bool vital = r.Furniture.Any(f => f.Machine is Machine m && (m.Spec.Critical || f.Type is FurnitureType.Workbench or FurnitureType.GrowBed));
                p = 0.55f + (homes ? 0.15f : 0f) + (vital ? 0.1f : 0f);
                break;
            }
            case WorkKind.ShedLoad:
                p = 0.75f + Math.Clamp((0.35f - w.Power.BatteryPercent) * 1.5f, 0f, 0.4f);
                break;
            case WorkKind.Cannibalize:
                p = o.Urgency;
                break;
            case WorkKind.ChangeCourse:
                // v11.2: 잔해 지대에서 나가자는 안은 맞을수록 쉽게, 들어가자는 안은 원료가 모자랄수록
                p = (ZoneKind)o.Circuit == ZoneKind.Normal ? 0.6f + 0.15f * w.Propulsion.HitsThisZone
                    : 0.45f + MathF.Min(0.3f, (12 - ItemKinds.RawKinds.Sum(k => w.Board.Have(k))) * 0.03f);
                break;
            case WorkKind.Ration:
                p = RationPressure(w); // v10.11
                break;
            case WorkKind.Distress:
            case WorkKind.AnswerSignal:
                p = CommsPressure(w, o); // v11.2
                break;
            case WorkKind.Recycle:
                // v10.10: 되돌릴 값이 없는 뜯긴 설비를 고철로 — 금속판이 모자랄수록 쉽게 통과
                p = 0.5f + MathF.Min(0.35f, (8 - w.Board.Have(ItemKind.Plate)) * 0.05f);
                break;
            case WorkKind.RepurposeRoom:
                p = 0.65f;
                break;
            case WorkKind.BuildWorkshop:
                p = 0.7f;
                break;
            case WorkKind.Upgrade:
                // 교훈이 무거울수록 (같은 사고를 여러 번 겪었을수록) 개조안이 쉽게 통과한다
                p = 0.45f + 0.2f * MathF.Min(1.5f, Evolution.LessonWeight(w, o));
                break;
            case WorkKind.Jettison:
            {
                var r = o.Target.Room!;
                if (o.Detail.Contains("불"))
                    p = 0.6f + MathF.Min(0.45f, (w.Fire.BurningHours(r) - 1.5f) / 4f) + (r.Doors.Any(d => d.Openness > 0.3f) ? 0.1f : 0f);
                else
                {
                    float cap = StructureSystem.KnownCapacity(r);
                    p = 0.75f + (cap < 0.22f * r.DesignJoints ? 0.3f : 0.15f)
                        + (r.Joints.Count(j => !j.KnownBroken) <= 1 ? 0.15f : 0f);
                }
                break;
            }
            case WorkKind.IsolateMain:
            {
                // 본관이 새는데도 흐르고 있다: 잠그면 원자로가 선다. 많이 샐수록, 냉각수가 줄수록, 증기에 사람이 데일수록 잠그자는 쪽으로
                var s = o.Target.Pipe!;
                var net = w.Piping;
                p = 0.4f + MathF.Min(0.4f, s.LeakRate / 50f) + (net.CoolantFraction < 0.6f ? 0.2f : net.CoolantFraction < 0.8f ? 0.08f : 0f)
                    + (w.Water.Level < 60f ? 0.15f : 0f) + (w.Power.BatteryPercent < 0.3f ? -0.15f : 0f);
                break;
            }
            case WorkKind.LimpMain:
            {
                // 잠가 둔 본관을 고칠 재료가 없다: 원자로 없이 얼마나 버틸 수 있나
                var s = o.Target.Pipe!;
                float hours = (w.Tick - s.ClosedSince) / (float)SimTime.TicksPerHour;
                p = 0.45f + (w.Power.BatteryPercent < 0.3f ? 0.3f : w.Power.BatteryPercent < 0.5f ? 0.15f : 0f)
                    + (w.Power.AuxRunning && w.Power.AuxFuel < 6f ? 0.2f : 0f) + MathF.Min(0.25f, hours / 16f)
                    - (w.Water.Level < 120f ? 0.15f : 0f);
                break;
            }
            case WorkKind.Brownout:
            {
                // 전기가 모자란 채로 굳었다: 모자랄수록, 재배대가 꺼져 있을수록, 배터리가 바닥일수록
                var pw = w.Power;
                bool hungry = w.Ship.FurnitureOf(FurnitureType.GrowBed).Any(f => !f.Room.Abandoned && !f.Machine!.Powered);
                p = 0.55f + MathF.Min(0.4f, (pw.Demand - pw.ReactorLimit) / 15f) + (hungry ? 0.15f : 0f) + (pw.BatteryPercent < 0.1f ? 0.1f : 0f);
                break;
            }
            case WorkKind.PlanRepipe:
            {
                // 임시 밀봉한 본관을 새 관으로: 멀쩡히 도는 원자로를 일부러 세운다. 밀봉이 여러 번 터졌을수록, 배터리가 넉넉할수록 하자는 쪽으로
                var s = o.Target.Pipe!;
                float battery = w.Power.BatteryPercent;
                p = 0.35f + 0.12f * MathF.Min(4, s.PatchFails) + 0.04f * MathF.Min(5, s.Patches)
                    + (battery >= 0.8f ? 0.1f : battery < 0.5f ? -0.25f : 0f) + (w.History.Doctrine.PipeReserve ? 0.1f : 0f);
                break;
            }
            case WorkKind.Retrieve:
            {
                var r = o.Target.Room!;
                p = 0.5f + RoomValue(w, r) - 0.35f * StructureSystem.WreckScore(w, r) + (Essential(w, r) != null ? 0.3f : 0f)
                    + (w.Crew.Any(c => c.Aboard?.Room == r && !c.Dead) ? 0.8f : 0f) // 사람이 타고 있다
                    + (r.Fragment is Fragment f && f.Distance > StructureSystem.LostRange * 0.6f ? 0.1f : 0f);
                break;
            }
            case WorkKind.RestoreRoom:
            {
                var r = o.Target.Room!;
                p = 0.5f + RoomValue(w, r) - 0.5f * StructureSystem.WreckScore(w, r) + (Essential(w, r) != null ? 0.3f : 0f);
                break;
            }
            default:
                p = 1f;
                break;
        }
        if (o.Kind == WorkKind.Jettison && w.Policies["jettison"] == 2) p += 0.15f; // v13.2 방침(사출: 적극)
        return p + 0.15f * o.Rejections;
    }

    /// <summary>
    /// 이 방이 없으면 배가 버티지 못하는 까닭: 다른 곳에 대신할 것이 없는 핵심 설비(배전반·원자로 …), 하나뿐인 외부 해치.
    /// 무서워도 되찾아야 하는 방 (v8).
    /// </summary>
    public static string? Essential(World w, Room r)
    {
        foreach (var f in r.Furniture)
        {
            // 파손돼도 다시 짜려면 그 방이 있어야 한다
            if (f.Machine is not Machine m || !m.Spec.Critical) continue;
            bool spare = w.Ship.Furniture.Any(o => o.Type == f.Type && o.Room != r && !o.Room.Detached
                                                   && o.Machine is Machine om && !om.Has(FaultKind.Wrecked));
            if (!spare) return f.Label;
        }
        if (w.Ship.Doors.Any(d => d.IsExternal && !d.Removed && (d.RoomA == r || d.RoomB == r))
            && !w.Ship.Doors.Any(d => d.IsExternal && !d.Removed && d.RoomA != r && d.RoomB != r && !(d.RoomA?.Detached ?? false) && !(d.RoomB?.Detached ?? false)))
            return "외부 해치";
        return null;
    }

    /// <summary>그 방이 얼마나 아쉬운지 0~0.6 (핵심 설비, 작업대·재배대, 침대, 실린 물자).</summary>
    public static float RoomValue(World w, Room r)
    {
        float v = 0f;
        foreach (var f in r.Furniture)
        {
            if (f.Machine is Machine m)
            {
                if (m.Spec.Critical && !m.Has(FaultKind.Wrecked)) v += 0.2f;
                else if (f.Type is FurnitureType.Workbench or FurnitureType.GrowBed or FurnitureType.WaterRecycler or FurnitureType.Refinery
                         or FurnitureType.Collector or FurnitureType.DroneDock or FurnitureType.SuitLocker) v += 0.08f;
            }
            if (f.Type == FurnitureType.Bed && f.Owner != null) v += 0.04f;
            if (f.Storage is Inventory inv) v += MathF.Min(0.15f, inv.Total / 200f);
        }
        return MathF.Min(0.6f, v);
    }

    /// <summary>이 사람의 찬반 (0보다 크면 찬성)과 그 까닭.</summary>
    public static (float support, string why) Opinion(World w, CrewMember c, WorkOrder o, float pressure)
    {
        var t = c.Traits;
        float s = pressure - 0.5f;
        var terms = new List<(float v, string why)>();
        switch (o.Kind)
        {
            case WorkKind.SealOffRoom:
            {
                var r = o.Target.Room!;
                terms.Add((-0.3f * t.Bravery, "아직 막을 수 있다"));
                float fear = c.Memory.FearOf(r);
                if (fear > 0.05f) terms.Add((0.35f * fear, $"거기가 무섭다 ({c.Memory.FearCause[r.Id]})"));
                if (Stake(c, r)) terms.Add((-0.25f, $"{StakeName(c, r)}다"));
                if (Essential(w, r) is string vital) terms.Add((-0.3f, $"{vital} 없이는 버틸 수 없다"));
                terms.Add((0.1f * t.Calm, "잃을 건 잃어야 한다"));
                terms.Add((MathF.Max(0f, pressure - 0.6f), o.Detail.Contains("실링폼") ? "막을 실링폼이 없다" : "너무 오래 새고 있다"));
                break;
            }
            case WorkKind.ReopenRoom:
            {
                var r = o.Target.Room!;
                float fear = c.Memory.FearOf(r);
                if (fear > 0.05f) terms.Add((-0.6f * fear, $"다시 들어가기 싫다 ({c.Memory.FearCause[r.Id]})"));
                if (Stake(c, r)) terms.Add((0.25f, $"{StakeName(c, r)}로 돌아가고 싶다"));
                terms.Add((0.1f * t.Bravery, "되찾을 수 있으면 되찾자"));
                terms.Add((-0.1f * (1f - t.Bravery), "또 샐지 모른다"));
                break;
            }
            case WorkKind.Cannibalize:
            {
                var donor = o.Target.Furniture!;
                if (Stake(c, donor.Room)) terms.Add((-0.25f, $"{Ko.EunNeun(donor.Label)} 내가 돌보는 설비다"));
                if (donor.Machine is Machine dm && c.SkillLevel(dm.Spec.Skill) > 0.6f) terms.Add((-0.1f, "뜯으면 다시는 못 쓴다"));
                terms.Add((0.1f * t.Calm, "살릴 것부터 살려야 한다"));
                terms.Add((MathF.Max(0f, pressure - 0.6f), "급한 설비가 멈춰 있다"));
                break;
            }
            case WorkKind.ChangeCourse:
            {
                bool intoDebris = (ZoneKind)o.Circuit == ZoneKind.Debris;
                if (intoDebris)
                {
                    terms.Add((0.3f * (t.Bravery - 0.5f), t.Bravery > 0.5f ? "원료를 채워야 한다" : "잔해 속으로 들어가는 건 무섭다"));
                    if (c.Role is CrewRole.Technician or CrewRole.Engineer) terms.Add((0.15f, "금속판이 바닥나 간다"));
                    float fear = w.Ship.Rooms.Max(r => c.Memory.FearOf(r));
                    if (fear > 0.2f) terms.Add((-0.3f * fear, "운석을 또 맞고 싶지 않다"));
                }
                else
                {
                    terms.Add((0.1f + 0.1f * w.Propulsion.HitsThisZone, $"잔해 지대에서 {w.Propulsion.HitsThisZone}번 맞았다"));
                    if (c.Role == CrewRole.Technician && ItemKinds.RawKinds.Sum(k => w.Board.Have(k)) < 20) terms.Add((-0.15f, "원료를 조금만 더"));
                }
                if (c.Role == CrewRole.Pilot) terms.Add((0.1f, "조종은 내 몫이다"));
                terms.Add((MathF.Max(0f, pressure - 0.6f), "상황이 그렇다"));
                break;
            }
            case WorkKind.Recycle:
            {
                var donor = o.Target.Furniture!;
                if (Stake(c, donor.Room)) terms.Add((-0.2f, $"{Ko.EunNeun(donor.Label)} 내 근무지에 있던 설비다"));
                if (c.Role is CrewRole.Technician or CrewRole.Engineer) terms.Add((0.15f, "고철로 두느니 금속판으로"));
                terms.Add((0.05f + MathF.Max(0f, pressure - 0.6f), $"금속판 {w.Board.Have(ItemKind.Plate)}개"));
                break;
            }
            case WorkKind.ShedLoad:
            {
                int i = o.Circuit;
                var mine = w.Ship.Rooms.FirstOrDefault(r => r.Circuit == i && Stake(c, r));
                if (mine != null) terms.Add((-0.2f, $"{Ko.IGa(mine.Name)} 캄캄해진다"));
                // v9.2: 주 컴퓨터가 그 회로에 있으면 자동화가 꺼진다 (기관사·전기 기사가 가장 싫어한다)
                if (w.Automation.ComputerBody is Furniture comp && comp.Room.Circuit == i && w.Automation.MainOnline)
                    terms.Add((c.Role is CrewRole.Engineer or CrewRole.Electrician or CrewRole.Pilot ? -0.35f : -0.15f, "주 컴퓨터가 꺼진다 — 격벽을 손으로 닫아야 한다"));
                terms.Add((0.1f * t.Calm, "배터리를 아껴야 한다"));
                terms.Add((MathF.Max(0f, pressure - 0.6f), $"배터리 {w.Power.BatteryPercent * 100:0}%"));
                break;
            }
            case WorkKind.RepurposeRoom:
            case WorkKind.BuildWorkshop:
            {
                var r = o.Target.Room!;
                if (c.Stations.Contains(r.Type)) terms.Add((-0.3f, $"{Ko.EunNeun(r.Name)} 내 근무지다"));
                bool homeless = c.Bed != null && c.Bed.Room.Abandoned;
                if (homeless && o.Kind == WorkKind.RepurposeRoom) terms.Add((0.3f, "바닥에서 자는 건 지쳤다"));
                if (o.Kind == WorkKind.BuildWorkshop && c.Role is CrewRole.Technician or CrewRole.Engineer) terms.Add((0.15f, "작업대 없이는 아무것도 못 고친다"));
                terms.Add((0.05f, "당장 필요하다"));
                break;
            }
            case WorkKind.Upgrade:
            {
                var kind = o.Upgrade ?? UpgradeKind.Mk3;
                float interest = 0.6f * Evolution.Interest(c, kind);
                if (interest > 0f) terms.Add((interest, "내 분야다"));
                var room = o.Target.CurrentRoom;
                float fear = c.Memory.FearOf(room);
                if (fear > 0.05f) terms.Add((0.35f * fear, $"{room!.Name}에서 겪은 일을 다시 겪고 싶지 않다"));
                if (o.Target.Furniture?.Machine is Machine mm && Stake(c, mm.Body.Room)) terms.Add((0.15f, $"{Ko.EunNeun(mm.Name)} 내가 돌보는 설비다"));
                if (kind == UpgradeKind.Partition && room != null)
                {
                    // v10.2: 넓은 방에서 모이던 사람은 방이 둘로 갈리는 게 싫고, 그 방이 감압됐을 때 거기 있던 사람은 반긴다
                    if (room.Type is RoomType.Mess or RoomType.Lounge or RoomType.Galley && t.Sociability > 0.55f)
                        terms.Add((-0.3f * t.Sociability, "넓은 방이 좁아진다"));
                    if (c.Stations.Contains(room.Type)) terms.Add((-0.1f, "내 일터가 둘로 갈린다"));
                }
                if (kind == UpgradeKind.RemovePartition && room?.SplitFrom is Room outer)
                {
                    // v10.12: 넓은 방이 그리운 사람 ↔ 칸막이 덕을 본(또는 그 방이 무서운) 사람
                    if (outer.Type is RoomType.Mess or RoomType.Lounge or RoomType.Galley && t.Sociability > 0.5f)
                        terms.Add((0.35f * t.Sociability, "넓은 방으로 돌아가자"));
                    float f2 = MathF.Max(c.Memory.FearOf(outer), c.Memory.FearOf(room));
                    if (f2 > 0.05f) terms.Add((-0.5f * f2, "또 통째로 감압된다"));
                    if (c.Role is CrewRole.Engineer or CrewRole.Technician) terms.Add((-0.12f, "칸막이가 절반을 지켜 줬다"));
                    if (c.Stations.Contains(outer.Type)) terms.Add((0.12f, "내 일터가 하나로 이어진다"));
                }
                if (kind == UpgradeKind.Relocate && o.Target.Furniture is Furniture mf)
                {
                    // v10.12: 뚫렸던 방이 무서운 사람은 반기고, 그 방이 일터인 사람은 설비가 빠지는 게 싫다
                    float f3 = c.Memory.FearOf(mf.Room);
                    if (f3 > 0.05f) terms.Add((0.4f * f3, $"{Ko.EunNeun(mf.Room.Name)} 또 뚫린다"));
                    if (c.Stations.Contains(mf.Room.Type)) terms.Add((-0.15f, "내 일터에서 설비가 빠진다"));
                    if (mf.Machine?.Spec.Critical == true) terms.Add((0.15f, "핵심 설비는 안쪽에 둬야 한다"));
                }
                if (kind == UpgradeKind.AddGrowBed)
                {
                    // v10.1: 배고픔과 목마름의 저울질 — 재배대는 먹을 것을 늘리지만 물을 더 먹는다
                    if (c.Needs.Food < 0.3f) terms.Add((0.25f, "배가 고프다"));
                    if (Evolution.WaterShort(w))
                        terms.Add((-(c.Role is CrewRole.Engineer or CrewRole.Technician ? 0.5f : 0.3f) * (1.5f - w.Water.Level / w.Water.Capacity), "물이 모자란데 재배대가 물을 더 먹는다"));
                }
                // 긴장한 사람, 겁 많은 사람은 재료를 비상용으로 남겨 두고 싶어 한다
                if (c.Memory.Trauma > 0.08f) terms.Add((-0.6f * c.Memory.Trauma, "재료는 비상용으로 남겨야 한다"));
                float slack = Evolution.Slack(w, Evolution.Cost(o));
                if (slack < 1f) terms.Add((-0.15f * (1f - MathF.Max(0f, slack)) * (1.2f - t.Bravery), "재료를 너무 많이 쓴다"));
                terms.Add((0.15f * (t.Diligence - 0.5f), t.Diligence >= 0.5f ? "할 수 있을 때 해 두자" : "지금도 괜찮다"));
                // 저마다 먼저 고치고 싶은 곳이 있다 (내가 무서워하는 방, 내 분야)
                if (Evolution.Favorite(w, c) is UpgradePlan fav && fav.Target.Key + ":" + fav.Kind != o.Target.Key + ":" + o.Upgrade
                    && Evolution.PersonalScore(w, c, fav) > Evolution.PersonalScore(w, c, o) + 0.25f)
                    terms.Add((-0.15f, $"차라리 {Evolution.ShortTitle(fav)}부터"));
                break;
            }
        }
        switch (o.Kind)
        {
            case WorkKind.Jettison:
            {
                var r = o.Target.Room!;
                var crit = r.Furniture.FirstOrDefault(f => f.Machine is Machine m && m.Spec.Critical && !m.Has(FaultKind.Wrecked));
                if (crit != null) terms.Add((-0.45f, $"{Ko.EulReul(crit.Label)} 버릴 수는 없다"));
                if (Stake(c, r)) terms.Add((-0.25f, $"{StakeName(c, r)}다"));
                terms.Add((-0.25f * t.Bravery, o.Detail.Contains("불") ? "아직 끌 수 있다" : "아직 붙잡을 수 있다"));
                float fear = c.Memory.FearOf(r);
                if (fear > 0.05f) terms.Add((0.3f * fear, $"거기가 무섭다 ({c.Memory.FearCause[r.Id]})"));
                if (c.Memory.Trauma > 0.08f) terms.Add((0.4f * c.Memory.Trauma, "뜯겨 나가며 옆방까지 찢기는 건 막아야 한다"));
                terms.Add((0.12f * t.Calm, "잃을 건 잃어야 한다"));
                terms.Add((MathF.Max(0f, pressure - 0.7f), o.Detail.Contains("불") ? "불이 옆방으로 번진다" : "뜯겨 나가면 이웃 벽까지 찢긴다"));
                break;
            }
            case WorkKind.IsolateMain:
            {
                var net = w.Piping;
                float battery = w.Power.BatteryPercent;
                // 기관사는 원자로를 세우는 게 싫다 (배터리로 버텨야 하고, 다시 켜는 것도 일이다)
                if (c.Role is CrewRole.Engineer or CrewRole.Electrician)
                    terms.Add((battery < 0.5f ? -0.3f : -0.12f, $"원자로를 세우면 배터리 {battery * 100:0}%로 버텨야 한다"));
                if (c.Stations.Contains(RoomType.Cooling) || c.Stations.Contains(RoomType.Reactor))
                    terms.Add((0.15f, "새는 채로 돌리다 펌프까지 망가진다"));
                terms.Add((0.15f * (1f - t.Bravery), "증기에 데기 전에 잠가야 한다"));
                terms.Add((-0.1f * t.Bravery, "냉각수를 부어 가며 버티자"));
                if (net.CoolantFraction < 0.7f) terms.Add((0.3f * (0.7f - net.CoolantFraction) / 0.4f + 0.1f, $"냉각수가 {net.CoolantFraction * 100:0}% 남았다"));
                if (w.Water.Level < 80f) terms.Add((0.15f, "부을 물도 모자라다"));
                // 고칠 방법이 있나: 압력이 걸린 채로 막을 수 있으면 굳이 세우지 않는다, 잠가도 고칠 재료가 없으면 세우기 싫다
                var sp = o.Target.Pipe!;
                int sealant = w.Ship.CountStored(ItemKind.Sealant), plates = w.Ship.CountStored(ItemKind.Plate);
                if (!sp.Severed && sp.LeakRate < 14f && sealant > 0) terms.Add((-0.2f, "세우지 않고도 실링폼으로 막을 수 있다"));
                if (plates < 2 && sealant == 0) terms.Add((-0.3f, "잠가도 고칠 재료가 없다"));
                terms.Add((MathF.Max(0f, pressure - 0.75f), "새는 게 너무 크다"));
                break;
            }
            case WorkKind.Ration:
                RationTerms(w, c, terms, pressure); // v10.11
                break;
            case WorkKind.Distress:
            case WorkKind.AnswerSignal:
                CommsTerms(w, c, o, terms, pressure); // v11.2
                break;
            case WorkKind.Brownout:
            {
                bool hungry = w.Ship.FurnitureOf(FurnitureType.GrowBed).Any(f => !f.Room.Abandoned && !f.Machine!.Powered);
                bool hurt = w.Crew.Any(x => !x.Dead && (x.Down || x.Vitals.Injury > 0.25f));
                if (c.Role == CrewRole.Botanist || c.Stations.Contains(RoomType.Galley)) terms.Add((hungry ? 0.3f : 0.1f, "재배대가 꺼지면 굶는다"));
                if (c.Role is CrewRole.Engineer or CrewRole.Electrician) terms.Add((0.15f, "있는 전기를 골라 써야 한다"));
                if (c.Role == CrewRole.Medic) terms.Add((hurt ? -0.25f : -0.05f, hurt ? "치료 침대를 끌 수는 없다" : "치료 침대는 켜 두고 싶다"));
                terms.Add((-0.15f * (1f - t.Bravery), "산소 발생기를 하나 끄는 건 불안하다"));
                terms.Add((0.1f * t.Calm, "있는 만큼으로 버티자"));
                terms.Add((MathF.Max(0f, pressure - 0.75f), "전기가 모자란 지 오래다"));
                break;
            }
            case WorkKind.PlanRepipe:
            {
                var sp = o.Target.Pipe!;
                float battery = w.Power.BatteryPercent;
                if (c.Role is CrewRole.Engineer or CrewRole.Electrician)
                    terms.Add((battery < 0.7f ? -0.25f : -0.05f, $"원자로를 세우면 배터리 {battery * 100:0}%로 버텨야 한다"));
                if (c.Stations.Contains(RoomType.Cooling) || c.Stations.Contains(RoomType.Reactor)) terms.Add((0.2f, "임시 밀봉이 또 터지기 전에"));
                if (sp.PatchFails > 0) terms.Add((0.12f * MathF.Min(3, sp.PatchFails), $"임시 밀봉이 {sp.PatchFails}번 다시 터졌다"));
                terms.Add((0.12f * t.Diligence, "땜질은 땜질일 뿐이다"));
                terms.Add((-0.12f * (1f - t.Diligence), "멀쩡히 돌고 있는데 굳이 세우나"));
                break;
            }
            case WorkKind.LimpMain:
            {
                float battery = w.Power.BatteryPercent;
                if (c.Role is CrewRole.Engineer or CrewRole.Electrician) terms.Add((0.25f, "원자로 없이는 오래 못 버틴다"));
                terms.Add((-0.2f * (1f - t.Bravery), "또 증기가 뿜어진다"));
                if (battery < 0.4f) terms.Add((0.3f, $"배터리가 {battery * 100:0}% 남았다"));
                if (w.Water.Level < 150f) terms.Add((-0.2f, $"부을 물이 {w.Water.Level:0}L뿐이다"));
                terms.Add((0.1f * t.Calm, "새는 걸 알고 돌리면 된다"));
                break;
            }
            case WorkKind.Retrieve:
            case WorkKind.RestoreRoom:
            {
                var r = o.Target.Room!;
                if (Stake(c, r)) terms.Add((0.25f, $"{StakeName(c, r)}다"));
                float fear = c.Memory.FearOf(r);
                if (fear > 0.05f) terms.Add((-0.4f * fear, $"다시 들어가기 싫다 ({c.Memory.FearCause[r.Id]})"));
                if (r.Jettisons > 0 && o.Kind == WorkKind.Retrieve) terms.Add((-0.15f, "우리가 일부러 떼어 낸 방이다"));
                if (Essential(w, r) is string need) terms.Add((0.45f + 0.2f * t.Calm, $"{need} 없이는 버틸 수 없다"));
                if (w.Crew.Where(x => x.Aboard?.Room == r && !x.Dead).ToList() is { Count: > 0 } onboard)
                    terms.Add((1.0f + (onboard.Any(x => c.AffinityTo(x) > 0.3f) ? 0.3f : 0f), $"{Ko.IGa(string.Join("·", onboard.Select(x => x.Name)))} 타고 있다"));
                float wreck = StructureSystem.WreckScore(w, r);
                if (wreck > 0.4f) terms.Add((-0.3f * wreck * (1.2f - t.Bravery), "너무 망가졌다 — 부품만 뜯자"));
                if (c.Memory.Trauma > 0.08f) terms.Add((-0.3f * c.Memory.Trauma, "또 떨어져 나갈 것이다"));
                terms.Add((0.12f * t.Diligence, "되찾을 수 있으면 되찾자"));
                break;
            }
        }
        ExtraTerms(w, c, o, terms, pressure); // v13.2 가치관 · 경험 · 상태 · 죄책감
        foreach (var (v, _) in terms) s += v;
        // 결정하는 사람과 가까우면 그 사람 편을 든다
        var leader = o.Decider;
        if (leader != null && leader != c) s += 0.1f * c.AffinityTo(leader);
        string reason = s > 0f
            ? terms.Where(x => x.v > 0f).OrderByDescending(x => x.v).Select(x => x.why).DefaultIfEmpty("해야 한다").First()
            : terms.Where(x => x.v < 0f).OrderBy(x => x.v).Select(x => x.why).DefaultIfEmpty("아직 이르다").First();
        return (s, reason);
    }

    /// <summary>작업 목록을 훑을 때마다: 결정이 필요한 일을 심의에 올리고, 때가 된 것은 결정한다.</summary>
    internal static void Review(World w, IEnumerable<WorkOrder> orders)
    {
        var h = w.History;
        foreach (var o in orders)
        {
            if (!Needs(o.Kind) || o.Closed || o.Assignee != null || o.Drone != null) continue;
            if (o.Decision == DecisionState.Approved) continue;
            if (o.Decision == DecisionState.Rejected)
            {
                if (w.Tick < o.BlockedUntil) continue;
                o.Decision = DecisionState.None;
            }
            if (o.Kind == WorkKind.ShedLoad && w.Tick < h.ShedApprovedUntil)
            {
                o.Decision = DecisionState.Approved;
                continue;
            }
            if (h.ApprovedUntil.TryGetValue(o.Key, out var until) && w.Tick < until)
            {
                // 앞서 정한 일이 잠깐 목록에서 빠졌다 돌아왔다 — 다시 모이지 않는다
                o.Decision = DecisionState.Approved;
                continue;
            }
            float pressure = Pressure(w, o);
            if (o.Decision == DecisionState.None)
            {
                bool alone = pressure >= 0.9f;
                var decider = Judge(w, alone);
                if (decider == null) { o.Decision = DecisionState.Approved; continue; }
                o.Decider = decider;
                o.Alone = alone;
                // v13.2 현장 협의: 위기 중(조가 짜여 있으면) 지휘자·조장들이 무전으로 짧게
                o.Field = !alone && w.Command.Active;
                float calm = alone ? decider.Traits.Calm : w.Crew.Where(c => c.CanAct).Select(c => c.Traits.Calm).DefaultIfEmpty(0.5f).Average();
                o.DecideAt = w.Tick + (alone ? SimTime.Minutes(2f + 10f * (1f - calm)) : o.Field ? SimTime.Minutes(1f + 1f * (1f - calm)) : SimTime.Minutes(15f + 25f * (1f - calm)));
                o.Decision = DecisionState.Pending;
                continue;
            }
            // v13.2 기다리는 사이 위기가 왔다 — 모일 새 없이 현장 협의(무전)로
            if (o.Decision == DecisionState.Pending && !o.Alone && !o.Field && w.Command.Active)
            {
                o.Field = true;
                o.DecideAt = Math.Min(o.DecideAt, w.Tick + SimTime.Minutes(2));
            }
            if (o.Decision == DecisionState.Pending && w.Tick >= o.DecideAt) Decide(w, o, pressure);
        }
    }

    /// <summary>v13.2 정기 회의에서 미뤄 둔 결정을 모인 사람들로 바로 정한다.</summary>
    internal static void DecideNow(World w, WorkOrder o, List<CrewMember> attendees, MeetingRecord rec)
    {
        if (attendees.FirstOrDefault(c => c.Id == rec.Chair) is CrewMember chair) o.Decider = chair;
        Decide(w, o, Pressure(w, o), attendees, rec);
    }

    private static void Decide(World w, WorkOrder o, float pressure, List<CrewMember>? attendees = null, MeetingRecord? rec = null)
    {
        var h = w.History;
        var decider = o.Decider is { CanAct: true } d0 ? d0 : Judge(w, o.Alone);
        if (decider == null) { o.Decision = DecisionState.Approved; return; }
        o.Decider = decider;
        var awake = w.Crew.Where(c => c.CanAct && (c.IsAwake || c == decider)).ToList();
        List<CrewMember> voters;
        if (attendees != null) { voters = attendees.Where(c => c.CanAct).ToList(); if (!voters.Contains(decider)) voters.Add(decider); }
        else if (o.Alone) voters = new List<CrewMember> { decider };
        else if (o.Field)
        {
            // 현장 협의: 지휘자 · 조를 맡은 사람 (무전)
            var cmd = w.Command;
            var ids = cmd.Teams.Where(t => t.Kind != TeamKind.Reserve).Select(t => t.Worker).Append(cmd.Commander?.Id ?? -1).Append(cmd.CaptainId).ToHashSet();
            voters = awake.Where(c => ids.Contains(c.Id) || c == decider).ToList();
        }
        else voters = awake;
        // v13.2 회의록 한 줄 (안건 · 발언 · 표)
        var kind = attendees != null ? rec!.Kind : o.Alone ? MeetingKind.Emergency : o.Field ? MeetingKind.Field : MeetingKind.AdHoc;
        var item = new AgendaItem { Title = o.Title, Topic = "order:" + o.Kind };
        var final = new Dictionary<CrewMember, (float s, string why)>();
        Evolution.Hold(w); // v17.7 의견을 듣는 사이 개조 후보는 한 번만
        try
        {
            if (voters.Count >= 3) w.Meetings.Debate(voters, c => Opinion(w, c, o, pressure), c => c.SkillLevel(o.Skill), item, decider, final);
            else foreach (var c in voters) { var op = Opinion(w, c, o, pressure); final[c] = op; item.Votes.Add((c.Id, op.support > 0f, op.why)); item.Speeches.Add(new Speech { Who = c.Id, For = op.support > 0f, Text = Persona.Say(c, op.why) }); }
        }
        finally { Evolution.Release(); }
        var opinions = voters.Select(c => (who: c, op: (support: final[c].s, why: final[c].why))).ToList();
        if (rec != null) rec.Items.Add(item);
        else
        {
            var mr = new MeetingRecord
            {
                Id = w.Meetings.Minutes.Count + 1, Kind = kind, Tick = w.Tick, End = w.Tick, Chair = decider.Id,
                Venue = o.Alone ? "그 자리" : o.Field ? "무전" : "깨어 있는 사람끼리", Attendees = voters.Select(c => c.Id).ToList(),
            };
            mr.Items.Add(item);
            w.Meetings.Minutes.Add(mr);
            if (w.Meetings.Minutes.Count > 60) w.Meetings.Minutes.RemoveAt(0);
        }
        var yes = opinions.Where(x => x.op.support > 0f).ToList();
        var no = opinions.Where(x => x.op.support <= 0f).ToList();
        // 혼자 정할 때도 깨어 있는 사람은 한마디 한다: 크게 반대하는 사람은 기록에 남고 서운함이 남는다
        var objections = o.Alone
            ? awake.Where(c => c != decider).Select(c => (who: c, op: Opinion(w, c, o, pressure))).Where(x => x.op.support < -0.1f).ToList()
            : new List<(CrewMember who, (float support, string why) op)>();
        var mine = opinions.First(x => x.who == decider).op;
        bool forced = pressure >= 1.25f;
        bool approve = forced || (o.Alone ? mine.support > 0f : yes.Count > no.Count || (yes.Count == no.Count && mine.support > 0f));
        string title = o.Title;

        if (approve)
        {
            o.Decision = DecisionState.Approved;
            h.DecisionsMade++;
            if (o.Kind == WorkKind.ShedLoad) h.ShedApprovedUntil = w.Tick + SimTime.Hours(12);
            // 결정만 하는 일(사출·견인·본관 잠그기 …)은 결정할 때 실행되니 기억하지 않는다
            if (o.Kind is not (WorkKind.Jettison or WorkKind.Retrieve or WorkKind.RestoreRoom or WorkKind.IsolateMain or WorkKind.LimpMain or WorkKind.PlanRepipe))
                h.ApprovedUntil[o.Key] = w.Tick + SimTime.Hours(12);
            string text;
            if (o.Alone)
            {
                text = mine.support > 0f ? $"{decider.Name}의 판단: {title} — {mine.why}"
                    : $"{decider.Name}의 판단: 내키지 않지만 {title} — 선택의 여지가 없다 ({mine.why}에도)";
                if (objections.Count > 0) text += $" · 반대: {string.Join("·", objections.Select(x => x.who.Name))} ({objections[0].op.why})";
                no.AddRange(objections);
            }
            else
            {
                text = $"{MeetingLabel(kind, decider)}: {title} — 찬성 {yes.Count} · 반대 {no.Count}";
                if (no.Count > 0) text += $" ({string.Join("·", no.Select(x => x.who.Name))}: {no[0].op.why})";
                else if (yes.Count > 0) text += $" ({yes.OrderByDescending(x => x.op.support).First().op.why})";
                if (item.FlippedBy != null) text += $" · {item.FlippedBy}의 설득으로 뒤집혔다";
            }
            o.Verdict = text;
            item.Passed = true;
            item.Outcome = "승인";
            w.Meetings.Record(title, "order:" + o.Kind, o.Target.CurrentRoom?.Id ?? -1, decider, yes.Select(x => x.who).ToList(), no.Select(x => x.who).ToList(), "");
            w.Meetings.Split(yes.Select(x => x.who).ToList(), no.Select(x => x.who).ToList());
            // 반대했던 사람은 서운하다 (결정한 사람에게)
            foreach (var (who, op) in no)
            {
                if (who == decider) continue;
                who.Needs.Stress = MathF.Min(1f, who.Needs.Stress + 0.03f);
                who.ChangeAffinity(decider, -0.03f);
            }
            var involved = new List<CrewMember> { decider };
            involved.AddRange(no.Select(x => x.who).Where(c => c != decider));
            h.Add(w, HistoryKind.Decision, text, o.Target.CurrentRoom, involved, log: true);
            if (pressure >= 0.9f) w.Board.RequestScan();
            // v8: 결정만 하는 일은 여기서 실행된다
            switch (o.Kind)
            {
                case WorkKind.Jettison:
                    w.Board.BeginJettison(o);
                    break;
                case WorkKind.Retrieve:
                    if (o.Target.Room!.Fragment is Fragment f) f.RetrieveApproved = true; // 견인 드론이 맡는다
                    break;
                case WorkKind.RestoreRoom:
                    o.Target.Room!.Restoring = true;
                    w.Board.Close(o);
                    w.Board.RequestScan();
                    break;
                case WorkKind.IsolateMain:
                    o.Target.Pipe!.IsolateApproved = true; // 밸브 잠그기가 올라온다
                    w.Board.Close(o);
                    w.Board.RequestScan();
                    break;
                case WorkKind.PlanRepipe:
                    o.Target.Pipe!.PlannedReplace = true; // 밸브 잠그기 → 새 관 → 밸브 열기 → 원자로 재기동
                    o.Target.Pipe!.IsolateApproved = true;
                    w.Board.Close(o);
                    w.Board.RequestScan();
                    break;
                case WorkKind.LimpMain:
                    o.Target.Pipe!.LimpApproved = true; // 밸브 열기가 올라온다
                    w.Board.Close(o);
                    w.Board.RequestScan();
                    break;
            }
        }
        else
        {
            o.Decision = DecisionState.Rejected;
            o.Rejections++;
            h.DecisionsRejected++;
            float hold = o.Kind == WorkKind.Upgrade ? 24f : o.Alone ? 0.75f : 3f;
            if (o.Kind == WorkKind.RestoreRoom && o.Rejections >= 2 && Essential(w, o.Target.Room!) == null)
            {
                // 두 번 부결: 잔해로 두고 쓸 만한 것만 뜯어 쓴다
                var wr = o.Target.Room!;
                wr.Wreck = true;
                MarkLog.Add(wr.Marks, w.Tick, "잔해로 두기로 했다");
                h.Add(w, HistoryKind.Structure, $"다시 붙인 {Ko.EulReul(wr.Name)} 되살리지 않기로 했다 — 잔해로 두고 부품과 물자만 꺼내 쓴다", wr, log: true);
            }
            o.BlockedUntil = w.Tick + SimTime.Hours(hold);
            string text = o.Alone ? $"{Ko.IGa(decider.Name)} {Ko.EulReul(title)} 미뤘다 — {mine.why}"
                : $"{MeetingLabel(kind, decider)}: {title} 부결 — 찬성 {yes.Count} · 반대 {no.Count} ({no.OrderBy(x => x.op.support).First().op.why})"
                  + (item.FlippedBy != null ? $" · {item.FlippedBy}의 설득으로 뒤집혔다" : "");
            o.Verdict = text;
            item.Outcome = "부결";
            w.Meetings.Split(yes.Select(x => x.who).ToList(), no.Select(x => x.who).ToList());
            foreach (var (who, _) in yes) who.Needs.Stress = MathF.Min(1f, who.Needs.Stress + 0.02f);
            if (o.Kind == WorkKind.Upgrade)
            {
                h.UpgradeVetoedUntil[o.Target.Key + ":" + o.Upgrade] = w.Tick + SimTime.Hours(hold * 3);
                h.PlannedUpgrade = null;
            }
            var involved = new List<CrewMember> { decider };
            involved.AddRange(no.Select(x => x.who).Where(c => c != decider));
            h.Add(w, HistoryKind.Decision, text, o.Target.CurrentRoom, involved, log: true);
        }
    }

    private static string MeetingLabel(MeetingKind k, CrewMember chair) => k switch
    {
        MeetingKind.Field => $"현장 협의 (무전 · {chair.Name})",
        MeetingKind.Regular or MeetingKind.Review => $"{MeetingSystem.KindName(k)} ({chair.Name} 주재)",
        _ => $"회의 ({chair.Name} 주재)",
    };

    /// <summary>v13.2 일마다 가치관이 기우는 쪽 (안전 · 효율 · 사람 · 규칙 · 자유 — 승인하는 쪽이 +).</summary>
    private static float[]? Axis(WorkOrder o) => o.Kind switch
    {
        WorkKind.SealOffRoom => new[] { 1f, 0.5f, -0.5f, 0.5f, 0f },
        WorkKind.ReopenRoom => new[] { -0.5f, 1f, 0.5f, 0f, 0.5f },
        WorkKind.Cannibalize => new[] { 0.3f, 0.5f, 0f, -0.5f, 0.5f },
        WorkKind.ShedLoad or WorkKind.Brownout => new[] { 0.5f, 0.5f, -0.5f, 0.3f, -0.3f },
        WorkKind.RepurposeRoom or WorkKind.BuildWorkshop => new[] { 0f, 1f, 0f, 0f, 0.3f },
        WorkKind.Recycle => new[] { 0f, 1f, 0f, 0f, 0f },
        WorkKind.Upgrade => new[] { 0.5f, 0.5f, 0f, 0f, 0f },
        WorkKind.Jettison => new[] { 1f, -0.5f, 0f, 0.3f, 0f },
        WorkKind.Retrieve or WorkKind.RestoreRoom => new[] { -0.5f, 1f, 0.5f, 0f, 0.3f },
        WorkKind.IsolateMain => new[] { 1f, -0.5f, 0f, 0.5f, 0f },
        WorkKind.LimpMain => new[] { -1f, 1f, 0f, -0.3f, 0.5f },
        WorkKind.PlanRepipe => new[] { 0.5f, -0.3f, 0f, 0.5f, 0f },
        WorkKind.ChangeCourse => (ZoneKind)o.Circuit == ZoneKind.Debris ? new[] { -0.5f, 1f, 0f, 0f, 0.5f } : new[] { 1f, -0.3f, 0.3f, 0f, 0f },
        WorkKind.Ration => new[] { 0.5f, 0.3f, -0.5f, 0.5f, -1f },
        WorkKind.Distress => new[] { 0.5f, 0f, 1f, 0f, 0f },
        WorkKind.AnswerSignal => new[] { -0.5f, 0f, 1f, 0f, 0.5f },
        _ => null,
    };

    private static readonly (string pro, string con)[] ValueVoice =
    {
        ("그게 더 안전하다", "위험을 늘린다"),
        ("배가 더 잘 돈다", "손해가 크다"),
        ("사람을 지키는 길이다", "사람에게 짐을 지운다"),
        ("절차대로다", "절차에 없는 일이다"),
        ("현장이 알아서 할 여지가 생긴다", "사람을 묶어 둔다"),
    };

    /// <summary>v13.2 의견의 근거 확장: 가치관 · 경험(베테랑) · 상태(지침·스트레스) · 죄책감.</summary>
    private static void ExtraTerms(World w, CrewMember c, WorkOrder o, List<(float v, string why)> terms, float pressure)
    {
        var axis = Axis(o);
        if (axis != null)
        {
            float a = axis[(int)c.Value];
            if (MathF.Abs(a) > 0.01f) terms.Add((0.1f * a, a > 0f ? ValueVoice[(int)c.Value].pro : ValueVoice[(int)c.Value].con));
            float g = w.Meetings.Guilt(c);
            if (g > 0.1f && MathF.Abs(axis[0]) > 0.01f) terms.Add((0.2f * g * MathF.Sign(axis[0]), "다시는 사람을 잃고 싶지 않다"));
        }
        // 베테랑: 미루면 커진다는 걸 안다
        if (c.Stats.Emergencies >= 10 && pressure > 0.6f) terms.Add((0.08f, "겪어 봐서 안다 — 미루면 커진다"));
        // 지치고 예민하면 일을 더 벌이기 싫다
        if (o.Kind is WorkKind.Upgrade or WorkKind.RepurposeRoom or WorkKind.BuildWorkshop or WorkKind.PlanRepipe)
        {
            if (c.Needs.Stress > 0.65f) terms.Add((-0.06f, "지쳤다 — 일을 더 벌이지 말자"));
            if (c.Needs.Rest < 0.25f) terms.Add((-0.05f, "잠도 못 잤다"));
        }
    }
}
