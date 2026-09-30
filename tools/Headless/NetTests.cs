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
            bool allFed = w.Ship.LiveRooms.All(r => r.PowerLinked && r.DuctLinked && r.WaterLinked && r.DataLinked);
            Check($"{w.Ship.Name}: 평소엔 모든 방이 이어져 있다", links > 0 && allFed, $"토막 {links}개 (전력·급수·덕트·데이터 {links / 4}개씩)");

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
            Check($"{w.Ship.Name}: 배전실 간선이 끊기면 그 너머만 정전 (보조 간선이 있으면 반대쪽으로) → 다시 잇는다", (dark.Count > 0 || w.Net.Rings.Count > 0) && back,
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
        // 데이터선: 함교에서 나가는 데이터선이 끊기면 그 너머 방의 감지기 값이 멈춘다 → 다시 잇는다
        {
            var w = DayOne(seed, "Mirinae");
            var bridge = w.Ship.FurnitureOf(FurnitureType.MainComputer).First().Room;
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && (l.Door?.RoomA == bridge || l.Door?.RoomB == bridge))) w.Net.Hurt(l, 1f, "시험");
            w.Net.Update(0f);
            var blind = w.Ship.LiveRooms.Where(r => !r.DataLinked).ToList();
            Run(w, SimTime.Minutes(40));
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!;
            float age = (w.Tick - pump.LastReading) / (float)SimTime.TicksPerHour;
            bool stale = !pump.Body.Room.DataLinked && age > 0.3f || pump.Body.Room.DataLinked;
            Run(w, SimTime.Hours(10));
            Check("데이터선이 끊기면 그 너머 감지기 값이 멈춘다 → 다시 잇는다", blind.Count > 0 && stale && w.Ship.LiveRooms.All(r => r.DataLinked),
                $"끊긴 방 {blind.Count}곳({string.Join("·", blind.Take(4).Select(r => r.Name))}) · 냉각 펌프 마지막 측정 {age * 60:0}분 전 · 열 시간 뒤 {(w.Ship.LiveRooms.All(r => r.DataLinked) ? "모두 이어짐" : "아직 끊김")}");
        }
        // 이중화: 중형 이상 배는 처음부터 보조 간선 — 배전실 문 앞이 다 끊겨도 생명유지실·함교는 전기가 들어온다
        {
            var w = DayOne(seed, "Hanbit");
            var power = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Room;
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Power && l.Door != null && (l.Door.RoomA == power || l.Door.RoomB == power))) w.Net.Hurt(l, 1f, "시험");
            w.Net.Update(0f);
            var life = w.Ship.RoomsOf(RoomType.LifeSupport).First();
            int dark = w.Ship.LiveRooms.Count(r => !r.PowerLinked);
            Check("중형 배 — 보조 간선이 있어 배전실 문 앞이 끊겨도 핵심 방은 산다", w.Net.Rings.Count > 0 && life.PowerLinked,
                $"보조 간선 {w.Net.Rings.Count}줄 · 생명유지실 {(life.PowerLinked ? "전기 있음" : "정전")} · 정전된 방 {dark}곳");
        }
        // 작은 배는 겪고 나서 깐다: 간선 정전 → 회의 → 보조 간선 공사
        {
            var w = DayOne(seed, "Mirinae");
            var power = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Room;
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Power && l.Door != null && (l.Door.RoomA == power || l.Door.RoomB == power))) w.Net.Hurt(l, 1f, "시험");
            w.Net.Update(0f);
            int before = w.Net.Rings.Count;
            Run(w, SimTime.TicksPerDay * 6);
            Check("작은 배 — 간선 정전을 겪으면 승무원이 보조 간선을 깐다", before == 0 && w.Net.Rings.Count > 0,
                $"보조 간선 {before} → {w.Net.Rings.Count} · 간선 정전 {w.Net.Stats.Blackouts} · " + string.Join(" / ", w.History.Events.Where(e => e.Text.Contains("보조 간선")).Select(e => e.Text).Take(2)));
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
