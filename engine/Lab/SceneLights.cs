using System.Diagnostics;
using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>화면 조명 시안 (그래픽 결정 7 · 8) — 같은 순간을 예전 그림(어두운 칸만 덮기)과 새 조명으로 나란히:
/// 낮의 생활 방 · 함교 · 밤 (주컴퓨터가 낮춘 침실 · 복도) · 정전 (비상등만) · 불 (일렁이는 불빛 · 연기). 그리기 시간도 잰다.</summary>
public static partial class Program
{
    private static void LightsScene(string dir)
    {
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 3;
        var s = ShipWorld.Crewed("Didim", seed, 4, 2);
        var L = s.Layout;
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        int k = 0;
        (int x0, int y0) Around(string type, int w, int h)
        {
            var r = L.Rooms.FirstOrDefault(r => r.Type == type) ?? L.Rooms[0];
            int cx = (int)r.Cells.Average(L.X), cy = (int)r.Cells.Average(L.Y);
            return (Math.Clamp(cx - w / 2, 0, Math.Max(0, L.W - w)), Math.Clamp(cy - h / 2, 0, Math.Max(0, L.H - h)));
        }
        void Shot(string what, string type)
        {
            const int w = 26, h = 16;
            var (x0, y0) = Around(type, w, h);
            painter.Lighting = false;
            var a = painter.Frame(s, x0, y0, w, h, k * 3);
            Png.WriteRgba(Path.Combine(dir, $"{k:00}a.png"), a.W, a.H, a.Px);
            painter.Lighting = true;
            var b = painter.Frame(s, x0, y0, w, h, k * 3);
            Png.WriteRgba(Path.Combine(dir, $"{k:00}b.png"), b.W, b.H, b.Px);
            page.Append($"<section><h2>{System.Net.WebUtility.HtmlEncode(what)} · {Time.Clock(s.Tick)}</h2><div><figure><img src=\"{k:00}a.png\"><figcaption>예전</figcaption></figure><figure><img src=\"{k:00}b.png\"><figcaption>새 조명</figcaption></figure></div></section>");
            Console.WriteLine($"    {k:00} {what} ({type}) {Time.Clock(s.Tick)}");
            k++;
        }
        // 그리기 시간: 관전 화면 크기(41×24칸)로 조명 끄고 / 켜고 (처음 몇 장은 그림 캐시를 굽느라 빼고 잰다)
        void Bench(string when)
        {
            var (bx, by) = Around("Mess", 41, 24);
            foreach (bool on in new[] { false, true, false, true })
            {
                painter.Lighting = on;
                Img? into = null;
                for (int i = 0; i < 5; i++) into = painter.Frame(s, bx, by, 41, 24, i, into);
                var sw = Stopwatch.StartNew();
                const int n = 30;
                for (int i = 0; i < n; i++) into = painter.Frame(s, bx, by, 41, 24, i, into);
                Console.WriteLine($"  그리기 41×24칸 ({when}) · 조명 {(on ? "켬" : "끔")}: {sw.Elapsed.TotalMilliseconds / n:0.0}ms");
            }
        }
        while (Time.HourOfDay(s.Tick) < 10) s.Run(25);
        Shot("낮 — 생활 방 (따뜻한 빛)", "Lounge");
        Shot("낮 — 함교 (푸른 빛 · 화면)", "Bridge");
        Shot("낮 — 재배실 (생장등)", "Hydroponics");
        Bench("조용한 낮");
        while (Time.HourOfDay(s.Tick) is >= 6 and < 23 || Time.HourOfDay(s.Tick) < 0.5) s.Run(25);
        while (Time.HourOfDay(s.Tick) < 0.5 || Time.HourOfDay(s.Tick) > 6) s.Run(25);
        s.Run(Time.Minutes(10));
        Shot("밤 — 침실 3% · 복도 30% (주컴퓨터가 낮췄다)", "Quarters");
        // 정전: 급전선 차단기를 모두 내린다 (장면 차리기) — 비상등만 붉게
        var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
        var keep = sm.Policy; var pol = keep; pol.ResetBreakers = 0; sm.Policy = pol;   // 주컴퓨터가 곧바로 올리지 않게 (장면 차리기)
        for (int f = 0; f < net.Breaker.Length; f++) net.Breaker[f] = 0;
        s.Step(); s.Step();
        Shot("정전 — 비상등만 붉게", "Mess");
        for (int f = 0; f < net.Breaker.Length; f++) net.ResetBreaker(f);
        sm.Policy = keep;
        s.Run(Time.Minutes(5));
        // 불: 생활 방 가구에 불씨 (장면 차리기)
        var lounge = L.Rooms.First(r => r.Type == "Lounge");
        int fire = L.Furniture.Where(fu => fu.Cells.Length > 0 && L.Room[fu.Cells[0]] == lounge.Id).Select(fu => fu.Cells[0]).DefaultIfEmpty(lounge.Cells[lounge.Cells.Length / 2]).First();
        s.Get<Chemistry>().Ignite(fire, 3e6, s.Causes.Add(CauseKind.Setup, "불씨", s.Tick));
        s.Run(40);
        Shot("불 — 일렁이는 불빛 · 연기가 빛을 받는다", "Lounge");
        Bench("불이 난 때");
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>lights</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif}}section div{{display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0}}img{{width:624px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  lights: {k}쌍 → {dir}");
    }

    /// <summary>그리기 시간만 잰다: --paint [배] [장 수] [조명 0/1] — 사람 넷 · 로봇 둘 · 아침 10시 식당 둘레 41×24칸 (관전 화면 크기).</summary>
    private static void PaintBench(string[] args)
    {
        int at = Array.IndexOf(args, "--paint");
        string ship = args.Length > at + 1 && !args[at + 1].StartsWith("-") ? args[at + 1] : "Didim";
        int n = args.Length > at + 2 && int.TryParse(args[at + 2], out int nn) ? nn : 200;
        bool lit = !(args.Length > at + 3 && args[at + 3] == "0");
        var s = ShipWorld.Crewed(ship, 3, 4, 2);
        while (Time.HourOfDay(s.Tick) < 10) s.Run(25);
        var L = s.Layout;
        var mess = L.Rooms.FirstOrDefault(r => r.Type == "Mess") ?? L.Rooms[0];
        int x0 = Math.Clamp((int)mess.Cells.Average(L.X) - 20, 0, Math.Max(0, L.W - 41)), y0 = Math.Clamp((int)mess.Cells.Average(L.Y) - 12, 0, Math.Max(0, L.H - 24));
        var painter = new Painter(L) { Lighting = lit };
        Img? into = null;
        for (int i = 0; i < 10; i++) into = painter.Frame(s, x0, y0, 41, 24, i, into);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) into = painter.Frame(s, x0, y0, 41, 24, i, into);
        Console.WriteLine($"  그리기 {ship} 41×24칸 · 조명 {(lit ? "켬" : "끔")}: {sw.Elapsed.TotalMilliseconds / n:0.00}ms/장 ({n}장)");
    }
}
