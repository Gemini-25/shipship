using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15.8 소품·장식 70: 방이 꾸며지고 · 쉼/잠이 나아지고 · 좋아하는 소품 곁에서 마음이 놓이고 · 겪은 일(불 · 추모 · 관행)이 소품으로 남는다
public static partial class Program
{
    private static int RunPropTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"소품·장식 70 점검 (v15.8) · 시드 {seed}\n");
        try
        {
            var all = Props.All;
            int ids = all.Select(p => p.Id).Distinct().Count();
            bool effect = all.All(p => p.Relax > 0f || p.Sleep > 0f || p.Hobby != null || p.Habit != null);
            bool priced = all.Where(p => p.Source == PropSource.Port).All(p => p.Cost > 0f);
            bool craft = all.Where(p => p.Source == PropSource.Craft).All(p => p.Maker != null || p.Kid || p.Material != null || p.Habit != null);
            Check("목록 — 소품 70 · 이름이 겹치지 않고 · 모두 효과가 있다 · 기항지 물건은 값이 있다", all.Length == 70 && ids == 70 && effect && priced && craft,
                $"{all.Length}종 · " + string.Join(" · ", Enum.GetValues<PropSource>().Select(s => $"{Props.SourceName(s)} {all.Count(p => p.Source == s)}")) + $" · 예: {Props.Describe(all[4])}");

            // 1) 며칠 지나면 승무원이 소품을 만들어 두고 기록된다 (방마다 몇 개까지)
            {
                var w = DayOne(seed, "Mirinae");
                Run(w, SimTime.TicksPerDay * 3);
                var made = w.Log.Entries.Where(e => e.Text.Contains("만들어") && e.Text.Contains("두었다")).Select(e => e.Text).ToList();
                bool diary = w.Crew.Any(c => c.Diary.Any(d => d.text.Contains("두었다")));
                var over = w.Ship.Rooms.Where(r => r.Decor.Count(p => p.Spec.Source != PropSource.Event) > Props.Cap(r)).ToList();
                Check("며칠 — 승무원이 소품을 만들어 두고 기록·일기에 남는다 · 방마다 몇 개까지", w.Props.Stats.Start > 0 && w.Props.Stats.Made >= 1 && made.Count >= 1 && diary && over.Count == 0,
                    $"{w.Props.Stats.Summary()} · 놓인 것 {w.Props.Placed.Count} · {made.FirstOrDefault()}" + (over.Count > 0 ? $" · 넘친 방 {string.Join(",", over.Select(r => r.Name))}" : ""));
            }

            // 2) 소품이 있는 방: 침실은 잠이, 휴게실은 쉼이 낫다
            {
                var w = DayOne(seed, "Mirinae");
                var bed = w.Ship.LiveRooms.Where(r => r.Type == RoomType.Quarters).OrderBy(r => r.Id).First();
                var rest = w.Ship.LiveRooms.Where(r => r.Type == RoomType.Lounge).OrderBy(r => r.Id).FirstOrDefault()
                           ?? w.Ship.LiveRooms.Where(r => Props.Fits(PropPlace.Rest, r)).OrderBy(r => r.Id).First();
                var keepB = bed.Decor.ToList();
                var keepR = rest.Decor.ToList();
                bed.Decor.Clear(); rest.Decor.Clear();
                float s0 = AmbienceSystem.SleepFactor(bed), r0 = AmbienceSystem.RelaxFactor(rest);
                bed.Decor.AddRange(keepB); rest.Decor.AddRange(keepR);
                w.Props.Place("nightlight", bed, "시험");
                w.Props.Place("aquarium", rest, "시험");
                float s1 = AmbienceSystem.SleepFactor(bed), r1 = AmbienceSystem.RelaxFactor(rest);
                Check("효과 — 소품을 둔 침실은 잠의 질이, 휴게실은 쉬는 효과가 오른다", s1 > s0 && r1 > r0,
                    $"{bed.Name} 잠 {s0 * 100:0.0}% → {s1 * 100:0.0}% ({string.Join("·", bed.Decor.Select(p => p.Name))}) · {rest.Name} 쉼 ×{r0:0.000} → ×{r1:0.000} ({string.Join("·", rest.Decor.Select(p => p.Name))})");
            }

            // 3) 좋아하는 소품 곁에서는 마음이 더 놓인다 (쉼·잠 효과가 없는 소품으로 견준다)
            {
                CrewMember Pick(World w) => w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Outside && c.Room != null && c.IsAwake).OrderBy(c => c.Id).First();
                var a = DayOne(seed, "Mirinae");
                var b = DayOne(seed, "Mirinae");
                var ca = Pick(a); var cb = Pick(b);
                foreach (var (w, c) in new[] { (a, ca), (b, cb) })
                {
                    if (!c.Habits.Contains(Habit.Worrier)) c.Habits.Add(Habit.Worrier);
                    c.Needs.Stress = 0.6f;
                }
                a.Props.Place(Props.Get("safety"), ca.Room!, null, "시험", near: Cell.FromPosition(ca.Position));
                Run(a, SimTime.Minutes(20)); Run(b, SimTime.Minutes(20));
                Check("곁에서 — 좋아하는 소품(걱정 많은 사람 · 안전 수칙) 곁에서 스트레스가 더 빠진다", ca.Needs.Stress < cb.Needs.Stress && a.Props.Stats.Eased > b.Props.Stats.Eased,
                    $"{ca.Name} ({ca.Room?.Name}) 스트레스 {ca.Needs.Stress * 100:0.00}% / 소품 없이 {cb.Needs.Stress * 100:0.00}% · 덜어 준 몫 {a.Props.Stats.Eased * 100:0.00}%p / {b.Props.Stats.Eased * 100:0.00}%p");
            }

            // 4) 불: 종이 소품이 타고 · 꺼진 뒤 그을린 판이 걸리고 · 생긴 관행은 안내판으로 붙는다
            {
                var w = DayOne(seed, "Mirinae");
                var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess).OrderBy(r => r.Id).First();
                w.Props.Place("travelposter", room, "시험");
                var cell = room.Cells.First(c => w.Ship.IsWalkable(c));
                w.Fire.Ignite(cell, 0.9f);
                Run(w, SimTime.Hours(30));
                var plate = w.Props.Placed.FirstOrDefault(p => p.Spec.Id == "scorch");
                var burnt = w.Log.Entries.Where(e => e.Text.Contains("탔다")).Select(e => e.Text).FirstOrDefault();
                Check("불 — 종이·천 소품이 타고, 꺼진 뒤 그을린 판이 걸린다", w.History.Fires >= 1 && w.Props.Stats.Burned >= 1 && plate != null,
                    $"화재 {w.History.Fires} · 탄 소품 {w.Props.Stats.Burned} ({burnt}) · " + (plate == null ? "그을린 판 없음" : $"{plate.Name} — {w.Ship.Rooms[plate.RoomId].Name} · {w.Log.Entries.Where(e => e.Text.Contains("그을린")).Select(e => e.Text).FirstOrDefault()}"));
                var sign = w.Props.Placed.FirstOrDefault(p => p.Spec.Id == "extsign");
                Check("관행 — 큰불 뒤 생긴 관행(소화기 자리 확인)이 안내판으로 붙는다", w.Culture.Of(CustomKind.FireCheck) != null && sign != null,
                    sign == null ? $"관행 {(w.Culture.Of(CustomKind.FireCheck) != null ? "있음" : "없음")} · 안내판 없음" : $"{sign.Name} — {w.Ship.Rooms[sign.RoomId].Name} · {sign.Origin}");
            }

            // 5) 추모: 떠난 사람의 추모 액자 · 추모일엔 그 앞에 촛불등
            {
                var w = DayOne(seed, "Mirinae");
                var dead = w.Crew.Where(c => !c.IsChild).OrderBy(c => c.Id).Skip(2).First();
                w.CrewCanDie = true;
                dead.Vitals.Health = 0f;
                Run(w, SimTime.Hours(2));
                w.CrewCanDie = false;
                Run(w, SimTime.Hours(24));
                var frame = w.Props.Placed.FirstOrDefault(p => p.Spec.Id == "memorial" && p.Label == dead.Name);
                var cu = w.Culture.Of(CustomKind.Memorial);
                if (cu != null) cu.NextDay = w.Tick + SimTime.Hours(1); // 이레 뒤를 당겨서
                Run(w, SimTime.Hours(8));
                var candle = w.Props.Placed.FirstOrDefault(p => p.Spec.Id == "candle");
                Check("추모 — 떠난 사람의 추모 액자가 걸리고, 추모일엔 그 앞에 촛불등이 켜진다", dead.Dead && frame != null && candle != null && candle.RoomId == frame.RoomId,
                    $"죽음 {dead.Dead} · " + (frame == null ? "액자 없음" : $"{frame.Name} — {w.Ship.Rooms[frame.RoomId].Name}") + " · " + (candle == null ? "촛불등 없음" : candle.Name)
                    + $" · {w.Log.Entries.Where(e => e.Text.Contains("추모 액자")).Select(e => e.Text).LastOrDefault()}");
            }

            // 6) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 소품이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 소품·장식 70 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
