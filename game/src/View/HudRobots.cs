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
        RobotState.Lost => r.Wrecked ? (r.AtShop ? "잔해 · 작업장에서 해체 대기" : "잔해") : "잃음",
        _ => "",
    };

    private void DrawRobotInspector(Robot r, float y, float maxHeight)
    {
        var col = ShipView.RobotColor(r.Kind);
        float x0 = ColX; // v20 고른 것 카드 자리 (창이 열리면 창 왼쪽)
        var marks = r.Marks.TakeLast(6).Reverse().ToList();
        bool wreck = r.Wrecked; // v22 잔해: 생각 · 거품 칸 없이 몸 · 이력만
        float height = Mathf.Min(maxHeight, 300 + (wreck ? 0 : RobotMindHeight(r)) + (!wreck && RobotSystem.FightsNow(r, _world) ? 22 : 0) + (r.CoreHeat > 0.05f || r.Runaway ? 22 : 0) + (r.Drafted(_world) ? 20 : 0) + (marks.Count > 0 ? 34 + marks.Count * 18 : 0) + 18); // v16.20b 생각 · v21 원자로 · 긴급 지정 · v22 몸
        var card = new Rect2(x0, y, RightColumnWidth, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;

        Gfx.RoundRect(this, new Rect2(x, y + 18, 16, 16), col.WithAlpha(0.3f), 5, col);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 32), r.Name, Ui.TextLarge, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 26, y + 50), UiKit.Fit($"{RobotsV21.MkName(r.Mk)} · 숙련 {r.Skill * 100:0}% · {r.Room?.Name ?? "?"} · {(RobotsV21.Reactor ? "정비대" : "충전대")} {r.Dock.Room.Name} · 버릇: {r.Quirk.Name}", right - (x + 26), Ui.TextBody, Fonts.Body), Ui.TextBody, Palette.TextMuted);
        var sc = r.Fault != null || r.State is RobotState.Stalled or RobotState.Towed ? Palette.Danger : r.Battery < 0.25f ? Palette.Warning : Palette.Good;
        Gfx.TextRight(this, Fonts.Bold, new Vector2(right, y + 32), RobotStateText(r), Ui.TextBody, sc);
        Divider(x, right, y + 64);

        float ly = y + 74;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), r.Doing, Ui.TextLabel, col.Lightened(0.25f));
        if (r.Order != null) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 32), $"맡은 일: {r.Order.Title}", Ui.TextSmall, Palette.TextMuted);
        else if (r.Helping != null) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 32), UiKit.Fit($"{Ko.EulReul(r.Helping.Name)} 거든다 — 긴 손일이 {RobotsV15.AssistBonus(r.Kind) * 100:0}% 빨라진다", right - (x), Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextMuted);
        if (r.Cargo is ItemStack cargo) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 48), $"싣고 있음: {cargo}", Ui.TextSmall, Palette.TextMuted);
        ly += 58;
        Row(x, right, ly, RobotsV21.Reactor ? "전지 (원자로)" : "배터리", r.Battery, Palette.Good, Pct(r.Battery), r.Battery < 0.25f);
        Row(x, right, ly + 22, "상태", r.Condition, new Color("#9fb4cc"), Pct(r.Condition), r.Condition < 0.45f);
        if (!wreck && RobotSystem.FightsNow(r, _world)) { Row(x, right, ly + 44, "소화 거품", r.Foam, new Color("#f4f1ec"), Pct(r.Foam), r.Foam < 0.2f); ly += 22; }
        if (RobotsV21.Reactor && (r.CoreHeat > 0.05f || r.Runaway)) { Row(x, right, ly + 44, r.Runaway ? "원자로 폭주!" : "원자로 열", r.CoreHeat, Palette.Danger, Pct(r.CoreHeat), r.CoreHeat > 0.5f); ly += 22; }
        if (r.Drafted(_world)) { Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 58), $"긴급 지정 — {RobotsV21.DraftName(r.Draft)} (주 컴퓨터)", Ui.TextSmall, Palette.Warning); ly += 20; }
        ly += 50;
        if (!wreck) ly = DrawRobotMind(r, x, right, ly); // v16.20b 지금 · 다음 · 배터리 · 왜 · 단계
        string fault = wreck ? (r.AtShop ? "잔해 — 작업장에서 해체를 기다린다 (쓸 부품은 다시 쓴다)" : r.State == RobotState.Towed ? "잔해 — 작업장으로 끌려가는 중" : "잔해 — 회수를 기다린다")
            : r.Fault is RobotFault f
            ? RobotSystem.CanSelfRepair(r)
                ? $"{RobotSystem.FaultName(f)} — 가벼움 · {(RobotsV21.Reactor ? "정비대" : "충전대")}에서 스스로 고친다 ({RobotSystem.SelfRepairHours(f) * 60:0}분)"
                : $"{RobotSystem.FaultName(f)} — 사람이 고쳐야 한다: " + (RobotSystem.WhyNotSelf(r) ?? "") +
                  (RobotSystem.FaultParts(f).Length == 0 ? "" : " · " + string.Join(" + ", RobotSystem.FaultParts(f).Select(p => $"{ItemKinds.Name(p.kind)} {p.count}")))
            : $"고장 없음 · 자가 수리 {r.SelfRepairs}/{RobotSystem.SelfRepairLimit} (사람 정비 뒤) · 임계점 상태 {RobotSystem.SelfRepairFloor * 100:0}%";
        Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), fault, Ui.TextBody, r.Fault == null ? Palette.TextDim : RobotSystem.CanSelfRepair(r) ? Palette.Warning : Palette.Danger);
        Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 34),
            $"한 일 {r.JobsDone}건 · 일한 {r.ActiveHours:0}시간 · 거든 {r.AssistHours:0.0}시간 · 고장 {r.Breakdowns}번(스스로 {r.SelfRepairsTotal}) · 끌려옴 {r.Fetched}번", Ui.TextSmall, Palette.TextMuted);
        string can = r.Kind switch
        {
            _ when RobotsV15.Bot(r.Kind) is { } v => $"특기: {v.Note}" + (RobotsV21.General ? " · 기본 행동은 다 한다" : ""), // v15.7 · v21
            RobotKind.Hauler => "맡는 일: 배식기 채우기 · 드론 자재 보급 · 물통 급수 · 비상 물자함",
            RobotKind.Maintainer => "맡는 일: 정기 정비(원자로 빼고) · 조명 · 사람 옆에서 거들기",
            RobotKind.Gardener => "맡는 일: 작물 돌보기 · 수확해 냉장고로",
            _ => "맡는 일: 순찰(불·사고 전조 찾기) · 소화 거품",
        };
        Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 52), can, Ui.TextSmall, Palette.TextMuted);
        // v22 몸: 부위마다 단계 · 덧댄 판 · 이어받은 잔해
        string body = $"몸: {RobotBody.Line(r)}" + (r.FromWreck != "" ? $" · {Ko.EulReul(r.FromWreck)} 이어 짠 {r.Rebuilt + 1}번째 몸" : "");
        Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 70), UiKit.Fit(body, right - x, Ui.TextSmall, Fonts.Body), Ui.TextSmall,
            RobotBody.Worst(r) < 0.25f ? Palette.Danger : RobotBody.Damaged(r) > 0 ? Palette.Warning : Palette.TextDim);
        ly += 82;
        if (marks.Count > 0)
        {
            SectionTitle(x, ly + 10, "이력");
            ly += 18;
            foreach (var m in marks)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), UiKit.Fit($"{SimTime.Day(m.Tick)}일 {SimTime.Clock(m.Tick)}  {m.Text}", right - (x), Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
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
            list.Add(("전조", $"{Prevention.Name(o.Kind)} · {Ko.IGa(o.KnownBy)} 찾음 · {Faults.Spec(o.Fault).Name}까지 {left:0}시간쯤"));
        }
    }
}
