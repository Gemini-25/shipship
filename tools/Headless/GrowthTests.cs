using System;
using System.Linq;
using ShipSim.Core;

// v11.3 승무원 성장과 회복: 배우기(도제), 재활, 후유증, 핵심 인력이 빠질 때
public static partial class Program
{
    private static int RunGrowthTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원 성장 점검 (v11.3) · 시드 {seed}\n");

        // ── 1) 배우기: 평화로운 스무 날 — 유일한 전문가 곁에서 후배가 배운다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var before = w.Crew.ToDictionary(c => c.Id, c => c.SkillLevels.ToArray());
            Run(w, SimTime.TicksPerDay * 20);
            var gains = w.Crew.Where(c => before.ContainsKey(c.Id)).SelectMany(c => Skills.All.Select(s => (c, s, d: c.RawSkill(s) - before[c.Id][(int)s]))).Where(x => x.d > 0.08f).OrderByDescending(x => x.d).Take(4).ToList(); // 통합6 스무 날 사이 새로 탄 사람(구조 · 합류)은 뺀다
            var medBackup = w.Crew.Where(c => c.Role != CrewRole.Medic).Max(c => c.RawSkill(Skill.Medicine));
            Check("배우기 — 유일한 전문가 곁에서 후배가 배운다", w.Growth.Lessons >= 5 && gains.Count > 0,
                $"수업 {w.Growth.Lessons}번 · 고비 {w.Growth.Milestones}번 · {string.Join(", ", gains.Select(g => $"{g.c.Name} {Skills.Name(g.s)} +{g.d * 100:0}"))} · 의무관 말고 가장 나은 의료 {medBackup * 100:0}%");
        }

        // ── 2) 핵심 인력이 빠지면: 배운 배 ↔ 안 배운 배 — 의무관이 떠난 뒤 세 명이 다친다 ──
        {
            float treatedA = 0f, treatedB = 0f, healA = 0f, healB = 0f, earlyA = 0f, earlyB = 0f;
            float qualA = 0f, qualB = 0f;
            int countA = 0, countB = 0;
            int runs = 3;
            for (int i = 0; i < runs; i++)
                foreach (bool train in new[] { true, false })
                {
                    var w = DayOne(seed + i * 211, "Mirinae");
                    w.Growth.NoTraining = !train;
                    Run(w, SimTime.TicksPerDay * 20);
                    // 통합8 스무 날째에 큰 사고가 겹쳐 다친 사람이 여럿이면 셋의 치료가 그 뒤로 밀린다 (치료 주문 8 · 의무관도 다침) — 배가 가라앉을 때까지 (이틀 안) 기다렸다가 견준다
                    for (int k = 0; k < 48 && (Crisis.Level(w) >= CrisisLevel.Alert || w.Crew.Any(c => !c.Dead && c.Vitals.Injury > 0.2f)); k++) Run(w, SimTime.Hours(1));
                    var medic = w.Crew.First(c => c.Role == CrewRole.Medic);
                    // 의무관이 떠났다 (구조선으로 옮겨 탔다 — 시험을 위해 배에서 뺀다)
                    medic.Vitals.Health = 0f;
                    w.CrewCanDie = true;
                    Run(w, 30);
                    w.CrewCanDie = false;
                    // 다치는 사람: 의료 솜씨가 가장 낮은 셋 (두 배 모두 가장 나은 사람은 멀쩡하다 — 그 사람까지 다치면 어느 배든 대신할 사람이 없다)
                    var hurt = w.Crew.Where(c => !c.Dead).OrderBy(c => c.RawSkill(Skill.Medicine)).ThenBy(c => c.Id).Take(3).ToList();
                    // 두 배 모두 구급 키트는 넉넉히 (스무 날 동안 쓴 양이 달라도 — 솜씨만 견준다)
                    var kits = w.Ship.Furniture.FirstOrDefault(f => f.Storage != null && f.Room.Type == RoomType.Medbay) ?? w.Ship.Furniture.First(f => f.Storage != null);
                    kits.Storage!.Add(ItemKind.MedKit, Math.Max(0, 8 - w.Ship.CountStored(ItemKind.MedKit)));
                    foreach (var c in hurt) Player.Hazard(w, HazardKind.WorkAccident, default, c.Id);
                    float inj0 = hurt.Sum(c => c.Vitals.Injury);
                    long first = -1;
                    long start = w.Tick;
                    float early = 0f, qual = 0f;
                    int count = 0;
                    var prevInj = hurt.Select(c => c.Vitals.Injury).ToArray();
                    for (int t = 0; t < SimTime.Hours(48); t++)
                    {
                        w.Step();
                        // 치료 한 번에 준 부상 (치료한 사람의 솜씨가 정한다: 0.06 + 0.14 × 의료)
                        for (int j = 0; j < hurt.Count; j++)
                        {
                            if (hurt[j].Vitals.TreatedTick == w.Tick) { qual += prevInj[j] - hurt[j].Vitals.Injury; count++; }
                            prevInj[j] = hurt[j].Vitals.Injury;
                        }
                        if (first < 0 && hurt.Any(c => c.Vitals.TreatedTick > start)) first = w.Tick;
                        if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "4" && i == int.Parse(Environment.GetEnvironmentVariable("SHIPSIM_RUN") ?? "0") && train && first < 0 && t % SimTime.Hours(2) == 0)
                        {
                            var doc = w.Crew.Where(c => !c.Dead).OrderByDescending(c => c.RawSkill(Skill.Medicine)).First();
                            Console.WriteLine($"      {SimTime.Clock(w.Tick)} 치료 주문 {w.Board.Open.Count(o => o.Kind == WorkKind.Treat)} [{string.Join(",", w.Board.Open.Where(o => o.Kind == WorkKind.Treat).Select(o => $"{o.Urgency:0.00}/{o.Assignee?.Name}/{o.BlockedReason}"))}] 구급키트 {w.Ship.CountStored(ItemKind.MedKit)} · {doc.Name} {doc.Job?.Label} 평가 {string.Join(",", doc.LastEvaluations.Take(3).Select(e => $"{e.Activity.Label}:{e.Score:0.00}({e.Reason})"))} · 다친 이 " +
                                string.Join(" | ", hurt.Select(c => $"{c.Name} {c.Vitals.Injury:0.00} {c.Job?.Label} 방 {c.Room?.Name}")));
                        }
                        if (t == SimTime.Hours(12)) early = inj0 - hurt.Sum(c => c.Vitals.Injury);
                    }
                    float heal = inj0 - hurt.Sum(c => c.Vitals.Injury);
                    float hrs = first < 0 ? 48f : (first - start) / (float)SimTime.TicksPerHour;
                    if (train) { treatedA += hrs; healA += heal; earlyA += early; qualA += qual; countA += count; }
                    else { treatedB += hrs; healB += heal; earlyB += early; qualB += qual; countB += count; }
                    Console.WriteLine($"    시드 {seed + i * 211} {(train ? "배운 배" : "안 배운 배")}: 의무관 말고 가장 나은 의료 {w.Crew.Where(c => !c.Dead).Max(c => c.RawSkill(Skill.Medicine)) * 100:0}% · 첫 치료 {hrs:0.0}시간 · 치료 {count}번(한 번에 {(count > 0 ? qual / count : 0f) * 100:0}%p) · 12시간 안에 나은 부상 {early * 100:0}%p · 이틀 동안 {heal * 100:0}%p");
                }
            // 치료 한 번의 질(솜씨가 정한다)이 낫고 첫 치료가 늦지 않다. 나은 부상의 합은 자연 치유·재활·부상 크기의 운이 섞여 참고로만 적는다
            float qa = countA > 0 ? qualA / countA : 0f, qb = countB > 0 ? qualB / countB : 0f;
            Check("핵심 인력이 빠져도 — 배운 배가 더 잘 치료한다", qa > qb && treatedA <= treatedB + 0.25f * runs,
                $"치료 한 번에 {qa * 100:0.0} ↔ {qb * 100:0.0}%p · 첫 치료 평균 {treatedA / runs:0.0} ↔ {treatedB / runs:0.0}시간 · 12시간 안에 나은 부상 합 {earlyA * 100:0} ↔ {earlyB * 100:0}%p · 이틀 {healA * 100:0} ↔ {healB * 100:0}%p");
        }

        // ── 3) 재활과 후유증: 크게 다친 사람은 후유증이 남고, 재활하면 부상이 빨리 낫고 후유증이 절반까지 준다 ──
        {
            float injA = 0f, injB = 0f, scarA = 0f, scarB = 0f, floorA = 0f, inj6A = 0f, inj6B = 0f;
            foreach (bool rehab in new[] { true, false })
            {
                var w = DayOne(seed, "Mirinae");
                w.Growth.NoRehab = !rehab;
                w.Growth.NoFirstAid = true; // v15 키트 없는 응급 처치도 빼고
                w.Ailments.Disabled = true; // v14.4 재활만 견준다 (상처 감염으로 누워 버리면 재활할 틈이 없다)
                // 치료 운을 빼고 재활만 견준다 (구급 키트가 없으면 둘 다 저절로 낫는다)
                foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null)) f.Storage!.Take(ItemKind.MedKit, 999);
                var c = w.Crew.First(x => x.Role == CrewRole.Technician);
                NeedsSystem.AddInjury(c.Vitals, 0.7f, "감압");
                c.Vitals.Health = 0.6f;
                c.Vitals.TreatedTick = w.Tick; // 처음 치료는 받았다 (재활은 치료 뒤에 — 그 뒤로는 키트가 없다)
                float scar0 = c.Vitals.Scar;
                // 빨리 낫나는 사흘째 부상으로 (엿새면 둘 다 다 낫는다), 후유증은 엿새째로
                Run(w, SimTime.TicksPerDay * 3);
                float inj3 = c.Vitals.Injury;
                Run(w, SimTime.TicksPerDay * 3);
                if (rehab) { injA = inj3; inj6A = c.Vitals.Injury; scarA = c.Vitals.Scar; floorA = c.Vitals.ScarFloor; }
                else { injB = inj3; inj6B = c.Vitals.Injury; scarB = c.Vitals.Scar; }
                Console.WriteLine($"    {(rehab ? "재활하는 배" : "재활 없는 배")}: {c.Name} 부상 70% → 사흘째 {inj3 * 100:0.0}% → 엿새째 {c.Vitals.Injury * 100:0}% · 후유증 {scar0 * 100:0}% → {c.Vitals.Scar * 100:0}% (바닥 {c.Vitals.ScarFloor * 100:0}%) · 재활 {c.Stats.RehabSessions}번");
            }
            Check("재활·후유증 — 재활하면 빨리 낫고, 후유증은 절반까지만", (injA < injB || injA <= injB && inj6A < inj6B) && scarA < scarB && scarA >= floorA - 1e-4f && floorA > 0f,
                $"사흘째 부상 {injA * 100:0.0} ↔ {injB * 100:0.0}% · 엿새째 후유증 {scarA * 100:0} ↔ {scarB * 100:0}%");
        }

        // ── 4) 결정론 ──
        {
            uint H() { var w = DayOne(seed, "Mirinae"); Run(w, SimTime.TicksPerDay * 6); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("배우기·재활이 들어간 배의 결정론", a == b, $"지문 {(a == b ? "같음" : "다름")}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 승무원 성장 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
