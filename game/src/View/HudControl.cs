using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v20 주 컴퓨터 관제 (아래 탭 "컴퓨터" · F6): 탭 넷 — 지금 · 앞날 · 기록 · 규칙.
///  · 지금: 상태 한 줄(등급 · 부하 · 온도 · 신뢰) · 지금 하는 일 · 진행 중인 대응 · 제안(함장 대신 받기 · 거절) · 지금 계획 · 최근 명령.
///  · 앞날: 사실 · 추측 · 예측 · 몇 시간 뒤 · 한 수 판단 · 전력 흐름.
///  · 기록: 다섯 칸 기록 · 보고 · 일정 · 하루 정비표 · 판단 근거.
///  · 규칙: 권한(갈래마다 조언만 · 제안 · 자동 실행 — 누르면 바꾼다) · 맡은 일(누르면 켜고 끈다) · 등급 · 방침 · 사람과 믿음 · 두뇌.
/// 명령선 · 못 보는 방 · 잘못 아는 방은 창 안 작은 지도 대신 실제 배 위에 그린다 (ShipViewComputerOverlay.cs). 직접 명령은 없다.
/// </summary>
public partial class Hud
{
    public bool ControlOpen { get; set; }
    private Rect2 _controlRect;
    private readonly int[] _ctlSub = new int[4];

    /// <summary>화면 확인용 (--open=computer:0:탭).</summary>
    public int ControlTab { get => _controlTab; set => _controlTab = Math.Clamp(value, 0, 3); }
    /// <summary>화면 확인용: 지금 탭 안의 작은 쪽 (--open=computer:0:탭:쪽).</summary>
    public int ControlSub { get => _ctlSub[_controlTab]; set => _ctlSub[_controlTab] = Math.Max(0, value); }

    public void ToggleControl()
    {
        ControlOpen = !ControlOpen;
        if (ControlOpen) { ChronicleOpen = false; TechOpen = false; PolicyOpen = false; OpenChain(null); }
    }

    private static readonly string[] CtlTabs = { "지금", "앞날", "기록", "규칙" };
    private static readonly string[] CtlIcons = { "computer", "trend-up", "log", "settings" };
    private static readonly string[][] CtlSubs =
    {
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[] { "다섯 칸 기록", "보고 · 일정", "하루 정비표", "판단 근거" },
        new[] { "권한 · 맡은 일", "등급 · 방침", "사람과 믿음", "두뇌" },
    };

