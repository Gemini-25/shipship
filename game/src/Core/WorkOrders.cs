using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

public enum WorkKind
{
    Repair, ResetBreaker, Maintain, Harvest, Tend, Cook, Restock,
    SealBreach, RepairHull, OperateDamper, Extinguish, Treat, RestartReactor, StartAux,
    Fabricate, Rescue, SealOffRoom, ReopenRoom,
    // v5 적응: 우회와 땜질
    ManualStart, Refuel, InstallJumper, ShedLoad, RestoreCircuit, Cannibalize, RepurposeRoom,
    CrankDoor,
    // v6 재료와 순환
    MeltIce, InstallSubstitute, RestoreGrade, ReplacePanel, BuildWorkshop,
    // v7 진화
    Upgrade,
    // v8 외부 작업과 구조
    InspectHull, RepairJoint, RebuildFrame, InstallTruss, Clamp, ReleaseJoint,
    ServiceDrone, StockDock,
    Jettison, Salvage, IsolatePower, IsolatePipes, IsolateVent, WeldBulkhead, Eject,
    Retrieve, RestoreRoom, Reconnect,
    // v9 배관·냉각
    CloseValve, OpenValve, PatchPipe, ReplacePipe, LayBypass, RefillCoolant, RepairRadiator, IsolateMain, LimpMain,
    // v9.2 자동화
    PilotDrones, BuildComputer, PlanRepipe,
    // v9.3 저출력 운영
    Brownout, EndBrownout, UnstockDock,
    // v9.4 문·조명
    RepairDoor, FixLights,
    // v10.1 통신실
    RadarWatch,
    // v10.10 선내 로봇 · 자원 회복
    RepairRobot, FetchRobot, ServiceRobot, CarryWater, StockCache, RemoveJumper, StowCot, Recycle,
    // v11.0 예방과 안전
    PreventiveCheck, SuitCheck, Drill,
    // v11.2 항로와 추진 (엔진실)
    RefillPropellant, ChangeCourse,
    // v11.2 사고 종류
    DiscardFood,
    // v10.11 대인원 생활
    Ration, EndRation,
    // v11.1 분산 운영: 산소 발생기를 모두 잃었을 때
    BuildOxygen,
    // v11.2 외부 교신
    Distress, UnloadSupply, AnswerSignal,
    // v11.3 승무원 성장
    Train, Rehab,
    // v12.7 승무원: 시신 수습 · 의수·의족
    RecoverBody, FitProsthetic,
    // v12.0 당직 일지·진단
    Calibrate, Handover,
    // v12.2 설비 열·폭발·잔해·공기
    CoolDown, ClearRubble, CleanUp, BleedRoom, SealO2Line,
    // 승무원 AI: 위기에 잠든 동료 깨우기
    WakeCrew,
    // v12.1 설비 전선·관
    Rewire, Reline,
    // 배 전체 망
    RepairNet,
    // v12.3 침수
    IsolateRoom, BreakerOn, ShutRoomValve, OpenRoomValve, PumpOut,
    // v12.5 관제석 수동 조종
    ManualControl,
    // v13.1 2인 1조: 문 밖에서 지킨다
    SafetyWatch,
}

public static class WorkKinds
{
    public static string Name(WorkKind k) => k switch
    {
        WorkKind.Repair => "수리",
        WorkKind.ResetBreaker => "차단기 복구",
        WorkKind.Maintain => "정비",
        WorkKind.Harvest => "수확",
        WorkKind.Tend => "작물 돌보기",
        WorkKind.Cook => "조리",
        WorkKind.Restock => "배식기 채우기",
        WorkKind.SealBreach => "파공 임시 봉합",
        WorkKind.RepairHull => "외벽 수리",
        WorkKind.OperateDamper => "댐퍼 수동 조작",
        WorkKind.Extinguish => "화재 진압",
        WorkKind.Treat => "부상 치료",
        WorkKind.RestartReactor => "원자로 재기동",
        WorkKind.StartAux => "보조 발전기 기동",
        WorkKind.Fabricate => "소모품 제작",
        WorkKind.Rescue => "구조",
        WorkKind.SealOffRoom => "구획 폐쇄",
        WorkKind.ReopenRoom => "구획 재개방",
        WorkKind.ManualStart => "원자로 수동 기동",
        WorkKind.Refuel => "보조 발전기 급유",
        WorkKind.InstallJumper => "임시 배선",
        WorkKind.ShedLoad => "절전",
        WorkKind.RestoreCircuit => "회로 복귀",
        WorkKind.Cannibalize => "부품 뜯어 쓰기",
        WorkKind.RepurposeRoom => "방 용도 변경",
        WorkKind.CrankDoor => "격벽 수동 폐쇄",
        WorkKind.MeltIce => "얼음 처리",
        WorkKind.InstallSubstitute => "Mk.1 대체품",
        WorkKind.RestoreGrade => "정품 복원",
        WorkKind.ReplacePanel => "외벽 패널 교체",
        WorkKind.BuildWorkshop => "임시 정비실",
        WorkKind.Upgrade => "개조",
        WorkKind.InspectHull => "외부 검사",
        WorkKind.RepairJoint => "연결부 보강",
        WorkKind.RebuildFrame => "골조 재건",
        WorkKind.InstallTruss => "임시 트러스",
        WorkKind.Clamp => "임시 도킹 고정",
        WorkKind.ReleaseJoint => "연결 해제",
        WorkKind.ServiceDrone => "드론 정비",
        WorkKind.StockDock => "드론 자재 보급",
        WorkKind.PilotDrones => "드론 손으로 조종",
        WorkKind.RadarWatch => "레이더 감시",
        WorkKind.BuildComputer => "임시 제어 컴퓨터",
        WorkKind.PlanRepipe => "계획 교체 (원자로 정지)",
        WorkKind.Brownout => "저출력 운영",
        WorkKind.EndBrownout => "정상 운전으로",
        WorkKind.UnstockDock => "거치대 자재 되가져오기",
        WorkKind.RepairDoor => "문 구동기 수리",
        WorkKind.FixLights => "조명 수리",
        WorkKind.Jettison => "구획 사출",
        WorkKind.Salvage => "물품 회수",
        WorkKind.IsolatePower => "전력 차단",
        WorkKind.IsolatePipes => "배관 차단",
        WorkKind.IsolateVent => "환기 차단",
        WorkKind.WeldBulkhead => "격벽 용접",
        WorkKind.Eject => "사출",
        WorkKind.Retrieve => "되찾기",
        WorkKind.RestoreRoom => "되살리기",
        WorkKind.Reconnect => "재연결",
        WorkKind.CloseValve => "밸브 잠그기",
        WorkKind.OpenValve => "밸브 열기",
        WorkKind.PatchPipe => "배관 임시 밀봉",
        WorkKind.ReplacePipe => "배관 교체",
        WorkKind.LayBypass => "우회 배관",
        WorkKind.RefillCoolant => "냉각수 보충",
        WorkKind.RepairRadiator => "방열판 수리",
        WorkKind.IsolateMain => "냉각 본관 차단",
        WorkKind.LimpMain => "새는 채로 다시 열기",
        WorkKind.RepairRobot => "로봇 수리",
        WorkKind.FetchRobot => "멈춘 로봇 끌어오기",
        WorkKind.ServiceRobot => "로봇 정비",
        WorkKind.CarryWater => "물통 나르기",
        WorkKind.StockCache => "비상 물자 채우기",
        WorkKind.RemoveJumper => "임시 배선 철거",
        WorkKind.StowCot => "간이침대 치우기",
        WorkKind.Recycle => "부품 재활용",
        WorkKind.PreventiveCheck => "예방 점검",
        WorkKind.SuitCheck => "우주복 점검",
        WorkKind.Drill => "비상 훈련",
        WorkKind.RefillPropellant => "추진제 보충",
        WorkKind.ChangeCourse => "항로 변경",
        WorkKind.DiscardFood => "상한 식사 버리기",
        WorkKind.Ration => "배급",
        WorkKind.EndRation => "배급 해제",
        WorkKind.BuildOxygen => "임시 산소 발생기",
        WorkKind.Distress => "조난 신호",
        WorkKind.UnloadSupply => "보급 캡슐 내리기",
        WorkKind.AnswerSignal => "탈출 캡슐 구조",
        WorkKind.Train => "배우기",
        WorkKind.Rehab => "재활",
        WorkKind.RecoverBody => "시신 수습",
        WorkKind.FitProsthetic => "의수·의족",
        WorkKind.Calibrate => "감지기 교정",
        WorkKind.Handover => "인수인계",
        WorkKind.CoolDown => "과열 설비 식히기",
        WorkKind.ClearRubble => "잔해 치우기",
        WorkKind.CleanUp => "분말·그을음 청소",
        WorkKind.BleedRoom => "역화 막기",
        WorkKind.SealO2Line => "산소관 막기",
        WorkKind.WakeCrew => "동료 깨우기",
        WorkKind.Rewire => "설비 전선",
        WorkKind.RepairNet => "망 잇기",
        WorkKind.IsolateRoom => "분전함 내리기",
        WorkKind.BreakerOn => "분전함 올리기",
        WorkKind.ShutRoomValve => "급수 밸브 잠그기",
        WorkKind.OpenRoomValve => "급수 밸브 열기",
        WorkKind.PumpOut => "물 퍼내기",
        WorkKind.ManualControl => "수동 조종",
        WorkKind.SafetyWatch => "안전 감시",
        WorkKind.Reline => "설비 관 이음",
        _ => k.ToString(),
    };

    /// <summary>선체 밖에서 하는 일 (드론이 하거나, 드론이 없으면 우주복 EVA).</summary>
    public static bool IsExternal(WorkKind k) =>
        k is WorkKind.InspectHull or WorkKind.RepairJoint or WorkKind.RebuildFrame or WorkKind.InstallTruss or WorkKind.Clamp or WorkKind.ReleaseJoint
            or WorkKind.RepairRadiator;

    /// <summary>사고 대응 작업인지 (기록·화면에서 강조).</summary>
    public static bool IsEmergency(WorkKind k) =>
        k is WorkKind.SealBreach or WorkKind.OperateDamper or WorkKind.Extinguish or WorkKind.RestartReactor or WorkKind.StartAux
            or WorkKind.Rescue or WorkKind.SealOffRoom or WorkKind.ManualStart or WorkKind.ShedLoad or WorkKind.CrankDoor
            or WorkKind.IsolatePower or WorkKind.IsolatePipes or WorkKind.IsolateVent or WorkKind.WeldBulkhead or WorkKind.Eject
            or WorkKind.CloseValve or WorkKind.PatchPipe or WorkKind.PilotDrones or WorkKind.IsolateRoom;

    /// <summary>원래 설계에 없던 방식으로 버티는 일 (적응).</summary>
    public static bool IsAdaptation(WorkKind k) =>
        k is WorkKind.ManualStart or WorkKind.Refuel or WorkKind.InstallJumper or WorkKind.ShedLoad or WorkKind.RestoreCircuit
            or WorkKind.Cannibalize or WorkKind.RepurposeRoom or WorkKind.InstallSubstitute or WorkKind.BuildWorkshop
            or WorkKind.Jettison or WorkKind.Salvage or WorkKind.InstallTruss or WorkKind.Clamp or WorkKind.RestoreRoom
            or WorkKind.LayBypass or WorkKind.IsolateMain or WorkKind.LimpMain or WorkKind.PilotDrones or WorkKind.BuildComputer
            or WorkKind.Brownout or WorkKind.RadarWatch or WorkKind.Ration or WorkKind.BuildOxygen;
}

