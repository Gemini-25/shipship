using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.20 ⑥ 관제 화면 "지휘" 탭 (읽기만 — 완전 관전): 위 — 주 컴퓨터의 몸(본체 · 예비 연산기 · 비상 전지 · 열 · 데이터선 · 절전)과 품질 · 성격.
/// 왼쪽 — 컴퓨터가 믿는 배 지도(믿음 색 · 닿는 길 · 믿음≠실제 · 명령선이 컴퓨터실에서 대상까지 흐른다) · 전력 흐름도(원자로 · 배터리 · 보조 발전기 → 회로 A~D → 필수 · 생활 · 작업 · 편의).
/// 오른쪽 — 견줘 본 판단 타임라인(안마다 막대 · 고른 안 · 막힌 안 · 몇 분 뒤 결과) · 명령선 목록(어느 밸브 · 문 · 사람 · 로봇에 무엇을 · 지금 어떻게).
/// </summary>
public partial class Hud
{
    private static readonly Color CmdRemote = new("#7cc4ff");
    private static readonly Color CmdHands = new("#f2c66d");
    private static readonly Color CmdRobot = new("#6ee7b7");
    private static readonly Color Glass = new(0.06f, 0.09f, 0.13f, 0.72f);

    private static Color OrderColor(ComputerOrder o) => o.Target switch { CmdTarget.Crew => CmdHands, CmdTarget.Robot or CmdTarget.Drone => CmdRobot, _ => CmdRemote };

    private static Color StateColor(string s) => s switch { "끝" => Palette.Good, "실패" => Palette.Danger, "취소" => Palette.TextMuted, "하는 중" => Palette.Accent, _ => CmdHands };

    /// <summary>관제 화면 "지휘" 탭 — v16.26 지금 계획을 읽는 화면: 왼쪽 계획 · 읽는 값, 오른쪽 믿는 배 · 전력 · 견준 판단 · 명령선.</summary>
    private void DrawCommandTab(Rect2 card, float x, float right, float y)
    {
        var a = _world.Automation;
        y = DrawCoreStrip(x, right, y);
        float bottom = card.End.Y - 14f;
        float colW = (right - x) * 0.54f - 8f;
        float lx = x, rx = x + colW + 16f;
        var rd = ComputerReadout.Read(_world);
        // 왼쪽: 지금 계획 (위) · 읽는 값 (아래)
        float planH = Mathf.Max(150f, (bottom - y) * 0.58f);
        var book = a.RecoveryOrNull;
        UiKit.Header(this, lx, lx + colW, y + 10, "지금 계획", $"열린 {rd.Open} · 세움 {book?.Made ?? 0} · 해냄 {book?.Succeeded ?? 0} · 고쳐 짬 {(book?.Revised ?? 0) + (book?.Replans ?? 0)}", "target");
        DrawPlanPanel(new Rect2(lx, y + 20, colW, planH - 24f), rd);
        float ry = y + planH;
        var pr = a.ProbeOrNull;
        UiKit.Header(this, lx, lx + colW, ry + 10, "사실 · 추측 · 예측", pr != null && pr.Checks > 0 ? $"확인 {pr.Checks}번 · 맞힘 {pr.Right} · 틀림 {pr.Wrong}" : null, "sensor");
        DrawReadings(new Rect2(lx, ry + 20, colW, bottom - ry - 20f), rd);
        // 오른쪽: 믿는 배 · 전력 흐름 · 견준 판단 · 명령선
        float h = bottom - y;
        float mapH = h * 0.34f, flowH = h * 0.2f, tlH = h * 0.24f;
        SectionTitle(rx, y + 10, "컴퓨터가 믿는 배 — 명령이 흐르는 길");
        DrawBeliefMap(new Rect2(rx, y + 20, right - rx, mapH - 24f));
        float py = y + mapH;
        SectionTitle(rx, py + 10, $"전력 흐름 — {a.Triage.Mode}" + (a.Triage.Plan != "" ? $" · {a.Triage.Plan}" : ""));
        DrawPowerFlow(new Rect2(rx, py + 20, right - rx, flowH - 24f));
        float ty = py + flowH;
        var fs = a.Foresee;
        SectionTitle(rx, ty + 10, $"한 수 판단 — {fs.Decisions}번 · 맞음 {fs.Right} · 틀림 {fs.Wrong}");
        DrawTimeline(new Rect2(rx, ty + 20, right - rx, tlH - 24f));
        float oy = ty + tlH;
        var cmd = a.Command;
        var fl = _world.Fleet;
        SectionTitle(rx, oy + 10, $"명령선 — 원격 {cmd.Remote} · 사람 {cmd.Hands} · 로봇 {cmd.RobotOrders} · 깨움 {cmd.Woken} · 함대 {fl.Mode}");
        DrawOrders(new Rect2(rx, oy + 20, right - rx, bottom - oy - 20f));
    }

