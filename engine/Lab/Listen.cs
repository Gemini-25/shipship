using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>소리 확인 (--listen): 관전 화면을 화면 1/60초씩 돌리며 귀가 들은 것을 섞어 WAV로 — build/sound/*.wav</summary>
public static partial class Program
{
    /// <summary>--listen [배] [시드] [화면 초] [건너뛸 게임 시간] → build/sound/배-시드.wav</summary>
    private static void Listen(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--listen").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Kestrel";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double screenSec = rest.Length > 2 ? double.Parse(rest[2]) : 60;
        double skipHours = rest.Length > 3 ? double.Parse(rest[3]) : 0;
        var show = LiveShow.Crewed(ship, seed);
        if (skipHours > 0) { show.Ears.Enabled = false; while (show.Sim.Tick < Time.Hours(skipHours)) show.Advance(1); show.Ears.Enabled = true; }
        var mix = new Mixer();
        int logged = 0;
        show.Ears.Log = m => { if (logged++ < 60) Console.WriteLine("    " + m); };
        const double dt = 1.0 / 60;
        int per = (int)(Dsp.Rate * dt), frames = (int)(screenSec / dt);
        var all = new float[frames * per * 2];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double mixMs = 0; int maxVoices = 0;
        for (int f = 0; f < frames; f++)
        {
            show.Advance(dt);
            var t0 = sw.Elapsed.TotalMilliseconds;
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(f * per * 2, per * 2), per);
            mixMs += sw.Elapsed.TotalMilliseconds - t0;
            maxVoices = Math.Max(maxVoices, mix.Voices);
            if (f % (60 * 15) == 0) Console.WriteLine($"  화면 {f * dt,5:0}초 · {show.Status()} · 소리 {mix.Voices}개 · 귀 칸 {show.Ears.EarCell} (짙기 {show.Ears.EarThin:0.00})");
        }
        string path = $"build/sound/{ship.ToLowerInvariant()}-{seed}.wav";
        Mixer.WriteWav(path, all);
        double rms = Math.Sqrt(all.Average(x => (double)x * x)), peak = all.Max(MathF.Abs);
        Console.WriteLine($"{path} · {screenSec:0}초 · 크기(RMS) {20 * Math.Log10(rms + 1e-9):0.0}dB · 꼭대기 {peak:0.00} · 섞기 1/60초마다 {mixMs / frames:0.00}ms · 동시에 많아야 {maxVoices}개");
    }
}
