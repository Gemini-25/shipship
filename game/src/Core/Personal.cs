using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ShipSim.Core;

// 압축-라 작은 묶음 — 한 사람의 몸에 걸친 것 · 고향에서 오는 편지 · 판돈과 맞바꾸기.
//   v18.0 의복 · 보호구 · 안경 · 손에 익은 물건 (Wear.cs)
//   v18.1 선외 인간관계 — 편지 · 답장 · 지연 · 끊김 · 밀린 편지 (Mail.cs)
//   v18.9 내기 · 물물교환 — 초콜릿 · 당번을 건 판 · 빚 → 대신 당직 · 선내 맞바꾸기 (Wager.cs)
// 셋 다 사람 단위 상태라 한 시스템이 차례로 돌린다 (World에는 필드 하나 · 틱 한 줄).

public sealed class PersonalSystem
{
    public WearSystem Wear { get; }
    public MailSystem Mail { get; }
    public WagerSystem Bets { get; }
    public bool Off { get; set; }

    public PersonalSystem(World w)
    {
        Wear = new WearSystem(w);
        Mail = new MailSystem(w);
        Bets = new WagerSystem(w);
    }

    /// <summary>이 묶음이 쓴 시간 (밀리초 · 성능 시험).</summary>
    [field: NotSaved] public double Ms { get; private set; } // 성능 측정 (상태 저장에 넣지 않는다)

    public void Update(float dt)
    {
        if (Off) return;
        long t0 = Stopwatch.GetTimestamp();
        Wear.Update(dt);
        Mail.Update(dt);
        Bets.Update(dt);
        Ms += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
    }

    public string Summary() => $"옷 · 안경: {Wear.Stats.Summary()} / 편지: {Mail.Stats.Summary()} / 내기: {Bets.Stats.Summary()}";

    public void Hash(Action<long> I, Action<float> F)
    {
        Wear.Hash(I, F);
        Mail.Hash(I, F);
        Bets.Hash(I, F);
    }
}
