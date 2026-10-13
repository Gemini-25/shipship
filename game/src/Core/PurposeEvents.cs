using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 항해 중의 일 (갈림길 · 늦게 오는 통신) · 끝 (도착 장면 · 항해의 책 · 다음 항해로 이어받기).
public sealed partial class PurposeSystem
{
    private readonly Dictionary<int, long> _cargoHitAt = new();

    /// <summary>시험용: 지금 임무를 버리고 이 종류로 새로 받는다.</summary>
    public void BeginAs(MissionKind k) { Now = null; Begin(k); }

    /// <summary>시험용: 기한을 지금부터 이만큼으로.</summary>
    public void SetDeadline(float days) { if (Now != null) Now.Deadline = _w.Tick + (long)(days * SimTime.TicksPerDay); }

    /// <summary>한 시간마다: 화물칸에 불 · 파공이 있는지 (상자를 잃는다).</summary>
    private void WatchCargo()
    {
        var w = _w;
        if (Now is not VoyageMission m || m.Kind != MissionKind.Cargo || m.Finished) return;
        foreach (var r in w.Ship.LiveRooms.Where(r => r.Kind is RoomType.Cargo or RoomType.Storage))
        {
            bool fire = w.Fire.CountIn(r) >= 1, breach = r.Leaking;
            if (!fire && !breach) continue;
            if (_cargoHitAt.TryGetValue(r.Id, out var at) && w.Tick - at < SimTime.Hours(6)) continue;
            _cargoHitAt[r.Id] = w.Tick;
            CargoHit(r, fire ? "불" : "파공");
        }
    }

    /// <summary>항로 갈림길: 지름길(험한 구간 · 빠름) ↔ 돌아가는 길(순한 구간 · 느림). 주컴퓨터가 기한 · 배 상태 · 방침으로 고른다.</summary>
    public void Fork()
    {
        var w = _w;
        var v = w.Voyage;
        var m = Now!;
        m.ForkDone = true;
        var risky = new[] { LegKind.Narrows, LegKind.RadiationBelt, LegKind.AsteroidBelt, LegKind.Graveyard }[_rng.Range(0, 4)];
        var safe = new[] { LegKind.Cruise, LegKind.TradeLane, LegKind.PatrolLane }[_rng.Range(0, 3)];
        float save = _rng.Range(1f, 2f), extra = _rng.Range(1f, 2f);
        string riskyName = VoyageSystem.KindName(risky), safeName = VoyageSystem.KindName(safe);
        float strain = w.Planet.ShipStrain();
        float margin = DaysLeft - RouteLeft;
        int route = w.Policies["route"];
        float shortScore = (margin < 1f ? 1.2f : margin < 3f ? 0.4f : -0.2f) - strain * 1.5f + (route == 2 ? 0.6f : route == 3 ? 0.3f : route == 1 ? -0.8f : 0f) + (m.Kind == MissionKind.Cargo ? 0.2f : 0f);
        bool takeShort = shortScore > 0f;
        string why = takeShort
            ? (margin < 1f ? $"기한이 빠듯하다 (여유 {margin:0.0}일)" : route == 2 ? "방침이 빠르게" : "배가 버틸 만하다") + $" — {riskyName}을 지나 {save:0.0}일 줄인다"
            : (strain >= 0.3f ? $"배가 상했다 ({strain * 100:0}%)" : route == 1 ? "방침이 안전하게" : $"여유가 있다 ({margin:0.0}일)") + $" — {safeName}으로 돌아 {extra:0.0}일 더 간다";
        v.ForkTo(takeShort ? risky : safe, takeShort ? -save : extra, _rng);
        Note(m, $"갈림길 — {(takeShort ? "지름길" : "돌아가는 길")}: {why}");
        w.RaiseAlert($"항로 갈림길 — 주 컴퓨터가 {(takeShort ? "지름길" : "돌아가는 길")}을 골랐다 · {why}", null, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, $"항로 갈림길에서 {(takeShort ? $"지름길({riskyName})" : $"돌아가는 길({safeName})")}을 골랐다 — {why}", null, log: true);
    }

    public enum CommsKind { Sooner, Later, MoreGoal, Redirect, RescueCall, PayCut }

