using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>v10.4 배 크기 템플릿 하나: 이름, 설계 인원, 설계도.</summary>
public sealed record ShipTemplate(string Key, string Name, int Crew, string Ascii, string Note);

/// <summary>
/// v10.4 배 크기 템플릿. 사람이 많으면 배도 커진다 — 방이 넓어지고, 원자로가 커지고(칸 수만큼 출력), 냉각 펌프·배터리·정수기·산소 발생기·
/// 재배대·침대·선반·작업대·조리대가 인원에 맞춰 늘어난다. 미리내호(6인)만 손으로 그렸고, 나머지는 같은 뼈대로 생성기(tools/shipgen)가 뽑았다.
/// </summary>
public static class ShipCatalog
{
    public static readonly ShipTemplate[] All =
    {
        new("Kestrel", ShipBlueprints.KestrelName, 4, ShipBlueprints.Kestrel, "소형 · 원자로 3×3 · 펌프 2 · 재배대 3"),
        new("Mirinae", ShipBlueprints.MirinaeName, 6, ShipBlueprints.Mirinae, "기본 · 원자로 3×3 · 펌프 2 · 재배대 4 (손으로 그린 배)"),
        new("Hanbit", ShipBlueprints.HanbitName, 12, ShipBlueprints.Hanbit, "중형 · 원자로 4×4 · 펌프 4 · 재배대 8"),
        new("Eunha", ShipBlueprints.EunhaName, 20, ShipBlueprints.Eunha, "대형 · 원자로 5×5 · 펌프 6 · 재배대 14"),
        new("Cheonma", ShipBlueprints.CheonmaName, 30, ShipBlueprints.Cheonma, "초대형 · 원자로 6×6 · 펌프 8 · 재배대 20"),
    };

    public static ShipTemplate Default => All[1];

    public static ShipTemplate? Find(string? key) => key == null ? null : All.FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>그 인원이 탈 수 있는 가장 작은 배 (30명 넘으면 가장 큰 배).</summary>
    public static ShipTemplate ForCrew(int crew) => All.FirstOrDefault(t => t.Crew >= crew) ?? All[^1];
}
