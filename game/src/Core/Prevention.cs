using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>사고 전조의 종류 (v11.0).</summary>
public enum OmenKind
{
    Heat,      // 배선 과열: 전선·접점이 달아오른다 → 배선 불량·단락, 가끔 불
    Vibration, // 진동: 베어링·펌프·압축기가 떨린다 → 베어링 마모·고착
    Pressure,  // 압력 변화: 필터·막·관이 막히거나 샌다 → 막힘·오염
    Drift,     // 계기 이상: 센서·제어 신호가 흔들린다 → 센서 오차·제어 불량
}

/// <summary>
/// 설비에 깃든 사고 전조 (v11.0). 닳아서 날 고장의 일부는 곧바로 오지 않고 몇 시간~하루쯤 먼저 기척을 낸다.
/// 감지기(주 컴퓨터·전기)·당직(그 방에 있는 사람의 솜씨)·순찰(방재 로봇·사람)이 먼저 보면 싼 재료로 손봐 고장을 막고,
/// 아무도 못 보면 그대로 고장 난다 (배선 과열은 불이 되기도 한다).
/// </summary>
public sealed class Omen
{
    public OmenKind Kind { get; init; }
    public FaultKind Fault { get; init; }
    public long Since { get; init; }
    public long Due { get; set; } // v15.9 전조 분석이 늦출 수 있다
    public bool Known { get; set; }
    public string? KnownBy { get; set; }
    public long KnownAt { get; set; }

    /// <summary>v12.0 진짜 원인 (같은 기척에도 원인이 여럿 — 열어 봐야 안다). Phantom이면 설비는 멀쩡하고 감지기가 틀렸다.</summary>
    public OmenCause Cause { get; init; } = OmenCause.LooseMount;

    /// <summary>v12.0 당직 일지의 기록 (누가 보기 전엔 없다).</summary>
    public ShiftNote? Note { get; set; }

    /// <summary>0~1: 얼마나 무르익었나 (기척이 커질수록 알아채기 쉽다).</summary>
    public float Level(long now) => Due <= Since ? 1f : Math.Clamp((now - Since) / (float)(Due - Since), 0f, 1f);
}

public sealed class PreventionStats
{
    public int Omens;
    public int Detected;
    public int BySensor;
    public int ByCrew;
    public int ByRobot;
    public int ByRounds;
    public int Prevented;
    public int Missed;
    public int MissedFires;

    public override string ToString() =>
        $"전조 {Omens} · 발견 {Detected}(감지기 {BySensor} · 당직 {ByCrew} · 로봇 순찰 {ByRobot} · 사람 순찰 {ByRounds}) · 막음 {Prevented} · 놓침 {Missed}(불 {MissedFires})";
}

public static class Prevention
{
    public static string Name(OmenKind k) => k switch
    {
        OmenKind.Heat => "배선 과열",
        OmenKind.Vibration => "진동",
        OmenKind.Pressure => "압력 변화",
        OmenKind.Drift => "계기 이상",
        _ => k.ToString(),
    };

    public static OmenKind? KindOf(FaultKind f) => f switch
    {
        FaultKind.BearingWear or FaultKind.PumpSeized or FaultKind.CompressorFail or FaultKind.ArmMotor or FaultKind.InjectorClog => OmenKind.Vibration,
        FaultKind.WiringFault or FaultKind.ShortCircuit or FaultKind.HeatingElement or FaultKind.ElectrolyzerFault or FaultKind.CellDegradation
            or FaultKind.ChargerFault or FaultKind.StorageFault or FaultKind.FurnaceFault => OmenKind.Heat,
        FaultKind.FilterClogged or FaultKind.MembraneFouling or FaultKind.NutrientClog or FaultKind.HopperJam or FaultKind.Jam => OmenKind.Pressure,
        FaultKind.SensorDrift or FaultKind.ControlFault or FaultKind.AntennaDrift or FaultKind.RadarFault or FaultKind.DisplayFault
            or FaultKind.SensorFouling or FaultKind.LightFailure => OmenKind.Drift,
        _ => FaultsV15.Omen(f), // v15
    };

    /// <summary>전조를 손보는 데 드는 것 (고장을 고치는 것보다 훨씬 싸다).</summary>
    public static (ItemKind kind, int count)[] FixCost(OmenKind k) => k switch
    {
        OmenKind.Heat => new[] { (ItemKind.Cable, 1) },
        OmenKind.Vibration => new[] { (ItemKind.Lubricant, 1) },
        OmenKind.Pressure => new[] { (ItemKind.Filter, 1) },
        _ => Array.Empty<(ItemKind, int)>(),
    };

