using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>관찰 장면 'forecast' (docs/UI2.md §8 · U8): 주컴퓨터가 한 예측과 실제 — 봉쇄 상상 (시험 배) · 미리 돌려 보기 · 걸음 시간 (매뉴얼 배) · 물자 날 (디딤호 하루 반) · 장부 줄과 맞음 몫.</summary>
public static partial class Program
{
    private static void ForecastScene(string dir)
    {
        void Print(string title, ForecastBook book)
        {
            Console.WriteLine($"  [{title}]");
            foreach (var fc in book.Closed.Concat(book.Open))
            {
                var g = fc.G;
                string guess = g.Kind switch
                {
                    GuessKind.Seal => $"{(g.Verdict == 1 ? "지금 닫음" : "기다림")} · 죽음 {g.A:0} 쓰러짐 {g.B:0} 다른 방 −{g.C:0.0}kPa",
                    GuessKind.Rehearse => $"{(g.Verdict == 1 ? "연다" : "하지 않는다")} · 쓰러짐 +{g.A:0} 잃는 공기 {g.B:0}몰",
                    GuessKind.Step => $"걸릴 분 {g.A:0.#}",
                    GuessKind.Supply => $"식량 {g.A:0.#}일 · 물 {g.B:0.#}일",
                    _ => $"{g.A / 60:0.#}분 뒤 닿는다",
                };
                string real = fc.State is ForecastState.Right or ForecastState.Wrong or ForecastState.Interfered
                    ? g.Kind switch { GuessKind.Seal => $"죽음 {fc.ActA:0} 쓰러짐 {fc.ActB:0} −{fc.ActC:0.0}kPa", GuessKind.Rehearse => $"쓰러짐 +{fc.ActA:0} 잃은 공기 {fc.ActB:0}몰", GuessKind.Step => $"{fc.ActA:0.#}분", GuessKind.Supply => $"하루 식량 {fc.ActA:0}몰 (예측 {(g.A > 0 && g.A < 999 ? fc.FoodThen / g.A : 0):0}) · 물 {fc.ActB:0}몰 (예측 {(g.B > 0 && g.B < 999 ? fc.WaterThen / g.B : 0):0})", _ => $"{fc.ActA / 60:0.#}분" }
                    : "";
                Console.WriteLine($"    {Time.Clock(g.Tick)} {ForecastBook.KindName(g.Kind)}: {guess}{(real.Length > 0 ? " / 실제 " + real : "")} → {fc.State}{(fc.Why.Length > 0 ? " (" + fc.Why + ")" : "")}");
            }
            var (r, n) = book.Summary();
            Console.WriteLine($"    맞음 {r}/{n} · 센서 선 {book.SensorWireRight}/{book.SensorWire} · 무선 {book.SensorRadioRight}/{book.SensorRadio} · 못 봄 {book.SensorBlind}");
        }

        // 봉쇄 상상 (시험 배 — RulesComputer '상상 (가)')
        {
            var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int a = m.AddPerson(s, At(6, 2)), b = m.AddPerson(s, At(13, 2));
            foreach (int p in new[] { a, b }) { ref var body = ref life[m[p].C.Body]; body.Conscious = 0; body.Hypoxia = 0.6f; }
            s.Get<Structure>().RequestDoor(lay.DoorAt[At(10, 3)], 1); s.Run(3);
            var show = new LiveShow(s, "시험호");
            s.Get<Field>().SetHole(At(0, 2), 0.05f);
            for (int k = 0; k < Time.Minutes(10); k++) { s.Step(); show.Book.Observe(s); }
            Print("봉쇄 상상 — 뚫린 방과 열린 문 너머에 쓰러진 사람", show.Book);
        }
        // 미리 돌려 보기 · 걸음 시간 (매뉴얼 배 — 진공이 될 방 · 지어낸 계획)
        {
            var legend = new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['h'] = "HeatExchanger", ['k'] = "Shelf", ['w'] = "Workbench" };
            var ml = Worlds.Ascii(ManualShip, legend);
            var s = Worlds.Full(ml, 7);
            var m = s.Get<Mind>();
            foreach (int x in new[] { 16, 17, 18, 23 }) { int a = m.AddPerson(s, Worlds.At(ml, x, 1)); m.SetPersonality(a, 0.5f, 0.7f, 0.6f, 0.5f, 0.3f); m[a].C.KnPressure = 0.5f; }
            m.AddRobot(s, Worlds.At(ml, 22, 1), AgentKind.Robot, Worlds.At(ml, 22, 1));
            s.Run(2); s.Get<Network>().AirReserve = 2e6;
            var show = new LiveShow(s, "시험호");
            s.Get<Field>().SetHole(Worlds.At(ml, 3, 0), 0.02f);
            for (int k = 0; k < Time.Hours(2); k++) { s.Step(); show.Book.Observe(s); }
            Print("미리 돌려 보기 · 걸음 시간 — 진공이 된 방을 지어낸 계획으로", show.Book);
        }
        // 물자 날 (디딤호 하루 반 — 여섯 시간 뒤부터 한 시간마다 · 하루 뒤 맞댄다)
        {
            var show = LiveShow.Crewed("Didim", 18);
            var s = show.Sim;
            for (int k = 0; k < Time.Hours(31); k++) { s.Step(); show.Book.Observe(s); }
            Print("물자 날 — 디딤호 하루 반", show.Book);
        }
        Console.WriteLine($"  forecast: 장부 줄 (그림 없음 — 화면 사진은 게임 --ui=computer-breach:1)");
    }
}
