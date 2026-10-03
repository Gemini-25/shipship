using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 의료 2차 — 장기 손상 · 부전 · 인공 장기 기계.
// 사람마다 폐 · 심장 · 간 · 신장의 상한 정도(0 멀쩡 ~ 1 멎음)가 있다. 원인은 이미 있는 것들에서 온다:
//   폐: 연기 · 유독 가스 · 감압으로 다친 폐(부위 상처) · 폐렴 · 큰 피폭     심장: 멎었던 심장(심정지) · 감전 · 가슴을 크게 다침 · 오래 상한 폐
//   간: 식중독 · 약을 너무 자주 씀(구급 키트) · 큰 피폭                     신장: 탈수 · 결석 · 쇼크(체력 바닥) · 식중독 · 약 · 큰 피폭 · 면역억제제
// 반쯤 상하면 저절로 낫지 않고 서서히 나빠진다 (부전). 증상은 앓는 것(폐 부전 · 심부전 · 간부전 · 신부전)으로 몸에 나온다 — 숨참 · 피로 · 부기 · 일 손 · 걸음.
// 진단: 의무관이 곁에서 보거나, 주컴퓨터가 생체 신호(치료 침대 · 진단 스캐너)로 읽는다 — 진단되기 전에는 본인도 왜 숨이 찬지 모른다.
// 기계가 대신한다: 투석기(노폐물을 걸러 낸다 — 이틀에 한 번 네 시간) · 인공 폐(체외 순환 — 누워 있는 동안 폐를 쉬게 한다) · 인공 심장(몸에 단 펌프 — 열두 시간마다 충전대에서 전지를 채운다).
// 전원이 끊기면 위험하다: 내장 전지가 버티는 동안 주컴퓨터가 그 회로를 지킨다 (전기를 몰아준다 · 다른 것을 먼저 내린다). 전지가 바닥나면
//   인공 폐는 곁의 사람이 손으로 펌프를 돌리고 · 투석은 회로의 피가 굳어 멈춘다 · 인공 심장은 멎는다.

public enum Organ : byte { Lungs, Heart, Liver, Kidney }

/// <summary>한 사람의 장기.</summary>
public sealed class OrganBody
{
    public int Crew { get; init; }
    public readonly float[] Dmg = new float[4];
    public readonly string?[] Cause = new string?[4];
    /// <summary>이식받은 장기: 준 사람 번호 (−1 제 것 · −2 배양 장기).</summary>
    public readonly int[] Graft = { -1, -1, -1, -1 };
    public readonly float[] Match = { 1f, 1f, 1f, 1f };
    public float Uremia { get; set; }              // 신장이 못 거른 노폐물 (0 ~ 1.5)
    public float Reject { get; set; }              // 거부반응 (0 ~ 1)
    public long DoseAt { get; set; } = -1;         // 면역억제제를 마지막으로 먹은 때
    public int Doses { get; set; }
    public bool Pump { get; set; }                 // 인공 심장을 달았다
    public float PumpCharge { get; set; } = 1f;    // 몸에 찬 전지 (열두 시간)
    public bool OneKidney { get; set; }            // 신장 하나를 나눠 줬다
    public bool LiverPart { get; set; }            // 간 일부를 나눠 줬다
    public int Machine { get; set; } = -1;         // 붙어 있는 기계 (가구 번호)
    public bool Hooked { get; set; }
    public long HookedAt { get; set; } = -1;
    public long LastSession { get; set; } = -1;    // 마지막 투석
    public long LastTreated { get; set; } = -1_000_000;
    public readonly long[] Meds = new long[6];     // 최근 약 쓴 때 (과용)
    public int ArrestSeen { get; set; } = -1;
    internal readonly float[] Seen = new float[3], Pending = new float[3];
    internal readonly string?[] PendCause = new string?[3];
    public bool Grafted => Graft[0] != -1 || Graft[1] != -1 || Graft[2] != -1 || Graft[3] != -1;
    public bool NeedsSuppress => Graft.Any(g => g >= 0);
}

public sealed class OrganStats
{
    public int Cases, Hooks, Sessions, Aborted, Cranked, Guards, PowerLost, Diagnosed, Arrests, PumpEmpty, Recovered, OverMed, Doses, NoDose;
    public readonly int[] ByOrgan = new int[4];
    public string Line() => $"장기 부전 {Cases}(폐 {ByOrgan[0]} · 심장 {ByOrgan[1]} · 간 {ByOrgan[2]} · 신장 {ByOrgan[3]}) · 연결 {Hooks} · 투석 {Sessions} · 멈춤 {Aborted} · 손 펌프 {Cranked} · 회로 지킴 {Guards} · 전원 끊김 {PowerLost} · 컴퓨터 진단 {Diagnosed} · 멎음 {Arrests} · 전지 바닥 {PumpEmpty} · 약 과용 {OverMed} · 면역억제제 {Doses}(없음 {NoDose})";
}

/// <summary>인공 장기 기계 · 장기 보관함 · 바이오 프린터 · 음압기 (개조로 단다).</summary>
public static class OrganGear
{
    public sealed record Row(FurnitureType Type, string Name, RoomType Room, (ItemKind kind, int count)[] Cost, string Note, float Power, int Priority, Skill Skill, FaultKind[] Faults, Func<World, (float, string)> Need);

    private static (ItemKind, int)[] C(params (ItemKind, int)[] x) => x;
    private static FaultKind[] F(params FaultKind[] x) => x;
    private static (float, string) No => (0f, "");