    public static float FixHours(OmenKind k) => k switch { OmenKind.Drift => 0.3f, OmenKind.Heat => 0.5f, _ => 0.4f };

    /// <summary>
    /// 닳아서 고장이 날 차례: 일부는 전조부터 낸다. 전조를 냈으면 true (지금은 고장 나지 않는다).
    /// 배전반 차단기 트립처럼 기척 없이 오는 고장, 핵심 부품이 없는 설비, 이미 전조가 있는 설비는 그냥 고장 난다.
    /// </summary>
    public static bool Foreshadow(World w, Machine m)
    {
        if (m.Omen != null || m.Faults.Count > 0) return false;
        var choices = m.Spec.FaultKinds.Where(k => k != FaultKind.BreakerTrip && KindOf(k) != null && !m.Has(k)).ToList();
        if (choices.Count == 0) return false;
        if (!w.Rng.Chance(Tuning.OmenShare)) return false;
        var fault = w.Rng.Pick(choices);
        float hours = w.Rng.Range(8f, 30f);
        var kind = KindOf(fault)!.Value;
        // v12.0 진짜 원인: 같은 기척을 내는 원인 중 하나 (무게대로)
        var cause = Causes.Weighted(Causes.For(m.Body.Type, kind), w.Rng.Float());
        m.Omen = new Omen { Kind = kind, Fault = fault, Cause = cause, Since = w.Tick, Due = w.Tick + SimTime.Hours(hours) };
        w.Precursors.Omens++;
        return true;
    }

    /// <summary>시스템 틱: 전조가 무르익고, 감지기·당직이 알아채고, 때가 되면 고장 난다.</summary>
    public static void Update(World w, float dt)
    {
        var st = w.Precursors;
        foreach (var m in w.Ship.Machines.ToList())
        {
            if (m.Omen is not Omen o) continue;
            if (m.Body.Room.OffLimits) { m.Omen = null; continue; }
            float level = o.Level(w.Tick);
            bool phantom = o.Cause == OmenCause.Phantom;
            if (!w.PreventionBlind)
            {
                // 감지기: 주 컴퓨터가 돌고 그 방에 전기가 있으면 (열·압력은 잘 잡고, 진동·계기는 덜).
                // v12.0 교정이 틀어질수록 덜 잡는다 · 계기 오류는 감지기만 낸다 (틀어진 감지기가 보는 헛것)
                bool logged = o.Note is { Logged: true };
                float sensor = !logged && !w.Watch.NoSensors && w.Automation.MainOnline && m.Body.Room.Powered && m.Body.Room.DataLinked
                    ? (phantom ? 0.5f : o.Kind switch { OmenKind.Heat => 0.22f, OmenKind.Pressure => 0.18f, OmenKind.Drift => 0.12f, _ => 0.05f }) * (0.4f + 0.6f * m.SensorCal)
                      * (!phantom && w.Automation.Level >= 4 ? 1.5f : 1f) * (phantom ? 1f : ErasV15.Mul(w, "omen")) * ComputerV15.OmenSensorMul(w, phantom) : 0f; // v12.5 IV 추론: 전조를 먼저 본다 · v15.5 감지 기술 · v15.9 전조 분석
                if (sensor > 0f && w.Rng.Chance(sensor * (phantom ? 5f : 0.3f + 0.7f * level) * dt))
                    Detect(w, m, o, "감지기", null);
                else if (!phantom)
                {
                    // 당직: 그 방에서 깨어 일하는 사람의 귀와 코 (솜씨·성실함만큼, 캄캄하면 덜).
                    // v12.0 그 설비를 여러 번 만진 사람은 평소와 다른 소리를 먼저 알아챈다 — 진동은 옆방에서도 (친숙함이 깊으면)
                    foreach (var c in w.Crew)
                    {
                        if (!c.CanAct || !c.IsAwake || c.Room == null) continue;
                        if (o.Note is ShiftNote seen && w.Watch.Knows(seen, c)) continue;
                        float fam = c.FamiliarityWith(m.Body.Type);
                        bool here = c.Room == m.Body.Room;
                        bool nextDoor = !here && o.Kind == OmenKind.Vibration && fam >= 0.4f && c.Room.Doors.Any(d => d.RoomA == m.Body.Room || d.RoomB == m.Body.Room);
                        if (!here && !nextDoor) continue;
                        float p = (0.04f + 0.22f * c.SkillLevel(m.Spec.Skill)) * (0.6f + 0.6f * c.Traits.Diligence) * (0.3f + 0.7f * level) * (1f + 0.8f * fam);
                        if (nextDoor) p *= 0.35f;
                        if (m.Body.Room.Dark) p *= 0.5f;
                        bool again = fam >= 0.3f && w.Parts.Owner(m, o.Fault) is { Failures: > 0 }; // v14.6 전에 나갔던 그 부품 — 귀에 익은 소리
                        if (again) p *= 1.6f;
                        if (w.Culture.Follows(c, CustomKind.MaintainerWay)) p *= 1.35f;
                        p *= ModulesV15.OmenMul(m.Body.Room, o.Kind); // v15 진동 감시기 · 열화상 · 누설 감지기 · 교정 장비 // v14.9 정전 속에서 배를 살린 정비사의 방식 — 소리부터 듣는다
                        if (w.Rng.Chance(p * dt)) { if (again) w.Parts.Stats.Noticed++; Detect(w, m, o, nextDoor ? "옆방에서 들음" : "당직", c); break; }
                    }
                }
            }
            if (w.Tick < o.Due) continue;
            if (phantom)
            {
                // 계기 오류는 저절로 걷힌다 (감지기가 제자리로 흘러 돌아왔다) — 그동안 부품을 갈았다면 헛일이었다
                m.Omen = null;
                w.Watch.Close(o, broke: false);
                continue;
            }
            // 때가 됐다: 손보지 못했으면 고장 난다
            m.Omen = null;
            w.Watch.Close(o, broke: true);
            st.Missed++;
            MarkLog.Add(m.Marks, w.Tick, o.Known ? $"{Ko.EulReul(Name(o.Kind))} 알았지만 손보기 전에 고장" : $"{Name(o.Kind)} — 아무도 몰랐다");
            w.Machines.Break(m, o.Fault);
            if (o.Kind == OmenKind.Heat && w.Rng.Chance(0.3f))
            {
                var spot = m.Body.UseSpots.FirstOrDefault(s => w.Ship.IsOpenFloor(s));
                if (spot != default && w.Fire.Ignite(spot, 0.3f))
                {
                    st.MissedFires++;
                    w.Log.Add(w.Tick, LogKind.Warning, $"달아오르던 {Ko.IGa(m.Name)} 끝내 불꽃을 튀겼다");
                }
            }
        }
    }

