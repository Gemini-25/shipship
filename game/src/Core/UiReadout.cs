using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.2 화면이 읽어 가는 계산 (순수 함수 · 읽기 전용): 자원 추세 · "언제 문제가 되나" · 기록 묶기 · 기록 속 장소.
// 시뮬레이션 상태를 바꾸지 않는다 — 화면이 있든 없든 결과가 같다.

public readonly record struct Sample(float Hour, float Value);

/// <summary>최근 값들 (시간 · 값). 오래된 것부터 버린다.</summary>
public sealed class Trend
{
    private readonly List<Sample> _s = new();
    public int Capacity { get; }
    public IReadOnlyList<Sample> Samples => _s;

    public Trend(int capacity = 144) => Capacity = capacity;

    public void Add(float hour, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        _s.Add(new Sample(hour, value));
        if (_s.Count > Capacity) _s.RemoveRange(0, _s.Count - Capacity);
    }

    public void Clear() => _s.Clear();
    public float? Last => _s.Count > 0 ? _s[^1].Value : null;
}

public enum TrendDir { Flat, Up, Down }

public enum ResourceKey { Power, Oxygen, AirTank, CO2, Water, Food }

/// <summary>자원 하나의 지금 모습: 값 · 시간당 변화 · 문제가 될 때까지 남은 시간 · 떠올라야 하나.</summary>
public readonly record struct ResourceStatus(ResourceKey Key, float Value, float? SlopePerHour, float? HoursLeft, bool WarnNow, bool Surfaced, TrendDir Dir);

