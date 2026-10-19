namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 별계 지도 (다) 다음 들를 곳 (docs/ENGINE.md ⑤ '별계 지도'): 함장이 정한 임무(갈 곳 차례)를 따라가되 닿을 때마다 · 떠나기 전 다음 갈 곳을 고른다 (5분마다 — 아는 것으로만: 지도 · 남은 Δv · 재고 기록).
/// <para>남은 Δv가 다음 임무 구간 × 1.05보다 모자라면 → 닿을 수 있는 정거장 (Δv가 남은 몫의 95% 안 · 발사 창이 400일 안) 가운데 가장 빨리 닿는 곳으로 (추진제를 채우러) ·
/// 수리 부품이 막혔고 (원료 · 전자 소자) 정거장 · 광산 창이 임무 창보다 먼저 열리면 → 들른다 · 아니면 임무 구간으로. 떠나는 점화는 발사 창에.
/// 갈 곳이 없으면 머물며 기다린다 (막히면 다른 길 — 까닭은 일지에 한 번).</para>
/// </summary>
public sealed partial class ShipMind
{
    public const double DvMargin = 1.05, MaxWindowDays = 400;
    public readonly List<int> Mission = new();       // 갈 곳 차례 (별계 지도 번호)
    public string NextLine = "";                     // 아침 방송 · 화면: 다음 들를 곳
    public long LegsPlanned, FuelDetours, PartsDetours, MissionsDone;
    private string _voyNote = "";
    private long _missSeen;
    private int _legCause;

    /// <summary>함장이 정한 임무 (시작 준비 — 처음 구간은 이미 발사 창에 맞춰 계획됐다).</summary>
    public void SetMission(IEnumerable<int> places) { Mission.Clear(); Mission.AddRange(places); }

    private void Voyage(Sim sim)
    {
        var o = _out;
        if (o == null || o.Places.Count == 0 || sim.Tick % Time.Minutes(5) != 3) return;
        long t = sim.Tick;
        if (o.AtPlace >= 0 && Mission.Count > 0 && Mission[0] == o.AtPlace)
        {
            string name = o.Places[o.AtPlace].Name;
            Mission.RemoveAt(0);
            int c = sim.Causes.Add(CauseKind.Computer, Mission.Count == 0 ? $"임무를 마쳤다 — {Ko.I(name)} 마지막 갈 곳이었다" : $"임무: {name}에 닿았다 — 다음은 {o.Places[Mission[0]].Name}", t);
            sim.Facts.Emit(t, FactKind.Decision, -1, 1, c, -3);
            Say(sim, Mission.Count == 0 ? $"임무를 마쳤습니다 — {name}" : $"{name}에 닿았습니다 — 다음 임무는 {o.Places[Mission[0]].Name}");
            if (Mission.Count == 0) { MissionsDone++; NextLine = ""; }
        }
        if (o.PlacesMissed > _missSeen)
        {
            // 닿았는데 그곳이 없다 (발사 창을 놓쳤다) — 알리고 · 이 궤도에서 다시 고른다
            _missSeen = o.PlacesMissed;
            Say(sim, $"갈 곳과 {o.ArrivalMissDeg:0}° 어긋났습니다 — 이 궤도에서 다시 길을 찾습니다");
        }
        if (Mission.Count == 0 || o.VoyageTicks > 0 || o.Planet != null || o.Burning != BurnKind.None) return;
        if (Supplying()) return;   // 붙어 오는 정거장 · 광산을 기다린다 (받고 떠난다)
        int target = Mission[0];
        var (choice, why, kind) = ChooseLeg(target, t);
        bool pending = !double.IsNaN(o.PendingTo);
        if (pending && o.PendingPlace == choice) return;
        if (choice < 0)
        {
            // 막히면 다른 길: 닿지 못할 길은 떠나지 않는다 (계획한 출발을 거둔다) — 머물며 기다린다 (까닭은 한 번)
            if (pending) o.CancelVoyage();
            if (_voyNote != why) { _voyNote = why; NextLine = why + " — 머물며 기다린다"; Say(sim, NextLine); int c = sim.Causes.Add(CauseKind.Computer, NextLine, t); sim.Facts.Emit(t, FactKind.Decision, -1, -1, c, -3); }
            return;
        }
        long wait = o.WindowWait(choice, t);
        var p = o.Places[choice];
        double days = Outside.VoyageDays(o.Distance, p.Au), dv = o.LegDv(o.Distance, choice);
        _legCause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 다음 들를 곳을 {Ko.Euro(p.Name)} 정했다 — {why} · 발사 창까지 {Span(wait)} · 가는 데 {days:0}일 · Δv {dv / 1000:0.0}km/s", t);
        sim.Facts.Emit(t, FactKind.Decision, -1, 1, _legCause, -3);
        o.PlanVoyageTo(sim, choice, wait / (double)Time.Minutes(1), _legCause);
        LegsPlanned++;
        if (kind == 1) FuelDetours++; else if (kind == 2) PartsDetours++;
        NextLine = $"다음 들를 곳은 {p.Name} — {why} · 발사 창까지 {Span(wait)}";
        Say(sim, NextLine);
        _voyNote = "";
    }

