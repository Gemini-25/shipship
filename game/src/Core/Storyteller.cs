using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.4 이야기꾼: 배의 연쇄 피해(긴장)와 회복 여력을 보고 언제·무엇을 일으킬지 정한다.
//  꾸준형 — 고르게, 거의 회복하면 다음 / 몰아치기형 — 오래 조용하다 한꺼번에 / 무작위형 — 예측 불가 / 시험관형 — 배의 급소를 노린다.
// 기본 규칙: 회복할 틈은 주되, 완전히 회복하기 전에. 난이도는 빈도·크기·시작 물자·부상 강도·작은 이상 빈도를 바꾼다.
// 수치(성격·난이도)는 밸런스 수치로 저장·재생에 남는다 → 같은 시드면 같은 이야기.
// v15.7 성격 넷 더 (StorytellerV15.cs): 느린 불씨형 · 계절형 · 자비형 · 앙갚음형.

public enum StoryPersona { Off, Steady, Burst, Random, Tester, SlowBurn, Seasonal, Merciful, Vengeful }

public sealed partial class Storyteller
{
    /// <summary>0 끔(예전 무작위 사고) · 1 꾸준형 · 2 몰아치기형 · 3 무작위형 · 4 시험관형 · 5 느린 불씨형 · 6 계절형 · 7 자비형 · 8 앙갚음형.</summary>
    public static float PersonaValue;
    /// <summary>난이도 1(느긋) ~ 5(가혹), 기본 3.</summary>
    public static float LevelValue = 3f;

    public static StoryPersona Persona => (StoryPersona)Math.Clamp((int)MathF.Round(PersonaValue), 0, (int)StoryPersona.Vengeful);
    public static int Level => Math.Clamp((int)MathF.Round(LevelValue), 1, 5);

    public static string PersonaName(StoryPersona p) => p switch
    {
        StoryPersona.Steady => "꾸준형", StoryPersona.Burst => "몰아치기형", StoryPersona.Random => "무작위형", StoryPersona.Tester => "시험관형",
        StoryPersona.SlowBurn => "느린 불씨형", StoryPersona.Seasonal => "계절형", StoryPersona.Merciful => "자비형", StoryPersona.Vengeful => "앙갚음형", _ => "끔",
    };
    public static string LevelName(int l) => l switch { 1 => "느긋", 2 => "쉬움", 3 => "보통", 4 => "어려움", _ => "가혹" };

    // 난이도가 바꾸는 것
    public static float GapDays => new[] { 3.0f, 2.0f, 1.4f, 1.0f, 0.7f }[Level - 1];
    public static float BigScale => new[] { 0.5f, 0.75f, 1f, 1.4f, 1.8f }[Level - 1];
    public static float StockScale => Persona == StoryPersona.Off ? 1f : new[] { 1.4f, 1.2f, 1f, 0.85f, 0.7f }[Level - 1];
    public static float InjuryScale => Persona == StoryPersona.Off ? 1f : new[] { 0.7f, 0.85f, 1f, 1.15f, 1.3f }[Level - 1];
    public static float AnomalyScale => Persona == StoryPersona.Off ? 1f : new[] { 0.6f, 0.8f, 1f, 1.3f, 1.6f }[Level - 1];

    private readonly World _w;
    private readonly Rng _rng;
    public long Next { get; private set; } = -1;
    public int Fired { get; internal set; }
    public int BurstLeft { get; private set; }
    public string LastWhy { get; private set; } = "";
    public string LastWhat { get; private set; } = "";
    public List<(long tick, string what, string why)> Journal { get; } = new();

    public Storyteller(World w, int seed)
    {
        _w = w;
        _rng = new Rng(seed * 2654435 + 97);
    }

    // ─────────────────────────────── 배를 읽는다 ───────────────────────────────

