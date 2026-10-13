using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 볼거리 — 다른 배와의 만남 · 기항지 상륙 · 축제 · 명절 (사용자가 고른 것).
//  다른 배: 교역선(남는 것과 모자란 것을 바꾼다) · 조난선(살아남은 사람을 태운다) · 다른 세대선(잔치 · 사람을 주고받는다 · 혼인)
//   · 약탈선(물자를 내놓을지 · 드론 방패로 버틸지 · 달아날지 — 버티면 선체가 맞는다). 화면 밖에 그 배가 나란히 보인다.
//  기항지 상륙: 몇 사람씩 내려 장을 본다 · 술집 다툼 · 고향 음식 · 내리는 사람(향수병이 깊으면) · 기념품.
//  축제 · 명절: 배의 생일 · 수확 축제(재배실 수확이 쌓이면) · 혼인 · 떠난 사람 추모 — 장식 불빛 · 특식 · 함께 노래.

public enum MeetKind : byte { Trader, Distress, Generation, Raider }

public sealed class ShipMeet
{
    public int Id { get; init; }
    public MeetKind Kind { get; init; }
    public string Name { get; init; } = "";
    public long Start { get; init; }
    public long End { get; set; }
    public bool Done { get; set; }
    public string Outcome { get; set; } = "";
    public float Side { get; init; } = 1f;
}

public sealed class EncounterSystem
{
    public static bool Off;
    private readonly World _w;
    private readonly Rng _rng;
    private long _nextHour, _meetAfter;
    private int _nextId, _lastLegIndex = -1, _harvestSeen, _lastBirthday = -1;
    public List<ShipMeet> Meets { get; } = new();
    public ShipMeet? Now => Meets.LastOrDefault(m => !m.Done);
    public int Traded, Rescued, Exchanged, Paid, Fought, Fled, ShoreLeaves, Brawls, Souvenirs, Festivals;
    public List<string> FestivalLog { get; } = new();
    /// <summary>장식 불빛이 걸린 방 · 언제까지.</summary>
    public int DecorRoom { get; private set; } = -1;
    public long DecorUntil { get; private set; } = -1;

    public EncounterSystem(World w) { _w = w; _rng = new Rng(unchecked(w.Seed * 73 + 909)); }

    public static string KindName(MeetKind k) => k switch { MeetKind.Trader => "교역선", MeetKind.Distress => "조난선", MeetKind.Generation => "다른 세대선", _ => "약탈선" };
    private static readonly string[] ShipNames = { "푸른 고래", "여명", "백조", "새벽별", "은하수", "모래시계", "늙은 거북", "붉은 매", "흰 돛", "검은 까마귀" };

