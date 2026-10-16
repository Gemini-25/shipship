namespace ShipEngine;

/// <summary>
/// 표준 세계 만들기: 배 도면 + 엔진들 (층 순서대로). 지금 있는 엔진: ③ 장 · ② 칸의 화학 · ③ 소리 · ③ 빛 · ④ 구조 · ⑤ 바깥 세계 · ⑥ 망 · 장치 · ⑦ 생명 · ⑧ 마음 · ⑩ 주컴퓨터 · ⑫ 이야기꾼(기본 꺼짐).
/// 위층 엔진이 생기면 여기에 더한다. 게임 · 실험실 · 주컴퓨터의 복제가 모두 이것으로 세계를 만든다.
/// </summary>
public static class ShipWorld
{
    public static SimSetup Setup(Layout layout, ulong seed) => new(layout, seed, sim =>
    {
        var f = new Field(sim.Layout, sim.Tables);
        sim.Add(f);
        sim.Add(new Chemistry(f));
        sim.Add(new Sound(f));
        sim.Add(new Light(f));
        sim.Add(new Structure(f));
        sim.Add(new Outside(f, sim.Seed));
        sim.Add(new Network(f, sim.Seed));
        sim.Add(new Life(f));
        sim.Add(new Mind(f));
        sim.Add(new ShipMind(f));
        sim.Add(new Storyteller(f, sim.Seed));
    });

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

    public static Sim Create(string shipKey, ulong seed) => Create(Layout.Ship(shipKey, Tables.Default), seed);

    /// <summary>사람 · 로봇 · 드론을 태운 배 (사람은 침대마다 · 로봇은 충전독마다 · 많아야 그 수).</summary>
    public static Sim Crewed(string shipKey, ulong seed, int people, int robots = 0, int drones = 0)
    {
        var s = Create(shipKey, seed);
        s.Get<Mind>().Crew(s, people, robots, drones);
        return s;
    }
}
