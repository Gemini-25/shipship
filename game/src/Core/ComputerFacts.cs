using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v19 정보 경계: 컴퓨터가 아는 것은 모두 "값 · 출처 · 확인 시각 · 믿음" 한 묶음이다.
//  출처는 감지기 · 계기(유량계) · 사람의 보고. 오래될수록 믿음이 옅어지고, 옅어진 값은 "모름"이다.
//  모르면 먼저 다시 읽어 보고(확인), 그래도 안 되면 사람에게 묻는다 (방 안에 있으면 손목 단말로 · 없으면 가서 봐 달라고).
//  값끼리 어긋나면 (급수 유량은 정상인데 바닥이 젖었다) 까닭을 여러 갈래로 의심하고 사람에게 확인을 부탁해 진단을 고친다.
//  틀린 계기는 덜 믿고 교정을 건다 · 맞힌 갈래와 틀린 갈래는 다음 짐작에 남는다.
//  컴퓨터는 세계를 직접 보지 않는다: 급수관 스밈(작은 누수)은 교정된 유량계가 잡거나 사람이 봐야 안다.

public enum FactFrom : byte { None, Sensor, Meter, Crew, Robot }

public enum FactKind : byte { People, Fire, O2, Pressure, Temp, Wet, Flow }

public sealed class ShipFact
{
    public float Value { get; set; }
    public FactFrom From { get; set; }
    public int By { get; set; } = -1;
    public long At { get; set; } = -1;
    /// <summary>값을 낸 쪽을 그때 얼마나 믿었나 (감지기 신뢰 · 계기 교정 짐작 · 사람은 1).</summary>
    public float Trust { get; set; } = 1f;
}

public sealed class FactDoubt
{
    public int Id { get; init; }
    public int RoomId { get; init; }
    /// <summary>wet = 값이 어긋남 (바닥이 젖었는데 까닭을 모른다) · stale = 모름 (값이 오래 멈췄다 · 소식이 끊겼다).</summary>
    public string Kind { get; init; } = "wet";
    public long Since { get; init; }
    public string Symptom { get; init; } = "";
    public List<ProbeHypo> H { get; } = new();
    public int Asked { get; set; } = -1;
    public long AskedAt { get; set; } = -1;
    /// <summary>의심 · 확인 중 · 맞음 · 고침 · 모름.</summary>
    public string State { get; set; } = "의심";
    public string Truth { get; set; } = "";
    public string Finding { get; set; } = "";
    public long Closed { get; set; } = -1;
    public int Tries { get; set; }
}

public sealed class ComputerFacts
{
    private readonly World _w;
    private readonly List<ShipFact[]> _rooms = new();
    public List<FactDoubt> Open { get; } = new();
    public List<FactDoubt> Done { get; } = new();
    private int _ids;
    private long _next;
    private readonly Dictionary<int, long> _calm = new(); // 방 → 이때까지 다시 의심하지 않는다
    private readonly HashSet<int> _seepKnown = new();
    private readonly Dictionary<int, long> _meterDoubt = new(); // 방 → 유량계가 틀렸다고 드러난 때 (교정하면 걷는다)
    private readonly Dictionary<int, float> _wetTrust = new();
    /// <summary>갈래마다 맞힌 수 · 짚은 수 (다음 짐작의 무게).</summary>
    public Dictionary<string, (int hit, int tries)> Lessons { get; } = new();
    public int Asks, Answers, Rights, Fixes, SeepsSeen, MeterFound, StaleAsks, WristAnswers;

    public const float UnknownBelow = 0.35f;

    public ComputerFacts(World w) => _w = w;

    private static float Life(FactKind k) => k switch
    {
        FactKind.People => 0.5f, FactKind.Fire => 0.25f, FactKind.O2 => 3f, FactKind.Pressure => 1f,
        FactKind.Temp => 4f, FactKind.Wet => 2f, _ => 3f,
    };

    public static string Name(FactKind k) => k switch
    {
        FactKind.People => "사람", FactKind.Fire => "불", FactKind.O2 => "산소", FactKind.Pressure => "기압",
        FactKind.Temp => "온도", FactKind.Wet => "바닥 물", _ => "급수 유량",
    };

    public static string FromName(FactFrom f) => f switch
    {
        FactFrom.Sensor => "감지기", FactFrom.Meter => "유량계", FactFrom.Crew => "사람이 봄", FactFrom.Robot => "로봇이 봄", _ => "없음",
    };

    public ShipFact Get(Room r, FactKind k)
    {
        while (_rooms.Count <= r.Id) { var row = new ShipFact[7]; for (int i = 0; i < 7; i++) row[i] = new ShipFact(); _rooms.Add(row); }
        return _rooms[r.Id][(int)k];
    }

