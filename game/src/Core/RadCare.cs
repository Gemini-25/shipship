using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 통합5 방사선 병 간호 (점검 항해: 태양 폭풍에 크게 쬔 사람이 며칠 뒤 혼자 쓰러져 숨졌다 — 아무도 손쓰지 않은 채).
//   수액과 구토 억제제 — 물과 염분이 빠지는 걸 막는다 (구급 키트 · 의무실 수액이면 키트 없이도)
//   골수 주사 — 피를 다시 만들게 재촉한다 (구급 키트 · 7Sv 아래에서 잘 듣고, 12Sv 넘으면 거의 안 듣는다)
//   수혈 — 피폭이 적고 건강한 사람이 곁에서 피를 나눠 준다 (가까운 사이부터 나선다 · 나눠 준 사람은 한동안 기운이 없다)
//   격리 — 골수가 무너진 몸은 작은 감기도 폐렴이 된다 (북적이는 방 · 문병 · 격리 설비가 없는 의무실이면 잘 옮는다)
//   → 4~7Sv는 손쓰면 대개 산다 · 7~12Sv는 수혈까지 제때 해야 산다 · 12Sv 넘으면 손을 써도 몇 시간 늦출 뿐이다.
// 사람: 의무관(없으면 의료를 아는 사람 · 그것도 없으면 누구든)이 차례로 돌보고, 숨이 넘어가는 사람 곁을 지킨다.
// 주 컴퓨터: 피폭 기록을 읽고 무엇이 필요한지(수혈 · 격리) · 누가 피를 줄 수 있는지 알린다 · 손쓸 길이 없으면 그렇게 말한다.
public sealed class RadPatient
{
    public int Crew { get; init; }
    public long Since { get; init; }
    public long FluidsAt { get; set; } = -1;
    public long StimAt { get; set; } = -1;
    public long BloodAt { get; set; } = -1;
    public int Fluids { get; set; }
    public int Stims { get; set; }
    public int Bloods { get; set; }
    public int Carer { get; set; } = -1;
    public long CarerAt { get; set; } = -1;
    public int Donor { get; set; } = -1;
    public long DonorAsked { get; set; } = -1;
    public int LastDonor { get; set; } = -1;
    public bool Stable { get; set; }
    public bool Infected { get; set; }
    public bool Told { get; set; }
    public bool ToldHopeless { get; set; }
    public bool ToldCrowd { get; set; }
    public bool Treated => Fluids + Stims + Bloods > 0;
}

