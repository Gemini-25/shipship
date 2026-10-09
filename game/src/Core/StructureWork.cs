using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>선체 밖 일의 결과 (드론이 하든 EVA 승무원이 하든 같다).</summary>
public static class ExternalWork
{
    public static void Apply(World w, WorkOrder o, float skill, CrewMember? crew, Drone? drone)
    {
        string by = drone?.Name ?? (crew != null ? crew.Name + "(EVA)" : "?");
        var who = crew != null ? new[] { crew } : null;
        var h = w.History;
        switch (o.Kind)
        {
            case WorkKind.RepairJoint:
            {
                var j = o.Target.Joint!;
                bool wasBroken = j.Broken;
                float before = j.Strength;
                j.MaxStrength = MathF.Max(0.55f, j.MaxStrength - (wasBroken ? 0.08f : 0.04f));
                j.Strength = MathF.Max(j.Strength, j.MaxStrength * (0.85f + 0.12f * skill));
                j.Known = j.Strength;
                j.SeenAt = w.Tick;
                j.Repairs++;
                MarkLog.Add(j.Marks, w.Tick, $"{by}: {(wasBroken ? "다시 이었다" : "보강")} ({before * 100:0}% → {j.Strength * 100:0}%)");
                float stress = w.Structure.Stress.GetValueOrDefault(j.Room.Id);
                if (wasBroken || stress > 1f)
                    h.Add(w, HistoryKind.Response, $"{by}: {(wasBroken ? $"끊어진 {Ko.EulReul(j.Label)} 다시 이었다" : $"{Ko.EulReul(j.Label)} 보강했다")} ({j.Strength * 100:0}%)",
                        j.Room, who, j.Cell);
                else w.Log.Add(w.Tick, LogKind.Work, $"{by}: {Ko.EulReul(j.Label)} 보강했다 ({j.Strength * 100:0}%)", crew?.Id ?? -1);
                break;
            }
            case WorkKind.RebuildFrame:
            {
                if (w.Ship.WallAt(o.Target.Cell) is WallState wall && wall.FrameLost)
                {
                    Hull.RebuildFrame(wall, skill);
                    MarkLog.Add(wall.Marks, w.Tick, $"{by}: 골조와 외판을 새로 세웠다");
                    h.Add(w, HistoryKind.Response, $"{by}: {o.Target.Room?.Name ?? "선체"} 뜯겨 나간 외벽 골조를 새로 세웠다", o.Target.Room, who, o.Target.Cell);
                }
                break;
            }
            case WorkKind.InstallTruss:
            {
                var room = o.Target.Room!;
                var j = w.Structure.AddTruss(room, o.Target.Cell);
                MarkLog.Add(j.Marks, w.Tick, $"{by}: 임시 트러스를 덧댔다");
                MarkLog.Add(room.Marks, w.Tick, $"{by}: 임시 트러스");
                h.Add(w, HistoryKind.Adaptation, $"{by}: {room.Name}에 임시 트러스를 덧대 하중을 나눴다 (연결부 {room.Joints.Count(x => !x.Broken)}개)", room, who, o.Target.Cell);
                break;
            }
            case WorkKind.Clamp:
            {
                var j = o.Target.Joint!;
                j.MaxStrength = MathF.Min(j.MaxStrength, 0.9f);
                j.Strength = MathF.Max(j.Strength, 0.35f + 0.1f * skill);
                j.Known = j.Strength;
                j.SeenAt = w.Tick;
                j.Released = false;
                MarkLog.Add(j.Marks, w.Tick, $"{by}: 임시 도킹 고정");
                w.Log.Add(w.Tick, LogKind.Work, $"{by}: {Ko.EulReul(j.Label)} 임시로 고정했다", crew?.Id ?? -1);
                break;
            }
            case WorkKind.ReleaseJoint:
            {
                var j = o.Target.Joint!;
                j.Released = true;
                j.Known = 0f;
                MarkLog.Add(j.Marks, w.Tick, $"{by}: 사출 준비로 풀었다");
                w.Log.Add(w.Tick, LogKind.Work, $"{by}: {Ko.EulReul(j.Label)} 풀었다 (사출 준비)", crew?.Id ?? -1);
                break;
            }
            case WorkKind.InspectHull:
                w.Structure.Inspect(o.Target.Room!, crew, drone);
                break;
            case WorkKind.RepairRadiator: // v9
                w.Piping.RepairRadiator(o.Target.Pipe!, skill, by);
                break;
        }
        w.Board.Close(o);
        w.Board.RequestScan();
    }
}

public sealed partial class WorkBoard
{
    /// <summary>거치대 자재칸 목표 (드론마다 한 번은 나갈 수 있게).</summary>
    private static readonly (ItemKind kind, int target, int full)[] DockStock = { (ItemKind.Structure, 2, 2), (ItemKind.Plate, 2, 6), (ItemKind.Sealant, 1, 4) }; // 강화 full: 구멍이 열렸거나 창고가 넉넉하면 파공 서너 번 몫까지

    /// <summary>v9.3: 사람이 맡을 일이 이 재료가 없어 멈춰 있다.</summary>
    private bool CrewWaitsFor(ItemKind kind)
    {
        string name = ItemKinds.Name(kind);
        return _open.Values.Any(o => o.Drone == null && o.Kind != WorkKind.StockDock && o.BlockedReason != null && o.BlockedReason.Contains(name)
                                     && _world.Tick < o.BlockedUntil + SimTime.Hours(1));
    }

    /// <summary>거치대에 실린 자재 (드론 몫).</summary>
    internal int DockHave(ItemKind k) =>
        _world.Ship.Furniture.Where(f => f.Type == FurnitureType.DroneDock && !f.Room.Detached).Sum(f => f.Storage!.Count(k));

