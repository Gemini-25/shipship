using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v21 드론 확장 — 사람 세 명에 한 대씩 더 (상한 24) · 새 드론 다섯:
//  · 방패 드론: 경보가 난 운석의 길목에 나가 몸으로 받아 낸다 (작은 것은 부수고 · 큰 것은 줄인다 — 드론이 상한다).
//  · 채굴 드론: 한가할 때 배 곁을 지나는 얼음 · 금속 먼지를 긁어 와 거치대 자재칸에 (얼음 → 물 · 금속 원료).
//  · 외판 손질 드론: 방열판에 앉은 먼지 · 서리를 닦아 냉각을 되살린다 (고장 나기 전).
//  · 중계 드론: 사람이 선외에 나가면 그쪽으로 떠서 무전을 이어 준다 (멀리 떠내려가도 신호가 닿는다).
//  · 구조 드론: 견인 원형 — 떠내려간 사람 · 드론을 빨리 · 세게 끌어온다.
//  모든 드론은 기본 바깥일(연결부 보강 · 죔쇠 · 연결 해제 · 방열판 수리)을 한다 — 특기 드론보다 느리다.

public enum DroneErrand : byte { None, Guard, Mine, Polish, Relay, /* v23 구역 중계기가 꺼진 구역 */ Zone, /* v23 행성 땅: 캐기 · 짓기 · 살피기 */ Dig, Raise, Survey }

public sealed partial class Drone
{
    /// <summary>v21 작업 목록 밖의 심부름 (방패 · 채굴 · 손질 · 중계).</summary>
    public DroneErrand Errand { get; internal set; }
    internal Vector2? ErrandSpot { get; set; }
    internal int GuardFor { get; set; } = -1;
    internal long NextErrand { get; set; }
}

public sealed class DroneV21Stats
{
    public int Guards, Intercepts, Shrunk, ShieldHurt, Mines, Ice, Ore, Polishes, Relays;
    public override string ToString() => $"방패 출동 {Guards}(막음 {Intercepts} · 줄임 {Shrunk} · 상함 {ShieldHurt}) · 채굴 {Mines}(얼음 {Ice} · 금속 {Ore}) · 방열판 손질 {Polishes} · 중계 {Relays}";
}

public static class DronesV21
{
    /// <summary>처음 다섯 대 다음에 싣는 순서.</summary>
    public static readonly DroneKind[] Extra =
    {
        DroneKind.Shield, DroneKind.Lifeguard, DroneKind.Miner, DroneKind.Polisher, DroneKind.Relay, DroneKind.Scout, DroneKind.Welder, DroneKind.Tug,
        DroneKind.Rigger, DroneKind.Radiator, DroneKind.Shield, DroneKind.Surveyor, DroneKind.Repair, DroneKind.Miner, DroneKind.Lifeguard, DroneKind.Inspect,
        DroneKind.Build, DroneKind.Polisher, DroneKind.Shield,
    };

    public const int Cap = 24;
    /// <summary>처음 다섯 + 사람 세 명에 한 대.</summary>
    public static int Count(int beds) => Math.Clamp(5 + beds / 3, 5, Cap);

    public static string ErrandName(DroneErrand e) => e switch
    {
        DroneErrand.Guard => "운석 길목 지키기", DroneErrand.Mine => "얼음 · 금속 먼지 캐기", DroneErrand.Polish => "방열판 닦기", DroneErrand.Relay => "무전 중계", DroneErrand.Zone => "구역 무전 임시 중계",
        DroneErrand.Dig => "행성 땅 캐기", DroneErrand.Raise => "행성 기지 짓기", DroneErrand.Survey => "행성 땅 살피기", _ => "",
    };

    public static float ErrandHours(Drone d) => d.Errand switch
    {
        DroneErrand.Mine => 1.5f,
        DroneErrand.Polish => 0.5f,
        DroneErrand.Relay => 1f,
        DroneErrand.Zone => 0.5f,
        DroneErrand.Dig => 2f, DroneErrand.Raise => 1.5f, DroneErrand.Survey => 1f,
        _ => 0.25f, // Guard: 아래에서 운석이 올 때까지로 다시 잡는다
    };
}

