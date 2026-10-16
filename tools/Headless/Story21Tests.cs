using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v21 이야기꾼 · 난이도 세분화 점검 (--story21test).
public static partial class Program
{
    private static int RunStory21Test(int seed)
    {
        _fails = 0;
        float p0 = Storyteller.PersonaValue, l0 = Storyteller.LevelValue, f0 = Storyteller.FreqValue, s0 = Storyteller.SizeValue, b0 = Storyteller.BurstValue;
        float rd0 = HazardSystem.RandomDays, rs0 = HazardSystem.SizeRangeValue;
        void Reset() { Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0; Storyteller.FreqValue = f0; Storyteller.SizeValue = s0; Storyteller.BurstValue = b0; HazardSystem.RandomDays = rd0; HazardSystem.SizeRangeValue = rs0; }
        Console.WriteLine($"이야기꾼 · 난이도 세분화 점검 (v21) · 시드 {seed}\n");
        try
        {
            // 1) 난이도 8단계
            {
                var names = Enumerable.Range(1, Storyteller.MaxLevel).Select(Storyteller.LevelName).ToList();
                var gaps = new List<float>(); var bigs = new List<float>(); var stock = new List<float>(); var panic = new List<float>();
                Storyteller.PersonaValue = 1;
                for (int l = 1; l <= Storyteller.MaxLevel; l++)
                {
                    Storyteller.LevelValue = l;
                    gaps.Add(Storyteller.GapDays); bigs.Add(Storyteller.BigScale); stock.Add(Storyteller.StockScale); panic.Add(MindSystem.PanicScale);
                }
                bool down = gaps.Zip(gaps.Skip(1), (a, b) => b < a).All(x => x), up = bigs.Zip(bigs.Skip(1), (a, b) => b > a).All(x => x);
                bool flat = stock.Skip(4).All(v => v == stock[4]) && panic.Skip(4).All(v => v == panic[4]);
                bool same3 = MathF.Abs(gaps[2] - 1.4f) < 1e-6f && MathF.Abs(bigs[2] - 1f) < 1e-6f && MathF.Abs(stock[2] - 1f) < 1e-6f; // 보통은 예전 값 그대로
                Check("난이도 8단계 — 위로 갈수록 잦고 크다 (물자 · 공황은 가혹 그대로 · 보통은 예전 값)", names.Distinct().Count() == 8 && down && up && flat && same3 && Tuning.Find("story.level")!.Max == 8f,
                    $"{string.Join(" · ", names.Select((n, i) => $"{n} {gaps[i]:0.00}일×{bigs[i]:0.0}"))} · 물자 {string.Join("/", stock.Select(v => $"{v:0.00}"))}");
            }
            // 2) 성격 15 · 리듬 · 크기 범위 수치
            {
                var names = Enumerable.Range(0, (int)StoryPersona.Wave + 1).Select(i => Storyteller.PersonaName((StoryPersona)i)).ToList();
                bool tunes = new[] { "story.freq", "story.size", "story.burst", "incident.size" }.All(k => Tuning.Find(k) != null) && Tuning.Find("story.persona")!.Max == (int)StoryPersona.Wave;
                bool notes = Enumerable.Range(1, (int)StoryPersona.Wave).All(i => Storyteller.PersonaNote((StoryPersona)i).Length > 0 && Storyteller.PersonaRhythm((StoryPersona)i).Length > 0);
                Check("성격 15 (끔 + 14) · 리듬 셋 · 무작위 사고 크기 범위가 기록되는 수치", names.Distinct().Count() == names.Count && tunes && notes && HazardSystem.RandomSteps.Length == 10,
                    string.Join(" · ", names) + $" · 주기 {string.Join("/", HazardSystem.RandomSteps.Select(s => s.name))}");
            }

            // 3) 고르는 규칙: 같은 배 · 같은 순간에서 성격마다 400번씩
            {
                Storyteller.LevelValue = 4;
                var ws = DayOne(seed, "Mirinae");
                var st = ws.Story;
                float cap = st.Capacity();
                const int N = 400;
                (float big, float[] fam, float shipShare) Dist(StoryPersona p)
                {
                    int big = 0, ship = 0;
                    var fam = new float[4];
                    for (int i = 0; i < N; i++)
                    {
                        var (k, _) = st.Sample(p, 0f, cap);
                        if (Storyteller.Big(k) || ScaleTable.OfKey(k) >= IncidentScale.Ship) big++;
                        if (ScaleTable.OfKey(k) >= IncidentScale.System) ship++;
                        fam[Storyteller.Family(k)] += 1f / N;
                    }
                    return (big / (float)N, fam, ship / (float)N);
                }
                string F(float[] f) => string.Join("/", f.Select(x => $"{x * 100:0}"));
                var steady = Dist(StoryPersona.Steady);
                var siege = Dist(StoryPersona.Siege);
                var implo = Dist(StoryPersona.Implosion);
                var mara = Dist(StoryPersona.Marathon);
                st.PhaseForTest = 0.05f; var early = Dist(StoryPersona.Climax);
                st.PhaseForTest = 0.95f; var late = Dist(StoryPersona.Climax);
                st.PhaseForTest = null;
                st.Waves = 0; var wave0 = Dist(StoryPersona.Wave);
                st.Waves = 5; var wave5 = Dist(StoryPersona.Wave);
                st.Waves = 0;
                st.LastKey = "meteor"; st.ChainLen = 1; var chain = Dist(StoryPersona.Chain);
                st.ChainLen = 0;
                bool siegeOk = siege.fam[Storyteller.FamSpace] >= 0.7f && siege.fam[Storyteller.FamSpace] > steady.fam[Storyteller.FamSpace] + 0.3f;
                bool imploOk = implo.fam[Storyteller.FamMachine] + implo.fam[Storyteller.FamLife] >= 0.75f && implo.fam[Storyteller.FamSpace] <= 0.05f;
                bool maraOk = mara.big < 0.03f && mara.big * 3f < steady.big + 0.01f;
                bool climaxOk = late.big > early.big + 0.15f;
                bool waveOk = wave5.big > wave0.big + 0.1f;
                bool chainOk = chain.fam[Storyteller.FamBurn] >= 0.5f && chain.fam[Storyteller.FamBurn] > steady.fam[Storyteller.FamBurn] + 0.2f; // 운석 뒤엔 불 · 공기 쪽으로 번진다
                Check("새 성격 — 고르는 규칙 (포위: 바깥만 · 내부 붕괴: 고장 · 사람 · 마라톤: 작은 것만 · 절정: 끝에 크게 · 파도: 물결마다 크게 · 연쇄: 이어지는 갈래로)",
                    siegeOk && imploOk && maraOk && climaxOk && waveOk && chainOk,
                    $"갈래(바깥/고장/불/생물 %) 꾸준 {F(steady.fam)} 큰 것 {steady.big * 100:0}% · 포위 {F(siege.fam)}{(siegeOk ? "" : " ✘")} · 내부 {F(implo.fam)}{(imploOk ? "" : " ✘")}" +
                    $" · 마라톤 큰 것 {mara.big * 100:0}%{(maraOk ? "" : " ✘")} · 절정 큰 것 {early.big * 100:0}% → {late.big * 100:0}%{(climaxOk ? "" : " ✘")}" +
                    $" · 파도 큰 것 {wave0.big * 100:0}% → 다섯째 물결 {wave5.big * 100:0}%{(waveOk ? "" : " ✘")} · 연쇄(운석 뒤) {F(chain.fam)}{(chainOk ? "" : " ✘")}");

                // 리듬 크기 · 혹독 이상의 규모 비율
                Storyteller.SizeValue = -2; var small = Dist(StoryPersona.Steady);
                Storyteller.SizeValue = 2; var large = Dist(StoryPersona.Steady);
                Storyteller.SizeValue = 0;
                Storyteller.LevelValue = 8; var hell = Dist(StoryPersona.Steady);
                Storyteller.LevelValue = 4;
                Check("리듬 크기 · 절멸 — 큰 것 몫이 커진다", large.shipShare > small.shipShare + 0.2f && hell.shipShare > steady.shipShare + 0.05f,
                    $"계통 이상 몫: 아주 작게 {small.shipShare * 100:0}% · 성격대로 {steady.shipShare * 100:0}% · 아주 크게 {large.shipShare * 100:0}% · 절멸 {hell.shipShare * 100:0}%");

                // 간격: 리듬 잦기 · 절정의 앞뒤 · 마라톤
                double Gap(StoryPersona p, int n = 200) { double sum = 0; for (int i = 0; i < n; i++) sum += st.SampleGap(p); return sum / n / SimTime.TicksPerHour; }
                Storyteller.FreqValue = -2; double rare = Gap(StoryPersona.Steady);
                Storyteller.FreqValue = 2; double often = Gap(StoryPersona.Steady);
                Storyteller.FreqValue = 0; double mid = Gap(StoryPersona.Steady);
                double mar = Gap(StoryPersona.Marathon);
                st.PhaseForTest = 0.1f; double cEarly = Gap(StoryPersona.Climax);
                st.PhaseForTest = 0.95f; double cLate = Gap(StoryPersona.Climax);
                st.PhaseForTest = null;
                Check("간격 — 잦기 리듬 · 마라톤은 잦게 · 절정은 끝으로 갈수록 잦게", often < rare * 0.35 && mar < mid * 0.6 && cLate * 3 < cEarly,
                    $"꾸준 아주 드물게 {rare:0.0}시간 · 성격대로 {mid:0.0} · 아주 잦게 {often:0.0} · 마라톤 {mar:0.0} · 절정 앞 {cEarly:0.0} → 끝 {cLate:0.0}");
            }

            // 4) 흐름: 같은 시드 · 같은 배 닷새 — 성격마다
            {
                var flows = new Dictionary<StoryPersona, (List<(long tick, string key, float tension)> picks, int fired, int overlaps, World w)>();
                foreach (var persona in new[] { StoryPersona.Steady, StoryPersona.Chain, StoryPersona.Siege, StoryPersona.Marathon, StoryPersona.Wave })
                {
                    Storyteller.PersonaValue = (int)persona; Storyteller.LevelValue = 4;
                    var w = DayOne(seed, "Mirinae");
                    Run(w, SimTime.TicksPerDay * 5);
                    flows[persona] = (w.Story.Picks.ToList(), w.Story.Fired, w.Story.Overlaps, w);
                }
                int Quick(List<(long tick, string key, float tension)> p, float hours) => p.Zip(p.Skip(1), (a, b) => b.tick - a.tick).Count(d => d < SimTime.Hours(hours));
                var chain = flows[StoryPersona.Chain];
                var spread = chain.w.Story.Journal.Count(j => j.why.StartsWith("앞 사고에서 번졌다"));
                var siege = flows[StoryPersona.Siege];
                float space = siege.picks.Count == 0 ? 0f : siege.picks.Count(p => Storyteller.Family(p.key) == Storyteller.FamSpace) / (float)siege.picks.Count;
                var mara = flows[StoryPersona.Marathon];
                var wave = flows[StoryPersona.Wave];
                var steady = flows[StoryPersona.Steady];
                bool chainOk = spread >= 1 && Quick(chain.picks, 1.6f) >= 1;
                bool siegeOk = siege.picks.Count >= 2 && space >= 0.8f;
                bool maraOk = mara.picks.Count > steady.picks.Count && mara.picks.All(p => !Storyteller.Big(p.key));
                bool waveOk = wave.w.Story.Waves >= 1 && Quick(wave.picks, 2.1f) >= 1;
                string J(StoryPersona p) => string.Join(" / ", flows[p].w.Story.Journal.Take(3).Select(j => $"{SimTime.Day(j.tick)}일 {SimTime.Clock(j.tick)} {j.what}"));
                Check("새 성격 — 닷새 흐름 (연쇄: 번진다 · 포위: 바깥 · 마라톤: 잦고 작다 · 파도: 무리로 온다)", chainOk && siegeOk && maraOk && waveOk,
                    $"꾸준 {steady.picks.Count}건 · 연쇄 {chain.picks.Count}건 번짐 {spread}{(chainOk ? "" : " ✘")} · 포위 {siege.picks.Count}건 바깥 {space * 100:0}%{(siegeOk ? "" : " ✘")}" +
                    $" · 마라톤 {mara.picks.Count}건{(maraOk ? "" : " ✘")} · 파도 {wave.picks.Count}건 물결 {wave.w.Story.Waves}{(waveOk ? "" : " ✘")} ‖ 연쇄: {J(StoryPersona.Chain)} ‖ 파도: {J(StoryPersona.Wave)}");
            }

            // 5) 겹침: 보통 ↔ 절멸 (같은 성격 · 같은 시드 · 나흘)
            {
                var res = new List<(int level, int fired, int overlaps, int quick, int deaths)>();
                foreach (int level in new[] { 3, 8 })
                {
                    Storyteller.PersonaValue = 1; Storyteller.LevelValue = level;
                    var w = DayOne(seed, "Mirinae");
                    Run(w, SimTime.TicksPerDay * 4);
                    var p = w.Story.Picks;
                    int quick = p.Zip(p.Skip(1), (a, b) => b.tick - a.tick).Count(d => d < SimTime.Hours(1.1f));
                    res.Add((level, w.Story.Fired, w.Story.Overlaps, quick, w.History.Deaths));
                }
                var (_, f3, o3, q3, d3) = res[0];
                var (_, f8, o8, q8, d8) = res[1];
                Check("절멸 — 사고가 잦고 겹친다 (수습하는 중에 · 곧이어 하나 더)", f8 >= f3 * 2 && o8 > o3 && q8 >= 1,
                    $"보통: 사고 {f3} · 겹침 {o3} · 곧이어 {q3} · 사망 {d3}  ↔  절멸: 사고 {f8} · 겹침 {o8} · 곧이어 {q8} · 사망 {d8}");
            }

            // 6) 우주급 재난: 혹독 이상은 항해마다 더 온다
            {
                int Plans(int level)
                {
                    Storyteller.PersonaValue = 1; Storyteller.LevelValue = level;
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    Run(w, SimTime.Hours(1));
                    return w.Cosmic.Planned.Count;
                }
                int a = Plans(3), b = Plans(8);
                Storyteller.PersonaValue = 0; Storyteller.LevelValue = 8;
                bool off = Storyteller.CosmicExtra == 0 && Storyteller.CosmicPower == 1f;
                Check("우주급 재난 — 절멸은 항해마다 셋 더 · 더 세게 (이야기꾼을 끄면 그대로)", b == a + 3 && off,
                    $"이번 항해 계획: 보통 {a} · 절멸 {b} · 세기 ×{1f + 0.12f * 3:0.00}");
            }

            // 7) 무작위 사고 10단계 · 크기 범위
            {
                Storyteller.PersonaValue = 0; Storyteller.LevelValue = 3;
                HazardSystem.RandomDays = 1f / 12f;
                var w = World.CreateDefault(seed, 0, "Mirinae");
                Run(w, SimTime.TicksPerDay);
                int c0 = w.Hazards.RandomCount;
                Run(w, SimTime.TicksPerDay);
                int day2 = w.Hazards.RandomCount - c0;
                var ws = World.CreateDefault(seed, 0, "Mirinae");
                HazardSystem.SizeRangeValue = 0;
                var smallKeys = Enumerable.Range(0, 120).Select(_ => ws.Hazards.SampleRandomKey()).ToList();
                HazardSystem.SizeRangeValue = 2;
                var bigKeys = Enumerable.Range(0, 120).Select(_ => ws.Hazards.SampleRandomKey()).ToList();
                HazardSystem.SizeRangeValue = 1;
                bool smallOk = smallKeys.All(k => ScaleTable.OfKey(k) <= IncidentScale.Room && !Storyteller.Big(k));
                bool bigOk = bigKeys.All(k => ScaleTable.OfKey(k) >= IncidentScale.System || Storyteller.Big(k));
                Check("무작위 사고 — 2시간 주기는 하루에 여러 번 · 크기 범위대로 고른다", day2 >= 6 && smallOk && bigOk,
                    $"2시간 주기 둘째 날 {day2}건 · 작은 것만: {string.Join(",", smallKeys.Distinct().Take(5))}{(smallOk ? "" : " ✘")} · 큰 것만: {string.Join(",", bigKeys.Distinct().Take(5))}{(bigOk ? "" : " ✘")}");
                HazardSystem.RandomDays = rd0;
            }

            // 8) 결정론 · 저장 — 파도형 지옥 + 몰아치기
            {
                Storyteller.PersonaValue = (int)StoryPersona.Wave; Storyteller.LevelValue = 7; Storyteller.BurstValue = 1;
                uint H() { var w = DayOne(seed, "Mirinae"); Run(w, SimTime.TicksPerDay * 2); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                var w1 = DayOne(seed, "Mirinae");
                Run(w1, SimTime.TicksPerDay);
                var bytes = StateSave.Write(w1);
                var w2 = StateSave.Read(bytes);
                Run(w1, SimTime.TicksPerDay);
                Run(w2, SimTime.TicksPerDay);
                uint x = SaveGame.StateHash(w1), y = SaveGame.StateHash(w2);
                bool story = w1.Story.Waves == w2.Story.Waves && w1.Story.Fired == w2.Story.Fired && w1.Story.Next == w2.Story.Next;
                Check("결정론 · 저장 — 같은 시드 같은 지문 · 저장해 이어 가도 같은 이야기", a == b && x == y && story,
                    $"{a:x8} / {b:x8} · 이어 감 {x:x8} / {y:x8} · 물결 {w1.Story.Waves}/{w2.Story.Waves} · 사고 {w1.Story.Fired}/{w2.Story.Fired}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        finally { Reset(); }
        Console.WriteLine(_fails == 0 ? "\n✔ 이야기꾼 · 난이도 세분화 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
