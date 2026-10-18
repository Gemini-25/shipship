using ShipEngine;

namespace Lab;

/// <summary>새 배 일곱 척 · 포장 식량 · 식량 나르기 · 다단 재배 선반 · 원자로 여럿 — docs/DEPTH.md (배)</summary>
public static partial class Program
{
    private static void ShipRules()
    {
        if (!Do("배")) return;
        Console.WriteLine("\n[배 일곱 척 · 식량 창고 · 재배 선반]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 배 일곱 척: 사람 수만큼 침대 · 모든 바닥이 문으로 이어진다 · 바깥문 · 식량이 항해를 버틴다 (세대선은 다 기르고 90일 비상) ──
        {
            var plan = new Dictionary<string, (int Crew, double Days)>
            {
                ["hwasal"] = (3, 60), ["didim"] = (6, 180), ["salpim"] = (12, 365), ["nareum"] = (8, 240),
                ["soedol"] = (10, 240), ["saetteul"] = (40, 365), ["hannuri"] = (100, double.PositiveInfinity),
            };
            var bad = new List<string>();
            foreach (var key in Layout.ShipKeys())
            {
                var L = Layout.Ship(key, Tables.Default);
                if (!plan.TryGetValue(key, out var p)) { bad.Add($"{L.Name}: 계획에 없는 배"); continue; }
                int beds = L.Furniture.Count(f => f.Type == "Bed");
                // 바닥 · 문이 한 덩어리 (문으로 모두 이어진다)
                var space = Enumerable.Range(0, L.N).Where(L.IsSpace).ToHashSet();
                var seen = new HashSet<int> { space.First() }; var q = new Queue<int>(); q.Enqueue(space.First());
                Span<int> nb = stackalloc int[4];
                while (q.Count > 0) { int c = q.Dequeue(); L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && space.Contains(j) && seen.Add(j)) q.Enqueue(j); }
                // 식량: 보관함 250kg (포장) · 냉장고 40 · 배식기 20 → 몰 (1kg = 3.33몰) · 재배 (칸 × 단 × 0.1몰/일)
                var t = Tables.Default;
                double foodMol = L.Furniture.Sum(f => t.Equip(f.Type).Parts.GetValueOrDefault("food")) * 3.33;
                double grow = L.Furniture.Sum(f => t.Dev(f.Type) is { Role: "grow" } d ? f.Cells.Length * Math.Max(1, d.Tiers) * Life.CropMolPerM2Day : 0);
                double need = p.Crew * 3.6;
                double days = need > grow ? foodMol / (need - grow) : double.PositiveInfinity;
                bool foodOk = double.IsPositiveInfinity(p.Days) ? grow >= need && foodMol / need >= 90 : days >= p.Days;
                if (beds != p.Crew) bad.Add($"{L.Name}: 침대 {beds} ≠ {p.Crew}");
                if (seen.Count != space.Count) bad.Add($"{L.Name}: 닿지 않는 바닥 {space.Count - seen.Count}칸");
                if (!L.Doors.Any(d => d.External)) bad.Add($"{L.Name}: 바깥문 없음");
                if (!foodOk) bad.Add($"{L.Name}: 식량 {days:0}일 (재배 {grow / need * 100:0}%) < {p.Days}일");
                if (Debug) Console.WriteLine($"    {L.Name} {L.W}×{L.H} · 침대 {beds} · 바닥 {space.Count} · 식량 {(double.IsInfinity(days) ? "끝없이" : $"{days:0}일")} · 재배 {grow / need * 100:0}%");
            }
            Check("배 일곱 척: 사람 수만큼 침대 · 모든 바닥이 문으로 이어진다 · 바깥문 · 식량이 항해 길이를 버틴다 (세대선은 다 기르고 90일 비상)",
                bad.Count == 0 && Layout.ShipKeys().Count() == 7, bad.Count == 0 ? $"{Layout.ShipKeys().Count()}척 모두 맞다" : string.Join(" · ", bad));
        }

        // ── 비상 호흡기: 배마다 산소 마스크가 사람 수보다 많고 · 어느 침대에서든 마스크함이 걸어서 25칸 안 (ISS — 모듈마다 휴대용 호흡기)
        //    (기준 평가가 찾은 결함 — 살핌호에는 마스크함이 없었고 새터호는 40명에 마스크 6개: 큰 구멍 뒤 옅은 공기에 쓸 마스크가 없어 모두 숨졌다) ──
        {
            var bad = new List<string>(); var note = new List<string>();
            foreach (var key in Layout.ShipKeys())
            {
                var L = Layout.Ship(key, Tables.Default);
                var boxes = L.Furniture.Where(f => f.Type == "OxygenMaskBox" && f.Cells.Length > 0).ToList();
                int beds = L.Furniture.Count(f => f.Type == "Bed");
                var dist = Enumerable.Repeat(-1, L.N).ToArray(); var q = new Queue<int>();
                foreach (var b in boxes) foreach (int c in b.Cells) { Span<int> nb0 = stackalloc int[4]; L.Neighbors(c, nb0); foreach (int j in nb0) if (j >= 0 && L.IsSpace(j) && dist[j] < 0) { dist[j] = 0; q.Enqueue(j); } }
                Span<int> nb = stackalloc int[4];
                while (q.Count > 0) { int c = q.Dequeue(); L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && dist[j] < 0) { dist[j] = dist[c] + 1; q.Enqueue(j); } }
                int far = 0;
                foreach (var b in L.Furniture.Where(f => f.Type == "Bed"))
                {
                    int best = int.MaxValue;
                    foreach (int c in b.Cells) { L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && dist[j] >= 0) best = Math.Min(best, dist[j]); }
                    far = Math.Max(far, best);
                }
                int masks = boxes.Count * ShipPlan.MasksPerBox;
                note.Add($"{L.Name} 마스크 {masks}/{beds}명 · 가장 먼 침대 {far}칸");
                if (masks < beds || far > 25) bad.Add($"{L.Name}: 마스크 {masks} · 사람 {beds} · 가장 먼 침대 {far}칸");
            }
            Check("비상 호흡기: 배마다 산소 마스크가 사람 수보다 많고 · 어느 침대에서든 마스크함이 걸어서 25칸 안",
                bad.Count == 0, bad.Count == 0 ? string.Join(" · ", note) : string.Join(" · ", bad));
        }

        // ── 내장된 도면 = 생성기가 지금 만드는 도면 (손으로 고치지 않았다 · 생성기는 결정론) ──
        {
            var diff = new List<string>();
            foreach (var (key, make) in Designs)
            {
                var plan = make(); plan.Build();
                string want = plan.Json(), have = DataFiles.Text($"ships/{key.ToLowerInvariant()}.json");
                if (want != have.TrimEnd('\n', '\r')) diff.Add(key);
            }
            Check("내장된 배 도면은 생성기(Lab --shipgen)가 지금 만드는 것과 같다", diff.Count == 0, diff.Count == 0 ? $"{Designs.Length}척 같다" : "다르다: " + string.Join(", ", diff) + " — Lab --shipgen 으로 다시 만든다");
        }

        // ── 포장 식량은 상하지 않는다: 따뜻하고 습한 방의 식량 보관함은 그대로 · 같은 방 바닥에 둔 음식은 썩는다 ──
        {
            var s = ShipWorld.Create("Didim", 3);
            var f = s.Get<Field>(); var L = s.Layout;
            var locker = L.Furniture.First(fu => fu.Type == "FoodLocker");
            int lc = locker.Cells[0];
            int open = L.Rooms[locker.Room].Cells.First(c => L.FurnitureAt[c] < 0);
            f.AddStore(open, s.Tables.sFood, 50);
            foreach (int c in L.Rooms[locker.Room].Cells) f.AddGas(c, s.Tables.gH2O, 1.2, f.Temp(c)); // 습한 방
            double l0 = f.Stored(s.Tables.sFood, lc), o0 = f.Stored(s.Tables.sFood, open);
            s.Run(Time.Hours(2)); // 썩는 빠르기는 늘 같다 — 이틀을 기다리지 않아도 두 시간이면 바닥 음식이 준다
            double l1 = f.Stored(s.Tables.sFood, lc), o1 = f.Stored(s.Tables.sFood, open);
            Check("포장 식량(식량 보관함)은 따뜻하고 습한 방에서도 상하지 않는다 · 같은 방 바닥에 둔 음식은 썩는다", Math.Abs(l1 - l0) < 1e-9 && o1 < o0 * 0.9998,
                $"보관함 {l0:0.0} → {l1:0.0}몰 · 바닥 음식 {o0:0.000} → {o1:0.000}몰 (두 시간)");
        }

        // ── 식량 나르기: 냉장고 · 배식기가 비면 로봇이 식량 보관함에서 상자를 날라 채운다 (장부는 그대로 — 배 안에서 옮길 뿐) ──
        {
            var s = ShipWorld.Crewed("Didim", 4, 2, 2);
            var f = s.Get<Field>(); var L = s.Layout; var m = s.Get<Mind>(); var w = s.Get<Work>();
            var points = L.Furniture.Where(fu => fu.Type is "Fridge" or "MealDispenser").ToList();
            var lockers = L.Furniture.Where(fu => fu.Type == "FoodLocker").ToList();
            s.Run(5);
            foreach (var fu in points) foreach (int c in fu.Cells) f.TakeStore(c, s.Tables.sFood, f.Stored(s.Tables.sFood, c)); // 다 먹었다
            double Food(IEnumerable<FurnitureDef> fs) => fs.Sum(fu => fu.Cells.Sum(c => f.Stored(s.Tables.sFood, c)));
            // 비운 몫은 장부 밖 — 처음 재고를 다시 잡는다
            s.Book.Started = false;
            double locker0 = Food(lockers);
            bool carried = false;
            for (int k = 0; k < Time.Hours(3); k++) { s.Step(); carried |= m.Agents.Any(a => !a.IsPerson && a.C.Holding == m.iRation); }
            double filled = Food(points), locker1 = Food(lockers);
            Check("식량 나르기: 냉장고 · 배식기가 비면 로봇이 식량 보관함에서 상자를 날라 채운다 (식량 장부는 맞는다)",
                carried && filled >= Work.RestockBelow && locker1 < locker0 - 1 && Math.Abs(w.Other(s, (int)Res.Food)) < 1e-6,
                $"로봇이 들고 감 {carried} · 냉장고 · 배식기 0 → {filled:0}몰 · 보관함 {locker0:0} → {locker1:0}몰 · 장부 어긋남 {w.Other(s, (int)Res.Food):0e0}");
        }

        // ── 다단 재배 선반: 자라는 넓이 = 칸 × 단 (4칸 × 6단 = 24m²) ──
        {
            var s = ShipWorld.Create("Salpim", 2);
            var life = s.Get<Life>(); var net = s.Get<Network>();
            int k = Enumerable.Range(0, life.Crops.Count).First(i => net.Devices[life.Crops[i].Device].Type == "GrowRack");
            double area = life.CropArea(net, k);
            Check("다단 재배 선반: 자라는 넓이 = 칸 × 단 (4칸 × 6단 = 24m²)", Math.Abs(area - 24) < 1e-9, $"{area:0.0}m²");
        }

        // ── 재배 선반 전등 열은 선반 칸에 고루 퍼진다 — 공조가 도는 재배실에 '너무 덥다'(45℃) 칸이 없다 (예전: 2.4kW가 첫 칸에 몰렸다) ──
        {
            var s = ShipWorld.Create("Salpim", 5);
            var f = s.Get<Field>(); var L = s.Layout;
            var farm = L.Rooms.First(r => r.Type == "Hydroponics");
            s.Run(Time.Hours(2));
            double hottest = farm.Cells.Max(c => f.Temp(c)), mean = farm.Cells.Average(c => f.Temp(c));
            Check("재배 선반 전등 열은 선반 칸에 고루 퍼진다 — 공조가 도는 재배실에 45℃ 넘는 칸이 없다", hottest < 318 && mean < 303,
                $"재배실 가장 더운 칸 {hottest - 273.15:0.0}℃ · 평균 {mean - 273.15:0.0}℃");
        }

        // ── 주컴퓨터 · 대형 냉각 펌프의 열은 냉각 고리로 — 그 칸이 45℃를 넘지 않는다 (예전: 방 공기로만 식혀 53℃ · 46℃) ──
        {
            var s = ShipWorld.Create("Hannuri", 4);
            var f = s.Get<Field>(); var L = s.Layout;
            s.Run(Time.Hours(1));
            var cells = L.Furniture.Where(fu => fu.Type is "MainComputer" or "LargeCoolantPump").SelectMany(fu => fu.Cells).ToList();
            double hottest = cells.Max(c => f.Temp(c));
            Check("주컴퓨터 · 대형 냉각 펌프의 열은 냉각 고리로 — 그 칸 공기가 45℃를 넘지 않는다", hottest < 318, $"가장 더운 칸 {hottest - 273.15:0.0}℃");
        }

        // ── 원자로가 여럿이면 저마다 맡은 몫만큼 노심이 덥다 (예전: 원자로마다 배 전체 출력의 열을 다 더했다) ──
        {
            var s = ShipWorld.Create("Hannuri", 2);
            var f = s.Get<Field>(); var net = s.Get<Network>();
            for (int d = 0; d < net.Devices.Count; d++) if (net.Devices[d].Def.Role == "pump") net.SetOn(d, false); // 냉각을 멈추고 노심에 쌓이는 열만 잰다
            var cores = net.Devices.Where(d => d.Def.Role == "reactor").ToList();
            s.Run(3);
            double E() => cores.Sum(d => f.Cs[d.Cell] * f.Ts[d.Cell]);
            double e0 = E(), kwSum = 0;
            const int Ticks = 25;
            for (int k = 0; k < Ticks; k++) { s.Step(); kwSum += net.ReactorKw; }
            double made = E() - e0, want = 2 * 1000 * kwSum * Time.SecondsPerTick; // 열효율 1/3 — 전력의 두 배가 노심 열
            Check("원자로가 여럿이면 저마다 맡은 몫만큼 노심이 덥다 (합쳐서 낸 전력의 두 배 — 원자로 수만큼 겹치지 않는다)",
                cores.Count >= 2 && made > want * 0.8 && made < want * 1.2, $"원자로 {cores.Count} · 노심 열 {made / 1e6:0.00}MJ / 기대 {want / 1e6:0.00}MJ");
        }
    }
}
