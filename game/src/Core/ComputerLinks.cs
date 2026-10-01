using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 주컴퓨터 ↔ 다른 시스템 (양방향 — 이게 저것을 바꾸고 저것이 이것을 바꾼다):
//  · 문 본체(v16.3): 문 감지기가 고장 나면(DoorBody.SensorBroken) 컴퓨터는 그 방 사람을 못 센다(믿음 Blind — 빈 방이라 믿는다) →
//    사람이 확인해 틀린 게 드러나면 컴퓨터가 그 문 감지기 고장을 알고 정비 일정표에 올린다 → 수리 손이 그 문을 먼저 본다.
//    출입 관리 모듈: 잠긴 문 앞에서 기다리는 사람을 급한 까닭이 있으면 원격으로 들인다(다친 사람 · 사람을 업은 사람 → 의무실,
//    고장 난 설비를 고칠 줄 아는 사람 → 기관실) — 들어간 사람은 컴퓨터를 더 믿고, 까닭 없이 막힌 사람은 조금 덜 믿는다. 사생활 · 함장 권한은 사람이.
//  · 식단(식량): 식단 계획 모듈은 남은 식량 · 재배량으로 바닥나는 날을 먼저 보고 배급을 하루 앞당긴다(방침이 "모두 묻는다"면 제안) →
//    주방 당번이 배급표를 붙인다 → 배급으로 배고픈 사람은 "컴퓨터 식단"을 탓한다(신뢰 ↓) → 불평이 절반을 넘으면 컴퓨터가 앞당기기를 거둔다(배우기).
//  · 대재난: 예보(CosmicForecast) — 미리 방송한다 → 들은 사람만(믿으면) 미리 대피소로 간다(HeedBroadcastActivity) → 예보가 맞으면 믿고, 헛예보면 의심한다.
//    EMP(CosmicHit) — 스피커 회로가 타고(방송이 안 들리는 방) · 감지기 값이 깨지고(Stuck — 낡은 믿음) · 재부팅(그동안 사람이 손으로).
//    방사선(태양 폭풍 · CosmicHit) — 대피 방송 (들은 사람만 먼저 움직인다).

public sealed partial class AutomationSystem
{
    private long _linkNext;
    private readonly HashSet<int> _doorBlind = new();   // 문 감지기 고장을 믿음에 옮긴 문 (고쳐질 때까지 한 번만)
    private readonly HashSet<int> _doorKnown = new();   // 사람 확인으로 고장이 드러난 문 (정비 일정에 올렸다)
    private readonly Dictionary<int, int> _doorRoom = new();
    private readonly Dictionary<int, long> _denyAt = new();
    private readonly Dictionary<int, long> _gripeAt = new();
    private bool _stormSeen, _mealEased;
    private long _rationOk = -1, _rationAsk = -1, _rationAdvised = -1;
    /// <summary>불평이 많아 배급 앞당기기를 거뒀다 (배급이 끝나면 다시).</summary>
    public bool MealEased => _mealEased;
    public int DoorBlinds, DoorsKnown, Passes, Denials, RationLeads, MealGripes, Emps, Forecasts, ForecastHits, ForecastMisses;

    /// <summary>예보 하나 (맞았나는 기한에 채점).</summary>
    public sealed class SpaceForecast
    {
        public string Name { get; init; } = "";
        public CosmicFx Fx { get; init; }
        public long Tick { get; init; }
        public long Due { get; init; }
        public bool Hit { get; set; }
        public bool Graded { get; set; }
        public int BroadcastId { get; init; } = -1;
    }
    public List<SpaceForecast> SpaceForecasts { get; } = new();
    /// <summary>대피 방송이 아직 유효하다 (예보 기한 · 폭풍 중).</summary>
    public bool ShelterCall => _shelterUntil > _world.Tick;
    private long _shelterUntil = -1;

    private void Links(float dt)
    {
        var w = _world;
        // 태양 폭풍(기존 Hazards)이 시작되면 같은 길로: 대피 방송 (들은 사람만 먼저 움직인다)
        if (w.Hazards.StormActive && !_stormSeen) { _stormSeen = true; CosmicHit("태양 폭풍", CosmicFx.Radiation, 0.5f); }
        else if (!w.Hazards.StormActive) _stormSeen = false;
        if (w.Tick < _linkNext) return;
        _linkNext = w.Tick + SimTime.Minutes(5);
        DoorSensors();
        if (MainOnline) { MealLead(); MealGripes_(); }
        if (!w.Food.Rationing) _mealEased = false;
        ForecastsDue();
    }

