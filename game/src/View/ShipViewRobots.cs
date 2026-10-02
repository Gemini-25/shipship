using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v10.10 선내 로봇·충전대·비상 물자함 그림.
/// 로봇은 위에서 내려다본 모양으로, 가는 방향으로 돌아간다 — 운반(바퀴 넷·짐칸), 정비(관절 팔·집게), 재배(물통·분무기), 방재(거품 통·경광등).
/// 충전 중이면 충전대 단자에 푸른 불꽃, 고장이면 붉게 깜빡이며 연기, 방전이면 회색으로 멈춰 서고, 끌려가면 사람과 끈으로 이어진다.
/// </summary>
public partial class ShipView
{
    public static Color RobotColor(RobotKind k) => k switch
    {
        RobotKind.Hauler => new Color("#e0a53a"),
        RobotKind.Maintainer => new Color("#58b6e8"),
        RobotKind.Gardener => new Color("#6fcf7c"),
        RobotKind.Safety => new Color("#e8584a"),
        _ => RobotColorV15(k),
    };

    // v15.7 새 로봇·드론: 원형의 색에서 조금씩 비튼 색 (원형 몸에 특기 표식을 단다)
    private static Color RobotColorV15(RobotKind k) => k switch
    {
        RobotKind.Courier => new Color("#f2c14e"),
        RobotKind.Tanker => new Color("#4fa3c7"),
        RobotKind.Stocker => new Color("#b07a3a"),
        RobotKind.Lineman => new Color("#f0e26a"),
        RobotKind.Assistant => new Color("#9fc7f0"),
        RobotKind.Overhauler => new Color("#3d7fd1"),
        RobotKind.Harvester => new Color("#b5d65a"),
        RobotKind.Tender => new Color("#3fae8a"),
        RobotKind.Sentry => new Color("#e88a4a"),
        RobotKind.Firefighter => new Color("#c8302a"),
        RobotKind.Utility => new Color("#a8a29a"),
        _ => new Color("#cccccc"),
    };

    public static Color DroneColorV15(DroneKind k) => k switch
    {
        DroneKind.Scout => new Color("#b8f3ff"),
        DroneKind.Surveyor => new Color("#4fb8d8"),
        DroneKind.Welder => new Color("#ffb347"),
        DroneKind.Radiator => new Color("#ff7f9e"),
        DroneKind.Tug => new Color("#d9733f"),
        DroneKind.Rigger => new Color("#5fbf6a"),
        _ => new Color("#9fe38a"),
    };

