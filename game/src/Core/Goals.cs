using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.15 승무원 두뇌 2.0 — ② 목표 층.
// 장기: 꿈 · 개인 이야기 (가치관 · 배경 · 취미 · 습관에서) — 몇 달을 가는 마음.
// 중기: 프로젝트 · 원정 · 별난 짓 계획 · 화해 · 추모 · 망가진 물건 (다른 시스템이 채우는 자리).
// 단기: 욕구 · 지금 하는 일 (v13.3 목표 계층 생존 > 역할 > 일 > 생활과 나란히 보인다).
// 장기 · 중기 목표가 행동 점수를 기울인다 (정원을 꿈꾸는 사람은 수확을 · 화해하려는 사람은 말 걸기를 조금 더).

public enum GoalLayer : byte { Long, Mid, Short }

public sealed class CrewGoal
{
    public GoalLayer Layer;
    public string Key = "";
    public string Text = "";
    public string Why = "";
    public ActCat Cat;
    public float Weight = 1f;
    public long Since;
    public long Until = long.MaxValue;
    /// <summary>다른 시스템이 밀어 넣은 목표 (다시 셀 때 지우지 않는다 — 기한까지).</summary>
    public bool Pushed;
}

public sealed class GoalSystem
{
    private readonly World _w;
    private readonly Dictionary<int, List<CrewGoal>> _g = new();
    private int _phase;
    public int Pushes, Refreshes;

    public GoalSystem(World w) => _w = w;

    public List<CrewGoal> Of(CrewMember c)
    {
        if (!_g.TryGetValue(c.Id, out var l))
        {
            _g[c.Id] = l = new List<CrewGoal>();
            Dream(c, l);
        }
        return l;
    }

    private static readonly List<CrewGoal> NoGoals = new();
    /// <summary>읽기 전용 (없으면 만들지 않는다 — 화면용).</summary>
    public IReadOnlyList<CrewGoal> Peek(CrewMember c) => _g.TryGetValue(c.Id, out var l) ? l : NoGoals;

    public bool Has(CrewMember c, string key) => _g.TryGetValue(c.Id, out var l) && l.Any(g => g.Key == key && g.Until > _w.Tick);

    public IEnumerable<CrewGoal> Layer(CrewMember c, GoalLayer layer) => Of(c).Where(g => g.Layer == layer && g.Until > _w.Tick);

    /// <summary>장 · 중기 목표가 이 행동 종류를 얼마나 기울이나 (1 = 그대로).</summary>
    public float Tilt(CrewMember c, ActCat cat)
    {
        if (!_g.TryGetValue(c.Id, out var l)) return 1f;
        float m = 1f;
        foreach (var g in l)
            if (g.Cat == cat && g.Until > _w.Tick) m += (g.Layer == GoalLayer.Long ? 0.12f : 0.2f) * g.Weight;
        return MathF.Min(1.5f, m);
    }

    /// <summary>원정 · 화해 같은 중기 목표가 다른 시스템의 판단을 민다 (Expedition.Initiative 등).</summary>
    public float Urge(CrewMember c, string key) => _g.TryGetValue(c.Id, out var l) && l.FirstOrDefault(g => g.Key == key && g.Until > _w.Tick) is CrewGoal g ? 0.3f * g.Weight : 0f;

    /// <summary>다른 시스템이 중기 목표를 넣는다 (계획이 막혀 "원정을 꺼내 보자" · 부탁받은 일 …).</summary>
    public CrewGoal Push(CrewMember c, string key, string text, string why, ActCat cat, float hours, float weight = 1f)
    {
        var l = Of(c);
        var g = l.FirstOrDefault(x => x.Key == key && x.Layer == GoalLayer.Mid);
        if (g == null) { g = new CrewGoal { Layer = GoalLayer.Mid, Key = key, Since = _w.Tick }; l.Add(g); Pushes++; }
        g.Text = text;
        g.Why = why;
        g.Cat = cat;
        g.Weight = weight;
        g.Pushed = true;
        g.Until = _w.Tick + SimTime.Hours(hours);
        return g;
    }

    /// <summary>꿈 (장기): 가치관이 첫째, 취미 · 습관 · 배경이 둘째.</summary>
    private void Dream(CrewMember c, List<CrewGoal> l)
    {
        long now = _w.Tick;
        void Add(string key, string text, string why, ActCat cat, float weight = 1f) =>
            l.Add(new CrewGoal { Layer = GoalLayer.Long, Key = key, Text = text, Why = why, Cat = cat, Weight = weight, Since = now });
        if (c.IsChild) { Add("grow", "어른들처럼 배를 돌보기", "배에서 자랐다", ActCat.Work); return; }
        string role = CrewRoles.Name(c.Role);
        switch (c.Value)
        {
            case CrewValue.Safety: Add("safe", "모두 무사히 항해를 마치기", "안전이 먼저", ActCat.Care); break;
            case CrewValue.Efficiency: Add("master", $"배에서 제일가는 {role}", "일 잘하는 게 자랑", ActCat.Work); break;
            case CrewValue.People: Add("home", "이 배를 집처럼 만들기", "사람이 먼저", ActCat.Social); break;
            case CrewValue.Rules: Add("record", "흠 없는 근무 기록", "규칙이 배를 지킨다", ActCat.Duty); break;
            default: Add("ownship", "언젠가 내 배를 갖기", "얽매이기 싫다", ActCat.Explore); break;
        }
        if (c.Hobbies.Contains(Hobby.Gardening) || c.Role == CrewRole.Botanist) Add("garden", "배를 초록으로 가득 채우기", c.Hobbies.Contains(Hobby.Gardening) ? "흙 만지는 게 좋다" : "식물을 돌보는 사람", ActCat.Work, 0.8f);
        else if (Life.Has(c, Habit.Homesick)) Add("gohome", "살아서 집에 돌아가기", "고향이 그립다", ActCat.Rest, 0.8f);
        else if (c.Hobbies.Contains(Hobby.Stargazing)) Add("star", "새 별을 찾아 이름 붙이기", "밤하늘을 좋아한다", ActCat.Hobby, 0.8f);
        else if (c.Hobbies.Contains(Hobby.Writing) || c.Hobbies.Contains(Hobby.Journaling)) Add("book", "이 배의 이야기를 책으로", "쓰는 사람", ActCat.Hobby, 0.8f);
        else if (c.Hobbies.Count > 0) Add("hobby", $"{Persona.Of(c.Hobbies[0]).Name} 실력을 끝까지", "좋아하는 일", ActCat.Hobby, 0.6f);
    }

