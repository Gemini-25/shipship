using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 지식 전수: 솜씨는 사람에게 있다 — 떠나면 같이 떠난다.
//   사수 · 부사수: 솜씨마다 가장 잘하는 사람이 부사수 하나를 오래 데리고 가르친다 (열네 살이 된 아이 · 젊은 사람 먼저). 부사수가 홀로 서면 매듭.
//   일지: 사수는 틈틈이 솜씨를 적는다 — 다 쓰면 주컴퓨터가 보관한다 (기억 칸이 틀어지면 일부가 깨진다). 일지가 있으면 누구든 기초까지는 혼자 익힌다.
//   잃는 솜씨: 그 솜씨를 제대로 아는 사람이 떠났는데 부사수도 일지도 없으면 배에서 그 솜씨가 사라진다 (일지가 있으면 다시 배운다).

public sealed class Mentorship
{
    public Skill Skill { get; init; }
    public int Mentor { get; init; }
    public int Apprentice { get; init; }
    public long Since { get; init; }
    public float Start { get; init; }
    public bool Ended { get; set; }
    public string EndWhy { get; set; } = "";
}

public sealed class Manual
{
    public Skill Skill { get; init; }
    public int Author { get; init; }
    public string AuthorName { get; init; } = "";
    public float Progress { get; set; }
    public long Done { get; set; } = -1;
    public int Damaged { get; set; }
    public bool Ready => Done >= 0;
}

public sealed class LostSkill
{
    public Skill Skill { get; init; }
    public string Who { get; init; } = "";
    public long At { get; init; }
    public bool Manual { get; init; }
    public bool Recovered { get; set; }
}

public sealed class KnowledgeSystem
{
    private readonly World _w;
    private readonly Rng _rng;
    public static readonly Skill[] Key = { Skill.Medicine, Skill.Engineering, Skill.Electrical, Skill.Piloting, Skill.Mechanics, Skill.Botany, Skill.Cooking };
    public List<Mentorship> Pairs { get; } = new();
    public List<Manual> Manuals { get; } = new();
    public List<LostSkill> Lost { get; } = new();
    /// <summary>솜씨마다 배에서 가장 높은 수준 (지난 시각).</summary>
    public Dictionary<Skill, float> Best { get; } = new();
    private long _nextHour;
    private int _corrupt0 = -1;

