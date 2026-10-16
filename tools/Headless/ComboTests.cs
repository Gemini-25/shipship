using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// v19 해법 조합 점검 (--combotest): 공통 동작 여섯 (막기 · 고정하기 · 옮기기 · 연결하기 · 절연하기 · 냉각하기) × 물건 성질에서
/// 저절로 생긴 갈래 — 표에 없는 물건 쓰임이 성질대로 생기고 · 실제로 통하고 · 처음 통하면 본 사람이 놀라고 · 거듭 통하면 관행이 된다.
/// </summary>
public static partial class Program
{
    private static int RunComboList()
    {
        foreach (var g in WaysTable.All.Where(WaysCombo.Is).GroupBy(x => x.Snag))
        {
            Console.WriteLine($"{WaysTable.Name(g.Key)} ({WaysCombo.Name(WaysCombo.VerbOf(g.First())!.Value)})");
            foreach (var x in g.OrderByDescending(x => x.Power)) Console.WriteLine($"   {x.Id,-26} {x.Name} · 믿음 {x.Power:0.00} · 위험 {x.Risk:0.00} · {x.Look}");
        }
        Console.WriteLine($"조합 갈래 {WaysTable.All.Count(WaysCombo.Is)} · 손으로 적은 갈래 {WaysTable.All.Count(x => !WaysCombo.Is(x))}");
        return 0;
    }

    private static void CbSet(object o, string prop, object v) => o.GetType().GetProperty(prop)!.GetSetMethod(true)!.Invoke(o, new[] { v });

