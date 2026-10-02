using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v16.7 이동식 장비: 작업등 · 히터 · 선풍기 · 공기청정기 · 이동식 배터리 · 양수기 · 카트
public static partial class Program
{
    private static int RunPortableTest(int seed)
    {
        _fails = 0;
        static bool Sec(int n) => Environment.GetEnvironmentVariable("PSEC") is not string s || s.Split(',').Contains(n.ToString()); // 개발용: 일부만
        Console.WriteLine($"이동식 장비 점검 (v16.7) · 시드 {seed}\n");

        // ── 0) 시작 장비: 모든 배(생성 배 포함) 창고에 배 크기에 맞게 ──
        if (Sec(0))
        {
            var notes = new List<string>();
            bool ok = true;
            var bySize = new List<(int crew, int n)>();
            foreach (var t in ShipCatalog.All)
            {
                World w;
                try { w = World.CreateDefault(seed, 0, t.Key); }
                catch (Exception e) { notes.Add($"{t.Key} 배 생성 실패({e.GetType().Name} — 배 담당)"); continue; }
                var ds = w.Portable.Devices;
                ok &= Enum.GetValues<PortableKind>().All(k => ds.Any(d => d.Kind == k)) && ds.All(d => d.Stored && w.Ship.RoomAt(d.Home) != null) ;
                bySize.Add((t.Crew, ds.Count));
                notes.Add($"{t.Key} {ds.Count}");
            }
            var sorted = bySize.OrderBy(x => x.crew).ThenBy(x => x.n).ToList();
            for (int i = 1; i < sorted.Count; i++) if (sorted[i].crew > sorted[i - 1].crew + 4) ok &= sorted[i].n >= sorted[i - 1].n; // 큰 배일수록 많다
            var g = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(16, seed));
            ok &= Enum.GetValues<PortableKind>().All(k => g.Portable.Devices.Any(d => d.Kind == k)) && g.Portable.Devices.All(d => d.Stored);
            notes.Add($"생성 배(16명) {g.Portable.Devices.Count}");
            Check("시작 장비 — 모든 배 · 생성 배의 창고에 일곱 가지가 배 크기에 맞게", ok, string.Join(" · ", notes));
        }

