using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.16 주컴퓨터 두뇌 2.0 — 배 전체 계획자 (게임의 중심: 컴퓨터가 배를 읽고 · 판단하고 · 사람에게 말한다).
//  자원 계획: 앞날 예측(ShipForecast)으로 자원마다 유지 · 주의 · 대책 · 위기를 정하고, 대책을 고른다 (배운 대로 — 같은 상황에 받아들여진 대책부터).
//    대책은 권한(ComputerAuthority) 안에서만 직접 한다: 자동 실행 → 바로 하고 방송 · 제안 → 제안 카드(함장 · 플레이어) · 조언 → 방송만.
//    원정 · 엄격한 절수처럼 모두에게 걸린 일은 늘 회의 안건 (회의가 반대하면 근거를 바꿔 다시 — 세 번까지 · 반대한 사람에게 설명).
//  일정: 고장 위험이 높은 설비 정비를 솜씨 있고 덜 지친 사람에게 부탁 · 지친 사람은 쉬라고 부탁 · 당번표에서 지칠 사람을 바꾼다 · 의심스러운 계기 교정.
//  위기 대응 계획: 소화조(자격 · 기력 · 솜씨) · 대피처를 미리 정해 두고, 사람이 지치거나 다치면 고친다 — 불이 나면 방송하고 소화조에게 부탁한다.
//  상황이 바뀌면(설비 고장 · 복구 · 사람이 쓰러짐 · 재부팅) 바로 다시 세우고, 고친 까닭을 남긴다 ("계획 고침: 물 유지 → 대책 — 물 재생기 고장").
//  성능: 두 시간마다 (부하가 넘치면 네 시간) · 변화는 15분마다 지문으로만 본다.

public sealed class ResourcePlan
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Mode { get; set; } = "유지";
    public string Option { get; set; } = "";
    public string Action { get; set; } = "지켜본다";
    public string Why { get; set; } = "";
    public string Status { get; set; } = "";
    public long Since { get; set; }
    public long Acted { get; set; } = -1;
    public int Revisions { get; set; }
    /// <summary>계획자가 바꾼 방침 (되돌릴 때): 방침 · 바꾸기 전 값.</summary>
    public (string id, int before)? Applied { get; set; }
}

public sealed record PlanRevision(long Tick, string Key, string From, string To, string Why);
public sealed record ScheduleItem(string Kind, string What, int CrewId, string Why, long Tick);

/// <summary>제안 · 안건 한 번 (협상의 한 수): 대책 · 근거 갈래(예측 · 사례 · 대안 · 가치) · 몇 번째 · 어디서(회의 · 함장 · 자동 · 조언) · 결과.</summary>
public sealed class Pitch
{
    public int Id { get; init; }
    public string Key { get; init; } = "";
    public string Option { get; init; } = "";
    public string Arg { get; init; } = "";
    public string Basis { get; init; } = "";
    public string Effect { get; init; } = "";
    public int Attempt { get; init; }
    public string Via { get; init; } = "";
    public string State { get; set; } = "기다림";
    public long Tick { get; init; }
    public long DecidedAt { get; set; } = -1;
    public int Yes { get; set; }
    public int No { get; set; }
    public int ProposalId { get; set; } = -1;
    public float Confidence { get; init; }
    public float BelievedDays { get; init; }
    public float TrueDays { get; init; }
    public List<(int id, string why)> Objectors { get; } = new();
    public bool Open => State == "기다림";
    /// <summary>채점 결과 (한 번만 — 기록 둘이 같은 제안을 본다).</summary>
    public (int, string)? Grade { get; set; }
}

public sealed class OptionStat { public int Tries, Accepted, Rejected, Right, Wrong; }

public sealed class CrisisPlan
{
    public List<int> FireTeam { get; } = new();
    public string Shelter { get; set; } = "";
    public long Revised { get; set; } = -1;
    public string Why { get; set; } = "";
    public int Revisions { get; set; }
    public int AnnouncedFire { get; set; } = -1;
}

public sealed class ShipPlanner
{
    private readonly World _w;
    private long _next, _sigNext, _dirtyAt;
    private int _sig;
    private bool _dirty, _wasDown;
    private string _cause = "";
    private readonly HashSet<int> _stopped = new();
    private int _pitchNext = 1;
    private readonly Dictionary<string, long> _cool = new();
    public List<ResourcePlan> Plans { get; } = new();
    public List<PlanRevision> Revisions { get; } = new();
    public List<ScheduleItem> Schedule { get; } = new();
    public List<Pitch> Pitches { get; } = new();
    public CrisisPlan Emergency { get; } = new();
    public Dictionary<string, Dictionary<string, OptionStat>> Strategy { get; } = new();
    public int Replans, Applied, Proposed, Motions, Advised, Explanations, Reverts;
    /// <summary>협상 중인 안건 · 제안이 있다 (연산 부하).</summary>
    public bool Busy { get; private set; }
    /// <summary>방송을 듣고 믿어 스스로 물을 아끼는 사람 몫 (WaterSystem 훅 — 1이면 그대로).</summary>
    public float HeedWaterMul { get; private set; } = 1f;

    public const int MaxAttempts = 3;

    public ShipPlanner(World w) => _w = w;

    public ResourcePlan? PlanOf(string key) => Plans.FirstOrDefault(p => p.Key == key);
    public Pitch? OpenPitch(string key) => Pitches.LastOrDefault(p => p.Key == key && p.Open);
    public string NowLine => Plans.Where(p => p.Mode != "유지").OrderByDescending(p => Rank(p.Mode)).Select(p => $"{p.Name} {p.Mode} — {p.Action}").FirstOrDefault()
                             ?? (Plans.Count > 0 ? "모든 자원 유지 — 지켜본다" : "계획 세우는 중");

    private static int Rank(string mode) => mode switch { "위기" => 3, "대책" => 2, "주의" => 1, _ => 0 };

