using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 새 모듈 10 — 배율이 아니라 실제 물건 · 행동을 만든다 (일정표 · 메시지 · 기록):
//   당번표 작성 · 개인 비서 · 식단 계획 · 조명 주기 · 야간 소음 관리 · 출입 관리 · 무게중심 · 오락 보관함 · 신입 교육 · 자동 항해 일지.
// 배율 모듈 둘도 실제 물건을 낸다: 정비 일정 → 정비 일정표 · 물 관리 → 30일 물 예측 곡선.
// 셋째 날부터 하루 하나 · 겪은 일(아니면 연구)이 있을 때 · 연산 자원이 남을 때만 올린다 (새 모듈은 가끔 업데이트 버그를 데려온다).

public sealed record DutySlot(int Day, string Duty, string Time, int CrewId);
public sealed record PersonalMessage(long Tick, int CrewId, string Kind, string Text);
public sealed record MaintSlot(long Tick, int MachineId, string Machine, string Room, string Why);
public sealed record AccessEntry(long Tick, int CrewId, int RoomId, bool Flagged);

public static class ComputerV16
{
    public sealed record Row(ComputerModule Module, string Name, string Note, Func<World, string?> Why, float Research);

    private static string? N(bool ok, string why) => ok ? why : null;
    private static int Live(World w) => w.Crew.Count(c => !c.Dead && !c.IsChild);

    public static readonly Row[] Rows =
    {
        new(ComputerModule.Roster, "당번표 작성", "날마다 야간 당직 · 조리 · 청소 · 순찰 당번을 짜서 알린다 (일이 적은 사람부터)",
            w => N(Live(w) >= 5, $"승무원 {Live(w)}명 — 당번이 엇갈린다"), 20f),
        new(ComputerModule.Assistant, "개인 비서", "사람마다 기상 알람 · 걸음 기록 · 혼잣말 상대 (외로운 사람과 친해진다 — 다 듣는다는 불만도)",
            w => N(w.Crew.Any(c => !c.Dead && c.Needs.Social < 0.3f), "외로운 사람이 있다"), 30f),
        new(ComputerModule.MealPlan, "식단 계획", "창고와 재배실을 보고 하루 세 끼 식단을 짠다 (상하기 전에 먼저)",
            w => N(w.Hazards.FoodDiscarded >= 1, $"버린 음식 {w.Hazards.FoodDiscarded}") ?? N(w.Food.Rationing, "배급 중"), 25f),
        new(ComputerModule.LightCycle, "조명 주기", "밤(22~6시)에는 복도·침실 조명을 낮춘다 — 잠이 깊어지고 전력을 아낀다", w => null, 20f),
        new(ComputerModule.QuietNight, "야간 소음 관리", "밤에 침실 옆 시끄러운 설비를 저속으로 돌린다", w => N(w.Ship.LiveRooms.Any(r => r.Type == RoomType.Quarters && r.Noise > 0.35f), "침실이 시끄럽다"), 25f),
        new(ComputerModule.Access, "출입 관리", "방마다 누가 언제 드나들었나 적고, 원자로·의무실에 자격 없는 출입을 알린다", w => N(w.History.Breaches >= 1, $"선체 구멍 {w.History.Breaches}번"), 30f),
        new(ComputerModule.Balance, "무게중심", "창고 짐의 무게중심을 재서 한쪽으로 쏠리면 옮길 짐을 권한다", w => null, 30f),
        new(ComputerModule.MediaVault, "오락 보관함", "영화·음악을 보관해 쉬는 사람에게 틀어 준다 (연산을 많이 먹어 위기엔 맨 먼저 끈다)",
            w => N(w.Crew.Where(c => !c.Dead).Select(c => c.Needs.Stress).DefaultIfEmpty(0f).Average() > 0.4f, "모두 지쳤다"), 20f),
        new(ComputerModule.Training, "신입 교육", "솜씨가 모자란 사람에게 날마다 짧은 교육 (약한 솜씨가 조금씩 는다)", w => N(w.Crew.Any(c => !c.Dead && c.SkillLevels.Max() < 0.4f), "솜씨가 모자란 사람이 있다"), 25f),
        new(ComputerModule.AutoLog, "자동 항해 일지", "여섯 시간마다 배 상태 · 사고 · 조치를 한 줄씩 항해 일지에 적는다", w => null, 15f),
    };