/// <summary>
/// 우주선이 발견한 "해야 할 일" 하나. 누구에게 시키는 게 아니라 목록에 올려둘 뿐이고,
/// 승무원이 각자 자기 판단으로 골라 맡는다 (맡으면 다른 사람은 안 건드림).
/// </summary>
public sealed class WorkOrder
{
    public int Id { get; init; }
    public WorkKind Kind { get; init; }
    /// <summary>작업 대상. 불처럼 움직이는 대상은 목록을 훑을 때마다 갱신된다.</summary>
    public WorkTarget Target { get; internal set; } = null!;
    public FaultKind? Fault { get; init; }
    public int Circuit { get; init; } = -1;

    /// <summary>제작 작업이면 만들 물건.</summary>
    public ItemKind? Product { get; init; }
    public Skill Skill { get; init; }

    /// <summary>이 기술이 이만큼은 돼야 손댈 수 있다 (0이면 누구나).</summary>
    public float MinSkill { get; set; }
    public long Posted { get; init; }

    /// <summary>긴급도 0~1+. 상황에 따라 계속 갱신된다.</summary>
    public float Urgency { get; set; }

    public string Detail { get; set; } = "";
    public CrewMember? Assignee { get; set; }

    public long BlockedUntil { get; set; }
    public string? BlockedReason { get; set; }

    public bool Closed { get; set; }

    /// <summary>긴 작업의 진척 0~1 (끊겨도 남는다).</summary>
    public float Progress { get; set; }

    // ── v7 결정 주체: 구획 포기·절전·뜯기·용도 변경·개조는 누군가 정해야 손댈 수 있다 ──
    public DecisionState Decision { get; set; }
    public long DecideAt { get; set; }
    public CrewMember? Decider { get; set; }

    /// <summary>급해서 지휘자 혼자 정하는지 (아니면 회의).</summary>
    public bool Alone { get; set; }
    /// <summary>v13.2 위기 중 현장 협의 (지휘자·조장들이 무전으로).</summary>
    public bool Field { get; set; }
    public int Rejections { get; set; }

    /// <summary>결정 한 줄 (누가 정했고 누가 반대했나).</summary>
    public string? Verdict { get; set; }

    /// <summary>개조 작업이면 그 종류.</summary>
    public UpgradeKind? Upgrade { get; init; }

    /// <summary>설비 대상이면 그 설비.</summary>
    public Furniture? Furniture => Target.Furniture;

    /// <summary>이 일을 맡은 드론 (v8). 드론이 맡은 일은 승무원 목록에서 빠진다.</summary>
    public Drone? Drone { get; set; }

    /// <summary>v10.10: 이 일을 맡은 선내 로봇. 로봇이 하는 일은 사람이 거들 수만 있다 (정비·재배는 합류해 함께 채운다).</summary>
    public Robot? Robot { get; set; }

    /// <summary>마지막으로 목록을 훑을 때 조건이 여전했던 틱 (드론이 일을 계속할지 판단).</summary>
    public long LastSeen { get; set; }

    /// <summary>선체 밖에서 하는 일인지.</summary>
    public bool External => WorkKinds.IsExternal(Kind);

    /// <summary>재연결 단계 (전력 → 배관 → 환기).</summary>
    internal string ReconnectStep => Target.Room is Room rr ? (rr.PowerCut ? "전력" : rr.PipesCut && rr.HasPipes ? "배관" : "환기") : "";

    public string Title => Kind switch
    {
        WorkKind.Repair => $"{Target.Label} 수리",
        WorkKind.ResetBreaker => $"{PowerGrid.CircuitName(Circuit)} 회로 차단기 복구",
        WorkKind.Maintain => $"{Target.Label} 정비",
        WorkKind.Harvest => $"{Target.Label} 수확",
        WorkKind.Tend => $"{Target.Label} 돌보기",
        WorkKind.Cook => "식사 조리",
        WorkKind.Restock => $"{Target.Label} 채우기",
        WorkKind.SealBreach => $"{Target.Label} 파공 봉합",
        WorkKind.RepairHull => $"{Target.Label} 수리",
        WorkKind.OperateDamper => $"{Target.Label} 환기 댐퍼 {(Target.Room!.VentOpen ? "폐쇄" : "개방")}",
        WorkKind.Extinguish => $"{Target.Label} 화재 진압",
        WorkKind.Treat => $"{Target.Label} 치료",
        WorkKind.RestartReactor => "원자로 재기동",
        WorkKind.StartAux => "보조 발전기 기동",
        WorkKind.Fabricate => Target.Furniture?.Type == FurnitureType.Refinery
            ? $"{ItemKinds.Name(Product ?? ItemKind.Plate)} 정제" : $"{ItemKinds.Name(Product ?? ItemKind.Filter)} 만들기",
        WorkKind.Rescue => $"{Target.Label} 구조",
        WorkKind.SealOffRoom => $"{Target.Label} 포기 · 격벽 용접",
        WorkKind.ReopenRoom => $"{Target.Label} 다시 열기",
        WorkKind.ManualStart => "원자로 저출력 수동 기동",
        WorkKind.Refuel => "보조 발전기 급유",
        WorkKind.InstallJumper => $"{PowerGrid.CircuitName(Circuit)} 회로 임시 배선",
        WorkKind.ShedLoad => $"{PowerGrid.CircuitName(Circuit)} 회로 절전 차단",
        WorkKind.RestoreCircuit => $"{PowerGrid.CircuitName(Circuit)} 회로 다시 켜기",
        WorkKind.Cannibalize => $"{Target.Label}에서 {ItemKinds.Name(Product ?? ItemKind.Motor)} 뜯기",
        WorkKind.RepurposeRoom => $"{Ko.EulReul(Target.Label)} 임시 침실로",
        WorkKind.CrankDoor => $"{Target.Room?.Name ?? "?"} 격벽 손으로 닫기",
        WorkKind.MeltIce => "얼음 녹여 공기 탱크 채우기",
        WorkKind.InstallSubstitute => $"{Target.Label}에 Mk.1 임시품 달기",
        WorkKind.RestoreGrade => $"{Ko.EulReul(Target.Label)} 정품으로 되돌리기",
        WorkKind.ReplacePanel => $"{Target.Label} 패널 교체",
        WorkKind.BuildWorkshop => $"{Target.Label}에 임시 작업대",
        WorkKind.Upgrade => Evolution.Title(this),
        WorkKind.InspectHull => $"{Target.Room!.Name} 외부 검사 (연결부)",
        WorkKind.RepairJoint => $"{Target.Label} 보강",
        WorkKind.RebuildFrame => $"{Target.Room?.Name ?? "?"} 외벽 골조 재건",
        WorkKind.InstallTruss => $"{Target.Room?.Name ?? "?"}에 임시 트러스",
        WorkKind.Clamp => $"{Target.Label} 임시 고정 (도킹)",
        WorkKind.ReleaseJoint => $"{Target.Label} 풀기 (사출 준비)",
        WorkKind.ServiceDrone => $"{Target.Label} {(Target.Drone?.Wrecked == true ? "재조립" : Target.Drone?.Faulty == true ? "수리" : "정비")}",
        WorkKind.StockDock => $"{Target.Label}에 {ItemKinds.Name(Product ?? ItemKind.Structure)} 보급",
        WorkKind.PilotDrones => $"{Target.Label}에서 드론을 손으로 몰기",
        WorkKind.BuildComputer => $"{Target.Label}에 임시 제어 컴퓨터",
        WorkKind.RadarWatch => $"{Target.Label}에서 레이더 화면을 눈으로 본다",
        WorkKind.PlanRepipe => $"원자로를 세우고 {Ko.EulReul(Target.Pipe!.Name)} 새 관으로",
        WorkKind.Brownout => "저출력 운영 — 설비를 골라 내리고 먹을 것에 전기를 돌린다",
        WorkKind.EndBrownout => "저출력 운영을 풀고 내린 설비를 다시 올린다",
        WorkKind.UnstockDock => $"{Target.Label}에서 {ItemKinds.Name(Product ?? ItemKind.Plate)} 도로 꺼내 오기",
        WorkKind.RepairDoor => $"{Target.Door!.RoomA?.Name ?? "?"}·{Target.Door!.RoomB?.Name ?? "?"} 사이 문 구동기 수리",
        WorkKind.FixLights => $"{Target.Room!.Name} 조명 수리",
        WorkKind.Jettison => $"{Target.Room!.Name} 사출",
        WorkKind.Salvage => $"{Target.Room!.Name}에서 물품 회수",
        WorkKind.IsolatePower => $"{Target.Room!.Name} 전력 차단",
        WorkKind.IsolatePipes => $"{Target.Room!.Name} 배관 차단",
        WorkKind.IsolateVent => $"{Target.Room!.Name} 환기관 차단",
        WorkKind.WeldBulkhead => $"{Target.Door?.RoomA?.Name ?? "?"}·{Target.Door?.RoomB?.Name ?? "?"} 격벽 용접",
        WorkKind.Eject => $"{Target.Room!.Name} 사출 스위치",
        WorkKind.Retrieve => $"떨어져 나간 {Ko.EulReul(Target.Room!.Name)} 되찾기 (견인)",
        WorkKind.RestoreRoom => $"다시 붙인 {Ko.EulReul(Target.Room!.Name)} 되살리기",
        WorkKind.Reconnect => $"{Target.Room!.Name} {ReconnectStep} 재연결",
        WorkKind.CloseValve => $"{Target.Pipe!.Name} 밸브 잠그기",
        WorkKind.OpenValve => $"{Target.Pipe!.Name} 밸브 다시 열기",
        WorkKind.PatchPipe => $"{Target.Pipe!.Name} 임시 밀봉",
        WorkKind.ReplacePipe => $"{Target.Pipe!.Name} 교체",
        WorkKind.LayBypass => $"{Target.Pipe!.Name} 우회 배관",
        WorkKind.RefillCoolant => "냉각수 보충",
        WorkKind.RepairRadiator => $"{Target.Label} 수리",
        WorkKind.IsolateMain => $"{Target.Pipe!.Name} 잠그기 (원자로 정지)",
        WorkKind.LimpMain => $"{Ko.EulReul(Target.Pipe!.Name)} 새는 채로 다시 열기",
        WorkKind.RepairRobot => $"{Target.Label} 수리",
        WorkKind.FetchRobot => $"멈춘 {Ko.EulReul(Target.Label)} 충전대로 끌어오기",
        WorkKind.ServiceRobot => $"{Target.Label} 정비",
        WorkKind.CarryWater => $"{Target.Label}에 물통으로 물 나르기",
        WorkKind.StockCache => $"{Target.Label}에 {ItemKinds.Name(Product ?? ItemKind.Sealant)} 채우기 (비상 물자)",
        WorkKind.RemoveJumper => $"{PowerGrid.CircuitName(Circuit)} 회로 임시 배선 걷기",
        WorkKind.StowCot => $"{Target.Label} 접어 창고로",
        WorkKind.Recycle => $"{Target.Label}에서 {ItemKinds.Name(Product ?? ItemKind.Plate)} 되살리기 (재활용)",
        WorkKind.PreventiveCheck => Target.Kind == TargetKind.Room ? $"{Target.Label} 순찰 점검" : $"{Target.Label} 전조 손보기",
        WorkKind.SuitCheck => $"{Target.Label} 우주복 점검",
        WorkKind.Drill => $"{Target.Label} 비상 훈련",
        WorkKind.RefillPropellant => "엔진 추진제 보충 (물)",
        WorkKind.ChangeCourse => $"{PropulsionSystem.ZoneName((ZoneKind)Circuit)}로 항로 변경",
        WorkKind.DiscardFood => $"{Target.Label}의 균이 든 식사 버리기",
        WorkKind.Ration => "배급 — 한 끼씩 줄여 먹는다",
        WorkKind.EndRation => "배급을 풀고 제대로 먹는다",
        WorkKind.BuildOxygen => $"{Target.Label}에 임시 산소 발생기",
        WorkKind.Distress => "통신실에서 조난 신호",
        WorkKind.UnloadSupply => "보급 캡슐 짐 내리기",
        WorkKind.AnswerSignal => "탈출 캡슐 구조 — 배를 돌린다",
        WorkKind.Train => $"{Target.Crew?.Name ?? "?"}에게 {Skills.Name((Skill)(Circuit % 10))} 배우기",
        WorkKind.Rehab => $"{Target.Crew?.Name ?? "?"} 재활 운동",
        WorkKind.RecoverBody => $"{Target.Crew?.Name ?? "?"} 모시기",
        WorkKind.FitProsthetic => $"{Target.Crew?.Name ?? "?"} 의수·의족",
        WorkKind.Calibrate => $"{Target.Label} 감지기 교정",
        WorkKind.Handover => $"{Target.Crew?.Name ?? "?"}에게 인수인계",
        WorkKind.CoolDown => $"{Target.Label} 식히기",
        WorkKind.ClearRubble => $"{Target.CurrentRoom?.Name ?? "?"} 잔해 치우기",
        WorkKind.CleanUp => $"{Target.Label} 분말·그을음 닦기",
        WorkKind.BleedRoom => $"{Target.Label} 역화 막기",
        WorkKind.SealO2Line => $"{Target.Label} 산소관 막기",
        WorkKind.WakeCrew => $"{Target.Crew?.Name ?? "?"} 깨우기",
        WorkKind.Rewire => $"{Target.Label} 전선",
        WorkKind.RepairNet => $"{Target.CurrentRoom?.Name ?? "?"} 간선 잇기",
        WorkKind.IsolateRoom or WorkKind.BreakerOn or WorkKind.ShutRoomValve or WorkKind.OpenRoomValve or WorkKind.PumpOut => $"{Target.CurrentRoom?.Name ?? "?"} {WorkKinds.Name(Kind)}",
        WorkKind.ManualControl => "관제석 수동 조종",
        WorkKind.SafetyWatch => $"{Target.CurrentRoom?.Name ?? "?"} 문 밖 안전 감시",
        WorkKind.Reline => $"{Target.Label} 관 이음",
        _ => Kind.ToString(),
    };

