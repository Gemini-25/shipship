using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.13 폭발과 연쇄: 압력파 · 파편 · 밀쳐냄 · 연쇄 · 이명 · 흔적 · 의도적 폭파 · 승무원 · 주 컴퓨터
public static partial class Program
{
    /// <summary>두 방 사이 안쪽 문 하나와, 문 앞(안) 칸 · 문 너머 칸.</summary>
    private static (Door door, Cell inside, Cell beyond)? BlastDoor(World w, int skip = 0)
    {
        var ship = w.Ship;
        foreach (var d in ship.Doors)
        {
            if (d.Removed || d.IsExternal || d.Bulkhead || d.RoomA == null || d.RoomB == null) continue;
            var axis = d.ConnectsVertically ? new Cell(0, 1) : new Cell(1, 0);
            for (int side = 0; side < 2; side++)
            {
                var ax = side == 0 ? axis : new Cell(-axis.X, -axis.Y);
                var a1 = d.Cell + ax; var b1 = new Cell(d.Cell.X - ax.X, d.Cell.Y - ax.Y);
                var a2 = a1 + ax;
                if (!ship.IsOpenFloor(a1) || !ship.IsOpenFloor(b1) || !ship.IsOpenFloor(a2) || ship.RoomAt(a2)?.Kind == RoomType.Corridor) continue;
                if (skip-- > 0) continue;
                return (d, a2, b1);
            }
        }
        return null;
    }

    private static void Clear(World w, Cell c, CrewMember? except = null)
    {
        foreach (var o in w.Crew)
            if (o != except && !o.Dead && (o.Position - c.Center).Length() < 6f)
            {
                var far = w.Ship.LiveRooms.Where(r => r.Kind == RoomType.Quarters || r.Kind == RoomType.Mess).SelectMany(r => r.Cells).Where(x => w.Ship.IsOpenFloor(x) && (x.Center - c.Center).Length() > 12f).FirstOrDefault();
                if (far != default) { o.Position = far.Center; o.PreviousPosition = o.Position; }
            }
    }

