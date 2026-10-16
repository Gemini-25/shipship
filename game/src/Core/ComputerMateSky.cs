using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 ② 우주 날씨 예보: 여섯 시간마다 다음 하루의 태양 폭풍 · 운석우 확률 (지금 구간 · 바깥 소식 · 센서 정확도 · 잡음).
//  높으면: 선외 작업을 미루고(작업 고르기 훅) · 밖의 드론을 들이고 · 대피소를 점검해 둔다.
//  하루 뒤 채점한다: 맞힘 · 헛예보(미룬 사람이 투덜댄다) · 놓침(밖의 사람이 서둘러 돌아온다 → 인정하고 약속).
//  "선외 작업 전엔 한 번 더 살피겠다" 약속이 있으면 사람이 나갈 때 다시 본다 (과부하면 못 지킨다).

public sealed class SkyForecast
{
    public long Tick { get; init; }
    public float PStorm { get; init; }
    public float PShower { get; init; }
    public bool Held { get; init; }
    public bool StormCame, ShowerCame, Judged, Hit;
    public long JudgedAt = -1;
    public string Verdict = "";
}

public sealed partial class ShipMate
{
    public List<SkyForecast> Forecasts { get; } = new();
    public SkyForecast? LastSky => Forecasts.Count > 0 ? Forecasts[^1] : null;
    /// <summary>예보 점수 (낮을수록 잘 맞힘 — 확률 제곱 오차 평균).</summary>
    public float SkyScore { get; private set; }
    private int _skyJudged;
    private bool _stormWas, _showerWas, _outsideWas;
    private long _skyNext, _outNext;
    public int Recalls, ShelterChecks;
    /// <summary>대피소를 점검해 달라 (예보가 높을 때 — 안내 · 의료 자리 사람이 간다).</summary>
    public bool ShelterWanted { get; private set; }
    /// <summary>대피소를 점검해 둔 때까지 (폭풍이 오면 사람들이 덜 긴장한다).</summary>
    public long ShelterReady { get; private set; } = -1;
    private int _shelterBy = -1;

    /// <summary>선외 작업을 미루라 (Chores 훅).</summary>
    public bool EvaHold => LastSky is SkyForecast f && f.Held && _w.Tick - f.Tick < SimTime.Hours(12);

    public string SkyLine(SkyForecast f)
    {
        string s = f.PStorm < 0.08f && f.PShower < 0.08f ? "우주 날씨 맑음" : $"우주 날씨: 태양 폭풍 {f.PStorm * 100:0}% · 운석우 {f.PShower * 100:0}%";
        return f.Held ? s + " — 선외 작업은 미루고 드론은 들여 둔다" : s;
    }

    private void SkyHourly()
    {
        var w = _w;
        if (w.Tick < _skyNext) return;
        _skyNext = w.Tick + SimTime.Hours(6);
        MakeForecast();
    }

    /// <summary>예보 한 번 (시험 · 약속도 부른다).</summary>
    public SkyForecast MakeForecast(float? storm = null, float? shower = null)
    {
        var w = _w;
        var a = A;
        float more = HazardSystem.RandomDays > 0f ? 1f + 1f / HazardSystem.RandomDays : 1f;
        float ls = 0.05f * w.Voyage.HazardMul(nameof(HazardKind.SolarStorm)) * more, lm = 0.07f * w.Voyage.HazardMul(nameof(HazardKind.MeteorShower)) * more;
        float acc = a.Core.Accuracy * (w.Sensors.CommsRoom != null ? 1f : 0.8f);
        float ps = storm ?? Math.Clamp((1f - MathF.Exp(-ls)) * (1f + R.Range(-0.6f, 0.6f) * (1f - acc) * 2f), 0f, 0.95f);
        float pm = shower ?? Math.Clamp((1f - MathF.Exp(-lm)) * (1f + R.Range(-0.6f, 0.6f) * (1f - acc) * 2f), 0f, 0.95f);
        float bar = a.Character.Caution > 0.25f ? 0.25f : a.Character.Caution < -0.25f ? 0.45f : 0.35f; // 신중하면 일찍 미룬다
        var f = new SkyForecast { Tick = w.Tick, PStorm = ps, PShower = pm, Held = ps >= bar || pm >= bar };
        Forecasts.Add(f);
        if (Forecasts.Count > 40) { Forecasts.RemoveAt(0); _skyJudged = Math.Max(0, _skyJudged - 1); }
        if (f.Held) Brace(f);
        return f;
    }

