using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 의료 2차 — 감염 · 격리.
// 상처 감염(곪은 상처, 'woundinf')을 오래 두면 패혈증이 된다 — 몸 곳곳의 장기(신장 · 폐 · 간 · 심장)가 함께 상한다. 면역억제제를 먹는 사람은 더 쉽게.
// 멸균 · 소독: 수술 · 관 꽂기 때 멸균기(의무실) · 손 · 방의 균 · 소독약으로 곪을지 정해진다 — 실패하면 무엇이 모자랐는지 남는다.
// 옮는 병(열병 · 감기 · 장염 …)은 같은 방에서 옮고(이미 있는 규칙) · 환기관을 타고 다른 방으로도 조금 옮는다.
//   주컴퓨터: 앓는 사람이 있는 방의 댐퍼를 닫아 공기가 다른 방으로 가지 않게 한다 (CO₂가 차면 숨이 먼저 — 다시 연다) · 격리를 권한다.
//   음압기가 도는 방(음압 격리실)은 공기를 걸러 빼내 문 밖으로 새지 않는다 — 댐퍼를 닫지 않아도 되고 · CO₂도 덜 찬다.
// 격리 권고를 듣는 사람과 버티는 사람이 있다 (규칙 · 공동체 가치관 · 병이 무서운 사람).

public sealed class InfectionStats
{
    public int Sepsis, SterileFail, SterileOk, Airborne, Dampers, Reopened, Advised, Isolated, Refused, Caught, SepsisSeen;
    public string Line() => $"패혈증 {Sepsis}(생체 신호로 봄 {SepsisSeen}) · 소독 실패 {SterileFail}/{SterileFail + SterileOk} · 환기관으로 옮음 {Airborne} · 댐퍼 닫음 {Dampers}(다시 엶 {Reopened}) · 격리 권고 {Advised}(따름 {Isolated} · 버팀 {Refused}) · 면역 약해 옮음 {Caught}";
}

