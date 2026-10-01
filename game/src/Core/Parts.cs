using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v14.6 부품마다의 수명과 내력.
// 같은 설비 안에서도 베어링은 새것, 모터와 축 씰은 처음 그대로 — 부품마다 돈 시간과 수명이 따로 간다.
// 어디서 온 부품인지(처음부터 · 새것 · 기항지 · 떼어 온 중고 · 손으로 만든 것), 누가 만들고 시험했는지, 같은 묶음은 무엇인지가 남는다.
// 같은 자리가 또 나가면 원인을 캐고, 일찍 나간 부품의 묶음은 다른 설비·선반의 것까지 의심한다.
// 그 설비를 여러 번 만진 정비사는 되풀이되는 기척을 먼저 알아듣는다.

public enum PartOrigin { Original, Factory, Port, Salvage, Handmade }

/// <summary>부품 하나의 내력 (선반에 있든 설비에 달렸든).</summary>
public sealed class PartLot
{
    public int Id { get; init; }
    public ItemKind Kind { get; init; }
    public PartOrigin Origin { get; init; }
    /// <summary>같은 때 같은 곳에서 온 묶음 (같은 결함을 함께 가질 수 있다).</summary>
    public string Batch { get; init; } = "";
    public string From { get; init; } = "";
    public string? Maker { get; init; }
    /// <summary>실제 품질 (수명 배율) — 시험해 보기 전엔 아무도 모른다.</summary>
    public float Quality { get; set; } = 1f;
    public bool Tested { get; set; }
    public string? TestNote { get; set; }
    /// <summary>같은 묶음이 일찍 나가서 의심받는 중.</summary>
    public bool Suspect { get; set; }
    public long Made { get; init; }
    /// <summary>떼어 온 중고가 이미 돈 시간 (모른다).</summary>
    public float PriorHours { get; init; }

    public string OriginName => Origin switch
    {
        PartOrigin.Original => "처음부터 달린 것",
        PartOrigin.Factory => $"새 부품 · {Batch}",
        PartOrigin.Port => $"{From}에서 산 것 · {Batch}",
        PartOrigin.Salvage => $"{From}에서 떼어 온 중고",
        _ => $"{Maker}이(가) 손으로 만든 것",
    };

    public string Label => OriginName + (Tested ? $" · 시험: {TestNote}" : Origin is PartOrigin.Salvage or PartOrigin.Handmade ? " · 시험 안 함" : "") + (Suspect ? " · 같은 묶음 의심" : "");
}

/// <summary>설비에 달린 부품 한 자리.</summary>
public sealed class FittedPart
{
    public PartSpec Spec { get; init; } = null!;
    public ItemKind? Kind { get; init; }
    public PartLot Lot { get; set; } = null!;
    public long Since { get; set; }
    public float Hours { get; set; }
    public int Failures { get; set; }
    public long LastFailure { get; set; } = -1;
    /// <summary>되풀이되는 고장의 진짜 원인 (못 찾았으면 계속 빨리 닳는다).</summary>
    public string? Root { get; set; }
    public bool RootKnown { get; set; }
    public float Stress { get; set; } = 1f;
    public List<Mark> Marks { get; } = new();

    public float Life => PartsSystem.BaseLife(Kind) * Lot.Quality;
    /// <summary>0~1+: 수명 대비 돈 시간 (중고는 앞서 돈 시간까지).</summary>
    public float Age => (Hours + Lot.PriorHours) / MathF.Max(1f, Life);
}

public sealed class PartStats
{
    public int Replaced, EarlyFailures, Recurrences, RootsFound, RootsMissed, BatchAlerts, Tested, Rejected, Noticed;
    public int HeavyLifts, Hoisted, Strains, FarRepairs, CartRepairs, Cramped;
    public readonly int[] ByOrigin = new int[5];
    public string Summary() =>
        $"교체 {Replaced}(처음 {ByOrigin[0]} · 새것 {ByOrigin[1]} · 기항지 {ByOrigin[2]} · 중고 {ByOrigin[3]} · 손으로 {ByOrigin[4]}) · 일찍 나감 {EarlyFailures} · 재발 {Recurrences}(원인 찾음 {RootsFound} · 못 찾음 {RootsMissed}) · 묶음 의심 {BatchAlerts} · 시험 {Tested}(버림 {Rejected}) · 먼저 알아들음 {Noticed}"
        + $" · 무거운 부품 맨손 {HeavyLifts}(허리 {Strains}) · 호이스트 {Hoisted} · 정비실 밖 {FarRepairs}(카트 {CartRepairs}) · 비좁음 {Cramped}";
}

