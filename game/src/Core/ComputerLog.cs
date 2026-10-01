using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.0 ④ 주컴퓨터 보이게 1차 — 모든 자동 조치를 다섯 칸으로 적는다: 관찰 → 판단 → 조치 → 요청 → 결과.
// 결과는 몇 분 뒤 채점한다 (그 방이 나아졌나 · 사람이 쓰러졌나 · 오경보였나). 기존 판단 근거(Reason)도 이 장부를 부른다.
// 배율 모듈이 실제로 아낀 양(전력 kWh · 물 L · 미룬 고장 · 피로 경보 …)을 쌓아 하루 보고로 남긴다.

public enum ActKind { Alarm, Damper, Bulkhead, Valve, Breaker, Suppress, Shed, Module, Zone, Advice, Proposal, Broadcast, Reboot, Door, Forecast }

/// <summary>컴퓨터가 한 일 하나 (다섯 칸).</summary>
public sealed class ComputerAct
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public ActKind Kind { get; init; }
    public int RoomId { get; init; } = -1;
    public string Key { get; init; } = "";
    public string Observe { get; set; } = "";
    public string Judge { get; set; } = "";
    public string Act { get; set; } = "";
    public string Request { get; set; } = "";
    public string Result { get; set; } = "";
    /// <summary>0 아직 · 1 맞았다 · −1 틀렸다 · 2 판단 보류(참고).</summary>
    public int Score { get; set; }
    public long GradeAt { get; set; }
    public float Before { get; init; }
    public int DownBefore { get; init; }
    /// <summary>사람이 관제석에서 대신 했다 (수동 조종).</summary>
    public string? By { get; init; }
    internal Func<World, ComputerAct, (int score, string why)?>? Grader { get; set; }
    public bool Graded => Score != 0;
}

/// <summary>배율 모듈이 실제로 한 일의 하루치 (보고가 나가면 0으로).</summary>
public sealed class ComputerTally
{
    public float Kwh, WaterL, CalibHours, SpoilHours, LeadMinutes, DeferredHours;
    public int Deferred, Fatigue, DoorEq, Archived, SoilRuns, Backflow, Schedules, Messages, Rosters, Meals, Logs, Trainings, Quiet, Access, Balance, Media;
    public int Passes, Denied, RationLeads, Forecasts, ForecastHits; // v16.6 출입 원격 열기 · 배급 앞당김 · 예보
    public void Clear()
    {
        Kwh = WaterL = CalibHours = SpoilHours = LeadMinutes = DeferredHours = 0f;
        Deferred = Fatigue = DoorEq = Archived = SoilRuns = Backflow = Schedules = Messages = Rosters = Meals = Logs = Trainings = Quiet = Access = Balance = Media = 0;
        Passes = Denied = RationLeads = Forecasts = ForecastHits = 0;
    }
}

public sealed record DailyReport(int Day, string Text, float Kwh, float WaterL, int Deferred, int Fatigue, int Acts, int Right, int Wrong);

public sealed class ComputerLogBook
{
    private readonly World _w;
    private int _next = 1;
    private readonly Dictionary<string, long> _dedupe = new();
    public List<ComputerAct> Acts { get; } = new();
    public int Total, Right, Wrong, Held;
    public Dictionary<ActKind, int> ByKind { get; } = Enum.GetValues<ActKind>().ToDictionary(k => k, _ => 0);
    public ComputerTally Today { get; } = new();
    public List<DailyReport> Reports { get; } = new();
    public long LastTick { get; private set; } = -1;
    public int RightToday, WrongToday, ActsToday;

    public ComputerLogBook(World w) => _w = w;

    /// <summary>그 방이 얼마나 위험한가 (채점용 — 불 칸 · 구멍 · 물 · 산소 · 기압).</summary>
    public static float Danger(World w, Room? r)
    {
        if (r == null || r.Detached) return 0f;
        return w.Fire.CountIn(r) + (r.Leaking ? 3f : 0f) + MoistureSystem.Depth(r) * 20f + MathF.Max(0f, 18f - r.Air.O2) * 0.5f + (r.Air.Pressure < 70f ? 2f : 0f);
    }

    public static int DownIn(World w, Room? r)
    {
        if (r == null) return 0;
        int n = 0;
        foreach (var c in w.Crew) if ((c.Down || c.Dead) && c.Room == r) n++;
        return n;
    }