    /// <summary>연쇄 피해 0~1.5: 번지는 중인 사고 고리 · 쓰러진 사람 · 캄캄한 방 · 원자로 정지.</summary>
    public float Tension()
    {
        var w = _w;
        var log = w.Causes;
        int serious = 0;
        foreach (var inc in log.Incidents)
        {
            if (!inc.Open) continue;
            foreach (var id in inc.Nodes)
                if (log.Node(id) is { Open: true } n && n.Kind is CauseKind.Fire or CauseKind.Breach or CauseKind.Flood or CauseKind.Suffocation
                        or CauseKind.Gas or CauseKind.Scram or CauseKind.Casualty or CauseKind.Illness or CauseKind.Outage) serious++;
        }
        int crew = Math.Max(1, w.Crew.Count(c => !c.Dead));
        float down = w.Crew.Count(c => !c.Dead && (c.Down || c.CareBed != null)) / (float)crew;
        var rooms = w.Ship.LiveRooms.Where(r => !r.Detached).ToList();
        float dark = rooms.Count == 0 ? 0f : rooms.Count(r => !r.Powered) / (float)rooms.Count;
        return MathF.Min(1.5f, 0.12f * serious + 0.6f * down + 0.5f * dark + (w.Power.ReactorOnline ? 0f : 0.25f));
    }

    /// <summary>회복 여력 0~1: 핵심 예비 부품 · 실링폼·금속판 · 움직일 수 있는 사람 · 배터리·공기·물·먹을 것.</summary>
    public float Capacity()
    {
        var w = _w;
        var ship = w.Ship;
        float parts = new[] { ItemKind.Pump, ItemKind.Bearing, ItemKind.Filter, ItemKind.Cable, ItemKind.PowerController }
            .Average(k => MathF.Min(1f, ship.CountStored(k) / 2f));
        float repair = (MathF.Min(1f, ship.CountStored(ItemKind.Sealant) / 8f) + MathF.Min(1f, ship.CountStored(ItemKind.Plate) / 6f)) * 0.5f;
        int crew = Math.Max(1, w.Crew.Count(c => !c.Dead));
        float able = w.Crew.Count(c => c.CanAct) / (float)crew;
        float life = (w.Power.BatteryCapacity > 0 ? MathF.Min(1f, w.Power.BatteryCharge / w.Power.BatteryCapacity) : 0.5f) * 0.25f
                     + (w.Air.ReserveCapacity > 0 ? w.Air.Reserve / w.Air.ReserveCapacity : 0.5f) * 0.25f
                     + (w.Water.Capacity > 0 ? MathF.Min(1f, w.Water.Level / w.Water.Capacity) : 0.5f) * 0.25f
                     + MathF.Min(1f, ship.CountStored(ItemKind.Meal) / (crew * 6f)) * 0.25f;
        return Math.Clamp(0.3f * parts + 0.2f * repair + 0.3f * able + 0.2f * life, 0f, 1f);
    }

    // ─────────────────────────────── 언제 ───────────────────────────────

