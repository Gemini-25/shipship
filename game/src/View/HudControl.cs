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
        if (ControlOpen) { ChronicleOpen = false; TechOpen = false; OpenChain(null); }
    }

    private void DrawControl(Vector2 mouse)
    {
        var w = _world;
        var a = w.Automation;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float wdt = Mathf.Min(640f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogHeight - Margin - 40f;
        var card = new Rect2(x0, y0, wdt, height);
        _controlRect = card;
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        int level = a.Level;
        string state = !a.Present ? "주 컴퓨터 없음" : a.MainOnline ? "온라인" : a.BackupActive ? "멎음 · 예비 제어기" : "멎음";
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y0 + 30), $"주 컴퓨터 관제 — 등급 {AutomationSystem.LevelName(level)}", 17, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 300, y0 + 30), level < 5 ? $"내려간 까닭: {a.LevelWhy}" : "멀쩡하면 V — 아래는 고장 났을 때 남는 기능", 11, level < 5 ? Palette.Warning : Palette.TextMuted);
        Button(new Rect2(right - 58, y0 + 12, 58, 26), "Y 닫기", false, mouse, ToggleControl, 11);
        var col = a.MainOnline ? Palette.Good : Palette.Danger;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y0 + 50), $"{state} · {(a.Computer is Machine m ? $"{m.Name} (단계 {m.Tier})" : "-")}" +
            (a.Operator is CrewMember op ? $" · 관제석: {op.Name} (수동 조종 — 좁게 끊고 빨리 되돌린다)" : " · 관제석 비어 있음 (컴퓨터가 보수적으로)"), 12, a.Operator != null ? Palette.Accent : col);

        // 등급 사다리
        float ly = y0 + 64;
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
            DrawCircle(new Vector2(x + 8, ly + 11), 4f, on ? Palette.Good : Palette.TextMuted.WithAlpha(0.4f), true, -1f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 20, ly + 15), AutomationSystem.LevelName(i), 12, on ? Palette.Text : Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 120, ly + 15), Fit(can[i - 1], right - x - 124, 11, Fonts.Body), 11, on ? Palette.TextDim : Palette.TextMuted.WithAlpha(0.6f));
            ly += 22;
        }
        Divider(x, right, ly + 6);
        // v13.0 모듈
        SectionTitle(x, ly + 24, "모듈");
        float mx = x + 44;
        foreach (var mod in Enum.GetValues<ComputerModule>())
        {
            bool on = a.Has(mod);
            string label = AutomationSystem.ModuleName(mod);
            float wl = Gfx.Width(Fonts.Body, label, 11) + 14;
            if (mx + wl > right) { mx = x + 44; ly += 18; }
            var r = new Rect2(mx, ly + 12, wl, 17);
            Gfx.RoundRect(this, r, on ? Palette.Good.WithAlpha(0.18f) : new Color(1, 1, 1, 0.03f), 4, on ? Palette.Good.WithAlpha(0.6f) : Palette.TextMuted.WithAlpha(0.3f), 1);
            Gfx.Text(this, Fonts.Body, new Vector2(mx + 7, ly + 25), label, 11, on ? Palette.Text : Palette.TextMuted);
            mx += wl + 6;
        }
        ly += 34;
        // v13.0 방침 (회의가 정한다)
        SectionTitle(x, ly + 14, "방침");
        foreach (var p in PolicySystem.All)
        {
            var last = w.Policies.Changes.LastOrDefault(c => c.Id == p.Id);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 14), p.Name, 11, Palette.TextDim);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 140, ly + 14), w.Policies.Option(p.Id), 11, Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 260, ly + 14), Fit(last != null ? $"{SimTime.Day(last.Tick)}일 {last.Why}" + (last.Yes + last.No > 0 ? $" (찬성 {last.Yes} · 반대 {last.No})" : "") : "처음 정한 대로", right - x - 264, 10, Fonts.Body), 10, Palette.TextMuted);
            ly += 16;
        }
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 14), $"늦게 닫아 옆방까지 잃은 일 {a.LateSeals} · 닫힌 방 안에서 쓰러짐 {a.TrappedCasualties} · 질식 소화 {a.Smothered} · 진공 소화 {a.Vacuumed} · 불활성 가스 {(a.InertCapacity > 0 ? a.InertGas / a.InertCapacity * 100 : 100):0}%", 10, Palette.TextMuted);
        ly += 22;
        // v13.0 진행 중인 대응 수순
        foreach (var fc in a.FireCases)
        {
            var room = w.Ship.Rooms[fc.RoomId];
            string step = fc.Stage switch { 0 => "① 소화조", 1 => "② 대피", 2 => fc.Method == "vacuum" ? "③ 진공 소화" : "③ 질식 소화", _ => "④ 다시 가압" };
            string count = fc.Stage == 1 && fc.ExecAt > w.Tick ? $" · {(fc.ExecAt - w.Tick) / (float)SimTime.Minutes(1):0.0}분" : "";
            var c = fc.Stage == 2 ? Palette.Danger : fc.Stage == 1 ? Palette.Warning : Palette.Accent;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), $"🔥 {room.Name} {step}{count}", 12, c);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 200, ly + 14), Fit(fc.Status, right - x - 204, 11, Fonts.Body), 11, Palette.TextDim);
            ly += 18;
        }
        if (a.ZoneActive)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), "▣ 공기 구역", 12, Palette.Warning);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 90, ly + 14), Fit(a.ZoneNote, right - x - 94, 11, Fonts.Body), 11, Palette.TextDim);
            ly += 18;
        }
        // 기다리는 격벽
        foreach (var room in w.Ship.Rooms.Where(r => r.LockPendingUntil >= 0))
        {
            float left = (room.LockPendingUntil - w.Tick) / (float)SimTime.Minutes(1);
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), $"⏱ {room.Name} 격벽 폐쇄까지 {left:0.0}분 — 안에 {string.Join("·", w.Crew.Where(c => !c.Dead && c.Room == room).Select(c => c.Name))}", 12, Palette.Danger);
            ly += 20;
        }
        // 감지기·데이터선
        var live = w.Ship.LiveRooms.Where(r => !r.Detached).ToList();
        int blind = live.Count(r => !r.DataLinked);
        int stale = w.Ship.Machines.Count(mm => (w.Tick - mm.LastReading) > SimTime.Minutes(20) && !mm.Body.Room.Detached);
        float cal = w.Ship.Machines.Select(mm => mm.SensorCal).DefaultIfEmpty(1f).Average();
        Divider(x, right, ly + 6);
        SectionTitle(x, ly + 24, "감지기·데이터선");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 90, ly + 24), $"데이터선이 끊긴 방 {blind}/{live.Count} · 값이 멈춘 설비 {stale} · 평균 교정 {cal * 100:0}%" +
            (w.Ship.Rooms.Count(r => r.BreakerOff) is int off && off > 0 ? $" · 분전함 내린 방 {off}" : ""), 11, blind > 0 || stale > 3 ? Palette.Warning : Palette.TextDim);
        ly += 32;
        // 판단 근거
        Divider(x, right, ly);
        SectionTitle(x, ly + 18, level >= 4 ? "판단 근거 (최근)" : "판단 근거 — 등급 IV부터 (지금은 관제석 사람이 있을 때만)");
        ly += 26;
        foreach (var (tick, text) in a.Reasoning.AsEnumerable().Reverse())
        {
            if (ly > card.End.Y - 20) break;
            var lines = WrapText(text, right - x - 50, 11);
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), SimTime.Clock(tick), 10, Palette.TextMuted);
            foreach (var l in lines.Take(2)) { Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 12), l, 11, Palette.Text); ly += 15; }
            ly += 3;
        }
        if (a.Reasoning.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), "아직 없음", 11, Palette.TextMuted);
    }
}
