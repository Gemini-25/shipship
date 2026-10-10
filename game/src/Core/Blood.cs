using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 의료 1차 — 피 (혈액형 · 혈액팩 · 헌혈 · 수혈).
//   혈액형: 사람마다 정해져 있다 (출항 때 건강 기록에 적힌 그대로 — 시드와 사람으로 정한다 · 결정론)
//   혈액팩: 혈액 냉장고에 차갑게 둔다 (서른닷새) — 전기가 끊겨 미지근해지면 몇 시간 만에 상한다
//   헌혈: 냉장고에 어떤 피가 모자라면 주컴퓨터가 맞는 사람을 찾아 부탁한다 · 다친 사람에게 급히 피가 필요하면 가까운 사이부터 나선다
//   수혈: 위중하게 피를 흘린 사람에게 맞는 피를 넣는다 — 맞는 피가 없으면 혈액 대용제 · 그것도 없으면 맞지 않는 피를 무릅쓴다 (거부 반응)
// 사람: 맞는 피를 가진 사람이 나선다 (관계 · 기억이 남는다 — 그 사람의 피로 살았다) · 피를 준 사람은 한나절 기운이 없다.
// 주컴퓨터: 기록으로 혈액형을 안다 — 누구 피가 맞는지 · 냉장고 재고 · 기한이 다가오는 팩 · 모자라는 형을 알리고 헌혈을 부탁한다.
public enum BloodGroup : byte { O, A, B, AB }

public readonly record struct BloodType(BloodGroup Group, bool Neg)
{
    public string Name => (Neg ? "Rh- " : "") + Group switch { BloodGroup.O => "O형", BloodGroup.A => "A형", BloodGroup.B => "B형", _ => "AB형" };
    public int Code => (int)Group * 2 + (Neg ? 1 : 0);
}

public sealed class BloodPack
{
    public int Id { get; init; }
    public BloodType Type { get; init; }
    public long Drawn { get; init; }
    public long Expires { get; set; }
    public int Donor { get; init; } = -1;     // 준 사람 (-1: 출항 때 실은 팩)
    public int Fridge { get; set; } = -1;     // 넣어 둔 냉장고 (가구 번호 · -1: 밖)
    public float Warm { get; set; }           // 차갑지 않게 지낸 시간 (시간)
    public bool Spoiled => Warm >= BloodSystem.WarmLimit;
}