    /// <summary>예보가 높으면: 드론을 들이고 대피소를 점검한다.</summary>
    private void Brace(SkyForecast f)
    {
        var w = _w;
        Recalls += w.Drones.WeatherRecall("우주 날씨 예보 — 폭풍 · 운석우에 앞서 들어온다");
        if (w.Tick > ShelterReady) ShelterWanted = true;
        Say($"{SkyLine(f)} · 대피소 물 · 마스크를 점검해 달라");
    }

    /// <summary>대피소 점검을 맡을 사람 (MateActivity): 안내 · 의료 자리 → 아무나.</summary>
    public Room? ShelterFor(CrewMember c)
    {
        if (!ShelterWanted || _shelterBy >= 0 && _shelterBy != c.Id && Crew(_shelterBy) is CrewMember b && b.CanAct && b.Job?.Activity is MateActivity) return null;
        var role = _w.CrisisCrew.BillRole(c);
        if (_shelterBy < 0 && role is not (StationRole.Guide or StationRole.Medical) && _w.Tick - (LastSky?.Tick ?? 0) < SimTime.Hours(1)) return null; // 처음 한 시간은 제 자리 사람이
        var (sh, _) = Facilities.Best(_w.Ship, "shelter", r => !r.Detached && !r.OffLimits);
        return sh;
    }

    internal void ShelterTaken(CrewMember c) => _shelterBy = c.Id;

    internal void ShelterChecked(CrewMember c, Room sh)
    {
        var w = _w;
        ShelterWanted = false;
        _shelterBy = -1;
        ShelterChecks++;
        ShelterReady = w.Tick + SimTime.TicksPerDay;
        Life.Diary(w, c, Persona.Say(c, $"{sh.Name} 물통 · 마스크 · 담요를 셌다. 폭풍이 온다고"));
        Say($"{Ko.IGa(c.Name)} {Ko.EulReul(sh.Name)} 점검했다 — 물 · 마스크 · 담요 다 있다", sh, -1, c.Id);
    }

    /// <summary>매 틱: 폭풍 · 운석우가 실제로 왔나 · 사람이 밖으로 나가나 (약속).</summary>
    private void SkyTick()
    {
        var w = _w;
        bool storm = w.Hazards.StormActive, shower = w.Hazards.Shower.Count > 0;
        if (storm && !_stormWas || shower && !_showerWas)
            for (int i = _skyJudged; i < Forecasts.Count; i++)
            {
                var f = Forecasts[i];
                if (w.Tick - f.Tick > SimTime.TicksPerDay) continue;
                if (storm && !_stormWas) f.StormCame = true;
                if (shower && !_showerWas) f.ShowerCame = true;
            }
        if (storm && !_stormWas && w.Tick < ShelterReady) // 미리 점검해 둔 대피소 — 사람들이 덜 긴장한다
        {
            foreach (var c in w.Crew) if (!c.Dead && !c.Outside) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
            Say("대피소는 어제 점검해 두었다 — 물 · 마스크 · 담요가 있다", null, 1);
        }
        _stormWas = storm; _showerWas = shower;
        // 약속: 선외 작업 전엔 다시 본다
        if (w.Tick >= _outNext)
        {
            _outNext = w.Tick + SimTime.Minutes(1);
            bool outside = false;
            foreach (var c in w.Crew) if (c.Outside && !c.Dead) { outside = true; break; }
            if (outside && !_outsideWas && Promised("weather") is Promise p && w.Tick - p.LastTest > SimTime.Hours(6))
            {
                bool kept = A.Load < 0.95f && Up;
                if (kept) { var nf = MakeForecast(); TestPromise(p, true, $"사람이 나가기 전에 다시 봤다 ({SkyLine(nf)})"); }
                else TestPromise(p, false, "일이 몰려 나가기 전에 다시 보지 못했다");
            }
            _outsideWas = outside;
        }
        // 채점: 하루가 지난 예보
        while (_skyJudged < Forecasts.Count && w.Tick - Forecasts[_skyJudged].Tick >= SimTime.TicksPerDay) Judge(Forecasts[_skyJudged++]);
    }

