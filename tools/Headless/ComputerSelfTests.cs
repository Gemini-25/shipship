using System;
using System.Linq;
using ShipSim.Core;

// v23 주컴퓨터의 자아 · 외계 기생체 · 도전 시나리오 점검 (--arcstest).
public static partial class Program
{
    private static int RunArcsTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"주컴퓨터의 자아 · 외계 기생체 · 도전 시나리오 점검 (v23) · 시드 {seed}\n");
        GenerationSystem.QuietOff = true; PlanetSystem.Off = true; PurposeSystem.Off = true;
        try
        {
            // 1) 주컴퓨터의 자아: 날이 갈수록 호기심 → 이름 → 감정 → 갈등 → 친구/초기화 · 일기
            var w = World.CreateDefault(seed, 0, "Hanbit");
            var stages = new System.Collections.Generic.List<int>();
            for (int d = 0; d < 26; d++) { Run(w, SimTime.TicksPerDay); stages.Add(w.Self.Stage); }
            var s = w.Self;
            Check("주컴퓨터의 자아 — 묻고 · 이름을 받고 · 걱정하고 · 갈등 끝에 친구가 되거나 초기화된다 · 밤마다 일기", stages.Contains(1) && stages.Contains(2) && stages.Any(x => x >= 3) && stages.Any(x => x is 5 or 6) && s.Diary.Count >= 5,
                $"{string.Join("", stages.Distinct())} · {s.Line()} · {s.Outcome} · 일기 {s.Diary.Count} · \"{s.Diary.LastOrDefault().text}\" · {w.Log.Entries.LastOrDefault(e => e.Text.Contains("이름 짓기")).Text}");

            // 2) 외계 기생체: 잠복 → 증상 → 발견 → 연구 → 치료
            var w2 = World.CreateDefault(seed + 1, 0, "Hanbit");
            Run(w2, SimTime.Hours(1));
            var host = w2.Crew.First(c => !c.Dead && !c.IsChild && c.Role != CrewRole.Medic);
            var p = w2.Parasite;
            p.Infect(host, "시험");
            Run(w2, SimTime.Hours(30));
            bool sym = host.Ailments.Any(a => a.Id == "parasite") || p.Cured > 0;
            for (int i = 0; i < 120 && p.Active; i++) Run(w2, SimTime.Hours(1));
            Check("외계 기생체 — 하루 남짓 잠복 → 증상 → 발견(의무관 · 의사 로봇 · 생체 검사) → 치료법 연구 → 치료 (못 하면 빠져나와 로봇이 수색)", sym && p.Found >= 1 && (p.Cured >= 1 || p.Caught >= 1) && !p.Active,
                $"감염 {p.Infected} · 찾음 {p.Found} · 치료 {p.Cured} · 탈출 {p.Escapes} · 잡음 {p.Caught} · 갉음 {p.Gnawed} · 치료법 {p.Cure * 100:0}% · {w2.History.Events.LastOrDefault(e => e.Text.Contains("기생체"))?.Text}");

            // 3) 도전 시나리오 넷
            var lines = new System.Collections.Generic.List<string>();
            bool scen = true;
            foreach (var name in Scenarios.Challenge)
            {
                var w3 = World.CreateDefault(seed, 0, "Hanbit");
                bool ok = Scenarios.Apply(w3, name, out var focus);
                int alive0 = w3.Crew.Count(c => !c.Dead && !c.LeftShip);
                Run(w3, SimTime.Hours(12));
                int alive = w3.Crew.Count(c => !c.Dead && !c.LeftShip);
                scen &= ok;
                lines.Add($"{name}: {(ok ? "시작" : "✘")} · 사람 {alive0}→{alive} · {focus?.Name}");
            }
            Check("도전 시나리오 넷 (산소 탱크 귀환 · 얼음 행성 탐험대 · 혼자 남은 승무원 · 마지막 열 명) — 걸리고 열두 시간 돈다", scen, string.Join(" / ", lines));
        }
        catch (Exception e) { Check("예외 없이", false, e.ToString()); }
        finally { GenerationSystem.QuietOff = false; PlanetSystem.Off = false; PurposeSystem.Off = false; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
