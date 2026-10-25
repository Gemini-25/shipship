namespace ShipEngine;

/// <summary>
/// ④ 틱 안 기록 (docs/PHYSICS.md §6-1 — PH5): 보기용 · 찍지 않는다 · 세계를 바꾸지 않는다 (켬 = 끔 — 법칙 13). <see cref="Structure.Track"/>가 null이면 셈하지 않는다 (게임 · 다시보기 세계만 켠다).
/// 한 틱 동안 깨어 있는 물체 · 몸마다 열쇠틀 (틱 처음 · k/16을 지난 잔걸음 끝 · 사건 · 틱 끝)을 남긴다 — 물체는 제 몸의 틀 (도면 m · 바닥에 대한 빠르기) · 몸은 세상 틀.
/// 그리는 쪽이 열쇠틀 사이를 에르미트로 (빠르기 · 각속도로) 메운다 — 고른 가속이면 꼭 맞는다. 부딪는 잔걸음으로 가는 토막은 곧게 (빠르기가 순간 바뀌었다).
/// 이번 틱에 사라진 물체 (우주로)의 생김새는 <see cref="Gone"/>에 — 그 열쇠틀의 마지막 때까지 그린다.
/// </summary>
public sealed class MotionTrack
{
    public const byte WhoProp = 0, WhoBody = 1;
    /// <summary>사건: 없음 · 부딪힘 (이 열쇠틀로 가는 토막은 곧게) · 섰다 (마찰로 — 그 때 그대로) · 사라짐 (우주로) · 막음 · 닿음 (떨어져 받침 · 바닥에 — PH7).</summary>
    public const byte EvNone = 0, EvHit = 1, EvStop = 2, EvGone = 3, EvPlug = 4, EvLand = 5;

    public struct Key
    {
        public byte Who, Ev;
        public int Id;
        public float T;              // 틱 처음에서 초 (0 ~ 2.4)
        public double X, Y, Th;      // 물체: 도면 m · 몸: 세상 m
        public double Vx, Vy, W;
        public float Z, Vz;          // 물체 밑면 높이 · 오르내림 (PH7 — 몸은 0)
    }

    /// <summary>기록한 틱 (그 틱의 걸음 — Tick − 1 끝에서 Tick 끝까지 · 그 밖이면 열쇠틀은 쓸모가 없다).</summary>
    public long Tick = long.MinValue;
    public double Dt = Time.SecondsPerTick;
    public readonly List<Key> Keys = new();
    /// <summary>이번 틱에 사라진 물체의 생김새 (마지막 열쇠틀의 때까지 그린다).</summary>
    public readonly List<Prop> Gone = new();
    private readonly Dictionary<long, (int at, int n)> _index = new();
    private Key[] _sorted = Array.Empty<Key>(), _buf = Array.Empty<Key>();
    private int _count;

    private sealed class ByWho : IComparer<Key>
    {
        public static readonly ByWho One = new();
        public int Compare(Key a, Key b) => a.Who != b.Who ? a.Who.CompareTo(b.Who) : a.Id != b.Id ? a.Id.CompareTo(b.Id) : a.T.CompareTo(b.T);
    }

    /// <summary>틱 처음 (④ 움직임 앞): 지난 틱 기록을 비운다.</summary>
    public void Begin(long tick, double dt) { Tick = tick; Dt = dt; Keys.Clear(); Gone.Clear(); _index.Clear(); _count = 0; }

    public void Add(byte who, int id, double t, double x, double y, double th, double vx, double vy, double w, byte ev = EvNone, float z = 0, float vz = 0) =>
        Keys.Add(new Key { Who = who, Id = id, T = (float)t, X = x, Y = y, Th = th, Vx = vx, Vy = vy, W = w, Ev = ev, Z = z, Vz = vz });

