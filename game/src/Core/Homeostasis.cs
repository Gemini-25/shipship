using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// 항상성 — 주 컴퓨터가 맡는다.
//  마) 우주 공간도 실감 나게: 선체 바깥 온도가 햇빛 쪽 · 그늘 쪽으로 다르고, 별에 가까운 구간(근일점)은 뜨겁고 먼 구간(깊은 공허 · 얼음 고리)은 차다.
//      구간이 바뀌어도 선체는 몇 시간에 걸쳐 데워지고 식는다. (행성에 내려앉아 있으면 행성 날씨가 정한다.)
//  나) 주 컴퓨터가 지킨다: 앞 구간 예보를 보고 미리 덥히거나 식히고 · 바깥 방 단열 덮개를 내리고 · 덜 쓰는 바깥 방은 난방을 줄이고(닫고)
//      · 사람을 안쪽 방으로 부르고 · 원자로 출력을 올려 난방에 보태고 · 뜨거운 구간에선 햇빛이 사람 없는 쪽으로 오게 배를 돌리고 천천히 굴린다.
//  라) 한 화면: 온도 · 습도 · 이산화탄소 · 기압 · 방사선 — 목표값 · 지금 · 자동 대응 (Readings).

public sealed class HomeoStats
{
    public int Preheats, Precools, Shutters, Closed, Boosts, Turns, Rolls, Calls, Dries, Wets, Vents, LandChecks, LandWarnings, ClosedVisits;
    public float ColdestOccupied = 99f, HottestOccupied = -99f;
    public string ColdestRoom = "", HottestRoom = "";
    public string Line() =>
        $"미리 덥힘 {Preheats} · 미리 식힘 {Precools} · 단열 덮개 {Shutters} · 바깥 방 닫음 {Closed} · 난방 보탬 {Boosts} · 자세 돌림 {Turns} · 굴리기 {Rolls} · 안쪽으로 부름 {Calls}"
        + $" · 제습 {Dries} · 가습 {Wets} · 환기 몰아줌 {Vents} · 착륙 열 점검 {LandChecks}(말림 {LandWarnings}) · 사람 지내는 방 최저 {ColdestOccupied:0.0}℃ · 최고 {HottestOccupied:0.0}℃ · 닫은 방에 들름 {ClosedVisits}";
}

/// <summary>한 화면에 보이는 항상성 한 줄: 무엇 · 지금 · 목표 · 상태 · 자동 대응.</summary>
public readonly record struct HomeoReading(string Name, string Now, string Target, int Level, string Response);