    /// <summary>
    /// v8 구조와 외부 작업: 연결부 보강, 골조 재건, 임시 트러스, 외부 검사(EVA), 드론 정비·보급,
    /// 구획 사출(결정 → 절차), 떨어져 나간 방 되찾기(결정 → 견인), 임시 도킹, 되살리기(결정 → 재연결).
    /// </summary>
    private void ScanStructure(Poster post)
    {
        var w = _world;
        var ship = w.Ship;
        var st = w.Structure;
        var drones = w.Drones;
        bool hatch = DroneSystem.Hatch(w) != null;
        int suits = ship.CountStored(ItemKind.Suit) + w.Crew.Count(c => c.Suit != null && !c.Dead);
        bool eva = hatch && suits > 0;
        bool repairHands = drones.Has(DroneKind.Repair) || drones.Has(DroneKind.Build) || eva;
        int structure = Have(ItemKind.Structure) + DockHave(ItemKind.Structure);

        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.DesignJoints <= 0) continue;
            int frames = StructureSystem.FrameLost(w, room);
            float knownCap = StructureSystem.KnownCapacity(room);
            float knownStress = StructureSystem.StressOf(knownCap, room.DesignJoints, frames);
            float ttf = StructureSystem.HoursToFailure(room, known: true, frames);

            // ── 사출 절차가 진행 중인 방 ──
            if (room.Jettison != null)
            {
                ProgressJettison(post, room, eva);
                continue;
            }

            // ── 연결부 보강 (아는 값으로 판단: 모르면 못 고친다) ──
            if (!room.Wreck)
                foreach (var j in room.Joints)
                {
                    if (j.Released) continue;
                    if (!j.KnownBroken && (j.Known >= 0.7f || j.MaxStrength - j.Known < 0.15f)) continue;
                    float u = j.KnownBroken ? 0.55f : 0.3f + 0.5f * (0.7f - j.Known);
                    if (knownStress > 1f) u = MathF.Max(u, 0.75f + 0.25f * MathF.Min(1f, knownStress - 1f));
                    if (ttf < 6f) u = MathF.Max(u, 1.05f);
                    if (room.Abandoned && !room.Docked) u *= 0.7f;
                    post(WorkKind.RepairJoint, WorkTarget.OfJoint(j), u, Skill.Mechanics,
                        (j.KnownBroken ? "끊어짐" : $"강도 {j.Known * 100:0}%") + $" · 남은 연결부 {room.Joints.Count(x => !x.KnownBroken)}/{room.Joints.Count}" +
                        (knownStress > 1f ? $" · 하중 {knownStress * 100:0}%" : "") + (ttf < 48f ? $" · {ttf:0}시간 안에 끊어질 수 있다" : ""),
                        minSkill: 0.2f);
                }

            // ── 임시 트러스: 하중이 넘치는데 건설 드론이 있으면 새 연결부를 덧댄다 ──
            if (!room.Wreck && knownStress > 1.15f && drones.Has(DroneKind.Build) && room.Joints.Count(j => j.Truss) < 2
                && st.TrussSpot(room) is Cell tc)
                post(WorkKind.InstallTruss, WorkTarget.OfOuterWall(tc, room), 0.85f, Skill.Mechanics,
                    $"하중 {knownStress * 100:0}% · 구조재 2 + 금속판 1로 임시 트러스", minSkill: 0.3f);

            // ── 외부 검사 (EVA): 운석을 맞았는데 검사 드론이 나갈 수 없으면 사람이 나가서 본다 ──
            if (st.Unseen.TryGetValue(room.Id, out var hitAt) && !drones.CanInspect() && w.Tick - hitAt > SimTime.Minutes(20))
                post(WorkKind.InspectHull, WorkTarget.OfExterior(room), room.Abandoned ? 0.35f : 0.6f, Skill.Mechanics,
                    "운석을 맞았다 · 검사 드론이 나갈 수 없다 → 우주복을 입고 연결부를 본다");