    /// <summary>믿음: 낸 쪽 신뢰 × 지난 시간만큼 옅어짐 (반감기는 값마다 다르다).</summary>
    public float Belief(Room r, FactKind k)
    {
        var f = Get(r, k);
        if (f.At < 0) return 0f;
        float age = (_w.Tick - f.At) / (float)SimTime.TicksPerHour;
        return f.Trust * MathF.Pow(0.5f, age / Life(k));
    }

    public bool Unknown(Room r, FactKind k) => Belief(r, k) < UnknownBelow;

    /// <summary>값 한 줄: "산소 20.9kPa (감지기 · 3분 전 · 믿음 높음)" · 모르면 "산소 ? (6시간 전 값 20.9)".</summary>
    public string Line(Room r, FactKind k)
    {
        var f = Get(r, k);
        string name = Name(k);
        if (f.At < 0) return $"{name} 모름";
        string v = ValueText(k, f.Value);
        float age = (_w.Tick - f.At) / (float)SimTime.TicksPerHour;
        string ago = age < 1f / 60f ? "방금" : age < 1f ? $"{age * 60f:0}분 전" : $"{age:0.#}시간 전";
        float b = Belief(r, k);
        if (b < UnknownBelow) return $"{name} ? ({ago} 값 {v} · {FromName(f.From)})";
        string trust = b > 0.8f ? "믿음 높음" : b > 0.55f ? "믿음 보통" : "믿음 낮음";
        string by = f.From == FactFrom.Crew && f.By >= 0 && f.By < _w.Crew.Count ? $"{Ko.IGa(_w.Crew[f.By].Name)} 봄" : FromName(f.From);
        return $"{name} {v} ({by} · {ago} · {trust})";
    }

    private static string ValueText(FactKind k, float v) => k switch
    {
        FactKind.People => $"{v:0}명",
        FactKind.Fire => v > 0.5f ? "있음" : "없음",
        FactKind.O2 => $"{v:0.0}kPa",
        FactKind.Pressure => $"{v:0}kPa",
        FactKind.Temp => $"{v:0}℃",
        FactKind.Wet => v < 0.1f ? "마름" : v < 0.5f ? "젖은 자국" : $"{v:0.0}cm",
        _ => v < 0.5f ? "정상" : $"시간당 {v:0.0}L 더 빠짐",
    };

    private void Put(Room r, FactKind k, float value, FactFrom from, long at, float trust, int by = -1)
    {
        var f = Get(r, k);
        if (at < f.At) return; // 더 새로운 값이 이긴다 (사람이 방금 본 값을 늦은 감지기 값이 덮지 않는다)
        if (at == f.At && f.From is FactFrom.Crew or FactFrom.Robot && from is FactFrom.Sensor or FactFrom.Meter) return;
        f.Value = value; f.From = from; f.At = at; f.Trust = trust; f.By = by;
    }

    /// <summary>v23 눈의 그물: 로봇이 지나가며 본 그 방 (사람 · 불 · 산소 · 기압 · 온도) — 로봇이 봄으로 장부에 (믿음 0.9).</summary>
    public void Saw(Room r, Robot rb)
    {
        var w = _w;
        int people = 0;
        foreach (var c in w.Crew) if (!c.Dead && c.Room == r && !c.Outside) people++;
        Put(r, FactKind.People, people, FactFrom.Robot, w.Tick, 0.9f, rb.Id);
        Put(r, FactKind.Fire, w.Fire.Count > 0 && w.Fire.CountIn(r) > 0 ? 1f : 0f, FactFrom.Robot, w.Tick, 0.9f, rb.Id);
        Put(r, FactKind.O2, r.Air.O2, FactFrom.Robot, w.Tick, 0.9f, rb.Id);
        Put(r, FactKind.Pressure, r.Air.Pressure, FactFrom.Robot, w.Tick, 0.9f, rb.Id);
        Put(r, FactKind.Temp, r.Air.Temperature, FactFrom.Robot, w.Tick, 0.9f, rb.Id);
    }

    // ───────────────────────────── 계기: 방 급수 유량계 ─────────────────────────────

    private static bool HasMeter(Room r)
    {
        foreach (var f in r.Furniture) if (f.Machine != null && Procedures.Plumbed(f.Type)) return true;
        return false;
    }

