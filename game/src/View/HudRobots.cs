using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>v10.10 로봇 상세 · 충전대/비상 물자함/전조 줄 · 비축 방침.</summary>
public partial class Hud
{
    private static string RobotStateText(Robot r) => r.State switch
    {
        RobotState.Docked => r.Fault != null ? "충전대 · 고장" : r.Disabled ? "꺼 둠" : r.Battery < 0.99f ? $"충전 {Pct(r.Battery)}" : "대기",
        RobotState.Active => r.Helping != null ? "거드는 중" : r.Order != null ? "일하는 중" : r.Doing.StartsWith("충전대로") ? "돌아가는 중" : "움직이는 중",
        RobotState.Stalled => r.Fault != null ? "고장 · 멈춤" : "방전 · 멈춤",
        RobotState.Towed => "끌려가는 중",
        RobotState.Lost => "잃음",
        _ => "",
    };

    private void DrawRobotInspector(Robot r, float y, float maxHeight)
    {
        var col = ShipView.RobotColor(r.Kind);
        float x0 = Screen.X - Margin - RightColumnWidth;
        var marks = r.Marks.TakeLast(6).Reverse().ToList();
        float height = Mathf.Min(maxHeight, 300 + (RobotsV15.Fights(r.Kind) ? 22 : 0) + (marks.Count > 0 ? 34 + marks.Count * 18 : 0));
        var card = new Rect2(x0, y, RightColumnWidth, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;

        Gfx.RoundRect(this, new Rect2(x, y + 18, 16, 16), col.WithAlpha(0.3f), 5, col);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 32), r.Name, 17, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 26, y + 50), $"{r.Room?.Name ?? "?"} · 충전대 {r.Dock.Room.Name} · 버릇: {r.Quirk.Name}", 12, Palette.TextMuted);
        var sc = r.Fault != null || r.State is RobotState.Stalled or RobotState.Towed ? Palette.Danger : r.Battery < 0.25f ? Palette.Warning : Palette.Good;
        Gfx.TextRight(this, Fonts.Bold, new Vector2(right, y + 32), RobotStateText(r), 12, sc);
        Divider(x, right, y + 64);

        float ly = y + 74;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), r.Doing, 13, col.Lightened(0.25f));
        if (r.Order != null) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 32), $"맡은 일: {r.Order.Title}", 11, Palette.TextMuted);
        else if (r.Helping != null) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 32), $"{Ko.EulReul(r.Helping.Name)} 거든다 — 긴 손일이 {RobotsV15.AssistBonus(r.Kind) * 100:0}% 빨라진다", 11, Palette.TextMuted);
        if (r.Cargo is ItemStack cargo) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 48), $"싣고 있음: {cargo}", 11, Palette.TextMuted);
        ly += 58;
        Row(x, right, ly, "배터리", r.Battery, Palette.Good, Pct(r.Battery), r.Battery < 0.25f);
        Row(x, right, ly + 22, "상태", r.Condition, new Color("#9fb4cc"), Pct(r.Condition), r.Condition < 0.45f);
        if (RobotsV15.Fights(r.Kind)) { Row(x, right, ly + 44, "소화 거품", r.Foam, new Color("#f4f1ec"), Pct(r.Foam), r.Foam < 0.2f); ly += 22; }
        ly += 50;
        string fault = r.Fault is RobotFault f
            ? RobotSystem.CanSelfRepair(r)
                ? $"{RobotSystem.FaultName(f)} — 가벼움 · 충전대에서 스스로 고친다 ({RobotSystem.SelfRepairHours(f) * 60:0}분)"
                : $"{RobotSystem.FaultName(f)} — 사람이 고쳐야 한다: " + (RobotSystem.WhyNotSelf(r) ?? "") +
                  (RobotSystem.FaultParts(f).Length == 0 ? "" : " · " + string.Join(" + ", RobotSystem.FaultParts(f).Select(p => $"{ItemKinds.Name(p.kind)} {p.count}")))
            : $"고장 없음 · 자가 수리 {r.SelfRepairs}/{RobotSystem.SelfRepairLimit} (사람 정비 뒤) · 임계점 상태 {RobotSystem.SelfRepairFloor * 100:0}%";
        Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), fault, 12, r.Fault == null ? Palette.TextDim : RobotSystem.CanSelfRepair(r) ? Palette.Warning : Palette.Danger);
        Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 34),
            $"한 일 {r.JobsDone}건 · 일한 {r.ActiveHours:0}시간 · 거든 {r.AssistHours:0.0}시간 · 고장 {r.Breakdowns}번(스스로 {r.SelfRepairsTotal}) · 끌려옴 {r.Fetched}번", 11, Palette.TextMuted);
        string can = r.Kind switch
        {
            _ when RobotsV15.Bot(r.Kind) is { } v => $"맡는 일: {v.Note}", // v15.7
            RobotKind.Hauler => "맡는 일: 배식기 채우기 · 드론 자재 보급 · 물통 급수 · 비상 물자함",
            RobotKind.Maintainer => "맡는 일: 정기 정비(원자로 빼고) · 조명 · 사람 옆에서 거들기",
            RobotKind.Gardener => "맡는 일: 작물 돌보기 · 수확해 냉장고로",
            _ => "맡는 일: 순찰(불·사고 전조 찾기) · 소화 거품",
        };
        Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 52), can, 11, Palette.TextMuted);
        ly += 64;
        if (marks.Count > 0)
        {
            SectionTitle(x, ly + 10, "이력");
            ly += 18;
            foreach (var m in marks)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), $"{SimTime.Day(m.Tick)}일 {SimTime.Clock(m.Tick)}  {m.Text}", 11, Palette.TextDim);
                ly += 18;
            }
        }
    }

    /// <summary>충전대·비상 물자함·전조가 있는 설비의 줄.</summary>
    private void RobotExtras(Furniture f, List<(string label, string value)> list)
    {
        if (f.Type == FurnitureType.RobotDock)
            foreach (var r in _world.Robots.Robots.Where(r => r.Dock == f))
                list.Add((r.Name, $"{RobotStateText(r)} · 배터리 {Pct(r.Battery)} · 상태 {Pct(r.Condition)} · 한 일 {r.JobsDone}"));
        if (f.Type == FurnitureType.SupplyCache && f.Storage is Inventory inv)
        {
            var target = Logistics.CacheTarget(_world);
            list.Add(("비상 물자", string.Join(" · ", target.Select(t => $"{ItemKinds.Name(t.kind)} {inv.Count(t.kind)}/{t.count}"))));
            list.Add(("비축 방침", Logistics.ModeName(_world.Ledger.Mode) + (_world.Ledger.ModeWhy.Length > 0 ? $" — {_world.Ledger.ModeWhy}" : "")));
        }
        // v11.2 균이 든 식사 (관찰자는 안다 — 배가 알아챘는지는 따로)
        if (f.Storage is Inventory food && food.Tainted > 0)
            list.Add(("오염", $"식사 {food.Tainted}끼 · " + (food.TaintKnown ? "버리러 온다" : "아무도 모른다")));
        if (f.Type == FurnitureType.SuitLocker)
        {
            float days = (_world.Tick - f.Checked) / (float)SimTime.TicksPerDay;
            list.Add(("점검", days < 5f ? $"{days:0.0}일 전" : $"{days:0}일 전 — 밸브가 샐 수 있다"));
        }
        if (f.Type == FurnitureType.EngineCore)
        {
            var p = _world.Propulsion;
            var (cq, by, _) = p.Control();
            list.Add(("항로", $"{PropulsionSystem.ZoneName(p.Zone)} · 잔해 밀도 {_world.Space.Density:0.00}" + (p.Zone == ZoneKind.Debris ? $" · 날아든 운석 {p.HitsThisZone}" : "")));
            list.Add(("추진제", $"{p.Propellant:0}/{p.Capacity:0}kg (물) · 회피 {p.EvadeCost:0}kg · 항로 변경 {p.TransferCost:0}kg"));
            list.Add(("추력·조종", $"추력 {p.Thrust * 100:0}% · {by} ({cq * 100:0}%)" + (p.Burning ? " · 연소 중" : "")));
            list.Add(("회피 기동", $"{p.Evasions}번 · 비껴감 {p.Dodged} · 스침 {p.Glanced} · 실패 {p.Missed}"));
        }
        if (f.Machine?.Omen is Omen o && o.Known)
        {
            float left = (o.Due - _world.Tick) / (float)SimTime.TicksPerHour;
            list.Add(("전조", $"{Prevention.Name(o.Kind)} · {o.KnownBy}이(가) 찾음 · {Faults.Spec(o.Fault).Name}까지 {left:0}시간쯤"));
        }
    }
}
