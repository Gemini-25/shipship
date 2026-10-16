using System;
using System.Linq;
using System.Globalization;
using System.IO;
using ShipSim.Core;

// v11.3 승무원 성장과 회복: 배우기(도제), 재활, 후유증, 핵심 인력이 빠질 때
public static partial class Program
{
    private static int RunGrowthTest(int seed)
    {
        _fails = 0;
        if (ChildIndex("growth") is int ci) // 자식 프로세스: 2) 장면의 시드 하나 — 스무 날을 한 번 돌려 배운 배 · 안 배운 배로 나눈다 (ChildRuns.cs)
        {
            static string Pack((float hrs, float heal, float early, float qual, int count, string line) r) =>
                string.Join("\t", new[] { r.hrs, r.heal, r.early, r.qual }.Select(f => BitConverter.SingleToInt32Bits(f).ToString(CultureInfo.InvariantCulture))) + $"\t{r.count}\t{r.line}";
            var (trained, plain) = GrowthPairRun(seed, ci);
            ChildReply(Pack(trained) + "\u001e" + Pack(plain));
            return 0;
        }
        Console.WriteLine($"승무원 성장 점검 (v11.3) · 시드 {seed}\n");

        // 2) 장면의 스무 날 항해 8번(시드마다 하나 — 둘로 나눠 배운 배 · 안 배운 배)은 시드마다 자식 프로세스 하나로 나눠 먼저 띄워 두고, 그동안 1) · 3) · 4)를 돈다 (출력 차례는 그대로)
        var core = System.Threading.Tasks.Task.Run(() => RunChildren("growth", 8));
        // ── 1) 배우기: 평화로운 스무 날 — 유일한 전문가 곁에서 후배가 배운다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var before = w.Crew.ToDictionary(c => c.Id, c => c.SkillLevels.ToArray());
            Run(w, SimTime.TicksPerDay * 20);
            var gains = w.Crew.Where(c => before.ContainsKey(c.Id)).SelectMany(c => Skills.All.Select(s => (c, s, d: c.RawSkill(s) - before[c.Id][(int)s]))).Where(x => x.d > 0.08f).OrderByDescending(x => x.d).Take(4).ToList(); // 통합6 스무 날 사이 새로 탄 사람(구조 · 합류)은 뺀다
            var medBackup = w.Crew.Where(c => c.Role != CrewRole.Medic).Max(c => c.RawSkill(Skill.Medicine));
            Check("배우기 — 유일한 전문가 곁에서 후배가 배운다", w.Growth.Lessons >= 4 && gains.Count > 0, // 로봇이 먼저: 일상 수리를 로봇이 맡아 사람이 함께 하는 일이 줄었다 (스무 날 5 → 4번)
                $"수업 {w.Growth.Lessons}번 · 고비 {w.Growth.Milestones}번 · {string.Join(", ", gains.Select(g => $"{g.c.Name} {Skills.Name(g.s)} +{g.d * 100:0}"))} · 의무관 말고 가장 나은 의료 {medBackup * 100:0}%");
        }

