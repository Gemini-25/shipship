using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.16 주컴퓨터 두뇌 2.0 — 앞날 예측 (몇 시간 ~ 며칠 뒤).
//  자원 여섯(물 · 식량 · 산소 · 전력 · 재료 · 추진제)을 두 시간마다 잰다. 컴퓨터는 계기로 잰다 — 계기는 틀린다:
//    데이터선이 끊긴 방의 계기는 낡은 값 · 값이 멈춘 계기(방사선 손상)는 그대로 · 교정이 틀어진 설비 옆 계기는 높게 읽는다.
//  흐름(생산 − 소비)으로 셈한 값과 계기 값을 견준다: 흐름은 움직이는데 계기가 멈춰 있으면 의심하고,
//    사람이 그 방에서 눈금을 직접 보면(승무원 ↔ 컴퓨터) 계기와 견줘 맞으면 더 믿고 틀리면 덜 믿는다 (계기마다 믿음).
//    덜 믿는 계기는 버리고 마지막으로 믿은 값에서 흐름으로 셈해 간다(추측 항법) · 교정 작업을 부른다 (Watch.ScanCalibration 훅).
//  예측: 지금 값 · 하루 증감 · 부족 문턱까지 며칠 · 하루 뒤 · 사흘 뒤 값 · 확신도(과거 적중률 × 계기 믿음 × 멀수록 흐림).
//    "사흘 뒤 물 부족 (확신 70%)" — 열두 시간 뒤 다시 재서 채점하고, 맞힌 비율이 다음 확신이 된다.
//  고장 위험(설비마다 하루 안 고장 확률 · 전조) · 바깥 위험(폭풍 · 우주 예보)도 함께 본다 → 계획자(ComputerPlanner)가 쓴다.

/// <summary>컴퓨터가 지켜보는 자원 하나: 실제 값 · 부족 문턱 · 상한 · 아는 흐름(단위/시간) · 계기가 달린 방 · 채점 허용 오차.</summary>
public sealed record ResourceModel(string Key, string Name, string Unit,
    Func<World, float> True, Func<World, float> Short, Func<World, float> Cap, Func<World, float> Flow,
    Func<World, Room?> Gauge, float Tol, Func<World, float>? Believed = null, bool UseTrend = false);

/// <summary>계기 하나를 얼마나 믿나 (사람 눈금 · 흐름과 견준 결과).</summary>
public sealed class GaugeLedger
{
    public string Key { get; init; } = "";
    public int RoomId { get; set; } = -1;
    public int Agree { get; set; }
    public int Disagree { get; set; }
    public long LastDisagree { get; set; } = -1;
    public long LastAgree { get; set; } = -1;
    public string LastWhy { get; set; } = "";
    public int ManualReads { get; set; }
    public bool Announced { get; set; }
    /// <summary>믿음 (처음 75% · 어긋날수록 준다).</summary>
    public float Reliability => (Agree + 3f) / (Agree + Disagree + 4f);
    public bool Suspect => Reliability < 0.5f;
}

/// <summary>자원 하나의 예측 (컴퓨터가 믿는 값 기준).</summary>
public sealed class ResourceForecast
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public long Tick { get; set; } = -1;
    public float Reading { get; set; }
    public float Estimate { get; set; }
    public float Flow { get; set; }
    public float Trend { get; set; }
    public float Rate { get; set; }
    public float Short { get; set; }
    public float DaysToShort { get; set; } = 99f;
    public float At24 { get; set; }
    public float At72 { get; set; }
    public float Confidence { get; set; }
    public bool Stale { get; set; }
    public bool DeadReckon { get; set; }
    public string Basis { get; set; } = "";
    /// <summary>"사흘 뒤 물 부족 (확신 70%)" 같은 한 줄.</summary>
    public string Line => DaysToShort >= 30f ? $"{Name} 넉넉 ({Estimate:0.#}{Unit} · 하루 {Rate * 24f:+0.#;-0.#}{Unit})"
        : DaysToShort <= 0.05f ? $"{Name} 이미 부족 ({Estimate:0.#}{Unit})"
        : $"{ShipForecast.When(DaysToShort)} {Name} 부족 (확신 {Confidence * 100:0}%)";
    internal readonly List<(long tick, float est)> History = new();
}

