using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>야간 조명 규칙 시험 — 주컴퓨터가 밤에 침실 · 복도를 낮추고 일하는 방은 그대로 · 위험한 방은 환하게 (그래픽 결정 8 · docs/GRAPHICS.md).</summary>
public static partial class Program
{
    private static void LightRules()
    {
        if (!Do("야간 조명")) return;
        Console.WriteLine("\n[야간 조명]");

        // ── 판단: 방 종류 · 시각 · 위험으로 조명 몫 ──
        {
            float q = ShipMind.LightPolicy("Quarters", 23, false), c = ShipMind.LightPolicy("Corridor", 2, false), b = ShipMind.LightPolicy("Bridge", 23, false);
            float morning = ShipMind.LightPolicy("Quarters", 6.5, false), smoky = ShipMind.LightPolicy("Corridor", 23, true), day = ShipMind.LightPolicy("Corridor", 14, false);
            Check("야간 조명의 판단: 밤 침실 3% · 복도 30% · 함교 그대로 · 아침 6시 넘으면 환하게 · 위험한 방은 밤에도 환하게 · 낮은 그대로",
                q == ShipMind.NightQuarters && c == ShipMind.NightCommon && b == 1 && morning == 1 && smoky == 1 && day == 1,
                $"침실 23시 {q} · 복도 2시 {c} · 함교 23시 {b} · 침실 6시 반 {morning} · 연기 찬 복도 23시 {smoky} · 복도 14시 {day}");
        }

        // ── 디딤호 0시 20분: 침실은 밤등 · 복도는 낮게 · 함교는 그대로 / 복도에 연기가 차면 10분 안에 환하게 ──
        {
            var s = ShipWorld.Create("Didim", 3);
            var L = s.Layout; var light = s.Get<Light>(); var f = s.Get<Field>();
            s.Run(Time.Minutes(20));
            int Room(string type) => L.Rooms.FindIndex(r => r.Type == type);
            int quarters = Room("Quarters"), corridor = Room("Corridor"), bridge = Room("Bridge");
            float lq = light.Lamp[quarters], lc = light.Lamp[corridor], lb = bridge >= 0 ? light.Lamp[bridge] : 300;
            foreach (int cell in L.Rooms[corridor].Cells) f.AddGas(cell, f.T.gSoot, f.Moles(cell) * 0.0002, f.Temp(cell));   // 200ppm 연기
            s.Run(Time.Minutes(12));
            float lc2 = light.Lamp[corridor];
            Check("디딤호 0시 20분: 침실은 밤등(30럭스 아래) · 복도 낮게(60~120럭스) · 함교 그대로(300) / 복도에 연기가 차면 주컴퓨터가 다시 환하게 (300)",
                lq < 30 && lc is > 60 and < 120 && lb > 290 && lc2 > 290,
                $"침실 {lq:0}럭스 · 복도 {lc:0} · 함교 {lb:0} · 연기 찬 복도 {lc2:0} · 낮춘 방 {s.Get<ShipMind>().LampsDimmed}");
        }

        // ── 비상 전지 (그래픽 결정 7의 세계 쪽) · 화면 조명 — 한 세계에서 차례로 (짧게: 1시간 20분) ──
        //    그림은 세계를 바꾸지 않는다 · 밤 침실은 환할 때보다 어둡다 · 정전되면 방마다 30럭스 붉은 비상등 (그림도 붉다) · 전지가 다한 방은 어둡다 · 전기가 돌아오면 다시 찬다
        {
            var s = ShipWorld.Create("Didim", 3);
            var L = s.Layout; var light = s.Get<Light>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            var painter = new Painter(L);
            var busy = new HashSet<int>(L.Furniture.SelectMany(f => f.Cells));
            (double r, double g, double b) Mean(string type)
            {
                var room = L.Rooms.First(r => r.Type == type);
                int x0 = room.Cells.Min(L.X), y0 = room.Cells.Min(L.Y), x1 = room.Cells.Max(L.X), y1 = room.Cells.Max(L.Y);
                var img = painter.Frame(s, x0, y0, x1 - x0 + 1, y1 - y0 + 1, 0);
                double r = 0, g = 0, b = 0; int n = 0;
                foreach (int c in room.Cells)
                {
                    if (busy.Contains(c) || L.Kind[c] != CellKind.Floor) continue;
                    var p = img.Get((L.X(c) - x0) * Painter.T + Painter.T / 2, (L.Y(c) - y0) * Painter.T + Painter.T - 6);
                    r += p.R; g += p.G; b += p.B; n++;
                }
                return n > 0 ? (r / n, g / n, b / n) : (0, 0, 0);
            }
            static double Sum((double r, double g, double b) c) => c.r + c.g + c.b;
            s.Run(Time.Hours(1));   // 밤 1시 (틱 1500 — 주컴퓨터의 조명 판단은 틱 %250 == 11)
            var before = s.Save();
            var night = Mean("Quarters");
            bool same = before.AsSpan().SequenceEqual(s.Save());
            for (int r = 0; r < L.Rooms.Count; r++) net.SetLampDim(r, 1);   // 환한 때와 견준다 (아침처럼)
            s.Run(2);
            var day = Mean("Quarters"); var lit = Mean("Mess");
            int mess = L.Rooms.FindIndex(r => r.Type == "Mess"), lounge = L.Rooms.FindIndex(r => r.Type == "Lounge");
            var keep = sm.Policy; var pol = keep; pol.ResetBreakers = 0; sm.Policy = pol;   // 차단기를 내린 채로 둔다 (장면 차리기)
            net.EmergencyMin[lounge] = 3;                                                     // 쉼터의 전지는 거의 다 닳았다
            for (int fd = 0; fd < net.Breaker.Length; fd++) net.Breaker[fd] = 0;
            s.Run(Time.Minutes(1));
            float m1 = light.Lamp[mess], l1 = light.Lamp[lounge]; byte st1 = net.LampState(mess);
            var dark = Mean("Mess");
            s.Run(Time.Minutes(5));
            float l2 = light.Lamp[lounge]; byte st2 = net.LampState(lounge);
            for (int fd = 0; fd < net.Breaker.Length; fd++) net.ResetBreaker(fd);
            sm.Policy = keep;
            s.Run(Time.Minutes(12));
            byte st3 = net.LampState(lounge); float charge = net.EmergencyMin[lounge];
            Check("정전되면 비상등 30럭스 · 전지가 다한 방은 어둡다 · 전기가 돌아오면 다시 켜지고 전지가 찬다 (6배 느리게)",
                m1 == Network.EmergencyLux && st1 == 2 && l1 == Network.EmergencyLux && l2 == 0 && st2 == 0 && st3 == 1 && charge > 1.5f && charge < 3,
                $"식당 1분 {m1:0}럭스(상태 {st1}) · 쉼터 1분 {l1:0} → 6분 {l2:0}(상태 {st2}) · 복구 12분 상태 {st3} · 전지 {charge:0.0}분");
            // 그리기 시간 (60프레임 — 관전 화면 크기 41×24칸 · 조명 켬): 예전 도트마다 섞기는 34ms였다 → 빠른 얹기 · 고정 소수 곱하기로 6~7ms
            var mroom = L.Rooms[mess];
            int bx = Math.Clamp((int)mroom.Cells.Average(L.X) - 20, 0, Math.Max(0, L.W - 41)), by = Math.Clamp((int)mroom.Cells.Average(L.Y) - 12, 0, Math.Max(0, L.H - 24));
            ShipSim.Art.Img? into = null;
            for (int i = 0; i < 5; i++) into = painter.Frame(s, bx, by, 41, 24, i, into);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) into = painter.Frame(s, bx, by, 41, 24, i, into);
            double ms = sw.Elapsed.TotalMilliseconds / 20;
            Check("그리기 시간: 관전 화면 한 장(41×24칸 · 조명 켬)이 25ms 아래 (시험 갈래 넷이 함께 돌 때도 — 혼자면 6~7ms)", ms < 25, $"{ms:0.0}ms/장");
            Check("화면 조명: 그림은 세계를 바꾸지 않는다 · 밤 침실은 환할 때보다 어둡다 · 정전된 식당은 붉고 어둡다 (비상등)",
                same && Sum(night) < 0.6 * Sum(day) && dark.r > 1.8 * dark.g && Sum(dark) < 0.6 * Sum(lit),
                $"같은 세계 {same} · 침실 밤 {night.r:0}/{night.g:0}/{night.b:0} · 환할 때 {day.r:0}/{day.g:0}/{day.b:0} · 정전 식당 {dark.r:0}/{dark.g:0}/{dark.b:0} · 켠 식당 {lit.r:0}/{lit.g:0}/{lit.b:0}");
        }
    }
}
