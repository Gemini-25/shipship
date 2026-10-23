namespace ShipEngine;

/// <summary>한 틱의 단계 (순서 고정 · docs/ENGINE.md §3).</summary>
public enum Phase : byte
{
    Outside = 1,   // 바깥: 우주 물체 · 충돌 · 별 · 날씨
    Sense,         // 감각
    Decide,        // 판단 → 요청
    Requests,      // 요청 적용
    Network,       // 망 · 장치
    Chemistry,     // 반응
    Field,         // 장: 기체 · 열 · 액체 · 소리 · 빛
    Structure,     // 구조
    Life,          // 생명
    Record,        // 기록
    Director,      // 연출
}

/// <summary>
/// 엔진 하나. (단계, 주기, 어긋남)에 따라 틱마다 불린다.
/// 아래 층의 상태만 읽고, 위층은 모른다. 상태는 찍기에 들어간다.
/// </summary>
public interface IEngine : IState
{
    string Name { get; }
    Phase Phase { get; }
    /// <summary>몇 틱마다 도나 (1 · 5 · 25 · 1500 …).</summary>
    int Every { get; }
    /// <summary>주기 안에서 어느 틱에 도나 (무거운 엔진끼리 같은 틱에 몰리지 않게).</summary>
    int Offset { get; }
    /// <summary>dt = Every × 2.4초.</summary>
    void Step(Sim sim, double dt);
    /// <summary>다른 엔진을 잇는다 — Sim이 첫 틱 전에 · 되살린 뒤에 한 번 부른다.
    /// Step 안에서 늦게 이으면, 되살린 세계에서 이 엔진이 아직 돌지 않았을 때 다른 엔진이 먼저 읽어 빈 값을 받는다 (검토 ⑤-2 · 법칙 '긴 왕복').</summary>
    void Bind(Sim sim) { }
}
