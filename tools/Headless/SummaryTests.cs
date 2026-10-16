using System;
using System.Linq;
using ShipSim.Core;

// v23 사고 결산 점검 (--summarytest): 지휘 건이 닫히면 결산 한 장 — 무엇이 · 누가 · 잃은 것 · 지킨 것 · 로봇 활약 · 주컴퓨터 판단 · 큰 건 · 저장.
public static partial class Program
{
    private static int RunSummaryTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"사고 결산 점검 (v23) · 시드 {seed}\n");
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(2)); return w; }
            Room Quiet(World w) => w.Ship.RoomsOf(RoomType.Storage).Concat(w.Ship.LiveRooms).First(r => !r.Detached && w.Crew.All(c => c.Room != r) && r.Cells.Count(w.Ship.IsOpenFloor) >= 6);

            // 1) 큰 불 (천장 소화 없이): 결산이 크게
            {
                var w = Fresh();
                NozzleSystem.On = false;
                var room = Quiet(w);
                foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(6)) w.Fire.Ignite(c, 0.95f);
                CommandCase? k = null;
                for (int m = 0; m < 90 && (k == null || k.Open); m++) { Run(w, SimTime.Minutes(1)); k ??= w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == room.Id); }
                NozzleSystem.On = true;
                var s = w.Commander.Summaries.LastOrDefault(x => k != null && x.CaseId == k.Id);
                Check("불 지휘 건이 닫히면 결산 한 장 — 무엇이 · 누가 · 지킨 것 · 로봇 · 주컴퓨터", s != null && s.What.Contains("칸") && s.Who.Any(x => x.Contains("첫 손")) && s.Kept.Count > 0 && s.Robots.Contains("닿았다") && s.Computer.Contains("알았다"),
                    s == null ? "결산 없음" : $"{s.Title} · {s.What} · 누가 {string.Join("/", s.Who)} · 잃음 {string.Join("/", s.Lost)} · 지킴 {string.Join("/", s.Kept)} · {s.Robots} · {s.Computer}");
                Check("큰 불(3칸 넘게)이면 크게 띄울 건", s != null && s.Big, s == null ? "" : $"가장 컸을 때 {k!.Peak:0}칸 · {s.Minutes:0}분");
            }

            // 2) 천장 소화가 금방 끈 작은 불: 작게
            {
                var w = Fresh();
                var room = Quiet(w);
                w.Fire.Ignite(room.Cells.Where(w.Ship.IsOpenFloor).First(), 0.4f);
                CommandCase? k = null;
                for (int m = 0; m < 40 && (k == null || k.Open); m++) { Run(w, SimTime.Minutes(1)); k ??= w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == room.Id); }
                var s = w.Commander.Summaries.LastOrDefault(x => k != null && x.CaseId == k.Id);
                Check("작은 불은 결산을 작게 (띠로만)", s != null && !s.Big, s == null ? "결산 없음" : $"{s.Title} · {s.What} · 잃음 {string.Join("/", s.Lost)} · {s.Robots}");
            }

            // 3) 다친 사람
            {
                var w = Fresh();
                var v = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Outside && c.Room != null).OrderBy(c => c.Id).First();
                w.Casualty.Inflict(v, TraumaKind.Bleed, 0.25f, "시험 — 크게 베였다");
                CommandCase? k = null;
                for (int m = 0; m < 180 && (k == null || k.Open); m++) { Run(w, SimTime.Minutes(1)); k ??= w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Casualty && c.CrewId == v.Id); }
                var s = w.Commander.Summaries.LastOrDefault(x => k != null && x.CaseId == k.Id);
                Check("다친 사람 결산 — 이름 · 결과", s != null && s.Title.Contains(v.Name), s == null ? (k == null ? "지휘 건 없음" : $"아직 열림 ({k.Outcome})") : $"{s.Title} · 잃음 {string.Join("/", s.Lost)} · 지킴 {string.Join("/", s.Kept)} · {s.Robots}");
            }

            // 4) 저장 · 되살림
            {
                var w = Fresh();
                var room = Quiet(w);
                w.Fire.Ignite(room.Cells.Where(w.Ship.IsOpenFloor).First(), 0.4f);
                for (int m = 0; m < 30 && w.Commander.Summaries.Count == 0; m++) Run(w, SimTime.Minutes(1));
                var w2 = StateSave.Read(StateSave.Write(w));
                Check("저장 · 되살림 — 결산이 남는다", w.Commander.Summaries.Count > 0 && w2.Commander.Summaries.Count == w.Commander.Summaries.Count && w2.Commander.Summaries[^1].Title == w.Commander.Summaries[^1].Title,
                    w2.Commander.Summaries.LastOrDefault()?.Title ?? "없음");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        finally { NozzleSystem.On = true; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
