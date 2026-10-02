using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.19 계통(③) · 배 전체(④) 사고 24종 — 자잘한 사고만 많던 분포를 바꾼다.
//  사고마다: 원인 · 전조(설비에 깃든 기척 — 감지기 · 당직이 먼저 보면 손봐서 막는다) · 발생(이미 있는 효과를 엮는다 — 관 파열 · 불 · 망 손상 ·
//  외벽 · 가스 · 오염 · 차단기 · 과열) · 번짐(손대지 않으면 몇 번 더 커진다) · 수습(효과가 다 걷히면) · 흔적(방 · 벽 · 설비 기록 + 그림) · 기억(겪은 사람).
//  규모 표(ScaleTable)에 "major:열쇠" 줄로 들어가 사건 규모 · 대응 인원이 그대로 따라오고, 이야기꾼은 규모 비율로 고른다.
//  주 컴퓨터: 전조를 읽으면 그 사고로 판단해 미리 손보자고 말하고, 발생하면 대응 순서를 방송한다.
//  승무원: 전조를 고치면 사고가 오지 않는다 · 겪은 방을 무서워한다 · 함께 막은 사람끼리 가까워진다.
// 난수는 전용(시드 × 소수) — 같은 시드면 같은 사고.

public enum MajorKind
{
    // 계통 · 구역 ③
    CoolantHeader, TrunkFire, CascadeDecomp, O2StoreLeak, DataCoreFire, PropellantLeak, WaterContam,
    SwitchboardFire, AirPlantFail, BatteryChain, SmokeSpread, WaterMainBurst, CropCollapse, ColdChain,
    // 배 전체 ④
    ReactorCoolingLoss, HullCrackRun, CargoBreakaway, MainBusFail, PlateTear, LifeSupportCascade,
    MultiZoneFire, AmmoniaRelease, EngineOverpressure, FrameResonance,
}

public enum MajorPhase : byte { Omen, Active, Contained, Averted }

/// <summary>사고 한 종류의 줄: 이름 · 규모 · 무게 · 원인 · 전조 · 대응 · 흔적 · 기억 · 전조가 깃드는 설비.</summary>
public sealed record MajorSpec(MajorKind Kind, string Key, string Name, IncidentScale Scale, float Weight,
    string Cause, string Omen, string Response, string Trace, string Memory, FurnitureType? OmenOn = null, OmenKind OmenSign = OmenKind.Vibration);

public sealed class MajorCase
{
    public int Id { get; init; }
    public MajorKind Kind { get; init; }
    public MajorSpec Spec => MajorIncidentSystem.Spec(Kind);
    public MajorPhase Phase { get; set; }
    public long Start { get; init; }
    public long Onset { get; set; } = -1;
    public long End { get; set; } = -1;
    public long OmenDue { get; set; } = -1;
    public int Machine { get; set; } = -1;   // 전조가 깃든 설비 (가구 번호)
    public FaultKind OmenFault { get; set; }
    public int Room { get; set; } = -1;
    public int Node { get; set; } = -1;
    public int Steps { get; set; }
    public long NextStep { get; set; } = -1;
    public List<int> Rooms { get; } = new();
    public List<Cell> Cells { get; } = new();
    public List<int> Links { get; } = new();
    public List<int> Doors { get; } = new();
    public List<int> Furn { get; } = new();
    public List<int> Witnesses { get; } = new();
    public List<int> Responders { get; } = new();
    public bool OmenRead { get; set; }
    public string Text { get; set; } = "";
    public bool Open => Phase is MajorPhase.Omen or MajorPhase.Active;
}

/// <summary>흔적 하나 (그림이 오래 남긴다).</summary>
public sealed record MajorTrace(MajorKind Kind, Cell At, long Tick, int Room);

