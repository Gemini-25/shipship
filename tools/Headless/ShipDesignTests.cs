using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// v16.22 배 재설계 · 크기별 등급 · 방 70종 등장 · 식량원 다양화 (--shipdesigntest).
///   ① 다섯 척 설계: 중요한 방(원자로 · 배전 · 주컴퓨터실)은 선체에 닿지 않고 · 주컴퓨터실은 가운데 · 크기별로 방 · 종류 수가 는다
///   ② v16.19 장갑 벽 · 차압 문 · 예비 회로 · 보조 간선이 새 배에 깔린다
///   ③ 방 종류 등장: 카탈로그 + 생성기 표본 50척 (점검 항해와 같은 표본)
///   ④ 다섯 척 하루 (사망 · 예외 없음) · 시작 물자 크기별 · 금속판이 사흘 안에 바닥나지 않는다
///   ⑤ 수경 재배실이 망가져도 조류 · 버섯 · 저장 식량 · 발효로 사흘 버틴다 (주컴퓨터가 셈해 알리고 · 재배 담당이 다른 재배실을 돌본다)
///   ⑥ 결정론
/// </summary>
public static partial class Program
{
    private static readonly string[] BaseShips = { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Cheonma" };

    private static bool TouchesHull(Ship s, Room r) => r.Cells.Any(c => Cell.Dirs8.Any(d => s.WallAt(c + d) is { IsHull: true }));

    private static (float dx, float dy) CenterOffset(Ship s, Room r)
    {
        int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
        for (int i = 0; i < s.Grid.CellCount; i++)
        {
            var c = s.Grid.CellAt(i);
            if (s.Grid.Kind(c) == TileKind.Void) continue;
            x0 = Math.Min(x0, c.X); x1 = Math.Max(x1, c.X); y0 = Math.Min(y0, c.Y); y1 = Math.Max(y1, c.Y);
        }
        float cx = (x0 + x1 + 1) / 2f, cy = (y0 + y1 + 1) / 2f;
        return ((r.Center.X - cx) / (x1 - x0 + 1), (r.Center.Y - cy) / (y1 - y0 + 1));
    }

    /// <summary>점검 항해(AuditCatalog)와 같은 표본: 카탈로그 + 용도 6 × 뼈대 3 × 8 · 24인 + 옛 키 8 · 16 · 30인.</summary>
    private static (HashSet<RoomType> kinds, int ships) CatalogKinds()
    {
        var temps = ShipCatalog.All.ToList();
        foreach (var p in ShipInfos.GenPurposes)
            foreach (var f in ShipInfos.GenFrames)
                foreach (int n in new[] { 8, 24 })
                    temps.Add(ShipGenerator.Template(p, f, n, 1));
        foreach (int n in new[] { 8, 16, 30 }) temps.Add(ShipGenerator.Template(n, 1));
        var kinds = new HashSet<RoomType>();
        int ships = 0;
        foreach (var t in temps)
        {
            try { foreach (var r in ShipBuilder.FromAscii(t.Name, t.Ascii).Rooms) kinds.Add(r.Kind); ships++; }
            catch (Exception e) { Console.WriteLine($"   ✘ {t.Key}: {e.Message}"); }
        }
        return (kinds, ships);
    }

    private static int RunShipDesignTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"v16.22 배 재설계 · 크기별 등급 · 방 종류 · 식량원 — 시드 {seed}\n");

