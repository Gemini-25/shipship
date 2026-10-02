using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5b 기술마다 배 모습: 익힌 기술의 Visual 열쇠(TechWeb) 111개가 모두 배 어딘가에 그려진다 — 자리 고르기와 나누기는 여기,
/// 그림은 자리마다 (TechVisualsFix.cs 설비에 붙는 것 · TechVisualsRoom.cs 방 벽 · 바닥 · 통로 · TechVisualsHull.cs 외판 · 바깥 · 배관 · 망).
/// 색은 기술 아이콘(TechIcons)과 같은 분야 색. 갈림길에서 고른 쪽은 배 전체에 짙게 퍼지고(모든 해당 방 · 촘촘히) 외판에 배 이름 문장이 붙는다.
/// 정적인 몸은 정적 층에 한 번, 움직이는 것(Live)은 동적 층, 빛은 더하기 층. 그리기는 Core 상태를 바꾸지 않는다.
/// </summary>
public partial class ShipView
{
    /// <summary>기술 모습 하나가 놓인 자리.</summary>
    internal struct VisPlace
    {
        public VisualRow Row;
        public Color Col;
        public bool Id;
        public int K;
        public Furniture? F;
        public Face Face;
        public HullSeg Hull;
        public Cell Cell;
        public Room? Room;
        public PipeSegment? Pipe;
        public NetLink? Link;
        public Vector2[]? Pts;
        public Rect2 Bound;
        public string Key => Row.Key;
    }

    private readonly List<VisPlace> _visPlaced = new();

