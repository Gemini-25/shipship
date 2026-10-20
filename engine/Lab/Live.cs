using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>관전 화면 확인 (--live): Godot 장면과 같은 LiveShow를 화면 1/60초씩 돌린다 — 걸린 시간 · 분위기 바뀜 · 장면 그림.</summary>
public static partial class Program
{
    /// <summary>--live [배] [시드] [화면 초] → build/live/*.png · 한 장 그리는 데 · 한 번 나아가는 데 걸린 시간</summary>
    private static void Live(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--live").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double screenSec = rest.Length > 2 ? double.Parse(rest[2]) : 600;
        const double dt = 1.0 / 60;
        Directory.CreateDirectory("build/live");
        foreach (var old in Directory.GetFiles("build/live", "*.png")) File.Delete(old);
        var show = LiveShow.Crewed(ship, seed);
        show.Sim.Measure = true;
        var adv = new List<double>(); var paint = new List<double>(); var mixMs = new List<double>();
        var paintParts = new Dictionary<string, double>();
        long advAlloc = 0, paintAlloc = 0; int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration();
        var mix = new ShipView.Audio.Mixer();   // 게임은 메인 스레드에서 섞는다 — 1/60초 몫을 잰다
        int per = (int)(ShipView.Audio.Dsp.Rate * dt);
        var audio = new float[per * 2];
        var sw = new System.Diagnostics.Stopwatch();
        string lastMood = ""; int shots = 0; double nextShot = 0; long ticks = 0;
        int frames = (int)(screenSec / dt);
        for (int fr = 0; fr < frames; fr++)
        {
            show.Sim.LastMs.Clear();
            sw.Restart();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            ticks += show.Advance(dt);
            advAlloc += GC.GetAllocatedBytesForCurrentThread() - a0;
            adv.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            mix.Take(show.Ears);
            mix.Render(audio, per);
            mixMs.Add(sw.Elapsed.TotalMilliseconds);
            if (adv[^1] > 50) Console.WriteLine($"  ⚠ 화면 {fr * dt:0.0}초 · 나아가기 {adv[^1]:0}ms — {string.Join(" · ", show.Sim.LastMs.OrderByDescending(x => x.Value).Take(4).Select(x => $"{x.Key} {x.Value:0}ms"))}");
            double t = fr * dt;
            bool moodChanged = show.Dir.Mood != lastMood;
            bool banner = show.BannerLeft > 5.99;
            if (moodChanged || banner) Console.WriteLine($"  화면 {t,6:0.0}초 · {show.Status()}{(banner ? $" · ★ {show.Banner}" : "")}");
            lastMood = show.Dir.Mood;
            // 그리기: 열 장에 한 번 재고 (화면은 작업 스레드에서 매번 그린다) · 장면이 바뀌거나 30초마다 한 장 남긴다
            if (fr % 10 == 0 || banner || t >= nextShot)
            {
                Painter.Prof = new(); Painter.ProfAlloc ??= new();
                sw.Restart();
                long p0 = GC.GetAllocatedBytesForCurrentThread();
                var lf = show.Render();
                paintAlloc += GC.GetAllocatedBytesForCurrentThread() - p0;
                paint.Add(sw.Elapsed.TotalMilliseconds);
                foreach (var kv in Painter.Prof) paintParts[kv.Key] = paintParts.GetValueOrDefault(kv.Key) + kv.Value;
                if (paint[^1] > 20 && fr > 60) Console.WriteLine($"  ⚠ 그리기 {fr * dt:0.0}초 · {paint[^1]:0}ms — {string.Join(" · ", Painter.Prof.OrderByDescending(x => x.Value).Take(4).Select(x => $"{x.Key} {x.Value:0.0}ms"))}");
                Painter.Prof = null;
                if ((banner || t >= nextShot) && shots < 40)
                {
                    var crop = Crop(lf);
                    Png.WriteRgba($"build/live/{shots:00}-{t:0000}s.png", crop.W, crop.H, crop.Px);
                    Console.WriteLine($"    → build/live/{shots:00}-{t:0000}s.png · {crop.W}×{crop.H} · {lf.Story}");
                    shots++;
                    nextShot = t + 30;
                }
                show.Recycle(lf.Img);
            }
        }
        static ShipSim.Art.Img Crop(LiveFrame lf)
        {
            // 화면이 실제로 보일 곳만 (Godot은 이 곳을 창 크기로 늘려 그린다)
            int sx = (int)lf.SrcX, sy = (int)lf.SrcY, w = (int)lf.SrcW, h = (int)lf.SrcH;
            var o = new ShipSim.Art.Img(w, h);
            for (int y = 0; y < h; y++) Buffer.BlockCopy(lf.Img.Px, ((sy + y) * lf.Img.W + sx) * 4, o.Px, y * w * 4, w * 4);
            return o;
        }
        // 다시보기: 첫 하이라이트를 첫 조짐 직전부터 — 화면처럼 돌려 몇 장 남긴다
        if (show.Dir.Highlights.Count > 0 && show.StartReplay(show.Dir.Highlights[0]))
        {
            Console.WriteLine($"  다시보기: '{show.ReplayOf!.Title}' — {Time.Clock(show.World.Tick)}부터");
            double rt = 0, next = 0; int rshots = 0;
            while (show.Replaying && rt < 900)
            {
                show.Advance(dt); rt += dt;
                if (rt >= next && show.Replaying && rshots < 12)
                {
                    var lf = show.Render(); var crop = Crop(lf);
                    Png.WriteRgba($"build/live/r{rshots:00}-{rt:0000}s.png", crop.W, crop.H, crop.Px);
                    Console.WriteLine($"    → build/live/r{rshots:00}-{rt:0000}s.png · {lf.Status} · {lf.Story}");
                    show.Recycle(lf.Img); rshots++; next = rt + 15;
                }
            }
            Console.WriteLine($"  다시보기 끝 · 화면 {rt:0}초 · 산 세계로 {(show.Replaying ? "못 돌아옴" : "돌아옴")}");
        }
        double P(List<double> l, double q) { var o = l.OrderBy(x => x).ToList(); return o.Count == 0 ? 0 : o[(int)Math.Min(o.Count - 1, q * o.Count)]; }
        Console.WriteLine($"{show.ShipName} 화면 {screenSec:0}초 → 게임 {Time.Clock(show.Sim.Tick)} ({ticks}틱) · 하이라이트 {show.Dir.Highlights.Count} · 그림 {shots}장");
        Console.WriteLine($"  나아가기 1/60초마다: 보통 {P(adv, 0.5):0.00}ms · 99% {P(adv, 0.99):0.0}ms · 가장 김 {adv.Max():0.0}ms (화면 {adv.IndexOf(adv.Max()) * dt:0.0}초)");
        Console.WriteLine($"  그리기 한 장: 보통 {P(paint, 0.5):0.0}ms · 99% {P(paint, 0.99):0.0}ms · 가장 김 {paint.Max():0.0}ms");
        if (Painter.ProfAlloc != null) Console.WriteLine("    단계마다 한 장 새로 잡기 KB: " + string.Join(" · ", Painter.ProfAlloc.OrderByDescending(x => x.Value).Take(10).Select(x => $"{x.Key} {x.Value / 1024.0 / Math.Max(1, paint.Count):0}")));
        Console.WriteLine("    단계마다 한 장 평균: " + string.Join(" · ", paintParts.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value / Math.Max(1, paint.Count):0.00}")));
        Console.WriteLine($"  새로 잡기: 나아가기 틱마다 {advAlloc / Math.Max(1, ticks) / 1024.0:0}KB · 그리기 한 장 {paintAlloc / Math.Max(1, paint.Count) / 1024.0:0}KB · 수거 {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2} · 멈춤 합 {(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds:0}ms");
        Console.WriteLine($"  미리 보기: 복제 {show.Dir.Foresights} · 다시 맞춤 {show.Dir.Resyncs} · 앞선 틱 {show.Dir.AheadSteps} · {show.Dir.ForeseeMs:0}ms");
        Console.WriteLine($"  새 그림판 {LiveShow.NewCanvas}장 / 그림 {paint.Count}장 (확대 단계가 바뀔 때만)");
        Console.WriteLine($"  소리 섞기 1/60초마다: 보통 {P(mixMs, 0.5):0.00}ms · 99% {P(mixMs, 0.99):0.00}ms · 가장 김 {mixMs.Max():0.0}ms · 목소리 {mix.Voices}");
        Console.WriteLine($"  분위기별 화면 초: 조용함 {show.Dir.CalmScreenSec:0} · 조짐 {show.Dir.OmenScreenSec:0} · 위험 {show.Dir.DangerScreenSec:0} · 결정적 {show.Dir.CriticalScreenSec:0}");
    }
}
