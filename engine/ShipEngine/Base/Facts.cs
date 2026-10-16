namespace ShipEngine;

/// <summary>사실의 종류 — 엔진이 '일어났다'고 밖에 알리는 것. 연출 · 기록 · 시험이 읽는다 (마음은 직접 읽지 않는다).</summary>
public enum FactKind : ushort
{
    None,
    // ③ 장
    RegionVented,      // 구역이 진공이 됐다
    PressureDrop,      // 구역 압력이 빠르게 떨어진다
    Ignition,          // 불이 붙었다
    FireOut,           // 불이 꺼졌다
    FireSpread,        // 불이 옆 칸으로 옮았다
    SmokeThick,        // 연기가 짙다
    Toxic,             // 독한 기체
    Condensation,      // 이슬 · 물이 고였다
    Flood,             // 물이 찼다
    LoudSound,         // 큰 소리
    // ④ 구조
    WallCrack,         // 금이 갔다
    WallBreach,        // 터졌다
    Impact,            // 충돌
    DoorJammed,        // 문이 끼었다
    ObjectFlung,       // 물건이 날아갔다
    // ⑤ 바깥
    DebrisNear,        // 파편이 다가온다
    Flare,             // 별 폭발
    // ⑥ 망
    BreakerTrip,       // 차단기가 떨어졌다
    Short,             // 합선
    Overheat,          // 과열
    PipeLeak,          // 관이 샌다
    MachineFail,       // 장치 고장
    // ⑦ 생명
    Hurt,              // 다쳤다
    Collapsed,         // 쓰러졌다
    Died,              // 죽었다
    Sick,              // 병
    // ⑧~⑪ 마음 · 사회 · 주컴퓨터
    Noticed,           // 알아챘다
    Rescue,            // 구했다
    Decision,          // 결정
    Mistake,           // 실수
    Relationship,      // 관계가 바뀌었다
    // ⑫
    StorySeed,         // 이야기꾼이 원인을 심었다
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)] // 찍기는 바이트를 그대로 쓴다 — 정렬용 빈 바이트(쓰레기 값)가 없게
public struct Fact
{
    public long Tick;
    public FactKind Kind;
    public int Cell;     // -1: 배 전체
    public int Actor;    // -1: 없음
    public int Other;    // 구역 · 문 · 상대 등 (종류마다)
    public float Size;   // 크기 (종류마다 — 압력 kPa · 에너지 kJ · 칸 수 …)
    public int Cause;    // 원인 마디
}

/// <summary>사실 흐름: 고리 버퍼 (최근 것만) + 종류별 누계.</summary>
public sealed class Facts : IState
{
    public const int Capacity = 8192;
    private Fact[] _ring = new Fact[Capacity];
    private long _total;
    private long[] _byKind = new long[64];

    public long Total => _total;
    public long CountOf(FactKind k) => _byKind[(int)k];

    public void Emit(in Fact f)
    {
        _ring[_total % Capacity] = f;
        _total++;
        _byKind[(int)f.Kind]++;
    }

    public void Emit(long tick, FactKind kind, int cell = -1, float size = 0, int cause = 0, int actor = -1, int other = -1)
        => Emit(new Fact { Tick = tick, Kind = kind, Cell = cell, Size = size, Cause = cause, Actor = actor, Other = other });

    /// <summary>번호 from(누계 기준) 이후의 사실들 (고리에 남은 것만).</summary>
    public IEnumerable<Fact> Since(long from)
    {
        long start = Math.Max(from, _total - Capacity);
        for (long i = start; i < _total; i++) yield return _ring[i % Capacity];
    }

    public IEnumerable<Fact> Recent(int n) => Since(_total - n);

    public void Save(StateWriter w)
    {
        w.Tag("facts");
        w.I64(_total);
        w.Arr(_ring);
        w.Arr(_byKind);
    }

    public void Load(StateReader r)
    {
        r.Tag("facts");
        _total = r.I64();
        _ring = r.Arr<Fact>();
        _byKind = r.Arr<long>();
    }
}
