using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v17.1 머리 위 딱지: 글자 대신 [행동 아이콘 | 감정 그림] 한 덩이 (감정 그림과 겹치지 않게 합친다).
/// 글자(이름 · 하는 일)는 가까이 확대했을 때 · 고르거나 가리켰을 때만 붙는다. 화면 좌표로 그려 늘 선명하다.
/// 이 딱지가 보이는 사람은 세계 쪽 감정 그림(ShipViewEmotions)을 따로 그리지 않는다.
/// </summary>
public static class HeadBadge
{
    public const float TextZoom = 1.5f;

    /// <summary>딱지가 보이나 (LabelOverlay의 라벨 규칙과 같다).</summary>
    public static bool Shown(World w, CrewMember c, float zoom, bool selected, bool hovered, bool crisis)
    {
        if (c.Dead || c.Down || c.Away) return false;
        if (c.Pose == Pose.Sleeping && !selected && !hovered) return false;
        if (zoom < 0.45f && !selected && !hovered) return false;
        if (crisis && !selected && !hovered && zoom < 1.1f && !Severity.Notable(c)) return false;
        if (c.CarriedBy != null && !selected && !hovered) return false;
        return true;
    }

    /// <summary>하는 일 → 아이콘 (v17.1 새 행동 + 손에 든 것 + 기존 상태 아이콘).</summary>
    public static string Action(World w, CrewMember c)
    {
        var job = c.Job;
        var a = job?.Activity;
        string? label = job?.Label;
        switch (a)
        {
            case HaircutActivity: return w.Body2.SessionOf(c) is HairSession s && s.Client == c.Id && !s.Self ? "seat" : "scissors";
            case SuitFitActivity: return "suit";
            case JogActivity: return "run";
            case SweepClipsActivity: return "broom";
            case ResearchActivity: return "scanner";
        }
        if (job?.Current is SprayToil || c.Carrying?.Kind == ItemKind.Extinguisher) return "extinguisher";
        if (c.Outside || c.EvaMode) return "suit";
        if (label != null)
        {
            if (label.Contains("운동", System.StringComparison.Ordinal)) return "treadmill";
            if (label.Contains("조리", System.StringComparison.Ordinal) || label.Contains("배식", System.StringComparison.Ordinal)) return "stove";
            if (label.Contains("걸레", System.StringComparison.Ordinal) || label.Contains("닦", System.StringComparison.Ordinal)) return "broom";
            if (label.Contains("수리", System.StringComparison.Ordinal) || label.Contains("교체", System.StringComparison.Ordinal) || label.Contains("정비", System.StringComparison.Ordinal)) return "wrench";
        }
        if (c.Carrying != null && c.IsMoving && a is ChoresActivity) return "bag";
        return Icons.CrewState(c, w.Tick);
    }

    /// <summary>딱지 한 덩이를 그린다 (center = 화면 좌표).</summary>
    public static void Draw(CanvasItem ci, World w, CrewMember c, Vector2 center, float zoom, bool selected, bool hovered, bool emergency, Color col, float time)
    {
        string icon = Action(w, c);
        bool text = selected || hovered || zoom >= TextZoom;
        string label = selected || hovered ? $"{c.Name} · {c.ActivityLabel}" : c.ActivityLabel;
        var emo = w.Brain2.Emotions.Dominant(c, 0.22f);
        int fs = zoom < 0.7f ? 10 : 11;
        const float iconPx = 14f, h = 18f, pad = 3f;
        float wEmo = emo != null ? 15f : 0f;
        float wText = text ? Gfx.Width(emergency ? Fonts.Bold : Fonts.Body, label, fs) + 6f : 0f;
        float width = pad * 2f + iconPx + wEmo + wText;
        var r = new Rect2(center.X - width * 0.5f, center.Y - h * 0.5f, width, h);
        var border = emergency ? Palette.Danger.WithAlpha(0.85f) : col.WithAlpha(selected ? 0.8f : 0.3f);
        Gfx.RoundRect(ci, r, new Color(0.04f, 0.055f, 0.08f, 0.82f), h * 0.5f, border);
        var iconColor = selected || hovered ? Palette.Text : col.Lightened(0.25f);
        Icons.Draw(ci, icon, new Vector2(r.Position.X + pad + iconPx * 0.5f, center.Y), iconPx, iconColor);
        float x = r.Position.X + pad + iconPx;
        if (emo is { } d)
        {
            float size = 4.6f + 1.6f * Mathf.Clamp(d.v, 0f, 1f);
            EmotionGlyphs.Draw(ci, d.f, new Vector2(x + 7.5f, center.Y + 0.5f), size, time, c.Id * 1.7f, 1.4f - 0.8f * c.Traits.Calm, Mathf.Clamp(0.55f + 0.5f * d.v, 0f, 1f), zoom >= 0.9f);
            x += wEmo;
        }
        if (text)
            Gfx.Text(ci, emergency ? Fonts.Bold : Fonts.Body, new Vector2(x + 3f, center.Y + Gfx.CenterOffset(Fonts.Body, fs)), label, fs,
                selected || hovered ? Palette.Text : col.Lightened(0.25f));
    }
}
