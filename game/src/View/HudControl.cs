using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v12.5 관제 화면 (Y): 주 컴퓨터 등급과 할 수 있는 일, 관제석의 사람, 감압 방침, 판단 근거, 감지기·데이터선 건강, 기다리는 격벽.
/// </summary>
public partial class Hud
{
    public bool ControlOpen { get; set; }
    private Rect2 _controlRect;

    public void ToggleControl()
    {
        ControlOpen = !ControlOpen;
        if (ControlOpen) { ChronicleOpen = false; TechOpen = false; PolicyOpen = false; OpenChain(null); }
    }

    private void DrawControl(Vector2 mouse)
    {
        var w = _world;
        var a = w.Automation;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float wdt = Mathf.Min(_controlTab == 6 ? 980f : 640f, Screen.X - RightColumnWidth - Margin * 3); // v16.20 지휘 탭은 넓게
        float height = Screen.Y - y0 - LogHeight - Margin - 40f;
        var card = new Rect2(x0, y0, wdt, height);
        _controlRect = card;
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        int level = a.Level;
        string state = !a.Present ? "주 컴퓨터 없음" : a.MainOnline ? "온라인" : a.BackupActive ? "멎음 · 예비 제어기" : "멎음";
        UiKit.CardTitle(this, x, right - 70f, y0 + 32, "주 컴퓨터 관제", $"등급 {AutomationSystem.LevelName(level)}" + (level < 5 ? $" · 내려간 까닭: {a.LevelWhy}" : ""), "computer", level < 5 ? Palette.Warning : null); // v16.24
        Button(new Rect2(right - 58, y0 + 12, 58, 26), "Y 닫기", false, mouse, ToggleControl, Ui.TextSmall);
        var col = a.MainOnline ? Palette.Good : Palette.Danger;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y0 + 50), UiKit.Fit($"{state} · {(a.Computer is Machine m ? $"{m.Name} (단계 {m.Tier})" : "-")}" +
            (a.Operator is CrewMember op ? $" · 관제석: {op.Name} (수동 조종 — 좁게 끊고 빨리 되돌린다)" : " · 관제석 비어 있음 (컴퓨터가 보수적으로)"), right - (x), Ui.TextBody, Fonts.Body), Ui.TextBody, a.Operator != null ? Palette.Accent : col);

        // 등급 사다리
        if (DrawControlTabs(card, x, right, y0, mouse)) return; // v16.0 ④ · v16.6 다섯 칸 기록 · 보고·모듈 · 사람·믿음
        float ly = y0 + 88;
        string[] can =
        {
            "감지·경보·일지",
            "격벽·댐퍼 자동 · 물 찬 방 분전함 차단 · 부하 차단",
            "차례로 재가동(기동 전류 ↓) · 새는 방 급수 밸브 원격 잠금",
            "원인 추정과 판단 근거를 말한다 · 전조를 1.5배 먼저 본다",
            "연쇄 예측(배터리·산소가 언제) · 드론·로봇 지휘",
        };
        for (int i = 1; i <= 5; i++)
        {
            bool on = level >= i;
            var r = new Rect2(x, ly, right - x, 22);
            if (level == i) Gfx.RoundRect(this, r, Palette.Accent.WithAlpha(0.1f), 5);
            this.Circle(new Vector2(x + 8, ly + 11), 4f, on ? Palette.Good : Palette.TextMuted.WithAlpha(0.4f), true, -1f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 20, ly + 15), AutomationSystem.LevelName(i), Ui.TextBody, on ? Palette.Text : Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 120, ly + 15), Fit(can[i - 1], right - x - 124, Ui.TextSmall, Fonts.Body), Ui.TextSmall, on ? Palette.TextDim : Palette.TextMuted.WithAlpha(0.6f));
            ly += 22;
        }
        Divider(x, right, ly + 6);
        // v13.0 모듈
        SectionTitle(x, ly + 24, "맡은 일");
        float mx = x + 44;
        foreach (var mod in Enum.GetValues<ComputerModule>())
        {
            bool on = a.Has(mod);
            string label = AutomationSystem.ModuleName(mod);
            float wl = Gfx.Width(Fonts.Body, label, Ui.TextSmall) + 14;
            if (mx + wl > right) { mx = x + 44; ly += 18; }
            var r = new Rect2(mx, ly + 12, wl, 17);
            Gfx.RoundRect(this, r, on ? Palette.Good.WithAlpha(0.18f) : new Color(1, 1, 1, 0.03f), 4, on ? Palette.Good.WithAlpha(0.6f) : Palette.TextMuted.WithAlpha(0.3f), 1);
            Gfx.Text(this, Fonts.Body, new Vector2(mx + 7, ly + 25), label, Ui.TextSmall, on ? Palette.Text : Palette.TextMuted);
            for (int g = 0; g < a.Core.Grade(mod); g++) this.Circle(new Vector2(mx + wl - 4 - g * 4, ly + 15), 1.3f, new Color("#f2c66d"), true, -1f, true); // v16.20 겪은 일로 다듬은 만큼
            mx += wl + 6;
        }
        ly += 34;
        // v13.0 방침 (회의가 정한다)
        SectionTitle(x, ly + 14, "방침");
        // 두 칸으로 (이름 · 지금 값 — 최근에 바뀐 것은 밝게) · v13.2 컴퓨터에 걸린 재난·지휘 방침만 (전부는 E 방침·회의 화면)
        float colW = (right - x - 44) / 2f;
        var pool = PolicySystem.All.Where(p => p.Area != "자원" && p.Id is not ("election" or "noconfidence" or "minutes")).ToList();
        // 카드 안에 들어가는 만큼만 — 아래 칸(지휘 · 감지기 · 판단 근거) 자리를 먼저 떼어 두고, 최근에 바뀐 방침부터
        float below = 120f + 18f * a.FireCases.Count + 20f * w.Ship.Rooms.Count(r => r.LockPendingUntil >= 0) + (a.ZoneActive ? 18f : 0f)
            + (w.Command.Active ? 15f * (w.Command.Teams.Count + 1) : 0f) + (w.Policies.Changes.Count > 0 ? 15f : 0f);
        int fitRows = Math.Max(0, (int)((card.End.Y - ly - 14f - below) / 15f));
        var shown = pool.OrderByDescending(p => w.Policies.Changes.LastOrDefault(c => c.Id == p.Id)?.Tick ?? -1).Take(fitRows * 2).ToArray();
        if (shown.Length < pool.Count) shown = pool.Where(shown.Contains).ToArray(); // 고른 것은 원래 차례대로
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 14), shown.Length == 0 ? $"{pool.Count}개 — 전부 보기: E" : shown.Length < pool.Count ? $"최근 바뀐 {shown.Length}개 · 전부 {pool.Count}개는 E" : "전부 보기: E", Ui.TextTiny, Palette.TextMuted);
        for (int i = 0; i < shown.Length; i++)
        {
            var p = shown[i];
            float px = x + 44 + (i % 2) * colW;
            var last = w.Policies.Changes.LastOrDefault(c => c.Id == p.Id);
            bool recent = last != null && w.Tick - last.Tick < SimTime.TicksPerDay;
            Gfx.Text(this, Fonts.Body, new Vector2(px, ly + 14), p.Name, Ui.TextSmall, Palette.TextDim);
            Gfx.Text(this, Fonts.Bold, new Vector2(px + 86, ly + 14), w.Policies.Option(p.Id), Ui.TextSmall, recent ? Palette.Accent : Palette.Text);
            if (i % 2 == 1 || i == shown.Length - 1) ly += 15;
        }
        if (shown.Length == 0) ly += 4;
        if (w.Policies.Changes.LastOrDefault() is PolicyChange lc)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 14), Fit($"최근: {SimTime.Day(lc.Tick)}일 {PolicySystem.Spec(lc.Id).Name} → {PolicySystem.Spec(lc.Id).Options[lc.To]} — {lc.Why}" + (lc.Yes + lc.No > 0 ? $" (찬성 {lc.Yes} · 반대 {lc.No})" : ""), right - x - 48, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
            ly += 15;
        }
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 14), UiKit.Fit($"늦게 닫아 옆방까지 잃은 일 {a.LateSeals} · 닫힌 방 안에서 쓰러짐 {a.TrappedCasualties} · 질식 소화 {a.Smothered} · 진공 소화 {a.Vacuumed} · 불활성 가스 {(a.InertCapacity > 0 ? a.InertGas / a.InertCapacity * 100 : 100):0}%", right - (x + 44), Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
        ly += 22;
        // v13.0 진행 중인 대응 수순
        foreach (var fc in a.FireCases)
        {
            var room = w.Ship.Rooms[fc.RoomId];
            string step = fc.Stage switch { 0 => "① 소화조", 1 => "② 대피", 2 => fc.Method == "vacuum" ? "③ 진공 소화" : "③ 질식 소화", _ => "④ 다시 가압" };
            string count = fc.Stage == 1 && fc.ExecAt > w.Tick ? $" · {(fc.ExecAt - w.Tick) / (float)SimTime.Minutes(1):0.0}분" : "";
            var c = fc.Stage == 2 ? Palette.Danger : fc.Stage == 1 ? Palette.Warning : Palette.Accent;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), $"🔥 {room.Name} {step}{count}", Ui.TextBody, c);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 200, ly + 14), Fit(fc.Status, right - x - 204, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
            ly += 18;
        }
        // v13.1 지휘
        {
            var cmd = w.Command;
            var cap = cmd.Captain;
            SectionTitle(x, ly + 14, "지휘");
            Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 14),
                $"함장 {cap?.Name ?? "-"} ({CommandSystem.StyleName(cmd.Style)}) · 신뢰 {cmd.Trust * 100:0}% · 컴퓨터 신뢰 {cmd.ComputerTrust * 100:0}%" + (w.Minds.ComputerNick != "" ? $" ('{w.Minds.ComputerNick}')" : "") +
                (cmd.Active ? $" · 현장 지휘 {cmd.CommanderName}" : " · 평시") + (cmd.Elections > 0 ? $" · 선거 {cmd.Elections}번" : ""), Ui.TextSmall, cmd.Trust < 0.35f ? Palette.Warning : Palette.TextDim);
            ly += 18;
            if (cmd.Active)
            {
                foreach (var t in cmd.Teams.Where(t => t.Kind != TeamKind.Reserve))
                {
                    string Nm(int id) => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?";
                    Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 13), Fit($"{CommandSystem.TeamName(t.Kind)} {Nm(t.Worker)}" + (t.Watcher >= 0 ? $" · 감시 {Nm(t.Watcher)}" : "") + (t.Room != null ? $" → {t.Room.Name}" : "") + $" ({t.Detail})", right - x - 48, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.Text);
                    ly += 15;
                }
                var reserve = cmd.Teams.Where(t => t.Kind == TeamKind.Reserve).Select(t => w.Crew.FirstOrDefault(c => c.Id == t.Worker)?.Name).ToList();
                if (reserve.Count > 0) { Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 13), Fit("대기조 " + string.Join("·", reserve), right - x - 48, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextMuted); ly += 15; }
            }
            ly += 4;
        }
        if (a.ZoneActive)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), "▣ 공기 구역", Ui.TextBody, Palette.Warning);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 90, ly + 14), Fit(a.ZoneNote, right - x - 94, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
            ly += 18;
        }
        // 기다리는 격벽
        foreach (var room in w.Ship.Rooms.Where(r => r.LockPendingUntil >= 0))
        {
            float left = (room.LockPendingUntil - w.Tick) / (float)SimTime.Minutes(1);
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), UiKit.Fit($"⏱ {room.Name} 격벽 폐쇄까지 {left:0.0}분 — 안에 {string.Join("·", w.Crew.Where(c => !c.Dead && c.Room == room).Select(c => c.Name))}", right - (x), Ui.TextBody, Fonts.Bold), Ui.TextBody, Palette.Danger);
            ly += 20;
        }
        // 감지기·데이터선
        var live = w.Ship.LiveRooms.Where(r => !r.Detached).ToList();
        int blind = live.Count(r => !r.DataLinked);
        int stale = w.Ship.Machines.Count(mm => (w.Tick - mm.LastReading) > SimTime.Minutes(20) && !mm.Body.Room.Detached);
        float cal = w.Ship.Machines.Select(mm => mm.SensorCal).DefaultIfEmpty(1f).Average();
        Divider(x, right, ly + 6);
        SectionTitle(x, ly + 24, "감지기·데이터선");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 90, ly + 24), UiKit.Fit($"데이터선이 끊긴 방 {blind}/{live.Count} · 값이 멈춘 설비 {stale} · 평균 교정 {cal * 100:0}%" +
            (w.Ship.Rooms.Count(r => r.BreakerOff) is int off && off > 0 ? $" · 분전함 내린 방 {off}" : ""), right - (x + 90), Ui.TextSmall, Fonts.Body), Ui.TextSmall, blind > 0 || stale > 3 ? Palette.Warning : Palette.TextDim);
        ly += 32;
        // 판단 근거
        Divider(x, right, ly);
        SectionTitle(x, ly + 18, level >= 4 ? "판단 근거 (최근)" : "판단 근거 — 등급 IV부터 (지금은 관제석 사람이 있을 때만)");
        ly += 26;
        foreach (var (tick, text) in a.Reasoning.AsEnumerable().Reverse())
        {
            if (ly > card.End.Y - 20) break;
            var lines = WrapText(text, right - x - 50, Ui.TextSmall);
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), SimTime.Clock(tick), Ui.TextTiny, Palette.TextMuted);
            foreach (var l in lines.Take(2)) { Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 12), l, Ui.TextSmall, Palette.Text); ly += 15; }
            ly += 3;
        }
        if (a.Reasoning.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), "아직 없음", Ui.TextSmall, Palette.TextMuted);
    }
}
