using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 → v16.16 "주컴퓨터 두뇌 2.0"의 바탕: 앞날 예측 · 계획표 · 예측 채점(배우기).
//  예측: 지켜보는 값(남은 식량 · 물 · 산소 · 컴퓨터 온도 · 기력 · 연산 부하)을 한 시간마다 재고, 최근 여섯 시간 추세로 여섯 시간 뒤를 내다본다.
//        컴퓨터는 자기가 믿는 배로 잰다(감지기 값이 깨지거나 데이터선이 끊긴 방은 낡은 값) — 그래서 틀린다.
//        기한에 실제 값과 견줘 채점하고, 갈래마다 맞힌 비율이 다음 예측의 "믿음"이 된다 (믿음이 낮으면 방송하지 않고 조용히 지켜본다).
//  계획: 예측이 문턱을 넘으면 계획표에 할 일(목표 · 조치 · 까닭 · 우선순위)을 올리고 다섯 칸 기록에 남긴다 — 이미 있는 조치(배급 앞당김 · 모듈 줄임 ·
//        물 아끼기 회의 · 당번 줄임)로 이어진다. 믿음이 높으면 미리 방송한다.
//  넓히기: v16.16 배 전체 계획자 · 승무원 개인 모형은 Predictors(지켜볼 값) · PlanRules(문턱 → 할 일) 표에 줄을 더하고, 할 일을 제안(OnAccept)으로 싣는다.

/// <summary>지켜볼 값 하나: 재는 법 · 단위 · 낮은/높은 문턱 · 채점 허용 오차 · 넘으면 세울 계획.</summary>
public sealed record Predictor(string Key, string Name, string Unit, Func<World, float?> Measure, Func<World, float> Low, Func<World, float> High, float Tolerance, string LowGoal, string HighGoal);

/// <summary>예측 하나 (지금 값 → 몇 시간 뒤 값 · 믿음 · 근거). 기한에 채점.</summary>
public sealed class Prediction
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public long Tick { get; init; }
    public long Due { get; init; }
    public float Now { get; init; }
    public float Value { get; init; }
    public float Slope { get; init; }
    public float Confidence { get; init; }
    public string Basis { get; init; } = "";
    public float? Actual { get; set; }
    public bool? Hit { get; set; }
}

/// <summary>계획표 한 줄: 목표 · 조치 · 까닭 · 우선순위 · 상태(예정 → 실행 · 제안 · 지남).</summary>
public sealed class PlanItem
{
    public long Tick { get; init; }
    public string Key { get; init; } = "";
    public string Goal { get; init; } = "";
    public string Action { get; init; } = "";
    public string Why { get; init; } = "";
    public float Priority { get; init; }
    public string State { get; set; } = "예정";
}

public sealed class ComputerForesight
{
    private readonly World _w;
    public ComputerForesight(World w) => _w = w;

    public const float Horizon = 6f; // 시간
    private long _next;
    private readonly Dictionary<string, List<float>> _samples = new();
    private readonly Dictionary<string, (int hits, int total)> _score = new();

    public List<Prediction> Current { get; } = new();
    public List<Prediction> Pending { get; } = new();
    public List<Prediction> Graded { get; } = new();
    public List<PlanItem> Plan { get; } = new();
    public int Made, Hits, Misses, Plans;

    private static float? AvgO2(World w)
    {
        float s = 0f; int n = 0;
        foreach (var r in w.Ship.LiveRooms) { if (r.Type == RoomType.Corridor) continue; var b = w.Automation.Belief.Of(r); s += b.O2; n++; }
        return n == 0 ? 21f : s / n;
    }

    private static float? AvgRest(World w)
    {
        float s = 0f; int n = 0;
        foreach (var c in w.Crew) if (!c.Dead && !c.IsChild) { s += c.Needs.Rest; n++; }
        return n == 0 ? null : s / n;
    }

    /// <summary>지켜볼 값 표 (v16.16에서 줄을 더한다).</summary>
    public static readonly List<Predictor> Predictors = new()
    {
        new("food", "남은 식량", "일치", w => w.Crew.Any(c => !c.Dead) ? FoodPolicy.FoodDays(w) : null, w => 2.5f, w => float.MaxValue, 0.35f,
            "식량 — 배급을 앞당긴다 (식단 계획)", ""),
        new("water", "물탱크", "L", w => w.Water.Level, w => w.Water.Capacity * 0.15f, w => float.MaxValue, 15f,
            "물 — 아끼기 (빨래 · 샤워 줄임 · 회의 안건)", ""),
        new("o2", "산소 (믿는 값)", "%", AvgO2, w => 18.5f, w => float.MaxValue, 0.6f,
            "산소 — 공기 구역 · 산소 발생기 점검", ""),
        new("heat", "컴퓨터 온도", "℃", w => w.Automation.ComputerBody?.Room.Air.Temperature, w => float.MinValue, w => AutomationSystem.OverheatC - 2f, 1.5f,
            "", "컴퓨터 열 — 비필수 모듈부터 줄인다"),
        new("rest", "평균 기력", "", AvgRest, w => 0.25f, w => float.MaxValue, 0.08f,
            "기력 — 당번을 줄이고 재운다 (피로 경보)", ""),
        new("load", "연산 부하", "%", w => w.Automation.Present ? w.Automation.Load * 100f : null, w => float.MinValue, w => 90f, 12f,
            "", "연산 — 낮은 순위 모듈을 쉬게 한다"),
    };

