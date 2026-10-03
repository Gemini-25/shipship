using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 의료 2차 — 장기 이식.
// 숨진 동료의 장기: 생전 뜻(가치관 · 믿음 — "남을 살리는 데 쓰라" / "몸을 온전히 보내 달라")과 남은 사람들(가까운 사람의 반발 · 지지)을
//   급히 모인 자리(회의)에서 몇십 분 안에 정한다 — 장기는 몇 시간이면 못 쓴다 (심장 6 · 폐 8 · 간 18 · 신장 36시간, 보관함 밖이면 4분의 1, 얼음이면 절반).
//   정해지면 의무관이 떼어 내 보관함에 넣는다. 결정에는 사람마다 가치관대로 마음이 남는다 (Values.React).
// 산 사람의 기증: 신장 하나 · 간 일부 — 받을 사람과 가까운 사람이 스스로 나선다 (희생 · 관계 · 기억).
// 조직 적합성: 사람마다 정해진 표지 여섯 개 (시드 · 사람 번호 — 결정론) · 맞는 수가 많을수록 수술이 잘 되고 거부반응이 덜하다.
// 거부반응: 면역억제제를 하루에 한 번 (재고 — 떨어지면 거부반응이 오른다 · 먹는 동안은 병에 잘 걸린다 → 감염).
// 배양 장기: 실험실 바이오 프린터가 제 세포로 찍는다 (적층 제작 · 재생 의학을 알아야 · 세포 잉크와 양액 · 전기가 두 시간 넘게 끊기면 세포가 죽는다).
// 수술: 지금은 치료 침대 + 의무관의 일. 성공률은 Chance 한 곳에 모은다 (나중에 수술 체계와 잇는다).
// 남는 것: "그 사람 심장으로 산다" — 기억 · 일기 · 연대기 · 숨진 사람과 가까웠던 사람의 마음.

public sealed class OrganGraft
{
    public int Id { get; init; }
    public Organ Organ { get; init; }
    public int Donor { get; init; } = -1;
    public string DonorName { get; init; } = "";
    public long At { get; init; }
    public bool Printed { get; init; }
    public bool Living { get; init; }
    public int[] Tissue { get; init; } = Array.Empty<int>();
    public int Cooler { get; set; } = -1;   // 넣어 둔 보관함 (가구 번호)
    public int Printer { get; set; } = -1;  // 찍어 둔 프린터
    public bool Ice { get; set; }
    public float Age { get; set; }          // 찬 곳 기준으로 흐른 시간
    public int For { get; set; } = -1;      // 받을 사람
    public bool Used { get; set; }
    public bool Spoiled { get; set; }
    public bool Gone => Used || Spoiled;
}

public sealed class DonorCase
{
    public int Crew { get; init; }
    public long DiedAt { get; init; }
    public int Wish { get; init; }           // +1 나눠라 · −1 온전히 · 0 말한 적 없다
    public string WishWhy { get; init; } = "";
    public int Verdict { get; set; }         // +1 · −1 · 0 아직
    public int Yes { get; set; }
    public int No { get; set; }
    public bool Harvested { get; set; }
    public int Harvester { get; set; } = -1;
    public bool Closed { get; set; }
}

public enum OpKind : byte { Graft, Pump, Take }

/// <summary>수술 차례 하나 (받는 사람 · 장기 / 인공 심장 / 산 사람에게서 떼기).</summary>
public sealed class OpCase
{
    public int Id { get; init; }
    public OpKind Kind { get; init; }
    public int Patient { get; init; }        // 눕는 사람 (받는 사람 · 주는 사람)
    public int Graft { get; set; } = -1;
    public int For { get; init; } = -1;      // 떼기: 받을 사람
    public Organ Organ { get; init; }
    public int Surgeon { get; set; } = -1;
    public long Claimed { get; set; } = -1;
    public bool Done { get; set; }
    public long Opened { get; init; }
}

public sealed class PrintJob
{
    public int Printer { get; init; }
    public int For { get; init; }
    public Organ Organ { get; init; }
    public float Progress { get; set; }
    public float Dark { get; set; }          // 전기 없이 지난 시간
    public bool Started { get; set; }
    public bool Failed { get; set; }
}

public sealed class TransplantStats
{
    public int Donors, Consent, Refused, Harvested, Missed, Grafts, Spoiled, Ops, Success, Failed, Living, Pumps, Printed, PrintFailed, Rejections, Offers;
    public string Line() => $"기증 {Donors}(나눔 {Consent} · 거절 {Refused} · 놓침 {Missed}) · 떼어 낸 장기 {Grafts}(상함 {Spoiled}) · 수술 {Ops}(성공 {Success} · 실패 {Failed}) · 산 사람 기증 {Living}(나섬 {Offers}) · 인공 심장 {Pumps} · 배양 {Printed}(실패 {PrintFailed}) · 거부반응 {Rejections}";
}

public sealed class TransplantSystem
{
    private readonly World _w;
    public List<OrganGraft> Grafts { get; } = new();
    public SortedDictionary<int, DonorCase> Donors { get; } = new();
    public List<OpCase> Ops { get; } = new();
    public List<PrintJob> Prints { get; } = new();
    public TransplantStats Stats { get; } = new();
    private readonly SortedDictionary<int, long> _asked = new();
    private readonly SortedDictionary<int, long> _told = new();
    private long _next, _last = -1;
    private int _ids, _opIds;
    public float LastChance { get; private set; } = -1f;
    public static bool Off;

    public TransplantSystem(World w) => _w = w;

    private CrewMember? P(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }

    // ───────────────────────────── 조직 · 시간 ─────────────────────────────

    /// <summary>조직 표지 여섯 개 (사람마다 정해져 있다 — 시드 · 사람 번호).</summary>
    public int[] Tissue(CrewMember c)
    {
        var r = new Rng(unchecked(_w.Seed * 31337 + c.Id * 7919 + 5));
        var t = new int[6];
        for (int i = 0; i < 6; i++) t[i] = r.Range(0, 3);
        return t;
    }

    /// <summary>맞는 정도 0~1 (같은 자리의 표지가 같은 수 / 6).</summary>
    public static float MatchOf(int[] a, int[] b)
    {
        if (a.Length != 6 || b.Length != 6) return 1f;
        int n = 0;
        for (int i = 0; i < 6; i++) if (a[i] == b[i]) n++;
        return n / 6f;
    }

    public float Match(OrganGraft g, CrewMember to) => g.Printed ? 1f : MatchOf(g.Tissue, Tissue(to));

    /// <summary>찬 곳에서 버티는 시간.</summary>
    public static float Limit(Organ o) => o switch { Organ.Heart => 6f, Organ.Lungs => 8f, Organ.Liver => 18f, _ => 36f };
    public float Quality(OrganGraft g) => Math.Clamp(1f - g.Age / (g.Printed ? 72f : Limit(g.Organ)), 0f, 1f);

