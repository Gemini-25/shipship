using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.8 항로 막대(지도 위): 구간을 색으로, 배의 자리, 다음 구간까지 · 목적지 · 시대와 지금 연구.
// 요약 진행 카드: 빨리 감는 동안 무엇이 있었는지 한 장으로.
public partial class Hud
{
    public static Color LegColor(LegKind k) => k switch
    {
        LegKind.AsteroidBelt => new Color("#c9a66b"), LegKind.Nebula => new Color("#b58cff"), LegKind.RadiationBelt => new Color("#f5d547"),
        LegKind.Derelict => new Color("#8d93a6"), LegKind.Port => new Color("#6ee7b7"), _ => new Color("#3d4b63"),
    };

    private void DrawVoyageBar(Rect2 minimap)
    {
        var v = _world.Voyage;
        float total = v.TotalDays;
        if (total <= 0f) return;
        var bar = new Rect2(minimap.Position.X, minimap.Position.Y - 46f, minimap.Size.X, 40f);
        Gfx.RoundRect(this, bar, new Color(0.04f, 0.05f, 0.08f, 0.88f), 8f, new Color(1, 1, 1, 0.08f));
        float x = bar.Position.X + 10f, w = bar.Size.X - 20f, y = bar.Position.Y + 22f;
        float acc = 0f;
        for (int i = 0; i < v.Legs.Count; i++)
        {
            var leg = v.Legs[i];
            float x0 = x + w * acc / total, x1 = x + w * (acc + leg.Days) / total;
            var col = LegColor(leg.Kind).WithAlpha(i < v.Index ? 0.35f : 0.9f);
            DrawRect(new Rect2(x0 + 0.5f, y - 3f, MathF.Max(1f, x1 - x0 - 1f), 6f), col);
            if (leg.Kind is LegKind.Port or LegKind.Derelict) DrawCircle(new Vector2((x0 + x1) / 2f, y), 4f, col);
            acc += leg.Days;
        }
        float px = x + w * v.DoneDays / total;
        DrawCircle(new Vector2(px, y), 5f, v.Drifting ? Palette.Danger : Colors.White);
        DrawLine(new Vector2(px, y - 9f), new Vector2(px, y + 9f), Colors.White.WithAlpha(0.6f), 1f);
        var cur = v.Current;
        float left = cur.Days - v.Progress;
        var next = v.Index + 1 < v.Legs.Count ? v.Legs[v.Index + 1] : null;
        string head = $"{v.Number}번째 항해 · {v.Origin} → {v.Destination} · 지금 {VoyageSystem.KindName(cur.Kind)}" + (cur.Kind != LegKind.Cruise ? $"({cur.Name})" : "")
                      + (next != null ? $" · {left:0.0}일 뒤 {VoyageSystem.KindName(next.Kind)}" : "") + (v.Drifting ? " · 표류 중" : "");
        Gfx.Text(this, Fonts.Body, new Vector2(bar.Position.X + 10f, bar.Position.Y + 13f), head, 11, v.Drifting ? Palette.Danger : Palette.TextDim);
        var e = _world.Eras;
        string era = $"{EraSystem.EraName(e.Era)} · " + (e.Project != null ? $"연구 {EraSystem.All.First(t => t.Id == e.Project).Name} {e.Progress / EraSystem.All.First(t => t.Id == e.Project).Cost * 100:0}%" : "연구할 것 없음");
        Gfx.TextRight(this, Fonts.Body, new Vector2(bar.End.X - 10f, bar.Position.Y + 36f), era, 10, Palette.TextMuted);
    }

    // ── 요약 진행 ──
    public string[]? SummaryLines { get; set; }
    public long SummaryShownAt { get; set; } = -1;

    private void DrawSummaryCard(Vector2 mouse)
    {
        if (SummaryLines == null) return;
        float w = 460f, h = 60f + SummaryLines.Length * 22f;
        var card = new Rect2((Screen.X - w) / 2f, Screen.Y * 0.22f, w, h);
        Card(card);
        Gfx.Text(this, Fonts.Bold, card.Position + new Vector2(20, 32), "요약 진행", 17, Palette.Text);
        Button(new Rect2(card.End.X - 74, card.Position.Y + 12, 58, 26), "닫기", false, mouse, () => SummaryLines = null, 11);
        float y = card.Position.Y + 58;
        foreach (var line in SummaryLines)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 20, y + 10), line, 13, line.StartsWith("멈춤") ? Palette.Warning : Palette.TextDim);
            y += 22f;
        }
    }
}

public partial class Hud
{
    /// <summary>v12.8 시대 기술 카드: 여섯 시대 — 익힌 것 · 연구 중 · 고를 수 있는 것 · 아직 잠긴 시대. 효과와 위험을 함께.</summary>
    private void DrawEraCard(Rect2 techCard)
    {
        var e = _world.Eras;
        float x0 = techCard.End.X + 10f, w = MathF.Min(380f, Screen.X - RightColumnWidth - Margin * 2 - x0);
        if (w < 240f) return;
        float rows = EraSystem.All.Length + EraSystem.Eras.Length;
        float h = MathF.Min(techCard.Size.Y, 70f + rows * 19f);
        var card = new Rect2(x0, techCard.Position.Y, w, h);
        Card(card);
        float x = x0 + 14f, right = card.End.X - 14f, y = card.Position.Y + 28f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), $"시대 기술 — {EraSystem.EraName(e.Era)}", 15, Palette.Text);
        y += 18f;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), e.Project != null ? $"연구 중: {EraSystem.All.First(t => t.Id == e.Project).Name} ({e.ProjectWhy})" : "고를 연구가 없다", 11, Palette.TextDim);
        y += 8f;
        foreach (var (era, name, need) in EraSystem.Eras)
        {
            y += 19f;
            if (y > card.End.Y - 10f) break;
            bool open = _world.Research >= need;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y), $"{era}. {name}" + (open ? "" : $" — 연구 {need:0}점에 열린다"), 12, open ? Palette.Accent : Palette.TextMuted);
            foreach (var t in EraSystem.All.Where(t => t.Era == era))
            {
                y += 19f;
                if (y > card.End.Y - 10f) break;
                bool known = e.Known.Contains(t.Id), cur = e.Project == t.Id;
                string mark = known ? "✓" : cur ? "▶" : open ? "·" : " ";
                var col = known ? Palette.Good : cur ? Palette.Warning : open ? Palette.TextDim : Palette.TextMuted.WithAlpha(0.6f);
                Gfx.Text(this, Fonts.Body, new Vector2(x + 8, y), $"{mark} {t.Name}", 12, col);
                string note = cur ? $"{e.Progress / t.Cost * 100:0}% · {t.Effect}" : t.Effect + (t.Risk != "없음" ? $" · 위험: {t.Risk}" : "");
                while (note.Length > 6 && Gfx.Width(Fonts.Body, note, 10) > right - x - 130f) note = note[..^2] + "…";
                Gfx.Text(this, Fonts.Body, new Vector2(x + 128, y), note, 10, known && t.Risk != "없음" ? Palette.Warning.WithAlpha(0.8f) : Palette.TextMuted);
            }
        }
    }
}