    private void Judge(SkyForecast f)
    {
        var w = _w;
        var a = A;
        f.Judged = true;
        f.JudgedAt = w.Tick;
        float err = (f.PStorm - (f.StormCame ? 1f : 0f)) * (f.PStorm - (f.StormCame ? 1f : 0f)) + (f.PShower - (f.ShowerCame ? 1f : 0f)) * (f.PShower - (f.ShowerCame ? 1f : 0f));
        int n = Forecasts.Count(x => x.Judged);
        SkyScore = n <= 1 ? err : SkyScore + (err - SkyScore) / n;
        bool came = f.StormCame || f.ShowerCame;
        string what = f.StormCame ? "태양 폭풍" : f.ShowerCame ? "운석우" : "폭풍";
        float p = f.StormCame ? f.PStorm : f.ShowerCame ? f.PShower : MathF.Max(f.PStorm, f.PShower);
        if (came && f.Held) { f.Hit = true; f.Verdict = $"{Ko.EulReul(what)} 미리 봤다 ({p * 100:0}%) — 선외 일정 · 드론을 미리 정리했다"; }
        else if (!came && !f.Held) { f.Hit = true; f.Verdict = "예보대로 조용했다"; }
        else if (!came && f.Held)
        {
            f.Verdict = $"{what} 예보({p * 100:0}%)는 빗나갔다 — 선외 일정만 미뤘다";
            foreach (var c in w.Crew.Where(c => !c.Dead && w.CrisisCrew.BillRole(c) == StationRole.Eva).OrderBy(c => c.Id))
            {
                a.Trusts.Change(c, -0.01f, "날씨 예보가 빗나가 일만 밀렸다", quiet: true);
                if (R.Chance(0.4f)) Life.Diary(w, c, Persona.Say(c, "폭풍이 온다더니 조용했다. 밖의 일만 하루 밀렸다"));
            }
            if (a.Character.Caution > 0.25f && p < 0.35f) Review("날씨", $"신중하게 낮은 확률({p * 100:0}%)에도 선외 일을 미뤘다 — 헛걱정으로 늦었다");
            a.Character.Nudge(-0.01f, 0f, "날씨 예보가 빗나갔다 (헛걱정)");
        }
        else
        {
            f.Verdict = $"{Ko.EulReul(what)} 못 봤다 ({p * 100:0}%) — 밖의 사람이 서둘러 돌아왔다";
            var heard = w.Crew.Where(c => !c.Dead && !c.IsChild).ToList();
            a.Authority.Learned("날씨", $"{Ko.EulReul(what)} {p * 100:0}%로 낮게 봤는데 왔다");
            if (Promised("weather") == null) MakePromise("weather", "확률이 낮아도 사람이 밖에 나가기 전엔 한 번 더 살피겠다", heard);
            a.Character.Nudge(0.03f, 0f, $"{Ko.EulReul(what)} 못 봤다");
        }
        if (f.Held || came) w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 어제 예보 — {f.Verdict}");
    }
}

public sealed partial class DroneSystem
{
    /// <summary>v16.27 날씨 예보로 드론을 들인다 (급하지 않은 일만 · 몇 대 들였나).</summary>
    internal int WeatherRecall(string why)
    {
        int n = 0;
        foreach (var d in Drones)
        {
            if (d.State is not (DroneState.Outbound or DroneState.Working) || d.Order is WorkOrder o && o.Urgency >= 0.8f) continue;
            if (d.Order is WorkOrder oo) { oo.Drone = null; d.Order = null; }
            GoHome(d);
            d.Doing = why;
            n++;
        }
        if (n > 0) _world.Log.Add(_world.Tick, LogKind.Ship, $"드론 {n}대 복귀 — {why}");
        return n;
    }

    /// <summary>v16.27 날씨 예보 중엔 급하지 않은 일로 나가지 않는다 (Decide 훅).</summary>
    internal bool SkyHold(Drone d) => _world.Automation.MateOrNull is ShipMate m && m.EvaHold && !_world.Hazards.StormActive && _world.Board.Open.All(o => o.Urgency < 0.8f || !CanDo(d.Kind, o.Kind));
}
