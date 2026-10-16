using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 큰 사고 여섯 (사용자가 고른 61 · 62 · 65 · 67 · 70 · 75 — docs/EXTREME_ENVIRONMENTS.md):
//  전자기 펄스 — 로봇 · 드론 · 주컴퓨터가 꺼지고 사람 손만 남는다 (전기 아는 사람이 한 대씩 손으로 되살린다).
//  원자로 노심 카운트다운 — 제어봉이 끼었다, 세 시간 안에 누가 들어가나 (로봇 먼저 → 없으면 용감한 기관사 → 못 하면 노심을 떼어 낸다).
//  작물 대역병 — 재배실 전체가 한꺼번에 시든다 (종자 보관소가 있으면 빨리 되살린다 · 없으면 남은 씨앗으로 격리 재배).
//  궤도에 붙잡힘 — 무게를 버려야 빠져나간다 (무엇을 버릴지 · 다투는 사람 · 모자라면 골조가 운다).
//  냉각 폭주 결빙 — 배 한쪽이 얼음 동굴이 된다 (배관 동파 · 문 끼임 · 갇힌 사람 · 로봇이 열을 끌어와 녹인다).
//  겹친 재난 — 이야기꾼이 사나우면 큰 사고가 둘 겹친다.

public enum BigKind : byte { Emp, CoreCountdown, Blight, OrbitTrap, FreezeRunaway, Compound }

public sealed class BigCase
{
    public int Id { get; init; }
    public BigKind Kind { get; init; }
    public long Start { get; init; }
    public long Deadline { get; set; } = -1;
    public int Stage { get; set; }
    public bool Done { get; set; }
    public string Outcome { get; set; } = "";
    public List<int> Robots { get; } = new();
    public List<int> Drones { get; } = new();
    public List<int> Rooms { get; } = new();
    public int Hero { get; set; } = -1;
    public int BotHero { get; set; } = -1;
    public long BotSince { get; set; } = -1;
    public int Dumped { get; set; }
    public int Need { get; set; }
}

public sealed class BigIncidentSystem
{
    public static bool Off;
    private readonly World _w;
    private readonly Rng _rng;
    private long _nextHour, _nextCheck;
    private int _nextId;
    public List<BigCase> Cases { get; } = new();
    public int[] Count { get; } = new int[6];

    public BigIncidentSystem(World w) { _w = w; _rng = new Rng(unchecked(w.Seed * 53 + 6061)); }

    public static string Name(BigKind k) => k switch
    {
        BigKind.Emp => "전자기 펄스", BigKind.CoreCountdown => "원자로 노심 카운트다운", BigKind.Blight => "작물 대역병", BigKind.OrbitTrap => "궤도에 붙잡힘",
        BigKind.FreezeRunaway => "냉각 폭주 결빙", _ => "겹친 재난",
    };

    public IEnumerable<BigCase> Open => Cases.Where(c => !c.Done);

    // ─────────────────────────────── 저절로 ───────────────────────────────

    private float RateMul => Off || Storyteller.Persona == StoryPersona.Off && HazardSystem.RandomDays <= 0f ? 0f : 0.4f + 0.1f * Storyteller.Level;

