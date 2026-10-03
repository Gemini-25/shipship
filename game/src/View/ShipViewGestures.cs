using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.2 몸짓 · 버릇: 인형 위에 그 순간의 손 · 시선 · 자세를 덧그린다 (읽기만 한다).
//  몸짓마다 다른 그림: 바닥에 내려놓은 짐 상자 · 문을 잡은 손바닥 · 내민 잔과 받친 손 · 흘린 잔과 튄 방울 · 든 작업등과 빛 부채 ·
//   고개만 돌린 눈길 · 공구를 허리에 꽂는 손 · 쓰러진 사람 어깨에 얹은 손과 맥박선 · 계기 눈금/선배 얼굴로 가는 점선 · 끄덕임 ·
//   다른 손으로 옮겨 쥔 공구와 휘어진 화살 · 짚은 자리의 분필 동그라미 · 이어 가는 말꼬리 · 음표 · 겹친 음표 · 쳐다보며 드는 손.
//  버릇마다 다른 그림: 도는 펜 · 떨리는 발 · 입에 댄 손 · 오므린 입과 물결 · 머리를 쓰는 손 · 맞댄 주먹과 불똥 · 톡톡 점 · 목 뒤 손 ·
//   깃을 당기는 손 · 깨문 입술 · 기지개. 멀리선(lod 0) 그리지 않는다.
public partial class ShipView
{
    private static readonly Color GsHand = new("#e8c4a0");
    private static readonly Color GsChalk = new(0.95f, 0.95f, 0.9f, 0.85f);
    private static readonly Color GsLamp = new(1f, 0.9f, 0.55f);
    private static readonly Color GsCoffee = new("#6b3f22");
    private static readonly Color GsNote = new(0.95f, 0.88f, 0.55f, 0.95f);
    private static readonly Color GsGaze = new(0.85f, 0.95f, 1f, 0.55f);

