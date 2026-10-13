using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 외부 세계 기억: 고향 · 남긴 사람 · 늦은 편지(Mail.cs)에 더해 —
//   향수병: 집을 떠나 있는 동안 조금씩 커진다 (향수병 기질 · 승객은 더) · 편지 · 고향 재료 · 기항지 · 축하가 누그러뜨린다 · 부고는 키운다.
//     깊어지면 마음이 무겁고 일기에 고향이 나온다 · 아주 깊으면 다음 기항지에서 내리고 싶어 한다. 배에서 태어난 아이에게는 배가 고향이다.
//   기념일: 사람마다 생일 (배의 달력으로) · 떠난 동료의 추모일 · 배의 생일. 조용히 넘긴 달 동안의 생일은 건너뛴 채 지나간다.

public sealed class HomelandSystem
{
    private readonly World _w;
    private readonly Rng _rng;
    public Dictionary<int, float> Homesick { get; } = new();
    public Dictionary<int, int> BirthDay { get; } = new();
    /// <summary>떠난 사람의 추모일 (배의 달력 날).</summary>
    public Dictionary<int, float> MemDay { get; } = new();
    public HashSet<string> Kept { get; } = new();
    public int Birthdays, Memorials, ShipBirthdays, SkippedBirthdays;
    private long _nextHour;
    private float _lastDay = -1f;