    /// <summary>다섯 칸 기록 하나. 같은 열쇠는 cooldown 안에 한 번만.</summary>
    public ComputerAct? Add(ActKind kind, Room? room, string observe, string judge, string act, string request, string key = "", long cooldown = 0, float gradeMinutes = 5f,
        Func<World, ComputerAct, (int, string)?>? grader = null)
    {
        var w = _w;
        var au = w.Automation;
        if (au.Present && !au.MainOnline && !au.BackupActive && kind != ActKind.Reboot) return null; // 멎은 컴퓨터는 아무것도 안 한다 (재부팅 중엔 사람이 손으로)
        if (key != "" && cooldown > 0 && _dedupe.TryGetValue(key, out var t) && w.Tick - t < cooldown) return null;
        if (key != "") _dedupe[key] = w.Tick;
        if (request == "") request = DefaultRequest(kind, room); // 넷째 칸(요청)이 비지 않게: 조치마다 사람에게 바라는 것
        var a = new ComputerAct
        {
            Id = _next++, Tick = w.Tick, Kind = kind, RoomId = room?.Id ?? -1, Key = key, Observe = observe, Judge = judge, Act = act, Request = request,
            GradeAt = w.Tick + SimTime.Minutes(gradeMinutes), Before = Danger(w, room), DownBefore = DownIn(w, room), Grader = grader,
            By = au.Operator?.Name ?? (au.Present && !au.MainOnline && au.BackupActive ? "예비 제어기" : null),
        };
        Acts.Add(a);
        if (Acts.Count > 240) Acts.RemoveAt(0);
        Total++; ActsToday++;
        ByKind[kind]++;
        LastTick = w.Tick;
        return a;
    }

    /// <summary>판단 근거 한 줄을 다섯 칸으로 나눈다 ("원인 추정:" · "예측:" → 판단, "조치:" · "→" → 조치, "요청:" → 요청).</summary>
    public ComputerAct? FromReason(string key, string text, long cooldown)
    {
        var w = _w;
        string observe = "", judge = "", act = "", request = "";
        var parts = text.Split(" · ");
        foreach (var raw in parts)
        {
            string p = raw.Trim();
            string Tail(string pre) => p.Substring(p.IndexOf(pre, StringComparison.Ordinal) + pre.Length).Trim();
            if (p.Contains("요청:")) { request = Join(request, Tail("요청:")); continue; }
            if (p.Contains("조치:")) { act = Join(act, Tail("조치:")); continue; }
            if (p.StartsWith("원인 추정:") || p.StartsWith("예측:") || p.StartsWith("방침") || p.StartsWith("교훈")) { judge = Join(judge, p); continue; }
            int arrow = p.IndexOf(" → ", StringComparison.Ordinal);
            if (arrow > 0) { observe = Join(observe, p[..arrow]); act = Join(act, p[(arrow + 3)..]); continue; }
            if (p.Contains(" — ") && observe != "") { judge = Join(judge, p); continue; }
            int dash = p.IndexOf(" — ", StringComparison.Ordinal);
            if (dash > 0 && observe == "") { observe = p[..dash]; judge = Join(judge, p[(dash + 3)..]); continue; }
            if (observe == "") observe = p; else judge = Join(judge, p);
        }
        var kind = KindOf(key);
        Room? room = RoomOf(key);
        if (act == "" && kind != ActKind.Advice) act = judge;
        return Add(kind, room, observe, judge, act, request, "r:" + key, cooldown, kind == ActKind.Advice ? 30f : 8f);
    }

    /// <summary>조치 종류마다 사람에게 바라는 것 (판단 근거에 "요청:"이 없을 때).</summary>
    private static string DefaultRequest(ActKind k, Room? r) => k switch
    {
        ActKind.Suppress => $"{(r != null ? r.Name + " " : "")}사람은 나가라 · 소화조는 문밖에서",
        ActKind.Damper => "그 방 문은 닫아 둔다",
        ActKind.Bulkhead => "안에 있으면 반대쪽 문으로",
        ActKind.Valve => "배관 담당이 새는 곳을 본다",
        ActKind.Breaker => "전기 담당이 분전함을 본다",
        ActKind.Alarm => "가까운 사람이 확인",
        ActKind.Zone => "구역 밖으로",
        ActKind.Shed => "꺼진 설비는 손대지 않는다",
        ActKind.Broadcast => "들은 사람은 따른다",
        ActKind.Reboot => "그동안 손으로",
        ActKind.Forecast => "미리 대비",
        _ => "참고",
    };

    private static string Join(string a, string b) => a == "" ? b : a + " · " + b;

    private static ActKind KindOf(string key)
    {
        string head = key.Split(':')[0];
        return head switch
        {
            "lock" => ActKind.Bulkhead,
            "flood" or "restore" => ActKind.Breaker,
            "valve" => ActKind.Valve,
            "fire1" or "fire2" or "fire3" or "firex" => ActKind.Suppress,
            "zone" => ActKind.Zone,
            _ => ActKind.Advice,
        };
    }