        // ── 1) 정전된 식당: 이동식 배터리와 작업등 하나를 켜 놓고 모여 식사 ──
        if (Sec(1))
        {
            var w = DayOne(seed, "Hanbit");
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            mess.PowerCut = true;
            PortableDevice? lamp = null;
            for (int i = 0; i < 4 * 12 && lamp == null; i++)
            {
                Run(w, SimTime.Minutes(5));
                lamp = w.Portable.Devices.FirstOrDefault(d => d.Kind == PortableKind.WorkLamp && d.Placed && d.Running && w.Ship.RoomAt(d.At) == mess);
            }
            bool onBattery = lamp is { Plug: PortablePlug.Battery, Source.Kind: PortableKind.Battery };
            Check("정전된 식당 — 창고에서 이동식 배터리와 작업등을 가져와 켠다 (어둡지 않다)", lamp != null && onBattery && !mess.Powered && !mess.Dark,
                $"작업등 {(lamp == null ? "없음" : $"{lamp.Plug} · 세기 {lamp.LightIntensity:0.00} · 반경 {lamp.LightRadius:0.0}")} · 식당 전기 {mess.Powered} · 어둠 {mess.Dark} · {w.Portable.Stats.Summary()}");
            foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.25f);
            float before = lamp?.Source?.Charge ?? 0f;
            int most = 0;
            for (int i = 0; i < 24; i++)
            {
                Run(w, SimTime.Minutes(5));
                most = Math.Max(most, w.Crew.Count(c => c.Room == mess && c.Job?.Activity is EatActivity && !c.IsMoving));
            }
            var st = w.Portable.Stats;
            Check("등불 아래 모여 식사 — 정전된 식당에서 작업등 하나 아래 여럿이 먹는다", st.LampMeals >= 2 && st.Gatherings >= 1 && most >= 2,
                $"등불 아래 식사 {st.LampMeals} · 모임 {st.Gatherings} · 한때 {most}명 · 배터리 {before:0.00}→{lamp?.Source?.Charge ?? 0f:0.00}kWh");
        }

        // ── 2) 작업등을 비추다 몸에 가리면 옮긴다 ──
        if (Sec(2))
        {
            var w = DayOne(seed, "Hanbit");
            bool done = false;
            string detail = "일하는 사람을 못 찾음";
            for (int attempt = 0; attempt < 4 && !done; attempt++)
            {
                CrewMember? worker = null;
                for (int i = 0; i < 180 && worker == null; i++)
                {
                    Run(w, SimTime.Minutes(1));
                    worker = w.Crew.FirstOrDefault(c => c.CanAct && c.Suit == null && c.Job?.Current is WorkToil { Progress: < 0.4f } && c.Room is { Type: not RoomType.Corridor }
                                                        && PortableSystem.WorkPoint(c) is Vector2 wp && (wp - c.Position).Length() > 0.6f);
                }
                if (worker == null) continue;
                var room = worker.Room!;
                var work = PortableSystem.WorkPoint(worker)!.Value;
                room.LightsOut = true;
                room.LightsOutSince = w.Tick;
                // 사람 등 뒤 (등 → 사람 → 작업 위치가 한 줄) — 가까운 칸부터
                Cell? behind = room.Cells.Where(cell => cell != worker.Cell && w.Ship.IsWalkable(cell) && PortableSystem.ShadowOf(cell.Center, work, worker.Position))
                    .OrderBy(cell => (cell.Center - worker.Position).LengthSquared()).Cast<Cell?>().FirstOrDefault();
                if (behind is not Cell b) { detail = $"{worker.Name} 등 뒤에 빈칸 없음"; Run(w, SimTime.Minutes(20)); continue; }
                var lamp = w.Portable.Devices.First(d => d.Kind == PortableKind.WorkLamp && d.Stored);
                w.Portable.PlaceNow(lamp, b, worker, $"work:{room.Id}:{worker.Id}", user: worker, aim: work);
                Run(w, World.SystemInterval * 2);
                bool shadowed = lamp.Shadowed;
                float mul = w.Portable.LampWorkMul(worker);
                int moves = w.Portable.Stats.LampMoves;
                Run(w, SimTime.Minutes(8));
                bool moved = w.Portable.Stats.LampMoves > moves && lamp.At != b;
                bool clear = !PortableSystem.ShadowOf(lamp.At.Center, work, worker.Position);
                detail = $"{worker.Name} · {room.Name} · 가림 {shadowed}(일 속도 ×{mul:0.00}) → 옮김 {moved} ({b}→{lamp.At}) · 지금 가림 {!clear}";
                done = shadowed && mul < 1f && moved && clear;
            }
            Check("작업등을 비추다 몸에 가리면 옮긴다 (등 · 사람 · 작업 위치로 판정)", done, detail);
        }

        // ── 3) 히터를 한 회로에 여럿 꽂으면 차단기가 떨어진다 → 뽑거나 옆 방 콘센트로 → 차단기를 올린다 ──
        if (Sec(3))
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.RoomsOf(RoomType.Quarters).First(r => PortableSystem.OutletOk(r));
            int circuit = room.Circuit;
            w.Automation.Reboot("시험 — 재부팅 중이라 콘센트 부하를 못 본다", 40f); // 미리 알려 줄 컴퓨터가 없다
            Run(w, SimTime.Minutes(1));
            var spots = room.Cells.Where(c => w.Ship.IsOpenFloor(c)).Take(3).ToList();
            var heaters = w.Portable.Devices.Where(d => d.Kind == PortableKind.Heater).Take(3).ToList();
            while (heaters.Count < 3) heaters.Add(w.Portable.Add(PortableKind.Heater, heaters[0].Home));
            for (int i = 0; i < 3; i++) w.Portable.PlaceNow(heaters[i], spots[i % spots.Count], null, "hold", outlet: room);
            var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
            bool Tripped() => panel.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == circuit);
            bool tripped = false;
            long t0 = w.Tick;
            for (int i = 0; i < 30 && !tripped; i++) { Run(w, SimTime.Minutes(1)); tripped = Tripped(); }
            Check("히터 셋을 한 회로에 꽂으면 몇 분 뒤 차단기가 떨어진다 (주 컴퓨터 재부팅 중 — 아무도 미리 모른다)", tripped && w.Portable.Stats.Trips >= 1 && w.Portable.Learned(circuit),
                $"{PowerGrid.CircuitName(circuit)} 회로 · 히터 {heaters.Count}대 {w.Portable.ProjectedKw(circuit):0.0}kW (콘센트 {PortableSystem.OutletCapKw}kW) · {(w.Tick - t0) / (float)SimTime.TicksPerHour * 60f:0}분");
            for (int i = 0; i < 36 && (Tripped() || w.Portable.ProjectedKw(circuit) > PortableSystem.OutletCapKw); i++) Run(w, SimTime.Minutes(10));
            Run(w, SimTime.Minutes(30));
            var st = w.Portable.Stats;
            // v16.20 원격으로 되는 건 컴퓨터가 직접: 재부팅이 끝난 주 컴퓨터가 사람보다 먼저 스마트 콘센트로 원인을 끊을 수 있다
            var tr = w.Automation.Triage;
            Check("차단기를 겪으면 히터를 뽑거나 다른 회로로 옮기고(또는 주 컴퓨터가 그 콘센트를 끊고) · 차단기를 올린다 (다시 안 떨어진다)",
                st.Unplugged + tr.CauseCuts >= 1 && !Tripped() && w.Portable.ProjectedKw(circuit) <= PortableSystem.OutletCapKw && room.Powered,
                $"차단 {st.Trips} · 뽑음 {st.Unplugged} (옆 방으로 {st.Rerouted}) · 컴퓨터가 끊음 {tr.CauseCuts} · {PowerGrid.CircuitName(circuit)} 회로 {w.Portable.ProjectedKw(circuit):0.0}kW · 배전반 수요 {w.Power.CircuitDemand(circuit):0.0}kW · 방 전기 {room.Powered}");
        }

        // ── 4) 침수된 방에 양수기를 가져와 물을 퍼낸다 → 다 쓰면 회수 (또는 잊고 남음) ──
        if (Sec(4))
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.RoomsOf(RoomType.Galley).First();
            w.Moisture.AddWater(room, room.Cells.Count * 20f * 0.5f);
            PortableDevice? pump = null;
            for (int i = 0; i < 6 * 12 && pump == null; i++)
            {
                Run(w, SimTime.Minutes(5));
                pump = w.Portable.Devices.FirstOrDefault(d => d.Kind == PortableKind.Pump && d.Placed && d.Running && w.Ship.RoomAt(d.At) == room);
            }
            string how = pump == null ? "없음" : $"{pump.Plug}{(pump.Outlet != null ? $"({pump.Outlet.Name})" : "")} · 호스 {(pump.HoseTo != null ? "문 너머로" : "없음")}";
            float noise = 0f;
            for (int i = 0; i < 8 * 6 && MoistureSystem.Depth(room) > 0.02f; i++) { Run(w, SimTime.Minutes(10)); if (pump?.Running == true) noise = MathF.Max(noise, room.Noise); }
            Check("양수기 소리가 방에 퍼진다 (인접성 소음 — 잠을 깨운다)", noise > 0.3f, $"{room.Name} 소음 {noise:0.00}");
            var st = w.Portable.Stats;
            Check("침수된 방에 양수기를 가져와 물을 퍼낸다", pump != null && st.PumpedL > 20f && MoistureSystem.Depth(room) < 0.06f,
                $"양수기 {how} · 양수기로 퍼냄 {st.PumpedL:0}L · 모두 퍼냄 {w.Moisture.Stats.Pumped:0}L · 남은 물 {room.Flood:0}L");
            for (int i = 0; i < 8 * 6 && pump != null && !(pump.Stored || pump.Forgotten); i++) Run(w, SimTime.Minutes(10));
            Check("다 쓰면 창고에 돌려놓는다 — 잊고 두기도 한다", pump != null && (pump.Stored || pump.Forgotten),
                $"양수기 {(pump == null ? "?" : pump.Stored ? "창고" : pump.Forgotten ? "잊고 둠" : "그대로")} · 회수 {st.Returned} · 잊음 {st.Forgotten}");
        }

        // ── 5) 잊고 오래 둔 장비 → 정식 시설로 (개조 제안) ──
        if (Sec(5))
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.RoomsOf(RoomType.Lounge).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Mess).First();
            var lamp = w.Portable.Devices.First(d => d.Kind == PortableKind.WorkLamp && d.Stored);
            var at = room.Cells.First(c => w.Ship.IsOpenFloor(c));
            w.Portable.PlaceNow(lamp, at, w.Crew[0], "hold");
            lamp.PlacedSince = w.Tick - SimTime.TicksPerDay * 4L;
            var plan = PortableSystem.Candidates(w).FirstOrDefault(p => p.Circuit == (int)FurnitureType.EmergencyLight && p.Target.Room == room);
            Check("오래 같은 자리에 둔 작업등 → 정식 비상등을 다는 개조 제안", plan != null, plan?.Why ?? "제안 없음");
        }

        // ── 6) 먼저 쓰는 사람이 있으면 기다리거나 다른 것을 ──
        if (Sec(6))
        {
            var w = DayOne(seed, "Hanbit");
            var shop = w.Ship.RoomsOf(RoomType.Workshop).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Lounge).First();
            var cells = shop.Cells.Where(c => w.Ship.IsOpenFloor(c)).ToList();
            int k = 0;
            foreach (var d in w.Portable.Devices.Where(d => d.Kind is PortableKind.WorkLamp or PortableKind.Purifier).ToList())
                w.Portable.PlaceNow(d, cells[k++ % cells.Count], w.Crew[0], "hold");
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            mess.PowerCut = true;
            for (int i = 0; i < 24 && w.Portable.Stats.Waits == 0; i++) Run(w, SimTime.Minutes(5));
            int waits = w.Portable.Stats.Waits;
            var freed = w.Portable.Devices.First(d => d.Kind == PortableKind.WorkLamp);
            freed.Purpose = null; // 쓰던 사람이 다 썼다
            bool reused = false;
            for (int i = 0; i < 36 && !reused; i++) { Run(w, SimTime.Minutes(5)); reused = freed.Placed && w.Ship.RoomAt(freed.At) == mess && freed.Running; }
            // 공기청정기가 다 쓰는 중이면 선풍기로
            var smoky = mess; // 정전된 식당 (환기도 멎었다) 에 연기가 고였다
            bool swapped = false;
            for (int i = 0; i < 36 && !swapped; i++)
            {
                smoky.Air.Smoke = MathF.Max(smoky.Air.Smoke, 0.3f);
                Run(w, SimTime.Minutes(5));
                swapped = w.Portable.Devices.Any(d => d.Kind == PortableKind.Fan && d.Placed && w.Ship.RoomAt(d.At) == smoky);
            }

            Check("먼저 쓰는 사람이 있으면 기다렸다가 풀리면 가져다 쓰고 · 청정기가 없으면 선풍기로", waits >= 1 && reused && swapped && w.Portable.Stats.Swaps >= 1,
                $"기다림 {waits} · 풀린 작업등을 식당으로 {reused} · 선풍기로 {swapped} ({w.Portable.Stats.Swaps})");
        }

        // ── 7) 상호작용: 히터 × 침구 × 불 · 젖은 케이블 × 누전 · 카트 × 대피 ──
        if (Sec(7))
        {
            // a) 침대 곁에 켜 둔 히터 → 불 → 그 뒤로 히터 자리는 침구 곁을 피한다
            var w = DayOne(seed, "Hanbit");
            float keep = PortableSystem.HeaterFireRate;
            PortableSystem.HeaterFireRate = 3f;
            var q = w.Ship.RoomsOf(RoomType.Quarters).First(r => PortableSystem.OutletOk(r) && r.Furniture.Any(f => f.Type == FurnitureType.Bed));
            var bedSide = q.Cells.First(c => w.Ship.IsOpenFloor(c) && Cell.Dirs8.Any(d => w.Ship.FurnitureAt(c + d)?.Type == FurnitureType.Bed));
            var heater = w.Portable.Devices.First(d => d.Kind == PortableKind.Heater);
            w.Portable.PlaceNow(heater, bedSide, null, "hold", outlet: q);
            for (int i = 0; i < 18 && w.Portable.Stats.HeaterFires == 0; i++) Run(w, SimTime.Minutes(5));
            PortableSystem.HeaterFireRate = keep;
            int fires = w.Fire.Count;
            Run(w, SimTime.Minutes(10));
            Check("히터 × 침구 × 불 — 침대 곁에 켜 둔 히터에서 불이 붙고 · 불길에 장비가 망가진다", w.Portable.Stats.HeaterFires >= 1 && (fires > 0 || w.History.Fires > 0),
                $"히터 불 {w.Portable.Stats.HeaterFires} · 불 칸 {fires} · 히터 {(heater.Broken ? "망가짐" : heater.Running ? "돈다" : "꺼짐")} · 장비 고장 {w.Portable.Stats.Breakdowns}");

            // b) 물에 잠긴 케이블 → 누전 → 차단기
            var w2 = DayOne(seed, "Hanbit");
            float keepW = PortableSystem.WetCableRate;
            PortableSystem.WetCableRate = 40f;
            var galley = w2.Ship.RoomsOf(RoomType.Galley).First();
            var next = galley.Doors.Select(d => d.RoomA == galley ? d.RoomB : d.RoomA).First(r => r != null && PortableSystem.OutletOk(r))!;
            w2.Moisture.AddWater(galley, galley.Cells.Count * 20f * 0.4f);
            var pump = w2.Portable.Devices.First(d => d.Kind == PortableKind.Pump);
            w2.Portable.PlaceNow(pump, galley.Cells.First(c => w2.Ship.IsOpenFloor(c)), null, "hold", outlet: next);
            var panel = w2.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
            bool trip = false;
            for (int i = 0; i < 24 && w2.Portable.Stats.WetTrips == 0; i++) { Run(w2, SimTime.Minutes(1)); trip |= panel.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == next.Circuit); }
            PortableSystem.WetCableRate = keepW;
            Check("물 × 전기 — 물에 잠긴 양수기 케이블에서 누전 → 옆 방 회로 차단기가 떨어진다", w2.Portable.Stats.WetTrips >= 1 && trip,
                $"누전 {w2.Portable.Stats.WetTrips} · 감전 {w2.Portable.Stats.CableShocks} · {next.Name} {PowerGrid.CircuitName(next.Circuit)} 회로 {(trip ? "차단" : "살아 있음")} · 주방 물 {MoistureSystem.Depth(galley):0.00} · {next.Name} 물 {MoistureSystem.Depth(next):0.00} 콘센트 {PortableSystem.OutletOk(next)} · 양수기 {pump.Plug} {pump.On} {pump.Placed} {pump.Outlet?.Name}");

            // c) 통로에 세워 둔 카트 → 뛰어 달아나던 사람이 걸려 늦는다
            var w3 = DayOne(seed, "Hanbit");
            var runner = w3.Crew.First(c => c.CanAct && !c.Outside);
            var far = w3.Ship.LiveRooms.Where(r => r.Type == RoomType.Corridor).SelectMany(r => r.Cells).Where(c => w3.Ship.IsWalkable(c))
                .OrderByDescending(c => (c.Center - runner.Position).LengthSquared()).First();
            Locomotion.SetDestination(runner, w3, far);
            var path = runner.Path!;
            var cartCell = path[Math.Min(runner.PathIndex, path.Count - 1)];
            var cart = w3.Portable.Devices.First(d => d.Kind == PortableKind.Cart);
            w3.Portable.PlaceNow(cart, cartCell, null, "hold");
            w3.Portable.Update(0f);
            float walk = w3.Portable.SqueezeMul(runner, path);
            runner.Dashing = true;
            float dash = w3.Portable.SqueezeMul(runner, path);
            runner.Dashing = false;
            Check("카트 × 대피 — 통로의 카트를 비켜 가고, 뛰어 달아나던 사람은 걸려 늦는다", walk < 1f && dash < walk && w3.Portable.Stats.CartSnags >= 1,
                $"걷기 ×{walk:0.00} · 뛰기 ×{dash:0.00} · 걸림 {w3.Portable.Stats.CartSnags}");
        }

        // ── 9) 주 컴퓨터 × 승무원: 콘센트 부하로 과부하를 예측해 방송 → 들은 사람이 차단기 전에 뽑는다 ──
        if (Sec(9))
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess && PortableSystem.OutletOk(r)).OrderBy(r => r.Id).First();
            var cells = room.Cells.Where(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null).ToList();
            var heaters = w.Portable.Devices.Where(d => d.Kind == PortableKind.Heater).Take(2).ToList();
            var pump = w.Portable.Devices.First(d => d.Kind == PortableKind.Pump);
            int k = 0;
            foreach (var d in heaters.Append(pump)) w.Portable.PlaceNow(d, cells[(k++ * 3) % cells.Count], null, "hold", outlet: room);
            int circuit = room.Circuit;
            bool tripped = false;
            ComputerAct? act = null;
            for (int i = 0; i < 40 && (w.Portable.Stats.HeededWarns == 0 || act == null); i++)
            {
                Run(w, SimTime.Minutes(1));
                tripped |= w.Portable.Stats.Trips > 0;
                act ??= w.Automation.Book.Acts.LastOrDefault(a => a.Kind == ActKind.Advice && a.Key == $"pw:over:{circuit}");
            }
            var st = w.Portable.Stats;
            Check("주 컴퓨터 — 콘센트 부하로 과부하를 예측해 방송한다 (무엇이 꽂혔는지는 모른다 · 다섯 칸 기록)", st.ComputerWarns >= 1 && act != null,
                act == null ? $"기록 없음 · 예측 {st.ComputerWarns}" : $"관찰: {act.Observe} · 판단: {act.Judge} · 요청: {act.Request}");
            Check("승무원 — 방송을 들은 사람이 차단기가 떨어지기 전에 뽑는다", st.HeededWarns >= 1 && !tripped,
                $"듣고 뽑음 {st.HeededWarns} · 차단기 {(tripped ? "떨어짐" : "그대로")} · {PowerGrid.CircuitName(circuit)} 회로 {w.Portable.CircuitKw(circuit):0.0}kW · {st.LinkSummary()}");
        }

        // ── 14) 냄새 × 승무원 (컴퓨터 없이): 과부하로 달아오른 콘센트 → 피복 타는 냄새 → 따라온 사람이 그 자리에서 뽑는다 ──
        if (Sec(14))
        {
            var w = DayOne(seed, "Hanbit");
            w.Automation.Reboot("시험 — 재부팅 중이라 콘센트 부하를 못 본다", 60f);
            Run(w, SimTime.Minutes(1));
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess && PortableSystem.OutletOk(r)).OrderBy(r => r.Id).First();
            var cells = room.Cells.Where(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null).ToList();
            int k = 0;
            foreach (var d in w.Portable.Devices.Where(d => d.Kind == PortableKind.Heater).Take(2).Append(w.Portable.Devices.First(d => d.Kind == PortableKind.Pump)).ToList())
            {
                d.Dust = 0f; // 먼지 냄새와 헷갈리지 않게
                w.Portable.PlaceNow(d, cells[(k++ * 3) % cells.Count], null, "hold", outlet: room);
            }
            float burnt = 0f;
            for (int i = 0; i < 30 && w.Portable.Stats.HotOutlets == 0 && w.Portable.Stats.Trips == 0; i++) { Run(w, SimTime.Minutes(1)); burnt = MathF.Max(burnt, w.Smells.Level(room, SmellKind.Burnt)); }
            var st = w.Portable.Stats;
            Check("냄새 × 승무원 — 컴퓨터가 멎어도 달아오른 콘센트의 피복 타는 냄새를 따라온 사람이 그 자리에서 뽑는다 (차단기 전에)",
                st.ComputerWarns == 0 && st.HotOutlets >= 1 && st.Unplugged >= 1 && st.Trips == 0,
                $"{room.Name} 탄내 최고 {burnt:0.00} · 뜨거운 콘센트 {st.HotOutlets} · 뽑음 {st.Unplugged} · 차단 {st.Trips} · 컴퓨터 예측 {st.ComputerWarns} · {PowerGrid.CircuitName(room.Circuit)} 회로 {w.Portable.CircuitKw(room.Circuit):0.0}kW");
        }

        // ── 10) 주 컴퓨터: 빈 방에서 오래 나가는 콘센트 부하 → 켜 둔 히터로 짚는다 → 가서 끈다 ──
        if (Sec(10))
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.Minutes(10));
            var q = w.Ship.RoomsOf(RoomType.Quarters).Where(r => PortableSystem.OutletOk(r) && r.DataLinked && !w.Crew.Any(c => c.Room == r) && w.Automation.Belief.PeopleIn(r) == 0).OrderBy(r => r.Id).FirstOrDefault()
                    ?? w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && PortableSystem.OutletOk(r) && r.DataLinked && !w.Crew.Any(c => c.Room == r) && w.Automation.Belief.PeopleIn(r) == 0).OrderBy(r => r.Id).First();
            var heater = w.Portable.Devices.First(d => d.Kind == PortableKind.Heater);
            w.Portable.PlaceNow(heater, q.Cells.First(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null), w.Crew[0], "hold", outlet: q);
            for (int i = 0; i < 24 && w.Portable.Stats.HeaterOffs == 0; i++) Run(w, SimTime.Minutes(5));
            var st = w.Portable.Stats;
            var act = w.Automation.Book.Acts.LastOrDefault(a => a.Key == $"pw:heat:{heater.Id}");
            Check("주 컴퓨터 — 빈 방(문 감지기)에서 콘센트 부하가 오래 나가면 켜 둔 히터로 짚어 끄라 한다 · 들은 사람이 가서 끈다",
                st.HeaterWarns >= 1 && act != null && st.HeaterOffs >= 1 && !heater.On,
                $"{q.Name} · 경고 {st.HeaterWarns} · 껐음 {st.HeaterOffs} · 히터 {(heater.On ? "켜짐" : "꺼짐")} · {(act == null ? "기록 없음" : $"관찰: {act.Observe} · 판단: {act.Judge}")}");
        }

        // ── 11) 주 컴퓨터: 창고 충전대의 빈 자리 → 잊은 장비를 떠올려 돌려놓는다 ──
        if (Sec(11))
        {
            var w = DayOne(seed, "Hanbit");
            var lounge = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess or RoomType.Workshop).OrderBy(r => r.Id).First();
            var lamp = w.Portable.Devices.First(d => d.Kind == PortableKind.WorkLamp);
            w.Portable.PlaceNow(lamp, lounge.Cells.First(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null), w.Crew[0], $"dark:{lounge.Id}");
            lamp.On = false;
            lamp.PlacedSince = 0; // 오래전에 꺼내 둔 것
            lamp.DoneSince = w.Tick - SimTime.Hours(3);
            lamp.Forgotten = true;
            int calls0 = w.Portable.Stats.InventoryCalls;
            for (int i = 0; i < 48 && !lamp.Stored; i++) Run(w, SimTime.Minutes(10));
            var st = w.Portable.Stats;
            var act = w.Automation.Book.Acts.LastOrDefault(a => a.Key == "pw:inv");
            Check("주 컴퓨터 — 창고 충전대 빈 자리로 하루 넘게 안 돌아온 장비를 알리고 · 잊은 사람이 떠올려 창고에 돌려놓는다",
                st.InventoryCalls > calls0 && st.InventoryFound >= 1 && lamp.Stored,
                $"알림 {st.InventoryCalls} · 떠올림 {st.InventoryFound} · 작업등 {(lamp.Stored ? "창고" : lamp.Forgotten ? "잊힌 채" : "밖")} · {(act == null ? "기록 없음" : $"관찰: {act.Observe} · 판단: {act.Judge}")}");
        }

        // ── 12) 냄새 × 승무원: 창고에 오래 둔 히터를 켜면 먼지 타는 냄새 → 냄새를 따라온 사람이 알아챈다 ──
        if (Sec(12))
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess && PortableSystem.OutletOk(r)).OrderBy(r => r.Id).First();
            var heater = w.Portable.Devices.First(d => d.Kind == PortableKind.Heater);
            float dust = heater.Dust;
            w.Portable.PlaceNow(heater, room.Cells.First(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null), null, "hold", outlet: room);
            float burnt = 0f;
            for (int i = 0; i < 30 && w.Portable.Stats.DustSniffs == 0; i++) { Run(w, SimTime.Minutes(1)); burnt = MathF.Max(burnt, w.Smells.Level(room, SmellKind.Burnt)); }
            Check("냄새 × 승무원 — 오래 둔 히터를 켜면 열선 먼지가 타는 냄새가 나고, 냄새를 따라온 사람이 까닭을 알아챈다",
                dust > 0.9f && burnt > SmellSystem.Threshold(SmellKind.Burnt) && w.Portable.Stats.DustSniffs >= 1,
                $"먼지 {dust:0.00}→{heater.Dust:0.00} · {room.Name} 탄내 최고 {burnt:0.00} · 알아챔 {w.Portable.Stats.DustSniffs} · 냄새 {w.Smells.Stats.Summary()}");
        }

        // ── 13) 배 본체: 얼어붙은 방(서리) × 히터 → 녹은 물웅덩이 → 바닥 케이블에 물이 스민다 · 양수기 호스 끝은 바닥을 적신다 ──
        if (Sec(13))
        {
            var w = DayOne(seed, "Hanbit");
            var cold = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Workshop or RoomType.Lounge && r.Doors.Any(d => (d.RoomA == r ? d.RoomB : d.RoomA) is Room n && PortableSystem.OutletOk(n)))
                .OrderBy(r => r.Id).First();
            var src = cold.Doors.Select(d => d.RoomA == cold ? d.RoomB : d.RoomA).First(n => n != null && PortableSystem.OutletOk(n))!;
            for (int i = 0; i < 6; i++) { cold.Air.Temperature = -6f; Run(w, SimTime.Minutes(1)); }
            var spot = cold.Cells.First(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null && w.Portable.Devices.All(d => d.At != c));
            float frost0 = w.Body.Mark(spot, CellMark.Frost);
            var heater = w.Portable.Devices.First(d => d.Kind == PortableKind.Heater);
            w.Portable.PlaceNow(heater, spot, null, "hold", outlet: src);
            float wetMax = 0f;
            for (int i = 0; i < 40 && heater.Soak <= 0f; i++) { Run(w, SimTime.Minutes(2)); wetMax = MathF.Max(wetMax, w.Body.Mark(spot, CellMark.Wet)); }
            Check("배 본체 — 얼어붙은 방에 히터를 켜면 서리가 녹아 물웅덩이가 되고, 그 칸 케이블에 물이 스민다",
                frost0 > 0.2f && w.Body.Mark(spot, CellMark.Frost) < frost0 && wetMax > 0.3f && heater.Soak > 0f,
                $"{cold.Name} 서리 {frost0:0.00}→{w.Body.Mark(spot, CellMark.Frost):0.00} · 물웅덩이 {wetMax:0.00} · 케이블 스밈 {heater.Soak:0.000} · 방 {cold.Air.Temperature:0}℃");
        }

        // ── 8) 결정론 ──
        if (Sec(8))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드는 같은 결과", a == b, $"{a:x8} / {b:x8}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 이동식 장비 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
