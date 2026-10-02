using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 ② 미리 막기 — 고장 예지 정비 · 장기 물자.
//  예지 정비: 감지기가 닿는 설비마다 두 시간에 한 번 진동 · 온도 · 소리를 읽는다 (잡음 · 값 멈춤 · 폭풍이면 흐림).
//    추세를 그어 남은 수명 범위("2~4일")를 내고 → 날마다 새벽에 주간 정비표를 짠다: 한가한 시간(10시 · 14시)에 같은 방 설비를 몰아서.
//    칸마다 "미루면 위험이 얼마나 커지나"(지금 정비 ↔ 이틀 미룸의 고장 확률)를 적는다. 성격이 날짜를 민다 — 과감하면 늦게(낙관), 신중하면 이르게.
//    그 시각이 오면 정비 일감이 미리 나오고(WorkOrders 훅) 솜씨 있는 사람에게 부탁한다. 정비 전에 고장 나면 놓친 것 — 사고 뒤 검토로 간다.
//  장기 물자: 여섯 시간마다 재고를 재 사흘치 소비 추세로 "며칠 뒤 바닥"을 낸다 — 5~16일 앞이면 원정 · 기항지 · 재활용 · 아껴 쓰기 ·
//    재배 늘리기 · 위험 구간 우회(시간 ↔ 피해)를 견줘 하나를 권한다 (원정은 함장 · 회의가 받으면 원정 계통에 청한다).

public sealed class WearTrend
{
    public int MachineId { get; init; }
    public float[] V { get; } = new float[12];
    public long[] T { get; } = new long[12];
    public int N;
    public float Now, Slope, Spread;
    public float LifeLo = 99f, LifeHi = 99f;
    public long Since = -1;
}

public sealed class UpkeepSlot
{
    public int Day { get; init; }
    public float Hour { get; init; }
    public long At { get; init; }
    public long Planned { get; init; }
    public int MachineId { get; init; }
    public string Machine { get; init; } = "";
    public string Room { get; init; } = "";
    public float LifeLo { get; init; }
    public float LifeHi { get; init; }
    public float RiskNow { get; init; }
    public float RiskLate { get; init; }
    public string Why { get; init; } = "";
    public bool Done, Missed, Asked;
}

public sealed class SupplyPlan
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public MatCat Cat { get; init; }
    public float Stock, Rate, DaysLeft;
    public List<(string opt, float score, string why)> Options { get; } = new();
    public string Choice { get; set; } = "";
    public bool Open { get; set; } = true;
    public string Result { get; set; } = "";
}

public sealed partial class ShipMate
{
    public SortedDictionary<int, WearTrend> Trends { get; } = new();
    public List<UpkeepSlot> Slots { get; } = new();
    public int SlotsDone, SlotsMissed, ServicedEarly;
    private readonly HashSet<int> _due = new();
    private long _sampleNext;
    private int _planDay = -1;
    /// <summary>시험용: 다음 정비표를 지금 짠다.</summary>
    public bool ForcePlan;

    /// <summary>WorkOrders 훅: 정비표가 정한 시각이 온 설비 — 마모 문턱을 앞당긴다.</summary>
    public float Early(Machine m) => _due.Count > 0 && _due.Contains(m.Body.Id) ? 0.3f : 0f;

    public static string TrendWord(Machine m) => m.Body.Type switch
    {
        FurnitureType.CoolantPump or FurnitureType.OxygenGenerator or FurnitureType.WaterRecycler or FurnitureType.AuxGenerator or FurnitureType.EngineCore => "진동",
        FurnitureType.PowerPanel or FurnitureType.Battery or FurnitureType.ReactorCore or FurnitureType.HeatExchanger or FurnitureType.MainComputer => "온도",
        _ => "소리",
    };

    /// <summary>설비 속 실제 상태 (세계 — 컴퓨터는 읽은 값만 안다).</summary>
    public float TrueIndex(Machine m)
    {
        float omen = m.Omen is Omen o && o.Kind is OmenKind.Vibration or OmenKind.Heat ? 0.25f * o.Level(_w.Tick) : 0f;
        return 0.12f + 0.8f * MathF.Pow(Math.Clamp(m.Wear, 0f, 1f), 1.2f) + 0.12f * (1f - m.Condition) + omen + 0.04f * MathF.Max(0f, _w.Parts.AgeFactor(m) - 1f);
    }

