namespace ShipEngine;

/// <summary>⑪ 장부의 덩어리 물자 (몰) — 식량(양분 · 포도당 몰) · 마실 물(물탱크) · 산소(배 공기 + 예비 공기).</summary>
public enum Res : byte { Food, Water, O2, Count }

/// <summary>⑪ 장부의 흐름 — 어디서 들어오고 어디로 나갔나. 들어옴은 +, 나감은 −로 적는다.</summary>
public enum Flow : byte
{
    // 들어옴
    Harvest,         // 작물을 거둬 식량 창고에 (식량)
    Recycled,        // 오수(오줌 · 닦은 물)를 걸러 되살렸다 (물)
    Condensed,       // 공기의 습기를 모았다 (물)
    Sabatier,        // 이산화탄소 + 수소 → 메탄 + 물 (물 — ISS 사바티에 장치)
    Electrolysis,    // 물을 갈라 낸 산소 (산소)
    Photosynthesis,  // 작물이 낸 산소 (산소)
    Made,            // 설비가 저절로 만들었다 (원료)
    Crafted,         // 작업대에서 만들었다 (물건)
    // 나감
    Eaten,           // 먹었다 (식량)
    Drunk,           // 마셨다 (물)
    Split,           // 산소를 만들려고 갈랐다 (물)
    Watered,         // 작물이 썼다 (물)
    Leaked,          // 새어 나갔다 (물)
    Breathed,        // 숨 쉬었다 (산소)
    Rotted,          // 썩었다 (식량 · 산소)
    Used,            // 썼다 (물건 — 고치기 · 막기 · 만들기 재료)
    Burned,          // 불에 탔다 (식량 · 산소)
    Rusted,          // 젖은 강철 벽이 녹슬며 먹었다 (산소)
    Spaced,          // 감압 바람에 구멍 밖 우주로 날아갔다 (물건)
    Count
}

/// <summary>
/// ⑪ 물자 장부 — 만들고 쓰는 곳마다 적는다 (엔진들이 직접 적는다 · 세계와 함께 찍힌다).
/// 재고는 세계에서 잰다(일과 물자 엔진) — 처음 재고 + 적은 흐름 = 지금 재고가 맞아야 한다 (안 맞는 몫은 '그 밖': 불 · 새어 나감 · 장부에 없는 길).
/// 한 시간마다 재고 · 누계를 고리에 남겨 '하루에 얼마나 쓰나 · 며칠 버티나'를 센다.
/// </summary>
public sealed class Book
{
    public const int RingHours = 25;                      // 고리: 한 시간마다 25칸 (24시간 앞과 견준다)
    public static readonly int Flows = (int)Flow.Count;
    public static readonly int Bulk = (int)Res.Count;

    public double[] BulkFlow = new double[Bulk * Flows]; // [물자 × 흐름] 누계
    public double[] ItemFlow = Array.Empty<double>();     // [물건 × 흐름] 누계
    public double[] Start = new double[Bulk];             // 처음 재고 (일과 물자 엔진이 첫 걸음에 잰다)
    public double[] ItemStart = Array.Empty<double>();
    public bool Started;
    public long StartTick;
    // 고리: 칸마다 [틱] + 물자(덩어리 · 물건)마다 [재고 · 들어옴 누계 · 나감 누계]
    public long[] RingTick = new long[RingHours];
    public double[] Ring = Array.Empty<double>();
    public int RingN, RingAt;

    public int Items => ItemFlow.Length / Flows;
    public int Kinds => Bulk + Items;

    public void Add(Res r, Flow f, double amount) => BulkFlow[(int)r * Flows + (int)f] += amount;

    public void AddItem(int item, Flow f, double n)
    {
        if (item < 0) return;
        EnsureItems(item + 1);
        ItemFlow[item * Flows + (int)f] += n;
    }

    public void EnsureItems(int n)
    {
        if (Items >= n) return;
        var a = new double[n * Flows]; ItemFlow.CopyTo(a, 0); ItemFlow = a;
        var s = new double[n]; ItemStart.CopyTo(s, 0); ItemStart = s;
        Ring = new double[RingHours * (Bulk + n) * 3]; RingN = 0; RingAt = 0; // 가짓수가 바뀌면 고리를 새로 (처음 한 번)
    }

