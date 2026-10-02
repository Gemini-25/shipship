using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.18 사고 · 재난 다섯 규모 — 표 하나.
//   ① 개인(베임 · 미끄러짐 · 데임) ② 방(작은 불 · 누수 · 고장) ③ 계통 · 구역(정전 · 배관 파열 · 구획 감압)
//   ④ 배 전체(선체 대파 · 원자로 정지 · 반란) ⑤ 우주급(초신성 · 감마선 폭발 · 블랙홀 · 큰 소행성 · 반물질).
// 흩어져 있던 사고의 종류(사고 70 · 이야기꾼 열쇠 · 우주급 30 · 인과 사슬 고리 · 고장 · 과열 폭발 · 폭발 · 질병 · 부상 · 선외)를
// 모두 한 줄씩 기본 규모로 나눈다. 실제 규모는 피해로 올라간다 (ScaleSystem.Measure — 같은 불도 방 하나면 방, 번지면 계통).

public enum IncidentScale : byte { Personal, Room, System, Ship, Cosmic }

/// <summary>표 한 줄: 열쇠 · 이름 · 어디서 온 사고 · 기본 규모 · 어떻게 커지나.</summary>
public sealed record ScaleRow(string Key, string Name, string Source, IncidentScale Base, string Grows);

public static class ScaleTable
{
    public static readonly IncidentScale[] Scales = { IncidentScale.Personal, IncidentScale.Room, IncidentScale.System, IncidentScale.Ship, IncidentScale.Cosmic };

    public static string Name(IncidentScale s) => s switch
    {
        IncidentScale.Personal => "개인", IncidentScale.Room => "방", IncidentScale.System => "계통 · 구역", IncidentScale.Ship => "배 전체", _ => "우주급",
    };
    public static string Mark(IncidentScale s) => s switch { IncidentScale.Personal => "①", IncidentScale.Room => "②", IncidentScale.System => "③", IncidentScale.Ship => "④", _ => "⑤" };
    public static string Label(IncidentScale s) => $"{Mark(s)} {Name(s)}";

    /// <summary>규모마다 커지는 대응.</summary>
    public static string Response(IncidentScale s) => s switch
    {
        IncidentScale.Personal => "혼자 · 곁의 사람",
        IncidentScale.Room => "당직이 맡는다",
        IncidentScale.System => "여러 명 · 작업 우선",
        IncidentScale.Ship => "전원 소집 · 일상 중단",
        _ => "대피 · 항로 변경",
    };

    public static string Examples(IncidentScale s) => s switch
    {
        IncidentScale.Personal => "베임 · 미끄러짐 · 데임",
        IncidentScale.Room => "작은 불 · 누수 · 고장",
        IncidentScale.System => "정전 · 배관 파열 · 구획 감압",
        IncidentScale.Ship => "선체 대파 · 원자로 정지 · 반란",
        _ => "초신성 · 감마선 폭발 · 블랙홀 · 큰 소행성 · 반물질",
    };

    /// <summary>화면 색 (읽기 전용 — 시뮬레이션은 쓰지 않는다).</summary>
    public static string Hex(IncidentScale s) => s switch
    {
        IncidentScale.Personal => "#7fd1b0", IncidentScale.Room => "#ffd43b", IncidentScale.System => "#ff922b", IncidentScale.Ship => "#ff4d5e", _ => "#b57cff",
    };

    /// <summary>주 컴퓨터가 부르는 사람 수: 개인 1 · 방(당직) 2 · 계통 3~6 · 배 · 우주급은 움직일 수 있는 사람 모두.</summary>
    public static int Muster(IncidentScale s, int able) => Math.Max(0, Math.Min(able, s switch
    {
        IncidentScale.Personal => 1,
        IncidentScale.Room => 2,
        IncidentScale.System => Math.Clamp(able / 4, 3, 6),
        _ => able,
    }));

    // ─────────────────────────────── 사고 70: 손으로 나눈 네 줄 (빠짐 없이 — 시험이 센다) ───────────────────────────────