    // ───────────────────── 문 본체 ─────────────────────

    /// <summary>사람 확인으로 고장이 드러난 문 감지기 (수리 손이 먼저 본다).</summary>
    public bool DoorKnownBroken(int doorId) => _doorKnown.Contains(doorId);

    private void DoorSensors()
    {
        var w = _world;
        var ship = w.Ship;
        foreach (var db in w.Body.Doors)
        {
            var d = db.Door < ship.Doors.Count && ship.Doors[db.Door].Id == db.Door ? ship.Doors[db.Door] : ship.Doors.FirstOrDefault(x => x.Id == db.Door);
            if (d == null || d.Removed) continue;
            if (!db.SensorBroken) { _doorBlind.Remove(db.Door); _doorKnown.Remove(db.Door); continue; }
            if (!_doorBlind.Contains(db.Door))
            {
                _doorBlind.Add(db.Door);
                Room? r = db.Inner >= 0 && db.Inner < ship.Rooms.Count ? ship.Rooms[db.Inner] : d.RoomB is Room rb && rb.Type != RoomType.Corridor ? rb : d.RoomA;
                if (r == null || r.Detached || r.Type == RoomType.Corridor || !r.DataLinked) continue;
                _doorRoom[db.Door] = r.Id;
                if (Belief.Of(r).Fault != SensorFault.None) continue;
                Belief.Break(r, SensorFault.Blind, $"{r.Name} 문 감지기 고장 — 드나드는 사람을 못 센다");
                DoorBlinds++;
                continue;
            }
            // 사람이 확인해 믿음을 고쳤는데 문 감지기는 아직 고장: 이제 컴퓨터가 안다 → 정비 일정표 · 수리 손이 먼저
            if (!_doorKnown.Contains(db.Door) && _doorRoom.TryGetValue(db.Door, out int rid) && rid < ship.Rooms.Count && Belief.Of(ship.Rooms[rid]).Fault == SensorFault.None && Belief.Of(ship.Rooms[rid]).Misreads > 0)
            {
                var room = ship.Rooms[rid];
                _doorKnown.Add(db.Door);
                DoorsKnown++;
                Apps.MaintPlan.Insert(0, new MaintSlot(w.Tick + SimTime.Minutes(30), -1, "문 감지기", room.Name, "사람 확인으로 드러났다 — 드나드는 사람을 못 센다"));
                Book.Add(ActKind.Advice, room, $"{room.Name} — 사람 확인에서 믿음과 실제가 달랐다", "문 감지기가 고장 났다 (드나드는 사람을 못 센다)", "정비 일정표 맨 앞에 올림", "가까운 손이 문 감지기를 고친다",
                    "doorfix:" + db.Door, SimTime.Hours(6), 240f, (world, a) => world.Body.Doors.FirstOrDefault(x => x.Door == db.Door) is { SensorBroken: false } ? (1, "맞았다 — 문 감지기를 고쳤다") : (2, "보류 — 아직 못 고쳤다"));
                w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {room.Name} 문 감지기 고장을 알았다 — 정비 일정표 맨 앞에 올렸다");
            }
        }
    }

