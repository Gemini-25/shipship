using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.6 환경 보기: 방마다 소음(노란 물결)·진동(주황 지그재그)·냄새(초록 아지랑이)·방사선(보라 빛)이 얼마나 닿는지,
// 그리고 큰 배의 구획(격벽 문으로 나뉜 덩어리)을 색 테두리로.
public partial class ShipView
{
    private static readonly Color NoiseCol = new("#f5d547"), VibCol = new("#f0883e"), SmellCol = new("#8fd65a"), RadCol = new("#b58cff");
    private static readonly Color[] CompCols = { new("#5ec8e6"), new("#f0a6f7"), new("#ffd166"), new("#86efac") };

    private void PaintAmbienceOverlay(CanvasItem ci)
    {
        var w = _world;
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Detached) continue;
            var box = RoomBox(room);
            var c = box.GetCenter();
            // 바탕: 가장 센 것의 색으로 방을 물들인다 (한눈에 — 어디가 시끄럽고 어디가 조용한지)
            float top = Mathf.Max(Mathf.Max(room.Noise, room.Vibration), Mathf.Max(room.Smell, room.Radiation));
            if (top > 0.06f)
            {
                var tint = room.Radiation >= top ? RadCol : room.Smell >= top ? SmellCol : room.Vibration >= top ? VibCol : NoiseCol;
                FillRoom(ci, room, tint.WithAlpha(0.06f + 0.4f * top * (room.Radiation >= top ? 0.8f + 0.2f * Mathf.Sin(_time * 3f + room.Id) : 1f)));
            }
            else if ((RoomCatalog.Tags(room.Kind) & RoomTag.Sleep) != 0) FillRoom(ci, room, Palette.Good.WithAlpha(0.08f));
            // 소음: 가운데에서 퍼지는 물결
            if (room.Noise > 0.06f)
            {
                int rings = 1 + (int)(room.Noise * 4f);
                float maxR = Mathf.Min(box.Size.X, box.Size.Y) * 0.45f;
                for (int i = 0; i < rings; i++)
                {
                    float t = (_time * 0.6f + i / (float)rings) % 1f;
                    ci.Arc(c, 6f + maxR * t, 0, Mathf.Tau, 32, NoiseCol.WithAlpha(MathF.Min(1f, room.Noise * 1.6f) * (1f - t)), 3f, true);
                }
            }
            // 진동: 바닥을 가로지르는 떨리는 선
            if (room.Vibration > 0.06f)
            {
                float y = box.End.Y - 6f;
                var pts = new Vector2[16];
                for (int i = 0; i < pts.Length; i++)
                {
                    float x = box.Position.X + 4f + (box.Size.X - 8f) * i / (pts.Length - 1);
                    pts[i] = new Vector2(x, y + ((i & 1) == 0 ? -1f : 1f) * 8f * room.Vibration * (0.7f + 0.3f * Mathf.Sin(_time * 30f + i)));
                }
                ci.Polyline(pts, VibCol.WithAlpha(0.5f + 0.5f * room.Vibration), 3f, true);
            }
            // 냄새: 위로 피어오르는 아지랑이
            if (room.Smell > 0.06f)
            {
                int wisps = 1 + (int)(room.Smell * 5f);
                for (int i = 0; i < wisps; i++)
                {
                    float t = (_time * 0.25f + i * 0.37f) % 1f;
                    float x = box.Position.X + box.Size.X * (0.2f + 0.6f * ((i * 0.618f) % 1f));
                    float y = box.End.Y - 8f - (box.Size.Y - 16f) * t;
                    ci.Arc(new Vector2(x + 5f * Mathf.Sin(_time * 2f + i), y), 6f + 4f * t, 0.3f, Mathf.Pi - 0.3f, 10, SmellCol.WithAlpha(MathF.Min(1f, room.Smell * 2f) * (1f - t)), 3f, true);
                }
            }
        }
        // 구획 테두리
        if (w.Ship.Compartments > 1)
            foreach (var room in w.Ship.LiveRooms.Where(r => r.Compartment >= 0))
            {
                var col = CompCols[room.Compartment % CompCols.Length].WithAlpha(0.55f);
                foreach (var cell in room.Cells)
                    foreach (var d in Cell.Dirs4)
                    {
                        var n = cell + d;
                        var other = w.Ship.RoomAt(n) ?? w.Ship.DoorAt(n)?.RoomA;
                        if (other != null && other.Compartment == room.Compartment) continue;
                        var r = CellRect(cell);
                        var a = d.X > 0 ? new Vector2(r.End.X, r.Position.Y) : d.X < 0 ? r.Position : d.Y > 0 ? new Vector2(r.Position.X, r.End.Y) : r.Position;
                        var b = d.X != 0 ? a + new Vector2(0, T) : a + new Vector2(T, 0);
                        ci.DrawLine(a, b, col, 4f);
                    }
            }
    }
}