public sealed class RadCareSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 2741));
    private readonly SortedDictionary<int, RadPatient> _pt = new();
    private long _next;

    public int FluidsGiven, StimsGiven, Transfusions, Infections, Saved, LostAnyway, LostUntreated, DonorCalls, Vigils;

    public static bool Off; // 시험: 간호 없이 (예전과 견줌)

    public RadCareSystem(World w) => _w = w;

    public IEnumerable<RadPatient> Patients => _pt.Values;
    public RadPatient? Of(CrewMember c) => _pt.TryGetValue(c.Id, out var p) ? p : null;
    public bool Treated(CrewMember c) => Of(c)?.Treated == true;

    private CrewMember? CrewOf(int id) => id >= 0 && id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(x => x.Id == id);

    /// <summary>Perils.Radiation: 손쓴 만큼 몸이 덜 무너진다 (12Sv 넘으면 거의 그대로).</summary>
    public float DrainMul(CrewMember c)
    {
        if (Of(c) is not RadPatient p) return 1f;
        long now = _w.Tick;
        int stage = _w.Perils.RadStage(c);
        float m = 1f;
        if (p.FluidsAt >= 0 && now - p.FluidsAt < SimTime.Hours(10)) m *= 0.7f;
        if (p.StimAt >= 0 && now - p.StimAt < SimTime.Hours(24)) m *= stage >= 3 ? 0.95f : 0.7f;
        if (p.BloodAt >= 0 && now - p.BloodAt < SimTime.Hours(12)) m *= 0.5f;
        if (p.Stable) m *= 0.2f; // 골수가 다시 피를 만든다
        return stage >= 3 ? MathF.Max(0.82f, m) : m;
    }

    /// <summary>수혈이 필요한가 (7Sv 넘고 기운이 빠졌다 · 열두 시간에 한 번).</summary>
    public bool NeedsBlood(RadPatient p, CrewMember c) =>
        !p.Stable && _w.Perils.RadStage(c) >= 2 && c.Vitals.Health < 0.6f && (p.BloodAt < 0 || _w.Tick - p.BloodAt > SimTime.Hours(12));

    /// <summary>돌볼 차례인가 (수액 열 시간 · 골수 주사 하루 · 수혈 · 숨이 넘어간다).</summary>
    public string? Due(RadPatient p, CrewMember c)
    {
        long now = _w.Tick;
        int stage = _w.Perils.RadStage(c);
        if (c.Vitals.Health < 0.3f) return "숨이 넘어간다";
        if (p.Donor >= 0 && CrewOf(p.Donor) is CrewMember d && d.CanAct && (d.Position - c.Position).LengthSquared() < 9f) return "피를 나눠 줄 사람이 곁에 왔다";
        if (p.Stable) return p.FluidsAt < 0 || now - p.FluidsAt > SimTime.Hours(24) ? "수액" : null;
        if (p.FluidsAt < 0 || now - p.FluidsAt > SimTime.Hours(10)) return "수액";
        if (stage <= 2 && (p.StimAt < 0 || now - p.StimAt > SimTime.Hours(24)) && KitOnBoard()) return "골수 주사";
        return null;
    }

    private bool KitOnBoard() => _w.Ship.Furniture.Any(f => f.Storage != null && f.Storage.Count(ItemKind.MedKit) > 0);

    /// <summary>이 사람이 돌볼 수 있나 (의무관 · 의료를 아는 사람 · 아무도 없으면 누구든).</summary>
    public bool CanNurse(CrewMember c) =>
        c.Role == CrewRole.Medic || c.Quals.Contains(Qual.Medic) || c.SkillLevel(Skill.Medicine) >= 0.3f
        || !_w.Crew.Any(o => !o.Dead && !o.Away && o.CanAct && (o.Role == CrewRole.Medic || o.Quals.Contains(Qual.Medic) || o.SkillLevel(Skill.Medicine) >= 0.3f) && _w.Perils.RadStage(o) < 2);

    /// <summary>격리됐나: 격리 설비가 있는 의무실 · 아니면 그 방에 돌보는 사람 말고는 아무도 없다.</summary>
    public bool Isolated(CrewMember c, RadPatient p)
    {
        if (c.Room is not Room r) return true;
        if (r.Type == RoomType.Medbay && Facilities.Factor(r, "quarantine") >= 1f) return true;
        return Others(c, p) == 0;
    }

    private int Others(CrewMember c, RadPatient p)
    {
        int n = 0;
        foreach (var o in _w.Crew)
            if (o != c && !o.Dead && !o.Away && o.Room == c.Room && o.Id != p.Carer && o.Id != p.Donor && o.Suit == null) n++;
        return n;
    }

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(1); // 통합6 북적이는 방은 금방 흩어진다 — 컴퓨터는 매분 본다
        float h = 1f / 60f;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || _pt.ContainsKey(c.Id) || w.Perils.RadStage(c) < 1) continue;
            _pt[c.Id] = new RadPatient { Crew = c.Id, Since = w.Tick };
        }
        foreach (var p in _pt.Values)
        {
            if (CrewOf(p.Crew) is not CrewMember c || c.Dead || c.Away) continue;
            int stage = w.Perils.RadStage(c);
            // 돌보던 사람 · 피를 줄 사람이 손을 놓았으면 비운다
            if (p.Carer >= 0 && (CrewOf(p.Carer) is not CrewMember cr || !cr.CanAct || cr.Job?.Activity is not RadCareActivity || w.Tick - p.CarerAt > SimTime.Hours(2))) p.Carer = -1;
            if (p.Donor >= 0 && (CrewOf(p.Donor) is not CrewMember dn || !dn.CanAct || w.Tick - p.DonorAsked > SimTime.Hours(3))) { p.LastDonor = p.Donor; p.Donor = -1; }
            Advise(c, p, stage);
            // 수혈이 필요하면 피를 나눠 줄 사람을 찾는다 (가까운 사이부터)
            if (p.Donor < 0 && NeedsBlood(p, c) && w.Tick - p.DonorAsked > SimTime.Hours(1)) AskDonor(c, p);
            // 골수가 무너진 몸: 북적이는 방 · 문병 · 격리 없는 의무실에서는 감염이 잘 옮는다
            if (!p.Infected && !p.Stable && !Isolated(c, p))
            {
                int others = Math.Min(4, Others(c, p));
                float rate = 0.003f * stage * others * (w.History.Doctrine.Quarantine ? 0.5f : 1f);
                if (R.Chance(rate * h))
                {
                    p.Infected = true;
                    Infections++;
                    w.Ailments.Catch(c, "pneumonia", null, "피폭으로 몸이 약해져 옮았다");
                    w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 열이 오른다 — 피를 못 만드는 몸에 폐렴이 옮았다 ({c.Room?.Name} · 곁에 {others}명)", c.Id);
                    MarkLog.Add(c.Memory.Marks, w.Tick, "피폭 뒤 폐렴 — 사람이 많은 방에서 옮았다");
                }
            }
            // 고비를 넘겼다: 손을 썼고 하루 반을 버티며 기운이 돌아왔다 (12Sv 넘으면 못 넘긴다)
            if (!p.Stable && stage <= 2 && p.Treated && w.Tick - p.Since > SimTime.Hours(36) && c.Vitals.Health > 0.6f && (stage < 2 || p.Bloods > 0 || p.Stims > 0))
            {
                p.Stable = true;
                Saved++;
                string what = Treatments(p);
                w.Log.Add(w.Tick, LogKind.Life, $"방사선 병 — 고비를 넘겼다 ({what})", c.Id);
                MarkLog.Add(c.Memory.Marks, w.Tick, $"방사선 병 고비를 넘겼다 — {what}");
                Life.Diary(w, c, Persona.Say(c, "토하는 게 멎었다. 피가 다시 도는 느낌이다."));
                w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} 방사선 병 고비를 넘겼다 — {what}", c.Room, new[] { c });
                if (p.LastDonor >= 0 && CrewOf(p.LastDonor) is CrewMember gave && !gave.Dead) MarkLog.Add(gave.Memory.Marks, w.Tick, $"{c.Name} — 내 피를 받고 고비를 넘겼다");
            }
        }
    }

    private static string Treatments(RadPatient p)
    {
        var parts = new List<string>();
        if (p.Fluids > 0) parts.Add($"수액 {p.Fluids}번");
        if (p.Stims > 0) parts.Add($"골수 주사 {p.Stims}번");
        if (p.Bloods > 0) parts.Add($"수혈 {p.Bloods}번");
        return parts.Count == 0 ? "손쓸 틈이 없었다" : string.Join(" · ", parts);
    }

    /// <summary>주 컴퓨터: 피폭 기록 → 필요한 처치 · 피를 줄 수 있는 사람 · 북적이는 방 · 손쓸 길이 없음.</summary>
    private void Advise(CrewMember c, RadPatient p, int stage)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) return;
        var room = c.Room;
        if (!p.Told)
        {
            p.Told = true;
            string need = stage >= 3 ? "손쓸 길이 거의 없습니다 — 곁을 지켜 주십시오" : stage == 2 ? "의무실 침대 · 수액 · 골수 주사 · 열두 시간 안에 수혈이 필요합니다" : "의무실 침대 · 수액 · 골수 주사가 필요합니다";
            var donor = stage >= 2 ? BestDonor(c, p) : null;
            a.Book.Add(ActKind.Advice, room, $"{c.Name} 누적 피폭 {c.Dose:0.0}Sv", stage >= 3 ? "골수 · 장이 무너지는 양 — 처치로 몇 시간 늦출 뿐" : "피를 만드는 골수가 상했다 — 며칠 뒤가 고비",
                need + (donor != null ? $" · 피를 줄 수 있는 사람: {donor.Name}" : ""), "", $"radcare:{c.Id}", SimTime.Hours(12), 60f * 24f,
                (world, act) => world.Crew.FirstOrDefault(x => x.Id == c.Id) is { Dead: false } ? (1, "살았다") : (-1, "숨졌다"));
            if (room != null && room.DataLinked && room.Powered)
                a.Speak.Announce(a.Voice.Style($"{c.Name} 피폭 {c.Dose:0.0}Sv — {need}"), room, 1);
            if (stage >= 3) p.ToldHopeless = true;
        }
        else if (stage >= 3 && !p.ToldHopeless)
        {
            p.ToldHopeless = true;
            a.Book.Add(ActKind.Advice, room, $"{c.Name} 누적 피폭 {c.Dose:0.0}Sv", "처치로 몇 시간 늦출 뿐", "곁을 지켜 주십시오", "", $"radcare3:{c.Id}", SimTime.Hours(12));
        }
        if (!p.ToldCrowd && !p.Stable && stage >= 2 && room != null && !Isolated(c, p) && Others(c, p) >= 2)
        {
            p.ToldCrowd = true;
            a.Book.Add(ActKind.Advice, room, $"{room.Name}에 {c.Name} 곁으로 {Others(c, p)}명", "골수가 무너진 몸은 작은 감기도 폐렴이 된다",
                "문병을 줄이고 마스크를 쓰십시오 · 격리할 자리로 옮기십시오", "", $"radcrowd:{c.Id}", SimTime.Hours(12));
        }
    }

    /// <summary>피를 줄 사람: 피폭이 적고(1Sv 밑) · 건강하고 · 어른 · 가까운 사이부터 (그다음 가까운 사람).</summary>
    private CrewMember? BestDonor(CrewMember c, RadPatient p) =>
        _w.Crew.Where(o => o != c && !o.Dead && !o.Away && o.CanAct && !o.IsChild && !o.Outside && o.Dose < 1f && o.Pose != Pose.Sleeping && o.Vitals.Health > 0.8f && o.Id != p.LastDonor
                           && (Of(o) is null) && !_pt.Values.Any(q => q.Donor == o.Id))
            .OrderByDescending(o => MathF.Round(o.AffinityTo(c) * 4f) + (c.AffinityTo(o) > 0.5f ? 1f : 0f))
            .ThenBy(o => (o.Position - c.Position).LengthSquared()).ThenBy(o => o.Id).FirstOrDefault();

    private void AskDonor(CrewMember c, RadPatient p)
    {
        var w = _w;
        var d = BestDonor(c, p);
        p.DonorAsked = w.Tick;
        if (d == null)
        {
            w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name}에게 피를 나눠 줄 사람이 없다 (모두 쬐었거나 다쳤다)", c.Id);
            return;
        }
        p.Donor = d.Id;
        DonorCalls++;
        d.NextThinkTick = Math.Min(d.NextThinkTick, w.Tick + 1);
        string who = d.AffinityTo(c) > 0.4f ? "가까운 사이라 먼저 나섰다" : "피폭이 적고 건강해서";
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(d.Name)} {c.Name}에게 피를 나눠 주기로 했다 — {who}", d.Id);
        if (w.Automation.Present && w.Automation.MainOnline)
            w.Automation.Book.Add(ActKind.Advice, c.Room, $"{c.Name} 수혈이 필요하다 (체력 {c.Vitals.Health * 100:0}%)", "피폭이 적고 건강한 사람", $"{Ko.EulReul(d.Name)} 불렀다", "", $"raddonor:{c.Id}", SimTime.Hours(6));
    }

    /// <summary>RadCareActivity가 곁에서 처치한다 (수액 · 골수 주사 · 곁에 온 사람의 피).</summary>
    internal string Apply(CrewMember nurse, CrewMember c, bool kit)
    {
        var w = _w;
        if (Of(c) is not RadPatient p) return "";
        int stage = w.Perils.RadStage(c);
        var did = new List<string>();
        long now = w.Tick;
        bool medbay = c.Room?.Type == RoomType.Medbay;
        if ((kit || medbay) && (p.FluidsAt < 0 || now - p.FluidsAt > SimTime.Hours(p.Stable ? 20 : 8)))
        {
            p.FluidsAt = now; p.Fluids++; FluidsGiven++;
            c.Needs.Food = MathF.Min(1f, c.Needs.Food + 0.1f);
            did.Add("수액 · 구토 억제제");
        }
        if (kit && !p.Stable && stage <= 3 && (p.StimAt < 0 || now - p.StimAt > SimTime.Hours(22)))
        {
            p.StimAt = now; p.Stims++; StimsGiven++;
            did.Add("골수 주사");
        }
        if (p.Donor >= 0 && CrewOf(p.Donor) is CrewMember d && d.CanAct && (d.Position - c.Position).LengthSquared() < 9f)
        {
            p.BloodAt = now; p.Bloods++; Transfusions++;
            p.LastDonor = d.Id; p.Donor = -1;
            c.Vitals.Health = MathF.Min(c.Vitals.MaxHealth, c.Vitals.Health + 0.12f);
            d.Vitals.Health = MathF.Max(0.3f, d.Vitals.Health - 0.1f);
            d.Needs.Rest = MathF.Max(0f, d.Needs.Rest - 0.15f);
            d.Needs.Food = MathF.Max(0f, d.Needs.Food - 0.1f);
            d.ExcusedUntil = Math.Max(d.ExcusedUntil, now + SimTime.Hours(4)); // 피를 준 사람은 한동안 쉰다
            w.Relations.Remember(c, d, RelationReason.SavedMe, "방사선 병으로 누웠을 때 피를 나눠 줬다");
            c.ChangeAffinity(d, 0.12f);
            d.ChangeAffinity(c, 0.05f);
            MarkLog.Add(d.Memory.Marks, now, $"{c.Name}에게 피를 나눠 줬다");
            Life.Diary(w, d, Persona.Say(d, $"{c.Name}에게 피를 줬다. 어지럽지만 괜찮다."));
            w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(d.Name)} 방사선 병을 앓는 {c.Name}에게 피를 나눠 줬다 ({Ko.IGa(nurse.Name)} 수혈)", c.Room, new[] { c, d, nurse });
            did.Add($"{d.Name}의 피 수혈");
        }
        if (did.Count == 0) return "";
        p.Carer = -1;
        w.Ailments.Treated(c, nurse);
        if (c != nurse) w.Relations.Remember(c, nurse, RelationReason.NursedMe, "방사선 병으로 누웠을 때 돌봐 줬다");
        c.ChangeAffinity(nurse, 0.05f);
        nurse.Practice(Skill.Medicine, 0.03f);
        string text = string.Join(" · ", did);
        w.Log.Add(w.Tick, LogKind.Work, $"{c.Name} 방사선 병 처치 — {text} (체력 {c.Vitals.Health * 100:0}%)", nurse.Id);
        return text;
    }

    /// <summary>숨이 넘어가는 사람 곁 (RadCareActivity의 마지막 단계): 산소 · 체온 · 손을 잡는다.</summary>
    internal void Vigil(CrewMember nurse, CrewMember c, float hours)
    {
        c.Vitals.Health = MathF.Min(c.Vitals.MaxHealth, c.Vitals.Health + 0.03f * hours);
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.1f * hours);
    }

    internal void Claim(CrewMember c, CrewMember nurse) { if (Of(c) is RadPatient p) { p.Carer = nurse.Id; p.CarerAt = _w.Tick; } }

    /// <summary>World.Die 뒤 (Perils.OnDeath): 손을 써 봤는지 · 무엇을 했는지.</summary>
    public string? DeathNote(CrewMember c)
    {
        if (Of(c) is not RadPatient p) return null;
        if (p.Treated) { LostAnyway++; return $"{Treatments(p)}을 받았지만 골수가 버티지 못했다"; }
        LostUntreated++;
        return null;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(FluidsGiven); I(StimsGiven); I(Transfusions); I(Infections); I(Saved); I(DonorCalls);
        foreach (var p in _pt.Values) { I(p.Crew); I(p.FluidsAt); I(p.StimAt); I(p.BloodAt); I(p.Donor); I(p.Stable ? 1 : 0); }
    }
}