    /// <summary>v15.7 특기 표식: 등판에 원형과 다른 작은 무늬 (짐칸·통 뒤쪽).</summary>
    private static void PaintRobotBadge(CanvasItem ci, Robot r, Color col, bool working, float t)
    {
        var ink = new Color("#11151b");
        var hi = col.Lightened(0.35f);
        switch (r.Kind)
        {
            case RobotKind.Courier: // 배식판 둘
                ci.DrawRect(new Rect2(-5.5f, -3.5f, 4f, 3f), hi); ci.DrawRect(new Rect2(-5.5f, 0.5f, 4f, 3f), hi); break;
            case RobotKind.Tanker: // 물통 (출렁인다)
                ci.DrawCircle(new Vector2(-3.5f, 0f), 3.2f, new Color("#2c6e8f"), true, -1f, true);
                ci.DrawCircle(new Vector2(-3.5f, 0f), 1.6f + 0.4f * Mathf.Sin(t * 3f), hi.WithAlpha(0.8f), true, -1f, true); break;
            case RobotKind.Stocker: // 겹친 상자
                ci.DrawRect(new Rect2(-6f, -3f, 3.5f, 3.5f), hi); ci.DrawRect(new Rect2(-4f, -0.5f, 3.5f, 3.5f), hi.Darkened(0.25f)); break;
            case RobotKind.Lineman: // 번개
                ci.DrawPolyline(new[] { new Vector2(-2f, -4f), new Vector2(-4.5f, 0f), new Vector2(-2.5f, 0f), new Vector2(-5f, 4f) }, ink, 1.4f, true); break;
            case RobotKind.Assistant: // 손 둘
                ci.DrawCircle(new Vector2(-4f, -2.5f), 1.4f, hi, true, -1f, true); ci.DrawCircle(new Vector2(-4f, 2.5f), 1.4f, hi, true, -1f, true); break;
            case RobotKind.Overhauler: // 엇갈린 렌치 둘 (일하면 깜빡인다)
            {
                var wr = working ? hi.Lerp(Colors.White, 0.5f + 0.5f * Mathf.Sin(t * 8f)) : hi;
                ci.DrawLine(new Vector2(-6f, -3f), new Vector2(-1f, 3f), wr, 1.4f, true); ci.DrawLine(new Vector2(-6f, 3f), new Vector2(-1f, -3f), wr, 1.4f, true); break;
            }
            case RobotKind.Harvester: // 바구니
                ci.DrawArc(new Vector2(-4f, 0f), 3f, 0f, Mathf.Pi, 8, hi, 1.4f, true); ci.DrawLine(new Vector2(-7f, 0f), new Vector2(-1f, 0f), hi, 1.4f, true); break;
            case RobotKind.Tender: // 잎
                ci.DrawColoredPolygon(new[] { new Vector2(-6f, 0f), new Vector2(-3.5f, -3f), new Vector2(-1f, 0f), new Vector2(-3.5f, 3f) }, hi); break;
            case RobotKind.Sentry: // 도는 눈
                ci.DrawCircle(new Vector2(-3.5f, 0f), 2.6f, ink, true, -1f, true);
                ci.DrawCircle(new Vector2(-3.5f, 0f) + new Vector2(Mathf.Cos(t * 2f), Mathf.Sin(t * 2f)) * 1.1f, 1.1f, new Color("#ffd27a"), true, -1f, true); break;
            case RobotKind.Firefighter: // 큰 거품 통 둘
                ci.DrawCircle(new Vector2(-4f, -2.5f), 2f, new Color("#f4f1ec"), true, -1f, true); ci.DrawCircle(new Vector2(-4f, 2.5f), 2f, new Color("#f4f1ec"), true, -1f, true); break;
            case RobotKind.Utility: // 네 칸
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(-6f + (i % 2) * 3f, -3f + (i / 2) * 3f, 2.4f, 2.4f), hi.Darkened(0.12f * i)); break;
        }
    }

    // ── 충전대: 벽에 붙은 충전 패드 · 두 개의 단자 · 상태 띠 · 벽 속으로 들어가는 전선관 ──
    private static void PaintRobotDockBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-2.5f);
        // 벽 쪽 (충전 케이블이 들어가는 쪽) = 로봇이 드나드는 앞 칸의 반대쪽
        Vector2 toWall = Vector2.Zero;
        if (f.UseSpots.Count > 0)
        {
            var avg = f.UseSpots.Aggregate(System.Numerics.Vector2.Zero, (s, c) => s + (c.Center - f.Center)) / f.UseSpots.Count;
            toWall = Mathf.Abs(avg.X) >= Mathf.Abs(avg.Y) ? new Vector2(-Mathf.Sign(avg.X), 0) : new Vector2(0, -Mathf.Sign(avg.Y));
        }
        Gfx.RoundRect(ci, r, new Color("#141922"), 6, new Color("#3b4658"), 1);
        // 바닥 패드 (미끄럼 방지 격자)
        var pad = r.Grow(-4f);
        Gfx.RoundRect(ci, pad, new Color("#1c232f"), 4, new Color("#2a3444"));
        for (int k = 1; k < 4; k++)
        {
            float x = pad.Position.X + pad.Size.X * k / 4f;
            ci.DrawLine(new Vector2(x, pad.Position.Y + 2), new Vector2(x, pad.End.Y - 2), new Color("#243040"), 1f);
        }
        // 가장자리 경고 띠 (노랑·검정 빗금)
        for (float x = r.Position.X + 2; x < r.End.X - 2; x += 5f)
            ci.DrawLine(new Vector2(x, r.End.Y - 2.5f), new Vector2(Mathf.Min(r.End.X - 2, x + 2.5f), r.End.Y - 2.5f), new Color("#c9a13a").WithAlpha(0.55f), 1.5f);
        // 단자 둘 (벽 쪽)
        var cc = r.GetCenter();
        var wallSide = cc + toWall * (r.Size.X * 0.36f);
        var across = new Vector2(-toWall.Y, toWall.X);
        if (toWall == Vector2.Zero) { wallSide = new Vector2(cc.X, r.Position.Y + 5); across = Vector2.Right; }
        for (int s = -1; s <= 1; s += 2)
        {
            var p = wallSide + across * (s * 5f);
            ci.DrawRect(new Rect2(p - new Vector2(2f, 2f), new Vector2(4f, 4f)), new Color("#b9c4d2"));
            ci.DrawRect(new Rect2(p - new Vector2(1f, 1f), new Vector2(2f, 2f)), new Color("#5d6878"));
        }
        // 전선관: 단자에서 벽 속으로
        if (toWall != Vector2.Zero)
            ci.DrawLine(wallSide, cc + toWall * (T * 0.62f), new Color("#2b3140"), 4f);
        // 명판
        ci.DrawRect(new Rect2(r.Position.X + 4, r.Position.Y + 3, 9, 3), new Color("#6fd3b0").WithAlpha(0.3f));
    }

    // ── 비상 물자함: 주황 캐비닛 · 흰 십자 · 유리창 너머로 실링폼(노랑)·구급 키트(흰)·소화기(빨강) ──
    private static void PaintSupplyCacheBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-4f);
        Gfx.RoundRect(ci, r, new Color("#6a2c16"), 4, new Color("#e0763a"), 2);
        var win = new Rect2(r.Position.X + 3, r.Position.Y + 3, r.Size.X - 6, r.Size.Y * 0.55f);
        ci.DrawRect(win, new Color("#1a1c22"));
        ci.DrawLine(win.Position + new Vector2(1, 1), new Vector2(win.End.X - 1, win.Position.Y + 1), new Color(1, 1, 1, 0.18f), 1f);
        // 손잡이 · 봉인 · 십자
        var cross = new Vector2(r.GetCenter().X, r.End.Y - 5.5f);
        ci.DrawRect(new Rect2(cross.X - 3.5f, cross.Y - 1f, 7f, 2f), new Color("#f2efe8"));
        ci.DrawRect(new Rect2(cross.X - 1f, cross.Y - 3.5f, 2f, 7f), new Color("#f2efe8"));
        ci.DrawRect(new Rect2(r.End.X - 4, r.GetCenter().Y - 3, 2, 6), new Color("#c9cfd8"));
    }

    /// <summary>비상 물자함 안의 물건 (동적: 채워지고 꺼내진다).</summary>
    private static void PaintSupplyCacheLife(CanvasItem ci, Furniture f)
    {
        if (f.Storage is not Inventory inv) return;
        var r = FurnitureRect(f).Grow(-4f);
        var win = new Rect2(r.Position.X + 4, r.Position.Y + 4, r.Size.X - 8, r.Size.Y * 0.55f - 2f);
        float x = win.Position.X + 1f;
        void Items(ItemKind k, Color col, float w)
        {
            int n = Mathf.Min(inv.Count(k), 3);
            for (int i = 0; i < n && x + w <= win.End.X; i++, x += w + 1f)
                ci.DrawRect(new Rect2(x, win.End.Y - win.Size.Y * 0.8f, w, win.Size.Y * 0.8f), col);
        }
        Items(ItemKind.Sealant, new Color("#e9c948"), 2.5f);
        Items(ItemKind.MedKit, new Color("#f2f2f2"), 3f);
        Items(ItemKind.Extinguisher, new Color("#d33b2c"), 2.5f);
    }

    // ═══════════════════════════════ 로봇 ═══════════════════════════════

    private Vector2 RobotPx(Robot r)
    {
        var a = r.PreviousPosition;
        var b = r.Position;
        if (System.Numerics.Vector2.DistanceSquared(a, b) > 4f) return ToPx(b);
        return ToPx(System.Numerics.Vector2.Lerp(a, b, _main.Alpha));
    }

    private void PaintRobots(CanvasItem ci)
    {
        var rs = _world.Robots;
        // 충전대 위의 충전 불꽃과 상태 띠 (로봇이 있든 없든)
        foreach (var dock in _world.Ship.FurnitureOf(FurnitureType.RobotDock))
        {
            var r = FurnitureRect(dock).Grow(-2.5f);
            var here = rs.Robots.FirstOrDefault(x => x.Dock == dock && x.AtDock);
            bool power = RobotSystem.DockWorking(dock);
            bool charging = here != null && power && (here.Battery < 0.995f || (RobotsV15.Fights(here.Kind) && here.Foam < 0.995f));
            Color led = !power ? Palette.Danger : charging ? new Color("#5fd0ff") : here != null ? Palette.Good : Palette.Warning;
            float blink = !power ? 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(_time * 3f)) : charging ? 0.6f + 0.4f * Mathf.Sin(_time * 5f + dock.Id) : 0.85f;
            ci.DrawRect(new Rect2(r.Position.X + 4, r.Position.Y + 3, 9, 3), led.WithAlpha(blink));
            if (charging)
            {
                var c = r.GetCenter();
                for (int k = 0; k < 3; k++)
                {
                    float a = _time * 6f + k * 2.1f + dock.Id;
                    var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (7f + 2f * Mathf.Sin(a * 1.7f));
                    ci.DrawLine(c + (p - c) * 0.45f, p, new Color("#9fe6ff").WithAlpha(0.55f), 1f, true);
                }
            }
        }
        foreach (var r in rs.Robots)
        {
            if (r.State == RobotState.Lost) continue;
            PaintRobot(ci, r);
        }
    }

    private void PaintRobot(CanvasItem ci, Robot r)
    {
        var p = RobotPx(r);
        var col = RobotColor(r.Kind);
        bool dead = r.State == RobotState.Stalled || r.State == RobotState.Towed || r.Disabled;
        bool faulty = r.Fault != null;
        var body = dead ? col.Darkened(0.55f).Lerp(new Color("#555a63"), 0.5f) : col.Darkened(0.25f);
        float angle = Mathf.Atan2(r.Facing.Y, r.Facing.X);
        bool working = r.State == RobotState.Active && r.Path == null && r.Steps != null;
        bool moving = r.State == RobotState.Active && r.Path != null;
        float t = _time + r.Id * 1.37f;

        // 끌려가는 줄 · 거드는 사람과의 연결
        if (r.TowedBy is CrewMember tower) ci.DrawLine(p, CrewPx(tower), TetherColor.WithAlpha(0.75f), 1.5f, true);
        if (r.Helping is CrewMember helped && working)
        {
            var hp = CrewPx(helped);
            float fl = 0.5f + 0.5f * Mathf.Sin(t * 9f);
            ci.DrawLine(p, p.Lerp(hp, 0.75f), new Color("#9fd8ff").WithAlpha(0.25f + 0.25f * fl), 1f, true);
        }
        // 그림자
        ci.DrawCircle(p + new Vector2(2.5f, 3f), 9.5f, new Color(0, 0, 0, 0.3f), true, -1f, true);

        ci.DrawSetTransform(p, angle, Vector2.One);
        switch (RobotsV15.Base(r.Kind)) // v15.7 새 로봇은 원형의 몸에 특기 표식
        {
            case RobotKind.Hauler: PaintHauler(ci, r, body, col, moving, t); break;
            case RobotKind.Maintainer: PaintMaintainer(ci, r, body, col, working, t); break;
            case RobotKind.Gardener: PaintGardener(ci, r, body, col, working, t); break;
            case RobotKind.Safety: PaintSafetyBot(ci, r, body, col, working, t); break;
        }
        if (RobotsV15.Bot(r.Kind) != null) PaintRobotBadge(ci, r, col, working, t);
        PaintRobotTier(ci, r, col, dead, t); // v16.20b 단계 (범퍼 · 방열판 · 다관절 팔)
        // 앞 센서 띠 (가는 쪽)
        var eye = dead ? new Color("#3a3f48") : new Color("#7de8ff").WithAlpha(0.85f);
        ci.DrawLine(new Vector2(8.5f, -4.5f), new Vector2(8.5f, 4.5f), eye, 1.8f, true);
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // 상태 불빛 · 배터리
        bool self = faulty && RobotSystem.CanSelfRepair(r);
        Color led = self ? Palette.Warning : faulty ? Palette.Danger : dead ? Palette.TextMuted : r.Battery < 0.25f ? Palette.Warning : Palette.Good;
        float blink = faulty || dead ? 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(t * 4f)) : 1f;
        ci.DrawCircle(p, 1.9f, led.WithAlpha(blink), true, -1f, true);
        if (!r.AtDock || r.Battery < 0.99f)
        {
            var bar = new Rect2(p.X - 8f, p.Y + 11.5f, 16f, 2.5f);
            ci.DrawRect(bar, new Color(0, 0, 0, 0.7f));
            ci.DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(r.Battery, 0f, 1f), bar.Size.Y)),
                (r.Battery < 0.25f ? Palette.Warning : Palette.Good).WithAlpha(0.9f));
        }
        // 가벼운 고장: 노란 렌치 (스스로 고친다) · 심한 고장: 연기 한 줄기 · 방전: 느낌표
        if (self)
        {
            var wp = p + new Vector2(0, -14f);
            ci.DrawLine(wp + new Vector2(-3f, 3f), wp + new Vector2(2f, -2f), Palette.Warning.WithAlpha(blink), 1.6f, true);
            ci.DrawArc(wp + new Vector2(2.6f, -2.6f), 2.2f, -2.4f, 1.6f, 8, Palette.Warning.WithAlpha(blink), 1.4f, true);
            if (r.AtDock)
                ci.DrawArc(p, 12f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Clamp(r.SelfRepairDone / RobotSystem.SelfRepairHours(r.Fault!.Value), 0f, 1f), 24, Palette.Warning.WithAlpha(0.8f), 1.5f, true);
        }
        else if (faulty)
            for (int k = 0; k < 3; k++)
            {
                float ph = (t * 0.6f + k / 3f) % 1f;
                ci.DrawCircle(p + new Vector2(2f + 3f * Mathf.Sin(ph * 6f + k), -6f - 14f * ph), 2f + 3f * ph, new Color(0.35f, 0.35f, 0.38f, 0.5f * (1f - ph)), true, -1f, true);
            }
        else if (r.State == RobotState.Stalled)
            Gfx.TextCentered(ci, Fonts.Bold, p + new Vector2(0, -14f), "!", 11, Palette.Warning.WithAlpha(blink));
        // 진척 (일하는 중)
        if (working && r.Progress is float pr && pr > 0f && RobotsV15.Base(r.Kind) != RobotKind.Safety)
        {
            ci.DrawArc(p, 12.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Clamp(pr, 0f, 1f), 24, col.WithAlpha(0.8f), 1.6f, true);
        }
    }

    // 운반 로봇: 네모난 몸 · 바퀴 넷 · 짐칸(실은 물건 색) · 도는 황색 경광등
    private static void PaintHauler(CanvasItem ci, Robot r, Color body, Color col, bool moving, float t)
    {
        var wheel = new Color("#1a1d22");
        foreach (var (x, y) in new[] { (-7f, -8.5f), (5f, -8.5f), (-7f, 6.5f), (5f, 6.5f) })
        {
            ci.DrawRect(new Rect2(x, y, 4.5f, 2.2f), wheel);
            if (moving) ci.DrawLine(new Vector2(x + ((t * 20f) % 4.5f), y), new Vector2(x + ((t * 20f) % 4.5f), y + 2.2f), new Color("#3a3f48"), 1f);
        }
        Gfx.RoundRect(ci, new Rect2(-9f, -7f, 18f, 14f), body, 3, col.Lightened(0.15f));
        // 짐칸
        var bed = new Rect2(-7.5f, -5f, 11f, 10f);
        ci.DrawRect(bed, new Color("#101318"));
        ci.DrawRect(bed, col.Darkened(0.5f), false, 1f);
        if (r.Cargo is ItemStack cargo)
        {
            var box = Palette.Item(cargo.Kind);
            int n = Mathf.Clamp(cargo.Count, 1, 4);
            for (int i = 0; i < n; i++)
            {
                float bx = -6.5f + (i % 2) * 5f, by = -4f + (i / 2) * 4.5f;
                ci.DrawRect(new Rect2(bx, by, 4.2f, 3.8f), box);
                ci.DrawRect(new Rect2(bx, by, 4.2f, 1f), box.Lightened(0.3f));
            }
        }
        else if (r.Order?.Kind == WorkKind.CarryWater)
            ci.DrawCircle(new Vector2(-2f, 0f), 3.5f, new Color("#4aa3ff").WithAlpha(0.8f), true, -1f, true);
        // 경광등 (돌 때만)
        float a = t * 7f;
        ci.DrawCircle(new Vector2(5.5f, 0f), 2f, new Color("#ffb347").WithAlpha(moving ? 0.95f : 0.5f), true, -1f, true);
        if (moving) ci.DrawLine(new Vector2(5.5f, 0f), new Vector2(5.5f, 0f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 7f, new Color("#ffb347").WithAlpha(0.35f), 2f, true);
    }

    // 정비 로봇: 둥근 몸 · 궤도 · 두 마디 팔과 집게 (일할 때 움직이고 불꽃이 튄다)
    private static void PaintMaintainer(CanvasItem ci, Robot r, Color body, Color col, bool working, float t)
    {
        var tread = new Color("#1b1f26");
        ci.DrawRect(new Rect2(-7.5f, -9f, 13f, 3f), tread);
        ci.DrawRect(new Rect2(-7.5f, 6f, 13f, 3f), tread);
        for (int k = 0; k < 5; k++)
        {
            float x = -7f + ((k * 3f + (working ? 0f : t * 8f)) % 13f);
            ci.DrawLine(new Vector2(x, -9f), new Vector2(x, -6f), new Color("#2c323c"), 1f);
            ci.DrawLine(new Vector2(x, 6f), new Vector2(x, 9f), new Color("#2c323c"), 1f);
        }
        ci.DrawCircle(Vector2.Zero, 7.5f, body, true, -1f, true);
        ci.DrawArc(Vector2.Zero, 7.5f, 0f, Mathf.Tau, 24, col.Lightened(0.2f), 1.2f, true);
        ci.DrawCircle(new Vector2(-2f, 0f), 3f, body.Darkened(0.3f), true, -1f, true); // 어깨 관절
        // 팔: 어깨 → 팔꿈치 → 집게
        float swing = working ? 0.5f * Mathf.Sin(t * 4f) : 0.25f;
        var shoulder = new Vector2(-2f, 0f);
        var elbow = shoulder + new Vector2(Mathf.Cos(-0.9f + swing), Mathf.Sin(-0.9f + swing)) * 7f;
        var hand = elbow + new Vector2(Mathf.Cos(0.7f - swing * 1.5f), Mathf.Sin(0.7f - swing * 1.5f)) * (working ? 8f : 5.5f);
        ci.DrawLine(shoulder, elbow, new Color("#c8d0da"), 2f, true);
        ci.DrawLine(elbow, hand, new Color("#c8d0da"), 1.6f, true);
        ci.DrawCircle(elbow, 1.4f, new Color("#8a94a2"), true, -1f, true);
        var dir = (hand - elbow).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        ci.DrawLine(hand, hand + dir * 2.5f + side * 1.8f, new Color("#e6ecf2"), 1.2f, true);
        ci.DrawLine(hand, hand + dir * 2.5f - side * 1.8f, new Color("#e6ecf2"), 1.2f, true);
        if (working && Mathf.Sin(t * 17f) > 0.35f)
        {
            ci.DrawCircle(hand + dir * 2.5f, 2.2f, new Color("#fff4c2").WithAlpha(0.85f), true, -1f, true);
            for (int k = 0; k < 3; k++)
            {
                float a = t * 23f + k * 2f;
                ci.DrawLine(hand + dir * 2.5f, hand + dir * 2.5f + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4f, new Color("#ffd27a").WithAlpha(0.8f), 1f, true);
            }
        }
        if (r.Cargo is ItemStack cargo) ci.DrawRect(new Rect2(-6.5f, -2f, 3f, 4f), Palette.Item(cargo.Kind));
    }

    // 재배 로봇: 둥근 초록 몸 · 투명 물통(물 높이) · 분무기 (일할 때 물안개)
    private static void PaintGardener(CanvasItem ci, Robot r, Color body, Color col, bool working, float t)
    {
        foreach (var (x, y) in new[] { (-6f, -8f), (-6f, 6f), (3f, -8f), (3f, 6f) })
            ci.DrawCircle(new Vector2(x + 1.5f, y + 1f), 1.8f, new Color("#1a1d22"), true, -1f, true);
        ci.DrawCircle(Vector2.Zero, 8f, body, true, -1f, true);
        ci.DrawArc(Vector2.Zero, 8f, 0f, Mathf.Tau, 24, col.Lightened(0.25f), 1.2f, true);
        // 물통
        ci.DrawCircle(new Vector2(-2.5f, 0f), 4.2f, new Color("#16304a"), true, -1f, true);
        ci.DrawCircle(new Vector2(-2.5f, 0f), 3.2f, new Color("#4aa3ff").WithAlpha(0.7f), true, -1f, true);
        ci.DrawCircle(new Vector2(-3.5f, -1.2f), 1f, new Color(1, 1, 1, 0.5f), true, -1f, true);
        // 분무기 팔
        ci.DrawLine(new Vector2(2f, 0f), new Vector2(9.5f, 0f), new Color("#b7c4b8"), 1.6f, true);
        ci.DrawCircle(new Vector2(10f, 0f), 1.5f, new Color("#dfe8e0"), true, -1f, true);
        if (working)
            for (int k = 0; k < 5; k++)
            {
                float ph = (t * 1.3f + k / 5f) % 1f;
                var q = new Vector2(10f + 9f * ph, (k - 2) * 1.8f * ph * 2f);
                ci.DrawCircle(q, 0.8f + 1.4f * ph, new Color("#bfe4ff").WithAlpha(0.55f * (1f - ph)), true, -1f, true);
            }
        // 거둔 채소
        if (r.Cargo is ItemStack cargo)
            for (int i = 0; i < Mathf.Min(3, cargo.Count / 4 + 1); i++)
                ci.DrawCircle(new Vector2(3f, -3f + i * 3f), 1.6f, Palette.Item(cargo.Kind), true, -1f, true);
    }

    // 방재 로봇: 붉은 몸 · 거품 통 눈금 · 경광등(돌 때) · 분사구 (뿌릴 때 흰 거품 원뿔)
    private static void PaintSafetyBot(CanvasItem ci, Robot r, Color body, Color col, bool working, float t)
    {
        var wheel = new Color("#1a1d22");
        ci.DrawRect(new Rect2(-8f, -9f, 12f, 2.5f), wheel);
        ci.DrawRect(new Rect2(-8f, 6.5f, 12f, 2.5f), wheel);
        Gfx.RoundRect(ci, new Rect2(-9f, -7f, 17f, 14f), body, 6, col.Lightened(0.2f));
        // 거품 통 (눈금)
        var tank = new Rect2(-7.5f, -4.5f, 7f, 9f);
        Gfx.RoundRect(ci, tank, new Color("#2a1614"), 3, new Color("#f0e6e0").WithAlpha(0.5f));
        float fill = Mathf.Clamp(r.Foam, 0f, 1f);
        ci.DrawRect(new Rect2(tank.Position.X + 1f, tank.End.Y - 1f - (tank.Size.Y - 2f) * fill, tank.Size.X - 2f, (tank.Size.Y - 2f) * fill), new Color("#f4f1ec").WithAlpha(0.85f));
        // 분사구
        ci.DrawLine(new Vector2(3f, 0f), new Vector2(9f, 0f), new Color("#d8d2cc"), 2.2f, true);
        bool spraying = working && r.Order == null && r.Doing.Contains("불");
        if (spraying)
        {
            for (int k = 0; k < 9; k++)
            {
                float ph = (t * 2.1f + k / 9f) % 1f;
                float spread = (k % 3 - 1) * 0.35f + 0.1f * Mathf.Sin(t * 5f + k);
                var q = new Vector2(9f + 22f * ph, 22f * ph * spread);
                ci.DrawCircle(q, 1.2f + 2.5f * ph, new Color(0.96f, 0.97f, 1f, 0.7f * (1f - ph)), true, -1f, true);
            }
        }
        // 경광등
        bool alert = r.State == RobotState.Active && (spraying || r.Doing.Contains("불"));
        float a = t * (alert ? 10f : 2f);
        ci.DrawCircle(new Vector2(3f, -3.5f), 1.8f, new Color("#ff6b5a").WithAlpha(alert ? 0.95f : 0.45f), true, -1f, true);
        if (alert) ci.DrawLine(new Vector2(3f, -3.5f), new Vector2(3f, -3.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9f, new Color("#ff6b5a").WithAlpha(0.35f), 2.5f, true);
        // 순찰 중이면 앞을 훑는 희미한 부채꼴 (열화상·진동 센서)
        if (r.State == RobotState.Active && r.Doing.StartsWith("순찰"))
        {
            float sweep = 0.5f * Mathf.Sin(t * 1.8f);
            var fan = new[] { new Vector2(8f, 0f), new Vector2(26f, -9f + 8f * sweep), new Vector2(26f, 9f + 8f * sweep) };
            ci.DrawColoredPolygon(fan, new Color("#ffd0c8").WithAlpha(0.07f));
        }
    }
}
