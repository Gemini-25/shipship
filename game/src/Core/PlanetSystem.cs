using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 행성: 항해 중 가끔 '착륙 창'이 열린다 — 주컴퓨터가 후보 2~3개를 멀리서 관측(흐리다)해 위험 · 보상 · 배 상태로 고르거나 지나친다.
//   고르면 접근(몇 시간) → 하강(기동 방송 · 몸 고정) → 머무름(임무 기간) → 이륙(배가 성하고 날씨가 허락하면) → 항해를 잇는다. 그동안 항로는 멈춘다.
//   가끔 불시착: 엔진이 시원찮거나 운이 나쁘면 예고 없이 내려앉는다 — 선체가 갈라지고 엔진이 멎어, 고치기 전엔 못 뜬다.

public enum PlanetPhase : byte { None, Window, Approach, Descent, Landed, Liftoff }

public sealed class PlanetVisit
{
    public int PlanetId { get; init; }
    public string Name { get; init; } = "";
    public string Key { get; init; } = "";
    public long Landed { get; init; }
    public long Left { get; set; } = -1;
    public bool Crash { get; init; }
    public string Outcome { get; set; } = "";
}

public sealed class PlanetStats
{
    public int Windows, Skipped, Landings, Crashes, Liftoffs, Holds, Early;
    public string Line() => $"착륙 창 {Windows} · 지나침 {Skipped} · 착륙 {Landings} · 불시착 {Crashes} · 이륙 {Liftoffs} · 미룸 {Holds} · 일찍 떠남 {Early}";
}

public sealed partial class PlanetSystem
{
    private readonly World _w;
    private readonly Rng _rng;
    /// <summary>시험용: 착륙 창을 열지 않는다.</summary>
    public static bool Off;
    /// <summary>시험용: 불시착을 굴리지 않는다.</summary>
    public static bool NoCrash;

    public PlanetPhase Phase { get; private set; }
    public long PhaseSince { get; private set; }
    public List<Planet> Candidates { get; } = new();
    /// <summary>가는 중 · 내려앉은 행성.</summary>
    public Planet? Here { get; private set; }
    public long NextWindow { get; private set; }
    public long StayUntil { get; private set; }
    public long LiftAt { get; private set; } = -1;
    public bool Crashed { get; private set; }
    public List<PlanetVisit> Visits { get; } = new();
    public HashSet<string> SeenSpecial { get; } = new();
    public int NextId { get; private set; } = 1;
    public PlanetStats Stats { get; } = new();
    /// <summary>주컴퓨터의 마지막 판단 (화면 · 기록).</summary>
    public string Decision { get; private set; } = "";
    public List<string> Notes { get; } = new();

    /// <summary>행성에 가는 · 머무는 동안 항로는 멈춘다.</summary>
    public bool Holding => Phase is PlanetPhase.Approach or PlanetPhase.Descent or PlanetPhase.Landed or PlanetPhase.Liftoff;
    /// <summary>배가 땅에 있다.</summary>
    public bool Landed => Phase is PlanetPhase.Landed || Phase == PlanetPhase.Liftoff && _w.Tick < LiftAt;