    private static int RunBlastTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"폭발과 연쇄 점검 (v16.13) · 시드 {seed}\n");
        var only = Environment.GetEnvironmentVariable("BLASTONLY")?.Split(',');
        bool On(int n) => only == null || only.Contains(n.ToString());

        // ── 1) 배터리 열폭주 → 곁의 산소통이 불에 달아 연쇄 ──
        if (On(1))
        {
            var w = DayOne(seed, "Hanbit");
            var bat = w.Ship.FurnitureOf(FurnitureType.Battery).First().Machine!;
            var at = bat.Body.UseSpots.First(s => w.Ship.IsOpenFloor(s));
            var tank = w.Blast.Items.Place(ExplosiveKind.OxygenTank, at, 1f);
            Clear(w, at);
            w.Volatile.Blow(bat, BlowKind.ThermalRunaway, "시험");
            for (int i = 0; i < SimTime.Hours(1.5f) && !tank.Spent; i++) w.Step();
            var o2 = w.Blast.Recent.FirstOrDefault(r => r.Kind == BlastKind.Oxygen);
            Check("배터리 열폭주 → 산소통 연쇄", tank.Spent && o2 != null && o2.Depth >= 1,
                $"산소통 {(tank.Spent ? "터짐" : $"열 {tank.Heat * 100:0}%")} · 원인 \"{tank.Why}\" · 연쇄 단 {o2?.Depth} · 폭발 {string.Join(" → ", w.Blast.Recent.Select(r => $"{r.Spec.Name}({BlastSystem.ScaleName(r.Scale)})"))}");
        }

        // ── 2) 닫힌 문 너머는 약하게 · 열린 문 너머는 넘어진다 (+ 문틀이 휜다 · 흔적 · 꺼림 · 구조 · 조사) ──
        if (On(2))
        {
            float pClosed = 0f, pOpen = 0f; bool fellClosed = true, fellOpen = false, bent = false;
            for (int pass = 0; pass < 2; pass++)
            {
                var w = DayOne(seed, "Hanbit");
                var (door, inside, beyond) = BlastDoor(w)!.Value;
                var x = w.Crew.First(c => c.CanAct);
                Clear(w, inside, x);
                x.Position = beyond.Center; x.PreviousPosition = x.Position;
                door.Openness = pass == 0 ? 0f : 1f;
                door.HoldOpen = pass == 1;
                var rec = w.Blast.Detonate(inside, 0.8f, BlastKind.Oxygen, "시험 산소통")!;
                float p = w.Blast.PAt(beyond);
                bool fell = rec.Fell.Contains(x.Id);
                if (pass == 0) { pClosed = p; fellClosed = fell; bent = door.Bent > 0.3f; }
                else
                {
                    pOpen = p; fellOpen = fell;
                    // 흔적 · 꺼림 · 구조 · 조사
                    var room = w.Ship.RoomAt(inside)!;
                    int soot = rec.Wave.Count(c => w.Body.Mark(new Cell(c.X, c.Y), CellMark.Soot) > 0.2f);
                    int rubble = w.Ship.Rubble.Count;
                    float fear = x.Memory.FearOf(room);
                    if (Environment.GetEnvironmentVariable("BLASTDBG") != null)
                        for (int k = 0; k < 8; k++)
                        {
                            Run(w, SimTime.Minutes(k < 4 ? 10 : 30));
                            Console.WriteLine($"   [dbg] {k}단계 · 다침 {string.Join(",", rec.Hurt)} 넘어짐 {string.Join(",", rec.Fell)} 들음 {rec.Heard.Count} · 불 {w.Fire.Count} · 조사자 {rec.Investigator} · 위기 {Crisis.Level(w)} · 방 위험 {Atmosphere.Danger(w.Ship.Rooms[rec.Room]):0.00} · 새는 중 {w.Ship.Rooms[rec.Room].Leaking} · 방 불 {w.Fire.CountIn(w.Ship.Rooms[rec.Room])} · 구조 {w.Blast.Stats.Rescues}");
                            foreach (var c in w.Crew) Console.WriteLine($"     {c.Id} {c.Name} {c.Job?.Label} · " + string.Join(" / ", c.LastEvaluations.Where((e, i) => e.Activity is BlastResponseActivity || i == 0).Select(e => $"{e.Activity.Id} {e.Score:0.00} {e.Reason}")));
                        }
                    Run(w, SimTime.Hours(3));
                    var st = w.Blast.Stats;
                    Check("흔적 — 방사형 그을음 · 잔해 · 깨진 조명 · 자리", soot >= 3 && rubble > 0 && w.Blast.Scars.Count > 0,
                        $"그을음 {soot}칸 · 잔해 {rubble}칸 · 깨진 조명 {st.Lights} · 자리 {w.Blast.Scars.Count} · 날아간 문 {st.DoorsBlown} · 휜 문 {st.DoorsBent} · 쾅 {st.DoorsSlammed}");
                    Check("꺼림 — 겪은 사람은 그 방이 무섭다 (길이 돌아간다)", fear > 0.15f && x.PathProfile.Fear != null,
                        $"{x.Name} {room.Name} 공포 {fear * 100:0}%");
                    Check("구조 · 조사 — 다친 사람을 살피고, 원인을 짚는다", st.Rescues > 0 && st.Investigations > 0,
                        $"구조 {st.Rescues} · 조사 {st.Investigations} · {w.Blast.Recent.FirstOrDefault()?.Finding}");
                    Run(w, SimTime.TicksPerDay * 2);
                    Check("흔적이 며칠 간다", w.Blast.Scars.Any(s => s.Fade > 0.05f) || w.Body.Stats.Cleaned > 0,
                        $"이틀 뒤 자리 {w.Blast.Scars.Count}(옅어짐 {string.Join(",", w.Blast.Scars.Select(s => $"{s.Fade * 100:0}%"))}) · 닦음 {w.Body.Stats.Cleaned} · 문틀 편 것 {w.Body.Stats.Fixed}");
                }
            }
            Check("닫힌 문 너머는 약하게 — 문틀이 휜다", pClosed < pOpen * 0.4f && !fellClosed && bent,
                $"닫힌 문 너머 {pClosed:0.000} · 열린 문 너머 {pOpen:0.000} · 넘어짐 {(fellClosed ? "예" : "아니오")} · 문틀 휨 {(bent ? "예" : "아니오")}");
            Check("열린 문 너머 사람이 넘어진다", fellOpen, $"압력 {pOpen:0.000}");
        }

        // ── 3) 파편이 외벽을 뚫어 감압 ──
        if (On(3))
        {
            var w = DayOne(seed, "Hanbit");
            var ship = w.Ship;
            Cell spot = default; Room? room = null;
            foreach (var r in ship.LiveRooms.Where(r => r.Kind != RoomType.Corridor))
            {
                foreach (var c in r.Cells)
                    if (ship.IsOpenFloor(c) && Cell.Dirs4.Any(d => ship.WallAt(c + d)?.IsHull == true)) { spot = c; room = r; break; }
                if (room != null) break;
            }
            Clear(w, spot);
            var rec = w.Blast.Detonate(spot, 0.9f, BlastKind.Charge, "시험 — 외벽 곁")!;
            Run(w, SimTime.Minutes(2));
            Check("파편이 외벽을 뚫어 감압", rec.Breaches > 0 && room!.Leaking,
                $"{room!.Name} · 파편 {rec.Shards.Count}(외벽 뚫음 {rec.Shards.Count(s => s.Hit == 2)}) · 파공 {rec.Breaches} · 새는 중 {(room.Leaking ? "예" : "아니오")} · 기압 {room.Air.Pressure:0}kPa · 규모 {BlastSystem.ScaleName(rec.Scale)}");
        }

        // ── 4) 이명 → 다음 경보를 늦게 듣는다 · 방송을 놓치면 컴퓨터가 다시 알린다 ──
        if (On(4))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Crew.First(c => c.CanAct && c.IsAwake);
            var b = w.Crew.First(c => c.CanAct && c.IsAwake && c != a && (c.Position - a.Position).Length() > 8f);
            var spot = a.Cell;
            var rec = w.Blast.Detonate(Cell.FromPosition(a.Position + new System.Numerics.Vector2(1.2f, 0f)) is Cell s2 && w.Ship.IsOpenFloor(s2) ? s2 : spot, 0.35f, BlastKind.Gas, "시험 — 곁에서")!;
            bool deaf = rec.Deafened.Contains(a.Id);
            Run(w, SimTime.Minutes(4));
            long t = w.Tick;
            w.RaiseAlert("시험 경보", b.Room, AlertLevel.Critical, shipWide: true);
            bool aNow = a.AlertedTick == t, bNow = b.AlertedTick == t;
            Run(w, SimTime.Minutes(30));
            long aHeard = a.AlertedTick; int late = w.Blast.Stats.HeardLate; // 시험 경보를 들은 때 (아래에서 더 기다리는 사이 다른 경보로 바뀌기 전)
            // 방송은 주 컴퓨터가 한다 — 폭발이 지나가는 간선을 끊어 함교가 꺼졌으면 고쳐 다시 켜질 때까지 (그 사이 이명이 가셨으면 다시 울린다)
            for (int k = 0; k < 36 && !w.Automation.MainOnline; k++) Run(w, SimTime.Minutes(10));
            w.Blast.Deafen(a, 1f, 3f);
            w.Automation.Speak.Announce("시험 방송", a.Room, 2);
            Run(w, SimTime.Minutes(15));
            var st = w.Blast.Stats;
            Check("이명 — 방송을 놓치면 주 컴퓨터가 손목 단말로 다시 알린다", st.BroadcastMissed > 0 && st.ComputerRelays > 0, $"놓침 {st.BroadcastMissed} · 다시 알림 {st.ComputerRelays}");
            Check("이명 — 경보를 늦게 듣는다", deaf && !aNow && bNow && aHeard > t && late > 0,
                $"{a.Name} 이명 {(deaf ? "예" : "아니오")} · 경보 바로 {(aNow ? "들음" : "못 들음")} → {(aHeard - t) / (float)SimTime.TicksPerHour * 60f:0.#}분 뒤 · {b.Name} 바로 {(bNow ? "들음" : "못 들음")} · 늦게 {late} · 방송 놓침 {st.BroadcastMissed}(컴퓨터 다시 {st.ComputerRelays})");
        }

        // ── 5) 분진 폭발: 첫 폭발이 가루를 날리고 불길이 옮겨 붙는다 ──
        if (On(5))
        {
            var w = DayOne(seed, "Hanbit");
            var galley = w.Ship.KindOf(RoomType.Galley).First();
            var cells = galley.Cells.Where(c => w.Ship.IsOpenFloor(c)).ToList();
            var c0 = cells.First(c => cells.Contains(c + new Cell(1, 0)));
            var dust = w.Blast.Items.Place(ExplosiveKind.Dust, c0 + new Cell(1, 0), 0.8f);
            Clear(w, c0);
            w.Blast.Detonate(c0, 0.4f, BlastKind.Gas, "시험 — 버너 가스");
            Run(w, SimTime.Minutes(3));
            var sec = w.Blast.Recent.FirstOrDefault(r => r.Kind == BlastKind.Dust);
            Check("분진 폭발 — 2차 폭발", dust.Spent && sec != null && sec.Depth >= 1,
                $"가루 {(dust.Spent ? "터짐" : $"구름 {dust.Cloud * 100:0}%")} · {string.Join(" → ", w.Blast.Recent.Select(r => $"{r.Spec.Name}[{r.Depth}]"))} · 불 {w.Fire.CountIn(galley)}");
        }

        // ── 6) 폭약으로 용접된 문을 뚫는다 (작업대 → 설치 → 비키게 → 카운트다운 · 불발이면 다시) ──
        if (On(6))
        {
            var w = DayOne(seed, "Hanbit");
            var (door, inside, beyond) = BlastDoor(w, 1) ?? BlastDoor(w)!.Value;
            door.Welded = true; door.Openness = 0f;
            var o = w.Blast.Items.RequestBreach(door.Cell, BreachKind.Door, "시험 — 용접된 문")!;
            for (int i = 0; i < SimTime.Hours(10) && o.Stage != BreachOrder.Step.Done; i++) w.Step();
            var ist = w.Blast.Items.Stats;
            Check("폭약으로 막힌 문을 뚫는다", o.Stage == BreachOrder.Step.Done && door.JammedOpen && !door.Welded,
                $"단계 {o.Stage} · {o.Result} · 문 열림 {door.Openness:0.0}(걸림 {door.JammedOpen} · 용접 {door.Welded} · 날아감 {w.Blast.Blown(door)}) · 만듦 {ist.Crafted} · 불발 {ist.Duds} · 과폭 {ist.Overcharges} · 비키게 함 {ist.Cleared} · 몸을 피함 {ist.TookCover}");
        }

        // ── 7) 쉭 소리에 몸을 피한다 · 귀가 울리는 사람은 못 듣는다 ──
        if (On(7))
        {
            var w = DayOne(seed, "Hanbit");
            bool Pick(CrewMember c) => c.CanAct && c.IsAwake && c.Room != null && c.Room.Kind != RoomType.Corridor;
            for (int i = 0; i < 24 && !w.Crew.Any(Pick); i++) Run(w, SimTime.Minutes(10)); // 통합: 새 배에서는 첫 순간 깨어 있는 사람이 다 복도에 있을 수 있다 — 방에 들어설 때까지
            var z = w.Crew.First(Pick);
            var room = z.Room!;
            var deafOne = w.Crew.FirstOrDefault(c => c != z && c.CanAct && c.IsAwake);
            var spot = Cell.Dirs8.Select(d => z.Cell + d).FirstOrDefault(c => w.Ship.IsOpenFloor(c) && w.Ship.RoomAt(c) == room);
            if (spot == default) spot = z.Cell;
            if (deafOne != null)
            {
                w.Blast.Deafen(deafOne, 1f, 6f);
                var near = Cell.Dirs8.Select(d => spot + d).FirstOrDefault(c => w.Ship.IsOpenFloor(c) && c != z.Cell && w.Ship.RoomAt(c) == room);
                if (near != default) { deafOne.Position = near.Center; deafOne.PreviousPosition = deafOne.Position; }
            }
            var cell = w.Blast.Items.Place(ExplosiveKind.BatteryCell, spot, 1f);
            w.Step();
            w.Blast.Items.Ignite(cell, "시험 — 셀이 부풀었다");
            cell.FuseAt = w.Tick + SimTime.Minutes(4);
            float d0 = (z.Position - spot.Center).Length();
            Run(w, SimTime.Minutes(3));
            float d1 = (z.Position - spot.Center).Length();
            var ist = w.Blast.Items.Stats;
            Check("쉭 소리 — 알아챈 사람이 몸을 피한다", ist.TookCover > 0 && d1 > d0 + 1.5f,
                $"{z.Name} 거리 {d0:0.0} → {d1:0.0}칸 · 알아챔 {ist.Noticed} · 피함 {ist.TookCover} · 지금 일 {z.Job?.Label}");
            Check("귀가 울리는 사람은 쉭 소리를 못 듣는다", deafOne != null && ist.Unheard > 0,
                $"못 들음 {ist.Unheard} · {deafOne?.Name}");
        }

        // ── 8) 주 컴퓨터가 위험 배치를 읽고 경고 · 제안 → 사람이 옮긴다 ──
        if (On(8))
        {
            var w = DayOne(seed, "Hanbit");
            var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
            var spot = stove.Cells.SelectMany(fc => Cell.Dirs8.Select(d => fc + d)).First(c => w.Ship.IsOpenFloor(c) && !stove.UseSpots.Contains(c) && w.Blast.Items.At(c) == null);
            var tank = w.Blast.Items.Place(ExplosiveKind.OxygenTank, spot, 1f, w.Crew[0].Id);
            for (int i = 0; i < SimTime.Hours(1) && tank.WarnedAt < 0; i++) w.Step();
            var ist = w.Blast.Items.Stats;
            bool warned = tank.WarnedAt >= 0;
            string risk = tank.RiskWhy;
            // v19 '옮김' 표시는 제자리를 벗어났다는 뜻 — 들고 가다 일이 끊겨 조리대 곁에 내려놓아도 붙는다. 안전한 곳에 내려놓기를 끝낼 때까지 본다
            for (int i = 0; i < SimTime.Hours(6) && !(tank.Moved && tank.Carried < 0 && tank.Risk <= 0f); i++) w.Step(); // 내려놓기를 끝내면 위험 0
            float dist = (tank.Cell.Center - stove.Center).Length();
            Check("주 컴퓨터 — 조리대 곁 산소통을 읽고 경고 · 제안", warned && risk.Contains("곁"),
                $"경고 {ist.ComputerWarnings} · 제안 {ist.Proposals} · 위험 {tank.Risk * 100:0}% \"{risk}\"");
            Check("승무원 — 경고를 받고 안전한 곳으로 옮긴다", tank.Moved && dist >= 2.5f,
                $"옮김 {ist.Secured} · 조리대와 거리 {dist:0.0}칸 · 사람이 알아챔 {ist.CrewNoticed}");
        }

        // ── 9) 무한 연쇄 없음: 빽빽한 폭발성 물건 무더기 ──
        if (On(9))
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.LiveRooms.Where(r => r.Kind != RoomType.Corridor).OrderByDescending(r => r.Cells.Count(c => w.Ship.IsOpenFloor(c))).First();
            var cells = room.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - room.Center).LengthSquared()).ThenBy(c => c.X).ThenBy(c => c.Y).Take(28).ToList();
            var kinds = new[] { ExplosiveKind.OxygenTank, ExplosiveKind.GasCylinder, ExplosiveKind.FuelCan, ExplosiveKind.HydrogenTank, ExplosiveKind.WeldingGas, ExplosiveKind.Aerosol, ExplosiveKind.BatteryCell };
            var placed = cells.Select((c, i) => w.Blast.Items.Place(kinds[i % kinds.Length], c, 1f)).ToList();
            Clear(w, cells[0]);
            int before = w.Blast.Stats.Detonations;
            w.Blast.Items.Ignite(placed[0], "시험 — 첫 불씨");
            placed[0].FuseAt = w.Tick + 1;
            Run(w, SimTime.Hours(2));
            if (Environment.GetEnvironmentVariable("BLASTDBG") != null)
                foreach (var r in w.Blast.Recent) Console.WriteLine($"   [dbg] {r.Spec.Name} at {r.At} p{r.Power:0.00} d{r.Depth} shards {string.Join(",", r.Shards.Select(x => $"{x.Hit}:{(x.To - x.From).Length():0.0}"))} · 쉭 {string.Join(",", placed.Where(e => e.Primed).Select(e => e.Id))} · items {string.Join(",", placed.Take(6).Select(e => $"{e.Cell}{(e.Spent ? "x" : "")}"))}");
            int det = w.Blast.Stats.Detonations - before;
            bool live = placed.Any(e => e.Primed && !e.Spent);
            Check("무한 연쇄 없음 — 끝이 있다", det <= placed.Count + 12 && !live && w.Blast.Stats.MaxDepth <= ExplosiveSet.MaxDepth + 1,
                $"무더기 {placed.Count} · 폭발 {det} · 터진 것 {placed.Count(e => e.Spent)} · 깊이 {w.Blast.Stats.MaxDepth} · 끊김 {w.Blast.Items.Stats.Fizzled} · 미룸 {w.Blast.Items.Stats.Budgeted} · 남은 쉭 {(live ? "있음" : "없음")}");
        }

        // ── 10) 발효 항아리 · 규모 ──
        if (On(10))
        {
            var w = DayOne(seed, "Hanbit");
            var galley = w.Ship.KindOf(RoomType.Galley).First();
            var cells = galley.Cells.Where(c => w.Ship.IsOpenFloor(c) && w.Blast.Items.At(c) == null).ToList();
            var burp = w.Blast.Items.Place(ExplosiveKind.FermentJar, cells[0], 0.5f);
            burp.Pressure = 0.8f;
            var pop = w.Blast.Items.Place(ExplosiveKind.FermentJar, cells[^1], 0.5f);
            pop.Pressure = 1.05f;
            Run(w, SimTime.Hours(8));
            var rec = w.Blast.Recent.FirstOrDefault(r => r.Kind == BlastKind.Ferment);
            Check("발효 항아리 — 김을 빼거나, 안 빼면 펑 (개인 규모)", w.Blast.Items.Stats.Burped > 0 && pop.Spent && rec?.Scale == BlastScale.Personal,
                $"김 빼기 {w.Blast.Items.Stats.Burped} · 펑 {(pop.Spent ? "예" : "아니오")} · 규모 {(rec != null ? BlastSystem.ScaleName(rec.Scale) : "-")} · 냄새 {w.Smells.Level(galley, SmellKind.Foul):0.00}");
        }

        // ── 11) 폭발성 물건 23종 · 그림 없는 것 없음 (종류마다 이름 · 폭발 종류) ──
        if (On(11))
        {
            var specs = ExplosiveSet.AllSpecs;
            Check("폭발성 물건 20종 넘게 · 종류마다 다른 폭발", specs.Count >= 20 && specs.Select(s => s.Blast).Distinct().Count() >= 15,
                $"{specs.Count}종 · 폭발 종류 {specs.Select(s => s.Blast).Distinct().Count()} · {string.Join(" · ", specs.Take(8).Select(s => s.Name))} …");
            var w = DayOne(seed, "Hanbit");
            w.Blast.Items.Sync();
            Check("배에 실린 폭발성 물건 (재고 · 설비 · 이동식 배터리)", w.Blast.Items.All.Count >= 8,
                $"{w.Blast.Items.All.Count}개 · {string.Join(" · ", w.Blast.Items.All.GroupBy(e => e.Spec.Name).Select(g => $"{g.Key} {g.Count()}"))}");
        }

        // ── 12) 결정론 ──
        if (On(12))
        {
            uint H()
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                return SaveGame.StateHash(w);
            }
            uint a = H(), b = H();
            Check("결정론", a == b, $"지문 {a:x8} / {b:x8}");
            uint H2()
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(20));
                var bat = w.Ship.FurnitureOf(FurnitureType.Battery).First().Machine!;
                w.Blast.Items.Place(ExplosiveKind.OxygenTank, bat.Body.UseSpots.First(s => w.Ship.IsOpenFloor(s)), 1f);
                w.Volatile.Blow(bat, BlowKind.ThermalRunaway, "시험");
                Run(w, SimTime.Hours(6));
                return SaveGame.StateHash(w);
            }
            uint c1 = H2(), c2 = H2();
            Check("결정론 — 연쇄가 든 배", c1 == c2, $"지문 {c1:x8} / {c2:x8}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 폭발과 연쇄 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
