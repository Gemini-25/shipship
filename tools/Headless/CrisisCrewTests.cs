using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.21 승무원 위기 행동: 공황 재조정 · 비상 배치표 · 비상 절차 훈련 · 한 작업에 여러 명 · 위기 우선순위
public static partial class Program
{
    /// <summary>한 판의 위기 측정값.</summary>
    private sealed class CrisisRun
    {
        public int PanicTicks, Panics, Deaths, PanicDeaths, Silent, AuxStarts, Samples, HandSum, HandJobs, HandMax, Snaps;
        public float AuxMinutes = -1f, AuxBattery = -1f;
    }

    /// <summary>위기 장면 하나를 돌리며 잰다: 공황 · 공황 중 사망 · 대응 없이 죽은 사람 · 보조 발전기 · 위급 작업에 붙은 인원.</summary>
    private static CrisisRun MeasureScene(int seed, string scene, float hours)
    {
        string ship = "Hanbit";
        if (scene.Contains('@')) { ship = scene[(scene.IndexOf('@') + 1)..]; scene = scene[..scene.IndexOf('@')]; }
        var w = DayOne(seed, ship);
        w.CrewCanDie = true;
        int panics0 = w.Minds.Panics;
        var r = new CrisisRun();
        switch (scene)
        {
            case "meteor":
                Player.Hazard(w, HazardKind.MeteorShower, default);
                break;
            case "blackout":
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.3f;
                break;
            case "fire":
                Scenarios.Apply(w, "combo", out _);
                break;
            case "bigmeteor":
                w.Hazards.FireStory("bigmeteor", null);
                break;
            case "storm":
                w.Hazards.FireStory("MeteorShower", null);
                break;
            case "chaos":
                Scenarios.Apply(w, "chaos", out _);
                break;
            case "harsh":
                // 새벽 두 시 · 잠든 침실에 큰 운석 · 유성우 · 냉각 펌프가 서서 원자로가 멎는다 · 발전기 방 데이터선이 끊겨 원격 기동이 안 된다
                while (Math.Abs(SimTime.HourOfDay(w.Tick) - 2f) > 0.05f) w.Step();
                Incidents.Meteor(w, Scenarios.OuterTarget(w, RoomType.Quarters), 1f);
                Player.Hazard(w, HazardKind.MeteorShower, default);
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.3f;
                CutAuxData(w);
                break;
            case "night":
                while (Math.Abs(SimTime.HourOfDay(w.Tick) - 23f) > 0.05f) w.Step();
                Player.Hazard(w, HazardKind.MeteorShower, default);
                break;
        }
        long t0 = w.Tick;
        var responded = new HashSet<int>();
        var lastPanic = new Dictionary<int, long>();
        var dead0 = w.Crew.Where(c => c.Dead).Select(c => c.Id).ToHashSet();
        bool aux = w.Power.AuxRunning;
        long end = w.Tick + SimTime.Hours(hours);
        var hands = new Dictionary<int, int>();
        bool trace = Environment.GetEnvironmentVariable("CR_TRACE") == $"{scene}:{seed}";
        int logAt = w.Log.Entries.Count;
        var lastDoing = new Dictionary<int, string>();
        while (w.Tick < end)
        {
            Run(w, 15);
            r.Samples++;
            if (trace)
            {
                var es = w.Log.Entries;
                int i0 = es.Count;
                while (i0 > 0 && es[i0 - 1].Tick > w.Tick - 15) i0--;
                for (int i = i0; i < es.Count; i++)
                {
                    var e = es[i];
                    if (e.Text.Contains("공황") || e.Text.Contains("숨졌") || e.Text.Contains("발전기") || e.Text.Contains("쓰러") || e.Text.Contains("정신"))
                        Console.WriteLine($"      {SimTime.Clock(e.Tick)} {w.Crew.FirstOrDefault(c => c.Id == e.CrewId)?.Name ?? "-"}: {e.Text}");
                }
                logAt = es.Count;
                if ((w.Tick - t0) % SimTime.Minutes(30) < 15)
                    Console.WriteLine($"    {SimTime.Clock(w.Tick)} 배터리 {w.Power.BatteryPercent * 100:0}% 흐름 {w.Power.BatteryFlow:0.0} 원자로 {(w.Power.ReactorOnline ? "켜짐" : "꺼짐")} 한도 {w.Power.ReactorLimit:0} 수요 {w.Power.Demand:0}/{w.Power.Delivered:0} 보조 {w.Power.AuxRunning} 위기 {Crisis.Name(Crisis.Level(w))} · 공황 {w.Crew.Count(c => c.Mind.Panicking(w.Tick))}");
                string? who = Environment.GetEnvironmentVariable("CR_WHO");
                foreach (var c in w.Crew)
                {
                    if (who != null && c.Name == who && !c.Dead && (w.Tick - t0) % SimTime.Minutes(2) < 15)
                        Console.WriteLine($"      · {SimTime.Clock(w.Tick)} {c.Room?.Name} {Doing(c, w)} 체력 {c.Vitals.Health:0.00} 산소 {c.Vitals.Oxygen:0.00} 압력 {c.Room?.Air.Pressure:0} 우주복 {(c.Suit != null ? $"{c.Suit.Oxygen:0.0}" : "-")}");
                    if (c.Dead && !dead0.Contains(c.Id)) Console.WriteLine($"      ✝ {c.Name} {c.Vitals.InjuryCause} · 마지막: {lastDoing.GetValueOrDefault(c.Id)}");
                    if (!c.Dead) lastDoing[c.Id] = $"{SimTime.Clock(w.Tick)} {c.Room?.Name} {Doing(c, w)} 체력 {c.Vitals.Health:0.00} 산소 {c.Vitals.Oxygen:0.00} 공황 {c.Mind.Panicking(w.Tick)}";
                }
            }
            hands.Clear();
            foreach (var c in w.Crew)
            {
                if (c.Dead)
                {
                    if (dead0.Add(c.Id))
                    {
                        r.Deaths++;
                        if (lastPanic.TryGetValue(c.Id, out long pt) && w.Tick - pt < SimTime.Minutes(4)) r.PanicDeaths++;
                        if (!responded.Contains(c.Id)) r.Silent++;
                    }
                    continue;
                }
                if (c.Mind.Panicking(w.Tick)) { lastPanic[c.Id] = w.Tick; r.PanicTicks += 15; }
                var job = c.Job;
                if (job != null && (job.Urgent || job.Activity is EvacuateActivity or TakeCoverActivity or RefillSuitActivity or ShelterActivity or MusterActivity
                                    || job.Order is { Urgency: >= 0.85f })) responded.Add(c.Id);
                if (CrisisHelpOrder(w, c) is int hid) { responded.Add(c.Id); if (job?.Current is AssistToil) hands[hid] = hands.GetValueOrDefault(hid) + 1; }
                else if (job?.Order is WorkOrder o && o.Urgency >= 0.85f && !o.Closed) hands[o.Id] = hands.GetValueOrDefault(o.Id) + 1;
            }
            foreach (var (_, n) in hands) { r.HandSum += n; r.HandJobs++; r.HandMax = Math.Max(r.HandMax, n); }
            if (w.Power.AuxRunning && !aux)
            {
                r.AuxStarts++;
                if (r.AuxMinutes < 0f) { r.AuxMinutes = (w.Tick - t0) * 60f / SimTime.TicksPerHour; r.AuxBattery = w.Power.BatteryPercent; }
            }
            aux = w.Power.AuxRunning;
        }
        r.Panics = w.Minds.Panics - panics0;
        r.Snaps = CrisisSnaps(w);
        return r;
    }

