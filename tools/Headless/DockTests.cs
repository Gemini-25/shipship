using System;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v18.5 도킹 · 난파선 탐사 · 해체 + v18.6 승객: 도킹 전 기밀 · 기압 확인 → 난파선에 우주복으로 들어가 어둠 · 진공 속에서 뒤지고 →
// 전 승무원의 마지막 기록을 찾아 함께 추모 → 잘라 온 자재가 창고에 · 시험을 건너뛰면 고리 옆이 샌다 · 거룻배와 교환 · 같이 손보기 · 손님 ·
// 불 앞에서 공황에 빠진 승객을 자원봉사 승객이 이끈다 · 승객 불만 · 결정론 · 성능.
public static partial class Program
{
    private static int RunDockTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"도킹 · 난파선 · 승객 점검 (v18.5 · v18.6) · 시드 {seed}\n");
        string? only = Environment.GetEnvironmentVariable("DOCKONLY"); // 고칠 때 한 장면만
        bool Sec(char k) => only == null || only.Contains(k);

        // ── 1) 난파선: 기밀 확인 → 탐사 → 기록 → 추모 → 해체 ──
        if (Sec('1'))
        {
            var w = DayOne(seed, "Hanbit");
            int h0 = w.Ship.Grid.Height;
            DockingSystem.HazardMul = 3f;
            var v = w.Dock.Arrive(DockKind.Wreck, "에오스호");
            Check("난파선이 붙는다 (격자가 아래로 · 통로 · 방 셋)", v != null && v.Rooms.Count == 3 && w.Ship.Grid.Height >= h0 && v.Moored.Count > 10, $"높이 {h0}→{w.Ship.Grid.Height} · 칸 {v?.Moored.Count}");
            if (v == null) { DockingSystem.HazardMul = 1f; return 1; }
            v.SealQ = 0.35f; // 휘어 있는 고리 — 시험해야 안다
            int stored0 = w.Ship.Containers.Sum(f => f.Storage!.Count(ItemKind.Plate) + f.Storage!.Count(ItemKind.Structure) + f.Storage!.Count(ItemKind.Cable) + f.Storage!.Count(ItemKind.Electronics) + f.Storage!.Count(ItemKind.Sensor) + f.Storage!.Count(ItemKind.Pump));
            bool sealSeen = false, insideSeen = false, outBefore = false;
            int maxInside = 0;
            long t0 = w.Tick;
            while (w.Tick - t0 < SimTime.TicksPerDay * 3)
            {
                w.Step();
                if (v.Stage == DockStage.Seal && v.SealBy >= 0) sealSeen = true;
                if (v.Stage == DockStage.Seal && w.Crew.Any(c => w.Dock.InWreck(c, v))) outBefore = true;
                int inside = w.Crew.Count(c => !c.Dead && w.Dock.WreckRoomOf(c) != null);
                if (inside > 0) insideSeen = true;
                maxInside = Math.Max(maxInside, inside);
                if (v.Mourned && v.Stage == DockStage.Gone) break;
            }
            DockingSystem.HazardMul = 1f;
            var st = w.Dock.Stats;
            Check("도킹 전 기밀 확인 (에어락 안쪽에서 압력 유지 시험)", sealSeen && st.SealChecks >= 1 && v.Checked, $"시험 {st.SealChecks} · 확인 {v.Checked}");
            Check("덜 물린 고리를 찾아 다시 물렸다 (새지 않았다)", v.Reseats >= 1 && !v.Leaked, $"다시 {v.Reseats} · 샘 {v.Leaked}");
            Check("주컴퓨터가 압력계를 읽었다 (분당 강하 · 저쪽 기압)", v.LeakRead >= 0f && st.ComputerReads >= 2 && w.Automation.Book.Acts.Any(a => a.Observe.Contains("도킹 고리 압력")), $"읽음 {st.ComputerReads} · 강하 {v.LeakRead:0.0}");
            Check("확인 전엔 아무도 난파선에 들어가지 않았다", !outBefore, "");
            Check("우주복으로 난파선 안에 들어갔다 (보이는 탐사)", insideSeen && st.Trips >= 2, $"나간 횟수 {st.Trips} · 동시 최대 {maxInside}");
            Check("방 셋을 다 뒤졌다 (어둠 속 · 헬멧 등)", v.Rooms.All(r => r.Searched), string.Join(" ", v.Rooms.Select(r => $"{r.Name}{r.Search:0.0}")));
            Check("진공 · 어둠 위험 (잔해에 걸림 · 찢긴 판 · 무너진 격벽)", st.DarkTrips + st.Hurts >= 1, $"걸림 {st.DarkTrips} · 다침 {st.Hurts} · 무너짐 {st.Collapses}");
            Check("전 승무원의 마지막 기록을 찾아 가져왔다", v.Records.Count(r => r.Home) >= 1, string.Join(" · ", v.Records.Where(r => r.Home).Select(r => $"{r.Who} {LastRecord.KindName(r.Kind)}")));
            var mem = w.History.Events.FirstOrDefault(e => e.Kind == HistoryKind.Memory && e.Text.Contains("이름판"));
            Check("함께 추모했다 (모여서 읽고 묵념 · 이름판 · 연대기)", v.Mourned && v.Mourners.Count >= 2 && mem != null, $"{v.Mourners.Count}명 · {mem?.Text}");
            var mourner = v.Mourners.Select(id => w.Crew[id]).FirstOrDefault();
            Check("추모가 사람에게 남았다 (기억 · 가까워짐)", mourner != null && mourner.Memory.Marks.Any(m => m.Text.Contains("마지막 기록")), mourner?.Name ?? "-");
            int stored1 = w.Ship.Containers.Sum(f => f.Storage!.Count(ItemKind.Plate) + f.Storage!.Count(ItemKind.Structure) + f.Storage!.Count(ItemKind.Cable) + f.Storage!.Count(ItemKind.Electronics) + f.Storage!.Count(ItemKind.Sensor) + f.Storage!.Count(ItemKind.Pump));
            Check("해체해 자재를 회수했다 (창고에)", st.Items >= 2 && v.Salvaged.Count > 0 && stored1 > stored0, $"{string.Join(" · ", v.Salvaged.Select(kv => $"{kv.Key} {kv.Value}"))} · 창고 {stored0}→{stored1}");
            Check("다 끝나고 떨어졌다 (통로 칸을 돌려놓음)", v.Stage == DockStage.Gone && !v.Moored.Any(c => w.Paths.MooredCells.Contains(c)), $"{v.Stage}");
            Console.WriteLine($"   기록: {string.Join(" / ", v.Records.Where(r => r.Home).Select(r => r.Text).Take(1))}");
            if (Environment.GetEnvironmentVariable("DOCKDBG") == "1")
                foreach (var e in w.Log.Entries.Where(e => e.Text.Contains("에오스") || e.Text.Contains("우주복") || e.Text.Contains("난파선")).Take(60))
                    Console.WriteLine($"     [{e.Tick / 25}분] {(e.CrewId >= 0 ? w.Crew[e.CrewId].Name : "")}: {e.Text}");
        }

        // ── 2) 시험을 건너뛰고 열면: 고리 옆이 샌다 → 기존 대응 ──
        if (Sec('2'))
        {
            var w = DayOne(seed, "Hanbit");
            var v = w.Dock.Arrive(DockKind.Wreck, "벨라호")!;
            v.SealQ = 0.3f;
            w.Dock.OpenNow(v);
            var ws = w.Ship.WallAt(v.LeakWall);
            Check("시험 없이 열면 고리 옆 외판이 샌다", v.Skipped && v.Leaked && ws != null && ws.Breach > 0f, $"샘 {v.Leaked} · 파공 {ws?.Breach:0.00}");
            Run(w, SimTime.Hours(6));
            Check("새는 곳을 승무원이 막았다 (선체 파공 대응)", ws != null && (ws.Breach <= 0f || ws.Patched), $"파공 {ws?.Breach:0.00} · 봉합 {ws?.Patched}");
        }

        // ── 3) 거룻배: 기압 맞추기 · 물자 교환 · 같이 손보기 · 손님 ──
        if (Sec('3'))
        {
            var w = DayOne(seed, "Hanbit");
            int crew0 = w.Crew.Count;
            var v = w.Dock.Arrive(DockKind.Ship, "하늬 거룻배")!;
            long t0 = w.Tick;
            while (w.Tick - t0 < SimTime.Hours(14) && v.Stage != DockStage.Gone) w.Step();
            var st = w.Dock.Stats;
            Check("다른 배와 도킹: 기밀 확인 · 기압을 맞춘 뒤 열었다", v.Checked && v.Equalized >= 1f && !v.DoorJolt && v.Opened >= 0, $"저쪽 {v.OtherKpa:0}kPa · 튕김 {v.DoorJolt}");
            Check("물자 교환 · 같이 손보기", v.TradeDone && v.Trades.Count > 0 && v.JointDone, $"{string.Join(" · ", v.Trades)} · 같이 {st.Joint}");
            Check("기항지 손님이 승객으로 탔다", w.Passengers.All.Count >= 1 && w.Crew.Count > crew0 && w.Crew.Where(c => c.Passenger).All(c => !c.Rescued), $"승객 {w.Passengers.All.Count}");
        }

        // ── 4) 승객: 불 앞에서 공황 → 자원봉사 승객이 이끈다 · 불만 ──
        if (Sec('4'))
        {
            var w = DayOne(seed, "Hanbit");
            var mess = w.Ship.LiveRooms.First(r => r.Type is RoomType.Mess or RoomType.Lounge);
            var room = w.Ship.LiveRooms.Where(r => r != mess && r.Cells.Count >= 6 && r.Type is RoomType.Quarters or RoomType.Storage or RoomType.Workshop).OrderBy(r => r.Id).First();
            var at = room.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
            var a = w.Passengers.Board(at[0], "구조 캡슐", rescued: true, volunteer: false)!;
            var helper = w.Passengers.Board(at[^1], "하늬 기항지", rescued: false, volunteer: true)!;
            Check("승객은 일을 하지 않는다 (당직 · 비상 배치에서 빠진다)", a.Passenger && !w.CrisisCrew.Bill.Of.ContainsKey(a.Id), $"{a.Name} · {helper.Name}");
            Run(w, SimTime.Minutes(5));
            var fireAt = (a.Room ?? room).Cells.Where(c => w.Ship.IsOpenFloor(c) && c != a.Cell).OrderBy(c => Math.Abs(c.X - a.Cell.X) + Math.Abs(c.Y - a.Cell.Y)).First();
            bool lit = w.Fire.Ignite(fireAt, 0.7f);
            Console.WriteLine($"   불: {lit} · {a.Room?.Name} · 봉사자 {helper.Room?.Name}");
            long t0 = w.Tick;
            bool forced = false;
            while (w.Tick - t0 < SimTime.Hours(1))
            {
                w.Step();
                if (!forced && w.Tick - t0 > SimTime.Minutes(12) && w.Passengers.Stats.Panics == 0)
                {
                    forced = true; // 운이 좋아 아무도 공황에 빠지지 않았으면 — 장면을 만든다
                    w.Passengers.Panic(a, true);
                }
                if (Environment.GetEnvironmentVariable("DOCKDBG") == "1" && (w.Tick - t0) % SimTime.Minutes(2) == 0 && w.Passengers.Stats.Panics > 0 && w.Tick - t0 < SimTime.Minutes(30))
                    Console.WriteLine($"     {(w.Tick - t0) / 25}분 봉사자 {helper.Job?.Label} [{string.Join(", ", (helper.LastEvaluations ?? Array.Empty<Evaluation>()).Take(3).Select(e => e.Activity.Id + ":" + e.Score.ToString("0.00") + " " + e.Reason))}] · {a.Name} 공황 {a.Mind.Panicking(w.Tick)} 거리 {(a.Position - helper.Position).Length():0.0}");
                if (w.Passengers.Stats.Led > 0 && w.Tick - t0 > SimTime.Minutes(30)) break;
            }
            var ps = w.Passengers.Stats;
            Check("훈련 없는 승객이 불 앞에서 공황", ps.Panics >= 1 && !forced, $"공황 {ps.Panics}{(forced ? " (꾸밈)" : "")} · 방송 {ps.Broadcasts}");
            Check("자원봉사 승객이 공황에 빠진 사람의 손을 잡고 이끌었다", ps.Led >= 1 && w.Passengers.Of(helper)!.Led >= 1, $"이끔 {ps.Led}");
            var led = w.Crew.Where(c => w.Passengers.Of(c)?.LedBy == helper.Id).FirstOrDefault();
            Check("이끌린 사람은 공황이 가라앉고 안전한 방으로 · 정이 생겼다", led != null && !led.Mind.Panicking(w.Tick) && led.AffinityTo(helper) > 0.2f && w.Fire.CountIn(led.Room ?? room) == 0, $"{led?.Name} → {led?.Room?.Name} · 정 {led?.AffinityTo(helper):0.00}");
            Check("주컴퓨터가 승객에게 갈 곳을 방송했다", ps.Broadcasts >= 1, "");

            // 불만: 간이침대 · 마음이 상했다 → 승무원(함장 먼저)을 찾아가 따진다
            var pa = w.Passengers.Of(a)!;
            pa.Content = 0.15f; pa.NextGripe = w.Tick;
            var pb = w.Passengers.Of(helper)!;
            // v19 불 뒤엔 다친 몸을 누이고 · 끼니부터 먹고 · 경보 중엔 따지지 않는다 — 네 시간 안에 못 가기도 했다 → 경보가 가라앉은 뒤 여덟 시간까지
            for (int k = 0; k < SimTime.Hours(2) && Crisis.Acting(w); k++) w.Step();
            for (int k = 0; k < 16 && !(ps.Complaints >= 1 && ps.Heard + ps.Brushed >= 1); k++) Run(w, SimTime.Minutes(30));
            Check("승객 불만 (승무원을 찾아가 따졌다 · 들어 주거나 흘려듣는다)", ps.Complaints >= 1 && ps.Heard + ps.Brushed >= 1, $"불만 {ps.Complaints} · 들어 줌 {ps.Heard} · 흘림 {ps.Brushed} · {pa.LastGripe}");
            Check("자원봉사 승객이 마음 상한 승객을 달랬다", ps.Comforts >= 1 || pa.Content > 0.3f, $"달램 {ps.Comforts} · 마음 {pa.Content:0.00}");
            // 같은 불만이 또 쌓이면 주컴퓨터가 길을 낸다
            pa.Content = 0.1f; pa.NextGripe = w.Tick; pa.EasedUntil = w.Tick; // 담요로 달랜 지 하루가 지났다
            Run(w, SimTime.Hours(4));
            Check("같은 불만이 쌓이면 주컴퓨터가 장부에 적는다", ps.Advices >= 1 || ps.Complaints >= 2 && w.Automation.Book.Acts.Any(x => x.Observe.StartsWith("승객 불만")), $"제안 {ps.Advices} · 불만 {ps.Complaints}");
        }

        // ── 5) 결정론 ──
        if (Sec('5'))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint H2()
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(8));
                w.Dock.Arrive(DockKind.Wreck, "에오스호");
                Run(w, SimTime.TicksPerDay);
                return SaveGame.StateHash(w);
            }
            uint a = H(), b = H();
            Check("결정론 (같은 시드 → 같은 지문)", a == b, $"{a:x8} {b:x8}");
            uint c = H2(), d = H2();
            Check("결정론 · 난파선이 붙은 날", c == d, $"{c:x8} {d:x8}");
        }

        // ── 6) 성능: 30인 배 하루 ──
        if (Sec('6'))
        {
            double Day(bool off)
            {
                DockingSystem.Off = PassengerSystem.Off = off;
                var w = World.CreateDefault(seed, 0, "Cheonma");
                Run(w, SimTime.Hours(2));
                if (!off)
                {
                    w.Dock.Arrive(DockKind.Wreck, "에오스호");
                    var at = w.Ship.LiveRooms.First(r => r.Type is RoomType.Mess or RoomType.Lounge).Cells.First(c => w.Ship.IsOpenFloor(c));
                    w.Passengers.Board(at, "구조 캡슐", true); w.Passengers.Board(at, "하늬 기항지", false);
                }
                DockingSystem.UpdateTicks = PassengerSystem.UpdateTicks = 0;
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                DockingSystem.Off = PassengerSystem.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            double t0 = Day(true), t1 = Day(false);
            double own = (DockingSystem.UpdateTicks + PassengerSystem.UpdateTicks) * 1.0 / Stopwatch.Frequency;
            Console.WriteLine($"   성능 (30인 하루): 끔 {t0:0.0}초 · 켬(난파선 · 승객 둘) {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%) · 이 시스템 틱 {own * 1000:0}ms");
            Check("성능 · 30인 배 하루 ±10%", t1 < t0 * 1.10 || own < t1 * 0.02, $"{t0:0.0} → {t1:0.0}초 · 자체 {own * 1000:0}ms");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}건");
        return _fails == 0 ? 0 : 1;
    }
}
