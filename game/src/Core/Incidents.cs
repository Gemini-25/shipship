using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

/// <summary>운석 충돌 기록 (화면 연출용).</summary>
public sealed record Impact(long Tick, Cell Entry, Cell Target, float Size, Vector2 Direction);

/// <summary>
/// 플레이어가 일으키는 사고. 여기서는 "처음 한 방"만 만들고, 그다음은 전부 시스템이 알아서 굴린다:
/// 벽이 약해지면 새고, 새면 감압되고, 감압되면 격벽이 잠기고, 파편이 전선을 끊으면 정전되고…
/// </summary>
public static class Incidents
{
    /// <summary>
    /// 운석. target 가까운 선체 벽으로 들어와 안쪽으로 파편을 뿌린다.
    /// size 0.3 = 작은 운석(대개 균열·미세 누출), 1 = 큰 운석(파공, 설비 파손, 부상, 화재 가능).
    /// </summary>
    public static Impact? Meteor(World w, Cell target, float size, WarnLevel warned = WarnLevel.None, float leadMinutes = 0f)
    {
        if (w.Eras.Has("forcefield")) size *= 0.6f; // v12.8 역장
        // v12.2 인과 사슬: 이 운석이 뿌리 (운석우의 한 알이면 운석우의 자식) — 이 안에서 생긴 피해는 모두 이 운석의 자식
        var hit = w.Ship.RoomAt(target) ?? w.Ship.LiveRooms.Where(r => !r.Detached).OrderBy(r => (r.Center - target.Center).LengthSquared()).FirstOrDefault();
        string text = $"{(size >= 0.7f ? "큰" : size >= 0.4f ? "중간" : "작은")} 운석 충돌 — {hit?.Name ?? "선체"}";
        int ctx = w.Causes.Context;
        int node = ctx >= 0 ? w.Causes.Effect(CauseKind.Impact, "", text, hit, target.Center, ctx, lasting: false)
            : w.Causes.Root(CauseKind.Impact, text, hit, target.Center, observer: w.Causes.ConsumeObserver());
        w.Causes.Hit(hit, node);
        using (w.Causes.Because(node))
        {
            w.Exterior.Hit(target, size, "운석"); // v12.6 선체 밖 설비
            return MeteorCore(w, target, size, warned, leadMinutes);
        }
    }