    private void DrawControl(Vector2 mouse)
    {
        var a = _world.Automation;
        var card = new Rect2(WinX, WinY, WinW, WinBottom - WinY);
        _controlRect = card;
        Card(card);
        float x = card.Position.X + 16, right = card.End.X - 16;
        float y = DrawCtlHead(card, x, right, mouse);
        // 탭 넷 (그림 + 이름 · 한 단계 큰 글씨)
        float tw = (right - x - 6f * 3) / 4f;
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect2(x + i * (tw + 6f), y, tw, 32f);
            bool on = _controlTab == i, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, on ? Palette.Accent.WithAlpha(0.16f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.025f), Ui.RadiusControl, on ? Palette.Accent.WithAlpha(0.5f) : Ui.PanelEdge);
            float lw = Gfx.Width(Fonts.Bold, CtlTabs[i], Ui.TextLabel);
            float cx = r.GetCenter().X - (lw + 22f) * 0.5f;
            var col = on ? Palette.Accent : hover ? Palette.Text : Palette.TextDim;
            Icons.Draw(this, CtlIcons[i], new Vector2(cx + 8f, r.GetCenter().Y), 16f, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(cx + 22f, r.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextLabel)), CtlTabs[i], Ui.TextLabel, col);
            if (i == 0 && a.Asks.Open.Any()) Gfx.Pill(this, Fonts.Bold, new Vector2(r.End.X - 14f, r.Position.Y + 2f), a.Asks.Open.Count().ToString(), Ui.TextTiny, Palette.Space, Palette.Warning);
            int ti = i;
            _buttons.Add((r, () => _controlTab = ti));
        }
        y += 40f;
        // 기록 · 규칙: 작은 쪽 단추
        var subs = CtlSubs[_controlTab];
        if (subs.Length > 0)
        {
            float sx = x;
            for (int i = 0; i < subs.Length; i++)
            {
                float bw = Gfx.Width(Fonts.Bold, subs[i], Ui.TextBody) + 22f;
                int si = i, tab = _controlTab;
                Button(new Rect2(sx, y, bw, 26f), subs[i], _ctlSub[tab] == i, mouse, () => _ctlSub[tab] = si, Ui.TextBody);
                sx += bw + 6f;
            }
            y += 34f;
        }
        var body = new Rect2(x, y, right - x, card.End.Y - 14f - y);
        switch (_controlTab)
        {
            case 0: DrawCtlNow(body, mouse); break;
            case 1: DrawCtlAhead(body); break;
            case 2:
                switch (_ctlSub[2])
                {
                    case 0: DrawActTable(card, x, right, y - 4f); break;
                    case 1: DrawReports(card, x, right, y - 4f, mouse); break;
                    case 2: DrawMateTab(card, x, right, y - 4f); break;
                    default: DrawReasoning(body); break;
                }
                break;
            default:
                switch (_ctlSub[3])
                {
                    case 0: DrawCtlPowers(body, mouse); break;
                    case 1: DrawCtlLevels(body); break;
                    case 2: DrawPeopleBelief(card, x, right, y - 4f); break;
                    default: DrawBrainTab(card, x, right, y - 4f); break;
                }
                break;
        }
    }

    // ─────────────────────────────── 머리: 늘 보이는 상태 ───────────────────────────────

    /// <summary>등급 배지 · 이름 · 상태 · 부하 · 온도 · 신뢰 (올리면 설명). 다음 줄의 y를 돌려준다.</summary>
    private float DrawCtlHead(Rect2 card, float x, float right, Vector2 mouse)
    {
        var w = _world;
        var a = w.Automation;
        float y = card.Position.Y + 14f;
        LevelBadge(new Vector2(x + 18f, y + 20f), 17f, a);
        var badge = new Rect2(x, y, 38f, 40f);
        Hint(badge, mouse, $"등급 {AutomationSystem.LevelName(a.Level)}", a.Level < 5 ? $"내려간 까닭: {a.LevelWhy}" : "모든 일을 할 수 있다 — 연쇄 예측 · 드론과 로봇 지휘", "computer");
        string state = a.Rebooting ? "재부팅 중" : !a.MainOnline ? (a.Core.BackupCore ? "본체 멎음 · 예비 연산기" : a.BackupActive ? "멎음 · 예비 제어기" : "멎음") : "온라인";
        var stCol = a.MainOnline && !a.Rebooting ? Palette.Good : Palette.Danger;
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 46f, y + 17f), UiKit.Fit($"{a.Voice.Call} · 등급 {AutomationSystem.LevelName(a.Level)}", right - x - 46f - 230f, Ui.TextTitle), Ui.TextTitle, Palette.Text);
        this.Circle(new Vector2(x + 50f, y + 32f), 4f, stCol.WithAlpha(0.6f + 0.4f * Mathf.Sin(_time * 3f)), true, -1f, true);
        string op = a.Operator is CrewMember o ? $"관제석 {o.Name}" : "관제석 비어 있음";
        Gfx.Text(this, Fonts.Body, new Vector2(x + 60f, y + 36f), UiKit.Fit($"{state} · {op}" + (a.Core.SafeMode ? " · 달아올라 느리게" : "") + (a.Core.OnUps ? $" · 비상 전지 {a.Core.Ups:0}분" : ""), right - x - 60f - 230f, Ui.TextBody), Ui.TextBody, stCol);
        // 오른쪽 계기 셋: 부하 · 온도 · 신뢰
        float gx = right - 220f;
        float load = a.Load;
        var lr = new Rect2(gx, y + 2f, 220f, 14f);
        Gfx.Text(this, Fonts.Body, new Vector2(gx, y + 12f), "부하", Ui.TextSmall, Palette.TextDim);
        for (int k = 0; k < 12; k++)
        {
            float f0 = k / 12f;
            var seg = new Rect2(gx + 34f + k * 10f, y + 3f, 8f, 10f);
            var sc = f0 < 0.6f ? Palette.Good : f0 < 0.85f ? new Color("#f2c66d") : Palette.Danger;
            this.Box(seg, load > f0 ? sc.WithAlpha(0.85f) : new Color(1, 1, 1, 0.06f));
        }
        Gfx.TextRight(this, Fonts.Bold, new Vector2(right, y + 12f), $"{load * 100:0}%", Ui.TextSmall, load > 0.9f ? Palette.Danger : Palette.Text);
        Hint(lr, mouse, "연산 부하", a.Suspended.Count > 0 ? $"모자라서 급하지 않은 일 {a.Suspended.Count}가지를 잠시 쉰다" : "맡은 일을 모두 돌리고 있다", "computer");
        float temp = a.Computer?.Body.Room.Air.Temperature ?? 20f;
        var tr = new Rect2(gx, y + 18f, 104f, 16f);
        DrawThermo(new Vector2(gx + 4f, y + 26f), temp);
        Gfx.Text(this, Fonts.Body, new Vector2(gx + 14f, y + 31f), $"{temp:0}℃", Ui.TextSmall, temp > AutomationSystem.OverheatC - 3f ? Palette.Danger : Palette.TextDim);
        Hint(tr, mouse, "컴퓨터실 온도", $"{AutomationSystem.OverheatC:0}℃를 넘으면 느려지고, 더 오르면 멎는다", "temperature");
        float trust = a.Trusts.Average();
        var trr = new Rect2(gx + 110f, y + 18f, 110f, 16f);
        Gfx.Text(this, Fonts.Body, new Vector2(gx + 110f, y + 31f), "신뢰", Ui.TextSmall, Palette.TextDim);
        Gfx.Bar(this, new Rect2(gx + 140f, y + 24f, 50f, 6f), trust, trust < 0.4f ? Palette.Warning : Palette.Accent);
        Gfx.TextRight(this, Fonts.Bold, new Vector2(right, y + 31f), $"{trust * 100:0}%", Ui.TextSmall, Palette.Text);
        Hint(trr, mouse, "사람들의 신뢰", $"승무원이 컴퓨터 말을 얼마나 믿나 — 함장 {w.Command.ComputerTrust * 100:0}%", "people");
        return y + 52f;
    }

    /// <summary>그 자리에 마우스를 올리면 설명 (제목 · 한 줄).</summary>
    private void Hint(Rect2 r, Vector2 mouse, string title, string text, string icon)
    {
        if (!r.HasPoint(mouse)) return;
        var anchor = new Vector2(r.GetCenter().X, r.End.Y);
        _tip = () => UiKit.Tooltip(this, anchor, Screen, title, new List<TipLine> { new(text, Palette.TextDim, icon) }, icon: icon);
    }

    // ─────────────────────────────── 지금 ───────────────────────────────

    private void DrawCtlNow(Rect2 body, Vector2 mouse)
    {
        var w = _world;
        var a = w.Automation;
        float x = body.Position.X, right = body.End.X, y = body.Position.Y, bottom = body.End.Y;
        // 지금 하는 일
        var open = a.Asks.Open.ToList();
        bool hot = a.FireCases.Count > 0 || a.ZoneActive || !a.MainOnline || open.Count > 0;
        var now = new Rect2(x, y, right - x, 48f);
        Gfx.RoundRect(this, now, hot ? new Color(0.18f, 0.12f, 0.05f, 0.7f) : UiKit.Well, Ui.RadiusControl, hot ? Palette.Warning.WithAlpha(0.4f) : Ui.PanelEdge);
        DrawNowGlyph(new Vector2(x + 16f, y + 16f), hot);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 32f, y + 15f), "지금 하는 일", Ui.TextSmall, Palette.TextMuted);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 32f, y + 35f), UiKit.Fit(a.NowLine, right - x - 44f, Ui.TextLabel), Ui.TextLabel, hot ? Palette.Warning : Palette.Text);
        y += 56f;
        // 진행 중인 대응 (소화 수순 · 공기 구역 · 기다리는 격벽)
        foreach (var fc in a.FireCases)
        {
            var room = w.Ship.Rooms[fc.RoomId];
            string step = fc.Stage switch { 0 => "① 소화조", 1 => "② 대피", 2 => fc.Method == "vacuum" ? "③ 진공 소화" : "③ 질식 소화", _ => "④ 다시 가압" };
            string count = fc.Stage == 1 && fc.ExecAt > w.Tick ? $" · {(fc.ExecAt - w.Tick) / (float)SimTime.Minutes(1):0.0}분" : "";
            var c = fc.Stage == 2 ? Palette.Danger : fc.Stage == 1 ? Palette.Warning : Palette.Accent;
            Icons.Draw(this, "fire", new Vector2(x + 9f, y + 9f), 15f, c);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 22f, y + 14f), $"{room.Name} {step}{count}", Ui.TextBody, c);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 220f, y + 14f), UiKit.Fit(fc.Status, right - x - 224f, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
            y += 20f;
        }
        if (a.ZoneActive)
        {
            Icons.Draw(this, "oxygen", new Vector2(x + 9f, y + 9f), 15f, Palette.Warning);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 22f, y + 14f), UiKit.Fit($"공기 구역 — {a.ZoneNote}", right - x - 24f, Ui.TextBody), Ui.TextBody, Palette.Warning);
            y += 20f;
        }
        foreach (var room in w.Ship.Rooms.Where(r => r.LockPendingUntil >= 0))
        {
            float left = (room.LockPendingUntil - w.Tick) / (float)SimTime.Minutes(1);
            Icons.Draw(this, "clock", new Vector2(x + 9f, y + 9f), 15f, Palette.Danger);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 22f, y + 14f), UiKit.Fit($"{room.Name} 격벽 닫기까지 {left:0.0}분 — 안에 {string.Join("·", w.Crew.Where(c => !c.Dead && c.Room == room).Select(c => c.Name))}", right - x - 24f, Ui.TextBody), Ui.TextBody, Palette.Danger);
            y += 20f;
        }
        if (a.FireCases.Count > 0 || a.ZoneActive || w.Ship.Rooms.Any(r => r.LockPendingUntil >= 0)) y += 6f;
        // 제안: 함장 대신 받기 · 거절 (기다리는 것 + 방금 정한 것 하나)
        var just = a.Asks.All.LastOrDefault(p => p.State != ProposalState.Pending && p.DecidedAt >= 0 && w.Tick - p.DecidedAt < SimTime.Minutes(20));
        var cards = open.Take(2).ToList();
        if (cards.Count == 0 && just != null) cards.Add(just);
        foreach (var p in cards)
        {
            if (y + ProposalH + 34f > bottom) break;
            var r = new Rect2(x, y, right - x, ProposalH + 30f);
            DrawProposal(new Rect2(r.Position, new Vector2(r.Size.X, ProposalH)), p, mouse);
            if (p.State == ProposalState.Pending)
            {
                float by = r.Position.Y + ProposalH - 4f;
                Gfx.Text(this, Fonts.Body, new Vector2(x + 12f, by + 19f), "함장 대신 정하기", Ui.TextSmall, Palette.TextMuted);
                int id = p.Id;
                var yes = new Rect2(right - 200f, by + 4f, 94f, 26f);
                var no = new Rect2(right - 100f, by + 4f, 94f, 26f);
                DecideButton(yes, "받기", Palette.Good, mouse, () => Player.Ask(_world, id, true));
                DecideButton(no, "거절", Palette.Danger, mouse, () => Player.Ask(_world, id, false));
            }
            y += (p.State == ProposalState.Pending ? ProposalH + 34f : ProposalH + 6f);
        }
        if (open.Count > cards.Count(p => p.State == ProposalState.Pending))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12f), $"기다리는 제안 {open.Count - 2}건 더", Ui.TextSmall, Palette.Warning);
            y += 18f;
        }
        // 지금 계획 · 최근 명령 (나머지 자리를 나눈다)
        float rest = bottom - y;
        if (rest < 80f) return;
        var rd = ComputerReadout.Read(w);
        var book = a.RecoveryOrNull;
        float planH = Mathf.Clamp(rest * 0.48f, 90f, 230f);
        UiKit.Header(this, x, right, y + 12f, "지금 계획", $"열린 {rd.Open} · 해냄 {book?.Succeeded ?? 0} · 고쳐 짬 {(book?.Revised ?? 0) + (book?.Replans ?? 0)}", "target");
        DrawPlanPanel(new Rect2(x, y + 22f, right - x, planH - 26f), rd);
        y += planH;
        var cmd = a.Command;
        UiKit.Header(this, x, right, y + 12f, "최근 명령", "배 위에 선으로 보인다 — 실선 원격 · 점선 사람 손", "route");
        if (bottom - y - 22f > 40f) DrawOrders(new Rect2(x, y + 22f, right - x, bottom - y - 22f));
    }

    private void DecideButton(Rect2 r, string label, Color col, Vector2 mouse, Action act)
    {
        bool hover = r.HasPoint(mouse);
        Gfx.RoundRect(this, r, col.WithAlpha(hover ? 0.3f : 0.16f), Ui.RadiusControl, col.WithAlpha(hover ? 0.9f : 0.55f));
        Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextLabel)), label, Ui.TextLabel, hover ? Palette.Text : col);
        _buttons.Add((r, act));
    }

    // ─────────────────────────────── 앞날 ───────────────────────────────

    private void DrawCtlAhead(Rect2 body)
    {
        var a = _world.Automation;
        float x = body.Position.X, right = body.End.X, y = body.Position.Y, bottom = body.End.Y;
        float h = bottom - y;
        var rd = ComputerReadout.Read(_world);
        var pr = a.ProbeOrNull;
        float readH = h * 0.28f, foreH = h * 0.36f, tlH = h * 0.18f;
        UiKit.Header(this, x, right, y + 12f, "사실 · 추측 · 예측", pr != null && pr.Checks > 0 ? $"확인 {pr.Checks}번 · 맞힘 {pr.Right} · 틀림 {pr.Wrong}" : null, "sensor");
        DrawReadings(new Rect2(x, y + 22f, right - x, readH - 26f), rd);
        y += readH;
        DrawForesight(new Rect2(x, y, right - x, foreH), x, right, y);
        y += foreH;
        var fs = a.Foresee;
        UiKit.Header(this, x, right, y + 12f, "한 수 판단", $"{fs.Decisions}번 · 맞음 {fs.Right} · 틀림 {fs.Wrong}", "why");
        DrawTimeline(new Rect2(x, y + 22f, right - x, tlH - 26f));
        y += tlH;
        UiKit.Header(this, x, right, y + 12f, "전력 흐름", a.Triage.Mode + (a.Triage.Plan != "" ? $" · {a.Triage.Plan}" : ""), "power");
        if (bottom - y - 22f > 30f) DrawPowerFlow(new Rect2(x, y + 22f, right - x, bottom - y - 22f));
    }

    // ─────────────────────────────── 기록: 판단 근거 ───────────────────────────────

    private void DrawReasoning(Rect2 body)
    {
        var a = _world.Automation;
        float x = body.Position.X, right = body.End.X, y = body.Position.Y;
        UiKit.Header(this, x, right, y + 12f, "판단 근거", a.Level >= 4 ? "왜 그렇게 했나 — 최근부터" : "등급 IV부터 말한다 (지금은 관제석 사람이 있을 때만)", "why");
        y += 26f;
        foreach (var (tick, text) in a.Reasoning.AsEnumerable().Reverse())
        {
            if (y > body.End.Y - 20f) break;
            var lines = WrapText(text, right - x - 56f, Ui.TextBody);
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 13f), SimTime.Clock(tick), Ui.TextSmall, Palette.TextMuted);
            foreach (var l in lines.Take(3)) { Gfx.Text(this, Fonts.Body, new Vector2(x + 50f, y + 13f), l, Ui.TextBody, Palette.Text); y += 17f; }
            y += 4f;
        }
        if (a.Reasoning.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14f), "아직 없다", Ui.TextBody, Palette.TextMuted);
    }

    // ─────────────────────────────── 규칙: 권한 · 맡은 일 ───────────────────────────────

    private static readonly string[] AuthNotes = { "말만 한다 — 사람이 정하고 한다", "함장에게 묻고 받으면 한다", "스스로 하고 알린다" };

    private void DrawCtlPowers(Rect2 body, Vector2 mouse)
    {
        var w = _world;
        var a = w.Automation;
        var au = a.Authority;
        float x = body.Position.X, right = body.End.X, y = body.Position.Y;
        UiKit.Header(this, x, right, y + 12f, "권한", "누르면 바꾼다 — 승무원 회의도 주고 거둔다 · 등급 III 아래면 모두 조언만", "duty");
        y += 24f;
        float nameW = 64f, segW = (right - x - nameW - 12f) / 3f;
        foreach (var d in ComputerAuthority.Domains)
        {
            var g = au.Granted0(d);
            var eff = au.Level(d);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 4f, y + 19f), ComputerAuthority.DomainName(d), Ui.TextLabel, Palette.Text);
            if (eff != g) Gfx.Text(this, Fonts.Body, new Vector2(x + 4f, y + 33f), $"지금 {ComputerAuthority.LevelName(eff)}", Ui.TextTiny, Palette.Warning);
            for (int i = 0; i < 3; i++)
            {
                var lv = (AuthLevel)i;
                var r = new Rect2(x + nameW + i * (segW + 4f), y + 4f, segW, 28f);
                bool on = g == lv, hover = r.HasPoint(mouse);
                var col = i == 2 ? Palette.Good : i == 1 ? Palette.Accent : Palette.TextDim;
                Gfx.RoundRect(this, r, on ? col.WithAlpha(0.2f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.025f), Ui.RadiusControl, on ? col.WithAlpha(0.65f) : Ui.PanelEdge);
                Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), ComputerAuthority.LevelName(lv), Ui.TextBody, on ? col : hover ? Palette.Text : Palette.TextMuted);
                Hint(r, mouse, $"{ComputerAuthority.DomainName(d)} — {ComputerAuthority.LevelName(lv)}", AuthNotes[i], "duty");
                var dd = d;
                if (!on) _buttons.Add((r, () => Player.Authority(_world, dd, lv)));
            }
            y += 40f;
        }
        if (au.Changes.LastOrDefault() is AuthorityChange ch)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12f), UiKit.Fit($"최근: {SimTime.Day(ch.Tick)}일 {ComputerAuthority.DomainName(ch.Domain)} {ComputerAuthority.LevelName(ch.From)} → {ComputerAuthority.LevelName(ch.To)} — {ch.Why}", right - x, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted);
            y += 18f;
        }
        y += 10f;
        // 맡은 일: 그림 칸 — 누르면 켜고 끈다 (끄면 그 일은 사람이 한다)
        UiKit.Header(this, x, right, y + 12f, "맡은 일", $"{a.Modules.Count}/{Enum.GetValues<ComputerModule>().Length}가지 · 누르면 켜고 끈다 · 끄면 사람이 한다", "work");
        y += 24f;
        var all = Enum.GetValues<ComputerModule>().OrderByDescending(AutomationSystem.ModulePriority).ThenBy(m => (int)m).ToList();
        const float cellH = 30f;
        int cols = Math.Max(2, (int)((right - x) / 150f));
        float cw = (right - x - (cols - 1) * 4f) / cols;
        for (int i = 0; i < all.Count; i++)
        {
            var m = all[i];
            var r = new Rect2(x + (i % cols) * (cw + 4f), y + (i / cols) * (cellH + 4f), cw, cellH);
            if (r.End.Y > body.End.Y) break;
            bool has = a.Has(m), resting = has && a.Suspended.Contains(m), hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, has ? Palette.Good.WithAlpha(resting ? 0.06f : 0.12f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), 6f, has ? Palette.Good.WithAlpha(resting ? 0.25f : 0.5f) : Ui.PanelEdge);
            ComputerIcons.Draw(this, m, new Vector2(r.Position.X + 14f, r.GetCenter().Y), 6.5f, has ? ComputerIcons.StateOf(w, m) : ComputerIcons.State.Off, _time);
            Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 28f, r.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), UiKit.Fit(AutomationSystem.ModuleName(m), cw - 54f, Ui.TextBody), Ui.TextBody, has ? Palette.Text : Palette.TextMuted);
            for (int gr = 0; gr < a.Core.Grade(m); gr++) this.Circle(new Vector2(r.End.X - 8f - gr * 5f, r.Position.Y + 7f), 1.6f, new Color("#f2c66d"), true, -1f, true);
            if (resting) Gfx.TextRight(this, Fonts.Body, new Vector2(r.End.X - 6f, r.End.Y - 6f), "쉼", Ui.TextTiny, Palette.Warning);
            Hint(r, mouse, AutomationSystem.ModuleName(m), $"{AutomationSystem.ModuleNote(m)} · 부하 {AutomationSystem.ModuleLoad(m)}" + (resting ? " · 연산이 모자라 잠시 쉰다" : "") + (has ? " — 누르면 거둔다" : " — 누르면 맡긴다"), "work");
            var mm = m;
            _buttons.Add((r, () => Player.Duty(_world, mm, !_world.Automation.Has(mm))));
        }
    }

    // ─────────────────────────────── 규칙: 등급 · 방침 ───────────────────────────────

    private void DrawCtlLevels(Rect2 body)
    {
        var w = _world;
        var a = w.Automation;
        float x = body.Position.X, right = body.End.X, y = body.Position.Y;
        int level = a.Level;
        UiKit.Header(this, x, right, y + 12f, "등급 — 할 수 있는 일", level < 5 ? $"내려간 까닭: {a.LevelWhy}" : null, "computer");
        y += 22f;
        string[] can =
        {
            "감지 · 경보 · 일지",
            "격벽 · 댐퍼 자동 · 물 찬 방 분전함 차단 · 부하 차단",
            "차례로 재가동(기동 전류 ↓) · 새는 방 급수 밸브 원격 잠금",
            "원인 추정과 판단 근거를 말한다 · 전조를 1.5배 먼저 본다",
            "연쇄 예측(배터리 · 산소가 언제) · 드론 · 로봇 지휘",
        };
        for (int i = 1; i <= 5; i++)
        {
            bool on = level >= i;
            var r = new Rect2(x, y, right - x, 24f);
            if (level == i) Gfx.RoundRect(this, r, Palette.Accent.WithAlpha(0.1f), 5);
            this.Circle(new Vector2(x + 8f, y + 12f), 4.5f, on ? Palette.Good : Palette.TextMuted.WithAlpha(0.4f), true, -1f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 20f, y + 16f), AutomationSystem.LevelName(i), Ui.TextLabel, on ? Palette.Text : Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 120f, y + 16f), UiKit.Fit(can[i - 1], right - x - 124f, Ui.TextBody), Ui.TextBody, on ? Palette.TextDim : Palette.TextMuted.WithAlpha(0.6f));
            y += 25f;
        }
        y += 10f;
        // 방침: 컴퓨터에 걸린 재난 · 지휘 방침 (전부는 사람 › 방침)
        var pool = PolicySystem.All.Where(p => p.Area != "자원" && p.Id is not ("election" or "noconfidence" or "minutes")).ToList();
        UiKit.Header(this, x, right, y + 12f, "방침", $"{pool.Count}가지 · 바꾸는 건 회의 (사람 › 방침)", "log");
        y += 22f;
        float colW = (right - x) / 2f;
        for (int i = 0; i < pool.Count; i++)
        {
            if (y > body.End.Y - 60f) break;
            var p = pool[i];
            float px = x + (i % 2) * colW;
            var last = w.Policies.Changes.LastOrDefault(c => c.Id == p.Id);
            bool recent = last != null && w.Tick - last.Tick < SimTime.TicksPerDay;
            Gfx.Text(this, Fonts.Body, new Vector2(px, y + 13f), UiKit.Fit(p.Name, 100f, Ui.TextBody), Ui.TextBody, Palette.TextDim);
            Gfx.Text(this, Fonts.Bold, new Vector2(px + 104f, y + 13f), UiKit.Fit(w.Policies.Option(p.Id), colW - 110f, Ui.TextBody), Ui.TextBody, recent ? Palette.Accent : Palette.Text);
            if (i % 2 == 1 || i == pool.Count - 1) y += 17f;
        }
        y += 8f;
        // 감지기 · 데이터선
        var live = w.Ship.LiveRooms.Where(r => !r.Detached).ToList();
        int blind = live.Count(r => !r.DataLinked);
        int stale = w.Ship.Machines.Count(mm => (w.Tick - mm.LastReading) > SimTime.Minutes(20) && !mm.Body.Room.Detached);
        float cal = w.Ship.Machines.Select(mm => mm.SensorCal).DefaultIfEmpty(1f).Average();
        UiKit.Header(this, x, right, y + 12f, "감지기 · 데이터선", null, "sensor");
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 32f), UiKit.Fit($"데이터선이 끊긴 방 {blind}/{live.Count} · 값이 멈춘 설비 {stale} · 평균 교정 {cal * 100:0}%", right - x, Ui.TextBody), Ui.TextBody, blind > 0 || stale > 3 ? Palette.Warning : Palette.TextDim);
    }
}
