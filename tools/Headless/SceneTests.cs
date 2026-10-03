using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.1 일상의 실제 행동화: 장면 (체스 · 커피 · 영화 · 국 · 간식 · 몽유병 · 소품) · 교대 인수인계 · 쪽지
public static partial class Program
{
    /// <summary>앞으로 열한 시간은 쉬는 사람으로 (근무 · 잠이 장면을 흔들지 않게).</summary>
    private static void ScFree(World w, CrewMember c)
    {
        float h = MathF.Floor(SimTime.HourOfDay(w.Tick));
        c.Schedule = new Schedule { SleepStart = SimTime.Wrap(h + 11f), SleepLength = 8f, WorkStart = SimTime.Wrap(h + 19f), WorkLength = 5f };
        c.Needs.Food = 1f; c.Needs.Rest = 1f;
    }

    private static bool ScUntil(World w, Func<bool> cond, float hours, int step = 15)
    {
        long end = w.Tick + SimTime.Hours(hours);
        while (w.Tick < end) { if (cond()) return true; Run(w, step); }
        return cond();
    }

    private static Room? ScFireRoom(World w, int avoid) =>
        w.Ship.LiveRooms.Where(r => r.Id != avoid && r.Type is RoomType.Storage or RoomType.Workshop or RoomType.Cargo && r.Cells.Any(w.Ship.IsWalkable)).OrderBy(r => r.Id).FirstOrDefault()
        ?? w.Ship.LiveRooms.Where(r => r.Id != avoid && r.Type != RoomType.Corridor && r.Cells.Any(w.Ship.IsWalkable)).OrderBy(r => r.Id).FirstOrDefault();

