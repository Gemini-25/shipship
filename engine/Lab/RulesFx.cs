using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>사고 효과 규칙 시험 (그래픽 결정 11) — 효과는 세계의 값 · 사실에서만 나온다: 폭발 충격파 · 합선 불꽃 · 방사선 화면 잡음 · 새는 공기 줄기.</summary>
public static partial class Program
{
    private static void FxRules()
    {
        if (!Do("사고 효과")) return;
        Console.WriteLine("\n[사고 효과]");
        const int W = 22, H = 14;
        (int x0, int y0) View(Layout L, int c) => (Math.Clamp(L.X(c) - W / 2, 0, Math.Max(0, L.W - W)), Math.Clamp(L.Y(c) - H / 2, 0, Math.Max(0, L.H - H)));
        static int Count(Img img, int px0, int py0, int pw, int ph, Func<Rgba, bool> pick)
        {
            int n = 0;
            for (int y = Math.Max(0, py0); y < Math.Min(img.H, py0 + ph); y++)
                for (int x = Math.Max(0, px0); x < Math.Min(img.W, px0 + pw); x++) if (pick(img.Get(x, y))) n++;
            return n;
        }
        int Cause(Sim s, string why) => s.Causes.Add(CauseKind.Setup, why, s.Tick);

        // ── 폭발: ④가 낸 큰 소리 사실(소리 종류 없음)에서 무게 · 닿는 칸을 되짚고 · 반 틱쯤 지나 그 반지름에 밝은 띠 ──
        {
            var s = ShipWorld.Create("Didim", 3);
            var L = s.Layout; var p = new Painter(L);
            var room = L.Rooms.First(r => r.Type == "Engine");
            int at = room.Cells[room.Cells.Length / 2];
            s.Run(5);
            var show = new LiveShow(s, "디딤호");
            var calmShake = show.Shake();
            s.Get<Structure>().Blast(s, at, 8e6, Cause(s, "폭발"));
            var blastShake = show.Shake();
            var bl = s.Facts.Recent(64).LastOrDefault(x => Painter.IsBlast(x));
            bool soundNotBlast = s.Facts.Recent(64).Where(x => x.Kind == FactKind.LoudSound && x.Other >= 0).All(x => !Painter.IsBlast(x));
            p.Sub = 0.4;
            var (x0, y0) = View(L, at);
            var img = p.Frame(s, x0, y0, W, H, 0);
            double reach = Painter.BlastReach(bl.Size), r = reach * Painter.T * 0.5;
            int cx = (L.X(at) - x0) * Painter.T + Painter.T / 2, cy = (L.Y(at) - y0) * Painter.T + Painter.T / 2;
            int ring = 0;
            for (int y = 0; y < img.H; y++)
                for (int x = 0; x < img.W; x++)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var c = img.Get(x, y);
                    if (Math.Abs(d - r) < 2.5 && c.R > 200 && c.G > 200 && c.B > 190) ring++;
                }
            s.Run(3);
            var afterShake = show.Shake();
            Check("폭발: 큰 소리 사실에서 TNT 무게 · 폭풍이 닿는 칸을 되짚는다 (8MJ → 1.9kg · 11칸) · 0.4틱에 그 반지름 절반에 밝은 띠 · 소리 엔진의 큰 소리는 폭발이 아니다 · 관전 화면이 흔들리고 (조용할 땐 0) · 두 틱 지나면 멎는다",
                bl.Kind == FactKind.LoudSound && Math.Abs(Painter.BlastKg(bl.Size) - 8e6 / 4.184e6) < 0.1 && reach is > 10 and < 12.5 && ring > 40 && soundNotBlast
                && calmShake == (0, 0) && blastShake != (0, 0) && afterShake == (0, 0),
                $"{bl.Size:0}dB · {Painter.BlastKg(bl.Size):0.00}kg · {reach:0.0}칸 · 띠 도트 {ring} · 흔들림 조용 {calmShake} · 폭발 {blastShake} · 뒤 {afterShake}");
        }

