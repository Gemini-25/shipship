namespace ShipEngine;

/// <summary>시계: 1틱 = 게임 2.4초 · 한 시간 1500틱 (옛 게임과 같은 빠르기).</summary>
public static class Time
{
    public const int TicksPerHour = 1500;
    public const int TicksPerDay = TicksPerHour * 24;
    public const double SecondsPerTick = 3600.0 / TicksPerHour; // 2.4

    public static long Minutes(double m) => (long)Math.Round(m * TicksPerHour / 60.0);
    public static long Hours(double h) => (long)Math.Round(h * TicksPerHour);
    public static long Days(double d) => (long)Math.Round(d * TicksPerDay);
    public static double ToSeconds(long ticks) => ticks * SecondsPerTick;
    /// <summary>하루 중 몇 시 (0~24).</summary>
    public static double HourOfDay(long tick) => (tick % TicksPerDay) / (double)TicksPerHour;

    /// <summary>"3일 07:42" — 게임 안 시각.</summary>
    public static string Clock(long tick)
    {
        long day = tick / TicksPerDay;
        double sec = (tick % TicksPerDay) * SecondsPerTick;
        int h = (int)(sec / 3600), m = (int)(sec % 3600 / 60);
        return $"{day + 1}일 {h:00}:{m:00}";
    }
}
