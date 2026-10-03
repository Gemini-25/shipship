using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.16 · v18.17 이야기의 그림 (읽기만):
//  바닥 — 밤 모임 식탁: 길쭉한 나무 식탁 · 주전자(김이 오른다) · 사람 수만큼 컵 · 가운데 따뜻한 등불.
//  위 — 이야기의 장소를 찾은 사람은 그 이야기의 물건을 손에 든다(편지 · 약병 · 씨앗 · 별 사진 …) ·
//       할 말이 있어 찾아가는 사람의 말풍선(갈래마다 색) · 막 끝난 대화의 여운(진정: 퍼지는 숨 · 설득: 열쇠 · 고백: 꽃 · 추궁: 손가락 ·
//       털어놓기: 촛불 · 중재: 맞잡은 손 · 고비: 꿰맨 실) — 잘 되면 밝게, 어긋나면 금이 가 있다 · 연인이 나란히 앉으면 따뜻한 빛.
public partial class ShipView
{
    private void PaintTalesFloor(CanvasItem ci)
    {
        var w = _world;
        if (StorySystem.Off || w.Tales.Camp is not CampNight camp) return;
        var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == camp.RoomId);
        if (room == null) return;
        var c = ToPx(room.Center);
        int here = 0;
        foreach (var m in w.Crew) if (!m.Dead && m.Room == room && m.Pose == Pose.Sitting) here++;
        // 등불 빛
        for (int i = 4; i >= 1; i--) ci.DrawCircle(c, T * (0.9f + i * 0.55f), new Color(1f, 0.72f, 0.38f, 0.035f * (1f + 0.15f * Mathf.Sin(_time * 1.7f))));
        // 식탁
        var top = new Rect2(c - new Vector2(T * 1.3f, T * 0.42f), new Vector2(T * 2.6f, T * 0.84f));
        ci.DrawRect(new Rect2(top.Position + new Vector2(2, 3), top.Size), new Color(0, 0, 0, 0.3f));
        ci.DrawRect(top, new Color("#6b4a2e"));
        for (int i = 1; i < 4; i++) ci.DrawLine(top.Position + new Vector2(0, top.Size.Y * i / 4f), top.Position + new Vector2(top.Size.X, top.Size.Y * i / 4f), new Color("#5a3d25"), 1f);
        ci.DrawRect(top, new Color("#8a6a48"), false, 1.2f);
        // 등잔
        ci.DrawCircle(c, T * 0.12f, new Color("#3a2c22"));
        ci.DrawCircle(c + new Vector2(0, -T * 0.05f), T * 0.07f, new Color(1f, 0.85f, 0.5f, 0.95f));
        // 주전자 + 김
        var k = c + new Vector2(-T * 0.75f, 0);
        ci.DrawCircle(k, T * 0.17f, new Color("#9aa3ad"));
        ci.DrawLine(k + new Vector2(T * 0.14f, -T * 0.05f), k + new Vector2(T * 0.3f, -T * 0.15f), new Color("#9aa3ad"), 2.2f, true);
        ci.DrawArc(k + new Vector2(-T * 0.16f, 0), T * 0.09f, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 8, new Color("#6f7882"), 1.6f, true);
        for (int i = 0; i < 3; i++)
        {
            float ph = Mathf.PosMod(_time * 0.6f + i * 0.33f, 1f);
            ci.DrawCircle(k + new Vector2(T * 0.3f + Mathf.Sin(ph * 6f + i) * 2f, -T * (0.2f + ph * 0.45f)), 2.2f * (1f - ph * 0.5f), new Color(1, 1, 1, 0.25f * (1f - ph)));
        }
        // 컵: 앉은 사람 수만큼 (식탁 가장자리)
        int cups = Math.Clamp(here, 2, 10);
        for (int i = 0; i < cups; i++)
        {
            float u = (i + 0.5f) / cups;
            var at = new Vector2(top.Position.X + top.Size.X * (0.2f + 0.75f * ((i * 2) % cups) / (float)cups), i % 2 == 0 ? top.Position.Y + T * 0.12f : top.End.Y - T * 0.12f);
            ci.DrawCircle(at, T * 0.07f, new Color("#e9e2d2"));
            ci.DrawCircle(at, T * 0.045f, i % 3 == 0 ? new Color("#5a3a20") : new Color("#b8743a"));
            _ = u;
        }
    }

    private void PaintTalesOver(CanvasItem ci)
    {
        var w = _world;
        if (StorySystem.Off) return;
        var st = w.Tales;
        bool close = Zoom > 0.8f;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            var px = CrewPx(c);
            var head = px + new Vector2(0, -CrewRadius * 2.1f);
            if (c.Job?.Activity is StoryActivity)
            {
                var t = st.Task(c);
                switch (t.Kind)
                {
                    case StoryTaskKind.Visit when st.ArcOf(c) is Arc a:
                    {
                        // 손에 든 이야기의 물건
                        var hand = px + new Vector2(CrewRadius * 0.9f, CrewRadius * 0.2f);
                        StoryArt.Emblem(ci, a.Spec.Emblem, hand, close ? CrewRadius * 1.1f : CrewRadius * 0.9f, new Color("#3a3f4a"), _time);
                        break;
                    }
                    case StoryTaskKind.Talk when st.Talking(c.Id) is TalkIntent ti:
                    {
                        var col = TalkTint(ti.Kind);
                        var b = new Rect2(head + new Vector2(-7, -6), new Vector2(14, 10));
                        ci.DrawRect(b, new Color(0.1f, 0.1f, 0.14f, 0.85f));
                        ci.DrawRect(b, col, false, 1.2f);
                        ci.DrawColoredPolygon(new[] { head + new Vector2(-2, 4), head + new Vector2(2, 4), head + new Vector2(-3, 8) }, col);
                        for (int i = 0; i < 3; i++) ci.DrawCircle(head + new Vector2(-4 + i * 4, -1), 1f + (Mathf.PosMod(_time * 3f, 3f) > i ? 0.4f : 0f), col);
                        break;
                    }
                    case StoryTaskKind.Date when st.LoveOf(c) is Love lv && w.Crew.FirstOrDefault(x => x.Id == lv.Other(c.Id)) is CrewMember mate && mate.Room == c.Room:
                    {
                        var mp = CrewPx(mate);
                        if ((mp - px).Length() < T * 2.5f) ci.DrawCircle((mp + px) * 0.5f, T * 0.9f, new Color(1f, 0.6f, 0.65f, 0.07f + 0.03f * Mathf.Sin(_time * 2f)));
                        break;
                    }
                }
            }
        }
        // 막 끝난 대화의 여운 (게임 시간 20분)
        for (int i = st.Cards.Count - 1; i >= 0; i--)
        {
            var k = st.Cards[i];
            if (w.Tick - k.Tick > SimTime.Minutes(20)) break;
            var li = w.Crew.FirstOrDefault(x => x.Id == k.Listener);
            if (li == null || li.Dead) continue;
            float fade = 1f - (w.Tick - k.Tick) / (float)SimTime.Minutes(20);
            var at = CrewPx(li) + new Vector2(0, -CrewRadius * 2.6f);
            TalkMark(ci, k, at, fade);
        }
    }

    private static Color TalkTint(CardKind k) => k switch
    {
        CardKind.Persuade => new Color("#ff8a5c"), CardKind.Calm => new Color("#7cc4ff"), CardKind.Mediate => new Color("#f5d547"),
        CardKind.Accuse => new Color("#ff5c6c"), CardKind.Confess => new Color("#ff8ac2"), CardKind.Heart => new Color("#b9a3ff"), _ => new Color("#e8a0d0"),
    };

    /// <summary>대화의 여운: 갈래마다 다른 작은 그림 · 잘 되면 밝게, 어긋나면 금이 간다.</summary>
    private void TalkMark(CanvasItem ci, TalkCard k, Vector2 p, float fade)
    {
        var col = TalkTint(k.Kind).WithAlpha(0.9f * fade);
        var ink = new Color(0.1f, 0.1f, 0.12f, 0.8f * fade);
        switch (k.Kind)
        {
            case CardKind.Calm: // 퍼지는 숨
                for (int i = 0; i < 3; i++) { float ph = Mathf.PosMod(_time * 0.5f + i / 3f, 1f); ci.DrawArc(p, 3f + ph * 7f, 0, Mathf.Tau, 16, col.WithAlpha(col.A * (1f - ph)), 1f, true); }
                break;
            case CardKind.Persuade: // 열쇠
                ci.DrawArc(p + new Vector2(-3, 0), 3f, 0, Mathf.Tau, 10, col, 1.4f, true);
                ci.DrawLine(p, p + new Vector2(7, 0), col, 1.4f, true);
                ci.DrawLine(p + new Vector2(5, 0), p + new Vector2(5, 3), col, 1.2f, true);
                break;
            case CardKind.Confess: // 꽃 한 송이
                for (int i = 0; i < 5; i++) { float a = i / 5f * Mathf.Tau + _time * 0.3f; ci.DrawCircle(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.6f, 1.8f, col); }
                ci.DrawCircle(p, 1.4f, new Color(1f, 0.9f, 0.4f, fade));
                ci.DrawLine(p + new Vector2(0, 3), p + new Vector2(1, 9), new Color(0.35f, 0.75f, 0.4f, fade), 1f, true);
                break;
            case CardKind.Accuse: // 가리키는 손가락
                ci.DrawColoredPolygon(new[] { p + new Vector2(-6, -2), p + new Vector2(5, -1), p + new Vector2(5, 1), p + new Vector2(-6, 2) }, col);
                ci.DrawCircle(p + new Vector2(-6, 0), 2.5f, col);
                break;
            case CardKind.Heart: // 촛불
                ci.DrawRect(new Rect2(p + new Vector2(-1.5f, -1), new Vector2(3, 6)), new Color(0.95f, 0.92f, 0.85f, fade));
                ci.DrawColoredPolygon(new[] { p + new Vector2(-1.2f, -1.5f), p + new Vector2(0, -5.5f - Mathf.Sin(_time * 8f)), p + new Vector2(1.2f, -1.5f) }, new Color(1f, 0.75f, 0.3f, fade));
                break;
            case CardKind.Mediate: // 맞잡은 두 손
                ci.DrawCircle(p + new Vector2(-2.5f, 0), 2.6f, col); ci.DrawCircle(p + new Vector2(2.5f, 0), 2.6f, TalkTint(CardKind.Calm).WithAlpha(col.A));
                break;
            default: // 꿰맨 실
                ci.DrawPolyline(new[] { p + new Vector2(-6, 0), p + new Vector2(-3, -2), p + new Vector2(0, 0), p + new Vector2(3, -2), p + new Vector2(6, 0) }, col, 1.2f, true);
                for (int i = -1; i <= 1; i++) ci.DrawLine(p + new Vector2(i * 3, -3), p + new Vector2(i * 3, 2), ink, 0.8f, true);
                break;
        }
        if (!k.Success) ci.DrawPolyline(new[] { p + new Vector2(-5, -6), p + new Vector2(-1, -1), p + new Vector2(-3, 1), p + new Vector2(3, 6) }, new Color(0.2f, 0.2f, 0.25f, 0.9f * fade), 1.3f, true);
        else ci.DrawCircle(p, 9f, col.WithAlpha(0.08f * fade));
    }
}