    /// <summary>갈래마다 맞힌 비율 (처음엔 반쯤 믿는다).</summary>
    public float Skill(string key) => _score.TryGetValue(key, out var s) ? (s.hits + 1f) / (s.total + 2f) : 0.5f;

    public void Update()
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(1);
        var a = w.Automation;
        // 1) 기한이 된 예측 채점 (컴퓨터가 멎어 있어도 나중에 기록을 본다)
        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            var p = Pending[i];
            if (w.Tick < p.Due) continue;
            Pending.RemoveAt(i);
            var pr = Predictors.FirstOrDefault(x => x.Key == p.Key);
            float? actual = pr?.Measure(w);
            if (pr == null || actual is not float act) continue;
            p.Actual = act;
            p.Hit = MathF.Abs(p.Value - act) <= MathF.Max(pr.Tolerance, 0.12f * MathF.Abs(act));
            var s = _score.GetValueOrDefault(p.Key);
            _score[p.Key] = (s.hits + (p.Hit == true ? 1 : 0), s.total + 1);
            if (p.Hit == true) { Hits++; a.Book.Today.ForecastHits++; } else Misses++;
            a.Book.Today.Forecasts++;
            Graded.Add(p);
            if (Graded.Count > 60) Graded.RemoveAt(0);
        }
        if (!a.Present || !a.MainOnline) return;
        // 2) 재고 · 내다본다
        Current.Clear();
        foreach (var pr in Predictors)
        {
            if (pr.Measure(w) is not float now) continue;
            if (!_samples.TryGetValue(pr.Key, out var list)) _samples[pr.Key] = list = new List<float>();
            list.Add(now);
            if (list.Count > 7) list.RemoveAt(0);
            if (list.Count < 3) continue;
            float slope = (list[^1] - list[0]) / (list.Count - 1); // 한 시간마다
            float value = now + slope * Horizon;
            float conf = Skill(pr.Key);
            var p = new Prediction
            {
                Key = pr.Key, Name = pr.Name, Unit = pr.Unit, Tick = w.Tick, Due = w.Tick + SimTime.Hours(Horizon), Now = now, Value = value, Slope = slope, Confidence = conf,
                Basis = $"지난 {list.Count - 1}시간 {(slope >= 0 ? "+" : "")}{slope:0.##}{pr.Unit}/시간",
            };
            Current.Add(p);
            Pending.Add(p);
            if (Pending.Count > 80) Pending.RemoveAt(0);
            Made++;
            // 3) 계획: 문턱을 넘을 것 같으면 할 일을 올린다 (지금 이미 넘었으면 다른 조치들이 이미 움직인다)
            float low = pr.Low(w), high = pr.High(w);
            string? goal = value < low && now >= low && pr.LowGoal != "" ? pr.LowGoal : value > high && now <= high && pr.HighGoal != "" ? pr.HighGoal : null;
            if (goal == null || Plan.Any(x => x.Key == pr.Key && w.Tick - x.Tick < SimTime.Hours(Horizon))) continue;
            var item = new PlanItem
            {
                Tick = w.Tick, Key = pr.Key, Goal = goal.Split(" — ")[0], Action = goal.Contains(" — ") ? goal.Split(" — ")[1] : goal,
                Why = $"{pr.Name} {now:0.#}{pr.Unit} → {Horizon:0}시간 뒤 {value:0.#}{pr.Unit} (믿음 {conf * 100:0}%)", Priority = conf,
            };
            Plan.Add(item);
            if (Plan.Count > 30) Plan.RemoveAt(0);
            Plans++;
            var key = pr.Key;
            float thr = value < low ? low : high;
            bool below = value < low;
            a.Book.Add(ActKind.Forecast, pr.Key == "heat" ? a.ComputerBody?.Room : null, $"{pr.Name} {now:0.#}{pr.Unit} · {p.Basis}", $"{Horizon:0}시간 뒤 {value:0.#}{pr.Unit} — 문턱 {thr:0.#} (믿음 {conf * 100:0}%)",
                $"계획: {item.Action}", conf >= 0.6f ? "방송" : "", "plan:" + key, SimTime.Hours(Horizon), Horizon * 60f,
                (world, act) => Predictors.First(x => x.Key == key).Measure(world) is float v ? ((below ? v < thr + pr.Tolerance : v > thr - pr.Tolerance) ? (1, $"맞았다 — {v:0.#}{pr.Unit}") : (-1, $"틀렸다 — {v:0.#}{pr.Unit} (문턱을 안 넘었다)")) : (2, "참고 — 잴 수 없다"));
            if (conf >= 0.6f && key is "food" or "water" or "o2") a.Speak.Announce(a.Voice.Style($"예측 — {Horizon:0}시간 뒤 {pr.Name} {value:0.#}{pr.Unit}. {item.Action}"), null, 1);
        }
        foreach (var it in Plan) if (it.State == "예정" && w.Tick - it.Tick > SimTime.Hours(Horizon)) it.State = "지남";
    }
}

public sealed partial class AutomationSystem
{
    private ComputerForesight? _foresight;
    /// <summary>v16.6 → v16.16 앞날 예측 · 계획표.</summary>
    public ComputerForesight Foresight => _foresight ??= new ComputerForesight(_world);
}