    public PlanetSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 8111 + 29));
        NextWindow = SimTime.Hours(24 * 8) + _rng.Range(0, SimTime.Hours(72)); // 처음 여드레는 배를 길들인다
    }

    private void Note(string text)
    {
        Notes.Add($"{SimTime.Clock(_w.Tick)} {text}");
        if (Notes.Count > 40) Notes.RemoveAt(0);
    }

    private void Go(PlanetPhase p) { Phase = p; PhaseSince = _w.Tick; }

    private float Sensor => Math.Clamp(_w.Voyage.SensorMul * _w.Exterior.AntennaFactor * (_w.Automation.CoreOnline ? 1f : 0.6f), 0.3f, 1.3f);

    public void Update(float dt)
    {
        var w = _w;
        switch (Phase)
        {
            case PlanetPhase.None:
                if (Off || w.Tick < NextWindow) return;
                if (w.Expedition.Halted || w.Voyage.Current.Kind == LegKind.Port || w.Commander.Cases.Any(k => k.Open)) { NextWindow = w.Tick + SimTime.Hours(6); return; }
                OpenWindow();
                break;
            case PlanetPhase.Window:
                if (w.Tick - PhaseSince >= SimTime.Minutes(40)) Decide();
                break;
            case PlanetPhase.Approach:
                if (w.Tick - PhaseSince >= SimTime.Hours(Here!.Id % 3 + 3)) Descend();
                break;
            case PlanetPhase.Descent:
                if (w.Tick >= LiftAt) Touchdown();
                break;
            case PlanetPhase.Landed:
                UpdateLanded(dt);
                break;
            case PlanetPhase.Liftoff:
                if (w.Tick >= LiftAt + SimTime.Minutes(10)) Gone();
                break;
        }
    }

    /// <summary>착륙 창: 후보 2~3개를 멀리서 본다.</summary>
    public void OpenWindow(bool forceCrash = false)
    {
        var w = _w;
        Candidates.Clear();
        int n = 2 + _rng.Range(0, 2);
        for (int i = 0; i < n; i++)
        {
            var p = PlanetGen.Make(_rng, NextId++, Storyteller.Level, SeenSpecial);
            PlanetGen.Observe(p, _rng, Sensor);
            Candidates.Add(p);
        }
        Stats.Windows++;
        Go(PlanetPhase.Window);
        // 가끔 불시착: 엔진이 시원찮으면 더 잦다
        float eng = w.Propulsion.Engines.Select(m => m.FaultFactor * (0.6f + 0.4f * m.Condition)).DefaultIfEmpty(0f).Max(); // 꺼 둔 엔진은 고장이 아니다 — 고장 · 상태로 본다
        float crash = 0.015f * Storyteller.Level + (eng < 0.4f ? 0.25f : 0f) + (w.Propulsion.Propellant < w.Propulsion.Capacity * 0.05f ? 0.15f : 0f);
        if (forceCrash || _rng.Chance(crash) && !NoCrash) { Crash(Candidates.OrderByDescending(c => c.Danger).First()); return; }
        string list = string.Join(" · ", Candidates.Select(c => $"{c.Title} (위험 {c.KnownDanger * 100:0}% · 보상 {c.KnownReward * 100:0}% · 확실 {c.Certainty * 100:0}%)"));
        Note($"착륙 창 — 후보 {Candidates.Count}: {list}");
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 내릴 만한 행성 {Candidates.Count}곳을 멀리서 본다 — {string.Join(" · ", Candidates.Select(c => c.Title))}");
    }

    /// <summary>배가 얼마나 상했나 0~1 (엔진 · 선체 · 공기 새는 방 · 다친 사람).</summary>
    public float ShipStrain()
    {
        var w = _w;
        float eng = w.Propulsion.Engines.Select(m => m.FaultFactor * (0.6f + 0.4f * m.Condition)).DefaultIfEmpty(1f).Average();
        int leaking = w.Ship.LiveRooms.Count(r => r.Leaking);
        int hurt = w.Crew.Count(c => !c.Dead && c.Vitals.Health < 0.6f);
        return Math.Clamp((1f - eng) * 0.6f + leaking * 0.15f + hurt * 0.08f, 0f, 1f);
    }

    /// <summary>배에 모자란 것이 이 행성에 있나 (재료 갈래별 남은 날).</summary>
    private float Need(Planet p)
    {
        var w = _w;
        float need = 0f;
        foreach (var (k, v) in p.Rich)
        {
            if (ExpeditionSites.CatOf(k) is not MatCat cat) continue;
            float days = w.Expedition.DaysLeft(cat);
            float urgency = days < 3f ? 1f : days < 8f ? 0.5f : 0.15f;
            need += v * urgency;
        }
        if (p.Rich.ContainsKey(ItemKind.Ice) && w.Expedition.DaysLeft(MatCat.Water) < 6f) need += 0.3f;
        return need;
    }

    /// <summary>주컴퓨터가 고른다: 보상 · 필요 − 위험 × (배 상태 · 방침 · 확실성). 다 별로면 지나친다.</summary>
    private void Decide()
    {
        var w = _w;
        float strain = ShipStrain();
        int route = w.Policies["route"]; // 1 안전 · 3 탐사
        float caution = (route == 1 ? 1.4f : route == 3 ? 0.75f : 1f) * (1f + strain * 1.5f);
        foreach (var p in Candidates)
        {
            float blur = 1f - p.Certainty;
            p.CompScore = p.KnownReward * 1.1f + Need(p) * 0.5f - (p.KnownDanger + blur * 0.2f) * caution;
            var why = new List<string> { $"보상 {p.KnownReward * 100:0}%", $"위험 {p.KnownDanger * 100:0}%" };
            if (Need(p) > 0.3f) why.Add("모자란 것이 있다");
            if (blur > 0.5f) why.Add("잘 안 보인다");
            if (p.Special) why.Add("이름난 곳");
            p.CompNote = string.Join(" · ", why);
        }
        var best = Candidates.OrderByDescending(c => c.CompScore).First();
        bool hurry = w.Purpose?.Behind == true && Need(best) < 0.6f; // v23 임무 기한이 빠듯하다
        bool go = best.CompScore > 0.05f && strain < 0.55f && !hurry;
        if (!go)
        {
            Stats.Skipped++;
            Decision = strain >= 0.55f ? $"지나친다 — 배가 상해 내릴 때가 아니다 (상한 정도 {strain * 100:0}%)"
                : hurry ? $"지나친다 — 임무 기한이 빠듯하다 (남은 길 {w.Purpose!.RouteLeft:0.0}일 · 기한 {w.Purpose.DaysLeft:0.0}일)"
                : $"지나친다 — 내릴 만한 곳이 없다 (가장 나은 {best.Title}: {best.CompNote})";
            Note(Decision);
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 행성에 {Decision}");
            Candidates.Clear();
            Go(PlanetPhase.None);
            NextWindow = w.Tick + SimTime.Hours(24 * (4 + _rng.Range(0f, 3f)));
            return;
        }
        Here = best;
        Decision = $"{best.Title}에 내린다 — {best.CompNote}" + (Candidates.Count > 1 ? $" (다른 후보: {string.Join(" · ", Candidates.Where(c => c != best).Select(c => $"{c.Name} {c.CompScore:0.00}"))})" : "");
        Note(Decision);
        w.RaiseAlert($"행성 착륙을 정했다 — {best.Title} · {best.Line}", null, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터가 {Ko.EulReul(best.Name)} 골랐다 — {best.CompNote}", null, log: true);
        Go(PlanetPhase.Approach);
    }

    private void Descend()
    {
        var w = _w;
        var p = Here!;
        float g = Math.Clamp(0.22f + 0.14f * p.Gravity + (p.Pressure > 200f ? 0.08f : 0f), 0.2f, 0.6f);
        var m = w.Maneuver.Begin(ManeuverKind.Landing, 6f, g, $"{p.Name} 착륙 하강", 8f);
        LiftAt = m.End;
        Go(PlanetPhase.Descent);
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {p.Name}{Ko.EuRo(p.Name)} 내려간다 — 6분 뒤 하강 · 모두 몸을 고정");
    }

    private void Touchdown()
    {
        var w = _w;
        var p = Here!;
        Land(p, crash: false);
    }

    private void Land(Planet p, bool crash)
    {
        var w = _w;
        Here = p;
        Crashed = crash;
        Go(PlanetPhase.Landed);
        LiftAt = -1;
        StayUntil = w.Tick + (long)(p.StayDays * SimTime.TicksPerDay);
        if (p.Special) SeenSpecial.Add(p.Key);
        Stats.Landings++;
        Visits.Add(new PlanetVisit { PlanetId = p.Id, Name = p.Name, Key = p.Key, Landed = w.Tick, Crash = crash });
        string hidden = p.Hidden.Count > 0 ? $" · 내려와 보니: {string.Join(" · ", p.Hidden.Select(PlanetGen.FeatureName))}" : "";
        p.Hidden.Clear();
        string feats = p.Features.Count > 0 ? $" · {string.Join(" · ", p.Features.Select(PlanetGen.FeatureName))}" : "";
        Note((crash ? "불시착 — " : "착륙 — ") + p.Title + hidden);
        w.RaiseAlert(crash ? $"불시착 — {p.Title} · 고치기 전엔 뜰 수 없다" : $"착륙 — {p.Title} · {p.StayDays:0.0}일 머문다", null, crash ? AlertLevel.Critical : AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Milestone, (crash ? "불시착 — " : "행성에 내렸다 — ") + $"{p.Title} · {p.Line}{feats}{hidden}", null, log: true);
        OnLanded(p, crash);
        w.Purpose?.Data(2, $"{p.Name}에 내렸다");
    }

    /// <summary>불시착: 예고 없이 내려앉는다 — 충격 · 선체 균열 · 엔진 고장.</summary>
    public void Crash(Planet p)
    {
        var w = _w;
        Stats.Crashes++;
        Candidates.Clear();
        Candidates.Add(p);
        PlanetGen.Observe(p, _rng, Sensor * 0.5f);
        Decision = $"{p.Title}에 불시착했다 — 엔진이 버티지 못했다";
        var rooms = w.Ship.LiveRooms.Where(r => r.Cells.Count > 0).ToList();
        var low = rooms.OrderByDescending(r => r.Cells.Max(c => c.Y)).First(); // 배 밑바닥 쪽
        w.Maneuver.Shock(0.95f, new System.Numerics.Vector2(0f, -1f), low, $"{p.Name} 불시착", warned: false);
        foreach (var m in w.Propulsion.Engines.ToList()) { w.Machines.Break(m, FaultKind.InjectorClog); w.Machines.Break(m, FaultKind.BearingWear); } // 하강 엔진이 버티지 못했다
        Hazards.Apply(w, HazardKind.HullCrack, low.Cells.OrderByDescending(c => c.Y).First(), -1);
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Away && c.Room == low).Take(3)) NeedsSystem.AddInjury(c.Vitals, 0.15f + _rng.Range(0f, 0.2f), "불시착 충격");
        Land(p, crash: true);
    }

    /// <summary>뜰 수 있나 (엔진 · 추진제 · 새는 방). 안 되면 까닭.</summary>
    public string? CannotLift()
    {
        var w = _w;
        if (!w.Propulsion.Engines.Any(m => m.FaultFactor * (0.6f + 0.4f * m.Condition) > 0.3f)) return "엔진이 멎었다"; // 땅에선 엔진을 꺼 둔다 — 고장 · 상태로 본다
        if (w.Propulsion.Capacity > 0f && w.Propulsion.Propellant < w.Propulsion.Capacity * 0.04f) return "추진제가 모자란다";
        if (w.Ship.LiveRooms.Any(r => r.Leaking)) return "선체가 새고 있다";
        return null;
    }

    private long _lastHoldNote = -1;

    private void UpdateLanded(float dt)
    {
        var w = _w;
        UpdateGround(dt);
        if (w.Tick < StayUntil && !LeaveEarly()) return;
        if (CannotLift() is string why)
        {
            if (_lastHoldNote < 0 || w.Tick - _lastHoldNote >= SimTime.Hours(6))
            {
                _lastHoldNote = w.Tick;
                Stats.Holds++;
                Decision = $"아직 못 뜬다 — {why}";
                Note(Decision);
                w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {Here!.Name}에서 아직 못 뜬다 — {why}");
            }
            return;
        }
        if (HoldForWeather() is string wait)
        {
            if (_lastHoldNote < 0 || w.Tick - _lastHoldNote >= SimTime.Hours(3))
            {
                _lastHoldNote = w.Tick;
                Stats.Holds++;
                Decision = $"이륙을 미룬다 — {wait}";
                Note(Decision);
                w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 이륙을 미룬다 — {wait}");
            }
            return;
        }
        Lift(w.Tick < StayUntil ? "일찍" : "");
    }

    public void Lift(string early = "")
    {
        var w = _w;
        var p = Here!;
        if (early.Length > 0) Stats.Early++;
        float g = Math.Clamp(0.25f + 0.18f * p.Gravity, 0.2f, 0.65f);
        foreach (var e in w.Propulsion.Engines) e.Parked = false; // 꺼 둔 엔진을 다시 켠다
        var m = w.Maneuver.Begin(ManeuverKind.Landing, 6f, g, $"{p.Name} 이륙", 10f);
        LiftAt = m.End;
        Go(PlanetPhase.Liftoff);
        OnLiftoff(p);
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {p.Name}에서 {(early.Length > 0 ? "일찍 " : "")}뜬다 — 6분 뒤 점화 · 모두 몸을 고정");
    }

    private void Gone()
    {
        var w = _w;
        var p = Here!;
        Stats.Liftoffs++;
        if (Visits.LastOrDefault() is PlanetVisit v && v.PlanetId == p.Id)
        {
            v.Left = w.Tick;
            v.Outcome = VisitLine(p);
        }
        float days = (w.Tick - (Visits.LastOrDefault()?.Landed ?? w.Tick)) / (float)SimTime.TicksPerDay;
        w.History.Add(w, HistoryKind.Milestone, $"{p.Name}에서 떠났다 — {days:0.0}일 머묾" + (VisitLine(p) is string vl && vl.Length > 0 ? $" · {vl}" : ""), null, log: true);
        Here = null;
        Crashed = false;
        Candidates.Clear();
        Go(PlanetPhase.None);
        LiftAt = -1;
        NextWindow = w.Tick + SimTime.Hours(24 * (5 + _rng.Range(0f, 4f) - Storyteller.Level * 0.25f));
    }

    /// <summary>시험용: 지금 바로 이 행성에 내린다 (예고 없이 바로 땅에).</summary>
    public void LandNow(Planet p, bool crash = false)
    {
        if (crash) { Crash(p); return; }
        Candidates.Clear();
        Candidates.Add(p);
        Land(p, crash: false);
    }

    /// <summary>시험용: 다음 착륙 창을 지금으로.</summary>
    public void WindowNow() => NextWindow = _w.Tick;

    public Planet NewPlanet(int level) => PlanetGen.Make(_rng, NextId++, level, SeenSpecial);

    /// <summary>이 행성에서 사고 무게 (우주의 운석 · 우주 날씨는 거의 없다).</summary>
    public float HazardMul(string key)
    {
        if (!Landed) return 1f;
        return key switch
        {
            "meteor" or "bigmeteor" or nameof(HazardKind.MicroShower) or nameof(HazardKind.MeteorShower) or nameof(HazardKind.DebrisCloud) => Here!.Has(PlanetFeature.MeteorRain) ? 0.6f : 0.05f,
            nameof(HazardKind.SolarStorm) => Here!.Has(PlanetFeature.NoMagnet) ? 1.2f : Here.Pressure > 30f ? 0.2f : 0.6f,
            _ => 1f,
        };
    }

    public void Hash(Action<int> I, Action<float> F)
    {
        I((int)Phase); I(Stats.Windows); I(Stats.Landings); I(Here?.Id ?? -1);
    }
}
