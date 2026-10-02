using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.22 식량원 다양화 — 수경 채소 하나에 기대지 않는다.
//   재배대는 놓인 방에 따라 기르는 것이 다르다: 수경재배실 채소 · 조류 배양실 조류(빨리 자라고 조금씩) · 단백질 농장 배양 단백질(물통 따로)
//   · 버섯 재배실 버섯(어둡고 습한 방 · 본관이 끊겨도 제 물통) · 정원 허브(조금). 거둔 것은 모두 냉장고의 식재료가 되어 주방이 쓴다.
//   그 밖에 들어오는 길: 냉동 창고 · 창고의 저장 식량(비상식량) · 기항지 교역 · 원정 전리품 · 주방 항아리의 발효 음식.
//   들어온 양을 출처마다 적는다 (기록 = 행동: 실제로 거둔 · 먹은 · 실은 · 익은 만큼).
// 수경 재배가 멎으면 (재배대 절반 넘게 멎음 · 두 시간 넘게):
//   ① 주컴퓨터가 읽는다 — 다른 식량원으로 며칠 버티는지 셈하고 다섯 칸 기록에 남긴다 (다른 재배실 돌봄 · 저장 식량 순서 · 고칠 곳).
//   ② 재배 담당(과 손이 빈 사람)이 다른 재배실을 한 번 더 돌본다 — 배지를 뒤집고 물을 갈아 조금 더 빨리 자란다.
//   ③ 조리사는 남는 채소를 항아리에 앉혀(발효) 오래 두고, 저장 식량을 섞어 쓴다.

public enum FoodSrc { Hydro, Algae, Protein, Mushroom, Garden, Stored, Trade, Expedition, Ferment }
public enum CropKind { Veg, Algae, Protein, Mushroom, Herb }

