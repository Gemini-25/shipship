using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.9 수집 도감 (⇧I): 사진 · 표본 · 유물 · 사람들 — 찾은 것은 그림 · 이름 · 누가 언제, 못 본 것은 흐린 실루엣.

public partial class Hud
{
    public bool CollectionOpen { get; private set; }
    private CodexShelf _shelf = CodexShelf.Photo;

    public void ToggleCollection()
    {
        CollectionOpen = !CollectionOpen;
        if (CollectionOpen) { ChronicleOpen = false; TechOpen = false; ControlOpen = false; PolicyOpen = false; OpenChain(null); if (ScaleCodexOpen) ToggleScaleCodex(); }
    }

    private void DrawCollection(Vector2 mouse)
    {
        var cu = _world.Curios;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f;
        float w = Mathf.Min(860f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogHeight - Margin - 20f;
        var card = new Rect2(x0, y0, w, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        int total = CurioTable.All.Length, seen = CurioTable.All.Count(s => cu.Seen.Contains(s.Key));
        UiKit.CardTitle(this, x, right - 90, y0 + 30, "도감 — 모은 것", $"{seen} / {total}", "star");
        Button(new Rect2(right - 76, y0 + 12, 76, 26), "⇧I 닫기", false, mouse, ToggleCollection, Ui.TextSmall);
        // 칸 고르기
        float tx = x, ty = y0 + 50;
        foreach (CodexShelf s in Enum.GetValues<CodexShelf>())
        {
            int all = CurioTable.All.Count(c => c.Shelf == s), got = CurioTable.All.Count(c => c.Shelf == s && cu.Seen.Contains(c.Key));
            string label = $"{CurioTable.ShelfName(s)} {got}/{all}";
            float bw = Gfx.Width(Fonts.Bold, label, Ui.TextSmall) + 22;
            var shelf = s;
            Button(new Rect2(tx, ty, bw, 26), label, _shelf == s, mouse, () => _shelf = shelf, Ui.TextSmall);
            tx += bw + 6;
        }
        Divider(x, right, ty + 34);
        // 그림 격자
        var items = CurioTable.All.Where(s => s.Shelf == _shelf).ToList();
        const float cw = 132f, ch = 132f;
        int cols = Math.Max(1, (int)((right - x + 8) / (cw + 8)));
        float gy = ty + 44;
        for (int i = 0; i < items.Count; i++)
        {
            var spec = items[i];
            var r = new Rect2(x + (i % cols) * (cw + 8), gy + (i / cols) * (ch + 8), cw, ch);
            if (r.End.Y > card.End.Y - 30) break;
            bool got = cu.Seen.Contains(spec.Key);
            var tone = got ? new Color(spec.Color) : Palette.TextMuted;
            var frame = new Rect2(r.Position + new Vector2(10, 6), new Vector2(cw - 20, ch - 46));
            CurioArt.Frame(this, spec.Shelf, frame, tone);
            CurioArt.Draw(this, spec, frame.GetCenter(), Mathf.Min(frame.Size.X, frame.Size.Y) * 0.36f, got, _time);
            if (!got) Gfx.TextCentered(this, Fonts.Bold, frame.GetCenter(), "?", Ui.TextHeading, new Color(1, 1, 1, 0.18f));
            string name = got ? spec.Name : "아직 못 본 것";
            Gfx.TextCentered(this, Fonts.Bold, new Vector2(r.GetCenter().X, r.End.Y - 30), Fit(name, cw - 8, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, got ? Palette.Text : Palette.TextMuted);
            string kind = CurioTable.KindName(spec.Kind);
            Gfx.TextCentered(this, Fonts.Body, new Vector2(r.GetCenter().X, r.End.Y - 14), kind, Ui.TextTiny, Palette.TextMuted);
            if (!r.HasPoint(mouse)) continue;
            Gfx.RoundRect(this, r, new Color(1, 1, 1, 0.03f), 8, tone.WithAlpha(0.4f));
            var at = new Vector2(r.Position.X, r.End.Y + 4);
            var lines = new List<TipLine>();
            if (got)
            {
                foreach (var f in cu.Finds.Where(f => f.Key == spec.Key).Take(3))
                {
                    var who = _world.Crew.FirstOrDefault(c => c.Id == f.By);
                    lines.Add(new TipLine($"{SimTime.Day(f.Tick)}일 {SimTime.Clock(f.Tick)} · {(who != null ? who.Name : "주 컴퓨터")}{(f.Room >= 0 && f.Room < _world.Ship.Rooms.Count ? $" · {_world.Ship.Rooms[f.Room].Name}" : "")}", Palette.TextDim));
                    lines.Add(new TipLine(f.Note, Palette.Text));
                }
            }
            else lines.Add(new TipLine(spec.Kind switch
            {
                CurioKind.Passing => "창밖을 자주 보면 언젠가 지나간다",
                CurioKind.Anomaly => "어떤 방이 어떤 상태일 때만 일어난다",
                CurioKind.Talent => "누군가 숨겨 둔 솜씨 — 맞는 때에 드러난다",
                CurioKind.Secret => "누군가의 비밀 — 가까운 사람이 먼저 안다",
                _ => "배 어딘가에 있다 — 치우고 고치다 보면 나온다",
            }, Palette.TextDim));
            var title = got ? spec.Name : "?";
            _tip = () => UiKit.Tooltip(this, at, Screen, title, lines);
        }
        // 지금 벌어지는 드문 일
        var now = cu.Active.Select(e => CurioTable.Of(e.Key)).Select(s => s.Name).ToList();
        string foot = now.Count > 0 ? $"지금 — {string.Join(" · ", now)}" : $"찾은 물건 {cu.Placed.Count(p => p.Found)} · 숨어 있는 것 {cu.Placed.Count(p => !p.Found)} · 창밖 {cu.Stats.Passings}번 · 이상한 일 {cu.Stats.Anomalies}번";
        Gfx.Text(this, Fonts.Body, new Vector2(x, card.End.Y - 12), Fit(foot, right - x, Ui.TextSmall, Fonts.Body), Ui.TextSmall, now.Count > 0 ? Palette.Accent : Palette.TextMuted);
    }
}