    private void UpkeepTick()
    {
        var w = _w;
        if (w.Tick >= _sampleNext) { _sampleNext = w.Tick + SimTime.Hours(2); Sample(); }
        int day = SimTime.Day(w.Tick);
        float h = Hour(w.Tick);
        if (ForcePlan || day != _planDay && h >= 4f) { ForcePlan = false; _planDay = day; PlanWeek(); }
        RunSlots();
    }

    private void Sample()
    {
        var w = _w;
        var a = A;
        float noise = (0.02f + 0.06f * (1f - a.Core.Accuracy)) * (w.Hazards.StormActive ? 2f : 1f);
        foreach (var m in w.Ship.Machines)
        {
            var room = m.Body.Room;
            if (room.Detached || !a.Belief.Reading(room)) continue;
            if (!Trends.TryGetValue(m.Body.Id, out var t)) Trends[m.Body.Id] = t = new WearTrend { MachineId = m.Body.Id };
            if (t.N > 0 && m.LastServiced > t.T[(t.N - 1) % 12]) { t.N = 0; t.Since = w.Tick; } // 정비했다 — 새로 잰다
            float v = a.Belief.Of(room).Fault == SensorFault.Stuck && t.N > 0 ? t.V[(t.N - 1) % 12] : TrueIndex(m) + R.Range(-noise, noise); // 값 멈춤: 컴퓨터는 모른다
            t.V[t.N % 12] = v; t.T[t.N % 12] = w.Tick; t.N++;
            Fit(t);
        }
    }

    private void Fit(WearTrend t)
    {
        int n = Math.Min(t.N, 12);
        if (n == 0) return;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        long t0 = t.T[(t.N - n) % 12];
        for (int k = 0; k < n; k++)
        {
            int i = (t.N - n + k) % 12;
            double x = (t.T[i] - t0) / (double)SimTime.TicksPerHour, y = t.V[i];
            sx += x; sy += y; sxx += x * x; sxy += x * y;
        }
        double den = n * sxx - sx * sx;
        double slope = n >= 3 && den > 1e-6 ? (n * sxy - sx * sy) / den : 0;
        double icpt = (sy - slope * sx) / n;
        double res = 0, xl = (t.T[(t.N - 1) % 12] - t0) / (double)SimTime.TicksPerHour;
        for (int k = 0; k < n; k++) { int i = (t.N - n + k) % 12; double x = (t.T[i] - t0) / (double)SimTime.TicksPerHour; res += Math.Pow(t.V[i] - (icpt + slope * x), 2); }
        t.Slope = (float)slope;
        t.Now = (float)(icpt + slope * xl);
        t.Spread = n >= 3 ? (float)Math.Sqrt(res / (n - 2 > 0 ? n - 2 : 1)) : 0.1f;
        if (n < 4 || t.Slope < 0.0004f) { t.LifeLo = t.LifeHi = t.Now > 0.8f ? 1f : 99f; return; }
        float hours = MathF.Max(0f, (0.85f - t.Now) / t.Slope);
        float u = t.Spread / t.Slope + 0.2f * hours;
        t.LifeLo = MathF.Max(0f, hours - u) / 24f;
        t.LifeHi = (hours + u) / 24f;
    }

    /// <summary>앞으로 hours 동안 고장 날 확률 (마모가 추세대로 늘면).</summary>
    private float RiskBy(Machine m, WearTrend t, float hours)
    {
        float baseRate = MathF.Max(1e-6f, m.FaultChancePerHour);
        float b = 0.05f + m.Wear * m.Wear * m.Wear * 8f;
        float dw = MathF.Max(0f, t.Slope) / 0.9f;
        double sum = 0;
        for (float x = 0; x < hours; x += 2f)
        {
            float wv = Math.Clamp(m.Wear + dw * x, 0f, 1f);
            sum += baseRate * ((0.05f + wv * wv * wv * 8f) / b) * 2f;
        }
        return (float)(1 - Math.Exp(-sum));
    }