    public static readonly Row[] Rows =
    {
        new(FurnitureType.Dialyzer, "투석기", RoomType.Medbay, C((ItemKind.Pump, 1), (ItemKind.Filter, 2), (ItemKind.Hose, 2)), "망가진 신장 대신 피를 걸러 낸다 (이틀에 한 번 네 시간)",
            1.2f, 6, Skill.Electrical, F(FaultKind.MembraneFouling, FaultKind.FilterClogged, FaultKind.SensorDrift), w => w.Organs.Stats.ByOrgan[(int)Organ.Kidney] > 0 ? (0.7f, "신장이 상한 사람이 있다") : (w.Ailments.Stats.Cases.GetValueOrDefault("kidneystone") >= 2 ? (0.2f, "결석이 잦다") : No)),
        new(FurnitureType.Ecmo, "인공 폐", RoomType.Medbay, C((ItemKind.Pump, 1), (ItemKind.Membrane, 2), (ItemKind.Hose, 2), (ItemKind.Sensor, 1)), "피에 산소를 넣어 상한 폐를 쉬게 한다 (손 펌프 달림)",
            1.5f, 6, Skill.Electrical, F(FaultKind.MembraneTear, FaultKind.PumpSeized, FaultKind.SensorDrift), w => w.Organs.Stats.ByOrgan[(int)Organ.Lungs] > 0 ? (0.7f, "폐가 상한 사람이 있다") : No),
        new(FurnitureType.HeartPump, "인공 심장 충전대", RoomType.Medbay, C((ItemKind.Electronics, 2), (ItemKind.CellPack, 2), (ItemKind.Cable, 1)), "몸에 단 심장 펌프의 전지를 채운다",
            0.6f, 5, Skill.Electrical, F(FaultKind.ChargerFault, FaultKind.WiringFault), w => w.Organs.Stats.ByOrgan[(int)Organ.Heart] > 0 ? (0.6f, "심장이 상한 사람이 있다") : w.Casualty.Arrests >= 2 ? (0.2f, $"심장이 {w.Casualty.Arrests}번 멎었다") : No),
        new(FurnitureType.OrganCooler, "장기 보관함", RoomType.Medbay, C((ItemKind.Thermostat, 1), (ItemKind.Plate, 2), (ItemKind.Gasket, 1)), "떼어 낸 장기를 차게 둔다 (몇 배 오래 간다)",
            0.4f, 5, Skill.Electrical, F(FaultKind.CompressorFail, FaultKind.ThermostatFault), w => w.Organs.Stats.Cases > 0 && w.History.Deaths > 0 ? (0.4f, "장기를 기다리는 사람이 있다") : No),
        new(FurnitureType.BioPrinter, "바이오 프린터", RoomType.Lab, C((ItemKind.Electronics, 2), (ItemKind.Nozzle, 2), (ItemKind.Sensor, 1)), "제 세포로 장기를 한 층씩 쌓는다 (몸이 밀어내지 않는다)",
            0.8f, 3, Skill.Electrical, F(FaultKind.NozzleClog, FaultKind.CalibrationLoss), w => w.Organs.Stats.Cases > 0 && TransplantSystem.PrintTech(w) ? (0.5f, "장기를 기다리는 사람 · 세포를 찍는 법을 안다") : No),
        new(FurnitureType.NegPressure, "음압기", RoomType.Medbay, C((ItemKind.Fan, 2), (ItemKind.Filter, 2), (ItemKind.Hose, 1)), "방 공기를 걸러 빼내 문 밖으로 균이 새지 않는다",
            0.7f, 5, Skill.Mechanics, F(FaultKind.FanFail, FaultKind.FilterClogged), w => w.Disease.Stats.Infections + w.Infection.Stats.Airborne >= 2 ? (0.5f, "병이 방을 넘어 옮았다") : No),
    };

    private static readonly Dictionary<FurnitureType, Row> ByType = Rows.ToDictionary(r => r.Type);
    public static Row? Of(FurnitureType t) => ByType.TryGetValue(t, out var r) ? r : null;
    public static bool Is(FurnitureType t) => ByType.ContainsKey(t);
    public static string? Name(FurnitureType t) => Of(t)?.Name;
    public static IEnumerable<Modules.Spec> Specs => Rows.Select(r => new Modules.Spec(r.Type, r.Room, 1, r.Cost, r.Note, 1f));
    public static IEnumerable<MachineSpec> Machines => Rows.Select(r => new MachineSpec(r.Type, r.Power, r.Priority, 50f, r.Skill, null, 0.4f, false, r.Faults));
    public static (float, string) Need(World w, FurnitureType t) => Of(t)?.Need(w) ?? (0f, "");

    /// <summary>쓸 수 있다 (고장 없음 · 버려진 방이 아님). 전기는 따로 본다 (내장 전지).</summary>
    public static bool Sound(Furniture f) => !f.Stowed && f.Machine is Machine m && !m.Stopped && m.Faults.Count == 0 && !f.Room.Abandoned && !f.Room.Detached;
    public static bool Works(Furniture f) => Sound(f) && f.Machine!.Powered;
}

