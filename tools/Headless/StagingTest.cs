using System;
using System.Linq;
using ShipSim.Art;
using ShipSim.Core;

// 도트 8단계: 연출 장부 시험 — 사고 · 우주 재난 · 폭발 하나하나가 소리 · 그림(· 카메라)을 갖는지, 소리가 실제로 나는지.
//   dotnet Headless.dll 1 1 --stagingtest   (하나라도 빠지면 종료 코드 1)
public static partial class Program
{
    private static int RunStagingTest()
    {
        int fail = 0, ok = 0;
        void Check(bool cond, string what) { if (cond) ok++; else { fail++; Console.WriteLine($"  ✗ {what}"); } }
        foreach (var k in Enum.GetValues<HazardKind>())
        {
            var c = Staging.Of(k);
            Check(Staging.Has(k), $"사고 {k}: 장부에 없음");
            Check(c.Sound != StageSound.None && c.Visual.Length > 0, $"사고 {k}: 소리 · 그림 없음");
        }
        foreach (var k in Enum.GetValues<CosmicKind>())
        {
            var c = Staging.Of(k);
            Check(Staging.Has(k), $"우주 재난 {k}: 장부에 없음");
            Check(c.Sound != StageSound.None && c.Visual.Length > 0, $"우주 재난 {k}: 소리 · 그림 없음");
        }
        foreach (var k in Enum.GetValues<BlastKind>())
        {
            var c = Staging.Of(k);
            Check(c.Sound != StageSound.None && c.Cam != StageCam.None && c.Visual.Length > 0, $"폭발 {k}: 소리 · 카메라 · 그림 없음");
        }
        Check(Staging.Duplicates.Count == 0, $"두 번 적힌 것: {string.Join(", ", Staging.Duplicates)}");
        foreach (var s in Enum.GetValues<StageSound>().Where(s => s != StageSound.None))
        {
            var w = StageSounds.Make(s);
            float peak = w.Length == 0 ? 0f : w.Max(MathF.Abs);
            double rms = w.Length == 0 ? 0 : Math.Sqrt(w.Average(v => (double)v * v));
            Check(w.Length >= StageSounds.Rate / 5 && peak > 0.5f && rms > 0.02, $"소리 {s}: 비었거나 너무 작다 (길이 {w.Length} · 최대 {peak:0.00} · RMS {rms:0.000})");
        }
        int hz = Enum.GetValues<HazardKind>().Length, cz = Enum.GetValues<CosmicKind>().Length, bz = Enum.GetValues<BlastKind>().Length;
        Console.WriteLine($"연출 장부: 사고 {hz} · 우주 재난 {cz} · 폭발 {bz} · 소리 {Enum.GetValues<StageSound>().Length - 1} — 통과 {ok} · 빠짐 {fail}");
        return fail == 0 ? 0 : 1;
    }
}
