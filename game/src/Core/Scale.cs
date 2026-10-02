using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.18 사고 · 재난 다섯 규모 — 판정 · 대응 · 완급 · 연쇄 · 도감.
//  · 판정: 인과 사슬(CauseLog)의 사고 하나를 사건(ScaleCase) 하나로 본다. 기본 규모(표) 위에 실제 피해가 규모를 올린다:
//    두 방 넘게 번지면 계통, 정전이 두 방부터 계통 · 배의 절반이면 배 전체, 원자로 정지 · 방 분리 · 쓰러진 사람 셋이면 배 전체.
//    부상(개인)과 우주급 예보는 사슬 밖에서 따로 잡는다.
//  · 대응: 규모가 오를 때마다 주 컴퓨터가 판정하고(방송 문구 · 소집 인원 · 제안) 사람을 부른다 — 개인 → 곁의 사람 · 방 → 당직 ·
//    계통 → 여러 명 · 작업 우선 · 배 → 전원 소집 · 일상 중단 · 우주급 → 대피 · 항로 변경. 컴퓨터가 멎었으면 사람이 늦게 판정한다.
//  · 승무원: 규모를 느낀다 — 부른 사람은 그 일에 붙고, 배 전체 · 우주급이면 일상을 멈추고 모이고(점호) 두려워한다.
//  · 이야기꾼: 최근 규모 이력으로 완급 — 큰 것 뒤엔 숨 돌릴 틈, 너무 조용하면 작은 것.
//  · 연쇄: 고리마다 규모를 남긴다 (② 누수 → ③ 정전 → ④ 원자로 정지) — 화면 사슬 · 시간 막대가 규모 색으로 그린다.
// 난수는 쓰지 않는다. 사전(Dictionary)을 돌 때는 늘 정렬해서 → 같은 시드면 같은 판정.

public sealed class ScaleStep
{
    public long Tick { get; init; }
    public IncidentScale From { get; init; }
    public IncidentScale To { get; init; }
    public string Why { get; init; } = "";
    public int Node { get; init; } = -1;
}

/// <summary>사고 하나의 규모와 대응.</summary>
public sealed class ScaleCase
{
    public int Id { get; init; }
    /// <summary>인과 사슬 사고의 뿌리 고리 (-1: 사슬 밖 — 부상 · 우주급 예보).</summary>
    public int Root { get; set; } = -1;
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public IncidentScale Base { get; set; }
    public IncidentScale Now { get; set; }
    public IncidentScale Peak { get; set; }
    /// <summary>판정한 규모 (컴퓨터든 사람이든 마지막으로).</summary>
    public IncidentScale Planned { get; set; }
    public long Start { get; init; }
    public long Changed { get; set; }
    public long End { get; set; } = -1;
    public int RoomId { get; set; } = -1;
    public int CrewId { get; set; } = -1;
    public int CosmicId { get; set; } = -1;
    public int CosmicPhase { get; set; } = -1;
    public Skill Skill { get; set; } = Skill.Engineering;
    /// <summary>번진 방 (정렬).</summary>
    public List<int> Rooms { get; } = new();
    public List<ScaleStep> Steps { get; } = new();
    /// <summary>그 일에 부른 사람 (규모가 오르면 는다).</summary>
    public List<int> Workers { get; } = new();
    public List<int> Mustered { get; } = new();
    /// <summary>소집을 들은 사람 (방송 · 외침 · 같은 방 사람의 말).</summary>
    public List<int> Told { get; } = new();
    public List<int> Feared { get; } = new();
    public HashSet<CauseKind> KindsSeen { get; } = new();
    public int Called { get; set; }
    /// <summary>규모마다 부른 수 · 실제로 붙은 사람 수 (가장 많았을 때).</summary>
    public int[] StageCalled { get; } = new int[5];
    public int[] StageResponders { get; } = new int[5];
    public int Responders { get; set; }
    public string Plan { get; set; } = "";
    public string Broadcast { get; set; } = "";
    public string Suggest { get; set; } = "";
    public string JudgedBy { get; set; } = "";
    public int BroadcastId { get; set; } = -1;
    public long MusterAt { get; set; } = -1;
    public int MusterRoom { get; set; } = -1;
    public bool MusterDone { get; set; }
    /// <summary>컴퓨터 없이 사람이 외친 때.</summary>
    public long ShoutAt { get; set; } = -1;
    /// <summary>컴퓨터가 없으면 사람이 이때 판정한다 (늦다).</summary>
    public long JudgeDue { get; set; } = -1;
    public bool Hot { get; set; }
    public bool Open => End < 0;
    public bool Big => Now >= IncidentScale.Ship;
}

