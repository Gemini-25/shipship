using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.8 반응 몸짓: 사람 인형 위에 그 순간의 몸짓을 덧그린다 (읽기만 한다).
//  늘 보이는 것: 손전등과 빛줄기 · 둘러쓴 담요(누빈 무늬) · 허리에 묶은 겉옷(소매 매듭) · 땀방울 · 떨림과 입김 · 손에 든 잔(김 · 물방울).
//  잠깐 보이는 것: 이마 훔치기 · 손부채 · 팔짱 · 벽 더듬기 · 까치발 · 몸 틀기 · 쳐다보기 · 귀 기울이기 · 코 막기 · 킁킁 · 기침 ·
//                  버티기 · 귀 막기 · 구경(반짝임) · 어깨 토닥이기 · 손가락질 · 손 비비기 · 제자리 뛰기 · 붙어 앉기 · 울기 · 말하기 · 어깨 으쓱 · 창밖 보기 · 끄덕임.
//  멀리선(lod 0) 빛줄기 · 담요 색 · 땀/입김 점만, 가까이 갈수록 손 · 무늬 · 김이 보인다.
public partial class ShipView
{
    private static readonly Color RxTorchBeam = new(1f, 0.95f, 0.72f);
    private static readonly Color RxSweat = new(0.62f, 0.85f, 1f, 0.9f);
    private static readonly Color RxBreath = new(0.92f, 0.96f, 1f, 0.55f);
    private static readonly Color[] RxQuilts = { new("#8a5a9e"), new("#3f7d6b"), new("#b0603a"), new("#4a6aa8"), new("#a8823a") };

