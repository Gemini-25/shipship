using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 행성 날씨: 땅에 있는 동안 날씨가 바뀌고 실제로 망가뜨린다.
//   번개 → 바깥 설비 · 회로 · 밖의 드론 · 사람 / 강풍 · 태풍 → 태양 날개 · 안테나 · 드론 · 외벽 / 산성비 · 바다 소금 → 외벽 부식(용접 일감)
//   모래 폭풍 · 유독 먼지 → 태양 빛 · 필터 막힘 / 폭염 · 냉기 · 눈보라 → 선체 온도(공조가 힘겹다) / 지진 · 유성 비 · 정전기 · 자기장 없음(방사선).
//   주컴퓨터는 몇 시간 앞을 예보하고, 사나운 날씨가 오면 드론을 불러들이고 밖 일을 멈춘다. 임무가 거의 끝났으면 일찍 뜨고, 이미 폭풍 속이면 버틴다.

public sealed partial class PlanetSystem
{
    public PlanetWeather Weather { get; private set; }
    public float WxPower { get; private set; }
    public long WxUntil { get; private set; }
    public PlanetWeather NextWx { get; private set; }
    public float NextWxPower { get; private set; }
    /// <summary>주컴퓨터가 다음 날씨를 미리 알린 때 (-1: 아직).</summary>
    public long Forecasted { get; private set; } = -1;
    /// <summary>하루 길이 (시간 · 0이면 조석 고정 — 늘 낮 · 늘 밤의 경계).</summary>
    public float DayHours { get; private set; }
    public float OutsideC { get; private set; } = 6f;
    /// <summary>사나운 날씨를 피해 밖 일을 멈추고 드론을 불러들인 상태.</summary>
    public bool Sheltering { get; private set; }
    public bool Hunkered { get; private set; }
    public long NextQuake { get; private set; } = -1;
    public Dictionary<string, int> Harm { get; } = new();
    public int Strikes { get; private set; }
    private Rng _wx = new(1);

    public static bool Severe(PlanetWeather w) => w is PlanetWeather.Thunder or PlanetWeather.Typhoon or PlanetWeather.Blizzard or PlanetWeather.DustStorm or PlanetWeather.AcidRain or PlanetWeather.HeatWave;
    private static bool Windy(PlanetWeather w) => w is PlanetWeather.Wind or PlanetWeather.Typhoon or PlanetWeather.Blizzard or PlanetWeather.DustStorm;

    /// <summary>바람 세기 0~1 (태풍이 가장 세다).</summary>
    public float Wind => !Landed ? 0f : Weather switch
    {
        PlanetWeather.Typhoon => 0.6f + 0.4f * WxPower, PlanetWeather.Wind => 0.3f + 0.4f * WxPower, PlanetWeather.Blizzard or PlanetWeather.DustStorm => 0.3f + 0.5f * WxPower,
        PlanetWeather.Thunder => 0.2f * WxPower, _ => 0f,
    };

    /// <summary>선체 바깥 온도가 끌어당기는 선체 온도 (열 시스템이 우주의 6°C 대신 쓴다).</summary>
    public float ShellC
    {
        get
        {
            if (!Landed || Here == null) return Atmosphere.ShellC;
            float k = Here.Pressure < 1f ? 0.12f : 0.2f + MathF.Min(0.1f, Here.Pressure / 3000f); // 선체 단열이 바깥 기온을 많이 걸러 낸다
            return Math.Clamp(Atmosphere.ShellC + (OutsideC - Atmosphere.ShellC) * k, -60f, 75f);
        }
    }

    /// <summary>외벽으로 드나드는 열 배율 (대기가 있으면 바람이 열을 실어 나른다).</summary>
    public float HullHeatMul => !Landed || Here == null || Here.Pressure < 1f ? 1f : 1f + MathF.Min(0.3f, Here.Pressure / 400f) + 0.5f * Wind;