    /// <summary>틱 끝: 누구마다 때 차례로 묶는다 (그리는 쪽이 찾기 쉽게).</summary>
    public void End()
    {
        int n = Keys.Count;
        if (_buf.Length < n) _buf = new Key[Math.Max(n, _buf.Length * 2)];   // 다시 쓰는 버퍼 (틱마다 새로 잡지 않는다)
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Keys).CopyTo(_buf);
        Array.Sort(_buf, 0, n, ByWho.One);   // 안정 정렬이 아니어도 (누구 · Id · 때)가 다 같으면 같은 열쇠틀 — 넣은 차례로 이어 붙인 같은 때는 뒤 것이 사건
        _sorted = _buf; _count = n;
        _index.Clear();
        for (int i = 0; i < n;)
        {
            int j = i;
            while (j < n && _sorted[j].Who == _sorted[i].Who && _sorted[j].Id == _sorted[i].Id) j++;
            _index[KeyOf(_sorted[i].Who, _sorted[i].Id)] = (i, j - i);
            i = j;
        }
    }

    private static long KeyOf(byte who, int id) => (long)who << 32 | (uint)id;

    /// <summary>그 물체 · 몸의 열쇠틀 (때 차례 — 없으면 빈 것).</summary>
    public ReadOnlySpan<Key> Of(byte who, int id) => _index.TryGetValue(KeyOf(who, id), out var r) ? _sorted.AsSpan(r.at, r.n) : ReadOnlySpan<Key>.Empty;

    /// <summary>
    /// 틱 처음에서 t초의 자리 · 자세 (열쇠틀 사이 에르미트 — 빠르기 · 각속도로 · 부딪힘으로 가는 토막은 곧게 · 처음보다 앞이면 처음 · 끝보다 뒤면 끝). 열쇠틀이 없으면 거짓.
    /// </summary>
    public bool Pose(byte who, int id, double t, out double x, out double y, out double th)
    {
        var k = Of(who, id);
        x = y = th = 0;
        if (k.Length == 0) return false;
        if (t <= k[0].T) { x = k[0].X; y = k[0].Y; th = k[0].Th; return true; }
        if (t >= k[^1].T) { x = k[^1].X; y = k[^1].Y; th = k[^1].Th; return true; }
        int i = 1;
        while (i < k.Length - 1 && k[i].T < t) i++;
        ref readonly var a = ref k[i - 1];
        ref readonly var b = ref k[i];
        double h = b.T - a.T;
        if (h <= 1e-9) { x = b.X; y = b.Y; th = b.Th; return true; }
        double s = (t - a.T) / h;
        if (b.Ev == EvHit) { x = a.X + (b.X - a.X) * s; y = a.Y + (b.Y - a.Y) * s; th = a.Th + (b.Th - a.Th) * s; return true; }
        double s2 = s * s, s3 = s2 * s, h00 = 2 * s3 - 3 * s2 + 1, h10 = s3 - 2 * s2 + s, h01 = -2 * s3 + 3 * s2, h11 = s3 - s2;
        x = h00 * a.X + h10 * h * a.Vx + h01 * b.X + h11 * h * b.Vx;
        y = h00 * a.Y + h10 * h * a.Vy + h01 * b.Y + h11 * h * b.Vy;
        th = h00 * a.Th + h10 * h * a.W + h01 * b.Th + h11 * h * b.W;
        return true;
    }

    /// <summary>
    /// 틱 처음에서 t초의 밑면 높이 (열쇠틀 사이 에르미트 — 오르내림으로 · 닿음 (떨어져 받침에)으로 가는 토막은 곧게 · 처음보다 앞이면 처음 · 끝보다 뒤면 끝). 열쇠틀이 없으면 거짓.
    /// </summary>
    public bool Height(byte who, int id, double t, out double z)
    {
        var k = Of(who, id);
        z = 0;
        if (k.Length == 0) return false;
        if (t <= k[0].T) { z = k[0].Z; return true; }
        if (t >= k[^1].T) { z = k[^1].Z; return true; }
        int i = 1;
        while (i < k.Length - 1 && k[i].T < t) i++;
        ref readonly var a = ref k[i - 1];
        ref readonly var b = ref k[i];
        double h = b.T - a.T;
        if (h <= 1e-9) { z = b.Z; return true; }
        double s = (t - a.T) / h;
        if (b.Ev == EvHit) { z = a.Z + (b.Z - a.Z) * s; return true; }
        double s2 = s * s, s3 = s2 * s;
        double vb = b.Ev == EvLand ? 2 * (b.Z - a.Z) / h - a.Vz : b.Vz;   // 닿는 열쇠틀의 오르내림은 튄 뒤 값 — 닿기 바로 앞 (고른 가속)으로
        z = (2 * s3 - 3 * s2 + 1) * a.Z + (s3 - 2 * s2 + s) * h * a.Vz + (-2 * s3 + 3 * s2) * b.Z + (s3 - s2) * h * vb;
        return true;
    }

    /// <summary>사라진 물체의 마지막 때 (초 — 열쇠틀이 없으면 0).</summary>
    public double LastT(byte who, int id) { var k = Of(who, id); return k.Length == 0 ? 0 : k[^1].T; }

    /// <summary>크기 어림 (바이트 — 비용 줄).</summary>
    public long Bytes => (long)Keys.Capacity * System.Runtime.CompilerServices.Unsafe.SizeOf<Key>() * 2;
}