    private void PaintReact(CanvasItem ci, CrewMember c, Vector2 body, Vector2 facing, float rr, float s, int lod)
    {
        if (ReactSystem.Off || _world.React.Peek(c) is not ReactState st) return;
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        var f = facing.LengthSquared() > 1e-4f ? facing.Normalized() : Vector2.Right;
        var sd = new Vector2(-f.Y, f.X);
        float t = _time + c.Id * 0.37f;
        var head = body + f * rr * 0.15f;
        long tick = _world.Tick;
        var g = tick < st.GUntil ? st.G : Gesture.None;

        // ── 늘 보이는 것 ──
        if (st.JacketOff && lod >= 1) PaintTiedJacket(ci, c, body, f, sd, rr);
        if (st.Wrapped) PaintBlanket(ci, c, body, f, sd, rr, lod, st.Shiver > 0 ? Mathf.Sin(t * 30f) * 0.5f : 0f);
        if (st.TorchOn) PaintTorch(ci, body, f, sd, rr, lod, t);
        if (st.Sweat > 0) PaintSweat(ci, head, f, sd, rr, st.Sweat, t, lod);
        if (st.Shiver > 0) PaintShiver(ci, body, head, f, sd, rr, st.Shiver, t, lod);
        if (tick < st.CupUntil && lod >= 1) PaintCup(ci, body + f * rr * 0.9f + sd * rr * 0.55f, rr, st.WayFor == Stir.Cold, t);
        if (g == Gesture.None || lod == 0 && g is not (Gesture.Torch or Gesture.Jog or Gesture.Cry)) return;

        // ── 몸짓 ──
        float age = (tick - st.GSince) / (float)System.Math.Max(1L, st.GUntil - st.GSince);
        Vector2? look = st.LookAt is System.Numerics.Vector2 la ? ToPx(la) : null;
        var hand = new Color("#e8c4a0");
        switch (g)
        {
            case Gesture.WipeBrow:
            {
                // 손등이 이마를 가로질러 훔친다 + 튀는 땀
                float k = Mathf.Sin(t * 5f);
                var hp = head + f * rr * 0.55f + sd * rr * 0.5f * k;
                ci.DrawLine(body + sd * rr * 0.8f, hp, hand.Darkened(0.2f), 2.2f * s, true);
                ci.DrawCircle(hp, 1.6f * s, hand, true, -1f, true);
                for (int i = 0; i < 2; i++) ci.DrawCircle(hp - sd * (3f + i * 2.5f) * s + f * (1f + i) * s, 0.9f * s, RxSweat, true, -1f, true);
                break;
            }
            case Gesture.FanSelf:
            {
                // 펼친 손바닥이 얼굴 앞에서 부채질 (부채꼴 잔상)
                float a0 = f.Angle() + Mathf.Pi * 0.5f + Mathf.Sin(t * 14f) * 0.5f;
                var hp = head + f * rr * 0.9f + sd * rr * 0.4f;
                ci.DrawArc(hp, rr * 0.6f, a0 - 0.5f, a0 + 0.5f, 8, hand.WithAlpha(0.5f), 2.6f * s, true);
                ci.DrawArc(hp, rr * 0.9f, a0 - 0.4f, a0 + 0.4f, 8, new Color(1, 1, 1, 0.25f), 1f * s, true);
                ci.DrawCircle(hp, 1.4f * s, hand, true, -1f, true);
                break;
            }
            case Gesture.ShedJacket:
            {
                // 벗은 겉옷을 휘둘러 허리에 묶는다: 소매가 휘날린다
                float sw = Mathf.Sin(age * Mathf.Pi * 3f);
                var cloth = RoleCloth(c.Role).Darkened(0.1f);
                var a = body - f * rr * 0.4f + sd * rr * 0.9f;
                var b = a + sd * rr * (0.8f + 0.4f * sw) - f * rr * 0.6f;
                ci.DrawLine(a, b, cloth, 3.2f * s, true);
                ci.DrawLine(b, b + f * rr * 0.5f * sw - sd * rr * 0.3f, cloth.Darkened(0.2f), 2.2f * s, true);
                break;
            }
            case Gesture.HugSelf:
            {
                // 팔짱: 가슴 앞에서 엇갈린 두 팔
                var cl = RoleCloth(c.Role).Darkened(0.15f);
                ci.DrawLine(body + sd * rr * 0.7f, body + f * rr * 0.55f - sd * rr * 0.45f, cl, 2.6f * s, true);
                ci.DrawLine(body - sd * rr * 0.7f, body + f * rr * 0.65f + sd * rr * 0.45f, cl.Lightened(0.08f), 2.6f * s, true);
                ci.DrawCircle(body + f * rr * 0.55f - sd * rr * 0.5f, 1.3f * s, hand, true, -1f, true);
                ci.DrawCircle(body + f * rr * 0.65f + sd * rr * 0.5f, 1.3f * s, hand, true, -1f, true);
                break;
            }
            case Gesture.RubHands:
            {
                // 두 손을 모아 비비고 입김을 분다
                float k = Mathf.Sin(t * 18f) * 1.2f * s;
                var hp = body + f * rr * 0.95f;
                ci.DrawCircle(hp + sd * (1.4f * s + k), 1.4f * s, hand, true, -1f, true);
                ci.DrawCircle(hp - sd * (1.4f * s + k), 1.4f * s, hand.Darkened(0.1f), true, -1f, true);
                ci.DrawCircle(hp + f * (2.5f + Mathf.PosMod(t * 3f, 3f)) * s, (1f + Mathf.PosMod(t * 3f, 3f) * 0.4f) * s, RxBreath, true, -1f, true);
                break;
            }
            case Gesture.Torch:
                PaintTorch(ci, body, f, sd, rr, lod, t);
                break;
            case Gesture.Screen:
            {
                // 손목 단말 · 콘솔 화면의 푸른 빛이 손과 얼굴을 비춘다
                var wp = body + f * rr * 0.8f - sd * rr * 0.55f;
                ci.DrawRect(new Rect2(wp - new Vector2(1.6f, 1.1f) * s, new Vector2(3.2f, 2.2f) * s), new Color("#7fd0ff"));
                ci.DrawCircle(wp, rr * 1.1f, new Color(0.45f, 0.75f, 1f, 0.12f + 0.04f * Mathf.Sin(t * 2f)), true, -1f, true);
                break;
            }
            case Gesture.FeelWall:
            {
                // 한 팔을 옆으로 뻗어 손끝으로 더듬는다 (손가락 셋)
                float k = Mathf.Sin(t * 4f) * 0.3f;
                var tip = body + sd * rr * 1.7f + f * rr * (0.5f + k);
                ci.DrawLine(body + sd * rr * 0.7f, tip, RoleCloth(c.Role), 2.4f * s, true);
                for (int i = -1; i <= 1; i++) ci.DrawLine(tip, tip + (sd * 1.6f + f * i * 1.1f) * s, hand, 0.8f * s, true);
                break;
            }
            case Gesture.Tiptoe:
            {
                // 까치발: 두 팔을 벌려 균형을 잡고, 발밑에 짧은 발자국
                ci.DrawLine(body + sd * rr * 0.6f, body + sd * rr * 1.5f + f * rr * 0.2f, RoleCloth(c.Role), 2f * s, true);
                ci.DrawLine(body - sd * rr * 0.6f, body - sd * rr * 1.5f + f * rr * 0.2f, RoleCloth(c.Role), 2f * s, true);
                for (int i = 0; i < 3; i++)
                {
                    var fp = body - f * rr * (1.2f + i * 0.7f) + sd * (i % 2 == 0 ? 1.5f : -1.5f) * s;
                    ci.DrawCircle(fp, 0.9f * s, new Color(0.2f, 0.2f, 0.22f, 0.35f - i * 0.08f), true, -1f, true);
                }
                break;
            }
            case Gesture.Sidestep:
            {
                // 몸을 틀어 비켜 가는 휜 화살표 (쳐다보는 곳 = 유리)
                var to = look ?? body + f * rr * 2f;
                var mid = (body + to) * 0.5f + sd * rr * 1.4f;
                ci.DrawPolyline(new[] { body + f * rr * 0.8f, mid, to + sd * rr * 1.2f + f * rr }, new Color(1f, 0.85f, 0.4f, 0.5f), 1.2f * s, true);
                ci.DrawArc(to, rr * 0.5f, 0f, Mathf.Tau, 10, new Color(0.8f, 0.95f, 1f, 0.45f), 0.8f * s, true);
                break;
            }
            case Gesture.Look or Gesture.Stare:
            {
                // 눈길: 쳐다보는 곳으로 가는 점선 (빤히 보면 두 줄)
                if (look is not Vector2 lp) break;
                var d = (lp - head);
                float len = Mathf.Min(d.Length(), rr * (g == Gesture.Stare ? 4.5f : 3.2f));
                var dir = d.LengthSquared() > 1e-3f ? d.Normalized() : f;
                var perp = new Vector2(-dir.Y, dir.X);
                int lines = g == Gesture.Stare ? 2 : 1;
                for (int l = 0; l < lines; l++)
                {
                    var off = lines == 2 ? perp * (l == 0 ? -1.2f : 1.2f) * s : Vector2.Zero;
                    for (float x = rr * 0.8f; x < len; x += 3.2f * s) ci.DrawLine(head + dir * x + off, head + dir * (x + 1.6f * s) + off, new Color(1, 1, 1, 0.35f), 0.8f * s, true);
                }
                break;
            }
            case Gesture.Listen:
            {
                // 손을 귀에 대고, 소리 나는 쪽에서 오는 물결
                var ear = head + sd * rr * 0.75f;
                ci.DrawArc(ear, 2.2f * s, f.Angle() - 1.2f, f.Angle() + 1.2f, 8, hand, 1.4f * s, true);
                var src = look ?? head + f * rr * 3f;
                var dir = (src - ear).LengthSquared() > 1e-3f ? (src - ear).Normalized() : f;
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.PosMod(t * 1.6f + i * 0.33f, 1f);
                    ci.DrawArc(ear + dir * rr * (0.9f + ph * 1.6f), rr * (0.3f + ph * 0.5f), dir.Angle() + Mathf.Pi - 0.6f, dir.Angle() + Mathf.Pi + 0.6f, 8, new Color(1f, 1f, 0.8f, 0.5f * (1f - ph)), 0.9f * s, true);
                }
                break;
            }
            case Gesture.CoverNose or Gesture.Sniff:
            {
                var nose = head + f * rr * 0.75f;
                if (g == Gesture.CoverNose) ci.DrawCircle(nose, 2f * s, hand, true, -1f, true);
                // 냄새 물결 (킁킁이면 코로 빨려 들고, 막으면 앞에서 흩어진다)
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.PosMod(t * 0.9f + i * 0.33f, 1f);
                    float dist = g == Gesture.Sniff ? (1f - ph) * rr * 2f + rr * 0.5f : rr * 1.2f + ph * rr;
                    var p0 = nose + f * dist + sd * (i - 1) * 2.2f * s;
                    ci.DrawPolyline(new[] { p0 - sd * 1.5f * s, p0 + f * 1f * s, p0 + sd * 1.5f * s }, new Color(0.75f, 0.85f, 0.55f, 0.55f * (1f - ph * 0.6f)), 0.8f * s, true);
                }
                break;
            }
            case Gesture.Cough:
            {
                // 기침: 주먹을 입에 대고 작은 구름이 튄다
                var mouth = head + f * rr * 0.8f;
                ci.DrawCircle(mouth + sd * 1.2f * s, 1.5f * s, hand, true, -1f, true);
                float ph = Mathf.PosMod(t * 2.2f, 1f);
                for (int i = 0; i < 3; i++) ci.DrawCircle(mouth + f * (2f + ph * 5f + i * 1.5f) * s + sd * (i - 1) * 1.4f * s, (1.1f + ph) * s, new Color(0.7f, 0.7f, 0.72f, 0.45f * (1f - ph)), true, -1f, true);
                break;
            }
            case Gesture.Brace:
            {
                // 다리를 벌리고 두 팔로 버틴다 + 흔들림 선
                ci.DrawLine(body + sd * rr * 0.6f, body + sd * rr * 1.6f - f * rr * 0.3f, RoleCloth(c.Role), 2.6f * s, true);
                ci.DrawLine(body - sd * rr * 0.6f, body - sd * rr * 1.6f - f * rr * 0.3f, RoleCloth(c.Role), 2.6f * s, true);
                float k = Mathf.Sin(t * 22f) * 1.2f * s;
                ci.DrawLine(body - f * rr * 1.3f + sd * (rr + k), body - f * rr * 1.3f - sd * (rr - k), new Color(1, 1, 1, 0.3f), 0.8f * s, true);
                break;
            }
            case Gesture.CoverEars:
            {
                ci.DrawCircle(head + sd * rr * 0.8f, 1.7f * s, hand, true, -1f, true);
                ci.DrawCircle(head - sd * rr * 0.8f, 1.7f * s, hand, true, -1f, true);
                float ph = Mathf.PosMod(t * 2f, 1f);
                ci.DrawArc(head, rr * (1.3f + ph * 0.8f), 0f, Mathf.Tau, 20, new Color(1f, 0.35f, 0.3f, 0.35f * (1f - ph)), 1f * s, true);
                break;
            }
            case Gesture.Admire:
            {
                // 구경: 쳐다보는 물건 쪽에 반짝임
                var at = look ?? head + f * rr * 2.5f;
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.PosMod(t * 1.3f + i * 0.31f, 1f);
                    var sp = at + new Vector2(Mathf.Cos(i * 2.1f), Mathf.Sin(i * 2.1f)) * rr * 0.6f;
                    float r2 = (1f + 1.5f * Mathf.Sin(ph * Mathf.Pi)) * s;
                    ci.DrawLine(sp - new Vector2(r2, 0), sp + new Vector2(r2, 0), new Color(1f, 0.95f, 0.6f, 0.8f), 0.7f * s, true);
                    ci.DrawLine(sp - new Vector2(0, r2), sp + new Vector2(0, r2), new Color(1f, 0.95f, 0.6f, 0.8f), 0.7f * s, true);
                }
                break;
            }
            case Gesture.Comfort or Gesture.Huddle:
            {
                // 옆 사람 어깨에 팔을 두른다 (붙어 앉기는 둘 사이에 따스한 빛)
                var to = look ?? body + sd * rr * 1.6f;
                var dir = (to - body).LengthSquared() > 1e-3f ? (to - body).Normalized() : sd;
                var shoulder = body + dir * rr * Mathf.Min(1.9f, (to - body).Length() / rr);
                ci.DrawLine(body + dir * rr * 0.5f, shoulder, RoleCloth(c.Role).Lightened(0.05f), 2.4f * s, true);
                ci.DrawCircle(shoulder, 1.5f * s, hand, true, -1f, true);
                if (g == Gesture.Huddle) ci.DrawCircle((body + to) * 0.5f, rr * 1.3f, new Color(1f, 0.6f, 0.3f, 0.08f + 0.03f * Mathf.Sin(t * 2f)), true, -1f, true);
                break;
            }
            case Gesture.Point or Gesture.Call:
            {
                var to = look ?? body + f * rr * 3f;
                var dir = (to - body).LengthSquared() > 1e-3f ? (to - body).Normalized() : f;
                var tip = body + dir * rr * 1.8f;
                ci.DrawLine(body + sd * rr * 0.5f, tip, RoleCloth(c.Role), 2.2f * s, true);
                ci.DrawLine(tip, tip + dir * 2f * s, hand, 1f * s, true);
                break;
            }
            case Gesture.Jog:
            {
                // 제자리 뛰기: 무릎이 번갈아 올라오고 발밑에 먼지 · 움직임 선
                float k = Mathf.Sin(t * 16f);
                ci.DrawCircle(body + f * rr * 0.5f + sd * rr * 0.4f * (k > 0 ? 1 : -1), 1.6f * s, RoleCloth(c.Role).Darkened(0.4f), true, -1f, true);
                for (int i = -1; i <= 1; i += 2) ci.DrawLine(body - f * rr * 1.2f + sd * i * rr * 0.5f, body - f * rr * (1.5f + 0.3f * Mathf.Abs(k)) + sd * i * rr * 0.5f, new Color(1, 1, 1, 0.3f), 0.8f * s, true);
                break;
            }
            case Gesture.Cry:
            {
                // 눈물 줄기 + 들썩이는 어깨
                float ph = Mathf.PosMod(t * 1.2f, 1f);
                for (int i = -1; i <= 1; i += 2) ci.DrawLine(head + f * rr * 0.55f + sd * i * rr * 0.3f, head + f * rr * (0.55f - ph * 0.6f) + sd * i * rr * 0.35f, new Color(0.6f, 0.82f, 1f, 0.8f), 0.9f * s, true);
                float hv = Mathf.Abs(Mathf.Sin(t * 6f)) * 1.2f * s;
                ci.DrawArc(body - f * hv, rr * 0.95f, f.Angle() + 1.2f, f.Angle() + Mathf.Pi - 1.2f, 6, new Color(0, 0, 0, 0.25f), 1f * s, true);
                break;
            }
            case Gesture.Talk:
            {
                // 말할 때 입 앞에 짧은 소리 결 셋
                var mouth = head + f * rr * 0.9f;
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.PosMod(t * 2.4f + i * 0.33f, 1f);
                    ci.DrawArc(mouth, (1.5f + ph * 3f) * s, f.Angle() - 0.5f, f.Angle() + 0.5f, 5, new Color(1, 1, 1, 0.5f * (1f - ph)), 0.7f * s, true);
                }
                break;
            }
            case Gesture.Shrug:
            {
                float up = Mathf.Sin(Mathf.Min(1f, age * 4f) * Mathf.Pi) * 1.5f * s;
                for (int i = -1; i <= 1; i += 2) ci.DrawArc(body + sd * i * rr * 0.7f - f * up, rr * 0.35f, f.Angle() + Mathf.Pi * 0.5f * i - 0.9f, f.Angle() + Mathf.Pi * 0.5f * i + 0.9f, 6, new Color(1, 1, 1, 0.4f), 0.8f * s, true);
                break;
            }
            case Gesture.Window:
            {
                // 창가: 차가운 별빛이 몸을 비춘다 (몇 점이 반짝)
                ci.DrawCircle(body, rr * 1.4f, new Color(0.6f, 0.7f, 1f, 0.1f + 0.03f * Mathf.Sin(t)), true, -1f, true);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 1.7f + t * 0.2f;
                    ci.DrawCircle(body + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr * 1.2f, 0.6f * s, new Color(0.85f, 0.9f, 1f, 0.6f + 0.3f * Mathf.Sin(t * 3f + i)), true, -1f, true);
                }
                break;
            }
            case Gesture.Nod:
            {
                float k = Mathf.Abs(Mathf.Sin(t * 8f)) * 1.5f * s;
                ci.DrawArc(head + f * (rr * 0.2f + k), rr * 0.5f, f.Angle() - 0.6f, f.Angle() + 0.6f, 6, new Color(1, 1, 1, 0.3f), 0.8f * s, true);
                break;
            }
            case Gesture.Cup:
                if (lod >= 1) PaintCup(ci, body + f * rr * 0.9f + sd * rr * 0.55f, rr, st.WayFor == Stir.Cold, t);
                break;
            case Gesture.Wrap:
                PaintBlanket(ci, c, body, f, sd, rr, lod, 0f);
                break;
        }
    }

    /// <summary>손전등: 손에 든 원통 · 앞으로 퍼지는 빛줄기 (가장자리가 흐린 부채꼴 세 겹).</summary>
    private void PaintTorch(CanvasItem ci, Vector2 body, Vector2 f, Vector2 sd, float rr, int lod, float t)
    {
        var hp = body + f * rr * 0.9f - sd * rr * 0.45f;
        float sway = Mathf.Sin(t * 0.9f) * 0.12f;
        var dir = f.Rotated(sway);
        float len = rr * 7f;
        for (int k = 0; k < 3; k++)
        {
            float wdt = 0.28f + k * 0.12f;
            var pts = new[] { hp, hp + dir.Rotated(-wdt) * len * (1f - k * 0.12f), hp + dir * len * (1.04f - k * 0.1f), hp + dir.Rotated(wdt) * len * (1f - k * 0.12f) };
            ci.DrawColoredPolygon(pts, RxTorchBeam.WithAlpha(0.13f - k * 0.035f));
        }
        if (lod == 0) return;
        ci.DrawLine(hp - dir * 2.6f, hp + dir * 1.2f, new Color("#3a3d44"), 2.4f, true);
        ci.DrawLine(hp + dir * 1.2f, hp + dir * 1.9f, new Color("#9aa0aa"), 2.9f, true);
        ci.DrawCircle(hp + dir * 2.1f, 1.1f, RxTorchBeam, true, -1f, true);
    }

    /// <summary>둘러쓴 담요: 어깨부터 등까지 덮는 천 · 누빈 줄 · 테두리 (사람마다 다른 색).</summary>
    private void PaintBlanket(CanvasItem ci, CrewMember c, Vector2 body, Vector2 f, Vector2 sd, float rr, int lod, float jit)
    {
        var col = RxQuilts[(c.Id * 7 + 3) % RxQuilts.Length];
        var b = body + sd * jit;
        var pts = new[]
        {
            b + f * rr * 0.35f + sd * rr * 1.05f, b + f * rr * 0.55f, b + f * rr * 0.35f - sd * rr * 1.05f,
            b - f * rr * 0.9f - sd * rr * 1.15f, b - f * rr * 1.25f, b - f * rr * 0.9f + sd * rr * 1.15f,
        };
        ci.DrawColoredPolygon(pts, col.WithAlpha(0.92f));
        if (lod == 0) return;
        var line = col.Lightened(0.3f).WithAlpha(0.7f);
        for (int i = 0; i < pts.Length; i++) ci.DrawLine(pts[i], pts[(i + 1) % pts.Length], col.Darkened(0.35f), 1f, true);
        // 누빈 줄: 마름모 격자
        for (int i = -1; i <= 1; i++)
        {
            ci.DrawLine(b + sd * rr * (i * 0.6f + 0.3f) + f * rr * 0.3f, b + sd * rr * (i * 0.6f - 0.3f) - f * rr * 0.9f, line, 0.6f, true);
            ci.DrawLine(b + sd * rr * (i * 0.6f - 0.3f) + f * rr * 0.3f, b + sd * rr * (i * 0.6f + 0.3f) - f * rr * 0.9f, line, 0.6f, true);
        }
    }

    /// <summary>허리에 묶은 겉옷: 허리를 감은 띠 + 앞에서 묶은 소매 매듭 두 개 + 늘어진 끝.</summary>
    private void PaintTiedJacket(CanvasItem ci, CrewMember c, Vector2 body, Vector2 f, Vector2 sd, float rr)
    {
        var cl = RoleCloth(c.Role).Darkened(0.25f);
        ci.DrawArc(body - f * rr * 0.15f, rr * 0.95f, f.Angle() + 0.4f, f.Angle() + Mathf.Tau - 0.4f, 14, cl, 2.2f, true);
        var knot = body + f * rr * 0.75f;
        ci.DrawCircle(knot + sd * 1.3f, 1.4f, cl.Lightened(0.1f), true, -1f, true);
        ci.DrawCircle(knot - sd * 1.3f, 1.4f, cl.Lightened(0.1f), true, -1f, true);
        ci.DrawLine(knot, knot + f * 3f + sd * 1.5f, cl, 1.4f, true);
    }

    /// <summary>땀: 단계만큼 이마 · 목에 물방울, 셋이면 흘러내린다.</summary>
    private void PaintSweat(CanvasItem ci, Vector2 head, Vector2 f, Vector2 sd, float rr, int lv, float t, int lod)
    {
        int n = lod == 0 ? 1 : lv + 1;
        for (int i = 0; i < n; i++)
        {
            float ph = lv >= 3 ? Mathf.PosMod(t * 0.7f + i * 0.27f, 1f) : 0f;
            var p = head + f * rr * (0.3f - ph * 0.8f) + sd * rr * (i % 2 == 0 ? 0.55f : -0.55f) * (1f + i * 0.15f);
            ci.DrawCircle(p, 0.9f + 0.15f * lv, RxSweat, true, -1f, true);
            if (lod >= 2) ci.DrawCircle(p - f * 0.3f + sd * 0.3f, 0.35f, new Color(1, 1, 1, 0.9f), true, -1f, true);
        }
    }

    /// <summary>떨림: 몸 테두리가 잘게 흔들리고 입김이 피어오른다 (단계가 높을수록 자주 · 크게).</summary>
    private void PaintShiver(CanvasItem ci, Vector2 body, Vector2 head, Vector2 f, Vector2 sd, float rr, int lv, float t, int lod)
    {
        float k = Mathf.Sin(t * (28f + lv * 6f)) * 0.6f * lv;
        if (lod >= 1)
            for (int i = -1; i <= 1; i += 2)
                ci.DrawPolyline(new[] { body + sd * i * (rr * 1.1f + k) - f * rr * 0.5f, body + sd * i * (rr * 1.25f - k) - f * rr * 0.1f, body + sd * i * (rr * 1.1f + k) + f * rr * 0.3f }, new Color(0.8f, 0.9f, 1f, 0.45f), 0.7f, true);
        float ph = Mathf.PosMod(t * (0.5f + 0.15f * lv), 1f);
        ci.DrawCircle(head + f * rr * (0.9f + ph * 1.4f), rr * (0.18f + ph * 0.25f), RxBreath.WithAlpha(0.5f * (1f - ph)), true, -1f, true);
    }

    /// <summary>손에 든 잔: 따뜻한 차는 머그와 김, 찬물은 유리컵과 물방울.</summary>
    private void PaintCup(CanvasItem ci, Vector2 at, float rr, bool warm, float t)
    {
        if (warm)
        {
            ci.DrawCircle(at, rr * 0.28f, new Color("#e9e2d4"), true, -1f, true);
            ci.DrawCircle(at, rr * 0.2f, new Color("#7a4a2a"), true, -1f, true);
            ci.DrawArc(at + new Vector2(rr * 0.3f, 0), rr * 0.12f, -1.4f, 1.4f, 6, new Color("#e9e2d4"), 0.8f, true);
            for (int i = 0; i < 2; i++)
            {
                float ph = Mathf.PosMod(t * 0.8f + i * 0.5f, 1f);
                var p = at + new Vector2(Mathf.Sin(ph * 6f + i) * 1.2f, -rr * (0.3f + ph * 0.8f));
                ci.DrawCircle(p, 0.8f + ph, new Color(1, 1, 1, 0.35f * (1f - ph)), true, -1f, true);
            }
        }
        else
        {
            ci.DrawArc(at, rr * 0.25f, 0f, Mathf.Tau, 12, new Color(0.85f, 0.95f, 1f, 0.8f), 0.8f, true);
            ci.DrawCircle(at, rr * 0.2f, new Color(0.55f, 0.8f, 1f, 0.45f), true, -1f, true);
            ci.DrawCircle(at + new Vector2(rr * 0.22f, rr * 0.05f), 0.45f, new Color(1, 1, 1, 0.8f), true, -1f, true);
        }
    }
}