    private Room? RoomOf(string key)
    {
        var parts = key.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out int id) || id < 0 || id >= _w.Ship.Rooms.Count) return null;
        return parts[0] is "lock" or "flood" or "floodwatch" or "valve" or "restore" or "fire1" or "fire2" or "fire3" or "firex" or "o2" ? _w.Ship.Rooms[id] : null;
    }

    /// <summary>채점: 몇 분 뒤 그 방이 나아졌나 · 사람이 쓰러졌나.</summary>
    internal void Grade()
    {
        var w = _w;
        foreach (var a in Acts)
        {
            if (a.Graded || w.Tick < a.GradeAt) continue;
            (int score, string why)? custom = a.Grader?.Invoke(w, a);
            if (a.Grader != null && custom == null) continue; // 아직 모른다 (조금 더 본다)
            var (score, why) = custom ?? Generic(a);
            a.Score = score;
            a.Result = why;
            if (score == 1) { Right++; RightToday++; }
            else if (score == -1) { Wrong++; WrongToday++; w.Automation.Learn.Wrong(a); }
            else Held++;
        }
    }

    private (int, string) Generic(ComputerAct a)
    {
        var w = _w;
        Room? r = a.RoomId >= 0 && a.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[a.RoomId] : null;
        if (r == null) return (2, "참고 — 배 전체 판단");
        int down = DownIn(w, r);
        if (down > a.DownBefore) return (-1, $"틀렸다 — 그 뒤 {r.Name}에서 {down - a.DownBefore}명이 쓰러졌다");
        float now = Danger(w, r);
        if (now <= 0.01f) return (1, a.Before > 0.01f ? $"맞았다 — {r.Name} 위험이 걷혔다" : $"맞았다 — {r.Name} 별일 없다");
        if (now < a.Before - 0.01f) return (1, $"맞았다 — {r.Name} 나아졌다 ({a.Before:0.#} → {now:0.#})");
        if (now > a.Before * 1.5f + 1f) return (-1, $"틀렸다 — {r.Name} 더 나빠졌다 ({a.Before:0.#} → {now:0.#})");
        return (2, $"보류 — {r.Name} 아직 그대로 ({now:0.#})");
    }

    /// <summary>하루 보고 (날이 바뀔 때): 배율 모듈이 실제로 한 일 + 조치 채점.</summary>
    internal DailyReport Report(int day)
    {
        var w = _w;
        var t = Today;
        var parts = new List<string>();
        if (t.Kwh >= 0.05f) parts.Add($"전력 분배로 {t.Kwh:0.#}kWh 아낌");
        if (t.WaterL >= 0.5f) parts.Add($"물 관리로 {t.WaterL:0}L 아낌");
        if (t.Deferred > 0) parts.Add($"전조 분석으로 고장 {t.Deferred}건 미룸 ({t.DeferredHours:0}시간)");
        if (t.Fatigue > 0) parts.Add($"피로 경보 {t.Fatigue}회");
        if (t.CalibHours >= 1f) parts.Add($"감지기 자동 교정 {t.CalibHours:0}대·시간");
        if (t.SpoilHours >= 0.1f) parts.Add($"화물 정리로 상할 음식 {t.SpoilHours:0.#}시간 늦춤");
        if (t.LeadMinutes >= 0.1f) parts.Add($"운석 경보 {t.LeadMinutes:0.#}분 먼저");
        if (t.DoorEq > 0) parts.Add($"문 압 맞춤 {t.DoorEq}번");
        if (t.Archived > 0) parts.Add($"관행 기록 {t.Archived}건");
        if (t.Schedules > 0) parts.Add($"정비 일정 {t.Schedules}건");
        if (t.Rosters > 0) parts.Add($"당번표 {t.Rosters}장");
        if (t.Messages > 0) parts.Add($"개인 비서 메시지 {t.Messages}통");
        if (t.Meals > 0) parts.Add($"식단 {t.Meals}끼");
        if (t.Logs > 0) parts.Add($"항해 일지 {t.Logs}줄");
        if (t.Quiet > 0) parts.Add($"야간 소음 낮춤 {t.Quiet}번");
        if (t.Trainings > 0) parts.Add($"신입 교육 {t.Trainings}번");
        if (t.Passes + t.Denied > 0) parts.Add($"출입 관리 원격 열기 {t.Passes}번" + (t.Denied > 0 ? $" · 막음 {t.Denied}번" : ""));
        if (t.RationLeads > 0) parts.Add("식단 계획으로 배급 하루 앞당김");
        if (t.Forecasts > 0) parts.Add($"앞날 예측 {t.ForecastHits}/{t.Forecasts} 맞힘");
        parts.Add($"자동 조치 {ActsToday}건 (맞음 {RightToday} · 틀림 {WrongToday})");
        string text = $"{day}일 컴퓨터 보고: " + string.Join(" · ", parts);
        var rep = new DailyReport(day, text, t.Kwh, t.WaterL, t.Deferred, t.Fatigue, ActsToday, RightToday, WrongToday);
        Reports.Add(rep);
        if (Reports.Count > 30) Reports.RemoveAt(0);
        t.Clear();
        ActsToday = RightToday = WrongToday = 0;
        return rep;
    }
}