/// <summary>통합5 방사선 병 간호: 의무관이 구급 키트를 들고 가 수액 · 골수 주사 · 수혈을 하고, 숨이 넘어가는 사람 곁을 지킨다.</summary>
public sealed class RadCareActivity : Activity
{
    public override string Id => "radcare";
    public override string Label => "방사선 병 간호";

    private static (CrewMember? pt, string why) Pick(CrewMember c, World w, DistanceField dist)
    {
        var rc = w.RadCare;
        CrewMember? best = null;
        string why = "";
        float bs = float.MinValue;
        foreach (var p in rc.Patients)
        {
            var pc = w.Crew.FirstOrDefault(x => x.Id == p.Crew);
            if (pc == null || pc == c || pc.Dead || pc.Away || pc.Outside || pc.Room is not Room room) continue;
            if (p.Carer >= 0 && p.Carer != c.Id) continue;
            if (rc.Due(p, pc) is not string due) continue;
            if (w.Ambience.StormPower >= 0.3f && room.Radiation >= 0.2f) continue; // 폭풍이 그치기 전엔 바깥 방에 못 간다
            if (Atmosphere.Danger(room) > 0.5f) continue;
            if (Near(w, dist, pc) is not Cell at) continue;
            float s = (due == "숨이 넘어간다" ? 3f : due.StartsWith("피를") ? 2f : 0f) + w.Perils.RadStage(pc) - dist.Get(at) / 400f;
            if (s > bs) { bs = s; best = pc; why = due; }
        }
        return (best, why);
    }