public sealed class InfectionSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6563 + 2711));
    public InfectionStats Stats { get; } = new();
    private readonly SortedDictionary<int, long> _advised = new();   // 사람 → 권고한 때
    private readonly SortedDictionary<int, long> _refused = new();
    private readonly SortedDictionary<int, long> _shut = new();      // 컴퓨터가 댐퍼를 닫은 방 → 때
    private readonly SortedDictionary<int, long> _told = new();
    private long _next, _last = -1;
    public static bool Off;

    public InfectionSystem(World w) => _w = w;

    public bool Advised(CrewMember c) => _advised.ContainsKey(c.Id);
    public bool Refuses(CrewMember c) => _refused.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(12);
    public bool Shut(Room r) => _shut.ContainsKey(r.Id);

    /// <summary>음압기가 도는 방.</summary>
    public static bool Negative(Room? r) => r != null && r.Furniture.Any(f => f.Type == FurnitureType.NegPressure && OrganGear.Works(f));

    /// <summary>격리할 방: 음압 격리실이 먼저, 없으면 격리실.</summary>
    public Room? Ward()
    {
        var w = _w;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.NegPressure).OrderBy(f => f.Id))
            if (OrganGear.Works(f) && !f.Room.Leaking && !f.Room.OffLimits) return f.Room;
        return Facilities.Best(w.Ship, "quarantine", r => !r.Detached && !r.OffLimits && !r.Leaking) is (Room q, >= 1f) ? q : null;
    }

    /// <summary>옮는 병을 앓는다 (무엇 · 세기 · 앓는 것 번호 — 열병이면 null).</summary>
    public (string name, float sev, string? id)? Contagious(CrewMember c)
    {
        var w = _w;
        if (c.Dead || c.Away) return null;
        if (DiseaseSystem.Sick(c) && w.Disease.Severity(c) > 0.12f) return ("열병", w.Disease.Severity(c), null);
        foreach (var a in c.Ailments)
        {
            var s = AilmentSystem.Spec(a.Id);
            if (s.Spread <= 0f) continue;
            float sev = w.Ailments.Severity(a);
            if (sev > 0.15f) return (s.Name, sev, a.Id);
        }
        return null;
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(10);
        float h = _last < 0 ? 1f / 6f : (w.Tick - _last) / (float)SimTime.TicksPerHour;
        _last = w.Tick;
        Sepsis(h);
        var sick = new List<(CrewMember c, string name, float sev, string? id)>();
        foreach (var c in w.Crew) if (Contagious(c) is (string n, float s, var id)) sick.Add((c, n, s, id));
        Air(sick, h);
        Advise(sick);
        Weak(sick, h);
    }

    /// <summary>곪은 상처 → 패혈증 → 장기.</summary>
    private void Sepsis(float h)
    {
        var w = _w;
        var a = w.Automation;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Ailments.Count == 0) continue;
            bool weak = w.Organs.Suppressed(c);
            var inf = c.Ailments.FirstOrDefault(x => x.Id == "woundinf");
            var sep = c.Ailments.FirstOrDefault(x => x.Id == "sepsis");
            if (sep == null && inf != null)
            {
                float sev = w.Ailments.Severity(inf);
                bool untreated = inf.TreatedAt < 0 || w.Tick - inf.TreatedAt > SimTime.Hours(24);
                if (sev > 0.35f && untreated && R.Chance(MathF.Min(0.6f, 0.05f * sev * h * (weak ? 2.5f : 1f) * (1f + c.Vitals.Frailty))))
                {
                    sep = w.Ailments.Catch(c, "sepsis", null, weak ? "곪은 상처 — 면역억제제로 몸이 약하다" : "곪은 상처를 오래 뒀다");
                    if (sep != null)
                    {
                        Stats.Sepsis++;
                        int parent = w.Causes.OpenNode($"ail:{c.Id}:woundinf");
                        w.Causes.Effect(CauseKind.Illness, $"ail:{c.Id}:sepsis", $"{c.Name} 패혈증 — 곪은 상처", c.Room, c.Position, parent);
                    }
                }
            }
            if (sep == null) continue;
            float s = w.Ailments.Severity(sep);
            if (s <= 0.05f) continue;
            // 몸 곳곳의 장기가 함께 상한다
            w.Organs.Hurt(c, Organ.Kidney, 0.05f * s * h, "패혈증");
            w.Organs.Hurt(c, Organ.Lungs, 0.03f * s * h, "패혈증");
            w.Organs.Hurt(c, Organ.Liver, 0.025f * s * h, "패혈증");
            w.Organs.Hurt(c, Organ.Heart, 0.015f * s * h, "패혈증");
            // 주컴퓨터: 열 · 맥박 · 숨 (생체 신호가 닿는 침대 · 의무실)
            if (!sep.Diagnosed && a.Present && a.MainOnline && c.Room is Room r && r.DataLinked && (c.CareBed != null || r.Type == RoomType.Medbay || w.Ship.FurnitureAt(c.Cell)?.Type == FurnitureType.MedBed))
            {
                sep.Diagnosed = true;
                Stats.SepsisSeen++;
                w.Ailments.Stats.Diagnosed++;
                a.Speak.Announce(a.Voice.Style($"생체 신호 — {c.Name} 고열 · 맥박이 빠르다 · 패혈증 징후 · 바로 약을"), r, 3);
            }
        }
    }

    /// <summary>환기관: 앓는 사람의 방에서 다른 방으로 조금씩 옮는다 — 주컴퓨터가 댐퍼를 닫는다 (숨이 먼저).</summary>
    private void Air(List<(CrewMember c, string name, float sev, string? id)> sick, float h)
    {
        var w = _w;
        var a = w.Automation;
        bool comp = a.Present && a.MainOnline;
        var hot = new SortedSet<int>();
        foreach (var (c, name, sev, id) in sick)
        {
            if (c.Room is not Room r || c.Outside) continue;
            hot.Add(r.Id);
            bool neg = Negative(r);
            // 컴퓨터: 앓는 사람이 있는 방의 댐퍼를 닫는다 (음압 격리실은 닫지 않아도 된다)
            if (comp && !neg && r.DataLinked && r.VentOpen && r.Air.CO2 < 1.0f && !r.Leaking && !r.Abandoned && !_shut.ContainsKey(r.Id))
            {
                r.VentOpen = false;
                _shut[r.Id] = w.Tick;
                Stats.Dampers++;
                a.Reason($"infdamper:{r.Id}", $"공기 — {r.Name} 댐퍼를 닫았다 ({c.Name} {name} · 다른 방으로 공기가 가지 않게)", SimTime.Hours(4));
            }
            if (neg || !Atmosphere.Vented(r)) continue;
            // 열린 환기관 — 다른 방 사람에게 조금
            foreach (var d in w.Crew)
            {
                if (d == c || d.Dead || d.Away || d.Outside || d.Room == null || d.Room == r || d.Suit != null || !Atmosphere.Vented(d.Room)) continue;
                if (id == null ? DiseaseSystem.Sick(d) || d.Immune : w.Ailments.Has(d, id) || d.AilmentImmune.Contains(id)) continue;
                float p = 0.004f * sev * h * (w.Organs.Suppressed(d) ? 3f : 1f) * (w.History.Doctrine.Quarantine ? 0.5f : 1f);
                if (!R.Chance(p)) continue;
                if (id == null) w.Disease.Infect(d, c); else w.Ailments.Catch(d, id, c);
                Stats.Airborne++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name}의 {name} — 환기관을 타고 {r.Name}에서 {d.Room.Name}의 {d.Name}에게 옮았다", d.Id);
            }
        }
        // 음압기: 걸러 빼낸다 (CO₂가 덜 찬다)
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.NegPressure))
        {
            if (!OrganGear.Works(f)) continue;
            f.Machine!.Active = hot.Contains(f.Room.Id) || f.Room.Air.CO2 > 0.4f;
            if (f.Machine.Active) f.Room.Air.CO2 = MathF.Max(0.04f, f.Room.Air.CO2 - 0.5f * h);
        }
        // 다시 연다: 앓는 사람이 떠났거나 · 숨이 차오른다
        foreach (var id in _shut.Keys.ToList())
        {
            var r = w.Ship.Rooms.FirstOrDefault(x => x.Id == id);
            if (r == null) { _shut.Remove(id); continue; }
            bool stuffy = r.Air.CO2 > 1.4f;
            if (hot.Contains(id) && !stuffy) continue;
            _shut.Remove(id);
            if (r.Leaking || r.Abandoned || r.Air.Smoke > 0.2f || r.VentOpen) continue;
            r.VentOpen = true;
            Stats.Reopened++;
            if (comp) a.Reason($"infdamper:{r.Id}", stuffy ? $"공기 — {r.Name} 이산화탄소가 찼다 · 숨이 먼저다 — 댐퍼를 다시 열었다" : $"공기 — {r.Name} 앓는 사람이 없다 · 댐퍼를 열었다", SimTime.Hours(1));
        }
    }

    /// <summary>격리 권고 (주컴퓨터 · 의무관이 진단한 뒤) — 듣는 사람 · 버티는 사람.</summary>
    private void Advise(List<(CrewMember c, string name, float sev, string? id)> sick)
    {
        var w = _w;
        var a = w.Automation;
        var ward = Ward();
        foreach (var k in _advised.Keys.ToList()) if (!sick.Any(s => s.c.Id == k)) _advised.Remove(k);
        foreach (var (c, name, sev, id) in sick)
        {
            if (_advised.ContainsKey(c.Id) || c.Room == ward) continue;
            bool known = id == null ? c.Ailments.Count == 0 || sev > 0.25f : c.Ailments.Any(x => x.Id == id && x.Diagnosed);
            bool comp = a.Present && a.MainOnline && c.Room is Room r && r.DataLinked;
            if (!known && !comp) continue;
            _advised[c.Id] = w.Tick;
            Stats.Advised++;
            string where = ward != null ? $"{Ko.EuRo(ward.Name)}" : "제 방에서 문을 닫고";
            if (comp) a.Speak.Announce(a.Voice.Style($"격리 권고 — {c.Name} {name} · {where}" + (ward != null && Negative(ward) ? " (음압)" : "")), c.Room, 2);
            else w.Log.Add(w.Tick, LogKind.Life, $"격리 권고 — {c.Name} {name}", c.Id);
            // 듣는가: 규칙 · 공동체를 중히 여기면 · 병이 무서우면
            var o = w.Values.Of(c);
            float comply = 0.55f + 0.3f * o.V[(int)Axis.Rule] + 0.25f * o.V[(int)Axis.Commune] + (c.Fears.Contains(Fear.Disease) ? 0.2f : 0f) - (Life.Has(c, Habit.Grumbler) ? 0.15f : 0f);
            if (comply < 0.35f)
            {
                _refused[c.Id] = w.Tick;
                Stats.Refused++;
                w.Log.Add(w.Tick, LogKind.Life, $"{c.Name} — 이 정도로 무슨 격리냐며 버틴다", c.Id);
                foreach (var d in w.Crew)
                    if (!d.Dead && d != c && d.Room == c.Room && d.Fears.Contains(Fear.Disease)) d.Needs.Stress = MathF.Min(1f, d.Needs.Stress + 0.1f);
            }
        }
    }

    /// <summary>면역억제제를 먹는 사람: 같은 방의 앓는 사람에게서 더 쉽게 옮는다.</summary>
    private void Weak(List<(CrewMember c, string name, float sev, string? id)> sick, float h)
    {
        var w = _w;
        foreach (var d in w.Crew)
        {
            if (d.Dead || d.Away || d.Room == null || d.Suit != null || !w.Organs.Suppressed(d)) continue;
            foreach (var (c, name, sev, id) in sick)
            {
                if (c == d || c.Room != d.Room) continue;
                if (id == null ? DiseaseSystem.Sick(d) || d.Immune : w.Ailments.Has(d, id) || d.AilmentImmune.Contains(id)) continue;
                if (!R.Chance(MathF.Min(0.5f, 0.12f * sev * h))) continue;
                if (id == null) w.Disease.Infect(d, c); else w.Ailments.Catch(d, id, c);
                Stats.Caught++;
            }
        }
    }

    /// <summary>멸균 · 소독: 수술 · 관 꽂기 뒤 곪는가 (멸균기 · 손 · 방의 균 · 소독약 · 약해진 몸).</summary>
    public bool Sterile(CrewMember by, CrewMember patient, Room? room, string what)
    {
        var w = _w;
        float p = 0.1f;
        string? why = null;
        bool autoclave = room != null && room.Furniture.Any(f => f.Type == FurnitureType.Autoclave);
        bool clave = autoclave && ModulesV15.Has(room, FurnitureType.Autoclave);
        if (clave) p *= 0.3f; else if (autoclave) { p *= 1.6f; why = "멸균기가 멎어 있었다"; }
        float hands = by.Soil.Hands[(int)SoilKind.Bio] + 0.5f * by.Soil.Hands[(int)SoilKind.Oil];
        if (hands > 0.3f) { p += 0.3f * hands; why ??= "손을 덜 씻었다"; }
        if (room != null) p += 0.25f * w.Soil.RoomSoil(room)[(int)SoilKind.Bio];
        if (ItemsV15.Use(w, ItemKind.Disinfectant)) p *= 0.6f; else { p *= 1.3f; why ??= "소독약이 떨어졌다"; }
        if (w.Organs.Suppressed(patient)) { p *= 1.8f; why ??= "면역억제제로 몸이 약하다"; }
        if (!R.Chance(MathF.Min(0.8f, p))) { Stats.SterileOk++; return true; }
        Stats.SterileFail++;
        w.Ailments.Catch(patient, "woundinf", null, $"{what}이(가) 곪았다 — {why ?? "균이 들었다"}");
        MarkLog.Add(by.Memory.Marks, w.Tick, $"{patient.Name} {what}이(가) 곪았다 ({why ?? "균"})");
        if (room != null) MarkLog.Add(room.Marks, w.Tick, $"소독이 덜 됐다 — {why ?? "균"}");
        return false;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var kv in _advised) { I(kv.Key); I(kv.Value); }
        foreach (var kv in _shut) { I(kv.Key); I(kv.Value); }
        I(Stats.Sepsis); I(Stats.SterileFail); I(Stats.Airborne); I(Stats.Dampers); I(Stats.Reopened); I(Stats.Caught);
    }
}