public sealed class OrganSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7457 + 3301));
    private readonly SortedDictionary<int, OrganBody> _b = new();
    private readonly SortedSet<int> _guard = new();
    private readonly SortedDictionary<int, float> _cell = new();   // 기계 내장 전지 (가구 번호 → 0~1)
    private readonly SortedDictionary<int, long> _crank = new();   // 손 펌프를 마지막으로 돌린 때
    private readonly SortedDictionary<int, long> _told = new();
    public OrganStats Stats { get; } = new();
    public static bool Off;
    public static bool NoGuard; // 시험: 주컴퓨터가 회로를 지키지 않으면
    private long _next, _last = -1;
    private bool _seeded, _short;

    public OrganSystem(World w) => _w = w;

    public static readonly string[] AilId = { "lungfail", "heartfail", "liverfail", "kidneyfail" };
    public static string Name(Organ o) => o switch { Organ.Lungs => "폐", Organ.Heart => "심장", Organ.Liver => "간", _ => "신장" };
    public static Organ[] All { get; } = { Organ.Lungs, Organ.Heart, Organ.Liver, Organ.Kidney };

    public IEnumerable<OrganBody> Bodies => _b.Values;
    public OrganBody? Peek(CrewMember c) => _b.TryGetValue(c.Id, out var b) ? b : null;
    public OrganBody Of(CrewMember c)
    {
        if (_b.TryGetValue(c.Id, out var b)) return b;
        return _b[c.Id] = new OrganBody { Crew = c.Id };
    }
    public float Dmg(CrewMember c, Organ o) => Peek(c)?.Dmg[(int)o] ?? 0f;
    public bool Guarded(Furniture f) => _guard.Contains(f.Id);
    public float Cell(Furniture f) => _cell.TryGetValue(f.Id, out var v) ? v : 1f;
    public bool Cranked(Furniture f) => _crank.TryGetValue(f.Id, out var t) && _w.Tick - t < SimTime.Minutes(3);
    /// <summary>면역억제제가 몸에 돈다 (서른여섯 시간).</summary>
    public bool Suppressed(CrewMember c) => Peek(c) is OrganBody b && b.DoseAt >= 0 && _w.Tick - b.DoseAt < SimTime.Hours(36);
    private CrewMember? P(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }

    /// <summary>기계가 돌고 있다 (전기 · 내장 전지 · 손 펌프).</summary>
    public bool Running(Furniture f) => OrganGear.Sound(f) && (f.Machine!.Powered || Cell(f) > 0f || f.Type == FurnitureType.Ecmo && Cranked(f));

    /// <summary>이 기계에 연결된 사람.</summary>
    public CrewMember? PatientOn(Furniture f)
    {
        foreach (var b in _b.Values) if (b.Hooked && b.Machine == f.Id) return P(b.Crew);
        return null;
    }

    public static FurnitureType? MachineFor(Organ o) => o switch { Organ.Lungs => FurnitureType.Ecmo, Organ.Kidney => FurnitureType.Dialyzer, Organ.Heart => FurnitureType.HeartPump, _ => null };

    /// <summary>장기가 상한다 (원인은 큰 것이 남는다).</summary>
    public void Hurt(CrewMember c, Organ o, float amount, string cause)
    {
        if (amount <= 0f || c.Dead) return;
        var b = Of(c);
        int i = (int)o;
        if (b.Cause[i] == null || amount >= 0.05f || b.Dmg[i] < 0.1f) b.Cause[i] = cause;
        b.Dmg[i] = MathF.Min(1f, b.Dmg[i] + amount);
    }

    /// <summary>목표까지 서서히 상한다 (상처가 큰 만큼 장기도 상한다 — 상처가 아물어도 장기는 그대로).</summary>
    private void Toward(CrewMember c, Organ o, float target, float rate, string cause)
    {
        float d = Dmg(c, o);
        if (target <= d + 0.005f) return;
        Hurt(c, o, (target - d) * MathF.Min(1f, rate), cause);
    }

    /// <summary>지금 이 사람에게 기계가 필요한 장기 (투석 · 인공 폐 · 인공 심장).</summary>
    public Organ? NeedsMachine(CrewMember c)
    {
        if (Peek(c) is not OrganBody b) return null;
        if (b.Dmg[(int)Organ.Lungs] >= 0.7f && b.Graft[(int)Organ.Lungs] == -1 || b.Hooked && Machine(b)?.Type == FurnitureType.Ecmo && b.Dmg[(int)Organ.Lungs] >= 0.45f) return Organ.Lungs;
        if (b.Uremia >= 0.35f || b.Hooked && Machine(b)?.Type == FurnitureType.Dialyzer && b.Uremia > 0.05f) return Organ.Kidney;
        return null;
    }

    public Furniture? Machine(OrganBody b) => b.Machine >= 0 && b.Machine < _w.Ship.Furniture.Count ? _w.Ship.Furniture[b.Machine] : null;

    /// <summary>진단이 됐다 (본인 · 의무관이 안다).</summary>
    public bool Known(CrewMember c, Organ o) => c.Ailments.Any(a => a.Id == AilId[(int)o] && a.Diagnosed);

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Off) return;
        if (!_seeded) Seed();
        Guard(); // 매 틱 (전기 배분이 읽는다)
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(10);
        float h = _last < 0 ? 1f / 6f : (w.Tick - _last) / (float)SimTime.TicksPerHour;
        _last = w.Tick;
        foreach (var c in w.Crew)
        {
            if (c.Dead) { if (_b.TryGetValue(c.Id, out var db)) { db.Hooked = false; db.Machine = -1; } continue; }
            if (c.Away) continue;
            Causes(c, h);
            if (!_b.TryGetValue(c.Id, out var b)) continue;
            Course(c, b, h);
            Machines(c, b, h);
            Effects(c, b, h);
            Sync(c, b);
            Vitals(c, b);
        }
    }

    /// <summary>처음: 의무실 선반에 투석액 · 면역억제제 조금 (세포 잉크는 실험실이 있으면).</summary>
    private void Seed()
    {
        _seeded = true;
        var w = _w;
        Stock(w, ItemKind.Immunosuppressant, 6);
        Stock(w, ItemKind.Dialysate, 6);
        if (w.Ship.Rooms.Any(r => r.Type == RoomType.Lab && !r.Detached)) Stock(w, ItemKind.BioInk, 2);
    }

    /// <summary>선반에 넣는다 (의무실 → 창고 → 그 밖 · 자리가 남는 곳부터).</summary>
    public static int Stock(World w, ItemKind k, int n)
    {
        int put = 0;
        foreach (var f in w.Ship.Furniture.Where(f => f.Type == FurnitureType.Shelf && f.Storage != null && !f.Room.Detached)
                     .OrderBy(f => f.Room.Type == RoomType.Medbay ? 0 : f.Room.Type == RoomType.Storage ? 1 : 2).ThenBy(f => f.Id))
        {
            if (put >= n) break;
            put += f.Storage!.Add(k, n - put);
        }
        return put;
    }

    private void Causes(CrewMember c, float h)
    {
        var w = _w;
        var v = c.Vitals;
        // 다친 폐 · 가슴 (부위 상처) — 새로 다친 만큼 장기도 상한다 (상처가 아물어도 장기는 그대로)
        float lungW = 0f, chestW = 0f, shockW = 0f;
        string? lungC = null;
        if (v.Wounds.Count > 0)
            foreach (var x in v.Wounds)
            {
                if (x.Lost) continue;
                float s = MathF.Min(1f, x.Weight); // 다친 양 (아물어도 줄지 않는다 — 새로 다친 만큼만 장기에)
                if (s < 0.03f) continue;
                if (x.Part == BodyPart.Lungs && x.Kind is WoundKind.Toxic or WoundKind.Barotrauma) { float k = (x.Kind == WoundKind.Toxic ? 1f : 0.9f) * s; if (k > lungW) { lungW = k; lungC = x.Cause; } }
                if (x.Part == BodyPart.Chest && x.Kind is WoundKind.Burn or WoundKind.Fracture or WoundKind.Crush or WoundKind.Radiation) chestW = MathF.Max(chestW, 0.6f * s);
                if (x.Cause.Contains("감전") || x.Cause.Contains("누전")) shockW = MathF.Max(shockW, 0.55f * s);
            }
        if (lungW > 0f || chestW > 0f || shockW > 0f || _b.ContainsKey(c.Id))
        {
            var ob = Of(c);
            // 상처가 커진 만큼 (한 번에 다 오르지 않고 몇십 분에 걸쳐 — 연기를 마신 폐가 붓는다)
            void Add(int k, Organ o, float now, string cause)
            {
                if (now > ob.Seen[k]) { ob.Pending[k] += now - ob.Seen[k]; ob.PendCause[k] = cause; }
                ob.Seen[k] = now;
                if (ob.Pending[k] <= 0.001f) return;
                float step = ob.Pending[k] * MathF.Min(1f, 2f * h);
                ob.Pending[k] -= step;
                Hurt(c, o, step, ob.PendCause[k] ?? cause);
            }
            Add(0, Organ.Lungs, lungW, lungC ?? "다친 폐");
            Add(1, Organ.Heart, chestW, "가슴을 크게 다침");
            Add(2, Organ.Heart, shockW, "감전");
        }
        // 멎었던 심장
        if (w.Casualty.Of(c) is Trauma t && t.Kind == TraumaKind.Arrest)
        {
            var b = Of(c);
            if (b.ArrestSeen != t.Id) { b.ArrestSeen = t.Id; Hurt(c, Organ.Heart, 0.18f, "멎었던 심장"); }
            Hurt(c, Organ.Heart, 0.25f * h, "멎었던 심장");
            Hurt(c, Organ.Kidney, 0.1f * h, "쇼크");
        }
        // 앓는 것
        if (c.Ailments.Count > 0)
            foreach (var a in c.Ailments)
            {
                float s = w.Ailments.Severity(a);
                switch (a.Id)
                {
                    case "foodpoison": Toward(c, Organ.Liver, 0.6f * s, 0.3f * h, "식중독"); Toward(c, Organ.Kidney, 0.5f * s, 0.3f * h, "식중독"); break;
                    case "dehydration": Toward(c, Organ.Kidney, 0.6f * s, 0.3f * h, "탈수"); break;
                    case "kidneystone": Toward(c, Organ.Kidney, 0.45f * s, 0.2f * h, "결석"); break;
                    case "pneumonia": Toward(c, Organ.Lungs, 0.7f * s, 0.25f * h, "폐렴"); break;
                    case "heatstroke": Toward(c, Organ.Kidney, 0.5f * s, 0.3f * h, "열사병"); break;
                }
            }
        // 쇼크 (체력 바닥) — 신장이 먼저 상한다
        if (v.Health < 0.25f && (v.Injury > 0.1f || c.Down)) Hurt(c, Organ.Kidney, 0.06f * h * (0.25f - v.Health) / 0.25f + 0.01f * h, "쇼크");
        // 큰 피폭: 2Sv를 넘은 만큼
        if (c.Dose > 2f)
        {
            float over = MathF.Min(1f, (c.Dose - 2f) / 6f);
            Toward(c, Organ.Kidney, 0.8f * over, 0.05f * h, "큰 피폭");
            Toward(c, Organ.Liver, 0.6f * over, 0.05f * h, "큰 피폭");
            Toward(c, Organ.Lungs, 0.5f * over, 0.04f * h, "큰 피폭");
        }
        // 약 과용: 하루에 다섯 번 넘게 치료(구급 키트 · 진통제)를 받으면 간 · 신장이 상한다
        if (v.TreatedTick > 0 && (Peek(c)?.LastTreated ?? -1_000_000) != v.TreatedTick)
        {
            var b = Of(c);
            b.LastTreated = v.TreatedTick;
            Array.Copy(b.Meds, 0, b.Meds, 1, b.Meds.Length - 1);
            b.Meds[0] = v.TreatedTick;
            int recent = b.Meds.Count(m => m > 0 && w.Tick - m < SimTime.TicksPerDay);
            if (recent >= 5)
            {
                Stats.OverMed++;
                Hurt(c, Organ.Liver, 0.06f * (recent - 4), "약을 너무 많이 썼다");
                Hurt(c, Organ.Kidney, 0.03f * (recent - 4), "약을 너무 많이 썼다");
                if (recent == 5) w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name} — 하루에 약을 {recent}번 썼다 · 간에 무리가 간다", c.Id);
            }
        }
    }

    /// <summary>저절로: 덜 상한 장기는 천천히 낫고 · 반 넘게 상하면 서서히 나빠진다 (기계가 대신하면 멈춘다).</summary>
    private void Course(CrewMember c, OrganBody b, float h)
    {
        var w = _w;
        float day = h / 24f;
        bool rest = c.CareBed != null || c.Pose == Pose.Sleeping;
        var m = b.Hooked ? Machine(b) : null;
        bool ecmo = m?.Type == FurnitureType.Ecmo && Running(m);
        for (int i = 0; i < 4; i++)
        {
            float d = b.Dmg[i];
            if (d <= 0f) continue;
            var o = (Organ)i;
            bool supported = o == Organ.Lungs && ecmo || o == Organ.Heart && b.Pump || o == Organ.Kidney && b.LastSession >= 0 && w.Tick - b.LastSession < SimTime.Hours(60);
            float regen = o switch { Organ.Lungs => 0.03f, Organ.Heart => 0.008f, Organ.Liver => 0.07f, _ => 0.02f } * (rest ? 1.6f : 1f) * ErasV15.Mul(w, "heal");
            if (o == Organ.Lungs && ecmo) regen = 0.12f; // 폐를 쉬게 한다
            if (b.Graft[i] != -1 && d < 0.5f && b.Reject < 0.3f) regen *= 2f; // 갓 이식한 장기 — 수술 자리가 아문다
            if (d < 0.5f || o == Organ.Lungs && ecmo)
            {
                float floor = o == Organ.Kidney && b.OneKidney ? 0.2f : 0f;
                b.Dmg[i] = MathF.Max(floor, d - regen * day);
                if (b.Dmg[i] <= 0f) b.Cause[i] = null;
            }
            else if (!supported) b.Dmg[i] = MathF.Min(1f, d + (o == Organ.Heart ? 0.03f : 0.035f) * day); // 부전 — 서서히 나빠진다
        }
        // 오래 상한 폐는 심장에 짐이 된다
        if (b.Dmg[0] > 0.6f && !ecmo) Hurt(c, Organ.Heart, 0.03f * (b.Dmg[0] - 0.6f) * day * 10f, "오래 상한 폐");
        // 신장이 못 거른 노폐물
        float k = b.Dmg[(int)Organ.Kidney];
        if (k > 0.5f) b.Uremia = MathF.Min(1.5f, b.Uremia + 0.55f * (k - 0.5f) / 0.5f * day);
        else b.Uremia = MathF.Max(0f, b.Uremia - 0.5f * day);
    }

    /// <summary>기계: 내장 전지 · 손 펌프 · 전원이 끊기면 위험 — 주컴퓨터가 회로를 지킨다.</summary>
    private void Machines(CrewMember c, OrganBody b, float h)
    {
        var w = _w;
        var a = w.Automation;
        // 인공 심장 전지
        if (b.Pump)
        {
            b.PumpCharge = MathF.Max(0f, b.PumpCharge - h / 12f);
            if (b.PumpCharge <= 0f && !c.Dead)
            {
                Stats.PumpEmpty++;
                b.PumpCharge = 0.02f;
                if (w.Casualty.Of(c) == null) { Stats.Arrests++; w.Casualty.Inflict(c, TraumaKind.Arrest, 0.6f, "인공 심장 전지가 바닥났다"); }
                w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name} — 인공 심장 전지가 바닥났다", c.Id);
                MarkLog.Add(c.Memory.Marks, w.Tick, "심장 펌프가 멎었다");
            }
            else if (b.PumpCharge < 0.2f && a.Present && a.MainOnline && Tell(c.Id * 8 + 1, SimTime.Hours(2)))
                a.Speak.Announce(a.Voice.Style($"{c.Name} — 인공 심장 전지 {b.PumpCharge * 12f:0.#}시간 · 충전대로"), c.Room, 3);
        }
        if (!b.Hooked || Machine(b) is not Furniture f) return;
        var m = f.Machine!;
        float cell = Cell(f);
        if (!OrganGear.Sound(f) || (c.Position - f.Center).Length() > 2.6f) { Unhook(c, b, OrganGear.Sound(f) ? "기계에서 떨어졌다" : $"{f.Name}가 멎었다"); return; }
        if (m.Powered) { _cell[f.Id] = MathF.Min(1f, cell + h * 2f); return; }
        // 전원이 끊겼다 — 내장 전지 (투석기 30분 · 인공 폐 한 시간)
        if (cell >= 1f) { Stats.PowerLost++; w.Log.Add(w.Tick, LogKind.Warning, $"{f.Name} 전원이 끊겼다 — 내장 전지로 돈다 ({c.Name})", c.Id); }
        float cap = f.Type == FurnitureType.Dialyzer ? 0.5f : 1f;
        cell = MathF.Max(0f, cell - h / cap);
        _cell[f.Id] = cell;
        if (a.Present && a.MainOnline && f.Room.DataLinked && Tell(f.Id * 8 + 2, SimTime.Hours(1)))
            a.Speak.Announce(a.Voice.Style($"{f.Room.Name} {f.Name} 전원이 끊겼다 — {c.Name}에게 연결돼 있다 · 내장 전지 {cell * cap * 60f:0}분 · 그 회로부터 살린다"), f.Room, 3);
        if (cell > 0f) return;
        if (f.Type == FurnitureType.Ecmo)
        {
            if (Cranked(f)) return;
            // 아무도 돌리지 않는다 — 피에 산소가 못 들어간다
            c.Vitals.Oxygen = MathF.Min(c.Vitals.Oxygen, 0.45f);
            c.Vitals.Health = MathF.Max(0f, c.Vitals.Health - 0.4f * h);
            if (a.Present && a.MainOnline && Tell(f.Id * 8 + 3, SimTime.Minutes(30))) a.Speak.Announce(a.Voice.Style($"{f.Room.Name} — 인공 폐 손 펌프를 돌릴 사람 · {c.Name}"), f.Room, 3);
        }
        else if (f.Type == FurnitureType.Dialyzer)
        {
            // 회로의 피가 굳었다
            Stats.Aborted++;
            c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - 0.08f);
            w.Log.Add(w.Tick, LogKind.Warning, $"투석 중에 전기가 끊겨 회로의 피가 굳었다 — {c.Name} 투석을 멈췄다", c.Id);
            Memory.Frighten(w, c, f.Room, 0.12f, "투석 중에 불이 나갔다");
            MarkLog.Add(f.Machine!.Marks, w.Tick, "투석 중 전원이 끊겨 회로를 버렸다");
            Unhook(c, b, null);
        }
    }

    private bool Tell(int key, long gap)
    {
        if (_told.TryGetValue(key, out var t) && _w.Tick - t < gap) return false;
        _told[key] = _w.Tick;
        return true;
    }

    /// <summary>주컴퓨터가 지키는 회로: 사람이 연결된 기계 · 장기가 든 보관함 · 세포를 찍는 프린터 · 전지가 빈 사람이 충전 중인 충전대.</summary>
    private void Guard()
    {
        var w = _w;
        var a = w.Automation;
        int before = _guard.Count;
        _guard.Clear();
        if (NoGuard || !a.Present || !a.MainOnline) return;
        foreach (var b in _b.Values)
            if (b.Hooked && Machine(b) is Furniture f && f.Room.DataLinked) _guard.Add(f.Id);
        foreach (var id in w.Transplant.Guarded()) _guard.Add(id);
        bool short_ = w.Power.Brownout || w.Power.DeficitSince >= 0 || w.Failsafe.ShedNow > 0 || !w.Power.ReactorOnline;
        if (short_ && _guard.Count > 0 && (!_short || _guard.Count > before))
        {
            Stats.Guards++;
            var f = w.Ship.Furniture[_guard.Max];
            a.Reason($"organguard:{f.Id}", $"전기가 모자라다 — {f.Room.Name} {f.Name}에 사람이 달려 있다 · 그 회로는 끝까지 지키고 다른 것부터 내린다", SimTime.Hours(2));
            if (Tell(f.Id * 8 + 4, SimTime.Hours(2))) a.Speak.Announce(a.Voice.Style($"전기가 모자라다 — {f.Room.Name} {f.Name} 회로는 끝까지 지킨다 · 다른 것부터 내린다"), f.Room, 2);
        }
        _short = short_ && _guard.Count > 0;
    }

    public void Hook(CrewMember c, Furniture f, CrewMember? by)
    {
        var w = _w;
        var b = Of(c);
        b.Machine = f.Id;
        b.Hooked = true;
        b.HookedAt = w.Tick;
        Stats.Hooks++;
        if (f.Type == FurnitureType.Dialyzer) ItemsV15.Use(w, ItemKind.Dialysate);
        string what = f.Type == FurnitureType.Dialyzer ? "투석을 시작했다" : "인공 폐를 달았다 — 피가 기계를 돌아 나온다";
        w.Log.Add(w.Tick, LogKind.Work, $"{c.Name} — {what}" + (by != null && by != c ? $" ({by.Name})" : ""), (by ?? c).Id);
        if (f.Type == FurnitureType.Ecmo) MarkLog.Add(c.Memory.Marks, w.Tick, "인공 폐에 매달렸다");
        if (by != null && by != c) { w.Relations.Remember(c, by, RelationReason.NursedMe, f.Type == FurnitureType.Dialyzer ? "투석을 걸어 줬다" : "인공 폐를 달아 줬다"); c.ChangeAffinity(by, 0.05f); }
    }

    public void Unhook(CrewMember c, OrganBody b, string? why)
    {
        var w = _w;
        if (!b.Hooked) return;
        var f = Machine(b);
        if (f?.Type == FurnitureType.Dialyzer && w.Tick - b.HookedAt >= SimTime.Hours(1)) { b.LastSession = w.Tick; Stats.Sessions++; }
        b.Hooked = false;
        b.Machine = -1;
        if (why != null) w.Log.Add(w.Tick, LogKind.Work, $"{c.Name} — {f?.Name ?? "기계"}에서 뗐다 ({why})", c.Id);
    }

    /// <summary>기계가 돌 동안 매 틱 (연결된 사람의 일에서 부른다).</summary>
    public void Support(CrewMember c, Furniture f)
    {
        var b = Of(c);
        if (!b.Hooked || b.Machine != f.Id || !Running(f)) return;
        const float dt = 1f / SimTime.TicksPerHour;
        if (f.Type == FurnitureType.Dialyzer) b.Uremia = MathF.Max(0f, b.Uremia - 0.28f * dt);
        else if (f.Type == FurnitureType.Ecmo) c.Vitals.Oxygen = MathF.Max(c.Vitals.Oxygen, f.Machine!.Powered || Cell(f) > 0f ? 0.95f : 0.8f);
    }

    public void Crank(CrewMember by, Furniture f)
    {
        if (!_crank.ContainsKey(f.Id) || _w.Tick - _crank[f.Id] > SimTime.Minutes(5)) { Stats.Cranked++; _w.Log.Add(_w.Tick, LogKind.Work, $"{f.Name} 손 펌프를 돌린다", by.Id); }
        _crank[f.Id] = _w.Tick;
        by.Needs.Rest = MathF.Max(0f, by.Needs.Rest - 0.25f / SimTime.TicksPerHour);
    }

    /// <summary>몸이 버티지 못한다 (부전이 깊으면).</summary>
    private void Effects(CrewMember c, OrganBody b, float h)
    {
        var w = _w;
        var v = c.Vitals;
        var m = b.Hooked ? Machine(b) : null;
        bool ecmo = m?.Type == FurnitureType.Ecmo && Running(m);
        float lung = b.Dmg[0], heart = b.Dmg[1], liver = b.Dmg[2];
        if (lung > 0.8f && !ecmo) { v.Health = MathF.Max(0f, v.Health - 0.05f * (lung - 0.75f) / 0.25f * h); v.Oxygen = MathF.Min(v.Oxygen, 1.2f - lung); }
        if (liver > 0.85f) v.Health = MathF.Max(0f, v.Health - 0.02f * h);
        if (b.Uremia > 0.8f) { v.Health = MathF.Max(0f, v.Health - 0.04f * (b.Uremia - 0.7f) * h); c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f * h); }
        if (heart > 0.85f && !b.Pump && w.Casualty.Of(c) == null && R.Chance(MathF.Min(0.5f, 0.03f * h * (heart - 0.8f) / 0.2f)))
        {
            Stats.Arrests++;
            w.Casualty.Inflict(c, TraumaKind.Arrest, 0.6f, "심장이 버티지 못했다");
        }
        if (!w.CrewCanDie) v.Health = MathF.Max(v.Health, 0.02f);
    }

    /// <summary>증상 = 앓는 것 (세기를 장기에 맞춘다).</summary>
    private void Sync(CrewMember c, OrganBody b)
    {
        var w = _w;
        var m = b.Hooked ? Machine(b) : null;
        for (int i = 0; i < 4; i++)
        {
            var o = (Organ)i;
            float d = b.Dmg[i];
            if (o == Organ.Kidney) d = MathF.Max(d * 0.8f, MathF.Min(1f, b.Uremia));
            bool helped = o == Organ.Lungs && m?.Type == FurnitureType.Ecmo && Running(m) || o == Organ.Heart && b.Pump;
            if (helped) d *= 0.45f;
            var a = c.Ailments.FirstOrDefault(x => x.Id == AilId[i]);
            if (a == null)
            {
                if (d < 0.35f) continue;
                a = w.Ailments.Catch(c, AilId[i], null, b.Cause[i]);
                if (a == null) continue;
                Stats.Cases++;
                Stats.ByOrgan[i]++;
                w.Causes.Effect(CauseKind.Illness, $"organ:{c.Id}:{i}", $"{c.Name} {Name(o)} — {b.Cause[i] ?? "상했다"}", c.Room, c.Position, -1);
            }
            if (d < 0.22f && !(a.Peak <= 0f))
            {
                a.Healed = AilmentSystem.Spec(a.Id).Days; // 다음 틱에 낫는다 (기록 · 일기)
                Stats.Recovered++;
                continue;
            }
            a.Peak = MathF.Min(0.95f, 0.15f + 0.8f * d);
        }
    }

    /// <summary>주컴퓨터: 치료 침대 · 진단 스캐너의 생체 신호로 상한 장기를 읽는다.</summary>
    private void Vitals(CrewMember c, OrganBody b)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline || c.Room is not Room r || !r.DataLinked) return;
        bool onBed = c.CareBed != null || w.Ship.FurnitureAt(c.Cell)?.Type == FurnitureType.MedBed || b.Hooked;
        bool scan = r.Type == RoomType.Medbay && ModulesV15.Has(r, FurnitureType.DiagnosticScanner);
        if (!onBed && !scan) return;
        foreach (var ail in c.Ailments)
        {
            int i = Array.IndexOf(AilId, ail.Id);
            if (i < 0 || ail.Diagnosed) continue;
            ail.Diagnosed = true;
            Stats.Diagnosed++;
            w.Ailments.Stats.Diagnosed++;
            var o = (Organ)i;
            string advice = o switch
            {
                Organ.Kidney => w.Ship.FurnitureOf(FurnitureType.Dialyzer).Any(OrganGear.Sound) ? "투석이 필요하다" : "투석기가 없다 — 하나 짜 넣어야 한다",
                Organ.Lungs => b.Dmg[0] >= 0.7f ? (w.Ship.FurnitureOf(FurnitureType.Ecmo).Any(OrganGear.Sound) ? "인공 폐를 달아야 한다" : "인공 폐가 없다") : "누워서 쉬어야 한다",
                Organ.Heart => b.Dmg[1] >= 0.75f ? "인공 심장 · 이식을 생각해야 한다" : "무리하면 안 된다",
                _ => b.Dmg[2] >= 0.75f ? "이식 말고는 길이 없다" : "약을 줄여야 한다",
            };
            a.Speak.Announce(a.Voice.Style($"생체 신호 — {c.Name} {Name(o)} 기능 {(1f - b.Dmg[i]) * 100:0}% ({b.Cause[i] ?? "원인 모름"}) · {advice}"), r, 2);
        }
    }

    public string? Line(CrewMember c)
    {
        if (Peek(c) is not OrganBody b) return null;
        var bits = new List<string>();
        for (int i = 0; i < 4; i++)
            if (b.Dmg[i] >= 0.2f && Known(c, (Organ)i)) bits.Add($"{Name((Organ)i)} {(1f - b.Dmg[i]) * 100:0}%");
        if (b.Hooked && Machine(b) is Furniture f) bits.Add(f.Type == FurnitureType.Dialyzer ? "투석 중" : "인공 폐");
        if (b.Pump) bits.Add($"인공 심장 전지 {b.PumpCharge * 100:0}%");
        for (int i = 0; i < 4; i++) if (b.Graft[i] >= 0) bits.Add($"{P(b.Graft[i])?.Name ?? "누군가"}의 {Name((Organ)i)}");
        return bits.Count > 0 ? string.Join(" · ", bits) : null;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var b in _b.Values)
        {
            I(b.Crew);
            foreach (var d in b.Dmg) F(d);
            foreach (var g in b.Graft) I(g);
            F(b.Uremia); F(b.Reject); F(b.PumpCharge); I(b.Machine); I(b.Hooked ? 1 : 0); I(b.Doses);
        }
        foreach (var kv in _cell) { I(kv.Key); F(kv.Value); }
        I(Stats.Cases); I(Stats.Hooks); I(Stats.Sessions); I(Stats.Aborted); I(Stats.Guards);
    }
}

