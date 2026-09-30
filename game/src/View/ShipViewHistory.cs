using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v7 역사와 진화의 모습.
/// - 늘 보이는 것 (몸에 붙은 개조): 외벽 보강판, 천장의 자동 소화 장치, Mk.3 딱지, 예비 배선.
/// - "흔적" 보기 (V): 벽마다 뚫리고 막고 용접하고 갈아 끼운 자국, 포기했던 방의 빗금, 불난 방의 그을음,
///   사람이 쓰러지고 죽은 자리, 자주 고장 난 설비. 승무원을 고르면 그 사람이 무서워하는 방이 보라색으로 물든다.
/// </summary>
public partial class ShipView
{
    public static readonly Color SteelColor = new("#8fb3cf");
    public static readonly Color FeederColor = new("#6fa8dc");
    public static readonly Color Mk3Color = new("#5fd4e8");
    public static readonly Color FearColor = new("#b07cff");

    // ─────────────────────────── 늘 보이는 개조 ───────────────────────────

    private void PaintEvolution(CanvasItem ci)
    {
        // 외벽 보강판: 우주 쪽에 덧댄 두꺼운 판과 리벳
        foreach (var (cell, wall) in _world.Ship.Walls)
        {
            if (!wall.Reinforced) continue;
            var r = CellRect(cell);
            var outDir = OutwardOf(cell);
            var plate = r.Grow(-2f);
            if (outDir != Vector2.Zero) plate.Position += outDir * 3f;
            ci.DrawRect(plate, SteelColor.WithAlpha(0.22f));
            ci.DrawRect(plate, SteelColor.WithAlpha(0.75f), false, 1.5f);
            foreach (var p in new[] { plate.Position + new Vector2(4, 4), new Vector2(plate.End.X - 4, plate.Position.Y + 4), plate.End - new Vector2(4, 4), new Vector2(plate.Position.X + 4, plate.End.Y - 4) })
                ci.DrawCircle(p, 1.4f, SteelColor.Lightened(0.3f).WithAlpha(0.9f), true, -1f, true);
        }

        // 자동 소화 장치: 천장 배관과 노즐 (불이 나면 물안개)
        foreach (var room in _world.Ship.LiveRooms)
        {
            if (!room.Suppression) continue;
            bool spraying = room.Powered && _world.Fire.CountIn(room) > 0;
            float y = (room.MinY + 0.18f) * T;
            float x0 = (room.MinX + 0.3f) * T, x1 = (room.MaxX + 0.7f) * T;
            ci.DrawLine(new Vector2(x0, y), new Vector2(x1, y), new Color("#c8d6e4").WithAlpha(0.35f), 2f);
            for (int x = room.MinX + 1; x <= room.MaxX; x += 3)
            {
                var n = new Vector2((x + 0.5f) * T, y);
                ci.DrawCircle(n, 2.6f, new Color("#d8e4f0").WithAlpha(0.8f), true, -1f, true);
                ci.DrawCircle(n, 1.2f, new Color("#3a8fd9"), true, -1f, true);
                if (!spraying) continue;
                for (int k = 0; k < 5; k++)
                {
                    float ph = Mathf.PosMod(_time * 1.6f + k * 0.2f + x * 0.37f, 1f);
                    var d = new Vector2((k - 2) * 0.35f, 1f).Normalized();
                    ci.DrawCircle(n + d * (4f + ph * T * 1.4f), 1.5f + ph * 2.5f, new Color("#9ad0ff").WithAlpha(0.45f * (1f - ph)), true, -1f, true);
                }
            }
        }

        // Mk.3 개량형: 청록 딱지와 개량 판
        foreach (var f in _world.Ship.Furniture.Where(f => !f.Room.Detached))
        {
            if (f.Machine is not { Grade: MachineGrade.Mk3 }) continue;
            var r = FurnitureRect(f).Grow(-2f);
            ci.DrawRect(r, Mk3Color.WithAlpha(0.5f), false, 1.2f);
            var tag = new Rect2(r.Position.X + 2, r.Position.Y + 2, 24, 11);
            Gfx.RoundRect(ci, tag, Mk3Color.WithAlpha(0.95f), 3);
            Gfx.TextCentered(ci, Fonts.Bold, tag.GetCenter(), "Mk.3", 8, new Color("#0b1a1f"));
        }
    }

