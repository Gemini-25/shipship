using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.1 위에서 본 승무원 인형: 신발 · 다리 → 몸통(옷 색 · 무늬 · 직업 표지) → 양팔 · 손 → 손에 든 물건 → 머리 · 머리카락 · 수염 · (우주복이면 등짐 · 헬멧).
// 사람마다: 피부 · 머리 모양 · 길이(덥수룩하면 윤곽이 커지고 삐죽) · 색 · 수염 · 체격(체중) · 옷 무늬 · 개인 색(목깃 · 무늬 — 승무원 목록 색과 같다).
// 자세: 서기 · 걷기(팔다리 엇갈림) · 뛰기(팔 굽혀 흔듦 · 앞으로 숙임) · 앉기(무릎이 앞으로) · 무릎 꿇기(정강이가 뒤로) · 일하기(두 손 앞으로 · 공구 움직임)
//       · 기어가기(정비 통로) · 눕기(잠 · 쓰러짐) · 다친 팔 늘어뜨림(팔걸이 붕대) · 잃은 팔(묶은 소매) · 의수(금속) · 양손 짐.
// 확대 단계: 멀리선 점 + 색(직업 옷 색 + 개인 색 테), 중간은 단순 인형, 가까이선 손 · 무늬 · 머리카락 결 · 물건 세부.
// 읽기만 한다 (Puppet 사양 · BodyLook).
public partial class ShipView
{
    private static readonly Color[] SkinTones =
    {
        new("#f3cfb0"), new("#e2b08a"), new("#c99068"), new("#a8714a"), new("#7f5236"), new("#5b3a26"),
    };

    private static readonly Color[] HairTones =
    {
        new("#1d1817"), new("#3b2a20"), new("#6b4b2f"), new("#8e4a26"), new("#cfa862"), new("#9a8a74"),
        new("#2f9a92"), new("#c95a95"), new("#bdb9b2"),
    };

    public static Color RoleCloth(CrewRole r) => r switch
    {
        CrewRole.Engineer => new Color("#e07a28"),
        CrewRole.Medic => new Color("#dfe9ee"),
        CrewRole.Pilot => new Color("#3a74d0"),
        CrewRole.Technician => new Color("#2f9aa6"),
        CrewRole.Botanist => new Color("#3fae4a"),
        CrewRole.Electrician => new Color("#8a6ae0"),
        _ => new Color("#ece6d6"),
    };

    private static Color HairColor(BodyLook? l) => l == null ? HairTones[1] : HairTones[Mathf.Clamp(l.HairColor, 0, HairTones.Length - 1)];
    private static Color SkinColor(BodyLook? l, int id) => SkinTones[(l?.Skin ?? (byte)(id % 6)) % SkinTones.Length];

    private static Vector2[] Close(Vector2[] pts)
    {
        var r = new Vector2[pts.Length + 1];
        pts.CopyTo(r, 0);
        r[^1] = pts[0];
        return r;
    }

    /// <summary>타원 (단위 원을 늘려 그린다 — 다각형을 만들지 않아 가볍다).</summary>
    private static void Oval(CanvasItem ci, Transform2D xf, Vector2 at, float rx, float ry, Color col) =>
        ci.DrawEllipse(at, rx, ry, col, true, -1f, true); // 인형 좌표(앞 = x)에 맞춘 타원 — 늘리지 않아 가장자리가 번지지 않는다

    /// <summary>멀리서: 점 + 색 (직업 옷 색 · 개인 색 테 · 머리색 점).</summary>
    private void PaintCrewDot(CanvasItem ci, CrewMember c, Vector2 p, float radius, Color accent)
    {
        var l = _world.Body2.Peek(c);
        var cloth = c.Suit != null ? new Color("#dfe6ee") : RoleCloth(c.Role);
        ci.DrawCircle(p, radius * 0.95f, Palette.Space.WithAlpha(0.85f), true, -1f, true);
        ci.DrawCircle(p, radius * 0.8f, cloth, true, -1f, true);
        ci.DrawArc(p, radius * 0.8f, 0f, Mathf.Tau, 20, accent, Mathf.Max(1.5f, radius * 0.22f), true);
        var f = c.Facing.ToGodot();
        ci.DrawCircle(p + f * radius * 0.18f, radius * 0.36f, c.Suit != null ? new Color("#1c2a3e") : l?.Style == HairStyle.Bald ? SkinColor(l, c.Id) : HairColor(l), true, -1f, true);
    }

