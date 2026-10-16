using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>이야기꾼의 성격.</summary>
public enum StoryStyle : byte { Balanced, Gentle, Relentless, SlowBurn, Cosmic, Human }

/// <summary>이야기꾼이 심는 원인의 종류.</summary>
public enum SeedKind : byte { None, Rock, Cloud, Flare, WornPart, HullFatigue, Mold, ArcRisk }

/// <summary>심은 원인 하나 (기록).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Seed
{
    public long Tick;
    public SeedKind Kind;
    public int Cause;      // 원인 마디 (뿌리)
    public int Cell;       // 자리 (-1: 배 밖)
    public float Size;
}

/// <summary>
/// ⑫ 이야기꾼 (docs/ENGINE.md §4 ⑫). 결과가 아니라 원인만 심는다 — 사건은 아래 엔진들이 알아서 만든다 (안 만들 수도 있다).
/// 지금 긴장(사실 흐름의 무게 · 반감기 2시간)이 성격의 목표 긴장보다 낮고 쉼이 지났으면 30분마다 하나 심는다. 가혹함 1~8 (기본 4 '아슬아슬').
/// </summary>
public sealed class Storyteller : IEngine
{
    public string Name => "이야기꾼";
    public Phase Phase => Phase.Outside;
    public int Every => 1;
    public int Offset => 0;

    public const int ThinkEvery = 750;   // 30분

    private readonly Field F;
    private readonly Layout L;
    private readonly Tables T;
    private RngStream _rng;

    /// <summary>기본은 꺼짐 — 게임 · 관찰 장면 · 재미 측정이 켠다 (규칙 시험이 뜻밖의 운석을 맞지 않게).</summary>
    public bool Enabled;
    public byte Level = 4;
    public StoryStyle Style = StoryStyle.Balanced;
    public double Tension;
    public long LastSeedTick = long.MinValue / 2;
    public SeedKind LastKind;
    public List<Seed> Seeds = new();
    private long _factCursor;

    public Storyteller(Field f, ulong seed)
    {
        F = f; L = f.L; T = f.T;
        _rng = Rng.Stream(seed, "이야기꾼");
    }

    public static string StyleName(StoryStyle s) => s switch
    {
        StoryStyle.Balanced => "고른 이야기꾼", StoryStyle.Gentle => "잔잔한 이야기꾼", StoryStyle.Relentless => "몰아치는 이야기꾼",
        StoryStyle.SlowBurn => "느린 불씨", StoryStyle.Cosmic => "우주의 변덕", StoryStyle.Human => "사람의 이야기", _ => "",
    };

    // ───────────────────────── 긴장 ─────────────────────────

    public static double Weight(FactKind k) => k switch
    {
        FactKind.Died => 10, FactKind.Collapsed => 4, FactKind.Hurt => 1, FactKind.WallBreach => 3, FactKind.Ignition => 2, FactKind.FireSpread => 1,
        FactKind.ComputerAlarm => 1, FactKind.Panicked => 2, FactKind.DoorBlown => 2, FactKind.MachineFail => 1, FactKind.Impact => 1,
        FactKind.Short => 2, FactKind.BreakerTrip => 0.5, FactKind.Flare => 2, FactKind.DebrisNear => 1, _ => 0,
    };

    /// <summary>성격 · 가혹함에 따른 지금 목표 긴장.</summary>
    public double Target(long tick)
    {
        double h = Time.HourOfDay(tick);
        double wave = Math.Pow(Math.Sin(Math.PI * h / 12), 2); // 하루 두 번 오르내림
        double baseT = Style switch
        {
            StoryStyle.Gentle => 1.0 + 1.0 * wave,
            StoryStyle.Relentless => 4.0 + 3.0 * wave,
            StoryStyle.SlowBurn => 1.5 + 1.5 * wave,
            StoryStyle.Cosmic => 2.0 + 3.0 * wave,
            StoryStyle.Human => 1.5 + 2.0 * wave,
            _ => 2.0 + 3.0 * wave,
        };
        return baseT * Level / 4.0;
    }

    private double RestHours => Style switch { StoryStyle.Gentle => 8, StoryStyle.Relentless => 2, StoryStyle.SlowBurn => 5, _ => 4 } * 4.0 / Math.Max(1, (int)Level);

    public void Step(Sim sim, double dt)
    {
        // 사실 흐름을 긴장으로
        foreach (var f in sim.Facts.Since(_factCursor)) Tension += Weight(f.Kind);
        _factCursor = sim.Facts.Total;
        Tension *= Math.Exp(-dt * 0.6931 / (2 * 3600));
        if (!Enabled || sim.Tick % ThinkEvery != ThinkEvery - 1) return;
        if (sim.Tick < Time.Hours(2)) return; // 첫 두 시간은 배를 보여 준다
        if (sim.Tick - LastSeedTick < Time.Hours(RestHours)) return;
        if (Tension >= Target(sim.Tick)) return;
        Plant(sim, Choose());
    }

