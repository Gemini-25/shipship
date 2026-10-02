using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.11 드론의 부위별 피해: 파편은 상태 하나가 아니라 부위를 맞힌다.
//   추진기 → 빙글빙글 돌며 떠내려간다 (견인 드론 · 사람이 건져 와야) · 팔 → 싣고 있던 자재를 놓치고 일을 못 한다 ·
//   센서 → 더듬어 돌아온다 · 배터리 → 셀이 부풀어 오르다 터진다 (파편이 옆 드론 · 사람 · 선체를 맞힌다 — 같은 규칙, 연쇄).
//   부푼 배터리는 거치대에 들이지 않는다 (주 컴퓨터가 밖에 세워 방전시킨다 — 안에서 터지면 불).
//   떠다니는 잔해는 견인 드론이 건져 오면 부품 · 고철이 된다. 이름 붙은 드론을 잃으면 돌보던 사람이 아쉬워한다.
//   운석 경보: 해치는 한 대씩 — 귀환 순서를 정하고, 늦는 드론은 사람처럼 선체 그늘에 숨는다.

public sealed class DroneHurt
{
    public float Thruster { get; set; } = 1f;
    public float Arm { get; set; } = 1f;
    public float Sensor { get; set; } = 1f;
    public float Battery { get; set; } = 1f;
    /// <summary>배터리 부풂 0~1 (1이면 터진다).</summary>
    public float Swell { get; set; }
    public bool Tumbling { get; set; }
    public float Spin { get; set; }
    public float SpinRate { get; set; }
    public long HitAt { get; set; } = -1;
    public string? HitPart { get; set; }
    public long ExplodedAt { get; set; } = -1;
    public Vector2 BlastAt { get; set; }
    public bool Sheltering { get; set; }
    public Cell? ShelterAt { get; set; }
    public int RecallRank { get; set; }
    public int RecallFor { get; set; } = -1;
    public bool Parked { get; set; }
    public Vector2 ParkAt { get; set; }
    public int FetchPerson { get; set; } = -1;
    public int FetchFloater { get; set; } = -1;
    public int Keeper { get; set; } = -1;
    public Dictionary<int, int> Care { get; } = new();
    public bool Mourned { get; set; }
    /// <summary>사람이 연기를 보고 거치대 충전기를 뽑았다.</summary>
    public bool Unplugged { get; set; }
    public bool Damaged => Thruster < 0.95f || Arm < 0.95f || Sensor < 0.95f || Battery < 0.95f;
}

public sealed partial class Drone
{
    /// <summary>v16.11 부위별 상처 · 배터리 부풂 · 그늘 · 건지러 가는 것.</summary>
    public DroneHurt Hurt { get; } = new();
}

/// <summary>그림: 드론 폭발 한 번.</summary>
public sealed record DroneBlast(long Tick, Vector2 At, float Power, bool Inside, int DroneId, string Name);

public sealed partial class DroneSystem
{
    private Rng? _hurtRng;
    private Rng HR => _hurtRng ??= new Rng(unchecked(_world.Seed * 6911 + 433));
    public int Explosions, PartHits, Salvaged, SalvagedItems, PersonFetches, Sheltered, ParkedCount, Recalls;
    public List<DroneBlast> Blasts { get; } = new();
    public List<string> RecallLog { get; } = new();
    private int _recallFor = -1;

    public static string PartName(string part) => part;

    // ─────────────────────────────── 매 틱 (Drones.Step 훅) ───────────────────────────────

    /// <summary>true면 이 틱의 기본 움직임을 건너뛴다 (건지기 · 그늘 · 세워 두기는 여기서 움직인다).</summary>
    internal bool StepHurt(Drone d, Door? hatch)
    {
        var h = d.Hurt;
        const float hr = 1f / SimTime.TicksPerHour;
        if (d.State == DroneState.Lost) return false;
        if (h.Tumbling)
        {
            h.Spin += h.SpinRate * hr;
            return false;
        }
        if (h.FetchPerson >= 0) return StepFetchPerson(d);
        if (h.FetchFloater >= 0 && d.State == DroneState.Outbound) return StepFetchFloater(d);
        if (h.Sheltering) return StepHold(d, h.ShelterAt?.Center ?? d.Position, 0.15f);
        if (h.Parked) return StepHold(d, h.ParkAt, 0.6f);
        return false;
    }

