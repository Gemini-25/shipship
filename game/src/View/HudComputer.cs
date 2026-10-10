using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.0 ④ · v16.6 주컴퓨터 상시 카드 (화면 왼쪽, 일지 위): 등급 육각 배지 · 지금 하는 일 한 줄 · 부하 막대 · 온도계 · 켜진 모듈 아이콘 줄 ·
/// 제안 카드(근거 · 예상 효과 · 기한 고리 · 받기/거절) · 누르면 관제 화면. "오늘의 결정" 모아 보기 (컴퓨터 제안 · 회의 안건 · 결정 · 개조).
/// 관제 화면 탭: 다섯 칸 기록 · 보고·모듈(30일 물 예측 곡선 · 정비 일정표 · 당번표 …) · 사람·믿음.
/// </summary>
public partial class Hud
{
    public bool ComputerFolded { get; set; }
    public bool DecisionsOpen { get; set; }
    private int _controlTab;

    private static string Roman(int l) => l switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => "V" };

    private static Color LevelColor(int l) => l switch { >= 5 => new Color("#6ee7b7"), 4 => new Color("#7cc4ff"), 3 => new Color("#f2c66d"), 2 => new Color("#ff9a6b"), _ => new Color("#ff5c6c") };

    /// <summary>등급 육각 배지: 돌면 테가 돈다 · 재부팅이면 진행 호 · 멎으면 붉은 X.</summary>
    private void LevelBadge(Vector2 c, float r, AutomationSystem a)
    {
        var col = LevelColor(a.Level);
        var hex = new Vector2[6];
        for (int k = 0; k < 6; k++) hex[k] = c + Vector2.FromAngle(k * Mathf.Tau / 6f + Mathf.Pi / 6f) * r;
        this.Poly(hex, new Color("#0b1119"));
        this.Polyline(hex.Append(hex[0]).ToArray(), col.WithAlpha(0.9f), 1.6f, true);
        if (a.Rebooting)
        {
            float frac = 1f - (a.RebootUntil - _world.Tick) / (float)SimTime.Minutes(4);
            this.Arc(c, r + 3f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Clamp(frac, 0f, 1f), 24, Palette.Warning, 2f, true);
            float sp = _time * 6f;
            this.Arc(c, r - 3f, sp, sp + 1.2f, 8, Palette.Warning.WithAlpha(0.8f), 1.4f, true);
        }
        else if (a.MainOnline)
        {
            float sp = _time * 0.8f;
            for (int k = 0; k < 3; k++) this.Arc(c, r + 3f, sp + k * Mathf.Tau / 3f, sp + k * Mathf.Tau / 3f + 0.7f, 6, col.WithAlpha(0.55f), 1.2f, true);
        }
        Gfx.TextCentered(this, Fonts.Bold, c, Roman(a.Level), a.Level >= 4 ? 13 : 12, a.MainOnline ? col : Palette.Danger);
        if (!a.MainOnline && !a.Rebooting)
        {
            DrawLine(c + new Vector2(-r * 0.7f, -r * 0.7f), c + new Vector2(r * 0.7f, r * 0.7f), Palette.Danger.WithAlpha(0.8f), 2f, true);
            DrawLine(c + new Vector2(-r * 0.7f, r * 0.7f), c + new Vector2(r * 0.7f, -r * 0.7f), Palette.Danger.WithAlpha(0.8f), 2f, true);
        }
    }

    /// <summary>상시 컴퓨터 카드 (Hud._Draw 한 줄에서).</summary>
    private void DrawComputerCard(Vector2 mouse)
    {
        var w = _world;
        var a = w.Automation;
        if (!a.Present || ControlOpen || ChronicleOpen || TechOpen || PolicyOpen || ChainOpen || VoyageOpen) return;
        const float width = 318f;
        var open = a.Asks.Open.ToList();
        // v16.24 방금 정한 제안 하나도 잠깐 남긴다 (누가 · 어디서 · 어떻게 정했는지 보이게)
        var just = a.Asks.All.LastOrDefault(p => p.State != ProposalState.Pending && p.DecidedAt >= 0 && w.Tick - p.DecidedAt < SimTime.Minutes(20));
        if (just != null) open.Add(just);
        // v17.6 자리는 배치 규칙이 정한다 (기록 바로 위 · 왼쪽 위 더미와 겹치면 접고, 그래도 안 되면 숨긴다)
        if (_plan.ComputerHidden || _plan.Computer.Empty) return;
        bool folded = ComputerFolded || _plan.ComputerFolded;
        var card = R(_plan.Computer);
        Card(card);
        // 카드 머리를 누르면 관제 화면
        var head = new Rect2(card.Position, new Vector2(width - 64f, 40f));
        _buttons.Add((head, ToggleControl));
        if (head.HasPoint(mouse)) Gfx.RoundRect(this, head.Grow(-3f), new Color(1, 1, 1, 0.03f), 10);
        float x = card.Position.X + 14, y = card.Position.Y, right = card.End.X - 12;
        LevelBadge(new Vector2(x + 13, y + 22), 12f, a);
        string state = a.Rebooting ? "재부팅" : !a.MainOnline ? (a.Core.BackupCore ? "본체 멎음 · 예비 연산기" : a.BackupActive ? "예비 제어기" : "멎음") : a.Operator != null ? $"관제석 {a.Operator.Name}" : "온라인";
        if (a.MainOnline && a.Core.SafeMode) state += " · 달아올라 느리게";
        if (a.Core.OnUps) state += $" · 비상 전지 {a.Core.Ups:0}분";
        else if (a.Core.SelfSaving) state += " · 제 연산 줄임";
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 34, y + 19), Fit($"{a.Voice.Call} · {AutomationSystem.LevelName(a.Level)}", width - 150, Ui.TextLabel, Fonts.Bold), Ui.TextLabel, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 34, y + 34), state + (a.Voice.Tone != "" ? $" · 말투 {a.Voice.Tone}" : ""), Ui.TextTiny, a.MainOnline ? Palette.Good : Palette.Danger);
        Button(new Rect2(right - 50, y + 9, 24, 22), folded ? "▴" : "▾", false, mouse, () => ComputerFolded = !ComputerFolded, Ui.TextSmall);
        Button(new Rect2(right - 22, y + 9, 22, 22), "Y", false, mouse, ToggleControl, Ui.TextSmall);
        if (folded) return;
        y += 44;
        // 지금 하는 일
        string now = a.NowLine;
        bool hot = a.FireCases.Count > 0 || a.ZoneActive || !a.MainOnline || open.Count > 0;
        var lines = WrapText(now, width - 40, Ui.TextBody);
        DrawNowGlyph(new Vector2(x + 6, y + 8), hot);
        for (int i = 0; i < Math.Min(2, lines.Count); i++)
        {
            string ln = i == 1 && lines.Count > 2 ? string.Join(" ", lines.Skip(1)) : lines[i]; // 두 줄을 넘으면 둘째 줄 끝에 "…"
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 20, y + 12 + i * 15), Fit(ln, width - 40, Ui.TextBody, Fonts.Bold), Ui.TextBody, hot ? Palette.Warning : Palette.Text);
        }
        y += 34;
        // 부하 · 온도
        float load = a.Load;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 10), "부하", Ui.TextTiny, Palette.TextDim);
        for (int k = 0; k < 12; k++)
        {
            float f0 = k / 12f;
            var seg = new Rect2(x + 30 + k * 9.5f, y + 2, 7.5f, 10);
            var sc = f0 < 0.6f ? Palette.Good : f0 < 0.85f ? new Color("#f2c66d") : Palette.Danger;
            this.Box(seg, load > f0 ? sc.WithAlpha(0.85f) : new Color(1, 1, 1, 0.06f));
        }
        Gfx.Text(this, Fonts.Body, new Vector2(x + 148, y + 11), $"{load * 100:0}%" + (a.Suspended.Count > 0 ? $" · {a.Suspended.Count}개 쉼" : ""), Ui.TextTiny, load > 0.9f ? Palette.Danger : Palette.TextDim);
        var room = a.Computer?.Body.Room;
        float temp = room?.Air.Temperature ?? 20f;
        float tx = x + 222;
        DrawThermo(new Vector2(tx, y + 7), temp);
        Gfx.Text(this, Fonts.Body, new Vector2(tx + 10, y + 11), $"{temp:0}℃", Ui.TextTiny, temp > AutomationSystem.OverheatC - 3f ? Palette.Danger : Palette.TextDim);
        if (temp > 32f) for (int k = 0; k < 3; k++) { float ph = Mathf.PosMod(_time * 0.8f + k * 0.33f, 1f); this.Arc(new Vector2(tx + 52 + k * 5, y + 10 - ph * 6), 2f, Mathf.Pi, Mathf.Tau, 5, Palette.Warning.WithAlpha(1f - ph), 1f, true); }
        y += 20;
        // 모듈 아이콘 줄
        // v17.6 줄 수를 미리 정해 아래 글줄과 겹치지 않게 (UiLayout.ComputerHeight 와 같은 셈)
        var mods = a.Modules.OrderByDescending(AutomationSystem.ModulePriority).ThenBy(m => (int)m).ToList();
        int perRow = UiLayout.IconsPerRow(width), iconRows = UiLayout.IconRows(mods.Count, width);
        ComputerModule? hover = null;
        for (int i = 0; i < mods.Count && i / perRow < iconRows; i++)
        {
            var m = mods[i];
            var c = new Vector2(x + 8 + (i % perRow) * UiLayout.CompIconStep, y + 10 + (i / perRow) * UiLayout.CompIconRow);
            ComputerIcons.Draw(this, m, c, 6.2f, ComputerIcons.StateOf(w, m), _time);
            if ((mouse - c).Length() < 8f) hover = m;
        }
        y += iconRows * UiLayout.CompIconRow + UiLayout.CompIconPad;
        if (hover is ComputerModule hm)
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 11), Fit($"{AutomationSystem.ModuleName(hm)} — {AutomationSystem.ModuleNote(hm)} (부하 {AutomationSystem.ModuleLoad(hm)})", width - 28, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Accent);
        else
        {
            var book = a.Book;
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 11), Fit($"오늘 조치 {book.ActsToday} · 맞음 {book.RightToday} · 틀림 {book.WrongToday} · 사람 신뢰 {a.Trusts.Average() * 100:0}% · 믿음≠실제 {a.Belief.DivergedCount()}방", width - 28, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
        }
        y += UiLayout.CompBookLine;
        DrawBrainBlock(x, y, width - 28); // v16.16 계획 · 예측 · 권한 · 배움
        y += BrainBlockH;
        // 버튼 줄
        float bx = x;
        foreach (var (label, active, act) in new (string, bool, Action)[]
                 {
                     ("관제", false, ToggleControl),
                     ("보는 배", _main.ViewMode == ViewMode.Belief, () => _main.ViewMode = _main.ViewMode == ViewMode.Belief ? ViewMode.Normal : ViewMode.Belief),
                     ("오늘의 결정", DecisionsOpen, () => DecisionsOpen = !DecisionsOpen),
                     ("선내 메신저", MessengerOpen, () => MessengerOpen = !MessengerOpen), // v17.3
                 })
        {
            float bw = Gfx.Width(Fonts.Bold, label, Ui.TextSmall) + 18;
            Button(new Rect2(bx, y + 4, bw, 22), label, active, mouse, act, Ui.TextSmall);
            bx += bw + 6;
        }
        y += UiLayout.CompButtons;
        // 제안 카드
        foreach (var p in open) { DrawProposal(new Rect2(x - 4, y, width - 20, ProposalH), p, mouse); y += ProposalH + 6f; }
        if (DecisionsOpen) DrawDecisions(new Vector2(card.End.X + 10, card.End.Y), mouse);
        if (MessengerOpen) DrawMessenger(new Vector2(card.End.X + (DecisionsOpen ? 460 : 10), card.End.Y), mouse); // v17.3
    }

    /// <summary>지금 하는 일 앞의 표시: 평시엔 천천히 도는 점, 일이 있으면 깜빡이는 삼각 경고.</summary>
    private void DrawNowGlyph(Vector2 c, bool hot)
    {
        if (hot)
        {
            float a = 0.55f + 0.45f * Mathf.Sin(_time * 7f);
            this.Poly(new[] { c + new Vector2(0, -6), c + new Vector2(6, 5), c + new Vector2(-6, 5) }, Palette.Warning.WithAlpha(a));
            Gfx.TextCentered(this, Fonts.Bold, c + new Vector2(0, 1), "!", Ui.TextMicro, new Color("#0b1119"));
        }
        else
        {
            this.Arc(c, 5f, 0f, Mathf.Tau, 16, Palette.Good.WithAlpha(0.4f), 1f, true);
            this.Circle(c + Vector2.FromAngle(_time * 2f) * 5f, 1.6f, Palette.Good, true, -1f, true);
        }
    }

    /// <summary>온도계: 유리관 · 눈금 · 과열선(38℃).</summary>
    private void DrawThermo(Vector2 top, float temp)
    {
        var tube = new Rect2(top.X - 2, top.Y - 5, 4, 11);
        Gfx.RoundRect(this, tube, new Color("#0b1119"), 2, new Color("#5f6879"), 1);
        float f = Mathf.Clamp((temp - 15f) / 30f, 0f, 1f);
        var col = temp > AutomationSystem.OverheatC - 3f ? Palette.Danger : temp > 30f ? Palette.Warning : Palette.Accent;
        this.Box(new Rect2(top.X - 1, top.Y + 6 - 11 * f, 2, 11 * f), col);
        this.Circle(new Vector2(top.X, top.Y + 7), 3f, col, true, -1f, true);
        float over = top.Y + 6 - 11 * Mathf.Clamp((AutomationSystem.OverheatC - 15f) / 30f, 0f, 1f);
        DrawLine(new Vector2(top.X + 3, over), new Vector2(top.X + 6, over), Palette.Danger, 1f);
    }

    private const float ProposalH = 112f;

    /// <summary>
    /// v16.24 제안 카드: 무엇을 · 왜 · 기한 고리 + 지나온 과정(올라옴 → 함장이 봄 → 정함). 완전 관전 — 받기 · 거절 단추는 없고,
    /// 함장 · 회의가 자연스러운 때 정한다(단말 앞 · 회의 · 급하면 바로). 아직이면 지금 누가 어디서 무엇을 기다리는지.
    /// </summary>
    private void DrawProposal(Rect2 r, Proposal p, Vector2 mouse)
    {
        var w = _world;
        bool pending = p.State == ProposalState.Pending;
        float pulse = pending ? 0.5f + 0.5f * Mathf.Sin(_time * 4f) : 0f;
        var tone = pending ? Palette.Warning : p.Accepted ? Palette.Good : Palette.TextDim;
        Gfx.RoundRect(this, r, pending ? new Color(0.18f, 0.12f, 0.05f, 0.85f) : UiKit.Well, Ui.RadiusControl, tone.WithAlpha(0.35f + 0.35f * pulse), 1);
        float x = r.Position.X + 32, right = r.End.X - 10;
        // 기한 고리 (정했으면 받음 ✔ · 거절 ✘)
        var rc = new Vector2(r.End.X - 20, r.Position.Y + 20);
        float left = Mathf.Clamp((p.Deadline - w.Tick) / (float)Math.Max(1, p.Deadline - p.Tick), 0f, 1f);
        this.Arc(rc, 11f, 0f, Mathf.Tau, 24, new Color(1, 1, 1, 0.08f), 3f, true);
        if (pending)
        {
            this.Arc(rc, 11f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * left, 24, left < 0.3f ? Palette.Danger : Palette.Warning, 3f, true);
            Gfx.TextCentered(this, Fonts.Bold, rc, $"{(p.Deadline - w.Tick) / (float)SimTime.Minutes(1):0}", Ui.TextMicro, Palette.Text);
        }
        else Icons.Draw(this, p.Accepted ? "target" : "close", rc, Ui.IconS, tone);
        // 조치 그림: 진공(바깥으로 빠지는 화살표) · 질식(가스 구름) · 그 밖(톱니)
        var ic = new Vector2(r.Position.X + 16, r.Position.Y + 18);
        if (p.Kind == "vacuum")
        {
            this.Box(new Rect2(ic - new Vector2(8, 7), new Vector2(12, 14)), new Color(1, 1, 1, 0.08f));
            this.Box(new Rect2(ic - new Vector2(8, 7), new Vector2(12, 14)), Palette.Warning, false, 1f);
            for (int k = 0; k < 3; k++) { float ph = Mathf.PosMod(_time * 1.5f + k * 0.33f, 1f); DrawLine(ic + new Vector2(4 + ph * 8, -4 + k * 4), ic + new Vector2(7 + ph * 8, -4 + k * 4), Palette.Accent.WithAlpha(1f - ph), 1.2f); }
        }
        else if (p.Kind == "inert")
            for (int k = 0; k < 4; k++) this.Circle(ic + new Vector2(-5 + k * 4, Mathf.Sin(_time * 2f + k) * 2f), 3.2f, new Color("#9fb3ff").WithAlpha(0.6f), true, -1f, true);
        else ComputerIcons.Draw(this, ComputerModule.Preempt, ic, 6f, ComputerIcons.State.On, _time);
        Gfx.Text(this, Fonts.Bold, new Vector2(x, r.Position.Y + 17), Fit(p.Title, r.Size.X - 74, Ui.TextLabel, Fonts.Bold), Ui.TextLabel, pending ? Palette.Warning : Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x, r.Position.Y + 32), Fit($"왜: {p.Basis} → {p.Effect}", r.Size.X - 74, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        // 지나온 과정 (마지막 세 단계) + 지금 기다리는 것
        var steps = new List<(long, string)>(p.Trail.Skip(Math.Max(0, p.Trail.Count - 3)));
        if (pending) steps.Add((w.Tick, ProposalWaitLine(p)));
        UiKit.Steps(this, x - 4, right, r.Position.Y + 40, steps, pending ? Tone.Caution : p.Accepted ? Tone.Good : Tone.Disabled, 4);
    }

    /// <summary>아직 정하지 않은 제안: 지휘하는 사람이 지금 어디서 무엇을 하나 (언제 정할지 보이게).</summary>
    private string ProposalWaitLine(Proposal p)
    {
        var w = _world;
        var boss = ProposalTiming.Boss(w);
        if (boss == null) return "정할 사람이 없다 — 기한이 되면 컴퓨터가 한다";
        if (ProposalTiming.Urgent(w, p)) return $"급하다 — {Ko.IGa(boss.Name)} 곧 정한다";
        if (p.SeenAt >= 0) return $"{Ko.IGa(boss.Name)} 생각하는 중";
        if (w.Meetings.Gathering || w.Meetings.Session != null) return "회의에서 이야기한다";
        string where = boss.Room?.Name ?? "밖";
        string doing = boss.Pose == Pose.Sleeping ? "자는 중" : "단말 앞에 가면 본다";
        return $"{boss.Name}: {where} · {doing}";
    }

    /// <summary>오늘의 결정: 컴퓨터 제안 · 회의 안건 · 결정 기록 · 개조.</summary>
    private void DrawDecisions(Vector2 bottomLeft, Vector2 mouse)
    {
        var w = _world;
        long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
        var rows = new List<(long tick, string src, string text, Color col, string glyph)>();
        foreach (var p in w.Automation.Asks.All.Where(p => p.Tick >= day0))
            rows.Add((p.Tick, "컴퓨터", $"{p.Title} — {(p.State == ProposalState.Pending ? "기다림" : p.DecideWhy)} ({(p.DecidedBy == "" ? "-" : p.DecidedBy)})" + (p.Result != "" ? $" · {p.Result}" : ""),
                p.State == ProposalState.Pending ? Palette.Warning : p.Score < 0 ? Palette.Danger : p.Score > 0 ? Palette.Good : Palette.TextDim, p.State == ProposalState.Pending ? "⏳" : p.Accepted ? "✔" : "✘"));
        foreach (var rec in w.Meetings.Minutes.Where(m => m.Tick >= day0))
            foreach (var it in rec.Items)
                rows.Add((rec.Tick, "회의", $"{it.Title} — {it.Outcome} (찬 {it.Yes} · 반 {it.No})" + (it.Computer != null ? $" · 컴퓨터: {it.Computer}" : ""), it.Passed ? Palette.Accent : Palette.TextDim, it.Passed ? "◆" : "◇"));
        foreach (var d in w.Meetings.Decisions.Where(d => d.Tick >= day0))
            rows.Add((d.Tick, "결정", $"{d.Title} (찬 {d.Yes.Count} · 반 {d.No.Count})", Palette.Text, "■"));
        foreach (var e in w.History.Events.Where(e => e.Tick >= day0 && e.Kind == HistoryKind.Upgrade))
            rows.Add((e.Tick, "개조", e.Text, new Color("#f2c66d"), "⚒"));
        rows.Sort((x, y) => y.tick.CompareTo(x.tick));
        float width = 440f, height = 40f + Math.Max(1, Math.Min(rows.Count, 14)) * 18f;
        var card = new Rect2(bottomLeft.X, bottomLeft.Y - height, width, height);
        Card(card);
        Gfx.Text(this, Fonts.Bold, card.Position + new Vector2(14, 22), $"오늘의 결정 ({SimTime.Day(w.Tick)}일) — 컴퓨터 제안 · 회의 · 결정 · 개조", Ui.TextBody, Palette.Text);
        Button(new Rect2(card.End.X - 30, card.Position.Y + 8, 22, 20), "×", false, mouse, () => DecisionsOpen = false, Ui.TextSmall);
        float y = card.Position.Y + 34;
        if (rows.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 14, y + 12), "오늘은 아직 정한 일이 없다", Ui.TextSmall, Palette.TextMuted);
        foreach (var (tick, src, text, col, glyph) in rows.Take(14))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 12, y + 12), SimTime.Clock(tick), Ui.TextTiny, Palette.TextMuted);
            Gfx.Text(this, Fonts.Bold, new Vector2(card.Position.X + 52, y + 12), glyph, Ui.TextSmall, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(card.Position.X + 68, y + 12), src, Ui.TextTiny, Palette.TextDim);
            Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 108, y + 12), Fit(text, width - 120, Ui.TextSmall, Fonts.Body), Ui.TextSmall, col);
            y += 18;
        }
    }

    // ───────────────────── 관제 화면 탭 ─────────────────────

    /// <summary>관제 화면 위 탭 (관제 · 기록 · 보고·모듈 · 사람·믿음). 0이 아니면 탭 내용을 그리고 true.</summary>
    private bool DrawControlTabs(Rect2 card, float x, float right, float y0, Vector2 mouse)
    {
        string[] tabs = { "관제", "지휘", "하루·정비표", "다섯 칸 기록", "보고·일정", "사람·믿음", "앞날·계획", "계획·권한" };
        int[] order = { 0, 6, 7, 1, 2, 3, 4, 5 }; // v16.20 "지휘"는 둘째 칸에 (번호는 그대로) · v16.27 "하루·정비표"는 셋째
        float tx = x;
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = order[i];
            float bw = Gfx.Width(Fonts.Bold, tabs[i], Ui.TextSmall) + 16;
            Button(new Rect2(tx, y0 + 58, bw, 22), tabs[i], _controlTab == idx, mouse, () => _controlTab = idx, Ui.TextSmall);
            tx += bw + 4;
        }
        if (_controlTab == 0) return false;
        float y = y0 + 88;
        switch (_controlTab)
        {
            case 1: DrawActTable(card, x, right, y); break;
            case 2: DrawReports(card, x, right, y, mouse); break;
            case 4: DrawForesight(card, x, right, y); break;
            case 5: DrawBrainTab(card, x, right, y); break; // v16.16
            case 6: DrawCommandTab(card, x, right, y); break; // v16.20 지휘: 믿는 배 지도 · 명령선 · 견줘 본 판단 · 전력 흐름
            case 7: DrawMateTab(card, x, right, y); break; // v16.27 아침 방송 · 주간 정비표 · 물자 · 날씨 · 돌봄 · 훈련 · 약속 · 거절 · 개조 · 약점
            default: DrawPeopleBelief(card, x, right, y); break;
        }
        return true;
    }

    private void DrawActTable(Rect2 card, float x, float right, float y)
    {
        var a = _world.Automation;
        var book = a.Book;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 10), UiKit.Fit($"모두 {book.Total}건 · 맞음 {book.Right} · 틀림 {book.Wrong} · 보류 {book.Held} — 몇 분 뒤 그 방이 나아졌나 · 사람이 쓰러졌나로 채점", right - (x), Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
        y += 22;
        float[] cols = { x, x + 44, x + 70, x + 70 + (right - x - 70) * 0.22f, x + 70 + (right - x - 70) * 0.42f, x + 70 + (right - x - 70) * 0.62f, x + 70 + (right - x - 70) * 0.76f };
        string[] head = { "시각", "", "관찰", "판단", "조치", "요청", "결과" };
        for (int i = 0; i < head.Length; i++) Gfx.Text(this, Fonts.Bold, new Vector2(cols[i], y + 10), head[i], Ui.TextTiny, Palette.TextMuted);
        Divider(x, right, y + 16);
        y += 22;
        foreach (var act in book.Acts.AsEnumerable().Reverse())
        {
            if (y > card.End.Y - 22) break;
            var sc = act.Score == 1 ? Palette.Good : act.Score == -1 ? Palette.Danger : act.Score == 2 ? Palette.TextDim : Palette.Accent;
            Gfx.Text(this, Fonts.Body, new Vector2(cols[0], y + 11), SimTime.Clock(act.Tick), Ui.TextTiny, Palette.TextMuted);
            DrawActGlyph(new Vector2(cols[1] + 10, y + 7), act.Kind, sc);
            string[] cells = { act.Observe, act.Judge, act.Act + (act.By != null ? $" ({act.By})" : ""), act.Request, act.Graded ? act.Result : $"채점 {Math.Max(0, (act.GradeAt - _world.Tick) / (float)SimTime.Minutes(1)):0}분 뒤" };
            for (int i = 0; i < cells.Length; i++)
            {
                float cw = (i + 3 < cols.Length ? cols[i + 3] : right) - cols[i + 2] - 6;
                Gfx.Text(this, Fonts.Body, new Vector2(cols[i + 2], y + 11), Fit(cells[i], cw, Ui.TextTiny, Fonts.Body), Ui.TextTiny, i == 4 ? sc : i == 2 ? Palette.Text : Palette.TextDim);
            }
            y += 17;
        }
    }

    /// <summary>조치 종류 그림 (표의 둘째 칸) — 배 화면의 빛 흐름 끝 아이콘과 같은 그림.</summary>
    private void DrawActGlyph(Vector2 c, ActKind k, Color col) => ComputerIcons.Act(this, k, c, 1f, col, _time);

    private void DrawReports(Rect2 card, float x, float right, float y, Vector2 mouse)
    {
        var w = _world;
        var a = w.Automation;
        var apps = a.Apps;
        float colW = (right - x) / 2f - 10;
        float lx = x, ly = y, rx = x + colW + 20, ry = y;
        // 왼쪽: 하루 보고 · 오늘 쌓이는 값 · 30일 물 예측
        SectionTitle(lx, ly + 10, "하루 보고");
        ly += 16;
        foreach (var rep in a.Book.Reports.AsEnumerable().Reverse().Take(2))
            foreach (var l in WrapText(rep.Text, colW, Ui.TextSmall).Take(3)) { Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 11), l, Ui.TextSmall, Palette.Text); ly += 14; }
        if (a.Book.Reports.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 11), "첫 보고는 날이 바뀔 때", Ui.TextSmall, Palette.TextMuted); ly += 14; }
        var t = a.Book.Today;
        Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 13), Fit($"오늘 지금까지: 전력 {t.Kwh:0.#}kWh · 물 {t.WaterL:0}L · 미룬 고장 {t.Deferred} · 피로 경보 {t.Fatigue} · 문 압 {t.DoorEq} · 메시지 {t.Messages}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        ly += 22;
        SectionTitle(lx, ly + 10, "30일 물 예측" + (a.Active(ComputerModule.WaterPlan) ? "" : " — 물 관리를 쉬고 있다"));
        ly += 14;
        var chart = new Rect2(lx, ly, colW, 90);
        DrawWaterChart(chart, apps, w.Water.Capacity);
        ly += 100;
        SectionTitle(lx, ly + 10, "맡은 일 · 부하 " + $"{a.Load * 100:0}% / 용량 {a.Capacity:0}");
        ly += 16;
        float mx = lx;
        foreach (var m in Enum.GetValues<ComputerModule>())
        {
            if (mx > lx + colW - 20) { mx = lx; ly += 22; }
            var st = ComputerIcons.StateOf(w, m);
            ComputerIcons.Draw(this, m, new Vector2(mx + 9, ly + 9), 6.5f, st, _time);
            if ((mouse - new Vector2(mx + 9, ly + 9)).Length() < 9f)
                Gfx.Text(this, Fonts.Body, new Vector2(lx, card.End.Y - 14), Fit($"{AutomationSystem.ModuleName(m)} — {AutomationSystem.ModuleNote(m)} · 부하 {AutomationSystem.ModuleLoad(m)} · 순위 {AutomationSystem.ModulePriority(m)}", right - lx, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Accent);
            mx += 22;
        }
        // 오른쪽: 정비 일정표 · 당번표 · 식단 · 메시지 · 항해 일지 · 무게중심
        SectionTitle(rx, ry + 10, "정비 일정표");
        ry += 16;
        foreach (var s in apps.MaintPlan.Take(5)) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit($"{SimTime.Clock(s.Tick)} {s.Machine} ({s.Room}) — {s.Why}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Text); ry += 13; }
        if (apps.MaintPlan.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), a.Active(ComputerModule.MaintPlan) ? "손볼 설비가 없다" : "정비 일정을 쉬고 있다", Ui.TextTiny, Palette.TextMuted); ry += 13; }
        ry += 6;
        SectionTitle(rx, ry + 10, "당번표");
        ry += 16;
        int today = SimTime.Day(w.Tick);
        foreach (var d in apps.Roster.Where(d => d.Day == today).Take(4)) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), $"{d.Duty} {d.Time} — {w.Crew.FirstOrDefault(c => c.Id == d.CrewId)?.Name}", Ui.TextTiny, Palette.Text); ry += 13; }
        ry += 6;
        SectionTitle(rx, ry + 10, "식단");
        ry += 16;
        foreach (var m in apps.Menu.Take(4)) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit(m, colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Text); ry += 13; }
        ry += 6;
        SectionTitle(rx, ry + 10, "개인 비서 메시지");
        ry += 16;
        foreach (var msg in apps.Messages.AsEnumerable().Reverse().Take(4)) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit($"{SimTime.Clock(msg.Tick)} → {w.Crew.FirstOrDefault(c => c.Id == msg.CrewId)?.Name} [{msg.Kind}] {msg.Text}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim); ry += 13; }
        ry += 6;
        SectionTitle(rx, ry + 10, "자동 항해 일지");
        ry += 16;
        foreach (var (tick, text) in apps.Logbook.AsEnumerable().Reverse().Take(3)) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit(text, colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim); ry += 13; }
        if (apps.BalanceAdvice != "") { ry += 6; Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit("무게중심: " + apps.BalanceAdvice, colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, apps.Tilt > 3f ? Palette.Warning : Palette.TextDim); }
    }

    /// <summary>30일 물 예측 곡선: 칸 · 용량선 · 바닥 나는 날 표시 · 오늘 점.</summary>
    private void DrawWaterChart(Rect2 r, ComputerApps apps, float capacity)
    {
        Gfx.RoundRect(this, r, new Color(0.02f, 0.05f, 0.09f, 0.9f), 6, new Color(1, 1, 1, 0.06f));
        for (int d = 0; d <= 30; d += 5)
        {
            float gx = r.Position.X + 6 + (r.Size.X - 12) * d / 30f;
            DrawLine(new Vector2(gx, r.Position.Y + 4), new Vector2(gx, r.End.Y - 12), new Color(1, 1, 1, 0.04f), 1f);
            Gfx.TextCentered(this, Fonts.Body, new Vector2(gx, r.End.Y - 5), $"{d}", 8, Palette.TextMuted);
        }
        var pts = new Vector2[31];
        for (int d = 0; d <= 30; d++)
            pts[d] = new Vector2(r.Position.X + 6 + (r.Size.X - 12) * d / 30f, r.End.Y - 14 - (r.Size.Y - 22) * Mathf.Clamp(apps.WaterForecast[d] / Mathf.Max(1f, capacity), 0f, 1f));
        var fill = new List<Vector2>(pts) { new(pts[30].X, r.End.Y - 14), new(pts[0].X, r.End.Y - 14) };
        this.Poly(fill.ToArray(), NetWaterColor.WithAlpha(0.18f));
        this.Polyline(pts, NetWaterColor, 1.6f, true);
        this.Circle(pts[0], 2.6f, Colors.White, true, -1f, true);
        if (apps.WaterEmptyDay >= 0)
        {
            var e = pts[apps.WaterEmptyDay];
            DrawLine(e + new Vector2(0, -20), e, Palette.Danger, 1f);
            Gfx.TextCentered(this, Fonts.Bold, e + new Vector2(0, -26), $"{apps.WaterEmptyDay}일 바닥", Ui.TextMicro, Palette.Danger);
        }
        Gfx.TextRight(this, Fonts.Body, new Vector2(r.End.X - 6, r.Position.Y + 12), $"{apps.WaterRate:+0;-0}L/일", Ui.TextMicro, apps.WaterRate < 0 ? Palette.Warning : Palette.Good);
    }

    private static readonly Color NetWaterColor = new("#4f9fdc");

    private void DrawPeopleBelief(Rect2 card, float x, float right, float y)
    {
        var w = _world;
        var a = w.Automation;
        float colW = (right - x) / 2f - 10;
        float lx = x, ly = y, rx = x + colW + 20, ry = y;
        SectionTitle(lx, ly + 10, $"사람마다 컴퓨터 신뢰 (배 전체 {w.Command.ComputerTrust * 100:0}%)");
        ly += 16;
        foreach (var c in w.Crew.Where(c => !c.Dead).OrderBy(c => a.Trusts.Of(c)))
        {
            if (ly > card.End.Y - 30) break;
            float tr = a.Trusts.Of(c);
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 11), Fit(c.Name, 56, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.Crew(c.Id));
            var bar = new Rect2(lx + 60, ly + 4, 90, 8);
            Gfx.Bar(this, bar, tr, tr < 0.3f ? Palette.Danger : tr < 0.5f ? Palette.Warning : Palette.Good);
            float b0 = a.Trusts.Base(c);
            DrawLine(new Vector2(bar.Position.X + bar.Size.X * b0, bar.Position.Y - 2), new Vector2(bar.Position.X + bar.Size.X * b0, bar.End.Y + 2), Colors.White.WithAlpha(0.5f), 1f);
            if (!a.Trusts.Obeys(c)) Gfx.Text(this, Fonts.Bold, new Vector2(lx + 154, ly + 11), "지시 의심", Ui.TextMicro, Palette.Danger);
            Gfx.Text(this, Fonts.Body, new Vector2(lx + 204, ly + 11), Fit(a.Trusts.LastWhy.GetValueOrDefault(c.Id, ""), colW - 204, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
            ly += 15;
        }
        ly += 8;
        SectionTitle(lx, ly + 10, "기억하는 틀린 판단");
        ly += 16;
        foreach (var (tick, roomId, kind, why) in a.Learn.WrongCalls.AsEnumerable().Reverse().Take(4))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 11), Fit($"{SimTime.Day(tick)}일 {SimTime.Clock(tick)} {(roomId >= 0 ? w.Ship.Rooms[roomId].Name : "")} — {why}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Warning);
            ly += 13;
        }
        if (a.Voice.Name != "") Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), UiKit.Fit($"이름 '{a.Voice.Name}' — {Ko.IGa(a.Voice.NamedBy)} 붙였다" + (a.Voice.Resets > 0 ? $" · 재설치 {a.Voice.Resets}번 (예전 '{a.Voice.FormerName}')" : ""), right - (lx), Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Accent);
        // 오른쪽: 컴퓨터가 보는 배 (믿음 ≠ 실제)
        SectionTitle(rx, ry + 10, "컴퓨터가 보는 배 — 믿음과 실제");
        ry += 16;
        foreach (var r in w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).OrderByDescending(r => a.Belief.Diverged(r, out _) ? 1 : 0).ThenBy(r => r.Id))
        {
            if (ry > card.End.Y - 20) break;
            var b = a.Belief.Of(r);
            bool diff = a.Belief.Diverged(r, out var why);
            float age = b.Updated < 0 ? 99f : (w.Tick - b.Updated) / (float)SimTime.Minutes(1);
            Gfx.Text(this, Fonts.Bold, new Vector2(rx, ry + 11), Fit(r.Name, 64, Ui.TextTiny, Fonts.Bold), Ui.TextTiny, diff ? Palette.Danger : Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 68, ry + 11), Fit($"사람 {b.People} · {(b.Fire ? "불 " : "")}{b.Pressure:0}kPa · 산소 {b.O2:0.0}" + (age > 1f ? $" · {age:0}분 전 값" : ""), 170, Ui.TextTiny, Fonts.Body), Ui.TextTiny, age > 5f ? Palette.TextMuted : Palette.TextDim);
            string tail = diff ? $"≠ {why}" : b.Fault != SensorFault.None ? BeliefModel.FaultName(b.Fault) : b.Trust < 1f ? $"감지기 믿음 {b.Trust * 100:0}%" : "";
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 242, ry + 11), Fit(tail, right - rx - 242, Ui.TextTiny, Fonts.Body), Ui.TextTiny, diff ? Palette.Danger : Palette.Warning);
            ry += 13;
        }
    }

    /// <summary>앞날 · 계획 탭 (v16.16 바탕): 왼쪽 — 지켜보는 값마다 지금 → 6시간 뒤 막대 · 문턱 · 믿음 고리 · 맞음/틀림 점 / 오른쪽 — 계획표 · 우주 예보.</summary>
    private void DrawForesight(Rect2 card, float x, float right, float y)
    {
        var w = _world;
        var a = w.Automation;
        var f = a.Foresight;
        float colW = (right - x) / 2f - 10;
        float lx = x, ly = y, rx = x + colW + 20, ry = y;
        SectionTitle(lx, ly + 10, $"앞날 예측 — {ComputerForesight.Horizon:0}시간 뒤 · 맞힘 {f.Hits}/{f.Hits + f.Misses}");
        ly += 18;
        foreach (var pr in ComputerForesight.Predictors)
        {
            if (ly > card.End.Y - 30) break;
            var p = f.Current.FirstOrDefault(q => q.Key == pr.Key);
            float conf = f.Skill(pr.Key);
            // 믿음 고리
            var rc = new Vector2(lx + 8, ly + 9);
            this.Arc(rc, 7f, 0f, Mathf.Tau, 18, Palette.TextMuted.WithAlpha(0.4f), 1.5f, true);
            this.Arc(rc, 7f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * conf, 18, conf >= 0.6f ? Palette.Good : conf >= 0.4f ? Palette.Accent : Palette.Warning, 2f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(lx + 20, ly + 13), Fit(pr.Name, 76, Ui.TextTiny, Fonts.Bold), Ui.TextTiny, Palette.Text);
            if (p == null) { Gfx.Text(this, Fonts.Body, new Vector2(lx + 100, ly + 13), "재는 중 (세 시간 모아야 추세)", Ui.TextTiny, Palette.TextMuted); ly += 22; continue; }
            // 막대: 지금(채움) → 예측(테) · 문턱 선
            float lo = pr.Low(w), hi = pr.High(w);
            float thr = lo > float.MinValue / 2 ? lo : hi < float.MaxValue / 2 ? hi : p.Now;
            float span = MathF.Max(MathF.Max(MathF.Abs(p.Now), MathF.Abs(p.Value)), MathF.Abs(thr)) * 1.25f + 0.001f;
            var bar = new Rect2(lx + 100, ly + 4, colW - 200, 10);
            this.Box(bar, new Color(1, 1, 1, 0.05f));
            float nx = Mathf.Clamp(p.Now / span, 0f, 1f), vx = Mathf.Clamp(p.Value / span, 0f, 1f), tx = Mathf.Clamp(thr / span, 0f, 1f);
            bool bad = p.Value < lo || p.Value > hi;
            this.Box(new Rect2(bar.Position, new Vector2(bar.Size.X * nx, bar.Size.Y)), Palette.Accent.WithAlpha(0.55f));
            this.Box(new Rect2(bar.Position.X + bar.Size.X * MathF.Min(nx, vx), bar.Position.Y + 2, bar.Size.X * MathF.Abs(vx - nx), bar.Size.Y - 4), (bad ? Palette.Danger : Palette.Good).WithAlpha(0.6f));
            DrawLine(new Vector2(bar.Position.X + bar.Size.X * vx, bar.Position.Y - 2), new Vector2(bar.Position.X + bar.Size.X * vx, bar.End.Y + 2), bad ? Palette.Danger : Palette.Text, 1.5f);
            DrawLine(new Vector2(bar.Position.X + bar.Size.X * tx, bar.Position.Y - 3), new Vector2(bar.Position.X + bar.Size.X * tx, bar.End.Y + 3), Palette.Warning, 1f);
            Gfx.Text(this, Fonts.Body, new Vector2(bar.End.X + 6, ly + 13), Fit($"{p.Now:0.#} → {p.Value:0.#}{p.Unit}", 92, Ui.TextTiny, Fonts.Body), Ui.TextTiny, bad ? Palette.Danger : Palette.TextDim);
            // 최근 채점 점 (맞음 초록 · 틀림 붉음)
            float dx = lx + 100;
            foreach (var g in f.Graded.Where(q => q.Key == pr.Key).Reverse().Take(12))
            {
                this.Circle(new Vector2(dx + 3, ly + 19), 2f, g.Hit == true ? Palette.Good : Palette.Danger, true, -1f, true);
                dx += 7;
            }
            ly += 26;
        }
        // 오른쪽: 계획표 · 우주 예보
        SectionTitle(rx, ry + 10, "계획표 — 예측이 문턱을 넘으면");
        ry += 18;
        foreach (var it in f.Plan.AsEnumerable().Reverse().Take(8))
        {
            var col = it.State == "예정" ? Palette.Accent : Palette.TextMuted;
            this.Box(new Rect2(rx, ry + 3, 4, 12), col);
            Gfx.Text(this, Fonts.Bold, new Vector2(rx + 10, ry + 12), Fit($"{SimTime.Clock(it.Tick)} {it.Goal} — {it.Action}", colW - 10, Ui.TextTiny, Fonts.Bold), Ui.TextTiny, Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 10, ry + 25), Fit($"[{it.State}] {it.Why}", colW - 10, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
            ry += 30;
        }
        if (f.Plan.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 12), "문턱을 넘을 것 같은 값이 없다", Ui.TextTiny, Palette.TextMuted); ry += 18; }
        ry += 8;
        SectionTitle(rx, ry + 10, $"우주 예보 · 대재난 — 맞음 {a.ForecastHits} · 헛예보 {a.ForecastMisses} · EMP {a.Emps}");
        ry += 18;
        foreach (var fc in a.SpaceForecasts.AsEnumerable().Reverse().Take(4))
        {
            string st = !fc.Graded ? $"기다림 ({Math.Max(0, (fc.Due - w.Tick) / (float)SimTime.Minutes(1)):0}분)" : fc.Hit ? "맞았다" : "헛예보";
            Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 12), Fit($"{SimTime.Clock(fc.Tick)} {fc.Name} — {st}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, !fc.Graded ? Palette.Warning : fc.Hit ? Palette.Good : Palette.Danger);
            ry += 14;
        }
        ry += 6;
        Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 12), Fit($"문 · 식단: 원격 열기 {a.Passes} · 막음 {a.Denials} · 문 감지기 고장 {a.DoorBlinds} (안 것 {a.DoorsKnown}) · 배급 앞당김 {a.RationLeads} · 식단 불평 {a.MealGripes}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
    }
}
