using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v12.2 설비 열·폭발·잔해·역화·일산화탄소·짙은 산소·재기동 지연·배터리 노화
public static partial class Program
{
    private static int RunVolatileTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"설비 열·폭발 점검 (v12.2) · 시드 {seed}\n");

        // ── 1) 알아채고 식히면 막는다: 달아오른 산소 발생기 ──
        {
            var w = DayOne(seed, "Mirinae");
            var gen = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!;
            Hazards.Apply(w, HazardKind.Overheat, gen.Body.Cells[0], -1);
            Run(w, SimTime.Hours(4));
            var st = w.Volatile.Stats;
            Check("달아오른 설비 — 사람이 먼저 내려 식힌다", st.CooledDown > 0,
                $"식힘 {st.CooledDown} · 폭발 {st.Explosions} · 산소 발생기 열 {gen.Heat * 100:0}% · 수소 {gen.Vapor * 100:0}% · {string.Join(" / ", gen.Marks.TakeLast(3).Select(m => m.Text))}");
        }

        // ── 2) 아무도 못 보면 터진다: 배터리 열폭주 → 불·유독 연기·옆 셀 ──
        {
            var w = DayOne(seed, "Hanbit");
            w.Automation.GetType(); // (그대로)
            var bats = w.Ship.FurnitureOf(FurnitureType.Battery).ToList();
            var b = bats.First().Machine!;
            // 곁에서 불이 달구는 것처럼: 식지 않게 (터지는 건 시간당 한 번꼴의 운 — 여섯 시간이면 거의 반드시)
            for (int i = 0; i < SimTime.Hours(6) && w.Volatile.Stats.ThermalRunaways == 0; i++) { b.Heat = MathF.Max(b.Heat, 1.25f); w.Step(); }
            var room = b.Body.Room;
            Check("배터리 열폭주 — 불·유독 연기·옆 셀이 달아오른다", w.Volatile.Stats.ThermalRunaways > 0 && (w.Fire.CountIn(room) > 0 || room.Air.Toxin > 0.05f),
                $"열폭주 {w.Volatile.Stats.ThermalRunaways} · 불 {w.Fire.CountIn(room)} · 유독 가스 {room.Air.Toxin * 100:0}% · 옆 셀 열 {string.Join(", ", bats.Skip(1).Select(x => $"{x.Machine!.Heat * 100:0}%"))} · 폭발 {w.Volatile.Stats.Explosions} · 잔해 {w.Ship.Rubble.Count}칸");
        }

        // ── 3) 수소 폭발 → 잔해가 문을 막고 → 사람이 치운다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var gen = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!;
            gen.Vapor = 0.9f;
            w.Volatile.Blow(gen, BlowKind.Hydrogen, "시험");
            int rubble = w.Ship.Rubble.Count;
            bool doorBlocked = w.Ship.Doors.Any(d => d.Blocked);
            Run(w, SimTime.Hours(10));
            Check("수소 폭발 — 잔해가 생기고 사람이 치운다", rubble > 0 && w.Volatile.Stats.RubbleCleared > 0,
                $"잔해 {rubble}칸 (문 막힘 {(doorBlocked ? "예" : "아니오")}) → 치움 {w.Volatile.Stats.RubbleCleared} · 남음 {w.Ship.Rubble.Count} · 부상 {w.Crew.Count(c => c.Vitals.InjuryCause?.Contains("폭발") == true)} · 쓰러짐 {w.History.Collapses}");
        }

        // ── 4) 아크 섬광: 곁의 사람 화상, 모든 회로가 떨어진다 → 복구 ──
        {
            var w = DayOne(seed, "Mirinae");
            var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
            var near = w.Crew.First(c => c.CanAct);
            var spot = panel.Body.UseSpots.First(s => w.Ship.IsOpenFloor(s));
            near.Position = spot.Center; near.PreviousPosition = near.Position;
            w.Volatile.Blow(panel, BlowKind.ArcFlash, "시험");
            int trips = panel.Faults.Count(f => f.Kind == FaultKind.BreakerTrip);
            string burn = near.Vitals.InjuryCause ?? "-";
            Run(w, SimTime.Hours(6));
            int left = panel.Faults.Count(f => f.Kind == FaultKind.BreakerTrip);
            Check("아크 섬광 — 화상, 모든 회로 차단 → 다시 올린다", trips >= 3 && burn.Contains("아크") && left < trips,
                $"차단 {trips} → 여섯 시간 뒤 {left} · {near.Name} {burn}");
        }

        // ── 5) 역화: 불탄 방을 밀폐 → 식기 전에 문이 열리면 불길이 터진다 / 조금씩 환기하면 막는다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var room = w.Ship.RoomsOf(RoomType.Galley).First();
            room.Backdraft = 0.8f;
            room.Air.Temperature = 70f;
            room.Air.O2 = 9f;
            foreach (var d in room.Doors) { d.Openness = 0f; }
            Run(w, SimTime.Hours(6));
            var st = w.Volatile.Stats;
            bool bled = w.History.Events.Any(e => e.Text.Contains("역화를 막았다"));
            Check("역화 — 알아채면 조금씩 환기해 막고, 모르고 열면 터진다", st.Backdrafts > 0 || bled,
                $"역화 {st.Backdrafts} · 조금씩 환기로 막음 {(bled ? "예" : "아니오")} · 남은 위험 {room.Backdraft * 100:0}% · {room.Air.Temperature:0}℃");
        }

        // ── 6) 일산화탄소: 전기 없는 방에서 보조 발전기 — 경보 없이 쌓인다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var aux = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).First();
            var room = aux.Room;
            float peak = 0f;
            // 원자로를 멈춰 보조 발전기를 돌리게 하고, 그 방 환기를 닫는다 (v12.6 태양 날개는 접어 둔다 — 배터리가 덜 빠져 보조 발전기가 늦게 켜진다)
            foreach (var f in w.Exterior.All) w.Exterior.Damage(f, 1f, "시험");
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
            for (int h = 0; h < 8 * 4; h++)
            {
                room.VentOpen = false;
                room.DamperStuck = true; // v16.22 새 설계 배전실 댐퍼는 컴퓨터가 바로 다시 연다 — "환기가 닫힌 방"은 닫힌 채 걸린 댐퍼로
                // 원자로가 계속 멈춰 있게 (고친 펌프는 다시 멈춘다 — 이 시험은 보조 발전기의 일산화탄소를 본다)
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) if (p.Machine!.Faults.Count == 0) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                Run(w, SimTime.Minutes(15));
                peak = MathF.Max(peak, room.Air.CO);
            }
            Check("일산화탄소 — 환기가 닫힌 방에서 보조 발전기를 돌리면 쌓인다", peak > 0.1f,
                $"보조 발전기 {(w.Power.AuxRunning ? "돎" : "멈춤")} · 최고 {peak * 1000:0}ppm쯤 · 경보 {w.Volatile.Stats.CoAlarms}");
        }

        // ── 7) 짙은 산소: 산소관 누출 → 산소가 짙어지고, 불꽃이 불이 된다 → 막는다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var room = w.Ship.RoomsOf(RoomType.Workshop).First();
            Hazards.Apply(w, HazardKind.OxygenLeak, Cell.FromPosition(room.Center), -1);
            float peak = 0f;
            for (int h = 0; h < 12 * 4; h++) { Run(w, SimTime.Minutes(15)); peak = MathF.Max(peak, room.Air.O2); }
            Check("짙은 산소 — 산소관이 새면 산소가 오르고, 사람이 막는다", peak > 21.3f && room.O2Leak == 0f, // 평소 21.0 ± 0.1
                $"최고 산소 {peak:0.0}kPa · 누출 {(room.O2Leak > 0f ? "그대로" : "막음")} · 짙은 산소 불꽃 {w.Volatile.Stats.SparkFires}");
        }

        // ── 8) 재기동 지연: 긴급 정지 뒤 몇 시간은 출력이 다 오르지 않는다 ──
        {
            var w = DayOne(seed, "Mirinae");
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.BearingWear);
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
            Run(w, SimTime.Minutes(30));
            float poison = w.Power.ReactorPoison;
            Check("재기동 지연 — 긴급 정지 뒤 제논 독", poison > 0.8f || w.History.Scrams > 0 && w.Power.ReactorPoison > 0f,
                $"긴급 정지 {w.History.Scrams} · 재기동 지연 {poison * 100:0}%");
        }

        // ── 9) 배터리 바닥 → 셀이 상한다 ──
        {
            var w = DayOne(seed, "Mirinae");
            float cap0 = w.Power.BatteryCapacity;
            w.Power.BatteryCharge = 0f;
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
            if (w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault() is Furniture ag) w.Machines.Break(ag.Machine!, FaultKind.Wrecked);
            Run(w, SimTime.Hours(3));
            Check("배터리가 바닥까지 — 용량이 영구히 준다", w.Volatile.Stats.DeepDischarges > 0 && w.Power.BatteryCapacity < cap0,
                $"용량 {cap0:0} → {w.Power.BatteryCapacity:0}kWh · 바닥 {w.Volatile.Stats.DeepDischarges}");
        }

        // ── 10) 평소에는 터지지 않는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.TicksPerDay * 5);
            float hot = w.Ship.Machines.Select(m => m.Heat).DefaultIfEmpty(0f).Max();
            Check("평소 운항 — 저절로 터지지 않는다", w.Volatile.Stats.Explosions == 0 && hot < 0.72f,
                $"폭발 {w.Volatile.Stats.Explosions} · 가장 뜨거운 설비 {hot * 100:0}%");
        }

        // ── 11) 결정론 ──
        {
            uint H()
            {
                var w = DayOne(seed, "Mirinae");
                w.Volatile.Blow(w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!, BlowKind.Hydrogen, "시험");
                Run(w, SimTime.Hours(12));
                return SaveGame.StateHash(w);
            }
            uint a = H(), b = H();
            Check("폭발이 든 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 설비 열·폭발 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