    private bool StepHold(Drone d, Vector2 at, float drainMul)
    {
        const float hr = 1f / SimTime.TicksPerHour;
        if (d.State is DroneState.Docked or DroneState.Adrift) { d.Hurt.Sheltering = false; return false; }
        d.Battery = MathF.Max(0f, d.Battery - Drain(d.Kind) * drainMul * hr);
        var to = at - d.Position;
        float len = to.Length();
        float sp = Speed(d.Kind) * d.Quirk.Speed;
        if (len > 0.02f) { d.Position += to / len * MathF.Min(sp, len); d.Facing = to / len; }
        if (d.Battery <= 0f) { d.Hurt.Sheltering = false; d.Hurt.Parked = false; Strand(d, "숨어 있다 배터리가 바닥났다"); }
        return true;
    }

    private bool StepFetchPerson(Drone d)
    {
        var w = _world;
        var h = d.Hurt;
        const float hr = 1f / SimTime.TicksPerHour;
        var c = w.Crew.FirstOrDefault(x => x.Id == h.FetchPerson);
        var p = c == null ? null : w.EvaRisk.Of(c);
        void Cancel(string? why)
        {
            if (p != null && p.TowDrone == d.Id) p.TowDrone = -1;
            h.FetchPerson = -1;
            if (why != null) w.Log.Add(w.Tick, LogKind.Warning, $"{d.Name}: {why}");
        }
        if (c == null || p == null || c.CarriedBy != null || !p.Adrift && p.TowDrone != d.Id) { Cancel(null); if (d.State is DroneState.Outbound or DroneState.Towing) GoHome(d); return false; }
        switch (d.State)
        {
            case DroneState.Outbound:
            {
                d.Battery = MathF.Max(0f, d.Battery - Drain(d.Kind) * hr);
                d.FlightHours += hr;
                if (d.Route.Count > 0) d.Route[^1] = c.Position;
                bool arrived = Move(d, Speed(d.Kind) * d.Quirk.Speed);
                if (!arrived && (d.Position - c.Position).Length() > 0.6f) { if (d.Battery <= 0f) { Cancel(null); Strand(d, "건지러 가다 배터리가 바닥났다"); } return true; }
                if (p.TowDrone >= 0 && p.TowDrone != d.Id || p.TowedBy >= 0) { Cancel(null); GoHome(d); return true; }
                // 붙잡았다: 집게로 등짐을 문다
                p.TowDrone = d.Id;
                p.Vel = Vector2.Zero;
                p.SpinRate *= 0.2f;
                d.State = DroneState.Towing;
                d.StateSince = w.Tick;
                d.Doing = c.Dead ? $"{c.Name}의 시신을 끌고 온다" : $"{Ko.EulReul(c.Name)} 끌고 온다";
                var outer = EvaRiskSystem.HatchOuter(w);
                PlanRoute(d, outer?.Center ?? d.DockPosition);
                w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(d.Name)} 떠내려가던 {Ko.EulReul(c.Name)} 붙잡았다 — 해치로 끌고 온다");
                return true;
            }
            case DroneState.Towing:
            {
                d.Battery = MathF.Max(0f, d.Battery - 0.3f * hr);
                d.FlightHours += hr;
                bool arrived = Move(d, 0.05f * RobotsV15.Tow(d.Kind));
                c.PreviousPosition = c.Position;
                c.Position = d.Position - d.Facing * 0.38f;
                if (!arrived) { if (d.Battery <= 0f) { Cancel("배터리가 바닥나 놓쳤다"); Strand(d, "끌고 오다 배터리가 바닥났다"); } return true; }
                var outer = EvaRiskSystem.HatchOuter(w);
                if (outer is Cell oc) c.Position = oc.Center;
                h.FetchPerson = -1;
                PersonFetches++;
                w.EvaRisk.OnDroneRescue(c, d);
                GoHome(d);
                return true;
            }
            default:
                Cancel(d.State == DroneState.Returning ? "건지던 사람을 놓고 돌아온다" : null);
                return false;
        }
    }

    private bool StepFetchFloater(Drone d)
    {
        var w = _world;
        var h = d.Hurt;
        const float hr = 1f / SimTime.TicksPerHour;
        var f = w.EvaRisk.Floaters.FirstOrDefault(x => x.Id == h.FetchFloater);
        if (f == null || f.CarriedBy >= 0 && f.CarriedBy != d.Id) { h.FetchFloater = -1; GoHome(d); return true; }
        d.Battery = MathF.Max(0f, d.Battery - Drain(d.Kind) * hr);
        d.FlightHours += hr;
        if (d.Route.Count > 0) d.Route[^1] = f.Pos;
        bool arrived = Move(d, Speed(d.Kind) * d.Quirk.Speed);
        if (!arrived && (d.Position - f.Pos).Length() > 0.5f) return true;
        f.CarriedBy = d.Id;
        f.Vel = Vector2.Zero;
        h.FetchFloater = -1;
        d.Doing = $"{f.Name} 건져 온다";
        GoHome(d);
        return true;
    }

    // ─────────────────────────────── 건지러 가기 (견인 드론 · PlanFetch 훅) ───────────────────────────────

    /// <summary>떠내려간 사람(살아 있으면 먼저 · 시신도)을 건지러 간다.</summary>
    internal bool PlanPersonFetch(Drone d)
    {
        var w = _world;
        if (d.Hurt.Arm < 0.5f || d.Battery < 0.55f) return false;
        if (w.EvaRisk.FetchCandidate(d) is not (CrewMember c, EvaPerson p)) return false;
        d.Hurt.FetchPerson = c.Id;
        Launch(d, c.Position, c.Dead ? $"{c.Name}의 시신을 건지러" : $"떠내려간 {Ko.EulReul(c.Name)} 건지러");
        return true;
    }

    /// <summary>떠다니는 잔해 · 공구를 건지러 간다 (할 일이 없을 때).</summary>
    internal void PlanSalvage(Drone d)
    {
        var w = _world;
        if (d.Hurt.Arm < 0.5f || d.Battery < 0.9f || w.Sensors.Alarm != null || w.Hazards.StormActive) return;
        var f = w.EvaRisk.Floaters.Where(x => x.CarriedBy < 0 && !x.Lost && x.Kind != FloatKind.Shard && x.Heat < 0.3f && (x.Pos - w.Structure.ShipCenter).Length() < StructureSystem.LostRange)
            .Where(x => !Drones.Any(o => o.Hurt.FetchFloater == x.Id))
            .OrderBy(x => (x.Pos - d.Position).LengthSquared()).FirstOrDefault();
        if (f == null) return;
        d.Hurt.FetchFloater = f.Id;
        Launch(d, f.Pos, $"떠다니는 {Ko.EulReul(f.Name)} 건지러");
    }

    // ─────────────────────────────── 파편 ───────────────────────────────

    /// <summary>운석 파편: 밖의 드론이 부위별로 맞는다 (선체 그늘이면 덜 — 사람과 같은 규칙).</summary>
    public void OnMeteorParts(Cell entry, float size, Vector2 dir)
    {
        var w = _world;
        w.Fleet.Struck(entry); // v16.20b 자주 맞는 쪽을 배운다 (외벽 순찰 순서)
        var from = entry.Center;
        float r = 3f + 2f * size;
        foreach (var d in Drones.ToList())
        {
            if (d.State is DroneState.Docked or DroneState.Lost) continue;
            float dist = (d.Position - from).Length();
            if (dist > r) continue;
            float power = 0.6f * size * (1f - dist / (r + 0.5f)) * HR.Range(0.6f, 1.2f) * w.EvaRisk.Exposure(d.Position, from, d.Hurt.Sheltering);
            if (power < 0.02f) continue;
            HitDrone(d, power, from, "운석 파편");
        }
    }

    /// <summary>드론 한 대가 맞았다: 부위를 고르고 그 부위의 일이 생긴다.</summary>
    public void HitDrone(Drone d, float power, Vector2 from, string cause)
    {
        var w = _world;
        var h = d.Hurt;
        if (d.State == DroneState.Lost) return;
        power *= Durability.DroneHurt * w.Fleet.Hurt; // v16.20b 외피 등급 · v16.19 두꺼운 외피 · 충격 흡수 다리
        PartHits++;
        h.HitAt = w.Tick;
        d.Condition = MathF.Max(0f, d.Condition - power);
        var away = d.Position - from;
        away = away.LengthSquared() < 0.0001f ? new Vector2(0f, 1f) : Vector2.Normalize(away);
        float roll = HR.Float() * 100f;
        string part = roll < 30f ? "추진기" : roll < 52f ? "팔" : roll < 72f ? "센서" : "배터리";
        float dmg = power * HR.Range(1.1f, 2f);
        h.HitPart = part;
        string note = "";
        bool outside = d.State != DroneState.Docked;
        switch (part)
        {
            case "추진기":
                h.Thruster = MathF.Max(0f, h.Thruster - dmg);
                if (h.Thruster < 0.5f && outside && !h.Tumbling)
                {
                    Strand(d, $"{cause}에 추진기가 상해 빙글빙글 떠내려간다");
                    d.DriftVelocity = away * (6f + 30f * power);
                    h.Tumbling = true;
                    h.SpinRate = (HR.Chance(0.5f) ? -1f : 1f) * (250f + 600f * power);
                    d.Faulty = true;
                    note = "추진기 — 빙글빙글 떠내려간다";
                }
                else note = $"추진기 {h.Thruster * 100:0}%";
                break;
            case "팔":
                h.Arm = MathF.Max(0f, h.Arm - dmg);
                if (h.Arm < 0.5f)
                {
                    d.Faulty = true;
                    // 싣고 있던 자재를 놓친다 → 떠다닌다
                    foreach (var (k, n) in d.Cargo)
                        w.EvaRisk.Spawn(k is ItemKind.Plate or ItemKind.Structure ? FloatKind.Plate : FloatKind.Scrap, $"{d.Name}이 놓친 {ItemKinds.Name(k)}", d.Position,
                            away * HR.Range(4f, 12f) + new Vector2(HR.Range(-4f, 4f), HR.Range(-4f, 4f)), new[] { (k, n) }, -1, d.Id);
                    d.Cargo = Array.Empty<(ItemKind, int)>();
                    if (outside && d.State != DroneState.Adrift) Abort(d, "팔이 꺾였다 — 일을 두고 돌아온다");
                    note = "팔이 꺾였다";
                }
                else note = $"팔 {h.Arm * 100:0}%";
                break;
            case "센서":
                h.Sensor = MathF.Max(0f, h.Sensor - dmg);
                if (h.Sensor < 0.5f)
                {
                    d.Faulty = true;
                    if (outside && d.State != DroneState.Adrift) Abort(d, "센서가 깨졌다 — 더듬어 돌아온다");
                    note = "센서가 깨졌다";
                }
                else note = $"센서 {h.Sensor * 100:0}%";
                break;
            default:
                h.Battery = MathF.Max(0f, h.Battery - dmg);
                d.Battery = MathF.Max(0f, d.Battery - dmg * 0.3f);
                if (h.Battery < 0.55f)
                {
                    h.Swell = MathF.Max(h.Swell, 0.05f + (0.55f - h.Battery));
                    if (h.Battery < 0.15f) h.Swell = MathF.Max(h.Swell, 0.93f); // 셀이 찢겼다 — 곧 터진다
                    note = h.Swell > 0.9f ? "배터리 셀이 찢겼다 — 곧 터진다" : "배터리 셀이 찌그러졌다 — 부풀기 시작";
                    d.Faulty = true;
                }
                else note = $"배터리 {h.Battery * 100:0}%";
                break;
        }
        MarkLog.Add(d.Marks, w.Tick, $"{cause} — {note}");
        w.Log.Add(w.Tick, LogKind.Warning, $"{d.Name} — {cause}에 {note}");
        if (d.Condition < 0.1f && !d.Wrecked && outside)
        {
            d.Wrecked = true;
            MarkLog.Add(d.Marks, w.Tick, $"{cause}에 부서졌다");
            w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(d.Name)} {cause}에 부서졌다", log: true);
            if (d.State != DroneState.Adrift) Strand(d, $"{cause}에 부서졌다");
            Debris(d, 2, away, 0f);
        }
    }

    /// <summary>잔해 조각을 뿌린다 (모양마다 건지면 나오는 것이 다르다).</summary>
    private void Debris(Drone d, int n, Vector2 away, float heat)
    {
        var w = _world;
        var kinds = new[]
        {
            (FloatKind.Plate, "외피 판", new[] { (ItemKind.Plate, 1) }),
            (FloatKind.Motor, "추진기 모터", new[] { (ItemKind.Motor, 1) }),
            (FloatKind.Board, "회로판", new[] { (ItemKind.Electronics, 1) }),
            (FloatKind.Rotor, "날개", new[] { (ItemKind.MetalOre, 1) }),
            (FloatKind.Cell, "배터리 셀", new[] { (ItemKind.CellPack, 1) }),
            (FloatKind.Scrap, "고철", new[] { (ItemKind.MetalOre, 2) }),
        };
        for (int i = 0; i < n; i++)
        {
            var (k, name, y) = kinds[HR.Range(0, kinds.Length)];
            if (k == FloatKind.Cell && heat > 0.5f) y = new[] { (ItemKind.MetalOre, 1) }; // 터진 셀은 쓸 수 없다
            float ang = HR.Float() * MathF.Tau;
            var v = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * HR.Range(3f, 11f) + away * 3f;
            w.EvaRisk.Spawn(k, $"{d.Name}의 {name}", d.Position, v, y, -1, d.Id, -1, heat * HR.Range(0.6f, 1f));
        }
    }

    // ─────────────────────────────── 배터리 · 폭발 ───────────────────────────────

    /// <summary>배터리가 터졌다: 섬광 · 파편이 옆 드론 · 사람 · 떠다니는 것 · 선체 · 외부 설비를 맞힌다 (배터리를 맞으면 그것도 부푼다 — 연쇄).</summary>
    public void Explode(Drone d)
    {
        var w = _world;
        var h = d.Hurt;
        if (h.ExplodedAt >= 0) return;
        bool inside = d.State == DroneState.Docked;
        var at = inside ? d.DockPosition : d.Position;
        float power = 0.45f + 0.5f * MathF.Max(0.2f, d.Battery);
        h.ExplodedAt = w.Tick;
        h.BlastAt = at;
        Explosions++;
        if (d.Towing != null || d.Order != null || d.Fetching != null) { if (d.Towing is Fragment fr) { fr.State = FragmentState.Adrift; fr.Tug = null; d.Towing = null; } if (d.Order != null) { d.Order.Drone = null; d.Order = null; } if (d.Fetching is Drone x) { x.State = DroneState.Adrift; d.Fetching = null; } }
        if (h.FetchPerson >= 0 && w.EvaRisk.Of(h.FetchPerson) is EvaPerson fp) { fp.TowDrone = -1; h.FetchPerson = -1; }
        d.Wrecked = true;
        d.State = DroneState.Lost;
        d.StateSince = w.Tick;
        d.Battery = 0f;
        d.Doing = "배터리 폭발";
        d.Cargo = Array.Empty<(ItemKind, int)>();
        Losses++;
        Blasts.Add(new DroneBlast(w.Tick, at, power, inside, d.Id, d.Name));
        if (Blasts.Count > 8) Blasts.RemoveAt(0);
        float r = 2.5f + 3f * power;
        var hit = new List<string>();
        // 옆 드론 (같은 규칙: 부위별)
        foreach (var x in Drones.ToList())
        {
            if (x == d || x.State == DroneState.Lost) continue;
            bool xin = x.State == DroneState.Docked;
            if (xin != inside || inside && x.Dock.Room != d.Dock.Room) continue;
            var xp = xin ? x.DockPosition : x.Position;
            float dist = (xp - at).Length();
            if (dist > r) continue;
            float shield = inside ? 1f : w.EvaRisk.Exposure(xp, at, x.Hurt.Sheltering);
            float pw = power * (1f - dist / (r + 0.3f)) * HR.Range(0.7f, 1.2f) * shield;
            if (pw < 0.03f) continue;
            HitDrone(x, pw, at, $"{d.Name} 폭발 파편");
            hit.Add(x.Name);
        }
        // 사람
        var room = inside ? d.Dock.Room : null;
        foreach (var c in w.Crew.ToList())
        {
            if (c.Dead) continue;
            float dist = (c.Position - at).Length();
            if (dist > r) continue;
            if (inside)
            {
                if (c.Room != room) continue;
                float pw = power * (1f - dist / (r + 0.3f));
                c.Vitals.Health = MathF.Max(0f, c.Vitals.Health - pw * 0.35f);
                NeedsSystem.AddInjury(c.Vitals, pw * 0.4f, "폭발 (드론 배터리)");
                Memory.Frighten(w, c, room, 0.35f, $"{d.Name} 배터리가 거치대에서 터졌다");
                c.Interrupt(w);
                hit.Add(c.Name);
            }
            else if (c.Outside)
            {
                float pw = power * (1f - dist / (r + 0.3f)) * 0.8f * w.EvaRisk.Exposure(c.Position, at, false);
                if (pw < 0.03f) continue;
                w.EvaRisk.HitPerson(c, pw, at, $"{d.Name} 배터리 폭발 파편");
                hit.Add(c.Name);
            }
        }
        // 떠다니는 것 · 선체 · 외부 설비
        w.EvaRisk.Push(at, r + 2f, 30f * power);
        if (!inside)
        {
            foreach (var (cell, wall) in w.Ship.Walls.ToList())
                if (wall.IsHull && (cell.Center - at).Length() < 1.7f) Hull.Damage(w.Ship, cell, 0.12f * power);
            var ac = Cell.FromPosition(at);
            if (w.Exterior.All.Any(f => (f.Anchor.Center - at).Length() < 2.5f)) w.Exterior.Hit(ac, 0.3f * power, "드론 폭발");
            Debris(d, 4 + HR.Range(0, 4), Vector2.Zero, 1f);
        }
        else
        {
            var dc = Cell.FromPosition(d.DockPosition);
            w.Fire.Ignite(dc, 0.35f + 0.3f * power);
            w.RaiseAlert($"{d.Name} 배터리 폭발 — {room?.Name ?? "거치대"} (불)", room, AlertLevel.Critical, shipWide: true);
        }
        MarkLog.Add(d.Marks, w.Tick, $"배터리가 터졌다 ({(inside ? "거치대에서" : "선체 밖에서")})");
        w.History.Add(w, HistoryKind.Damage, $"{d.Name}의 배터리가 {(inside ? $"{room?.Name ?? "거치대"}에서" : "선체 밖에서")} 터졌다" + (hit.Count > 0 ? $" — 파편: {string.Join("·", hit)}" : ""), room, log: true);
        Mourn(d, "배터리가 터져 잃었다");
    }

    /// <summary>시스템 틱: 배터리 부풂 · 주 컴퓨터가 부푼 드론을 밖에 세운다 · 경보 귀환 순서 · 잃은 드론 아쉬움 · 건져 온 사람 정리.</summary>
    internal void HurtUpdate(float dt)
    {
        var w = _world;
        var au = w.Automation;
        foreach (var d in Drones.ToList())
        {
            var h = d.Hurt;
            if (d.State == DroneState.Lost) { if (!h.Mourned) Mourn(d, "시야 밖으로 떠내려가 잃었다"); continue; }
            if (h.Tumbling && d.State == DroneState.Docked) h.Tumbling = false;
            if (h.FetchFloater >= 0 && d.State == DroneState.Docked) h.FetchFloater = -1;
            if (h.Swell > 0f && h.ExplodedAt < 0)
            {
                bool charging = d.State == DroneState.Docked && DockWorking(d) && d.Battery < 1f && !h.Unplugged;
                if (h.Unplugged && d.State == DroneState.Docked && DockWorking(d)) d.Battery = MathF.Max(0f, d.Battery - ChargePerHour * d.Dock.Machine!.Efficiency * dt); // 뽑았으니 충전되지 않는다
                float rate = h.Battery >= 0.55f ? -0.3f : (h.Battery < 0.3f ? 0.9f : 0.45f) * (0.4f + d.Battery) * (charging ? 2.5f : 1f);
                h.Swell = Math.Clamp(h.Swell + rate * dt, 0f, 1f);
                if (h.Swell >= 1f) { Explode(d); continue; }
                // 주 컴퓨터: 셀 온도를 읽는다 → 들이지 않는다 / 거치대에서 내보낸다
                if (h.Swell > 0.25f && !h.Parked && au.Present && au.MainOnline && DroneControl())
                {
                    h.Parked = true;
                    ParkedCount++;
                    var outer = EvaRiskSystem.HatchOuter(w);
                    var away = (outer?.Center ?? d.Position) - w.Structure.ShipCenter;
                    away = away.LengthSquared() < 0.01f ? new Vector2(0f, 1f) : Vector2.Normalize(away);
                    h.ParkAt = (outer?.Center ?? d.Position) + away * 5f + new Vector2(away.Y, -away.X) * (d.Slot - 1) * 1.6f;
                    if (d.State == DroneState.Docked) { d.State = DroneState.Returning; d.StateSince = w.Tick; w.CycleAirlock(World.DronePortCost); }
                    else { if (d.Order != null) { d.Order.Drone = null; d.Order = null; } if (d.Towing != null) ReleaseTow(d, "배터리가 부푼다"); d.State = DroneState.Returning; }
                    d.Doing = "배터리 부풂 — 밖에 세워 방전";
                    au.Book.Add(ActKind.Advice, d.Dock.Room, $"{d.Name} 배터리 셀 온도 상승 · 부풂 {h.Swell * 100:0}%", "거치대에서 터지면 불 — 들이면 안 된다", "밖에 세워 두고 방전", "정비: 배터리 셀 교체",
                        $"drone:park:{d.Id}", SimTime.Minutes(30), 90f, (world, a) => d.Hurt.ExplodedAt >= 0 ? (d.State == DroneState.Docked ? 2 : 1, d.Hurt.ExplodedAt >= 0 ? "밖에서 터졌다 — 안은 무사" : "식었다") : d.Hurt.Swell <= 0f ? (1, "식었다") : null);
                    w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {d.Name} 배터리가 부푼다 — 거치대에 들이지 않고 밖에 세워 방전시킨다");
                }
                // 식었다: 다시 들인다
                if (h.Parked && h.Swell <= 0f)
                {
                    h.Parked = false;
                    GoHome(d);
                    w.Log.Add(w.Tick, LogKind.Ship, $"{d.Name} 배터리가 식었다 — 거치대로 들인다 (셀 교체가 필요하다)");
                }
                // 컴퓨터가 없으면: 거치대 방 사람이 연기를 보고 충전기를 뽑는다
                if (d.State == DroneState.Docked && h.Swell > 0.55f && !(au.Present && au.MainOnline))
                    foreach (var c in w.Crew)
                        if (!c.Dead && c.CanAct && c.Room == d.Dock.Room && c.IsAwake && !h.Unplugged)
                        {
                            h.Unplugged = true;
                            w.Log.Add(w.Tick, LogKind.Warning, $"{d.Name}에서 연기 — 거치대 충전기를 뽑았다", c.Id);
                            Memory.Frighten(w, c, d.Dock.Room, 0.1f, "드론 배터리가 부풀어 연기가 났다");
                            break;
                        }
            }
            if (h.Swell <= 0f && h.Parked) { h.Parked = false; GoHome(d); }
        }
        Recall();
    }

    private bool DroneControl() => _world.Automation.DroneControl;

    /// <summary>운석 경보: 해치는 한 대씩 — 사람을 끄는 드론 먼저, 배터리가 없는 드론 · 오래 일한 드론 순. 늦으면 그늘로.</summary>
    private void Recall()
    {
        var w = _world;
        var inc = w.Sensors.Alarm;
        if (inc == null || inc.Warned < WarnLevel.Manual || inc.MinutesLeft(w.Tick) <= 0f)
        {
            if (_recallFor >= 0 && (inc == null || inc.Id != _recallFor))
            {
                _recallFor = -1;
                foreach (var d in Drones)
                    if (d.Hurt.Sheltering) { d.Hurt.Sheltering = false; if (FleetResume(d)) continue; GoHome(d); w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: 파편이 지나갔다 — 그늘에서 나와 돌아온다"); }
            }
            return;
        }
        if (inc.Id == _recallFor) return;
        _recallFor = inc.Id;
        float left = inc.MinutesLeft(w.Tick);
        var outside = Drones.Where(d => d.State is DroneState.Outbound or DroneState.Working or DroneState.Towing or DroneState.Returning && !d.Hurt.Parked && !d.Hurt.Tumbling).ToList();
        if (outside.Count == 0) return;
        float Eta(Drone d) => FlightLength(d, d.Position, d.DockPosition) / ((d.State == DroneState.Towing ? TowSpeed : Speed(d.Kind)) * SimTime.TicksPerHour / 60f);
        int Klass(Drone d) => d.Hurt.FetchPerson >= 0 ? 0 : d.Towing != null ? 1 : d.Battery < 0.3f ? 2 : 3;
        var order = outside.OrderBy(Klass).ThenByDescending(d => d.Sorties).ThenBy(Eta).ThenBy(d => d.Id).ToList();
        float t = 0f;
        int rank = 1;
        RecallLog.Clear();
        foreach (var d in order)
        {
            float eta = Eta(d);
            float arrive = MathF.Max(eta, t + 0.5f); // 해치 드론 포트는 한 대씩 (30초)
            if (arrive + 0.2f < left && !FleetShelters(d) || d.Hurt.FetchPerson >= 0) // v16.20b 맡은 파공 곁에서 그늘로 피했다 다시
            {
                d.Hurt.RecallRank = rank;
                d.Hurt.RecallFor = inc.Id;
                t = arrive;
                RecallLog.Add($"{rank}. {d.Name} ({arrive:0.#}분)");
                rank++;
                Recalls++;
                if (d.State != DroneState.Returning && d.Hurt.FetchPerson < 0) Abort(d, $"운석 경보 — 귀환 {d.Hurt.RecallRank}번째");
            }
            else
            {
                var sh = DroneShelter(d, inc.Entry.Center);
                if (d.Towing != null) ReleaseTow(d, "운석 경보 — 잡아 세워 두고 그늘로");
                if (d.Order != null) { if (!_world.Fleet.DroneTask.ContainsKey(d.Id)) d.Order.Drone = null; d.Order = null; } // v16.20b 맡은 파공은 그늘에 숨어 있는 동안에도 이 드론 몫
                d.Hurt.Sheltering = true;
                d.Hurt.ShelterAt = sh;
                d.Hurt.RecallRank = 0;
                d.State = DroneState.Returning;
                d.StateSince = w.Tick;
                d.Doing = "운석 경보 — 선체 그늘에 숨는다";
                d.Route.Clear();
                Sheltered++;
                RecallLog.Add($"{d.Name} → 그늘 ({eta:0.#}분 — 늦다)");
            }
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"운석 경보 {left:0.#}분 — 드론 귀환 순서: {string.Join(" · ", RecallLog)}");
        if (w.Automation.Present && w.Automation.MainOnline)
            w.Automation.Book.Add(ActKind.Advice, w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault(), $"운석 {left:0.#}분 · 밖의 드론 {outside.Count}대", "드론 포트는 한 대씩 — 늦는 드론은 그늘", string.Join(" · ", RecallLog), "", $"drone:recall:{inc.Id}", SimTime.Minutes(5), 10f);
    }

    /// <summary>드론이 숨을 그늘 (선체에 가려지는 우주 칸 — 사람과 같은 규칙).</summary>
    private Cell? DroneShelter(Drone d, Vector2 from)
    {
        var w = _world;
        var grid = w.Ship.Grid;
        var at = Cell.FromPosition(d.Position);
        Cell? best = null;
        float bd = float.MaxValue;
        for (int dy = -10; dy <= 10; dy++)
        for (int dx = -10; dx <= 10; dx++)
        {
            var c = new Cell(at.X + dx, at.Y + dy);
            if (!grid.InBounds(c) || grid.Kind(c) != TileKind.Void || !w.Paths.IsSpace(c)) continue;
            if (!w.EvaRisk.LineBlocked(from, c.Center)) continue;
            float dd = (c.Center - d.Position).LengthSquared();
            if (dd < bd) { bd = dd; best = c; }
        }
        return best;
    }

    // ─────────────────────────────── 정비 · 아쉬움 ───────────────────────────────

    /// <summary>부위를 갈아 끼우는 데 드는 것 (정비 비용에 더한다).</summary>
    public static (ItemKind kind, int count)[] WithParts(Drone d, (ItemKind kind, int count)[] baseCost)
    {
        var h = d.Hurt;
        if (!h.Damaged) return baseCost;
        var list = baseCost.ToList();
        void Add(ItemKind k, int n) { int i = list.FindIndex(x => x.kind == k); if (i >= 0) list[i] = (k, list[i].count + n); else list.Add((k, n)); }
        if (h.Thruster < 0.95f) Add(ItemKind.Motor, 1);
        if (h.Arm < 0.95f) Add(ItemKind.Gear, 1);
        if (h.Sensor < 0.95f) Add(ItemKind.Sensor, 1);
        if (h.Battery < 0.95f) Add(ItemKind.CellPack, 1);
        return list.ToArray();
    }

    /// <summary>정비를 마쳤다: 상한 부위를 갈았다 · 돌보는 사람을 기억한다.</summary>
    internal void HealParts(Drone d, CrewMember by)
    {
        var h = d.Hurt;
        h.Care[by.Id] = h.Care.GetValueOrDefault(by.Id) + 1;
        int best = -1, n = 0;
        foreach (var kv in h.Care.OrderBy(k => k.Key)) if (kv.Value > n) { n = kv.Value; best = kv.Key; }
        h.Keeper = best;
        if (!h.Damaged && h.Swell <= 0f) return;
        var parts = new List<string>();
        if (h.Thruster < 0.95f) parts.Add("추진기");
        if (h.Arm < 0.95f) parts.Add("팔");
        if (h.Sensor < 0.95f) parts.Add("센서");
        if (h.Battery < 0.95f) parts.Add("배터리 셀");
        h.Thruster = h.Arm = h.Sensor = h.Battery = 1f;
        h.Swell = 0f;
        h.Tumbling = false;
        h.Parked = false;
        h.Unplugged = false;
        if (parts.Count > 0) MarkLog.Add(d.Marks, _world.Tick, $"{Ko.IGa(by.Name)} {string.Join("·", parts)} 갈았다");
    }

    /// <summary>이름 붙은 드론을 잃었다: 돌보던 사람이 아쉬워한다.</summary>
    private void Mourn(Drone d, string why)
    {
        var w = _world;
        var h = d.Hurt;
        if (h.Mourned) return;
        h.Mourned = true;
        var keeper = w.Crew.FirstOrDefault(c => c.Id == h.Keeper && !c.Dead)
                     ?? w.Crew.Where(c => !c.Dead && !c.IsChild && c.Role is CrewRole.Technician or CrewRole.Engineer).OrderBy(c => c.Id).FirstOrDefault();
        var touched = new List<CrewMember>();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            float bond = c == keeper ? 1f : h.Care.ContainsKey(c.Id) ? 0.5f : 0f;
            if (bond <= 0f) continue;
            touched.Add(c);
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f + 0.04f * bond * MathF.Min(1f, d.Sorties / 20f));
            Memory.Shake(w, c, 0.015f * bond, $"{Ko.EulReul(d.Name)} 잃었다");
            Life.Diary(w, c, Persona.Say(c, c == keeper
                ? $"{Ko.EulReul(d.Name)} 잃었다. {d.Sorties}번을 나갔다 돌아온 녀석이었다"
                : $"{Ko.IGa(d.Name)} 없으니 거치대가 허전하다"));
        }
        w.History.Add(w, HistoryKind.Memory, $"{Ko.EulReul(d.Name)} 잃었다 — {why} · 출동 {d.Sorties}번 · 비행 {d.FlightHours:0}시간" + (keeper != null ? $" · {Ko.IGa(keeper.Name)} 아쉬워한다" : ""), null, touched);
    }
}