        var held = Console.Out; var later = new StringWriter(); Console.SetOut(later); // 3) · 4)는 2) 뒤에 찍는다
        // ── 3) 재활과 후유증: 크게 다친 사람은 후유증이 남고, 재활하면 부상이 빨리 낫고 후유증이 절반까지 준다 ──
        {
            float injA = 0f, injB = 0f, scarA = 0f, scarB = 0f, floorA = 0f, inj6A = 0f, inj6B = 0f, inj3A = 0f, inj3B = 0f;
            foreach (bool rehab in new[] { true, false })
            {
                var w = DayOne(seed, "Mirinae");
                w.Growth.NoRehab = !rehab;
                w.Growth.NoFirstAid = true; // v15 키트 없는 응급 처치도 빼고
                w.Ailments.Disabled = true; // v14.4 재활만 견준다 (상처 감염으로 누워 버리면 재활할 틈이 없다)
                w.Policies.Set("expedition", 2, "시험: 원정 없이"); // v19 부상 31%로 재활하던 사람이 원정대로 나가 사흘 동안 배에 없었다 (원정 자격은 부상 35% 미만)
                // 치료 운을 빼고 재활만 견준다 (구급 키트가 없으면 둘 다 저절로 낫는다)
                foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null)) f.Storage!.Take(ItemKind.MedKit, 999);
                var c = w.Crew.First(x => x.Role == CrewRole.Technician);
                NeedsSystem.AddInjury(c.Vitals, 0.7f, "감압");
                c.Vitals.Health = 0.6f;
                c.Vitals.TreatedTick = w.Tick; // 처음 치료는 받았다 (재활은 치료 뒤에 — 그 뒤로는 키트가 없다)
                float scar0 = c.Vitals.Scar;
                // 빨리 낫나는 사흘째 부상으로 (엿새면 둘 다 다 낫는다), 후유증은 엿새째로
                // v19 빨리 낫나는 엿새 동안의 평균 부상으로 (사흘째 한 시점은 그사이 잠 · 침대에서 쉰 시간에 따라 크게 흔들렸다 — 같은 배 16 ~ 40%)
                float inj3 = 0f, mean = 0f;
                for (int k = 1; k <= 24; k++)
                {
                    Run(w, SimTime.Hours(6));
                    mean += c.Vitals.Injury / 24f;
                    if (k == 12) inj3 = c.Vitals.Injury;
                }
                if (rehab) { injA = mean; inj3A = inj3; inj6A = c.Vitals.Injury; scarA = c.Vitals.Scar; floorA = c.Vitals.ScarFloor; }
                else { injB = mean; inj3B = inj3; inj6B = c.Vitals.Injury; scarB = c.Vitals.Scar; }
                Console.WriteLine($"    {(rehab ? "재활하는 배" : "재활 없는 배")}: {c.Name} 부상 70% → 사흘째 {inj3 * 100:0.0}% → 엿새째 {c.Vitals.Injury * 100:0}% · 후유증 {scar0 * 100:0}% → {c.Vitals.Scar * 100:0}% (바닥 {c.Vitals.ScarFloor * 100:0}%) · 재활 {c.Stats.RehabSessions}번");
            }
            Check("재활·후유증 — 재활하면 빨리 낫고, 후유증은 절반까지만", injA < injB && scarA < scarB && scarA >= floorA - 1e-4f && floorA > 0f,
                $"엿새 평균 부상 {injA * 100:0.0} ↔ {injB * 100:0.0}% (사흘째 {inj3A * 100:0.0} ↔ {inj3B * 100:0.0}% · 엿새째 {inj6A * 100:0} ↔ {inj6B * 100:0}%) · 엿새째 후유증 {scarA * 100:0} ↔ {scarB * 100:0}%");
        }

        // ── 4) 결정론 ──
        {
            uint H() { var w = DayOne(seed, "Mirinae"); Run(w, SimTime.TicksPerDay * 6); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("배우기·재활이 들어간 배의 결정론", a == b, $"지문 {(a == b ? "같음" : "다름")}");
        }

        Console.SetOut(held);

        // ── 2) 핵심 인력이 빠지면: 배운 배 ↔ 안 배운 배 — 의무관이 떠난 뒤 세 명이 다친다 ──
        {
            float treatedA = 0f, treatedB = 0f, healA = 0f, healB = 0f, earlyA = 0f, earlyB = 0f;
            float qualA = 0f, qualB = 0f;
            int countA = 0, countB = 0;
            var firstA = new System.Collections.Generic.List<float>(); var firstB = new System.Collections.Generic.List<float>();
            int runs = 8; // v19 셋으로는 스무 날 동안 갈린 역사의 운(누가 남아 치료하나 — 타고난 솜씨)이 훈련보다 컸다 — 여덟 번 견준다
            var replies = core.Result;
            for (int i = 0; i < runs; i++)
                foreach (bool train in new[] { true, false })
                {
                    var f = replies[i].Split('\u001e')[train ? 0 : 1].Split('\t', 6);
                    float Bits(int k) => BitConverter.Int32BitsToSingle(int.Parse(f[k], CultureInfo.InvariantCulture));
                    float hrs = Bits(0), heal = Bits(1), early = Bits(2), qual = Bits(3);
                    int count = int.Parse(f[4], CultureInfo.InvariantCulture);
                    if (train) { treatedA += hrs; healA += heal; earlyA += early; qualA += qual; countA += count; firstA.Add(hrs); }
                    else { treatedB += hrs; healB += heal; earlyB += early; qualB += qual; countB += count; firstB.Add(hrs); }
                    Console.WriteLine(f[5]);
                }
            // 사람이 한 치료 한 번의 질(솜씨가 정한다)이 낫고 첫 치료가 늦지 않다. 나은 부상의 합은 로봇 치료 · 자연 치유 · 재활이 섞여 참고로만 적는다
            float qa = countA > 0 ? qualA / countA : 0f, qb = countB > 0 ? qualB / countB : 0f;
            // v19 첫 치료는 가운데값으로 견준다 — 스무 날 사이 식량난에 빠진 배 하나(의사까지 굶주려 48시간 치료 없음)가 평균을 통째로 뒤집었다
            static float Median(System.Collections.Generic.List<float> x) { var o = x.OrderBy(v => v).ToList(); int n = o.Count; return n == 0 ? 0f : n % 2 == 1 ? o[n / 2] : (o[n / 2 - 1] + o[n / 2]) / 2f; }
            float ma = Median(firstA), mb = Median(firstB);
            // 같은 배를 둘로 나눴으니 사람이 한 치료가 양쪽 다 있어야 견줄 수 있다 (한쪽이 0번이면 로봇만 치료한 것)
            Check("핵심 인력이 빠져도 — 배운 배가 더 잘 치료한다 (같은 배 · 같은 사람 · 솜씨만 다르게)", countA > 0 && countB > 0 && qa > qb && ma <= mb + 0.25f,
                $"사람 치료 {countA} ↔ {countB}번 · 한 번에 {qa * 100:0.0} ↔ {qb * 100:0.0}%p · 첫 치료 가운데값 {ma:0.0} ↔ {mb:0.0}시간 (평균 {treatedA / runs:0.0} ↔ {treatedB / runs:0.0}) · 12시간 안에 나은 부상 합 {earlyA * 100:0} ↔ {earlyB * 100:0}%p · 이틀 {healA * 100:0} ↔ {healB * 100:0}%p");
        }

        Console.Write(later.ToString());
        Console.WriteLine(_fails == 0 ? "\n✔ 승무원 성장 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>growthtest 2): 의무관이 떠난 배에서 셋이 다친다 — 시드 seed + i × 211.
    /// 스무 날을 한 번만 돌린 배를 둘로 나눠, '안 배운 배'는 그 스무 날 늘어난 솜씨를 첫날 값으로 되돌린다 —
    /// 같은 사람 · 같은 역사 · 같은 세 사람이 다치고, 솜씨만 다르다.
    /// (예전엔 두 배가 스무 날을 따로 살아 탄 사람부터 갈렸다: 의무관 말고 가장 나은 의료가 짝끼리 46% ↔ 98% — 훈련보다 운이 컸다)
    /// 치료 한 번의 질은 사람이 한 치료만 센다 — 로봇이 먼저 나서는 배라 로봇 치료가 섞이면 사람 솜씨가 묻힌다.</summary>
    private static ((float hrs, float heal, float early, float qual, int count, string line) train, (float hrs, float heal, float early, float qual, int count, string line) plain) GrowthPairRun(int seed, int i)
    {
        var w = DayOne(seed + i * 211, "Mirinae");
        var day1 = w.Crew.ToDictionary(c => c.Id, c => c.SkillLevels.ToArray());
        Run(w, SimTime.TicksPerDay * 20);
        // 통합8 스무 날째에 큰 사고가 겹쳐 다친 사람이 여럿이면 셋의 치료가 그 뒤로 밀린다 (치료 주문 8 · 의무관도 다침) — 배가 가라앉을 때까지 (이틀 안) 기다렸다가 견준다
        for (int k = 0; k < 48 && (Crisis.Level(w) >= CrisisLevel.Alert || w.Crew.Any(c => !c.Dead && c.Vitals.Injury > 0.2f)); k++) Run(w, SimTime.Hours(1));
        var snap = StateSave.Write(w);
        int medicId = w.Crew.First(c => c.Role == CrewRole.Medic).Id;
        // 다치는 사람: 배운 배에서 의료 솜씨가 가장 낮은 셋 — 두 배 모두 같은 사람 (가장 나은 사람은 멀쩡하다 — 그 사람까지 다치면 어느 배든 대신할 사람이 없다)
        var hurtIds = w.Crew.Where(c => !c.Dead && c.Id != medicId).OrderBy(c => c.RawSkill(Skill.Medicine)).ThenBy(c => c.Id).Take(3).Select(c => c.Id).ToArray();
        return (Half(true), Half(false));

        (float hrs, float heal, float early, float qual, int count, string line) Half(bool train)
        {
            var x = StateSave.Read(snap);
            x.Growth.NoTraining = !train;
            float unlearned = 0f;
            if (!train)
                foreach (var c in x.Crew)
                    if (day1.TryGetValue(c.Id, out var s0))
                        for (int s = 0; s < s0.Length; s++)
                            if (c.SkillLevels[s] > s0[s]) { if (c.Id != medicId) unlearned += c.SkillLevels[s] - s0[s]; c.SkillLevels[s] = s0[s]; }
            // 의무관이 떠났다 (구조선으로 옮겨 탔다 — 시험을 위해 배에서 뺀다)
            x.Crew.First(c => c.Id == medicId).Vitals.Health = 0f;
            x.CrewCanDie = true;
            Run(x, 30);
            x.CrewCanDie = false;
            var hurt = hurtIds.Select(id => x.Crew.First(c => c.Id == id)).Where(c => !c.Dead).ToList();
            // 두 배 모두 구급 키트는 넉넉히 — 솜씨만 견준다
            var kits = x.Ship.Furniture.FirstOrDefault(f => f.Storage != null && f.Room.Type == RoomType.Medbay) ?? x.Ship.Furniture.First(f => f.Storage != null);
            kits.Storage!.Add(ItemKind.MedKit, Math.Max(0, 8 - x.Ship.CountStored(ItemKind.MedKit)));
            foreach (var c in hurt) Player.Hazard(x, HazardKind.WorkAccident, default, c.Id);
            float inj0 = hurt.Sum(c => c.Vitals.Injury);
            long first = -1;
            long start = x.Tick;
            float early = 0f, qual = 0f;
            int count = 0, bots = 0;
            var prevInj = hurt.Select(c => c.Vitals.Injury).ToArray();
            for (int t = 0; t < SimTime.Hours(48); t++)
            {
                int botTreats = x.Robots.V21.Treats;
                x.Step();
                bool byBot = x.Robots.V21.Treats != botTreats;
                // 치료 한 번에 준 부상 (치료한 사람의 솜씨가 정한다: 0.06 + 0.14 × 의료)
                for (int j = 0; j < hurt.Count; j++)
                {
                    // v19 남은 부상이 한 번에 줄 수 있는 양(0.2)보다 작으면 누가 해도 다 낫는다 — 솜씨가 드러나지 않아 자주 치료한 배가 손해를 봤다 → 큰 부상일 때만 센다
                    if (hurt[j].Vitals.TreatedTick == x.Tick && prevInj[j] >= 0.2f)
                    {
                        if (byBot) bots++;
                        else { qual += prevInj[j] - hurt[j].Vitals.Injury; count++; }
                    }
                    prevInj[j] = hurt[j].Vitals.Injury;
                }
                if (first < 0 && hurt.Any(c => c.Vitals.TreatedTick > start)) first = x.Tick;
                if (t == SimTime.Hours(12)) early = inj0 - hurt.Sum(c => c.Vitals.Injury);
            }
            float heal = inj0 - hurt.Sum(c => c.Vitals.Injury);
            float hrs = first < 0 ? 48f : (first - start) / (float)SimTime.TicksPerHour;
            // 떠난 의무관은 30틱 사이 다 숨지지 않을 수 있다 (쓰러져 있다) — 예전 줄의 '가장 나은 의료 99%'는 의무관이었다
            string line = $"    시드 {seed + i * 211} {(train ? "배운 배" : "안 배운 배")}: 의무관 말고 가장 나은 의료 {x.Crew.Where(c => !c.Dead && c.Id != medicId).Max(c => c.RawSkill(Skill.Medicine)) * 100:0}%"
                          + (train ? $" · 스무 날 수업 {x.Growth.Lessons}번" : $" · 스무 날 늘어난 솜씨 {unlearned * 100:0}%p를 되돌림")
                          + $" · 첫 치료 {hrs:0.0}시간 · 사람 치료 {count}번(한 번에 {(count > 0 ? qual / count : 0f) * 100:0}%p) · 로봇 치료 {bots}번 · 12시간 안에 나은 부상 {early * 100:0}%p · 이틀 동안 {heal * 100:0}%p";
            return (hrs, heal, early, qual, count, line);
        }
    }
}