            // ── 사출 제안: 뜯겨 나가기 직전이거나, 끌 수 없는 불 ──
            if (room.Type == RoomType.Corridor || room.Docked) continue;
            string? reason = null;
            if (knownCap < 0.22f * room.DesignJoints && room.Joints.Any(j => !j.KnownBroken))
                reason = $"연결부가 거의 다 끊어졌다 ({room.Joints.Count(j => !j.KnownBroken)}/{room.Joints.Count}) — 뜯겨 나가기 전에 떼어 낸다";
            else if (ttf < 12f && (structure < 1 || !repairHands))
                reason = $"연결부가 {ttf:0}시간 안에 버티지 못한다 — " + (structure < 1 ? "이을 구조재가 없다" : "밖에 나갈 손(드론·우주복)이 없다");
            else if (w.Fire.IsKnown(room) && w.Fire.BurningHours(room) >= 1.5f && w.Fire.CountIn(room) >= Math.Max(4, room.Volume / 4)
                     && Have(ItemKind.Extinguisher) == 0 && !(room.Suppression && room.Powered))
                reason = $"불이 {w.Fire.BurningHours(room):0.#}시간째 꺼지지 않는다 ({w.Fire.CountIn(room)}칸) — 소화기가 없다";
            // v13.2 방침(사출: 적극): 더 일찍 떼어 낸다
            if (reason == null && w.Policies["jettison"] == 2)
            {
                if (knownCap < 0.3f * room.DesignJoints && room.Joints.Any(j => !j.KnownBroken))
                    reason = $"연결부가 많이 끊어졌다 ({room.Joints.Count(j => !j.KnownBroken)}/{room.Joints.Count}) — 방침대로 일찍 떼어 낸다";
                else if (w.Fire.IsKnown(room) && w.Fire.CountIn(room) >= Math.Max(3, room.Volume / 5)
                         && (w.Fire.BurningHours(room) >= 1f && Have(ItemKind.Extinguisher) == 0 || w.Fire.BurningHours(room) >= 2.5f))
                    reason = $"불이 {w.Fire.BurningHours(room):0.#}시간째 — 방침대로 일찍 떼어 낸다";
            }
            if (reason != null && w.Policies["jettison"] != 0) // v13.2 방침(사출: 금지)
                post(WorkKind.Jettison, WorkTarget.OfRoom(room), 1.0f, Skill.Mechanics, reason);
        }

        // ── 골조 재건: 외판이 골조에서 뜯겨 나간 벽 (밖에서만 고칠 수 있다) ──
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull || !wall.FrameLost) continue;
            var inside = Hull.InsideRoom(ship, cell);
            if (inside?.Jettison != null || inside?.Wreck == true) continue;
            float u = inside == null ? 0.3f : inside.Abandoned && !inside.Docked ? 0.45f : 0.95f;
            post(WorkKind.RebuildFrame, WorkTarget.OfOuterWall(cell, inside), u, Skill.Mechanics,
                "구조 연결 상실 — 실링폼으로는 못 막는다 · 구조재 2 + 금속판 1", minSkill: 0.3f);
        }

        // ── 드론 정비·수리·재조립 (거치대에서) ──
        foreach (var d in drones.Drones)
        {
            if (d.State != DroneState.Docked || d.Dock.Room.Detached) continue;
            bool only = !drones.Drones.Any(x => x != d && x.Kind == d.Kind && x.Operational);
            if (d.Wrecked || d.Faulty)
                post(WorkKind.ServiceDrone, WorkTarget.OfDrone(d), (d.Wrecked ? 0.35f : 0.45f) + (only ? 0.2f : 0f), Skill.Electrical,
                    d.Wrecked ? "부서졌다 · 전자재 2 + 모터 1 + 금속판 2" : "고장 · 전자재 1", minSkill: 0.25f);
            else if (d.Condition < 0.55f)
                post(WorkKind.ServiceDrone, WorkTarget.OfDrone(d), 0.2f + 0.4f * (0.55f - d.Condition), Skill.Electrical,
                    $"상태 {d.Condition * 100:0}% · 윤활유 1");
        }

        // ── 거치대 자재 보급: 드론이 싣고 나갈 구조재·금속판·실링폼 ──
        //    v9.3: 선반이 바닥났는데 사람이 할 일이 그 재료를 기다리면, 거치대에 실어 둔 것을 도로 꺼내 쓴다 (사람 일이 먼저)
        foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock))
        {
            if (dock.Room.Detached) continue;
            foreach (var (kind, _, _) in DockStock)
            {
                int inDock = dock.Storage!.Count(kind);
                if (inDock == 0 || ship.CountStored(kind) > 0 || !CrewWaitsFor(kind)) continue;
                post(WorkKind.UnstockDock, WorkTarget.Of(dock), 0.8f, Skill.Mechanics,
                    $"선반에 {ItemKinds.Name(kind)} 없음 · 거치대에 {inDock}개 · 사람이 할 일이 기다린다", product: kind);
            }
        }
        bool holes = _open.Values.Any(o => o.Kind == WorkKind.SealBreach && !o.Closed); // 강화: 구멍이 열려 있으면 드론 자재가 급하다
        foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock))
        {
            foreach (var (kind, normal, full) in DockStock)
            {
                int have = dock.Storage!.Count(kind);
                int spare = ship.CountStored(kind);
                int target = holes || spare >= 10 ? full : normal; // 평소에는 승무원 몫을 거치대로 빼 가지 않는다
                if (have >= target || CrewWaitsFor(kind)) continue;
                bool waiting = drones.Waiting.Contains(kind);
                bool rush = holes && kind is ItemKind.Sealant or ItemKind.Plate && have < 2; // 강화: 구멍이 열렸는데 거치대가 비어 간다
                if (spare < (waiting ? 1 : rush ? 3 : 4)) continue; // 승무원 몫(비상용·개조)을 남긴다 — 마지막 것은 드론이 기다릴 때만
                post(WorkKind.StockDock, WorkTarget.Of(dock), waiting || rush ? (holes ? 0.9f : 0.75f) : 0.2f + 0.08f * (target - have), Skill.Mechanics,
                    $"{ItemKinds.Name(kind)} {have}/{target}" + (waiting ? " · 드론이 자재를 기다린다" : rush ? " · 구멍이 열렸다 — 드론 몫" : ""), product: kind);
            }
        }

        // ── v9.2: 관제(주 컴퓨터)가 끊겼는데 드론이 할 일이 있으면 사람이 거치대 콘솔에서 한 대씩 몬다 ──
        if (hatch && !w.Automation.DroneControl && drones.ManualWork() is (string why, float urgency)
            && ship.FurnitureOf(FurnitureType.DroneDock).FirstOrDefault(f => !f.Room.Detached) is Furniture pilotDock)
            post(WorkKind.PilotDrones, WorkTarget.Of(pilotDock), urgency, Skill.Piloting, $"자동화가 꺼져 드론 관제가 없다 · {why}");

        // ── 떨어져 나간 조각: 되찾을지 정하고, 견인 드론이 끌어온다 ──
        foreach (var f in st.Fragments.ToList())
        {
            if (f.State is FragmentState.Lost) continue;
            var room = f.Room;
            if (f.State == FragmentState.Moored)
            {
                // 임시 도킹: 연결부 둘(없으면 있는 만큼)을 임시로 고정하면 다시 끼운다
                var clamps = room.Joints.Where(j => !j.Truss).OrderBy(j => j.Index).Take(Math.Min(2, room.Joints.Count)).ToList();
                if (clamps.All(j => j.Strength >= 0.3f && !j.Released))
                {
                    w.Drones.MarkFootprint(f, false);
                    if (!st.Reattach(f)) w.Drones.MarkFootprint(f, true);
                    continue;
                }
                bool vital = Council.Essential(w, room) != null;
                foreach (var j in clamps)
                    if (j.Strength < 0.3f || j.Released)
                        post(WorkKind.Clamp, WorkTarget.OfJoint(j), vital ? 0.95f : 0.6f, Skill.Mechanics, "끌어온 조각을 원래 자리에 임시로 고정 · 구조재 1");
                continue;
            }
            if (f.Tug != null || f.RetrieveApproved || f.Distance < StructureSystem.LostRange)
            {
                bool tow = drones.Has(DroneKind.Tow);
                float ru = Council.Essential(w, room) != null ? 0.95f : f.RetrieveApproved ? 0.7f : 0.6f;
                post(WorkKind.Retrieve, WorkTarget.OfFragment(room), ru, Skill.Mechanics,
                    $"{f.Distance:0}칸 떨어짐 · " + (tow ? "견인 드론으로 끌어온다" : "견인 드론이 없다") + $" · 손상 {StructureSystem.WreckScore(w, room) * 100:0}%");
            }
        }

        // ── 다시 붙인 방: 되살릴지(재연결) 잔해로 둘지 ──
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || !room.Docked) continue;
            if (room.Wreck)
            {
                if (SalvageTargets(w, room).Any())
                    post(WorkKind.Salvage, WorkTarget.OfRoom(room), 0.3f, Skill.Mechanics, "잔해에서 쓸 만한 것을 꺼낸다 (우주복)");
                continue;
            }
            string? need = Council.Essential(w, room);
            if (!room.Restoring)
            {
                post(WorkKind.RestoreRoom, WorkTarget.OfRoom(room), need != null ? 0.9f : 0.5f, Skill.Mechanics,
                    $"손상 {StructureSystem.WreckScore(w, room) * 100:0}% · 전력·배관·환기를 다시 잇고 재가압할지");
                continue;
            }
            // 연결부부터 제대로 이어야 한다 (계류줄 없이 하중을 버틸 수 있어야 재연결한다)
            float docStress = StructureSystem.StressOf(StructureSystem.KnownCapacity(room), room.DesignJoints, StructureSystem.FrameLost(w, room));
            if (docStress > 1f)
            {
                // 대신할 게 없는 방(배전반 …)은 연결부를 다 잇기 전에 전력부터 잇는다 (계류줄이 버티는 동안)
                if (need != null && room.PowerCut)
                    post(WorkKind.Reconnect, WorkTarget.OfRoom(room), 1.05f, Skill.Electrical, $"케이블 2 · {need} 없이는 못 버틴다 (연결부는 아직)");
                continue;
            }
            if (room.PowerCut || (room.PipesCut && room.HasPipes) || room.VentSealed)
                post(WorkKind.Reconnect, WorkTarget.OfRoom(room), need != null && room.PowerCut ? 1.05f : need != null ? 0.8f : 0.5f, Skill.Electrical,
                    (room.PowerCut ? "케이블 2" : room.PipesCut && room.HasPipes ? "금속판 1" : "금속판 1") + (need != null ? $" · {need} 없이는 못 버틴다" : ""));
            else
            {
                room.Docked = false; // Restoring은 재가압(구획 재개방)까지 남는다: 새는 곳을 제대로 막는다
                room.AbandonReason = "재연결 끝 · 재가압 대기";
                w.History.Add(w, HistoryKind.Structure, $"{room.Name} 전력·배관·환기를 다시 이었다 — 새는 곳을 막으면 재가압한다", room, log: true);
            }
        }
    }

    /// <summary>사출 절차를 한 단계씩 밀고 간다. 조건이 채워지면 다음 단계로.</summary>
    private void ProgressJettison(Poster post, Room room, bool eva)
    {
        var w = _world;
        var plan = room.Jettison!;
        var target = WorkTarget.OfRoom(room);
        float urgency = 1.0f;
        for (int guard = 0; guard < 8; guard++)
        {
            float inStage = (w.Tick - plan.StageSince) / (float)SimTime.TicksPerHour;
            switch (plan.Stage)
            {
                case JettisonStage.Evacuate:
                    if (w.Crew.Any(c => !c.Dead && c.Room == room && c.CarriedBy == null))
                    {
                        // 쓰러진 사람은 구조 작업이 데려간다
                        return;
                    }
                    break;
                case JettisonStage.Salvage:
                {
                    // 회수에 쓸 시간: 뜯겨 나가기까지 남은 시간의 일부 (불타는 방은 들어갈 수 없다)
                    float ttf = StructureSystem.HoursToFailure(room, true, StructureSystem.FrameLost(w, room));
                    float cap = plan.Fire ? 0f : Math.Clamp((ttf - 2.5f) * 0.4f, 0f, 2f);
                    if (inStage < cap && w.Fire.CountIn(room) == 0 && SalvageTargets(w, room).Any())
                    {
                        post(WorkKind.Salvage, target, 0.95f, Skill.Mechanics, $"사출 전에 쓸 만한 것을 꺼낸다 ({plan.Salvaged}번 날랐다)");
                        return;
                    }
                    break;
                }
                case JettisonStage.Power:
                case JettisonStage.Pipes:
                case JettisonStage.Vent:
                case JettisonStage.Bulkheads:
                {
                    // 전력 → 배관 → 환기 → 격벽: 체크리스트 순서지만, 손이 여럿이면 나눠서 한꺼번에 한다
                    bool pending = false;
                    if (!room.PowerCut) { post(WorkKind.IsolatePower, target, urgency, Skill.Electrical, "뜯겨 나갈 때 단락되지 않게 회로를 끊는다"); pending = true; }
                    if (room.HasPipes && !room.PipesCut) { post(WorkKind.IsolatePipes, target, urgency, Skill.Mechanics, "물이 새지 않게 밸브를 잠근다"); pending = true; }
                    if (!room.VentSealed) { post(WorkKind.IsolateVent, target, urgency, Skill.Mechanics, "전력을 끊으면 댐퍼가 자동으로 안 닫힌다 → 손으로 닫고 덕트를 봉한다"); pending = true; }
                    foreach (var d in room.Doors.Where(d => !d.Removed && !d.IsExternal && !d.Welded))
                    {
                        post(WorkKind.WeldBulkhead, WorkTarget.OfDoor(d), urgency, Skill.Mechanics, "이웃 구획 쪽 격벽을 용접해 막는다 · 금속판 1");
                        pending = true;
                    }
                    // 화면에 보일 단계: 아직 안 끝난 첫 항목
                    var shown = !room.PowerCut ? JettisonStage.Power : room.HasPipes && !room.PipesCut ? JettisonStage.Pipes
                        : !room.VentSealed ? JettisonStage.Vent : JettisonStage.Bulkheads;
                    if (pending)
                    {
                        if (shown != plan.Stage) { plan.Stage = shown; }
                        return;
                    }
                    plan.Stage = JettisonStage.Bulkheads;
                    break;
                }
                case JettisonStage.Release:
                {
                    var holding = room.Joints.Where(j => !j.Broken).ToList();
                    if (holding.Count > 0 && !plan.Bolts)
                    {
                        bool hands = w.Drones.Has(DroneKind.Repair) || w.Drones.Has(DroneKind.Build) || eva;
                        if (!hands || inStage > 2f)
                        {
                            plan.Bolts = true;
                            w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} 연결부를 밖에서 풀 수 없다 — 폭발 볼트로 끊는다 (이웃 벽이 조금 상한다)");
                            break;
                        }
                        foreach (var j in holding) post(WorkKind.ReleaseJoint, WorkTarget.OfJoint(j), urgency, Skill.Mechanics, "사출 준비 · 밖에서 볼트를 푼다");
                        return;
                    }
                    break;
                }
                case JettisonStage.Eject:
                    post(WorkKind.Eject, target, 1.1f, Skill.Mechanics, plan.Bolts ? "폭발 볼트로 끊고 밀어낸다" : "풀어 둔 구획을 밀어낸다");
                    return;
                default:
                    return;
            }
            plan.Stage++;
            plan.StageSince = w.Tick;
            if (plan.Stage <= JettisonStage.Eject)
                w.Log.Add(w.Tick, LogKind.Ship, $"{room.Name} 사출 준비: {JettisonPlan.StageName(plan.Stage - 1)} 끝 → {JettisonPlan.StageName(plan.Stage)}");
        }
    }

    /// <summary>사출 전·잔해에서 꺼낼 만한 것: 보관함 속 물건, 설비 속 부품.</summary>
    internal static IEnumerable<(Furniture f, ItemKind kind, int count)> SalvageTargets(World w, Room room)
    {
        foreach (var f in room.Furniture)
        {
            if (f.Storage is Inventory inv)
                foreach (var (k, n) in inv.Contents)
                    if (!(ItemKinds.IsFood(k) && room.Wreck)) yield return (f, k, n);
            if (f.Machine is Machine m && !m.Has(FaultKind.Stripped) && !m.Has(FaultKind.Wrecked) && f.Type != FurnitureType.PowerPanel
                && Faults.SalvageOf(f.Type).Length > 0)
                yield return (f, Faults.SalvageOf(f.Type)[0], 0);
        }
    }

    /// <summary>회의가 사출을 승인했다: 절차를 시작한다.</summary>
    internal void BeginJettison(WorkOrder o)
    {
        var w = _world;
        var room = o.Target.Room!;
        if (room.Jettison != null || room.Detached) return;
        room.Jettison = new JettisonPlan
        {
            Started = w.Tick, StageSince = w.Tick, Reason = o.Detail, Decider = o.Decider,
            Fire = o.Detail.Contains("불"),
        };
        MarkLog.Add(room.Marks, w.Tick, $"사출 결정 — {o.Detail}");
        w.RaiseAlert($"{room.Name} 사출 결정 — 모두 {Ko.EulReul(room.Name)} 비운다", room, AlertLevel.Warning, shipWide: true);
        Close(o);
        RequestScan();
    }
}

