using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.6 숫자마다 "왜 이 값" — 누르면 그 값을 이루는 몫이 붙박이 쪽지로 남는다 (다시 누르면 닫힘). 식은 Core/UiWhy.

public partial class Hud
{
    private string? _whyPinned;

    /// <summary>값 자리에 건다: 누르면 고정 · hoverTip이면 올려 둘 때도 보인다.</summary>
    private void WhyHook(Rect2 hit, string key, string title, Func<List<TipLine>> lines, Vector2 mouse, bool hoverTip = true)
    {
        bool hover = hit.HasPoint(mouse);
        bool pinned = _whyPinned == key;
        if (pinned) Gfx.RoundRect(this, hit.Grow(2), new Color(1, 1, 1, 0.04f), 5, Palette.Accent.WithAlpha(0.4f));
        else if (hover && hoverTip) Gfx.RoundRect(this, hit.Grow(2), new Color(1, 1, 1, 0.03f), 5);
        _buttons.Add((hit, () => _whyPinned = _whyPinned == key ? null : key));
        if (!pinned && !(hover && hoverTip)) return;
        var at = new Vector2(hit.Position.X, hit.End.Y + 6);
        var ls = lines();
        if (ls.Count == 0) ls.Add(new TipLine("깎이는 것 없음", Palette.TextDim));
        if (hover || _tip == null) _tip = () => UiKit.Tooltip(this, at, Screen, title, ls);
    }

    private static List<TipLine> WhyLines(IReadOnlyList<WhyTerm> terms) =>
        terms.Where(t => t.Shown).Select(t => new TipLine($"{t.Name} {t.Pct}" + (t.Note.Length > 0 ? $" · {t.Note}" : ""), t.Factor >= 1f ? Palette.Good : t.Factor < 0.7f ? Palette.Danger : Palette.Warning)).ToList();

    private void WhyMachine(Machine m, Rect2 hit, Vector2 mouse) =>
        WhyHook(hit, $"m:{m.Body.Id}", $"효율 {m.Efficiency * 100:0}% — 무엇이 깎았나", () => WhyLines(UiWhy.Efficiency(m)), mouse);

    private void WhyChip(string label, Rect2 hit, Vector2 mouse) =>
        WhyHook(hit, $"chip:{label}", $"{label} — 까닭", () => UiWhy.Chip(_world, label).Select(l => new TipLine(l, Palette.TextDim)).ToList(), mouse, hoverTip: false);
}
