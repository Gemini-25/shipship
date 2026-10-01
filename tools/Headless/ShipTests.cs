using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.9 배 종류: 뼈대 · 용도 · 설계사 · 시작 상태 · 숨은 이야기 · 갈라짐 · 침대 교대
//   시드 --shiptest            점검
//   시드 --shiptest --dump=키   설계도 보기
//   시드 --shiptest --sweep     생성기 조합을 모두 만들어 본다
public static partial class Program
{
    private static int RunShipTest(int seed)
    {
        _fails = 0;
        var argv = Environment.GetCommandLineArgs();
        if (argv.FirstOrDefault(a => a.StartsWith("--dump=")) is string dump)
        {
            var t = ShipCatalog.Find(dump[7..]);
            if (t == null) { Console.WriteLine("없음"); return 1; }
            Console.WriteLine($"{t.Key} {t.Name} {t.Crew}인 · {t.Note}");
            Console.WriteLine(t.Ascii);
            var w = World.CreateDefault(seed, 0, t.Key);
            Console.WriteLine($"방 {w.Ship.Rooms.Count} · 구획 {w.Ship.Compartments} · 문 {w.Ship.Doors.Count} · 격자 {w.Ship.Grid.Width}x{w.Ship.Grid.Height}");
            return 0;
        }
        if (argv.FirstOrDefault(a => a.StartsWith("--f1s=")) is string f1s) return F1sPower(seed, f1s[6..], 120); // 임시
        if (argv.FirstOrDefault(a => a.StartsWith("--diag=")) is string diag)
        {
            var w = argv.Contains("--fresh") ? World.CreateDefault(seed, 0, diag[7..]) : DayOne(seed, diag[7..]);
            bool fire = !argv.Contains("--nofire");
            if (fire) w.Fire.Ignite(w.Ship.RoomsOf(RoomType.Galley).First().Cells.First(w.Ship.IsOpenFloor), 0.6f);
            for (int h = 0; h < 12; h++)
            {
                Run(w, SimTime.Hours(1));
                Console.WriteLine($"-- {SimTime.HourOfDay(w.Tick):0.0}시 불 {w.Fire.Count} · 원자로 {w.Power.ReactorOnline} · 위기 {Crisis.Level(w)} {string.Join(",", Crisis.Now(w).Reasons)} · 급한 일 {string.Join(",", w.Board.Open.Where(o => o.Urgency >= 0.9f).Take(3).Select(o => o.Title))}");
                foreach (var c in w.Crew)
                {
                    Console.WriteLine($"   {c.Name,-8} 휴식 {c.Needs.Rest * 100:0} 배 {c.Needs.Food * 100:0} {c.Pose} {c.Room?.Name} {c.Cell} · {c.Job?.Label} · 침대 {c.Bed?.Label}/{c.Bed?.Room.Name} 예약 {c.Bed?.ReservedBy?.Name} 취침 {c.Schedule.SleepStart:0} 길 {(c.Bed != null ? w.Paths.Find(c.Cell, c.Bed.UseSpots[0])?.Count ?? -1 : -2)} · "
                        + string.Join(" | ", c.LastEvaluations.Take(3).Select(e => $"{e.Activity.Label} {e.Score:0.00} {e.Reason}")));
                }
            }
            foreach (var c in w.Crew.Where(c => c.Bed != null))
                foreach (var d in c.Bed!.Room.Doors) Console.WriteLine($"   {c.Bed.Room.Name} 문 {d.Cell} 잠김 {d.Locked} 용접 {d.Welded} 열림 {d.Openness:0.0} 바깥 {d.IsExternal} · 침대 자리 {string.Join(",", c.Bed.UseSpots)} 흐름 {w.Paths.Flood(c.Cell, c.PathProfile).Get(c.Bed.UseSpots[0])}");
            foreach (var c in w.Crew.Where(c => c.Job?.Label == "대기"))
            {
                var fl = w.Paths.Flood(c.Cell, c.PathProfile);
                var fd = w.Paths.Flood(c.Cell, PathProfile.Default);
                int n1 = 0, n2 = 0;
                for (int i = 0; i < w.Ship.Grid.CellCount; i++) { var cc = w.Ship.Grid.CellAt(i); if (fl.Reachable(cc)) n1++; if (fd.Reachable(cc)) n2++; }
                Console.WriteLine($"   대기 {c.Name} {c.Cell} 칸 {w.Ship.Grid.Kind(c.Cell)} 걸을 수 {w.Ship.IsWalkable(c.Cell)} · 닿는 칸 {n1}/{n2} · 막힘 {c.PathBlocked} · 두려움 {c.Memory.AnyFear} · 문 {w.Ship.DoorAt(c.Cell)?.Cell} 가구 {w.Ship.FurnitureAt(c.Cell)?.Label}");
                var messC = w.Ship.RoomsOf(RoomType.Mess).First().Cells.First(w.Ship.IsOpenFloor);
                var pth = w.Paths.Find(c.Cell, messC, c.PathProfile);
                Console.WriteLine($"      식당 길 {pth?.Count}: " + string.Join(" ", (pth ?? new List<Cell>()).Where(x => w.Ship.DoorAt(x) != null).Select(x => $"{x}[{w.Ship.DoorAt(x)!.RoomA?.Name}/{w.Ship.DoorAt(x)!.RoomB?.Name} 잠김 {w.Ship.DoorAt(x)!.Locked}]")) + $" · 지나는 방 {string.Join(">", (pth ?? new List<Cell>()).Select(x => w.Ship.RoomAt(x)?.Name).Where(n => n != null).Distinct())}");
                foreach (var e in w.Log.Entries.Where(e => e.CrewId == c.Id).TakeLast(6)) Console.WriteLine($"      {SimTime.HourOfDay(e.Tick):0.00}시 {e.Text}");
                foreach (var d in w.Ship.Doors.Where(d => Math.Abs(d.Cell.X - c.Cell.X) + Math.Abs(d.Cell.Y - c.Cell.Y) <= 3)) Console.WriteLine($"      곁의 문 {d.Cell} {d.RoomA?.Name}/{d.RoomB?.Name} 잠김 {d.Locked} 용접 {d.Welded} 열림 {d.Openness:0.0} 격벽 {d.Bulkhead} 영역 {w.Body.DoorOf(d)?.Zone}");
            }
            Console.WriteLine($"   내력 {w.Origin.Info?.Designer} {w.Origin.Info?.Start} · 원자로 온도 {w.Power.ReactorTemperature:0} · 냉각 {w.Power.CoolingCapacity:0}kW · 컴퓨터 {w.Automation.MainOnline}");
            foreach (var o in w.Board.Open.Where(o => o.Kind is WorkKind.Repair or WorkKind.Maintain).Take(12)) Console.WriteLine($"   작업 {o.Title} 급함 {o.Urgency:0.00} 막힘 {o.BlockedReason} · {o.Detail} · 맡은 {o.Assignee?.Name}");
            foreach (var m in w.Ship.Machines.Where(m => !m.Powered)) Console.WriteLine($"   전기 없음: {m.Name} {m.Body.Room.Name} 회로 {m.Body.Room.Circuit} 고장 {m.Faults.Count}");
            return 0;
        }
        if (argv.Contains("--sweep"))
        {
            int bad = 0, total = 0;
            foreach (var p in ShipInfos.GenPurposes)
                foreach (var f in ShipInfos.GenFrames)
                    foreach (int n in new[] { 2, 4, 6, 8, 9, 12, 20, 30, 40, 48, 60 })
                        foreach (int s in new[] { seed, seed + 1, seed + 2 })
                        {
                            total++;
                            try { var t = ShipGenerator.Template(p, f, n, s); ShipBuilder.FromAscii(t.Name, t.Ascii); }
                            catch (Exception e) { bad++; Console.WriteLine($"  ✘ {ShipGenerator.KeyFor(p, f, n, s)}: {e.Message}"); }
                        }
            Console.WriteLine($"만듦 {total - bad}/{total}");
            return bad == 0 ? 0 : 1;
        }
        // --part=4,8 : 그 부분만 (진단용)
        var parts = argv.FirstOrDefault(a => a.StartsWith("--part="))?[7..].Split(',').Select(int.Parse).ToHashSet();
        bool On(int k) => parts == null || parts.Contains(k);
        Console.WriteLine($"배 종류 점검 (v16.9) · 시드 {seed}\n");
        try
        {
            // 1) 대표 배 6척: 뼈대가 저마다 다르고, 필수 설비가 다 있고, 모든 방에 길이 닿는다
            if (On(1))
            {
                var mine = ShipCatalog.All.Where(t => !t.Legacy).ToList();
                var frames = mine.Select(t => t.Meta.Frame).Distinct().Count();
                var bad = new List<string>();
                foreach (var t in ShipCatalog.All)
                {
                    var ship = ShipBuilder.FromAscii(t.Name, t.Ascii);
                    var miss = Essentials(ship);
                    if (miss.Count > 0) bad.Add($"{t.Name}: {string.Join(",", miss)} 없음");
                    var cut = Unreached(ship);
                    if (cut.Count > 0) bad.Add($"{t.Name}: {string.Join(",", cut)}에 길 없음");
                }
                Check("대표 배 6척 — 뼈대 여섯 · 필수 설비 · 모든 방에 길", mine.Count == 6 && frames == 6 && bad.Count == 0,
                    string.Join(" · ", mine.Select(t => $"{t.Name}({ShipInfos.Name(t.Meta.Frame)} {ShipInfos.Name(t.Meta.Purpose)} {t.Crew}인)")) + (bad.Count > 0 ? " · " + string.Join(" / ", bad) : ""));
                var crews = ShipCatalog.All.Select(t => t.Crew).ToList();
                Check("크기 공백 — 2 · 8~9 · 40인 배가 있다 (60인은 생성)", crews.Contains(2) && crews.Any(c => c is 8 or 9) && crews.Any(c => c >= 40), string.Join(",", crews.OrderBy(c => c)));
            }

            // 2) 생성기: 옛 키는 그대로, 새 키는 용도 · 뼈대 · 2~60인
            if (On(2))
            {
                var old = ShipCatalog.Find(ShipGenerator.KeyFor(12, seed))!;
                var mining = Enumerable.Range(0, 4).Select(i => ShipGenerator.Template(ShipPurpose.Mining, ShipFrame.Linear, 12, seed + i)).ToList();
                var general = Enumerable.Range(0, 4).Select(i => ShipGenerator.Template(ShipPurpose.General, ShipFrame.Linear, 12, seed + i)).ToList();
                int Fav(IEnumerable<ShipTemplate> ts) => ts.Sum(t => ShipBuilder.FromAscii(t.Name, t.Ascii).Rooms.Count(r => r.Kind is RoomType.Crusher or RoomType.Cargo or RoomType.DroneBay));
                var hosp = ShipBuilder.FromAscii("x", ShipGenerator.Template(ShipPurpose.Hospital, ShipFrame.Ring, 12, seed).Ascii);
                var gen12 = ShipBuilder.FromAscii("x", ShipGenerator.Template(ShipPurpose.General, ShipFrame.Ring, 12, seed).Ascii);
                int med(Ship s) => s.Furniture.Count(f => f.Type == FurnitureType.MedBed);
                var two = ShipCatalog.Find(ShipGenerator.KeyFor(ShipPurpose.General, ShipFrame.Spine, 2, seed));
                var sixty = ShipCatalog.Find(ShipGenerator.KeyFor(ShipPurpose.Colony, ShipFrame.Ring, 60, seed));
                Check("생성기 — 옛 키 그대로 · 새 키 gen:용도:뼈대:인원:시드 · 용도가 방을 바꾼다",
                    old.Legacy && Fav(mining) > Fav(general) && med(hosp) > med(gen12) && two?.Crew == 2 && sixty?.Crew == 60 && !sixty.Legacy,
                    $"옛 키 내력 없음 {old.Legacy} · 채굴 방(파쇄·화물·드론) 채굴선 {Fav(mining)} / 일반 {Fav(general)} · 치료 침대 병원선 {med(hosp)} / 일반 {med(gen12)} · {two?.Name} {two?.Crew}인 · {sixty?.Name} {sixty?.Crew}인");
                bool same = ShipGenerator.Template(ShipPurpose.Mining, ShipFrame.Spine, 9, seed).Ascii == ShipCatalog.Find(ShipGenerator.KeyFor(ShipPurpose.Mining, ShipFrame.Spine, 9, seed))!.Ascii;
                var spine = ShipBuilder.FromAscii("x", ShipGenerator.Template(ShipPurpose.Mining, ShipFrame.Spine, 9, seed).Ascii);
                Check("척추형 — 모듈마다 기밀문(격벽)으로 구획이 나뉜다 · 같은 키 같은 배", same && spine.Compartments >= 5, $"구획 {spine.Compartments} · 격벽 문 {spine.Doors.Count(d => d.Bulkhead)}");
            }

            // 3) 설계사 · 시작 상태가 세계에 남는다
            if (On(3))
            {
                var mil = World.CreateDefault(seed, 0, "Bodeum");
                var civ = World.CreateDefault(seed, 0, "Saeteo");
                var set = World.CreateDefault(seed, 0, "Ttaemjil");
                var junk = set;
                float Wear(World w) => w.Ship.Machines.Average(m => m.Wear);
                float Age(World w) => w.Ship.Machines.Where(m => m.Crop == null).Average(m => w.Parts.AgeFactor(m));
                int salvage = junk.Ship.Machines.Sum(m => junk.Parts.Of(m).Count(p => p.Lot.Origin == PartOrigin.Salvage));
                int welds = junk.Ship.Walls.Count(kv => kv.Value.Welds > 0);
                Check("시작 상태 — 고물 배는 마모 · 부품 나이 · 용접 자국이 많고 새 배는 깨끗하다",
                    Wear(junk) > Wear(civ) + 0.4f && Age(junk) > Age(civ) && salvage > 0 && welds > civ.Ship.Walls.Count(kv => kv.Value.Welds > 0),
                    $"마모 땜질호 {Wear(junk) * 100:0}% / 새터호 {Wear(civ) * 100:0}% · 부품 고장 배율 {Age(junk):0.00} / {Age(civ):0.00} · 떼어 온 부품 {salvage} · 용접 자국 {welds}");
                bool mk3 = mil.Ship.FurnitureOf(FurnitureType.ReactorCore).All(f => f.Machine!.Grade == MachineGrade.Mk3);
                int spliced = set.Ship.Machines.Count(m => m.Spliced) + set.Net.Links.Count(l => l.Temp);
                Check("설계사 — 군용: Mk.3 · 이중 배선 / 민간: 단일 고장점 / 개척민: 임시 개조",
                    mk3 && mil.Net.Rings.Count >= 3 && civ.Net.Rings.Count == 0 && spliced > 0,
                    $"보듬호 보조 간선 {mil.Net.Rings.Count} · Mk.3 원자로 {mk3} · 새터호 보조 간선 {civ.Net.Rings.Count} · 땜질호 임시 개조 {spliced}");
                var war = mil;
                int sealedW = war.Origin.SealedRooms.Count;
                bool welded = war.Origin.SealedRooms.All(id => war.Ship.Rooms[id].Doors.All(d => d.Welded) && war.Ship.Rooms[id].Abandoned);
                float scorch = war.Ship.Walls.Max(kv => kv.Value.Scorch);
                int cargo = World.CreateDefault(seed, 0, "Nareumi").Ship.CountStored(ItemKind.Ration) - World.CreateDefault(seed, 0, "Hanbit").Ship.CountStored(ItemKind.Ration);
                Check("전쟁 상흔 · 시작 화물 — 그을음 · 막힌 구역(용접 · 포기) · 보급선은 화물을 싣고 떠난다",
                    sealedW >= 1 && welded && scorch > 0.5f && cargo >= 100,
                    $"막힌 구역 {string.Join(",", war.Origin.SealedRooms.Select(id => war.Ship.Rooms[id].Name))} · 그을음 {scorch * 100:0}% · 비상식량 더 실음 {cargo}");
            }

            // 4) 고리형: 통로 한쪽이 막혀도(용접) 반대로 돌아 대피 — 태양 폭풍에 위 식당에서 아래 대피소로
            if (On(4))
            {
                bool off = MeetingSystem.MaidenOff;
                MeetingSystem.MaidenOff = true;
                var w = World.CreateDefault(seed, 8, "Saeteo"); // 여덟 명만 (대피소 자리를 다투지 않게)
                Run(w, SimTime.Hours(9));
                MeetingSystem.MaidenOff = off;
                var ship = w.Ship;
                var mess = ship.Rooms.First(r => r.Type == RoomType.Mess);
                var shelter = Facilities.Best(ship, "shelter").room!;
                var from = mess.Cells.First(ship.IsOpenFloor);
                var to = shelter.Cells.First(ship.IsOpenFloor);
                int leftX = ship.Rooms.Where(r => r.Type == RoomType.Corridor).Min(r => r.MinX);
                int rightX = ship.Rooms.Where(r => r.Type == RoomType.Corridor).Max(r => r.MaxX);
                var direct = w.Paths.Find(from, to);
                bool directRight = direct != null && direct.Any(c => c.X >= rightX - 2);
                // 식당과 오른쪽 연결 통로 사이, 위 통로의 격벽을 용접한다
                var cut = ship.Doors.Where(d => d.Bulkhead && d.Cell.Y > mess.MaxY && d.Cell.Y <= mess.MaxY + 4 && d.Cell.X > mess.MaxX && d.Cell.X < rightX - 2).ToList();
                foreach (var d in cut) { d.Welded = true; d.Locked = true; }
                w.Paths.Invalidate();
                var around = w.Paths.Find(from, to);
                bool viaLeft = around != null && around.Any(c => c.X < mess.MinX - 8 && c.Y > mess.MaxY + 3); // 식당 왼쪽에서 아래로 (왼쪽 연결 통로나 가운데 기관 구역을 지나)
                Check("고리형 — 오른쪽 위 통로 격벽을 용접해도 왼쪽으로 돌아 대피소에 닿는다", directRight && cut.Count > 0 && viaLeft,
                    $"길이 {direct?.Count ?? -1}(오른쪽으로 {directRight}) → {around?.Count ?? -1}(왼쪽으로 돌아 내려감 {viaLeft}) · 용접한 격벽 {cut.Count}");
                // 그 상태로 태양 폭풍: 식당의 네 사람이 대피소로 — 막힌 오른쪽 대신 왼쪽으로 돈다
                var crew = w.Crew.Where(c => !c.Dead && !c.Outside).OrderBy(c => c.Id).Take(4).ToList();
                var spots = mess.Cells.Where(c => ship.IsOpenFloor(c) && ship.FurnitureAt(c) == null).OrderBy(x => x.X).ThenBy(x => x.Y).ToList();
                for (int i = 0; i < crew.Count; i++) { crew[i].Position = spots[i * 3].Center; crew[i].PreviousPosition = crew[i].Position; crew[i].Interrupt(w); }
                Hazards.Apply(w, HazardKind.SolarStorm, default, -1);
                Run(w, SimTime.Minutes(1));
                foreach (var c in crew) c.Interrupt(w);
                bool passedLeft = false;
                for (int i = 0; i < SimTime.Minutes(90) / 10; i++)
                {
                    Run(w, 10);
                    if (crew.Any(c => c.Cell.X < mess.MinX - 8 && c.Cell.Y > mess.MaxY + 3)) passedLeft = true;
                    if (argv.Contains("--trace") && i % (SimTime.Minutes(5) / 10) == 0) Console.WriteLine($"   {SimTime.HourOfDay(w.Tick):0.00}시 폭풍 {w.Ambience.StormPower:0.00} · " + string.Join(" | ", crew.Select(c => $"{c.Name} {c.Cell} {c.Room?.Name}({c.Room?.Radiation * 100:0}%) {c.Job?.Label} 휴식 {c.Needs.Rest * 100:0} · " + string.Join(",", c.LastEvaluations.Take(2).Select(e => $"{e.Activity.Label} {e.Score:0.00}")))));
                }
                int safe = crew.Count(c => c.Room == shelter);
                Check("고리형 대피 — 태양 폭풍에 식당의 넷이 막힌 오른쪽 대신 왼쪽으로 돌아 대피소에 든다", passedLeft && safe >= 3 && crew.All(c => !c.Dead),
                    $"왼쪽으로 돌아 내려감 {passedLeft} · 대피소에 {safe}/{crew.Count} · 식당 방사선 {mess.Radiation * 100:0}% · 대피소 {shelter.Radiation * 100:0}% · " + string.Join(", ", crew.Select(c => $"{c.Name} {c.Room?.Name}({c.Job?.Label})")));
            }

            // 5) 쌍동선: 연결 통로를 잃고(봉쇄 · 끊김) 둘로 갈라져 버티다 다시 잇는다
            if (On(5))
            {
                var w = DayOne(seed, "Bodeum");
                var ship = w.Ship;
                var links = ship.Rooms.Where(r => r.Type == RoomType.Corridor && r.Cells.Count >= 10 && r.Cells.All(c => c.X == r.Cells[0].X)).ToList();
                foreach (var r in links)
                {
                    foreach (var d in r.Doors) { d.Welded = true; d.Locked = true; }
                    foreach (var l in w.Net.Links.Where(l => l.Room == r)) w.Net.Hurt(l, 1f, "연결 통로 끊김");
                }
                w.Paths.Invalidate();
                int aff0 = 0;
                Run(w, SimTime.Hours(6));
                bool split = w.Origin.Split && w.Origin.Stats.Splits >= 1;
                var lower = ship.Rooms.Where(r => r.Kind is RoomType.Medbay or RoomType.Triage or RoomType.HvacRoom or RoomType.WaterPlant && r.Special != RoomType.Quarantine).ToList();
                bool lowerOk = lower.All(r => r.PowerLinked && r.Air.O2 > 17f);
                int alive = w.Crew.Count(c => !c.Dead);
                var hullB = ship.Rooms.First(r => r.Kind == RoomType.BackupBridge);
                var bCell = hullB.Cells.First(ship.IsOpenFloor);
                var aCell = ship.Rooms.First(r => r.Type == RoomType.Bridge).Cells.First(ship.IsOpenFloor);
                bool cutOff = w.Paths.Find(aCell, bCell) == null;
                Check("쌍동선 — 연결 통로 셋을 잃으면 둘로 갈리고, 아래 선체도 제 몫의 전기 · 공기로 버틴다", split && cutOff && lowerOk && alive == w.Crew.Count,
                    $"갈라짐 {w.Origin.Split} · 길 끊김 {cutOff} · 아래 선체 전기·공기 {lowerOk} ({string.Join(", ", lower.Select(r => $"{r.Name} {(r.PowerLinked ? "전기" : "정전")} O₂{r.Air.O2:0}"))}) · 생존 {alive}/{w.Crew.Count}");
                var splitAct = w.Automation.Book.Acts.LastOrDefault(x => x.Key == "origin:split");
                Check("쌍동선 — 주컴퓨터가 쪽마다 없는 것을 알리고 · 걱정되는 사람은 통신기로 건너편을 부른다", splitAct != null && w.Origin.Stats.Calls >= 1,
                    $"판단: {splitAct?.Judge} · 교신 {w.Origin.Stats.Calls}");
                // 다시 잇는다: 용접을 끊고 문을 연다 → 다시 만나고, 끊긴 선을 잇는다
                foreach (var r in links) foreach (var d in r.Doors) { d.Welded = false; d.Locked = false; }
                w.Paths.Invalidate();
                int repairs0 = w.Net.Stats.Repairs + w.Net.Stats.TempRepairs;
                Run(w, SimTime.Hours(10));
                bool reunited = !w.Origin.Split && w.Origin.Stats.Reunions >= 1;
                int fixedLinks = w.Net.Links.Count(l => links.Contains(l.Room) && !l.Cut);
                int totalLinks = w.Net.Links.Count(l => links.Contains(l.Room));
                Check("쌍동선 — 다시 이으면 다시 만나고(관계) · 끊긴 선을 다시 잇는다", reunited && w.Paths.Find(aCell, bCell) != null && w.Net.Stats.Repairs + w.Net.Stats.TempRepairs > repairs0,
                    $"다시 이음 {w.Origin.Stats.Reunions} · 연결 통로 선 {fixedLinks}/{totalLinks} 이어짐 · 다시 이은 선 {w.Net.Stats.Repairs + w.Net.Stats.TempRepairs - repairs0}{aff0}");
            }

            // 6) 숨은 이야기: 정비하다 발견 → 일기 · 물건 · 이야기가 퍼진다 · 쪽지의 요령
            if (On(6))
            {
                var w = World.CreateDefault(seed, 0, "Busitdol");
                Run(w, SimTime.Hours(2));
                var o = w.Origin;
                int hidden = o.Finds.Count;
                var note = o.Finds.Where(f => !f.Found && f.Kind == FindKind.Note && f.Machine >= 0).OrderBy(f => f.Id).FirstOrDefault();
                bool found = false, diary = false, item = false;
                if (note != null)
                {
                    var m = w.Ship.Furniture[note.Machine].Machine!;
                    w.Machines.Break(m);
                    for (int i = 0; i < 36 && !note.Found; i++) Run(w, SimTime.Minutes(20));
                    found = note.Found;
                    if (found)
                    {
                        var c = w.Crew[note.FoundBy];
                        diary = c.Diary.Any(d => d.text.Contains(note.Author));
                        item = w.Belongings.Of(c).Any(b => b.Name.Contains(note.Author));
                    }
                }
                Run(w, SimTime.Hours(18));
                int knows = note?.Knows.Count ?? 0;
                var nm = note != null ? w.Ship.Furniture[note.Machine] : null;
                Check("숨은 이야기 — 버려졌던 배엔 쪽지 · 술병 · 낙서가 숨어 있고, 고치다 패널 뒤에서 찾는다", hidden >= 5 && found && diary && item,
                    $"[{nm?.Label} {nm?.Room.Name} 고장 {nm?.Machine?.Faults.Count} 손봄 {nm?.Machine?.ServiceCount}] 숨은 것 {hidden} · 쪽지 찾음 {found} ({(note != null && note.FoundBy >= 0 ? w.Crew[note.FoundBy].Name : "-")}) · 일기 {diary} · 소지품 {item} · {o.Stats.Summary()}");
                Check("숨은 이야기 — 이야기가 퍼진다 (들은 사람 · 일기 · 가까워짐)", knows >= 2 && o.Stats.Told >= 1, $"아는 사람 {knows} · 전함 {o.Stats.Told}");
            }

            // 7) 크기 공백: 2 · 8~9 · 40~60인 — 역할 · 침대 · 당직 · 하루
            if (On(7))
            {
                foreach (var key in new[] { "Pabal", "Nareumi", "Busitdol", ShipGenerator.KeyFor(ShipPurpose.Colony, ShipFrame.Ring, 60, seed) })
                {
                    var w = World.CreateDefault(seed, 0, key);
                    var roles = w.Crew.Select(c => c.Role).Distinct().Count();
                    int beds = w.Ship.FurnitureOf(FurnitureType.Bed).Count() + w.Ship.FurnitureOf(FurnitureType.Cot).Count();
                    int bedless = w.Crew.Count(c => c.Bed == null);
                    int hoursUncovered = 0;
                    float minFood = 1f, minRest = 1f;
                    string hungry = "", tired = "";
                    for (int h = 0; h < 24; h++)
                    {
                        Run(w, SimTime.Hours(1));
                        if (!w.Crew.Any(c => !c.Dead && c.IsAwake)) hoursUncovered++;
                        foreach (var c in w.Crew.Where(c => !c.Dead))
                        {
                            if (c.Needs.Food < minFood) { minFood = c.Needs.Food; hungry = $"{c.Name} {SimTime.HourOfDay(w.Tick):0}시 {c.Room?.Name}({c.Job?.Label})"; }
                            if (c.Needs.Rest < minRest) { minRest = c.Needs.Rest; tired = $"{c.Name} {SimTime.HourOfDay(w.Tick):0}시 {c.Room?.Name}({c.Job?.Label})"; }
                        }
                    }
                    int n = w.Crew.Count;
                    bool rolesOk = n == 2 ? w.Crew.Any(c => c.Role == CrewRole.Pilot) && w.Crew.Any(c => c.Role == CrewRole.Engineer) : roles >= Math.Min(6, n);
                    bool ok = rolesOk && bedless == 0 && hoursUncovered == 0 && w.Crew.All(c => !c.Dead) && minFood > 0.05f && minRest > 0.05f && w.Power.ReactorOnline;
                    Check($"크기 {n}인 ({w.Ship.Name}) — 역할 · 침대 · 당직(늘 누군가 깨어 있다) · 하루", ok,
                        $"역할 {roles}가지 · 침대 {beds}(없는 사람 {bedless}) · 아무도 안 깬 시간 {hoursUncovered} · 최저 배고픔 {minFood * 100:0}%({hungry}) · 최저 휴식 {minRest * 100:0}%({tired})"
                        + (w.Origin.BedShares.Count > 0 ? $" · 침대 교대 {w.Origin.BedShares.Count}쌍 (인계 {w.Origin.Stats.Handovers})" : ""));
                }
            }

            // 8) 모든 배 하루: 길 · 문 · 설비 · 식사 · 수면 · 사고 하나 대응
            if (On(8))
            {
                var keys = ShipCatalog.All.Select(t => t.Key).ToList();
                foreach (var f in ShipInfos.GenFrames)
                    foreach (var p in new[] { ShipPurpose.Mining, ShipPurpose.Hospital })
                        keys.Add(ShipGenerator.KeyFor(p, f, 9, seed + (int)f));
                keys.Add(ShipGenerator.KeyFor(ShipPurpose.Supply, ShipFrame.Spine, 20, seed));
                keys.Add(ShipGenerator.KeyFor(ShipPurpose.Research, ShipFrame.Ring, 12, seed));
                int ok = 0;
                var bad = new List<string>();
                foreach (var key in keys)
                {
                    var w = DayOne(seed, key);
                    int meals0 = w.Crew.Sum(c => c.Stats.Meals);
                    var galley = w.Ship.RoomsOf(RoomType.Galley).First();
                    w.Fire.Ignite(galley.Cells.First(w.Ship.IsOpenFloor), 0.6f);
                    Run(w, SimTime.Hours(12));
                    int alive = w.Crew.Count(c => !c.Dead);
                    bool fireOut = w.Fire.Count == 0;
                    float powered = w.Ship.Machines.Count(m => m.Powered) / (float)Math.Max(1, w.Ship.Machines.Count());
                    bool fed = w.Crew.All(c => c.Dead || c.Needs.Food > 0.1f);
                    bool slept = w.Crew.All(c => c.Dead || c.Needs.Rest > 0.1f);
                    int reach = Unreached(w.Ship).Count;
                    int stuck = w.Ship.Doors.Count(d => d.MotorBroken && !d.Welded);
                    bool good = alive == w.Crew.Count && fireOut && powered > 0.85f && fed && slept && reach == 0 && w.Power.ReactorOnline;
                    if (good) ok++; else bad.Add(key);
                    Console.WriteLine($"   {(good ? "·" : "←")} {w.Ship.Name,-6} {key,-28} 생존 {alive}/{w.Crew.Count} · 불 {(fireOut ? "꺼짐" : $"{w.Fire.Count}칸")} · 설비 전기 {powered * 100:0}% · 먹음 {fed} · 잠 {slept} · 길 끊긴 방 {reach} · 고장 문 {stuck} · 원자로 {(w.Power.ReactorOnline ? "돎" : "멈춤")}");
                }
                Check("모든 배 하루 — 길 · 문 · 설비 · 식사 · 수면 · 불 하나 끄기", ok == keys.Count, $"{ok}/{keys.Count}" + (bad.Count > 0 ? " · 문제: " + string.Join(", ", bad) : ""));
            }

            // 10) ★★ 승무원이 배의 내력을 알아채고 다르게 행동한다 · 주컴퓨터가 읽고 판단한다
            if (On(10))
            {
                var junk = DayOne(seed, "Ttaemjil");
                var fresh = DayOne(seed, "Saeteo");
                var war = DayOne(seed, "Bodeum");
                var o = junk.Origin;
                var walkers = junk.Crew.Where(c => o.RoundsBy(c) > 0).Select(c => $"{c.Name} {o.RoundsBy(c)}").ToList();
                Check("승무원 — 고물 배를 알아채고 아침마다 한 바퀴 돌며 귀를 댄다 (새 배에선 아무도 안 한다)", o.Stats.Rounds >= 2 && fresh.Origin.Stats.Rounds == 0 && o.Stats.RoundFixes >= 1,
                    $"땜질호 한 바퀴 {o.Stats.Rounds}(손봄 {o.Stats.RoundFixes} · {string.Join(", ", walkers)}) · 새터호 {fresh.Origin.Stats.Rounds}");
                // 주컴퓨터: 고장 위험 순위 → 정비를 앞당긴다 (새 배는 하나만 조금) · 컴퓨터가 멎으면 순위도 멎는다
                var act = junk.Automation.Book.Acts.LastOrDefault(x => x.Key == "origin:rank");
                var top = o.Ranked.OrderByDescending(kv => kv.Value).Select(kv => junk.Ship.Furniture[kv.Key].Machine!).ToList();
                float junkEarly = top.Count > 0 ? o.Early(top[0]) : 0f;
                float freshEarly = fresh.Origin.Ranked.Count > 0 ? fresh.Origin.Ranked.Values.Max() : 0f;
                var maint = junk.Board.All.Where(x => x.Kind == WorkKind.Maintain && x.Target.Furniture != null).ToList();
                int rankedNow = maint.Count(x => o.Ranked.ContainsKey(x.Target.Furniture!.Id));
                int rankedOrders = rankedNow + o.Stats.RankFixes; // 지금 떠 있는 순위 설비 정비 + 하루 동안 순위대로 먼저 손본 것 (하루 끝 한순간만 보면 막 끝낸 정비는 안 보인다)
                Check("주컴퓨터 — 시작 상태 · 부품 내력으로 고장 위험 순위를 매겨 정비를 앞당긴다 (고물 배는 넷을 크게 · 새 배는 하나를 조금)",
                    act != null && top.Count >= 3 && junkEarly > freshEarly * 1.5f && rankedOrders >= 1,
                    $"순위 {string.Join(" · ", top.Select(m => m.Name))} · 앞당김 땜질호 {junkEarly * 100:0}%p / 새터호 {freshEarly * 100:0}%p · 순위 설비 정비 지금 {rankedNow}/{maint.Count} · 먼저 손봄 {o.Stats.RankFixes} · 판단: {act?.Judge}");
                foreach (var comp in junk.Ship.FurnitureOf(FurnitureType.MainComputer)) junk.Machines.Break(comp.Machine!, FaultKind.Wrecked); // 아무 고장이나 걸면 가벼운 고장(효율 25% 넘음)이라 컴퓨터가 안 멎을 수 있다
                Run(junk, 30);
                Check("주컴퓨터가 멎으면 정비 순위도 멎는다 (사람 귀만 남는다)", !junk.Automation.MainOnline && top.All(m => o.Early(m) == 0f), $"컴퓨터 {(junk.Automation.MainOnline ? "돎" : "멎음")}");
                bool spof = fresh.Automation.Book.Acts.Any(x => x.Key == "origin:spof");
                bool sealedAdv = war.Automation.Book.Acts.Any(x => x.Key == "origin:sealed");
                Check("주컴퓨터 — 민간 배의 단일 고장점 · 막아 둔 구역의 공기를 읽고 제안 · 경고한다", spof && sealedAdv, $"단일 고장점 제안(새터호) {spof} · 막힌 구역 경고(보듬호) {sealedAdv}");
                // 좁은 군용 배: 비켜서기 → 자꾸 마주친 둘의 관계가 바뀐다 (친하면 웃고 · 나쁘면 짜증)
                var ws = war.Origin.Stats;
                float perWar = ws.Squeezes / (float)war.Crew.Count, perFresh = fresh.Origin.Stats.Squeezes / (float)fresh.Crew.Count;
                Check("좁은 군용 배 — 비켜서기가 잦고, 자꾸 마주친 둘의 사이가 바뀐다", ws.Squeezes >= 3 && ws.SqueezeBonds + ws.SqueezeSpats >= 1,
                    $"보듬호 비켜섬 {ws.Squeezes}(1인당 {perWar:0.0}) · 웃음 {ws.SqueezeBonds} · 짜증 {ws.SqueezeSpats} / 새터호 1인당 {perFresh:0.0}");
                // 배 본체 · 불: 닳은 배는 바닥이 닳아 미끄럽고 기름이 묻어 있다 · 군용 내장재는 덜 탄다
                float FloorWear(World x) { float sum = 0f; int n = 0; for (int i = 0; i < x.Body.Wear.Length; i++) if (x.Body.Floor[i] != Material.None) { sum += x.Body.Wear[i]; n++; } return sum / Math.Max(1, n); }
                int oil = junk.Body.Marks.Values.Count(m => m.V[(int)CellMark.Oil] > 0.05f);
                Check("배 본체 × 시작 상태 · 설계사 × 불 — 고물 배 바닥은 닳고 기름 · 군용 내장재는 덜 타고 개척민 것은 잘 탄다",
                    FloorWear(junk) > FloorWear(fresh) + 0.15f && oil >= 1 && war.Origin.FireMul < fresh.Origin.FireMul && fresh.Origin.FireMul < junk.Origin.FireMul,
                    $"바닥 닳음 땜질호 {FloorWear(junk) * 100:0}% / 새터호 {FloorWear(fresh) * 100:0}% · 기름 칸 {oil} · 넘어짐 땜질호 {junk.Body.Stats.Falls} / 새터호 {fresh.Body.Stats.Falls} · 불 번짐 군용 {war.Origin.FireMul} · 민간 {fresh.Origin.FireMul} · 개척민 {junk.Origin.FireMul}");
            }

            // 11) 정비 문화: 닳은 배에서 고장을 겪고 한 바퀴가 몸에 배면 "소리부터 듣는다"가 관행이 된다 (→ 전조를 더 잘 듣는다)
            if (On(11))
            {
                var w = DayOne(seed, "Ttaemjil");
                for (int d = 0; d < 3 && w.Culture.Of(CustomKind.MaintainerWay) == null; d++) Run(w, SimTime.TicksPerDay);
                var cu = w.Culture.Of(CustomKind.MaintainerWay);
                Check("상호작용 — 고물 배 마모 → 고장 → 아침 한 바퀴 → 정비 문화 (따르는 사람이 전조를 더 잘 듣는다)", cu != null && cu.Followers.Count >= 2 && w.Origin.Stats.CultureBorn == 1,
                    $"관행 {(cu != null ? $"{cu.Origin} · 따르는 사람 {cu.Followers.Count}" : "없음")} · {w.Origin.Stats.Summary()}");
            }

            // 9) 결정론
            if (On(9))
            {
                uint H(string key) { var w = World.CreateDefault(seed, 0, key); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H("Hanbit"), y = H("Hanbit");
                uint a = H("Busitdol"), b = H("Busitdol");
                Check("결정론 — 같은 시드 같은 지문 (한빛호 · 부싯돌호)", x == y && a == b, $"{x:x8}/{y:x8} · {a:x8}/{b:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 배 종류 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>필수 설비: 방 17종 기능 · 원자로 · 엔진 · 냉각 펌프 · 배전반 · 배터리 · 산소 · 정수 · 재배 · 조리 · 냉장 · 배식 · 침대 · 치료 침대 · 우주복 · 드론 · 주 컴퓨터 · 감지기.</summary>
    private static List<string> Essentials(Ship ship)
    {
        var miss = new List<string>();
        foreach (var t in new[] { RoomType.Corridor, RoomType.Bridge, RoomType.Engine, RoomType.Reactor, RoomType.Cooling, RoomType.Power, RoomType.LifeSupport,
                     RoomType.Workshop, RoomType.Storage, RoomType.Galley, RoomType.Mess, RoomType.Hydroponics, RoomType.Airlock, RoomType.Quarters, RoomType.Medbay,
                     RoomType.Lounge, RoomType.Comms })
            if (!ship.Rooms.Any(r => r.Type == t)) miss.Add(RoomTypes.Name(t));
        foreach (var f in new[] { FurnitureType.ReactorCore, FurnitureType.EngineCore, FurnitureType.CoolantPump, FurnitureType.PowerPanel, FurnitureType.Battery,
                     FurnitureType.OxygenGenerator, FurnitureType.WaterRecycler, FurnitureType.GrowBed, FurnitureType.Stove, FurnitureType.Fridge, FurnitureType.MealDispenser,
                     FurnitureType.Bed, FurnitureType.MedBed, FurnitureType.SuitLocker, FurnitureType.DroneDock, FurnitureType.MainComputer, FurnitureType.SensorArray,
                     FurnitureType.Workbench, FurnitureType.Refinery, FurnitureType.Collector, FurnitureType.AuxGenerator })
            if (!ship.Furniture.Any(x => x.Type == f)) miss.Add(FurnitureTypes.Name(f));
        if (!ship.Doors.Any(d => d.IsExternal && (d.RoomA?.Type == RoomType.Airlock || d.RoomB?.Type == RoomType.Airlock))) miss.Add("에어락 바깥 문");
        return miss;
    }

    /// <summary>문(용접 · 떨어져 나간 것 빼고)으로 함교에서 닿지 않는 방.</summary>
    private static List<string> Unreached(Ship ship)
    {
        var start = ship.Rooms.FirstOrDefault(r => r.Type == RoomType.Bridge && !r.Detached);
        if (start == null) return new List<string> { "함교" };
        var seen = new HashSet<int> { start.Id };
        var q = new Queue<Room>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var r = q.Dequeue();
            foreach (var d in r.Doors)
            {
                if (d.IsExternal || d.Removed) continue;
                var o = d.RoomA == r ? d.RoomB : d.RoomA;
                if (o != null && !o.Detached && seen.Add(o.Id)) q.Enqueue(o);
            }
        }
        return ship.Rooms.Where(r => !r.Detached && !seen.Contains(r.Id)).Select(r => r.Name).ToList();
    }
}
