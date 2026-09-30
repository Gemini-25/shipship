using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v8 외부 작업과 구조: 구조 연결부(외벽 바깥의 브래킷), 떨어져 나가 떠다니는 방, 드론, EVA 안전줄, 구조 보기.
/// </summary>
public partial class ShipView
{
    private static readonly Color JointSteel = new("#8b97a8");
    private static readonly Color JointWarn = new("#f5c24a");
    private static readonly Color JointBad = new("#ff6b4a");
    private static readonly Color TrussColor = new("#9fd49a");
    private static readonly Color TetherColor = new("#e8d9a8");

    public static Color DroneColor(DroneKind k) => k switch
    {
        DroneKind.Inspect => new Color("#7fe3ff"),
        DroneKind.Repair => new Color("#ffd166"),
        DroneKind.Tow => new Color("#ff9a5c"),
        _ => new Color("#9fe38a"),
    };

    private int _structureVersion = -1;

    /// <summary>방이 떨어져 나가거나 다시 붙으면 정적 레이어(벽·바닥·가구)와 외곽선을 다시 만든다.</summary>
    private void CheckStructureChanged()
    {
        int v = _world.Structure.Version;
        // v10.2: 칸막이로 방이 늘면 (시작 인자로 곧바로 나눈 경우 포함) 이름표·외곽선도 다시
        bool roomsChanged = _outlines.Count != _world.Ship.Rooms.Count;
        if (v == _structureVersion && !roomsChanged) return;
        if (_structureVersion >= 0 || roomsChanged)
        {
            _outlines.Clear();
            BuildOutlines();
            BuildLabelAnchors();
            RedrawStatic();
        }
        _structureVersion = v;
    }

    private static Vector2 Px(System.Numerics.Vector2 v) => new(v.X * T, v.Y * T);

    // ─────────────────────────────── 연결부 ───────────────────────────────

    /// <summary>
    /// 외벽 바깥면의 연결부 브래킷. 아는 강도로 색을 칠한다 (모르는 손상은 보이지 않는다 — 검사해야 안다).
    /// 운석을 맞고 아직 밖에서 못 본 방에는 물음표.
    /// </summary>
    private void PaintJoints(CanvasItem ci, bool structureView)
    {
        var st = _world.Structure;
        foreach (var room in _world.Ship.Rooms)
        {
            if (room.Detached) continue;
            bool unseen = st.Unseen.ContainsKey(room.Id);
            foreach (var j in room.Joints) PaintJoint(ci, j, structureView, 1f);
            if (unseen && room.Joints.Count > 0)
            {
                var j0 = room.Joints[0];
                var p = CellRect(j0.Cell).GetCenter() + new Vector2(j0.Out.X, j0.Out.Y) * T * 0.95f;
                float pulse = 0.55f + 0.45f * Mathf.Sin(_time * 3f);
                ci.DrawCircle(p, 7f, new Color(0.1f, 0.08f, 0.02f, 0.85f), true, -1f, true);
                ci.DrawArc(p, 7f, 0f, Mathf.Tau, 20, JointWarn.WithAlpha(pulse), 1.5f, true);
                Gfx.TextCentered(ci, Fonts.Bold, p, "?", 10, JointWarn.WithAlpha(pulse));
            }
        }
    }

