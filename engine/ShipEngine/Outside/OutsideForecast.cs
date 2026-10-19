namespace ShipEngine;

/// <summary>
/// ⑤ 내다보기 (⑫ 긴 줄기 (가) · docs/ENGINE.md ⑤ '긴 줄기'): 지금 항해의 남은 전이 궤도에서 구간마다 드는 · 나오는 때 · 앞으로 태양 합이 드는 · 끝나는 때 —
/// 셈만 한다 (세상을 바꾸지 않는다). 도착 점화가 제때라고 보고 · 닿은 뒤에는 그 궤도를 돈다고 본다. 주컴퓨터(⑩)가 별계 지도로 읽는다.
/// </summary>
public sealed partial class Outside
{
    public struct Span { public int Zone; public long From, To; }   // Zone: 구간 번호 (태양 합은 -1)

    private long ArriveTick => VoyageStart + VoyageTicks;

    /// <summary>그때의 별까지 거리 (전이 궤도 · 닿은 뒤 그 궤도 · 머물면 그대로).</summary>
    public double DistanceAt(long tick)
    {
        if (VoyageTicks <= 0) return Distance;
        double frac = (tick - VoyageStart) / (double)VoyageTicks;
        if (Missed == 0 && frac >= 1) return VoyageTo;
        return TransferDistance(VoyageFrom, VoyageTo, Missed != 0 ? frac : Math.Max(0, frac), Missed != 0);
    }

    /// <summary>그때의 배의 각 (닿은 뒤에는 그 궤도를 돈다고 본다).</summary>
    public double ShipThetaAt(long tick)
    {
        if (VoyageTicks <= 0 || Missed != 0 || tick <= ArriveTick) return ShipTheta(tick);
        return ShipTheta(ArriveTick) + Mean(VoyageTo) * (tick - ArriveTick);
    }

    /// <summary>그때 고향에서 본 별과 배 사이 각 (도).</summary>
    public double SepDegAt(long tick)
    {
        if (Places.Count == 0) return 180;
        double home = Places[Home].Au, r = DistanceAt(tick), ang = ShipThetaAt(tick) - PlaceTheta(Home, tick);
        double sx = r * Math.Cos(ang) - home, sy = r * Math.Sin(ang);
        double len = Math.Sqrt(sx * sx + sy * sy);
        return len < 1e-9 ? 180 : Math.Acos(Math.Clamp(-sx / len, -1, 1)) * 180 / Math.PI;
    }

    private int ZoneIndexAt(long tick) { var z = ZoneAt(DistanceAt(tick)); return z == null ? -1 : T.Space.Zones.IndexOf(z); }

    /// <summary>남은 전이 궤도에서 지나는 구간들 (드는 때 · 나오는 때 — 경계는 나눠 찾아 한 틱 안 · 지금 안이면 From = 지금).</summary>
    public List<Span> ZoneSpans(long now)
    {
        var spans = new List<Span>();
        if (VoyageTicks <= 0 || Planet != null) return spans;
        long end = ArriveTick;
        if (end <= now) return spans;
        const int N = 2000;
        long Edge(long a, long b, int za)   // a는 za · b는 아니다 — 처음 바뀌는 틱
        {
            while (b - a > 1) { long m = a + (b - a) / 2; if (ZoneIndexAt(m) == za) a = m; else b = m; }
            return b;
        }
        long prev = now; int zp = ZoneIndexAt(now); long start = now;
        for (int i = 1; i <= N; i++)
        {
            long t = now + (end - now) * i / N;
            int z = ZoneIndexAt(t);
            if (z == zp) { prev = t; continue; }
            long at = Edge(prev, t, zp);
            if (zp >= 0) spans.Add(new Span { Zone = zp, From = start, To = at });
            zp = ZoneIndexAt(at); start = at; prev = at;
            if (zp != z) { i--; continue; }   // 한 칸 안에 둘이 바뀌었다 — 다시 본다
            prev = t;
        }
        if (zp >= 0) spans.Add(new Span { Zone = zp, From = start, To = end });
        return spans;
    }

    /// <summary>앞으로 [from, to) 사이의 태양 합 (6시간마다 셈하고 경계는 나눠 찾아 1분 안).</summary>
    public List<Span> ConjSpans(long from, long to)
    {
        var spans = new List<Span>();
        if (Route <= 0) return spans;
        long step = Time.Hours(6);
        bool In(long t) => SepDegAt(t) < ConjunctionDeg;
        long Edge(long a, long b, bool ina)
        {
            while (b - a > Time.Minutes(1)) { long m = a + (b - a) / 2; if (In(m) == ina) a = m; else b = m; }
            return b;
        }
        bool inside = In(from); long start = from, prev = from;
        for (long t = from + step; t < to + step; t += step)
        {
            long tt = Math.Min(t, to);
            bool i = In(tt);
            if (i != inside)
            {
                long at = Edge(prev, tt, inside);
                if (inside) spans.Add(new Span { Zone = -1, From = start, To = at });
                inside = i; start = at;
            }
            prev = tt;
            if (tt >= to) break;
        }
        if (inside) spans.Add(new Span { Zone = -1, From = start, To = to });
        return spans;
    }
}
