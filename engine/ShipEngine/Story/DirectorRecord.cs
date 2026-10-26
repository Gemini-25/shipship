namespace ShipEngine;

/// <summary>
/// ⑬ 연출 기록 (docs/OBSERVER.md §3.1 · V1): 이어 보기 — 연출이 쌓아 온 모든 것 (하이라이트 · 박자 · 장 · 오늘 장 재료 · 처음 본 일 · 열린 사슬 · 하루 예산 · 카메라 · 미리 보기)을 찍고 되살린다.
/// 세계 찍기와 따로 판을 센다 (<see cref="RecordVersion"/>). 되살린 연출은 이어 보지 않은 연출과 다음 틱부터 같은 일을 한다 (하이라이트 · 장 · 카메라 — 법칙 '이어 보기' S1).
/// 사전 · 모음은 열쇠 차례로 쓴다 (같은 상태면 같은 바이트 — 시험이 두 연출을 바이트로 견준다). 하이라이트의 박자는 사슬의 박자 목록을 함께 쓰므로 목록마다 번호를 매겨 한 번만 쓴다 ·
/// 장의 장면은 하이라이트 번호로. 미리 보기의 앞선 세계는 찍기 바이트로 (다른 코어 몫을 기다려 받은 뒤).
/// 다시보기 고리 (찍기 30장)는 담지 않는다 — 되살린 뒤 첫 틱에 새로 찍는다. 재는 값 (미리 보기에 걸린 ms)도 담지 않는다.
/// </summary>
public sealed partial class Director
{
    /// <summary>연출 기록 판 (세계 판과 따로 — 연출의 상태가 바뀌면 올린다): 2 — 장의 분위기 · 잃은 이 · 사람 일 장부 (V3 · 판 1도 읽는다 — 없는 것은 빈 채로).</summary>
    public const int RecordVersion = 2;