    public KnowledgeSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 3907 + 61));
    }

    private IEnumerable<CrewMember> Aboard => _w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away && !c.IsChild && !c.Passenger);
    private CrewMember? P(int id) => id >= 0 && id < _w.Crew.Count ? _w.Crew[id] : null;
    public Mentorship? PairOf(Skill s) => Pairs.FirstOrDefault(p => !p.Ended && p.Skill == s);
    public Manual? ManualOf(Skill s) => Manuals.FirstOrDefault(m => m.Skill == s);
    public IEnumerable<Mentorship> Of(CrewMember c) => Pairs.Where(p => !p.Ended && (p.Mentor == c.Id || p.Apprentice == c.Id));

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        if (w.Tick < SimTime.Hours(6)) return; // 출항 첫 반나절은 자리를 잡는다
        bool newDay = w.Tick % SimTime.TicksPerDay < SimTime.Hours(1);
        var aboard = Aboard.ToList();
        // 짝이 끝났나 (떠남 · 홀로 섬)
        foreach (var p in Pairs.Where(p => !p.Ended).ToList())
        {
            var m = P(p.Mentor); var a = P(p.Apprentice);
            if (a == null || a.Dead || a.LeftShip) { End(p, "부사수가 떠났다"); continue; }
            if (m == null || m.Dead || m.LeftShip) { End(p, $"사수 {Ko.IGa(m?.Name ?? "?")} 떠났다 — {Ko.IGa(a.Name)} 홀로 남았다"); continue; }
            if (a.RawSkill(p.Skill) >= MathF.Min(0.62f, m.RawSkill(p.Skill) * 0.85f))
            {
                End(p, "부사수가 홀로 섰다");
                w.History.Add(w, HistoryKind.Milestone, $"{Ko.IGa(a.Name)} {Skills.Name(p.Skill)}에서 홀로 섰다 — {m.Name}의 부사수였다", a.Room, new[] { a, m }, log: true);
                continue;
            }
            if (newDay) Teach(p, m, a, 1f);
        }
        bool formed = false;
        foreach (var skill in Key)
        {
            var best = aboard.OrderByDescending(c => c.RawSkill(skill)).ThenBy(c => c.Id).FirstOrDefault();
            float lvl = best?.RawSkill(skill) ?? 0f;
            float prev = Best.TryGetValue(skill, out var b0) ? b0 : lvl;
            Best[skill] = lvl;
            if (prev >= 0.6f && lvl < 0.38f) Lose(skill, aboard);
            if (best == null || lvl < 0.65f) continue;
            // 사수 · 부사수 맺기
            if (!formed && PairOf(skill) == null)
            {
                var busy = Pairs.Where(p => !p.Ended).SelectMany(p => new[] { p.Mentor, p.Apprentice }).ToHashSet();
                var app = aboard.Where(c => c != best && !busy.Contains(c.Id) && c.RawSkill(skill) < lvl * 0.65f && c.Vitals.Injury < 0.3f)
                    .OrderByDescending(c => (c.BornAboard ? 0.5f : 0f) + (c.Age < 30f ? 0.3f : 0f) + 0.3f * c.Traits.Diligence + 0.3f * c.AffinityTo(best) + 0.2f * c.RawSkill(skill)).ThenBy(c => c.Id).FirstOrDefault();
                if (app != null && !busy.Contains(best.Id))
                {
                    var p = new Mentorship { Skill = skill, Mentor = best.Id, Apprentice = app.Id, Since = w.Tick, Start = app.RawSkill(skill) };
                    Pairs.Add(p);
                    formed = true; // 한 시간에 한 짝씩
                    w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(best.Name)} {app.Name}의 {Skills.Name(skill)} 사수가 되었다 — 곁에 두고 가르친다", best.Room, new[] { best, app }, log: true);
                }
            }
            // 일지: 잘하는 사람이 틈틈이 적는다
            if (newDay && lvl >= 0.75f)
            {
                var man = ManualOf(skill);
                if (man == null) { man = new Manual { Skill = skill, Author = best.Id, AuthorName = best.Name }; Manuals.Add(man); }
                if (!man.Ready) Write(man, best, 0.03f);
            }
        }
        // 일지로 혼자 익힌다 (기초까지)
        if (newDay)
            foreach (var man in Manuals.Where(m => m.Ready))
                foreach (var c in aboard.Where(c => c.RawSkill(man.Skill) < 0.4f).OrderByDescending(c => c.Traits.Diligence).Take(1))
                    c.SkillLevels[(int)man.Skill] = MathF.Min(0.45f, c.SkillLevels[(int)man.Skill] + 0.006f * (w.Automation.Present && w.Automation.MainOnline ? 1.5f : 1f));
        // 주컴퓨터 기억 칸이 틀어지면 보관한 일지 일부가 깨진다
        int corr = w.Automation.MateOrNull?.Corruptions ?? 0;
        if (_corrupt0 < 0) _corrupt0 = corr;
        if (corr > _corrupt0)
        {
            _corrupt0 = corr;
            var ready = Manuals.Where(m => m.Ready).ToList();
            if (ready.Count > 0 && _rng.Chance(0.5f))
            {
                var m = ready[_rng.Range(0, ready.Count)];
                m.Progress = 0.65f; m.Done = -1; m.Damaged++;
                w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 기억 칸이 틀어져 {m.AuthorName}의 {Skills.Name(m.Skill)} 일지 일부가 깨졌다 — 다시 적어야 한다");
            }
        }
    }

    private void Teach(Mentorship p, CrewMember m, CrewMember a, float days)
    {
        float cap = MathF.Min(0.9f, m.RawSkill(p.Skill) * 0.9f);
        float gain = 0.004f * days * (0.6f + 0.6f * a.Traits.Diligence) * (0.7f + 0.3f * MathF.Max(0f, a.AffinityTo(m) + 1f) * 0.5f);
        a.SkillLevels[(int)p.Skill] = MathF.Min(cap, MathF.Max(a.SkillLevels[(int)p.Skill], a.SkillLevels[(int)p.Skill] + gain));
    }

    private void Write(Manual man, CrewMember author, float amount)
    {
        var w = _w;
        man.Progress = MathF.Min(1f, man.Progress + amount * (0.6f + 0.6f * author.Traits.Diligence));
        if (man.Progress < 1f) return;
        man.Done = w.Tick;
        bool kept = w.Automation.Present && w.Automation.MainOnline;
        w.History.Add(w, HistoryKind.Milestone, $"{author.Name}의 {Skills.Name(man.Skill)} 일지가 완성됐다" + (kept ? " — 주 컴퓨터가 보관한다" : " — 종이로 남겼다"), author.Room, new[] { author }, log: true);
    }

    private void End(Mentorship p, string why)
    {
        p.Ended = true;
        p.EndWhy = why;
        _w.Log.Add(_w.Tick, LogKind.Life, $"{Skills.Name(p.Skill)} 사수 · 부사수 — {why}");
    }

    /// <summary>그 솜씨를 제대로 아는 사람이 배에서 없어졌다.</summary>
    private void Lose(Skill skill, List<CrewMember> aboard)
    {
        var w = _w;
        var gone = w.Crew.Where(c => (c.Dead || c.LeftShip) && c.RawSkill(skill) >= 0.6f).OrderByDescending(c => c.Dead ? c.DiedAt : 0).FirstOrDefault();
        var man = ManualOf(skill);
        bool hasManual = man is { Ready: true };
        Lost.Add(new LostSkill { Skill = skill, Who = gone?.Name ?? "?", At = w.Tick, Manual = hasManual });
        string text = hasManual
            ? $"{Skills.Name(skill)} — {Ko.IGa(gone?.Name ?? "?")} 떠나 제대로 아는 사람이 없다 · {man!.AuthorName}의 일지가 남아 다시 배운다"
            : $"{Ko.EulReul(Skills.Name(skill))} 잃었다 — {Ko.IGa(gone?.Name ?? "?")} 떠나며 그 손맛도 같이 떠났다 (부사수도 일지도 없었다)";
        w.History.Add(w, HistoryKind.Lesson, text, null, log: true);
        w.RaiseAlert(text, null, AlertLevel.Warning, shipWide: true);
        if (hasManual && aboard.OrderByDescending(c => c.RawSkill(skill)).FirstOrDefault() is CrewMember next)
            next.SkillLevels[(int)skill] = MathF.Max(next.SkillLevels[(int)skill], 0.35f); // 일지를 펴 들고 바로 시작한다
    }

    /// <summary>열네 살이 된 아이: 부사수 자리부터 찾는다 — 빈자리가 없으면 배에서 태어나지 않은 부사수의 자리를 이어받는다.</summary>
    public void NewAdult(CrewMember c)
    {
        var w = _w;
        CrewMember? MentorOf(Skill s) => Aboard.Where(x => x != c && x.RawSkill(s) >= 0.65f).OrderByDescending(x => x.RawSkill(s)).ThenBy(x => x.Id).FirstOrDefault();
        var order = Key.OrderByDescending(s => c.RawSkill(s)).ToList();
        Skill? pick = order.Where(s => PairOf(s) == null && MentorOf(s) != null).Cast<Skill?>().FirstOrDefault();
        if (pick == null)
        {
            var take = order.Select(PairOf).FirstOrDefault(p => p != null && P(p.Apprentice) is CrewMember ca && !ca.BornAboard);
            if (take == null) return;
            End(take, $"{Ko.IGa(c.Name)} 그 자리를 이어받았다");
            pick = take.Skill;
        }
        var skill = pick.Value;
        if (MentorOf(skill) is not CrewMember mentor) return;
        Pairs.Add(new Mentorship { Skill = skill, Mentor = mentor.Id, Apprentice = c.Id, Since = w.Tick, Start = c.RawSkill(skill) });
        w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} {mentor.Name} 곁에서 {Ko.EulReul(Skills.Name(skill))} 배우기 시작했다 — 첫 부사수 자리", c.Room, new[] { c, mentor }, log: true);
    }

    /// <summary>조용한 달: 부사수는 크게 배우고, 사수는 일지를 적는다.</summary>
    public void QuietMonths(int months, QuietSpan span)
    {
        foreach (var p in Pairs.Where(p => !p.Ended).ToList())
        {
            if (P(p.Mentor) is not CrewMember m || P(p.Apprentice) is not CrewMember a || m.Dead || a.Dead) continue;
            float before = a.RawSkill(p.Skill);
            Teach(p, m, a, 10f * months);
            if (a.RawSkill(p.Skill) - before >= 0.05f) span.Lines.Add($"{Ko.IGa(a.Name)} {m.Name} 곁에서 {Ko.EulReul(Skills.Name(p.Skill))} 익혔다 ({before * 100:0} → {a.RawSkill(p.Skill) * 100:0})");
        }
        foreach (var man in Manuals.Where(x => !x.Ready).ToList())
            if (P(man.Author) is CrewMember au && !au.Dead && !au.LeftShip)
            {
                Write(man, au, 0.1f * months);
                if (man.Ready) span.Lines.Add($"{au.Name}의 {Skills.Name(man.Skill)} 일지가 완성됐다");
            }
    }
}