    public HomelandSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 5113 + 71));
    }

    /// <summary>하루에 자라는 향수병 (향수병 기질 · 승객은 더 · 아이는 덜 · 배에서 태어나면 없음).</summary>
    public static float GrowPerDay(CrewMember c) => c.BornAboard ? 0f : 0.022f * (Life.Has(c, Habit.Homesick) ? 1.6f : 1f) * (c.Passenger ? 1.3f : 1f) * (c.IsChild ? 0.3f : 1f);

    public float Of(CrewMember c) => Homesick.TryGetValue(c.Id, out var h) ? h : 0f;
    public bool WantsHome(CrewMember c) => Of(c) >= 0.85f;
    public string HomeOf(CrewMember c) => c.BornAboard ? $"{_w.Ship.Name} (배에서 태어남)" : StorySystem.Homes[Math.Clamp(_w.Tales.RootsOf(c).Home, 0, StorySystem.Homes.Length - 1)];
    public IEnumerable<string> LeftBehind(CrewMember c) => _w.Personal.Mail.Of(c).Where(k => k.Kin && k.Alive).Select(k => $"{k.Rel} {k.Name}");

    private IEnumerable<CrewMember> Aboard => _w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away);

    private void Bump(CrewMember c, float d)
    {
        if (c.BornAboard) { Homesick[c.Id] = 0f; return; }
        Homesick[c.Id] = Math.Clamp(Of(c) + d, 0f, 1f);
    }

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        float day = w.Generation.CalendarDays;
        foreach (var c in Aboard)
        {
            if (!BirthDay.ContainsKey(c.Id)) { BirthDay[c.Id] = c.BornAboard ? (int)(day % 360f) : _rng.Range(0, 360); Bump(c, _rng.Range(0f, 0.2f)); }
            Bump(c, GrowPerDay(c) / 24f);
            float h = Of(c);
            if (h > 0.6f) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.004f * (h - 0.6f) / 0.4f);
            if (h > 0.6f && w.Tick % SimTime.TicksPerDay < SimTime.Hours(1) && _rng.Chance(0.5f))
            {
                string home = HomeOf(c);
                w.Brain2.Emotions.Feel(c, Feeling.Sadness, 0.1f * h, $"{home} 생각");
                Life.Diary(w, c, Persona.Say(c, h > 0.85f ? $"{home}에 가고 싶다. 다음 기항지에서 내릴까." : $"{home} 꿈을 꿨다."));
            }
        }
        // 떠난 사람: 추모일
        foreach (var c in w.Crew.Where(c => c.Dead && !MemDay.ContainsKey(c.Id))) MemDay[c.Id] = day;
        if (_lastDay < 0f) { _lastDay = day; return; }
        if (day <= _lastDay) return;
        Anniversaries(_lastDay, day, quiet: false);
        _lastDay = day;
    }

    /// <summary>배의 달력이 from → to 로 넘어가는 동안의 기념일.</summary>
    private void Anniversaries(float from, float to, bool quiet, QuietSpan? span = null)
    {
        var w = _w;
        bool Crossed(float mark) { for (float k = MathF.Floor(from / 360f); k <= MathF.Floor(to / 360f); k++) { float d = k * 360f + mark; if (d > from && d <= to) return true; } return false; }
        int year = (int)(to / 360f);
        // 생일
        foreach (var c in Aboard.ToList())
        {
            if (!BirthDay.TryGetValue(c.Id, out var bd) || !Crossed(bd)) continue;
            string key = $"bd:{c.Id}:{year}";
            if (!Kept.Add(key)) continue;
            if (quiet) { SkippedBirthdays++; continue; }
            Birthdays++;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.12f);
            w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.2f, "생일");
            Bump(c, -0.05f);
            foreach (var f in Aboard.Where(o => o != c && o.AffinityTo(c) > 0.3f).Take(4)) w.Brain2.Emotions.Feel(f, Feeling.Joy, 0.06f, $"{c.Name}의 생일", c);
            bool big = c.IsChild || (int)c.Age % 10 == 0;
            w.History.Add(w, big ? HistoryKind.Bond : HistoryKind.Memory, $"{c.Name}의 생일 — {(int)c.Age}살 · 다 같이 노래를 불렀다", c.Room, new[] { c }, log: true);
        }
        // 추모일
        foreach (var (id, md) in MemDay.ToList())
        {
            if (!Crossed(md + 360f) && !Crossed(md)) continue;
            int k = (int)((to - md) / 360f);
            if (k < 1) continue;
            string key = $"mem:{id}:{k}";
            if (!Kept.Add(key)) continue;
            var d = w.Crew[id];
            if (quiet) { span?.Lines.Add($"{Ko.IGa(d.Name)} 떠난 지 {k}년 — 조용히 기렸다"); continue; }
            Memorials++;
            foreach (var f in Aboard.Where(o => o.AffinityTo(d) > 0.2f)) { w.Brain2.Emotions.Feel(f, Feeling.Sadness, 0.12f, $"{d.Name}의 추모일", d); f.Needs.Stress = MathF.Max(0f, f.Needs.Stress - 0.04f); }
            w.History.Add(w, HistoryKind.Memory, $"추모 — {Ko.IGa(d.Name)} 떠난 지 {k}년 · 모두 잠시 일손을 놓았다", null, log: true);
        }
        // 배의 생일
        if (Crossed(0.001f) && to >= 360f && Kept.Add($"ship:{year}"))
        {
            ShipBirthdays++;
            foreach (var c in Aboard) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
            string text = $"배의 생일 — {Ko.IGa(w.Ship.Name)} 처음 떠난 지 {year}년";
            if (quiet) span?.Lines.Add(text);
            else w.History.Add(w, HistoryKind.Milestone, text, null, log: true);
        }
    }

    /// <summary>승무원 카드 한 줄: 사수 · 부사수 · 고향 · 남긴 사람 · 향수병.</summary>
    public string? CardLine(CrewMember c)
    {
        var w = _w;
        var parts = new List<string>();
        foreach (var p in w.Knowledge.Of(c))
            parts.Add(p.Mentor == c.Id ? $"{Skills.Name(p.Skill)} 사수 — 부사수 {w.Crew[p.Apprentice].Name}" : $"{Skills.Name(p.Skill)} 부사수 — 사수 {w.Crew[p.Mentor].Name}");
        if (!c.BornAboard)
        {
            string s = $"고향 {HomeOf(c)}";
            var kin = LeftBehind(c).FirstOrDefault();
            if (kin != null) s += $" · {kin}";
            float h = Of(c);
            if (h >= 0.3f) s += $" · 향수병 {h * 100:0}%" + (WantsHome(c) ? " — 내리고 싶다" : "");
            parts.Add(s);
        }
        else parts.Add("배에서 태어남 — 배가 고향");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>편지가 닿았다 (Mail.cs).</summary>
    public void Letter(CrewMember c, News kind) => Bump(c, kind switch { News.Hello => -0.025f, News.Parcel => -0.05f, News.Wedding or News.Birth => -0.01f, News.Death => 0.15f, News.Ill => 0.05f, _ => 0f });

    /// <summary>기항지: 사람 · 소식 · 고향 재료 — 모두 조금 누그러진다.</summary>
    public void Port() { foreach (var c in Aboard) Bump(c, -0.15f); }

    /// <summary>조용한 달: 향수병이 깊어지고 · 그동안의 생일은 건너뛴 채 지나간다.</summary>
    public void QuietMonths(int months, QuietSpan span, float fromDay)
    {
        int skipped0 = SkippedBirthdays;
        Anniversaries(fromDay, _w.Generation.CalendarDays, quiet: true, span);
        _lastDay = _w.Generation.CalendarDays;
        foreach (var c in Aboard) Bump(c, 0.03f * months * (Life.Has(c, Habit.Homesick) ? 1.5f : 1f));
        if (SkippedBirthdays > skipped0) span.Lines.Add($"그동안 생일 {SkippedBirthdays - skipped0}번이 조촐하게 지나갔다");
        var worst = Aboard.Where(c => !c.BornAboard).OrderByDescending(Of).FirstOrDefault();
        if (worst != null && Of(worst) >= 0.6f) span.Lines.Add($"{Ko.IGa(worst.Name)} {Ko.EulReul(HomeOf(worst))} 자주 이야기했다 (향수병 {Of(worst) * 100:0}%)");
    }
}