    public void Update(float dt)
    {
        var w = _w;
        if (Off) return;
        if (w.Tick >= _nextCheck) { _nextCheck = w.Tick + SimTime.Minutes(5); foreach (var c in Open.ToList()) Step(c); }
        if (w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        float mul = RateMul;
        if (mul <= 0f || Open.Any() || w.Tick - w.StartTickOf < SimTime.TicksPerDay * 2) return;
        var env = w.Env.Now;
        float day(float p) => p / 24f * mul;
        var opts = new List<(BigKind k, float p)>
        {
            (BigKind.Emp, day(0.006f + 0.05f * MathF.Max(env[EnvMech.Magnetic], env[EnvMech.Lightning]))),
            (BigKind.CoreCountdown, day(0.004f + 0.02f * env[EnvMech.Heat] + (w.Ship.FurnitureOf(FurnitureType.ReactorCore).Any(f => f.Machine?.Wear > 0.6f) ? 0.01f : 0f))),
            (BigKind.Blight, day(0.006f + 0.04f * MathF.Max(env[EnvMech.Bio], MathF.Max(env[EnvMech.Radiation], env[EnvMech.Dark])))),
            (BigKind.OrbitTrap, day(w.Planet.Holding && w.Planet.Here is Planet pl && (pl.Gravity > 1.5f || pl.Body == PlanetBody.GasGiant) || w.Voyage.Current.Kind == LegKind.GasGiant ? 0.08f : 0f)),
            (BigKind.FreezeRunaway, day(0.004f + 0.05f * env[EnvMech.Freeze])),
        };
        foreach (var (k, p) in opts)
            if (_rng.Chance(p))
            {
                Start(k);
                if ((Storyteller.Level >= 7 || Storyteller.Persona is StoryPersona.Vengeful or StoryPersona.Climax or StoryPersona.Siege) && _rng.Chance(0.25f)) Start(BigKind.Compound);
                break;
            }
    }

    // ─────────────────────────────── 시작 ───────────────────────────────

    public BigCase? Start(BigKind k)
    {
        var w = _w;
        if (k == BigKind.Compound)
        {
            var first = Open.FirstOrDefault()?.Kind;
            var pool = new[] { BigKind.Emp, BigKind.CoreCountdown, BigKind.Blight, BigKind.FreezeRunaway }.Where(x => x != first).ToList();
            var second = pool[_rng.Range(0, pool.Count)];
            if (first == null) { first = pool[_rng.Range(0, pool.Count)]; pool.Remove(first.Value); second = pool[_rng.Range(0, pool.Count)]; Start(first.Value); }
            var cc = new BigCase { Id = ++_nextId, Kind = BigKind.Compound, Start = w.Tick, Done = true, Outcome = $"{Name(first.Value)} + {Name(second)}" };
            Cases.Add(cc);
            Count[(int)BigKind.Compound]++;
            w.RaiseAlert($"겹친 재난 — {cc.Outcome}", null, AlertLevel.Critical, shipWide: true);
            w.History.Add(w, HistoryKind.Incident, $"겹친 재난 — {Name(first.Value)}에 {Ko.IGa(Name(second))} 겹쳤다");
            Start(second);
            if (!w.Env.Now.Kind.Equals(EnvKind.None)) w.Env.Begin(IncidentScale.Ship, null, null, now: true);
            return cc;
        }
        var c = new BigCase { Id = ++_nextId, Kind = k, Start = w.Tick };
        bool ok = k switch
        {
            BigKind.Emp => BeginEmp(c),
            BigKind.CoreCountdown => BeginCore(c),
            BigKind.Blight => BeginBlight(c),
            BigKind.OrbitTrap => BeginOrbit(c),
            BigKind.FreezeRunaway => BeginFreeze(c),
            _ => false,
        };
        if (!ok) return null;
        Cases.Add(c);
        Count[(int)k]++;
        return c;
    }

    private void Finish(BigCase c, string outcome)
    {
        var w = _w;
        c.Done = true;
        c.Outcome = outcome;
        long hours = (w.Tick - c.Start) / SimTime.TicksPerHour;
        w.History.Add(w, HistoryKind.Incident, $"{Name(c.Kind)} — {outcome} ({hours}시간)");
        w.Log.Add(w.Tick, LogKind.Ship, $"{Name(c.Kind)} 끝 — {outcome}");
    }

    private IEnumerable<CrewMember> Aboard => _w.Crew.Where(x => !x.Dead && !x.Away && !x.LeftShip);
    private static float SkillOf(CrewMember c, Skill s) => c.RawSkill(s);

    // ─────────────────────────────── 전자기 펄스 ───────────────────────────────

    private bool BeginEmp(BigCase c)
    {
        var w = _w;
        foreach (var r in w.Robots.Robots.Where(r => !r.Wrecked && r.State != RobotState.Lost && !r.Disabled)) { r.Disabled = true; r.Doing = "전자기 펄스 — 꺼졌다"; c.Robots.Add(r.Id); }
        foreach (var d in w.Drones.Drones.Where(d => d.State != DroneState.Lost)) c.Drones.Add(d.Id);
        w.Automation.Reboot("전자기 펄스 — 회로가 다 튀었다", 60f * 14f);
        for (int i = 0; i < 2; i++) w.Hazards.FireStory("LightsOut", null);
        w.RaiseAlert("전자기 펄스 — 로봇 · 드론 · 주컴퓨터가 꺼졌다 (사람 손만 남았다)", null, AlertLevel.Critical, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"전자기 펄스 — 로봇 {c.Robots.Count}대 · 주컴퓨터가 꺼졌다");
        foreach (var p in Aboard) p.Needs.Stress = MathF.Min(1f, p.Needs.Stress + 0.12f);
        return c.Robots.Count > 0;
    }

    private void StepEmp(BigCase c)
    {
        var w = _w;
        if (w.Tick % SimTime.Hours(1) >= SimTime.Minutes(5)) return; // 한 시간에 한 번
        var hands = Aboard.Where(p => p.CanAct && p.IsAwake && !p.Outside).OrderByDescending(p => SkillOf(p, Skill.Electrical)).ThenBy(p => p.Id).Take(3).ToList();
        int n = 0;
        foreach (var h in hands)
        {
            if (SkillOf(h, Skill.Electrical) < 0.25f && n > 0) break;
            var id = c.Robots.FirstOrDefault(rid => w.Robots.Robots.FirstOrDefault(r => r.Id == rid) is Robot r && r.Disabled);
            if (!c.Robots.Any(rid => w.Robots.Robots.FirstOrDefault(r => r.Id == rid)?.Disabled == true)) break;
            var bot = w.Robots.Robots.First(r => r.Id == id);
            bot.Disabled = false;
            bot.Doing = "대기";
            MarkLog.Add(bot.Marks, w.Tick, $"{h.Name}: 손으로 다시 켰다 (전자기 펄스)");
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(h.Name)} {Ko.EulReul(bot.Name)} 손으로 다시 켰다 — 기판을 갈고 하나씩");
            n++;
        }
        if (hands.Count > 0 && w.Tick - c.Start < SimTime.Hours(3)) Life.Diary(w, hands[0], "로봇도 컴퓨터도 없다. 손전등을 입에 물고 펌프를 돌렸다.");
        bool robots = !c.Robots.Any(rid => w.Robots.Robots.FirstOrDefault(r => r.Id == rid)?.Disabled == true);
        if (robots && !w.Automation.Rebooting) Finish(c, $"사람 손으로 로봇 {c.Robots.Count}대를 다시 켰다");
        else if (w.Tick - c.Start > SimTime.Hours(48)) { foreach (var rid in c.Robots) if (w.Robots.Robots.FirstOrDefault(r => r.Id == rid) is Robot r) r.Disabled = false; Finish(c, "이틀 만에 모두 되살렸다"); }
    }