    public static void Detect(World w, Machine m, Omen o, string how, CrewMember? by, string? byName = null)
    {
        var st = w.Precursors;
        w.Watch.Observe(m, o, how, by, byName); // v12.0 당직 일지 (다시 찾았으면 중복 점검)
        if (o.Known) { w.Board.RequestScan(); return; }
        o.Known = true;
        o.KnownAt = w.Tick;
        o.KnownBy = byName ?? by?.Name ?? how;
        ComputerV15.OnDetect(w, m, o); // v15.9 전조 분석: 부하를 낮춰 고장을 늦춘다
        st.Detected++;
        switch (how)
        {
            case "감지기": st.BySensor++; break;
            case "당직" or "옆방에서 들음": st.ByCrew++; break;
            case "로봇": st.ByRobot++; break;
            default: st.ByRounds++; break;
        }
        float hours = (o.Due - w.Tick) / (float)SimTime.TicksPerHour;
        string who = how switch
        {
            "감지기" => "감지기가 잡았다", "당직" => $"{Ko.IGa(by!.Name)} 알아챘다", "옆방에서 들음" => $"{Ko.IGa(by!.Name)} 옆방에서 소리를 듣고 알아챘다",
            "로봇" => $"{Ko.IGa(byName ?? "로봇")} 순찰하다 찾았다", _ => $"{Ko.IGa(byName ?? by?.Name ?? "?")} 순찰하다 찾았다",
        };
        MarkLog.Add(m.Marks, w.Tick, $"전조: {Name(o.Kind)} ({who})");
        w.RaiseAlert($"전조 — {m.Name} {Name(o.Kind)} ({who} · 고장까지 {hours:0}시간쯤)", m.Body.Room, AlertLevel.Notice, shipWide: false);
        w.Board.RequestScan();
    }