    /// <summary>예비 배선: 배전반 위를 가로지르는 가지런한 굵은 전선 (평소엔 쉬고, 회로가 끊기면 불이 들어온다).</summary>
    private void PaintFeeder(CanvasItem ci, Furniture panel, Jumper j, int index)
    {
        var a = PanelSwitch(panel, j.From);
        var b = PanelSwitch(panel, j.To);
        float lift = 12f + index * 5f;
        var pts = new[] { a, a - new Vector2(0, lift), b - new Vector2(0, lift), b };
        var col = j.Burnt ? new Color("#2a2220") : j.Active ? FeederColor.Lightened(0.25f) : FeederColor.WithAlpha(0.55f);
        ci.DrawPolyline(pts, new Color(0, 0, 0, 0.4f), 5f, true);
        ci.DrawPolyline(pts, col, 3f, true);
        // 고정 브래킷
        for (int k = 0; k < 3; k++)
        {
            var p = pts[1].Lerp(pts[2], (k + 1) / 4f);
            ci.DrawRect(new Rect2(p.X - 2f, p.Y - 3f, 4f, 6f), new Color("#c8d6e4").WithAlpha(0.7f));
        }
        if (j.Active)
        {
            float ph = Mathf.PosMod(_time * 0.8f, 1f);
            var p = ph < 0.5f ? pts[1].Lerp(pts[2], ph * 2f) : pts[2].Lerp(pts[3], (ph - 0.5f) * 2f);
            ci.DrawCircle(p, 3f, FeederColor.Lightened(0.5f), true, -1f, true);
        }
    }

    // ─────────────────────────── 흔적 보기 ───────────────────────────

    private void PaintTraceOverlay(CanvasItem ci)
    {
        var ship = _world.Ship;
        // 바탕을 가라앉혀 흔적이 도드라지게
        foreach (var room in ship.LiveRooms)
            foreach (var c in room.Cells) ci.DrawRect(CellRect(c), new Color(0.02f, 0.03f, 0.05f, 0.45f));

        // 선택한 사람의 공포 지도
        var who = _main.SelectedCrew;
        if (who != null)
            foreach (var room in ship.LiveRooms)
            {
                float fear = who.Memory.FearOf(room);
                if (fear < 0.05f) continue;
                foreach (var c in room.Cells) ci.DrawRect(CellRect(c), FearColor.WithAlpha(0.12f + 0.45f * fear));
            }

        foreach (var room in ship.LiveRooms)
        {
            // 포기했던 방: 빗금
            if (room.TimesAbandoned > 0)
            {
                var col = new Color(0.75f, 0.78f, 0.85f, room.Abandoned ? 0.22f : 0.12f);
                for (float s = (room.MinX + room.MinY) * T; s < (room.MaxX + room.MaxY + 2) * T; s += 14f)
                    foreach (var c in room.Cells)
                    {
                        var r = CellRect(c);
                        // 이 칸을 지나는 대각선 조각만
                        float d0 = r.Position.X + r.Position.Y, d1 = r.End.X + r.End.Y;
                        if (s < d0 || s > d1) continue;
                        var p0 = new Vector2(Mathf.Clamp(s - r.End.Y, r.Position.X, r.End.X), 0);
                        p0.Y = s - p0.X;
                        var p1 = new Vector2(Mathf.Clamp(s - r.Position.Y, r.Position.X, r.End.X), 0);
                        p1.Y = s - p1.X;
                        ci.DrawLine(p0, p1, col, 1.5f);
                    }
            }
            // 불난 방: 그을음 (횟수만큼 짙게)
            if (room.Fires > 0)
                foreach (var c in room.Cells)
                    if (Hash(c.X, c.Y, 7) < 0.25f + 0.15f * room.Fires)
                        ci.DrawCircle(CellRect(c).GetCenter(), T * (0.3f + 0.2f * Hash(c.X, c.Y, 8)), new Color(0.35f, 0.18f, 0.08f, 0.22f), true, -1f, true);
            // 사람이 쓰러진 곳, 죽은 곳
            var mid = ToPx(room.Center);
            for (int k = 0; k < room.Collapses; k++)
            {
                var p = mid + new Vector2(-T * 0.8f + k * 10f, T * 0.6f);
                ci.DrawLine(p - new Vector2(3, 3), p + new Vector2(3, 3), Palette.Danger.WithAlpha(0.8f), 2f);
                ci.DrawLine(p + new Vector2(-3, 3), p + new Vector2(3, -3), Palette.Danger.WithAlpha(0.8f), 2f);
            }
            for (int k = 0; k < room.Deaths; k++)
            {
                var p = mid + new Vector2(T * 0.5f + k * 12f, T * 0.5f);
                ci.DrawRect(new Rect2(p.X - 1f, p.Y - 7f, 2f, 12f), new Color("#e6eaf2").WithAlpha(0.85f));
                ci.DrawRect(new Rect2(p.X - 4f, p.Y - 4f, 8f, 2f), new Color("#e6eaf2").WithAlpha(0.85f));
            }
        }

    }

