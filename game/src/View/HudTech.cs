using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v10.7 기술 화면 (T): 연구, 설비별 단계(풀린 것 · 달린 것), 방 모듈.
/// 개조는 여전히 회의가 정한다 — 이 화면은 보여 줄 뿐이다 (관찰 시뮬레이터).
/// </summary>
public partial class Hud
{
    public bool TechOpen { get; set; }
    /// <summary>기술 탭 둘째 쪽 (설비 단계). 첫 쪽은 v23 기술표.</summary>
    public bool TechWebOpen { get; set; }

    public void ToggleTech()
    {
        TechOpen = !TechOpen;
        if (TechOpen) { ChronicleOpen = false; PolicyOpen = false; OpenChain(null); }
    }

    /// <summary>단계 색: I 강철 · II 청동 · III 은 · IV 보라 (배 화면의 설비 배지와 같다).</summary>
    public static Color TierColor(int tier) => tier switch
    {
        2 => new Color("#d9a35b"),
        3 => new Color("#cfe3f2"),
        4 => new Color("#b98cff"),
        _ => new Color("#7f8a99"),
    };

    private void DrawTech(Vector2 mouse)
    {
        if (!TechWebOpen) { DrawTechTree(mouse); return; } // v23 기술표가 첫 쪽 · 이 쪽(설비 단계)은 둘째
        var w = _world;
        var types = Tech.Types.Where(t => w.Ship.FurnitureOf(t).Any()).ToList();
        var modules = Modules.All.Where(s => w.Ship.RoomsOf(s.Room).Any()).ToList();
        float x0 = WinX, y0 = WinY; // v20 오른쪽 창
        float width = WinW;
        float avail = WinBottom - y0;
        float rowH = Mathf.Clamp((avail - 150f) / (types.Count + modules.Count + 1), 17f, 30f);
        float height = Mathf.Min(avail, 110f + types.Count * rowH + 34f + modules.Count * rowH + 30f);
        var card = new Rect2(x0, y0, width, height);
        _chronicleRect = card; // 휠·클릭이 월드로 새지 않게 (연대기와 같은 자리)
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;

        float perDay = Tech.ResearchPerHour(w) * 24f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y0 + 30), $"{w.Ship.Name} 기술", Ui.TextLarge, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + Gfx.Width(Fonts.Bold, $"{w.Ship.Name} 기술", Ui.TextLarge) + 10, y0 + 30),
            $"연구 {w.Research:0}점 · 하루 +{perDay:0.0} (작업대·솜씨 좋은 사람·정밀 가공기)", Ui.TextBody, Palette.TextMuted);
        Button(new Rect2(right - 58, y0 + 12, 58, 26), "닫기", false, mouse, ToggleTech, Ui.TextSmall);
        Button(new Rect2(right - 58 - 96, y0 + 12, 90, 26), "기술표", false, mouse, () => TechWebOpen = false, Ui.TextSmall); // v23
        Gfx.Text(this, Fonts.Body, new Vector2(x, y0 + 50),
            "기술을 익히면 설계가 생기고, 로봇 · 승무원이 재료로 한 대씩 고쳐 단다. 높은 단계는 출력이 크고 전기 · 닳음 · 고장 값을 조금 더 치른다.", Ui.TextSmall, Palette.TextDim);

        // v23 설치 공사: 지을 줄 아는 단계보다 낮은 설비 · 지금 하는 공사
        int behind = 0;
        foreach (var type in Tech.Types) { int u = Tech.Unlocked(w, type); foreach (var f in w.Ship.FurnitureOf(type)) if (f.Machine is Machine m && m.Tier < u) behind++; }
        int installing = w.Board.Open.Count(o => Evolution.TechInstall(o));
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, y0 + 84), behind == 0 ? "모든 설비가 아는 단계까지 지어져 있다" : $"설치 기다림 {behind}대 · 공사 중 {installing}", Ui.TextSmall, behind == 0 ? new Color("#8fd65a") : Palette.Accent);

        float y = y0 + 96;
        float nameW = 128f;
        float chipW = (right - x - nameW) / 4f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 4), "설비 단계", Ui.TextBody, Palette.TextMuted);
        y += 10;
        foreach (var type in types)
        {
            if (y + rowH > card.End.Y - 30) break;
            var all = w.Ship.FurnitureOf(type).Where(f => f.Machine != null).ToList();
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + rowH * 0.5f + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), $"{FurnitureTypes.Name(type)} ×{all.Count}", Ui.TextBody, Palette.Text);
            int unlocked = Tech.Unlocked(w, type);
            foreach (var t in Tech.Tiers(type))
            {
                var chip = new Rect2(x + nameW + (t.Tier - 1) * chipW, y + 2, chipW - 6, rowH - 4);
                int here = all.Count(f => f.Machine!.Tier == t.Tier);
                bool open = t.Tier <= unlocked;
                var col = TierColor(t.Tier);
                Gfx.RoundRect(this, chip, here > 0 ? col.WithAlpha(0.22f) : new Color(1, 1, 1, open ? 0.05f : 0.02f), 5,
                    here > 0 ? col.WithAlpha(0.85f) : open ? col.WithAlpha(0.35f) : new Color(1, 1, 1, 0.08f));
                string label = $"{Tech.Roman(t.Tier)} {t.Name}";
                int fs = rowH < 22f ? 10 : 11;
                string tail = here > 0 ? $" ×{here}" : open ? " · 설치 가능" : "";
                while (label.Length > 3 && Gfx.Width(Fonts.Body, label + tail, fs) > chip.Size.X - 10) label = label[..^1];
                Gfx.Text(this, here > 0 ? Fonts.Bold : Fonts.Body, new Vector2(chip.Position.X + 6, chip.GetCenter().Y + Gfx.CenterOffset(Fonts.Body, fs)),
                    label + tail, fs, here > 0 ? Palette.Text : open ? col.Lightened(0.1f) : Palette.TextMuted);
                if (chip.HasPoint(mouse)) _techHover = $"{t.Name} — {t.Note} · 출력 ×{t.Output:0.##} · 전기 ×{t.Power:0.##} · 마모 ×{t.Wear:0.##} · 고장 ×{t.Faults:0.##}"
                                                     + (t.Cost.Length > 0 ? " · 재료 " + string.Join(" + ", t.Cost.Select(c => $"{ItemKinds.Name(c.kind)} {c.count}")) : "");
            }
            y += rowH;
        }

        y += 8;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 14), "보조 장비", Ui.TextBody, Palette.TextMuted);
        y += 20;
        foreach (var spec in modules)
        {
            if (y + rowH > card.End.Y - 26) break;
            int have = w.Ship.FurnitureOf(spec.Type).Count();
            int working = Modules.Working(w, spec.Type);
            int rooms = w.Ship.RoomsOf(spec.Room).Count(r => !r.Abandoned);
            float cy = y + rowH * 0.5f;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), Modules.Name(spec.Type), Ui.TextBody, have > 0 ? Palette.Text : Palette.TextDim);
            for (int i = 0; i < spec.Max * rooms; i++)
            {
                var c = new Vector2(x + nameW + 8 + i * 16, cy);
                if (i < working) this.Circle(c, 5f, new Color("#8fd65a"), true, -1f, true);
                else if (i < have) this.Circle(c, 5f, Palette.Warning, true, -1f, true);
                else this.Arc(c, 5f, 0, Mathf.Tau, 16, new Color(1, 1, 1, 0.25f), 1f, true);
            }
            Gfx.Text(this, Fonts.Body, new Vector2(x + nameW + 16 + spec.Max * rooms * 16, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)),
                $"{spec.Note} · {string.Join(" + ", spec.Cost.Select(k => $"{ItemKinds.Name(k.kind)} {k.count}"))}", Ui.TextSmall, Palette.TextMuted);
            y += rowH;
        }

        if (_techHover != null)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, card.End.Y - 12), _techHover, Ui.TextSmall, Palette.Accent);
            _techHover = null;
        }
    }

    private string? _techHover;
}