    public static bool PrintTech(World w) => w.Eras.Has("regenmed") || w.Eras.Has("additive");

    /// <summary>수술이 잘 될 확률 — 한 곳에 모은다 (솜씨 · 맞는 정도 · 장기의 신선도 · 침대 · 전기 · 받는 몸 · 기술).</summary>
    public static float Chance(World w, CrewMember surgeon, CrewMember patient, OrganGraft? g, Furniture? bed, OpKind kind = OpKind.Graft)
    {
        float p = 0.55f + 0.35f * surgeon.SkillLevel(Skill.Medicine);
        if (g != null)
        {
            float match = w.Transplant.Match(g, patient);
            p += 0.2f * (match - 0.5f);
            p -= 0.3f * (1f - w.Transplant.Quality(g));
            if (g.Printed) p += 0.08f;
            if (g.Living) p += 0.05f;
        }
        if (kind == OpKind.Take) p += 0.25f; // 건강한 몸에서 떼는 것
        if (kind == OpKind.Pump) p += 0.05f;
        bool works = bed?.Machine is Machine m && m.Efficiency > 0f;
        p += works ? 0.05f : -0.15f;
        if (patient.Room is Room r && !r.Powered) p -= 0.2f;
        p -= 0.3f * MathF.Max(0f, 0.5f - patient.Vitals.Health);
        p -= 0.1f * patient.Vitals.Frailty;
        if (w.Eras.Has("regenmed")) p += 0.05f;
        if (surgeon.Needs.Rest < 0.2f) p -= 0.1f;
        p += w.Surgery.RoomFactor(surgeon, bed?.Room ?? patient.Room); // 의료 1차 수술실 사정 (집도의 마음 · 수술대 · 무영등 · 멸균)
        return Math.Clamp(p, 0.05f, 0.95f);
    }

    /// <summary>주컴퓨터가 지킬 회로: 장기가 든 보관함 · 세포를 찍는 프린터 · 전지가 줄어든 사람이 앉은 충전대.</summary>
    public IEnumerable<int> Guarded()
    {
        foreach (var g in Grafts) if (!g.Gone && g.Cooler >= 0) yield return g.Cooler;
        foreach (var j in Prints) if (j.Started && !j.Failed) yield return j.Printer;
        foreach (var b in _w.Organs.Bodies)
            if (b.Pump && b.PumpCharge < 0.5f)
                foreach (var f in _w.Ship.FurnitureOf(FurnitureType.HeartPump)) if (f.ReservedBy?.Id == b.Crew) yield return f.Id;
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(10);
        float h = _last < 0 ? 1f / 6f : (w.Tick - _last) / (float)SimTime.TicksPerHour;
        _last = w.Tick;
        Deaths();
        Keep(h);
        Assign();
        Living();
        Pumps();
        Printing(h);
        Rejection(h);
        Ops.RemoveAll(o => o.Done && w.Tick - o.Opened > SimTime.TicksPerDay * 3);
    }