/// <summary>격리 권고를 들은 사람: 음압 격리실(없으면 격리실)로 가서 앓는다.</summary>
public sealed class IsolateActivity : Activity
{
    public override string Id => "isolate";
    public override string Label => "격리";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.Room == null || !w.Infection.Advised(c) || w.Infection.Contagious(c) is not (string name, _, _)) return (0f, "—");
        if (w.Infection.Refuses(c)) return (0f, "괜찮다며 버틴다");
        if (w.Infection.Ward() is not Room ward) return (0f, "격리할 방이 없다");
        return c.Room == ward ? (0.88f, $"{name} — 격리실에서 앓는다") : (0.93f, $"{name} — 격리 권고 · {Ko.EuRo(ward.Name)}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Infection.Ward() is not Room ward) return null;
        Cell? best = null;
        int bc = int.MaxValue;
        foreach (var cell in ward.Cells)
        {
            int d = dist.Get(cell);
            if (d < 0 || d >= bc || !w.Ship.IsOpenFloor(cell) && w.Ship.FurnitureAt(cell)?.Type != FurnitureType.MedBed || w.IsSpotTaken(cell, c)) continue;
            best = cell; bc = d;
        }
        if (best is not Cell at) return null;
        var toils = new List<Toil>();
        if (c.Cell != at) toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Hours(2), Pose.Sleeping));
        if (c.Room != ward) w.Infection.Stats.Isolated++;
        return new Job(this, "격리", toils) { LogText = c.Room == ward ? null : "옮기 전에 격리실로 간다", TargetRoom = ward };
    }
}
