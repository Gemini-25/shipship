using System;
using System.Linq;
using ShipSim.Core;

// v23 오늘의 장면 점검 (--albumtest): 연대기 기록마다 장면 후보 점수 · 가벼운 일은 빼고 · 같은 방 30분은 가장 센 것 하나 · 사고 대응은 결산 카드의 말로
//   · 저녁 22시에 1~3장을 골라 사진첩에 · 저장 · 실제 이틀.
public static partial class Program
{
    private static int RunAlbumTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"오늘의 장면 점검 (v23) · 시드 {seed}\n");
        try
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var book = w.Album;
            var rooms = w.Ship.LiveRooms.Where(r => r.Cells.Count(w.Ship.IsOpenFloor) >= 6).ToList();
            Room a = rooms[0], b = rooms[1];
            var crew = w.Crew.Where(c => !c.Dead).Take(2).ToList();
            book.Today.Clear();

            // 1) 점수 · 가벼운 일은 빼고 · 같은 방 30분은 가장 센 것 하나
            w.History.Add(w, HistoryKind.Memory, "시험 — 차를 마셨다", a);
            int afterLight = book.Today.Count;
            w.History.Add(w, HistoryKind.Death, $"{crew[0].Name} 숨졌다 (시험)", a, new[] { crew[0] });
            Run(w, SimTime.Minutes(5));
            w.History.Add(w, HistoryKind.Damage, "시험 — 벽이 그을렸다", a);
            w.History.Add(w, HistoryKind.Bond, $"{crew[1].Name} 사이에 아이가 태어났다 (시험)", b, new[] { crew[1] });
            var inA = book.Today.Where(s => s.RoomId == a.Id).ToList();
            var baby = book.Today.FirstOrDefault(s => s.RoomId == b.Id);
            Check("연대기 기록마다 장면 후보 — 가벼운 일은 빼고 · 같은 방 30분은 가장 센 것 하나 · 탄생이 가장 높다",
                afterLight == 0 && inA.Count == 1 && inA[0].Kind == HistoryKind.Death && baby != null && baby.Score > inA[0].Score && baby.X >= 0,
                $"가벼운 일 뒤 {afterLight} · {a.Name} {string.Join(" / ", inA.Select(s => $"{s.Title} {s.Score:0.#}"))} · 탄생 {baby?.Score:0.#}");

            // 2) 큰 불 — 사고 대응은 결산 카드의 말로 (지휘 건 번호 대신 어디서 무엇이)
            var core = w.Automation.ComputerBody?.Room;
            var fireRoom = rooms.Where(r => r != a && r != b && r != core && w.Crew.All(c => c.Room != r)).OrderByDescending(r => r.Cells.Count(w.Ship.IsOpenFloor)).First();
            foreach (var c in fireRoom.Cells.Where(w.Ship.IsOpenFloor).Take(5)) w.Fire.Ignite(c, 0.95f);
            SceneShot? resp = null;
            for (int m = 0; m < 90 && resp == null; m++)
            {
                Run(w, SimTime.Minutes(1));
                if (w.Commander.Cases.Any(k => k.Kind == CaseKind.Fire && k.RoomId == fireRoom.Id && !k.Open)) resp = book.Today.FirstOrDefault(s => s.Kind == HistoryKind.Response && s.RoomId == fireRoom.Id);
            }
            if (Environment.GetEnvironmentVariable("AL_DEBUG") == "1")
            {
                Console.WriteLine($"    불 방 {fireRoom.Name}#{fireRoom.Id} · 후보: " + string.Join(" / ", book.Today.Select(s => $"{s.Kind} r{s.RoomId} {s.Title} {s.Score:0.#}")));
                foreach (var e in w.History.Events.Where(e => e.Tick > SimTime.Hours(1))) Console.WriteLine($"    연대기 {SimTime.Clock(e.Tick)} {e.Kind} r{e.RoomId} {e.Text}");
            }
            Check("사고 대응 장면은 결산 카드의 말로 — 어디서 · 무엇이 · 로봇", resp != null && !resp.Title.Contains('#') && resp.Title.Contains(fireRoom.Name) && resp.Score >= 4f && w.Commander.Summaries.Any(s => s.Title == resp.Title),
                resp == null ? $"대응 장면 없음 · {string.Join(" / ", w.Commander.Cases.Select(k => $"#{k.Id} {k.Kind} {k.Phase} 최대 {k.Peak:0}"))}" : $"{resp.Title} · {resp.Text} · {resp.Score:0.#}");

            // 3) 저녁 22시 — 1~3장을 골라 사진첩에
            int before = book.Shots.Count;
            for (int m = 0; m < 24 * 60 && book.LastPickTick < 0; m += 10) Run(w, SimTime.Minutes(10));
            var picked = book.LastPick.Select(book.Get).Where(s => s != null).Cast<SceneShot>().ToList();
            var log = w.Log.Entries.Where(e => e.Text.StartsWith("오늘의 장면")).Select(e => e.Text).LastOrDefault();
            Check("저녁 22시에 그날 장면 1~3장을 골라 사진첩에 · 후보는 비운다",
                SimTime.HourOfDay(book.LastPickTick) >= 22f && picked.Count is >= 1 and <= 3 && book.Shots.Count - before == picked.Count && book.Today.Count == 0 && log != null && picked.Any(s => s.Kind == HistoryKind.Bond),
                $"{SimTime.Clock(book.LastPickTick)} · {picked.Count}장 · {log}");

            // 4) 저장 · 되살림
            var w2 = StateSave.Read(StateSave.Write(w));
            Check("저장 · 되살림 — 사진첩 · 마지막 장면 · 번호", w2.Album.Shots.Count == book.Shots.Count && w2.Album.LastPick.SequenceEqual(book.LastPick) && w2.Album.NextId == book.NextId && w2.Album.Shots.Select(s => s.Title).SequenceEqual(book.Shots.Select(s => s.Title)),
                $"{w2.Album.Shots.Count}장 · 다음 번호 {w2.Album.NextId}");

            // 5) 실제 이틀 — 날마다 1~3장
            var n = World.CreateDefault(seed, 0, "Hanbit");
            var perDay = new System.Collections.Generic.List<string>();
            long seen = -1;
            for (int h = 0; h < 50; h++)
            {
                Run(n, SimTime.Hours(1));
                if (n.Album.LastPickTick != seen && n.Album.LastPickTick >= 0) { seen = n.Album.LastPickTick; perDay.Add($"{SimTime.Day(seen)}일 {n.Album.LastPick.Count}장: " + string.Join(" · ", n.Album.LastPick.Select(id => n.Album.Get(id)!.Title))); }
            }
            foreach (var d in perDay) Console.WriteLine("    " + d);
            Check("실제 이틀 — 날마다 1~3장 (후보가 있으면)", n.Album.Shots.Count >= 1 && perDay.Count >= 1 && n.Album.Shots.GroupBy(s => s.Day).All(g => g.Count() <= 3), $"사진첩 {n.Album.Shots.Count}장 · {perDay.Count}번 고름");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
