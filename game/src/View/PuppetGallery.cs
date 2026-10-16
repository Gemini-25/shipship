using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v17.1 화면 확인용 (--puppets): 한 방에 승무원을 세워 자세 · 든 것 · 머리 · 수염 · 다친 팔 · 우주복을 한눈에 본다.
/// 시뮬레이션 상태를 바꾸므로 화면 시험(--pause와 함께) 전용 — --break · --injure와 같은 디버그 옵션.
/// </summary>
public static class PuppetGallery
{
    public static Vector2? Setup(World w)
    {
        var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Lounge or RoomType.Galley or RoomType.Workshop or RoomType.Hydroponics)
            .OrderBy(r => r.Type is RoomType.Mess or RoomType.Lounge ? 0 : 1).ThenByDescending(r => r.Cells.Count(w.Ship.IsOpenFloor)).FirstOrDefault();
        if (room == null) return null;
        var spots = room.Cells.Where(cell => w.Ship.IsOpenFloor(cell) && (cell.X - room.MinX) % 2 == 1 && (cell.Y - room.MinY) % 2 == 1)
            .OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToList();
        int i = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || i >= spots.Count) continue;
            var at = spots[i];
            if (c.Job != null) c.EndJob(w, ToilStatus.Interrupted);
            c.Path = null; c.Position = at.Center; c.PreviousPosition = at.Center; c.Facing = new System.Numerics.Vector2(1f, 0f);
            c.Pose = Pose.Standing; c.Carrying = null; c.Suit = null; c.Dashing = false;
            var l = w.Body2.Of(c);
            switch (i % 12)
            {
                case 0: c.Carrying = new ItemStack(ItemKind.Structure, 2); break; // 양손 짐
                case 1: c.Pose = Pose.Walking; c.Path = new List<Cell> { at }; c.Facing = new System.Numerics.Vector2(0f, 1f); l.HairCm = BodyLook.ShagAt(l.StyleCm) * 1.5f; break; // 걷기 · 덥수룩
                case 2: c.Pose = Pose.Walking; c.Path = new List<Cell> { at }; c.Dashing = true; break; // 뛰기
                case 3: c.Pose = Pose.Sitting; c.Carrying = new ItemStack(ItemKind.Meal, 1); break; // 앉아 접시
                case 4: c.Pose = Pose.Working; break; // 무릎 꿇기 (다음 사람이 쓰러졌다)
                case 5: c.Down = true; c.Vitals.Health = 0.1f; c.Pose = Pose.Down; c.Position = spots[i - 1].Center + new System.Numerics.Vector2(1f, 0f); break;
                case 6: c.Vitals.Wounds.Add(new Wound { Part = BodyPart.RightArm, Kind = WoundKind.Fracture, Weight = 1f, Cause = "화면 시험" }); c.Vitals.Injury = 0.45f; c.Carrying = new ItemStack(ItemKind.Extinguisher, 1); break;
                case 7: c.Suit = new SuitState(); l.Kg = l.SuitKg + 8f; break; // 꽉 끼는 우주복
                case 8: c.Carrying = new ItemStack(ItemKind.MedKit, 1); c.Facing = new System.Numerics.Vector2(0f, -1f); break;
                case 9: l.Uneven = 0.9f; l.LastCut = CutResult.Botched; if (l.Stubbly) l.BeardMm = 16f; break; // 삐뚤빼뚤 · 수염
                case 10: c.Carrying = new ItemStack(ItemKind.Electronics, 1); c.Pose = Pose.Walking; c.Path = new List<Cell> { at }; c.Facing = new System.Numerics.Vector2(-1f, 0f); break;
                default: c.Vitals.Wounds.Add(new Wound { Part = BodyPart.LeftArm, Kind = WoundKind.Crush, Weight = 1f, Cause = "화면 시험", Lost = true }); c.Carrying = new ItemStack(ItemKind.Ration, 1); break; // 잃은 팔
            }
            i++;
        }
        return ShipView.ToPx(room.Center);
    }
}