public sealed class Homeostasis
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6151 + 29));
    /// <summary>시험 · 개발용: 우주 공간 온도를 예전처럼 고르게 (선체 6℃) · 주 컴퓨터 항상성 대응을 끈다.</summary>
    public static bool Off;

    // ── 마) 공간 열 환경 ──
    /// <summary>햇빛 세기 (보통 항로 = 1 · 근일점 4 · 깊은 공허 0.05).</summary>
    public float Sun { get; private set; } = 1f;
    /// <summary>햇빛이 오는 쪽 (배 그림 기준 각도, 라디안).</summary>
    public float SunAngle { get; private set; } = 0.6f;
    private float _base = Atmosphere.ShellC, _amp = 5f, _angleTarget = 0.6f;
    private int _leg = -1;
    private long _nextHour;
    private readonly Dictionary<int, float> _facing = new();
    private int _facingKey = -1;

    // ── 나) 대응 상태 ──
    /// <summary>천천히 굴린다 (햇빛 쪽 · 그늘 쪽 차이가 40%로).</summary>
    public bool Rolling { get; private set; }
    /// <summary>난방에 원자로 출력을 보탠다 (난방 몫 배율 · 그만큼 전기가 든다).</summary>
    public float HeatBoost { get; private set; } = 1f;
    /// <summary>미리 덥히기(+) · 식히기(−) 온도 (℃).</summary>
    public float Bias { get; private set; }
    private readonly HashSet<int> _shutters = new(), _closed = new();
    private int _mode; // 0 평온 · 1 추위 대비 · 2 더위 대비
    private long _calmSince = -1;
    private long _calledAt = -1;
    private string _lastPlan = "";
    public HomeoStats Stats { get; } = new();

    public Homeostasis(World w) { _w = w; }

    /// <summary>구간마다 햇빛 세기.</summary>
    public static float SunOf(LegKind k) => k switch
    {
        LegKind.Perihelion => 4f,
        LegKind.SolarWind => 1.6f,
        LegKind.RadiationBelt => 1.2f,
        LegKind.Pulsar => 0.9f,
        LegKind.AsteroidBelt or LegKind.Graveyard or LegKind.SporeCloud => 0.8f,
        LegKind.CometTrail => 0.7f,
        LegKind.Nebula => 0.5f,
        LegKind.GasGiant => 0.25f,
        LegKind.IceRing => 0.2f,
        LegKind.DeepVoid => 0.05f,
        _ => 1f,
    };

    /// <summary>햇빛 세기에 따른 선체 평균 온도 (보통 6℃ · 근일점 22℃ · 깊은 공허 −28℃).</summary>
    public static float BaseOf(float sun) => Math.Clamp(Atmosphere.ShellC + 8f * MathF.Log2(MathF.Max(0.02f, sun)), -35f, 40f);
    /// <summary>햇빛 쪽과 그늘 쪽의 차 (± · 보통 5℃ · 근일점 10℃).</summary>
    public static float AmpOf(float sun) => MathF.Min(12f, 5f * MathF.Sqrt(MathF.Max(0f, sun)));

    /// <summary>이 방 바깥벽의 온도 (행성에 앉아 있으면 행성이 정한다).</summary>
    public float Shell(Room r)
    {
        if (Off) return Atmosphere.ShellC;
        return _base + _amp * (Rolling ? 0.4f : 1f) * Facing(r);
    }

    /// <summary>이 방이 햇빛을 향하는 정도 (−1 그늘 ~ +1 햇빛 · 안쪽 방도 계산은 되지만 외벽이 없으면 쓰이지 않는다).</summary>
    public float Facing(Room r)
    {
        var w = _w;
        int key = w.Ship.Rooms.Count * 31 + (int)(SunAngle * 50f);
        if (key != _facingKey) { _facing.Clear(); _facingKey = key; }
        if (_facing.TryGetValue(r.Id, out var f)) return f;
        var d = r.Center - w.Structure.ShipCenter;
        float len = d.Length();
        f = len < 0.5f ? 0f : Vector2.Dot(d / len, new Vector2(MathF.Cos(SunAngle), MathF.Sin(SunAngle)));
        _facing[r.Id] = f;
        return f;
    }

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        // 구간이 바뀌면 햇빛 세기 · 들어오는 쪽이 바뀐다 (선체는 서너 시간에 걸쳐 따라간다)
        if (_leg != w.Voyage.Index)
        {
            _leg = w.Voyage.Index;
            _angleTarget = R.Range(0f, MathF.PI * 2f);
            if (_leg == 0) SunAngle = _angleTarget;
        }
        Sun = SunOf(w.Voyage.Current.Kind);
        float k = 0.3f; // 한 시간에 30%씩
        _base += (BaseOf(Sun) - _base) * k;
        _amp += (AmpOf(Sun) - _amp) * k;
        float da = MathF.IEEERemainder(_angleTarget - SunAngle, MathF.PI * 2f);
        SunAngle += Math.Clamp(da, -0.5f, 0.5f); // 배를 돌리는 데 시간이 든다 (한 시간에 30도쯤)
        Watch();
        if (w.Automation.Present && w.Automation.MainOnline) Manage();
        else Release("주 컴퓨터가 멎어 항상성 대응을 풀었다");
    }

    // ═══════════════════════ 나) 주 컴퓨터가 지킨다 ═══════════════════════

    private bool Landed => _w.Planet?.Landed == true;

    /// <summary>앞으로 열두 시간 안에 닥칠 가장 찬 · 가장 뜨거운 선체 온도와 그 까닭.</summary>
    public (float cold, float hot, string why, float hours) Forecast()
    {
        var w = _w;
        if (Landed) { float sh = w.Planet.ShellC; return (sh, sh, w.Planet.Here?.Name ?? "행성", 0f); } // 행성: 지금 날씨의 선체 온도
        float cold = _base - _amp, hot = _base + _amp;
        string why = VoyageSystem.KindName(w.Voyage.Current.Kind);
        float hours = 0f;
        if (!Landed && w.Voyage.Index + 1 < w.Voyage.Legs.Count)
        {
            var next = w.Voyage.Legs[w.Voyage.Index + 1];
            float left = (w.Voyage.Current.Days - w.Voyage.Progress) * 24f;
            if (left <= 12f)
            {
                float s = SunOf(next.Kind);
                float nc = BaseOf(s) - AmpOf(s), nh = BaseOf(s) + AmpOf(s);
                if (nc < cold) { cold = nc; why = VoyageSystem.KindName(next.Kind); hours = left; }
                if (nh > hot) { hot = nh; why = VoyageSystem.KindName(next.Kind); hours = left; }
            }
        }
        return (cold, hot, why, hours);
    }

    private void Manage()
    {
        var w = _w;
        var (cold, hot, why, hours) = Forecast();
        int mode = cold < -6f ? 1 : hot > 26f ? 2 : 0;
        if (mode == 0)
        {
            if (_mode != 0)
            {
                if (_calmSince < 0) _calmSince = w.Tick;
                if (w.Tick - _calmSince >= SimTime.Hours(3)) Release($"{why} — 선체 온도가 돌아왔다");
            }
            return;
        }
        _calmSince = -1;
        if (mode != _mode) { if (_mode != 0) Release(""); _mode = mode; }
        var occupied = w.Ship.LiveRooms.Where(r => w.Crew.Any(c => !c.Dead && !c.Outside && c.Room == r)).ToList();
        float coldest = occupied.Count == 0 ? 21f : occupied.Min(r => r.Air.Temperature);
        float hottest = occupied.Count == 0 ? 21f : occupied.Max(r => r.Air.Temperature);
        var plan = new List<string>();
        if (mode == 1)
        {
            // ① 미리 덥힌다 (선체 · 벽 · 가구에 열을 쟁여 둔다)
            if (Bias <= 0f) { Bias = 2f; Stats.Preheats++; plan.Add("미리 2℃ 덥힌다"); }
            // ② 바깥 방 단열 덮개: 빠지는 열이 드는 열보다 많은 외벽 방 (기계 열이 많은 방은 갇히니 빼고)
            foreach (var r in w.Ship.LiveRooms)
            {
                if (r.Insulated || _shutters.Contains(r.Id) || w.Air.HullConductance(r) <= 0f) continue;
                float loss = w.Air.HullConductance(r) * (Atmosphere.Setpoint(r) - (Landed ? w.Planet.ShellC : Shell(r)));
                if (loss <= r.HeatIn * 0.8f || r.Type is RoomType.Reactor or RoomType.ServerRoom) continue;
                r.Insulated = true;
                _shutters.Add(r.Id);
                Stats.Shutters++;
            }
            if (_shutters.Count > 0 && !plan.Any(p => p.Contains("덮개"))) plan.Add($"바깥 방 {_shutters.Count}곳 단열 덮개를 내린다");
            // ③ 사람 없는 바깥 방은 난방을 낮춘다 (닫는다) — 열을 사람 있는 안쪽 방으로
            if (cold < -15f)
                foreach (var r in w.Ship.LiveRooms)
                {
                    if (_closed.Contains(r.Id) || w.Air.HullConductance(r) <= 0f || occupied.Contains(r) || !Closable(r)) continue;
                    r.HomeoOffset = -9f;
                    _closed.Add(r.Id);
                    Stats.Closed++;
                }
            // ④ 사람 있는 방이 식으면: 원자로 출력을 올려 난방에 보태고 · 안쪽 방으로 부른다
            if (coldest < 17f && HeatBoost < 1.5f) { HeatBoost = 1.6f; Stats.Boosts++; plan.Add("원자로 출력을 올려 난방에 보탠다"); }
            if (coldest < 14f && (_calledAt < 0 || w.Tick - _calledAt > SimTime.Hours(6)))
            {
                _calledAt = w.Tick;
                Stats.Calls++;
                plan.Add("안쪽 방으로 모이도록 부른다");
            }
            // ⑤ 그나마 남은 햇빛을 사람 많은 쪽으로 (우주에서만)
            if (Sun >= 0.15f && !Landed) Turn(occupied, warm: true, plan);
        }
        else
        {
            if (Bias >= 0f) { Bias = -2f; Stats.Precools++; plan.Add("미리 2℃ 식힌다"); }
            // 햇빛이 사람 없는 쪽으로 오게 배를 돌리고 · 아주 뜨거우면 천천히 굴린다 (우주에서만)
            if (!Landed) Turn(occupied, warm: false, plan);
            if (hot > 34f && !Rolling && !Landed) { Rolling = true; Stats.Rolls++; plan.Add("배를 천천히 굴려 햇빛을 고르게 받는다"); }
            // 햇빛 쪽 바깥 방에 덮개 (들어오는 열을 막는다)
            foreach (var r in w.Ship.LiveRooms)
            {
                if (r.Insulated || _shutters.Contains(r.Id) || w.Air.HullConductance(r) <= 0f || !Landed && Facing(r) < 0.3f) continue;
                r.Insulated = true;
                _shutters.Add(r.Id);
                Stats.Shutters++;
            }
            if (hottest > 27f && HeatBoost < 1.5f) { HeatBoost = 1.6f; Stats.Boosts++; plan.Add("냉방에 전기를 더 돌린다"); }
        }
        // 닫은 방에 사람이 머물면 다시 연다 (지나가는 게 아니라 일하거나 쉬는 사람)
        foreach (var r in occupied) if (_closed.Remove(r.Id)) r.HomeoOffset = Bias;
        foreach (var r in w.Ship.LiveRooms) if (!_closed.Contains(r.Id)) r.HomeoOffset = Bias;
        if (plan.Count == 0) return;
        string text = $"{why}{(hours > 0.5f ? $"이 {hours:0}시간 뒤" : "")} — 선체가 {(mode == 1 ? $"{cold:0}℃까지 식는다" : $"{hot:0}℃까지 달아오른다")}: {string.Join(" · ", plan)}";
        if (text == _lastPlan) return;
        _lastPlan = text;
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {text}");
        if (plan.Any(p => p.Contains("모이도록"))) w.Automation.Speak.Announce($"{why} 때문에 바깥 방이 춥습니다 — 쉴 때는 안쪽 방으로 모여 주세요", null, 2);
        else if (plan.Count >= 2) w.Automation.Speak.Announce(text, null, 1);
    }

    /// <summary>추위에 닫아 둘 만한 방: 덜 쓰는 창고 · 화물칸 · 전망실 · 체력단련실 · 오락실 같은 곳 (일하러 오는 방 · 침실 · 식당은 닫지 않는다).</summary>
    private static bool Closable(Room r) => r.Kind is RoomType.Storage or RoomType.Cargo or RoomType.Observatory or RoomType.SeedVault or RoomType.DockingBay;
    // 재활용실 · 파쇄실 · 연구실은 정제 · 실험하러 사람이 온다 · 오락실 · 명상실 · 예배실 · 극장 · 체육관은 지친 사람이 쉬러 간다 (추운 구간일수록 — 닫아 차게 두면 발길이 끊긴다)

    /// <summary>배의 자세: 햇빛이 오는 쪽을 고른다 (따뜻하게 = 사람 많은 쪽으로 · 시원하게 = 사람 없는 쪽으로).</summary>
    private void Turn(List<Room> occupied, bool warm, List<string> plan)
    {
        var w = _w;
        float best = float.MinValue, pick = _angleTarget;
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI / 4f;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            float s = 0f;
            foreach (var r in w.Ship.LiveRooms)
            {
                float g = w.Air.HullConductance(r);
                if (g <= 0f) continue;
                var d = r.Center - w.Structure.ShipCenter;
                if (d.LengthSquared() < 0.25f) continue;
                float f = Vector2.Dot(Vector2.Normalize(d), dir);
                float people = w.Crew.Count(c => !c.Dead && !c.Outside && c.Room == r) + (occupied.Contains(r) ? 0.5f : 0f) + (Essentials.Of(r.Kind) == Essential.Vital ? 1f : 0f);
                s += g * f * people * (warm ? 1f : -1f);
            }
            if (s > best + 0.001f) { best = s; pick = a; }
        }
        if (MathF.Abs(MathF.IEEERemainder(pick - _angleTarget, MathF.PI * 2f)) < 0.3f) return;
        _angleTarget = pick;
        Stats.Turns++;
        plan.Add(warm ? "남은 햇빛이 사람 많은 쪽으로 오게 배를 돌린다" : "햇빛이 창고 · 빈 방 쪽으로 오게 배를 돌린다");
    }

    /// <summary>대응을 푼다: 덮개를 올리고 · 닫은 방을 열고 · 난방 보탬 · 미리 덥힘을 거둔다.</summary>
    private void Release(string why)
    {
        var w = _w;
        if (_mode == 0 && _shutters.Count == 0 && _closed.Count == 0 && HeatBoost == 1f && Bias == 0f && !Rolling) return;
        foreach (var id in _shutters) if (id < w.Ship.Rooms.Count) w.Ship.Rooms[id].Insulated = false;
        foreach (var r in w.Ship.Rooms) r.HomeoOffset = 0f;
        _shutters.Clear();
        _closed.Clear();
        HeatBoost = 1f;
        Bias = 0f;
        Rolling = false;
        _mode = 0;
        _calmSince = -1;
        _lastPlan = "";
        if (why.Length > 0) w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {why} — 단열 덮개를 올리고 난방 · 냉방을 평소대로 돌린다");
    }

    /// <summary>사람 있는 방의 최저 · 최고 온도를 적어 둔다 (점검용).</summary>
    private void Watch()
    {
        var w = _w;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (!w.Crew.Any(c => !c.Dead && !c.Outside && c.Room == r)) continue;
            if (_closed.Contains(r.Id)) { Stats.ClosedVisits++; continue; } // 닫아 둔 창고에 잠깐 들른 사람 (그 방은 일부러 차게 둔다)
            if (r.Air.Temperature < Stats.ColdestOccupied) { Stats.ColdestOccupied = r.Air.Temperature; Stats.ColdestRoom = $"{r.Name}({r.Type}{(r.Insulated ? " 덮개" : "")}{(_closed.Contains(r.Id) ? " 닫음" : "")} 열 {r.HeatIn:0.0}kW · 외벽 {w.Air.HullConductance(r):0.00} · 전기 {r.Powered})"; }
            if (r.Air.Temperature > Stats.HottestOccupied) { Stats.HottestOccupied = r.Air.Temperature; Stats.HottestRoom = r.Name; }
        }
    }

    /// <summary>착륙 열 점검: 그 행성의 가장 찬 · 가장 뜨거운 때를 버틸 수 있나 — 단열 덮개를 다 내리고 원자로 출력을 보탠다고 치고 (방마다 · 사람 있는 방 기준).</summary>
    public (bool ok, string note) CanHold(Planet p)
    {
        var w = _w;
        Stats.LandChecks++;
        float k = p.Pressure < 1f ? 0.12f : 0.2f + MathF.Min(0.1f, p.Pressure / 3000f); // PlanetWeatherSim.ShellC 와 같은 셈
        float mul = p.Pressure < 1f ? 1f : 1f + MathF.Min(0.3f, p.Pressure / 400f) + 0.25f; // 바람은 어림으로
        float lo = Math.Clamp(Atmosphere.ShellC + (p.TempC - p.Swing * 0.5f - Atmosphere.ShellC) * k, -60f, 75f);
        float hi = Math.Clamp(Atmosphere.ShellC + (p.TempC + p.Swing * 0.5f - Atmosphere.ShellC) * k, -60f, 75f);
        int bad = 0, rooms = 0;
        float worst = 0f, kw = 0f;
        foreach (var r in w.Ship.LiveRooms)
        {
            float g = w.Air.HullConductance(r);
            if (g <= 0f || r.Type == RoomType.Corridor) continue;
            rooms++;
            float can = Atmosphere.HvacPerCell * Math.Max(4, r.Volume) * 1.6f;
            float cold = g * 0.35f * mul * (Atmosphere.Setpoint(r) - lo) - r.HeatIn; // 덮개를 내리고도 빠지는 열 − 방에 드는 열
            float hot = g * 0.35f * mul * (hi - Atmosphere.Setpoint(r)) + r.HeatIn;
            float over = MathF.Max(cold, hot) - can;
            if (over > 0f) { bad++; worst = MathF.Max(worst, over); }
            // 전기: 우주 순항(선체 6℃)보다 더 드는 몫만 — 더 식히는 열은 방열판으로 버리는 값(냉각기 3kW에 1kW) · 더 덥히는 열은 전기 그대로
            float set = Atmosphere.Setpoint(r), now = g * (set - Atmosphere.ShellC);
            float moreHeat = g * 0.35f * mul * (set - lo) - now, moreCool = g * 0.35f * mul * (hi - set) + now;
            kw += hi > set ? MathF.Max(0f, moreCool) / Atmosphere.CoolCop : MathF.Max(0f, moreHeat);
        }
        float spare = MathF.Max(0f, w.Power.ReactorRated - w.Power.FullDemand);
        bool power = kw <= spare;
        bool ok = (rooms == 0 || bad <= rooms / 4) && power; // 바깥 방 넷에 하나까지는 닫고 버틴다 · 원자로가 그 전기를 대야 한다
        string note = ok
            ? $"선체 {lo:0}~{hi:0}℃ — 단열 덮개 · 난방 보탬으로 버틴다" + (bad > 0 ? $" (바깥 방 {bad}곳은 닫는다)" : "")
            : !power ? $"선체 {lo:0}~{hi:0}℃ — 공조에 전기 {kw:0}kW가 더 드는데 원자로 여유는 {spare:0}kW뿐 (방이 {(hi > 30f && hi - 21f > 21f - lo ? "달아오른다" : "얼어붙는다")})"
            : $"선체 {lo:0}~{hi:0}℃ — 덮개를 다 내리고 원자로를 올려도 바깥 방 {bad}/{rooms}곳이 {(hi > 30f && hi - 21f > 21f - lo ? "달아오른다" : "얼어붙는다")}";
        if (!ok) Stats.LandWarnings++;
        return (ok, note);
    }

    // ═══════════════════════ 라) 한 화면 ═══════════════════════

    /// <summary>온도 · 습도 · 이산화탄소 · 기압 · 방사선: 사람 있는 방 가운데 가장 나쁜 값 · 목표 · 지금 하는 대응.</summary>
    public List<HomeoReading> Readings()
    {
        var w = _w;
        var rooms = w.Ship.LiveRooms.Where(r => w.Crew.Any(c => !c.Dead && !c.Outside && c.Room == r)).ToList();
        if (rooms.Count == 0) rooms = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).ToList();
        var list = new List<HomeoReading>();
        if (rooms.Count == 0) return list;
        // 온도
        var cold = rooms.OrderBy(r => r.Air.Temperature).First();
        var hot = rooms.OrderByDescending(r => r.Air.Temperature).First();
        float dev = MathF.Max(19f - cold.Air.Temperature, hot.Air.Temperature - 25f);
        string tNow = $"{cold.Air.Temperature:0}~{hot.Air.Temperature:0}℃";
        string tResp = _mode == 1 ? $"추위 대비 — 미리 덥힘 · 덮개 {_shutters.Count}곳{(_closed.Count > 0 ? $" · 바깥 방 {_closed.Count}곳 닫음" : "")}{(HeatBoost > 1f ? " · 원자로 출력 보탬" : "")}"
            : _mode == 2 ? $"더위 대비 — 미리 식힘 · 햇빛을 빈 쪽으로{(Rolling ? " · 천천히 굴림" : "")} · 덮개 {_shutters.Count}곳"
            : Landed ? "행성 날씨에 맞춘다" : $"공조가 맞춘다 · 선체 {_base - _amp:0}~{_base + _amp:0}℃ ({VoyageSystem.KindName(w.Voyage.Current.Kind)})";
        list.Add(new("온도", tNow, "19~25℃", dev > 6f ? 2 : dev > 0f ? 1 : 0, tResp));
        // 습도
        var wet = rooms.OrderByDescending(r => r.Humidity - HumidBand(r).hi).First();
        var dry = rooms.OrderBy(r => r.Humidity - HumidBand(r).lo).First();
        float over = wet.Humidity - HumidBand(wet).hi, under = HumidBand(dry).lo - dry.Humidity;
        int hl = over > 0.15f || under > 0.1f ? 2 : over > 0f || under > 0f ? 1 : 0;
        list.Add(new("습도", $"{rooms.Min(r => r.Humidity) * 100:0}~{rooms.Max(r => r.Humidity) * 100:0}%", "30~60% (재배실 80%까지)", hl,
            over > 0f ? $"{wet.Name} 제습 운전" : under > 0f ? $"{dry.Name} 가습" : "환기가 맞춘다"));
        // 이산화탄소
        var stuffy = rooms.OrderByDescending(r => r.Air.CO2).First();
        float co2 = stuffy.Air.CO2;
        list.Add(new("이산화탄소", $"{co2:0.00}kPa", "0.5kPa 아래", co2 > 2f ? 2 : co2 > 0.8f ? 1 : 0,
            co2 > 0.8f ? $"{stuffy.Name}에 세정을 몰아 준다" : $"세정 {w.Air.CO2Scrubbed / Atmosphere.BreathCO2:0.0}사람 몫"));
        // 기압
        var thin = rooms.OrderBy(r => r.Air.Pressure).First();
        float p = thin.Air.Pressure;
        list.Add(new("기압", $"{p:0}kPa", "95~105kPa", p < 80f ? 2 : p < 95f ? 1 : 0,
            thin.Air.Leak > 0f ? $"{thin.Name} 새는 곳을 막는다" : p < 95f ? $"공기 탱크에서 채운다 ({w.Air.Reserve / w.Air.ReserveCapacity * 100:0}%)" : "밀폐 · 정상"));
        // 방사선
        var rad = rooms.OrderByDescending(r => r.Radiation).First();
        float rv = rad.Radiation;
        bool storm = w.Ambience.StormPower >= 0.3f;
        list.Add(new("방사선", rv < 0.05f ? "잔잔함" : rv < 0.15f ? $"약함 ({rv * 100:0})" : rv < 0.4f ? $"높음 ({rv * 100:0})" : $"위험 ({rv * 100:0})", "잔잔함", rv > 0.4f ? 2 : rv > 0.15f || storm ? 1 : 0,
            storm ? "폭풍 — 대피소 · 바깥 방 피하기" : rv > 0.15f ? $"{rad.Name}을 피한다" : "차폐 · 정상"));
        return list;
    }

    // ═══════════════════════ 라) 자동 대응: 습도 · 이산화탄소 ═══════════════════════

    /// <summary>제습 · 가습 운전: 사람 있는 방이 너무 습하거나 메마르면 공조가 맞춘다 (방마다 전기 0.3kW).</summary>
    public void Moist(float dtHours)
    {
        var w = _w;
        if (Off || !w.Automation.Present || !w.Automation.MainOnline) return;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (!r.Powered || !Atmosphere.Vented(r)) continue;
            var (lo, hi) = HumidBand(r); // 방마다 평소 습도를 기준으로 (재배실은 원래 68% — 그걸 말리지는 않는다)
            if (r.Humidity > hi) { r.Humidity = MathF.Max(hi - 0.05f, r.Humidity - 0.08f * dtHours); r.HvacKw += 0.3f; if (_dryRooms.Add(r.Id)) Stats.Dries++; }
            else if (r.Humidity < lo) { r.Humidity = MathF.Min(lo + 0.05f, r.Humidity + 0.05f * dtHours); r.HvacKw += 0.2f; if (_wetRooms.Add(r.Id)) Stats.Wets++; }
            else { _dryRooms.Remove(r.Id); _wetRooms.Remove(r.Id); }
        }
    }
    private readonly HashSet<int> _dryRooms = new(), _wetRooms = new();
    /// <summary>그 방이 맞추는 습도 띠 (평소 습도 −10% ~ +12% · 적어도 25~65%).</summary>
    public static (float lo, float hi) HumidBand(Room r)
    {
        float b = MoistureSystem.BaseHumidity(r);
        return (MathF.Min(0.25f, b - 0.1f), MathF.Max(0.65f, b + 0.12f));
    }

    /// <summary>이산화탄소가 짙은 방(0.8kPa 넘게)에 세정 몫을 몰아 준다 (같은 몫 안에서 세 배 먼저).</summary>
    public float ScrubWeight(Room r)
    {
        if (Off || r.Air.CO2 <= 0.8f || !_w.Automation.Present || !_w.Automation.MainOnline) return 1f;
        if (_ventRooms.Add(r.Id)) Stats.Vents++;
        return 3f;
    }
    private readonly HashSet<int> _ventRooms = new();

    public void Hash(Action<long> I, Action<float> F)
    {
        F(_base); F(_amp); F(SunAngle); F(Bias); F(HeatBoost); I(_mode); I(_shutters.Count); I(_closed.Count);
    }
}
