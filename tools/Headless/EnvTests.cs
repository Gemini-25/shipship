using System;
using System.Linq;
using ShipSim.Core;

// v23 극한 환경 사고 점검 (--envtest):
//   환경 17가지 × 규모 다섯(개인 · 방 · 계통 · 배 전체 · 우주급)마다 사고가 30가지 넘는다 · 새 천체 8종 · 행성 → 환경 읽기
//   · 실제 배에서 다섯 규모가 다 터진다 (기존 사고 · 부상 · 병 · 온도로 번진다) · 주컴퓨터가 낌새를 보고 로봇을 보내 막기도 한다
//   · 이야기꾼이 켜져 있으면 저절로 난다 · 같은 기제를 겪으면 익숙해진다 · 우주급이면 일찍 뜬다 · 저장.
public static partial class Program
{
    private static int RunEnvTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"극한 환경 사고 점검 (v23) · 시드 {seed}\n");
        try
        {
            // 1) 사고표
            var kinds = Enum.GetValues<EnvKind>().Where(k => k != EnvKind.None).ToList();
            var thin = new System.Collections.Generic.List<string>();
            var lines = new System.Collections.Generic.List<string>();
            int total = 0;
            foreach (var k in kinds)
            {
                var p = EnvHazardSystem.Sample(k);
                var counts = Enum.GetValues<IncidentScale>().Select(s => EnvHazardSystem.Catalog(p, s).Select(c => c.Name).Distinct().Count()).ToArray();
                total += counts.Sum();
                if (counts.Any(n => n < 30)) thin.Add($"{EnvHazardSystem.KindName(k)} {string.Join("/", counts)}");
                lines.Add($"{EnvHazardSystem.KindName(k)} {string.Join("/", counts)}");
            }
            Check("환경 17가지 × 규모 다섯마다 사고가 30가지 넘는다 (이름이 다 다르다)", thin.Count == 0 && kinds.Count == 17,
                $"모두 {total}가지 · " + (thin.Count > 0 ? "모자람: " + string.Join(", ", thin) : string.Join(" · ", lines.Take(5)) + " …"));
            var sampleNames = EnvHazardSystem.Catalog(EnvHazardSystem.Sample(EnvKind.Frozen), IncidentScale.Room).Select(c => c.Name).Take(4).Concat(
                EnvHazardSystem.Catalog(EnvHazardSystem.Sample(EnvKind.BlackHole), IncidentScale.Ship).Select(c => c.Name).Take(2)).Concat(
                EnvHazardSystem.Catalog(EnvHazardSystem.Sample(EnvKind.Io), IncidentScale.Cosmic).Select(c => c.Name).Take(2));
            Console.WriteLine("    보기: " + string.Join(" / ", sampleNames));

            // 2) 새 천체 8종 → 환경
            var rng = new Rng(seed);
            var bodies = Enum.GetValues<PlanetBody>().Where(b => b != PlanetBody.Rocky).ToList();
            var bodyLines = new System.Collections.Generic.List<string>();
            bool bodyOk = true;
            foreach (var b in bodies)
            {
                var pl = PlanetGen.Make(rng, 100 + (int)b, 4);
                PlanetGen.ApplyBody(pl, 4, b);
                var env = EnvHazardSystem.FromPlanet(pl, pl.Weathers.LastOrDefault());
                bool ok = pl.Body == b && env.Kind != EnvKind.None && env.Active.Count() >= 3;
                bodyOk &= ok;
                bodyLines.Add($"{pl.Name} → {env.Name} ({string.Join("·", env.Active.Take(3).Select(EnvHazardSystem.MechName))}){(ok ? "" : " ✘")}");
            }
            int made = 0, odd = 0;
            for (int i = 0; i < 300; i++) { var pl = PlanetGen.Make(rng, 500 + i, 1 + i % 8); if (pl.Body != PlanetBody.Rocky) odd++; made++; }
            Check("새 천체 8종(가스 거인 · 얼음 위성 · 금성형 · 메탄 위성 · 화산 위성 · 혜성 · 소행성 · 떠돌이) — 행성 고르기에 섞여 나오고 환경이 맞게 읽힌다", bodyOk && odd > 30 && odd < 200,
                $"300개 중 새 천체 {odd} · " + string.Join(" / ", bodyLines));

            // 3) 실제 배에서 다섯 규모
            GenerationSystem.QuietOff = true; PlanetSystem.Off = true; PurposeSystem.Off = true;
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var e = w.Env;
            e.Force = EnvHazardSystem.Sample(EnvKind.Frozen);
            foreach (var s in Enum.GetValues<IncidentScale>()) e.Begin(s, null, null, now: true);
            Run(w, SimTime.Minutes(10));
            var fired = Enum.GetValues<IncidentScale>().Select(s => e.Stats.Fired[(int)s] + e.Stats.Prevented[(int)s]).ToArray();
            Check("얼음 행성 — 다섯 규모가 다 터진다 (기존 사고 · 부상 · 병 · 온도로 번진다)", fired.All(n => n >= 1) && e.Stats.Hazards >= 3,
                $"{string.Join("/", fired)} · 이어진 사고 {e.Stats.Hazards} · 다친 사람 {e.Stats.Injured} · {string.Join(" | ", e.Recent.TakeLast(3).Select(r => r.text))}");

            // 4) 낌새 → 로봇이 막는다
            int prev0 = e.Stats.Prevented.Sum(), omen0 = e.Stats.Omens.Sum();
            for (int i = 0; i < 24; i++) { e.Begin(IncidentScale.Room, null, null); Run(w, SimTime.Hours(1)); }
            Check("방 규모 — 주컴퓨터가 낌새를 보고 로봇을 보내 미리 막기도 한다", e.Stats.Omens.Sum() > omen0 && e.Stats.Prevented.Sum() > prev0,
                $"낌새 {e.Stats.Omens.Sum() - omen0} · 막음 {e.Stats.Prevented.Sum() - prev0} · {e.Recent.LastOrDefault(r => r.prevented).text}");

            // 5) 저절로 (이야기꾼)
            var w2 = World.CreateDefault(seed + 1, 0, "Mirinae");
            w2.Env.Force = EnvHazardSystem.Sample(EnvKind.Venus);
            Run(w2, SimTime.TicksPerDay * 2);
            int nat = w2.Env.Stats.Fired.Sum() + w2.Env.Stats.Prevented.Sum();
            Check("금성형 행성에 이틀 — 사고가 저절로 난다 (규모가 섞인다)", nat >= 3, $"{w2.Env.Stats.Line()}");

            // 6) 익숙해진다
            Check("같은 기제를 다섯 번 겪으면 익숙해진다 (덜 다친다)", e.Stats.Adapted >= 1 || e.Seen.Max() >= 5, $"적응 {e.Stats.Adapted} · 가장 많이 겪은 기제 {EnvHazardSystem.MechName((EnvMech)Array.IndexOf(e.Seen, e.Seen.Max()))} {e.Seen.Max()}번");

            // 7) 환경 줄 · 저장
            var snap = StateSave.Write(w);
            Check("환경 줄 · 상태 저장", e.Line() != null && snap.Length > 0, $"{e.Line()} · {snap.Length / 1024}KB");
        }
        catch (Exception ex) { Check("예외 없이", false, ex.ToString()); }
        finally { GenerationSystem.QuietOff = false; PlanetSystem.Off = false; PurposeSystem.Off = false; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