    /// <summary>유량계 교정 (실제 — 컴퓨터는 모른다): 그 방 급수 설비 감지기 가운데 가장 틀어진 것.</summary>
    private static float MeterCal(Room r)
    {
        float cal = 1f;
        foreach (var f in r.Furniture) if (f.Machine is Machine m && Procedures.Plumbed(f.Type)) cal = MathF.Min(cal, m.SensorCal);
        return cal;
    }

    /// <summary>컴퓨터가 짐작하는 유량계 교정 (마지막 교정 뒤 지난 날로 · 틀렸다고 드러나면 반으로).</summary>
    public float MeterGuess(Room r)
    {
        float cal = 1f;
        foreach (var f in r.Furniture) if (f.Machine is Machine m && Procedures.Plumbed(f.Type)) cal = MathF.Min(cal, WatchLog.KnownCal(m, _w));
        if (MeterSuspect(r)) cal *= 0.5f;
        return cal;
    }

    /// <summary>유량계가 잰 "더 빠지는 물" (L/h): 교정이 60% 아래면 작은 샘을 못 잡고, 1L/h 아래는 눈금에 안 걸린다.</summary>
    private float Measured(Room r)
    {
        if (!r.WaterLinked || r.ValveShut || _w.Water.Level <= 1f) return 0f;
        float leak = r.SeepLph;
        foreach (var f in r.Furniture) if (f.Machine is Machine m && Procedures.Plumbed(f.Type) && m.Line < 0.3f) leak += 6f;
        if (leak < 1f) return 0f;
        return MeterCal(r) >= 0.6f ? leak : 0f;
    }

    /// <summary>컴퓨터가 이 방 급수관 스밈을 안다 (유량계가 잡았거나 사람이 봤다).</summary>
    public bool SeepKnown(Room r) => r.SeepLph > 0f && (_seepKnown.Contains(r.Id) || Get(r, FactKind.Flow) is { From: FactFrom.Meter, Value: >= 1f } && Belief(r, FactKind.Flow) >= UnknownBelow && !JointLeak(r));

    private static bool JointLeak(Room r)
    {
        foreach (var f in r.Furniture) if (f.Machine is Machine m && Procedures.Plumbed(f.Type) && m.Line < 0.3f) return true;
        return false;
    }

    /// <summary>유량계가 틀렸다고 드러났고 아직 다시 맞추지 않았다 (교정을 건다).</summary>
    public bool MeterSuspect(Room r)
    {
        if (!_meterDoubt.TryGetValue(r.Id, out var at)) return false;
        foreach (var f in r.Furniture) if (f.Machine is Machine m && Procedures.Plumbed(f.Type) && m.LastCalibrated < at) return true;
        _meterDoubt.Remove(r.Id);
        return false;
    }

    public void Sealed(Room r, CrewMember? by)
    {
        r.SeepLph = 0f;
        r.SeepSince = -1;
        _seepKnown.Remove(r.Id);
        _wetSince.Remove(r.Id);
        _calm[r.Id] = _w.Tick + SimTime.Hours(3); // 땜질한 자리가 마를 동안은 다시 의심하지 않는다
        Put(r, FactKind.Flow, 0f, by != null ? FactFrom.Crew : FactFrom.Robot, _w.Tick, 1f, by?.Id ?? -1);
    }