    public void Update()
    {
        var w = _w;
        var persona = Persona;
        if (persona == StoryPersona.Off) { Next = -1; return; }
        float gap = GapDays;
        if (Next < 0) { Next = Math.Max(w.Tick, w.StartTickOf + SimTime.TicksPerDay) + GapTicks(gap, persona); return; }
        if (w.Tick < Next) return;
        float t = Tension(), cap = Capacity();
        // 자비: 이미 배가 무너지는 중이면 (가혹이 아니면) 기다린다
        float mercy = Level >= 5 ? 1.4f : Level == 4 ? 1.0f : 0.8f;
        switch (persona)
        {
            case StoryPersona.Steady:
                // 거의 회복하면 다음 (완전히 회복하기 전에)
                if (t > 0.25f) { Next = w.Tick + SimTime.Hours(1); return; }
                break;
            case StoryPersona.Burst:
                if (BurstLeft == 0 && t > 0.3f) { Next = w.Tick + SimTime.Hours(1); return; }
                if (t > mercy) { Next = w.Tick + SimTime.Hours(1); return; }
                break;
            case StoryPersona.Random:
                if (t > mercy + 0.2f) { Next = w.Tick + SimTime.Hours(1); return; }
                break;
            case StoryPersona.Tester:
                if (t > (Level >= 4 ? 0.35f : 0.2f)) { Next = w.Tick + SimTime.Hours(1); return; }
                break;
            default:
                if (Hold(persona, t, cap)) { Next = w.Tick + SimTime.Hours(1); return; } // v15.7
                break;
        }
        if (w.Scale.Breather(persona, out var rest)) { Next = w.Tick + SimTime.Hours(1); LastWhy = rest; return; } // v16.18 큰 사고 뒤 숨 돌릴 틈
        var (key, room, why) = Choose(persona, t, cap);
        string? what = w.Hazards.FireStory(key, room);
        if (what == null) { Next = w.Tick + SimTime.Hours(2); return; }
        Fired++;
        Note(key, t); // v15.7
        LastWhat = what;
        LastWhy = why;
        Journal.Add((w.Tick, what, why));
        if (Journal.Count > 200) Journal.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"이야기꾼({PersonaName(persona)}·{LevelName(Level)}): {what} — {why}");
        // 다음
        if (persona == StoryPersona.Burst)
        {
            if (BurstLeft == 0) BurstLeft = 1 + _rng.Range(1, 3); // 이번 것 뒤로 한두 개 더
            BurstLeft--;
            Next = BurstLeft > 0 ? w.Tick + SimTime.Minutes(_rng.Range(20f, 90f)) : w.Tick + GapTicks(gap * 2.2f, persona);
        }
        else Next = w.Tick + GapTicks(gap * GapMul(persona, cap), persona); // v15.7 새 성격은 간격 규칙이 다르다 (원래 넷은 1)
    }

    private long GapTicks(float days, StoryPersona p)
    {
        float d = p switch
        {
            StoryPersona.Random => -MathF.Log(1f - MathF.Min(_rng.Float(), 0.995f)) * days,
            _ => days * (0.7f + 0.6f * _rng.Float()),
        };
        return SimTime.Hours(MathF.Max(3f, d * 24f));
    }

    // ─────────────────────────────── 무엇을 ───────────────────────────────

    private (string key, Room? room, string why) Choose(StoryPersona p, float tension, float cap)
    {
        var w = _w;
        if (p == StoryPersona.Tester && Weakness() is { } weak) return weak;
        float big = BigScale;
        // 여력이 바닥이면 (가혹이 아니면) 작은 것으로
        bool gentle = cap < 0.35f && Level <= 3;
        var pool = new List<(string key, float weight)>
        {
            ("meteor", 10f), ("bigmeteor", gentle ? 0.5f : 3f * big), ("fire", 8f), ("break", 10f),
            (nameof(HazardKind.CoolantLoss), gentle ? 0.3f : 2f * big), (nameof(HazardKind.DuctFire), 3f), (nameof(HazardKind.HydrogenBuildup), 2.5f),
            (nameof(HazardKind.ComputerMisjudge), 2.5f), (nameof(HazardKind.Epidemic), gentle ? 0.3f : 1.5f * big), (nameof(HazardKind.MicroShower), 3f),
            (nameof(HazardKind.GasTankRupture), gentle ? 0.3f : 1.5f * big), (nameof(HazardKind.FreezerFailure), 2.5f),
        };
        if (w.Piping.Segments.Count > 0) pool.Add(("pipe", 5f));
        foreach (var s in HazardsV15.Specs) pool.Add((s.Kind.ToString(), s.Weight * (gentle ? 0.5f : 0.8f))); // v15 새 사고 44
        foreach (var s in Hazards.All)
        {
            if (s.Kind >= HazardKind.CoolantLoss || s.Kind == HazardKind.RescueSignal) continue;
            float wt = s.Weight;
            if (s.Kind == HazardKind.SolarStorm && w.Hazards.StormActive) continue;
            if (s.Kind is HazardKind.DebrisCloud or HazardKind.MeteorShower or HazardKind.ReactorTransient) wt *= gentle ? 0.3f : big;
            if (p == StoryPersona.Random) wt = 4f; // 무작위형: 다 비슷하게
            pool.Add((s.Kind.ToString(), wt));
        }
        if (p == StoryPersona.Random) for (int i = 0; i < pool.Count; i++) pool[i] = (pool[i].key, 4f);
        if (p >= StoryPersona.SlowBurn) Shape(p, pool, cap); // v15.7 성격마다 고르는 규칙
        for (int i = 0; i < pool.Count; i++) pool[i] = (pool[i].key, pool[i].weight * w.Voyage.HazardMul(pool[i].key) * w.Eras.RiskMul(pool[i].key)); // v12.8 구간 · 새 기술의 위험
        for (int i = 0; i < pool.Count; i++) pool[i] = (pool[i].key, pool[i].weight * w.Scale.PaceMul(pool[i].key)); // v16.18 규모 완급 (큰 것 뒤엔 작은 것 · 조용하면 작은 것부터)
        float total = pool.Sum(x => x.weight), roll = _rng.Float() * total;
        foreach (var x in pool) { roll -= x.weight; if (roll <= 0f) return (x.key, Aim(p), Why(p, tension, cap)); }
        return (pool[^1].key, Aim(p), Why(p, tension, cap));
    }

    private string Why(StoryPersona p, float t, float cap) => p switch
    {
        >= StoryPersona.SlowBurn => WhyV15(p, t, cap),
        StoryPersona.Steady => $"배가 거의 추슬렀다 (긴장 {t:0.00} · 여력 {cap * 100:0}%)",
        StoryPersona.Burst => $"몰아친다 (긴장 {t:0.00} · 여력 {cap * 100:0}%)",
        StoryPersona.Random => $"아무 때나 (긴장 {t:0.00})",
        _ => $"여력 {cap * 100:0}%",
    };

    /// <summary>시험관형: 배의 급소를 고른다 — 보조 간선이 없는 배전실, 예비 부품이 없는 냉각, 교정이 틀어진 감지기, 모자란 실링폼.</summary>
    private (string key, Room? room, string why)? Weakness()
    {
        var w = _w;
        var ship = w.Ship;
        var options = new List<(string key, Room? room, string why, float score)>();
        var power = ship.FurnitureOf(FurnitureType.PowerPanel).Select(f => f.Room).FirstOrDefault(r => !r.Detached);
        if (power != null && w.Net.Rings.Count(r => r.kind == NetKind.Power) == 0)
            options.Add((Level >= 4 && _rng.Chance(0.5f) ? "bigmeteor" : Level >= 3 && _rng.Chance(0.4f) ? "meteor" : "fire", power, "보조 간선이 없는 배전실 — 한 곳이 끊기면 배 전체가 꺼진다", 3f));
        if (ship.CountStored(ItemKind.Pump) + ship.CountStored(ItemKind.Bearing) <= 1)
            options.Add((nameof(HazardKind.CoolantLoss), null, "냉각 펌프 예비 부품이 거의 없다", 2.5f));
        if (ship.Machines.Count(m => m.SensorCal < 0.7f) >= 3 && w.Automation.MainOnline)
            options.Add((nameof(HazardKind.ComputerMisjudge), null, "교정이 틀어진 감지기가 많다 — 컴퓨터가 잘못 읽을 것", 2f));
        if (ship.CountStored(ItemKind.Sealant) < 6)
            options.Add((nameof(HazardKind.MicroShower), null, "실링폼이 모자라다 — 작은 구멍 여럿", 2f));
        if (!ship.RoomsOf(RoomType.Medbay).Any(r => !r.Detached) || ship.CountStored(ItemKind.MedKit) < 2)
            options.Add((nameof(HazardKind.Epidemic), null, "의무실·구급 키트가 모자라다 — 병이 돌면", 1.5f));
        var fridge = ship.FurnitureOf(FurnitureType.Fridge).FirstOrDefault(f => !f.Room.Detached);
        if (fridge != null && ship.CountStored(ItemKind.Meal) > w.Crew.Count * 4)
            options.Add((nameof(HazardKind.FreezerFailure), fridge.Room, "냉장고에 먹을 것이 몰려 있다", 1f));
        if (options.Count == 0) return null;
        float total = options.Sum(o => o.score), roll = _rng.Float() * total;
        foreach (var o in options) { roll -= o.score; if (roll <= 0f) return (o.key, o.room, $"급소: {o.why}"); }
        var last = options[^1];
        return (last.key, last.room, $"급소: {last.why}");
    }
}
