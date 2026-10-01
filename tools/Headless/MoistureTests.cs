using System;
using System.Linq;
using ShipSim.Core;

// v12.3 물·습기·전기
public static partial class Program
{
    private static int RunMoistureTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"물·습기·전기 점검 (v12.3) · 시드 {seed}\n");

        // ── 1) 컴퓨터가 있을 때: 물이 차면 분전함을 통째로 내린다 → 퍼낸다 → 다시 올린다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var room = w.Ship.RoomsOf(RoomType.Galley).First();
            w.Moisture.AddWater(room, room.Cells.Count * 20f * 0.5f);
            bool isolated = false;
            for (int i = 0; i < 16 * 4; i++) { Run(w, SimTime.Minutes(15)); isolated |= room.BreakerOff; }
            var st = w.Moisture.Stats;
            Check("컴퓨터 — 물 찬 방의 분전함을 내리고, 사람이 퍼내고 다시 올린다", isolated && st.AutoIsolations > 0 && st.Pumped > 0 && !room.BreakerOff && MoistureSystem.Depth(room) < 0.06f,
                $"{st} · 남은 물 {room.Flood:0}L · 분전함 {(room.BreakerOff ? "내림" : "올림")}");
        }

        // ── 2) 컴퓨터가 없고 모두 잠든 밤: 아무도 모르는 물 → 누전·감전 → 깬 사람이 분전함을 내린다 ──
        {
            int shorts = 0, shocks = 0, fires = 0, crewIso = 0;
            for (int k = 0; k < 6 && (shorts + shocks + fires == 0 || crewIso == 0); k++) // 배 여섯까지 (누전이 먼저 회로를 떨어뜨리면 내릴 분전함이 없다)
            {
                var w = DayOne(seed + k * 17, "Mirinae");
                foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked);
                var room = w.Ship.RoomsOf(RoomType.Storage).First();
                // 창고 곁에 아무도 없을 때 물이 찬다 (아무도 모른다)
                for (int t = 0; t < 24 * 12 && w.Crew.Any(c => c.Room == room || c.Room != null && room.Doors.Any(d => d.RoomA == c.Room || d.RoomB == c.Room)); t++) Run(w, SimTime.Minutes(5));
                w.Moisture.AddWater(room, room.Cells.Count * 20f * 0.5f);
                Run(w, SimTime.Hours(8));
                var st = w.Moisture.Stats;
                shorts += st.Shorts; shocks += st.Shocks; fires += st.ElectricFires; crewIso += st.CrewIsolations;
            }
            Check("컴퓨터 없음 — 아무도 모르는 물이 누전·전기 화재를 부르고, 알아챈 사람이 분전함을 내린다", shorts + shocks + fires > 0 && crewIso > 0,
                $"누전 {shorts} · 감전 {shocks} · 전기 화재 {fires} · 사람이 분전함 차단 {crewIso}");
        }

        // ── 3) 설비 관 이음이 빠지면 물이 샌다 → 급수 밸브를 잠그고 → 관을 잇고 → 다시 연다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var gen = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).First().Machine!;
            gen.Line = 0.1f;
            var room = gen.Body.Room;
            bool shut = false;
            for (int i = 0; i < 14 * 4; i++) { Run(w, SimTime.Minutes(15)); shut |= room.ValveShut; }
            Check("설비 관 누수 — 밸브를 잠그고, 관을 잇고, 다시 연다", shut && !room.ValveShut && gen.Line >= 0.3f,
                $"밸브 잠금 {(shut ? "예" : "아니오")} → 지금 {(room.ValveShut ? "잠김" : "열림")} · 관 {gen.Line * 100:0}% · 샌 물 {w.Moisture.Stats.Leaked:0}L · 퍼냄 {w.Moisture.Stats.Pumped:0}L");
        }

        // ── 4) 결로: 습하고 환기가 끊긴 냉각실 ──
        {
            var w = DayOne(seed, "Mirinae");
            var room = w.Ship.RoomsOf(RoomType.Cooling).First();
            for (int i = 0; i < 6 * 4; i++) { room.Humidity = MathF.Max(room.Humidity, 0.92f); room.VentOpen = false; Run(w, SimTime.Minutes(15)); }
            Check("결로 — 습한 방의 찬 배관에 물이 맺혀 고인다", w.Moisture.Stats.Condensed > 5f, $"결로 {w.Moisture.Stats.Condensed:0}L · 바닥 물 {room.Flood:0}L");
        }

        // ── 5) 기동 전류: 전기가 돌아올 때 설비가 한꺼번에 켜지면 차단기가 떨어질 수 있다 (컴퓨터가 없으면 더 자주) ──
        {
            int trips = 0, tries = 0;
            for (int k = 0; k < 8; k++)
            {
                var w = DayOne(seed + k * 31, "Mirinae");
                foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked);
                var room = w.Ship.RoomsOf(RoomType.LifeSupport).First();
                room.BreakerOff = true; Run(w, SimTime.Minutes(5));
                room.BreakerOff = false; Run(w, SimTime.Minutes(5));
                tries++;
                trips += w.Moisture.Stats.Inrush;
            }
            Check("기동 전류 — 다시 켤 때 차단기가 떨어지기도 한다", trips > 0 && trips < tries, $"{tries}번 다시 켜서 {trips}번 트립");
        }

        // ── 6) 평소에는 물이 차지 않는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.TicksPerDay * 4);
            float maxDepth = w.Ship.Rooms.Max(MoistureSystem.Depth);
            float maxHum = w.Ship.Rooms.Max(r => r.Humidity);
            Check("평소 운항 — 물이 차지 않는다", w.Moisture.Stats.Floods == 0 && maxDepth < 0.05f, $"{w.Moisture.Stats} · 가장 깊은 물 {maxDepth * 12:0.0}cm · 가장 습한 방 {maxHum * 100:0}%");
        }

        // ── 7) 결정론 ──
        {
            uint H() { var w = DayOne(seed, "Mirinae"); w.Moisture.AddWater(w.Ship.RoomsOf(RoomType.Galley).First(), 200f); Run(w, SimTime.Hours(8)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("물이 든 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 물·습기·전기 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
