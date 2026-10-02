using System;
using System.Linq;
using ShipSim.Core;

// v10.11 대인원 생활: 조리사, 배급
public static partial class Program
{
    /// <summary>먹을 것을 바닥내고 재배대 절반을 세운 배 (배급 회의가 열릴 만큼).</summary>
    private static World Hungry(int seed, string ship, bool noRation)
    {
        var w = DayOne(seed, ship);
        w.Food.Disabled = noRation;
        int crew = w.Crew.Count;
        Scenarios.LimitStock(w, ItemKind.Meal, crew);
        Scenarios.LimitStock(w, ItemKind.Ration, 0);
        Scenarios.LimitStock(w, ItemKind.Produce, crew);
        foreach (var bed in w.Ship.FurnitureOf(FurnitureType.GrowBed).Where((_, i) => i % 2 == 0).ToList())
        {
            bed.Machine!.Crop!.Growth = 0.05f;
            w.Machines.Break(bed.Machine, FaultKind.Wrecked);
        }
        // 통합: 새 배는 재배대가 두 배 — 절반을 부숴도 모자라지 않았다. 먹는 양의 60%를 못 대도록 더 부순다 (조류 · 버섯 판 포함)
        float need = crew * FoodPolicy.MealsPerPersonDay;
        foreach (var bed in w.Ship.FurnitureOf(FurnitureType.GrowBed).Where(f => f.Machine!.Efficiency > 0f).OrderBy(f => f.Id).ToList())
        {
            if (FoodPolicy.GrowingPerDay(w) < need * 0.6f) break;
            bed.Machine!.Crop!.Growth = 0.05f;
            w.Machines.Break(bed.Machine, FaultKind.Wrecked);
        }
        return w;
    }

    private static int RunLivingTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"대인원 생활 점검 (v10.11) · 시드 {seed}\n");

        // ── 1) 조리사: 큰 배에는 조리 전담이 있고, 그 사람이 조리를 가장 많이 한다 ──
        foreach (var key in new[] { "Hanbit", "Cheonma" })
        {
            var w = World.CreateDefault(seed, 0, key);
            Run(w, SimTime.TicksPerDay * 3);
            var cooks = w.Crew.Where(c => c.Role == CrewRole.Cook).ToList();
            var bots = w.Crew.Where(c => c.Role == CrewRole.Botanist).ToList();
            int total = w.Crew.Sum(c => c.Stats.MealsCooked);
            float cookAvg = cooks.Count == 0 ? 0f : (float)cooks.Average(c => c.Stats.MealsCooked);
            float botAvg = bots.Count == 0 ? 0f : (float)bots.Average(c => c.Stats.MealsCooked);
            float botHarvest = bots.Count == 0 ? 0f : (float)bots.Average(c => c.Stats.Harvests);
            // 조리사가 가장 많이 한다 (작은 배의 재배 담당은 "재배·조리" 겸직이라 동률까지는 괜찮다) · 조리사 몫이 전체의 40% 넘게
            float share = total == 0 ? 0f : cooks.Sum(c => c.Stats.MealsCooked) / (float)total;
            int topOther = w.Crew.Where(c => c.Role != CrewRole.Cook).Select(c => c.Stats.MealsCooked).DefaultIfEmpty(0).Max();
            bool ok = cooks.Count >= 1 && cookAvg >= botAvg && cookAvg >= topOther && share >= 0.4f;
            Check($"{w.Ship.Name}: 조리사가 부엌을 맡는다", ok,
                $"조리사 {cooks.Count}명 · 사흘에 조리 {total}인분 (조리사 몫 {share * 100:0}%) · 조리사 평균 {cookAvg:0} ↔ 재배 담당 평균 {botAvg:0} (수확 {botHarvest:0.0}번)");
            if (!ok)
                foreach (var c in w.Crew.Where(c => c.Stats.MealsCooked > 0))
                    Console.WriteLine($"      {c.Name} ({CrewRoles.Name(c.Role)}) 조리 {c.Stats.MealsCooked} · 근무 {c.Schedule.WorkStart:0}시부터 {c.Schedule.WorkLength:0}시간 · 조리 솜씨 {c.SkillLevel(Skill.Cooking) * 100:0}%");
        }

        // ── 2) 배급: 먹을 것이 이틀치 아래로 떨어지면 회의로 정하고, 배급하는 배가 덜 굶는다 ──
        {
            var a = Hungry(seed, "Hanbit", noRation: false);
            var b = Hungry(seed, "Hanbit", noRation: true);
            float daysA = FoodPolicy.FoodDays(a);
            bool decided = false;
            float starvA = 0f, starvB = 0f;
            for (int t = 0; t < SimTime.TicksPerDay * 4; t++)
            {
                a.Step(); b.Step();
                decided |= a.Food.Rationing;
                if (a.Tick % World.SystemInterval != 0) continue;
                float dt = World.SystemInterval / (float)SimTime.TicksPerHour;
                starvA += a.Crew.Count(c => !c.Dead && c.Needs.Food < 0.05f) * dt;
                starvB += b.Crew.Count(c => !c.Dead && c.Needs.Food < 0.05f) * dt;
            }
            var vote = a.History.Events.FirstOrDefault(e => e.Kind == HistoryKind.Decision && e.Text.Contains("배급"));
            Check("배급 — 이틀치 아래면 회의로 정하고, 덜 굶는다", decided && starvA < starvB,
                $"처음 {daysA:0.0}일치 · 배급 {(decided ? $"{a.Food.Rationings}번" : "안 함")} · 굶주림 {starvA:0} ↔ 배급 없는 배 {starvB:0}사람·시간" +
                (vote != null ? $" · {vote.Text}" : ""));
        }

        // ── 3) 배급 해제: 먹을 것이 넉넉해지면 푼다 ──
        {
            var w = Hungry(seed, "Hanbit", noRation: false);
            for (int t = 0; t < SimTime.TicksPerDay * 2 && !w.Food.Rationing; t++) w.Step();
            bool on = w.Food.Rationing;
            // 보급이 왔다: 냉장고 가득
            foreach (var fr in w.Ship.FurnitureOf(FurnitureType.Fridge)) fr.Storage!.Add(ItemKind.Meal, 80);
            for (int t = 0; t < SimTime.TicksPerDay * 2 && w.Food.Rationing; t++) w.Step();
            Check("배급 해제 — 넉넉해지면 푼다", on && !w.Food.Rationing, $"배급 {(on ? "시작함" : "안 함")} → {(w.Food.Rationing ? "아직 배급 중" : "풀었다")} · 먹을 것 {FoodPolicy.FoodDays(w):0.0}일치");
        }

        // ── 4) 배급한 배도 같은 시드면 같은 역사 ──
        {
            var w = Hungry(seed, "Hanbit", noRation: false);
            Run(w, SimTime.Hours(30));
            uint h = SaveGame.StateHash(w);
            // Hungry는 기록 밖에서 물자를 줄였다 (불러온 배는 그 일을 모른다) → 결정론만: 같은 배를 두 번
            var w2 = Hungry(seed, "Hanbit", noRation: false);
            Run(w2, SimTime.Hours(30));
            Check("배급한 배의 결정론", SaveGame.StateHash(w2) == h, $"배급 {w.Food.Rationings}번 · 지문 {(SaveGame.StateHash(w2) == h ? "같음" : "다름")}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 대인원 생활 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