    /// <summary>방을 들여다봤다 (로봇·사람 순찰): 그 방 설비의 전조를 찾는다.</summary>
    public static void Inspect(World w, Room room, string who, bool robot, CrewMember? by = null)
    {
        w.RoomsInspected[room.Id] = w.Tick;
        if (w.PreventionBlind) return;
        foreach (var f in room.Furniture)
        {
            if (f.Machine?.Omen is not Omen o || o.Cause == OmenCause.Phantom) continue;
            // v12.0 이미 알고 있는 것(로봇은 컴퓨터 일지에 올라간 것)은 다시 보지 않는다 — 모르는 사람이 찾으면 중복 점검
            if (o.Note is ShiftNote seen && (robot ? seen.Logged : by != null && w.Watch.Knows(seen, by))) continue;
            // 로봇은 빠짐없이(열화상·진동 센서), 사람은 솜씨만큼 (기척이 작으면 놓치기도)
            float p = robot ? 0.95f : (0.45f + 0.45f * (by?.SkillLevel(f.Machine.Spec.Skill) ?? 0.5f)) * (0.55f + 0.45f * o.Level(w.Tick));
            if (w.Rng.Chance(p)) Detect(w, f.Machine, o, robot ? "로봇" : "순찰", by, who);
        }
    }