    /// <summary>출입 관리: 잠긴 문 앞에서 기다리는 사람을 급한 까닭이 있으면 원격으로 들인다 (문 본체에서 부른다). 들였으면 true.</summary>
    public bool RemotePass(CrewMember c, Door d, DoorBody db)
    {
        var w = _world;
        if (!MainOnline || !d.Powered) return false;
        if (db.Zone is AccessZone.Captain or AccessZone.Security or AccessZone.Cabin or AccessZone.Open) return false; // 사생활 · 함장 권한은 사람이
        var inner = db.Inner >= 0 && db.Inner < w.Ship.Rooms.Count ? w.Ship.Rooms[db.Inner] : null;
        if (inner == null || inner.Detached || !inner.DataLinked) return false;
        // 제 방송으로 보낸 대피소가 잠겨 있으면 컴퓨터가 연다 (출입 관리 모듈이 없어도 — 제 말에 책임진다)
        bool shelter = ShelterCall && inner == ShelterRoom() && Speak.Ordered(c, "shelter", SimTime.Hours(1)) != null;
        if (!shelter && !Active(ComputerModule.Access)) return false;
        string zone = DoorBody.ZoneName(db.Zone);
        string? why = db.Zone switch
        {
            _ when shelter => "대피 방송 — 대피소로 들인다",
            AccessZone.Medicine when c.CarryingPerson is CrewMember p => $"{Ko.EulReul(p.Name)} 업고 왔다",
            AccessZone.Medicine when c.Vitals.Injury > 0.15f || c.Vitals.Health < 0.7f || DiseaseSystem.Sick(c) => "다쳤거나 아프다",
            AccessZone.Reactor when inner.Furniture.Any(f => f.Machine is { Faults.Count: > 0 }) && (c.SkillLevel(Skill.Engineering) >= 0.4f || c.SkillLevel(Skill.Electrical) >= 0.4f) => "고장 난 설비를 고칠 줄 안다",
            _ => null,
        };
        if (why == null)
        {
            if (!_denyAt.TryGetValue(c.Id, out var t0) || w.Tick - t0 > SimTime.Hours(3))
            {
                _denyAt[c.Id] = w.Tick;
                Denials++;
                Book.Today.Denied++;
                Book.Add(ActKind.Door, inner, $"{c.Name} — 잠긴 {zone} 문 앞", "출입 자격이 없고 급한 까닭도 안 보인다", "잠근 채 둔다", "자격 있는 사람이 열어 준다", "deny:" + c.Id, SimTime.Hours(3), 30f,
                    (world, a) => (2, "참고 — 사람이 판단했다"));
                Trusts.Change(c, -0.015f, $"잠긴 {zone} 문 앞에서 컴퓨터가 안 열어 줬다", quiet: true);
            }
            return false;
        }
        db.Pass = c.Id;
        db.PassUntil = w.Tick + SimTime.Minutes(5);
        Passes++;
        Book.Today.Passes++;
        w.Log.Add(w.Tick, LogKind.Life, $"주 컴퓨터가 {zone} 문을 원격으로 열었다 — {c.Name} ({why})", c.Id);
        Book.Add(ActKind.Door, inner, $"{c.Name} — 잠긴 {zone} 문 앞 · {why}", "출입 관리: 급한 까닭이 있다", $"{zone} 문 원격 열기 (5분)", "", "pass:" + c.Id + ":" + db.Door, SimTime.Minutes(30), 20f,
            (world, a) => c.Dead ? (-1, $"틀렸다 — {c.Name} 숨졌다") : c.Room?.Id == inner.Id || world.Tick - a.Tick > SimTime.Minutes(15) ? (1, $"맞았다 — {c.Name} 기다리지 않고 들어갔다") : null);
        Trusts.Change(c, 0.04f, $"잠긴 {zone} 문을 컴퓨터가 열어 줬다", quiet: true);
        c.Say(w, Persona.Say(c, $"고마워, {Voice.Call}"));
        return true;
    }

    // ───────────────────── 식단 · 식량 ─────────────────────

    /// <summary>식단 계획이 배급을 앞당기는 기준 (일치) — 0이면 앞당기지 않는다. 배급 계획(Living)이 읽는다.</summary>
    public float RationLead => Active(ComputerModule.MealPlan) && MainOnline && !_mealEased && _rationOk >= 0 && _world.Tick - _rationOk < SimTime.Hours(12) ? 3f : 0f;