        // ── 카메라 연출 (결정 13): 먼 곳의 폭발 → 카메라가 미끄러지지 않고 끊어 넘긴다 (컷) · 결정적 순간엔 가까이 ──
        {
            var s = ShipWorld.Create("Didim", 3);
            s.Run(5);
            var L = s.Layout;
            var show = new LiveShow(s, "디딤호");
            for (int i = 0; i < 30; i++) show.Advance(1.0 / 60);
            var room = L.Rooms.First(r => r.Type == "Engine");
            int at = room.Cells[room.Cells.Length / 2];
            var far = L.Rooms.OrderByDescending(r => Math.Abs(L.X(r.Cells[0]) - L.X(at)) + Math.Abs(L.Y(r.Cells[0]) - L.Y(at))).First();
            show.CamX = L.X(far.Cells[0]); show.CamY = L.Y(far.Cells[0]); show.ViewW = 24; show.Follow = true;
            long cuts0 = show.Cuts;
            s.Get<Structure>().Blast(s, at, 8e6, Cause(s, "폭발"));
            double minW = 99; string moods = ""; bool near = false;
            for (int i = 0; i < 240; i++)
            {
                show.Advance(1.0 / 60);
                minW = Math.Min(minW, show.ViewW);
                if (!moods.Contains(show.View.Mood)) moods += show.View.Mood + " ";
                var (l, t, w, h) = show.Camera();
                near |= L.X(at) + 0.5 >= l && L.X(at) + 0.5 <= l + w && L.Y(at) + 0.5 >= t && L.Y(at) + 0.5 <= t + h;
            }
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    먼 방 {far.Name} ({L.X(far.Cells[0])},{L.Y(far.Cells[0])}) · 폭발 ({L.X(at)},{L.Y(at)}) · 연출 초점 {show.View.FocusCell}");
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    컷 {show.Cuts - cuts0} · 가장 가까이 {minW:0.0}칸 · 분위기 {moods}· 폭발이 화면 안 {near}");
            Check("카메라 연출: 먼 곳의 폭발로 카메라가 끊어 넘긴다 (컷) · 폭발이 화면에 담긴다 · 결정적 순간이면 18칸까지 가까이",
                show.Cuts > cuts0 && near && (!moods.Contains("결정적") || minW <= LiveShow.CloseUp + 1),
                $"컷 {show.Cuts - cuts0} · 가장 가까이 {minW:0.0}칸 · 분위기 {moods}· 폭발이 화면 안 {near}");
        }

        // ── 합선: 젖은 전선 칸 — '합선' 사실 2틱 안은 그 칸에 푸른 흰 불꽃 · 지나면 없다 (차단기가 떨어져 아크가 멎었다) ──
        {
            var s = ShipWorld.Create("Didim", 3);
            var L = s.Layout; var f = s.Get<Field>(); var net = s.Get<Network>(); var p = new Painter(L);
            s.Run(5);
            int mess = L.Rooms.FindIndex(r => r.Type == "Mess");
            var feeder = net.Feeders.First(fd => fd.Id == net.RoomFeeder[mess]);
            int wet = feeder.Path[feeder.Path.Length / 2];
            f.AddStore(wet, f.T.sWater, 30);
            for (int t = 0; t < 4 && s.Facts.CountOf(FactKind.Short) == 0; t++) s.Step();
            var sc = s.Facts.Recent(64).LastOrDefault(x => x.Kind == FactKind.Short);
            int cell = sc.Kind == FactKind.Short ? sc.Cell : wet;
            var (x0, y0) = View(L, cell);
            int px = (L.X(cell) - x0) * Painter.T, py = (L.Y(cell) - y0) * Painter.T;
            static bool Blue(Rgba c) => c.B > 225 && c.R > 130 && c.B >= c.R;
            int now = Count(p.Frame(s, x0, y0, W, H, 0), px - 8, py - 8, Painter.T + 16, Painter.T + 16, Blue);
            s.Run(3);
            int later = Count(p.Frame(s, x0, y0, W, H, 0), px - 8, py - 8, Painter.T + 16, Painter.T + 16, Blue);
            Check("합선: 젖은 전선 칸에 '합선' 사실 → 그 칸에 푸른 흰 불꽃 · 세 틱 뒤(차단기가 떨어졌다)엔 없다",
                sc.Kind == FactKind.Short && now > 4 && later < now / 3,
                $"합선 {(sc.Kind == FactKind.Short ? "있음" : "없음")} · 불꽃 도트 {now} → {later}");
        }