    internal string Key => $"{Kind}:{Target.Key}:{Fault}:{Circuit}:{Product}:{Upgrade}";
}

/// <summary>작업 목록. 주기적으로 우주선을 훑어서 할 일을 올리고, 끝난 일은 내린다.</summary>
public sealed partial class WorkBoard
{
    private readonly World _world;
    private readonly Dictionary<string, WorkOrder> _open = new();
    private int _nextId = 1;
    private bool _scanRequested = true;

    public WorkBoard(World world) => _world = world;
    internal World World => _world;

    public IEnumerable<WorkOrder> Open => _open.Values.Where(o => !o.Closed).OrderByDescending(o => o.Urgency);
    public int OpenCount => _open.Count;

    public void RequestScan() => _scanRequested = true;

    public IEnumerable<WorkOrder> AvailableTo(CrewMember c) =>
        _open.Values.Where(o => !o.Closed && (o.Assignee == null || o.Assignee == c) && o.Drone == null && (o.Robot == null || RobotSystem.Joinable(o))
                                && o.BlockedUntil <= _world.Tick && Council.Cleared(o));

    /// <summary>드론이 맡을 수 있는 열린 일 (v8).</summary>
    internal IEnumerable<WorkOrder> OpenFor(Drone d) =>
        _open.Values.Where(o => !o.Closed && o.Assignee == null && o.Drone == null && o.Robot == null && o.BlockedUntil <= _world.Tick && Council.Cleared(o));

    /// <summary>v10.10: 선내 로봇이 맡을 수 있는 열린 일 (사람이 맡지 않은 것).</summary>
    internal IEnumerable<WorkOrder> OpenForRobot() =>
        _open.Values.Where(o => !o.Closed && o.Assignee == null && o.Drone == null && o.Robot == null && o.BlockedUntil <= _world.Tick && Council.Cleared(o))
            .OrderByDescending(o => o.Urgency).ThenBy(o => o.Id);

    public IEnumerable<WorkOrder> All => _open.Values;

    public void Close(WorkOrder o)
    {
        o.Closed = true;
        _open.Remove(o.Key);
    }

    public void Release(WorkOrder o, CrewMember c)
    {
        if (o.Assignee == c) o.Assignee = null;
    }

    /// <summary>잠시 미룬다. reason이 null이면 조용히 (기록 없이).</summary>
    public void Block(WorkOrder o, string? reason, float hours)
    {
        o.BlockedUntil = _world.Tick + SimTime.Hours(hours);
        if (reason != null && o.BlockedReason != reason)
        {
            o.BlockedReason = reason;
            _world.Log.Add(_world.Tick, LogKind.Warning, $"{o.Title} 보류 — {reason}");
        }
    }

    public void Update()
    {
        if (!_scanRequested && _world.Tick % SimTime.Minutes(5) != 0) return;
        _scanRequested = false;
        Scan();
    }

    /// <summary>새는 구획을 포기해야 하는 이유 (없으면 null).</summary>
    private string? AbandonReason(Room room, int sealantLeft)
    {
        var ship = _world.Ship;
        int need = 0;
        foreach (var (cell, wall) in ship.Walls)
            if (wall.IsHull && wall.Breach > 0f && !wall.Patched && Hull.InsideRoom(ship, cell) == room) need += Hull.SealantFor(wall);
        if (need > sealantLeft) return $"실링폼이 모자라다 (필요 {need}, 남은 {sealantLeft})";

        bool someoneOnIt = _open.Values.Any(o => o.Kind == WorkKind.SealBreach && o.Target.Room == room && o.Assignee != null);
        // v9.2: 대신할 게 없는 방은 "아무도 못 막았다"고 버리지 않는다 (막을 실링폼이 없을 때만)
        float hours = _world.Policies.AbandonHours; // v13.0 방침: 구역 포기 시점
        if (!someoneOnIt && !float.IsInfinity(hours) && _world.Tick - room.LeakingSince > SimTime.Hours(hours) && Council.Essential(_world, room) == null)
            return hours < 2f ? $"{hours:0.#}시간째 아무도 막지 못했다 (방침: 일찍 포기)" : $"{hours:0}시간째 아무도 막지 못했다";
        return null;
    }

    /// <summary>재고 + 누가 들고 옮기는 중인 것 + 가방에 든 것.</summary>
    internal int Have(ItemKind k)
    {
        int n = _world.Ship.CountStored(k);
        foreach (var c in _world.Crew)
        {
            if (c.Carrying is ItemStack held && held.Kind == k) n += held.Count;
            n += c.KitCount(k);
        }
        return n;
    }

    /// <summary>
    /// 지금 쓸 수 있는 작업대/정제기: 전기가 들어오고 멈추지 않은 것. 포기한 구획의 것은 다른 게 없을 때만 (우주복을 입고 들어가서).
    /// </summary>
    internal Furniture? StationFor(Station station) =>
        _world.Ship.FurnitureOf(Recipes.StationType(station))
            .Where(f => f.Machine!.Efficiency > 0f && !f.Machine.Has(FaultKind.Stripped) && _world.Fire.CountIn(f.Room) == 0)
            .OrderBy(f => f.Room.Abandoned ? 1 : 0).ThenByDescending(f => f.Machine!.Efficiency).FirstOrDefault();

    /// <summary>
    /// 이 물건을 곧 구할 수 있는지: 재고가 있거나, 만들 곳과 재료가 있거나 (한 단계 아래 재료까지 만들 수 있으면 된다).
    /// 없으면 Mk.1 대체품이나 부품 뜯기로 넘어간다.
    /// </summary>
    internal bool Obtainable(ItemKind k, int count = 1, int depth = 2)
    {
        if (Have(k) >= count) return true;
        if (depth <= 0) return false;
        foreach (var r in Recipes.AllFor(k))
        {
            if (StationFor(r.Station) == null) continue;
            if (r.MinSkill > 0f && !_world.Crew.Any(c => !c.Dead && !c.Down && c.SkillLevel(r.Skill) >= r.MinSkill)) continue;
            int batches = (count + r.Yield - 1) / r.Yield;
            if (r.Inputs.All(x => Obtainable(x.kind, x.count * batches, depth - 1))) return true;
        }
        return false;
    }

    /// <summary>수리가 기다리는 부품과 그 긴급도 (부품 제작 → 정제로 한 단계씩 내려간다).</summary>
    private Dictionary<ItemKind, float> PartDemand()
    {
        var demand = new Dictionary<ItemKind, float>();
        void Want(ItemKind k, int n, float u, int depth)
        {
            if (Have(k) >= n || depth < 0) return;
            demand[k] = MathF.Max(demand.GetValueOrDefault(k), u);
            foreach (var r in Recipes.AllFor(k))
                foreach (var (kind, count) in r.Inputs) Want(kind, count, u * 0.95f, depth - 1);
        }
        foreach (var m in _world.Ship.Machines)
        {
            if (m.Body.Room.Abandoned && !m.Spec.Critical) continue;
            if (m.Body.Room.OffLimits) continue;
            foreach (var f in m.Faults)
            {
                if (f.Kind == FaultKind.BreakerTrip) continue;
                // 뜯긴 설비 중 쓸모 있는 것(정수기·냉장고…)은 되돌릴 부품을 만든다
                if (f.Kind == FaultKind.Stripped && Adaptation.Worth(m, asDonor: false) < 10f) continue;
                float u = f.Kind == FaultKind.Stripped ? 0.45f : m.Spec.Critical ? 0.9f : f.Spec.OutputFactor <= 0f ? 0.6f : 0.4f;
                foreach (var (kind, count) in f.Materials) Want(kind, count, u, 2);
            }
        }
        // v9: 끊어졌거나 우회로 버티는 관을 새 관으로 갈 금속판 (냉각 본관이 가장 급하다)
        foreach (var seg in _world.Piping.Segments)
        {
            if (seg.Integrity >= 0.75f || (seg.ValveRoom?.Detached ?? true)) continue;
            if (!seg.Severed && seg.Patched) continue; // 임시로라도 막혀 있으면 덜 급하다
            float u = seg.IsCoolant ? (seg.Role is PipeRole.HotLeg or PipeRole.ColdLeg ? 0.85f : 0.7f) : 0.5f;
            Want(ItemKind.Plate, 2, u, 2);
        }
        return demand;
    }