    /// <summary>태양 날개에 드는 빛 배율 (밤 · 모래 폭풍 · 눈보라 · 두꺼운 대기).</summary>
    public float SolarMul
    {
        get
        {
            if (!Landed || Here == null) return 1f;
            float day = DayHours <= 0f ? 0.5f : MathF.Max(0f, MathF.Sin(MathF.Tau * Phase01()));
            float sky = Here.Pressure < 1f ? 1.2f : Here.Pressure > 1000f ? 0.15f : 1f - MathF.Min(0.5f, Here.Pressure / 600f);
            float wx = Weather switch { PlanetWeather.DustStorm => 1f - 0.8f * WxPower, PlanetWeather.Blizzard => 0.3f, PlanetWeather.Thunder or PlanetWeather.Rain or PlanetWeather.AcidRain or PlanetWeather.Typhoon => 0.4f, _ => 1f };
            return day * sky * wx;
        }
    }

    /// <summary>바깥 벽에 닿은 방의 방사선 (자기장 없는 행성).</summary>
    public float RoomRad(Room r) => !Landed || Here == null || !Here.Has(PlanetFeature.NoMagnet) ? 0f : r.Cells.Any(c => Cell.Dirs4.Any(d => _w.Ship.WallAt(c + d) is WallState wl && wl.IsHull)) ? 0.14f : 0.04f;
    public float OutsideRad => !Landed || Here == null ? 0f : Here.Has(PlanetFeature.NoMagnet) ? 0.6f : Here.Pressure < 1f ? 0.25f : 0.02f;

    private float Phase01() => DayHours <= 0f ? 0.25f : (_w.Tick - PhaseSince) / (float)SimTime.TicksPerHour / DayHours % 1f;

    private void StartWeather(Planet p)
    {
        var w = _w;
        _wx = new Rng(unchecked(w.Seed * 9431 + p.Seed));
        DayHours = p.Has(PlanetFeature.TidalLock) ? 0f : _wx.Range(10f, 40f);
        Harm.Clear();
        Strikes = 0;
        Sheltering = Hunkered = false;
        Weather = PlanetWeather.Calm;
        WxPower = 0f;
        WxUntil = w.Tick + SimTime.Hours(_wx.Range(2f, 6f));
        PickNext();
        NextQuake = p.Has(PlanetFeature.Quakes) ? w.Tick + SimTime.Hours(_wx.Range(4f, 14f)) : -1;
        OutsideC = Outside();
    }

    private void PickNext()
    {
        var p = Here!;
        NextWx = p.Weathers[_wx.Range(0, p.Weathers.Count)];
        float h = Math.Clamp((Storyteller.Level - 1) / 7f, 0f, 1f);
        NextWxPower = NextWx == PlanetWeather.Calm ? 0f : Math.Clamp(_wx.Range(0.3f, 0.9f) + 0.2f * h, 0.2f, 1f);
        Forecasted = -1;
    }

    private float Outside()
    {
        var p = Here!;
        float t = p.TempC + (DayHours <= 0f ? 0f : p.Swing * 0.5f * MathF.Sin(MathF.Tau * Phase01()));
        t += Weather switch { PlanetWeather.HeatWave => (15f + 0.25f * MathF.Abs(p.TempC)) * WxPower, PlanetWeather.Blizzard => -15f * WxPower, PlanetWeather.Rain => -3f, _ => 0f };
        return t;
    }

    /// <summary>예보 시간 (시간): 안테나 · 센서 · 기상탑이 좋으면 길다.</summary>
    public float ForecastLead => Math.Clamp(1.5f + 2.5f * Sensor * (_w.Automation.CoreOnline ? 1f : 0.4f) + TowerLead(), 0.5f, 6f);

    private void Count(string what, int n = 1) => Harm[what] = Harm.GetValueOrDefault(what) + n;

