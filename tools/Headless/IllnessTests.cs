using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v14.1 질병·몸 상태 1 → 30: 원인 · 증상 · 옮음 · 진단 · 치료 · 면역
public static partial class Program
{
    private static int RunIllnessTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"질병 점검 (v14.1) · 시드 {seed}\n");
        try
        {
            // 1) 목록 30 (열병 + 29) — 다섯 갈래, 저마다 원인·증상·낫는 길
            {
                var all = AilmentSystem.Catalog.ToList();
                var groups = all.GroupBy(a => a.Group).Select(g => $"{AilmentSystem.GroupName(g.Key)} {g.Count()}");
                bool ok = all.Count == 30 && all.Select(a => a.Id).Distinct().Count() == 30 && all.Select(a => a.Name).Distinct().Count() == 30
                          && all.All(a => a.Cause.Length > 0 && a.Symptom.Length > 0 && a.Days > 0f && a.Peak > 0f) && all.GroupBy(a => a.Group).All(g => g.Count() >= 3);
                Check("목록 — 질병·몸 상태 30 (다섯 갈래)", ok, $"{all.Count}가지 · " + string.Join(" · ", groups) + "\n      " + string.Join(" · ", all.Select(a => a.Name)));
            }
            // 2) 원인: 그 조건일 때만 잘 걸린다
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.First(x => x.CanAct && x.Room != null && !x.BornAboard);
                var r = c.Room!;
                var found = new List<string>();
                bool Risk(string id) => w.Ailments.Risks(c).Any(x => x.id == id && x.perHour >= 0.002f);
                void Try(string id, Action set, Action undo) { bool before = Risk(id); set(); bool after = Risk(id); undo(); if (!before && after) found.Add(AilmentSystem.Spec(id).Name); }
                float temp = r.Air.Temperature;
                Try("hypothermia", () => r.Air.Temperature = 4f, () => r.Air.Temperature = temp);
                Try("heatstroke", () => r.Air.Temperature = 45f, () => r.Air.Temperature = temp);
                Try("radiation", () => c.Dose = 1.4f, () => c.Dose = 0f);
                Try("atrophy", () => c.Fitness = 0.2f, () => c.Fitness = 0.6f);
                float trauma = c.Memory.Trauma;
                Try("ptsd", () => c.Memory.Trauma = 0.8f, () => c.Memory.Trauma = trauma);
                float age = c.Age;
                Try("arthritis", () => c.Age = 72f, () => c.Age = age);
                float inj = c.Vitals.Injury; long tt = c.Vitals.TreatedTick;
                Try("woundinf", () => { c.Vitals.Injury = 0.4f; c.Vitals.TreatedTick = w.Tick - SimTime.Hours(14); }, () => { c.Vitals.Injury = inj; c.Vitals.TreatedTick = tt; });
                float stress = c.Needs.Stress;
                Try("insomnia", () => c.Needs.Stress = 0.85f, () => c.Needs.Stress = stress);
                float food = c.Needs.Food;
                Try("malnutrition", () => c.Needs.Food = 0.1f, () => c.Needs.Food = food);
                float water = w.Water.Level;
                Try("dehydration", () => w.Water.Level = 0f, () => w.Water.Level = water);
                float co = r.Air.CO, tox = r.Air.Toxin, co2 = r.Air.CO2;
                Try("cobrain", () => r.Air.CO = 0.3f, () => r.Air.CO = co);
                Try("chemburn", () => r.Air.Toxin = 0.5f, () => r.Air.Toxin = tox);
                Try("co2ache", () => r.Air.CO2 = 2f, () => r.Air.CO2 = co2);
                float flood = r.Flood;
                Try("moldlung", () => r.Flood = r.Cells.Count * 20f * 0.1f, () => r.Flood = flood);
                // 출항 첫 이틀의 우주 멀미
                var fresh = World.CreateDefault(seed, 0, "Hanbit");
                bool sick = fresh.Crew.Any(x => fresh.Ailments.Risks(x).Any(k => k.id == "spacesick"));
                if (sick) found.Add("우주 멀미");
                Check("원인 — 그 조건일 때만 잘 걸린다 (추위·더위·피폭·운동 부족·상처·나이·스트레스·굶주림·물·가스·곰팡이·출항)", found.Count >= 15,
                    $"{found.Count}가지: " + string.Join(" · ", found));
            }
            // 3) 증상: 손이 느려지고 · 걸음이 굼뜨고 · 덜 쉰다 · 공황이 잦다
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.First(x => x.CanAct && x.Room != null);
                w.Ailments.Catch(c, "atrophy");
                w.Ailments.Catch(c, "insomnia");
                w.Ailments.Catch(c, "ptsd");
                Run(w, SimTime.Hours(6));
                var fx = c.Fx;
                Check("증상 — 근육 위축은 손과 걸음을, 불면증은 잠을, 외상 후 스트레스는 공황을", fx.WorkMul < 0.97f && fx.WalkMul < 0.97f && fx.Sleep > 0.1f && fx.Panic > 0.3f,
                    $"{c.Name}: {w.Ailments.Line(c)} → 손 ×{fx.WorkMul:0.00} · 걸음 ×{fx.WalkMul:0.00} · 잠 −{fx.Sleep * 100:0}% · 공황 ×{1f + fx.Panic:0.00}");
            }
            // 4) 옮는 병: 감기가 같은 방에서 옮고, 나으면 면역
            {
                int spread = 0, immune = 0;
                string sample = "";
                for (int k = 0; k < 3; k++)
                {
                    var w = DayOne(seed + k, "Hanbit");
                    var c = w.Crew.First(x => x.CanAct);
                    w.Ailments.Catch(c, "cold");
                    Run(w, SimTime.Hours(80));
                    spread += w.Ailments.Stats.Spread;
                    immune += w.Crew.Count(x => x.AilmentImmune.Contains("cold"));
                    if (sample.Length == 0) sample = w.Ailments.Stats.ToString();
                }
                Check("옮는 병 — 감기가 같은 방에서 옮고, 나으면 면역", spread > 0 && immune > 0, $"배 셋, 80시간: 옮음 {spread}번 · 면역 {immune}명 · 예: {sample}");
            }
            // 5) 진단과 치료: 치통은 의무관이 진단하고 약(구급 키트)을 써야 낫는다 — 약이 없으면 그대로
            {
                (bool diag, int treat, float healed, string line) Case(bool kits)
                {
                    var w = DayOne(seed, "Mirinae");
                    if (!kits) foreach (var f in w.Ship.Containers) { int n = f.Storage!.Count(ItemKind.MedKit); if (n > 0) f.Storage.Take(ItemKind.MedKit, n); }
                    var c = w.Crew.First(x => x.CanAct && !x.Quals.Contains(Qual.Medic));
                    var a = w.Ailments.Catch(c, "toothache")!;
                    Run(w, SimTime.Hours(40));
                    return (a.Diagnosed, a.Treatments, a.Healed, $"{c.Name}: {(c.Ailments.Contains(a) ? w.Ailments.Line(c) : "나았다")}");
                }
                var with = Case(true);
                var without = Case(false);
                Check("진단과 치료 — 의무관이 진단하고 약을 쓴다 (약이 없으면 낫지 않는다)", with.diag && with.treat >= 1 && with.healed > 0f && without.healed == 0f,
                    $"구급 키트 있음: 진단 {(with.diag ? "됨" : "안 됨")} · 약 {with.treat}번 · 나은 만큼 {with.healed:0.00}일 ({with.line}) / 없음: 약 {without.treat}번 · {without.healed:0.00}일");
            }
            // 6) 누워야 낫는 병: 방사선 병이면 치료 침대에 눕는다
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => x.CanAct && x.Room != null);
                var a = w.Ailments.Catch(c, "radiation")!;
                bool bed = false;
                for (int h = 0; h < 36 && !bed; h++) { Run(w, SimTime.Hours(1)); bed |= AilmentSystem.InBed(c); }
                Check("누워야 낫는 병 — 방사선 병이면 치료 침대에 눕는다", bed && a.Healed > 0f, $"{c.Name}: 침대 {(bed ? "누웠다" : "안 누움")} · 나은 만큼 {a.Healed:0.00}일 · {w.Ailments.Line(c)}");
            }
            // 7) 몸을 움직여야: 근육 위축은 체력 단련이 오르면 낫는다
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.First(x => x.CanAct);
                var a = w.Ailments.Catch(c, "atrophy")!;
                c.Fitness = 0.25f; Run(w, SimTime.Hours(12)); float lazy = a.Healed;
                c.Fitness = 0.8f; Run(w, SimTime.Hours(12)); float fit = a.Healed - lazy;
                Check("몸을 움직여야 — 근육 위축은 체력 단련이 오르면 낫는다", lazy == 0f && fit > 0.2f, $"체력 단련 25%: {lazy:0.00}일 · 80%: +{fit:0.00}일");
            }
            // 8) 저절로: 열흘 항해에 여러 가지가 생기고 낫는다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay * 10);
                var st = w.Ailments.Stats;
                Check("저절로 — 열흘 항해에 여러 가지 병이 생기고 낫는다", st.Cases.Count >= 3 && st.Recovered >= 2,
                    $"{st} · " + string.Join(" · ", st.Cases.OrderByDescending(k => k.Value).Select(k => $"{AilmentSystem.Spec(k.Key).Name} {k.Value}")));
            }
            // 9) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay * 2);
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 병이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 질병 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
