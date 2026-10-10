using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.7 이야기꾼 성격 넷 더 — 고르는 규칙 · 간격 · 크기 · 기다림이 저마다 다르다.
//  느린 불씨형 — 처음엔 작은 고장만 띄엄띄엄, 사고를 낼수록 달아올라 잦고 크고 불 쪽으로 번진다.
//  계절형     — 철(운석철 · 고장철 · 불철 · 역병철)이 돈다. 그 철의 갈래만 몰아 오고, 철이 바뀔 무렵엔 조용하다.
//  자비형     — 다 추스르고 쓰러진 사람이 없을 때만, 막을 물자가 있는 갈래만, 큰 것은 거의 없이. 다친 사람이 있으면 더 쉰다.
//  앙갚음형   — 잘 버틴 배일수록 빨리·크게 되갚는다. 방금 막아 낸 갈래를 다시, 개조했거나 버텨 낸 방을 노린다.
// 난수는 쓰지 않는다 (원래 이야기꾼의 난수 하나로 고른다) → 같은 시드면 같은 이야기.

public sealed partial class Storyteller
{
    /// <summary>사고의 갈래 (계절형의 철 · 자비형의 물자 · 앙갚음형의 되갚기).</summary>
    public const int FamSpace = 0, FamMachine = 1, FamBurn = 2, FamLife = 3;
    public static string FamilyName(int f) => f switch { FamSpace => "바깥", FamMachine => "고장", FamBurn => "불·공기", _ => "생물·사람" };
    public static string SeasonName(int s) => s switch { FamSpace => "운석철", FamMachine => "고장철", FamBurn => "불철", _ => "역병철" };

    /// <summary>계절형의 한 철 (일): 사고 간격의 세 배.</summary>
    public static float SeasonDays => 3f * GapDays;

    /// <summary>마지막으로 낸 사고의 열쇠.</summary>
    public string LastKey { get; internal set; } = "";
    /// <summary>낸 사고 (틱 · 열쇠 · 그때의 긴장) — 성격마다 흐름이 어떻게 다른지 본다.</summary>
    public List<(long tick, string key, float tension)> Picks { get; } = new();

    private void Note(string key, float tension)
    {
        LastKey = key;
        Picks.Add((_w.Tick, key, tension));
        if (Picks.Count > 200) Picks.RemoveAt(0);
    }

    /// <summary>느린 불씨가 달아오른 정도 0~1 (낸 사고 여덟이면 다 달아오른다).</summary>
    public float Heat => MathF.Min(1f, Fired / 8f);

    /// <summary>지금 철 (0 운석철 · 1 고장철 · 2 불철 · 3 역병철) — 시드마다 다른 철에서 시작한다.</summary>
    public int Season => (int)((Into / SeasonTicks + (uint)_w.Seed % 4u) % 4);
    /// <summary>이번 철이 얼마나 지났나 0~1.</summary>
    public float SeasonPhase => Into % SeasonTicks / (float)SeasonTicks;
    private long Into => Math.Max(0L, _w.Tick - _w.StartTickOf);
    private static long SeasonTicks => Math.Max(SimTime.TicksPerDay, (long)(SeasonDays * SimTime.TicksPerDay));

    // ─────────────────────────────── 갈래 ───────────────────────────────

    public static int Family(string key)
    {
        switch (key)
        {
            case "meteor": case "bigmeteor": return FamSpace;
            case "fire": return FamBurn;
            case "break": case "pipe": return FamMachine;
        }
        if (!Enum.TryParse<HazardKind>(key, out var k)) return FamMachine;
        return k switch
        {
            HazardKind.MeteorShower or HazardKind.SolarStorm or HazardKind.HullCrack or HazardKind.DebrisCloud or HazardKind.MicroShower
                or HazardKind.WeldFatigue or HazardKind.WindowCrack or HazardKind.HatchSeal or HazardKind.ThermalStress or HazardKind.FrameCreak
                or HazardKind.RadiationBurst or HazardKind.IonStorm or HazardKind.DebrisAlert or HazardKind.CometTail or HazardKind.StaticDischarge => FamSpace,
            HazardKind.GasLeak or HazardKind.OxygenLeak or HazardKind.Overheat or HazardKind.DuctFire or HazardKind.HydrogenBuildup or HazardKind.GasTankRupture
                or HazardKind.GreaseFire or HazardKind.DryerFire or HazardKind.CableTrayFire or HazardKind.Smolder
                or HazardKind.Co2Spike or HazardKind.ScrubberSaturation or HazardKind.InsulationSmoke or HazardKind.SealLeak or HazardKind.Ozone => FamBurn,
            HazardKind.CropBlight or HazardKind.FoodPoisoning or HazardKind.WorkAccident or HazardKind.WaterContamination or HazardKind.Epidemic
                or HazardKind.Backflow or HazardKind.SewageBackup or HazardKind.HumiditySpike or HazardKind.CondensateFlood or HazardKind.HoistDrop or HazardKind.BadBatch
                or HazardKind.MoldOutbreak or HazardKind.SeedRot or HazardKind.NutrientCrash or HazardKind.SkinFungus or HazardKind.PanicAttack or HazardKind.MedError => FamLife,
            _ => FamMachine,
        };
    }