    private void UpdateWeather(float dt)
    {
        var w = _w;
        var p = Here!;
        // 날씨 바뀜
        if (w.Tick >= WxUntil)
        {
            var was = Weather;
            Weather = NextWx;
            WxPower = NextWxPower;
            WxUntil = w.Tick + SimTime.Hours(Weather == PlanetWeather.Calm ? _wx.Range(3f, 10f) : _wx.Range(2f, 7f));
            PickNext();
            if (Weather != was)
            {
                string line = Weather == PlanetWeather.Calm ? $"{p.Name} — 날씨가 갰다" : $"{p.Name} — {PlanetGen.WeatherName(Weather)} (세기 {WxPower * 100:0}%)";
                w.Log.Add(w.Tick, LogKind.Ship, line);
                if (Severe(Weather) && WxPower >= 0.5f) w.RaiseAlert(line, null, AlertLevel.Warning, shipWide: true);
            }
        }
        OutsideC = Outside();
        // 예보: 사나운 날씨가 리드 시간 안이면 알리고 대비한다
        float toNext = (WxUntil - w.Tick) / (float)SimTime.TicksPerHour;
        if (Forecasted < 0 && Severe(NextWx) && NextWxPower >= 0.4f && toNext <= ForecastLead)
        {
            Forecasted = w.Tick;
            string fc = $"예보 — {toNext:0.0}시간 뒤 {PlanetGen.WeatherName(NextWx)} (세기 {NextWxPower * 100:0}%)";
            Note(fc);
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {fc} — 드론을 불러들이고 밖 일을 멈춘다");
            w.RaiseAlert($"{p.Name} {fc}", null, AlertLevel.Notice, shipWide: true);
        }
        bool wasShelter = Sheltering;
        // 바람 폭풍은 35%부터 · 뇌우 · 산성비 · 폭염은 60%부터 밖 일을 멈춘다 (드론이 날아갈 수는 있다)
        static float Gate(PlanetWeather x) => Windy(x) ? 0.35f : 0.6f;
        Sheltering = Severe(Weather) && WxPower >= Gate(Weather) || Forecasted >= 0 && Severe(NextWx) && NextWxPower >= Gate(NextWx) && toNext <= ForecastLead;
        if (Sheltering) Shelter();
        else if (wasShelter) { Hunkered = false; w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {p.Name} 날씨가 누그러졌다 — 밖 일을 다시 연다"); }

        Damage(dt);
    }

    /// <summary>대피: 밖의 드론을 불러들이고 밖에 나간 사람을 들인다.</summary>
    private void Shelter()
    {
        var w = _w;
        foreach (var d in w.Drones.Drones.Where(d => d.Outside && d.State != DroneState.Lost && d.State != DroneState.Returning).ToList())
            w.Drones.Recall(d, $"{PlanetGen.WeatherName(Severe(Weather) ? Weather : NextWx)} — 들어온다");
    }

    private void Damage(float dt)
    {
        var w = _w;
        var p = Here!;
        float pw = WxPower;
        var outsideDrones = w.Drones.Drones.Where(d => d.Outside && d.State != DroneState.Lost).ToList();
        var outsideCrew = w.Crew.Where(c => !c.Dead && c.Outside).ToList();
        var exts = w.Exterior.All;
        switch (Weather)
        {
            case PlanetWeather.Thunder:
            {
                float rate = 1.0f * pw * (p.Has(PlanetFeature.Static) ? 1.3f : 1f) * (p.Has(PlanetFeature.StrongMagnet) ? 1.2f : 1f);
                if (_wx.Chance(MathF.Min(0.9f, rate * dt))) Strike(outsideDrones, outsideCrew);
                break;
            }
            case PlanetWeather.Typhoon or PlanetWeather.Wind or PlanetWeather.Blizzard or PlanetWeather.DustStorm:
            {
                float wind = Wind;
                foreach (var f in exts.Where(f => f.Condition > 0f))
                    if (_wx.Chance(MathF.Min(0.6f, wind * wind * 0.25f * dt * (f.Kind == ExtKind.SolarWing ? 1.4f : 1f) * (1f - BarrierShield())))) { w.Exterior.Damage(f, 0.08f + 0.25f * wind, PlanetGen.WeatherName(Weather)); Count("바람에 상한 바깥 설비"); }
                foreach (var d in outsideDrones)
                    if (_wx.Chance(MathF.Min(0.8f, wind * 0.6f * dt))) { w.Drones.HitDrone(d, 0.1f + 0.3f * wind, new Vector2(1f, 0f), PlanetGen.WeatherName(Weather)); Count("바람에 다친 드론"); }
                foreach (var c in outsideCrew)
                    if (_wx.Chance(MathF.Min(0.8f, wind * 0.5f * dt))) { NeedsSystem.AddInjury(c.Vitals, 0.06f + 0.15f * wind, $"{PlanetGen.WeatherName(Weather)}에 휩쓸림"); Count("바람에 다친 사람"); }
                if (Weather == PlanetWeather.Typhoon && _wx.Chance(MathF.Min(0.5f, 0.25f * pw * dt * (1f - BarrierShield())))) { Hull.Damage(w.Ship, WindWall(), 0.05f + 0.1f * pw); Count("태풍에 찌그러진 외벽"); }
                if (Weather == PlanetWeather.DustStorm) Dust(dt, pw * (p.Has(PlanetFeature.ToxicDust) ? 1.5f : 1f));
                if (Weather == PlanetWeather.DustStorm && p.Has(PlanetFeature.Static) && _wx.Chance(0.15f * pw * dt)) Zap("모래 폭풍의 정전기");
                break;
            }
            case PlanetWeather.AcidRain:
                Corrode(dt, 0.9f * pw);
                break;
            case PlanetWeather.HeatWave:
                foreach (var c in outsideCrew) if (_wx.Chance(0.3f * pw * dt)) { NeedsSystem.AddInjury(c.Vitals, 0.05f, "폭염 열사병"); Count("더위 먹은 사람"); }
                break;
        }
        // 바다 · 녹슨별: 늘 조금씩 부식
        if (p.Surface == PlanetSurface.Shallows || p.Key == "rust") Corrode(dt, p.Key == "rust" ? 0.5f : 0.25f);
        if (p.Has(PlanetFeature.ToxicDust) && Weather != PlanetWeather.DustStorm) Dust(dt, 0.15f);
        if (p.Has(PlanetFeature.Static) && _wx.Chance(0.04f * dt)) Zap("정전기");
        if (p.Has(PlanetFeature.MeteorRain) && _wx.Chance(0.12f * dt) && w.Ship.LiveRooms.Any())
        {
            var rooms = w.Ship.LiveRooms.ToList();
            var target = rooms[_wx.Range(0, rooms.Count)].Cells.First();
            w.Sensors.Launch(target, _wx.Range(0.12f, 0.35f));
            Count("유성");
        }
        if (NextQuake >= 0 && w.Tick >= NextQuake) Quake();
        if (p.Has(PlanetFeature.Sinkholes) && _wx.Chance(0.02f * dt))
        {
            var low = w.Ship.LiveRooms.OrderByDescending(r => r.Cells.Max(c => c.Y)).First();
            w.Maneuver.Shock(0.3f, new Vector2(_wx.Chance(0.5f) ? 1f : -1f, 0.3f), low, $"{p.Name} 땅이 꺼졌다", warned: false);
            Count("땅 꺼짐");
            w.Log.Add(w.Tick, LogKind.Ship, $"{p.Name} — 착륙 다리 밑 땅이 꺼져 배가 기울었다");
        }
    }

    /// <summary>번개 한 번: 기상탑 → 바깥 설비 → 외벽 회로 → 밖의 드론 · 사람.</summary>
    private void Strike(List<Drone> drones, List<CrewMember> crew)
    {
        var w = _w;
        Strikes++;
        Count("번개");
        if (TowerCatches()) { w.Log.Add(w.Tick, LogKind.Ship, $"번개 — 기상탑 피뢰침이 받았다"); Count("피뢰침이 받은 번개"); return; }
        float x = _wx.Float();
        if (crew.Count > 0 && x < 0.15f)
        {
            var c = crew[_wx.Range(0, crew.Count)];
            NeedsSystem.AddInjury(c.Vitals, 0.3f + _wx.Range(0f, 0.25f), "번개");
            Count("번개 맞은 사람");
            w.RaiseAlert($"번개 — {Ko.IGa(c.Name)} 밖에서 맞았다", c.Room, AlertLevel.Critical, shipWide: true);
            w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} {Here!.Name}에서 번개를 맞았다", c.Room, new[] { c }, log: true);
            return;
        }
        if (drones.Count > 0 && x < 0.35f)
        {
            var d = drones[_wx.Range(0, drones.Count)];
            w.Drones.HitDrone(d, 0.45f + _wx.Range(0f, 0.3f), new Vector2(0f, 1f), "번개");
            Count("번개 맞은 드론");
            w.Log.Add(w.Tick, LogKind.Ship, $"번개 — 밖에 있던 {Ko.IGa(d.Name)} 맞았다");
            return;
        }
        var ext = w.Exterior.All.Where(f => f.Condition > 0.05f).ToList();
        if (ext.Count > 0 && x < 0.75f)
        {
            var f = ext.OrderBy(f => f.Kind == ExtKind.Antenna ? 0 : 1).ThenBy(_ => _wx.Float()).First(); // 안테나가 가장 높다
            w.Exterior.Damage(f, 0.3f + _wx.Range(0f, 0.35f), "번개");
            Count("번개 맞은 바깥 설비");
            return;
        }
        Zap("번개가 외벽을 타고 들어왔다");
    }

