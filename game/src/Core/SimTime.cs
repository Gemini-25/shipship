using System;

namespace ShipSim.Core;

/// <summary>
/// 시뮬레이션 시간 규칙.
/// 틱 하나의 길이는 고정이고, 배속은 "현실 1초에 틱을 몇 번 돌리느냐"로만 바뀐다.
/// 그래서 1배속이든 30배속이든 결과가 같다(결정론).
/// </summary>
public static class SimTime
{
    /// <summary>1배속에서 현실 1초에 도는 틱 수.</summary>
    public const int TicksPerSecond = 30;

    /// <summary>게임 1시간 = 1500틱 = 1배속 기준 현실 50초.</summary>
    public const int TicksPerHour = 1500;

    /// <summary>게임 하루 = 36000틱 = 1배속 기준 현실 20분.</summary>
    public const int TicksPerDay = TicksPerHour * 24;

    public static int Minutes(float minutes) => (int)MathF.Round(minutes * TicksPerHour / 60f);
    public static int Hours(float hours) => (int)MathF.Round(hours * TicksPerHour);

    public static int Day(long tick) => (int)(tick / TicksPerDay) + 1;
    public static float HourOfDay(long tick) => (tick % TicksPerDay) / (float)TicksPerHour;

    public static string Clock(long tick)
    {
        float h = HourOfDay(tick);
        int hh = (int)h;
        int mm = (int)((h - hh) * 60f);
        return $"{hh:00}:{mm:00}";
    }

    /// <summary>24시간 원형 시간창 안인지. start=23, length=8 이면 23:00~07:00.</summary>
    public static bool InWindow(float hour, float start, float length) => Wrap(hour - start) < length;

    /// <summary>from 시각에서 to 시각까지 몇 시간 남았는지 (0 이상 24 미만).</summary>
    public static float HoursFromTo(float from, float to) => Wrap(to - from);

    public static float Wrap(float hours)
    {
        float d = hours % 24f;
        return d < 0 ? d + 24f : d;
    }

    public static string Range(float start, float length) =>
        $"{(int)Wrap(start):00}:00–{(int)Wrap(start + length):00}:00";
}
