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
    DoorBlown,         // 문이 압력에 날아갔다
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
    Warned,            // 외쳐 알렸다
    Panicked,          // 공황에 빠졌다
    Fled,              // 몸을 피했다 (본능)
    Extinguished,      // 불을 껐다
    Patched,           // 구멍을 막았다
    Repaired,          // 장치를 고쳤다
    DoorSealed,        // 도망치며 문을 닫았다
    Woke,              // 잠에서 깼다
    ComputerAlarm,     // 주컴퓨터가 경보를 울렸다 (Other: 사건 종류)
    ComputerSeal,      // 주컴퓨터가 방을 봉쇄했다 (문을 닫았다)
    ComputerAssign,    // 주컴퓨터가 로봇에게 일을 맡겼다
    LoadShed,          // 전력이 모자라 장치를 껐다
    BreakerReset,      // 떨어진 차단기를 다시 올렸다
    ComputerDown,      // 주컴퓨터가 멈췄다
    // ⑫
    StorySeed,         // 이야기꾼이 원인을 심었다
    // ⑨
    Grieved,           // 가까운 이를 잃었다 (Actor: 남은 사람 · Other: 죽은 사람)
    TrustLost,         // 주컴퓨터를 덜 믿게 됐다
}

public static class FactNames
{
    /// <summary>화면 · 기록에 쓰는 이름.</summary>
    public static string Of(FactKind k) => k switch
    {
        FactKind.RegionVented => "공기가 다 빠졌다", FactKind.PressureDrop => "기압이 떨어진다", FactKind.Ignition => "불이 붙었다", FactKind.FireOut => "불이 꺼졌다",
        FactKind.FireSpread => "불이 번졌다", FactKind.SmokeThick => "연기가 짙다", FactKind.Toxic => "독한 기체", FactKind.Condensation => "이슬이 맺혔다",
        FactKind.Flood => "물이 찼다", FactKind.LoudSound => "큰 소리", FactKind.WallCrack => "금이 갔다", FactKind.WallBreach => "벽이 뚫렸다",
        FactKind.Impact => "충돌", FactKind.DoorJammed => "문이 끼었다", FactKind.DoorBlown => "문이 날아갔다", FactKind.ObjectFlung => "물건이 날아갔다",
        FactKind.DebrisNear => "무언가 다가온다", FactKind.Flare => "별 폭발", FactKind.BreakerTrip => "차단기가 떨어졌다", FactKind.Short => "합선",
        FactKind.Overheat => "과열", FactKind.PipeLeak => "관이 샌다", FactKind.MachineFail => "고장", FactKind.Hurt => "다쳤다",
        FactKind.Collapsed => "쓰러졌다", FactKind.Died => "죽었다", FactKind.Sick => "병", FactKind.Noticed => "알아챘다",
        FactKind.Rescue => "구했다", FactKind.Decision => "결정", FactKind.Mistake => "실수", FactKind.Relationship => "사이가 달라졌다",
        FactKind.Warned => "외쳐 알렸다", FactKind.Panicked => "공황에 빠졌다", FactKind.Fled => "몸을 피했다", FactKind.Extinguished => "불을 껐다",
        FactKind.Patched => "구멍을 막았다", FactKind.Repaired => "고쳤다", FactKind.DoorSealed => "문을 닫았다", FactKind.Woke => "잠에서 깼다",
        FactKind.ComputerAlarm => "경보", FactKind.ComputerSeal => "봉쇄", FactKind.ComputerAssign => "로봇을 보냈다", FactKind.LoadShed => "장치를 껐다",
        FactKind.BreakerReset => "차단기를 올렸다", FactKind.ComputerDown => "주컴퓨터가 멈췄다", FactKind.StorySeed => "조짐",
        FactKind.Grieved => "슬픔", FactKind.TrustLost => "믿음이 깨졌다", _ => "",
    };
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
