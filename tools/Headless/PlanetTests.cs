using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v23 행성 점검 (--planettest): 요소 조합 · 이름 붙은 특별 행성 · 난이도 · 원거리 관측(흐림 · 숨은 현상) · 착륙 창과 주컴퓨터의 고르기
//   · 접근 → 하강(기동 방송) → 머무름(항로 멈춤) → 이륙 → 항로 다시 · 불시착(엔진 · 선체 · 못 뜸) · 저장.
public static partial class Program
{
    private static int RunPlanetTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"행성 점검 (v23) · 시드 {seed}\n");
        try
        {
            // 1) 요소 조합 · 특별 행성 · 난이도
            var rng = new Rng(seed * 17 + 3);
            var many = Enumerable.Range(0, 400).Select(i => PlanetGen.Make(rng, i, 3)).ToList();
            int climates = many.Select(p => p.Climate).Distinct().Count(), surfaces = many.Select(p => p.Surface).Distinct().Count();
            int weathers = many.SelectMany(p => p.Weathers).Distinct().Count(), feats = many.SelectMany(p => p.Features).Distinct().Count();
            var plain = many.Where(p => !p.Special).ToList();
            int combos = plain.Select(p => $"{p.Climate}/{p.Surface}/{string.Join(",", p.Weathers.Distinct().OrderBy(x => x))}/{string.Join(",", p.Features.OrderBy(x => x))}").Distinct().Count();
            Check("요소 조합 — 기후 · 지표 · 날씨 · 현상이 고루 나오고 조합이 거의 겹치지 않는다",
                climates == 6 && surfaces == 8 && weathers == 9 && feats == 15 && combos >= plain.Count * 0.85f && many.All(p => p.Danger is >= 0f and <= 1f && p.Reward is >= 0f and <= 1f && p.Name.Length > 0 && p.Line.Length > 0),
                $"기후 {climates} · 지표 {surfaces} · 날씨 {weathers} · 현상 {feats} · 조합 {combos}/{plain.Count} · 예: {many[0].Title} — {many[0].Line}");
            var specials = PlanetGen.Specials.Select((s, i) => PlanetGen.Make(new Rng(i), 1000 + i, 3, new HashSet<string>(PlanetGen.Specials.Where(x => x != s).Select(x => x.Key)))).ToList();
            int specialSeen = many.Count(p => p.Special);
            Check("이름 붙은 특별 행성 열 — 저마다 다르고 가끔 섞여 나온다", PlanetGen.Specials.Select(s => s.Name).Distinct().Count() == 10 && specialSeen is >= 20 and <= 80,
                $"400개 가운데 특별 {specialSeen} · {string.Join(" · ", PlanetGen.Specials.Select(s => s.Name))}");
            float Avg(int level) { var r = new Rng(seed + level * 101); return Enumerable.Range(0, 300).Select(i => PlanetGen.Make(r, i, level)).Average(p => p.Danger); }
            float easy = Avg(1), hard = Avg(8);
            Check("난이도가 높을수록 험한 행성이 잦다", hard > easy + 0.08f, $"느긋 평균 위험 {easy * 100:0}% · 절멸 {hard * 100:0}%");
            var obs = many.Take(60).ToList();
            foreach (var p in obs) PlanetGen.Observe(p, rng, 0.7f);
            float errMean = obs.Average(p => MathF.Abs(p.KnownDanger - p.Danger));
            int hiddenN = obs.Count(p => p.Hidden.Count > 0);
            Check("원거리 관측은 흐리다 — 아는 위험 ≠ 실제 위험 · 숨은 현상", errMean > 0.02f && hiddenN >= 5 && obs.All(p => p.Hidden.All(p.Features.Contains)), $"위험 오차 평균 {errMean * 100:0.0}%p · 숨은 현상 있는 행성 {hiddenN}/60");

            // 2) 착륙 창 → 고르기 → 접근 → 하강 → 머무름 → 이륙
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(2));
            var pl = w.Planet;
            bool chosen = false;
            for (int t = 0; t < 8 && !chosen; t++)
            {
                pl.WindowNow();
                Run(w, SimTime.Minutes(50));
                chosen = pl.Phase == PlanetPhase.Approach;
            }
            Check("착륙 창 — 주컴퓨터가 후보를 멀리서 보고 하나를 고른다 (까닭과 함께) · 별로면 지나친다",
                chosen && pl.Here != null && pl.Decision.Contains(pl.Here.Name) && pl.Stats.Windows >= 1 && pl.Candidates.Count >= 2,
                $"{pl.Decision} · {pl.Stats.Line()}");
            float done0 = w.Voyage.DoneDays;
            bool announced = false;
            for (int m = 0; m < 8 * 60 && pl.Phase != PlanetPhase.Landed; m += 5)
            {
                Run(w, SimTime.Minutes(5));
                announced |= w.Maneuver.Recent.Any(x => x.Kind == ManeuverKind.Landing && x.Announced);
            }
            var landed = pl.Here;
            Check("접근 → 하강(방송 · 몸 고정) → 착륙 · 연대기", pl.Phase == PlanetPhase.Landed && announced && w.History.Events.Any(e => e.Text.StartsWith("행성에 내렸다")) && MathF.Abs(w.Voyage.DoneDays - done0) < 0.001f,
                $"{pl.Phase} · 방송 {(announced ? "있음" : "없음")} · 항로 {done0:0.00} → {w.Voyage.DoneDays:0.00}일 · {w.History.Events.LastOrDefault(e => e.Text.StartsWith("행성에 내렸다"))?.Text}");
            Run(w, SimTime.Hours(6));
            Check("머무는 동안 항로는 멈춘다 · 우주의 운석은 거의 없다", MathF.Abs(w.Voyage.DoneDays - done0) < 0.001f && w.Voyage.HazardMul("meteor") <= 0.6f * 2.5f,
                $"항로 {w.Voyage.DoneDays:0.00}일 · 운석 무게 {w.Voyage.HazardMul("meteor"):0.00}");
            var w2 = StateSave.Read(StateSave.Write(w));
            Check("저장 · 되살림 — 착륙한 행성 · 단계 · 떠날 때", w2.Planet.Phase == PlanetPhase.Landed && w2.Planet.Here?.Name == landed?.Name && w2.Planet.StayUntil == pl.StayUntil && w2.Planet.Here!.Features.SequenceEqual(landed!.Features),
                $"{w2.Planet.Phase} · {w2.Planet.Here?.Title}");
            for (int h = 0; h < 24 * 6 && pl.Phase != PlanetPhase.None; h++) Run(w, SimTime.Hours(1));
            var v = pl.Visits.LastOrDefault();
            Run(w, SimTime.Hours(3));
            Check("임무 기간이 지나면 이륙(방송) → 항로를 잇는다", pl.Phase == PlanetPhase.None && pl.Stats.Liftoffs == 1 && v != null && v.Left > v.Landed && w.Voyage.DoneDays > done0 && w.History.Events.Any(e => e.Text.Contains("에서 떠났다")),
                $"{pl.Stats.Line()} · 머문 {(v != null ? (v.Left - v.Landed) / (float)SimTime.TicksPerDay : 0):0.0}일 · 항로 {w.Voyage.DoneDays:0.00}일");

            // 3) 불시착
            var c = World.CreateDefault(seed + 1, 0, "Hanbit");
            Run(c, SimTime.Hours(2));
            int leak0 = c.Ship.LiveRooms.Count(r => r.Leaking);
            c.Planet.OpenWindow(forceCrash: true);
            Run(c, 30);
            string? why = c.Planet.CannotLift();
            bool enginesDown = !c.Propulsion.Engines.Any(m => m.FaultFactor * (0.6f + 0.4f * m.Condition) > 0.3f);
            Check("불시착 — 예고 없는 충격 · 엔진 고장 · 선체 균열 · 고치기 전엔 못 뜬다",
                c.Planet.Phase == PlanetPhase.Landed && c.Planet.Crashed && why != null && enginesDown && c.Maneuver.Recent.Any(m => m.Kind == ManeuverKind.Impact) && c.History.Events.Any(e => e.Text.StartsWith("불시착")),
                $"{c.Planet.Decision} · 못 뜨는 까닭: {why ?? "없음"} · 새는 방 {leak0} → {c.Ship.LiveRooms.Count(r => r.Leaking)}");
            bool lifted = false; string? lastWhy = why;
            for (int h = 0; h < 24 * 7 && !lifted; h++) { Run(c, SimTime.Hours(1)); lifted = c.Planet.Stats.Liftoffs > 0; if (!lifted) lastWhy = c.Planet.CannotLift(); }
            Check("고치고 나서야 뜬다 (로봇 · 사람이 엔진 · 선체를 고친다)", lifted, $"{c.Planet.Stats.Line()} · 마지막 까닭 {lastWhy ?? "없음"} · {string.Join(" / ", c.Planet.Notes.TakeLast(3))}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