public sealed class PartsSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6563 + 71));
    private readonly Dictionary<int, FittedPart[]> _fitted = new();   // 설비 몸체 Id → 부품 자리들
    private readonly Dictionary<ItemKind, List<PartLot>> _stock = new(); // 선반의 부품 내력 (먼저 온 것부터)
    private int _lotId;
    public HashSet<string> SuspectBatches { get; } = new();
    public PartStats Stats { get; } = new();

    public PartsSystem(World w) => _w = w;

    /// <summary>새로 달았을 때의 수명 (돌린 시간).</summary>
    public static float BaseLife(ItemKind? k) => k switch
    {
        ItemKind.Bearing => 700f, ItemKind.Filter => 400f, ItemKind.Lubricant => 350f, ItemKind.Pump => 1300f, ItemKind.Motor => 1800f,
        ItemKind.PowerController => 2200f, ItemKind.Sensor => 1500f, ItemKind.Electronics => 2000f, ItemKind.Cable => 2600f, ItemKind.Fuse => 1600f,
        ItemKind.ReactorControl => 3000f, ItemKind.Sealant => 900f, _ => 2400f,
    };

    private PartLot NewLot(ItemKind? kind, PartOrigin origin, string batch = "", string from = "", string? maker = null, float quality = 1f, float prior = 0f) =>
        new() { Id = ++_lotId, Kind = kind ?? ItemKind.Plate, Origin = origin, Batch = batch, From = from, Maker = maker, Quality = quality, Made = _w.Tick, PriorHours = prior };

    /// <summary>그 설비의 부품 자리들 (처음 볼 때 만든다: 처음부터 달린 것, 배가 지나온 만큼 닳아 있다).</summary>
    public FittedPart[] Of(Machine m)
    {
        if (_fitted.TryGetValue(m.Body.Id, out var parts)) return parts;
        var specs = MachineParts.For(m.Body.Type)
                    ?? m.Spec.FaultKinds.Select(k => Faults.Spec(k).Part).Where(k => k != null).Distinct()
                        .Select(k => new PartSpec(ItemKinds.Name(k!.Value), m.Spec.FaultKinds.Where(f => Faults.Spec(f).Part == k).ToArray(), Array.Empty<OmenCause>())).ToArray();
        var rng = new Rng(unchecked(_w.Seed * 7411 + m.Body.Id * 6271 + 13));
        parts = specs.Select(s =>
        {
            var kind = s.Faults.Select(f => Faults.Spec(f).Part).FirstOrDefault(k => k != null);
            var fp = new FittedPart { Spec = s, Kind = kind, Lot = NewLot(kind, PartOrigin.Original, "처음", quality: 0.85f + 0.3f * rng.Float()) };
            fp.Hours = fp.Life * (0.05f + 0.4f * rng.Float()) * (0.4f + 1.2f * m.Wear); // 배가 지나온 만큼 (부품마다 다르게)
            return fp;
        }).ToArray();
        _fitted[m.Body.Id] = parts;
        return parts;
    }

    public FittedPart? Owner(Machine m, FaultKind k) => Of(m).FirstOrDefault(p => p.Spec.Faults.Contains(k));

    /// <summary>시스템 틱: 돌아가는 설비의 부품이 닳는다 (뜨거우면 · 때가 끼면 · 원인을 못 고쳤으면 더).</summary>
    public void Update(float dt)
    {
        foreach (var m in _w.Ship.Machines)
        {
            if (m.Stopped || !m.Active || m.Body.Room.Detached) continue;
            float load = (1f + MathF.Max(0f, m.Heat - 0.4f)) * (m.Fouled > 0.3f ? 1.3f : 1f);
            foreach (var p in Of(m)) p.Hours += dt * load * p.Stress;
        }
    }

    /// <summary>오래된 부품이 많을수록 고장이 잦다 (설비의 고장 확률 배율 — 반쯤 닳은 설비가 1, 새것은 덜, 수명을 넘긴 부품이 있으면 훨씬 더).</summary>
    public float AgeFactor(Machine m)
    {
        var parts = Of(m);
        if (parts.Length == 0) return 1f;
        float mean = 0f, over = 0f;
        foreach (var p in parts) { mean += MathF.Min(1.5f, p.Age); if (p.Age > 0.9f) over += p.Age - 0.9f; }
        mean /= parts.Length;
        return Math.Clamp(0.7f + 0.6f * mean + 1.5f * over, 0.6f, 3f);
    }

    /// <summary>고장 고르기: 오래된 부품이 맡은 고장일수록 잘 난다.</summary>
    public FaultKind PickFault(Machine m, List<FaultKind> choices, float roll)
    {
        float total = 0f;
        Span<float> weight = stackalloc float[choices.Count];
        for (int i = 0; i < choices.Count; i++)
        {
            weight[i] = 0.4f + MathF.Min(2f, Owner(m, choices[i])?.Age ?? 0.5f);
            total += weight[i];
        }
        float x = roll * total;
        for (int i = 0; i < choices.Count; i++) { x -= weight[i]; if (x <= 0f) return choices[i]; }
        return choices[^1];
    }

    // ───────────────────────────── 선반의 부품 ─────────────────────────────

    /// <summary>선반에 부품이 들어왔다 (만들었다 · 샀다 · 떼어 왔다).</summary>
    public PartLot Stock(ItemKind kind, PartOrigin origin, string batch = "", string from = "", string? maker = null, float quality = 1f, float prior = 0f)
    {
        var lot = NewLot(kind, origin, batch, from, maker, quality, prior);
        if (!_stock.TryGetValue(kind, out var list)) _stock[kind] = list = new();
        list.Add(lot);
        return lot;
    }

    /// <summary>시험대에 올려 볼 수 있는 부품.</summary>
    public static readonly ItemKind[] TestKinds = { ItemKind.Bearing, ItemKind.Pump, ItemKind.Motor, ItemKind.PowerController, ItemKind.Sensor, ItemKind.Electronics, ItemKind.ReactorControl };

    /// <summary>내력을 따지는 부품 (설비에 들어가는 것).</summary>
    public static bool IsPart(ItemKind k) => k is ItemKind.Bearing or ItemKind.Pump or ItemKind.Motor or ItemKind.PowerController or ItemKind.Sensor
        or ItemKind.Electronics or ItemKind.Cable or ItemKind.Fuse or ItemKind.Filter or ItemKind.ReactorControl or ItemKind.Lubricant;

    /// <summary>작업대에서 만들었다: 만든 사람과 그날이 묶음 — 솜씨만큼 품질이 고르다.</summary>
    public void Made(ItemKind kind, int count, CrewMember maker, float skill)
    {
        if (!IsPart(kind)) return;
        string batch = $"{maker.Name} {SimTime.Day(_w.Tick)}일째";
        for (int i = 0; i < count; i++)
            Stock(kind, PartOrigin.Handmade, batch, "작업대", maker.Name, Math.Clamp(0.5f + 0.55f * skill + 0.3f * (R.Float() - 0.5f) * (1.2f - skill), 0.3f, 1.15f));
    }

    /// <summary>다른 설비·난파선에서 떼어 왔다: 얼마나 돌았는지 모른다.</summary>
    public void Salvaged(ItemKind kind, int count, string from)
    {
        if (!IsPart(kind)) return;
        for (int i = 0; i < count; i++)
            Stock(kind, PartOrigin.Salvage, "", from, null, 0.5f + 0.55f * R.Float(), BaseLife(kind) * (0.15f + 0.55f * R.Float()));
    }

    /// <summary>기항지에서 샀다: 한 번에 산 것은 한 묶음 (같은 결함을 함께 가질 수 있다).</summary>
    public void Bought(ItemKind kind, int count, string port)
    {
        if (!IsPart(kind)) return;
        string batch = $"{port} {SimTime.Day(_w.Tick)}일째 {ItemKinds.Name(kind)}";
        float q = R.Chance(0.15f) ? 0.35f + 0.25f * R.Float() : 0.85f + 0.3f * R.Float(); // 가끔 불량 묶음
        for (int i = 0; i < count; i++) Stock(kind, PartOrigin.Port, batch, port, null, q + 0.05f * (R.Float() - 0.5f));
    }

    /// <summary>선반 수와 내력 수를 맞춘다 (모르는 길로 들어온 것은 "출항 때 실은 것" · 다 쓰고 없으면 비운다 — 손에 들고 가는 사이엔 그대로 둔다).</summary>
    private List<PartLot> Reconcile(ItemKind kind)
    {
        if (!_stock.TryGetValue(kind, out var list)) _stock[kind] = list = new();
        int have = _w.Ship.CountStored(kind);
        while (list.Count < have) list.Add(NewLot(kind, PartOrigin.Factory, "출항 때 실은 것", quality: 0.9f + 0.2f * R.Float()));
        if (have == 0 && list.Count > 0 && !_w.Crew.Any(c => c.Carrying?.Kind == kind || c.Kit.Any(k => k.kind == kind))) list.Clear();
        return list;
    }

    public IReadOnlyList<PartLot> StockOf(ItemKind kind) => Reconcile(kind);

    /// <summary>달 부품 하나를 고른다: 시험한 것 → 의심받지 않는 것 → 먼저 온 것.</summary>
    private PartLot TakeLot(ItemKind kind, bool used)
    {
        var list = Reconcile(kind);
        if (used)
        {
            // 떼어 온 중고를 쓴다 (선반에 내력이 있으면 그것을)
            var sal = list.FirstOrDefault(l => l.Origin == PartOrigin.Salvage);
            if (sal != null) { list.Remove(sal); return sal; }
            return NewLot(kind, PartOrigin.Salvage, "", "다른 설비", quality: 0.55f + 0.5f * R.Float(), prior: BaseLife(kind) * (0.2f + 0.5f * R.Float()));
        }
        var lot = list.OrderBy(l => l.Suspect && !l.Tested ? 1 : 0).ThenBy(l => l.Tested ? 0 : 1).ThenBy(l => l.Made).FirstOrDefault();
        if (lot == null) return NewLot(kind, PartOrigin.Factory, "출항 때 실은 것", quality: 0.9f + 0.2f * R.Float());
        list.Remove(lot);
        return lot;
    }

    // ───────────────────────────── 고쳤을 때 ─────────────────────────────

    /// <summary>수리를 마쳤다: 그 고장을 맡은 부품만 새것으로 (나머지는 그대로) · 일찍 나갔으면 묶음을 의심 · 또 나갔으면 원인을 캔다.</summary>
    public void OnFixed(Machine m, FaultKind fault, CrewMember c, bool usedPart)
    {
        var w = _w;
        var p = Owner(m, fault);
        if (p == null) return;
        long now = w.Tick;
        Space(m, fault, c);
        bool recurring = p.LastFailure >= 0 && now - p.LastFailure < SimTime.TicksPerDay * 12;
        bool early = p.Age < 0.35f;
        var old = p.Lot;
        p.Failures++;
        p.LastFailure = now;

        // 일찍 나간 부품: 묶음을 의심한다 (다른 설비에 달린 것 · 선반의 것)
        if (early && old.Origin is not PartOrigin.Original && old.Batch.Length > 0 && SuspectBatches.Add(old.Batch))
        {
            Stats.EarlyFailures++;
            Stats.BatchAlerts++;
            int fitted = 0, shelf = 0;
            foreach (var (bid, list) in _fitted)
                foreach (var q in list)
                    if (q != p && q.Lot.Batch == old.Batch) { q.Lot.Suspect = true; fitted++; MarkLog.Add(q.Marks, now, $"같은 묶음({old.Batch})이 {m.Name}에서 일찍 나갔다 — 의심"); }
            foreach (var l in _stock.Values.SelectMany(x => x))
                if (l.Batch == old.Batch) { l.Suspect = true; shelf++; }
            w.Log.Add(now, LogKind.Warning, $"{m.Name}의 {p.Spec.Name}이(가) {p.Hours:0}시간 만에 나갔다 — 같은 묶음({old.Batch})을 의심한다: 설비에 {fitted}개 · 선반에 {shelf}개", c.Id);
            MarkLog.Add(m.Marks, now, $"{p.Spec.Name} 일찍 나감 — {old.OriginName}");
        }
        else if (early) Stats.EarlyFailures++;

        // 또 나갔다: 원인을 캔다 (그 설비를 여러 번 만진 사람 · 솜씨 좋은 사람이 잘 찾는다)
        if (recurring)
        {
            Stats.Recurrences++;
            p.Root ??= RootOf(m, p);
            float find = 0.25f + 0.45f * c.FamiliarityWith(m.Body.Type) + 0.3f * c.SkillLevel(m.Spec.Skill);
            if (!p.RootKnown && R.Chance(find))
            {
                p.RootKnown = true;
                Stats.RootsFound++;
                p.Stress = 1f;
                MarkLog.Add(p.Marks, now, $"{c.Name}: 되풀이된 원인 — {p.Root} (바로잡았다)");
                MarkLog.Add(m.Marks, now, $"{p.Spec.Name}이(가) 또 나갔다 — {c.Name}이(가) 원인({p.Root})을 찾아 바로잡았다");
                w.Log.Add(now, LogKind.Work, $"{m.Name}의 {p.Spec.Name}이(가) 또 나갔다 — 원인은 {p.Root} (바로잡았다)", c.Id);
                w.History.Add(w, HistoryKind.Maintenance, $"{Ko.IGa(c.Name)} {m.Name}의 {p.Spec.Name}이(가) 자꾸 나가는 까닭({p.Root})을 찾아냈다", m.Body.Room, new[] { c });
            }
            else if (!p.RootKnown)
            {
                Stats.RootsMissed++;
                p.Stress = MathF.Min(2f, p.Stress + 0.25f);
                MarkLog.Add(m.Marks, now, $"{p.Spec.Name}이(가) 또 나갔다 — 원인을 못 찾았다 (또 그럴 수 있다)");
                w.Log.Add(now, LogKind.Warning, $"{m.Name}의 {p.Spec.Name}이(가) 또 나갔다 — 갈기만 했다, 원인은 모른다", c.Id);
            }
        }

        // 부품을 갈았다 (이 고장이 부품을 먹는 고장일 때만) — 나머지 자리는 그대로
        if (Faults.Spec(fault).Part is ItemKind kind && p.Kind == kind)
        {
            var lot = TakeLot(kind, usedPart);
            p.Lot = lot;
            p.Hours = 0f;
            p.Since = now;
            Stats.Replaced++;
            Stats.ByOrigin[(int)lot.Origin]++;
            MarkLog.Add(p.Marks, now, $"{c.Name}: 갈았다 — {lot.Label}");
        }
        else MarkLog.Add(p.Marks, now, $"{c.Name}: 손봤다 ({Faults.Spec(fault).Name})");
    }

    /// <summary>되풀이되는 고장의 진짜 원인 (그때의 사정으로).</summary>
    private string RootOf(Machine m, FittedPart p)
    {
        if (m.Heat > 0.45f) return "옆 설비 열기에 늘 달궈진다";
        if (m.Fouled > 0.3f) return "분진·때가 낀다";
        if (p.Lot.Origin is PartOrigin.Salvage or PartOrigin.Handmade && !p.Lot.Tested) return "부품 자체 불량";
        if (m.Line < 0.8f) return "새는 이음에서 물이 튄다";
        return p.Kind is ItemKind.Bearing or ItemKind.Pump or ItemKind.Motor ? "축 정렬이 틀어졌다" : "접점이 헐겁다";
    }

    /// <summary>시험대: 의심받거나 시험 안 한 부품을 돌려 본다 (나쁜 것은 버린다).</summary>
    public (int tested, int rejected) Test(ItemKind kind, CrewMember c, int max)
    {
        var w = _w;
        int t = 0, r = 0;
        var list = Reconcile(kind);
        foreach (var lot in list.Where(l => !l.Tested && (l.Suspect || l.Origin is PartOrigin.Salvage or PartOrigin.Handmade)).Take(max).ToList())
        {
            t++;
            lot.Tested = true;
            bool bad = lot.Quality < 0.7f;
            lot.TestNote = bad ? $"불량 ({c.Name})" : $"합격 ({c.Name} · 수명 {lot.Quality * 100:0}%)";
            if (bad)
            {
                r++;
                list.Remove(lot);
                foreach (var box in w.Ship.Containers) if (box.Storage!.Take(kind, 1) > 0) break;
            }
        }
        Stats.Tested += t; Stats.Rejected += r;
        return (t, r);
    }

    // ───────────────────────────── 정비 공간 · 장비 ─────────────────────────────

    /// <summary>둘이 들거나 매달아야 하는 부품.</summary>
    public static bool Heavy(ItemKind? k) => k is ItemKind.Motor or ItemKind.Pump or ItemKind.ReactorControl;

    /// <summary>설비 둘레의 빈 바닥 칸 (큰 수리는 부품을 내려놓고 몸을 돌릴 자리가 있어야 한다).</summary>
    public int Clearance(Machine m)
    {
        var f = m.Body;
        var ship = _w.Ship;
        int free = 0;
        for (int x = f.MinX - 1; x <= f.MinX + f.Width; x++)
        for (int y = f.MinY - 1; y <= f.MinY + f.Height; y++)
        {
            if (x >= f.MinX && x < f.MinX + f.Width && y >= f.MinY && y < f.MinY + f.Height) continue;
            if (ship.IsOpenFloor(new Cell(x, y))) free++;
        }
        return free;
    }

    /// <summary>수리 시간 배율: 무거운 부품(호이스트가 없으면 느리다) · 정비 카트(정비실 밖) · 비좁은 자리(큰 수리).</summary>
    public float RepairFactor(Machine m, FaultKind fault, out string? note)
    {
        var w = _w;
        float f = 1f;
        var notes = new List<string>(2);
        var spec = Faults.Spec(fault);
        if (Heavy(spec.Part))
        {
            if (Modules.Working(w, FurnitureType.Hoist) > 0) { f *= 0.85f; notes.Add("호이스트로 매달아 든다"); }
            else { f *= 1.3f; notes.Add($"{ItemKinds.Name(spec.Part!.Value)}을(를) 맨손으로 든다"); }
        }
        if (m.Body.Room.Type != RoomType.Workshop && Modules.Working(w, FurnitureType.MaintCart) > 0) { f *= 0.9f; notes.Add("정비 카트를 끌고 왔다"); }
        if (spec.RepairHours >= 1.5f && Clearance(m) < 3) { f *= 1.25f; notes.Add($"비좁다 (둘레 빈 칸 {Clearance(m)})"); }
        note = notes.Count > 0 ? string.Join(" · ", notes) : null;
        return f;
    }

    /// <summary>수리를 마친 뒤의 정비 공간 기록 (맨손으로 무거운 부품 · 허리 · 정비실 밖 · 비좁음).</summary>
    private void Space(Machine m, FaultKind fault, CrewMember c)
    {
        var w = _w;
        var spec = Faults.Spec(fault);
        if (m.Body.Room.Type != RoomType.Workshop) { Stats.FarRepairs++; if (Modules.Working(w, FurnitureType.MaintCart) > 0) Stats.CartRepairs++; }
        if (spec.RepairHours >= 1.5f && Clearance(m) < 3) { Stats.Cramped++; MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: 비좁아 애먹었다"); }
        if (!Heavy(spec.Part)) return;
        if (Modules.Working(w, FurnitureType.Hoist) > 0) { Stats.Hoisted++; return; }
        Stats.HeavyLifts++;
        // 맨손으로 무거운 부품: 곁에 거드는 사람이 없으면 허리를 삐끗하기도 한다
        bool helper = w.Crew.Any(o => o != c && o.CanAct && o.Room == m.Body.Room && (o.Position - c.Position).LengthSquared() < 9f);
        if (!helper && R.Chance(0.15f + 0.2f * (1f - c.Needs.Rest) + (c.Age >= 55f ? 0.1f : 0f)))
        {
            Stats.Strains++;
            NeedsSystem.AddInjury(c.Vitals, 0.06f, "무거운 부품을 들다 허리를 삐끗");
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{m.Name}의 {ItemKinds.Name(spec.Part!.Value)}을(를) 혼자 들다 허리를 삐끗했다");
            w.Log.Add(w.Tick, LogKind.Warning, $"{ItemKinds.Name(spec.Part!.Value)}을(를) 혼자 들다 허리를 삐끗했다", c.Id);
        }
    }

    /// <summary>화면용: 그 설비의 부품 한 줄씩.</summary>
    public IEnumerable<string> Lines(Machine m) =>
        Of(m).Select(p => $"{p.Spec.Name}: {(p.Age >= 1f ? "수명 넘김" : p.Age > 0.75f ? "수명 끝 무렵" : p.Age > 0.4f ? "반쯤 닳음" : "아직 새것")} ({p.Hours + p.Lot.PriorHours:0}/{p.Life:0}시간) · {p.Lot.OriginName}"
                          + (p.Failures > 0 ? $" · 고장 {p.Failures}번" : "") + (p.Root != null ? (p.RootKnown ? $" · 원인: {p.Root}" : " · 원인 모름") : ""));
}