public sealed partial class ScaleSystem
{
    private readonly World _w;
    public List<ScaleCase> Cases { get; } = new();
    private readonly List<ScaleCase> _open = new();
    private readonly Dictionary<int, ScaleCase> _byRoot = new();
    private readonly Dictionary<int, string> _tags = new();
    private readonly Dictionary<int, IncidentScale> _nodeScale = new();
    private readonly Dictionary<int, int> _groupPeak = new();
    private readonly HashSet<int> _cosmicCased = new();
    private readonly Dictionary<int, float> _injury = new();
    private readonly Dictionary<int, ScaleCase> _roomCase = new();
    private readonly Dictionary<int, ScaleCase> _crewCase = new();
    private readonly Dictionary<int, ScaleCase> _workerCase = new();
    private readonly Dictionary<int, (IncidentScale s, ScaleCase k)> _felt = new();
    private int _lastRoot = -1;
    private int _next = 1;
    private long _nextScan, _nextUnrest;
    private int _liveRooms = 1;

    /// <summary>도감: 겪은 사고 열쇠마다 몇 번 (표의 열쇠 · 사슬 고리 "cause:…").</summary>
    public SortedDictionary<string, int> Seen { get; } = new(StringComparer.Ordinal);
    /// <summary>끝난 사건을 가장 컸던 규모로 센다.</summary>
    public int[] ByPeak { get; } = new int[5];
    /// <summary>규모가 오를 때마다 하나씩 (화면 · 소리가 새 것을 안다).</summary>
    public int Serial { get; private set; }
    public (int caseId, IncidentScale scale, long tick) LastRise { get; private set; } = (-1, IncidentScale.Personal, -1);
    public int Escalations, Plans, Broadcasts, HumanJudged, Musters, Rests, Fears, Halts, Proposals, WordOfMouth;
    /// <summary>마지막으로 배 전체 · 우주급 사건이 가라앉은 때.</summary>
    public long LastBigEnd { get; private set; } = -1;
    public string PaceNote { get; private set; } = "";

    public ScaleSystem(World w) => _w = w;

    /// <summary>사고를 건 코드가 뿌리 고리에 표의 열쇠를 붙인다 (사고 70 · 이야기꾼 · 반란).</summary>
    public void Tag(int node, string key) { if (node >= 0) _tags[node] = key; }

    public IEnumerable<ScaleCase> OpenCases => _open;
    public ScaleCase? CaseOf(CauseIncident? inc) => inc != null && _byRoot.TryGetValue(inc.Root, out var k) ? k : null;
    public ScaleCase? CaseById(int id) => _open.FirstOrDefault(k => k.Id == id) ?? Cases.FirstOrDefault(k => k.Id == id);

    /// <summary>고리 하나의 규모 (화면 사슬 · 시간 막대). 모르면 null.</summary>
    public IncidentScale? NodeScale(int node) => _nodeScale.TryGetValue(node, out var s) ? s : null;

    /// <summary>지금 가장 큰 뜨거운 사건 (화면 테두리 · 소리).</summary>
    public ScaleCase? Top
    {
        get
        {
            ScaleCase? best = null;
            foreach (var k in _open)
                if (k.Hot && (best == null || k.Now > best.Now || k.Now == best.Now && k.Changed > best.Changed)) best = k;
            return best;
        }
    }

