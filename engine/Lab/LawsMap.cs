using ShipEngine;

namespace Lab;

/// <summary>⑤ 별계 지도 (깊이 바퀴 3) — 공전 · 앞선 각 · 회합 주기 · 발사 창 · 창에 떠나면 닿는다. docs/ENGINE.md ⑤ '별계 지도'</summary>
public static partial class Program
{
    private static void MapLaws()
    {
        if (!Do("별계 지도")) return;
        Console.WriteLine("\n[⑤ 별계 지도 — 공전 · 발사 창 · 닿는 자리]");
        // ── 케플러: 공전 주기 a^1.5 년 · 지구 → 화성 호만 전이는 화성이 44° 앞설 때 · 그 창은 780일마다 (실제 값) ──
        {
            double lead = Outside.LeadAngle(1, 1.524) * 180 / Math.PI, syn = Outside.SynodicDays(1, 1.524), mars = Outside.PeriodDays(1.524);
            double turn = Outside.Mean(1.524) * Time.TicksPerDay * mars;
            Check("케플러: 화성 궤도(1.524AU) 공전 687일 · 지구 → 화성 호만 전이는 화성이 44.3° 앞설 때 떠난다 · 그 발사 창은 780일마다 (실제 값)",
                Math.Abs(mars - 687) < 2 && Math.Abs(lead - 44.3) < 0.3 && Math.Abs(syn - 780) < 3 && Math.Abs(turn - 2 * Math.PI) < 1e-9,
                $"공전 {mars:0}일 · 앞선 각 {lead:0.0}° · 회합 주기 {syn:0}일");
        }
        // ── 발사 창: 기다린 뒤에는 목표가 꼭 앞선 각만큼 앞서 있다 · 창에 떠나 반 바퀴 가면 닿을 때 목표가 그 자리에 ──
        {
            var s = ShipWorld.Create("Hwasal", 3);
            var o = s.Get<Outside>();
            int mars = o.PlaceOf("붉은 사막");
            long w = o.WindowWait(mars, s.Tick);
            double ahead = o.AheadDeg(mars, s.Tick + w), lead = Outside.LeadAngle(1, 1.52) * 180 / Math.PI;
            long T = (long)(Outside.VoyageDays(1, 1.52) * Time.TicksPerDay);
            double arrive = o.ShipTheta(s.Tick + w) + Outside.Travelled(1, 1.52, 1);
            double miss = Math.Abs(Math.IEEERemainder(arrive - o.PlaceTheta(mars, s.Tick + w + T), 2 * Math.PI)) * 180 / Math.PI;
            double inward = Outside.Travelled(1.52, 1, 1) * 180 / Math.PI, half = Outside.Travelled(1, 1.52, 0.5) * 180 / Math.PI;
            Check("발사 창: 기다린 만큼 지나면 붉은 사막이 꼭 앞선 각만큼 앞서 있다 · 그때 떠나 전이 궤도 반 바퀴를 가면 닿을 때 붉은 사막이 그 자리에 (0.1° 안) · 안으로도 반 바퀴",
                w > 0 && Math.Abs(ahead - lead) < 0.01 && miss < 0.1 && Math.Abs(inward - 180) < 1e-6 && half > 90,
                $"창까지 {w / (double)Time.TicksPerDay:0}일 · 그때 앞선 각 {ahead:0.00}° (셈 {lead:0.00}°) · 닿을 때 어긋남 {miss:0.000}° · 반쯤 간 각 {half:0.0}° (근일점에서 빠르다)");
        }
        // ── 엔진으로: 창에 떠난 배는 닿는 곳에 든다 · 30일 늦게 떠나면 목표가 앞서 가 버려 빈 궤도를 돈다 ──
        {
            (int at, double miss, string story) Fly(double lateDays)
            {
                var s = ShipWorld.Create("Hwasal", 4);
                var o = s.Get<Outside>();
                int mars = o.PlaceOf("붉은 사막");
                s.Run(3);
                long T = (long)(Outside.VoyageDays(1, 1.52) * Time.TicksPerDay);
                long dep = s.Tick - T;   // 전이 궤도를 다 지나 이번 틱에 닿게 (떠난 때를 앞으로 당긴다)
                o.AlignWindow(mars, dep - (long)(lateDays * Time.TicksPerDay));   // 창은 lateDays 일 앞이었다
                o.StartVoyage(s, 1.52); o.VoyageStart = dep; o.VoyagePlace = mars;
                s.Run(2);
                string st = s.Facts.Since(0).Where(f => f.Kind == FactKind.Burn && f.Other == -100).Select(f => s.Causes.Story(f.Cause)).FirstOrDefault() ?? "";
                return (o.AtPlace, o.ArrivalMissDeg, st);
            }
            var on = Fly(0); var late = Fly(30);
            var o0 = ShipWorld.Create("Hwasal", 4).Get<Outside>();
            int m = o0.PlaceOf("붉은 사막");
            Check("엔진: 창에 떠난 배는 붉은 사막에 든다 · 30일 늦게 떠나면 붉은 사막이 앞서 가 버려 (14°) 빈 궤도를 돈다 — '어긋났다'",
                on.at == m && on.miss < 0.1 && late.at == -1 && Math.Abs(late.miss - 30 * (Outside.Mean(1) - Outside.Mean(1.52)) * Time.TicksPerDay * 180 / Math.PI) < 0.5 && late.story.Contains("어긋났다"),
                $"제때 {on.miss:0.00}° → 든 곳 {on.at} · 30일 늦게 {late.miss:0.0}° → {late.at} · \"{late.story}\"");
        }
    }
}