    /// <summary>주간 정비표 (날마다 새벽에 다시).</summary>
    private void PlanWeek()
    {
        var w = _w;
        var a = A;
        int day = SimTime.Day(w.Tick);
        long day0 = (long)(day - 1) * SimTime.TicksPerDay;
        Slots.RemoveAll(s => !s.Done && !s.Missed && !s.Asked);
        if (Slots.Count > 40) Slots.RemoveRange(0, Slots.Count - 40);
        float caution = a.Character.Caution;
        var cands = new List<(Machine m, WearTrend t, int due)>();
        foreach (var t in Trends.Values)
        {
            if (t.N < 4 || !(t.LifeLo < 7f || t.Now > 0.62f)) continue;
            if (w.Ship.Machines.FirstOrDefault(x => x.Body.Id == t.MachineId) is not Machine m || m.Faults.Count > 0 || m.Body.Room.Detached) continue;
            if (Slots.Any(s => s.MachineId == m.Body.Id && !s.Done && !s.Missed)) continue;
            if (BelievedFresh(m)) continue; // 기억으로는 막 정비했다 (틀어졌을 수도)
            float lean = caution > 0.25f ? 0.55f : caution < -0.25f ? 0.95f : 0.75f; // 성격: 과감 = 늦게 (낙관) · 신중 = 이르게
            float when = caution < -0.25f ? t.LifeLo * 0.6f + t.LifeHi * 0.4f : t.LifeLo * lean;
            cands.Add((m, t, Math.Clamp((int)MathF.Floor(when), 0, 6)));
        }
        var taken = new Dictionary<(int d, int h), int>();
        foreach (var grp in cands.GroupBy(x => x.m.Body.Room.Id).OrderBy(g => g.Min(x => x.due)).ThenBy(g => g.Key))
        {
            int d = grp.Min(x => x.due);
            if (d == 0 && Hour(w.Tick) > 13f) d = 1;
            int hh = 10;
            while (taken.GetValueOrDefault((d, hh)) + grp.Count() > 3 && hh < 16) hh = hh == 10 ? 14 : 16;
            foreach (var (m, t, _) in grp.OrderBy(x => x.m.Body.Id))
            {
                taken[(d, hh)] = taken.GetValueOrDefault((d, hh)) + 1;
                long at = day0 + (long)d * SimTime.TicksPerDay + SimTime.Hours(hh);
                float hrs = MathF.Max(1f, (at - w.Tick) / (float)SimTime.TicksPerHour);
                float now = RiskBy(m, t, hrs), late = RiskBy(m, t, hrs + 48f);
                Slots.Add(new UpkeepSlot
                {
                    Day = day + d, Hour = hh, At = at, Planned = w.Tick, MachineId = m.Body.Id, Machine = m.Name, Room = m.Body.Room.Name, LifeLo = t.LifeLo, LifeHi = t.LifeHi,
                    RiskNow = now, RiskLate = late, Why = $"{TrendWord(m)} 추세 {t.Now * 100:0} (하루 +{t.Slope * 2400:0.#})",
                });
            }
        }
        int n = Slots.Count(s => !s.Done && !s.Missed);
        if (n > 0) a.Book.Today.Schedules += n;
    }

    private void RunSlots()
    {
        var w = _w;
        var a = A;
        bool calm = Crisis.Level(w) < CrisisLevel.Emergency && w.Fire.Count == 0;
        foreach (var s in Slots)
        {
            if (s.Done || s.Missed) continue;
            var m = w.Ship.Machines.FirstOrDefault(x => x.Body.Id == s.MachineId);
            if (m == null) { s.Missed = true; continue; }
            if (m.LastServiced >= s.At - SimTime.TicksPerDay && (s.Asked || m.LastServiced >= s.At - SimTime.Hours(20)))
            {
                s.Done = true; SlotsDone++; _due.Remove(m.Body.Id);
                if (m.LastServiced < s.At) ServicedEarly++;
                if (m.Omen is Omen om && om.Kind == OmenKind.Vibration)
                {
                    m.Omen = null;
                    w.Log.Add(w.Tick, LogKind.Work, $"{m.Name} 정비 중에 떨림의 원인을 찾아 손봤다 (정비표대로)");
                }
                if (Trends.TryGetValue(m.Body.Id, out var tr)) { tr.N = 0; tr.Since = w.Tick; }
                Remember(m);
                continue;
            }
            if (m.Faults.Count > 0 && w.Tick >= s.At - SimTime.TicksPerDay * 3)
            {
                s.Missed = true; SlotsMissed++; _due.Remove(m.Body.Id);
                OnMissed(s, m);
                continue;
            }
            if (w.Tick < s.At) continue;
            _due.Add(m.Body.Id);
            if (!calm || s.Asked) continue;
            var o = w.Board.Open.FirstOrDefault(x => x.Target.Furniture == m.Body && x.Kind == WorkKind.Maintain);
            if (o == null || o.Assignee != null) continue;
            if (a.CrewModel.Best(o.Skill) is CrewMember who)
            {
                s.Asked = true;
                a.CrewModel.Ask(who, o, $"정비표 — {s.Why} · 남은 수명 {s.LifeLo:0.#}~{s.LifeHi:0.#}일 · 미루면 고장 확률 {s.RiskNow * 100:0}% → {s.RiskLate * 100:0}%", 6f);
            }
            if (w.Tick - s.At > SimTime.TicksPerDay * 2 && !s.Asked) { s.Missed = true; SlotsMissed++; _due.Remove(m.Body.Id); }
        }
    }