// ═══════════════════════════════ 사람이 하는 일 ═══════════════════════════════

/// <summary>투석 · 인공 폐: 진단받은 사람이 기계 곁 침대에 눕는다 (연결은 의무관 · 투석은 배운 사람이 스스로).</summary>
public sealed class OrganSupportActivity : Activity
{
    public override string Id => "organsupport";
    public override string Label => "투석 · 인공 폐";

    internal static Furniture? FreeMachine(World w, CrewMember c, FurnitureType t, DistanceField dist)
    {
        Furniture? best = null; int bd = int.MaxValue;
        foreach (var f in w.Ship.FurnitureOf(t))
        {
            if (!OrganGear.Sound(f) || f.UseSpots.Count == 0) continue;
            if (w.Organs.PatientOn(f) is CrewMember p && p != c) continue;
            if (f.ReservedBy != null && f.ReservedBy != c) continue;
            var spot = Spot(w, f, c);
            if (spot is not Cell s || !dist.Reachable(s)) continue;
            int d = dist.Get(s);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    /// <summary>기계 곁 자리: 붙은 치료 침대가 비었으면 거기, 아니면 기계 앞 바닥.</summary>
    internal static Cell? Spot(World w, Furniture f, CrewMember c)
    {
        foreach (var bed in f.Room.Furniture)
            if (bed.Type == FurnitureType.MedBed && (bed.ReservedBy == null || bed.ReservedBy == c) && bed.UseSpots.Count > 0 && (bed.UseSpots[0].Center - f.Center).Length() <= 2.3f) return bed.UseSpots[0];
        foreach (var s in f.UseSpots) if (!w.IsSpotTaken(s, c)) return s;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Dead || c.Away || c.Outside || c.Down || w.Organs.NeedsMachine(c) is not Organ o || !w.Organs.Known(c, o)) return (0f, "—");
        var t = OrganSystem.MachineFor(o)!.Value;
        if (FreeMachine(w, c, t, dist) == null) return (0f, $"{OrganGear.Name(t)} 없음");
        float u = w.Organs.Of(c).Uremia;
        return o == Organ.Lungs ? (1.02f, $"폐 기능 {(1f - w.Organs.Dmg(c, o)) * 100:0}% — 인공 폐에 눕는다") : (0.88f + 0.1f * MathF.Min(1f, u), $"투석할 때다 — 몸이 붓고 메스껍다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Organs.NeedsMachine(c) is not Organ o) return null;
        var t = OrganSystem.MachineFor(o)!.Value;
        if (FreeMachine(w, c, t, dist) is not Furniture f || Spot(w, f, c) is not Cell at) return null;
        var bed = w.Ship.FurnitureAt(at) is Furniture fb && fb.Type == FurnitureType.MedBed ? fb : null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        bool self = t == FurnitureType.Dialyzer && (c.SkillLevel(Skill.Medicine) >= 0.25f || w.Organs.Stats.Sessions + w.Organs.Of(c).Doses > 0 && w.Organs.Of(c).LastSession >= 0);
        long arrived = -1;
        toils.Add(new WaitToil(o == Organ.Lungs ? SimTime.Hours(24) : SimTime.Hours(6), Pose.Sleeping, f.Center)
        {
            EveryTick = (cm, world) =>
            {
                var b = world.Organs.Of(cm);
                if (arrived < 0) arrived = world.Tick;
                if (!b.Hooked && self && world.Tick - arrived >= SimTime.Minutes(20)) world.Organs.Hook(cm, f, cm); // 배운 사람은 바늘을 스스로 꽂는다
                world.Organs.Support(cm, f);
            },
            DoneWhen = (cm, world) =>
            {
                var b = world.Organs.Of(cm);
                bool done = o == Organ.Kidney ? b.Uremia <= 0.04f && b.Hooked : b.Dmg[0] < 0.45f || b.Graft[0] != -1;
                return done || !OrganGear.Sound(f) || b.Machine >= 0 && b.Machine != f.Id;
            },
        });
        var job = new Job(this, o == Organ.Lungs ? "인공 폐" : "투석", toils)
        {
            LogText = o == Organ.Lungs ? "숨이 차 인공 폐 곁 침대에 눕는다" : $"{f.Room.Name}에 투석하러 간다",
            TargetRoom = f.Room,
            InterruptMargin = 0.5f,
            OnFinished = (cm, world, _) => { if (world.Organs.Peek(cm) is OrganBody b && b.Machine == f.Id) world.Organs.Unhook(cm, b, o == Organ.Kidney && b.Uremia <= 0.05f ? "다 걸렀다" : null); },
        };
        job.Reserve(f, c);
        if (bed != null) job.Reserve(bed, c);
        return job;
    }
}

/// <summary>의무관: 기계 곁에 누운 사람에게 관을 꽂아 연결한다.</summary>
public sealed class OrganCareActivity : Activity
{
    public override string Id => "organcare";
    public override string Label => "인공 장기 연결";

    private static (CrewMember? pt, Furniture? f) Pick(CrewMember c, World w)
    {
        foreach (var b in w.Organs.Bodies)
        {
            if (b.Hooked || b.Crew == c.Id) continue;
            var pt = w.Crew.FirstOrDefault(x => x.Id == b.Crew);
            if (pt == null || pt.Dead || pt.Room == null || w.Organs.NeedsMachine(pt) is not Organ o) continue;
            var t = OrganSystem.MachineFor(o)!.Value;
            foreach (var f in pt.Room.Furniture)
                if (f.Type == t && OrganGear.Sound(f) && w.Organs.PatientOn(f) == null && (pt.Position - f.Center).Length() <= 2.4f && (pt.Down || pt.Pose == Pose.Sleeping || pt.Job?.Activity is OrganSupportActivity))
                    return (pt, f);
        }
        return (null, null);
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping || !Medic(c)) return (0f, "—");
        var (pt, f) = Pick(c, w);
        if (pt == null || f == null) return (0f, "—");
        return (f.Type == FurnitureType.Ecmo ? 1.04f : 0.9f, $"{pt.Name} — {f.Name}에 연결");
    }

    internal static bool Medic(CrewMember c) => c.Quals.Contains(Qual.Medic) || c.Role == CrewRole.Medic || c.SkillLevel(Skill.Medicine) >= 0.45f;

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (pt, f) = Pick(c, w);
        if (pt == null || f == null || RadCareActivity.Near(w, dist, pt) is not Cell at) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.AddRange(w.Soil.WashFirst(c, f.Type == FurnitureType.Ecmo, "관 꽂기"));
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.3f, Skill.Medicine, pt.Position) { CanContinue = (cm, _) => !pt.Dead && (pt.Position - cm.Position).Length() < 2.4f });
        toils.Add(new DoToil((cm, world) =>
        {
            if (world.Organs.Peek(pt)?.Hooked == true || world.Organs.PatientOn(f) != null) return true;
            world.Organs.Hook(pt, f, cm);
            world.Infection.Sterile(cm, pt, f.Room, "관 꽂은 자리");
            cm.Practice(Skill.Medicine, 0.03f);
            return true;
        }));
        return new Job(this, $"{pt.Name} {f.Name} 연결", toils) { LogText = $"{Ko.EulReul(pt.Name)} {f.Name}에 연결하러 간다", TargetRoom = f.Room, Urgent = f.Type == FurnitureType.Ecmo, InterruptMargin = 0.25f };
    }
}

