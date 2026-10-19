namespace ShipEngine;

/// <summary>
/// ⑫ 자연 사건 — 병원체가 배에 드는 길 (docs/ENGINE.md ⑫ '배 안의 일은 모두 세상 법칙에서'): 식량 상자에 묻어 온 바이러스 · 출항 때 잠복한 채 탄 이.
/// 사람끼리 옮는 것 · 조난선의 병은 <see cref="Spread"/> · 주컴퓨터의 넘어옴이 한다. 몫은 기능 확인용 첫 값 — 맨 끝에 다듬는다 (함장 결정).
/// 어느 상자 · 누구인지는 시드에서 (결정론) — 세상이 계산할 뿐 누구의 마음도 건드리지 않는다.
/// </summary>
public sealed partial class Life
{
    /// <summary>식량 상자 하나에 감기 바이러스가 묻어 있을 몫 (포장 · 싣는 동안) — 세계마다 (시험이 바꾼다 · 찍지 않는다).</summary>
    public double CrateColdChance = 0.01;
    /// <summary>출항 때 누군가 고향에서 옮은 감기를 잠복한 채 탈 몫.</summary>
    public double BoardColdChance = 0.15;
    /// <summary>열어 본 식량 상자 수 (상자마다 시드의 난수 하나).</summary>
    public long CratesOpened;

    /// <summary>⑧의 요청: 사람이 식량 상자를 열었다 — 그 상자에 바이러스가 묻어 있었으면 연 이가 잠복에 든다.</summary>
    public void OpenedCrate(Sim sim, int body, int cell, int actor, string name)
    {
        long n = CratesOpened++;
        if (body < 0 || body >= Bodies.Count || this[body].Alive == 0) return;
        if (Rng.Unit(sim.Seed, Rng.Key("상자 감기"), n, 0) >= CrateColdChance) return;
        int c0 = sim.Causes.Add(CauseKind.Biology, "식량 상자 하나에 감기 바이러스가 묻어 있었다", sim.Tick, cell);
        int c1 = sim.Causes.Add(CauseKind.Biology, $"{Ko.I(name)} 그 상자를 열다 감기가 옮았다", sim.Tick, cell, c0, 0, actor);
        CatchCold(sim, body, c1);
    }

    /// <summary>출항 (한 번): 고향에서 옮은 감기가 잠복한 채 탄 이 — 잠복이 얼마나 지났는지도 시드에서.</summary>
    public void Boarding(Sim sim)
    {
        var alive = Enumerable.Range(0, Bodies.Count).Where(i => this[i].Alive != 0 && this[i].Ill == 0).ToList();
        if (alive.Count == 0 || Rng.Unit(sim.Seed, Rng.Key("출항 잠복"), 0, 0) >= BoardColdChance) return;
        int b = alive[(int)(Rng.Unit(sim.Seed, Rng.Key("출항 잠복 누구"), 0, 0) * alive.Count) % alive.Count];
        int c0 = sim.Causes.Add(CauseKind.Biology, "출항 전 고향에서 옮은 감기가 잠복해 있었다", sim.Tick, this[b].Cell);
        CatchCold(sim, b, c0);
        this[b].IllH = (float)(Rng.Unit(sim.Seed, Rng.Key("출항 잠복 때"), 0, 0) * IncubateHours * 0.8);
    }
}