    private void PaintJoint(CanvasItem ci, Joint j, bool emphasize, float alpha)
    {
        var r = CellRect(j.Cell);
        var c = r.GetCenter();
        var outDir = new Vector2(j.Out.X, j.Out.Y).Normalized();
        if (outDir == Vector2.Zero) outDir = Vector2.Up;
        var side = new Vector2(-outDir.Y, outDir.X);
        var face = c + outDir * (T * 0.5f + 2f); // 벽 바깥면
        float k = j.Known;
        bool broken = j.KnownBroken;
        Color col = j.Released ? new Color("#7a8494") : broken ? JointBad : k < 0.35f ? JointBad : k < 0.7f ? JointWarn : j.Truss ? TrussColor : JointSteel;
        float a = alpha * (emphasize || broken || k < 0.7f || j.Truss ? 1f : 0.55f);
        float len = emphasize ? 13f : 10f;

        if (j.Truss)
        {
            // 임시 트러스: 선체 밖으로 삐져나온 격자 보
            var p0 = face - side * len * 0.6f;
            var p1 = face + side * len * 0.6f;
            var q0 = p0 + outDir * 7f;
            var q1 = p1 + outDir * 7f;
            ci.DrawLine(p0, p1, col.WithAlpha(a), 2f, true);
            ci.DrawLine(q0, q1, col.WithAlpha(a), 2f, true);
            ci.DrawLine(p0, q1, col.WithAlpha(a * 0.8f), 1.3f, true);
            ci.DrawLine(p1, q0, col.WithAlpha(a * 0.8f), 1.3f, true);
            return;
        }
        if (broken && !j.Released)
        {
            // 끊어진 연결부: 부러진 브래킷 두 동강과 X
            var b0 = face - side * len * 0.5f;
            var b1 = face + side * len * 0.5f;
            ci.DrawLine(b0, b0 + side * 4f + outDir * 3f, col.WithAlpha(a), 2.5f, true);
            ci.DrawLine(b1, b1 - side * 4f - outDir * 2f, col.WithAlpha(a), 2.5f, true);
            ci.DrawLine(face + new Vector2(-3.5f, -3.5f), face + new Vector2(3.5f, 3.5f), col.WithAlpha(a), 2f, true);
            ci.DrawLine(face + new Vector2(-3.5f, 3.5f), face + new Vector2(3.5f, -3.5f), col.WithAlpha(a), 2f, true);
            return;
        }
        // 브래킷: 벽 바깥면에 붙은 판 + 볼트 둘 (풀어 놓았으면 볼트가 빠져 있다)
        var plateA = face - side * len * 0.5f;
        var plateB = face + side * len * 0.5f;
        ci.DrawLine(plateA, plateB, new Color(0, 0, 0, 0.6f * a), 6f, true);
        ci.DrawLine(plateA, plateB, col.WithAlpha(a), 3.5f, true);
        if (!j.Released)
            foreach (var bolt in new[] { face - side * len * 0.28f, face + side * len * 0.28f })
                ci.DrawCircle(bolt, 1.6f, new Color(0.95f, 0.97f, 1f, 0.8f * a), true, -1f, true);
        // 약해진 곳은 브래킷을 따라 금
        if (k < 0.7f && !j.Released)
            ci.DrawLine(face - side * 2f + outDir * 3f, face + side * 3f - outDir * 1f, new Color(0.05f, 0.03f, 0.02f, 0.85f * a), 1.2f, true);
        if (emphasize && k < 0.7f)
            ci.DrawArc(face, len * 0.75f, 0f, Mathf.Tau, 20, col.WithAlpha(0.35f + 0.25f * Mathf.Sin(_time * 4f)), 1.2f, true);
    }

    // ─────────────────────────────── 떨어져 나간 방 ───────────────────────────────