    /// <summary>숨진 사람: 쓸 만한 장기가 있고 · 기다리는 사람이 있거나 보관함이 있으면 바로 정한다.</summary>
    private void Deaths()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (!c.Dead || c.DiedAt < 0 || Donors.ContainsKey(c.Id) || w.Tick - c.DiedAt > SimTime.Hours(2)) continue;
            if (c.Outside || c.Room == null || c.Away || c.IsChild) continue;
            var viable = OrganSystem.All.Where(o => Viable(c, o)).ToList();
            if (viable.Count == 0) continue;
            bool want = viable.Any(o => Waiting(o).Any()) || w.Ship.FurnitureOf(FurnitureType.OrganCooler).Any(OrganGear.Sound);
            if (!want) continue;
            var (wish, why) = Wish(c);
            var d = new DonorCase { Crew = c.Id, DiedAt = c.DiedAt, Wish = wish, WishWhy = why };
            Donors[c.Id] = d;
            Stats.Donors++;
            Decide(c, d, viable);
        }
        // 시간 안에 못 뗐다
        foreach (var d in Donors.Values)
            if (!d.Closed && d.Verdict > 0 && !d.Harvested && w.Tick - d.DiedAt > SimTime.Hours(4))
            {
                d.Closed = true;
                Stats.Missed++;
                var c = P(d.Crew);
                w.Log.Add(w.Tick, LogKind.Warning, $"{c?.Name ?? "숨진 사람"}의 장기 — 시간 안에 떼지 못했다");
            }
    }

    /// <summary>쓸 수 있는 장기: 덜 상했고 · 불에 탄 몸이 아니다.</summary>
    public bool Viable(CrewMember dead, Organ o)
    {
        if (_w.Organs.Dmg(dead, o) >= 0.45f) return false;
        if (_w.Organs.Peek(dead) is OrganBody b && b.Graft[(int)o] != -1) return false;
        string cause = dead.Vitals.InjuryCause ?? "";
        if (cause.Contains("화재") || cause.Contains("불") || cause.Contains("폭발")) return o == Organ.Kidney && !cause.Contains("폭발");
        if (o == Organ.Lungs && (cause.Contains("연기") || cause.Contains("가스") || cause.Contains("감압") || cause.Contains("질식"))) return false;
        if (o == Organ.Heart && cause.Contains("심")) return false;
        return true;
    }

    /// <summary>그 장기를 기다리는 사람 (가장 급한 사람부터).</summary>
    public IEnumerable<CrewMember> Waiting(Organ o) =>
        _w.Crew.Where(c => !c.Dead && !c.Away && _w.Organs.Peek(c) is OrganBody b && b.Graft[(int)o] == -1 && (b.Dmg[(int)o] >= 0.6f || o == Organ.Kidney && b.Uremia >= 0.5f) && !(o == Organ.Heart && b.Pump && b.Dmg[1] < 0.6f))
            .OrderByDescending(c => _w.Organs.Dmg(c, o)).ThenBy(c => c.Id);

    /// <summary>생전 뜻: 가치관(동정 · 공동체)이 크면 나누라고 했고 · 믿음(미신 · 명상)이 깊으면 몸을 온전히 보내 달라고 했다.</summary>
    public (int wish, string why) Wish(CrewMember c)
    {
        var o = _w.Values.Of(c);
        float give = o.V[(int)Axis.Mercy] + 0.6f * o.V[(int)Axis.Commune];
        bool faith = Life.Has(c, Habit.Superstitious) || c.Hobbies.Contains(Hobby.Meditation);
        if (faith && give < 0.7f) return (-1, "몸을 온전히 보내 달라고 했었다");
        if (give >= 0.35f) return (1, "가면 쓸 수 있는 건 남을 살리는 데 쓰라고 했었다");
        return (0, "그런 얘기를 한 적이 없다");
    }

    private static readonly float[] GiveVec = { 0f, 0.5f, 0f, 0.6f };

    /// <summary>급히 모인 자리: 사람마다 가치관 · 숨진 사람과의 사이 · 기다리는 사람과의 사이로 찬반 → 본인 뜻을 먼저 따른다.</summary>
    private void Decide(CrewMember dead, DonorCase d, List<Organ> viable)
    {
        var w = _w;
        var waiting = viable.SelectMany(Waiting).Distinct().ToList();
        var vec = new[] { 0f, 0.5f, 0.6f * d.Wish, 0.6f };
        int yes = 0, no = 0;
        var voters = new List<int>();
        string? loud = null;
        float loudest = 0f;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.IsChild) continue;
            float s = Stance(c, dead, d, waiting);
            voters.Add(c.Id);
            if (s > 0.1f) yes++; else if (s < -0.1f) no++;
            if (MathF.Abs(s) > loudest && dead.AffinityTo(c) + c.AffinityTo(dead) > 0.4f) { loudest = MathF.Abs(s); loud = s > 0 ? $"{Ko.IGa(c.Name)} 그 사람도 그걸 바랐을 거라고 했다" : $"{Ko.IGa(c.Name)} 그 몸에 손대지 말라고 했다"; }
        }
        d.Yes = yes; d.No = no;
        d.Verdict = d.Wish < 0 ? -1 : d.Wish > 0 ? 1 : yes > no ? 1 : -1;
        if (d.Verdict > 0) Stats.Consent++; else Stats.Refused++;
        string organs = string.Join(" · ", viable.Select(OrganSystem.Name).Distinct());
        string title = d.Verdict > 0 ? $"{dead.Name}의 장기를 나누기로 했다" : $"{dead.Name}의 몸은 그대로 보내기로 했다";
        w.History.Add(w, HistoryKind.Decision, $"회의 — {title} ({d.WishWhy} · 찬성 {yes} · 반대 {no})" + (loud != null ? $" — {loud}" : ""), dead.Room, new[] { dead }, log: true);
        // 사람마다 그 결정에 대한 마음 (가치관 · 자기 생각과 같았나)
        w.Values.React(-1, -2, title, vec, 0.6f, voters: voters, extra: c => 0.5f * Stance(c, dead, d, waiting) * (d.Verdict > 0 ? 1f : -1f));
        foreach (var c in w.Crew)
            if (!c.Dead && dead.AffinityTo(c) + c.AffinityTo(dead) > 0.5f)
                MarkLog.Add(c.Memory.Marks, w.Tick, d.Verdict > 0 ? $"{dead.Name}의 장기를 나누기로 했다" : $"{dead.Name}을(를) 온전히 보냈다");
        // 주컴퓨터: 시간과 맞는 사람
        var a = w.Automation;
        if (d.Verdict > 0 && a.Present && a.MainOnline)
        {
            bool cool = w.Ship.FurnitureOf(FurnitureType.OrganCooler).Any(OrganGear.Works);
            var best = waiting.Select(c => (c, m: MatchOf(Tissue(dead), Tissue(c)))).OrderByDescending(x => x.m).FirstOrDefault();
            a.Speak.Announce(a.Voice.Style($"{dead.Name}의 {organs} — " + string.Join(" · ", viable.Select(o => $"{OrganSystem.Name(o)} {Limit(o) / (cool ? 1f : 4f):0.#}시간")) + (cool ? " (보관함)" : " 안에 · 보관함이 없다")
                + (best.c != null ? $" · 조직이 맞는 사람 {best.c.Name} {best.m * 6:0}/6" : "")), dead.Room, 2);
        }
    }

    private float Stance(CrewMember c, CrewMember dead, DonorCase d, List<CrewMember> waiting)
    {
        var w = _w;
        var vec = new[] { 0f, 0.5f, 0.6f * d.Wish, 0.6f };
        float s = w.Values.Lean(c, vec);
        float close = MathF.Max(0f, c.AffinityTo(dead));
        s += d.Wish != 0 ? 0.5f * d.Wish * close : -0.35f * close; // 가까운 사람: 본인 뜻을 지키려 하고 · 뜻을 모르면 손대기 싫다
        if (Life.Has(c, Habit.Superstitious)) s -= 0.25f;
        if (waiting.Contains(c)) s += 0.8f;
        else foreach (var x in waiting) s += 0.4f * MathF.Max(0f, c.AffinityTo(x));
        return s;
    }

    /// <summary>의무관이 떼어 낸다 (HarvestActivity의 끝).</summary>
    public void Harvest(CrewMember medic, CrewMember dead)
    {
        var w = _w;
        if (!Donors.TryGetValue(dead.Id, out var d) || d.Harvested) return;
        d.Harvested = true;
        d.Harvester = medic.Id;
        Stats.Harvested++;
        var cooler = w.Ship.FurnitureOf(FurnitureType.OrganCooler).Where(OrganGear.Works).OrderBy(f => (f.Center - dead.Position).LengthSquared()).ThenBy(f => f.Id).FirstOrDefault();
        var tissue = Tissue(dead);
        float age = (w.Tick - dead.DiedAt) / (float)SimTime.TicksPerHour * 2f; // 숨진 뒤 따뜻하게 흐른 시간
        var made = new List<string>();
        foreach (var o in OrganSystem.All)
        {
            if (!Viable(dead, o)) continue;
            int n = o == Organ.Kidney ? 2 : 1;
            for (int k = 0; k < n; k++)
            {
                var g = new OrganGraft { Id = ++_ids, Organ = o, Donor = dead.Id, DonorName = dead.Name, At = w.Tick, Tissue = tissue, Cooler = cooler?.Id ?? -1, Age = age };
                if (cooler == null && ItemsV15.Use(w, ItemKind.Ice)) g.Ice = true;
                Grafts.Add(g);
                Stats.Grafts++;
            }
            made.Add(OrganSystem.Name(o) + (n > 1 ? " 둘" : ""));
        }
        string where = cooler != null ? $"{cooler.Room.Name} 보관함에 넣었다" : Grafts.Any(g => g.Donor == dead.Id && g.Ice) ? "얼음에 묻었다" : "찰 곳이 없다";
        w.Log.Add(w.Tick, LogKind.Work, $"{dead.Name}의 {string.Join(" · ", made)} — 떼어 내 {where}", medic.Id);
        MarkLog.Add(medic.Memory.Marks, w.Tick, $"{dead.Name}의 몸에서 장기를 뗐다");
        Memory.Shake(w, medic, 0.04f, $"{dead.Name}의 몸에 칼을 댔다");
        if (cooler != null) MarkLog.Add(cooler.Machine!.Marks, w.Tick, $"{dead.Name}의 장기");
        Assign();
    }

    /// <summary>보관: 보관함이 돌면 천천히 · 얼음은 두 배 · 밖은 네 배로 시간이 흐른다.</summary>
    private void Keep(float h)
    {
        var w = _w;
        foreach (var g in Grafts)
        {
            if (g.Gone) continue;
            Furniture? box = g.Cooler >= 0 ? w.Ship.Furniture[g.Cooler] : g.Printer >= 0 ? w.Ship.Furniture[g.Printer] : null;
            bool cold = box != null && OrganGear.Works(box);
            g.Age += h * (cold ? 1f : g.Ice ? 2f : g.Living ? 1f : 4f);
            if (box != null && !cold && g.Cooler >= 0 && w.Automation.Present && w.Automation.MainOnline && Tell(box.Id * 4 + 1, SimTime.Hours(1)))
                w.Automation.Speak.Announce(w.Automation.Voice.Style($"{box.Room.Name} 장기 보관함이 멎었다 — {g.DonorName}의 {OrganSystem.Name(g.Organ)}이(가) 데워진다 · {Math.Max(0f, (1f - Quality(g)) < 1f ? (Limit(g.Organ) - g.Age) / 4f : 0f):0.#}시간"), box.Room, 3);
            if (Quality(g) > 0f) continue;
            g.Spoiled = true;
            Stats.Spoiled++;
            w.Log.Add(w.Tick, LogKind.Warning, $"{g.DonorName}의 {OrganSystem.Name(g.Organ)} — 시간이 지나 쓸 수 없게 됐다");
            if (P(g.For) is CrewMember to && !to.Dead) { MarkLog.Add(to.Memory.Marks, w.Tick, $"기다리던 {OrganSystem.Name(g.Organ)}이 상했다"); to.Needs.Stress = MathF.Min(1f, to.Needs.Stress + 0.2f); }
        }
    }

    /// <summary>받을 사람을 고른다 (맞는 정도 · 급한 정도) · 수술 차례를 연다.</summary>
    private void Assign()
    {
        var w = _w;
        foreach (var g in Grafts)
        {
            if (g.Gone || g.For >= 0 && P(g.For) is CrewMember f0 && !f0.Dead) continue;
            g.For = -1;
            CrewMember? best = null; float bs = float.MinValue;
            foreach (var c in Waiting(g.Organ))
            {
                if (Grafts.Any(x => !x.Gone && x != g && x.For == c.Id && x.Organ == g.Organ)) continue;
                float m = Match(g, c);
                if (g.Living && m < 0.15f) continue;
                float s = 2f * m + w.Organs.Dmg(c, g.Organ);
                if (s > bs) { bs = s; best = c; }
            }
            if (best == null) continue;
            g.For = best.Id;
            Ops.Add(new OpCase { Id = ++_opIds, Kind = OpKind.Graft, Patient = best.Id, Graft = g.Id, Organ = g.Organ, Opened = w.Tick });
            var a = w.Automation;
            string from = g.Printed ? "제 세포로 찍은" : g.Living ? $"{g.DonorName}이(가) 준" : $"{g.DonorName}의";
            w.Log.Add(w.Tick, LogKind.Warning, $"이식 — {best.Name} ← {from} {OrganSystem.Name(g.Organ)} (조직 {Match(g, best) * 6:0}/6)", best.Id);
            if (a.Present && a.MainOnline) a.Speak.Announce(a.Voice.Style($"이식 — {best.Name}에게 {from} {OrganSystem.Name(g.Organ)} · 조직 {Match(g, best) * 6:0}/6 · 의무실 치료 침대로"), best.Room, 2);
        }
    }

    /// <summary>산 사람의 기증: 신장이 다 상했거나 간이 무너지는데 줄 장기가 없으면 — 가까운 사람이 스스로 나선다.</summary>
    private void Living()
    {
        var w = _w;
        foreach (var o in new[] { Organ.Kidney, Organ.Liver })
            foreach (var rc in Waiting(o).ToList())
            {
                if (w.Organs.Dmg(rc, o) < (o == Organ.Kidney ? 0.75f : 0.8f)) continue;
                if (Grafts.Any(g => !g.Gone && g.For == rc.Id && g.Organ == o) || Ops.Any(x => !x.Done && (x.For == rc.Id || x.Patient == rc.Id) && x.Organ == o)) continue;
                if (Prints.Any(j => !j.Failed && j.For == rc.Id && j.Organ == o)) continue;
                if (_asked.TryGetValue(rc.Id * 4 + (int)o, out var t) && w.Tick - t < SimTime.Hours(36)) continue;
                _asked[rc.Id * 4 + (int)o] = w.Tick;
                CrewMember? best = null; float bw = 0.55f;
                foreach (var c in w.Crew)
                {
                    if (c == rc || c.Dead || c.Away || c.IsChild || c.Down || c.Vitals.Health < 0.75f || c.Vitals.Injury > 0.15f) continue;
                    var cb = w.Organs.Peek(c);
                    if (cb != null && (cb.Dmg[(int)o] > 0.15f || o == Organ.Kidney && cb.OneKidney || o == Organ.Liver && cb.LiverPart)) continue;
                    if (MatchOf(Tissue(c), Tissue(rc)) < 0.3f) continue;
                    var ov = w.Values.Of(c);
                    float will = 0.9f * c.AffinityTo(rc) + 0.35f * ov.V[(int)Axis.Mercy] + 0.2f * ov.V[(int)Axis.Commune] + 0.2f * (c.Traits.Bravery - 0.5f)
                                 + (Memory.AreComrades(c, rc) ? 0.2f : 0f) - (c.Fears.Contains(Fear.Disease) ? 0.1f : 0f);
                    if (will > bw) { bw = will; best = c; }
                }
                var a = w.Automation;
                if (best == null)
                {
                    if (a.Present && a.MainOnline && Tell(rc.Id * 4 + 3, SimTime.Hours(24)))
                        a.Reason($"living:{rc.Id}", $"{rc.Name} {OrganSystem.Name(o)} — 줄 장기가 없다 · 조직이 맞는 사람 {w.Crew.Count(c => c != rc && !c.Dead && MatchOf(Tissue(c), Tissue(rc)) >= 0.34f)}명 (나서는 사람은 없다)", SimTime.Hours(24));
                    continue;
                }
                Stats.Offers++;
                Ops.Add(new OpCase { Id = ++_opIds, Kind = OpKind.Take, Patient = best.Id, For = rc.Id, Organ = o, Opened = w.Tick });
                string part = o == Organ.Kidney ? "신장 하나" : "간 일부";
                w.Log.Add(w.Tick, LogKind.Warning, $"{best.Name} — {rc.Name}에게 {Ko.EulReul(part)} 주겠다고 나섰다", best.Id);
                w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(best.Name)} {rc.Name}에게 {Ko.EulReul(part)} 주겠다고 나섰다", best.Room, new[] { best, rc });
                best.Say(w, Persona.Say(best, o == Organ.Kidney ? "하나면 충분해. 나머지 하나 가져가" : "간은 다시 자란다잖아. 떼어 가"));
                Life.Diary(w, best, $"{rc.Name}에게 {Ko.EulReul(part)} 주기로 했다.");
                break;
            }
    }

    /// <summary>심장이 다 상했고 이식할 심장이 없으면 — 인공 심장을 단다.</summary>
    private void Pumps()
    {
        var w = _w;
        if (!w.Ship.FurnitureOf(FurnitureType.HeartPump).Any(OrganGear.Works)) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || w.Organs.Peek(c) is not OrganBody b || b.Pump || b.Dmg[1] < 0.75f || b.Graft[1] != -1) continue;
            if (Grafts.Any(g => !g.Gone && g.For == c.Id && g.Organ == Organ.Heart) || Ops.Any(x => !x.Done && x.Patient == c.Id && x.Kind == OpKind.Pump)) continue;
            if (!w.Organs.Known(c, Organ.Heart)) continue;
            Ops.Add(new OpCase { Id = ++_opIds, Kind = OpKind.Pump, Patient = c.Id, Organ = Organ.Heart, Opened = w.Tick });
            w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name} — 심장이 버티지 못한다 · 인공 심장을 달기로 했다", c.Id);
        }
    }

    /// <summary>바이오 프린터: 장기를 기다리는 사람이 있으면 실험실 사람이 세포를 건다 (PrintActivity) · 전기가 오래 끊기면 세포가 죽는다.</summary>
    private void Printing(float h)
    {
        var w = _w;
        foreach (var j in Prints)
        {
            if (!j.Started || j.Failed || j.Progress >= 1f) continue;
            var f = w.Ship.Furniture[j.Printer];
            if (!OrganGear.Sound(f)) { j.Dark += h; }
            else if (!f.Machine!.Powered) j.Dark += h;
            else { j.Dark = MathF.Max(0f, j.Dark - h * 0.25f); j.Progress += h / (j.Organ == Organ.Kidney ? 40f : 52f) * (w.Eras.Has("regenmed") ? 1.3f : 1f); }
            f.Machine!.Active = true;
            if (j.Dark > 2f)
            {
                j.Failed = true;
                Stats.PrintFailed++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{f.Room.Name} 바이오 프린터 — 전기가 오래 끊겨 세포가 죽었다 ({OrganSystem.Name(j.Organ)})");
                MarkLog.Add(f.Machine.Marks, w.Tick, "세포가 죽었다");
                continue;
            }
            if (j.Progress < 1f) continue;
            var to = P(j.For);
            var g = new OrganGraft { Id = ++_ids, Organ = j.Organ, Donor = -2, DonorName = to?.Name ?? "", At = w.Tick, Printed = true, Printer = f.Id, Tissue = to != null ? Tissue(to) : Array.Empty<int>(), For = -1 };
            Grafts.Add(g);
            Stats.Printed++;
            f.Machine.Active = false;
            w.Log.Add(w.Tick, LogKind.Work, $"바이오 프린터 — {to?.Name ?? "누군가"}의 세포로 {OrganSystem.Name(j.Organ)}을(를) 다 찍었다");
            w.History.Add(w, HistoryKind.Upgrade, $"{f.Room.Name}에서 처음으로 장기를 찍어 냈다 — {to?.Name}의 {OrganSystem.Name(j.Organ)}", f.Room, to != null ? new[] { to } : null);
        }
        Prints.RemoveAll(j => j.Failed && w.Tick % SimTime.TicksPerDay < 20 || j.Progress >= 1f);
        Assign();
    }

    /// <summary>찍을 일: 장기를 기다리는 사람 · 맞는 프린터 · 기술 · 잉크.</summary>
    public (Furniture? f, CrewMember? to, Organ o) PrintWanted()
    {
        var w = _w;
        if (!PrintTech(w) || Prints.Any(j => !j.Failed && j.Progress < 1f)) return (null, null, Organ.Kidney);
        var f = w.Ship.FurnitureOf(FurnitureType.BioPrinter).Where(OrganGear.Works).OrderBy(x => x.Id).FirstOrDefault();
        if (f == null || w.Ship.CountStored(ItemKind.BioInk) <= 0 || w.Ship.CountStored(ItemKind.Nutrient) <= 0) return (null, null, Organ.Kidney);
        foreach (var o in new[] { Organ.Kidney, Organ.Liver })
            foreach (var c in Waiting(o))
                if (!Grafts.Any(g => !g.Gone && g.For == c.Id && g.Organ == o)) return (f, c, o);
        return (null, null, Organ.Kidney);
    }

    public void StartPrint(CrewMember by, Furniture f, CrewMember to, Organ o)
    {
        var w = _w;
        if (!ItemsV15.Use(w, ItemKind.BioInk) || !ItemsV15.Use(w, ItemKind.Nutrient)) return;
        Prints.Add(new PrintJob { Printer = f.Id, For = to.Id, Organ = o, Started = true });
        w.Log.Add(w.Tick, LogKind.Work, $"바이오 프린터에 {to.Name}의 세포를 걸었다 — {OrganSystem.Name(o)} (이틀 남짓)", by.Id);
        MarkLog.Add(f.Machine!.Marks, w.Tick, $"{by.Name}: {to.Name}의 {OrganSystem.Name(o)}을(를) 걸었다");
        by.Practice(Skill.Medicine, 0.03f);
    }

    /// <summary>거부반응: 약을 거르면 오르고 · 먹으면 가라앉는다 (맞는 정도가 낮을수록 빨리).</summary>
    private void Rejection(float h)
    {
        var w = _w;
        float day = h / 24f;
        var a = w.Automation;
        foreach (var b in w.Organs.Bodies)
        {
            if (!b.NeedsSuppress || P(b.Crew) is not CrewMember c || c.Dead) continue;
            bool dosed = w.Organs.Suppressed(c);
            float worst = 0f;
            for (int i = 0; i < 4; i++) if (b.Graft[i] >= 0) worst = MathF.Max(worst, 1f - b.Match[i]);
            float before = b.Reject;
            b.Reject = Math.Clamp(b.Reject + (dosed ? -0.15f : 0.05f + 0.35f * worst) * day, 0f, 1f);
            if (b.Reject >= 0.35f)
            {
                for (int i = 0; i < 4; i++) if (b.Graft[i] >= 0) w.Organs.Hurt(c, (Organ)i, 0.12f * (b.Reject - 0.25f) * day, "거부반응");
                if (before < 0.35f) { Stats.Rejections++; w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name} — 이식받은 장기를 몸이 밀어낸다", c.Id); }
                var ra = c.Ailments.FirstOrDefault(x => x.Id == "rejection") ?? w.Ailments.Catch(c, "rejection", null, dosed ? "몸이 밀어낸다" : "면역억제제를 걸렀다");
                if (ra != null) ra.Peak = MathF.Min(0.9f, b.Reject);
                int stock = w.Ship.CountStored(ItemKind.Immunosuppressant);
                if (a.Present && a.MainOnline && Tell(c.Id * 4 + 2, SimTime.Hours(12)))
                    a.Speak.Announce(a.Voice.Style($"생체 신호 — {c.Name} 거부반응 징후 · " + (stock > 0 ? $"면역억제제 {stock}회분 남음" : "면역억제제가 떨어졌다")), c.Room, 3);
                if (stock == 0) w.Organs.Stats.NoDose++;
            }
            else if (c.Ailments.FirstOrDefault(x => x.Id == "rejection") is Ailment ra && b.Reject < 0.15f) ra.Healed = AilmentSystem.Spec("rejection").Days;
        }
    }

    // ───────────────────────────── 수술 ─────────────────────────────

    public OrganGraft? GraftOf(OpCase op) => op.Graft >= 0 ? Grafts.FirstOrDefault(g => g.Id == op.Graft) : null;

    /// <summary>준비된 수술: 장기가 상하지 않았고 · 눕는 사람이 살아 있다.</summary>
    public bool Ready(OpCase op)
    {
        if (op.Done || P(op.Patient) is not CrewMember pt || pt.Dead || pt.Away) return false;
        if (op.Kind == OpKind.Graft) return GraftOf(op) is OrganGraft g && !g.Gone && g.For == op.Patient;
        if (op.Kind == OpKind.Take) return P(op.For) is CrewMember rc && !rc.Dead;
        return _w.Ship.FurnitureOf(FurnitureType.HeartPump).Any(OrganGear.Works);
    }

    /// <summary>수술대(치료 침대)에 누웠다.</summary>
    public bool InPlace(CrewMember c) =>
        (c.Pose == Pose.Sleeping || c.Down) && (c.CareBed != null || _w.Ship.FurnitureAt(c.Cell)?.Type == FurnitureType.MedBed || c.Room?.Type == RoomType.Medbay && c.Job?.Activity is OrganSupportActivity);

    /// <summary>의무관의 손 — 결과는 Chance 한 번.</summary>
    public void Operate(CrewMember surgeon, OpCase op)
    {
        var w = _w;
        if (op.Done || P(op.Patient) is not CrewMember pt || pt.Dead) return;
        op.Done = true;
        Stats.Ops++;
        var bed = w.Ship.FurnitureAt(pt.Cell) is Furniture fb && fb.Type == FurnitureType.MedBed ? fb : pt.CareBed;
        var g = GraftOf(op);
        float p = Chance(w, surgeon, pt, g, bed, op.Kind);
        LastChance = p;
        var roll = new Rng(unchecked(w.Seed * 9001 + op.Id * 7919 + pt.Id * 131 + (int)(w.Tick % 100000)));
        roll.Float();
        bool ok = roll.Chance(p); // 수술마다 따로 (순서에 묶이지 않게)
        var b = w.Organs.Of(pt);
        surgeon.Practice(Skill.Medicine, 0.06f);
        Cut(pt, op.Kind == OpKind.Take ? 0.12f : 0.1f, op.Kind == OpKind.Take ? (op.Organ == Organ.Kidney ? "신장 기증" : "간 기증") : "이식 수술");
        w.Infection.Sterile(surgeon, pt, pt.Room, "수술 자리");
        switch (op.Kind)
        {
            case OpKind.Take:
            {
                var rc = P(op.For)!;
                string part = op.Organ == Organ.Kidney ? "신장 하나" : "간 일부";
                if (!ok) { pt.Vitals.Health = MathF.Max(0.05f, pt.Vitals.Health - 0.15f); w.Log.Add(w.Tick, LogKind.Warning, $"{pt.Name}에게서 {Ko.EulReul(part)} 떼다 일이 꼬였다 — 다음에 다시", surgeon.Id); op.Done = false; op.Surgeon = -1; Stats.Failed++; return; }
                Stats.Living++;
                if (op.Organ == Organ.Kidney) { b.OneKidney = true; b.Dmg[(int)Organ.Kidney] = MathF.Max(b.Dmg[(int)Organ.Kidney], 0.2f); }
                else { b.LiverPart = true; b.Dmg[(int)Organ.Liver] = MathF.Max(b.Dmg[(int)Organ.Liver], 0.35f); }
                var lg = new OrganGraft { Id = ++_ids, Organ = op.Organ, Donor = pt.Id, DonorName = pt.Name, At = w.Tick, Living = true, Tissue = Tissue(pt), For = rc.Id };
                Grafts.Add(lg);
                Ops.Add(new OpCase { Id = ++_opIds, Kind = OpKind.Graft, Patient = rc.Id, Graft = lg.Id, Organ = op.Organ, Opened = w.Tick });
                w.Log.Add(w.Tick, LogKind.Work, $"{pt.Name}에게서 {Ko.EulReul(part)} 떼어 냈다 — {rc.Name}에게 간다", surgeon.Id);
                MarkLog.Add(pt.Memory.Marks, w.Tick, $"{rc.Name}에게 {Ko.EulReul(part)} 줬다");
                Life.Diary(w, pt, $"{rc.Name}에게 {Ko.EulReul(part)} 줬다. 배가 당긴다.");
                w.Values.Shift(pt, Axis.Mercy, 0.08f, $"{rc.Name}에게 {Ko.EulReul(part)} 줬다");
                return;
            }
            case OpKind.Pump:
            {
                if (!ok) { Fail(surgeon, pt, "인공 심장 수술"); return; }
                Stats.Pumps++;
                b.Pump = true;
                b.PumpCharge = 1f;
                w.Log.Add(w.Tick, LogKind.Work, $"{pt.Name} — 가슴에 인공 심장을 달았다 (열두 시간마다 충전)", surgeon.Id);
                w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(surgeon.Name)} {pt.Name}의 가슴에 인공 심장을 달았다", pt.Room, new[] { surgeon, pt });
                MarkLog.Add(pt.Memory.Marks, w.Tick, "가슴에 펌프를 달았다 — 윙윙거린다");
                Life.Diary(w, pt, "가슴에서 펌프 소리가 난다. 전지를 잊으면 안 된다.");
                Thank(pt, surgeon);
                return;
            }
        }
        // 이식
        if (g == null) return;
        g.Used = true;
        if (!ok) { Fail(surgeon, pt, $"{OrganSystem.Name(g.Organ)} 이식"); if (g.Living && P(g.Donor) is CrewMember ld) { ld.Needs.Stress = MathF.Min(1f, ld.Needs.Stress + 0.3f); Memory.Shake(w, ld, 0.1f, $"내 {OrganSystem.Name(g.Organ)}을(를) 받고도 {pt.Name}이(가) 버티지 못했다"); } return; }
        Stats.Success++;
        int i = (int)g.Organ;
        float match = Match(g, pt);
        b.Dmg[i] = 0.3f;
        b.Cause[i] = "이식 수술";
        b.Graft[i] = g.Printed ? -2 : g.Donor;
        b.Match[i] = match;
        if (g.Organ == Organ.Kidney) b.Uremia *= 0.5f;
        if (g.Organ == Organ.Heart) b.Pump = false;
        if (!g.Printed)
        {
            b.Reject = MathF.Max(b.Reject, 0.1f * (1f - match));
            if (ItemsV15.Use(w, ItemKind.Immunosuppressant)) { b.DoseAt = w.Tick; b.Doses++; w.Organs.Stats.Doses++; }
        }
        string organ = OrganSystem.Name(g.Organ);
        string whose = g.Printed ? $"제 세포로 찍은 {organ}" : $"{g.DonorName}의 {organ}";
        w.Log.Add(w.Tick, LogKind.Work, $"{pt.Name} — {whose}을(를) 받았다 (조직 {match * 6:0}/6)", surgeon.Id);
        w.History.Add(w, HistoryKind.Bond, g.Printed ? $"{Ko.IGa(pt.Name)} 배에서 찍은 {Ko.EuRo(organ)} 다시 산다" : $"{Ko.IGa(pt.Name)} {Ko.EuRo(whose)} 산다", pt.Room, new[] { pt, surgeon });
        MarkLog.Add(pt.Memory.Marks, w.Tick, g.Printed ? $"내 세포로 찍은 {Ko.EuRo(organ)} 산다" : $"{Ko.EuRo(whose)} 산다");
        Life.Diary(w, pt, g.Printed ? $"새 {organ}. 내 것이라는데 아직 낯설다." : $"{whose}. 가끔 그 사람 생각이 난다.");
        w.Values.Shift(pt, Axis.Mercy, 0.06f, $"{Ko.EuRo(whose)} 산다");
        Thank(pt, surgeon);
        if (!g.Printed && P(g.Donor) is CrewMember donor)
        {
            if (g.Living)
            {
                w.Relations.Remember(pt, donor, RelationReason.SavedMe, $"{organ}을(를) 나눠 줬다");
                pt.ChangeAffinity(donor, 0.3f); donor.ChangeAffinity(pt, 0.2f);
                if (!pt.Memory.Comrades.Contains(donor.Id)) pt.Memory.Comrades.Add(donor.Id);
                if (!donor.Memory.Comrades.Contains(pt.Id)) donor.Memory.Comrades.Add(pt.Id);
                MarkLog.Add(donor.Memory.Marks, w.Tick, $"내 {organ}이(가) {pt.Name} 안에서 일한다");
            }
            else
                // 숨진 사람과 가까웠던 사람: 그 사람의 장기가 저 사람 안에 있다
                foreach (var c in w.Crew)
                {
                    if (c.Dead || c == pt) continue;
                    float close = MathF.Max(0f, c.AffinityTo(donor));
                    if (close < 0.3f) continue;
                    c.ChangeAffinity(pt, 0.2f * close);
                    MarkLog.Add(c.Memory.Marks, w.Tick, $"{donor.Name}의 {organ}이(가) {pt.Name} 안에서 뛴다");
                    if (c.GriefUntil > w.Tick) c.GriefUntil = Math.Max(w.Tick, c.GriefUntil - SimTime.Hours(12));
                }
        }
    }

    private void Cut(CrewMember pt, float amount, string cause)
    {
        var v = pt.Vitals;
        v.Injury = MathF.Min(1f, v.Injury + amount);
        var wd = v.Wounds.FirstOrDefault(x => x.Part == BodyPart.Chest && x.Kind == WoundKind.Cut && !x.Lost);
        if (wd == null) v.Wounds.Add(wd = new Wound { Part = BodyPart.Chest, Kind = WoundKind.Cut, Cause = cause });
        wd.Weight += amount;
        v.InjuryCause ??= cause;
        v.TreatedTick = _w.Tick; // 꿰매 두었다 (피가 멎어 있다)
    }

    private void Fail(CrewMember surgeon, CrewMember pt, string what)
    {
        var w = _w;
        Stats.Failed++;
        pt.Vitals.Health = MathF.Max(w.CrewCanDie ? 0f : 0.02f, pt.Vitals.Health - 0.35f);
        w.Log.Add(w.Tick, LogKind.Warning, $"{pt.Name} {what} — 잘 되지 않았다", surgeon.Id);
        MarkLog.Add(surgeon.Memory.Marks, w.Tick, $"{pt.Name} {what} — 내 손이 모자랐다");
        surgeon.Needs.Stress = MathF.Min(1f, surgeon.Needs.Stress + 0.25f);
        Memory.Shake(w, surgeon, 0.06f, $"{pt.Name} 수술");
    }

    private void Thank(CrewMember pt, CrewMember surgeon)
    {
        if (pt == surgeon) return;
        _w.Relations.Remember(pt, surgeon, RelationReason.SavedMe, "수술로 살려 줬다");
        pt.ChangeAffinity(surgeon, 0.12f);
    }

    private bool Tell(int key, long gap)
    {
        if (_told.TryGetValue(key, out var t) && _w.Tick - t < gap) return false;
        _told[key] = _w.Tick;
        return true;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var g in Grafts) { I(g.Id); I((int)g.Organ); I(g.Donor); I(g.For); F(g.Age); I(g.Gone ? 1 : 0); }
        foreach (var d in Donors.Values) { I(d.Crew); I(d.Verdict); I(d.Harvested ? 1 : 0); }
        foreach (var o in Ops) { I(o.Id); I(o.Done ? 1 : 0); I(o.Surgeon); }
        foreach (var j in Prints) F(j.Progress);
        I(Stats.Ops); I(Stats.Success);
    }
}

