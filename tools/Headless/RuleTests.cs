using System;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 규칙 시험 (--ruletest): 작은 배에서 규칙 하나씩만 본다 — 몇 초 · 결정적 · 반드시 통과 (실패면 진짜 버그).
/// 장면 시험(나머지 --xxxtest)은 배 전체의 역사를 몇 시간 돌려 보므로 상관없는 변경에도 뒤집힌다 — 그쪽은 막지 않고 추세만 본다 (docs/DEV_PROCESS.md).
/// 새 기능 · 고친 버그마다 여기에 규칙을 하나씩 더한다.
/// </summary>
public static partial class Program
{
    private static int RunRuleTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"규칙 시험 · 시드 {seed}\n");
        string? only = Environment.GetEnvironmentVariable("RULE_ONLY");
        bool Do(string k) => only == null || only.Split(',').Contains(k);

        // ── 상태 저장: 찍고 되살린 배는 바이트까지 같고, 이어 가도 틱마다 같다 (첫날 배 캐시 · 자동 저장의 바탕) ──
        if (Do("save"))
        {
            var a = World.CreateDefault(seed, 0, "Mirinae");
            Run(a, SimTime.Hours(1));
            var snap = StateSave.Write(a, out var raw0);
            var b = StateSave.Read(snap);
            StateSave.Write(b, out var raw1);
            int at = FirstDiff(raw0, raw1);
            long split = -1;
            for (int t = 0; t < SimTime.Minutes(30) && split < 0; t++)
            {
                a.Step(); b.Step();
                if (SaveGame.StateHash(a) != SaveGame.StateHash(b)) split = a.Tick;
            }
            Check("상태 저장 — 되살린 배는 바이트까지 같고 30분을 이어 가도 틱마다 같다", at < 0 && split < 0,
                $"바이트 {(at < 0 ? "같음" : $"@{at} 다름")} · {(split < 0 ? "30분 내내 같음" : $"틱 {split}에 갈라짐")} ({raw0.Length / 1024}KB)");
        }

        // ── 상처 분류: 원인 글의 낱말로 고른다 — '불시착'의 '불' · '열린 뚜껑'의 '열'은 화상이 아니다 (제비호: 불시착으로 다친 사람이 "넓게 데었다") ──
        if (Do("wound"))
        {
            WoundKind? K(string cause) => Wounds.Classify(cause, 0.3f, 0)?.kind;
            var wrong = new[] { "불시착 충격", "열린 점검 뚜껑에 발이 빠졌다" }.Where(x => K(x) == WoundKind.Burn).ToList();
            var missed = new[] { "불길에 덴 화상", "배기 불꽃 열기", "불이 옮겨붙은 화상", "자다가 불에 덴 화상", "화학 화상" }.Where(x => K(x) != WoundKind.Burn).ToList();
            Check("상처 분류 — 불시착 · 열린 뚜껑은 화상이 아니고, 불길 · 불꽃 · 열기는 화상", wrong.Count == 0 && missed.Count == 0,
                $"화상으로 잘못 {wrong.Count} ({string.Join(", ", wrong)}) · 화상 놓침 {missed.Count} ({string.Join(", ", missed)})");
        }

        // ── 위험한 방: 넘어진 사람은 주저앉아 있지 않고 곧바로 일어나며, 기동 뒤 뒷정리는 그 방에서 하지 않는다 (제비호: 갈라진 별채에서 2분 반 앉았다가 히터를 세웠다) ──
        if (Do("hostile"))
        {
            var w = World.CreateDefault(seed, 0, "Kestrel");
            Run(w, SimTime.Minutes(10));
            var c = w.Crew.First(x => !x.Dead && x.IsAwake && x.Room is Room rr && rr.Cells.Count(w.Ship.IsOpenFloor) > 4);
            var r = c.Room!;
            void Thin() { r.Air.O2 = 2f; r.Air.N2 = 9f; r.Air.CO2 = 0f; } // 11kPa — 숨 쉴 수 없다
            var spot = r.Cells.Where(x => w.Ship.IsOpenFloor(x) && x != c.Cell).OrderBy(x => (x.Center - c.Position).LengthSquared()).First();
            w.Maneuver.Fallen.Add(new FallenThing { Id = 990001, Kind = default, At = spot, RoomId = r.Id, Tick = w.Tick });
            var dist = w.Paths.Flood(c.Cell, c.PathProfile);
            var calm = w.Maneuver.PickAfter(c, dist).kind;
            Thin();
            var bad = w.Maneuver.PickAfter(c, dist).kind;
            w.Maneuver.Fallen.Clear();
            // 넘어진 채로: 숨 쉴 수 없는 방이면 곧바로 일어난다
            var t = new Tumble { Crew = c.Id, Tick = w.Tick, At = c.Position, Dir = new System.Numerics.Vector2(1f, 0f), Hurt = true };
            w.Maneuver.Tumbles.Add(t);
            c.NextThinkTick = w.Tick;
            long t0 = w.Tick;
            while (!t.Up && w.Tick - t0 < SimTime.Minutes(2)) { Thin(); w.Step(); }
            float upMin = (w.Tick - t0) / (float)SimTime.Minutes(1);
            Check("위험한 방 — 넘어진 사람은 곧바로 일어나고 (1분 안), 뒷정리는 그 방에서 하지 않는다", calm == AfterKind.Pickup && bad == AfterKind.None && t.Up && upMin < 1f,
                $"공기 멀쩡: {calm} · 11kPa: {bad} · 일어나기까지 {upMin:0.0}분{(t.Up ? "" : " (2분이 지나도 앉아 있다)")}");
        }

        // ── 증축 가압은 공기 탱크로: 옆 방(사람이 있을 수 있다) 공기를 반씩 나눠 받지 않는다 (제비호 33일: 새 창고 산소 10.4kPa) ──
        if (Do("annexair"))
        {
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Minutes(5));
            var p = w.Annex.Order(RoomType.Storage, 0);
            var R0 = p != null ? w.Ship.Rooms[p.Site.AttachRoom] : null;
            float before = R0?.Air.Pressure ?? 0f, tank0 = w.Air.Reserve;
            var room = p != null ? w.Annex.BuildNow(p) : null;
            float after = R0?.Air.Pressure ?? 0f;
            Check("증축 가압은 공기 탱크로 — 옆 방 기압이 그대로 (3kPa 안), 새 방도 찬다", room != null && after >= before - 3f && room.Air.Pressure >= before * 0.9f && w.Air.Reserve < tank0,
                $"옆 방 {R0?.Name} {before:0} → {after:0}kPa · 새 방 {room?.Air.Pressure:0}kPa · 탱크 {tank0:0} → {w.Air.Reserve:0}");
        }

        // ── 주 컴퓨터는 같은 결론을 되풀이해 적지 않는다: 쉬는 드론이 없는 파공 — "누가 막나"는 한 번 (제비호 82일: 40분에 열여섯 번) ──
        if (Do("sealquiet"))
        {
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Minutes(5));
            var (wallCell, room) = w.Ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => (cell: kv.Key, room: Hull.InsideRoom(w.Ship, kv.Key)))
                .Where(x => x.room is Room r && !r.Detached && w.Crew.Any(c => !c.Dead && c.Room == r)).OrderBy(x => x.cell.X * 1000 + x.cell.Y).First();
            void Busy() // 드론 거치대에 봉합 재료가 없다 (밖에서 막을 드론이 없다)
            {
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.DroneDock))
                    if (f.Storage != null) foreach (var k in Enum.GetValues<ItemKind>()) f.Storage.Take(k, 999);
            }
            // 막을 손이 한동안 없다 (제비호 82일: 불시착 뒤 40분) — 사람은 모두 배 밖 (원정), 로봇도 일을 받지 않게 멈춘다
            foreach (var c in w.Crew) c.Away = true;
            foreach (var rb in w.Robots.Robots) rb.Disabled = true;
            Busy();
            int Said() => w.Log.Entries.Count(e => e.Text.Contains("누가 막나"));
            int said0 = Said();
            string? hit = Hazards.Apply(w, HazardKind.HullCrack, wallCell, -1);
            long t0 = w.Tick;
            int seals = 0;
            while (w.Tick - t0 < SimTime.Minutes(15))
            {
                Busy(); w.Step(); seals = Math.Max(seals, w.Board.Open.Count(o => o.Kind == WorkKind.SealBreach));
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && (w.Tick - t0) % SimTime.Minutes(1) == 0)
                    Console.WriteLine($"    {(w.Tick - t0) / SimTime.Minutes(1)}분: " + string.Join(" | ", w.Board.All.Where(o => o.Kind == WorkKind.SealBreach).Select(o => $"#{o.Id} 닫힘 {o.Closed} 맡은 {o.Assignee?.Name ?? "-"} 드론 {o.Drone?.Name ?? "-"} 대상 {o.Target.Kind} 방 {o.Target.Room?.Name} 버림 {o.Target.Room?.Abandoned}"))
                        + $" · 드론 조종 {w.Automation.DroneControl} · 해치 {DroneSystem.Hatch(w) != null}");
            }
            int said = Said() - said0;
            Check("주 컴퓨터 — 드론 없는 파공의 '누가 막나'는 한 번만 적는다 (15분)", said == 1, $"{said}번 · {room!.Name} · {hit ?? "균열 없음"} · 봉합 일 {seals}");
        }

        // ── 하나뿐인 주방은 '일찍 포기'하지 않는다 (보듬호 34일: 구멍 난 주방을 1.5시간 만에 포기하고 이레 동안 되찾지 않아, 그 방 냉장고의 채소를 두고 모두 굶었다) ──
        if (Do("galleykeep"))
        {
            var w = World.CreateDefault(seed, 0, "Bodeum");
            Run(w, SimTime.Minutes(5));
            var kitchens = w.Ship.FurnitureOf(FurnitureType.Stove).Where(f => !f.Stowed).Select(f => f.Room).Distinct().ToList();
            var room = kitchens.Count == 1 ? kitchens[0] : null;
            var hull = w.Ship.Walls.Where(kv => kv.Value.IsHull && room != null && Hull.InsideRoom(w.Ship, kv.Key) == room)
                .Select(kv => kv.Key).OrderBy(c => c.X * 1000 + c.Y).ToList();
            foreach (var c in w.Crew) c.Away = true; // 막을 손이 없다 — 구멍이 그대로 남아 '포기' 판단까지 간다
            foreach (var rb in w.Robots.Robots) rb.Disabled = true;
            void Busy()
            {
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.DroneDock))
                    if (f.Storage != null) foreach (var k in Enum.GetValues<ItemKind>()) f.Storage.Take(k, 999);
            }
            w.Policies.Set("zoneabandon", 0); // 방침: 일찍 포기 (1.5시간)
            string? hit = hull.Count > 0 ? Hazards.Apply(w, HazardKind.HullCrack, hull[hull.Count / 2], -1) : null;
            Busy();
            Run(w, SimTime.Minutes(3));
            if (room != null && room.Leaking) room.LeakingSince = w.Tick - SimTime.Hours(2); // 두 시간째 아무도 못 막았다
            bool offered = false;
            for (long t0 = w.Tick; w.Tick - t0 < SimTime.Minutes(10);)
            {
                Busy(); w.Board.RequestScan(); w.Step();
                offered |= w.Board.All.Any(o => o.Kind == WorkKind.SealOffRoom && o.Target.Room == room);
            }
            Check("하나뿐인 주방 — 구멍이 두 시간째 안 막혀도 '일찍 포기' 방침으로 버리지 않는다 (대신할 조리대 · 냉장고가 없다)",
                room != null && room.Leaking && !offered && Council.Essential(w, room) != null,
                $"{room?.Name ?? "주방이 하나가 아님"} · {hit ?? "균열 없음"} · 새는 중 {room?.Leaking} · 포기 일 {(offered ? "올라옴!" : "없음")} · 대신할 것 없음 {(room == null ? "?" : Council.Essential(w, room) ?? "없음")}");
        }

        // ── 결정론: 같은 시드 두 배는 같은 역사 ──
        if (Do("det"))
        {
            var a = World.CreateDefault(seed, 0, "Mirinae");
            var b = World.CreateDefault(seed, 0, "Mirinae");
            Run(a, SimTime.Hours(1));
            Run(b, SimTime.Hours(1));
            uint ha = SaveGame.StateHash(a), hb = SaveGame.StateHash(b);
            Check("결정론 — 같은 시드 두 배는 한 시간 뒤 같은 지문", ha == hb, $"{ha:x8} / {hb:x8}");
        }

        // ── 첫날 배 캐시 열쇠: 시험이 켜고 끄는 전역 속성 스위치도 열쇠에 든다 (44cb413 — 빠져서 aftertest · crisistest가 남의 배를 받았다) ──
        if (Do("warmkey"))
        {
            string k0 = WarmCache.KeyOf("day1", seed, "Hanbit");
            bool think = RoomPlanSystem.ThinkOff, crew = CrisisCrewSystem.Off;
            RoomPlanSystem.ThinkOff = !think;
            string k1 = WarmCache.KeyOf("day1", seed, "Hanbit");
            RoomPlanSystem.ThinkOff = think;
            CrisisCrewSystem.Off = !crew;
            string k2 = WarmCache.KeyOf("day1", seed, "Hanbit");
            CrisisCrewSystem.Off = crew;
            string k3 = WarmCache.KeyOf("day1", seed, "Hanbit");
            Check("첫날 배 캐시 — 전역 속성 스위치(방 안건 끔 · 위기 행동 끔)를 바꾸면 열쇠가 달라진다", k1 != k0 && k2 != k0 && k3 == k0,
                $"방 안건 {(k1 != k0 ? "다름" : "같음!")} · 위기 행동 {(k2 != k0 ? "다름" : "같음!")} · 되돌리면 {(k3 == k0 ? "같음" : "다름!")}");
        }

        // ── 길찾기 거리장 캐시: 꺼내 쓴 값은 다시 편 값과 같다 ──
        if (Do("flood"))
        {
            bool was = Pathfinder.CheckCache;
            Pathfinder.CheckCache = true;
            Pathfinder.StaleHits = 0; Pathfinder.FirstStale = null;
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Hours(4));
            Pathfinder.CheckCache = was;
            Check("길찾기 — 캐시에서 꺼낸 거리장이 다시 편 것과 같다 (네 시간)", Pathfinder.StaleHits == 0 && w.Paths.FloodHits > 0,
                $"꺼냄 {w.Paths.FloodHits}번 · 낡음 {Pathfinder.StaleHits}{(Pathfinder.FirstStale is { } fs ? " · " + fs : "")}");
        }

        // ── 몰래 꺼내 먹기: 나중에 먹을 것이 들어온 보관함도 바로 고른다 (예전엔 한 시간 묵은 목록 — 저장도 안 돼 불러온 배와 갈렸다) ──
        if (Do("sneak"))
        {
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Minutes(30));
            var c = w.Crew.Where(x => !x.Dead && x.CanAct && x.IsAwake).OrderBy(x => x.Id).First();
            var dist = w.Paths.Flood(c.Cell, c.PathProfile);
            foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null))
                foreach (var k in new[] { ItemKind.Ration, ItemKind.Meal }) f.Storage!.Take(k, f.Storage.Count(k));
            var empty = SneakFoodActivity.Pick(c, w, dist);
            var box = SneakFoodActivity.Boxes(w).FirstOrDefault(f => f.Storage!.Accepts(ItemKind.Ration) && !f.Room.Abandoned && !f.Room.Detached && !f.Stowed
                && dist.Reachable(f.UseSpots[0]) && !w.Crew.Any(o => o != c && !o.Dead && o.IsAwake && o.Room == f.Room));
            box?.Storage!.Add(ItemKind.Ration, 1);
            var got = SneakFoodActivity.Pick(c, w, dist);
            Check("몰래 꺼내 먹기 — 다 비운 뒤 한 보관함에 배급을 넣으면 그 보관함을 바로 고른다", empty == null && box != null && got == box,
                $"빈 때 {(empty == null ? "없음" : empty.Name)} · 넣은 곳 {box?.Name ?? "(보는 눈 없는 보관함 없음)"} · 고른 곳 {got?.Name ?? "없음"}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 규칙 시험 모두 통과" : $"\n✘ 규칙 시험 {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