    /// <summary>큰 사고 (배 전체가 흔들리는 것).</summary>
    public static bool Big(string key) => key is "bigmeteor"
        || Enum.TryParse<HazardKind>(key, out var k) && k is HazardKind.CoolantLoss or HazardKind.Epidemic or HazardKind.GasTankRupture or HazardKind.DebrisCloud
            or HazardKind.MeteorShower or HazardKind.ReactorTransient or HazardKind.HydrogenBuildup or HazardKind.CometTail or HazardKind.RadiationBurst
            or HazardKind.BreakerCascade or HazardKind.MoldOutbreak;

    // ─────────────────────────────── 언제 ───────────────────────────────

    /// <summary>기다릴까 (참이면 한 시간 뒤 다시 본다).</summary>
    private bool Hold(StoryPersona p, float t, float cap) => p switch
    {
        StoryPersona.SlowBurn => t > 0.25f + 0.25f * Heat,                       // 처음엔 다 추스를 때까지, 달아오를수록 덜 기다린다
        StoryPersona.Seasonal => t > 0.35f,
        StoryPersona.Merciful => t > 0.05f || cap < (Level >= 4 ? 0.4f : 0.5f)
                                 || _w.Crew.Any(c => !c.Dead && (c.Down || c.CareBed != null)),  // 쓰러진 사람이 있으면 기다린다
        StoryPersona.Vengeful => t > (Level >= 4 ? 0.8f : 0.55f),                 // 웬만해선 기다리지 않는다
        _ => false,
    };

    /// <summary>다음 사고까지의 간격 배율 (원래 성격은 1).</summary>
    private float GapMul(StoryPersona p, float cap) => p switch
    {
        StoryPersona.SlowBurn => 2.2f - 1.6f * Heat,                                           // 띄엄띄엄 → 잦게
        StoryPersona.Seasonal => SeasonPhase < 0.2f ? 1.6f : 0.55f + 0.6f * MathF.Abs(SeasonPhase - 0.6f), // 철이 바뀔 무렵 조용, 한철 한가운데 잦게
        StoryPersona.Merciful => 1.2f + 0.8f * (1f - cap) + Wounded(),                         // 약할수록 · 다친 사람이 많을수록 오래 쉰다
        StoryPersona.Vengeful => 1.4f - 0.9f * cap,                                            // 잘 추스른 배일수록 빨리
        _ => 1f,
    };

    private float Wounded()
    {
        int alive = Math.Max(1, _w.Crew.Count(c => !c.Dead));
        return _w.Crew.Count(c => !c.Dead && c.Vitals.Wounds.Count > 0) / (float)alive;
    }

    // ─────────────────────────────── 무엇을 ───────────────────────────────