        // ① 설계
        var ships = BaseShips.Select(k => (key: k, t: ShipCatalog.Find(k)!, s: ShipBuilder.FromAscii(k, ShipCatalog.Find(k)!.Ascii))).ToList();
        var hullBad = new List<string>();
        var offs = new List<string>();
        bool central = true;
        foreach (var (key, t, s) in ships)
        {
            foreach (var r in s.Rooms.Where(r => r.Type is RoomType.Reactor or RoomType.Power || r.Kind == RoomType.ComputerRoom))
                if (TouchesHull(s, r)) hullBad.Add($"{t.Name} {r.Name}");
            var comp = s.Rooms.FirstOrDefault(r => r.Kind == RoomType.ComputerRoom);
            var main = s.Furniture.FirstOrDefault(f => f.Type == FurnitureType.MainComputer);
            if (comp == null || main?.Room != comp) { central = false; offs.Add($"{t.Name} 주컴퓨터실 없음"); continue; }
            var (dx, dy) = CenterOffset(s, comp);
            offs.Add($"{t.Name} {dx:+0.00;-0.00},{dy:+0.00;-0.00}");
            if (Math.Abs(dx) > 0.15f || Math.Abs(dy) > 0.15f) central = false;
            var (rooms, kinds) = ShipClasses.Count(s);
            Console.WriteLine($"   {t.Name,-5} {ShipClasses.Name(ShipClasses.Of(t)),-3} {t.Crew,2}인 · {s.Grid.Width}×{s.Grid.Height} · 방 {rooms} · 종류 {kinds} · 문 {s.Doors.Count} · 차압 문 {s.Doors.Count(d => d.Bulkhead)} · 구획 {s.Compartments}");
        }
        Check("중요한 방(원자로 · 배전 · 주컴퓨터실)은 선체 바깥 줄에 닿지 않는다", hullBad.Count == 0, hullBad.Count == 0 ? "다섯 척 모두 안쪽" : string.Join(", ", hullBad));
        Check("주 컴퓨터는 주컴퓨터실에 · 주컴퓨터실은 배 한가운데 근처 (가로 · 세로 15% 안)", central, string.Join(" · ", offs));
        var counts = ships.Select(x => ShipClasses.Count(x.s)).ToList();
        bool rise = Enumerable.Range(1, counts.Count - 1).All(i => counts[i].rooms > counts[i - 1].rooms && counts[i].kinds > counts[i - 1].kinds);
        Check("크기가 클수록 방 · 방 종류가 많다 (소형 < 기본형 < 중형 < 대형 < 초대형)", rise && counts[0].kinds <= 20 && counts[^1].kinds >= 55,
            string.Join(" · ", ships.Select((x, i) => $"{x.t.Name} {counts[i].rooms}/{counts[i].kinds}")));
        var small = ships[0].s;
        var big = ships[^1].s;
        string[] lux = { "극장", "학교", "원심 거주구", "셔틀 격납고", "정원", "관측실" };
        bool smallLean = !small.Rooms.Any(r => RoomCatalog.Tier(r.Kind) >= 2) && small.Rooms.Any(r => r.Kind == RoomType.Freezer);
        bool bigLux = lux.All(n => big.Rooms.Any(r => r.Name == n));
        Check("소형은 꼭 필요한 방만 (냉동 창고 하나뿐) · 초대형은 극장 · 학교 · 원심 거주구 · 셔틀 · 정원 · 관측실", smallLean && bigLux,
            $"소형 특수 방 {string.Join(", ", small.Rooms.Where(r => r.Special != null).Select(r => r.Name))} · 초대형 호화 {lux.Count(n => big.Rooms.Any(r => r.Name == n))}/{lux.Length}");

