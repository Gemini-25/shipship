using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 세대 · 조용한 달 넘기기: 긴 순항 구간 한가운데가 조용하면 몇 달을 이야기 시간으로 건너뛴다 (배의 시계는 그대로 — 이야기 달력만 넘어간다).
//   그동안: 나이 · 짝 · 아이 탄생 · 아이 배움 · 열네 살 성인 · 예순다섯 은퇴 · 노환 · 손에 붙은 솜씨 · 낡아 가는 설비 · 사람마다 남는 한 줄 기억.
//   첫 항해는 열이틀째부터 (배를 길들이는 동안엔 넘기지 않는다).

public sealed class QuietSpan
{
    public long Tick { get; init; }
    public int Months { get; init; }
    public string Leg { get; init; } = "";
    public List<string> Lines { get; } = new();
    public int Born, Paired, Grown, Retired, Passed;
}

public sealed partial class GenerationSystem
{
    /// <summary>시험용: 저절로 넘기지 않는다.</summary>
    public static bool QuietOff;
    public float Months { get; private set; }
    public long LastSkip { get; private set; } = -1;
    public List<QuietSpan> Spans { get; } = new();
    private long _nextQuietCheck;

    private static bool CalmLeg(LegKind k) => k is LegKind.Cruise or LegKind.DeepVoid or LegKind.TradeLane or LegKind.PatrolLane or LegKind.Lagrange or LegKind.SolarWind;

    /// <summary>한 시간마다: 조용한 긴 구간 한가운데인가.</summary>
    private void QuietCheck()
    {
        var w = _w;
        if (QuietOff || Enabled || w.Tick < _nextQuietCheck) return;
        _nextQuietCheck = w.Tick + SimTime.Hours(1);
        var v = w.Voyage;
        if (v.Number == 1 && w.Tick < SimTime.TicksPerDay * 12) return;
        if (LastSkip >= 0 && w.Tick - LastSkip < SimTime.TicksPerDay * 3) return;
        var leg = v.Current;
        if (!CalmLeg(leg.Kind) || leg.Days < 2.5f) return;
        float f = v.Progress / leg.Days;
        if (f < 0.35f || f > 0.65f) return;
        if (w.Commander.Cases.Any(k => k.Open) || w.Cosmic.Main != null || w.Planet.Holding || w.Expedition.Current != null) return;
        if (w.Ship.LiveRooms.Any(r => r.Leaking || w.Fire.CountIn(r) > 0)) return;
        if (w.Crew.Any(c => !c.Dead && !c.LeftShip && c.Vitals.Health < 0.5f)) return;
        int months = Math.Clamp((int)MathF.Round(leg.Days * 0.8f), 1, 6);
        SkipMonths(months, leg.Name == "순항" ? VoyageSystem.KindName(leg.Kind) : leg.Name);
    }

    private static string MonthsName(int m) => m switch { 1 => "한 달", 2 => "두 달", 3 => "석 달", 4 => "넉 달", 5 => "다섯 달", 6 => "여섯 달", _ => $"{m}달" };