    // ─────────────────────────────── 원자로 노심 카운트다운 ───────────────────────────────

    private Furniture? Reactor => _w.Ship.FurnitureOf(FurnitureType.ReactorCore).FirstOrDefault(f => !f.Room.Detached && f.Machine != null);

    private bool BeginCore(BigCase c)
    {
        var w = _w;
        if (Reactor is not Furniture core) return false;
        w.Hazards.FireStory("ReactorTransient", core.Room);
        if (!core.Machine!.Faults.Any(f => f.Kind == FaultKind.ControlFault)) w.Machines.Break(core.Machine, FaultKind.ControlFault);
        c.Deadline = w.Tick + SimTime.Hours(3);
        c.Rooms.Add(core.Room.Id);
        w.RaiseAlert("원자로 노심 이상 — 제어봉이 끼었다, 세 시간 안에 손으로 넣어야 한다", core.Room, AlertLevel.Critical, shipWide: true);
        w.Automation.Reason("core", "노심 이상 — 세 시간. 로봇을 먼저 보낸다. 못 하면 사람에게 부탁한다. 끝내 못 하면 노심을 떼어 낸다", SimTime.Hours(3));
        return true;
    }

    private void StepCore(BigCase c)
    {
        var w = _w;
        if (Reactor is not Furniture core) { Finish(c, "원자로가 없다"); return; }
        var m = core.Machine!;
        var fault = m.Faults.FirstOrDefault(f => f.Kind == FaultKind.ControlFault);
        if (fault == null)
        {
            if (c.Hero >= 0 && w.Crew.FirstOrDefault(x => x.Id == c.Hero) is CrewMember hero) { hero.Dose += 2.2f; Life.Diary(w, hero, "노심에 들어갔다 나왔다. 손이 아직도 떨린다."); }
            Finish(c, c.BotHero >= 0 ? "로봇이 노심에 들어가 제어봉을 넣었다" : c.Hero >= 0 ? $"{w.Crew.First(x => x.Id == c.Hero).Name}이 노심에 들어가 제어봉을 넣었다" : "제어봉을 넣었다");
            return;
        }
        // 1) 로봇 먼저 (방사선에 강한 몸)
        if (c.BotHero < 0 && w.Tick - c.Start > SimTime.Minutes(15))
        {
            if (w.Robots.SendCheck(core.Room, "노심에 들어간다 — 제어봉 손으로", out var bot)) { c.BotHero = bot.Id; c.BotSince = -1; w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {Ko.EulReul(bot.Name)} 노심으로 보낸다"); }
        }
        if (c.BotHero >= 0 && w.Robots.Robots.FirstOrDefault(r => r.Id == c.BotHero) is Robot rb)
        {
            if (rb.Room == core.Room && rb.Operational) { if (c.BotSince < 0) c.BotSince = w.Tick; }
            if (c.BotSince >= 0 && w.Tick - c.BotSince >= SimTime.Minutes(25))
            {
                m.Faults.Remove(fault);
                w.Robots.Hurt(rb, BotPart.Brain, 0.7f, "노심 방사선");
                w.Robots.Hurt(rb, BotPart.Sensor, 0.6f, "노심 방사선");
                w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(rb.Name)} 노심에 들어가 제어봉을 넣었다 — 몸이 방사선에 탔다");
                w.Board.RequestScan();
                return;
            }
            if (!rb.Operational) c.BotHero = -2; // 가다 멈췄다 — 사람에게
        }
        // 2) 한 시간 남으면 사람이 간다 (용감한 기관사)
        if (c.Hero < 0 && c.Deadline - w.Tick < SimTime.Hours(1))
        {
            var hero = Aboard.Where(p => p.CanAct && !p.IsChild).OrderByDescending(p => SkillOf(p, Skill.Mechanics) + p.Traits.Bravery).ThenBy(p => p.Id).FirstOrDefault();
            if (hero != null)
            {
                c.Hero = hero.Id;
                w.Automation.Reason("core-hero", $"{hero.Name} 씨가 노심에 들어가겠답니다 — 15분 안에 나오세요", SimTime.Hours(1));
                w.Log.Add(w.Tick, LogKind.Life, Persona.Say(hero, "내가 간다. 다들 뒤로 물러나 있어"), hero.Id);
                hero.HoldUntil = w.Tick + SimTime.Minutes(15);
                hero.HoldWhy = "노심 안 — 제어봉을 손으로";
                m.Faults.Remove(fault);
                foreach (var p in Aboard.Where(p => p != hero)) w.Brain2.Emotions.Feel(p, Feeling.Fear, 0.3f, "노심에 들어간 사람", hero);
                return;
            }
        }
        // 3) 시간이 다 됐다 — 노심을 떼어 낸다 (며칠 배터리 · 보조 발전기)
        if (w.Tick >= c.Deadline)
        {
            m.Wear = 1f;
            w.Machines.Break(m, FaultKind.ControlFault);
            foreach (var p in Aboard.Where(p => p.Room == core.Room)) p.Dose += 1f;
            w.RaiseAlert("노심을 떼어 냈다 — 며칠 배터리 · 보조 발전기로 버틴다", core.Room, AlertLevel.Critical, shipWide: true);
            Finish(c, "노심을 떼어 냈다 (원자로 없이 며칠)");
        }
    }