    // ───────────── 장기 물자 ─────────────

    private static readonly (string key, string name, ItemKind? item, MatCat cat)[] SupplyKeys =
    {
        ("plate", "금속판", ItemKind.Plate, MatCat.Repair), ("cable", "케이블", ItemKind.Cable, MatCat.Repair), ("sealant", "실링폼", ItemKind.Sealant, MatCat.Repair),
        ("electronics", "전자재", ItemKind.Electronics, MatCat.Repair), ("structure", "구조재", ItemKind.Structure, MatCat.Structure),
        ("filter", "필터", ItemKind.Filter, MatCat.Parts), ("food", "식량", null, MatCat.Food),
    };
    private readonly SortedDictionary<string, (float[] v, long[] t, int n)> _stock = new(StringComparer.Ordinal);
    public List<SupplyPlan> Supplies { get; } = new();
    private long _supplyNext;
    private int _supplyId = 1;

    private float StockOf((string key, string name, ItemKind? item, MatCat cat) k) => k.item is ItemKind ik ? _w.Ship.CountStored(ik) : _w.Expedition.Stock(k.cat);

    private void SupplyTick()
    {
        var w = _w;
        if (w.Tick < _supplyNext) return;
        _supplyNext = w.Tick + SimTime.Hours(6);
        foreach (var k in SupplyKeys)
        {
            if (!_stock.TryGetValue(k.key, out var s)) s = (new float[12], new long[12], 0);
            s.v[s.n % 12] = StockOf(k); s.t[s.n % 12] = w.Tick; s.n++;
            _stock[k.key] = s;
            JudgeSupply(k, s);
        }
    }

    /// <summary>사흘치 추세로 며칠 뒤 바닥나나.</summary>
    public float DaysLeft(string key)
    {
        if (!_stock.TryGetValue(key, out var s)) return 99f;
        int n = Math.Min(s.n, 12);
        if (n < 3) return 99f;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        long t0 = s.t[(s.n - n) % 12];
        for (int k = 0; k < n; k++) { int i = (s.n - n + k) % 12; double x = (s.t[i] - t0) / (double)SimTime.TicksPerDay; sx += x; sy += s.v[i]; sxx += x * x; sxy += x * s.v[i]; }
        double den = n * sxx - sx * sx;
        double slope = den > 1e-9 ? (n * sxy - sx * sy) / den : 0;
        float cur = s.v[(s.n - 1) % 12];
        return slope >= -0.01 ? 99f : MathF.Max(0f, cur / (float)-slope);
    }

    private float DaysToPort()
    {
        var v = _w.Voyage;
        float d = MathF.Max(0f, v.Current.Days - v.Progress);
        if (v.Current.Kind == LegKind.Port) return 0f;
        for (int i = v.Index + 1; i < v.Legs.Count; i++) { if (v.Legs[i].Kind == LegKind.Port) return d; d += v.Legs[i].Days; }
        return 99f;
    }

