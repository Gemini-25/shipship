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

/// <summary>주컴퓨터가 한 예측의 갈래 (docs/UI2.md §4.5 — 화면용 예측 고리): 봉쇄 상상 · 미리 돌려 보기 · 걸음 시작 · 걸음 끝 · 물자 날 · 대피 도착.</summary>
public enum GuessKind : byte { Seal, Rehearse, Step, StepEnd, Supply, Shelter }

/// <summary>
/// 예측 하나 (숫자 · 번호만 — 글을 짓지 않는다): 때 · 갈래 · 방 · 대상 (문 · 걸음 번호 · 구름) · 사건 · 값 셋 · 고른 것.
/// Seal: A 죽음 · B 쓰러짐 · C 다른 방이 잃은 기압 (고른 미래) · Verdict 0 기다림 · 1 지금 닫음 / Rehearse: A 쓰러짐 (더) · B 잃는 공기 몰 · C 셈한 공기 몰 · Verdict 1 연다 · 0 하지 않는다 /
/// Step: A 걸릴 분 / StepEnd: Verdict 1 됐다 · 0 못 했다 / Supply: A 식량 날 · B 물 날 · Verdict 배급 비트 / Shelter: A 닿기까지 초 · Verdict 1 파편 · 2 방사선.
/// </summary>
public struct Guess
{
    public long Tick;
    public GuessKind Kind;
    public int Room, Obj, Incident;
    public float A, B, C;
    public byte Verdict;
}

/// <summary>상상 그림 (화면용 — 찍지 않는다 · 판단은 읽지 않는다): 복제 하나가 5분 뒤 끝난 모습 — 방마다 첫 칸 기압 · 사람마다 칸과 상태 (0 멀쩡 · 1 쓰러짐 · 2 숨짐).</summary>
public sealed class ImagineEnd
{
    public float[] RoomKPa = Array.Empty<float>();
    public int[] Cell = Array.Empty<int>();
    public byte[] State = Array.Empty<byte>();
}

/// <summary>마지막 봉쇄 상상의 두 그림 (화면용 · 마지막 하나 · 찍지 않는다): 상상한 때 · 방 · 사건 · 고른 안 · 기다림 · 지금 닫음.</summary>
public sealed class ImagineShot
{
    public long Tick;
    public int Room, Incident;
    public byte Chosen;
    public ImagineEnd? Wait, Now;
}

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
    /// <summary>지금까지 넣은 수 (밀려난 것 포함 — 화면이 새 것만 읽는 자리표).</summary>
    public long Added { get; private set; }
    public void Add(in T x)
    {
        _a ??= new T[_cap];
        _a[_head] = x; _head = (_head + 1) % _cap;
        if (_n < _cap) _n++;
        Added++;
    }
    /// <summary>지금까지 넣은 수를 옮겨 놓는다 (이어 보기 — 되살린 고리에 다시 넣은 뒤 원래의 차례 번호로: 화면 줄의 열쇠가 그대로).</summary>
    public void Rebase(long added) { if (added >= _n) Added = added; }
    /// <summary>k번째로 새 것 (0: 가장 새 것 · k &lt; Count).</summary>
    public ref T Newest(int k) => ref _a![(_head - 1 - k + 2 * _cap) % _cap];
}