public sealed class BloodSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 4111));
    private int _next = 1;
    private long _lastCheck = -1, _lastAsk = -1, _lastStockTalk = -1;
    public const float ShelfDays = 35f;
    public const float WarmLimit = 5f;       // 다섯 시간 넘게 미지근하면 못 쓴다
    public List<BloodPack> Packs { get; } = new();
    /// <summary>헌혈하기로 한 사람 → (받을 사람 · -1이면 냉장고에) · 부탁한 때.</summary>
    public SortedDictionary<int, (int forId, long asked)> Donors { get; } = new();
    public SortedDictionary<int, long> LastGave { get; } = new();   // 마지막으로 피를 준 때 (여드레는 쉬어야)
    public int Donations, Transfusions, Fresh, Substitutes, Mismatched, Reactions, Spoiled, Expired, Calls;

    public BloodSystem(World w) => _w = w;

    // ───────────── 혈액형 ─────────────

    /// <summary>이 사람의 혈액형 (시드 · 사람 번호 · 이름으로 정한다 — 같은 배면 늘 같다).</summary>
    public BloodType TypeOf(CrewMember c)
    {
        uint h = unchecked((uint)_w.Seed * 2654435761u ^ (uint)(c.Id + 1) * 40503u);
        foreach (char ch in c.Name) h = unchecked(h * 31u + ch);
        h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15;
        int g = (int)(h % 100u);
        var grp = g < 33 ? BloodGroup.A : g < 61 ? BloodGroup.O : g < 88 ? BloodGroup.B : BloodGroup.AB; // 대략 A 33 · O 28 · B 27 · AB 12
        bool neg = (h >> 8) % 100u < 6u; // Rh- 는 드물다
        return new BloodType(grp, neg);
    }

    /// <summary>이 피를 이 사람에게 넣어도 되나 (ABO · Rh).</summary>
    public static bool Compatible(BloodType donor, BloodType to)
    {
        if (!donor.Neg && to.Neg) return false;
        return donor.Group == BloodGroup.O || donor.Group == to.Group || to.Group == BloodGroup.AB;
    }

    public bool Compatible(CrewMember donor, CrewMember to) => Compatible(TypeOf(donor), TypeOf(to));

    // ───────────── 냉장고 ─────────────

    public IEnumerable<Furniture> Fridges => _w.Ship.FurnitureOf(FurnitureType.BloodFridge).Where(f => !f.Room.Detached);
    private static bool Cold(Furniture f) => f.Machine is not Machine m || m.Powered && m.Efficiency > 0.2f;

    public int Count(Func<BloodPack, bool>? match = null) { int n = 0; foreach (var p in Packs) if (!p.Spoiled && (match == null || match(p))) n++; return n; }
    public int CompatibleFor(CrewMember to) { var t = TypeOf(to); return Count(p => Compatible(p.Type, t)); }

    /// <summary>이 사람에게 넣을 팩 (기한이 가까운 것부터 · 같은 형부터 · O형 Rh- 는 아낀다).</summary>
    public BloodPack? PackFor(CrewMember to)
    {
        var t = TypeOf(to);
        BloodPack? best = null;
        float bs = float.MaxValue;
        foreach (var p in Packs)
        {
            if (p.Spoiled || !Compatible(p.Type, t)) continue;
            float s = p.Expires / (float)SimTime.TicksPerDay + (p.Type == t ? 0f : 3f) + (p.Type.Group == BloodGroup.O && p.Type.Neg && t != p.Type ? 6f : 0f);
            if (s < bs || s == bs && best != null && p.Id < best.Id) { bs = s; best = p; }
        }
        return best;
    }

    public BloodPack Store(BloodType t, int donor, string why)
    {
        var w = _w;
        var fr = Fridges.Where(f => Cold(f)).OrderBy(f => f.Id).FirstOrDefault() ?? Fridges.OrderBy(f => f.Id).FirstOrDefault();
        var p = new BloodPack { Id = _next++, Type = t, Drawn = w.Tick, Expires = w.Tick + (long)(ShelfDays * SimTime.TicksPerDay), Donor = donor, Fridge = fr?.Id ?? -1 };
        Packs.Add(p);
        return p;
    }

    /// <summary>처음 싣는 팩 (어디든 쓸 수 있는 O형 Rh- 두 팩).</summary>
    internal void Load(int n)
    {
        for (int i = 0; i < n; i++) Store(new BloodType(BloodGroup.O, true), -1, "출항 때");
    }

    // ───────────── 수혈 ─────────────

    /// <summary>피가 모자란 사람: 피를 흘리고 체력이 많이 떨어졌다.</summary>
    public bool NeedsBlood(CrewMember c)
    {
        if (c.Dead || c.Away || c.Outside) return false;
        float l = Lost(c);
        return (l >= 0.3f || l >= 0.2f && c.Vitals.Health < 0.5f) && c.Vitals.Health < 0.95f && _w.Tick - _lastGiven.GetValueOrDefault(c.Id, -1_000_000) > SimTime.Minutes(30);
    }
    private readonly SortedDictionary<int, long> _lastGiven = new();
    private readonly SortedDictionary<int, float> _lost = new(), _prevHealth = new();
    /// <summary>흘린 피 (피가 나는 동안 빠진 체력의 합 — 응급 처치로 기운이 돌아도 피는 그대로다).</summary>
    public float Lost(CrewMember c) => _lost.TryGetValue(c.Id, out var x) ? x : 0f;
    /// <summary>수혈하러 가는 사람 (한 사람에게 한 명만 — 여럿이 냉장고로 몰리지 않게).</summary>
    private readonly SortedDictionary<int, (int by, long at)> _claims = new();
    public bool Claimed(CrewMember pt, CrewMember by) => _claims.TryGetValue(pt.Id, out var x) && x.by != by.Id && _w.Tick - x.at < SimTime.Minutes(40) && _w.Crew.Any(c => c.Id == x.by && c.CanAct && c.Job?.Activity is TransfuseActivity);
    public void Claim(CrewMember pt, CrewMember by) => _claims[pt.Id] = (by.Id, _w.Tick);
    /// <summary>냉장고에서 팩을 꺼내 들고 다니는 사람 (환자가 자리를 옮겨도 다시 냉장고로 가지 않는다 — 한 시간 넘으면 미지근해져 돌려놓는다).</summary>
    private readonly SortedDictionary<int, long> _holding = new();
    public bool Holding(CrewMember c) => _holding.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(1);
    public void Hold(CrewMember c, bool on) { if (on) _holding[c.Id] = _w.Tick; else _holding.Remove(c.Id); }
    /// <summary>마지막으로 피(또는 대용제)를 받은 때.</summary>
    public long GivenAt(CrewMember c) => _lastGiven.TryGetValue(c.Id, out var t) ? t : -1_000_000;

    /// <summary>시스템 틱마다: 피가 나는 사람의 빠진 체력을 센다 (피가 멎으면 몸이 천천히 채운다).</summary>
    private void Bleeding(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead) { _lost.Remove(c.Id); _prevHealth.Remove(c.Id); continue; }
            float h = c.Vitals.Health;
            float prev = _prevHealth.TryGetValue(c.Id, out var p) ? p : h;
            _prevHealth[c.Id] = h;
            var t = w.Casualty.Of(c);
            bool bleeding = t is { Kind: TraumaKind.Bleed } || w.Surgery.Busy(c) && w.Surgery.CaseOf(c)?.Patient == c.Id;
            float l = _lost.TryGetValue(c.Id, out var x) ? x : 0f;
            if (bleeding) l += MathF.Max(0f, prev - h);
            else l = MathF.Max(0f, l - 0.06f * dt);
            if (l > 0.001f) _lost[c.Id] = MathF.Min(1f, l); else _lost.Remove(c.Id);
        }
    }

    /// <summary>
    /// 수혈한다 (by가 곁에서): 맞는 팩 → 혈액 대용제 → (desperate면) 맞지 않는 팩. 무엇을 했는지 한 줄 (아무것도 못 했으면 "").
    /// </summary>
    public string Transfuse(CrewMember pt, CrewMember? by, bool desperate)
    {
        var w = _w;
        var t = TypeOf(pt);
        if (PackFor(pt) is BloodPack p)
        {
            Packs.Remove(p);
            Transfusions++;
            Give(pt, 0.28f);
            string from = p.Donor >= 0 && w.Crew.FirstOrDefault(x => x.Id == p.Donor) is CrewMember d ? $"{d.Name}의 피" : "실어 온 피";
            w.Log.Add(w.Tick, LogKind.Life, $"{pt.Name}에게 {p.Type.Name} 피를 넣었다 ({from}" + (by != null ? $" · {by.Name}" : "") + ")", pt.Id);
            if (p.Donor >= 0 && w.Crew.FirstOrDefault(x => x.Id == p.Donor) is CrewMember dn && dn != pt && !dn.Dead)
            {
                MarkLog.Add(pt.Memory.Marks, w.Tick, $"{dn.Name}의 피를 받았다");
                MarkLog.Add(dn.Memory.Marks, w.Tick, $"내가 준 피가 {pt.Name}에게 들어갔다");
                pt.ChangeAffinity(dn, 0.08f);
                dn.ChangeAffinity(pt, 0.04f);
            }
            return $"{p.Type.Name} 수혈";
        }
        if (ItemsV15.Use(w, ItemKind.BloodSubstitute))
        {
            Substitutes++;
            Give(pt, 0.14f);
            w.Pharmacy.Used(ItemKind.BloodSubstitute);
            w.Log.Add(w.Tick, LogKind.Warning, $"{pt.Name}({t.Name})에게 맞는 피가 없어 혈액 대용제를 넣었다", pt.Id);
            return "혈액 대용제";
        }
        if (desperate && Packs.FirstOrDefault(x => !x.Spoiled) is BloodPack bad)
        {
            Packs.Remove(bad);
            Mismatched++;
            Give(pt, 0.2f);
            // 맞지 않는 피: 열이 오르고 · 숨이 차고 · 심하면 심장이 선다
            if (R.Chance(0.55f))
            {
                Reactions++;
                pt.Vitals.Health = MathF.Max(0.01f, pt.Vitals.Health - 0.22f);
                if (R.Chance(0.35f)) w.Casualty.Inflict(pt, TraumaKind.Arrest, 0.6f, "맞지 않는 피");
                w.Log.Add(w.Tick, LogKind.Warning, $"{pt.Name}({t.Name})에게 {bad.Type.Name} 피를 넣었다 — 몸이 받지 않는다 (열 · 떨림)", pt.Id);
                if (by != null) MarkLog.Add(by.Memory.Marks, w.Tick, $"{pt.Name}에게 맞지 않는 피를 넣었다 — 다른 길이 없었다");
                return $"맞지 않는 {bad.Type.Name} 피 — 거부 반응";
            }
            w.Log.Add(w.Tick, LogKind.Warning, $"{pt.Name}({t.Name})에게 맞지 않는 {bad.Type.Name} 피를 무릅쓰고 넣었다 — 다행히 버텼다", pt.Id);
            return $"맞지 않는 {bad.Type.Name} 피";
        }
        return "";
    }

    private void Give(CrewMember pt, float amount)
    {
        pt.Vitals.Health = MathF.Min(MathF.Max(pt.Vitals.MaxHealth, 0.3f), pt.Vitals.Health + amount);
        _prevHealth[pt.Id] = pt.Vitals.Health;
        _lastGiven[pt.Id] = _w.Tick;
        if (_lost.TryGetValue(pt.Id, out var l)) { l -= amount; if (l > 0.001f) _lost[pt.Id] = l; else _lost.Remove(pt.Id); }
    }

    /// <summary>곁에 온 사람의 피를 바로 넣는다 (냉장고에 맞는 피가 없을 때).</summary>
    public void FreshFrom(CrewMember donor, CrewMember pt)
    {
        var w = _w;
        Fresh++; Donations++;
        Give(pt, 0.3f);
        Drained(donor);
        Donors.Remove(donor.Id);
        MarkLog.Add(pt.Memory.Marks, w.Tick, $"{donor.Name}의 피로 살았다");
        MarkLog.Add(donor.Memory.Marks, w.Tick, $"{pt.Name}에게 내 피를 바로 나눠 줬다");
        pt.ChangeAffinity(donor, 0.12f);
        donor.ChangeAffinity(pt, 0.06f);
        w.Relations.Remember(pt, donor, RelationReason.SavedMe, "피를 나눠 줬다");
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(donor.Name)} {pt.Name}에게 피를 바로 나눠 줬다 ({TypeOf(donor).Name} → {TypeOf(pt).Name})", donor.Id);
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(donor.Name)} {pt.Name}에게 제 피를 나눠 줬다", pt.Room, new[] { donor, pt });
    }

    /// <summary>냉장고에 넣을 피를 뽑았다.</summary>
    public void Donated(CrewMember donor)
    {
        var w = _w;
        Donations++;
        var p = Store(TypeOf(donor), donor.Id, "헌혈");
        Drained(donor);
        Donors.Remove(donor.Id);
        MarkLog.Add(donor.Memory.Marks, w.Tick, $"헌혈했다 ({p.Type.Name})");
        donor.Needs.Stress = MathF.Max(0f, donor.Needs.Stress - 0.04f); // 쓸모 있는 일을 했다
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(donor.Name)} 피 한 팩을 냉장고에 넣었다 ({p.Type.Name} · {ShelfDays:0}일)", donor.Id);
    }

    private void Drained(CrewMember d)
    {
        d.Vitals.Health = MathF.Max(0.5f, d.Vitals.Health - 0.12f);
        d.Needs.Rest = MathF.Max(0.05f, d.Needs.Rest - 0.15f); // 한나절 기운이 없다
        LastGave[d.Id] = _w.Tick;
    }

    /// <summary>피를 줄 수 있는 사람인가 (건강 · 쬔 양 · 여드레 · 앓는 것).</summary>
    public bool CanGive(CrewMember o)
    {
        if (o.Dead || o.Down || o.Away || o.Outside || o.IsChild || !o.CanAct) return false;
        if (o.Vitals.Health < 0.8f || o.Vitals.Injury > 0.1f || o.Dose >= 1f || o.Fx.Worst > 0.25f) return false;
        if (LastGave.TryGetValue(o.Id, out var last) && _w.Tick - last < SimTime.TicksPerDay * 8) return false;
        if (_w.Casualty.Of(o) != null || Donors.ContainsKey(o.Id)) return false;
        return true;
    }

    /// <summary>맞는 사람 가운데 누가 나서나: 가까운 사이 → 가까운 곳 (같으면 번호 순).</summary>
    public CrewMember? Volunteer(CrewMember pt)
    {
        var w = _w;
        var t = TypeOf(pt);
        CrewMember? best = null;
        float bs = float.MinValue;
        foreach (var o in w.Crew)
        {
            if (o == pt || !CanGive(o) || !Compatible(TypeOf(o), t)) continue;
            if (w.Surgery.Busy(o)) continue;
            float s = MathF.Round(o.AffinityTo(pt) * 4f) + (pt.AffinityTo(o) > 0.5f ? 1f : 0f) - (o.Position - pt.Position).Length() / 60f + (TypeOf(o) == t ? 0.3f : 0f);
            if (s > bs) { bs = s; best = o; }
        }
        return best;
    }

    /// <summary>급히 피를 부른다 (수혈할 맞는 팩이 없을 때).</summary>
    public CrewMember? Call(CrewMember pt, string why)
    {
        var w = _w;
        if (Donors.Values.Any(d => d.forId == pt.Id)) return null;
        var d = Volunteer(pt);
        if (d == null)
        {
            if (w.Tick - _lastAsk > SimTime.Minutes(30))
            {
                _lastAsk = w.Tick;
                w.Log.Add(w.Tick, LogKind.Warning, $"{pt.Name}({TypeOf(pt).Name})에게 피를 줄 수 있는 사람이 없다", pt.Id);
                if (w.Automation.Present && w.Automation.MainOnline)
                    w.Automation.Book.Add(ActKind.Advice, pt.Room, $"{pt.Name} {TypeOf(pt).Name} — 맞는 피 없음", "맞는 사람은 다쳤거나 이미 피를 줬다",
                        "혈액 대용제로 버틴다", "", $"bloodnone:{pt.Id}", SimTime.Hours(2));
            }
            return null;
        }
        Calls++;
        Donors[d.Id] = (pt.Id, w.Tick);
        d.NextThinkTick = Math.Min(d.NextThinkTick, w.Tick + 1);
        string who = d.AffinityTo(pt) > 0.4f ? "가까운 사이라 먼저 나섰다" : $"피가 맞아서 ({TypeOf(d).Name})";
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(d.Name)} {pt.Name}에게 피를 주러 간다 — {who}", d.Id);
        if (w.Automation.Present && w.Automation.MainOnline)
        {
            var a = w.Automation;
            var ok = w.Crew.Where(o => o != pt && !o.Dead && Compatible(TypeOf(o), TypeOf(pt))).Select(o => o.Name).Take(4).ToList();
            a.Speak.Announce(a.Voice.Style($"{pt.Name} {TypeOf(pt).Name} — {why}. 피가 맞는 사람: {string.Join(" · ", ok)}. {d.Name}, {Ko.EuRo(pt.Room?.Name ?? "의무실")} 와 주세요"), pt.Room, 2);
            a.Book.Add(ActKind.Advice, pt.Room, $"{pt.Name} {TypeOf(pt).Name} · 냉장고에 맞는 팩 {CompatibleFor(pt)}", "기록에 적힌 혈액형으로 맞는 사람을 골랐다",
                $"{Ko.EulReul(d.Name)} 불렀다", "", $"bloodcall:{pt.Id}", SimTime.Minutes(30));
        }
        return d;
    }

    // ───────────── 매 시간: 냉장고 · 기한 · 재고 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        Bleeding(dt);
        if (w.Tick % SimTime.Minutes(10) >= World.SystemInterval) return;
        float hours = (w.Tick - (_lastCheck < 0 ? w.Tick : _lastCheck)) / (float)SimTime.TicksPerHour;
        _lastCheck = w.Tick;
        // 부탁받은 사람이 못 오게 됐으면 (다쳤다 · 받을 사람이 숨졌다 · 너무 오래) 풀어 준다
        if (Donors.Count > 0)
            foreach (var id in Donors.Keys.ToList())
            {
                var (forId, asked) = Donors[id];
                var d = w.Crew.FirstOrDefault(x => x.Id == id);
                var pt = forId >= 0 ? w.Crew.FirstOrDefault(x => x.Id == forId) : null;
                if (d == null || !d.CanAct || forId >= 0 && (pt == null || pt.Dead || Lost(pt) < 0.15f) || w.Tick - asked > SimTime.Hours(forId >= 0 ? 3 : 20)) Donors.Remove(id);
            }
        if (Packs.Count > 0)
        {
            for (int i = Packs.Count - 1; i >= 0; i--)
            {
                var p = Packs[i];
                var fr = p.Fridge >= 0 ? w.Ship.Furniture.ElementAtOrDefault(p.Fridge) : null;
                bool cold = fr != null && fr.Type == FurnitureType.BloodFridge && !fr.Room.Detached && Cold(fr);
                if (!cold) p.Warm += hours;
                else if (fr != null && p.Warm > 0f) p.Warm = MathF.Max(0f, p.Warm - hours * 0.1f); // 다시 차가워져도 지난 시간은 거의 남는다
                if (p.Spoiled || w.Tick >= p.Expires)
                {
                    Packs.RemoveAt(i);
                    if (p.Spoiled) Spoiled++; else Expired++;
                    w.Log.Add(w.Tick, LogKind.Warning, p.Spoiled ? $"냉장고가 멎어 {p.Type.Name} 피 한 팩이 상했다 — 버렸다" : $"{p.Type.Name} 피 한 팩이 기한({ShelfDays:0}일)을 넘겨 버렸다");
                    if (p.Spoiled && w.Automation.Present && w.Automation.MainOnline)
                        w.Automation.Book.Add(ActKind.Advice, fr?.Room, "혈액 냉장고 온도가 올랐다", $"미지근한 채 {WarmLimit:0}시간 넘은 팩은 못 쓴다", "상한 팩을 버리라고 알렸다", "냉장고 전기를 살린다", "bloodwarm", SimTime.Hours(4));
                }
            }
            // 냉장고가 꺼졌다 — 아직 쓸 수 있을 때 알린다
            if (w.Automation.Present && w.Automation.MainOnline && Packs.Any(p => p.Warm > 1f && !p.Spoiled))
            {
                var p0 = Packs.Where(p => p.Warm > 1f && !p.Spoiled).OrderByDescending(p => p.Warm).First();
                w.Automation.Book.Add(ActKind.Advice, null, $"혈액 냉장고가 차갑지 않다 — {p0.Warm:0.#}시간째", $"{WarmLimit - p0.Warm:0.#}시간 뒤면 피가 상한다",
                    "냉장고 회로를 먼저 살리라고 알렸다", "", "bloodcold", SimTime.Hours(2));
            }
        }
        if (w.Tick % SimTime.Hours(2) < World.SystemInterval) StockTalk();
    }

    /// <summary>주컴퓨터: 배에 탄 사람들의 혈액형과 냉장고를 견줘 모자라는 형을 알리고, 조용한 때 헌혈을 부탁한다.</summary>
    private void StockTalk()
    {
        var w = _w;
        if (!w.Automation.Present || !w.Automation.MainOnline) return;
        var alive = w.Crew.Where(c => !c.Dead && !c.Away).ToList();
        if (alive.Count == 0 || Fridges.All(f => !Cold(f))) return;
        // 맞는 팩이 하나도 없는 사람 (가장 위험한 형부터)
        var bare = alive.Where(c => CompatibleFor(c) == 0).OrderBy(c => TypeOf(c).Neg ? 0 : 1).ThenBy(c => c.Id).ToList();
        bool calm = !Crisis.Acting(w) && w.Grades.Now(alive[0]) < InjuryGrade.Critical && alive.All(c => w.Grades.Now(c) < InjuryGrade.Critical);
        if (bare.Count == 0 || !calm || Donors.Values.Any(d => d.forId < 0)) return;
        var need = TypeOf(bare[0]);
        // 그 형에게 맞는 피를 줄 수 있는 사람 — 같은 형 · 아니면 O형
        var giver = alive.Where(o => CanGive(o) && Compatible(TypeOf(o), need) && !w.Surgery.Busy(o))
            .OrderBy(o => TypeOf(o) == need ? 0 : 1).ThenBy(o => o.Needs.Stress).ThenBy(o => o.Id).FirstOrDefault();
        if (w.Tick - _lastStockTalk < SimTime.Hours(20)) return;
        _lastStockTalk = w.Tick;
        string names = string.Join(" · ", bare.Take(3).Select(c => c.Name));
        if (giver == null)
        {
            w.Automation.Book.Add(ActKind.Advice, null, $"냉장고에 {need.Name}에게 맞는 피가 없다 ({names})", "줄 수 있는 사람이 없다", "다치지 않게 조심하라고 알렸다", "", "bloodstock", SimTime.Hours(20));
            return;
        }
        Calls++;
        Donors[giver.Id] = (-1, w.Tick);
        giver.NextThinkTick = Math.Min(giver.NextThinkTick, w.Tick + 1);
        w.Automation.Speak.Announce(w.Automation.Voice.Style($"냉장고에 {need.Name}에게 맞는 피가 없습니다 ({names}). {giver.Name}, 시간 날 때 헌혈을 부탁합니다"), null, 1);
        w.Automation.Book.Add(ActKind.Advice, null, $"혈액 냉장고 {Count()}팩 · {need.Name}에게 맞는 팩 0", "다치면 바로 넣을 피가 없다", $"{Ko.EulReul(giver.Name)} 헌혈해 달라고 불렀다", "", "bloodask", SimTime.Hours(20));
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Packs.Count);
        foreach (var p in Packs) { I(p.Id); I(p.Type.Code); I(p.Expires); F(p.Warm); }
        foreach (var kv in Donors) { I(kv.Key); I(kv.Value.forId); }
        foreach (var kv in _lost) { I(kv.Key); F(kv.Value); }
        I(Donations); I(Transfusions); I(Fresh); I(Substitutes); I(Mismatched); I(Reactions); I(Spoiled); I(Expired);
    }
}

