using ShipEngine;

namespace Lab;

/// <summary>⑤ 바깥 — 항로: 별과의 거리 → 햇빛 · 외판 온도 · 방사선 · 호만 전이 항해 (지어낸 별계 '한빛계' · 실제 물리). docs/DEPTH.md</summary>
public static partial class Program
{
    private static void VoyageRules()
    {
        if (!Do("항로")) return;
        Console.WriteLine("\n[⑤ 항로]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 호만 전이: 1AU → 1.524AU(지구 → 화성 궤도)는 259일 (실제 값) · 가는 동안 거리는 케플러 방정식대로 늘고 · 도착하면 목표 거리 ──
        {
            double days = Outside.VoyageDays(1, 1.524);
            var path = Enumerable.Range(0, 11).Select(k => Outside.TransferDistance(1, 1.524, k / 10.0)).ToList();
            bool rising = path.Zip(path.Skip(1)).All(p => p.Second > p.First);
            var s = ShipWorld.Create("Hwasal", 1);
            var o = s.Get<Outside>();
            o.StartVoyage(s, 0.72);
            long half = o.VoyageTicks / 2;
            s.Run(3);
            double inwardDays = Outside.VoyageDays(1, 0.72);
            Check("호만 전이: 1 → 1.524AU(지구 → 화성 궤도)는 259일(실제 값) · 가는 동안 거리는 케플러 방정식대로 늘고 끝에 목표 거리 · 안쪽(0.72AU)으로도 간다",
                Math.Abs(days - 259) < 3 && rising && Math.Abs(path[^1] - 1.524) < 1e-9 && Math.Abs(path[0] - 1) < 1e-9 && o.Distance < 1 && Math.Abs(Outside.TransferDistance(1, 0.72, 1) - 0.72) < 1e-9,
                $"1 → 1.524AU {days:0}일 · 반쯤 {path[5]:0.000}AU · 1 → 0.72AU {inwardDays:0}일 · 떠난 지 세 틱 {o.Distance:0.0000000}AU");
        }

        // ── 별에 가까워지면: 햇빛 1/d² · 햇빛 쪽 외판이 더 뜨겁다 · 별 입자 방사선 1/d² (은하 우주선은 그대로) ──
        {
            (double sun, double hot, double dose) At(double au)
            {
                var s = ShipWorld.Create("Hwasal", 2);
                var o = s.Get<Outside>(); var f = s.Get<Field>();
                o.Distance = au; o.SunDrift = 0; // 햇빛 방향을 멈춘다 (한쪽 외판만 쬔다)
                s.Run(3);
                double hot = Enumerable.Range(0, f.Cells).Where(i => f.L.Hull[i]).Max(i => f.SkinTemp(i)); // 다층 단열재 겉면 (안의 벽은 단열재가 지킨다)
                return (f.Outside.Sun, hot, o.DoseOut);
            }
            var far = At(1.0); var near = At(0.5);
            double gcr = Tables.Default.Space.System.Gcr;
            if (Debug) Console.WriteLine($"    1AU: 햇빛 {far.sun:0} · 외판 {far.hot:0}K · {far.dose:0.000}mGy/시 / 0.5AU: {near.sun:0} · {near.hot:0}K · {near.dose:0.000}");
            Check("별에 가까워지면 햇빛은 거리의 제곱에 반비례(0.5AU에서 네 배) · 햇빛 쪽 외판 겉 온도는 복사 평형대로 √2배 · 별 입자 방사선도 네 배 (은하 우주선은 그대로)",
                Math.Abs(near.sun / far.sun - 4) < 1e-9 && Math.Abs(near.hot / far.hot - Math.Sqrt(2)) < 0.02 && Math.Abs((near.dose - gcr) / (far.dose - gcr) - 4) < 1e-6,
                $"햇빛 {far.sun:0} → {near.sun:0}W/m² · 햇빛 쪽 외판 겉 {far.hot - 273.15:0} → {near.hot - 273.15:0}℃ · 바깥 방사선 {far.dose:0.000} → {near.dose:0.000}mGy/시");
        }
    }
}