    private static readonly Dictionary<ComputerModule, Row> ByModule = Rows.ToDictionary(r => r.Module);
    public static Row? Of(ComputerModule m) => ByModule.TryGetValue(m, out var r) ? r : null;
    public static string? Why(World w, Row r) => r.Why(w) ?? (w.Research >= r.Research ? $"연구 {w.Research:0}점" : null);

    public static readonly string[] Films = { "별바다 소나타", "마지막 기항지", "얼음 행성의 여름", "작은 배 큰 꿈", "고향 바다 다큐", "웃음 모음 3", "옛 노래 모음", "우주 정거장 탈출기" };
}

public sealed class ComputerApps
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 1949 + 41));
    private long _hourNext, _sixNext, _forecastNext;
    private int _rosterDay = -1, _menuDay = -1, _maintDay = -1;
    private readonly Dictionary<int, int> _lastRoom = new();
    private readonly Dictionary<int, System.Numerics.Vector2> _lastPos = new();
    private readonly Dictionary<int, long> _privacyAt = new();
    private readonly Dictionary<int, long> _alarmDay = new();

    public List<DutySlot> Roster { get; } = new();
    public List<PersonalMessage> Messages { get; } = new();
    public List<string> Menu { get; } = new();
    public List<MaintSlot> MaintPlan { get; } = new();
    public List<AccessEntry> AccessLog { get; } = new();
    public List<(long tick, string text)> Logbook { get; } = new();
    public Dictionary<int, float> Steps { get; } = new();
    public Dictionary<int, int> Bond { get; } = new();
    public Dictionary<string, int> Watched { get; } = new();
    /// <summary>30일 물 예측 (0일 = 지금, L).</summary>
    public float[] WaterForecast { get; } = new float[31];
    public float WaterRate { get; private set; }
    public int WaterEmptyDay { get; private set; } = -1;
    private readonly List<float> _waterSamples = new();
    /// <summary>무게중심 (칸 좌표) · 배 가운데 · 기울어짐 (칸).</summary>
    public System.Numerics.Vector2 Mass { get; private set; }
    public System.Numerics.Vector2 Middle { get; private set; }
    public float Tilt { get; private set; }
    public string BalanceAdvice { get; private set; } = "";
    public int Lessons, QuietNights, Flags;

    public ComputerApps(World w) => _w = w;

    private bool On(ComputerModule m) => _w.Automation.Active(m) && _w.Automation.MainOnline;

    private void Msg(CrewMember c, string kind, string text)
    {
        Messages.Add(new PersonalMessage(_w.Tick, c.Id, kind, text));
        if (Messages.Count > 120) Messages.RemoveAt(0);
        _w.Automation.Book.Today.Messages++;
    }

    public void Update(float dt)
    {
        var w = _w;
        var a = w.Automation;
        // 출입 관리 · 걸음 기록: 시스템 틱마다 (방 단위)
        if (On(ComputerModule.Access) || On(ComputerModule.Assistant)) Track();
        if (w.Tick >= _forecastNext) { _forecastNext = w.Tick + SimTime.Hours(6); if (On(ComputerModule.WaterPlan)) Forecast(); }
        if (w.Tick < _hourNext) return;
        _hourNext = w.Tick + SimTime.Hours(1);
        if (!a.MainOnline) return;
        int day = SimTime.Day(w.Tick);
        float hour = SimTime.HourOfDay(w.Tick);
        if (On(ComputerModule.Roster) && day != _rosterDay && hour >= 5f) { _rosterDay = day; MakeRoster(day); }
        if (On(ComputerModule.MealPlan) && day != _menuDay && hour >= 5f) { _menuDay = day; MakeMenu(day); }
        if (On(ComputerModule.MaintPlan) && day != _maintDay && hour >= 4f) { _maintDay = day; MakeMaint(); }
        if (On(ComputerModule.Assistant)) Assistant(day, hour);
        if (On(ComputerModule.LightCycle) && (hour >= 22f || hour < 6f))
            foreach (var r in w.Ship.LiveRooms) if (r.Type is RoomType.Corridor or RoomType.Quarters && r.Powered) a.Book.Today.Kwh += 0.08f;
        if (On(ComputerModule.QuietNight) && (hour >= 22f || hour < 6f)) Quiet();
        if (On(ComputerModule.MediaVault)) Media();
        if (On(ComputerModule.Training) && (int)hour == 15) Train();
        if (On(ComputerModule.Balance) && (int)hour % 6 == 0) Balance();
        if (On(ComputerModule.AutoLog) && w.Tick >= _sixNext) { _sixNext = w.Tick + SimTime.Hours(6); WriteLog(); }
        if (On(ComputerModule.FatigueAlert)) Fatigue();
    }

    public void MakeRoster(int day)
    {
        var w = _w;
        var crew = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Down).OrderBy(c => c.Id).ToList();
        if (crew.Count == 0) return;
        Roster.RemoveAll(s => s.Day < day - 2);
        var load = crew.ToDictionary(c => c.Id, c => Roster.Count(s => s.CrewId == c.Id));
        foreach (var (duty, time) in new[] { ("야간 당직", "22~06시"), ("조리", "07·12·18시"), ("청소", "10시"), ("정비 순찰", "14시") })
        {
            var cm = w.Automation.CrewModel; // v16.16 승무원 모형: 지칠 사람 · 싫어하는 사람을 피한다 (짐작이라 틀릴 수 있다)
            var who = crew.OrderBy(c => load[c.Id] + cm.DutyCost(c, duty)).ThenBy(c => duty == "조리" ? -c.RawSkill(Skill.Cooking) : duty == "정비 순찰" ? -c.RawSkill(Skill.Mechanics) : 0f).ThenBy(c => (c.Id + day) % crew.Count).First();
            load[who.Id]++;
            Roster.Add(new DutySlot(day, duty, time, who.Id));
            Msg(who, "당번", $"오늘 {duty} 당번 ({time})");
            cm.OnDuty(who, duty);
        }
        w.Automation.Book.Today.Rosters++;
    }

    private void MakeMenu(int day)
    {
        var w = _w;
        Menu.Clear();
        int meals = w.Ship.CountStored(ItemKind.Meal), rations = w.Ship.CountStored(ItemKind.Ration), produce = w.Ship.CountStored(ItemKind.Produce);
        int ripe = w.Ship.FurnitureOf(FurnitureType.GrowBed).Count(f => f.Machine?.Crop is { Ripe: true });
        Menu.Add($"아침: {(produce > 4 ? "채소죽" : rations > 0 ? "배급 비스킷" : "물에 불린 곡물")}");
        Menu.Add($"점심: {(meals > 3 ? "어제 만든 찜 (먼저 먹는다)" : produce > 0 ? "수경 채소 볶음" : "배급 식량")}");
        Menu.Add($"저녁: {(ripe > 0 ? "갓 딴 채소 국" : produce > 2 ? "채소 조림" : "배급 식량 데움")}");
        if (rations + meals + produce < w.Crew.Count(c => !c.Dead) * 3) Menu.Add("메모: 남은 식량이 사흘 치가 안 된다 — 양을 줄였다");
        w.Automation.Book.Today.Meals += 3;
    }

    private void MakeMaint()
    {
        var w = _w;
        MaintPlan.Clear();
        long t = w.Tick - w.Tick % SimTime.TicksPerDay + SimTime.Hours(9);
        foreach (var m in w.Ship.Machines.Where(m => !m.Body.Room.Detached && m.Body.Room.DataLinked && (m.Wear > 0.35f || m.Omen != null || m.SensorCal < 0.75f))
                     .OrderByDescending(m => (m.Omen != null ? 1f : 0f) + m.Wear + (1f - m.SensorCal)).ThenBy(m => m.Body.Id).Take(6))
        {
            string why = m.Omen != null ? "전조가 보인다" : m.Wear > 0.35f ? $"마모 {m.Wear * 100:0}%" : $"감지기 교정 {m.SensorCal * 100:0}%";
            MaintPlan.Add(new MaintSlot(t, m.Body.Id, m.Name, m.Body.Room.Name, why));
            t += SimTime.Hours(1.5f);
        }
        w.Automation.Book.Today.Schedules += MaintPlan.Count;
    }

    private void Track()
    {
        var w = _w;
        bool access = On(ComputerModule.Access), steps = On(ComputerModule.Assistant);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room == null) continue;
            if (steps)
            {
                if (_lastPos.TryGetValue(c.Id, out var p)) Steps[c.Id] = Steps.GetValueOrDefault(c.Id) + (c.Position - p).Length();
                _lastPos[c.Id] = c.Position;
            }
            if (!access) continue;
            int rid = c.Room.Id;
            if (_lastRoom.TryGetValue(c.Id, out var prev) && prev != rid && c.Room.Type != RoomType.Corridor)
            {
                bool flag = c.Room.Type == RoomType.Reactor && c.RawSkill(Skill.Engineering) < 0.3f && c.Job?.Order == null && !c.Down;
                AccessLog.Add(new AccessEntry(w.Tick, c.Id, rid, flag));
                if (AccessLog.Count > 200) AccessLog.RemoveAt(0);
                if (flag)
                {
                    Flags++;
                    w.Log.Add(w.Tick, LogKind.Ship, $"출입 관리 — {Ko.IGa(c.Name)} 자격 없이 {c.Room.Name}에 들어갔다 (일 없이)", c.Id);
                }
                w.Automation.Book.Today.Access++;
            }
            _lastRoom[c.Id] = rid;
        }
    }

    private void Assistant(int day, float hour)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            // 기상 알람
            if ((int)hour == (int)c.Schedule.WakeHour && _alarmDay.GetValueOrDefault(c.Id, -1) != day) { _alarmDay[c.Id] = day; Msg(c, "알람", $"{(int)c.Schedule.WakeHour}시 — 일어날 시간"); }
            // 걸음 기록 (자기 전 한 번)
            if ((int)hour == (int)c.Schedule.SleepStart && Steps.TryGetValue(c.Id, out var s) && s > 1f) { Msg(c, "운동", $"오늘 {s:0}칸 걸었다" + (s < 150f ? " — 조금 더 움직이자" : "")); Steps[c.Id] = 0f; }
            if (!c.IsAwake || c.Room == null || c.Job?.Order != null) continue;
            // 혼잣말 상대: 외로운 사람과 친해진다
            if (c.Needs.Social < 0.35f && R.Chance(0.35f))
            {
                c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.12f);
                int bond = Bond[c.Id] = Bond.GetValueOrDefault(c.Id) + 1;
                Msg(c, "말벗", "혼잣말에 컴퓨터가 대답했다");
                w.Automation.Trusts.Change(c, 0.02f, "외로울 때 말벗이 되어 줬다", quiet: true);
                if (bond == 3)
                {
                    Life.Diary(w, c, Persona.Say(c, $"요즘은 {w.Automation.Voice.Call}랑 이야기하는 게 제일 편하다"));
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 개인 비서와 친해졌다 — 밤마다 이야기한다", c.Id);
                }
            }
            // "다 듣는다" — 사생활을 중시하는 사람은 불편하다
            else if ((c.Value == CrewValue.Freedom || w.Policies["privacy"] == 1) && _privacyAt.GetValueOrDefault(c.Id, -SimTime.TicksPerDay) < w.Tick - SimTime.TicksPerDay && R.Chance(0.15f))
            {
                _privacyAt[c.Id] = w.Tick;
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
                w.Automation.Trusts.Change(c, -0.03f, "개인 비서가 다 듣는다", quiet: true);
                w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, "컴퓨터가 침실에서 하는 말까지 다 듣는다 — 꺼 달라"), c.Id);
            }
        }
    }

    private void Quiet()
    {
        var w = _w;
        foreach (var q in w.Ship.LiveRooms.Where(r => r.Type == RoomType.Quarters))
        {
            if (!w.Crew.Any(c => c.Room == q && c.Pose == Pose.Sleeping)) continue;
            int slowed = 0;
            foreach (var d in q.Doors)
                if ((d.RoomA == q ? d.RoomB : d.RoomA) is Room n && n.Noise > 0.3f) slowed += n.Furniture.Count(f => f.Machine is { Active: true });
            if (slowed == 0) continue;
            QuietNights++;
            w.Automation.Book.Today.Quiet++;
            foreach (var c in w.Crew) if (c.Room == q && c.Pose == Pose.Sleeping) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.005f);
        }
    }

    private void Media()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Room?.Type is not (RoomType.Lounge or RoomType.Mess) || c.Job?.Order != null) continue;
            string film = ComputerV16.Films[(SimTime.Day(w.Tick) + c.Room.Id) % ComputerV16.Films.Length];
            Watched[film] = Watched.GetValueOrDefault(film) + 1;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f);
            w.Automation.Book.Today.Media++;
        }
    }

    private void Train()
    {
        var w = _w;
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Down && c.IsAwake).OrderBy(c => c.Id))
        {
            int weak = 0;
            for (int i = 1; i < c.SkillLevels.Length; i++) if (c.SkillLevels[i] < c.SkillLevels[weak]) weak = i;
            if (c.SkillLevels[weak] >= 0.4f) continue;
            c.SkillLevels[weak] = MathF.Min(1f, c.SkillLevels[weak] + 0.01f);
            Lessons++;
            w.Automation.Book.Today.Trainings++;
            Msg(c, "교육", $"오늘의 교육: {Skills.Name((Skill)weak)} 기초 (15분)");
        }
    }

    private void Balance()
    {
        var w = _w;
        float sx = 0, sy = 0, total = 0, cx = 0, cy = 0; int cells = 0;
        foreach (var f in w.Ship.Containers)
        {
            int n = f.Storage!.Total;
            if (n <= 0) continue;
            var c = f.Room.Center;
            sx += c.X * n; sy += c.Y * n; total += n;
        }
        foreach (var r in w.Ship.LiveRooms) { cx += r.Center.X * r.Volume; cy += r.Center.Y * r.Volume; cells += r.Volume; }
        if (total <= 0 || cells <= 0) return;
        Mass = new(sx / total, sy / total);
        Middle = new(cx / cells, cy / cells);
        Tilt = (Mass - Middle).Length();
        var heavy = w.Ship.Containers.Where(f => f.Storage!.Total > 0).OrderByDescending(f => (f.Room.Center - Middle).Length() * f.Storage!.Total).FirstOrDefault();
        var light = w.Ship.Containers.OrderBy(f => (f.Room.Center - (2 * Middle - Mass)).Length()).FirstOrDefault();
        BalanceAdvice = Tilt > 3f && heavy != null && light != null && heavy.Room != light.Room ? $"무게중심이 {Tilt:0.#}칸 쏠렸다 — {heavy.Room.Name} 짐 {Math.Min(heavy.Storage!.Total, 10)}개를 {Ko.EuRo(light.Room.Name)}" : $"무게중심 {Tilt:0.#}칸 — 괜찮다";
        w.Automation.Book.Today.Balance++;
    }

    private void WriteLog()
    {
        var w = _w;
        var a = w.Automation;
        string zone = PropulsionSystem.ZoneName(w.Propulsion.Zone);
        int fires = a.FireCases.Count, acts = a.Book.ActsToday;
        string text = $"{w.Day}일 {w.Clock} — 승무원 {w.Crew.Count(c => !c.Dead)}명 · 물 {w.Water.Level:0}L · 배터리 {w.Power.BatteryCharge:0}kWh · 위기 {Crisis.Name(Crisis.Level(w))} · 오늘 자동 조치 {acts}건" + (fires > 0 ? $" · 불 {fires}곳 대응 중" : "") + (zone != "" ? $" · {zone}" : "");
        Logbook.Add((w.Tick, text));
        if (Logbook.Count > 60) Logbook.RemoveAt(0);
        a.Book.Today.Logs++;
    }

    private readonly Dictionary<int, long> _fatigued = new();
    private void Fatigue()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || c.Needs.Rest >= 0.25f || c.Job?.Order == null) continue;
            if (_fatigued.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(4)) continue;
            _fatigued[c.Id] = w.Tick;
            w.Automation.Book.Today.Fatigue++;
            Msg(c, "피로", $"기력 {c.Needs.Rest * 100:0}% — 지금 일은 한 번 더 확인하고, 끝나면 쉬자");
            w.Automation.Book.Add(ActKind.Module, c.Room, $"{c.Name} 기력 {c.Needs.Rest * 100:0}% · {c.Job.Order.Title}", "졸려서 하는 실수가 4배", "피로 경보", "끝나면 쉬기", "fat:" + c.Id, SimTime.Hours(4), 60f,
                (world, act) => (c.Needs.Rest > 0.3f || c.Pose == Pose.Sleeping ? 1 : 2, c.Needs.Rest > 0.3f || c.Pose == Pose.Sleeping ? "맞았다 — 쉬었다" : "보류 — 아직 일한다"));
        }
    }

    /// <summary>물 관리: 하루 동안의 물 증감으로 30일 물 예측 곡선을 그린다.</summary>
    private void Forecast()
    {
        var w = _w;
        _waterSamples.Add(w.Water.Level);
        if (_waterSamples.Count > 8) _waterSamples.RemoveAt(0);
        float rate = _waterSamples.Count >= 2 ? (_waterSamples[^1] - _waterSamples[0]) / ((_waterSamples.Count - 1) * 0.25f) : (w.Water.Produced - w.Water.Consumed) * 24f; // L/일
        WaterRate = rate;
        WaterEmptyDay = -1;
        for (int d = 0; d <= 30; d++)
        {
            WaterForecast[d] = Math.Clamp(w.Water.Level + rate * d, 0f, w.Water.Capacity);
            if (WaterEmptyDay < 0 && WaterForecast[d] <= 0.5f) WaterEmptyDay = d;
        }
    }
}