public static partial class WorkPlanners
{
    /// <summary>에어락으로 나간다: 우주복 → 에어락 안쪽 → 감압. 에어락이 없으면 나갈 수 없다.</summary>
    private static bool EvaOut(CrewMember c, World w, DistanceField dist, List<Toil> toils, out string? blocked)
    {
        blocked = null;
        var hatch = DroneSystem.Hatch(w);
        if (hatch == null) { blocked = "에어락이 없다"; return false; }
        if (Inner(w, hatch) is not Cell inner) { blocked = "에어락이 없다"; return false; }
        if (c.Suit is { Oxygen: < 2f }) { blocked = "입은 우주복 산소가 모자라다"; return false; }
        if (w.EvaRisk.Refuses(c, out blocked)) return false; // v16.11 선외 공포 — 정비가 밀린다
        if (!SuitUp(c, w, dist, toils)) { blocked = "우주복 없음"; return false; }
        toils.Add(new DoToil((cm, world) =>
        {
            cm.EvaMode = true;
            return true;
        }));
        toils.Add(new GotoToil(inner));
        toils.AddRange(w.Flow.AirlockOut(c, hatch, inner, Crisis.Level(w) >= CrisisLevel.Emergency)); // v14.8 점검 → 감압 (전기가 모자라면 손 펌프 · 급하면 공기를 버리고)
        return true;
    }

