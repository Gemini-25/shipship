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

            // ── 3) 인수인계: 감지기·컴퓨터 일지 없이 사람의 말로만 — 넘기는 배 ↔ 안 넘기는 배 ──
            {
                int[] lost = new int[2], dup = new int[2], missed = new int[2], prevented = new int[2], verbal = new int[2], unhandedMiss = new int[2];
                for (int i = 0; i < 3; i++)
                    foreach (bool hand in new[] { true, false })
                    {
                        var w = Watchful(seed + i * 131, "Mirinae", 0.1f, x => { x.Watch.NoSensors = true; x.Watch.NoHandover = !hand; });
                        Run(w, SimTime.TicksPerDay * 6);
                        int k = hand ? 0 : 1;
                        lost[k] += w.Watch.Stats.Lost;
                        dup[k] += w.Watch.Stats.Duplicates;
                        missed[k] += w.Precursors.Missed;
                        prevented[k] += w.Precursors.Prevented;
                        verbal[k] += w.Watch.Stats.Verbal;
                        unhandedMiss[k] += w.Watch.Stats.MissedUnhanded;
                    }
                Check("인수인계 — 넘기면 덜 잃고 덜 겹친다", verbal[0] > 0 && lost[0] < lost[1] && dup[0] + missed[0] <= dup[1] + missed[1],
                    $"넘기는 배: 말로 인계 {verbal[0]} · 인계 못 함 {lost[0]} · 중복 점검 {dup[0]} · 막음 {prevented[0]} · 놓침 {missed[0]}(전해지지 않아 {unhandedMiss[0]})  ↔  " +
                    $"안 넘기는 배: 인계 못 함 {lost[1]} · 중복 점검 {dup[1]} · 막음 {prevented[1]} · 놓침 {missed[1]}(전해지지 않아 {unhandedMiss[1]})");
            }

            // ── 4) 오래된 측정값: 주 컴퓨터가 멎으면 값이 멈춘다 — 그래도 현장 사람은 소리를 듣는다 ──
            {
                var w = Watchful(seed, "Mirinae", 0f);
                var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!;
                foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.StorageFault);
                pump.Omen = new Omen { Kind = OmenKind.Vibration, Fault = FaultKind.BearingWear, Cause = OmenCause.BearingWear, Since = w.Tick, Due = w.Tick + SimTime.Hours(30) };
                long before = pump.LastReading;
                Run(w, SimTime.Hours(3));
                float age = (w.Tick - pump.LastReading) / (float)SimTime.TicksPerHour;
                bool offline = !w.Automation.MainOnline;
                w.Watch.NoSensors = true; // 이제부터는 사람의 귀로만 (컴퓨터가 돌아와도 감지기가 먼저 찾지 않게)
                // 친숙한 정비사를 펌프 옆에 둔다
                var mech = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                mech.Familiarize(FurnitureType.CoolantPump, 0.8f);
                bool heard = false;
                for (int h = 0; h < 48 && !heard; h++)
                {
                    if (mech.CanAct && mech.Room != pump.Body.Room && pump.Body.UseSpots.FirstOrDefault(s => w.Ship.IsOpenFloor(s)) is var spot && spot != default)
                    {
                        mech.Position = spot.Center; mech.PreviousPosition = mech.Position;
                    }
                    Run(w, SimTime.Minutes(15));
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
                Run(w, SimTime.TicksPerDay * 3);
                var st = w.Watch.Stats;
                float avg = w.Ship.Machines.Average(m => m.SensorCal);
                Check("계기 오류 — 교정이 틀어진 감지기가 헛경보를 내고, 전기 기사가 다시 맞춘다", st.Phantoms > 0 && st.Calibrations > 0 && avg > 0.75f,
                    $"계기 오류 {st.Phantoms}(잡음 {st.PhantomsCaught}) · 교정 {st.Calibrations}방 · 평균 교정 50% → {avg * 100:0}% · 헛일(계기 오류에 부품을 갈음) {w.Watch.Notes.Count(n => n.Omen.Cause == OmenCause.Phantom && n.WrongFixes > 0)}");
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
