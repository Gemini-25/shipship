using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.14 기술 지도 (Shift+T · 기술 화면의 "기술 지도"): 시대(가로) × 분야(세로) 위에 기술 111개의 마디와 선행 선.
/// · 마디: 기술마다 고유 아이콘 (분야 실루엣 + 그림) — 익힘(채움) · 고를 수 있음(테) · 선행 · 조건 대기(흐림 + 조건 딱지) · 갈림길에서 버림(회색 + 자물쇠).
/// · 숨은 기술: 조합의 한쪽을 익혔거나 유물 · 교류를 기다리면 점선 "?"로 기척만, 드러나면 금빛 점선이 조합 부모에서 이어진다.
/// · 갈림길: 두 해법 사이 보라 점선 + 갈래 표시, 고른 쪽엔 금빛 별, 회의 대기 중이면 깜빡인다.
/// · 연구 중: 도는 고리 + 진척 호 · 컴퓨터 추천: 하늘색 칩 딱지 · 지금 실험: 플라스크 딱지.
/// · 오른쪽: 연구 중 · 컴퓨터 추천(근거) · 실험(누가 · 버릇 · 짝 · 노트) · 갈림길(배의 이름) · 최근 일.
/// 보기만 한다 — 회의 · 실험은 승무원이 한다.
/// </summary>
public partial class Hud
{
    public bool TechWebOpen { get; set; }

    public void ToggleTechWeb()
    {
        if (!TechOpen) { ToggleTech(); TechWebOpen = true; return; }
        TechWebOpen = !TechWebOpen;
    }

    private static readonly Color WebFork = new("#c58cff"), WebGold = new("#f2c94c"), WebRec = new("#7cd4ff");

    private void DrawTechWeb(Vector2 mouse)
    {
        var w = _world;
        var tw = w.TechWeb;
        var e = w.Eras;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f;
        float width = Screen.X - RightColumnWidth - Margin * 3;
        float height = Screen.Y - y0 - LogFullHeight - Margin - 10f;
        if (width < 520f || height < 300f) { DrawTechWebSmall(mouse, x0, y0, width, height); return; }
        var card = new Rect2(x0, y0, width, height);
        _chronicleRect = card;
        Card(card);
        float left = x0 + 16f, right = card.End.X - 16f;
        UiKit.CardTitle(this, left, right - 170f, y0 + 30, $"{w.Ship.Name} 기술 지도", $"{EraSystem.EraName(e.Era)} · 익힘 {e.Known.Count}/{TechWeb.Every.Length}" + (tw.Identity != "" ? $" · {tw.Identity}" : ""), "star"); // v16.24
        Button(new Rect2(right - 58, y0 + 10, 58, 26), "T 닫기", false, mouse, ToggleTech, Ui.TextSmall);
        Button(new Rect2(right - 58 - 96, y0 + 10, 90, 26), "설비 단계", false, mouse, () => TechWebOpen = false, Ui.TextSmall);
        DrawWebLegend(left, y0 + 50f);

        float panelW = MathF.Min(300f, width * 0.3f);
        var map = new Rect2(left, y0 + 66f, right - left - panelW - 14f, height - 66f - 12f);
        DrawWebMap(map, mouse);
        DrawWebPanel(new Rect2(map.End.X + 14f, map.Position.Y, panelW, map.Size.Y));
    }

    private void DrawTechWebSmall(Vector2 mouse, float x0, float y0, float width, float height)
    {
        var card = new Rect2(x0, y0, MathF.Max(260f, width), MathF.Max(200f, height));
        _chronicleRect = card;
        Card(card);
        Gfx.Text(this, Fonts.Bold, new Vector2(x0 + 16, y0 + 28), "기술 지도 — 창이 좁다", Ui.TextTitle, Palette.Text);
        Button(new Rect2(card.End.X - 74, y0 + 10, 58, 26), "T 닫기", false, mouse, ToggleTech, Ui.TextSmall);
        DrawWebPanel(new Rect2(x0 + 16, y0 + 44, card.Size.X - 32, card.Size.Y - 56));
    }

