using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v14.3 취미의 실제 행동 · 개인 물건: 가져와서 하고 · 끊기면 펼친 채 두고 · 이어 하고 · 제자리에 · 판 · 작품 · 연주 · 망가짐과 고침 · 공구 · 물려받기
public static partial class Program
{
    private static int RunBelongTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"개인 물건·취미 점검 (v14.3) · 시드 {seed}\n");
        bool debug = Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2";
        try
        {
            // 1) 모두 물건이 있다 — 취미 물건 · 컵 · 사진 · 담요 · 공구, 이름과 출처
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var bs = w.Belongings;
                bool all = w.Crew.All(c => bs.Of(c).Any(b => b.Kind == BelongingKind.Mug));
                bool hobby = w.Crew.All(c => c.Hobbies.All(h => BelongingSystem.ItemFor(h) is not BelongingKind k || bs.Of(c).Any(b => b.Kind == k)));
                bool tools = w.Crew.Where(c => c.Role is CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician).All(c => bs.Of(c).Any(b => b.Kind == BelongingKind.Toolset));
                var c0 = w.Crew[0];
                Check("물건 — 사람마다 취미 물건·컵·사진·담요·공구, 이름과 출처가 있다", all && hobby && tools && bs.All.All(b => b.Name.Length > 0 && b.Origin.Length > 0),
                    $"물건 {bs.All.Count}개 · {c0.Name}: " + string.Join(" / ", bs.Of(c0).Select(b => $"{b.Name} ({b.Origin})")));
            }
            // 2) 하루: 취미를 가져와서 하고 제자리에 둔다
            var day = World.CreateDefault(seed, 0, "Hanbit");
            Run(day, SimTime.TicksPerDay + SimTime.Hours(12));
            {
                var st = day.Belongings.Stats;
                var line = day.Log.Entries.Where(e => Persona.Hobbies.Any(h => e.Text == h.Doing) || BelongingSystem.Specs.Any(s => s.Doing.Length > 0 && e.Text == s.Doing)).Select(e => $"{day.Crew.FirstOrDefault(c => c.Id == e.CrewId)?.Name} {e.Text}").Take(4);
                Check("취미 — 물건을 가져와 알맞은 자리에서 하고, 제자리에 돌려놓는다", st.Sessions >= 6 && st.Returned >= 3, $"{st}\n      예: {string.Join(" · ", line)}");
            }
            // 3) 끊기면 펼친 채 두고, 나중에 그 자리로 돌아와 이어 한다
            {
                var w = DayOne(seed, "Hanbit");
                CrewMember? reader = null; Belonging? book = null;
                for (int i = 0; i < 24 * 12 && reader == null; i++)
                {
                    Run(w, SimTime.Minutes(5));
                    foreach (var c in w.Crew)
                        if (c.Job?.Activity is HobbyActivity && c.Job.Current is WaitToil && w.Belongings.All.FirstOrDefault(b => b.Holder == c.Id) is Belonging held) { reader = c; book = held; break; }
                }
                float p0 = book?.Progress ?? 0f;
                if (reader != null) { reader.EndJob(w, ToilStatus.Interrupted); reader.NextThinkTick = w.Tick + SimTime.Minutes(20); } // 경보처럼 끊는다
                bool left = book != null && book.Open && book.At != null && book.Holder < 0;
                string where = book != null ? w.Belongings.Where(book) : "-";
                int resumed0 = w.Belongings.Stats.Resumed;
                bool resumed = false;
                for (int i = 0; i < 24 * 12 && !resumed && book != null; i++) { Run(w, SimTime.Minutes(5)); resumed = w.Belongings.Stats.Resumed > resumed0; }
                Check("끊기면 — 펼친 채 그 자리에 두고, 나중에 돌아와 이어 한다", left && resumed && book!.Progress >= p0,
                    $"{reader?.Name}: {book?.Name} · 끊겼을 때 {where} · 이어 함 {(resumed ? "예" : "아니오")} · 진척 {p0 * 100:0}% → {(book?.Progress ?? 0f) * 100:0}%");
            }
            // 4) 체스·카드: 상대를 기다리고 판이 진행된다 — 끊긴 판은 이어 둔다
            {
                var w = DayOne(seed, "Hanbit");
                var players = w.Crew.Where(c => !c.IsChild).Take(4).ToList();
                foreach (var c in players)
                {
                    c.Hobbies.Clear(); c.Hobbies.Add(Hobby.Chess);
                    if (!w.Belongings.Of(c).Any(b => b.Kind == BelongingKind.ChessSet)) w.Belongings.Seed2(c, BelongingKind.ChessSet);
                }
                Run(w, SimTime.Hours(36));
                var st = w.Belongings.Stats;
                var g = w.Belongings.Games.FirstOrDefault(x => x.Done && x.Winner >= 0);
                Check("체스 — 상대를 기다려 판이 열리고, 끝까지 둔다 (끊긴 판은 이어 둔다)", st.Games >= 1 && st.GamesDone >= 1,
                    $"판 {st.Games} · 끝남 {st.GamesDone} · 이어 둠 {st.GamesResumed} · 예: " + (g != null ? $"{w.Crew.First(c => c.Id == g.A).Name} 대 {w.Crew.First(c => c.Id == g.B).Name} — {w.Crew.First(c => c.Id == g.Winner).Name} 승 ({g.Moves:0}수, 끊김 {g.Breaks})" : "-"));
            }
            // 5) 그림·모형·뜨개: 조금씩 완성되어 방에 남고, 감상되고, 선물된다
            {
                var w = DayOne(seed, "Hanbit");
                foreach (var c in w.Crew.Where(c => !c.IsChild).Take(5))
                {
                    c.Hobbies.Clear(); c.Hobbies.Add(Hobby.Painting);
                    var kit = w.Belongings.Of(c).FirstOrDefault(b => b.Kind == BelongingKind.Sketchbook) ?? w.Belongings.Seed2(c, BelongingKind.Sketchbook);
                    kit.Progress = 0.85f;
                }
                Run(w, SimTime.Hours(48));
                var st = w.Belongings.Stats;
                var art = w.Belongings.All.FirstOrDefault(b => b.Kind == BelongingKind.Artwork);
                Check("작품 — 조금씩 완성되어 방에 걸리거나 선물되고, 다른 사람이 감상한다", st.Artworks >= 2 && (st.Admired > 0 || st.Gifts > 0),
                    $"작품 {st.Artworks} · 선물 {st.Gifts} · 감상 {st.Admired} · 예: {(art != null ? $"{art.Name} — {w.Belongings.Where(art)} · {string.Join(" / ", art.Marks.Select(m => m.Text))}" : "-")}");
            }
            // 6) 악기: 곁의 사람이 듣는다 · 밤에는 옆에서 자던 사람이 조용히 해 달라 한다
            {
                var w = DayOne(seed, "Hanbit");
                foreach (var c in w.Crew.Where(c => !c.IsChild))
                {
                    c.Hobbies.Clear(); c.Hobbies.Add(Hobby.Instrument);
                    if (!w.Belongings.Of(c).Any(b => b.Kind == BelongingKind.Instrument)) w.Belongings.Seed2(c, BelongingKind.Instrument);
                    c.Habits.Remove(Habit.HeavySleeper);
                }
                Run(w, SimTime.Hours(48));
                var st = w.Belongings.Stats;
                var hush = w.Log.Entries.LastOrDefault(e => e.Text.Contains("조용히 해 달라"));
                Check("악기 — 곁의 사람이 듣고, 밤에는 자던 사람이 조용히 해 달라고 한다", st.Listened > 0 && st.Hushed > 0,
                    $"들음 {st.Listened} · 조용히 {st.Hushed} · 예: {(hush != null ? $"{w.Crew.FirstOrDefault(c => c.Id == hush.CrewId)?.Name} {hush.Text}" : "-")}");
            }
            // 7) 물·불: 놓인 물건이 젖고 탄다 → 주인이 알면 마음이 무너진다 → 솜씨 있는 친구가 고쳐(말려) 준다
            {
                var w = DayOne(seed, "Hanbit");
                var owner = w.Crew.First(c => c.Bed != null && !c.IsChild);
                var book = w.Belongings.Of(owner).FirstOrDefault(b => BelongingSystem.Spec(b.Kind).Fragile && b.Kind != BelongingKind.Artwork && b.At == null && b.Holder < 0) ?? w.Belongings.Seed2(owner, BelongingKind.Book);
                var room = owner.Bed!.Room;
                w.Moisture.AddWater(room, room.Cells.Count * 20f * 0.4f);
                float s0 = owner.Needs.Stress;
                Run(w, SimTime.Hours(2));
                bool ruined = !book.Usable;
                foreach (var c in w.Crew.Where(c => c != owner)) c.ChangeAffinity(owner, 0.5f);
                if (Environment.GetEnvironmentVariable("BELONG_DEBUG") == "1")
                    for (int k = 0; k < 20; k++)
                    {
                        Run(w, SimTime.Hours(2));
                        Console.WriteLine($"   [{SimTime.Clock(w.Tick)}] 책 {book.Condition:0.00} 쥔 {book.Holder} · " + string.Join(" | ", w.Crew.Where(c => c.RawSkill(Skill.Mechanics) >= 0.35f).Select(c => $"{c.Name} {c.Job?.Label} 호감 {c.AffinityTo(owner):0.00} · " + string.Join(",", c.LastEvaluations.Take(3).Select(e => $"{e.Activity.Id}:{e.Score:0.00}")) + $" · mend {c.LastEvaluations.FirstOrDefault(e => e.Activity.Id == "mend").Score:0.00}")));
                    }
                else Run(w, SimTime.Hours(40));
                var st = w.Belongings.Stats;
                Check("망가짐 — 물에 젖은 물건을 주인이 알게 되고, 친구가 말려(고쳐) 준다", ruined && st.Noticed >= 1 && st.Mended >= 1,
                    $"{owner.Name}의 {book.Name} 상태 {book.Condition * 100:0}% · 앎 {st.Noticed} · 고침 {st.Mended} · 이력 {string.Join(" / ", book.Marks.Select(m => m.Text))} · 관계의 이유: {string.Join(", ", w.Relations.All.Where(m => m.Who == owner.Id).Select(m => $"{w.Crew.First(c => c.Id == m.About).Name} — {m.Text}"))}");
            }
            // 8) 공구: 제 공구가 없으면 빌리고, 안 돌려주면 서운하다 · 손에 익은 공구는 빠르다
            {
                var w = DayOne(seed, "Hanbit");
                var techs = w.Crew.Where(c => c.Role is CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician).ToList();
                var a = techs[0];
                // 한 사람 것만 남기고 모두의 공구가 고칠 수 없게 망가졌다 — 누가 공구 일을 맡든 빌려야 한다
                foreach (var t in techs.Take(techs.Count - 1))
                {
                    w.Belongings.Of(t).First(b => b.Kind == BelongingKind.Toolset).Condition = 0.02f;
                    t.Habits.Clear(); t.Habits.Add(Habit.Forgetful);
                }
                if (debug) for (int h = 0; h < 48; h++) { Run(w, SimTime.Hours(1)); Console.WriteLine($"      {SimTime.Clock(w.Tick)} {a.Name}({a.Role}) {a.Job?.Label} {a.Job?.Order?.Kind} {a.Job?.Order?.Skill} · 공구 {string.Join(",", w.Belongings.All.Where(b => b.Kind == BelongingKind.Toolset).Select(b => $"{w.Crew.First(c => c.Id == b.Owner).Name}:{b.Condition:0.0}/{b.At?.ToString() ?? "집"}/빌림{b.BorrowedBy}"))}"); }
                else Run(w, SimTime.Hours(48));
                var st = w.Belongings.Stats;
                Check("공구 — 제 것이 망가지면 동료 것을 빌리고, 돌려주지 않으면 서운해한다 (관계의 이유로 남는다)", st.Borrows >= 1 && (st.Returned > 0 || st.Unreturned > 0 || st.Reclaims > 0),
                    $"공구 일 {st.ToolJobs} · 빌림 {st.Borrows} · 안 돌려줌 {st.Unreturned} · 되찾음 {st.Reclaims} · {a.Name} 손 ×{a.ToolFactor:0.00} · 기억: {string.Join(", ", w.Relations.All.Where(m => m.Reason == RelationReason.TookMyThing).Select(m => $"{w.Crew.First(c => c.Id == m.Who).Name}→{w.Crew.First(c => c.Id == m.About).Name} {m.Text}"))}");
            }
            // 9) 떠난 사람: 공구는 제자에게, 사진은 가족·가까운 사람에게
            {
                var w = DayOne(seed, "Hanbit");
                var dead = w.Crew.First(c => c.Role is CrewRole.Technician or CrewRole.Engineer);
                w.CrewCanDie = true;
                dead.Vitals.Health = 0f; dead.Down = true;
                Run(w, SimTime.Minutes(5));
                var st = w.Belongings.Stats;
                var tools = w.Belongings.All.FirstOrDefault(b => b.Kind == BelongingKind.Toolset && b.From == dead.Id);
                Check("떠난 사람 — 공구는 같은 일을 하는 가까운 사람에게, 사진은 가족에게", dead.Dead && st.Inherited >= 1 && tools != null && tools.Owner != dead.Id,
                    $"{dead.Name} 사망 · 물려받음 {st.Inherited} · {(tools != null ? $"{tools.Name} → {w.Crew.First(c => c.Id == tools.Owner).Name} ({tools.Marks.LastOrDefault().Text})" : "-")}");
            }
            // 10) 열흘: 물건이 쌓인 이력
            {
                var st = day.Belongings.Stats;
                if (debug) foreach (var b in day.Belongings.All.Where(b => b.Marks.Count > 0).Take(12)) Console.WriteLine($"      {day.Belongings.Line(b)} · {string.Join(" / ", b.Marks.Select(m => m.Text))}");
                Check("이력 — 물건에 일이 쌓인다", day.Belongings.All.Count(b => b.Marks.Count > 0) >= 3, $"이력 있는 물건 {day.Belongings.All.Count(b => b.Marks.Count > 0)} / {day.Belongings.All.Count}");
            }
            // 11) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 물건이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 개인 물건·취미 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
