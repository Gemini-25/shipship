using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v14.7 오염이 사람과 함께 옮겨 다닌다.
// 정비하면 손과 옷에 기름이, 불을 끄면 그을음이, 선외 작업을 하면 우주복에 분진이, 아픈 사람의 손에는 균이 묻는다.
// 손으로 만지는 곳(방)에 떨어지고 거기서 다른 손으로 옮는다. 조리·치료 전에는 손을 씻고, 일이 끝나면 씻고 옷을 갈아입는다.
// 더러운 옷은 세탁실로, 선외에서 들어오면 우주복을 털어 낸다. 물이 모자라면 손만 씻고 빨래를 미룬다.
// 씻지 않은 손으로 만든 식사에 균이 들고, 더러운 손으로 치료한 상처가 곪는다 — 누가 만들었는지 남는다.

public enum SoilKind { Oil, Dust, Soot, Bio }

/// <summary>한 사람의 손과 옷 (종류별 0~1) · 우주복에 묻은 분진.</summary>
public sealed class Soil
{
    public const int Kinds = 4;
    public readonly float[] Hands = new float[Kinds];
    public readonly float[] Clothes = new float[Kinds];
    public float SuitDust { get; set; }
    public long WashedAt { get; set; } = -100000;
    public long ChangedAt { get; set; } = -100000;

    public float HandsMax => Max(Hands);
    public float ClothesMax => Max(Clothes);
    private static float Max(float[] a) { float m = 0f; foreach (var v in a) if (v > m) m = v; return m; }

    public static string Name(SoilKind k) => k switch { SoilKind.Oil => "기름", SoilKind.Dust => "분진", SoilKind.Soot => "그을음", _ => "균" };

