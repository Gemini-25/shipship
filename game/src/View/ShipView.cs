using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 우주선과 승무원을 월드 좌표(1칸 = 32px)로 그린다.
/// 바뀌지 않는 것(벽, 바닥 텍스처, 가구 몸체)은 정적 레이어에 한 번만,
/// 움직이는 것(문, 승무원, 설비 불빛, 보기 모드 오버레이)은 동적 레이어에 매 프레임 그린다.
/// </summary>
public partial class ShipView : Node2D
{
    public const float T = 32f;

    private Main _main = null!;
    private World _world = null!;
    private DrawLayer _static = null!;
    private DrawLayer _dynamic = null!;
    private DrawLayer _lights = null!;
    private readonly Dictionary<Room, List<(Vector2 a, Vector2 b)>> _outlines = new();
    private readonly Dictionary<Room, Vector2> _labelAnchors = new();
    private float _time;

    public Rect2 Bounds { get; private set; }

    /// <summary>v17.7 드물게 바뀌는 층은 화면 확대에 맞춘 그림 한 장으로 구워 그린다 (격자 전체 + 여백).</summary>
    internal BakedLayer Baked(DrawLayer layer, int margin = 2, int maxTex = 8192) => new(layer, () =>
    {
        var g = _world.Ship.Grid;
        return new Rect2(-margin * T, -margin * T, (g.Width + 2 * margin) * T, (g.Height + 2 * margin) * T);
    }, TextureFilter, maxTex);