        // ── 방사선 화면 잡음: 칸 선량률이 1mGy/시를 넘을 때만 · 클수록 짙게 ──
        {
            var s = ShipWorld.Create("Didim", 3);
            var L = s.Layout; var o = s.Get<Outside>(); var p = new Painter(L);
            s.Run(2);
            var room = L.Rooms.First(r => r.Type == "Mess");
            int c1 = room.Cells.First(c => L.FurnitureAt[c] < 0), c2 = room.Cells.Last(c => L.FurnitureAt[c] < 0);
            var (x0, y0) = View(L, c1);
            int Dots(int c, float dose)
            {
                Array.Fill(o.Dose, 0f); o.Dose[c] = dose;
                int sum = 0;
                for (int fr = 0; fr < 4; fr++)
                {
                    var img = p.Frame(s, x0, y0, W, H, fr);
                    sum += Count(img, (L.X(c) - x0) * Painter.T, (L.Y(c) - y0) * Painter.T, Painter.T, Painter.T, q => q.R > 225 && q.G > 230 && q.B > 240);
                }
                return sum;
            }
            int calm = Dots(c1, 0.05f), storm = Dots(c1, 10), flare = Dots(c1, 500);
            Check("방사선 화면 잡음: 평소(0.05mGy/시)엔 없고 · 10mGy/시에 보이고 · 500이면 더 짙다",
                calm == 0 && storm > 4 && flare > storm, $"0.05 → {calm}점 · 10 → {storm} · 500 → {flare} (네 장)");
        }

        // ── 새는 공기 줄기: 바람이 4m/s를 넘는 칸에만 흰 줄기 (구멍을 내기 전 같은 칸엔 없다) ──
        {
            var s = ShipWorld.Create("Didim", 3);
            var L = s.Layout; var f = s.Get<Field>(); var p = new Painter(L);
            s.Run(2);
            var room = L.Rooms.First(r => r.Type == "Lounge");
            int hull = -1; var nb = new int[4];
            foreach (int c in room.Cells) { L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && L.Hull[j] && hull < 0) hull = j; }
            var (x0, y0) = View(L, hull);
            int windyBefore = room.Cells.Count(c => MathF.Sqrt(f.WindX[c] * f.WindX[c] + f.WindY[c] * f.WindY[c]) >= Painter.JetMin);
            f.SetHole(hull, 0.3f);
            s.Run(2);
            var windyCells = room.Cells.Where(c => MathF.Sqrt(f.WindX[c] * f.WindX[c] + f.WindY[c] * f.WindY[c]) >= Painter.JetMin).ToList();
            int windy = windyCells.Count;
            // 같은 세계 · 같은 장을 바람만 0으로 (복제) 그려 견준다 — 센 바람 칸에서 밝아진 도트가 줄기 (밤이면 줄기도 어둡게 빛을 받는다)
            var still = s.Clone(); var sf = still.Get<Field>(); Array.Clear(sf.WindX); Array.Clear(sf.WindY);
            var withWind = p.Frame(s, x0, y0, W, H, 0); var noWind = new Painter(L).Frame(still, x0, y0, W, H, 0);
            int Brighter(Img a1, Img b1)
            {
                int n = 0;
                foreach (int c in windyCells)
                    for (int y = 0; y < Painter.T; y++)
                        for (int x = 0; x < Painter.T; x++)
                        {
                            int px = (L.X(c) - x0) * Painter.T + x, py = (L.Y(c) - y0) * Painter.T + y;
                            var q1 = a1.Get(px, py); var q2 = b1.Get(px, py);
                            if (q1.R + q1.G + q1.B - q2.R - q2.G - q2.B > 60) n++;
                        }
                return n;
            }
            int streaks = Brighter(withWind, noWind), stillStreaks = Brighter(noWind, new Painter(L).Frame(still, x0, y0, W, H, 0));
            Check("새는 공기 줄기: 구멍 전엔 센 바람 칸이 없고 · 0.3m² 구멍 두 틱 뒤 센 바람 칸에 흰 줄기 · 같은 세계에서 바람만 없으면 줄기도 없다",
                windyBefore == 0 && windy > 0 && streaks > 20 && stillStreaks < streaks / 4,
                $"센 바람 칸 {windyBefore} → {windy} · 줄기 도트 {streaks} · 바람 0이면 {stillStreaks}");
        }
    }
}
