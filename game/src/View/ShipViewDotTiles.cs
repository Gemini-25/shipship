using System;
using Godot;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipSim.View;

// 도트 바닥 · 벽: 바탕 층(LookBase)을 도트 타일로 — 바닥 재질 6 × 모양 4 · 벽 3종류 × 이웃 16 · 남쪽 앞면.
// 바탕 층은 구워 두는 층이라 다시 구울 때만 그린다 (60프레임에 영향 없음).
public partial class ShipView
{
    private static ImageTexture? _floorAtlas, _wallAtlas;
    private static CanvasTexture? _floorCrisp, _wallCrisp;

    private static void DotTilesLoad()
    {
        if (_floorAtlas != null) return;
        static (ImageTexture, CanvasTexture) Make(Img img)
        {
            var gi = Image.CreateFromData(img.W, img.H, false, Image.Format.Rgba8, img.Px);
            gi.GenerateMipmaps();
            var tex = ImageTexture.CreateFromImage(gi);
            return (tex, new CanvasTexture { DiffuseTexture = tex, TextureFilter = TextureFilterEnum.Nearest });
        }
        (_floorAtlas, _floorCrisp) = Make(DotTiles.FloorAtlas());
        (_wallAtlas, _wallCrisp) = Make(DotTiles.WallAtlas());
    }

    private static int CellVariant(Cell c) => (int)(DotHash.U(c.X, c.Y, 41) % DotTiles.Variants);

    /// <summary>도트 문 (열림 7단계 · 종류 · 상태등). 그렸으면 true.</summary>
    private bool PaintDotDoor(CanvasItem ci, Door d, float open)
    {
        bool h = d.ConnectsVertically;
        int step = Math.Clamp((int)MathF.Round(open * 6f), 0, 6);
        int kind = d.IsExternal ? 1 : d.Bulkhead ? 2 : 0;
        int light = !d.Powered ? 2 : d.Locked ? 3 : open > 0.05f ? 1 : 0;
        var e = Dots.Get(DotTex.Key(4, h ? 1 : 0, step, kind, light), () => DotTiles.Door(h, step, kind, light), force: true);
        if (e == null) return false;
        var r = CellRect(d.Cell);
        ci.DrawTextureRect(Zoom >= 0.999f ? e.Crisp : e.Tex, new Rect2(r.Position, e.W, e.H), false);
        return true;
    }

    /// <summary>바닥 · 벽 · 문 칸을 도트 타일로 (구울 때의 확대가 1 이상이면 또렷하게, 아래면 부드럽게).</summary>
    private void PaintDotCells(CanvasItem ci)
    {
        DotTilesLoad();
        var ship = _world.Ship;
        var g = ship.Grid;
        var body = _world.Body;
        bool crisp = Zoom >= 0.999f;
        Texture2D floors = crisp ? _floorCrisp! : _floorAtlas!, walls = crisp ? _wallCrisp! : _wallAtlas!;
        const int T = DotTiles.T;
        bool Solid(Cell c) => g.Kind(c) is TileKind.Wall or TileKind.Door;
        DotWall WallKind(Cell c)
        {
            var wb = body.WallAt(c);
            return wb == null ? DotWall.Panel : wb.Hull ? DotWall.Hull : wb.Thin ? DotWall.Partition : DotWall.Panel;
        }
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            var r = CellRect(c);
            switch (g.Kind(c))
            {
                case TileKind.Wall:
                {
                    int mask = (Solid(c + new Cell(0, -1)) ? 1 : 0) | (Solid(c + new Cell(1, 0)) ? 2 : 0) | (Solid(c + new Cell(0, 1)) ? 4 : 0) | (Solid(c + new Cell(-1, 0)) ? 8 : 0);
                    ci.DrawTextureRectRegion(walls, r, new Rect2(mask * T, (int)WallKind(c) * T, T, T));
                    break;
                }
                case TileKind.Floor:
                {
                    var room = ship.RoomAt(c);
                    if (room == null) break;
                    var fl = FloorLookAt(c, room);
                    var tint = Colors.White.Lerp(Palette.Room(room.Kind), 0.08f);
                    ci.DrawTextureRectRegion(floors, r, new Rect2(CellVariant(c) * T, (int)fl * T, T, T), tint);
                    break;
                }
                case TileKind.Door:
                {
                    var door = ship.DoorAt(c);
                    var room = door?.RoomA ?? door?.RoomB;
                    var fl = room != null && LookSpec.Heavy(room.Kind) ? LookSpec.FloorLook.Engine : LookSpec.FloorLook.Metal;
                    ci.DrawTextureRectRegion(floors, r, new Rect2(CellVariant(c) * T, (int)fl * T, T, T), new Color(0.85f, 0.87f, 0.9f));
                    break;
                }
            }
        }
        // 벽 남쪽 앞면: 아래 칸이 바닥이면 그 칸 위쪽에 (양 끝이 트였으면 마감)
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) != TileKind.Wall) continue;
            var below = c + new Cell(0, 1);
            if (g.Kind(below) != TileKind.Floor) continue;
            var west = c + new Cell(-1, 0); var east = c + new Cell(1, 0);
            bool openW = !Solid(west) || g.Kind(west + new Cell(0, 1)) != TileKind.Floor;
            bool openE = !Solid(east) || g.Kind(east + new Cell(0, 1)) != TileKind.Floor;
            var (fx, fy) = DotTiles.FaceCell(WallKind(c), openW, openE, false);
            ci.DrawTextureRectRegion(walls, new Rect2(below.X * T, below.Y * T, T, DotTiles.Face), new Rect2(fx, fy, T, DotTiles.Face));
        }
    }
}