    /// <summary>화면용: "손에 기름 · 옷에 그을음".</summary>
    public string? Line()
    {
        var parts = new List<string>(3);
        int h = Argmax(Hands), c = Argmax(Clothes);
        if (Hands[h] > 0.25f) parts.Add($"손에 {Name((SoilKind)h)}");
        if (Clothes[c] > 0.3f) parts.Add($"옷에 {Name((SoilKind)c)}");
        if (SuitDust > 0.2f) parts.Add("우주복에 분진");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static int Argmax(float[] a) { int i = 0; for (int k = 1; k < a.Length; k++) if (a[k] > a[i]) i = k; return i; }
}

public sealed class SoilStats
{
    public int HandWashes, SkippedWashes, PartialWashes, Changes, LaundryRuns, LaundryDeferred, Decons, SkippedDecons, TaintedMeals, WoundInfections, ContactCatches;
    public int NoSoap, NoDetergent; // v15
    public float WaterUsed;
    public string Summary() =>
        $"손 씻기 {HandWashes}(급해서 건너뜀 {SkippedWashes} · 물이 모자라 손만 {PartialWashes}) · 옷 갈아입기 {Changes} · 빨래 {LaundryRuns}(미룸 {LaundryDeferred}) · 우주복 털기 {Decons}(그냥 들어옴 {SkippedDecons})"
        + $" · 균 든 식사 {TaintedMeals} · 곪은 상처 {WoundInfections} · 손으로 옮은 병 {ContactCatches} · 쓴 물 {WaterUsed:0}L";
}

public sealed class SoilSystem
{
    private int _soapUses; private bool _noSoap; // v15 비누
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 5113 + 73));
    private readonly Dictionary<int, float[]> _room = new();
    private readonly HashSet<int> _wasOutside = new();
    private readonly HashSet<int> _tracking = new();
    /// <summary>세탁 바구니에 쌓인 옷 (벌).</summary>
    public int LaundryLoad { get; private set; }
    public SoilStats Stats { get; } = new();

    public SoilSystem(World w) => _w = w;

    public float[] RoomSoil(Room r)
    {
        if (!_room.TryGetValue(r.Id, out var a)) _room[r.Id] = a = new float[Soil.Kinds];
        return a;
    }

    /// <summary>깨끗해야 하는 방 (먹을 것 · 상처 · 작물).</summary>
    public static bool CleanRoom(Room r) => r.Type is RoomType.Galley or RoomType.Medbay or RoomType.Hydroponics or RoomType.Mess;

    public void Update(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            var s = c.Soil;
            // 선외: 우주복에 분진이 앉는다 · 들어오면 털어 낼 때까지 옷과 방으로 번진다
            if (c.Outside) { s.SuitDust = MathF.Min(1f, s.SuitDust + 0.4f * dt); _wasOutside.Add(c.Id); continue; }
            _wasOutside.Remove(c.Id);
            if (s.SuitDust < 0.05f) _tracking.Remove(c.Id);
            if (c.Room is not Room room) continue;
            // 일이 손과 옷을 더럽힌다
            if (c.Pose == Pose.Working && c.Job?.Order is WorkOrder o)
            {
                switch (o.Kind)
                {
                    case WorkKind.Repair or WorkKind.Maintain or WorkKind.PreventiveCheck or WorkKind.Upgrade or WorkKind.Fabricate or WorkKind.Cannibalize:
                        Add(s.Hands, SoilKind.Oil, 0.6f * dt); Add(s.Clothes, SoilKind.Oil, 0.15f * dt); break;
                    case WorkKind.Extinguish:
                        Add(s.Hands, SoilKind.Soot, 0.4f * dt); Add(s.Clothes, SoilKind.Soot, 0.6f * dt); break;
                    case WorkKind.Treat or WorkKind.Rescue:
                        Add(s.Hands, SoilKind.Bio, 0.5f * dt); break;
                }
            }
            if (room.Air.Smoke > 0.3f) Add(s.Clothes, SoilKind.Soot, 0.3f * room.Air.Smoke * dt);
            // 들어오며 털지 않은 우주복: 옷과 방으로
            if (s.SuitDust > 0.05f && c.Suit == null && room.Type is not (RoomType.Airlock or RoomType.EvaPrep or RoomType.Decon))
            {
                if (_tracking.Add(c.Id)) Stats.SkippedDecons++; // 털지 않고 들어왔다
                float move = s.SuitDust * 0.4f * dt * 4f;
                s.SuitDust -= move;
                Add(s.Clothes, SoilKind.Dust, move * 0.6f);
                RoomSoil(room)[(int)SoilKind.Dust] = MathF.Min(1f, RoomSoil(room)[(int)SoilKind.Dust] + move * 0.4f * 10f / MathF.Max(4, room.Cells.Count));
            }
            // 옮는 병을 앓는 사람의 손에 균
            foreach (var a in c.Ailments)
                if (AilmentSystem.Spec(a.Id).Spread > 0f && w.Ailments.Severity(a) > 0.2f) Add(s.Hands, SoilKind.Bio, 0.25f * w.Ailments.Severity(a) * dt);
            // 손 ↔ 방: 만지는 곳에 떨어뜨리고, 거기서 묻힌다
            var rs = RoomSoil(room);
            float scale = 10f / MathF.Max(4, room.Cells.Count);
            for (int k = 0; k < Soil.Kinds; k++)
            {
                float drop = (s.Hands[k] * 0.06f + s.Clothes[k] * 0.02f) * dt;
                rs[k] = MathF.Min(1f, rs[k] + drop * scale);
                s.Hands[k] = MathF.Min(1f, s.Hands[k] + rs[k] * 0.08f * dt);
                s.Hands[k] = MathF.Max(0f, s.Hands[k] - 0.02f * dt);
                s.Clothes[k] = MathF.Max(0f, s.Clothes[k] - 0.003f * dt);
            }
        }
        // 방은 천천히 옅어진다 (환기 · 닦기)
        foreach (var a in _room.Values) for (int k = 0; k < Soil.Kinds; k++) a[k] = MathF.Max(0f, a[k] - 0.01f * dt);
    }

    private static void Add(float[] a, SoilKind k, float v) => a[(int)k] = Math.Clamp(a[(int)k] + v, 0f, 1f);

    // ───────────────────────────── 씻기 · 갈아입기 · 빨래 · 털기 ─────────────────────────────

    /// <summary>물을 아끼는 중인가 (방침 · 물탱크).</summary>
    public bool WaterShort => _w.Policies["water"] >= 1 || _w.Water.Level < _w.Water.Capacity * 0.2f;

    /// <summary>조리·치료 앞에 손 씻기 (급하면 건너뛰고 — 그게 남는다).</summary>
    public List<Toil> WashFirst(CrewMember c, bool urgent, string forWhat)
    {
        var toils = new List<Toil>();
        bool strict = _w.Culture.Follows(c, CustomKind.HandWash); // v14.9 탈이 났던 배는 손이 깨끗해 보여도 씻는다
        if (c.Soil.HandsMax < (strict ? 0.03f : 0.15f)) return toils;
        if (strict && c.Soil.HandsMax < 0.15f) _w.Culture.Stats.StrictWashes++;
        if (urgent)
        {
            toils.Add(new DoToil((cm, world) =>
            {
                world.Soil.Stats.SkippedWashes++;
                MarkLog.Add(cm.Memory.Marks, world.Tick, $"급해서 손을 못 씻고 {forWhat}");
                return true;
            }));
            return toils;
        }
        toils.Add(new WaitToil(SimTime.Minutes(WaterShort ? 1 : 2), Pose.Standing));
        toils.Add(new DoToil((cm, world) => { world.Soil.WashHands(cm, $"{forWhat} 전에"); return true; }));
        return toils;
    }

    public void WashHands(CrewMember c, string why)
    {
        var w = _w;
        bool thrift = !WaterShort && w.Culture.Follows(c, CustomKind.WaterThrift); // v14.9 물이 바닥났던 배는 넉넉해도 아낀다
        // v15 비누: 여덟 번 씻으면 하나 — 없으면 물로만 (덜 씻긴다)
        bool soap = true;
        if ((_soapUses += 1) >= 8) { _soapUses = 0; soap = ItemsV15.Use(w, ItemKind.Soap); if (!soap) { Stats.NoSoap++; _noSoap = true; } else _noSoap = false; }
        else soap = !_noSoap;
        bool partial = WaterShort || thrift;
        if (thrift) w.Culture.Stats.ThriftWashes++;
        float use = partial ? 0.3f : 1f;
        w.Water.Level = MathF.Max(0f, w.Water.Level - use);
        Stats.WaterUsed += use;
        for (int k = 0; k < Soil.Kinds; k++) c.Soil.Hands[k] *= partial ? 0.35f : soap ? 0.08f : 0.25f;
        c.Soil.WashedAt = w.Tick;
        Stats.HandWashes++;
        if (partial) Stats.PartialWashes++;
    }

    /// <summary>씻고 옷을 갈아입는다 (벗은 옷은 세탁 바구니로).</summary>
    public void WashUp(CrewMember c)
    {
        var w = _w;
        WashHands(c, "일 끝나고");
        if (c.Soil.ClothesMax > 0.3f)
        {
            for (int k = 0; k < Soil.Kinds; k++) c.Soil.Clothes[k] = 0f;
            c.Soil.ChangedAt = w.Tick;
            LaundryLoad++;
            Stats.Changes++;
        }
    }

    /// <summary>빨래를 돌린다 (물이 모자라면 미룬다).</summary>
    public bool Laundry(CrewMember c)
    {
        var w = _w;
        if (LaundryLoad == 0) return false;
        if (WaterShort && LaundryLoad < 10) { Stats.LaundryDeferred++; return false; }
        float use = 4f * LaundryLoad;
        w.Water.Level = MathF.Max(0f, w.Water.Level - use);
        Stats.WaterUsed += use;
        Stats.LaundryRuns++;
        if (!ItemsV15.Use(w, ItemKind.Detergent)) Stats.NoDetergent++; // v15 세제가 없으면 물로만 (옷이 덜 깨끗해진다)
        w.Log.Add(w.Tick, LogKind.Life, $"빨래를 돌렸다 ({LaundryLoad}벌 · 물 {use:0}L)", c.Id);
        LaundryLoad = 0;
        return true;
    }

    /// <summary>우주복을 털어 낸다 (선외 작업 뒤).</summary>
    public void Decon(CrewMember c)
    {
        c.Soil.SuitDust = 0f;
        c.Soil.Clothes[(int)SoilKind.Dust] *= 0.5f;
        Stats.Decons++;
    }

    // ───────────────────────────── 결과 ─────────────────────────────

    /// <summary>조리를 마쳤다: 손이 더러웠으면 몇 끼에 균이 든다 (들고 가는 식사에 — 보관함에 넣으면 거기 남는다).</summary>
    public void OnCooked(CrewMember cook, int meals)
    {
        var w = _w;
        float dirty = cook.Soil.Hands[(int)SoilKind.Bio] + 0.5f * cook.Soil.Hands[(int)SoilKind.Oil];
        if (meals <= 0 || dirty < 0.3f) return;
        if (!R.Chance(Math.Clamp(dirty, 0f, 0.9f))) return;
        int n = Math.Max(1, (int)MathF.Round(meals * Math.Clamp(dirty * 0.5f, 0.1f, 0.6f)));
        cook.CarryTaint += n;
        Stats.TaintedMeals += n;
        MarkLog.Add(cook.Memory.Marks, w.Tick, "손을 제대로 못 씻고 조리했다");
    }

    /// <summary>치료를 마쳤다: 더러운 손으로 상처를 만지면 곪을 수 있다.</summary>
    public void OnTreated(CrewMember medic, CrewMember patient)
    {
        var w = _w;
        float dirty = medic.Soil.Hands[(int)SoilKind.Bio] + medic.Soil.Hands[(int)SoilKind.Oil] * 0.6f + medic.Soil.Hands[(int)SoilKind.Soot] * 0.4f;
        if (patient.Vitals.Injury < 0.15f || dirty < 0.35f || w.Ailments.Has(patient, "woundinf")) return;
        if (!R.Chance(0.25f * dirty)) return;
        w.Ailments.Catch(patient, "woundinf", null, $"{Ko.IGa(medic.Name)} 손을 못 씻고 상처를 만졌다");
        Stats.WoundInfections++;
        MarkLog.Add(medic.Memory.Marks, w.Tick, $"손을 못 씻고 {Ko.EulReul(patient.Name)} 치료했는데 상처가 곪았다");
    }
}