/// <summary>인공 폐의 전지가 바닥났다 — 곁의 사람이 손 펌프를 돌린다.</summary>
public sealed class CrankActivity : Activity
{
    public override string Id => "organcrank";
    public override string Label => "손 펌프";

    private static Furniture? Dead(World w, CrewMember c)
    {
        foreach (var b in w.Organs.Bodies)
            if (b.Hooked && w.Organs.Machine(b) is Furniture f && f.Type == FurnitureType.Ecmo && !f.Machine!.Powered && w.Organs.Cell(f) < 0.25f && b.Crew != c.Id
                && (c.Room == f.Room || c.Room != null && (c.Position - f.Center).Length() < 14f)) return f;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping || Dead(w, c) is not Furniture f) return (0f, "—");
        return (1.08f, $"{f.Name} 전기가 끊겼다 — 손으로 돌린다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Dead(w, c) is not Furniture f) return null;
        Cell? at = f.UseSpots.Where(s => dist.Reachable(s)).OrderBy(s => dist.Get(s)).Cast<Cell?>().FirstOrDefault() ?? RadCareActivity.Near(w, dist, w.Organs.PatientOn(f)!);
        if (at is not Cell spot) return null;
        var toils = new List<Toil> { new GotoToil(spot), new WaitToil(SimTime.Hours(2), Pose.Working, f.Center)
        {
            EveryTick = (cm, world) => { if ((cm.Position - f.Center).Length() < 2.4f) world.Organs.Crank(cm, f); },
            DoneWhen = (cm, world) => f.Machine!.Powered || world.Organs.PatientOn(f) == null || cm.Needs.Rest < 0.08f,
        } };
        return new Job(this, "손 펌프", toils) { LogText = $"{f.Name} 손 펌프를 잡으러 뛴다", TargetRoom = f.Room, Urgent = true, InterruptMargin = 0.4f };
    }
}

