using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15.4 항해 확장: 구간 20 · 지명 70 · 기항지 성격 10 · 외부 사건 40
public static partial class Program
{
    private static int RunVoyageV15Test(int seed)
    {
        _fails = 0;
        Console.WriteLine($"항해 확장 점검 (v15.4) · 시드 {seed}\n");
        float keepDays = HazardSystem.RandomDays;
        try
        {
            var newKinds = Enum.GetValues<LegKind>().Where(k => k >= LegKind.SolarWind).ToList();
            string[] classic = { "meteor", "bigmeteor", "fire", "break", "pipe" };

            // 0) 개수: 구간 20 · 지명 70 · 기항지 성격 10 · 외부 사건 40 (이름이 겹치지 않고, 새 구간마다 사고 배율 · 이점)
            {
                var places = VoyageV15.PortNames.Concat(VoyageV15.Wrecks).Concat(VoyageV15.Belts).Concat(VoyageV15.Clouds).Concat(VoyageV15.Rads).Concat(VoyageV15.Legs.SelectMany(l => l.Places)).ToList();
                var badKey = VoyageV15.Legs.SelectMany(l => l.Risks.Select(r => r.key)).Where(k => !classic.Contains(k) && !Enum.TryParse<HazardKind>(k, out _)).ToList();
                bool specs = newKinds.All(k => VoyageV15.Spec(k) is LegSpec s && s.Kind == k && s.Risks.Any(r => r.mul != 1f) && VoyageSystem.KindName(k) == s.Name)
                    && VoyageV15.Legs.Select(l => l.Perk).Distinct().Count() == newKinds.Count;
                bool traitsUsed = VoyageV15.Traits.All(t => VoyageV15.Ports.Any(p => p.trait == t.Id));
                Check("개수 — 구간 20 · 지명 70 · 기항지 성격 10 · 외부 사건 40",
                    Enum.GetValues<LegKind>().Length == 20 && VoyageV15.PlaceCount == 70 && places.Distinct().Count() == 70
                    && VoyageV15.Traits.Length == 10 && VoyageV15.Traits.Select(t => t.Id).Distinct().Count() == 10 && traitsUsed
                    && OutsideSystem.Catalog.Length == 40 && OutsideSystem.Catalog.Select(s => s.Id).Distinct().Count() == 40 && specs && badKey.Count == 0,
                    $"구간 {Enum.GetValues<LegKind>().Length} · 지명 {VoyageV15.PlaceCount}(겹침 없이 {places.Distinct().Count()}) · 기항지 성격 {VoyageV15.Traits.Length} · 외부 사건 {OutsideSystem.Catalog.Length} · 이점 {VoyageV15.Legs.Select(l => l.Perk).Distinct().Count()}가지"
                    + (badKey.Count > 0 ? $" · 없는 사고 키: {string.Join(",", badKey)}" : "") + (specs ? "" : " · 구간 표가 어긋남"));
            }

            // 1) 새 구간이 항로에 섞여 나온다 (순항으로 시작해 기항지로 끝나는 틀은 그대로)
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                var seen = new Dictionary<LegKind, int>();
                bool frame = true, named = true;
                int voyages = 100, legs = 0;
                for (int i = 0; i < voyages; i++)
                {
                    if (i > 0) w.Voyage.Replan();
                    var v = w.Voyage;
                    frame &= v.Legs.First().Kind == LegKind.Cruise && v.Legs.Last().Kind == LegKind.Port;
                    foreach (var l in v.Legs)
                    {
                        legs++;
                        seen[l.Kind] = seen.GetValueOrDefault(l.Kind) + 1;
                        named &= VoyageV15.Pool(l.Kind).Contains(l.Name);
                    }
                }
                var missing = newKinds.Where(k => !seen.ContainsKey(k)).ToList();
                int fresh = newKinds.Sum(k => seen.GetValueOrDefault(k));
                Check("새 구간 — 항로에 섞여 나온다", missing.Count == 0 && frame && named && fresh > voyages / 2,
                    $"항해 {voyages}번 · 구간 {legs} 중 새 구간 {fresh} · " + string.Join(" ", newKinds.Select(k => $"{VoyageSystem.KindName(k)} {seen.GetValueOrDefault(k)}"))
                    + (missing.Count > 0 ? $" · 안 나옴: {string.Join(",", missing)}" : "") + (frame ? "" : " · 틀이 깨짐") + (named ? "" : " · 풀에 없는 이름"));
            }

            // 2) 새 구간마다 이점이 실제로 든다 (들어설 때 · 지나는 동안)
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(2));
                var v = w.Voyage;
                w.Propulsion.Propellant = w.Propulsion.Capacity * 0.3f;
                w.Air.Reserve = w.Air.ReserveCapacity * 0.5f;
                w.Water.Level = w.Water.Capacity * 0.5f;
                VoyageV15.Put(w, ItemKind.MetalOre, 30);
                // v16.22 새 설계에서는 번호 앞쪽이 냉각 펌프다 (닳게 하면 원자로가 멎고 엔진이 선다) — 원자로 · 냉각 · 엔진 · 배전 밖의 설비 넷을 닳게 한다
                foreach (var m in w.Ship.Machines.Where(m => m.Body.Room.Type is not (RoomType.Reactor or RoomType.Cooling or RoomType.Engine or RoomType.Power) && m.Body.Type is not (FurnitureType.CoolantPump or FurnitureType.ReactorCore or FurnitureType.EngineCore)).OrderBy(m => m.Body.Id).Take(4)) m.Wear = 0.6f;
                foreach (var c in w.Crew) c.Needs.Stress = MathF.Max(c.Needs.Stress, 0.4f);
                float Stored(params ItemKind[] ks) => ks.Sum(k => w.Ship.CountStored(k));
                float Metric(LegPerk p) => p switch
                {
                    LegPerk.Ice => Stored(ItemKind.Ice) + w.Water.Level,
                    LegPerk.Propellant => w.Propulsion.Propellant,
                    LegPerk.Mining => w.Space.Mean,
                    LegPerk.Research => w.Research,
                    LegPerk.Sensor => w.Sensors.Quality,
                    LegPerk.Credits => v.Credits,
                    LegPerk.Battery => w.Power.BatteryCharge,
                    LegPerk.Salvage => Stored(ItemKind.Plate, ItemKind.Valve, ItemKind.Gasket, ItemKind.Fuse, ItemKind.Cable, ItemKind.Motor),
                    LegPerk.Calm => -w.Crew.Where(c => !c.Dead).Sum(c => c.Needs.Stress),
                    LegPerk.Air => w.Air.Reserve,
                    LegPerk.Nutrient => Stored(ItemKind.Nutrient, ItemKind.Seed),
                    LegPerk.Repair => -w.Ship.Machines.Sum(m => m.Wear),
                    LegPerk.Training => w.Crew.Where(c => !c.Dead).Sum(c => c.RawSkill(Skill.Piloting)),
                    _ => 0f,
                };
                var ok = new List<string>();
                var bad = new List<string>();
                foreach (var spec in VoyageV15.Legs)
                {
                    v.Force(LegKind.Cruise);
                    w.Power.BatteryCharge = w.Power.BatteryCapacity * (spec.Perk == LegPerk.Battery ? 0.2f : 0.8f); // v16.22 새 한빛호는 원자로 여유가 적다 — 배터리가 바닥이면 엔진 · 채집 팔부터 끊는다 (배터리 이점을 잴 때만 낮춘다)
                    bool good;
                    string how;
                    if (spec.Perk == LegPerk.Speed)
                    {
                        float p0 = v.Progress; Run(w, SimTime.Hours(2)); float cruise = v.Progress - p0;
                        v.Force(spec.Kind, enter: true);
                        p0 = v.Progress; Run(w, SimTime.Hours(2)); float fast = v.Progress - p0;
                        good = fast > cruise * 1.3f;
                        how = $"{cruise * 24:0.0}→{fast * 24:0.0}시간";
                    }
                    else
                    {
                        float a = Metric(spec.Perk);
                        v.Force(spec.Kind, enter: true);
                        float b = Metric(spec.Perk);
                        good = b > a + 1e-4f;
                        how = $"{a:0.##}→{b:0.##}";
                    }
                    if (!good) how += $" · 표류 {v.Drifting} · 엔진 {string.Join(",", w.Propulsion.Engines.Select(m => $"{m.Efficiency:0.00}"))} · 채집 {w.Ship.FurnitureOf(FurnitureType.Collector).Sum(f => f.Machine!.Efficiency):0.00} · 원자로 {w.Power.ReactorOnline} · 배터리 {w.Power.BatteryCharge:0} · 엔진실 전기 {w.Propulsion.Engines.First().Body.Room.Powered}/{w.Propulsion.Engines.First().Powered}/고장 {string.Join(",", w.Propulsion.Engines.First().Faults.Select(f => f.Kind))} · 배전반 {string.Join(",", w.Ship.FurnitureOf(FurnitureType.PowerPanel).SelectMany(f => f.Machine!.Faults).Select(f => $"{f.Kind}@{f.Circuit}"))} · 회로 {string.Join("", w.Power.CircuitFed.Select(x => x ? "1" : "0"))}";
                    (good ? ok : bad).Add($"{spec.Name}({spec.Perk} {how})");
                }
                Check("새 구간 — 이점이 실제로 든다 (14가지)", bad.Count == 0, $"듦 {ok.Count}/{VoyageV15.Legs.Length}" + (bad.Count > 0 ? $" · 안 듦: {string.Join(" / ", bad)}" : $" · 예: {string.Join(" / ", ok.Take(4))}"));
                v.Force(LegKind.PatrolLane);
                bool hz = v.HazardMul("meteor") < 1f;
                v.Force(LegKind.Perihelion);
                hz &= v.HazardMul(nameof(HazardKind.Overheat)) > 2f && v.HazardMul("meteor") == 1f;
                Check("새 구간 — 사고의 무게가 구간마다 다르다", hz, $"순찰 항로 운석 ×{VoyageV15.HazardMul(LegKind.PatrolLane, "meteor")} · 근일점 과열 ×{v.HazardMul(nameof(HazardKind.Overheat))}");
            }

            // 3) 기항지 성격: 값(가격 배율) · 파는 물건 · 태우는 사람 · 들르면 생기는 일
            {
                (float earned, int sold, World w) TradeAt(string place)
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    VoyageV15.Put(w, ItemKind.MetalOre, 30);
                    w.Voyage.Force(LegKind.Port, enter: true, name: place);
                    return (w.Voyage.Earned, w.Voyage.Sold.GetValueOrDefault(ItemKind.MetalOre), w);
                }
                var yard = TradeAt("팔라스 조선소");
                var mine = TradeAt("베스타 광산");
                Check("기항지 성격 — 같은 짐도 기항지마다 값이 다르다", yard.sold == mine.sold && yard.sold > 0 && yard.earned > mine.earned * 1.5f,
                    $"금속 원료 {yard.sold}개 · 조선소 {yard.earned:0.0} 대 광산 {mine.earned:0.0} (조선소 원료 ×{VoyageV15.Trait("shipyard").Sell(ItemKind.MetalOre):0.##} · 광산 ×{VoyageV15.Trait("mine").Sell(ItemKind.MetalOre):0.##})");

                // 파는 물건 · 들르면 생기는 일
                var sci = World.CreateDefault(seed, 0, "Hanbit");
                float r0 = sci.Research;
                int fiber0 = sci.Ship.CountStored(ItemKind.Fiber) + sci.Ship.CountStored(ItemKind.Thermocouple) + sci.Ship.CountStored(ItemKind.Sensor);
                sci.Voyage.Force(LegKind.Port, enter: true, name: "트리톤 관측소");
                int fiber1 = sci.Ship.CountStored(ItemKind.Fiber) + sci.Ship.CountStored(ItemKind.Thermocouple) + sci.Ship.CountStored(ItemKind.Sensor);
                var trade = sci.History.Events.LastOrDefault(e => e.Text.Contains("교역 —"));
                // 태우는 사람: 사람이 모자란 배가 병원선에 들르면 간호사가 탄다
                var med = World.CreateDefault(seed, 0, "Hanbit");
                med.Crew.OrderBy(c => c.Id).Last().Dead = true;
                CrewMember? nurse = null;
                for (int i = 0; i < 6 && nurse == null; i++)
                {
                    int n0 = med.Crew.Count;
                    med.Voyage.Force(LegKind.Port, enter: true, name: "칼리스토 병원선");
                    if (med.Crew.Count > n0) nurse = med.Crew[^1];
                }
                Check("기항지 성격 — 파는 물건 · 들르면 생기는 일 · 태우는 사람", sci.Research >= r0 + 6f && fiber1 > fiber0 && nurse != null && nurse.Background == Background.Nurse,
                    $"관측 기지: 연구 {r0:0}→{sci.Research:0} · 전용 물건 {fiber0}→{fiber1} · 병원선에서 탄 사람 {(nurse != null ? $"{nurse.Name}({Life.Name(nurse.Background)})" : "없음")}"
                    + (trade != null ? $" · {trade.Text}" : ""));
            }

            // 4) 외부 사건 40: 조건(구간 · 통신실 · 사고 켜짐)을 맞추면 일어나고, 무언가를 바꾼다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(3));
                VoyageV15.Put(w, ItemKind.MetalOre, 30);
                HazardSystem.RandomDays = 0f;
                bool quiet = OutsideSystem.Catalog.Where(s => s.Risky).All(s => { w.Voyage.Force(s.Legs?[0] ?? LegKind.Cruise); return !w.Outside.Fits(s); });
                HazardSystem.RandomDays = 1f; // 사고 켜짐 (Fire만 부르고 시뮬레이션은 돌리지 않는다)
                var fired = new List<string>();
                var missed = new List<string>();
                var still = new List<string>();
                foreach (var s in OutsideSystem.Catalog)
                {
                    w.Voyage.Force(s.Legs?[0] ?? LegKind.Cruise);
                    var before = Snap(w);
                    string? id = w.Outside.Fits(s) ? w.Outside.Fire(s.Id) : null;
                    if (id == null) { missed.Add(s.Id); continue; }
                    fired.Add(s.Id);
                    var after = Snap(w);
                    if (!before.Zip(after).Any(p => MathF.Abs(p.First - p.Second) > 1e-4f)) still.Add(s.Id);
                }
                HazardSystem.RandomDays = keepDays;
                int moved = fired.Count - still.Count;
                Check("외부 사건 — 40 중 34 이상이 조건을 맞추면 일어나 무언가를 바꾼다", fired.Count >= 34 && moved >= 34 && quiet,
                    $"일어남 {fired.Count}/40 · 바꿈 {moved}" + (missed.Count > 0 ? $" · 안 일어남: {string.Join(",", missed)}" : "") + (still.Count > 0 ? $" · 바뀐 것 없음: {string.Join(",", still)}" : "")
                    + (quiet ? " · 사고 꺼짐이면 사고를 부르는 사건은 쉰다" : " · 사고 꺼짐에도 사고 사건이 맞음") + $" · 최근: {string.Join(" / ", w.Outside.Recent.TakeLast(3).Select(r => r.text))}");
            }

            // 5) 하루 한두 번꼴 · 결정론
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay * 3);
                Check("외부 사건 — 하루 한두 번꼴", w.Outside.Fired >= 2 && w.Outside.Fired <= 9,
                    $"사흘에 {w.Outside.Fired}번 · " + string.Join(" / ", w.Outside.Recent.Select(r => $"{SimTime.Day(r.tick)}일 {r.text}")));
                uint H()
                {
                    var x = World.CreateDefault(seed, 0, "Hanbit");
                    Run(x, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(x);
                }
                uint a = H(), b = H();
                Check("결정론 — 새 구간 · 바깥 사건이 든 배도 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"예외: {ex}");
            _fails++;
        }
        finally { HazardSystem.RandomDays = keepDays; }
        Console.WriteLine(_fails == 0 ? "\n✔ 항해 확장 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>바깥 사건이 바꿀 수 있는 것들 (물자 · 돈 · 연구 · 추진제 · 물 · 공기 · 배터리 · 항로 · 사람 · 설비 · 사고 무게 · 구조 신호).</summary>
    private static float[] Snap(World w)
    {
        var x = new List<float>();
        foreach (var k in Enum.GetValues<ItemKind>()) x.Add(w.Ship.CountStored(k));
        x.Add(w.Voyage.Credits); x.Add(w.Research); x.Add(w.Propulsion.Propellant); x.Add(w.Water.Level); x.Add(w.Air.Reserve); x.Add(w.Power.BatteryCharge);
        x.Add(w.Voyage.Progress); x.Add(w.Voyage.Index); x.Add(w.Hazards.RandomCount); x.Add(w.Comms.SignalAt);
        foreach (var key in new[] { "meteor", nameof(HazardKind.SolarStorm) }) x.Add(w.Voyage.HazardMul(key));
        foreach (var c in w.Crew)
        {
            x.Add(c.Needs.Stress); x.Add(c.Needs.Social); x.Add(c.Needs.Rest); x.Add(c.Needs.Food); x.Add(c.Vitals.Injury); x.Add(c.Memory.Trauma);
            x.Add(c.SkillLevels.Sum());
            x.Add(w.Crew.Sum(o => o.AffinityTo(c)));
        }
        foreach (var m in w.Ship.Machines) { x.Add(m.Wear); x.Add(m.Faults.Count); }
        return x.ToArray();
    }
}
