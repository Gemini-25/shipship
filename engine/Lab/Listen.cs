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
        if (rest.Length > 0 && rest[0] == "demo") { ListenDemo(); return; }
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

    /// <summary>
    /// 소리 시연 (--listen demo): 시험 배 왼쪽 방에 귀를 두고 차례로 — 외침 · 강철 벽 쾅 · 닫힌 문 너머 쾅 · 불 · 작은 틈 · 큰 구멍 · 경보 · 먼 외판 충돌 · 문 열림.
    /// → build/sound/demo.wav (+ 스펙트로그램은 tools/sound/spectro.py)
    /// </summary>
    private static void ListenDemo()
    {
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var m = show.Sim.Get<Mind>();
        int who = m.AddPerson(show.Sim, At(3, 4));
        const double dt = 1.0 / 60;
        show.Advance(dt);
        show.Ears.ForceEar = At(5, 3);
        show.SpeedOverride = 1;          // 화면 1초에 1틱 (게임 2.4초) — 불 · 바람이 천천히 자란다
        var snd = show.Sim.Get<Sound>();
        var cues = new (double t, string what, Action act)[]
        {
            (1.0, "같은 방에서 외침", () => snd.Emit(show.Sim, At(7, 3), 80, SoundKind.Voice, 0, who)),
            (3.0, "같은 방 강철 벽 쾅 (5kJ)", () => snd.Emit(show.Sim, At(8, 1), 92, SoundKind.Bang)),
            (5.5, "닫힌 문 너머 옆 방 쾅 (같은 세기)", () => snd.Emit(show.Sim, At(15, 1), 92, SoundKind.Bang)),
            (8.0, "같은 방 침대에 불", () => show.Sim.Get<Chemistry>().Ignite(At(3, 5), 3e6, show.Sim.Causes.Add(CauseKind.Story, "불씨", show.Sim.Tick))),
            (14.0, "벽에 작은 틈 (3cm²)", () => show.Sim.Get<Field>().SetHole(At(0, 2), 0.0003f)),
            (19.0, "틈이 큰 구멍으로 (150cm²)", () => show.Sim.Get<Field>().SetHole(At(0, 2), 0.015f)),
            (24.0, "주컴퓨터 경보", () => snd.Emit(show.Sim, At(5, 2), 90, SoundKind.Alarm, 0, 9999)),
            (26.0, "먼 통로 외판에 파편 (뼈대로)", () => snd.Emit(show.Sim, At(20, 8), 118, SoundKind.Bang)),
            (28.0, "외판에 금 (뼈대로)", () => snd.Emit(show.Sim, At(18, 8), 85, SoundKind.Crack)),
        };
        var mix = new Mixer();
        int per = (int)(Dsp.Rate * dt), frames = (int)(32 / dt);
        var all = new float[frames * per * 2];
        int next = 0;
        for (int f = 0; f < frames; f++)
        {
            double t = f * dt;
            while (next < cues.Length && t >= cues[next].t) { Console.WriteLine($"  {cues[next].t,5:0.0}초 · {cues[next].what}"); cues[next].act(); next++; }
            show.Advance(dt);
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(f * per * 2, per * 2), per);
        }
        Mixer.WriteWav("build/sound/demo.wav", all);
        Console.WriteLine($"build/sound/demo.wav · 32초 · 꼭대기 {all.Max(MathF.Abs):0.00}");
    }
}
