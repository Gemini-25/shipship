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
        // 부상 등급 (경상 · 중상 · 위중) — 맨 위 한 줄
        var grade = _world.Grades.Now(c);
        if (grade != InjuryGrade.None)
        {
            var gc = grade == InjuryGrade.Critical ? Palette.Danger : grade == InjuryGrade.Serious ? Palette.Warning : new Color("#ffd27a");
            string gn = InjuryGradeSystem.Name(grade);
            Gfx.Pill(this, Fonts.Bold, new Vector2(lx + Gfx.Width(Fonts.Bold, gn, Ui.TextSmall) * 0.5f + 7, ly + 6), gn, Ui.TextSmall, gc, new Color(0, 0, 0, 0.3f), gc.WithAlpha(0.5f));
            string why = grade == InjuryGrade.Critical ? "지금 손대지 않으면 위험하다" : grade == InjuryGrade.Serious ? "치료를 받아야 낫는다" : "스스로 감고 일한다";
            Gfx.Text(this, Fonts.Body, new Vector2(lx + Gfx.Width(Fonts.Bold, gn, Ui.TextSmall) + 22, ly + 10), Fit(why, right - lx - 60, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
            ly += 20;
        }
        string summary = Wounds.Summary(v);
        foreach (var part in (summary.Length > 0 ? summary : "다친 데 없음").Split(" · "))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 10), part, Ui.TextBody, summary.Length > 0 ? Palette.Warning : Palette.Good);
            ly += 16;
        }
        float hand = Wounds.HandFactor(v), leg = Wounds.LegFactor(v), lung = Wounds.LungLoad(v);
        Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), $"손 {hand * 100:0}% · 걸음 {leg * 100:0}% · 숨 {100f / lung:0}%", Ui.TextBody, Palette.TextDim);
        ly += 18;
        if (c.Dose > 0.05f) { Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), $"방사선 {c.Dose:0.00}Sv" + (c.Dose > 1f ? " — 몸이 상한다" : ""), Ui.TextBody, c.Dose > 1f ? Palette.Danger : Palette.TextDim); ly += 18; }
        if (_world.Organs.Line(c) is string organs) { Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), Fit(organs, right - lx, Ui.TextBody, Fonts.Body), Ui.TextBody, Palette.Warning); ly += 18; } // 의료 2차 진단된 장기 · 기계 · 이식
        Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), $"체력 단련 {c.Fitness * 100:0}%", Ui.TextBody, Palette.TextDim);
        if (_world.Body2.Line(c) is string bodyLine) { ly += 18; Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), bodyLine, Ui.TextBody, _world.Body2.Peek(c) is { Tight: true } or { Loose: true } ? Palette.Warning : Palette.TextDim); } // v17.1 몸무게 · 우주복 · 머리
        // v14.1 앓는 것 (진단 전이면 "어딘가 아프다")
        if (DiseaseSystem.Sick(c)) { ly += 18; Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), $"열병 {_world.Disease.Severity(c) * 100:0}%", Ui.TextBody, Palette.Warning); }
        foreach (var a in c.Ailments.Take(3))
        {
            ly += 18;
            var s = AilmentSystem.Spec(a.Id);
            float sev = _world.Ailments.Severity(a);
            string name = a.Diagnosed ? $"{s.Name} {sev * 100:0}% · {AilmentSystem.CureName(s.Cure)}" : $"어딘가 아프다 {sev * 100:0}% — 진단 전";
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 14), Fit(name, right - lx, Ui.TextBody, Fonts.Body), Ui.TextBody, sev > 0.5f ? Palette.Danger : sev > 0.25f ? Palette.Warning : new Color("#ffd27a"));
        }

        // 자격 · 습관
        float sy = MathF.Max(y + 136, ly + 24);
        Divider(x, right, sy);
        SectionTitle(x, sy + 22, "자격");
        string quals = c.Quals.Count == 0 ? "없음" : string.Join(" · ", c.Quals.OrderBy(q => q).Select(Life.Name));
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, sy + 24), Fit(quals, right - x - 160, Ui.TextBody, Fonts.Body), Ui.TextBody, Palette.Text);
        SectionTitle(x, sy + 44, "습관");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, sy + 46), c.Habits.Count == 0 ? "—" : string.Join(" · ", c.Habits.Select(Life.Name)), Ui.TextBody, Palette.Text);
        // v14.0 취미 · 두려움 · 말버릇
        SectionTitle(x, sy + 66, "취미");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, sy + 68), c.Hobbies.Count == 0 ? "—" : string.Join(" · ", c.Hobbies.Select(h => Persona.Of(h).Name)), Ui.TextBody, new Color("#9fe0b0"));
        SectionTitle(x, sy + 88, "두려움");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 52, sy + 90), c.Fears.Count == 0 ? "딱히 없다" : Fit(string.Join(" · ", c.Fears.Select(f => $"{Persona.Of(f).Name} ({Persona.Of(f).Note})")), right - x - 60, Ui.TextBody, Fonts.Body), Ui.TextBody, c.Fears.Count == 0 ? Palette.TextMuted : new Color("#ffb38a"));
        SectionTitle(x, sy + 110, "말버릇");
        Gfx.Text(this, Fonts.Body, new Vector2(x + 52, sy + 112), c.Quirk < 0 ? "—" : $"“{Persona.Say(c, "그건 내가 볼게")}”", Ui.TextBody, Palette.TextDim);
        SectionTitle(x, sy + 132, "칭호"); // v15.9 칭호·업적
        Gfx.Text(this, Fonts.Body, new Vector2(x + 44, sy + 134), Fit(_world.Titles.Line(c), right - x - 52, Ui.TextBody, Fonts.Body), Ui.TextBody, new Color("#ffd27a"));
        if (c.Stats.Mistakes > 0) Gfx.TextRight(this, Fonts.Body, new Vector2(right, sy + 46), $"실수 {c.Stats.Mistakes}번", Ui.TextBody, Palette.Warning);
        if (c.GriefUntil > _world.Tick) Gfx.TextRight(this, Fonts.Body, new Vector2(right, sy + 24), "슬픔에 잠겨 있다", Ui.TextBody, new Color("#9fb4ff"));

        // 일기
        float dy = sy + 152;
        Divider(x, right, dy);
        SectionTitle(x, dy + 22, "일기");
        float ey = dy + 34;
        // v17.6 공책 한 쪽처럼: 종이 · 줄 · 여백선 · 쪽 번호 (줄에 맞춰 쓴다)
        BookLook.Page(this, new Rect2(x - 6, ey - 2, right - x + 12, 6 * 19 + 26), Math.Max(1, c.Diary.Count / 6 + 1), true, 19f, 15f, 70f);
        foreach (var (tick, text) in c.Diary.AsEnumerable().Reverse().Take(6))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ey + 12), $"{SimTime.Day(tick)}일 {SimTime.Clock(tick)}", Ui.TextSmall, BookLook.InkFaint);
            string t = text;
            while (t.Length > 4 && Gfx.Width(Fonts.Body, t, Ui.TextBody) > right - x - 78) t = t[..^2] + "…";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 74, ey + 12), t, Ui.TextBody, BookLook.Ink);
            ey += 19;
        }
        if (c.Diary.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(x + 74, ey + 12), "아직 쓴 것이 없다", Ui.TextBody, BookLook.InkDim);
    }

    /// <summary>v14.3 물건 탭: 가진 것 — 어디에 있나 · 상태 · 진척 · 출처와 이력 · 관계의 이유.</summary>
    private void DrawCrewThings(CrewMember c, float x, float right, float y, Color col)
    {
        var w = _world;
        var bs = w.Belongings;
        SectionTitle(x, y + 10, "가진 것");
        float ly = y + 22;
        foreach (var b in bs.Of(c).OrderBy(b => b.Kind == BelongingKind.Artwork ? 1 : 0).ThenBy(b => b.Id).Take(9))
        {
            var bc = !b.Usable ? Palette.Danger : b.Open ? new Color("#ffd27a") : Palette.Text;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 12), Fit(b.Name, (right - x) * 0.5f, Ui.TextBody, Fonts.Bold), Ui.TextBody, bc);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 12), Fit(bs.Where(b) + (b.Condition < 0.95f ? $" · {b.Condition * 100:0}%" : "") + (b.Progress > 0.02f && b.Kind != BelongingKind.Artwork ? $" · {b.Progress * 100:0}%" : ""), (right - x) * 0.5f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
            string hist = (b.From >= 0 ? $"{w.Crew.FirstOrDefault(o => o.Id == b.From)?.Name}에게서 · " : "") + (b.Marks.Count > 0 ? b.Marks[^1].Text : b.Origin);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 8, ly + 27), Fit(hist, right - x - 8, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
            ly += 33;
        }
        if (!bs.Of(c).Any()) { Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), "가진 것이 없다", Ui.TextBody, Palette.TextMuted); ly += 18; }
        // 관계의 이유 (v14.3~)
        var why = w.Relations.All.Where(m => m.Who == c.Id).OrderByDescending(m => m.Tick).Take(4).ToList();
        if (why.Count > 0)
        {
            Divider(x, right, ly + 4);
            SectionTitle(x, ly + 22, "왜 그런 사이인가");
            ly += 30;
            foreach (var m in why)
            {
                var o = w.Crew.FirstOrDefault(p => p.Id == m.About);
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), Fit($"{o?.Name} — {m.Text}", right - x, Ui.TextSmall, Fonts.Body), Ui.TextSmall, m.Weight >= 0f ? Palette.Good : new Color("#ff9a8a"));
                ly += 16;
            }
        }
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
            if (_world.Tick - people[i].Quarrel < SimTime.Hours(6)) Gfx.TextCentered(this, Fonts.Bold, pos[i] + new Vector2(0, -10), "!", Ui.TextSmall, Palette.Danger);
        }
        var st = _world.Life.Stats;
        Gfx.TextCentered(this, Fonts.Body, new Vector2(center.X, y + size + 14), $"말다툼 {st.Arguments} · 중재 {st.Mediations} · 문병 {st.Visits}", Ui.TextSmall, Palette.TextMuted);
    }
}
