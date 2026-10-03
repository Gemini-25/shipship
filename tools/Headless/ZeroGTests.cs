using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v18.4 무중력 · v18.2 선내 생태계 · v18.3 배수 점검 (--zerogtest).
public static partial class Program
{
    private static int RunZeroGTest(int seed)
    {
        _fails = 0;
        // ── 1) 무중력: 고정된 것은 안 뜨고 · 방송 · 물방울 합선 · 손잡이 · 멀미 · 붙잡기 · 수리 ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.Hours(9)); // 아침
            var z = w.ZeroG;
            var latched = w.Ship.Furniture.Where(f => f.Type is FurnitureType.Shelf or FurnitureType.ToolWall or FurnitureType.Bookshelf or FurnitureType.Workbench && !f.Room.Detached && !f.Stowed).OrderBy(f => f.Id).FirstOrDefault();
            if (latched != null) w.Maneuver.Latched.Add(latched.Id);
            var tied = w.Matter.Things.Where(t => t.Loose && !t.Stowed && !t.Spec.Flat).OrderBy(t => t.Id).FirstOrDefault();
            if (tied != null) { tied.Fixed = true; w.Maneuver.Tied.Add(tied.Id); }
            z.Begin("중력 판 제어기가 타 버렸다", repair: true);
            Check("무중력 — 고정 안 된 것이 떠오른다", z.Floaters.Count > 0, $"떠오름 {z.Floaters.Count} · {z.Stats.Line()}");
            Check("무중력 — 걸쇠 건 선반 · 묶은 물건은 안 뜬다",
                (latched == null || !z.Floaters.Any(f => f.From == latched.Id)) && (tied == null || !z.Floaters.Any(f => f.Article == tied.Id)) && z.Stats.HeldFast > 0,
                $"걸쇠 {latched?.Label} · 묶음 {tied?.Id} · 버틴 것 {z.Stats.HeldFast}");
            Check("무중력 — 컴퓨터가 방송하고 사람들이 듣는다", z.Stats.Broadcasts > 0 && z.Stats.Heard > 0, $"방송 {z.Stats.Broadcasts} · 들음 {z.Stats.Heard} · 짚은 방 {z.OrderRoom}");
            if (w.Eco.Cat is ShipCat cat0) Check("무중력 — 고양이가 허우적거린다", cat0.State is CatState.Float or CatState.Carried && w.Eco.Stats.Floated + w.Eco.Stats.Grabbed > 0, $"{cat0.State}");
            // 물방울 × 전기 설비
            var con = w.Ship.Furniture.Where(f => ZeroGSystem.Electric(f.Type) && !f.Room.Detached && f.Room.Powered && f.UseSpots.Count > 0).OrderBy(f => f.Id).First();
            var drop = z.Spawn(DriftKind.Droplet, con.UseSpots[0].Center, con.Room);
            drop.Vel = Vector2.Zero;
            int shorts = z.Stats.Shorts;
            Run(w, 12);
            Check("무중력 — 떠다니던 물방울이 전기 설비에 닿아 합선", z.Stats.Shorts > shorts && z.Shorts.Any(s => s.Furniture == con.Id), $"{con.Label} · 합선 {z.Stats.Shorts}");
            float mm = w.Crew.Where(c => !c.Dead && !c.Outside).Min(c => z.MoveMul(c));
            Run(w, SimTime.Hours(2));
            Check("무중력 — 손잡이를 잡고 벽을 따라 느리게 움직인다", z.Stats.Grips > 0 && mm < 1f, $"손잡이 {z.Stats.Grips} · 밀기 {z.Stats.PushOffs} · 속도 {mm:0.00}");
            Check("무중력 — 처음 겪는 사람은 멀미를 한다", z.Stats.Queasy > 0, $"멀미 {z.Stats.Queasy} · 앓음 {z.Stats.Sick} · 토함 {z.Stats.Vomits}");
            Check("무중력 — 떠다니는 것을 붙잡아 넣는다", z.Stats.Caught > 0, $"붙잡음 {z.Stats.Caught} · 공구 {z.Stats.Stowed}");
            for (int i = 0; i < 24 && z.Weightless; i++) Run(w, SimTime.Minutes(15));
            Check("무중력 — 정비사가 고치고 컴퓨터가 1분 전에 알린 뒤 중력이 돌아온다", !z.Weightless && z.Stats.Repairs > 0 && z.Stats.Warned > 0, $"수리 {z.Stats.Repairs} · 예고 {z.Stats.Warned} · 떨어짐 {z.Stats.Fell} · 비킴 {z.Stats.Dodged}");
            if (z.Weightless) z.Restore();

            // ── 2) 예고 없이 돌아오는 순간: 떠 있던 공구가 사람 위로 → 다침 → 관행 ──
            z.Begin("전기가 끊겨 중력 판이 꺼졌다", repair: false, power: true);
            var under = w.Crew.Where(c => !c.Dead && !c.Outside && c.IsAwake && c.Room != null).OrderBy(c => c.Id).Take(6).ToList();
            var before = under.ToDictionary(c => c.Id, c => c.Vitals.Injury);
            foreach (var c in under) { var f = z.Spawn(DriftKind.Wrench, c.Position, c.Room!); f.Vel = Vector2.Zero; }
            int hurt0 = z.Stats.Hurt;
            z.Restore();
            var hit = under.FirstOrDefault(c => c.Vitals.Injury > before[c.Id]);
            Check("복구 — 떠 있던 공구가 떨어져 누군가 다친다", hit != null && z.Stats.Hurt > hurt0, $"맞은 사람 {hit?.Name} · 다침 {z.Stats.Hurt - hurt0} · 비킴 {z.Stats.Dodged}");
            Check("복구 — 떨어진 것은 바닥에 남는다 (줍기 · 쓸기 거리)", w.Maneuver.Fallen.Count > 0, $"바닥 {w.Maneuver.Fallen.Count}");
            Check("겪을수록 — 공구를 끈에 매는 관행이 생긴다", w.Culture.Of(CustomKind.TetherTools) != null, w.Culture.Of(CustomKind.TetherTools)?.Origin ?? "없음");
            z.Begin("회전 고리 베어링이 걸렸다", repair: false);
            bool tools = w.Ship.Furniture.Any(f => f.Type is FurnitureType.ToolWall or FurnitureType.Workbench && !f.Room.Detached && !w.Maneuver.Latched.Contains(f.Id));
            Check("겪을수록 — 다음엔 끈에 매인 공구가 버틴다 · 겪은 횟수가 쌓인다", (!tools || z.Stats.Tethered > 0) && z.Exposures.Values.DefaultIfEmpty(0).Max() >= 3, $"끈 {z.Stats.Tethered} · 최다 {z.Exposures.Values.DefaultIfEmpty(0).Max()}");
            z.Restore();
        }

