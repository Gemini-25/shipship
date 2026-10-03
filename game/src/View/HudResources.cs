using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.2 자원은 "언제 문제가 되나": 아이콘 + 추세 화살표 + "산소 · 14시간 뒤 부족".
/// 마우스를 올리면 최근 하루 그래프와 생산/소비 분해. 조용한 HUD에서는 핵심 자원만 작게, 이상이 생긴 것만 떠오른다.
/// </summary>
public partial class Hud
{
    /// <summary>화면이 10분마다 적어 두는 자원 기록 (읽기만 — 시뮬레이션에 닿지 않는다).</summary>
    private readonly ResourceWatch _watch = new();

    /// <summary>조용한 HUD (기본). 설정의 "전부 보기"를 켜면 예전처럼 다 띄운다.</summary>
    public bool Quiet => !Settings.ShowAllHud;

    private Action? _tip;

    private static string ChipIcon(string label) => label switch
    {
        "전력" => "power",
        "배터리" => "battery",
        "산소" => "oxygen",
        "공기 탱크" => "airtank",
        "물" => "water",
        "식량" or "먹을 것" => "food",
        "작업" => "work",
        "땜질" => "patch",
        "구조" => "structure",
        "CO2" => "co2",
        "냉각수·노심" => "coolant",
        "자동화" => "automation",
        "운석 접근" => "meteor",
        "센서" => "sensor",
        "비축" => "materials",
        "항로" => "route",
        "태양 폭풍" => "storm",
        "유독 가스" => "toxin",
        "보급 캡슐" or "탈출 캡슐" => "capsule",
        "구조 요청" => "signal",
        "배급" => "ration",
        "병충해" => "blight",
        "전조" => "omen",
        _ => "info",
    };

    private static ResourceKey? ChipResource(string label) => label switch
    {
        "배터리" => ResourceKey.Power,
        "산소" => ResourceKey.Oxygen,
        "공기 탱크" => ResourceKey.AirTank,
        "CO2" => ResourceKey.CO2,
        "물" => ResourceKey.Water,
        "식량" => ResourceKey.Food,
        _ => null,
    };

    /// <summary>조용한 HUD에서도 늘 보이는 핵심 자원.</summary>
    private static bool CoreChip(string label) => label is "배터리" or "산소" or "물" or "식량";

