using System;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// v19 정보 경계 점검 (--knowtest): 주 컴퓨터가 아는 것은 값 · 출처 · 확인 시각 · 믿음 한 묶음 — 오래되면 모름.
/// 급수 유량은 정상인데 바닥이 젖었다 → 까닭을 여러 갈래로 의심 → 로봇 · 사람에게 확인 → 진단을 고친다 (틀린 유량계는 덜 믿고 교정).
/// 값이 멈춘 방 · 소식이 끊긴 방은 다시 읽어 보고 묻는다 (방 안의 사람은 손목 단말로 바로 답한다).
/// </summary>
public static partial class Program
{
    private static Room? PlumbedRoom(World w) =>
        w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.WaterLinked && r.Furniture.Any(f => f.Machine != null && Procedures.Plumbed(f.Type)) && r.Cells.Count >= 6)
            .OrderBy(r => r.Type == RoomType.Cooling || r.Type == RoomType.Reactor ? 1 : 0).ThenBy(r => r.Id).FirstOrDefault();

    private static void Seep(World w, Room r, float lph)
    {
        r.SeepLph = lph;
        r.SeepSince = w.Tick;
        r.SeepAt = r.Cells.Where(c => w.Ship.IsWalkable(c)).OrderBy(c => (c.Center - r.Center).LengthSquared()).First();
    }

    private static void Meter(Room r, float cal)
    {
        foreach (var f in r.Furniture) if (f.Machine is Machine m && Procedures.Plumbed(f.Type)) m.SensorCal = cal;
    }

    private static int RunKnowTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"정보 경계 점검 (v19) · 시드 {seed}\n");

        // 1) 장부: 방마다 값 · 출처 · 확인 시각 · 믿음 · 데이터선이 끊기면 믿음이 옅어져 "모름"
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var fx = w.Automation.Facts;
            var live = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).ToList();
            int full = live.Count(r => new[] { FactKind.People, FactKind.O2, FactKind.Pressure, FactKind.Temp, FactKind.Wet }.All(k => fx.Get(r, k).At >= 0 && fx.Get(r, k).From != FactFrom.None && fx.Belief(r, k) > 0.8f));
            var room = live.Where(r => r.Type == RoomType.Storage || r.Type == RoomType.Quarters).OrderByDescending(r => r.Type == RoomType.Storage).ThenBy(r => r.Id).First();
            w.Automation.Belief.Break(room, SensorFault.Stuck, "시험 — 값 멈춤"); // 값이 멈췄다 (아무도 없는 방이라 묻지는 않는다)
            Run(w, SimTime.Hours(5.75f)); // 여섯 시간이 넘으면 자동 교정 모듈이 스스로 잡는다
            bool unknown = fx.Unknown(room, FactKind.O2);
            string line = fx.Line(room, FactKind.O2);
            Check("장부 — 방마다 값 · 출처 · 확인 시각 · 믿음이 한 묶음이고, 값이 멈춘 채 오래되면 믿음이 옅어져 \"모름\"이 된다", full == live.Count && unknown && line.Contains("?") && line.Contains("전"),
                $"갖춘 방 {full}/{live.Count} · {room.Name} 다섯 시간 반 뒤: {line}");
        }

        // 2) 틀어진 유량계 + 작은 누수: 컴퓨터는 모른다 → 바닥이 젖는다 → 의심(여러 갈래) → 로봇이 가서 본다 → 진단 고침 · 유량계 교정 · 땜질
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var fx = w.Automation.Facts;
            var r = PlumbedRoom(w)!;
            Meter(r, 0.45f);
            Seep(w, r, 1.6f);
            Run(w, SimTime.Minutes(30));
            bool blindAtFirst = !fx.SeepKnown(r) && fx.Get(r, FactKind.Flow).Value < 0.5f;
            FactDoubt? d = null;
            for (int i = 0; i < 24 * 6 && d == null; i++) { Run(w, SimTime.Minutes(10)); d = fx.Open.Concat(fx.Done).FirstOrDefault(x => x.RoomId == r.Id && x.Kind == "wet"); }
            string hyps = d == null ? "-" : string.Join(" · ", d.H.Select(h => $"{h.Name} {h.P * 100:0}%"));
            Check("어긋남 — 틀어진 유량계는 작은 누수를 못 잡는다 → 바닥이 젖자 \"유량은 정상인데 젖었다\" → 여러 갈래로 의심 (누수 · 결로 · 옆방 · 감지기)", blindAtFirst && d != null && d.H.Count >= 3 && d.H.Any(h => h.Key == "seep"),
                $"{r.Name} · 처음엔 모름 {blindAtFirst} · {d?.Symptom} · {hyps}");
            for (int i = 0; i < 6 * 8 && (d == null || d.Closed < 0); i++) Run(w, SimTime.Minutes(10));
            bool robotLooked = d != null && d.Finding.Contains("로봇") || d != null && d.Asked < 0 && d.Closed >= 0;
            Check("확인 — 로봇이 먼저 가서 보고, 본 대로 진단을 고친다 (스밈 · 유량계가 틀어져 못 잡았다)", d != null && d.Truth == "seep" && d.State is "고침" or "맞음" && fx.MeterSuspect(r),
                $"{d?.State} · {d?.Finding} · 유량계 의심 {fx.MeterSuspect(r)} · 로봇이 봄 {robotLooked}");
            bool cal = false, sealedUp = false;
            for (int i = 0; i < 6 * 24 && !(cal && sealedUp); i++)
            {
                Run(w, SimTime.Minutes(10));
                cal |= r.Furniture.Where(f => f.Machine != null && Procedures.Plumbed(f.Type)).All(f => f.Machine!.SensorCal >= 0.85f);
                sealedUp |= r.SeepLph <= 0f;
            }
            Check("고침 — 틀린 유량계는 교정하고 스미는 관은 땜질한다 (하루 안)", cal && sealedUp, $"교정 {cal} · 땜질 {sealedUp} · 남은 물 {MoistureSystem.DepthCm(r):0.0}cm");
        }

        // 3) 맞는 유량계 + 같은 누수: 유량계가 바로 잡는다 → 묻지 않고 땜질 (계기를 믿을 때는 확인을 아낀다)
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var fx = w.Automation.Facts;
            var r = PlumbedRoom(w)!;
            Meter(r, 1f);
            Seep(w, r, 1.6f);
            int asks0 = fx.Asks;
            bool known = false;
            for (int i = 0; i < 12 && !known; i++) { Run(w, SimTime.Minutes(10)); known = fx.SeepKnown(r); }
            bool sealedUp = false;
            for (int i = 0; i < 6 * 12 && !sealedUp; i++) { Run(w, SimTime.Minutes(10)); sealedUp = r.SeepLph <= 0f; }
            Check("계기를 믿을 때 — 교정된 유량계는 시간당 1L 넘는 샘을 바로 잡고 (출처: 유량계), 묻지 않고 땜질한다", known && sealedUp && !fx.Done.Concat(fx.Open).Any(x => x.RoomId == r.Id && x.Kind == "wet"),
                $"{r.Name} · 앎 {known} · {fx.Line(r, FactKind.Flow)} · 땜질 {sealedUp} · 묻기 {fx.Asks - asks0}");
        }

        // 4) 로봇이 없을 때: 사람에게 손목 단말로 부탁한다 → 가서 본다 → 엎지른 물 / 결로처럼 새지 않는 물이면 땜질하지 않는다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            foreach (var rb in w.Robots.Robots) rb.Disabled = true;
            var fx = w.Automation.Facts;
            var r = PlumbedRoom(w)!;
            Meter(r, 1f);
            FactDoubt? d = null;
            for (int i = 0; i < 6 * 10 && (d == null || d.Closed < 0); i++)
            {
                if (d == null || d.Closed < 0) r.Flood = MathF.Max(r.Flood, r.Cells.Count * 20f * 0.045f); // 바닥에 물 (스미는 데는 없다) — 확인하러 올 때까지 젖어 있다
                Run(w, SimTime.Minutes(10));
                d = fx.Open.Concat(fx.Done).FirstOrDefault(x => x.RoomId == r.Id && x.Kind == "wet");
            }
            var asked = d != null && d.Asked >= 0 ? w.Crew[d.Asked] : null;
            bool wrist = asked != null && w.Log.Entries.Any(e => e.Text.StartsWith("[손목 단말]") && e.Text.Contains(asked.Name) && e.Text.Contains(r.Name));
            Check("사람에게 — 로봇이 없으면 손목 단말로 사람에게 부탁하고, 그 사람이 가서 본 대로 답한다 (새지 않는 물이면 땜질 일을 만들지 않는다)", d != null && asked != null && wrist && d.Truth is "spill" or "cond" or "near" && r.SeepLph <= 0f && !w.Board.Open.Any(o => o.Kind == WorkKind.SealSeep),
                $"{d?.State} · {asked?.Name} · {d?.Finding} · 손목 단말 {wrist}");
        }

        // 5) 모름 → 확인 → 물음: 사람이 있는 방의 산소 감지기 값이 멈췄다 → 다시 읽어도 같다 → 방 안 사람에게 손목 단말로 → 그 자리에서 다시 켠다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var fx = w.Automation.Facts;
            var r = w.Ship.LiveRooms.Where(x => x.Type is RoomType.Quarters or RoomType.Mess or RoomType.Lounge && x.Cells.Count >= 6).OrderBy(x => x.Id).First();
            var who = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderBy(c => c.Id).First();
            who.Position = r.Center; who.PreviousPosition = r.Center; who.Room = r;
            Run(w, SimTime.Minutes(5));
            w.Automation.Belief.Break(r, SensorFault.Stuck, "시험 — 값 멈춤");
            FactDoubt? d = null;
            for (int i = 0; i < 6 * 16 && (d == null || d.Closed < 0); i++)
            {
                Run(w, SimTime.Minutes(10));
                if (who.Room != r && who.IsAwake && who.Job?.Urgent != true) { who.Position = r.Center; who.PreviousPosition = r.Center; who.Room = r; } // 그 방에 머문다
                d = fx.Done.Concat(fx.Open).FirstOrDefault(x => x.RoomId == r.Id && x.Kind == "stale");
            }
            Check("모름 — 사람이 있던 방의 산소 값이 멈추면 다시 읽어 보고, 안 되면 방 안 사람에게 손목 단말로 묻고 그 자리에서 감지기를 다시 켠다", d != null && d.Closed >= 0 && d.Truth == "sensor" && w.Automation.Belief.Of(r).Fault == SensorFault.None && fx.Get(r, FactKind.O2).From is FactFrom.Crew or FactFrom.Sensor && !fx.Unknown(r, FactKind.O2),
                $"{r.Name} · {d?.Symptom} → {d?.Finding} · 지금 {fx.Line(r, FactKind.O2)} · 손목 단말 답 {fx.WristAnswers}");
        }

        // 6) 배우기: 같은 갈래가 거듭 맞으면 다음 짐작에서 그 갈래가 무거워진다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var fx = w.Automation.Facts;
            fx.Lessons["spill"] = (4, 4);
            fx.Lessons["seep"] = (0, 4);
            var r = PlumbedRoom(w)!;
            Meter(r, 0.45f);
            FactDoubt? d = null;
            for (int i = 0; i < 30 && d == null; i++) { r.Flood = MathF.Max(r.Flood, r.Cells.Count * 20f * 0.045f); Run(w, SimTime.Minutes(10)); d = fx.Open.Concat(fx.Done).FirstOrDefault(x => x.RoomId == r.Id && x.Kind == "wet"); }
            var spill = d?.H.FirstOrDefault(h => h.Key == "spill");
            var seep = d?.H.FirstOrDefault(h => h.Key == "seep");
            Check("배우기 — 지난 확인에서 자주 맞은 갈래(엎지른 물)는 무거워지고, 자주 틀린 갈래(누수)는 가벼워진다", d != null && spill != null && seep != null && spill.P > 0.15f && seep.P < 0.4f,
                d == null ? "의심 없음" : string.Join(" · ", d.H.Select(h => $"{h.Name} {h.P * 100:0}%")));
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 정보 경계" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>--knowprobe: 평소 항해에서 컴퓨터가 얼마나 묻나 (의심 · 모름 · 맞음/고침 · 스밈 · 손목 단말).</summary>
    private static int RunKnowProbe(int seed, int days, string? ship)
    {
        var w = World.CreateDefault(seed, 0, ship ?? "Saeteo");
        var fx = w.Automation.Facts;
        for (int d = 1; d <= days; d++)
        {
            Run(w, SimTime.TicksPerDay);
            Console.WriteLine($"  {d}일째 · 묻기 {fx.Asks} · 답 {fx.Answers} (손목 단말 {fx.WristAnswers}) · 맞음 {fx.Rights} · 고침 {fx.Fixes} · 모름 확인 {fx.StaleAsks} · 스밈 생김 {w.Moisture.Stats.Seeps} · 사람이 봄 {fx.SeepsSeen} · 틀린 유량계 {fx.MeterFound} · 열린 {fx.Open.Count}");
        }
        foreach (var x in fx.Done.TakeLast(12)) Console.WriteLine($"    {SimTime.Clock(x.Since)} {w.Ship.Rooms[x.RoomId].Name} [{x.Kind}] {x.State} · {x.Symptom} → {x.Finding}");
        return 0;
    }
}