/// <summary>씻을 곳: 세탁실 · 제염실 · 침실 · 주방 · 의무실 중 가까운 곳의 빈 바닥.</summary>
internal static class Washrooms
{
    public static Cell? Spot(CrewMember c, World w, DistanceField dist, params RoomType[] types)
    {
        Cell? best = null;
        int bestD = int.MaxValue;
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Detached || room.Abandoned || room.OffLimits || Array.IndexOf(types, room.Type) < 0) continue;
            foreach (var cell in room.Cells)
            {
                if (!w.Ship.IsOpenFloor(cell)) continue;
                int d = dist.Get(cell);
                if (d < 0 || d >= bestD) continue;
                best = cell; bestD = d;
            }
        }
        return best;
    }
}

/// <summary>더러워진 손을 씻고 옷을 갈아입는다 (벗은 옷은 세탁 바구니로).</summary>
public sealed class WashUpActivity : Activity
{
    public override string Id => "washup";
    public override string Label => "씻기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var s = c.Soil;
        float dirt = MathF.Max(s.HandsMax, s.ClothesMax * 0.9f);
        if (dirt < 0.45f || c.Down || Crisis.Acting(w)) return (0f, "—");
        float score = 0.18f + 0.45f * (dirt - 0.4f) + (Life.Has(c, Habit.NeatFreak) ? 0.15f : 0f) - (Life.Has(c, Habit.Messy) ? 0.12f : 0f);
        if (Bedtime(c, w)) score += 0.1f; // 자기 전에 씻는다
        return (MathF.Max(0f, score), s.Line() ?? "더럽다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Washrooms.Spot(c, w, dist, RoomType.Laundry, RoomType.Decon, RoomType.Quarters, RoomType.Galley, RoomType.Medbay) is not Cell spot) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(c.Soil.ClothesMax > 0.3f ? 8 : 4), Pose.Standing));
        toils.Add(new DoToil((cm, world) => { world.Soil.WashUp(cm); return true; }));
        return new Job(this, "씻기", toils) { LogText = c.Soil.ClothesMax > 0.3f ? "씻고 옷을 갈아입는다" : "손을 씻는다", LogKind = LogKind.Life };
    }
}

