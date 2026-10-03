using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.6 승무원 목록 2차 — 얼굴 아이콘(표정) · 상태 아이콘 · 문제 있는 사람이 위로 · 역할별 접기 (30인 배에서도 한눈에).

public partial class Hud
{
    /// <summary>사람이 직접 펼치거나 접은 역할 (나머지는 자리에 맞춰 저절로).</summary>
    private readonly Dictionary<CrewRole, bool> _roleFold = new();

    private float DrawCrewList(Vector2 mouse)
    {
        var order = UiCrewList.Order(_world);
        var trouble = order.Where(x => x.t.Any).ToList();
        var groups = UiCrewList.Groups(_world, order);
        int n = _world.Crew.Count;
        bool two = n > 12;
        float rowH = two ? 20f : n <= 8 ? 30f : 22f, headH = n <= 12 ? 18f : 20f, tRowH = 30f;
        float maxH = Mathf.Max(220f, Screen.Y * 0.52f);
        int tShow = Math.Min(trouble.Count, 6);
        float GroupH(List<CrewMember> l, bool folded) => headH + (folded ? 0f : (two ? (l.Count + 1) / 2 : l.Count) * rowH);
        // 저절로 접기: 넘치면 큰 묶음부터 (사람이 펼친 묶음은 그대로)
        var fold = groups.ToDictionary(g => g.role, g => _roleFold.TryGetValue(g.role, out var f) && f);
        float Total() => 40f + (tShow > 0 ? 18f + tShow * tRowH + (trouble.Count > tShow ? 16f : 0f) + 6f : 0f) + groups.Sum(g => GroupH(g.crew, fold[g.role])) + 8f;
        foreach (var g in groups.OrderByDescending(g => g.crew.Count))
        {
            if (Total() <= maxH) break;
            if (!_roleFold.ContainsKey(g.role)) fold[g.role] = true;
        }
        float x0 = Screen.X - Margin - RightColumnWidth;
        var card = new Rect2(x0, Margin, RightColumnWidth, Total());
        Card(card);
        SectionTitle(x0 + 18, card.Position.Y + 24, "승무원");
        int alive = _world.Crew.Count(c => !c.Dead);
        string head = trouble.Count > 0 ? $"{alive}/{n}명 · 살펴볼 {trouble.Count}" : $"{alive}/{n}명";
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 18, card.Position.Y + 24), head, Ui.TextSmall, trouble.Count > 0 ? Palette.Warning : Palette.TextMuted);
        float y = card.Position.Y + 36;
        // ① 살펴볼 사람
        if (tShow > 0)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x0 + 14, y + 12), "살펴볼 사람", Ui.TextTiny, Palette.Warning);
            y += 18;
            for (int i = 0; i < tShow; i++)
            {
                var (c, t) = trouble[i];
                var row = new Rect2(x0 + 8, y, RightColumnWidth - 16, tRowH - 3);
                RosterRowBack(row, c, mouse, true);
                float cy = row.GetCenter().Y;
                DrawFace(new Vector2(row.Position.X + 15, cy), 10f, c);
                float pulse = 0.65f + 0.35f * Mathf.Sin(_time * 4f + i);
                var tc = t.Score >= 5f ? Palette.Danger : Palette.Warning;
                Icons.Draw(this, t.Icon, new Vector2(row.Position.X + 38, cy), 14, tc.WithAlpha(pulse));
                Gfx.Text(this, Fonts.Bold, new Vector2(row.Position.X + 52, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), c.Name, Ui.TextBody, Palette.Text);
                float nw = Gfx.Width(Fonts.Bold, c.Name, Ui.TextBody);
                float room = row.End.X - 10 - (row.Position.X + 58 + nw);
                Gfx.TextRight(this, Fonts.Body, new Vector2(row.End.X - 10, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), Fit(t.Why, room, Ui.TextSmall, Fonts.Body), Ui.TextSmall, tc);
                RosterClick(row, c);
                y += tRowH;
            }
            if (trouble.Count > tShow)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x0 + 18, y + 11), $"그 밖에 {trouble.Count - tShow}명 — {string.Join(" · ", trouble.Skip(tShow).Take(4).Select(x => x.c.Name))}", Ui.TextTiny, Palette.Warning);
                y += 16;
            }
            Divider(x0 + 14, card.End.X - 14, y + 3);
            y += 6;
        }
        // ② 역할별 (접기)
        float colW = (RightColumnWidth - 20f) / 2f;
        foreach (var (role, crew) in groups)
        {
            bool folded = fold[role];
            var hr = new Rect2(x0 + 8, y, RightColumnWidth - 16, headH - 2);
            bool hh = hr.HasPoint(mouse);
            if (hh) Gfx.RoundRect(this, hr, Ui.HoverSoft, 5);
            var rc = RoleColor(role);
            DrawRect(new Rect2(hr.Position.X + 2, hr.Position.Y + 4, 2, hr.Size.Y - 8), rc.WithAlpha(0.8f));
            string label = $"{CrewRoles.Name(role)} {crew.Count}";
            Gfx.Text(this, Fonts.Bold, new Vector2(hr.Position.X + 10, hr.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextTiny)), label, Ui.TextTiny, rc);
            Gfx.TextRight(this, Fonts.Body, new Vector2(hr.End.X - 6, hr.GetCenter().Y + Gfx.CenterOffset(Fonts.Body, Ui.TextTiny)), folded ? "▸" : "▾", Ui.TextTiny, Palette.TextMuted);
            if (folded)
            {
                // 접어도 얼굴은 보인다 (표정 · 표식)
                float fx = hr.Position.X + 10 + Gfx.Width(Fonts.Bold, label, Ui.TextTiny) + 14;
                foreach (var c in crew)
                {
                    if (fx > hr.End.X - 24) break;
                    DrawFace(new Vector2(fx, hr.GetCenter().Y), 6.5f, c);
                    var cell = new Rect2(fx - 7, hr.Position.Y, 14, hr.Size.Y);
                    if (cell.HasPoint(mouse)) { var who = c; var at = new Vector2(fx, hr.End.Y + 4); _tip = () => UiKit.Tooltip(this, at, Screen, who.Name, new[] { new TipLine($"{CrewRoles.Name(who.Role)} · {who.ActivityLabel}", Palette.TextDim) }); }
                    RosterClick(cell, c);
                    fx += 15;
                }
            }
            var role0 = role;
            _buttons.Add((new Rect2(hr.Position, new Vector2(folded ? 16 + Gfx.Width(Fonts.Bold, label, Ui.TextTiny) : hr.Size.X, hr.Size.Y)), () => _roleFold[role0] = !folded));
            if (folded) { _buttons.Add((new Rect2(hr.End.X - 22, hr.Position.Y, 22, hr.Size.Y), () => _roleFold[role0] = false)); y += headH; continue; }
            y += headH;
            for (int i = 0; i < crew.Count; i++)
            {
                var c = crew[i];
                var row = two ? new Rect2(x0 + 8 + (i % 2) * (colW + 4), y + (i / 2) * rowH, colW, rowH - 2) : new Rect2(x0 + 8, y + i * rowH, RightColumnWidth - 16, rowH - 3);
                RosterRowBack(row, c, mouse, false);
                float cy = row.GetCenter().Y;
                float fr = two ? 7f : rowH >= 30f ? 10f : 8f;
                DrawFace(new Vector2(row.Position.X + fr + 4, cy), fr, c);
                int ns = two ? Ui.TextSmall : Ui.TextSubtitle;
                float nx = row.Position.X + fr * 2 + 10;
                Gfx.Text(this, Fonts.Bold, new Vector2(nx, cy + Gfx.CenterOffset(Fonts.Bold, ns)), c.Name, ns, c.Dead ? Palette.TextMuted : Palette.Text);
                float nw = Gfx.Width(Fonts.Bold, c.Name, ns);
                string state = c.Dead ? "사망" : c.ActivityLabel;
                var sc = c.Dead ? Palette.TextMuted : Palette.Crew(c.Id).Lightened(0.2f);
                int ss = two ? Ui.TextTiny : Ui.TextBody;
                float room = row.End.X - 6 - (nx + nw + 22);
                if (!two && c.Room != null && room > 150)
                {
                    Gfx.TextRight(this, Fonts.Body, new Vector2(row.End.X - 8, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), c.Room.Name, Ui.TextSmall, Palette.TextMuted);
                    room -= Gfx.Width(Fonts.Body, c.Room.Name, Ui.TextSmall) + 8;
                    state = Fit(state, room, ss, Fonts.Bold);
                    Gfx.TextRight(this, Fonts.Bold, new Vector2(row.End.X - 16 - Gfx.Width(Fonts.Body, c.Room.Name, Ui.TextSmall), cy + Gfx.CenterOffset(Fonts.Bold, ss)), state, ss, sc);
                }
                else
                {
                    state = Fit(state, room, ss, Fonts.Body);
                    Gfx.TextRight(this, Fonts.Body, new Vector2(row.End.X - 6, cy + Gfx.CenterOffset(Fonts.Body, ss)), state, ss, sc);
                }
                Icons.Draw(this, Icons.CrewState(c, _world.Tick), new Vector2(nx + nw + 10, cy), two ? 10 : 12, sc);
                RosterClick(row, c);
            }
            y += (two ? (crew.Count + 1) / 2 : crew.Count) * rowH;
        }
        return card.End.Y;
    }

    private void RosterRowBack(Rect2 row, CrewMember c, Vector2 mouse, bool trouble)
    {
        var col = Palette.Crew(c.Id);
        if (_main.SelectedCrew == c) Gfx.RoundRect(this, row, col.WithAlpha(0.12f), 6, col.WithAlpha(0.3f));
        else if (row.HasPoint(mouse)) Gfx.RoundRect(this, row, Ui.HoverSoft, 6);
        else if (trouble) Gfx.RoundRect(this, row, Palette.Warning.WithAlpha(0.05f), 6);
    }

    private void RosterClick(Rect2 r, CrewMember c)
    {
        var target = c;
        _buttons.Add((r, () => _main.Select(_main.SelectedCrew == target ? null : target)));
    }

    private static readonly Color[] SkinTones = { new("#f1d0b0"), new("#d9a77c"), new("#b67d55"), new("#8a5a3c"), new("#e8bf96"), new("#c58c63") };

    /// <summary>얼굴 아이콘: 피부 · 머리 모양(사람마다) · 표정(지금 마음) · 둘레 색(그 사람 색) · 색약 팔레트면 표식.</summary>
    private void DrawFace(Vector2 c, float r, CrewMember cm)
    {
        var col = Palette.Crew(cm.Id);
        var look = ZoomDetail.Face(_world, cm);
        var skin = SkinTones[(cm.Id * 7 + 3) % SkinTones.Length];
        if (cm.Dead) skin = skin.Darkened(0.5f).Lerp(new Color("#5f6879"), 0.6f);
        DrawCircle(c, r + 1.6f, col.WithAlpha(cm.Dead ? 0.35f : 0.9f), true, -1f, true);
        DrawCircle(c, r, skin, true, -1f, true);
        // 머리 (네 가지 모양)
        var hair = new Color("#2b2320").Lerp(col, 0.25f);
        switch (cm.Id % 4)
        {
            case 0: DrawArc(c, r * 0.82f, Mathf.Pi * 1.05f, Mathf.Pi * 1.95f, 10, hair, r * 0.38f, true); break;
            case 1: DrawColoredPolygon(new[] { c + new Vector2(-r, -r * 0.1f), c + new Vector2(-r * 0.6f, -r * 0.95f), c + new Vector2(r * 0.7f, -r * 0.9f), c + new Vector2(r * 0.2f, -r * 0.45f) }, hair); break;
            case 2: DrawArc(c, r * 0.85f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 10, hair, r * 0.3f, true); DrawCircle(c + new Vector2(0, -r * 1.05f), r * 0.32f, hair, true, -1f, true); break;
            default: DrawArc(c, r * 0.9f, Mathf.Pi * 0.95f, Mathf.Pi * 2.05f, 12, hair, r * 0.22f, true); break;
        }
        var ink = new Color("#1b1a22");
        float ex = r * 0.36f, ey = -r * 0.05f, lw = Mathf.Max(1f, r * 0.13f);
        Vector2 L = c + new Vector2(-ex, ey), R2 = c + new Vector2(ex, ey), M = c + new Vector2(0, r * 0.42f);
        switch (look)
        {
            case FaceLook.Asleep:
                DrawArc(L, r * 0.16f, 0f, Mathf.Pi, 5, ink, lw, true); DrawArc(R2, r * 0.16f, 0f, Mathf.Pi, 5, ink, lw, true);
                DrawLine(M + new Vector2(-r * 0.12f, 0), M + new Vector2(r * 0.12f, 0), ink, lw, true); break;
            case FaceLook.Pain:
                DrawPolyline(new[] { L + new Vector2(-r * 0.15f, -r * 0.1f), L + new Vector2(r * 0.1f, 0), L + new Vector2(-r * 0.15f, r * 0.1f) }, ink, lw, true);
                DrawPolyline(new[] { R2 + new Vector2(r * 0.15f, -r * 0.1f), R2 + new Vector2(-r * 0.1f, 0), R2 + new Vector2(r * 0.15f, r * 0.1f) }, ink, lw, true);
                DrawPolyline(new[] { M + new Vector2(-r * 0.25f, 0), M + new Vector2(-r * 0.08f, -r * 0.08f), M + new Vector2(r * 0.08f, r * 0.04f), M + new Vector2(r * 0.25f, -r * 0.04f) }, ink, lw, true); break;
            case FaceLook.Fear:
                DrawCircle(L, r * 0.13f, ink, true, -1f, true); DrawCircle(R2, r * 0.13f, ink, true, -1f, true);
                DrawLine(L + new Vector2(-r * 0.15f, -r * 0.3f), L + new Vector2(r * 0.12f, -r * 0.4f), ink, lw, true); DrawLine(R2 + new Vector2(r * 0.15f, -r * 0.3f), R2 + new Vector2(-r * 0.12f, -r * 0.4f), ink, lw, true);
                DrawArc(M, r * 0.14f, 0f, Mathf.Tau, 8, ink, lw, true); break;
            case FaceLook.Angry:
                DrawCircle(L, r * 0.1f, ink, true, -1f, true); DrawCircle(R2, r * 0.1f, ink, true, -1f, true);
                DrawLine(L + new Vector2(-r * 0.18f, -r * 0.32f), L + new Vector2(r * 0.15f, -r * 0.18f), ink, lw, true); DrawLine(R2 + new Vector2(r * 0.18f, -r * 0.32f), R2 + new Vector2(-r * 0.15f, -r * 0.18f), ink, lw, true);
                DrawLine(M + new Vector2(-r * 0.22f, 0), M + new Vector2(r * 0.22f, 0), ink, lw, true); break;
            case FaceLook.Sad:
                DrawCircle(L, r * 0.1f, ink, true, -1f, true); DrawCircle(R2, r * 0.1f, ink, true, -1f, true);
                DrawArc(M + new Vector2(0, r * 0.2f), r * 0.24f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 6, ink, lw, true);
                DrawCircle(R2 + new Vector2(r * 0.05f, r * 0.25f), r * 0.07f, new Color("#7cc4ff"), true, -1f, true); break;
            case FaceLook.Tired:
                DrawLine(L + new Vector2(-r * 0.14f, 0), L + new Vector2(r * 0.14f, 0), ink, lw, true); DrawLine(R2 + new Vector2(-r * 0.14f, 0), R2 + new Vector2(r * 0.14f, 0), ink, lw, true);
                DrawLine(M + new Vector2(-r * 0.15f, 0), M + new Vector2(r * 0.15f, r * 0.03f), ink, lw, true); break;
            case FaceLook.Worry:
                DrawCircle(L, r * 0.1f, ink, true, -1f, true); DrawCircle(R2, r * 0.1f, ink, true, -1f, true);
                DrawLine(L + new Vector2(-r * 0.15f, -r * 0.22f), L + new Vector2(r * 0.15f, -r * 0.32f), ink, lw, true); DrawLine(R2 + new Vector2(r * 0.15f, -r * 0.22f), R2 + new Vector2(-r * 0.15f, -r * 0.32f), ink, lw, true);
                DrawPolyline(new[] { M + new Vector2(-r * 0.18f, r * 0.02f), M + new Vector2(0, -r * 0.05f), M + new Vector2(r * 0.18f, r * 0.02f) }, ink, lw, true); break;
            case FaceLook.Smile:
                DrawCircle(L, r * 0.1f, ink, true, -1f, true); DrawCircle(R2, r * 0.1f, ink, true, -1f, true);
                DrawArc(M + new Vector2(0, -r * 0.18f), r * 0.26f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 6, ink, lw, true); break;
            default:
                DrawCircle(L, r * 0.1f, ink, true, -1f, true); DrawCircle(R2, r * 0.1f, ink, true, -1f, true);
                DrawLine(M + new Vector2(-r * 0.16f, 0), M + new Vector2(r * 0.16f, 0), ink, lw, true); break;
        }
        if (ColorSafe.On && r >= 6f) ColorSafe.DrawMark(this, cm.Id, c + new Vector2(r * 0.85f, r * 0.75f), Mathf.Max(3f, r * 0.42f), col, Palette.Panel);
    }
}