    /// <summary>회로가 튄다: 바깥쪽 방 설비 하나에 단락 · 차단기.</summary>
    private void Zap(string why)
    {
        var w = _w;
        var outer = w.Ship.Machines.Where(m => m.Faults.Count == 0 && m.Body.Room is Room r && !r.Detached && r.Cells.Any(c => Cell.Dirs4.Any(d => w.Ship.WallAt(c + d) is WallState ws && ws.IsHull))).ToList();
        if (outer.Count == 0) return;
        var m = outer[_wx.Range(0, outer.Count)];
        if (w.Machines.Break(m, _wx.Chance(0.5f) ? FaultKind.ShortCircuit : FaultKind.BreakerTrip) != null)
        {
            Count("튄 회로");
            w.Log.Add(w.Tick, LogKind.Ship, $"{why} — {m.Body.Room.Name} {m.Name} 회로가 튀었다");
        }
    }

    /// <summary>부식: 바람 받는 외벽이 조금씩 삭는다 (용접 일감이 된다) · 바깥 설비도.</summary>
    private void Corrode(float dt, float k)
    {
        var w = _w;
        if (_wx.Chance(MathF.Min(0.9f, 0.5f * k * dt))) { Hull.Damage(w.Ship, WindWall(), 0.02f + 0.04f * k); Count("삭은 외벽"); }
        foreach (var f in w.Exterior.All) f.Condition = MathF.Max(0f, f.Condition - 0.004f * k * dt);
    }

