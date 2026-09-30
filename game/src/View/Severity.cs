using System.Linq;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 사고 화면의 시각 위계. 치명(방 전체 하나의 상태로 크게) → 긴급(작은 배지) → 일반(평소 그대로).
/// 포기한 구획은 "끝난 상태"라 따로 조용한 모양으로 그린다.
/// </summary>
public enum RoomSeverity { Normal, Urgent, Critical, Abandoned }

public static class Severity
{
    public static RoomSeverity Of(World w, Room r)
    {
        if (r.Abandoned) return RoomSeverity.Abandoned;
        bool fire = w.Fire.IsKnown(r) && w.Fire.CountIn(r) > 0;
        if (fire || r.Unbreathable) return RoomSeverity.Critical;
        if (r.Leaking || r.Lockdown || r.Air.Smoke > 0.25f || r.Air.Toxin > 0.2f || w.Crew.Any(c => c.Down && !c.Dead && c.CareBed == null && c.CarriedBy == null && c.Room == r))
            return RoomSeverity.Urgent;
        return RoomSeverity.Normal;
    }

    /// <summary>치명 상태 이름과 부제 (방 한가운데 크게).</summary>
    public static (string title, string sub) CriticalLabel(World w, Room r)
    {
        int fires = w.Fire.CountIn(r);
        if (w.Fire.IsKnown(r) && fires > 0)
            return ("화재", $"불 {fires}칸 · 연기 {r.Air.Smoke * 100:0}%");
        int breaches = w.Ship.Walls.Count(kv => kv.Value.IsHull && kv.Value.Breach > 0f && !kv.Value.Patched && Hull.InsideRoom(w.Ship, kv.Key) == r);
        return ("진공", breaches > 0 ? $"{r.Air.Pressure:0}kPa · 새는 곳 {breaches}" : $"{r.Air.Pressure:0}kPa");
    }

    /// <summary>지금 우주선이 위기 상황인지 (치명 방이 있거나 쓰러진 사람이 있다).</summary>
    public static bool Crisis(World w) =>
        w.Ship.Rooms.Any(r => Of(w, r) == RoomSeverity.Critical) || w.Crew.Any(c => c.Down && !c.Dead);

    /// <summary>위기 때 라벨을 남겨 둘 승무원: 사고 대응 중, 위험한 곳, 쓰러짐, 업고 가는 중.</summary>
    public static bool Notable(CrewMember c) =>
        c.Down || c.Dead || c.CarryingPerson != null || c.Suit != null
        || c.Job?.Urgent == true
        || (c.Job?.Order is WorkOrder o && WorkKinds.IsEmergency(o.Kind));
}
