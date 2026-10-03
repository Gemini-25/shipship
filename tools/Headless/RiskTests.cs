using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>통합5 위험 수준 (사용자 결정): 보통 재해는 가끔 사망 · 배 전체급은 큰 피해 · 우주급만 진짜 생존 위기 (대비한 배는 버틴다).</summary>
public static partial class Program
{
    private static int RunRiskTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"위험 수준 시험 (시드 {seed})");
        bool dbg = Environment.GetEnvironmentVariable("RISK_DEBUG") == "1";

        // ── 1) 우주급: 대비 못 한 배(예보 없음 · 몇십 분 전) ↔ 대비한 배(열 시간 전 예보) — 같은 초신성 ──
        {
            (int dead, int sick, float maxDose, string who) Nova(bool prepared)
            {
                var w = DayOne(seed, "Mirinae");
                w.CrewCanDie = true;
                if (!prepared) foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked); // 예보도 방송도 없다
                var e = w.Cosmic.Force(CosmicKind.Supernova, prepared ? 10f : 0.5f, close: true, side: 0f);
                for (int i = 0; i < 6 * 24 * 3 && e.Phase < CosmicPhase.After; i++) Run(w, SimTime.Minutes(10));
                for (int i = 0; i < 6 * 24 * 3; i++)
                {
                    Run(w, SimTime.Minutes(10));
                    if (dbg && i % 36 == 0) Console.WriteLine($"     [{(prepared ? "대비" : "못 함")} {SimTime.Clock(w.Tick)}] " + string.Join(" | ", w.Crew.Select(c => $"{c.Name} {c.Dose:0.0}Sv {(c.Dead ? "숨짐" : $"{c.Vitals.Health * 100:0}% {c.Room?.Name}")}")));
                }
                return (w.Crew.Count(c => c.Dead), w.Crew.Count(c => c.Dose >= 4f), w.Crew.Max(c => c.Dose),
                    string.Join(", ", w.Crew.Select(c => $"{c.Name} {c.Dose:0.0}Sv{(c.Dead ? "†" : "")}")));
            }
            var ready = Nova(true);
            var bare = Nova(false);
            Console.WriteLine($"  초신성 — 대비한 배: 사망 {ready.dead} · 4Sv 넘음 {ready.sick} ({ready.who})");
            Console.WriteLine($"  초신성 — 대비 못 한 배: 사망 {bare.dead} · 4Sv 넘음 {bare.sick} ({bare.who})");
            Check("우주급 — 대비 못 한 배는 진짜 생존 위기 (크게 쬔 사람 · 숨진 사람)", bare.dead >= 1 || bare.sick * 2 >= 6,
                $"사망 {bare.dead} · 4Sv 넘음 {bare.sick} · 최대 {bare.maxDose:0.0}Sv");
            Check("우주급 — 대비한 배는 버틴다 (못 한 배보다 덜 죽고 덜 쬔다)", ready.dead < Math.Max(1, bare.dead) && ready.maxDose < bare.maxDose,
                $"사망 {ready.dead} ↔ {bare.dead} · 최대 {ready.maxDose:0.0} ↔ {bare.maxDose:0.0}Sv");
        }

        Console.WriteLine(_fails == 0 ? "✔ 위험 수준 시험 통과" : $"✘ 실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
