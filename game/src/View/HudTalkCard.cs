using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v18.17 대화 카드 (보기만): 중요한 순간이 오면 화면 위쪽에 잠깐 떠오른다.
/// 두 사람의 초상(표정이 결과에 따라 바뀐다) · 장면 · 첫마디 · 선택지(열린 것은 가능성 막대와 근거 — 솜씨 · 관계 · 기분 · 상황,
/// 닫힌 것은 자물쇠와 이유) → 말하는 사람이 고른 것과 그 이유 → 결과 · 대답 · 새 갈래.
/// 승무원 카드에는 지금 이야기의 상징과 단계 한 줄 (HudCrewCard에서 부른다).
/// </summary>
public partial class Hud
{
    private int _talkId = -1, _talkShift;
    private float _talkAt = -99f;
    private bool _talkClosed;
    private Rect2 _talkRect;

    private static Color TalkColor(CardKind k) => k switch
    {
        CardKind.Persuade => new Color("#ff8a5c"), CardKind.Calm => new Color("#7cc4ff"), CardKind.Mediate => new Color("#f5d547"),
        CardKind.Accuse => new Color("#ff5c6c"), CardKind.Confess => new Color("#ff8ac2"), CardKind.Heart => new Color("#b9a3ff"), _ => new Color("#e8a0d0"),
    };

    private CrewMember? TalkWho(int id) => _world.Crew.FirstOrDefault(c => c.Id == id);

    private void DrawTalkCard(Vector2 mouse)
    {
        var w = _world;
        var st = w.Tales;
        if (StorySystem.Off || st.Cards.Count == 0) return;
        if (CouncilOpen || PolicyOpen || ChronicleOpen || TechOpen || ControlOpen || ChainOpen || ScaleCodexOpen || HelpOpen || VoyageOpen) return; // v17.7 항해 결산과 겹치지 않게
        var last = st.Cards[^1];
        if (last.Id != _talkId) { _talkId = last.Id; _talkAt = _time; _talkShift = 0; _talkClosed = false; }
        bool hover = _talkRect.HasPoint(mouse);
        if (_talkClosed || _time - _talkAt > 16f && !hover && _talkShift == 0) { _talkRect = new Rect2(); return; }
        int idx = Math.Clamp(st.Cards.Count - 1 - _talkShift, 0, st.Cards.Count - 1);
        var k = st.Cards[idx];
        float age = _talkShift > 0 ? 99f : _time - _talkAt;
        var sp = TalkWho(k.Speaker); var li = TalkWho(k.Listener);
        if (sp == null || li == null) return;
        var tone = TalkColor(k.Kind);

        float W = 610f, rowH = 30f;
        float H = 44f + 92f + k.Options.Count * rowH + 70f;
        float x0 = Mathf.Clamp((Screen.X - RightColumnWidth) * 0.5f - W * 0.5f + 150f, Margin + 290f, Screen.X - RightColumnWidth - W - Margin * 2);
        float y0 = Margin + 52f + 8f + 40f + 18f;
        var rect = new Rect2(x0, y0, W, H);
        _talkRect = rect;
        UiKit.Panel(this, rect);
        DrawRect(new Rect2(x0, y0 + 10, 3, H - 20), tone.WithAlpha(0.85f));
        float x = x0 + 18, right = rect.End.X - 18;

        // ── 머리: 갈래 · 제목 · 자리 · 시각 · 넘기기
        string kind = StorySystem.KindName(k.Kind);
        UiKit.Chip(this, new Vector2(x, y0 + 12), kind, true, false);
        float tx = x + UiKit.ChipWidth(kind) + 8;
        var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == k.RoomId);
        Gfx.Text(this, Fonts.Bold, new Vector2(tx, y0 + 27), UiKit.Fit(k.Title, 220, Ui.TextTitle, Fonts.Bold), Ui.TextTitle, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(tx + 230, y0 + 27), $"{room?.Name ?? ""} · {SimTime.Clock(k.Tick)}", Ui.TextSmall, Palette.TextMuted);
        Button(new Rect2(right - 22, y0 + 10, 22, 20), "×", false, mouse, () => _talkClosed = true, 11);
        if (st.Cards.Count > 1)
        {
            Button(new Rect2(right - 72, y0 + 10, 22, 20), "◀", false, mouse, () => _talkShift = Math.Min(_talkShift + 1, _world.Tales.Cards.Count - 1), 10);
            Button(new Rect2(right - 48, y0 + 10, 22, 20), "▶", false, mouse, () => { _talkShift = Math.Max(0, _talkShift - 1); if (_talkShift == 0) _talkAt = _time; }, 10);
        }