    internal static Cell? Near(World w, DistanceField dist, CrewMember pt)
    {
        Cell? best = null;
        int bd = int.MaxValue;
        foreach (var d in Cell.Dirs8)
        {
            var x = pt.Cell + d;
            if (!w.Ship.IsWalkable(x) || !dist.Reachable(x)) continue;
            int g = dist.Get(x);
            if (g < bd) { bd = g; best = x; }
        }
        return best ?? (w.Ship.IsWalkable(pt.Cell) && dist.Reachable(pt.Cell) ? pt.Cell : null);
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        var rc = w.RadCare;
        if (!rc.Patients.Any() || !c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping || w.Perils.RadStage(c) >= 2 || !rc.CanNurse(c)) return (0f, "—");
        var (pt, why) = Pick(c, w, dist);
        if (pt == null) return (0f, "돌볼 차례가 아니다");
        float s = why == "숨이 넘어간다" ? 1.05f : why.StartsWith("피를") ? 0.92f : 0.6f + 0.1f * w.Perils.RadStage(pt);
        if (c.Role == CrewRole.Medic) s += 0.08f;
        return (s, $"{pt.Name} 방사선 병 — {why}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var rc = w.RadCare;
        var (pt, why) = Pick(c, w, dist);
        if (pt == null || Near(w, dist, pt) is not Cell at) return null;
        bool critical = why == "숨이 넘어간다";
        var toils = Plans.DropOff(c, w, dist);
        bool kit = c.Carrying?.Kind == ItemKind.MedKit;
        if (!kit && !critical)
        {
            var (box, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Count(ItemKind.MedKit) > 0);
            if (box != null) { toils.Add(new GotoToil(spot)); toils.Add(new TakeToil(box, ItemKind.MedKit, 1)); }
        }
        toils.Add(new GotoToil(at));
        // 환자가 그새 자리를 옮겼으면 (업혀 의무실로 · 침대로) 따라간다
        Cell? Chase(CrewMember cm) => (pt.Position - cm.Position).Length() < 2f || pt.Dead ? null
            : Cell.Dirs8.Select(d => pt.Cell + d).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - cm.Position).LengthSquared()).ThenBy(x => x.X).ThenBy(x => x.Y).Cast<Cell?>().FirstOrDefault();
        toils.Add(new GotoToilLate(Chase));
        toils.Add(new WorkToil(critical ? 0.1f : 0.25f, Skill.Medicine, pt.Position)
        {
            CanContinue = (cm, _) => !pt.Dead && (pt.Position - cm.Position).Length() < 2.4f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            bool held = cm.Carrying?.Kind == ItemKind.MedKit;
            string did = world.RadCare.Apply(cm, pt, held);
            if (held && did.Length > 0 && (did.Contains("골수") || pt.Room?.Type != RoomType.Medbay))
                cm.Carrying = cm.Carrying!.Value.Count > 1 ? new ItemStack(ItemKind.MedKit, cm.Carrying.Value.Count - 1) : null;
            if (did.Length > 0) cm.Say(world, Persona.Say(cm, critical ? "숨 쉬어 — 내가 여기 있어" : "조금 따끔해. 이거 맞으면 속이 덜 뒤집힐 거야"));
            return true;
        }));
        if (critical || w.Perils.RadStage(pt) >= 3)
            toils.Add(new WaitToil(SimTime.Minutes(40), Pose.Working, pt.Position)
            {
                EveryTick = (cm, world) => { if ((pt.Position - cm.Position).Length() < 2.6f) world.RadCare.Vigil(cm, pt, 1f / SimTime.TicksPerHour); },
                DoneWhen = (cm, _) => pt.Dead || pt.Vitals.Health > 0.4f,
            });
        rc.Claim(pt, c);
        if (critical) rc.Vigils++;
        return new Job(this, critical ? $"{pt.Name} 곁을 지킨다" : $"{pt.Name} 방사선 병 처치", toils)
        {
            LogText = critical ? $"{Ko.EulReul(pt.Name)} 살리러 간다 — 숨이 넘어간다" : $"{pt.Name}에게 {why}",
            TargetRoom = pt.Room,
            Urgent = critical,
            InterruptMargin = 0.25f,
        };
    }
}

