using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>이야기꾼의 성격.</summary>
public enum StoryStyle : byte { Balanced, Gentle, Relentless, SlowBurn, Cosmic, Human }

/// <summary>이야기꾼이 심는 원인의 종류.</summary>
public enum SeedKind : byte { None, Rock, Cloud, Flare, WornPart, HullFatigue, Mold, ArcRisk, Mistake, OldGrudge, Cold, Injury, Crush, HiddenFault, GoodNews }

/// <summary>심은 원인 하나 (기록).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Seed
{
    public long Tick;
    public SeedKind Kind;
    public int Cause;      // 원인 마디 (뿌리)
    public int Cell;       // 자리 (-1: 배 밖)
    public float Size;
    public byte Act;       // 심은 막 (조짐 · 절정 · 여파)
    public byte Fruit;     // 일이 되었나 (원인 사슬의 뿌리가 이 씨앗인 볼 만한 사실)
    public byte Judged;    // 헛돌았나 · 열매를 맺었나를 따졌다 (배우기)
}

/// <summary>
/// ⑫ 이야기꾼 (docs/ENGINE.md §4 ⑫). 결과가 아니라 원인만 심는다 — 사건은 아래 엔진들이 알아서 만든다 (안 만들 수도 있다).
/// 지금 긴장(사실 흐름의 무게 · 반감기 2시간)이 성격의 목표 긴장보다 낮고 쉼이 지났으면 30분마다 하나 심는다. 가혹함 1~8 (기본 4 '아슬아슬').
/// </summary>
public sealed partial class Storyteller : IEngine
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
    /// <summary>심었지만 아직 터지지 않은 원인의 예상 긴장 (반감기 8시간) — 잠복 원인을 겹겹이 쌓아 한꺼번에 터지지 않게.</summary>
    public double Pending;
    public long LastSeedTick = long.MinValue / 2;
    public SeedKind LastKind;
    public List<Seed> Seeds = new();
    private long _factCursor;

    public Storyteller(Field f, ulong seed)
    {
        F = f; L = f.L; T = f.T;
        _rng = Rng.Stream(seed, "이야기꾼");
    }

    /// <summary>가혹함 이름 (함장이 고르는 판).</summary>
    public static string LevelName(int level) => level switch
    {
        1 => "평온한 항해", 2 => "잔잔함", 3 => "이따금", 4 => "아슬아슬", 5 => "거칠게", 6 => "가혹하게", 7 => "잔혹하게", _ => level >= 8 ? "끝까지" : "평온한 항해",
    };

    public static string StyleName(StoryStyle s) => s switch
    {
        StoryStyle.Balanced => "고른 이야기꾼", StoryStyle.Gentle => "잔잔한 이야기꾼", StoryStyle.Relentless => "몰아치는 이야기꾼",
        StoryStyle.SlowBurn => "느린 불씨", StoryStyle.Cosmic => "우주의 변덕", StoryStyle.Human => "사람의 이야기", _ => "",
    };

    /// <summary>성격 한 줄 (함장이 고르는 판) — 무엇을 심고 얼마나 쉬나.</summary>
    public static string StyleLine(StoryStyle s) => s switch
    {
        StoryStyle.Balanced => "조용한 때와 사고를 고르게 — 운석 · 낡은 부품 · 외판 피로 · 사람 사이의 일",
        StoryStyle.Gentle => "드물고 작게 — 오래 쉬고 · 낡은 부품 · 먼지구름이 주로",
        StoryStyle.Relentless => "쉴 틈 없이 — 운석 · 먼지구름 · 합선 불씨가 잇달아",
        StoryStyle.SlowBurn => "작은 조짐이 쌓인다 — 낡은 부품 · 외판 피로 · 곰팡이 · 합선 불씨",
        StoryStyle.Cosmic => "배 밖의 일 — 운석 · 먼지구름 · 별 폭발",
        StoryStyle.Human => "사람 사이의 일 — 사람의 실수 · 묵은 앙금 · 짝사랑 · 숨긴 실수",
        _ => "",
    };

    // ───────────────────────── 긴장 ─────────────────────────

    public static double Weight(FactKind k) => k switch
    {
        FactKind.Died => 10, FactKind.Collapsed => 4, FactKind.Hurt => 1, FactKind.WallBreach => 3, FactKind.Ignition => 2, FactKind.FireSpread => 1,
        FactKind.ComputerAlarm => 1, FactKind.Panicked => 2, FactKind.DoorBlown => 2, FactKind.MachineFail => 1, FactKind.Impact => 1,
        FactKind.Short => 2, FactKind.BreakerTrip => 0.5, FactKind.Flare => 2, FactKind.DebrisNear => 1,
        FactKind.Fight => 2, FactKind.Blamed => 0.5, FactKind.Grieved => 1, FactKind.Sick => 0.5, FactKind.Rationed => 1, FactKind.Confessed => 0.5, _ => 0,
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

    /// <summary>원인 하나를 심은 뒤 쉬는 시간 (시간) — 성격 · 가혹함에 따라.</summary>
    public double RestHours => Style switch { StoryStyle.Gentle => 8, StoryStyle.Relentless => 2, StoryStyle.SlowBurn => 5, _ => 4 } * 4.0 / Math.Max(1, (int)Level);

    public void Step(Sim sim, double dt)
    {
        // 사실 흐름을 긴장으로 · 큰 일 · 이야기의 실 · 씨앗의 열매
        foreach (var f in sim.Facts.Since(_factCursor)) { Tension += Weight(f.Kind); Notice(sim, f); }
        _factCursor = sim.Facts.Total;
        Tension *= Math.Exp(-dt * 0.6931 / (2 * 3600));
        Pending *= Math.Exp(-dt * 0.6931 / (8 * 3600));
        if (sim.Tick % ThinkEvery != ThinkEvery - 1) return;
        Think(sim);
    }

    /// <summary>원인 하나가 터지면 올 긴장 (가혹함 배율 전).</summary>
    public static double Potential(SeedKind k) => k switch
    {
        SeedKind.Rock => 1.5, SeedKind.Cloud => 2, SeedKind.Flare => 2, SeedKind.WornPart => 1, SeedKind.HullFatigue => 1.5, SeedKind.Mold => 0.5, SeedKind.ArcRisk => 2.5,
        SeedKind.Mistake => 1.5, SeedKind.OldGrudge => 1, SeedKind.Cold => 1, SeedKind.Injury => 1, SeedKind.Crush => 0.8, SeedKind.HiddenFault => 1, SeedKind.GoodNews => 0, _ => 1,
    };

    /// <summary>작은 씨앗 (조짐의 막) · 큰 씨앗 (절정의 막).</summary>
    public static bool Big(SeedKind k) => k is SeedKind.Rock or SeedKind.Cloud or SeedKind.Flare or SeedKind.HullFatigue or SeedKind.ArcRisk;

    /// <summary>다음에 심을 원인을 고른다 (심지는 않는다 — 시험이 고르는 버릇만 볼 때도 쓴다). set: 0 모두 · 1 작은 씨앗 · 2 큰 씨앗 ('사람의 이야기'는 사람 사이 씨앗도 절정에 쓴다).</summary>
    public SeedKind Choose(Sim sim, int set = 0)
    {
        // 성격마다 무게 (없음 · 운석 · 구름 · 폭발 · 낡은 부품 · 외판 피로 · 곰팡이 · 합선 불씨 · 사람의 실수 · 묵은 앙금 · 감기 · 다침 · 짝사랑 · 숨긴 실수 · 반가운 소식)
        double[] w = Style switch
        {
            StoryStyle.Cosmic => new double[] { 0, 3, 3, 2, 0.3, 0.3, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            StoryStyle.SlowBurn => new double[] { 0, 0.3, 0.3, 0.3, 3, 2, 2, 2, 1, 1, 1.5, 0.5, 1, 2, 0 },
            StoryStyle.Gentle => new double[] { 0, 0.5, 1, 1, 2, 1, 1, 0.3, 0.5, 0.5, 0.5, 0.5, 1, 0.5, 0 },
            StoryStyle.Human => new double[] { 0, 0.3, 0.5, 0.5, 1, 0.5, 1, 0.5, 3, 3, 1, 1, 3, 2.5, 0 },
            StoryStyle.Relentless => new double[] { 0, 2, 2, 1.5, 1.5, 1.5, 1, 2, 1, 1, 1, 1.5, 0.3, 1, 0 },
            _ => new double[] { 0, 1.5, 1.5, 1, 2, 1.5, 1, 1.5, 1, 1, 0.8, 1, 0.8, 0.8, 0 },
        };
        var mindForSeed = sim.Find<Mind>();
        var lifeForSeed = sim.Find<Life>();
        int people = mindForSeed?.Agents.Count(a => a.IsPerson) ?? 0;
        if (people < 1 || lifeForSeed == null) w[(int)SeedKind.Mistake] = w[(int)SeedKind.Cold] = w[(int)SeedKind.Injury] = w[(int)SeedKind.HiddenFault] = 0;
        if (people < 2 || sim.Find<Society>() == null) w[(int)SeedKind.OldGrudge] = w[(int)SeedKind.Crush] = 0;
        if (LastKind != SeedKind.None) w[(int)LastKind] *= 0.15; // 같은 종류를 연달아 피한다
        var net = sim.Find<Network>();
        if (net != null && net.BreakerStuck.Any(b => b != 0)) w[(int)SeedKind.ArcRisk] = 0; // 눌어붙은 차단기가 이미 있다
        for (int i = 1; i < w.Length && i < KindTrust.Length; i++) w[i] *= KindTrust[i];   // 이 배에서 헛돌던 씨앗은 덜 · 열매를 맺던 씨앗은 더
        if (set != 0)
        {
            var masked = (double[])w.Clone();
            bool human = Style == StoryStyle.Human;
            for (int i = 1; i < masked.Length; i++)
            {
                var k = (SeedKind)i;
                bool fit = set == 2 ? Big(k) || human && k is SeedKind.Mistake or SeedKind.OldGrudge or SeedKind.HiddenFault or SeedKind.Injury : !Big(k);
                if (!fit) masked[i] = 0;
            }
            if (masked.Sum() > 0) w = masked;   // 그 막에 맞는 씨앗이 이 배에 없으면 아무것이나
        }
        double sum = w.Sum(), r = _rng.Unit() * sum;
        for (int i = 1; i < w.Length; i++) { r -= w[i]; if (r <= 0) return (SeedKind)i; }
        return SeedKind.WornPart;
    }

    // ───────────────────────── 심기 ─────────────────────────

    /// <summary>원인 하나를 심는다 (시험 · 함장의 '이야기꾼 부르기'도 이것을 쓴다).</summary>
    /// <summary>네 이웃에 방 바닥이 있는 칸 (외판의 안쪽 면).</summary>
    private bool NextToRoom(int i)
    {
        int x = L.X(i), y = L.Y(i);
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (L.In(x + dx, y + dy) && L.Room[L.Index(x + dx, y + dy)] >= 0) return true;
        return false;
    }

    public bool Plant(Sim sim, SeedKind kind)
    {
        double g = Level / 4.0 * (Act == ActClimax ? ClimaxBoost : 1); // 가혹함 배율 (절정이면 더 — 항해가 길수록)
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
                text = $"{Ko.I(d.Name)} 항로로 들어온다";
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
                outside.ScheduleFlare(minutes, c0, g);   // 세기도 가혹함만큼
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
                // 기압을 받치는 외판만 (안쪽이 방인 칸 — 착륙 다리 · 날개 같은 뼈대는 압력을 받지 않는다)
                var hull = Enumerable.Range(0, L.N).Where(i => L.Hull[i] && L.Kind[i] == CellKind.Wall && F.Hole[i] <= 0 && NextToRoom(i)).ToList();
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
            case SeedKind.Mistake:
            {
                // 사람의 실수: 조리대를 켜 둔 채 자리를 떴다 — 그 위 음식이 눌어 불이 날 수 있다 (켠 원인이 불의 뿌리 → 다친 이 · 본 이가 그를 탓한다)
                var mind = sim.Find<Mind>();
                if (net == null || mind == null) return false;
                var stoves = net.Devices.Where(d => d.Def.Role == "stove" && net.On[d.Id] == 0 && !net.IsBroken(d.Id, Part.Electric)).ToList();
                var people = mind.Agents.Where(a => a.IsPerson && a.C.Up != 0).ToList();
                if (stoves.Count == 0 || people.Count == 0) return false;
                var stove = stoves[_rng.Range(0, stoves.Count)];
                // 덜 성실한 사람일수록 (조리사라면 더) · 지난 실수에서 배운 이는 덜 (⑧ 조심)
                var who = people.OrderBy(a => a.C.Consc + a.C.Careful + 0.3 * _rng.Unit() - (a.C.SkCook > 0.5f ? 0.2 : 0) - 0.3 * Math.Min(1, ThreadOf(a.Id))).First();
                Involve(who.Id, 1);
                text = $"{Ko.I(mind.NameOf(who.Id))} {Ko.Eul(stove.Name)} 켜 둔 채 자리를 떴다";
                int c0 = sim.Causes.Add(CauseKind.Person, text, sim.Tick, stove.Cell, 0, 0, who.Id);
                net.SetOn(stove.Id, true); net.OnCause[stove.Id] = c0; net.PanWater[stove.Id] = 0.4f * 55.5f; // 국 냄비 하나 (물 0.4L)
                return Record(sim, kind, c0, stove.Cell, 1);
            }
            case SeedKind.OldGrudge:
            {
                // 묵은 앙금: 거친 두 사람 사이의 지난 일 — 원한의 씨앗만 (따질지 · 풀지 · 터질지는 그들이 정한다)
                var mind = sim.Find<Mind>(); var so = sim.Find<Society>();
                if (mind == null || so == null) return false;
                var people = mind.Agents.Where(a => a.IsPerson && a.C.Up != 0).ToList();
                if (people.Count < 2) return false;
                var a1 = people.OrderBy(a => a.C.Agree + 0.4 * _rng.Unit() - 0.3 * Math.Min(1, ThreadOf(a.Id))).First();
                var a2 = people.Where(a => a.Id != a1.Id).OrderBy(a => a.C.Agree + 0.4 * _rng.Unit() - so.KnownOf(a1.Id, a.Id) * 0.3).First();
                string why = new[] { "지난 항구에서 크게 다툰 적이 있다", "예전 배에서 서로의 실수를 덮어 주지 않았다", "같은 자리를 두고 겨룬 적이 있다" }[_rng.Range(0, 3)];
                text = $"{Ko.Gwa(mind.NameOf(a1.Id))} {Ko.Eun(mind.NameOf(a2.Id))} {why}";
                int c0 = sim.Causes.Add(CauseKind.Person, text, sim.Tick, a1.C.Cell, 0, 0, a2.Id);
                float g0 = (float)(0.3 + 0.15 * g);
                so.Grudge[a1.Id * so.N + a2.Id] = Math.Max(so.Grudge[a1.Id * so.N + a2.Id], g0);
                so.Grudge[a2.Id * so.N + a1.Id] = Math.Max(so.Grudge[a2.Id * so.N + a1.Id], g0 * 0.6f);
                so.Remember(sim, a1.Id, Topic.Blame, a2.Id, c0, 0.7f, a1.C.Cell);
                so.Remember(sim, a2.Id, Topic.Blame, a1.Id, c0, 0.5f, a2.C.Cell);
                Involve(a1.Id, 1); Involve(a2.Id, 1);
                return Record(sim, kind, c0, a1.C.Cell, g0);
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
            case SeedKind.Cold or SeedKind.Injury or SeedKind.Crush or SeedKind.HiddenFault or SeedKind.GoodNews:
                return PlantPerson(sim, kind, g);
        }
        return false;
    }

    private bool Record(Sim sim, SeedKind kind, int cause, int cell, float size)
    {
        _seedOf[cause] = Seeds.Count;
        Seeds.Add(new Seed { Tick = sim.Tick, Kind = kind, Cause = cause, Cell = cell, Size = size, Act = Act });
        if (kind != SeedKind.GoodNews) { LastSeedTick = sim.Tick; LastKind = kind; }   // 반가운 소식은 막의 셈에 들지 않는다
        Pending += Potential(kind) * Level / 4.0;
        sim.Facts.Emit(sim.Tick, FactKind.StorySeed, cell, size, cause, -1, (int)kind);
        return true;
    }

    public static string SeedName(SeedKind k) => k switch
    {
        SeedKind.Rock => "운석", SeedKind.Cloud => "파편 구름", SeedKind.Flare => "별 폭발", SeedKind.WornPart => "낡은 부품",
        SeedKind.HullFatigue => "외판 피로", SeedKind.Mold => "곰팡이", SeedKind.ArcRisk => "합선 불씨", SeedKind.Mistake => "사람의 실수", SeedKind.OldGrudge => "묵은 앙금",
        SeedKind.Cold => "감기", SeedKind.Injury => "일하다 다침", SeedKind.Crush => "짝사랑", SeedKind.HiddenFault => "숨긴 실수", SeedKind.GoodNews => "반가운 소식", _ => "",
    };

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        _rng.Save(w);
        w.Bool(Enabled); w.Byte(Level); w.Byte((byte)Style);
        w.F64(Tension); w.F64(Pending); w.I64(LastSeedTick); w.Byte((byte)LastKind); w.List(Seeds); w.I64(_factCursor);
        SaveArc(w);
    }

    public void Load(StateReader r)
    {
        _rng.Load(r);
        Enabled = r.Bool(); Level = r.Byte(); Style = (StoryStyle)r.Byte();
        Tension = r.F64(); Pending = r.F64(); LastSeedTick = r.I64(); LastKind = (SeedKind)r.Byte(); Seeds = r.List<Seed>(); _factCursor = r.I64();
        LoadArc(r);
    }
}