        // ── 3) 고양이: 경보에 숨고 → 좋아하는 사람이 찾아 데려온다 ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.Hours(9));
            var e = w.Eco;
            var cat = e.Cat;
            Check("고양이 — 한 마리가 산다 (좋아하는 사람이 있다)", cat != null && cat.Favorite >= 0, $"{cat?.Name} · 좋아하는 사람 {cat?.Favorite}");
            if (cat != null)
            {
                var far = w.Ship.Rooms.Where(r => !r.Detached && r.Kind is RoomType.Workshop or RoomType.Engine or RoomType.Power && r.Cells.Count > 0).OrderBy(r => r.Id).FirstOrDefault()
                          ?? w.Ship.Rooms.First(r => !r.Detached && r.Kind != RoomType.Corridor && r.Id != cat.RoomId);
                var fc = far.Cells.First(c => w.Ship.IsWalkable(c));
                w.Fire.Ignite(fc, 0.4f);
                Run(w, SimTime.Minutes(4));
                Check("고양이 — 화재 경보에 숨는다", cat.State == CatState.Hide && e.Stats.Hid > 0, $"{cat.State} · 숨은 방 {cat.HideRoom}");
                foreach (var r in w.Ship.Rooms) if (w.Fire.CountIn(r) > 0) w.Fire.ClearRoom(r);
                for (int i = 0; i < 40 && e.Stats.Returned == 0 && cat.Alive; i++) Run(w, SimTime.Minutes(15));
                var by = e.CrewOf(cat.Favorite);
                Check("고양이 — 좋아하는 사람이 찾아 안고 데려온다", e.Stats.Found > 0 && e.Stats.Returned > 0, $"찾음 {e.Stats.Found} · 데려옴 {e.Stats.Returned} · 컴퓨터 귀띔 {e.Stats.CatHints} · {cat.State} · 좋아하는 사람 {by?.Name}");
                Check("고양이 — 다시 숨을 곳을 좋아하는 사람이 기억한다", cat.KnownHides.Count > 0, string.Join(",", cat.KnownHides));
                // 배고픔 → 밥
                cat.Hunger = 0.7f;
                int fed = e.Stats.Fed;
                for (int i = 0; i < 24 && e.Stats.Fed == fed; i++) Run(w, SimTime.Minutes(15));
                Check("고양이 — 배고파 울면 누군가 밥을 준다", e.Stats.Fed > fed && cat.Hunger < 0.5f, $"밥 {e.Stats.Fed} · 사료 {e.Kibble} · 배고픔 {cat.Hunger:0.00}");
            }
        }

        // ── 4) 바구미: 창고 → 번식 → 발견 → 방제 · 식량 손실 ──
        {
            var w = DayOne(seed, "Hanbit");
            var e = w.Eco;
            var shelf = w.Ship.Furniture.Where(f => f.Type == FurnitureType.Shelf && f.Storage != null && !f.Room.Detached && f.Storage.Accepts(ItemKind.Ration)).OrderByDescending(f => f.Storage!.Count(ItemKind.Ration)).ThenBy(f => f.Id).First();
            if (shelf.Storage!.Count(ItemKind.Ration) < 12) shelf.Storage.Add(ItemKind.Ration, 12);
            shelf.Room.Air.Temperature = MathF.Max(shelf.Room.Air.Temperature, 22f);
            int r0 = shelf.Storage.Count(ItemKind.Ration);
            var x = e.Seed(shelf, 0.12f);
            for (int i = 0; i < 72 && !x.Treated; i++) Run(w, SimTime.Hours(1));
            Check("바구미 — 따뜻한 창고에서 불어나 곡식을 갉는다", e.Stats.Bred > 0 && x.Eaten > 0, $"번식 {e.Stats.Bred} · 먹힘 {x.Eaten} · 개체 {x.Pop:0.00}");
            Check("바구미 — 사람이 보거나 컴퓨터가 장부 차이로 짚어 찾는다", x.Found && e.Stats.FoundByCrew > 0, $"사람 {e.Stats.FoundByCrew} · 컴퓨터 {e.Stats.FoundByComputer}");
            Check("바구미 — 골라 버리고 닦아 밀폐 통에 (방제 · 식량 손실)", x.Treated && e.Stats.Controlled > 0 && e.Sealed.Contains(shelf.Id) && shelf.Storage.Count(ItemKind.Ration) < r0,
                $"방제 {e.Stats.Controlled} · 버림 {e.Stats.Discarded} · 비상식량 {r0}→{shelf.Storage.Count(ItemKind.Ration)}");
        }

        // ── 5) 화분: 마르면 컴퓨터가 짚고 · 돌보는 사람이 물을 준다 ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.Hours(9));
            var e = w.Eco;
            var p = e.Plants.Where(x => !x.Dead && x.Carer >= 0).OrderBy(x => x.Id).FirstOrDefault();
            Check("화분 — 배에 화분이 있고 돌보는 사람이 정해진다", p != null && e.Plants.Count >= 3, $"화분 {e.Plants.Count} · {string.Join(", ", e.Plants.Select(x => $"{x.Name}/{x.Carer}"))}");
            if (p != null)
            {
                p.Water = 0.05f; p.Watered = w.Tick - SimTime.Hours(31);
                int w0 = e.Stats.Watered;
                for (int i = 0; i < 40 && e.Stats.Watered == w0; i++) Run(w, SimTime.Minutes(15));
                Check("화분 — 컴퓨터가 마른 흙을 짚고 돌보는 사람이 물을 준다", e.Stats.PlantHints > 0 && e.Stats.Watered > w0 && p.Water > 0.5f, $"귀띔 {e.Stats.PlantHints} · 물 {e.Stats.Watered} · 준 사람 {p.WateredBy} (돌보는 {p.Carer})");
            }
        }

        // ── 6) 배수: 막힌 배수구 → 역류 → 냄새 → 그 방에서 안 먹는다 → 뚫음 → 관행 ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.Hours(9));
            var dr = w.Drains;
            var d = dr.Drains.Where(x => w.Ship.Rooms[x.RoomId].Kind == RoomType.Mess).Concat(dr.Drains.Where(x => x.Kind == DrainKind.Sink)).FirstOrDefault();
            Check("배수 — 배수구 · 쓰레기통이 놓인다", d != null && dr.Bins.Count > 0, $"배수구 {dr.Drains.Count} · 쓰레기통 {dr.Bins.Count}");
            if (d != null)
            {
                var room = w.Ship.Rooms[d.RoomId];
                dr.ForceClog(d, 0.8f);
                Run(w, SimTime.Minutes(10));
                Check("배수 — 막힌 배수구로 회색물이 역류하고 냄새가 난다", d.Backflow && dr.Stink(room) >= DrainSystem.AvoidAt && dr.Stats.Backflows > 0, $"{room.Name} 냄새 {dr.Stink(room):0.00}");
                var nose = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(SmellSystem.Nose).ThenBy(c => c.Id).First();
                var seat = room.Kind == RoomType.Mess ? room.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Seat) : null;
                var plan = dr.EatAway(nose, seat, w.Paths.Flood(nose.Cell));
                Check("배수 — 냄새 때문에 그 방을 피해 다른 방에서 먹는다", room.Kind != RoomType.Mess || plan != null && plan.Room != room, $"{nose.Name} → {plan?.Room.Name ?? "없음"} ({plan?.Why})");
                for (int i = 0; i < 32 && d.Backflow; i++) Run(w, SimTime.Minutes(15));
                Check("배수 — 손재주 있는 사람이 뚫는다 · 음식물로 막혔으면 나눠 버리는 관행", !d.Backflow && dr.Stats.Cleared > 0 && w.Culture.Of(CustomKind.SortWaste) != null, $"뚫음 {dr.Stats.Cleared} · 관행 {w.Culture.Of(CustomKind.SortWaste)?.Origin}");
            }
        }

        // ── 7) 결정론 · 성능 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드 두 번이 같다", a == b, $"{a:x8} / {b:x8}");
            double Time(bool off)
            {
                ZeroGSystem.Off = EcoSystem.Off = DrainSystem.Off = off;
                var w = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(30, seed));
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                sw.Stop();
                ZeroGSystem.Off = EcoSystem.Off = DrainSystem.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            ZeroGSystem.UpdateTicks = EcoSystem.UpdateTicks = DrainSystem.UpdateTicks = 0;
            double off1 = Time(true), on1 = Time(false);
            double ratio = on1 / off1;
            if (ratio > 1.1) { double off2 = Time(true), on2 = Time(false); ratio = Math.Min(on1, on2) / Math.Min(off1, off2); }
            double own = (ZeroGSystem.UpdateTicks + EcoSystem.UpdateTicks + DrainSystem.UpdateTicks) * 1000.0 / Stopwatch.Frequency;
            Check("성능 — 30명 배 하루가 10% 안쪽으로 느려진다", ratio <= 1.10, $"{ratio:0.000}배 (끔 {off1:0.0}초 · 켬 {on1:0.0}초 · 이 시스템 틱 {own:0}ms)");
        }

        Console.WriteLine(_fails == 0 ? "\n무중력 · 생태계 점검 통과" : $"\n무중력 · 생태계 점검 실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
