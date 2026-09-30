using System;
using System.Linq;
using ShipSim.Core;

// 배 전체 유틸리티 망: 전력 간선·급수관·환기 덕트
public static partial class Program
{
    private static int RunNetTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"배 전체 망 점검 · 시드 {seed}\n");
        foreach (var key in new[] { "Mirinae", "Hanbit" })
        {
            var w = DayOne(seed, key);
            var net = w.Net;
            int links = net.Links.Count;
            bool allFed = w.Ship.LiveRooms.All(r => r.PowerLinked && r.DuctLinked && r.WaterLinked);
            Check($"{w.Ship.Name}: 평소엔 모든 방이 이어져 있다", links > 0 && allFed, $"토막 {links}개 (전력·급수·덕트 {links / 3}개씩)");

            // 배전실에서 나가는 간선을 끊는다 → 그 너머 방이 정전 → 사람이 잇는다
            var power = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Room;
            var link = net.Links.Where(l => l.Kind == NetKind.Power && (l.Door?.RoomA == power || l.Door?.RoomB == power)).OrderBy(l => l.Id).First();
            net.Hurt(link, 1f, "시험");
            net.Update(0f);
            var dark = w.Ship.LiveRooms.Where(r => !r.PowerLinked).Select(r => r.Name).ToList();
            Run(w, SimTime.Minutes(2));
            int unpowered = w.Ship.LiveRooms.Count(r => !r.Powered);
            Run(w, SimTime.Hours(8));
            bool back = w.Ship.LiveRooms.All(r => r.PowerLinked);
            Check($"{w.Ship.Name}: 배전실 간선이 끊기면 그 너머만 정전 → 다시 잇는다", dark.Count > 0 && back,
                $"끊긴 뒤 간선 끊긴 방 {dark.Count}곳({string.Join("·", dark.Take(5))}) · 정전 {unpowered} · 여덟 시간 뒤 {(back ? "모두 이어짐" : "아직 끊김")} · {net.Stats}");
        }
        // 급수: 수경재배실로 가는 급수관이 끊기면 단수 → 재배대가 마른다
        {
            var w = DayOne(seed, "Mirinae");
            var hydro = w.Ship.RoomsOf(RoomType.Hydroponics).First();
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Water && (l.Door?.RoomA == hydro || l.Door?.RoomB == hydro))) w.Net.Hurt(l, 1f, "시험");
            w.Net.Update(0f);
            bool dry = !hydro.WaterLinked && !w.Piping.WaterTo(hydro);
            Run(w, SimTime.Hours(10));
            Check("급수관이 끊기면 단수 → 다시 잇는다", dry && hydro.WaterLinked, $"단수 {(dry ? "예" : "아니오")} → 열 시간 뒤 {(hydro.WaterLinked ? "다시 이어짐" : "아직 단수")}");
        }
        // 결정론
        {
            uint H() { var w = DayOne(seed, "Mirinae"); Scenarios.Apply(w, "chaos", out _); Run(w, SimTime.Hours(12)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("망이 든 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 배 전체 망 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
