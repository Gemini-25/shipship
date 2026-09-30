using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v10.10 선내 로봇 · 자원 회복, v11.0 예방: 하나씩 재현해 본다
public static partial class Program
{
    private static World DayOne(int seed, string ship)
    {
        var w = World.CreateDefault(seed, 0, ship);
        Run(w, SimTime.TicksPerDay);
        return w;
    }

    private static int RunRobotTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"로봇 · 자원 회복 · 예방 · 추진 점검 (v10.10~v11.2) · 시드 {seed}\n");

        // ── 1) 로봇이 고장 나면 사람이 부품을 들고 가서 고친다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var r = w.Robots.Robots.First(x => x.Kind == RobotKind.Maintainer);
            w.Robots.ForceFault(r, RobotFault.Drive);
            Run(w, SimTime.Hours(14));
            bool fixedIt = r.Fault == null && r.Marks.Any(m => m.Text.Contains("고쳤다"));
            Check("로봇 고장 → 사람이 고친다", fixedIt, $"고장 {(r.Fault is RobotFault f ? RobotSystem.FaultName(f) : "없음")} · 이력 {string.Join(" / ", r.Marks.Select(m => m.Text))}");
        }

        // ── 1b) 가벼운 고장은 충전대로 돌아가 스스로 고치고, 임계점 아래로 닳았으면 사람이 고친다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var r = w.Robots.Robots.First(x => x.Kind == RobotKind.Gardener);
            // 일하러 나가 있을 때 고장 나야 한다: 나갈 때까지 기다린다
            for (int t = 0; t < SimTime.Hours(12) && r.State != RobotState.Active; t++) w.Step();
            w.Robots.ForceFault(r, RobotFault.Jam);
            Run(w, SimTime.Hours(4));
            bool self = r.Fault == null && r.SelfRepairsTotal == 1 && w.Log.Entries.Any(e => e.Text.Contains("자가 진단"));
            var r2 = w.Robots.Robots.First(x => x.Kind == RobotKind.Maintainer);
            r2.Condition = 0.2f;
            w.Robots.ForceFault(r2, RobotFault.Sensor);
            bool posted = false;
            for (int t = 0; t < SimTime.Hours(14); t++)
            {
                w.Step();
                posted |= w.Board.Open.Any(o => o.Kind == WorkKind.RepairRobot && o.Target.Robot == r2);
            }
            Check("가벼운 고장은 스스로, 임계점을 넘으면 사람이", self && posted && r2.Fault == null && r2.SelfRepairsTotal == 0,
                $"재배 로봇 자가 수리 {r.SelfRepairsTotal} · 닳은 정비 로봇 수리 요청 {(posted ? "올라옴" : "없음")} · 고장 {(r2.Fault is RobotFault f2 ? RobotSystem.FaultName(f2) : "없음")}");
        }

        // ── 2) 방전돼 멈춘 로봇은 사람이 충전대까지 끌고 온다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var r = w.Robots.Robots.First(x => x.Kind == RobotKind.Hauler);
            var corridor = w.Ship.RoomsOf(RoomType.Corridor).First();
            var far = corridor.Cells.Where(w.Ship.IsOpenFloor).OrderByDescending(c => (c.Center - r.DockPosition).LengthSquared()).First();
            w.Robots.ForceStall(r, far);
            Run(w, SimTime.Hours(14));
            Check("방전 → 사람이 끌어온다", r.Fetched >= 1 && r.State is RobotState.Docked or RobotState.Active, $"상태 {r.State} · 끌려옴 {r.Fetched} · {r.Doing}");
        }

        // ── 3) 로봇이 없으면 그 일은 사람이 한다 (재배 로봇이 멈춘 배는 사람이 거둔다) ──
        {
            var with = DayOne(seed, "Mirinae");
            var without = World.CreateDefault(seed, 0, "Mirinae");
            DisableRobots(without);
            Run(without, SimTime.TicksPerDay);
            Run(with, SimTime.Hours(48));
            Run(without, SimTime.Hours(48));
            int crewWith = with.Crew.Sum(c => c.Stats.Harvests + c.Stats.Services);
            int crewWithout = without.Crew.Sum(c => c.Stats.Harvests + c.Stats.Services);
            Check("로봇이 멈추면 사람이 떠맡는다", crewWithout > crewWith && with.Robots.JobsDone > 0 && without.Robots.JobsDone == 0,
                $"사람의 수확·정비: 로봇 있음 {crewWith} ↔ 없음 {crewWithout} · 로봇이 한 일 {with.Robots.JobsDone}");
        }

        // ── 4) 정비 로봇은 긴 손일을 하는 사람 옆에서 거든다 ──
        {
            var w = DayOne(seed, "Mirinae");
            Run(w, SimTime.Hours(72));
            float assist = w.Robots.Robots.Sum(r => r.AssistHours);
            Check("정비 로봇이 사람을 거든다", assist > 0.5f, $"거든 {assist:0.0}시간");
        }

        // ── 5) 방재 로봇이 불에 거품을 뿌린다 (한빛호 정비실) ──
        {
            var w = DayOne(seed, "Hanbit");
            var shop = w.Ship.RoomsOf(RoomType.Workshop).First();
            var spot = shop.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - shop.Center).LengthSquared()).First();
            Player.Fire(w, spot);
            long t0 = w.Tick;
            long outAt = -1;
            for (int t = 0; t < SimTime.Hours(3); t++)
            {
                w.Step();
                if (outAt < 0 && w.Fire.Count == 0) outAt = w.Tick;
            }
            Check("방재 로봇이 불을 끈다", w.Robots.FiresFought > 0 && w.Fire.Count == 0,
                $"로봇 출동 {w.Robots.FiresFought} · 진화 {(outAt > 0 ? $"{(outAt - t0) / (float)SimTime.TicksPerHour * 60f:0}분" : "안 됨")}");
        }

        // ── 6) 급수 본관이 끊기면 물통으로 재배대를 적신다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var main = w.Piping.WaterMain!;
            Player.PipeBurst(w, main.Path[main.Path.Count / 2], 1.2f);
            Run(w, SimTime.Hours(20));
            var beds = w.Ship.FurnitureOf(FurnitureType.GrowBed).ToList();
            bool watered = beds.Any(b => b.Machine!.Crop!.HandWateredHours > 0f) || w.Log.Entries.Any(e => e.Text.Contains("물통으로"));
            int dead = w.Machines.CropsLost;
            Check("급수 본관이 끊기면 물통으로 나른다", watered, $"본관 {(main.Flow > 0f ? "흐름" : "끊김")} · 물통 급수 {w.Log.Entries.Count(e => e.Text.Contains("물통으로"))}번 · 말라 죽은 작물 {dead}");
        }

        // ── 7) 비상 물자함은 창고에서 채운다 (창고 몫은 남긴다) ──
        {
            var w = DayOne(seed, "Mirinae");
            var bridge = w.Ship.RoomsOf(RoomType.Bridge).First();
            var at = Modules.Spot(w, bridge)!.Value;
            Logistics.InstallCache(w, bridge, at, w.Crew[0]);
            var cache = w.Ship.FurnitureOf(FurnitureType.SupplyCache).First();
            Run(w, SimTime.Hours(16));
            var inv = cache.Storage!;
            Check("비상 물자함이 채워진다", inv.Count(ItemKind.Sealant) >= 1 && inv.Count(ItemKind.MedKit) >= 1,
                $"실링폼 {inv.Count(ItemKind.Sealant)} · 구급 키트 {inv.Count(ItemKind.MedKit)} · 소화기 {inv.Count(ItemKind.Extinguisher)}");
        }

        // ── 8) 사고 뒤 정리: 제 회로가 돌면 임시 배선을 걷어 케이블을 되찾는다, 빈 간이침대는 접는다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First();
            int cables0 = w.Board.Have(ItemKind.Cable);
            w.Power.AddJumper(0, 2, panel.UseSpots[0], 1f);
            var cotCell = Adaptation.CotCells(w, w.Ship.RoomsOf(RoomType.Lounge).First()).First();
            var cot = w.Ship.AddFurniture(FurnitureType.Cot, cotCell);
            w.Paths.Invalidate();
            Run(w, SimTime.Hours(34));
            bool jumperGone = !w.Power.Jumpers.Any(j => !j.Permanent) && w.Power.RemovedJumpers.Count == 1;
            Check("임시 배선을 걷는다", jumperGone && w.Board.Have(ItemKind.Cable) >= cables0, $"걷음 {w.Adapt.JumpersRemoved} · 케이블 {cables0} → {w.Board.Have(ItemKind.Cable)}");
            Check("빈 간이침대를 치운다", cot.Stowed, $"치움 {w.Adapt.CotsStowed}");
        }

        // ── 9) 전조: 순찰하는 배는 찾아서 막고, 아무도 못 보는 배는 고장 난다 ──
        {
            int prevented = 0, missed = 0, faultsSeen = 0, faultsBlind = 0;
            for (int k = 0; k < 4; k++)
                foreach (bool blind in new[] { false, true })
                {
                    var w = DayOne(seed + k * 101, "Hanbit");
                    w.PreventionBlind = blind;
                    var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!;
                    pump.Omen = new Omen { Kind = OmenKind.Vibration, Fault = FaultKind.BearingWear, Since = w.Tick, Due = w.Tick + SimTime.Hours(16) };
                    Run(w, SimTime.Hours(20));
                    if (blind) { missed += w.Precursors.Missed; faultsBlind += pump.FaultCount; }
                    else { prevented += w.Precursors.Prevented; faultsSeen += pump.FaultCount; }
                }
            Check("전조를 찾은 배는 막고, 못 본 배는 고장 난다", prevented >= 3 && missed >= 3 && faultsBlind > faultsSeen,
                $"막음 {prevented}/4 · 놓침 {missed}/4 · 펌프 고장 (보는 배 {faultsSeen} ↔ 못 보는 배 {faultsBlind})");
        }

        // ── 9b) 엔진: 센서가 먼저 본 운석은 회피 기동으로 비키고(스치거나), 엔진이 멎은 배는 그대로 맞는다 ──
        {
            int dodged = 0, glanced = 0, attempts = 0, stoppedAttempts = 0, breachOk = 0, breachStopped = 0;
            // 확률이라 표본을 넉넉히 (16번): 자동 조종이면 반쯤 비키거나 스친다
            for (int k = 0; k < 16; k++)
                foreach (bool stopped in new[] { false, true })
                {
                    var w = DayOne(seed + k * 37, "Mirinae");
                    if (stopped) foreach (var e in w.Propulsion.Engines) w.Machines.Break(e, FaultKind.Wrecked);
                    var room = w.Ship.RoomsOf(RoomType.Quarters).First();
                    Player.Meteor(w, Scenarios.OuterTarget(w, room), 0.9f);
                    Run(w, SimTime.Minutes(20));
                    if (stopped) { stoppedAttempts += w.Propulsion.Evasions; breachStopped += w.History.Breaches; }
                    else { attempts += w.Propulsion.Evasions; dodged += w.Propulsion.Dodged; glanced += w.Propulsion.Glanced; breachOk += w.History.Breaches; }
                }
            Check("엔진으로 운석을 비킨다 (엔진이 멎으면 못 비킨다)", attempts >= 12 && dodged + glanced >= 4 && stoppedAttempts == 0 && breachOk < breachStopped,
                $"회피 {attempts}/16 · 비껴감 {dodged} · 스침 {glanced} · 엔진 멎은 배 회피 {stoppedAttempts} · 파공 {breachOk} ↔ {breachStopped}");
        }

        // ── 9c) 항로: 원료가 모자라면 잔해 지대로 가고(회의), 거기선 작은 운석이 날아든다. 엔진이 멎으면 빠져나오지 못한다 ──
        {
            var w = DayOne(seed, "Mirinae");
            w.Propulsion.Propellant = w.Propulsion.Capacity;
            // 원료를 크게 쓰는 배 (날마다 원료·금속판이 바닥난다)
            for (int d = 0; d < 6 && w.Propulsion.Transfers == 0; d++)
            {
                foreach (var k in ItemKinds.RawKinds) Scenarios.LimitStock(w, k, 2);
                Scenarios.LimitStock(w, ItemKind.Plate, 4);
                Run(w, SimTime.TicksPerDay);
            }
            bool went = w.Propulsion.Transfers >= 1;
            if (!went)
            {
                var cc = w.Board.All.FirstOrDefault(o => o.Kind == WorkKind.ChangeCourse);
                Console.WriteLine($"    [항로] 원료 {ItemKinds.RawKinds.Sum(k => w.Board.Have(k))} · 금속판 {w.Board.Have(ItemKind.Plate)} · 평화 {Evolution.Peaceful(w)} · 탱크 {w.Air.Reserve / w.Air.ReserveCapacity:0.00} · 실링폼 {w.Board.Have(ItemKind.Sealant)} · 추력 {w.Propulsion.Thrust:0.00} · 추진제 {w.Propulsion.Propellant:0}/{w.Propulsion.TransferCost:0} · 일 {(cc == null ? "없음" : $"{cc.Decision} {cc.Verdict} {cc.Assignee?.Name} 막힘 {cc.BlockedReason}")}");
            }
            var w2 = DayOne(seed, "Mirinae");
            w2.Propulsion.Place(ZoneKind.Debris);
            foreach (var e in w2.Propulsion.Engines) w2.Machines.Break(e, FaultKind.Wrecked);
            bool leftWhileDead = false;
            float deadHours = 0f;
            for (int t = 0; t < SimTime.Hours(24 * 6); t++)
            {
                w2.Step();
                if (w2.Tick % World.SystemInterval != 0) continue;
                if (w2.Propulsion.Thrust < 0.5f) deadHours += World.SystemInterval / (float)SimTime.TicksPerHour;
                if (w2.Propulsion.Zone == ZoneKind.Normal && w2.Propulsion.Thrust < 0.5f && w2.Propulsion.Transfers > 0) leftWhileDead = true;
            }
            bool stuck = !leftWhileDead && w2.Propulsion.AmbientHits >= 1;
            Check("원료가 모자라면 잔해 지대로 · 엔진이 멎은 동안엔 못 나온다", went && stuck,
                $"항로 변경 {w.Propulsion.Transfers}번 · 지금 {PropulsionSystem.ZoneName(w.Propulsion.Zone)} · 엔진 멎은 배: 멎은 {deadHours:0}시간 동안 날아든 운석 {w2.Propulsion.AmbientHits} · 고친 뒤 {PropulsionSystem.ZoneName(w2.Propulsion.Zone)}");
        }

        // ── 10) 로봇·전조·자원 장부가 들어간 배도 저장·불러오기가 같은 역사를 흘린다 ──
        {
            var w = DayOne(seed, "Hanbit");
            Player.Fire(w, w.Ship.RoomsOf(RoomType.Galley).First().Cells.First(w.Ship.IsOpenFloor));
            Run(w, SimTime.Hours(6));
            Player.Break(w, w.Ship.FurnitureOf(FurnitureType.CoolantPump).First());
            Run(w, SimTime.Hours(12));
            uint h = SaveGame.StateHash(w);
            var runner = new ReplayRunner(SaveGame.Write(w));
            while (!runner.Advance(50000)) { }
            Check("로봇이 있는 배의 저장·불러오기", SaveGame.StateHash(runner.World) == h, $"로봇 {w.Robots.Summary} · 지문 {(SaveGame.StateHash(runner.World) == h ? "같음" : "다름")}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 로봇·자원 회복·예방 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
