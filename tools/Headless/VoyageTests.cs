using System;
using System.Linq;
using ShipSim.Core;

// v12.8 장기 진행: 항로 구간 · 기항지 교역 · 난파선 · 시대 기술 · 항해 정리 → 다음 항해
public static partial class Program
{
    private static int RunVoyageTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"장기 진행 점검 (v12.8) · 시드 {seed}\n");
        try
        {
            // 1) 항로: 처음 엿새는 순항, 그 뒤로 구간들, 끝은 기항지
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                var v = w.Voyage;
                Check("항로 — 순항으로 시작해 기항지로 끝난다", v.Legs.First().Kind == LegKind.Cruise && v.Legs.Last().Kind == LegKind.Port && v.Legs.Count >= 5,
                    $"{v.Origin} → {v.Destination} · " + string.Join(" → ", v.Legs.Select(l => $"{VoyageSystem.KindName(l.Kind)} {l.Days:0.#}일")));
            }
            // 2) 한 항해를 끝까지: 구간을 지나고, 기항지에서 교역하고, 정리하고 다음 항해를 잡는다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var v = w.Voyage;
                float total = v.TotalDays;
                int legsSeen = 0, last = -1, halted = 0; // 통합6 원정대를 기다리며 엔진을 끈 날은 일정이 밀린다 (그만큼 더 기다린다 · 열흘까지)
                for (int d = 0; d < (int)total + 6 + Math.Min(10, halted) && v.Number == 1; d++)
                {
                    Run(w, SimTime.TicksPerDay);
                    if (w.Expedition.Halted) halted++;
                    if (v.Index != last) { legsSeen++; last = v.Index; }
                    if (Environment.GetEnvironmentVariable("VOY_DEBUG") == "1") Console.WriteLine($"   {d}일 구간 {v.Index} {v.Progress:0.0} 표류 {v.Drifting} 멈춤 {w.Expedition.Halted} {w.Expedition.HaltWhy} 불 {w.History.Events.Count(h => h.Text.Contains("불"))} 사고 {w.Scale.Cases.Count} · 원자로 {(w.Power.ReactorOnline ? "켜짐" : "꺼짐")}{(w.Power.LowPowerMode ? "(저출력)" : "")} 배터리 {w.Power.BatteryPercent * 100:0}% 차단 {w.Failsafe.ShedLevel} 출력 {w.Power.ReactorOutput:0}/{w.Power.ReactorLimit:0} 수요 {w.Power.Demand:0} 온도 {w.Power.ReactorTemperature:0} 냉각 {w.Power.CoolingCapacity:0} 독 {w.Power.ReactorPoison:0.00} 정비제한 {w.Power.MaintenanceCap} 큰 소비 {string.Join(",", w.Ship.Machines.Where(m => m.Powered && !m.Parked).OrderByDescending(m => m.Spec.PowerDraw).Take(4).Select(m => $"{m.Body.Type}{m.Spec.PowerDraw:0}"))} 원자로 고장 {string.Join(",", w.Ship.Machines.Where(m => m.Body.Type == FurnitureType.ReactorCore).SelectMany(m => m.Faults.Select(f => f.Kind)))} · 추진제 {w.Propulsion.Propellant:0}/{w.Propulsion.Capacity:0} · 엔진 {string.Join(" ", w.Propulsion.Engines.Select(m => $"{m.Efficiency:0.00}{(m.Faults.Count > 0 ? "[" + string.Join(",", m.Faults.Select(f => f.Kind)) + "]" : "")}{(m.Powered ? "" : "·전기없음")}{(m.Active ? "" : "·꺼짐")}{(m.Parked ? "·세움" : "")}"))}");
                }
                var past = v.Past.FirstOrDefault();
                Check("항해 하나를 끝까지 — 정리하고 다음 항해", v.Number >= 2 && past != null,
                    past != null ? $"{past.From} → {past.To} · {past.Days:0.0}일 · 사고 {past.Incidents} · 사망 {past.Deaths} · 교역 {past.Trades} · 새 사람 {past.Recruits} · 건짐 {past.Salvage} · 새 기술 {past.Techs} · 가장 큰 일: {past.Highlight} · 남은 돈 {v.Credits:0}"
                    : $"아직 {v.Index}/{v.Legs.Count} 구간 · {v.DoneDays:0.0}/{total:0.0}일 · 표류 {v.Drifting}");
                var trade = w.History.Events.FirstOrDefault(e => e.Text.Contains("교역 —"));
                Check("기항지 — 팔고 사고 개수 공사", trade != null, trade?.Text ?? "교역 기록 없음");
            }
            // 3) 구간마다 사고의 무게가 다르다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                Check("구간 — 순항은 보통, 소행성대는 운석이 잦다", w.Voyage.HazardMul("meteor") == 1f, $"순항 운석 ×{w.Voyage.HazardMul("meteor")}");
            }
            // 4) 시대 기술: 연구가 쌓이면 회의가 다음 연구를 고르고, 익히면 효과와 위험이 붙는다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay * 10);
                var e = w.Eras;
                var known = e.Order.Select(id => (EraSystem.Find(id)?.Name ?? id)).ToList();
                Check("시대 기술 — 회의가 고르고, 익힌다", known.Count >= 1 && w.History.Events.Any(x => x.Text.Contains("다음 연구는")),
                    $"{EraSystem.EraName(e.Era)} · 연구 {w.Research:0} · 익힘 {string.Join(", ", known)} · 지금 {(e.Project ?? "-")} {e.Progress:0}");
            }
            // 5) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    Run(w, SimTime.TicksPerDay * 4);
                    return SaveGame.StateHash(w);
                }
                uint a = H(), b = H();
                Check("결정론 — 항로·기술이 든 배도 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"예외: {ex}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 장기 진행 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