public sealed class FoodSourceSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 1229));
    public FoodSourceSystem(World w) => _w = w;

    public const int Count = 9;
    /// <summary>출처마다 들어온 식량 (식재료 · 끼니 · 비상식량 개수).</summary>
    public int[] In { get; } = new int[Count];
    /// <summary>출처마다 거둔 · 받은 횟수.</summary>
    public int[] Times { get; } = new int[Count];
    /// <summary>수경 재배가 멎었다 (재배대 절반 넘게 · 두 시간 넘게).</summary>
    public bool HydroDown { get; private set; }
    public long HydroDownSince { get; private set; } = -1;
    /// <summary>주컴퓨터가 마지막으로 셈한 버틸 날 (다른 식량원 포함) · 그중 재배 몫.</summary>
    public float DaysLeft { get; private set; } = 99f;
    public float GrowPerDay { get; private set; }
    public int Advised, AltTends, Preserved, HydroOutages;
    /// <summary>주컴퓨터가 마지막으로 알린 때 (알림을 들은 사람은 더 서둘러 다른 재배실로 간다).</summary>
    public long AdvisedAt { get; private set; } = -1_000_000;
    private long _next, _downFrom = -1;

    // ─────────────────────────────── 재배대 종류 ───────────────────────────────

    public static CropKind Crop(Room r) => r.Kind switch
    {
        RoomType.AlgaeLab => CropKind.Algae,
        RoomType.ProteinFarm => CropKind.Protein,
        RoomType.MushroomFarm => CropKind.Mushroom,
        RoomType.Garden => CropKind.Herb,
        _ => CropKind.Veg,
    };
    public static CropKind Crop(Furniture bed) => Crop(bed.Room);

    public static FoodSrc Src(CropKind k) => k switch
    {
        CropKind.Algae => FoodSrc.Algae, CropKind.Protein => FoodSrc.Protein, CropKind.Mushroom => FoodSrc.Mushroom, CropKind.Herb => FoodSrc.Garden, _ => FoodSrc.Hydro,
    };

    /// <summary>자라는 빠르기 배율 (채소 60시간 = 1): 조류는 하루, 버섯은 이틀 남짓, 배양 단백질은 사십 시간.</summary>
    public static float GrowMul(Machine m) => Crop(m.Body.Room) switch
    {
        CropKind.Algae => 2.5f, CropKind.Mushroom => 1.4f, CropKind.Protein => 1.5f, CropKind.Herb => 1.2f, _ => 1f,
    };

    /// <summary>한 번에 거두는 양 배율: 조류는 조금씩 자주, 버섯 · 단백질 · 허브는 중간 (하루 몫은 채소와 비슷).</summary>
    public static float YieldMul(Furniture bed) => Crop(bed) switch
    {
        CropKind.Algae => 0.45f, CropKind.Mushroom => 0.72f, CropKind.Protein => 0.7f, CropKind.Herb => 0.7f, _ => 1f, // 하루 몫은 채소와 비슷하게 (자주 · 조금씩)
    };

    /// <summary>제 물통이 있어 급수 본관이 끊겨도 자라는 재배대 (버섯 배지 · 단백질 배양조).</summary>
    public static bool OwnWater(Machine m) => Crop(m.Body.Room) is CropKind.Mushroom or CropKind.Protein;

    public static string Noun(CropKind k) => k switch
    {
        CropKind.Algae => "조류 반죽", CropKind.Protein => "배양 단백질", CropKind.Mushroom => "버섯", CropKind.Herb => "허브", _ => "채소",
    };

    public static string Name(FoodSrc s) => s switch
    {
        FoodSrc.Hydro => "수경 채소", FoodSrc.Algae => "조류", FoodSrc.Protein => "배양 단백질", FoodSrc.Mushroom => "버섯", FoodSrc.Garden => "정원 허브",
        FoodSrc.Stored => "저장 식량", FoodSrc.Trade => "교역", FoodSrc.Expedition => "원정", _ => "발효",
    };

    /// <summary>수확 기록 한 줄 ("수경재배실 재배대 #2에서 채소 14개를 수확했다").</summary>
    public static string HarvestText(Furniture bed, int yield)
    {
        var k = Crop(bed);
        string unit = k switch { CropKind.Algae => "통", CropKind.Protein => "덩이", CropKind.Mushroom => "송이", CropKind.Herb => "줌", _ => "개" };
        string verb = k switch { CropKind.Algae => "떠냈다", CropKind.Protein => "건져 냈다", CropKind.Mushroom => "땄다", CropKind.Herb => "뜯었다", _ => "수확했다" };
        string where = k == CropKind.Veg ? bed.Label : $"{bed.Room.Name} {bed.Label}";
        return $"{where}에서 {Noun(k)} {yield}{unit}을 {verb}";
    }

    // ─────────────────────────────── 기록 ───────────────────────────────

    public void Note(FoodSrc s, int n)
    {
        if (n <= 0) return;
        In[(int)s] += n;
        Times[(int)s]++;
    }

    public void Harvested(Furniture bed, int yield) => Note(Src(Crop(bed)), yield);

    /// <summary>원정 전리품 중 먹을 것.</summary>
    public void Expedition(IEnumerable<KeyValuePair<ItemKind, int>> loot) =>
        Note(FoodSrc.Expedition, loot.Where(kv => kv.Key is ItemKind.Ration or ItemKind.Meal or ItemKind.Produce).Sum(kv => kv.Value));

    /// <summary>수경 채소 말고 들어온 몫 (0~1).</summary>
    public float OtherShare
    {
        get
        {
            int all = In.Sum();
            return all == 0 ? 0f : 1f - In[(int)FoodSrc.Hydro] / (float)all;
        }
    }

    /// <summary>항아리를 앉힐 확률 배율 — 수경이 멎으면 남는 채소를 절여 오래 둔다.</summary>
    public float JarMul => HydroDown ? 2.2f : 1f;

    public string Summary() => string.Join(" · ", Enumerable.Range(0, Count).Where(i => In[i] > 0).Select(i => $"{Name((FoodSrc)i)} {In[i]}"));

    // ─────────────────────────────── 한 시간마다 ───────────────────────────────

    /// <summary>수경재배실 재배대 중 자라지 못하는 것의 몫.</summary>
    public float HydroStalled()
    {
        int all = 0, bad = 0;
        foreach (var f in _w.Ship.FurnitureOf(FurnitureType.GrowBed))
        {
            if (f.Room.Detached || Crop(f) != CropKind.Veg || f.Machine?.Crop is not CropState crop) continue;
            all++;
            bool water = _w.Water.Level > 1f && (_w.Piping.WaterTo(f.Room) || crop.HandWateredHours > 0f);
            if (f.Machine.Efficiency <= 0f || !water || f.Room.Abandoned) bad++;
        }
        return all == 0 ? 0f : bad / (float)all;
    }

    /// <summary>다른 재배실 (조류 · 단백질 · 버섯 · 정원)의 재배대.</summary>
    public IEnumerable<Furniture> AltBeds => _w.Ship.FurnitureOf(FurnitureType.GrowBed).Where(f => !f.Room.Detached && !f.Room.Abandoned && Crop(f) != CropKind.Veg && f.Machine?.Crop != null);

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(30);
        float stalled = HydroStalled();
        if (stalled >= 0.5f)
        {
            if (_downFrom < 0) _downFrom = w.Tick;
            if (!HydroDown && w.Tick - _downFrom >= SimTime.Hours(2))
            {
                HydroDown = true;
                HydroDownSince = w.Tick;
                HydroOutages++;
                Advise(stalled);
            }
        }
        else
        {
            _downFrom = -1;
            if (HydroDown)
            {
                HydroDown = false;
                w.Log.Add(w.Tick, LogKind.Ship, "수경재배실 재배대가 다시 자란다 — 다른 재배실 돌봄은 평소대로");
            }
        }
        // 버틸 날: 지금 먹을 것 + 다른 재배실이 하루에 대 주는 몫
        int crew = w.Crew.Count(c => !c.Dead && !c.Away);
        GrowPerDay = AltPerDay() + (HydroDown ? 0f : HydroPerDay());
        float need = MathF.Max(1f, crew * FoodPolicy.MealsPerPersonDay);
        float stock = FoodPolicy.FoodStock(w);
        DaysLeft = GrowPerDay >= need ? 99f : stock / MathF.Max(0.5f, need - GrowPerDay);
        if (HydroDown && w.Tick - HydroDownSince > SimTime.Hours(20) && w.Tick - HydroDownSince < SimTime.Hours(21)) Advise(stalled); // 하루쯤 뒤 다시 셈
    }

    private float PerDay(Func<Furniture, bool> which) =>
        _w.Ship.FurnitureOf(FurnitureType.GrowBed).Where(f => !f.Room.Abandoned && !f.Room.Detached && f.Machine!.Efficiency > 0f && f.Machine.Crop != null && which(f))
            .Sum(f => FoodChain.HarvestYield * FoodChain.BedSize(f) * YieldMul(f) * GrowMul(f.Machine!) * f.Machine!.Efficiency * f.Machine.Rating * 24f / FoodChain.GrowHours)
        * (FoodChain.MealsPerBatch / (float)FoodChain.ProducePerBatch);
    public float AltPerDay() => PerDay(f => Crop(f) != CropKind.Veg);
    public float HydroPerDay() => PerDay(f => Crop(f) == CropKind.Veg);

    /// <summary>주컴퓨터: 수경이 멎었을 때 다른 식량원으로 며칠 버티는지 셈해 다섯 칸 기록에 남긴다.</summary>
    private void Advise(float stalled)
    {
        var w = _w;
        var hydro = w.Ship.RoomsOf(RoomType.Hydroponics).FirstOrDefault(r => r.Kind == RoomType.Hydroponics);
        int crew = w.Crew.Count(c => !c.Dead && !c.Away);
        float alt = AltPerDay();
        float need = MathF.Max(1f, crew * FoodPolicy.MealsPerPersonDay);
        float stock = FoodPolicy.FoodStock(w);
        int rations = w.Ship.CountStored(ItemKind.Ration);
        float days = alt >= need ? 99f : stock / MathF.Max(0.5f, need - alt);
        DaysLeft = days;
        var alts = AltBeds.GroupBy(f => f.Room.Kind).OrderBy(g => (int)g.Key).Select(g => $"{RoomTypes.Name(g.Key)} {g.Count()}대").ToList();
        string judge = (alts.Count == 0 ? "다른 재배실이 없다" : $"다른 재배실 {string.Join(" · ", alts)} — 하루 {alt:0}끼")
                       + $" · 저장 식량 {rations}개 · 먹을 것 {stock:0}끼 → " + (days >= 60f ? "모자라지 않는다" : $"{days:0.0}일 버틴다");
        string act = alts.Count > 0 ? "다른 재배실 수확을 앞당기고 남는 채소는 절여 둔다 · 저장 식량은 냉동 창고 것부터" : "저장 식량으로 버틴다 · 배급을 준비한다";
        string ask = hydro != null ? $"{hydro.Name} 재배대 · 급수 본관을 봐 달라" : "재배대를 봐 달라";
        var a = w.Automation.Book.Add(ActKind.Advice, hydro, $"수경 재배대 {stalled * 100:0}%가 자라지 않는다", judge, act, ask, "food.hydro", SimTime.Hours(6), 120f);
        if (a == null) return; // 컴퓨터가 멎었으면 사람이 알아서 (재배 담당은 그래도 다른 재배실을 돌본다)
        Advised++;
        AdvisedAt = w.Tick;
        w.Log.Add(w.Tick, LogKind.Ship, $"주컴퓨터: 수경 재배가 멎었다 — {judge}");
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var n in In) I(n);
        I(HydroDown ? 1 : 0); I(Advised); I(AltTends); I(Preserved); F(DaysLeft);
    }

    // ─────────────────────────────── 승무원: 다른 재배실 돌보기 ───────────────────────────────

    /// <summary>지금 돌볼 만한 다른 재배실 재배대 (수경이 멎었을 때만 · 익지 않았고 · 이 시간 안에 손대지 않은 것).</summary>
    public Furniture? AltBedFor(CrewMember c, DistanceField dist)
    {
        if (!HydroDown) return null;
        Furniture? best = null;
        float bd = float.MaxValue;
        foreach (var f in AltBeds)
        {
            var crop = f.Machine!.Crop!;
            if (crop.Ripe || f.Machine.Efficiency <= 0f || f.Room.OffLimits || f.Room.Leaking) continue;
            if (_tended.TryGetValue(f.Id, out var t) && _w.Tick - t < SimTime.Hours(5)) continue;
            if (f.UseSpots.Count == 0 || !dist.Reachable(f.UseSpots[0])) continue;
            float d = dist.Get(f.UseSpots[0]);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    private readonly Dictionary<int, long> _tended = new();

    public void Tended(CrewMember cm, Furniture bed)
    {
        _tended[bed.Id] = _w.Tick;
        if (bed.Machine?.Crop is not CropState crop) return;
        crop.Care = 1f;
        float skill = cm.SkillLevel(Skill.Botany);
        crop.Growth = MathF.Min(1f, crop.Growth + 0.04f + 0.05f * skill);
        AltTends++;
        cm.Practice(Skill.Botany, 0.02f);
        string what = Crop(bed) switch
        {
            CropKind.Algae => "조류 배양관 물을 갈고 빛을 맞췄다",
            CropKind.Mushroom => "버섯 배지 봉지를 뒤집고 습기를 맞췄다",
            CropKind.Protein => "배양조 양분을 더 넣고 온도를 맞췄다",
            _ => "허브 화분에 물을 줬다",
        };
        _w.Log.Add(_w.Tick, LogKind.Work, $"{bed.Room.Name}: {what} — 수경 재배가 멎은 동안", cm.Id);
    }
}

/// <summary>수경 재배가 멎은 동안 재배 담당 · 손이 빈 사람이 다른 재배실(조류 · 버섯 · 단백질 · 정원)을 한 번 더 돌본다.</summary>
public sealed class AltCropActivity : Activity
{
    public override string Id => "altcrop";
    public override string Label => "다른 재배실 돌보기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var fs = w.FoodSources;
        if (!fs.HydroDown || c.IsChild || c.Away) return (0f, "—");
        if (fs.AltBedFor(c, dist) is not Furniture bed) return (0f, "—");
        float s = c.Role == CrewRole.Botanist ? 0.62f : 0.18f + 0.3f * c.SkillLevel(Skill.Botany);
        bool told = w.Tick - fs.AdvisedAt < SimTime.Hours(12); // 주컴퓨터가 셈해 알린 뒤엔 다들 조금 더 서두른다
        if (told) s += 0.1f;
        if (Bedtime(c, w)) s -= 0.3f;
        return (MathF.Max(0f, s), $"수경 재배가 멎었다 — {bed.Room.Name}을 한 번 더 돌본다" + (told ? " (주컴퓨터가 버틸 날을 셈해 알렸다)" : ""));
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.FoodSources.AltBedFor(c, dist) is not Furniture bed) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(bed.UseSpots[0]));
        toils.Add(new WorkToil(0.3f, Skill.Botany, bed.Center));
        toils.Add(new DoToil((cm, world) => { world.FoodSources.Tended(cm, bed); return true; }));
        return new Job(this, "다른 재배실 돌보기", toils)
        {
            TargetRoom = bed.Room, Target = bed, LogText = $"{bed.Room.Name}을 돌보러 간다 (수경 재배가 멎었다)", LogKind = LogKind.Work,
        }.Reserve(bed, c);
    }
}