    private static string Span(long ticks) => ticks < Time.TicksPerDay ? $"{ticks / (double)Time.TicksPerHour:0}시간" : $"{ticks / (double)Time.TicksPerDay:0}일";

    /// <summary>붙으러 오는 정거장 · 광산이 있다 (부탁을 들어주기로 했다).</summary>
    private bool Supplying()
    {
        foreach (var c in _out!.Contacts) if (c.Kind is ContactKind.Station or ContactKind.Mine && c.Meet != 0 && c.State != ContactState.Gone) return true;
        return false;
    }

    /// <summary>다음 갈 곳 (아는 것으로만): 추진제가 모자라면 정거장 · 부품이 막혔으면 먼저 열리는 정거장 · 광산 · 아니면 임무. 까닭 · 갈래 (0 임무 · 1 추진제 · 2 부품).</summary>
    private (int place, string why, int kind) ChooseLeg(int target, long now)
    {
        var o = _out!;
        double left = o.DvLeft, bare = o.LegDv(o.Distance, target), need = DvMargin * bare;
        string tName = o.Places[target].Name;
        long maxWait = (long)(MaxWindowDays * Time.TicksPerDay);
        // 들를 수 있는 곳 가운데 가장 빨리 닿는 곳: 정거장은 추진제를 채워 주니 가는 몫만 · 광산은 거기서 임무 곳까지도
        int Best(Func<Place, bool> ok, long before)
        {
            int best = -1; double bestT = double.MaxValue;
            for (int i = 0; i < o.Places.Count; i++)
            {
                var p = o.Places[i];
                if (i == o.AtPlace || !ok(p) || Math.Abs(p.Au - o.Distance) < 1e-6) continue;
                double go = o.LegDv(o.Distance, i);
                if (p.Kind == 1 ? go > 0.95 * left : go + DvMargin * o.LegDv(p.Au, target) > left) continue;
                long w = o.WindowWait(i, now);
                if (w > Math.Min(maxWait, before)) continue;
                double total = w + Outside.VoyageDays(o.Distance, p.Au) * Time.TicksPerDay;
                if (total < bestT) { bestT = total; best = i; }
            }
            return best;
        }
        if (left < need)
        {
            string short_ = $"추진제가 모자라다 (남은 Δv {left / 1000:0.0}km/s < {tName}까지 {need / 1000:0.0}km/s)";
            int s = Best(p => p.Kind == 1, long.MaxValue);
            if (s >= 0) return (s, short_ + " — 채우러 간다", 1);
            // 막히면 다른 길: 여유 없이라도 닿을 수 있으면 간다 · 아니면 머문다
            return left >= bare ? (target, $"추진제 여유 없이 {Ko.Euro(tName)} 간다 (남은 Δv {left / 1000:0.0}km/s · 드는 {bare / 1000:0.0}km/s — 닿을 정거장 창이 없다)", 0)
                                : (-1, short_ + " · 닿을 정거장도 없다", 0);
        }
        var want = SupplyWants();
        if (want[0] >= 3 || want[1] >= 2)
        {
            int s = Best(p => p.Kind is 1 or 2, o.WindowWait(target, now));
            if (s >= 0) return (s, "수리 부품이 막혔다 (원료 · 전자 소자) — 임무 창보다 먼저 열린다", 2);
        }
        return (target, $"임무 — {tName}", 0);
    }

    private void SaveVoyage(StateWriter w)
    {
        w.I32(Mission.Count); foreach (int p in Mission) w.I32(p);
        w.Str(NextLine); w.I64(LegsPlanned); w.I64(FuelDetours); w.I64(PartsDetours); w.I64(MissionsDone); w.Str(_voyNote); w.I32(_legCause); w.I64(_missSeen);
    }
    private void LoadVoyage(StateReader r)
    {
        Mission.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) Mission.Add(r.I32());
        NextLine = r.Str() ?? ""; LegsPlanned = r.I64(); FuelDetours = r.I64(); PartsDetours = r.I64(); MissionsDone = r.I64(); _voyNote = r.Str() ?? ""; _legCause = r.I32(); _missSeen = r.I64();
    }
}