    // ─────────────────────────────── 작물 대역병 ───────────────────────────────

    private static readonly RoomType[] CropRooms = { RoomType.Hydroponics, RoomType.Garden, RoomType.AlgaeLab, RoomType.MushroomFarm, RoomType.ProteinFarm };

    private bool BeginBlight(BigCase c)
    {
        var w = _w;
        var rooms = w.Ship.Rooms.Where(r => !r.Detached && CropRooms.Contains(r.Kind)).ToList();
        if (rooms.Count == 0) return false;
        int hit = 0;
        foreach (var r in rooms) { c.Rooms.Add(r.Id); if (w.Hazards.FireStory("CropBlight", r) != null) hit++; w.Hazards.FireStory("SeedRot", r); }
        bool vault = w.Ship.KindOf(RoomType.SeedVault).Any();
        c.Deadline = w.Tick + SimTime.Hours(vault ? 36 : 72);
        w.RaiseAlert($"작물 대역병 — 재배실 {rooms.Count}곳이 한꺼번에 시든다", rooms[0], AlertLevel.Critical, shipWide: true);
        w.Automation.Reason("blight", vault ? "종자 보관소의 씨앗으로 다시 심는다 — 하루 반" : "종자 보관소가 없다 — 남은 씨앗을 아껴 격리 재배 (사흘 · 식사를 줄인다)", SimTime.Hours(12));
        foreach (var p in Aboard) p.Needs.Stress = MathF.Min(1f, p.Needs.Stress + 0.08f);
        return hit > 0 || rooms.Count > 0;
    }

