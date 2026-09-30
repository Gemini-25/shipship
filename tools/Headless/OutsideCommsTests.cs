using System;
using System.Linq;
using ShipSim.Core;

// v11.2 외부 교신: 조난 신호·보급 캡슐, 탈출 캡슐 구조
public static partial class Program
{
    private static int RunOutsideCommsTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"외부 교신 점검 (v11.2) · 시드 {seed}\n");

        // ── 1) 조난 신호: 공기 탱크가 바닥나면 회의로 신호를 보내고, 보급 캡슐이 와서 짐을 내린다 ──
        {
            var w = DayOne(seed, "Hanbit");
            w.Air.Reserve = w.Air.ReserveCapacity * 0.12f;
            float air0 = w.Air.Reserve;
            int sealant0 = w.Board.Have(ItemKind.Sealant);
            long sent = -1, docked = -1;
            for (int t = 0; t < SimTime.TicksPerDay * 4 && w.Comms.Supplies == 0; t++)
            {
                w.Step();
                if (sent < 0 && w.Comms.DistressAt >= 0) sent = w.Tick;
                if (docked < 0 && w.Comms.SupplyDocked) docked = w.Tick;
            }
            var vote = w.History.Events.FirstOrDefault(e => e.Kind == HistoryKind.Decision && e.Text.Contains("조난 신호"));
            Check("조난 신호 → 보급 캡슐 → 짐 내리기", sent >= 0 && docked >= 0 && w.Comms.Supplies == 1 && w.Air.Reserve > air0 + w.Air.ReserveCapacity * 0.2f && w.Board.Have(ItemKind.Sealant) > sealant0,
                $"신호 {(sent >= 0 ? $"{SimTime.Clock(sent)}" : "안 보냄")} · 도킹 {(docked >= 0 ? $"{(docked - sent) / (float)SimTime.TicksPerHour:0}시간 뒤" : "안 옴")} · 내림 {w.Comms.Supplies} · 공기 탱크 {air0 / w.Air.ReserveCapacity * 100:0}% → {w.Air.Reserve / w.Air.ReserveCapacity * 100:0}% · 실링폼 {sealant0} → {w.Board.Have(ItemKind.Sealant)}"
                + (vote != null ? $" · {vote.Text}" : ""));
        }

        // ── 2) 통신실을 잃은 배는 도움을 청할 수 없다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var comms = w.Sensors.CommsRoom!;
            foreach (var c in w.Crew.Where(c => c.Room == comms)) { c.Position = w.Ship.RoomsOf(RoomType.Corridor).First().Cells.First(w.Ship.IsOpenFloor).Center; c.PreviousPosition = c.Position; }
            w.Structure.Detach(comms, "교신 시험", controlled: true);
            comms.Wreck = true;
            w.Air.Reserve = w.Air.ReserveCapacity * 0.12f;
            Run(w, SimTime.TicksPerDay);
            Check("통신실을 잃은 배는 조난 신호를 못 보낸다", w.Comms.Distresses == 0 && w.Comms.Console == null, $"신호 {w.Comms.Distresses}번");
        }

        // ── 3) 구조 요청: 회의로 건지기로 하고, 생존자가 에어락으로 올라와 치료받는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            int crew0 = w.Crew.Count;
            Player.Hazard(w, HazardKind.RescueSignal, default);
            int survivors = w.Comms.SignalSurvivors;
            bool answered = false;
            for (int t = 0; t < SimTime.TicksPerDay * 2 && w.Comms.Rescued == 0; t++) { w.Step(); answered |= w.Comms.PodEta >= 0; }
            var joined = w.Crew.Where(c => c.Rescued).ToList();
            Run(w, SimTime.Hours(20));
            bool treated = joined.Count > 0 && joined.All(c => c.Vitals.TreatedTick > 0 || c.CareBed != null || c.Vitals.Injury < 0.2f);
            bool beds = joined.All(c => c.Bed != null);
            var vote = w.History.Events.FirstOrDefault(e => e.Kind == HistoryKind.Decision && e.Text.Contains("탈출 캡슐"));
            Check("구조 요청 → 건지기로 → 생존자 합류·치료", answered && w.Crew.Count == crew0 + survivors && treated && beds && joined.All(c => !c.Dead),
                $"생존자 {survivors}명 · 승무원 {crew0} → {w.Crew.Count} ({string.Join("·", joined.Select(c => $"{c.Name} {CrewRoles.Name(c.Role)} 부상 {c.Vitals.Injury * 100:0}%"))}) · 잠자리 {(beds ? "있음" : "없음")}"
                + (vote != null ? $" · {vote.Text}" : ""));
        }

        // ── 4) 구조한 배도 저장·불러오기가 같은 역사 ──
        {
            var w = DayOne(seed, "Hanbit");
            Player.Hazard(w, HazardKind.RescueSignal, default);
            Run(w, SimTime.Hours(36));
            uint h = SaveGame.StateHash(w);
            var runner = new ReplayRunner(SaveGame.Write(w));
            while (!runner.Advance(50000)) { }
            Check("구조한 배의 저장·불러오기", SaveGame.StateHash(runner.World) == h, $"승무원 {w.Crew.Count} · 지문 {(SaveGame.StateHash(runner.World) == h ? "같음" : "다름")}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 외부 교신 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
