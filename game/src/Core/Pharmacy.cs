using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 의료 1차 — 약 (진통제 · 항생제 · 마취제 · 혈액 대용제 · 약초).
//   재고: 약장 · 선반에 종류별로 둔다 — 받은 날부터 기한이 흐르고, 기한이 지나면 버린다 (먼저 받은 것부터 쓴다)
//   만들기: 재배실 채소로 약초를 고르고 (작업대) · 약초로 진통제 · 항생제를 · 원료로 마취제 · 혈액 대용제를 만든다 (의료를 아는 사람)
//   항생제 남용: 감기 · 열병처럼 듣지 않는 데까지 "혹시 몰라" 쓰면 배 안에 내성이 쌓인다 → 정작 곪은 상처 · 폐렴에 잘 안 듣는다
//   진통제 의존: 오래 · 자주 먹으면 끊기 어렵다 — 약이 없으면 안절부절 · 몰래 꺼내 먹는다 (재고가 장부보다 빨리 준다)
// 사람: 의무관이 처방한다 (솜씨가 모자라면 남용 · 컴퓨터 말을 들으면 아낀다) · 의존된 사람은 약장 앞을 서성인다.
// 주컴퓨터: 쓰는 속도로 남은 날을 예측해 미리 만들게 하고 · 남용 · 내성 · 장부와 다른 재고를 알아채 의무관에게 알린다.
public sealed class PharmacySystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7349 + 2213));
    public static readonly ItemKind[] Kinds = { ItemKind.Painkiller, ItemKind.Antibiotic, ItemKind.Anesthetic, ItemKind.BloodSubstitute, ItemKind.MedHerb };
    public static float ShelfDays(ItemKind k) => k switch { ItemKind.Painkiller => 60f, ItemKind.Antibiotic => 45f, ItemKind.Anesthetic => 40f, ItemKind.BloodSubstitute => 90f, ItemKind.MedHerb => 6f, _ => 999f };
    private static int BaseWant(ItemKind k) => k switch { ItemKind.Painkiller => 4, ItemKind.Antibiotic => 3, ItemKind.Anesthetic => 3, ItemKind.BloodSubstitute => 2, ItemKind.MedHerb => 2, _ => 0 };

    // 먼저 받은 것부터 (개수 · 기한)
    private readonly Dictionary<ItemKind, List<(int n, long exp)>> _batches = new();
    private readonly Dictionary<ItemKind, List<long>> _uses = new();
    private readonly Dictionary<ItemKind, int> _bump = new();
    private readonly Dictionary<ItemKind, long> _told = new();
    public SortedDictionary<int, float> Dependence { get; } = new();
    public SortedDictionary<int, long> LastDose { get; } = new();
    public float Resistance { get; private set; }
    /// <summary>주컴퓨터 말을 듣고 항생제를 아낀다 (듣지 않는 병에는 쓰지 않는다).</summary>
    public bool Careful { get; private set; }
    public int Doses, Misuse, Failed, ExpiredCount, Sneaked, SneakSeen, Forecasts, Withdrawals;
    private int _sneakUnseen;
    private long _lastMisuseTalk = -1, _lastResistTalk = -1;

    public PharmacySystem(World w) => _w = w;

    public static bool Bacterial(string id) => id is "pneumonia" or "woundinf" or "pinkeye" or "foodpoison";
    public static bool Viral(string id) => id is "cold" or "fever" or "gastro";
    public static bool Painful(string id) => id is "toothache" or "backpain" or "arthritis" or "kidneystone" or "chemburn" or "stiffhand" or "limp";

    public int Stock(ItemKind k) => _w.Ship.CountStored(k);

    /// <summary>제작 목표 (모자랄 것 같으면 컴퓨터가 올린다).</summary>
    public int Want(ItemKind k) => Array.IndexOf(Kinds, k) < 0 ? 0 : BaseWant(k) + _bump.GetValueOrDefault(k);

    public void Used(ItemKind k)
    {
        if (!_uses.TryGetValue(k, out var l)) _uses[k] = l = new();
        l.Add(_w.Tick);
    }

    /// <summary>하루에 쓰는 양 (지난 사흘).</summary>
    public float PerDay(ItemKind k)
    {
        if (!_uses.TryGetValue(k, out var l) || l.Count == 0) return 0f;
        long from = _w.Tick - SimTime.TicksPerDay * 3;
        int n = 0; foreach (var t in l) if (t >= from) n++;
        float days = MathF.Max(1f, MathF.Min(3f, _w.Tick / (float)SimTime.TicksPerDay));
        return n / days;
    }

    /// <summary>남은 날 (쓰지 않으면 무한).</summary>
    public float DaysLeft(ItemKind k) { float d = PerDay(k); return d <= 0.01f ? float.PositiveInfinity : Stock(k) / d; }

    /// <summary>약 하나를 쓴다 (없으면 false).</summary>
    public bool Take(ItemKind k)
    {
        if (!ItemsV15.Use(_w, k)) return false;
        Doses++;
        Used(k);
        return true;
    }

    // ───────────── 처방 (Ailments.Treated에서) ─────────────

    /// <summary>의무관이 병 하나를 다스린다: 항생제 · 진통제를 쓸지 정하고 효과를 돌려준다 (나은 날 더하기).</summary>
    public float Treat(CrewMember pt, CrewMember doctor, string id)
    {
        var w = _w;
        if (Bacterial(id))
        {
            if (!Take(ItemKind.Antibiotic)) return 0f;
            Resistance = MathF.Min(1f, Resistance + 0.025f);
            if (R.Chance(Resistance * 0.9f))
            {
                Failed++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{pt.Name}에게 항생제를 썼지만 잘 듣지 않는다 (내성)", pt.Id);
                return 0f;
            }
            return 1.2f;
        }
        if (Viral(id))
        {
            // 듣지 않는 병에도 "혹시 몰라" — 서툰 의무관 · 컴퓨터 말을 안 들었을 때
            float urge = (1f - doctor.SkillLevel(Skill.Medicine)) * 0.6f + (pt.AffinityTo(doctor) > 0.5f || doctor.AffinityTo(pt) > 0.5f ? 0.15f : 0f);
            if (!Careful && Stock(ItemKind.Antibiotic) > 0 && R.Chance(urge) && Take(ItemKind.Antibiotic))
            {
                Misuse++;
                Resistance = MathF.Min(1f, Resistance + 0.06f);
                w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(doctor.Name)} {pt.Name}에게 혹시 몰라 항생제를 줬다", doctor.Id);
            }
            return 0f;
        }
        if (Painful(id)) Painkill(pt, doctor, "아픈 곳");
        return 0f;
    }

    /// <summary>진통제를 준다 — 덜 아프지만 자주 먹으면 끊기 어렵다.</summary>
    public bool Painkill(CrewMember pt, CrewMember? by, string why)
    {
        var w = _w;
        if (LastDose.TryGetValue(pt.Id, out var last) && w.Tick - last < SimTime.Hours(6)) return false;
        if (!Take(ItemKind.Painkiller)) return false;
        LastDose[pt.Id] = w.Tick;
        pt.Needs.Stress = MathF.Max(0f, pt.Needs.Stress - 0.12f);
        float dep = Dependence.GetValueOrDefault(pt.Id) + 0.09f * (1.2f - pt.Traits.Calm);
        Dependence[pt.Id] = MathF.Min(1f, dep);
        return true;
    }

    // ───────────── 2시간마다: 기한 · 예측 · 의존 · 내성 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick % SimTime.Hours(2) >= World.SystemInterval) return;
        foreach (var k in Kinds) Reconcile(k);
        Resistance = MathF.Max(0f, Resistance - 0.012f / 12f); // 하루에 1%씩 (두 시간마다)
        Cravings();
        if (w.Automation.Present && w.Automation.MainOnline) Watch();
    }

    /// <summary>창고의 실제 수와 묶음 기록을 맞추고 (쓴 건 오래된 것부터 · 새로 생긴 건 오늘 받은 것) 기한이 지난 것을 버린다.</summary>
    private void Reconcile(ItemKind k)
    {
        var w = _w;
        if (!_batches.TryGetValue(k, out var b)) _batches[k] = b = new();
        int have = Stock(k), sum = 0;
        foreach (var x in b) sum += x.n;
        if (sum < have) b.Add((have - sum, w.Tick + (long)(ShelfDays(k) * SimTime.TicksPerDay)));
        while (sum > have && b.Count > 0)
        {
            int cut = Math.Min(b[0].n, sum - have);
            sum -= cut;
            if (cut >= b[0].n) b.RemoveAt(0); else b[0] = (b[0].n - cut, b[0].exp);
        }
        int gone = 0;
        while (b.Count > 0 && b[0].exp <= w.Tick)
        {
            int n = b[0].n;
            b.RemoveAt(0);
            for (int i = 0; i < n; i++) if (ItemsV15.Use(w, k)) gone++;
        }
        if (gone > 0)
        {
            ExpiredCount += gone;
            w.Log.Add(w.Tick, LogKind.Work, $"기한이 지난 {ItemKinds.Name(k)} {gone}개를 버렸다 ({ShelfDays(k):0}일)");
        }
        _uses.GetValueOrDefault(k)?.RemoveAll(t => t < w.Tick - SimTime.TicksPerDay * 3);
    }

    /// <summary>다음 기한이 언제인가 (없으면 -1).</summary>
    public long NextExpiry(ItemKind k) => _batches.TryGetValue(k, out var b) && b.Count > 0 ? b[0].exp : -1;

    private void Cravings()
    {
        var w = _w;
        foreach (var id in Dependence.Keys.ToList())
        {
            float d = Dependence[id] = MathF.Max(0f, Dependence[id] - 0.025f / 12f);
            var c = w.Crew.FirstOrDefault(x => x.Id == id);
            if (c == null || c.Dead) { Dependence.Remove(id); continue; }
            if (d < 0.01f) { Dependence.Remove(id); continue; }
            if (d >= 0.55f && !w.Ailments.Has(c, "pkdep"))
            {
                w.Ailments.Catch(c, "pkdep", null, "진통제를 오래 먹었다");
                MarkLog.Add(c.Memory.Marks, w.Tick, "진통제 없이는 잠이 안 온다");
            }
            if (d < 0.5f || !c.CanAct) continue;
            long since = w.Tick - LastDose.GetValueOrDefault(id, -1_000_000);
            if (since < SimTime.Hours(12)) continue;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f * d);
            // 몰래 하나 꺼내 먹는다 (약장이 손 닿는 데 있으면 — 의존이 깊고 마음이 약할수록)
            if (Stock(ItemKind.Painkiller) > 0 && R.Chance(0.25f * d * (1.2f - c.Traits.Calm)) && ItemsV15.Use(w, ItemKind.Painkiller))
            {
                Sneaked++; _sneakUnseen++;
                LastDose[id] = w.Tick;
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.1f);
                Dependence[id] = MathF.Min(1f, d + 0.04f);
                MarkLog.Add(c.Memory.Marks, w.Tick, "약장에서 진통제를 몰래 하나 꺼냈다");
            }
            else if (since > SimTime.Hours(24)) Withdrawals++;
        }
    }

    /// <summary>주컴퓨터: 남은 날 예측 → 만들게 한다 · 남용 · 내성 · 장부와 다른 재고.</summary>
    private void Watch()
    {
        var w = _w;
        var a = w.Automation;
        foreach (var k in Kinds)
        {
            if (k == ItemKind.MedHerb) continue;
            float left = DaysLeft(k);
            int stock = Stock(k);
            bool deferred = k == ItemKind.Anesthetic && w.Surgery.WaitingFor(ItemKind.Anesthetic) > 0;
            if (left < 4f || stock == 0 && (PerDay(k) > 0f || deferred))
            {
                int want = Math.Max(BaseWant(k), (int)MathF.Ceiling(PerDay(k) * 5f)) + (deferred ? 2 : 0);
                _bump[k] = Math.Max(0, want - BaseWant(k)) + 1;
                if (w.Tick - _told.GetValueOrDefault(k, -1_000_000) > SimTime.Hours(18))
                {
                    _told[k] = w.Tick;
                    Forecasts++;
                    string when = float.IsInfinity(left) ? "지금 하나도 없다" : $"이대로면 {left:0.#}일 뒤 떨어진다";
                    a.Book.Add(ActKind.Forecast, null, $"{ItemKinds.Name(k)} {stock}개 · 하루 {PerDay(k):0.#}개씩 씀", when,
                        $"{ItemKinds.Name(k)}을 더 만들자고 했다 ({Want(k)}개까지)", "", $"rx:{k}", SimTime.Hours(18));
                    a.Speak.Announce(a.Voice.Style($"{ItemKinds.Name(k)} — {when}. 의무실에서 더 만들어 두십시오" + (deferred ? " · 마취제가 없어 미룬 수술이 있습니다" : "")), null, 1);
                }
            }
            else if (_bump.ContainsKey(k) && left > 8f) _bump.Remove(k);
            // 기한이 하루 안에 지나는 약
            long ex = NextExpiry(k);
            if (ex > 0 && ex - w.Tick < SimTime.TicksPerDay && stock > 0)
                a.Book.Add(ActKind.Forecast, null, $"{ItemKinds.Name(k)} 기한이 하루 안에 지난다", "기한이 지나면 버린다", "먼저 쓰라고 알렸다", "", $"rxexp:{k}", SimTime.TicksPerDay);
        }
        // 남용: 듣지 않는 병에 쓴 항생제 → 의무관에게 아끼자고
        if (!Careful && (Misuse >= 2 || Resistance > 0.25f) && w.Tick - _lastMisuseTalk > SimTime.Hours(24))
        {
            _lastMisuseTalk = w.Tick;
            Careful = true;
            var med = w.Crew.Where(c => !c.Dead && c.Role == CrewRole.Medic).OrderBy(c => c.Id).FirstOrDefault();
            a.Book.Add(ActKind.Advice, null, $"듣지 않는 병(감기 · 열병)에 항생제 {Misuse}번 · 내성 {Resistance * 100:0}%", "자꾸 쓰면 곪은 상처 · 폐렴에 듣지 않게 된다",
                "항생제는 곪았을 때 · 폐렴일 때만 쓰자고 했다", med != null ? $"{med.Name}에게" : "", "rxmisuse", SimTime.Hours(48));
            a.Speak.Announce(a.Voice.Style("감기와 열병에는 항생제가 듣지 않습니다. 곪은 상처와 폐렴에만 쓰십시오"), null, 1);
            if (med != null) MarkLog.Add(med.Memory.Marks, w.Tick, "컴퓨터가 항생제를 아끼라고 했다");
        }
        if (Resistance > 0.4f && w.Tick - _lastResistTalk > SimTime.Hours(36))
        {
            _lastResistTalk = w.Tick;
            a.Book.Add(ActKind.Advice, null, $"항생제 내성 {Resistance * 100:0}% · 듣지 않은 일 {Failed}번", "배 안의 균이 약을 이긴다", "수술 뒤 예방으로 쓰지 말고 멸균을 더 하자고 했다", "", "rxresist", SimTime.Hours(36));
        }
        // 장부보다 빨리 준다: 약장 칸에 데이터선이 닿으면 센다
        if (_sneakUnseen >= 2 && w.Ship.FurnitureOf(FurnitureType.MedCabinet).Any(f => f.Room.DataLinked && !f.Room.Detached))
        {
            SneakSeen++;
            _sneakUnseen = 0;
            var who = Dependence.Where(kv => kv.Value >= 0.5f).OrderByDescending(kv => kv.Value).Select(kv => w.Crew.FirstOrDefault(c => c.Id == kv.Key)).FirstOrDefault(c => c != null && !c.Dead);
            var med = w.Crew.Where(c => !c.Dead && c.CanAct && c.Role == CrewRole.Medic && c != who).OrderBy(c => c.Id).FirstOrDefault();
            a.Book.Add(ActKind.Advice, null, "진통제가 장부보다 빨리 준다", who != null ? $"오래 먹던 사람이 있다 ({who.Name})" : "누가 꺼냈는지 모른다",
                med != null ? $"{med.Name}에게만 조용히 알렸다" : "약장을 잠그자고 했다", "", "rxsneak", SimTime.Hours(24));
            if (who != null && med != null)
            {
                // 의무관이 조용히 이야기한다 — 처음엔 서운하지만, 줄여 가는 길을 함께 정한다
                who.ChangeAffinity(med, -0.04f);
                med.ChangeAffinity(who, 0.03f);
                MarkLog.Add(who.Memory.Marks, w.Tick, $"{Ko.IGa(med.Name)} 진통제 이야기를 꺼냈다");
                MarkLog.Add(med.Memory.Marks, w.Tick, $"{who.Name}의 진통제 — 줄여 가기로 했다");
                Dependence[who.Id] = MathF.Max(0f, Dependence[who.Id] - 0.1f);
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(med.Name)} {who.Name}와 진통제를 줄여 가기로 이야기했다", med.Id);
            }
        }
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var k in Kinds) if (_batches.TryGetValue(k, out var b)) { I((int)k); foreach (var x in b) { I(x.n); I(x.exp); } }
        foreach (var kv in Dependence) { I(kv.Key); F(kv.Value); }
        F(Resistance); I(Careful ? 1 : 0); I(Doses); I(Misuse); I(Failed); I(ExpiredCount); I(Sneaked);
    }
}
