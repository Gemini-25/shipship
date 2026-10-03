using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 의료 1차 그림 표 — 수술실 · 혈액 냉장고 · 약장 (5종). 저마다 실루엣 · 무늬 · 움직임이 다르다.
///   수술대: 기둥 하나 + 십자 받침 · 머리 · 몸 · 다리 세 토막 패드 · 양옆으로 뻗은 팔 받침(T자) · 무중력 띠 —
///     수술 중이면 초록 덮개 · 기구 쟁반 반짝임 · 작은 모니터 심전도 · 진행 띠 · 피가 모자라면 붉은 경고등
///   무영등: 네 발 받침 · 꺾인 팔 · 둥근 머리에 전구 일곱 (꽃 모양) — 켜지면 수술대 쪽으로 빛 웅덩이 ·
///     정전이면 주황 비상 배터리 빛이 가물거리고 배터리 칸이 준다 · 꺼지면 캄캄
///   마취기: 바퀴 달린 수레 · 보라 · 노랑 기화기 통 · 유리 유량계 · 고무 호흡 주머니 · 주름관 고리 —
///     쓰는 동안 주머니가 숨 쉬듯 부풀고 유량계 구슬이 뜬다 · 전기가 끊기면 손으로 짜는 느린 박자
///   혈액 냉장고: 키 큰 유리문 · 선반 줄 · 핏방울 표지 · 온도 창 — 안의 혈액팩 수만큼 붉은 주머니 (형마다 띠 색) ·
///     차가우면 초록 4° · 미지근해지면 붉게 깜빡 · 문에 서리
///   약장: 벽에 단 쌍여닫이 유리장 · 자물쇠 · 칸마다 병 — 재고만큼 병이 늘고 준다 (진통제 흰 병 · 항생제 호박색 · 마취제 보라 앰플 · 대용제 투명 주머니 · 약초 초록 다발)
/// 그리기는 Core 상태를 읽기만 한다.
/// </summary>
public static partial class FixtureArt
{
    private static void MedArt1(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.OperatingTable] = new(OrTableBody, OrTableLife, OrTableFine, Look.Sparks, 0.5f, 0.95f);
        t[FurnitureType.SurgicalLamp] = new(OrLampBody, OrLampLife, OrLampFine, Look.Flicker, 0.5f, 0.5f);
        t[FurnitureType.AnesthesiaMachine] = new(AnesBody, AnesLife, AnesFine, Look.Gas, 0.3f, 0.3f);
        t[FurnitureType.BloodFridge] = new(BloodFridgeBody, BloodFridgeLife, BloodFridgeFine, Look.Leak, 0.5f, 0.95f);
        t[FurnitureType.MedCabinet] = new(CabinetBody, CabinetLife, CabinetFine, Look.Jam, 0.5f, 0.9f);
    }

    private static readonly Color MSteel = new("#c4ccd4"), MPad = new("#2f6f6a"), MDrape = new("#3f9a74"), MLampHead = new("#e8ecef"), MBulb = new("#fff6dc");
    private static readonly Color MCart = new("#d8dde2"), MVapPurple = new("#8e5ac8"), MVapYellow = new("#e0c040"), MBag = new("#2a2a30"), MO2 = new("#2a8a4a");
    private static readonly Color MFridge = new("#eef2f5"), MBlood = new("#a0141e"), MBloodHi = new("#d8323c"), MCab = new("#e6e2d8"), MWood = new("#8a6a48");
    private static readonly Color MPill = new("#f4f4f0"), MAmber = new("#c8781e"), MAmpoule = new("#a070d8"), MHerb = new("#4f9a3a");

    /// <summary>이 수술대에서 지금 하는 수술 (없으면 null).</summary>
    private static SurgeryCase? OrCase(in Fix x)
    {
        if (x.W == null) return null;
        foreach (var k in x.W.Surgery.Cases) if (k.Table == x.F.Id && k.State is CaseState.Prep or CaseState.Operating) return k;
        return null;
    }

    private static SurgeryCase? OrCaseInRoom(in Fix x)
    {
        if (x.W == null) return null;
        foreach (var k in x.W.Surgery.Cases)
            if (k.State is CaseState.Prep or CaseState.Operating && x.W.Surgery.TableOf(k) is Furniture tf && tf.Room == x.F.Room) return k;
        return null;
    }

    // ─────────────── 수술대 ───────────────

    private static void OrTableBody(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        // 십자 받침 + 기둥 (멀리서도 T자로 보인다)
        Line(ci, c + new Vector2(-x.Px(9f), x.Px(9f)), c + new Vector2(x.Px(9f), -x.Px(9f)), MSteel.Darkened(0.35f), x.Px(2.2f));
        Line(ci, c + new Vector2(-x.Px(9f), -x.Px(9f)), c + new Vector2(x.Px(9f), x.Px(9f)), MSteel.Darkened(0.35f), x.Px(2.2f));
        Dot(ci, c, x.Px(4.2f), MSteel.Darkened(0.2f));
        // 팔 받침 (양옆으로)
        Box(ci, new Rect2(c - x.V * x.Lv * 0.62f - x.U * x.Px(2.5f) - new Vector2(x.Px(1.5f), x.Px(1.5f)), x.U * x.Px(5f) + x.V * x.Lv * 0.24f + new Vector2(x.Px(3f), x.Px(3f))).Abs(), MPad.Darkened(0.15f), 1.5f, MSteel);
        Box(ci, new Rect2(c + x.V * x.Lv * 0.38f - x.U * x.Px(2.5f) - new Vector2(x.Px(1.5f), x.Px(1.5f)), x.U * x.Px(5f) + x.V * x.Lv * 0.24f + new Vector2(x.Px(3f), x.Px(3f))).Abs(), MPad.Darkened(0.15f), 1.5f, MSteel);
        // 세 토막 패드 (머리 · 몸 · 다리)
        Box(ci, x.Q(0.02f, 0.2f, 0.98f, 0.8f), MSteel, 2.5f, MSteel.Darkened(0.4f));
        Box(ci, x.Q(0.05f, 0.26f, 0.24f, 0.74f), MPad.Lightened(0.08f), 3f);
        Box(ci, x.Q(0.27f, 0.24f, 0.66f, 0.76f), MPad, 2f);
        Box(ci, x.Q(0.69f, 0.24f, 0.95f, 0.76f), MPad.Darkened(0.06f), 2f);
        // 무중력 띠 둘 (가슴 · 무릎)
        Line(ci, x.P(0.42f, 0.18f), x.P(0.42f, 0.82f), new Color("#1c1c22"), x.Px(1.4f));
        Line(ci, x.P(0.8f, 0.18f), x.P(0.8f, 0.82f), new Color("#1c1c22"), x.Px(1.4f));
        if (x.Tier >= 2) Box(ci, new Rect2(x.P(0.98f, 0.02f) - new Vector2(x.Px(3f), 0f), new Vector2(x.Px(6f), x.Px(5f))), new Color("#0f1418"), 1f, MSteel); // II: 머리맡 모니터
    }

    private static void OrTableLife(in Fix x)
    {
        var ci = x.Ci;
        var k = OrCase(x);
        var mon = new Rect2(x.P(0.98f, 0.02f) - new Vector2(x.Px(3f), 0f), new Vector2(x.Px(6f), x.Px(5f)));
        if (k == null)
        {
            if (x.Lit && x.Lod > 0) Led(ci, x.P(0.5f, 0.86f), Good, 0.25f * x.Glow, 0.8f); // 대기: 받침 불빛만
            return;
        }
        var pt = x.W?.Crew.Find(cm => cm.Id == k.Patient);
        // 덮개: 수술하는 동안 초록 천 (준비 땐 접힌 채 발치에)
        if (k.State == CaseState.Operating)
        {
            ci.DrawRect(x.Q(0.22f, 0.14f, 0.98f, 0.86f), MDrape.WithAlpha(0.72f));
            var hole = x.P(k.Part == BodyPart.Head ? 0.12f : k.Part is BodyPart.LeftLeg or BodyPart.RightLeg ? 0.78f : 0.48f, 0.5f);
            ci.DrawRect(new Rect2(hole - new Vector2(x.Px(2.5f), x.Px(2f)), new Vector2(x.Px(5f), x.Px(4f))), new Color("#d89a8a").WithAlpha(0.8f)); // 여는 자리
            if (x.Lod > 0) // 기구 쟁반 반짝임
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.PosMod(x.T * 0.7f + i * 0.33f, 1f);
                    if (ph < 0.12f) Dot(ci, hole + new Vector2(x.Px(-3f + i * 3f), x.Px(-3.5f)), x.Px(0.6f), Colors.White.WithAlpha(0.9f));
                }
            // 진행 띠 (발치)
            Line(ci, x.P(0.05f, 0.92f), x.P(0.05f + 0.9f * k.Progress, 0.92f), Good.WithAlpha(0.8f), x.Px(1.2f));
        }
        else ci.DrawRect(x.Q(0.8f, 0.16f, 0.95f, 0.84f), MDrape.WithAlpha(0.6f)); // 접힌 덮개
        // 모니터 심전도 (빨라지면 · 피가 모자라면 붉게)
        bool low = pt != null && pt.Vitals.Health < 0.35f;
        if (x.Lit || k.State == CaseState.Operating)
        {
            ci.DrawRect(mon, new Color("#05080a"));
            var pts = new Vector2[7];
            float beat = low ? 2.2f : 1.2f;
            for (int i = 0; i < 7; i++)
            {
                float u = i / 6f, ph = Mathf.PosMod(u * 2f - x.T * beat, 1f);
                float spike = ph > 0.45f && ph < 0.55f ? (ph < 0.5f ? -1f : 0.6f) : 0f;
                pts[i] = new Vector2(mon.Position.X + u * mon.Size.X, mon.GetCenter().Y + spike * x.Px(1.6f));
            }
            ci.DrawPolyline(pts, (low ? Danger : Ecg).WithAlpha(0.9f), 0.6f, true);
        }
        if (low) Led(ci, mon.End + new Vector2(x.Px(1f), 0f), Danger, Mathf.PosMod(x.T * 2f, 1f) < 0.5f ? 1f : 0.2f, x.Px(1.1f));
        // 수혈 중이면 링거 봉에 붉은 주머니
        if (k.Bloods > 0)
        {
            var pole = x.P(0.06f, 0.06f);
            Line(ci, pole, pole + new Vector2(0f, x.Px(6f)), Chrome, 0.8f);
            Box(ci, new Rect2(pole + new Vector2(-x.Px(2f), -x.Px(3f)), new Vector2(x.Px(4f), x.Px(4.5f))), MBlood.WithAlpha(0.9f), 1f, MBloodHi);
            float ph = Mathf.PosMod(x.T * 1.1f, 1f);
            Dot(ci, pole + new Vector2(0f, x.Px(2f + ph * 4f)), x.Px(0.5f), MBloodHi.WithAlpha(1f - ph));
        }
    }

    private static void OrTableFine(in Fix x)
    {
        var ci = x.Ci;
        for (int i = 0; i < 4; i++) Line(ci, x.P(0.3f + i * 0.09f, 0.3f), x.P(0.3f + i * 0.09f, 0.7f), new Color(0, 0, 0, 0.12f), 0.4f); // 패드 바느질
        Box(ci, new Rect2(x.P(0.42f, 0.16f) - new Vector2(x.Px(1.2f), x.Px(1.2f)), new Vector2(x.Px(2.4f), x.Px(2.4f))), Chrome, 0.5f); // 띠 버클
        Box(ci, new Rect2(x.P(0.8f, 0.16f) - new Vector2(x.Px(1.2f), x.Px(1.2f)), new Vector2(x.Px(2.4f), x.Px(2.4f))), Chrome, 0.5f);
        Dot(ci, x.C + x.V * x.Px(6f) + x.U * x.Px(6f), x.Px(1.4f), new Color("#3a3a40")); // 높이 발판
        Bolts(ci, x.Q(0.02f, 0.2f, 0.98f, 0.8f), 1.6f, 0.45f);
    }

    // ─────────────── 무영등 ───────────────

    private static void OrLampBody(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        for (int i = 0; i < 4; i++) // 네 발
        {
            var d = Vector2.FromAngle(Mathf.Pi / 4f + i * Mathf.Pi / 2f) * x.Px(10f);
            Line(ci, c, c + d, MSteel.Darkened(0.4f), x.Px(1.6f));
            Dot(ci, c + d, x.Px(1.4f), Rubber);
        }
        var head = c + new Vector2(x.Px(2f), -x.Px(2f));
        Line(ci, c, c + new Vector2(-x.Px(4f), -x.Px(6f)), MSteel, x.Px(1.8f)); // 꺾인 팔
        Line(ci, c + new Vector2(-x.Px(4f), -x.Px(6f)), head, MSteel, x.Px(1.6f));
        Dot(ci, head, x.Px(8.2f), MLampHead.Darkened(0.25f));
        Dot(ci, head, x.Px(7.4f), MLampHead);
        // 전구 일곱 (꽃 모양)
        Dot(ci, head, x.Px(1.9f), MBulb.Darkened(0.35f));
        for (int i = 0; i < 6; i++) Dot(ci, head + Vector2.FromAngle(i * Mathf.Tau / 6f) * x.Px(4.4f), x.Px(1.7f), MBulb.Darkened(0.35f));
        if (x.Tier >= 2) Ring(ci, head, x.Px(9.2f), Cyan.WithAlpha(0.5f), 0.8f); // II: 둘레 띠
    }

    private static void OrLampLife(in Fix x)
    {
        var ci = x.Ci;
        var head = x.C + new Vector2(x.Px(2f), -x.Px(2f));
        var k = OrCaseInRoom(x);
        float charge = x.W != null ? x.W.Surgery.LampCharge(x.F) : 40f;
        bool battery = x.M != null && !x.M.Powered && k != null && charge > 0f;
        bool on = x.Lit && (k != null || x.St == State.Running);
        if (!on && !battery) return;
        Color col = battery ? Amber : MBulb;
        float a = battery ? 0.45f + 0.25f * (Hash(x.Id, (int)(x.T * 9f), 3) > 0.25f ? 1f : 0.3f) : 0.95f;
        Dot(ci, head, x.Px(1.9f), col.WithAlpha(a));
        for (int i = 0; i < 6; i++) Dot(ci, head + Vector2.FromAngle(i * Mathf.Tau / 6f) * x.Px(4.4f), x.Px(1.7f), col.WithAlpha(a));
        if (k != null && x.Lod > 0 && x.W?.Surgery.TableOf(k) is Furniture tf)
        {
            // 수술대로 떨어지는 빛 웅덩이
            var pool = ShipView.FurnitureRect(tf).GetCenter();
            var dir = pool - head;
            var side = new Vector2(-dir.Y, dir.X).Normalized() * x.Px(battery ? 5f : 8f);
            ci.DrawColoredPolygon(new[] { head + side * 0.6f, head - side * 0.6f, pool - side * 1.4f, pool + side * 1.4f }, col.WithAlpha(battery ? 0.07f : 0.12f));
            ci.DrawCircle(pool, x.Px(battery ? 6f : 10f), col.WithAlpha(battery ? 0.08f : 0.14f));
        }
        if (battery) // 배터리 칸 (40분 → 4칸)
            for (int i = 0; i < 4; i++)
                ci.DrawRect(new Rect2(x.P(0.7f, 0.9f) + new Vector2(i * x.Px(2.2f), 0f), new Vector2(x.Px(1.8f), x.Px(2.4f))), (charge > i * 10f ? Amber : new Color("#3a2a10")).WithAlpha(0.9f));
    }

    private static void OrLampFine(in Fix x)
    {
        var ci = x.Ci;
        var head = x.C + new Vector2(x.Px(2f), -x.Px(2f));
        Ring(ci, head, x.Px(1.2f), new Color("#5a6470"), 0.6f); // 멸균 손잡이
        for (int i = 0; i < 12; i++) Dot(ci, head + Vector2.FromAngle(i * Mathf.Tau / 12f) * x.Px(6.8f), x.Px(0.3f), new Color("#8a949e")); // 바람 구멍
        Bolt(ci, x.C + new Vector2(-x.Px(4f), -x.Px(6f)), 0.5f); // 팔 마디
    }

    // ─────────────── 마취기 ───────────────

    private static void AnesBody(in Fix x)
    {
        var ci = x.Ci;
        foreach (var p in new[] { x.P(0.06f, 0.95f), x.P(0.94f, 0.95f), x.P(0.06f, 0.05f), x.P(0.94f, 0.05f) }) Dot(ci, p, x.Px(1.4f), Rubber); // 바퀴
        Box(ci, x.Q(0.04f, 0.1f, 0.96f, 0.9f), MCart, 2.5f, MCart.Darkened(0.35f));
        Can(ci, x.P(0.2f, 0.32f), x.Px(3.2f), MVapPurple, MVapPurple.Darkened(0.3f)); // 기화기 둘
        Can(ci, x.P(0.42f, 0.32f), x.Px(3.2f), MVapYellow, MVapYellow.Darkened(0.3f));
        Can(ci, x.P(0.2f, 0.74f), x.Px(2.6f), MO2, MO2.Darkened(0.3f)); // 산소통
        for (int i = 0; i < 3; i++) Glass(ci, x.Q(0.56f + i * 0.1f, 0.18f, 0.62f + i * 0.1f, 0.58f), new Color("#cfe8f0"), 1f); // 유량계 유리관
        // 주름관 고리 + 호흡 주머니 자리
        ci.DrawArc(x.P(0.62f, 0.78f), x.Px(4.5f), 0f, Mathf.Pi * 1.5f, 14, new Color("#3a5a7a"), x.Px(1.6f), true);
        Dot(ci, x.P(0.88f, 0.74f), x.Px(2.6f), MBag);
    }

    private static void AnesLife(in Fix x)
    {
        var ci = x.Ci;
        var k = OrCaseInRoom(x);
        bool use = k != null && k.State == CaseState.Operating && k.Anesthesia;
        bool hand = use && x.M != null && !x.M.Powered;
        float rate = hand ? 0.45f : 0.9f;
        float br = use ? 0.5f + 0.5f * Mathf.Sin(x.T * Mathf.Tau * rate * 0.5f) : 0f;
        Dot(ci, x.P(0.88f, 0.74f), x.Px(2.6f + 1.6f * br), MBag.Lightened(0.08f)); // 숨 쉬는 주머니
        if (use) Ring(ci, x.P(0.88f, 0.74f), x.Px(2.6f + 1.6f * br), new Color("#5a5a66"), 0.5f);
        // 유량계 구슬
        for (int i = 0; i < 3; i++)
        {
            float lvl = use ? 0.25f + 0.15f * i + 0.04f * Mathf.Sin(x.T * 3f + i) : 0.02f;
            var tube = x.Q(0.56f + i * 0.1f, 0.18f, 0.62f + i * 0.1f, 0.58f);
            Dot(ci, new Vector2(tube.GetCenter().X, tube.End.Y - tube.Size.Y * lvl), x.Px(0.9f), (i == 0 ? MO2 : i == 1 ? Colors.White : MVapPurple).WithAlpha(0.95f));
        }
        if (x.Lit && x.Lod > 0) // CO2 파형 창
        {
            var scr = x.Q(0.32f, 0.62f, 0.52f, 0.86f);
            ci.DrawRect(scr, new Color("#05080a"));
            if (use && !hand)
            {
                var pts = new Vector2[6];
                for (int i = 0; i < 6; i++) { float u = i / 5f, ph = Mathf.PosMod(u - x.T * 0.4f, 1f); pts[i] = new Vector2(scr.Position.X + u * scr.Size.X, scr.End.Y - scr.Size.Y * (ph < 0.5f ? 0.75f : 0.2f)); }
                ci.DrawPolyline(pts, Amber.WithAlpha(0.9f), 0.6f, true);
            }
        }
        if (hand) Led(ci, x.P(0.5f, 0.12f), Danger, Mathf.PosMod(x.T, 1f) < 0.5f ? 0.9f : 0.2f, x.Px(1f)); // 전기 없음 — 손으로 짠다
    }

    private static void AnesFine(in Fix x)
    {
        var ci = x.Ci;
        Knob(ci, x.P(0.2f, 0.32f), x.Px(1.2f), 0.6f, Colors.White);
        Knob(ci, x.P(0.42f, 0.32f), x.Px(1.2f), -0.4f, Colors.White);
        for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) Line(ci, x.P(0.555f + i * 0.1f, 0.22f + j * 0.09f), x.P(0.57f + i * 0.1f, 0.22f + j * 0.09f), Colors.Black.WithAlpha(0.4f), 0.3f); // 눈금
        Tag(ci, x.P(0.2f, 0.88f), "O₂", 5, Colors.White.WithAlpha(0.8f));
    }

    // ─────────────── 혈액 냉장고 ───────────────

    private static void BloodFridgeBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.04f, 0.02f, 0.96f, 0.98f), MFridge, 2.5f, MFridge.Darkened(0.35f));
        Glass(ci, x.Q(0.12f, 0.1f, 0.88f, 0.78f), new Color("#a8d8f0"), 1.5f); // 유리문
        for (int i = 1; i < 4; i++) Line(ci, x.P(0.12f, 0.1f + i * 0.17f), x.P(0.88f, 0.1f + i * 0.17f), MSteel.Darkened(0.2f), 0.8f); // 선반
        // 핏방울 표지
        var d = x.P(0.3f, 0.88f);
        ci.DrawColoredPolygon(new[] { d + new Vector2(0f, -x.Px(3.2f)), d + new Vector2(x.Px(2f), x.Px(0.4f)), d + new Vector2(0f, x.Px(2f)), d + new Vector2(-x.Px(2f), x.Px(0.4f)) }, MBlood);
        Box(ci, x.Q(0.5f, 0.82f, 0.88f, 0.94f), new Color("#0a0f12"), 1f); // 온도 창
        Line(ci, x.P(0.92f, 0.2f), x.P(0.92f, 0.6f), Chrome, x.Px(1.2f)); // 손잡이
    }

    private static void BloodFridgeLife(in Fix x)
    {
        var ci = x.Ci;
        int n = 0;
        if (x.W != null) foreach (var p in x.W.Blood.Packs) if (p.Fridge == x.F.Id && !p.Spoiled) n++;
        bool cold = x.M == null || x.M.Powered && x.M.Efficiency > 0.2f;
        // 혈액팩: 선반마다 넷 (형마다 띠 색)
        int i = 0;
        if (x.W != null)
            foreach (var p in x.W.Blood.Packs)
            {
                if (p.Fridge != x.F.Id || p.Spoiled || i >= 12) continue;
                int row = i / 4, col = i % 4;
                var r = x.Q(0.16f + col * 0.18f, 0.12f + row * 0.17f, 0.3f + col * 0.18f, 0.25f + row * 0.17f);
                Box(ci, r, (p.Warm > 1f ? MBlood.Darkened(0.3f) : MBlood).WithAlpha(0.92f), 1f, MBloodHi.WithAlpha(0.6f));
                var band = p.Type.Group switch { BloodGroup.O => Colors.White, BloodGroup.A => new Color("#5a9ae0"), BloodGroup.B => new Color("#e0c040"), _ => new Color("#c070e0") };
                ci.DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, Mathf.Max(1f, r.Size.Y * 0.22f))), band.WithAlpha(p.Type.Neg ? 0.5f : 0.95f));
                i++;
            }
        // 온도 창: 차가우면 초록 4° · 미지근하면 붉게 깜빡
        var win = x.Q(0.5f, 0.82f, 0.88f, 0.94f);
        if (x.M == null || x.M.Powered || n > 0)
        {
            var c = cold ? Good : Danger;
            float a = cold ? 0.9f : Mathf.PosMod(x.T * 1.5f, 1f) < 0.5f ? 1f : 0.2f;
            if (x.Lod > 0) Tag(ci, win.GetCenter(), cold ? "4°" : "!°", 5, c.WithAlpha(a));
            else ci.DrawRect(win.Grow(-1f), c.WithAlpha(0.5f * a));
        }
        if (cold && x.Lit && x.Lod > 0) // 서리 · 압축기 숨
        {
            float ph = Mathf.PosMod(x.T * 0.2f + x.Id * 0.13f, 1f);
            Dot(ci, x.P(0.2f + ph * 0.6f, 0.08f), x.Px(0.6f), Colors.White.WithAlpha(0.35f * (1f - ph)));
        }
    }

    private static void BloodFridgeFine(in Fix x)
    {
        var ci = x.Ci;
        Line(ci, x.P(0.1f, 0.08f), x.P(0.1f, 0.8f), new Color("#6a7480"), 0.6f); // 문 고무
        Bolt(ci, x.P(0.06f, 0.15f), 0.5f); Bolt(ci, x.P(0.06f, 0.7f), 0.5f); // 경첩
        for (int i = 0; i < 5; i++) Line(ci, x.P(0.1f + i * 0.08f, 0.96f), x.P(0.14f + i * 0.08f, 0.96f), new Color("#3a3a40"), 0.4f); // 바닥 통풍
    }

    // ─────────────── 약장 ───────────────

    private static void CabinetBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.02f, 0.04f, 0.98f, 0.96f), MCab, 2f, MWood);
        Glass(ci, x.Q(0.06f, 0.08f, 0.48f, 0.9f), new Color("#d8eef0"), 1f); // 쌍여닫이 유리
        Glass(ci, x.Q(0.52f, 0.08f, 0.94f, 0.9f), new Color("#d8eef0"), 1f);
        for (int i = 1; i < 3; i++) Line(ci, x.P(0.06f, 0.08f + i * 0.27f), x.P(0.94f, 0.08f + i * 0.27f), MWood.Lightened(0.2f), 0.8f);
        // 초록 십자
        var c = x.P(0.5f, 0.02f);
        ci.DrawRect(new Rect2(c - new Vector2(x.Px(0.8f), x.Px(0.2f)), new Vector2(x.Px(1.6f), x.Px(3.4f))), MO2);
        ci.DrawRect(new Rect2(c + new Vector2(-x.Px(1.7f), x.Px(0.8f)), new Vector2(x.Px(3.4f), x.Px(1.4f))), MO2);
    }

    private static void CabinetLife(in Fix x)
    {
        var ci = x.Ci;
        var st = x.F.Storage;
        CabRow(x, st, 0, ItemKind.Painkiller, 5, 0.04f, MPill, 0);
        CabRow(x, st, 0, ItemKind.Antibiotic, 5, 0.5f, MAmber, 0);
        CabRow(x, st, 1, ItemKind.Anesthetic, 5, 0.04f, MAmpoule, 1);
        CabRow(x, st, 1, ItemKind.BloodSubstitute, 3, 0.5f, new Color("#e8f0ff"), 2);
        CabRow(x, st, 2, ItemKind.MedHerb, 4, 0.04f, MHerb, 3);
        CabRow(x, st, 2, ItemKind.MedKit, 3, 0.5f, new Color("#e05a5a"), 0);
        if (x.User != null) // 누가 문을 열었다 — 유리문이 젖혀진다
            ci.DrawRect(x.Q(0.52f, 0.08f, 0.94f, 0.9f), new Color("#d8eef0").WithAlpha(0.25f));
    }

    /// <summary>약장 한 칸: 재고만큼 병 · 앰플 · 주머니 · 다발.</summary>
    private static void CabRow(in Fix x, Inventory? st, int row, ItemKind k, int max, float u0, Color col, int shape)
    {
        var ci = x.Ci;
        int n = Mathf.Min(st?.Count(k) ?? 0, max);
        for (int i = 0; i < n; i++)
        {
            var p = x.P(u0 + 0.06f + i * (0.38f / max), 0.26f + row * 0.27f);
            switch (shape)
            {
                case 0: Box(ci, new Rect2(p - new Vector2(x.Px(1f), x.Px(2.4f)), new Vector2(x.Px(2f), x.Px(2.6f))), col, 0.5f, col.Darkened(0.3f)); Line(ci, p - new Vector2(x.Px(1f), x.Px(2.4f)), p - new Vector2(-x.Px(1f), x.Px(2.4f)), Colors.White, 0.6f); break; // 병 + 뚜껑
                case 1: Line(ci, p - new Vector2(0f, x.Px(2.6f)), p, col, x.Px(1f)); Dot(ci, p - new Vector2(0f, x.Px(2.8f)), x.Px(0.5f), col.Lightened(0.3f)); break; // 앰플
                case 2: Box(ci, new Rect2(p - new Vector2(x.Px(1.4f), x.Px(2.4f)), new Vector2(x.Px(2.8f), x.Px(2.6f))), col.WithAlpha(0.45f), 1f, Colors.White.WithAlpha(0.6f)); break; // 투명 주머니
                default: for (int j = -1; j <= 1; j++) Line(ci, p, p + new Vector2(j * x.Px(0.9f), -x.Px(2.6f)), col, 0.7f); break; // 약초 다발
            }
        }
    }

    private static void CabinetFine(in Fix x)
    {
        var ci = x.Ci;
        Dot(ci, x.P(0.5f, 0.5f), x.Px(1.1f), Brass); // 자물쇠
        Dot(ci, x.P(0.5f, 0.5f), x.Px(0.35f), Rubber);
        Bolt(ci, x.P(0.03f, 0.2f), 0.4f); Bolt(ci, x.P(0.03f, 0.8f), 0.4f); Bolt(ci, x.P(0.97f, 0.2f), 0.4f); Bolt(ci, x.P(0.97f, 0.8f), 0.4f);
        for (int i = 0; i < 3; i++) Line(ci, x.P(0.1f, 0.3f + i * 0.27f), x.P(0.2f, 0.3f + i * 0.27f), MWood.Darkened(0.2f), 0.4f); // 칸 이름표
    }
}