    private static int RunSceneTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"일상 장면 점검 (v16.1) · 시드 {seed}\n");
        try
        {
            // 1) 첫 검증 장면: 체스 → 경보로 떠남 → 복구 뒤 그대로 남은 판으로 돌아와 이어 둠 → 다른 사람이 커피를 놓아 줌
            {
                var w = DayOne(seed, "Hanbit");
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
                var a = adults[0]; var b = adults[1]; var cof = adults[2];
                foreach (var c in new[] { a, b, cof }) ScFree(w, c);
                if (!a.Hobbies.Contains(Hobby.Chess)) a.Hobbies.Add(Hobby.Chess);
                if (w.Belongings.ItemFor(a, Hobby.Chess) == null) w.Belongings.Seed2(a, BelongingKind.ChessSet);
                var s = w.Scenes.OpenChess(a, b)!;
                if (s.Other < 0) { s.Other = b.Id; s.Invited.Add(b.Id); s.Declined.Clear(); } // 상대가 받았다고 치고
                bool played = ScUntil(w, () => s.Stage == SceneStage.Run && s.Progress > 0.3f, 3f);
                var g = w.Belongings.Games.FirstOrDefault(x => x.Id == s.Game);
                Check("체스 — 체스판을 가져와 탁자에 펴고, 상대가 와서 둔다", played && g != null && g.Scene == s.Id,
                    $"{s.Title} · {s.Stage} · 진척 {s.Progress:P0} · {b.Name}: {b.ActivityLabel} · {string.Join(" / ", s.Trail.TakeLast(3))}");

                var fire = ScFireRoom(w, s.RoomId)!;
                w.Fire.Ignite(fire.Cells.First(w.Ship.IsWalkable), 0.9f);
                bool paused = ScUntil(w, () => s.Stage == SceneStage.Paused, 0.5f);
                float movesAtPause = g?.Moves ?? 0f;
                var set = w.Belongings.Get(s.Item);
                bool left = s.Here.Count == 0 && a.Job?.Activity != SceneActivity.Instance;
                Check("경보 — 판을 두고 떠난다 (판 · 진척은 그 자리에)", paused && left && set?.At == s.Table && s.Things.Any(t => t.Kind == ThingKind.Board),
                    $"{fire.Name} 화재 · {s.Stage} ({s.PauseWhy}) · {movesAtPause:0}수 · 체스판 {(set?.At == s.Table ? "탁자 위" : "?")} · {a.Name}: {a.ActivityLabel}");

                bool calm = ScUntil(w, () => !Crisis.Acting(w), 6f, 60);
                foreach (var c in new[] { a, b }) { c.Needs.Food = MathF.Max(c.Needs.Food, 0.7f); }
                bool back = ScUntil(w, () => s.Stage == SceneStage.Run && s.Here.Contains(a.Id) && s.Here.Contains(b.Id), 6f);
                float movesAtResume = g?.Moves ?? -1f;
                Check("복구 뒤 — 둘이 그대로 남은 판으로 돌아와 이어 둔다 (진척 보존)", calm && back && movesAtResume >= movesAtPause - 0.01f && w.Scenes.Stats.Resumed >= 1,
                    $"복구 {calm} · 돌아옴 {back} · {movesAtPause:0}수 → {movesAtResume:0}수 · {s.Trail.LastOrDefault(t => t.Contains("다시"))}");

                // 판을 두는 동안 다른 사람이 커피를 놓아 준다
                foreach (var k in new[] { ItemKind.Coffee, ItemKind.TeaLeaf })
                    if (w.Ship.CountStored(k) < 3) w.Ship.Containers.First(f => f.Storage!.Accepts(k) && f.Storage.Free > 3).Storage!.Add(k, 4);
                int stock0 = w.Ship.CountStored(ItemKind.Coffee) + w.Ship.CountStored(ItemKind.TeaLeaf);
                float affA0 = a.AffinityTo(cof), affB0 = b.AffinityTo(cof);
                int mem0 = w.Relations.All.Count(m => m.About == cof.Id && m.Reason == RelationReason.Comforted);
                cof.Needs.Food = 1f; cof.Needs.Rest = 1f;
                var cs = w.Scenes.OpenCoffee(cof, new List<CrewMember> { a, b })!;
                bool served = ScUntil(w, () => !cs.Open, 2f);
                int stock1 = w.Ship.CountStored(ItemKind.Coffee) + w.Ship.CountStored(ItemKind.TeaLeaf);
                int mem1 = w.Relations.All.Count(m => m.About == cof.Id && m.Reason == RelationReason.Comforted);
                var cups = cs.Things.Where(t => t.Kind == ThingKind.Cup).ToList();
                Check("커피 — 타서(재고가 줄고) 들고 가 판 곁에 놓아 준다 · 받은 사람의 마음과 관계가 바뀐다",
                    served && cs.Delivered >= 1 && stock1 < stock0 && cups.Count >= 1 && (a.AffinityTo(cof) > affA0 || b.AffinityTo(cof) > affB0) && mem1 > mem0,
                    $"{cs.Title} · {cs.Stage} · 놓음 {cs.Delivered}/{cs.Targets.Count} · 재고 {stock0} → {stock1} · 잔 {cups.Count}개({string.Join(",", cups.Select(t => t.At == s.Table ? "탁자" : "곁"))}) · 관계 기억 {mem0} → {mem1}");

                bool done = ScUntil(w, () => !s.Open, 4f);
                Check("판이 끝난다 — 끊긴 판을 이어 둔 대국 (이긴 사람 · 끊김이 기록에)", done && s.Stage == SceneStage.Done && g!.Done && g.Winner >= 0 && s.Pauses >= 1,
                    $"{s.Stage} · {g?.Moves:0}수 · 끊김 {s.Pauses} · 이긴 사람 {w.Crew.FirstOrDefault(c => c.Id == g?.Winner)?.Name} · {w.Scenes.Stats.Summary()}");
            }

            // 2) 영화의 밤: 제안 → 참석 판단 → (자리 모자라면 바닥) → 경보에 멈춤 → 남은 사람이 이어 봄
            {
                var w = DayOne(seed, "Hanbit");
                var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Theater or RoomType.Lounge && !r.OffLimits).OrderBy(r => r.Type == RoomType.Theater ? 0 : 1).ThenBy(r => r.Id).First();
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
                foreach (var c in adults) ScFree(w, c);
                var host = adults[0];
                if (!host.Hobbies.Contains(Hobby.Movies)) host.Hobbies.Add(Hobby.Movies);
                var s = w.Scenes.OpenMovie(host, room)!;
                foreach (var c in adults.Skip(1)) if (!s.Invited.Contains(c.Id)) { s.Invited.Add(c.Id); s.Declined.Remove(c.Id); } // 다들 오겠다고 했다 (자리가 모자라게)
                int seats = room.Furniture.Count(f => f.Type == FurnitureType.Seat);
                bool running = ScUntil(w, () => s.Stage == SceneStage.Run && s.Progress > 0.25f && s.Here.Count >= 2, 3f);
                int came = s.Joined.Count;
                Check("영화의 밤 — 제안하고 저마다 와서 앉는다 (자리가 모자라면 바닥)", running && came >= 2 && (seats >= came || s.Floor.Count > 0),
                    $"{room.Name} · 초대 {s.Invited.Count} · 못 옴 {s.Declined.Count} · 온 사람 {came} · 의자 {seats} · 바닥 {s.Floor.Count} · 늦게 옴 {w.Scenes.Stats.Late} · 진척 {s.Progress:P0}");

                var fire = ScFireRoom(w, room.Id)!;
                w.Fire.Ignite(fire.Cells.First(w.Ship.IsWalkable), 0.9f);
                bool paused = ScUntil(w, () => s.Stage == SceneStage.Paused, 0.5f);
                float at = s.Progress;
                // 한 사람은 불을 끄고 지쳐서 자러 간다 (돌아오지 않는다)
                // 통합7 막 들어와 1%만 본 사람을 고르면 '결말을 못 봤다'고 적을 만큼 본 게 없다 — 앞부분을 꽤 본 사람이 지쳐 간다
                var tired = s.Joined.Where(id => id != host.Id).OrderByDescending(id => s.Share.TryGetValue(id, out var sv) ? sv : 0f).ThenBy(id => id).Select(id => w.Crew.First(c => c.Id == id)).FirstOrDefault();
                if (tired != null) { tired.Needs.Rest = 0.02f; }
                bool calm = ScUntil(w, () => !Crisis.Acting(w), 3f, 60);
                bool resumed = ScUntil(w, () => s.Stage == SceneStage.Run, 3f);
                Check("경보 — 영화가 멈췄다가, 남은 사람이 멈춘 데서부터 이어 본다", paused && calm && resumed && s.Progress >= at - 0.001f,
                    $"멈춤 {paused} ({at:P0}) · 복구 {calm} · 다시 {resumed} · 지금 {s.Here.Count}명 · {s.Trail.LastOrDefault(t => t.Contains("다시"))}");
                // 정전: 영사기가 꺼졌다가 전기가 돌아오면 다시 튼다 · 도는 동안 옆방까지 시끄럽다
                var nb = w.Ambience.Neighbors(room).Select(x => x.room).FirstOrDefault();
                float noiseNb = nb?.Noise ?? 0f;
                room.PowerCut = true;
                bool cut = ScUntil(w, () => s.Stage == SceneStage.Paused && s.PauseWhy == "정전", 0.3f);
                float atCut = s.Progress;
                Run(w, SimTime.Minutes(10));
                bool frozen = MathF.Abs(s.Progress - atCut) < 0.001f;
                room.PowerCut = false;
                bool lit = ScUntil(w, () => s.Stage == SceneStage.Run || !s.Open, 0.5f);
                foreach (var id in s.Here.ToList()) if (w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember v) ScFree(w, v); // 남은 사람은 끝까지 볼 만큼 배부르고 기운 있다
                Check("정전 — 영사기가 꺼져 멈췄다가, 전기가 돌아오면 그 자리부터 · 영화 소리는 옆방까지 번진다", !s.Open || cut && frozen && lit && noiseNb > 0.1f,
                    $"정전 멈춤 {cut} · 멈춘 동안 진척 그대로 {frozen} ({atCut:P0}) · 다시 {lit} · 옆방 {nb?.Name} 소음 {noiseNb:0.00} · {s.Trail.LastOrDefault(t => t.Contains("전기"))}");
                bool end = ScUntil(w, () => !s.Open, 3f);
                bool partial = tired == null || !s.Here.Contains(tired.Id) && (s.Share.TryGetValue(tired.Id, out var sh) ? sh : 0f) < 1f && tired.Diary.Any(d => d.text.Contains("결말을 못 봤다"));
                Check("끝 — 끝까지 본 사람만 끝까지 본 만큼 (떠난 사람은 결말을 못 봤다)", end && s.Stage == SceneStage.Done && s.Progress >= 1f && s.Finished >= 1 && partial,
                    $"{s.Stage} · 끊김 {s.Pauses} · 끝까지 {s.Finished}명 · {tired?.Name}: {(tired != null && s.Share.TryGetValue(tired.Id, out var t2) ? t2 : 0f):P0} · {tired?.Diary.LastOrDefault().text} · {string.Join(" / ", s.Trail.TakeLast(4))}");
            }

            // 2-2) 주 컴퓨터: 영화 소리 × 옆방에서 자는 사람 → 볼륨을 낮추거나(등급 · 야간 소음 관리) 연 사람에게 알린다
            {
                var w = DayOne(seed, "Hanbit");
                var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Theater or RoomType.Lounge && !r.OffLimits).OrderBy(r => r.Type == RoomType.Theater ? 0 : 1).ThenBy(r => r.Id).First();
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
                var host = adults[0];
                ScFree(w, host);
                var s = w.Scenes.OpenMovie(host, room)!;
                bool running = ScUntil(w, () => s.Stage == SceneStage.Run && s.Here.Count >= 1, 2f);
                // 옆방 (소리가 넘어가는 방)에 둘을 재운다
                var nb = w.Ambience.Neighbors(room).Select(x => x.room).Where(r => r.Cells.Count(w.Ship.IsWalkable) >= 2).OrderBy(r => r.Type == RoomType.Corridor ? 1 : 0).ThenBy(r => r.Id).First();
                var sleepers = w.Crew.Where(c => !c.Dead && c != host && !s.Invited.Contains(c.Id) && !s.Here.Contains(c.Id)).OrderBy(c => c.Id).Take(2).ToList();
                var beds = nb.Cells.Where(x => w.Ship.IsWalkable(x) && w.Ship.FurnitureAt(x) == null).OrderBy(x => x.Y).ThenBy(x => x.X).ToList();
                for (int k = 0; k < sleepers.Count && k < beds.Count; k++) Stay(w, sleepers[k], beds[k], Pose.Sleeping);
                ComputerAct? act = null;
                bool judged = ScUntil(w, () => (act = w.Automation.Book.Acts.LastOrDefault(a => a.Key == "mvq:" + s.Id)) != null, 0.5f);
                bool reacted = s.Holding || s.Trail.Any(t => t.Contains("못 본 척"));
                Check("주 컴퓨터 — 영화 소리가 옆방에서 자는 사람을 깨울 것 같으면 판단하고 볼륨을 낮추거나 연 사람에게 알린다",
                    running && judged && reacted && w.Scenes.Stats.PcQuiet + w.Scenes.Stats.Hushed + (s.Trail.Any(t => t.Contains("못 본 척")) ? 1 : 0) >= 1,
                    $"{room.Name} → 옆 {nb.Name} {sleepers.Count}명 잠 · 등급 {w.Automation.Level} · [{act?.Observe} | {act?.Judge} | {act?.Act} | {act?.Request}] · 볼륨 낮춤 {s.Holding} · {s.Trail.LastOrDefault()}");
            }

            // 3) 국 엎기: 자국이 남고 · 둘레가 반응하고 · 누군가 도구를 가져와 닦는다
            {
                var w = DayOne(seed, "Mirinae");
                CrewMember? c = null;
                ScUntil(w, () => (c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.IsAwake && x.Room?.Type is RoomType.Mess or RoomType.Galley).OrderBy(x => x.Id).FirstOrDefault()) != null, 24f, 150);
                var s = w.Scenes.OpenSpill(c!)!;
                float wet0 = w.Body.Mark(s.Spot, CellMark.Wet), oil0 = w.Body.Mark(s.Spot, CellMark.Oil);
                float slip0 = w.Body.SlipAt(w.Ship.Grid.Index(s.Spot));
                Run(w, SimTime.Minutes(3));
                bool stays = s.Things.Any(t => t.Kind == ThingKind.Stain);
                if (s.Other < 0 || s.Other == c!.Id)
                {
                    // 곁의 꼼꼼한 사람이 나선다 (없으면 가장 가까운 어른)
                    var helper = w.Crew.Where(x => x != c && !x.Dead && !x.IsChild && x.CanAct && x.IsAwake).OrderBy(x => (x.Position - c!.Position).LengthSquared()).First();
                    ScFree(w, helper);
                    s.Other = helper.Id;
                    helper.Interrupt(w);
                }
                bool cleaned = ScUntil(w, () => !s.Open, 8f);
                var cleaner = w.Crew.FirstOrDefault(x => x.Id == s.Other); // 처음 나선 사람이 못 오면 다른 사람이 닦는다
                bool mem = cleaner != null && (cleaner == c || w.Relations.All.Any(m => m.Who == c!.Id && m.About == cleaner.Id && m.Reason == RelationReason.FixedMyThing));
                Check("국 — 바닥에 자국이 남았다가, 걸레를 가져온 사람이 닦는다 (엎은 사람이 기억한다)", stays && cleaned && s.Stage == SceneStage.Done && !s.Things.Any(t => t.Kind == ThingKind.Stain) && w.Scenes.Stats.Cleaned >= 1 && mem,
                    $"{c!.Name} 엎음 · 본 사람 {s.Seen.Count} · 닦은 사람 {cleaner?.Name} · {s.Stage} · {string.Join(" / ", s.Trail.TakeLast(4))}");
                float oil1 = w.Body.Mark(s.Spot, CellMark.Oil);
                Check("국 × 배 본체 — 엎은 자리가 젖고 기름져 미끄러워졌다가, 닦으면 기름이 걷힌다 (바닥 규칙 · 길찾기가 같은 상태를 읽는다)",
                    wet0 > 0.5f && oil0 > 0.3f && slip0 > 0.25f && oil1 < 0.05f,
                    $"젖음 {wet0:0.00} · 기름 {oil0:0.00} · 미끄럼 {slip0:0.00} → 닦은 뒤 기름 {oil1:0.00} · 젖음 {w.Body.Mark(s.Spot, CellMark.Wet):0.00} · 미끄러진 사람 {s.Slipped.Count}");
            }

            // 4) 한밤의 간식: 냉장고까지 가서 꺼내 먹는다 (식량 재고) · 남의 것이었으면 냉장고 쪽지 → 읽은 사람만 안다
            {
                var w = DayOne(seed, "Mirinae");
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
                var eater = adults[1];
                ScFree(w, eater);
                eater.Needs.Food = 0.6f;
                int meals0 = w.Ship.CountStored(ItemKind.Meal) + w.Ship.CountStored(ItemKind.Produce);
                var s = w.Scenes.OpenSnack(eater, takePlate: false)!;
                s.Victim = adults[0].Id;
                bool ate = ScUntil(w, () => !s.Open, 2f);
                int meals1 = w.Ship.CountStored(ItemKind.Meal) + w.Ship.CountStored(ItemKind.Produce);
                Check("간식 — 냉장고까지 가서 꺼내 먹는다 (식량 재고가 준다)", ate && s.Stage == SceneStage.Done && meals1 < meals0 && eater.Needs.Food > 0.6f,
                    $"{eater.Name} · {s.Stage} · 식량 {meals0} → {meals1} · 배 {eater.Needs.Food:P0}");
                // 주인이 알아채고 쪽지를 붙인다 → 냉장고 앞에 선 사람만 읽는다
                var owner = adults[0];
                ScFree(w, owner);
                ShipNote? note = null;
                ScUntil(w, () => (note = w.Scenes.Notes.FirstOrDefault(n => n.Kind == NoteKind.Fridge)) != null, 14f, 150);
                int readers0 = note?.Readers.Count ?? 0;
                var reader = adults.Where(c => c != owner && c != eater && c.CanAct).OrderBy(c => c.Id).Last();
                ScFree(w, reader);
                // 통합: 새 배는 냉장고가 여럿이다 — 쪽지가 붙은 냉장고가 있는 방에서 출출해진다 (가까운 다른 냉장고로 가면 쪽지를 못 본다)
                if (note != null && note.RoomId >= 0 && note.RoomId < w.Ship.Rooms.Count) Put(w, reader, w.Ship.Rooms[note.RoomId]);
                // 통합: 쪽지 붙은 냉장고에도 끼니가 있다 (끼니가 다른 방 냉장고에만 남아 있으면 그리로 가서 쪽지를 못 본다)
                if (note != null && w.Ship.Furniture.Where(f => f.Storage != null && f.Room.Id == note.RoomId && f.Storage.Accepts(ItemKind.Meal)).OrderBy(f => (f.Cells[0].Center - note.At.Center).LengthSquared()).FirstOrDefault() is Furniture nf) nf.Storage!.Add(ItemKind.Meal, 1);
                reader.Needs.Food = 0.6f;
                ScUntil(w, () => reader.Room != null, 1f, 1); // 문턱을 지나는 중이면 장면을 못 연다 (어느 방에 있어야 일상 장면이 열린다) — 연구 · 배우기가 생긴 뒤로 그 순간 문간에 있기도 하다
                var s2 = w.Scenes.OpenSnack(reader, takePlate: false); // 냉장고 앞에 서면 읽는다
                ScUntil(w, () => s2 == null || !s2.Open, 2f);
                var far = adults.FirstOrDefault(c => note != null && !note.Readers.Contains(c.Id));
                Check("쪽지 — 냉장고 쪽지는 냉장고 앞에 선 사람만 읽는다 (안 간 사람은 모른다)", note != null && note.Readers.Contains(reader.Id) && far != null,
                    $"\"{note?.Text}\" · 읽은 사람 {readers0} → {note?.Readers.Count}명({string.Join(",", note?.Readers.Select(id => w.Crew.First(c => c.Id == id).Name) ?? Array.Empty<string>())}) · 못 읽은 사람 예: {far?.Name} · 냉장고에 보낸 사람 {reader.Name}({reader.Room?.Name}) 간식 {(s2 == null ? "못 엶" : s2.Stage.ToString())} · 쪽지 방 {(note != null && note.RoomId >= 0 && note.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[note.RoomId].Name : "?")}");
            }

            // 4-2) 간식 × 음식: 식탁에 이름표를 붙여 덜어 둔 남의 몫을 밤에 먹어 버린다 → 주인은 늦은 끼니를 못 찾고 · 쪽지를 붙인다
            {
                var w = DayOne(seed, "Mirinae");
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Id).ToList();
                var owner = adults[0];
                var eater = adults.Skip(1).OrderBy(c => c.AffinityTo(owner)).ThenBy(c => c.Id).First();
                var table = w.Ship.RoomsOf(RoomType.Mess).SelectMany(r => r.Furniture).FirstOrDefault(f => f.Type == FurnitureType.Table)
                            ?? w.Ship.Furniture.First(f => f.Type == FurnitureType.Table && !f.Room.OffLimits);
                var plate = new Plate { Id = 90001, Recipe = 0, Cook = adults[^1].Id, Quality = 0.7f, For = owner.Id, By = adults[^1].Id, SetAt = w.Tick, Temp = 60f, Table = table };
                w.Cooking.Plates.Add(plate);
                ScFree(w, eater); ScFree(w, owner);
                eater.Needs.Food = 0.3f;
                bool hadPlate = w.Cooking.PlateFor(owner) == plate;
                var s = w.Scenes.OpenSnack(eater, takePlate: true)!;
                bool ate = ScUntil(w, () => !s.Open, 2f);
                bool gone = plate.Eaten && w.Cooking.PlateFor(owner) == null;
                bool owned = ScUntil(w, () => owner.Diary.Any(d => d.text.Contains("이름표 붙여 둔 내 몫")), 14f, 150);
                var note = w.Scenes.Notes.LastOrDefault(n => n.Kind == NoteKind.Fridge && n.Author == owner.Id);
                Check("간식 × 음식 — 이름표 붙은 남의 접시를 먹으면 주인은 제 몫을 못 찾고, 알아채고 쪽지를 붙인다 (먹은 사람은 안다)",
                    hadPlate && ate && s.Plate == plate.Id && gone && owned && note != null && eater.Diary.Any(d => d.text.Contains("이름표")),
                    $"{eater.Name} → {owner.Name} 몫 {plate.Spec.Name} · 먹음 {plate.Eaten} · 주인 앎 {owned} · 쪽지 \"{note?.Text}\" · {s.Trail.LastOrDefault()}");
            }

            // 5) 몽유병: 잠결에 복도로 → 당직이 발견 → 침대로 (본인은 아침에 들어서 안다)
            //    경보가 울려 혼자 깨면 다음 밤에 다시 (경보는 장면을 끊는다 — 그건 규칙대로다)
            (World w, CrewMember c, DailyScene s, bool walked, bool knewBefore, bool home, int heard0)? Walk(bool computer)
            {
                var w = DayOne(seed, "Hanbit");
                for (int night = 0; night < 3; night++)
                {
                    CrewMember? c = null;
                    ScUntil(w, () => SimTime.HourOfDay(w.Tick) is >= 1f and < 3.5f && !Crisis.Acting(w)
                        && (c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.Pose == Pose.Sleeping && x.Bed != null && !w.Society.OnNightWatch(x) && !w.Scenes.Busy(x)).OrderBy(x => x.Id).FirstOrDefault()) != null, 30f, 150);
                    if (c == null) return null;
                    c.Needs.Stress = 0.7f;
                    int heard0 = c.Diary.Count(d => d.text.Contains("걸어 나왔단다")); // 전에 들은 몽유병 이야기 (이번 일과 따로 센다)
                    var s = w.Scenes.OpenSleepwalk(c)!;
                    if (computer)
                    {
                        // 깨어 있는 사람은 당직 하나만 — 복도에서 먼 방에 둔다 (지나가다 볼 수 없게: 컴퓨터가 불러야 안다)
                        var awake = w.Crew.Where(x => !x.Dead && x != c && x.IsAwake && x.CanAct).OrderBy(x => x.Id).ToList();
                        var watch = awake.Where(x => !x.IsChild).OrderByDescending(x => w.Society.OnNightWatch(x)).ThenBy(x => x.Id).FirstOrDefault()
                                    ?? w.Crew.Where(x => !x.Dead && x != c && !x.IsChild && x.CanAct).OrderBy(x => x.Id).First();
                        foreach (var x in awake) if (x != watch) Stay(w, x, x.Cell, Pose.Sleeping);
                        float h = SimTime.HourOfDay(w.Tick);
                        if (!w.Society.OnNightWatch(watch)) watch.Schedule = new Schedule { SleepStart = SimTime.Wrap(h + 6f), SleepLength = 8f, WorkStart = SimTime.Wrap(h - 1f), WorkLength = 8f };
                        var far = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.OffLimits).SelectMany(r => r.Cells)
                            .Where(x => w.Ship.IsWalkable(x) && w.Ship.FurnitureAt(x) == null).OrderByDescending(x => Math.Abs(x.X - s.Spot.X) + Math.Abs(x.Y - s.Spot.Y)).ThenBy(x => x.Y).ThenBy(x => x.X).First();
                        Stay(w, watch, far, Pose.Working);
                        watch.NextThinkTick = w.Tick + SimTime.Hours(1);
                    }
                    bool walked = ScUntil(w, () => c.Cell == s.Spot || !s.Open, 1f);
                    bool knewBefore = c.Diary.Count(d => d.text.Contains("걸어 나왔단다")) > heard0;
                    long dbgAt = 0;
                    bool home = ScUntil(w, () =>
                    {
                        if (Environment.GetEnvironmentVariable("SC_DEBUG") == "1" && w.Tick >= dbgAt && s.Other >= 0 && w.Crew.FirstOrDefault(x => x.Id == s.Other) is CrewMember ed)
                        {
                            dbgAt = w.Tick + SimTime.Minutes(5);
                            Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] {ed.Name} {ed.Job?.Label} {ed.Pose} 거리 {(ed.Position - c.Position).Length():0.0} 잡음 {s.Holding} · " + string.Join(", ", ed.LastEvaluations.OrderByDescending(e => e.Score).Take(4).Select(e => $"{e.Activity.Id}:{e.Score:0.00}")));
                        }
                        return !s.Open;
                    }, 2.5f);
                    if (s.Trail.Any(t => t.Contains("경보"))) { Run(w, SimTime.Hours(12)); continue; }
                    return (w, c, s, walked, knewBefore, home, heard0);
                }
                return null;
            }
            {
                var r = Walk(false);
                if (r is not { } rv) { Check("몽유병 — 장면이 열린다", false, "잠든 사람이 없거나 사흘 밤 내내 경보"); }
                else
                {
                    var (w, c, s, walked, knewBefore, home, heard0) = rv;
                    var esc = w.Crew.FirstOrDefault(x => x.Id == s.Other);
                    var bed = c.Bed ?? c.HomeBed;
                    bool atBed = bed != null && (c.Position - bed.Center).LengthSquared() < 9f;
                    Check("몽유병 — 잠결에 복도로 걸어 나오고, 깨어 있던 사람(당직)이 찾아 침대로 데려간다", walked && home && esc != null && atBed && w.Scenes.Stats.Escorts >= 1 && !knewBefore,
                        $"{c.Name} · 복도 {walked} · 찾은 사람 {esc?.Name}({(esc != null && w.Society.OnNightWatch(esc) ? "야간 당직" : "깨어 있던 사람")}) · 침대 곁 {atBed} · {string.Join(" / ", s.Trail.TakeLast(3))}");
                    c.Needs.Stress = 0.2f; // 다음 밤에 또 걷지 않게 (이번 일만 센다)
                    ScUntil(w, () => s.Told, 20f, 300);
                    bool knows = c.Diary.Count(d => d.text.Contains("걸어 나왔단다")) > heard0;
                    Check("세계 ≠ 아는 것 — 본인은 데려다준 사람이 말해 줘야 안다", !s.Told && !knows || s.Told && knows,
                        s.Told ? $"{esc?.Name}에게 들었다: {c.Diary.LastOrDefault(d => d.text.Contains("걸어")).text}" : "아직 못 들었다 (모른다)");
                }
            }
            {
                // 5-2) 주 컴퓨터: 새벽 복도의 움직임 → 잠결 걸음으로 판단 → 멀리 있던 당직을 부른다 → 당직이 와서 침대로
                var r = Walk(true);
                if (r is not { } rv) { Check("주 컴퓨터 — 몽유병 장면이 열린다", false, "잠든 사람이 없거나 사흘 밤 내내 경보"); }
                else
                {
                    var (w, c, s, walked, _, home, _) = rv;
                    var act = w.Automation.Book.Acts.FirstOrDefault(a => a.Key == "sw:" + s.Id);
                    var esc = w.Crew.FirstOrDefault(x => x.Id == s.Other);
                    Run(w, SimTime.Hours(2));
                    Check("주 컴퓨터 — 새벽 복도에 멈춰 선 사람을 잠결 걸음으로 보고 당직을 불러, 당직이 와서 침대로 데려간다",
                        walked && home && act != null && esc != null && s.Holding && s.Stage == SceneStage.Done && w.Scenes.Stats.PcPages >= 1,
                        $"{c.Name} · [{act?.Observe} | {act?.Judge} | {act?.Act} | {act?.Request}] → 결과 {act?.Result} ({act?.Score}) · 온 사람 {esc?.Name} · {string.Join(" / ", s.Trail.TakeLast(3))}");
                }
            }

            // 6) 교대 인수인계: 메모를 남기면 다음 근무자가 확인하러 가고 / 빠뜨리면 모르고 지나간다
            {
                (World w, CrewMember a, CrewMember b, Machine m, int t) Setup(bool omit)
                {
                    var w = DayOne(seed, "Hanbit");
                    w.PreventionBlind = true; // 감지기 · 순찰이 먼저 찾아 고쳐 버리지 않게 (사람의 인수인계만 본다)
                    var m = w.Ship.Machines.Where(x => x.Body.Type is FurnitureType.CoolantPump or FurnitureType.WaterRecycler && x.Faults.Count == 0 && !x.Body.Stowed)
                        .OrderBy(x => x.Body.Type == FurnitureType.CoolantPump ? 0 : 1).ThenBy(x => x.Body.Id).First();
                    var fault = m.Spec.FaultKinds.First(k => k != FaultKind.BreakerTrip && Prevention.KindOf(k) != null);
                    var kind = Prevention.KindOf(fault)!.Value;
                    m.Omen = new Omen { Kind = kind, Fault = fault, Cause = Causes.Weighted(Causes.For(m.Body.Type, kind), 0.3f), Since = w.Tick, Due = w.Tick + SimTime.Hours(40) };
                    var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).ToList();
                    var a = adults.OrderBy(c => c.Traits.Diligence).ThenBy(c => c.Id).First();
                    var b = adults.Where(c => c != a).OrderByDescending(c => c.Traits.Diligence).ThenBy(c => c.Id).First();
                    int t = ((int)SimTime.HourOfDay(w.Tick) + 2) % 24;
                    a.Schedule = new Schedule { SleepStart = SimTime.Wrap(t + 4f), SleepLength = 8f, WorkStart = SimTime.Wrap(t - 8f), WorkLength = 8f };
                    b.Schedule = new Schedule { SleepStart = SimTime.Wrap(t - 9f), SleepLength = 8f, WorkStart = t, WorkLength = 8f };
                    foreach (var o in adults)
                        if (o != a && o != b && SimTime.InWindow(SimTime.Wrap(t + 1f), o.Schedule.WorkStart, 2f))
                            o.Schedule = new Schedule { SleepStart = o.Schedule.SleepStart, SleepLength = o.Schedule.SleepLength, WorkStart = SimTime.Wrap(o.Schedule.WorkStart + 3f), WorkLength = o.Schedule.WorkLength };
                    w.Scenes.ForceOmit = omit;
                    // 교대 15분 전: 근무하던 사람이 펌프 소리가 이상한 걸 듣는다 (아직 정식으로 알리진 않았다)
                    ScUntil(w, () => SimTime.HourOfDay(w.Tick) >= SimTime.Wrap(t - 0.25f) && SimTime.HourOfDay(w.Tick) < t, 3f, 15);
                    w.Scenes.Notice(a, m, $"{m.Name} 소리가 이상하다");
                    return (w, a, b, m, t);
                }

                {
                    var (w, a, b, m, t) = Setup(false); long t0 = w.Tick;
                    bool handed = ScUntil(w, () => w.Scenes.Handoffs.Any(h => h.From == a.Id && h.Tick >= t0), 1f);
                    var h = w.Scenes.Handoffs.FirstOrDefault(x => x.From == a.Id && x.Tick >= t0);
                    var memo = w.Scenes.Notes.FirstOrDefault(n => n.Kind == NoteKind.Memo && n.Author == a.Id && n.Written >= t0);
                    bool checkedIt = ScUntil(w, () => w.Scenes.Concerns.Any(k => k.Who == b.Id && k.Machine == m && k.Checked), 5f);
                    var kb = w.Scenes.Concerns.FirstOrDefault(k => k.Who == b.Id && k.Machine == m);
                    Check("인수인계 — 근무가 끝나며 \"펌프 소리 이상\"을 넘기고(메모 · 말), 다음 근무자가 확인하러 가서 찾는다",
                        handed && h!.To == b.Id && h.Told.Count >= 1 && (memo != null || h.Verbal) && checkedIt && kb?.Result == "정말 이상했다" && (m.Omen == null || m.Omen.Known),
                        $"{a.Name} → {w.Crew.FirstOrDefault(c => c.Id == h?.To)?.Name}: {string.Join(" · ", h?.Told ?? new())} ({(h?.Verbal == true ? "말" : "")}{(memo != null ? " 메모" : "")}) · {b.Name}: {kb?.How} → {kb?.Result}");
                    Check("쪽지 — 받을 사람이 정해진 메모는 그 사람만 읽는다", memo == null || memo.Readers.All(id => id == a.Id || id == b.Id),
                        memo == null ? "말로만 넘김" : $"읽은 사람 {string.Join(",", memo.Readers.Select(id => w.Crew.First(c => c.Id == id).Name))}");
                }
                {
                    // 불 — 아직 못 읽은 인수인계 메모가 타면 다음 근무자는 모른다
                    var (w, a, b, m, t) = Setup(false); long t0 = w.Tick;
                    w.Scenes.ForceOmit = false;
                    ScUntil(w, () => w.Scenes.Handoffs.Any(h => h.From == a.Id && h.Tick >= t0), 1f, 5);
                    var h = w.Scenes.Handoffs.FirstOrDefault(x => x.From == a.Id && x.Tick >= t0);
                    var memo = w.Scenes.Notes.FirstOrDefault(n => n.Kind == NoteKind.Memo && n.Author == a.Id && n.Written >= t0);
                    if (memo != null && !h!.Verbal && !memo.Readers.Contains(b.Id))
                    {
                        w.Fire.Ignite(memo.At, 0.9f);
                        ScUntil(w, () => memo.Gone || memo.Readers.Contains(b.Id), 0.3f, 5);
                        bool burnt = memo.Gone && !memo.Readers.Contains(b.Id);
                        ScUntil(w, () => !Crisis.Acting(w), 4f, 60);
                        Run(w, SimTime.Hours(2));
                        bool bKnows = w.Scenes.Concerns.Any(k => k.Who == b.Id && k.Machine == m);
                        Check("불 — 아직 못 읽은 인수인계 메모가 타면, 다음 근무자는 끝내 모른다", burnt && !bKnows && w.Scenes.Stats.Burned >= 1,
                            $"메모 탐 {burnt} · {b.Name} 앎 {bKnows} · {w.Log.Entries.Where(e => e.Text.Contains("탔다")).Select(e => e.Text).LastOrDefault()}");
                    }
                    else Check("불 — 메모가 타는 장면 (이번엔 말로 넘겨 건너뜀)", true, h?.Verbal == true ? "말로 넘김" : "메모 없음");
                }
                {
                    // 주 컴퓨터: 받을 사람이 근무를 시작하고도 메모를 안 열면 단말로 알린다 → 게시판으로 가서 읽고 확인하러 간다
                    var (w, a, b, m, t) = Setup(false); long t0 = w.Tick;
                    var board = w.Scenes.DutyBoard();
                    var far = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.OffLimits && r.Id != board?.room.Id).SelectMany(r => r.Cells)
                        .Where(x => w.Ship.IsWalkable(x) && w.Ship.FurnitureAt(x) == null).OrderByDescending(x => board == null ? 0 : Math.Abs(x.X - board.Value.at.X) + Math.Abs(x.Y - board.Value.at.Y)).ThenBy(x => x.Y).ThenBy(x => x.X).First();
                    Stay(w, b, far, Pose.Working); // 근무 시작 무렵 먼 곳에서 일에 붙들려 있다
                    b.NextThinkTick = w.Tick + SimTime.Minutes(70);
                    ScUntil(w, () => w.Scenes.Handoffs.Any(h => h.From == a.Id && h.Tick >= t0), 1f, 5);
                    var memo = w.Scenes.Notes.FirstOrDefault(n => n.Kind == NoteKind.Memo && n.Author == a.Id && n.Written >= t0);
                    ComputerAct? act = null;
                    bool pinged = memo != null && ScUntil(w, () => (act = w.Automation.Book.Acts.FirstOrDefault(x => x.Key == "memo:" + memo.Id)) != null, 2f);
                    bool read = memo != null && ScUntil(w, () => memo.Readers.Contains(b.Id), 3f);
                    Run(w, SimTime.Minutes(30));
                    Check("주 컴퓨터 — 받을 사람이 근무를 시작하고도 인수인계 메모를 안 열면 단말로 알리고, 그 사람이 게시판에 가서 읽는다",
                        memo != null && pinged && memo.Pinged && read && w.Scenes.Stats.PcMemo >= 1,
                        memo == null ? "메모 없이 말로 넘김" : $"[{act?.Observe} | {act?.Act} | {act?.Request}] → {act?.Result} · {b.Name} 읽음 {read} · {w.Automation.Apps.Messages.LastOrDefault(x => x.CrewId == b.Id)?.Text}");
                }
                {
                    var (w, a, b, m, t) = Setup(true); long t0 = w.Tick;
                    ScUntil(w, () => w.Scenes.Handoffs.Any(h => h.From == a.Id && h.Tick >= t0), 1f);
                    var h = w.Scenes.Handoffs.FirstOrDefault(x => x.From == a.Id && x.Tick >= t0);
                    Run(w, SimTime.Hours(5));
                    bool bKnows = w.Scenes.Concerns.Any(k => k.Who == b.Id && k.Machine == m);
                    Check("빠뜨리면 — 다음 근무자는 모르고, 확인하러 가지 않는다", h != null && h.Dropped.Count >= 1 && h.Told.Count == 0 && !bKnows && !w.Scenes.Concerns.Any(k => k.Who == b.Id && k.Machine == m && k.Checked),
                        $"{a.Name}: 빠뜨림 {string.Join(" · ", h?.Dropped ?? new())} ({h?.Why}) · {b.Name} 앎 {bKnows} · 확인 {w.Scenes.Stats.Checks}");
                }
            }

            // 7) 쪽지 · 게시판: 돌려 가며 서명하는 생일 카드 — 주인공은 받을 때까지 모른다 · 당번표 낙서는 이름 없이
            {
                var w = DayOne(seed, "Mirinae");
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
                var who = adults[0];
                var org = adults[1];
                var card = w.Scenes.Write(NoteKind.Card, org, $"{who.Name} 생일 축하해! — 한마디씩", who.Id)!;
                var doodle = w.Scenes.Write(NoteKind.Roster, adults[2], $"{adults[3].Name} 차례: 영원히 미정", adults[3].Id, anonymous: true)!;
                Run(w, SimTime.Hours(14));
                bool secret = !card.Readers.Contains(who.Id);
                int signed = card.Signers.Count;
                var board = w.Scenes.RosterBoard();
                card.Given = true; card.At = who.Cell; card.RoomId = who.Room!.Id;
                w.Scenes.Read(card, who);
                bool joy = who.Diary.Any(d => d.text.Contains("생일 카드"));
                Check("생일 카드 — 게시판을 지나간 사람들이 몰래 서명하고, 받은 날에야 주인공이 안다", secret && joy && card.Readers.Contains(who.Id),
                    $"{board?.room.Name} 게시판 · 서명 {signed}명({string.Join(",", card.Signers.Select(id => w.Crew.First(c => c.Id == id).Name))}) · 읽은 사람 {card.Readers.Count} · {who.Diary.LastOrDefault().text}");
                var target = adults[3];
                var guess = w.Log.Entries.Where(e => e.Text.Contains("당번표 낙서를 보고")).Select(e => e.Text).LastOrDefault();
                Check("당번표 낙서 — 지나간 사람이 읽고 반응하고, 놀림 받은 사람은 쓴 사람을 모른 채 의심한다 (틀릴 수 있다)", doodle.Readers.Contains(target.Id) && guess != null,
                    $"\"{doodle.Text}\" · 읽은 사람 {doodle.Readers.Count}/{adults.Count} · 웃음 {w.Scenes.Stats.Laughs} · 짜증 {w.Scenes.Stats.Annoyed} · {guess}");
            }

            // 8) 소품 제작: 재료를 쓰고 · 작업대에서 진척을 쌓고 · 들고 가서 · 설치한다
            {
                var w = DayOne(seed, "Mirinae");
                var spec = Props.All.First(p => p.Source == PropSource.Craft && p.Material is ItemKind m && w.Ship.CountStored(m) > 0 && !p.Kid);
                var maker = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Id).Skip(2).First();
                ScFree(w, maker);
                int mat0 = w.Ship.CountStored(spec.Material!.Value);
                int made0 = w.Props.Stats.Made;
                bool opened = w.Scenes.Craft(maker, spec);
                var s = w.Scenes.Scenes.Last();
                float midway = 0f; bool carried = false;
                ScUntil(w, () => { if (s.Progress is > 0.2f and < 0.9f) midway = s.Progress; if (s.Holding) carried = true; return !s.Open; }, 12f);
                var placed = w.Props.Placed.LastOrDefault(p => p.Spec == spec && p.Maker == maker.Id);
                Check("소품 — 재료를 쓰고 · 진척을 쌓고 · 들고 가서 설치한다", opened && s.Stage == SceneStage.Done && midway > 0f && carried && placed != null && w.Props.Stats.Made > made0 && w.Ship.CountStored(spec.Material.Value) < mat0,
                    $"{maker.Name}의 {spec.Name} · 중간 {midway:P0} · 들고 감 {carried} · {(placed != null ? $"{w.Ship.Rooms[placed.RoomId].Name}에 놓임" : "없음")} · 재료 {mat0} → {w.Ship.CountStored(spec.Material.Value)}");
            }

            // 8-2) 소품 × 정전 · 이동식 등: 캄캄한 작업대에선 손을 놓고 기다리다, 불(또는 작업등)이 들어오면 이어 만든다
            {
                var w = DayOne(seed, "Mirinae");
                var spec = Props.All.First(p => p.Source == PropSource.Craft && p.Material is ItemKind m && w.Ship.CountStored(m) > 0 && !p.Kid);
                // 통합7 넷째 사람이 이미 제 소품(노래 목록판)을 만드는 중이면 새 판이 안 열리고 Last()가 남의 옛 장면을 집었다 — 손이 빈 사람이 연다
                CrewMember? maker = null;
                foreach (var cand in w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Id).Skip(3))
                {
                    ScFree(w, cand);
                    if (w.Scenes.Craft(cand, spec)) { maker = cand; break; }
                }
                var s = w.Scenes.Scenes.Last();
                bool going = maker != null && ScUntil(w, () => s.Stage == SceneStage.Run && s.Progress > 0.1f, 4f);
                var bench = w.Ship.Rooms[s.RoomId];
                bench.PowerCut = true;
                bool dark = ScUntil(w, () => s.Stage == SceneStage.Paused && s.PauseWhy == "어두움", 0.5f);
                float at = s.Progress;
                Run(w, SimTime.Minutes(10));
                bool held = bench.PortableLit > 0 || MathF.Abs(s.Progress - at) < 0.001f;
                bench.PowerCut = false;
                bool back = ScUntil(w, () => s.Stage == SceneStage.Run && s.Progress > at + 0.01f || !s.Open, 3f);
                Check("소품 × 정전 — 캄캄한 작업대에선 손을 놓았다가(진척 그대로), 불이 들어오면 이어 만든다",
                    going && dark && held && back && w.Scenes.Stats.Dark >= 1,
                    $"{bench.Name} · 멈춤 {dark} ({at:P0}) · 그대로 {held} · 작업등 {bench.PortableLit} · 다시 {back} ({s.Progress:P0}) · {s.Trail.LastOrDefault(t => t.Contains("다시"))}");
            }

            // 9) 저절로: 며칠 지내면 장면이 열리고 · 끝나고 · 끊긴 것이 이어진다
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.TicksPerDay * 2);
                var st = w.Scenes.Stats;
                Check("저절로 — 이틀 동안 일상이 장면으로 열리고 끝난다", st.Opened >= 4 && st.Done >= 2, st.Summary());
            }

            // 10) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 장면이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 일상 장면 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