    /// <summary>시스템 틱마다 (가볍게): 변화 지문 → 다시 세운다 · 아니면 두 시간마다.</summary>
    public void Update()
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present) return;
        if (!a.MainOnline) { _wasDown = true; return; }
        if (_wasDown) { _wasDown = false; _dirty = true; _cause = "재부팅 뒤 다시 세운다"; }
        if (w.Tick >= _sigNext)
        {
            _sigNext = w.Tick + SimTime.Minutes(15);
            int sig = Signature(out string cause);
            // 바뀐 걸 알면 한 틱 뒤에 다시 세운다 (설비가 멈춘 뒤의 흐름을 읽도록)
            if (sig != _sig) { if (_sig != 0 && cause != "") { _dirty = true; _cause = cause; _dirtyAt = w.Tick + World.SystemInterval; } _sig = sig; }
        }
        if (_dirty ? w.Tick < _dirtyAt : w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(a.Load > 0.95f ? 4f : 2f); // 연산이 넘치면 계획을 덜 자주 (부하 ↔ 계획)
        string why = _dirty ? _cause : "";
        _dirty = false;
        _cause = "";
        Replans++;
        a.Outlook.Update(force: why != "");
        a.CrewModel.Update(force: why != "");
        PlanResources(why);
        FollowPitches();
        PlanSchedule();
        PlanCrisis(why);
        Heed();
        Busy = Pitches.Any(p => p.Open);
    }

    /// <summary>바꾸면 계획을 고쳐야 할 것들의 지문 (멈춘 설비 · 일할 수 있는 사람 · 불 · 새는 방 · 물 방침).</summary>
    private int Signature(out string cause)
    {
        var w = _w;
        var now = new HashSet<int>();
        var news = new List<string>();
        foreach (var m in w.Ship.Machines)
        {
            if (m.Body.Room.Detached || !(m.Stopped || m.Spec.PowerDraw > 0f && !m.Powered)) continue;
            if (m.Body.Type is not (FurnitureType.WaterRecycler or FurnitureType.OxygenGenerator or FurnitureType.GrowBed or FurnitureType.Battery or FurnitureType.Fridge or FurnitureType.ReactorCore)) continue;
            now.Add(m.Body.Id);
            if (!_stopped.Contains(m.Body.Id) && m.Body.Room.DataLinked) news.Add($"{m.Name} 고장 ({m.Body.Room.Name})");
        }
        foreach (var id in _stopped) if (!now.Contains(id) && w.Ship.Machines.FirstOrDefault(x => x.Body.Id == id) is Machine back) news.Add($"{back.Name} 복구");
        _stopped.Clear();
        foreach (var id in now) _stopped.Add(id);
        int able = 0, down = 0;
        foreach (var c in w.Crew) { if (c.Dead) continue; if (c.CanAct) able++; else if (c.Down) down++; }
        int h = now.Count * 7919 + able * 131 + down * 17 + (w.Automation.FireCases.Count > 0 ? 3 : 0) + w.Policies["water"] * 100003 + w.Ship.Rooms.Count(r => r.Leaking) * 5;
        if (news.Count == 0 && h != _sig && _sig != 0) news.Add(down > 0 ? $"쓰러진 사람 {down}명" : "배 상태가 바뀌었다");
        cause = string.Join(" · ", news.Take(3));
        return h == 0 ? 1 : h;
    }

    // ═══════════════════════════════ 자원 계획 ═══════════════════════════════

    private static string ModeOf(ResourceForecast f, float k = 1f) => f.Estimate <= f.Short || f.DaysToShort <= 1.5f * k ? "위기" : f.DaysToShort <= 4f * k ? "대책" : f.DaysToShort <= 8f * k ? "주의" : "유지";

    private void PlanResources(string cause)
    {
        var w = _w;
        var a = w.Automation;
        foreach (var m in ShipForecast.Models)
        {
            var f = a.Outlook.Get(m.Key);
            if (f == null) continue;
            var plan = PlanOf(m.Key);
            if (plan == null) { plan = new ResourcePlan { Key = m.Key, Name = m.Name, Since = w.Tick }; Plans.Add(plan); }
            string mode = ModeOf(f);
            if (Rank(mode) < Rank(plan.Mode) && Rank(ModeOf(f, 1.25f)) >= Rank(plan.Mode)) mode = plan.Mode; // 나아질 때는 여유를 두고 (문턱에서 오락가락하지 않게)
            // 확신이 아주 낮으면 대책 대신 주의 (계기를 먼저 맞춘다)
            if (mode is "대책" && f.Confidence < 0.2f || Rank(mode) >= 2 && f.History.Count < 3) mode = "주의"; // 잰 지 얼마 안 됐다
            if (mode != plan.Mode)
            {
                string why = (cause != "" ? cause + " — " : "") + f.Line;
                Revise(plan, mode, why);
                if (Rank(mode) < 2) plan.Acted = -1;
            }
            plan.Why = f.Line;
            if (Rank(mode) >= 2) Act(plan, m, f);
            else
            {
                plan.Action = mode == "주의" ? "지켜본다 · 계기 확인" : "지켜본다";
                if (mode == "유지" && plan.Applied is (string pid, int before) && w.Policies[pid] != before) Lift(plan, pid, before, f);
                foreach (var p in Pitches.Where(p => p.Key == m.Key && p.Open && p.Via == "회의")) p.State = "철회";
            }
        }
    }

    private void Revise(ResourcePlan plan, string to, string why)
    {
        var w = _w;
        var r = new PlanRevision(w.Tick, plan.Key, plan.Mode, to, why);
        Revisions.Add(r);
        if (Revisions.Count > 40) Revisions.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call} 계획 고침 — {plan.Name} {plan.Mode} → {to} · {why}");
        plan.Mode = to;
        plan.Since = w.Tick;
        plan.Revisions++;
        plan.Status = "";
    }

    /// <summary>대책 후보 (자원마다) · 회의가 정해야 하는 대책.</summary>
    private List<string> Options(string key, ResourceForecast f)
    {
        var w = _w;
        bool canTrip = w.Expedition.Current == null && w.Expedition.Pending == null && w.Policies["expedition"] != 2 && w.Expedition.ComputerPick != null;
        var o = new List<string>();
        switch (key)
        {
            case "water":
                if (canTrip) o.Add("원정");
                if (w.Policies["water"] < 1) o.Add("절수");
                if (w.Policies["water"] < 2 && (ModeOf(f) == "위기" || w.Policies["water"] >= 1)) o.Add("엄격 절수");
                break;
            case "food":
                if (w.Policies["rations"] != 3) o.Add("배급");
                if (canTrip) o.Add("원정");
                break;
            case "o2": o.Add("산소 점검"); break;
            case "power": o.Add("절전"); break;
            case "materials":
                if (canTrip && w.Expedition.Low(MatCat.Repair)) o.Add("원정");
                break;
            case "propellant":
                if (canTrip && w.Expedition.Low(MatCat.Fuel)) o.Add("원정");
                break;
        }
        return o;
    }

    /// <summary>모두의 생활에 걸린 대책 (권한이 제안이면 회의에 올린다).</summary>
    public static bool Everyone(string option) => option is "원정" or "엄격 절수" or "절수" or "배급";

    /// <summary>바로 다시 세운다 (시험 · 큰 변화).</summary>
    public void Force(string why) { _dirty = true; _cause = why; _dirtyAt = 0; }

    private OptionStat Stat(string situation, string option)
    {
        if (!Strategy.TryGetValue(situation, out var d)) Strategy[situation] = d = new Dictionary<string, OptionStat>();
        return d.TryGetValue(option, out var s) ? s : d[option] = new OptionStat();
    }

    /// <summary>배운 대로 고른다: 받아들여진 비율 · 맞은 비율 + 지금 사정에 맞는 정도 (같은 상황 · 다른 조치).</summary>
    private string Choose(string key, List<string> options, ResourceForecast f)
    {
        var w = _w;
        string situation = key + ":short";
        string best = options[0];
        float bs = float.MinValue;
        bool broken = key == "water" && w.Ship.FurnitureOf(FurnitureType.WaterRecycler).All(x => x.Machine is { Stopped: true } || x.Machine is { Efficiency: < 0.2f });
        foreach (var o in options)
        {
            var s = Stat(situation, o);
            float prior = o switch
            {
                "원정" => broken && f.DaysToShort <= 4f ? 0.2f : -0.1f, // 재생기가 멎으면 아껴도 줄기만 한다 — 가져와야 한다
                "절수" => 0.1f,
                "엄격 절수" => ModeOf(f) == "위기" ? (w.Automation.ShipFirst ? 0.3f : 0.12f) : -0.2f,
                _ => 0.1f,
            };
            float score = prior + 0.5f * (s.Accepted + 1f) / (s.Accepted + s.Rejected + 2f) + 0.4f * (s.Right + 1f) / (s.Right + s.Wrong + 2f);
            if (score > bs) { bs = score; best = o; }
        }
        return best;
    }

    /// <summary>대책: 협상 중이면 기다리고 · 아니면 다음 수를 둔다 (권한 안에서).</summary>
    private void Act(ResourcePlan plan, ResourceModel m, ResourceForecast f)
    {
        var w = _w;
        var a = w.Automation;
        if (plan.Acted >= 0 && w.Tick - plan.Acted < SimTime.TicksPerDay) return; // 이미 대책을 했다 (하루는 지켜본다)
        if (OpenPitch(m.Key) is Pitch open)
        {
            // 위기인데 회의가 아직이면 함장에게 서두른다
            if (open.Via == "회의" && ModeOf(f) == "위기" && w.Tick - open.Tick > SimTime.Hours(6)) { open.State = "철회"; _cool.Remove(m.Key); }
            else return;
        }
        if (_cool.TryGetValue(m.Key, out var until) && w.Tick < until) return;
        var tries = Pitches.Where(p => p.Key == m.Key && p.Via != "자동" && w.Tick - p.Tick < SimTime.TicksPerDay * 3).ToList(); // 조른 횟수 — 권한 안에서 직접 한 일은 "말씀드린" 게 아니다
        if (tries.Count >= MaxAttempts)
        {
            if (plan.Status != "포기")
            {
                plan.Status = "포기";
                plan.Action = "더 조르지 않는다 — 회의 뜻을 따르고 지켜본다";
                a.Speak.Announce(a.Authority.Say($"{m.Name} 대책은 세 번 말씀드렸다. 회의 뜻을 따르겠다 — {f.Line}"), null, 0);
                a.Authority.Learned("협상", $"{m.Name}: 세 번 거절 — 더 조르지 않는다");
            }
            return;
        }
        var options = Options(m.Key, f);
        if (options.Count == 0) { plan.Action = m.Key switch { "water" => "이미 아끼는 중 — 지켜본다", "food" => "이미 배급 중 — 지켜본다", _ => "할 수 있는 대책이 없다 — 지켜본다" }; return; }
        var last = tries.LastOrDefault();
        // 바로 전에 거절된 대책은 한 번 쉰다 (다른 대책이 있으면)
        if (last != null && last.State is "부결" or "거절" && options.Count > 1 && options.Contains(last.Option) && Stat(m.Key + ":short", last.Option).Rejected > Stat(m.Key + ":short", last.Option).Accepted)
            options.Remove(last.Option);
        string option = Choose(m.Key, options, f);
        int attempt = tries.Count + 1;
        string arg = attempt == 1 ? "예측" : last != null && last.Option != option ? "대안" : attempt == 2 ? "사례" : "가치";
        MakePitch(plan, m, f, option, arg, attempt, last);
    }

    private string Effect(string key, string option, ResourceForecast f)
    {
        var w = _w;
        switch (option)
        {
            case "절수":
            case "엄격 절수":
            {
                float mul = option == "절수" ? 0.75f : 0.55f;
                float crewUse = w.Crew.Count(c => !c.Dead) * WaterSystem.CrewLitersPerHour;
                float rate = f.Rate + crewUse * (1f - mul);
                float d2 = rate >= -1e-4f ? 99f : (f.Estimate - f.Short) / -rate / 24f;
                return d2 >= 30f ? $"물이 줄지 않는다 (하루 {rate * 24f:+0;-0}L)" : $"부족이 {f.DaysToShort:0.0}일 → {d2:0.0}일 뒤로";
            }
            case "원정": return $"{w.Expedition.ComputerPick?.Name ?? "가까운 곳"}에서 {(key == "water" ? "얼음" : key == "food" ? "식량" : key == "propellant" ? "연료" : "재료")}을 가져온다 (위험 {(w.Expedition.ComputerPick?.Risk ?? 0f) * 100:0}%)";
            case "배급": return "하루 먹는 양을 줄여 버티는 날을 늘린다";
            case "산소 점검": return "산소 발생기 · 공기 순환을 먼저 본다";
            case "절전": return "덜 급한 설비 · 오락부터 줄인다";
            default: return "";
        }
    }

    /// <summary>근거 갈래마다 다른 말: 예측(숫자) · 사례(지난 일 · 맞힌 비율) · 대안(다른 대책) · 가치(반대한 사람의 마음에 맞춰).</summary>
    private string Basis(ResourceModel m, ResourceForecast f, string option, string arg, Pitch? last)
    {
        var w = _w;
        var a = w.Automation;
        switch (arg)
        {
            case "사례":
            {
                var hits = a.Outlook.Warnings.Count(x => x.Score == 1);
                var graded = a.Outlook.Warnings.Count(x => x.Score != 0);
                var past = w.History.Events.LastOrDefault(e => e.Text.Contains(m.Name) && (e.Text.Contains("바닥") || e.Text.Contains("부족") || e.Text.Contains("모자")));
                string story = past != null ? $"{SimTime.Day(past.Tick)}일에 겪었다 — \"{(past.Text.Length > 30 ? past.Text[..30] + "…" : past.Text)}\"" :
                    m.Key == "water" ? "다른 배 기록: 물이 바닥나면 이틀 안에 탈수 · 재배대가 마른다" : $"다른 배 기록: {Ko.IGa(m.Name)} 바닥나면 손쓸 틈이 없다";
                return $"{story} · 지난 예측 {hits}/{Math.Max(graded, hits)} 맞힘 · 지금 {f.Line}";
            }
            case "대안":
                return $"{(last != null ? $"{Ko.IGa(last.Option)} 싫다면 " : "")}{option} — 위험 없이 {Effect(m.Key, option, f)} · {f.Line}";
            case "가치":
            {
                var values = last?.Objectors.Select(o => w.Crew.FirstOrDefault(c => c.Id == o.id)).Where(c => c != null).GroupBy(c => c!.Value).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList() ?? new();
                var v = values.Count > 0 ? values[0] : CrewValue.Safety;
                string pitch = v switch
                {
                    CrewValue.Safety => "탈수 · 굶주림은 사고를 부른다 — 안전하려면 지금 대비해야 한다",
                    CrewValue.People => "아무도 목마르거나 배고프지 않게 — 지금 조금씩 나누자",
                    CrewValue.Freedom => "줄이는 건 샤워 · 빨래뿐이다 — 나머지는 각자 자유",
                    CrewValue.Rules => "방침에 있는 대로 '아낀다'만 — 절차대로",
                    _ => $"원정보다 싸다 — {Effect(m.Key, option, f)}",
                };
                return $"{Ko.EulReul(MeetingSystem.ValueName(v))} 아끼는 분들께: {pitch} · {f.Line}";
            }
            default:
                return $"{f.Line} · {f.Basis}";
        }
    }

    private void MakePitch(ResourcePlan plan, ResourceModel m, ResourceForecast f, string option, string arg, int attempt, Pitch? last)
    {
        var w = _w;
        var a = w.Automation;
        var dom = Domain.Resources;
        var level = a.Authority.Level(dom);
        // 권한 안에서만: 조언만 → 말만 · 원정은 늘 회의 · 자동 실행이면 직접 (엄격한 절수는 위기일 때만) · 아니면 모두에게 걸린 일은 회의(시간이 있으면) · 급하면 함장
        bool crisis = ModeOf(f) == "위기";
        string via = level == AuthLevel.Advise ? "조언"
            : option == "원정" ? "회의"
            : level == AuthLevel.Auto && (option != "엄격 절수" || crisis) ? "자동"
            : !crisis && Everyone(option) ? "회의" : "함장";
        float trueDays = m.True(w) <= f.Short ? 0f : m.Flow(w) < -1e-4f ? (m.True(w) - f.Short) / -m.Flow(w) / 24f : 99f;
        var p = new Pitch
        {
            Id = _pitchNext++, Key = m.Key, Option = option, Arg = arg, Attempt = attempt, Via = via, Tick = w.Tick, Confidence = f.Confidence,
            Basis = Basis(m, f, option, arg, last), Effect = Effect(m.Key, option, f), BelievedDays = f.DaysToShort, TrueDays = trueDays,
        };
        Pitches.Add(p);
        if (Pitches.Count > 40) Pitches.RemoveAt(0);
        Stat(m.Key + ":short", option).Tries++;
        plan.Option = option;
        string title = Title(m, option);
        switch (via)
        {
            case "회의":
                Motions++;
                plan.Action = $"{title} — 다음 회의 안건 ({attempt}번째 · {arg})";
                plan.Status = "안건";
                a.Book.Add(ActKind.Proposal, null, f.Line, p.Basis, $"회의 안건: {title}", "회의에서 정해 달라", "brain:motion:" + p.Id, 0, 60f * 24f, (world, act) => p.Open ? null : (2, $"회의 — {p.State}"));
                a.Speak.Announce(a.Authority.Say($"{Ko.EulReul(title)} 다음 회의에 올린다 — {p.Basis}", f.Confidence), null, 1);
                break;
            case "자동":
                Applied++;
                p.State = "실행";
                p.DecidedAt = w.Tick;
                Apply(plan, m, f, option, p, "자동 실행 (권한 안)");
                break;
            case "함장":
            {
                Proposed++;
                plan.Action = $"{title} — 제안 ({attempt}번째 · {arg})";
                plan.Status = "제안";
                var pr = a.Asks.Propose("plan:" + m.Key, "plan", null, title, p.Basis, p.Effect, 20f, null,
                    (world, pp) => world.Automation.Planner.Accepted(p.Id, pp.DecidedBy),
                    (world, pp) => world.Automation.Planner.GradePitch(p));
                p.ProposalId = pr.Id;
                break;
            }
            default:
                Advised++;
                p.State = "조언";
                plan.Action = $"조언: {title} (권한 밖 — 말만 한다)";
                plan.Status = "조언";
                plan.Acted = w.Tick;
                a.Speak.Announce(a.Authority.Say($"조언 — {title}. {p.Basis} (제게는 정할 권한이 없다)", f.Confidence), null, 1);
                a.Book.Add(ActKind.Advice, null, f.Line, p.Basis, $"조언: {title}", "사람이 정한다", "brain:adv:" + m.Key, SimTime.Hours(12), 60f);
                break;
        }
    }

    private static string Title(ResourceModel m, string option) => option switch
    {
        "절수" => "물 아끼기 (방침 물: 아낀다)",
        "엄격 절수" => "엄격한 물 배급 (방침 물: 엄격)",
        "배급" => "식량 배급 줄이기 (방침 식량: 줄인다)",
        "원정" => $"{m.Name} 원정",
        "산소 점검" => "산소 발생기 점검",
        "절전" => "절전 (덜 급한 설비부터)",
        _ => option,
    };

    /// <summary>대책을 실제로 한다 (자동 실행 · 받은 제안 · 통과한 안건).</summary>
    private void Apply(ResourcePlan plan, ResourceModel m, ResourceForecast f, string option, Pitch p, string how)
    {
        var w = _w;
        var a = w.Automation;
        plan.Acted = w.Tick;
        plan.Status = "실행";
        plan.Action = $"{Title(m, option)} — {how}";
        void Policy(string id, int to, string why)
        {
            int before = w.Policies[id];
            if (before == to) return;
            plan.Applied ??= (id, before);
            w.Policies.Set(id, to, why);
        }
        switch (option)
        {
            case "절수": Policy("water", Math.Max(1, w.Policies["water"]), $"주 컴퓨터 계획 — {f.Line}"); break;
            case "엄격 절수":
                Policy("water", 2, $"주 컴퓨터 계획 — {f.Line}");
                a.Authority.Dilemma("배 ↔ 사람", $"{m.Name} {f.Line}", "사람 몫 물을 줄이고 재배대 · 탱크를 지킨다", a.ShipFirst ? "방침 '배 우선'" : "회의가 정했다", w.Crew.Where(c => !c.Dead));
                break;
            case "배급": Policy("rations", 3, $"주 컴퓨터 계획 — {f.Line}"); break;
            case "원정":
            {
                var cat = m.Key switch { "water" => MatCat.Water, "food" => MatCat.Food, "propellant" => MatCat.Fuel, _ => MatCat.Repair };
                if (!w.Expedition.ComputerRequest(cat, true)) { plan.Action = $"{m.Name} 원정 — 지금은 보낼 수 없다 (다른 원정 중)"; plan.Acted = -1; }
                break;
            }
            case "산소 점검":
            {
                var gen = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Select(x => x.Machine!).Where(x => !x.Body.Room.Detached).OrderBy(x => x.Efficiency).ThenBy(x => x.Body.Id).FirstOrDefault();
                var o = gen == null ? null : w.Board.Open.FirstOrDefault(x => x.Target.Furniture == gen.Body);
                if (o != null && a.CrewModel.Best(o.Skill) is CrewMember who) a.CrewModel.Ask(who, o, $"산소 예측 — {f.Line}");
                break;
            }
            case "절전":
                a.Authority.Dilemma("배 ↔ 사람", $"배터리 {f.Line}", a.ShipFirst ? "오락 · 침실 조명부터 끈다 (배 설비를 지킨다)" : "엔진 대기 전력부터 줄인다 (사람 공간을 지킨다)",
                    a.ShipFirst ? "방침 '배 우선'" : "방침 '사람 우선'", w.Crew.Where(c => !c.Dead));
                break;
        }
        // 들은 사람만 안다 · 채점은 하루 뒤 (실제로 필요했나)
        a.Speak.Announce(a.Authority.Say($"{Title(m, option)} — {how}. {f.Line}"), null, 1);
        a.Book.Add(ActKind.Module, null, f.Line, p.Basis, $"{Title(m, option)} ({how})", "따라 달라", "brain:act:" + p.Id, 0, 60f * 6f, (world, act) => world.Automation.Planner.GradePitch(p));
    }

    /// <summary>계획자가 바꾼 방침을 되돌린다 (자원 사정이 나아졌다).</summary>
    private void Lift(ResourcePlan plan, string id, int before, ResourceForecast f)
    {
        var w = _w;
        var a = w.Automation;
        if (a.Authority.Level(Domain.Resources) == AuthLevel.Auto)
        {
            w.Policies.Set(id, before, $"주 컴퓨터 — {plan.Name} 사정이 나아졌다");
            Reverts++;
            plan.Applied = null;
            a.Speak.Announce(a.Authority.Say($"{plan.Name} 사정이 나아졌다 — {PolicySystem.Spec(id).Name} 방침을 되돌린다. {f.Line}"), null, 0);
        }
        else if (a.Asks.Pending("lift:" + id) == null && (a.Asks.Latest("lift:" + id) is not Proposal old || w.Tick - old.Tick > SimTime.Hours(12)))
        {
            a.Asks.Propose("lift:" + id, "plan", null, $"{PolicySystem.Spec(id).Name} 방침 되돌리기", f.Line, "불편을 걷는다", 20f, null,
                (world, pp) => { world.Policies.Set(id, before, "주 컴퓨터 제안 — 사정이 나아졌다"); plan.Applied = null; world.Automation.Planner.Reverts++; });
        }
    }

    // ═══════════════════════════════ 협상 ═══════════════════════════════

    /// <summary>제안 카드를 받았다 (함장 · 플레이어).</summary>
    internal void Accepted(int pitchId, string by)
    {
        var w = _w;
        var p = Pitches.FirstOrDefault(x => x.Id == pitchId);
        if (p == null || !p.Open) return;
        p.State = "받음";
        p.DecidedAt = w.Tick;
        Stat(p.Key + ":short", p.Option).Accepted++;
        var m = ShipForecast.Models.First(x => x.Key == p.Key);
        var plan = PlanOf(p.Key)!;
        var f = w.Automation.Outlook.Get(p.Key);
        if (f != null) Apply(plan, m, f, p.Option, p, $"{Ko.IGa(by)} 받았다");
    }

    /// <summary>회의 결과 (ComputerAuthority.Agenda).</summary>
    internal void Decided(Pitch p, bool pass, int yes, int no, List<(int id, string why)> objectors, string chair)
    {
        var w = _w;
        var a = w.Automation;
        p.State = pass ? "통과" : "부결";
        p.DecidedAt = w.Tick;
        p.Yes = yes; p.No = no;
        p.Objectors.AddRange(objectors);
        var st = Stat(p.Key + ":short", p.Option);
        foreach (var (id, _) in objectors) if (w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c) a.CrewModel.Of(c).Rejects++;
        var m = ShipForecast.Models.First(x => x.Key == p.Key);
        var plan = PlanOf(p.Key)!;
        if (pass)
        {
            st.Accepted++;
            if (a.Outlook.Get(p.Key) is ResourceForecast f) Apply(plan, m, f, p.Option, p, $"회의가 정했다 (찬성 {yes} · 반대 {no})");
            if (p.Attempt >= 2) a.Authority.Learned("협상", $"{m.Name}: {p.Arg} 근거로 {p.Attempt}번째에 설득 — \"{p.Option}\"");
        }
        else Rejected(p, plan, m, $"회의 부결 (찬성 {yes} · 반대 {no})");
    }

    /// <summary>거절됐다: 배우고 · 반대한 사람에게 설명하고 · 쉬었다가 근거를 바꿔 다시.</summary>
    private void Rejected(Pitch p, ResourcePlan plan, ResourceModel m, string how)
    {
        var w = _w;
        var a = w.Automation;
        var st = Stat(p.Key + ":short", p.Option);
        st.Rejected++;
        plan.Status = "거절됨";
        plan.Action = $"{Title(m, p.Option)} — {how} · 근거를 바꿔 다시 ({p.Attempt}/{MaxAttempts})";
        _cool[p.Key] = w.Tick + SimTime.Hours(p.Via == "회의" ? 1f : 3f);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {Title(m, p.Option)} — {how}. 받아들인다 · 반대한 까닭을 듣고 다시 생각한다");
        if (st.Rejected == 2 && st.Accepted == 0) a.Authority.Learned("협상", $"{m.Name} 부족엔 \"{p.Option}\" 대책이 잘 안 받아들여진다 — 다음엔 다른 대책부터");
        foreach (var (id, why) in p.Objectors.Take(4)) if (w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c && !c.Dead) Explain(c, p, m, why);
    }

    /// <summary>반대한 사람에게 설명 (개인 메시지) — 그 사람의 까닭에 맞춰 말하면 조금 믿는다.</summary>
    private void Explain(CrewMember c, Pitch p, ResourceModel m, string why)
    {
        var w = _w;
        var a = w.Automation;
        var f = a.Outlook.Get(m.Key);
        why = why.StartsWith("거절 — ") ? why[5..] : why;
        string text = $"{c.Name}님 — \"{why}\" 맞는 말이다. {Ko.EunNeun(Title(m, p.Option))} 거두겠다. 다만 {f?.Line ?? $"{Ko.IGa(m.Name)} 모자라다"}. 다른 방법을 다시 가져오겠다";
        a.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "설명", text));
        var prof = a.CrewModel.Of(c);
        prof.Explained++;
        prof.LastExplained = w.Tick;
        Explanations++;
        bool fits = why.Contains("위험") && c.Value == CrewValue.Safety || why.Contains("자유") || why.Contains("사람") && c.Value == CrewValue.People;
        a.Trusts.Change(c, fits ? 0.02f : 0.01f, "컴퓨터가 반대한 까닭을 듣고 설명했다", quiet: true);
        if (prof.Explained == 1) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, $"컴퓨터가 따로 설명하더라 — {m.Name} 이야기"), c.Id);
    }

    /// <summary>열린 제안 카드를 본다 (거절 · 기한 지남).</summary>
    private void FollowPitches()
    {
        var w = _w;
        var a = w.Automation;
        foreach (var p in Pitches.Where(x => x.Open && x.Via == "함장").ToList())
        {
            var pr = a.Asks.All.FirstOrDefault(x => x.Id == p.ProposalId);
            if (pr == null) { p.State = "철회"; continue; }
            if (pr.State == ProposalState.Pending || pr.Accepted) continue;
            p.State = "거절";
            p.DecidedAt = w.Tick;
            if (w.Crew.FirstOrDefault(c => c.Name == pr.DecidedBy) is CrewMember boss) p.Objectors.Add((boss.Id, pr.DecideWhy));
            var m = ShipForecast.Models.First(x => x.Key == p.Key);
            Rejected(p, PlanOf(p.Key)!, m, $"{Ko.IGa(pr.DecidedBy)} 거절했다");
        }
    }

    /// <summary>실행한 대책을 채점 (실제로 필요했나 — 세계로): 컴퓨터가 믿은 날수 ↔ 실제 날수.</summary>
    internal (int, string)? GradePitch(Pitch p)
    {
        var w = _w;
        if (p.State is "기다림" or "거절" or "부결" or "철회") return p.Open ? null : (2, "실행하지 않았다");
        if (w.Tick - p.DecidedAt < SimTime.Hours(6)) return null;
        if (p.Grade is (int, string) done) return done;
        p.Grade = GradeNow(p);
        return p.Grade;
    }

    private (int, string) GradeNow(Pitch p)
    {
        var w = _w;
        var a = w.Automation;
        var st = Stat(p.Key + ":short", p.Option);
        var m = ShipForecast.Models.First(x => x.Key == p.Key);
        if (p.TrueDays >= MathF.Max(4f, 2.2f * p.BelievedDays) && p.TrueDays > p.BelievedDays + 1.5f)
        {
            st.Wrong++;
            var g = m.Gauge(w);
            bool stuck = g != null && a.Belief.Of(g).Fault == SensorFault.Stuck;
            string cause = stuck ? $"{g!.Name} 계기 값이 멈춰 있었다" : a.Outlook.Ledger(p.Key).Disagree > 0 ? $"{g?.Name ?? "배"} 계기가 틀어져 있었다" : "예측이 틀렸다";
            var plan = PlanOf(p.Key);
            a.Authority.Admit("act:" + p.Id, Domain.Resources, $"{Ko.EulReul(Title(m, p.Option))} 괜히 했다 — 실제로는 {Ko.IGa(m.Name)} {p.TrueDays:0.#}일 치 있었다 (믿은 값 {p.BelievedDays:0.#}일)", cause,
                stuck ? "그 방 계기를 사람이 확인하게 하고, 방침을 되돌린다" : "방침을 되돌리고 계기를 다시 맞춘다", w.Crew.Where(c => !c.Dead && !c.IsChild),
                () =>
                {
                    if (plan?.Applied is (string id, int before)) { w.Policies.Set(id, before, "주 컴퓨터 — 잘못을 인정하고 되돌렸다"); plan.Applied = null; Reverts++; }
                    if (stuck && g != null) a.RequestCheck(g, $"{m.Name} 계기 값이 멈춘 것 같다 — 눈금을 직접 봐 달라");
                    if (plan != null) { plan.Acted = -1; plan.Status = "되돌림"; }
                });
            return (-1, $"틀렸다 — 실제 {p.TrueDays:0.#}일 치 (믿은 값 {p.BelievedDays:0.#}일) · {cause}");
        }
        st.Right++;
        return (1, $"맞았다 — 실제 {MathF.Min(p.TrueDays, 99f):0.#}일 치였다 (믿은 값 {p.BelievedDays:0.#}일)");
    }

    // ═══════════════════════════════ 일정 ═══════════════════════════════

    private void PlanSchedule()
    {
        var w = _w;
        var a = w.Automation;
        var cm = a.CrewModel;
        Schedule.Clear();
        var level = a.Authority.Level(Domain.Maintenance);
        bool calm = a.FireCases.Count == 0 && !w.Command.Active && Crisis.Level(w) < CrisisLevel.Emergency; // 위기엔 정비 부탁을 하지 않는다 (지휘 · 대응 수순이 먼저)
        // 고장 위험 → 정비 부탁 (솜씨 있고 덜 지친 사람)
        foreach (var risk in a.Outlook.Risks)
        {
            var o = w.Board.Open.FirstOrDefault(x => x.Target.Furniture?.Id == risk.MachineId && x.Kind is WorkKind.Maintain or WorkKind.Repair or WorkKind.PreventiveCheck && x.Assignee == null);
            var who = o != null ? cm.Best(o.Skill) : null;
            Schedule.Add(new ScheduleItem("정비", $"{risk.Machine} ({risk.Room}) — 하루 안 고장 {risk.P24 * 100:0}%", who?.Id ?? -1, risk.Why, w.Tick));
            if (o == null || who == null || !calm) continue;
            if (cm.Asks.TryGetValue(who.Id, out var had) && had.OrderId == o.Id) continue;
            string why = $"하루 안 고장 {risk.P24 * 100:0}% · {risk.Why}";
            if (level == AuthLevel.Auto) cm.Ask(who, o, why);
            else if (level == AuthLevel.Propose && a.Asks.Pending("maint:" + o.Id) == null && a.Asks.Latest("maint:" + o.Id) == null)
            {
                var oo = o; var ww = who;
                a.Asks.Propose("maint:" + o.Id, "plan", o.Target.CurrentRoom, $"정비 맡기기: {o.Title} → {who.Name}", why, "고장 전에 손본다", 30f, null, (world, pp) => world.Automation.CrewModel.Ask(ww, oo, why));
            }
            break; // 한 번에 한 건 (사람을 몰아세우지 않는다)
        }
        // 지친 사람 → 쉬라고 부탁 (일정 권한)
        var sched = a.Authority.Level(Domain.Schedule);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild || !c.CanAct || cm.RestAsked(c) || !cm.Knows(c)) continue;
            float rest = cm.RestNow(c);
            if (rest >= 0.25f) continue;
            if (c.Pose == Pose.Sleeping || c.Job?.Order == null && !ChoresActivity.OnShiftStatic(c, w)) continue; // 이미 쉬는 사람 · 근무 밖에 일 없는 사람은 그냥 둔다
            Schedule.Add(new ScheduleItem("쉼", $"{c.Name} 쉬게 — 기력 {rest * 100:0}%", c.Id, "지친 사람에게 일을 덜 맡긴다", w.Tick));
            if (sched == AuthLevel.Auto) cm.AskRest(c, $"기력 {rest * 100:0}%로 보인다");
            else if (sched == AuthLevel.Propose && a.Asks.Pending("rest:" + c.Id) == null && (a.Asks.Latest("rest:" + c.Id) is not Proposal old || w.Tick - old.Tick > SimTime.TicksPerDay))
            {
                var cc = c;
                a.Asks.Propose("rest:" + c.Id, "plan", c.Room, $"{c.Name} 쉬게 하기", $"기력 {rest * 100:0}%로 보인다 (컴퓨터 짐작)", "급하지 않은 일은 남에게", 30f, null, (world, pp) => world.Automation.CrewModel.AskRest(cc, "함장이 쉬게 했다"));
            }
        }
        // 원정대로 뽑힌 사람: 부탁하지 않고(Best가 뺀다) · 모이는 동안엔 남은 부탁 · 쉼 부탁을 거둔다 (원정 ↔ 일정)
        if (w.Expedition.Current is Trip tg && tg.Phase == TripPhase.Gathering) foreach (var mm in tg.Members) cm.Forget(mm.Id);
        if (w.Expedition.Pending is ExpProposal ep) foreach (var id in ep.Team) Schedule.Add(new ScheduleItem("원정", $"{w.Crew.FirstOrDefault(c => c.Id == id)?.Name} 원정대 — 다른 일은 부탁하지 않는다", id, ep.Site.Name, w.Tick));
        // 당번표: 오늘 당번 중 지칠 사람을 바꾼다
        if (sched != AuthLevel.Advise && a.Active(ComputerModule.Roster))
        {
            int day = SimTime.Day(w.Tick);
            var roster = a.Apps.Roster;
            for (int i = 0; i < roster.Count; i++)
            {
                var s = roster[i];
                if (s.Day != day || w.Crew.FirstOrDefault(c => c.Id == s.CrewId) is not CrewMember c || !cm.Knows(c) || cm.DutyCost(c, s.Duty) < 2.5f) continue;
                var alt = w.Crew.Where(x => !x.Dead && !x.IsChild && x.CanAct && x != c && !roster.Any(r => r.Day == day && r.CrewId == x.Id)).OrderBy(x => cm.DutyCost(x, s.Duty)).ThenBy(x => x.Id).FirstOrDefault();
                if (alt == null || cm.DutyCost(alt, s.Duty) >= 1.5f) continue;
                roster[i] = s with { CrewId = alt.Id };
                a.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "당번", $"오늘 {Ko.EunNeun(s.Duty)} {alt.Name}에게 넘겼다 — 기력이 모자라 보인다"));
                a.Apps.Messages.Add(new PersonalMessage(w.Tick, alt.Id, "당번", $"오늘 {s.Duty} ({s.Time}) — {c.Name} 대신"));
                cm.OnDuty(alt, s.Duty);
                Schedule.Add(new ScheduleItem("당번", $"{s.Duty}: {c.Name} → {alt.Name}", alt.Id, $"{c.Name} 기력 {cm.RestNow(c) * 100:0}%", w.Tick));
                w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 당번 바꿈 — 오늘 {s.Duty} {c.Name} → {alt.Name} ({c.Name} 기력이 모자라 보인다)", alt.Id);
            }
        }
        // 의심스러운 계기 → 교정 (Watch가 교정 작업을 올린다 · 전기 솜씨 있는 사람에게 부탁)
        foreach (var m in ShipForecast.Models)
        {
            var L = a.Outlook.Ledger(m.Key);
            if (!L.Suspect || L.RoomId < 0) continue;
            var room = w.Ship.Rooms[L.RoomId];
            var o = w.Board.Open.FirstOrDefault(x => x.Kind == WorkKind.Calibrate && x.Target.Room == room && x.Assignee == null);
            var who = o != null ? cm.Best(Skill.Electrical) : null;
            Schedule.Add(new ScheduleItem("교정", $"{room.Name} {m.Name} 계기 교정", who?.Id ?? -1, L.LastWhy, w.Tick));
            if (o != null && who != null && calm && level != AuthLevel.Advise && !(cm.Asks.TryGetValue(who.Id, out var had) && had.OrderId == o.Id)) cm.Ask(who, o, $"{m.Name} 계기가 {L.Disagree}번 어긋났다");
        }
        // 원정 일정
        if (w.Expedition.Current is Trip t) Schedule.Add(new ScheduleItem("원정", $"{t.Site.Name} — {t.Phase}", t.Leader, t.Why, w.Tick));
        foreach (var p in Pitches.Where(x => x.Open && x.Via == "회의")) Schedule.Add(new ScheduleItem("회의", $"안건: {p.Option} ({p.Attempt}번째)", -1, p.Basis, w.Tick));
    }

    // ═══════════════════════════════ 위기 대응 계획 ═══════════════════════════════

    private void PlanCrisis(string cause)
    {
        var w = _w;
        var a = w.Automation;
        var cm = a.CrewModel;
        // 소화조: 지금 조원이 아직 할 수 있으면 그대로 두고(계획이 이리저리 흔들리지 않게) · 빈자리만 자격 · 기력 · 용기 · 침착으로 채운다
        bool Fit(CrewMember c) => !c.Dead && !c.IsChild && c.CanAct && !c.Outside && cm.RestNow(c) >= 0.3f && c.Vitals.Injury < 0.4f;
        var team = Emergency.FireTeam.Where(id => w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c && Fit(c)).ToList();
        foreach (var c in w.Crew.Where(c => Fit(c) && !team.Contains(c.Id))
                     .OrderByDescending(c => (Life.HasQual(c, Qual.Firefighting) ? 0.5f : 0f) + 0.4f * cm.RestNow(c) + 0.3f * c.Traits.Bravery + 0.2f * c.Traits.Calm - 0.4f * c.Vitals.Injury)
                     .ThenBy(c => c.Id))
        {
            if (team.Count >= 2) break;
            team.Add(c.Id);
        }
        var shelter = w.Ship.RoomsOf(RoomType.Shelter).FirstOrDefault(r => !r.Leaking && w.Fire.CountIn(r) == 0) ?? w.Ship.RoomsOf(RoomType.Medbay).FirstOrDefault(r => !r.Leaking) ?? w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault();
        string sh = shelter?.Name ?? "가까운 안전한 방";
        if (!team.SequenceEqual(Emergency.FireTeam) || sh != Emergency.Shelter)
        {
            string Names(IEnumerable<int> ids) => string.Join("·", ids.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?"));
            string why = Emergency.FireTeam.Count == 0 ? "처음 세움" :
                string.Join(" · ", Emergency.FireTeam.Where(id => !team.Contains(id)).Select(id => w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c ? $"{c.Name} {(c.CanAct ? $"기력 {cm.RestNow(c) * 100:0}%" : "쓰러짐")}" : "?"));
            if (Emergency.FireTeam.Count > 0)
            {
                Emergency.Revisions++;
                Revisions.Add(new PlanRevision(w.Tick, "crisis", $"소화조 {Names(Emergency.FireTeam)}", $"소화조 {Names(team)}", (cause != "" ? cause + " · " : "") + why));
                if (Revisions.Count > 40) Revisions.RemoveAt(0);
            }
            Emergency.FireTeam.Clear();
            Emergency.FireTeam.AddRange(team);
            Emergency.Shelter = sh;
            Emergency.Revised = w.Tick;
            Emergency.Why = why;
        }
        // 불이 났다: 계획을 방송하고 소화조에게 부탁 (위기 권한 — 자동이면 부탁까지 · 아니면 방송만)
        var fc = a.FireCases.FirstOrDefault();
        if (fc != null && Emergency.AnnouncedFire != fc.RoomId)
        {
            Emergency.AnnouncedFire = fc.RoomId;
            var room = w.Ship.Rooms[fc.RoomId];
            // 현장 지휘가 조를 짰으면 그 조를 따른다 (컴퓨터 계획은 지휘가 없을 때의 밑그림)
            bool led = w.Command.Active;
            var cmdTeam = led ? w.Crew.Where(c => !c.Dead && w.Command.TeamOf(c) is Team t && t.Kind == TeamKind.Fire).Select(c => c.Id).ToList() : Emergency.FireTeam;
            string names = cmdTeam.Count > 0 ? string.Join("·", cmdTeam.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?")) : "지휘가 정한다";
            a.Speak.Announce(a.Authority.Say($"화재 대응 계획 — {room.Name}: 소화조 {names} · 나머지는 {Emergency.Shelter}로"), room, 1);
            if (!led && a.Authority.Level(Domain.Crisis) == AuthLevel.Auto)
                foreach (var id in Emergency.FireTeam)
                    if (w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c && w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Extinguish && o.Target.CurrentRoom == room) is WorkOrder o)
                        cm.Ask(c, o, "화재 대응 계획의 소화조", 1f, crisis: true);
        }
        if (fc == null) Emergency.AnnouncedFire = -1;
    }

    /// <summary>예측 방송을 듣고 컴퓨터를 믿는 사람은 스스로 물을 아낀다 (WaterSystem 훅).</summary>
    private void Heed()
    {
        var w = _w;
        var a = w.Automation;
        var warn = a.Outlook.Warnings.LastOrDefault(x => x.Key == "water" && w.Tick - x.Tick < SimTime.TicksPerDay * 2);
        if (warn == null || warn.Heard.Count == 0) { HeedWaterMul = 1f; return; }
        int live = 0, heed = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            live++;
            if (warn.Heard.Contains(c.Id) && a.Trusts.Of(c) >= 0.5f) heed++;
        }
        HeedWaterMul = live == 0 ? 1f : 1f - 0.15f * heed / live;
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Replans); I(Applied); I(Proposed); I(Motions); I(Advised); I(Explanations); I(Reverts); I(Revisions.Count); I(Pitches.Count); I(Emergency.Revisions); F(HeedWaterMul);
        foreach (var p in Plans) I(Rank(p.Mode));
        foreach (var id in Emergency.FireTeam) I(id);
    }
}