    private void MealLead()
    {
        var w = _world;
        var f = w.Food;
        if (!Active(ComputerModule.MealPlan) || f.Rationing || f.Disabled || _mealEased) return;
        int crew = w.Crew.Count(c => !c.Dead);
        if (crew < 2) return;
        float days = FoodPolicy.FoodDays(w);
        float below = w.Policies["rations"] == 3 ? 4f : 2f;
        float grow = FoodPolicy.GrowingPerDay(w), need = crew * FoodPolicy.MealsPerPersonDay;
        if (days >= 3f || days < below || grow >= need * 1.05f) return; // 기본 기준으로도 붙일 때는 컴퓨터 몫이 아니다
        var kitchen = w.Ship.FurnitureOf(FurnitureType.Stove).FirstOrDefault()?.Room ?? w.Ship.FurnitureOf(FurnitureType.MealDispenser).FirstOrDefault()?.Room;
        string basis = $"남은 식량 {days:0.0}일치 · 재배 하루 {grow:0}끼 / 먹는 양 {need:0}끼";
        if (Asks.Needed("ration"))
        {
            if (Asks.Pending("ration") != null || _rationAsk >= 0 && w.Tick - _rationAsk < SimTime.Hours(12)) return;
            _rationAsk = w.Tick;
            Asks.Propose("ration", "ration", kitchen, "배급 하루 앞당기기", basis, "바닥나는 날이 하루쯤 늦어진다 · 대신 배고프다", 30f, null,
                (world, p) => { _rationOk = world.Tick; _rationAdvised = world.Tick; },
                (world, p) => !p.Accepted ? (2, "참고 — 거절됐다 (기본 기준까지 기다린다)") : world.Food.Rationing ? (1, "맞았다 — 배급표가 붙었다") : null);
            return;
        }
        if (_rationOk >= 0 && w.Tick - _rationOk < SimTime.Hours(12)) return;
        if (Book.Add(ActKind.Advice, kitchen, basis, "이틀치가 되기 전에 줄이면 바닥나는 날이 늦어진다 (식단 계획)", "배급 하루 앞당김", "주방 당번 — 배급표", "rationlead", SimTime.Hours(12), 240f,
                (world, a) => world.Food.Rationing ? (1, "맞았다 — 배급표가 붙었고 식량이 오래 간다") : (2, "보류 — 아직 배급표가 없다")) == null) return;
        _rationOk = w.Tick;
        _rationAdvised = w.Tick;
        RationLeads++;
        Book.Today.RationLeads++;
        Apps.Menu.Add("메모: 남은 식량이 사흘 치가 안 된다 — 배급을 하루 앞당긴다");
        Speak.Announce(Voice.Style($"식단 — 남은 식량 {days:0.0}일치. 배급을 하루 앞당긴다"), kitchen, 1);
    }