    /// <summary>먼지: 공기 필터가 막힌다.</summary>
    private void Dust(float dt, float k)
    {
        var w = _w;
        foreach (var m in w.Ship.Machines.Where(m => m.Spec.FaultKinds.Contains(FaultKind.FilterClogged) && !m.Has(FaultKind.FilterClogged)).ToList())
            if (_wx.Chance(MathF.Min(0.5f, 0.06f * k * dt))) { w.Machines.Break(m, FaultKind.FilterClogged); Count("막힌 필터"); }
    }

    private void Quake()
    {
        var w = _w;
        var p = Here!;
        float g = _wx.Range(0.18f, 0.45f) + 0.03f * Storyteller.Level;
        bool warned = w.Automation.CoreOnline && _wx.Chance(0.55f);
        var rooms = w.Ship.LiveRooms.ToList();
        var room = rooms[_wx.Range(0, rooms.Count)];
        if (warned) w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 땅이 울린다 — 지진이 온다, 몸을 낮춰라");
        w.Maneuver.Shock(g, new Vector2(_wx.Range(-1f, 1f), _wx.Range(-0.3f, 0.3f)), room, $"{p.Name} 지진", warned);
        for (int i = 0; i < 1 + (int)(g * 4); i++) Hull.Damage(w.Ship, WindWall(), 0.03f * g * 4f);
        Count("지진");
        OnQuake(g);
        NextQuake = w.Tick + SimTime.Hours(_wx.Range(8f, 20f) / (1f + 0.1f * Storyteller.Level));
    }