    private void PaintGestures(CanvasItem ci, CrewMember c, Vector2 body, Vector2 facing, float rr, float s, int lod)
    {
        if (GestureSystem.Off || lod == 0 || _world.Gestures.Peek(c) is not MannerState st) return;
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        var f = facing.LengthSquared() > 1e-4f ? facing.Normalized() : Vector2.Right;
        var sd = new Vector2(-f.Y, f.X);
        float t = _time + c.Id * 0.53f;
        var head = body + f * rr * 0.15f;
        long tick = _world.Tick;
        var m = _world.Gestures.MienOf(c);
        float age = st.MUntil > st.MSince ? Mathf.Clamp((tick - st.MSince) / (float)(st.MUntil - st.MSince), 0f, 1f) : 0f;
        Vector2? gaze = _world.Gestures.GazeOf(c) is System.Numerics.Vector2 g ? ToPx(g) : null;
        Vector2? spot = st.Spot is System.Numerics.Vector2 sp ? ToPx(sp) : null;
        int hs = st.LeftHanded ? -1 : 1; // 잘 쓰는 손 쪽

        switch (m)
        {
            case Mien.SetDown:
            {
                // 바닥에 내려놓은 짐 상자 (널빤지 무늬 · 모서리) + 숙인 두 팔
                var bp = spot ?? body + f * rr * 1.1f;
                var bx = new Rect2(bp - new Vector2(4.2f, 3.4f) * s, new Vector2(8.4f, 6.8f) * s);
                ci.DrawRect(bx, new Color("#9c7a4c"));
                ci.DrawRect(bx, new Color("#5e4526"), false, 1f * s);
                ci.DrawLine(bx.Position + new Vector2(0, bx.Size.Y * 0.5f), bx.Position + new Vector2(bx.Size.X, bx.Size.Y * 0.5f), new Color("#5e4526"), 0.8f * s);
                var cl = RoleCloth(c.Role).Darkened(0.2f);
                ci.DrawLine(body + sd * rr * 0.6f, bp + sd * 3f * s, cl, 2.2f * s, true);
                ci.DrawLine(body - sd * rr * 0.6f, bp - sd * 3f * s, cl, 2.2f * s, true);
                if (age > 0.4f) ci.DrawCircle(head + f * rr * 0.9f, 1.2f * s, GsHand, true, -1f, true); // 버튼을 누르는 손
                break;
            }
            case Mien.AskDoor:
            {
                // 턱으로 문을 가리키며 말한다: 턱 끝 호 + 말 물결 (문 쪽)
                var to = gaze is Vector2 gz ? (gz - head).Normalized() : f;
                for (int i = 0; i < 2; i++)
                    ci.DrawArc(head + to * (rr * 0.7f + i * 3f * s), (2f + i * 1.6f) * s, to.Angle() - 0.7f, to.Angle() + 0.7f, 8, new Color(1, 1, 1, 0.7f - i * 0.25f), 1.1f * s, true);
                ci.DrawArc(head + to * rr * 0.35f, rr * 0.35f, to.Angle() - 0.4f, to.Angle() + 0.4f, 6, GsHand.Darkened(0.3f), 1.2f * s, true);
                break;
            }
            case Mien.HoldDoor:
            {
                // 팔을 뻗어 문을 잡은 손바닥 (손가락 셋)
                var to = gaze is Vector2 gz ? gz : head + f * rr * 2f;
                var dir = (to - body).Normalized();
                var palm = body + dir * Mathf.Min((to - body).Length() - 2f * s, rr * 1.8f);
                ci.DrawLine(body + sd * rr * 0.5f * hs, palm, RoleCloth(c.Role).Darkened(0.15f), 2.4f * s, true);
                ci.DrawCircle(palm, 1.8f * s, GsHand, true, -1f, true);
                var pn = new Vector2(-dir.Y, dir.X);
                for (int i = -1; i <= 1; i++) ci.DrawLine(palm + pn * i * 1.1f * s, palm + pn * i * 1.1f * s + dir * 2.2f * s, GsHand.Darkened(0.1f), 0.9f * s, true);
                break;
            }
            case Mien.CupSteady:
            {
                // 잔을 앞으로 쭉 내밀고 다른 손으로 받친다 (출렁 호)
                var cp = body + f * rr * 1.4f + sd * rr * 0.2f * hs;
                PaintMug(ci, cp, s, f, 0f);
                ci.DrawCircle(cp - f * 1.6f * s, 1.3f * s, GsHand, true, -1f, true);
                float k = Mathf.Sin(t * 22f) * 0.35f;
                ci.DrawArc(cp, 3.6f * s, f.Angle() - 1.2f + k, f.Angle() + 1.2f + k, 8, new Color(1, 1, 1, 0.5f), 0.8f * s, true);
                break;
            }
            case Mien.CupSpill:
            {
                // 기울어진 잔 · 튄 방울 셋 · 바닥 웅덩이
                var cp = body + f * rr * 1.2f + sd * rr * 0.3f * hs;
                PaintMug(ci, cp, s, f, 0.9f);
                for (int i = 0; i < 3; i++)
                {
                    var dp = cp + f * (2.5f + i * 1.8f + age * 4f) * s + sd * (i - 1) * 2.2f * s;
                    ci.DrawCircle(dp, (1f - i * 0.2f) * s, GsCoffee.Lightened(0.15f), true, -1f, true);
                }
                if (spot is Vector2 pd)
                {
                    ci.DrawSetTransform(pd, 0f, new Vector2(1f, 0.55f));
                    ci.DrawCircle(Vector2.Zero, 5.5f * s * (0.6f + 0.4f * age), GsCoffee.WithAlpha(0.55f), true, -1f, true);
                    ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
                break;
            }
            case Mien.LampCarry:
            {
                // 두 손에 든 작업등 (몸체 · 손잡이) + 빛 부채 + 옛 자리에서 끌어온 점선
                var lp = body + f * rr * 1.1f;
                ci.DrawRect(new Rect2(lp - new Vector2(2.4f, 1.8f) * s, new Vector2(4.8f, 3.6f) * s), new Color("#d7a52a"));
                ci.DrawArc(lp - f * 2f * s, 2.2f * s, f.Angle() + 2.2f, f.Angle() + 4.1f, 8, new Color("#3b3b3b"), 1f * s, true);
                var pts = new[] { lp, lp + f.Rotated(-0.45f) * rr * 2.4f, lp + f.Rotated(0.45f) * rr * 2.4f };
                ci.DrawColoredPolygon(pts, GsLamp.WithAlpha(0.22f + 0.05f * Mathf.Sin(t * 3f)));
                ci.DrawCircle(lp + sd * 2.6f * s, 1.2f * s, GsHand, true, -1f, true);
                ci.DrawCircle(lp - sd * 2.6f * s, 1.2f * s, GsHand, true, -1f, true);
                if (spot is Vector2 old) PaintDots(ci, old, lp, GsLamp.WithAlpha(0.5f), s);
                break;
            }
            case Mien.HeadTurn:
            {
                // 몸은 그대로 · 고개만 돌린다: 돌린 쪽 코끝 선 + 두 눈
                var to = gaze is Vector2 gz ? (gz - head).Normalized() : sd;
                var nose = head + to * rr * 0.55f;
                ci.DrawLine(head + to * rr * 0.25f, nose, GsHand.Darkened(0.35f), 1.2f * s, true);
                var en = new Vector2(-to.Y, to.X);
                ci.DrawCircle(head + to * rr * 0.32f + en * 1.4f * s, 0.7f * s, new Color("#1d1817"), true, -1f, true);
                ci.DrawCircle(head + to * rr * 0.32f - en * 1.4f * s, 0.7f * s, new Color("#1d1817"), true, -1f, true);
                break;
            }
            case Mien.WrapUp:
            {
                // 하던 걸 정리: 손이 공구를 허리춤에 꽂는다 (스패너 모양이 허리로 미끄러진다)
                var from = body + f * rr * 1.1f;
                var belt = body + sd * rr * 0.75f * hs - f * rr * 0.1f;
                float k = Mathf.Clamp((tick - st.WrapFrom) / (float)System.Math.Max(1L, st.WrapUntil - st.WrapFrom), 0f, 1f);
                var tp = from.Lerp(belt, k);
                ci.DrawLine(body + sd * rr * 0.5f * hs, tp, RoleCloth(c.Role).Darkened(0.15f), 2.2f * s, true);
                PaintWrench(ci, tp, (belt - from).Normalized(), s);
                ci.DrawRect(new Rect2(belt - new Vector2(2f, 1.2f) * s, new Vector2(4f, 2.4f) * s), new Color("#4a3a2a"), false, 0.9f * s);
                break;
            }
            case Mien.KneelBy:
            {
                // 쓰러진 사람 어깨에 얹은 손 + 머리 위 맥박선 (뛰는 선)
                if (spot is Vector2 dp)
                {
                    ci.DrawLine(body + sd * rr * 0.5f * hs, dp - (dp - body).Normalized() * 3f * s, RoleCloth(c.Role).Darkened(0.15f), 2.2f * s, true);
                    ci.DrawCircle(dp - (dp - body).Normalized() * 3f * s, 1.6f * s, GsHand, true, -1f, true);
                    var p0 = dp + new Vector2(-6f, -9f) * s;
                    float ph = Mathf.PosMod(t * 1.6f, 1f);
                    var line = new[] { p0, p0 + new Vector2(3f, 0) * s, p0 + new Vector2(4.2f, -3f) * s, p0 + new Vector2(5.4f, 2.5f) * s, p0 + new Vector2(6.6f, 0) * s, p0 + new Vector2(12f, 0) * s };
                    ci.DrawPolyline(line, new Color(1f, 0.45f, 0.45f, 0.4f + 0.5f * (1f - ph)), 1.1f * s, true);
                }
                break;
            }
            case Mien.GlanceGauge:
            {
                // 계기를 본다: 점선 눈길 + 작은 눈금판 (바늘이 떤다)
                var to = gaze ?? head + f * rr * 1.5f;
                PaintDots(ci, head + f * rr * 0.4f, to, GsGaze, s);
                ci.DrawCircle(to, 3f * s, new Color(0.1f, 0.12f, 0.14f, 0.85f), true, -1f, true);
                ci.DrawArc(to, 3f * s, 0f, Mathf.Tau, 14, new Color("#c8d4dc"), 0.8f * s, true);
                float na = -2.4f + 1.4f + Mathf.Sin(t * 9f) * 0.3f;
                ci.DrawLine(to, to + Vector2.FromAngle(na) * 2.4f * s, new Color("#ff8a3a"), 0.8f * s, true);
                break;
            }
            case Mien.GlanceSenior:
            {
                // 선배 얼굴을 본다: 점선 눈길이 그 사람 머리로 + 눈썹 올림
                if (gaze is Vector2 to)
                {
                    var d = (to - head).Normalized();
                    PaintDots(ci, head + d * rr * 0.4f, to - d * rr * 0.6f, GsGaze, s);
                    var en = new Vector2(-d.Y, d.X);
                    ci.DrawArc(head + d * rr * 0.3f + en * 1.5f * s, 1.3f * s, d.Angle() - 2.4f, d.Angle() - 0.8f, 5, new Color("#3b2a20"), 0.8f * s, true);
                    ci.DrawArc(head + d * rr * 0.3f - en * 1.5f * s, 1.3f * s, d.Angle() + 0.8f, d.Angle() + 2.4f, 5, new Color("#3b2a20"), 0.8f * s, true);
                }
                break;
            }
            case Mien.Nod:
            {
                // 끄덕임: 머리 앞 위아래 짧은 호 둘 (움직임)
                float k = Mathf.Sin(t * 10f);
                var p = head + f * rr * (0.6f + 0.15f * k);
                ci.DrawArc(p, 2.4f * s, f.Angle() - 0.8f, f.Angle() + 0.8f, 6, new Color(1, 1, 1, 0.75f), 1f * s, true);
                ci.DrawArc(p + f * 2f * s, 2.4f * s, f.Angle() - 0.8f, f.Angle() + 0.8f, 6, new Color(1, 1, 1, 0.4f), 1f * s, true);
                break;
            }
            case Mien.SwapGrip:
            {
                // 다친 손 대신 다른 손에 쥔 공구 + 몸 앞을 넘어가는 휘어진 화살
                var oh = body + f * rr * 0.9f - sd * rr * 0.7f * hs;
                PaintWrench(ci, oh, f, s);
                var a0 = body + f * rr * 0.9f + sd * rr * 0.7f * hs;
                var mid = body + f * rr * 1.6f;
                ci.DrawPolyline(new[] { a0, a0.Lerp(mid, 0.5f) + f * 1.5f * s, mid, mid.Lerp(oh, 0.5f) + f * 1.5f * s, oh + sd * 2f * s * hs }, new Color(1f, 0.85f, 0.4f, 0.8f), 1f * s, true);
                var tip = oh + sd * 2f * s * hs;
                var back = (mid - oh).Normalized();
                ci.DrawLine(tip, tip + back.Rotated(0.6f) * 2f * s, new Color(1f, 0.85f, 0.4f, 0.8f), 1f * s, true);
                ci.DrawLine(tip, tip + back.Rotated(-0.6f) * 2f * s, new Color(1f, 0.85f, 0.4f, 0.8f), 1f * s, true);
                break;
            }
            case Mien.KnownSpot:
            {
                // 짚은 자리에 분필 동그라미 (점선) + 바로 그곳을 가리키는 검지
                if (spot is Vector2 kp)
                {
                    int n = 10;
                    for (int i = 0; i < n; i += 2)
                        ci.DrawArc(kp, 4.2f * s, i * Mathf.Tau / n + t * 0.4f, (i + 1) * Mathf.Tau / n + t * 0.4f, 3, GsChalk, 1f * s, true);
                    var d = (kp - body).Normalized();
                    var fp = kp - d * 5f * s;
                    ci.DrawLine(body + sd * rr * 0.5f * hs, fp, RoleCloth(c.Role).Darkened(0.15f), 2f * s, true);
                    ci.DrawLine(fp, fp + d * 2.4f * s, GsHand, 1f * s, true);
                }
                break;
            }
            case Mien.Resume:
            {
                // 이어 가는 말꼬리: 말풍선 꼬리에서 뒤로 말린 화살 + 점 셋
                var bp = head - sd * rr * 1.1f - f * rr * 0.6f;
                ci.DrawArc(bp, 3.6f * s, 0f, Mathf.Tau, 14, new Color(1, 1, 1, 0.8f), 1f * s, true);
                for (int i = -1; i <= 1; i++) ci.DrawCircle(bp + new Vector2(i * 1.4f * s, 0), 0.55f * s, new Color(1, 1, 1, 0.9f), true, -1f, true);
                ci.DrawArc(bp + new Vector2(4.6f, 2.2f) * s, 2f * s, 0.2f, 3.6f, 8, new Color(0.7f, 0.9f, 1f, 0.8f), 0.9f * s, true);
                break;
            }
            case Mien.Hum:
                PaintNote(ci, head + new Vector2(2f, -8f - Mathf.PosMod(t * 4f, 6f)) * s, s, GsNote, false);
                break;
            case Mien.SingAlong:
            {
                // 겹친 음표 (따라 부름) + 함께 부르는 사람 쪽으로 이어진 호
                PaintNote(ci, head + new Vector2(-2f, -8f - Mathf.PosMod(t * 4f, 6f)) * s, s, GsNote, true);
                if (gaze is Vector2 to)
                {
                    var mid = (head + to) * 0.5f + new Vector2(0, -8f * s);
                    ci.DrawPolyline(new[] { head + new Vector2(0, -6f * s), mid, to + new Vector2(0, -6f * s) }, GsNote.WithAlpha(0.35f), 0.9f * s, true);
                }
                break;
            }
            case Mien.Notice:
            {
                // 알아챔: 그 사람 쪽으로 펼친 손 + 올린 눈썹 (걱정)
                var to = gaze is Vector2 gz ? (gz - head).Normalized() : f;
                var hp = body + to * rr * 1.2f;
                ci.DrawLine(body + sd * rr * 0.5f * hs, hp, RoleCloth(c.Role).Darkened(0.15f), 2f * s, true);
                ci.DrawCircle(hp, 1.5f * s, GsHand, true, -1f, true);
                var en = new Vector2(-to.Y, to.X);
                ci.DrawLine(head + to * rr * 0.3f + en * 2.2f * s, head + to * rr * 0.4f + en * 0.6f * s, new Color("#3b2a20"), 0.9f * s, true);
                ci.DrawLine(head + to * rr * 0.3f - en * 2.2f * s, head + to * rr * 0.4f - en * 0.6f * s, new Color("#3b2a20"), 0.9f * s, true);
                break;
            }
        }

        // ── 버릇 ──
        if (st.Current(tick) is Tic tic && m is not (Mien.KneelBy or Mien.LampCarry or Mien.SetDown or Mien.CupSpill))
            PaintTic(ci, c, tic, st.StressTic == tic, body, head, f, sd, rr, s, t, hs);
    }

    private void PaintTic(CanvasItem ci, CrewMember c, Tic tic, bool stressed, Vector2 body, Vector2 head, Vector2 f, Vector2 sd, float rr, float s, float t, int hs)
    {
        var cl = RoleCloth(c.Role).Darkened(0.15f);
        var hand = stressed ? GsHand.Darkened(0.08f) : GsHand;
        switch (tic)
        {
            case Tic.PenSpin:
            {
                // 손가락 위에서 도는 펜
                var hp = body + f * rr * 0.8f + sd * rr * 0.6f * hs;
                ci.DrawCircle(hp, 1.3f * s, hand, true, -1f, true);
                var a = Vector2.FromAngle(t * 9f);
                ci.DrawLine(hp - a * 2.6f * s, hp + a * 2.6f * s, new Color("#2f5fb0"), 0.9f * s, true);
                ci.DrawCircle(hp + a * 2.6f * s, 0.45f * s, new Color("#d0d0d0"), true, -1f, true);
                break;
            }
            case Tic.LegBounce:
            {
                // 다리 떨기: 발끝에서 위아래로 떨리는 짧은 선 셋
                var fp = body - f * rr * 0.2f + sd * rr * 0.45f;
                float k = Mathf.Sin(t * 28f) * 1.2f * s;
                for (int i = 0; i < 3; i++) ci.DrawLine(fp + f * (i * 1.4f) * s + new Vector2(0, k), fp + f * (i * 1.4f) * s + new Vector2(0, k + 1.6f * s), new Color(1, 1, 1, 0.55f), 0.8f * s, true);
                ci.DrawCircle(fp + new Vector2(0, k), 1.2f * s, new Color("#2a2a2a"), true, -1f, true);
                break;
            }
            case Tic.NailBite:
            {
                // 입에 댄 손 + 깨문 손톱 초승달 (불안)
                var mp = head + f * rr * 0.5f;
                ci.DrawLine(body + sd * rr * 0.6f * hs, mp, cl, 2.2f * s, true);
                ci.DrawCircle(mp, 1.5f * s, hand, true, -1f, true);
                ci.DrawArc(mp + f * 1.2f * s, 1f * s, f.Angle() - 1.2f, f.Angle() + 1.2f, 5, new Color("#f0f0f0"), 0.7f * s, true);
                ci.DrawArc(head, rr * 0.75f, -1.2f + Mathf.Sin(t * 6f) * 0.2f, -0.6f, 5, new Color(1f, 0.5f, 0.4f, 0.5f), 0.8f * s, true);
                break;
            }
            case Tic.Whistle:
            {
                // 오므린 입 (작은 동그라미) + 흘러나가는 물결선
                var mp = head + f * rr * 0.55f;
                ci.DrawCircle(mp, 0.9f * s, new Color("#7a3a32"), false, 0.6f * s, true);
                var pts = new Vector2[8];
                for (int i = 0; i < 8; i++) pts[i] = mp + f * (1.5f + i * 1.3f) * s + sd * Mathf.Sin(t * 8f + i * 1.1f) * 1.2f * s;
                ci.DrawPolyline(pts, new Color(0.8f, 0.95f, 1f, 0.6f), 0.8f * s, true);
                break;
            }
            case Tic.HairTouch:
            {
                // 머리를 쓰는 손 + 손가락 사이 말린 머리칼 (스트레스면 쥐어뜯는다)
                var hp = head - f * rr * 0.1f + sd * rr * 0.55f * hs;
                ci.DrawLine(body + sd * rr * 0.6f * hs, hp, cl, 2.2f * s, true);
                ci.DrawCircle(hp, 1.6f * s, hand, true, -1f, true);
                ci.DrawArc(hp + sd * 1.4f * s * hs, 1.6f * s, t * 3f, t * 3f + 4.2f, 8, new Color("#3b2a20"), 0.8f * s, true);
                if (stressed) for (int i = 0; i < 2; i++) ci.DrawLine(hp + new Vector2(-1.5f + i * 3f, -2f) * s, hp + new Vector2(-2f + i * 4f, -3.6f) * s, new Color(1f, 0.5f, 0.4f, 0.6f), 0.7f * s, true);
                break;
            }
            case Tic.KnuckleCrack:
            {
                // 맞댄 두 주먹 + 딱 하는 불똥 선
                var hp = body + f * rr * 0.95f;
                ci.DrawCircle(hp + sd * 1.3f * s, 1.5f * s, hand, true, -1f, true);
                ci.DrawCircle(hp - sd * 1.3f * s, 1.5f * s, hand.Darkened(0.1f), true, -1f, true);
                if (Mathf.PosMod(t * 2f, 1f) < 0.3f)
                    for (int i = 0; i < 4; i++) { var d = Vector2.FromAngle(i * Mathf.Pi / 2f + 0.4f); ci.DrawLine(hp + d * 2.4f * s, hp + d * 3.6f * s, new Color(1f, 1f, 0.8f, 0.8f), 0.7f * s, true); }
                break;
            }
            case Tic.Hum:
                PaintNote(ci, head + new Vector2(3f, -8f - Mathf.PosMod(t * 3f, 6f)) * s, s, GsNote.WithAlpha(0.8f), false);
                break;
            case Tic.TapFingers:
            {
                // 손가락 톡톡: 손 곁의 점 셋이 차례로 튄다
                var hp = body + f * rr * 0.85f + sd * rr * 0.5f * hs;
                ci.DrawCircle(hp, 1.4f * s, hand, true, -1f, true);
                int on = (int)(t * 8f) % 3;
                for (int i = 0; i < 3; i++) ci.DrawCircle(hp + f * 2.6f * s + sd * (i - 1) * 1.4f * s, (i == on ? 0.8f : 0.45f) * s, new Color(1, 1, 1, i == on ? 0.9f : 0.4f), true, -1f, true);
                break;
            }
            case Tic.RubNeck:
            {
                // 목 뒤로 넘긴 손 + 주무르는 짧은 호
                var np = head - f * rr * 0.55f;
                ci.DrawLine(body + sd * rr * 0.6f * hs, np + sd * 1f * s * hs, cl, 2.2f * s, true);
                ci.DrawCircle(np, 1.5f * s, hand, true, -1f, true);
                ci.DrawArc(np, 2.8f * s, -f.Angle() + t * 5f, -f.Angle() + t * 5f + 1.2f, 5, new Color(1, 1, 1, 0.45f), 0.7f * s, true);
                break;
            }
            case Tic.CollarTug:
            {
                // 깃을 손가락으로 당긴다 (깃이 벌어진 V)
                var cp = head + f * rr * 0.1f - f * rr * 0.4f;
                var tug = Mathf.Abs(Mathf.Sin(t * 5f)) * 1.4f * s;
                ci.DrawLine(cp + sd * 1.8f * s, cp + f * (2f * s + tug), new Color("#f0ece0"), 1f * s, true);
                ci.DrawLine(cp - sd * 1.8f * s, cp + f * (2f * s + tug), new Color("#f0ece0"), 1f * s, true);
                ci.DrawCircle(cp + f * (2.2f * s + tug), 1.2f * s, hand, true, -1f, true);
                break;
            }
            case Tic.LipBite:
            {
                // 깨문 아랫입술 (붉은 짧은 호) + 턱의 긴장 선
                var mp = head + f * rr * 0.5f;
                ci.DrawArc(mp, 1.4f * s, f.Angle() + 0.6f, f.Angle() + 2.5f, 6, new Color("#b0413a"), 1f * s, true);
                ci.DrawLine(mp + sd * 2.2f * s, mp + sd * 2.6f * s - f * 1.2f * s, new Color(1f, 0.5f, 0.4f, 0.5f), 0.7f * s, true);
                ci.DrawLine(mp - sd * 2.2f * s, mp - sd * 2.6f * s - f * 1.2f * s, new Color(1f, 0.5f, 0.4f, 0.5f), 0.7f * s, true);
                break;
            }
            case Tic.Stretch:
            {
                // 기지개: 두 팔을 비스듬히 위로 쭉
                float k = 0.8f + 0.2f * Mathf.Sin(t * 2f);
                ci.DrawLine(body + sd * rr * 0.5f, body + (sd + f * 0.3f) * rr * 1.7f * k, cl, 2.2f * s, true);
                ci.DrawLine(body - sd * rr * 0.5f, body + (-sd + f * 0.3f) * rr * 1.7f * k, cl, 2.2f * s, true);
                ci.DrawCircle(body + (sd + f * 0.3f) * rr * 1.7f * k, 1.3f * s, hand, true, -1f, true);
                ci.DrawCircle(body + (-sd + f * 0.3f) * rr * 1.7f * k, 1.3f * s, hand, true, -1f, true);
                break;
            }
        }
    }

    private static void PaintMug(CanvasItem ci, Vector2 at, float s, Vector2 f, float tilt)
    {
        var d = f.Rotated(tilt);
        var n = new Vector2(-d.Y, d.X);
        var pts = new[] { at - n * 1.8f * s - d * 1.6f * s, at + n * 1.8f * s - d * 1.6f * s, at + n * 1.8f * s + d * 1.6f * s, at - n * 1.8f * s + d * 1.6f * s };
        ci.DrawColoredPolygon(pts, new Color("#e9e4da"));
        ci.DrawCircle(at + d * 1.6f * s, 1.5f * s, GsCoffee, true, -1f, true);
        ci.DrawArc(at + n * 2.4f * s, 1f * s, n.Angle() - 1.5f, n.Angle() + 1.5f, 5, new Color("#c9c2b4"), 0.7f * s, true);
        if (tilt < 0.1f) ci.DrawArc(at + d * 3.4f * s, 1.2f * s, d.Angle() - 0.8f, d.Angle() + 0.8f, 4, new Color(1, 1, 1, 0.4f), 0.6f * s, true); // 김
    }

    private static void PaintWrench(CanvasItem ci, Vector2 at, Vector2 dir, float s)
    {
        var d = dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector2.Right;
        ci.DrawLine(at - d * 2.6f * s, at + d * 2f * s, new Color("#9aa4ad"), 1.1f * s, true);
        ci.DrawArc(at + d * 2.6f * s, 1.1f * s, d.Angle() - 2.2f, d.Angle() + 2.2f, 6, new Color("#9aa4ad"), 0.9f * s, true);
        ci.DrawCircle(at - d * 1.6f * s, 0.9f * s, GsHand, true, -1f, true);
    }

    private static void PaintNote(CanvasItem ci, Vector2 at, float s, Color col, bool two)
    {
        ci.DrawCircle(at, 1.3f * s, col, true, -1f, true);
        ci.DrawLine(at + new Vector2(1.2f, 0) * s, at + new Vector2(1.2f, -4.5f) * s, col, 0.8f * s, true);
        if (!two) { ci.DrawLine(at + new Vector2(1.2f, -4.5f) * s, at + new Vector2(3f, -3.2f) * s, col, 0.8f * s, true); return; }
        var b = at + new Vector2(4f, 0.8f) * s;
        ci.DrawCircle(b, 1.3f * s, col, true, -1f, true);
        ci.DrawLine(b + new Vector2(1.2f, 0) * s, b + new Vector2(1.2f, -4.5f) * s, col, 0.8f * s, true);
        ci.DrawLine(at + new Vector2(1.2f, -4.5f) * s, b + new Vector2(1.2f, -4.5f) * s, col, 1.2f * s, true);
    }

    private static void PaintDots(CanvasItem ci, Vector2 a, Vector2 b, Color col, float s)
    {
        float len = (b - a).Length();
        int n = Mathf.Clamp((int)(len / (3f * s)), 1, 24);
        for (int i = 1; i <= n; i++) ci.DrawCircle(a.Lerp(b, i / (float)(n + 1)), 0.55f * s, col, true, -1f, true);
    }
}
