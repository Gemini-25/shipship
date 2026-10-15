using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 재료 수집 (가 · 나 · 다) · 항상성 (나 · 라 · 마) 장면 시험 (--matbtest)
//   재료: 채집 장치를 배 크기에 맞춰 늘린다 · 모자란 원료를 겨눠 캔다 · 바위 · 혜성 조각을 끌어온다 · 돌 많은 구간을 천천히 지난다
//         · 고철을 어느 정제기에서든 녹인다 · 헌 부품을 뜯는다 · 찌꺼기 · 숨에서 탄소 · 노는 정제기를 주 컴퓨터가 돌린다 · 해체된 로봇이 쥔 일을 놓는다
//   항상성: 햇빛 쪽 · 그늘 쪽 · 깊은 공허 · 근일점 · 예보로 미리 덥힌다 · 착륙 열 점검 · 한 화면 다섯 줄 · 추위를 난방 고장으로 오진하지 않는다
public static partial class Program
{
    private static void ClearStock(World w, params ItemKind[] kinds)
    {
        foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null))
            foreach (var k in kinds) { int n = f.Storage!.Count(k); if (n > 0) f.Storage.Take(k, n); }
    }

    private static int RunMatHomeoTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"재료 수집 · 항상성 점검 · 시드 {seed}\n");
        try
        {
            // ═══ 재료 가) 채집 장치를 배 크기에 맞춘다 ═══
            {
                var w = DayOne(seed, "Cheonma");
                int before = w.Ship.FurnitureOf(FurnitureType.Collector).Count();
                int want = w.Mats.CollectorsWanted();
                Run(w, SimTime.TicksPerDay);
                int after = w.Ship.FurnitureOf(FurnitureType.Collector).Count();
                var order = w.Board.All.FirstOrDefault(o => o.Kind == WorkKind.Upgrade && o.Upgrade == UpgradeKind.Works);
                Check("가) 큰 배는 채집 장치를 늘린다 — 회의 없이 주 컴퓨터가 공사를 올린다", want >= 3 && (after > before || order != null && Evolution.NoMeeting(order)),
                    $"설비 {w.Ship.Machines.Count()}대 · 원하는 수 {want} · 채집 장치 {before} → {after}" + (order != null ? $" · 공사 {order.Detail}" : ""));
            }

            // ═══ 재료 나) 모자란 원료를 겨눈다 ═══
            {
                var w = DayOne(seed, "Bodeum");
                ClearStock(w, ItemKind.Carbon);
                w.Mats.Lacked(ItemKind.Carbon);
                var urgent = w.Mats.Urgent();
                var aim = w.Mats.MineFor();
                int sieved = 0;
                for (int i = 0; i < 200; i++) if (w.Mats.Sieve(ItemKind.MetalOre) == ItemKind.Carbon) sieved++;
                Check("나) 탄소가 바닥이면 가장 급한 원료로 꼽고 · 드론이 탄소를 노리고 · 채집 장치 체가 탄소 쪽을 더 거른다",
                    urgent == ItemKind.Carbon && aim == ItemKind.Carbon && sieved is > 30 and < 100, $"급함 {urgent} · 드론 {aim} · 200번 중 탄소로 {sieved}번 · {w.Mats.Brief()}");

                // 혜성 꼬리에서 바위 · 혜성 조각 끌어오기
                var w2 = DayOne(seed, "Bodeum");
                ClearStock(w2, ItemKind.Carbon);
                w2.Voyage.Force(LegKind.CometTrail, enter: true, days: 3f);
                int c0 = w2.Ship.CountStored(ItemKind.Carbon), i0 = w2.Ship.CountStored(ItemKind.Ice);
                for (int h = 0; h < 30 && w2.Mats.Stats.Tows == 0; h++) { w2.Mats.Lacked(ItemKind.Carbon); Run(w2, SimTime.Hours(1)); }
                Check("나) 혜성 꼬리에서 탄소가 급하면 채굴 드론이 혜성 조각을 끌어와 쪼갠다", w2.Mats.Stats.Tows >= 1,
                    $"끌어옴 {w2.Mats.Stats.Tows} · 탄소 {c0} → {w2.Ship.CountStored(ItemKind.Carbon)} · 얼음 {i0} → {w2.Ship.CountStored(ItemKind.Ice)}");

                // 소행성대: 급한 금속이면 반나절 천천히 지난다
                var w3 = DayOne(seed, "Mirinae");
                ClearStock(w3, ItemKind.MetalOre);
                w3.Mats.Lacked(ItemKind.MetalOre);
                w3.Voyage.Force(LegKind.AsteroidBelt, enter: true, days: 2f);
                Run(w3, SimTime.Hours(2));
                Check("나) 돌 많은 구간에서 금속이 바닥이면 주 컴퓨터 혼자 반나절 천천히 지나며 더 거둔다", w3.Mats.Stats.Lingers == 1 && w3.Mats.SpeedMul < 1f,
                    $"천천히 {w3.Mats.Stats.Lingers} · 속도 ×{w3.Mats.SpeedMul:0.0} · {w3.Log.Entries.LastOrDefault(e => e.Text.Contains("천천히")).Text}");
            }

            // ═══ 재료 다) 배 안에서 되살린다 ═══
            {
                var w = DayOne(seed, "Mirinae");
                bool recycling = w.Ship.Rooms.Any(r => r.Kind is RoomType.Recycling or RoomType.Crusher);
                int ore0 = w.Ship.CountStored(ItemKind.MetalOre);
                w.Scrap.AddScrap(9f);
                Run(w, SimTime.Hours(9)); // 네 시간마다 녹인다
                Check("다) 재활용실이 없는 배도 정제기에서 고철을 녹인다 (고철 3 → 금속 원료 1)", w.Scrap.Recovered >= 3 || recycling && w.Scrap.Recovered >= 4,
                    $"재활용실 {(recycling ? "있음" : "없음")} · 되살린 금속 원료 {w.Scrap.Recovered} · 남은 고철 {w.Scrap.Scrap:0.0} · 금속 원료 {ore0} → {w.Ship.CountStored(ItemKind.MetalOre)}");

                float s0 = w.Scrap.Scrap;
                var who = w.Crew.First(c => !c.IsChild && !c.Dead);
                var m = w.Ship.Machines.First();
                for (int i = 0; i < 6; i++) w.Mats.Stripped(ItemKind.Motor, who, m);
                Check("다) 갈아 낸 헌 모터를 뜯으면 고철이 되고 · 가끔 감긴 구리선을 건진다", w.Scrap.Scrap >= s0 + 5.9f && w.Mats.Stats.Stripped == 6,
                    $"고철 {s0:0.0} → {w.Scrap.Scrap:0.0} · 건짐 {w.Mats.Stats.Salvaged}/6");

                ClearStock(w, ItemKind.Carbon);
                w.Scrap.AddOrganic(9f);
                Run(w, SimTime.Hours(5));
                Check("다) 모아 둔 음식 찌꺼기를 정제기에서 말려 탄소로 (찌꺼기 4 → 탄소 1)", w.Scrap.CarbonBack >= 2 && w.Ship.CountStored(ItemKind.Carbon) >= 2,
                    $"탄소 {w.Ship.CountStored(ItemKind.Carbon)} · 되살림 {w.Scrap.CarbonBack} · 남은 찌꺼기 {w.Scrap.Organic:0.0}");

                // 탄소 회수기
                var w2 = DayOne(seed, "Hanbit");
                var ls = w2.Ship.RoomsOf(RoomType.LifeSupport).First();
                var cm = w2.Crew.First(c => !c.IsChild);
                bool put = Modules.Spot(w2, ls) is Cell at && Modules.Install(w2, ls, FurnitureType.CarbonReclaimer, cm, at);
                ClearStock(w2, ItemKind.Carbon);
                Run(w2, SimTime.TicksPerDay);
                Check("다) 탄소 회수기: 사람이 내쉰 이산화탄소에서 하루 사람 하나에 0.12개쯤 탄소를 뽑는다", put && w2.Mats.Stats.CarbonFromAir is > 0.8f and < 3f,
                    $"설치 {put} · 공기에서 탄소 {w2.Mats.Stats.CarbonFromAir:0.00} (사람 {w2.Aboard}) · 탄소 {w2.Ship.CountStored(ItemKind.Carbon)}");

                // 노는 정제기를 주 컴퓨터가 돌린다
                var w3 = DayOne(seed, "Hanbit");
                NoRobots(w3);
                ClearStock(w3, ItemKind.Plate, ItemKind.Cable);
                VoyageV15.Put(w3, ItemKind.MetalOre, 40);
                foreach (var c in w3.Crew) c.Away = true; // 아무도 정제하지 못한다
                Run(w3, SimTime.Hours(12));
                foreach (var c in w3.Crew) c.Away = false;
                Check("다) 사람 · 로봇이 못 쓰는 정제기는 주 컴퓨터가 혼자 천천히 돌린다 (금속판 · 케이블이 바닥 · 원료 넉넉)",
                    w3.Mats.Stats.AutoRefined >= 3 && w3.Ship.CountStored(ItemKind.Plate) + w3.Ship.CountStored(ItemKind.Cable) >= 3,
                    $"컴퓨터가 정제 {w3.Mats.Stats.AutoRefined} · 금속판 {w3.Ship.CountStored(ItemKind.Plate)} · 케이블 {w3.Ship.CountStored(ItemKind.Cable)} · 금속 원료 {w3.Ship.CountStored(ItemKind.MetalOre)}");

                // 큰 배는 수리재를 더 쌓는다
                var big = DayOne(seed, "Cheonma");
                Check("다) 큰 배(설비 260대)는 금속판 목표가 네 배쯤 — 12개 아래로 떨어질 때까지 정제를 미루지 않는다",
                    MaterialSystem.StockScale(big, ItemKind.Plate) >= 3.5f && MaterialSystem.StockScale(big, ItemKind.Sealant) == 1f,
                    $"금속판 배율 ×{MaterialSystem.StockScale(big, ItemKind.Plate):0.0} · 실링폼 ×{MaterialSystem.StockScale(big, ItemKind.Sealant):0.0}");
            }

            // ═══ 해체된 로봇이 쥔 일을 놓는다 ═══
            {
                var w = DayOne(seed, "Hanbit");
                var bot = w.Robots.Robots.First();
                var refinery = w.Ship.FurnitureOf(FurnitureType.Refinery).First();
                Run(w, SimTime.Minutes(30));
                var o = w.Board.All.Where(x => !x.Closed && x.Assignee == null && x.Robot == null && x.Kind is not (WorkKind.RepairRobot or WorkKind.FetchRobot)).OrderBy(x => x.Id).FirstOrDefault();
                bool set = false;
                if (o != null) { o.Robot = bot; set = true; }
                w.Robots.Wreck(bot, "시험: 부서졌다");
                Run(w, SimTime.Minutes(30));
                Check("로봇: 부서지거나 해체된 로봇이 이름만 걸어 둔 일은 놓아 준다 (정제기가 하루 넘게 묶이던 것)", set && o!.Robot != bot,
                    $"일 {o?.Detail ?? "없음"} · 지금 맡은 로봇 {o?.Robot?.Name ?? "-"} · 사람 {o?.Assignee?.Name ?? "-"}");
            }

            // ═══ 항상성 마) 우주 공간 온도 ═══
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.Hours(2));
                float b0 = w.Homeo.Shell(w.Ship.Rooms.First());
                var outer = w.Ship.LiveRooms.Where(r => w.Air.HullConductance(r) > 0f).ToList();
                float sunny = outer.Max(r => w.Homeo.Shell(r)), shade = outer.Min(r => w.Homeo.Shell(r));
                Check("마) 보통 항로: 햇빛 쪽 선체가 그늘 쪽보다 따뜻하다 (평균은 예전처럼 6℃ 안팎)", sunny - shade > 4f && sunny - shade < 14f && (sunny + shade) / 2f is > 2f and < 10f,
                    $"햇빛 쪽 {sunny:0.0}℃ · 그늘 쪽 {shade:0.0}℃ · 햇빛 ×{w.Homeo.Sun:0.0}");

                w.Voyage.Force(LegKind.DeepVoid, enter: true, days: 3f);
                Run(w, SimTime.Hours(8));
                float cold = outer.Min(r => w.Homeo.Shell(r));
                var occ = w.Ship.LiveRooms.Where(r => w.Crew.Any(c => !c.Dead && c.Room == r)).ToList();
                Check("마 · 나) 깊은 공허: 선체가 영하 20℃ 아래로 식고 · 주 컴퓨터가 미리 덥히고 바깥 방 단열 덮개를 내린다",
                    cold < -20f && w.Homeo.Stats.Shutters >= 2 && w.Homeo.Stats.Preheats >= 1,
                    $"선체 최저 {cold:0}℃ · 덮개 {w.Homeo.Stats.Shutters} · 미리 덥힘 {w.Homeo.Stats.Preheats} · 난방 보탬 {w.Homeo.Stats.Boosts} · {w.Log.Entries.LastOrDefault(e => e.Text.Contains("선체가")).Text}");
                Run(w, SimTime.Hours(16));
                float worst = w.Homeo.Stats.ColdestOccupied;
                Check("나) 하루 깊은 공허를 지나도 사람이 지내는 방은 15℃ 아래로 떨어지지 않는다 (일부러 닫은 창고에 잠깐 들르는 건 빼고)", worst >= 15f,
                    $"사람 있는 방 최저 {worst:0.0}℃ ({w.Homeo.Stats.ColdestRoom}) · {w.Homeo.Stats.Line()}");

                w.Voyage.Force(LegKind.Perihelion, enter: true, days: 3f);
                Run(w, SimTime.Hours(10));
                Check("마 · 나) 근일점: 선체가 달아오르면 미리 식히고 · 햇빛이 빈 쪽으로 오게 배를 돌리거나 굴린다",
                    w.Homeo.Stats.Precools >= 1 && (w.Homeo.Stats.Turns >= 1 || w.Homeo.Stats.Rolls >= 1) && w.Homeo.Stats.HottestOccupied < 30f,
                    $"미리 식힘 {w.Homeo.Stats.Precools} · 돌림 {w.Homeo.Stats.Turns} · 굴림 {w.Homeo.Stats.Rolls} · 사람 있는 방 최고 {w.Homeo.Stats.HottestOccupied:0.0}℃");
            }

            // ═══ 항상성 나) 예보 · 착륙 열 점검 ═══
            {
                var w = DayOne(seed, "Mirinae");
                w.Voyage.Force(LegKind.Cruise, enter: true, days: 1.2f);
                if (w.Voyage.Index + 1 < w.Voyage.Legs.Count)
                    w.Voyage.Legs[w.Voyage.Index + 1] = new Leg { Kind = LegKind.DeepVoid, Name = "검은 틈", Days = 2f };
                Run(w, SimTime.Hours(22));
                bool before = w.Voyage.Current.Kind == LegKind.Cruise;
                Check("나) 예보: 깊은 공허가 열두 시간 안에 오면 들어서기 전에 미리 덥힌다", w.Homeo.Stats.Preheats >= 1 && (before || w.Homeo.Stats.Shutters > 0),
                    $"미리 덥힘 {w.Homeo.Stats.Preheats} · 아직 보통 항로 {before} · {w.Log.Entries.LastOrDefault(e => e.Text.Contains("시간 뒤")).Text}");

                var mild = new Planet { Name = "온화", TempC = 15f, Swing = 10f, Pressure = 100f };
                var hell = new Planet { Name = "불가마", TempC = 460f, Swing = 4f, Pressure = 9000f };
                var (okMild, nMild) = w.Homeo.CanHold(mild);
                var (okHell, nHell) = w.Homeo.CanHold(hell);
                Check("나) 착륙 열 점검: 온화한 땅은 버티고 · 460℃ 땅은 버티지 못한다고 셈한다", okMild && !okHell, $"{nMild} / {nHell}");
            }

            // ═══ 항상성 라) 한 화면 · 습도 · 오진 ═══
            {
                var w = DayOne(seed, "Hanbit");
                var rows = w.Homeo.Readings();
                Check("라) 한 화면: 온도 · 습도 · 이산화탄소 · 기압 · 방사선 다섯 줄 (지금 · 목표 · 대응)",
                    rows.Select(r => r.Name).SequenceEqual(new[] { "온도", "습도", "이산화탄소", "기압", "방사선" }) && rows.All(r => r.Target.Length > 0 && r.Response.Length > 0),
                    string.Join(" | ", rows.Select(r => $"{r.Name} {r.Now} (목표 {r.Target}) {r.Response}")));
                var room = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess || r.Type == RoomType.Galley);
                room.Humidity = 0.9f;
                Run(w, SimTime.Hours(3));
                Check("라) 너무 습한 방은 제습 운전으로 60%대까지 내린다", room.Humidity < 0.75f && w.Homeo.Stats.Dries >= 1, $"{room.Name} 습도 {room.Humidity * 100:0}% · 제습 {w.Homeo.Stats.Dries}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 재료 수집 · 항상성 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
