using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

public enum OutcomeKind { Full, Partial, LongTerm, Failure }

/// <summary>사고 뒤 우주선이 어떤 상태로 남았는지.</summary>
public sealed record Outcome(OutcomeKind Kind, IReadOnlyList<string> Notes)
{
    public static string Name(OutcomeKind k) => k switch
    {
        OutcomeKind.Full => "완전 복구",
        OutcomeKind.Partial => "부분 복구",
        OutcomeKind.LongTerm => "장기 장애",
        _ => "실패",
    };

    public override string ToString() => Notes.Count > 0 ? $"{Name(Kind)} — {string.Join(", ", Notes)}" : Name(Kind);
}

/// <summary>
/// 사고가 어떻게 끝났는지 네 가지로 나눈다.
/// - 실패: 사람이 죽었거나 둘 이상 쓰러져 있다, 우주선이 숨을 못 쉰다, 전력이 완전히 죽었다
/// - 장기 장애: 생명유지에 꼭 필요한 기능(원자로·산소·물)이 반쯤 이하, 포기한 구획, 쓰러진 사람, 중상자
/// - 부분 복구: 살아는 났지만 남은 게 있다 (고장·임시 수리, 낮은 기압, 바닥난 공기 탱크, 다친 사람)
/// - 완전 복구: 아무것도 안 남았다 (흔적 빼고)
/// </summary>
/// <summary>사고 직전 상태. 사고 뒤와 비교해서 "영구히 잃은 것"을 센다.</summary>
public sealed class Snapshot
{
    public Dictionary<ItemKind, int> Items { get; } = new();
    public Dictionary<Machine, float> Condition { get; } = new();
    public float Reserve { get; private set; }
    public int CropsLost { get; init; }
    public int ItemsBurned { get; init; }

    /// <summary>
    /// 시나리오가 처음 상황으로 물자를 줄여 놓은 경우(우주복 두 벌만, 공기 탱크 비움 등)
    /// 그건 사고로 잃은 게 아니므로 기준을 지금 값으로 낮춘다.
    /// </summary>
    public void RebaseStock(World w)
    {
        foreach (var k in ItemKinds.All) Items[k] = Math.Min(Items[k], w.Ship.CountStored(k));
        Reserve = MathF.Min(Reserve, w.Air.Reserve);
    }

    public static Snapshot Take(World w)
    {
        var s = new Snapshot { Reserve = w.Air.Reserve, CropsLost = w.Machines.CropsLost, ItemsBurned = w.Fire.ItemsBurned };
        foreach (var k in ItemKinds.All) s.Items[k] = w.Ship.CountStored(k);
        foreach (var m in w.Ship.Machines) s.Condition[m] = m.Condition;
        return s;
    }
}

public static class Assessment
{
    /// <summary>사고로 잃은 뒤 쉽게 되찾을 수 없는 물자 (제작할 수 없는 것).</summary>
    private static readonly ItemKind[] Irreplaceable = { ItemKind.MedKit, ItemKind.Extinguisher, ItemKind.Suit, ItemKind.ReactorControl };