    private SeedKind Choose()
    {
        // 성격마다 무게 (없음 · 운석 · 구름 · 폭발 · 낡은 부품 · 외판 피로 · 곰팡이 · 합선 불씨)
        double[] w = Style switch
        {
            StoryStyle.Cosmic => new double[] { 0, 3, 3, 2, 0.3, 0.3, 0, 0 },
            StoryStyle.SlowBurn => new double[] { 0, 0.3, 0.3, 0.3, 3, 2, 2, 2 },
            StoryStyle.Gentle => new double[] { 0, 0.5, 1, 1, 2, 1, 1, 0.3 },
            StoryStyle.Human => new double[] { 0, 0.3, 0.5, 0.5, 2, 1, 1.5, 1 },
            StoryStyle.Relentless => new double[] { 0, 2, 2, 1.5, 1.5, 1.5, 1, 2 },
            _ => new double[] { 0, 1.5, 1.5, 1, 2, 1.5, 1, 1.5 },
        };
        if (LastKind != SeedKind.None) w[(int)LastKind] *= 0.15; // 같은 종류를 연달아 피한다
        double sum = w.Sum(), r = _rng.Unit() * sum;
        for (int i = 1; i < w.Length; i++) { r -= w[i]; if (r <= 0) return (SeedKind)i; }
        return SeedKind.WornPart;
    }

    // ───────────────────────── 심기 ─────────────────────────

    /// <summary>원인 하나를 심는다 (시험 · 함장의 '이야기꾼 부르기'도 이것을 쓴다).</summary>
    public bool Plant(Sim sim, SeedKind kind)
    {
        double g = Level / 4.0; // 가혹함 배율
        int cell = -1; float size = 0; string text;
        var outside = sim.Find<Outside>(); var net = sim.Find<Network>(); var st = sim.Find<Structure>(); var life = sim.Find<Life>();
        switch (kind)
        {
            case SeedKind.Rock:
            {
                if (outside == null) return false;
                double mass = Math.Min(5, 0.05 * Math.Pow(10, _rng.Range(0.0, 1.6)) * g);   // 0.05 ~ 2kg (가혹함 4)
                double speed = _rng.Range(3000, 12000);
                double minutes = _rng.Range(15, 40);
                double offset = _rng.Range(0, 120);                                          // 비껴갈 수도 있다
                double ang = _rng.Range(0, 2 * Math.PI);
                text = $"{mass:0.##}kg 운석이 {minutes:0}분 거리에서 다가온다";
                int c0 = sim.Causes.Add(CauseKind.Story, text, sim.Tick);
                outside.AddRock(mass, speed, Math.Cos(ang), Math.Sin(ang), offset, speed * minutes * 60, c0);
                size = (float)mass;
                return Record(sim, kind, c0, cell, size);
            }
            case SeedKind.Cloud:
            {
                if (outside == null) return false;
                string k = g >= 1.5 && _rng.Chance(0.3) ? "kessler" : _rng.Chance(0.5) ? "rubble" : "dust";
                var d = T.Space.Debris[k];
                double minutes = _rng.Range(20, 60);
                double radius = _rng.Range(300_000, 800_000);
                double density = (k == "dust" ? 3e-6 : k == "rubble" ? 3e-7 : 6e-8) * g;
                double ang = _rng.Range(0, 2 * Math.PI);
                text = $"{d.Name}가 항로로 들어온다";
                int c0 = sim.Causes.Add(CauseKind.Story, text, sim.Tick);
                outside.AddCloud(k, d.Speed * minutes * 60 + radius, radius, density, Math.Cos(ang), Math.Sin(ang), c0);
                size = (float)density;
                return Record(sim, kind, c0, cell, size);
            }
            case SeedKind.Flare:
            {
                if (outside == null) return false;
                double minutes = _rng.Range(30, 120);
                text = "별의 표면이 끓어오르기 시작했다";
                int c0 = sim.Causes.Add(CauseKind.Story, text, sim.Tick);
                outside.ScheduleFlare(minutes, c0);
                return Record(sim, kind, c0, cell, (float)minutes);
            }
            case SeedKind.WornPart:
            {
                if (net == null || net.Devices.Count == 0) return false;
                // 돌고 있는 장치의 부품을 숨은 한계 가까이 낡게 한다 — 몇 시간 안에 저절로 고장 난다 (기계는 그 전에 끽끽거린다)
                var running = net.Devices.Where(d => net.On[d.Id] != 0 && Enum.GetValues<Part>().Any(p => d.Life(p) > 0 && !net.IsBroken(d.Id, p))).ToList();
                if (running.Count == 0) return false;
                var dev = running[_rng.Range(0, running.Count)];
                var parts = Enum.GetValues<Part>().Where(p => dev.Life(p) > 0 && !net.IsBroken(dev.Id, p)).ToList();
                var part = parts[_rng.Range(0, parts.Count)];
                double left = _rng.Range(1.0, 8.0) / g;
                text = $"{dev.Name}의 {Device.PartName(part)} 부품이 오래돼 닳았다";
                int c0 = sim.Causes.Add(CauseKind.Wear, text, sim.Tick, dev.Cell);
                net.AgeLeft(dev.Id, part, left, c0);
                return Record(sim, kind, c0, dev.Cell, (float)left);
            }
            case SeedKind.HullFatigue:
            {
                if (st == null) return false;
                var hull = Enumerable.Range(0, L.N).Where(i => L.Hull[i] && L.Kind[i] == CellKind.Wall && F.Hole[i] <= 0).ToList();
                if (hull.Count == 0) return false;
                int c = hull[_rng.Range(0, hull.Count)];
                // 숨은 피로: 1기압 부하가 정격의 0.8~0.95가 되게 — 끼익 소리가 먼저 · 가혹하면 금이 간다
                double load = Math.Min(0.97, 0.78 + 0.04 * g + _rng.Range(0.0, 0.05));
                double rating = st.Rating(c) / Math.Max(1e-3, (1 - st.Fatigue[c]) * (1 - st.Corrode[c]));
                double dp = 101.3;
                st.Fatigue[c] = (float)Math.Clamp(1 - dp / (rating * load), st.Fatigue[c], 0.95);
                text = "외판 한 곳에 보이지 않는 금이 자라고 있었다";
                int c0 = sim.Causes.Add(CauseKind.Wear, text, sim.Tick, c);
                st.DamageCause[c] = c0;
                return Record(sim, kind, c0, c, (float)load);
            }
            case SeedKind.Mold:
            {
                if (life == null) return false;
                var wet = L.Rooms.Where(r => r.Type is "Hydroponics" or "Garden" or "AlgaeLab" or "MushroomFarm" or "Laundry" or "WaterPlant" or "Galley").ToList();
                if (wet.Count == 0) wet = L.Rooms.Where(r => r.Cells.Length > 6).ToList();
                if (wet.Count == 0) return false;
                var room = wet[_rng.Range(0, wet.Count)];
                int c = room.Cells[_rng.Range(0, room.Cells.Length)];
                text = $"{room.Name} 구석에 곰팡이 포자가 내려앉았다";
                int c0 = sim.Causes.Add(CauseKind.Biology, text, sim.Tick, c);
                foreach (int x in room.Cells) if (Math.Abs(L.X(x) - L.X(c)) <= 1 && Math.Abs(L.Y(x) - L.Y(c)) <= 1) life.Mold[x] = Math.Max(life.Mold[x], (float)(0.2 * g));
                return Record(sim, kind, c0, c, (float)(0.2 * g));
            }
            case SeedKind.ArcRisk:
            {
                if (net == null || net.Feeders.Count == 0) return false;
                // 차단기가 눌어붙고 · 그 방 장치의 봉함이 낡았다 — 물이 새어 전선을 적시면 합선이 아크로 이어진다
                var withSeal = net.Devices.Where(d => d.Feeder >= 0 && d.Has(Part.Seal) && !net.IsBroken(d.Id, Part.Seal)).ToList();
                if (withSeal.Count == 0) return false;
                var dev = withSeal[_rng.Range(0, withSeal.Count)];
                text = $"{dev.Name}의 봉함이 삭았고, 그 방 차단기는 눌어붙어 있었다";
                int c0 = sim.Causes.Add(CauseKind.Wear, text, sim.Tick, dev.Cell);
                net.BreakerStuck[dev.Feeder] = 1;
                net.AgeLeft(dev.Id, Part.Seal, _rng.Range(3.0, 12.0) / g, c0);
                return Record(sim, kind, c0, dev.Cell, 1);
            }
        }
        return false;
    }

