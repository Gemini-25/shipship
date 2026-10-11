using System;
using System.Linq;
using Godot;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipSim.View;

// 도트 벽 소품 · 바닥 꾸밈: 구워 두는 층에 한 번 (북쪽 벽은 앞면에 걸고, 서/동/남 벽은 모서리 띠, 서 있는 것은 그 앞 바닥에 세운다).
public partial class ShipView
{
    /// <summary>캐시된 도트 그림 한 장을 그 자리에 (구울 때 쓰므로 예산과 상관없이 만든다).</summary>
    private void DotBlit(CanvasItem ci, ulong key, Func<Img> make, Vector2 at)
    {
        var e = Dots.Get(key, make, force: true);
        if (e is not { W: > 0 }) return;
        ci.DrawTextureRect(Zoom >= 0.999f ? e.Crisp : e.Tex, new Rect2(at, e.W, e.H), false);
    }

    private static int RampOfRoom(Room room)
    {
        var c = Palette.Room(room.Kind);
        return Ramps.Derive((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255));
    }

    /// <summary>벽 소품 하나 (도트가 켜져 있으면 도트, 아니면 예전 그림).</summary>
    private void PropAt(CanvasItem ci, Prop p, Cell wall, Vector2 inward, Room room)
    {
        if (!DotCrew) { DrawProp(ci, p, wall, inward, room); return; }
        var dp = (DotProp)(int)p;
        var side = inward.Y > 0.5f ? PropSide.Face : Mathf.Abs(inward.X) > 0.5f ? PropSide.Side : PropSide.Back;
        int accent = p == Prop.Screen ? (room.Type is RoomType.Bridge or RoomType.Comms ? Ramps.Led : Ramps.Cyan) : RampOfRoom(room);
        int liquid = room.Type == RoomType.Hydroponics ? Ramps.Leaf : room.Type == RoomType.Cooling ? Ramps.Cyan : Ramps.Glass;
        int seed = (int)(DotHash.U(wall.X, wall.Y, 77) & 15);
        string code = p == Prop.Sign ? $"{RoomCode(room.Kind)}-{room.Id + 1}" : "";
        var key = DotTex.Key(8, (int)dp, (int)side, accent, liquid, seed, code.GetHashCode());
        var floor = wall + new Cell((int)MathF.Round(inward.X), (int)MathF.Round(inward.Y));
        float fx = floor.X * T, fy = floor.Y * T;
        Vector2 at;
        if (side == PropSide.Face) at = new Vector2(fx, fy);
        else if (DotProps.Standing(dp))
        {
            // 서 있는 것: 벽 앞 바닥에 (발이 칸 안쪽)
            float cx = side == PropSide.Back ? fx + T * 0.5f : inward.X > 0 ? fx + 10f : fx + T - 10f;
            float foot = side == PropSide.Back ? fy + T - 2f : fy + T - 6f;
            at = new Vector2(cx - DotProps.FaceW / 2f, foot - DotProps.FaceH + 1);
        }
        else if (side == PropSide.Side) at = new Vector2(inward.X > 0 ? fx : fx + T - DotProps.Strip, fy + 4f);
        else at = new Vector2(fx + 4f, fy + T - DotProps.Strip);
        DotBlit(ci, key, () => DotProps.Wall(dp, side, accent, liquid, seed, code), at);
    }

    /// <summary>바닥 꾸밈 (도트): 환기 격자 · 배수구 · 식탁 밑 깔개 · 침대 발치 매트 · 노심 · 엔진 둘레 경고 띠. 그렸으면 true.</summary>
    private bool PaintDotFloorDecor(CanvasItem ci)
    {
        if (!DotCrew) return false;
        var ship = _world.Ship;
        var g = ship.Grid;
        foreach (var room in ship.Rooms)
        {
            if (room.Detached) continue;
            if (room.Type != RoomType.Corridor && g.Kind(room.DamperSpot) == TileKind.Floor)
                DotBlit(ci, DotTex.Key(9, 0), DotProps.Grate, CellRect(room.DamperSpot).Position + new Vector2(7, 7));
            switch (room.Type)
            {
                case RoomType.Hydroponics:
                case RoomType.Galley:
                    foreach (var c in room.Cells.Where(c => ship.IsOpenFloor(c) && H3(c.X, c.Y, 21) < 0.06f))
                        DotBlit(ci, DotTex.Key(9, 1), DotProps.Drain, CellRect(c).Position + new Vector2(10, 10));
                    break;
                case RoomType.Lounge:
                case RoomType.Mess:
                    foreach (var t in room.Furniture.Where(f => f.Type == FurnitureType.Table))
                    {
                        var r = FurnitureRect(t).Grow(T * 0.85f);
                        int w = (int)r.Size.X, h = (int)r.Size.Y, ramp = room.Type == RoomType.Lounge ? Ramps.ClothPurple : Ramps.ClothBrown;
                        DotBlit(ci, DotTex.Key(9, 2, w, h, ramp, t.Id), () => DotProps.Rug(w, h, ramp, t.Id), r.Position.Round());
                    }
                    break;
                case RoomType.Quarters:
                    foreach (var b in room.Furniture.Where(f => f.Type == FurnitureType.Bed))
                    {
                        var r = FurnitureRect(b);
                        int ramp = b.Owner != null ? DotBodyArgs(b).Blanket : Ramps.ClothNavy;
                        var at = new Vector2(r.Position.X - 2f, r.Position.Y + (b.Cells[0].Y == b.MinY ? r.Size.Y : -10f));
                        int w = (int)r.Size.X + 4;
                        DotBlit(ci, DotTex.Key(9, 3, w, ramp, b.Id), () => DotProps.Rug(w, 10, ramp, b.Id), at);
                    }
                    break;
                case RoomType.Corridor:
                    foreach (var c in room.Cells.Where(c => c.X % 12 == 6 && g.Kind(c + new Cell(0, -1)) == TileKind.Wall && ship.IsOpenFloor(c + new Cell(0, 1))))
                    {
                        var q = CellRect(c).GetCenter() + new Vector2(0, T * 0.5f);
                        Gfx.TextCentered(ci, Fonts.Bold, q, $"{(char)('A' + (c.Y / 8) % 26)}-{c.X / 12:00}", 9, new Color(1, 1, 1, 0.12f));
                    }
                    break;
            }
        }
        void Frame(Furniture f, int t)
        {
            var r = FurnitureRect(f).Grow(t * 1.5f);
            int w = (int)r.Size.X, h = (int)r.Size.Y;
            DotBlit(ci, DotTex.Key(9, 4, w, h, t), () => DotProps.HazardFrame(w, h, t), r.Position.Round());
        }
        foreach (var f in ship.FurnitureOf(FurnitureType.ReactorCore)) if (!f.Room.Detached) Frame(f, 5);
        foreach (var f in ship.FurnitureOf(FurnitureType.EngineCore)) if (!f.Room.Detached) Frame(f, 4);
        return true;
    }
}