/// <summary>피를 나눠 준다: 냉장고에 넣을 헌혈 (의무실에 앉아 한 팩) · 급할 때는 다친 사람 곁에 앉아 바로.</summary>
public sealed class DonateBloodActivity : Activity
{
    public override string Id => "donateblood";
    public override string Label => "헌혈";

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.Pose == Pose.Sleeping && !w.Blood.Donors.ContainsKey(c.Id)) return (0f, "—");
        if (!w.Blood.Donors.TryGetValue(c.Id, out var d)) return (0f, "—");
        if (d.forId >= 0)
        {
            var pt = w.Crew.FirstOrDefault(x => x.Id == d.forId);
            if (pt == null || pt.Dead) return (0f, "—");
            return (1.02f, $"{pt.Name}에게 피를 주러 간다 ({w.Blood.TypeOf(c).Name})");
        }
        if (Crisis.Acting(w) || c.Needs.Food < 0.25f) return (0f, "나중에");
        return (0.5f + (OnShift(c, w) ? 0f : 0.1f), "헌혈해 달라는 부탁을 받았다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (!w.Blood.Donors.TryGetValue(c.Id, out var d)) return null;
        var toils = Plans.DropOff(c, w, dist);
        if (d.forId >= 0)
        {
            var pt = w.Crew.FirstOrDefault(x => x.Id == d.forId);
            if (pt == null || pt.Dead || RadCareActivity.Near(w, dist, pt) is not Cell at) return null;
            toils.Add(new GotoToil(at));
            Cell? Chase(CrewMember cm) => (pt.Position - cm.Position).Length() < 2f || pt.Dead ? null
                : Cell.Dirs8.Select(x => pt.Cell + x).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - cm.Position).LengthSquared()).ThenBy(x => x.X).ThenBy(x => x.Y).Cast<Cell?>().FirstOrDefault();
            toils.Add(new GotoToilLate(Chase));
            toils.Add(new WaitToil(SimTime.Minutes(25), Pose.Sitting, pt.Position)
            {
                DoneWhen = (cm, world) => pt.Dead || !world.Blood.Donors.ContainsKey(cm.Id),
            });
            toils.Add(new DoToil((cm, world) =>
            {
                if (pt.Dead || (pt.Position - cm.Position).Length() > 2.6f || !world.Blood.Donors.ContainsKey(cm.Id)) return false;
                world.Blood.FreshFrom(cm, pt);
                cm.Say(world, Persona.Say(cm, "내 피 가져가. 버텨"));
                return true;
            }));
            return new Job(this, $"{pt.Name}에게 피를 준다", toils) { LogText = $"{pt.Name} 곁에 소매를 걷고 앉았다", TargetRoom = pt.Room, Urgent = true, InterruptMargin = 0.3f };
        }
        var fr = w.Blood.Fridges.Where(f => f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0])).OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();
        if (fr == null) return null;
        toils.Add(new GotoToil(fr.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Minutes(40), Pose.Sitting, fr.Center));
        toils.Add(new DoToil((cm, world) => { if (world.Blood.Donors.ContainsKey(cm.Id)) world.Blood.Donated(cm); return true; }));
        return new Job(this, "헌혈", toils) { LogText = "의무실에서 피를 한 팩 뽑는다", TargetRoom = fr.Room };
    }
}

