namespace ShipEngine;

/// <summary>판단 일지의 결과 갈래 (docs/MIND.md 4단 — Turned: 막힘이 아니라 제 판단으로 다른 곳으로 돌아섬 · 뒤 셋은 8 · 10단 자리).</summary>
public enum JudgeOut : byte { Set, Done, Blocked, Dropped, CantPlan, Assigned, Released, Turned, Hesitate, Defer, Forgot }

/// <summary>판단 하나 — 언제 · 무엇을 (목표 · 대상 · 자리) · 어떻게 (첫 손 걸음 · 든 것) · 어떻게 됐나 · 왜 (막힌 까닭 · 이미 지은 까닭 글의 참조) · 누가 정했나 (-1 제 판단 · -2 주컴퓨터) · 맡긴 까닭.</summary>
public struct Judgment
{
    public long Tick;
    public GoalKind Goal;
    public int Target, Cell;
    public byte Method, Held;
    public JudgeOut Out;
    public SnagKind Why;
    public int By;
    public byte Reason;
    public string? Note;
}

/// <summary>주컴퓨터 일지의 갈래.</summary>
public enum NoteKind : byte { Other, Assign, Defer, Release, Block, Seal, Door, Vent, Power, Plan, Ask, Job, Stuck }

/// <summary>주컴퓨터가 한 말 하나 — 갈래 · 맡긴 로봇 · 사건 번호 · 까닭 갈래 · 글 (이미 지은 글을 그대로 담는다).</summary>
public struct ComputerNote
{
    public long Tick;
    public NoteKind Kind;
    public int Robot, Incident;
    public byte Reason;
    public string Text;
}

/// <summary>
/// 판단 일지 고리 (docs/MIND.md 4단 '판단 일지 구조체'): 쓰기만 — 판단 · 연출 · 사회는 읽지 않고 (읽는 것은 화면뿐) · 찍지 않는다.
/// 세계마다 켠다 (<see cref="Mind.Journal"/> · <see cref="ShipMind.NoteOn"/> — 기본 끔: 되살린 세계 · 복제 · 상상 · 앞선 세계는 쓰지 않아 할당도 없다 · 화면과 시험이 산 세계에서 켠다).
/// 원인 마디 · 사실 · 소리 · 난수를 부르지 않고 글을 새로 짓지 않는다 (열거형 · 번호 · 이미 지은 글의 참조만). 고리는 첫 쓰기 때 한 번 잡는다.
/// </summary>
public sealed class Ring<T> where T : struct
{
    private T[]? _a;
    private int _head, _n;
    private readonly int _cap;
    public Ring(int cap) { _cap = cap; }
    public int Count => _n;
    public void Add(in T x)
    {
        _a ??= new T[_cap];
        _a[_head] = x; _head = (_head + 1) % _cap;
        if (_n < _cap) _n++;
    }
    /// <summary>k번째로 새 것 (0: 가장 새 것 · k &lt; Count).</summary>
    public ref T Newest(int k) => ref _a![(_head - 1 - k + 2 * _cap) % _cap];
}
