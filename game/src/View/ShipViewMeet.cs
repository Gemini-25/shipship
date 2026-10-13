using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 볼거리: 나란히 선 다른 배 (교역선 · 조난선 · 다른 세대선 · 약탈선) · 축제 날 방에 걸린 줄 전구.
public partial class ShipView
{
    private void PaintMeetShip(CanvasItem ci)
    {
        if (_world.Encounters.Now is not ShipMeet m) return;
        long now = _world.Tick;
        // 다가오고 (처음 40분) 멀어진다 (마지막 40분)
        float span = SimTime.Minutes(40);
        float inT = Mathf.Clamp((now - m.Start) / span, 0f, 1f), outT = Mathf.Clamp((m.End - now) / span, 0f, 1f);
        float near = Mathf.SmoothStep(0f, 1f, Mathf.Min(inT, outT));
        var b = Bounds;
        float len = m.Kind == MeetKind.Generation ? b.Size.Y * 0.9f : b.Size.Y * 0.45f;
        float wid = len * (m.Kind == MeetKind.Generation ? 0.32f : 0.24f);
        float gap = T * 5f + (1f - near) * T * 40f;
        float cx = m.Side > 0 ? b.End.X + gap + wid * 0.5f : b.Position.X - gap - wid * 0.5f;
        float cy = b.GetCenter().Y + (1f - near) * T * 10f * m.Side;
        var c = new Vector2(cx, cy);
        var hull = m.Kind switch
        {
            MeetKind.Trader => new Color("#7d7058"),
            MeetKind.Distress => new Color("#4f4a4a"),
            MeetKind.Generation => new Color("#5e7a6a"),
            _ => new Color("#5a3c3c"),
        };
        var dark = hull.Darkened(0.45f);
        // 몸통: 위가 뾰족한 길쭉한 몸 + 옆 날개
        var top = c + new Vector2(0, -len * 0.5f);
        var bot = c + new Vector2(0, len * 0.5f);
        var body = new[]
        {
            top, c + new Vector2(wid * 0.5f, -len * 0.3f), c + new Vector2(wid * 0.5f, len * 0.42f),
            bot, c + new Vector2(-wid * 0.5f, len * 0.42f), c + new Vector2(-wid * 0.5f, -len * 0.3f),
        };
        ci.DrawColoredPolygon(body, hull);
        ci.DrawPolyline(body.Append(top).ToArray(), dark, 2f, true);
        if (m.Kind == MeetKind.Generation)
        {
            // 회전 고리 둘
            for (int i = 0; i < 2; i++)
            {
                var rc = c + new Vector2(0, (i - 0.5f) * len * 0.4f);
                ci.Arc(rc, wid * 0.85f, 0, Mathf.Tau, 28, dark, 4f, true);
                float a = _time * 0.4f + i;
                ci.Circle(rc + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.35f) * wid * 0.85f, 3f, new Color("#9fe0b0"));
            }
        }
        else if (m.Kind == MeetKind.Trader)
        {
            // 화물 칸
            for (int i = 0; i < 4; i++)
                ci.Box(new Rect2(c + new Vector2(-wid * 0.42f, -len * 0.18f + i * len * 0.13f), new Vector2(wid * 0.84f, len * 0.1f)), new Color("#a58d5a").Darkened(i % 2 * 0.2f));
        }
        else if (m.Kind == MeetKind.Raider)
        {
            // 앞에 돋친 가시 · 포대
            ci.DrawLine(top, top + new Vector2(0, -T * 1.5f), dark, 3f);
            ci.Circle(c + new Vector2(wid * 0.55f, -len * 0.1f), 4f, new Color("#c45050"));
            ci.Circle(c + new Vector2(-wid * 0.55f, -len * 0.1f), 4f, new Color("#c45050"));
        }
        // 날개
        ci.DrawLine(c + new Vector2(wid * 0.5f, len * 0.2f), c + new Vector2(wid * 1.1f, len * 0.38f), dark, 3f, true);
        ci.DrawLine(c + new Vector2(-wid * 0.5f, len * 0.2f), c + new Vector2(-wid * 1.1f, len * 0.38f), dark, 3f, true);
        // 창 불빛 (조난선은 깜빡이다 꺼진다)
        int rows = Mathf.Max(3, (int)(len / (T * 1.2f)));
        for (int i = 1; i < rows - 1; i++)
        {
            float y = -len * 0.3f + (len * 0.7f) * i / rows;
            bool on = m.Kind != MeetKind.Distress || Mathf.PosMod(_time * 1.3f + i * 0.7f, 2f) < (i % 3 == 0 ? 0.2f : 1.2f);
            if (!on) continue;
            var lit = m.Kind == MeetKind.Raider ? new Color("#ff8a6b") : new Color("#ffe8a3");
            ci.Box(new Rect2(c + new Vector2(-wid * 0.3f, y), new Vector2(3f, 2f)), lit);
            ci.Box(new Rect2(c + new Vector2(wid * 0.3f - 3f, y), new Vector2(3f, 2f)), lit);
        }
        // 엔진 불꽃
        float flick = 0.7f + 0.3f * Mathf.Sin(_time * 17f);
        var flame = m.Kind == MeetKind.Distress ? new Color("#ff9b4a").WithAlpha(0.35f * flick) : new Color("#8fd3ff").WithAlpha(0.8f * flick);
        ci.DrawColoredPolygon(new[] { bot + new Vector2(-wid * 0.25f, -2f), bot + new Vector2(wid * 0.25f, -2f), bot + new Vector2(0, T * (1.2f + (1f - near) * 2f) * flick) }, flame);
        if (m.Kind == MeetKind.Distress && Mathf.PosMod(_time, 0.9f) < 0.12f)
            ci.Circle(c + new Vector2(wid * 0.2f, len * 0.1f), 3f, new Color("#ffd166")); // 끊긴 전선 불꽃
        // 다가와 붙으면: 연결 통로 (약탈선은 갈고리 줄)
        if (near > 0.95f)
        {
            var from = new Vector2(m.Side > 0 ? b.End.X : b.Position.X, cy);
            var to = new Vector2(cx - m.Side * wid * 0.5f, cy);
            if (m.Kind == MeetKind.Raider)
            {
                for (int i = 0; i < 2; i++) ci.DrawLine(from + new Vector2(0, (i - 0.5f) * T * 2f), to + new Vector2(0, (i - 0.5f) * T), new Color("#9a9a9a"), 1.2f);
            }
            else
            {
                ci.DrawLine(from, to, new Color("#6d7688"), 6f);
                ci.DrawLine(from, to, new Color("#aab3c5"), 2f);
                float p = Mathf.PosMod(_time * 0.35f, 1f);
                ci.Circle(from.Lerp(to, m.Side > 0 ? p : 1f - p), 2f, new Color("#f5d547")); // 오가는 짐
            }
        }
        // 이름표
        Gfx.TextCentered(ci, Fonts.Bold, top + new Vector2(0, -T * 1.4f), $"{EncounterSystem.KindName(m.Kind)} {m.Name}", 13, hull.Lightened(0.55f));
    }

    private void PaintFestival(CanvasItem ci)
    {
        var e = _world.Encounters;
        if (e.DecorRoom < 0 || e.DecorUntil <= _world.Tick) return;
        var room = _world.Ship.Rooms.FirstOrDefault(r => r.Id == e.DecorRoom);
        if (room == null || room.Cells.Count == 0) return;
        int x0 = room.Cells.Min(c => c.X), x1 = room.Cells.Max(c => c.X), y0 = room.Cells.Min(c => c.Y);
        var a = new Vector2(x0 * T + 3f, y0 * T + 3f);
        var bEnd = new Vector2((x1 + 1) * T - 3f, y0 * T + 3f);
        var cols = new[] { new Color("#ff6b6b"), new Color("#ffd166"), new Color("#6be675"), new Color("#6fa8ff"), new Color("#d58bff") };
        int n = Mathf.Max(4, (x1 - x0 + 1) * 2);
        var prev = a;
        for (int i = 1; i <= n; i++)
        {
            float u = (float)i / n;
            var p = a.Lerp(bEnd, u) + new Vector2(0, Mathf.Sin(u * Mathf.Pi * Mathf.Max(1, (x1 - x0) / 3)) * T * 0.35f);
            ci.DrawLine(prev, p, new Color("#3a3a3a"), 1f);
            bool on = Mathf.PosMod(_time * 2f + i * 0.37f, 1.5f) < 1.1f;
            var col = cols[i % cols.Length];
            ci.Circle(p, 2.2f, on ? col : col.Darkened(0.6f));
            if (on) ci.Circle(p, 5f, col.WithAlpha(0.18f));
            prev = p;
        }
    }
}
