using ShipEngine;

namespace Lab;

/// <summary>규칙 시험 — 게임 규칙 하나씩 (작은 배 · 몇 초). 새 기능 · 고친 버그마다 하나씩 더한다.</summary>
public static partial class Program
{
    private static void Rules()
    {
        Console.WriteLine("규칙 시험\n");

        if (Do("배도면"))
        {
            int n = 0;
            foreach (var key in Layout.ShipKeys())
            {
                var l = Layout.Ship(key, Tables.Default);
                var s = Worlds.FieldOnly(l, 1);
                var f = s.Get<Field>();
                s.Run(Time.Minutes(30));
                double lo = double.MaxValue, hi = 0;
                foreach (var r in f.Regions) { double p = f.Pressure(r[0]); lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
                Check($"배 도면 {l.Name}: 공기를 채우고 30분 — 새는 곳 없이 1기압 근처", lo > 98 && hi < 104 && f.VentedTotal < 1,
                    $"{l.W}×{l.H} · 방 {l.Rooms.Count} · 구역 {f.Regions.Count} · 압력 {lo:0.0}~{hi:0.0}kPa · 샌 공기 {f.VentedTotal:0.0}몰");
                n++;
            }
            if (n == 0) Check("배 도면이 하나 이상 있다", false);
        }

        if (Do("차압문"))
        {
            var l = Worlds.Ascii(Worlds.TwoRooms);
            var s = Worlds.FieldOnly(l, 1, f => f.SetHole(Worlds.At(f.L, 10, 9), 2.5f)); // 통로 아래 외판이 뚫렸다
            s.Run(Time.Minutes(10));
            var f = s.Get<Field>();
            double left = f.Pressure(Worlds.At(l, 3, 2)), right = f.Pressure(Worlds.At(l, 15, 2));
            Check("통로가 뚫리면 통로는 진공 · 보통 문틈 너머 방은 천천히 샌다 (10분에 4분의 1 아래) · 차압 문으로만 통로와 닿은 방은 더 천천히",
                f.Pressure(Worlds.At(l, 10, 7)) < 1 && right > 75 && left > right,
                $"왼쪽 방 {f.Pressure(Worlds.At(l, 3, 2)):0.0} · 오른쪽 방 {f.Pressure(Worlds.At(l, 15, 2)):0.0} · 통로 {f.Pressure(Worlds.At(l, 10, 7)):0.00} kPa");
        }
    }
}
