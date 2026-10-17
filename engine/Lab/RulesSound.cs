using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>소리 엔진 규칙 시험 — 퍼짐(벽 · 문 · 진공 · 뼈대) · 만들기(분출 · 재질) · 결정론.</summary>
public static partial class Program
{
    private static void SoundRules()
    {
        if (!Do("소리")) return;
        Console.WriteLine("\n[소리]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        const double dt = 1.0 / 60;

        LiveShow Fresh()
        {
            var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
            show.Advance(dt);
            show.Ears.ForceEar = At(5, 3);
            show.Ears.Remap(At(5, 3));
            return show;
        }

        // ── 가림: 같은 방의 쾅 · 닫힌 문 너머 옆 방의 쾅 ──
        {
            var show = Fresh();
            var e = show.Ears;
            int ear = At(5, 3);
            e.Hear(At(8, 2), 110, ear, 12, false, out float lpA, out _, out float cutA, out _);
            e.Hear(At(15, 2), 110, ear, 12, false, out float lpB, out _, out float cutB, out _);
            Check("소리: 닫힌 문 너머 옆 방의 쾅은 같은 방의 쾅보다 작고 먹먹하다 (문이 높은 음을 깎는다)",
                lpA > lpB + 15 && cutA > 6000 && cutB < cutA / 3,
                $"같은 방 {lpA:0}dB · {cutA:0}Hz / 옆 방 {lpB:0}dB · {cutB:0}Hz");
        }

        // ── 뼈대: 멀리 외판을 때린 쾅은 공기 길(벽 둘)보다 뼈대를 타고 온다 — 낮고 먹먹하게 ──
        {
            var show = Fresh();
            var e = show.Ears;
            int ear = At(5, 3), hit = At(20, 8), hull = At(20, 9);
            e.Hear(hit, 110, ear, 12, false, out float lpAir, out _, out _, out _);
            bool heard = e.Hear(hit, 110, ear, 12, false, out float lpS, out _, out float cutS, out _, hull);
            Check("소리: 먼 외판의 쾅은 뼈대를 타고 들린다 — 공기 길보다 크고 · 높은 음이 깎인다",
                heard && lpS > lpAir + 5 && cutS < 1500,
                $"공기 길 {lpAir:0}dB · 뼈대 {lpS:0}dB · {cutS:0}Hz");
        }

        // ── 진공: 듣는 방의 공기를 빼면 공기 소리는 사라지고 · 발밑 판으로 온 쾅만 먹먹하게 ──
        {
            var show = Fresh();
            var f = show.Sim.Get<Field>();
            foreach (int c in lay.Rooms[lay.Room[At(5, 3)]].Cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g]);
            show.Sim.Run(1);
            var e = show.Ears;
            int ear = At(5, 3);
            e.Remap(ear);
            bool voice = e.Hear(At(7, 3), 80, ear, 12, false, out _, out _, out _, out _);
            bool bang = e.Hear(At(8, 1), 115, ear, 12, false, out float lpBang, out _, out float cutBang, out _, At(8, 0));
            Check("소리: 진공에선 목소리가 들리지 않고 · 벽을 때린 쾅만 발밑 판으로 먹먹하게 들린다",
                !voice && bang && cutBang < 1500, $"목소리 {voice} · 쾅 {(bang ? $"{lpBang:0}dB · {cutBang:0}Hz" : "못 들음")} · 귀 짙기 {e.EarThin:0.00}");
        }

        // ── 분출: 작은 틈은 높고 · 큰 구멍은 낮고 크게 (f = 0.2·v/d · 막힘 흐름이면 소리 빠르기) ──
        {
            (float hz, float gain) JetOf(float area)
            {
                var show = Fresh();
                var f = show.Sim.Get<Field>();
                f.SetHole(At(0, 3), area);
                show.Sim.Run(2);
                while (show.Ears.Out.TryDequeue(out _)) { }
                show.Advance(dt);
                float hz = 0, gain = 0;
                while (show.Ears.Out.TryDequeue(out var fr))
                    foreach (var u in fr.Loops) if (u.Kind == LoopKind.Jet && u.Gain > gain) { hz = u.A; gain = u.Gain; }
                return (hz, gain);
            }
            var small = JetOf(0.0004f); var big = JetOf(0.02f);
            Check("소리: 새는 틈 — 작은 틈(4cm²)은 높게 쉬익 · 큰 구멍(200cm²)은 낮고 크게 (분출 속도와 틈 크기에서)",
                small.hz > big.hz * 4 && big.gain > small.gain * 2 && small.gain > 0,
                $"작은 틈 {small.hz:0}Hz · 크기 {small.gain:0.000} / 큰 구멍 {big.hz:0}Hz · 크기 {big.gain:0.000}");
        }

        // ── 재질: 강철 판은 길게 울리고 · 고분자는 금방 그친다 · 유리는 더 높다 ──
        {
            (float ring, float centroid) Measure(string mat)
            {
                var v = new Hit(Acoustic.Of(mat), 5, 42);
                var x = new List<float>();
                for (int i = 0; i < Dsp.Rate * 3 && !v.Done; i++) x.Add(v.Next());
                // 울림: 20ms 창 크기가 꼭대기에서 40dB 떨어지기까지
                int win = Dsp.Rate / 50;
                var env = Enumerable.Range(0, x.Count / win).Select(k => Math.Sqrt(x.Skip(k * win).Take(win).Average(s => (double)s * s))).ToList();
                double peak = env.Max(); int end = env.FindLastIndex(v2 => v2 > peak * 0.01);
                // 무게 중심 높이: 영점 지나기 수 → 대략의 높이
                int zc = 0; for (int i = 1; i < Math.Min(x.Count, Dsp.Rate / 5); i++) if (x[i - 1] < 0 != x[i] < 0) zc++;
                return ((end + 1) * win / (float)Dsp.Rate, zc * 5 / 2f);
            }
            var steel = Measure("steel"); var poly = Measure("polymer"); var glass = Measure("glass");
            Check("소리: 재질 — 강철 판은 길게 울리고 고분자는 금방 그친다 · 유리 소리가 고분자보다 높다",
                steel.ring > poly.ring * 3 && glass.centroid > poly.centroid,
                $"울림 강철 {steel.ring:0.00}초 · 고분자 {poly.ring:0.00}초 · 높이 유리 {glass.centroid:0}Hz · 고분자 {poly.centroid:0}Hz");
        }

        // ── 결정론 · 실제로 섞기: 같은 장면을 두 번 섞으면 표본까지 같다 · 쾅이 섞인 소리에 드러난다 ──
        {
            float[] Render()
            {
                var show = Fresh();
                var mix = new Mixer();
                int per = (int)(Dsp.Rate * dt);
                var buf = new float[per * 2 * 90];
                for (int fr = 0; fr < 90; fr++)
                {
                    if (fr == 10) show.Sim.Get<Sound>().Emit(show.Sim, At(8, 2), 110, SoundKind.Bang);
                    show.Advance(dt);
                    mix.Take(show.Ears);
                    mix.Render(buf.AsSpan(fr * per * 2, per * 2), per);
                }
                return buf;
            }
            var a = Render(); var b = Render();
            bool same = a.AsSpan().SequenceEqual(b);
            int per2 = (int)(Dsp.Rate * dt) * 2;
            double before = Math.Sqrt(a.Take(per2 * 10).Average(x => (double)x * x)), after = Math.Sqrt(a.Skip(per2 * 11).Take(per2 * 10).Average(x => (double)x * x));
            Check("소리: 같은 장면은 같은 소리 (표본까지) · 같은 방의 쾅이 섞인 소리를 크게 키운다",
                same && after > before * 4, $"같음 {same} · 앞 {20 * Math.Log10(before + 1e-9):0}dB → 쾅 뒤 {20 * Math.Log10(after + 1e-9):0}dB");
        }
    
        // ── 바탕 음악: 연출의 분위기를 따라 — 조용할 때 작고 · 조짐부터 낮은 심장 박동 · 위험이면 빨라지고 밝아진다 · 결정적 순간엔 비우고 지속음만 ──
        {
            const int sec = 12;
            (double rms, double low, double high, double mid, int beats, float[] buf, float cut, float pad, int set) Play(string mood, uint seed = 11)
            {
                var sc = new Score(seed);
                sc.SetMood(mood);
                for (int i = 0; i < Dsp.Rate * 20; i++) sc.Next();           // 긴장이 따라올 때까지 (4초 시간 상수)
                var buf = new float[Dsp.Rate * sec];
                OnePole hi = default, env = default;
                var lo = Biquad.Lowpass(90); var band = Biquad.Band(450, 0.6f);
                double all = 0, lowE = 0, highE = 0, midE = 0; int beats = 0, quiet = 0;
                for (int i = 0; i < buf.Length; i++)
                {
                    var (l, r) = sc.Next();
                    float m = (l + r) * 0.5f; buf[i] = m;
                    float lw = lo.Run(m), hw = hi.High(m, 900), mw = band.Run(m);
                    all += m * m; lowE += lw * lw; highE += hw * hw; midE += mw * mw;
                    float e = env.Low(MathF.Abs(lw), 20);
                    quiet--;
                    if (quiet <= 0 && e > 0.02f) { beats++; quiet = int.MaxValue; }                 // 올라선 순간 하나
                    else if (quiet > (int)(0.4f * Dsp.Rate) && e < 0.008f) quiet = (int)(0.4f * Dsp.Rate); // 내려앉고 0.4초 (쿵쿵의 둘째는 세지 않는다)
                }
                return (Math.Sqrt(all / buf.Length), lowE / Math.Max(1e-12, all), highE / buf.Length, midE / buf.Length, beats, buf, sc.Cut, sc.Pad, sc.ChordSet);
            }
            double Db(double x) => 20 * Math.Log10(x + 1e-12);
            var calm = Play("조용함"); var omen = Play("조짐"); var danger = Play("위험"); var crit = Play("결정적 순간");
            var again = Play("조용함");
            bool same = calm.buf.AsSpan().SequenceEqual(again.buf);
            Check("바탕 음악: 조용할 때 작게 (−40 ~ −20dB) · 조짐부터 낮은 심장 박동 · 위험이면 박동이 빨라지고 밝아진다 · 결정적 순간엔 패드(200~900Hz)를 비운다 · 같은 씨앗은 같은 음악",
                Db(calm.rms) is > -40 and < -20 && calm.beats == 0 && calm.set == 0 && omen.set == 1 && omen.low > calm.low * 2 && omen.beats >= 10 && danger.beats > omen.beats * 1.4
                    && danger.cut > calm.cut * 2.5 && crit.pad < 0.2f && danger.pad > 0.9f && same,
                $"크기 조용함 {Db(calm.rms):0}dB · 조짐 {Db(omen.rms):0} · 위험 {Db(danger.rms):0} · 결정적 {Db(crit.rms):0} / 낮은 소리 몫 {calm.low:0.00} → {omen.low:0.00} / 박동 {sec}초에 조용함 {calm.beats} · 조짐 {omen.beats} · 위험 {danger.beats} / 화음 갈래 {calm.set} → {omen.set} / 자르는 높이 {calm.cut:0} → {danger.cut:0}Hz / 패드 위험 {danger.pad:0.00} → 결정적 {crit.pad:0.00} · 같음 {same}");
        }

        // ── 효과음이 크면 음악은 잠깐 작아진다 (같은 쾅 — 음악을 켠 섞개와 끈 섞개의 차가 곧 음악) ──
        {
            float[] Mix(float music)
            {
                var mx = new Mixer { MusicGain = music };
                var f0 = new SoundFrame { Mood = "조용함" }; mx.Apply(f0);
                var buf = new float[Dsp.Rate * 2 * 6];
                mx.Render(buf.AsSpan(0, Dsp.Rate * 2 * 3), Dsp.Rate * 3);
                var f1 = new SoundFrame(); f1.Shots.Add(new Hit(Acoustic.Of("steel"), 400, 7) { Gain = 0.8f }); mx.Apply(f1);
                mx.Render(buf.AsSpan(Dsp.Rate * 2 * 3), Dsp.Rate * 3);
                return buf;
            }
            var on = Mix(0.8f); var off = Mix(0);
            double Part(int from, int len) { double e = 0; for (int i = from; i < from + len; i++) { double d = on[i] - off[i]; e += d * d; } return Math.Sqrt(e / len); }
            double before = Part(Dsp.Rate * 2 * 2, Dsp.Rate * 2), during = Part(Dsp.Rate * 2 * 3 + Dsp.Rate / 5, Dsp.Rate);
            Check("효과음이 크면 바탕 음악은 잠깐 작아진다 (쾅 동안 음악 몫이 앞보다 작다)", during < before * 0.6,
                $"음악 몫 쾅 앞 {20 * Math.Log10(before + 1e-12):0}dB · 쾅 동안 {20 * Math.Log10(during + 1e-12):0}dB");
        }
}