/// <summary>인공 심장을 단 사람: 전지가 줄면 충전대 곁에 앉아 채운다.</summary>
public sealed class PumpChargeActivity : Activity
{
    public override string Id => "pumpcharge";
    public override string Label => "심장 전지 충전";

    private static Furniture? Dock(World w, DistanceField dist) =>
        w.Ship.FurnitureOf(FurnitureType.HeartPump).Where(f => OrganGear.Works(f) && f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0])).OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || w.Organs.Peek(c) is not { Pump: true } b || b.PumpCharge > (c.Job?.Activity is PumpChargeActivity ? 0.97f : 0.35f)) return (0f, "—");
        if (Dock(w, dist) == null) return (0f, "충전대가 멎었다");
        return (b.PumpCharge < 0.15f ? 1.1f : 0.8f, $"심장 전지 {b.PumpCharge * 100:0}% — 충전대로");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Dock(w, dist) is not Furniture f) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(f.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Hours(3), Pose.Sitting, f.Center)
        {
            EveryTick = (cm, world) => { if (OrganGear.Works(f) && world.Organs.Peek(cm) is OrganBody b) b.PumpCharge = MathF.Min(1f, b.PumpCharge + 0.6f / SimTime.TicksPerHour); },
            DoneWhen = (cm, world) => world.Organs.Peek(cm)?.PumpCharge >= 0.97f,
        });
        return new Job(this, "심장 전지 충전", toils) { LogText = "가슴의 펌프 전지를 채우러 간다", TargetRoom = f.Room, InterruptMargin = 0.4f }.Reserve(f, c);
    }
}

