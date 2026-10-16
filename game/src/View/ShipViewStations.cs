using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.21 비상 배치: 경보가 울리면 각자 어깨의 작은 완장 그림(소화기 · 격벽 바퀴 · 번개 · 십자 · 헬멧 · 비상구 화살표) ·
// 큰 일을 거드는 손(이끄는 사람과 잇는 짧은 끈) · 공황에 빠진 사람을 붙잡아 깨운 손(잠깐 남는 호).
// 읽기만 한다 (결정론).
public partial class ShipView
{
    private static Color StationColor(StationRole r) => r switch
    {
        StationRole.Fire => new Color(0.93f, 0.28f, 0.22f),
        StationRole.Bulkhead => new Color(0.55f, 0.72f, 0.9f),
        StationRole.Power => new Color(1f, 0.82f, 0.25f),
        StationRole.Medical => new Color(0.45f, 0.9f, 0.55f),
        StationRole.Eva => new Color(0.85f, 0.88f, 0.95f),
        StationRole.Guide => new Color(0.35f, 0.95f, 0.6f),
        _ => new Color(0.6f, 0.6f, 0.6f),
    };

    private void PaintStations(CanvasItem ci)
    {
        var w = _world;
        var cc = w.CrisisCrew;
        if (CrisisCrewSystem.Off) return;
        float r = CrewRadius;
        foreach (var (id, role) in cc.ActiveRoles)
        {
            var c = w.Brain2.Beliefs.CrewById(id);
            if (c == null || c.Dead || c.Down || c.CarriedBy != null) continue;
            bool atPost = c.Job?.Activity is StationActivity && c.Job.Current is WaitToil;
            bool stand = cc.BillRole(c) != role; // 빈 자리를 대신 맡았다 — 테두리가 점선
            float bob = atPost ? 0f : Mathf.Sin(_time * 7f + id) * 1.2f;
            var at = CrewPx(c) + new Vector2(r * 0.95f, r * 0.55f + bob);
            DrawStationTag(ci, at, role, 6.2f, stand, atPost);
        }
        // 산소 마스크: 얼굴 앞 노란 컵 · 고무줄 · 어깨로 내려간 관
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.CarriedBy != null || !cc.Masked(c)) continue;
            var p = CrewPx(c);
            var f = new Vector2(c.Facing.X, c.Facing.Y);
            if (f.LengthSquared() < 0.01f) f = new Vector2(0, 1);
            f = f.Normalized();
            var face = p + f * r * 0.55f + new Vector2(0, -r * 0.25f);
            var side = new Vector2(-f.Y, f.X);
            ci.DrawLine(face - side * r * 0.5f, face + side * r * 0.5f, new Color(0.2f, 0.2f, 0.2f, 0.8f), 1f, true);
            ci.Circle(face, r * 0.24f, new Color(0.98f, 0.78f, 0.2f));
            ci.Circle(face + f * r * 0.06f, r * 0.12f, new Color(0.75f, 0.55f, 0.1f));
            ci.Polyline(new[] { face, face + side * r * 0.35f + f * r * 0.1f, p + side * r * 0.55f + new Vector2(0, r * 0.3f) }, new Color(0.85f, 0.88f, 0.9f, 0.85f), 1.2f, true);
        }
        // 거드는 사람 ↔ 이끄는 사람: 짧게 이은 끈과 손 (둘이 같은 곳을 잡고 있다)
        foreach (var o in w.Board.All)
        {
            if (o.Closed || o.Assignee is not CrewMember lead || lead.Dead) continue;
            var hs = cc.HelpersOf(o);
            if (hs.Count == 0) continue;
            var pl = CrewPx(lead);
            foreach (var h in hs)
            {
                if (h.Dead || h.Job?.Current is not AssistToil) continue;
                var ph = CrewPx(h);
                var mid = (pl + ph) * 0.5f + new Vector2(0, -3f);
                var col = new Color(0.95f, 0.85f, 0.6f, 0.75f);
                ci.DrawLine(ph, mid, col, 1.4f, true);
                ci.DrawLine(mid, pl, col, 1.4f, true);
                // 손 (둥근 손바닥 + 손가락 세 줄)
                ci.Circle(mid, 2.6f, new Color(0.9f, 0.72f, 0.55f));
                for (int k = -1; k <= 1; k++) ci.DrawLine(mid + new Vector2(k * 1.3f, -1.6f), mid + new Vector2(k * 1.5f, -4f), new Color(0.9f, 0.72f, 0.55f), 1f, true);
            }
        }
        // 정신 차리게 한 손: 3분 동안 옅어지는 호
        foreach (var (tick, from, to, hand) in cc.SnapMarks)
        {
            var a = w.Brain2.Beliefs.CrewById(from);
            var b = w.Brain2.Beliefs.CrewById(to);
            if (a == null || b == null || a.Dead || b.Dead) continue;
            float age = (w.Tick - tick) / (float)SimTime.Minutes(3);
            if (age < 0f || age >= 1f) continue;
            float alpha = 0.8f * (1f - age);
            var pa = CrewPx(a); var pb = CrewPx(b);
            var col = new Color(0.55f, 0.85f, 1f, alpha);
            if (hand)
            {
                // 어깨를 붙잡은 손: 잇는 선 + 받는 사람 어깨에 둥근 손
                ci.DrawLine(pa, pb, col, 2f, true);
                ci.Circle(pb + new Vector2(-r * 0.6f, -r * 0.4f), 2.8f, new Color(0.9f, 0.72f, 0.55f, alpha));
            }
            else
            {
                // 부르는 소리: 받는 사람 쪽으로 퍼지는 물결 셋
                var dir = (pb - pa).Normalized();
                float baseA = dir.Angle();
                for (int k = 0; k < 3; k++)
                {
                    float rr = 6f + k * 4f + age * 10f;
                    ci.Arc(pa, rr, baseA - 0.5f, baseA + 0.5f, 8, col.WithAlpha(alpha * (1f - k * 0.25f)), 1.4f, true);
                }
            }
        }
    }

    /// <summary>완장 하나: 어두운 둥근 판 + 그 자리의 그림 (멀리서는 색 · 가까이서는 모양).</summary>
    private void DrawStationTag(CanvasItem ci, Vector2 at, StationRole role, float s, bool stand, bool atPost)
    {
        var col = StationColor(role);
        var bg = new Color(0.07f, 0.08f, 0.1f, 0.88f);
        ci.Circle(at, s + 1.2f, bg);
        if (stand)
        {
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.Tau / 8f;
                ci.Arc(at, s + 1.2f, a0, a0 + Mathf.Tau / 16f, 3, col, 1f, true);
            }
        }
        else ci.Arc(at, s + 1.2f, 0f, Mathf.Tau, 18, col.WithAlpha(atPost ? 1f : 0.7f), 1f, true);
        var dark = col.Darkened(0.55f);
        switch (role)
        {
            case StationRole.Fire:
            {
                // 소화기: 둥근 통 · 손잡이 · 호스
                var body = new Rect2(at.X - s * 0.32f, at.Y - s * 0.35f, s * 0.64f, s * 1.0f);
                ci.Box(body, col);
                ci.Circle(new Vector2(at.X, at.Y - s * 0.35f), s * 0.32f, col);
                ci.Box(new Rect2(at.X - s * 0.12f, at.Y - s * 0.78f, s * 0.24f, s * 0.2f), new Color(0.2f, 0.2f, 0.22f));
                ci.DrawLine(new Vector2(at.X, at.Y - s * 0.72f), new Vector2(at.X + s * 0.5f, at.Y - s * 0.6f), new Color(0.2f, 0.2f, 0.22f), 1.2f, true);
                ci.DrawLine(new Vector2(at.X + s * 0.5f, at.Y - s * 0.6f), new Vector2(at.X + s * 0.55f, at.Y + s * 0.1f), new Color(0.15f, 0.15f, 0.15f), 1f, true);
                ci.Box(new Rect2(at.X - s * 0.32f, at.Y + s * 0.05f, s * 0.64f, s * 0.12f), new Color(1f, 0.95f, 0.85f, 0.85f));
                break;
            }
            case StationRole.Bulkhead:
            {
                // 격벽 손바퀴: 테 · 바퀴살 넷 · 가운데 축
                ci.Arc(at, s * 0.62f, 0f, Mathf.Tau, 16, col, 1.6f, true);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.Pi / 2f + Mathf.Pi / 4f;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    ci.DrawLine(at, at + d * s * 0.62f, col, 1.1f, true);
                    ci.Circle(at + d * s * 0.7f, 0.9f, col.Lightened(0.3f));
                }
                ci.Circle(at, s * 0.18f, dark);
                break;
            }
            case StationRole.Power:
            {
                // 번개
                var pts = new[]
                {
                    at + new Vector2(s * 0.15f, -s * 0.8f), at + new Vector2(-s * 0.4f, s * 0.1f), at + new Vector2(-s * 0.02f, s * 0.1f),
                    at + new Vector2(-s * 0.18f, s * 0.8f), at + new Vector2(s * 0.42f, -s * 0.15f), at + new Vector2(s * 0.04f, -s * 0.15f),
                };
                ci.Poly(pts, col);
                ci.Polyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[0] }, dark, 0.8f, true);
                break;
            }
            case StationRole.Medical:
            {
                // 흰 판 위 초록 십자
                float a = s * 0.62f, b = s * 0.2f;
                ci.Box(new Rect2(at.X - a, at.Y - b, a * 2f, b * 2f), col);
                ci.Box(new Rect2(at.X - b, at.Y - a, b * 2f, a * 2f), col);
                ci.Box(new Rect2(at.X - b * 0.4f, at.Y - b * 0.4f, b * 0.8f, b * 0.8f), col.Lightened(0.4f));
                break;
            }
            case StationRole.Eva:
            {
                // 우주복 헬멧: 둥근 머리 · 짙은 얼굴 가리개 · 반사광
                ci.Circle(at, s * 0.7f, col);
                ci.Poly(new[]
                {
                    at + new Vector2(-s * 0.48f, -s * 0.12f), at + new Vector2(s * 0.48f, -s * 0.12f),
                    at + new Vector2(s * 0.38f, s * 0.32f), at + new Vector2(-s * 0.38f, s * 0.32f),
                }, new Color(0.15f, 0.25f, 0.4f));
                ci.DrawLine(at + new Vector2(-s * 0.3f, -s * 0.05f), at + new Vector2(-s * 0.05f, -s * 0.05f), new Color(0.8f, 0.95f, 1f, 0.8f), 1f, true);
                ci.Box(new Rect2(at.X - s * 0.45f, at.Y + s * 0.55f, s * 0.9f, s * 0.15f), col.Darkened(0.3f));
                break;
            }
            case StationRole.Guide:
            {
                // 비상구 화살표 (조금씩 흐른다)
                float sh = Mathf.PosMod(_time * 2f, 1f) * s * 0.2f;
                for (int k = 0; k < 2; k++)
                {
                    float x = at.X - s * 0.45f + k * s * 0.45f + sh;
                    ci.Polyline(new[] { new Vector2(x, at.Y - s * 0.45f), new Vector2(x + s * 0.35f, at.Y), new Vector2(x, at.Y + s * 0.45f) }, col, 1.6f, true);
                }
                break;
            }
        }
    }
}