/// <summary>설비 고장 위험 (하루 안) — 계획자의 정비 일정.</summary>
public sealed record FailureRisk(int MachineId, string Machine, string Room, float P24, string Why);

/// <summary>부족 경고 하나 (기한에 채점).</summary>
public sealed class ShortWarning
{
    public string Key { get; init; } = "";
    public long Tick { get; init; }
    public float Days { get; init; }
    public float Confidence { get; init; }
    public string Text { get; init; } = "";
    public List<int> Heard { get; } = new();
    public int Score { get; set; }
}

public sealed class ShipForecast
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 4127 + 389));
    private long _next;
    private readonly Dictionary<string, ResourceForecast> _f = new();
    private readonly Dictionary<string, GaugeLedger> _ledger = new();
    private readonly Dictionary<string, (float hits, float total)> _skill = new();
    private readonly List<(string key, long due, float predicted)> _checks = new();
    public List<FailureRisk> Risks { get; } = new();
    public List<ShortWarning> Warnings { get; } = new();
    public string HazardLine { get; private set; } = "";
    public int Samples, ManualReads, Disagreements, Graded, Hits, Suspects;

    public ShipForecast(World w) => _w = w;

    public const float SampleHours = 2f;

    /// <summary>"n시간 뒤" · "내일" · "사흘 뒤" (사람 말로).</summary>
    public static string When(float days) => days < 0.75f ? $"{MathF.Max(1f, days * 24f):0}시간 뒤" : days < 1.5f ? "내일" : days < 2.5f ? "이틀 뒤" : days < 3.5f ? "사흘 뒤" : days < 4.5f ? "나흘 뒤" : $"{days:0}일 뒤";

    private static Room? RoomOf(World w, FurnitureType t) => w.Ship.FurnitureOf(t).Where(f => !f.Room.Detached).OrderBy(f => f.Id).Select(f => f.Room).FirstOrDefault();

    private static float AvgO2(World w, bool believed)
    {
        float s = 0f; int n = 0;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Type == RoomType.Corridor) continue;
            s += believed ? w.Automation.Belief.Of(r).O2 : r.Air.O2;
            n++;
        }
        return n == 0 ? 21f : s / n;
    }

    /// <summary>지켜보는 자원 여섯.</summary>
    public static readonly List<ResourceModel> Models = new()
    {
        new("water", "물", "L", w => w.Water.Level, w => w.Water.Capacity * 0.15f, w => w.Water.Capacity, w => w.Water.Produced - w.Water.Consumed,
            w => RoomOf(w, FurnitureType.WaterRecycler), 12f),
        new("food", "식량", "일치", w => FoodPolicy.FoodDays(w), w => 2f, w => float.MaxValue,
            w => (FoodPolicy.GrowingPerDay(w) / MathF.Max(1f, w.Crew.Count(c => !c.Dead) * FoodPolicy.MealsPerPersonDay) - 1f) / 24f,
            w => RoomOf(w, FurnitureType.Fridge), 0.4f),
        new("o2", "산소", "%", w => AvgO2(w, false), w => 18.5f, w => 23f, w => 0f, w => null, 0.6f, w => AvgO2(w, true)),
        new("power", "배터리", "kWh", w => w.Power.BatteryCharge, w => w.Power.BatteryCapacity * 0.15f, w => w.Power.BatteryCapacity, w => w.Power.BatteryFlow,
            w => RoomOf(w, FurnitureType.Battery), 6f, null, true), // 배터리 흐름은 순간마다 출렁인다 — 추세로
        new("materials", "수리재", "개", w => w.Expedition.Stock(MatCat.Repair), w => 2f, w => float.MaxValue, w => -w.Expedition.Rate[(int)MatCat.Repair] / 24f,
            w => w.Ship.RoomsOf(RoomType.Storage).FirstOrDefault(r => !r.Detached), 1.5f),
        new("propellant", "추진제", "%", w => w.Expedition.Stock(MatCat.Fuel), w => 15f, w => 100f, w => -w.Expedition.Rate[(int)MatCat.Fuel] / 24f,
            w => w.Ship.RoomsOf(RoomType.Engine).FirstOrDefault(r => !r.Detached), 4f),
    };

    public ResourceForecast? Get(string key) => _f.TryGetValue(key, out var f) && f.Tick >= 0 ? f : null;
    public GaugeLedger Ledger(string key) => _ledger.TryGetValue(key, out var l) ? l : _ledger[key] = new GaugeLedger { Key = key };
    public IEnumerable<ResourceForecast> All => Models.Select(m => Get(m.Key)).Where(f => f != null)!;

    /// <summary>갈래마다 맞힌 비율 (처음엔 반쯤 · 오래된 채점은 옅어진다).</summary>
    public float Skill(string key) => _skill.TryGetValue(key, out var s) ? (s.hits + 1f) / (s.total + 2f) : 0.5f;

    /// <summary>이 방 계기를 의심하나 (Watch.ScanCalibration 훅 — 교정 작업이 더 급해진다).</summary>
    public bool Suspect(Room r)
    {
        foreach (var l in _ledger.Values) if (l.RoomId == r.Id && l.Suspect) return true;
        return false;
    }

    /// <summary>계기 값 (컴퓨터가 읽는 값) · 살아 있나.</summary>
    private (float read, bool live) Read(ResourceModel m, Room? g, float truth, ResourceForecast f)
    {
        var w = _w;
        if (m.Believed != null) return (m.Believed(w), true);
        if (g == null) return (truth, true);
        var a = w.Automation;
        if (!a.Belief.Reading(g)) return (f.Tick >= 0 ? f.Reading : truth, false); // 데이터선이 끊겼다 — 낡은 값 (컴퓨터도 안다)
        if (a.Belief.Of(g).Fault == SensorFault.Stuck && f.Tick >= 0) return (f.Reading, true); // 값이 멈췄다 — 컴퓨터는 모른다
        float cal = 1f;
        foreach (var fu in g.Furniture) if (fu.Machine is Machine mm) cal = MathF.Min(cal, mm.SensorCal);
        float bias = MathF.Max(0f, 0.9f - cal) * 0.7f; // 교정이 틀어진 계기는 높게 읽는다 (넉넉하다고 믿는다)
        float cap = m.Cap(w);
        return (MathF.Min(truth * (1f + bias) + bias * m.Tol, cap < float.MaxValue / 2 ? cap * 1.25f : float.MaxValue), true);
    }

    /// <summary>두 시간마다 (계획자가 변화를 알아채면 바로).</summary>
    public void Update(bool force = false)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) return;
        if (!force && w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(SampleHours);
        Samples++;
        foreach (var m in Models) Sample(m);
        Grade();
        Risk();
    }

    private void Sample(ResourceModel m)
    {
        var w = _w;
        var a = w.Automation;
        if (!_f.TryGetValue(m.Key, out var f)) _f[m.Key] = f = new ResourceForecast { Key = m.Key, Name = m.Name, Unit = m.Unit };
        float truth = m.True(w);
        var g = m.Gauge(w);
        var L = Ledger(m.Key);
        L.RoomId = g?.Id ?? -1;
        // 교정한 뒤로는 다시 믿기 시작한다 (어긋난 기록을 반쯤 지운다)
        if (g != null && L.Disagree > 0 && g.Furniture.Any(fu => fu.Machine is Machine mm && mm.LastCalibrated > L.LastDisagree))
        {
            bool was = L.Suspect;
            L.Disagree /= 3;
            L.LastDisagree = w.Tick;
            if (was && !L.Suspect) a.Authority.Learned("계기", $"{g.Name} {m.Name} 계기 — 교정한 뒤 다시 믿는다");
        }
        var (read, live) = Read(m, g, truth, f);
        float dtH = f.Tick >= 0 ? (w.Tick - f.Tick) / (float)SimTime.TicksPerHour : 0f;
        float flow = m.Flow(w);
        float cap = m.Cap(w), sh = m.Short(w);
        // 흐름은 움직이는데 계기가 그대로다 (값이 멈춘 계기를 의심한다)
        if (live && g != null && f.Tick >= 0 && dtH > 0.5f && MathF.Abs(read - f.Reading) < 1e-3f && MathF.Abs(flow * dtH) > m.Tol * 1.2f
            && (flow < 0f ? f.Reading > 0.5f : f.Reading < cap - 0.5f))
            Disagree(m, L, g, $"흐름은 {flow * dtH:+0.#;-0.#}{m.Unit}인데 계기가 {read:0.#}{m.Unit}에서 멈췄다", null);
        // 사람이 그 방에서 눈금을 직접 본다 (깨어 있고 일하던 사람)
        bool told = false;
        if (g != null && m.Believed == null)
        {
            CrewMember? eye = null;
            foreach (var c in w.Crew)
                if (!c.Dead && c.Room == g && c.CanAct && c.IsAwake && !c.IsChild && !c.Outside && (eye == null || c.Id < eye.Id)) eye = c;
            if (eye != null && R.Chance(0.6f)) { ManualReading(m, eye, truth, read); told = true; }
        }
        // 컴퓨터가 믿는 값: 믿는 계기면 계기 · 의심하면 마지막 믿은 값에서 흐름으로 셈한다 · 사람이 말해 주면 그 값
        f.DeadReckon = L.Suspect && f.Tick >= 0;
        if (L.Suspect && g != null) a.Belief.Of(g).Trust = MathF.Min(a.Belief.Of(g).Trust, 0.45f); // 의심하는 동안은 그 방 감지기 전체를 덜 믿는다
        if (told) f.Estimate = truth;
        else if (f.DeadReckon) f.Estimate = Math.Clamp(f.Estimate + flow * dtH, 0f, cap);
        else f.Estimate = read;
        f.Reading = read;
        f.Stale = !live;
        f.Flow = flow;
        f.Short = sh;
        f.History.Add((w.Tick, f.Estimate));
        if (f.History.Count > 7) f.History.RemoveAt(0);
        var med = MedianSlope(f.History);
        f.Trend = med ?? flow;
        f.Rate = m.Believed != null || m.UseTrend ? med ?? 0f : flow; // 흐름(생산 − 소비)을 아는 자원은 흐름으로 · 모르는 자원(산소)은 추세로
        // 상한에 닿아 넘치는 몫은 버린다 (가득 찬 탱크)
        if (f.Rate > 0f && f.Estimate >= cap - 0.5f) f.Rate = 0f;
        f.DaysToShort = f.Estimate <= sh ? 0f : f.Rate < -1e-4f ? MathF.Min(99f, (f.Estimate - sh) / -f.Rate / 24f) : 99f;
        f.At24 = Math.Clamp(f.Estimate + f.Rate * 24f, 0f, cap);
        f.At72 = Math.Clamp(f.Estimate + f.Rate * 72f, 0f, cap);
        float sensor = g == null || m.Believed != null ? 1f : (live ? 1f : 0.6f) * (0.35f + 0.65f * L.Reliability) * (f.DeadReckon ? 0.85f : 1f);
        float horizon = f.DaysToShort >= 30f ? 1f : 1f / (1f + f.DaysToShort / 10f);
        f.Confidence = Math.Clamp((0.25f + 0.75f * Skill(m.Key)) * sensor * horizon * 1.15f, 0.05f, 0.95f);
        f.Basis = $"{(f.DeadReckon ? "흐름으로 셈한" : f.Stale ? "낡은" : "계기")} {f.Estimate:0.#}{m.Unit} · 흐름 {flow * 24f:+0.#;-0.#}{m.Unit}/일 · 추세 {f.Trend * 24f:+0.#;-0.#}{m.Unit}/일" +
                  (L.Disagree > 0 && g != null ? $" · 계기 믿음 {L.Reliability * 100:0}%" : "");
        f.Tick = w.Tick;
        // 열두 시간 뒤 다시 재서 채점한다
        _checks.Add((m.Key, w.Tick + SimTime.Hours(12), Math.Clamp(f.Estimate + f.Rate * 12f, 0f, cap)));
        if (_checks.Count > 60) _checks.RemoveAt(0);
        Warn(m, f, g);
    }

    /// <summary>추세: 이웃한 두 값 사이 기울기들의 가운데 값 (한 번 튄 값에 흔들리지 않는다) · 셋이 안 되면 null.</summary>
    private static float? MedianSlope(List<(long tick, float est)> h)
    {
        if (h.Count < 4) return null;
        var s = new List<float>(h.Count - 1);
        for (int i = 1; i < h.Count; i++)
        {
            float dt = (h[i].tick - h[i - 1].tick) / (float)SimTime.TicksPerHour;
            if (dt > 0.1f) s.Add((h[i].est - h[i - 1].est) / dt);
        }
        if (s.Count < 3) return null;
        s.Sort();
        return s[s.Count / 2];
    }

    /// <summary>사람이 눈금을 직접 봤다 — 계기와 견준다.</summary>
    public void ManualReading(ResourceModel m, CrewMember eye, float truth, float read)
    {
        var w = _w;
        var a = w.Automation;
        var L = Ledger(m.Key);
        L.ManualReads++;
        ManualReads++;
        var g = m.Gauge(w);
        bool off = MathF.Abs(read - truth) > MathF.Max(m.Tol, 0.08f * MathF.Abs(truth));
        if (!off)
        {
            L.Agree = Math.Min(L.Agree + 1, 12);
            L.LastAgree = w.Tick;
            return;
        }
        // 놓친 부족: 컴퓨터는 넉넉하다고 했는데 사람 눈금으로는 곧 바닥이다 → 잘못을 인정한다
        var f = Get(m.Key);
        float sh = m.Short(w);
        float trueDays = truth <= sh ? 0f : m.Flow(w) < -1e-4f ? (truth - sh) / -m.Flow(w) / 24f : 99f;
        if (f != null && f.DaysToShort >= 6f && trueDays <= 2.5f)
            a.Authority.Admit("miss:" + m.Key, Domain.Resources, $"{m.Name} 부족을 못 봤다 — 계기는 {read:0}{m.Unit}, {eye.Name}의 눈금은 {truth:0}{m.Unit}",
                $"{g?.Name ?? "배"} 계기가 틀어져 있었다", "그 계기를 덜 믿고 흐름으로 셈한다 · 교정을 부른다", w.Crew.Where(c => !c.Dead && !c.IsChild));
        Disagree(m, L, g, $"{eye.Name} 눈금 {truth:0.#}{m.Unit} ↔ 계기 {read:0.#}{m.Unit}", eye);
    }

    private void Disagree(ResourceModel m, GaugeLedger L, Room? g, string why, CrewMember? eye)
    {
        var w = _w;
        var a = w.Automation;
        bool was = L.Suspect;
        L.Disagree++;
        L.LastDisagree = w.Tick;
        L.LastWhy = why;
        Disagreements++;
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {m.Name} 계기가 어긋났다 — {why} (계기 믿음 {L.Reliability * 100:0}%)", eye?.Id ?? -1);
        if (eye != null) a.Trusts.Change(eye, -0.01f, $"{g?.Name ?? "배"} {m.Name} 계기가 틀렸다", quiet: true);
        if (!was && L.Suspect)
        {
            Suspects++;
            a.Authority.Learned("계기", $"{g?.Name ?? "배"} {m.Name} 계기를 덜 믿는다 — {L.Disagree}번 어긋남 · 이제 흐름으로 셈한다");
            if (g != null)
            {
                var b = a.Belief.Of(g);
                b.Trust = MathF.Min(b.Trust, 0.45f); // 그 방 감지기 전체를 의심한다 → 위험한 조치 전에 사람을 먼저 보낸다 (v16.6)
                b.Misreads++;
                a.Book.Add(ActKind.Advice, g, why, $"{m.Name} 계기 믿음 {L.Reliability * 100:0}% — 계기 대신 흐름으로 셈한다", "계기 교정 요청", "전기 담당이 교정",
                    "brain:gauge:" + m.Key, SimTime.Hours(12), 12f * 60f, (world, act) => L.Suspect ? (2, "보류 — 아직 교정 전") : (1, "맞았다 — 교정해 다시 맞다"));
            }
        }
    }

    /// <summary>
    /// v16 통합: 예측을 들은 사람의 믿음 — 컴퓨터를 믿는 만큼 × 예측의 확신만큼 "며칠 뒤 모자란다"고 믿는다.
    /// 회의에서 컴퓨터 안건에 손을 들 때 · 아껴 쓸 때 이 믿음을 읽는다 (Mind.Knows 는 사고 열쇠만 쥔다).
    /// </summary>
    private void HeardForecast(CrewMember c, ResourceModel m, ResourceForecast f, BeliefSource src)
    {
        var w = _w;
        int i = Models.IndexOf(m);
        if (i < 0 || c.Dead) return;
        float conf = (0.3f + 0.7f * w.Automation.Trusts.Of(c)) * (0.4f + 0.6f * f.Confidence);
        w.Brain2.Beliefs.Learn(c, Topic.Forecast, i, 1, src, conf, -2, (int)MathF.Round(Math.Clamp(f.DaysToShort, 0f, 99f) * 10f));
    }

    /// <summary>부족 경고: 나흘 안에 문턱 아래 · 믿음이 반 넘으면 방송 (들은 사람만 안다).</summary>
    private void Warn(ResourceModel m, ResourceForecast f, Room? g)
    {
        var w = _w;
        var a = w.Automation;
        if (f.DaysToShort > 4f || f.DaysToShort <= 0.05f) return;
        if (Warnings.Any(x => x.Key == m.Key && w.Tick - x.Tick < SimTime.TicksPerDay && MathF.Abs(x.Days - (f.DaysToShort + (w.Tick - x.Tick) / (float)SimTime.TicksPerDay)) < 0.75f)) return;
        var wn = new ShortWarning { Key = m.Key, Tick = w.Tick, Days = f.DaysToShort, Confidence = f.Confidence, Text = f.Line };
        Warnings.Add(wn);
        if (Warnings.Count > 30) Warnings.RemoveAt(0);
        string key = m.Key;
        float sh = f.Short;
        long due = w.Tick + SimTime.Hours(MathF.Min(72f, f.DaysToShort * 24f + 6f));
        a.Book.Add(ActKind.Forecast, g, $"{m.Name} {f.Basis}", $"{f.Line}", "계획에 올림", "미리 대비", "brain:fc:" + key + ":" + Warnings.Count, 0, (due - w.Tick) / (float)SimTime.Minutes(1),
            (world, act) =>
            {
                var plan = world.Automation.Planner.PlanOf(key);
                float v = Models.First(x => x.Key == key).True(world);
                (int, string) r = v <= sh + Models.First(x => x.Key == key).Tol ? (1, $"맞았다 — {v:0.#}{m.Unit}까지 내려갔다")
                    : plan != null && plan.Acted >= wn.Tick ? (2, $"막았다 — 대책 뒤 {v:0.#}{m.Unit}")
                    : (-1, $"틀렸다 — 아직 {v:0.#}{m.Unit}");
                wn.Score = r.Item1;
                world.Automation.Authority.ForecastGraded(wn, r.Item1);
                return r;
            });
        // 목숨에 걸린 자원(물 · 식량 · 산소 · 전력)은 배 전체에 방송 · 재료 · 추진제는 맡은 사람에게만 (온 배를 걱정시키지 않는다)
        if (key is "materials" or "propellant")
        {
            var sk = key == "materials" ? ShipSim.Core.Skill.Mechanics : ShipSim.Core.Skill.Piloting;
            var who = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => c.SkillLevel(sk)).ThenBy(c => c.Id).FirstOrDefault();
            if (who != null)
            {
                a.Apps.Messages.Add(new PersonalMessage(w.Tick, who.Id, "예측", $"{f.Line} — {f.Basis}"));
                HeardForecast(who, m, f, BeliefSource.Computer); // 개인 메시지 — 믿음 장부로 (Mind.Knows 는 사고 열쇠만 남기고 지운다)
                wn.Heard.Add(who.Id);
            }
            return;
        }
        if (f.Confidence >= 0.4f)
        {
            var b = a.Speak.Announce(a.Authority.Say($"예측 — {f.Line}. {m.Name} {f.Estimate:0}{m.Unit} · 하루 {f.Rate * 24f:+0;-0}{m.Unit}"), g, 1);
            if (b != null)
                foreach (var id in b.HeardBy)
                {
                    wn.Heard.Add(id);
                    var c = w.Crew.FirstOrDefault(x => x.Id == id);
                    if (c == null) continue;
                    HeardForecast(c, m, f, BeliefSource.Broadcast); // 들은 사람만 안다 — 승무원 두뇌 2.0 믿음 (예전 Mind.Knows["forecast:…"] 는 Mind 가 사고가 아니라며 바로 지웠다)
                    if (c.Traits.Calm < 0.4f) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f * f.Confidence); // 걱정 많은 사람은 마음이 쓰인다
                }
        }
    }

    /// <summary>기한이 된 예측을 다시 재서 채점한다 (그 뒤 컴퓨터가 믿는 값과 견준다).</summary>
    private void Grade()
    {
        var w = _w;
        for (int i = _checks.Count - 1; i >= 0; i--)
        {
            var (key, due, predicted) = _checks[i];
            if (w.Tick < due) continue;
            _checks.RemoveAt(i);
            var f = Get(key);
            var m = Models.First(x => x.Key == key);
            if (f == null) continue;
            bool hit = MathF.Abs(predicted - f.Estimate) <= MathF.Max(m.Tol, 0.1f * MathF.Abs(f.Estimate));
            var s = _skill.GetValueOrDefault(key);
            _skill[key] = (s.hits * 0.95f + (hit ? 1f : 0f), s.total * 0.95f + 1f);
            Graded++;
            if (hit) Hits++;
        }
    }

    /// <summary>고장 위험 · 바깥 위험 (컴퓨터가 데이터선으로 보는 설비만).</summary>
    private void Risk()
    {
        var w = _w;
        Risks.Clear();
        foreach (var mm in w.Ship.Machines.Where(x => !x.Body.Room.Detached && x.Body.Room.DataLinked && !x.Stopped)
                     .Select(x => (m: x, p: 1f - MathF.Pow(1f - MathF.Min(0.5f, x.FaultChancePerHour), 24f) + (x.Omen != null ? 0.25f : 0f)))
                     .Where(x => x.p >= 0.08f).OrderByDescending(x => x.p).ThenBy(x => x.m.Body.Id).Take(4))
            Risks.Add(new FailureRisk(mm.m.Body.Id, mm.m.Name, mm.m.Body.Room.Name, MathF.Min(0.99f, mm.p),
                mm.m.Omen != null ? "전조가 보인다" : $"마모 {mm.m.Wear * 100:0}%"));
        var parts = new List<string>();
        if (w.Hazards.StormActive) parts.Add("태양 폭풍 중");
        foreach (var sf in w.Automation.SpaceForecasts.Where(x => !x.Graded).Take(2)) parts.Add($"{sf.Name} 예보");
        HazardLine = parts.Count > 0 ? string.Join(" · ", parts) : "";
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Samples); I(ManualReads); I(Disagreements); I(Graded); I(Hits); I(Suspects); I(Warnings.Count);
        foreach (var m in Models) { if (Get(m.Key) is ResourceForecast f) { F(f.Estimate); F(f.Confidence); } I(Ledger(m.Key).Disagree); }
    }
}