    private float RateMul => Off || Storyteller.Persona == StoryPersona.Off && HazardSystem.RandomDays <= 0f ? 0f : 0.5f + 0.1f * Storyteller.Level;
    private IEnumerable<CrewMember> Aboard => _w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away);

    public void Update(float dt)
    {
        var w = _w;
        if (Off) return;
        if (Now is ShipMeet m && w.Tick >= m.End) Close(m);
        if (w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        float mul = RateMul;
        var leg = w.Voyage.Current;
        // 다른 배
        if (mul > 0f && Now == null && w.Tick >= _meetAfter && leg.Kind is not (LegKind.Port or LegKind.DeepVoid or LegKind.Nebula) && !w.Planet.Landed)
        {
            float perDay = leg.Kind switch { LegKind.TradeLane => 0.6f, LegKind.PatrolLane => 0.35f, LegKind.Lagrange => 0.3f, LegKind.Cruise => 0.2f, _ => 0.08f };
            if (_rng.Chance(perDay / 24f * mul))
            {
                float x = _rng.Float();
                var k = leg.Kind == LegKind.TradeLane ? (x < 0.6f ? MeetKind.Trader : x < 0.8f ? MeetKind.Generation : x < 0.92f ? MeetKind.Distress : MeetKind.Raider)
                    : leg.Kind == LegKind.PatrolLane ? (x < 0.4f ? MeetKind.Raider : x < 0.7f ? MeetKind.Trader : MeetKind.Distress)
                    : (x < 0.4f ? MeetKind.Trader : x < 0.65f ? MeetKind.Distress : x < 0.85f ? MeetKind.Generation : MeetKind.Raider);
                Meet(k);
            }
        }
        // 기항지 상륙 (구간이 바뀌어 항구에 닿으면)
        if (w.Voyage.Index != _lastLegIndex) { _lastLegIndex = w.Voyage.Index; if (leg.Kind == LegKind.Port) ShoreLeave(leg.Name); }
        Festival();
    }

    // ─────────────────────────────── 다른 배 ───────────────────────────────

    public ShipMeet Meet(MeetKind k)
    {
        var w = _w;
        var m = new ShipMeet { Id = ++_nextId, Kind = k, Name = ShipNames[_rng.Range(0, ShipNames.Length)] + "호", Start = w.Tick, End = w.Tick + SimTime.Hours(k == MeetKind.Generation ? 14 : 6), Side = _rng.Chance(0.5f) ? 1f : -1f };
        Meets.Add(m);
        _meetAfter = w.Tick + SimTime.TicksPerDay * 2;
        w.Log.Add(w.Tick, LogKind.Ship, $"{KindName(k)} {m.Name}이 나란히 섰다");
        switch (k)
        {
            case MeetKind.Trader: Trade(m); break;
            case MeetKind.Distress: Rescue(m); break;
            case MeetKind.Generation: Visit(m); break;
            case MeetKind.Raider: Raid(m); break;
        }
        w.History.Add(w, HistoryKind.Incident, $"{KindName(k)} {m.Name}과 만났다 — {m.Outcome}");
        return m;
    }

    private void Close(ShipMeet m)
    {
        m.Done = true;
        _w.Log.Add(_w.Tick, LogKind.Ship, $"{m.Name}이 멀어진다 — 불빛이 별들 사이로 작아진다");
    }

    private static readonly ItemKind[] Raw = { ItemKind.MetalOre, ItemKind.Ice, ItemKind.Silicate, ItemKind.Carbon, ItemKind.Rare, ItemKind.Plate };
    private static readonly ItemKind[] Wanted = { ItemKind.Electronics, ItemKind.Motor, ItemKind.Sensor, ItemKind.Filter, ItemKind.Sealant, ItemKind.Seed, ItemKind.Pump };

    private int Stock(ItemKind k, int n)
    {
        int put = 0;
        foreach (var f in _w.Ship.Furniture.Where(f => f.Storage != null && f.Type is FurnitureType.Shelf or FurnitureType.SupplyCache && !f.Room.Detached))
        {
            put += f.Storage!.Add(k, n - put);
            if (put >= n) break;
        }
        return put;
    }

    private void Trade(ShipMeet m)
    {
        var w = _w;
        var give = Raw.OrderByDescending(k => w.Ship.CountStored(k)).First();
        var need = Wanted.OrderBy(k => w.Ship.CountStored(k)).ThenBy(k => (int)k).First();
        int n = 0;
        for (int i = 0; i < 4 && w.Ship.CountStored(give) > 2 && ItemsV15.Use(w, give); i++) n++;
        int got = n > 0 ? Stock(need, n) : 0;
        Traded++;
        var talker = Aboard.OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).FirstOrDefault();
        if (talker != null) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(talker, $"{ItemKinds.Name(give)}은 넉넉해 — {ItemKinds.Name(need)}이랑 바꾸자"), talker.Id);
        m.Outcome = got > 0 ? $"{ItemKinds.Name(give)} {n} → {ItemKinds.Name(need)} {got}" : "바꿀 것이 없었다 — 소식만 나눴다";
    }

    private void Rescue(ShipMeet m)
    {
        var w = _w;
        int alive = w.Crew.Count(c => !c.Dead && !c.LeftShip);
        int take = Math.Min(_rng.Range(1, 4), Math.Max(0, World.MaxCrew - alive));
        var dock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault()?.Cells.FirstOrDefault(w.Ship.IsOpenFloor);
        var names = new List<string>();
        for (int i = 0; i < take && dock is Cell d; i++)
        {
            var nc = w.AddSurvivor(d);
            nc.Joined = $"조난선 {m.Name}에서 구조";
            nc.Memory.Trauma = MathF.Max(nc.Memory.Trauma, 0.3f);
            names.Add(nc.Name);
        }
        Rescued += names.Count;
        m.Outcome = names.Count > 0 ? $"살아남은 {names.Count}명을 태웠다 ({string.Join(", ", names)})" : "자리가 없어 물자만 건넸다";
        if (names.Count == 0) for (int i = 0; i < 3; i++) ItemsV15.Use(w, ItemKind.Meal);
    }

    private void Visit(ShipMeet m)
    {
        var w = _w;
        w.Motions.ScheduleFeast(null, $"다른 세대선 {m.Name}과 함께 잔치");
        // 사람을 주고받는다: 짝 없는 젊은이 하나가 그 배로 · 그 배의 하나가 이쪽으로 (가끔 혼인)
        var go = Aboard.Where(c => !c.IsChild && !c.Passenger && c.Age < 40f && c.Partner == null).OrderBy(c => c.Traits.Bravery).ThenBy(c => c.Id).LastOrDefault();
        var dock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault()?.Cells.FirstOrDefault(w.Ship.IsOpenFloor);
        if (go != null && dock is Cell d && _rng.Chance(0.6f))
        {
            go.LeftShip = true; go.Room = null;
            var nc = w.AddSurvivor(d);
            nc.Joined = $"세대선 {m.Name}에서 건너왔다";
            nc.Vitals.Injury = 0f; nc.Vitals.Health = 1f;
            Exchanged++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(go.Name)} {m.Name}로 건너가고, 그 배의 {Ko.IGa(nc.Name)} 이쪽으로 왔다");
            foreach (var f in Aboard.Where(o => o.AffinityTo(go) > 0.3f)) Life.Diary(w, f, $"{Ko.IGa(go.Name)} {m.Name}로 떠났다. 창밖으로 불빛이 멀어졌다.");
            m.Outcome = $"잔치 · {go.Name} ↔ {nc.Name}";
        }
        else m.Outcome = "잔치 · 씨앗과 노래를 나눴다";
        Stock(ItemKind.Seed, 2);
    }

    private void Raid(ShipMeet m)
    {
        var w = _w;
        int drones = w.Drones.Drones.Count(d => d.State != DroneState.Lost && !d.Faulty);
        float fight = 0.2f + 0.12f * drones + (w.Automation.MainOnline ? 0.15f : 0f);
        w.RaiseAlert($"약탈선 {m.Name} — 물자를 내놓으라 한다", null, AlertLevel.Warning, shipWide: true);
        var hawk = Aboard.FirstOrDefault(c => c.Value is CrewValue.Freedom or CrewValue.Efficiency);
        var dove = Aboard.FirstOrDefault(c => c.Value is CrewValue.Safety or CrewValue.People);
        if (hawk != null) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(hawk, "한 번 주면 또 온다 — 버티자"), hawk.Id);
        if (dove != null) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(dove, "사람이 먼저다 — 주고 보내자"), dove.Id);
        float x = _rng.Float();
        if (x < fight)
        {
            Fought++;
            w.Hazards.FireStory("meteor", null);
            w.Hazards.FireStory("DebrisAlert", null);
            m.Outcome = $"드론 {drones}대로 막아섰다 — 선체에 몇 발 맞고 물러갔다";
        }
        else if (x < fight + 0.25f && w.Ship.FurnitureOf(FurnitureType.EngineCore).Any())
        {
            Fled++;
            w.Voyage.Shift(0.4f);
            m.Outcome = "엔진을 끝까지 올려 달아났다 — 연료를 많이 썼다";
        }
        else
        {
            Paid++;
            var lost = new List<string>();
            foreach (var k in new[] { ItemKind.Rare, ItemKind.Electronics, ItemKind.Meal })
            {
                int n = 0;
                while (n < 4 && w.Ship.CountStored(k) > 0 && ItemsV15.Use(w, k)) n++;
                if (n > 0) lost.Add($"{ItemKinds.Name(k)} {n}");
            }
            m.Outcome = lost.Count > 0 ? $"물자를 내주고 보냈다 ({string.Join(" · ", lost)})" : "내줄 것도 없었다 — 그냥 갔다";
            foreach (var c in Aboard) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f);
        }
    }

    // ─────────────────────────────── 기항지 상륙 ───────────────────────────────

    public void ShoreLeave(string port)
    {
        var w = _w;
        var crew = Aboard.Where(c => c.CanAct && !c.IsChild).OrderBy(c => c.Id).ToList();
        if (crew.Count == 0) return;
        ShoreLeaves++;
        var lines = new List<string>();
        // 장보기 · 고향 음식 · 기념품
        var shopper = crew[_rng.Range(0, crew.Count)];
        lines.Add($"{Ko.IGa(shopper.Name)} 장터에서 {(_rng.Chance(0.5f) ? "고향 향신료" : "말린 과일")}을 샀다");
        foreach (var c in crew.Take(Math.Max(1, crew.Count / 2))) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.15f); w.Homeland.Ease(c, 0.05f); }
        if (_rng.Chance(0.6f)) { Souvenirs++; var s = crew[_rng.Range(0, crew.Count)]; lines.Add($"{Ko.IGa(s.Name)} 기념품을 샀다 — {port}의 작은 종"); Life.Diary(w, s, $"{port}에서 작은 종을 샀다. 침대 머리맡에 달았다."); }
        // 술집 다툼
        if (crew.Count >= 2 && _rng.Chance(0.3f))
        {
            Brawls++;
            var a = crew[_rng.Range(0, crew.Count)];
            a.Vitals.Injury = MathF.Max(a.Vitals.Injury, 0.12f); a.Vitals.InjuryCause = "술집 다툼";
            lines.Add($"{Ko.IGa(a.Name)} 술집에서 다투다 눈두덩이 부었다");
            w.Log.Add(w.Tick, LogKind.Life, Persona.Say(a, "그쪽이 먼저 우리 배를 깎아내렸다고"), a.Id);
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"{port} 상륙 — " + string.Join(" · ", lines));
        w.History.Add(w, HistoryKind.Incident, $"{port}에 내렸다 — " + string.Join(" · ", lines.Take(2)));
    }

    // ─────────────────────────────── 축제 · 명절 ───────────────────────────────

    private void Festival()
    {
        var w = _w;
        if (w.Motions.Feasting || Aboard.Count() < 3) return;
        string? why = null;
        // 배의 생일: 출항한 지 100일마다
        long age = (w.Tick - w.StartTickOf) / SimTime.TicksPerDay;
        if (age > 0 && age % 100 == 0 && _lastBirthday != (int)age) { _lastBirthday = (int)age; why = $"배의 {age}일 생일"; }
        // 수확 축제: 수확이 쌓이면
        int harvests = w.FoodSources.In.Sum();
        if (why == null && harvests - _harvestSeen >= 120) { _harvestSeen = harvests; why = "수확 축제"; }
        // 혼인 · 추모는 이야기(Tales) 쪽 사건을 받아 잔치로
        if (why == null && w.History.Events.LastOrDefault() is { } e && w.Tick - e.Tick < SimTime.Hours(1) && (e.Text.Contains("혼인") || e.Text.Contains("결혼")))
            why = "혼인 잔치";
        if (why != null) Celebrate(why);
    }

    /// <summary>잔치를 연다 (식당 · 장식 불빛 · 노래).</summary>
    public bool Celebrate(string why)
    {
        var w = _w;
        int day = (int)(w.Tick / SimTime.TicksPerDay);
        if (!w.Motions.ScheduleFeast(null, why)) return false;
        Festivals++;
        DecorRoom = w.Motions.FeastRoom?.Id ?? -1;
        DecorUntil = w.Motions.FeastAt + SimTime.Hours(6);
        string food = why.Contains("수확") ? "새로 딴 채소 전" : why.Contains("생일") ? "배 모양 케이크" : "고향식 국수";
        FestivalLog.Add($"{day + 1}일 {why} — {food} · 장식 불빛 · 함께 노래");
        for (int i = 0; i < 2; i++) ItemsV15.Use(w, ItemKind.Meal);
        foreach (var c in Aboard) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.08f); c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.1f); }
        w.History.Add(w, HistoryKind.Incident, $"{why} — {food}, 장식 불빛 아래 다 같이 노래했다", w.Motions.FeastRoom);
        return true;
    }

    public string? Line() => Now is ShipMeet m ? $"{KindName(m.Kind)} {m.Name} — {m.Outcome}" : null;

    public void Hash(Action<long> I, Action<float> F) { I(Meets.Count); I(Festivals); I(ShoreLeaves); }
}