    private void ScanFabrication(Poster post, int crewCount, int produce, int meals)
    {
        var w = _world;
        var ship = w.Ship;
        var demand = PartDemand();
        int spareFood = produce + meals * 2 / 3 - crewCount * 6;
        foreach (Station station in Enum.GetValues(typeof(Station)))
        {
            var at = StationFor(station);
            if (at == null) continue;
            // 한 곳에서는 한 번에 한 가지만 (누가 하고 있으면 새로 올리지 않는다)
            var busy = _open.Values.Where(o => o.Assignee != null && o.Target.Furniture == at && o.Kind is WorkKind.Fabricate or WorkKind.MeltIce).ToList();
            if (busy.Count > 0)
            {
                foreach (var o in busy) post(o.Kind, o.Target, o.Urgency, o.Skill, o.Detail, product: o.Product, minSkill: o.MinSkill);
                continue;
            }
            (Recipe? recipe, float u, string detail) best = (null, 0f, "");
            foreach (var r in Recipes.All)
            {
                if (r.Station != station || !w.History.Doctrine.Knows(r)) continue;
                int have = Have(r.Product);
                int held = w.Board.Held(r.Product), target = Math.Max(w.History.Doctrine.Target(r), held); // v15.6 비축 임무: 모을 만큼 더 만든다
                float wanted = demand.GetValueOrDefault(r.Product);
                if (have >= target && wanted <= 0f) continue;
                if (!r.Inputs.All(x => Have(x.kind) >= x.count)) continue;
                // 수리가 기다리는 재료는 재고 채우기용 제작에 쓰지 않는다 (뜯어 온 부품이 엉뚱한 데 쓰이지 않게)
                if (wanted <= 0f && r.Inputs.Any(x => demand.GetValueOrDefault(x.kind) > 0f)) continue;
                if (r.MinSkill > 0f && !w.Crew.Any(c => !c.Dead && !c.Down && c.SkillLevel(r.Skill) >= r.MinSkill)) continue;
                float u;
                string why;
                if (r.FromProduce)
                {
                    int need = r.Inputs[0].count;
                    if (spareFood < need) continue;
                    u = have == 0 ? 0.75f : 0.25f + 0.4f * (1f - have / (float)target);
                    // 연료는 암흑 우주선을 벗어나는 길이라, 보조 발전기가 굶고 있으면 급하다
                    if (r.Product == ItemKind.Fuel && have == 0 && (w.Power.AuxRunning || !w.Power.ReactorOnline) && w.Power.AuxFuel < 6f) u = 1.0f;
                    why = $"{ItemKinds.Name(r.Product)} {have}개 남음 · 채소 {need}개로";
                }
                else
                {
                    u = have == 0 ? 0.42f : 0.18f + 0.22f * (1f - have / (float)target) + (held > 0 && held >= target ? 0.15f : 0f);
                    why = $"{ItemKinds.Name(r.Product)} {have}개 · 목표 {target}" + (held >= target && target > r.Target ? " (임무 비축)" : target > r.Target ? " (교훈)" : "");
                }
                if (wanted > 0f && wanted > u)
                {
                    u = wanted;
                    why = $"수리에 쓸 {ItemKinds.Name(r.Product)} 없음 · " + string.Join(" + ", r.Inputs.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"));
                }
                if (u > best.u) best = (r, u, why);
            }

            // 정제기: 얼음을 녹여 공기 탱크(와 물)에 보탠다
            if (station == Station.Refinery)
            {
                int ice = Have(ItemKind.Ice);
                float tank = w.Air.Reserve / w.Air.ReserveCapacity;
                if (ice >= 2 && (tank < 0.97f || w.Water.Level < w.Water.Capacity * (w.History.Doctrine.PipeReserve ? 0.95f : 0.8f)))
                {
                    float u = 0.2f + 0.6f * (1f - tank);
                    if (u > best.u)
                    {
                        post(WorkKind.MeltIce, WorkTarget.Of(at), u, Skill.Mechanics, $"공기 탱크 {tank * 100:0}% · 얼음 {ice}개");
                        continue;
                    }
                }
            }
            if (best.recipe is Recipe pick)
                post(WorkKind.Fabricate, WorkTarget.Of(at), best.u, pick.Skill, best.detail, product: pick.Product, minSkill: pick.MinSkill);
        }
    }

    private delegate void Poster(WorkKind kind, WorkTarget target, float urgency, Skill skill, string detail, FaultKind? fault = null,
        int circuit = -1, ItemKind? product = null, float minSkill = 0f, UpgradeKind? upgrade = null);

    /// <summary>
    /// v5 적응: 완벽하게 못 고칠 때 버티는 일들.
    /// 암흑 우주선 탈출(연료통 급유·원자로 수동 기동), 임시 배선, 절전, 부품 뜯어 쓰기, 방 용도 변경.
    /// </summary>
    private void ScanAdaptation(Poster post)
    {
        var w = _world;
        var ship = w.Ship;
        var p = w.Power;
        var reactor = ship.FurnitureOf(FurnitureType.ReactorCore).FirstOrDefault();
        var aux = ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault();
        var panel = ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault();
        int fuelCans = ship.CountStored(ItemKind.Fuel);
        bool auxUsable = aux != null && !aux.Machine!.Stopped;
        bool dark = !p.ReactorOnline && p.BatteryPercent < 0.15f;

        // ── 급유: 보조 발전기 연료통 (위기 때는 급하게, 평소엔 틈날 때 채워 둔다) ──
        if (aux != null && auxUsable && fuelCans > 0 && p.AuxFuel <= PowerGrid.AuxFuelHours - PowerGrid.FuelCanHours)
        {
            bool needed = p.AuxRunning || dark;
            float u = needed && p.AuxFuel < 3f ? 1.1f : needed ? 0.7f : 0.25f;
            post(WorkKind.Refuel, WorkTarget.Of(aux), u, Skill.Mechanics, $"연료 {p.AuxFuel:0.0}시간분 · 연료통 {fuelCans}개");
        }

        // ── 원자로 수동 기동: 배터리도, 보조 발전기도(연료통까지) 없을 때 마지막 수단 ──
        bool auxOption = auxUsable && (p.AuxFuel > 0.1f || fuelCans > 0);
        if (reactor != null && !p.ReactorOnline && !reactor.Machine!.Stopped && p.CoolingCapacity < PowerGrid.RestartCoolingKw
            && p.BatteryPercent < 0.1f && !p.AuxRunning && !auxOption)
            post(WorkKind.ManualStart, WorkTarget.Of(reactor), 1.0f, Skill.Engineering,
                "전기가 하나도 없다 · 자연 순환 냉각으로 저출력 기동 (과열 위험)", minSkill: 0.4f);

        var needs = new List<(ItemKind part, float worth, float urgency, Machine? forMachine, string why)>();

        // ── 임시 배선: 배전반 회로가 죽었는데 고칠 부품(퓨즈)이 없으면 다른 회로에서 끌어온다 ──
        if (panel != null)
        {
            var pt = WorkTarget.Of(panel);
            int cables = ship.CountStored(ItemKind.Cable);
            for (int i = 0; i < PowerGrid.CircuitCount; i++)
            {
                if (p.CircuitLive[i] || p.FeedingJumper(i) != null) continue;
                var fault = panel.Machine!.Faults.FirstOrDefault(f => f.Circuit == i && f.Kind != FaultKind.BreakerTrip);
                if (fault == null || fault.Part is not ItemKind need || ship.CountStored(need) > 0) continue;
                int from = p.JumperSource(i);
                if (from < 0) continue;
                float u = i == 0 ? 1.1f : 0.6f;
                if (cables < PowerGrid.JumperCables)
                {
                    needs.Add((ItemKind.Cable, i == 0 ? 30f : 12f, u, null, $"{PowerGrid.CircuitName(i)} 회로 임시 배선"));
                    continue;
                }
                post(WorkKind.InstallJumper, pt, u, Skill.Electrical,
                    $"{fault.Name} · {ItemKinds.Name(need)} 없음 → {PowerGrid.CircuitName(from)} 회로에서 끌어온다 (케이블 {PowerGrid.JumperCables})",
                    circuit: i, minSkill: 0.3f);
            }

            // ── v9.3 저출력 운영: 원자로가 돌아도 수요를 못 댄 지 한 시간 — 회로를 통째로 끊는 대신 설비를 골라 내린다 ──
            if (!p.Brownout && p.DeficitSince >= 0 && w.Tick - p.DeficitSince > SimTime.Hours(1))
            {
                bool hungry = ship.FurnitureOf(FurnitureType.GrowBed).Any(f => !f.Room.Abandoned && !f.Machine!.Powered);
                post(WorkKind.Brownout, pt, 0.9f, Skill.Electrical,
                    $"원자로 {p.ReactorLimit:0}kW < 수요 {p.Demand:0}kW · 배터리 {p.BatteryPercent * 100:0}%" + (hungry ? " · 재배대가 꺼져 있다" : ""));
            }
            if (p.Brownout && p.SurplusSince >= 0 && w.Tick - p.SurplusSince > SimTime.Hours(6))
                post(WorkKind.EndBrownout, pt, 0.4f, Skill.Electrical, $"원자로 {p.ReactorLimit:0}kW ≥ 다 켠 수요 {p.FullDemand:0}kW (여섯 시간째)");

            // ── 절전: 배터리가 빠지는데 주 전력이 모자라면 덜 중요한 회로부터 사람이 내린다 (A는 절대 안 내림) ──
            //    v9.3: 배터리가 이미 바닥이면(빠질 것도 없이) 모자란 채로 굳은 것도 굶는 것이다
            bool draining = p.BatteryFlow < -0.5f;
            bool starving = (!p.ReactorOnline || p.LowPowerMode || p.ReactorLimit < p.Demand)
                            && (draining || (p.BatteryPercent < 0.05f && p.ReactorLimit + 0.5f < p.Demand));
            // 수리가 부품을 기다리는 동안에는 작업대·정제기가 있는 회로를 끊지 않는다 (끊으면 부품을 못 만든다)
            bool making = PartDemand().Count > 0;
            // v13.2 방침(전원 차단): 넓게 = 일찍 · 좁게 = 바닥 가까이까지
            float[] shedBelow = w.Policies["shed"] switch
            {
                0 => new[] { 0f, 0.3f, 0.45f, 0.6f },
                2 => new[] { 0f, 0.12f, 0.24f, 0.38f },
                _ => new[] { 0f, 0.2f, 0.35f, 0.5f },
            };
            if (starving)
                for (int i = PowerGrid.CircuitCount - 1; i >= 1; i--)
                {
                    if (p.ManualOff[i]) continue;
                    // v9.3: 저출력 운영 중이거나 배터리가 이미 바닥이면 기관·작업(D) 회로만 통째로 내린다 —
                    //       생활(B: 재배·조리)·거주(C: 함교의 주 컴퓨터·침실 환기)까지 끊으면 굶거나 자동화를 잃는다
                    if ((p.Brownout || !draining) && i < PowerGrid.CircuitCount - 1) break;
                    bool feedsJumper = p.Jumpers.Any(j => j.From == i && j.Active);
                    // 격벽·감지기가 일해야 하는 방(감압·잠금·불)이 있는 회로는 내리지 않는다
                    int circuit = i;
                    bool needed = ship.Rooms.Any(r => r.Circuit == circuit && !r.Abandoned && (r.Leaking || r.Lockdown || w.Fire.CountIn(r) > 0))
                        || (making && ship.Machines.Any(m => m.Body.Room.Circuit == circuit && m.Body.Type is FurnitureType.Workbench or FurnitureType.Refinery));
                    // 가장 덜 중요한 회로 하나만 본다: 그 회로를 끊을 수 없으면(부품 제작 중 등) 더 중요한 회로를 대신 끊지 않고
                    // 배전반의 자동 우선순위 차단에 맡긴다 (v7 물자 부족 시험: B까지 끊으면 조리·재배가 멈춰 굶는다)
                    if (p.BatteryPercent < shedBelow[i] && !feedsJumper && !needed)
                        post(WorkKind.ShedLoad, pt, 0.95f, Skill.Electrical,
                            $"배터리 {p.BatteryPercent * 100:0}% · 방전 {-p.BatteryFlow:0.0}kW → {PowerGrid.CircuitRole(i)} 회로를 끊어 필수 회로에 몰아준다",
                            circuit: i);
                    break;
                }

            // ── v10.1 블랙스타트: 저출력 수동 운전 중인데 고친 냉각 펌프에 전기가 안 간다 (자동화가 꺼지면 배전반이 우선순위를 몰라
            //    먼저 붙은 방 환기부터 먹는다) → 펌프 말고 다른 회로를 하나씩 내려 펌프에 몰아준다. 펌프가 돌면 원자로를 제대로 올린다 ──
            var pumps = ship.FurnitureOf(FurnitureType.CoolantPump).Where(f => !f.Room.Abandoned).ToList();
            if (p.ReactorOnline && p.LowPowerMode && pumps.Any(f => !f.Machine!.Stopped && !f.Machine.Parked) && !pumps.Any(f => f.Machine!.Powered))
                for (int i = PowerGrid.CircuitCount - 1; i >= 1; i--)
                {
                    if (p.ManualOff[i] || pumps.Any(f => f.Room.Circuit == i)) continue;
                    int circuit = i;
                    bool needed = ship.Rooms.Any(r => r.Circuit == circuit && !r.Abandoned && (r.Leaking || r.Lockdown || w.Fire.CountIn(r) > 0));
                    if (needed) continue;
                    post(WorkKind.ShedLoad, pt, 0.95f, Skill.Electrical,
                        $"냉각 펌프는 고쳤는데 전기가 안 간다 (저출력 {p.ReactorLimit:0}kW) → {PowerGrid.CircuitRole(i)} 회로를 내려 펌프에 몰아준다", circuit: i);
                    break;
                }

            // ── 회로 복귀: 주 전력에 여유가 생기면 중요한 회로부터 다시 올린다.
            //    여유가 모자라도 배터리가 가득 차면 일단 올린다 → 다시 빠지면 또 내린다 (돌아가며 끊는 윤번 정전) ──
            bool online = p.ReactorOnline && !p.LowPowerMode && p.ReactorRamp >= 1f;
            if (online && p.BatteryPercent > 0.4f)
                for (int i = 1; i < PowerGrid.CircuitCount; i++)
                {
                    if (!p.ManualOff[i]) continue;
                    float headroom = p.ReactorLimit - p.Delivered;
                    float need = p.CircuitDemand(i);
                    if (headroom >= need + 1f)
                        post(WorkKind.RestoreCircuit, pt, 0.5f, Skill.Electrical, $"주 전력 여유 {headroom:0}kW", circuit: i);
                    else if (p.BatteryPercent > 0.85f)
                        post(WorkKind.RestoreCircuit, pt, 0.45f, Skill.Electrical,
                            $"배터리 {p.BatteryPercent * 100:0}% · 여유 {headroom:0}kW < {need:0}kW → 배터리가 버티는 동안만 (윤번)", circuit: i);
                    break;
                }
        }

        // ── 부품 뜯어 쓰기: 급한 수리에 부품이 바닥났으면, 덜 중요한 설비에서 뜯는다 ──
        // (누가 들고 옮기는 중인 부품도 있는 것으로 친다 — 안 그러면 뜯은 부품을 나르는 동안 또 뜯는다)
        foreach (var m in ship.Machines)
        {
            if (m.Body.Room.Abandoned && !m.Spec.Critical) continue;
            foreach (var fault in m.Faults)
            {
                if (fault.Kind is FaultKind.Stripped or FaultKind.BreakerTrip || fault.Part is not ItemKind part) continue;
                if (Have(part) > 0) continue;
                // 수리의 연속선 (리뷰어 안): 정상 수리 → 부품 제작 → Mk.1 대체품 → 부품 뜯기.
                // 만들 수 있으면 만들고(PartDemand가 제작·정제 작업을 올린다), 못 만들면 기본 수리재로 Mk.1을 달고, 그것도 안 되면 뜯는다
                if (fault.Materials.All(x => Obtainable(x.kind, x.count))) continue;
                if (SubstituteOk(m, fault))
                {
                    post(WorkKind.InstallSubstitute, WorkTarget.Of(m.Body), m.Spec.Critical ? 0.92f : 0.55f, m.Spec.Skill,
                        $"{fault.Spec.Name} · {Ko.EulReul(ItemKinds.Name(part))} 구할 수도 만들 수도 없음 → " +
                        string.Join(" + ", Faults.SubstituteCost(m.Body.Type).Select(x => $"{ItemKinds.Name(x.kind)} {x.count}")) +
                        " (효율 65%·전력 130%)");
                    continue;
                }
                // 누가 이미 손대고 있으면 (부분 수리 중이라도) 끝나고 다시 본다
                if (_open.Values.Any(o => o.Kind == WorkKind.Repair && o.Target.Furniture == m.Body && o.Fault == fault.Kind && o.Circuit == fault.Circuit && o.Assignee != null)) continue;
                bool urgent = m.Spec.Critical ? fault.Stage < 2 : fault.Spec.OutputFactor <= 0f && fault.Stage == 0;
                // 핵심 설비가 60%로 버티는 중이면, 값싼 설비(엔진·콘솔, 포기한 구획의 설비)만 뜯어서 마저 고친다
                bool finish = m.Spec.Critical && fault.Stage == 2;
                if (!urgent && !finish) continue;
                // 배전반 회로를 이미 임시 배선이 받치고 있으면 덜 급하다
                if (fault.Circuit >= 0 && p.FeedingJumper(fault.Circuit) != null && fault.Circuit != 0) continue;
                needs.Add((part, finish ? MathF.Min(6f, Adaptation.Worth(m, asDonor: false)) : Adaptation.Worth(m, asDonor: false),
                    finish ? 0.5f : m.Spec.Critical ? 0.9f : 0.6f, m, $"{m.Name} {fault.Spec.Name}"));
            }
        }
        // v9: 냉각이 통째로 멈췄는데 끊어진 관을 돌아 이을 금속판조차 없으면, 덜 중요한 설비(엔진·정제기)의 외판을 뜯는다
        foreach (var seg in w.Piping.Segments)
        {
            if (!seg.IsCoolant || !seg.Severed || seg.Bypass >= 0.45f || Have(ItemKind.Plate) >= 1) continue;
            bool alone = seg.Role is PipeRole.HotLeg or PipeRole.ColdLeg || !w.Piping.Branches.Any(b => b != seg && b.Flow > 0f);
            if (!alone || (seg.ValveRoom?.Detached ?? true)) continue;
            needs.Add((ItemKind.Plate, 30f, 0.95f, null, $"{seg.Name} 우회 배관"));
        }
        // v9.2: 주 컴퓨터를 아예 잃었는데 임시 제어 컴퓨터를 짤 전자재가 없으면, 콘솔 같은 덜 중요한 설비에서 뜯는다
        if (w.Automation.Gone && w.Tick - w.Automation.GoneSince > SimTime.Hours(4) && Have(ItemKind.Electronics) < 2)
            needs.Add((ItemKind.Electronics, 8f, 0.6f, null, "임시 제어 컴퓨터"));
        if (w.Policies["cannibalize"] == 0) needs.Clear(); // v13.2 방침(부품 뜯기: 금지)
        foreach (var group in needs.GroupBy(n => n.part))
        {
            var part = group.Key;
            // 한 부품에 한 번씩만 (뜯고 나면 다음 훑기에서 또 모자라면 또 뜯는다)
            if (_open.Values.Any(o => o.Kind == WorkKind.Cannibalize && o.Product == part && o.Assignee != null)) continue;
            var top = group.OrderByDescending(n => n.worth).First();
            var exclude = group.Where(n => n.forMachine != null).Select(n => n.forMachine!).ToHashSet();
            var donor = Adaptation.BestDonor(w, part, top.worth, exclude, anything: w.Policies["cannibalize"] == 2);
            if (donor == null) continue;
            post(WorkKind.Cannibalize, WorkTarget.Of(donor.Body), MathF.Min(0.95f, group.Max(n => n.urgency)), Skill.Mechanics,
                $"{top.why}에 쓸 {ItemKinds.Name(part)} 재고 없음 · {Ko.EunNeun(donor.Name)} 영구히 멈춘다" + (donor.Body.Room.Abandoned ? " · 포기한 구획" : ""),
                product: part);
        }

        // ── 정품 복원: Mk.1 임시품은 부품이 넉넉해지면 되돌린다 ──
        foreach (var m in ship.Machines)
        {
            if (m.Grade != MachineGrade.Mk1 || m.Faults.Count > 0 || m.Body.Room.Abandoned) continue;
            var key = Faults.KeyPart(m.Body.Type);
            if (Have(key) >= (m.Spec.Critical ? 1 : 2) && Have(ItemKind.Plate) >= 1)
                post(WorkKind.RestoreGrade, WorkTarget.Of(m.Body), m.Spec.Critical ? 0.4f : 0.28f, m.Spec.Skill,
                    $"{ItemKinds.Name(key)} {Have(key)}개 · Mk.1 임시품 → 정품");
        }

        // ── 외벽 패널 교체: 여러 번 때운 벽은 구조재가 넉넉할 때 통째로 간다 (땜질 ≠ 새것) ──
        if (Have(ItemKind.Structure) >= 3 && Have(ItemKind.Plate) >= 3)
        {
            (Cell cell, WallState wall, Room room)? worst = null;
            foreach (var (cell, wall) in ship.Walls)
            {
                if (!wall.IsHull || wall.Breach > 0f || wall.Patched || wall.FrameLost || (wall.MaxIntegrity >= 0.9f && wall.Welds < 2)) continue;
                var inside = Hull.InsideRoom(ship, cell);
                if (inside == null || inside.Abandoned || inside.OffLimits || inside.Leaking || inside.Air.Pressure < 85f) continue;
                if (worst == null || wall.MaxIntegrity < worst.Value.wall.MaxIntegrity) worst = (cell, wall, inside);
            }
            if (worst is var (wc, ww, wr))
                post(WorkKind.ReplacePanel, WorkTarget.OfWall(wc, wr), 0.22f, Skill.Mechanics,
                    $"용접 {ww.Welds}회 · 최대 강도 {ww.MaxIntegrity * 100:0}% → 구조재 2 + 금속판 2로 새 패널");
        }

        // ── 임시 정비실: 쓸 수 있는 작업대가 하나도 없으면(정비실을 포기했으면) 다른 방에 작업대를 짠다 ──
        var benches = ship.FurnitureOf(FurnitureType.Workbench).ToList();
        if (!benches.Any(b => !b.Room.Abandoned && !b.Machine!.Has(FaultKind.Wrecked) && !b.Machine.Has(FaultKind.Stripped))
            && Have(ItemKind.Plate) >= 2 && Have(ItemKind.Cable) >= 1 && Adaptation.WorkshopTarget(w) is Room shop)
            post(WorkKind.BuildWorkshop, WorkTarget.OfRoom(shop), 0.65f, Skill.Mechanics,
                "쓸 수 있는 작업대가 없다 → 금속판 2 + 케이블 1로 임시 작업대");

        // ── v9.2 임시 제어 컴퓨터: 주 컴퓨터를 아예 잃었으면(함교가 떨어져 나갔거나 부서졌으면) 네 시간 뒤 다른 방에 짠다 ──
        //    (되찾아 올 수 있으면 그동안 되찾는다. Mk.1이라 느리지만 자동화가 돌아온다)
        if (w.Automation.Gone && w.Tick - w.Automation.GoneSince > SimTime.Hours(4)
            && Have(ItemKind.Electronics) >= 2 && Have(ItemKind.Cable) >= 2 && Adaptation.ComputerTarget(w) is Room ctrl)
            post(WorkKind.BuildComputer, WorkTarget.OfRoom(ctrl), 0.6f, Skill.Electrical,
                $"주 컴퓨터를 잃은 지 {(w.Tick - w.Automation.GoneSince) / (float)SimTime.TicksPerHour:0}시간 → 전자재 2 + 케이블 2로 임시 제어 컴퓨터 (Mk.1)");

        // ── v11.1 임시 산소 발생기: 돌아가는 산소 발생기가 하나도 없는 지 한 시간 — 전력 제어기·케이블·금속판으로 Mk.1을 짠다 ──
        {
            bool none = !ship.FurnitureOf(FurnitureType.OxygenGenerator).Any(f => !f.Stowed && !f.Room.Detached && !f.Room.Abandoned
                                                                               && !f.Machine!.Has(FaultKind.Wrecked) && !f.Machine.Has(FaultKind.Stripped));
            if (none && w.Air.NoGeneratorSince >= 0 && w.Tick - w.Air.NoGeneratorSince > SimTime.Hours(1)
                && Have(ItemKind.PowerController) >= 1 && Have(ItemKind.Cable) >= 2 && Have(ItemKind.Plate) >= 2 && Adaptation.ComputerTarget(w) is Room o2room)
                post(WorkKind.BuildOxygen, WorkTarget.OfRoom(o2room), 1.0f, Skill.Mechanics,
                    $"산소 발생기를 모두 잃은 지 {(w.Tick - w.Air.NoGeneratorSince) / (float)SimTime.TicksPerHour:0}시간 → 전력 제어기 1 + 케이블 2 + 금속판 2로 임시 산소 발생기 (Mk.1)");
        }

        // ── v10.1 레이더 감시: 주 컴퓨터가 꺼져 궤적 계산이 없으면, 센서가 살아 있는 동안 사람이 통신실 화면을 지킨다 ──
        //    (운석이 오면 몇 분 전에라도 방향을 알 수 있다 — 대신 일손 하나가 묶인다)
        if (!w.Automation.MainOnline && w.Sensors.Online && w.Sensors.CommsRoom is Room comms && !comms.Abandoned && !comms.Leaking && !comms.OffLimits
            && comms.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Console && !f.Machine!.Stopped) is Furniture radar)
            post(WorkKind.RadarWatch, WorkTarget.Of(radar), w.Sensors.Incoming.Count > 0 ? 0.7f : 0.34f, Skill.Piloting,
                $"주 컴퓨터가 꺼져 운석 궤적을 계산하지 못한다 · 센서 {w.Sensors.Quality * 100:0}% → 사람이 화면을 본다");

        // ── 방 용도 변경: 침실을 포기한 지 여섯 시간이 넘으면 다른 방에 간이침대를 놓는다 ──
        var homeless = Adaptation.Homeless(w);
        if (homeless.Count > 0 && Adaptation.DormTarget(w, homeless.Count) is Room dorm)
        {
            var lost = homeless[0].Bed!.Room;
            post(WorkKind.RepurposeRoom, WorkTarget.OfRoom(dorm), 0.45f, Skill.Mechanics,
                $"{lost.Name} 폐쇄 {(w.Tick - lost.AbandonedSince) / (float)SimTime.TicksPerHour:0}시간 · {homeless.Count}명 잘 곳 없음 → 간이침대");
        }
        // 침실을 되찾으면 원래 침대로 돌아간다 (간이침대는 흔적으로 남는다)
        foreach (var c in w.Crew)
        {
            if (c.HomeBed is not Furniture home || home.Room.Abandoned || home.Room.OffLimits || home.Room.Leaking) continue;
            if (c.Bed != null && c.Bed != home && c.Bed.Owner == c) c.Bed.Owner = null;
            c.Bed = home;
            c.HomeBed = null;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.EulReul(home.Room.Name)} 되찾아 원래 침대로 돌아간다", c.Id);
            foreach (var room in ship.Rooms)
                if (room.Purpose == "임시 침실" && !room.Furniture.Any(f => f.Type == FurnitureType.Cot && f.Owner != null))
                {
                    room.Purpose = "임시 침실 (비어 있음)";
                    if (!room.FormerPurposes.Contains("임시 침실")) room.FormerPurposes.Add("임시 침실");
                }
        }
    }

    /// <summary>이 고장에 Mk.1 임시품을 달 수 있는지 (배전반은 임시 배선이 그 역할, 뜯긴 설비는 제외).</summary>
    private bool SubstituteOk(Machine m, Fault fault)
    {
        if (m.Body.Type is FurnitureType.PowerPanel || m.Has(FaultKind.Stripped) || fault.Circuit >= 0) return false;
        if (fault.Kind != FaultKind.Wrecked && fault.Spec.OutputFactor > 0.5f) return false; // 가벼운 고장은 그냥 버틴다
        return Faults.SubstituteCost(m.Body.Type).All(x => Have(x.kind) >= x.count);
    }

    private void Scan()
    {
        var w = _world;
        var ship = w.Ship;
        var seen = new HashSet<string>();
        long psc = Prof.Now; // v14.2

        void Post(WorkKind kind, WorkTarget target, float urgency, Skill skill, string detail, FaultKind? fault = null, int circuit = -1,
            ItemKind? product = null, float minSkill = 0f, UpgradeKind? upgrade = null)
        {
            var probe = new WorkOrder { Kind = kind, Target = target, Fault = fault, Circuit = circuit, Product = product, Upgrade = upgrade };
            if (!_open.TryGetValue(probe.Key, out var o))
            {
                o = new WorkOrder
                {
                    Id = _nextId++, Kind = kind, Target = target, Fault = fault, Circuit = circuit, Product = product,
                    Skill = skill, Posted = w.Tick, Upgrade = upgrade,
                };
                _open[o.Key] = o;
            }
            o.Urgency = urgency;
            o.Detail = detail;
            o.MinSkill = minSkill;
            o.Target = target; // 열쇠가 같아도 대상의 세부(가장 뜨거운 칸 등)는 바뀔 수 있다
            o.LastSeen = w.Tick;
            seen.Add(o.Key);
        }

        // ── 설비: 고장 → 수리, 차단기 → 복구, 마모 → 정비, 작물 ──
        foreach (var m in ship.Machines)
        {
            var t = WorkTarget.Of(m.Body);
            if (m.Body.Room.Abandoned && !m.Spec.Critical) continue;
            if (m.Body.Room.OffLimits) continue; // 사출할 방·잔해의 설비는 고치지 않는다
            foreach (var fault in m.Faults)
            {
                if (fault.Kind == FaultKind.BreakerTrip)
                {
                    Post(WorkKind.ResetBreaker, t, fault.Circuit == 0 ? 1.05f : 0.85f, Skill.Electrical,
                        $"{PowerGrid.CircuitRole(fault.Circuit)} 회로 정전", fault.Kind, fault.Circuit);
                    continue;
                }
                if (fault.Kind == FaultKind.Stripped)
                {
                    // 뜯어 간 설비: 쓸모 있는 설비는 부품이 생기면 되돌려 놓고, 콘솔 같은 건 부품이 넘칠 때만
                    bool useful = Adaptation.Worth(m, asDonor: false) >= 10f;
                    if (fault.Part is ItemKind back && ship.CountStored(back) >= (useful ? 1 : 3))
                        Post(WorkKind.Repair, t, useful ? 0.5f : 0.2f, m.Spec.Skill, $"{fault.Name} · 부품이 생겼다 → 되돌려 놓기", fault.Kind, fault.Circuit);
                    continue;
                }
                float u = m.Spec.Critical ? 0.95f : fault.Spec.OutputFactor <= 0f ? 0.7f : 0.55f;
                if (fault.Circuit == 0) u = 1.05f;
                // 임시로라도 살려 놓았으면 덜 급하다 (완전 수리는 여유 있을 때)
                if (fault.Kind == FaultKind.Wrecked) u = m.Spec.Critical ? 1.0f : 0.5f;
                if (fault.Kind == FaultKind.GasLeak) u = 0.92f; // v11.2 새는 가스는 방 하나를 통째로 못 쓰게 한다
                if (fault.Stage == 1) u = m.Spec.Critical ? 0.7f : 0.45f;
                else if (fault.Stage == 2) u = m.Spec.Critical ? 0.5f : 0.35f;
                var missing = fault.Materials.Where(x => ship.CountStored(x.kind) < x.count).Select(x => ItemKinds.Name(x.kind)).ToList();
                string partNote = missing.Count > 0 ? $" · {string.Join("·", missing)} 없음" : "";
                Post(WorkKind.Repair, t, u, m.Spec.Skill, fault.Name + partNote, fault.Kind, fault.Circuit);
            }
            bool haveItem = (m.Spec.ServiceItem is not ItemKind item || ship.CountStored(item) > 0) && !Adaptation.Rationed(w, m);
            bool sealedOff = m.Body.Room.Abandoned; // 포기한 구획의 설비는 핵심 설비 고장만 우주복 입고 간다
            // 소모품이 없으면 임시 정비(마모를 0.35까지만 낮춤)밖에 못 하므로, 0.5는 넘어야 손을 댄다
            // v13.4 방침(정비: 고장 나면) — 거의 닳아 빠질 때까지 손대지 않는다
            if (!sealedOff && m.Faults.Count == 0 && m.Wear >= (w.Policies["maint"] == 1 ? 0.88f : haveItem ? 0.35f : 0.5f))
            {
                float u = 0.15f + 0.7f * (m.Wear - 0.35f) / 0.65f + (m.Spec.Critical ? 0.1f : 0f);
                Post(WorkKind.Maintain, t, u, m.Spec.Skill, haveItem ? $"마모 {m.Wear * 100:0}%"
                    : Adaptation.Rationed(w, m) ? $"마모 {m.Wear * 100:0}% · 소모품은 핵심 설비 몫 → 임시 정비만" : $"마모 {m.Wear * 100:0}% · 소모품 없어 임시 정비만");
            }
            if (m.Crop is CropState crop && !sealedOff)
            {
                if (crop.Ripe) Post(WorkKind.Harvest, t, 0.5f, Skill.Botany, "다 자람");
                else if (crop.Blight > 0f && crop.BlightKnown) // v11.2 병충해: 사람이 약을 쳐야 한다
                    Post(WorkKind.Tend, t, 0.6f + 0.3f * crop.Blight, Skill.Botany, $"병충해 {crop.Blight * 100:0}% · 약을 친다");
                else if (crop.Care < 0.55f) Post(WorkKind.Tend, t, 0.2f + (0.55f - crop.Care) * 0.8f, Skill.Botany, $"돌봄 {crop.Care * 100:0}%");
            }
        }

        // ── 전력: 원자로 재기동, 보조 발전기 ──
        var p = w.Power;
        if (!p.ReactorOnline && ship.FurnitureOf(FurnitureType.ReactorCore).FirstOrDefault() is Furniture reactor)
        {
            if (p.CoolingCapacity >= PowerGrid.RestartCoolingKw)
                Post(WorkKind.RestartReactor, WorkTarget.Of(reactor), 1.1f, Skill.Engineering, "냉각 확보됨 · 재기동 가능");
        }
        if (ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault() is Furniture aux && !p.AuxRunning && p.AuxFuel > 0.1f
            && !aux.Machine!.Stopped && p.BatteryPercent < 0.15f && p.ReactorLimit < 12f)
            Post(WorkKind.StartAux, WorkTarget.Of(aux), 1.15f, Skill.Electrical, $"배터리 {p.BatteryPercent * 100:0}% · 원자로 {p.ReactorLimit:0}kW");

        ScanAdaptation(Post);
        psc = Prof.Lap("scan.Adaptation", psc);

        psc = Prof.Lap("scan.machines·power", psc);
        // ── 선체: 파공 봉합, 외벽 수리 ──
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull || (wall.Breach <= 0f && wall.Integrity >= 0.6f && !wall.Patched)) continue;
            if (wall.FrameLost) continue; // 구조 연결 상실: 밖에서 골조부터 (ScanStructure)
            var inside = Hull.InsideRoom(ship, cell);
            if (inside == null || inside.Jettison != null || inside.Wreck) continue;
            var t = WorkTarget.OfWall(cell, inside);
            if (wall.Breach > 0f && !wall.Patched)
            {
                bool big = wall.Breach >= 0.25f;
                if (inside.Abandoned)
                {
                    // 포기한 구획: 실링폼이 넉넉할 때만, 여유 있게 되찾으러 간다 (되살리기로 한 방은 제대로).
                    // v9.2: 대신할 게 없는 방(배전반·원자로 …)은 실링폼이 있는 대로 서둘러 되찾는다
                    string? vital = Council.Essential(w, inside);
                    if (vital != null && ship.CountStored(ItemKind.Sealant) >= Hull.SealantFor(wall))
                        Post(WorkKind.SealBreach, t, 0.9f, Skill.Mechanics, $"포기한 구획 되찾기 — {vital} 없이는 버틸 수 없다 · {wall.Stage} · 우주복 필요");
                    else if (inside.Restoring || ship.CountStored(ItemKind.Sealant) >= Hull.SealantFor(wall) + 2)
                        Post(WorkKind.SealBreach, t, inside.Restoring ? 0.55f : 0.4f, Skill.Mechanics,
                            (inside.Restoring ? "다시 붙인 방 되살리기" : "포기한 구획 되찾기") + $" · {wall.Stage} · 우주복 필요");
                    // v12.2 실링폼이 없으면 우주복을 입고 금속판을 덧대 용접한다 (느리지만 영구) — 평시에, 금속판이 남을 때
                    else if (vital != null || Crisis.Level(w) < CrisisLevel.Emergency && ship.CountStored(ItemKind.Plate) >= 3)
                        if (ship.CountStored(ItemKind.Plate) >= 2)
                            Post(WorkKind.SealBreach, t, vital != null ? 0.85f : 0.42f, Skill.Mechanics,
                                $"포기한 구획 되찾기 — 실링폼이 없어 금속판을 덧대 용접 · {wall.Stage} · 우주복 필요");
                }
                else
                    Post(WorkKind.SealBreach, t, big ? 1.25f : 0.95f, Skill.Mechanics,
                        $"{wall.Stage} {wall.Breach * 100:0}% · 실링폼 {Hull.SealantFor(wall)}개" + (inside.Unbreathable ? " · 우주복 필요" : ""));
            }
            else if (!inside.Leaking && !inside.Unbreathable && inside.Air.Pressure > 85f && Hull.WorthWelding(wall))
            {
                Post(WorkKind.RepairHull, t, wall.Patched ? 0.5f : 0.35f, Skill.Mechanics,
                    wall.Patched ? "임시 봉합 → 용접 수리" : $"{wall.Stage} (강도 {wall.Integrity * 100:0}%)");
            }
        }

        // ── 구획 포기와 재개방 (비상 규정: 막을 수 없으면 격리하고, 되찾을 수 있으면 다시 연다) ──
        int sealantLeft = ship.CountStored(ItemKind.Sealant);
        foreach (var room in ship.Rooms)
        {
            if (room.Type == RoomType.Corridor) continue; // 통로를 버리면 배가 둘로 갈린다
            if (room.Detached || room.Jettison != null) continue;
            if (room.Abandoned)
            {
                if (room.Docked || room.Wreck) continue; // 다시 붙인 방은 재연결부터
                if (!room.Leaking && w.Air.Reserve >= room.Volume * 70f)
                    Post(WorkKind.ReopenRoom, WorkTarget.OfRoom(room), Council.Essential(w, room) != null ? 0.8f : 0.45f, Skill.Mechanics,
                        "새는 곳 없음 · 공기 탱크 여유 있음" + (Council.Essential(w, room) is string v ? $" · {v} 없이는 버틸 수 없다" : ""));
                continue;
            }
            if (!room.Leaking) continue;
            string? reason = AbandonReason(room, sealantLeft);
            if (reason != null)
                Post(WorkKind.SealOffRoom, WorkTarget.OfRoom(room), 1.0f, Skill.Mechanics, reason);
        }

        // ── v9.4 문 구동기: 망가진 문은 손으로 천천히 열리고 격벽이 저절로 잠기지 않는다 ──
        foreach (var d in ship.Doors)
        {
            // 임시 구동기 문은 모터가 넉넉해지면 정식으로 간다
            if (d.MotorMk1 && !d.MotorBroken && !d.Removed && ship.CountStored(ItemKind.Motor) >= 2)
                Post(WorkKind.RepairDoor, WorkTarget.OfDoor(d), 0.2f, Skill.Mechanics, "모터가 생겼다 — 임시 구동기를 떼고 정식으로");
            if (!d.MotorBroken || d.Removed || d.Welded || d.IsExternal) continue;
            var ra = d.RoomA; var rb = d.RoomB;
            if ((ra?.Detached ?? true) || (rb?.Detached ?? true) || ((ra?.Abandoned ?? false) && (rb?.Abandoned ?? false))) continue;
            bool danger = ra!.Leaking || rb!.Leaking || ra.Lockdown || rb.Lockdown;
            bool route = ra.Type is RoomType.Medbay or RoomType.Corridor || rb!.Type is RoomType.Medbay or RoomType.Corridor;
            Post(WorkKind.RepairDoor, WorkTarget.OfDoor(d), 0.35f + (danger ? 0.4f : 0f) + (route ? 0.1f : 0f), Skill.Mechanics,
                (danger ? "감압인데 저절로 안 잠긴다 · " : route ? "많이 다니는 길 · " : "") + "모터 1 (없으면 케이블 1 + 금속판 1로 임시 구동기)");
        }
        // ── v9.4 조명: 나간 방은 캄캄하다 (일이 느리고, 꺼리고, 불안하다) ──
        foreach (var room in ship.LiveRooms)
        {
            if (!room.LightsOut || room.Abandoned) continue;
            bool busy = room.Type is RoomType.Medbay or RoomType.Workshop or RoomType.Galley or RoomType.Hydroponics or RoomType.Bridge
                        || room.Furniture.Any(f => f.Machine is Machine m && m.Spec.Critical);
            Post(WorkKind.FixLights, WorkTarget.OfRoom(room), busy ? 0.45f : 0.3f, Skill.Electrical, "케이블 1");
        }

        // ── 환기 댐퍼: 전기가 없어 자동으로 못 움직이면 손으로 ──
        //    (떨어져 나간 방의 댐퍼가 열려 있으면 환기관이 우주로 열린 것: 방 밖 댐퍼를 손으로 닫는다)
        foreach (var room in ship.Rooms)
        {
            if (room.Jettison != null && room.Jettison.Stage <= JettisonStage.Vent) continue; // 사출 절차가 따로 닫는다
            if (room.Detached && room.VentOpen && !ship.IsWalkable(room.DamperSpot)
                && w.Tick - (room.Fragment?.Since ?? w.Tick) > SimTime.Minutes(30))
            {
                room.VentOpen = false; // 댐퍼 자리가 함께 떨어져 나갔다: 역류 방지판이 결국 닫힌다
                continue;
            }
            bool want = Hull.WantVentOpen(w, room);
            bool autoDamper = room.Powered && !room.DamperJammed && !room.DamperStuck && w.Automation.DampersIn(room);
            if (room.VentOpen == want || autoDamper) continue;
            if (room.DamperJammed && want) continue;
            // 환기망 격리 중: 손으로는 떨어져 나간 방 쪽 댐퍼(환기관이 뚫린 곳)만 닫으러 간다. 격리가 풀리면 다시 연다
            if (w.Structure.DuctOpen && !room.Detached && !room.Leaking && !w.Fire.IsKnown(room)) continue;
            if (room.Detached)
            {
                Post(WorkKind.OperateDamper, WorkTarget.OfRoom(room), 1.2f, Skill.Mechanics, "떨어져 나간 방 쪽 환기관이 우주로 열렸다 — 댐퍼를 손으로 닫는다");
                continue;
            }
            bool computerRoom = w.Automation.ComputerBody?.Room == room;
            Post(WorkKind.OperateDamper, WorkTarget.OfRoom(room), want ? (computerRoom ? 1.0f : 0.6f) : 1.15f, Skill.Mechanics,
                room.DamperStuck ? $"댐퍼 구동기가 걸렸다 — 손으로 {(want ? "연다" : "닫는다")}" + (computerRoom ? $" (주 컴퓨터가 달아오른다 · {room.Air.Temperature:0}℃)" : "")
                : room.Powered && !w.Automation.DampersIn(room) ? $"자동화가 꺼져 댐퍼가 저절로 안 움직인다 — 손으로 {(want ? "연다" : "닫는다")}"
                : want ? "위험 해소 · 환기하려면 열어야 함"
                : room.DamperJammed ? "댐퍼가 열에 걸려 안 닫힌다 — 불이 우주선 공기를 빨아들인다"
                : room.Leaking ? "감압 중인데 전기가 없어 자동으로 안 닫힘" : "화재 중인데 전기가 없어 자동으로 안 닫힘");
        }

        // ── 격벽: 감압 중인데 전기가 없어 자동으로 안 닫힌 문은 손으로 돌려 닫는다 ──
        //    (v8: 불에 휘어 열린 채 걸린 문은 지렛대로 억지로 닫는다 — 안 닫으면 불이 옆방 공기를 끌어들인다)
        foreach (var d in ship.Doors)
        {
            if (!d.JammedOpen || d.Removed) continue;
            var burning = new[] { d.RoomA, d.RoomB }.FirstOrDefault(r => r != null && w.Fire.IsKnown(r));
            if (burning == null) { d.JammedOpen = false; continue; } // 식으면 풀린다
            Post(WorkKind.CrankDoor, WorkTarget.OfDoor(d), 1.1f, Skill.Mechanics, $"{burning.Name} 문이 열에 휘어 열린 채 걸렸다 — 지렛대로 억지로 닫는다");
        }
        foreach (var room in ship.Rooms)
        {
            if (!room.Lockdown || room.Abandoned) continue;
            foreach (var d in room.Doors)
            {
                if (d.IsExternal || d.Locked || (d.Powered && w.Automation.Doors && w.Policies["autoscope"] >= 1 && (d.RoomA?.DataLinked ?? true) && (d.RoomB?.DataLinked ?? true))) continue;
                var other = d.RoomA == room ? d.RoomB : d.RoomA;
                Post(WorkKind.CrankDoor, WorkTarget.OfDoor(d), room.Leaking ? 1.15f : 0.9f, Skill.Mechanics,
                    $"{room.Name} 감압 중인데 {(d.Powered ? "자동화가 꺼져" : "전기가 없어")} {(other != null ? other.Name + " 쪽 " : "")}격벽이 안 닫힘"
                    + (d.Powered ? " — 손으로 잠근다" : ""));
            }
        }

        // ── 화재: 알려진 불만 ──
        foreach (var (room, hottest, count) in w.Fire.KnownFires())
        {
            if (room.ResponseHold) continue; // v13.0 컴퓨터가 질식·진공 소화로 끄는 중 — 사람은 들어가지 않는다
            bool critical = room.Type is RoomType.Reactor or RoomType.Power or RoomType.LifeSupport or RoomType.Cooling;
            // v13.2 방침(불 끄는 수단: 자동 소화 먼저): 장치가 있는 방은 45분 동안 장치에 맡긴다
            if (w.Policies["firemethod"] == 1 && room.Suppression && room.Powered && w.Fire.BurningHours(room) < 0.75f) continue;
            // 불이 크면 두 사람 몫
            int crews = count >= 4 ? 2 : 1;
            for (int slot = 0; slot < crews; slot++)
                Post(WorkKind.Extinguish, WorkTarget.FireIn(room, hottest, slot), (critical ? 1.2f : 1.1f) - 0.05f * slot, Skill.Mechanics, $"불 {count}칸");
        }

        psc = Prof.Lap("scan.hull·doors·fire", psc);
        // ── 부상자 치료 ──
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.CarriedBy != null) continue;
            // 쓰러진 사람: 치료 침대에 눕혀지기 전이면 먼저 옮긴다
            if (c.Down && c.CareBed == null)
            {
                bool danger = c.Room == null || Atmosphere.Danger(c.Room) > 0.2f || c.Room.Leaking || w.Fire.AnyWithin(c.Cell, 2.5f);
                // 안전한 곳에 눕혀 두었고 빈 치료 침대가 없으면, 침대가 날 때까지 그대로 둔다 (같은 사람을 계속 옮기지 않게)
                if (!danger && c.LaidSafe && !ship.FurnitureOf(FurnitureType.MedBed).Any(b => b.ReservedBy == null && b.Machine!.Efficiency > 0f)) continue;
                // v13.2 방침(구조: 가망 있을 때만): 위험한 곳에서 살 가망이 낮은 사람은 들어가지 않는다 (분류)
                if (danger && w.Policies["rescue"] == 2 && (c.Vitals.Health < 0.15f || c.Vitals.Oxygen < 0.1f && c.Room is { } vr && WorkPlanners.Unsafe(vr))) continue;
                Post(WorkKind.Rescue, WorkTarget.OfCrew(c), danger ? 1.3f : 1.0f, Skill.Medicine,
                    $"{c.Room?.Name ?? "?"}에 쓰러짐 · 체력 {c.Vitals.Health * 100:0}%" + (danger ? " · 위험한 곳" : ""));
                continue;
            }
            bool resting = c.Job?.Activity is RecoverActivity || c.CareBed != null;
            bool needs = c.Vitals.Health < 0.6f || (resting && c.Vitals.Health < 0.8f) || c.Vitals.Injury >= 0.3f;
            // v13.2 방침(의약품: 아낀다): 크게 다친 사람에게만 구급 키트를 쓴다
            if (w.Policies["medicine"] == 0) needs = c.Vitals.Health < 0.45f || c.Vitals.Injury >= 0.45f;
            // v14.1 약을 써야 낫는 병 · 진단 안 된 병 (의무관이 봐 줘야 한다) — 다친 사람 치료와 같은 일 (한 번에 함께)
            var ill = c.Ailments.Count > 0 ? w.Ailments.NeedsMedic(c) : null;
            bool exam = c.Ailments.Count > 0 && ill == null && w.Ailments.Undiagnosed(c) && c.Fx.Worst > (w.Policies["medicine"] == 0 ? 0.45f : 0.3f) && w.Tick - c.Vitals.TreatedTick > SimTime.Hours(8);
            bool sick = ill != null && (w.Policies["medicine"] != 0 || w.Ailments.Severity(ill) > 0.4f) || exam;
            if (sick && !(needs && w.Tick - c.Vitals.TreatedTick > SimTime.Hours(c.Vitals.Injury >= 0.3f ? 6 : 3)))
                Post(WorkKind.Treat, WorkTarget.OfCrew(c), 0.5f + 0.4f * c.Fx.Worst, Skill.Medicine,
                    ill != null ? $"{AilmentSystem.Spec(ill.Id).Name} {w.Ailments.Severity(ill) * 100:0}% — 약" : $"어딘가 아프다 {c.Fx.Worst * 100:0}% — 진찰");
            if (needs && w.Tick - c.Vitals.TreatedTick > SimTime.Hours(c.Vitals.Injury >= 0.3f ? 6 : 3))
                Post(WorkKind.Treat, WorkTarget.OfCrew(c), 0.6f + MathF.Max(0.6f - c.Vitals.Health, c.Vitals.Injury * 0.5f), Skill.Medicine,
                    c.Vitals.Injury >= 0.05f ? $"체력 {c.Vitals.Health * 100:0}% · 부상 {c.Vitals.Injury * 100:0}% ({c.Vitals.InjuryCause})" : $"체력 {c.Vitals.Health * 100:0}%");
        }

        psc = Prof.Lap("scan.treat", psc);
        // ── 식량 ──
        int crewCount = Math.Max(1, w.Crew.Count(c => !c.Dead));
        int meals = ship.CountStored(ItemKind.Meal);
        int produce = ship.CountStored(ItemKind.Produce);
        // v10.4: 조리대가 여럿이면 모자란 만큼 여럿이 동시에 요리한다 (큰 배)
        int si = 0;
        // v11.3: 조리사가 근무 중이면 비번 동안 먹을 것까지 미리 해 둔다 (한 사람이 하루 내내 부엌을 지킬 수는 없다)
        //   조리사가 비번이면 다른 사람은 모자랄 때만 (한 끼 반치 아래) 부엌에 선다
        bool cookOn = w.Crew.Any(x => x.Role == CrewRole.Cook && !x.Dead && !x.Down && x.Pose != Pose.Sleeping && ChoresActivity.OnShiftStatic(x, w));
        bool hasCook = w.Crew.Any(x => x.Role == CrewRole.Cook && !x.Dead && !x.Down && x.CareBed == null);
        float mealTarget = cookOn ? 4f : hasCook ? 1.5f : 3f;
        foreach (var stove in ship.FurnitureOf(FurnitureType.Stove).Where(s => !s.Machine!.Stopped && !s.Room.Abandoned && s.Room.WaterLinked && !s.Room.ValveShut)) // 단수면 요리를 못 한다
        {
            if (meals + si * FoodChain.MealsPerBatch >= mealTarget * crewCount || produce < (si + 1) * FoodChain.ProducePerBatch) break;
            float lack = MathF.Min(1f, 1f - meals / (3f * crewCount) + (cookOn ? 0.1f : 0f));
            Post(WorkKind.Cook, WorkTarget.Of(stove), 0.3f + 0.6f * lack - 0.05f * si, Skill.Cooking, $"식사 {meals}인분 남음");
            si++;
        }
        int fridgeMeals = ship.FurnitureOf(FurnitureType.Fridge).Sum(f => f.Storage!.Count(ItemKind.Meal));
        foreach (var d in ship.FurnitureOf(FurnitureType.MealDispenser))
        {
            int n = d.Storage!.Count(ItemKind.Meal);
            if (n < 6 && fridgeMeals > 0)
                Post(WorkKind.Restock, WorkTarget.Of(d), 0.35f + 0.3f * (1f - n / 6f), Skill.Cooking, $"{n}인분 남음");
        }

        // ── 제작과 정제 (v6): 작업대(부품·소모품)와 정제기(원료 → 기본 수리재), 얼음 → 공기 탱크 ──
        ScanFabrication(Post, crewCount, produce, meals);
        psc = Prof.Lap("scan.Fabrication", psc);

        // ── 진화 (v7): 평화롭고 재료가 남으면, 겪은 사고가 가르쳐 준 곳을 고쳐 짠다 ──
        foreach (var busy in _open.Values.Where(o => o.Kind == WorkKind.Upgrade && o.Assignee != null).ToList())
            Post(busy.Kind, busy.Target, busy.Urgency, busy.Skill, busy.Detail, circuit: busy.Circuit, minSkill: busy.MinSkill, upgrade: busy.Upgrade);
        if (!_open.Values.Any(o => o.Kind == WorkKind.Upgrade && o.Assignee != null) && Evolution.Plan(w) is UpgradePlan plan)
            Post(WorkKind.Upgrade, plan.Target, plan.Score, plan.Skill, plan.Why, circuit: plan.Circuit, minSkill: plan.MinSkill, upgrade: plan.Kind);

        // ── 구조와 외부 작업 (v8): 연결부·골조·드론·사출·되찾기·재연결 ──
        ScanStructure(Post);
        psc = Prof.Lap("scan.Structure", psc);
        ScanPiping(Post); // v9
        psc = Prof.Lap("scan.Piping", psc);
        ScanRobots(Post); // v10.10 선내 로봇 (수리·끌어오기·정비)
        psc = Prof.Lap("scan.Robots", psc);
        ScanRecovery(Post); // v10.10 자원 회복 (물통·비상 물자·정리)
        psc = Prof.Lap("scan.Recovery", psc);
        ScanPrevention(Post); // v11.0 예방과 안전
        psc = Prof.Lap("scan.Prevention", psc);
        ScanCalibration(Post); // v12.0 감지기 교정
        psc = Prof.Lap("scan.Calibration", psc);
        ScanHandover(Post); // v12.0 찾아가 인수인계
        psc = Prof.Lap("scan.Handover", psc);
        ScanVolatile(Post); // v12.2 식히기·잔해·청소·역화·산소관
        psc = Prof.Lap("scan.Volatile", psc);
        ScanWake(Post); // 위기에 잠든 동료 깨우기
        psc = Prof.Lap("scan.Wake", psc);
        ScanLinks(Post); // v12.1 설비 전선·관
        psc = Prof.Lap("scan.Links", psc);
        ScanNet(Post); // 배 전체 망
        psc = Prof.Lap("scan.Net", psc);
        ScanMoisture(Post); // v12.3 침수·분전함·밸브
        psc = Prof.Lap("scan.Moisture", psc);
        ScanManualControl(Post); // v12.5 관제석 수동 조종
        psc = Prof.Lap("scan.ManualControl", psc);
        ScanSafetyWatch(Post); // v13.1 2인 1조
        psc = Prof.Lap("scan.SafetyWatch", psc);
        ScanNavigation(Post); // v11.2 항로와 추진
        psc = Prof.Lap("scan.Navigation", psc);
        ScanHazards(Post); // v11.2 사고 뒷정리 (오염된 식사)
        psc = Prof.Lap("scan.Hazards", psc);
        ScanLiving(Post); // v10.11 배급
        psc = Prof.Lap("scan.Living", psc);
        ScanComms(Post); // v11.2 외부 교신
        psc = Prof.Lap("scan.Comms", psc);
        ScanGrowth(Post); // v11.3 배우기 · 재활
        psc = Prof.Lap("scan.Growth", psc);
        ScanLife(Post); // v12.7 시신 수습 · 의수·의족
        psc = Prof.Lap("scan.Life", psc);

        // ── 결정 (v7): 사람이 정해야 하는 일은 심의에 올린다 ──
        Council.Review(w, _open.Values.Where(o => seen.Contains(o.Key)).ToList());

        // 조건이 사라진 일은 내린다 (누가 하고 있으면 그 사람이 끝낼 때까지 둔다)
        foreach (var key in _open.Keys.ToList())
        {
            if (seen.Contains(key)) continue;
            var o = _open[key];
            if (o.Assignee == null && o.Drone == null && o.Robot == null) { o.Closed = true; _open.Remove(key); }
        }
    }
}