public sealed partial class DroneSystem
{
    public DroneV21Stats V21 { get; } = new();
    /// <summary>시험용: 방패 드론이 운석 길목에 나가지 않는다 (다른 장면을 재는 시험).</summary>
    public static bool ShieldOff;
    private Rng? _v21Rng;
    private Rng R21 => _v21Rng ??= new Rng(unchecked(_world.Seed * 7741 + 307));

    /// <summary>v21 심부름: 방패(경보 난 운석) · 중계(선외의 사람) · 손질(방열판) · 채굴(한가할 때).</summary>
    private bool Errand(Drone d)
    {
        var w = _world;
        if (FleetSystem.Off) return false;
        // v23 행성 땅: 짓기 · 살피기 · 캐기 (날씨가 사나우면 나가지 않는다)
        if (w.Planet.DroneJob(d) is (DroneErrand pe, Vector2 pat, int ptarget, string plabel) && TripCost(d, pat, DronesV21.ErrandHours(d)) <= d.Battery)
        {
            d.Errand = pe;
            d.ErrandSpot = pat;
            d.GuardFor = ptarget;
            Launch(d, pat, plabel);
            return true;
        }
        if (!RobotsV21.General) return false;
        switch (d.Kind)
        {
            case DroneKind.Shield:
            {
                if (ShieldOff) return false;
                foreach (var m in w.Sensors.Incoming)
                {
                    if (m.Warned == WarnLevel.None || m.Arrive - w.Tick > SimTime.Minutes(40) || m.Arrive <= w.Tick + SimTime.Minutes(1)) continue;
                    if (Drones.Any(x => x.Errand == DroneErrand.Guard && x.GuardFor == m.Id)) continue;
                    if (OuterSpot(m.Target) is not Vector2 at || TripCost(d, at, 0.5f) > d.Battery) continue;
                    d.Errand = DroneErrand.Guard;
                    d.GuardFor = m.Id;
                    d.ErrandSpot = at;
                    V21.Guards++;
                    Launch(d, at, $"운석 길목을 지킨다 ({(m.Arrive - w.Tick) / (float)SimTime.Minutes(1):0}분 뒤 · {m.Room?.Name ?? "선체"})");
                    d.Mind.Say("경보가 난 운석 — 길목에 나가 몸으로 받는다", w.Tick);
                    return true;
                }
                return false;
            }
            case DroneKind.Relay:
            {
                var eva = w.Crew.Where(c => !c.Dead && c.Outside).ToList();
                if (eva.Count == 0 || Drones.Any(x => x != d && x.Errand == DroneErrand.Relay && x.State != DroneState.Docked)) return false;
                var hatch = Hatch(w);
                if (hatch == null) return false;
                var far = eva.OrderByDescending(c => (c.Position - hatch.Cell.Center).LengthSquared()).First();
                var at = hatch.Cell.Center + (far.Position - hatch.Cell.Center) * 0.6f;
                if (TripCost(d, at, 1f) > d.Battery) return false;
                d.Errand = DroneErrand.Relay;
                d.ErrandSpot = at;
                V21.Relays++;
                Launch(d, at, $"무전 중계 — {far.Name} 쪽으로");
                d.Mind.Say($"{Ko.IGa(far.Name)} 선외에 있다 — 무전을 이어 준다", w.Tick);
                return true;
            }
            case DroneKind.Polisher:
            {
                if (w.Tick < d.NextErrand) return false;
                d.NextErrand = w.Tick + SimTime.Hours(2);
                var seg = w.Piping.Segments.Where(s => s.Radiator.Count > 0 && s.RadiatorCondition is > 0.55f and < 0.92f).OrderBy(s => s.RadiatorCondition).FirstOrDefault();
                if (seg == null) return false;
                var at = seg.Radiator[seg.Radiator.Count / 2].Center;
                if (TripCost(d, at, 0.5f) > d.Battery) return false;
                d.Errand = DroneErrand.Polish;
                d.ErrandSpot = at;
                d.GuardFor = w.Piping.Segments.IndexOf(seg);
                Launch(d, at, $"{seg.Name} 방열판 닦기 ({seg.RadiatorCondition * 100:0}%)");
                return true;
            }
            case DroneKind.Miner:
            {
                if (w.Tick < d.NextErrand || d.Battery < 0.95f || w.Hazards.StormActive || w.Sensors.Incoming.Count > 0) return false;
                d.NextErrand = w.Tick + SimTime.Minutes(30);
                // 선체 바깥 네 칸 — 먼지 띠가 지나가는 쪽
                var hull = w.Ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
                var center = w.Structure.ShipCenter;
                var grid = w.Ship.Grid;
                for (int k = 0; k < 6 && hull.Count > 0; k++)
                {
                    var hc = hull[R21.Range(0, hull.Count)];
                    var dir = hc.Center - center;
                    if (dir.LengthSquared() < 0.01f) continue;
                    var at = hc.Center + Vector2.Normalize(dir) * 4f;
                    at = new Vector2(Math.Clamp(at.X, 0.5f, grid.Width - 0.5f), Math.Clamp(at.Y, 0.5f, grid.Height - 0.5f));
                    if (grid.Kind(Cell.FromPosition(at)) != TileKind.Void || TripCost(d, at, 1.5f) > d.Battery) continue;
                    d.Errand = DroneErrand.Mine;
                    d.ErrandSpot = at;
                    d.NextErrand = w.Tick + SimTime.Hours(8);
                    Launch(d, at, "얼음 · 금속 먼지 캐기");
                    return true;
                }
                return false;
            }
        }
        return false;
    }

