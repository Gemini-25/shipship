using System;
using System.Linq;
using ShipSim.Core;



public static partial class Program
{
    /// <summary>v11.2 사고 종류: 걸면 제대로 일어나고, 배가 수습하는지. 그리고 무작위 사고가 결정적인지.</summary>
    private static int RunHazardTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"사고 종류 · 무작위 사고 점검 (v11.2) · 시드 {seed}\n");
        const string ship = "Hanbit";

        // ── 1) 운석우: 30분 동안 여러 개가 날아온다 ──
        {
            var w = DayOne(seed, ship);
            int before = w.History.Meteors + w.Propulsion.Dodged;
            string? what = Player.Hazard(w, HazardKind.MeteorShower, default);
            int queued = w.Hazards.Shower.Count;
            Run(w, SimTime.Hours(1));
            int hits = w.History.Meteors + w.Propulsion.Dodged - before;
            Check("운석우 — 30분에 걸쳐 여러 개", what != null && queued >= 5 && w.Hazards.Shower.Count == 0 && hits >= 4,
                $"예정 {queued}개 · 맞음/비껴감 {hits}개 (회피 {w.Propulsion.Dodged} · 스침 {w.Propulsion.Glanced})");
        }

        // ── 2) 태양 폭풍: 센서가 흐려지고 장비가 튀고, 지나가면 선외 작업을 다시 한다 ──
        {
            var w = DayOne(seed, ship);
            float q0 = w.Sensors.Quality;
            int faults0 = w.Ship.Machines.Sum(m => m.Faults.Count);
            Player.Hazard(w, HazardKind.SolarStorm, default);
            float q1 = w.Sensors.Quality;
            int faults1 = w.Ship.Machines.Sum(m => m.Faults.Count);
            bool active = w.Hazards.StormActive;
            Run(w, SimTime.Hours(12));
            Check("태양 폭풍 — 센서 흐림 · 장비가 튄다 · 지나간다", active && q1 < q0 * 0.5f && faults1 > faults0 && !w.Hazards.StormActive
                && w.Log.Entries.Any(e => e.Text.Contains("태양 폭풍이 지나갔다")),
                $"센서 {q0 * 100:0}% → {q1 * 100:0}% · 고장 {faults0} → {faults1} · 지금 {(w.Hazards.StormActive ? "폭풍 중" : "지나감")}");
        }

        // ── 3) 전력 서지: 차단기·단락이 나고, 사람이 복구한다 ──
        {
            var w = DayOne(seed, ship);
            var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
            Player.Hazard(w, HazardKind.PowerSurge, default);
            int circuit = panel.Faults.Count(f => f.Circuit >= 0);
            Run(w, SimTime.Hours(10));
            int left = panel.Faults.Count(f => f.Circuit >= 0);
            Check("전력 서지 — 회로가 끊기고 복구한다", circuit >= 2 && left == 0, $"끊긴 회로 {circuit} → 남은 {left}");
        }

        // ── 4) 유독 가스: 방에 가스가 차고, 우주복을 입고 들어가 막고, 환기로 걷어 낸다 ──
        {
            var w = DayOne(seed, ship);
            var room = w.Ship.RoomsOf(RoomType.LifeSupport).First();
            Player.Hazard(w, HazardKind.GasLeak, room.Cells[0]);
            Run(w, SimTime.Minutes(30));
            float peak = room.Air.Toxin;
            bool vent = room.VentOpen;
            float worst = 1f;
            bool suited = false;
            for (int t = 0; t < SimTime.Hours(14); t++)
            {
                w.Step();
                peak = MathF.Max(peak, room.Air.Toxin);
                worst = MathF.Min(worst, w.Crew.Min(c => c.Vitals.Health));
                suited |= w.Crew.Any(c => c.Room == room && c.Suit != null && room.Air.Toxin > 0.2f);
            }
            bool sealedIt = w.Hazards.GasSource(room) == null;
            Check("유독 가스 — 우주복 입고 막고, 환기로 걷어 낸다", peak > 0.3f && !vent && sealedIt && room.Air.Toxin < 0.1f && suited && worst > 0.3f,
                $"최고 {peak:0.00} · 새는 동안 댐퍼 {(vent ? "열림" : "닫힘")} · 우주복 {(suited ? "입고 들어감" : "없이")} · 지금 {room.Air.Toxin:0.00} · {(sealedIt ? "막음" : "아직 샌다")} · 가장 낮은 체력 {worst * 100:0}%");
        }

        // ── 5) 병충해: 번지기 전에 알아채고 약을 쳐서 잡는다 (로봇이 아니라 사람이) ──
        {
            var w = DayOne(seed, ship);
            var bed = w.Ship.FurnitureOf(FurnitureType.GrowBed).First(f => f.Machine!.Crop is { Growth: > 0.1f } c && !c.Ripe);
            Player.Hazard(w, HazardKind.CropBlight, bed.Cells[0]);
            bool known = false;
            for (int t = 0; t < SimTime.Hours(40); t++)
            {
                w.Step();
                known |= bed.Machine!.Crop!.BlightKnown;
            }
            var beds = w.Ship.FurnitureOf(FurnitureType.GrowBed).Select(f => f.Machine!.Crop!).ToList();
            Check("병충해 — 알아채고 사람이 약을 쳐서 잡는다", known && w.Hazards.BlightCured >= 1 && beds.All(c => c.Blight <= 0f) && w.Hazards.BlightKilled == 0,
                $"알아챔 {(known ? "예" : "아니오")} · 잡음 {w.Hazards.BlightCured} · 죽음 {w.Hazards.BlightKilled} · 남은 병충해 {beds.Count(c => c.Blight > 0f)}곳");
        }

        // ── 6) 식중독: 먹은 사람이 앓고, 같은 묶음을 찾아 버린다 ──
        {
            var w = DayOne(seed, ship);
            // 사람들이 실제로 먹는 배식기 (가장 많이 줄어드는 것)
            var disp = w.Ship.FurnitureOf(FurnitureType.MealDispenser).ToList();
            var taken = disp.ToDictionary(f => f, _ => 0);
            var last = disp.ToDictionary(f => f, f => f.Storage!.Count(ItemKind.Meal));
            for (int t = 0; t < SimTime.Hours(6); t++)
            {
                w.Step();
                foreach (var f in disp)
                {
                    int n = f.Storage!.Count(ItemKind.Meal);
                    if (n < last[f]) taken[f] += last[f] - n;
                    last[f] = n;
                }
            }
            var box = disp.OrderByDescending(f => taken[f]).First();
            if (box.Storage!.Count(ItemKind.Meal) < 3) box.Storage.Add(ItemKind.Meal, 4);
            Player.Hazard(w, HazardKind.FoodPoisoning, box.Cells[0]);
            int tainted = w.Ship.Containers.Sum(f => f.Storage!.Tainted);
            int meals0 = w.Crew.Sum(c => c.Stats.Meals), inBox0 = box.Storage.Count(ItemKind.Meal);
            Run(w, SimTime.Hours(36));
            int left = w.Ship.Containers.Sum(f => f.Storage!.Tainted);
            Check("식중독 — 먹은 사람이 앓고, 남은 것은 버린다", tainted >= 2 && w.Hazards.Poisoned >= 1 && left == 0 && w.Hazards.Poisoned < tainted + 1,
                $"균이 든 식사 {tainted}끼 · 앓은 사람 {w.Hazards.Poisoned} · 버림 {w.Hazards.FoodDiscarded} · 남음 {left}");
        }

        // ── 7) 로봇 오작동: 멈춘 로봇을 사람이 고친다 ──
        {
            var w = DayOne(seed, ship);
            var r = w.Robots.Robots.First();
            string? what = Player.Hazard(w, HazardKind.RobotMalfunction, default, r.Id);
            bool stalled = r.Fault == RobotFault.Controller;
            Run(w, SimTime.Hours(20));
            Check("로봇 오작동 — 사람이 제어기를 고친다", what != null && stalled && r.Fault == null, $"{r.Name} · 지금 {(r.Fault is RobotFault f ? RobotSystem.FaultName(f) : "멀쩡")}");
        }

        // ── 8) 선체 균열: 새는 곳을 막고, 벽의 피로는 남는다 ──
        {
            var w = DayOne(seed, ship);
            var room = w.Ship.RoomsOf(RoomType.Quarters).First();
            var at = Scenarios.OuterTarget(w, room);
            var wallCell = Hazards.HullAt(w, at)!.Value;
            float max0 = w.Ship.WallAt(wallCell)!.MaxIntegrity;
            Player.Hazard(w, HazardKind.HullCrack, at);
            bool leaked = false;
            for (int t = 0; t < SimTime.Hours(16); t++) { w.Step(); leaked |= room.Leaking; }
            var wall = w.Ship.WallAt(wallCell)!;
            Check("선체 균열 — 새는 곳을 막는다 · 피로는 남는다", leaked && !room.Leaking && wall.MaxIntegrity < max0,
                $"샜음 {(leaked ? "예" : "아니오")} · 지금 {(room.Leaking ? "새는 중" : "막힘")} · 벽 한계 {max0 * 100:0}% → {wall.MaxIntegrity * 100:0}%");
        }

        // ── 9) 원자로 이상: 제어봉이 걸리고 노심이 뜨거워진다 → 기관사가 고친다 ──
        {
            var w = DayOne(seed, ship);
            float t0 = w.Power.ReactorTemperature;
            Player.Hazard(w, HazardKind.ReactorTransient, default);
            float t1 = w.Power.ReactorTemperature;
            var reactor = w.Power.Reactor!;
            bool fault = reactor.Has(FaultKind.ControlFault);
            Run(w, SimTime.Hours(14));
            Check("원자로 이상 — 노심 온도가 뛰고, 제어봉을 고친다", t1 > t0 + 40f && fault && !reactor.Has(FaultKind.ControlFault),
                $"노심 {t0:0} → {t1:0}℃ · 긴급 정지 {w.History.Scrams}번 · 지금 {(reactor.Has(FaultKind.ControlFault) ? "제어봉 불량" : "고침")}");
        }

        // ── 10) 문·조명·작업 사고·물 오염·컴퓨터 오류 ──
        {
            var w = DayOne(seed, ship);
            var door = w.Ship.Doors.First(d => !d.IsExternal && d.RoomA != null && d.RoomB != null);
            var dark = w.Ship.RoomsOf(RoomType.Mess).First();
            var hurt = w.Crew.First();
            float water0 = w.Water.Level;
            bool a = Player.Hazard(w, HazardKind.DoorJam, door.Cell) != null && door.MotorBroken;
            bool b = Player.Hazard(w, HazardKind.LightsOut, dark.Cells[0]) != null && dark.LightsOut;
            bool c = Player.Hazard(w, HazardKind.WorkAccident, default, hurt.Id) != null && hurt.Vitals.Injury > 0.15f;
            bool d = Player.Hazard(w, HazardKind.WaterContamination, default) != null && w.Water.Level < water0 * 0.85f;
            bool e = Player.Hazard(w, HazardKind.ComputerFault, default) != null && w.Ship.FurnitureOf(FurnitureType.MainComputer).First().Machine!.Has(FaultKind.StorageFault);
            Run(w, SimTime.Hours(24));
            var rec = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).First().Machine!;
            bool fixedAll = !door.MotorBroken && !dark.LightsOut && hurt.Vitals.TreatedTick > w.Tick - SimTime.Hours(24) && !rec.Has(FaultKind.FilterClogged);
            Check("문 고장·조명·작업 사고·물 오염·컴퓨터 오류 — 걸리고 수습된다", a && b && c && d && e && fixedAll,
                $"걸림 {(a ? 1 : 0)}{(b ? 1 : 0)}{(c ? 1 : 0)}{(d ? 1 : 0)}{(e ? 1 : 0)} · 문 {(door.MotorBroken ? "고장" : "고침")} · 조명 {(dark.LightsOut ? "꺼짐" : "켜짐")} · 치료 {(hurt.Vitals.TreatedTick > w.Tick - SimTime.Hours(24) ? "받음" : "아직")} · 정수기 {(rec.Has(FaultKind.FilterClogged) ? "막힘" : "고침")} · 주 컴퓨터 {(w.Automation.MainOnline ? "켜짐" : "꺼짐")}");
        }

        // ── 11) 잔해 구름: 잔해 지대로 밀려나고 운석이 날아든다 ──
        {
            var w = DayOne(seed, ship);
            Player.Hazard(w, HazardKind.DebrisCloud, default);
            bool pushed = w.Propulsion.Zone == ZoneKind.Debris;
            Run(w, SimTime.Hours(1));
            Check("잔해 구름 — 잔해 지대로 밀려난다", pushed && w.Hazards.Shower.Count < 3, $"지금 {PropulsionSystem.ZoneName(w.Propulsion.Zone)} · 남은 파편 {w.Hazards.Shower.Count}");
        }

        // ── 12) 무작위 사고: 켜면 평균 간격대로 나고, 같은 시드면 같은 역사, 끄면 난수를 한 번도 안 뽑는다 ──
        {
            float keep = HazardSystem.RandomDays;
            HazardSystem.RandomDays = 1f;
            var a = World.CreateDefault(seed, 0, ship);
            var b = World.CreateDefault(seed, 0, ship);
            Run(a, SimTime.TicksPerDay * 6);
            Run(b, SimTime.TicksPerDay * 6);
            HazardSystem.RandomDays = 0f;
            var off = World.CreateDefault(seed, 0, ship);
            Run(off, SimTime.TicksPerDay * 2);
            HazardSystem.RandomDays = keep;
            var kinds = a.Log.Entries.Where(e => e.Text.StartsWith("무작위 사고:")).Select(e => e.Text["무작위 사고: ".Length..]).ToList();
            Check("무작위 사고 — 평균 간격대로 · 결정적 · 끄면 조용", a.Hazards.RandomCount >= 2 && SaveGame.StateHash(a) == SaveGame.StateHash(b) && off.Hazards.RandomRng.Draws == 0,
                $"6일에 {a.Hazards.RandomCount}번 ({string.Join(", ", kinds)}) · 같은 시드 지문 {(SaveGame.StateHash(a) == SaveGame.StateHash(b) ? "같음" : "다름")} · 끔: 난수 {off.Hazards.RandomRng.Draws}번");
        }

        // ── 13) 사고를 건 배와 무작위 사고가 난 배도 저장·불러오기가 같은 역사를 흘린다 ──
        {
            float keep = HazardSystem.RandomDays;
            HazardSystem.RandomDays = 0.5f;
            var w = DayOne(seed, ship);
            Player.Hazard(w, HazardKind.GasLeak, w.Ship.RoomsOf(RoomType.Galley).First().Cells[0]);
            Run(w, SimTime.Hours(3));
            var box = w.Ship.FurnitureOf(FurnitureType.MealDispenser).OrderByDescending(f => f.Storage!.Count(ItemKind.Meal)).First();
            Player.Hazard(w, HazardKind.FoodPoisoning, box.Cells[0]);
            Player.Hazard(w, HazardKind.SolarStorm, default);
            Run(w, SimTime.Hours(40));
            uint h = SaveGame.StateHash(w);
            var runner = new ReplayRunner(SaveGame.Write(w));
            while (!runner.Advance(50000)) { }
            HazardSystem.RandomDays = keep;
            Check("사고·무작위 사고가 난 배의 저장·불러오기", SaveGame.StateHash(runner.World) == h,
                $"무작위 {w.Hazards.RandomCount}번 · 지문 {(SaveGame.StateHash(runner.World) == h ? "같음" : "다름")}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 사고 종류 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
