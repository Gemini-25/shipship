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
        Console.WriteLine($"배 종류 점검 (v16.9) · 시드 {seed}\n");
        try
        {
            // 1) 대표 배 6척: 뼈대가 저마다 다르고, 필수 설비가 다 있고, 모든 방에 길이 닿는다
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

            // 4) 고리형: 통로 한쪽이 막혀도(용접 · 불) 반대로 돌아 대피
            {
                var w = DayOne(seed, "Saeteo");
                var ship = w.Ship;
                var mess = ship.Rooms.First(r => r.Type == RoomType.Mess);
                var airlock = ship.Rooms.First(r => r.Type == RoomType.Airlock);
                var from = mess.Cells.First(ship.IsOpenFloor);
                var to = airlock.Cells.First(ship.IsOpenFloor);
                int before = w.Paths.Find(from, to)?.Count ?? -1;
                // 식당 쪽 위 통로의 왼쪽 격벽을 용접한다
                var left = ship.Doors.Where(d => d.Bulkhead && d.Cell.Y < mess.MaxY + 4 && d.Cell.X < mess.MinX).OrderByDescending(d => d.Cell.X).Take(2).ToList();
                foreach (var d in left) { d.Welded = true; d.Locked = true; }
                w.Paths.Invalidate();
                var around = w.Paths.Find(from, to);
                int rightX = ship.Rooms.Where(r => r.Type == RoomType.Corridor).Max(r => r.MaxX);
                bool viaRight = around != null && around.Any(c => c.X >= rightX - 2);
                Check("고리형 — 위 통로 격벽을 용접해도 오른쪽으로 한 바퀴 돌아 에어락에 닿는다", before > 0 && around != null && viaRight && around.Count > before,
                    $"길이 {before} → {around?.Count ?? -1} (오른쪽 연결 통로를 지남 {viaRight}) · 용접한 격벽 {left.Count}");
                // 그 상태로 식당에 불 — 안에 있던 사람들이 반대쪽으로 돌아 빠져나간다
                var crew = w.Crew.Where(c => !c.Dead).Take(4).ToList();
                foreach (var c in crew)
                {
                    var cell = mess.Cells.Where(ship.IsOpenFloor).OrderBy(x => x.X).ThenBy(x => x.Y).ElementAt(crew.IndexOf(c) * 3);
                    c.Position = cell.Center; c.PreviousPosition = c.Position; c.Interrupt(w);
                }
                var fireCell = mess.Cells.Where(ship.IsOpenFloor).OrderByDescending(x => x.X).First();
                w.Fire.Ignite(fireCell, 0.8f);
                bool passedRight = false;
                for (int i = 0; i < SimTime.Hours(1) / 10; i++)
                {
                    Run(w, 10);
                    if (crew.Any(c => c.Cell.X >= rightX - 2)) passedRight = true;
                }
                int inMess = crew.Count(c => c.Room == mess);
                Check("고리형 대피 — 불난 식당에서 빠져나오고, 막힌 왼쪽 대신 오른쪽으로 돈다", inMess <= 1 && crew.All(c => !c.Dead) && passedRight,
                    $"식당에 남음 {inMess}/{crew.Count} · 오른쪽 연결 통로를 지난 사람 있음 {passedRight} · 불 {w.Fire.Count}칸");
            }

            // 5) 쌍동선: 연결 통로를 잃고(봉쇄 · 끊김) 둘로 갈라져 버티다 다시 잇는다
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
            {
                var w = DayOne(seed, "Busitdol");
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
                Check("숨은 이야기 — 버려졌던 배엔 쪽지 · 술병 · 낙서가 숨어 있고, 고치다 패널 뒤에서 찾는다", hidden >= 5 && found && diary && item,
                    $"숨은 것 {hidden} · 쪽지 찾음 {found} ({(note != null && note.FoundBy >= 0 ? w.Crew[note.FoundBy].Name : "-")}) · 일기 {diary} · 소지품 {item} · {o.Stats.Summary()}");
                Check("숨은 이야기 — 이야기가 퍼진다 (들은 사람 · 일기 · 가까워짐)", knows >= 2 && o.Stats.Told >= 1, $"아는 사람 {knows} · 전함 {o.Stats.Told}");
            }

            // 7) 크기 공백: 2 · 8~9 · 40~60인 — 역할 · 침대 · 당직 · 하루
            {
                foreach (var key in new[] { "Pabal", "Nareumi", "Busitdol", ShipGenerator.KeyFor(ShipPurpose.Colony, ShipFrame.Ring, 60, seed) })
                {
                    var w = World.CreateDefault(seed, 0, key);
                    var roles = w.Crew.Select(c => c.Role).Distinct().Count();
                    int beds = w.Ship.FurnitureOf(FurnitureType.Bed).Count() + w.Ship.FurnitureOf(FurnitureType.Cot).Count();
                    int bedless = w.Crew.Count(c => c.Bed == null);
                    int hoursUncovered = 0;
                    float minFood = 1f, minRest = 1f;
                    for (int h = 0; h < 24; h++)
                    {
                        Run(w, SimTime.Hours(1));
                        if (!w.Crew.Any(c => !c.Dead && c.IsAwake)) hoursUncovered++;
                        foreach (var c in w.Crew.Where(c => !c.Dead)) { minFood = MathF.Min(minFood, c.Needs.Food); minRest = MathF.Min(minRest, c.Needs.Rest); }
                    }
                    int n = w.Crew.Count;
                    bool rolesOk = n == 2 ? w.Crew.Any(c => c.Role == CrewRole.Pilot) && w.Crew.Any(c => c.Role == CrewRole.Engineer) : roles >= Math.Min(6, n);
                    bool ok = rolesOk && bedless == 0 && hoursUncovered == 0 && w.Crew.All(c => !c.Dead) && minFood > 0.05f && minRest > 0.05f && w.Power.ReactorOnline;
                    Check($"크기 {n}인 ({w.Ship.Name}) — 역할 · 침대 · 당직(늘 누군가 깨어 있다) · 하루", ok,
                        $"역할 {roles}가지 · 침대 {beds}(없는 사람 {bedless}) · 아무도 안 깬 시간 {hoursUncovered} · 최저 배고픔 {minFood * 100:0}% · 최저 휴식 {minRest * 100:0}%"
                        + (w.Origin.BedShares.Count > 0 ? $" · 침대 교대 {w.Origin.BedShares.Count}쌍 (인계 {w.Origin.Stats.Handovers})" : ""));
                }
            }

            // 8) 모든 배 하루: 길 · 문 · 설비 · 식사 · 수면 · 사고 하나 대응
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

            // 9) 결정론
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