    /// <summary>연출 기록을 쓴다 (작업 스레드 · 틱 사이 — 미리 보기의 다른 코어 몫을 기다려 받은 뒤).</summary>
    public void SaveRecord(StateWriter w)
    {
        JoinAhead();
        w.Tag("연출 기록"); w.I32(RecordVersion); w.I32(L.N);
        // 박자 목록 (사슬 · 하이라이트가 함께 쓴다 — 한 번만)
        var beatId = new Dictionary<List<Beat>, int>(ReferenceEqualityComparer.Instance);
        var beatLists = new List<List<Beat>>();
        int BeatsId(List<Beat>? l) { if (l == null) return -1; if (!beatId.TryGetValue(l, out int id)) { id = beatLists.Count; beatId[l] = id; beatLists.Add(l); } return id; }
        var chainKeys = Chains.Keys.OrderBy(k => k).ToList();
        foreach (int k in chainKeys) BeatsId(Chains[k].Beats);
        foreach (var c in BestChains) BeatsId(c.Beats);
        foreach (var h in Highlights) BeatsId(h.Beats);
        w.I32(beatLists.Count); foreach (var l in beatLists) w.List(l);

        // 관심 · 카메라 · 빠르기
        w.Arr(Heat); w.Arr(Aux);
        w.I32(FocusCell); w.I32(Zoom); w.F64(Speed); w.Str(Mood); w.Str(Caption);
        w.F64(DayMinutes); w.Bool(_ringLate); w.Bool(Foresee);
        w.I64(Switches); w.F64(ScreenSeconds); w.F64(CalmScreenSec); w.F64(OmenScreenSec); w.F64(DangerScreenSec); w.F64(CriticalScreenSec); w.F64(PropScreenSec);
        WriteSet(w, SeenKinds.Select(k => (int)k)); WriteSet(w, _seenTalk);
        w.I32(Firsts); w.I32(NearMisses);
        w.I64(_cursor); w.I32(_top); w.F32(_max); w.Bool(_resting); w.F64(_sinceSwitch); w.F64(_criticalLeft);
        w.I64(_lastOmen); w.I64(_lastDanger); w.I64(_lastHeavyOmen); w.I32(_omenRoot);
        w.I64(_budgetDay); w.F64(_screenToday); w.F64(_slowToday);
        w.I32(_wasInDanger.Count); foreach (var (k, v) in _wasInDanger.OrderBy(x => x.Key)) { w.I32(k); w.Byte(v); }
        w.I32(_lastNear.Count); foreach (var (k, v) in _lastNear.OrderBy(x => x.Key)) { w.I32(k); w.I64(v); }
        w.I32(_propShowLeft); w.Bool(_propNew); w.Bool(_propHot);
        w.I32(HourBest.Count); foreach (var (spot, kind) in HourBest) { w.F32(spot); w.I32((int)kind); }

        // 하이라이트
        w.I32(Highlights.Count);
        foreach (var h in Highlights)
        {
            w.I64(h.Tick); w.I64(h.StartTick); w.I64(h.Last); w.I32(h.Cell); w.F32(h.Score); w.I32((int)h.Kind); w.I32(h.Cause); w.I32(h.Actor); w.I32(h.Root);
            w.Str(h.Title); w.Bool(h.People); w.Str(h.Line); w.I32(BeatsId(h.Beats));
        }

        // 카메라 (DirectorCamera)
        w.I32(SceneRoot); w.I32(Subject); w.I32(Pulls); w.I32(CrossCuts); w.I32(AirWarnings); w.I64(DailyCuts);
        w.I32(_dailyRoom); w.F64(_dailyAt); w.F64(_cutAt); w.Arr(_roomShown); w.I64(FollowTicks);
        w.I32(_pullCell); w.I32(_pullRoot); w.I32(_pullActor); w.F64(_pullAt); w.F32(_pullScore); w.I64(_subjectQuiet);
        w.I32(_airKPa.Count); foreach (var (k, v) in _airKPa.OrderBy(x => x.Key)) { w.I32(k); w.I32(v.cell); w.F64(v.kpa); w.I64(v.tick); w.F64(v.rate); }
        w.I32(_airBase.Count); foreach (var (k, v) in _airBase.OrderBy(x => x.Key)) { w.I32(k); w.F32(v); }
        WriteCounts(w, AirWhy); w.Str(LastAir);
        w.I32(_recentN); w.I32(_recentAt);
        for (int i = 0; i < RecentSize; i++) { var r = _recent[i]; w.I32(r.cell); w.I32(r.root); w.I32(r.actor); w.F32(r.score); w.I64(r.tick); }

        // 사슬 (DirectorChains)
        w.I32(chainKeys.Count); foreach (int k in chainKeys) WriteChain(w, Chains[k], BeatsId);
        w.I32(BestChains.Count); foreach (var c in BestChains) WriteChain(w, c, BeatsId);
        w.Arr(ChainsByEngines);
        w.I32(Orphans.Count); foreach (var (k, v) in Orphans.OrderBy(x => x.Key)) { w.I32((int)k); w.I32(v); }
        w.I32(MissedKinds.Count); foreach (var (k, v) in MissedKinds.OrderBy(x => x.Key)) { w.I32((int)k); w.I32(v); }
        w.I32(Spreads); w.I32(Watched); w.I32(SeenBefore); w.I32(SeenAfter); w.I32(Missed); w.I32(MissedStory); w.I32(ChainWatched); w.I32(ChainSeen); w.F64(IdleScreenSec);
        w.I32(_pending.Count); foreach (var p in _pending) { w.I32(p.cell); w.F64(p.at); w.Bool(p.chain); w.I32((int)p.kind); w.I32(p.root); }
        w.I64(_lastPrune);

        // 장 (DirectorChapters)
        var hid = new Dictionary<Highlight, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < Highlights.Count; i++) hid[Highlights[i]] = i;
        w.I32(Chapters.Count);
        foreach (var ch in Chapters)
        {
            w.I32(ch.Day); w.Bool(ch.Week); w.I64(ch.From); w.I64(ch.To); w.Str(ch.Title); w.F32(ch.Peak); w.Str(ch.Arc);
            w.I32(ch.Lines.Count); foreach (var s in ch.Lines) w.Str(s);
            w.I32(ch.Scenes.Count); foreach (var h in ch.Scenes) w.I32(hid.GetValueOrDefault(h, -1));
            w.List(ch.Cast);
            w.I32(ch.Mood); w.List(ch.Lost);
        }
        w.I32(_day); w.I64(_dayFrom);
        w.I32(_dayDeaths.Count); foreach (var (a, c) in _dayDeaths) { w.I32(a); w.I32(c); }
        w.I32(_dayDecisions.Count); foreach (var (d, c) in _dayDecisions) { w.I32(d); w.I32(c); }
        w.I32(_dayVoyage.Count); foreach (var s in _dayVoyage) w.Str(s);
        w.I32(_dayMoods.Count); foreach (var s in _dayMoods) w.Str(s);
        w.I32(_episodePeak); w.I32(_omenRun); w.I64(_episodePeakAt);
        w.I32(_dayCast.Count); foreach (var (k, v) in _dayCast.OrderBy(x => x.Key)) { w.I32(k); w.F32(v); }
        w.I32(_dayMood); People.Save(w);

