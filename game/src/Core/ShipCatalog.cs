using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>v10.4 배 크기 템플릿 하나: 이름, 설계 인원, 설계도.</summary>
public sealed record ShipTemplate(string Key, string Name, int Crew, string Ascii, string Note, ShipInfo? Info = null)
{
    /// <summary>v16.9 내력이 없는 예전 배 (제비 · 미리내 · 한빛 · 은하 · 천마 · 옛 생성 키) — 시작 상태를 입히지 않는다.</summary>
    public bool Legacy => Info == null;
    /// <summary>v16.9 내력 (예전 배는 일반 · 직선형 · 민간 · 중고로 본다).</summary>
    public ShipInfo Meta => Info ?? new ShipInfo(ShipPurpose.General, ShipFrame.Linear, ShipDesigner.Civilian, ShipStart.Used, ShipInfos.Year - 12,
        Pros: "익숙한 배치 · 무난하다", Cons: "특별한 것이 없다", Difficulty: Crew <= 6 ? 2 : 3);
}

/// <summary>
/// v10.4 배 크기 템플릿 · v16.22 크기 등급(ShipClasses): 작을수록 꼭 필요한 방만, 클수록 방 종류가 많고 호화롭다. 원자로가 커지고(칸 수만큼 출력), 냉각 펌프·배터리·정수기·산소 발생기·
/// 재배대·침대·선반·작업대·조리대가 인원에 맞춰 늘어난다. 기본 배 다섯 척은 같은 뼈대(심장부 안쪽 · 주컴퓨터실 한가운데)로 생성기(tools/shipgen)가 뽑았다.
/// </summary>
public static class ShipCatalog
{
    public static readonly ShipTemplate[] All =
    {
        new("Kestrel", ShipBlueprints.KestrelName, 4, ShipBlueprints.Kestrel, "소형 · 꼭 필요한 방만, 좁고 알뜰하다 · 냉동 창고의 저장 식량"),
        new("Mirinae", ShipBlueprints.MirinaeName, 6, ShipBlueprints.Mirinae, "기본형 · 휴게실 · 대피소 · 조류 배양실 · 선외 준비실"),
        new("Hanbit", ShipBlueprints.HanbitName, 12, ShipBlueprints.Hanbit, "중형 · 차압 문 두 구획 · 수경 · 조류 · 버섯 · 연료전지 · 펌프실"),
        new("Eunha", ShipBlueprints.EunhaName, 20, ShipBlueprints.Eunha, "대형 · 연구실 · 서버실 · 보안실 · 정원 · 관측실 · 개인 선실"),
        new("Cheonma", ShipBlueprints.CheonmaName, 30, ShipBlueprints.Cheonma, "초대형 · 극장 · 학교 · 원심 거주구 · 셔틀 격납고"),
        // v16.9 대표 배 6척 (손으로 그린 배 · 뼈대 · 용도 · 설계사 · 시작 상태가 저마다 다르다)
        ShipBlueprints.SaeteoShip, ShipBlueprints.BusitdolShip, ShipBlueprints.BodeumShip,
        ShipBlueprints.NareumiShip, ShipBlueprints.TtaemjilShip, ShipBlueprints.PabalShip,
    };

    public static ShipTemplate Default => All[1];

    public static ShipTemplate? Find(string? key) => key == null ? null
        : key.StartsWith("gen:", StringComparison.OrdinalIgnoreCase) ? ShipGenerator.FromKey(key.ToLowerInvariant()) // v12.6 절차 생성 배 ("gen:인원:시드" · v16.9 "gen:용도:뼈대:인원:시드")
        : All.FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>그 인원이 탈 수 있는 가장 작은 배 (30명 넘으면 가장 큰 배).</summary>
    public static ShipTemplate ForCrew(int crew) => All.FirstOrDefault(t => t.Legacy && t.Crew >= crew)
        ?? All.Where(t => t.Crew >= crew).OrderBy(t => t.Crew).FirstOrDefault() ?? All.OrderBy(t => t.Crew).Last(); // v16.9 30명 넘으면 큰 새 배
}
