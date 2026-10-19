using System;

namespace ShipSim.Art;

// 도트 사람 — 앞(남) · 옆(동, 서는 뒤집기) · 뒤(북) 네 방향, 걷기 4장면 · 앉기 · 일하기 · 들기 · 웅크리기.
//   한눈에: 성별(어깨 · 허리 · 머리) · 나이(아이는 키 60%에 큰 머리 · 청소년은 가늘게 · 노인은 흰머리 · 주름 · 지팡이) ·
//   체격(마름 · 보통 · 통통 · 단단) · 사람마다(피부 · 머리 모양과 색 · 수염 · 안경 · 직업복 색 · 개인 색 목깃 · 무늬).
//   몸 상태: 붕대 · 안대 · 팔걸이 · 깁스 · 목발 · 피 · 화상 · 잃은 팔다리 · 의수 · 동상 · 앓음 · 빠진 머리 · 멍 · 땀.
//   틀 24×40, 발바닥 = 37줄, 가운데 = 12 (칸 경계).

public enum DotAge : byte { Child, Teen, Adult, Middle, Elder }
public enum DotBuild : byte { Thin, Normal, Heavy, Strong }
public enum DotHair : byte { Buzz, Crop, Side, Curly, Bob, Long, Ponytail, Bun, Braid, Bald, Receding }
public enum DotFacing : byte { S, E, N, W }
public enum DotPose : byte { Stand, Walk, Sit, Work, Carry, Cower, Kneel }
public enum DotHeld : byte { None, Wrench, Welder, Tablet, Cup, Box, Extinguisher, Mop, MedKit, Plate, Cable }
/// <summary>표정 (그래픽 결정 6): 덤덤 · 웃음 · 겁 · 아픔 · 슬픔 — 마음(감정) · 몸(통증)에서 고른다.</summary>
public enum DotFace : byte { Calm, Smile, Fear, Pain, Sad }
/// <summary>생활 동작 (그래픽 결정 10): 손을 입으로(먹고 마시기) · 공구 돌리기 · 말하며 손짓 · 졸며 고개 끄덕임.</summary>
public enum DotMotion : byte { None, Drink, Tool, Talk, Doze }

[Flags]
public enum DotHurt : uint
{
    None = 0,
    HeadWrap = 1 << 0, EyePatch = 1 << 1, SlingL = 1 << 2, CastArmR = 1 << 3, CastLegL = 1 << 4, TorsoWrap = 1 << 5,
    Bleed = 1 << 6, Burn = 1 << 7, NoArmL = 1 << 8, MetalArmR = 1 << 9, NoLegR = 1 << 10, Frost = 1 << 11,
    Sick = 1 << 12, RadHair = 1 << 13, Bruise = 1 << 14, Sweat = 1 << 15, MetalLegL = 1 << 16,
    NoArmR = 1 << 17, MetalArmL = 1 << 18, CastArmL = 1 << 19, SlingR = 1 << 20, CastLegR = 1 << 21, NoLegL = 1 << 22, MetalLegR = 1 << 23,
}

/// <summary>사람 하나의 겉모습 (시뮬레이션의 BodyLook · 나이 · 직업 · 다친 곳에서 만든다).</summary>
public sealed class PersonLook
{
    public bool Female;
    public DotAge Age = DotAge.Adult;
    public DotBuild Build = DotBuild.Normal;
    public int Height;               // −2 ~ +2 도트
    public int Skin;                 // 0 ~ 5
    public int HairRamp = Ramps.HairBrown;
    public DotHair Hair = DotHair.Crop;
    public int Cloth = Ramps.ClothBeige;
    public int Accent = Ramps.ClothRed;
    public int Pattern;              // 0 민무늬 · 1 줄무늬 · 2 소매 · 3 조끼 · 4 목도리 · 5 덧댄 천
    public int Badge;                // 0 없음 · 1 기관(노랑 네모) · 2 의무(붉은 십자) · 3 조종(날개) · 4 정비(공구) · 5 재배(잎) · 6 전기(번개)
    public bool Beard, Mustache, Stubble, Glasses, Cane, Casual;
    /// <summary>우주복 (흰 몸 · 개인 색 띠 · 헬멧 유리) · 눈 감음(잠 · 쓰러짐) · 숨짐(잿빛 피부).</summary>
    public bool Suit, EyesClosed, Dead;
    public DotHurt Hurt;
    public DotHeld Held;
    public DotFace Face;
    public DotMotion Motion;
    /// <summary>여덟 장 동작 (그래픽 결정 9 — 걷기 한 걸음씩 · 손 · 고개가 장마다) — 끄면 예전 네 장.</summary>
    public bool Cycle8;
    public int Seed;
}

public static class DotPeople
{
    public const int FW = 24, FH = 40, Foot = 37, Cx = 12;

    private sealed class Body
    {
        public int Shoe, Leg, Torso, HeadRows, Shoulder, Waist, Hip, ArmW, LegW, Gap;
        public int Bob;          // 몸 전체를 아래로
        public int Hunch;        // 노인: 옆에서 머리가 앞으로
        public int HeadTop, TorsoTop, LegTop;
        public int SkinRamp;
        public int[] Head = Array.Empty<int>();
    }

    /// <summary>걷기 한 장: 보폭 (옆) · 몸이 내려앉음 · 팔 흔들기 · 앞에서 본 두 발 들기. 네 장(옛)은 1 · 3장에 한 발씩 · 여덟 장은 보폭이 0 → 2 → 0 → −2로 이어진다.</summary>
    private static (int stride, int bob, int liftL, int liftR) Gait(PersonLook l, int frame)
    {
        if (!l.Cycle8) return (frame == 1 ? 2 : frame == 3 ? -2 : 0, (frame & 1) == 1 ? 1 : 0, frame == 1 ? 1 : 0, frame == 3 ? 1 : 0);
        int f = frame & 7;
        int stride = f switch { 1 => 1, 2 => 2, 3 => 1, 5 => -1, 6 => -2, 7 => -1, _ => 0 };
        int bob = f is 1 or 2 or 5 or 6 ? 1 : 0;
        return (stride, bob, f is 1 or 2 or 3 ? 1 : 0, f is 5 or 6 or 7 ? 1 : 0);
    }

    /// <summary>생활 동작의 이 장: 손이 입에 (마시기 · 먹기 — 여덟 장 중 3~6) · 공구 손 높낮이 · 손짓 (0~3) · 고개 떨굼 (4~7).</summary>
    private static bool ToMouth(PersonLook l, int frame) => l.Motion == DotMotion.Drink && (frame & 7) is >= 3 and <= 6;
    private static int ToolBob(PersonLook l, int frame) => l.Motion == DotMotion.Tool ? (frame & 3) switch { 1 => -1, 2 => -2, 3 => -1, _ => 0 } : 0;
    private static bool Gesture(PersonLook l, int frame) => l.Motion == DotMotion.Talk && (frame & 7) < 4;
    private static bool Nod(PersonLook l, int frame) => l.Motion == DotMotion.Doze && (frame & 7) >= 4;

    private static Body Measure(PersonLook l, DotPose pose, int frame)
    {
        var b = new Body { Shoe = 2, ArmW = 3, Gap = 2 };
        bool f = l.Female;
        switch (l.Age)
        {
            case DotAge.Child:
                b.Leg = 4; b.Torso = 6; b.HeadRows = 10; b.Shoulder = 4; b.Waist = 4; b.Hip = 4; b.ArmW = 2; b.Shoe = 2;
                b.Head = new[] { 3, 5, 5, 6, 6, 6, 6, 5, 4, 3 };
                break;
            case DotAge.Teen:
                b.Leg = 6; b.Torso = 8; b.HeadRows = 11; b.Shoulder = f ? 4 : 5; b.Waist = 4; b.Hip = f ? 5 : 4; b.ArmW = 2;
                b.Head = new[] { 3, 5, 6, 6, 6, 6, 6, 6, 5, 4, 3 };
                break;
            default:
                b.Leg = 6; b.Torso = 9; b.HeadRows = 11; b.Shoulder = f ? 5 : 6; b.Waist = f ? 4 : 5; b.Hip = 5;
                b.Head = new[] { 3, 5, 6, 6, 6, 6, 6, 6, 5, 4, 3 };
                break;
        }
        if (l.Age == DotAge.Elder) { b.Leg = 5; b.Hunch = 1; }
        b.Leg += Math.Clamp(l.Height, -2, 2) / 2 + (l.Height >= 2 ? 1 : 0) - (l.Height <= -2 ? 0 : 0);
        switch (l.Build)
        {
            case DotBuild.Thin: b.Waist = Math.Max(3, b.Waist - 1); if (b.Shoulder > 4) b.Shoulder--; break;
            case DotBuild.Heavy: b.Waist++; b.Hip++; break;
            case DotBuild.Strong: b.Shoulder++; break;
        }
        if (l.Age == DotAge.Middle && l.Build != DotBuild.Thin) b.Waist = Math.Max(b.Waist, b.Shoulder - 1 + (l.Build == DotBuild.Heavy ? 1 : 0));
        b.LegW = b.Hip - b.Gap / 2;
        if (pose == DotPose.Walk) b.Bob = Gait(l, frame).bob;
        if (pose == DotPose.Cower) b.Bob = 2;
        if (pose == DotPose.Sit) b.Bob = 0;
        b.SkinRamp = l.Dead ? Ramps.DeadSkin : (l.Hurt & DotHurt.Sick) != 0 ? Ramps.SickSkin : Ramps.Skin[Math.Clamp(l.Skin, 0, Ramps.Skin.Length - 1)];
        int legShown = pose == DotPose.Sit ? 3 : pose == DotPose.Kneel ? 2 : pose == DotPose.Cower ? b.Leg - 2 : b.Leg;
        b.LegTop = Foot - b.Shoe - legShown + 1;
        b.TorsoTop = b.LegTop - b.Torso + (pose == DotPose.Cower ? 0 : 0);
        b.HeadTop = b.TorsoTop - 1 - b.HeadRows;
        b.LegTop += b.Bob; b.TorsoTop += b.Bob; b.HeadTop += b.Bob;
        if (Nod(l, frame)) b.HeadTop += 1;   // 졸며 고개가 떨어진다
        return b;
    }