    internal static readonly HazardKind[] HPersonal =
    {
        HazardKind.WorkAccident, HazardKind.HoistDrop, HazardKind.PanicAttack, HazardKind.MedError, HazardKind.SkinFungus, HazardKind.FoodPoisoning,
    };
    internal static readonly HazardKind[] HRoom =
    {
        HazardKind.GasLeak, HazardKind.CropBlight, HazardKind.RobotMalfunction, HazardKind.HullCrack, HazardKind.DoorJam, HazardKind.LightsOut,
        HazardKind.OxygenLeak, HazardKind.Overheat, HazardKind.HydrogenBuildup, HazardKind.FreezerFailure,
        HazardKind.ArcFault, HazardKind.GroundFault, HazardKind.CellAging, HazardKind.PipeFreeze, HazardKind.ValveSeize, HazardKind.CondensateFlood, HazardKind.SewageBackup,
        HazardKind.ScrubberSaturation, HazardKind.InsulationSmoke, HazardKind.SealLeak, HazardKind.Ozone, HazardKind.HumiditySpike,
        HazardKind.BearingSeize, HazardKind.FanImbalance, HazardKind.ShaftMisalign, HazardKind.LooseMount, HazardKind.BadBatch,
        HazardKind.WeldFatigue, HazardKind.WindowCrack, HazardKind.HatchSeal, HazardKind.StaticDischarge,
        HazardKind.GreaseFire, HazardKind.DryerFire, HazardKind.Smolder, HazardKind.MoldOutbreak, HazardKind.NutrientCrash,
    };
    internal static readonly HazardKind[] HSystem =
    {
        HazardKind.PowerSurge, HazardKind.ComputerFault, HazardKind.WaterContamination, HazardKind.RescueSignal, HazardKind.DuctFire, HazardKind.ComputerMisjudge,
        HazardKind.Epidemic, HazardKind.MicroShower, HazardKind.GasTankRupture,
        HazardKind.BreakerCascade, HazardKind.InsulationCrack, HazardKind.TrunkSag, HazardKind.Backflow, HazardKind.TankSludge, HazardKind.Co2Spike,
        HazardKind.ThermalStress, HazardKind.FrameCreak, HazardKind.IonStorm, HazardKind.DebrisAlert, HazardKind.CometTail, HazardKind.CableTrayFire, HazardKind.SeedRot,
    };
    internal static readonly HazardKind[] HShip =
    {
        HazardKind.MeteorShower, HazardKind.SolarStorm, HazardKind.ReactorTransient, HazardKind.DebrisCloud, HazardKind.CoolantLoss, HazardKind.RadiationBurst,
    };

    private static readonly Dictionary<HazardKind, IncidentScale> HMap = BuildHazards();
    private static Dictionary<HazardKind, IncidentScale> BuildHazards()
    {
        var d = new Dictionary<HazardKind, IncidentScale>();
        foreach (var k in HPersonal) d.TryAdd(k, IncidentScale.Personal);
        foreach (var k in HRoom) d.TryAdd(k, IncidentScale.Room);
        foreach (var k in HSystem) d.TryAdd(k, IncidentScale.System);
        foreach (var k in HShip) d.TryAdd(k, IncidentScale.Ship);
        return d;
    }

    public static IncidentScale Of(HazardKind k) => HMap.TryGetValue(k, out var s) ? s : IncidentScale.Room;

    /// <summary>시험용: 사고 70 가운데 어느 줄에도 없거나 두 줄에 든 것 (없어야 한다).</summary>
    public static List<string> Unclassified()
    {
        var bad = new List<string>();
        var all = HPersonal.Concat(HRoom).Concat(HSystem).Concat(HShip).ToList();
        foreach (var k in Enum.GetValues<HazardKind>())
        {
            int n = all.Count(x => x == k);
            if (n != 1) bad.Add($"{k}×{n}");
        }
        return bad;
    }

    // ─────────────────────────────── 인과 사슬의 고리 (번진 피해) ───────────────────────────────

    public static IncidentScale Of(CauseKind k) => k switch
    {
        CauseKind.Casualty or CauseKind.Shock or CauseKind.Illness or CauseKind.Mistake or CauseKind.Recovery => IncidentScale.Personal,
        CauseKind.Impact or CauseKind.Explosion or CauseKind.Fire or CauseKind.Breach or CauseKind.Suffocation or CauseKind.Gas
            or CauseKind.Fault or CauseKind.Flood or CauseKind.Hazard => IncidentScale.Room,
        CauseKind.Cut or CauseKind.Outage or CauseKind.NoWater or CauseKind.NoAir or CauseKind.NoData or CauseKind.Stop or CauseKind.Death => IncidentScale.System,
        CauseKind.Scram or CauseKind.Detach => IncidentScale.Ship,
        _ => IncidentScale.Room,
    };