    // ───────────────────────────── 시스템 틱마다 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        var a = w.Automation;
        // 1) 감지기 · 계기 값을 장부에 (감지기 믿음 모형이 잰 때 · 감지기 신뢰 그대로)
        ScanMarks();
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) continue;
            var b = a.Belief.Of(r);
            if (b.Updated >= 0)
            {
                Put(r, FactKind.People, b.People, FactFrom.Sensor, b.Updated, b.Trust);
                Put(r, FactKind.Fire, b.Fire ? 1f : 0f, FactFrom.Sensor, b.Updated, b.Trust);
                Put(r, FactKind.O2, b.O2, FactFrom.Sensor, b.Updated, b.Trust);
                Put(r, FactKind.Pressure, b.Pressure, FactFrom.Sensor, b.Updated, b.Trust);
            }
            if (!a.Belief.Reading(r) || b.Fault == SensorFault.Stuck) continue;
            Put(r, FactKind.Temp, r.Air.Temperature, FactFrom.Sensor, w.Tick, 1f);
            Put(r, FactKind.Wet, WetLevel(r), FactFrom.Sensor, w.Tick, WetTrust(r));
            if (r.WaterLinked && HasMeter(r)) Put(r, FactKind.Flow, Measured(r), FactFrom.Meter, w.Tick, MeterGuess(r));
        }
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(10);
        Notice();
        if (!a.MainOnline) return;
        foreach (var r in w.Ship.LiveRooms) if (SeepKnown(r) && !_seepKnown.Contains(r.Id)) { _seepKnown.Add(r.Id); a.Reason($"seep:{r.Id}", $"{r.Name} 급수 유량이 시간당 {Get(r, FactKind.Flow).Value:0.0}L 더 빠진다 — 급수관이 스민다 · 요청: 땜질", SimTime.Hours(6)); }
        Suspect();
        Stale();
        Progress();
    }

    private float WetTrust(Room r) => _wetTrust.TryGetValue(r.Id, out var t) ? t : 0.95f;

    private float[] _wetMark = Array.Empty<float>();
    private readonly Dictionary<int, long> _wetSince = new();

    /// <summary>바닥 젖음: 고인 물 깊이(cm)와 젖은 자국(가장 젖은 칸 × 0.5) 가운데 큰 것 — 엎지른 자국은 금방 마르고, 스미는 자리는 마르지 않는다.</summary>
    public float WetLevel(Room r) => MathF.Max(MoistureSystem.DepthCm(r), r.Id < _wetMark.Length ? _wetMark[r.Id] * 0.5f : 0f);

    private void ScanMarks()
    {
        var w = _w;
        var ship = w.Ship;
        if (_wetMark.Length != ship.Rooms.Count) _wetMark = new float[ship.Rooms.Count];
        Array.Clear(_wetMark);
        foreach (var (i, st) in w.Body.Marks)
        {
            float v = st.V[(int)CellMark.Wet];
            if (v <= 0.05f || ship.RoomAt(ship.Grid.CellAt(i)) is not Room r || r.Id >= _wetMark.Length) continue;
            if (v > _wetMark[r.Id]) _wetMark[r.Id] = v;
        }
    }

    /// <summary>사람이 젖은 방에 있다가 스미는 데를 스스로 본다 (컴퓨터가 몰라도 · 꼼꼼할수록).</summary>
    private void Notice()
    {
        var w = _w;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.SeepLph <= 0f || _seepKnown.Contains(r.Id) || WetLevel(r) < 0.2f) continue;
            foreach (var c in w.Crew)
            {
                if (c.Room != r || !c.CanAct || !c.IsAwake || c.Outside) continue;
                if (((w.Tick / SimTime.Minutes(10) + c.Id * 7 + r.Id * 13) % 100) >= (int)(10f * (0.5f + c.Traits.Diligence))) continue; // 열 분에 5~15%
                _seepKnown.Add(r.Id);
                SeepsSeen++;
                Put(r, FactKind.Wet, WetLevel(r), FactFrom.Crew, w.Tick, 1f, c.Id);
                c.Say(w, Persona.Say(c, "어, 바닥 밑 관에서 물이 스미네"));
                w.Log.Add(w.Tick, LogKind.Work, $"{r.Name} 바닥이 젖은 데를 따라가 급수관이 스미는 걸 봤다 — 땜질해야 한다", c.Id);
                MarkLog.Add(r.Marks, w.Tick, $"{c.Name}: 급수관 스밈 발견");
                w.Board.RequestScan();
                break;
            }
        }
    }

    // ───────────────────────────── 어긋남 → 의심 ─────────────────────────────

    private static bool ColdPipes(World w, Room r) => r.Type is RoomType.Cooling or RoomType.Reactor
        || w.Piping.Segments.Any(s => s.IsCoolant && s.Path.Any(c => w.Ship.RoomAt(c) == r));

    private float Weight(string key) => Lessons.TryGetValue(key, out var l) ? (l.hit + 1f) / (l.tries + 2f) * 2f : 1f;

    /// <summary>바닥이 젖었는데 까닭이 장부에 없다: 급수 유량은 정상 (또는 유량계가 없다) → 갈래를 세워 묻는다.</summary>
    private void Suspect()
    {
        var w = _w;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Type == RoomType.Corridor || r.Abandoned || r.OffLimits) continue;
            if (SeepKnown(r) || JointLeak(r) || w.Moisture.Noticed(r) && MoistureSystem.Depth(r) > 0.12f) { _wetSince.Remove(r.Id); continue; } // 까닭을 안다 · 크게 찬 물은 이미 큰 일로 다룬다 (까닭을 모르는 동안만 센다)
            var wet = Get(r, FactKind.Wet);
            if (wet.Value < 0.15f) _wetSince.Remove(r.Id);
            else if (!_wetSince.ContainsKey(r.Id)) _wetSince[r.Id] = wet.At;
            if (wet.Value < 0.25f || Belief(r, FactKind.Wet) < 0.5f) continue;
            if (!_wetSince.TryGetValue(r.Id, out var since) || w.Tick - since < SimTime.Hours(1.5f)) continue; // 엎지른 자국은 금방 마른다 — 오래 젖어 있어야 까닭을 찾는다
            if (_calm.TryGetValue(r.Id, out var calm) && w.Tick < calm) continue;
            if (Open.Any(d => d.RoomId == r.Id)) continue;
            var flow = Get(r, FactKind.Flow);
            bool meter = r.WaterLinked && HasMeter(r) && Belief(r, FactKind.Flow) >= UnknownBelow;
            if (meter && flow.Value >= 1f) continue; // 유량계가 샘을 잡고 있다 (곧 스밈으로 안다)
            float guess = meter ? MeterGuess(r) : 0.5f;
            var neighbor = w.Ambience.Neighbors(r).Where(x => x.Item2).Select(x => x.Item1).Where(n => !n.Detached && Get(n, FactKind.Wet).Value > wet.Value + 0.2f).OrderByDescending(n => Get(n, FactKind.Wet).Value).FirstOrDefault();
            var d = new FactDoubt
            {
                Id = ++_ids, RoomId = r.Id, Kind = "wet", Since = w.Tick,
                Symptom = $"바닥이 {(w.Tick - since) / (float)SimTime.TicksPerHour:0.#}시간째 젖어 있다 ({FromName(wet.From)}) — 그런데 " + (meter ? $"급수 유량은 정상 (유량계 교정 짐작 {guess * 100:0}%)" : "이 방엔 유량계가 없다"),
            };
            void H(string key, string name, float p) { if (p > 0f) d.H.Add(new ProbeHypo { Key = key, Name = name, P = p * Weight(key) }); }
            H("seep", meter ? "작은 누수 — 유량계가 못 잡는다" : "작은 누수", r.WaterLinked ? 0.25f + 0.45f * (1f - guess) : 0.03f);
            bool humid = r.Humidity > 0.7f, cold = ColdPipes(w, r);
            H("cond", "결로 — 찬 관에 맺힌 물", humid && cold ? 0.5f : cold ? 0.15f : humid ? 0.1f : 0.04f);
            H("near", neighbor != null ? $"옆 {neighbor.Name}에서 흘러듦" : "옆방에서 흘러듦", neighbor != null ? 0.4f : 0.04f);
            H("sensor", "바닥 감지기 오류", wet.From == FactFrom.Sensor ? (WetTrust(r) < 0.7f ? 0.3f : 0.08f) : 0.02f);
            H("spill", "누가 엎지른 물", 0.08f);
            float sum = d.H.Sum(x => x.P);
            foreach (var x in d.H) x.P /= sum;
            d.H.Sort((x, y) => y.P.CompareTo(x.P));
            Open.Add(d);
            _calm[r.Id] = w.Tick + SimTime.Hours(6);
            w.Automation.Reason($"doubt:{r.Id}", $"{r.Name} {d.Symptom} — 의심: {string.Join(" · ", d.H.Take(3).Select(x => $"{x.Name} {x.P * 100:0}%"))} → 사람에게 확인을 부탁한다", SimTime.Hours(2));
            if (Open.Count >= 3) return;
        }
    }

    /// <summary>모름: 사람이 있던 방 · 생명유지에 걸린 방의 산소 값이 오래 멈췄다 (감지기가 멎었거나 소식이 끊겼다) → 다시 읽어 보고, 안 되면 묻는다.</summary>
    private void Stale()
    {
        var w = _w;
        if (Open.Count(d => d.Kind == "stale") >= 2) return;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Type == RoomType.Corridor || r.Abandoned || r.OffLimits || r.Air.Pressure < 40f) continue;
            var o2 = Get(r, FactKind.O2);
            if (o2.At < 0 || !Unknown(r, FactKind.O2)) continue;
            // 사람이 있었거나 · 사람이 있는지조차 모르는 (자주 쓰는) 방 · 생명유지에 걸린 방
            bool busyRoom = r.Type is not (RoomType.Storage or RoomType.GasStorage or RoomType.Airlock or RoomType.Corridor);
            bool matters = Get(r, FactKind.People).Value > 0.5f || busyRoom && Unknown(r, FactKind.People) || r.Type is RoomType.LifeSupport or RoomType.Medbay or RoomType.Bridge or RoomType.Reactor;
            if (!matters) continue;
            if (_calm.TryGetValue(r.Id, out var calm) && w.Tick < calm) continue;
            if (Open.Any(d => d.RoomId == r.Id)) continue;
            float age = (w.Tick - o2.At) / (float)SimTime.TicksPerHour;
            bool reading = w.Automation.Belief.Reading(r);
            var d = new FactDoubt
            {
                Id = ++_ids, RoomId = r.Id, Kind = "stale", Since = w.Tick,
                Symptom = reading ? $"산소 값이 {age:0.#}시간째 그대로 — 다시 읽어도 같다 (감지기가 멎은 것 같다)" : $"데이터선이 끊겨 {age:0.#}시간째 소식이 없다",
            };
            Open.Add(d);
            StaleAsks++;
            _calm[r.Id] = w.Tick + SimTime.Hours(6);
            w.Automation.Reason($"stale:{r.Id}", $"{r.Name} {d.Symptom} — 모르는 채로 두지 않는다 → 사람에게 묻는다", SimTime.Hours(2));
            return;
        }
    }

    // ───────────────────────────── 묻기 · 답 ─────────────────────────────

    private void Progress()
    {
        var w = _w;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var d = Open[i];
            var r = w.Ship.Rooms[d.RoomId];
            // 물을 까닭이 사라졌다 (그사이 누가 봤다 · 값이 다시 들어온다)
            if (d.Kind == "wet" && (SeepKnown(r) || JointLeak(r)) && d.State != "확인 중") { Close(d, "seep", "그사이 스미는 데를 찾았다", null, ""); continue; }
            if (d.Kind == "stale" && !Unknown(r, FactKind.O2)) { Close(d, "", "값이 다시 들어온다", null, ""); continue; }
            if (w.Tick - d.Since > SimTime.Hours(8)) { d.State = "모름"; Close(d, "", "아무도 확인하지 못했다", null, ""); continue; }
            // 값이 끊긴 방에 깨어 있는 사람이 있다: 손목 단말로 묻고 그 자리에서 답을 듣는다 (가서 볼 일을 만들지 않는다)
            if (d.Kind == "stale" && d.Asked < 0)
                foreach (var c in w.Crew)
                    if (c.Room == r && c.CanAct && c.IsAwake && !c.Outside && !c.IsChild)
                    {
                        Asked(d, c);
                        WristAnswers++;
                        Answer(d, c, null);
                        break;
                    }
        }
    }

    /// <summary>게시판에 올릴 "살펴보기" (아직 답이 없는 의심 · 모름).</summary>
    public IEnumerable<FactDoubt> Looks()
    {
        foreach (var d in Open) if (d.Closed < 0) yield return d;
    }

    public FactDoubt? Doubt(int id)
    {
        foreach (var d in Open) if (d.Id == id) return d;
        return null;
    }

    /// <summary>살펴보기를 맡았다: 사람이면 손목 단말로 부탁을 받는다 · 로봇이면 그냥 간다.</summary>
    public void Asked(FactDoubt d, CrewMember? c, string robot = "")
    {
        var w = _w;
        var r = w.Ship.Rooms[d.RoomId];
        if (c != null ? d.Asked == c.Id : d.State == "확인 중") return; // 이미 맡았다
        d.State = "확인 중";
        d.AskedAt = w.Tick;
        d.Tries++;
        Asks++;
        if (c == null) { w.Log.Add(w.Tick, LogKind.Ship, $"{robot} — {r.Name} 살피러 간다 ({d.Symptom})"); return; }
        d.Asked = c.Id;
        string what = d.Kind == "wet" ? $"{r.Name} 바닥이 젖었는데 급수 유량은 정상입니다 — 어디서 온 물인지 봐 주시겠어요?" : $"{r.Name} 값이 끊겼습니다 — 산소 · 사람 상태를 알려 주시겠어요?";
        w.Log.Add(w.Tick, LogKind.Ship, $"[손목 단말] {w.Automation.Voice.Call} → {c.Name}: {what}", c.Id);
    }

    /// <summary>가서 봤다 (사람 · 로봇) 또는 방 안에서 손목 단말로 답했다: 장부를 본 값으로 · 어긋남이면 까닭을 가린다.</summary>
    public void Answer(FactDoubt d, CrewMember? c, Robot? rb)
    {
        var w = _w;
        var r = w.Ship.Rooms[d.RoomId];
        if (d.Closed >= 0) return;
        Answers++;
        long now = w.Tick;
        var from = c != null ? FactFrom.Crew : FactFrom.Robot;
        int by = c?.Id ?? -1;
        string who = c?.Name ?? rb?.Name ?? "로봇";
        int people = 0;
        foreach (var x in w.Crew) if (!x.Dead && x.Room == r && !x.Outside) people++;
        Put(r, FactKind.People, people, from, now, 1f, by);
        Put(r, FactKind.O2, r.Air.O2, from, now, 1f, by);
        Put(r, FactKind.Pressure, r.Air.Pressure, from, now, 1f, by);
        Put(r, FactKind.Temp, r.Air.Temperature, from, now, 1f, by);
        Put(r, FactKind.Wet, WetLevel(r), from, now, 1f, by);
        var b = w.Automation.Belief.Of(r);
        if (d.Kind == "stale")
        {
            bool stuck = b.Fault != SensorFault.None;
            w.Automation.Belief.Reset(r, who, by, stuck); // 그 자리에서 감지기를 껐다 켠다
            string said = $"산소 {r.Air.O2:0.0}kPa · {people}명" + (stuck ? " — 벽 감지기 숫자가 멈춰 있었다, 다시 켰다" : "");
            if (c != null) c.Say(w, Persona.Say(c, said));
            w.Log.Add(w.Tick, LogKind.Work, $"{(c != null ? "[손목 단말] " : "")}{who} → 주 컴퓨터: {r.Name} {said}", by);
            Close(d, stuck ? "sensor" : "link", stuck ? "감지기 값이 멈춰 있었다 — 그 자리에서 다시 켰다" : "데이터선이 끊겨 소식만 없었다 — 별일 없다", c, who);
            return;
        }
        // 어긋남: 실제로 무엇이었나 (본 그대로)
        string truth, finding;
        var neighbor = w.Ambience.Neighbors(r).Where(x => x.Item2).Select(x => x.Item1).Where(n => !n.Detached && MoistureSystem.Depth(n) > MoistureSystem.Depth(r) + 0.01f).OrderByDescending(MoistureSystem.Depth).FirstOrDefault();
        if (r.SeepLph > 0f)
        {
            truth = "seep";
            _seepKnown.Add(r.Id);
            bool missed = MeterCal(r) < 0.6f && r.WaterLinked && HasMeter(r);
            if (missed) { _meterDoubt[r.Id] = now; MeterFound++; }
            finding = $"바닥 밑 급수관이 스민다 (시간당 {r.SeepLph:0.0}L)" + (missed ? $" — 유량계 교정이 {MeterCal(r) * 100:0}%라 못 잡았다" : r.WaterLinked && HasMeter(r) ? " — 유량계 눈금 아래로 새는 양" : "");
            w.Board.RequestScan();
        }
        else if (JointLeak(r)) { truth = "seep"; finding = "설비 관 이음이 빠져 샌다"; }
        else if (neighbor != null) { truth = "near"; finding = $"옆 {neighbor.Name}에서 흘러든 물"; }
        else if (WetLevel(r) < 0.08f) { truth = "sensor"; finding = "바닥은 말랐다 — 바닥 감지기가 틀렸다"; _wetTrust[r.Id] = MathF.Max(0.3f, WetTrust(r) - 0.3f); }
        else if (r.Humidity > 0.65f && ColdPipes(w, r)) { truth = "cond"; finding = "찬 관에 맺혀 떨어진 물 (결로)"; }
        else { truth = "spill"; finding = "엎지른 물 같다 — 스미는 데는 없다"; }
        if (c != null) c.Say(w, Persona.Say(c, finding));
        w.Log.Add(w.Tick, LogKind.Work, $"{(c != null ? "[손목 단말] " : "")}{who} → 주 컴퓨터: {r.Name} {finding}", by);
        MarkLog.Add(r.Marks, w.Tick, $"{who}: 젖은 바닥 확인 — {finding}");
        Close(d, truth, finding, c, who);
    }

    private void Close(FactDoubt d, string truth, string finding, CrewMember? by, string who)
    {
        var w = _w;
        var r = w.Ship.Rooms[d.RoomId];
        d.Truth = truth;
        d.Finding = finding + (who != "" ? $" ({who})" : "");
        d.Closed = w.Tick;
        if (d.Kind == "wet" && d.H.Count > 0 && truth != "")
        {
            var top = d.H[0];
            foreach (var h in d.H)
            {
                Lessons.TryGetValue(h.Key, out var l);
                Lessons[h.Key] = (l.hit + (h.Key == truth ? 1 : 0), l.tries + (h == top || h.Key == truth ? 1 : 0));
            }
            var right = d.H.FirstOrDefault(h => h.Key == truth);
            if (top.Key == truth)
            {
                d.State = "맞음";
                Rights++;
                w.Automation.Reason($"doubtok:{r.Id}", $"{r.Name} 확인 — {finding}. 짐작이 맞았다 ({top.Name} {top.P * 100:0}%)", SimTime.Hours(1));
            }
            else
            {
                d.State = "고침";
                Fixes++;
                w.Automation.Reason($"doubtfix:{r.Id}", $"{r.Name} 확인 — {finding}. 진단을 고친다: {top.Name} {top.P * 100:0}% → {right?.Name ?? finding}" + (truth == "seep" && MeterSuspect(r) ? " · 이 방 유량계를 덜 믿는다 (교정 요청)" : ""), SimTime.Hours(1));
            }
        }
        else if (d.State is "의심" or "확인 중") d.State = truth == "" ? "모름" : "맞음";
        Open.Remove(d);
        Done.Add(d);
        if (Done.Count > 30) Done.RemoveAt(0);
        if (by != null && d.Kind == "wet") w.Automation.Trusts.Change(by, 0.01f, $"{r.Name} 젖은 바닥을 같이 봐 줬다", quiet: true);
        w.Board.RequestScan();
    }
}