        // ── 두 사람: 초상 · 이름 · 출신
        float py = y0 + 44f + 40f;
        bool resolved = age > 2.4f;
        var lFace = resolved ? k.ListenerAfter : k.ListenerFace;
        var sFace = resolved ? (k.Success ? FaceLook.Smile : k.Kind == CardKind.Calm ? FaceLook.Worry : FaceLook.Sad) : k.SpeakerFace == FaceLook.Asleep ? FaceLook.Calm : k.SpeakerFace;
        StoryArt.Portrait(this, new Vector2(x + 34, py), 26f, sp, w.Body2.Peek(sp), sFace, ShipView.RoleCloth(sp.Role), _time);
        StoryArt.Portrait(this, new Vector2(right - 34, py), 26f, li, w.Body2.Peek(li), lFace, ShipView.RoleCloth(li.Role), _time + 1.3f);
        Gfx.Text(this, Fonts.Bold, new Vector2(x, py + 44), UiKit.Fit(sp.Name, 90, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, Palette.Text);
        float lw = Gfx.Width(Fonts.Bold, li.Name, Ui.TextSmall);
        Gfx.Text(this, Fonts.Bold, new Vector2(right - Mathf.Min(lw, 90), py + 44), UiKit.Fit(li.Name, 90, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, Palette.Text);
        var sr = w.Tales.RootsOf(sp); var lr = w.Tales.RootsOf(li);
        Gfx.Text(this, Fonts.Body, new Vector2(x, py + 56), $"{StorySystem.HomeShort[sr.Home]} · {StorySystem.Gens[sr.Gen]}", Ui.TextMicro, Palette.TextMuted);
        string lroots = $"{StorySystem.HomeShort[lr.Home]} · {StorySystem.Gens[lr.Gen]}";
        Gfx.Text(this, Fonts.Body, new Vector2(right - Gfx.Width(Fonts.Body, lroots, Ui.TextMicro), py + 56), lroots, Ui.TextMicro, Palette.TextMuted);
        // 가운데: 장면 · 첫마디 (말풍선) · 사이 (관계 막대)
        float mx = x + 80, mr = right - 80;
        Gfx.Text(this, Fonts.Body, new Vector2(mx, py - 26), UiKit.Fit(k.Scene, mr - mx, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
        var bub = new Rect2(mx, py - 16, mr - mx, 26);
        Gfx.RoundRect(this, bub, new Color(1, 1, 1, 0.06f), 8f, tone.WithAlpha(0.35f));
        DrawColoredPolygon(new[] { new Vector2(mx, py - 6), new Vector2(mx - 8, py + 2), new Vector2(mx, py + 4) }, new Color(1, 1, 1, 0.06f));
        Gfx.Text(this, Fonts.Body, new Vector2(mx + 8, py + 2), UiKit.Fit($"“{k.Opening}”", mr - mx - 16, Ui.TextBody), Ui.TextBody, Palette.Text);
        float rel = Mathf.Clamp(li.AffinityTo(sp), -1f, 1f);
        float cx = (mx + mr) * 0.5f;
        DrawRect(new Rect2(mx, py + 20, mr - mx, 3), new Color(1, 1, 1, 0.07f));
        DrawRect(new Rect2(cx, py + 20, (mr - mx) * 0.5f * rel, 3), rel >= 0 ? Palette.Good.WithAlpha(0.8f) : Palette.Danger.WithAlpha(0.8f));
        Gfx.Text(this, Fonts.Body, new Vector2(mx, py + 34), $"{li.Name}에게 {Ko.EunNeun(sp.Name)} {(rel >= 0.5f ? "아주 가까운 사람" : rel >= 0.2f ? "가까운 사람" : rel <= -0.3f ? "껄끄러운 사람" : "그냥 동료")}", Ui.TextTiny, Palette.TextMuted);

        // ── 선택지
        float oy = y0 + 44f + 92f;
        bool picked = age > 1.2f;
        for (int i = 0; i < k.Options.Count; i++)
        {
            var o = k.Options[i];
            var r = new Rect2(x, oy, right - x, rowH - 3);
            bool chosen = picked && i == k.Chosen;
            Gfx.RoundRect(this, r, chosen ? tone.WithAlpha(0.16f) : UiKit.Well, Ui.RadiusControl, chosen ? tone.WithAlpha(0.8f) : new Color(1, 1, 1, o.Open ? 0.05f : 0.02f));
            var tc = o.Open ? Palette.Text : Palette.TextMuted;
            if (!o.Open)
            {
                // 자물쇠
                var lc = new Vector2(x + 12, oy + 14);
                DrawRect(new Rect2(lc + new Vector2(-4, -1), new Vector2(8, 7)), Palette.TextMuted);
                DrawArc(lc + new Vector2(0, -2), 3f, Mathf.Pi, Mathf.Tau, 8, Palette.TextMuted, 1.4f, true);
            }
            else if (chosen) DrawColoredPolygon(new[] { new Vector2(x + 7, oy + 8), new Vector2(x + 15, oy + 13.5f), new Vector2(x + 7, oy + 19) }, tone);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 22, oy + 13), UiKit.Fit(o.Text, 250, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, tc);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 22, oy + 24), UiKit.Fit(o.Open ? $"“{o.Say}”" : o.Lock, 250, Ui.TextMicro), Ui.TextMicro, o.Open ? Palette.TextDim : Palette.TextMuted.Darkened(0.1f));
            if (o.Open)
            {
                float bx = x + 285, bw = 110;
                DrawRect(new Rect2(bx, oy + 8, bw, 6), new Color(1, 1, 1, 0.07f));
                var bc = o.Chance >= 0.6f ? Palette.Good : o.Chance >= 0.35f ? Palette.NeedFood : Palette.Danger;
                DrawRect(new Rect2(bx, oy + 8, bw * o.Chance, 6), bc.WithAlpha(0.85f));
                if (chosen && resolved) DrawLine(new Vector2(bx + bw * k.Roll, oy + 5), new Vector2(bx + bw * k.Roll, oy + 17), Palette.Text, 1.5f, true); // 굴린 눈
                Gfx.Text(this, Fonts.Bold, new Vector2(bx + bw + 6, oy + 15), $"{o.Chance * 100:0}%", Ui.TextSmall, bc);
                // 근거: 솜씨 · 관계 · 기분 · 상황
                float fx = bx;
                foreach (var (label, v) in o.Why)
                {
                    string s = $"{label} {v * 100:+0;-0;0}";
                    Gfx.Text(this, Fonts.Body, new Vector2(fx, oy + 26), s, Ui.TextMicro, v > 0.01f ? Palette.Good.WithAlpha(0.85f) : v < -0.01f ? Palette.Danger.WithAlpha(0.85f) : Palette.TextMuted);
                    fx += Gfx.Width(Fonts.Body, s, Ui.TextMicro) + 8;
                }
            }
            oy += rowH;
        }

        // ── 고른 이유 · 결과 · 새 갈래
        float fy = oy + 6;
        if (picked) Gfx.Text(this, Fonts.Body, new Vector2(x, fy + 10), UiKit.Fit($"{Ko.IGa(sp.Name)} 고른 까닭: {k.ChoseWhy}", right - x, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
        if (resolved)
        {
            var oc = k.Success ? Palette.Good : Palette.Danger;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, fy + 30), UiKit.Fit(k.Outcome, 200, Ui.TextLabel, Fonts.Bold), Ui.TextLabel, oc);
            if (k.Answer != "") Gfx.Text(this, Fonts.Body, new Vector2(x + 210, fy + 30), UiKit.Fit($"{li.Name}: “{k.Answer}”", right - x - 210, Ui.TextSmall), Ui.TextSmall, Palette.Text);
            if (k.Branch != "")
            {
                DrawLine(new Vector2(x + 4, fy + 38), new Vector2(x + 4, fy + 48), Palette.Warning, 1.5f, true);
                DrawLine(new Vector2(x + 4, fy + 48), new Vector2(x + 12, fy + 48), Palette.Warning, 1.5f, true);
                Gfx.Text(this, Fonts.Body, new Vector2(x + 16, fy + 52), UiKit.Fit(k.Branch, right - x - 16, Ui.TextSmall), Ui.TextSmall, Palette.Warning);
            }
        }
        else if (picked)
        {
            int dots = 1 + (int)(_time * 3f) % 3;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, fy + 30), new string('·', dots), Ui.TextLabel, Palette.TextDim);
        }
    }

    /// <summary>승무원 카드: 지금 이야기 한 줄 (상징 그림 + 제목 · 단계 · 마음이 가는 사람).</summary>
    private float DrawTaleLine(CrewMember c, float x, float right, float y, Func<float, bool> fits)
    {
        if (StorySystem.Off || _world.Tales.CardLine(c) is not (string emblem, string text) || !fits(18)) return y;
        float tx = x;
        if (emblem != "") { StoryArt.Emblem(this, emblem, new Vector2(x + 7, y + 8), 13f, Palette.TextDim, _time); tx += 18; }
        Gfx.Text(this, Fonts.Body, new Vector2(tx, y + 12), UiKit.Fit(text, right - tx, Ui.TextSmall), Ui.TextSmall, new Color("#d9c6ff"));
        return y + 18;
    }
}