    /// <summary>앞모습(서 있음)에서 부위 자리 (건강 카드의 점): 머리 · 가슴 · 폐 · 왼팔 · 오른팔 · 왼다리 · 오른다리.
    /// 앞에서 본 그림이라 그 사람의 왼쪽은 화면 오른쪽.</summary>
    public static (int x, int y)[] Anchors(PersonLook l)
    {
        var b = Measure(l, DotPose.Stand, 0);
        int arm = b.Shoulder + 1, leg = b.Hip / 2 + 1, armY = b.TorsoTop + b.Torso / 2, legY = b.LegTop + (Foot - b.LegTop) / 2;
        return new[]
        {
            (Cx, b.HeadTop + 4), (Cx - 2, b.TorsoTop + 3), (Cx + 2, b.TorsoTop + 4),
            (Cx + arm, armY), (Cx - arm - 1, armY), (Cx + leg - 1, legY), (Cx - leg, legY),
        };
    }

    private static readonly (DotHurt l, DotHurt r)[] Pairs =
    {
        (DotHurt.NoArmL, DotHurt.NoArmR), (DotHurt.MetalArmL, DotHurt.MetalArmR), (DotHurt.CastArmL, DotHurt.CastArmR), (DotHurt.SlingL, DotHurt.SlingR),
        (DotHurt.CastLegL, DotHurt.CastLegR), (DotHurt.NoLegL, DotHurt.NoLegR), (DotHurt.MetalLegL, DotHurt.MetalLegR),
    };

    /// <summary>왼쪽 · 오른쪽 상처를 맞바꾼다.</summary>
    public static DotHurt SwapSides(DotHurt h)
    {
        var o = h;
        foreach (var (a, c) in Pairs) { o &= ~(a | c); if ((h & a) != 0) o |= c; if ((h & c) != 0) o |= a; }
        return o;
    }

    /// <summary>한 장면을 그린다 (마무리 Bevel까지 — 외곽선은 ToImg에서).</summary>
    public static Dot Draw(PersonLook l, DotFacing face, DotPose pose = DotPose.Stand, int frame = 0)
    {
        if (face == DotFacing.W)
        {
            // 서쪽: 동쪽 그림을 뒤집는다 — 가까운 쪽이 왼쪽이 되도록 좌우 상처를 바꿔 그린다
            var keep = l.Hurt;
            l.Hurt = SwapSides(keep);
            try { return Draw(l, DotFacing.E, pose, frame).Flip(); }
            finally { l.Hurt = keep; }
        }
        var d = new Dot(FW, FH);
        var b = Measure(l, pose, frame);
        switch (face)
        {
            case DotFacing.S: Front(d, l, b, pose, frame, back: false); break;
            case DotFacing.N: Front(d, l, b, pose, frame, back: true); break;
            default: Side(d, l, b, pose, frame); break;
        }
        d.Bevel();
        Marks(d, l, b, face, pose, frame);
        return d;
    }

    // ───────────────────────────── 앞 · 뒤 ─────────────────────────────

    private static void Front(Dot d, PersonLook l, Body b, DotPose pose, int frame, bool back)
    {
        int skin = b.SkinRamp;
        bool child = l.Age == DotAge.Child;
        int cloth = l.Suit ? Ramps.SuitWhite : child || l.Casual ? l.Accent : l.Cloth;
        int pants = l.Suit ? Ramps.SuitWhite : child ? Ramps.ClothNavy : l.Casual ? Ramps.ClothNavy : l.Cloth;
        int pantShade = pants == l.Cloth ? 1 : 2;

        // 1 뒤 머리카락 (긴 머리는 어깨 뒤로)
        d.Part = 1;
        if (!back) BackHair(d, l, b);

        // 2~5 다리 · 신발
        // 화면 왼쪽(VL) · 오른쪽(VR) 다리: 앞모습이면 그 사람의 오른다리가 화면 왼쪽
        bool Has(DotHurt f) => (l.Hurt & f) != 0;
        bool noVL = Has(back ? DotHurt.NoLegL : DotHurt.NoLegR), noVR = Has(back ? DotHurt.NoLegR : DotHurt.NoLegL);
        bool castVL = Has(back ? DotHurt.CastLegL : DotHurt.CastLegR), castVR = Has(back ? DotHurt.CastLegR : DotHurt.CastLegL);
        bool metalVL = Has(back ? DotHurt.MetalLegL : DotHurt.MetalLegR), metalVR = Has(back ? DotHurt.MetalLegR : DotHurt.MetalLegL);
        var gait = Gait(l, frame);
        int lift = pose == DotPose.Walk ? gait.liftL : 0, liftR = pose == DotPose.Walk ? gait.liftR : 0;
        if (back) (lift, liftR) = (liftR, lift);
        int legRows = Foot - b.Shoe + 1 - b.LegTop;
        int lx = Cx - b.Hip, rx = Cx + b.Hip - b.LegW;
        if (pose == DotPose.Sit)
        {
            // 앉음: 허벅지는 앞으로 접혀 보이지 않고, 무릎 · 정강이 · 신발 (무릎이 조금 벌어진다)
            d.Part = 2; if (!noVL) d.Rect(lx - 1, b.LegTop, b.LegW + 1, legRows, pants, pantShade);
            d.Part = 3; if (!noVR) d.Rect(rx, b.LegTop, b.LegW + 1, legRows, pants, pantShade);
        }
        else
        {
            d.Part = 2;
            if (noVL) d.Rect(lx, b.LegTop, b.LegW, 3, pants, pantShade); // 접어 핀으로 꽂은 바짓단
            else d.Rect(lx, b.LegTop, b.LegW, legRows - lift, pants, pantShade);
            d.Part = 3;
            if (noVR) d.Rect(rx, b.LegTop, b.LegW, 3, pants, pantShade);
            else d.Rect(rx, b.LegTop, b.LegW, legRows - liftR, pants, pantShade);
        }
        // 가랑이 위 두 줄은 이어진다
        d.Part = 3; d.Rect(lx, b.LegTop, b.Hip * 2, Math.Min(2, legRows), pants, pantShade);
        if (child) { d.Part = 3; if (!noVL) d.Rect(lx, b.LegTop + 2, b.LegW, Math.Max(0, legRows - 2 - lift), skin, 3); if (!noVR) d.Rect(rx, b.LegTop + 2, b.LegW, Math.Max(0, legRows - 2 - liftR), skin, 3); }
        d.Part = 3;
        if (castVL && !noVL) d.Rect(lx, b.LegTop + 2, b.LegW, legRows - 2 - lift, Ramps.Bandage, 2);
        if (castVR && !noVR) d.Rect(rx, b.LegTop + 2, b.LegW, legRows - 2 - liftR, Ramps.Bandage, 2);
        if (metalVL && pose != DotPose.Sit) d.Rect(lx + 1, b.LegTop + 2, b.LegW - 2, legRows - 2 - lift, Ramps.Steel, 4);
        if (metalVR && pose != DotPose.Sit) d.Rect(rx + 1, b.LegTop + 2, b.LegW - 2, legRows - 2 - liftR, Ramps.Steel, 4);
        int shoe = Ramps.Rubber, sy = Foot - b.Shoe + 1;
        d.Part = 4; if (!noVL) d.Rect(lx - 1, sy - lift, b.LegW + 1, b.Shoe, shoe, 2);
        d.Part = 5; if (!noVR) d.Rect(rx, sy - liftR, b.LegW + 1, b.Shoe, shoe, 2);
        if (noVL) d.Mark(lx + b.LegW / 2, b.LegTop + 2, Ramps.Steel, 5);
        if (noVR) d.Mark(rx + b.LegW / 2, b.LegTop + 2, Ramps.Steel, 5);

        // 6 목
        d.Part = 6; d.Rect(Cx - 2, b.TorsoTop - 1, 4, 2, skin, 2);

        // 7 몸통 (어깨 → 허리 → 엉덩이)
        d.Part = 7;
        var rows = new int[b.Torso];
        for (int i = 0; i < b.Torso; i++)
        {
            float t = b.Torso <= 1 ? 0 : i / (float)(b.Torso - 1);
            int hw = i == 0 ? b.Shoulder - 1 : t < 0.6f ? (int)MathF.Round(b.Shoulder + (b.Waist - b.Shoulder) * (t / 0.6f)) : (int)MathF.Round(b.Waist + (b.Hip - b.Waist) * ((t - 0.6f) / 0.4f));
            if (l.Build == DotBuild.Heavy && t is > 0.35f and < 0.85f) hw++;
            rows[i] = hw;
        }
        d.Rows(Cx, b.TorsoTop, rows, cloth, 2);
        TorsoDress(d, l, b, cloth, back);

        // 8 · 9 팔
        Arms(d, l, b, pose, frame, cloth, skin, back);

        // 10 머리
        d.Part = 20;
        d.Rows(Cx, b.HeadTop, b.Head, skin, 3);
        if (!back)
        {
            // 귀
            d.Set(Cx - 7, b.HeadTop + 5, skin, 2); d.Set(Cx + 6, b.HeadTop + 5, skin, 2);
            d.Set(Cx - 7, b.HeadTop + 6, skin, 2); d.Set(Cx + 6, b.HeadTop + 6, skin, 2);
        }
        else
        {
            d.Set(Cx - 7, b.HeadTop + 5, skin, 2); d.Set(Cx + 6, b.HeadTop + 5, skin, 2);
        }
        // 11 앞 머리카락 (뒤에서는 뒤통수 전체)
        d.Part = 22;
        if (back) BackOfHead(d, l, b); else FrontHair(d, l, b);
        if (!back && (l.Beard || l.Mustache)) { d.Part = 24; Beard(d, l, b); }

        // 덧붙는 것 (붕대 · 지팡이 · 목발)
        if (l.Suit) Helmet(d, l, b, back ? DotFacing.N : DotFacing.S);
        Overlays(d, l, b, pose, frame, back);
        Held(d, l, b, pose, frame, DotFacing.S);
    }