    public static Outcome Assess(World w, Snapshot? before = null)
    {
        var ship = w.Ship;
        var p = w.Power;
        var fail = new List<string>();
        var longTerm = new List<string>();
        var partial = new List<string>();

        // ── 사람 ──
        int dead = w.Crew.Count(c => c.Dead);
        int down = w.Crew.Count(c => c.Down && !c.Dead);
        if (dead > 0) fail.Add($"사망 {dead}명");
        if (down >= 2) fail.Add($"쓰러진 사람 {down}명");
        else if (down == 1) longTerm.Add("쓰러진 사람 1명");
        int severe = w.Crew.Count(c => !c.Dead && c.Vitals.Injury >= 0.5f);
        if (severe > 0) longTerm.Add($"중상 {severe}명");
        int hurt = w.Crew.Count(c => !c.Dead && (c.Vitals.Health < 0.6f || c.Vitals.Injury >= 0.15f));
        if (hurt > 0 && severe == 0) partial.Add($"부상 {hurt}명");

        // ── 공기 ──
        var living = ship.Rooms.Where(r => !r.Abandoned).ToList();
        float vol = living.Sum(r => r.Volume);
        float avgP = vol > 0 ? living.Sum(r => r.Air.Pressure * r.Volume) / vol : 0f;
        float avgO2 = vol > 0 ? living.Sum(r => r.Air.O2 * r.Volume) / vol : 0f;
        if (avgP < 60f || avgO2 < 14f) fail.Add($"숨쉬기 힘든 우주선 (평균 {avgP:0}kPa, O2 {avgO2:0.0})");
        float o2Max = ship.FurnitureOf(FurnitureType.OxygenGenerator).Sum(f => f.Machine!.Rating) * Atmosphere.GeneratorCapacity;
        // v9.3: 저출력 운영으로 내려 둔 산소 발생기는 고장이 아니다 (전기가 돌아오면 다시 올린다)
        float o2Cap = w.Air.O2Capacity + ship.FurnitureOf(FurnitureType.OxygenGenerator)
            .Where(f => f.Machine!.Parked).Sum(f => f.Machine!.FaultFactor * f.Machine.Rating * Atmosphere.GeneratorCapacity);
        if (o2Max > 0 && o2Cap < o2Max * 0.5f) longTerm.Add($"산소 생산 {o2Cap / o2Max * 100:0}%");
        if (w.Power.Brownout) partial.Add($"저출력 운영 중 (설비 {w.Power.ParkedCount}대 내림)");
        // v9.4 문 구동기·조명
        int brokenDoors = ship.Doors.Count(d => d.MotorBroken && !d.Removed && !d.Welded);
        if (brokenDoors > 0) partial.Add($"문 구동기 고장 {brokenDoors}");
        int dimDoors = ship.Doors.Count(d => d.MotorMk1 && !d.MotorBroken);
        if (dimDoors > 0) partial.Add($"임시 구동기 문 {dimDoors}");
        var darkRooms = ship.LiveRooms.Where(r => r.LightsOut && !r.Abandoned).ToList();
        if (darkRooms.Count > 0) partial.Add($"조명 나간 방 {darkRooms.Count}개 ({string.Join("·", darkRooms.Select(r => r.Name))})");
        var abandonedRooms = ship.Rooms.Where(r => r.Abandoned && !r.Detached && !r.Docked).ToList();
        if (abandonedRooms.Count > 0) longTerm.Add($"포기한 구획 {abandonedRooms.Count}개 ({string.Join("·", abandonedRooms.Select(r => r.Name))})");

        // ── 구조 (v8): 떨어져 나간 방, 임시 도킹, 하중이 몰린 방, 골조를 잃은 외벽 ──
        foreach (var f in w.Structure.Fragments)
            longTerm.Add(f.State == FragmentState.Lost ? $"{f.Room.Name} 잃음 ({(f.Jettisoned ? "사출" : "뜯겨 나감")})"
                : $"{f.Room.Name} {(f.Jettisoned ? "사출" : "떨어져 나감")} ({f.Distance:0}칸 · {(f.State == FragmentState.Towed ? "견인 중" : f.State == FragmentState.Moored ? "계류" : "표류")})");
        var docked = ship.Rooms.Where(r => r.Docked && !r.Detached).ToList();
        if (docked.Count > 0) longTerm.Add($"다시 붙였지만 {(docked.Any(r => r.Wreck) ? "잔해로 둔" : "아직 안 이은")} 구획 ({string.Join("·", docked.Select(r => r.Name))})");
        var jett = ship.Rooms.Where(r => r.Jettison != null).ToList();
        if (jett.Count > 0) longTerm.Add($"사출 준비 중 ({string.Join("·", jett.Select(r => $"{r.Name} {JettisonPlan.StageName(r.Jettison!.Stage)}"))})");
        var stressed = ship.Rooms.Where(r => !r.Detached && w.Structure.Stress.GetValueOrDefault(r.Id) > 1f).ToList();
        if (stressed.Count > 0) longTerm.Add($"하중이 몰린 방 ({string.Join("·", stressed.Select(r => $"{r.Name} {w.Structure.Stress[r.Id] * 100:0}%"))})");
        int framesLost = ship.Walls.Count(kv => kv.Value.IsHull && kv.Value.FrameLost);
        if (framesLost > 0) longTerm.Add($"골조를 잃은 외벽 {framesLost}칸");
        int broken = ship.Rooms.Where(r => !r.Detached).Sum(r => r.Joints.Count(j => j.Broken && !j.Released));
        int weak = ship.Rooms.Where(r => !r.Detached).Sum(r => r.Joints.Count(j => !j.Broken && j.Strength < 0.6f));
        if (broken > 0) partial.Add($"끊어진 연결부 {broken}");
        else if (weak > 0) partial.Add($"약해진 연결부 {weak}");
        int trusses = ship.Rooms.Where(r => !r.Detached).Sum(r => r.Joints.Count(j => j.Truss && !j.Broken));
        if (trusses > 0) partial.Add($"임시 트러스 {trusses}");
        var badDrones = w.Drones.Drones.Where(d => !d.OnTrip && (d.Wrecked || d.Faulty || d.State is DroneState.Adrift or DroneState.Lost)).ToList(); // 원정에 따라 나간 드론은 잃은 게 아니다
        if (badDrones.Count > 0) partial.Add($"드론 {string.Join("·", badDrones.Select(d => $"{d.Name} {(d.State == DroneState.Lost ? "잃음" : d.State == DroneState.Adrift ? "표류" : d.Wrecked ? "부서짐" : "고장")}"))}");
        var outside = w.Crew.Where(c => !c.Dead && c.Outside).ToList();
        if (outside.Count > 0) longTerm.Add($"선체 밖에 남은 사람 ({string.Join("·", outside.Select(c => c.Name))})");
        int leaking = living.Count(r => r.Leaking);
        if (leaking > 0) longTerm.Add($"아직 새는 방 {leaking}개");
        int locked = living.Count(r => r.Lockdown && !r.Leaking);
        if (locked > 0) partial.Add($"격벽 폐쇄 {locked}곳");
        float minP = living.Count > 0 ? living.Min(r => r.Air.Pressure) : 0f;
        if (minP < 90f && avgP >= 60f) partial.Add($"최저 기압 {minP:0}kPa");
        if (w.Air.Reserve < w.Air.ReserveCapacity * 0.05f) longTerm.Add("공기 탱크 바닥 (다음 파공은 재가압 못 함)");
        else if (w.Air.Reserve < w.Air.ReserveCapacity * 0.3f) partial.Add($"공기 탱크 {w.Air.Reserve / w.Air.ReserveCapacity * 100:0}%");

        // ── 전력 ──
        bool powerDead = !p.ReactorOnline && !p.AuxRunning && p.BatteryPercent < 0.05f;
        if (powerDead) fail.Add("전력 완전 상실");
        else if (!p.ReactorOnline) longTerm.Add(p.AuxRunning ? "원자로 정지 (보조 발전기로 버팀)" : "원자로 정지 (배터리로 버팀)");
        else if (p.LowPowerMode) longTerm.Add($"원자로 저출력 수동 운전 ({p.ReactorLimit:0}kW)");
        else if (p.ReactorLimit < p.ReactorRated * 0.5f) longTerm.Add($"원자로 출력 {p.ReactorLimit:0}kW");

        // ── 설비 ──
        var water = ship.FurnitureOf(FurnitureType.WaterRecycler).FirstOrDefault()?.Machine;
        if (water != null && water.Stopped) longTerm.Add("정수기 정지");
        int critFaults = ship.Machines.Count(m => m.Spec.Critical && m.Faults.Count > 0);
        int faults = ship.Machines.Sum(m => m.Faults.Count(f => f.Kind != FaultKind.Stripped));
        int makeshift = ship.Machines.Sum(m => m.Faults.Count(f => f.Stage > 0));
        if (critFaults > 0) longTerm.Add($"핵심 설비 고장 {critFaults}대");
        else if (faults > 0) partial.Add($"고장 {faults}건" + (makeshift > 0 ? $" (임시 운전 {makeshift})" : ""));
        if (w.Fire.Count > 0) longTerm.Add($"불 {w.Fire.Count}칸");

        // ── 적응의 흔적: 원래 설계와 달라진 곳 ──
        var stripped = ship.Machines.Where(m => m.Has(FaultKind.Stripped)).ToList();
        if (stripped.Count > 0) partial.Add($"뜯긴 설비 {stripped.Count}대 ({string.Join("·", stripped.Select(m => m.Name).Take(4))}{(stripped.Count > 4 ? "…" : "")})");
        int jumpers = p.Jumpers.Count(j => j.Active);
        if (jumpers > 0) partial.Add($"임시 배선 {jumpers}가닥 ({string.Join("·", p.Jumpers.Where(j => j.Active).Select(j => $"{PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)}"))})");
        var off = Enumerable.Range(0, PowerGrid.CircuitCount).Where(i => p.ManualOff[i]).ToList();
        if (off.Count > 0)
        {
            string note = $"절전으로 끈 회로 {string.Join("·", off.Select(PowerGrid.CircuitName))}";
            if (off.Contains(1)) longTerm.Add(note); else partial.Add(note);
        }
        int mk1 = ship.Machines.Count(m => m.Grade == MachineGrade.Mk1);
        if (mk1 > 0) partial.Add($"Mk.1 임시품 {mk1}대 ({string.Join("·", ship.Machines.Where(m => m.Grade == MachineGrade.Mk1).Select(m => m.Name).Take(3))})");
        var shops = ship.Rooms.Where(r => r.Purpose == "임시 정비실").ToList();
        if (shops.Count > 0) partial.Add($"임시 정비실 ({string.Join("·", shops.Select(r => r.Name))})");
        var dorms = ship.Rooms.Where(r => r.Purpose == "임시 침실").ToList();
        if (dorms.Count > 0) partial.Add($"임시 침실 ({string.Join("·", dorms.Select(r => r.Name))})");

