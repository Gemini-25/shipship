using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.13 우주 대재난 예보 패널 (왼쪽 위): 재난 그림 · 우주급 · 단계 · 남은 시간 · 신뢰도와 오차 · 대비 계획 진행 · 컴퓨터 제안 · 아는 사람 · 바깥 시점 켜고 끄기.
public partial class Hud
{
    private bool _cosmicFolded;

    private static float CosmicIconScale(CosmicKind k) => k switch
    {
        CosmicKind.RedGiantShell => 0.35f, CosmicKind.PlanetRing or CosmicKind.SuperFlare or CosmicKind.DarkNebula => 0.5f,
        CosmicKind.RoguePlanet or CosmicKind.ShatteredPlanet or CosmicKind.Kessler or CosmicKind.MineField => 0.6f, _ => 1f,
    };

    private static Color CosmicPhaseColor(CosmicPhase p) => p switch
    {
        CosmicPhase.Forecast => Palette.Accent, CosmicPhase.Brace => Palette.Warning, CosmicPhase.Impact => Palette.Danger, CosmicPhase.After => Palette.Good, _ => Palette.TextMuted,
    };

    private void DrawCosmicPanel(Vector2 mouse)
    {
        var w = _world;
        var cs = w.Cosmic;
        _cosmicH = 0f;
        if (ControlOpen || ChronicleOpen || TechOpen || PolicyOpen || ChainOpen || VoyageOpen) return;
        var e = cs.Main;
        const float width = 344f;
        float x = Margin, y = _plan.CosmicY; // v17.6 왼쪽 위 더미에 쌓는다
        if (e == null)
        {
            if (cs.Sky.Count == 0 && cs.Customs.Count == 0) return;
            // 지나간 뒤: 하늘에 남은 것 · 관행 한 줄
            var small = new Rect2(x, y, width, 30f);
            _cosmicH = 30f;
            if (_plan.Cosmic.Empty) return;
            Card(small);
            string sky = cs.Sky.Count > 0 ? $"창밖: {string.Join(" · ", cs.Sky.TakeLast(2).Select(s => s.Name))}" : "";
            var vig = cs.CustomOf(CosmicCustomKind.Vigil);
            string cust = vig != null ? $" · 그날의 밤 {Math.Max(0, (int)Math.Ceiling((vig.NextDay - w.Tick) / (float)SimTime.TicksPerDay))}일 뒤" : "";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 12f, y + 19f), Fit(sky + cust, width - 24f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
            return;
        }
        var spec = e.Spec;
        var pcol = CosmicPhaseColor(e.Phase);
        var openAsks = w.Automation.Asks.Open.Where(p => p.Key.StartsWith("cosmic:")).ToList();
        float h = _cosmicFolded ? 54f : 176f + openAsks.Count * 30f;
        _cosmicH = h; // v17.6 펼친 높이를 알리고, 자리는 배치 규칙대로 (모자라면 접힘 · 숨김)
        if (_plan.Cosmic.Empty) return;
        h = _plan.Cosmic.H;
        bool cosmicFold = _cosmicFolded || _plan.CosmicFolded;
        var card = new Rect2(x, y, width, h);
        Card(card, e.Phase == CosmicPhase.Impact ? Tone.Danger : e.Phase == CosmicPhase.Brace ? Tone.Caution : Tone.Info);
        // 그림 (재난마다 다른 실루엣 · 움직임)
        var iconRect = new Rect2(x + 8f, y + 8f, 40f, 40f);
        this.Box(iconRect, new Color(0.01f, 0.015f, 0.03f, 1f), true);
        var ic = iconRect.GetCenter();
        float near = CosmicSky.Near(w, e), hit = CosmicSky.Hit(w, e);
        CosmicArt.Draw(this, e.Kind, ic, 22f * CosmicIconScale(e.Kind), _time, MathF.Max(0.7f, near), MathF.Min(0.6f, hit), iconRect, ic + new Vector2(30f, 10f), e.Id * 97 + 13);
        this.Box(iconRect, pcol.WithAlpha(0.6f), false, 1.5f);
        // 머리: 이름 · 우주급 · 단계
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 56f, y + 22f), Fit(spec.Name, width - 170f, Ui.TextSubtitle, Fonts.Bold), Ui.TextSubtitle, Palette.Text);
        Gfx.Pill(this, Fonts.Bold, new Vector2(x + width - 96f, y + 18f), CosmicCatalog.Scale, Ui.TextTiny, new Color("#ffd166"), new Color(0.12f, 0.08f, 0.02f, 0.9f), new Color("#ffd166").WithAlpha(0.5f), 7f, 3f);
        Gfx.Pill(this, Fonts.Bold, new Vector2(x + width - 40f, y + 18f), e.PhaseName, Ui.TextTiny, pcol, new Color(0.03f, 0.04f, 0.07f, 0.9f), pcol.WithAlpha(0.5f), 7f, 3f);
        string sub;
        if (e.Phase <= CosmicPhase.Brace)
        {
            float hrs = e.HoursTo(w.Tick, e.Predicted);
            sub = hrs > 0f ? $"{(int)hrs}시간 {(int)((hrs - (int)hrs) * 60f):00}분 뒤 (예보) · 오차 ±{e.ErrorHours:0.0}시간" : $"예보한 시각이 지났다 (오차 ±{e.ErrorHours:0.0}시간)";
        }
        else if (e.Phase == CosmicPhase.Impact)
        {
            int idx = Enumerable.Range(0, spec.Stages.Length).LastOrDefault(i => (e.StagesStarted & (1 << i)) != 0);
            sub = e.SceneLine != "" ? e.SceneLine : spec.Stages[idx].Text + (e.Avoided ? " (비켜 약하게)" : ""); // v19 고유 장면
        }
        else sub = $"{e.Grade} · {(e.AfterUntil - w.Tick) / (float)SimTime.TicksPerDay:0}일 동안 후유증";
        Gfx.Text(this, Fonts.Body, new Vector2(x + 56f, y + 40f), Fit(sub, width - 66f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, pcol);
        Button(new Rect2(card.End.X - 24f, y + 30f, 18f, 18f), cosmicFold ? "▾" : "▴", false, mouse, () => _cosmicFolded = !_cosmicFolded, Ui.TextTiny);
        if (cosmicFold) return;
        float yy = y + 62f;
        float lx = x + 12f, right = card.End.X - 12f;
        // 신뢰도 막대 (컴퓨터가 믿는 정도) · 누가 봤나
        Gfx.Text(this, Fonts.Body, new Vector2(lx, yy + 4f), "예보 신뢰도", Ui.TextTiny, Palette.TextMuted);
        var bar = new Rect2(lx + 70f, yy - 3f, 150f, 8f);
        Gfx.Bar(this, bar, e.Confidence, e.Confidence >= 0.7f ? Palette.Good : e.Confidence >= 0.45f ? Palette.Warning : Palette.Danger);
        Gfx.Text(this, Fonts.Bold, new Vector2(bar.End.X + 6f, yy + 4f), $"{e.Confidence * 100:0}%", Ui.TextTiny, Palette.Text);
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, yy + 4f), Fit(e.KnownBy, 60f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        yy += 18f;
        int live = w.Crew.Count(c => !c.Dead), knows = w.Crew.Count(c => !c.Dead && cs.Knows(c, e));
        int hidden = w.Crew.Count(c => !c.Dead && !c.Outside && c.Room != null && cs.RelExposure(c.Room) <= 0.32f);
        Gfx.Text(this, Fonts.Body, new Vector2(lx, yy + 4f), Fit($"아는 사람 {knows}/{live} · 차폐 쪽 {hidden}명 · {spec.Detect}", width - 24f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        yy += 18f;
        // 대비 계획 진행: 종류마다 작은 칸 (끝난 것은 채운다)
        int done = e.Tasks.Count(t => t.Done), all = e.Tasks.Count;
        Gfx.Text(this, Fonts.Bold, new Vector2(lx, yy + 4f), all == 0 ? "대비 계획 — 아직" : $"대비 {done}/{all}", Ui.TextSmall, all > 0 && done == all ? Palette.Good : Palette.Text);
        float bx = lx + 72f;
        foreach (var t in e.Tasks.Take(18))
        {
            var r = new Rect2(bx, yy - 5f, 12f, 12f);
            var col = t.Done ? Palette.Good : t.By >= 0 ? Palette.Warning : new Color(1f, 1f, 1f, 0.15f);
            this.Box(r, col.WithAlpha(t.Done ? 0.85f : 0.5f), t.Done || t.By >= 0);
            if (!t.Done && t.By < 0) this.Box(r, col, false, 1f);
            BraceGlyph(t.Kind, r.GetCenter(), t.Done ? new Color(0.02f, 0.05f, 0.04f) : Palette.Text);
            if (r.HasPoint(mouse)) Gfx.Text(this, Fonts.Body, new Vector2(lx, yy + 22f), Fit($"{t.Label}{(t.Done ? $" ✓ {t.DoneBy}" : t.By >= 0 ? " — 하는 중" : "")}", width - 24f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Warning);
            bx += 14f;
        }
        yy += 32f;
        // 결정: 항로 · 봉쇄 · 컴퓨터 끄기
        string plan = spec.Avoid
            ? e.Avoided ? $"항로를 바꿔 비켰다 (추진제 {e.FuelSpent:0}kg)" : e.BurnAt >= 0 ? (w.Tick >= e.BurnAt ? "항로 변경 연소 중" : "곧 항로 변경 연소") : e.AvoidPlan < 0 ? $"그대로 간다 — {e.AvoidWhy}" : e.AvoidPlan == 0 ? "항로를 바꿀지 정하는 중" : "항로 변경"
            : "피할 수 없다 — 버틴다";
        if (e.SealPlan && e.TargetRoom >= 0) plan += $" · {w.Ship.Rooms[e.TargetRoom].Name} {(e.Sealed ? "봉쇄됨" : "비우는 중")}";
        if (e.ShutdownComputer) plan += " · 주 컴퓨터를 내린다";
        if (e.Phase <= CosmicPhase.Brace && e.SceneLine != "") plan += " · " + e.SceneLine; // v19 고유 장면
        Gfx.Text(this, Fonts.Body, new Vector2(lx, yy + 4f), Fit(plan, width - 24f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, e.Avoided ? Palette.Good : Palette.Text);
        yy += 18f;
        foreach (var p in openAsks)
        {
            var pr = new Rect2(lx, yy - 6f, width - 24f, 26f);
            Gfx.RoundRect(this, pr, new Color(0.1f, 0.08f, 0.02f, 0.8f), 6f, Palette.Warning.WithAlpha(0.4f));
            float left = (p.Deadline - w.Tick) / (float)SimTime.Minutes(1);
            Gfx.Text(this, Fonts.Body, new Vector2(lx + 8f, yy + 11f), Fit($"제안 · {p.Title} ({left:0}분)", width - 140f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Warning);
            // v16.20 완전 관전: 받기 · 거절은 지휘하는 사람이 (화면에는 누가 정하는지만)
            var boss = w.Command.Active && w.Command.Commander != null ? w.Command.Commander : w.Command.Captain;
            Gfx.TextRight(this, Fonts.Body, new Vector2(pr.End.X - 8f, yy + 11f), boss != null ? $"{Ko.IGa(boss.Name)} 정한다" : "기한이 지나면 컴퓨터가", Ui.TextMicro, Palette.TextMuted);
            yy += 30f;
        }
        // 관행 · 바깥 시점
        var drill = cs.CustomOf(CosmicCustomKind.Drill);
        string foot = cs.Customs.Count > 0 ? "관행: " + string.Join(" · ", cs.Customs.Select(c => c.Kind switch { CosmicCustomKind.Vigil => "그날의 밤", CosmicCustomKind.Drill => "예보 훈련", _ => "흔들림 대비 정리" })) : "";
        if (drill != null && e.Phase <= CosmicPhase.Brace) foot += " (대비를 일찍 시작한다)";
        Gfx.Text(this, Fonts.Body, new Vector2(lx, yy + 6f), Fit(foot, width - 130f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
        Button(new Rect2(right - 104f, yy - 6f, 104f, 20f), CosmicViewSettings.OutsideView ? "바깥 시점 켬" : "바깥 시점 끔", CosmicViewSettings.OutsideView, mouse, () => CosmicViewSettings.OutsideView = !CosmicViewSettings.OutsideView, Ui.TextTiny);
    }

    /// <summary>대비 일마다 다른 작은 기호 (물방울 · 상자 · 덮개 · 전원 · 끈 · 빗금 · 접힘 · 담요 · 조타 · 다시 켬).</summary>
    private void BraceGlyph(BraceKind k, Vector2 c, Color col)
    {
        switch (k)
        {
            case BraceKind.WaterWall: this.Circle(c + new Vector2(0, 1.5f), 2.6f, col, true, -1f, true); this.Poly(new[] { c + new Vector2(0, -4f), c + new Vector2(2.4f, 0.5f), c + new Vector2(-2.4f, 0.5f) }, col); break;
            case BraceKind.Supplies: this.Box(new Rect2(c - new Vector2(3.5f, 3f), new Vector2(7f, 6f)), col, false, 1f); DrawLine(c - new Vector2(0, 2f), c + new Vector2(0, 2f), col, 1f); break;
            case BraceKind.Shutters: for (int i = -1; i <= 1; i++) DrawLine(c + new Vector2(-3.5f, i * 2.2f), c + new Vector2(3.5f, i * 2.2f), col, 1f); break;
            case BraceKind.PowerDown: this.Arc(c, 3.2f, -Mathf.Pi * 0.3f, Mathf.Pi * 1.3f, 10, col, 1f, true); DrawLine(c - new Vector2(0, 4f), c, col, 1f); break;
            case BraceKind.Restart: this.Arc(c, 3.2f, 0f, Mathf.Pi * 1.5f, 10, col, 1f, true); DrawLine(c + new Vector2(3.2f, 0), c + new Vector2(3.2f, -2.5f), col, 1f); break;
            case BraceKind.Stow: DrawLine(c - new Vector2(3.5f, 3.5f), c + new Vector2(3.5f, 3.5f), col, 1f); DrawLine(c + new Vector2(-3.5f, 3.5f), c + new Vector2(3.5f, -3.5f), col, 1f); break;
            case BraceKind.Seal: for (int i = -1; i <= 1; i++) DrawLine(c + new Vector2(i * 2.5f - 1.5f, 3.5f), c + new Vector2(i * 2.5f + 1.5f, -3.5f), col, 1.2f); break;
            case BraceKind.Fold: DrawLine(c + new Vector2(-3.5f, 3f), c + new Vector2(0, -3f), col, 1f); DrawLine(c + new Vector2(0, -3f), c + new Vector2(3.5f, 3f), col, 1f); break;
            case BraceKind.Insulate: this.Arc(c + new Vector2(-1.8f, 0), 1.8f, 0f, Mathf.Pi, 5, col, 1f, true); this.Arc(c + new Vector2(1.8f, 0), 1.8f, 0f, Mathf.Pi, 5, col, 1f, true); break;
            case BraceKind.Barricade: DrawLine(c - new Vector2(3.5f, 3.5f), c + new Vector2(3.5f, 3.5f), col, 1.5f); DrawLine(c + new Vector2(-3.5f, 3.5f), c + new Vector2(3.5f, -3.5f), col, 1.5f); this.Box(new Rect2(c - new Vector2(3.5f, 3.5f), new Vector2(7f, 7f)), col, false, 1f); break;
            case BraceKind.Decon: for (int i = -1; i <= 1; i++) this.Circle(c + new Vector2(i * 2.4f, 1.5f + (i & 1)), 1.1f, col, true, -1f, true); DrawLine(c + new Vector2(-3.5f, -3f), c + new Vector2(3.5f, -3f), col, 1f); break;
            case BraceKind.Logbook: this.Box(new Rect2(c - new Vector2(3f, 4f), new Vector2(6f, 8f)), col, false, 1f); for (int i = -1; i <= 1; i++) DrawLine(c + new Vector2(-1.8f, i * 2f), c + new Vector2(1.8f, i * 2f), col, 1f); break;
            case BraceKind.Pilot: this.Arc(c, 3.2f, 0f, Mathf.Tau, 12, col, 1f, true); DrawLine(c - new Vector2(3.2f, 0), c + new Vector2(3.2f, 0), col, 1f); DrawLine(c, c + new Vector2(0, 3.2f), col, 1f); break;
        }
    }
}