    /// <summary>컴퓨터가 앞당긴 배급으로 배고픈 사람은 컴퓨터 식단을 탓한다 · 불평이 절반을 넘으면 컴퓨터가 앞당기기를 거둔다.</summary>
    private void MealGripes_()
    {
        var w = _world;
        if (!w.Food.Rationing || _rationAdvised < 0 || w.Tick - _rationAdvised > SimTime.TicksPerDay * 3) return;
        int hungry = 0, live = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            live++;
            if (c.Needs.Food >= 0.3f) continue;
            hungry++;
            if (!c.IsAwake || _gripeAt.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(8)) continue;
            _gripeAt[c.Id] = w.Tick;
            MealGripes++;
            Trusts.Change(c, -0.02f, $"{Voice.Call} 식단이 너무 박하다", quiet: true);
            w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, "배고프다 — 컴퓨터가 짠 식단이 너무 박하다"), c.Id);
        }
        if (!_mealEased && live > 0 && hungry * 2 > live)
        {
            _mealEased = true;
            Learn.Remember(-1, "ration", $"배급을 앞당겼더니 {hungry}/{live}명이 배고프다고 했다");
            Apps.Menu.Add("메모: 불평이 많다 — 다음엔 배급을 앞당기지 않는다");
            Book.Add(ActKind.Advice, null, $"배고프다는 사람 {hungry}/{live}명", "앞당긴 배급이 너무 박했다", "다음엔 기본 기준까지 기다린다", "", "rationease", SimTime.TicksPerDay, 10f, (world, a) => (2, "참고 — 배운 것"));
        }
    }

    // ───────────────────── 대재난: 예보 · EMP · 방사선 ─────────────────────

    private Room? ShelterRoom() => Facilities.Best(_world.Ship, "shelter", r => !r.Detached && !r.OffLimits && !r.Leaking).room;

    /// <summary>예보: 몇 분 뒤 무엇이 온다 (Cosmic 쪽 · 항로 위험 예보에서 부른다). 방송 — 들은 사람만(믿으면) 미리 대피소로 간다. 기한에 맞았나 채점.</summary>
    public Broadcast? CosmicForecast(string name, float minutesAhead, CosmicFx fx)
    {
        var w = _world;
        if (!Present || !MainOnline) return null;
        bool hide = (fx & (CosmicFx.Radiation | CosmicFx.Emp | CosmicFx.Shock | CosmicFx.Debris | CosmicFx.Heat)) != 0;
        var shelter = ShelterRoom();
        var b = Speak.Announce(Voice.Style($"예보 — {minutesAhead:0}분 뒤 {name}. " + (hide ? $"{Ko.EuRo(shelter?.Name ?? "안쪽 방")} 미리 가라" : "대비하라")), shelter, 2, hide ? "shelter" : "");
        var fc = new SpaceForecast { Name = name, Fx = fx, Tick = w.Tick, Due = w.Tick + SimTime.Minutes(minutesAhead + 30f), BroadcastId = b?.Id ?? -1 };
        SpaceForecasts.Add(fc);
        if (SpaceForecasts.Count > 20) SpaceForecasts.RemoveAt(0);
        Forecasts++;
        Book.Today.Forecasts++;
        if (hide) _shelterUntil = Math.Max(_shelterUntil, fc.Due);
        Book.Add(ActKind.Forecast, shelter, $"{name} 예보 — {minutesAhead:0}분 뒤", hide ? "선체 밖에서 닥친다 — 안쪽 차폐된 방이 덜 다친다" : "대비할 시간이 있다", "예보 방송",
            hide ? "대피소로 미리" : "대비", "fc:" + name + ":" + w.Tick, 0, minutesAhead + 35f,
            (world, a) => fc.Graded ? (fc.Hit ? (1, $"맞았다 — 예보대로 {name}이(가) 왔다") : (-1, $"헛예보 — {name}은(는) 오지 않았다")) : null);
        return b;
    }

    /// <summary>대재난이 닿았다: 방사선이면 대피 방송 · EMP면 스피커 회로가 타고 감지기 값이 깨지고 재부팅. 예보가 있었으면 맞은 것으로.</summary>
    public void CosmicHit(string name, CosmicFx fx, float strength)
    {
        var w = _world;
        strength = Math.Clamp(strength, 0f, 1f);
        bool forecast = false;
        foreach (var fc in SpaceForecasts) if (!fc.Graded && fc.Name == name && w.Tick <= fc.Due) { fc.Hit = true; forecast = true; }
        if (forecast) ForecastsDue(); // 닿는 순간 채점 — 미리 피한 사람은 곧장 "컴퓨터 말이 맞았다"
        if ((fx & CosmicFx.Radiation) != 0 && Present && MainOnline)
        {
            var shelter = ShelterRoom();
            _shelterUntil = Math.Max(_shelterUntil, w.Tick + SimTime.Hours(2));
            var b = Speak.Announce(Voice.Style($"{name} — 방사선이 세다. {Ko.EuRo(shelter?.Name ?? "안쪽 방")}"), shelter, 2, "shelter");
            Book.Add(ActKind.Forecast, shelter, $"{name} · 세기 {strength * 100:0}%", "선체 밖 방사선 — 안쪽 차폐된 방이 덜 쬔다", "대피 방송", "대피소로", "cosmic:rad", SimTime.Hours(1), 40f,
                (world, a) =>
                {
                    if (b == null) return (2, "참고 — 방송이 안 나갔다");
                    int safe = b.HeardBy.Count(id => world.Crew.FirstOrDefault(c => c.Id == id) is { Dead: false, Room: Room r } && r.Radiation < 0.2f);
                    return safe * 2 >= Math.Max(1, b.HeardBy.Count) ? (1, $"맞았다 — 들은 {b.HeardBy.Count}명 가운데 {safe}명이 덜 쬐는 곳에 있다") : (2, $"보류 — 들은 {b.HeardBy.Count}명 가운데 {safe}명만 피했다");
                });
        }
        if ((fx & CosmicFx.Emp) != 0 && Present)
        {
            Emps++;
            var rooms = w.Ship.LiveRooms.OrderBy(r => r.Id).ToList();
            int broke = 0, stuck = 0;
            foreach (var r in rooms)
                if (!Speak.SpeakerBroken(r) && RR.Chance(0.15f + 0.4f * strength)) { Speak.BreakSpeaker(r, $"{name} — 회로가 탔다", 4f + 8f * strength); broke++; }
            foreach (var r in rooms)
                if (r.DataLinked && r.Type != RoomType.Corridor && Belief.Of(r).Fault == SensorFault.None && RR.Chance(0.25f + 0.5f * strength)) { Belief.Break(r, SensorFault.Stuck, $"{name} — 감지기 값이 깨졌다"); stuck++; }
            w.Log.Add(w.Tick, LogKind.Warning, $"{name} — 전자기 펄스: 스피커 {broke}곳이 타고, 감지기 {stuck}곳 값이 깨졌다 · 주 컴퓨터가 멎는다");
            if (MainOnline) Reboot($"{name} — 전자기 펄스", 3f + 6f * strength);
        }
    }

    /// <summary>예보 기한: 맞았나 채점 · 미리 대피한 사람의 신뢰 (맞으면 ↑, 헛예보면 ↓).</summary>
    private void ForecastsDue()
    {
        var w = _world;
        foreach (var fc in SpaceForecasts)
        {
            if (fc.Graded || !fc.Hit && w.Tick < fc.Due) continue;
            fc.Graded = true;
            if (fc.Hit) { ForecastHits++; Book.Today.ForecastHits++; } else ForecastMisses++;
            var b = Speak.Recent.FirstOrDefault(x => x.Id == fc.BroadcastId);
            if (b == null) continue;
            var shelter = ShelterRoom();
            foreach (var id in b.HeardBy)
            {
                if (w.Crew.FirstOrDefault(c => c.Id == id) is not CrewMember c || c.Dead) continue;
                bool hid = c.Room == shelter || c.Job?.Activity is HeedBroadcastActivity; // 실제로 따랐나
                if (fc.Hit) Trusts.Change(c, hid ? 0.06f : 0.02f, hid ? $"{fc.Name} 예보를 듣고 미리 피했다 — 컴퓨터 말이 맞았다" : $"{fc.Name} 예보가 맞았다 — 따를 걸 그랬다", quiet: true);
                else Trusts.Change(c, hid ? -0.05f : -0.02f, $"{fc.Name} 예보를 듣고 " + (hid ? "대피소에서 기다렸는데 오지 않았다" : "긴장했는데 오지 않았다"), quiet: true);
                if (fc.Hit && hid) MarkLog.Add(c.Memory.Marks, w.Tick, $"{fc.Name} 예보를 듣고 미리 {Ko.EuRo(shelter?.Name ?? "대피소")} 피했다");
            }
            if (!fc.Hit) Learn.Remember(-1, "forecast", $"{fc.Name} 헛예보");
        }
    }
}