    /// <summary>바람 받는 외벽 한 칸 (배 위 · 옆).</summary>
    private Cell WindWall()
    {
        var hull = _w.Ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).ToList();
        if (hull.Count == 0) return new Cell(0, 0);
        int top = hull.Min(c => c.Y);
        var upper = hull.Where(c => c.Y <= top + 3).ToList();
        var pool = _wx.Chance(0.6f) && upper.Count > 0 ? upper : hull;
        return pool[_wx.Range(0, pool.Count)];
    }

    /// <summary>지금 이륙하면 위험한 날씨 (버틴다).</summary>
    private string? WeatherHold()
    {
        if (Weather is PlanetWeather.Thunder or PlanetWeather.Typhoon or PlanetWeather.Blizzard or PlanetWeather.DustStorm && WxPower >= 0.5f)
            return $"{PlanetGen.WeatherName(Weather)} 속 이륙은 위험하다 — 지나가길 기다린다 ({(WxUntil - _w.Tick) / (float)SimTime.TicksPerHour:0.0}시간)";
        return null;
    }

    /// <summary>일찍 떠날까: 사나운 날씨가 오는데 임무가 거의 끝났거나 배가 많이 상했다 (아직 하늘이 열려 있을 때).</summary>
    private bool WeatherLeaveEarly()
    {
        var w = _w;
        if (Severe(Weather) && WxPower >= 0.5f) return false; // 이미 폭풍 속 — 버틴다
        bool storm = Forecasted >= 0 && Severe(NextWx) && NextWxPower >= 0.7f;
        if (!storm) return false;
        float done = (w.Tick - PhaseSince) / (float)(StayUntil - PhaseSince);
        bool hurt = ShipStrain() >= 0.35f;
        if ((done >= 0.6f || hurt) && CannotLift() == null)
        {
            Decision = $"일찍 뜬다 — {PlanetGen.WeatherName(NextWx)}(세기 {NextWxPower * 100:0}%)가 오기 전에" + (hurt ? " · 배가 이미 많이 상했다" : $" · 임무 {done * 100:0}% 마침");
            Note(Decision);
            w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터가 {Here!.Name}에서 일찍 뜨기로 했다 — {PlanetGen.WeatherName(NextWx)}가 온다", null, log: true);
            return true;
        }
        if (!Hunkered)
        {
            Hunkered = true;
            Decision = $"버틴다 — {PlanetGen.WeatherName(NextWx)}가 오지만 할 일이 남았다 (임무 {done * 100:0}%)";
            Note(Decision);
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {Decision}");
        }
        return false;
    }

    private string WeatherLine()
    {
        if (Harm.Count == 0) return "";
        return string.Join(" · ", Harm.Where(kv => kv.Key != "피뢰침이 받은 번개" || kv.Value > 0).OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} {kv.Value}"));
    }
}

public sealed partial class PlanetSystem
{
    // ── 시험용 손잡이 ──
    public void SetWeather(PlanetWeather wx, float power, float hours) { Weather = wx; WxPower = power; WxUntil = _w.Tick + SimTime.Hours(hours); OutsideC = Outside(); }
    public void SetNext(PlanetWeather wx, float power, float inHours) { NextWx = wx; NextWxPower = power; WxUntil = _w.Tick + SimTime.Hours(inHours); Forecasted = -1; }
    public void SetStay(float daysLeft) => StayUntil = _w.Tick + (long)(daysLeft * SimTime.TicksPerDay);
    public void QuakeNow() => NextQuake = _w.Tick;
}
