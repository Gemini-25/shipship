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
        // 다가오고 (처음 40분) 멀어진다 (마지막 40분). 우리 배처럼 가로로 누워 위(Side<0)나 아래(Side>0)에 나란히 선다
        float span = SimTime.Minutes(40);
        float inT = Mathf.Clamp((now - m.Start) / span, 0f, 1f), outT = Mathf.Clamp((m.End - now) / span, 0f, 1f);
        float near = Mathf.SmoothStep(0f, 1f, Mathf.Min(inT, outT));
        var b = Bounds;
        float len = b.Size.X * (m.Kind == MeetKind.Generation ? 0.46f : 0.34f);
        float wid = Mathf.Min(T * (m.Kind == MeetKind.Generation ? 4.2f : 3.6f), len * 0.2f);
        float gap = T * 2.5f;
        const float side = 1f; // 늘 아래에 선다 (위쪽은 화면 막대가 가린다)
        float cy = b.End.Y + gap + wid * 0.5f;
        float cx = b.GetCenter().X + (1f - near) * b.Size.X * 0.9f; // 오른쪽(앞)에서 다가와 멈춘다
        var c = new Vector2(cx, cy);
        Vector2 P(float u, float v) => c + new Vector2(u * len, v * wid);
        var hull = m.Kind switch
        {
            MeetKind.Trader => new Color("#7d7058"),
            MeetKind.Distress => new Color("#4f4a4a"),
            MeetKind.Generation => new Color("#5e7a6a"),
            _ => new Color("#5a3c3c"),
        };
        var dark = hull.Darkened(0.45f);
        // 몸통: 앞(오른쪽)이 뾰족한 길쭉한 몸 · 뒤에 엔진 둘
        var body = new[] { P(0.5f, 0f), P(0.36f, -0.5f), P(-0.44f, -0.5f), P(-0.5f, -0.3f), P(-0.5f, 0.3f), P(-0.44f, 0.5f), P(0.36f, 0.5f) };
        ci.DrawColoredPolygon(body, hull);
        ci.DrawPolyline(body.Append(body[0]).ToArray(), dark, 2f, true);
        for (int i = 1; i < 8; i++) ci.DrawLine(P(-0.44f + i * 0.1f, -0.5f), P(-0.44f + i * 0.1f, 0.5f), dark.WithAlpha(0.35f), 1f); // 외판 이음매
        ci.DrawLine(P(-0.44f, 0f), P(0.42f, 0f), hull.Lightened(0.15f).WithAlpha(0.5f), 1f);
        ci.Box(new Rect2(P(-0.56f, -0.42f), new Vector2(len * 0.07f, wid * 0.3f)), dark);
        ci.Box(new Rect2(P(-0.56f, 0.12f), new Vector2(len * 0.07f, wid * 0.3f)), dark);
        switch (m.Kind)
        {
            case MeetKind.Generation:
                // 회전 거주 고리 둘 (옆에서 본 원반 — 배 폭 안에) · 불빛이 돈다
                for (int i = 0; i < 2; i++)
                {
                    var rc = P(-0.12f + i * 0.3f, 0f);
                    var ring = new Rect2(rc - new Vector2(len * 0.025f, wid * 0.8f), new Vector2(len * 0.05f, wid * 1.6f));
                    ci.Box(ring, dark);
                    ci.Box(new Rect2(ring.Position + new Vector2(1.5f, 1.5f), new Vector2(ring.Size.X - 3f, 2f)), hull.Lightened(0.25f));
                    float a = _time * 0.6f + i * 1.7f;
                    ci.Circle(rc + new Vector2(0, Mathf.Sin(a) * wid * 0.7f), 2.5f, new Color("#9fe0b0"));
                }
                break;
            case MeetKind.Trader:
                for (int i = 0; i < 5; i++) // 화물 칸
                    ci.Box(new Rect2(P(-0.34f + i * 0.13f, -0.38f), new Vector2(len * 0.11f, wid * 0.76f)), new Color("#a58d5a").Darkened(i % 2 * 0.2f));
                break;
            case MeetKind.Raider:
                ci.DrawLine(P(0.5f, 0f), P(0.5f, 0f) + new Vector2(T * 1.6f, 0f), dark, 3f); // 충각
                ci.Circle(P(0.1f, -0.55f), 4f, new Color("#c45050")); // 우리 쪽을 겨눈 포대
                ci.Circle(P(-0.15f, -0.55f), 4f, new Color("#c45050"));
                break;
        }
        // 창 불빛 (조난선은 깜빡이다 꺼진다)
        int cols = Mathf.Max(4, (int)(len / (T * 1.4f)));
        for (int i = 1; i < cols - 1; i++)
        {
            float u = -0.4f + 0.8f * i / cols;
            bool on = m.Kind != MeetKind.Distress || Mathf.PosMod(_time * 1.3f + i * 0.7f, 2f) < (i % 3 == 0 ? 0.2f : 1.2f);
            if (!on) continue;
            var lit = m.Kind == MeetKind.Raider ? new Color("#ff8a6b") : new Color("#ffe8a3");
            ci.Box(new Rect2(P(u, -0.3f), new Vector2(3f, 2f)), lit);
            ci.Box(new Rect2(P(u, 0.24f), new Vector2(3f, 2f)), lit);
        }
        // 엔진 불꽃 (뒤 = 왼쪽) — 다가오는 동안은 길다
        float flick = 0.7f + 0.3f * Mathf.Sin(_time * 17f);
        var flame = m.Kind == MeetKind.Distress ? new Color("#ff9b4a").WithAlpha(0.35f * flick) : new Color("#8fd3ff").WithAlpha(0.8f * flick);
        float fl = T * (0.8f + (1f - near) * 3f) * flick;
        foreach (float v in new[] { -0.27f, 0.27f })
            ci.DrawColoredPolygon(new[] { P(-0.56f, v - 0.14f), P(-0.56f, v + 0.14f), P(-0.56f, v) - new Vector2(fl, 0f) }, flame);
        if (m.Kind == MeetKind.Distress && Mathf.PosMod(_time, 0.9f) < 0.12f)
            ci.Circle(P(0.05f, 0.2f), 3f, new Color("#ffd166")); // 끊긴 전선 불꽃
        // 다가와 붙으면: 연결 통로 (약탈선은 갈고리 줄)
        if (near > 0.95f)
        {
            float hullY = b.End.Y;
            float shipY = cy - side * wid * (m.Kind == MeetKind.Generation ? 0.8f : 0.5f);
            for (int i = 0; i < (m.Kind == MeetKind.Raider ? 2 : 1); i++)
            {
                float x = cx + (i - (m.Kind == MeetKind.Raider ? 0.5f : 0f)) * len * 0.3f;
                var from = new Vector2(x, hullY);
                var to = new Vector2(x, shipY);
                if (m.Kind == MeetKind.Raider) { ci.DrawLine(from, to, new Color("#9a9a9a"), 1.2f); continue; }
                ci.DrawLine(from, to, new Color("#6d7688"), 6f);
                ci.DrawLine(from, to, new Color("#aab3c5"), 2f);
                float p = Mathf.PosMod(_time * 0.35f, 1f);
                ci.Circle(from.Lerp(to, p), 2f, new Color("#f5d547")); // 오가는 짐
            }
        }
        // 이름표 (바깥쪽)
        var tag = new Vector2(cx, cy + wid * (m.Kind == MeetKind.Generation ? 0.8f : 0.5f) + 20f);
        Gfx.TextCentered(ci, Fonts.Bold, tag, $"{EncounterSystem.KindName(m.Kind)} {m.Name}", 13, hull.Lightened(0.55f));
    }

    private void PaintFestival(CanvasItem ci)
    {
        var e = _world.Encounters;
        if (e.DecorRoom < 0 || e.DecorUntil <= _world.Tick) return;
        var room = _world.Ship.Rooms.FirstOrDefault(r => r.Id == e.DecorRoom);
        if (room == null || room.Cells.Count == 0) return;
        int x0 = room.Cells.Min(c => c.X), x1 = room.Cells.Max(c => c.X), y0 = room.Cells.Min(c => c.Y);
        var a = new Vector2(x0 * T + 4f, y0 * T + T * 0.6f);
        var bEnd = new Vector2((x1 + 1) * T - 4f, y0 * T + T * 0.6f);
        var cols = new[] { new Color("#ff6b6b"), new Color("#ffd166"), new Color("#6be675"), new Color("#6fa8ff"), new Color("#d58bff") };
        int n = Mathf.Max(4, (x1 - x0 + 1) * 2);
        var prev = a;
        for (int i = 1; i <= n; i++)
        {
            float u = (float)i / n;
            var p = a.Lerp(bEnd, u) + new Vector2(0, Mathf.Abs(Mathf.Sin(u * Mathf.Pi * Mathf.Max(1, (x1 - x0) / 3))) * T * 0.5f);
            ci.DrawLine(prev, p, new Color("#2a2a2a"), 1.5f);
            bool on = Mathf.PosMod(_time * 2f + i * 0.37f, 1.5f) < 1.1f;
            var col = cols[i % cols.Length];
            if (on) ci.Circle(p + new Vector2(0, 2f), 8f, col.WithAlpha(0.22f));
            ci.Circle(p + new Vector2(0, 2f), 3.2f, on ? col : col.Darkened(0.6f));
            prev = p;
        }
    }
}