        // ── 선체 ──
        int temp = ship.Walls.Count(kv => kv.Value.IsHull && kv.Value.Patched);
        if (temp > 0) partial.Add($"임시 봉합 {temp}곳");

        // ── 자동화 (v9.2) ──
        var auto = w.Automation;
        if (auto.Present && !auto.MainOnline)
            longTerm.Add($"자동화 꺼짐 (주 컴퓨터 {auto.Computer?.Faults.FirstOrDefault()?.Name ?? "멈춤"})" + (auto.BackupActive ? " · 예비 제어기로 격벽·댐퍼만" : ""));
        var stuck = ship.Rooms.Where(r => r.DamperStuck && !r.Detached).ToList();
        if (stuck.Count > 0) partial.Add($"걸린 댐퍼 ({string.Join("·", stuck.Select(r => r.Name))})");

        // ── 배관 (v9) ──
        var net = w.Piping;
        if (net.Built)
        {
            var closed = net.Segments.Where(x => x.Closed && x.Bypass <= 0f && !x.LimpApproved).ToList();
            var coolClosed = closed.Where(x => x.IsCoolant).ToList();
            if (coolClosed.Count > 0) longTerm.Add($"잠근 냉각 배관 ({string.Join("·", coolClosed.Select(x => x.Name))}) — 냉각 {net.CoolingKw:0}kW");
            var waterClosed = closed.Where(x => !x.IsCoolant).ToList();
            if (waterClosed.Count > 0) (waterClosed.Any(x => x.Role == PipeRole.WaterMain) ? longTerm : partial).Add($"잠근 급수관 ({string.Join("·", waterClosed.Select(x => x.Name))})");
            var leakingPipes = net.Segments.Where(x => x.Leaking).ToList();
            if (leakingPipes.Count > 0) longTerm.Add($"새는 배관 ({string.Join("·", leakingPipes.Select(x => $"{x.Name} {x.LeakRate:0}L/시간"))})");
            int bypass = net.Segments.Count(x => x.Bypass > 0f && x.Closed);
            if (bypass > 0) partial.Add($"우회 배관 {bypass}");
            int patchedPipes = net.Segments.Count(x => x.Patched);
            if (patchedPipes > 0) partial.Add($"임시 밀봉한 배관 {patchedPipes}");
            var radiators = net.Branches.Where(x => x.RadiatorCondition < 0.7f).ToList();
            if (radiators.Count > 0) partial.Add($"부서진 방열판 {radiators.Count}");
            if (net.CoolantFraction < 0.5f) longTerm.Add($"냉각수 {net.CoolantFraction * 100:0}%");
            else if (net.CoolantFraction < 0.75f) partial.Add($"냉각수 {net.CoolantFraction * 100:0}%");
        }

