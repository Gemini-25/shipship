using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15.7 로봇·드론 8 → 25 · 이야기꾼 4 → 8: 새 종류가 들이면 맡은 일을 하고, 새 이야기꾼은 저마다 다른 사고 흐름을 만든다
public static partial class Program
{
    private static int RunRobotV15Test(int seed)
    {
        _fails = 0;
        float p0 = Storyteller.PersonaValue, l0 = Storyteller.LevelValue;
        Console.WriteLine($"로봇·드론 25 · 이야기꾼 8 점검 (v15.7) · 시드 {seed}\n");
        try
        {
            // 0) 목록
            {
                int robots = Enum.GetValues<RobotKind>().Length, drones = Enum.GetValues<DroneKind>().Length;
                int personas = Enum.GetValues<StoryPersona>().Count(p => p != StoryPersona.Off);
                var specs = RobotsV15.Bots.Select(b => $"{string.Join(",", b.Jobs)}|{b.Speed}|{b.Drain}|{b.Fault}|{b.Work}|{b.Fight}|{b.Patrol}|{b.Assist}")
                    .Concat(RobotsV15.Flyers.Select(d => $"{string.Join(",", d.Jobs)}|{d.Speed}|{d.Drain}|{d.Fault}|{d.Work}|{d.Tow}|{d.PatrolRooms}")).ToList();
                bool named = RobotsV15.Bots.All(b => RobotSystem.KindName(b.Kind) != b.Kind.ToString() && b.Base <= RobotKind.Safety && b.Note.Length > 0)
                             && RobotsV15.Flyers.All(d => DroneSystem.KindName(d.Kind) != d.Kind.ToString() && d.Base <= DroneKind.Build && d.Note.Length > 0);
                bool covered = Enum.GetValues<RobotKind>().All(k => k <= RobotKind.Safety || RobotsV15.Bot(k) != null)
                               && Enum.GetValues<DroneKind>().All(k => k <= DroneKind.Build || RobotsV15.Flyer(k) != null);
                bool tuned = Tuning.Entries.Any(e => e.Key == "story.persona" && e.Max >= (int)StoryPersona.Vengeful)
                             && Enum.GetValues<StoryPersona>().All(p => p == StoryPersona.Off || Storyteller.PersonaName(p) != "끔");
                Check("목록 — 로봇·드론 25 (새 17은 원형을 쓰고 특기가 모두 다르다) · 이야기꾼 8", robots + drones == 25 && RobotsV15.KindCount == 25
                      && RobotsV15.Bots.Length + RobotsV15.Flyers.Length == 17 && named && covered && specs.Distinct().Count() == specs.Count && personas == 8 && tuned,
                    $"로봇 {robots} · 드론 {drones} · 새 {RobotsV15.Bots.Length}+{RobotsV15.Flyers.Length} · 특기 겹침 {specs.Count - specs.Distinct().Count()} · 이야기꾼 {personas}" +
                    (named ? "" : " · 이름·원형 빠짐") + (covered ? "" : " · 표에 없는 종류") + (tuned ? "" : " · 설정 범위 모자람"));
            }

            // 1) 개조로 들인다: 겪은 일이 후보를 부르고, 개조를 마치면 충전대·거치대와 함께 들어온다
            {
                var w = DayOne(seed, "Hanbit");
                w.Fixtures.LightFailures = 6;                                     // 조명이 여섯 번 나갔다 → 배선 로봇
                var plan = Evolution.Candidates(w).FirstOrDefault(p => p.Kind == UpgradeKind.Robot);
                int docks0 = w.Ship.FurnitureOf(FurnitureType.RobotDock).Count();
                bool robotIn = plan != null && plan.Circuit == RobotsV15.Code(RobotKind.Lineman) && V15Refit(w, plan);
                var lineman = w.Robots.Robots.FirstOrDefault(r => r.Kind == RobotKind.Lineman);
                bool again = !RobotsV15.Candidates(w).Any(p => p.Circuit == RobotsV15.Code(RobotKind.Lineman)); // 종류마다 한 대
                w.Fixtures.LightFailures = 0;
                foreach (var hull in w.Ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).Take(6))
                    w.History.ImpactCells.Add(hull);                              // 운석이 여섯 번 → 정찰 드론
                var plan2 = Evolution.Candidates(w).FirstOrDefault(p => p.Kind == UpgradeKind.Robot);
                bool droneIn = plan2 != null && plan2.Circuit == RobotsV15.Code(DroneKind.Scout) && V15Refit(w, plan2);
                var scout = w.Drones.Drones.FirstOrDefault(d => d.Kind == DroneKind.Scout);
                bool logged = w.History.Events.Count(e => e.Kind == HistoryKind.Upgrade && e.Text.Contains("짜 들였다")) >= 2;
                Check("개조로 들인다 — 조명 고장 → 배선 로봇(새 충전대) · 운석 → 정찰 드론(거치대) · 역사에 남는다", robotIn && droneIn && lineman != null && scout != null && again && logged
                      && w.Ship.FurnitureOf(FurnitureType.RobotDock).Count() == docks0 + 1 && RobotSystem.DockWorking(lineman.Dock),
                    $"후보 {plan?.Why ?? "없음"} / {plan2?.Why ?? "없음"} · 충전대 {docks0} → {w.Ship.FurnitureOf(FurnitureType.RobotDock).Count()} ({lineman?.Dock.Room.Name ?? "?"}{(lineman != null && RobotSystem.DockWorking(lineman.Dock) ? " · 전기 들어옴" : " · 전기 없음")})" +
                    $" · 드론 {scout?.Dock.Label ?? "없음"} 자리 {scout?.Slot} · 다시 후보 {(again ? "안 됨" : "됨")}");
            }

            // 2) 새 로봇: 원래 로봇을 끄고 하나씩 들여 일을 만들어 준다
            var works = new List<(string name, bool ok, string detail)>();
            {
                var w = DayOne(seed, "Hanbit");
                DisableRobots(w);
                var kinds = new[] { RobotKind.Courier, RobotKind.Tanker, RobotKind.Stocker, RobotKind.Lineman, RobotKind.Overhauler, RobotKind.Harvester, RobotKind.Tender, RobotKind.Sentry };
                var bots = V15Install(w, kinds);
                V15Demand(w);
                int patrols0 = w.Robots.Patrols;
                var seen = V15Watch(w, bots, SimTime.Hours(14), out var patrolled, out _);
                foreach (var r in bots) works.Add(V15Did(r, seen[r.Id], patrolled.Contains(r.Id)));
            }
            {
                var w = DayOne(seed, "Hanbit");
                DisableRobots(w);
                var bots = V15Install(w, new[] { RobotKind.Utility, RobotKind.Assistant, RobotKind.Firefighter });
                V15Demand(w);
                // 고장 둘 (사람이 긴 수리를 한다 → 조수 로봇이 거든다)
                foreach (var m in w.Ship.Machines.Where(m => !m.Body.Room.Detached && m.Faults.Count == 0 && m.Spec.Skill == Skill.Mechanics && !m.Spec.Critical).OrderBy(m => m.Body.Id).Take(2))
                    w.Machines.Break(m);
                // 한 시간 뒤 불 (소방 로봇)
                Run(w, SimTime.Hours(1));
                var ff = bots.First(r => r.Kind == RobotKind.Firefighter);
                var fireRoom = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Workshop or RoomType.Galley && !r.Detached)
                    .OrderBy(r => (r.Center - ff.DockPosition).LengthSquared()).First();
                var spot = fireRoom.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - fireRoom.Center).LengthSquared()).First();
                Incidents.Fire(w, spot);
                var seen = V15Watch(w, bots, SimTime.Hours(13), out _, out var sprayed);
                foreach (var r in bots) works.Add(V15Did(r, seen[r.Id], false, sprayed.Contains(r.Id)));
            }

            // 3) 새 드론: 원래 드론은 잃은 것으로 두고 (견인 원형 하나는 떠내려가게) 하나씩 내보낸다
            foreach (var group in new[] { new[] { DroneKind.Scout, DroneKind.Welder, DroneKind.Radiator, DroneKind.Tug }, new[] { DroneKind.Surveyor, DroneKind.Rigger } })
            {
                var w = DayOne(seed, "Hanbit");
                var olds = w.Drones.Drones.ToList();
                foreach (var d in olds) d.State = DroneState.Lost;
                var news = new List<Drone>();
                foreach (var k in group)
                    if (RobotsV15.Install(w, RobotsV15.Code(k), null, out _)) news.Add(w.Drones.Drones[^1]);
                // 외벽 연결부를 오래 못 봤다 (검사) · 연결부 하나가 상했다 (용접·골조) · 방열판이 상했다 (방열판)
                foreach (var j in w.Structure.Joints) j.SeenAt = w.Tick - SimTime.Hours(120);
                var joint = w.Structure.Joints.Where(j => !j.Released && !j.Room.Detached && j.Room.Type != RoomType.Corridor).OrderBy(j => j.Room.Id).ThenBy(j => j.Index).First();
                joint.Strength = 0.35f; joint.Known = 0.35f;
                var rad = w.Piping.Segments.FirstOrDefault(s => s.Radiator.Count > 0);
                if (rad != null && group.Contains(DroneKind.Radiator)) rad.RadiatorCondition = 0.4f;
                Drone? adrift = null;
                if (group.Contains(DroneKind.Tug))
                {
                    adrift = olds.First(d => d.Kind == DroneKind.Tow);
                    var hatch = DroneSystem.Hatch(w)!.Cell;
                    var space = Enumerable.Range(-8, 17).SelectMany(dx => Enumerable.Range(-8, 17).Select(dy => new Cell(hatch.X + dx, hatch.Y + dy)))
                        .Where(c => w.Ship.Grid.Kind(c) == TileKind.Void && (c.Center - hatch.Center).Length() >= 3f).OrderBy(c => (c.Center - hatch.Center).LengthSquared()).First();
                    adrift.Position = space.Center; adrift.PreviousPosition = space.Center;
                    adrift.State = DroneState.Adrift;
                    adrift.DriftVelocity = new System.Numerics.Vector2(0.05f, 0f);
                }
                int insp0 = w.Drones.Inspections;
                var kinds = news.ToDictionary(d => d.Id, _ => new HashSet<WorkKind>());
                for (long t = 0; t < SimTime.Hours(12); t++)
                {
                    w.Step();
                    foreach (var d in news) if (d.Order != null) kinds[d.Id].Add(d.Order.Kind);
                }
                foreach (var d in news)
                {
                    var row = RobotsV15.Flyer(d.Kind)!;
                    bool own = kinds[d.Id].All(k => row.Jobs.Contains(k));
                    bool ok = d.Kind switch
                    {
                        DroneKind.Scout or DroneKind.Surveyor => d.Sorties >= 1 && w.Drones.Inspections > insp0,
                        DroneKind.Welder or DroneKind.Rigger => d.Sorties >= 1 && kinds[d.Id].Contains(WorkKind.RepairJoint) && joint.Strength >= 0.7f,
                        DroneKind.Radiator => rad != null && d.Sorties >= 1 && rad.RadiatorCondition >= 0.7f,
                        DroneKind.Tug => adrift != null && adrift.State == DroneState.Docked && d.Sorties >= 1,
                        _ => false,
                    } && own;
                    works.Add((d.Name, ok, $"출격 {d.Sorties} · 맡은 일 {string.Join("·", kinds[d.Id].Select(WorkKinds.Name))}" +
                        (d.Kind is DroneKind.Scout or DroneKind.Surveyor ? $" · 검사 {w.Drones.Inspections - insp0}" : "") +
                        (d.Kind is DroneKind.Welder or DroneKind.Rigger ? $" · 연결부 {joint.Strength * 100:0}%" : "") +
                        (d.Kind == DroneKind.Radiator ? $" · 방열판 {(rad != null ? rad.RadiatorCondition * 100 : 0):0}%" : "") +
                        (d.Kind == DroneKind.Tug ? $" · 건질 드론 {adrift?.State}" : "") + (own ? "" : " · 남의 일을 맡았다")));
                }
            }
            int okCount = works.Count(x => x.ok);
            Check($"새 종류 {works.Count} 중 {works.Count - 2} 이상이 들이면 맡은 일을 한다", works.Count == 17 && okCount >= works.Count - 2,
                $"{okCount}/{works.Count} — " + string.Join(" / ", works.Select(x => $"{(x.ok ? "" : "✘")}{x.name}: {x.detail}")));

            // 4) 새 이야기꾼 넷: 같은 시드에서 서로 다른 사고 흐름 · 저마다의 규칙이 보인다
            {
                var flows = new Dictionary<StoryPersona, (List<(long tick, string key, float tension)> picks, List<int> seasons, string journal)>();
                foreach (var persona in new[] { StoryPersona.SlowBurn, StoryPersona.Seasonal, StoryPersona.Merciful, StoryPersona.Vengeful })
                {
                    Storyteller.PersonaValue = (int)persona; Storyteller.LevelValue = 4;
                    var w = DayOne(seed, "Mirinae");
                    var seasons = new List<int>();
                    int fired = 0;
                    for (long t = 0; t < SimTime.TicksPerDay * 5; t++)
                    {
                        w.Step();
                        if (w.Story.Fired != fired) { fired = w.Story.Fired; seasons.Add(w.Story.Season); }
                    }
                    var s = w.Story;
                    flows[persona] = (s.Picks.ToList(), seasons, string.Join(" / ", s.Journal.Take(4).Select(j => $"{SimTime.Day(j.tick)}일 {SimTime.Clock(j.tick)} {j.what}")));
                }
                string Flow(StoryPersona p) => string.Join(",", flows[p].picks.Select(x => $"{x.tick / SimTime.Minutes(10)}:{x.key}"));
                var keys = flows.Keys.ToList();
                bool distinct = keys.All(a => keys.All(b => a == b || Flow(a) != Flow(b)));
                bool fires = keys.All(p => flows[p].picks.Count >= 2);
                Check("이야기꾼 — 느린 불씨 · 계절 · 자비 · 앙갚음이 같은 시드에서 서로 다른 흐름", distinct && fires,
                    string.Join("  ‖  ", keys.Select(p => $"{Storyteller.PersonaName(p)} {flows[p].picks.Count}건: {flows[p].journal}")));

                // 흐름에서 보이는 규칙: 불씨는 작은 것부터 · 계절은 철의 갈래 · 자비는 다 추스른 뒤 작은 것만 · 앙갚음은 더 잦다
                var slow = flows[StoryPersona.SlowBurn].picks;
                var sea = flows[StoryPersona.Seasonal];
                int inSeason = sea.picks.Zip(sea.seasons, (p, s) => Storyteller.Family(p.key) == s).Count(x => x);
                var mer = flows[StoryPersona.Merciful].picks;
                var ven = flows[StoryPersona.Vengeful].picks;
                bool flowOk = slow.Count >= 1 && !Storyteller.Big(slow[0].key) && inSeason * 10 >= sea.picks.Count * 6
                              && mer.All(p => p.tension <= 0.05f && !Storyteller.Big(p.key)) && ven.Count > mer.Count;

                // 고르는 규칙: 같은 배 · 같은 순간에서 성격마다 400번씩 골라 본다
                Storyteller.LevelValue = 4;
                var ws = DayOne(seed, "Mirinae");
                var st = ws.Story;
                float cap = st.Capacity();
                const int N = 400;
                (float big, float[] fam, int aimed) Dist(StoryPersona p)
                {
                    int big = 0, aimed = 0;
                    var fam = new float[4];
                    for (int i = 0; i < N; i++)
                    {
                        var (k, room) = st.Sample(p, 0f, cap);
                        if (Storyteller.Big(k)) big++;
                        fam[Storyteller.Family(k)] += 1f / N;
                        if (room != null) aimed++;
                    }
                    return (big / (float)N, fam, aimed);
                }
                st.Fired = 0; st.LastKey = "fire";
                var target = ws.Ship.LiveRooms.Where(r => r.Type == RoomType.Galley).OrderBy(r => r.Id).First();
                ws.History.FiresByRoom[target.Id] = 2; // 불을 두 번 버텨 낸 주방
                ws.History.Add(ws, HistoryKind.Upgrade, "불을 두 번 넘긴 주방을 고쳐 지었다", target); // 통합8 이레 사이 다른 방 개조가 끼면 앙갚음이 그 방(함교)을 노려 불 몫이 반에 걸쳤다 — 버텨 낸 주방이 가장 최근에 고친 방
                var steady = Dist(StoryPersona.Steady);
                var cold = Dist(StoryPersona.SlowBurn);
                st.Fired = 8;
                var hot = Dist(StoryPersona.SlowBurn);
                var season = Dist(StoryPersona.Seasonal);
                var mercy = Dist(StoryPersona.Merciful);
                var venge = Dist(StoryPersona.Vengeful);
                int now = st.Season;
                bool slowOk = cold.big < 0.04f && hot.big > cold.big + 0.05f && hot.fam[Storyteller.FamBurn] > cold.fam[Storyteller.FamBurn] + 0.05f;
                bool seaOk = season.fam[now] >= 0.6f && season.fam[now] > steady.fam[now] + 0.25f;
                bool merOk = mercy.big < 0.04f && mercy.big * 4f < steady.big;
                bool venOk = venge.fam[Storyteller.FamBurn] >= 0.5f && venge.fam[Storyteller.FamBurn] > steady.fam[Storyteller.FamBurn] + 0.2f && venge.aimed == N && steady.aimed == 0
                             && st.Grudge() is Room aim && (aim == target || ws.History.Events.Any(e => e.Kind == HistoryKind.Upgrade && e.RoomId == aim.Id));
                string F(float[] f) => string.Join("/", f.Select(x => $"{x * 100:0}"));
                Check("이야기꾼 — 성격마다 고르는 규칙이 다르다 (불씨: 작게 시작해 불·큰 것으로 · 계절: 철의 갈래 · 자비: 다 추스른 뒤 작은 것만 · 앙갚음: 같은 갈래로 버텨 낸 방을)",
                    slowOk && seaOk && merOk && venOk && flowOk,
                    $"갈래(바깥/고장/불/생물 %) 꾸준 {F(steady.fam)} 큰 것 {steady.big * 100:0}%" +
                    $" · 불씨 처음 큰 것 {cold.big * 100:0}% 불 {cold.fam[Storyteller.FamBurn] * 100:0}% → 달아올라 큰 것 {hot.big * 100:0}% 불 {hot.fam[Storyteller.FamBurn] * 100:0}%{(slowOk ? "" : " ✘")}" +
                    $" · 계절({Storyteller.SeasonName(now)}) {F(season.fam)}{(seaOk ? "" : " ✘")} · 자비 큰 것 {mercy.big * 100:0}%{(merOk ? "" : " ✘")}" +
                    $" · 앙갚음(불 뒤) {F(venge.fam)} 노린 방 {venge.aimed}/{N} {st.Grudge()?.Name}{(venOk ? "" : " ✘")}" +
                    $" · 흐름: 불씨 첫 {(slow.Count > 0 ? slow[0].key : "-")} · 계절 철맞음 {inSeason}/{sea.picks.Count} · 자비 최대 긴장 {(mer.Count > 0 ? mer.Max(p => p.tension) : 0):0.00} · 앙갚음 {ven.Count}건 ↔ 자비 {mer.Count}건{(flowOk ? "" : " ✘")}");
                Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0;
            }

            // 5) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Storyteller.PersonaValue = (int)StoryPersona.Vengeful; Storyteller.LevelValue = 4;
                uint H2()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    foreach (var k in new[] { RobotKind.Courier, RobotKind.Sentry, RobotKind.Utility }) RobotsV15.Install(w, RobotsV15.Code(k), null, out _);
                    foreach (var k in new[] { DroneKind.Scout, DroneKind.Rigger }) RobotsV15.Install(w, RobotsV15.Code(k), null, out _);
                    Run(w, SimTime.TicksPerDay * 2 + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                uint a = H2(), b = H2();
                Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0;
                Check("결정론 — 같은 시드 같은 지문 (새 로봇·드론 다섯 · 앙갚음형 이야기꾼이 든 배도)", x == y && a == b, $"{x:x8} / {y:x8} · {a:x8} / {b:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        finally { Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0; }
        Console.WriteLine(_fails == 0 ? "\n✔ 로봇·드론 25 · 이야기꾼 8 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>개조안을 마친 것처럼 적용한다 (개조 일을 끝낸 사람이 Evolution.Apply를 부르는 그 길).</summary>
    private static bool V15Refit(World w, UpgradePlan plan)
    {
        var o = new WorkOrder { Kind = WorkKind.Upgrade, Target = plan.Target, Circuit = plan.Circuit, Upgrade = plan.Kind, Skill = plan.Skill };
        var cm = w.Crew.First(c => !c.Dead);
        return Evolution.Apply(w, o, cm);
    }

    private static List<Robot> V15Install(World w, RobotKind[] kinds)
    {
        var list = new List<Robot>();
        foreach (var k in kinds)
            if (RobotsV15.Install(w, RobotsV15.Code(k), null, out _)) list.Add(w.Robots.Robots[^1]);
        return list;
    }

    /// <summary>새 로봇이 맡을 일을 만든다: 배식기 비우기 · 추진제 · 드론 자재 · 조명 · 마모 · 익은 작물 · 시든 작물.</summary>
    private static void V15Demand(World w)
    {
        var ship = w.Ship;
        foreach (var d in ship.FurnitureOf(FurnitureType.MealDispenser)) d.Storage!.Take(ItemKind.Meal, 999);
        w.Propulsion.Propellant = w.Propulsion.Capacity * 0.3f;
        foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock)) { dock.Storage!.Take(ItemKind.Structure, 99); dock.Storage.Take(ItemKind.Plate, 99); }
        foreach (var room in ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess or RoomType.Storage).Take(2)) room.LightsOut = true;
        foreach (var m in ship.Machines.Where(m => m.Faults.Count == 0 && m.Body.Type != FurnitureType.ReactorCore && !m.Body.Room.Detached && m.Spec.ServiceHours > 0f)
                     .OrderBy(m => m.Body.Id).Where((m, i) => i % 3 == 0).Take(8))
            m.Wear = 0.75f;
        var beds = ship.FurnitureOf(FurnitureType.GrowBed).Where(b => b.Machine?.Crop != null).OrderBy(b => b.Id).ToList();
        for (int i = 0; i < beds.Count; i++)
        {
            var crop = beds[i].Machine!.Crop!;
            if (i % 2 == 0) crop.Growth = 1f; else { crop.Care = 0.3f; crop.Growth = MathF.Min(crop.Growth, 0.5f); }
        }
    }

    /// <summary>로봇이 맡은 일 종류를 지켜본다 (순찰 · 거품 뿌리기도).</summary>
    private static Dictionary<int, HashSet<WorkKind>> V15Watch(World w, List<Robot> bots, long ticks, out HashSet<int> patrolled, out HashSet<int> sprayed)
    {
        var seen = bots.ToDictionary(r => r.Id, _ => new HashSet<WorkKind>());
        patrolled = new HashSet<int>();
        sprayed = new HashSet<int>();
        for (long t = 0; t < ticks; t++)
        {
            w.Step();
            foreach (var r in bots)
            {
                if (r.Order != null) seen[r.Id].Add(r.Order.Kind);
                if (r.Doing.StartsWith("순찰")) patrolled.Add(r.Id);
                if (r.Foam < 0.999f) sprayed.Add(r.Id);
            }
        }
        return seen;
    }

    private static (string name, bool ok, string detail) V15Did(Robot r, HashSet<WorkKind> seen, bool patrolled, bool sprayed = false)
    {
        var row = RobotsV15.Bot(r.Kind)!;
        bool own = seen.All(k => row.Jobs.Contains(k));
        bool ok = r.Kind switch
        {
            RobotKind.Sentry => patrolled,
            RobotKind.Assistant => r.AssistHours > 0.05f,
            RobotKind.Firefighter => sprayed,
            _ => r.JobsDone >= 1 && seen.Count > 0,
        } && own;
        return (r.Name, ok, $"한 일 {r.JobsDone}" + (seen.Count > 0 ? $" ({string.Join("·", seen.Select(WorkKinds.Name))})" : "") +
                            (r.AssistHours > 0 ? $" · 거든 {r.AssistHours:0.0}시간" : "") + (patrolled ? " · 순찰" : "") + (sprayed ? $" · 거품 {r.Foam * 100:0}%" : "") +
                            $" · 배터리 {r.Battery * 100:0}% · 고장 {r.Breakdowns}" + (own ? "" : " · 남의 일을 맡았다"));
    }
}