    private void DrawWebLegend(float x, float y)
    {
        var demo = TechWeb.Find("fireproof")!;
        float cx = x + 8f;
        void Item(Action<Vector2> draw, string text)
        {
            draw(new Vector2(cx, y - 4f));
            Gfx.Text(this, Fonts.Body, new Vector2(cx + 12f, y), text, Ui.TextTiny, Palette.TextMuted);
            cx += 22f + Gfx.Width(Fonts.Body, text, Ui.TextTiny) + 10f;
        }
        Item(c => TechIcons.Draw(this, demo, c, 7f, 1), "익힘");
        Item(c => TechIcons.Draw(this, demo, c, 7f, 0), "고를 수 있음");
        Item(c => TechIcons.Draw(this, demo, c, 7f, 2, 0.8f), "선행 · 조건 대기");
        Item(c => { TechIcons.Draw(this, demo, c, 7f, 3); TechIcons.Lock(this, c, 4f, Palette.Danger); }, "갈림길에서 버림");
        Item(c => { DrawArc(c, 7f, 0, Mathf.Tau, 14, WebGold.WithAlpha(0.7f), 1f, true); Gfx.TextCentered(this, Fonts.Bold, c + new Vector2(0, 3.5f), "?", Ui.TextTiny, WebGold); }, "숨은 기술의 기척");
        Item(c => { DrawDashed(c - new Vector2(7, 0), c + new Vector2(7, 0), WebFork, 1.4f, 3f); }, "갈림길");
        Item(c => TechIcons.Glyph(this, "chip", c, 5f, WebRec, 1.2f), "컴퓨터 추천");
        Item(c => TechIcons.Glyph(this, "flask", c, 5f, Palette.Warning, 1.2f), "실험 중");
    }

    private void DrawDashed(Vector2 a, Vector2 b, Color col, float w, float dash)
    {
        float len = (b - a).Length();
        if (len < 0.5f) return;
        var d = (b - a) / len;
        for (float t = 0f; t < len; t += dash * 2f) DrawLine(a + d * t, a + d * MathF.Min(len, t + dash), col, w, true);
    }

