namespace ShipEngine;

/// <summary>
/// ⑪ 공급 사슬 (마) — 보급 (docs/ENGINE.md ⑪): 다른 배 · 정거장의 형편은 세상이다 — 부탁을 받으면 나눠 줄지 · 무엇을 몇 개 줄지는 그 배의 형편(세상의 확률 · 난수 갈래 '보급'),
/// 주기로 하면 그 배가 맞춰 와서 붙는다 (빠르기 = 거리 ÷ 10분 · 많아야 6km/s · 가까이서 2m/s). 부탁할지는 주컴퓨터(⑩)가 · 받은 것을 넣는 것은 마음(⑧).
/// <para>나눠 줄 몫 (처음 값 — 정밀한 조절은 맨 끝에): 정거장 0.95 · 상선 0.7 · 순찰선 0.4 / 부탁한 것마다 정거장 2~4개 · 상선 1~2개 · 순찰선 1개.
/// 정거장(space.json stations)은 그 거리를 지날 때 기울기가 낮은 항로만 곁을 지난다 (5,000km 곁 · 300m/s로 지난다 · 한 번).</para>
/// </summary>
public sealed partial class Outside
{
    /// <summary>보급 물건의 차례 (선물 비트 자리).</summary>
    public static readonly string[] SupplyItems = { "원료", "전자 소자", "전기 부품", "기계 부품", "봉함재", "거르개" };
    public const double MeetTau = 600, MeetMaxSpeed = 6000, MeetSlow = 2;
    public double ShareTrader = 0.7, SharePatrol = 0.4, ShareStation = 0.95;   // 나눠 줄 몫 (시험이 바꾼다)

    public int StationsMet;              // 지난 정거장 (비트)
    public long SupplyAsked, SupplyGiven;

    public static int GiftOf(long gift, int k) => (int)(gift >> (8 * k) & 0xFF);

    /// <summary>정거장 곁을 지난다: 그 거리에 닿고 · 항로가 그 정거장 곁을 지나면 (기울기) 교신 거리에 놓는다 (한 번).</summary>
    private void StationsStep(Sim sim)
    {
        var st = T.Space.Stations;
        for (int i = 0; i < st.Count && i < 31; i++)
        {
            if ((StationsMet >> i & 1) != 0 || Math.Abs(Distance - st[i].Au) > 0.005) continue;
            if (Rt != null && Rt.Incline > st[i].MaxIncline) continue;   // 높은 항로는 곁을 지나지 않는다
            StationsMet |= 1 << i;
            int cause = sim.Causes.Add(CauseKind.Contact, $"{st[i].Name} 곁을 지난다 — {st[i].Line}", sim.Tick);
            int id = AddContact(sim, ContactKind.Station, 5e6, 1, 0, 300, 2e5, 0, 0, cause);
            int k = ContactIndex(id); var c = Contacts[k]; c.NameNo = i; c.Beacon = 1; Contacts[k] = c;
        }
    }

    /// <summary>맞춰 오는 배: 우리 쪽으로 (거리 ÷ 10분 · 많아야 6km/s · 가까이서 2m/s) — 붙는 것은 '다가오는 배'의 법칙대로.</summary>
    private static void MeetStep(ref Contact c)
    {
        double R = c.Range;
        if (R < 1e-3) return;
        double v = Math.Min(MeetMaxSpeed, Math.Max(R / MeetTau, MeetSlow));
        c.Vx = -c.X / R * v; c.Vy = -c.Y / R * v;
        if (c.State == ContactState.Far) c.State = ContactState.Coming;
    }

    /// <summary>
    /// 주컴퓨터의 부탁 (want: 보급 물건 차례마다 모자란 수 — 0이면 부탁하지 않는다): 그 배의 형편으로 나눠 줄지 · 무엇을 몇 개 — 주면 맞춰 온다.
    /// 돌려주는 것: 나눠 주나 · 줄 것 (차례마다).
    /// </summary>
    public (bool yes, int[] give) AskSupply(Sim sim, int id, int[] want)
    {
        var give = new int[SupplyItems.Length];
        int k = ContactIndex(id);
        if (k < 0) return (false, give);
        var c = Contacts[k];
        SupplyAsked++;
        double share = c.Kind switch { ContactKind.Station => ShareStation, ContactKind.Trader => ShareTrader, ContactKind.Patrol => SharePatrol, _ => 0 };
        if (Rng.Unit(sim.Seed, Rng.Key("보급"), sim.Tick, c.Id) >= share) return (false, give);
        long gift = 0;
        for (int i = 0; i < give.Length && i < want.Length; i++)
        {
            if (want[i] <= 0) continue;
            double u = Rng.Unit(sim.Seed, Rng.Key("보급 물건"), sim.Tick, c.Id * 8 + i);
            give[i] = c.Kind switch { ContactKind.Station => 2 + (int)(3 * u), ContactKind.Trader => 1 + (int)(2 * u), _ => 1 };
            gift |= (long)Math.Min(255, give[i]) << (8 * i);
        }
        if (gift == 0) return (false, give);
        c.Gift = gift; c.Meet = 1;
        Contacts[k] = c;
        SupplyGiven++;
        return (true, give);
    }
}