// ═══════════════════════════════ 사람이 하는 일 ═══════════════════════════════

/// <summary>의무관: 숨진 동료의 몸에서 장기를 떼어 보관함에 넣는다 (정해진 뒤 · 몇 시간 안에).</summary>
public sealed class HarvestActivity : Activity
{
    public override string Id => "harvest";
    public override string Label => "장기 떼기";

    private static CrewMember? Body(CrewMember c, World w)
    {
        foreach (var d in w.Transplant.Donors.Values)
            if (d.Verdict > 0 && !d.Harvested && !d.Closed && (d.Harvester < 0 || d.Harvester == c.Id) && w.Crew.FirstOrDefault(x => x.Id == d.Crew) is CrewMember dead && dead.Room != null && !dead.Outside) return dead;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping || !OrganCareActivity.Medic(c) || Body(c, w) is not CrewMember dead) return (0f, "—");
        if (dead.Room is Room r && Atmosphere.Danger(r) > 0.3f) return (0f, "들어갈 수 없는 방");
        return (1.15f, $"{dead.Name}의 장기 — 시간이 없다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Body(c, w) is not CrewMember dead || RadCareActivity.Near(w, dist, dead) is not Cell at) return null;
        var d = w.Transplant.Donors[dead.Id];
        d.Harvester = c.Id;
        var toils = Plans.DropOff(c, w, dist);
        toils.AddRange(w.Soil.WashFirst(c, true, "장기 떼기"));
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.2f, Skill.Medicine, dead.Position) { CanContinue = (cm, _) => (dead.Position - cm.Position).Length() < 2.4f });
        toils.Add(new DoToil((cm, world) => { world.Transplant.Harvest(cm, dead); return true; }));
        return new Job(this, $"{dead.Name}의 장기를 뗀다", toils)
        {
            LogText = $"{dead.Name}의 몸 곁으로 — 장기를 떼러 간다",
            TargetRoom = dead.Room,
            Urgent = true,
            InterruptMargin = 0.3f,
            OnFinished = (cm, world, _) => { if (!d.Harvested && d.Harvester == cm.Id) d.Harvester = -1; },
        };
    }
}

