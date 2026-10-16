using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 그림 표 — 정비 · 제작 · 채집 (13종).
/// 작업대(바이스 · 타공판 공구 · 서랍 · 작업등 · 톱니) · 정밀 가공기(갠트리 · 노즐 · 쌓이는 층) · 부품 시험대(물림쇠 · 바늘 · 시험선)
/// · 호이스트(A자 틀 · 체인 드럼 · 갈고리) · 정비 카트(서랍 셋 · 바퀴 · 밀대) · 선반(척 · 심압대 · 공구대 · 칩) · 납땜대(인두 · 스펀지 · 돋보기 팔 · 기판)
/// · 공구 벽(그림자판 · 꺼내 간 자리) · 교정 장비(광학 레일 · 레이저 · 과녁) · 채집 장치(호퍼 · 선체 밖 팔) · 정제기(도가니 · 유도 코일 · 쇳물)
/// · 드론 거치대(받침 셋 · 유도등) · 로봇 충전대(미끄럼 방지 패드 · 단자).
/// </summary>
public static partial class FixtureArt
{
    private static void WorkArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.Workbench] = new(WorkbenchBody, WorkbenchLife, WorkbenchFine, Look.Sparks, 0.6f, 0.4f);
        t[FurnitureType.Fabricator] = new(FabricatorBody, FabricatorLife, FabricatorFine, Look.Smoke, 0.5f, 0.5f);
        t[FurnitureType.PartTestBench] = new(TestBenchBody, TestBenchLife, TestBenchFine, Look.Sparks, 0.5f, 0.85f);
        t[FurnitureType.Hoist] = new(HoistBody, HoistLife, HoistFine, Look.Jam, 0.5f, 0.15f);
        t[FurnitureType.MaintCart] = new(CartBody, CartLife, CartFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.Lathe] = new(LatheBody, LatheLife, LatheFine, Look.Grind, 0.35f, 0.5f);
        t[FurnitureType.SolderStation] = new(SolderBody, SolderLife, SolderFine, Look.Heat, 0.3f, 0.3f);
        t[FurnitureType.ToolWall] = new(ToolWallBody, ToolWallLife, ToolWallFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.CalibrationRig] = new(CalibBody, CalibLife, CalibFine, Look.Flicker, 0.88f, 0.5f);
        t[FurnitureType.Collector] = new(CollectorBody, CollectorLife, CollectorFine, Look.Grind, 0.5f, 0.0f);
        t[FurnitureType.Refinery] = new(RefineryBody, RefineryLife, RefineryFine, Look.Smoke, 0.5f, 0.4f);
        t[FurnitureType.DroneDock] = new(DroneDockBody, DroneDockLife, DroneDockFine, Look.Sparks, 0.1f, 0.5f);
        t[FurnitureType.RobotDock] = new(RobotDockBody, RobotDockLife, RobotDockFine, Look.Sparks, 0.5f, 0.2f);
    }

    private static readonly Color Wood = new("#4a3a26");
    private static readonly Color Laser = new("#ff4a3a");
    private static readonly Color Molten = new("#ffb04a");

    private static bool SomeoneWorkingIn(in Fix x)
    {
        if (x.W == null) return false;
        foreach (var c in x.W.Crew) if (c.Room == x.F.Room && c.Pose == Pose.Working) return true;
        return false;
    }

    // ─────────────── 작업대 ───────────────

    private static void WorkbenchBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#2c261d"), 3, new Color("#54482f"));
        for (int k = 1; k < 4; k++) Line(ci, x.P(0.02f, 0.2f * k + 0.1f), x.P(0.98f, 0.2f * k + 0.1f), Wood.Darkened(0.2f), 1f); // 상판 널
        ci.Box(x.Q(0.0f, 0.0f, 1f, 0.16f), new Color("#22201a")); // 뒤쪽 타공판
        for (int k = 0; k < 14; k++) Dot(ci, x.P(0.03f + k * 0.07f, 0.08f), 0.5f, new Color("#0e0d0a"));
        Line(ci, x.P(0.22f, 0.04f), x.P(0.22f, 0.15f), new Color("#9aa6b5"), 1.4f); // 걸린 렌치
        Ring(ci, x.P(0.22f, 0.04f), 1.6f, new Color("#9aa6b5"), 1f, 10);
        Line(ci, x.P(0.32f, 0.03f), x.P(0.32f, 0.15f), new Color("#c0392b"), 2f); // 드라이버
        Line(ci, x.P(0.4f, 0.05f), x.P(0.46f, 0.05f), new Color("#6a6050"), 2.4f); // 망치
        Line(ci, x.P(0.43f, 0.05f), x.P(0.43f, 0.15f), Wood, 1.4f);
        var vise = x.Q(0.02f, 0.55f, 0.13f, 0.92f);
        Box(ci, vise, new Color("#4a5566"), 2, new Color("#6a7486")); // 바이스
        Line(ci, x.P(0.075f, 0.55f), x.P(0.075f, 0.42f), Chrome, 1.4f);
        Line(ci, x.P(0.04f, 0.42f), x.P(0.11f, 0.42f), Chrome, 1.4f); // 바이스 손잡이
        var gear = x.P(0.55f, 0.55f);
        for (int k = 0; k < 8; k++) Line(ci, gear + Vector2.FromAngle(k * Mathf.Tau / 8f) * 3f, gear + Vector2.FromAngle(k * Mathf.Tau / 8f) * 5f, new Color("#8a8f99"), 1.6f); // 손보는 톱니
        Dot(ci, gear, 3.5f, new Color("#6a6f79"));
        Dot(ci, gear, 1.2f, new Color("#2c261d"));
        for (int k = 0; k < 3; k++) // 앞 서랍
        {
            var d = x.Q(0.22f + k * 0.22f, 0.86f, 0.4f + k * 0.22f, 0.98f);
            Box(ci, d, new Color("#24201a"), 1, new Color("#3a3226"));
            Line(ci, d.GetCenter() - x.U * 2f, d.GetCenter() + x.U * 2f, Chrome, 1f);
        }
        Line(ci, x.P(0.96f, 0.85f), x.P(0.9f, 0.3f), new Color("#3a3a3a"), 1.4f); // 작업등 팔
        Dot(ci, x.P(0.9f, 0.3f), 2.4f, new Color("#3b3a2f"));
    }

    private static void WorkbenchLife(in Fix x)
    {
        var ci = x.Ci;
        bool working = x.User != null;
        var lamp = x.P(0.9f, 0.3f);
        float lit = working ? 1f : x.On ? 0.25f : 0f;
        Led(ci, lamp, new Color("#f5d547"), lit * 0.85f, 1.6f);
        if (lit > 0.5f && x.Lod > 0) Dot(ci, x.P(0.6f, 0.5f), x.Lu * 0.22f, new Color("#f5d547").WithAlpha(0.06f)); // 작업등 불빛 웅덩이
        if (!working) return;
        var gear = x.P(0.55f, 0.55f);
        float a = x.T * 4f; // 드라이버가 톱니를 돌린다
        Line(ci, gear + Vector2.FromAngle(a) * 2f, gear + Vector2.FromAngle(a) * 9f, new Color("#c0392b"), 1.6f);
        if (x.Lod > 0 && Mathf.PosMod(x.T * 0.6f, 1f) < 0.12f) // 줄질 불똥
            for (int k = 0; k < 4; k++) Line(ci, gear, gear + Vector2.FromAngle(-Mathf.Pi / 2f + (k - 1.5f) * 0.4f) * 6f, SparkHot, 0.8f);
    }

    private static void WorkbenchFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.7f);
        for (int k = 0; k < 4; k++) Bolt(ci, x.P(0.02f + k * 0.04f, 0.6f), 0.45f);
        for (int k = 0; k < 3; k++) Tag(ci, x.P(0.31f + k * 0.22f, 0.92f) - x.V * 2.5f, ((char)('A' + k)).ToString(), 4, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── 정밀 가공기 ───────────────

    private static void FabricatorBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a1814"), 3, new Color("#6b5a3a"), 2);
        Line(ci, x.P(0.04f, 0.12f), x.P(0.96f, 0.12f), new Color("#4a4030"), 2.2f); // 갠트리 레일
        Line(ci, x.P(0.04f, 0.88f), x.P(0.96f, 0.88f), new Color("#4a4030"), 2.2f);
        var bed = x.Q(0.14f, 0.3f, 0.86f, 0.78f);
        Box(ci, bed, new Color("#2a261f"), 2);
        Grille(ci, bed, 3f, new Color("#3a3428"), 0.6f); // 작업판 격자
        ci.Box(x.Q(0.0f, 0.4f, 0.08f, 0.6f), new Color("#3a3228")); // 재료 실 통
        Ring(ci, x.P(0.04f, 0.5f), 2f, Brass, 1f, 10);
    }

    private static void FabricatorLife(in Fix x)
    {
        var ci = x.Ci;
        bool on = x.On;
        float ph = on ? 0.5f + 0.5f * Mathf.Sin(x.T * 1.7f * x.Spin) : 0.5f;
        float cycle = Mathf.PosMod(x.T * 0.03f, 1f);
        var bed = x.Q(0.2f, 0.36f, 0.8f, 0.72f);
        int layers = on ? (int)(cycle * 6f) : 2; // 쌓여 가는 출력물
        for (int k = 0; k < layers; k++) ci.Box(new Rect2(bed.GetCenter() - new Vector2(4f - k * 0.3f, -2f + k * 1.1f), new Vector2(8f - k * 0.6f, 1f)), new Color("#c8b27a").Darkened(k * 0.05f));
        Line(ci, x.P(Mathf.Lerp(0.1f, 0.9f, ph), 0.12f), x.P(Mathf.Lerp(0.1f, 0.9f, ph), 0.88f), new Color("#6b5a3a"), 1.2f); // 가로 들보
        float hv = on ? 0.5f + 0.25f * Mathf.Sin(x.T * 2.3f) : 0.5f;
        var nozzle = x.P(Mathf.Lerp(0.1f, 0.9f, ph), hv);
        ci.Box(new Rect2(nozzle - new Vector2(3f, 2.5f), new Vector2(6f, 5f)), new Color("#c8b27a"));
        if (on)
        {
            Dot(ci, nozzle, 2f, Laser.WithAlpha(0.8f * x.Glow));
            Dot(ci, nozzle, 5f, Laser.WithAlpha(0.12f * x.Glow));
        }
    }

    private static void FabricatorFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        Tag(ci, x.P(0.5f, 0.95f) - x.V * 1f, "FAB", 4, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── 부품 시험대 ───────────────

    private static void TestBenchBody(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        Box(ci, x.B, new Color("#17191d"), 3, new Color("#7a6a3a"), 2);
        ci.Box(x.Q(0.05f, 0.75f, 0.95f, 0.9f), new Color("#2b2a26")); // 물림쇠 레일
        Box(ci, x.Q(0.1f, 0.68f, 0.28f, 0.95f), new Color("#4a5566"), 1); // 물림쇠 턱
        Box(ci, x.Q(0.72f, 0.68f, 0.9f, 0.95f), new Color("#4a5566"), 1);
        float rad = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.22f;
        Dot(ci, c + new Vector2(0, -3), rad, new Color("#0e1013"));
        ci.Arc(c + new Vector2(0, -3), rad, 0f, Mathf.Tau, 18, new Color("#a08c4a"), 1.2f, true);
        Cable(ci, x.P(0.92f, 0.1f), x.P(0.7f, 0.8f), 3f, new Color("#c0392b"), 1f); // 시험선 (빨강 · 검정)
        Cable(ci, x.P(0.96f, 0.14f), x.P(0.3f, 0.8f), 4f, new Color("#1a1a1a"), 1f);
    }

    private static void TestBenchLife(in Fix x)
    {
        var ci = x.Ci;
        bool testing = false;
        if (x.On && x.W != null)
            foreach (var c in x.W.Crew)
                if (c.Room == x.F.Room && c.Job?.Activity is PartTestActivity && c.Pose == Pose.Working) { testing = true; break; }
        float rad = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.2f;
        float a = -Mathf.Pi * 0.8f + (testing ? 0.6f + 0.25f * Mathf.Sin(x.T * 9f) : 0.1f);
        var o = x.C + new Vector2(0, -3);
        Line(ci, o, o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad, testing ? new Color("#ffd27a") : new Color("#6d6450"), 1.2f);
        if (testing && x.Lod > 0) // 물린 부품이 돈다
        {
            var p = x.P(0.5f, 0.82f);
            for (int k = 0; k < 3; k++) Line(ci, p, p + Vector2.FromAngle(x.T * 12f + k * Mathf.Tau / 3f) * 3f, Chrome, 1f);
        }
        Led(ci, x.P(0.1f, 0.12f), testing ? Amber : Good, Mathf.Max(0.15f, x.Glow) * (testing ? Pulse(x.T, 5f) : 0.6f), 1f);
    }

    private static void TestBenchFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        float rad = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.22f;
        for (int k = 0; k < 7; k++) Line(ci, x.C + new Vector2(0, -3) + Vector2.FromAngle(-Mathf.Pi * 0.8f + k * 0.2f) * rad * 0.8f, x.C + new Vector2(0, -3) + Vector2.FromAngle(-Mathf.Pi * 0.8f + k * 0.2f) * rad, new Color("#a08c4a"), 0.6f);
    }

    // ─────────────── 호이스트 ───────────────

    private static void HoistBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-3f);
        foreach (var p in new[] { r.Position, new Vector2(r.End.X, r.Position.Y), new Vector2(r.Position.X, r.End.Y), r.End })
            ci.Box(new Rect2(p - new Vector2(2f, 2f), new Vector2(4f, 4f)), new Color("#e0b64a").Darkened(0.3f)); // 다리 받침
        Line(ci, r.Position, new Vector2(r.GetCenter().X, r.Position.Y + 2f), new Color("#8a8f99"), 2f); // A자 틀
        Line(ci, new Vector2(r.End.X, r.Position.Y), new Vector2(r.GetCenter().X, r.Position.Y + 2f), new Color("#8a8f99"), 2f);
        Line(ci, new Vector2(r.Position.X, r.End.Y), new Vector2(r.Position.X + 3f, r.Position.Y + 2f), new Color("#5a5f69"), 2f);
        Line(ci, r.End, new Vector2(r.End.X - 3f, r.Position.Y + 2f), new Color("#5a5f69"), 2f);
        Line(ci, new Vector2(r.Position.X + 2f, r.Position.Y + 2f), new Vector2(r.End.X - 2f, r.Position.Y + 2f), WarnYellow, 3f); // 위 들보
        var drum = new Vector2(r.GetCenter().X, r.Position.Y + 2f);
        Dot(ci, drum, 3.2f, new Color("#3a3f48")); // 체인 드럼
        Ring(ci, drum, 3.2f, Chrome, 0.8f, 12);
    }

    private static void HoistLife(in Fix x)
    {
        var ci = x.Ci;
        bool lifting = false;
        if (x.On && x.W != null)
            foreach (var c in x.W.Crew)
                if (c.Job?.Order is WorkOrder wo && wo.Kind == WorkKind.Repair && c.Pose == Pose.Working && wo.Target.Furniture?.Machine is Machine tm && tm.Faults.Exists(fl => PartsSystem.Heavy(fl.Part)))
                { lifting = true; break; }
        var r = x.R.Grow(-3f);
        float drop = lifting ? r.Size.Y * 0.62f : r.Size.Y * 0.22f;
        float sway = lifting ? Mathf.Sin(x.T * 2.2f) * 2f : 0f;
        var top = new Vector2(r.GetCenter().X, r.Position.Y + 4f);
        var hook = top + new Vector2(sway, drop);
        int links = (int)(drop / 2.5f);
        for (int k = 0; k < links; k++) // 체인 고리
        {
            var p = top.Lerp(hook, (k + 0.5f) / links);
            ci.Arc(p, 1f, 0f, Mathf.Tau, 6, new Color("#b9bec8"), 0.7f, true);
        }
        ci.Arc(hook + new Vector2(0, 2), 2.5f, 0f, Mathf.Pi, 6, new Color("#e0b040"), 1.4f, true);
        if (lifting) Box(ci, new Rect2(hook + new Vector2(-4f, 4f), new Vector2(8f, 6f)), new Color("#5a6474"), 1.5f, Chrome); // 매달린 부품
        Dot(ci, top - new Vector2(0f, 2f), 1f, (lifting ? Amber : x.On ? Good : Palette.TextMuted).WithAlpha(0.9f)); // 드럼 표시등
    }

    private static void HoistFine(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-3f);
        for (int k = 0; k < 5; k++) Line(ci, new Vector2(r.Position.X + 4f + k * (r.Size.X - 8f) / 5f, r.Position.Y + 0.8f), new Vector2(r.Position.X + 6f + k * (r.Size.X - 8f) / 5f, r.Position.Y + 3.2f), WarnBlack, 1f); // 들보 빗금
        Tag(ci, new Vector2(r.GetCenter().X, r.End.Y - 3f), "500kg", 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 정비 카트 ───────────────

    private static void CartBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R;
        var box = new Rect2(r.Position.X + 5, r.Position.Y + 6, r.Size.X - 10, r.Size.Y - 13);
        foreach (var p in new[] { new Vector2(box.Position.X + 2f, box.End.Y + 2f), new Vector2(box.End.X - 2f, box.End.Y + 2f) }) Dot(ci, p, 2.4f, new Color("#1a1a1a")); // 바퀴
        Box(ci, box, new Color("#7a2a20"), 3, new Color("#e0623e"));
        for (int k = 1; k < 3; k++) Line(ci, new Vector2(box.Position.X + 2f, box.Position.Y + box.Size.Y * k / 3f), new Vector2(box.End.X - 2f, box.Position.Y + box.Size.Y * k / 3f), new Color("#2a1410"), 1f); // 서랍 셋
        for (int k = 0; k < 3; k++) Line(ci, new Vector2(box.GetCenter().X - 3f, box.Position.Y + box.Size.Y * (k + 0.5f) / 3f), new Vector2(box.GetCenter().X + 3f, box.Position.Y + box.Size.Y * (k + 0.5f) / 3f), Chrome, 1f);
        Line(ci, new Vector2(r.Position.X + 4f, r.Position.Y + 3f), new Vector2(r.End.X - 4f, r.Position.Y + 3f), Chrome, 1.6f); // 밀대
        Line(ci, new Vector2(r.Position.X + 4f, r.Position.Y + 3f), new Vector2(r.Position.X + 5f, r.Position.Y + 7f), Chrome, 1.2f);
        Line(ci, new Vector2(r.End.X - 4f, r.Position.Y + 3f), new Vector2(r.End.X - 5f, r.Position.Y + 7f), Chrome, 1.2f);
    }

    private static void CartLife(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R;
        var box = new Rect2(r.Position.X + 5, r.Position.Y + 6, r.Size.X - 10, r.Size.Y - 13);
        if (SomeoneWorkingIn(x)) // 누가 그 방에서 일하면 가운데 서랍이 빠져 있다
        {
            var d = new Rect2(box.Position.X - 2f, box.Position.Y + box.Size.Y / 3f + 1f, box.Size.X + 4f, box.Size.Y / 3f - 1f);
            Box(ci, d, new Color("#5a1e16"), 1, new Color("#e0623e"));
            for (int k = 0; k < 4; k++) Dot(ci, d.Position + new Vector2(3f + k * (d.Size.X - 6f) / 3f, d.Size.Y * 0.5f), 0.9f, Chrome);
        }
        float sway = Mathf.Sin(x.T * 1.4f) * 0.4f; // 걸레가 흔들린다
        var hang = new Vector2(box.End.X, box.Position.Y + 2f);
        ci.Poly(new[] { hang, hang + new Vector2(2.5f, 0f), hang + Vector2.FromAngle(Mathf.Pi / 2f + sway) * 6f + new Vector2(2.5f, 0f), hang + Vector2.FromAngle(Mathf.Pi / 2f + sway) * 6f }, new Color("#c8c4b0").WithAlpha(0.8f));
    }

    private static void CartFine(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R;
        var box = new Rect2(r.Position.X + 5, r.Position.Y + 6, r.Size.X - 10, r.Size.Y - 13);
        Bolts(ci, box, 1.6f, 0.45f);
        Tag(ci, box.GetCenter() + new Vector2(0f, box.Size.Y * 0.42f), "TOOLS", 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 선반(旋盤) ───────────────

    private static void LatheBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1e2226"), 2, new Color("#a8844f"));
        ci.Box(x.Q(0.02f, 0.72f, 0.98f, 0.96f), new Color("#121416")); // 칩 받이
        Line(ci, x.P(0.05f, 0.42f), x.P(0.95f, 0.42f), new Color("#6a7080"), 1.6f); // 베드 레일
        Line(ci, x.P(0.05f, 0.62f), x.P(0.95f, 0.62f), new Color("#6a7080"), 1.6f);
        Box(ci, x.Q(0.02f, 0.1f, 0.28f, 0.7f), new Color("#3a4a5a"), 2, new Color("#5a6a7a")); // 주축대
        var chuck = x.P(0.34f, 0.4f);
        Dot(ci, chuck, x.Lv * 0.2f, new Color("#5a6270"));
        Ring(ci, chuck, x.Lv * 0.2f, Chrome, 1f, 16);
        Box(ci, x.Q(0.82f, 0.25f, 0.96f, 0.6f), new Color("#3a4a5a"), 2, new Color("#5a6a7a")); // 심압대
        Line(ci, x.P(0.34f, 0.4f), x.P(0.82f, 0.4f), new Color("#9aa6b5"), 2.2f); // 공작물
        ci.Box(x.Q(0.52f, 0.52f, 0.62f, 0.68f), new Color("#4a4a3a")); // 공구대
        Line(ci, x.P(0.57f, 0.52f), x.P(0.57f, 0.44f), new Color("#e0b64a"), 1.2f); // 바이트
    }

    private static void LatheLife(in Fix x)
    {
        var ci = x.Ci;
        var chuck = x.P(0.34f, 0.4f);
        float a = x.Ang(14f);
        for (int k = 0; k < 3; k++) Line(ci, chuck + Vector2.FromAngle(a + k * Mathf.Tau / 3f) * x.Lv * 0.06f, chuck + Vector2.FromAngle(a + k * Mathf.Tau / 3f) * x.Lv * 0.18f, new Color("#2a2e36"), 1.6f); // 척 물림 턱
        if (!x.On) return;
        float s = Mathf.PosMod(x.T * 3f * x.Spin, 1f); // 도는 공작물의 반짝 줄
        Line(ci, x.P(0.4f + s * 0.4f, 0.37f), x.P(0.4f + s * 0.4f, 0.43f), Colors.White.WithAlpha(0.5f), 0.8f);
        if (x.User != null || SomeoneWorkingIn(x)) // 깎는 중: 칩이 튄다
        {
            var tip = x.P(0.57f, 0.44f);
            for (int k = 0; k < (x.Lod == 0 ? 1 : 4); k++)
            {
                float ph = Mathf.PosMod(x.T * 2.5f + k / 4f, 1f);
                Dot(ci, tip + Vector2.FromAngle(Mathf.Pi * 0.3f + Hash(x.Id, k, 91) * 1.2f) * ph * 9f, 0.7f, Ember.WithAlpha(1f - ph));
            }
        }
    }

    private static void LatheFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.02f, 0.1f, 0.28f, 0.7f), 1.6f, 0.45f);
        Knob(ci, x.P(0.15f, 0.85f), 1.4f, 0.3f, new Color("#3a4a5a"));
        for (int k = 0; k < 10; k++) Line(ci, x.P(0.1f + k * 0.08f, 0.62f), x.P(0.1f + k * 0.08f, 0.66f), new Color(1, 1, 1, 0.25f), 0.5f); // 눈금
    }

    // ─────────────── 납땜대 ───────────────

    private static void SolderBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#2a1e22"), 3, new Color("#6a4a52")); // 정전기 매트
        var unit = x.Q(0.05f, 0.08f, 0.38f, 0.42f);
        Box(ci, unit, new Color("#3a3f48"), 2, new Color("#6a7080")); // 본체
        ci.Box(new Rect2(unit.Position + new Vector2(1.5f, 1.5f), new Vector2(unit.Size.X - 3f, unit.Size.Y * 0.4f)), GlassDark); // 온도 표시
        var holder = x.P(0.22f, 0.7f);
        for (int k = 1; k <= 3; k++) Ring(ci, holder, k * 1.4f, Chrome.WithAlpha(0.7f), 0.7f, 12); // 인두 거치 스프링
        Line(ci, holder, holder + new Vector2(8f, -3f), new Color("#2a2a2a"), 1.8f); // 인두
        ci.Box(x.Q(0.08f, 0.86f, 0.36f, 0.97f), new Color("#e0c040")); // 스펀지
        var pcb = x.Q(0.5f, 0.45f, 0.92f, 0.85f);
        Box(ci, pcb, new Color("#1a5a2a"), 1); // 기판
        for (int k = 0; k < 4; k++) Line(ci, pcb.Position + new Vector2(2f, 2f + k * 2.5f), pcb.Position + new Vector2(pcb.Size.X - 2f, 2f + k * 2.5f), new Color("#c8a040").WithAlpha(0.6f), 0.6f); // 배선
        var arm = x.P(0.7f, 0.15f);
        Line(ci, arm, x.P(0.55f, 0.45f), new Color("#6a7080"), 1f); // 돋보기 팔
        Line(ci, arm, x.P(0.9f, 0.45f), new Color("#6a7080"), 1f);
        Ring(ci, arm, 3.6f, Chrome, 1f, 16); // 돋보기
        Dot(ci, arm, 3f, new Color(0.7f, 0.85f, 1f, 0.12f));
    }

    private static void SolderLife(in Fix x)
    {
        var ci = x.Ci;
        var holder = x.P(0.22f, 0.7f);
        var tip = holder + new Vector2(8f, -3f);
        Led(ci, tip, Ember, x.Glow * 0.9f, 1f); // 달아오른 인두 끝
        var unit = x.Q(0.05f, 0.08f, 0.38f, 0.42f);
        if (x.Lit) // 온도 숫자 자리 (깜빡이며 오른다)
            for (int k = 0; k < 3; k++) ci.Box(new Rect2(unit.Position + new Vector2(2.5f + k * 2.5f, 2.2f), new Vector2(1.6f, unit.Size.Y * 0.3f)), Danger.WithAlpha(0.8f * x.Glow));
        if (x.User != null && x.On && x.Lod > 0) // 송진 연기
            for (int k = 0; k < 2; k++)
            {
                float ph = Mathf.PosMod(x.T * 0.6f + k * 0.5f, 1f);
                Dot(ci, x.P(0.7f, 0.6f) + new Vector2(Mathf.Sin(ph * 7f) * 2f, -ph * 12f), 1f + 2.5f * ph, SmokeGrey.WithAlpha(0.3f * (1f - ph)));
            }
    }

    private static void SolderFine(in Fix x)
    {
        var ci = x.Ci;
        var pcb = x.Q(0.5f, 0.45f, 0.92f, 0.85f);
        for (int k = 0; k < 6; k++) Dot(ci, pcb.Position + new Vector2(2f + (k % 3) * pcb.Size.X / 3f, pcb.Size.Y * (k < 3 ? 0.3f : 0.7f)), 0.6f, Chrome); // 납 자리
        Tag(ci, x.P(0.22f, 0.5f), "350°", 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 공구 벽 ───────────────

    private static void ToolWallBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#3a3428"), 2, new Color("#5a4a32"), 2); // 타공판
        for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) Dot(ci, x.P(0.08f + i * 0.17f, 0.08f + j * 0.17f), 0.55f, new Color("#1a1610"));
        var shade = new Color("#1e1a14"); // 그림자 윤곽 (어디에 뭘 거는지)
        // 렌치
        Line(ci, x.P(0.15f, 0.15f), x.P(0.15f, 0.7f), shade, 3f);
        Line(ci, x.P(0.15f, 0.15f), x.P(0.15f, 0.7f), new Color("#9aa6b5"), 1.6f);
        Ring(ci, x.P(0.15f, 0.12f), 2.2f, new Color("#9aa6b5"), 1.2f, 10);
        // 망치
        Line(ci, x.P(0.38f, 0.18f), x.P(0.38f, 0.75f), Wood, 1.8f);
        ci.Box(x.Q(0.3f, 0.12f, 0.46f, 0.22f), new Color("#6a6f79"));
        // 드라이버 둘
        Line(ci, x.P(0.6f, 0.15f), x.P(0.6f, 0.45f), new Color("#c0392b"), 2.4f);
        Line(ci, x.P(0.6f, 0.45f), x.P(0.6f, 0.68f), new Color("#c8ced8"), 0.9f);
        Line(ci, x.P(0.72f, 0.15f), x.P(0.72f, 0.45f), new Color("#e0b64a"), 2.4f);
        Line(ci, x.P(0.72f, 0.45f), x.P(0.72f, 0.68f), new Color("#c8ced8"), 0.9f);
        // 펜치
        Line(ci, x.P(0.84f, 0.15f), x.P(0.9f, 0.6f), new Color("#3a6aa0"), 1.4f);
        Line(ci, x.P(0.92f, 0.15f), x.P(0.86f, 0.6f), new Color("#3a6aa0"), 1.4f);
        // 톱 (아래 줄)
        var saw = x.Q(0.12f, 0.82f, 0.88f, 0.92f);
        ci.Box(saw, new Color("#8a929e"));
        for (int k = 0; k < 10; k++) ci.Poly(new[] { new Vector2(saw.Position.X + k * saw.Size.X / 10f, saw.End.Y), new Vector2(saw.Position.X + (k + 0.5f) * saw.Size.X / 10f, saw.End.Y + 1.5f), new Vector2(saw.Position.X + (k + 1f) * saw.Size.X / 10f, saw.End.Y) }, new Color("#8a929e"));
    }

    private static void ToolWallLife(in Fix x)
    {
        var ci = x.Ci;
        if (SomeoneWorkingIn(x)) // 누가 일하러 가져갔다: 드라이버 하나가 빠진 그림자만
        {
            int k = (int)(Hash(x.Id, (int)(x.T * 0.02f), 92) * 2f);
            float u = k == 0 ? 0.6f : 0.72f;
            Line(ci, x.P(u, 0.13f), x.P(u, 0.7f), new Color("#1e1a14"), 3.2f);
            Line(ci, x.P(u, 0.13f), x.P(u, 0.7f), new Color("#2a2418"), 2f);
        }
        if (x.Lod == 0) return;
        float ph = Mathf.PosMod(x.T * 0.15f, 1f); // 크롬 공구 위로 지나가는 빛
        if (ph < 0.3f) Dot(ci, x.P(0.1f + ph * 2.6f, 0.5f - ph), 1.2f, Colors.White.WithAlpha(0.5f * (1f - ph / 0.3f)));
    }

    private static void ToolWallFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.5f);
        Tag(ci, x.P(0.5f, 0.97f) - x.V * 1f, "제자리", 4, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── 교정 장비 ───────────────

    private static void CalibBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#161a20"), 3, new Color("#4f7fa8"));
        Line(ci, x.P(0.04f, 0.4f), x.P(0.96f, 0.4f), new Color("#5a6270"), 1.4f); // 광학 레일
        Line(ci, x.P(0.04f, 0.6f), x.P(0.96f, 0.6f), new Color("#5a6270"), 1.4f);
        Box(ci, x.Q(0.03f, 0.32f, 0.2f, 0.68f), new Color("#2a2e36"), 1.5f, new Color("#6a7080")); // 레이저 발사기
        var tgt = x.P(0.88f, 0.5f);
        float tr = Mathf.Min(x.Lv * 0.32f, 6f);
        Dot(ci, tgt, tr, Cream);
        Ring(ci, tgt, tr * 0.66f, new Color("#c0392b"), 0.8f, 14);
        Ring(ci, tgt, tr * 0.33f, new Color("#c0392b"), 0.8f, 10);
        Line(ci, tgt - new Vector2(tr, 0f), tgt + new Vector2(tr, 0f), new Color("#2a2a2a"), 0.5f);
        Line(ci, tgt - new Vector2(0f, tr), tgt + new Vector2(0f, tr), new Color("#2a2a2a"), 0.5f);
        for (int k = 0; k < 3; k++) Can(ci, x.P(0.35f + k * 0.12f, 0.85f), 1.8f + k * 0.4f, new Color("#a8844f"), new Color("#d8b47a")); // 기준 추
        Knob(ci, x.P(0.12f, 0.15f), 2f, 0.4f, new Color("#3a4454")); // 마이크로미터
    }

    private static void CalibLife(in Fix x)
    {
        var ci = x.Ci;
        float cal = x.M?.SensorCal ?? 1f; // 틀어질수록 점이 과녁 가운데서 벗어나 떤다
        var tgt = x.P(0.88f, 0.5f);
        float drift = (1f - cal) * 6f;
        var hit = tgt + new Vector2(Mathf.Sin(x.T * 1.3f) * drift, Mathf.Cos(x.T * 1.7f) * drift);
        if (x.Lit)
        {
            Line(ci, x.P(0.2f, 0.5f), hit, Laser.WithAlpha(0.6f * x.Glow), 0.8f);
            Dot(ci, hit, 1.3f, Laser.WithAlpha(x.Glow));
            Dot(ci, hit, 3f, Laser.WithAlpha(0.15f * x.Glow));
        }
        Led(ci, x.P(0.12f, 0.85f), cal > 0.8f ? Good : cal > 0.5f ? Amber : Danger, Mathf.Max(0.2f, x.Glow), 1f);
    }

    private static void CalibFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 12; k++) Line(ci, x.P(0.22f + k * 0.05f, 0.6f), x.P(0.22f + k * 0.05f, k % 5 == 0 ? 0.68f : 0.64f), new Color(1, 1, 1, 0.3f), 0.5f); // 눈금자
        Tag(ci, x.P(0.12f, 0.5f), "λ", 5, Laser.WithAlpha(0.6f));
    }

    // ─────────────── 채집 장치 ───────────────

    private static Rect2 Bin(in Fix x, int k) => x.Q(0.04f + k * 0.19f, 0.25f, 0.21f + k * 0.19f, 0.92f);

    private static void CollectorBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.B;
        Box(ci, r, new Color("#1c2027"), 4, new Color("#4a5260"), 2);
        for (int k = 0; k < 5; k++)
        {
            var bin = Bin(x, k);
            Box(ci, bin, new Color("#12151a"), 2, new Color("#2a3038"));
            ci.Box(new Rect2(bin.Position.X, bin.Position.Y, bin.Size.X, 1.5f), Palette.Item(ItemKinds.RawKinds[k]).WithAlpha(0.5f)); // 칸마다 원료 색 띠
        }
        ci.Box(new Rect2(r.GetCenter().X - 6, x.R.Position.Y - 6, 12, 8), new Color("#3a414d")); // 선체 관통부
        Dot(ci, new Vector2(r.GetCenter().X, r.Position.Y + 3f), 3f, new Color("#4a5260")); // 팔 뿌리 베어링
        Ring(ci, new Vector2(r.GetCenter().X, r.Position.Y + 3f), 3f, Chrome, 0.8f, 12);
    }

    private static void CollectorLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var f = x.F;
        bool working = x.Eff > 0f && !x.Dead;
        var kinds = ItemKinds.RawKinds;
        for (int k = 0; k < kinds.Length; k++) // 칸마다 채워진 만큼
        {
            var bin = Bin(x, k);
            int cap = kinds[k] == ItemKind.Rare ? 5 : CollectionSystem.BinSize;
            float fill = Mathf.Clamp((f.Storage?.Count(kinds[k]) ?? 0) / (float)cap, 0f, 1f);
            float h = (bin.Size.Y - 2f) * fill;
            ci.Box(new Rect2(bin.Position.X + 1, bin.End.Y - 1 - h, bin.Size.X - 2, h), Palette.Item(kinds[k]).WithAlpha(0.75f));
        }
        // 선체 밖으로 뻗은 채집 팔
        var root = new Vector2(x.B.GetCenter().X, f.MinY * ShipView.T - ShipView.T * 0.1f);
        float sweep = working ? Mathf.Sin(x.T * 0.6f) * 0.7f : 0.2f;
        var elbow = root + new Vector2(0, -ShipView.T * 1.2f) + Vector2.FromAngle(-Mathf.Pi / 2 + sweep) * 4f;
        var hand = elbow + Vector2.FromAngle(-Mathf.Pi / 2 + sweep * 1.6f) * ShipView.T * 1.1f;
        var armCol = working ? new Color("#8a93a3") : new Color("#4a505b");
        ci.DrawLine(root, elbow, new Color(0, 0, 0, 0.5f), 8f, true);
        ci.DrawLine(root, elbow, armCol, 5f, true);
        ci.DrawLine(elbow, hand, new Color(0, 0, 0, 0.5f), 6f, true);
        ci.DrawLine(elbow, hand, armCol, 4f, true);
        Dot(ci, elbow, 3.5f, armCol.Darkened(0.2f));
        var dir = (hand - elbow).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        ci.Poly(new[] { hand + side * 7f + dir * 4f, hand - side * 7f + dir * 4f, hand - side * 4f - dir * 3f, hand + side * 4f - dir * 3f }, working ? new Color("#6b7385") : new Color("#3a3f48"));
        if (!working) return;
        Dot(ci, hand, 12f, new Color("#8ee6ff").WithAlpha(0.06f + 0.04f * Mathf.Sin(x.T * 2f)));
        int motes = x.Lod == 0 ? 2 : 3 + (int)(x.W.Space.Density * 4f);
        for (int k = 0; k < motes; k++) // 빨려 드는 먼지 · 얼음 알갱이
        {
            float phase = Mathf.PosMod(x.T * 0.35f + k / (float)motes, 1f);
            float ang = Hash(f.Id, k, 3) * Mathf.Tau;
            var from = hand + Vector2.FromAngle(ang) * ShipView.T * (1.4f + Hash(k, f.Id, 4));
            var col = Palette.Item(SpaceEnvironment.Composition[k % 4].kind);
            Dot(ci, from.Lerp(hand, phase), 1.4f, col.WithAlpha(0.8f * Mathf.Sin(phase * Mathf.Pi)));
        }
    }

    private static void CollectorFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.6f);
        string[] names = { "금", "규", "탄", "얼", "희" };
        for (int k = 0; k < 5; k++) Tag(ci, Bin(x, k).GetCenter() - new Vector2(0f, Bin(x, k).Size.Y * 0.32f), names[k], 4, new Color(1, 1, 1, 0.45f));
    }

    // ─────────────── 정제기 ───────────────

    private static void RefineryBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-4f);
        Box(ci, r, new Color("#211b18"), 8, new Color("#5a4636"), 2);
        var c = r.GetCenter() + new Vector2(0, -4);
        float cr = Mathf.Min(r.Size.X, r.Size.Y) * 0.3f;
        for (int k = 0; k < 4; k++) ci.Arc(c, cr + 2f + k * 1.8f, -2.6f, -0.5f, 10, Copper.WithAlpha(0.8f), 1.2f, true); // 유도 코일
        Dot(ci, c, cr, new Color("#15110f")); // 도가니
        Ring(ci, c, cr, new Color("#5a4636"), 2f, 32);
        Box(ci, new Rect2(r.Position.X + 8, r.End.Y - 12, r.Size.X - 16, 6), new Color("#2a2420"), 2); // 배출 트레이
        for (int k = 0; k < 4; k++) ci.Box(new Rect2(r.Position.X + 10 + k * (r.Size.X - 20) / 4f, r.End.Y - 11, (r.Size.X - 20) / 4f - 2, 4), new Color("#1a1612")); // 주형 칸
        Box(ci, new Rect2(r.End.X - 12f, r.Position.Y + 4f, 8f, 10f), new Color("#1a1a1e"), 1.5f, new Color("#4a4a52")); // 조작판
    }

    private static void RefineryLife(in Fix x)
    {
        var ci = x.Ci;
        var m = x.M;
        var r = x.R.Grow(-4f);
        var c = r.GetCenter() + new Vector2(0, -4);
        float cr = Mathf.Min(r.Size.X, r.Size.Y) * 0.3f;
        bool hot = m != null && m.Active && m.Efficiency > 0f && !x.Dead;
        float pulse = 0.6f + 0.4f * Mathf.Sin(x.T * 3f);
        Dot(ci, c, cr * 0.7f, (hot ? Ember : new Color("#4a2a1a")).WithAlpha(hot ? 0.35f * pulse + 0.2f : 0.5f));
        Dot(ci, c, cr * 0.4f, (hot ? new Color("#ffd08a") : new Color("#3a2418")).WithAlpha(hot ? pulse : 0.8f));
        if (hot)
        {
            for (int k = 0; k < 4; k++) ci.Arc(c, cr + 2f + k * 1.8f, -2.6f, -0.5f, 10, Ember.WithAlpha(0.3f * pulse), 1.2f, true); // 코일이 달아오른다
            float cyc = Mathf.PosMod(x.T * 0.2f, 1f); // 가끔 쇳물을 붓는다
            if (cyc < 0.25f)
            {
                int slot = (int)(x.T * 0.2f) % 4;
                var mold = new Vector2(r.Position.X + 10 + (slot + 0.5f) * (r.Size.X - 20) / 4f, r.End.Y - 9f);
                Line(ci, c + new Vector2(0f, cr * 0.5f), mold, Molten.WithAlpha(0.85f), 1.6f);
                Dot(ci, mold, 2f, Molten);
            }
            if (x.Lod > 0)
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.PosMod(x.T * 0.8f + k / 3f, 1f);
                    Dot(ci, c + new Vector2((k - 1) * 6f, -ph * 18f), 2f + 2f * ph, new Color(0.6f, 0.55f, 0.5f, 0.35f * (1f - ph)));
                }
        }
        Led(ci, new Vector2(r.End.X - 8f, r.Position.Y + 7f), hot ? Ember : Good, Mathf.Max(0.2f, x.Glow), 1f);
    }

    private static void RefineryFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.R.Grow(-4f), 3f, 0.7f);
        Tag(ci, x.R.GetCenter() + new Vector2(0f, x.R.Size.Y * 0.1f), "1600°", 5, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── 드론 거치대 ───────────────

    private static void DroneDockBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#161c26"), 5, new Color("#3a4a5e"), 1);
        ci.Box(x.Q(0.0f, 0.02f, 1f, 0.12f), new Color("#6fd3b0").WithAlpha(0.3f)); // 충전 레일
        for (int k = 0; k < 3; k++)
        {
            var c = x.P((k + 0.5f) / 3f, 0.55f);
            float rr = Mathf.Min(x.Lu / 3f, x.Lv) * 0.32f;
            Ring(ci, c, rr, new Color("#2c3a4a"), 1.5f, 20); // 받침
            Line(ci, c + new Vector2(-rr * 0.4f, -rr * 0.45f), c + new Vector2(-rr * 0.4f, rr * 0.45f), new Color("#2c3a4a"), 1f); // H 착륙 표시
            Line(ci, c + new Vector2(rr * 0.4f, -rr * 0.45f), c + new Vector2(rr * 0.4f, rr * 0.45f), new Color("#2c3a4a"), 1f);
            Line(ci, c + new Vector2(-rr * 0.4f, 0f), c + new Vector2(rr * 0.4f, 0f), new Color("#2c3a4a"), 1f);
        }
    }

    private static void DroneDockLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.Lit) ci.Box(x.Q(0.0f, 0.04f, 1f, 0.1f), new Color("#6fd3b0").WithAlpha((0.25f + 0.25f * Pulse(x.T, 2f)) * x.Glow));
        if (!x.Lit || x.Lod == 0) return;
        for (int k = 0; k < 3; k++) // 받침마다 도는 유도등
        {
            var c = x.P((k + 0.5f) / 3f, 0.55f);
            float rr = Mathf.Min(x.Lu / 3f, x.Lv) * 0.32f;
            float a = x.Ang(2f) + k * 1.3f;
            Dot(ci, c + Vector2.FromAngle(a) * rr, 1f, Cyan.WithAlpha(0.8f * x.Glow));
        }
    }

    private static void DroneDockFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2f, 0.55f);
        for (int k = 0; k < 3; k++) Tag(ci, x.P((k + 0.5f) / 3f, 0.92f), (k + 1).ToString(), 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 로봇 충전대 ───────────────

    private static Vector2 DockToWall(Furniture f)
    {
        if (f.UseSpots.Count == 0) return Vector2.Zero;
        float sx = 0f, sy = 0f;
        foreach (var c in f.UseSpots) { sx += c.X + 0.5f - f.Center.X; sy += c.Y + 0.5f - f.Center.Y; }
        return Mathf.Abs(sx) >= Mathf.Abs(sy) ? new Vector2(-Mathf.Sign(sx), 0) : new Vector2(0, -Mathf.Sign(sy));
    }

    private static void RobotDockBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-2.5f);
        var toWall = DockToWall(x.F);
        Box(ci, r, new Color("#141922"), 6, new Color("#3b4658"), 1);
        var pad = r.Grow(-4f);
        Box(ci, pad, new Color("#1c232f"), 4, new Color("#2a3444")); // 미끄럼 방지 패드
        for (int k = 1; k < 4; k++)
        {
            float px = pad.Position.X + pad.Size.X * k / 4f;
            Line(ci, new Vector2(px, pad.Position.Y + 2), new Vector2(px, pad.End.Y - 2), new Color("#243040"), 1f);
        }
        for (float px = r.Position.X + 2; px < r.End.X - 2; px += 5f) // 가장자리 경고 띠
            Line(ci, new Vector2(px, r.End.Y - 2.5f), new Vector2(Mathf.Min(r.End.X - 2, px + 2.5f), r.End.Y - 2.5f), new Color("#c9a13a").WithAlpha(0.55f), 1.5f);
        var cc = r.GetCenter();
        var wallSide = cc + toWall * (r.Size.X * 0.36f);
        var across = new Vector2(-toWall.Y, toWall.X);
        if (toWall == Vector2.Zero) { wallSide = new Vector2(cc.X, r.Position.Y + 5); across = Vector2.Right; }
        for (int s = -1; s <= 1; s += 2) // 단자 둘
        {
            var p = wallSide + across * (s * 5f);
            ci.Box(new Rect2(p - new Vector2(2f, 2f), new Vector2(4f, 4f)), new Color("#b9c4d2"));
            ci.Box(new Rect2(p - new Vector2(1f, 1f), new Vector2(2f, 2f)), new Color("#5d6878"));
        }
        if (toWall != Vector2.Zero) ci.DrawLine(wallSide, cc + toWall * (ShipView.T * 0.62f), new Color("#2b3140"), 4f); // 전선관
        ci.Box(new Rect2(r.Position.X + 4, r.Position.Y + 3, 9, 3), new Color("#6fd3b0").WithAlpha(0.3f)); // 명판 (상태 띠 자리 — ShipViewRobots 가 칠한다)
    }

    private static void RobotDockLife(in Fix x)
    {
        // 전기가 들어오면 패드 위로 안내 빛이 훑는다 (로봇 · 충전 불꽃 · 상태 띠는 ShipViewRobots)
        var ci = x.Ci;
        if (!x.Lit || x.Lod == 0) return;
        var pad = x.R.Grow(-6.5f);
        float ph = Mathf.PosMod(x.T * 0.5f, 1f);
        float px = pad.Position.X + pad.Size.X * ph;
        Line(ci, new Vector2(px, pad.Position.Y + 2f), new Vector2(px, pad.End.Y - 2f), new Color("#6fd3b0").WithAlpha(0.25f * x.Glow * Mathf.Sin(ph * Mathf.Pi)), 1.5f);
    }

    private static void RobotDockFine(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-2.5f);
        Bolts(ci, r, 2f, 0.5f);
        Tag(ci, r.GetCenter() + new Vector2(0f, 5f), "BOT", 4, new Color(1, 1, 1, 0.3f));
    }
}