    public void Init(Main main, World world)
    {
        _main = main;
        _world = world;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _static = new DrawLayer { Name = "Static", Painter = PaintStatic };
        _lights = new DrawLayer { Name = "Lights", Painter = PaintLights, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _dynamic = new DrawLayer { Name = "Dynamic", Painter = PaintDynamic };
        AddLookUnder(); // v16.5a 바탕(생성기 바닥재 · 벽) · 상태 겹치기 · 흔적 — 정적 층 아래
        AddChild(Baked(_static)); // v17.7 구워 둔다 (BakedLayer.cs)
        AddFixtureFineLayer(); // v16.5c 설비 디테일 층 (가까이서만)
        AddLookLight(); // v16.5a 2D 조명: 낮은 해상도 빛 버퍼 (곱하기)
        AddTechLookLayers(); // v16.5b 기술 수준 벽 · 기술 모습 · 간접 조명 (TechLook.cs)
        AddChild(_lights); // v10: 천장 조명이 바닥에 떨어뜨리는 빛 (더하기 섞기)
        // 60프레임: 사람을 따라 움직이지 않는 그림은 조각으로 나눠 돌아가며 다시 그리고 한 장씩 구워 둔다 (배 밖 멀리 가는 것만 그대로)
        AddPart("DynamicFar", PaintDynamicFar, -1); // 떨어져 나간 방 · 다가오는 재난 (구운 그림 밖)
        AddPart("DynamicUnder", PaintDynamicUnder, 16); // v17.7 배 바깥 · 흔적 · 바닥 상태 · 물건
        AddPart("Fixtures0", ci => PaintFixtureLifePart(ci, 0, 2), 6); // 설비 움직임 (반씩)
        AddPart("Fixtures1", ci => PaintFixtureLifePart(ci, 1, 2), 6);
        AddPart("SlowFloor", PaintSlowFloor, 6); // 소품 · 폭발성 물건 · 음식 · 어두운 방 · 이동식 장비 · 공사 · 겹쳐 보기 · 물
        AddPart("SlowFrame", PaintSlowFrame, 6); // 벽 · 관 · 망 · 이음 · 표지 · 장갑 · 방 상태
        AddPart("SlowMarks", PaintSlowMarks, 6); // 주컴퓨터 · 정비 꼬리표 · 고장 표시 · 바닥 흔적
        AddChild(_dynamic);
        AddLookOver(); // v16.5a 입자 (김 · 물방울 · 불꽃 · 연기 · 먼지 · 결로)
        BuildOutlines();
        BuildLabelAnchors();
        Bounds = ComputeBounds();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        CheckFurnitureChanged();
        CheckStructureChanged();
        CheckCircuitsChanged();
        UpdateFixtureLod(); // v16.5c 확대 단계 · 보이는 범위
        UpdateLook((float)delta); // v16.5a 확대 단계 · 겹치기 지문 · 빛 버퍼 · 입자
        UpdateTechLook(); // v16.5b 미감 세트 · 익힌 기술 · 개조 칸이 바뀌면 다시 그린다
        _dynamic.QueueRedraw();
        RedrawParts(delta);
        _lights.QueueRedraw();
    }

    // ─────────── 60프레임: 나눠 그리는 층 ───────────
    // 사람이 없는 장식 · 바닥 · 설비 · 관 · 표지는 초당 15~30번이면 충분하다 (멀수록 드물게).
    // 조각마다 지난번 든 시간을 알고, 한 프레임에 예산(4ms)만큼만 가장 오래 기다린 조각부터 다시 그린다 — 한 프레임에 몰려 튀지 않게.
    private sealed class Part
    {
        public DrawLayer Layer = null!;
        public double Since = 1e9;
    }
    private readonly List<Part> _slowParts = new(), _slowOrder = new();
    private const double PartBudgetMs = 4.0;
    private (ViewMode, ViewMode?) _partMode;

    private void AddPart(string name, System.Action<CanvasItem> painter, int bakeMargin)
    {
        var layer = new DrawLayer { Name = name, Painter = painter };
        _slowParts.Add(new Part { Layer = layer });
        if (bakeMargin < 0) AddChild(layer);
        else AddChild(Baked(layer, bakeMargin, 3072));
    }

    private void RedrawParts(double delta)
    {
        float z = Zoom;
        double every = z >= 0.8f ? 1.0 / 30.0 : z >= 0.4f ? 1.0 / 20.0 : 1.0 / 15.0;
        var mode = (_main.ViewMode, _main.SecondaryView);
        if (mode != _partMode) { _partMode = mode; foreach (var p in _slowParts) p.Since = 1e9; } // 보기를 바꾸면 바로
        foreach (var p in _slowParts) p.Since += delta;
        _slowOrder.Clear();
        _slowOrder.AddRange(_slowParts);
        _slowOrder.Sort((a, b) => b.Since.CompareTo(a.Since));
        double spent = 0;
        foreach (var p in _slowOrder)
        {
            if (p.Since < every) break;
            if (spent > 0 && spent + p.Layer.LastMs > PartBudgetMs) break;
            p.Layer.QueueRedraw();
            p.Since = 0;
            spent += p.Layer.LastMs;
        }
    }

    /// <summary>선체가 바뀌었을 때(사고, 개조) 호출.</summary>
    public void RedrawStatic() { RedrawLook(); _static.QueueRedraw(); _fixFine?.QueueRedraw(); } // v16.5c 디테일 층도

    public static Rect2 CellRect(Cell c) => new(c.X * T, c.Y * T, T, T);
    private CrewMember? CrewById(int id) { foreach (var c in _world.Crew) if (c.Id == id) return c; return null; }
    public static Rect2 FurnitureRect(Furniture f) => new(f.MinX * T, f.MinY * T, f.Width * T, f.Height * T);
    public static Vector2 ToPx(System.Numerics.Vector2 v) => new(v.X * T, v.Y * T);
    public static Cell CellAtPx(Vector2 px) => new(Mathf.FloorToInt(px.X / T), Mathf.FloorToInt(px.Y / T));

    private float Zoom => GetViewport().GetCanvasTransform().X.Length();

    /// <summary>이전 틱과 현재 틱 사이를 보간한 승무원 위치.</summary>
    public Vector2 CrewPx(CrewMember c)
    {
        var a = c.PreviousPosition;
        var b = c.Position;
        if (System.Numerics.Vector2.DistanceSquared(a, b) > 4f) return ToPx(b);
        return ToPx(System.Numerics.Vector2.Lerp(a, b, _main.Alpha));
    }

    /// <summary>화면에서 너무 작아지지 않게, 멀리서 볼 때는 승무원을 조금 크게 그린다.</summary>
    public float CrewRadius => Mathf.Max(9.5f, 7f / Mathf.Max(0.1f, Zoom));

    private Rect2 ComputeBounds()
    {
        var g = _world.Ship.Grid;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) == TileKind.Void) continue;
            minX = Mathf.Min(minX, c.X); minY = Mathf.Min(minY, c.Y);
            maxX = Mathf.Max(maxX, c.X); maxY = Mathf.Max(maxY, c.Y);
        }
        return new Rect2(minX * T - T * 2.5f, minY * T, (maxX - minX + 1) * T + T * 2.5f, (maxY - minY + 1) * T);
    }

    /// <summary>방 이름표는 방 위쪽 벽에 붙인다(문 표지판처럼). 벽에서 가장 긴 빈 구간의 왼쪽 끝.</summary>
    public Vector2 RoomLabelAnchor(Room room) => _labelAnchors.TryGetValue(room, out var a) ? a : new Vector2(room.MinX * T + 4f, (room.MinY - 1) * T + T * 0.5f);

    /// <summary>방 외곽선 (v10.2: 방금 칸막이로 생긴 방은 다음 프레임에 만들어진다 — 그동안은 빈 목록).</summary>
    private List<(Vector2 a, Vector2 b)> Outline(Room room) => _outlines.TryGetValue(room, out var o) ? o : NoOutline;
    private static readonly List<(Vector2 a, Vector2 b)> NoOutline = new();

    private void BuildLabelAnchors()
    {
        var g = _world.Ship.Grid;
        foreach (var room in _world.Ship.Rooms)
        {
            int y = room.MinY - 1;
            int bestStart = room.MinX, bestLen = -1, runStart = -1;
            for (int x = room.MinX; x <= room.MaxX + 1; x++)
            {
                bool wall = x <= room.MaxX && g.Kind(new Cell(x, y)) == TileKind.Wall;
                if (wall && runStart < 0) runStart = x;
                if (!wall && runStart >= 0)
                {
                    if (x - runStart > bestLen) { bestLen = x - runStart; bestStart = runStart; }
                    runStart = -1;
                }
            }
            _labelAnchors[room] = new Vector2(bestStart * T + 4f, y * T + T * 0.5f);
        }
    }

    private void BuildOutlines()
    {
        var ship = _world.Ship;
        foreach (var room in ship.Rooms)
        {
            var segs = new List<(Vector2, Vector2)>();
            foreach (var c in room.Cells)
            {
                var r = CellRect(c);
                if (ship.RoomAt(c + new Cell(0, -1)) != room) segs.Add((r.Position, new Vector2(r.End.X, r.Position.Y)));
                if (ship.RoomAt(c + new Cell(0, 1)) != room) segs.Add((new Vector2(r.Position.X, r.End.Y), r.End));
                if (ship.RoomAt(c + new Cell(-1, 0)) != room) segs.Add((r.Position, new Vector2(r.Position.X, r.End.Y)));
                if (ship.RoomAt(c + new Cell(1, 0)) != room) segs.Add((new Vector2(r.End.X, r.Position.Y), r.End));
            }
            _outlines[room] = segs;
        }
    }

    private static Rect2 Variant(Cell c)
    {
        int h = (c.X * 73856093) ^ (c.Y * 19349663);
        int v = (h >> 3) & 3;
        return new Rect2((v & 1) * 128f, (v >> 1) * 128f, 128f, 128f); // v10: 칸 하나 = 128px 무늬
    }

    // ═══════════════════════════════ 정적 레이어 ═══════════════════════════════

    private void PaintStatic(CanvasItem ci)
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        // v16.5a 외판 · 테두리 · 바닥 · 벽 칸은 바탕 층(PaintLookBase)이 그린다 — 겹치기 · 흔적이 그 위, 가구 아래에 오도록
        if (_hullCells == null || _hullVersion != _world.Structure.Version) BuildHull(); // 그리는 순서가 바뀌어도 외판 칸은 먼저

        // 벽 안쪽 모서리 하이라이트
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) != TileKind.Wall) continue;
            var r = CellRect(c);
            if (g.Kind(c + new Cell(1, 0)) == TileKind.Floor)
                ci.DrawLine(new Vector2(r.End.X - 1, r.Position.Y), new Vector2(r.End.X - 1, r.End.Y), Palette.WallEdge, 2f);
            if (g.Kind(c + new Cell(-1, 0)) == TileKind.Floor)
                ci.DrawLine(new Vector2(r.Position.X + 1, r.Position.Y), new Vector2(r.Position.X + 1, r.End.Y), Palette.WallEdge, 2f);
            if (g.Kind(c + new Cell(0, 1)) == TileKind.Floor)
                ci.DrawLine(new Vector2(r.Position.X, r.End.Y - 1), new Vector2(r.End.X, r.End.Y - 1), Palette.WallEdge, 2f);
            if (g.Kind(c + new Cell(0, -1)) == TileKind.Floor)
                ci.DrawLine(new Vector2(r.Position.X, r.Position.Y + 1), new Vector2(r.End.X, r.Position.Y + 1), Palette.WallEdge, 2f);
        }

        PaintBodyFloors(ci); // v16.3 바닥재 무늬 · 점검 뚜껑 자리 · 칸막이 · 유도선
        PaintHullSeams(ci);
        PaintWindows(ci);
        PaintWallShade(ci);
        PaintCorridorGuides(ci);
        PaintFloorDecor(ci); // v10.9 바닥 격자·배수구·깔개·경고 띠
        PaintRoomKinds(ci); // v16.22 방 종류마다 바닥 (주컴퓨터실 · 버섯 · 냉동 · 항법 …)
        PaintHoloPad(ci); // v11.2 함교 홀로그램 테이블 패드
        PaintWiring(ci); // v10.9 케이블 트레이·분전함·간선
        PaintWallProps(ci); // v10.9 벽에 붙은 계기·사물함·소화기·번호판
        // v10: 가구 그림자 (빛이 왼쪽 위에서)
        foreach (var f in ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached && !FurnitureTypes.Walkable(f.Type)))
            Gfx.RoundRect(ci, new Rect2(FurnitureRect(f).Position + new Vector2(3f, 4f), FurnitureRect(f).Size).Grow(-3f), new Color(0, 0, 0, 0.28f), 6);
        foreach (var f in ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached)) PaintFurniture(ci, f);
    }

    /// <summary>v10: 벽 아래 그늘 — 벽과 맞닿은 바닥 칸의 가장자리가 어둡다 (앰비언트 오클루전 흉내).</summary>
    private void PaintWallShade(CanvasItem ci)
    {
        var g = _world.Ship.Grid;
        float w = T * 0.42f;
        var dark = new Color(0, 0, 0, 0.34f);
        var clear = new Color(0, 0, 0, 0f);
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) != TileKind.Floor) continue;
            var r = CellRect(c);
            float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
            if (g.Kind(c + new Cell(0, -1)) == TileKind.Wall)
                ci.Polygon(new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y0 + w), new Vector2(x0, y0 + w) }, new[] { dark, dark, clear, clear });
            if (g.Kind(c + new Cell(-1, 0)) == TileKind.Wall)
                ci.Polygon(new[] { new Vector2(x0, y0), new Vector2(x0 + w, y0), new Vector2(x0 + w, y1), new Vector2(x0, y1) }, new[] { dark, clear, clear, dark });
            if (g.Kind(c + new Cell(1, 0)) == TileKind.Wall)
                ci.Polygon(new[] { new Vector2(x1 - w, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x1 - w, y1) }, new[] { clear, dark * 0.7f, dark * 0.7f, clear });
            if (g.Kind(c + new Cell(0, 1)) == TileKind.Wall)
                ci.Polygon(new[] { new Vector2(x0, y1 - w), new Vector2(x1, y1 - w), new Vector2(x1, y1), new Vector2(x0, y1) }, new[] { clear, clear, dark * 0.7f, dark * 0.7f });
        }
    }

    /// <summary>
    /// v10: 천장 조명. 전기가 있고 조명이 멀쩡한 방은 네 칸마다 빛이 떨어진다 (방 종류마다 빛깔이 조금 다르다).
    /// 정전된 방은 비상등(희미한 붉은빛)만, 조명이 나간 방은 아무것도 없다. 더하기 섞기라 바닥이 밝아진다.
    /// </summary>
    private void PaintLights(CanvasItem ci)
    {
        if (Textures.Light is not Texture2D tex || _main.ViewMode != ViewMode.Normal) return;
        if (_hullCells != null) PaintWindowGlow(ci); // v10.9 창밖 불빛
        foreach (var room in _world.Ship.LiveRooms)
        {
            if (room.LightsOut || room.Cells.Count == 0) continue;
            bool emergency = !room.Powered;
            var tint = emergency ? new Color(0.9f, 0.12f, 0.08f) : room.Type switch
            {
                RoomType.Quarters or RoomType.Lounge or RoomType.Mess or RoomType.Galley => new Color(1f, 0.86f, 0.62f),
                RoomType.Engine or RoomType.Reactor or RoomType.Cooling or RoomType.Power => new Color(0.62f, 0.8f, 1f),
                RoomType.Hydroponics => new Color(0.75f, 1f, 0.7f),
                RoomType.Medbay => new Color(0.9f, 0.97f, 1f),
                RoomType.Bridge => new Color(0.55f, 0.7f, 1f),
                RoomType.Comms => new Color(0.55f, 0.95f, 0.8f),
                _ => new Color(0.88f, 0.9f, 0.95f),
            };
            float strength = emergency ? 0.14f : room.Type == RoomType.Corridor ? 0.10f : 0.17f;
            if (!emergency && room.PowerFlow < 0.75f) // v14.8 전압 강하: 불이 흐리고, 많이 떨어지면 깜빡인다
                strength *= (0.45f + 0.55f * room.PowerFlow / 0.75f) * (room.PowerFlow < 0.55f ? 0.8f + 0.2f * Mathf.Sin(_world.Tick * 0.7f + room.Id) : 1f);
            if (emergency && Mathf.Sin(_time * 2.2f + room.Id) < -0.2f) strength *= 0.5f; // 비상등은 느리게 깜빡인다
            if (LookLightOn) strength *= 0.5f; // v16.5a 바탕 빛은 빛 버퍼가 맡고 여기선 번짐만
            int minX = room.Cells.Min(c => c.X), minY = room.Cells.Min(c => c.Y);
            foreach (var c in room.Cells)
            {
                if ((c.X - minX) % 4 != 1 || (c.Y - minY) % 4 != 1) continue;
                if (emergency && ((c.X - minX) / 4 + (c.Y - minY) / 4) % 2 == 1) continue;
                var center = CellRect(c).GetCenter() + new Vector2(T * 0.5f, T * 0.5f);
                float size = T * (emergency ? 3.2f : 4.6f);
                ci.DrawTextureRect(tex, new Rect2(center - new Vector2(size, size) * 0.5f, size, size), false, new Color(tint.R, tint.G, tint.B, strength));
            }
        }
        PaintPortableLights(ci); // v16.7 이동식 광원 (작업등 원뿔 · 히터 열기)
        PaintLookGlow(ci); // v16.5a 불길 깜빡임 번짐
        PaintBeacons(ci);
    }

    /// <summary>
    /// v12.9.1 경광등: 공기가 새거나, 불이 났거나, 공기가 위험하거나, 봉쇄된 방은 천장의 붉은 회전등이 돈다.
    /// 두 갈래 빛이 방을 쓸고 지나간다 (더하기 섞기 — 어두운 방일수록 또렷하다).
    /// </summary>
    private void PaintBeacons(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            if (room.Cells.Count == 0 || room.Type == RoomType.Corridor) continue;
            bool fire = _world.Fire.IsKnown(room) && _world.Fire.CountIn(room) > 0;
            bool alarm = room.Leaking || fire || room.Lockdown || room.Unbreathable || Atmosphere.Danger(room) > 0.35f;
            if (!alarm) continue;
            // 경광등은 축전지로 돈다 — 정전에도 돌지만, 조명 회로가 끊긴 방은 꺼진다
            if (room.LightsOut) continue;
            var color = fire ? new Color(1f, 0.45f, 0.1f) : new Color(1f, 0.12f, 0.08f);
            float cx = (room.MinX + room.MaxX + 1) * 0.5f * T, top = room.MinY * T + T * 0.6f;
            var lamp = new Vector2(cx, top);
            float reach = Mathf.Clamp((room.MaxX - room.MinX + 1) * 0.55f, 3f, 9f) * T;
            float angle = _time * 3.6f + room.Id * 1.3f;
            for (int k = 0; k < 2; k++)
            {
                float a0 = angle + k * Mathf.Pi;
                const int seg = 7;
                var pts = new Vector2[seg + 2];
                var cols = new Color[seg + 2];
                pts[0] = lamp;
                cols[0] = new Color(color.R, color.G, color.B, 0.34f);
                for (int i = 0; i <= seg; i++)
                {
                    float a = a0 - 0.32f + 0.64f * i / seg;
                    pts[i + 1] = lamp + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * reach;
                    cols[i + 1] = new Color(color.R, color.G, color.B, 0f);
                }
                ci.Polygon(pts, cols);
            }
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_time * 3.6f + room.Id));
            ci.Circle(lamp, T * 0.42f, new Color(color.R, color.G, color.B, 0.35f * pulse));
            ci.Circle(lamp, T * 0.18f, new Color(1f, 0.85f, 0.8f, 0.8f * pulse));
        }
    }

    private void PaintCorridorGuides(CanvasItem ci)
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        var accent = Palette.Room(RoomType.Corridor);
        foreach (var room in ship.RoomsOf(RoomType.Corridor))
        foreach (var c in room.Cells)
        {
            var r = CellRect(c);
            bool wallAbove = g.Kind(c + new Cell(0, -1)) == TileKind.Wall;
            bool wallBelow = g.Kind(c + new Cell(0, 1)) == TileKind.Wall;
            if (wallAbove && c.X % 2 == 0) ci.Box(new Rect2(r.Position.X + 10, r.Position.Y + 3, 12, 2), Palette.Accent.WithAlpha(0.28f));
            if (wallBelow && c.X % 2 == 0) ci.Box(new Rect2(r.Position.X + 10, r.End.Y - 5, 12, 2), Palette.Accent.WithAlpha(0.28f));
            if (!wallBelow && room == ship.RoomAt(c + new Cell(0, 1)) && c.X % 2 == 1)
                ci.Box(new Rect2(r.Position.X + 8, r.End.Y - 1, 16, 2), accent.WithAlpha(0.2f));
        }
    }

    private static void PaintFurniture(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f);
        var center = r.GetCenter();
        var accent = Palette.Room(f.Room.Kind);
        if (FixtureArt.PaintBody(ci, f)) return; // v16.5c 설비 그림 표 (표에 없는 것만 아래 예전 그림)
        if (PaintModuleBody(ci, f)) return; // v10.8 방 모듈

        switch (f.Type)
        {
            case FurnitureType.RobotDock: PaintRobotDockBody(ci, f); return; // v10.10
            case FurnitureType.SupplyCache: PaintSupplyCacheBody(ci, f); return;
            case FurnitureType.Bed:
            {
                var frame = r.Grow(-3f);
                Gfx.RoundRect(ci, frame, new Color("#232a37"), 6, new Color("#323b4b"));
                var blanket = f.Owner != null ? Palette.Crew(f.Owner.Id).Darkened(0.6f) : new Color("#2b3342");
                var b = new Rect2(frame.Position.X + 3, frame.Position.Y + 14, frame.Size.X - 6, frame.Size.Y - 17);
                Gfx.RoundRect(ci, b, blanket, 4);
                ci.DrawLine(new Vector2(b.Position.X, b.Position.Y + 6), new Vector2(b.End.X, b.Position.Y + 6), blanket.Lightened(0.18f), 1.5f);
                Gfx.RoundRect(ci, new Rect2(frame.Position.X + 5, frame.Position.Y + 4, frame.Size.X - 10, 8), new Color(0.8f, 0.84f, 0.9f, 0.75f), 3);
                break;
            }
            case FurnitureType.Seat:
            {
                // v10.9: 방석과 등받이
                var s0 = r.Grow(-7f);
                Gfx.RoundRect(ci, s0, new Color("#2f3747"), 6, new Color("#46506a"));
                Gfx.RoundRect(ci, s0.Grow(-3f), new Color("#3a4459").Lerp(accent, 0.12f), 4);
                ci.Box(new Rect2(s0.Position.X + 3, s0.Position.Y + 1.5f, s0.Size.X - 6, 3), new Color("#56627a"));
                break;
            }
            case FurnitureType.Table:
            {
                // v10.9: 상판 가장자리 광택과 식기 (주방에서는 조리 도구)
                var t0 = r.Grow(-4f);
                Gfx.RoundRect(ci, t0, new Color("#232b38"), 7, new Color("#3d4859"));
                ci.DrawLine(new Vector2(t0.Position.X + 7, t0.Position.Y + 2.5f), new Vector2(t0.End.X - 7, t0.Position.Y + 2.5f), new Color(1, 1, 1, 0.08f), 1.5f);
                int n = Mathf.Max(1, (int)(t0.Size.X / 22f)) * Mathf.Max(1, (int)(t0.Size.Y / 22f));
                for (int k = 0; k < n; k++)
                {
                    float fx = (k + 0.5f) / n;
                    var p = new Vector2(t0.Position.X + t0.Size.X * fx, t0.GetCenter().Y + (k % 2 == 0 ? -4f : 4f) * Mathf.Min(1f, t0.Size.Y / 24f));
                    if (f.Room.Type == RoomType.Galley)
                    {
                        ci.Box(new Rect2(p - new Vector2(5, 3), new Vector2(10, 6)), new Color("#6b5a3a"));
                        ci.DrawLine(p + new Vector2(-4, 0), p + new Vector2(4, 0), new Color("#9aa6b5"), 1f);
                    }
                    else
                    {
                        ci.Circle(p, 4f, new Color("#c9d1dc").WithAlpha(0.8f), true, -1f, true);
                        ci.Circle(p, 2.2f, new Color("#8894a8"), true, -1f, true);
                        ci.Circle(p + new Vector2(6f, -3f), 1.6f, new Color("#e0b64a").WithAlpha(0.8f), true, -1f, true);
                    }
                }
                break;
            }

            case FurnitureType.MealDispenser:
            {
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#283040"), 5, new Color("#3b4556"));
                var dir = f.UseSpots.Count > 0 ? f.UseSpots[0] - f.Cells[0] : new Cell(0, 1);
                var strip = dir.Y > 0 ? new Rect2(r.Position.X + 7, r.End.Y - 8, r.Size.X - 14, 3)
                          : dir.Y < 0 ? new Rect2(r.Position.X + 7, r.Position.Y + 5, r.Size.X - 14, 3)
                          : dir.X > 0 ? new Rect2(r.End.X - 8, r.Position.Y + 7, 3, r.Size.Y - 14)
                          : new Rect2(r.Position.X + 5, r.Position.Y + 7, 3, r.Size.Y - 14);
                ci.Box(strip, accent.WithAlpha(0.85f));
                // v10.9: 메뉴 화면과 버튼
                var sc = r.Grow(-9f);
                ci.Box(sc, new Color("#0b1f29"));
                ci.DrawLine(sc.Position + new Vector2(2, 3), new Vector2(sc.End.X - 3, sc.Position.Y + 3), accent.WithAlpha(0.5f), 1f);
                ci.DrawLine(sc.Position + new Vector2(2, 6), new Vector2(sc.End.X - 6, sc.Position.Y + 6), accent.WithAlpha(0.3f), 1f);
                break;
            }
            case FurnitureType.Console:
            {
                // v10.9: 화면 테두리와 자판
                var b0 = r.Grow(-3f);
                Gfx.RoundRect(ci, b0, new Color("#11161e"), 5, new Color("#2f3a4b"));
                var scr = new Rect2(b0.Position.X + 4, b0.Position.Y + 4, b0.Size.X - 8, b0.Size.Y * 0.48f);
                ci.Box(scr, new Color("#0b1f29"));
                ci.Box(scr, new Color("#2b4c63"), false, 1f);
                for (int k = 0; k < 4; k++)
                    ci.Box(new Rect2(b0.Position.X + 5 + k * (b0.Size.X - 10) / 4f, b0.End.Y - 8, (b0.Size.X - 10) / 4f - 2, 3), new Color("#3a4454"));
                break;
            }

            case FurnitureType.ReactorCore:
            {
                // v10.8: 큰 배의 원자로(4×4~6×6)는 그만큼 크게
                float rs = Mathf.Min(r.Size.X, r.Size.Y) / (3f * T);
                Gfx.RoundRect(ci, r.Grow(-4f), new Color("#1d1912"), 18, new Color("#4a3f22"), 2);
                ci.Circle(center, 30f * rs, new Color("#28221a"), true, -1f, true);
                ci.Arc(center, 30f * rs, 0f, Mathf.Tau, 48, new Color("#4a3f22"), 1.5f, true);
                for (int k = 0; k < 4; k++)
                {
                    var d = Vector2.FromAngle(Mathf.Pi * 0.25f + k * Mathf.Pi * 0.5f);
                    ci.DrawLine(center + d * 31f * rs, center + d * 40f * rs, new Color("#4a3f22"), 3f, true);
                }
                break;
            }
            case FurnitureType.EngineCore:
            {
                // v10.9: 터빈 — 동심원 케이싱과 날개, 연료 공급관
                Gfx.RoundRect(ci, r.Grow(-4f), new Color("#1f1716"), 10, new Color("#4b302b"), 2);
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.38f;
                ci.Circle(center, rad, new Color("#150f0e"), true, -1f, true);
                ci.Arc(center, rad, 0f, Mathf.Tau, 40, new Color("#5a3a33"), 2.5f, true);
                ci.Arc(center, rad * 0.7f, 0f, Mathf.Tau, 32, new Color("#3a2724"), 1.5f, true);
                for (int k = 0; k < 10; k++)
                {
                    var d = Vector2.FromAngle(k * Mathf.Tau / 10f);
                    var d2 = Vector2.FromAngle(k * Mathf.Tau / 10f + 0.35f);
                    ci.DrawLine(center + d * rad * 0.28f, center + d2 * rad * 0.92f, new Color("#4b302b"), 2f, true);
                }
                ci.Circle(center, rad * 0.22f, new Color("#3a2724"), true, -1f, true);
                for (int k = 0; k < 8; k++)
                    ci.Circle(center + Vector2.FromAngle(k * Mathf.Tau / 8f + 0.2f) * (rad + 5f), 1.5f, new Color("#6b4a3f"), true, -1f, true);
                ci.DrawLine(new Vector2(r.End.X - 6, r.Position.Y + 8), new Vector2(r.End.X - 6, r.End.Y - 8), new Color("#6b4a3f"), 3f);
                break;
            }

            case FurnitureType.OxygenGenerator:
                Gfx.RoundRect(ci, r.Grow(-4f), new Color("#10201f"), 9, new Color("#1f4a4a"), 2);
                foreach (float fy in new[] { 0.28f, 0.72f })
                {
                    var p = new Vector2(center.X, r.Position.Y + r.Size.Y * fy);
                    ci.Circle(p, 12f, new Color("#163130"), true, -1f, true);
                    ci.Arc(p, 12f, 0f, Mathf.Tau, 32, accent.WithAlpha(0.4f), 1.5f, true);
                }
                break;

            case FurnitureType.Workbench:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#2c261d"), 4, new Color("#54482f"));
                ci.Box(new Rect2(r.Position.X + 10, r.Position.Y + 10, 12, 4), new Color("#8a7550"));
                ci.Box(new Rect2(r.Position.X + 32, r.Position.Y + 8, 4, 15), new Color("#6b5a3a"));
                ci.Circle(new Vector2(r.End.X - 18, center.Y), 4.5f, new Color("#5d6b7a"), true, -1f, true);
                // v10.9: 바이스, 렌치·드라이버, 작업 조명
                Gfx.RoundRect(ci, new Rect2(r.Position.X + 8, r.End.Y - 12, 14, 7), new Color("#4a5566"), 2, new Color("#6a7486"));
                ci.DrawLine(new Vector2(r.Position.X + 44, r.Position.Y + 9), new Vector2(r.Position.X + 56, r.Position.Y + 19), new Color("#9aa6b5"), 2f, true);
                ci.Circle(new Vector2(r.Position.X + 44, r.Position.Y + 9), 2.5f, new Color("#9aa6b5"), false, 1.2f, true);
                ci.DrawLine(new Vector2(r.Position.X + 60, r.End.Y - 8), new Vector2(r.Position.X + 70, r.End.Y - 14), new Color("#c0392b"), 2.5f, true);
                ci.DrawLine(new Vector2(r.Position.X + 70, r.End.Y - 14), new Vector2(r.Position.X + 75, r.End.Y - 17), new Color("#9aa6b5"), 1f, true);
                ci.Circle(new Vector2(r.End.X - 8, r.Position.Y + 8), 3f, new Color("#f5d547").WithAlpha(0.6f), true, -1f, true);
                break;

            case FurnitureType.Shelf:
            {
                // v10.9: 기둥과 선반 판
                Gfx.RoundRect(ci, r.Grow(-2f), new Color("#1b2029"), 3, new Color("#2b3341"));
                ci.Box(new Rect2(r.Position.X + 3, r.Position.Y + 3, 2.5f, r.Size.Y - 6), new Color("#3a4454"));
                ci.Box(new Rect2(r.End.X - 5.5f, r.Position.Y + 3, 2.5f, r.Size.Y - 6), new Color("#3a4454"));
                for (int k = 1; k < 3; k++)
                    ci.DrawLine(new Vector2(r.Position.X + 4, r.Position.Y + r.Size.Y * k / 3f), new Vector2(r.End.X - 4, r.Position.Y + r.Size.Y * k / 3f), new Color("#303a4a"), 1.5f);
                break;
            }

            case FurnitureType.MedBed:
            {
                var frame = r.Grow(-4f);
                Gfx.RoundRect(ci, frame, new Color("#241c20"), 6, accent.WithAlpha(0.35f));
                Gfx.RoundRect(ci, new Rect2(frame.Position.X + 4, frame.Position.Y + 4, frame.Size.X - 8, 8), new Color(0.95f, 0.9f, 0.92f, 0.55f), 3);
                var cc = new Vector2(center.X, center.Y + 8);
                ci.Box(new Rect2(cc.X - 1.5f, cc.Y - 6, 3, 12), accent.WithAlpha(0.75f));
                ci.Box(new Rect2(cc.X - 6, cc.Y - 1.5f, 12, 3), accent.WithAlpha(0.75f));
                break;
            }
            case FurnitureType.CoolantPump:
            {
                // 벽으로 이어지는 배관
                ci.Box(new Rect2(center.X - 5, r.Position.Y - 4, 10, r.Size.Y * 0.5f), new Color("#223440"));
                ci.Box(new Rect2(center.X - 5, r.Position.Y - 4, 10, 3), new Color("#35505f"));
                ci.Circle(center, 24f, new Color("#122029"), true, -1f, true);
                ci.Arc(center, 24f, 0f, Mathf.Tau, 48, new Color("#2d5263"), 2.5f, true);
                ci.Arc(center, 17f, 0f, Mathf.Tau, 40, new Color("#1f3a47"), 1.5f, true);
                break;
            }
            case FurnitureType.PowerPanel:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#1b2029"), 4, new Color("#4a4a36"), 2);
                ci.Box(new Rect2(r.Position.X + 6, r.End.Y - 9, r.Size.X - 12, 2), new Color("#f5d547").WithAlpha(0.35f));
                // v10.9: 회로별 색 띠와 통풍 틈
                for (int k = 0; k < PowerGrid.CircuitCount; k++)
                    ci.Box(new Rect2(r.Position.X + 8 + k * (r.Size.X - 16) / PowerGrid.CircuitCount, r.Position.Y + 5, (r.Size.X - 16) / PowerGrid.CircuitCount - 4, 2), CircuitColors[k].WithAlpha(0.6f));
                for (int k = 0; k < 6; k++)
                    ci.DrawLine(new Vector2(r.End.X - 22 + k * 3, r.End.Y - 16), new Vector2(r.End.X - 22 + k * 3, r.End.Y - 11), new Color("#0a0d12"), 1.2f);
                break;

            case FurnitureType.Battery:
                Gfx.RoundRect(ci, r.Grow(-4f), new Color("#18211d"), 6, new Color(f.Improved ? "#4a7a8a" : "#2f4a3a"), 2);
                if (f.Width < 2)
                {
                    // 항해 중에 단 배터리 모듈 (한 칸): 새 판이라 테두리가 밝다
                    Gfx.RoundRect(ci, new Rect2(r.Position.X + 9, r.Position.Y + 6, 14, r.Size.Y - 12), new Color("#101713"), 3);
                    break;
                }
                for (int k = 0; k < 3; k++)
                    Gfx.RoundRect(ci, new Rect2(r.Position.X + 10 + k * 15, r.Position.Y + 10, 11, r.Size.Y - 20), new Color("#101713"), 3);
                break;

            case FurnitureType.GrowBed:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#1c2619"), 5, new Color("#3b5a33"));
                Gfx.RoundRect(ci, new Rect2(r.Position.X + 6, r.Position.Y + 9, r.Size.X - 12, r.Size.Y - 18), new Color("#2a2219"), 3);
                break;

            case FurnitureType.WaterRecycler:
            {
                // v10.9: 둥근 여과 탱크, 막 고리, 관 이음, 압력계
                Gfx.RoundRect(ci, r.Grow(-4f), new Color("#122029"), 10, new Color("#2b4c63"), 2);
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.3f;
                ci.Circle(center, rad, new Color("#0c151c"), true, -1f, true);
                ci.Arc(center, rad, 0f, Mathf.Tau, 32, new Color("#3f6f88"), 2f, true);
                ci.Arc(center, rad * 0.62f, 0f, Mathf.Tau, 24, new Color("#1f3a47"), 1.5f, true);
                ci.Arc(center, rad * 0.8f, 3.6f, 5.0f, 10, new Color(1, 1, 1, 0.18f), 1.5f, true);
                ci.Box(new Rect2(r.Position.X + 3, center.Y - 3, 7, 6), new Color("#35505f"));
                ci.Box(new Rect2(r.End.X - 10, center.Y - 3, 7, 6), new Color("#35505f"));
                var gp = new Vector2(r.End.X - 10, r.Position.Y + 10);
                ci.Circle(gp, 4f, new Color("#d8dee8"), true, -1f, true);
                ci.DrawLine(gp, gp + new Vector2(2.5f, -2f), new Color("#c0392b"), 1f, true);
                break;
            }

            case FurnitureType.Stove:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#262220"), 5, new Color("#4a3f35"));
                ci.Arc(new Vector2(r.Position.X + r.Size.X * 0.28f, center.Y), 8f, 0f, Mathf.Tau, 24, new Color("#3f3733"), 2f, true);
                ci.Arc(new Vector2(r.Position.X + r.Size.X * 0.72f, center.Y), 8f, 0f, Mathf.Tau, 24, new Color("#3f3733"), 2f, true);
                break;

            case FurnitureType.Fridge:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#2a313b"), 5, new Color("#6b7788"));
                ci.DrawLine(new Vector2(center.X, r.Position.Y + 6), new Vector2(center.X, r.End.Y - 6), new Color("#6b7788").WithAlpha(0.6f), 1.5f);
                ci.Box(new Rect2(center.X - 6, center.Y - 5, 2, 10), new Color("#9aa6b5"));
                ci.Box(new Rect2(center.X + 4, center.Y - 5, 2, 10), new Color("#9aa6b5"));
                break;

            case FurnitureType.AuxGenerator:
                PaintAuxGeneratorBody(ci, f);
                break;

            case FurnitureType.Cot:
                PaintCotBody(ci, f);
                break;

            case FurnitureType.Collector:
                PaintCollectorBody(ci, f);
                break;

            case FurnitureType.Refinery:
                PaintRefineryBody(ci, f);
                break;

            case FurnitureType.DroneDock:
                PaintDroneDockBody(ci, f);
                break;

            case FurnitureType.SensorArray:
            {
                // 장거리 센서 (v10.1): 받침 위의 레이더 접시와 안테나
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#141a24"), 5, new Color("#3a4a66"), 2);
                var c0 = r.GetCenter();
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.36f;
                ci.Circle(c0, rad, new Color("#0b1119"), true, -1f, true);
                ci.Arc(c0, rad, 0f, Mathf.Tau, 32, new Color("#4b5f80"), 2f, true);
                ci.Arc(c0, rad * 0.55f, 0f, Mathf.Tau, 24, new Color("#26344a"), 1f, true);
                ci.DrawLine(c0 + new Vector2(-rad, 0), c0 + new Vector2(rad, 0), new Color("#1d2838"), 1f);
                ci.DrawLine(c0 + new Vector2(0, -rad), c0 + new Vector2(0, rad), new Color("#1d2838"), 1f);
                break;
            }
            case FurnitureType.MainComputer:
            {
                // 서버 랙 두 줄 (v9.2)
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#12161e"), 5, new Color("#34405a"), 2);
                float half = r.Size.X * 0.5f;
                for (int k = 0; k < 2; k++)
                {
                    var rack = new Rect2(r.Position.X + 6 + k * half, r.Position.Y + 6, half - 9, r.Size.Y - 12);
                    ci.Box(rack, new Color("#0c0f15"));
                    for (int row = 0; row < 6; row++)
                        ci.DrawLine(new Vector2(rack.Position.X + 2, rack.Position.Y + 5 + row * (rack.Size.Y - 8) / 5f),
                            new Vector2(rack.End.X - 2, rack.Position.Y + 5 + row * (rack.Size.Y - 8) / 5f), new Color("#232b3a"), 1f);
                }
                break;
            }

            case FurnitureType.SuitLocker:
            {
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#1f2530"), 5, new Color("#3a4455"));
                var head = new Vector2(center.X, r.Position.Y + 16);
                ci.Circle(head, 7f, new Color("#d8dee8").WithAlpha(0.55f), true, -1f, true);
                ci.Circle(head + new Vector2(0, 1), 4f, new Color("#2a3a50"), true, -1f, true);
                Gfx.RoundRect(ci, new Rect2(center.X - 8, r.Position.Y + 25, 16, r.Size.Y - 32), new Color("#d8dee8").WithAlpha(0.35f), 5);
                break;
            }
        }
    }

    // ═══════════════════════════════ 동적 레이어 ═══════════════════════════════

    /// <summary>
    /// v17.7 동적 층 아래쪽 (배 바깥 · 흔적 · 바닥 상태 · 물건 · 설비 움직임): 사람이 없는 장식이라 멀리서 볼 때는 두세 프레임에 한 번만 다시 그린다.
    /// </summary>
    private void PaintDynamicUnder(CanvasItem ci)
    {
        var ship = _world.Ship;
        PaintNavLights(ci); // v10.9 항해등
        PaintRadiators(ci); // v9 선체 밖 방열판
        PaintExterior(ci); // v12.6 안테나·태양 날개
        PaintHullKind(ci); // v18.8 뼈대 · 용도마다 다른 선체 바깥 (바퀴 · 컨테이너 · 덧댄 판 · 집게 · 탱크 · 온실)
        PaintScorch(ci);
        PaintBlastScars(ci); // v16.13 폭발 흔적 (방사형 그을음 · 깨진 조명 · 날아간 문짝 · 추모)
        PaintBody(ci); // v16.3 칸 상태 · 열린 뚜껑 · 닳은 길 · 뗀 패널 · 관측창 · 문 잠금 · 벽 장착물
        PaintMatter(ci); // v16.4 물건 · 쏟은 액체 · 열기 · 전기 불꽃 · 바람 · 가루 (재질 · 상태마다 다른 그림)
        PaintWays(ci); // v16.25 급한 대로 쓴 갈래의 흔적 (매트리스 마개 · 벌린 문 · 젖은 수건 · 호스 · 속 빈 설비 …)
    }

    /// <summary>설비 움직임 (n 조각 중 k 번째 — 설비는 겹치지 않아 나눠 그려도 같다).</summary>
    private void PaintFixtureLifePart(CanvasItem ci, int k, int n)
    {
        int i = 0;
        foreach (var f in _world.Ship.Furniture)
        {
            if (i++ % n != k || f.Stowed || f.Room.Detached) continue;
            PaintFurnitureLife(ci, f);
        }
    }

    /// <summary>배 밖 멀리까지 가는 것 (떨어져 나간 방 · 다가오는 재난) — 구운 그림 밖이라 그대로 그린다.</summary>
    private void PaintDynamicFar(CanvasItem ci)
    {
        PaintFragments(ci); // 떨어져 나가 떠다니는 방 (우주선 밖이라 맨 아래)
        PaintCosmicUnder(ci); // v18.13 우주 대재난: 다가오는 것 · 자기력선 · 조석 · 스치는 잔해
    }

    /// <summary>
    /// 사람 · 로봇을 따라 움직이지 않는 동적 그림 ① 바닥 (소품 · 물건 · 어두운 방 · 이동식 장비 · 공사 · 겹쳐 보기 · 물): 초당 15~30번 다시 그려 한 장으로 굽는다.
    /// </summary>
    private void PaintSlowFloor(CanvasItem ci)
    {
        var ship = _world.Ship;
        var mode = _main.ViewMode;
        PaintTierBadges(ci); // v10.8
        PaintTechLookLive(ci); // v16.5b 움직이는 기술 모습 · 설치 · 업그레이드 순간
        PaintRoomProps(ci); // v15.8 소품·장식 (어두운 방은 아래에서 함께 어두워진다)
        PaintExplosives(ci, false); // v16.13 폭발성 물건 23종 (달아오름 · 쉭 · 카운트다운 · 잔해) — 들고 가는 것은 위 층
        PaintFood(ci, mode); // v16.8 냄비 · 항아리 · 남겨 둔 접시 · 냄새 (읽기만)

        // 정전된 방은 어둡게 (v9.4: 조명이 나간 방도)
        foreach (var room in ship.LiveRooms)
            if (room.Dark)
                foreach (var c in room.Cells) ci.Box(CellRect(c), new Color(0, 0, 0, room.Powered ? 0.36f : 0.42f));
        PaintPortable(ci, false); // v16.7 이동식 장비 (작업등 빛 웅덩이 · 케이블 · 호스 · 장비마다 다른 그림) — 든 것은 위 층
        PaintResearch(ci); // v16.14 실험대 장치 (분야마다) · 연구 노트 · 사고 그을음 · 돌파구 반짝임 · 보안경 · 돋보기
        PaintRoomWork(ci); // v16.17 방 공사 (분리 · 상자 · 손수레 · 골조 · 놓을 자리) · 쓰임 원판 · 헷갈림 · 땀
        PaintAnnex(ci); // v16.10 증축 (청사진 · 비계 · 골조 · 외판 · 압력계 · 배선 · 비닐 막 · 설비 · 개통식 · 새 패널)
        PaintSpaceUnder(ci, false); // v17.4 펼친 작업장 · 앞 상자 · 잠금표 · 줄 바닥 · 받는 곳 (카트 옮겨 싣기는 위 층)
        PaintAccessUnder(ci); // v17.6 색약 무늬 · v17.9 창밖 · 숨은 것 · 이상 현상 (ShipViewCurios.cs)
        PaintAfterUnder(ci); // v17.5 묵은 그을음 냄새 · 다른 방 쟁반 · 널어 둔 침구 · 냉장고 쪽지 · 빈 의자의 컵 · 종이꽃 · 다시 그린 그림 · 옮겨 온 등
        PaintJumpers(ci);

        foreach (var vm in new[] { mode, _main.SecondaryView ?? mode }.Distinct()) // v12.3 겹쳐 보기
        switch (vm)
        {
            case ViewMode.Power: PaintPowerOverlay(ci); break;
            case ViewMode.Air: PaintAirOverlay(ci); break;
            case ViewMode.Temperature: PaintTemperatureOverlay(ci); break;
            case ViewMode.Condition: PaintConditionOverlay(ci); break;
            case ViewMode.Trace: PaintTraceOverlay(ci); break;
            case ViewMode.Structure: PaintStructureOverlay(ci); break;
            case ViewMode.Pipes: PaintPipeOverlay(ci); break;
            case ViewMode.Sensors: PaintSensorOverlay(ci); break;
            case ViewMode.Ambience: PaintAmbienceOverlay(ci); break;
        }

        PaintWater(ci); // v12.3 바닥 물·결로·분전함 차단
    }

    /// <summary>② 뼈대: 벽 · 관 · 망 · 이음 · 변천 · 홀로그램 · 통신 · 인과 사슬 · 규모 · 장갑 · 방 상태 · 표지판.</summary>
    private void PaintSlowFrame(CanvasItem ci)
    {
        var ship = _world.Ship;
        var mode = _main.ViewMode;
        PaintWalls(ci);
        PaintPipes(ci, mode == ViewMode.Pipes); // v9: 벽·바닥 밑을 지나는 관 (배관 보기에서는 굵게)
        PaintNet(ci, mode); // v12.1 배 전체 망 (간선·급수관·덕트)
        if (_main.SecondaryView is ViewMode sv2 && sv2 != mode) PaintNet(ci, sv2);
        PaintJoints(ci, mode == ViewMode.Structure);
        PaintEvolution(ci);
        if (mode == ViewMode.Trace) PaintTraceMarks(ci);
        PaintHoloTable(ci); // v11.2 함교 홀로그램
        PaintComms(ci); // v11.2 보급·탈출 캡슐, 송신 파동
        PaintCauseChain(ci); // v12.2 고른 사고의 인과 사슬
        PaintScaleRooms(ci); // v16.18 번진 방마다 규모별 테두리 · 점호 깃발
        PaintFailsafe(ci, mode); PaintMajors(ci); // v16.19 장갑 벽 · 차압 문 · 칸막이 · 예비 회로 · 큰 사고 문양과 흔적
        PaintDampers(ci, mode == ViewMode.Air);
        PaintRoomStates(ci);
        PaintRoomSigns(ci); // v16.17 승무원이 붙인 이름 표지판 (나무 · 놋쇠 · 칠판 · 법랑)
    }

    /// <summary>③ 표시: 주컴퓨터 세계 · 정비 꼬리표 · 고장 표시 · 바닥에 남은 것.</summary>
    private void PaintSlowMarks(CanvasItem ci)
    {
        var ship = _world.Ship;
        PaintComputerWorld(ci); // v16.6 서버 랙 · 조작 빛 흐름 · 스피커 · 콘솔 경고 · 홀로그램 정보판 (ShipViewComputer.cs)
        PaintMateWorld(ci); // v16.27 늘린 장비 · 설치 장면 · 정찰 · 훈련 깃발 · 정비 꼬리표 · 식당 화면 · 딜레마 · 안부 쪽지 · 날씨 표지 · 기억 검사 (ShipViewMate.cs)

        foreach (var f in ship.Furniture)
        {
            if (f.Stowed || f.Room.Detached || f.Machine is not Machine m) continue;
            if (m.Grade == MachineGrade.Mk1 && !FixtureArt.Has(f.Type)) PaintMk1(ci, f, m); // v16.5b 표에 있는 설비는 FixtureArtTier 가 종류마다 손질을 그린다
            if (m.Has(FaultKind.Wrecked)) PaintWrecked(ci, f);
            if (m.Faults.Count == 0) continue;
            if (m.Has(FaultKind.Stripped)) PaintStripped(ci, f, m); // 뜯긴 설비는 경고 대신 조용한 흔적
            else PaintFaultMarker(ci, f, m);
        }
        PaintManeuver(ci); // v17.0 걸쇠 · 끈 · 냄비 집게 · 떨어진 물건 · 쏟은 자국 · 빗자루
        PaintEco(ci); // v18.2 · v18.3 · v18.4 화분 · 고양이 방석 · 밥그릇 · 바구미 포대 · 배수구 · 쓰레기통 · 합선 그을음
        PaintSchemesFloor(ci); // v18.14 꾸미는 일 (밀주 통 · 비밀 정원 · 방송 장비 · 덮개 · 압수 상자 · 팻말 · 깃발 줄)
        PaintHairClips(ci); // v17.1 바닥에 떨어진 머리카락
        PaintValuesFloor(ci); // v18.15 딜레마 표지판 · 밀항자 자리 · 꾸린 짐 · 식당 벽 쪽지
        PaintTalesFloor(ci); // v18.17 밤 모임 등불 · 둘러앉은 자리 (ShipViewTales.cs)
        PaintIncidentTraces(ci); // 압축-마 새 사고 30의 흔적 · 무르익은 원인의 낌새 (ShipViewTraces.cs)
    }

    /// <summary>매 프레임: 사람 · 로봇 · 드론과 그들이 든 것 · 불 · 연기 · 문 · 고른 것.</summary>
    private void PaintDynamic(CanvasItem ci)
    {
        var ship = _world.Ship;
        var mode = _main.ViewMode;
        PaintMatterCarried(ci); // v16.4 손에 든 물건
        PaintExplosives(ci, true); // 들고 가는 폭발성 물건
        PaintPortable(ci, true); // v16.7 사람이 든 이동식 장비 · 몸에 가린 작업등 그림자
        PaintDock(ci); // v18.5 도킹 통로 · 난파선 · 거룻배 · 압력계 · 교환 상자 · 이름판
        PaintSpaceUnder(ci, true); // v17.4 카트 옮겨 싣기
        PaintSmoke(ci);
        PaintToxin(ci); // v11.2 유독 가스
        PaintVenting(ci);
        PaintResponse(ci); // v13.0 진공·질식 소화 · 대피 카운트다운 · 공기 구역
        PaintFires(ci);
        PaintCosmicOver(ci); // v18.13 방사선 반짝임 · 물벽 · 꺼 둔 설비 · 봉쇄 · 충격파 앞머리
        foreach (var d in ship.Doors) if (!d.Removed) PaintDoor(ci, d); // v10.12 걷은 칸막이 문 · 떨어져 나간 방의 문은 벽이 됐다
        PaintComputerAsks(ci); // v16.16 사람 곁의 부탁 · 쉼 · 의심하는 계기

        if (_main.HoveredRoom is Room hr && hr != _main.SelectedRoom && !hr.Detached)
            PaintOutline(ci, hr, Palette.Room(hr.Kind).WithAlpha(0.35f), false);
        if (_main.SelectedRoom is Room sr && !sr.Detached)
            PaintOutline(ci, sr, Palette.Room(sr.Kind).WithAlpha(0.9f), true);
        if (_main.HoveredFurniture is Furniture hf && hf != _main.SelectedFurniture)
            Gfx.RoundRect(ci, FurnitureRect(hf).Grow(1f), new Color(1, 1, 1, 0f), 6, new Color(1, 1, 1, 0.35f), 2);
        if (_main.SelectedFurniture is Furniture sf)
            Gfx.RoundRect(ci, FurnitureRect(sf).Grow(2f + Mathf.Sin(_time * 4f)), new Color(1, 1, 1, 0.04f), 7, new Color(1, 1, 1, 0.85f), 2);

        if (_main.SelectedCrew is CrewMember sel) PaintPath(ci, sel);
        if (_main.SelectedRobot is Robot sr2 && sr2.State != RobotState.Lost)
        {
            var rp = RobotPx(sr2);
            ci.Arc(rp, 14f + Mathf.Sin(_time * 4f), 0f, Mathf.Tau, 32, new Color(1, 1, 1, 0.85f), 1.6f, true);
            if (sr2.Path is { Count: > 0 } rpath)
            {
                var prev = rp;
                for (int k = sr2.PathIndex; k < rpath.Count; k++)
                {
                    var q = CellRect(rpath[k]).GetCenter();
                    ci.DrawLine(prev, q, RobotColor(sr2.Kind).WithAlpha(0.45f), 1.5f, true);
                    prev = q;
                }
            }
        }
        else if (_main.HoveredRobot is Robot hr2) ci.Arc(RobotPx(hr2), 13f, 0f, Mathf.Tau, 28, new Color(1, 1, 1, 0.35f), 1.2f, true);
        PaintTethers(ci);
        PaintBelongings(ci); // v14.3 놓인 물건 · 손에 든 취미 물건 · 음표 · 판
        PaintInfo(ci); // v17.3 컵 · 깨진 조각 · 벽 사진 · 만든 것 · 독서등 · 그릇 더미 · 당번표 · 손목 단말
        PaintScenes(ci); // v16.1 진행 중 장면 (판 · 커피 · 국 자국 · 쪽지 · 스크린 · 만들다 만 소품)
        PaintRobots(ci); // v10.10 선내 로봇 (사람 밑에)
        PaintFleetLinks(ci); // v16.20b 견인 줄 · 같이 드는 들것 · 고치는 불꽃 · 명령선 · 잔해
        PaintCouncilFloor(ci); // v18.18 둘러앉는 방석 · 의장 탁자 · 재판석 · 투표함 · 잔치 깃발
        PaintBlackbox(ci); // v18.7 블랙박스 상자 · 숨은 실수의 흔적 · 조사 자리의 기록 띠 · 숨긴 사람의 몸짓
        PaintPersonalFloor(ci); // v18.1 밀린 편지 더미 · v18.9 맞바꾸는 물건
        // 쓰러진 사람은 밑에, 업힌 사람은 업은 사람 위에
        foreach (var c in _world.Crew.OrderBy(c => c.CarriedBy != null ? 2 : c.Down ? 0 : 1)) PaintCrew(ci, c);
        PaintDockOver(ci); // v18.5 헬멧 등 · v18.6 승객 차림 · 이끄는 손
        PaintManeuverOver(ci); // v17.0 침대 끈 · 손잡이 쥔 손 · 컵 쥔 손 · 넘어진 사람 · 빗자루질
        PaintZeroGOver(ci); // v18.4 떠다니는 것 · 벽 손잡이 · 멀미 · v18.2 고양이
        PaintCommandBadges(ci); // v13.1 선장 별 · 지휘자 테 · 조 배지
        PaintStations(ci); // v16.21 비상 배치 완장 · 거드는 손 · 정신 차리게 한 손
        PaintMeeting(ci); // v13.2 회의 장면 · 발언 말풍선
        PaintSchemesOver(ci); // v18.14 쉿 · 귓속말 · 냄새 따라 · 팻말 · 빚 · 고른 사람이 아는 일
        PaintCouncilOver(ci); // v18.18 파벌 완장 · 서명 종이 · 손 들기 · 투표용지 · 증언 · 불만 구름 · 본 눈 · 벌 근무
        PaintValuesOver(ci); // v18.15 끄덕임 · 고개 젓기 · 금 간 별 · 엉킨 실타래
        PaintTalesOver(ci); // v18.16 · v18.17 이야기 상징 · 대화 카드 표시 · 연인 · 질투 (ShipViewTales.cs)
        PaintMinds(ci); // v13.3 공황 · 영웅심 · 분노 · 모름
        PaintEmotions(ci); // v16.15 머리 위 감정 그림 (분노 · 두려움 · 기쁨 · 슬픔 · 수치 · 자부심)
        PaintTalk(ci); // v14.4 말풍선 (목적 있는 대화 · 인수인계 · 깨우기)
        PaintSpaceOver(ci); // v17.4 줄 번호 · 새치기 · 둘이 드는 짐 · 예약 · 구경꾼 · "비켜!" · 소문
        PaintAccessOver(ci); // v17.6 방 스피커 음악 · 색약 표식
        PaintAfterOver(ci); // v17.5 악몽에 뒤척임 · 놀라 깸 · 안고 가는 침구 · 들고 가는 등
        PaintSounds(ci); // v17.2 소리 표시 (발소리 · 베어링 · 팬 · 물방울 · 삐걱 · 무전 · 흥얼 …) · 문 너머로 넘는 소리
        PaintComputerTop(ci, mode); // v16.6 선내 방송 말풍선 · 컴퓨터가 보는 배
        PaintDrones(ci);
        PaintIncoming(ci);
        PaintImpacts(ci);
        PaintBlasts(ci); // v16.13 폭발 연출 (섬광 · 화구 · 벽에 막히는 충격파 · 파편 · 불똥 · 연기 · 흔들림)
        PaintToolPreview(ci);
    }

    private void PaintFurnitureLife(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f);
        var center = r.GetCenter();
        var accent = Palette.Room(f.Room.Kind);
        float t = _time + f.Id * 0.73f;
        var m = f.Machine;
        float eff = m?.Efficiency ?? 1f;
        bool alive = eff > 0.01f;
        if (PaintFixtureLife(ci, f)) return; // v16.5c 설비 그림 표 (움직임 · 상태)
        if (Modules.IsModule(f.Type)) { PaintModuleLife(ci, f, t); return; } // v10.8
        if (f.Type == FurnitureType.SupplyCache) { PaintSupplyCacheLife(ci, f); return; } // v10.10
        if (m is { Tier: >= 2 } && f.Type != FurnitureType.ReactorCore) PaintTierLife(ci, f, m, t); // v11.3 단계마다 다른 모양

        switch (f.Type)
        {
            case FurnitureType.ReactorCore when m is { Tier: >= 3 }:
                PaintFusion(ci, f, t); // v10.8 핵융합로
                break;
            case FurnitureType.ReactorCore:
            {
                float rs = Mathf.Min(r.Size.X, r.Size.Y) / (3f * T);
                float load = Mathf.Clamp(_world.Power.ReactorOutput / Mathf.Max(1f, _world.Power.ReactorRated), 0.05f, 1f);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * (1f + load));
                var hot = _world.Power.ReactorTemperature > 380f ? Palette.Danger : accent;
                ci.Circle(center, 26f * rs, hot.WithAlpha((0.04f + 0.05f * pulse) * load + 0.02f), true, -1f, true);
                ci.Circle(center, 19f * rs, hot.WithAlpha((0.1f + 0.06f * pulse) * load + 0.03f), true, -1f, true);
                ci.Circle(center, (7f + 6f * load + 1.5f * pulse) * rs, hot.WithAlpha(0.9f), true, -1f, true);
                ci.Circle(center, 5f * rs, new Color(1f, 0.95f, 0.84f, 0.95f), true, -1f, true);
                float a = t * 0.7f * load;
                ci.Arc(center, 24f * rs, a, a + 1.3f, 20, hot.WithAlpha(0.6f), 2f, true);
                ci.Arc(center, 24f * rs, a + Mathf.Pi, a + Mathf.Pi + 1.3f, 20, hot.WithAlpha(0.6f), 2f, true);
                if (m is { Tier: 2 }) ci.Arc(center, 33f * rs, 0f, Mathf.Tau, 48, Hud.TierColor(2).WithAlpha(0.35f), 1.5f, true); // 개량형: 보강 링
                break;
            }
            case FurnitureType.EngineCore:
            {
                // v11.2: 엔진은 연소할 때만 불꽃이 크다 (회피 기동·항로 변경). 평소엔 노즐 속 파일럿 불만
                var prop = _world.Propulsion;
                bool burn = prop.Burning || prop.CourseBurnVisible;
                bool standby = m != null && !m.Stopped && (m.Powered || m.Spec.PowerDraw <= 0f);
                float power = burn && alive ? 1f : standby ? 0.16f : 0f;
                float x0 = (f.MinX - 1) * T - 8f - NozzleLen; // v10.9: 노즐 끝에서 나온다
                float y0 = f.MinY * T + 4f, y1 = (f.MaxY + 1) * T - 4f;
                float yc = (y0 + y1) * 0.5f, h = y1 - y0;
                float flicker = 0.88f + 0.08f * Mathf.Sin(t * 11f) + 0.04f * Mathf.Sin(t * 23f);
                if (power > 0f)
                {
                    float len = T * (burn ? 3.4f : 0.5f) * flicker * (0.4f + 0.6f * Mathf.Max(0.3f, prop.Thrust));
                    var plume = new[] { new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x0 - len, yc + h * 0.12f), new Vector2(x0 - len, yc - h * 0.12f) };
                    ci.Polygon(plume, new[] { accent.WithAlpha(0.5f), accent.WithAlpha(0.5f), accent.WithAlpha(0f), accent.WithAlpha(0f) });
                    float len2 = len * 0.55f;
                    var core = new[] { new Vector2(x0, yc - h * 0.25f), new Vector2(x0, yc + h * 0.25f), new Vector2(x0 - len2, yc + 2f), new Vector2(x0 - len2, yc - 2f) };
                    var hotc = new Color(1f, 0.86f, 0.72f);
                    ci.Polygon(core, new[] { hotc.WithAlpha(0.75f), hotc.WithAlpha(0.75f), hotc.WithAlpha(0f), hotc.WithAlpha(0f) });
                }
                if (power > 0f) ci.DrawLine(new Vector2(x0, y0 - 2f), new Vector2(x0, y1 + 2f), accent.Lightened(0.3f).WithAlpha((burn ? 0.8f : 0.25f) * flicker), 2f); // 노즐 끝이 달아오른다
                if (burn)
                {
                    // 연소 중: 배 옆구리의 자세 제어 분사 (회피 기동)
                    for (int k = 0; k < 4; k++)
                    {
                        float ph = (t * 3f + k * 0.25f) % 1f;
                        ci.Circle(new Vector2(x0 - T * 3.4f * ph, yc + (k % 2 == 0 ? -1f : 1f) * h * 0.2f * ph), 3f + 7f * ph, new Color(1f, 0.8f, 0.6f, 0.25f * (1f - ph)), true, -1f, true);
                    }
                }
                ci.Box(new Rect2(r.Position.X + 8, r.Position.Y + 10, 6, r.Size.Y - 20), accent.WithAlpha(power * (0.35f + 0.25f * flicker)));
                break;
            }
            case FurnitureType.SensorArray:
            {
                // 레이더 스윕: 돌면 초록 부채꼴이 돌고, 궤적을 잡으면 붉은 점, 멈추면 붉게 깜빡인다
                var c0 = r.GetCenter();
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.36f;
                var sens = _world.Sensors;
                if (sens.Online)
                {
                    float ang = t * 2.2f;
                    var sweep = new Color("#6ee7b7");
                    for (int k = 0; k < 6; k++)
                    {
                        float a0 = ang - k * 0.12f;
                        ci.DrawLine(c0, c0 + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rad, sweep.WithAlpha(0.5f * (1f - k / 6f) * sens.Quality), 2f, true);
                    }
                    foreach (var inc in sens.Incoming.Where(x => x.Warned != WarnLevel.None))
                    {
                        var d = new Vector2(-inc.Direction.X, -inc.Direction.Y);
                        float far = Mathf.Clamp(inc.MinutesLeft(_world.Tick) / SensorSystem.ApproachMinutes, 0.15f, 1f);
                        ci.Circle(c0 + d * rad * far, 2.2f, Palette.Danger.WithAlpha(0.6f + 0.4f * Mathf.Sin(t * 8f)), true, -1f, true);
                    }
                }
                else if (Mathf.Sin(t * 3f) > 0f) ci.Circle(c0, 2.5f, Palette.Danger, true, -1f, true);
                break;
            }
            case FurnitureType.MainComputer:
            {
                // 깜빡이는 표시등: 돌면 청록, 멈추면 붉게 한두 개만, 달아오르면 주황 아지랑이
                float half = r.Size.X * 0.5f;
                bool online = _world.Automation.MainOnline;
                for (int k = 0; k < 2; k++)
                for (int row = 0; row < 5; row++)
                for (int led = 0; led < 3; led++)
                {
                    var p = new Vector2(r.Position.X + 10 + k * half + led * 5f, r.Position.Y + 11 + row * (r.Size.Y - 20) / 5f);
                    float blink = Hash(f.Id * 7 + k, row * 3 + led, 11);
                    bool on = online ? Mathf.Sin(t * (3f + 5f * blink) + blink * 9f) > -0.2f : led == 0 && row == 0 && Mathf.Sin(t * 4f) > 0f;
                    var col = online ? (blink > 0.8f ? new Color("#ffd166") : new Color("#5fd0c8")) : Palette.Danger;
                    if (on) ci.Circle(p, 1.4f, col.WithAlpha(online ? 0.85f : 0.9f), true, -1f, true);
                }
                float heat = Mathf.Clamp((f.Room.Air.Temperature - 28f) / 12f, 0f, 1f);
                if (heat > 0.05f)
                    for (int k = 0; k < 4; k++)
                    {
                        float ph = Mathf.PosMod(t * 0.7f + k * 0.25f, 1f);
                        ci.Circle(new Vector2(r.Position.X + r.Size.X * (0.2f + 0.2f * k), r.Position.Y + r.Size.Y * (1f - ph)),
                            3f + 6f * ph, new Color("#ff8a4a").WithAlpha(0.18f * heat * (1f - ph)), true, -1f, true);
                    }
                break;
            }
            case FurnitureType.Console:
            {
                var screen = r.Grow(-8f);
                if (!alive) { ci.Box(screen, new Color("#0b0e13")); break; }
                ci.Box(screen, accent.WithAlpha(0.2f + 0.08f * Mathf.Sin(t * 2.1f)));
                float sy = screen.Position.Y + Mathf.PosMod(t * 7f, screen.Size.Y);
                ci.DrawLine(new Vector2(screen.Position.X, sy), new Vector2(screen.End.X, sy), accent.WithAlpha(0.55f), 1f);
                for (int k = 0; k < 3; k++)
                {
                    float bh = 2f + 4f * (0.5f + 0.5f * Mathf.Sin(t * (1.3f + k * 0.7f) + k));
                    ci.Box(new Rect2(screen.Position.X + 3 + k * 5, screen.End.Y - 2 - bh, 3, bh), accent.WithAlpha(0.7f));
                }
                break;
            }
            case FurnitureType.OxygenGenerator:
            {
                int k = 0;
                foreach (float fy in new[] { 0.28f, 0.72f })
                {
                    var p = new Vector2(center.X, r.Position.Y + r.Size.Y * fy);
                    float pulse = 0.5f + 0.5f * Mathf.Sin(t * 1.8f + k * 1.9f);
                    ci.Circle(p, 6f + 2f * pulse * eff, accent.WithAlpha((0.25f + 0.25f * pulse) * eff + 0.05f), true, -1f, true);
                    k++;
                }
                if (!alive) break;
                for (int b = 0; b < 3; b++)
                {
                    float travel = r.Size.Y - 18f;
                    float y = r.End.Y - 9f - Mathf.PosMod(t * 12f * eff + b * travel / 3f, travel);
                    float x = r.Position.X + 10f + b * (r.Size.X - 20f) / 2f;
                    ci.Circle(new Vector2(x, y), 1.8f, accent.WithAlpha(0.45f), true, -1f, true);
                }
                break;
            }
            case FurnitureType.CoolantPump:
            {
                float spin = alive ? _time * 6f * eff + f.Id : f.Id;
                var col = alive ? accent : Palette.Danger;
                for (int k = 0; k < 3; k++)
                {
                    var d = Vector2.FromAngle(spin + k * Mathf.Tau / 3f);
                    ci.DrawLine(center + d * 4f, center + d * 15f, col.WithAlpha(alive ? 0.75f : 0.5f), 3f, true);
                }
                ci.Circle(center, 4.5f, col.WithAlpha(0.9f), true, -1f, true);
                break;
            }
            case FurnitureType.PowerPanel:
            {
                var pw = _world.Power;
                for (int k = 0; k < PowerGrid.CircuitCount; k++)
                {
                    bool live = pw.CircuitLive[k];
                    bool manualOff = pw.ManualOff[k];
                    bool jumpered = !live && pw.CircuitFed[k];
                    float x = r.Position.X + 10 + k * (r.Size.X - 20) / (PowerGrid.CircuitCount - 1);
                    var sw = new Rect2(x - 4, r.Position.Y + 8, 8, 12);
                    Gfx.RoundRect(ci, sw, new Color("#11151b"), 2);
                    // 사람이 일부러 내린 회로는 깜빡이지 않는 회색, 임시 배선으로 받는 회로는 주황
                    var col = manualOff ? Palette.TextMuted : jumpered ? JumperColor : live ? Palette.Good : Palette.Danger;
                    float blink = live || manualOff || jumpered ? 1f : 0.5f + 0.5f * Mathf.Sin(_time * 8f);
                    bool up = live && !manualOff || jumpered;
                    ci.Box(new Rect2(x - 2.5f, up ? sw.Position.Y + 2 : sw.End.Y - 6, 5, 4), col.WithAlpha(blink));
                }
                break;
            }
            case FurnitureType.Battery:
            {
                float charge = _world.Power.BatteryPercent;
                var col = charge < 0.2f ? Palette.Danger : charge < 0.5f ? new Color("#f5d547") : Palette.Good;
                int bars = f.Width >= 2 ? 3 : 1; // 항해 중에 단 한 칸짜리 모듈은 막대 하나
                for (int k = 0; k < bars; k++)
                {
                    var cell = bars == 1 ? new Rect2(r.Position.X + 11, r.Position.Y + 8, 10, r.Size.Y - 16)
                        : new Rect2(r.Position.X + 11 + k * 15, r.Position.Y + 11, 9, r.Size.Y - 22);
                    float h = cell.Size.Y * charge;
                    ci.Box(new Rect2(cell.Position.X, cell.End.Y - h, cell.Size.X, h), col.WithAlpha(0.55f));
                }
                if (_world.Power.BatteryFlow < -0.1f)
                    ci.Circle(new Vector2(r.End.X - 9, r.Position.Y + 9), 2.5f, Palette.Warning.WithAlpha(0.5f + 0.5f * Mathf.Sin(_time * 6f)), true, -1f, true);
                break;
            }
            case FurnitureType.GrowBed when m?.Crop is CropState crop:
            {
                // 생장등
                if (alive) ci.Box(new Rect2(r.Position.X + 5, r.Position.Y + 4, r.Size.X - 10, 2), new Color("#d77cff").WithAlpha(0.35f + 0.1f * Mathf.Sin(t)));
                int plants = f.Width * 2;
                for (int k = 0; k < plants; k++)
                {
                    float x = r.Position.X + 10 + k * (r.Size.X - 20) / (plants - 1);
                    float y = center.Y + ((k & 1) == 0 ? -2 : 2);
                    float size = 2f + 6f * crop.Growth;
                    float sway = Mathf.Sin(_time * 1.3f + k) * 0.6f;
                    var leaf = new Color("#5fae3e").Lerp(new Color("#8fd65a"), crop.Care) * (alive ? 1f : 0.6f);
                    ci.Circle(new Vector2(x + sway, y), size, leaf.WithAlpha(0.9f), true, -1f, true);
                    ci.Circle(new Vector2(x + sway - size * 0.3f, y - size * 0.3f), size * 0.5f, leaf.Lightened(0.25f).WithAlpha(0.8f), true, -1f, true);
                    if (crop.Ripe && (k % 2 == 0))
                        ci.Circle(new Vector2(x + sway + 2, y + 1), 2.2f, new Color("#ff8a5c"), true, -1f, true);
                }
                PaintBlight(ci, f, crop); // v11.2
                break;
            }
            case FurnitureType.WaterRecycler:
            {
                var tank = new Rect2(r.Position.X + 12, r.Position.Y + 10, r.Size.X - 24, r.Size.Y - 20);
                float level = _world.Water.Level / _world.Water.Capacity;
                float h = tank.Size.Y * level;
                ci.Box(new Rect2(tank.Position.X + 2, tank.End.Y - h, tank.Size.X - 4, h), new Color("#3a8fd9").WithAlpha(0.55f));
                if (alive)
                {
                    float wy = tank.End.Y - h + 1.5f * Mathf.Sin(_time * 2f);
                    ci.DrawLine(new Vector2(tank.Position.X + 2, wy), new Vector2(tank.End.X - 2, wy), new Color("#9ad0ff").WithAlpha(0.7f), 1.2f);
                }
                break;
            }
            case FurnitureType.Stove when m != null && m.Active:
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 7f);
                foreach (float fx in new[] { 0.28f, 0.72f })
                {
                    var p = new Vector2(r.Position.X + r.Size.X * fx, center.Y);
                    ci.Circle(p, 8f, new Color("#ff7a3c").WithAlpha(0.25f * pulse), true, -1f, true);
                    ci.Arc(p, 8f, 0f, Mathf.Tau, 24, new Color("#ff9a5c").WithAlpha(0.9f * pulse), 2f, true);
                }
                break;
            }
            case FurnitureType.Fridge:
            {
                var led = alive ? Palette.Good : Palette.Danger;
                ci.Circle(new Vector2(r.End.X - 8, r.Position.Y + 8), 2f, led.WithAlpha(0.85f), true, -1f, true);
                float fill = f.Storage!.Total / (float)f.Storage.Capacity;
                ci.Box(new Rect2(r.Position.X + 6, r.End.Y - 7, (r.Size.X - 12) * fill, 2), Palette.Item(ItemKind.Produce).WithAlpha(0.7f));
                PaintTaint(ci, f); // v11.2
                break;
            }
            case FurnitureType.MealDispenser:
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.5f);
                ci.Circle(new Vector2(r.End.X - 8, r.Position.Y + 8), 2f, (alive ? accent : Palette.Danger).WithAlpha(0.4f + 0.5f * pulse), true, -1f, true);
                int meals = f.Storage!.Count(ItemKind.Meal);
                for (int k = 0; k < System.Math.Min(5, (meals + 3) / 4); k++)
                    ci.Box(new Rect2(r.Position.X + 7 + k * 4, r.Position.Y + 9, 3, 6), Palette.Item(ItemKind.Meal).WithAlpha(0.8f));
                PaintTaint(ci, f); // v11.2
                break;
            }
            case FurnitureType.AuxGenerator:
                PaintAuxGeneratorLife(ci, f);
                break;
            case FurnitureType.Collector:
                PaintCollectorLife(ci, f);
                break;
            case FurnitureType.Refinery:
                PaintRefineryLife(ci, f);
                break;
            case FurnitureType.SuitLocker:
            {
                // 걸려 있는 우주복 수만큼 불이 켜진다
                int suits = f.Storage!.Count(ItemKind.Suit);
                for (int k = 0; k < f.Storage.Capacity; k++)
                    ci.Circle(new Vector2(r.Position.X + 8 + k * 6, r.End.Y - 7), 2f,
                        (k < suits ? Palette.Good : Palette.TextMuted).WithAlpha(0.85f), true, -1f, true);
                break;
            }
            case FurnitureType.Shelf:
            {
                Color[] crates = { new("#363e4c"), new("#463c30"), new("#2a423e"), new("#3b3446") };
                float fill = f.Storage!.Total / (float)f.Storage.Capacity;
                int show = Mathf.CeilToInt(fill * f.Cells.Count * 2);
                int i = 0;
                foreach (var c in f.Cells)
                {
                    var cr = CellRect(c);
                    for (int half = 0; half < 2; half++, i++)
                    {
                        if (i >= show) break;
                        int h = (c.X * 7 + c.Y * 13 + half) & 3;
                        Gfx.RoundRect(ci, new Rect2(cr.Position.X + 5 + half * 12, cr.Position.Y + 7, 10, 18), crates[h], 2);
                    }
                }
                break;
            }
        }

        // 전기가 안 들어오는 설비는 꺼진 듯 어둡게
        if (m != null && m.Spec.PowerDraw > 0f && !m.Powered)
            Gfx.RoundRect(ci, r.Grow(-2f), new Color(0, 0, 0, 0.35f), 5);
    }

    private void PaintFaultMarker(CanvasItem ci, Furniture f, Machine m)
    {
        var r = FurnitureRect(f);
        if (!m.Stopped && m.Faults.All(x => x.Stage > 0))
        {
            // 임시로 살려 둔 설비: 깜빡이지 않는 노란 게이지 (몇 %로 돌고 있는지)
            var gp = new Vector2(r.End.X - 4, r.Position.Y + 4);
            float frac = m.Faults.Min(x => Fault.StageFloor(x.Stage));
            ci.Circle(gp, 6f, new Color("#1a160c"), true, -1f, true);
            ci.Arc(gp, 4.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * frac, 16, new Color("#f5d547"), 2.5f, true);
            ci.Arc(gp, 6f, 0f, Mathf.Tau, 16, new Color("#f5d547").WithAlpha(0.6f), 1f, true);
            return;
        }
        float blink = 0.55f + 0.45f * Mathf.Sin(_time * 6f);
        bool stopped = m.Stopped || m.Faults.Any(x => x.Circuit >= 0);
        var col = stopped ? Palette.Danger : Palette.Warning;
        Gfx.RoundRect(ci, r.Grow(1f), new Color(0, 0, 0, 0f), 6, col.WithAlpha(0.7f * blink), 2);
        var p = new Vector2(r.End.X - 2, r.Position.Y + 2);
        var tri = new[] { p + new Vector2(0, -8), p + new Vector2(7, 5), p + new Vector2(-7, 5) };
        ci.Poly(tri, col.WithAlpha(blink));
        ci.Box(new Rect2(p.X - 0.8f, p.Y - 3.5f, 1.6f, 4.5f), new Color("#1a1010"));
        ci.Box(new Rect2(p.X - 0.8f, p.Y + 2f, 1.6f, 1.5f), new Color("#1a1010"));
    }

    // ── 보기 모드 오버레이 ──

    private void FillRoom(CanvasItem ci, Room room, Color color)
    {
        foreach (var c in room.Cells) ci.Box(CellRect(c), color);
    }

    /// <summary>v9.3 저출력 운영으로 사람이 내려 둔 설비 (고장·정전과 구별되는 차분한 파랑).</summary>
    private static readonly Color ParkedColor = new("#7aa2c8");

    private void PaintPowerOverlay(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            if (room.Powered) FillRoom(ci, room, Palette.Good.WithAlpha(0.07f));
            else
            {
                FillRoom(ci, room, Palette.Danger.WithAlpha(0.16f));
                foreach (var c in room.Cells)
                    if ((c.X + c.Y) % 3 == 0)
                    {
                        var r = CellRect(c);
                        ci.DrawLine(r.Position + new Vector2(0, T), r.Position + new Vector2(T, 0), Palette.Danger.WithAlpha(0.35f), 1.5f);
                    }
            }
        }
        foreach (var f in _world.Ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached))
        {
            if (f.Machine is not Machine m || m.Spec.PowerDraw <= 0f) continue;
            var r = FurnitureRect(f);
            int circuit = f.Room.Circuit;
            var col = _world.Power.ManualOff[circuit] ? Palette.TextMuted
                : m.Parked ? ParkedColor // v9.3 저출력 운영으로 내려 둠
                : !_world.Power.CircuitFed[circuit] ? Palette.Danger
                : !m.Powered ? Palette.Warning
                : !_world.Power.CircuitLive[circuit] ? JumperColor
                : Palette.Good;
            var p = new Vector2(r.Position.X + 6, r.Position.Y + 6);
            ci.Circle(p, 4.5f, new Color("#0b0e13"), true, -1f, true);
            var bolt = new[] { p + new Vector2(1, -3.5f), p + new Vector2(-2, 0.5f), p + new Vector2(0.2f, 0.5f), p + new Vector2(-1, 3.5f), p + new Vector2(2, -0.5f), p + new Vector2(-0.2f, -0.5f) };
            ci.Poly(bolt, col);
            if (m.Parked) ci.DrawLine(p + new Vector2(-5f, 5f), p + new Vector2(5f, -5f), ParkedColor, 1.5f, true);
        }
    }

    private void PaintAirOverlay(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            var air = room.Air;
            if (air.Pressure < 92f)
            {
                // 기압이 빠진 방: 붉은 빗금 (진공일수록 진하게)
                float vac = Mathf.Clamp((95f - air.Pressure) / 60f, 0f, 1f);
                FillRoom(ci, room, Palette.Danger.WithAlpha(0.08f + 0.2f * vac));
                foreach (var c in room.Cells)
                {
                    if ((c.X + c.Y) % 2 != 0) continue;
                    var r = CellRect(c);
                    ci.DrawLine(r.Position + new Vector2(0, T), r.Position + new Vector2(T, 0), Palette.Danger.WithAlpha(0.15f + 0.3f * vac), 1.5f);
                }
                continue;
            }
            float bad = Mathf.Clamp((20.5f - air.O2) / 5f, 0f, 1f);
            var col = bad < 0.05f ? Palette.Accent : Palette.Severity(0.35f + bad * 0.65f);
            FillRoom(ci, room, col.WithAlpha(0.12f + 0.1f * bad));
            float haze = Mathf.Clamp((air.CO2 - 0.3f) / 2.5f, 0f, 1f);
            if (haze > 0f) FillRoom(ci, room, new Color(0.55f, 0.55f, 0.5f, 0.3f * haze));
        }
    }

    private void PaintTemperatureOverlay(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            float temp = room.Air.Temperature;
            Color col;
            if (temp < 19f) col = new Color("#6cb8ff").WithAlpha(Mathf.Clamp((21f - temp) / 14f, 0.08f, 0.35f));
            else if (temp > 24f) col = new Color("#ff7a5c").WithAlpha(Mathf.Clamp((temp - 22f) / 18f, 0.08f, 0.4f));
            else col = new Color(1f, 1f, 1f, 0.04f);
            FillRoom(ci, room, col);
        }
    }

    private void PaintConditionOverlay(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms) FillRoom(ci, room, new Color(0, 0, 0, 0.35f));
        foreach (var f in _world.Ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached))
        {
            if (f.Machine is not Machine m) continue;
            var r = FurnitureRect(f).Grow(-1f);
            float bad = m.Faults.Count > 0 ? 1f : m.Wear;
            var col = Palette.Severity(bad);
            Gfx.RoundRect(ci, r, col.WithAlpha(0.22f), 5, col.WithAlpha(0.85f), 2);
            // 수명 막대
            var bar = new Rect2(r.Position.X + 3, r.End.Y - 5, r.Size.X - 6, 3);
            ci.Box(bar, new Color(0, 0, 0, 0.6f));
            ci.Box(new Rect2(bar.Position, new Vector2(bar.Size.X * m.Condition, bar.Size.Y)), new Color(1, 1, 1, 0.7f));
        }
        foreach (var (cell, wall) in _world.Ship.Walls)
        {
            if (wall.Integrity >= 0.999f) continue;
            ci.Box(CellRect(cell), Palette.Severity(1f - wall.Integrity).WithAlpha(0.5f));
        }
    }

    private void PaintDoor(CanvasItem ci, Door d)
    {
        var r = CellRect(d.Cell);
        float open = Mathf.SmoothStep(0f, 1f, d.Openness);
        float half = T * 0.5f * (1f - open);
        float th = d.IsExternal ? 12f : d.Bulkhead ? 11f : 8f; // v12.6 구획 격벽 문은 두껍다
        var panel = d.IsExternal ? new Color("#4a4f5c") : d.Powered ? new Color("#3a4456") : new Color("#3e3336");
        var light = d.IsExternal ? Palette.Warning.WithAlpha(0.7f)
                  : !d.Powered ? Palette.Danger.WithAlpha(0.55f)
                  : open > 0.05f ? Palette.Good.WithAlpha(0.85f) : Palette.Accent.WithAlpha(0.5f);

        // 문짝은 벽 방향을 따라 미끄러진다. from/len은 벽 방향 위치, th는 두께.
        void Band(float from, float len, Color c)
        {
            if (d.ConnectsVertically)
                ci.Box(new Rect2(r.Position.X + from, r.GetCenter().Y - th * 0.5f, len, th), c);
            else
                ci.Box(new Rect2(r.GetCenter().X - th * 0.5f, r.Position.Y + from, th, len), c);
        }

        Band(0f, 3f, Palette.WallEdge);
        Band(T - 3f, 3f, Palette.WallEdge);
        if (half > 0.5f)
        {
            Band(0f, half, panel);
            Band(T - half, half, panel);
            Band(half - 2f, 2f, light);
            Band(T - half, 2f, light);
            if (d.IsExternal)
                for (float s = 3f; s < T - 3f; s += 6f) Band(s, 2.5f, new Color("#f5d547").WithAlpha(0.4f));
            else if (d.Bulkhead)
                for (float s = 2f; s < half - 2f; s += 4f) { Band(s, 1.5f, new Color("#e0a050").WithAlpha(0.55f)); Band(T - s - 1.5f, 1.5f, new Color("#e0a050").WithAlpha(0.55f)); }
        }
        if (d.Locked && ((d.RoomA?.Abandoned ?? false) || (d.RoomB?.Abandoned ?? false)))
        {
            // 포기한 구획의 격벽: 두꺼운 판을 대고 용접했다
            Band(1f, T - 2f, new Color("#4b505c"));
            for (float s = 4f; s < T - 3f; s += 5f) Band(s, 1.6f, WeldColor.WithAlpha(0.8f));
        }
        else if (d.Locked)
        {
            // 잠긴 격벽: 붉은·노란 경고 줄무늬와 자물쇠
            float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 5f);
            for (float s = 3f; s < T - 3f; s += 6f) Band(s, 3f, (((int)(s / 6f) & 1) == 0 ? Palette.Danger : new Color("#f5d547")).WithAlpha(0.75f * pulse));
            var c = r.GetCenter();
            Gfx.RoundRect(ci, new Rect2(c.X - 4.5f, c.Y - 2f, 9f, 7f), new Color("#1a0d10"), 1.5f, Palette.Danger);
            ci.Arc(c + new Vector2(0, -2f), 3f, Mathf.Pi, Mathf.Tau, 10, Palette.Danger, 1.5f, true);
        }
        // v9.4 문 구동기: 망가지면 주황 톱니(손으로만), 임시 구동기면 노란 점
        if (d.MotorBroken && !d.Removed)
        {
            var c = r.GetCenter() + (d.ConnectsVertically ? new Vector2(T * 0.32f, -T * 0.32f) : new Vector2(T * 0.32f, -T * 0.32f));
            ci.Circle(c, 5f, new Color("#1a120c"), true, -1f, true);
            ci.Arc(c, 3.6f, 0f, Mathf.Tau, 12, new Color("#f0883e"), 1.6f, true);
            for (int i = 0; i < 6; i++)
            {
                float ang = i * Mathf.Tau / 6f;
                ci.DrawLine(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 3.6f, c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 5.4f, new Color("#f0883e"), 1.4f);
            }
            ci.DrawLine(c + new Vector2(-4f, 4f), c + new Vector2(4f, -4f), Palette.Danger, 1.4f, true);
        }
        else if (d.MotorMk1 && !d.Removed)
            ci.Circle(r.GetCenter() + new Vector2(T * 0.32f, -T * 0.32f), 2.6f, new Color("#f5d547"), true, -1f, true);
    }

    private void PaintOutline(CanvasItem ci, Room room, Color color, bool fill)
    {
        if (fill)
            foreach (var c in room.Cells) ci.Box(CellRect(c), color.WithAlpha(0.05f));
        foreach (var (a, b) in Outline(room)) ci.DrawLine(a, b, color, 2f);
    }

    private void PaintPath(CanvasItem ci, CrewMember c)
    {
        if (c.Path == null || c.Destination is not Cell dest) return;
        var color = Palette.Crew(c.Id).WithAlpha(0.6f);
        var prev = CrewPx(c);
        for (int i = c.PathIndex; i < c.Path.Count; i++)
        {
            var p = ToPx(c.Path[i].Center);
            ci.DrawDashedLine(prev, p, color, 2f, 5f);
            prev = p;
        }
        var dp = ToPx(dest.Center);
        ci.Arc(dp, 7f, 0f, Mathf.Tau, 24, color, 1.5f, true);
        ci.Circle(dp, 2.5f, color, true, -1f, true);
    }

    private void PaintCrew(CanvasItem ci, CrewMember c)
    {
        var p = CrewPx(c) + new Vector2(c.Gait.Aside.X, c.Gait.Aside.Y) * T; // v14.5 비켜선 몸
        var col = Palette.Crew(c.Id);
        if (c.Vitals.Health < 0.5f) col = col.Lerp(new Color("#8a8f99"), 0.4f);
        bool selected = _main.SelectedCrew == c;
        bool hovered = _main.HoveredCrew == c;
        float radius = CrewRadius * (c.IsChild ? 0.55f + 0.03f * c.Age : 1f); // v12.9 아이는 작다
        float s = radius / 9.5f;
        int lod = Puppet.Lod(Zoom); // v17.1 멀리선 점 + 색 · 가까이선 인형
        PaintTrauma(ci, c, p, s); // v16.24 출혈 웅덩이 · 화상 열기 · 끊긴 심전도 (몸 아래에)
        PaintMedical(ci, c, p, s); // 의료 1차 수혈 주머니 · 수술 자국 붕대 · 굳은 손 · 지팡이 · 헌혈 솜

        if (c.Dead || c.Down)
        {
            // 쓰러진 사람: 옆으로 누운 몸. 죽었으면 회색에 X, 살아 있으면 붉은 십자 표시가 깜빡인다
            var body = c.Dead ? new Color("#5a5f69") : col.Darkened(0.2f);
            // 업혀 가는 사람은 업은 사람 어깨 옆에 (라벨에 가리지 않게)
            var lie = c.CarriedBy is CrewMember carrier
                ? CrewPx(carrier) + new Vector2(-carrier.Facing.Y, carrier.Facing.X).Normalized() * 13f * s
                : p;
            if (lod >= 1) PaintPuppetLying(ci, c, lie, s * 0.9f, c.Dead, false); // v17.1 누운 인형
            else
            {
                Gfx.RoundRect(ci, new Rect2(lie.X - 13f * s, lie.Y - 6f * s, 26f * s, 12f * s), Palette.Space.WithAlpha(0.8f), 6f * s);
                Gfx.RoundRect(ci, new Rect2(lie.X - 12f * s, lie.Y - 5f * s, 24f * s, 10f * s), body, 5f * s);
                ci.Circle(lie + new Vector2(-8f * s, 0f), 5.5f * s, body.Lightened(0.25f), true, -1f, true);
            }
            if (c.Suit != null) ci.Arc(lie + new Vector2(-8f * s, 0f), 6f * s, 0f, Mathf.Tau, 16, new Color("#dfe6ee"), 1.5f, true);
            if (c.Dead)
            {
                ci.DrawLine(lie + new Vector2(-11, -5) * s, lie + new Vector2(-5, 5) * s, new Color("#1a1c22"), 1.8f, true);
                ci.DrawLine(lie + new Vector2(-5, -5) * s, lie + new Vector2(-11, 5) * s, new Color("#1a1c22"), 1.8f, true);
            }
            else
            {
                float blink = 0.5f + 0.5f * Mathf.Sin(_time * 5f);
                var cp = lie + new Vector2(9f * s, -9f * s);
                ci.Circle(cp, 5.5f, Palette.Danger.WithAlpha(0.35f + 0.5f * blink), true, -1f, true);
                ci.Box(new Rect2(cp.X - 1f, cp.Y - 3.5f, 2f, 7f), Colors.White);
                ci.Box(new Rect2(cp.X - 3.5f, cp.Y - 1f, 7f, 2f), Colors.White);
            }
        }
        else if (_world.Body.Crawling(c))
        {
            if (lod >= 1) PaintPuppetCrawl(ci, c, p, s * 0.85f); // v17.1 기는 인형
            else PaintCrawler(ci, c, p, col, s); // v16.3 정비 통로 속: 엎드려 기는 몸
        }
        else if (c.Pose == Pose.Sleeping)
        {
            Gfx.RoundRect(ci, new Rect2(p.X - 10f, p.Y + 1f, 20f, 26f), col.Darkened(0.35f).WithAlpha(0.95f), 8);
            var head = p + new Vector2(0f, -5f);
            if (lod >= 1) PaintPuppetLying(ci, c, p + new Vector2(0f, 1.5f), s * 0.85f, false, true); // v17.1 베개 위 머리 · 머리카락
            else
            {
                ci.Circle(head, 8.5f * s, Palette.Space.WithAlpha(0.8f), true, -1f, true);
                ci.Circle(head, 7f * s, col, true, -1f, true);
            }
        }
        else
        {
            // v14.5 몸짓: 조용한 걸음은 작게 · 다리를 다쳤으면 절뚝 · 지치거나 앓으면 앞으로 숙인다
            bool quiet = c.Gait.Quiet(_world);
            float leg = Wounds.LegFactor(c.Vitals);
            float bob = c.IsMoving ? Mathf.Sin(_time * (c.Job?.Urgent == true ? 20f : quiet ? 8f : 14f) + c.Id) * (quiet ? 0.35f : 0.9f) : 0f;
            var sway = Vector2.Zero;
            if (c.IsMoving && leg < 0.9f)
            {
                float ph = _time * 7f + c.Id;
                bob = Mathf.Abs(Mathf.Sin(ph)) * 2.4f * (1f - leg);
                sway = new Vector2(-c.Facing.Y, c.Facing.X) * Mathf.Sin(ph) * 1.6f;
            }
            bool slump = c.Needs.Rest < 0.2f || c.Fx.Worst > 0.4f || c.Vitals.Health < 0.5f;
            var body = p + new Vector2(0f, bob) + sway + (slump ? c.Facing.ToGodot() * 1.6f + new Vector2(0f, 1.2f) : Vector2.Zero);

            ci.DrawSetTransform(p + new Vector2(0f, 8f * s), 0f, new Vector2(1f, 0.42f));
            ci.Circle(Vector2.Zero, 10f * s, new Color(0f, 0f, 0f, 0.4f), true, -1f, true);
            ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

            float rr = c.Pose == Pose.Sitting ? radius * 0.9f : radius;
            var facing = _world.React.FaceAt(c) is System.Numerics.Vector2 rf ? rf.ToGodot() : c.Facing.ToGodot(); // v17.8 쳐다보는 쪽으로 몸을 돌린다
            if (ZoomDetail.Shows(Zoom, Detail.CrewDot)) PaintCrewDot(ci, c, body, rr, Palette.Crew(c.Id)); // v17.1 · v16.24 멀리선 점 + 색
            else PaintPuppet(ci, c, body, facing, s * (c.Pose == Pose.Sitting ? 0.92f : 1f), Puppet.Of(_world, c), lod, Palette.Crew(c.Id), c.Vitals.Health < 0.5f); // v17.1 위에서 본 인형

            if (c.Pose == Pose.Working)
            {
                float a = _time * 3f + c.Id;
                float prog = c.Job?.Current?.Progress ?? -1f;
                if (prog >= 0f)
                {
                    ci.Arc(body, rr + 5f * s, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * prog, 32, col.WithAlpha(0.85f), 2.2f, true);
                    ci.Arc(body, rr + 5f * s, 0f, Mathf.Tau, 32, col.WithAlpha(0.15f), 2.2f, true);
                }
                else ci.Arc(body, rr + 5f * s, a, a + 1.1f, 12, col.WithAlpha(0.65f), 2f, true);
            }

            PaintReact(ci, c, body, facing, rr, s, ZoomDetail.Shows(Zoom, Detail.CrewDot) ? 0 : lod); // v17.8 반응 몸짓 · 손전등 · 담요 · 땀 · 입김
            PaintGestures(ci, c, body, facing, rr, s, ZoomDetail.Shows(Zoom, Detail.CrewDot) ? 0 : lod); // v17.2 손 · 시선 · 자세 · 버릇
            PaintPersonal(ci, c, body, facing, rr, s, ZoomDetail.Shows(Zoom, Detail.CrewDot) ? 0 : lod); // v18.1 편지 · v18.9 대신 서는 당직 완장
            if (c.Job?.Current is SprayToil) PaintSpray(ci, body, facing, rr);
            PaintGait(ci, c, body, facing, rr, s);

            // 손에 든 물건: 멀리서는 색 네모 (가까이선 인형 손에 각자 다른 모양)
            if (lod == 0 && c.Carrying is ItemStack held)
            {
                var side = new Vector2(-facing.Y, facing.X);
                var bp = body + side * (rr * 0.95f) + facing * (rr * 0.35f);
                Gfx.RoundRect(ci, new Rect2(bp.X - 4f * s, bp.Y - 4f * s, 8f * s, 8f * s), Palette.Item(held.Kind), 2 * s, Palette.Space.WithAlpha(0.8f), 1);
            }
        }

        if (selected)
            ci.Arc(p, radius + 5.5f + Mathf.Sin(_time * 4f), 0f, Mathf.Tau, 48, new Color(1, 1, 1, 0.9f), 1.5f, true);
        else if (hovered)
            ci.Arc(p, radius + 4.5f, 0f, Mathf.Tau, 48, new Color(1, 1, 1, 0.4f), 1.5f, true);
    }

    public CrewMember? PickCrew(Vector2 worldPx)
    {
        float reach = CrewRadius + 5f;
        return _world.Crew
            .Select(c => (c, d: CrewPx(c).DistanceTo(worldPx)))
            .Where(x => x.d < reach)
            .OrderBy(x => x.d)
            .Select(x => x.c)
            .FirstOrDefault();
    }

    /// <summary>v10.10: 클릭한 곳의 로봇 (충전대에 있으면 충전대 칸 가운데).</summary>
    public Robot? PickRobot(Vector2 worldPx) =>
        _world.Robots.Robots.Where(r => r.State != RobotState.Lost)
            .Select(r => (r, d: RobotPx(r).DistanceTo(worldPx)))
            .Where(x => x.d < 11f)
            .OrderBy(x => x.d)
            .Select(x => x.r)
            .FirstOrDefault();

    /// <summary>클릭한 곳의 설비·보관함 (침대·의자·테이블 같은 단순 가구는 제외).</summary>
    public Furniture? PickFurniture(Vector2 worldPx)
    {
        var f = _world.Ship.FurnitureAt(CellAtPx(worldPx));
        return f != null && (f.Machine != null || f.Storage != null) ? f : null;
    }
}