/// <summary>수술을 기다리는 사람 (받는 사람 · 주는 사람): 의무실 치료 침대에 눕는다.</summary>
public sealed class SurgeryWaitActivity : Activity
{
    public override string Id => "surgerywait";
    public override string Label => "수술 대기";

    internal static OpCase? Mine(CrewMember c, World w) => w.Transplant.Ops.FirstOrDefault(o => !o.Done && o.Patient == c.Id && w.Transplant.Ready(o));

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || Mine(c, w) is not OpCase op) return (0f, "—");
        if (w.Transplant.InPlace(c) && c.Job?.Activity is not SurgeryWaitActivity) return (0f, "이미 누웠다");
        return (op.Kind == OpKind.Graft ? 1.1f : 0.98f, op.Kind == OpKind.Take ? "떼어 줄 수술 — 치료 침대로" : "수술 — 치료 침대로");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Mine(c, w) is not OpCase op) return null;
        var bed = w.Ship.FurnitureOf(FurnitureType.MedBed).Where(b => b.ReservedBy == null && b.UseSpots.Count > 0 && dist.Reachable(b.UseSpots[0]))
            .OrderBy(b => dist.Get(b.UseSpots[0])).FirstOrDefault();
        if (bed == null) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(bed.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Hours(8), Pose.Sleeping, minTicks: SimTime.Hours(1))
        {
            EveryTick = (cm, world) => { if (bed.Machine!.Efficiency > 0f) cm.Vitals.Health = MathF.Min(cm.Vitals.MaxHealth, cm.Vitals.Health + 0.08f / SimTime.TicksPerHour); },
            DoneWhen = (cm, world) => op.Done && world.Tick - world.Transplant.Ops.Where(o => o == op).Select(o => o.Claimed).FirstOrDefault() > SimTime.Hours(3) || !world.Transplant.Ready(op) && !op.Done,
        });
        return new Job(this, "수술 대기", toils) { LogText = op.Kind == OpKind.Take ? "떼어 줄 수술을 받으러 눕는다" : "수술을 받으러 치료 침대에 눕는다", TargetRoom = bed.Room, InterruptMargin = 0.5f }.Reserve(bed, c);
    }
}