    /// <summary>에어락으로 돌아온다: 해치 안쪽 → 가압.</summary>
    private static void EvaIn(World w, List<Toil> toils)
    {
        var hatch = DroneSystem.Hatch(w);
        if (hatch == null || Inner(w, hatch) is not Cell inner) return;
        toils.Add(new GotoToil(inner));
        toils.AddRange(w.Flow.AirlockIn(hatch, inner)); // v14.8 가압 → 에어락 안에서 우주복을 턴다
        toils.Add(new DoToil((cm, world) =>
        {
            cm.EvaMode = false;
            return true;
        }));
    }

    private static Cell? Inner(World w, Door hatch)
    {
        foreach (var d in Cell.Dirs4)
            if (w.Ship.RoomAt(hatch.Cell + d) != null && w.Ship.IsWalkable(hatch.Cell + d)) return hatch.Cell + d;
        return null;
    }

    /// <summary>선체 밖 일 (EVA): 재료 → 우주복 → 에어락 → 자리 → 일 → 에어락.</summary>
    private static Job? Eva(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked,
        float hours, string label)
    {
        blocked = null;
        var need = DroneSystem.Materials(o);
        var toils = need.Length > 0 ? FetchAll(c, w, dist, need) : Plans.DropOff(c, w, dist);
        if (toils == null) { blocked = $"{Cost(need)} 없음"; return null; }
        if (!EvaOut(c, w, dist, toils, out blocked)) return null;
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(hours, Skill.Mechanics, o.Target.Cell.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, need)) return false;
            float skill = cm.SkillLevel(Skill.Mechanics);
            ExternalWork.Apply(world, o, skill, cm, null);
            cm.Practice(Skill.Mechanics, 0.04f);
            cm.Stats.Repairs++;
            MarkLog.Add(cm.Memory.Marks, world.Tick, $"EVA: {o.Title}");
            return true;
        }));
        EvaIn(w, toils);
        return Wrap(a, o, c, w, label + " (EVA)", toils, $"우주복을 입고 선체 밖으로 — {o.Title}");
    }

    private static Job? RepairJoint(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked) =>
        Eva(a, o, c, w, dist, at, out blocked, 1.0f, "연결부 보강");

    private static Job? RebuildFrame(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked) =>
        Eva(a, o, c, w, dist, at, out blocked, 1.5f, "골조 재건");

    private static Job? InstallTruss(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked) =>
        Eva(a, o, c, w, dist, at, out blocked, 2.0f, "임시 트러스");

    private static Job? Clamp(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked) =>
        Eva(a, o, c, w, dist, at, out blocked, 0.75f, "임시 도킹");

    private static Job? ReleaseJoint(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked) =>
        Eva(a, o, c, w, dist, at, out blocked, 0.35f, "연결 해제");

    /// <summary>외부 검사 (EVA): 연결부를 하나씩 들러 눈으로 본다.</summary>
    private static Job? InspectHull(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        var room = o.Target.Room!;
        var toils = Plans.DropOff(c, w, dist);
        if (!EvaOut(c, w, dist, toils, out blocked)) return null;
        var spots = room.Joints.Select(j => j.Spot).Where(s => dist.Reachable(s)).OrderBy(dist.Get).ToList();
        if (spots.Count == 0) { blocked = "밖에서 닿을 수 없다"; return null; }
        foreach (var s in spots)
        {
            toils.Add(new GotoToil(s));
            toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Working, s.Center));
        }
        toils.Add(new DoToil((cm, world) =>
        {
            ExternalWork.Apply(world, o, cm.SkillLevel(Skill.Mechanics), cm, null);
            MarkLog.Add(cm.Memory.Marks, world.Tick, $"EVA: {room.Name} 외부 검사");
            return true;
        }));
        EvaIn(w, toils);
        return Wrap(a, o, c, w, "외부 검사 (EVA)", toils, $"우주복을 입고 {room.Name} 바깥 연결부를 보러 나간다");
    }

    /// <summary>드론 정비·수리·재조립 (거치대에서).</summary>
    private static Job? ServiceDrone(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var d = o.Target.Drone!;
        if (d.State != DroneState.Docked) { blocked = "드론이 나가 있다"; return null; }
        var need = DroneSystem.ServiceCost(d);
        var toils = FetchAll(c, w, dist, need);
        if (toils == null) { blocked = $"{Cost(need)} 없음"; return null; }
        float hours = d.Wrecked ? 2f : d.Faulty ? 1f : 0.5f;
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(hours, Skill.Electrical, d.Dock.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (d.State != DroneState.Docked || !UseAll(cm, need)) return false;
            world.Drones.Serviced(d, cm, cm.SkillLevel(Skill.Electrical));
            cm.Practice(Skill.Electrical, 0.03f);
            cm.Stats.Repairs++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "드론 정비", toils, $"{o.Title} ({o.Detail})");
    }

    /// <summary>드론 거치대 자재칸 채우기.</summary>
    private static Job? StockDock(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var dock = o.Target.Furniture!;
        var kind = o.Product ?? ItemKind.Structure;
        int target = kind == ItemKind.Sealant ? 1 : 2;
        int want = target - dock.Storage!.Count(kind);
        if (want <= 0) { w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        var (box, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Count(kind) > 0);
        if (box == null) { blocked = $"{ItemKinds.Name(kind)} 없음"; return null; }
        toils.Add(new GotoToil(spot));
        toils.Add(new TakeToil(box, kind, Math.Min(want, box.Storage!.Count(kind)), partialOk: true));
        toils.Add(new GotoToil(at));
        toils.Add(new PutToil(dock));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "드론 자재 보급", toils, $"{o.Title}");
    }

    /// <summary>
    /// v9.2 비상수단: 관제(주 컴퓨터)가 끊기면 사람이 거치대 콘솔에 앉아 드론을 한 대씩 손으로 몬다.
    /// 앉아 있는 동안만 드론이 나가고, 자리를 뜨면 (잠깐의 틈은 봐준다) 밖의 드론은 하던 일을 두고 돌아온다.
    /// </summary>
    private static Job? PilotDrones(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var dock = o.Target.Furniture!;
        if (w.Automation.DroneControl) { w.Board.Close(o); return null; }
        if (w.Drones.Pilot is CrewMember p && p != c && p.CanAct) { blocked = $"{Ko.IGa(p.Name)} 몰고 있다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(3f, Skill.Piloting, dock.Center)
        {
            CanContinue = (cm, world) => !world.Automation.DroneControl && !o.Closed && world.Tick - o.LastSeen < SimTime.Minutes(20),
            OnBegin = (cm, world) =>
            {
                world.Drones.TakeControl(cm);
            },
            OnEnd = (cm, world) =>
            {
                if (world.Drones.Pilot == cm) world.Drones.Pilot = null;
            },
        });
        return Wrap(a, o, c, w, "드론 조종", toils, $"{o.Title} — {o.Detail}");
    }

    /// <summary>v9.3: 거치대 자재칸의 재료를 선반으로 도로 가져온다 (사람이 할 일이 기다린다).</summary>
    private static Job? UnstockDock(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var dock = o.Target.Furniture!;
        var kind = o.Product ?? ItemKind.Plate;
        int n = dock.Storage!.Count(kind);
        if (n == 0) { w.Board.Close(o); return null; }
        var (shelf, shelfSpot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Shelf && f.Storage!.Accepts(kind) && f.Storage.Free > 0);
        if (shelf == null) { blocked = "둘 선반 없음"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new TakeToil(dock, kind, n, partialOk: true));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{dock.Label}에서 {ItemKinds.Name(kind)} {n}개를 도로 꺼냈다 — 사람이 할 일이 먼저다", cm.Id);
            return true;
        }));
        toils.Add(new GotoToil(shelfSpot));
        toils.Add(new PutToil(shelf));
        return Wrap(a, o, c, w, "거치대 자재 꺼내기", toils, $"{o.Title}");
    }

    /// <summary>손에 든 것을 그 방 밖의 보관함에 둔다 (사출할 방·잔해에서 꺼낸 것).</summary>
    private static void DropOutside(CrewMember c, World w, DistanceField dist, Room room, List<Toil> toils)
    {
        toils.Add(new DoToil((cm, world) => true));
        var holder = new Furniture?[1];
        toils.Add(new GotoToilLate(cm =>
        {
            if (cm.Carrying is not ItemStack held) return null;
            var d2 = w.Paths.Flood(cm.Cell, cm.PathProfile);
            var (box, spot) = Plans.NearestContainer(w, d2, cm, f => f.Room != room && f.Storage!.Accepts(held.Kind) && f.Storage.Free > 0);
            holder[0] = box;
            return box != null ? spot : null;
        }));
        toils.Add(new DoToil((cm, world) =>
        {
            if (holder[0] is Furniture box && cm.Carrying is ItemStack held)
            {
                int put = box.Storage!.Add(held.Kind, held.Count);
                cm.Carrying = held.Count > put ? new ItemStack(held.Kind, held.Count - put) : null;
            }
            return true;
        }));
    }

    /// <summary>물품 회수: 사출할 방·잔해에서 가장 쓸모 있는 것 하나를 꺼내 방 밖에 둔다 (여러 번 나른다).</summary>
    private static Job? Salvage(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        var pick = SalvagePick(w, room, dist);
        if (pick == null) { blocked = "꺼낼 것이 없다"; return null; }
        var (f, kind, count, spot) = pick.Value;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        if (count > 0)
            toils.Add(new TakeToil(f, kind, Math.Min(count, 12), partialOk: true));
        else
        {
            // 설비에서 부품을 뜯는다 (그 설비는 영구히 멈춘다)
            toils.Add(new WorkToil(0.5f, Skill.Mechanics, f.Center));
            toils.Add(new DoToil((cm, world) =>
            {
                var m = f.Machine!;
                if (m.Has(FaultKind.Stripped)) return false;
                m.Faults.Add(new Fault { Kind = FaultKind.Stripped, Since = world.Tick, PartOverride = kind });
                m.TimesStripped++;
                MarkLog.Add(m.Marks, world.Tick, $"{Ko.IGa(cm.Name)} {Ko.EulReul(ItemKinds.Name(kind))} 떼어 냈다 (회수)");
                cm.Carrying = new ItemStack(kind, 1);
                return true;
            }));
        }
        DropOutside(c, w, dist, room, toils);
        toils.Add(new DoToil((cm, world) =>
        {
            if (room.Jettison != null) room.Jettison.Salvaged++;
            cm.Stats.Emergencies++;
            world.Board.Release(o, cm);
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name}에서 {Ko.EulReul(ItemKinds.Name(kind))} 꺼내 왔다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "물품 회수", toils, $"{room.Name}에서 {Ko.EulReul(ItemKinds.Name(kind))} 꺼내러 간다");
    }

    /// <summary>회수할 것 고르기: 우주복·부품·수리재 먼저, 음식은 나중.</summary>
    private static (Furniture f, ItemKind kind, int count, Cell spot)? SalvagePick(World w, Room room, DistanceField dist)
    {
        (Furniture, ItemKind, int, Cell)? best = null;
        float bestV = 0f;
        foreach (var (f, kind, count) in WorkBoard.SalvageTargets(w, room))
        {
            var spot = f.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell s) continue;
            float v = kind switch
            {
                ItemKind.Suit => 10f,
                ItemKind.ReactorControl => 9f,
                _ => ItemKinds.Tier(kind) switch { ItemTier.General => 6f, ItemTier.Basic => 4f, ItemTier.Advanced => 9f, ItemTier.Raw => 1.5f, _ => 2.5f },
            };
            if (count == 0) v *= 0.8f; // 뜯는 데 시간이 든다
            else v += MathF.Min(2f, count / 6f);
            v -= dist.Get(s) / 3000f;
            if (v > bestV) { bestV = v; best = (f, kind, count, s); }
        }
        return best;
    }

    private static Job IsolateStep(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, float hours, Skill skill,
        Action<Room, World> apply, string label, string log)
    {
        var room = o.Target.Room!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(hours, skill, room.DamperSpot.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            apply(room, world);
            cm.Stats.Emergencies++;
            MarkLog.Add(room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} {label}");
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} {log}", cm.Id);
            world.Board.Close(o);
            world.Board.RequestScan();
            return true;
        }));
        return Wrap(a, o, c, w, label, toils, $"{o.Title} ({o.Detail})");
    }

    private static Job IsolatePower(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at) =>
        IsolateStep(a, o, c, w, dist, at, 0.25f, Skill.Electrical, (r, _) => r.PowerCut = true, "전력 차단", "회로를 끊었다 (사출 준비)");

    private static Job IsolatePipes(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at) =>
        IsolateStep(a, o, c, w, dist, at, 0.35f, Skill.Mechanics, (r, _) => r.PipesCut = true, "배관 차단", "배관 밸브를 잠갔다 (사출 준비)");

    private static Job IsolateVent(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at) =>
        IsolateStep(a, o, c, w, dist, at, 0.3f, Skill.Mechanics, (r, _) => { r.VentOpen = false; r.VentSealed = true; }, "환기 차단",
            "댐퍼를 손으로 닫고 환기관을 봉했다 (사출 준비)");

    /// <summary>격벽 용접: 사출할 방으로 이어진 문을 이웃 쪽에서 용접해 막는다.</summary>
    private static Job? WeldBulkhead(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var door = o.Target.Door!;
        var toils = Fetch(c, w, dist, ItemKind.Plate, 1);
        if (toils == null) { blocked = "금속판 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.35f, Skill.Mechanics, door.Cell.Center) { CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Plate });
        toils.Add(new DoToil((cm, world) =>
        {
            Consume(cm, ItemKind.Plate);
            door.JammedOpen = false; // 휜 문은 판을 덧대 막는다
            door.Openness = 0f;
            door.Welded = true;
            door.Locked = true;
            cm.Stats.Emergencies++;
            world.Log.Add(world.Tick, LogKind.Work, $"{door.RoomA?.Name ?? "?"}·{door.RoomB?.Name ?? "?"} 사이 격벽을 용접해 막았다", cm.Id);
            world.Board.Close(o);
            world.Board.RequestScan();
            return true;
        }));
        return Wrap(a, o, c, w, "격벽 용접", toils, $"{o.Title}");
    }

    /// <summary>사출 스위치.</summary>
    private static Job Eject(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var room = o.Target.Room!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Working, room.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (room.Detached || room.Jettison == null) { world.Board.Close(o); return true; }
            if (world.Crew.Any(x => !x.Dead && x.Room == room)) return false; // 아직 안에 사람이 있다
            string why = room.Jettison.Reason;
            var decider = room.Jettison.Decider;
            world.Board.Close(o);
            world.Structure.Detach(room, why + (decider != null ? $" (결정: {decider.Name})" : ""), controlled: true);
            cm.Stats.Emergencies++;
            MarkLog.Add(cm.Memory.Marks, world.Tick, $"{room.Name} 사출 스위치를 당겼다");
            return true;
        }));
        return Wrap(a, o, c, w, "사출", toils, $"{room.Name} 사출 스위치로 간다");
    }

    /// <summary>재연결: 다시 붙인 방에 전력 → 배관 → 환기를 하나씩 잇는다 (방 밖 분기점에서).</summary>
    private static Job? Reconnect(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        string step = o.ReconnectStep;
        var need = step == "전력" ? new[] { (ItemKind.Cable, 2) } : new[] { (ItemKind.Plate, 1) };
        var toils = FetchAll(c, w, dist, need);
        if (toils == null) { blocked = $"{Cost(need)} 없음"; return null; }
        toils.Add(new GotoToil(at));
        var skill = step == "전력" ? Skill.Electrical : Skill.Mechanics;
        toils.Add(new WorkToil(0.5f, skill, room.DamperSpot.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, need)) return false;
            if (step == "전력") room.PowerCut = false;
            else if (step == "배관") room.PipesCut = false;
            else room.VentSealed = false;
            cm.Practice(skill, 0.03f);
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} {Ko.EulReul(step)} 다시 이었다", cm.Id);
            MarkLog.Add(room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} {step} 재연결");
            world.Board.Release(o, cm);
            world.Board.RequestScan();
            return true;
        }));
        return Wrap(a, o, c, w, "재연결", toils, $"{o.Title}");
    }
}