    private bool Record(Sim sim, SeedKind kind, int cause, int cell, float size)
    {
        Seeds.Add(new Seed { Tick = sim.Tick, Kind = kind, Cause = cause, Cell = cell, Size = size });
        LastSeedTick = sim.Tick; LastKind = kind;
        sim.Facts.Emit(sim.Tick, FactKind.StorySeed, cell, size, cause, -1, (int)kind);
        return true;
    }

    public static string SeedName(SeedKind k) => k switch
    {
        SeedKind.Rock => "운석", SeedKind.Cloud => "파편 구름", SeedKind.Flare => "별 폭발", SeedKind.WornPart => "낡은 부품",
        SeedKind.HullFatigue => "외판 피로", SeedKind.Mold => "곰팡이", SeedKind.ArcRisk => "합선 불씨", _ => "",
    };

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        _rng.Save(w);
        w.Bool(Enabled); w.Byte(Level); w.Byte((byte)Style);
        w.F64(Tension); w.I64(LastSeedTick); w.Byte((byte)LastKind); w.List(Seeds); w.I64(_factCursor);
    }

    public void Load(StateReader r)
    {
        _rng.Load(r);
        Enabled = r.Bool(); Level = r.Byte(); Style = (StoryStyle)r.Byte();
        Tension = r.F64(); LastSeedTick = r.I64(); LastKind = (SeedKind)r.Byte(); Seeds = r.List<Seed>(); _factCursor = r.I64();
    }
}
