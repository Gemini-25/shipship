using System;
using System.Linq;
using ShipSim.Core;

// v23 항해의 목적 점검 (--missiontest): 배 종류(가진 방)마다 맡는 임무 · 임무 받기 · 화물칸 불 → 상자를 잃음 · 탐사 자료 · 늦게 오는 통신(기한 · 목적지 · 구조)
//   · 갈림길(빠듯하면 지름길 · 상했거나 여유면 돌아감) · 기한이 빠듯하면 행성을 지나침 · 도착(이주민 내림 · 보수 · 항해의 책 · 이어받기 · 다음 임무) · 저장.
public static partial class Program
{
    private static int RunMissionTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"항해의 목적 점검 (v23) · 시드 {seed}\n");
        try
        {
            // 1) 배 종류마다 맡는 임무
            var tops = new System.Collections.Generic.List<string>();
            foreach (var key in new[] { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Cheonma" })
            {
                var sw = World.CreateDefault(seed, 0, key);
                var roles = PurposeSystem.Roles(sw.Ship).OrderByDescending(r => r.w).ToList();
                tops.Add($"{key} {string.Join("/", roles.Take(3).Select(r => $"{PurposeSystem.KindName(r.k)} {r.w:0.0}"))}");
            }
            foreach (var t in tops) Console.WriteLine("    " + t);
            var w = World.CreateDefault(seed, 0, "Hanbit");
            PlanetSystem.Off = true;
            Run(w, SimTime.Hours(1));
            var pu = w.Purpose;
            Check("항해마다 임무를 받는다 — 의뢰인 · 목표 · 기한", pu.Now != null && pu.Now.Goal > 0 && pu.DaysLeft > pu.RouteLeft && w.Log.Entries.Any(e => e.Text.Contains("번째 항해의 임무")),
                pu.Now == null ? "임무 없음" : $"{PurposeSystem.KindName(pu.Now.Kind)} — {pu.Now.Title} ({pu.Now.Client}) · 기한 {pu.DaysLeft:0.0}일 · 남은 길 {pu.RouteLeft:0.0}일");

            // 2) 화물: 화물칸 불 → 상자를 잃는다
            pu.BeginAs(MissionKind.Cargo);
            int crates0 = pu.Now!.Aboard;
            var hold = w.Ship.LiveRooms.Where(r => r.Kind is RoomType.Cargo or RoomType.Storage).OrderByDescending(r => r.Cells.Count(w.Ship.IsOpenFloor)).First();
            foreach (var c in hold.Cells.Where(w.Ship.IsOpenFloor).Take(4)) w.Fire.Ignite(c, 0.95f);
            Run(w, SimTime.Hours(2));
            Check("화물 — 화물칸에 불이 나면 상자를 잃는다", pu.Now.Lost >= 1 && pu.Now.Aboard < crates0, $"{hold.Name} · 상자 {crates0} → {pu.Now.Aboard} · {pu.Now.Notes.LastOrDefault()}");

            // 3) 탐사 자료
            pu.BeginAs(MissionKind.Survey);
            w.Voyage.JumpTo(LegKind.Nebula);
            int d1 = pu.Now!.Progress;
            w.Planet.LandNow(PlanetGen.Special(new Rng(seed), 990, "crystal"));
            int d2 = pu.Now.Progress;
            Check("탐사 — 처음 본 풍경 · 내려앉은 행성 · 살핀 곳마다 자료", d1 >= 1 && d2 >= d1 + 2, $"풍경 뒤 {d1} · 행성 뒤 {d2}/{pu.Now.Goal}");
            w.Planet.SetStay(0.01f);
            for (int h = 0; h < 12 && w.Planet.Phase != PlanetPhase.None; h++) Run(w, SimTime.Hours(1));

            // 4) 늦게 오는 통신
            pu.BeginAs(MissionKind.Rescue);
            long dl0 = pu.Now!.Deadline;
            pu.Comms(PurposeSystem.CommsKind.Sooner);
            bool sooner = pu.Now.Deadline < dl0 && w.Log.Entries.Any(e => e.Text.Contains("일 전에 보낸 통신"));
            string dest0 = w.Voyage.Destination;
            pu.Comms(PurposeSystem.CommsKind.Redirect);
            bool redirected = w.Voyage.Destination != dest0 && w.Voyage.Legs[^1].Name == w.Voyage.Destination;
            int crew0 = w.Crew.Count(c => !c.Dead && !c.LeftShip);
            pu.Comms(PurposeSystem.CommsKind.RescueCall);
            Check("늦게 오는 통신 — 기한을 당기고 · 목적지를 바꾸고 · 조난 신호에 들러 사람을 태운다", sooner && redirected && pu.Now.People.Count >= 1 && w.Crew.Count(c => !c.Dead && !c.LeftShip) > crew0,
                $"기한 {(dl0 - pu.Now.Deadline) / (float)SimTime.TicksPerDay:0.0}일 당김 · 목적지 {dest0} → {w.Voyage.Destination} · 구조 {pu.Now.People.Count}명 · {string.Join(" / ", pu.Now.Notes.TakeLast(3))}");

            // 5) 갈림길
            pu.BeginAs(MissionKind.Cargo);
            pu.SetDeadline(pu.RouteLeft * 0.9f);
            int at = Math.Min(w.Voyage.Index + 1, w.Voyage.Legs.Count - 2);
            float days0 = w.Voyage.TotalDays;
            pu.Fork();
            var fork1 = w.Voyage.Legs[at];
            bool shortcut = w.Voyage.TotalDays < days0 && fork1.Kind is LegKind.Narrows or LegKind.RadiationBelt or LegKind.AsteroidBelt or LegKind.Graveyard;
            pu.BeginAs(MissionKind.Cargo);
            pu.SetDeadline(pu.RouteLeft + 8f);
            w.Policies.Set("route", 1, "시험");
            float days1 = w.Voyage.TotalDays;
            pu.Fork();
            bool detour = w.Voyage.TotalDays > days1;
            w.Policies.Set("route", 0, "시험");
            Check("갈림길 — 기한이 빠듯하면 지름길(험한 구간) · 여유 있고 안전 방침이면 돌아가는 길", shortcut && detour,
                $"빠듯: {fork1.Name}({VoyageSystem.KindName(fork1.Kind)}) {days0:0.0} → {days0 - (days0 - w.Voyage.TotalDays):0.0}일 · 여유: {days1:0.0} → {w.Voyage.TotalDays:0.0}일");

            // 6) 기한이 빠듯하면 행성을 지나친다
            PlanetSystem.Off = false;
            PlanetSystem.NoCrash = true;
            pu.SetDeadline(pu.RouteLeft * 0.7f);
            w.Planet.WindowNow();
            Run(w, SimTime.Minutes(50));
            Check("기한이 빠듯하면 주컴퓨터가 행성 착륙을 지나친다", w.Planet.Decision.Contains("기한") || w.Planet.Phase == PlanetPhase.None && w.Planet.Stats.Skipped >= 1,
                $"{w.Planet.Decision} · {w.Planet.Stats.Line()}");
            PlanetSystem.Off = true;
            PlanetSystem.NoCrash = false;

            // 7) 도착 — 이주민이 내리고 · 보수 · 항해의 책 · 이어받기 · 다음 임무
            if (w.Planet.Phase != PlanetPhase.None) { w.Planet.SetStay(0.01f); for (int h = 0; h < 24 && w.Planet.Phase != PlanetPhase.None; h++) Run(w, SimTime.Hours(1)); }
            pu.BeginAs(MissionKind.Colony);
            var colonists = pu.Now!.People.ToList();
            pu.SetDeadline(5f);
            float credits0 = w.Voyage.Credits;
            int voyage0 = w.Voyage.Number;
            w.Voyage.ArriveSoon();
            for (int t = 0; t < 30 && pu.Books.Count == 0; t++) Run(w, 15);
            var book = pu.Books.LastOrDefault();
            var done = pu.Past.LastOrDefault();
            Run(w, 30);
            Check("도착 — 이주민이 내리고 · 보수를 받고 · 항해의 책 한 권 · 다음 임무", book != null && done != null && done.Kind == MissionKind.Colony && colonists.Count > 0 && colonists.All(id => w.Crew[id].LeftShip || w.Crew[id].Dead)
                && w.Voyage.Credits > credits0 && w.Voyage.Number == voyage0 + 1 && pu.Now != null && pu.Now.Voyage == voyage0 + 1 && book.Chapters.Count >= 1 && book.Carried.Length > 0 && w.History.Events.Any(e => e.Text.Contains(" 도착 — ")),
                book == null ? "책 없음" : $"{done?.Result} · 이주민 {colonists.Count}명 · 돈 {credits0:0} → {w.Voyage.Credits:0} · 책 {book.Number}권 장 {book.Chapters.Count} · 이어진 것 {book.Carried} · 다음 임무 {pu.Now?.Title}");
            if (book != null) foreach (var c in book.Chapters.Take(4)) Console.WriteLine("    · " + c);

            // 8) 저장
            var w2 = StateSave.Read(StateSave.Write(w));
            Check("저장 · 되살림 — 지금 임무 · 지난 임무 · 항해의 책", w2.Purpose.Now?.Title == pu.Now?.Title && w2.Purpose.Books.Count == pu.Books.Count && w2.Purpose.Past.Count == pu.Past.Count && (pu.Books.Count == 0 || w2.Purpose.Books[^1].Chapters.SequenceEqual(pu.Books[^1].Chapters)),
                $"{w2.Purpose.Now?.Title} · 책 {w2.Purpose.Books.Count}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        PlanetSystem.Off = false;
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