    private void DrawCurve(Vector2 a, Vector2 b, Color col, float w, bool dashed = false)
    {
        float dx = MathF.Max(20f, (b.X - a.X) * 0.5f);
        var c1 = a + new Vector2(dx, 0f);
        var c2 = b - new Vector2(dx, 0f);
        var pts = new Vector2[17];
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f, u = 1f - t;
            pts[i] = u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b;
        }
        if (!dashed) { DrawPolyline(pts, col, w, true); return; }
        for (int i = 0; i < 16; i += 2) DrawLine(pts[i], pts[i + 1], col, w, true);
    }

    /// <summary>숨은 기술의 기척: 조합 부모 하나를 익혔거나 · 유물 · 교류 조건.</summary>
    private bool Hinted(EraTech t)
    {
        var n = TechWeb.Node(t.Id);
        if (n.Combo != null) return n.Combo.Any(_world.TechWeb.Known);
        return n.Gate is { Kind: GateKind.Relic or GateKind.Contact };
    }

    private void DrawWebMap(Rect2 map, Vector2 mouse)
    {
        var w = _world;
        var tw = w.TechWeb;
        var e = w.Eras;
        var fields = Enum.GetValues<TechField>();
        float labelW = 64f, headH = 20f;
        float colW = (map.Size.X - labelW) / EraSystem.Eras.Length;
        float rowH = (map.Size.Y - headH) / fields.Length;
        // 바탕: 시대 기둥 · 분야 줄
        for (int i = 0; i < EraSystem.Eras.Length; i++)
        {
            var (era, name, need) = EraSystem.Eras[i];
            var col = new Rect2(map.Position.X + labelW + i * colW, map.Position.Y, colW, map.Size.Y);
            bool open = w.Research >= need;
            DrawRect(new Rect2(col.Position + new Vector2(1, headH), col.Size - new Vector2(2, headH)), new Color(1, 1, 1, open ? (i % 2 == 0 ? 0.025f : 0.015f) : 0.005f));
            string head = $"{era}. {name}";
            while (head.Length > 3 && Gfx.Width(Fonts.Bold, head, Ui.TextTiny) > colW - 6) head = head[..^1];
            Gfx.TextCentered(this, Fonts.Bold, new Vector2(col.GetCenter().X, map.Position.Y + 10f), head, Ui.TextTiny, open ? (era == e.Era ? Palette.Accent : Palette.TextDim) : Palette.TextMuted);
            if (!open) Gfx.TextCentered(this, Fonts.Body, new Vector2(col.GetCenter().X, map.End.Y - 6f), $"연구 {need:0}점", Ui.TextMicro, Palette.TextMuted);
        }
        for (int j = 0; j < fields.Length; j++)
        {
            float y = map.Position.Y + headH + j * rowH;
            if (j % 2 == 1) DrawRect(new Rect2(map.Position.X, y, map.Size.X, rowH), new Color(1, 1, 1, 0.012f));
            var fc = TechIcons.FieldColor(fields[j]);
            DrawRect(new Rect2(map.Position.X, y + 3, 2.5f, rowH - 6), fc.WithAlpha(0.7f));
            Gfx.Text(this, Fonts.Bold, new Vector2(map.Position.X + 7, y + rowH * 0.5f + Gfx.CenterOffset(Fonts.Bold, Ui.TextTiny)), EraSystem.Fields(fields[j]), Ui.TextTiny, fc.Lightened(0.1f));
        }
        // 자리: 같은 칸(시대 × 분야)에 여럿이면 가로로 나눈다
        var pos = new Dictionary<string, Vector2>();
        float r = 13f;
        foreach (var g in TechWeb.Every.GroupBy(t => (t.Era, t.Field)))
        {
            var list = g.OrderBy(t => TechWeb.IsExtra(t.Id) ? 1 : 0).ThenBy(t => t.Cost).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
            int n = list.Count;
            float cx0 = map.Position.X + labelW + (g.Key.Era - 1) * colW;
            float cy = map.Position.Y + headH + Array.IndexOf(fields, g.Key.Field) * rowH + rowH * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float zig = n >= 3 ? (i % 2 == 0 ? -1f : 1f) * rowH * 0.16f : 0f;
                pos[list[i].Id] = new Vector2(cx0 + colW * (i + 1) / (n + 1), cy + zig);
            }
            r = MathF.Min(r, MathF.Min(rowH * 0.36f, colW / (n + 1) * 0.42f));
        }
        r = MathF.Max(6f, r);
        bool Shown(EraTech t) => tw.Visible(t) || Hinted(t);
        // 선: 선행 → 기술
        foreach (var t in TechWeb.Every)
        {
            if (!Shown(t)) continue;
            var n = TechWeb.Node(t.Id);
            var to = pos[t.Id];
            foreach (var p in n.Pre)
            {
                if (TechWeb.Find(p) is not EraTech pt || !Shown(pt)) continue;
                bool kp = tw.Known(p), kt = tw.Known(t.Id);
                var col = kp && kt ? Palette.Good.WithAlpha(0.45f) : kp ? Palette.Accent.WithAlpha(0.5f) : new Color(1, 1, 1, 0.1f);
                if (tw.Locked(t)) col = new Color(1, 1, 1, 0.05f);
                DrawCurve(pos[p] + new Vector2(r, 0f), to - new Vector2(r, 0f), col, kp ? 1.4f : 1f);
            }
            if (n.Combo != null && tw.Visible(t))
                foreach (var p in n.Combo) DrawCurve(pos[p] + new Vector2(r, 0f), to - new Vector2(r, 0f), WebGold.WithAlpha(tw.Known(p) ? 0.55f : 0.2f), 1.1f, dashed: true);
        }
        // 갈림길: 보라 점선 + 갈래
        foreach (var f in TechWeb.Forks)
        {
            var a = pos[f.A];
            var b = pos[f.B];
            var st = tw.ForkStates[f.Id];
            bool pending = st.Side < 0 && st.PendingSince >= 0;
            float blink = pending ? 0.55f + 0.45f * Mathf.Sin(Time.GetTicksMsec() / 260f) : 0.7f;
            DrawDashed(a, b, WebFork.WithAlpha((st.Side >= 0 ? 0.35f : 0.6f) * blink), pending ? 2f : 1.3f, 4f);
            var m = (a + b) * 0.5f;
            DrawCircle(m, 5.5f, new Color(0.06f, 0.06f, 0.1f, 0.95f), true, -1f, true);
            DrawLine(m + new Vector2(0, 3.5f), m, WebFork, 1.4f, true);
            DrawLine(m, m + new Vector2(-3f, -3.5f), WebFork, 1.4f, true);
            DrawLine(m, m + new Vector2(3f, -3.5f), WebFork, 1.4f, true);
        }
        // 마디
        EraTech? hover = null;
        Vector2 hoverAt = default;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 300f);
        foreach (var t in TechWeb.Every)
        {
            var c = pos[t.Id];
            if (!tw.Visible(t))
            {
                if (!Hinted(t)) continue;
                DrawDashed(c + new Vector2(-r, -r), c + new Vector2(r, -r), WebGold.WithAlpha(0.35f), 1f, 2.5f);
                for (int k = 0; k < 10; k++) { float a0 = Mathf.Tau * k / 10f; DrawArc(c, r * 0.9f, a0, a0 + 0.3f, 4, WebGold.WithAlpha(0.55f), 1f, true); }
                Gfx.TextCentered(this, Fonts.Bold, c + new Vector2(0, r * 0.35f), "?", (int)MathF.Max(9f, r * 1.1f), WebGold.WithAlpha(0.85f));
                if ((mouse - c).LengthSquared() < r * r) { hover = t; hoverAt = c; }
                continue;
            }
            bool known = tw.Known(t.Id), locked = tw.Locked(t), open = !known && tw.Open(t) && t.Era <= e.Era;
            int state = known ? 1 : locked ? 3 : open ? 0 : 2;
            TechIcons.Draw(this, t, c, r, state, state == 2 ? 0.75f : 1f);
            if (locked) TechIcons.Lock(this, c + new Vector2(-r * 0.7f, -r * 0.7f), r * 0.42f, Palette.Danger);
            else if (!known && TechWeb.Node(t.Id).Gate is TechGate g && !tw.GateMet(t))
            {
                var bc = c + new Vector2(-r * 0.75f, -r * 0.75f);
                DrawCircle(bc, r * 0.34f, new Color(0.08f, 0.08f, 0.1f, 0.95f), true, -1f, true);
                TechIcons.Glyph(this, TechIcons.GateGlyph(g.Kind), bc, r * 0.22f, Palette.Warning, 1f);
            }
            if (TechWeb.ForkFor(t.Id) is var (f, side) && tw.ForkStates[f.Id].Side == side)
                TechIcons.Glyph(this, "star", c + new Vector2(0f, -r * 1.15f), r * 0.32f, WebGold, 1f);
            if (e.Project == t.Id)
            {
                DrawArc(c, r * 1.32f, 0f, Mathf.Tau, 28, Palette.Warning.WithAlpha(0.25f + 0.35f * pulse), 1.4f, true);
                float frac = Mathf.Clamp(e.Progress / MathF.Max(1f, tw.CostOf(t)), 0f, 1f);
                DrawArc(c, r * 1.32f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * frac, 28, Palette.Warning, 2.4f, true);
            }
            if (tw.RecTech == t.Id && !known) TechIcons.Glyph(this, "chip", c + new Vector2(r * 1.05f, -r * 0.95f), r * 0.32f, WebRec, 1f);
            if (tw.Trial?.Tech == t.Id) TechIcons.Glyph(this, "flask", c + new Vector2(-r * 1.05f, r * 0.9f), r * 0.34f, Palette.Warning, 1f);
            if ((mouse - c).LengthSquared() < r * r * 1.2f) { hover = t; hoverAt = c; }
        }
        if (hover != null)
        {
            DrawArc(hoverAt, r * 1.5f, 0f, Mathf.Tau, 24, Palette.Text.WithAlpha(0.6f), 1.2f, true);
            var ht = hover;
            var at = hoverAt + new Vector2(r + 6f, -r);
            _tip = () => UiKit.Tooltip(this, at, Screen, WebTitle(ht), WebLines(ht));
        }
    }

    private string WebTitle(EraTech t) => _world.TechWeb.Visible(t) ? $"{t.Name} · {EraSystem.EraName(t.Era)} · {EraSystem.Fields(t.Field)}" : "숨은 기술";

    private static IEnumerable<string> Wrap(string text, int max)
    {
        while (text.Length > max)
        {
            int cut = text.LastIndexOf(' ', max);
            if (cut < max / 2) cut = max;
            yield return text[..cut].TrimEnd();
            text = text[cut..].TrimStart();
        }
        if (text.Length > 0) yield return text;
    }

    private List<TipLine> WebLines(EraTech t)
    {
        var w = _world;
        var tw = w.TechWeb;
        var n = TechWeb.Node(t.Id);
        var l = new List<TipLine>();
        void Add(string s, Color c) { foreach (var x in Wrap(s, 34)) l.Add(new TipLine(x, c)); }
        if (!tw.Visible(t))
        {
            if (n.Combo != null) Add($"무언가 보일 듯하다 — {string.Join(" + ", n.Combo.Select(p => tw.Known(p) ? TechWeb.Find(p)!.Name : "???"))}", WebGold);
            else if (n.Gate != null) Add($"아직 모른다 — {n.Gate.Text}", WebGold);
            return l;
        }
        Add(t.Effect, Palette.Text);
        if (t.Risk != "없음") Add($"위험: {t.Risk}", Palette.Warning);
        Add($"상태: {tw.Status(t)}", tw.Known(t.Id) ? Palette.Good : tw.Open(t) ? Palette.Accent : Palette.TextDim);
        if (n.Pre.Length > 0) Add("선행: " + string.Join(" · ", n.Pre.Select(p => $"{(tw.Known(p) ? "✓" : "·")}{TechWeb.Find(p)!.Name}({EraSystem.Fields(TechWeb.Find(p)!.Field)})")), Palette.TextDim);
        if (n.Gate != null) Add((tw.GateMet(t) ? "열린 까닭: " + tw.Why.GetValueOrDefault(t.Id, n.Gate.Text) : "조건: " + n.Gate.Text), tw.GateMet(t) ? Palette.Good : Palette.Warning);
        else if (n.Combo != null) Add("드러난 까닭: " + tw.Why.GetValueOrDefault(t.Id, ""), WebGold);
        if (n.Trial) Add("역설계 — 연구 흐름은 1/4만, 실험으로 익힌다", Palette.TextDim);
        if (TechWeb.ForkFor(t.Id) is var (f, side))
        {
            var st = tw.ForkStates[f.Id];
            var rival = TechWeb.Find(side == 0 ? f.B : f.A)!;
            Add(st.Side < 0 ? $"갈림길 — {f.Problem}: {rival.Name}과(와) 둘 중 하나 ({(st.PendingSince >= 0 ? "회의 안건" : "아직")})"
                : st.Side == side ? $"갈림길에서 골랐다 — '{(side == 0 ? f.TitleA : f.TitleB)}' ({st.Why})" : $"갈림길에서 버렸다 — {(st.Reopened ? "다시 꺼냄 · 값 두 배" : "잠김")}", WebFork);
            if (st.Advice != "" && st.Side < 0) Add("주 컴퓨터: " + st.Advice, WebRec);
        }
        foreach (var c in TechWeb.Chains.Where(c => c.Tech == t.Id)) Add($"부작용: {c.Text} ({TechWeb.KeyName(c.Key)} ×{c.Mul:0.##})", Palette.Warning.WithAlpha(0.85f));
        if (tw.RecTech == t.Id) Add("컴퓨터 추천: " + tw.RecWhy, WebRec);
        Add($"값 {tw.CostOf(t):0}점 · 배 모습: {TechWeb.Visual(t)}", Palette.TextMuted);
        return l;
    }

    /// <summary>
    /// v16.24 오른쪽 칸 — 같은 유리 톤의 작은 카드 다섯: 연구 중 · 주 컴퓨터 추천 · 실험 · 갈림길 · 최근.
    /// 카드마다 아이콘 머리 · 한두 줄 · 숫자는 칩으로 (글 줄을 줄였다).
    /// </summary>
    private void DrawWebPanel(Rect2 p)
    {
        var w = _world;
        var tw = w.TechWeb;
        var e = w.Eras;
        float x = p.Position.X, y = p.Position.Y, right = p.End.X;
        bool Room(float need) => y + need < p.End.Y;
        // 카드 하나: 바탕 · 머리 → 안쪽 그리기(쓴 높이를 돌려준다)
        void Section(string icon, string title, Color accent, Func<float, float, float> body, float est)
        {
            if (!Room(est)) return;
            float top = y;
            float h = body(x + Ui.S3, top + 28f) + 34f;
            h = MathF.Min(h, p.End.Y - top);
            // 바탕은 내용 뒤에 깔 수 없으니 테두리만 (안쪽은 유리 그대로)
            Gfx.RoundRect(this, new Rect2(x, top, right - x, h), new Color(1, 1, 1, 0.025f), Ui.RadiusControl, accent.WithAlpha(0.22f));
            UiKit.Header(this, x + Ui.S3, right - Ui.S3, top + 18f, title, null, icon, accent);
            y = top + h + Ui.S2;
        }
        float Lines(string text, float lx, float ly, Color c, int maxLines = 2, int size = Ui.TextSmall)
        {
            var ls = UiKit.Wrap(text, right - lx - Ui.S3, size, Fonts.Body, maxLines);
            for (int i = 0; i < ls.Count; i++) Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 12f + i * 15f), ls[i], size, c);
            return ls.Count * 15f;
        }

        Section("star", "연구 중", Palette.Warning, (lx, ly) =>
        {
            if (TechWeb.Find(e.Project) is not EraTech pt) return Lines("고를 연구가 없다 — 회의나 갈림길을 기다린다", lx, ly, Palette.TextMuted);
            TechIcons.Draw(this, pt, new Vector2(lx + 9f, ly + 7f), 8f, 0);
            Gfx.Text(this, Fonts.Bold, new Vector2(lx + 24f, ly + 12f), UiKit.Fit(pt.Name, right - lx - 70f, Ui.TextBody, Fonts.Bold), Ui.TextBody, Palette.Text);
            float f = Mathf.Clamp(e.Progress / MathF.Max(1f, tw.CostOf(pt)), 0f, 1f);
            Gfx.TextRight(this, Fonts.Bold, new Vector2(right - Ui.S3, ly + 12f), $"{f * 100f:0}%", Ui.TextSmall, Palette.Warning);
            UiKit.Gauge(this, new Rect2(lx, ly + 20f, right - lx - Ui.S3, Ui.GaugeH), f, Palette.Warning);
            return 28f + Lines(e.ProjectWhy, lx, ly + 26f, Palette.TextDim);
        }, 80f);

        Section("computer", "주 컴퓨터 추천", WebRec, (lx, ly) =>
        {
            if (TechWeb.Find(tw.RecTech) is not EraTech rt)
                return Lines(w.Automation.Present && w.Automation.MainOnline ? "아직 권할 것이 없다" : "주 컴퓨터가 멎어 권하지 못한다", lx, ly, Palette.TextMuted);
            Gfx.Text(this, Fonts.Bold, new Vector2(lx, ly + 12f), UiKit.Fit(rt.Name, right - lx - Ui.S3, Ui.TextBody, Fonts.Bold), Ui.TextBody, WebRec);
            float h = 16f + Lines(tw.RecWhy, lx, ly + 16f, Palette.TextDim);
            UiKit.Stats(this, new Vector2(lx, ly + h + 12f), right - Ui.S3, new (string, int, string, Tone)[]
            {
                ("target", tw.Stats.Followed, "따름", Tone.Good), ("close", tw.Stats.Ignored, "다른 것", Tone.Normal),
            });
            return h + (tw.Stats.Followed + tw.Stats.Ignored > 0 ? 22f : 4f);
        }, 70f);

        Section("diagnostic", "실험", Palette.Accent, (lx, ly) =>
        {
            float h = 0f;
            if (tw.Trial is ExperimentState x1 && TechWeb.Find(x1.Tech) is EraTech xt)
            {
                var lead = x1.Lead >= 0 && x1.Lead < w.Crew.Count ? w.Crew[x1.Lead] : null;
                var mate = x1.Partner >= 0 && x1.Partner < w.Crew.Count ? w.Crew[x1.Partner] : null;
                Gfx.Text(this, Fonts.Bold, new Vector2(lx, ly + 12f), UiKit.Fit(xt.Name + (x1.Relic ? " · 유물을 뜯어 본다" : ""), right - lx - 50f, Ui.TextBody, Fonts.Bold), Ui.TextBody, Palette.Text);
                Gfx.TextRight(this, Fonts.Bold, new Vector2(right - Ui.S3, ly + 12f), $"{x1.Progress * 100f:0}%", Ui.TextSmall, x1.Paused ? Palette.TextMuted : Palette.Warning);
                UiKit.Gauge(this, new Rect2(lx, ly + 20f, right - lx - Ui.S3, Ui.GaugeH), x1.Progress, x1.Paused ? Palette.TextMuted : Palette.Warning);
                h = 30f;
                string who = $"{lead?.Name ?? "?"} ({TechWebSystem.StyleName(x1.Style)})" + (mate != null ? $" · 짝 {mate.Name}" : "");
                h += Lines(who, lx, ly + h - 4f, Palette.TextDim, 1);
                string state = x1.Paused ? "멈춤 — 작업대에 노트가 있다 · 누구든 이어 한다" : lead != null && tw.Researching(lead) ? x1.LeadWhy : $"차례를 기다린다 · {x1.LeadWhy}";
                h += Lines(state, lx, ly + h - 4f, x1.Paused ? Palette.Warning : Palette.TextMuted, 1);
                if (x1.Warned) h += Lines(x1.Heeded ? "컴퓨터 경고를 듣고 천천히 한다" : "컴퓨터 경고를 듣지 않았다", lx, ly + h - 4f, x1.Heeded ? Palette.Good : Palette.Danger, 1);
            }
            else h = Lines("다음 실험 차례를 기다린다", lx, ly, Palette.TextMuted);
            var s = tw.Stats;
            UiKit.Stats(this, new Vector2(lx, ly + h + 12f), right - Ui.S3, new (string, int, string, Tone)[]
            {
                ("diagnostic", s.Experiments, "실험", Tone.Normal), ("target", s.Successes, "성공", Tone.Good), ("broken", s.Failures, "실패", Tone.Caution),
                ("incident", s.Accidents, "사고", Tone.Danger), ("star", s.Breakthroughs, "돌파", Tone.Info), ("log", tw.Notes.Count, "노트", Tone.Normal),
            });
            return h + 20f;
        }, 90f);

        Section("route", "갈림길", WebFork, (lx, ly) =>
        {
            float h = 0f;
            foreach (var f in TechWeb.Forks)
            {
                if (ly + h + 18f > p.End.Y - 40f) break;
                var st = tw.ForkStates[f.Id];
                var a = TechWeb.Find(f.A)!;
                var b = TechWeb.Find(f.B)!;
                float yy = ly + h + 12f;
                float cx = lx;
                void Side(EraTech t, bool chosen, bool lost)
                {
                    TechIcons.Draw(this, t, new Vector2(cx + 6f, yy - 4f), 6f, chosen ? 1 : lost ? 3 : 0);
                    cx += 15f;
                    Gfx.Text(this, chosen ? Fonts.Bold : Fonts.Body, new Vector2(cx, yy), t.Name, Ui.TextTiny, chosen ? WebGold : lost ? Palette.TextMuted : Palette.TextDim);
                    cx += Gfx.Width(Fonts.Body, t.Name, Ui.TextTiny) + 6f;
                }
                Side(a, st.Side == 0, st.Side == 1);
                Gfx.Text(this, Fonts.Bold, new Vector2(cx, yy), "↔", Ui.TextTiny, WebFork);
                cx += 16f;
                Side(b, st.Side == 1, st.Side == 0);
                string mid = st.Side < 0 ? (st.PendingSince >= 0 ? "회의 대기" : "") : st.Reopened ? "버린 쪽을 다시 꺼냈다" : "";
                if (mid != "" && cx < right - 40f) Gfx.Text(this, Fonts.Body, new Vector2(cx, yy), UiKit.Fit(mid, right - cx - Ui.S3, Ui.TextMicro), Ui.TextMicro, st.Side < 0 ? WebFork : Palette.Warning);
                h += 16f;
            }
            return h;
        }, 50f);

        Section("clock", "최근", Palette.TextDim, (lx, ly) =>
        {
            float h = 0f;
            foreach (var (tick, text, tone) in tw.Recent.AsEnumerable().Reverse().Take(6))
            {
                if (ly + h + 30f > p.End.Y) break;
                Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + h + 12f), SimTime.Clock(tick), Ui.TextTiny, Palette.TextMuted);
                Gfx.Text(this, Fonts.Body, new Vector2(lx + 36f, ly + h + 12f), UiKit.Fit(text, right - lx - 36f - Ui.S3, Ui.TextSmall), Ui.TextSmall,
                    tone == 1 ? Palette.Good.WithAlpha(0.9f) : tone == 2 ? Palette.Warning : Palette.TextDim);
                h += 15f;
            }
            return h == 0f ? Lines("아직 없다", lx, ly, Palette.TextMuted) : h;
        }, 40f);
    }
}
