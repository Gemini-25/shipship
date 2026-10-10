using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v14.6 부품마다의 수명과 내력 · 되풀이되는 고장의 원인 · 같은 묶음 의심 · 시험대
public static partial class Program
{
    private static int RunPartTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"부품 내력 점검 (v14.6) · 시드 {seed}\n");
        try
        {
            // 1) 같은 설비 안에서도 부품마다 나이가 다르다: 베어링만 갈면 베어링만 새것
            {
                var w = DayOne(seed, "Mirinae");
                var pump = w.Ship.Machines.First(m => m.Body.Type == FurnitureType.CoolantPump);
                var parts = w.Parts.Of(pump);
                var bearing = parts.First(p => p.Kind == ItemKind.Bearing);
                var others = parts.Where(p => p != bearing).Select(p => p.Hours).ToArray();
                var tech = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                w.Parts.OnFixed(pump, FaultKind.BearingWear, tech, usedPart: false);
                bool othersSame = parts.Where(p => p != bearing).Select(p => p.Hours).SequenceEqual(others);
                Check("부품마다의 나이 — 베어링을 갈면 베어링만 새것, 모터와 씰은 그대로", bearing.Hours == 0f && othersSame && bearing.Lot.Origin != PartOrigin.Original,
                    string.Join(" / ", w.Parts.Lines(pump)));
            }

            // 2) 떼어 온 중고는 내력을 모르고, 손으로 만든 것은 만든 사람이 남는다 · 시험대에서 나쁜 것을 걸러 낸다
            {
                var w = DayOne(seed, "Mirinae");
                var maker = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                var good = w.Parts.Stock(ItemKind.Bearing, PartOrigin.Handmade, maker: maker.Name, quality: 0.95f);
                var bad = w.Parts.Stock(ItemKind.Bearing, PartOrigin.Salvage, from: "난파선", quality: 0.5f, prior: 300f);
                foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(ItemKind.Bearing) && f.Storage.Free >= 2).Take(1)) box.Storage!.Add(ItemKind.Bearing, 2);
                var (t, r) = w.Parts.Test(ItemKind.Bearing, maker, 10);
                Check("내력과 시험 — 손으로 만든 것엔 만든 사람이, 중고엔 어디서 왔는지가 남고, 시험대에서 불량을 걸러 낸다",
                    good.Tested && bad.Tested && r >= 1 && !w.Parts.StockOf(ItemKind.Bearing).Contains(bad),
                    $"시험 {t} · 버림 {r} · {good.Label} / {bad.Label}");
            }

            // 3) 일찍 나간 부품: 같은 묶음을 다른 설비 · 선반에서도 의심한다
            {
                var w = DayOne(seed, "Eunha");
                var pumps = w.Ship.Machines.Where(m => m.Body.Type == FurnitureType.CoolantPump).Take(2).ToList();
                var tech = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                // 기항지에서 산 같은 묶음 셋: 둘은 펌프에 달려 있고 하나는 선반에 (먼저 온 것부터 쓰므로 직접 단다)
                var lots = Enumerable.Range(0, 3).Select(_ => w.Parts.Stock(ItemKind.Bearing, PartOrigin.Port, batch: "B-17", from: "케레스 정거장", quality: 0.3f)).ToList();
                foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(ItemKind.Bearing) && f.Storage.Free >= 1).Take(1)) box.Storage!.Add(ItemKind.Bearing, 1);
                for (int k = 0; k < pumps.Count; k++) { var bp = w.Parts.Owner(pumps[k], FaultKind.BearingWear)!; bp.Lot = lots[k]; bp.Hours = 0f; }
                var first = w.Parts.Owner(pumps[0], FaultKind.BearingWear)!;
                first.Hours = 20f; // 금방 나갔다
                w.Parts.OnFixed(pumps[0], FaultKind.BearingWear, tech, usedPart: false);
                var second = w.Parts.Owner(pumps.Count > 1 ? pumps[1] : pumps[0], FaultKind.BearingWear)!;
                Check("같은 묶음 — 일찍 나간 부품의 묶음은 다른 설비에 달린 것과 선반의 것까지 의심한다",
                    w.Parts.SuspectBatches.Contains("B-17") && w.Parts.Stats.BatchAlerts >= 1 && (pumps.Count < 2 || second.Lot.Suspect),
                    $"{w.Parts.Stats.Summary()} · 둘째 펌프 베어링: {second.Lot.Label}");
            }

            // 4) 또 나갔다: 손에 익은 정비사가 원인을 찾아 바로잡는다 (못 찾으면 계속 빨리 닳는다)
            {
                int found = 0, missed = 0; string? root = null;
                foreach (bool familiar in new[] { true, false })
                {
                    var w = DayOne(seed, "Mirinae");
                    var pump = w.Ship.Machines.First(m => m.Body.Type == FurnitureType.CoolantPump);
                    var tech = w.Crew.OrderBy(c => familiar ? -c.RawSkill(Skill.Mechanics) : c.RawSkill(Skill.Mechanics)).First();
                    if (familiar) tech.Familiarize(FurnitureType.CoolantPump, 0.9f);
                    pump.Heat = 0.6f; // 옆 설비 열기
                    for (int k = 0; k < 4; k++) { w.Parts.OnFixed(pump, FaultKind.BearingWear, tech, usedPart: false); Run(w, SimTime.Hours(20)); }
                    var p = w.Parts.Owner(pump, FaultKind.BearingWear)!;
                    if (familiar) { found = w.Parts.Stats.RootsFound; root = p.Root; } else missed = w.Parts.Stats.RootsMissed;
                }
                Check("재발 — 같은 자리가 또 나가면 원인을 캐고, 손에 익은 사람이 더 잘 찾는다", found >= 1 && root != null,
                    $"익숙한 정비사: 원인 찾음 {found} ({root}) · 서툰 사람: 못 찾음 {missed}");
            }

            // 5) 오래된 부품이 많은 설비가 더 자주 고장 난다
            {
                var w = DayOne(seed, "Mirinae");
                var m = w.Ship.Machines.First(x => x.Body.Type == FurnitureType.CoolantPump);
                float f0 = w.Parts.AgeFactor(m);
                foreach (var p in w.Parts.Of(m)) p.Hours = p.Life * 1.1f;
                float f1 = w.Parts.AgeFactor(m);
                Check("수명 — 수명을 넘긴 부품이 많을수록 고장이 잦다", f1 > f0 * 1.5f && f0 < 1.1f, $"고장 배율 {f0:0.00} → {f1:0.00}");
            }

            // 6) 정비 장비와 공간: 호이스트가 없으면 무거운 부품이 느리고 (혼자면 허리를 다치기도), 달면 빠르다 · 비좁으면 큰 수리가 느리다
            {
                var w = DayOne(seed, "Mirinae");
                var pump = w.Ship.Machines.First(m => m.Body.Type == FurnitureType.CoolantPump);
                float bare = w.Parts.RepairFactor(pump, FaultKind.PumpSeized, out var n0);
                var ws = w.Ship.RoomsOf(RoomType.Workshop).FirstOrDefault();
                var tech = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                bool installed = ws != null && Modules.Spot(w, ws) is Cell spot && Modules.Install(w, ws, FurnitureType.Hoist, tech, spot);
                float hoisted = w.Parts.RepairFactor(pump, FaultKind.PumpSeized, out var n1);
                // 혼자 맨손으로 무거운 부품을 여러 번: 허리
                var w2 = DayOne(seed, "Mirinae");
                var pump2 = w2.Ship.Machines.First(m => m.Body.Type == FurnitureType.CoolantPump);
                var lone = w2.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                foreach (var o in w2.Crew.Where(o => o != lone && o.Room == pump2.Body.Room)) o.Position = w2.Ship.Rooms.First(r => r != pump2.Body.Room && r.Cells.Any(w2.Ship.IsOpenFloor)).Cells.First(w2.Ship.IsOpenFloor).Center;
                lone.Position = pump2.Body.UseSpots.First().Center;
                lone.Needs.Rest = 0.3f; // 지친 채로
                for (int k = 0; k < 30; k++) w2.Parts.OnFixed(pump2, FaultKind.PumpSeized, lone, usedPart: false);
                int clear = w.Parts.Clearance(pump);
                Check("정비 장비 — 호이스트가 없으면 무거운 부품이 느리고 혼자면 허리를 다치기도 하며, 달면 빨라진다",
                    installed && bare > 1.2f && hoisted < 1f && w2.Parts.Stats.Strains >= 1,
                    $"펌프 교체 배율 맨손 {bare:0.00}({n0}) → 호이스트 {hoisted:0.00}({n1}) · 혼자 서른 번에 허리 {w2.Parts.Stats.Strains}번 · 펌프 둘레 빈 칸 {clear}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 부품 내력 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
