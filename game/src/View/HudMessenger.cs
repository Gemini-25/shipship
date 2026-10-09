using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v17.3 선내 메신저 패널 (어두운 유리): 사람마다 다른 말투의 짧은 글 · 사진 · 주 컴퓨터 알림 · 읽은 사람 수.
/// 승무원을 고르면 그 사람 단말로 본다 — 아직 안 읽은 글에 점이 찍힌다 (안 읽은 사람은 모른다).
/// </summary>
public partial class Hud
{
    public bool MessengerOpen { get; set; }
    private static readonly Color ChatGlass = new(0.05f, 0.07f, 0.1f, 0.82f);
    private static readonly Color ChatMine = new(0.16f, 0.24f, 0.32f, 0.9f);
    private static readonly Color ChatOther = new(0.12f, 0.14f, 0.18f, 0.9f);
    private static readonly Color ChatComputer = new(0.1f, 0.2f, 0.3f, 0.92f);

    private void DrawMessenger(Vector2 bottomLeft, Vector2 mouse)
    {
        var w = _world;
        var chat = w.Info.Chat;
        var sel = _main.SelectedCrew;
        const float width = 360f;
        var msgs = chat.All.TakeLast(11).ToList();
        float H(ChatMsg m) => 34f + (m.Photo >= 0 ? 40f : 0f);
        float height = 46f + Math.Max(1, msgs.Count) * 0f + msgs.Sum(H) + (msgs.Count == 0 ? 30f : 0f);
        height = Math.Min(height, 620f);
        var card = new Rect2(bottomLeft.X, bottomLeft.Y - height, width, height);
        Gfx.RoundRect(this, card, ChatGlass, 10, new Color(1, 1, 1, 0.07f));
        string head = sel != null ? $"선내 메신저 — {sel.Name}의 단말" : "선내 메신저";
        Gfx.Text(this, Fonts.Bold, card.Position + new Vector2(14, 22), head, 12, Palette.Text);
        if (sel != null)
        {
            string state = chat.CannotRead(sel) is string why ? why : $"안 읽음 {chat.Unread(sel)}";
            Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 38, card.Position.Y + 22), state, 10, Palette.TextMuted);
        }
        Button(new Rect2(card.End.X - 30, card.Position.Y + 8, 22, 20), "×", false, mouse, () => MessengerOpen = false, 11);
        float y = card.Position.Y + 36;
        if (msgs.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 14, y + 12), "조용하다", 11, Palette.TextMuted);
        // 넘치면 오래된 글부터 뺀다
        while (msgs.Count > 0 && y + msgs.Sum(H) > card.End.Y - 6) msgs.RemoveAt(0);
        foreach (var m in msgs)
        {
            var author = m.Author >= 0 && m.Author < w.Crew.Count ? w.Crew[m.Author] : null;
            bool mine = sel != null && m.Author == sel.Id;
            bool comp = author == null;
            string name = comp ? "주 컴퓨터" : author!.Name;
            var ncol = comp ? Palette.Accent : Palette.Crew(author!.Id);
            float bx = card.Position.X + (mine ? 52 : 12);
            float bw = width - 64;
            Gfx.Text(this, Fonts.Bold, new Vector2(bx + 2, y + 10), name, 10, ncol);
            Gfx.TextRight(this, Fonts.Body, new Vector2(bx + bw, y + 10), SimTime.Clock(m.Tick), 9, Palette.TextMuted);
            float bh = 18f + (m.Photo >= 0 ? 40f : 0f);
            var bubble = new Rect2(bx, y + 13, bw, bh);
            Gfx.RoundRect(this, bubble, comp ? ChatComputer : mine ? ChatMine : ChatOther, 7, comp ? Palette.Accent.WithAlpha(0.25f) : null);
            if (m.Photo >= 0 && w.Info.Photo(m.Photo) is PhotoInfo p)
            {
                var pr = new Rect2(bubble.Position.X + 6, bubble.Position.Y + 4, 48, 34);
                InfoArt.Photo(this, pr, p, w, _time, true);
                Gfx.Text(this, Fonts.Body, new Vector2(pr.End.X + 8, pr.Position.Y + 14), Fit(m.Text, bw - 70, 10, Fonts.Body), 10, Palette.Text);
                Gfx.Text(this, Fonts.Body, new Vector2(pr.End.X + 8, pr.Position.Y + 28), $"{p.People.Length}명", 9, Palette.TextMuted);
            }
            else Gfx.Text(this, Fonts.Body, new Vector2(bubble.Position.X + 8, bubble.Position.Y + 13), Fit(m.Text, bw - 46, 10, Fonts.Body), 10, comp ? new Color("#cfe8ff") : Palette.Text);
            // 읽은 사람 수 · 고른 사람이 아직 안 읽었으면 점
            int read = chat.ReadCount(m);
            Gfx.TextRight(this, Fonts.Body, new Vector2(bubble.End.X - 6, bubble.End.Y - 4), $"{read}", 8, Palette.TextMuted);
            if (sel != null && !mine && !chat.HasRead(sel, m.Id)) this.Circle(new Vector2(bubble.Position.X - 6, bubble.Position.Y + bubble.Size.Y * 0.5f), 2.6f, Palette.Accent);
            y += H(m);
        }
    }
}