public sealed class MajorIncidentSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7727 + 3301));
    private int _next = 1;

    public List<MajorCase> Cases { get; } = new();
    public List<MajorTrace> Traces { get; } = new();
    public int[] Count { get; } = new int[Enum.GetValues<MajorKind>().Length];
    public int Averted, Contained, OmensRead, Broadcasts;

    public MajorIncidentSystem(World w) => _w = w;

    // ─────────────────────────────── 표 ───────────────────────────────

    public static readonly MajorSpec[] All =
    {
        new(MajorKind.CoolantHeader, "coolheader", "주 냉각 루프 파열", IncidentScale.System, 2.2f,
            "냉각 본관 이음매의 피로 · 펌프 진동", "냉각 펌프가 떨리고 본관 압력이 요동친다",
            "밸브로 그 관을 막고 · 쏟아진 냉각수를 피해 관을 덧대거나 간다 · 원자로 출력을 낮춘다", "바닥에 냉각수 얼룩 · 새로 간 관 이음매", "뜨거운 냉각수가 뿜어지던 소리",
            FurnitureType.CoolantPump, OmenKind.Vibration),
        new(MajorKind.TrunkFire, "trunkfire", "간선 화재", IncidentScale.System, 2.2f,
            "배전실 앞 간선 접속부가 달아올랐다", "배전실 앞 천장에서 탄내 · 접속부가 뜨겁다",
            "그 간선을 내리고 불을 끈다 · 끊긴 간선은 다시 잇는다 (필수 방은 예비 간선으로 버틴다)", "그을린 케이블 트레이 · 새로 이은 간선", "천장에서 케이블이 타 들어가던 냄새",
            FurnitureType.PowerPanel, OmenKind.Heat),
        new(MajorKind.CascadeDecomp, "cascadedecomp", "구획 연쇄 감압", IncidentScale.System, 1.8f,
            "외벽 파공에 문틀까지 휘었다", "외벽이 삐걱이고 문틀이 어긋난다",
            "새는 방에서 나온다 · 휜 문을 손으로 닫고 구멍부터 막는다 · 옆 구획은 문이 막아 준다", "휜 문틀을 편 자국 · 외벽 용접선", "문이 안 닫혀 귀가 먹먹해지던 순간"),
        new(MajorKind.O2StoreLeak, "o2store", "산소 저장고 누출", IncidentScale.System, 1.6f,
            "고압 산소관 밸브 씰이 상했다", "산소 탱크 압력이 조금씩 떨어진다",
            "불꽃을 내지 않는다 · 실링폼으로 막고 환기한다 · 짙은 산소가 빠질 때까지 용접 금지", "새 밸브 씰 · 탱크에 붙인 경고 딱지", "숨이 이상하게 가볍던 공기",
            FurnitureType.OxygenGenerator, OmenKind.Pressure),
        new(MajorKind.DataCoreFire, "datacore", "데이터 코어 화재", IncidentScale.System, 1.4f,
            "서버 랙 전원부가 과열됐다", "주 컴퓨터 팬이 비명을 지르고 열이 오른다",
            "그 랙만 내리고 질식 소화 · 자동 제어가 늦으면 문 · 댐퍼는 손으로", "그을린 랙 · 갈아 끼운 저장장치", "컴퓨터 목소리가 끊기던 순간",
            FurnitureType.MainComputer, OmenKind.Heat),
        new(MajorKind.PropellantLeak, "propellant", "추진제 누출", IncidentScale.System, 1.4f,
            "추진제 이송관 이음매가 풀렸다", "엔진실에 시큼한 냄새 · 이송 압력이 흔들린다",
            "엔진을 세우고 불꽃을 없앤다 · 우주복 입고 새는 곳을 막는다 · 환기", "추진제 얼룩 · 조여 둔 이음매", "눈이 따갑던 엔진실",
            FurnitureType.EngineCore, OmenKind.Pressure),
        new(MajorKind.WaterContam, "watercontam", "정수 계통 오염", IncidentScale.System, 1.8f,
            "정수기 막이 찢어져 오염수가 섞였다", "물맛이 이상하다 · 정수기 압력이 오르내린다",
            "물 마시지 말라고 알린다 · 막을 갈고 관을 씻어 낸다 · 비축 물을 쓴다", "물탱크 소독 기록 · 새 막", "물에서 쇠 맛이 나던 날",
            FurnitureType.WaterRecycler, OmenKind.Pressure),
        new(MajorKind.SwitchboardFire, "switchfire", "주 배전반 화재", IncidentScale.System, 1.6f,
            "배전반 모선 단자가 풀려 아크가 튄다", "배전반에서 지직 소리 · 단자 열",
            "회로를 차례로 내리고 불을 끈다 · 필수 방은 예비 회로로 넘어간다 · 차단기는 원인부터 보고 올린다", "그을린 배전반 문짝 · 새 단자", "배전반에서 파란 불꽃이 튀던 소리",
            FurnitureType.PowerPanel, OmenKind.Heat),
        new(MajorKind.AirPlantFail, "airplant", "공기 정화 계통 마비", IncidentScale.System, 1.8f,
            "세정 필터가 한꺼번에 포화됐다", "산소 발생기 필터 압력이 오른다",
            "필터를 갈고 · 사람 많은 방을 흩어 쉰다 · 이산화탄소가 높은 방은 비운다", "갈아 낸 필터 더미", "머리가 지끈거리던 저녁",
            FurnitureType.OxygenGenerator, OmenKind.Pressure),
        new(MajorKind.BatteryChain, "batterychain", "배터리 열폭주 연쇄", IncidentScale.System, 1.4f,
            "셀 하나가 부풀어 옆 셀까지 달군다", "배터리 셀 온도가 들쭉날쭉하다",
            "달아오른 배터리를 떼고 식힌다 · 옆 배터리와 거리를 둔다", "녹은 셀 자국 · 떼어 둔 배터리", "배터리가 쉭쉭 끓던 소리",
            FurnitureType.Battery, OmenKind.Heat),
        new(MajorKind.SmokeSpread, "smokespread", "환기 계통 연기 확산", IncidentScale.System, 1.6f,
            "덕트 속 단열재가 그을린다", "환기구에서 매캐한 냄새",
            "댐퍼를 닫고 연기 난 방을 비운다 · 덕트를 열어 불씨를 찾는다", "그을린 환기구", "온 배에 연기 냄새가 돌던 밤"),
        new(MajorKind.WaterMainBurst, "watermain", "급수 본관 파열", IncidentScale.System, 1.6f,
            "급수 본관이 압력에 터졌다", "정수기 펌프가 떨리고 수압이 튄다",
            "밸브를 잠그고 고인 물을 퍼낸다 · 젖은 분전함은 내린다 · 관을 잇는다", "물때 자국 · 새 급수관", "발목까지 물이 차던 복도",
            FurnitureType.WaterRecycler, OmenKind.Vibration),
        new(MajorKind.CropCollapse, "cropcollapse", "수경 계통 붕괴", IncidentScale.System, 1.4f,
            "양액 배합이 틀어져 뿌리가 썩는다", "잎끝이 누렇게 말린다",
            "양액을 갈고 병든 판을 뽑는다 · 저장 식량으로 버틴다", "뽑아낸 판 · 새 양액 통", "누렇게 시든 재배실",
            FurnitureType.GrowBed, OmenKind.Pressure),
        new(MajorKind.ColdChain, "coldchain", "냉동 저장고 고장", IncidentScale.System, 1.4f,
            "냉장 압축기가 줄줄이 멈췄다", "냉장고 문 틈에 성에가 녹는다",
            "압축기를 고치거나 식량을 옮긴다 · 늦으면 상한 것을 버린다", "버린 식량 기록 · 새 압축기", "상한 냄새가 나던 주방",
            FurnitureType.Fridge, OmenKind.Vibration),
        // ── 배 전체 ④ ──
        new(MajorKind.ReactorCoolingLoss, "reactorcool", "원자로 냉각 상실", IncidentScale.Ship, 1.2f,
            "냉각 펌프가 한꺼번에 들러붙었다", "냉각 펌프 여러 대가 같이 떨린다",
            "원자로를 세운다 · 배터리 · 보조 발전기로 필수 방을 지킨다 · 펌프를 하나씩 살린다", "새 펌프 베어링 · 노심 온도 기록", "배 전체 불이 깜빡이던 순간",
            FurnitureType.CoolantPump, OmenKind.Vibration),
        new(MajorKind.HullCrackRun, "crackrun", "선체 균열 전파", IncidentScale.Ship, 1.2f,
            "피로가 쌓인 외판이 갈라지며 옆 판으로 번진다", "외벽에서 금속 비명 · 실금",
            "균열 끝을 먼저 용접해 멈춘다 · 그 방은 비운다", "길게 이어진 용접선", "벽 속에서 금이 달려가던 소리"),
        new(MajorKind.CargoBreakaway, "cargo", "대형 화물 이탈", IncidentScale.Ship, 1.0f,
            "고정끈이 끊겨 화물이 쏠렸다", "화물 고정끈이 삐걱인다",
            "다친 사람부터 빼낸다 · 문을 막은 짐을 치운다 · 다시 묶는다", "찌그러진 선반 · 새 고정끈", "짐이 쏟아지던 굉음"),
        new(MajorKind.MainBusFail, "mainbus", "주 모선 고장", IncidentScale.Ship, 0.8f,
            "배전반 모선이 끊어졌다", "배전반 전압계가 흔들린다",
            "필수 회로부터 하나씩 올린다 · 보조 발전기를 돌린다", "새 모선 막대", "배 전체가 캄캄해지던 순간",
            FurnitureType.PowerPanel, OmenKind.Drift),
        new(MajorKind.PlateTear, "platetear", "외판 박리", IncidentScale.Ship, 0.9f,
            "외판이 골조에서 뜯겨 나갔다", "외벽 리벳이 튀는 소리",
            "그 방을 비우고 문을 닫는다 · 밖에서 골조부터 다시 세운다 (드론 · 선외 작업)", "새 골조 · 덧댄 외판", "벽이 뜯겨 나가던 바람 소리"),
        new(MajorKind.LifeSupportCascade, "lscascade", "생명유지 연쇄 정지", IncidentScale.Ship, 1.0f,
            "전해조가 멎고 물까지 끊겼다", "산소 발생기 전압이 흔들린다",
            "산소 발생기부터 살린다 · 물을 길어 넣는다 · 공기 탱크로 버틴다", "갈아 끼운 전해조", "숨이 가빠지던 밤",
            FurnitureType.OxygenGenerator, OmenKind.Heat),
        new(MajorKind.MultiZoneFire, "multifire", "다구역 화재", IncidentScale.Ship, 1.0f,
            "열에 휜 문 사이로 불이 두 방을 오간다", "문틀이 뜨겁다",
            "두 방을 동시에 끈다 · 휜 문을 억지로 닫는다", "검게 탄 두 방 · 편 문틀", "불길이 문을 넘던 순간"),
        new(MajorKind.AmmoniaRelease, "ammonia", "냉매 대량 누출", IncidentScale.Ship, 1.0f,
            "열교환기 냉매관이 터졌다", "냉각실에 톡 쏘는 냄새",
            "우주복 입고 들어가 막는다 · 옆 방을 비운다 · 세정기로 걸러 낸다", "새 냉매관 · 경고 딱지", "눈물이 나던 냉각실",
            FurnitureType.CoolantPump, OmenKind.Pressure),
        new(MajorKind.EngineOverpressure, "engineover", "엔진 연소실 과압", IncidentScale.Ship, 0.9f,
            "분사기가 막혀 연소실 압력이 치솟는다", "엔진 소리가 거칠어진다",
            "엔진을 세우고 식힌다 · 상한 외벽을 막는다", "그을린 연소실 · 새 분사기", "엔진이 터질 듯 울던 소리",
            FurnitureType.EngineCore, OmenKind.Vibration),
        new(MajorKind.FrameResonance, "resonance", "골조 공진", IncidentScale.Ship, 1.0f,
            "엔진 진동이 골조와 맞물려 울린다", "배 전체가 낮게 웅웅거린다",
            "엔진 출력을 바꾼다 · 연결부를 살펴 조인다", "조인 연결부 표시", "배가 통째로 울던 밤"),
    };

    public static MajorSpec Spec(MajorKind k) => All[(int)k];
    public static MajorSpec? ByKey(string key) => All.FirstOrDefault(s => "major:" + s.Key == key);

    // ─────────────────────────────── 걸기 ───────────────────────────────

    /// <summary>이야기꾼 · 무작위 사고가 건다 ("major:열쇠"). 전조가 있는 사고는 전조부터 — 설비에 기척이 깃든다.</summary>
    public string? Fire(string key, Room? prefer)
    {
        var spec = ByKey(key);
        return spec == null ? null : Start(spec.Kind, prefer, omen: true);
    }

    /// <summary>시험 · 기록: 사고를 건다 (omen이 거짓이면 바로 발생).</summary>
    public string? Start(MajorKind kind, Room? prefer = null, bool omen = true)
    {
        var w = _w;
        if (Cases.Any(c => c.Open && c.Kind == kind)) return null;
        var spec = Spec(kind);
        var k = new MajorCase { Id = _next++, Kind = kind, Start = w.Tick, Phase = MajorPhase.Omen };
        if (omen && spec.OmenOn is FurnitureType ft && OmenMachine(ft, prefer) is Machine m && PlantOmen(k, m, spec))
        {
            Cases.Add(k);
            k.Text = $"{spec.Name} 전조({m.Name})";
            return k.Text;
        }
        Cases.Add(k);
        if (!Onset(k, prefer)) { Cases.Remove(k); return null; }
        return k.Text;
    }

    private Machine? OmenMachine(FurnitureType t, Room? prefer)
    {
        var list = _w.Ship.FurnitureOf(t).Where(f => !f.Stowed && !f.Room.Detached && f.Machine is { } m && m.Omen == null && m.Faults.Count == 0).OrderBy(f => f.Id).ToList();
        if (list.Count == 0) return null;
        return (prefer != null ? list.FirstOrDefault(f => f.Room == prefer) : null)?.Machine ?? list[R.Range(0, list.Count)].Machine;
    }

    /// <summary>설비에 기척을 심는다 (1~3시간 뒤 고장 — 감지기 · 당직 · 순찰이 먼저 보면 손봐서 막는다).</summary>
    private bool PlantOmen(MajorCase k, Machine m, MajorSpec spec)
    {
        var w = _w;
        var faults = m.Spec.FaultKinds.Where(f => f != FaultKind.BreakerTrip && Prevention.KindOf(f) != null).ToList();
        if (faults.Count == 0) return false;
        var fault = faults.FirstOrDefault(f => Prevention.KindOf(f) == spec.OmenSign);
        if (fault == default && Prevention.KindOf(faults[0]) != spec.OmenSign) fault = faults[0];
        var sign = Prevention.KindOf(fault)!.Value;
        var causes = ShipSim.Core.Causes.For(m.Body.Type, sign).Where(c => c != OmenCause.Phantom).ToList();
        var cause = causes.Count > 0 ? causes[R.Range(0, causes.Count)] : OmenCause.LooseMount;
        float hours = R.Range(1.2f, 3f);
        m.Omen = new Omen { Kind = sign, Fault = fault, Cause = cause, Since = w.Tick, Due = w.Tick + SimTime.Hours(hours) };
        w.Precursors.Omens++;
        k.Machine = m.Body.Id;
        k.OmenFault = fault;
        k.OmenDue = m.Omen.Due;
        k.Room = m.Body.Room.Id;
        MarkLog.Add(m.Marks, w.Tick, $"기척: {spec.Omen}");
        return true;
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        for (int i = 0; i < Cases.Count; i++)
        {
            var k = Cases[i];
            if (k.Phase == MajorPhase.Omen) OmenTick(k);
            else if (k.Phase == MajorPhase.Active) ActiveTick(k);
        }
        if (Cases.Count > 120) Cases.RemoveAll(c => !c.Open && w.Tick - c.Start > SimTime.Hours(72));
        if (Traces.Count > 200) Traces.RemoveRange(0, Traces.Count - 200);
    }

    private Furniture? FurnById(int id) => _w.Ship.Furniture.FirstOrDefault(f => f.Id == id);
    private Room? RoomById(int id) => _w.Ship.Rooms.FirstOrDefault(r => r.Id == id);

    private void OmenTick(MajorCase k)
    {
        var w = _w;
        var f = FurnById(k.Machine);
        var m = f?.Machine;
        if (m == null || f!.Room.Detached) { k.Phase = MajorPhase.Averted; k.End = w.Tick; return; }
        // 주 컴퓨터가 기척을 읽었다 (감지기가 잡았거나 당직이 일지에 올렸다) — 그 사고로 판단해 미리 손보자고 말한다
        if (m.Omen is Omen o && o.Known && !k.OmenRead)
        {
            k.OmenRead = true;
            OmensRead++;
            string text = $"{m.Name} {Prevention.Name(o.Kind)} — {k.Spec.Name}로 번질 기척으로 본다 · 고장 나기 전에 손보자 ({k.Spec.Omen})";
            w.Automation.Reason("major:omen:" + k.Id, text, SimTime.Hours(2));
            w.RaiseAlert($"{k.Spec.Name} 전조 — {m.Name}", f.Room, AlertLevel.Warning, shipWide: false);
            foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == f.Room && c.IsAwake))
                w.Brain2.Beliefs.Learn(c, Topic.Omen, f.Id, 1, BeliefSource.Computer, 0.7f);
        }
        if (m.Omen != null) return;
        if (m.Has(k.OmenFault) || w.Tick >= k.OmenDue)
        {
            // 아무도 손보지 못했다 — 터진다
            if (!Onset(k, f.Room)) { k.Phase = MajorPhase.Averted; k.End = w.Tick; }
            return;
        }
        // 전조를 손봤다: 사고가 오지 않는다
        k.Phase = MajorPhase.Averted;
        k.End = w.Tick;
        Averted++;
        var by = w.Crew.Where(c => !c.Dead && c.Room == f.Room).OrderBy(c => c.Id).FirstOrDefault();
        string done = $"{m.Name}의 기척을 미리 손봐 {Ko.EulReul(k.Spec.Name)} 막았다" + (by != null ? $" — {by.Name}" : "");
        w.History.Add(w, HistoryKind.Response, done, f.Room, by != null ? new[] { by } : null);
        MarkLog.Add(f.Room.Marks, w.Tick, done);
        if (by != null) MarkLog.Add(by.Memory.Marks, w.Tick, $"{Ko.EulReul(k.Spec.Name)} 미리 막았다");
        w.Automation.Reason("major:averted:" + k.Id, $"{done} · 같은 기척이 또 보이면 바로 부르겠다", SimTime.Hours(2));
    }

    private void ActiveTick(MajorCase k)
    {
        var w = _w;
        // 그 방들에서 일하는 사람 = 대응한 사람
        foreach (var c in w.Crew)
            if (!c.Dead && c.Room != null && k.Rooms.Contains(c.Room.Id) && c.Job != null && !k.Responders.Contains(c.Id)) k.Responders.Add(c.Id);
        if (k.NextStep >= 0 && w.Tick >= k.NextStep && k.Steps < MaxSteps(k.Kind))
        {
            k.Steps++;
            k.NextStep = w.Tick + StepEvery(k.Kind);
            using (w.Causes.Because(k.Node)) Spread(k);
        }
        bool done = w.Tick - k.Onset > SimTime.Minutes(5) && Settled(k);
        if (!done && w.Tick - k.Onset < SimTime.Hours(14)) return;
        Close(k, done);
    }

    // ─────────────────────────────── 발생 ───────────────────────────────

    private bool Onset(MajorCase k, Room? prefer)
    {
        var w = _w;
        var spec = k.Spec;
        int node = w.Causes.Root(CauseKind.Hazard, spec.Name, prefer, null, observer: w.Causes.ConsumeObserver());
        w.Scale.Tag(node, "major:" + spec.Key);
        string? what;
        using (w.Causes.Because(node)) what = Apply(k, prefer);
        if (what == null) { w.Causes.Discard(node); return false; }
        k.Node = node;
        k.Phase = MajorPhase.Active;
        k.Onset = w.Tick;
        k.Text = what;
        if (k.Room < 0 && k.Rooms.Count > 0) k.Room = k.Rooms[0];
        if (k.Room >= 0 && !k.Rooms.Contains(k.Room)) k.Rooms.Insert(0, k.Room);
        k.NextStep = MaxSteps(k.Kind) > 0 ? w.Tick + StepEvery(k.Kind) : -1;
        w.Causes.Node(node).Text = what;
        Count[(int)k.Kind]++;
        var room = RoomById(k.Room);
        w.History.Add(w, HistoryKind.Incident, $"{what} — {spec.Cause}", room);
        w.History.NoteCause(w, what);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room == null || !k.Rooms.Contains(c.Room.Id)) continue;
            k.Witnesses.Add(c.Id);
            Memory.Frighten(w, c, c.Room, spec.Scale >= IncidentScale.Ship ? 0.35f : 0.22f, spec.Memory);
        }
        // 주 컴퓨터: 대응 순서를 방송한다 (멎었으면 사람이 외친다 — 규모 체계가 받는다)
        if (w.Automation.MainOnline)
        {
            Broadcasts++;
            w.Automation.Reason("major:on:" + k.Id, $"{spec.Name} — {spec.Response}", SimTime.Hours(1));
        }
        w.RaiseAlert($"{what} — {spec.Response}", room, AlertLevel.Critical, shipWide: true);
        if (room != null) w.Movement.Bang(room, room.Center, spec.Scale >= IncidentScale.Ship ? 0.9f : 0.6f, spec.Name);
        w.Board.RequestScan();
        return true;
    }

    private Room? Pick(Func<Room, bool> ok, Room? prefer)
    {
        var rooms = _w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && ok(r)).OrderBy(r => r.Id).ToList();
        if (rooms.Count == 0) return null;
        if (prefer != null && rooms.Contains(prefer)) return prefer;
        // 통합: 큰 사고도 대개 사람이 쓰고 · 지내는 곳에서 커진다 (돌리던 설비 · 자던 방 — 사람이 있으면 더 자주, 빈 방도 가끔)
        float sum = 0f;
        var wt = new float[rooms.Count];
        for (int i = 0; i < rooms.Count; i++)
        {
            int n = 0;
            foreach (var c in _w.Crew) if (!c.Dead && !c.Away && c.Room == rooms[i]) n++;
            sum += wt[i] = 1f + 1.2f * Math.Min(3, n);
        }
        float x = R.Float() * sum;
        for (int i = 0; i < rooms.Count; i++) { x -= wt[i]; if (x <= 0f) return rooms[i]; }
        return rooms[^1];
    }

    private Furniture? Furn(FurnitureType t, Room? prefer = null)
    {
        var list = _w.Ship.FurnitureOf(t).Where(f => !f.Stowed && !f.Room.Detached).OrderBy(f => f.Id).ToList();
        if (list.Count == 0) return null;
        return (prefer != null ? list.FirstOrDefault(f => f.Room == prefer) : null) ?? list[0];
    }

    private Cell FloorNear(Room r, Cell near)
    {
        var floor = r.Cells.Where(_w.Ship.IsOpenFloor).OrderBy(c => (c.Center - near.Center).LengthSquared()).ThenBy(c => c.Y).ThenBy(c => c.X).FirstOrDefault();
        return floor == default ? r.Cells[0] : floor;
    }

    private bool Ignite(MajorCase k, Room r, Cell near, float amt)
    {
        var c = FloorNear(r, near);
        if (!_w.Fire.Ignite(c, amt)) return false;
        if (!k.Cells.Contains(c)) k.Cells.Add(c);
        if (!k.Rooms.Contains(r.Id)) k.Rooms.Add(r.Id);
        return true;
    }

    private void Scald(Room r, float amt, string cause)
    {
        foreach (var c in _w.Crew)
            if (!c.Dead && c.Room == r && R.Chance(0.6f)) { NeedsSystem.AddInjury(c.Vitals, amt, cause); c.Interrupt(_w); }
    }

    private bool BreakM(Machine m, FaultKind f) => m.Has(f) || _w.Machines.Break(m, f) != null;

    private IEnumerable<Room> Neighbors(Room r) => r.Doors.Where(d => !d.Removed && !d.IsExternal).Select(d => d.RoomA == r ? d.RoomB : d.RoomA).OfType<Room>().Where(x => !x.Detached).Distinct().OrderBy(x => x.Id);

    private List<Cell> HullCells(Room r) => _w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(_w.Ship, kv.Key) == r).Select(kv => kv.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();

    private string? Apply(MajorCase k, Room? prefer)
    {
        var w = _w;
        var ship = w.Ship;
        var spec = k.Spec;
        switch (k.Kind)
        {
            case MajorKind.CoolantHeader:
            {
                var segs = w.Piping.Segments.Where(s => s.IsCoolant && s.Path.Count > 0 && s.Role is PipeRole.HotLeg or PipeRole.ColdLeg).OrderBy(s => s.Id).ToList();
                if (segs.Count == 0) segs = w.Piping.Segments.Where(s => s.IsCoolant && s.Path.Count > 0).OrderBy(s => s.Id).ToList();
                if (segs.Count == 0) return null;
                var seg = segs.OrderByDescending(s => s.Path.Count).First();
                var hit = w.Piping.Burst(seg.Path[seg.Path.Count / 2], 0.8f);
                if (hit == null) return null;
                k.Links.Add(hit.Id);
                k.Cells.Add(hit.LeakAt);
                if (ship.RoomAt(hit.LeakAt) is Room r) { k.Room = r.Id; w.Moisture.AddWater(r, 50f); Scald(r, 0.08f, "뜨거운 냉각수에 데었다"); }
                return $"{spec.Name}({hit.Name})";
            }
            case MajorKind.TrunkFire:
            {
                var src = w.Net.SourceRoom(NetKind.Power);
                if (src == null) return null;
                var links = w.Net.Links.Where(l => l.Kind == NetKind.Power && !l.Cut && l.Door != null && (l.Door.RoomA == src || l.Door.RoomB == src)).OrderBy(l => l.Id).ToList();
                if (links.Count == 0) return null;
                var l0 = links[R.Range(0, links.Count)];
                w.Net.Hurt(l0, 0.8f, spec.Name);
                k.Links.Add(l0.Id);
                k.Room = l0.Room.Id;
                if (l0.Cells.Count > 0) Ignite(k, l0.Room, l0.Cells[l0.Cells.Count / 2], 0.45f);
                return $"{spec.Name}({l0.Room.Name})";
            }
            case MajorKind.CascadeDecomp:
            {
                var r = Pick(x => x.Type != RoomType.Corridor && x.Doors.Count(d => !d.IsExternal && !d.Removed) >= 1 && HullCells(x).Count > 0, prefer);
                if (r == null) return null;
                var hull = HullCells(r);
                var cell = hull[R.Range(0, hull.Count)];
                Hull.Damage(ship, cell, 1.2f);
                k.Cells.Add(cell);
                k.Room = r.Id;
                var door = r.Doors.Where(d => !d.IsExternal && !d.Removed && d.RoomA != null && d.RoomB != null).OrderBy(d => d.Id).FirstOrDefault();
                if (door != null) { door.Bent = MathF.Max(door.Bent, 0.4f); k.Doors.Add(door.Id); }
                return $"{spec.Name}({r.Name})";
            }
            case MajorKind.O2StoreLeak:
            {
                var r = Furn(FurnitureType.OxygenGenerator, prefer)?.Room ?? Pick(x => x.Type is RoomType.LifeSupport or RoomType.GasStorage, prefer);
                if (r == null) return null;
                r.O2Leak = MathF.Max(r.O2Leak, 7f);
                float lost = w.Air.ReserveCapacity * 0.12f;
                w.Air.Reserve = MathF.Max(0f, w.Air.Reserve - lost);
                k.Room = r.Id;
                return $"{spec.Name}({r.Name})";
            }
            case MajorKind.DataCoreFire:
            {
                var f = Furn(FurnitureType.MainComputer, prefer);
                if (f == null) return null;
                f.Machine!.Heat = MathF.Max(f.Machine.Heat, 0.9f);
                if (!Ignite(k, f.Room, f.Cells[0], 0.5f)) return null;
                foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && l.Room == f.Room && !l.Cut).OrderBy(l => l.Id).Take(2)) { w.Net.Hurt(l, 0.7f, spec.Name); k.Links.Add(l.Id); }
                k.Room = f.Room.Id;
                k.Furn.Add(f.Id);
                return $"{spec.Name}({f.Room.Name})";
            }
            case MajorKind.PropellantLeak:
            {
                var f = Furn(FurnitureType.EngineCore, prefer);
                if (f == null) return null;
                BreakM(f.Machine!, FaultKind.GasLeak);
                f.Room.Air.Toxin = MathF.Min(1.5f, f.Room.Air.Toxin + 0.45f);
                w.Propulsion.Propellant = MathF.Max(0f, w.Propulsion.Propellant * 0.9f);
                k.Room = f.Room.Id;
                k.Furn.Add(f.Id);
                return $"{spec.Name}({f.Room.Name})";
            }
            case MajorKind.WaterContam:
            {
                var f = Furn(FurnitureType.WaterRecycler, prefer);
                if (f == null) return null;
                BreakM(f.Machine!, FaultKind.MembraneFouling);
                w.Flow.WaterQuality = MathF.Max(0f, w.Flow.WaterQuality - 0.55f);
                foreach (var g in ship.RoomsOf(RoomType.Galley).Where(g => !g.Detached).Take(1))
                    w.Soil.RoomSoil(g)[(int)SoilKind.Bio] = MathF.Min(1f, w.Soil.RoomSoil(g)[(int)SoilKind.Bio] + 0.3f);
                k.Room = f.Room.Id;
                k.Furn.Add(f.Id);
                return $"{spec.Name}({f.Room.Name})";
            }
            case MajorKind.SwitchboardFire:
            {
                var f = Furn(FurnitureType.PowerPanel, prefer);
                if (f == null) return null;
                var panel = f.Machine!;
                panel.Heat = MathF.Max(panel.Heat, 0.9f);
                Ignite(k, f.Room, f.Cells[0], 0.4f);
                int n = 0;
                for (int c = 1; c < PowerGrid.CircuitCount && n < 2; c++)
                {
                    int circuit = (c + R.Range(0, PowerGrid.CircuitCount)) % PowerGrid.CircuitCount;
                    if (panel.Faults.Any(x => x.Circuit == circuit)) continue;
                    panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = circuit });
                    w.Causes.OnFault(panel, panel.Faults[^1]);
                    panel.FaultCount++;
                    w.History.CircuitFaults++;
                    n++;
                }
                k.Room = f.Room.Id;
                k.Furn.Add(f.Id);
                return $"{spec.Name}({f.Room.Name})";
            }
            case MajorKind.AirPlantFail:
            {
                var gens = ship.FurnitureOf(FurnitureType.OxygenGenerator).Concat(ship.FurnitureOf(FurnitureType.Scrubber)).Where(f => !f.Room.Detached && f.Machine != null).OrderBy(f => f.Id).ToList();
                if (gens.Count == 0) return null;
                foreach (var g in gens) if (BreakM(g.Machine!, FaultKind.FilterClogged) || g.Machine!.Has(FaultKind.FilterClogged)) k.Furn.Add(g.Id);
                if (k.Furn.Count == 0) return null;
                foreach (var r in Crowded(2)) { r.Air.CO2 += 0.8f; if (!k.Rooms.Contains(r.Id)) k.Rooms.Add(r.Id); }
                k.Room = gens[0].Room.Id;
                return spec.Name;
            }
            case MajorKind.BatteryChain:
            {
                var bats = ship.FurnitureOf(FurnitureType.Battery).Where(f => !f.Room.Detached && f.Machine != null && !f.Machine.Parked).OrderBy(f => f.Id).ToList();
                if (bats.Count == 0) return null;
                var b = prefer != null ? bats.FirstOrDefault(x => x.Room == prefer) ?? bats[0] : bats[R.Range(0, bats.Count)];
                b.Machine!.Heat = MathF.Max(b.Machine.Heat, 0.93f);
                k.Furn.Add(b.Id);
                k.Room = b.Room.Id;
                return $"{spec.Name}({b.Label})";
            }
            case MajorKind.SmokeSpread:
            {
                var r = Pick(x => x.Type != RoomType.Corridor && x.VentOpen && x.DuctLinked, prefer);
                if (r == null) return null;
                r.Air.Smoke = MathF.Min(1f, r.Air.Smoke + 0.6f);
                Ignite(k, r, r.Cells[r.Cells.Count / 2], 0.2f);
                k.Room = r.Id;
                return $"{spec.Name}({r.Name})";
            }
            case MajorKind.WaterMainBurst:
            {
                var f = Furn(FurnitureType.WaterRecycler, prefer);
                if (f == null) return null;
                var links = w.Net.Links.Where(l => l.Kind == NetKind.Water && !l.Cut && l.Door != null && (l.Door.RoomA == f.Room || l.Door.RoomB == f.Room)).OrderBy(l => l.Id).Take(2).ToList();
                if (links.Count == 0) return null;
                foreach (var l in links) { w.Net.Hurt(l, 1f, spec.Name); k.Links.Add(l.Id); if (!k.Rooms.Contains(l.Room.Id)) k.Rooms.Add(l.Room.Id); }
                w.Moisture.AddWater(f.Room, 45f);
                k.Room = f.Room.Id;
                return $"{spec.Name}({f.Room.Name})";
            }
            case MajorKind.CropCollapse:
            {
                var beds = ship.FurnitureOf(FurnitureType.GrowBed).Where(f => !f.Room.Detached && f.Machine?.Crop != null).OrderBy(f => f.Id).ToList();
                if (beds.Count == 0) return null;
                int n = 0;
                foreach (var b in beds.Take(3)) { if (BreakM(b.Machine!, FaultKind.NutrientClog)) n++; k.Furn.Add(b.Id); }
                w.Hazards.Blight(beds[0]);
                k.Room = beds[0].Room.Id;
                return n == 0 && beds[0].Machine!.Crop!.Blight <= 0f ? null : $"{spec.Name}({beds[0].Room.Name})";
            }
            case MajorKind.ColdChain:
            {
                var fr = ship.FurnitureOf(FurnitureType.Fridge).Where(f => !f.Room.Detached && f.Machine != null).OrderBy(f => f.Id).ToList();
                if (fr.Count == 0) return null;
                foreach (var f in fr) if (BreakM(f.Machine!, FaultKind.CompressorFail)) k.Furn.Add(f.Id);
                if (k.Furn.Count == 0) return null;
                k.Room = fr[0].Room.Id;
                return $"{spec.Name}({fr[0].Room.Name})";
            }
            case MajorKind.ReactorCoolingLoss:
            {
                var pumps = ship.FurnitureOf(FurnitureType.CoolantPump).Where(f => !f.Room.Detached && f.Machine != null).OrderBy(f => f.Id).ToList();
                if (pumps.Count == 0) return null;
                foreach (var p in pumps.Take(Math.Max(1, pumps.Count - 1))) if (BreakM(p.Machine!, FaultKind.PumpSeized)) k.Furn.Add(p.Id);
                w.Power.Heat(35f);
                k.Room = (w.Power.Reactor?.Body.Room ?? pumps[0].Room).Id;
                foreach (var p in pumps) if (!k.Rooms.Contains(p.Room.Id)) k.Rooms.Add(p.Room.Id);
                return spec.Name;
            }
            case MajorKind.HullCrackRun:
            {
                var r = Pick(x => x.Type != RoomType.Corridor && HullCells(x).Count >= 4, prefer);
                if (r == null) return null;
                var hull = HullCells(r);
                var cell = hull[R.Range(0, hull.Count)];
                if (ship.WallAt(cell) is not WallState ws) return null;
                ws.Integrity = MathF.Min(ws.Integrity, 0.28f);
                ws.Breach = Hull.BreachOf(ws);
                k.Cells.Add(cell);
                k.Room = r.Id;
                MarkLog.Add(ws.Marks, w.Tick, "균열이 시작됐다");
                return $"{spec.Name}({r.Name})";
            }
            case MajorKind.CargoBreakaway:
            {
                var r = Pick(x => x.Type is RoomType.Storage or RoomType.Cargo or RoomType.Workshop, prefer);
                if (r == null) return null;
                var hit = new HashSet<Machine>();
                foreach (var f in r.Furniture.Where(f => f.Machine != null).OrderBy(f => f.Id).Take(2)) Shrapnel.HitMachine(w, R, f.Machine!, 0.45f, spec.Name);
                foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == r).OrderBy(c => c.Id))
                    if (R.Chance(0.45f)) Shrapnel.HitCrew(w, R, c, 0.22f, 1f, "쏟아진 화물에 짓눌렸다", $"쏟아진 화물에 짓눌렸다 ({r.Name})");
                var door = r.Doors.Where(d => !d.IsExternal && !d.Removed).OrderBy(d => d.Id).FirstOrDefault();
                if (door != null) { door.Blocked = true; k.Doors.Add(door.Id); }
                k.Room = r.Id;
                return $"{spec.Name}({r.Name})";
            }
            case MajorKind.MainBusFail:
            {
                var f = Furn(FurnitureType.PowerPanel, prefer);
                if (f == null) return null;
                var panel = f.Machine!;
                int n = 0;
                for (int c = 0; c < PowerGrid.CircuitCount; c++)
                {
                    if (panel.Faults.Any(x => x.Circuit == c)) continue;
                    panel.Faults.Add(new Fault { Kind = c == 3 ? FaultKind.ShortCircuit : FaultKind.BreakerTrip, Since = w.Tick, Circuit = c });
                    w.Causes.OnFault(panel, panel.Faults[^1]);
                    panel.FaultCount++;
                    w.History.CircuitFaults++;
                    n++;
                }
                if (n == 0) return null;
                k.Room = f.Room.Id;
                k.Furn.Add(f.Id);
                return $"{spec.Name}({f.Room.Name})";
            }
            case MajorKind.PlateTear:
            {
                var r = Pick(x => x.Type != RoomType.Corridor && HullCells(x).Count > 0 && Essentials.Of(x) >= Essential.Core, prefer)
                    ?? Pick(x => x.Type != RoomType.Corridor && HullCells(x).Count > 0, prefer);
                if (r == null) return null;
                var hull = HullCells(r);
                var cell = hull[R.Range(0, hull.Count)];
                if (ship.WallAt(cell) is not WallState ws) return null;
                ws.Integrity = 0f;
                ws.Frame = MathF.Min(ws.Frame, 0.2f);
                ws.Patched = false;
                ws.Breach = Hull.BreachOf(ws);
                k.Cells.Add(cell);
                k.Room = r.Id;
                MarkLog.Add(ws.Marks, w.Tick, "외판이 골조에서 뜯겨 나갔다");
                return $"{spec.Name}({r.Name})";
            }
            case MajorKind.LifeSupportCascade:
            {
                var gens = ship.FurnitureOf(FurnitureType.OxygenGenerator).Where(f => !f.Room.Detached && f.Machine != null).OrderBy(f => f.Id).ToList();
                if (gens.Count == 0) return null;
                foreach (var g in gens) if (BreakM(g.Machine!, FaultKind.ElectrolyzerFault)) k.Furn.Add(g.Id);
                if (Furn(FurnitureType.WaterRecycler) is Furniture wr) { BreakM(wr.Machine!, FaultKind.PumpSeized); k.Furn.Add(wr.Id); }
                if (k.Furn.Count == 0) return null;
                k.Room = gens[0].Room.Id;
                return spec.Name;
            }
            case MajorKind.MultiZoneFire:
            {
                var r = Pick(x => x.Type != RoomType.Corridor && Neighbors(x).Any(n => n.Type != RoomType.Corridor), prefer)
                    ?? Pick(x => x.Type != RoomType.Corridor && Neighbors(x).Any(), prefer);
                if (r == null) return null;
                var n2 = Neighbors(r).FirstOrDefault(n => n.Type != RoomType.Corridor) ?? Neighbors(r).First();
                var door = r.Doors.First(d => !d.IsExternal && !d.Removed && (d.RoomA == n2 || d.RoomB == n2));
                door.JammedOpen = true;
                k.Doors.Add(door.Id);
                bool a = Ignite(k, r, door.Cell, 0.5f), b = Ignite(k, n2, door.Cell, 0.5f);
                if (!a && !b) return null;
                k.Room = r.Id;
                return $"{spec.Name}({r.Name}·{n2.Name})";
            }
            case MajorKind.AmmoniaRelease:
            {
                var r = Pick(x => x.Type == RoomType.Cooling, prefer) ?? Furn(FurnitureType.CoolantPump)?.Room;
                if (r == null || w.Hazards.Gas(r) == null) return null;
                r.Air.Toxin = MathF.Min(1.5f, r.Air.Toxin + 0.4f);
                foreach (var n in Neighbors(r).Take(2)) { n.Air.Toxin = MathF.Min(1.5f, n.Air.Toxin + 0.2f); k.Rooms.Add(n.Id); }
                k.Room = r.Id;
                return $"{spec.Name}({r.Name})";
            }
            case MajorKind.EngineOverpressure:
            {
                var f = Furn(FurnitureType.EngineCore, prefer);
                if (f == null) return null;
                f.Machine!.Heat = MathF.Max(f.Machine.Heat, 0.97f);
                f.Machine.Vapor = MathF.Max(f.Machine.Vapor, 0.4f);
                foreach (var c in HullCells(f.Room).Take(2)) { Hull.Damage(ship, c, 0.35f); k.Cells.Add(c); }
                k.Room = f.Room.Id;
                k.Furn.Add(f.Id);
                return $"{spec.Name}({f.Room.Name})";
            }
            case MajorKind.FrameResonance:
            {
                var hull = ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
                if (hull.Count == 0) return null;
                for (int i = 0; i < 3; i++)
                {
                    var c = hull[R.Range(0, hull.Count)];
                    w.Structure.OnImpact(c, 0.45f);
                    k.Cells.Add(c);
                    if (Hull.InsideRoom(ship, c) is Room ir && !k.Rooms.Contains(ir.Id)) k.Rooms.Add(ir.Id);
                }
                foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.08f);
                k.Room = k.Rooms.FirstOrDefault(-1);
                return spec.Name;
            }
        }
        return null;
    }

    private List<Room> Crowded(int n) => _w.Ship.Rooms.Where(r => !r.Detached).OrderByDescending(r => _w.Crew.Count(c => !c.Dead && c.Room == r)).ThenBy(r => r.Id).Take(n).ToList();

    // ─────────────────────────────── 번짐 ───────────────────────────────

    private static int MaxSteps(MajorKind k) => k switch
    {
        MajorKind.TrunkFire or MajorKind.HullCrackRun => 4,
        MajorKind.CoolantHeader or MajorKind.CascadeDecomp or MajorKind.O2StoreLeak or MajorKind.DataCoreFire or MajorKind.PropellantLeak or MajorKind.WaterContam
            or MajorKind.AirPlantFail or MajorKind.BatteryChain or MajorKind.SmokeSpread or MajorKind.WaterMainBurst or MajorKind.CropCollapse
            or MajorKind.MultiZoneFire or MajorKind.FrameResonance => 2,
        MajorKind.ColdChain => 1,
        _ => 0,
    };

    private static long StepEvery(MajorKind k) => k switch
    {
        MajorKind.TrunkFire or MajorKind.DataCoreFire or MajorKind.MultiZoneFire or MajorKind.BatteryChain or MajorKind.SmokeSpread or MajorKind.PropellantLeak => SimTime.Minutes(10),
        MajorKind.HullCrackRun or MajorKind.CascadeDecomp or MajorKind.O2StoreLeak or MajorKind.WaterMainBurst or MajorKind.CoolantHeader => SimTime.Minutes(15),
        MajorKind.ColdChain => SimTime.Hours(4),
        _ => SimTime.Minutes(30),
    };

    private bool FireIn(MajorCase k) => k.Rooms.Any(id => RoomById(id) is Room r && _w.Fire.CountIn(r) > 0);

    private void Spread(MajorCase k)
    {
        var w = _w;
        var ship = w.Ship;
        var room = RoomById(k.Room);
        switch (k.Kind)
        {
            case MajorKind.CoolantHeader:
                if (room != null && w.Piping.Segments.FirstOrDefault(s => k.Links.Contains(s.Id)) is PipeSegment seg && seg.Leaking) w.Moisture.AddWater(room, 15f);
                break;
            case MajorKind.TrunkFire:
            {
                if (!FireIn(k)) break;
                // 불이 간선을 따라 다음 토막으로
                var last = w.Net.Links.FirstOrDefault(l => l.Id == k.Links[^1]);
                if (last == null) break;
                var next = w.Net.Links.Where(l => l.Kind == NetKind.Power && !k.Links.Contains(l.Id) && !l.Cut && (l.NodeA == last.NodeA || l.NodeA == last.NodeB || l.NodeB == last.NodeA || l.NodeB == last.NodeB)).OrderBy(l => l.Id).FirstOrDefault();
                if (next == null) break;
                w.Net.Hurt(next, 0.5f, k.Spec.Name);
                k.Links.Add(next.Id);
                if (next.Cells.Count > 0) Ignite(k, next.Room, next.Cells[next.Cells.Count / 2], 0.3f);
                break;
            }
            case MajorKind.CascadeDecomp:
            {
                // 휜 문 너머 방이 끌려가면 그 방의 다른 문틀도 어긋난다
                var bent = k.Doors.Select(id => ship.Doors.FirstOrDefault(d => d.Id == id)).OfType<Door>().LastOrDefault();
                if (bent == null || bent.Bent <= 0.3f) break;
                var far = new[] { bent.RoomA, bent.RoomB }.OfType<Room>().FirstOrDefault(r => !k.Rooms.Contains(r.Id));
                if (far == null || far.Air.Pressure > 80f) break;
                k.Rooms.Add(far.Id);
                var d2 = far.Doors.Where(d => !d.IsExternal && !d.Removed && !k.Doors.Contains(d.Id) && d.RoomA != null && d.RoomB != null).OrderBy(d => d.Id).FirstOrDefault();
                if (d2 != null && R.Chance(0.5f)) { d2.Bent = MathF.Max(d2.Bent, 0.35f); k.Doors.Add(d2.Id); }
                break;
            }
            case MajorKind.O2StoreLeak:
                if (room != null && room.O2Leak > 0f)
                    foreach (var n in Neighbors(room).Take(2)) { n.Air.O2 = MathF.Min(30f, n.Air.O2 + 1.2f); if (!k.Rooms.Contains(n.Id)) k.Rooms.Add(n.Id); }
                break;
            case MajorKind.DataCoreFire:
            case MajorKind.SmokeSpread:
                if (room != null && w.Fire.CountIn(room) > 0) Ignite(k, room, room.Cells[R.Range(0, room.Cells.Count)], 0.25f);
                if (k.Kind == MajorKind.SmokeSpread && room != null)
                    foreach (var n in Neighbors(room).Where(n => n.VentOpen).Take(2)) { n.Air.Smoke = MathF.Min(1f, n.Air.Smoke + 0.25f); if (!k.Rooms.Contains(n.Id)) k.Rooms.Add(n.Id); }
                break;
            case MajorKind.PropellantLeak:
                if (room != null && w.Hazards.GasSource(room) != null)
                {
                    room.Air.Toxin = MathF.Min(1.5f, room.Air.Toxin + 0.15f);
                    if (room.Furniture.Any(f => f.Machine is { Active: true } || f.Machine is { Heat: > 0.6f }) && R.Chance(0.25f)) Ignite(k, room, room.Cells[0], 0.35f);
                }
                break;
            case MajorKind.WaterContam:
                if (k.Furn.Count > 0 && FurnById(k.Furn[0])?.Machine is Machine rec && rec.Has(FaultKind.MembraneFouling))
                    w.Flow.WaterQuality = MathF.Max(0f, w.Flow.WaterQuality - 0.08f);
                break;
            case MajorKind.AirPlantFail:
                if (k.Furn.Any(id => FurnById(id)?.Machine is Machine m && m.Has(FaultKind.FilterClogged)))
                    foreach (var r in Crowded(2)) { r.Air.CO2 += 0.35f; if (!k.Rooms.Contains(r.Id)) k.Rooms.Add(r.Id); }
                break;
            case MajorKind.BatteryChain:
            {
                var hot = k.Furn.Select(FurnById).OfType<Furniture>().Any(f => f.Machine!.Heat > 0.7f && !f.Machine.Parked);
                if (!hot) break;
                var next = ship.FurnitureOf(FurnitureType.Battery).Where(f => !f.Room.Detached && f.Machine != null && !k.Furn.Contains(f.Id) && !f.Machine.Parked)
                    .OrderBy(f => k.Furn.Select(FurnById).OfType<Furniture>().Min(x => (x.Center - f.Center).LengthSquared())).ThenBy(f => f.Id).FirstOrDefault();
                if (next == null) break;
                next.Machine!.Heat = MathF.Max(next.Machine.Heat, 0.82f);
                k.Furn.Add(next.Id);
                if (!k.Rooms.Contains(next.Room.Id)) k.Rooms.Add(next.Room.Id);
                break;
            }
            case MajorKind.WaterMainBurst:
                if (room != null && k.Links.Any(id => w.Net.Links.FirstOrDefault(l => l.Id == id) is { Cut: true })) w.Moisture.AddWater(room, 15f);
                break;
            case MajorKind.CropCollapse:
            {
                var beds = ship.FurnitureOf(FurnitureType.GrowBed).Where(f => !f.Room.Detached && f.Machine?.Crop is { Blight: <= 0f }).OrderBy(f => f.Id).ToList();
                if (beds.Count > 0 && k.Furn.Any(id => FurnById(id)?.Machine is Machine m && (m.Has(FaultKind.NutrientClog) || m.Crop is { Blight: > 0f })))
                    w.Hazards.Blight(beds[0]);
                break;
            }
            case MajorKind.ColdChain:
                // 네 시간 안에 못 고쳤으면 식사가 상한다
                foreach (var f in k.Furn.Select(FurnById).OfType<Furniture>())
                    if (f.Machine!.Has(FaultKind.CompressorFail) && f.Storage is Inventory inv)
                    {
                        int meals = inv.Count(ItemKind.Meal) - inv.Tainted;
                        if (meals > 0) { inv.Taint(Math.Min(meals, 4)); MarkLog.Add(f.Machine.Marks, w.Tick, "냉기가 빠져 식사가 상했다"); }
                    }
                break;
            case MajorKind.HullCrackRun:
            {
                // 균열 끝이 용접 · 봉합됐으면 멈춘다 · 아니면 옆 외판으로 달려간다
                var tip = k.Cells[^1];
                if (ship.WallAt(tip) is not WallState tw || tw.Patched || tw.Integrity > 0.5f) break;
                var next = Cell.Dirs4.Select(d => tip + d).Where(c => ship.WallAt(c) is { IsHull: true } && !k.Cells.Contains(c)).OrderBy(c => c.Y).ThenBy(c => c.X).FirstOrDefault();
                if (next == default || ship.WallAt(next) is not WallState nw) break;
                nw.Integrity = MathF.Min(nw.Integrity, 0.3f - 0.04f * k.Steps);
                if (!nw.Patched) nw.Breach = Hull.BreachOf(nw);
                MarkLog.Add(nw.Marks, w.Tick, "옆 판에서 균열이 넘어왔다");
                k.Cells.Add(next);
                if (Hull.InsideRoom(ship, next) is Room ir && !k.Rooms.Contains(ir.Id)) k.Rooms.Add(ir.Id);
                break;
            }
            case MajorKind.MultiZoneFire:
                if (FireIn(k) && room != null)
                {
                    var third = k.Rooms.Select(RoomById).OfType<Room>().SelectMany(Neighbors).FirstOrDefault(n => !k.Rooms.Contains(n.Id) && n.Type != RoomType.Corridor);
                    if (third != null && R.Chance(0.5f)) Ignite(k, third, third.Cells[third.Cells.Count / 2], 0.3f);
                }
                break;
            case MajorKind.FrameResonance:
            {
                var hull = ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
                if (hull.Count == 0) break;
                var c = hull[R.Range(0, hull.Count)];
                w.Structure.OnImpact(c, 0.3f);
                k.Cells.Add(c);
                break;
            }
        }
    }

    // ─────────────────────────────── 수습 ───────────────────────────────

    private bool Settled(MajorCase k)
    {
        var w = _w;
        var ship = w.Ship;
        var room = RoomById(k.Room);
        bool Fixed(FaultKind f) => k.Furn.Select(FurnById).OfType<Furniture>().All(x => x.Room.Detached || !x.Machine!.Has(f));
        bool NoLeakCells() => k.Cells.All(c => ship.WallAt(c) is not WallState ws || ws.Patched || Hull.EffectiveBreach(ws) <= 0f || Hull.InsideRoom(ship, c) is not Room r || r.Abandoned || r.Detached);
        switch (k.Kind)
        {
            case MajorKind.CoolantHeader: return w.Piping.Segments.Where(s => k.Links.Contains(s.Id)).All(s => !s.Leaking);
            case MajorKind.TrunkFire: return !FireIn(k) && k.Links.All(id => w.Net.Links.FirstOrDefault(l => l.Id == id) is not { Cut: true });
            case MajorKind.CascadeDecomp: return NoLeakCells() && k.Rooms.Select(RoomById).OfType<Room>().All(r => r.Abandoned || r.Detached || r.Air.Pressure > 75f);
            case MajorKind.O2StoreLeak: return room == null || room.O2Leak <= 0f && !FireIn(k);
            case MajorKind.DataCoreFire: return !FireIn(k);
            case MajorKind.PropellantLeak: return room == null || w.Hazards.GasSource(room) == null && room.Air.Toxin < 0.15f && !FireIn(k);
            case MajorKind.WaterContam: return Fixed(FaultKind.MembraneFouling) && w.Flow.WaterQuality > 0.6f;
            case MajorKind.SwitchboardFire: return !FireIn(k) && (k.Furn.Count == 0 || FurnById(k.Furn[0])?.Machine is not Machine p || p.Faults.All(f => f.Kind != FaultKind.BreakerTrip));
            case MajorKind.AirPlantFail: return Fixed(FaultKind.FilterClogged);
            case MajorKind.BatteryChain: return k.Furn.Select(FurnById).OfType<Furniture>().All(f => f.Machine!.Heat < 0.6f || f.Machine.Parked) && !FireIn(k);
            case MajorKind.SmokeSpread: return !FireIn(k) && k.Rooms.Select(RoomById).OfType<Room>().All(r => r.Air.Smoke < 0.15f);
            case MajorKind.WaterMainBurst: return k.Links.All(id => w.Net.Links.FirstOrDefault(l => l.Id == id) is not { Cut: true });
            case MajorKind.CropCollapse: return Fixed(FaultKind.NutrientClog) && k.Furn.Select(FurnById).OfType<Furniture>().All(f => f.Machine?.Crop is not { Blight: > 0f });
            case MajorKind.ColdChain: return Fixed(FaultKind.CompressorFail);
            case MajorKind.ReactorCoolingLoss: return Fixed(FaultKind.PumpSeized) && w.Power.ReactorOnline;
            case MajorKind.HullCrackRun: return k.Steps >= 1 && NoLeakCells();
            case MajorKind.CargoBreakaway: return k.Doors.Select(id => ship.Doors.FirstOrDefault(d => d.Id == id)).OfType<Door>().All(d => !d.Blocked);
            case MajorKind.MainBusFail: return k.Furn.Count == 0 || FurnById(k.Furn[0])?.Machine is not Machine pn || pn.Faults.Count(f => f.Kind is FaultKind.BreakerTrip or FaultKind.ShortCircuit) == 0;
            case MajorKind.PlateTear: return NoLeakCells();
            case MajorKind.LifeSupportCascade: return k.Furn.Select(FurnById).OfType<Furniture>().Any(f => f.Type == FurnitureType.OxygenGenerator && !f.Machine!.Has(FaultKind.ElectrolyzerFault));
            case MajorKind.MultiZoneFire: return !FireIn(k);
            case MajorKind.AmmoniaRelease: return (room == null || w.Hazards.GasSource(room) == null) && k.Rooms.Select(RoomById).OfType<Room>().All(r => r.Air.Toxin < 0.15f);
            case MajorKind.EngineOverpressure: return !FireIn(k) && k.Furn.Select(FurnById).OfType<Furniture>().All(f => f.Machine!.Heat < 0.7f) && NoLeakCells();
            case MajorKind.FrameResonance: return k.Steps >= MaxSteps(k.Kind) && w.Tick - k.Onset > SimTime.Hours(1);
        }
        return true;
    }

    /// <summary>수습: 흔적을 남기고 · 겪은 사람이 기억하고 · 함께 막은 사람끼리 가까워진다 · 컴퓨터가 정리한다.</summary>
    private void Close(MajorCase k, bool settled)
    {
        var w = _w;
        var spec = k.Spec;
        k.Phase = MajorPhase.Contained;
        k.End = w.Tick;
        Contained++;
        var room = RoomById(k.Room);
        float hours = (w.Tick - k.Onset) / (float)SimTime.TicksPerHour;
        var resp = k.Responders.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)).OfType<CrewMember>().Where(c => !c.Dead).ToList();
        string who = resp.Count > 0 ? $" — {string.Join("·", resp.Take(4).Select(c => c.Name))}" : "";
        string text = settled ? $"{spec.Name} 수습 ({hours:0.#}시간){who}" : $"{spec.Name} — 끝내 다 수습하지 못한 채 가라앉았다 ({hours:0.#}시간)";
        w.History.Add(w, HistoryKind.Response, text, room, resp);
        foreach (var id in k.Rooms)
            if (RoomById(id) is Room r) MarkLog.Add(r.Marks, w.Tick, $"{spec.Name}: {spec.Trace}");
        var at = k.Cells.Count > 0 ? k.Cells[0] : room?.Cells.FirstOrDefault() ?? default;
        if (at != default) Traces.Add(new MajorTrace(k.Kind, at, w.Tick, k.Room));
        foreach (var c in resp)
        {
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{Ko.EulReul(spec.Name)} 함께 막았다 — {spec.Memory}");
            foreach (var o in resp) if (o != c) c.ChangeAffinity(o, 0.04f);
        }
        foreach (var id in k.Witnesses)
            if (w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c2 && !c2.Dead && !resp.Contains(c2)) MarkLog.Add(c2.Memory.Marks, w.Tick, $"{spec.Name} — {spec.Memory}");
        w.Automation.Reason("major:end:" + k.Id, $"{text} · 흔적: {spec.Trace}", SimTime.Hours(1));
        w.Log.Add(w.Tick, LogKind.Ship, text);
    }

    /// <summary>이야기꾼 · 무작위 사고의 고르는 표에 넣는다 (가혹이 아니면 여력이 없을 때 큰 것은 줄인다).</summary>
    public static void AddToPool(World w, List<(string key, float weight)> pool, bool gentle, float big)
    {
        foreach (var s in All)
        {
            float wt = s.Weight * (s.Scale >= IncidentScale.Ship ? (gentle ? 0.25f : big) : gentle ? 0.6f : 1f);
            if (w.Major.Cases.Any(c => c.Open && c.Kind == s.Kind)) continue;
            pool.Add(("major:" + s.Key, wt));
        }
    }

    /// <summary>
    /// 규모 비율로 고른다: 고르는 표의 무게를 규모별로 모아 목표 비율에 맞춘다 (개인 6% · 방 34% · 계통 38% · 배 전체 22%) —
    /// 자잘한 사고가 무게 합으로 판을 덮지 않게. 규모는 표(ScaleTable)의 기본 규모.
    /// </summary>
    public static void ShapeByScale(List<(string key, float weight)> pool)
    {
        if (Durability.Legacy || pool.Count == 0) return;
        float[] target = { 0.06f, 0.34f, 0.38f, 0.22f, 0f };
        var sum = new float[5];
        var scale = new IncidentScale[pool.Count];
        for (int i = 0; i < pool.Count; i++) { scale[i] = ScaleTable.OfKey(pool[i].key); sum[(int)scale[i]] += pool[i].weight; }
        float total = sum.Sum();
        if (total <= 0f) return;
        float have = 0f;
        for (int s = 0; s < 5; s++) if (sum[s] > 0f) have += target[s];
        if (have <= 0f) return;
        for (int i = 0; i < pool.Count; i++)
        {
            int s = (int)scale[i];
            if (sum[s] <= 0f) continue;
            pool[i] = (pool[i].key, pool[i].weight / sum[s] * target[s] / have * total);
        }
    }

    /// <summary>지문.</summary>
    public void Hash(Action<long> I)
    {
        I(Cases.Count); I(Averted); I(Contained); I(OmensRead); I(Traces.Count);
        foreach (var k in Cases) { I((int)k.Kind); I((int)k.Phase); I(k.Steps); I(k.Rooms.Count); I(k.Onset % 1000003); }
    }
}