    private void StepBlight(BigCase c)
    {
        var w = _w;
        if (w.Tick % SimTime.Hours(12) < SimTime.Minutes(5) && w.Tick < c.Deadline)
        {
            var cook = Aboard.FirstOrDefault(p => p.CanAct && p.IsAwake);
            if (cook != null) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(cook, "오늘도 죽 한 그릇이다 — 다음 수확까지 버티자"), cook.Id);
        }
        if (w.Tick >= c.Deadline) Finish(c, w.Ship.KindOf(RoomType.SeedVault).Any() ? "종자 보관소 씨앗으로 다시 심었다" : "남은 씨앗으로 격리 재배 — 첫 싹이 났다");
    }

    // ─────────────────────────────── 궤도에 붙잡힘 ───────────────────────────────

    private static readonly ItemKind[] Ballast = { ItemKind.MetalOre, ItemKind.Silicate, ItemKind.Plate, ItemKind.Carbon, ItemKind.Ice, ItemKind.Structure, ItemKind.Rare, ItemKind.Electronics };

    private bool BeginOrbit(BigCase c)
    {
        var w = _w;
        c.Need = 18 + _rng.Range(0, 14);
        c.Deadline = w.Tick + SimTime.Hours(6);
        w.RaiseAlert("궤도에 붙잡혔다 — 무게를 버려야 빠져나간다", null, AlertLevel.Critical, shipWide: true);
        w.Automation.Reason("orbit", $"끌려 내려간다 — 여섯 시간 안에 짐 {c.Need}개만큼 버려야 한다. 무게 버리기 회의를 연다", SimTime.Hours(6));
        return true;
    }

    private void StepOrbit(BigCase c)
    {
        var w = _w;
        if (c.Stage == 0)
        {
            // 회의: 덜 아까운 것부터 · 다투는 사람
            c.Stage = 1;
            var keeper = Aboard.OrderByDescending(p => p.Value == CrewValue.Safety ? 1 : 0).ThenBy(p => p.Id).FirstOrDefault();
            var saver = Aboard.Where(p => p != keeper).OrderByDescending(p => SkillOf(p, Skill.Mechanics)).ThenBy(p => p.Id).FirstOrDefault();
            if (keeper != null) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(keeper, "살아야 짐도 있지 — 다 버리자"), keeper.Id);
            if (saver != null) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(saver, "희귀 광물만은 남기자 — 그거 없으면 다음 수리를 못 한다"), saver.Id);
        }
        if (w.Tick % SimTime.Hours(1) < SimTime.Minutes(5) && c.Dumped < c.Need)
        {
            var thrown = new List<string>();
            foreach (var k in Ballast)
            {
                if (c.Dumped >= c.Need) break;
                int n = 0;
                while (c.Dumped < c.Need && n < 6 && w.Ship.CountStored(k) > (k == ItemKind.Rare ? 2 : 0) && ItemsV15.Use(w, k)) { n++; c.Dumped++; }
                if (n > 0) thrown.Add($"{ItemKinds.Name(k)} {n}");
            }
            if (c.Dumped < c.Need && w.Water.Level > 120f) { float wl = MathF.Min(60f, w.Water.Level - 120f); w.Water.Level -= wl; c.Dumped += (int)(wl / 10f); thrown.Add($"물 {wl:0}L"); }
            if (thrown.Count > 0) w.Log.Add(w.Tick, LogKind.Ship, $"무게를 버렸다 — {string.Join(" · ", thrown)} ({c.Dumped}/{c.Need})");
        }
        if (c.Dumped >= c.Need) { Finish(c, $"짐 {c.Dumped}개를 버리고 빠져나왔다"); return; }
        if (w.Tick >= c.Deadline)
        {
            w.Hazards.FireStory("FrameCreak", null);
            w.Hazards.FireStory("HullCrack", null);
            Finish(c, $"버린 짐이 모자랐다 ({c.Dumped}/{c.Need}) — 골조가 울며 겨우 빠져나왔다");
        }
    }

    // ─────────────────────────────── 냉각 폭주 결빙 ───────────────────────────────

    private bool BeginFreeze(BigCase c)
    {
        var w = _w;
        var start = w.Ship.Rooms.Where(r => !r.Detached && r.Kind is RoomType.Cooling or RoomType.HeatStorage or RoomType.Freezer).OrderBy(r => r.Id).FirstOrDefault()
                    ?? w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor).OrderBy(_ => _rng.Float()).FirstOrDefault();
        if (start == null) return false;
        var rooms = new List<Room> { start };
        foreach (var d in w.Ship.Doors)
        {
            if (rooms.Count >= 3) break;
            var other = d.RoomA == start ? d.RoomB : d.RoomB == start ? d.RoomA : null;
            if (other != null && !other.Detached && other.Type != RoomType.Corridor && !rooms.Contains(other)) rooms.Add(other);
        }
        foreach (var r in rooms)
        {
            c.Rooms.Add(r.Id);
            r.Air.Temperature = -30f - _rng.Range(0f, 10f);
            w.Hazards.FireStory("PipeFreeze", r);
            MarkLog.Add(r.Marks, w.Tick, "얼음 동굴이 됐다 (냉각 폭주)");
        }
        w.Hazards.FireStory("DoorJam", start);
        var trapped = Aboard.Where(p => p.Room != null && rooms.Contains(p.Room)).ToList();
        w.RaiseAlert($"냉각 폭주 — {string.Join(" · ", rooms.Select(r => r.Name))}이 얼어붙었다" + (trapped.Count > 0 ? $" (갇힌 사람 {trapped.Count})" : ""), start, AlertLevel.Critical, shipWide: true);
        foreach (var p in trapped) { w.Ailments.Catch(p, "hypothermia", null, "냉각 폭주"); Life.Diary(w, p, "숨이 하얗게 언다. 문이 안 열린다."); }
        return true;
    }

    private void StepFreeze(BigCase c)
    {
        var w = _w;
        var rooms = c.Rooms.Select(id => w.Ship.Rooms.FirstOrDefault(r => r.Id == id)).Where(r => r != null && !r.Detached).Cast<Room>().ToList();
        foreach (var r in rooms)
        {
            // 로봇이 열을 끌어온다 (쉬는 로봇이 있으면 · 없으면 천천히)
            bool bot = w.Robots.Robots.Any(rb => rb.Room == r && rb.Operational && rb.Steps != null);
            if (!bot && c.Stage < 6) { w.Robots.SendCheck(r, "얼음 녹이기 — 열을 끌어온다", out _); c.Stage++; }
            r.Air.Temperature = MathF.Min(18f, r.Air.Temperature + (bot ? 2.2f : 0.8f));
        }
        if (rooms.All(r => r.Air.Temperature > 8f)) Finish(c, "얼음 동굴이 녹았다");
        else if (w.Tick - c.Start > SimTime.Hours(30)) { foreach (var r in rooms) r.Air.Temperature = 12f; Finish(c, "하루 넘게 걸려 녹였다"); }
    }

    // ─────────────────────────────── 진행 ───────────────────────────────

    private void Step(BigCase c)
    {
        switch (c.Kind)
        {
            case BigKind.Emp: StepEmp(c); break;
            case BigKind.CoreCountdown: StepCore(c); break;
            case BigKind.Blight: StepBlight(c); break;
            case BigKind.OrbitTrap: StepOrbit(c); break;
            case BigKind.FreezeRunaway: StepFreeze(c); break;
        }
    }

    public string? Line() => Open.FirstOrDefault() is BigCase c
        ? $"{Name(c.Kind)}" + (c.Deadline > 0 ? $" · {Math.Max(0, (c.Deadline - _w.Tick) / SimTime.Minutes(1))}분 남음" : $" · {(_w.Tick - c.Start) / SimTime.TicksPerHour}시간째")
        : null;

    public void Hash(Action<long> I, Action<float> F) { I(Cases.Count); I(Open.Count()); }
}
