using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.22 방 종류마다 바닥이 다르다 (정적 층 · 가구 아래):
/// 주컴퓨터실 — 구멍 뚫린 올림 바닥 · 벽을 따라 케이블 받침 · 찬 바람 줄 / 서버실 — 랙 줄 그림자 · 올림 바닥
/// 버섯 재배실 — 어둡고 젖은 바닥 · 물기 반짝임 · 배수구 / 조류 배양실 · 단백질 농장 — 바닥 관 · 녹색 물때
/// 냉동 창고 — 벽 밑 성에 / 항법실 — 별자리 원판 상감 / 보안실 — 감시 화면 빛 / 축열실 — 단열판 · 경고 띠
/// 교정실 — 정밀 격자 · 맞춤 표시 / 펌프실 — 바닥 수로 · 배수구 / 연료전지실 — 수소 경고 마름모 · 환기 격자.
/// </summary>
public partial class ShipView
{
    private void PaintRoomKinds(CanvasItem ci)
    {
        var ship = _world.Ship;
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.Special == null) continue;
            var cells = room.Cells.Where(ship.IsOpenFloor).ToList();
            if (cells.Count == 0) continue;
            switch (room.Kind)
            {
                case RoomType.ComputerRoom:
                case RoomType.ServerRoom:
                {
                    bool core = room.Kind == RoomType.ComputerRoom;
                    foreach (var c in cells)
                    {
                        var r = CellRect(c).Grow(-1.5f);
                        ci.Box(r, new Color("#1a222c"));
                        ci.Box(r, new Color("#3a4a5c"), false, 1f);
                        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) // 올림 바닥 구멍
                            ci.Circle(r.Position + new Vector2(r.Size.X * (i + 1) / 4f, r.Size.Y * (j + 1) / 4f), 1.1f, new Color("#0b0f14"), true, -1f, true);
                        if (core && (c.X + c.Y) % 2 == 0) ci.DrawLine(r.Position + new Vector2(2, r.Size.Y - 2), r.End - new Vector2(2, 2), new Color("#5ec8e6").WithAlpha(0.18f), 1.5f); // 찬 바람 줄
                    }
                    foreach (var c in room.Cells) // 벽을 따라 케이블 받침
                        foreach (var d in Cell.Dirs4)
                        {
                            if (ship.Grid.Kind(c + d) != TileKind.Wall) continue;
                            var r = CellRect(c);
                            var a = r.GetCenter() + new Vector2(d.X, d.Y) * (r.Size.X * 0.42f);
                            var side = new Vector2(-d.Y, d.X) * (r.Size.X * 0.5f);
                            ci.DrawLine(a - side, a + side, new Color("#2c3640"), 4f);
                            ci.DrawLine(a - side, a + side, core ? new Color("#e0a040").WithAlpha(0.7f) : new Color("#7fe0c0").WithAlpha(0.55f), 1f);
                        }
                    break;
                }
                case RoomType.MushroomFarm:
                    foreach (var c in cells)
                    {
                        var r = CellRect(c);
                        ci.Box(r, new Color(0.05f, 0.04f, 0.03f, 0.45f)); // 어둡게
                        if (H3(c.X, c.Y, 41) < 0.35f) ci.Circle(r.GetCenter() + new Vector2(H3(c.X, c.Y, 42) * 10f - 5f, H3(c.X, c.Y, 43) * 10f - 5f), 3f + 4f * H3(c.X, c.Y, 44), new Color("#9fb8c8").WithAlpha(0.12f), true, -1f, true); // 물기
                        if (H3(c.X, c.Y, 45) < 0.05f) { ci.Circle(r.GetCenter(), 4f, new Color("#0b0b0b"), true, -1f, true); ci.Arc(r.GetCenter(), 4f, 0f, Mathf.Tau, 12, new Color("#5a5a50"), 1f, true); } // 배수구
                    }
                    break;
                case RoomType.AlgaeLab:
                case RoomType.ProteinFarm:
                {
                    var tint = room.Kind == RoomType.AlgaeLab ? new Color("#3faa55") : new Color("#c98a7a");
                    foreach (var c in cells)
                    {
                        var r = CellRect(c);
                        if (H3(c.X, c.Y, 51) < 0.25f) ci.Circle(r.GetCenter() + new Vector2(H3(c.X, c.Y, 52) * 12f - 6f, 0f), 2f + 3f * H3(c.X, c.Y, 53), tint.WithAlpha(0.12f), true, -1f, true); // 물때
                    }
                    var first = cells.OrderBy(c => c.Y).ThenBy(c => c.X).First();
                    var last = cells.OrderBy(c => -c.Y).ThenBy(c => -c.X).First();
                    ci.DrawLine(CellRect(first).GetCenter() + new Vector2(0, 10), CellRect(last).GetCenter() + new Vector2(0, 10), tint.Darkened(0.4f).WithAlpha(0.6f), 3f); // 바닥 관
                    break;
                }
                case RoomType.Freezer:
                    foreach (var c in room.Cells)
                        foreach (var d in Cell.Dirs4)
                        {
                            if (ship.Grid.Kind(c + d) != TileKind.Wall) continue;
                            var r = CellRect(c);
                            var edge = r.GetCenter() + new Vector2(d.X, d.Y) * (r.Size.X * 0.5f);
                            var side = new Vector2(-d.Y, d.X);
                            for (int k = -2; k <= 2; k++) // 성에 송이
                            {
                                var p = edge + side * (k * 6f + H3(c.X, c.Y, k + 60) * 3f);
                                ci.DrawLine(p, p - new Vector2(d.X, d.Y) * (3f + 4f * H3(c.X + k, c.Y, 61)), new Color("#e8f6ff").WithAlpha(0.5f), 1.2f);
                            }
                        }
                    break;
                case RoomType.Navigation:
                {
                    var ctr = cells.Aggregate(Vector2.Zero, (s, c) => s + CellRect(c).GetCenter()) / cells.Count;
                    ci.Arc(ctr, 22f, 0f, Mathf.Tau, 40, new Color("#7dd9b8").WithAlpha(0.35f), 1.5f, true);
                    ci.Arc(ctr, 14f, 0f, Mathf.Tau, 32, new Color("#7dd9b8").WithAlpha(0.2f), 1f, true);
                    for (int k = 0; k < 9; k++) // 별자리
                    {
                        var p = ctr + Vector2.FromAngle(k * 0.7f + 0.3f) * (6f + 14f * H3(k, 3, 70));
                        ci.Circle(p, 1.1f, new Color("#e8fff6").WithAlpha(0.6f), true, -1f, true);
                        if (k > 0) ci.DrawLine(p, ctr + Vector2.FromAngle((k - 1) * 0.7f + 0.3f) * (6f + 14f * H3(k - 1, 3, 70)), new Color("#7dd9b8").WithAlpha(0.25f), 0.8f);
                    }
                    break;
                }
                case RoomType.Security:
                    foreach (var c in cells.Where(c => H3(c.X, c.Y, 80) < 0.3f))
                        ci.Box(CellRect(c).Grow(-6f), new Color("#8ad0c0").WithAlpha(0.06f)); // 감시 화면이 바닥에 비친 빛
                    break;
                case RoomType.HeatStorage:
                    foreach (var c in cells)
                    {
                        var r = CellRect(c).Grow(-2f);
                        ci.Box(r, new Color("#3a2a1a").WithAlpha(0.5f)); // 단열판
                        if ((c.X + c.Y) % 3 == 0) for (int k = 0; k < 3; k++) ci.DrawLine(r.Position + new Vector2(k * 8f, r.Size.Y), r.Position + new Vector2(k * 8f + 6f, r.Size.Y - 6f), new Color("#f0b070").WithAlpha(0.35f), 2f); // 경고 띠
                    }
                    break;
                case RoomType.Calibration:
                    foreach (var c in cells)
                    {
                        var r = CellRect(c);
                        ci.Box(r.Grow(-0.5f), new Color("#cfae6d").WithAlpha(0.12f), false, 0.6f); // 정밀 격자
                        ci.DrawLine(r.GetCenter() - new Vector2(3, 0), r.GetCenter() + new Vector2(3, 0), new Color("#cfae6d").WithAlpha(0.35f), 0.8f); // 맞춤 표시
                        ci.DrawLine(r.GetCenter() - new Vector2(0, 3), r.GetCenter() + new Vector2(0, 3), new Color("#cfae6d").WithAlpha(0.35f), 0.8f);
                    }
                    break;
                case RoomType.PumpRoom:
                    foreach (var c in cells)
                    {
                        var r = CellRect(c);
                        ci.DrawLine(new Vector2(r.Position.X, r.GetCenter().Y + 6f), new Vector2(r.End.X, r.GetCenter().Y + 6f), new Color("#0d1a22"), 3f); // 바닥 수로
                        if (H3(c.X, c.Y, 90) < 0.08f) ci.Circle(r.GetCenter(), 3.5f, new Color("#0b0b0b"), true, -1f, true);
                    }
                    break;
                case RoomType.FuelCell:
                {
                    var c0 = cells[cells.Count / 2];
                    var p = CellRect(c0).GetCenter();
                    var pts = new[] { p + new Vector2(0, -8), p + new Vector2(8, 0), p + new Vector2(0, 8), p + new Vector2(-8, 0) };
                    ci.Poly(pts, new Color("#e3c75a").WithAlpha(0.25f));
                    ci.Polyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, new Color("#e3c75a").WithAlpha(0.7f), 1.2f, true); // 수소 경고 마름모
                    foreach (var c in cells.Where(c => H3(c.X, c.Y, 95) < 0.12f))
                    {
                        var r = CellRect(c).Grow(-8f);
                        for (int k = 0; k < 4; k++) ci.DrawLine(new Vector2(r.Position.X, r.Position.Y + k * r.Size.Y / 3f), new Vector2(r.End.X, r.Position.Y + k * r.Size.Y / 3f), new Color("#202428"), 1.4f); // 환기 격자
                    }
                    break;
                }
            }
        }
    }
}