    /// <summary>흔적 보기의 윗층: 벽과 설비의 이력, 전우.</summary>
    private void PaintTraceMarks(CanvasItem ci)
    {
        var ship = _world.Ship;
        var who = _main.SelectedCrew;
        // 벽의 이력: 뚫림(빨강 점), 봉합(노랑), 용접(주황 눈금), 교체(파랑 테두리)
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull || (wall.Breaches == 0 && wall.Seals == 0 && wall.TotalWelds == 0 && wall.Replacements == 0)) continue;
            var r = CellRect(cell);
            var c = r.GetCenter();
            if (wall.Replacements > 0) ci.DrawRect(r.Grow(-1.5f), new Color("#7cc4ff").WithAlpha(0.8f), false, 1.5f);
            for (int k = 0; k < wall.Breaches && k < 4; k++)
                ci.DrawCircle(c + new Vector2(-7f + k * 5f, -6f), 2.4f, Palette.Danger, true, -1f, true);
            if (wall.Seals > 0) ci.DrawCircle(c + new Vector2(6f, 5f), 3.2f, new Color("#f2d24b").WithAlpha(0.85f), true, -1f, true);
            for (int k = 0; k < wall.TotalWelds && k < 4; k++)
                ci.DrawLine(c + new Vector2(-8f + k * 4f, 5f), c + new Vector2(-6f + k * 4f, 10f), new Color("#ff9a3c"), 1.6f);
        }

        // 설비의 이력: 고장 횟수 링 (자주 고장 난 설비일수록 짙게), Mk.1을 거친 설비는 노란 점, 뜯긴 적 있는 설비는 회색 점
        foreach (var f in ship.Furniture.Where(f => !f.Room.Detached))
        {
            if (f.Machine is not Machine m) continue;
            var r = FurnitureRect(f);
            if (m.FaultCount > 0)
            {
                float a = Mathf.Clamp(m.FaultCount / 6f, 0.2f, 1f);
                ci.DrawRect(r.Grow(-1f), Palette.Warning.WithAlpha(0.35f + 0.5f * a), false, 1f + 2f * a);
            }
            var dot = new Vector2(r.End.X - 6f, r.End.Y - 6f);
            if (m.Substitutions > 0) { ci.DrawCircle(dot, 3.5f, Hazard, true, -1f, true); dot.X -= 9f; }
            if (m.TimesStripped > 0) { ci.DrawCircle(dot, 3.5f, new Color("#9aa3b5"), true, -1f, true); dot.X -= 9f; }
            if (m.Rebuilds > 0) ci.DrawCircle(dot, 3.5f, new Color("#ff7a5c"), true, -1f, true);
        }

        // 전우: 선택한 사람과 전우 사이에 가는 선
        if (who != null)
            foreach (var id in who.Memory.Comrades)
            {
                var o = _world.Crew[id];
                if (o.Dead) continue;
                ci.DrawDashedLine(CrewPx(who), CrewPx(o), new Color("#6ee7b7").WithAlpha(0.6f), 2f, 8f, true, true);
            }
    }
}