    /// <summary>선체 칸 곁의 바깥 자리.</summary>
    private Vector2? OuterSpot(Cell target)
    {
        var grid = _world.Ship.Grid;
        for (int r = 1; r <= 3; r++)
            foreach (var d in Cell.Dirs8)
            {
                var c = new Cell(target.X + d.X * r, target.Y + d.Y * r);
                if (grid.InBounds(c) && grid.Kind(c) == TileKind.Void) return c.Center;
            }
        return null;
    }

    /// <summary>심부름을 마쳤다 (일한 자리에서). 돌아가면 true.</summary>
    private bool ErrandDone(Drone d)
    {
        var w = _world;
        switch (d.Errand)
        {
            case DroneErrand.Mine:
            {
                // v23 얼음이 창고에 넉넉하면(30개↑) 얼음 대신 금속 · 탄소 먼지를 고른다 — 얼음만 90개 넘게 쌓이고 금속 원료 · 탄소가 바닥났다 (보듬호)
                bool iceFull = w.Ship.CountStored(ItemKind.Ice) >= 30;
                int ice = iceFull ? 0 : 1 + (R21.Chance(0.6f) ? 1 : 0), ore = iceFull ? 1 + (R21.Chance(0.5f) ? 1 : 0) : R21.Chance(0.5f) ? 1 : 0, carbon = iceFull && R21.Chance(0.6f) ? 1 : 0;
                if (ice > 0) VoyageV15.Put(w, ItemKind.Ice, ice); // 배 창고로 (거치대에서 로봇이 옮긴 셈)
                if (ore > 0) VoyageV15.Put(w, ItemKind.MetalOre, ore);
                if (carbon > 0) VoyageV15.Put(w, ItemKind.Carbon, carbon);
                V21.Mines++; V21.Ice += ice; V21.Ore += ore;
                string got = string.Join(" · ", new[] { ice > 0 ? $"얼음 {ice}" : null, ore > 0 ? $"금속 원료 {ore}" : null, carbon > 0 ? $"탄소 {carbon}" : null }.Where(x => x != null));
                MarkLog.Add(d.Marks, w.Tick, $"{got} 캐 옴");
                w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: 배 곁을 지나는 먼지 띠에서 {got}을 긁어 왔다 (거치대 자재칸{(iceFull ? " · 얼음은 넉넉해 금속 · 탄소 먼지만" : "")})");
                break;
            }
            case DroneErrand.Polish when d.GuardFor >= 0 && d.GuardFor < w.Piping.Segments.Count:
            {
                var seg = w.Piping.Segments[d.GuardFor];
                float before = seg.RadiatorCondition;
                seg.RadiatorCondition = MathF.Min(1f, seg.RadiatorCondition + 0.12f);
                V21.Polishes++;
                w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: {seg.Name} 방열판의 먼지 · 서리를 닦았다 ({before * 100:0}% → {seg.RadiatorCondition * 100:0}%)");
                break;
            }
            case DroneErrand.Zone when ZoneStays(d):
                return false;
            case DroneErrand.Dig or DroneErrand.Raise or DroneErrand.Survey:
                w.Planet.DroneDone(d, d.Errand, d.GuardFor);
                break;
            case DroneErrand.Relay when w.Crew.Any(c => !c.Dead && c.Outside) && d.Battery > ReturnCost(d) + 0.2f:
                d.WorkDone = 0f; // 아직 밖에 사람이 있다 — 더 머문다
                d.WorkNeeded = 0.5f;
                return false;
        }
        d.GuardFor = -1;
        return true;
    }