    public static string CauseName(CauseKind k) => k switch
    {
        CauseKind.Impact => "충돌", CauseKind.Explosion => "폭발", CauseKind.Fire => "화재", CauseKind.Breach => "감압", CauseKind.Suffocation => "산소 부족",
        CauseKind.Gas => "가스", CauseKind.Cut => "망 끊김", CauseKind.Outage => "정전", CauseKind.NoWater => "단수", CauseKind.NoAir => "환기 끊김",
        CauseKind.Fault => "설비 고장", CauseKind.Stop => "핵심 설비 멈춤", CauseKind.Scram => "원자로 긴급 정지", CauseKind.Casualty => "쓰러짐",
        CauseKind.Death => "사망", CauseKind.Detach => "방 분리 (선체 대파)", CauseKind.Hazard => "그 밖의 사고", CauseKind.Recovery => "복구",
        CauseKind.Flood => "침수", CauseKind.Shock => "감전", CauseKind.NoData => "데이터선 끊김", CauseKind.Illness => "감염", _ => "사람의 실수",
    };

    // ─────────────────────────────── 고장 · 과열 폭발 · 폭발 · 질병 · 부상 · 선외 · 우주급 ───────────────────────────────

    /// <summary>설비 고장: 방 (회로 단락 · 차단기 · 주 컴퓨터 저장장치는 여러 방을 끄니 계통).</summary>
    public static IncidentScale Of(FaultKind k) => k is FaultKind.BreakerTrip or FaultKind.ShortCircuit or FaultKind.StorageFault ? IncidentScale.System : IncidentScale.Room;

    public static IncidentScale Of(BlowKind k) => k is BlowKind.Combustion or BlowKind.Rupture ? IncidentScale.System : IncidentScale.Room;
    public static string BlowName(BlowKind k) => k switch
    {
        BlowKind.ThermalRunaway => "열폭주", BlowKind.Hydrogen => "수소 폭발", BlowKind.ArcFlash => "아크 섬광", BlowKind.FuelFire => "연료 화재",
        BlowKind.Combustion => "연소실 파열", BlowKind.Rupture => "과압 파열", BlowKind.Grease => "기름 불", BlowKind.Furnace => "가열로 파열",
        BlowKind.Refrigerant => "냉매 누출", _ => "없음",
    };

    public static IncidentScale Of(BlastKind k) => k switch
    {
        BlastKind.ColdGas or BlastKind.Ferment or BlastKind.Aerosol or BlastKind.Flare => IncidentScale.Personal,
        BlastKind.Combustion or BlastKind.Charge or BlastKind.Propellant or BlastKind.Nitrate => IncidentScale.System,
        _ => IncidentScale.Room,
    };
    /// <summary>폭발 기록의 피해 규모(v16.13)를 다섯 규모로.</summary>
    public static IncidentScale Of(BlastScale s) => (IncidentScale)(int)s;

    public static IncidentScale Of(AilmentSpec a) => a.Group == AilmentGroup.Contagious ? IncidentScale.Room : IncidentScale.Personal;
    public static IncidentScale Of(WoundKind k) => IncidentScale.Personal;
    public static string WoundName(WoundKind k) => k switch
    {
        WoundKind.Cut => "베임", WoundKind.Burn => "데임", WoundKind.Fracture => "골절", WoundKind.Crush => "짓눌림",
        WoundKind.Barotrauma => "압력 손상", WoundKind.Toxic => "중독", _ => "방사선 피폭",
    };
    public static IncidentScale Of(SuitBreach b) => IncidentScale.Personal;
    public static string SuitName(SuitBreach b) => b switch
    {
        SuitBreach.Scratch => "우주복 긁힘", SuitBreach.Tear => "우주복 찢김", SuitBreach.MicroLeak => "우주복 미세 누출", SuitBreach.Puncture => "우주복 구멍", _ => "없음",
    };
    public static IncidentScale Of(CosmicKind k) => IncidentScale.Cosmic;

    // ─────────────────────────────── 표 하나 ───────────────────────────────

    public static readonly ScaleRow[] All = BuildRows();
    private static readonly Dictionary<string, ScaleRow> ByKey = BuildIndex();

    private static Dictionary<string, ScaleRow> BuildIndex()
    {
        var d = new Dictionary<string, ScaleRow>();
        foreach (var r in All) d.TryAdd(r.Key, r);
        return d;
    }

