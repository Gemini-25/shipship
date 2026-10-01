using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.7 승무원 몸·일기·관계도: 어디를 다쳤는지(사람 모양 그림) · 자격 · 습관 · 최근 일기 · 배 전체 관계도.
public partial class Hud
{
    private static Color WoundColor(float s) => s < 0.05f ? new Color("#3a4a3f") : s < 0.3f ? new Color("#e0c050") : s < 0.6f ? new Color("#f0883e") : new Color("#e5534b");

    private void DrawCrewBody(CrewMember c, float x, float right, float y, Color col)
    {
        var v = c.Vitals;
        SectionTitle(x, y + 10, "몸");
        // 사람 모양: 머리·가슴(폐)·팔·다리 — 다친 곳은 노랑→주황→빨강, 잃은 팔다리는 점선, 의수·의족은 금속색
        float cx = x + 52, top = y + 26;
        float Sev(BodyPart p) => v.Wounds.Where(wd => wd.Part == p).Select(wd => Wounds.Severity(v, wd)).DefaultIfEmpty(0f).Max();
        Wound? LostOf(BodyPart p) => v.Wounds.FirstOrDefault(wd => wd.Part == p && wd.Lost);
        void Limb(BodyPart p, Vector2 a, Vector2 b)
        {
            var lost = LostOf(p);
            if (lost == null) { DrawLine(a, b, WoundColor(Sev(p)), 6f, true); return; }
            if (lost.Prosthetic) { DrawLine(a, b, new Color("#9aa7b8"), 5f, true); DrawCircle(b, 2.5f, new Color("#cfd8e3")); return; }
            DrawDashedLine(a, b, new Color(1, 1, 1, 0.3f), 2f, 4f);
        }
        DrawCircle(new Vector2(cx, top + 10), 9f, WoundColor(Sev(BodyPart.Head)), true, -1f, true);
        var chest = new Rect2(cx - 11, top + 22, 22, 34);
        DrawRect(chest, WoundColor(MathF.Max(Sev(BodyPart.Chest), Sev(BodyPart.Lungs))));
        if (Sev(BodyPart.Lungs) > 0.05f) { DrawCircle(chest.GetCenter() + new Vector2(-5, -4), 4f, new Color("#ff9aa2").WithAlpha(0.8f)); DrawCircle(chest.GetCenter() + new Vector2(5, -4), 4f, new Color("#ff9aa2").WithAlpha(0.8f)); }
        Limb(BodyPart.LeftArm, new Vector2(cx - 14, top + 26), new Vector2(cx - 26, top + 56));
        Limb(BodyPart.RightArm, new Vector2(cx + 14, top + 26), new Vector2(cx + 26, top + 56));
        Limb(BodyPart.LeftLeg, new Vector2(cx - 6, top + 58), new Vector2(cx - 10, top + 96));
        Limb(BodyPart.RightLeg, new Vector2(cx + 6, top + 58), new Vector2(cx + 10, top + 96));
        // 오른쪽: 상처 목록과 몸이 하는 일
        float lx = x + 110, ly = y + 30;
        string summary = Wounds.Summary(v);
        foreach (var part in (summary.Length > 0 ? summary : "다친 데 없음").Split(" · "))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 10), part, 12, summary.Length > 0 ? Palette.Warning : Palette.Good);
            ly += 16;
        }
        float hand = Wounds.HandFactor(v), leg = Wounds.LegFactor(v), lung = Wounds.LungLoad(v);
        Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), $"손 {hand * 100:0}% · 걸음 {leg * 100:0}% · 숨 {100f / lung:0}%", 12, Palette.TextDim);
        ly += 18;
        if (c.Dose > 0.05f) { Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), $"방사선 {c.Dose:0.00}Sv" + (c.Dose > 1f ? " — 몸이 상한다" : ""), 12, c.Dose > 1f ? Palette.Danger : Palette.TextDim); ly += 18; }
        Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), $"체력 단련 {c.Fitness * 100:0}%", 12, Palette.TextDim);

        // 자격 · 습관
        float sy = y + 136;
        Divider(x, right, sy);
        SectionTitle(x, sy + 22, "자격");
        string quals = c.Quals.Count == 0 ? "없음" : string.Join(" · ", c.Quals.OrderBy(q => q).Select(Life.Name));
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, sy + 24), Fit(quals, right - x - 160, 12, Fonts.Body), 12, Palette.Text);
        SectionTitle(x, sy + 44, "습관");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, sy + 46), c.Habits.Count == 0 ? "—" : string.Join(" · ", c.Habits.Select(Life.Name)), 12, Palette.Text);
        // v14.0 취미 · 두려움 · 말버릇
        SectionTitle(x, sy + 66, "취미");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, sy + 68), c.Hobbies.Count == 0 ? "—" : string.Join(" · ", c.Hobbies.Select(h => Persona.Of(h).Name)), 12, new Color("#9fe0b0"));
        SectionTitle(x, sy + 88, "두려움");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 52, sy + 90), c.Fears.Count == 0 ? "딱히 없다" : Fit(string.Join(" · ", c.Fears.Select(f => $"{Persona.Of(f).Name} ({Persona.Of(f).Note})")), right - x - 60, 12, Fonts.Body), 12, c.Fears.Count == 0 ? Palette.TextMuted : new Color("#ffb38a"));
        SectionTitle(x, sy + 110, "말버릇");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 52, sy + 112), c.Quirk < 0 ? "—" : $"“{Persona.Say(c, "그건 내가 볼게")}”", 12, Palette.TextDim);
        if (c.Stats.Mistakes > 0) Gfx.TextRight(this, Fonts.Body, new Vector2(right, sy + 46), $"실수 {c.Stats.Mistakes}번", 12, Palette.Warning);
        if (c.GriefUntil > _world.Tick) Gfx.TextRight(this, Fonts.Body, new Vector2(right, sy + 24), "슬픔에 잠겨 있다", 12, new Color("#9fb4ff"));

        // 일기
        float dy = sy + 130;
        Divider(x, right, dy);
        SectionTitle(x, dy + 22, "일기");
        float ey = dy + 34;
        foreach (var (tick, text) in c.Diary.AsEnumerable().Reverse().Take(6))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ey + 12), $"{SimTime.Day(tick)}일 {SimTime.Clock(tick)}", 11, Palette.TextMuted);
            string t = text;
            while (t.Length > 4 && Gfx.Width(Fonts.Body, t, 12) > right - x - 78) t = t[..^2] + "…";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 74, ey + 12), t, 12, Palette.TextDim);
            ey += 19;
        }
        if (c.Diary.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(x, ey + 12), "아직 쓴 것이 없다", 12, Palette.TextMuted);
    }

    /// <summary>배 전체 관계도: 사람들을 둥글게 놓고, 가까우면 초록 선·사이가 나쁘면 붉은 선. 고른 사람의 선은 진하게.</summary>
    private void DrawRelationGraph(CrewMember sel, float x, float right, float y)
    {
        var people = _world.Crew.Where(p => !p.Dead).ToList();
        if (people.Count < 3) return;
        float size = MathF.Min(right - x, 200f);
        var center = new Vector2((x + right) / 2f, y + size / 2f + 6f);
        float r = size / 2f - 10f;
        var pos = people.Select((p, i) => center + new Vector2(MathF.Cos(i * MathF.Tau / people.Count - MathF.PI / 2f), MathF.Sin(i * MathF.Tau / people.Count - MathF.PI / 2f)) * r).ToList();
        for (int i = 0; i < people.Count; i++)
            for (int j = i + 1; j < people.Count; j++)
            {
                float a = (people[i].AffinityTo(people[j]) + people[j].AffinityTo(people[i])) / 2f;
                if (MathF.Abs(a) < 0.15f) continue;
                bool mine = people[i] == sel || people[j] == sel;
                var lc = (a > 0 ? Palette.Good : Palette.Danger).WithAlpha((mine ? 0.85f : 0.25f) * MathF.Min(1f, MathF.Abs(a) * 1.5f));
                DrawLine(pos[i], pos[j], lc, mine ? 2.2f : 1.2f, true);
            }
        for (int i = 0; i < people.Count; i++)
        {
            bool me = people[i] == sel;
            DrawCircle(pos[i], me ? 6f : 4f, Palette.Crew(people[i].Id), true, -1f, true);
            if (_world.Tick - people[i].Quarrel < SimTime.Hours(6)) Gfx.TextCentered(this, Fonts.Bold, pos[i] + new Vector2(0, -10), "!", 11, Palette.Danger);
        }
        var st = _world.Life.Stats;
        Gfx.TextCentered(this, Fonts.Body, new Vector2(center.X, y + size + 14), $"말다툼 {st.Arguments} · 중재 {st.Mediations} · 문병 {st.Visits}", 11, Palette.TextMuted);
    }
}