    /// <summary>몸 · 품질 · 성격 두 줄 (높이를 돌려준다).</summary>
    private float DrawCoreStrip(float x, float right, float y)
    {
        var w = _world;
        var a = w.Automation;
        var core = a.Core;
        var m = a.Computer;
        var strip = new Rect2(x, y, right - x, 46f);
        Gfx.RoundRect(this, strip, Glass, 8, new Color(1, 1, 1, 0.06f));
        float cx = x + 12, cy = y + 15;
        // 본체
        float temp = m?.Body.Room.Air.Temperature ?? 20f;
        var mainCol = !a.MainOnline ? Palette.Danger : core.SafeMode ? Palette.Warning : Palette.Good;
        DrawRack(new Vector2(cx + 7, cy), mainCol, a.MainOnline, core.SafeMode ? 0.35f : 1f);
        string main = !a.MainOnline ? (a.Rebooting ? $"본체 다시 켜는 중 ({ShipCore.StageName(core.RebootStage)})" : "본체 멎음") : core.SafeMode ? $"본체 느리게 ({temp:0}℃)" : $"본체 돈다 ({temp:0}℃)";
        Gfx.Text(this, Fonts.Bold, new Vector2(cx + 18, cy + 4), main, 10, mainCol);
        cx += 24 + Gfx.Width(Fonts.Bold, main, 10) + 14;
        // 예비 연산기
        var bCol = core.BackupCore ? Palette.Warning : Palette.TextMuted;
        DrawRack(new Vector2(cx + 7, cy), bCol, core.BackupCore, 0.5f);
        string backup = core.BackupCore ? $"예비 연산기가 붙잡는 중 {(w.Tick - core.BackupSince) / (float)SimTime.Minutes(1):0}분" : "예비 연산기 대기";
        Gfx.Text(this, Fonts.Body, new Vector2(cx + 18, cy + 4), backup, 10, bCol);
        cx += 24 + Gfx.Width(Fonts.Body, backup, 10) + 14;
        // 비상 전지
        float ups = core.Ups < 0 ? core.UpsCapacity : core.Ups;
        var uCol = core.OnUps ? Palette.Warning : Palette.TextDim;
        var cell = new Rect2(cx, cy - 6, 22, 11);
        DrawRect(cell, new Color(1, 1, 1, 0.05f));
        DrawRect(new Rect2(cell.Position, new Vector2(cell.Size.X * Mathf.Clamp(ups / Mathf.Max(1f, core.UpsCapacity), 0f, 1f), cell.Size.Y)), uCol.WithAlpha(core.OnUps ? 0.6f + 0.3f * Mathf.Sin(_time * 5f) : 0.7f));
        DrawRect(cell, uCol, false, 1f);
        DrawRect(new Rect2(cell.End.X, cy - 3, 2, 5), uCol);
        string upsText = $"비상 전지 {ups:0}분" + (core.OnUps ? " · 쓰는 중" : "");
        Gfx.Text(this, Fonts.Body, new Vector2(cx + 28, cy + 4), upsText, 10, uCol);
        cx += 32 + Gfx.Width(Fonts.Body, upsText, 10) + 12;
        // 데이터선 · 무선
        int live = 0, linked = 0, wireless = 0;
        foreach (var r in w.Ship.LiveRooms) { live++; int reach = core.Reach(r); if (reach == 2) linked++; else if (reach == 1) wireless++; }
        DrawRing(new Vector2(cx + 7, cy), 6f, linked / (float)Math.Max(1, live), wireless / (float)Math.Max(1, live));
        string net = $"데이터선 {linked}/{live}방" + (wireless > 0 ? $" · 무선만 {wireless}" : "");
        Gfx.Text(this, Fonts.Body, new Vector2(cx + 18, cy + 4), net, 10, linked < live ? Palette.Warning : Palette.TextDim);
        cx += 24 + Gfx.Width(Fonts.Body, net, 10) + 12;
        if (core.SelfSaving && cx < right - 80)
            Gfx.Pill(this, Fonts.Bold, new Vector2(cx + 34, cy), "제 연산 줄임", 9, Palette.Warning, Palette.Warning.WithAlpha(0.14f));
        // 둘째 줄: 품질 · 성격
        var ch = a.Character;
        string quality = $"정확 {core.Accuracy * 100:0}% · 판단 {core.Speed:0.0}배 빠르기 · 한 번에 사고 {core.Concurrency}건 · {core.Horizon:0}분 앞까지 내다봄";
        Gfx.Text(this, Fonts.Body, new Vector2(x + 12, y + 37), Fit(quality, (right - x) * 0.52f, 10, Fonts.Body), 10, Palette.TextDim);
        string temper = $"성격 {ch.Temper} · {ch.Tilt}" + (ch.Shifts.LastOrDefault() is var (tick, text, _, _) && text != null ? $" — {text}" : "");
        DrawTemper(new Vector2(x + 12 + (right - x) * 0.54f, y + 33), ch.Caution, ch.PeopleTilt);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 26 + (right - x) * 0.54f, y + 37), Fit(temper, (right - x) * 0.44f - 20, 10, Fonts.Body), 10, CmdHands);
        return y + 52f;
    }

    /// <summary>서버 랙: 세 칸 · 불빛이 깜빡인다 (느리게 돌면 천천히).</summary>
    private void DrawRack(Vector2 c, Color col, bool on, float pace)
    {
        var r = new Rect2(c - new Vector2(6, 7), new Vector2(12, 14));
        DrawRect(r, new Color(0.08f, 0.1f, 0.14f, 0.9f));
        DrawRect(r, col.WithAlpha(0.7f), false, 1f);
        for (int k = 0; k < 3; k++)
        {
            DrawLine(new Vector2(r.Position.X + 2, r.Position.Y + 3.5f + k * 4), new Vector2(r.End.X - 4, r.Position.Y + 3.5f + k * 4), col.WithAlpha(0.35f), 1f);
            float blink = on ? 0.5f + 0.5f * Mathf.Sin(_time * 6f * pace + k * 1.7f) : 0.15f;
            DrawCircle(new Vector2(r.End.X - 2.5f, r.Position.Y + 3.5f + k * 4), 1f, col.WithAlpha(blink), true, -1f, true);
        }
    }

    /// <summary>데이터선 고리: 실선 몫(데이터선) · 점선 몫(무선) · 나머지 빈칸.</summary>
    private void DrawRing(Vector2 c, float rad, float linked, float wireless)
    {
        DrawArc(c, rad, 0f, Mathf.Tau, 24, new Color(1, 1, 1, 0.08f), 2f, true);
        float a0 = -Mathf.Pi / 2f, a1 = a0 + Mathf.Tau * linked;
        if (linked > 0f) DrawArc(c, rad, a0, a1, 24, CmdRemote, 2f, true);
        int n = (int)(wireless * 12f);
        for (int k = 0; k < n; k++) { float t = a1 + (k + 0.5f) / 12f * Mathf.Tau; DrawCircle(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * rad, 0.9f, Palette.Warning, true, -1f, true); }
        DrawCircle(c, 1.6f, CmdRemote.WithAlpha(0.6f + 0.4f * Mathf.Sin(_time * 3f)), true, -1f, true);
    }

    /// <summary>성격 두 축 점: 가로 신중(왼쪽)↔과감(오른쪽) · 세로 사람 우선(위)↔배 우선(아래).</summary>
    private void DrawTemper(Vector2 c, float caution, float people)
    {
        var box = new Rect2(c - new Vector2(6, 6), new Vector2(12, 12));
        DrawRect(box, new Color(1, 1, 1, 0.04f));
        DrawLine(new Vector2(c.X, box.Position.Y), new Vector2(c.X, box.End.Y), new Color(1, 1, 1, 0.12f), 1f);
        DrawLine(new Vector2(box.Position.X, c.Y), new Vector2(box.End.X, c.Y), new Color(1, 1, 1, 0.12f), 1f);
        var p = c + new Vector2(Mathf.Clamp(-caution, -1f, 1f) * 5f, Mathf.Clamp(-people, -1f, 1f) * 5f);
        DrawCircle(p, 2.2f, CmdHands, true, -1f, true);
    }

    // ───────────────────────── 컴퓨터가 믿는 배 ─────────────────────────

    private void DrawBeliefMap(Rect2 r)
    {
        var w = _world;
        var a = w.Automation;
        Gfx.RoundRect(this, r, Glass, 8, new Color(1, 1, 1, 0.05f));
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var room in w.Ship.LiveRooms)
            foreach (var c in room.Cells) { minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); }
        if (minX > maxX) return;
        float pad = 8f;
        float s = Mathf.Min((r.Size.X - pad * 2) / (maxX - minX + 1), (r.Size.Y - pad * 2 - 12f) / (maxY - minY + 1));
        var origin = r.Position + new Vector2(pad + ((r.Size.X - pad * 2) - s * (maxX - minX + 1)) * 0.5f, pad);
        Vector2 P(float cx, float cy) => origin + new Vector2((cx - minX) * s, (cy - minY) * s);
        var centers = new Dictionary<int, Vector2>();
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Cells.Count == 0) continue;
            var b = a.Belief.Of(room);
            int reach = a.Core.Reach(room);
            Color col;
            if (reach == 0) col = new Color(0.3f, 0.32f, 0.36f, 0.35f);
            else if (b.Fire) col = Palette.Danger.WithAlpha(0.45f + 0.25f * Mathf.Sin(_time * 5f));
            else if (b.Pressure < 85f) col = new Color("#5ec8e6").WithAlpha(Mathf.Clamp(0.25f + (85f - b.Pressure) / 100f, 0.25f, 0.8f));
            else if (b.O2 < 18f) col = new Color("#c79be0").WithAlpha(0.5f);
            else col = Palette.Room(room.Kind).WithAlpha(room.Type == RoomType.Corridor ? 0.14f : 0.28f);
            float sx = 0, sy = 0;
            foreach (var c in room.Cells)
            {
                DrawRect(new Rect2(P(c.X, c.Y), new Vector2(Mathf.Max(1f, s - 0.4f), Mathf.Max(1f, s - 0.4f))), col);
                sx += c.X; sy += c.Y;
            }
            var mid = P(sx / room.Cells.Count + 0.5f, sy / room.Cells.Count + 0.5f);
            centers[room.Id] = mid;
            if (room.Type == RoomType.Corridor) continue;
            // 닿는 길: 무선만이면 작은 전파 · 못 보면 물음표
            if (reach == 1) for (int k = 1; k <= 2; k++) DrawArc(mid + new Vector2(0, 3), 2.5f * k, -Mathf.Pi * 0.8f, -Mathf.Pi * 0.2f, 6, Palette.Warning.WithAlpha(0.8f), 1f, true);
            else if (reach == 0) Gfx.TextCentered(this, Fonts.Bold, mid, "?", 9, Palette.TextMuted);
            // 믿는 사람 수 (점) · 믿음≠실제 (깜빡이는 표)
            int ppl = Math.Min(6, b.People);
            for (int k = 0; k < ppl; k++) DrawCircle(mid + new Vector2((k - (ppl - 1) * 0.5f) * 3.2f, -4f), 1.2f, Palette.Text.WithAlpha(0.85f), true, -1f, true);
            if (reach > 0 && a.Belief.Diverged(room, out _)) Gfx.TextCentered(this, Fonts.Bold, mid + new Vector2(0, 5), "≠", 10, Palette.Danger.WithAlpha(0.6f + 0.4f * Mathf.Sin(_time * 7f)));
            else if (b.Fault != SensorFault.None) DrawCircle(mid + new Vector2(5, 4), 1.6f, Palette.Warning, true, -1f, true);
        }
        DrawPlanMarks(centers); // v16.26 끊긴 구역 · 계획 걸음
        // 컴퓨터실: 칩
        var home = a.Computer?.Body.Room;
        if (home == null || !centers.TryGetValue(home.Id, out var hc)) return;
        CommandIcons.Draw(this, CmdTarget.Self, hc, 5f, a.MainOnline ? CmdRemote : a.Core.BackupCore ? Palette.Warning : Palette.Danger, _time);
        // 명령선: 열린 명령 · 6분 안의 명령이 컴퓨터실에서 대상까지 흐른다
        int drawn = 0;
        for (int i = a.Command.Lines.Count - 1; i >= 0 && drawn < 14; i--)
        {
            var o = a.Command.Lines[i];
            if (!o.Open && w.Tick - o.Tick > SimTime.Minutes(6)) continue;
            if (o.RoomId < 0 || !centers.TryGetValue(o.RoomId, out var to)) continue;
            drawn++;
            var col = OrderColor(o);
            float age = (w.Tick - o.Tick) / (float)SimTime.Minutes(6);
            float alpha = o.Open ? 0.9f : Mathf.Clamp(1f - age, 0.15f, 0.7f);
            var from = hc;
            var mid = (from + to) * 0.5f + (to - from).Orthogonal().Normalized() * Mathf.Min(14f, (to - from).Length() * 0.18f) * (i % 2 == 0 ? 1f : -1f);
            var pts = new Vector2[13];
            for (int k = 0; k <= 12; k++) { float t = k / 12f; pts[k] = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * mid + t * t * to; }
            if (o.Remote) DrawPolyline(pts, col.WithAlpha(alpha * 0.55f), 1.2f, true);
            else for (int k = 0; k < 12; k += 2) DrawLine(pts[k], pts[k + 1], col.WithAlpha(alpha * 0.6f), 1.2f, true);
            // 흐르는 빛 (열린 명령만)
            if (o.Open)
            {
                float ph = Mathf.PosMod(_time * 0.7f + i * 0.13f, 1f);
                int seg = Math.Min(11, (int)(ph * 12f));
                float f = ph * 12f - seg;
                DrawCircle(pts[seg].Lerp(pts[seg + 1], f), 1.8f, col, true, -1f, true);
            }
            CommandIcons.Draw(this, o.Target, to + new Vector2(0, -9), 3.4f, col.WithAlpha(Mathf.Max(0.4f, alpha)), _time);
        }
        // 범례
        float ly = r.End.Y - 6f;
        Gfx.Text(this, Fonts.Body, new Vector2(r.Position.X + 8, ly), "실선 원격 · 점선 사람 손 · 초록 로봇 · 전파 무선만 · ? 못 봄 · ≠ 믿음과 실제가 다름", 9, Palette.TextMuted);
    }

    // ───────────────────────── 전력 흐름도 ─────────────────────────

    private void DrawPowerFlow(Rect2 r)
    {
        var w = _world;
        var a = w.Automation;
        var p = w.Power;
        if (r.Size.Y < 60f) return;
        Gfx.RoundRect(this, r, Glass, 8, new Color(1, 1, 1, 0.05f));
        float x0 = r.Position.X + 10, y0 = r.Position.Y + 8, h = r.Size.Y - 16;
        // 회로별 수요 · 단계별 수 (끈 것)
        var cDemand = new float[PowerGrid.CircuitCount];
        var tierOn = new int[4];
        var tierOff = new int[4];
        foreach (var m in w.Ship.Machines)
        {
            if (m.Body.Room.Detached || m.Spec.PowerDraw <= 0f) continue;
            int c = Math.Clamp(m.Body.Room.Circuit, 0, PowerGrid.CircuitCount - 1);
            if (m.Powered) cDemand[c] += m.Demand;
            int rank = PowerTriage.Rank(w, m);
            int t = rank >= 9 ? 0 : rank >= 6 ? 1 : rank >= 4 ? 2 : 3;
            if (m.Parked || a.Triage.Parked.Contains(m.Body.Id)) tierOff[t]++; else tierOn[t]++;
        }
        bool[] tripped = new bool[PowerGrid.CircuitCount];
        if (w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel)
            foreach (var f in panel.Faults) if (f.Kind == FaultKind.BreakerTrip && f.Circuit >= 0 && f.Circuit < tripped.Length) tripped[f.Circuit] = true;
        // 공급원 셋 (왼쪽)
        float sw = Mathf.Min(118f, r.Size.X * 0.3f), sh = Mathf.Min(30f, (h - 8) / 3f);
        var sources = new (string name, string value, float frac, bool on, Color col)[]
        {
            ("원자로", p.ReactorOnline ? $"{p.ReactorOutput:0}kW" + (a.ReactorCap < 0.99f ? $" (상한 {a.ReactorCap * 100:0}%)" : "") : "멎음", p.ReactorRated > 0 ? p.ReactorOutput / p.ReactorRated : 0f, p.ReactorOnline, p.ReactorOnline ? Palette.Good : Palette.Danger),
            ("배터리", $"{p.BatteryPercent * 100:0}% " + (p.BatteryFlow < -0.05f ? $"↓{-p.BatteryFlow:0.#}kW" : p.BatteryFlow > 0.05f ? $"↑{p.BatteryFlow:0.#}kW" : ""), p.BatteryPercent, p.BatteryFlow < -0.05f, p.BatteryPercent < 0.2f ? Palette.Danger : p.BatteryFlow < -0.05f ? Palette.Warning : Palette.Accent),
            ("보조 발전기", p.AuxRunning ? $"{p.AuxOutput:0}kW · 연료 {p.AuxFuel:0.#}시간" : a.Triage.AuxNeedsHands ? "시동 안 걸림 — 손으로" : "쉼", p.AuxRunning ? 1f : 0f, p.AuxRunning, p.AuxRunning ? Palette.Good : a.Triage.AuxNeedsHands ? Palette.Danger : Palette.TextMuted),
        };
        float busX = x0 + sw + 22f;
        for (int i = 0; i < sources.Length; i++)
        {
            var (name, value, frac, on, col) = sources[i];
            var box = new Rect2(x0, y0 + i * (sh + 4), sw, sh);
            Gfx.RoundRect(this, box, new Color(1, 1, 1, 0.03f), 5, col.WithAlpha(0.5f));
            DrawSourceGlyph(new Vector2(box.Position.X + 10, box.Position.Y + sh * 0.5f), i, col, on);
            Gfx.Text(this, Fonts.Bold, new Vector2(box.Position.X + 22, box.Position.Y + 12), name, 10, Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(box.Position.X + 22, box.Position.Y + 24), Fit(value, sw - 26, 9, Fonts.Body), 9, col);
            DrawRect(new Rect2(box.Position.X + 4, box.End.Y - 3, (sw - 8) * Mathf.Clamp(frac, 0f, 1f), 2), col.WithAlpha(0.6f));
            var a0 = new Vector2(box.End.X, box.Position.Y + sh * 0.5f);
            FlowLine(a0, new Vector2(busX, a0.Y), col, on);
        }
        // 모선
        float busTop = y0 + 4, busBot = y0 + 3 * (sh + 4) - 8;
        DrawLine(new Vector2(busX, busTop), new Vector2(busX, busBot), Palette.TextDim.WithAlpha(0.6f), 2f);
        // 회로 넷 (가운데)
        float cx0 = busX + 18f, cw = Mathf.Min(96f, (r.End.X - cx0) * 0.5f), ch = (busBot - busTop) / PowerGrid.CircuitCount;
        for (int i = 0; i < PowerGrid.CircuitCount; i++)
        {
            var box = new Rect2(cx0, busTop + i * ch + 1, cw, ch - 3);
            bool jumper = p.FeedingJumper(i) != null && !p.CircuitLive[i];
            string st = tripped[i] ? "차단" : p.ManualOff[i] ? "끔" : !p.CircuitFed[i] ? "죽음" : jumper ? "예비 배선" : "삶";
            var col = tripped[i] || !p.CircuitFed[i] && !p.ManualOff[i] ? Palette.Danger : p.ManualOff[i] ? Palette.TextMuted : jumper ? CmdHands : Palette.Good;
            bool on = p.CircuitFed[i] && !tripped[i];
            FlowLine(new Vector2(busX, box.Position.Y + box.Size.Y * 0.5f), new Vector2(box.Position.X, box.Position.Y + box.Size.Y * 0.5f), col, on);
            Gfx.RoundRect(this, box, col.WithAlpha(0.08f), 4, col.WithAlpha(0.55f));
            Gfx.Text(this, Fonts.Bold, new Vector2(box.Position.X + 6, box.Position.Y + box.Size.Y * 0.5f + 4), PowerGrid.CircuitName(i), 11, col);
            Gfx.Text(this, Fonts.Body, new Vector2(box.Position.X + 20, box.Position.Y + box.Size.Y * 0.5f + 4), Fit($"{st} · {cDemand[i]:0.#}kW", cw - 24, 9, Fonts.Body), 9, Palette.TextDim);
            if (tripped[i]) DrawBreakerGlyph(new Vector2(box.End.X - 7, box.Position.Y + box.Size.Y * 0.5f), Palette.Danger);
        }
        // 필수 → 편의 (오른쪽): 도는 것 · 컴퓨터가 끈 것
        float tx = cx0 + cw + 14f, tw = r.End.X - tx - 8f;
        string[] tiers = { "필수", "생활", "작업", "편의" };
        Color[] tc = { Palette.Good, Palette.Accent, CmdHands, Palette.TextDim };
        for (int t = 0; t < 4 && tw > 40f; t++)
        {
            float ty = busTop + t * ch + ch * 0.5f;
            int total = tierOn[t] + tierOff[t];
            Gfx.Text(this, Fonts.Bold, new Vector2(tx, ty + 4), tiers[t], 10, tc[t]);
            float bx = tx + 30, bw = Mathf.Max(10f, tw - 30);
            var bar = new Rect2(bx, ty - 3, bw, 7);
            DrawRect(bar, new Color(1, 1, 1, 0.05f));
            if (total > 0)
            {
                DrawRect(new Rect2(bar.Position, new Vector2(bw * tierOn[t] / total, 7)), tc[t].WithAlpha(0.65f));
                for (int k = 0; k < tierOff[t]; k++) // 끈 것: 빗금 칸
                {
                    float fx = bx + bw * (tierOn[t] + k) / total;
                    DrawLine(new Vector2(fx + 1, ty + 3), new Vector2(fx + Mathf.Min(6f, bw / total), ty - 3), Palette.TextMuted, 1f);
                }
            }
            if (tierOff[t] > 0) Gfx.Text(this, Fonts.Body, new Vector2(bx, ty + 13), $"{tierOff[t]}대 내려 둠", 8, Palette.TextMuted);
        }
        // 맨 아래 한 줄: 컴퓨터 자신 · 저출력 운영 · 원격 차단기
        var tr = a.Triage;
        float myKw = a.Computer?.Demand ?? 0f;
        string foot = $"주 컴퓨터 {myKw:0.#}kW" + (a.Core.SelfSaving ? $" (절전 — {a.Core.SelfSavingWhy})" : "") + (p.Brownout ? " · 저출력 운영" : "") +
                      $" · 차단기 원격 {tr.RemoteResets} · 원인 끊음 {tr.CauseCuts} · 붙잡음 {tr.Holds}" + (tr.AuxStarts > 0 ? $" · 발전기 원격 {tr.AuxStarts}" : "");
        Gfx.Text(this, Fonts.Body, new Vector2(x0, r.End.Y - 6), Fit(foot, r.Size.X - 20, 9, Fonts.Body), 9, a.Core.SelfSaving ? Palette.Warning : Palette.TextMuted);
    }

    /// <summary>전기가 흐르는 선: 켜져 있으면 점이 흐른다.</summary>
    private void FlowLine(Vector2 a, Vector2 b, Color col, bool on)
    {
        DrawLine(a, b, col.WithAlpha(on ? 0.5f : 0.15f), on ? 1.6f : 1f);
        if (!on) return;
        for (int k = 0; k < 2; k++)
        {
            float ph = Mathf.PosMod(_time * 0.9f + k * 0.5f + a.Y * 0.013f, 1f);
            DrawCircle(a.Lerp(b, ph), 1.5f, col, true, -1f, true);
        }
    }

    /// <summary>공급원 그림: 원자로(세 궤도) · 배터리(눈금 통) · 보조 발전기(돌아가는 날개).</summary>
    private void DrawSourceGlyph(Vector2 c, int kind, Color col, bool on)
    {
        switch (kind)
        {
            case 0:
                for (int k = 0; k < 3; k++)
                {
                    float rot = (on ? _time * 1.2f : 0f) + k * Mathf.Pi / 3f;
                    var pts = new Vector2[13];
                    for (int j = 0; j <= 12; j++) { float t = j / 12f * Mathf.Tau; var e = new Vector2(Mathf.Cos(t) * 6f, Mathf.Sin(t) * 2.2f); pts[j] = c + e.Rotated(rot); }
                    DrawPolyline(pts, col.WithAlpha(0.8f), 1f, true);
                }
                DrawCircle(c, 1.8f, col, true, -1f, true);
                break;
            case 1:
                DrawRect(new Rect2(c - new Vector2(4, 6), new Vector2(8, 12)), col, false, 1f);
                DrawRect(new Rect2(c + new Vector2(-2, -8), new Vector2(4, 2)), col);
                for (int k = 0; k < 3; k++) DrawRect(new Rect2(c + new Vector2(-2.5f, 3 - k * 3.5f), new Vector2(5, 2)), col.WithAlpha(on && k == (int)(_time * 3f) % 3 ? 0.3f : 0.8f));
                break;
            default:
                DrawArc(c, 6f, 0f, Mathf.Tau, 16, col.WithAlpha(0.7f), 1f, true);
                for (int k = 0; k < 3; k++) { float t = (on ? _time * 8f : 0.3f) + k * Mathf.Tau / 3f; DrawLine(c, c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * 5f, col, 1.4f, true); }
                break;
        }
    }

    /// <summary>떨어진 차단기 손잡이 (아래로 꺾임).</summary>
    private void DrawBreakerGlyph(Vector2 c, Color col)
    {
        DrawRect(new Rect2(c - new Vector2(3, 5), new Vector2(6, 10)), col, false, 1f);
        DrawLine(c, c + new Vector2(0, 4).Rotated(0.5f + 0.15f * Mathf.Sin(_time * 6f)), col, 1.6f, true);
    }

    // ───────────────────────── 견줘 본 판단 타임라인 ─────────────────────────

    private void DrawTimeline(Rect2 r)
    {
        var w = _world;
        var fs = w.Automation.Foresee;
        Gfx.RoundRect(this, r, Glass, 8, new Color(1, 1, 1, 0.05f));
        float x = r.Position.X + 8, right = r.End.X - 8, y = r.Position.Y + 4;
        if (fs.Queue.Count > 1)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 11), "겹친 사고", 10, Palette.Warning);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 52, y + 11), Fit(fs.QueueLine, right - x - 52, 9, Fonts.Body), 9, Palette.TextDim);
            y += 16;
        }
        if (fs.Timeline.Count == 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), "아직 견줘 볼 만한 사고가 없었다 — 사고가 나면 안 두세 가지를 몇 분 앞까지 돌려 보고 고른다", 10, Palette.TextMuted);
            return;
        }
        float barW = (right - x) * 0.42f;
        for (int i = fs.Timeline.Count - 1; i >= 0; i--)
        {
            var d = fs.Timeline[i];
            float need = 16f + 13f * d.Options.Count + 13f;
            if (y + need > r.End.Y - 2) break;
            var rc = d.Score == 1 ? Palette.Good : d.Score == -1 ? Palette.Danger : d.Score == 2 ? Palette.TextDim : Palette.Accent;
            DrawRect(new Rect2(x - 4, y + 3, 2, need - 6), rc.WithAlpha(0.7f));
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), $"{SimTime.Day(d.Tick)}일 {SimTime.Clock(d.Tick)}", 9, Palette.TextMuted);
            DrawKindGlyph(new Vector2(x + 66, y + 8), d.Kind, rc);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 76, y + 12), Fit(d.Title + (d.Rank > 0 ? $" ({d.Rank}번째)" : "") + (d.By != "" && d.By != "주 컴퓨터" ? $" · {d.By}" : ""), right - x - 76, 10, Fonts.Bold), 10, Palette.Text);
            y += 16;
            float max = 0.01f;
            foreach (var o in d.Options) if (o.Allowed) max = Mathf.Max(max, o.Score);
            for (int k = 0; k < d.Options.Count; k++)
            {
                var o = d.Options[k];
                bool pick = k == d.Chosen;
                var oc = !o.Allowed ? Palette.TextMuted : pick ? CmdRemote : Palette.TextDim;
                if (pick) DrawColoredPolygon(new[] { new Vector2(x + 2, y + 4), new Vector2(x + 8, y + 7.5f), new Vector2(x + 2, y + 11) }, CmdRemote);
                Gfx.Text(this, pick ? Fonts.Bold : Fonts.Body, new Vector2(x + 12, y + 11), Fit(o.Name, (right - x) * 0.3f, 9, Fonts.Body), 9, oc);
                float bx = x + 12 + (right - x) * 0.3f + 4;
                var bar = new Rect2(bx, y + 4, barW, 6);
                DrawRect(bar, new Color(1, 1, 1, 0.04f));
                if (o.Allowed) DrawRect(new Rect2(bar.Position, new Vector2(barW * Mathf.Clamp(o.Score / max, 0.02f, 1f), 6)), oc.WithAlpha(pick ? 0.85f : 0.45f));
                else for (int j = 0; j < 8; j++) DrawLine(new Vector2(bx + j * barW / 8f, y + 10), new Vector2(bx + j * barW / 8f + 4, y + 4), Palette.TextMuted.WithAlpha(0.5f), 1f);
                string tail = o.Allowed ? $"다칠 사람 {o.People:0.#} · 잃는 것 {o.Ship:0.##}" : o.Blocked;
                Gfx.Text(this, Fonts.Body, new Vector2(bx + barW + 6, y + 11), Fit(tail, right - bx - barW - 6, 9, Fonts.Body), 9, oc);
                y += 13;
            }
            string res = d.Score != 0 ? d.Result : $"{Math.Max(0, (d.GradeAt - w.Tick) / (float)SimTime.Minutes(1)):0}분 뒤 채점 — {d.Reason}";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 12, y + 10), Fit((d.Score != 0 ? "→ " : "") + res, right - x - 12, 9, Fonts.Body), 9, rc);
            y += 15;
        }
    }

    /// <summary>사고 종류 그림: 파공(갈라진 원) · 불(불꽃) · 정전(번개) · 차단기(손잡이).</summary>
    private void DrawKindGlyph(Vector2 c, string kind, Color col)
    {
        switch (kind)
        {
            case "구멍 막기": // v16.25 막을 방법 견줌
            case "파공":
                DrawArc(c, 4.5f, 0f, Mathf.Tau, 14, col, 1.2f, true);
                DrawPolyline(new[] { c + new Vector2(-2, -4), c + new Vector2(0, -1), c + new Vector2(-1, 1), c + new Vector2(2, 4) }, col, 1.2f, true);
                break;
            case "불 끄기": // v16.25 끌 방법 견줌
            case "불":
                float f = Mathf.Sin(_time * 9f) * 0.8f;
                DrawColoredPolygon(new[] { c + new Vector2(0, -5 + f), c + new Vector2(3.5f, 1), c + new Vector2(0, 4), c + new Vector2(-3.5f, 1) }, col.WithAlpha(0.85f));
                break;
            case "정전":
                DrawPolyline(new[] { c + new Vector2(1.5f, -5), c + new Vector2(-2, 0.5f), c + new Vector2(1.5f, 0.5f), c + new Vector2(-1.5f, 5) }, col, 1.4f, true);
                break;
            default:
                DrawBreakerGlyph(c, col);
                break;
        }
    }

    // ───────────────────────── 명령선 목록 ─────────────────────────

    private void DrawOrders(Rect2 r)
    {
        var w = _world;
        var cmd = w.Automation.Command;
        Gfx.RoundRect(this, r, Glass, 8, new Color(1, 1, 1, 0.05f));
        float x = r.Position.X + 8, right = r.End.X - 8, y = r.Position.Y + 4;
        if (cmd.Lines.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), "아직 내린 명령이 없다", 10, Palette.TextMuted); return; }
        // 열린 명령 먼저, 그다음 최근
        var rows = cmd.Lines.Where(o => o.Open).Reverse().Concat(cmd.Lines.Where(o => !o.Open).Reverse()).Take(40);
        foreach (var o in rows)
        {
            if (y + 26 > r.End.Y) break;
            var col = OrderColor(o);
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 11), SimTime.Clock(o.Tick), 9, Palette.TextMuted);
            CommandIcons.Draw(this, o.Target, new Vector2(x + 44, y + 7), 4f, col, _time);
            var chip = Gfx.Pill(this, Fonts.Bold, new Vector2(right - 22, y + 7), o.State, 8, StateColor(o.State), StateColor(o.State).WithAlpha(0.12f));
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 54, y + 11), Fit(o.What, chip.Position.X - x - 60, 10, Fonts.Bold), 10, o.Open ? Palette.Text : Palette.TextDim);
            string why = o.Why + (o.Result != "" ? $" → {o.Result}" : "") + (o.By != "주 컴퓨터" ? $" ({o.By})" : "");
            Gfx.Text(this, Fonts.Body, new Vector2(x + 54, y + 23), Fit(why, right - x - 58, 9, Fonts.Body), 9, Palette.TextMuted);
            y += 27;
        }
    }
}

