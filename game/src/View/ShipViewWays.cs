using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.25 여러 갈래 해법이 남기는 그림 — 갈래마다 실루엣 · 무늬 · 움직임이 다르다:
///   문틈에 쑤셔 넣은 젖은 수건 · 수경 탱크에서 끌어온 호스와 초록빛 물웅덩이 · 엎어진 양동이 · 그을린 담요 더미 · 냄비 뚜껑 ·
///   구멍에 빨려 들어간 줄무늬 매트리스(닳으면 처지고 바람 줄이 샌다) · 다리가 삐죽한 탁자 상판 · 서리 낀 냄비 · 얼음 마개 · 몸으로 막은 로봇 ·
///   벌어진 문짝과 박힌 쇠지레 · 잘라 낸 문틀의 붉은 자국 · 날아간 문 · 열어 둔 통로 덮개 · 문 모터에 물린 노란 임시선 ·
///   배터리를 뺀 로봇 · 전선을 단 달리기 기구 · 작업등 빛 · 들것 · 수레 · 펼친 구급 키트 · 속이 빈 설비(늘어진 선 · 빈 자리 테두리) ·
///   테이프로 감은 임시 부품 · 쇳밥 · 산소 양초 · 물 가르는 병 · 묶은 짐 · 바퀴 밑 숟가락 · 테이프와 호스 · 냄비 보온기 …
/// 그리기는 시뮬레이션 상태를 읽기만 한다.
/// </summary>
public partial class ShipView
{
    private static readonly Color WyCloth = new("#6f8fb0"), WyWet = new("#3f6f9f"), WySoot = new("#1c1610"), WyEmber = new("#ff7a2a"),
        WyMetal = new("#9aa4ad"), WyDark = new("#2a2f36"), WyFrost = new("#dff4ff"), WyCable = new("#e8c547"), WyRubber = new("#2e3a2c"), WyWood = new("#9a7048");