        if (Environment.GetEnvironmentVariable("FSH_ONLY") == null) { // 진단: 수경 장면만 볼 때는 건너뛴다
        // ② v16.19 장갑 벽 · 차압 문 · 예비 회로 · 보조 간선 · 대표 설비 (하루 첫 시간)
        var v19 = new List<string>();
        bool v19ok = true;
        foreach (var key in BaseShips)
        {
            var w = World.CreateDefault(seed, 0, key);
            Run(w, SimTime.Hours(2));
            var crit = w.Ship.Rooms.Where(r => r.Type is RoomType.Reactor or RoomType.Power || r.Kind == RoomType.ComputerRoom).ToList();
            bool armored = crit.All(r => w.Ship.Walls.Any(kv => kv.Value.Armor < 0.99f && Cell.Dirs4.Any(d => w.Ship.RoomAt(kv.Key + d) == r)));
            int alt = w.Ship.Rooms.Count(r => r.AltCircuit >= 0);
            int bulk = w.Ship.Doors.Count(d => d.Bulkhead);
            int rings = w.Net.Rings.Count(x => x.kind == NetKind.Power);
            int sig = w.Ship.Furniture.Count(f => f.Room.Special != null && ShipClasses.Signature(f.Room.Kind).Contains(f.Type));
            bool ok = armored && alt >= 3 && (key is "Kestrel" or "Mirinae" || bulk >= 2 && rings >= 2);
            v19ok &= ok;
            v19.Add($"{w.Ship.Name} 장갑 벽 {w.Failsafe.ArmorWalls} · 두 갈래 급전 {alt} · 차압 문 {bulk} · 보조 간선 {rings} · 대표 설비 {sig}");
        }
        Check("v16.19 장갑 벽(중요한 방) · 두 갈래 급전 · 차압 문 · 보조 간선이 새 배에 깔린다", v19ok, string.Join(" / ", v19));

        // ③ 방 종류 등장 (카탈로그 + 생성기 표본)
        var (kindsSeen, sampled) = CatalogKinds();
        var all = Enum.GetValues<RoomType>().Where(t => t != RoomType.Corridor).ToList();
        RoomType[] eight = { RoomType.FuelCell, RoomType.ServerRoom, RoomType.Calibration, RoomType.PumpRoom, RoomType.AlgaeLab, RoomType.Navigation, RoomType.HeatStorage, RoomType.Security };
        var missing = all.Where(k => !kindsSeen.Contains(k)).ToList();
        var baseKinds = ships.SelectMany(x => x.s.Rooms.Select(r => r.Kind)).Where(k => k != RoomType.Corridor).Distinct().Count();
        Check("방 종류 등장 — 카탈로그+생성 50척에서 거의 전부 · 안 나오던 8종(연료전지 · 서버 · 교정 · 펌프 · 조류 · 항법 · 축열 · 보안) 모두",
            sampled >= 50 && missing.Count <= 2 && eight.All(kindsSeen.Contains),
            $"표본 {sampled}척 · {all.Count - missing.Count}/{all.Count}종 (기본 다섯 척만 {baseKinds}종) · 안 나옴: {(missing.Count == 0 ? "-" : string.Join(", ", missing.Select(RoomTypes.Name)))}");

        // ④ 다섯 척 하루 + 시작 물자 + 금속판
        var day = new List<string>();
        bool dayOk = true, plateOk = true, stockRise = true;
        int prevPlate = -1;
        var plateLines = new List<string>();
        foreach (var key in BaseShips)
        {
            var w = World.CreateDefault(seed, 0, key);
            int plate0 = w.Ship.CountStored(ItemKind.Plate), ore0 = w.Ship.CountStored(ItemKind.MetalOre), ration0 = w.Ship.CountStored(ItemKind.Ration);
            if (plate0 + ore0 / 2 < prevPlate) stockRise = false;
            prevPlate = plate0 + ore0 / 2;
            string err = "";
            int minPlate = plate0;
            float minRest = 1f, minFood = 1f;
            string restWho = "", foodWho = "";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (int h = 0; h < 72; h++)
                {
                    Run(w, SimTime.Hours(1));
                    minPlate = Math.Min(minPlate, w.Ship.CountStored(ItemKind.Plate));
                    foreach (var c in w.Crew.Where(c => !c.Dead))
                    {
                        if (c.Needs.Rest < minRest) { minRest = c.Needs.Rest; restWho = $"{c.Name} {h}시 {c.Room?.Name}({c.Job?.Label}) 침대 {c.Bed?.Room.Name}"; }
                        if (c.Needs.Food < minFood) { minFood = c.Needs.Food; foodWho = $"{c.Name} {h}시 {c.Room?.Name}({c.Job?.Label})"; }
                    }
                    if (h == 71) Console.WriteLine($"   {w.Ship.Name} 사흘 · 최저 휴식 {minRest * 100:0}% {restWho} · 최저 배부름 {minFood * 100:0}% {foodWho}");
                    if (h == 23) day.Add($"{w.Ship.Name} 하루 {sw.Elapsed.TotalSeconds:0.0}초 · 생존 {w.Crew.Count(c => !c.Dead)}/{w.Crew.Count}");
                    if (h == 23 && w.Crew.Any(c => c.Dead)) dayOk = false;
                }
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message; dayOk = false; }
            if (err != "") day.Add($"{key} 예외 {err}");
            if (minPlate <= 0) plateOk = false;
            plateLines.Add($"{w.Ship.Name} 판 {plate0}→최저 {minPlate} · 원료 {ore0} · 저장 식량 {ration0} · 되살림 {w.Scrap.Recovered}");
        }
        Check("다섯 척 모두 하루 정상 (사망 · 예외 없음)", dayOk, string.Join(" · ", day));
        Check("시작 물자는 크기에 맞게 · 금속판이 사흘 안에 바닥나지 않는다 (재활용실이 고철을 되살린다)", plateOk && stockRise, string.Join(" / ", plateLines));

        }

        // ⑤ 수경 재배실이 망가진 사흘
        {
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Hours(1));
            var hydroBeds = w.Ship.FurnitureOf(FurnitureType.GrowBed).Where(f => f.Room.Kind == RoomType.Hydroponics).ToList();
            foreach (var b in hydroBeds) { w.Machines.Break(b.Machine!, FaultKind.Wrecked); b.Machine!.Crop!.Growth = 0f; }
            // 먹을 것을 줄여 둔다 (냉장고 채소 · 끼니 · 창고 비상식량을 반쯤)
            foreach (var f in w.Ship.Containers)
            {
                f.Storage!.Take(ItemKind.Produce, f.Storage.Count(ItemKind.Produce) * 2 / 3);
                f.Storage.Take(ItemKind.Meal, f.Storage.Count(ItemKind.Meal) / 2);
                f.Storage.Take(ItemKind.Ration, f.Storage.Count(ItemKind.Ration) / 2);
            }
            float minFood = 1f;
            string hungry = "";
            for (int h = 0; h < 72; h++)
            {
                Run(w, SimTime.Hours(1));
                foreach (var c in w.Crew.Where(c => !c.Dead))
                    if (c.Needs.Food < minFood) { minFood = c.Needs.Food; hungry = $"{c.Name} {h}시 {c.Room?.Name}({c.Job?.Label}) 끼니 {w.Ship.CountStored(ItemKind.Meal)} 채소 {w.Ship.CountStored(ItemKind.Produce)} 비상 {w.Ship.CountStored(ItemKind.Ration)}"; }
            }
            Console.WriteLine($"   수경 고장 사흘 · 가장 배고팠던 때: {hungry}");
            var fs = w.FoodSources;
            int other = fs.In[(int)FoodSrc.Algae] + fs.In[(int)FoodSrc.Mushroom] + fs.In[(int)FoodSrc.Protein] + fs.In[(int)FoodSrc.Garden];
            int alive = w.Crew.Count(c => !c.Dead);
            bool comp = w.Automation.Book.Acts.Any(a => a.Key == "food.hydro");
            Check("수경 재배실이 망가져도 다른 식량원으로 사흘 버틴다 (사망 0 · 굶지 않음 · 다른 재배실 수확)",
                alive == w.Crew.Count && minFood > 0.05f && other > 0 && fs.In[(int)FoodSrc.Hydro] == 0,
                $"생존 {alive}/{w.Crew.Count} · 최저 배부름 {minFood * 100:0}% · 들어온 것 {fs.Summary()} · 수경 멎음 {fs.HydroDown}");
            Check("주컴퓨터 — 수경이 멎은 것을 읽고 다른 식량원으로 며칠 버티는지 셈해 알린다", comp && fs.Advised >= 1,
                $"조언 {fs.Advised} · 버틸 날 {fs.DaysLeft:0.0} · 기록: {w.Automation.Book.Acts.LastOrDefault(a => a.Key == "food.hydro")?.Judge}");
            Check("승무원 — 재배 담당이 다른 재배실(조류 · 버섯)을 한 번 더 돌보고, 조리사는 저장 식량 · 발효로 돌린다",
                fs.AltTends >= 1 && fs.In[(int)FoodSrc.Stored] + fs.In[(int)FoodSrc.Ferment] > 0,
                $"다른 재배실 돌봄 {fs.AltTends} · 저장 식량 {fs.In[(int)FoodSrc.Stored]} · 발효 {fs.In[(int)FoodSrc.Ferment]}");
        }

        // ⑥ 결정론
        uint H(string key) { var w = World.CreateDefault(seed, 0, key); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
        uint a1 = H("Hanbit"), a2 = H("Hanbit");
        Check("결정론 — 같은 시드 같은 지문 (한빛호)", a1 == a2, $"{a1:x8}/{a2:x8}");

        Console.WriteLine(_fails == 0 ? "\n✔ 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