    private static Impact? MeteorCore(World w, Cell target, float size, WarnLevel warned, float leadMinutes)
    {
        var ship = w.Ship;
        Cell? entry = null;
        float best = float.MaxValue;
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull) continue;
            float d = (cell.Center - target.Center).LengthSquared();
            if (d < best) { best = d; entry = cell; }
        }
        if (entry is not Cell e) return null;

        // 들어오는 방향: 선체 벽에서 과녁 쪽 (과녁이 벽이면 가까운 방 쪽)
        var inside = Hull.InsideRoom(ship, e);
        var aim = target == e || ship.RoomAt(target) == null ? (inside?.Center ?? target.Center) : target.Center;
        var dir = aim - e.Center;
        dir = dir.LengthSquared() < 0.01f ? new Vector2(0, 1) : Vector2.Normalize(dir);

        // 1) 선체: 맞은 곳이 제일 크게, 주변은 덜
        float radius = 1f + 1.5f * size;
        float power = 0.5f + 0.75f * size;
        foreach (var (cell, _) in ship.Walls.ToList())
        {
            float dist = (cell.Center - e.Center).Length();
            if (dist > radius) continue;
            float falloff = 1f - dist / (radius + 0.5f);
            Hull.Damage(ship, cell, power * falloff * w.Rng.Range(0.8f, 1.1f));
        }

        // 1-b) 구조 (v8): 가까운 연결부가 상하고(밖에서 보기 전에는 모른다), 밖에 나와 있던 드론·사람이 맞는다
        w.Structure.OnImpact(e, size);
        w.Drones.OnImpact(e, size);
        w.Piping.OnImpact(e, size); // v9: 벽 안의 관과 선체 밖 방열판
        var hurtOutside = new List<CrewMember>();
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.Outside) continue;
            float dist = (c.Position - e.Center).Length();
            if (dist > 2.5f + 2f * size) continue;
            float dmg = (0.15f + 0.3f * size) * (1f - dist / (3f + 2f * size)) * w.Rng.Range(0.6f, 1.1f);
            c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
            NeedsSystem.AddInjury(c.Vitals, dmg * 0.9f, "파편 (선체 밖)");
            hurtOutside.Add(c);
            Memory.Shake(w, c, 0.08f + 0.1f * size, "선체 밖에서 운석 파편을 맞았다");
            w.Log.Add(w.Tick, LogKind.Warning, $"선체 밖에서 파편에 맞았다 (체력 {c.Vitals.Health * 100:0}%)", c.Id);
        }

        // 2) 파편: 안쪽으로 뻗는 원뿔
        float reach = 2f + 5f * size;
        var hitMachines = new HashSet<Machine>();
        var hurt = new HashSet<CrewMember>();
        bool fireStarted = false;
        for (float t = 0.5f; t <= reach; t += 0.5f)
        {
            var p = e.Center + dir * t;
            var cell = Cell.FromPosition(p);
            float strength = size * (1f - t / (reach + 1f));
            Shrapnel.Sweep(w, w.Rng, cell, strength, ring: true, hitMachines, "파편"); // v16.13 폭발 파편과 같은 판정
            foreach (var c in w.Crew)
            {
                if ((c.Position - p).Length() > 1.4f || !hurt.Add(c)) continue;
                if (c.Dead) continue;
                Shrapnel.HitCrew(w, w.Rng, c, 0.2f + 0.35f * size, warned != WarnLevel.None ? 0.75f : 1f, "파편", $"운석 파편에 맞았다 ({c.Room?.Name ?? "?"})"); // v10.1: 경보를 듣고 몸을 숙였다
            }
            if (!fireStarted && w.Rng.Chance(0.12f * size) && w.Fire.Ignite(cell, 0.3f)) fireStarted = true;
        }

        // 3) 배선: 파편이 그 구역 회로를 끊을 수 있다
        var room = inside ?? ship.RoomAt(target);
        if (room != null && w.Rng.Chance(0.45f * size))
        {
            var panel = ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine;
            if (panel != null && !panel.Faults.Any(f => f.Circuit == room.Circuit))
            {
                var kind = w.Rng.Chance(0.5f) ? FaultKind.ShortCircuit : FaultKind.BreakerTrip;
                panel.Faults.Add(new Fault { Kind = kind, Since = w.Tick, Circuit = room.Circuit });
                panel.FaultCount++;
                w.Causes.OnFault(panel, panel.Faults[^1]);
                w.History.CircuitFaults++;
                MarkLog.Add(panel.Marks, w.Tick, $"파편이 {PowerGrid.CircuitName(room.Circuit)} 회로를 끊었다");
                w.Log.Add(w.Tick, LogKind.Warning, $"파편이 배선을 끊었다 — {PowerGrid.CircuitName(room.Circuit)} 회로 {Faults.Spec(kind).Name}");
            }
        }

        // 배가 흔들린다: 큰 충돌은 온 배가, 작은 것은 그 방·옆방 사람이 잠에서 깬다
        foreach (var c in w.Crew)
            if (!c.Dead && (size >= 0.6f || c.Room == room || room != null && c.Room != null && c.Room.Doors.Any(d => d.RoomA == room || d.RoomB == room))) c.Jolt(w);
        var impact = new Impact(w.Tick, e, target, size, dir);
        w.Impacts.Add(impact);
        if (w.Impacts.Count > 12) w.Impacts.RemoveAt(0);
        string where = room?.Name ?? "선체";
        var state = ship.WallAt(e)!.Stage;

        // 역사: 어디에 들어왔나 (보강할 곳을 고를 때 쓴다)
        var h = w.History;
        h.Meteors++;
        h.ImpactCells.Add(e);
        MarkLog.Add(ship.WallAt(e)!.Marks, w.Tick, $"{(size >= 0.7f ? "큰 " : "")}운석이 박혔다");
        if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{(size >= 0.7f ? "큰 " : "")}운석 ({state})");
        h.Add(w, HistoryKind.Incident, $"{(size >= 1.3f ? "거대 " : size >= 0.7f ? "큰 " : "작은 ")}운석이 {where} 외벽에 박혔다 — {state}" +
            (hurt.Count > 0 ? $" · 파편에 {string.Join("·", hurt.Select(c => c.Name))} 부상" : "") +
            (hurtOutside.Count > 0 ? $" · 선체 밖의 {string.Join("·", hurtOutside.Select(c => c.Name))} 부상" : "") +
            (hitMachines.Count > 0 ? $" · 설비 {hitMachines.Count}대 손상" : "")
            + (warned != WarnLevel.None ? $" · {SensorSystem.LevelName(warned)} {leadMinutes:0.#}분 전" : w.Sensors.Array != null ? " · 경보 없이" : ""), room, hurt, e);
        foreach (var c in hurt) Memory.Frighten(w, c, c.Room, 0.3f + 0.3f * size, "운석 파편에 맞았다");
        w.RaiseAlert($"{(size >= 1.3f ? "거대 " : size >= 0.7f ? "대형 " : "")}운석 충돌 — {where} ({state})", room, AlertLevel.Critical, shipWide: true);
        w.Movement.Bang(room, e.Center, MathF.Min(1f, 0.5f + 0.4f * size), "운석이 외벽을 때렸다"); // v14.5
        w.Board.RequestScan();
        return impact;
    }

    /// <summary>화재. 바닥 칸에 불을 붙인다.</summary>
    public static bool Fire(World w, Cell cell)
    {
        if (!w.Fire.Ignite(cell, 0.35f)) return false;
        w.Log.Add(w.Tick, LogKind.Warning, $"{w.Ship.RoomAt(cell)?.Name ?? "?"}에서 불꽃이 튀었다");
        return true;
    }

    /// <summary>설비 고장.</summary>
    public static bool Break(World w, Furniture f) => f.Machine != null && w.Machines.Break(f.Machine) != null;
}
