using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 의료 3차 그림 — 수술 로봇 팔: 바닥에 박힌 둥근 기둥(노랑 · 검정 주의 띠 · 크롬 고리) · 벽으로 가는 굵은 전선관(데이터선 빛이 흐른다) ·
/// 기구 회전대(메스 · 집게 · 카메라 · 흡인관) · 작은 조작 화면 · 세 마디 팔과 기구 머리.
/// 상태: 쉼(팔을 접어 세우고 푸른 숨 불빛) · 거듦(수술대 가장자리로 뻗어 카메라 불빛을 비춘다) ·
/// 집도(수술대 한가운데로 뻗어 끝이 잘게 · 빠르게 움직이고 초록 조준선) · 멈춤(팔이 축 늘어지고 붉게 깜빡) · 어긋남(조준 십자가 흔들린다).
/// 겪은 수술만큼 기둥 둘레에 새긴 눈금이 는다.
/// </summary>
public static partial class FixtureArt
{
    private static void MedArt3(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.SurgicalArm] = new(ArmBody, ArmLife, ArmFine, Look.Jam, 0.5f, 0.5f);
    }

    private static readonly Color ArmWhite = new("#eef2f6"), ArmJoint = new("#2b3442"), ArmHazard = new("#f2c230"), ArmLaser = new("#5dff9a"),
        ArmCam = new("#bfe8ff"), ArmData = new("#5fb8ff"), ArmRed = new("#ff4d4d"), ArmBlade = new("#dfe7ef");

    private static void ArmBody(in Fix x)
    {
        var ci = x.Ci;
        var c = x.P(0.5f, 0.5f);
        float r = x.Px(9.5f);
        // 전선관: 기둥에서 벽 쪽(앞의 반대)으로
        var back = c - x.Front * x.Px(13f);
        Pipe(ci, c, back, x.Px(3.2f), ArmJoint, false);
        // 바닥판 · 주의 띠 (노랑 · 검정 번갈아)
        Dot(ci, c, r + x.Px(1.6f), Rubber);
        for (int k = 0; k < 12; k++)
        {
            float a0 = k * Mathf.Tau / 12f;
            ci.DrawArc(c, r + x.Px(0.6f), a0, a0 + Mathf.Tau / 12f, 4, k % 2 == 0 ? ArmHazard : Rubber, x.Px(1.6f), true);
        }
        Dot(ci, c, r - x.Px(0.4f), Steel3);
        Ring(ci, c, r - x.Px(1.6f), Chrome, x.Px(1f));
        Dot(ci, c, x.Px(5.4f), ArmWhite); // 기둥 머리
        Ring(ci, c, x.Px(5.4f), Steel4, x.Px(0.8f));
        // 기구 회전대 (기둥 옆 작은 원판 · 기구 넷)
        var tray = x.P(0.84f, 0.2f);
        Dot(ci, tray, x.Px(3.4f), Steel3);
        Ring(ci, tray, x.Px(3.4f), Chrome, x.Px(0.6f));
        for (int k = 0; k < 4; k++)
        {
            var d = new Vector2(Mathf.Cos(k * Mathf.Pi / 2f + 0.4f), Mathf.Sin(k * Mathf.Pi / 2f + 0.4f));
            Line(ci, tray, tray + d * x.Px(2.6f), k == 0 ? ArmBlade : k == 1 ? Chrome : k == 2 ? ArmCam : Steel4, x.Px(0.7f));
        }
        // 조작 화면
        Box(ci, x.Q(0.08f, 0.76f, 0.34f, 0.96f), new Color("#0c1a20"), 1.5f, Steel4);
        if (x.Tier >= 2) Ring(ci, c, r - x.Px(3f), Chrome.WithAlpha(0.6f), x.Px(0.6f)); // II: 둘째 고리 (더 단단한 받침)
        if (x.Tier >= 3) Dot(ci, x.P(0.16f, 0.2f), x.Px(1.8f), new Color("#38485c")); // III: 보조 연산기
    }

    private static void ArmLife(in Fix x)
    {
        var ci = x.Ci;
        var w = x.W;
        var c = x.P(0.5f, 0.5f);
        int mode = 0;
        Vector2 target = c + x.Front * x.Px(10f);
        float calib = 1f;
        int ops = 0;
        if (w != null)
        {
            var (m, at) = w.SurgArm.PoseOf(x.F);
            mode = m;
            if (at is System.Numerics.Vector2 p) target = ShipView.ToPx(p);
            calib = w.SurgArm.Calib(x.F);
            ops = w.SurgArm.Ops;
        }
        else mode = x.St == State.Running ? 1 : 0;
        bool on = mode is 1 or 2;
        // 데이터선 빛 (전선관을 따라 흐른다)
        if (w != null && x.F.Room.DataLinked && mode != 3 && x.Lod > 0)
        {
            var back = c - x.Front * x.Px(13f);
            for (int k = 0; k < 3; k++)
            {
                float u = Mathf.PosMod(x.T * (on ? 0.9f : 0.3f) + k / 3f, 1f);
                Dot(ci, back.Lerp(c, u), x.Px(0.7f), ArmData.WithAlpha(0.8f));
            }
        }
        // 세 마디 팔: 어깨(기둥) → 팔꿈치 → 손목 → 기구 끝
        var dir = target - c;
        float reach = dir.Length();
        if (reach < 0.01f) dir = x.Front; else dir /= reach;
        var side = dir.Orthogonal();
        Vector2 elbow, wrist, tip;
        if (mode == 0) // 접어 세움 — 기둥 위에 웅크린다
        {
            float br = 0.08f * Mathf.Sin(x.T * 0.8f);
            elbow = c + (dir * 0.5f + side * 0.7f).Normalized() * x.Px(7f);
            wrist = elbow + (-dir * 0.2f + side * -0.9f + side * br).Normalized() * x.Px(5f);
            tip = wrist + (-side + dir * 0.3f).Normalized() * x.Px(3f);
        }
        else if (mode == 3) // 멈춤 — 축 늘어졌다
        {
            var hang = target - c;
            float len = Mathf.Min(hang.Length() * 0.5f, x.Px(14f));
            elbow = c + (dir + side * 0.5f).Normalized() * x.Px(8f);
            wrist = elbow + (dir * 0.4f + side * 0.9f).Normalized() * x.Px(6f);
            tip = wrist + (side + dir * 0.2f).Normalized() * Mathf.Max(x.Px(3f), len * 0.25f);
        }
        else
        {
            // 거듦: 수술대 가장자리 · 집도: 한가운데까지 (끝이 잘게 움직인다)
            float to = mode == 2 ? 1f : 0.7f;
            var goal = c + dir * reach * to;
            float jit = mode == 2 ? x.Px(1.4f) : x.Px(0.5f);
            float sp = mode == 2 ? 5.3f : 1.6f;
            goal += new Vector2(Mathf.Sin(x.T * sp), Mathf.Sin(x.T * sp * 1.7f + 1f)) * jit * (0.6f + 0.4f * (1f - calib) * 3f);
            var mid = c.Lerp(goal, 0.45f) + side * x.Px(mode == 2 ? 6f : 8f);
            elbow = mid;
            wrist = mid.Lerp(goal, 0.65f) + side * x.Px(2f);
            tip = goal;
        }
        var seg = mode == 3 ? Steel4 : ArmWhite;
        float wd = x.Px(2.6f);
        Line(ci, c, elbow, ArmJoint, wd + x.Px(1.2f)); Line(ci, c, elbow, seg, wd);
        Line(ci, elbow, wrist, ArmJoint, wd + x.Px(0.8f)); Line(ci, elbow, wrist, seg, wd * 0.8f);
        Line(ci, wrist, tip, ArmJoint, x.Px(1.8f)); Line(ci, wrist, tip, ArmBlade, x.Px(1f));
        Dot(ci, elbow, x.Px(2.2f), ArmJoint); Dot(ci, elbow, x.Px(1.1f), Chrome);
        Dot(ci, wrist, x.Px(1.7f), ArmJoint); Dot(ci, wrist, x.Px(0.8f), Chrome);
        Dot(ci, c, x.Px(2.8f), ArmJoint); Dot(ci, c, x.Px(1.3f), on ? Good : Chrome);
        // 기구 머리
        if (mode == 1) // 카메라 불빛 원뿔
        {
            var cone = new[] { tip, tip + (dir + side * 0.35f) * x.Px(9f), tip + (dir - side * 0.35f) * x.Px(9f) };
            ci.DrawColoredPolygon(cone, ArmCam.WithAlpha(0.12f + 0.06f * Pulse(x.T, 2f)));
            Dot(ci, tip, x.Px(1.4f), ArmCam);
        }
        else if (mode == 2) // 초록 조준선 · 칼끝 반짝
        {
            Line(ci, tip, tip + dir * x.Px(5f), ArmLaser.WithAlpha(0.55f + 0.35f * Pulse(x.T, 9f)), x.Px(0.5f));
            Dot(ci, tip, x.Px(1.1f), Colors.White.WithAlpha(0.6f + 0.4f * Pulse(x.T, 13f)));
        }
        // 수술 로봇 기술: 끝에 두 손가락 미세 집게 (집도할 때 오므렸다 편다)
        if (w != null && w.Eras.Has("surgbot") && mode != 3)
        {
            float open = mode == 2 ? 0.35f + 0.25f * Mathf.Sin(x.T * 6f) : 0.5f;
            Line(ci, tip, tip + (dir + side * open).Normalized() * x.Px(2.2f), Chrome, x.Px(0.6f));
            Line(ci, tip, tip + (dir - side * open).Normalized() * x.Px(2.2f), Chrome, x.Px(0.6f));
        }
        // 어긋남: 조준 십자가 흔들린다
        if (calib < 0.6f && mode != 3 && x.Lod > 0)
        {
            var cr = tip + new Vector2(Mathf.Sin(x.T * 7f), Mathf.Cos(x.T * 5f)) * x.Px(2f) * (1f - calib);
            Line(ci, cr - new Vector2(x.Px(2f), 0), cr + new Vector2(x.Px(2f), 0), Amber.WithAlpha(0.8f), x.Px(0.5f));
            Line(ci, cr - new Vector2(0, x.Px(2f)), cr + new Vector2(0, x.Px(2f)), Amber.WithAlpha(0.8f), x.Px(0.5f));
        }
        // 상태 불빛 · 화면
        var led = x.P(0.21f, 0.86f);
        if (mode == 3) Led(ci, x.P(0.5f, 0.5f) + x.Front * x.Px(9.5f), ArmRed, Pulse(x.T, 7f) > 0.5f ? 1f : 0.15f, x.Px(1.5f));
        var scr = x.Q(0.1f, 0.79f, 0.32f, 0.93f);
        if (mode == 2) // 심전도처럼 뛰는 선
        {
            float y0 = scr.GetCenter().Y;
            Vector2? prev = null;
            for (int i = 0; i <= 10; i++)
            {
                float u = i / 10f;
                float ph = Mathf.PosMod(u * 2f - x.T * 0.8f, 1f);
                float y = y0 - (ph > 0.45f && ph < 0.52f ? scr.Size.Y * 0.4f : 0f);
                var p = new Vector2(scr.Position.X + u * scr.Size.X, y);
                if (prev is Vector2 pp) Line(ci, pp, p, ArmLaser, x.Px(0.5f));
                prev = p;
            }
        }
        else if (mode == 1) ci.DrawRect(new Rect2(scr.Position, new Vector2(scr.Size.X * (0.5f + 0.5f * Pulse(x.T, 1.2f)), x.Px(0.8f))), ArmCam.WithAlpha(0.7f));
        else if (mode == 0) Led(ci, led, ArmData, 0.25f + 0.35f * Pulse(x.T, 1.1f), x.Px(1.1f));
        // 겪은 수술 — 기둥 둘레 눈금
        int n = Mathf.Min(ops, 16);
        for (int k = 0; k < n; k++)
        {
            float a = -Mathf.Pi / 2f + k * Mathf.Tau / 16f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Line(ci, c + d * x.Px(6.2f), c + d * x.Px(7.2f), Good.WithAlpha(0.7f), x.Px(0.6f));
        }
    }

    private static void ArmFine(in Fix x)
    {
        var ci = x.Ci;
        var c = x.P(0.5f, 0.5f);
        for (int k = 0; k < 6; k++) // 바닥 볼트 여섯
        {
            float a = k * Mathf.Tau / 6f + 0.26f;
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * x.Px(8.6f);
            Dot(ci, p, x.Px(0.7f), Chrome);
            Dot(ci, p, x.Px(0.3f), Steel3);
        }
        // 조작 화면 아래 비상 정지 단추 · 수동 해제 손잡이
        Dot(ci, x.P(0.4f, 0.9f), x.Px(1.2f), ArmRed);
        Ring(ci, x.P(0.4f, 0.9f), x.Px(1.6f), ArmHazard, x.Px(0.5f));
        Line(ci, x.P(0.62f, 0.86f), x.P(0.74f, 0.94f), Chrome, x.Px(0.9f));
        // 전선관 이음쇠
        var back = c - x.Front * x.Px(11f);
        Ring(ci, back, x.Px(2.2f), Chrome, x.Px(0.6f));
    }
}