    /// <summary>늦게 오는 통신: 며칠 전에 보낸 소식이 이제 닿는다 — 임무가 흔들린다.</summary>
    public void Comms(CommsKind? force = null)
    {
        var w = _w;
        var m = Now;
        if (m == null || m.Finished) return;
        m.CommsHeard++;
        float delay = _rng.Range(1.5f, 6f);
        var kinds = new List<(CommsKind k, float wt)> { (CommsKind.Sooner, 1f), (CommsKind.Later, 0.8f), (CommsKind.Redirect, 0.6f), (CommsKind.RescueCall, m.Kind == MissionKind.Rescue && m.Aboard == 0 ? 4f : 0.5f), (CommsKind.PayCut, 0.5f) };
        if (m.Kind is MissionKind.Survey or MissionKind.Mining) kinds.Add((CommsKind.MoreGoal, 1.2f));
        float sum = kinds.Sum(x => x.wt), x = _rng.Range(0f, sum);
        var kind = kinds.Last().k;
        foreach (var (k, wt) in kinds) { x -= wt; if (x <= 0f) { kind = k; break; } }
        if (force is CommsKind fk) kind = fk;
        string head = $"{delay:0.0}일 전에 보낸 통신이 이제야 닿았다 — ";
        string text;
        switch (kind)
        {
            case CommsKind.Sooner:
            {
                float d = _rng.Range(0.5f, 1.3f);
                m.Deadline -= (long)(d * SimTime.TicksPerDay);
                m.Reward *= 1.3f;
                text = $"{m.Client}: 기한을 {d:0.0}일 당겨 달라 — 보수를 더 얹겠다";
                break;
            }
            case CommsKind.Later:
            {
                float d = _rng.Range(0.8f, 2f);
                m.Deadline += (long)(d * SimTime.TicksPerDay);
                text = $"{m.Client}: 받는 쪽 항구가 막혔다 — 기한을 {d:0.0}일 늦춰 준다";
                break;
            }
            case CommsKind.MoreGoal:
            {
                int n = 1 + _rng.Range(0, 3);
                m.Goal += n; m.Reward *= 1.25f;
                text = $"{m.Client}: {(m.Kind == MissionKind.Survey ? $"자료 {n}건" : $"희귀 소재 {n}개")}를 더 바란다 — 보수도 더 준다";
                break;
            }
            case CommsKind.Redirect:
            {
                var ports = VoyageV15.PortNames.Where(p => p != w.Voyage.Destination && p != w.Voyage.Origin).ToArray();
                string np = ports[_rng.Range(0, ports.Length)];
                float add = _rng.Range(-0.5f, 1.5f);
                string old = w.Voyage.Destination;
                w.Voyage.Redirect(np, add);
                m.Deadline += (long)(MathF.Max(0f, add) * SimTime.TicksPerDay);
                m.Title = m.Title.Replace(old, np);
                text = $"{m.Client}: 목적지를 {old}에서 {Ko.EuRo(np)} 바꾼다 ({(add >= 0f ? $"{add:0.0}일 더" : $"{-add:0.0}일 덜")})";
                break;
            }
            case CommsKind.RescueCall:
            {
                bool go = m.Kind == MissionKind.Rescue || !Behind || w.Policies["rescue"] == 0;
                if (go)
                {
                    w.Voyage.Shift(-_rng.Range(0.3f, 0.7f)); // 들렀다 간다 — 일정이 밀린다
                    var dock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault()?.Cells.FirstOrDefault(w.Ship.IsOpenFloor) ?? w.Ship.LiveRooms.SelectMany(r => r.Cells).First(w.Ship.IsOpenFloor);
                    var saved = new List<CrewMember>();
                    int n = 1 + _rng.Range(0, 3);
                    for (int i = 0; i < n; i++) if (w.Passengers.Board(dock, "조난선", rescued: true) is CrewMember c) saved.Add(c);
                    if (saved.Count > 0) Rescued(saved, "조난선");
                    text = $"조난 신호 — 들러서 {saved.Count}명을 태웠다" + (m.Kind == MissionKind.Rescue ? "" : " (임무 밖이지만 지나칠 수 없었다)");
                    w.History.Add(w, HistoryKind.Milestone, $"늦게 닿은 조난 신호를 따라가 {saved.Count}명을 구했다 — {string.Join("·", saved.Select(c => c.Name))}", null, saved, log: true);
                }
                else
                {
                    foreach (var c in w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.IsChild)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.06f);
                    text = "조난 신호 — 기한이 빠듯해 지나쳤다 (모두 말이 없었다)";
                    w.History.Add(w, HistoryKind.Decision, "늦게 닿은 조난 신호를 지나쳤다 — 기한이 빠듯했다", null, log: true);
                }
                break;
            }
            default:
                m.Reward *= 0.6f;
                text = $"{m.Client}: 사정이 나빠졌다 — 보수를 40% 깎겠다";
                break;
        }
        Note(m, head + text);
        w.Log.Add(w.Tick, LogKind.Ship, $"통신 — {head}{text}");
        w.RaiseAlert($"늦게 닿은 통신 — {text}", null, AlertLevel.Notice, shipWide: true);
    }

    // ─────────────────────────── 도착 ───────────────────────────

    /// <summary>목적지에 닿았다 (항로가 끝나기 직전에 부른다): 임무를 매듭짓고 항해의 책을 쓰고 다음 항해로 넘긴다.</summary>
    public void Arrive()
    {
        var w = _w;
        var v = w.Voyage;
        var m = Now;
        if (m == null || m.Finished) return;
        m.Finished = true;
        m.Late = w.Tick > m.Deadline;
        float late = (w.Tick - m.Deadline) / (float)SimTime.TicksPerDay;
        float share;
        switch (m.Kind)
        {
            case MissionKind.Cargo: share = m.Goal == 0 ? 1f : m.Aboard / (float)m.Goal; m.Progress = m.Aboard; break;
            case MissionKind.Survey: share = Math.Clamp(m.Progress / (float)Math.Max(1, m.Goal), 0f, 1f); break;
            case MissionKind.Rescue: share = m.People.Count == 0 ? 0.5f : m.Aboard / (float)m.People.Count; m.Progress = m.Aboard; break;
            case MissionKind.Colony: share = m.Goal == 0 ? 1f : m.Aboard / (float)m.Goal; m.Progress = m.Aboard; break;
            default:
            {
                int have = w.Ship.CountStored(ItemKind.Rare);
                m.Progress = Math.Min(have, m.Goal);
                if (m.Progress > 0) Life.Take(w, ItemKind.Rare, m.Progress);
                share = m.Progress / (float)Math.Max(1, m.Goal);
                break;
            }
        }
        m.Success = share >= 0.8f && !m.Late;
        float pay = m.Reward * Math.Clamp(share, 0f, 1f) * (m.Late ? 0.7f : 1f);
        v.Credits += pay;
        m.Result = m.Kind == MissionKind.Rescue && m.People.Count == 0 ? "조난 신호가 오지 않았다 — 대기 보수"
            : $"{(m.Success ? "해냈다" : share >= 0.5f ? "반쯤 해냈다" : "못 해냈다")} — {Progress(m)}" + (m.Late ? $" · {late:0.0}일 늦음" : " · 기한 안") + $" · 보수 {pay:0}";
        // 이주민 · 구조한 사람은 내린다
        if (m.Kind is MissionKind.Colony or MissionKind.Rescue)
            foreach (var id in m.People.ToList())
                if (id < w.Crew.Count && w.Crew[id] is CrewMember c && !c.Dead && !c.LeftShip) w.Values.Depart(c, m.Kind == MissionKind.Colony ? $"{v.Destination}에 정착했다" : $"{v.Destination}에서 내렸다 — 구조된 사람");
        // 마음
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away && !c.IsChild))
        {
            c.Needs.Stress = Math.Clamp(c.Needs.Stress + (m.Success ? -0.12f : 0.05f), 0f, 1f);
            MarkLog.Add(c.Memory.Marks, w.Tick, m.Success ? $"{v.Destination} 도착 — 임무를 해냈다" : $"{v.Destination} 도착 — 임무가 아쉬웠다");
        }
        Note(m, $"도착 — {m.Result}");
        w.History.Add(w, HistoryKind.Milestone, $"{v.Destination} 도착 — {m.Title}: {m.Result}", null, log: true);
        WriteBook(m);
        Handover(m);
        w.Homeland.Port(); // v23 도착 — 사람 · 소식 · 고향 재료
        Past.Add(m);
        if (Past.Count > 30) Past.RemoveAt(0);
        LastArrival = w.Tick;
        Now = null;
    }

    public static string Progress(VoyageMission m) => m.Kind switch
    {
        MissionKind.Cargo => $"화물 {m.Aboard}/{m.Goal}상자",
        MissionKind.Survey => $"자료 {m.Progress}/{m.Goal}건",
        MissionKind.Rescue => m.People.Count == 0 ? "조난 신호 기다리는 중" : $"구조한 사람 {m.Aboard}/{m.People.Count}명",
        MissionKind.Colony => $"이주민 {m.Aboard}/{m.Goal}명",
        _ => $"희귀 소재 {m.Progress}/{m.Goal}개",
    };

    private static float Weight(HistoryEvent e)
    {
        float s = e.Kind switch { HistoryKind.Death => 6f, HistoryKind.Milestone => 3f, HistoryKind.Bond => 3f, HistoryKind.Structure => 2.5f, HistoryKind.Casualty => 2.5f, HistoryKind.Decision => 1.6f, HistoryKind.Response => 2f, _ => 0.5f };
        if (e.Text.StartsWith("회의")) s -= 0.8f;
        if (e.Text.Contains("갈림길") || e.Text.Contains("통신") || e.Text.Contains("조난")) s += 1f;
        if (e.Text.Contains("태어")) s += 3f;
        if (e.Text.Contains("행성") || e.Text.Contains("불시착")) s += 1f;
        if (e.Text.Contains("조건이 풀렸다") || e.Text.Contains("저장")) s -= 2f;
        return s;
    }

    private void WriteBook(VoyageMission m)
    {
        var w = _w;
        var v = w.Voyage;
        var events = w.History.Events.Where(e => e.Tick >= v.StartTick && e.Tick <= w.Tick).ToList();
        var book = new VoyageBook
        {
            Number = v.Number, From = v.Origin, To = v.Destination, Days = (w.Tick - v.StartTick) / (float)SimTime.TicksPerDay,
            Mission = $"{KindName(m.Kind)} — {m.Title} ({m.Client})", Result = m.Result, Success = m.Success,
        };
        foreach (var e in events.Where(e => Weight(e) >= 1.5f).OrderByDescending(Weight).ThenBy(e => e.Tick).Take(8).OrderBy(e => e.Tick))
            book.Chapters.Add($"{SimTime.Day(e.Tick)}일 — {e.Text}");
        foreach (var c in w.Crew)
        {
            bool atStart = _crewAtStart.Contains(c.Id);
            if (c.Dead && c.DiedAt >= v.StartTick) book.Lost.Add(c.Name);
            else if (c.LeftShip && atStart) book.Left.Add(c.Name);
            else if (!atStart && !c.Dead && !c.LeftShip) book.Joined.Add(c.Name);
        }
        book.Planets.AddRange(w.Planet.Visits.Where(x => x.Landed >= v.StartTick).Select(x => x.Name + (x.Crash ? " (불시착)" : "")));
        book.Scenes.AddRange(w.Album.Shots.Where(s => s.Tick >= v.StartTick).Select(s => s.Id).TakeLast(6));
        Books.Add(book);
        if (Books.Count > 40) Books.RemoveAt(0);
    }

    /// <summary>다음 항해로: 지친 사람 한둘은 내리고, 나머지 사람 · 로봇 · 주컴퓨터의 경험 · 배가 이어진다.</summary>
    private void Handover(VoyageMission m)
    {
        var w = _w;
        var crew = w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away && !c.IsChild && !c.Passenger).ToList();
        int leave = 0;
        foreach (var c in crew.OrderByDescending(c => w.Homeland.WantsHome(c) ? 2f : c.Needs.Stress).ThenBy(c => c.Id))
        {
            if (leave >= 2 || crew.Count - leave <= 4 || c.Id == w.Command.CaptainId) break;
            bool home = w.Homeland.WantsHome(c);
            if (!home && c.Needs.Stress < 0.65f || !_rng.Chance(home ? 0.7f : 0.5f)) continue;
            w.Values.Depart(c, home ? $"{w.Voyage.Destination}에서 내렸다 — {Ko.EuRo(w.Homeland.HomeOf(c))} 돌아간다 (향수병)" : $"{w.Voyage.Destination}에서 이번 항해를 끝으로 내렸다 — 지쳤다");
            leave++;
        }
        int people = w.Crew.Count(c => !c.Dead && !c.LeftShip && !c.Passenger);
        int bots = w.Robots.Robots.Count(r => !r.Wrecked);
        int lessons = w.Commander.Lessons.Reviews;
        if (Books.LastOrDefault() is VoyageBook b) b.Carried = $"사람 {people}명 · 로봇 {bots}대 · 주컴퓨터 복기 {lessons}건 · {w.Ship.Name}";
    }
}

