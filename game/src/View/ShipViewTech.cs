using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v10.8 기술 단계와 방 모듈 그림.
/// - 단계 II~IV 설비: 오른쪽 위 로마 숫자 딱지와 테두리 (II 청동 · III 은 · IV 보라). Mk.3 딱지(왼쪽 위)와 겹치지 않는다.
/// - 핵융합로(원자로 III·IV): 황색 노심 대신 푸른 플라스마와 도는 토카막 고리.
/// - 방 모듈: LED 생장판(분홍빛), 열교환기(흐르는 냉각수), 축전기(충전 막대), CO₂ 세정기(도는 팬), 정밀 가공기(움직이는 레이저 머리).
/// </summary>
public partial class ShipView
{
    private static readonly Color Plasma = new("#7fd4ff");
    private static readonly Color PlasmaHot = new("#c9a2ff");

    /// <summary>정적 층: 모듈 몸체.</summary>
    private static bool PaintModuleBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f);
        var c = r.GetCenter();
        if (ModulesV15.Of(f.Type) is ModulesV15.Row mv) // v15 새 모듈 34: 역할마다 색 · 이름 첫 글자
        {
            var (bodyC, edgeC) = mv.Role switch
            {
                ModuleRole.Omen => (new Color("#141a22"), new Color("#4f7fa8")),
                ModuleRole.Speed => (new Color("#1d1912"), new Color("#a8844f")),
                ModuleRole.Clean or ModuleRole.SuitDry or ModuleRole.Laundry or ModuleRole.Dry => (new Color("#121d1b"), new Color("#4fa892")),
                ModuleRole.Sleep => (new Color("#16142a"), new Color("#6a5fb0")),
                ModuleRole.Relax => (new Color("#22161a"), new Color("#b06a7e")),
                _ => (new Color("#221612"), new Color("#c0603c")),
            };
            Gfx.RoundRect(ci, r.Grow(-4f), bodyC, 4, edgeC, 2);
            Gfx.TextCentered(ci, Fonts.Bold, c + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, 11)), mv.Name[..1], 11, edgeC);
            return true;
        }
        switch (f.Type)
        {
            case FurnitureType.LedPanel:
                Gfx.RoundRect(ci, r.Grow(-4f), new Color("#1b1622"), 4, new Color("#5a3f6e"), 2);
                for (int k = 0; k < 4; k++)
                    ci.DrawRect(new Rect2(r.Position.X + 8 + k * (r.Size.X - 16) / 4f, r.Position.Y + 9, (r.Size.X - 16) / 4f - 2, r.Size.Y - 18), new Color("#2b2033"));
                return true;
            case FurnitureType.HeatExchanger:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#10202a"), 4, new Color("#2f6b86"), 2);
                for (int k = 0; k < 5; k++)
                {
                    float x = r.Position.X + 7 + k * (r.Size.X - 14) / 4f;
                    ci.DrawLine(new Vector2(x, r.Position.Y + 6), new Vector2(x, r.End.Y - 6), new Color("#23495c"), 2f);
                }
                return true;
            case FurnitureType.CapacitorBank:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#161a12"), 4, new Color("#5d6b2c"), 2);
                for (int k = 0; k < 3; k++)
                    Gfx.RoundRect(ci, new Rect2(r.Position.X + 6 + k * (r.Size.X - 12) / 3f, r.Position.Y + 6, (r.Size.X - 12) / 3f - 3, r.Size.Y - 12), new Color("#0d100a"), 3, new Color("#3a4420"));
                return true;
            case FurnitureType.Scrubber:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#141c1f"), 6, new Color("#3f6f78"), 2);
                ci.DrawCircle(c, Mathf.Min(r.Size.X, r.Size.Y) * 0.34f, new Color("#0b1215"), true, -1f, true);
                ci.DrawArc(c, Mathf.Min(r.Size.X, r.Size.Y) * 0.34f, 0f, Mathf.Tau, 24, new Color("#2c4d55"), 1.5f, true);
                return true;
            case FurnitureType.PartTestBench: // v14.6 부품 시험대: 물림쇠 + 바늘 계기
            {
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#17191d"), 4, new Color("#7a6a3a"), 2);
                ci.DrawRect(new Rect2(r.Position.X + 5, r.End.Y - 9, r.Size.X - 10, 4), new Color("#2b2a26"));
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.22f;
                ci.DrawCircle(c + new Vector2(0, -3), rad, new Color("#0e1013"), true, -1f, true);
                ci.DrawArc(c + new Vector2(0, -3), rad, 0f, Mathf.Tau, 18, new Color("#a08c4a"), 1.2f, true);
                return true;
            }
            case FurnitureType.Hoist: // v14.6 호이스트: 레일과 기둥
                ci.DrawLine(new Vector2(r.Position.X + 3, r.Position.Y + 5), new Vector2(r.End.X - 3, r.Position.Y + 5), new Color("#8a8f99"), 3f);
                ci.DrawLine(new Vector2(r.Position.X + 4, r.Position.Y + 5), new Vector2(r.Position.X + 4, r.End.Y - 3), new Color("#5a5f69"), 2f);
                ci.DrawLine(new Vector2(r.End.X - 4, r.Position.Y + 5), new Vector2(r.End.X - 4, r.End.Y - 3), new Color("#5a5f69"), 2f);
                return true;
            case FurnitureType.MaintCart: // v14.6 정비 카트: 붉은 상자에 바퀴
                Gfx.RoundRect(ci, new Rect2(r.Position.X + 5, r.Position.Y + 7, r.Size.X - 10, r.Size.Y - 14), new Color("#7a2a20"), 3, new Color("#e0623e"), 1);
                ci.DrawLine(new Vector2(r.Position.X + 7, r.Position.Y + 12), new Vector2(r.End.X - 7, r.Position.Y + 12), new Color("#2a1410"), 1f);
                ci.DrawCircle(new Vector2(r.Position.X + 8, r.End.Y - 6), 2.5f, new Color("#222222"), true, -1f, true);
                ci.DrawCircle(new Vector2(r.End.X - 8, r.End.Y - 6), 2.5f, new Color("#222222"), true, -1f, true);
                return true;
            case FurnitureType.Fabricator:
                Gfx.RoundRect(ci, r.Grow(-3f), new Color("#1a1814"), 4, new Color("#6b5a3a"), 2);
                ci.DrawRect(new Rect2(r.Position.X + 6, r.Position.Y + 6, r.Size.X - 12, 3), new Color("#4a4030"));
                Gfx.RoundRect(ci, new Rect2(r.Position.X + 8, r.End.Y - 12, r.Size.X - 16, 6), new Color("#2a261f"), 2);
                return true;
        }
        return false;
    }

    /// <summary>동적 층: 모듈이 일하는 모습.</summary>
    private void PaintModuleLife(CanvasItem ci, Furniture f, float t)
    {
        var m = f.Machine;
        bool on = m != null && m.Faults.Count == 0 && (m.Powered || m.Spec.PowerDraw <= 0f) && !f.Room.Abandoned;
        var r = FurnitureRect(f);
        var c = r.GetCenter();
        switch (f.Type)
        {
            case FurnitureType.LedPanel:
            {
                if (!on) break;
                var pink = new Color("#ff7fd0");
                for (int k = 0; k < 4; k++)
                {
                    var cell = new Rect2(r.Position.X + 8 + k * (r.Size.X - 16) / 4f, r.Position.Y + 9, (r.Size.X - 16) / 4f - 2, r.Size.Y - 18);
                    ci.DrawRect(cell, (k % 2 == 0 ? pink : new Color("#8f7fff")).WithAlpha(0.75f + 0.1f * Mathf.Sin(t * 2f + k)));
                }
                ci.DrawCircle(c, T * 1.3f, pink.WithAlpha(0.05f), true, -1f, true);
                break;
            }
            case FurnitureType.HeatExchanger:
            {
                var water = new Color("#56c8ff");
                for (int k = 0; k < 5; k++)
                {
                    float x = r.Position.X + 7 + k * (r.Size.X - 14) / 4f;
                    float ph = (t * (on ? 0.9f : 0f) + k * 0.23f) % 1f;
                    ci.DrawCircle(new Vector2(x, Mathf.Lerp(r.Position.Y + 7, r.End.Y - 7, ph)), 1.8f, water.WithAlpha(on ? 0.85f : 0.25f), true, -1f, true);
                }
                break;
            }
            case FurnitureType.CapacitorBank:
            {
                float charge = Mathf.Clamp(_world.Power.BatteryPercent, 0f, 1f);
                for (int k = 0; k < 3; k++)
                {
                    var cell = new Rect2(r.Position.X + 6 + k * (r.Size.X - 12) / 3f, r.Position.Y + 6, (r.Size.X - 12) / 3f - 3, r.Size.Y - 12);
                    float h = (cell.Size.Y - 4) * charge;
                    ci.DrawRect(new Rect2(cell.Position.X + 2, cell.End.Y - 2 - h, cell.Size.X - 4, h), (on ? new Color("#c6e85a") : new Color("#5c6640")).WithAlpha(0.8f));
                }
                break;
            }
            case FurnitureType.PartTestBench:
            {
                // 누가 부품을 물려 돌리고 있으면 바늘이 떨린다
                bool testing = on && _world.Crew.Any(x => x.Room == f.Room && x.Job?.Activity is PartTestActivity && x.Pose == Pose.Working);
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.2f;
                float a = -Mathf.Pi * 0.8f + (testing ? 0.6f + 0.25f * Mathf.Sin(t * 9f) : 0.1f);
                var o = c + new Vector2(0, -3);
                ci.DrawLine(o, o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad, testing ? new Color("#ffd27a") : new Color("#6d6450"), 1.2f, true);
                break;
            }
            case FurnitureType.Hoist:
            {
                // 무거운 부품을 갈고 있으면 갈고리가 내려와 흔들린다
                bool lifting = on && _world.Crew.Any(x => x.Job?.Order is WorkOrder wo && wo.Kind == WorkKind.Repair && x.Pose == Pose.Working
                    && wo.Target.Furniture?.Machine?.Faults.Any(fl => PartsSystem.Heavy(fl.Part)) == true);
                float drop = lifting ? r.Size.Y * 0.55f : r.Size.Y * 0.2f;
                float sway = lifting ? Mathf.Sin(t * 2.2f) * 2f : 0f;
                var top = new Vector2(c.X, r.Position.Y + 5);
                var hook = top + new Vector2(sway, drop);
                ci.DrawLine(top, hook, new Color("#b9bec8"), 1f, true);
                ci.DrawArc(hook + new Vector2(0, 2), 2.5f, 0f, Mathf.Pi, 6, new Color("#e0b040"), 1.4f, true);
                break;
            }
            case FurnitureType.Scrubber:
            {
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.3f;
                float a = on ? t * 6f : 0.3f;
                for (int k = 0; k < 4; k++)
                {
                    var d = Vector2.FromAngle(a + k * Mathf.Pi * 0.5f);
                    ci.DrawLine(c, c + d * rad, new Color("#7fd0c0").WithAlpha(on ? 0.8f : 0.3f), 2.5f, true);
                }
                ci.DrawCircle(c, 2.5f, new Color("#cfeee8"), true, -1f, true);
                break;
            }
            case FurnitureType.Fabricator:
            {
                float ph = on ? 0.5f + 0.5f * Mathf.Sin(t * 1.7f) : 0.5f;
                var head = new Vector2(Mathf.Lerp(r.Position.X + 9, r.End.X - 9, ph), r.Position.Y + 8);
                ci.DrawRect(new Rect2(head.X - 3, head.Y - 2, 6, 5), new Color("#c8b27a"));
                if (on)
                {
                    ci.DrawLine(head + new Vector2(0, 3), new Vector2(head.X, r.End.Y - 12), new Color("#ff5a4a").WithAlpha(0.8f), 1.2f);
                    ci.DrawCircle(new Vector2(head.X, r.End.Y - 12), 2f, new Color("#ffd0a0"), true, -1f, true);
                }
                break;
            }
        }
    }

    /// <summary>원자로 III·IV: 핵융합 — 푸른 플라스마와 도는 자기장 고리 (분열로의 노란 노심을 덮는다).</summary>
    private void PaintFusion(CanvasItem ci, Furniture f, float t)
    {
        var r = FurnitureRect(f);
        var c = r.GetCenter();
        float s = Mathf.Min(r.Size.X, r.Size.Y) / (3f * T);
        float load = Mathf.Clamp(_world.Power.ReactorOutput / Mathf.Max(1f, _world.Power.ReactorRated), 0.05f, 1f);
        bool online = _world.Power.ReactorOnline;
        var col = f.Machine!.Tier >= 4 ? PlasmaHot : Plasma;
        ci.DrawCircle(c, 30f * s, new Color("#0b0f1a"), true, -1f, true);
        float ring = 21f * s;
        ci.DrawArc(c, ring, 0f, Mathf.Tau, 64, col.WithAlpha(online ? 0.25f + 0.2f * load : 0.08f), 7f * s, true);
        if (online)
        {
            for (int k = 0; k < 3; k++)
            {
                float a = t * (1.2f + load) + k * Mathf.Tau / 3f;
                ci.DrawArc(c, ring, a, a + 0.9f, 20, col.WithAlpha(0.9f), 3f * s, true);
            }
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3f);
            ci.DrawCircle(c, (6f + 3f * load + pulse) * s, col.Lightened(0.4f).WithAlpha(0.9f), true, -1f, true);
            ci.DrawCircle(c, 3f * s, Colors.White, true, -1f, true);
            ci.DrawCircle(c, 38f * s, col.WithAlpha(0.05f + 0.04f * pulse), true, -1f, true);
        }
        // 자석 코일
        for (int k = 0; k < 8; k++)
        {
            var d = Vector2.FromAngle(k * Mathf.Tau / 8f);
            ci.DrawLine(c + d * (ring - 6f * s), c + d * (ring + 6f * s), new Color("#3b4a66"), 3f * s, true);
        }
    }

    /// <summary>단계 딱지: 오른쪽 위 로마 숫자 + 테두리.</summary>
    private void PaintTierBadges(CanvasItem ci)
    {
        foreach (var f in _world.Ship.Furniture)
        {
            if (f.Stowed || f.Room.Detached || f.Machine is not Machine m || m.Tier < 2) continue;
            var col = Hud.TierColor(m.Tier);
            var r = FurnitureRect(f).Grow(-1.5f);
            ci.DrawRect(r, col.WithAlpha(0.45f), false, 1.2f);
            string label = Tech.Roman(m.Tier);
            float w = 8f + 5f * label.Length;
            var tag = new Rect2(r.End.X - w - 2, r.Position.Y + 2, w, 11);
            Gfx.RoundRect(ci, tag, col.WithAlpha(0.95f), 3);
            Gfx.TextCentered(ci, Fonts.Bold, tag.GetCenter(), label, 8, new Color("#10131a"));
        }
    }
}