/// <summary>시험대(없으면 작업대)에서 의심받거나 시험 안 한 부품을 돌려 본다 — 나쁜 것은 버리고, 쓸 것엔 "합격"을 붙인다.</summary>
public sealed class PartTestActivity : Activity
{
    public override string Id => "parttest";
    public override string Label => "부품 시험";

    private static ItemKind? Pending(World w) =>
        PartsSystem.TestKinds.FirstOrDefault(k => w.Parts.StockOf(k).Any(l => !l.Tested && (l.Suspect || l.Origin is PartOrigin.Salvage or PartOrigin.Handmade)));

    private static Furniture? Bench(World w, DistanceField dist) =>
        w.Ship.FurnitureOf(FurnitureType.PartTestBench).Concat(w.Ship.FurnitureOf(FurnitureType.Workbench))
            .Where(f => f.Machine is { Stopped: false } && !f.Room.Abandoned && f.UseSpots.Any(s => dist.Reachable(s)))
            .OrderBy(f => f.Type == FurnitureType.PartTestBench ? 0 : 1).FirstOrDefault();

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild || w.Movement.Hurry(w) || c.RawSkill(Skill.Mechanics) < 0.3f) return (0f, "—");
        if (Pending(w) is not ItemKind k) return (0f, "—");
        if (Bench(w, dist) is not Furniture bench) return (0f, "시험할 곳이 없다");
        var lots = w.Parts.StockOf(k);
        bool suspect = lots.Any(l => l.Suspect && !l.Tested);
        float s = (suspect ? 0.42f : 0.24f) + 0.15f * c.Traits.Diligence + (OnShift(c, w) ? 0.1f : -0.1f) + (bench.Type == FurnitureType.PartTestBench ? 0.05f : 0f);
        if (Bedtime(c, w)) s -= 0.3f;
        return (MathF.Max(0f, s), suspect ? $"같은 묶음이 일찍 나갔다 — 선반의 {ItemKinds.Name(k)}부터 돌려 본다" : $"시험 안 한 {ItemKinds.Name(k)} (중고·손으로 만든 것)");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Pending(w) is not ItemKind k || Bench(w, dist) is not Furniture bench) return null;
        var spot = bench.UseSpots.Where(s => dist.Reachable(s)).OrderBy(dist.Get).First();
        bool real = bench.Type == FurnitureType.PartTestBench;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WorkToil(real ? 0.4f : 0.8f, Skill.Mechanics, bench.Center)); // 시험대가 없으면 작업대에서 손으로 돌려 본다 (느리다)
        toils.Add(new DoToil((cm, world) =>
        {
            var (t, r) = world.Parts.Test(k, cm, real ? 4 : 2);
            if (t > 0)
                world.Log.Add(world.Tick, LogKind.Work, $"{ItemKinds.Name(k)} {t}개를 {(real ? "시험대" : "작업대")}에서 돌려 봤다" + (r > 0 ? $" — {r}개 불량, 버렸다" : " — 모두 쓸 만하다"), cm.Id);
            cm.Practice(Skill.Mechanics, 0.01f);
            return true;
        }));
        return new Job(this, "부품 시험", toils) { LogText = $"{ItemKinds.Name(k)}을(를) {(real ? "시험대" : "작업대")}에서 시험한다", LogKind = LogKind.Work };
    }
}
