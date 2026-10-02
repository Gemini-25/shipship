using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.14 실험 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다).
// · 실험대 위의 장치는 분야마다 다르다: 동력 — 코일과 튀는 불꽃 / 냉각 — 서리 낀 시료관과 흰 김 / 생명유지 — 거품 오르는 플라스크 /
//   식량 — 싹 튼 배양 접시 / 선체 — 바이스에 물린 시편과 균열 / 의료 — 현미경 / 컴퓨터 — 기판과 깜빡이는 LED / 로봇 — 관절 팔 /
//   추진 — 작은 노즐과 푸른 불꽃 / 센서 — 오실로스코프 파형 / 제작 — 도가니의 붉은 쇳물 / 거주 — 축소 모형 방 / 방어 — 표적 판과 레이저 점.
//   돌아가면 움직이고(불꽃 · 거품 · 파형), 끊기면 덮개를 덮고 노란 쪽지, 진척은 위쪽 호.
// · 연구 노트: 작업대 위 종이 묶음 — 끊김(노란 쪽지 · 느낌표) · 성공(체크) · 실패(가위표) · 사고(그을린 가장자리) · 돌파구(금빛 별).
// · 흔적: 사고는 방사형 그을음과 깨진 유리 조각(하루), 실패는 작은 연기(한 시간), 돌파구는 반짝임(두 시간).
// · 사람: 실험하는 사람 머리 위에 그 기술의 아이콘 · 대담한 사람은 땀, 신중한 사람은 보안경 · 유물을 살펴보는 사람 곁엔 돋보기.
public partial class ShipView
{
    private void PaintResearch(CanvasItem ci)
    {
        var w = _world;
        var tw = w.TechWeb;
        bool fine = Zoom > 0.9f;
        // 흔적
        foreach (var m in tw.Marks)
        {
            float age = (w.Tick - m.Tick) / (float)SimTime.TicksPerHour;
            var c = ToPx(new System.Numerics.Vector2(m.At.X + 0.5f, m.At.Y + 0.5f));
            switch (m.Kind)
            {
                case 0 when age < 24f:
                {
                    float a = 1f - age / 24f;
                    ci.DrawCircle(c, T * 0.9f, new Color(0.05f, 0.04f, 0.03f, 0.35f * a), true, -1f, true);
                    for (int k = 0; k < 9; k++)
                    {
                        float ang = k * 0.7f + m.At.X;
                        var d = Vector2.FromAngle(ang);
                        ci.DrawLine(c + d * T * 0.2f, c + d * T * (0.75f + 0.3f * ((k * 37) % 5) / 5f), new Color(0.08f, 0.07f, 0.06f, 0.5f * a), 2f, true);
                    }
                    if (fine)
                        for (int k = 0; k < 6; k++)
                        {
                            var g = c + new Vector2(((k * 53) % 21 - 10) * 1.6f, ((k * 29) % 17 - 8) * 1.6f);
                            ci.DrawColoredPolygon(new[] { g, g + new Vector2(3f, 1f), g + new Vector2(1f, 3.5f) }, new Color(0.75f, 0.9f, 1f, 0.55f * a));
                        }
                    break;
                }
                case 1 when age < 2f:
                {
                    float a = 1f - age / 2f;
                    for (int k = 0; k < 5; k++)
                    {
                        float ph = Mathf.PosMod(_time * 0.8f + k * 0.2f, 1f);
                        var p = c + Vector2.FromAngle(k * 1.3f + _time) * T * (0.3f + 0.4f * ph);
                        TechIcons.Glyph(ci, "star", p, 3.5f * (1f - ph) + 1f, new Color(1f, 0.85f, 0.35f, a * (1f - ph)), 1f);
                    }
                    break;
                }
                case 2 when age < 1f:
                {
                    float a = 1f - age;
                    for (int k = 0; k < 3; k++)
                    {
                        float ph = Mathf.PosMod(_time * 0.5f + k * 0.33f, 1f);
                        ci.DrawCircle(c + new Vector2(k * 4f - 4f, -T * 0.3f - ph * T * 0.5f), 3f + 4f * ph, new Color(0.6f, 0.6f, 0.62f, 0.35f * a * (1f - ph)), true, -1f, true);
                    }
                    break;
                }
            }
        }
        // 노트
        int ni = 0;
        foreach (var n in tw.Notes)
        {
            var c = ToPx(new System.Numerics.Vector2(n.At.X + 0.5f, n.At.Y + 0.5f)) + new Vector2((ni % 3) * 4f - 4f, (ni % 2) * 3f - T * 0.18f);
            ni++;
            PaintNote(ci, n, c, fine);
        }
        // 실험대 위 장치
        if (tw.Trial is ExperimentState x && TechWeb.Find(x.Tech) is EraTech t && x.Bench >= 0 && x.Bench < w.Ship.Furniture.Count)
        {
            var bench = w.Ship.Furniture[x.Bench];
            var r = FurnitureRect(bench);
            var lead = x.Lead >= 0 && x.Lead < w.Crew.Count ? w.Crew[x.Lead] : null;
            bool running = lead != null && tw.Researching(lead) && w.Tick - x.LastWork <= 2;
            var c = r.GetCenter() + new Vector2(0f, -T * 0.08f);
            PaintApparatus(ci, t.Field, c, T * 0.36f, running, x.Paused);
            if (x.Progress > 0f)
            {
                var top = new Vector2(c.X, r.Position.Y - 5f);
                ci.DrawArc(top, 6f, 0f, Mathf.Tau, 18, new Color(1, 1, 1, 0.15f), 2f, true);
                ci.DrawArc(top, 6f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * x.Progress, 18, x.Paused ? new Color("#f2c230") : TechIcons.FieldColor(t.Field), 2.2f, true);
            }
            // 사람
            foreach (int id in new[] { x.Lead, x.Partner })
            {
                if (id < 0 || id >= w.Crew.Count) continue;
                var cm = w.Crew[id];
                if (cm.Dead || cm.Away || cm.Job?.Activity is not ResearchActivity) continue;
                var p = ToPx(cm.Position);
                if (Zoom > 0.6f)
                {
                    var b = p + new Vector2(0f, -T * 0.95f);
                    ci.DrawCircle(b, 8.5f, new Color(0.05f, 0.06f, 0.09f, 0.85f), true, -1f, true);
                    TechIcons.Draw(ci, t, b, 7f, 0);
                }
                var style = id == x.Lead ? x.Style : tw.StyleOf(cm);
                if (style == ResearchStyle.Cautious && fine) // 보안경
                {
                    var g = p + new Vector2(0f, -T * 0.32f);
                    ci.DrawRect(new Rect2(g + new Vector2(-6f, -2f), new Vector2(12f, 4f)), new Color(0.55f, 0.85f, 1f, 0.75f));
                    ci.DrawLine(g + new Vector2(-7f, 0f), g + new Vector2(7f, 0f), new Color(0.1f, 0.12f, 0.15f, 0.9f), 1f, true);
                }
                else if (style == ResearchStyle.Bold && fine) // 땀 · 서두름
                {
                    float ph = Mathf.PosMod(_time * 1.6f + id * 0.3f, 1f);
                    var d = p + new Vector2(7f, -T * 0.4f + ph * 7f);
                    ci.DrawCircle(d, 1.6f, new Color(0.55f, 0.82f, 1f, 0.85f * (1f - ph)), true, -1f, true);
                }
                // 유물 살펴보기: 돋보기
                if (x.Relic && !x.RelicSeen && id == x.Lead && TechWeb.Node(t.Id).Gate is TechGate gate && tw.RelicFor(gate.Key) is PlacedProp relic
                    && (relic.At.Center - cm.Position).Length() < 2.2f)
                {
                    var rp = ToPx(new System.Numerics.Vector2(relic.At.X + 0.5f, relic.At.Y + 0.5f)) + new Vector2(Mathf.Sin(_time * 1.3f) * 4f, Mathf.Cos(_time * 1.1f) * 3f);
                    TechIcons.Glyph(ci, "lens", rp, 7f, new Color(1f, 0.9f, 0.6f, 0.9f), 1.4f);
                }
            }
        }
    }