/// <summary>의무관: 이식 · 인공 심장 · 산 사람에게서 떼기 — 치료 침대에 누운 사람을 수술한다.</summary>
public sealed class TransplantOpActivity : Activity
{
    public override string Id => "transplantop";
    public override string Label => "수술";

    private static (OpCase? op, CrewMember? pt) Pick(CrewMember c, World w)
    {
        var t = w.Transplant;
        foreach (var op in t.Ops.OrderBy(o => o.Kind == OpKind.Take ? 1 : 0).ThenBy(o => o.Id))
        {
            if (op.Done || op.Patient == c.Id || op.Surgeon >= 0 && op.Surgeon != c.Id && w.Tick - op.Claimed < SimTime.Hours(4) || !t.Ready(op)) continue;
            if (w.Crew.FirstOrDefault(x => x.Id == op.Patient) is not CrewMember pt || !t.InPlace(pt)) continue;
            return (op, pt);
        }
        return (null, null);
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping || !OrganCareActivity.Medic(c)) return (0f, "—");
        var (op, pt) = Pick(c, w);
        if (op == null || pt == null) return (0f, "—");
        return (op.Kind == OpKind.Graft ? 1.2f : 1.1f, $"{pt.Name} 수술 — " + (op.Kind == OpKind.Pump ? "인공 심장" : op.Kind == OpKind.Take ? "떼어 낸다" : OrganSystem.Name(op.Organ) + " 이식"));
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (op, pt) = Pick(c, w);
        if (op == null || pt == null || RadCareActivity.Near(w, dist, pt) is not Cell at) return null;
        op.Surgeon = c.Id;
        op.Claimed = w.Tick;
        var toils = Plans.DropOff(c, w, dist);
        toils.AddRange(w.Soil.WashFirst(c, false, "수술"));
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(op.Kind == OpKind.Take ? 1.5f : 2.5f, Skill.Medicine, pt.Position) { CanContinue = (cm, world) => !pt.Dead && (pt.Position - cm.Position).Length() < 2.4f && world.Transplant.Ready(op) });
        toils.Add(new DoToil((cm, world) => { world.Transplant.Operate(cm, op); return true; }));
        string what = op.Kind == OpKind.Pump ? "인공 심장" : op.Kind == OpKind.Take ? (op.Organ == Organ.Kidney ? "신장 떼기" : "간 떼기") : $"{OrganSystem.Name(op.Organ)} 이식";
        return new Job(this, $"{pt.Name} {what}", toils)
        {
            LogText = $"{pt.Name} {what} 수술을 시작한다",
            TargetRoom = pt.Room,
            InterruptMargin = 0.45f,
            OnFinished = (cm, world, _) => { if (!op.Done && op.Surgeon == cm.Id) op.Surgeon = -1; },
        };
    }
}

