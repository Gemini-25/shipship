namespace ShipEngine;

/// <summary>
/// 표준 세계 만들기: 배 도면 + 엔진들 (층 순서대로). 지금 있는 엔진: ③ 장 · ② 칸의 화학 · ③ 소리 · ③ 빛 · ④ 구조 · ⑤ 바깥 세계 · ⑥ 망 · 장치 · ⑥ 감지 · ⑦ 생명 · ⑧ 마음 · ⑨ 사회 · ⑩ 주컴퓨터 · ⑪ 일과 물자 (⑫ 자연 사건은 이들의 법칙 · 이야기꾼은 없다).
/// 위층 엔진이 생기면 여기에 더한다. 게임 · 실험실 · 주컴퓨터의 복제가 모두 이것으로 세계를 만든다.
/// </summary>
public static class ShipWorld
{
    public static SimSetup Setup(Layout layout, ulong seed) => new(layout, seed, sim =>
    {
        sim.Add(new Hand());   // 함장의 손 (바깥 입력 — docs/UI2.md §2.4): 맨 앞 — 같은 단계 '바깥'에서 ⑤보다 먼저 = 틱의 가장 처음
        var f = new Field(sim.Layout, sim.Tables);
        sim.Add(f);
        sim.Add(new Chemistry(f));
        sim.Add(new Sound(f));
        sim.Add(new Light(f));
        sim.Add(new Structure(f));
        sim.Add(new Outside(f, sim.Seed));
        sim.Add(new Network(f, sim.Seed));
        sim.Add(new Sensors(sim.Tables));
        sim.Add(new Life(f));
        sim.Add(new Mind(f));
        sim.Add(new Society(f.L));
        sim.Add(new ShipMind(f));
        sim.Add(new Work(f.L, f.T));
    });

    public const int DefaultRoute = 4;

    /// <summary>새 배: 공기를 채우고 설비를 앉힌다.</summary>
    public static Sim Create(Layout layout, ulong seed)
    {
        var s = new Sim(Setup(layout, seed));
        var f = s.Get<Field>();
        f.FillAir();
        Furnish.Apply(f);
        s.Get<Life>().Plant(s.Get<Network>());
        return s;
    }

    /// <summary>설계한 배 (도면 표): 새 배 + 만들 때 남은 작은 흠 (⑫ — 시드에서) + 생활 소품 (④ 꾸밈 — 탁자 위 컵 · 접시 · 선반 위 책 · 물병 · 작업대 위 공구 상자 · 시드에서 · PH9) ·
    /// 시험의 글자 도면 배는 흠 · 꾸밈 없이 깨끗하다.</summary>
    public static Sim Create(string shipKey, ulong seed)
    {
        var s = Create(Layout.Ship(shipKey, Tables.Default), seed);
        var st = s.Get<Structure>();
        st.BuildFlaws(s);
        st.Decorate(s);
        return s;
    }

    /// <summary>
    /// 항해에 나선다 (관전 · 재미 측정): 붉은 사막(1.52AU)으로 가는 호만 전이 — minutes 뒤 출발 점화 (주컴퓨터가 방송하고 한다) · 다른 배가 지나간다.
    /// route: 함장이 고른 항로 1~8 (⑫ 위험도 = 항로만 — 기본 4 '보통 길'). 시험은 부르지 않는다 (가만히 떠 있는 배).
    /// </summary>
    public static void Embark(Sim s, int route = DefaultRoute, double minutes = 20)
    {
        var o = s.Find<Outside>();
        if (o == null) return;
        o.TrafficOn = true;
        o.Route = Math.Clamp(route, 1, Math.Max(1, s.Tables.Space.Routes.Count));
        var to = Tables.Default.Space.System.Bodies.FirstOrDefault(b => b.Au > o.Distance + 0.1) ?? Tables.Default.Space.System.Bodies[^1];
        string rn = o.Rt != null ? $" · 항로 {o.Route} '{o.Rt.Name}'" : "";
        int c = s.Causes.Add(CauseKind.Route, $"{Ko.Euro(to.Name)} 가는 항해 ({to.Au:0.##}AU{rn})", s.Tick);
        int place = o.PlaceOf(to.Name);
        if (place >= 0)
        {
            // ⑤ 별계 지도: 임무는 발사 창에 맞춰 나선다 (처음 구간) · 다음부터는 주컴퓨터가 고른다
            o.AlignWindow(place, s.Tick + Time.Minutes(minutes));
            o.PlanVoyageTo(s, place, minutes, c);
            s.Find<ShipMind>()?.SetMission(new[] { place });
        }
        else o.PlanVoyage(s, to.Au, to.Name, minutes, c);
        s.Find<Life>()?.Boarding(s);   // ⑫ 고향에서 옮은 감기가 잠복한 채 탄 이 (시드에서)
    }

    /// <summary>사람 · 로봇 · 드론을 태운 배 (사람은 침대마다 · 로봇은 충전독마다 · 많아야 그 수).</summary>
    public static Sim Crewed(string shipKey, ulong seed, int people, int robots = 0, int drones = 0)
    {
        var s = Create(shipKey, seed);
        s.Get<Mind>().Crew(s, people, robots, drones);
        s.Get<Society>().Backstory(s);   // ⑫ 출항 과거사 — 사람 사이의 지난 일 (시드에서 · 한 번)
        return s;
    }
}
