using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>v11.2 외부 교신의 그림: 에어락 바깥 해치에 붙은 보급 캡슐 · 다가오는 탈출 캡슐, 통신실 안테나의 송신 파동.</summary>
public partial class ShipView
{
    private void PaintComms(CanvasItem ci)
    {
        var cm = _world.Comms;
        var hatch = _world.Ship.Doors.FirstOrDefault(d => d.IsExternal && !d.Removed && d.RoomA is Room ra && ra.Type == RoomType.Airlock
                                                          || d.IsExternal && !d.Removed && d.RoomB is Room rb && rb.Type == RoomType.Airlock);
        if (hatch != null)
        {
            var outDir = OutwardOf(hatch.Cell);
            if (outDir == Vector2.Zero) outDir = new Vector2(0, 1);
            var at = CellRect(hatch.Cell).GetCenter();
            // 보급 캡슐: 붙어 있다
            if (cm.SupplyDocked) PaintCapsule(ci, at + outDir * T * 1.6f, outDir, new Color("#e0b64a"), "보급", 1f);
            // 다가오는 캡슐 (보급 · 탈출): 마지막 30분 동안 밖에서 다가온다
            long eta = cm.SupplyEta >= 0 ? cm.SupplyEta : cm.PodEta;
            if (eta >= 0)
            {
                float left = (eta - _world.Tick) / (float)SimTime.Minutes(30);
                if (left < 1f)
                {
                    bool pod = cm.PodEta >= 0 && cm.SupplyEta < 0;
                    PaintCapsule(ci, at + outDir * T * (1.6f + 9f * left), outDir, pod ? new Color("#f47b7b") : new Color("#e0b64a"), pod ? "탈출 캡슐" : "보급", 1f - 0.4f * left);
                }
            }
        }
        // 통신실 안테나: 조난 신호를 보낸 뒤 한 시간 · 구조 요청을 듣는 동안 파동
        if (_world.Sensors.Array is Furniture arr && !arr.Room.Detached)
        {
            bool sending = cm.DistressAt >= 0 && _world.Tick - cm.DistressAt < SimTime.Hours(1);
            bool hearing = cm.SignalOpen;
            if (sending || hearing)
            {
                var c = FurnitureRect(arr).GetCenter();
                var col = hearing ? new Color("#f47b7b") : new Color("#7fb2ff");
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.PosMod(_time * 0.8f + k / 3f, 1f);
                    ci.Arc(c, T * (0.6f + 2.2f * ph), -0.9f, 0.9f, 20, col.WithAlpha(0.7f * (1f - ph)), 1.5f, true);
                    ci.Arc(c, T * (0.6f + 2.2f * ph), Mathf.Pi - 0.9f, Mathf.Pi + 0.9f, 20, col.WithAlpha(0.7f * (1f - ph)), 1.5f, true);
                }
            }
        }
    }

    private void PaintCapsule(CanvasItem ci, Vector2 center, Vector2 outDir, Color accent, string label, float alpha)
    {
        float ang = outDir.Angle();
        var along = outDir;
        var side = new Vector2(-outDir.Y, outDir.X);
        float len = T * 1.1f, wid = T * 0.55f;
        var pts = new[]
        {
            center - along * len * 0.5f - side * wid * 0.5f, center + along * len * 0.35f - side * wid * 0.5f,
            center + along * len * 0.55f, center + along * len * 0.35f + side * wid * 0.5f, center - along * len * 0.5f + side * wid * 0.5f,
        };
        ci.Poly(pts, new Color("#2a2f38").WithAlpha(alpha));
        for (int i = 0; i < pts.Length; i++) ci.DrawLine(pts[i], pts[(i + 1) % pts.Length], accent.WithAlpha(0.9f * alpha), 1.5f, true);
        // 도킹 고리와 깜박이는 표시등
        ci.DrawLine(center - along * len * 0.5f, center - along * len * 0.78f, new Color("#8b949e").WithAlpha(alpha), 3f);
        ci.Circle(center + side * wid * 0.25f, 2.2f, accent.WithAlpha((0.5f + 0.5f * Mathf.Sin(_time * 5f)) * alpha), true, -1f, true);
        Gfx.TextCentered(ci, Fonts.Bold, center + along * len * 0.9f + new Vector2(0, 4), label, 10, accent.WithAlpha(alpha));
        _ = ang;
    }
}