/// <summary>통합5 피를 나눠 준다: 컴퓨터 · 의무관이 부른 사람이 환자 곁에 가 앉아 기다린다 (의무관이 와서 수혈한다).</summary>
public sealed class GiveBloodActivity : Activity
{
    public override string Id => "giveblood";
    public override string Label => "피 나눠 주기";

    private static (RadPatient? p, CrewMember? pt) Mine(CrewMember c, World w)
    {
        foreach (var p in w.RadCare.Patients)
            if (p.Donor == c.Id && w.Crew.FirstOrDefault(x => x.Id == p.Crew) is CrewMember pt && !pt.Dead && !pt.Away && !pt.Outside) return (p, pt);
        return (null, null);
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.Pose == Pose.Sleeping || !w.RadCare.Patients.Any()) return (0f, "—");
        var (p, pt) = Mine(c, w);
        if (p == null || pt == null) return (0f, "—");
        if (w.Ambience.StormPower >= 0.3f && pt.Room?.Radiation >= 0.2f) return (0f, "폭풍이 그치면");
        return (0.86f, $"{pt.Name}에게 피를 나눠 주러 간다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (p, pt) = Mine(c, w);
        if (p == null || pt == null || RadCareActivity.Near(w, dist, pt) is not Cell at) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Minutes(70), Pose.Sitting, pt.Position)
        {
            DoneWhen = (cm, _) => p.Donor != cm.Id || pt.Dead,
        });
        return new Job(this, $"{pt.Name}에게 피를 나눠 준다", toils) { LogText = $"{pt.Name} 곁에 소매를 걷고 앉았다", TargetRoom = pt.Room };
    }
}