/// <summary>v16.6 방송대로 움직이기: 대피 지시가 실린 방송을 들은 사람만(컴퓨터를 믿으면) 대피소로 간다. 못 들은 사람은 위험이 눈앞에 오기 전까지 모른다.</summary>
public sealed class HeedBroadcastActivity : Activity
{
    public override string Id => "heed";
    public override string Label => "방송대로 대피";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var a = w.Automation;
        if (c.Outside || c.Down || c.CarriedBy != null || !a.ShelterCall) return (0f, "—"); // 문간(방 없음)에서 다시 생각해도 잊지 않는다
        var b = a.Speak.Ordered(c, "shelter", SimTime.Hours(1));
        if (b == null) return (0f, "대피 방송을 못 들었다");
        if (!a.Trusts.Obeys(c)) return (0f, "컴퓨터 방송 — 믿지 않는다");
        var (shelter, _) = Facilities.Best(w.Ship, "shelter", r => !r.Detached && !r.OffLimits && !r.Leaking);
        if (shelter == null) return (0f, "대피소가 없다");
        // 믿는 만큼 급하다: 컴퓨터를 믿는 사람은 밥숟가락을 놓고 가고, 반신반의하는 사람은 하던 일을 마치고 간다
        float urge = 0.8f + 0.5f * a.Trusts.Of(c);
        if (c.Room == shelter) return c.Job?.Activity is HeedBroadcastActivity ? (urge, "방송대로 대피소에서 기다린다") : (0f, "이미 대피소");
        return (urge, $"방송 — {b.Text}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (shelter, _) = Facilities.Best(w.Ship, "shelter", r => !r.Detached && !r.OffLimits && !r.Leaking);
        if (shelter == null) return null;
        if (c.Room == shelter) return new Job(this, "방송대로 대피", new List<Toil> { new WaitToil(SimTime.Minutes(20), Pose.Sitting) }) { TargetRoom = shelter };
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var cell in shelter.Cells)
        {
            int d = dist.Get(cell);
            if (d < 0 || d >= bestCost || !w.Ship.IsOpenFloor(cell) || w.IsSpotTaken(cell, c)) continue;
            best = cell;
            bestCost = d;
        }
        if (best is not Cell target) return null;
        return new Job(this, "방송대로 대피", new List<Toil> { new GotoToil(target), new WaitToil(SimTime.Minutes(30), Pose.Sitting) })
        {
            TargetRoom = shelter,
            Urgent = true,
            LogText = $"선내 방송을 듣고 {Ko.EuRo(shelter.Name)} 미리 대피",
            LogKind = LogKind.Warning,
        };
    }
}
