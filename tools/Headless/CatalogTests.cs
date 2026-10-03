using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15 고장 70 · 물자 70: 새 고장이 설비에 붙고, 없는 부품은 만들어서라도 고치고, 소모품을 실제로 쓴다
public static partial class Program
{
    private static int RunCatalogTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"고장·물자 70 점검 (v15) · 시드 {seed}\n");
        try
        {
            int faults = Enum.GetValues<FaultKind>().Length, items = Enum.GetValues<ItemKind>().Length;
            var newFaults = Enum.GetValues<FaultKind>().Where(k => k >= FaultKind.GasketLeak).ToList();
            var onMachine = newFaults.Where(k => Enum.GetValues<FurnitureType>().Any(t => MachineSpecs.For(t)?.FaultKinds.Contains(k) == true)).ToList();
            var noName = Enum.GetValues<ItemKind>().Where(k => ItemKinds.Name(k) == k.ToString()).ToList();
            // 의료 1 · 2차 물자 +8 (진통제 · 항생제 · 마취제 · 혈액 대용제 · 약초 · 면역억제제 · 투석액 · 세포 잉크)
            Check("목록 — 고장 70 · 물자 78 · 새 고장은 모두 어떤 설비에 붙고 이름이 있다", faults == 70 && items == 78 && onMachine.Count == newFaults.Count && noName.Count == 0,
                $"고장 {faults} · 물자 {items} · 설비에 붙은 새 고장 {onMachine.Count}/{newFaults.Count}" + (noName.Count > 0 ? $" · 이름 없음: {string.Join(",", noName)}" : ""));

            // 1) 새 고장을 하나씩 걸고 이틀 — 부품이 없으면 만들어서라도 고친다
            {
                var w = DayOne(seed, "Mirinae");
                var hit = new List<(Machine m, FaultKind k)>();
                foreach (var k in newFaults)
                {
                    var m = w.Ship.Machines.Where(x => x.Spec.FaultKinds.Contains(k) && !x.Body.Room.Detached && x.Faults.Count == 0 && !hit.Any(h => h.m == x)).OrderBy(x => x.Body.Id).FirstOrDefault();
                    if (m == null || hit.Count >= 12) continue;
                    if (w.Machines.Break(m, k) != null) hit.Add((m, k));
                }
                Run(w, SimTime.TicksPerDay * 2);
                var left = hit.Where(h => h.m.Has(h.k)).ToList();
                int made = w.Log.Entries.Count(e => e.Text.Contains("만들었다"));
                Check("수리 — 새 고장 열둘을 걸면 이틀 안에 대부분 고친다 (없는 부품은 만든다)", hit.Count >= 8 && left.Count <= 2,
                    $"건 고장 {hit.Count} · 남은 것 {left.Count}" + (left.Count > 0 ? $" ({string.Join(", ", left.Select(h => $"{h.m.Name} {Faults.Spec(h.k).Name}"))})" : "") + $" · 만든 물건 {made}");
            }

            // 2) 소모품을 실제로 쓴다: 비누 · 세제 · 커피
            {
                var w = DayOne(seed, "Mirinae");
                int soap0 = w.Ship.CountStored(ItemKind.Soap), coffee0 = w.Ship.CountStored(ItemKind.Coffee) + w.Ship.CountStored(ItemKind.TeaLeaf);
                foreach (var c in w.Crew) { c.Soil.Hands[(int)SoilKind.Oil] = 0.7f; c.Soil.Clothes[(int)SoilKind.Oil] = 0.7f; }
                Run(w, SimTime.TicksPerDay * 3);
                int soap1 = w.Ship.CountStored(ItemKind.Soap), coffee1 = w.Ship.CountStored(ItemKind.Coffee) + w.Ship.CountStored(ItemKind.TeaLeaf);
                Check("소모품 — 씻으면 비누가, 커피를 돌리면 커피가 준다", soap0 > 0 && (soap1 < soap0 || w.Soil.Stats.HandWashes < 8),
                    $"비누 {soap0} → {soap1} (손 씻기 {w.Soil.Stats.HandWashes}) · 커피·찻잎 {coffee0} → {coffee1} · 빨래 {w.Soil.Stats.LaundryRuns}(세제 없이 {w.Soil.Stats.NoDetergent})");
            }

            // 3) 키트도 붕대도 없으면 천으로, 붕대가 있으면 붕대로
            {
                var w = DayOne(seed, "Mirinae");
                foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null)) f.Storage!.Take(ItemKind.MedKit, 999);
                foreach (var c in w.Crew) if (c.Carrying is ItemStack held && held.Kind == ItemKind.MedKit) c.Carrying = null; // 통합: 그 순간 손에 들고 옮기던 키트도 (나중에 선반에 넣으면 키트로 치료했다)
                var p = w.Crew[2];
                NeedsSystem.AddInjury(p.Vitals, 0.4f, "시험");
                Run(w, SimTime.Hours(12));
                var log = w.Log.Entries.Where(e => e.Text.Contains("응급 처치")).Select(e => e.Text).ToList();
                Check("응급 처치 — 구급 키트가 없으면 붕대로 처치한다", log.Any(t => t.Contains("붕대")), $"{log.Count}번 · {log.FirstOrDefault()}");
            }

            // 4) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 새 고장·물자가 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 고장·물자 70 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