        // ── 영구히 잃은 것 (사고 직전과 비교) ──
        if (before != null)
        {
            float tankLost = (before.Reserve - w.Air.Reserve) / w.Air.ReserveCapacity;
            // 한 번의 사고로 되돌릴 수 없는 것을 크게 잃었을 때만 센다 (보통 수리에 드는 부품 몇 개는 아니다)
            if (tankLost >= 0.35f && w.Air.Reserve >= w.Air.ReserveCapacity * 0.3f) partial.Add($"공기 탱크 −{tankLost * 100:0}%");
            var lostItems = Irreplaceable
                .Select(k => (k, lost: before.Items[k] - ship.CountStored(k), left: ship.CountStored(k)))
                .Where(x => x.lost > 0 && ((x.lost >= 3 && x.lost >= before.Items[x.k] / 4) || x.left <= 2))
                .Select(x => $"{ItemKinds.Name(x.k)} −{x.lost}(남은 {x.left})").ToList();
            if (lostItems.Count > 0) partial.Add(string.Join(" ", lostItems));
            int damaged = ship.Machines.Count(m => before.Condition.TryGetValue(m, out var c0) && (c0 - m.Condition >= 0.25f || (m.Condition < 0.5f && c0 >= 0.5f)));
            if (damaged > 0) partial.Add($"설비 영구 손상 {damaged}대");
            int crops = w.Machines.CropsLost - before.CropsLost;
            if (crops > 0) partial.Add($"작물 손실 {crops}");
            int burned = w.Fire.ItemsBurned - before.ItemsBurned;
            if (burned >= 3) partial.Add($"불탄 물자 {burned}개");
            // 큰 사고는 몇 주 동안 모은 재료를 한 번에 쓴다
            float v0 = ItemKinds.All.Sum(k => Recipes.Value(k) * before.Items[k]);
            float v1 = ItemKinds.All.Sum(k => Recipes.Value(k) * ship.CountStored(k));
            if (v0 > 10f && v1 < v0 * 0.75f) partial.Add($"수리재 −{(1f - v1 / v0) * 100:0}%");
        }

        if (fail.Count > 0) return new Outcome(OutcomeKind.Failure, fail.Concat(longTerm).ToList());
        if (longTerm.Count > 0) return new Outcome(OutcomeKind.LongTerm, longTerm.Concat(partial).ToList());
        if (partial.Count > 0) return new Outcome(OutcomeKind.Partial, partial);
        return new Outcome(OutcomeKind.Full, new List<string>());
    }
}