public static class Readout
{
    /// <summary>최근 windowHours 안의 표본으로 시간당 기울기 (최소제곱). 표본이 셋 미만이면 null.</summary>
    public static float? Slope(IReadOnlyList<Sample> s, float windowHours)
    {
        if (s.Count < 3) return null;
        float end = s[^1].Hour;
        int n = 0;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = s.Count - 1; i >= 0; i--)
        {
            if (end - s[i].Hour > windowHours) break;
            double x = s[i].Hour - end, y = s[i].Value;
            sx += x; sy += y; sxx += x * x; sxy += x * y;
            n++;
        }
        if (n < 3) return null;
        double den = n * sxx - sx * sx;
        if (Math.Abs(den) < 1e-9) return null;
        return (float)((n * sxy - sx * sy) / den);
    }

    /// <summary>
    /// 최근 추세가 이어지면 몇 시간 뒤에 문턱을 넘나 (falling: 문턱 아래로, 아니면 문턱 위로).
    /// 이미 넘었으면 0, 그쪽으로 움직이지 않거나(minRate보다 느리면) null.
    /// </summary>
    public static float? HoursUntil(IReadOnlyList<Sample> s, float threshold, bool falling, float windowHours = 3f, float minRate = 1e-4f)
    {
        if (s.Count == 0) return null;
        float now = s[^1].Value;
        if (falling ? now <= threshold : now >= threshold) return 0f;
        if (Slope(s, windowHours) is not float k) return null;
        if (falling ? k > -minRate : k < minRate) return null;
        return (threshold - now) / k;
    }

    public static TrendDir Direction(float? slope, float minRate) =>
        slope is not float k || MathF.Abs(k) < minRate ? TrendDir.Flat : k > 0 ? TrendDir.Up : TrendDir.Down;

    /// <summary>"14시간 뒤" · "40분 뒤" · "2.5일 뒤" · "지금".</summary>
    public static string When(float hours) =>
        hours <= 0.01f ? "지금" : hours < 1f ? $"{MathF.Max(1f, hours * 60f):0}분 뒤" : hours < 48f ? $"{hours:0}시간 뒤" : $"{hours / 24f:0.#}일 뒤";

    /// <summary>떠올라야 하나: 이미 주의 범위이거나, horizon 시간 안에 문제가 된다.</summary>
    public static bool Surface(float? hoursLeft, bool warnNow, float horizonHours) =>
        warnNow || hoursLeft is float h && h <= horizonHours;

    // ─────────────────────────── 기록 묶기 · 거르기 ───────────────────────────

    /// <summary>
    /// 같은 줄(같은 사람 · 같은 글)을 묶는다. 바로 앞 lookback 묶음 안에 같은 줄이 있으면 그 묶음에 더하고 맨 뒤(최근)로 옮긴다.
    /// 걸러진 줄만 보고, 최근 maxGroups 묶음을 오래된 것부터 돌려준다.
    /// </summary>
    public static List<LogGroup> Group(IReadOnlyList<LogEntry> entries, int maxGroups, Func<LogEntry, bool>? keep = null, int lookback = 3, int scanLimit = 400)
    {
        var groups = new List<LogGroup>();
        int start = Math.Max(0, entries.Count - scanLimit);
        for (int i = start; i < entries.Count; i++)
        {
            var e = entries[i];
            if (keep != null && !keep(e)) continue;
            int found = -1;
            for (int j = groups.Count - 1; j >= Math.Max(0, groups.Count - lookback); j--)
                if (groups[j].Last.CrewId == e.CrewId && groups[j].Last.Text == e.Text) { found = j; break; }
            if (found >= 0)
            {
                var g = groups[found];
                groups.RemoveAt(found);
                groups.Add(g with { Last = e, Count = g.Count + 1 });
            }
            else groups.Add(new LogGroup(e, e, 1));
        }
        if (groups.Count > maxGroups) groups.RemoveRange(0, groups.Count - maxGroups);
        return groups;
    }

    /// <summary>기록 거르기: 사람(-1이면 모두) · 방 이름(null이면 모두) · 종류(null이면 모두). 방은 글에 그 이름이 있거나 그 방 사람의 줄.</summary>
    public static bool Matches(LogEntry e, int crewId, string? roomName, LogKind? kind)
    {
        if (crewId >= 0 && e.CrewId != crewId) return false;
        if (kind is LogKind k && e.Kind != k) return false;
        if (roomName != null && !e.Text.Contains(roomName, StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>글 속에 나온 방 (가장 긴 이름부터 — "주방"이 "주방 창고" 안에서 먼저 잡히지 않게). 없으면 null.</summary>
    public static Room? RoomIn(string text, IReadOnlyList<Room> rooms)
    {
        Room? best = null;
        foreach (var r in rooms)
        {
            if (r.Detached) continue;
            string n = r.Name;
            if (n.Length < 2 || !text.Contains(n, StringComparison.Ordinal)) continue;
            if (best == null || n.Length > best.Name.Length) best = r;
        }
        return best;
    }
}

public readonly record struct LogGroup(LogEntry First, LogEntry Last, int Count);

/// <summary>
/// v16.2 자원 지켜보기: 화면이 10분마다 핵심 자원을 적어 두고, 최근 추세로 "언제 문제가 되나"를 낸다.
/// 읽기만 한다 (화면 · 시험이 같은 것을 쓴다).
/// </summary>
public sealed class ResourceWatch
{
    public const int SampleMinutes = 10;
    private readonly Trend[] _t;
    private long _last = long.MinValue;

    public ResourceWatch()
    {
        _t = new Trend[Enum.GetValues<ResourceKey>().Length];
        for (int i = 0; i < _t.Length; i++) _t[i] = new Trend(144); // 하루치
    }

    public Trend this[ResourceKey k] => _t[(int)k];

    /// <summary>(문턱, 줄어드는 쪽이 나쁜가, 추세 창 시간, 무시할 변화 속도, 미리 띄울 시간)</summary>
    public static (float threshold, bool falling, float window, float minRate, float horizon) Rule(ResourceKey k) => k switch
    {
        ResourceKey.Power => (0.1f, true, 1f, 0.01f, 6f),       // 배터리 10%
        ResourceKey.Oxygen => (19.5f, true, 2f, 0.05f, 24f),   // 평균 산소 19.5 kPa
        ResourceKey.AirTank => (0.05f, true, 2f, 0.004f, 24f), // 공기 탱크 5%
        ResourceKey.CO2 => (1.5f, false, 2f, 0.02f, 12f),      // 가장 짙은 방 CO2 1.5
        ResourceKey.Water => (60f, true, 3f, 0.5f, 24f),       // 물 60 L
        _ => (2f, true, 12f, 0.01f, 48f),                      // 먹을 것 이틀치
    };

    public static string Name(ResourceKey k) => k switch
    {
        ResourceKey.Power => "배터리",
        ResourceKey.Oxygen => "산소",
        ResourceKey.AirTank => "공기 탱크",
        ResourceKey.CO2 => "CO2",
        ResourceKey.Water => "물",
        _ => "식량",
    };

    /// <summary>지금 값 (읽기만).</summary>
    public static float Value(World w, ResourceKey k)
    {
        switch (k)
        {
            case ResourceKey.Power: return w.Power.BatteryPercent;
            case ResourceKey.AirTank: return w.Air.ReserveCapacity > 0f ? w.Air.Reserve / w.Air.ReserveCapacity : 0f;
            case ResourceKey.Water: return w.Water.Level;
            case ResourceKey.Food: return FoodPolicy.FoodDays(w);
        }
        float o2 = 0f, vol = 0f, co2 = 0f;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Abandoned || r.Detached) continue;
            o2 += r.Air.O2 * r.Volume;
            vol += r.Volume;
            if (r.Air.CO2 > co2) co2 = r.Air.CO2;
        }
        return k == ResourceKey.CO2 ? co2 : vol > 0f ? o2 / vol : 0f;
    }

    /// <summary>10분마다 적는다. 시간이 거꾸로 가면(불러오기 · 되감기) 처음부터.</summary>
    public void Sample(World w)
    {
        if (_last != long.MinValue && w.Tick < _last) Clear();
        if (_last != long.MinValue && w.Tick - _last < SimTime.Minutes(SampleMinutes)) return;
        _last = w.Tick;
        float hour = w.Tick / (float)SimTime.TicksPerHour;
        foreach (var k in Enum.GetValues<ResourceKey>()) _t[(int)k].Add(hour, Value(w, k));
    }

    public void Clear()
    {
        foreach (var t in _t) t.Clear();
        _last = long.MinValue;
    }

    /// <summary>지금 모습 (값 · 추세 · 남은 시간 · 떠오름). 산소는 방 공기와 공기 탱크 가운데 먼저 바닥나는 쪽.</summary>
    public ResourceStatus Status(World w, ResourceKey k)
    {
        var (th, falling, window, minRate, horizon) = Rule(k);
        var s = _t[(int)k].Samples;
        float value = Value(w, k);
        float? slope = Readout.Slope(s, window);
        float? left = Readout.HoursUntil(s, th, falling, window, minRate);
        if (s.Count == 0) left = falling ? (value <= th ? 0f : null) : (value >= th ? 0f : null);
        if (k == ResourceKey.Oxygen && Status(w, ResourceKey.AirTank).HoursLeft is float tank && w.Air.ReserveCapacity > 0f)
            left = left is float l ? MathF.Min(l, tank) : tank;
        bool warn = WarnNow(w, k, value);
        return new ResourceStatus(k, value, slope, left, warn, Readout.Surface(left, warn, horizon), Readout.Direction(slope, minRate));
    }

    /// <summary>지금 이미 주의 범위인가 (윗줄 칩의 노란 · 빨간 기준과 같다).</summary>
    public static bool WarnNow(World w, ResourceKey k, float v) => k switch
    {
        ResourceKey.Power => v < 0.2f || !w.Power.ReactorOnline || w.Power.ShedCount > 0,
        ResourceKey.Oxygen => v < 19.5f,
        ResourceKey.AirTank => v < 0.25f,
        ResourceKey.CO2 => v > 0.8f,
        ResourceKey.Water => v < 60f,
        _ => v < 3f || w.Food.Rationing,
    };
}