/// <summary>수혈: 의료를 아는 사람이 냉장고에서 맞는 피를 꺼내 위중한 사람에게 넣는다 (맞는 피가 없으면 헌혈자를 부른다).</summary>
public sealed class TransfuseActivity : Activity
{
    public override string Id => "transfuse";
    public override string Label => "수혈";

    private static CrewMember? Pick(CrewMember c, World w)
    {
        CrewMember? best = null;
        foreach (var pt in w.Crew)
        {
            if (pt == c || !w.Blood.NeedsBlood(pt) || w.Surgery.OnTable(pt) || w.Blood.Claimed(pt, c) || w.MedBots.Claimed(pt)) continue; // 의료 3차 간호 로봇이 피를 가지러 갔다
            if (best == null || pt.Vitals.Health < best.Vitals.Health) best = pt;
        }
        return best;
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping && c.Needs.Rest > 0.1f && c.Role != CrewRole.Medic) return (0f, "—");
        if (c.Role != CrewRole.Medic && c.SkillLevel(Skill.Medicine) < 0.25f) return (0f, "—");
        if (w.Surgery.Busy(c)) return (0f, "수술 중");
        var pt = Pick(c, w);
        if (pt == null) return (0f, "—");
        float s = 0.95f + (pt.Vitals.Health < 0.2f ? 0.15f : 0f) + (c.Role == CrewRole.Medic ? 0.05f : 0f);
        return (s, $"{pt.Name} 피를 많이 흘렸다 — 체력 {pt.Vitals.Health * 100:0}% ({w.Blood.TypeOf(pt).Name})");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var pt = Pick(c, w);
        if (pt == null || RadCareActivity.Near(w, dist, pt) is not Cell at) return null;
        w.Blood.Claim(pt, c);
        var toils = Plans.DropOff(c, w, dist);
        bool have = w.Blood.PackFor(pt) != null;
        var fr = have ? w.Blood.Fridges.Where(f => f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0])).OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault() : null;
        if (fr != null && !w.Blood.Holding(c))
        {
            toils.Add(new GotoToil(fr.UseSpots[0]));
            toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Working, fr.Center));
            toils.Add(new DoToil((cm, world) => { world.Blood.Hold(cm, true); return true; }));
        }
        if (!have && w.Blood.CompatibleFor(pt) == 0) w.Blood.Call(pt, "맞는 피가 냉장고에 없다");
        toils.Add(new GotoToil(at));
        // 환자가 그새 자리를 떴으면 따라간다 (피를 흘린 사람은 비틀거리며 돌아다니기도 한다)
        Cell? Chase(CrewMember cm) => (pt.Position - cm.Position).Length() < 2f || pt.Dead ? null
            : Cell.Dirs8.Select(x => pt.Cell + x).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - cm.Position).LengthSquared()).ThenBy(x => x.X).ThenBy(x => x.Y).Cast<Cell?>().FirstOrDefault();
        for (int i = 0; i < 4; i++) toils.Add(new GotoToilLate(Chase));
        toils.Add(new DoToil((cm, world) =>
        {
            if (pt.Dead || (pt.Position - cm.Position).Length() > 2.6f) return false;
            bool waiting = world.Blood.Donors.Values.Any(d => d.forId == pt.Id);
            string did = world.Blood.Transfuse(pt, cm, desperate: pt.Vitals.Health < 0.12f && !waiting);
            world.Blood.Hold(cm, false);
            if (did == "") { world.Blood.Call(pt, "넣을 피가 없다"); return true; }
            cm.Practice(Skill.Medicine, 0.02f);
            cm.Say(world, Persona.Say(cm, did.StartsWith("맞지") ? "다른 길이 없어. 버텨 줘" : "피 들어간다. 조금만 앉아 있어"));
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Minutes(10), Pose.Working, pt.Position)); // 주머니를 들고 곁에서 지켜본다
        return new Job(this, $"{pt.Name} 수혈", toils) { LogText = $"{pt.Name}에게 피를 넣으러 간다", TargetRoom = pt.Room, Urgent = pt.Vitals.Health < 0.25f, InterruptMargin = 0.25f };
    }
}
