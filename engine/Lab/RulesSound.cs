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
    }
}