    /// <summary>바탕 음악 견본 (--music [파일]): 조용함 20초 → 조짐 15초 → 위험 15초 → 결정적 순간 10초 → 조용함 15초 · 25초에 쾅 하나 (음악이 비켜 준다).</summary>
    internal static void MusicDemo(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--music").Skip(1).ToArray();
        string path = rest.Length > 0 ? rest[0] : "build/music.wav";
        var mx = new ShipView.Audio.Mixer();
        var parts = new (string mood, int sec)[] { ("조용함", 20), ("조짐", 15), ("위험", 15), ("결정적 순간", 10), ("조용함", 15) };
        var all = new List<float>();
        int t = 0;
        foreach (var (mood, sec) in parts)
        {
            mx.Apply(new ShipView.Audio.SoundFrame { Mood = mood });
            for (int k = 0; k < sec; k++, t++)
            {
                if (t == 25) { var f = new ShipView.Audio.SoundFrame { Mood = mood }; f.Shots.Add(new ShipView.Audio.Hit(ShipView.Audio.Acoustic.Of("steel"), 400, 7) { Gain = 0.7f }); mx.Apply(f); }
                var buf = new float[ShipView.Audio.Dsp.Rate * 2];
                mx.Render(buf, ShipView.Audio.Dsp.Rate);
                all.AddRange(buf);
            }
            Console.WriteLine($"  {mood} {sec}초 · 긴장 {mx.Music.Tension:0.00} · 자르는 높이 {mx.Music.Cut:0}Hz · 패드 {mx.Music.Pad:0.00}");
        }
        ShipView.Audio.Mixer.WriteWav(path, all.ToArray());
        Console.WriteLine($"{path} · {all.Count / 2 / ShipView.Audio.Dsp.Rate}초");
    }
}
