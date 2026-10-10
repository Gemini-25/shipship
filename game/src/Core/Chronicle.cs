using System.Collections.Generic;

namespace ShipSim.Core;

public enum LogKind
{
    Life,     // 일상 (식사, 수면, 대화)
    Work,     // 작업 (정비, 수리, 수확, 조리)
    Ship,     // 우주선 상태
    Warning,  // 경고 (고장, 정전, 부족)
}

public readonly record struct LogEntry(long Tick, LogKind Kind, string Text, int CrewId);

/// <summary>
/// 사건 기록. 지금은 일상과 고장 기록이지만, 나중에 "우주선의 역사"의 토대가 된다.
/// </summary>
public sealed class Chronicle
{
    private readonly List<LogEntry> _entries = new();

    public int Capacity { get; set; } = 800;
    public IReadOnlyList<LogEntry> Entries => _entries;

    public int Version { get; private set; }

    public void Add(long tick, LogKind kind, string text, int crewId = -1)
    {
        _entries.Add(new LogEntry(tick, kind, text, crewId));
        if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        Version++;
    }
}

public enum AlertLevel { Notice, Warning, Critical }

/// <summary>경보 한 건. 누가 알아챘는지는 경보마다 다르다 (같은 방, 우주선 전체 방송).</summary>
public sealed record Alert(long Tick, string Text, Room? Room, AlertLevel Level, bool ShipWide)
{
    /// <summary>v10.3: 울린 순서 (1부터).</summary>
    public long Serial { get; init; }
}