    private static ScaleRow[] BuildRows()
    {
        var rows = new List<ScaleRow>();
        // 이야기꾼 열쇠 (바깥 사건도 같은 열쇠로 건다)
        rows.Add(new("meteor", "작은 운석", "이야기꾼", IncidentScale.Room, "구멍이 번지면 계통"));
        rows.Add(new("bigmeteor", "큰 운석", "이야기꾼", IncidentScale.Ship, "선체 대파 · 분리"));
        rows.Add(new("fire", "작은 불", "이야기꾼", IncidentScale.Room, "옆방으로 번지면 계통 · 네 방이면 배 전체"));
        rows.Add(new("break", "설비 고장", "이야기꾼", IncidentScale.Room, "핵심 설비면 계통 (멈춤 → 정전)"));
        rows.Add(new("pipe", "배관 파열", "이야기꾼", IncidentScale.System, "냉각관이면 원자로 정지"));
        // 사고 70
        foreach (var s in Hazards.All)
            rows.Add(new(s.Kind.ToString(), s.Name, "사고", Of(s.Kind), Grows(Of(s.Kind))));
        // v16.19 계통 · 배 전체 사고 24
        foreach (var s in MajorIncidentSystem.All)
            rows.Add(new("major:" + s.Key, s.Name, "큰 사고", s.Scale, s.Scale >= IncidentScale.Ship ? "—" : "배의 절반 · 원자로 정지면 배 전체"));
        // 반란 (사기 바닥 · 다수의 분노 — ScaleSystem이 살핀다)
        rows.Add(new("unrest", "반란 조짐", "사람", IncidentScale.Ship, "사기가 돌아오면 가라앉는다"));
        // 인과 사슬의 고리
        foreach (var k in Enum.GetValues<CauseKind>())
            rows.Add(new("cause:" + k, CauseName(k), "사슬", Of(k), k is CauseKind.Outage or CauseKind.NoWater or CauseKind.NoAir or CauseKind.NoData
                ? "한 방이면 방 · 두 방부터 계통 · 배의 절반이면 배 전체" : Grows(Of(k))));
        // 설비 고장
        foreach (var k in Enum.GetValues<FaultKind>())
            rows.Add(new("fault:" + k, Faults.Spec(k).Name, "고장", Of(k), "핵심 설비가 멈추면 계통"));
        // 과열 폭발 (v12.2)
        foreach (var k in Enum.GetValues<BlowKind>())
            if (k != BlowKind.None) rows.Add(new("blow:" + k, BlowName(k), "과열 폭발", Of(k), "불 · 파편이 번지면 계통"));
        // 폭발 (v16.13)
        foreach (var k in Enum.GetValues<BlastKind>())
            rows.Add(new("blast:" + k, BlastSystem.Spec(k).Name, "폭발", Of(k), "압력파가 닿은 방 · 쓰러진 사람으로"));
        // 질병 30 + 열병
        foreach (var a in AilmentSystem.All.Append(AilmentSystem.Fever))
            rows.Add(new("ail:" + a.Id, a.Name, "질병", Of(a), a.Group == AilmentGroup.Contagious ? "옮아 여럿이면 계통" : "혼자 앓는다"));
        // 부상 (개인)
        foreach (var k in Enum.GetValues<WoundKind>())
            rows.Add(new("wound:" + k, WoundName(k), "부상", Of(k), "쓰러지면 곁의 사람이"));
        rows.Add(new("wound:slip", "미끄러짐", "부상", IncidentScale.Personal, "젖은 · 기름진 바닥"));
        rows.Add(new("wound:fall", "넘어짐", "부상", IncidentScale.Personal, "흔들림 · 발 빠짐"));
        // 선외 (v16.11)
        foreach (var b in Enum.GetValues<SuitBreach>())
            if (b != SuitBreach.None) rows.Add(new("eva:" + b, SuitName(b), "선외", Of(b), "새면 구조 EVA (계통)"));
        rows.Add(new("eva:drift", "선외 표류", "선외", IncidentScale.Personal, "구조 EVA가 나가면 계통"));
        // 우주급 30 (v18.13)
        foreach (var s in CosmicCatalog.All)
            rows.Add(new("cosmic:" + s.Id, s.Name, "우주급", IncidentScale.Cosmic, s.Avoid ? "항로를 바꿔 피할 수 있다" : "피할 수 없다 — 버틴다"));
        return rows.ToArray();
    }

    private static string Grows(IncidentScale s) => s switch
    {
        IncidentScale.Personal => "쓰러진 사람이 셋이면 배 전체",
        IncidentScale.Room => "두 방부터 계통",
        IncidentScale.System => "배의 절반 · 원자로 정지면 배 전체",
        _ => "—",
    };

    public static ScaleRow? Row(string key) => ByKey.TryGetValue(key, out var r) ? r : null;

    /// <summary>열쇠의 기본 규모 (모르는 열쇠는 방).</summary>
    public static IncidentScale OfKey(string key) => Row(key)?.Base ?? IncidentScale.Room;

    /// <summary>도감: 그 규모의 줄.</summary>
    public static IEnumerable<ScaleRow> Group(IncidentScale s) => All.Where(r => r.Base == s);

    /// <summary>어디서 온 사고 (표의 갈래 순서).</summary>
    public static readonly string[] Sources = { "이야기꾼", "사고", "큰 사고", "사람", "사슬", "고장", "과열 폭발", "폭발", "질병", "부상", "선외", "우주급" };
}