    /// <summary>윗줄 칩 그리기 (조용한 HUD · 전부 보기). 칩 목록은 DrawStatus가 만든다.</summary>
    private void DrawStatusChips(float x0, List<(string label, string value, Color color, float? bar)> chips, Vector2 mouse)
    {
        bool quiet = Quiet;
        var items = new List<(string label, string value, Color color, float? bar, ResourceKey? res, bool compact)>();
        foreach (var (label, value0, color0, bar) in chips)
        {
            string value = value0;
            var color = color0;
            var res = ChipResource(label);
            if (res is ResourceKey rk)
            {
                var st = _watch.Status(_world, rk);
                if (st.Surfaced && st.HoursLeft is float left && left > 0.01f)
                {
                    value += $" · {Readout.When(left)} {(rk == ResourceKey.CO2 ? "위험" : "부족")}";
                    if (color == Palette.Text) color = Palette.Warning;
                }
                else if (st.Surfaced && color == Palette.Text) color = Palette.Warning;
            }
            bool abnormal = color != Palette.Text;
            if (quiet && !abnormal && !CoreChip(label)) continue; // 조용한 HUD: 평상인 것은 접는다
            bool compact = quiet && !abnormal;
            if (compact && label == "식량") value = $"{FoodPolicy.FoodDays(_world):0.#}일치";
            items.Add((label, value, color, bar, res, compact));
        }

        const float pad = 16f, gap = 18f;
        float Width(int i)
        {
            var it = items[i];
            float tw = Gfx.Width(Fonts.Bold, it.value, Ui.TextLabel);
            if (it.compact) return Ui.IconM + 6 + tw + (it.res != null ? 16 : 0);
            return Mathf.Max(tw + (it.res != null ? 16 : 0), Gfx.Width(Fonts.Body, it.label, Ui.TextSmall) + 16);
        }
        var widths = Enumerable.Range(0, items.Count).Select(Width).ToList();
        float w = pad * 2 - gap + widths.Sum() + gap * items.Count;
        // 오른쪽 승무원 칸을 넘지 않게: 가장 긴 글(구조·땜질)부터 줄인다
        float avail = Mathf.Min(Screen.X - Margin - RightColumnWidth - 12f - x0, _plan.StatusMaxW > 0f ? _plan.StatusMaxW : float.MaxValue);
        for (int guard = 0; w > avail && guard < 8; guard++)
        {
            int i = Enumerable.Range(0, items.Count).Where(k => !items[k].compact && items[k].res == null || items[k].label is "구조" or "땜질" or "냉각수·노심" or "비축")
                .OrderByDescending(k => widths[k]).DefaultIfEmpty(-1).First();
            if (i < 0) break;
            var it = items[i];
            float target = Mathf.Max(60f, widths[i] - (w - avail));
            string value = it.value;
            while (value.Length > 4 && Gfx.Width(Fonts.Bold, value + "…", Ui.TextLabel) > target) value = value[..^1];
            items[i] = it with { value = value.TrimEnd(' ', '·') + "…" };
            w -= widths[i];
            widths[i] = Width(i);
            w += widths[i];
            if (widths[i] >= target + 20f) break;
        }
        // v17.6 그래도 넘치면 뒤쪽의 평상 칩부터 "외 n" 하나로 접는다 (경고색 칩은 남긴다 · 누르면 펼친 목록)
        var foldedChips = new List<string>();
        const float moreW = 46f;
        while (w > avail && items.Count > 1)
        {
            int k = items.FindLastIndex(it => it.color == Palette.Text);
            if (k < 0) k = items.Count - 1;
            foldedChips.Insert(0, $"{items[k].label} {items[k].value}");
            w -= widths[k] + gap;
            items.RemoveAt(k);
            widths.RemoveAt(k);
            if (foldedChips.Count == 1) w += moreW + gap;
        }
        var card = new Rect2(x0, Margin, Mathf.Max(w, 60f), Ui.TopBarH);
        bool alarm = items.Any(it => it.color == Palette.Danger);
        Card(card);
        if (alarm) Gfx.RoundRect(this, card, new Color(0, 0, 0, 0), Ui.RadiusCard, Palette.Danger.WithAlpha(0.25f + 0.15f * Mathf.Sin(_time * 3f)));

        float x = card.Position.X + pad;
        for (int i = 0; i < items.Count; i++)
        {
            var (label, value, color, bar, res, compact) = items[i];
            var hit = new Rect2(x - 6, card.Position.Y + 4, widths[i] + 12, card.Size.Y - 8);
            bool hover = hit.HasPoint(mouse);
            if (hover) Gfx.RoundRect(this, hit, Ui.HoverSoft, Ui.RadiusChip);
            var trend = res is ResourceKey rk0 ? _watch.Status(_world, rk0).Dir : TrendDir.Flat;
            // 화살표 색: 나빠지는 쪽이면 주의색
            bool worse = res is ResourceKey rk1 && trend != TrendDir.Flat && (trend == TrendDir.Down) == ResourceWatch.Rule(rk1).falling;
            var arrowCol = worse ? Palette.Warning : Palette.TextMuted;
            if (compact)
            {
                float cy = card.GetCenter().Y;
                Icons.Draw(this, ChipIcon(label), new Vector2(x + Ui.IconM * 0.5f, cy), Ui.IconM, Palette.TextDim);
                float vx = x + Ui.IconM + 6;
                Gfx.Text(this, Fonts.Bold, new Vector2(vx, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextLabel)), value, Ui.TextLabel, color);
                if (res != null) Icons.Draw(this, UiKit.TrendIcon(trend), new Vector2(vx + Gfx.Width(Fonts.Bold, value, Ui.TextLabel) + 9, cy), 12, arrowCol);
                if (bar is float b) Gfx.Bar(this, new Rect2(vx, cy + 11, widths[i] - Ui.IconM - 6, 2), b, color.WithAlpha(0.5f));
            }
            else
            {
                Icons.Draw(this, ChipIcon(label), new Vector2(x + 6, card.Position.Y + 17), 12, color == Palette.Text ? Palette.TextMuted : color);
                Gfx.Text(this, Fonts.Body, new Vector2(x + 16, card.Position.Y + 21), label, Ui.TextSmall, Palette.TextMuted);
                Gfx.Text(this, Fonts.Bold, new Vector2(x, card.Position.Y + 40), value, Ui.TextLabel, color);
                if (res != null) Icons.Draw(this, UiKit.TrendIcon(trend), new Vector2(x + Gfx.Width(Fonts.Bold, value, Ui.TextLabel) + 9, card.Position.Y + 35), 12, arrowCol);
                if (bar is float b)
                    Gfx.Bar(this, new Rect2(x, card.Position.Y + 45, widths[i], 3), b, color.WithAlpha(0.8f));
            }
            if (hover && res is ResourceKey rk2)
            {
                var anchor = new Vector2(hit.GetCenter().X, card.End.Y);
                _tip = () => ResourceTooltip(rk2, anchor);
            }
            x += widths[i] + gap;
        }
        if (foldedChips.Count > 0)
        {
            var more = new Rect2(x - 6, card.Position.Y + 10, moreW, card.Size.Y - 20);
            bool hv = more.HasPoint(mouse);
            Gfx.RoundRect(this, more, hv ? Ui.Hover : new Color(1, 1, 1, 0.03f), Ui.RadiusChip, Palette.PanelBorder);
            Gfx.TextCentered(this, Fonts.Bold, more.GetCenter(), $"+{foldedChips.Count}", Ui.TextLabel, Palette.TextDim);
            if (hv)
            {
                var at = new Vector2(more.Position.X, card.End.Y + 6);
                _tip = () => UiKit.Tooltip(this, at, Screen, "접어 둔 것", foldedChips.Select(t => new TipLine(t, Palette.TextDim)).ToList());
            }
        }
    }

    private static FurnitureType[] Makers(ResourceKey k) => k switch
    {
        ResourceKey.Power => new[] { FurnitureType.ReactorCore, FurnitureType.AuxGenerator, FurnitureType.Battery },
        ResourceKey.Oxygen or ResourceKey.AirTank or ResourceKey.CO2 => new[] { FurnitureType.OxygenGenerator, FurnitureType.Scrubber },
        ResourceKey.Water => new[] { FurnitureType.WaterRecycler },
        _ => new[] { FurnitureType.GrowBed, FurnitureType.Stove, FurnitureType.MealDispenser },
    };

    /// <summary>설비가 덜 내는 까닭을 다른 시스템까지 이어서 (전기 · 물 · 고장 · 마모). 멀쩡하면 null.</summary>
    private static string? MachineCause(Furniture f)
    {
        var m = f.Machine!;
        if (m.Faults.Count > 0) return string.Join("·", m.Faults.Take(2).Select(x => x.Name));
        if (!m.Powered && m.Spec.PowerDraw > 0f) return f.Room.Powered ? "전기가 끊김" : $"{f.Room.Name} 정전";
        if (f.Type == FurnitureType.OxygenGenerator && (!f.Room.WaterLinked || f.Room.ValveShut)) return "단수 → 전해할 물이 없다";
        if (f.Room.Dark && f.Type == FurnitureType.GrowBed) return "조명이 꺼짐";
        if (m.Efficiency < 0.9f) return m.Wear > 0.5f ? "닳음" : "덜 돈다";
        return null;
    }

    private void MakerLines(ResourceKey k, List<TipLine> lines)
    {
        int shown = 0, total = 0;
        foreach (var t in Makers(k))
            foreach (var f in _world.Ship.FurnitureOf(t))
            {
                if (f.Machine == null || f.Room.Detached) continue;
                total++;
                if (shown >= 3 || MachineCause(f) is not string cause) continue;
                float e = f.Machine.Efficiency;
                lines.Add(new($"{f.Label} · {cause} → {e * 100:0}%", e < 0.3f ? Palette.Danger : Palette.Warning, Icons.Furniture(f.Type)));
                shown++;
            }
        if (shown == 0 && total > 0)
            lines.Add(new($"{FurnitureTypes.Name(Makers(k)[0])} 등 설비 {total}대 모두 정상", Palette.Good, Icons.Furniture(Makers(k)[0])));
    }

    /// <summary>주컴퓨터가 이 자원을 두고 본 것 · 판단 (최근 6시간 판단 기록 · 물 30일 예측). 읽기만 한다.</summary>
    private void ComputerLines(ResourceKey k, List<TipLine> lines)
    {
        var a = _world.Automation;
        if (!a.Present) return;
        if (!a.MainOnline) { lines.Add(new("주 컴퓨터가 멎어 이 자원을 지켜보지 못한다", Palette.Danger, "computer")); return; }
        string[] words = k switch
        {
            ResourceKey.Power => new[] { "전력", "배터리", "부하", "원자로" },
            ResourceKey.Oxygen or ResourceKey.AirTank => new[] { "산소", "공기 탱크" },
            ResourceKey.CO2 => new[] { "CO2", "CO₂", "세정" },
            ResourceKey.Water => new[] { "물", "정수", "급수" },
            _ => new[] { "식량", "배급", "식사" },
        };
        long since = _world.Tick - SimTime.Hours(6);
        var acts = a.Book.Acts;
        for (int i = acts.Count - 1; i >= 0 && acts[i].Tick >= since; i--)
        {
            var act = acts[i];
            if (!words.Any(wd => act.Observe.Contains(wd) || act.Judge.Contains(wd))) continue;
            string said = act.Judge.Length > 0 ? act.Judge : act.Observe;
            lines.Add(new($"{a.Voice.Call}: {said}" + (act.Act.Length > 0 ? $" → {act.Act}" : ""), Palette.Accent, "computer"));
            break;
        }
        if (k == ResourceKey.Water && a.Active(ComputerModule.WaterPlan))
        {
            int d = a.Apps.WaterEmptyDay;
            lines.Add(new(d >= 0 ? $"{a.Voice.Call} 30일 물 예측: {d}일 뒤 탱크가 바닥난다" : $"{a.Voice.Call} 30일 물 예측: 한 달 안에는 바닥나지 않는다",
                d >= 0 && d <= 12 ? Palette.Warning : Palette.Accent, "computer"));
        }
    }

    /// <summary>자원 툴팁: 최근 하루 그래프 + 문턱 + 생산/소비 분해.</summary>
    private void ResourceTooltip(ResourceKey k, Vector2 anchor)
    {
        var st = _watch.Status(_world, k);
        var (th, falling, window, _, _) = ResourceWatch.Rule(k);
        var lines = new List<TipLine>();
        string unit = k switch { ResourceKey.Oxygen => " kPa", ResourceKey.Water => " L", ResourceKey.Food => "일치", _ => "" };
        string Val(float v) => k is ResourceKey.Power or ResourceKey.AirTank ? $"{v * 100:0}%" : k == ResourceKey.CO2 ? $"{v:0.00}" : $"{v:0.#}{unit}";
        if (st.HoursLeft is float left)
            lines.Add(new(left <= 0.01f ? $"지금 문턱({Val(th)})을 넘었다" : $"이대로면 {Readout.When(left)} {(falling ? "부족" : "위험")} — 문턱 {Val(th)}", left < 6f ? Palette.Danger : Palette.Warning));
        else lines.Add(new($"최근 {window:0.#}시간 추세로는 문제없다 · 문턱 {Val(th)}", Palette.Good));
        if (st.SlopePerHour is float s)
            lines.Add(new($"최근 {window:0.#}시간: 시간당 {(k is ResourceKey.Power or ResourceKey.AirTank ? $"{s * 100:+0.0;-0.0}%p" : $"{s:+0.00;-0.00}{unit}")}", Palette.TextDim));
        else lines.Add(new("추세를 보려면 30분쯤 지켜봐야 한다", Palette.TextMuted));
        int alive = _world.Crew.Count(c => !c.Dead);
        var p = _world.Power;
        var air = _world.Air;
        switch (k)
        {
            case ResourceKey.Power:
                lines.Add(new($"원자로 {p.ReactorOutput:0} kW · 수요 {p.Demand:0} kW · 공급 {p.Delivered:0} kW", Palette.TextDim));
                lines.Add(new($"배터리 {p.BatteryCharge:0}/{p.BatteryCapacity:0} · {(p.BatteryFlow >= 0 ? "충전" : "방전")} {MathF.Abs(p.BatteryFlow):0.0} kW", p.BatteryFlow < -0.1f ? Palette.Warning : Palette.TextDim));
                if (p.AuxRunning) lines.Add(new($"보조 발전 {p.AuxOutput:0.0} kW · 연료 {p.AuxFuel:0.0}시간", Palette.TextDim));
                if (p.ShedCount > 0) lines.Add(new($"끊은 부하 {p.ShedCount}곳", Palette.Danger));
                break;
            case ResourceKey.Oxygen:
                lines.Add(new($"발생 +{air.O2Produced:0} · 호흡 −{alive * Atmosphere.BreathO2:0} (kPa·칸/시간)", Palette.TextDim));
                lines.Add(new($"발생기 용량 {air.O2Capacity:0} · 공기 탱크 {(air.ReserveCapacity > 0 ? air.Reserve / air.ReserveCapacity * 100 : 0):0}%", Palette.TextDim));
                break;
            case ResourceKey.AirTank:
                lines.Add(new($"남은 공기 {air.Reserve:0} / {air.ReserveCapacity:0} (kPa·칸)", Palette.TextDim));
                break;
            case ResourceKey.CO2:
                lines.Add(new($"제거 −{air.CO2Scrubbed:0} · 호흡 +{alive * Atmosphere.BreathCO2:0} (kPa·칸/시간)", Palette.TextDim));
                break;
            case ResourceKey.Water:
                lines.Add(new($"정수 +{_world.Water.Produced:0.0} L/시간 · 사용 −{_world.Water.Consumed:0.0} L/시간", Palette.TextDim));
                lines.Add(new($"탱크 {_world.Water.Level:0} / {_world.Water.Capacity:0} L", Palette.TextDim));
                break;
            default:
                lines.Add(new($"재고 {FoodPolicy.FoodStock(_world):0}끼 · 재배 +{FoodPolicy.GrowingPerDay(_world):0.0}끼/일 · 소비 −{alive * FoodPolicy.MealsPerPersonDay:0.0}끼/일", Palette.TextDim));
                if (_world.Food.Rationing) lines.Add(new("배급 중", Palette.Warning));
                break;
        }
        // 그 자원을 만드는 설비: 왜 덜 내는지 (고장 · 정전 · 단수 · 닳음 → 효율) — 설비마다 고유 아이콘
        MakerLines(k, lines);
        // 주컴퓨터가 이 자원을 두고 본 것 · 판단 (화면의 추정과 나란히)
        ComputerLines(k, lines);
        var col = st.Surfaced ? Palette.Warning : Palette.Accent;
        UiKit.Tooltip(this, anchor, Screen, $"{ResourceWatch.Name(k)} · {Val(st.Value)}", lines, _watch[k].Samples, th, col, Icons.Resource(k));
    }
}