    private static Rect2 FaceRect(in Face s, float b0, float b1)
    {
        var a = s.L(-16f, b0);
        var b = s.L(16f, b1);
        return new Rect2(new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y)), new Vector2(Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y)));
    }

    private Rect2 RoomRect(Room r) => new(r.MinX * T, r.MinY * T, (r.MaxX - r.MinX + 1) * T, (r.MaxY - r.MinY + 1) * T);

    /// <summary>정적 층: 익힌 기술 모습을 자리마다 놓고 그린다 (자리는 동적 · 빛 층이 다시 쓴다).</summary>
    private void PaintVisualsStatic(CanvasItem ci)
    {
        _visRects.Clear();
        _visPlaced.Clear();
        if (_techActive == null || _techActive.Count == 0) return;
        var w = _world;
        var ship = w.Ship;
        var used = new HashSet<Cell>();
        foreach (var row in _techActive)
        {
            var col = TechLook.Field(row.Key);
            bool id = TechLookTable.Identity(w, row.Key);
            int every = id ? Math.Max(1, row.Every / 2) : Math.Max(1, row.Every);
            int k = 0;
            switch (row.Anchor)
            {
                case VAnchor.Fix:
                    foreach (var f in ship.FurnitureOf(row.Fix!.Value))
                        Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, F = f, Room = f.Room, Bound = FurnitureRect(f) });
                    break;
                case VAnchor.Machines:
                    foreach (var f in ship.Furniture)
                    {
                        if (f.Stowed || f.Room.Detached || f.Machine == null) continue;
                        if (k++ % every == 0) Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k, F = f, Room = f.Room, Bound = FurnitureRect(f).Grow(4f) });
                    }
                    break;
                case VAnchor.Room when row.Rooms.Length == 0:
                    foreach (var r in ship.LiveRooms)
                        if (r.Type != RoomType.Corridor && r.Cells.Count > 0)
                            Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Room = r, Bound = RoomRect(r) });
                    break;
                case VAnchor.Room:
                    foreach (var r in RoomsFor(row, id))
                        if (TakeSlot(r, used) is Face s)
                            Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Face = s, Room = r, Cell = s.Floor, Bound = FaceRect(s, -9f, 16f) });
                    break;
                case VAnchor.Wall:
                {
                    int i = 0;
                    foreach (var f in _faces)
                    {
                        if (f.Room.Type == RoomType.Corridor && row.Rooms.Length == 0) continue;
                        if (row.Rooms.Length > 0 && Array.IndexOf(row.Rooms, f.Room.Type) < 0) continue;
                        if (i++ % every != 0) continue;
                        Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Face = f, Room = f.Room, Cell = f.Floor, Bound = FaceRect(f, -9f, 4f) });
                    }
                    break;
                }
                case VAnchor.Corridor:
                {
                    int i = 0;
                    foreach (var f in _faces)
                    {
                        if (f.Room.Type != RoomType.Corridor || i++ % every != 0) continue;
                        Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Face = f, Room = f.Room, Cell = f.Floor, Bound = FaceRect(f, -6f, 30f) });
                    }
                    break;
                }
                case VAnchor.Hull:
                    foreach (var h in _hullSegs)
                        if (h.K % every == 0) Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Hull = h, Cell = h.Cell, Bound = CellRect(h.Cell).Grow(16f) });
                    break;
                case VAnchor.Exterior:
                    Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = 0, Bound = Bounds.Grow(T * 3f) });
                    break;
                case VAnchor.Floor:
                    foreach (var r in ship.LiveRooms)
                    {
                        if (r.Type == RoomType.Corridor && row.Rooms.Length == 0) continue;
                        if (row.Rooms.Length > 0 && Array.IndexOf(row.Rooms, r.Type) < 0) continue;
                        foreach (var c in r.Cells)
                            if (ship.IsOpenFloor(c) && (c.X * 7 + c.Y * 3) % every == 0)
                                Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Cell = c, Room = r, Bound = CellRect(c) });
                    }
                    break;
                case VAnchor.Pipes:
                    foreach (var seg in w.Piping.Segments)
                    {
                        if (seg.Path.Count < 2 || (row.Key is "pipe.coolantdope" or "pipe.heatpipe" or "pipe.liquidmetal") && !seg.IsCoolant) continue;
                        var off = PipeOffset(seg);
                        var pts = seg.Path.Select(c => CellRect(c).GetCenter() + off).ToArray();
                        Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Pipe = seg, Pts = pts, Bound = PtsRect(pts).Grow(8f) });
                    }
                    break;
                case VAnchor.Net:
                {
                    var kind = row.Key == "wall.fiber" ? NetKind.Data : NetKind.Power;
                    foreach (var l in w.Net.Links)
                    {
                        if (l.Kind != kind || l.Room.Detached || l.Cells.Count < 2) continue;
                        float lane = (kind == NetKind.Power ? -0.3f : -0.15f) * T;
                        var off = new Vector2(lane, lane * 0.6f);
                        var pts = l.Cells.Select(c => CellRect(c).GetCenter() + off).ToArray();
                        Place(ci, new VisPlace { Row = row, Col = col, Id = id, K = k++, Link = l, Pts = pts, Room = l.Room, Bound = PtsRect(pts).Grow(6f) });
                    }
                    break;
                }
            }
        }
        PaintIdentityCrest(ci); // TechVisualsHull.cs — 갈림길에서 고른 배 이름 문장
    }

    /// <summary>순간 연출을 그릴 자리: 대개 놓인 자리 그대로, 긴 배관 · 망은 가운데 한 칸, 바깥 장치는 그 장치 둘레.</summary>
    private Rect2 MomentRect(in VisPlace p)
    {
        if (p.Pts is { Length: > 0 } pts) return new Rect2(pts[pts.Length / 2] - new Vector2(T * 0.5f, T * 0.5f), new Vector2(T, T));
        if (p.Row.Anchor != VAnchor.Exterior || _hullSegs.Count == 0) return p.Bound;
        var (l, r, t, b) = HullBox();
        float wdt = r - l, mid = (l + r) * 0.5f;
        Vector2 o = p.Key switch
        {
            "exterior.drones" => HullAt(l + wdt * 0.35f, -1),
            "exterior.rcs" => HullAt(l + wdt * 0.12f, -1),
            "exterior.pdlaser" => HullAt(mid + wdt * 0.1f, -1),
            "engine.magnozzle" => _world.Ship.FurnitureOf(FurnitureType.EngineCore).FirstOrDefault() is Furniture f ? new Vector2((f.MinX - 1) * T - NozzleLen * 0.5f, (f.MinY + f.MaxY + 1) * 0.5f * T) : new Vector2(l, (t + b) * 0.5f),
            "hull.centrifuge" => new Vector2(mid, t - 18f),
            "exterior.ionring" => _noseTip,
            "exterior.radiator" => HullAt(l + wdt * 0.42f, -1) + new Vector2(-4f, -27f),
            "engine.warp" => new Vector2(mid, t - 50f),
            "exterior.gravlens" => _noseTip + new Vector2(54f, 0f),
            "exterior.swarm" => HullAt(mid, 1),
            "exterior.salvagescan" => HullAt(r - wdt * 0.2f, 1) + new Vector2(0f, 10f),
            "sensor.gravwave" => HullAt(r - wdt * 0.3f, -1) + new Vector2(0f, -20f),
            _ => HullAt(mid, -1),
        };
        return new Rect2(o - new Vector2(T * 0.75f, T * 0.75f), new Vector2(T * 1.5f, T * 1.5f));
    }

    private static Rect2 PtsRect(Vector2[] pts)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var p in pts) { x0 = Mathf.Min(x0, p.X); y0 = Mathf.Min(y0, p.Y); x1 = Mathf.Max(x1, p.X); y1 = Mathf.Max(y1, p.Y); }
        return new Rect2(x0, y0, x1 - x0, y1 - y0);
    }

    private void Place(CanvasItem ci, VisPlace p)
    {
        _visPlaced.Add(p);
        if (!_visRects.TryGetValue(p.Key, out var l)) _visRects[p.Key] = l = new List<Rect2>();
        l.Add(MomentRect(p)); // 익힌 순간 연출 자리 (긴 배관 · 바깥은 장치 한 곳만)
        switch (p.Row.Anchor)
        {
            case VAnchor.Fix: VisFixStatic(ci, p); break;
            case VAnchor.Machines: VisMachineStatic(ci, p); break;
            case VAnchor.Room when p.Row.Rooms.Length == 0: VisRoomAllStatic(ci, p); break;
            case VAnchor.Room: VisSlotStatic(ci, p); VisMakerPlate(ci, p); break;
            case VAnchor.Wall: VisWallStatic(ci, p); break;
            case VAnchor.Corridor: VisCorridorStatic(ci, p); break;
            case VAnchor.Hull: VisHullStatic(ci, p); break;
            case VAnchor.Exterior: VisExteriorStatic(ci, p); break;
            case VAnchor.Floor: VisFloorStatic(ci, p); break;
            case VAnchor.Pipes: VisPipeStatic(ci, p); break;
            case VAnchor.Net: VisNetStatic(ci, p); break;
        }
    }

    /// <summary>방 안건: 표에 적힌 방 종류 차례로 — 고른 갈림길이면 모든 해당 방, 아니면 첫 방. 없으면 벽 자리가 가장 많은 방.</summary>
    private List<Room> RoomsFor(VisualRow row, bool identity)
    {
        var list = new List<Room>();
        foreach (var t in row.Rooms)
            foreach (var r in _world.Ship.RoomsOf(t))
                if (!list.Contains(r) && _roomSlots.ContainsKey(r.Id)) list.Add(r);
        if (list.Count == 0)
        {
            Room? best = null;
            int bestN = -1;
            foreach (var r in _world.Ship.LiveRooms)
            {
                if (r.Type == RoomType.Corridor || !_roomSlots.TryGetValue(r.Id, out var sl)) continue;
                if (sl.Count > bestN) { best = r; bestN = sl.Count; }
            }
            if (best != null) list.Add(best);
        }
        return identity ? list : list.Take(1).ToList();
    }

    /// <summary>방 벽 자리 하나 (이미 쓴 자리 · 그 옆은 비킨다).</summary>
    private Face? TakeSlot(Room r, HashSet<Cell> used)
    {
        if (!_roomSlots.TryGetValue(r.Id, out var slots) || slots.Count == 0) return null;
        foreach (var s in slots)
        {
            bool near = false;
            foreach (var u in used) if (Math.Abs(u.X - s.Floor.X) <= 1 && Math.Abs(u.Y - s.Floor.Y) <= 1) { near = true; break; }
            if (near) continue;
            used.Add(s.Floor);
            return s;
        }
        foreach (var s in slots)
            if (!used.Contains(s.Floor)) { used.Add(s.Floor); return s; }
        return null;
    }

    /// <summary>방 벽 장치 옆 작은 명판: 기술 아이콘 (가까이서 무슨 기술인지 안다).</summary>
    private static void VisMakerPlate(CanvasItem ci, in VisPlace p)
    {
        if (TechLookTable.TechOf(p.Key) is not EraTech t) return;
        var c = p.Face.L(-12.5f, -4f);
        TechIcons.Draw(ci, t, c, 3.2f, 1, 0.9f);
    }

    /// <summary>동적 층: 움직이는 기술 모습 (화면 안만).</summary>
    private void PaintVisualsLive(CanvasItem ci)
    {
        if (_visPlaced.Count == 0) return;
        foreach (var p in _visPlaced)
        {
            if (!p.Row.Live || !_fixView.Intersects(p.Bound)) continue;
            switch (p.Row.Anchor)
            {
                case VAnchor.Fix: VisFixLive(ci, p); break;
                case VAnchor.Machines: VisMachineLive(ci, p); break;
                case VAnchor.Room when p.Row.Rooms.Length == 0: VisRoomAllLive(ci, p); break;
                case VAnchor.Room: VisSlotLive(ci, p); break;
                case VAnchor.Wall: VisWallLive(ci, p); break;
                case VAnchor.Corridor: VisCorridorLive(ci, p); break;
                case VAnchor.Hull: VisHullLive(ci, p); break;
                case VAnchor.Exterior: VisExteriorLive(ci, p); break;
                case VAnchor.Pipes: VisPipeLive(ci, p); break;
                case VAnchor.Net: VisNetLive(ci, p); break;
            }
        }
    }

    /// <summary>더하기 층: 기술 모습의 빛 (생물 발광 · 노심 · 역장 · 도가니 …). 전기가 없는 방은 빛이 죽는다.</summary>
    private void PaintVisualsGlow(CanvasItem ci)
    {
        foreach (var p in _visPlaced)
        {
            if (p.Room is Room r && (!r.Powered || r.LightsOut)) continue;
            VisGlow(ci, p);
        }
    }

    /// <summary>설비가 돌고 있나 (기술 장치의 빛 · 움직임이 따른다).</summary>
    private static bool Running(Furniture? f) => f?.Machine is not Machine m || FixtureArt.StateOf(f, m) is FixtureArt.State.Running or FixtureArt.State.Passive;
}