    private static void TorsoDress(Dot d, PersonLook l, Body b, int cloth, bool back)
    {
        int top = b.TorsoTop, n = b.Torso;
        bool child = l.Age == DotAge.Child;
        // 허리띠
        if (!child && !l.Casual)
        {
            int by = top + n - 3;
            int hw = Math.Max(b.Waist, 3);
            for (int x = Cx - hw - 1; x <= Cx + hw; x++) d.Mark(x, by, Ramps.Panel, 3);
            if (!back) { d.Mark(Cx - 1, by, Ramps.Hazard, 3); d.Mark(Cx, by, Ramps.Hazard, 2); }
        }
        if (back) { d.Mark(Cx - 2, top, l.Accent, 2); d.Mark(Cx + 1, top, l.Accent, 2); return; }
        // 목깃 (개인 색) · 브이넥
        d.Mark(Cx - 3, top, l.Accent, 3); d.Mark(Cx + 2, top, l.Accent, 2);
        d.Mark(Cx - 2, top + 1, l.Accent, 2); d.Mark(Cx + 1, top + 1, l.Accent, 1);
        d.Mark(Cx - 2, top, b.SkinRamp, 2); d.Mark(Cx - 1, top, b.SkinRamp, 2); d.Mark(Cx, top, b.SkinRamp, 2); d.Mark(Cx + 1, top, b.SkinRamp, 1);
        d.Mark(Cx - 1, top + 1, b.SkinRamp, 2); d.Mark(Cx, top + 1, b.SkinRamp, 1);
        if (!child && !l.Casual)
        {
            // 앞 지퍼
            for (int y = top + 2; y < top + n - 3; y++) d.Mark(Cx - 1, y, cloth, 1);
        }
        switch (l.Pattern)
        {
            case 1: for (int x = Cx - 7; x < Cx + 7; x++) { d.Mark(x, top + 4, l.Accent, 2); if (child) d.Mark(x, top + 2, l.Accent, 3); } break;
            case 3: for (int y = top + 1; y < top + n - 2; y++) { d.Mark(Cx - 3, y, l.Accent, 1); d.Mark(Cx - 4, y, l.Accent, 1); d.Mark(Cx + 2, y, l.Accent, 1); d.Mark(Cx + 3, y, l.Accent, 1); } break;
            case 4:
                for (int x = Cx - 3; x < Cx + 3; x++) { d.Mark(x, top, l.Accent, 3); d.Mark(x, top + 1, l.Accent, 2); }
                d.Mark(Cx + 1, top + 2, l.Accent, 2); d.Mark(Cx + 1, top + 3, l.Accent, 2); d.Mark(Cx + 1, top + 4, l.Accent, 1);
                break;
            case 5: d.Mark(Cx - 4, top + 5, Ramps.ClothBrown, 2); d.Mark(Cx - 3, top + 5, Ramps.ClothBrown, 3); d.Mark(Cx - 4, top + 6, Ramps.ClothBrown, 2); d.Mark(Cx - 3, top + 6, Ramps.ClothBrown, 2); break;
        }
        // 직업 표지 (가슴 오른쪽)
        int bx = Cx + 2, byy = top + 2;
        switch (l.Badge)
        {
            case 1: d.Mark(bx, byy, Ramps.Hazard, 3); d.Mark(bx + 1, byy, Ramps.Hazard, 4); d.Mark(bx, byy + 1, Ramps.Hazard, 2); d.Mark(bx + 1, byy + 1, Ramps.Hazard, 3); break;
            case 2: d.Mark(bx + 1, byy, Ramps.Alarm, 3); d.Mark(bx, byy + 1, Ramps.Alarm, 3); d.Mark(bx + 1, byy + 1, Ramps.Alarm, 4); d.Mark(bx + 2, byy + 1, Ramps.Alarm, 3); d.Mark(bx + 1, byy + 2, Ramps.Alarm, 3); break;
            case 3: d.Mark(bx, byy, Ramps.Hazard, 4); d.Mark(bx + 1, byy + 1, Ramps.Hazard, 3); d.Mark(bx + 2, byy, Ramps.Hazard, 4); break;
            case 4: d.Mark(bx, byy, Ramps.Steel, 5); d.Mark(bx + 1, byy + 1, Ramps.Steel, 4); d.Mark(bx + 2, byy + 2, Ramps.Steel, 4); break;
            case 5: d.Mark(bx + 1, byy, Ramps.Leaf, 4); d.Mark(bx, byy + 1, Ramps.Leaf, 3); d.Mark(bx + 1, byy + 1, Ramps.Leaf, 3); d.Mark(bx + 1, byy + 2, Ramps.Wood, 2); break;
            case 6: d.Mark(bx + 1, byy, Ramps.Hazard, 4); d.Mark(bx, byy + 1, Ramps.Hazard, 4); d.Mark(bx + 1, byy + 1, Ramps.Hazard, 4); d.Mark(bx, byy + 2, Ramps.Hazard, 3); break;
        }
    }

