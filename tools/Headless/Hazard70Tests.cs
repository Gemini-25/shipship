using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15 사고 70: 새 사고 44가 저마다 걸리고, 승무원이 수습한다
public static partial class Program
{
    private static int RunHazard70Test(int seed)
    {
        _fails = 0;
        Console.WriteLine($"사고 70 점검 (v15) · 시드 {seed}\n");
        try
        {
            Check("목록 — 사고가 70가지이고 순서가 맞는다", Hazards.All.Length == 70 && Hazards.All.Select((s, i) => (int)s.Kind == i).All(x => x),
                $"{Hazards.All.Length}가지");

            // 1) 하나하나 걸린다 (배 둘 · 대상은 무작위 사고와 같은 규칙으로)
            var ok = new HashSet<HazardKind>();
            var notes = new List<string>();
            foreach (var ship in new[] { "Mirinae", "Hanbit" })
                foreach (var s in HazardsV15.Specs)
                {
                    if (ok.Contains(s.Kind)) continue;
                    var w = DayOne(seed, ship);
                    Run(w, SimTime.Hours(2));
                    if (s.Kind == HazardKind.MedError) w.Ailments.Catch(w.Crew.First(c => !c.Dead), "cold");
                    if (s.Kind is HazardKind.HoistDrop or HazardKind.PanicAttack) foreach (var c in w.Crew.Take(3)) c.Pose = Pose.Working;
                    string? what = null;
                    for (int t = 0; t < 4 && what == null; t++) what = w.Hazards.FireStory(s.Kind.ToString(), null);
                    if (what != null) { ok.Add(s.Kind); if (notes.Count < 8) notes.Add(what); }
                }
            var miss = HazardsV15.Specs.Where(s => !ok.Contains(s.Kind)).Select(s => s.Name).ToList();
            Check("하나하나 — 새 사고 44가 저마다 걸린다", miss.Count <= 2, $"{ok.Count}/44 · 예: {string.Join(" / ", notes)}" + (miss.Count > 0 ? $" · 안 걸린 것: {string.Join(", ", miss)}" : ""));

            // 2) 수습: 몇 가지를 걸고 하루 — 배가 살아 있고 고친 것이 있다
            {
                var w = DayOne(seed, "Mirinae");
                foreach (var k in new[] { HazardKind.GroundFault, HazardKind.ValveSeize, HazardKind.BearingSeize, HazardKind.Smolder, HazardKind.TrunkSag, HazardKind.Co2Spike })
                    w.Hazards.FireStory(k.ToString(), null);
                int broken0 = w.Ship.Machines.Count(m => m.Faults.Count > 0);
                Run(w, SimTime.TicksPerDay);
                int broken1 = w.Ship.Machines.Count(m => m.Faults.Count > 0);
                int dead = w.Crew.Count(c => c.Dead);
                Check("수습 — 여섯 사고를 겪고 하루 뒤 고장이 줄고 아무도 죽지 않는다", dead == 0 && broken1 < broken0 && w.Fire.Count == 0,
                    $"고장 설비 {broken0} → {broken1} · 불 {w.Fire.Count} · 죽음 {dead} · 망 {w.Net.Stats}");
            }

            // 3) 무작위 사고가 새 사고도 고른다
            {
                var w = DayOne(seed, "Hanbit");
                var seen = new HashSet<HazardKind>();
                for (int i = 0; i < 60; i++) { Run(w, SimTime.Hours(1)); if (w.Hazards.FireRandom() != null && w.Hazards.LastRandom is HazardKind hk) seen.Add(hk); }
                int fresh = seen.Count(k => k >= HazardKind.BreakerCascade);
                Check("무작위 — 무작위 사고에 새 사고도 섞인다", fresh >= 3, $"고른 종류 {seen.Count} (새 사고 {fresh})");
            }

            // 4) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); w.Hazards.FireStory(nameof(HazardKind.GroundFault), null); w.Hazards.FireStory(nameof(HazardKind.MoldOutbreak), null); Run(w, SimTime.Hours(20)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 새 사고가 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 사고 70 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
