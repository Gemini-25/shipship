using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v10.3 신뢰성 점검: 검토에서 재현된 문제들이 다시 나오지 않는지 하나씩 확인한다
public static partial class Program
{
    private static int _fails;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _fails++;
        Console.WriteLine($"  {(ok ? "✔" : "✘")} {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
    }

    private static World Day1(int seed)
    {
        var w = World.CreateDefault(seed);
        for (int t = 0; t < SimTime.TicksPerDay; t++) w.Step();
        return w;
    }

    private static void Run(World w, long ticks) { for (long t = 0; t < ticks; t++) w.Step(); }

    private static int RunSelfTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"신뢰성 점검 (v10.3~v10.7) · 시드 {seed}\n");

        // ── 1) 되감기: 사고 직전으로 되감으면 그 사고가 다시 일어나고, 새 사고를 일으키면 갈라진다 ──
        {
            var w = Day1(seed);
            var galley = w.Ship.RoomsOf(RoomType.Galley).First();
            long fireTick = w.Tick;
            Player.Fire(w, galley.Cells.First(w.Ship.IsOpenFloor));
            Run(w, SimTime.Hours(2));
            uint h = SaveGame.StateHash(w);
            int fires = w.History.Fires;
            var runner = new ReplayRunner(SaveGame.WriteAt(w, fireTick - SimTime.Minutes(3)));
            while (!runner.Advance(50000)) { }
            int upcoming = runner.Upcoming;
            while (runner.World.Tick < w.Tick) runner.World.Step();
            Check("되감기 → 원래 사고가 다시 일어난다", upcoming == 1 && runner.World.History.Fires == fires && SaveGame.StateHash(runner.World) == h,
                $"다시 일어날 사고 {upcoming}건 · 화재 {runner.World.History.Fires}/{fires}");
            var branch = new ReplayRunner(SaveGame.WriteAt(w, fireTick - SimTime.Minutes(3)));
            while (!branch.Advance(50000)) { }
            Player.Fire(branch.World, w.Ship.RoomsOf(RoomType.Mess).First().Cells.First(branch.World.Ship.IsOpenFloor));
            bool split = branch.World.Scheduled.Count == 0 && branch.World.Log.Entries.Any(e => e.Text.Contains("갈라진다"));
            Check("되감은 뒤 새 사고 → 뒤의 사고는 버리고 갈라진다", split);
        }

        // ── 2) 칸막이를 세우면 가던 길이 새 벽을 지나지 않는다 ──
        {
            var w = Day1(seed);
            var q = w.Ship.RoomsOf(RoomType.Quarters).First();
            var plan = Remodel.FindSplit(w, q)!;
            var across = plan.Vertical ? new Cell(1, 0) : new Cell(0, 1);
            var c = w.Crew[0];
            // 칸막이 줄을 문이 아닌 곳에서 곧장 가로지르는 길 (칸막이를 세우기 전에 짠다)
            Cell? from = null, to = null;
            foreach (var l in plan.Wall.OrderByDescending(x => (x.Center - plan.Door.Center).LengthSquared()))
            {
                var f0 = l - across; var t0 = l + across + across;
                bool aSide = !plan.SideB.Contains(f0);
                if (!aSide) (f0, t0) = (l + across, l - across - across);
                if (w.Ship.IsOpenFloor(f0) && w.Ship.IsOpenFloor(t0) && w.Ship.IsOpenFloor(l)) { from = f0; to = t0; break; }
            }
            if (from == null || to == null) { Check("칸막이 전에 짠 길 (가로지를 자리 없음)", false); goto afterWalk; }
            c.Position = from.Value.Center;
            c.PreviousPosition = c.Position;
            bool ok = Locomotion.SetDestination(c, w, to.Value);
            var saved = c.Path!.ToList();
            var line = plan.Wall.Where(x => x != plan.Door).ToHashSet();
            bool crossed = saved.Any(line.Contains);
            Remodel.Apply(w, plan);
            // 개조 전에 짠 길을 그대로 쥐고 걷게 한다 (검토에서 재현된 상황)
            c.Path = saved;
            c.PathIndex = 0;
            c.Destination = to.Value;
            bool throughWall = false;
            for (int i = 0; i < 2000 && c.Path != null; i++)
            {
                Locomotion.Step(c, w);
                foreach (var d in w.Ship.Doors) { d.Openness = 1f; } // 문 여닫이는 World.Step이 하므로 여기선 열어 둔다
                if (w.Ship.Grid.Kind(c.Cell) == TileKind.Wall) throughWall = true;
            }
            Check("칸막이 전에 짠 길로 걸어도 새 벽을 지나지 않는다 (문으로 돌아간다)", ok && crossed && !throughWall && c.Cell == to.Value, $"옛 길이 칸막이를 가로지름 {crossed} · 도착 {c.Cell} · 목표 {to}");
            afterWalk:
            // 온 배를 몇 시간 돌려도 벽 칸에 선 사람이 없다
            bool anyone = false;
            for (int t = 0; t < SimTime.Hours(4); t++)
            {
                w.Step();
                if (w.Crew.Any(x => !x.Dead && !x.Outside && w.Ship.Grid.Kind(x.Cell) == TileKind.Wall)) anyone = true;
            }
            Check("칸막이 뒤 네 시간 — 벽 칸에 선 사람 없음", !anyone);
        }

        // ── 3) 개조 뒤 작업 대상은 새 방을 본다 ──
        {
            var w = Day1(seed);
            var q = w.Ship.RoomsOf(RoomType.Quarters).First();
            var plan = Remodel.FindSplit(w, q)!;
            var spot = plan.SideB.Where(w.Ship.IsOpenFloor).OrderByDescending(x => (x.Center - plan.Door.Center).LengthSquared()).First();
            w.Fire.Ignite(spot, 0.3f);
            for (int t = 0; t < 60; t++) { w.Board.RequestScan(); w.Step(); }
            var inner = Remodel.Apply(w, plan)!;
            var bad = w.Board.All.Where(o => o.Target.Kind is TargetKind.Cell or TargetKind.Wall && w.Ship.RoomAt(o.Target.Cell) is Room at && at != o.Target.Room).ToList();
            var furn = w.Board.All.Where(o => o.Target.Furniture is Furniture f && o.Target.Room != f.Room).ToList();
            bool fireOrder = w.Board.All.Any(o => o.Target.Room == inner && o.Kind == WorkKind.Extinguish);
            Check("개조 뒤 작업 대상의 방", bad.Count == 0 && furn.Count == 0 && fireOrder,
                $"칸 대상 어긋남 {bad.Count} · 설비 대상 어긋남 {furn.Count} · 안쪽 칸 불 끄기 {(fireOrder ? "있음" : "없음")}");
        }

        // ── 4) 방이 떨어져 나갈 때 열린 문과 닫힌 문의 결과가 다르다 ──
        {
            float Plug(bool open)
            {
                var w = Day1(seed);
                var room = w.Ship.RoomsOf(RoomType.Quarters).First();
                var door = room.Doors.First(d => !d.IsExternal && (d.RoomA?.Type == RoomType.Corridor || d.RoomB?.Type == RoomType.Corridor));
                door.Openness = open ? 1f : 0f;
                door.JammedOpen = open;
                w.Structure.Detach(room, "시험", controlled: false);
                return w.Ship.WallAt(door.Cell)?.Integrity ?? -1f;
            }
            float o = Plug(true), cl = Plug(false);
            Check("떨어져 나갈 때 열린 문은 뚫리고 닫힌 문은 버틴다", o < cl, $"열림 {o:0.00} · 닫힘 {cl:0.00}");
        }

        // ── 5) 다른 방의 재배대도 급수 본관이 끊기면 물을 못 받는다 ──
        {
            var w = Day1(seed);
            var lounge = w.Ship.RoomsOf(RoomType.Lounge).First();
            var cells = Adaptation.FreeCells(w, lounge).Take(1).ToList();
            if (cells.Count > 0) w.Ship.AddFurniture(FurnitureType.GrowBed, cells);
            bool before = w.Piping.WaterTo(lounge);
            if (w.Piping.WaterMain is PipeSegment main) main.Closed = true;
            bool after = w.Piping.WaterTo(lounge);
            Check("휴게실 재배대: 급수 본관이 끊기면 물이 안 간다", cells.Count > 0 && before && !after, $"본관 전 {before} · 후 {after}");
        }

        // ── 6) 지문이 뒤의 결과를 바꾸는 상태를 본다 ──
        {
            var a = Day1(seed);
            uint h0 = SaveGame.StateHash(a);
            bool Differs(Action<World> change)
            {
                var b = Day1(seed);
                change(b);
                return SaveGame.StateHash(b) != h0;
            }
            var tests = new (string, Action<World>)[]
            {
                ("혈중 산소", w => w.Crew[0].Vitals.Oxygen -= 0.01f),
                ("방 온도", w => w.Ship.Rooms[3].Air.Temperature += 0.5f),
                ("불", w => w.Fire.Ignite(w.Ship.RoomsOf(RoomType.Mess).First().Cells.First(w.Ship.IsOpenFloor), 0.2f)),
                ("작물", w => w.Ship.FurnitureOf(FurnitureType.GrowBed).First().Machine!.Crop!.Growth += 0.01f),
                ("난수", w => w.Rng.Float()),
            };
            var missed = tests.Where(t => !Differs(t.Item2)).Select(t => t.Item1).ToList();
            Check("지문이 혈중 산소·방 온도·불·작물·난수를 본다", missed.Count == 0, missed.Count > 0 ? "못 보는 것: " + string.Join(", ", missed) : "");
        }

        // ── 7) 저장 시점 뒤의 기록이 든 파일도 불러오기가 끝난다 · 망가진 파일은 까닭을 알린다 ──
        {
            var w = Day1(seed);
            Player.Fire(w, w.Ship.RoomsOf(RoomType.Galley).First().Cells.First(w.Ship.IsOpenFloor));
            string text = SaveGame.Write(w).Replace($"tick {w.Tick}", $"tick {w.Tick - SimTime.Hours(1)}");
            var runner = new ReplayRunner(text);
            int guard = 0;
            while (!runner.Advance(50000) && guard++ < 100) { }
            Check("저장 시점 뒤의 기록 → 불러오기가 100%에서 끝난다", runner.Done && runner.Upcoming == 1, $"반복 {guard} · 앞으로 {runner.Upcoming}건");
            string? why = null;
            try { SaveGame.Parse("shipsim-save 9\nseed x\n"); } catch (FormatException e) { why = e.Message; }
            try { if (why == null) new ReplayRunner("shipsim-save 9\nseed 1\ntick 10\ncmd 5 break 999999\n"); }
            catch (Exception e) when (e is FormatException or ArgumentException) { why = e.Message; }
            Check("망가진 저장 파일은 예외로 까닭을 알린다 (화면은 새 항해로)", why != null, why ?? "");
        }

        // ── 8) 한 틱에 여러 경보가 울려도 치명 경보를 놓치지 않는다 ──
        {
            var w = Day1(seed);
            long seen = w.AlertSerial;
            w.RaiseAlert("시험 치명", null, AlertLevel.Critical, shipWide: true);
            w.RaiseAlert("시험 경고", null, AlertLevel.Warning, shipWide: false);
            var fresh = w.Alerts.Where(a => a.Serial > seen).ToList();
            Check("경보 순번: 치명 뒤에 경고가 붙어도 둘 다 보인다", fresh.Count == 2 && fresh.Any(a => a.Level == AlertLevel.Critical));
        }

        // ── 9) v10.7 수치 조정: 시작 수치와 항해 중에 바꾼 수치가 저장·불러오기에서 같은 역사를 만든다 (다른 배에서도) ──
        {
            Tuning.ResetDefaults();
            Tuning.Apply("research.bench", 12f);
            var w = World.CreateDefault(seed, 0, "hanbit");
            Run(w, SimTime.Hours(20));
            Player.Tune(w, "food.grow_hours", 30f);
            Run(w, SimTime.Hours(10));
            uint h = SaveGame.StateHash(w);
            string text = SaveGame.Write(w);
            Tuning.ResetDefaults();
            var runner = new ReplayRunner(text);
            while (!runner.Advance(50000)) { }
            bool same = SaveGame.StateHash(runner.World) == h && MathF.Abs(FoodChain.GrowHours - 30f) < 1e-4f && MathF.Abs(Tuning.ResearchPerBenchDay - 12f) < 1e-4f;
            Check("수치 조정(시작·항해 중)이 저장·불러오기에서 같은 역사 · 한빛호", same && text.Contains("tune research.bench") && text.Contains("ship " + w.ShipKey) && w.ShipKey != ShipCatalog.Default.Key,
                $"지문 {(SaveGame.StateHash(runner.World) == h ? "같음" : "다름")} · 생장 {FoodChain.GrowHours} · 연구 {Tuning.ResearchPerBenchDay} · 틱 {runner.World.Tick}/{w.Tick}");
            Tuning.ResetDefaults();
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 신뢰성 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
