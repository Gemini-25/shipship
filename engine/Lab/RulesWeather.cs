using ShipEngine;

namespace Lab;

/// <summary>우주 날씨 대비 (⑤ · ⑩ · ⑧): 파편 구름 앞에서 문을 닫고 대피실로 · 양성자 폭풍에는 방사선이 가장 덜 드는 방으로 · 믿지 않는 이는 흘려듣는다 (docs/ENGINE.md ⑩ 우주 날씨 대비).</summary>
public static partial class Program
{
    private const string WeatherShip = @"
##############################
#..e.......#..R....#.........#
#..........D..c.pb.D.........#
#..e.......#.......#.........#
##############################";

    private static void WeatherRules()
    {
        if (!Do("우주 날씨")) return;
        Console.WriteLine("\n[⑤ · ⑩ · ⑧ 우주 날씨 대비]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['R'] = "SensorArray" };   // 센서 배열 — 바깥에서 아는 것은 센서가 잡은 것만 (⑫)
        var lay = Worlds.Ascii(WeatherShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        double Kpa(Sim s, int x, int y) => s.Layout.Rooms[s.Layout.Room[At(x, y)]].Cells.Average(c => s.Get<Field>().Pressure(c));

        // ── 파편 구름: 왼쪽에서 12분 뒤 닿는 파편 폭풍 — 주컴퓨터가 바람 아래쪽(오른쪽) 방을 대피실로 고르고 열어 둔 해치를 닫는다 · 왼쪽 방에서 자던 이 · 깨어 있던 이가 닿기 전에 모인다 /
        //    닿는 순간 왼쪽 방 외판이 뚫리면 왼쪽 방만 빠지고 대피실은 그대로 — 대피하지 않은 배(먼지 구름 · 문이 열린 채)는 대피실이 될 방까지 공기가 빠진다 ──
        {
            (bool gathered, int shelter, bool doorsShut, double rightKpa, double leftKpa, long shelters, string log) Run(string kind)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var f = s.Get<Field>(); var soc = s.Get<Society>(); var life = s.Get<Life>();
                int a = m.AddPerson(s, At(3, 1)), b = m.AddPerson(s, At(3, 3));
                s.Run(5);
                foreach (int p in new[] { a, b }) { soc.TrustComputer[p] = 0.9f; m[p].C.Diligent = 0.8f; }
                life[m[b].C.Body].Asleep = 1; m[b].C.Sleepy = 0.9f;
                int prop = s.Layout.DoorAt[At(11, 2)];
                f.SetDoor(prop, 1); s.Get<Structure>().RequestDoor(prop, 1);   // 평소처럼 열어 둔 해치
                long t0 = s.Tick;
                int cause = s.Causes.Add(CauseKind.Setup, "파편 폭풍이 항로로 들어온다", s.Tick);
                o.AddCloud(kind, 10000 * 12 * 60 + 400_000, 400_000, 0, 1, 0, cause);   // 입자 밀도 0 — 맞는 일은 시험이 정한다
                for (int k = 0; k < Time.Minutes(12); k++) s.Step();
                int right = s.Layout.Room[At(25, 2)];
                bool gathered = new[] { a, b }.All(p => m.RoomOf(m[p].C.Cell) == right);
                bool shut = f.DoorOpen[prop] <= 0.01f;
                int shelter = sm.ShelterRoomAt;
                f.SetHole(At(0, 2), 0.02f);   // 파편이 왼쪽 외판을 뚫었다
                for (int k = 0; k < Time.Minutes(3); k++) s.Step();
                string log = string.Join(" / ", sm.Log.Where(l => l.tick >= t0).Select(l => l.text).Take(8));
                return (gathered, shelter, shut, Kpa(s, 25, 2), Kpa(s, 5, 2), sm.Shelters, log);
            }
            var noneT = Task.Run(() => Run("dust")); var yes = Run("kessler"); var none = noneT.Result;
            int right = lay.Room[At(25, 2)];
            if (Debug) Console.WriteLine($"    {yes.log}\n    먼지: {none.log}");
            Check("파편 구름: 12분 뒤 닿는 파편 폭풍 — 주컴퓨터가 바람 아래쪽 방을 대피실로 고르고 열어 둔 해치를 닫는다 · 자던 이도 깨어 닿기 전에 모인다 / 왼쪽 외판이 뚫려도 대피실은 그대로 · 대피하지 않은 배는 그 방까지 빠진다",
                yes.shelters == 1 && yes.shelter == right && yes.gathered && yes.doorsShut && yes.rightKpa > 90 && none.shelters == 0 && !none.doorsShut && none.rightKpa < yes.rightKpa - 10,
                $"대피 {yes.shelters} · 대피실 {(yes.shelter >= 0 ? lay.Rooms[yes.shelter].Name : "-")} · 모임 {yes.gathered} · 열어 둔 해치를 닫음 {yes.doorsShut} · 3분 뒤 대피실 {yes.rightKpa:0}kPa · 뚫린 방 {yes.leftKpa:0} / 먼지 구름(대피 없음): 해치 닫힘 {none.doorsShut} · 그 방 {none.rightKpa:0}kPa · 뚫린 방 {none.leftKpa:0}");
        }

        // ── 양성자 폭풍: X선 조짐에 방사선이 가장 덜 드는 방(칸마다 선량 평균이 가장 낮은 방)으로 모인다 · 열어 둔 해치는 그대로 (몇 시간 — 공조가 돈다) · 폭풍이 그치면 푼다 ──
        {
            var s = Worlds.Full(lay, 8);
            var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var f = s.Get<Field>(); var soc = s.Get<Society>();
            int a = m.AddPerson(s, At(3, 1)), b = m.AddPerson(s, At(25, 3));
            s.Run(5);
            foreach (int p in new[] { a, b }) { soc.TrustComputer[p] = 0.9f; m[p].C.Diligent = 0.8f; }
            int prop = s.Layout.DoorAt[At(19, 2)];
            f.SetDoor(prop, 1); s.Get<Structure>().RequestDoor(prop, 1);
            o.ScheduleFlare(1, s.Causes.Add(CauseKind.Setup, "별의 표면이 끓어오르기 시작했다", s.Tick));
            for (int k = 0; k < Time.Minutes(8); k++) s.Step();
            int want = -1; double low = double.MaxValue;
            for (int r = 0; r < s.Layout.Rooms.Count; r++) { double mean = s.Layout.Rooms[r].Cells.Average(c => (double)o.Dose[c]); if (mean < low) { low = mean; want = r; } }
            int shelter = sm.ShelterRoomAt;
            bool gathered = new[] { a, b }.All(p => m.RoomOf(m[p].C.Cell) == shelter);
            bool open = f.DoorOpen[prop] > 0.5f && sm.ShelterDoorsShut == 0;
            for (int k = 0; k < Time.Hours(12) && sm.ShelterOn != 0; k++) s.Step();
            Check("양성자 폭풍: X선 조짐에 방사선이 가장 덜 드는 방으로 모인다 · 열어 둔 해치는 그대로 · 폭풍이 그치면 푼다",
                shelter == want && shelter >= 0 && gathered && open && sm.ShelterOn == 0 && m.ShelterRoom < 0,
                $"대피실 {(shelter >= 0 ? s.Layout.Rooms[shelter].Name : "-")} (선량이 가장 낮은 방 {(want >= 0 ? s.Layout.Rooms[want].Name : "-")}) · 모임 {gathered} · 문 열림 {open} · 끝에 풂 {sm.ShelterOn == 0}");
        }

        // ── 흘려듣는 이: 주컴퓨터를 믿지 않는 덜 성실한 이는 대피 방송을 흘려듣는 일이 많다 (원인 마디) · 믿는 성실한 이는 거의 따른다 ──
        {
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>(); var o = s.Get<Outside>(); var soc = s.Get<Society>();
            var low = new List<int>(); var high = new List<int>();
            for (int i = 0; i < 16; i++) { int p = m.AddPerson(s, At(2 + i, 2 + (i % 2) - (i % 3 == 0 ? 1 : 0))); (i % 2 == 0 ? low : high).Add(p); }
            s.Run(5);
            foreach (int p in low) { soc.TrustComputer[p] = 0.05f; m[p].C.Diligent = 0.1f; }
            foreach (int p in high) { soc.TrustComputer[p] = 0.9f; m[p].C.Diligent = 0.9f; }
            long f0 = s.Facts.Total;
            o.AddCloud("kessler", 10000 * 10 * 60 + 400_000, 400_000, 0, 1, 0, s.Causes.Add(CauseKind.Setup, "파편 폭풍이 항로로 들어온다", s.Tick));
            for (int k = 0; k < Time.Minutes(2); k++) s.Step();
            var ignored = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Decision && x.Other == (int)GoalKind.Shelter && x.Size < 0).Select(x => x.Actor).ToHashSet();
            int lowIgn = low.Count(ignored.Contains), highIgn = high.Count(ignored.Contains);
            var one = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Decision && x.Other == (int)GoalKind.Shelter && x.Size < 0);
            string story = one.Kind != FactKind.None ? s.Causes.Story(one.Cause) : "";
            Check("흘려듣는 이: 주컴퓨터를 믿지 않는 덜 성실한 이는 대피 방송을 흘려듣는 일이 많다 (원인 — 대피 알림 → 흘려들었다) · 믿는 성실한 이는 거의 따른다",
                lowIgn >= 3 && highIgn <= 1 && story.Contains("흘려들었다") && story.Contains("파편 폭풍"),
                $"믿지 않는 이 8명 중 흘려들음 {lowIgn} · 믿는 이 8명 중 {highIgn} · {story}");
        }
    }
}