    /// <summary>
    /// 떠다니는 조각: 원래 자리에서 밀려나며 천천히 돈다. 바닥·벽·가구를 그 변환으로 다시 그린다.
    /// 뜯겨 나간 가장자리는 붉은 선, 끌려오는 동안은 견인 드론과 줄로 이어진다.
    /// </summary>
    private void PaintFragments(CanvasItem ci)
    {
        foreach (var f in _world.Structure.Fragments)
        {
            if (f.State == FragmentState.Lost) continue;
            var room = f.Room;
            var off = System.Numerics.Vector2.Lerp(f.PreviousOffset, f.Offset, _main.Alpha);
            float ang = Mathf.Lerp(f.PreviousAngle, f.Angle, _main.Alpha);
            var center = Px(room.Center);
            var pos = center + Px(off) - center.Rotated(ang);
            ci.DrawSetTransform(pos, ang, Vector2.One);

            // 그림자처럼 번진 테두리 (선체 테두리 대신)
            foreach (var c in room.Cells) ci.DrawRect(CellRect(c).Grow(3f), Palette.HullRim.WithAlpha(0.8f));
            foreach (var (cell, _) in f.WallCells) ci.DrawRect(CellRect(cell).Grow(3f), Palette.HullRim.WithAlpha(0.8f));
            foreach (var c in room.Cells)
            {
                var r = CellRect(c);
                ci.DrawRect(r, Palette.RoomFloor(room.Type).Darkened(0.25f));
                var (tex, alpha) = Textures.Floor(room.Type);
                if (tex != null) ci.DrawTextureRectRegion(tex, r, Variant(c), new Color(1, 1, 1, alpha * 0.7f));
            }
            foreach (var (cell, wall) in f.WallCells)
            {
                var r = CellRect(cell);
                ci.DrawRect(r, Palette.Wall);
                if (Textures.Wall != null) ci.DrawTextureRectRegion(Textures.Wall, r, Variant(cell), new Color(1, 1, 1, 0.85f));
                if (wall.StageIndex > 0) PaintWallDamage(ci, cell, wall);
            }
            foreach (var d in f.Doors)
                ci.DrawRect(CellRect(d.Cell), Palette.Wall.Lightened(0.08f));
            foreach (var fu in room.Furniture) PaintFurniture(ci, fu);
            // 진공: 차갑게 가라앉은 색
            foreach (var c in room.Cells) ci.DrawRect(CellRect(c), new Color(0.02f, 0.04f, 0.08f, 0.2f));

            // 뜯겨 나간 가장자리: 원래 이웃과 맞닿았던 쪽 (조각에 벽이 없는 바닥 가장자리)
            var walls = new HashSet<Cell>(f.WallCells.Select(x => x.cell));
            var floor = new HashSet<Cell>(room.Cells);
            var torn = f.Jettisoned ? new Color("#a8b3c5").WithAlpha(0.6f) : new Color("#ff6b4a").WithAlpha(0.75f);
            foreach (var c in room.Cells)
                foreach (var d in Cell.Dirs4)
                {
                    var n = c + d;
                    if (floor.Contains(n) || walls.Contains(n)) continue;
                    var r = CellRect(c);
                    Vector2 a = d.X > 0 ? new Vector2(r.End.X, r.Position.Y) : d.X < 0 ? r.Position : d.Y > 0 ? new Vector2(r.Position.X, r.End.Y) : r.Position;
                    Vector2 b = d.X > 0 ? r.End : d.X < 0 ? new Vector2(r.Position.X, r.End.Y) : d.Y > 0 ? r.End : new Vector2(r.End.X, r.Position.Y);
                    ci.DrawLine(a, b, torn, 2.5f, true);
                    if (!f.Jettisoned)
                        for (int k = 0; k < 2; k++)
                        {
                            var t = a.Lerp(b, 0.3f + 0.4f * k);
                            ci.DrawLine(t, t + new Vector2(d.X, d.Y) * (4f + 3f * Hash(c.X, c.Y, 90 + k)) + (b - a).Normalized() * 2f, new Color("#6b7486"), 1.5f, true);
                        }
                }
            // 연결부 (끊어진 채 / 임시 고정)
            foreach (var j in room.Joints) PaintJoint(ci, j, true, 0.9f);
            ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }

    // ─────────────────────────────── 드론 ───────────────────────────────

    private void PaintDrones(CanvasItem ci)
    {
        foreach (var d in _world.Drones.Drones)
        {
            if (d.State == DroneState.Lost) continue;
            var p = d.State == DroneState.Docked && !d.Dock.Room.Detached ? Px(d.DockPosition)
                : (d.PreviousPosition - d.Position).LengthSquared() > 4f ? Px(d.Position)
                : Px(System.Numerics.Vector2.Lerp(d.PreviousPosition, d.Position, _main.Alpha));
            var col = DroneColor(d.Kind);
            bool docked = d.State == DroneState.Docked;
            float size = docked ? 5f : 7f;

            // 견인줄 / 작업 빔
            if (d.Towing is Fragment f)
            {
                var fc = Px(f.Center);
                ci.DrawLine(p, fc.Lerp(p, 0.15f), TetherColor.WithAlpha(0.7f), 1.5f, true);
                ci.DrawCircle(fc.Lerp(p, 0.15f), 3f, TetherColor, true, -1f, true);
            }
            if (d.Fetching is Drone x && d.State == DroneState.Towing)
                ci.DrawLine(p, Px(x.Position), TetherColor.WithAlpha(0.7f), 1.2f, true);
            if (d.State == DroneState.Working && d.Order != null)
            {
                var tgt = CellRect(d.Order.Target.Cell).GetCenter();
                float flick = 0.5f + 0.5f * Mathf.Sin(_time * 23f + d.Id);
                ci.DrawLine(p, tgt, new Color("#ffcf7a").WithAlpha(0.35f + 0.4f * flick), 1.5f, true);
                ci.DrawCircle(tgt, 3f + 2f * flick, new Color("#fff1c2").WithAlpha(0.8f * flick), true, -1f, true);
            }

            // 몸체: 다이아몬드 + 회전 날개 점
            bool adrift = d.State == DroneState.Adrift;
            float spin = adrift ? _time * 2.5f + d.Id : 0f;
            var pts = new Vector2[4];
            for (int k = 0; k < 4; k++) pts[k] = p + Vector2.FromAngle(spin + k * Mathf.Pi * 0.5f) * size;
            var body = d.Wrecked ? new Color("#4a4a4a") : docked ? col.Darkened(0.35f) : col.Darkened(0.15f);
            ci.DrawColoredPolygon(pts, new Color(0.03f, 0.04f, 0.06f, 0.9f));
            var inner = pts.Select(q => p + (q - p) * 0.78f).ToArray();
            ci.DrawColoredPolygon(inner, body);
            for (int k = 0; k < 4; k++)
                ci.DrawCircle(pts[k], docked ? 1.5f : 2.2f, col.WithAlpha(docked ? 0.5f : 0.9f), true, -1f, true);
            // 상태 불빛
            Color led = d.Wrecked ? Palette.TextMuted : d.Faulty ? Palette.Danger : adrift ? Palette.Danger : d.Battery < 0.3f ? Palette.Warning : Palette.Good;
            float blink = adrift || d.Faulty ? 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(_time * 4f)) : 1f;
            ci.DrawCircle(p, 1.8f, led.WithAlpha(blink), true, -1f, true);
            // 배터리 (밖에 있거나 충전 중일 때)
            if (!docked || d.Battery < 0.99f)
            {
                var bar = new Rect2(p.X - 7f, p.Y + size + 3f, 14f, 2.5f);
                ci.DrawRect(bar, new Color(0, 0, 0, 0.7f));
                ci.DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(d.Battery, 0f, 1f), bar.Size.Y)),
                    (d.Battery < 0.3f ? Palette.Warning : Palette.Good).WithAlpha(0.9f));
            }
            // 짐 (자재)
            if (d.Cargo.Length > 0)
                ci.DrawRect(new Rect2(p.X - 2.5f, p.Y - size - 5f, 5f, 4f), Palette.Item(d.Cargo[0].kind));
        }
    }

    /// <summary>EVA 중인 사람: 외부 해치로 이어진 안전줄.</summary>
    private void PaintTethers(CanvasItem ci)
    {
        var hatch = DroneSystem.Hatch(_world);
        if (hatch == null) return;
        var h = CellRect(hatch.Cell).GetCenter();
        foreach (var c in _world.Crew)
        {
            if (!c.Outside || c.Dead || c.CarriedBy != null) continue;
            var p = CrewPx(c);
            var mid = (p + h) * 0.5f + new Vector2(0f, 10f + 4f * Mathf.Sin(_time * 1.3f + c.Id));
            var pts = new Vector2[9];
            for (int k = 0; k < pts.Length; k++)
            {
                float t = k / (pts.Length - 1f);
                pts[k] = (1 - t) * (1 - t) * h + 2 * (1 - t) * t * mid + t * t * p;
            }
            ci.DrawPolyline(pts, TetherColor.WithAlpha(0.55f), 1.3f, true);
            // 우주복 헬멧 빛
            ci.DrawArc(p, CrewRadius + 3f, 0f, Mathf.Tau, 24, new Color("#cfe8ff").WithAlpha(0.45f), 1.2f, true);
        }
    }

    // ─────────────────────────────── 구조 보기 ───────────────────────────────

    /// <summary>구조 보기: 방마다 하중(연결부가 버티는 정도), 골조를 잃은 벽, 드론의 길.</summary>
    private void PaintStructureOverlay(CanvasItem ci)
    {
        var st = _world.Structure;
        foreach (var room in _world.Ship.Rooms)
        {
            if (room.Detached || room.DesignJoints <= 0) continue;
            float known = StructureSystem.StressOf(StructureSystem.KnownCapacity(room), room.DesignJoints, StructureSystem.FrameLost(_world, room));
            float sev = Mathf.Clamp((known - 0.5f) / 0.8f, 0f, 1f);
            FillRoom(ci, room, Palette.Severity(sev).WithAlpha(0.1f + 0.22f * sev));
        }
        foreach (var (cell, wall) in _world.Ship.Walls)
            if (wall.IsHull && wall.FrameLost)
                ci.DrawRect(CellRect(cell).Grow(2f), Palette.Danger.WithAlpha(0.5f + 0.3f * Mathf.Sin(_time * 4f)), false, 2f);
        foreach (var d in _world.Drones.Drones)
        {
            if (d.State is DroneState.Docked or DroneState.Lost or DroneState.Adrift) continue;
            var col = DroneColor(d.Kind).WithAlpha(0.35f);
            for (int k = d.RouteIndex; k < d.Route.Count; k += 2)
                ci.DrawCircle(Px(d.Route[k]), 1.5f, col, true, -1f, true);
        }
    }

    // ─────────────────────────────── 드론 거치대 ───────────────────────────────

    private static void PaintDroneDockBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-3f);
        Gfx.RoundRect(ci, r, new Color("#161c26"), 5, new Color("#3a4a5e"), 1);
        // 드론 받침 셋
        for (int k = 0; k < 3; k++)
        {
            var c = new Vector2(r.GetCenter().X, r.Position.Y + r.Size.Y * (k + 0.5f) / 3f);
            ci.DrawArc(c, 7f, 0f, Mathf.Tau, 20, new Color("#2c3a4a"), 1.5f, true);
        }
        // 충전 단자
        ci.DrawRect(new Rect2(r.Position.X + 2f, r.Position.Y + 3f, 3f, r.Size.Y - 6f), new Color("#6fd3b0").WithAlpha(0.35f));
    }
}