public sealed partial class AutomationSystem
{
    private ComputerFacts? _facts;
    /// <summary>v19 정보 경계: 컴퓨터가 아는 것 (값 · 출처 · 확인 시각 · 믿음) · 어긋남 의심 · 묻기.</summary>
    public ComputerFacts Facts => _facts ??= new ComputerFacts(_world);
}

public sealed partial class WorkBoard
{
    /// <summary>v19 정보 경계: 주 컴퓨터가 의심하거나 모르는 방 → "살펴보기" (로봇 먼저) · 아는 급수관 스밈 → 땜질.</summary>
    private void ScanFacts(Poster post)
    {
        var w = _world;
        var facts = w.Automation.Facts;
        if (w.Automation.Present)
            foreach (var d in facts.Looks())
            {
                var r = w.Ship.Rooms[d.RoomId];
                if (r.Detached || r.Abandoned || r.OffLimits || r.Air.Pressure < 40f) continue;
                var spot = r.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - r.Center).LengthSquared()).FirstOrDefault();
                if (spot == default) continue;
                string why = d.Kind == "wet" ? $"바닥 물 — 어디서 왔는지 모른다 (짐작: {(d.H.Count > 0 ? d.H[0].Name : "?")})" : "값이 끊겼다 — 가서 보고 알려 준다";
                post(WorkKind.LookRoom, WorkTarget.AtCell(spot, r), 0.5f, Skill.Mechanics, why, circuit: d.Id);
            }
        foreach (var r in w.Ship.LiveRooms)
            if (r.SeepLph > 0f && !r.Abandoned && !r.OffLimits && facts.SeepKnown(r) && w.Ship.IsWalkable(r.SeepAt))
                post(WorkKind.SealSeep, WorkTarget.AtCell(r.SeepAt, r), 0.45f, Skill.Mechanics, $"바닥 밑 급수관이 스민다 (시간당 {r.SeepLph:0.0}L)");
    }
}

