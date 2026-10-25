using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>사고 효과 시안 (그래픽 결정 11) — 모두 세계의 값에서: 선체 구멍(새는 공기 줄기) · 불의 연기층(천장 아래 엷은 막) · 젖은 전선의 합선(불꽃) ·
/// 폭발(충격파 · 섬광 · 화면 흔들림) · 별 폭발(방사선 화면 잡음). 장면마다 그림 몇 장 + 그때의 세계 값.</summary>
public static partial class Program
{
    private static void FxScene(string dir)
    {
        var page = new System.Text.StringBuilder();
        int k = 0;
        void Shot(Sim s, Painter p, int cell, string what, int frame = 0)
        {
            var L = s.Layout;
            const int w = 22, h = 14;
            int x0 = Math.Clamp(L.X(cell) - w / 2, 0, Math.Max(0, L.W - w)), y0 = Math.Clamp(L.Y(cell) - h / 2, 0, Math.Max(0, L.H - h));
            var img = p.Frame(s, x0, y0, w, h, frame);
            Png.WriteRgba(Path.Combine(dir, $"{k:00}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:00}.png\"><figcaption>{System.Net.WebUtility.HtmlEncode(what)}</figcaption></figure>");
            Console.WriteLine($"    {k:00} {what}");
            k++;
        }
        int Cause(Sim s, string why) => s.Causes.Add(CauseKind.Setup, why, s.Tick);

        // ① 선체 구멍: 생활 방 바깥벽에 0.3m² 구멍 — 새는 공기 줄기 (칸 바람이 4m/s를 넘는 칸만)
        {
            var s = ShipWorld.Crewed("Didim", 3, 4, 2);
            s.Run(Time.Hours(9));
            var L = s.Layout; var f = s.Get<Field>(); var p = new Painter(L);
            var room = L.Rooms.First(r => r.Type == "Lounge");
            int hull = -1; Span<int> nb = stackalloc int[4];
            foreach (int c in room.Cells) { L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && L.Hull[j] && hull < 0) hull = j; }
            f.SetHole(hull, 0.3f);
            for (int t = 0; t < 3; t++)
            {
                s.Step();
                float vmax = 0; foreach (int c in room.Cells) vmax = Math.Max(vmax, MathF.Sqrt(f.WindX[c] * f.WindX[c] + f.WindY[c] * f.WindY[c]));
                Shot(s, p, hull, $"선체 구멍 {t + 1}틱 — 방 기압 {f.Pressure(room.Cells[0]):0}kPa · 가장 센 바람 {vmax:0}m/s", t * 2);
            }
        }
        // ② 불의 연기층: 쉼터 가구에 불 — 5분 뒤 천장 아래 짙은 층 (엷은 막) · 바닥 쪽은 덜하다
        {
            var s = ShipWorld.Crewed("Didim", 3, 4, 2);
            s.Run(Time.Hours(9));
            var L = s.Layout; var f = s.Get<Field>(); var p = new Painter(L);
            var room = L.Rooms.First(r => r.Type == "Lounge");
            int fire = L.Furniture.Where(fu => fu.Cells.Length > 0 && L.Room[fu.Cells[0]] == room.Id).Select(fu => fu.Cells[0]).First();
            s.Get<Chemistry>().Ignite(fire, 3e6, Cause(s, "불씨"));
            foreach (int min in new[] { 2, 5 })
            {
                s.Run(Time.Minutes(min == 2 ? 2 : 3));
                int probe = room.Cells.OrderBy(c => Math.Abs(L.X(c) - L.X(fire)) + Math.Abs(L.Y(c) - L.Y(fire))).ElementAt(Math.Min(6, room.Cells.Length - 1));
                if (Environment.GetEnvironmentVariable("FX_DEBUG") == "1")
                    foreach (int c in room.Cells.Take(40)) Console.WriteLine($"      칸 {L.X(c)},{L.Y(c)} 층 {f.Strat[c]:0.00} 위 {f.HeadPpm(f.T.gSoot, c, false):0} 아래 {f.HeadPpm(f.T.gSoot, c, true):0} 칸 {f.Ppm(f.T.gSoot, c):0} 방 {L.Room[c]}");
                Shot(s, p, fire, $"불 {min}분 — 층 {f.Strat[probe]:0.00} · 그을음 천장 {f.HeadPpm(f.T.gSoot, probe, false):0}ppm / 바닥 {f.HeadPpm(f.T.gSoot, probe, true):0}ppm", min);
            }
        }
        // ③ 합선: 식당으로 가는 전선이 지나는 칸에 물 30몰 — 불꽃 · 차단기가 떨어진다
        {
            var s = ShipWorld.Crewed("Didim", 3, 4, 2);
            s.Run(Time.Hours(9));
            var L = s.Layout; var f = s.Get<Field>(); var net = s.Get<Network>(); var p = new Painter(L);
            int mess = L.Rooms.FindIndex(r => r.Type == "Mess");
            var feeder = net.Feeders.First(fd => fd.Id == net.RoomFeeder[mess]);
            int wet = feeder.Path[feeder.Path.Length / 2];
            f.AddStore(wet, f.T.sWater, 30);
            long shorts = s.Facts.CountOf(FactKind.Short);
            for (int t = 0; t < 4 && s.Facts.CountOf(FactKind.Short) == shorts; t++) s.Step();
            var sc = s.Facts.Recent(64).LastOrDefault(x => x.Kind == FactKind.Short);
            for (int fr = 0; fr < 2; fr++) { p.Sub = fr * 0.5; Shot(s, p, sc.Cell >= 0 ? sc.Cell : wet, $"합선 ({(sc.Kind == FactKind.Short ? "사실 있음" : "없음")}) · 장 {fr}", fr * 3); }
        }
        // ④ 폭발: 기관실에 TNT 2kg쯤 (8MJ) — 섬광 · 충격파가 퍼진다 · 화면이 흔들린다 (관전 화면)
        {
            var s = ShipWorld.Crewed("Didim", 3, 4, 2);
            s.Run(Time.Hours(9));
            var L = s.Layout; var p = new Painter(L);
            var room = L.Rooms.FirstOrDefault(r => r.Type == "Engine") ?? L.Rooms.First(r => r.Type == "Power");
            int at = room.Cells[room.Cells.Length / 2];
            s.Get<Structure>().Blast(s, at, 8e6, Cause(s, "폭발 (장면 차리기)"));
            var bl = s.Facts.Recent(64).LastOrDefault(x => Painter.IsBlast(x));
            foreach (double sub in new[] { 0.1, 0.4, 0.8 })
            {
                p.Sub = sub;
                Shot(s, p, at, $"폭발 {sub * 2.4:0.0}초 — 큰 소리 {bl.Size:0}dB · TNT {Painter.BlastKg(bl.Size):0.0}kg · 폭풍이 닿는 칸 {Painter.BlastReach(bl.Size):0.0}", (int)(sub * 8));
            }
        }
        // ⑤ 별 폭발: 바깥 방사선이 오르면 화면에 하얀 점 (선량률이 클수록 짙게) — 대피소는 가려 덜하다
        {
            var s = ShipWorld.Crewed("Didim", 3, 4, 2);
            s.Run(Time.Hours(9));
            var L = s.Layout; var o = s.Get<Outside>(); var p = new Painter(L);
            o.ScheduleFlare(s.Tick, 0, Cause(s, "별 폭발 (장면 차리기)"), 3);
            long until = s.Tick + Time.Hours(3);
            while (s.Tick < until && o.Dose.Max() < 50) s.Run(25);
            int hot = Enumerable.Range(0, L.N).Where(c => L.IsSpace(c)).OrderByDescending(c => o.Dose[c]).First();
            Shot(s, p, hot, $"별 폭발 — 이 칸 {o.Dose[hot]:0}mGy/시 · 바깥 {o.DoseOut:0}mGy/시", 1);
        }
        // ⑥ 재질 흔적: 오래된 배처럼 — 벽마다 부식 · 피로를 나이만큼 (장면 차리기) · 냉매 · 전해액이 쏟아진 바닥 · 그을음
        {
            var s = ShipWorld.Crewed("Didim", 3, 4, 2);
            s.Run(Time.Hours(9));
            var L = s.Layout; var st = s.Get<Structure>(); var f = s.Get<Field>(); var p = new Painter(L);
            var room = L.Rooms.First(r => r.Type == "Engine");
            int at = room.Cells[room.Cells.Length / 2];
            for (int c = 0; c < L.N; c++)
                if (L.Kind[c] == CellKind.Wall && Math.Abs(L.X(c) - L.X(at)) < 12 && Math.Abs(L.Y(c) - L.Y(at)) < 8)
                {
                    float u = DotHash.F(c, 5, 9);
                    st.Corrode[c] = u < 0.5f ? 0 : (u - 0.5f) * 1.2f; st.Fatigue[c] = DotHash.F(c, 6, 9) < 0.25f ? 0.5f + DotHash.F(c, 7, 9) * 0.5f : 0;
                }
            var open = room.Cells.Where(c => L.FurnitureAt[c] < 0).ToList();
            f.AddStore(open[0], f.T.sNH3l, 30); f.AddStore(open[Math.Min(3, open.Count - 1)], f.T.sEC, 15);
            f.AddStore(open[Math.Min(6, open.Count - 1)], f.T.sWater, 40);
            Shot(s, p, at, "재질 흔적 — 녹 (윗면 얼룩 · 앞면 흘러내림) · 실금 · 냉매(청록) · 전해액(호박) · 물 웅덩이", 2);
        }
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>fx</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0}}img{{width:528px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  fx: {k}장 → {dir}");
    }
}