    private void PaintWays(CanvasItem ci)
    {
        var ways = _world.Ways;
        // 매트리스를 떼어 간 침대: 맨 틀 (나무 살)
        foreach (var f in _world.Ship.Furniture)
        {
            if (f.Type is not (FurnitureType.Bed or FurnitureType.Cot or FurnitureType.MedBed) || !ways.IsBare(f.Id)) continue;
            var r = FurnitureRect(f).Grow(-4f);
            ci.DrawRect(r, new Color(0.16f, 0.13f, 0.1f, 0.85f));
            bool wide = r.Size.X > r.Size.Y;
            int n = (int)((wide ? r.Size.X : r.Size.Y) / 7f);
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)n;
                if (wide) ci.DrawLine(new Vector2(r.Position.X + r.Size.X * t, r.Position.Y + 2), new Vector2(r.Position.X + r.Size.X * t, r.End.Y - 2), WyWood, 2.5f);
                else ci.DrawLine(new Vector2(r.Position.X + 2, r.Position.Y + r.Size.Y * t), new Vector2(r.End.X - 2, r.Position.Y + r.Size.Y * t), WyWood, 2.5f);
            }
            Gfx.RoundRect(ci, r, new Color(0, 0, 0, 0), 3, WyWood.Darkened(0.3f), 2);
        }
        foreach (var m in ways.Marks)
        {
            float a = 1f;
            if (m.Until >= 0) a = Mathf.Clamp((m.Until - _world.Tick) / (float)SimTime.Hours(2), 0.15f, 1f);
            if (!m.Ok && !m.Active) a *= 0.8f;
            var rect = CellRect(m.At);
            var o = rect.GetCenter();
            var dir = new Vector2(m.Dir.X, m.Dir.Y);
            if (dir.LengthSquared() > 0.01f) dir = dir.Normalized(); else dir = new Vector2(0, 1);
            var side = new Vector2(-dir.Y, dir.X);
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5f + m.At.X);
            PaintWay(ci, m, o, dir, side, a, pulse);
        }
    }

    private void PaintWay(CanvasItem ci, WayMark m, Vector2 o, Vector2 dir, Vector2 side, float a, float pulse)
    {
        float h = T * 0.5f;
        switch (m.Look)
        {
            case WayLook.Seal:
            {
                // 문 앞에 쑤셔 넣은 젖은 수건 (말린 천 · 물방울) · 문짝에 분필 ×
                var p = o + dir * h * 0.75f;
                for (int i = -2; i <= 2; i++) ci.DrawCircle(p + side * (i * 5f), 4.5f, WyWet.WithAlpha(0.95f * a));
                ci.DrawLine(p - side * 13f, p + side * 13f, WyCloth.Lightened(0.2f).WithAlpha(a), 2f, true);
                for (int i = 0; i < 3; i++) ci.DrawCircle(p + side * (i * 7f - 7f) + dir * (3f + 2f * ((i + (int)(_time * 2)) % 3)), 1.3f, WyWet.Lightened(0.4f).WithAlpha(0.8f * a));
                var c = Colors.White.WithAlpha(0.55f * a);
                ci.DrawLine(o - side * 6f - dir * 6f, o + side * 6f + dir * 2f, c, 2f, true);
                ci.DrawLine(o + side * 6f - dir * 6f, o - side * 6f + dir * 2f, c, 2f, true);
                break;
            }
            case WayLook.Burnt:
            {
                // 타게 두고 잠근 문: 문틈의 그을음 부채꼴 · 자물쇠
                for (int i = -3; i <= 3; i++) ci.DrawColoredPolygon(new[] { o, o - dir * h * 1.4f + side * (i * 5f), o - dir * h * 1.4f + side * (i * 5f + 3f) }, WySoot.WithAlpha(0.45f * a));
                var lk = o + dir * h * 0.6f;
                Gfx.RoundRect(ci, new Rect2(lk - new Vector2(5, 3), new Vector2(10, 8)), new Color("#b88a2a").WithAlpha(a), 2);
                ci.DrawArc(lk - new Vector2(0, 3), 3.5f, Mathf.Pi, Mathf.Tau, 8, new Color("#b88a2a").WithAlpha(a), 1.6f, true);
                break;
            }
            case WayLook.Douse or WayLook.HydroHose or WayLook.Bucket:
            {
                // 물웅덩이 (양액이면 초록빛) · 동심 물결 · 김
                var water = m.Look == WayLook.HydroHose ? new Color("#5fa86a") : WyWet.Lightened(0.15f);
                DrawEllipse(ci, o, T * 0.62f, T * 0.4f, water.WithAlpha(0.45f * a));
                for (int i = 0; i < 2; i++) ci.DrawArc(o, T * (0.18f + 0.18f * ((i + _time * 0.6f) % 1f)), 0, Mathf.Tau, 18, water.Lightened(0.3f).WithAlpha(0.6f * a * (1f - (i + _time * 0.6f) % 1f)), 1.2f, true);
                if (m.Active || a > 0.7f) for (int i = 0; i < 3; i++) { float y = (_time * 14f + i * 9f) % 26f; ci.DrawCircle(o + new Vector2(-6 + i * 6, -y), 2.5f + y * 0.1f, new Color(0.9f, 0.92f, 0.95f, 0.22f * a * (1f - y / 26f))); }
                if (m.Look == WayLook.HydroHose)
                {
                    // 수경 탱크 쪽으로 늘어진 호스
                    var end = o + dir * T * 2.5f;
                    var mid = (o + end) * 0.5f + side * 9f;
                    ci.DrawPolyline(new[] { o, (o + mid) * 0.5f + side * 3f, mid, (mid + end) * 0.5f - side * 2f, end }, new Color("#3f7a4a").WithAlpha(a), 4f, true);
                    ci.DrawRect(new Rect2(o - new Vector2(3, 3), new Vector2(6, 6)), WyMetal.WithAlpha(a));
                }
                if (m.Look == WayLook.Bucket)
                {
                    // 엎어진 양동이
                    var b = o + side * 10f + dir * 6f;
                    ci.DrawColoredPolygon(new[] { b + new Vector2(-7, -5), b + new Vector2(7, -7), b + new Vector2(9, 5), b + new Vector2(-6, 6) }, new Color("#c84a3a").WithAlpha(a));
                    DrawEllipse(ci, b + new Vector2(-7, 0), 2.5f, 6f, new Color("#7a2a20").WithAlpha(a));
                }
                break;
            }
            case WayLook.Smother:
            {
                // 그을린 담요 더미 (주름) · 실패면 불씨
                var pts = new List<Vector2>();
                for (int i = 0; i < 9; i++) { float ang = i / 9f * Mathf.Tau; float r = h * (0.75f + 0.18f * Mathf.Sin(i * 2.3f + m.At.Y)); pts.Add(o + new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.8f)); }
                ci.DrawColoredPolygon(pts.ToArray(), (m.Mat == Material.Rubber ? WyRubber : new Color("#5b4a3e")).WithAlpha(0.95f * a));
                for (int i = 0; i < 3; i++) ci.DrawLine(o + new Vector2(-10 + i * 8, -6), o + new Vector2(-6 + i * 8, 6), WySoot.WithAlpha(0.7f * a), 2f, true);
                ci.DrawCircle(o + new Vector2(3, -2), 5f, WySoot.WithAlpha(0.6f * a));
                if (!m.Ok) for (int i = 0; i < 5; i++) ci.DrawCircle(o + new Vector2(Mathf.Sin(i * 1.7f) * 9f, Mathf.Cos(i * 2.1f) * 6f), 1.6f, WyEmber.WithAlpha((0.4f + 0.6f * pulse) * a));
                else { float y = (_time * 10f) % 20f; ci.DrawCircle(o - new Vector2(0, y), 2f + y * 0.15f, new Color(0.5f, 0.5f, 0.5f, 0.25f * a * (1 - y / 20f))); }
                break;
            }
            case WayLook.PotLid:
                ci.DrawCircle(o, h * 0.7f, WyMetal.WithAlpha(a));
                ci.DrawArc(o, h * 0.7f, 0, Mathf.Tau, 20, WyMetal.Lightened(0.4f).WithAlpha(a), 1.5f, true);
                ci.DrawCircle(o, 3.5f, WyDark.WithAlpha(a));
                ci.DrawArc(o, h * 0.85f + 2f * pulse, 0, Mathf.Tau, 20, new Color(1f, 0.6f, 0.3f, 0.3f * a), 1.2f, true);
                break;
            case WayLook.Eject:
            {
                // 문밖에 내던진 탄 덩어리 · 끌린 그을음 자국
                for (int i = 0; i < 4; i++) ci.DrawLine(o + dir * (i * 6f), o + dir * (i * 6f + 3f), WySoot.WithAlpha(0.6f * a), 3f, true);
                ci.DrawColoredPolygon(new[] { o + new Vector2(-8, -3), o + new Vector2(-2, -8), o + new Vector2(7, -5), o + new Vector2(8, 4), o + new Vector2(-4, 7) }, WySoot.WithAlpha(a));
                for (int i = 0; i < 3; i++) ci.DrawCircle(o + new Vector2(-3 + i * 4, -1 + (i % 2) * 3), 1.4f, WyEmber.WithAlpha(pulse * a * 0.8f));
                break;
            }
            case WayLook.MattressPlug:
            {
                // 구멍에 빨려 들어가 불룩한 줄무늬 매트리스 · 끈 · 닳으면 처지고 바람이 샌다
                float sag = (1f - Mathf.Clamp(m.Q, 0f, 1f)) * 6f;
                var b = o + dir * (h * 0.35f + sag);
                var pts = new[] { b - side * h * 1.1f - dir * 5f, b - side * h * 1.2f + dir * 6f, b + dir * 9f, b + side * h * 1.2f + dir * 6f, b + side * h * 1.1f - dir * 5f, b - dir * 9f };
                ci.DrawColoredPolygon(pts, new Color("#e8e2d0").WithAlpha(a));
                for (int i = -2; i <= 2; i++) ci.DrawLine(b + side * (i * 6f) - dir * 7f, b + side * (i * 6f) + dir * 7f, new Color("#4a6fa5").WithAlpha(a), 2f, true);
                ci.DrawLine(b - side * h * 0.6f - dir * 9f, b - side * h * 0.6f + dir * 9f, new Color("#3a3a3a").WithAlpha(a), 1.5f, true);
                ci.DrawLine(b + side * h * 0.6f - dir * 9f, b + side * h * 0.6f + dir * 9f, new Color("#3a3a3a").WithAlpha(a), 1.5f, true);
                if (m.Q < 0.45f) for (int i = 0; i < 3; i++) { float t = (_time * 3f + i * 0.33f) % 1f; ci.DrawLine(o - dir * 4f + side * (i * 8f - 8f), o - dir * 4f + side * (i * 8f - 8f) + dir * (-6f - 10f * t), new Color(0.85f, 0.9f, 1f, 0.5f * (1 - t) * a), 1f, true); }
                break;
            }
            case WayLook.TablePlug:
            {
                var b = o + dir * h * 0.45f;
                ci.DrawColoredPolygon(new[] { b - side * h * 1.2f - dir * 4f, b + side * h * 1.2f - dir * 4f, b + side * h * 1.1f + dir * 4f, b - side * h * 1.1f + dir * 4f }, WyWood.WithAlpha(a));
                ci.DrawLine(b - side * h * 1.1f, b + side * h * 1.1f, WyWood.Darkened(0.35f).WithAlpha(a), 1f, true);
                foreach (var s in new[] { -0.8f, 0.8f }) ci.DrawLine(b + side * h * s + dir * 3f, b + side * h * s + dir * 15f, WyWood.Darkened(0.45f).WithAlpha(a), 3f, true);
                ci.DrawColoredPolygon(new[] { b + dir * 4f - side * 3f, b + dir * 10f, b + dir * 4f + side * 3f }, WyDark.WithAlpha(a)); // 괸 쐐기
                break;
            }
            case WayLook.PotPlug:
            {
                var b = o + dir * h * 0.4f;
                ci.DrawCircle(b, h * 0.62f, WyMetal.Darkened(0.1f).WithAlpha(a));
                ci.DrawArc(b, h * 0.62f, 0, Mathf.Tau, 20, WyMetal.Lightened(0.35f).WithAlpha(a), 2f, true);
                ci.DrawLine(b + side * h * 0.6f, b + side * h * 1.05f, WyDark.WithAlpha(a), 3f, true);
                ci.DrawArc(b, h * 0.75f, 0, Mathf.Tau, 24, WyFrost.WithAlpha(0.6f * a), 1.5f, true); // 서리 고리
                break;
            }
            case WayLook.CratePlug:
            {
                var b = o + dir * h * 0.4f;
                var r = new Rect2(b - new Vector2(h * 0.85f, h * 0.55f), new Vector2(h * 1.7f, h * 1.1f));
                Gfx.RoundRect(ci, r, new Color("#7a8fa8").WithAlpha(a), 3, new Color("#4c5d72").WithAlpha(a), 1);
                for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(r.Position + new Vector2(4 + i * (r.Size.X - 8) / 3f, r.Size.Y * 0.35f), new Vector2((r.Size.X - 8) / 3f - 3, 3)), WyDark.WithAlpha(0.7f * a));
                break;
            }
            case WayLook.MatPlug:
            {
                var b = o + dir * h * 0.4f;
                ci.DrawColoredPolygon(new[] { b - side * h - dir * 6f, b + side * h - dir * 6f, b + side * h + dir * 6f, b - side * h + dir * 6f }, WyRubber.WithAlpha(a));
                for (int i = -3; i <= 3; i++) ci.DrawLine(b + side * (i * 4f) - dir * 5f, b + side * (i * 4f) + dir * 5f, WyRubber.Lightened(0.25f).WithAlpha(a), 1f, true);
                break;
            }
            case WayLook.GluePatch:
            {
                // 둥근 수선 패치 · 번들거리는 접착제 테두리
                var b = o + dir * h * 0.4f;
                ci.DrawCircle(b, h * 0.55f, new Color("#4a4a52").WithAlpha(a));
                ci.DrawArc(b, h * 0.6f, 0, Mathf.Tau, 20, new Color(1f, 0.95f, 0.7f, (0.35f + 0.25f * pulse) * a), 2f, true);
                ci.DrawLine(b - side * 4f, b + side * 4f, new Color("#e8c547").WithAlpha(a), 1.5f, true);
                break;
            }
            case WayLook.FrostPlug:
            {
                // 뾰족뾰족 얼음 마개 (녹으면 작아진다)
                float sz = h * (0.5f + 0.5f * Mathf.Clamp(m.Q, 0.2f, 1f));
                var b = o + dir * h * 0.3f;
                var pts = new Vector2[10];
                for (int i = 0; i < 10; i++) { float ang = i / 10f * Mathf.Tau; float r = sz * (i % 2 == 0 ? 1f : 0.55f); pts[i] = b + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r; }
                ci.DrawColoredPolygon(pts, WyFrost.WithAlpha(0.9f * a));
                ci.DrawPolyline(new[] { pts[0], pts[2], pts[4], pts[6], pts[8], pts[0] }, new Color("#9fd4f0").WithAlpha(a), 1f, true);
                if (m.Q < 0.5f) ci.DrawCircle(b + dir * sz, 2f + pulse, WyWet.WithAlpha(0.6f * a)); // 녹은 물방울
                break;
            }
            case WayLook.BackPlug:
            {
                // 등으로 막은 자리: 몸 자국 (넓은 타원) · 어깨 서리
                DrawEllipse(ci, o + dir * h * 0.4f, h * 0.9f, h * 0.45f, new Color("#7a6a5a").WithAlpha(0.55f * a));
                ci.DrawArc(o + dir * h * 0.4f, h * 0.7f, 0, Mathf.Pi, 10, WyFrost.WithAlpha(0.7f * a), 2f, true);
                break;
            }
            case WayLook.RobotBrace:
            {
                // 몸으로 막은 로봇 (네모난 몸 · 바퀴 · 눈빛)
                var b = o + dir * h * 0.55f;
                var r = new Rect2(b - new Vector2(h * 0.7f, h * 0.55f), new Vector2(h * 1.4f, h * 1.1f));
                Gfx.RoundRect(ci, r, WyMetal.Darkened(0.2f).WithAlpha(a), 4, WyDark.WithAlpha(a), 2);
                ci.DrawCircle(b, 3f, new Color(0.4f, 0.9f, 1f, (0.5f + 0.5f * pulse) * a));
                foreach (var s in new[] { -1f, 1f }) ci.DrawRect(new Rect2(b + side * h * 0.75f * s - new Vector2(3, 6), new Vector2(6, 12)), WyDark.WithAlpha(a));
                break;
            }
            case WayLook.CrawlHatch:
            {
                // 벽 속 통로: 떼어 낸 격자 덮개가 기대 서 있다 · 나사
                var r = new Rect2(o - new Vector2(h * 0.7f, h * 0.7f), new Vector2(h * 1.4f, h * 1.4f));
                ci.DrawRect(r, new Color(0.03f, 0.03f, 0.04f, 0.9f * a));
                var g = new Rect2(r.Position + new Vector2(h * 0.6f, h * 0.5f), new Vector2(h * 1.2f, h * 1.1f));
                ci.DrawRect(g, WyMetal.WithAlpha(0.85f * a), false, 1.5f);
                for (int i = 1; i < 4; i++) ci.DrawLine(g.Position + new Vector2(g.Size.X * i / 4f, 0), g.Position + new Vector2(g.Size.X * i / 4f, g.Size.Y), WyMetal.WithAlpha(0.7f * a), 1f);
                for (int i = 0; i < 3; i++) ci.DrawCircle(r.Position + new Vector2(4 + i * 5, r.Size.Y + 3), 1.2f, WyMetal.Lightened(0.3f).WithAlpha(a));
                break;
            }
            case WayLook.PriedDoor:
            {
                // 문짝 두 쪽이 비틀려 벌어지고 쇠지레가 박혀 있다 · 긁힌 자국
                var gapA = o - side * 3f; var gapB = o + side * 3f;
                ci.DrawColoredPolygon(new[] { gapA - side * h, gapA - side * h + dir * 3f, gapA + dir * 6f, gapA - dir * 4f }, new Color("#5a6470").WithAlpha(a));
                ci.DrawColoredPolygon(new[] { gapB + side * h, gapB + side * h - dir * 2f, gapB - dir * 6f, gapB + dir * 4f }, new Color("#5a6470").WithAlpha(a));
                ci.DrawLine(o - dir * h * 0.9f + side * 2f, o + dir * h * 1.1f - side * 1f, new Color("#c0392b").WithAlpha(a), 3f, true); // 쇠지레
                ci.DrawLine(o + dir * h * 1.1f - side * 1f, o + dir * h * 1.1f + side * 5f, new Color("#c0392b").WithAlpha(a), 3f, true);
                for (int i = 0; i < 3; i++) ci.DrawLine(o - side * (6 + i * 3f) - dir * 4f, o - side * (8 + i * 3f) + dir * 4f, Colors.White.WithAlpha(0.4f * a), 1f, true);
                break;
            }
            case WayLook.CutDoor or WayLook.WallHole:
            {
                // 잘라 낸 자리: 아직 붉게 달아오른 절단선 · 녹아 흐른 쇳물
                var r = new Rect2(o - new Vector2(h * 0.8f, h * 0.8f), new Vector2(h * 1.6f, h * 1.6f));
                if (m.Look == WayLook.WallHole) ci.DrawRect(r.Grow(-3f), new Color(0.02f, 0.02f, 0.03f, 0.9f * a));
                float hot = Mathf.Clamp(1f - (_world.Tick - m.Since) / (float)SimTime.Hours(1), 0f, 1f);
                ci.DrawRect(r, new Color(1f, 0.45f + 0.3f * hot, 0.15f, (0.25f + 0.6f * hot) * a), false, 2f);
                for (int i = 0; i < 4; i++) ci.DrawLine(r.Position + new Vector2(4 + i * 7, r.Size.Y), r.Position + new Vector2(4 + i * 7, r.Size.Y + 3 + i % 2 * 3), new Color("#6a5a50").WithAlpha(a), 2f, true);
                break;
            }
            case WayLook.BlownDoor:
            {
                for (int i = 0; i < 10; i++) { float ang = i / 10f * Mathf.Tau; ci.DrawLine(o, o + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * T * (0.8f + 0.3f * (i % 3)), WySoot.WithAlpha(0.5f * a), 3f, true); }
                for (int i = 0; i < 6; i++) ci.DrawRect(new Rect2(o + new Vector2(Mathf.Sin(i * 2.7f) * 18f, Mathf.Cos(i * 1.9f) * 18f), new Vector2(4, 3)), WyMetal.Darkened(0.3f).WithAlpha(a));
                break;
            }
            case WayLook.Jumper:
            {
                // 문 모터에 물린 노란 임시선 · 악어 집게
                var e = o + side * T * 1.2f + dir * 6f;
                ci.DrawPolyline(new[] { o, o + side * 10f + dir * 8f, e }, WyCable.WithAlpha(a), 2f, true);
                foreach (var p in new[] { o, e }) ci.DrawColoredPolygon(new[] { p + new Vector2(-3, -2), p + new Vector2(3, -2), p + new Vector2(0, 3) }, new Color("#dd2233").WithAlpha(a));
                break;
            }
            case WayLook.Knock:
            {
                // 두드리는 렌치 · 퍼지는 소리 고리
                ci.DrawLine(o + new Vector2(-6, 6), o + new Vector2(5, -5), WyMetal.WithAlpha(a), 3f, true);
                ci.DrawArc(o + new Vector2(6, -6), 3.5f, 0, Mathf.Pi * 1.5f, 8, WyMetal.WithAlpha(a), 2f, true);
                for (int i = 0; i < 3; i++) { float t = (_time * 1.2f + i / 3f) % 1f; ci.DrawArc(o, 6f + 22f * t, -0.9f, 0.9f, 10, Colors.White.WithAlpha(0.45f * (1 - t) * a), 1.5f, true); }
                break;
            }
            case WayLook.RobotBattery:
            {
                var r = new Rect2(o - new Vector2(10, 7), new Vector2(20, 14));
                Gfx.RoundRect(ci, r, WyMetal.Darkened(0.35f).WithAlpha(a), 3, WyDark.WithAlpha(a), 1);
                ci.DrawRect(new Rect2(o + new Vector2(-4, -6), new Vector2(8, 5)), new Color(0, 0, 0, 0.9f * a)); // 빈 배터리 칸
                ci.DrawRect(new Rect2(o + new Vector2(12, 2), new Vector2(9, 6)), new Color("#3a7a3a").WithAlpha(a)); // 뺀 배터리
                ci.DrawLine(o + new Vector2(21, 5), o + new Vector2(30, 0), WyCable.WithAlpha(a), 1.5f, true);
                break;
            }
            case WayLook.Pedal:
            {
                ci.DrawPolyline(new[] { o, o + new Vector2(10, -6), o + new Vector2(22, -4) }, new Color("#202020").WithAlpha(a), 2f, true);
                float zig = pulse;
                ci.DrawPolyline(new[] { o + new Vector2(24, -12), o + new Vector2(20, -6), o + new Vector2(25, -6), o + new Vector2(21, 0) }, WyCable.WithAlpha((0.4f + 0.6f * zig) * a), 2f, true);
                ci.DrawArc(o, 7f, _time * 6f, _time * 6f + 4.5f, 10, WyMetal.WithAlpha(a), 2f, true);
                break;
            }
            case WayLook.Lamp:
            {
                // 작업등: 세 다리 · 빛 웅덩이
                ci.DrawCircle(o, T * 1.4f, new Color(1f, 0.92f, 0.7f, 0.12f * a));
                ci.DrawCircle(o, T * 0.8f, new Color(1f, 0.92f, 0.7f, 0.12f * a));
                foreach (var s in new[] { -1f, 0f, 1f }) ci.DrawLine(o, o + new Vector2(7f * s, 9f), WyDark.WithAlpha(a), 1.5f, true);
                ci.DrawCircle(o - new Vector2(0, 2), 4f, new Color(1f, 0.95f, 0.75f, a));
                break;
            }
            case WayLook.Stretcher or WayLook.Cart:
            {
                if (m.Look == WayLook.Stretcher)
                {
                    foreach (var s in new[] { -5f, 5f }) ci.DrawLine(o + new Vector2(-16, s), o + new Vector2(16, s), WyMetal.WithAlpha(a), 2f, true);
                    ci.DrawRect(new Rect2(o + new Vector2(-12, -4), new Vector2(24, 8)), new Color("#c8b88a").WithAlpha(0.9f * a));
                }
                else
                {
                    Gfx.RoundRect(ci, new Rect2(o + new Vector2(-12, -7), new Vector2(24, 12)), new Color("#4a6a8a").WithAlpha(a), 2);
                    foreach (var s in new[] { -8f, 8f }) ci.DrawCircle(o + new Vector2(s, 7), 3f, WyDark.WithAlpha(a));
                }
                break;
            }
            case WayLook.Bandage or WayLook.Terminal:
            {
                Gfx.RoundRect(ci, new Rect2(o + new Vector2(-9, -6), new Vector2(12, 10)), new Color("#e8e8e8").WithAlpha(a), 2);
                ci.DrawRect(new Rect2(o + new Vector2(-4.5f, -4), new Vector2(2, 6)), new Color("#dd2233").WithAlpha(a));
                ci.DrawRect(new Rect2(o + new Vector2(-6.5f, -2), new Vector2(6, 2)), new Color("#dd2233").WithAlpha(a));
                ci.DrawPolyline(new[] { o + new Vector2(4, 2), o + new Vector2(10, 4), o + new Vector2(14, 0), o + new Vector2(18, 3) }, Colors.White.WithAlpha(a), 3f, true); // 풀린 붕대
                if (m.Look == WayLook.Terminal)
                {
                    Gfx.RoundRect(ci, new Rect2(o + new Vector2(-16, -16), new Vector2(12, 9)), new Color("#112233").WithAlpha(a), 2, new Color(0.4f, 0.9f, 1f, a), 1);
                    for (int i = 0; i < 3; i++) ci.DrawLine(o + new Vector2(-14, -14 + i * 2.5f), o + new Vector2(-14 + 4 + (i * 3) % 6, -14 + i * 2.5f), new Color(0.5f, 1f, 0.9f, (0.5f + 0.5f * pulse) * a), 1f);
                }
                break;
            }
            case WayLook.Stripped:
            {
                // 속이 빈 설비: 열린 점검창 · 늘어진 선 · 떼어 간 부품 자리 (점선 테두리)
                var r = new Rect2(o - new Vector2(9, 7), new Vector2(18, 14));
                ci.DrawRect(r, new Color(0.03f, 0.03f, 0.04f, 0.95f * a));
                for (int i = 0; i < 6; i++) ci.DrawLine(r.Position + new Vector2(3 + i * 3f, 3), r.Position + new Vector2(5 + i * 3f, 6), new Color("#e0c050").WithAlpha(a), 1f);
                ci.DrawPolyline(new[] { r.Position + new Vector2(3, r.Size.Y), r.Position + new Vector2(1, r.Size.Y + 6), r.Position + new Vector2(4, r.Size.Y + 10) }, new Color("#dd2233").WithAlpha(a), 1.5f, true);
                ci.DrawPolyline(new[] { r.Position + new Vector2(10, r.Size.Y), r.Position + new Vector2(12, r.Size.Y + 7) }, new Color("#3388cc").WithAlpha(a), 1.5f, true);
                ci.DrawColoredPolygon(new[] { r.End + new Vector2(-2, -4), r.End + new Vector2(6, -2), r.End + new Vector2(4, 6), r.End + new Vector2(-3, 4) }, new Color("#e8d070").WithAlpha(a)); // 꼬리표
                break;
            }
            case WayLook.Improvised:
            {
                // 테이프로 감은 임시 부품 · 숟가락 쇳조각
                for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(o + new Vector2(-8 + i * 6, -6), new Vector2(3, 12)), new Color("#c0c4c8").WithAlpha(a));
                ci.DrawLine(o + new Vector2(-10, 8), o + new Vector2(6, 8), WyMetal.Lightened(0.2f).WithAlpha(a), 2f, true);
                DrawEllipse(ci, o + new Vector2(9, 8), 3.5f, 2.2f, WyMetal.Lightened(0.2f).WithAlpha(a));
                break;
            }
            case WayLook.Shavings:
                for (int i = 0; i < 6; i++) ci.DrawArc(o + new Vector2(Mathf.Sin(i * 2.1f) * 10f, Mathf.Cos(i * 1.3f) * 7f), 2.5f, 0, Mathf.Pi * 1.6f, 8, WyMetal.Lightened(0.3f).WithAlpha(a), 1f, true);
                break;
            case WayLook.Candle or WayLook.Splitter:
            {
                if (m.Look == WayLook.Candle)
                    for (int i = 0; i < 3; i++) { var p = o + new Vector2(-8 + i * 8, 2); ci.DrawRect(new Rect2(p - new Vector2(2.5f, 0), new Vector2(5, 8)), new Color("#d8d0b8").WithAlpha(a)); ci.DrawCircle(p - new Vector2(0, 2 + pulse), 2f, new Color(1f, 0.8f, 0.3f, a)); }
                else
                {
                    Gfx.RoundRect(ci, new Rect2(o + new Vector2(-6, -8), new Vector2(12, 16)), new Color(0.7f, 0.85f, 1f, 0.4f * a), 3, WyFrost.WithAlpha(a), 1);
                    for (int i = 0; i < 4; i++) ci.DrawCircle(o + new Vector2(-2 + (i % 2) * 4, 6 - ((_time * 8f + i * 4f) % 14f)), 1.2f, Colors.White.WithAlpha(0.8f * a));
                    ci.DrawLine(o + new Vector2(-3, -8), o + new Vector2(-8, -14), new Color("#dd2233").WithAlpha(a), 1.5f); ci.DrawLine(o + new Vector2(3, -8), o + new Vector2(8, -14), WyDark.WithAlpha(a), 1.5f);
                }
                break;
            }
            case WayLook.Huddle or WayLook.Blanket:
                for (int i = 0; i < 4; i++) DrawEllipse(ci, o + new Vector2(Mathf.Sin(i * 1.6f) * 8f, Mathf.Cos(i * 1.6f) * 6f), 7f, 4.5f, (m.Look == WayLook.Blanket ? new Color("#8a6fb0") : new Color("#b07a5a")).WithAlpha(0.8f * a));
                break;
            case WayLook.RationBox or WayLook.ColdStash:
            {
                Gfx.RoundRect(ci, new Rect2(o + new Vector2(-10, -6), new Vector2(20, 12)), new Color("#8a7a4a").WithAlpha(a), 2);
                for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(o + new Vector2(-8 + i * 6, -9), new Vector2(4, 5)), new Color("#c8b070").WithAlpha(a));
                if (m.Look == WayLook.ColdStash) ci.DrawRect(new Rect2(o + new Vector2(-10, -6), new Vector2(20, 12)), WyFrost.WithAlpha(0.45f * a), false, 2f);
                break;
            }
            case WayLook.Porridge or WayLook.PotWarmer:
            {
                ci.DrawCircle(o, 9f, WyMetal.Darkened(0.2f).WithAlpha(a));
                ci.DrawCircle(o, 7f, (m.Look == WayLook.Porridge ? new Color("#d8c89a") : WyDark).WithAlpha(a));
                if (m.Look == WayLook.PotWarmer) { ci.DrawRect(new Rect2(o + new Vector2(8, 2), new Vector2(8, 6)), new Color("#3a7a3a").WithAlpha(a)); ci.DrawLine(o + new Vector2(6, 3), o + new Vector2(8, 4), WyCable.WithAlpha(a), 1.5f); }
                for (int i = 0; i < 3; i++) { float y = (_time * 9f + i * 6f) % 18f; ci.DrawLine(o + new Vector2(-4 + i * 4, -8 - y), o + new Vector2(-2 + i * 4, -12 - y), (m.Look == WayLook.PotWarmer ? new Color(1f, 0.6f, 0.3f) : Colors.White).WithAlpha(0.4f * a * (1 - y / 18f)), 1.5f, true); }
                break;
            }
            case WayLook.SignalLamp or WayLook.Radio:
            {
                if (m.Look == WayLook.SignalLamp) { ci.DrawCircle(o, 4f, new Color(1f, 0.9f, 0.4f, (Mathf.Sin(_time * 9f) > 0 ? 1f : 0.2f) * a)); for (int i = 0; i < 6; i++) { float ang = i / 6f * Mathf.Tau; ci.DrawLine(o + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 6f, o + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 11f, new Color(1f, 0.9f, 0.4f, 0.5f * pulse * a), 1f); } }
                else { Gfx.RoundRect(ci, new Rect2(o + new Vector2(-4, -6), new Vector2(8, 12)), WyDark.WithAlpha(a), 2); ci.DrawLine(o + new Vector2(2, -6), o + new Vector2(2, -13), WyDark.WithAlpha(a), 1.5f); for (int i = 0; i < 2; i++) ci.DrawArc(o + new Vector2(2, -13), 4f + 4f * i + 2f * pulse, -1f, 1f, 8, Colors.White.WithAlpha(0.4f * a), 1f, true); }
                break;
            }
            case WayLook.Runner:
                for (int i = 0; i < 4; i++) DrawEllipse(ci, o + dir * (i * 7f) + side * (i % 2 == 0 ? 3f : -3f), 2f, 3.2f, new Color(0.3f, 0.25f, 0.2f, 0.45f * a));
                break;
            case WayLook.Strap or WayLook.Wedge:
            {
                if (m.Look == WayLook.Strap)
                {
                    Gfx.RoundRect(ci, new Rect2(o + new Vector2(-10, -8), new Vector2(20, 16)), new Color("#7a6a4a").WithAlpha(0.6f * a), 2);
                    ci.DrawLine(o + new Vector2(-12, -10), o + new Vector2(12, 10), WyCable.WithAlpha(a), 2f, true);
                    ci.DrawLine(o + new Vector2(12, -10), o + new Vector2(-12, 10), WyCable.WithAlpha(a), 2f, true);
                }
                else
                {
                    ci.DrawLine(o + new Vector2(-8, 6), o + new Vector2(4, -2), WyMetal.Lightened(0.3f).WithAlpha(a), 2f, true); // 숟가락 자루
                    DrawEllipse(ci, o + new Vector2(7, -4), 4f, 2.5f, WyMetal.Lightened(0.3f).WithAlpha(a));
                }
                break;
            }
            case WayLook.TapeHose:
            {
                ci.DrawLine(o - side * h, o + side * h, WyMetal.WithAlpha(a), 6f, true); // 관
                for (int i = -1; i <= 1; i++) ci.DrawLine(o + side * (i * 5f) - dir * 4f, o + side * (i * 5f) + dir * 4f, new Color("#c8ccd0").WithAlpha(a), 3f, true); // 테이프
                ci.DrawPolyline(new[] { o, o + dir * 10f + side * 6f, o + dir * 20f - side * 2f, o + dir * 26f + side * 8f }, new Color("#2a6a3a").WithAlpha(a), 3f, true); // 호스
                break;
            }
            case WayLook.Towel or WayLook.WetCloth:
                ci.DrawColoredPolygon(new[] { o + new Vector2(-9, -5), o + new Vector2(8, -6), o + new Vector2(10, 5), o + new Vector2(-8, 6) }, WyWet.WithAlpha(0.9f * a));
                ci.DrawLine(o + new Vector2(-8, 0), o + new Vector2(9, -1), WyCloth.Lightened(0.3f).WithAlpha(a), 1.5f, true);
                ci.DrawCircle(o + new Vector2(0, 8 + 2 * pulse), 1.3f, WyWet.Lightened(0.4f).WithAlpha(a));
                break;
            case WayLook.FanDoor:
                for (int i = 0; i < 3; i++) { float t = (_time * 1.5f + i / 3f) % 1f; var p = o - dir * h + dir * T * 1.6f * t + side * (i * 6f - 6f); ci.DrawLine(p, p + dir * 6f, new Color(0.85f, 0.9f, 1f, 0.5f * a * (1 - t)), 1.5f, true); ci.DrawColoredPolygon(new[] { p + dir * 8f, p + dir * 5f + side * 2f, p + dir * 5f - side * 2f }, new Color(0.85f, 0.9f, 1f, 0.5f * a * (1 - t))); }
                break;
            case WayLook.Sawdust:
                for (int i = 0; i < 14; i++) ci.DrawCircle(o + new Vector2(Mathf.Sin(i * 2.3f) * 10f, Mathf.Cos(i * 1.7f) * 7f), 1.5f, new Color("#e8dcb8").WithAlpha(0.85f * a));
                break;
            case WayLook.Plunger:
                ci.DrawLine(o + new Vector2(0, -12), o + new Vector2(0, 2), WyWood.WithAlpha(a), 2f, true);
                DrawEllipse(ci, o + new Vector2(0, 5), 6f, 3.5f, new Color("#c0392b").WithAlpha(a));
                break;
            case WayLook.OffTag:
                ci.DrawColoredPolygon(new[] { o + new Vector2(-4, -2), o + new Vector2(4, -2), o + new Vector2(4, 9), o + new Vector2(-4, 9) }, new Color("#e8d070").WithAlpha(a));
                ci.DrawArc(o + new Vector2(0, 3), 2.5f, -2.2f, 2.2f + Mathf.Pi, 10, WyDark.WithAlpha(a), 1f, true);
                ci.DrawLine(o + new Vector2(0, -2), o + new Vector2(0, -8), WyDark.WithAlpha(a), 1f);
                break;
            case WayLook.Condense:
                ci.DrawColoredPolygon(new[] { o + new Vector2(-6, 0), o + new Vector2(6, 0), o + new Vector2(5, 10), o + new Vector2(-5, 10) }, WyMetal.WithAlpha(a));
                for (int i = 0; i < 3; i++) ci.DrawCircle(o + new Vector2(-3 + i * 3, -((_time * 10f + i * 5f) % 12f)), 1.3f, WyWet.Lightened(0.3f).WithAlpha(a));
                break;
            case WayLook.SuitShare:
                ci.DrawCircle(o, 7f, new Color("#e8e8e8").WithAlpha(a));
                ci.DrawCircle(o + new Vector2(0, 1), 4.5f, new Color("#2a3a50").WithAlpha(a));
                ci.DrawPolyline(new[] { o + new Vector2(7, 2), o + new Vector2(14, 6), o + new Vector2(20, 2) }, new Color("#6a8aa8").WithAlpha(a), 2f, true);
                break;
        }
    }

    private static void DrawEllipse(CanvasItem ci, Vector2 c, float rx, float ry, Color col)
    {
        var pts = new Vector2[14];
        for (int i = 0; i < pts.Length; i++) { float ang = i / (float)pts.Length * Mathf.Tau; pts[i] = c + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * ry); }
        ci.DrawColoredPolygon(pts, col);
    }
}
