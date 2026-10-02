using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.9 세대선: 사람이 나이 들고, 가까운 둘이 짝이 되고, 아이가 태어나 자라 배를 물려받는다.
// 세대선 모드에서는 시간이 빠르게 흐른다 (기본 사흘 = 한 해). 아이는 일하지 않고 놀며 배우고(학교가 있으면 빨리),
// 열네 살이 되면 부모를 닮은 역할로 일을 시작한다. 일흔이 넘으면 몸이 약해지고, 언젠가 조용히 떠난다.
// 해마다 배는 지난 일을 기린다 (기념일) — 세대가 바뀌어도 이야기는 남는다.

public sealed class GenerationSystem
{
    private readonly World _w;
    public bool Enabled { get; private set; }
    public static float YearDaysValue { get; set; } = 3f;
    public float YearDays => YearDaysValue;
    public int Births, Comings, Elders, Anniversaries;
    private float _yearClock;
    private readonly Rng _rng;

    public GenerationSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 911 + 5));
    }

    public void Enable()
    {
        if (Enabled) return;
        Enabled = true;
        _w.History.Add(_w, HistoryKind.Decision, $"세대선이 되었다 — 이제 {YearDays:0}일이 한 해다", null, log: true);
    }

    /// <summary>시스템 틱.</summary>
    public void Update(float dt)
    {
        var w = _w;
        float years = Enabled ? dt / 24f / YearDays : dt / 24f / 365f;
        foreach (var c in w.Crew.Where(c => !c.Dead))
        {
            bool wasChild = c.IsChild;
            c.Age += years;
            if (wasChild && !c.IsChild) ComeOfAge(c);
            if (c.IsChild)
            {
                // 놀면서 배운다 — 학교가 있으면 두 배
                float school = Facilities.Best(w.Ship, "learning").factor;
                // v13.4 방침(아이 교육): 실무는 부모 곁에서 더 빨리 · 학교는 고르게 · 자유는 덜 배우지만 덜 지친다
                int edu = w.Policies["education"];
                float rate = edu == 0 ? 1.3f : edu == 2 ? 0.7f : 1f + school;
                for (int s = 0; s < c.SkillLevels.Length; s++)
                    c.SkillLevels[s] = MathF.Min(edu == 0 ? 0.6f : 0.5f, c.SkillLevels[s] + years * 0.02f * rate);
                if (edu == 2) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.002f * dt);
            }
            c.Vitals.Frailty = Math.Clamp((c.Age - 68f) / 35f, 0f, 0.6f);
            if (c.Age > 70f)
            {
                c.Vitals.Health = MathF.Min(c.Vitals.Health, c.Vitals.MaxHealth);
                if (Enabled && w.CrewCanDie && _rng.Chance(years * 0.04f * (c.Age - 68f))) { c.Vitals.InjuryCause = "노환"; c.Vitals.Health = 0f; Elders++; }
            }
        }
        if (!Enabled) return;
        _yearClock += years;
        if (_yearClock >= 1f) { _yearClock -= 1f; NewYear(); }
        // 짝과 아이
        var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Age is >= 20f and <= 45f).ToList();
        int beds = w.Ship.FurnitureOf(FurnitureType.Bed).Count() + w.Ship.FurnitureOf(FurnitureType.Cot).Count();
        int alive = w.Crew.Count(c => !c.Dead);
        bool room = alive < Math.Min(World.MaxCrew, beds + 2) && w.Ship.CountStored(ItemKind.Meal) + w.Ship.CountStored(ItemKind.Produce) > alive * 3;
        // v13.4 방침(짝·출산: 수용력에 맞춘 허가) — 빈 침대가 있고 먹을 것이 두 배로 넉넉할 때만
        if (w.Policies["birth"] == 1) room &= alive < beds && w.Ship.CountStored(ItemKind.Meal) + w.Ship.CountStored(ItemKind.Produce) > alive * 6;
        foreach (var a in adults)
        {
            if (a.Partner is int pid)
            {
                var b = w.Crew.FirstOrDefault(x => x.Id == pid);
                if (b == null || b.Dead) { a.Partner = null; continue; }
                if (a.Id < b.Id && room && b.Age <= 45f && _rng.Chance(0.35f * years)) Birth(a, b);
                continue;
            }
            var mate = adults.Where(b => b != a && b.Partner == null && a.AffinityTo(b) > 0.55f && b.AffinityTo(a) > 0.55f).OrderByDescending(b => a.AffinityTo(b)).FirstOrDefault();
            if (mate != null)
            {
                a.Partner = mate.Id; mate.Partner = a.Id;
                w.History.Add(w, HistoryKind.Bond, $"{Ko.WaGwa(a.Name)} {Ko.IGa(mate.Name)} 짝이 되었다", a.Room, new[] { a, mate }, log: true);
            }
        }
    }

    private void Birth(CrewMember a, CrewMember b)
    {
        var w = _w;
        var c = w.AddChild(a, b, _rng);
        Births++;
        w.History.Add(w, HistoryKind.Bond, $"{Ko.WaGwa(a.Name)} {b.Name} 사이에 {Ko.IGa(c.Name)} 태어났다 — 배에서 태어난 {Births}번째 아이", a.Room, new[] { a, b }, log: true);
        Life.Diary(w, a, $"{Ko.IGa(c.Name)} 태어났다.");
        Life.Diary(w, b, $"{Ko.IGa(c.Name)} 태어났다.");
        foreach (var o in w.Crew.Where(o => !o.Dead)) o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.1f);
    }

    private void ComeOfAge(CrewMember c)
    {
        var w = _w;
        Comings++;
        w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} 열네 살 — 이제 {CrewRoles.Name(c.Role)} 일을 배운다", c.Room, new[] { c }, log: true);
        Life.Diary(w, c, "오늘부터 일을 한다.");
    }

    /// <summary>새해: 지난 일 하나를 기린다 (가장 큰 사고, 아니면 첫 항해).</summary>
    private void NewYear()
    {
        var w = _w;
        Anniversaries++;
        var big = w.Causes.Incidents.OrderByDescending(i => i.Weight(w.Causes)).FirstOrDefault();
        string what = big != null ? w.Causes.Node(big.Root).Text : "첫 출항";
        foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
        w.History.Add(w, HistoryKind.Decision, $"새해 — 배가 {Anniversaries}번째 해를 맞았다 · 기념일: {what}", null, log: true);
    }
}