    private void PaintNote(CanvasItem ci, ResearchNote n, Vector2 c, bool fine)
    {
        var paper = n.Kind == 0 ? new Color("#f2d64b") : new Color("#e9e4d6");
        var rect = new Rect2(c + new Vector2(-5f, -4f), new Vector2(10f, 8f));
        ci.DrawRect(new Rect2(rect.Position + new Vector2(1.5f, 1.5f), rect.Size), new Color(0, 0, 0, 0.35f));
        ci.DrawRect(rect, paper);
        if (n.Kind == 3) // 그을린 가장자리
        {
            ci.DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 2f)), new Color(0.15f, 0.1f, 0.06f, 0.8f));
            ci.DrawRect(new Rect2(rect.End - new Vector2(3f, 3f), new Vector2(3f, 3f)), new Color(0.1f, 0.07f, 0.05f, 0.9f));
        }
        if (fine)
            for (int i = 0; i < 3; i++) ci.DrawLine(rect.Position + new Vector2(1.5f, 2f + i * 2f), rect.Position + new Vector2(rect.Size.X - 2f, 2f + i * 2f), new Color(0.3f, 0.3f, 0.4f, 0.5f), 0.6f);
        var mark = rect.End + new Vector2(-1f, -1f);
        switch (n.Kind)
        {
            case 0: Gfx.TextCentered(ci, Fonts.Bold, rect.GetCenter() + new Vector2(0f, 3f), "!", 9, new Color("#a33a1a")); break;
            case 1: TechIcons.Glyph(ci, "check", mark, 3f, new Color("#2f9e5b"), 1.2f); break;
            case 2: TechIcons.Glyph(ci, "slash", mark, 3f, new Color("#c0392b"), 1.2f); ci.DrawLine(mark + new Vector2(2.5f, -2.5f), mark + new Vector2(-2.5f, 2.5f), new Color("#c0392b"), 1.2f, true); break;
            case 4: TechIcons.Glyph(ci, "star", mark, 3.5f, new Color("#f2c94c"), 1f); break;
        }
    }

    /// <summary>분야마다 다른 실험 장치 (돌 때 · 끊겼을 때 · 그냥 놓인 때).</summary>
    private void PaintApparatus(CanvasItem ci, TechField f, Vector2 c, float s, bool running, bool paused)
    {
        var steel = new Color("#8d96a3");
        var dark = new Color("#2a3039");
        var fc = TechIcons.FieldColor(f);
        float t = _time;
        switch (f)
        {
            case TechField.Power:
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.7f, s * 0.2f), new Vector2(s * 1.4f, s * 0.35f)), dark);
                for (int i = 0; i < 5; i++) ci.DrawArc(c + new Vector2(-s * 0.4f + i * s * 0.2f, 0f), s * 0.22f, 0f, Mathf.Tau, 10, new Color("#c9853a"), 1.6f, true);
                if (running) for (int i = 0; i < 3; i++) { var a = c + new Vector2(s * 0.6f, -s * 0.2f); var b = a + Vector2.FromAngle(t * 9f + i * 2.1f) * s * 0.5f; ci.DrawLine(a, b, new Color(0.7f, 0.9f, 1f, 0.9f), 1.2f, true); }
                break;
            case TechField.Cooling:
                for (int i = 0; i < 3; i++)
                {
                    var p = c + new Vector2(-s * 0.5f + i * s * 0.5f, 0f);
                    Gfx.RoundRect(ci, new Rect2(p + new Vector2(-s * 0.12f, -s * 0.5f), new Vector2(s * 0.24f, s * 0.9f)), new Color(0.75f, 0.9f, 1f, 0.55f), 2f, new Color(0.9f, 0.97f, 1f, 0.9f));
                }
                if (running) for (int i = 0; i < 3; i++) { float ph = Mathf.PosMod(t * 0.7f + i * 0.33f, 1f); ci.DrawCircle(c + new Vector2(i * 4f - 4f, -s * 0.6f - ph * s), 2f + 3f * ph, new Color(1f, 1f, 1f, 0.4f * (1f - ph)), true, -1f, true); }
                break;
            case TechField.Life:
                TechIcons.Glyph(ci, "flask", c, s * 0.75f, new Color(0.85f, 0.95f, 1f, 0.9f), 1.3f);
                if (running) for (int i = 0; i < 4; i++) { float ph = Mathf.PosMod(t * 1.2f + i * 0.25f, 1f); ci.DrawCircle(c + new Vector2(Mathf.Sin(i * 2.3f) * s * 0.25f, s * 0.4f - ph * s * 0.9f), 1.3f, fc.WithAlpha(0.9f * (1f - ph)), true, -1f, true); }
                break;
            case TechField.Food:
                ci.DrawCircle(c, s * 0.6f, new Color(0.85f, 0.9f, 0.85f, 0.35f), true, -1f, true);
                ci.DrawArc(c, s * 0.6f, 0f, Mathf.Tau, 18, new Color(0.9f, 0.95f, 0.9f, 0.8f), 1.2f, true);
                for (int i = 0; i < 4; i++) { var p = c + Vector2.FromAngle(i * 1.6f) * s * 0.3f; float h = running ? 0.25f + 0.1f * Mathf.Sin(t * 2f + i) : 0.25f; ci.DrawLine(p, p + new Vector2(0f, -s * h), new Color("#6fbf4a"), 1.4f, true); ci.DrawCircle(p + new Vector2(1.5f, -s * h), 1.6f, new Color("#8fd65a"), true, -1f, true); }
                break;
            case TechField.Hull:
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.7f, -s * 0.15f), new Vector2(s * 0.25f, s * 0.5f)), steel);
                ci.DrawRect(new Rect2(c + new Vector2(s * 0.45f, -s * 0.15f), new Vector2(s * 0.25f, s * 0.5f)), steel);
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.45f, -s * 0.05f), new Vector2(s * 0.9f, s * 0.3f)), new Color("#b4bfcc"));
                if (running) ci.DrawPolyline(new[] { c + new Vector2(0f, -s * 0.05f), c + new Vector2(s * 0.08f, s * 0.05f), c + new Vector2(-s * 0.04f, s * 0.15f), c + new Vector2(s * 0.05f, s * 0.25f) }, new Color(0.1f, 0.1f, 0.12f), 1.2f, true);
                break;
            case TechField.Medical:
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.4f, s * 0.35f), new Vector2(s * 0.8f, s * 0.15f)), dark);
                ci.DrawLine(c + new Vector2(-s * 0.2f, s * 0.35f), c + new Vector2(s * 0.1f, -s * 0.5f), steel, 3f, true);
                ci.DrawCircle(c + new Vector2(s * 0.1f, -s * 0.5f), s * 0.15f, dark, true, -1f, true);
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.15f, s * 0.05f), new Vector2(s * 0.5f, s * 0.08f)), new Color(0.8f, 0.9f, 1f, running ? 0.9f : 0.5f));
                break;
            case TechField.Computing:
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.7f, -s * 0.45f), new Vector2(s * 1.4f, s * 0.9f)), new Color("#1d4d2e"));
                for (int i = 0; i < 4; i++) ci.DrawLine(c + new Vector2(-s * 0.6f, -s * 0.3f + i * s * 0.2f), c + new Vector2(s * 0.6f, -s * 0.3f + i * s * 0.2f), new Color("#c9a24a").WithAlpha(0.6f), 0.8f);
                for (int i = 0; i < 4; i++) ci.DrawCircle(c + new Vector2(-s * 0.45f + i * s * 0.3f, s * 0.3f), 1.6f, running && ((int)(t * 6f) + i) % 3 == 0 ? new Color("#7cff9a") : new Color(0.2f, 0.3f, 0.2f), true, -1f, true);
                break;
            case TechField.Robotics:
            {
                float a = running ? Mathf.Sin(t * 2.2f) * 0.6f : 0f;
                var j1 = c + new Vector2(-s * 0.4f, s * 0.35f);
                var j2 = j1 + Vector2.FromAngle(-1.1f + a) * s * 0.6f;
                var j3 = j2 + Vector2.FromAngle(-0.2f - a) * s * 0.5f;
                ci.DrawLine(j1, j2, new Color("#f2994a"), 3f, true);
                ci.DrawLine(j2, j3, new Color("#f2994a"), 2.4f, true);
                ci.DrawCircle(j1, 2.5f, dark, true, -1f, true);
                ci.DrawCircle(j2, 2f, dark, true, -1f, true);
                ci.DrawLine(j3, j3 + new Vector2(3f, -2f), steel, 1.2f, true);
                ci.DrawLine(j3, j3 + new Vector2(3f, 2f), steel, 1.2f, true);
                break;
            }
            case TechField.Propulsion:
                ci.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.5f, -s * 0.25f), c + new Vector2(s * 0.1f, -s * 0.15f), c + new Vector2(s * 0.1f, s * 0.15f), c + new Vector2(-s * 0.5f, s * 0.25f) }, steel);
                if (running) { float fl = 0.5f + 0.3f * Mathf.Sin(t * 30f); ci.DrawColoredPolygon(new[] { c + new Vector2(s * 0.1f, -s * 0.12f), c + new Vector2(s * (0.3f + 0.5f * fl), 0f), c + new Vector2(s * 0.1f, s * 0.12f) }, new Color(0.45f, 0.75f, 1f, 0.9f)); }
                break;
            case TechField.Sensors:
            {
                Gfx.RoundRect(ci, new Rect2(c + new Vector2(-s * 0.7f, -s * 0.45f), new Vector2(s * 1.4f, s * 0.9f)), dark, 2f, steel);
                var pts = new Vector2[16];
                for (int i = 0; i < 16; i++) { float xx = -0.6f + 1.2f * i / 15f; pts[i] = c + new Vector2(xx * s, (running ? Mathf.Sin(xx * 9f + t * 6f) * 0.25f : 0f) * s); }
                ci.DrawPolyline(pts, new Color("#5ef0a0"), 1.1f, true);
                break;
            }
            case TechField.Fabrication:
                ci.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.4f, -s * 0.3f), c + new Vector2(s * 0.4f, -s * 0.3f), c + new Vector2(s * 0.3f, s * 0.35f), c + new Vector2(-s * 0.3f, s * 0.35f) }, new Color("#4a4f57"));
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.32f, -s * 0.3f), new Vector2(s * 0.64f, s * 0.12f)), running ? new Color(1f, 0.45f + 0.15f * Mathf.Sin(t * 5f), 0.15f) : new Color("#6b3a22"));
                break;
            case TechField.Habitat:
                TechIcons.Glyph(ci, "house", c, s * 0.7f, new Color("#e6a8d7"), 1.2f);
                if (running) ci.DrawCircle(c + new Vector2(0f, s * 0.15f), 2f, new Color(1f, 0.9f, 0.6f, 0.6f + 0.4f * Mathf.Sin(t * 3f)), true, -1f, true);
                break;
            default: // 방어
                ci.DrawArc(c, s * 0.6f, 0f, Mathf.Tau, 18, new Color("#d9dde3"), 1.4f, true);
                ci.DrawArc(c, s * 0.35f, 0f, Mathf.Tau, 14, new Color("#c0392b"), 1.4f, true);
                if (running) ci.DrawCircle(c + new Vector2(Mathf.Sin(t * 3f) * s * 0.2f, Mathf.Cos(t * 2.3f) * s * 0.2f), 1.8f, new Color(1f, 0.2f, 0.2f), true, -1f, true);
                break;
        }
        if (paused) // 덮개
        {
            Gfx.RoundRect(ci, new Rect2(c + new Vector2(-s * 0.85f, -s * 0.65f), new Vector2(s * 1.7f, s * 1.2f)), new Color(0.75f, 0.7f, 0.55f, 0.55f), 3f, new Color(0.6f, 0.55f, 0.4f, 0.8f));
            ci.DrawLine(c + new Vector2(-s * 0.85f, -s * 0.2f), c + new Vector2(s * 0.85f, s * 0.1f), new Color(0.55f, 0.5f, 0.38f, 0.6f), 1f, true);
        }
    }
}