    /// <summary>선 · 걷는 · 뛰는 · 앉은 · 무릎 꿇은 · 일하는 인형.</summary>
    private void PaintPuppet(CanvasItem ci, CrewMember c, Vector2 body, Vector2 facing, float s, PuppetSpec spec, int lod, Color accent, bool dim)
    {
        var l = _world.Body2.Peek(c);
        bool detail = lod >= 2;
        float t = _time;
        float ang = facing.LengthSquared() > 1e-4f ? facing.Angle() : 0f;
        float u = s * (c.IsChild ? 0.9f : 1f);
        var xf = new Transform2D(ang, new Vector2(u, u), 0f, body);
        ci.DrawSetTransformMatrix(xf);

        var skin = SkinColor(l, c.Id);
        var hair = HairColor(l);
        // v16.24 어두운 피부 · 머리는 어두운 바닥에 묻힌다: 색감은 두고 밝기만 조금 (테두리는 PaintHead)
        skin = skin.Lightened(ZoomDetail.Lift(ZoomDetail.Luma(skin.R, skin.G, skin.B)) * 0.5f);
        hair = hair.Lightened(ZoomDetail.Lift(ZoomDetail.Luma(hair.R, hair.G, hair.B)));
        bool suit = spec.Suit;
        var cloth = suit ? new Color("#dfe6ee") : WearCloth(c, RoleCloth(c.Role)); // v18.0 작업복 · 평상복 · 잠옷 · 방열복
        if (dim) cloth = cloth.Lerp(new Color("#8a8f99"), 0.35f);
        var pants = suit ? new Color("#c3ccd6") : cloth.Darkened(0.42f);
        var outline = Palette.Space.WithAlpha(0.6f);
        // 체격: 체중 · 키 (BMI) → 어깨 폭 · 배
        float bmi = l?.Bmi ?? 23f;
        float wf = Mathf.Clamp(0.86f + (bmi - 22f) * 0.03f, 0.82f, 1.3f) * (suit ? 1.14f : 1f);
        float W = 7.9f * wf, D = 4.7f * Mathf.Clamp(0.9f + (bmi - 22f) * 0.025f, 0.85f, 1.25f);
        var pose = spec.Pose;
        bool moving = pose is PuppetPose.Walk or PuppetPose.Run;
        float freq = pose == PuppetPose.Run ? 17f : c.Gait.Quiet(_world) ? 7f : 11f;
        float ph = t * freq + c.Id * 1.3f;
        float swing = moving ? Mathf.Sin(ph) : 0f;
        float lean = pose == PuppetPose.Run ? 1.4f : pose == PuppetPose.Kneel ? 1.8f : 0f;

        // ── 다리 · 신발 ──
        var shoe = suit ? new Color("#9aa4ae") : new Color("#2a2522");
        switch (pose)
        {
            case PuppetPose.Walk:
            case PuppetPose.Run:
            {
                float stride = (pose == PuppetPose.Run ? 4.6f : c.Gait.Quiet(_world) ? 1.8f : 3.2f) * (spec.Limp ? 0.6f : 1f);
                for (int k = -1; k <= 1; k += 2)
                {
                    float x = stride * swing * k;
                    if (spec.Limp && k > 0) x *= 0.45f; // 다친 다리는 짧게 딛는다
                    ci.DrawLine(new Vector2(-0.6f, k * 2.3f), new Vector2(x, k * 2.5f), pants, 2.8f, true);
                    Oval(ci, xf, new Vector2(x + 0.9f, k * 2.5f), 1.6f, 1.15f, shoe);
                }
                break;
            }
            case PuppetPose.Sit:
                for (int k = -1; k <= 1; k += 2)
                {
                    ci.DrawLine(new Vector2(-0.5f, k * 2.4f), new Vector2(6.4f, k * 2.7f), outline, 4.4f, true);
                    ci.DrawLine(new Vector2(-0.5f, k * 2.4f), new Vector2(6.4f, k * 2.7f), pants, 3.3f, true);
                    Oval(ci, xf, new Vector2(7.6f, k * 2.8f), 1.5f, 1.15f, shoe);
                }
                break;
            case PuppetPose.Kneel:
                for (int k = -1; k <= 1; k += 2)
                {
                    ci.DrawLine(new Vector2(-1.2f, k * 2.4f), new Vector2(-7.4f, k * 2.6f), outline, 4f, true);
                    ci.DrawLine(new Vector2(-1.2f, k * 2.4f), new Vector2(-7.4f, k * 2.6f), pants, 2.9f, true);
                    Oval(ci, xf, new Vector2(-8.3f, k * 2.6f), 1.1f, 1.2f, shoe.Lightened(0.35f)); // 신발 바닥이 보인다
                }
                break;
            default:
                for (int k = -1; k <= 1; k += 2) Oval(ci, xf, new Vector2(2.6f, k * 2.2f), 1.3f, 1f, shoe);
                break;
        }

        // ── 우주복 등짐 (몸통 아래) ──
        if (suit)
        {
            Gfx.RoundRect(ci, new Rect2(-7.4f, -4.2f, 4.6f, 8.4f), new Color("#7d8794"), 1.4f, outline);
            if (detail)
            {
                ci.DrawLine(new Vector2(-6.6f, -2.2f), new Vector2(-3.6f, -2.2f), new Color("#4c5560"), 0.6f);
                ci.DrawLine(new Vector2(-6.6f, 2.2f), new Vector2(-3.6f, 2.2f), new Color("#4c5560"), 0.6f);
                ci.DrawCircle(new Vector2(-5.1f, 0f), 0.9f, (c.Suit!.Oxygen < 0.75f ? Palette.Warning : Palette.Good).WithAlpha(0.6f + 0.4f * Mathf.Sin(t * 4f)), true, -1f, true);
            }
        }

        // ── 몸통 (옷 · 무늬 · 배) ──
        var torso = new Vector2(-0.7f + lean * 0.3f, 0f);
        Oval(ci, xf, torso, D + 0.9f, W + 0.9f, outline);
        Oval(ci, xf, torso, D, W, cloth);
        if (bmi > 26f && !suit) Oval(ci, xf, torso + new Vector2(D * 0.55f, 0f), 1f + (bmi - 26f) * 0.32f, W * 0.55f, cloth.Lightened(0.06f));
        var pattern = l?.Pattern ?? OutfitPattern.Plain;
        if (!suit && lod >= 1)
        {
            switch (pattern)
            {
                case OutfitPattern.Stripe: Gfx.RoundRect(ci, new Rect2(torso.X - 0.8f, -W * 0.93f, 1.5f, W * 1.86f), accent.WithAlpha(0.9f), 0.6f); break;
                case OutfitPattern.Vest: Oval(ci, xf, torso + new Vector2(-0.9f, 0f), D * 0.62f, W * 0.66f, accent.Darkened(0.15f)); break;
                case OutfitPattern.Patch:
                    Oval(ci, xf, new Vector2(torso.X, -W * 0.78f), 1.3f, 1.3f, accent);
                    Oval(ci, xf, new Vector2(torso.X, W * 0.78f), 1.3f, 1.3f, accent);
                    break;
            }
            if (c.Role == CrewRole.Cook) // 앞치마 끈 · 체크
                for (int k = -2; k <= 2; k++) ci.DrawLine(new Vector2(torso.X - D * 0.7f, k * W * 0.32f), new Vector2(torso.X + D * 0.4f, k * W * 0.32f), new Color(0.75f, 0.3f, 0.3f, 0.35f), 0.5f);
            if (detail) PaintRoleMark(ci, c.Role, torso + new Vector2(-1.9f, 0f));
            PaintWear(ci, xf, c, torso, D, W, accent, detail, t); // v18.0 옷 무늬 · 조끼 · 때 · 찢어짐
        }
        if (suit)
        {
            // 개인 색 어깨띠 (헬멧을 써도 누군지 안다) · 치수가 안 맞으면 표가 난다
            ci.DrawLine(new Vector2(torso.X - 0.5f, -W * 0.85f), new Vector2(torso.X + 0.5f, -W * 0.45f), accent, 1.1f, true);
            ci.DrawLine(new Vector2(torso.X - 0.5f, W * 0.85f), new Vector2(torso.X + 0.5f, W * 0.45f), accent, 1.1f, true);
            if (detail && spec.Misfit >= Body2System.FitWarnKg)
                for (int k = -1; k <= 1; k += 2) // 꽉 낀다: 어깨 · 겨드랑이 이음매가 당긴다 (붉은 당김 금)
                    for (int j = 0; j < 3; j++)
                        ci.DrawLine(new Vector2(torso.X - 1.2f + j * 1.1f, k * (W - 0.4f)), new Vector2(torso.X - 0.8f + j * 1.1f, k * (W - 1.8f)), new Color(0.9f, 0.3f, 0.25f, 0.85f), 0.45f, true);
            else if (detail && spec.Misfit <= -Body2System.FitWarnKg)
                for (int j = 0; j < 3; j++) // 헐렁하다: 몸통에 주름이 진다
                    ci.DrawArc(torso + new Vector2(-1.6f + j * 1.2f, 0f), W * 0.55f, -0.7f, 0.7f, 6, new Color(0.45f, 0.5f, 0.58f, 0.8f), 0.45f, true);
        }

        // ── 팔 · 손 · 든 것 ──
        PaintArms(ci, xf, c, spec, W, swing, pose, cloth, accent, skin, suit, lod, t, pattern);

        // ── 목깃 (개인 색) · 목도리 ──
        var head = new Vector2(1.2f + lean, 0f);
        if (!suit)
        {
            ci.DrawArc(head, 3.6f, Mathf.Pi * 0.55f, Mathf.Pi * 1.45f, 10, accent, 1.2f, true);
            if (pattern == OutfitPattern.Scarf && lod >= 1)
            {
                float sw = Mathf.Sin(t * 3f + c.Id) * (moving ? 1.2f : 0.3f);
                ci.DrawPolyline(new[] { new Vector2(-2.6f, 2f), new Vector2(-5f, 2.6f + sw * 0.5f), new Vector2(-7.4f, 2.2f + sw) }, accent, 1.5f, true);
            }
        }

        // ── 머리 · 머리카락 · 수염 · 헬멧 ──
        if (suit) PaintHelmet(ci, xf, head, skin, accent, detail, t, c);
        else PaintHead(ci, xf, head, c, l, skin, hair, lod, moving ? swing : 0f, t);
        if (!suit) PaintEyewear(ci, xf, head, c, lod); // v18.0 안경 · 보안경 · 방열복 두건

        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private static void PaintRoleMark(CanvasItem ci, CrewRole role, Vector2 at)
    {
        var mk = new Color(1f, 1f, 1f, 0.75f);
        switch (role)
        {
            case CrewRole.Medic:
                ci.DrawRect(new Rect2(at.X - 0.35f, at.Y - 1.1f, 0.7f, 2.2f), new Color("#e05050"));
                ci.DrawRect(new Rect2(at.X - 1.1f, at.Y - 0.35f, 2.2f, 0.7f), new Color("#e05050"));
                break;
            case CrewRole.Engineer: ci.DrawArc(at, 1f, 0f, Mathf.Tau, 8, mk, 0.5f, true); for (int k = 0; k < 6; k++) { float a = k * Mathf.Tau / 6f; ci.DrawLine(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)), at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.5f, mk, 0.45f); } break;
            case CrewRole.Pilot: ci.DrawPolyline(new[] { at + new Vector2(-0.6f, -1.6f), at + new Vector2(0.6f, 0f), at + new Vector2(-0.6f, 1.6f) }, mk, 0.55f, true); break;
            case CrewRole.Technician: ci.DrawLine(at + new Vector2(-1f, -1f), at + new Vector2(1f, 1f), mk, 0.55f); ci.DrawArc(at + new Vector2(1.1f, 1.1f), 0.6f, 0f, Mathf.Tau * 0.7f, 6, mk, 0.45f, true); break;
            case CrewRole.Botanist: ci.DrawArc(at, 1.2f, -2.2f, 0.6f, 8, new Color("#bff0a0"), 0.6f, true); ci.DrawLine(at + new Vector2(-0.8f, 0.8f), at + new Vector2(0.8f, -0.8f), new Color("#bff0a0"), 0.4f); break;
            case CrewRole.Electrician: ci.DrawPolyline(new[] { at + new Vector2(-1.2f, -0.7f), at + new Vector2(0.1f, 0.2f), at + new Vector2(-0.3f, -0.2f), at + new Vector2(1.2f, 0.7f) }, new Color("#ffe066"), 0.55f, true); break;
            default: ci.DrawCircle(at, 1f, new Color(1f, 1f, 1f, 0.6f), true, -1f, true); break;
        }
    }

    private void PaintArms(CanvasItem ci, Transform2D xf, CrewMember c, PuppetSpec spec, float W, float swing, PuppetPose pose, Color cloth, Color accent, Color skin, bool suit, int lod, float t, OutfitPattern pattern)
    {
        var outline = Palette.Space.WithAlpha(0.85f);
        var sleeve = pattern == OutfitPattern.Sleeves && !suit ? accent : cloth;
        var glove = suit ? new Color("#b9c2cc") : WearGlove(c, skin); // v18.0 장갑
        bool work = pose is PuppetPose.Work or PuppetPose.Kneel;
        var held = spec.Held;
        bool two = spec.TwoHands;
        // 든 손: 오른손 (못 쓰면 왼손)
        int holdSide = spec.Right is ArmState.Ok or ArmState.Prosthetic ? 1 : -1;
        bool runBent = pose == PuppetPose.Run;
        for (int k = -1; k <= 1; k += 2)
        {
            var state = k > 0 ? spec.Right : spec.Left;
            var shoulder = new Vector2(-0.5f, k * W * 0.93f);
            if (state == ArmState.Lost)
            {
                Oval(ci, xf, shoulder, 1.3f, 1.3f, sleeve.Darkened(0.2f)); // 묶은 소매
                ci.DrawLine(shoulder + new Vector2(-0.6f, 0f), shoulder + new Vector2(0.6f, 0f), outline, 0.4f);
                continue;
            }
            Vector2 hand;
            Vector2? elbow = null;
            if (state == ArmState.Hurt)
                hand = new Vector2(-0.4f, k * (W + 1.7f)); // 늘어뜨림 — 흔들지 않는다
            else if (two && held != HeldThing.None)
                hand = new Vector2(6.4f, k * 3.7f);
            else if (held != HeldThing.None && k == holdSide && held != HeldThing.Person)
                hand = work ? new Vector2(6f, k * 2.4f) : new Vector2(4.4f, k * (W * 0.75f));
            else if (work)
            {
                float m = Mathf.Sin(t * 6f + k) * 0.7f;
                hand = new Vector2(5.8f + m, k * 2.6f);
            }
            else if (pose == PuppetPose.Sit) hand = new Vector2(4.2f, k * 3.1f);
            else if (pose == PuppetPose.Walk || pose == PuppetPose.Run)
            {
                float sw = -swing * k * (runBent ? 4f : 3f);
                hand = runBent ? new Vector2(1.6f + sw, k * (W * 0.82f)) : new Vector2(1.1f + sw, k * (W + 1.1f));
                if (runBent) elbow = new Vector2(-1.2f + sw * 0.3f, k * (W + 0.6f));
            }
            else hand = new Vector2(1.2f, k * (W + 1.1f));
            var armCol = state == ArmState.Prosthetic ? new Color("#9aa3ad") : sleeve;
            if (elbow is Vector2 e)
            {
                // 굽힌 팔: 위팔 · 아래팔 두 토막 + 둥근 팔꿈치 (모서리가 뾰족하지 않게)
                ci.DrawLine(shoulder, e, outline, 3.6f, true); ci.DrawLine(e, hand, outline, 3.6f, true);
                ci.DrawLine(shoulder, e, armCol, 2.6f, true); ci.DrawLine(e, hand, armCol, 2.6f, true);
                ci.DrawCircle(e, 1.3f, armCol, true, -1f, true);
            }
            else
            {
                ci.DrawLine(shoulder, hand, outline, 3.6f, true);
                ci.DrawLine(shoulder, hand, armCol, 2.6f, true);
            }
            if (state == ArmState.Prosthetic && lod >= 2) ci.DrawCircle(shoulder.Lerp(hand, 0.5f), 0.7f, new Color("#5f6873"), true, -1f, true);
            if (state == ArmState.Hurt)
            {
                // 붕대 · 팔걸이 (반대쪽 어깨로 건 천)
                ci.DrawLine(shoulder.Lerp(hand, 0.45f) + new Vector2(-0.9f, 0f), shoulder.Lerp(hand, 0.45f) + new Vector2(0.9f, 0f), new Color("#f2f2ee"), 1.1f, true);
                if (lod >= 2) ci.DrawLine(hand + new Vector2(0.4f, 0f), new Vector2(-0.5f, -k * W * 0.7f), new Color(0.95f, 0.95f, 0.92f, 0.85f), 0.9f, true);
            }
            if (lod >= 2 || state != ArmState.Ok) ci.DrawCircle(hand, 1.35f, state == ArmState.Prosthetic ? new Color("#7c8590") : glove, true, -1f, true);
        }
        if (held == HeldThing.None || held == HeldThing.Person) return;
        var at = two ? new Vector2(7.2f, 0f) : work ? new Vector2(6.6f, holdSide * 2.4f) : new Vector2(5.1f, holdSide * W * 0.75f);
        PaintHeld(ci, xf, held, at, holdSide, accent, lod, t, c);
    }

    private void PaintHeld(CanvasItem ci, Transform2D xf, HeldThing held, Vector2 at, int side, Color accent, int lod, float t, CrewMember c)
    {
        var outline = Palette.Space.WithAlpha(0.8f);
        bool detail = ZoomDetail.Draws((ZoomTier)lod).HasFlag(Detail.Held); // v16.24 가까이: 든 물건 자세히
        switch (held)
        {
            case HeldThing.Tool:
            {
                // 렌치: 손잡이 + 벌어진 입 (돌리는 손짓)
                float a = Mathf.Sin(t * 6f + c.Id) * 0.45f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * side);
                var tip = at + dir * 4.2f;
                ci.DrawLine(at, tip, outline, 1.8f, true);
                ci.DrawLine(at, tip, new Color("#aeb6bf"), 1.1f, true);
                ci.DrawArc(tip + dir * 0.6f, 1f, dir.Angle() + 0.9f, dir.Angle() + Mathf.Tau - 0.9f, 8, new Color("#aeb6bf"), 0.9f, true);
                break;
            }
            case HeldThing.Scissors:
            {
                // 가위: 두 날이 사각사각 · 손가락 고리 · 다른 손엔 빗
                float snip = 0.18f + 0.22f * Mathf.Abs(Mathf.Sin(t * 9f));
                for (int k = -1; k <= 1; k += 2)
                {
                    var d = new Vector2(Mathf.Cos(snip * k), Mathf.Sin(snip * k));
                    ci.DrawLine(at, at + d * 3.6f, new Color("#dfe4ea"), 0.7f, true);
                    ci.DrawArc(at - d * 0.9f, 0.6f, 0f, Mathf.Tau, 8, new Color("#d0465a"), 0.45f, true);
                }
                if (detail)
                {
                    var comb = new Vector2(at.X - 0.4f, -side * 3.2f);
                    ci.DrawRect(new Rect2(comb.X - 0.3f, comb.Y - 1.4f, 0.6f, 2.8f), new Color("#2b2b30"));
                    for (int j = 0; j < 5; j++) ci.DrawLine(comb + new Vector2(0.3f, -1.2f + j * 0.6f), comb + new Vector2(1f, -1.2f + j * 0.6f), new Color("#2b2b30"), 0.25f);
                }
                break;
            }
            case HeldThing.Cup:
                ci.DrawCircle(at, 1.6f, outline, true, -1f, true);
                ci.DrawCircle(at, 1.3f, accent.Lightened(0.25f), true, -1f, true);
                ci.DrawCircle(at, 0.9f, new Color("#5a3a22"), true, -1f, true);
                ci.DrawArc(at + new Vector2(0f, side * 1.5f), 0.6f, 0f, Mathf.Tau, 8, accent.Lightened(0.25f), 0.4f, true);
                if (detail)
                    for (int j = 0; j < 2; j++)
                    {
                        float u = Mathf.PosMod(t * 0.7f + j * 0.5f, 1f);
                        ci.DrawLine(at + new Vector2(1f + u * 2.4f, -0.4f + j * 0.8f), at + new Vector2(1.6f + u * 2.4f, -0.1f + j * 0.8f + Mathf.Sin(t * 3f + j) * 0.3f), new Color(1f, 1f, 1f, 0.35f * (1f - u)), 0.35f, true);
                    }
                break;
            case HeldThing.Box:
                Gfx.RoundRect(ci, new Rect2(at.X - 1.8f, at.Y - 1.8f, 3.6f, 3.6f), new Color("#b98a52"), 0.4f, outline);
                ci.DrawLine(at + new Vector2(-1.8f, 0f), at + new Vector2(1.8f, 0f), new Color("#e3c48e"), 0.45f);
                break;
            case HeldThing.Crate:
            {
                bool produce = c.Carrying?.Kind == ItemKind.Produce;
                Gfx.RoundRect(ci, new Rect2(at.X - 2.6f, at.Y - 3.6f, 5.2f, 7.2f), produce ? new Color("#8a6a3e") : new Color("#7b6a58"), 0.6f, outline);
                if (produce)
                    for (int j = 0; j < 5; j++) ci.DrawCircle(at + new Vector2(-1.4f + (j % 3) * 1.4f, -2f + (j / 3) * 2.2f), 0.75f, new Color("#7fcf55"), true, -1f, true);
                else
                {
                    ci.DrawLine(at + new Vector2(-2.6f, -3.6f), at + new Vector2(2.6f, 3.6f), new Color("#a8957c"), 0.5f);
                    ci.DrawLine(at + new Vector2(-2.6f, 3.6f), at + new Vector2(2.6f, -3.6f), new Color("#a8957c"), 0.5f);
                    if (detail && c.Carrying is ItemStack st) ci.DrawRect(new Rect2(at.X - 0.9f, at.Y - 0.9f, 1.8f, 1.8f), Palette.Item(st.Kind));
                }
                break;
            }
            case HeldThing.Extinguisher:
            {
                // 빨간 통 (몸 옆) · 검은 호스 · 노즐은 앞으로
                var can = new Vector2(at.X - 2.4f, at.Y + side * 0.8f);
                Gfx.RoundRect(ci, new Rect2(can.X - 2.4f, can.Y - 1.2f, 4.8f, 2.4f), new Color("#d23b2f"), 1.1f, outline);
                ci.DrawRect(new Rect2(can.X + 1.8f, can.Y - 0.6f, 0.8f, 1.2f), new Color("#2a2a2a"));
                ci.DrawPolyline(new[] { can + new Vector2(2.6f, 0f), at + new Vector2(0.5f, -side * 0.6f), at + new Vector2(2.8f, 0f) }, new Color("#1c1c1c"), 0.6f, true);
                if (detail) ci.DrawRect(new Rect2(can.X - 0.6f, can.Y - 1.2f, 0.5f, 2.4f), new Color("#f5d547"));
                break;
            }
            case HeldThing.Mop:
            {
                // 걸레: 긴 자루 끝에 가닥 · 앞뒤로 민다
                float push = Mathf.Sin(t * 4f + c.Id) * 1.6f;
                var head = at + new Vector2(6.5f + push, side * 1.2f);
                ci.DrawLine(at + new Vector2(-1.5f, 0f), head, new Color("#8b6a45"), 0.8f, true);
                for (int j = -3; j <= 3; j++)
                    ci.DrawLine(head, head + new Vector2(1.6f + 0.3f * Mathf.Sin(t * 8f + j), j * 0.55f), new Color("#d7d2c4"), 0.5f, true);
                break;
            }
            case HeldThing.Plate:
                ci.DrawCircle(at, 2.5f, outline, true, -1f, true);
                ci.DrawCircle(at, 2.2f, new Color("#f1efe8"), true, -1f, true);
                ci.DrawCircle(at + new Vector2(-0.5f, -0.4f), 0.9f, new Color("#e0a040"), true, -1f, true);
                ci.DrawCircle(at + new Vector2(0.7f, 0.5f), 0.7f, new Color("#7cc05a"), true, -1f, true);
                if (detail) ci.DrawLine(at + new Vector2(-1.8f, 1.6f), at + new Vector2(1.4f, 2.6f), new Color("#c9ced6"), 0.4f, true);
                break;
            case HeldThing.MedKit:
                Gfx.RoundRect(ci, new Rect2(at.X - 1.9f, at.Y - 1.5f, 3.8f, 3f), new Color("#f4f4f2"), 0.5f, outline);
                ci.DrawRect(new Rect2(at.X - 0.3f, at.Y - 1f, 0.6f, 2f), new Color("#d33a3a"));
                ci.DrawRect(new Rect2(at.X - 1f, at.Y - 0.3f, 2f, 0.6f), new Color("#d33a3a"));
                break;
            case HeldThing.Cable:
                ci.DrawArc(at, 1.8f, 0f, Mathf.Tau, 14, new Color("#e08a2a"), 0.7f, true);
                ci.DrawArc(at, 1.1f, 0f, Mathf.Tau, 12, new Color("#e08a2a"), 0.6f, true);
                break;
            case HeldThing.Suit:
                Gfx.RoundRect(ci, new Rect2(at.X - 2.4f, at.Y - 3.4f, 4.8f, 6.8f), new Color("#dfe6ee"), 1.2f, outline);
                ci.DrawCircle(at + new Vector2(0.8f, 0f), 1.3f, new Color("#1c2a3e"), true, -1f, true);
                break;
            case HeldThing.Packet:
                Gfx.RoundRect(ci, new Rect2(at.X - 1.5f, at.Y - 1f, 3f, 2f), new Color("#c9ced6"), 0.3f, outline);
                ci.DrawLine(at + new Vector2(-1.5f, 0f), at + new Vector2(1.5f, 0f), new Color("#c9a66b"), 0.5f);
                break;
        }
    }

    private void PaintHead(CanvasItem ci, Transform2D xf, Vector2 head, CrewMember c, BodyLook? l, Color skin, Color hair, int lod, float swing, float t)
    {
        // v16.24 어두운 머리 · 피부: 어두운 윤곽 대신 밝은 테두리 (바닥과 갈린다)
        bool rim = ZoomDetail.NeedsRim(Mathf.Min(ZoomDetail.Luma(skin.R, skin.G, skin.B), ZoomDetail.Luma(hair.R, hair.G, hair.B)));
        var outline = rim ? new Color(0.82f, 0.88f, 0.98f, 0.45f) : Palette.Space.WithAlpha(0.6f);
        const float R = 3.1f;
        bool detail = lod >= 2;
        var style = l?.Style ?? HairStyle.Crop;
        float shag = l?.Shag ?? 0f;
        float uneven = l?.Uneven ?? 0f;
        float len = l?.HairCm ?? 4f;
        float sway = swing * 1.4f;
        // 등 뒤로 늘어지는 머리 (머리보다 먼저 — 어깨 위에 얹힌다)
        switch (style)
        {
            case HairStyle.Long:
            {
                float L = Mathf.Clamp(len, 12f, 70f);
                Oval(ci, xf, head + new Vector2(-3.2f - L * 0.05f, sway * 0.4f), 2.6f + L * 0.07f, 4.4f + shag * 0.6f, hair.Darkened(0.08f));
                break;
            }
            case HairStyle.Ponytail:
            {
                float L = 2.4f + Mathf.Clamp(len, 6f, 60f) * 0.11f;
                var a = head + new Vector2(-R + 0.2f, 0f);
                var b = a + new Vector2(-L, sway);
                ci.DrawLine(a, b, outline, 2.9f, true);
                ci.DrawLine(a, b, hair, 2.1f, true);
                ci.DrawCircle(b, 1.2f, hair, true, -1f, true);
                if (lod >= 1) ci.DrawCircle(a + new Vector2(-0.4f, 0f), 0.75f, Palette.Crew(c.Id), true, -1f, true); // 머리끈
                break;
            }
            case HairStyle.Braid:
            {
                int n = Mathf.Clamp((int)(len * 0.16f), 3, 9);
                for (int j = 0; j < n; j++)
                    Oval(ci, xf, head + new Vector2(-R - 0.6f - j * 1.15f, (j % 2 == 0 ? 0.35f : -0.35f) + sway * j / n), 0.75f, 0.95f, j % 2 == 0 ? hair : hair.Darkened(0.12f));
                break;
            }
            case HairStyle.Bob:
                Oval(ci, xf, head + new Vector2(-0.8f, 0f), R + 0.6f + shag * 0.5f, R + 1.6f + shag * 0.8f, hair.Darkened(0.06f));
                break;
        }
        ci.DrawCircle(head, R + 0.55f, outline, true, -1f, true);
        ci.DrawCircle(head, R, skin, true, -1f, true);
        if (detail)
        {
            Oval(ci, xf, head + new Vector2(0.1f, R - 0.1f), 0.7f, 0.5f, skin.Darkened(0.1f)); // 귀
            Oval(ci, xf, head + new Vector2(0.1f, -R + 0.1f), 0.7f, 0.5f, skin.Darkened(0.1f));
            ci.DrawCircle(head + new Vector2(R - 0.2f, 0f), 0.55f, skin.Darkened(0.12f), true, -1f, true); // 코끝
        }
        // 수염: 얼굴 앞쪽 가장자리 (길이만큼 두껍게 · 짧으면 거뭇한 그림자)
        if (l is { Stubbly: true } && l.BeardMm > 0.6f)
        {
            float th = Mathf.Clamp(l.BeardMm * 0.16f, 0.5f, 2.8f);
            var bc = l.BeardMm < 3f ? hair.WithAlpha(0.35f) : hair.Darkened(0.05f);
            ci.DrawArc(head, R - th * 0.5f + 0.3f, -1.05f, 1.05f, 12, bc, th, true);
        }
        if (style == HairStyle.Bald)
        {
            if (c.Age > 40f) ci.DrawArc(head + new Vector2(-0.4f, 0f), R - 0.3f, Mathf.Pi * 0.55f, Mathf.Pi * 1.45f, 10, hair, 1f, true); // 뒤통수 테
            if (detail) Oval(ci, xf, head + new Vector2(-0.6f, -1.2f), 1.3f, 0.7f, new Color(1f, 1f, 1f, 0.35f));
            PaintFace(ci, head, c, lod, t, 1.2f);
            return;
        }
        // 머리 덮개: 뒤로 치우친 원 (앞쪽 얼굴이 초승달로 남는다) · 길이 · 덥수룩함 · 삐뚤함이 윤곽을 바꾼다
        float back = style switch { HairStyle.Buzz => 1.3f, HairStyle.Bun or HairStyle.Ponytail or HairStyle.Braid => 2f, _ => 1.85f };
        float r0 = style switch
        {
            HairStyle.Buzz => R * 0.96f,
            HairStyle.Curly => R + 0.4f,
            _ => R - 0.05f,
        } + Mathf.Min(shag, 2f) * (style == HairStyle.Buzz ? 0.35f : 0.75f);
        var capC = head + new Vector2(-back, 0f);
        int n2 = detail ? 22 : 12;
        var pts = new Vector2[n2];
        for (int j = 0; j < n2; j++)
        {
            float a = j * Mathf.Tau / n2;
            float r = r0;
            if (shag > 0.7f) { float h = Mathf.PosMod(Mathf.Sin(j * 12.9898f + c.Id * 3.1f) * 43758.5f, 1f); r += (0.15f + 0.45f * h) * Mathf.Min(1.4f, shag - 0.5f); } // 덥수룩: 들쭉날쭉한 머리끝
            if (uneven > 0.05f && Mathf.Sin(a) > 0.2f) r -= uneven * 1.3f * Mathf.Sin(a) + (j % 3 == 0 ? uneven * 0.7f : 0f); // 삐뚤빼뚤: 한쪽이 짧고 들쭉날쭉
            pts[j] = capC + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        ci.DrawColoredPolygon(pts, style == HairStyle.Buzz ? hair.WithAlpha(0.8f) : hair);
        if (lod >= 1) ci.DrawPolyline(Close(pts), hair.Lightened(0.22f).WithAlpha(0.5f), 0.35f, true); // 머리 윤곽 (어두운 머리도 몸과 갈린다)
        switch (style)
        {
            case HairStyle.Side when lod >= 1:
                ci.DrawLine(head + new Vector2(2.4f, -1.4f), head + new Vector2(-3.6f, -1.8f), hair.Lightened(0.3f), 0.5f, true);
                break;
            case HairStyle.Curly:
                for (int j = 0; j < (detail ? 10 : 6); j++)
                {
                    float a = j * Mathf.Tau / (detail ? 10 : 6) + 0.3f;
                    ci.DrawCircle(capC + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r0 - 0.4f), 1.3f + 0.25f * Mathf.Min(shag, 2f), hair.Lightened(0.08f), true, -1f, true);
                }
                break;
            case HairStyle.Bun:
                ci.DrawCircle(head + new Vector2(-R + 0.2f, 0f), 2.3f, outline, true, -1f, true);
                ci.DrawCircle(head + new Vector2(-R + 0.2f, 0f), 1.9f, hair.Lightened(0.06f), true, -1f, true);
                if (detail) ci.DrawLine(head + new Vector2(-R - 1.6f, -1.6f), head + new Vector2(-R + 1.8f, 1.4f), new Color("#c9a86a"), 0.4f, true); // 비녀
                break;
            case HairStyle.Buzz when detail:
                for (int j = 0; j < 7; j++) ci.DrawCircle(capC + new Vector2(Mathf.Cos(j * 2.4f), Mathf.Sin(j * 2.4f)) * (j * 0.45f), 0.25f, hair.Darkened(0.25f), true, -1f, true);
                break;
        }
        if (detail && style is not HairStyle.Buzz)
        {
            // 머리카락 결 (정수리에서 뒤로)
            for (int j = -1; j <= 1; j++)
                ci.DrawLine(capC + new Vector2(1.6f, j * 0.8f), capC + new Vector2(-r0 * 0.7f, j * 1.1f), hair.Lightened(0.12f).WithAlpha(0.3f), 0.3f, true);
        }
        PaintFace(ci, head, c, lod, t, -back + r0 + 0.1f);
    }

    /// <summary>
    /// v16.24 가까이: 표정 — 머리털 앞 얼굴(앞쪽 초승달)에 눈 · 눈썹 · 입. 감정 · 몸에서 읽는다 (ZoomDetail.Face):
    /// 평온(점 눈 · 작은 입) · 웃음(둥근 입) · 걱정(처진 눈썹) · 두려움(크게 뜬 눈 · 벌린 입) · 아픔(질끈 감은 눈) · 피곤(반쯤 감은 눈) ·
    /// 슬픔(처진 입) · 화(모인 눈썹) · 잠(감은 눈). 가끔 눈을 깜빡인다.
    /// </summary>
    private void PaintFace(CanvasItem ci, Vector2 head, CrewMember c, int lod, float t, float browX)
    {
        if (!ZoomDetail.Draws((ZoomTier)lod).HasFlag(Detail.Face)) return;
        var e = ZoomDetail.Face(_world, c);
        var ink = new Color(0.07f, 0.05f, 0.05f, 0.92f);
        float ex = Mathf.Max(1.75f, browX + 0.45f), ey = 0.95f;
        var eyeL = head + new Vector2(ex, -ey);
        var eyeR = head + new Vector2(ex, ey);
        bool blink = Mathf.PosMod(t * 0.27f + c.Id * 0.37f, 1f) < 0.035f;
        void Closed(Vector2 p) => ci.DrawLine(p - new Vector2(0f, 0.38f), p + new Vector2(0f, 0.38f), ink, 0.32f, true);
        switch (e)
        {
            case FaceLook.Asleep:
            case FaceLook.Tired when blink:
                Closed(eyeL); Closed(eyeR);
                break;
            case FaceLook.Tired:
                ci.DrawLine(eyeL - new Vector2(0f, 0.38f), eyeL + new Vector2(0f, 0.38f), ink, 0.42f, true);
                ci.DrawLine(eyeR - new Vector2(0f, 0.38f), eyeR + new Vector2(0f, 0.38f), ink, 0.42f, true);
                ci.DrawCircle(eyeL + new Vector2(0.15f, 0f), 0.16f, ink, true, -1f, true);
                ci.DrawCircle(eyeR + new Vector2(0.15f, 0f), 0.16f, ink, true, -1f, true);
                break;
            case FaceLook.Pain:
                // 질끈: > <
                foreach (var (p, k) in new[] { (eyeL, -1f), (eyeR, 1f) })
                {
                    ci.DrawLine(p + new Vector2(-0.3f, -0.35f * k), p + new Vector2(0.2f, 0f), ink, 0.3f, true);
                    ci.DrawLine(p + new Vector2(0.2f, 0f), p + new Vector2(-0.3f, 0.35f * k), ink, 0.3f, true);
                }
                break;
            case FaceLook.Fear:
                foreach (var p in new[] { eyeL, eyeR })
                {
                    ci.DrawCircle(p, 0.42f, new Color(0.97f, 0.97f, 0.95f), true, -1f, true);
                    ci.DrawCircle(p + new Vector2(0.12f, 0f), 0.2f, ink, true, -1f, true);
                }
                break;
            default:
                if (blink) { Closed(eyeL); Closed(eyeR); }
                else { ci.DrawCircle(eyeL, 0.27f, ink, true, -1f, true); ci.DrawCircle(eyeR, 0.27f, ink, true, -1f, true); }
                break;
        }
        // 눈썹 (화 · 걱정 · 두려움만 — 기울기로)
        if (e is FaceLook.Angry or FaceLook.Worry or FaceLook.Fear)
        {
            float tilt = e == FaceLook.Angry ? 0.35f : -0.3f; // 화: 안쪽이 앞으로 · 걱정: 안쪽이 뒤로
            foreach (float k in new[] { -1f, 1f })
            {
                var inner = head + new Vector2(ex - 0.55f + tilt, k * 0.45f);
                var outer = head + new Vector2(ex - 0.55f - tilt * 0.3f, k * 1.35f);
                ci.DrawLine(inner, outer, ink, 0.3f, true);
            }
        }
        // 입 (앞쪽 가장자리)
        var mouth = head + new Vector2(2.55f, 0f);
        switch (e)
        {
            case FaceLook.Smile: ci.DrawArc(head + new Vector2(1.85f, 0f), 0.85f, -0.85f, 0.85f, 8, ink, 0.3f, true); break;
            case FaceLook.Sad: ci.DrawArc(head + new Vector2(3.35f, 0f), 0.8f, Mathf.Pi - 0.7f, Mathf.Pi + 0.7f, 8, ink, 0.3f, true); break;
            case FaceLook.Fear: ci.DrawCircle(mouth, 0.33f, ink, true, -1f, true); break;
            case FaceLook.Pain:
            case FaceLook.Angry: ci.DrawLine(mouth - new Vector2(0f, 0.55f), mouth + new Vector2(0f, 0.55f), ink, 0.34f, true); break;
            case FaceLook.Asleep: break;
            default: ci.DrawLine(mouth - new Vector2(0f, 0.35f), mouth + new Vector2(0f, 0.35f), ink.WithAlpha(0.7f), 0.26f, true); break;
        }
    }

    private static void PaintHelmet(CanvasItem ci, Transform2D xf, Vector2 head, Color skin, Color accent, bool detail, float t, CrewMember c)
    {
        bool low = c.Suit is SuitState su && su.Oxygen < 0.75f;
        float blink = low ? 0.5f + 0.5f * Mathf.Sin(t * 8f) : 1f;
        ci.DrawCircle(head, 5.2f, Palette.Space.WithAlpha(0.7f), true, -1f, true);
        ci.DrawCircle(head, 4.8f, (low ? Palette.Warning : new Color("#dfe6ee")).WithAlpha(0.95f * blink), true, -1f, true);
        ci.DrawCircle(head + new Vector2(0.6f, 0f), 3.7f, new Color("#1c2a3e"), true, -1f, true);
        ci.DrawCircle(head + new Vector2(0.2f, 0f), 2.2f, skin.WithAlpha(0.28f), true, -1f, true); // 유리 너머 얼굴
        ci.DrawArc(head + new Vector2(0.6f, 0f), 2.9f, -1.2f, -0.2f, 8, new Color(1f, 1f, 1f, 0.6f), 0.8f, true);
        if (detail)
        {
            ci.DrawCircle(head + new Vector2(-2.8f, -3.2f), 0.6f, accent, true, -1f, true); // 개인 색 헬멧 등
            ci.DrawCircle(head + new Vector2(-2.8f, 3.2f), 0.6f, accent, true, -1f, true);
        }
    }

    /// <summary>누운 인형 (잠 · 쓰러짐 · 숨짐): 머리 · 머리카락 · 몸통 · 팔이 몸을 따라.</summary>
    private void PaintPuppetLying(CanvasItem ci, CrewMember c, Vector2 at, float s, bool dead, bool blanket)
    {
        var l = _world.Body2.Peek(c);
        var xf = new Transform2D(blanket ? -Mathf.Pi / 2f : Mathf.Pi, new Vector2(s, s), 0f, at);
        ci.DrawSetTransformMatrix(xf);
        var skin = dead ? new Color("#8a8f99") : SkinColor(l, c.Id);
        var hair = dead ? new Color("#5a5f69") : HairColor(l);
        var cloth = dead ? new Color("#5a5f69") : c.Suit != null ? new Color("#dfe6ee") : RoleCloth(c.Role);
        var outline = Palette.Space.WithAlpha(0.85f);
        float wf = Mathf.Clamp(0.86f + ((l?.Bmi ?? 23f) - 22f) * 0.03f, 0.82f, 1.3f);
        if (!blanket)
        {
            for (int k = -1; k <= 1; k += 2)
            {
                ci.DrawLine(new Vector2(-2f, k * 2f), new Vector2(-11f, k * 2.6f), outline, 3.8f, true);
                ci.DrawLine(new Vector2(-2f, k * 2f), new Vector2(-11f, k * 2.6f), cloth.Darkened(0.4f), 2.8f, true);
            }
            Oval(ci, xf, new Vector2(0f, 0f), 6.4f, 5.6f * wf, outline);
            Oval(ci, xf, new Vector2(0f, 0f), 5.8f, 5f * wf, cloth);
            for (int k = -1; k <= 1; k += 2)
            {
                var st = Puppet.Arm(c, k > 0 ? BodyPart.RightArm : BodyPart.LeftArm);
                if (st == ArmState.Lost) continue;
                ci.DrawLine(new Vector2(3f, k * 5.6f * wf), new Vector2(-3.4f, k * 6.4f * wf), cloth.Darkened(0.1f), 2.4f, true);
                ci.DrawCircle(new Vector2(-3.8f, k * 6.4f * wf), 1.2f, skin, true, -1f, true);
            }
        }
        var head = new Vector2(blanket ? 6.5f : 8.6f, 0f);
        if (l != null && l.Style is HairStyle.Long or HairStyle.Bob or HairStyle.Braid or HairStyle.Ponytail && !dead)
            Oval(ci, xf, head + new Vector2(1.6f, 0f), 3.2f + Mathf.Min(l.HairCm, 50f) * 0.05f, 4.8f, hair.Darkened(0.1f)); // 베개에 퍼진 머리
        ci.DrawCircle(head, 4.8f, outline, true, -1f, true);
        ci.DrawCircle(head, 4.3f, skin, true, -1f, true);
        if (l == null || l.Style != HairStyle.Bald)
        {
            float r0 = 4.4f + Mathf.Min(l?.Shag ?? 0f, 2f) * 0.6f;
            Oval(ci, xf, head + new Vector2(1.2f, 0f), r0 * 0.8f, r0, hair);
        }
        if (l is { Stubbly: true } && l.BeardMm > 2f) ci.DrawArc(head, 3.6f, Mathf.Pi - 1f, Mathf.Pi + 1f, 10, hair.WithAlpha(0.6f), Mathf.Clamp(l.BeardMm * 0.15f, 0.5f, 2.4f), true);
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>정비 통로를 기는 인형: 엎드린 몸 · 팔꿈치와 무릎이 번갈아 · 머리카락 · 머리등.</summary>
    private void PaintPuppetCrawl(CanvasItem ci, CrewMember c, Vector2 p, float s)
    {
        var l = _world.Body2.Peek(c);
        var f = c.Facing.ToGodot();
        float ang = f.LengthSquared() > 1e-4f ? f.Angle() : 0f;
        var xf = new Transform2D(ang, new Vector2(s, s), 0f, p);
        ci.DrawSetTransformMatrix(xf);
        var cloth = RoleCloth(c.Role);
        var skin = SkinColor(l, c.Id);
        var hair = HairColor(l);
        var outline = Palette.Space.WithAlpha(0.85f);
        float stroke = Mathf.Sin(_time * 7f + c.Id);
        for (int k = -1; k <= 1; k += 2)
        {
            // 무릎 (뒤) · 팔꿈치 (앞) — 번갈아 나간다
            var hip = new Vector2(-6f, k * 2.6f);
            var knee = hip + new Vector2(-3.6f + 2.2f * stroke * k, k * 1.6f);
            ci.DrawLine(hip, knee, outline, 3.4f, true); ci.DrawLine(hip, knee, cloth.Darkened(0.42f), 2.5f, true);
            var sh = new Vector2(2.4f, k * 4.2f);
            var elbow = sh + new Vector2(2.8f - 2.2f * stroke * k, k * 2.2f);
            var hand = elbow + new Vector2(2.4f, -k * 1.4f);
            ci.DrawPolyline(new[] { sh, elbow, hand }, outline, 3.2f, true);
            ci.DrawPolyline(new[] { sh, elbow, hand }, cloth, 2.3f, true);
            ci.DrawCircle(hand, 1.2f, skin, true, -1f, true);
        }
        Oval(ci, xf, new Vector2(-2f, 0f), 6.6f, 4.8f, outline);
        Oval(ci, xf, new Vector2(-2f, 0f), 6f, 4.2f, cloth);
        var head = new Vector2(6.6f, 0f);
        ci.DrawCircle(head, 4.3f, outline, true, -1f, true);
        ci.DrawCircle(head, 3.8f, skin, true, -1f, true);
        if (l == null || l.Style != HairStyle.Bald) Oval(ci, xf, head + new Vector2(-0.8f, 0f), 3.6f + Mathf.Min(l?.Shag ?? 0f, 2f) * 0.5f, 3.9f + Mathf.Min(l?.Shag ?? 0f, 2f) * 0.5f, hair);
        ci.DrawCircle(head + new Vector2(2.6f, 0f), 1.1f, new Color(1f, 0.95f, 0.7f), true, -1f, true); // 머리등
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>바닥에 떨어진 머리카락 (자른 사람 · 머리색 · 길이만큼).</summary>
    private void PaintHairClips(CanvasItem ci)
    {
        var clips = _world.Body2.Clips;
        if (clips.Count == 0 || Zoom < 0.7f) return;
        foreach (var x in clips)
        {
            var c0 = ToPx(x.Cell.Center);
            var col = HairTones[Mathf.Clamp(x.Color, 0, HairTones.Length - 1)];
            int n = Mathf.Clamp((int)(x.Cm * 2f), 4, 18);
            uint h = (uint)(x.Id * 2654435761u);
            for (int j = 0; j < n; j++)
            {
                h = h * 1664525u + 1013904223u;
                float a = (h & 0xffff) / 65535f * Mathf.Tau;
                float r = ((h >> 16) & 0xff) / 255f * 11f;
                var q = c0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                float la = 1.5f + Mathf.Min(x.Cm, 20f) * 0.12f;
                ci.DrawLine(q, q + new Vector2(Mathf.Cos(a * 3f), Mathf.Sin(a * 3f)) * la, col.WithAlpha(0.8f), 0.8f, true);
            }
        }
    }
}
