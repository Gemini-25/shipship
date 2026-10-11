using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 승무원 카드의 몸 상태: 그 사람의 도트 앞모습을 크게 (다친 곳이 그대로 보인다 — 붕대 · 깁스 · 의수 · 화상)
/// 위에 부위마다 점(가벼움 노랑 · 중간 주황 · 심함 빨강 · 잃음 회색 · 의수 쇠색), 옆에 상처 · 병 · 피폭 목록.
/// </summary>
public partial class Hud
{
    private static readonly Color WoundLight = new("#f2c94c"), WoundMid = new("#f2994a"), WoundBad = new("#eb5757"), WoundLost = new("#9aa0aa"), WoundMetal = new("#7fb2c9");

    private static Color WoundColor(Vitals v, Wound x) =>
        x.Lost ? (x.Prosthetic ? WoundMetal : WoundLost) : Wounds.Severity(v, x) is var s && s >= 0.55f ? WoundBad : s >= 0.3f ? WoundMid : WoundLight;

    /// <summary>몸 상태 줄 (부위 · 무엇 · 얼마나).</summary>
    private List<(BodyPart? part, string text, Color color)> BodyLines(CrewMember c)
    {
        var lines = new List<(BodyPart?, string, Color)>();
        var v = c.Vitals;
        foreach (var x in v.Wounds)
        {
            string what = x.Lost ? (x.Prosthetic ? "잃음 · 의수/의족" : "잃음") : $"{Wounds.KindName(x.Kind)} {Mathf.RoundToInt(Wounds.Severity(v, x) * 100)}%";
            if (!x.Lost && Wounds.Severity(v, x) < 0.03f) continue;
            lines.Add((x.Part, $"{Wounds.PartName(x.Part)} · {what}", WoundColor(v, x)));
        }
        foreach (var a in c.Ailments)
        {
            var spec = AilmentSystem.Spec(a.Id);
            lines.Add((null, a.Diagnosed ? $"{spec.Name} · {spec.Symptom}" : $"원인 모를 {spec.Symptom}", Palette.Warning));
        }
        if (c.InfectedAt >= 0 && !c.Immune) lines.Add((null, "옮는 병을 앓는 중", WoundMid));
        if (c.Dose > 0.5f) lines.Add((null, $"쬔 방사선 {c.Dose:0.0}", c.Dose > 2f ? WoundBad : WoundLight));
        if (v.Scar > 0.05f && v.ScarCause is string sc) lines.Add((null, $"흉터 · {sc}", Palette.TextMuted));
        return lines;
    }

    /// <summary>몸 상태 칸 (그린 높이만큼 y를 내려 돌려준다).</summary>
    private float DrawBodyState(CrewMember c, float x, float right, float y, Func<float, bool> fits)
    {
        if (c.Dead) return y;
        var lines = BodyLines(c);
        int shown = Math.Min(lines.Count, 5);
        float h = Mathf.Max(84f, 22f + Mathf.Max(1, shown) * 15f);
        if (!fits(h)) return y;
        UiKit.Divider(this, x, right, y);
        UiKit.Header(this, x, right, y + 18, "몸 상태", lines.Count == 0 ? "다친 곳 없음" : $"{lines.Count}곳", "health", lines.Count > 0 ? Palette.Warning : null);
        y += 24;
        // 도트 앞모습 (2배 · 또렷하게) + 부위 점
        const float k = 2f;
        var tex = _main.ShipView.Portrait(c, out var look);
        var at = new Vector2(x + 2f, y - 4f);
        if (tex != null)
        {
            DrawRect(new Rect2(at - new Vector2(2, 2), DotPeople.FW * k + 4, DotPeople.FH * k - 10), new Color(0.05f, 0.06f, 0.09f, 0.6f));
            DrawTextureRect(tex, new Rect2(at, DotPeople.FW * k, DotPeople.FH * k), false);
            var anchors = DotPeople.Anchors(look);
            float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 4f);
            foreach (var w in c.Vitals.Wounds)
            {
                if (!w.Lost && Wounds.Severity(c.Vitals, w) < 0.03f) continue;
                var (ax, ay) = anchors[(int)w.Part];
                var p = at + new Vector2(ax + 0.5f, ay + 0.5f) * k;
                var col = WoundColor(c.Vitals, w);
                this.Circle(p, 4.5f, new Color(0, 0, 0, 0.55f), true, -1f, true);
                this.Circle(p, 3.2f, col.WithAlpha(w.Lost ? 0.9f : pulse), true, -1f, true);
            }
        }
        // 목록
        float lx = x + DotPeople.FW * k + 12f, ly = y;
        if (lines.Count == 0)
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 12), "다친 곳 · 앓는 곳 없음", Ui.TextSmall, Palette.TextMuted);
        for (int i = 0; i < shown; i++)
        {
            var (part, text, col) = lines[i];
            this.Circle(new Vector2(lx + 4, ly + 8), 3f, col, true, -1f, true);
            Gfx.Text(this, Fonts.Body, new Vector2(lx + 12, ly + 12), UiKit.Fit(text, right - lx - 12, Ui.TextSmall), Ui.TextSmall, part == null ? Palette.TextDim : col.Lightened(0.25f));
            ly += 15f;
        }
        if (lines.Count > shown) Gfx.Text(this, Fonts.Body, new Vector2(lx + 12, ly + 12), $"외 {lines.Count - shown}가지", Ui.TextTiny, Palette.TextMuted);
        return y + h - 24f;
    }
}