/// <summary>이식받은 사람: 하루에 한 번 면역억제제를 챙겨 먹는다 (떨어지면 거부반응).</summary>
public sealed class DoseActivity : Activity
{
    public override string Id => "immunodose";
    public override string Label => "면역억제제";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.Pose == Pose.Sleeping || w.Organs.Peek(c) is not OrganBody b || !b.NeedsSuppress) return (0f, "—");
        if (b.DoseAt >= 0 && w.Tick - b.DoseAt < SimTime.Hours(22)) return (0f, "먹었다");
        if (w.Ship.CountStored(ItemKind.Immunosuppressant) <= 0) return (0f, "약이 없다");
        return (0.7f + (b.DoseAt >= 0 && w.Tick - b.DoseAt > SimTime.Hours(30) ? 0.25f : 0f), "면역억제제 먹을 때");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (box, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Count(ItemKind.Immunosuppressant) > 0);
        if (box == null) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new DoToil((cm, world) =>
        {
            if (box.Storage!.Take(ItemKind.Immunosuppressant, 1) <= 0) return true;
            var b = world.Organs.Of(cm);
            b.DoseAt = world.Tick;
            b.Doses++;
            world.Organs.Stats.Doses++;
            world.Organs.Hurt(cm, Organ.Kidney, 0.002f, "면역억제제"); // 오래 먹으면 신장에 짐
            return true;
        }));
        return new Job(this, "면역억제제", toils) { LogText = "면역억제제를 챙겨 먹는다", TargetRoom = box.Room };
    }
}
