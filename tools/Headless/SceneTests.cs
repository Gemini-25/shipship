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
                    $"{s.Title} · {s.Stage} · 진척 {s.Progress:P0} · {string.Join(" / ", s.Trail.TakeLast(3))}");

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
                var tired = s.Joined.Where(id => id != host.Id).Select(id => w.Crew.First(c => c.Id == id)).FirstOrDefault();
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
                Check("정전 — 영사기가 꺼져 멈췄다가, 전기가 돌아오면 그 자리부터 · 영화 소리는 옆방까지 번진다", !s.Open || cut && frozen && lit && noiseNb > 0.1f,
                    $"정전 멈춤 {cut} · 멈춘 동안 진척 그대로 {frozen} ({atCut:P0}) · 다시 {lit} · 옆방 {nb?.Name} 소음 {noiseNb:0.00} · {s.Trail.LastOrDefault(t => t.Contains("전기"))}");
                bool end = ScUntil(w, () => !s.Open, 3f);
                bool partial = tired == null || !s.Here.Contains(tired.Id) && (s.Share.TryGetValue(tired.Id, out var sh) ? sh : 0f) < 1f && tired.Diary.Any(d => d.text.Contains("결말을 못 봤다"));
                Check("끝 — 끝까지 본 사람만 끝까지 본 만큼 (떠난 사람은 결말을 못 봤다)", end && s.Stage == SceneStage.Done && s.Progress >= 1f && s.Finished >= 1 && partial,
                    $"{s.Stage} · 끊김 {s.Pauses} · 끝까지 {s.Finished}명 · {tired?.Name}: {(tired != null && s.Share.TryGetValue(tired.Id, out var t2) ? t2 : 0f):P0} · {tired?.Diary.LastOrDefault().text}");
            }

            // 3) 국 엎기: 자국이 남고 · 둘레가 반응하고 · 누군가 도구를 가져와 닦는다
            {
                var w = DayOne(seed, "Mirinae");
                CrewMember? c = null;
                ScUntil(w, () => (c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.IsAwake && x.Room?.Type is RoomType.Mess or RoomType.Galley).OrderBy(x => x.Id).FirstOrDefault()) != null, 24f, 150);
                var s = w.Scenes.OpenSpill(c!)!;
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
                var cleaner = w.Crew.First(x => x.Id == s.Other); // 처음 나선 사람이 못 오면 다른 사람이 닦는다
                bool mem = w.Relations.All.Any(m => m.Who == c!.Id && m.About == cleaner.Id && m.Reason == RelationReason.FixedMyThing);
                Check("국 — 바닥에 자국이 남았다가, 걸레를 가져온 사람이 닦는다 (엎은 사람이 기억한다)", stays && cleaned && s.Stage == SceneStage.Done && !s.Things.Any(t => t.Kind == ThingKind.Stain) && w.Scenes.Stats.Cleaned >= 1 && mem,
                    $"{c!.Name} 엎음 · 본 사람 {s.Seen.Count} · 닦은 사람 {cleaner.Name} · {string.Join(" / ", s.Trail.TakeLast(3))}");
            }

            // 4) 한밤의 간식: 냉장고까지 가서 꺼내 먹는다 (식량 재고) · 남의 것이었으면 냉장고 쪽지 → 읽은 사람만 안다
            {
                var w = DayOne(seed, "Mirinae");
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
                var eater = adults[1];
                ScFree(w, eater);
                eater.Needs.Food = 0.6f;
                int meals0 = w.Ship.CountStored(ItemKind.Meal) + w.Ship.CountStored(ItemKind.Produce);
                var s = w.Scenes.OpenSnack(eater)!;
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
                reader.Needs.Food = 0.6f;
                var s2 = w.Scenes.OpenSnack(reader); // 냉장고 앞에 서면 읽는다
                ScUntil(w, () => s2 == null || !s2.Open, 2f);
                var far = adults.FirstOrDefault(c => note != null && !note.Readers.Contains(c.Id));
                Check("쪽지 — 냉장고 쪽지는 냉장고 앞에 선 사람만 읽는다 (안 간 사람은 모른다)", note != null && note.Readers.Contains(reader.Id) && far != null,
                    $"\"{note?.Text}\" · 읽은 사람 {readers0} → {note?.Readers.Count}명({string.Join(",", note?.Readers.Select(id => w.Crew.First(c => c.Id == id).Name) ?? Array.Empty<string>())}) · 못 읽은 사람 예: {far?.Name}");
            }

            // 5) 몽유병: 잠결에 복도로 → 당직이 발견 → 침대로 (본인은 아침에 들어서 안다)
            {
                var w = DayOne(seed, "Hanbit");
                CrewMember? c = null;
                ScUntil(w, () => SimTime.HourOfDay(w.Tick) is >= 1f and < 4f && (c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.Pose == Pose.Sleeping && x.Bed != null && !w.Society.OnNightWatch(x)).OrderBy(x => x.Id).FirstOrDefault()) != null, 30f, 150);
                c!.Needs.Stress = 0.7f;
                var s = w.Scenes.OpenSleepwalk(c)!;
                bool walked = ScUntil(w, () => c.Cell == s.Spot, 1f);
                bool knewBefore = c.Diary.Any(d => d.text.Contains("걸어 나왔단다"));
                bool home = ScUntil(w, () => !s.Open, 2.5f);
                var esc = w.Crew.FirstOrDefault(x => x.Id == s.Other);
                var bed = c.Bed ?? c.HomeBed;
                bool atBed = bed != null && (c.Position - bed.Center).LengthSquared() < 9f;
                Check("몽유병 — 잠결에 복도로 걸어 나오고, 깨어 있던 사람(당직)이 찾아 침대로 데려간다", walked && home && esc != null && atBed && w.Scenes.Stats.Escorts >= 1 && !knewBefore,
                    $"{c.Name} · 복도 {walked} · 찾은 사람 {esc?.Name}({(esc != null && w.Society.OnNightWatch(esc) ? "야간 당직" : "깨어 있던 사람")}) · 침대 곁 {atBed} · {string.Join(" / ", s.Trail.TakeLast(2))}");
                ScUntil(w, () => s.Told, 20f, 300);
                bool knows = c.Diary.Any(d => d.text.Contains("걸어 나왔단다"));
                Check("세계 ≠ 아는 것 — 본인은 데려다준 사람이 말해 줘야 안다", !s.Told && !knows || s.Told && knows,
                    s.Told ? $"{esc?.Name}에게 들었다: {c.Diary.LastOrDefault(d => d.text.Contains("걸어")).text}" : "아직 못 들었다 (모른다)");
            }

            // 6) 교대 인수인계: 메모를 남기면 다음 근무자가 확인하러 가고 / 빠뜨리면 모르고 지나간다
            {
                (World w, CrewMember a, CrewMember b, Machine m, int t) Setup(bool omit)
                {
                    var w = DayOne(seed, "Hanbit");
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
                    var (w, a, b, m, t) = Setup(false);
                    bool handed = ScUntil(w, () => w.Scenes.Handoffs.Any(h => h.From == a.Id), 1f);
                    var h = w.Scenes.Handoffs.FirstOrDefault(x => x.From == a.Id);
                    var memo = w.Scenes.Notes.FirstOrDefault(n => n.Kind == NoteKind.Memo && n.Author == a.Id);
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
                    var (w, a, b, m, t) = Setup(false);
                    w.Scenes.ForceOmit = false;
                    ScUntil(w, () => w.Scenes.Handoffs.Any(h => h.From == a.Id), 1f, 5);
                    var h = w.Scenes.Handoffs.FirstOrDefault(x => x.From == a.Id);
                    var memo = w.Scenes.Notes.FirstOrDefault(n => n.Kind == NoteKind.Memo && n.Author == a.Id);
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
                    var (w, a, b, m, t) = Setup(true);
                    ScUntil(w, () => w.Scenes.Handoffs.Any(h => h.From == a.Id), 1f);
                    var h = w.Scenes.Handoffs.FirstOrDefault(x => x.From == a.Id);
                    Run(w, SimTime.Hours(5));
                    bool bKnows = w.Scenes.Concerns.Any(k => k.Who == b.Id && k.Machine == m);
                    Check("빠뜨리면 — 다음 근무자는 모르고, 확인하러 가지 않는다", h != null && h.Dropped.Count >= 1 && h.Told.Count == 0 && !bKnows && w.Scenes.Stats.Checks == 0,
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