    /// <summary>전조를 손봤다: 고장이 오지 않는다.</summary>
    public static void Fixed(World w, Machine m, CrewMember? by, Robot? bot)
    {
        if (m.Omen is not Omen o) return;
        m.Omen = null;
        if (o.Note is ShiftNote n) w.Watch.Resolve(n, by, "손봄 — 고장을 막았다"); // v12.0
        w.Precursors.Prevented++;
        m.Wear = MathF.Max(0f, m.Wear - 0.15f);
        MarkLog.Add(m.Marks, w.Tick, $"{by?.Name ?? bot?.Name ?? "?"}: {Name(o.Kind)} 손봄 — 고장을 막았다");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(m.Name)} {Name(o.Kind)} 전조를 손봤다 — {Ko.EulReul(Faults.Spec(o.Fault).Name)} 막았다", by?.Id ?? -1);
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v11.0 예방: 알아챈 전조를 손보고, 순찰 로봇이 없으면 사람이 하루에 한 번 방을 돈다.</summary>
    private void ScanPrevention(Poster post)
    {
        var w = _world;
        foreach (var m in w.Ship.Machines)
        {
            if (m.Omen is not Omen o || !o.Known || m.Body.Room.Abandoned || m.Body.Room.OffLimits) continue;
            // v12.0 누군가 손댈 수 있는 기록만 (컴퓨터 일지로 읽히거나, 아는 사람이 깨어 있다)
            if (o.Note is not ShiftNote n || !w.Watch.Actionable(n)) continue;
            float left = (o.Due - w.Tick) / (float)SimTime.TicksPerHour;
            string state = n.Confirmed is OmenCause done
                ? $"확인: {Causes.Name(done)}" + (Causes.NeedsParts(done) ? " — " + string.Join(" + ", Causes.Spec(done).Fix.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}")) : " — 부품 없이")
                : n.Suspect is OmenCause s ? $"관측: {n.Observation} · 판단: {Causes.Name(s)} 의심 ({n.Confidence * 100:0}%)" : $"관측: {n.Observation} · 원인 모름";
            post(WorkKind.PreventiveCheck, WorkTarget.Of(m.Body), (0.45f + 0.4f * o.Level(w.Tick) + (m.Spec.Critical ? 0.15f : 0f)) * (w.Policies["maint"] == 1 ? 0.5f : 1f), m.Spec.Skill,
                $"{state} · {left:0}시간쯤 남음"); // v13.4 방침(정비: 고장 나면)이면 전조 점검은 뒤로
        }
        ScanSafety(post);
        if (w.PreventionBlind) return;
        // 순찰: 방재 로봇이 돌고 있으면 사람은 돌지 않는다 (로봇이 멈추면 사람이 다시 돈다)
        bool robotRounds = w.Robots.Robots.Any(r => RobotsV15.Patrols(r.Kind) && r.Operational); // v15.7 순찰하는 로봇 (순찰 로봇도 · 소방 로봇은 아니다)
        if (robotRounds) return;
        // 가장 오래 안 본 방 두 곳씩만 (한꺼번에 올리면 작업 목록이 순찰로 덮인다) — 하루에 한 번 모든 방을 돈다
        var due = w.Ship.LiveRooms
            .Where(room => !room.Abandoned && !room.OffLimits && room.Type != RoomType.Corridor && room.Furniture.Any(f => f.Machine != null))
            .Select(room => (room, since: (w.Tick - w.RoomsInspected.GetValueOrDefault(room.Id, w.StartTickOf)) / (float)SimTime.TicksPerHour))
            .Where(x => x.since >= 24f)
            .OrderByDescending(x => x.room.Furniture.Any(f => f.Machine is Machine mm && mm.Spec.Critical) ? x.since + 6f : x.since).ThenBy(x => x.room.Id)
            .Take(2);
        foreach (var (room, since) in due)
        {
            bool vital = room.Furniture.Any(f => f.Machine is Machine mm && mm.Spec.Critical);
            post(WorkKind.PreventiveCheck, WorkTarget.OfRoom(room), MathF.Min(0.4f, 0.12f + (since - 24f) / 150f + (vital ? 0.06f : 0f)), Skill.Mechanics,
                $"마지막으로 본 지 {since:0}시간" + (vital ? " · 핵심 설비" : ""));
        }
    }
}

public sealed partial class WorkBoard
{
    /// <summary>
    /// v11.0 안전 담당(정비사의 겸직): 우주복 보관함 점검(닷새마다), 비상 훈련(평화로울 때 이레마다 한 사람씩).
    /// </summary>
    private void ScanSafety(Poster post)
    {
        var w = _world;
        foreach (var locker in w.Ship.FurnitureOf(FurnitureType.SuitLocker))
        {
            if (locker.Room.Abandoned || locker.Room.OffLimits || locker.Room.Leaking) continue;
            float days = (w.Tick - locker.Checked) / (float)SimTime.TicksPerDay;
            if (days < 5f) continue;
            post(WorkKind.SuitCheck, WorkTarget.Of(locker), MathF.Min(0.45f, 0.18f + (days - 5f) * 0.04f), Skill.Mechanics,
                $"마지막 점검 {days:0}일 전 · 밸브·봉합·산소통");
        }
        if (!Evolution.Peaceful(w)) return;
        var airlock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault(r => !r.Abandoned && !r.Leaking && !r.OffLimits);
        if (airlock == null) return;
        foreach (var c in w.Crew)
        {
            // 훈련 효과(열흘)가 사흘 남았을 때부터 다시 (처음 훈련은 항해 사흘째부터)
            // v13.4 방침(비상 훈련): 없음 · 주 1회(지금) · 이틀마다
            int drills = w.Policies["drills"];
            if (drills == 0) break;
            if (!c.CanAct || w.Tick - w.StartTickOf < SimTime.Hours(72) || w.Tick < c.DrilledUntil - SimTime.Hours(drills == 2 ? 192 : 72)) continue;
            post(WorkKind.Drill, WorkTarget.OfRoom(airlock), 0.22f, Skill.Mechanics, $"{c.Name} · 우주복 착용·격벽·소화 훈련", circuit: c.Id);
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>우주복 점검: 보관함의 우주복 밸브·봉합·산소통을 본다.</summary>
    private static Job? SuitCheck(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var locker = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.35f, Skill.Mechanics, locker.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            locker.Checked = world.Tick;
            world.Board.Close(o);
            if (locker.Machine != null) MarkLog.Add(locker.Machine.Marks, world.Tick, $"{cm.Name}: 우주복 점검");
            return true;
        }));
        return Wrap(a, o, c, w, "우주복 점검", toils, o.Title);
    }

    /// <summary>비상 훈련: 에어락에서 우주복 착용·격벽 손 조작·소화기 쓰기를 되풀이한다 (열흘 동안 몸이 기억한다).</summary>
    private static Job? Drill(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        if (o.Circuit != c.Id) { blocked = null; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Mechanics, o.Target.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            cm.DrilledUntil = world.Tick + SimTime.Hours(24 * 10);
            cm.Drills++;
            cm.Traits.Calm = MathF.Min(0.95f, cm.Traits.Calm + 0.01f);
            world.CrisisCrew.Drilled(cm); // v16.21 비상 절차가 손에 붙는다 (제 자리 절차를 크게)
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, "비상 훈련을 마쳤다 (우주복 2분 30초 · 격벽 손 조작)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "훈련", toils, "에어락에서 비상 훈련");
    }
}
