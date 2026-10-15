using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 아래 탭 "배" — 항상성 · 재료 (한 화면):
///   위: 온도 · 습도 · 이산화탄소 · 기압 · 방사선 — 사람 있는 방 가운데 가장 나쁜 값 · 목표 · 상태 · 주 컴퓨터가 지금 하는 대응. 선체 바깥 온도와 앞으로 열두 시간 예보.
///   아래: 원료마다 남은 양 · 며칠 치 · 급한 정도 · 채집 장치 수(배 크기에 맞춘 수) · 배 안에서 되살린 것.
/// </summary>
public partial class Hud
{
    private void DrawHomeoPage(Vector2 mouse)
    {
        var w = _world;
        var h = w.Homeo;
        var rows = h.Readings();
        var mats = w.Mats;
        const float rowH = 24f;
        float height = 16f + 30f + rows.Count * rowH + 30f + 14f + 30f + MaterialSystem.Raw.Length * rowH + 50f;
        var card = PageCard(height);
        float x0 = card.Position.X + 16f, x1 = card.End.X - 16f, y = card.Position.Y + 14f;
        float wdt = x1 - x0;

        // ── 항상성 ──
        Gfx.Text(this, Fonts.Bold, new Vector2(x0, y + 14f), "항상성 — 주 컴퓨터가 지킨다", Ui.TextLabel, Palette.Text);
        string shell = w.Planet?.Landed == true ? $"행성 · 바깥 {w.Planet.OutsideC:0}℃" : $"햇빛 ×{h.Sun:0.##} · {VoyageSystem.KindName(w.Voyage.Current.Kind)}";
        Gfx.TextRight(this, Fonts.Body, new Vector2(x1, y + 14f), shell, Ui.TextTiny, Palette.TextMuted);
        y += 30f;
        float cName = x0, cNow = x0 + wdt * 0.17f, cGoal = x0 + wdt * 0.33f, cResp = x0 + wdt * 0.5f;
        foreach (var r in rows)
        {
            var col = r.Level >= 2 ? Palette.Danger : r.Level == 1 ? Palette.Warning : Palette.Good;
            Gfx.RoundRect(this, new Rect2(x0 - 6f, y, wdt + 12f, rowH - 3f), new Color(1, 1, 1, 0.02f), 5);
            this.Circle(new Vector2(cName + 4f, y + rowH * 0.45f), 4f, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(cName + 14f, y + 15f), r.Name, Ui.TextBody, Palette.Text);
            Gfx.Text(this, Fonts.Bold, new Vector2(cNow, y + 15f), UiKit.Fit(r.Now, cGoal - cNow - 6f, Ui.TextBody), Ui.TextBody, col);
            Gfx.Text(this, Fonts.Body, new Vector2(cGoal, y + 15f), UiKit.Fit($"목표 {r.Target}", cResp - cGoal - 6f, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(cResp, y + 15f), UiKit.Fit(r.Response, x1 - cResp, Ui.TextSmall), Ui.TextSmall, r.Level > 0 ? Palette.Text : Palette.TextDim);
            y += rowH;
        }
        var (cold, hot, why, hours) = h.Forecast();
        string fc = hours > 0.5f ? $"예보 — {hours:0}시간 뒤 {why}: 선체 {cold:0}~{hot:0}℃" : $"예보 — 앞으로 열두 시간 {why} 그대로 (선체 {cold:0}~{hot:0}℃)";
        fc += h.HeatBoost > 1f ? " · 원자로 출력을 보태는 중" : "";
        fc += h.Rolling ? " · 천천히 굴리는 중" : "";
        fc += h.Bias > 0f ? " · 미리 덥혀 둠" : h.Bias < 0f ? " · 미리 식혀 둠" : "";
        Gfx.Text(this, Fonts.Body, new Vector2(x0, y + 16f), UiKit.Fit(fc, wdt, Ui.TextSmall), Ui.TextSmall, cold < -6f || hot > 26f ? Palette.Warning : Palette.TextDim);
        y += 30f;
        DrawLine(new Vector2(x0, y), new Vector2(x1, y), new Color(1, 1, 1, 0.06f), 1f);
        y += 14f;

        // ── 재료 ──
        int have = w.Ship.FurnitureOf(FurnitureType.Collector).Count(), want = mats.CollectorsWanted();
        Gfx.Text(this, Fonts.Bold, new Vector2(x0, y + 14f), "재료 — 며칠 치 남았나", Ui.TextLabel, Palette.Text);
        Gfx.TextRight(this, Fonts.Body, new Vector2(x1, y + 14f), $"채집 장치 {have}/{want}대 · 채집 {w.Space.DensityName}", Ui.TextTiny, have < want ? Palette.Warning : Palette.TextMuted);
        y += 30f;
        var urgent = mats.Urgent();
        foreach (var k in MaterialSystem.Raw)
        {
            float u = mats.Urgency(k), days = mats.DaysLeft(k);
            int n = w.Ship.CountStored(k);
            var col = u >= 0.75f ? Palette.Danger : u >= 0.4f ? Palette.Warning : Palette.Good;
            Gfx.Text(this, Fonts.Bold, new Vector2(cName, y + 15f), ItemKinds.Name(k) ?? k.ToString(), Ui.TextBody, Palette.Text);
            Gfx.Text(this, Fonts.Bold, new Vector2(cNow, y + 15f), $"{n}", Ui.TextBody, col);
            Gfx.Text(this, Fonts.Body, new Vector2(cGoal, y + 15f), days >= 90f ? "줄지 않는다" : $"{days:0}일 치", Ui.TextSmall, Palette.TextMuted);
            var bar = new Rect2(cResp, y + 7f, (x1 - cResp) * 0.45f, 8f);
            Gfx.RoundRect(this, bar, new Color(1, 1, 1, 0.06f), 3);
            if (u > 0f) Gfx.RoundRect(this, new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(u, 0.05f, 1f), bar.Size.Y)), col.WithAlpha(0.8f), 3);
            if (urgent == k) Gfx.Text(this, Fonts.Body, new Vector2(bar.End.X + 8f, y + 15f), UiKit.Fit("가장 급함 — 드론 · 채집 장치가 겨눈다", x1 - bar.End.X - 8f, Ui.TextSmall), Ui.TextSmall, col);
            y += rowH;
        }
        var s = mats.Stats;
        string back = $"되살림: 고철 → 금속 {w.Scrap.Recovered} · 찌꺼기 → 탄소 {s.CarbonFromWaste:0} · 숨 → 탄소 {s.CarbonFromAir:0} · 헌 부품 {s.Stripped} · 컴퓨터가 정제 {s.AutoRefined}";
        string aim = $"겨눠 캠 {s.Aimed} · 체 바꿔 거름 {s.Sieved} · 바위 끌어옴 {s.Tows} · 천천히 지남 {s.Lingers} · 증설 {s.CollectorsBuilt + s.ReclaimersBuilt}";
        Gfx.Text(this, Fonts.Body, new Vector2(x0, y + 16f), UiKit.Fit(back, wdt, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
        Gfx.Text(this, Fonts.Body, new Vector2(x0, y + 34f), UiKit.Fit(aim, wdt, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted);
    }
}