    /// <summary>몇 달을 건너뛴다 (이야기 시간): 사람 · 배에 그만큼의 시간이 지난다.</summary>
    public QuietSpan SkipMonths(int months, string where)
    {
        var w = _w;
        float years = months / 12f;
        var span = new QuietSpan { Tick = w.Tick, Months = months, Leg = where };
        var people = w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away).ToList();
        int edu = w.Policies["education"];
        float school = Facilities.Best(w.Ship, "learning").factor;
        foreach (var c in people)
        {
            bool wasChild = c.IsChild;
            float before = c.Age;
            c.Age += years;
            if (wasChild)
            {
                float rate = edu == 0 ? 1.3f : edu == 2 ? 0.7f : 1f + school;
                for (int s = 0; s < c.SkillLevels.Length; s++) c.SkillLevels[s] = MathF.Min(edu == 0 ? 0.6f : 0.5f, c.SkillLevels[s] + years * 0.25f * rate);
                if (!c.IsChild) { ComeOfAge(c); span.Grown++; span.Lines.Add($"{Ko.IGa(c.Name)} 열네 살이 되어 일을 배우기 시작했다"); }
            }
            else
            {
                // 손에 붙는 솜씨 (맡은 일)
                int best = 0;
                for (int s = 1; s < c.SkillLevels.Length; s++) if (c.SkillLevels[s] > c.SkillLevels[best]) best = s;
                c.SkillLevels[best] = MathF.Min(1f, c.SkillLevels[best] + 0.01f * months);
            }
            if (before < 65f && c.Age >= 65f)
            {
                span.Retired++;
                bool rest = w.Policies["elders"] == 0;
                span.Lines.Add(rest ? $"{Ko.IGa(c.Name)} 예순다섯 — 은퇴했다 (급한 일 말고는 쉰다)" : $"{Ko.IGa(c.Name)} 예순다섯 — 할 수 있는 만큼 더 일하겠다고 했다");
                w.History.Add(w, HistoryKind.Milestone, rest ? $"{Ko.IGa(c.Name)} 은퇴했다 — 예순다섯" : $"{Ko.IGa(c.Name)} 예순다섯 — 은퇴를 미뤘다", c.Room, new[] { c }, log: true);
            }
            c.Vitals.Frailty = Math.Clamp((c.Age - 68f) / 35f, 0f, 0.6f);
            if (c.Age > 70f && w.CrewCanDie && _rng.Chance(MathF.Min(0.9f, months * 0.004f * (c.Age - 68f))))
            {
                c.Vitals.InjuryCause = "노환";
                c.Vitals.Health = 0f;
                Elders++;
                span.Passed++;
                span.Lines.Add($"{Ko.IGa(c.Name)} {(int)c.Age}세로 조용히 눈을 감았다");
            }
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.08f);
        }
        // 짝
        var adults = people.Where(c => !c.Dead && !c.IsChild && c.Age is >= 20f and <= 50f && !c.Passenger).ToList();
        foreach (var a in adults.Where(a => a.Partner == null).ToList())
        {
            if (a.Partner != null) continue;
            var mate = adults.Where(b => b != a && b.Partner == null && a.AffinityTo(b) > 0.45f && b.AffinityTo(a) > 0.45f).OrderByDescending(b => a.AffinityTo(b)).ThenBy(b => b.Id).FirstOrDefault();
            if (mate == null || !_rng.Chance(MathF.Min(0.9f, 0.2f * months))) continue;
            a.Partner = mate.Id; mate.Partner = a.Id;
            span.Paired++;
            span.Lines.Add($"{Ko.WaGwa(a.Name)} {Ko.IGa(mate.Name)} 짝이 되었다");
            w.History.Add(w, HistoryKind.Bond, $"{Ko.WaGwa(a.Name)} {Ko.IGa(mate.Name)} 짝이 되었다 — 조용한 {MonthsName(months)} 동안", a.Room, new[] { a, mate }, log: true);
        }
        // 아이
        int beds = w.Ship.FurnitureOf(FurnitureType.Bed).Count() + w.Ship.FurnitureOf(FurnitureType.Cot).Count();
        int alive = w.Crew.Count(c => !c.Dead && !c.LeftShip);
        bool room = alive < Math.Min(World.MaxCrew, beds + 2) && (w.Policies["birth"] == 0 || alive < beds);
        foreach (var a in adults.Where(a => a.Partner is int p && a.Id < p && a.Age <= 45f).ToList())
        {
            var b = w.Crew.FirstOrDefault(x => x.Id == a.Partner);
            if (b == null || b.Dead || b.LeftShip || b.Age > 45f || !room || !_rng.Chance(MathF.Min(0.6f, 0.035f * months))) continue;
            Birth(a, b);
            span.Born++;
            span.Lines.Add($"{Ko.WaGwa(a.Name)} {b.Name} 사이에 아이가 태어났다");
            alive++;
            room = alive < Math.Min(World.MaxCrew, beds + 2);
        }
        // 가까워지는 사이
        foreach (var a in people.Where(c => !c.Dead))
            foreach (var b in people.Where(c => !c.Dead && c.Id > a.Id))
                if (a.AffinityTo(b) > 0.25f) { a.ChangeAffinity(b, 0.015f * months); b.ChangeAffinity(a, 0.015f * months); }
        // 배도 나이를 먹는다
        foreach (var m in w.Ship.Machines) m.Wear = MathF.Min(0.95f, m.Wear + 0.008f * months);
        // 사람마다 한 줄 기억
        string[] marks = { "창밖 별을 세며 지냈다", "새 취미를 붙였다", "편지를 몇 통 썼다", "밤마다 일지를 적었다", "운동을 거르지 않았다", "아이들에게 글을 가르쳤다", "고향 노래를 다시 외웠다", "식당에서 늦게까지 이야기했다" };
        foreach (var c in people.Where(c => !c.Dead))
            MarkLog.Add(c.Memory.Marks, w.Tick, $"조용한 {MonthsName(months)} — {marks[_rng.Range(0, marks.Length)]}");
        Months += months;
        LastSkip = w.Tick;
        Spans.Add(span);
        if (Spans.Count > 40) Spans.RemoveAt(0);
        string sum = $"조용한 {MonthsName(months)}이 흘렀다 ({where})" + (span.Lines.Count > 0 ? " — " + string.Join(" · ", new[] { span.Born > 0 ? $"태어난 아이 {span.Born}" : "", span.Paired > 0 ? $"짝 {span.Paired}" : "", span.Grown > 0 ? $"성인 {span.Grown}" : "", span.Retired > 0 ? $"예순다섯 {span.Retired}" : "", span.Passed > 0 ? $"떠난 이 {span.Passed}" : "" }.Where(s => s.Length > 0)) : " — 별일 없이");
        w.Log.Add(w.Tick, LogKind.Ship, sum);
        w.History.Add(w, span.Born + span.Passed + span.Paired > 0 ? HistoryKind.Milestone : HistoryKind.Memory, sum, null, log: false);
        return span;
    }

    /// <summary>배가 살아온 이야기 시간 (해 · 달).</summary>
    public string Calendar
    {
        get
        {
            float days = _w.Tick / (float)SimTime.TicksPerDay;
            float m = Months + days / 30f;
            int y = (int)(m / 12f), mo = (int)(m % 12f);
            return y > 0 ? $"{y}년 {mo}달째" : $"{mo + 1}달째";
        }
    }
}