public sealed partial class VoyageSystem
{
    /// <summary>v23 갈림길: 다음 구간(목적지 앞)을 이 종류로 바꾸고 날 수를 더하거나 줄인다.</summary>
    public void ForkTo(LegKind kind, float addDays, Rng r)
    {
        int at = Math.Min(Index + 1, Legs.Count - 2);
        if (at < 0 || at >= Legs.Count - 1) return;
        var pool = VoyageV15.Pool(kind);
        float baseDays = Legs[at].Days;
        Legs[at] = new Leg { Kind = kind, Name = pool.Length > 0 ? pool[r.Range(0, pool.Length)] : KindName(kind), Days = MathF.Max(0.5f, baseDays + addDays) };
    }

    /// <summary>시험용: 목적지 바로 앞으로 (다음 갱신 즈음 도착).</summary>
    public void ArriveSoon()
    {
        Index = Legs.Count - 1;
        Progress = MathF.Max(0f, Legs[^1].Days - 0.005f);
    }

    /// <summary>v23 늦은 통신: 목적지가 바뀌었다 (마지막 기항지 이름 · 날 수).</summary>
    public void Redirect(string port, float addDays)
    {
        Destination = port;
        var last = Legs[^1];
        Legs[^1] = new Leg { Kind = LegKind.Port, Name = port, Days = last.Days };
        if (Legs.Count >= 2 && Index < Legs.Count - 2)
        {
            var pre = Legs[^2];
            Legs[^2] = new Leg { Kind = pre.Kind, Name = pre.Name, Days = MathF.Max(0.5f, pre.Days + addDays) };
        }
    }
}
