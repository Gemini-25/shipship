using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v20 관제를 실제 배 위에: 아래 탭 "컴퓨터"가 열려 있으면 창 안 작은 지도 대신 배에 바로 —
//   명령선(컴퓨터실 → 대상 방 · 실선 원격 · 점선 사람 손 · 초록 로봇 · 열린 명령은 빛이 흐른다) ·
//   못 보는 방(어둡게 · ?) · 무선만 닿는 방(전파) · 잘못 아는 방(붉은 점선 테 · ≠) · 계기가 이상한 방(노란 점).
// 늘: 기다리는 제안이 있으면 컴퓨터실 위에 짧은 말풍선 (몇 건 · F6).
public partial class ShipView
{
    private static readonly Color OvRemote = new("#7cc4ff"), OvHands = new("#f2c66d"), OvRobot = new("#6ee7b7");

    private void PaintComputerOverlay(CanvasItem ci)
    {
        var w = _world;
        var a = w.Automation;
        if (!a.Present) return;
        var home = a.Computer?.Body.Room;
        if (_main.Hud.Dock == DockTab.Computer)
        {
            foreach (var room in w.Ship.LiveRooms)
            {
                if (room.Cells.Count == 0 || room.Type == RoomType.Corridor) continue;
                var rr = RoomRect(room);
                if (!_fixView.Intersects(rr)) continue;
                int reach = a.Core.Reach(room);
                var mid = rr.GetCenter();
                if (reach == 0)
                {
                    ci.DrawRect(rr, new Color(0.02f, 0.03f, 0.05f, 0.45f));
                    Gfx.TextCentered(ci, Fonts.Bold, mid, "?", 22, new Color(0.75f, 0.8f, 0.9f, 0.75f));
                    continue;
                }
                if (reach == 1)
                    for (int k = 1; k <= 3; k++) ci.Arc(rr.Position + new Vector2(14f, 18f), 4f * k, -Mathf.Pi * 0.8f, -Mathf.Pi * 0.2f, 8, Palette.Warning.WithAlpha(0.8f), 1.5f, true);
                if (a.Belief.Diverged(room, out _))
                {
                    float pulse = 0.55f + 0.45f * Mathf.Sin(_time * 6f);
                    var col = Palette.Danger.WithAlpha(pulse);
                    OvDashedRect(ci, rr.Grow(-3f), col, 2f, 10f);
                    Gfx.TextCentered(ci, Fonts.Bold, new Vector2(rr.End.X - 16f, rr.Position.Y + 18f), "≠", 20, col);
                }
                else if (a.Belief.Of(room).Fault != SensorFault.None) ci.Circle(new Vector2(rr.End.X - 12f, rr.Position.Y + 12f), 4f, Palette.Warning, true, -1f, true);
            }
            if (home != null) PaintCommandLines(ci, w, a, RoomRect(home).GetCenter());
        }
        // 기다리는 제안: 컴퓨터실 위 말풍선
        int n = a.Asks.Open.Count();
        if (n > 0 && home != null && !home.Detached)
        {
            var top = RoomRect(home);
            float k = 1f / Mathf.Clamp(Zoom, 0.3f, 2f); // 화면에서 늘 같은 크기로
            int fs = Mathf.RoundToInt(14f * k);
            string text = $"제안 {n}건 — 컴퓨터 F6";
            float tw = Gfx.Width(Fonts.Bold, text, fs) + 22f * k, th = 26f * k;
            var bubble = new Rect2(new Vector2(top.GetCenter().X - tw * 0.5f, top.Position.Y - th - 10f * k + Mathf.Sin(_time * 2.5f) * 2f * k), new Vector2(tw, th));
            var fill = new Color(0.18f, 0.12f, 0.05f, 0.94f);
            Gfx.RoundRect(ci, bubble, fill, 8f * k, Palette.Warning.WithAlpha(0.6f + 0.4f * Mathf.Sin(_time * 4f)), Mathf.Max(1, Mathf.RoundToInt(2f * k)));
            ci.DrawColoredPolygon(new[] { new Vector2(bubble.GetCenter().X - 7f * k, bubble.End.Y), new Vector2(bubble.GetCenter().X + 7f * k, bubble.End.Y), new Vector2(bubble.GetCenter().X, bubble.End.Y + 9f * k) }, fill);
            Gfx.TextCentered(ci, Fonts.Bold, bubble.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, fs)), text, fs, Palette.Warning);
        }
    }

    /// <summary>명령선: 열린 명령 · 6분 안의 명령이 컴퓨터실에서 대상 방까지 휘어 흐른다.</summary>
    private void PaintCommandLines(CanvasItem ci, World w, AutomationSystem a, Vector2 from)
    {
        int drawn = 0;
        var lines = a.Command.Lines;
        for (int i = lines.Count - 1; i >= 0 && drawn < 16; i--)
        {
            var o = lines[i];
            if (!o.Open && w.Tick - o.Tick > SimTime.Minutes(6)) continue;
            if (o.RoomId < 0 || o.RoomId >= w.Ship.Rooms.Count) continue;
            var to = RoomRect(w.Ship.Rooms[o.RoomId]).GetCenter();
            if ((to - from).Length() < 4f) continue;
            drawn++;
            var col = o.Target switch { CmdTarget.Crew => OvHands, CmdTarget.Robot or CmdTarget.Drone => OvRobot, _ => OvRemote };
            float age = (w.Tick - o.Tick) / (float)SimTime.Minutes(6);
            float alpha = o.Open ? 0.95f : Mathf.Clamp(1f - age, 0.2f, 0.7f);
            var mid = (from + to) * 0.5f + (to - from).Orthogonal().Normalized() * Mathf.Min(T * 2f, (to - from).Length() * 0.18f) * (i % 2 == 0 ? 1f : -1f);
            var pts = new Vector2[17];
            for (int k = 0; k <= 16; k++) { float t = k / 16f; pts[k] = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * mid + t * t * to; }
            if (o.Remote) ci.DrawPolyline(pts, col.WithAlpha(alpha * 0.7f), 2.5f, true);
            else for (int k = 0; k < 16; k += 2) ci.DrawLine(pts[k], pts[k + 1], col.WithAlpha(alpha * 0.75f), 2.5f, true);
            if (o.Open)
            {
                float ph = Mathf.PosMod(_time * 0.6f + i * 0.13f, 1f);
                int seg = Math.Min(15, (int)(ph * 16f));
                ci.Circle(pts[seg].Lerp(pts[seg + 1], ph * 16f - seg), 4f, col, true, -1f, true);
            }
            ci.Arc(to, 9f, 0f, Mathf.Tau, 20, col.WithAlpha(alpha), 2f, true);
            CommandIcons.Draw(ci, o.Target, to, 6f, col.WithAlpha(Mathf.Max(0.5f, alpha)), _time);
        }
    }

    private static void OvDashedRect(CanvasItem ci, Rect2 r, Color c, float width, float dash)
    {
        void Side(Vector2 a, Vector2 b)
        {
            float len = (b - a).Length();
            var d = (b - a) / Mathf.Max(1f, len);
            for (float s = 0; s < len; s += dash * 2f) ci.DrawLine(a + d * s, a + d * Mathf.Min(len, s + dash), c, width);
        }
        Side(r.Position, new Vector2(r.End.X, r.Position.Y));
        Side(new Vector2(r.End.X, r.Position.Y), r.End);
        Side(r.End, new Vector2(r.Position.X, r.End.Y));
        Side(new Vector2(r.Position.X, r.End.Y), r.Position);
    }
}