public sealed partial class AutomationSystem
{
    private ComputerApps? _apps;
    /// <summary>v16.6 새 모듈이 만드는 물건 (당번표 · 메시지 · 식단 · 정비 일정표 · 물 예측 · 출입 기록 · 항해 일지 …).</summary>
    public ComputerApps Apps => _apps ??= new ComputerApps(_world);

    private long _v16Next, _v16Last = -SimTime.TicksPerDay * 2;

    /// <summary>새 모듈 올리기: 셋째 날부터 하루 하나 · 연산이 남을 때만.</summary>
    private void InstallV16()
    {
        var w = _world;
        if (w.Tick < _v16Next) return;
        _v16Next = w.Tick + SimTime.Hours(1);
        if (V15NoAuto || !MainOnline || w.Tick <= SimTime.TicksPerDay * 2 || w.Tick - _v16Last < SimTime.TicksPerDay) return;
        foreach (var r in ComputerV16.Rows)
        {
            if (Has(r.Module) || ComputerV16.Why(w, r) is not string why) continue; // v16.20 첫날부터 다 있다 (시험이 뺀 모듈만 다시 단다)
            if ((Demand() + ModuleLoad(r.Module)) / MathF.Max(1f, Capacity) > 0.8f) return; // 연산이 모자라다
            _v16Last = w.Tick;
            Install(r.Module, why);
            MaybeBug(r.Module);
            return;
        }
    }
}