/// <summary>실험실: 장기를 기다리는 사람의 세포를 바이오 프린터에 건다.</summary>
public sealed class PrintActivity : Activity
{
    public override string Id => "bioprint";
    public override string Label => "장기 찍기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping || c.SkillLevel(Skill.Medicine) + c.SkillLevel(Skill.Botany) < 0.35f) return (0f, "—");
        var (f, to, o) = w.Transplant.PrintWanted();
        if (f == null || to == null || f.UseSpots.Count == 0 || !dist.Reachable(f.UseSpots[0])) return (0f, "—");
        return (0.78f, $"{to.Name}의 {OrganSystem.Name(o)} — 프린터에 세포를 건다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (f, to, o) = w.Transplant.PrintWanted();
        if (f == null || to == null) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.AddRange(w.Soil.WashFirst(c, false, "세포 다루기"));
        toils.Add(new GotoToil(f.UseSpots[0]));
        toils.Add(new WorkToil(0.5f, Skill.Medicine, f.Center));
        toils.Add(new DoToil((cm, world) => { if (world.Transplant.PrintWanted().f == f) world.Transplant.StartPrint(cm, f, to, o); return true; }));
        return new Job(this, "바이오 프린터", toils) { LogText = $"바이오 프린터에 {to.Name}의 세포를 걸러 간다", TargetRoom = f.Room }.Reserve(f, c);
    }
}