    /// <summary>보조 발전기 방의 데이터선을 끊는다 (주 컴퓨터가 원격으로 못 켠다).</summary>
    private static void CutAuxData(World w)
    {
        var aux = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault();
        if (aux == null) return;
        w.Net.EnsureBuilt();
        foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && (l.Room == aux.Room || l.Door != null && (l.Door.RoomA == aux.Room || l.Door.RoomB == aux.Room))).ToList())
            w.Net.Hurt(l, 1f, "합선");
        w.Net.Update(0f);
    }

    /// <summary>이 사람이 거드는 일 (없으면 null) — 예전 AI에는 없다.</summary>
    private static int? CrisisHelpOrder(World w, CrewMember c) => w.CrisisCrew.HelpingOrder(c) is int id && id >= 0 ? id : null;
    private static int CrisisSnaps(World w) => w.CrisisCrew.Snaps;

    /// <summary>장면 묶음을 여러 시드로 돌려 표 한 줄씩.</summary>
    private static List<(string scene, CrisisRun sum, int runs)> MeasureBundle(int[] seeds, string[] scenes, float hours)
    {
        var rows = new List<(string, CrisisRun, int)>();
        foreach (var s in scenes)
        {
            var sum = new CrisisRun();
            float auxMin = 0f; int auxN = 0; float auxBat = 0f;
            foreach (int seed in seeds)
            {
                var r = MeasureScene(seed, s, hours);
                sum.Panics += r.Panics; sum.PanicTicks += r.PanicTicks; sum.Deaths += r.Deaths; sum.PanicDeaths += r.PanicDeaths; sum.Silent += r.Silent; sum.AuxStarts += r.AuxStarts;
                sum.HandSum += r.HandSum; sum.HandJobs += r.HandJobs; sum.HandMax = Math.Max(sum.HandMax, r.HandMax); sum.Snaps += r.Snaps;
                if (r.AuxMinutes >= 0f) { auxMin += r.AuxMinutes; auxBat += r.AuxBattery; auxN++; }
                Console.WriteLine($"    {s,-8} 시드 {seed,3}: 공황 {r.Panics,2} ({r.PanicTicks * 60f / SimTime.TicksPerHour:0}분) · 사망 {r.Deaths} (공황 중 {r.PanicDeaths} · 대응 없이 {r.Silent}) · 발전기 {r.AuxStarts}회"
                                  + (r.AuxMinutes >= 0f ? $" {r.AuxMinutes:0}분 배터리 {r.AuxBattery * 100:0}%" : "") + $" · 위급 작업 평균 {(r.HandJobs > 0 ? r.HandSum / (float)r.HandJobs : 0f):0.00}명 최대 {r.HandMax} · 깨움 {r.Snaps}");
            }
            sum.AuxMinutes = auxN > 0 ? auxMin / auxN : -1f;
            sum.AuxBattery = auxN > 0 ? auxBat / auxN : -1f;
            sum.Samples = auxN;
            rows.Add((s, sum, seeds.Length));
        }
        return rows;
    }

    private static void PrintBundle(string title, List<(string scene, CrisisRun sum, int runs)> rows)
    {
        Console.WriteLine($"  [{title}]  장면 · 판당 공황 · 사망(공황 중 · 대응 없이) · 발전기 켠 판/걸린 분/배터리 · 위급 작업 평균 인원(최대)");
        foreach (var (s, x, n) in rows)
            Console.WriteLine($"    {s,-8} 공황 {x.Panics / (float)n:0.0}번 {x.PanicTicks * 60f / SimTime.TicksPerHour / n:0}분 · 사망 {x.Deaths}/{n}판 (공황 중 {x.PanicDeaths} · 대응 없이 {x.Silent}) · 발전기 {x.Samples}/{n}판"
                              + (x.AuxMinutes >= 0f ? $" {x.AuxMinutes:0}분 {x.AuxBattery * 100:0}%" : "") + $" · 인원 {(x.HandJobs > 0 ? x.HandSum / (float)x.HandJobs : 0f):0.00} (최대 {x.HandMax}) · 깨움 {x.Snaps}");
    }


    private static Cell CrFloor(World w, RoomType t) => w.Ship.RoomsOf(t).First().Cells.First(w.Ship.IsOpenFloor);

    private static void CrPut(World w, CrewMember c, Cell at)
    {
        c.EndJob(w, ToilStatus.Interrupted);
        c.Position = at.Center;
        c.PreviousPosition = at.Center;
        c.Room = w.Ship.RoomAt(at);
        c.Path = null;
        if (c.Pose == Pose.Sleeping) c.Pose = Pose.Standing;
    }

    private static bool CrResponding(CrewMember c, World w) =>
        c.Job is Job j && (j.Activity is StationActivity or HelpActivity or EvacuateActivity or TakeCoverActivity or TellActivity or CheckSmellActivity or FireBeliefActivity or OutageActivity or MusterActivity
                           || j.Urgent || j.Order is { Urgency: >= 0.8f });

    private static int RunCrisisCrewTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원 위기 행동 점검 (v16.21) · 시드 {seed}\n");
        bool off0 = CrisisCrewSystem.Off;
        float lv0 = Storyteller.LevelValue;
        try
        {
            string only = Environment.GetEnvironmentVariable("CR_ONLY") ?? "";
            if (only != "measure") CrisisCrewUnits(seed);
            if (only == "" || only == "measure") CrisisCrewMeasure(seed);
        }
        finally { CrisisCrewSystem.Off = off0; Storyteller.LevelValue = lv0; }
        return _fails;
    }

    /// <summary>장면 하나씩: 배치표 · 자리 · 빈 자리 · 보조 발전기 · 공황 · 몸이 먼저 · 여러 손 · 우선순위 · 결정론 · 성능.</summary>
    private static void CrisisCrewUnits(int seed)
    {
        string? pick = Environment.GetEnvironmentVariable("CR_UNIT");
        bool Do(string k) => pick == null || pick.Split(',').Contains(k);

        // ── 1) 비상 배치표: 첫 출항에 함장이 붙인다 · 자리마다 대신할 차례 · 사람이 빠지면 다시 짠다
        if (Do("bill"))
        {
            var w = DayOne(seed, "Hanbit");
            var cc = w.CrisisCrew;
            var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Away).ToList();
            var counts = adults.GroupBy(c => cc.BillRole(c)).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine($"  배치표 {cc.Bill.Version}판 · " + string.Join(" · ", counts.OrderBy(kv => kv.Key).Select(kv => $"{CrisisCrewSystem.RoleName(kv.Key)} {kv.Value}")));
            Check("배치표 · 첫 출항에 함장이 붙였다 (모두 자리가 있다)", cc.Bill.Drawn >= 0 && cc.Bill.By == w.Command.CaptainId && adults.All(c => cc.BillRole(c) != StationRole.None),
                $"그린 때 {cc.Bill.Drawn} · 함장 {cc.Bill.By}/{w.Command.CaptainId}");
            Check("배치표 · 소화 · 전력 · 의료 자리 + 대신할 차례", counts.ContainsKey(StationRole.Fire) && counts.ContainsKey(StationRole.Power) && counts.ContainsKey(StationRole.Medical)
                && cc.Bill.Order[StationRole.Power].Count > counts[StationRole.Power], $"전력 차례 {cc.Bill.Order[StationRole.Power].Count}명");
            Check("배치표 · 기록에 남는다", w.Log.Entries.Any(e => e.Text.Contains("비상 배치표를 붙였다")), "");
            var gone = adults.First(c => c.Id != w.Command.CaptainId);
            w.CrewCanDie = true;
            gone.Vitals.Health = 0f;
            int v0 = cc.Bill.Version;
            Run(w, SimTime.Hours(30));
            Check("배치표 · 사람이 빠지면 다시 짠다 (회의 또는 함장)", cc.Bill.Version > v0, $"{v0} → {cc.Bill.Version}판 · {cc.Bill.Why}");
        }

        // ── 2) 불 + 파공: 경보를 안 사람은 각자 제 자리로 · 소화 자리가 불을 끈다
        if (Do("fire"))
        {
            var w = DayOne(seed, "Hanbit");
            var cc = w.CrisisCrew;
            Scenarios.Apply(w, "combo", out _); // 창고에 큰 운석 + 주방 불 — 소화 · 격벽 자리가 함께 선다
            var seen = new HashSet<int>();
            var did = new HashSet<int>();
            var stationed = new HashSet<int>();
            int fireRoleOnFire = 0, unaware = 0;
            for (int i = 0; i < SimTime.Minutes(12) / 15; i++)
            {
                Run(w, 15);
                foreach (var (id, r) in cc.ActiveRoles)
                {
                    var c = w.Crew.First(x => x.Id == id);
                    seen.Add(id);
                    if (c.Job?.Activity is StationActivity or HelpActivity || c.Job?.Order is WorkOrder o && CrisisCrewSystem.RoleFor(o) == r) { did.Add(id); stationed.Add(id); }
                    else if (CrResponding(c, w) || c.Job?.Label == "알리러 감" || r == StationRole.Guide && !w.Crew.Any(x => x.Mind.Panicking(w.Tick))) did.Add(id); // 다른 급한 일 · 알리러 · 공황 난 사람이 없으면 대피 유도는 제자리
                    if (r == StationRole.Fire && (c.Job?.Order?.Kind == WorkKind.Extinguish || cc.HelpingOrder(c) is int ho && w.Board.All.Any(o => o.Id == ho && o.Kind == WorkKind.Extinguish))) fireRoleOnFire++;
                    if (c.Mind.Knows.Count == 0 && w.Scale.Felt(c) < IncidentScale.System) unaware++;
                    if (Environment.GetEnvironmentVariable("CR_DBG") == "fire" && i % 4 == 0) Console.WriteLine($"    {SimTime.Clock(w.Tick)} {c.Name} {CrisisCrewSystem.RoleName(r)}: {Doing(c, w)} · {c.Room?.Name}");
                }
                if (Environment.GetEnvironmentVariable("CR_DBG") == "fire" && i % 4 == 0)
                {
                    foreach (var o in w.Board.All.Where(o => !o.Closed && o.Kind == WorkKind.Extinguish))
                        Console.WriteLine($"      불끄기 {o.Title} {o.Urgency:0.00} · {o.Assignee?.Name ?? "-"} ({(o.Assignee is CrewMember a ? CrisisCrewSystem.RoleName(cc.BillRole(a)) : "")}) · 상한 {w.Board.MaxHands(o)} · 돕는 {cc.HelpersOf(o).Count}");
                }
            }
            Console.WriteLine($"  불 + 파공: 자리에 선 사람 {seen.Count} · 대응 {did.Count} (제 자리 · 제 몫의 일 {stationed.Count}) · 소집 {cc.Musters}");
            Check("불 + 파공 · 경보 → 각자 제 자리 (자리에 선 사람 대부분이 대응)", seen.Count >= 3 && did.Count >= seen.Count * 0.6f && stationed.Count >= 2, $"{did.Count}/{seen.Count} · 제 자리 {stationed.Count}");
            Check("불 · 소화 자리 사람이 불을 끄거나 거든다", fireRoleOnFire > 0, $"표본 {fireRoleOnFire}");
            Check("불 · 모르는 사람은 자리로 가지 않는다", unaware == 0, $"모르는 채 자리 {unaware}");
        }

        // ── 3) 정전 예측 · 원격 기동 불가: 사람이 바닥나기 전에 발전기를 손으로 켠다 (예전과 비교)
        if (Do("aux"))
        {
            (float bat, bool byHand, int calls, bool spoke, long at) AuxScene(bool off)
            {
                CrisisCrewSystem.Off = off;
                var w = DayOne(seed, "Hanbit");
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.45f;
                CutAuxData(w);
                long t0 = w.Tick;
                float bat = -1f;
                while (w.Tick - t0 < SimTime.Hours(5) && !w.Power.AuxRunning) Run(w, 15);
                if (w.Power.AuxRunning) bat = w.Power.BatteryPercent;
                bool hand = w.Log.Entries.Any(e => e.Tick >= t0 && e.Text.Contains("시동 손잡이를 당겼다"));
                bool spoke = w.Log.Entries.Any(e => e.Tick >= t0 && e.Text.Contains("보조 발전기로"));
                int calls = w.CrisisCrew.AuxCalls;
                CrisisCrewSystem.Off = false;
                return (bat, hand, calls, spoke, w.Tick - t0);
            }
            var before = AuxScene(true);
            var after = AuxScene(false);
            string Say((float bat, bool byHand, int calls, bool spoke, long at) x) => x.bat >= 0 ? $"배터리 {x.bat * 100:0}%에서 {x.at * 60f / SimTime.TicksPerHour:0}분" : "안 켰다";
            Console.WriteLine($"  보조 발전기 (원격 불가): 예전 {Say(before)} · 지금 {Say(after)} · 부름 {after.calls}");
            Check("보조 발전기 · 정전 예측에 사람이 미리 손으로 켠다", after.bat >= 0.15f && after.byHand, $"배터리 {after.bat * 100:0}% · 손 {after.byHand}");
            Check("보조 발전기 · 주 컴퓨터가 사람을 보낸다 (원격 불가)", after.calls > 0 && after.spoke, $"부름 {after.calls}");
            Check("보조 발전기 · 예전보다 일찍", before.bat < 0f || after.at < before.at, $"예전 {before.at} · 지금 {after.at}틱");
        }

        // ── 4) 빈 자리: 전력 자리 사람이 쓰러지면 다음 차례가 맡는다
        if (Do("fill"))
        {
            var w = DayOne(seed, "Hanbit");
            var cc = w.CrisisCrew;
            foreach (var c in w.Crew.Where(c => cc.BillRole(c) == StationRole.Power).ToList()) c.Vitals.Health = 0.05f;
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
            w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.4f;
            CutAuxData(w);
            CrewMember? stand = null;
            for (int i = 0; i < SimTime.Minutes(40) / 15 && stand == null; i++)
            {
                Run(w, 15);
                foreach (var (id, r) in cc.ActiveRoles)
                    if (r == StationRole.Power && cc.BillRole(w.Crew.First(x => x.Id == id)) != StationRole.Power) stand = w.Crew.First(x => x.Id == id);
            }
            Check("빈 자리 · 전력 자리가 비면 다음 차례가 맡는다", stand != null && cc.Fills > 0, stand != null ? $"{stand.Name} ({CrisisCrewSystem.RoleName(cc.BillRole(stand))} → 전력)" : "없음");
        }

        // ── 5) 공황: 짧고 · 대피 유도 자리 동료가 와서 깨운다 · 깨면 다시 대응
        if (Do("panic"))
        {
            var w = DayOne(seed, "Hanbit");
            var cc = w.CrisisCrew;
            Incidents.Fire(w, CrFloor(w, RoomType.Galley));
            // 통합: 장면 동안 위기가 이어지게 — 불 한 칸은 부엌에 있던 사람이 3분 안에 꺼 버리기도 한다 (공황이 끝날 까닭이 사고가 끝나서가 되지 않게)
            foreach (var x in w.Ship.RoomsOf(RoomType.Galley).First().Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => x.Y).ThenBy(x => x.X).Take(3)) w.Fire.Ignite(x, 0.8f);
            Run(w, SimTime.Minutes(3));
            var p = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct && cc.Active(c) != StationRole.Fire && c.Room?.Type != RoomType.Galley)
                .OrderBy(c => c.Traits.Calm).First();
            p.Mind.PanicUntil = w.Tick + SimTime.Minutes(12);
            p.Mind.Frozen = true;
            p.EndJob(w, ToilStatus.Interrupted);
            Run(w, 30); // 공황에 빠진 사람이 생기면 대피 유도 자리가 선다
            p.Mind.Frozen = true;
            var guide = w.Crew.FirstOrDefault(c => cc.Active(c) == StationRole.Guide);
            if (guide?.Room is Room gr && gr.Type != RoomType.Galley && guide != p)
                CrPut(w, p, gr.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => MathF.Abs((x.Center - guide.Position).Length() - 3.5f)).First());
            long t0 = w.Tick;
            int snaps0 = cc.Snaps;
            long woke = -1, back = -1;
            while (w.Tick - t0 < SimTime.Minutes(30))
            {
                Run(w, 15);
                if (woke < 0 && !p.Mind.Panicking(w.Tick)) woke = w.Tick - t0;
                if (woke >= 0 && back < 0 && (CrResponding(p, w) || !Crisis.Acting(w))) back = w.Tick - t0; // 사고가 끝났으면 할 일이 없다
                if (back >= 0) break;
            }
            float wm = woke * 60f / SimTime.TicksPerHour, bm = back * 60f / SimTime.TicksPerHour;
            Console.WriteLine($"  공황: {p.Name} (침착 {p.Traits.Calm:0.00}) · 대피 유도 {guide?.Name ?? "-"} · 깬 때 {wm:0.0}분 · 다시 대응 {bm:0.0}분 · 깨움 {cc.Snaps - snaps0}");
            Check("공황 · 동료가 와서 깨운다 (12분 공황이 일찍 끝난다)", woke >= 0 && woke < SimTime.Minutes(11) && cc.Snaps > snaps0, $"{wm:0.0}분 · 깨움 {cc.Snaps - snaps0}");
            Check("공황 · 깨면 다시 대응한다", back >= 0, $"{bm:0.0}분 · {Doing(p, w)}");
            var lens = new List<float>();
            for (int i = 0; i < 40; i++) lens.Add(cc.PanicMinutes(p, i % 3 == 0, 3f));
            Check("공황 · 길이는 수십 초 ~ 몇 분", lens.Average() < 2f && lens.Max() <= 3f, $"평균 {lens.Average():0.00}분 · 최대 {lens.Max():0.00}");
            Check("공황 · 훈련 · 제 자리 · 겪어 본 규모가 공황을 줄인다", cc.PanicMul(p) < 1f, $"배율 {cc.PanicMul(p):0.00}");
        }

        // ── 6) 공황 중에도 몸이 먼저: 얼어붙은 사람의 방에 구멍이 나면 빠져나간다
        if (Do("body"))
        {
            (bool left, bool alive, int firsts) BodyScene(bool off)
            {
                CrisisCrewSystem.Off = off;
                var w = DayOne(seed, "Hanbit");
                // 통합: 새 배의 첫 침실은 안쪽 방일 수 있다 — 운석이 뚫을 수 있는 외벽 침실에서 (외벽 없는 방은 뚫리지 않아 몸이 먼저일 까닭이 없었다)
                var room = w.Ship.RoomsOf(RoomType.Quarters).OrderBy(r => w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r) ? 0 : 1).ThenBy(r => r.Id).First();
                var p = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Traits.Bravery).First();
                CrPut(w, p, room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - room.Center).LengthSquared()).First());
                p.Mind.PanicUntil = w.Tick + SimTime.Minutes(8);
                p.Mind.Frozen = true;
                Incidents.Meteor(w, Scenarios.OuterTarget(w, room), 1f);
                // 통합: 새 배의 외판은 운석 하나로 잘 안 뚫린다 (보강 · 장갑) — 장면은 "뚫린 방"이니 그 방 외벽을 확실히 뚫는다
                if (!room.Leaking && w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).OrderBy(kv => (kv.Key.Center - p.Position).LengthSquared()).Select(kv => kv.Key).Cast<Cell?>().FirstOrDefault() is Cell hole)
                    Hull.Damage(w.Ship, hole, 2f);
                long t0 = w.Tick;
                bool left = false;
                while (w.Tick - t0 < SimTime.Minutes(6)) { Run(w, 15); if (p.Room != room) left = true; }
                Run(w, SimTime.Minutes(30));
                int f = w.CrisisCrew.BodyFirsts;
                CrisisCrewSystem.Off = false;
                return (left, !p.Dead, f);
            }
            var b0 = BodyScene(true);
            var b1 = BodyScene(false);
            Console.WriteLine($"  몸이 먼저: 예전 {(b0.left ? "빠져나옴" : "얼어붙은 채")} · {(b0.alive ? "살았다" : "죽었다")} / 지금 {(b1.left ? "빠져나옴" : "얼어붙은 채")} · {(b1.alive ? "살았다" : "죽었다")} · 몸이 먼저 {b1.firsts}");
            Check("몸이 먼저 · 공황 중에도 뚫린 방에서 빠져나간다", b1.left && b1.alive && b1.firsts > 0, $"빠져나옴 {b1.left} · 생존 {b1.alive}");
            var w2 = DayOne(seed, "Hanbit");
            var room2 = w2.Ship.RoomsOf(RoomType.Quarters).First();
            var q = w2.Crew.First(c => !c.Dead && !c.IsChild && c.CanAct);
            CrPut(w2, q, room2.Cells.First(w2.Ship.IsOpenFloor));
            room2.Air.O2 = 4f; room2.Air.N2 = 14f;
            Check("몸이 먼저 · 숨 막히는 자리에서는 얼어붙지 않는다", !w2.CrisisCrew.MayFreeze(q), "");
        }

        // ── 7) 한 작업에 여러 명: 큰 일에 셋이 붙어 혼자보다 빨리
        if (Do("hands"))
        {
            (long ticks, int max, int joins) HandsScene(bool off)
            {
                CrisisCrewSystem.Off = off;
                var w = DayOne(seed, "Hanbit");
                var f = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First();
                // 부품은 창고에 있다 (부품을 구하러 다니는 시간이 아니라 손을 잰다)
                w.Ship.Containers.First(x => x.Room.Type == RoomType.Storage && x.Storage!.Free > 0).Storage!.Add(ItemKind.PowerController, 1);
                w.Machines.Break(f.Machine!, FaultKind.ElectrolyzerFault);
                long t0 = w.Tick;
                int max = 0;
                while (w.Tick - t0 < SimTime.Hours(12) /* v16.26 혼자서도 끝날 때까지 잰다 (4시간이면 혼자는 못 끝낸 채 견줬다) */ && f.Machine!.Faults.Count > 0)
                {
                    Run(w, 15);
                    if (Environment.GetEnvironmentVariable("CR_DBG") == "hands" && (w.Tick - t0) % SimTime.Minutes(10) < 15)
                        foreach (var o in w.Board.All.Where(o => !o.Closed && o.Target.Furniture == f))
                            Console.WriteLine($"    {SimTime.Clock(w.Tick)} {o.Title} 긴급 {o.Urgency:0.00} · {o.Assignee?.Name ?? "-"} {(o.Assignee is CrewMember a0 ? $"{a0.Pose} {a0.Job?.Current?.GetType().Name}" : "")} · 상한 {w.Board.MaxHands(o)} · 돕는 {w.CrisisCrew.HelpersOf(o).Count} · 위기 {Crisis.Acting(w)}");
                    foreach (var o in w.Board.All)
                        if (!o.Closed && o.Target.Furniture == f && o.Assignee is CrewMember lead && lead.Pose == Pose.Working)
                            max = Math.Max(max, 1 + w.CrisisCrew.HelpersOf(o).Count(h => h.Job?.Current is AssistToil));
                }
                int joins = w.CrisisCrew.Joins;
                CrisisCrewSystem.Off = false;
                return (f.Machine!.Faults.Count == 0 ? w.Tick - t0 : long.MaxValue, max, joins);
            }
            var h0 = HandsScene(true);
            var h1 = HandsScene(false);
            string M(long t) => t == long.MaxValue ? "12시간 안에 못 끝냄" : $"{t * 60f / SimTime.TicksPerHour:0}분";
            Console.WriteLine($"  큰 수리 (산소 발생기): 혼자 {M(h0.ticks)} · 여럿 {M(h1.ticks)} (최대 {h1.max}명 · 거들기 {h1.joins})");
            Check("여러 손 · 큰 수리에 셋이 붙는다", h1.max >= 3, $"최대 {h1.max}명");
            Check("여러 손 · 혼자보다 빨리 끝난다", h0.ticks != long.MaxValue && h1.ticks < h0.ticks * 0.85f, $"혼자 {M(h0.ticks)} → 여럿 {M(h1.ticks)}");
            var w = DayOne(seed, "Hanbit");
            var o2 = new WorkOrder { Kind = WorkKind.ClearRubble, Target = WorkTarget.AtCell(CrFloor(w, RoomType.Storage), w.Ship.RoomsOf(RoomType.Storage).First()), Skill = Skill.Mechanics, Urgency = 1f };
            Check("여러 손 · 일감마다 인원 상한 (잔해 여럿 · 치료는 혼자)", w.Board.MaxHands(o2) >= 3 && w.Board.MaxHands(new WorkOrder { Kind = WorkKind.Treat, Target = o2.Target }) == 1,
                $"잔해 {w.Board.MaxHands(o2)}");
        }

        // ── 8) 위기 우선순위: 생명 > 산소/압력 > 불 > 전력 > 나머지 · 제 자리가 있으면 잠을 미룬다
        if (Do("tier"))
        {
            var w = DayOne(seed, "Hanbit");
            var t = WorkTarget.AtCell(CrFloor(w, RoomType.Storage), w.Ship.RoomsOf(RoomType.Storage).First());
            int T(WorkKind k, float u = 1f) => CrisisCrewSystem.Tier(w, new WorkOrder { Kind = k, Target = t, Urgency = u });
            Check("우선순위 · 생명 < 산소 < 불 < 전력 < 나머지", T(WorkKind.Rescue) == 0 && T(WorkKind.SealO2Line) == 1 && T(WorkKind.Extinguish) == 2 && T(WorkKind.StartAux) == 3 && T(WorkKind.CleanUp, 0.7f) == 4,
                $"{T(WorkKind.Rescue)} {T(WorkKind.SealO2Line)} {T(WorkKind.Extinguish)} {T(WorkKind.StartAux)} {T(WorkKind.CleanUp, 0.7f)}");
            Incidents.Fire(w, CrFloor(w, RoomType.Galley));
            Run(w, SimTime.Minutes(3));
            var c = w.Crew.FirstOrDefault(x => w.CrisisCrew.Active(x) != StationRole.None);
            float s = 1f; string why = "";
            if (c != null) { c.Needs.Rest = 0.2f; w.CrisisCrew.Damp(c, new SleepActivity(), ref s, ref why); }
            Check("우선순위 · 제 자리가 있으면 잠을 미룬다 (몹시 지치지 않았으면)", c != null && s < 0.7f, $"{c?.Name} · 잠 배율 {s:0.00}");
        }

        // ── 9) 결정론
        if (Do("det"))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 · 같은 시드 같은 지문", a == b, $"{a:X8} / {b:X8}");
        }

        // ── 10) 성능: 30인 배 하루
        if (Do("perf"))
        {
            double Day(bool off)
            {
                CrisisCrewSystem.Off = off;
                var w = World.CreateDefault(seed, 0, "Cheonma");
                Run(w, SimTime.Hours(2));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                CrisisCrewSystem.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false)); // 다른 일이 CPU를 나눠 써서 두 번 중 빠른 쪽
            Console.WriteLine($"  성능 (30인 하루): 예전 {t0:0.0}초 · 지금 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%)");
            Check("성능 · 30인 배 하루가 크게 늘지 않는다", t1 < t0 * 1.15, $"{t0:0.0} → {t1:0.0}초");
        }
    }

    /// <summary>전 · 후 측정표: 보통 재해(유성우 · 정전 · 불 · 작은 배 유성우) + 거친 밤(새벽 운석 · 원격 기동 불가) 여러 시드.</summary>
    private static void CrisisCrewMeasure(int seed)
    {
        var seeds = Environment.GetEnvironmentVariable("CR_SEEDS") is string ss ? ss.Split(",").Select(int.Parse).ToArray() : new[] { seed, seed + 4, seed + 13, seed + 22 };
        var normal = Environment.GetEnvironmentVariable("CR_SCENES")?.Split(",") ?? new[] { "meteor", "blackout", "fire", "storm@Mirinae" };
        var harsh = new[] { "harsh", "harsh@Mirinae" };
        if (Environment.GetEnvironmentVariable("CR_LEVEL") is string lv) Storyteller.LevelValue = float.Parse(lv);
        Console.WriteLine($"\n  측정 · 시드 {string.Join(",", seeds)} · 난이도 {Storyteller.LevelName(Storyteller.Level)} · 장면마다 6시간");
        CrisisCrewSystem.Off = true;
        var b0 = MeasureBundle(seeds, normal, 6f);
        var b1 = MeasureBundle(seeds, harsh, 6f);
        CrisisCrewSystem.Off = false;
        var a0 = MeasureBundle(seeds, normal, 6f);
        var a1 = MeasureBundle(seeds, harsh, 6f);
        PrintBundle("전 · 예전 승무원", b0.Concat(b1).ToList());
        PrintBundle("후", a0.Concat(a1).ToList());
        CrisisRun Sum(IEnumerable<(string scene, CrisisRun sum, int runs)> rows)
        {
            var s = new CrisisRun();
            foreach (var (_, x, _) in rows)
            {
                s.Panics += x.Panics; s.PanicTicks += x.PanicTicks; s.Deaths += x.Deaths; s.PanicDeaths += x.PanicDeaths; s.Silent += x.Silent;
                s.HandSum += x.HandSum; s.HandJobs += x.HandJobs; s.HandMax = Math.Max(s.HandMax, x.HandMax); s.Snaps += x.Snaps; s.Samples += x.Samples;
            }
            return s;
        }
        var B = Sum(b0.Concat(b1));
        var A = Sum(a0.Concat(a1));
        var An = Sum(a0);
        int runs = seeds.Length * (normal.Length + harsh.Length), nRuns = seeds.Length * normal.Length;
        float pmB = B.PanicTicks * 60f / SimTime.TicksPerHour / runs, pmA = A.PanicTicks * 60f / SimTime.TicksPerHour / runs;
        float hB = B.HandJobs > 0 ? B.HandSum / (float)B.HandJobs : 0f, hA = A.HandJobs > 0 ? A.HandSum / (float)A.HandJobs : 0f;
        float auxB = b1.Where(r => r.sum.AuxMinutes >= 0).Select(r => r.sum.AuxMinutes).DefaultIfEmpty(999f).Average();
        float auxA = a1.Where(r => r.sum.AuxMinutes >= 0).Select(r => r.sum.AuxMinutes).DefaultIfEmpty(999f).Average();
        float batB = b1.Where(r => r.sum.AuxBattery >= 0).Select(r => r.sum.AuxBattery).DefaultIfEmpty(0f).Average();
        float batA = a1.Where(r => r.sum.AuxBattery >= 0).Select(r => r.sum.AuxBattery).DefaultIfEmpty(0f).Average();
        Console.WriteLine($"\n  [전 → 후]  판당 공황 {B.Panics / (float)runs:0.0} → {A.Panics / (float)runs:0.0}번 · 공황 시간 {pmB:0.0} → {pmA:0.0}분 · 사망 {B.Deaths} → {A.Deaths} (공황 중 {B.PanicDeaths} → {A.PanicDeaths} · 대응 없이 {B.Silent} → {A.Silent})"
                          + $" · 거친 밤 발전기 {auxB:0}분 {batB * 100:0}% → {auxA:0}분 {batA * 100:0}% · 위급 작업 인원 {hB:0.00} → {hA:0.00} (최대 {B.HandMax} → {A.HandMax}) · 깨움 {B.Snaps} → {A.Snaps}");
        Check("측정 · 공황 시간이 줄었다", pmA < pmB * 0.6f, $"{pmB:0.0} → {pmA:0.0}분");
        Check("측정 · 공황 중 · 대응 없이 죽는 사람이 늘지 않는다", A.PanicDeaths <= B.PanicDeaths && A.Silent <= B.Silent, $"공황 중 {A.PanicDeaths} · 대응 없이 {A.Silent}");
        Check("측정 · 원격 불가 정전에 발전기를 일찍 켠다", auxA < auxB && batA > batB, $"{auxB:0} → {auxA:0}분");
        Check("측정 · 위급 작업에 여럿이 붙는다", hA > hB && A.HandMax >= 3, $"{hB:0.00} → {hA:0.00}");
        Check("측정 · 보통 재해 사망은 가끔 (판당 0.35 이하)", An.Deaths / (float)nRuns <= 0.35f, $"{An.Deaths}/{nRuns}판");
        Check("측정 · 완벽하지 않다 (공황은 여전히 온다)", A.Panics > 0, $"공황 {A.Panics}번");
    }
}