        // 미리 보기 (DirectorForesight) — 앞선 세계는 찍기 바이트로
        w.I32(Foresights); w.I32(Predicted); w.I32(ForeCuts); w.I32(Resyncs); w.I64(AheadSteps);
        WriteCounts(w, BrewWhy);
        w.I64(_aheadCursor); w.I64(_lastBrew); w.I64(_foreHold);
        w.I32(_aheadTotals.Count); foreach (var (t, f) in _aheadTotals) { w.I64(t); w.I64(f); }
        WriteFuture(w, _future); WriteFuture(w, _pendFuture);
        w.I32(_pendTotals.Count); foreach (var (t, f) in _pendTotals) { w.I64(t); w.I64(f); }
        w.I32(_pendSteps); w.I32(_pendPredicted); w.I64(_pendCursor);
        w.Bool(_ahead != null);
        if (_ahead != null) w.Arr(_ahead.Save());
        w.Tag("연출 기록 끝");
    }

    /// <summary>연출 기록을 읽어 넣는다 (새로 지은 연출에 · sim은 되살린 산 세계 — 미리 보기의 앞선 세계를 같은 준비로 짓는다). 판 · 배가 다르면 거짓 (아무것도 바꾸지 않는다).</summary>
    public bool LoadRecord(StateReader r, Sim sim)
    {
        r.Tag("연출 기록");
        int ver = r.I32();
        if (ver is < 1 or > RecordVersion || r.I32() != L.N) return false;
        JoinAhead();
        int nb = r.I32();
        var beatLists = new List<List<Beat>>(nb);
        for (int i = 0; i < nb; i++) beatLists.Add(r.List<Beat>());
        var shared = new Dictionary<int, List<Beat>>();   // 박자 번호 → 연출 안의 목록 (사슬이 먼저 제 목록을 내놓는다)
        List<Beat>? Beats(int id) => id < 0 ? null : shared.TryGetValue(id, out var l) ? l : shared[id] = new List<Beat>(beatLists[id]);

        r.ArrInto(Heat); r.ArrInto(Aux);
        FocusCell = r.I32(); Zoom = r.I32(); Speed = r.F64(); Mood = r.Str() ?? ""; Caption = r.Str() ?? "";
        DayMinutes = r.F64(); _ringLate = r.Bool(); Foresee = r.Bool();
        Switches = r.I64(); ScreenSeconds = r.F64(); CalmScreenSec = r.F64(); OmenScreenSec = r.F64(); DangerScreenSec = r.F64(); CriticalScreenSec = r.F64(); PropScreenSec = r.F64();
        SeenKinds.Clear(); foreach (int k in ReadSet(r)) SeenKinds.Add((FactKind)k);
        _seenTalk.Clear(); foreach (int k in ReadSet(r)) _seenTalk.Add(k);
        Firsts = r.I32(); NearMisses = r.I32();
        _cursor = r.I64(); _top = r.I32(); _max = r.F32(); _resting = r.Bool(); _sinceSwitch = r.F64(); _criticalLeft = r.F64();
        _lastOmen = r.I64(); _lastDanger = r.I64(); _lastHeavyOmen = r.I64(); _omenRoot = r.I32();
        _budgetDay = r.I64(); _screenToday = r.F64(); _slowToday = r.F64();
        _wasInDanger.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _wasInDanger[r.I32()] = r.Byte();
        _lastNear.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _lastNear[r.I32()] = r.I64();
        _propShowLeft = r.I32(); _propNew = r.Bool(); _propHot = r.Bool();
        HourBest.Clear(); for (int i = 0, n = r.I32(); i < n; i++) HourBest.Add((r.F32(), (FactKind)r.I32()));

        // 하이라이트 (박자는 사슬을 읽은 뒤에 잇는다)
        Highlights.Clear();
        var hBeats = new List<int>();
        for (int i = 0, n = r.I32(); i < n; i++)
        {
            var h = new Highlight { Tick = r.I64(), StartTick = r.I64(), Last = r.I64(), Cell = r.I32(), Score = r.F32(), Kind = (FactKind)r.I32(), Cause = r.I32(), Actor = r.I32(), Root = r.I32(),
                Title = r.Str() ?? "", People = r.Bool(), Line = r.Str() ?? "" };
            hBeats.Add(r.I32());
            Highlights.Add(h);
        }

        // 카메라
        SceneRoot = r.I32(); Subject = r.I32(); Pulls = r.I32(); CrossCuts = r.I32(); AirWarnings = r.I32(); DailyCuts = r.I64();
        _dailyRoom = r.I32(); _dailyAt = r.F64(); _cutAt = r.F64();
        var shown = r.Arr<double>();
        if (shown.Length > 0) { RoomsInit(shown.Length); Array.Copy(shown, _roomShown, shown.Length); }
        else { _roomAwake = Array.Empty<int>(); _roomAll = Array.Empty<int>(); _roomShown = Array.Empty<double>(); _roomBox = Array.Empty<(int, int, int, int)>(); }
        FollowTicks = r.I64();
        _pullCell = r.I32(); _pullRoot = r.I32(); _pullActor = r.I32(); _pullAt = r.F64(); _pullScore = r.F32(); _subjectQuiet = r.I64();
        _airKPa.Clear(); for (int i = 0, n = r.I32(); i < n; i++) { int k = r.I32(); _airKPa[k] = (r.I32(), r.F64(), r.I64(), r.F64()); }
        _airBase.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _airBase[r.I32()] = r.F32();
        ReadCounts(r, AirWhy); LastAir = r.Str() ?? "";
        _recentN = r.I32(); _recentAt = r.I32();
        for (int i = 0; i < RecentSize; i++) _recent[i] = (r.I32(), r.I32(), r.I32(), r.F32(), r.I64());

        // 사슬
        Chains.Clear(); for (int i = 0, n = r.I32(); i < n; i++) { var c = ReadChain(r, beatLists, shared); Chains[c.Root] = c; }
        BestChains.Clear(); for (int i = 0, n = r.I32(); i < n; i++) BestChains.Add(ReadChain(r, beatLists, shared));
        r.ArrInto(ChainsByEngines);
        Orphans.Clear(); for (int i = 0, n = r.I32(); i < n; i++) Orphans[(FactKind)r.I32()] = r.I32();
        MissedKinds.Clear(); for (int i = 0, n = r.I32(); i < n; i++) MissedKinds[(FactKind)r.I32()] = r.I32();
        Spreads = r.I32(); Watched = r.I32(); SeenBefore = r.I32(); SeenAfter = r.I32(); Missed = r.I32(); MissedStory = r.I32(); ChainWatched = r.I32(); ChainSeen = r.I32(); IdleScreenSec = r.F64();
        _pending.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _pending.Add((r.I32(), r.F64(), r.Bool(), (FactKind)r.I32(), r.I32()));
        _lastPrune = r.I64();
        for (int i = 0; i < Highlights.Count; i++) Highlights[i].Beats = Beats(hBeats[i]);

        // 장
        Chapters.Clear();
        for (int i = 0, n = r.I32(); i < n; i++)
        {
            var ch = new Chapter { Day = r.I32(), Week = r.Bool(), From = r.I64(), To = r.I64(), Title = r.Str() ?? "", Peak = r.F32(), Arc = r.Str() ?? "" };
            for (int k = 0, m = r.I32(); k < m; k++) ch.Lines.Add(r.Str() ?? "");
            for (int k = 0, m = r.I32(); k < m; k++) { int hi = r.I32(); if (hi >= 0 && hi < Highlights.Count) ch.Scenes.Add(Highlights[hi]); }
            ch.Cast.AddRange(r.List<int>());
            if (ver >= 2) { ch.Mood = r.I32(); ch.Lost.AddRange(r.List<int>()); }
            Chapters.Add(ch);
        }
        _day = r.I32(); _dayFrom = r.I64();
        _dayDeaths.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _dayDeaths.Add((r.I32(), r.I32()));
        _dayDecisions.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _dayDecisions.Add((r.I32(), r.I32()));
        _dayVoyage.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _dayVoyage.Add(r.Str() ?? "");
        _dayMoods.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _dayMoods.Add(r.Str() ?? "");
        _episodePeak = r.I32(); _omenRun = r.I32(); _episodePeakAt = r.I64();
        _dayCast.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _dayCast[r.I32()] = r.F32();
        if (ver >= 2) { _dayMood = r.I32(); People.Load(r); } else { _dayMood = 0; People.Clear(); }

        // 미리 보기
        Foresights = r.I32(); Predicted = r.I32(); ForeCuts = r.I32(); Resyncs = r.I32(); AheadSteps = r.I64();
        ReadCounts(r, BrewWhy);
        _aheadCursor = r.I64(); _lastBrew = r.I64(); _foreHold = r.I64();
        _aheadTotals.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _aheadTotals.Enqueue((r.I64(), r.I64()));
        ReadFuture(r, _future); ReadFuture(r, _pendFuture);
        _pendTotals.Clear(); for (int i = 0, n = r.I32(); i < n; i++) _pendTotals.Add((r.I64(), r.I64()));
        _pendSteps = r.I32(); _pendPredicted = r.I32(); _pendCursor = r.I64();
        _ahead = null;
        if (r.Bool())
        {
            _ahead = new Sim(sim.Setup); _ahead.Load(r.Arr<byte>());
            if (_ahead.Find<ShipMind>() is { } aheadMind) aheadMind.Inline = true;   // 미리 보기 복제는 일꾼 없이 (Foresight와 같게)
        }
        r.Tag("연출 기록 끝");
        Ring.Clear();   // 다시보기 고리는 담지 않는다 — 첫 틱에 새로 찍는다
        return true;
    }

    private static void WriteSet(StateWriter w, IEnumerable<int> set) { var a = set.OrderBy(x => x).ToArray(); w.Arr(a); }
    private static int[] ReadSet(StateReader r) => r.Arr<int>();

    private static void WriteCounts(StateWriter w, Dictionary<string, int> d)
    {
        w.I32(d.Count);
        foreach (var (k, v) in d.OrderBy(x => x.Key, StringComparer.Ordinal)) { w.Str(k); w.I32(v); }
    }

    private static void ReadCounts(StateReader r, Dictionary<string, int> d)
    {
        d.Clear();
        for (int i = 0, n = r.I32(); i < n; i++) { string k = r.Str() ?? ""; d[k] = r.I32(); }
    }

    private static void WriteFuture(StateWriter w, List<(long tick, int cell, float w, FactKind kind)> l)
    {
        w.I32(l.Count);
        foreach (var p in l) { w.I64(p.tick); w.I32(p.cell); w.F32(p.w); w.I32((int)p.kind); }
    }

    private static void ReadFuture(StateReader r, List<(long tick, int cell, float w, FactKind kind)> l)
    {
        l.Clear();
        for (int i = 0, n = r.I32(); i < n; i++) l.Add((r.I64(), r.I32(), r.F32(), (FactKind)r.I32()));
    }

    private static void WriteChain(StateWriter w, CauseChain c, Func<List<Beat>?, int> beatsId)
    {
        w.I32(c.Root); w.I64(c.First); w.I64(c.Last); w.I32(c.Engines); w.List(c.Order);
        w.I32(c.Facts); w.I32(c.Depth); w.I32(c.Cause); w.F32(c.Weight);
        WriteSet(w, c.Links); WriteSet(w, c.Leaves);
        w.I32(c.Line); w.I32(c.LineCause); w.Str(c.LinePath); w.I32(beatsId(c.Beats));
    }

    private static CauseChain ReadChain(StateReader r, List<List<Beat>> beatLists, Dictionary<int, List<Beat>> shared)
    {
        var c = new CauseChain { Root = r.I32(), First = r.I64(), Last = r.I64(), Engines = r.I32() };
        c.Order.AddRange(r.List<byte>());
        c.Facts = r.I32(); c.Depth = r.I32(); c.Cause = r.I32(); c.Weight = r.F32();
        c.Links.UnionWith(ReadSet(r)); c.Leaves.UnionWith(ReadSet(r));
        c.Line = r.I32(); c.LineCause = r.I32(); c.LinePath = r.Str() ?? "";
        int id = r.I32();
        if (id >= 0) { c.Beats.AddRange(beatLists[id]); shared[id] = c.Beats; }   // 이 사슬의 박자 목록을 하이라이트가 함께 쓴다
        return c;
    }
}