public sealed partial class AutomationSystem
{
    private ComputerLogBook? _book;
    /// <summary>v16.0 다섯 칸 기록 · 하루 보고.</summary>
    public ComputerLogBook Book => _book ??= new ComputerLogBook(_world);

    private long _tallyNext, _reportDay = -1;
    private bool _wasRebooting;

    /// <summary>v16 주컴퓨터 한 틱 (Update 안에서 · 방 단위 · 간격을 두고).</summary>
    private void V16(float dt)
    {
        Belief.Update(dt);
        Resources(dt);
        if (_wasRebooting && !Rebooting) { Suspended.Clear(); Speak.Announce(Voice.Style($"{Voice.Call} 다시 켜짐 — 자동화가 돌아왔다"), null, 1); }
        _wasRebooting = Rebooting;
        if (MainOnline) { Asks.Update(); InstallV16(); }
        UpdateChecks();
        Book.Grade();
        Tally(dt);
        Speak.Update();
        Trusts.Update();
        Voice.Update();
        Apps.Update(dt);
        Links(dt); // v16.6 문 본체 · 식단 · 대재난과 잇기 (ComputerLinks.cs)
        Foresight.Update(); // v16.6 → v16.16 앞날 예측 · 계획 (ComputerForesight.cs)
    }

    /// <summary>배율 모듈이 실제로 아낀 양을 1분마다 쌓는다 (방 단위 · 설비 단위).</summary>
    private void Tally(float dt)
    {
        var w = _world;
        if (w.Tick < _tallyNext) return;
        long step = SimTime.Minutes(1);
        float h = step / (float)SimTime.TicksPerHour;
        _tallyNext = w.Tick + step;
        var t = Book.Today;
        if (MainOnline)
        {
            if (Active(ComputerModule.PowerShare))
            {
                foreach (var r in w.Ship.LiveRooms)
                    if (ComputerV15.RoomKwMul(w, r) < 1f && r.Powered) t.Kwh += PowerGrid.RoomSystemsKw * 0.4f * h;
                foreach (var m in w.Ship.Machines)
                    if (!m.Active && m.Powered && ComputerV15.IdleKwMul(w, m) < 1f) t.Kwh += m.Demand * 0.5f * h; // 대기 전력을 반으로 줄인 몫
            }
            if (Active(ComputerModule.WaterPlan)) t.WaterL += w.Water.Consumed / 0.88f * 0.12f * h;
            if (Active(ComputerModule.AutoCalib))
                foreach (var m in w.Ship.Machines) if (m.Body.Room.DataLinked && !m.Body.Room.Detached) t.CalibHours += h;
            if (Active(ComputerModule.CargoSort))
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.Fridge)) if (f.Machine is { Stopped: true } || f.Machine is { Powered: false }) t.SpoilHours += h * 0.6f;
        }
        // 날이 바뀌면 하루 보고
        int day = SimTime.Day(w.Tick);
        if (_reportDay < 0) _reportDay = day;
        if (day != _reportDay)
        {
            int wrong = Book.WrongToday, right = Book.RightToday;
            var rep = Book.Report((int)_reportDay);
            _reportDay = day;
            // 사후 검토: 컴퓨터가 자주 틀리면 묻게 하자 · 제안이 늘 맞으면 바로 하게 하자 · 물 예측이 바닥을 가리키면 아끼자 (다음 정기 회의)
            if (wrong >= 2 && w.Policies["computerask"] == 0) w.Meetings.QueueReview("computerask", 1, $"컴퓨터 자동 조치가 하루에 {wrong}번 틀렸다");
            else if (w.Policies["computerask"] >= 1 && wrong == 0 && Asks.All.Count(p => p.Score == 1 && w.Tick - p.Tick < SimTime.TicksPerDay * 3) >= 3) w.Meetings.QueueReview("computerask", 0, "컴퓨터 제안이 사흘 동안 늘 맞았다");
            if (Active(ComputerModule.WaterPlan) && Apps.WaterEmptyDay is int d && d >= 0 && d <= 12 && w.Policies["water"] == 0) w.Meetings.QueueReview("water", 1, $"30일 물 예측 — {d}일 뒤 물탱크가 바닥난다");
            w.Log.Add(w.Tick, LogKind.Ship, rep.Text);
            Speak.Announce(Voice.Style($"어제 보고 — {string.Join(" · ", rep.Text.Split(": ").Skip(1))}"), null, 0);
        }
    }

    /// <summary>모듈이 올라가 있고 연산 자원이 모자라 잠시 끄지 않았다.</summary>
    public bool Active(ComputerModule m) => Has(m) && !Suspended.Contains(m);

    /// <summary>상시 카드의 "지금 하는 일" 한 줄.</summary>
    public string NowLine
    {
        get
        {
            var w = _world;
            if (!Present) return "주 컴퓨터가 없는 배 — 자동 회로만";
            if (Rebooting) return $"재부팅 중 · {(RebootUntil - w.Tick) / (float)SimTime.Minutes(1):0.0}분 · 사람이 손으로" + (RebootWhy != "" ? $" ({RebootWhy})" : "");
            if (!MainOnline) return BackupActive ? "멎음 — 예비 제어기가 격벽·댐퍼·경보만" : "멎음 — 모든 자동화를 사람이 손으로";
            foreach (var fc in FireCases.OrderByDescending(f => f.Stage == 2 ? 3 : f.Stage == 1 ? 2 : f.Stage == 3 ? 1 : 0).ThenBy(f => f.RoomId))
            {
                var room = w.Ship.Rooms[fc.RoomId];
                string damper = room.VentOpen ? (room.DamperJammed || room.DamperStuck ? "댐퍼 걸림" : "댐퍼 열림") : "댐퍼 폐쇄";
                string step = fc.Stage switch
                {
                    0 => fc.Status.StartsWith("소화조") ? "소화조가 끈다" : fc.Status,
                    1 => Asks.Pending($"fire:{room.Id}:{fc.Method}") is Proposal p
                        ? $"{(fc.Method == "vacuum" ? "진공" : "질식")} 소화 제안 · 승인 기다림 {Math.Max(0, (p.Deadline - w.Tick) / (float)SimTime.Minutes(1) * 60f):0}초"
                        : $"대피 기다림 {Seconds(fc):0}초",
                    2 => fc.Method == "vacuum" ? $"진공 소화 중 {room.Air.Pressure:0}kPa" : $"질식 소화 중 산소 {room.Air.O2:0.0}",
                    _ => $"다시 가압 {room.Air.Pressure:0}kPa",
                };
                return $"{room.Name} 화재 · {damper} · {step}";
            }
            if (ZoneActive) return $"공기 구역 · {ZoneNote}";
            foreach (var r in w.Ship.Rooms)
                if (r.LockPendingUntil >= 0) return $"{r.Name} 감압 · 격벽 폐쇄까지 {(r.LockPendingUntil - w.Tick) / (float)SimTime.Minutes(1) * 60f:0}초 · 대피 기다림";
            if (Asks.Open.FirstOrDefault() is Proposal q) return $"제안 · {q.Title} · {Math.Max(0, (q.Deadline - w.Tick) / (float)SimTime.Minutes(1)):0.0}분 안에";
            if (Checks.Count > 0) return $"{w.Ship.Rooms[Checks[0].RoomId].Name} 사람 확인 요청 · {Checks[0].Why}";
            if (Book.Acts.LastOrDefault() is ComputerAct a && w.Tick - a.Tick < SimTime.Minutes(5))
                return $"{(a.RoomId >= 0 ? w.Ship.Rooms[a.RoomId].Name + " · " : "")}{(a.Act != "" ? a.Act : a.Observe)}";
            if (Suspended.Count > 0) return $"부하 {Load * 100:0}% · 모듈 {Suspended.Count}개 잠시 끔";
            return $"평시 감시 · 모듈 {_modules.Count}개 · 부하 {Load * 100:0}%";
        }
    }

    /// <summary>대피 기다림: 카운트다운이면 남은 초, 아니면 기다린 초.</summary>
    private float Seconds(FireCase fc)
    {
        var w = _world;
        if (fc.ExecAt > w.Tick && Policy(fc.Method) >= 2) return (fc.ExecAt - w.Tick) / (float)SimTime.Minutes(1) * 60f;
        return (w.Tick - fc.PlannedAt) / (float)SimTime.Minutes(1) * 60f;
    }
}