    /// <summary>중기 목표를 다시 센다 (다른 시스템의 상태에서): 계획 · 원정 · 망가진 물건 · 추모 · 화해 · 별난 짓.</summary>
    private void Refresh(CrewMember c)
    {
        var w = _w;
        long now = w.Tick;
        var l = Of(c);
        l.RemoveAll(g => g.Layer == GoalLayer.Mid && (!g.Pushed || g.Until <= now));
        void Add(string key, string text, string why, ActCat cat, float weight = 1f)
        {
            if (l.Any(g => g.Key == key && g.Layer == GoalLayer.Mid)) return;
            l.Add(new CrewGoal { Layer = GoalLayer.Mid, Key = key, Text = text, Why = why, Cat = cat, Weight = weight, Since = now });
        }
        if (w.Brain2.Plans.Current(c) is CrewPlan p) Add("plan", $"계획 — {p.Goal}", p.Why, ActCat.Plan);
        var ex = w.Expedition;
        if (ex.Current is Trip t && t.Members.Any(m => m.Id == c.Id)) Add("trip", $"원정 — {t.Site.Name}", "원정대", ActCat.Explore);
        else if (ex.Pending is ExpProposal pp && (pp.Team.Contains(c.Id) || pp.Volunteers.Any(v => v.who == c.Id))) Add("trip", $"원정 준비 — {pp.Site.Name}", "손을 들었다", ActCat.Explore);
        if (w.Belongings.Of(c).FirstOrDefault(b => b.Condition < 0.4f) is Belonging bb) Add("mend", $"{Ko.EulReul(bb.Name)} 고치기", "망가졌다", ActCat.Care, 0.7f);
        if (c.GriefUntil > now && w.Life.Memorial.Count > 0) Add("mourn", $"{w.Life.Memorial[^1].name}을(를) 기리기", "떠난 사람", ActCat.Care);
        if (c.Quarrel > 0 && now - c.Quarrel < SimTime.TicksPerDay * 3)
        {
            var foe = w.Crew.Where(o => o != c && !o.Dead).OrderBy(o => c.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
            if (foe != null && c.AffinityTo(foe) < 0f) Add("reconcile", $"{Ko.WaGwa(foe.Name)} 화해", "다툰 일이 마음에 걸린다", ActCat.Social, 0.8f);
        }
        // 별난 짓 계획 (습관 · 버릇에서) — 자리만 두고 다른 장면이 채운다
        if (Life.Has(c, Habit.Tinkerer)) Add("tinker", "몰래 손볼 거리 찾기", "만지작거리는 버릇", ActCat.Hobby, 0.6f);
        else if (Life.Has(c, Habit.Prankster)) Add("prank", "누구 한 번 놀래 주기", "장난꾸러기", ActCat.Social, 0.5f);
        else if (Life.Has(c, Habit.Gazer)) Add("gaze", "창밖 사진 한 장", "멍하니 보는 버릇", ActCat.Hobby, 0.5f);
        else if (c.Hobbies.Count > 0 && BelongingSystem.Crafts(c.Hobbies[0])) Add("craft", $"{Persona.Of(c.Hobbies[0]).Name} 작품 하나 완성", "손으로 만드는 게 좋다", ActCat.Hobby, 0.6f);
        Refreshes++;
    }

    /// <summary>단기 목표 (욕구 · 지금 하는 일) — 저장하지 않고 그때그때.</summary>
    public List<CrewGoal> Short(CrewMember c)
    {
        var l = new List<CrewGoal>();
        void Add(string key, string text, ActCat cat) => l.Add(new CrewGoal { Layer = GoalLayer.Short, Key = key, Text = text, Cat = cat });
        if (c.Needs.Hunger > 0.6f) Add("eat", "배고프다", ActCat.Food);
        if (c.Needs.Fatigue > 0.7f) Add("sleep", "졸리다", ActCat.Rest);
        if (c.Needs.Stress > 0.6f) Add("calm", "숨 좀 돌리고 싶다", ActCat.Rest);
        if (c.Needs.Social < 0.3f) Add("talk", "누구랑 얘기하고 싶다", ActCat.Social);
        if (c.Job != null) Add("now", $"지금: {c.Job.Label}", ActCat.None);
        return l;
    }

    public void Update(float dt)
    {
        var w = _w;
        if (!BrainSystem.Enabled) return;
        // 30분(시스템 틱 50번)에 한 번 · 4분의 1씩
        if (w.Tick % (World.SystemInterval * 12) != 0) return;
        foreach (var c in w.Crew)
            if (!c.Dead && ((c.Id + _phase) & 3) == 0) Refresh(c);
        _phase++;
    }

    public long Hash()
    {
        long h = 29;
        foreach (var (id, l) in _g) { h = h * 31 + id; foreach (var g in l) h = h * 31 + g.Key.Length * 3 + (int)g.Layer; }
        return h;
    }
}