    private static int RunComboTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"해법 조합 점검 (v19) · 시드 {seed}\n");
        var combos = WaysTable.All.Where(WaysCombo.Is).ToList();
        bool Has(string id) => combos.Any(x => x.Id == id);

        // 1) 성질에서 생긴 갈래: 여섯 동작이 모두 쓰이고, 성질에 안 맞는 물건은 빠진다
        var verbs = combos.Select(x => WaysCombo.VerbOf(x)).Where(v => v != null).Select(v => v!.Value).Distinct().ToList();
        Check("공통 동작 여섯 — 막기 · 고정하기 · 옮기기 · 연결하기 · 절연하기 · 냉각하기가 모두 물건 성질에서 갈래를 낳는다", verbs.Count == 6,
            $"{string.Join(" · ", Enum.GetValues<Verb>().Select(v => $"{WaysCombo.Name(v)} {combos.Count(x => WaysCombo.VerbOf(x) == v)}"))} · 모두 {combos.Count}");
        Check("성질대로 — 말랑하고 새지 않는 고무 매트는 감싸고 · 깔고 · 끌고 · 괸다 / 종이는 문틈을 막고 몸을 덥힌다 / 금속은 두드려 소리를 잇는다",
            Has("leak.c.rubbermat") && Has("cargo.c.rubbermat") && Has("hurt.c.rubbermat") && Has("smoke.c.paperstack") && Has("cold.c.cardboardbox") && Has("comms.c.toolbox"),
            string.Join(" · ", new[] { "leak.c.rubbermat", "cargo.c.rubbermat", "hurt.c.rubbermat", "smoke.c.paperstack", "cold.c.cardboardbox", "comms.c.toolbox" }.Select(i => $"{i} {Has(i)}")));
        Check("성질에 안 맞으면 없다 — 얼음은 녹으면 전기가 통한다 · 무전기로 구멍을 막지 않는다 · 미끄러운 얼음으로 괴지 않는다 · 한 모금 유리병으로 불을 끄지 않는다",
            !Has("spark.c.iceblock") && !Has("breach.c.radio") && !Has("cargo.c.iceblock") && !Has("fire.c.glassjar") && !combos.Any(x => x.Snag == Snag.Spark && x.Things.Any(k => Materials.Of(ArticleSpecs.Of(k).Mat).Conduct > 0.3f)),
            $"얼음 절연 {Has("spark.c.iceblock")} · 무전기 마개 {Has("breach.c.radio")} · 얼음 쐐기 {Has("cargo.c.iceblock")} · 유리병 불 {Has("fire.c.glassjar")}");
        Check("손으로 적은 물건은 겹치지 않는다 (파공의 고무 매트 · 상자는 이미 표에 있다)", !Has("breach.c.rubbermat") && !Has("breach.c.plasticcrate") && WaysTable.Audit().Count == 0,
            $"감사 {string.Join(" / ", WaysTable.Audit().Take(3))}");

        // 2) 표의 물건이 없을 때 — 가루 포대를 구멍에 대고 버틴다 (스스로 고른다)
        {
            var w = WyShip(seed, "Mirinae");
            WyStrip(w, ItemKind.Sealant); WyStrip(w, ItemKind.Plate); WyStrip(w, ItemKind.Glue);
            foreach (var a in w.Matter.Things.Where(a => a.Kind is ArticleKind.RubberMat or ArticleKind.PlasticCrate or ArticleKind.CardboardBox or ArticleKind.Rug).ToList()) w.Matter.Remove(a);
            foreach (var f in w.Ship.Furniture.Where(f => f.Type is FurnitureType.Bed or FurnitureType.Cot or FurnitureType.MedBed or FurnitureType.Table or FurnitureType.GameTable or FurnitureType.Stove or FurnitureType.Oven
                         or FurnitureType.CoolantPump or FurnitureType.HeatExchanger or FurnitureType.Fridge or FurnitureType.SuitLocker)) CbSet(f, "Stowed", true); // 표에 적힌 마개(매트리스 · 상판 · 냄비 · 냉매)를 치운다
            foreach (var r in w.Robots.Robots) r.Disabled = true;
            var room = w.Ship.LiveRooms.Where(r => r.Kind != RoomType.Corridor && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r)).OrderBy(r => r.Id).First();
            var wallCell = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).Select(kv => kv.Key).OrderBy(c => c.X).ThenBy(c => c.Y).First();
            var floor = room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => Math.Abs(c.X - wallCell.X) + Math.Abs(c.Y - wallCell.Y)).First();
            w.Matter.Add(ArticleKind.PowderSack, floor, "시험");
            Hull.Damage(w.Ship, wallCell, 0.85f);
            var wall = w.Ship.WallAt(wallCell)!;
            WayTry? plug = null;
            for (int m = 0; m < 120 && (plug == null || plug.State != 3); m++)
            {
                Run(w, SimTime.Minutes(1));
                plug ??= w.Ways.Tries.FirstOrDefault(t => t.CrewId >= 0 && w.Ways.Case(t.CaseId)?.Snag == Snag.Breach && t.Way.Fx == WayFx.Plug);
            }
            var ck = w.Ways.CaseFor(Snag.Breach, room);
            Console.WriteLine($"     {room.Name} 파공: {WyTries(w, ck)}");
            Check("표에 적힌 마개가 없으면 성질이 맞는 물건을 스스로 찾아 쓴다 — 가루 포대를 구멍에 대고 버틴다", plug != null && plug.WayId == "breach.c.powdersack" && plug.Ok && wall.Patched,
                $"{WyName(plug?.WayId ?? "")} · {plug?.Result} · 봉합 {wall.PatchQuality:0.00}");
            Check("처음 통하면 연대기에 배운 것으로 남고 · 컴퓨터가 갈래 장부에 적는다",
                w.Ways.Stats.Combos >= 1 && w.History.Events.Any(e => e.Text.Contains("처음 해 보였다")) && w.Log.Entries.Any(e => e.Text.Contains("새 갈래를 적어 둡니다")),
                $"처음 통함 {w.Ways.Stats.Combos} · " + (w.Log.Entries.LastOrDefault(e => e.Text.Contains("풀었다 (")).Text ?? ""));
        }

        // 3) 동작마다 실제로 통하나 (사람을 정해 맡긴다)
        WayTry? Go(World w, Snag s, Room room, string wayId, CrewMember c, int minutes)
        {
            w.Ways.ScanNow();
            var k = w.Ways.CaseFor(s, room);
            if (k == null) { Console.WriteLine($"     ({WaysTable.Name(s)} 사건이 안 열렸다 — {room.Name})"); return null; }
            var t = w.Ways.Force(c, k, wayId);
            for (int m = 0; m < minutes && t != null && t.State != 3; m++) Run(w, SimTime.Minutes(1));
            return t;
        }
        (World w, Room room, CrewMember c, Cell at) Scene(string shipName = "Mirinae", Func<Room, bool>? pick = null)
        {
            var w = WyShip(seed, shipName);
            foreach (var r in w.Robots.Robots) r.Disabled = true;
            var room = w.Ship.LiveRooms.Where(r => r.Kind is not (RoomType.Corridor or RoomType.Medbay) && r.Cells.Count(w.Ship.IsOpenFloor) >= 6 && (pick == null || pick(r))).OrderBy(r => r.Id).First();
            var at = room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => x.X).ThenBy(x => x.Y).ElementAt(room.Cells.Count(w.Ship.IsOpenFloor) / 2);
            var c = w.Crew.Where(o => !o.Dead && o.CanAct && !o.IsChild && !o.Outside).OrderBy(o => o.Id).First();
            WyPut(w, c, at);
            Run(w, 2);
            return (w, room, c, at);
        }
        Cell Beside(World w, Room room, Cell at) => room.Cells.Where(x => w.Ship.IsOpenFloor(x) && x != at).OrderBy(x => Math.Abs(x.X - at.X) + Math.Abs(x.Y - at.Y)).First();

        // 막기 — 물 샘: 고무 매트로 감싸 조인다
        {
            var (w, room, c, at) = Scene();
            w.Matter.Add(ArticleKind.RubberMat, Beside(w, room, at), "시험");
            var watcher = w.Crew.Where(o => o != c && !o.Dead && o.CanAct && !o.IsChild).OrderBy(o => o.Id).First();
            room.Flood = 0.6f;
            w.Ways.ScanNow();
            var lk = w.Ways.CaseFor(Snag.Leak, room);
            WayTry? t = lk == null ? null : w.Ways.Force(c, lk, "leak.c.rubbermat");
            for (int m = 0; m < 40 && t != null && t.State != 3; m++)
            {
                if (watcher.Room != room) { WyPut(w, watcher, Beside(w, room, Beside(w, room, at))); watcher.Pose = Pose.Sitting; } // 옆에서 지켜본다
                Run(w, SimTime.Minutes(1));
            }
            Check("막기 — 물 샘: 고무 매트로 새는 곳을 감싸 조인다 (물이 준다 · 매트는 그 자리에 남는다)", t != null && t.Ok && room.Flood < 0.6f * 0.7f && w.Matter.Things.Any(a => a.Kind == ArticleKind.RubberMat && a.CarriedBy < 0 && w.Ship.RoomAt(a.At) == room),
                $"{t?.Result ?? "없음"} · 물 0.60 → {room.Flood:0.00}");
            var wow = w.Log.Entries.LastOrDefault(e => e.Text.Contains("저걸 저렇게 쓰네"));
            Check("처음 통하면 옆에서 본 사람이 \"저걸 저렇게 쓰네?\" — 본 사람의 기억에도 남는다", wow.Text != null && wow.Text.Contains(watcher.Name) && watcher.Memory.Marks.Any(mk => mk.Text.Contains("저걸 저렇게 쓰네")),
                wow.Text ?? "놀람 없음");
        }
        // 막기 — 연기: 서류 뭉치로 문틈
        {
            var (w, room, c, at) = Scene(pick: r => r.Doors.Any(d => !d.IsExternal));
            w.Matter.Add(ArticleKind.PaperStack, Beside(w, room, at), "시험");
            room.Air.Smoke = 0.8f;
            foreach (var d in room.Doors) if (!d.IsExternal) d.Openness = 1f;
            var t = Go(w, Snag.Smoke, room, "smoke.c.paperstack", c, 30);
            Check("막기 — 연기: 서류 뭉치로 문틈을 막는다 (문이 닫히고 옆방으로 덜 번진다)", t != null && t.Ok && room.Doors.Where(d => !d.IsExternal && !d.JammedOpen && !d.Removed).All(d => d.Openness <= 0.11f),
                $"{t?.Result ?? "없음"} · 문 {string.Join(",", room.Doors.Select(d => $"{d.Openness:0.0}"))}");
        }
        // 고정하기 — 미끄러지는 짐 밑에 공구함을 괸다
        {
            var (w, room, c, at) = Scene();
            var box = w.Matter.Add(ArticleKind.Toolbox, Beside(w, room, at), "시험");
            var load = w.Matter.Add(ArticleKind.FoodCrate, at, "시험");
            CbSet(w.Matter, "Gravity", 0.1f); // 기동 중 무중력 — 짐이 떠서 미끄러진다
            ObjectPhysics.Push(w, at, 40f, new System.Numerics.Vector2(1f, 0f), "시험 — 짐이 미끄러진다");
            WayTry? t = null;
            if (load.Vel.LengthSquared() >= 1.2f * 1.2f) t = Go(w, Snag.CargoLoose, w.Ship.RoomAt(load.At) ?? room, "cargo.c.toolbox", c, 20);
            Check("고정하기 — 미끄러지는 짐 밑에 공용 공구함을 괴어 세운다 (괸 물건이 아니라 짐이 선다)", t != null && t.Ok && (load.Fixed || load.Vel.LengthSquared() < 0.01f) && !box.Fixed,
                $"{t?.Result ?? $"짐 속도 {load.Vel.Length():0.0}"} · 짐 고정 {load.Fixed} · 공구함 {box.Fixed}");
        }
        // 옮기기 — 러그에 눕혀 끌고 간다
        {
            var (w, room, c, at) = Scene();
            var rug = w.Matter.Add(ArticleKind.Rug, Beside(w, room, at), "시험");
            var pt = w.Crew.Where(o => o != c && !o.Dead && o.CanAct && !o.IsChild).OrderBy(o => o.Id).First();
            WyPut(w, pt, Beside(w, room, Beside(w, room, at)));
            pt.Vitals.Oxygen = 0.1f; pt.Down = true; pt.Pose = Pose.Down; NeedsSystem.AddInjury(pt.Vitals, 0.4f, "시험 — 넘어짐");
            Run(w, 2);
            w.Ways.ScanNow();
            var hk = w.Ways.Cases.LastOrDefault(x => x.Snag == Snag.Injured && x.CrewId == pt.Id && x.Open);
            WayTry? t = hk == null ? null : w.Ways.Force(c, hk, "hurt.c.rug");
            for (int m = 0; m < 60 && t != null && t.State != 3; m++) Run(w, SimTime.Minutes(1));
            Check("옮기기 — 러그에 눕혀 끌고 간다 (다친 사람이 치료 침대 · 안전한 곳에 닿고 러그는 거기 남는다)", t != null && t.Ok && t.Result.Contains("옮겼다") && rug.CarriedBy < 0 && Math.Abs(rug.At.X - pt.Cell.X) + Math.Abs(rug.At.Y - pt.Cell.Y) <= 1,
                $"{t?.Result ?? "없음"} · 러그 {(rug.At == pt.Cell ? "곁에" : $"{rug.At}")} · 치료 침대 {pt.CareBed != null}");
        }
        // 연결하기 — 공구함으로 배관을 두드려 신호를 잇는다
        {
            var (w, room, c, at) = Scene();
            w.Matter.Add(ArticleKind.Toolbox, Beside(w, room, at), "시험");
            room.DataLinked = false;
            var t = Go(w, Snag.CommsDown, room, "comms.c.toolbox", c, 20);
            Check("연결하기 — 통신이 끊긴 방에서 공용 공구함으로 배관을 두드려 신호를 잇는다", t != null && t.Ok, $"{t?.Result ?? "없음"} · {t?.Thing}");
        }
        // 절연하기 — 플라스틱 상자로 튀는 선을 덮는다 · 젖은 러그는 쓰지 않는다
        {
            var (w, room, c, at) = Scene();
            var j = w.Matter.AddJunction(at);
            CbSet(j, "Wet", 0.6f); CbSet(j, "Live", true); CbSet(j, "LastSpark", w.Tick); // 물이 스민 접속부 — 젖어 있는 동안 튄다
            var rug = w.Matter.Add(ArticleKind.Rug, Beside(w, room, at), "시험");
            CbSet(rug, "Water", rug.Spec.Capacity); // 흠뻑 젖은 러그
            w.Ways.ScanNow();
            var k = w.Ways.CaseFor(Snag.Spark, room);
            var wetCheck = k == null ? default : w.Ways.Check(c, WaysTable.Get("spark.c.rug")!, k, room);
            w.Matter.Add(ArticleKind.PlasticCrate, Beside(w, room, at), "시험");
            var t = Go(w, Snag.Spark, room, "spark.c.plasticcrate", c, 30);
            if (t is { Ok: false } && j.Live) // 젖은 접속부에 데어 쓰러졌다 — 전기를 아는 다른 사람이 다시 한다
            {
                var c2 = w.Crew.Where(o => o != c && !o.Dead && o.CanAct && !o.IsChild && !o.Outside).OrderByDescending(o => o.SkillLevel(Skill.Electrical)).ThenBy(o => o.Id).First();
                Console.WriteLine($"     {c.Name}: {t.Result} → {Ko.IGa(c2.Name)} 다시");
                WyPut(w, c2, at);
                if (w.Matter.Things.All(a => a.Kind != ArticleKind.PlasticCrate || a.CarriedBy >= 0)) w.Matter.Add(ArticleKind.PlasticCrate, Beside(w, room, at), "시험");
                t = Go(w, Snag.Spark, room, "spark.c.plasticcrate", c2, 30);
            }
            Check("절연하기 — 젖은 러그로는 덮지 않는다 (전기가 통한다)", k != null && !wetCheck.Ok && wetCheck.Note.Contains("젖어"), $"{(k == null ? "사건 없음" : wetCheck.Note)}");
            Check("절연하기 — 플라스틱 상자로 튀는 선을 덮어 누른다 (접속부가 죽는다)", t != null && t.Ok && !j.Live && j.Taped, $"{t?.Result ?? "없음"} · 살아 있음 {j.Live} · 감김 {j.Taped}");
        }
        // 절연하기 — 추위: 종이 상자를 두르고 깔고 앉는다
        {
            var (w, room, c, at) = Scene();
            w.Matter.Add(ArticleKind.CardboardBox, Beside(w, room, at), "시험");
            room.Air.Temperature = 6f;
            c.Needs.Stress = 0.6f;
            var t = Go(w, Snag.Cold, room, "cold.c.cardboardbox", c, 20);
            Check("절연하기 — 추위: 종이 상자를 두르고 깔고 앉는다 (덜 떤다)", t != null && t.Ok && c.Needs.Stress < 0.6f, $"{t?.Result ?? "없음"} · 긴장 0.60 → {c.Needs.Stress:0.00}");
        }
        // 냉각하기 — 과열: 물통으로 식힌다
        {
            var (w, room, c, at) = Scene();
            w.Matter.Add(ArticleKind.WaterJug, Beside(w, room, at), "시험");
            room.Air.Temperature = 44f;
            float t0 = 0f;
            w.Ways.ScanNow();
            var k = w.Ways.CaseFor(Snag.Overheat, room);
            WayTry? t = null;
            if (k != null) { t = w.Ways.Force(c, k, "heat.c.waterjug"); for (int m = 0; m < 20 && t != null && t.State != 3; m++) { t0 = room.Air.Temperature; Run(w, SimTime.Minutes(1)); } }
            Check("냉각하기 — 과열: 물통의 물로 달아오른 설비를 식힌다 (방 온도가 내려간다)", t != null && t.Ok && room.Air.Temperature < t0, $"{t?.Result ?? "없음"} · {t0:0.0} → {room.Air.Temperature:0.0}℃");
        }
        // 냉각하기 — 불: 얼음 덩어리를 던져 넣는다
        {
            var (w, room, c, at) = Scene();
            WyStrip(w, ItemKind.Extinguisher);
            var fireAt = Beside(w, room, Beside(w, room, at));
            w.Matter.Add(ArticleKind.IceBlock, Beside(w, room, at), "시험");
            Player.Fire(w, fireAt);
            Run(w, SimTime.Minutes(1));
            int before = w.Fire.CountIn(room);
            var t = Go(w, Snag.Fire, room, "fire.c.iceblock", c, 20);
            Check("냉각하기 — 불: 얼음 덩어리를 불에 던져 넣는다 (녹으며 불을 덮는다 · 얼음은 없어진다)", t != null && (t.Ok || w.Fire.CountIn(room) < before) && !w.Matter.Things.Any(a => a.Kind == ArticleKind.IceBlock && w.Ship.RoomAt(a.At) == room),
                $"{t?.Result ?? "없음"} · 불 {before} → {w.Fire.CountIn(room)}칸");
        }

        // 4) 거듭 통하면 이 배의 관행이 된다 — 다음 사람이 그 이유로 고른다
        {
            var (w, room, c, at) = Scene(pick: r => r.Doors.Any(d => !d.IsExternal));
            int ok = 0;
            for (int i = 0; i < 3; i++)
            {
                WyPut(w, c, at); Run(w, 2);
                w.Matter.Add(ArticleKind.PaperStack, Beside(w, room, at), "시험");
                room.Air.Smoke = 0.8f;
                foreach (var d in room.Doors) if (!d.IsExternal) d.Openness = 1f;
                var t = Go(w, Snag.Smoke, room, "smoke.c.paperstack", c, 30);
                if (t?.Ok == true) ok++;
                room.Air.Smoke = 0f;
                Run(w, SimTime.Hours(2.5f)); // 같은 사건이 다시 열리게
            }
            var p = w.Ways.PracticeOrNull("smoke.c.paperstack");
            Check("거듭 통한 조합은 이 배의 관행이 된다 (연대기 · 관행 목록)", p is { Custom: true } && w.Ways.CustomLines().Any(l => l.Contains("서류 뭉치")),
                $"통함 {ok}/3 · 관행 {p?.Custom} · {p?.Origin}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 해법 조합" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