    private void JudgeSupply((string key, string name, ItemKind? item, MatCat cat) k, (float[] v, long[] t, int n) s)
    {
        var w = _w;
        var a = A;
        float left = DaysLeft(k.key);
        var open = Supplies.LastOrDefault(p => p.Key == k.key && p.Open);
        if (open != null)
        {
            open.DaysLeft = left;
            if (left > 20f) { open.Open = false; open.Result = $"{open.Result} → 추세가 풀렸다".TrimStart(' ', '→'); }
            return;
        }
        if (left < 5f || left > 16f || Math.Min(s.n, 12) < 4) return;
        if (Supplies.Any(p => p.Key == k.key && w.Tick - p.Tick < SimTime.TicksPerDay * 2)) return;
        var plan = new SupplyPlan { Id = _supplyId++, Tick = w.Tick, Key = k.key, Name = k.name, Cat = k.cat, Stock = s.v[(s.n - 1) % 12], DaysLeft = left };
        plan.Rate = plan.Stock / MathF.Max(0.5f, left);
        var ex = w.Expedition;
        bool canGo = ex.Current == null && ex.Pending == null && w.Policies["expedition"] != 2 && w.Voyage.Current.Kind != LegKind.Port && (ex.Sites.Any(x => !x.Taken) || ex.HasShuttle);
        float port = DaysToPort();
        if (canGo) plan.Options.Add(("원정", 0.55f + (left < 10f ? 0.15f : 0f) - 0.4f * (ex.ComputerPick?.Risk ?? 0.3f), $"가까운 곳에서 {k.name}을 구해 온다"));
        if (port < left - 1f) plan.Options.Add(("기항지에서 사기", 0.8f - 0.4f * port / MathF.Max(1f, left), $"{port:0}일 뒤 기항지 — 그 전엔 버틴다"));
        if (k.cat is MatCat.Repair or MatCat.Structure && w.Scrap.Smelter != null) plan.Options.Add(("재활용", 0.42f, "고철을 녹여 되살린다"));
        if (k.cat == MatCat.Food && w.Ship.LiveRooms.Any(r => r.Type == RoomType.Hydroponics)) plan.Options.Add(("재배 늘리기", 0.5f, "재배대를 하나 더 돌린다"));
        if (k.key is "plate" or "sealant" && (w.Voyage.Index + 1 < w.Voyage.Legs.Count) && w.Voyage.Legs[w.Voyage.Index + 1].Kind is LegKind.AsteroidBelt or LegKind.RadiationBelt)
            plan.Options.Add(("위험 구간 우회", 0.45f - 0.05f * (left < 8f ? 2f : 0f), "다음 구간을 돌아가면 하루쯤 늦지만 운석에 덜 맞아 덜 쓴다"));
        plan.Options.Add(("아껴 쓰기", 0.3f + (left > 12f ? 0.15f : 0f), "급하지 않은 공사에 쓰지 않는다"));
        var best = plan.Options.OrderByDescending(o => o.score).ThenBy(o => o.opt, StringComparer.Ordinal).First();
        plan.Choice = best.opt;
        Supplies.Add(plan);
        if (Supplies.Count > 30) Supplies.RemoveAt(0);
        string basis = $"사흘 추세로 하루 {plan.Rate:0.#}개씩 준다 — {left:0}일 뒤 바닥 · 견준 것: {string.Join(" / ", plan.Options.Select(o => $"{o.opt} {o.score:0.00}"))}";
        Say($"{left:0}일 뒤 {Ko.IGa(k.name)} 바닥난다 — {best.opt}를 권한다 ({best.why})");
        switch (best.opt)
        {
            case "원정":
            {
                var cat = k.cat;
                var pp = plan;
                if (a.Authority.Level(Domain.Resources) == AuthLevel.Auto) { pp.Result = ex.ComputerRequest(cat, true) ? "원정을 청했다" : "지금은 보낼 수 없다"; }
                else a.Asks.Propose("supply:" + k.key, "plan", null, $"{k.name} 원정", basis, $"{left:0}일 뒤 바닥나기 전에", 90f, null,
                    (world, pr) => pp.Result = world.Expedition.ComputerRequest(cat, true) ? "원정을 청했다 (받았다)" : "받았지만 보낼 수 없다");
                break;
            }
            case "위험 구간 우회":
            {
                var pp = plan;
                a.Asks.Propose("supply:" + k.key, "plan", null, "항로: 위험 구간 우회", basis, "하루쯤 늦지만 금속판을 덜 쓴다", 90f, null,
                    (world, pr) => { world.Policies.Set("route", 1, $"주 컴퓨터 물자 계획 — {k.name} {left:0}일"); pp.Result = "항로를 안전 쪽으로"; });
                break;
            }
            case "기항지에서 사기": plan.Result = "기항지 장보기 목록에 올렸다"; break;
            default: plan.Result = best.why; break;
        }
    }
}
