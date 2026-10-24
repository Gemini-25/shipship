namespace ShipEngine;

/// <summary>
/// ⑨ → ⑧ 문화 · 관습이 마음에 닿는 곳 (깊이 바퀴 3). 사회(⑨)가 정하고 마음은 지킨다:
/// <para><b>함께 먹는 때</b>: 관습이 된 시각(10분 앞부터 그 한 시간 — 걸어가면 제때 닿는다)에는 조금 배고파도 (0.3 넘게) 먹으러 간다 (+0.8).</para>
/// <para><b>방 금기</b>: 아무도 그 방에서 쉬지 않는다 — 꺼리지 않는 이도 그 방을 꺼림 0.82로 친다 (쉬기 · 어울리기 · 먹기 0.3배 · 지나가거나 일하러는 간다).</para>
/// <para><b>침대 금기</b>: 숨진 이의 침대에는 그때까지 아무도 눕지 않는다 (제 침대여도 — 다른 빈 침대가 없으면 눕는다).</para>
/// <para><b>조리대 규칙</b>: 배의 규칙이 되면 켜 둔 채 잊는 몫이 반.</para>
/// </summary>
public sealed partial class Mind
{
    public const float TabooShun = 0.82f;
    /// <summary>관습이 된 함께 먹는 시각 (시 · −1 없음).</summary>
    public int MealHour = -1;
    /// <summary>배의 규칙 '조리대를 떠날 땐 끄고 간다'.</summary>
    public bool StoveRule;
    /// <summary>관습이 된 샤워 시간 (분 · 0 없음 — 5단 배의 규칙 셋째) · 그때.</summary>
    public int ShowerCustomMin;
    public long ShowerCustomSince;
    private byte[] _taboo = Array.Empty<byte>();
    private readonly List<(int cell, long until)> _bedTaboo = new();
    /// <summary>관습의 시각에 먹으러 간 번 · 숨진 이의 침대를 비켜 간 번.</summary>
    public long CustomEats, BedsShunned;

    /// <summary>⑨가 요청: 함께 먹는 시각 (−1이면 관습이 사라졌다).</summary>
    public void SetMealCustom(int hour) => MealHour = hour;
    /// <summary>⑨가 요청: 그 방은 금기다 · 풀렸다.</summary>
    public void SetTaboo(int room, bool on)
    {
        if (room < 0 || room >= L.Rooms.Count) return;
        if (_taboo.Length < L.Rooms.Count) Array.Resize(ref _taboo, L.Rooms.Count);
        _taboo[room] = (byte)(on ? 1 : 0);
    }
    public bool Taboo(int room) => room >= 0 && room < _taboo.Length && _taboo[room] != 0;
    /// <summary>⑨가 요청: 그 침대에는 그때까지 아무도 눕지 않는다.</summary>
    public void TabooBed(int cell, long until)
    {
        _bedTaboo.RemoveAll(b => b.cell == cell || b.until <= _nowTick);
        if (cell >= 0) _bedTaboo.Add((cell, until));
    }
    public bool BedTaboo(int cell)
    {
        foreach (var (c, until) in _bedTaboo) if (c == cell && until > _nowTick) return true;
        return false;
    }

    /// <summary>⑨가 요청 (추모): 곁에서 본 죽음의 충격이 덜어진다.</summary>
    public void Calmed(int agent, float shock)
    {
        if (agent < 0 || agent >= Agents.Count || !Agents[agent].IsPerson) return;
        ref var c = ref Agents[agent].C;
        c.Shock = Math.Max(0, c.Shock - shock);
    }

    /// <summary>그 방을 피하는 정도 — 제 꺼림 · 배의 금기 가운데 큰 것.</summary>
    private float Shun(int agent, int room) => Math.Max(Dread(agent, room), Taboo(room) ? TabooShun : 0);

    /// <summary>관습의 식사 때인가 (10분 앞부터 그 한 시간).</summary>
    private bool MealTime(double hour)
    {
        if (MealHour < 0) return false;
        double d = ((hour - MealHour) % 24 + 24) % 24;
        return d < 1 || d >= 24 - 1 / 6.0;
    }

    /// <summary>시험용: 그 사람이 그 방을 꺼리는 정도를 정한다.</summary>
    public void SetDread(int agent, int room, float v)
    {
        int nr = L.Rooms.Count;
        if (_dread.Length < Agents.Count * nr) Array.Resize(ref _dread, Agents.Count * nr);
        _dread[agent * nr + room] = v;
    }

    private void SaveCustoms(StateWriter w)
    {
        w.I32(MealHour); w.Byte((byte)(StoveRule ? 1 : 0)); w.Arr(_taboo);
        w.I32(_bedTaboo.Count); foreach (var (c, u) in _bedTaboo) { w.I32(c); w.I64(u); }
        w.I64(CustomEats); w.I64(BedsShunned);
        w.I32(ShowerCustomMin); w.I64(ShowerCustomSince);
    }
    private void LoadCustoms(StateReader r)
    {
        MealHour = r.I32(); StoveRule = r.Byte() != 0; _taboo = r.Arr<byte>();
        _bedTaboo.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { int c = r.I32(); long u = r.I64(); _bedTaboo.Add((c, u)); }
        CustomEats = r.I64(); BedsShunned = r.I64();
        ShowerCustomMin = r.I32(); ShowerCustomSince = r.I64();
    }
}
