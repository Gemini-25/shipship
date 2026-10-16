using System;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// v19 열 점검 (--heattest): 방마다 열 장부 — 드는 열(사람 · 설비 · 주 컴퓨터) · 선체로 빠지는 열 · 공조.
/// 평소엔 예전처럼 21℃ · 연산이 늘면 컴퓨터실 냉각기 전기가 늘고 · 냉각이 끊기면 과열 → 연산을 줄인다 · 단열은 추위를 막지만 기계 열을 가둔다 · 전기가 끊긴 방은 식는다.
/// </summary>
public static partial class Program
{
    private static int RunHeatTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"열 점검 (v19) · 시드 {seed}\n");

        // 1) 평소: 모든 배 — 전기 · 환기가 든 방은 공조가 맞춘 온도 근처 · 공조가 설계 몫 안에서 (냉방 전기가 더 들지 않는다)
        {
            var off = new System.Collections.Generic.List<string>();
            var heavy = new System.Collections.Generic.List<string>();
            foreach (var t in ShipCatalog.All)
            {
                var w = World.CreateDefault(seed, 0, t.Key);
                Run(w, SimTime.Hours(4));
                foreach (var r in w.Ship.LiveRooms)
                    if (r.Type != RoomType.Corridor && Atmosphere.Vented(r) && MathF.Abs(r.Air.Temperature - Atmosphere.Setpoint(r)) > 3f) off.Add($"{w.Ship.Name} {r.Name} {r.Air.Temperature:0.0}");
                if (w.Air.HvacElectricKw > 0.03f * MathF.Max(1f, w.Power.ReactorLimit)) heavy.Add($"{w.Ship.Name} {w.Air.HvacElectricKw:0.0}/{w.Power.ReactorLimit:0}kW");
            }
            Check("평소 — 어느 배든 전기 · 환기가 든 방은 공조가 맞춘 온도(21℃ · 원자로실 27 · 기관실 24)에서 3℃ 안 · 더 드는 냉방 전기는 원자로 출력의 3% 안", off.Count == 0 && heavy.Count == 0,
                $"{ShipCatalog.All.Count()}척 · 벗어난 방 {off.Count} {string.Join(", ", off.Take(5))} · 냉방 전기가 큰 배 {string.Join(", ", heavy)}");
        }

        // 2) 연산이 늘면 컴퓨터실이 더 달아오르고 → 전용 냉각기 전기가 늘어 전력 수요에 얹힌다 · 냉각이 끊기면 과열 → 연산을 줄인다
        {
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Hours(2));
            var a = w.Automation;
            var cr = a.ComputerBody!.Room;
            float kw0 = cr.HvacKw, heat0 = a.HeatKw(), t0 = cr.Air.Temperature, load0 = a.Load;
            AutomationSystem.TestLoad = MathF.Max(0f, a.Capacity * 0.85f - a.Demand() + AutomationSystem.TestLoad);
            Run(w, SimTime.Minutes(40));
            float kw1 = cr.HvacKw, heat1 = a.HeatKw(), t1 = cr.Air.Temperature, load1 = a.Load;
            Check("연산이 늘면 — 컴퓨터실 열이 늘고 전용 냉각기 전기가 그만큼 늘어 전력 수요에 얹힌다 (방은 시원하게 지킨다)", heat1 > heat0 + 0.6f && kw1 > kw0 + 0.2f && t1 < Atmosphere.Setpoint(cr) + 3f,
                $"연산 {load0 * 100:0} → {load1 * 100:0}% · 컴퓨터 열 {heat0:0.0} → {heat1:0.0}kW · 냉각기 전기 {kw0:0.00} → {kw1:0.00}kW · 컴퓨터실 {t0:0.0} → {t1:0.0}℃");
            cr.VentOpen = false; cr.DamperStuck = true;
            foreach (var d in cr.Doors) { d.Locked = true; d.Openness = 0f; } // 문을 닫아 둔다 (열린 문으로는 옆방과 금방 섞인다)
            long hot = -1, tCut = w.Tick;
            int safe0 = a.Core.SafeModes;
            for (int i = 0; i < 12 * 6 && hot < 0; i++)
            {
                cr.VentOpen = false; cr.DamperStuck = true; // 사람이 댐퍼를 고치러 오기 전 (고치면 다시 식는다)
                Run(w, SimTime.Minutes(10));
                if (a.Core.CoreC > AutomationSystem.OverheatC || a.Core.SafeModes > safe0) hot = w.Tick;
            }
            Run(w, SimTime.Minutes(10));
            Check("냉각이 끊기면 — 컴퓨터 함 안이 버틸 온도를 넘고, 주 컴퓨터가 연산을 줄인다 (안전 모드 · 낮은 순위 모듈부터)", hot >= 0 && (a.Core.SafeMode || a.Suspended.Count > 0),
                $"냉각이 끊긴 지 {(hot < 0 ? "-" : $"{(hot - tCut) / (float)SimTime.TicksPerHour:0.0}시간")}에 넘었다 · 함 안 {a.Core.CoreC:0.0}℃ · 방 {cr.Air.Temperature:0.0}℃ · 안전 모드 {a.Core.SafeMode} · 끈 모듈 {a.Suspended.Count} · 컴퓨터 열 {a.HeatKw():0.0}kW");
            AutomationSystem.TestLoad = 0f;
        }

        // 3) 단열의 맞바꿈: 선체로 빠지는 열이 준다 — 전기가 끊긴 빈 방은 덜 식지만, 냉각이 끊긴 기계 방은 열이 갇혀 더 달아오른다
        {
            var a = World.CreateDefault(seed, 0, "Hanbit");
            var b = World.CreateDefault(seed, 0, "Hanbit");
            Run(a, SimTime.Hours(1)); Run(b, SimTime.Hours(1));
            // 가장 열을 많이 내는 기계 방 (공조 몫이 큰 방)
            Room Busy(World w) => w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r != w.Automation.ComputerBody?.Room && r.Type is not (RoomType.Reactor or RoomType.Engine)).OrderByDescending(r => r.HeatIn / Math.Max(4, r.Volume)).First(); // 열을 가장 많이 내는 기계 방
            Room Quiet(World w) => w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.Furniture.All(f => f.Machine == null || f.Machine.Spec.PowerDraw <= 0.5f) && r != w.Automation.ComputerBody?.Room)
                                   .OrderByDescending(r => w.Air.HullConductance(r) / Math.Max(4, r.Volume)).First(); // 외벽이 가장 많이 닿은 빈 방
            var ma = Busy(a); var mb = b.Ship.Rooms[ma.Id];
            var qa = Quiet(a); var qb = b.Ship.Rooms[qa.Id];
            var ib = b.Ship.LiveRooms.Where(r => r.Type is not (RoomType.Corridor or RoomType.Reactor or RoomType.Engine) && b.Air.HullConductance(r) == 0f && r.HeatIn < 0.5f && r.Furniture.All(f => f.Machine == null || f.Machine.Spec.PowerDraw <= 0.5f) && r != b.Automation.ComputerBody?.Room).OrderBy(r => r.Id).FirstOrDefault(); // 외벽이 없는 안쪽 방
            if (ib != null) { ib.PowerCut = true; foreach (var d in ib.Doors) { d.Locked = true; d.Openness = 0f; } }
            float q0 = qb.Air.Temperature, i0 = ib?.Air.Temperature ?? 21f;
            foreach (var (m, q, w) in new[] { (ma, qa, a), (mb, qb, b) })
            {
                m.VentOpen = false; m.DamperStuck = true; // 기계 방: 냉각이 끊겼다 (덥히기는 그대로)
                q.PowerCut = true; // 빈 방: 전기가 끊겼다
                foreach (var d in m.Doors.Concat(q.Doors)) { d.Locked = true; d.Openness = 0f; } // 문을 닫아 둔다
                foreach (var c in w.Crew.Where(c => c.Room == q || c.Room == m)) c.Position = w.Ship.LiveRooms.First(r => r.Type == RoomType.Corridor).Center;
            }
            ma.Insulated = true; qa.Insulated = true;
            for (int i = 0; i < 6 * 6; i++)
            {
                foreach (var m in new[] { ma, mb }) { m.VentOpen = false; m.DamperStuck = true; } // 고치러 오기 전
                Run(a, SimTime.Minutes(10)); Run(b, SimTime.Minutes(10));
                foreach (var w in new[] { a, b }) foreach (var c in w.Crew.Where(c => c.Room?.Id == qa.Id || c.Room?.Id == ma.Id)) c.Position = w.Ship.LiveRooms.First(r => r.Type == RoomType.Corridor).Center;
            }
            // 식는 폭은 선체 바깥 온도(햇빛 쪽 · 그늘 쪽)에 따라 다르다 — 단열하면 식는 폭이 2/3 아래로 준다
            Check("단열 — 전기가 끊긴 빈 방은 단열하면 덜 식는다", q0 - qb.Air.Temperature > 2f && q0 - qa.Air.Temperature < (q0 - qb.Air.Temperature) * 0.65f,
                $"{qa.Name}: 단열 {q0:0.0} → {qa.Air.Temperature:0.0}℃ ↔ 그대로 {q0:0.0} → {qb.Air.Temperature:0.0}℃ (여섯 시간)");
            Check("단열 — 냉각이 끊긴 기계 방은 단열하면 열이 갇혀 더 달아오른다", ma.Air.Temperature > mb.Air.Temperature + 2f,
                $"{ma.Name} (설비 열 {mb.HeatIn:0.0}kW): 단열 {ma.Air.Temperature:0.0}℃ ↔ 그대로 {mb.Air.Temperature:0.0}℃");
            // 4) 정전: 외벽이 닿은 방은 선체로 빨리 식고 · 안쪽 방은 이웃 방 덕에 덜 식는다
            Check("정전 — 전기가 끊긴 방은 외벽으로 식고 (여섯 시간에 4℃ 넘게), 외벽이 없는 안쪽 방은 덜 식는다", q0 - qb.Air.Temperature > 4f && (ib == null || i0 - ib.Air.Temperature < (q0 - qb.Air.Temperature) * 0.6f),
                $"{qb.Name}(외벽) {q0:0.0} → {qb.Air.Temperature:0.0}℃ · {ib?.Name ?? "-"}(안쪽) {i0:0.0} → {ib?.Air.Temperature:0.0}℃");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 열" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
