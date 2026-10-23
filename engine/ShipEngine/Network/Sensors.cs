using System.Runtime.InteropServices;

namespace ShipEngine;

public enum TrackKind : byte { Rock, Cloud, Contact, Flare }

/// <summary>센서가 잡은 것 하나: 갈래 · 그 물체의 번호(운석 · 구름 · 배 · 폭발은 원인 마디) · 처음 잡은 틱 · 잡은 원인 마디.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Track
{
    public TrackKind Kind;
    public int Id;
    public long First;
    public int Cause;
}

/// <summary>
/// ⑥ 감지 법칙 (⑫ 자연 사건 — '미리 알기 = 센서로 잡힌 것만' · docs/ENGINE.md ⑫): 배의 센서가 바깥에서 무엇을 잡았나를 월드가 계산한다.
/// <para>센서 배열(SensorArray)이 하나라도 돌면 — 레이더: 운석은 R = radarKm·σ^¼ (σ = 돌의 단면 πr² · 3000kg/m³ — 1kg 28km) · 다른 배는 단면 400m² ·
/// 넓게 퍼진 파편 구름은 구름 가장자리까지 cloudKm · X선 감시: 별 폭발의 조짐. 신호기를 켠 배는 beaconKm에서 교신으로 안다 (센서 배열이 멈춰도).</para>
/// <para>처음 잡으면 원인 마디(그 물체의 원인에 잇는다) + 사실 '센서에 잡혔다'. 주컴퓨터 · 화면은 이것만 안다 (판단 엔진은 읽기만).
/// 감각 단계에 돈다 — 바깥(⑤)이 움직인 뒤 · 주컴퓨터(⑩)가 읽기 전 (같은 틱).</para>
/// </summary>
public sealed class Sensors : IEngine
{
    public string Name => "감지";
    public Phase Phase => Phase.Sense;
    public int Every => 1;
    public int Offset => 0;

    public const double BoatSigma = 400;   // 배 한 척 단면 (m²)

    private readonly Tables T;
    private Network? _net;
    private Outside? _out;
    private int[]? _arrays;

    // ── 상태 (찍는다) ──
    public List<Track> Tracks = new();
    public long Sensed;

    public Sensors(Tables t) { T = t; }

    /// <summary>센서 배열이 하나라도 돈다.</summary>
    public bool Up
    {
        get
        {
            if (_net == null || _arrays == null) return false;
            foreach (int d in _arrays) if (_net.Working(d)) return true;
            return false;
        }
    }

    public bool Tracked(TrackKind k, int id)
    {
        foreach (var t in Tracks) if (t.Kind == k && t.Id == id) return true;
        return false;
    }

    public int CauseOf(TrackKind k, int id)
    {
        foreach (var t in Tracks) if (t.Kind == k && t.Id == id) return t.Cause;
        return 0;
    }

    /// <summary>레이더가 단면 σ(m²)를 잡는 거리 (m).</summary>
    public double RadarRange(double sigma) => T.Space.Ship.RadarKm * 1000 * Math.Pow(Math.Max(1e-9, sigma), 0.25);

    /// <summary>돌덩이의 레이더 단면 (m² — 3000kg/m³ 구의 πr²).</summary>
    public static double RockSigma(double mass) { double r = Math.Cbrt(3 * mass / (4 * Math.PI * 3000)); return Math.PI * r * r; }

    public void Bind(Sim sim) { _net ??= sim.Find<Network>(); _out ??= sim.Find<Outside>(); }

    public void Step(Sim sim, double dt)
    {
        if (_net == null || _out == null) return;
        _arrays ??= _net.Devices.Where(d => d.Type == "SensorArray").Select(d => d.Id).ToArray();
        var o = _out;
        Tracks.RemoveAll(t => !Exists(o, t));
        bool up = Up;
        var M = T.Space.Ship;
        if (up)
        {
            foreach (var r in o.Rocks)
            {
                if (r.Mass <= 0 || Tracked(TrackKind.Rock, r.Id)) continue;
                double R = Math.Sqrt(r.X * r.X + r.Y * r.Y);
                if (R <= RadarRange(RockSigma(r.Mass))) See(sim, TrackKind.Rock, r.Id, r.Cause, $"레이더에 {r.Mass:0.##}kg 운석이 잡혔다 — {R / 1000:0.#}km", (float)(R / 1000));
            }
            double reach = M.CloudKm * 1000;
            foreach (var c in o.Clouds)
            {
                if (Tracked(TrackKind.Cloud, c.Id)) continue;
                double gap = Math.Sqrt(c.X * c.X + c.Y * c.Y) - c.Radius;
                if (gap <= reach) See(sim, TrackKind.Cloud, c.Id, c.Cause, $"센서에 {o.DebrisName(c.Kind)}이(가) 잡혔다 — {Math.Max(0, gap) / 1000:0}km", (float)(Math.Max(0, gap) / 1000));
            }
            if (o.FlareWarnAt >= 0 && o.FlareStart < 0 && o.FlareCause != 0 && !Tracked(TrackKind.Flare, o.FlareCause))
                See(sim, TrackKind.Flare, o.FlareCause, o.FlareCause, $"X선 감시에 {o.StarDef.Name}의 폭발 조짐이 잡혔다", 0);
        }
        double boat = up ? RadarRange(BoatSigma) : 0, beacon = M.BeaconKm * 1000;
        foreach (var c in o.Contacts)
        {
            if (c.State == ContactState.Gone || Tracked(TrackKind.Contact, c.Id)) continue;
            bool heard = c.Beacon != 0 && c.Range < beacon;
            if (heard || c.Range < boat) See(sim, TrackKind.Contact, c.Id, c.Cause, heard ? $"{o.ContactName(c)}의 신호가 잡혔다" : $"레이더에 배 한 척이 잡혔다 — {c.Range / 1000:0}km", (float)(c.Range / 1000));
        }
    }

    private static bool Exists(Outside o, in Track t)
    {
        int id = t.Id;
        return t.Kind switch
        {
            TrackKind.Rock => o.Rocks.Exists(r => r.Id == id && r.Mass > 0),
            TrackKind.Cloud => o.Clouds.Exists(c => c.Id == id),
            TrackKind.Contact => o.Contacts.Exists(c => c.Id == id && c.State != ContactState.Gone),
            _ => o.FlareCause == id && (o.FlareWarnAt >= 0 || o.FlareStart >= 0),
        };
    }

    private void See(Sim sim, TrackKind k, int id, int parent, string text, float km)
    {
        int cause = sim.Causes.Add(CauseKind.Sensor, text, sim.Tick, -1, parent);
        Tracks.Add(new Track { Kind = k, Id = id, First = sim.Tick, Cause = cause });
        Sensed++;
        sim.Facts.Emit(sim.Tick, FactKind.Sensed, -1, km, cause, other: (int)k);
    }

    public void Save(StateWriter w) { w.List(Tracks); w.I64(Sensed); }
    public void Load(StateReader r) { Tracks = r.List<Track>(); Sensed = r.I64(); }
}