    public double Of(Res r, Flow f) => BulkFlow[(int)r * Flows + (int)f];
    public double OfItem(int item, Flow f) => item >= 0 && item < Items ? ItemFlow[item * Flows + (int)f] : 0;

    /// <summary>물자 k(덩어리 0..Bulk−1, 그 뒤로 물건)의 흐름 누계: 들어옴 · 나감(양수로) · 합.</summary>
    public (double inn, double outt) Totals(int k)
    {
        double i = 0, o = 0;
        for (int f = 0; f < Flows; f++)
        {
            double v = k < Bulk ? BulkFlow[k * Flows + f] : ItemFlow[(k - Bulk) * Flows + f];
            if (v > 0) i += v; else o -= v;
        }
        return (i, o);
    }

    /// <summary>한 시간마다: 재고 · 누계를 고리에 넣는다 (stock: 물자 k의 지금 재고).</summary>
    public void Snapshot(long tick, Func<int, double> stock)
    {
        int K = Kinds;
        if (Ring.Length != RingHours * K * 3) { Ring = new double[RingHours * K * 3]; RingN = 0; RingAt = 0; }
        RingTick[RingAt] = tick;
        for (int k = 0; k < K; k++)
        {
            var (i, o) = Totals(k);
            int at = (RingAt * K + k) * 3;
            Ring[at] = stock(k); Ring[at + 1] = i; Ring[at + 2] = o;
        }
        RingAt = (RingAt + 1) % RingHours;
        RingN = Math.Min(RingHours, RingN + 1);
    }

    /// <summary>고리의 가장 오래된 칸과 지금(nowStock)을 견준 하루 빠르기: 쓰는 양 · 만드는 양 (하루에) — 한 시간도 안 쌓였으면 (0, 0).</summary>
    public (double use, double make) PerDay(int k, long now)
    {
        if (RingN == 0) return (0, 0);
        int K = Kinds;
        int oldest = RingN < RingHours ? 0 : RingAt;
        long t0 = RingTick[oldest];
        double days = (now - t0) * Time.SecondsPerTick / 86400;
        if (days < 1.0 / 24 - 1e-9) return (0, 0);
        var (i, o) = Totals(k);
        int at = (oldest * K + k) * 3;
        return ((o - Ring[at + 2]) / days, (i - Ring[at + 1]) / days);
    }

    /// <summary>마지막 한 시간 칸의 재고 (고리가 비었으면 −1).</summary>
    public double LastStock(int k)
    {
        if (RingN == 0) return -1;
        int K = Kinds, at = (((RingAt - 1 + RingHours) % RingHours) * K + k) * 3;
        return Ring[at];
    }

    /// <summary>며칠 버티나 (마지막 재고 ÷ 하루에 줄어드는 양) — 모르거나 줄지 않으면 무한. 사람 · 주컴퓨터가 읽는 '창고를 보면 아는' 값.</summary>
    public double DaysLeft(int k, long now)
    {
        double st = LastStock(k);
        var (use, make) = PerDay(k, now);
        return st < 0 || use - make <= 1e-9 ? double.PositiveInfinity : st / (use - make);
    }

    public void Save(StateWriter w)
    {
        w.Arr(BulkFlow); w.Arr(ItemFlow); w.Arr(Start); w.Arr(ItemStart); w.Bool(Started); w.I64(StartTick);
        w.Arr(RingTick); w.Arr(Ring); w.I32(RingN); w.I32(RingAt);
    }

    public void Load(StateReader r)
    {
        BulkFlow = r.Arr<double>(); ItemFlow = r.Arr<double>(); Start = r.Arr<double>(); ItemStart = r.Arr<double>(); Started = r.Bool(); StartTick = r.I64();
        RingTick = r.Arr<long>(); Ring = r.Arr<double>(); RingN = r.I32(); RingAt = r.I32();
    }
}