/// <summary>세탁 바구니가 차면 빨래를 돌린다 (물이 모자라면 미룬다).</summary>
public sealed class LaundryActivity : Activity
{
    public override string Id => "laundry";
    public override string Label => "빨래";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        int load = w.Soil.LaundryLoad;
        if (load < 4 || c.IsChild || Crisis.Acting(w)) return (0f, "—");
        if (w.Soil.WaterShort && load < 10) return (0f, "물을 아끼느라 빨래를 미룬다");
        float s = 0.15f + 0.03f * load + (OnShift(c, w) ? 0f : 0.05f) + (Life.Has(c, Habit.NeatFreak) ? 0.1f : 0f);
        if (Bedtime(c, w)) s -= 0.3f;
        return (MathF.Max(0f, s), $"세탁 바구니 {load}벌");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Washrooms.Spot(c, w, dist, RoomType.Laundry, RoomType.Galley, RoomType.Quarters) is not Cell spot) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(30), Pose.Working));
        toils.Add(new DoToil((cm, world) => { world.Soil.Laundry(cm); return true; }));
        return new Job(this, "빨래", toils) { LogText = $"빨래를 돌린다 ({w.Soil.LaundryLoad}벌)", LogKind = LogKind.Life };
    }
}

/// <summary>선외 작업 뒤 우주복의 분진을 털어 낸다 (털지 않으면 안으로 번진다).</summary>
public sealed class DeconActivity : Activity
{
    public override string Id => "decon";
    public override string Label => "우주복 털기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Outside || c.Soil.SuitDust < 0.2f || c.Down) return (0f, "—");
        return (0.55f + 0.2f * c.Soil.SuitDust, "선외에서 묻은 분진 — 들어가기 전에 턴다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Washrooms.Spot(c, w, dist, RoomType.Decon, RoomType.EvaPrep, RoomType.Airlock) is not Cell spot) return null;
        var toils = new List<Toil> { new GotoToil(spot), new WaitToil(SimTime.Minutes(5), Pose.Working) };
        toils.Add(new DoToil((cm, world) => { world.Soil.Decon(cm); return true; }));
        return new Job(this, "우주복 털기", toils) { LogText = "우주복의 분진을 털어 낸다", LogKind = LogKind.Work };
    }
}