    private static void Arms(Dot d, PersonLook l, Body b, DotPose pose, int frame, int cloth, int skin, bool back)
    {
        int aw = b.ArmW;
        int top = b.TorsoTop + 1;
        int len = b.Torso + (l.Age == DotAge.Child ? 0 : 0);
        int sleeveCol = l.Pattern == 2 ? l.Accent : cloth;
        // 0: 왼팔(화면 왼쪽) · 1: 오른팔
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            int x0 = left ? Cx - b.Shoulder - aw + (aw == 3 ? 1 : 0) : Cx + b.Shoulder - (aw == 3 ? 1 : 0);
            d.Part = (byte)(left ? 9 : 8);
            bool pl = back ? left : !left; // 그 사람의 왼팔인가 (앞모습이면 화면 오른쪽이 왼팔)
            bool Arm(DotHurt lf, DotHurt rf) => (l.Hurt & (pl ? lf : rf)) != 0;
            bool none = Arm(DotHurt.NoArmL, DotHurt.NoArmR);
            bool metal = Arm(DotHurt.MetalArmL, DotHurt.MetalArmR);
            bool cast = Arm(DotHurt.CastArmL, DotHurt.CastArmR);
            bool sling = Arm(DotHurt.SlingL, DotHurt.SlingR) && !back;
            bool frost = (l.Hurt & DotHurt.Frost) != 0;
            int handRamp = metal ? Ramps.Steel : frost ? Ramps.FrostSkin : skin;
            if (none)
            {
                // 잃은 팔: 접은 소매를 핀으로
                d.Rect(x0, top, aw, 3, sleeveCol, 2);
                d.Mark(x0 + aw / 2, top + 2, Ramps.Steel, 5);
                continue;
            }
            int swing = 0;
            if (pose == DotPose.Walk) { int st = Gait(l, frame).stride; swing = st == 0 ? 0 : (st > 0) == left ? -1 : 1; }
            if (!back && !left && ToMouth(l, frame) && !sling && !cast)
            {
                // 손을 입으로 (마시기 · 먹기): 위팔은 내리고 아래팔을 들어 손이 턱 앞에
                d.Rect(x0, top, aw, 3, sleeveCol, 2);
                d.Rect(Cx + 1, b.HeadTop + b.HeadRows - 2, 2, top - (b.HeadTop + b.HeadRows - 2) + 1, sleeveCol, 2);
                d.Rect(Cx + 1, b.HeadTop + b.HeadRows - 3, 2, 2, handRamp, 3);
                continue;
            }
            if (!back && left && Gesture(l, frame) && !sling && !cast && pose is DotPose.Stand or DotPose.Sit)
            {
                // 말하며 손짓: 아래팔을 들어 손바닥이 어깨 높이
                d.Rect(x0, top, aw, 3, sleeveCol, 2);
                d.Rect(x0 - 1, top - 3, 2, 4, sleeveCol, 2);
                d.Rect(x0 - 2, top - 5, 2, 2, handRamp, 3);
                continue;
            }
            if (pose == DotPose.Cower)
            {
                // 팔을 머리 위로 (얼굴 가리기)
                int hx = left ? Cx - 5 : Cx + 2;
                d.Rect(left ? x0 + 1 : x0, top - 2, aw, 3, sleeveCol, 2);
                d.Rect(hx, b.HeadTop + 1, 3, 3, sleeveCol, 2);
                d.Rect(hx + (left ? 2 : -1), b.HeadTop - 1, 2, 2, handRamp, 3);
                continue;
            }
            if (pose is DotPose.Carry or DotPose.Kneel || (pose == DotPose.Work && !back) || sling)
            {
                if (sling)
                {
                    // 팔걸이: 위팔은 내리고 아래팔은 배 앞으로
                    // 팔걸이: 위팔은 내리고 아래팔은 배 앞으로 (반대편 어깨에서 내려온 천에 걸친다)
                    d.Rect(x0, top, aw, 4, sleeveCol, 2);
                    int fa = left ? x0 : Cx - 3, fb = left ? Cx + 1 : x0 + aw - 1;
                    d.Part = 26;
                    for (int x = fa; x <= fb; x++) { d.Set(x, top + 4, sleeveCol, 2); d.Set(x, top + 5, sleeveCol, 1); }
                    d.Rect(left ? Cx + 1 : Cx - 4, top + 4, 2, 2, handRamp, 3);
                    d.Part = 27;
                    for (int x = fa; x <= fb + (left ? 1 : 0); x++) d.Set(x - (left ? 0 : 1), top + 6, Ramps.Bandage, 2);
                    for (int i = 0; i < 6; i++) d.Set(left ? Cx + 2 - i / 2 : Cx - 3 + i / 2, b.TorsoTop + i, Ramps.Bandage, 3);
                    continue;
                }
                // 두 손을 몸 앞으로 (들기 · 일하기 — 공구를 돌리면 손이 오르내린다)
                int tb = pose == DotPose.Work ? ToolBob(l, frame) * (left ? 1 : -1) : 0;
                d.Rect(x0, top, aw, 4, sleeveCol, 2);
                d.Part = 26;
                int fx = left ? Cx - 4 : Cx + 1;
                d.Rect(left ? x0 + 1 : fx, top + 3 + tb, Math.Abs(fx - x0) + 1, 2, sleeveCol, 2);
                d.Rect(fx + (left ? 0 : 1), top + 3 + tb, 2, 2, handRamp, 3);
                continue;
            }
            int sl = len - 2 + (swing < 0 ? -1 : 0);
            d.Rect(x0, top + Math.Max(0, -swing) * 0, aw, sl, cast ? Ramps.Bandage : metal ? Ramps.Steel : sleeveCol, cast ? 2 : metal ? 3 : 2);
            if (metal) { d.Mark(x0 + aw / 2, top + sl / 2, Ramps.Steel, 1); d.Mark(x0 + aw / 2, top + sl / 2 + 1, Ramps.Cyan, 3); }
            if (cast) for (int y = top + 1; y < top + sl; y += 2) d.Mark(x0 + (left ? 0 : aw - 1), y, Ramps.Bandage, 1);
            // 손
            int hy = top + sl;
            d.Rect(x0 + (aw == 3 ? (left ? 0 : 1) : 0), hy, aw == 3 ? 2 : aw, 2, handRamp, metal ? 4 : 3);
        }
    }

    // ───────────────────────────── 머리카락 ─────────────────────────────

    private static int HairBase(PersonLook l) => l.Age == DotAge.Elder && l.HairRamp != Ramps.HairWhite ? Ramps.HairGrey : l.HairRamp;

    private static void BackHair(Dot d, PersonLook l, Body b)
    {
        int h = HairBase(l), t = b.HeadTop;
        switch (l.Hair)
        {
            case DotHair.Long:
                d.Rect(Cx - 7, t + 3, 14, 13, h, 1);
                for (int x = Cx - 6; x < Cx + 6; x += 2) d.Set(x, t + 16, h, 1);
                break;
            case DotHair.Bob:
                d.Rect(Cx - 7, t + 3, 14, 8, h, 1);
                break;
            case DotHair.Curly:
                d.Rect(Cx - 8, t + 2, 16, 8, h, 1);
                break;
        }
    }

    private static void FrontHair(Dot d, PersonLook l, Body b)
    {
        int h = HairBase(l), t = b.HeadTop;
        var hw = b.Head;
        void Cap(int rows, int volume, int shade = 2)
        {
            for (int x = Cx - hw[0] + 1; x < Cx + hw[0] - 1; x++) d.Set(x, t - 1, h, shade);
            for (int r = 0; r < rows; r++)
            {
                int w = hw[Math.Min(r, hw.Length - 1)] + (r >= 1 ? volume : 0);
                for (int x = Cx - w; x < Cx + w; x++) d.Set(x, t + r, h, shade);
            }
        }
        void Strands(int y0, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = Cx - 6; x < Cx + 6; x++)
                    if (d.RampAt(x, y) == h && ((x * 3 + y * 2) % 5 == 0)) d.Mark(x, y, h, 3);
        }
        switch (l.Hair)
        {
            case DotHair.Bald:
                d.Mark(Cx - 3, t + 1, b.SkinRamp, 4); d.Mark(Cx - 2, t + 1, b.SkinRamp, 4); d.Mark(Cx - 3, t + 2, b.SkinRamp, 4);
                break;
            case DotHair.Receding:
                for (int r = 3; r < 7; r++) { d.Set(Cx - 7, t + r, h, 2); d.Set(Cx - 6, t + r, h, 2); d.Set(Cx + 5, t + r, h, 2); d.Set(Cx + 6, t + r, h, 2); }
                for (int x = Cx - 5; x < Cx + 5; x += 2) d.Mark(x, t, h, 2);
                d.Mark(Cx - 2, t + 1, b.SkinRamp, 4); d.Mark(Cx - 1, t + 1, b.SkinRamp, 4);
                break;
            case DotHair.Buzz:
                for (int r = 0; r < 3; r++)
                    for (int x = Cx - hw[r]; x < Cx + hw[r]; x++)
                        if (r < 2 || ((x + r) & 1) == 0) d.Mark(x, t + r, h, 1);
                break;
            case DotHair.Crop:
                Cap(4, 1);
                for (int x = Cx - 6; x < Cx + 2; x++) d.Set(x, t + 4, h, 2);
                d.Set(Cx - 6, t + 5, h, 2); d.Set(Cx + 5, t + 4, h, 2); d.Set(Cx + 5, t + 5, h, 2);
                Strands(t, t + 2);
                break;
            case DotHair.Side:
                Cap(4, 1);
                for (int x = Cx - 7; x < Cx + 3; x++) d.Set(x, t + 4, h, 2);
                for (int x = Cx - 7; x < Cx - 2; x++) d.Set(x, t + 5, h, 2);
                d.Set(Cx - 7, t + 6, h, 1);
                d.Mark(Cx + 2, t, h, 0); d.Mark(Cx + 2, t + 1, h, 0);
                Strands(t, t + 3);
                break;
            case DotHair.Curly:
                Cap(5, 2);
                for (int x = Cx - 4; x < Cx + 4; x++) if (x != Cx - 1 && x != Cx + 2) d.Set(x, t - 2, h, 2);
                for (int r = 5; r < 8; r++) { d.Set(Cx - 8, t + r, h, 2); d.Set(Cx - 7, t + r, h, 2); d.Set(Cx + 6, t + r, h, 2); d.Set(Cx + 7, t + r, h, 2); }
                for (int y = t - 1; y < t + 8; y++)
                    for (int x = Cx - 8; x < Cx + 8; x++)
                        if (d.RampAt(x, y) == h && ((x + y * 3) % 4 == 0)) d.Mark(x, y, h, 3);
                        else if (d.RampAt(x, y) == h && ((x * 2 + y) % 5 == 0)) d.Mark(x, y, h, 1);
                break;
            case DotHair.Bob:
                Cap(4, 1);
                for (int x = Cx - 6; x < Cx + 6; x++) d.Set(x, t + 4, h, 2);
                for (int r = 4; r < 10; r++) { d.Set(Cx - 7, t + r, h, 2); d.Set(Cx - 6, t + r, h, 2); d.Set(Cx + 5, t + r, h, 2); d.Set(Cx + 6, t + r, h, 2); }
                d.Set(Cx - 5, t + 9, h, 2); d.Set(Cx + 4, t + 9, h, 2);
                Strands(t, t + 2);
                break;
            case DotHair.Long:
                Cap(4, 1);
                for (int x = Cx - 6; x < Cx - 1; x++) d.Set(x, t + 4, h, 2);
                for (int x = Cx + 1; x < Cx + 6; x++) d.Set(x, t + 4, h, 2);
                d.Part = 28; // 어깨 앞으로 늘어진 머리
                for (int r = 4; r < 14; r++) { d.Set(Cx - 7, t + r, h, 2); d.Set(Cx - 6, t + r, h, 2); d.Set(Cx + 5, t + r, h, 2); d.Set(Cx + 6, t + r, h, 2); }
                d.Set(Cx - 7, t + 14, h, 1); d.Set(Cx + 6, t + 14, h, 1);
                Strands(t, t + 3);
                break;
            case DotHair.Ponytail:
                Cap(4, 0);
                for (int x = Cx - 6; x < Cx - 2; x++) d.Set(x, t + 4, h, 2);
                d.Set(Cx + 5, t + 4, h, 2);
                d.Mark(Cx + 5, t + 1, l.Accent, 3); // 머리끈
                for (int r = 3; r < 9; r++) d.Set(Cx + 6, t + r, h, 1);
                Strands(t, t + 2);
                break;
            case DotHair.Bun:
                d.Oval(Cx, t - 1.5f, 2.4f, 2.0f, h, 1);
                Cap(4, 1);
                d.Mark(Cx - 3, t - 1, l.Accent, 3);
                for (int x = Cx - 6; x < Cx - 3; x++) d.Set(x, t + 4, h, 2);
                d.Set(Cx + 5, t + 4, h, 2);
                Strands(t - 3, t + 2);
                break;
            case DotHair.Braid:
                Cap(4, 1);
                for (int x = Cx - 6; x < Cx + 1; x++) d.Set(x, t + 4, h, 2);
                d.Part = 28;
                for (int r = 4; r < 17; r++) { d.Set(Cx + 5, t + r, h, (r & 1) == 0 ? 2 : 1); d.Set(Cx + 6, t + r, h, (r & 1) == 0 ? 1 : 2); }
                d.Set(Cx + 5, t + 17, l.Accent, 3); d.Set(Cx + 6, t + 17, l.Accent, 2);
                break;
        }
        if ((l.Hurt & DotHurt.RadHair) != 0)
            for (int y = t - 3; y < t + 6; y++)
                for (int x = Cx - 8; x < Cx + 8; x++)
                    if (d.RampAt(x, y) == h && DotHash.F(l.Seed, x, y) < 0.45f)
                    {
                        if (y >= t && x >= Cx - b.Head[Math.Min(y - t, b.Head.Length - 1)] && x < Cx + b.Head[Math.Min(y - t, b.Head.Length - 1)]) d.Mark(x, y, b.SkinRamp, 3);
                        else d.Clear(x, y);
                    }
    }

    private static void BackOfHead(Dot d, PersonLook l, Body b)
    {
        int h = HairBase(l), t = b.HeadTop;
        var hw = b.Head;
        if (l.Hair == DotHair.Bald) { d.Mark(Cx - 2, t + 1, b.SkinRamp, 4); return; }
        int rows = l.Hair switch { DotHair.Buzz => 7, DotHair.Receding => 7, DotHair.Crop or DotHair.Side => 9, _ => 10 };
        for (int r = 0; r < rows; r++)
        {
            int w = hw[Math.Min(r, hw.Length - 1)] + (r is >= 1 and <= 6 && l.Hair is not (DotHair.Buzz or DotHair.Receding or DotHair.Ponytail or DotHair.Bun) ? 1 : 0);
            for (int x = Cx - w; x < Cx + w; x++)
            {
                if (l.Hair == DotHair.Receding && r < 3) { if (((x + r) & 1) == 0) d.Mark(x, t + r, h, 1); continue; }
                d.Set(x, t + r, l.Hair == DotHair.Buzz ? h : h, l.Hair == DotHair.Buzz ? 1 : 2);
            }
        }
        d.Set(Cx - hw[0] + 1, t - 1, h, 2); for (int x = Cx - hw[0] + 1; x < Cx + hw[0] - 1; x++) d.Set(x, t - 1, h, 2);
        for (int y = t; y < t + rows; y++)
            for (int x = Cx - 7; x < Cx + 7; x++)
                if (d.RampAt(x, y) == h && ((x * 2 + y * 3) % 5 == 0)) d.Mark(x, y, h, 3);
        d.Part = 28;
        switch (l.Hair)
        {
            case DotHair.Long: d.Rect(Cx - 6, t + 9, 12, 8, h, 2); for (int x = Cx - 6; x < Cx + 6; x += 2) d.Set(x, t + 17, h, 1); break;
            case DotHair.Bob: d.Rect(Cx - 7, t + 8, 14, 3, h, 2); break;
            case DotHair.Ponytail: d.Mark(Cx - 1, t + 3, l.Accent, 3); d.Mark(Cx, t + 3, l.Accent, 3); d.Rect(Cx - 1, t + 4, 2, 10, h, 2); d.Set(Cx - 1, t + 14, h, 1); break;
            case DotHair.Bun: d.Oval(Cx, t + 2, 3.2f, 2.6f, h, 2); d.Mark(Cx - 1, t + 4, l.Accent, 3); break;
            case DotHair.Braid: for (int r = 8; r < 18; r++) { d.Set(Cx - 1, t + r, h, (r & 1) == 0 ? 2 : 1); d.Set(Cx, t + r, h, (r & 1) == 0 ? 1 : 2); } d.Set(Cx - 1, t + 18, l.Accent, 3); d.Set(Cx, t + 18, l.Accent, 2); break;
            case DotHair.Curly: d.Rect(Cx - 7, t + 8, 14, 3, h, 2); break;
        }
    }

    private static void Beard(Dot d, PersonLook l, Body b)
    {
        int h = HairBase(l), t = b.HeadTop;
        if (l.Beard)
        {
            for (int r = 7; r < 11; r++)
            {
                int w = b.Head[Math.Min(r, b.Head.Length - 1)];
                for (int x = Cx - w; x < Cx + w; x++) d.Set(x, t + r, h, 2);
            }
            for (int x = Cx - 2; x < Cx + 2; x++) d.Set(x, t + 11, h, 1);
            d.Mark(Cx - 1, t + 8, h, 0); d.Mark(Cx, t + 8, h, 0); // 입
            d.Mark(Cx - 4, t + 9, h, 3); d.Mark(Cx - 3, t + 10, h, 3);
        }
        if (l.Mustache || l.Beard)
            for (int x = Cx - 2; x < Cx + 2; x++) d.Set(x, t + 7, h, l.Beard ? 2 : 1);
    }

    // ───────────────────────────── 옆 ─────────────────────────────

    private static void Side(Dot d, PersonLook l, Body b, DotPose pose, int frame)
    {
        int skin = b.SkinRamp;
        bool child = l.Age == DotAge.Child;
        int cloth = l.Suit ? Ramps.SuitWhite : child || l.Casual ? l.Accent : l.Cloth;
        int pants = l.Suit ? Ramps.SuitWhite : child || l.Casual ? Ramps.ClothNavy : l.Cloth;
        int pantShade = pants == l.Cloth ? 1 : 2;
        int hh = b.Hunch;
        int legRows = Foot - b.Shoe + 1 - b.LegTop;
        int tw = Math.Max(3, b.Waist); // 옆 몸통 반폭
        // 걷기: 앞다리 · 뒷다리
        int stride = pose == DotPose.Walk ? Gait(l, frame).stride : 0;
        int legW = Math.Max(3, b.LegW);
        int lx = Cx - legW / 2 - 1;
        bool sit = pose == DotPose.Sit;
        if (sit)
        {
            // 앉음: 허벅지가 앞으로 · 정강이가 아래로
            d.Part = 2; d.Rect(Cx - 2, b.LegTop - 1, 7, 3, pants, pantShade);
            d.Rect(Cx + 3, b.LegTop + 1, 3, legRows - 1, child ? skin : pants, pantShade);
            d.Part = 4; d.Rect(Cx + 3, Foot - b.Shoe + 1, 4, b.Shoe, Ramps.Rubber, 2);
        }
        else
        {
            // 동쪽을 보면 가까운 다리 = 오른다리, 먼 다리 = 왼다리 (서쪽은 좌우를 바꿔 그린 뒤 뒤집는다)
            bool Has(DotHurt f) => (l.Hurt & f) != 0;
            // 먼 다리 (어둡게)
            if (!Has(DotHurt.NoLegL))
            {
                d.Part = 2; d.Rect(lx - stride, b.LegTop, legW, legRows, Has(DotHurt.CastLegL) ? Ramps.Bandage : Has(DotHurt.MetalLegL) ? Ramps.Steel : pants, Has(DotHurt.CastLegL) ? 1 : Has(DotHurt.MetalLegL) ? 3 : pantShade - 1);
                d.Part = 3; d.Rect(lx - stride, Foot - b.Shoe + 1, legW + 1, b.Shoe, Ramps.Rubber, 1);
            }
            // 가까운 다리
            if (Has(DotHurt.NoLegR)) { d.Part = 4; d.Rect(lx, b.LegTop, legW, 3, pants, pantShade); d.Mark(lx + 1, b.LegTop + 2, Ramps.Steel, 5); }
            else
            {
                bool legCast = Has(DotHurt.CastLegR), legMetal = Has(DotHurt.MetalLegR);
                d.Part = 4; d.Rect(lx + stride, b.LegTop, legW, legRows, pants, pantShade);
                if (child) d.Rect(lx + stride, b.LegTop + 2, legW, legRows - 2, skin, 3);
                if (legCast) d.Rect(lx + stride, b.LegTop + 2, legW, legRows - 2, Ramps.Bandage, 2);
                if (legMetal) d.Rect(lx + stride + 1, b.LegTop + 2, legW - 2, legRows - 2, Ramps.Steel, 4);
                d.Part = 5; d.Rect(lx + stride, Foot - b.Shoe + 1, legW + 1, b.Shoe, Ramps.Rubber, 2);
            }
        }
        // 몸통 (노인은 등이 굽어 위가 앞으로)
        d.Part = 7;
        for (int i = 0; i < b.Torso; i++)
        {
            int lean = hh > 0 && i < 3 ? 1 : 0;
            int back = Cx - tw + lean, front = Cx + tw + lean + (l.Build == DotBuild.Heavy && i is >= 3 and <= 6 ? 1 : 0) + (l.Female && l.Age >= DotAge.Teen && i is 2 or 3 ? 1 : 0);
            for (int x = back; x < front; x++) d.Set(x, b.TorsoTop + i, cloth, 2);
        }
        if (!child && !l.Casual) for (int x = Cx - tw; x < Cx + tw + 1; x++) d.Mark(x, b.TorsoTop + b.Torso - 3, Ramps.Panel, 3);
        d.Mark(Cx + tw - 1 + hh, b.TorsoTop, l.Accent, 3); d.Mark(Cx + tw - 2 + hh, b.TorsoTop, l.Accent, 2);
        // 목
        d.Part = 6; d.Rect(Cx - 1 + hh, b.TorsoTop - 1, 3, 1, skin, 2);
        // 팔 (가까운 팔 하나, 걸으면 흔든다)
        d.Part = 9;
        int swing = pose == DotPose.Walk ? -Gait(l, frame).stride : 0;
        int at = b.TorsoTop + 1;
        bool metal = (l.Hurt & DotHurt.MetalArmR) != 0, noArm = (l.Hurt & DotHurt.NoArmR) != 0;
        bool armCast = (l.Hurt & DotHurt.CastArmR) != 0, armSling = (l.Hurt & DotHurt.SlingR) != 0;
        int sleeve = l.Pattern == 2 ? l.Accent : cloth;
        int handRamp = metal ? Ramps.Steel : (l.Hurt & DotHurt.Frost) != 0 ? Ramps.FrostSkin : skin;
        if (noArm)
        {
            // 잃은 팔: 어깨에 접어 꽂은 소매
            d.Rect(Cx - 1 + hh, at, 3, 3, sleeve, 2);
            d.Mark(Cx + hh, at + 2, Ramps.Steel, 5);
        }
        else if (armSling)
        {
            // 팔걸이: 아래팔을 가슴 앞으로 올려 천에 건다
            d.Rect(Cx - 1 + hh, at, 3, 4, sleeve, 2);
            d.Rect(Cx + hh, at + 3, 4, 2, sleeve, 2);
            d.Rect(Cx + 4 + hh, at + 3, 2, 2, handRamp, 3);
            d.Part = 27; d.Rect(Cx - 1 + hh, at + 5, 6, 1, Ramps.Bandage, 3);
        }
        else if (ToMouth(l, frame))
        {
            // 손을 입으로: 아래팔을 얼굴 앞으로 올린다
            d.Rect(Cx - 1 + hh, at, 3, 3, sleeve, 2);
            d.Rect(Cx + 2 + hh, b.HeadTop + b.HeadRows - 1, 2, at - (b.HeadTop + b.HeadRows - 1) + 2, sleeve, 2);
            d.Rect(Cx + 3 + hh, b.HeadTop + b.HeadRows - 2, 2, 2, handRamp, 3);
        }
        else if (Gesture(l, frame) && pose is DotPose.Stand or DotPose.Sit)
        {
            // 말하며 손짓: 손을 앞으로 내민다
            d.Rect(Cx - 1 + hh, at, 3, 3, sleeve, 2);
            d.Rect(Cx + 1 + hh, at + 1, 5, 2, sleeve, 2);
            d.Rect(Cx + 6 + hh, at, 2, 2, handRamp, 3);
        }
        else if (pose is DotPose.Work or DotPose.Carry or DotPose.Kneel)
        {
            int tb = pose == DotPose.Work ? ToolBob(l, frame) : 0;
            d.Rect(Cx - 1 + hh, at, 3, 4, sleeve, 2);
            d.Rect(Cx + 1 + hh, at + 3 + tb, 5, 2, sleeve, 2);
            d.Rect(Cx + 6 + hh, at + 3 + tb, 2, 2, handRamp, 3);
        }
        else if (pose == DotPose.Cower)
        {
            d.Rect(Cx + hh, at - 2, 3, 3, sleeve, 2);
            d.Rect(Cx + 2 + hh, b.HeadTop + 2, 3, 3, handRamp, 3);
        }
        else
        {
            int len = b.Torso - 2;
            for (int i = 0; i < len; i++)
            {
                int off = swing * i / Math.Max(1, len);
                d.Rect(Cx - 1 + off + hh, at + i, 3, 1, metal ? Ramps.Steel : armCast ? Ramps.Bandage : sleeve, metal ? 3 : 2);
            }
            d.Rect(Cx - 1 + swing + hh, at + len, 2, 2, handRamp, 3);
        }
        // 머리 (얼굴이 오른쪽)
        d.Part = 20;
        int hx = Cx + hh * 2;
        var hw = b.Head;
        for (int r = 0; r < hw.Length; r++)
        {
            int w = hw[r] - 1;
            for (int x = hx - w; x < hx + w + (r is >= 6 and <= 8 ? 1 : 0); x++) d.Set(x, b.HeadTop + r, skin, 3);
        }
        d.Set(hx + hw[6], b.HeadTop + 6, skin, 3); // 코
        // 귀
        d.Mark(hx - 2, b.HeadTop + 5, skin, 2); d.Mark(hx - 2, b.HeadTop + 6, skin, 1); d.Mark(hx - 1, b.HeadTop + 6, skin, 2);
        // 머리카락
        d.Part = 22;
        int h = HairBase(l), t = b.HeadTop;
        int frontX = hx + hw[5];
        if (l.Hair is not (DotHair.Bald))
        {
            if (l.Hair == DotHair.Receding)
            {
                for (int r = 3; r < 8; r++) for (int x = hx - 5; x < hx - 2; x++) d.Set(x, t + r, h, 2);
            }
            else if (l.Hair == DotHair.Buzz)
            {
                for (int r = 0; r < 7; r++) for (int x = hx - hw[r] + 1; x < hx + (r < 3 ? hw[r] - 2 : -2); x++) d.Mark(x, t + r, h, 1);
            }
            else
            {
                int vol = l.Hair is DotHair.Curly ? 2 : l.Hair is DotHair.Ponytail or DotHair.Bun ? 0 : 1;
                for (int x = hx - hw[0] + 1; x < hx + hw[0] - 1; x++) d.Set(x, t - 1, h, 2);
                for (int r = 0; r < 4; r++) for (int x = hx - hw[r] - (r > 0 ? vol : 0) + 1; x < hx + hw[r] - 1 + (r == 3 ? 1 : 0); x++) d.Set(x, t + r, h, 2);
                int backRows = l.Hair switch { DotHair.Crop or DotHair.Side => 8, DotHair.Bob => 10, DotHair.Curly => 9, _ => 9 };
                for (int r = 4; r < backRows; r++) for (int x = hx - hw[Math.Min(r, hw.Length - 1)] - vol + 1; x < hx - 1; x++) d.Set(x, t + r, h, 2);
                d.Set(frontX - 1, t + 4, h, 2); d.Set(frontX - 2, t + 4, h, 2);
                d.Part = 28;
                switch (l.Hair)
                {
                    case DotHair.Long: for (int r = 8; r < 17; r++) { d.Set(hx - 6, t + r, h, 2); d.Set(hx - 5, t + r, h, 2); d.Set(hx - 4, t + r, h, 1); } break;
                    case DotHair.Ponytail: d.Mark(hx - 6, t + 3, l.Accent, 3); for (int r = 3; r < 11; r++) { d.Set(hx - 7 - (r > 5 ? 1 : 0), t + r, h, 2); d.Set(hx - 8 + (r > 7 ? 1 : 0), t + r + 1, h, 1); } break;
                    case DotHair.Bun: d.Oval(hx - 5, t + 1, 2.6f, 2.4f, h, 2); break;
                    case DotHair.Braid: for (int r = 6; r < 17; r++) { d.Set(hx - 6, t + r, h, (r & 1) == 0 ? 2 : 1); d.Set(hx - 5, t + r, h, (r & 1) == 0 ? 1 : 2); } break;
                    case DotHair.Bob: for (int r = 6; r < 10; r++) d.Set(hx - 6, t + r, h, 2); break;
                }
            }
            for (int y = t - 1; y < t + 10; y++)
                for (int x = hx - 8; x < hx + 7; x++)
                    if (d.RampAt(x, y) == h && ((x * 2 + y * 3) % 5 == 0)) d.Mark(x, y, h, 3);
        }
        if (l.Beard)
        {
            d.Part = 24;
            for (int r = 7; r < 11; r++) for (int x = hx - 1; x < hx + hw[Math.Min(r, hw.Length - 1)]; x++) d.Set(x, t + r, h, 2);
            d.Set(hx + 1, t + 11, h, 1); d.Set(hx + 2, t + 11, h, 1);
        }
        if (l.Suit) Helmet(d, l, b, DotFacing.E);
        Overlays(d, l, b, pose, frame, back: false, side: true);
        Held(d, l, b, pose, frame, DotFacing.E);
    }

    // ───────────────────────────── 우주복 헬멧 ─────────────────────────────

    /// <summary>헬멧: 흰 테 + 얼굴 앞 유리(위가 밝게 비친다). 머리카락은 안에 눌려 보이지 않는다.</summary>
    private static void Helmet(Dot d, PersonLook l, Body b, DotFacing f)
    {
        int t = b.HeadTop, hx = f == DotFacing.E ? Cx + b.Hunch * 2 : Cx;
        float cy = t + 4.5f;
        d.Part = 45;
        d.Oval(hx, cy, 8.2f, 7.8f, Ramps.SuitWhite, 2);
        d.Part = 46;
        if (f == DotFacing.S) d.Oval(hx, cy + 0.5f, 6.2f, 5.6f, Ramps.Glass, 2);
        else if (f == DotFacing.E) d.Oval(hx + 2.5f, cy + 0.5f, 4.2f, 5.4f, Ramps.Glass, 2);
        // 목 고리 · 등짐
        d.Part = 47;
        d.Rect(hx - 4, t + 12, 8, 2, Ramps.Steel, 4);
        if (f != DotFacing.S) { d.Part = 9; d.Rect(f == DotFacing.E ? Cx - 7 : Cx - 5, b.TorsoTop + 1, f == DotFacing.E ? 4 : 10, 7, Ramps.Steel, 3); }
    }

    private static void HelmetMarks(Dot d, PersonLook l, Body b, DotFacing f)
    {
        int t = b.HeadTop, hx = f == DotFacing.E ? Cx + b.Hunch * 2 : Cx;
        if (f == DotFacing.S)
        {
            // 유리 속 눈 · 비친 빛
            d.Mark(hx - 3, t + 5, Ramps.Ink, 1); d.Mark(hx + 2, t + 5, Ramps.Ink, 1);
            d.Mark(hx - 4, t + 1, Ramps.Glass, 4); d.Mark(hx - 3, t + 1, Ramps.Glass, 4); d.Mark(hx - 5, t + 2, Ramps.Glass, 4);
            d.Mark(hx + 3, t + 8, Ramps.Glass, 3);
        }
        else if (f == DotFacing.E) { d.Mark(hx + 3, t + 5, Ramps.Ink, 1); d.Mark(hx + 2, t + 1, Ramps.Glass, 4); d.Mark(hx + 3, t + 2, Ramps.Glass, 4); }
        // 헬멧 위 표시등 · 개인 색 띠
        d.Mark(hx - 1, t - 3, Ramps.Led, 3, onEmpty: false);
        for (int y = b.TorsoTop + 2; y < b.TorsoTop + 4; y++) for (int x = Cx - b.Shoulder; x < Cx + b.Shoulder; x++) d.Mark(x, y, l.Accent, 3);
    }

    // ───────────────────────────── 덧붙는 것 ─────────────────────────────

    private static void Overlays(Dot d, PersonLook l, Body b, DotPose pose, int frame, bool back, bool side = false)
    {
        int t = b.HeadTop;
        var hurt = l.Hurt;
        if ((hurt & DotHurt.HeadWrap) != 0)
        {
            d.Part = 40;
            int w0 = side ? Cx - 5 : Cx - 7, w1 = side ? Cx + 7 : Cx + 7;
            for (int x = w0; x < w1; x++) { d.Set(x, t + 2, Ramps.Bandage, 3); d.Set(x, t + 3, Ramps.Bandage, 2); }
            if (!side && !back) { d.Set(Cx + 7, t + 4, Ramps.Bandage, 2); d.Set(Cx + 8, t + 5, Ramps.Bandage, 1); }
        }
        if (l.Cane && pose != DotPose.Sit && pose != DotPose.Cower)
        {
            d.Part = 41;
            int cx = side ? Cx + 5 + b.Hunch : Cx + b.Shoulder + 2;
            int top = b.TorsoTop + b.Torso - 2;
            for (int y = top; y <= Foot; y++) d.Set(cx, y, Ramps.Wood, 2);
            d.Set(cx - 1, top, Ramps.Wood, 3); d.Set(cx - 2, top, Ramps.Wood, 3);
        }
        // 목발: 다친 다리 쪽 (화면 기준 — 앞모습이면 그 사람의 오른다리가 화면 왼쪽)
        var legL = DotHurt.CastLegL | DotHurt.NoLegL; var legR = DotHurt.CastLegR | DotHurt.NoLegR;
        bool crutchL = (hurt & (back ? legL : legR)) != 0, crutchR = (hurt & (back ? legR : legL)) != 0;
        if ((crutchL || crutchR) && pose != DotPose.Sit && !side)
        {
            d.Part = 42;
            int cx = crutchL ? Cx - b.Shoulder - b.ArmW - 1 : Cx + b.Shoulder + b.ArmW;
            for (int y = b.TorsoTop + 1; y <= Foot; y++) d.Set(cx, y, Ramps.Steel, 4);
            d.Set(cx - 1, b.TorsoTop + 1, Ramps.Rubber, 3); d.Set(cx + 1, b.TorsoTop + 1, Ramps.Rubber, 3);
            d.Set(cx + (crutchL ? 1 : -1), b.TorsoTop + 6, Ramps.Steel, 3);
            d.Set(cx, Foot, Ramps.Rubber, 2);
        }
    }

    /// <summary>얼굴 · 상처 자국 (빛을 받지 않는 세부 — Bevel 뒤에 찍는다).</summary>
    private static void Marks(Dot d, PersonLook l, Body b, DotFacing face, DotPose pose, int frame)
    {
        int t = b.HeadTop, skin = b.SkinRamp;
        var hurt = l.Hurt;
        if (l.Suit) { if (face != DotFacing.N) HelmetMarks(d, l, b, face); return; }
        if (face == DotFacing.S)
        {
            bool patch = (hurt & DotHurt.EyePatch) != 0;
            int ey = t + 5;
            // 눈 (아프면 찡그려 감는다)
            if (l.EyesClosed || l.Face == DotFace.Pain)
            {
                if (!patch) { d.Mark(Cx - 5, ey + 1, Ramps.Ink, 1); d.Mark(Cx - 4, ey + 1, Ramps.Ink, 1); }
                d.Mark(Cx + 3, ey + 1, Ramps.Ink, 1); d.Mark(Cx + 4, ey + 1, Ramps.Ink, 1);
            }
            else
            {
                if (!patch) { d.Mark(Cx - 4, ey, Ramps.Ink, 1); d.Mark(Cx - 4, ey + 1, Ramps.Ink, 0); }
                d.Mark(Cx + 3, ey, Ramps.Ink, 1); d.Mark(Cx + 3, ey + 1, Ramps.Ink, 0);
            }
            if (l.Female && l.Age >= DotAge.Teen) { if (!patch) d.Mark(Cx - 5, ey, Ramps.Ink, 1); d.Mark(Cx + 4, ey, Ramps.Ink, 1); }
            // 눈썹
            int brow = HairBase(l);
            if (l.Hair != DotHair.Side || true)
            {
                if (d.RampAt(Cx - 4, ey - 1) == skin) { d.Mark(Cx - 5, ey - 1, brow, 1); d.Mark(Cx - 4, ey - 1, brow, 1); }
                if (d.RampAt(Cx + 3, ey - 1) == skin) { d.Mark(Cx + 3, ey - 1, brow, 1); d.Mark(Cx + 4, ey - 1, brow, 1); }
            }
            // 코 · 입
            d.Mark(Cx, ey + 2, skin, 2);
            if (!l.Beard) { d.Mark(Cx - 1, t + 8, skin, 1); d.Mark(Cx, t + 8, skin, 1); }
            FaceFront(d, l, t, ey, skin, brow);
            if (pose == DotPose.Cower && !l.Beard) { d.Mark(Cx - 1, t + 9, Ramps.Ink, 1); d.Mark(Cx, t + 9, Ramps.Ink, 1); }
            // 볼 (아이 · 여자)
            if (l.Age == DotAge.Child || (l.Female && l.Age is DotAge.Teen or DotAge.Adult)) { d.Mark(Cx - 5, ey + 2, Ramps.ClothRed, 4); d.Mark(Cx + 4, ey + 2, Ramps.ClothRed, 4); }
            // 나이: 눈 밑 · 이마 주름
            if (l.Age == DotAge.Elder) { d.Mark(Cx - 4, ey + 2, skin, 2); d.Mark(Cx + 3, ey + 2, skin, 2); if (d.RampAt(Cx - 1, t + 3) == skin) { d.Mark(Cx - 2, t + 3, skin, 2); d.Mark(Cx - 1, t + 3, skin, 2); d.Mark(Cx + 1, t + 3, skin, 2); } }
            if (l.Age == DotAge.Middle) { d.Mark(Cx - 5, ey + 1, skin, 2); }
            if (l.Stubble && !l.Beard)
                for (int y = t + 9; y < t + 11; y++)
                    for (int x = Cx - 5; x < Cx + 5; x++)
                        if (d.RampAt(x, y) == skin && ((x + y) & 1) == 0) d.Mark(x, y, skin, 2);
            if (l.Glasses)
            {
                for (int x = Cx - 6; x < Cx - 1; x++) d.Mark(x, ey - 1, Ramps.Ink, 1);
                for (int x = Cx + 1; x < Cx + 6; x++) d.Mark(x, ey - 1, Ramps.Ink, 1);
                d.Mark(Cx - 1, ey - 1, Ramps.Ink, 2); d.Mark(Cx, ey - 1, Ramps.Ink, 2);
                d.Mark(Cx - 6, ey, Ramps.Ink, 1); d.Mark(Cx + 5, ey, Ramps.Ink, 1); d.Mark(Cx - 2, ey, Ramps.Ink, 1); d.Mark(Cx + 1, ey, Ramps.Ink, 1);
                d.Mark(Cx - 5, ey, Ramps.Glass, 4); d.Mark(Cx + 2, ey, Ramps.Glass, 4);
            }
            if (patch)
            {
                d.Mark(Cx - 5, ey, Ramps.Ink, 1); d.Mark(Cx - 4, ey, Ramps.Ink, 0); d.Mark(Cx - 5, ey + 1, Ramps.Ink, 0); d.Mark(Cx - 4, ey + 1, Ramps.Ink, 1); d.Mark(Cx - 3, ey, Ramps.Ink, 1);
                for (int i = 0; i < 6; i++) d.Mark(Cx - 3 + i, ey - 1 - i / 2, Ramps.Ink, 1);
            }
            if ((hurt & DotHurt.Bruise) != 0) { d.Mark(Cx + 2, ey + 2, Ramps.Bruise, 2); d.Mark(Cx + 3, ey + 2, Ramps.Bruise, 1); d.Mark(Cx + 4, ey + 1, Ramps.Bruise, 2); }
            if ((hurt & DotHurt.Frost) != 0) { d.Mark(Cx, ey + 2, Ramps.FrostSkin, 3); d.Mark(Cx - 5, ey + 2, Ramps.FrostSkin, 3); d.Mark(Cx + 4, ey + 2, Ramps.FrostSkin, 3); }
            if ((hurt & DotHurt.HeadWrap) != 0 && (hurt & DotHurt.Bleed) != 0) { d.Mark(Cx - 3, t + 2, Ramps.Blood, 3); d.Mark(Cx - 2, t + 3, Ramps.Blood, 2); }
        }
        else if (face == DotFacing.E)
        {
            int hx = Cx + b.Hunch * 2;
            int ex = hx + b.Head[5] - 3;
            if (l.EyesClosed || l.Face == DotFace.Pain) { d.Mark(ex, t + 6, Ramps.Ink, 1); d.Mark(ex - 1, t + 6, Ramps.Ink, 1); }
            else { d.Mark(ex, t + 5, Ramps.Ink, 1); d.Mark(ex, t + 6, Ramps.Ink, 0); }
            if (l.Female && l.Age >= DotAge.Teen) d.Mark(ex + 1, t + 5, Ramps.Ink, 1);
            int brow = HairBase(l);
            if (d.RampAt(ex, t + 4) == skin) { d.Mark(ex, t + 4, brow, 1); d.Mark(ex + 1, t + 4, brow, 1); }
            if (!l.Beard) d.Mark(hx + b.Head[8] - 1, t + 8, skin, 1);
            FaceSide(d, l, t, ex, hx + b.Head[8] - 1, skin, brow);
            if (l.Glasses) { d.Mark(ex - 1, t + 5, Ramps.Ink, 1); d.Mark(ex + 1, t + 5, Ramps.Glass, 4); d.Mark(ex + 1, t + 6, Ramps.Glass, 3); for (int x = hx - 2; x < ex - 1; x++) d.Mark(x, t + 5, Ramps.Ink, 1); }
            if (l.Age == DotAge.Elder) d.Mark(ex - 1, t + 7, skin, 2);
            if (l.Age == DotAge.Child || (l.Female && l.Age is DotAge.Teen or DotAge.Adult)) d.Mark(ex - 1, t + 7, Ramps.ClothRed, 4);
            if (l.Stubble && !l.Beard) for (int y = t + 7; y < t + 11; y++) for (int x = hx - 1; x < hx + 6; x++) if (d.RampAt(x, y) == skin && ((x + y) & 1) == 0) d.Mark(x, y, skin, 2);
        }
        // 몸의 상처
        var rng = l.Seed * 31 + 7;
        if ((hurt & DotHurt.Burn) != 0)
            for (int y = b.TorsoTop - 6; y < b.TorsoTop + b.Torso; y++)
                for (int x = 0; x < Cx; x++)
                    if (d.Has(x, y) && DotHash.F(rng, x, y) < 0.22f) d.Mark(x, y, Ramps.Char, DotHash.F(rng, y, x) < 0.5f ? 1 : 2);
        if ((hurt & DotHurt.TorsoWrap) != 0)
            for (int i = 0; i < b.Torso; i++) { int x = Cx - b.Shoulder + 1 + i; d.Mark(x, b.TorsoTop + i, Ramps.Bandage, 3); d.Mark(x + 1, b.TorsoTop + i, Ramps.Bandage, 2); }
        if ((hurt & DotHurt.Bleed) != 0)
        {
            int sx = Cx + 2, syy = b.TorsoTop + 4;
            d.Mark(sx, syy, Ramps.Blood, 2); d.Mark(sx + 1, syy, Ramps.Blood, 3); d.Mark(sx, syy + 1, Ramps.Blood, 2); d.Mark(sx + 1, syy + 1, Ramps.Blood, 1);
            for (int y = syy + 2; y < b.LegTop + 3; y++) if (((y + l.Seed) % 3) != 0) d.Mark(sx + 1, y, Ramps.Blood, 1);
            if (face == DotFacing.S) d.Mark(sx + 1, Foot + 1, Ramps.Blood, 2, onEmpty: true);
        }
        if ((hurt & DotHurt.Sweat) != 0 || (hurt & DotHurt.Sick) != 0)
        {
            d.Mark(Cx + 7, t + 3, Ramps.Cyan, 3, onEmpty: true); d.Mark(Cx + 7, t + 4, Ramps.Cyan, 2, onEmpty: true);
            if (face == DotFacing.S) d.Mark(Cx - 3, t + 4, Ramps.Cyan, 4);
        }
    }

    /// <summary>앞모습 표정: 웃음(입꼬리가 올라간다) · 겁(눈썹이 올라가고 입이 벌어진다) · 아픔(눈썹이 모이고 이를 문다) · 슬픔(입꼬리 · 눈썹 바깥이 처진다).</summary>
    private static void FaceFront(Dot d, PersonLook l, int t, int ey, int skin, int brow)
    {
        if (l.EyesClosed && l.Face != DotFace.Pain) return;
        bool mouth = !l.Beard;
        switch (l.Face)
        {
            case DotFace.Smile:
                if (mouth) { d.Mark(Cx - 2, t + 7, skin, 1); d.Mark(Cx + 1, t + 7, skin, 1); }
                break;
            case DotFace.Fear:
                if (d.RampAt(Cx - 4, ey - 2) == skin) { d.Mark(Cx - 5, ey - 2, brow, 1); d.Mark(Cx - 4, ey - 2, brow, 1); }
                if (d.RampAt(Cx + 3, ey - 2) == skin) { d.Mark(Cx + 3, ey - 2, brow, 1); d.Mark(Cx + 4, ey - 2, brow, 1); }
                if (mouth) { d.Mark(Cx - 1, t + 8, Ramps.Ink, 1); d.Mark(Cx, t + 8, Ramps.Ink, 1); d.Mark(Cx - 1, t + 9, Ramps.Ink, 0); d.Mark(Cx, t + 9, Ramps.Ink, 0); }
                break;
            case DotFace.Pain:
                d.Mark(Cx - 3, ey, brow, 1); d.Mark(Cx + 2, ey, brow, 1);
                if (mouth) for (int x = Cx - 2; x <= Cx + 1; x++) d.Mark(x, t + 8, Ramps.Ink, 1);
                break;
            case DotFace.Sad:
                if (mouth) { d.Mark(Cx - 2, t + 9, skin, 1); d.Mark(Cx + 1, t + 9, skin, 1); }
                if (d.RampAt(Cx - 5, ey) == skin) d.Mark(Cx - 5, ey, brow, 1);
                if (d.RampAt(Cx + 4, ey) == skin) d.Mark(Cx + 4, ey, brow, 1);
                break;
        }
    }

    /// <summary>옆모습 표정 (입 하나 · 눈 하나).</summary>
    private static void FaceSide(Dot d, PersonLook l, int t, int ex, int mx, int skin, int brow)
    {
        if (l.EyesClosed && l.Face != DotFace.Pain) return;
        bool mouth = !l.Beard;
        switch (l.Face)
        {
            case DotFace.Smile: if (mouth) d.Mark(mx - 1, t + 7, skin, 1); break;
            case DotFace.Fear:
                if (d.RampAt(ex, t + 3) == skin) d.Mark(ex, t + 3, brow, 1);
                if (mouth) { d.Mark(mx, t + 8, Ramps.Ink, 1); d.Mark(mx, t + 9, Ramps.Ink, 0); }
                break;
            case DotFace.Pain: d.Mark(ex - 1, t + 5, brow, 1); if (mouth) { d.Mark(mx, t + 8, Ramps.Ink, 1); d.Mark(mx - 1, t + 8, Ramps.Ink, 1); } break;
            case DotFace.Sad: if (mouth) d.Mark(mx - 1, t + 9, skin, 1); break;
        }
    }

    // ───────────────────────────── 손에 든 것 ─────────────────────────────

    private static void Held(Dot d, PersonLook l, Body b, DotPose pose, int frame, DotFacing face)
    {
        if (l.Held == DotHeld.None) return;
        d.Part = 50;
        bool side = face == DotFacing.E;
        int hx, hy;
        if (side)
        {
            hx = Cx + 7 + b.Hunch; hy = b.TorsoTop + 4;
            if (pose is not (DotPose.Work or DotPose.Carry)) { hx = Cx - 1; hy = b.TorsoTop + b.Torso; }
        }
        else if (pose is DotPose.Work or DotPose.Carry) { hx = Cx; hy = b.TorsoTop + 4 - ToolBob(l, frame); }
        else { hx = Cx + b.Shoulder + 1; hy = b.TorsoTop + b.Torso + 1; }
        if (ToMouth(l, frame)) { hx = side ? Cx + 5 + b.Hunch : Cx + 2; hy = b.HeadTop + b.HeadRows - 1; }   // 잔이 입에
        switch (l.Held)
        {
            case DotHeld.Wrench:
                if (side) { d.Line(hx, hy, hx + 4, hy - 2, Ramps.Steel, 4); d.Set(hx + 5, hy - 3, Ramps.Steel, 4); d.Set(hx + 5, hy - 2, Ramps.Steel, 3); d.Set(hx + 4, hy - 4, Ramps.Steel, 4); }
                else { for (int i = 0; i < 5; i++) d.Set(hx, hy + i, Ramps.Steel, 4); d.Set(hx - 1, hy + 5, Ramps.Steel, 4); d.Set(hx + 1, hy + 5, Ramps.Steel, 4); }
                break;
            case DotHeld.Welder:
                if (side) { d.Rect(hx, hy - 1, 4, 2, Ramps.Hazard, 2); d.Set(hx + 4, hy, Ramps.Steel, 4); d.Set(hx + 5, hy, Ramps.Steel, 5); }
                else { d.Rect(hx - 1, hy, 2, 4, Ramps.Hazard, 2); d.Set(hx, hy + 4, Ramps.Steel, 4); }
                break;
            case DotHeld.Tablet:
                if (side) { d.Rect(hx - 1, hy - 3, 2, 5, Ramps.Panel, 2); d.Mark(hx, hy - 2, Ramps.Cyan, 3); }
                else
                {
                    int tx = pose is DotPose.Work or DotPose.Carry ? Cx - 3 : hx - 1, ty = pose is DotPose.Work or DotPose.Carry ? b.TorsoTop + 3 : hy - 3;
                    d.Rect(tx, ty, 6, 4, Ramps.Panel, 2);
                    d.MarkRect(tx + 1, ty + 1, 4, 2, Ramps.Cyan, 2);
                    d.Mark(tx + 1, ty + 1, Ramps.Cyan, 4);
                }
                break;
            case DotHeld.Cup:
                d.Rect(hx - 1, hy - 2, 3, 3, Ramps.Bandage, 3); d.Set(hx + 2, hy - 1, Ramps.Bandage, 2); d.Mark(hx - 1, hy - 2, Ramps.Wood, 1); d.Mark(hx, hy - 2, Ramps.Wood, 1);
                break;
            case DotHeld.Box:
                {
                    int bx = side ? Cx + 3 + b.Hunch : Cx - 5, by = b.TorsoTop + 1;
                    d.Rect(bx, by, side ? 7 : 10, 7, Ramps.Wood, 3);
                    for (int x = bx; x < bx + (side ? 7 : 10); x++) d.Mark(x, by + 3, Ramps.Wood, 1);
                    d.Mark(bx + 1, by + 1, Ramps.Hazard, 3);
                    break;
                }
            case DotHeld.Extinguisher:
                d.Rect(hx - 1, hy - 4, 3, 6, Ramps.Alarm, 3); d.Set(hx, hy - 5, Ramps.Rubber, 2); d.Set(hx + 1, hy - 6, Ramps.Rubber, 2);
                break;
            case DotHeld.MedKit:
                {
                    int kx = side ? hx - 1 : hx - 2, ky = side ? hy - 2 : hy - 1;
                    d.Rect(kx, ky, 5, 4, Ramps.Bandage, 3);
                    d.Mark(kx + 2, ky + 1, Ramps.Alarm, 3); d.Mark(kx + 1, ky + 2, Ramps.Alarm, 3); d.Mark(kx + 2, ky + 2, Ramps.Alarm, 4); d.Mark(kx + 3, ky + 2, Ramps.Alarm, 3);
                    break;
                }
            case DotHeld.Plate:
                {
                    int px = pose is DotPose.Carry or DotPose.Work ? Cx - 3 : hx - 3, py = pose is DotPose.Carry or DotPose.Work ? b.TorsoTop + 3 : hy - 2;
                    d.Rect(px, py, 7, 2, Ramps.Steel, 4);
                    d.Mark(px + 2, py, Ramps.ClothOrange, 3); d.Mark(px + 3, py, Ramps.Leaf, 3); d.Mark(px + 4, py, Ramps.ClothYellow, 4);
                    break;
                }
            case DotHeld.Cable:
                for (int i = 0; i < 6; i++) d.Set(hx + (i & 1), hy + i - 2, Ramps.ClothOrange, 2 + (i & 1));
                break;
            case DotHeld.Mop:
                for (int y = b.TorsoTop - 2; y < Foot; y++) d.Set(hx, y, Ramps.Wood, 3);
                d.Rect(hx - 2, Foot - 1, 5, 2, Ramps.ClothBeige, 3);
                break;
        }
    }
}