/// <summary>명령 대상 그림 (명령선 끝 · 목록): 대상마다 다른 실루엣.</summary>
internal static class CommandIcons
{
    public static void Draw(CanvasItem ci, CmdTarget t, Vector2 c, float s, Color col, float time)
    {
        float k = s / 4f;
        switch (t)
        {
            case CmdTarget.Valve: // 나비 밸브: 원 + 나비꼴 + 손잡이
                ci.DrawArc(c, 3.6f * k, 0f, Mathf.Tau, 12, col, 1f, true);
                ci.DrawColoredPolygon(new[] { c + new Vector2(-3, -2) * k, c, c + new Vector2(-3, 2) * k }, col);
                ci.DrawColoredPolygon(new[] { c + new Vector2(3, -2) * k, c, c + new Vector2(3, 2) * k }, col);
                ci.DrawLine(c + new Vector2(0, -3.6f) * k, c + new Vector2(0, -6) * k, col, 1f);
                break;
            case CmdTarget.Door: // 문: 두 짝 사이 틈
                ci.DrawRect(new Rect2(c - new Vector2(4, 5) * k, new Vector2(3.4f, 10) * k), col, false, 1f);
                ci.DrawRect(new Rect2(c + new Vector2(0.6f, -5) * k, new Vector2(3.4f, 10) * k), col, false, 1f);
                break;
            case CmdTarget.Damper: // 댐퍼: 사각 덕트 안 비스듬한 날 셋
                ci.DrawRect(new Rect2(c - new Vector2(4.5f, 4) * k, new Vector2(9, 8) * k), col, false, 1f);
                for (int i = -1; i <= 1; i++) ci.DrawLine(c + new Vector2(-3, i * 2.5f - 1) * k, c + new Vector2(3, i * 2.5f + 1) * k, col, 1f);
                break;
            case CmdTarget.Breaker: // 차단기: 몸통 + 올린 손잡이
                ci.DrawRect(new Rect2(c - new Vector2(3, 5) * k, new Vector2(6, 10) * k), col, false, 1f);
                ci.DrawLine(c, c + new Vector2(0, -4).Rotated(-0.4f) * k, col, 1.6f);
                break;
            case CmdTarget.Circuit: // 회로: 지그재그 선
                ci.DrawPolyline(new[] { c + new Vector2(-5, 0) * k, c + new Vector2(-3, -3) * k, c + new Vector2(-1, 3) * k, c + new Vector2(1, -3) * k, c + new Vector2(3, 3) * k, c + new Vector2(5, 0) * k }, col, 1f, true);
                break;
            case CmdTarget.Outlet: // 콘센트: 둥근 판 + 구멍 둘
                ci.DrawArc(c, 4.2f * k, 0f, Mathf.Tau, 12, col, 1f, true);
                ci.DrawCircle(c + new Vector2(-1.6f, 0) * k, 0.9f * k, col, true, -1f, true);
                ci.DrawCircle(c + new Vector2(1.6f, 0) * k, 0.9f * k, col, true, -1f, true);
                break;
            case CmdTarget.Generator: // 발전기: 원 + 사인 곡선
                ci.DrawArc(c, 4.5f * k, 0f, Mathf.Tau, 14, col, 1f, true);
                var sp = new Vector2[9];
                for (int i = 0; i <= 8; i++) { float f = i / 8f; sp[i] = c + new Vector2((f - 0.5f) * 6f, -Mathf.Sin(f * Mathf.Tau + time * 4f) * 1.8f) * k; }
                ci.DrawPolyline(sp, col, 1f, true);
                break;
            case CmdTarget.Reactor: // 원자로: 육각 + 가운데 점
                var hex = new Vector2[7];
                for (int i = 0; i <= 6; i++) { float a = i / 6f * Mathf.Tau; hex[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4.6f * k; }
                ci.DrawPolyline(hex, col, 1f, true);
                ci.DrawCircle(c, 1.5f * k, col, true, -1f, true);
                break;
            case CmdTarget.Battery:
                ci.DrawRect(new Rect2(c - new Vector2(3, 4.5f) * k, new Vector2(6, 9) * k), col, false, 1f);
                ci.DrawRect(new Rect2(c + new Vector2(-1.2f, -5.8f) * k, new Vector2(2.4f, 1.3f) * k), col);
                break;
            case CmdTarget.Machine: // 설비: 톱니
                ci.DrawArc(c, 3f * k, 0f, Mathf.Tau, 12, col, 1.2f, true);
                for (int i = 0; i < 6; i++) { float a = i / 6f * Mathf.Tau + time * 0.5f; ci.DrawLine(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3f * k, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4.8f * k, col, 1.4f); }
                break;
            case CmdTarget.Room: // 분전함: 상자 + 번개
                ci.DrawRect(new Rect2(c - new Vector2(4, 4) * k, new Vector2(8, 8) * k), col, false, 1f);
                ci.DrawPolyline(new[] { c + new Vector2(1, -3) * k, c + new Vector2(-1.2f, 0.3f) * k, c + new Vector2(1, 0.3f) * k, c + new Vector2(-1, 3) * k }, col, 1f, true);
                break;
            case CmdTarget.Crew: // 사람: 머리 + 어깨
                ci.DrawCircle(c + new Vector2(0, -2.6f) * k, 1.8f * k, col, true, -1f, true);
                ci.DrawArc(c + new Vector2(0, 3.6f) * k, 3.4f * k, Mathf.Pi, Mathf.Tau, 8, col, 1.4f, true);
                break;
            case CmdTarget.Robot: // 로봇: 네모 머리 + 안테나 + 눈
                ci.DrawRect(new Rect2(c - new Vector2(3.5f, 2.5f) * k, new Vector2(7, 6) * k), col, false, 1f);
                ci.DrawLine(c + new Vector2(0, -2.5f) * k, c + new Vector2(0, -5) * k, col, 1f);
                ci.DrawCircle(c + new Vector2(0, -5.3f) * k, 0.8f * k, col.WithAlpha(0.5f + 0.5f * Mathf.Sin(time * 5f)), true, -1f, true);
                ci.DrawCircle(c + new Vector2(-1.4f, 0.5f) * k, 0.7f * k, col, true, -1f, true);
                ci.DrawCircle(c + new Vector2(1.4f, 0.5f) * k, 0.7f * k, col, true, -1f, true);
                break;
            case CmdTarget.Drone: // 드론: X 팔 + 날개 넷
                ci.DrawLine(c + new Vector2(-3.5f, -3.5f) * k, c + new Vector2(3.5f, 3.5f) * k, col, 1f);
                ci.DrawLine(c + new Vector2(-3.5f, 3.5f) * k, c + new Vector2(3.5f, -3.5f) * k, col, 1f);
                foreach (var d in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    ci.DrawArc(c + d * 3.8f * k, 1.6f * k, time * 12f, time * 12f + Mathf.Pi * 1.2f, 6, col, 1f, true);
                break;
            case CmdTarget.Broadcast: // 방송: 스피커 + 퍼지는 호
                ci.DrawColoredPolygon(new[] { c + new Vector2(-4, -1.5f) * k, c + new Vector2(-2, -1.5f) * k, c + new Vector2(0.5f, -4) * k, c + new Vector2(0.5f, 4) * k, c + new Vector2(-2, 1.5f) * k, c + new Vector2(-4, 1.5f) * k }, col);
                for (int i = 1; i <= 2; i++) ci.DrawArc(c + new Vector2(0.5f, 0) * k, (1.8f + i * 1.6f) * k, -0.8f, 0.8f, 6, col.WithAlpha(0.5f + 0.5f * Mathf.Sin(time * 4f - i)), 1f, true);
                break;
            case CmdTarget.Terminal: // 개인 단말: 세운 판 + 진동 선
                ci.DrawRect(new Rect2(c - new Vector2(2.4f, 4.2f) * k, new Vector2(4.8f, 8.4f) * k), col, false, 1f);
                float jig = Mathf.Sin(time * 20f) * 0.6f;
                ci.DrawLine(c + new Vector2(-4 + jig, -2) * k, c + new Vector2(-4 + jig, 2) * k, col.WithAlpha(0.6f), 1f);
                ci.DrawLine(c + new Vector2(4 - jig, -2) * k, c + new Vector2(4 - jig, 2) * k, col.WithAlpha(0.6f), 1f);
                break;
            default: // 주 컴퓨터 자신: 칩 + 다리
                ci.DrawRect(new Rect2(c - new Vector2(3.5f, 3.5f) * k, new Vector2(7, 7) * k), col, false, 1.2f);
                ci.DrawRect(new Rect2(c - new Vector2(1.5f, 1.5f) * k, new Vector2(3, 3) * k), col.WithAlpha(0.5f + 0.5f * Mathf.Sin(time * 3f)));
                for (int i = -1; i <= 1; i++)
                {
                    ci.DrawLine(c + new Vector2(i * 2f, -3.5f) * k, c + new Vector2(i * 2f, -5.2f) * k, col, 1f);
                    ci.DrawLine(c + new Vector2(i * 2f, 3.5f) * k, c + new Vector2(i * 2f, 5.2f) * k, col, 1f);
                    ci.DrawLine(c + new Vector2(-3.5f, i * 2f) * k, c + new Vector2(-5.2f, i * 2f) * k, col, 1f);
                    ci.DrawLine(c + new Vector2(3.5f, i * 2f) * k, c + new Vector2(5.2f, i * 2f) * k, col, 1f);
                }
                break;
        }
    }
}