    /// <summary>운석이 부딪히기 직전: 길목을 지키던 방패 드론이 받아 낸다 (작은 것은 부수고 · 큰 것은 줄인다). 막았으면 true.</summary>
    public bool Intercept(IncomingMeteor m)
    {
        var w = _world;
        // 길목에 닿았으면 (일하는 중) 몸으로 · 가는 중이면 요격 침으로 — 가까울수록 잘 맞는다
        var guard = Drones.FirstOrDefault(x => x.Kind == DroneKind.Shield && x.Errand == DroneErrand.Guard && x.GuardFor == m.Id && !x.Faulty && !x.Wrecked
            && x.State is DroneState.Working or DroneState.Outbound && x.Position.X > -50f);
        if (guard == null) return false;
        float far = guard.ErrandSpot is Vector2 sp ? (guard.Position - sp).Length() : 99f;
        float chance = guard.State == DroneState.Working || far < 4f ? 0.85f : far < 15f ? 0.6f : 0.35f;
        if (!R21.Chance(chance)) { w.Log.Add(w.Tick, LogKind.Ship, $"{guard.Name}: 운석을 놓쳤다 — 길목까지 {far:0}칸 남았다"); return false; }
        bool stop = m.Size <= 0.4f;
        if (!stop) { m.Size *= 0.5f; V21.Shrunk++; } else V21.Intercepts++;
        guard.Condition = MathF.Max(0f, guard.Condition - 0.25f);
        string where = m.Room?.Name ?? "선체";
        if (R21.Chance(0.25f))
        {
            guard.Faulty = true;
            V21.ShieldHurt++;
            MarkLog.Add(guard.Marks, w.Tick, "운석을 받다가 상했다");
        }
        MarkLog.Add(guard.Marks, w.Tick, stop ? "운석을 부쉈다" : "운석을 쳐서 줄였다");
        w.Log.Add(w.Tick, LogKind.Ship, $"{guard.Name}: {where} 쪽 운석을 {(stop ? "몸으로 받아 부쉈다 — 부딪히지 않았다" : $"쳐서 줄였다 (크기 절반) — 그래도 부딪힌다")}");
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(guard.Name)} {where}로 오던 운석을 {(stop ? "막았다" : "줄였다")}", m.Room);
        guard.Errand = DroneErrand.None;
        guard.GuardFor = -1;
        GoHome(guard);
        return stop;
    }

    /// <summary>중계 드론이 띄운 무전 세기 (그 자리에서 멀수록 약하다).</summary>
    public float RelaySignal(Vector2 at, float range)
    {
        float best = 0f;
        foreach (var d in Drones)
            if (d.Errand == DroneErrand.Relay && d.State is DroneState.Working or DroneState.Outbound && !d.Faulty)
                best = MathF.Max(best, Math.Clamp(1f - (at - d.Position).Length() / MathF.Max(1f, range * 0.8f), 0f, 1f));
        return best;
    }

    /// <summary>방패 드론: 운석이 올 때까지 길목에서 기다린다 (도착할 때 일할 시간을 다시 잡는다).</summary>
    internal float GuardHours(Drone d)
    {
        var m = _world.Sensors.Incoming.FirstOrDefault(x => x.Id == d.GuardFor);
        return m == null ? 0.1f : MathF.Max(0.1f, (m.Arrive - _world.Tick) / (float)SimTime.TicksPerHour + 0.05f);
    }
}
