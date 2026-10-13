using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 행성 카드 (왼쪽 위 더미 — 우주 대재난 자리를 함께 쓴다): 착륙 창 후보 · 단계 · 날씨 · 예보 · 기지 · 피해.
public partial class Hud
{
    private static string PhaseName(PlanetSystem pl) => pl.Phase switch
    {
        PlanetPhase.Window => "착륙 창", PlanetPhase.Approach => "접근", PlanetPhase.Descent => "하강", PlanetPhase.Landed => pl.Crashed ? "불시착" : "머무는 중",
        PlanetPhase.Liftoff => "이륙", _ => "",
    };

    private static void PlanetDisc(CanvasItem ci, Vector2 c, float r, Planet p)
    {
        var col = new Color(p.Hex);
        ci.DrawCircle(c, r, col);
        for (int i = -2; i <= 2; i++) ci.DrawLine(c + new Vector2(-r * MathF.Sqrt(1f - i * i / 9f), i * r / 3f), c + new Vector2(r * MathF.Sqrt(1f - i * i / 9f), i * r / 3f), col.Darkened(0.2f + 0.05f * i), 2f);
        ci.DrawArc(c + new Vector2(-r * 0.25f, -r * 0.25f), r * 0.55f, MathF.PI, MathF.PI * 1.5f, 8, new Color(1, 1, 1, 0.25f), 2f);
        if (p.Has(PlanetFeature.Rings)) ci.DrawArc(c, r * 1.45f, -0.3f, MathF.PI + 0.3f, 24, new Color(0.9f, 0.85f, 0.7f, 0.7f), 2f);
        ci.DrawArc(c, r, 0, MathF.Tau, 24, new Color(p.Hex2).WithAlpha(0.6f), 1.5f);
    }

    /// <summary>우주 대재난이 없을 때 그 자리에 행성 카드. 그렸으면 true.</summary>
    private bool DrawPlanetPanel(float x, float y, float width, Vector2 mouse)
    {
        var w = _world;
        var pl = w.Planet;
        if (pl.Phase == PlanetPhase.None) return false;
        var p = pl.Here ?? pl.Candidates.FirstOrDefault();
        if (p == null) return false;
        bool window = pl.Phase == PlanetPhase.Window;
        int rows = window ? pl.Candidates.Count : 4 + (pl.Harm.Count > 0 ? 1 : 0);
        float h = 64f + rows * 19f + 10f;
        _cosmicH = h;
        if (_plan.Cosmic.Empty) return true;
        h = _plan.Cosmic.H;
        var card = new Rect2(x, y, width, h);
        Card(card, pl.Crashed ? Tone.Danger : PlanetSystem.Severe(pl.Weather) && pl.WxPower >= 0.5f ? Tone.Caution : Tone.Info);
        PlanetDisc(this, new Vector2(x + 28f, y + 28f), 17f, p);
        string title = window ? "착륙할 행성을 고른다" : p.Title;
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 54f, y + 22f), Fit(title, width - 140f, Ui.TextSubtitle, Fonts.Bold), Ui.TextSubtitle, Palette.Text);
        var pc = pl.Crashed ? Palette.Danger : window ? Palette.Accent : Palette.Good;
        Gfx.Pill(this, Fonts.Bold, new Vector2(x + width - 44f, y + 18f), PhaseName(pl), Ui.TextTiny, pc, new Color(0.03f, 0.04f, 0.07f, 0.9f), pc.WithAlpha(0.5f), 7f, 3f);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 54f, y + 40f), Fit(window ? "주 컴퓨터가 멀리서 본다 — 흐리다" : p.Line, width - 66f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        if (_plan.CosmicFolded) return true;
        float ly = y + 62f;
        void Row(string head, string text, Color col)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 12f, ly), head, Ui.TextTiny, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 62f, ly), Fit(text, width - 74f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, col);
            ly += 19f;
        }
        if (window)
        {
            foreach (var c in pl.Candidates)
                Row(c.Special ? "★" : "·", $"{c.Name} — 위험 {c.KnownDanger * 100:0}% · 보상 {c.KnownReward * 100:0}% · 확실 {c.Certainty * 100:0}%", Palette.Text);
            return true;
        }
        string wxNow = pl.Weather == PlanetWeather.Calm ? "맑음" : $"{PlanetGen.WeatherName(pl.Weather)} {pl.WxPower * 100:0}%";
        Row("날씨", $"{wxNow} · 바깥 {pl.OutsideC:0}°C" + (pl.Wind > 0.05f ? $" · 바람 {pl.Wind * 100:0}%" : "") + (pl.DayHours <= 0f ? " · 늘 낮과 밤의 경계" : ""), PlanetSystem.Severe(pl.Weather) ? Palette.Warning : Palette.Text);
        float toNext = (pl.WxUntil - w.Tick) / (float)SimTime.TicksPerHour;
        Row("예보", pl.Forecasted >= 0 ? $"{toNext:0.0}시간 뒤 {PlanetGen.WeatherName(pl.NextWx)} {pl.NextWxPower * 100:0}%" + (pl.Sheltering ? " · 밖 일 멈춤" : "") : $"{pl.ForecastLead:0.0}시간 앞까지 본다 — 큰 날씨 없음", pl.Sheltering ? Palette.Warning : Palette.TextDim);
        string stay = pl.Phase == PlanetPhase.Landed ? (pl.StayUntil > w.Tick ? $"임무 {(pl.StayUntil - w.Tick) / (float)SimTime.TicksPerDay:0.0}일 남음" : "떠날 때") : PhaseName(pl);
        Row("판단", pl.Decision.Length > 0 ? $"{stay} · {pl.Decision}" : stay, pl.Crashed ? Palette.Danger : Palette.Text);
        var built = pl.Builds.Where(b => b.Done >= 0).Select(b => PlanetSystem.BuildName(b.Kind)).ToList();
        var making = pl.Builds.FirstOrDefault(b => b.Done < 0);
        string gathered = string.Join(" · ", pl.Gathered.Where(kv => kv.Value > 0).Take(3).Select(kv => $"{(kv.Key == ItemKind.Fuel ? "추진제" : ItemKinds.Name(kv.Key))} {kv.Value}"));
        Row("기지", (built.Count > 0 ? string.Join("·", built) : "아직 없음") + (making != null ? $" · {PlanetSystem.BuildName(making.Kind)} 짓는 중" : "") + (gathered.Length > 0 ? $" · 캔 것 {gathered}" : ""), Palette.Text);
        if (pl.Harm.Count > 0) Row("피해", string.Join(" · ", pl.Harm.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value}")), Palette.Danger);
        return true;
    }
}
