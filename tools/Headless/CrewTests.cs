using System;
using System.Linq;
using ShipSim.Core;

// v12.7 승무원: 경력·가치관·습관·자격 · 부위 부상·의수 · 실수 · 말다툼 · 죽음 뒤 · 문병
public static partial class Program
{
    private static int RunCrewTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원 점검 (v12.7) · 시드 {seed}\n");
        try
        {
            // 1) 사람마다 살아온 길·가치관·습관·자격
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var lines = w.Crew.Take(4).Select(c => $"{c.Name}: {Life.Profile(c)} · 자격 {string.Join("·", c.Quals.Select(Life.Name))}");
                int kinds = w.Crew.Select(c => c.Background).Distinct().Count();
                bool all = w.Crew.All(c => c.Profiled && c.Habits.Count >= 1);
                Check("경력·가치관·습관·자격 — 사람마다 다르다", all && kinds >= 4, $"경력 {kinds}가지 · " + string.Join(" / ", lines));
                var w2 = World.CreateDefault(seed, 0, "Hanbit");
                Check("같은 시드는 같은 사람들 (결정론)", w.Crew.Zip(w2.Crew).All(p => p.First.Background == p.Second.Background && p.First.Value == p.Second.Value), "");
            }
            // 2) 부위 부상: 파편은 팔·다리, 감압은 폐 — 다리를 다치면 걸음이, 팔을 다치면 손이 느리다
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew[0];
                float s0 = Locomotion.Speed(c);
                NeedsSystem.AddInjury(c.Vitals, 0.3f, "감압");
                NeedsSystem.AddInjury(c.Vitals, 0.35f, "운석 파편");
                float s1 = Locomotion.Speed(c);
                bool lungs = c.Vitals.Wounds.Any(x => x.Part == BodyPart.Lungs);
                bool limb = c.Vitals.Wounds.Any(x => Wounds.IsArm(x.Part) || Wounds.IsLeg(x.Part));
                Check("부위 부상 — 감압은 폐, 파편은 팔·다리", lungs && limb && s1 < s0, $"{Wounds.Summary(c.Vitals)} · 걸음 {s0:0.000} → {s1:0.000} · 손 {Wounds.HandFactor(c.Vitals) * 100:0}%");
            }
            // 3) 팔다리를 잃으면 정비실에서 의수·의족을 만들어 단다
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => x.Role != CrewRole.Technician);
                NeedsSystem.AddInjury(c.Vitals, 0.5f, "구조물에 짓눌림");
                var lost = c.Vitals.Wounds.FirstOrDefault(x => x.Lost);
                float hand0 = Wounds.HandFactor(c.Vitals), leg0 = Wounds.LegFactor(c.Vitals);
                foreach (var k in new[] { ItemKind.Electronics, ItemKind.Plate })
                    w.Ship.Containers.First(f => f.Storage!.Accepts(k) && f.Storage.Free >= 3).Storage!.Add(k, 3);
                Run(w, SimTime.Hours(30));
                for (int h = 0; h < 18 && lost != null && !lost.Prosthetic; h++) Run(w, SimTime.Hours(1)); // 통합8 정비실 일이 밀리는 날은 서른 시간을 조금 넘긴다 — 달 때까지 (이틀 안)
                Check("팔다리를 잃으면 의수·의족을 만들어 단다", lost != null && lost.Prosthetic,
                    $"{(lost == null ? "잃지 않음" : Wounds.PartName(lost.Part))} · {Wounds.Summary(c.Vitals)} · 손 {hand0 * 100:0}→{Wounds.HandFactor(c.Vitals) * 100:0}% · 걸음 {leg0 * 100:0}→{Wounds.LegFactor(c.Vitals) * 100:0}% · {w.Life.Stats}");
            }
            // 4) 실수: 피곤하면 틀린다 — 확률이 오르고, 왜 틀렸는지가 기록과 인과 사슬에 남는다
            {
                var w0 = DayOne(seed, "Hanbit");
                var c0 = w0.Crew.First(c => !c.Dead);
                var probe = new WorkOrder { Kind = WorkKind.Maintain, Skill = Skill.Mechanics };
                c0.Needs.Rest = 0.9f; c0.Needs.Stress = 0.2f;
                var (rested, _) = w0.Life.MistakeOdds(c0, probe);
                c0.Needs.Rest = 0.1f; c0.Needs.Stress = 0.8f;
                var (tired, why) = w0.Life.MistakeOdds(c0, probe);
                int mistakes = 0;
                string sample = "";
                for (int k = 0; k < 3; k++)
                {
                    var w = DayOne(seed + k, "Hanbit");
                    for (int h = 0; h < 96; h++)
                    {
                        foreach (var c in w.Crew.Where(c => !c.Dead)) { c.Needs.Rest = MathF.Min(c.Needs.Rest, 0.22f); c.Needs.Stress = MathF.Max(c.Needs.Stress, 0.7f); }
                        Run(w, SimTime.Hours(1));
                    }
                    mistakes += w.Life.Stats.Mistakes;
                    var node = w.Causes.Nodes.FirstOrDefault(n => n.Kind == CauseKind.Mistake);
                    if (node != null && sample.Length == 0) sample = node.Text;
                }
                Check("실수 — 지치고 예민하면 틀리기 쉽고, 까닭이 남는다", tired > rested * 4f && why.Contains("졸려서") && mistakes >= 1 && sample.Length > 0,
                    $"확률 {rested * 100:0.0}% → {tired * 100:0.0}% ({why}) · 나흘 × 세 배에 실수 {mistakes}번 · 예: {sample}");
            }
            // 5) 말다툼: 예민한 둘이 가치관이 부딪히면 다투고, 사교적인 사람이 달랜다
            {
                var w = DayOne(seed, "Hanbit");
                foreach (var c in w.Crew) c.Value = c.Id % 2 == 0 ? CrewValue.Safety : CrewValue.Efficiency;
                for (int h = 0; h < 48; h++)
                {
                    foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Stress = MathF.Max(c.Needs.Stress, 0.85f);
                    Run(w, SimTime.Hours(1));
                }
                var st = w.Life.Stats;
                var foes = w.Crew.SelectMany(a => w.Crew.Where(b => b.Id > a.Id && a.AffinityTo(b) < -0.1f)).Count();
                Check("말다툼 — 예민한 둘의 가치관이 부딪힌다", st.Arguments >= 2, $"{st} · 사이가 나빠진 쌍 {foes}");
            }
            // 6) 죽음 뒤: 시신을 안치실(없으면 창고)로 모시고, 다음 날 추모한다. 가까웠던 사람은 슬퍼한다
            {
                var w = DayOne(seed, "Mirinae");
                w.CrewCanDie = true;
                var dead = w.Crew.First(c => c.Role != CrewRole.Medic);
                var friend = w.Crew.First(c => c != dead);
                friend.ChangeAffinity(dead, 0.6f);
                dead.Vitals.Health = 0f;
                dead.Vitals.Oxygen = 0.1f;
                Run(w, SimTime.Hours(1));
                bool died = dead.Dead;
                bool grief = friend.GriefUntil > w.Tick;
                Run(w, SimTime.Hours(30));
                if (!dead.Laid && Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1")
                {
                    Console.WriteLine($"   [시신] {dead.Name} 방 {dead.Room?.Name} · 칸 {dead.Cell}");
                    foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.RecoverBody)) Console.WriteLine($"   [작업] {o.Title} {o.Urgency:0.00} 맡은 {o.Assignee?.Name} 보류 {o.BlockedReason}");
                    foreach (var e in w.Log.Entries.Where(e => e.Text.Contains("시신") || e.Text.Contains("모시")).TakeLast(10)) Console.WriteLine($"   [기록] {SimTime.Clock(e.Tick)} {e.Text}");
                    foreach (var c in w.Crew.Where(c => !c.Dead)) Console.WriteLine($"   {c.Name} {c.Room?.Name} {c.Job?.Label} · {c.LastEvaluations?.FirstOrDefault().Reason}");
                }
                var st = w.Life.Stats;
                Check("죽음 뒤 — 시신을 모시고, 추모하고, 가까웠던 사람이 슬퍼한다", died && dead.Laid && st.Funerals >= 1 && grief,
                    $"죽음 {(died ? "예" : "아니오")} · 모신 곳 {(dead.Laid ? dead.Room?.Name : "그대로")} · 추모 {st.Funerals} · {friend.Name} 슬픔 {(grief ? "예" : "아니오")} · 추모 명단 {string.Join(",", w.Life.Memorial.Select(m => m.name))}");
            }
            // 7) 문병: 다친 사람에게 가까운 사람이 찾아온다
            {
                var w = DayOne(seed, "Mirinae");
                var p = w.Crew[1];
                NeedsSystem.AddInjury(p.Vitals, 0.45f, "작업 중 사고");
                foreach (var o in w.Crew.Where(o => o != p)) o.ChangeAffinity(p, 0.4f);
                Run(w, SimTime.Hours(24));
                Check("문병 — 다친 사람 곁에 가까운 사람이 와 앉는다", w.Life.Stats.Visits + w.Relations.Stats.Comforts >= 1, $"문병 {w.Life.Stats.Visits} · 위로 {w.Relations.Stats.Comforts} · {p.Name} 일기: {string.Join(" / ", p.Diary.Select(d => d.text).TakeLast(2))}");
            }
            // 8) 자격: 솜씨가 오르면 자격을 딴다 · 자격이 없으면 그 일을 덜 맡는다
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => !x.Quals.Contains(Qual.Reactor));
                c.SkillLevels[(int)Skill.Engineering] = 0.65f;
                Run(w, SimTime.Hours(1));
                Check("자격 — 솜씨가 오르면 자격을 딴다", c.Quals.Contains(Qual.Reactor), $"{c.Name} 원자로 운전 자격 {(c.Quals.Contains(Qual.Reactor) ? "땄다" : "없음")} · 배 전체 자격 {w.Life.Stats.QualsEarned}");
            }
            // 9) 결정론: 실수·다툼·문병이 든 배도 같은 시드면 같은 역사
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    Run(w, SimTime.TicksPerDay * 3);
                    return SaveGame.StateHash(w);
                }
                uint a = H(), b = H();
                Check("결정론 — 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 승무원 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}

public static partial class Program
{
    private static int RunTreatDebug(int seed)
    {
        var w = DayOne(seed, "Mirinae");
        Run(w, SimTime.TicksPerDay * 20);
        var medic = w.Crew.First(c => c.Role == CrewRole.Medic);
        medic.Vitals.Health = 0f; w.CrewCanDie = true; Run(w, 30); w.CrewCanDie = false;
        var hurt = w.Crew.Where(c => !c.Dead).OrderBy(c => c.RawSkill(Skill.Medicine)).ThenBy(c => c.Id).Take(3).ToList();
        foreach (var c in hurt) Player.Hazard(w, HazardKind.WorkAccident, default, c.Id);
        long start = w.Tick;
        for (int h = 0; h < 10; h++)
        {
            Run(w, SimTime.Hours(1));
            var orders = w.Board.Open.Where(o => o.Kind is WorkKind.Treat).Select(o => $"{o.Target.Crew?.Name}/{o.Urgency:0.00}/{o.Assignee?.Name ?? "-"}/{(o.BlockedUntil > w.Tick ? o.BlockedReason ?? "미룸" : "")}");
            Console.WriteLine($"{h + 1}h 치료됨 {hurt.Count(c => c.Vitals.TreatedTick > start)} · 일감 {string.Join(" ; ", orders)} · 사람 {string.Join(", ", w.Crew.Where(c => !c.Dead).Select(c => $"{c.Name}:{c.Job?.Label ?? "-"}{(c.Quals.Contains(Qual.Medic) ? "*" : "")}"))}");
        }
        return 0;
    }
}

public static partial class Program
{
    private static int RunWatchDebug(int seed)
    {
        var w = Watchful(seed, "Mirinae", 0f);
        var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!;
        foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked);
        pump.Omen = new Omen { Kind = OmenKind.Vibration, Fault = FaultKind.BearingWear, Cause = OmenCause.BearingWear, Since = w.Tick, Due = w.Tick + SimTime.Hours(30) };
        for (int q = 0; q < 180; q++)
        {
            foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer))
                if (comp.Machine!.Efficiency > 0.25f) w.Machines.Break(comp.Machine!, FaultKind.Wrecked);
            Run(w, SimTime.Minutes(1));
        }
        Console.WriteLine($"3h 후 전조 {(pump.Omen == null ? "없음" : $"{pump.Omen.Kind} note={pump.Omen.Note?.How}")}");
        w.Watch.NoSensors = true;
        var mech = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
        mech.Familiarize(FurnitureType.CoolantPump, 0.8f);
        for (int h = 0; h < 24; h++)
        {
            if (mech.CanAct && mech.Room != pump.Body.Room && pump.Body.UseSpots.FirstOrDefault(s => w.Ship.IsOpenFloor(s)) is var spot && spot != default)
            { mech.Position = spot.Center; mech.PreviousPosition = mech.Position; }
            Run(w, SimTime.Minutes(15));
            if (h % 2 == 0) Console.WriteLine($"{h / 4f:0.0}h {mech.Name} 깸 {mech.IsAwake} 방 {mech.Room?.Name} 일 {mech.Job?.Label} · 전조 {(pump.Omen == null ? "없음" : $"lv{pump.Omen.Level(w.Tick):0.00} note={pump.Omen.Note?.How}")} · 일지 {string.Join(",", w.Watch.Notes.Where(x => x.Machine == pump).Select(x => x.How))} · blind {w.PreventionBlind}");
        }
        return 0;
    }
}