    /// <summary>성격마다 고르는 규칙: 사고 무게를 갈래·크기에 따라 바꾼다.</summary>
    private void Shape(StoryPersona p, List<(string key, float weight)> pool, float cap)
    {
        float heat = Heat;
        int season = Season, last = LastKey.Length > 0 ? Family(LastKey) : -1;
        float[]? ready = p == StoryPersona.Merciful ? Readiness() : null;
        for (int i = 0; i < pool.Count; i++)
        {
            var (key, wt) = pool[i];
            int fam = Family(key);
            bool big = Big(key);
            wt *= p switch
            {
                // 작은 고장부터 → 불씨가 번지고 → 큰 것
                StoryPersona.SlowBurn => big ? 0.05f + 1.6f * heat * heat : fam == FamMachine ? 1.8f - heat : fam == FamBurn ? 0.4f + 1.6f * heat : 1f,
                // 그 철의 갈래만
                StoryPersona.Seasonal => fam == season ? 5f : 0.2f,
                // 막을 물자가 있는 갈래만, 큰 것은 거의 없이
                StoryPersona.Merciful => (big ? 0.05f * cap : 1f) * ready![fam],
                // 방금 막아 낸 갈래를 다시 · 같은 것이면 더 · 잘 버틴 배엔 큰 것
                StoryPersona.Vengeful => (fam == last ? 8f : 1f) * (key == LastKey ? 2f : 1f) * (big ? 0.4f + 1.6f * cap : 1f), // 통합8 사고 목록이 늘어 같은 갈래 몫이 엷어졌다 (×5로는 반에 걸쳤다)
                _ => 1f,
            };
            pool[i] = (key, wt);
        }
    }

    /// <summary>자비형: 갈래마다 막아 낼 물자가 있나 (있으면 1, 모자라면 작게).</summary>
    private float[] Readiness()
    {
        var ship = _w.Ship;
        int parts = new[] { ItemKind.Pump, ItemKind.Bearing, ItemKind.Cable, ItemKind.Filter, ItemKind.PowerController }.Sum(ship.CountStored);
        bool medbay = ship.RoomsOf(RoomType.Medbay).Any(r => !r.Detached);
        return new[]
        {
            ship.CountStored(ItemKind.Sealant) >= 6 && ship.CountStored(ItemKind.Plate) >= 4 ? 1f : 0.25f, // 바깥: 실링폼·금속판
            parts >= 5 ? 1f : 0.3f,                                                                      // 고장: 예비 부품
            ship.CountStored(ItemKind.Extinguisher) >= 2 ? 1f : 0.2f,                                   // 불: 소화기
            medbay && ship.CountStored(ItemKind.MedKit) >= 2 ? 1f : 0.3f,                               // 생물·사람: 의무실·구급 키트
        };
    }

    /// <summary>노릴 방 (앙갚음형만).</summary>
    private Room? Aim(StoryPersona p) => p == StoryPersona.Vengeful ? Grudge() : null;

    /// <summary>앙갚음형이 노리는 방: 가장 최근에 개조한 방, 없으면 운석·불을 가장 많이 버텨 낸 방.</summary>
    public Room? Grudge()
    {
        var w = _w;
        var h = w.History;
        for (int i = h.Events.Count - 1; i >= 0; i--)
        {
            var e = h.Events[i];
            if (e.Kind != HistoryKind.Upgrade || e.RoomId < 0 || e.RoomId >= w.Ship.Rooms.Count) continue;
            var r = w.Ship.Rooms[e.RoomId];
            if (!r.Detached && !r.Abandoned && r.Type != RoomType.Corridor) return r;
        }
        int Scars(Room r) => h.BreachesByRoom.GetValueOrDefault(r.Id) + h.FiresByRoom.GetValueOrDefault(r.Id);
        return w.Ship.LiveRooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor && Scars(r) > 0)
            .OrderByDescending(Scars).ThenBy(r => r.Id).FirstOrDefault();
    }

    /// <summary>시험용: 지금 배에서 이 성격이 고를 사고 하나 (이야기꾼의 난수를 쓴다).</summary>
    internal (string key, Room? room) Sample(StoryPersona p, float tension, float cap) { var (key, room, _) = Choose(p, tension, cap); return (key, room); }

    private string WhyV15(StoryPersona p, float t, float cap) => p switch
    {
        StoryPersona.SlowBurn => $"불씨가 달아오른다 ({Heat * 100:0}% · 긴장 {t:0.00})",
        StoryPersona.Seasonal => $"{SeasonName(Season)} — 철의 {SeasonPhase * 100:0}% (긴장 {t:0.00})",
        StoryPersona.Merciful => $"다 추슬렀고 막을 물자가 있다 (여력 {cap * 100:0}%)",
        _ => Grudge() is Room g ? $"{Ko.EulReul(g.Name)} 노린다 — 잘 버틴 만큼 되갚는다 (여력 {cap * 100:0}%)" : $"잘 버틴 만큼 되갚는다 (여력 {cap * 100:0}%)",
    };
}