public sealed partial class AutomationSystem
{
    private ShipPlanner? _planner;
    private ShipForecast? _outlook;
    private CrewModelBook? _crewModel;
    private ComputerAuthority? _authority;
    /// <summary>v16.16 배 전체 계획자.</summary>
    public ShipPlanner Planner => _planner ??= new ShipPlanner(_world);
    /// <summary>v16.16 앞날 예측 (며칠 뒤 · 확신 · 계기 믿음).</summary>
    public ShipForecast Outlook => _outlook ??= new ShipForecast(_world);
    /// <summary>v16.16 승무원 개인 모형.</summary>
    public CrewModelBook CrewModel => _crewModel ??= new CrewModelBook(_world);
    /// <summary>v16.16 권한 · 협상 · 윤리 · 책임 · 말투.</summary>
    public ComputerAuthority Authority => _authority ??= new ComputerAuthority(_world);

    /// <summary>시험: 두뇌 2.0을 끄고 견준다.</summary>
    public static bool BrainOff { get; set; }
    /// <summary>두뇌 2.0이 쓴 시간 (지문에 넣지 않는다 · 성능 시험).</summary>
    public static long BrainStopwatchTicks { get; set; }

    /// <summary>v16.16 두뇌 2.0 한 번 (V16 안에서 · 간격을 두고).</summary>
    private void Brain2()
    {
        if (BrainOff || !Present) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_self == null || !_self.Thin("예측")) Outlook.Update(); // v16.26 ⑥ 달아오르거나 계산이 몰리면 긴 예측부터 줄인다
        CrewModel.Update();
        Planner.Update();
        Authority.Update();
        BrainStopwatchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    internal void HashBrain(Action<long> I, Action<float> F)
    {
        if (_outlook != null) _outlook.Hash(I, F);
        if (_crewModel != null) _crewModel.Hash(I, F);
        if (_planner != null) _planner.Hash(I, F);
        if (_authority != null) _authority.Hash(I, F);
    }
}