public static partial class WorkPlanners
{
    private static Job? LookRoom(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var d = w.Automation.Facts.Doubt(o.Circuit);
        var room = o.Target.Room;
        if (d == null || room == null) { w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new DoToil((cm, world) => { world.Automation.Facts.Asked(d, cm); return true; }));
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil((int)SimTime.Minutes(d.Kind == "wet" ? 4 : 2), Pose.Working, at.Center));
        toils.Add(new DoToil((cm, world) => { if (!o.Closed) { world.Automation.Facts.Answer(d, cm, null); world.Board.Close(o); } return true; }));
        return Wrap(a, o, c, w, "살펴보기", toils, $"주 컴퓨터 부탁 — {room.Name}에 가서 본다");
    }

    private static Job? SealSeep(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room;
        if (room == null || room.SeepLph <= 0f) { w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.6f, Skill.Mechanics, at.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) => { if (!o.Closed) { Sealed(world, room, cm.Name, cm); world.Board.Close(o); } return true; }));
        return Wrap(a, o, c, w, "급수관 땜질", toils, $"{room.Name} 바닥 밑 급수관을 땜질하러 간다");
    }

    /// <summary>급수관 스밈을 땜질했다 (사람 · 로봇).</summary>
    internal static void Sealed(World w, Room room, string who, CrewMember? c)
    {
        float l = room.SeepLph;
        w.Automation.Facts.Sealed(room, c);
        w.Log.Add(w.Tick, LogKind.Work, $"{who}: {room.Name} 바닥 밑 급수관 스밈을 땜질했다 (시간당 {l:0.0}L)", c?.Id ?? -1);
        MarkLog.Add(room.Marks, w.Tick, $"{who}: 급수관 땜질");
        w.Board.RequestScan();
    }
}