    /// <summary>그 규모로 겪은 사건 (끝난 것 + 지금 그 규모인 것).</summary>
    public int Experienced(IncidentScale s) => ByPeak[(int)s] + _open.Count(k => k.Peak == s);
    public int SeenOf(string key) => Seen.TryGetValue(key, out var n) ? n : 0;

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _nextScan) return;
        _nextScan = w.Tick + SimTime.Minutes(1);
        _liveRooms = Math.Max(1, w.Ship.Rooms.Count(r => !r.Detached && r.Type != RoomType.Corridor));
        ScanChain();
        ScanCosmic();
        ScanInjuries();
        if (w.Tick >= _nextUnrest) { _nextUnrest = w.Tick + SimTime.Hours(1); ScanUnrest(); }
        for (int i = _open.Count - 1; i >= 0; i--) Refresh(_open[i]);
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            var k = _open[i];
            if (k.JudgeDue >= 0 && w.Tick >= k.JudgeDue) HumanJudge(k);
            if (!k.Open) { Close(k); _open.RemoveAt(i); }
        }
        Index();
        foreach (var k in _open)
        {
            if (!k.Hot) continue;
            k.Responders = CountResponders(k);
            int st = (int)k.Now;
            if (k.Responders > k.StageResponders[st]) k.StageResponders[st] = k.Responders;
        }
        Feel();
        MusterCheck();
    }

    // ─────────────────────────────── 사건 잡기 ───────────────────────────────

    private ScaleCase NewCase(string key, string name, IncidentScale bas, int roomId)
    {
        var w = _w;
        var k = new ScaleCase { Id = _next++, Start = w.Tick, Changed = w.Tick, Key = key, Name = name, Base = bas, Now = bas, Peak = bas, RoomId = roomId };
        Cases.Add(k);
        _open.Add(k);
        Seen[key] = SeenOf(key) + 1;
        if (Cases.Count > 400)
        {
            // 오래 끝난 것은 덜어 낸다 (번호는 그대로 — 화면은 열린 것 · 최근 것만 본다)
            var old = Cases[0];
            if (!old.Open) { Cases.RemoveAt(0); if (old.Root >= 0) _byRoot.Remove(old.Root); }
        }
        return k;
    }

    private void ScanChain()
    {
        var log = _w.Causes;
        int i = log.Incidents.Count - 1;
        while (i >= 0 && log.Incidents[i].Root > _lastRoot) i--;
        for (i++; i < log.Incidents.Count; i++)
        {
            var inc = log.Incidents[i];
            _lastRoot = Math.Max(_lastRoot, inc.Root);
            if (_byRoot.ContainsKey(inc.Root)) continue;
            var root = log.Node(inc.Root);
            if (root.Kind == CauseKind.Recovery) continue;
            // 우주급 사건의 본 사건 고리면 그 사건에 붙인다
            ScaleCase? cos = null;
            foreach (var k0 in _open)
                if (k0.CosmicId >= 0 && k0.Root < 0 && _w.Cosmic.Events.FirstOrDefault(e => e.Id == k0.CosmicId) is CosmicEvent ce && ce.Cause == inc.Root) { cos = k0; break; }
            if (cos != null) { cos.Root = inc.Root; _byRoot[inc.Root] = cos; continue; }
            string key = _tags.TryGetValue(inc.Root, out var t) ? t : "cause:" + root.Kind;
            var bas = _tags.ContainsKey(inc.Root) ? ScaleTable.OfKey(key) : ScaleTable.Of(root.Kind);
            var k = NewCase(key, root.Text, bas, root.RoomId);
            k.Root = inc.Root;
            k.Skill = SkillOf(root.Kind, key);
            _byRoot[inc.Root] = k;
            Rise(k, bas, bas, "처음 판정", inc.Root, first: true);
        }
    }

    private void ScanCosmic()
    {
        var w = _w;
        foreach (var e in w.Cosmic.Events)
        {
            if (e.Ghost || _cosmicCased.Contains(e.Id) || e.Phase == CosmicPhase.Done) continue;
            if (!e.Known && e.Phase < CosmicPhase.Impact) continue;
            _cosmicCased.Add(e.Id);
            var k = NewCase("cosmic:" + e.Spec.Id, e.Spec.Name, IncidentScale.Cosmic, e.TargetRoom);
            k.CosmicId = e.Id;
            k.CosmicPhase = (int)e.Phase;
            k.Skill = Skill.Piloting;
            if (e.Cause >= 0) { k.Root = e.Cause; _byRoot[e.Cause] = k; }
            Rise(k, IncidentScale.Cosmic, IncidentScale.Cosmic, $"{e.PhaseName} — {e.Spec.Detect}", e.Cause, first: true);
        }
    }

    /// <summary>개인 사고: 부상이 새로 늘었다 (사슬 사고 속의 부상이면 그 사건이 맡는다).</summary>
    private void ScanInjuries()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            float inj = c.Dead ? 0f : c.Vitals.Injury;
            float was = _injury.TryGetValue(c.Id, out var v) ? v : inj;
            _injury[c.Id] = inj;
            if (c.Dead || inj < was + 0.04f || _crewCase.ContainsKey(c.Id)) continue;
            // 방금 그 방을 때린 사고가 있으면 그 사건의 일 (운석 · 불 · 폭발이 낸 부상)
            if (c.Room != null && w.Causes.ParentFor(c.Room) >= 0) continue;
            string cause = c.Vitals.InjuryCause ?? "다침";
            string key = WoundKey(c, cause);
            var k = NewCase(key, $"{c.Name} — {cause}", IncidentScale.Personal, c.Room?.Id ?? -1);
            k.CrewId = c.Id;
            k.Skill = Skill.Medicine;
            if (c.Room != null) k.Rooms.Add(c.Room.Id);
            _crewCase[c.Id] = k;
            Rise(k, IncidentScale.Personal, IncidentScale.Personal, cause, -1, first: true);
        }
    }

    private static string WoundKey(CrewMember c, string cause)
    {
        if (cause.Contains("미끄러")) return "wound:slip";
        if (cause.Contains("넘어") || cause.Contains("발이 빠져")) return "wound:fall";
        if (cause.Contains("데었") || cause.Contains("화상") || cause.Contains("뜨거")) return "wound:" + WoundKind.Burn;
        if (cause.Contains("베") || cause.Contains("파편") || cause.Contains("찔")) return "wound:" + WoundKind.Cut;
        var last = c.Vitals.Wounds.Count > 0 ? c.Vitals.Wounds[^1].Kind : WoundKind.Crush;
        return "wound:" + last;
    }

    /// <summary>반란 조짐: 사기가 바닥이고 절반 넘게 화가 나 있다 (배 전체).</summary>
    private bool Unrest()
    {
        var w = _w;
        int alive = 0, angry = 0;
        foreach (var c in w.Crew) { if (c.Dead || c.IsChild) continue; alive++; if (c.Mind.Anger > 0.55f) angry++; }
        return alive >= 4 && w.Society.Morale < 0.2f && angry * 2 >= alive;
    }

    private void ScanUnrest()
    {
        var w = _w;
        if (_open.Any(k => k.Key == "unrest") || !Unrest()) return;
        int node = w.Causes.Root(CauseKind.Hazard, "반란 조짐 — 사기가 바닥이고 절반 넘게 명령을 거부할 기세", null, null);
        Tag(node, "unrest");
        w.Causes.Until(node, () => !Unrest(), "사기가 돌아왔다 — 반란 조짐이 가라앉았다");
        w.RaiseAlert("반란 조짐 — 사기 바닥 · 다수가 화나 있다", null, AlertLevel.Critical, shipWide: true);
    }

    private static Skill SkillOf(CauseKind k, string key) => k switch
    {
        CauseKind.Outage or CauseKind.Cut or CauseKind.Scram or CauseKind.Shock or CauseKind.NoData => Skill.Electrical,
        CauseKind.Flood or CauseKind.NoWater or CauseKind.Breach or CauseKind.Fault or CauseKind.Stop or CauseKind.NoAir => Skill.Mechanics,
        CauseKind.Casualty or CauseKind.Illness or CauseKind.Death or CauseKind.Mistake => Skill.Medicine,
        _ => key is "unrest" ? Skill.Medicine : Skill.Engineering,
    };

    // ─────────────────────────────── 실제 피해로 규모를 잰다 ───────────────────────────────

    private int BigRooms => Math.Max(6, (int)MathF.Ceiling(_liveRooms * 0.45f));

    /// <summary>고리 하나의 규모: 묶음 상태(정전 · 단수 · 환기 · 데이터)는 방 수로, 뿌리는 붙은 열쇠로.</summary>
    private IncidentScale Of(CauseNode n)
    {
        if (_tags.TryGetValue(n.Id, out var key)) return ScaleTable.OfKey(key);
        if (n.Kind is CauseKind.Outage or CauseKind.NoWater or CauseKind.NoAir or CauseKind.NoData)
        {
            int size = _w.Causes.GroupRooms(n.Id).Count();
            int peak = Math.Max(size, _groupPeak.TryGetValue(n.Id, out var p) ? p : 0);
            _groupPeak[n.Id] = peak;
            return peak >= BigRooms ? IncidentScale.Ship : peak >= 2 ? IncidentScale.System : IncidentScale.Room;
        }
        if (n.Kind == CauseKind.Explosion)
        {
            // v16.13 폭발 기록의 피해 규모 (세기 · 번진 방 · 다친 사람)
            var s = IncidentScale.Room;
            foreach (var rec in _w.Blast.Recent)
                if (Math.Abs(rec.Tick - n.Tick) <= SimTime.Minutes(1) && (rec.Room == n.RoomId || n.RoomId < 0) && ScaleTable.Of(rec.Scale) > s) s = ScaleTable.Of(rec.Scale);
            return s;
        }
        return ScaleTable.Of(n.Kind);
    }

    private void Refresh(ScaleCase k)
    {
        var w = _w;
        if (k.CosmicId >= 0) { RefreshCosmic(k); return; }
        if (k.Root < 0) { RefreshPersonal(k); return; }
        var log = w.Causes;
        var inc = log.IncidentOf(k.Root);
        if (inc == null) { k.End = w.Tick; return; }
        // 피해를 센다: 모든 고리(가장 컸을 때)와 지금 살아 있는 고리(지금)
        IncidentScale peak = k.Base, now = IncidentScale.Personal;
        string why = ""; int whyNode = -1;
        var rooms = new SortedSet<int>(); var liveRooms = new SortedSet<int>();
        var fire = new SortedSet<int>(); var breach = new SortedSet<int>(); var flood = new SortedSet<int>();
        var fireL = new SortedSet<int>(); var breachL = new SortedSet<int>(); var floodL = new SortedSet<int>();
        int downL = 0, deathsRecent = 0;
        foreach (int id in inc.Nodes)
        {
            var n = log.Node(id);
            if (n.Kind == CauseKind.Recovery) continue;
            var ns = Of(n);
            if (!_nodeScale.TryGetValue(id, out var old) || ns > old) _nodeScale[id] = ns;
            if (k.KindsSeen.Add(n.Kind) && id != k.Root) Seen["cause:" + n.Kind] = SeenOf("cause:" + n.Kind) + 1;
            bool live = n.Open || !n.Lasting && w.Tick - n.Tick < SimTime.Minutes(30) || id == k.Root && w.Tick - n.Tick < SimTime.Minutes(30);
            if (ns > peak) { peak = ns; why = n.Text; whyNode = id; }
            if (live && ns > now) { now = ns; if (ns >= peak) { why = n.Text; whyNode = id; } }
            bool roomy = n.RoomId >= 0 && n.Kind is not (CauseKind.Casualty or CauseKind.Death or CauseKind.Illness or CauseKind.Mistake or CauseKind.Shock);
            if (roomy) { rooms.Add(n.RoomId); if (live) liveRooms.Add(n.RoomId); }
            if (n.Kind is CauseKind.Outage or CauseKind.NoWater or CauseKind.NoAir)
                foreach (int r in log.GroupRooms(id)) { rooms.Add(r); if (live) liveRooms.Add(r); }
            if (n.RoomId >= 0)
            {
                if (n.Kind == CauseKind.Fire) { fire.Add(n.RoomId); if (live) fireL.Add(n.RoomId); }
                if (n.Kind == CauseKind.Breach) { breach.Add(n.RoomId); if (live) breachL.Add(n.RoomId); }
                if (n.Kind == CauseKind.Flood) { flood.Add(n.RoomId); if (live) floodL.Add(n.RoomId); }
            }
            if (n.Kind == CauseKind.Casualty && n.Open) downL++;
            if (n.Kind == CauseKind.Death && w.Tick - n.Tick < SimTime.Hours(2)) deathsRecent++;
        }
        var (dp, dpWhy) = Spread(rooms.Count, fire.Count, breach.Count, flood.Count, inc.Casualties, inc.Deaths);
        if (dp > peak) { peak = dp; why = dpWhy; whyNode = inc.Nodes[^1]; }
        var (dn, dnWhy) = Spread(liveRooms.Count, fireL.Count, breachL.Count, floodL.Count, downL, deathsRecent);
        if (dn > now) { now = dn; why = dnWhy; whyNode = inc.Nodes[^1]; }
        if (k.Key == "unrest") now = Unrest() ? IncidentScale.Ship : IncidentScale.Personal;
        k.Rooms.Clear();
        k.Rooms.AddRange(rooms);
        if (k.RoomId < 0 && rooms.Count > 0) k.RoomId = rooms.Min;
        bool open = inc.Open;
        k.Hot = open && now >= IncidentScale.Room;
        if (!open) k.End = inc.End >= 0 ? inc.End : w.Tick;
        if (peak > k.Peak) k.Peak = peak;
        if (now != k.Now)
        {
            var from = k.Now;
            k.Now = now;
            k.Changed = w.Tick;
            if (from >= IncidentScale.Ship && now < IncidentScale.Ship) LastBigEnd = w.Tick; // 큰 고비를 넘겼다 (이야기꾼은 이때부터 쉬어 간다)
            if (now > from) Rise(k, from, now, why.Length > 0 ? why : "번졌다", whyNode);
            else if (from >= IncidentScale.System && now <= IncidentScale.Room) Calm(k, from);
        }
    }

    /// <summary>번진 정도로 오르는 규모: 같은 불도 방 하나면 방, 번지면 계통.</summary>
    private (IncidentScale s, string why) Spread(int rooms, int fire, int breach, int flood, int down, int deaths)
    {
        int big = BigRooms;
        if (rooms >= big) return (IncidentScale.Ship, $"배의 절반 가까이 ({rooms}곳)로 번졌다");
        if (fire >= 4) return (IncidentScale.Ship, $"불이 {fire}개 방으로 번졌다");
        if (breach >= 4) return (IncidentScale.Ship, $"{breach}개 방이 감압 — 선체 대파");
        if (down >= 3) return (IncidentScale.Ship, $"쓰러진 사람 {down}");
        if (deaths >= 2) return (IncidentScale.Ship, $"사망 {deaths}");
        if (fire >= 2) return (IncidentScale.System, $"불이 옆방으로 번졌다 ({fire}곳)");
        if (breach >= 2) return (IncidentScale.System, $"구획 감압 ({breach}곳)");
        if (flood >= 2) return (IncidentScale.System, $"물이 {flood}개 방에 찼다");
        if (deaths >= 1) return (IncidentScale.System, "사망");
        if (rooms >= 2) return (IncidentScale.System, $"{rooms}개 방으로 번졌다");
        if (rooms == 1) return (IncidentScale.Room, "한 방");
        return (IncidentScale.Personal, "");
    }

    private void RefreshPersonal(ScaleCase k)
    {
        var w = _w;
        var c = k.CrewId >= 0 && k.CrewId < w.Crew.Count ? w.Crew[k.CrewId] : null;
        bool healed = c == null || c.Dead || c.Vitals.Injury < 0.03f;
        bool cooled = w.Tick - k.Start > SimTime.Hours(3) || w.Tick - k.Start > SimTime.Minutes(30) && c is { Down: false };
        k.Hot = !healed && w.Tick - k.Start < SimTime.Hours(3);
        if (c != null && c.Down && !c.Dead && k.Now < IncidentScale.Room)
        {
            // 쓰러졌다: 곁의 사람만으로는 모자란다 — 당직이 온다
            var from = k.Now;
            k.Now = k.Peak = IncidentScale.Room;
            k.Changed = w.Tick;
            Rise(k, from, IncidentScale.Room, $"{c.Name} 쓰러짐", -1);
        }
        if (healed || cooled && (c == null || !c.Down)) k.End = w.Tick;
    }

    private void RefreshCosmic(ScaleCase k)
    {
        var w = _w;
        var e = w.Cosmic.Events.FirstOrDefault(x => x.Id == k.CosmicId);
        if (e == null || e.Phase == CosmicPhase.Done) { k.End = w.Tick; k.Hot = false; return; }
        if (k.Root < 0 && e.Cause >= 0) { k.Root = e.Cause; _byRoot[e.Cause] = k; }
        k.Hot = e.Phase is CosmicPhase.Brace or CosmicPhase.Impact || e.Phase == CosmicPhase.After && w.Tick - e.PhaseSince < SimTime.Hours(2);
        if (e.TargetRoom >= 0 && !k.Rooms.Contains(e.TargetRoom)) k.Rooms.Add(e.TargetRoom);
        if ((int)e.Phase != k.CosmicPhase)
        {
            k.CosmicPhase = (int)e.Phase;
            k.Changed = w.Tick;
            if (e.Phase is CosmicPhase.Brace or CosmicPhase.Impact) { Serial++; LastRise = (k.Id, IncidentScale.Cosmic, w.Tick); Plan(k, $"{e.PhaseName} — {e.Spec.Name}"); }
        }
    }

    private void Close(ScaleCase k)
    {
        ByPeak[(int)k.Peak]++;
        if (k.Now >= IncidentScale.Ship || k.Peak >= IncidentScale.Ship && LastBigEnd < k.Start) LastBigEnd = _w.Tick;
        if (k.CrewId >= 0) _crewCase.Remove(k.CrewId);
        if (k.Peak >= IncidentScale.System)
            _w.Log.Add(_w.Tick, LogKind.Ship, $"사고 수습 — {k.Name} (가장 컸을 때 {ScaleTable.Label(k.Peak)} · 부른 사람 {k.Called} · 붙은 사람 최대 {k.StageResponders.Max()})");
    }

    // ─────────────────────────────── 규모가 오른다 ───────────────────────────────

    private void Rise(ScaleCase k, IncidentScale from, IncidentScale to, string why, int node, bool first = false)
    {
        var w = _w;
        k.Steps.Add(new ScaleStep { Tick = w.Tick, From = from, To = to, Why = why, Node = node });
        if (node >= 0) { if (!_nodeScale.TryGetValue(node, out var old) || to > old) _nodeScale[node] = to; }
        if (to > k.Peak) k.Peak = to;
        if (!first) Escalations++;
        Serial++;
        LastRise = (k.Id, to, w.Tick);
        var room = k.RoomId >= 0 && k.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[k.RoomId] : null;
        if (!first) w.Log.Add(w.Tick, LogKind.Warning, $"사고 규모 {ScaleTable.Label(from)} → {ScaleTable.Label(to)} — {k.Name} ({why})");
        if (!first && to >= IncidentScale.System)
            w.History.Add(w, HistoryKind.Damage, $"{k.Name} — {ScaleTable.Label(to)}로 커졌다 ({why})", room);
        if (to >= IncidentScale.Room || k.CrewId >= 0) Plan(k, why);
    }

    private void Calm(ScaleCase k, IncidentScale from)
    {
        var w = _w;
        w.Log.Add(w.Tick, LogKind.Ship, $"사고 규모가 내려갔다 {ScaleTable.Label(from)} → {ScaleTable.Label(k.Now)} — {k.Name}");
        // 부른 사람은 일상으로 (방 규모 당직만 남긴다)
        if (k.Workers.Count > 2) k.Workers.RemoveRange(2, k.Workers.Count - 2);
        k.MusterAt = -1;
    }

    // ─────────────────────────────── 읽기 (승무원 · 작업) ───────────────────────────────

    /// <summary>방 · 다친 사람 · 부른 사람으로 뜨거운 사건을 찾을 표.</summary>
    private void Index()
    {
        _roomCase.Clear();
        _workerCase.Clear();
        foreach (var k in _open)
        {
            if (!k.Hot) continue;
            foreach (int r in k.Rooms)
                if (!_roomCase.TryGetValue(r, out var cur) || k.Now > cur.Now) _roomCase[r] = k;
            foreach (int c in k.Workers)
                if (!_workerCase.TryGetValue(c, out var cw) || k.Now > cw.Now) _workerCase[c] = k;
        }
    }

    private ScaleCase? CaseFor(WorkOrder o)
    {
        if (o.Target.Crew is CrewMember p && _crewCase.TryGetValue(p.Id, out var pc) && pc.Hot) return pc;
        return o.Target.CurrentRoom is Room r && _roomCase.TryGetValue(r.Id, out var rc) ? rc : null;
    }

    /// <summary>작업 매력에 더할 몫 (Chores.Appeal): 부른 사람은 그 사건의 일에 붙고, 계통부터는 모두가 그 방 일을 앞에 둔다.</summary>
    public float Bias(CrewMember c, WorkOrder o)
    {
        if (_roomCase.Count == 0 && _crewCase.Count == 0) return 0f;
        var k = CaseFor(o);
        if (k == null) return 0f;
        bool worker = k.Workers.Contains(c.Id);
        return k.Now switch
        {
            IncidentScale.Personal => worker || c.Room != null && k.Rooms.Contains(c.Room.Id) ? 0.15f : 0f, // 혼자 · 곁의 사람
            IncidentScale.Room => worker ? 0.25f : 0f,                                                          // 당직
            IncidentScale.System => worker ? 0.3f : 0.08f,                                                      // 여러 명 · 작업 우선
            _ => worker ? 0.35f : 0.18f,                                                                         // 전원
        };
    }

    /// <summary>비번이어도 나온다: 계통부터 부른 사람 · 배 전체부터 모두.</summary>
    public bool AllHands(CrewMember c, WorkOrder o)
    {
        if (_roomCase.Count == 0) return false;
        var k = CaseFor(o);
        return k != null && (k.Now >= IncidentScale.Ship || k.Now >= IncidentScale.System && k.Workers.Contains(c.Id));
    }

    /// <summary>그 방의 뜨거운 사건 규모 (없으면 null).</summary>
    public IncidentScale? RoomScale(Room r) => _roomCase.TryGetValue(r.Id, out var k) ? k.Now : null;

    /// <summary>주 컴퓨터 화재 수순의 기다림 배율: 불이 번져 계통이면 0.6 · 배 전체면 0.45 (소화조만 믿고 기다리지 않는다).</summary>
    public float GraceMul(Room r) => RoomScale(r) switch { IncidentScale.System => 0.6f, >= IncidentScale.Ship => 0.45f, _ => 1f };

    /// <summary>이 사람이 느끼는 가장 큰 규모 (모르면 개인).</summary>
    public IncidentScale Felt(CrewMember c) => _felt.TryGetValue(c.Id, out var f) ? f.s : IncidentScale.Personal;
    public ScaleCase? FeltCase(CrewMember c) => _felt.TryGetValue(c.Id, out var f) ? f.k : null;
    public ScaleCase? WorkerOf(CrewMember c) => _workerCase.TryGetValue(c.Id, out var k) ? k : null;

    /// <summary>
    /// 행동 점수를 규모가 누른다 (Brain.Think): 배 전체 · 우주급을 느끼면 일상(쉼 · 수다 · 취미 · 장면 · 공사)을 멈추고,
    /// 계통 사건에 부른 사람은 일상을 반쯤 미룬다.
    /// </summary>
    public void Damp(CrewMember c, Activity a, ref float score, ref string reason)
    {
        if (_felt.Count == 0 || score <= 0f) return;
        if (!_felt.TryGetValue(c.Id, out var f)) return;
        bool routine = a is RelaxActivity or ChatActivity or WanderActivity or HobbyActivity or TidyActivity or MendActivity or ReachOutActivity
            or InspectActivity or SceneActivity or SharedMealActivity or MemorialVisitActivity or RoomWorkActivity or LaundryActivity or PartTestActivity
            or ShipRoundsActivity or ExpeditionActivity;
        if (!routine && a is not DutyActivity) return;
        float mul;
        if (f.s >= IncidentScale.Cosmic) mul = a is DutyActivity ? 0.4f : 0.08f;
        else if (f.s >= IncidentScale.Ship) mul = a is DutyActivity ? 0.5f : 0.15f;
        else if (f.s >= IncidentScale.System && f.k.Workers.Contains(c.Id)) mul = a is DutyActivity ? 0.8f : 0.5f;
        else return;
        score *= mul;
        reason += f.s >= IncidentScale.Ship ? $" · {ScaleTable.Name(f.s)} 사고 — 일상을 멈춘다" : " · 계통 사고에 불려 왔다";
        if (routine) Halts++;
    }

    // ─────────────────────────────── 승무원이 규모를 느낀다 ───────────────────────────────

    /// <summary>누가 무엇을 아는가: 계통은 그 방 · 부른 사람 · 방송을 들은 사람, 배 전체부터는 깨어 있는 모두(경보) · 방송을 들은 사람.</summary>
    private void Feel()
    {
        var w = _w;
        _felt.Clear();
        foreach (var k in _open)
        {
            if (!k.Hot || k.Now < IncidentScale.System) continue;
            bool big = k.Now >= IncidentScale.Ship;
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Away || c.Down) continue;
                bool heard = k.BroadcastId >= 0 && w.Automation.Speak.Heard(c, k.BroadcastId);
                bool knows = heard || (big ? c.IsAwake || k.ShoutAt >= 0 : c.Room != null && k.Rooms.Contains(c.Room.Id) || k.Workers.Contains(c.Id));
                if (!knows) continue;
                if (!_felt.TryGetValue(c.Id, out var cur) || k.Now > cur.s) _felt[c.Id] = (k.Now, k);
                if (big && !k.Feared.Contains(c.Id))
                {
                    // 두려움: 배 전체가 흔들린다 · 하늘이 무너진다 (용감 · 침착하면 덜)
                    k.Feared.Add(c.Id);
                    Fears++;
                    float fear = k.Now >= IncidentScale.Cosmic ? 0.32f : 0.2f;
                    w.Brain2.Emotions.Feel(c, Feeling.Fear, fear, $"{k.Name} — {ScaleTable.Name(k.Now)}");
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + (k.Now >= IncidentScale.Cosmic ? 0.07f : 0.04f) * (1.3f - 0.6f * c.Traits.Bravery));
                }
            }
        }
    }
}
