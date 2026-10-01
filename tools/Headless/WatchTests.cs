using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v12.0 당직 일지 · 진단 · 인수인계 · 감지기 교정
public static partial class Program
{
    /// <summary>작은 이상이 잦은 배 (하루에 설비 하나당 이 확률).</summary>
    private static World Watchful(int seed, string ship, float daily, Action<World>? setup = null)
    {
        var w = DayOne(seed, ship);
        setup?.Invoke(w);
        Tuning.AnomalyPerDay = daily;
        return w;
    }

    private static int RunWatchTest(int seed)
    {
        _fails = 0;
        float daily0 = Tuning.AnomalyPerDay;
        Console.WriteLine($"당직 일지 · 진단 점검 (v12.0) · 시드 {seed}\n");
        try
        {
            // ── 1) 관측 · 판단 · 확인이 나뉜다 ──
            {
                var w = Watchful(seed, "Mirinae", 0.08f);
                Run(w, SimTime.TicksPerDay * 5);
                var notes = w.Watch.Notes;
                bool split = notes.Count >= 5 && notes.All(n => n.Observation.Length > 0) && notes.Any(n => n.Suspect != null || n.Confirmed != null)
                             && notes.Any(n => n.Confirmed != null && n.Finding != null);
                Check("관측 · 판단 · 확인이 나뉜다", split, $"기록 {notes.Count} · {w.Watch.Stats}");
                foreach (var n in notes.Where(n => n.Trail.Count >= 3).OrderByDescending(n => n.Trail.Count).Take(3))
                {
                    Console.WriteLine($"    #{n.Id} {n.Machine.Name} ({n.Stage}) 진짜 원인: {Causes.Name(n.Omen.Cause)}");
                    foreach (var t in n.Trail) Console.WriteLine($"        {t}");
                }
            }

            // ── 2) 열어 보는 배 ↔ 판단대로 가는 배: 잘못된 부품 교체와 막은 고장 ──
            {
                int[] wrong = new int[2], prevented = new int[2], missed = new int[2], diag = new int[2];
                for (int i = 0; i < 3; i++)
                    foreach (bool careful in new[] { true, false })
                    {
                        var w = Watchful(seed + i * 101, "Mirinae", 0.08f, x => x.Watch.NoDiagnosis = !careful);
                        Run(w, SimTime.TicksPerDay * 6);
                        int k = careful ? 0 : 1;
                        wrong[k] += w.Watch.Stats.WrongFixes;
                        prevented[k] += w.Precursors.Prevented;
                        missed[k] += w.Precursors.Missed;
                        diag[k] += w.Watch.Stats.Diagnoses;
                    }
                Check("꼼꼼한 사람은 열어 본다 — 잘못된 부품 교체가 준다", diag[0] > 0 && wrong[0] < wrong[1],
                    $"열어 보는 배: 분해 검사 {diag[0]} · 잘못된 부품 교체 {wrong[0]} · 막음 {prevented[0]} · 놓침 {missed[0]}  ↔  판단대로: 잘못된 교체 {wrong[1]} · 막음 {prevented[1]} · 놓침 {missed[1]}");
            }

            // ── 3) 인수인계: 근무 끝나기 직전에 본 이상 — 넘기는 배 ↔ 안 넘기는 배 (감지기·컴퓨터 일지 없이 사람의 말로만) ──
            {
                int[] lost = new int[2], dup = new int[2], missed = new int[2], prevented = new int[2], verbal = new int[2];
                for (int i = 0; i < 4; i++)
                    foreach (bool hand in new[] { true, false })
                    {
                        var w = Watchful(seed + i * 131, "Mirinae", 0f, x => { x.Watch.NoSensors = true; x.Watch.NoHandover = !hand; });
                        // 근무가 30분 안에 끝나는 사람을 찾는다
                        CrewMember? a = null;
                        for (int t = 0; t < 24 * 12 && a == null; t++)
                        {
                            float hr = SimTime.HourOfDay(w.Tick);
                            a = w.Crew.FirstOrDefault(c => WatchLog.OnShift(c, w) && !SimTime.InWindow(SimTime.Wrap(hr + 0.5f), c.Schedule.WorkStart, c.Schedule.WorkLength));
                            if (a == null) Run(w, SimTime.Minutes(5));
                        }
                        if (a == null) continue;
                        // 그 사람이 설비 넷에서 작은 이상을 본다 (고치려면 부품·시간이 드는 것들)
                        var targets = w.Ship.Machines.Where(m => m.Omen == null && m.Faults.Count == 0 && m.Spec.PowerDraw > 0f
                                                                 && m.Spec.FaultKinds.Any(k => k != FaultKind.BreakerTrip && Prevention.KindOf(k) != null))
                            .OrderBy(m => m.Body.Id).Where((m, k) => k % 5 == i % 5).Take(4).ToList();
                        foreach (var m in targets)
                        {
                            var fault = m.Spec.FaultKinds.First(k => k != FaultKind.BreakerTrip && Prevention.KindOf(k) != null);
                            var kind = Prevention.KindOf(fault)!.Value;
                            var o = new Omen { Kind = kind, Fault = fault, Cause = Causes.For(m.Body.Type, kind).First(), Since = w.Tick, Due = w.Tick + SimTime.Hours(14) };
                            m.Omen = o;
                            w.Precursors.Omens++;
                            Prevention.Detect(w, m, o, "당직", a);
                        }
                        Run(w, SimTime.Hours(16));
                        int k2 = hand ? 0 : 1;
                        lost[k2] += w.Watch.Stats.Lost;
                        dup[k2] += w.Watch.Stats.Duplicates;
                        missed[k2] += w.Precursors.Missed;
                        prevented[k2] += w.Precursors.Prevented;
                        verbal[k2] += w.Watch.Stats.Verbal;
                    }
                Check("인수인계 — 근무 끝에 본 이상을 넘기면 덜 잃고 더 막는다", verbal[0] > 0 && lost[0] < lost[1] && prevented[0] >= prevented[1],
                    $"넘기는 배: 말로 인계 {verbal[0]} · 인계 못 함 {lost[0]} · 중복 점검 {dup[0]} · 막음 {prevented[0]} · 놓침 {missed[0]}  ↔  " +
                    $"안 넘기는 배: 인계 못 함 {lost[1]} · 중복 점검 {dup[1]} · 막음 {prevented[1]} · 놓침 {missed[1]}");
            }

            // ── 4) 오래된 측정값: 주 컴퓨터가 멎으면 값이 멈춘다 — 그래도 현장 사람은 소리를 듣는다 ──
            {
                var w = Watchful(seed, "Mirinae", 0f);
                var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!;
                foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked);
                pump.Omen = new Omen { Kind = OmenKind.Vibration, Fault = FaultKind.BearingWear, Cause = OmenCause.BearingWear, Since = w.Tick, Due = w.Tick + SimTime.Hours(30) };
                long before = pump.LastReading;
                for (int q = 0; q < 180; q++)
                {
                    // 세 시간 동안 컴퓨터가 멎어 있게 (고치러 와도 다시 멎는다)
                    foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer))
                        if (comp.Machine!.Efficiency > 0.25f) w.Machines.Break(comp.Machine!, FaultKind.Wrecked);
                    Run(w, SimTime.Minutes(1));
                }
                float age = (w.Tick - pump.LastReading) / (float)SimTime.TicksPerHour;
                bool offline = !w.Automation.MainOnline;
                w.Watch.NoSensors = true; // 이제부터는 사람의 귀로만 (컴퓨터가 돌아와도 감지기가 먼저 찾지 않게)
                // 친숙한 정비사를 펌프 옆에 둔다
                var mech = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                mech.Familiarize(FurnitureType.CoolantPump, 0.8f);
                bool heard = false;
                for (int h = 0; h < 48 && !heard; h++)
                {
                    // 펌프 곁에 붙여 둔다 (1분마다 — 데려다 놓아도 곧 쉬러 걸어가 버리면 들을 틈이 없다)
                    for (int m = 0; m < 15; m++)
                    {
                        if (mech.CanAct && mech.Room != pump.Body.Room && pump.Body.UseSpots.FirstOrDefault(s => w.Ship.IsOpenFloor(s)) is var spot && spot != default)
                        {
                            mech.Position = spot.Center; mech.PreviousPosition = mech.Position;
                        }
                        Run(w, SimTime.Minutes(1));
                    }
                    heard = pump.Omen?.Note is ShiftNote n && n.How is "당직" or "옆방에서 들음" || pump.Omen == null && w.Watch.Notes.Any(x => x.Machine == pump && x.How != "감지기");
                }
                Check("오래된 측정값 — 컴퓨터가 멎어도 현장에서 소리를 듣는다", offline && age > 2.5f && heard,
                    $"펌프 마지막 측정 {age:0.0}시간 전 · 세 시간 뒤 주 컴퓨터 {(offline ? "멎음" : "켜짐")} · 소리로 찾음 {(heard ? "예" : "아니오")} · " +
                    string.Join(" / ", w.Watch.Notes.Where(x => x.Machine == pump).Select(x => $"{x.Author} {x.How}: {x.Observation}")));
            }

            // ── 5) 감지기 교정이 틀어지면 계기 오류가 나고, 교정하면 걷힌다 ──
            {
                var w = Watchful(seed, "Mirinae", 0f);
                foreach (var m in w.Ship.Machines) m.SensorCal = 0.5f;
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2")
                    for (int h = 0; h < 72; h++)
                    {
                        Run(w, SimTime.Hours(1));
                        if (h % 6 != 0) continue;
                        var cal = w.Board.Open.Where(o => o.Kind == WorkKind.Calibrate).ToList();
                        Console.WriteLine($"      {SimTime.Clock(w.Tick)} 교정 주문 {cal.Count} (긴급 {string.Join(",", cal.Select(o => o.Urgency.ToString("0.00")))}) · 자격자 " +
                            string.Join(" | ", w.Crew.Where(c => Life.HasQual(c, Qual.Computer)).Select(c => $"{c.Name} {c.Job?.Label} 잠 {c.Pose == Pose.Sleeping} 평가 " + string.Join(",", c.LastEvaluations.Take(3).Select(e => $"{e.Activity.Label}:{e.Score:0.00}")))));
                    }
                else Run(w, SimTime.TicksPerDay * 3);
                var st = w.Watch.Stats;
                float avg = w.Ship.Machines.Average(m => m.SensorCal);
                // 사람은 진짜 교정값을 모른다 — 헛경보를 현장에서 확인하고 나서야 그 방을 교정한다
                var calibrated = w.Ship.Machines.Where(m => m.LastCalibrated > 0).ToList();
                float fixedAvg = calibrated.Select(m => m.SensorCal).DefaultIfEmpty(0f).Average();
                // (헛경보가 난 감지기는 현장에서 바로 맞추고, 남은 방은 교정 순회로 — 순회는 급하지 않아 사흘 안에 안 돌 수도 있다)
                Check("계기 오류 — 교정이 틀어진 감지기가 헛경보를 내고, 현장 확인 뒤 다시 맞춘다 (그 자리에서 · 방을 돌며)", st.Phantoms > 0 && st.PhantomsCaught > 0 && calibrated.Count >= Math.Min(3, st.PhantomsCaught) && fixedAvg > 0.85f,
                    $"계기 오류 {st.Phantoms}(잡음 {st.PhantomsCaught}) · 교정 {st.Calibrations}방(맞춘 감지기 {calibrated.Count}개 평균 {fixedAvg * 100:0}%) · 배 전체 평균 50% → {avg * 100:0}% · 헛일(계기 오류에 부품을 갈음) {w.Watch.Notes.Count(n => n.Omen.Cause == OmenCause.Phantom && n.WrongFixes > 0)}");
            }

            // ── 6) 친숙함: 설비를 만질수록 ──
            {
                var w = Watchful(seed, "Mirinae", 0.08f);
                Run(w, SimTime.TicksPerDay * 5);
                var best = w.Crew.SelectMany(c => c.Familiarity.Select(kv => (c, kv.Key, kv.Value))).OrderByDescending(x => x.Value).Take(3).ToList();
                Check("친숙함 — 만져 본 설비일수록", best.Count > 0 && best[0].Value > 0.08f,
                    string.Join(", ", best.Select(b => $"{b.c.Name} {FurnitureTypes.Name(b.Key)} {b.Value * 100:0}%")));
            }

            // ── 7) 결정론 ──
            {
                uint H() { var w = Watchful(seed, "Mirinae", 0.08f); Run(w, SimTime.TicksPerDay * 4); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                Check("당직 일지가 들어간 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
            }
        }
        finally { Tuning.AnomalyPerDay = daily0; }

        Console.WriteLine(_fails == 0 ? "\n✔ 당직 일지 · 진단 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
