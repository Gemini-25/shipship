namespace ShipEngine;

/// <summary>
/// 표준 세계 만들기: 배 도면 + 엔진들 (층 순서대로). 지금 있는 엔진: ③ 장 · ② 칸의 화학 · ③ 소리 · ③ 빛 · ④ 구조.
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
    });

    /// <summary>새 배: 공기를 채우고 설비를 앉힌다.</summary>
    public static Sim Create(Layout layout, ulong seed)
    {
        var s = new Sim(Setup(layout, seed));
        var f = s.Get<Field>();
        f.FillAir();
        Furnish.Apply(f);
        return s;
    }

    public static Sim Create(string shipKey, ulong seed) => Create(Layout.Ship(shipKey, Tables.Default), seed);
}
