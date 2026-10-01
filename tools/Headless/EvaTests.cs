using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v16.11 선외 작업의 위험: 그늘에 숨기 · 우주복 패치/사망 · 표류자 구조/실종 · 무전 들은 사람만 · 드론 추진기 표류 → 회수 ·
// 배터리 폭발 → 옆 드론 · 잔해 부품 · 주 컴퓨터 원격 측정(산소 · 생명줄 · 운석 도착 시간) · 선외 공포 · 결정론
public static partial class Program
{
    /// <summary>선체 밖 손잡이 칸 (선체에 붙은 우주 칸) 중 해치에서 먼 것부터.</summary>
    private static List<Cell> HullSpots(World w, int minFromHatch)
    {
        var outer = EvaRiskSystem.HatchOuter(w)!.Value;
        var grid = w.Ship.Grid;
        var list = new List<Cell>();
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            var c = new Cell(x, y);
            if (!w.Paths.IsSpace(c) || grid.Kind(c) != TileKind.Void) continue;
            bool hug = Cell.Dirs4.Any(d => grid.InBounds(c + d) && grid.Kind(c + d) != TileKind.Void);
            if (!hug) continue;
            if ((c.Center - outer.Center).Length() < minFromHatch) continue;
            list.Add(c);
        }
        return list.OrderByDescending(c => (c.Center - outer.Center).LengthSquared()).ThenBy(c => c.X).ThenBy(c => c.Y).ToList();
    }

    /// <summary>사람을 선체 밖 칸에 세운다 (우주복 · 생명줄 — 에어락으로 나간 것처럼).</summary>
    private static EvaPerson PutOutside(World w, CrewMember c, Cell at, float oxygenHours = 3f)
    {
        if (c.Job != null) c.EndJob(w, ToilStatus.Interrupted);
        c.Down = false;
        c.Suit ??= new SuitState();
        c.Suit.Oxygen = oxygenHours;
        c.Position = at.Center;
        c.PreviousPosition = at.Center;
        c.Pose = Pose.Standing;
        c.EvaMode = true;
        c.Room = null;
        c.Outside = true;
        c.NextThinkTick = w.Tick + 1;
        w.EvaRisk.Step();
        return w.EvaRisk.Of(c)!;
    }

    private static CrewMember Adult(World w, int skip = 0) => w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).Skip(skip).First();

    private static int RunEvaTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"선외 작업의 위험 (v16.11) · 시드 {seed}\n");

        // ── 1) 운석 경보: 에어락이 늦으면 선체 그늘에 숨는다 (주 컴퓨터가 운석 도착 시간을 읽고 "그늘로") — 숨으면 덜 맞는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var c = Adult(w);
            Cell? spot = null, entry = null;
            foreach (var s in HullSpots(w, 12))
            {
                PutOutside(w, c, s);
                foreach (var (wc, wall) in w.Ship.Walls)
                {
                    if (!wall.IsHull) continue;
                    float d = (wc.Center - s.Center).Length();
                    if (d < 2.5f || d > 4.2f) continue;
                    if (w.EvaRisk.FindShelter(c, wc.Center) is not Cell sh || sh == s || w.EvaRisk.LineBlocked(wc.Center, s.Center)) continue;
                    spot = s; entry = wc; break;
                }
                if (spot != null) break;
            }
            Check("그늘 시험 자리 (선체 밖 · 운석 들어올 곳 · 가려진 칸)", spot != null && entry != null);
            if (spot is Cell sp && entry is Cell en)
            {
                var p = PutOutside(w, c, sp);
                float hp0 = c.Vitals.Health;
                float old = SensorSystem.ApproachMinutes;
                SensorSystem.ApproachMinutes = 4f;
                var inc = w.Sensors.Launch(en, 0.8f)!;
                SensorSystem.ApproachMinutes = old;
                bool sheltered = false, advised = false, atShade = false;
                for (int t = 0; t < SimTime.Minutes(4) && w.Sensors.Incoming.Contains(inc); t++)
                {
                    w.Step();
                    if (p.Plan == EvaPlan.Shelter) sheltered = true;
                    if (p.AdviceComputer && p.AdvicePlan == EvaPlan.Shelter) advised = true;
                    atShade = p.ShelterAt is Cell sc && c.Cell == sc;
                    if (Environment.GetEnvironmentVariable("EVADBG") == "1" && t % 10 == 0)
                        Console.WriteLine($"    t{t} 경보 {inc.Warned} {w.Sensors.Alarm?.Id} 판단 {p.Plan}/{p.PlanFor} 밖 {c.Outside} 칸 {c.Cell} 일 {c.Job?.Label} 남은 {inc.MinutesLeft(w.Tick):0.0} 사람 {w.EvaRisk.People.Count}");
                }
                Run(w, 30);
                Check("경보 → 에어락이 늦다 → 선체 그늘에 숨는다", sheltered && atShade, $"판단 {EvaRiskSystem.PlanName(p.Plan)} · 그늘 {p.ShelterAt} · 지금 {c.Cell} · 에어락까지 {w.EvaRisk.EtaMinutes(c):0.0}분");
                Check("주 컴퓨터: 운석 도착 시간 · 에어락 거리를 읽고 무전으로 \"그늘로\"", advised && w.EvaRisk.Radio.Any(r => r.Tone == RadioTone.Computer && r.Text.Contains("그늘")) && w.Automation.Book.Acts.Any(a => a.Key.StartsWith("eva:")),
                    w.EvaRisk.Radio.LastOrDefault(r => r.Tone == RadioTone.Computer)?.Text ?? "컴퓨터 무전 없음");
                float lossShade = hp0 - c.Vitals.Health;
                // 같은 자리 · 같은 운석인데 경보 없이 (숨지 못했다)
                var w2 = DayOne(seed, "Hanbit");
                var c2 = Adult(w2);
                PutOutside(w2, c2, sp);
                float hp2 = c2.Vitals.Health;
                Incidents.Meteor(w2, en, 0.8f);
                float lossOpen = hp2 - c2.Vitals.Health;
                Check("그늘에 숨으면 덜 맞는다 (같은 운석 · 같은 자리)", lossShade < lossOpen && w2.EvaRisk.Stats.Hits >= 1, $"숨음 {lossShade * 100:0}% · 안 숨음 {lossOpen * 100:0}% (맞음 {w2.EvaRisk.Stats.Hits})");
            }
        }

        // ── 2) 우주복: 미세 누출은 응급 패치로 버틴다 / 큰 파공에 패치가 없고 에어락이 멀면 죽는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            w.CrewCanDie = true;
            var a = Adult(w);
            a.Traits.Calm = 0.9f;
            a.SkillLevels[(int)Skill.Mechanics] = 0.9f;
            var spots = HullSpots(w, 14);
            var pa = PutOutside(w, a, spots[0]);
            a.Suit!.Wear.Breach = SuitBreach.MicroLeak;
            a.Suit.Wear.BreachPart = EvaPart.LeftArm;
            a.Suit.Wear.Known = true;
            Run(w, SimTime.Minutes(12));
            Check("미세 누출 → 응급 패치로 막는다", a.Suit?.Wear.Patched == true && !a.Dead, $"패치 {w.EvaRisk.Stats.Patches} · 실패 {w.EvaRisk.Stats.PatchFails} · 산소 {a.Suit?.Oxygen:0.00}시간 · {a.Suit?.Wear.Describe()}");
            Run(w, SimTime.Minutes(50));
            Check("패치한 사람은 살아서 돌아온다", !a.Dead && (!a.Outside || a.Suit?.Oxygen > 0.3f), $"밖 {a.Outside} · 체력 {a.Vitals.Health:0.00}");

            var b = Adult(w, 1);
            var far = spots.First(s => (s.Center - a.Position).Length() > 6f);
            PutOutside(w, b, far);
            b.Suit!.Wear.Breach = SuitBreach.Puncture;
            b.Suit.Wear.BreachPart = EvaPart.Torso;
            b.Suit.Wear.PatchKit = 0;
            b.Suit.Wear.Known = true;
            foreach (var o in w.Crew) if (o != b) w.EvaRisk.AddDread(o, 1f); // 아무도 구하러 나오지 못한다
            Run(w, SimTime.Minutes(45));
            Check("큰 파공 · 패치 없음 · 에어락이 멀다 → 죽는다", b.Dead, $"죽음 {b.Dead} · 사인 {b.Vitals.InjuryCause} · 밖 {b.Outside} · 산소 {b.Suit?.Oxygen:0.00}");
            Check("선외 사망이 기록된다", w.EvaRisk.Stats.Deaths >= 1 && w.History.Events.Any(e => e.Text.Contains("선체 밖에서 숨졌다")));
        }

        // ── 3) 생명줄이 끊겨 떠내려간 사람을 동료가 구조 EVA로 건져 온다 (견인 드론이 없을 때) ──
        {
            var w = DayOne(seed, "Hanbit");
            foreach (var d in w.Drones.Drones) d.Faulty = true; // 드론이 없다
            var v = Adult(w);
            var spots = HullSpots(w, 6);
            var p = PutOutside(w, v, spots[^1]);
            var aff0 = w.Crew.ToDictionary(x => x.Id, x => v.AffinityTo(x));
            v.Suit!.Wear.Fuel = 0f; // 추진팩이 비었다 — 스스로 못 돌아온다
            var away = v.Position - w.Structure.ShipCenter;
            w.EvaRisk.StartDrift(v, p, Vector2.Normalize(away) * 10f, 220f, "시험: 생명줄 끊김");
            Check("생명줄이 끊기면 표류한다 · 주 컴퓨터가 장력으로 바로 안다 (방송 · 기록)", p.Adrift && w.Automation.Book.Acts.Any(a => a.Key == $"eva:drift:{v.Id}") && p.KnownBy.Count > 0,
                $"아는 사람 {p.KnownBy.Count} · 지목 {p.AssignedId}");
            for (int t = 0; t < SimTime.Hours(3) && p.Adrift; t += 25) Run(w, 25);
            var rescuer = w.Crew.FirstOrDefault(x => x.Id == p.SavedBy);
            Run(w, SimTime.Minutes(30));
            Check("동료가 구조 EVA로 건져 온다", !p.Adrift && !v.Dead && w.EvaRisk.Stats.CrewRescues >= 1, $"표류 {p.Adrift} · 구조 {w.EvaRisk.Stats.CrewRescues} · 구한 사람 {rescuer?.Name} · 거리 {(v.Position - w.Structure.ShipCenter).Length():0}");
            Check("구해 준 사람과의 관계 · 기억 · 일기", rescuer != null && v.AffinityTo(rescuer) - aff0[rescuer.Id] >= 0.25f && v.Memory.Marks.Any(m => m.Text.Contains("붙잡아 왔다")) && v.Diary.Any(d => d.text.Contains("장갑")),
                $"호감 {(rescuer != null ? aff0[rescuer.Id] : 0):0.00} → {(rescuer != null ? v.AffinityTo(rescuer) : 0):0.00} · 기억 {v.Memory.Marks.LastOrDefault().Text} · 일기 {v.Diary.LastOrDefault().text}");
            Check("두 줄 관행이 생겼다 → 다음 EVA는 생명줄 두 줄", w.EvaRisk.TwoLines != null && w.EvaRisk.TwoLines.Followers.Contains(v.Id));
            var nx = Adult(w, 2);
            w.EvaRisk.TwoLines!.Followers.Add(nx.Id);
            var pn = PutOutside(w, nx, spots[0]);
            Check("두 줄 관행을 따르는 사람은 두 줄을 건다", pn.Tethers == 2, $"줄 {pn.Tethers}");
        }

        // ── 4) 아무도 못 오면: 무전이 점점 약해지다 끊긴다 → 실종 ──
        {
            var w = DayOne(seed, "Hanbit");
            foreach (var d in w.Drones.Drones) d.Faulty = true;
            var v = Adult(w);
            foreach (var o in w.Crew) if (o != v) w.EvaRisk.AddDread(o, 1f);
            var p = PutOutside(w, v, HullSpots(w, 6)[0]);
            v.Suit!.Wear.Fuel = 0f;
            var away = Vector2.Normalize(v.Position - w.Structure.ShipCenter);
            w.EvaRisk.StartDrift(v, p, away * 70f, 300f, "시험: 큰 파편");
            Run(w, SimTime.Minutes(5));
            float s1 = p.Signal;
            Run(w, SimTime.Minutes(30));
            float s2 = p.Signal;
            for (int t = 0; t < SimTime.Hours(2) && !p.Missing; t += 100) Run(w, 100);
            Check("멀어질수록 무전이 약해진다", s2 < s1, $"{s1:0.00} → {s2:0.00}");
            Check("무전이 끊기면 실종", p.Missing && w.EvaRisk.Stats.Missing >= 1, $"신호 {p.Signal:0.00} · 거리 {(v.Position - w.Structure.ShipCenter).Length():0}칸");
            Check("약한 무전은 지직거린다", EvaRiskSystem.Garble("메이데이 표류 중 해치에서 서른 칸", 0.1f, 3).Contains('·'));
        }

        // ── 5) 무전은 들은 사람만 안다 (함교 콘솔 O · 침실 X) ──
        {
            var w = DayOne(seed, "Hanbit");
            var x = Adult(w);
            PutOutside(w, x, HullSpots(w, 4)[0]);
            var bridge = w.Ship.RoomsOf(RoomType.Bridge).First();
            var quarters = w.Ship.Rooms.First(r => r.Type == RoomType.Quarters && !r.Detached);
            var cmd = w.Command.Active ? w.Command.Commander : null;
            var near = w.Crew.Where(o => o != x && !o.Dead && !o.IsChild && o != cmd).OrderBy(o => o.Id).ToList();
            var a = near[0];
            var b = near[1];
            if (a.Job != null) a.EndJob(w, ToilStatus.Interrupted);
            if (b.Job != null) b.EndJob(w, ToilStatus.Interrupted);
            a.Room = bridge; a.Position = bridge.Center; a.Pose = Pose.Standing; a.Down = false;
            b.Room = quarters; b.Position = quarters.Center; b.Pose = Pose.Standing; b.Down = false;
            var call = w.EvaRisk.Say(x, "시험 무전 — 들리나", RadioTone.Chat, x.Id, x.Position, x.Name);
            Check("무전: 함교 콘솔 곁은 듣고 침실은 못 듣는다", call.Heard.Contains(a.Id) && !call.Heard.Contains(b.Id), $"들은 사람 {string.Join(",", call.Heard)} · 함교 {a.Id} · 침실 {b.Id}");
        }

        // ── 6) 드론: 추진기를 맞으면 빙글빙글 떠내려간다 → 견인 드론이 건져 온다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var tow = w.Drones.Drones.FirstOrDefault(d => RobotsV15.Base(d.Kind) == DroneKind.Tow);
            var hurt = w.Drones.Drones.FirstOrDefault(d => d != tow && d.State == DroneState.Docked);
            Check("시험 드론 (견인 + 다른 한 대)", tow != null && hurt != null);
            if (tow != null && hurt != null)
            {
                var spot = HullSpots(w, 4)[0];
                hurt.State = DroneState.Working;
                hurt.Position = spot.Center + new Vector2(0.6f, 0f);
                for (int i = 0; i < 80 && !hurt.Hurt.Tumbling; i++) { hurt.Condition = 1f; w.Drones.HitDrone(hurt, 0.3f, spot.Center + new Vector2(-2f, 0f), "시험 파편"); }
                Check("추진기 → 빙글빙글 표류", hurt.Hurt.Tumbling && hurt.State == DroneState.Adrift && MathF.Abs(hurt.Hurt.SpinRate) > 100f, $"상태 {hurt.State} · 추진기 {hurt.Hurt.Thruster:0.00} · 회전 {hurt.Hurt.SpinRate:0}");
                tow.Battery = 1f;
                for (int t = 0; t < SimTime.Hours(4) && hurt.State != DroneState.Docked; t += 50) Run(w, 50);
                Check("견인 드론이 빙글빙글 떠내려가던 드론을 건져 온다", hurt.State == DroneState.Docked && !hurt.Hurt.Tumbling, $"상태 {hurt.State} · {hurt.Doing} · 견인 {tow.State} {tow.Doing}");
                Check("부위별 정비 비용 (추진기 → 모터)", DroneSystem.ServiceCost(hurt).Any(x => x.kind == ItemKind.Motor) || hurt.Hurt.Thruster >= 0.95f);
            }
        }

        // ── 7) 배터리 폭발 → 옆 드론이 파편을 맞는다 · 잔해가 떠다니다 → 견인 드론이 건져 부품 · 고철로 ──
        {
            var w = DayOne(seed, "Hanbit");
            var ds = w.Drones.Drones.Where(d => RobotsV15.Base(d.Kind) != DroneKind.Tow && d.State == DroneState.Docked).Take(2).ToList();
            var tow = w.Drones.Drones.FirstOrDefault(d => RobotsV15.Base(d.Kind) == DroneKind.Tow);
            Check("폭발 시험 드론 두 대", ds.Count == 2);
            if (ds.Count == 2)
            {
                var spot = HullSpots(w, 5)[0];
                var a = ds[0];
                var b = ds[1];
                a.State = DroneState.Working; a.Position = spot.Center + new Vector2(0f, 0f); a.Battery = 0.8f;
                b.State = DroneState.Working; b.Position = spot.Center + new Vector2(1.1f, 0.4f); b.Battery = 0.8f;
                a.Hurt.Battery = 0.2f;
                a.Hurt.Swell = 0.995f;
                float bCond = b.Condition;
                int stockBefore = 0;
                foreach (var k in new[] { ItemKind.Plate, ItemKind.Motor, ItemKind.Electronics, ItemKind.MetalOre, ItemKind.CellPack })
                    stockBefore += w.Ship.CountStored(k) + w.Drones.Drones.Select(d => d.Dock).Distinct().Sum(f => f.Storage?.Count(k) ?? 0);
                Run(w, World.SystemInterval * 2);
                Check("배터리가 부풀다 터진다", a.Hurt.ExplodedAt >= 0 && a.State == DroneState.Lost && w.Drones.Explosions >= 1, $"부풂 {a.Hurt.Swell:0.00} · 상태 {a.State}");
                Check("폭발 파편이 옆 드론을 맞힌다", b.Condition < bCond && b.Hurt.HitAt >= 0, $"옆 드론 상태 {bCond:0.00} → {b.Condition:0.00} · 맞은 부위 {b.Hurt.HitPart}");
                int debris = w.EvaRisk.Floaters.Count(f => f.FromDrone == a.Id);
                Check("잔해가 떠다닌다", debris >= 3, $"잔해 {debris}");
                Check("이름 붙은 드론을 잃으면 기록 · 아쉬움", a.Hurt.Mourned && w.History.Events.Any(e => e.Text.Contains($"{a.Name}") && e.Text.Contains("잃었다")));
                if (tow != null)
                {
                    tow.Battery = 1f;
                    for (int t = 0; t < SimTime.Hours(6) && w.Drones.Salvaged < 1; t += 50)
                    {
                        Run(w, 50);
                        if (Environment.GetEnvironmentVariable("EVADBG") == "2" && t % 500 == 0)
                            Console.WriteLine($"    t{t} 견인 {tow.State} {tow.Doing} 배터리 {tow.Battery:0.00} 작동 {tow.Operational} 잔해 {w.EvaRisk.Floaters.Count} 가까운 {w.EvaRisk.Floaters.Select(f => (f.Pos - w.Structure.ShipCenter).Length()).DefaultIfEmpty(-1).Min():0}");
                    }
                    int stockAfter = 0;
                    foreach (var k in new[] { ItemKind.Plate, ItemKind.Motor, ItemKind.Electronics, ItemKind.MetalOre, ItemKind.CellPack })
                        stockAfter += w.Ship.CountStored(k) + w.Drones.Drones.Select(d => d.Dock).Distinct().Sum(f => f.Storage?.Count(k) ?? 0);
                    Check("견인 드론이 잔해를 건져 부품 · 고철로", w.Drones.Salvaged >= 1 && w.Drones.SalvagedItems >= 1, $"건짐 {w.Drones.Salvaged} · 부품 {w.Drones.SalvagedItems} · 재고 {stockBefore} → {stockAfter}");
                }
            }
        }

        // ── 8) 주 컴퓨터: 산소가 모자라면 "들어오라" · 승무원: 겪은 일이 무서우면 EVA를 거부한다 (정비가 밀린다) ──
        {
            var w = DayOne(seed, "Hanbit");
            var c = Adult(w);
            var (mid, eta0) = HullSpots(w, 5).Select(s0 => { PutOutside(w, c, s0); return (s0, eta: w.EvaRisk.EtaMinutes(c)); }).Where(x => x.eta > 5f && x.eta < 10f).OrderBy(x => x.eta).First();
            var p = PutOutside(w, c, mid, oxygenHours: (eta0 + 9f) / 60f);
            Run(w, SimTime.Minutes(3));
            Check("주 컴퓨터: 우주복 산소 · 에어락 거리 → \"들어오십시오\"", w.EvaRisk.Radio.Any(r => r.Tone == RadioTone.Computer && r.Text.Contains("산소") && r.Text.Contains("들어오")) && w.Automation.Book.Acts.Any(a => a.Key == $"eva:{c.Id}:{EvaPlan.ToAirlock}"),
                w.EvaRisk.Radio.LastOrDefault()?.Text ?? "무전 없음");
            Run(w, SimTime.Minutes(25));
            Check("경고를 들은 사람은 에어락으로 돌아온다", !c.Outside && !c.Dead, $"밖 {c.Outside} · 산소 {c.Suit?.Oxygen:0.00}");

            var f = Adult(w, 1);
            w.EvaRisk.AddDread(f, 1f);
            var toils = new List<Toil>();
            bool ok = WorkPlanners.EvaOutFor(f, w, w.Paths.Flood(f.Cell, f.PathProfile), toils, out var why);
            Check("선외 공포 → EVA 거부 (정비가 밀린다)", !ok && why != null && why.Contains("공포") && w.EvaRisk.Stats.Refusals >= 1, why ?? "거부 안 함");
        }

        // ── 9) 상한 우주복: 돌아오면 점검 → 수리 대기로 걸린다 → 고친다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var c = Adult(w);
            // 입고 나간 우주복은 보관함에서 꺼낸 것이다 (걸이 한 칸이 빈다 — 돌아와 걸 자리)
            foreach (var lk in w.Ship.FurnitureOf(FurnitureType.SuitLocker).OrderBy(f => f.Id)) if (lk.Storage!.Take(ItemKind.Suit, 1) > 0) break;
            var p = PutOutside(w, c, HullSpots(w, 4)[0]);
            c.Suit!.Wear.Breach = SuitBreach.Tear;
            c.Suit.Wear.Scuff = 0.4f;
            c.Suit.Wear.Scratches = 3;
            p.Incident = true;
            for (int t = 0; t < SimTime.Hours(6) && (c.Suit != null || c.Outside); t += 50) Run(w, 50);
            bool hung = w.EvaRisk.Stats.SuitChecks >= 1 && w.EvaRisk.DamagedSuits.Count >= 1;
            Check("돌아와 우주복 점검 → 상한 우주복은 수리 대기로 걸린다", hung, $"점검 {w.EvaRisk.Stats.SuitChecks} · 대기 {w.EvaRisk.DamagedSuits.Count} · 우주복 {(c.Suit != null ? "입음" : "벗음")} · 밖 {c.Outside} · 일 {c.Job?.Label} · 보관함 빈칸 {w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Free)}");
            VoyageV15.Put(w, ItemKind.Tape, 2); VoyageV15.Put(w, ItemKind.Glue, 2); // 받는 선반에 (첫 보관함이 테이프를 받지 않을 수 있다)
            for (int t = 0; t < SimTime.Hours(20) && w.EvaRisk.Stats.SuitsMended < 1; t += 100) Run(w, 100);
            var mender = w.Crew.Where(x => !x.Dead && x.Role is CrewRole.Technician or CrewRole.Engineer).OrderBy(x => x.Id).FirstOrDefault();
            string mendWhy = mender == null ? "기술자 없음" : string.Join(" · ", mender.LastEvaluations.Where(e => e.Activity is SuitMendActivity).Select(e => $"{mender.Name} {e.Score:0.00} {e.Reason}"));
            Check("상한 우주복을 고친다", w.EvaRisk.Stats.SuitsMended >= 1, $"수리 {w.EvaRisk.Stats.SuitsMended} · 대기 {w.EvaRisk.DamagedSuits.Count} · 테이프 {w.Ship.CountStored(ItemKind.Tape)} · 접착제 {w.Ship.CountStored(ItemKind.Glue)} · {mendWhy}");
        }

        // ── 10) 결정론 ──
        {
            uint H()
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                return SaveGame.StateHash(w);
            }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 → 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
