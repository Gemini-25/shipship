namespace ShipEngine;

/// <summary>
/// 계산 일꾼 (6단 · docs/ENGINE.md §3-10): 봉쇄 상상 (기다림 · 지금 닫음 두 복제 × 125틱)을 틱 t에 찍어 다른 코어에 맡기고 t + ThinkTicks에 받는다.
/// 받을 틱에 아직 셈 중이면 그 틱에서 기다린다 (관전 화면은 <see cref="Waiting"/>을 보고 그 틱을 넘지 않고 그림만 그린다) — 결과와 받는 틱은 기계 빠르기와 상관없다 (1코어 = 4코어).
/// 찍을 때 맡긴 셈이 있으면 끝을 기다려 결과를 함께 찍는다 (생각 중 저장 왕복). 복제 안(Imagining)에서는 맡기지 않는다.
/// </summary>
public sealed partial class ShipMind
{
    /// <summary>맡긴 셈을 받는 틱 (1틱 = 2.4초 — 뚫린 방 곁 쓰러진 사람을 위한 '지금 닫음'은 몇 초도 기압이다 · 늦은 셈은 세계만 멈춤이 받아 그림은 늘 60프레임).</summary>
    public const int ThinkTicks = 1;
    /// <summary>시험용: 일꾼을 이만큼 늦춘다 (ms — 늦은 일꾼에도 그림이 계속 나오는지).</summary>
    public static int WorkerDelayMs;
    /// <summary>일꾼 없이 그 틱에 바로 돌린다 (연출의 미리 보기 복제 — 이미 다른 코어에서 돈다 · 결과 · 받는 틱은 같다). 찍지 않는다.</summary>
    public bool Inline;

    /// <summary>맡긴 셈 하나: 방 · 사건 · 맡긴 틱 · 받을 틱 · 두 미래 (끝났으면).</summary>
    public struct Thought
    {
        public int Room, Incident;
        public long Start, Due;
        public Imagined Wait, Now;
        /// <summary>반사로 닫은 문 (그 방 문 목록의 차례 — 기다림을 고르면 다시 연다).</summary>
        public ulong ReflexMask;
    }
    /// <summary>센서 한 번 읽는 사이 (12초) 이보다 많이 빠지면 그려 보는 동안 문을 먼저 닫는다 (빠른 구멍 21kPa · 천천히 새는 방 3kPa — 모르면 안전한 쪽).</summary>
    public const float ReflexKPa = 8f;
    private readonly List<Thought> _thoughts = new();
    private readonly List<Task<(Imagined wait, Imagined now)>?> _thoughtJobs = new();
    private readonly List<ImagineEnd[]?> _thoughtEnds = new();   // 화면용 상상 그림 (NoteOn일 때만 · 찍지 않는다 — 되살리면 없다)

    /// <summary>맡긴 셈 수 (받기 전).</summary>
    public int Thinking => _thoughts.Count;
    /// <summary>맡긴 셈들 (화면이 읽기만 — 방 · 사건 · 맡긴 틱 · 받을 틱 · docs/UI2.md §4.4 · §4.5).</summary>
    public IReadOnlyList<Thought> Thoughts => _thoughts;
    /// <summary>재기용 (찍지 않는다): 맡긴 수 · 그 가운데 일꾼에서 돈 수.</summary>
    public long ThoughtsStarted { get; private set; }
    public long ThoughtsOnWorker { get; private set; }

    /// <summary>틱 tick을 돌리면 아직 셈 중인 일꾼을 기다려야 한다.</summary>
    public bool Waiting(long tick)
    {
        for (int i = 0; i < _thoughts.Count; i++)
            if (_thoughts[i].Due <= tick && _thoughtJobs[i] is { IsCompleted: false }) return true;
        return false;
    }

    /// <summary>일꾼이 셈 중이다 (찍으면 기다린다 — 연출은 이때 미리 보기 복제 · 다시보기 찍기를 미룬다).</summary>
    public bool WorkerBusy
    {
        get { foreach (var j in _thoughtJobs) if (j is { IsCompleted: false }) return true; return false; }
    }

    /// <summary>맡긴 셈이 모두 끝날 때까지 기다려 결과를 받아 둔다 (찍기 전 · 시험).</summary>
    public void WaitThoughts() { for (int i = 0; i < _thoughts.Count; i++) Settle(i); }

    private void Settle(int i)
    {
        var job = _thoughtJobs[i];
        if (job == null) return;
        var (w, n) = job.Result;
        var th = _thoughts[i]; th.Wait = w; th.Now = n; _thoughts[i] = th;
        _thoughtJobs[i] = null;
    }

    private int ThoughtFor(int room)
    {
        for (int i = 0; i < _thoughts.Count; i++) if (_thoughts[i].Room == room) return i;
        return -1;
    }

    /// <summary>받을 틱이 된 셈을 받는다 (아직 셈 중이면 기다린다).</summary>
    private (Imagined wait, Imagined now, ulong reflex) TakeThought(int i, out ImagineEnd[]? ends)
    {
        Settle(i);
        var th = _thoughts[i];
        ends = i < _thoughtEnds.Count ? _thoughtEnds[i] : null;
        _thoughts.RemoveAt(i); _thoughtJobs.RemoveAt(i); if (i < _thoughtEnds.Count) _thoughtEnds.RemoveAt(i);
        return (th.Wait, th.Now, th.ReflexMask);
    }

    /// <summary>반사로 닫은 문을 다시 연다 (기다림을 골랐다 · 받지 못하고 버린 셈).</summary>
    private void Unreflex(Sim sim, int room, ulong mask, int incident)
    {
        if (mask == 0 || _st == null) return;
        if (Incidents.Exists(y => y.Active != 0 && y.Room == room && y.Sealed != 0)) return;   // 그사이 봉쇄했다 (같은 방의 다른 사건 · 방침) — 닫힌 채로
        var doors = _roomDoors[room];
        for (int k = 0; k < doors.Length && k < 64; k++) if ((mask & 1UL << k) != 0) _st.RequestDoor(doors[k], 1);
        Say(sim, $"{RoomName(room)}: 닫아 두었던 문을 다시 연다 (기다리는 쪽이 낫다)", NoteKind.Door, -1, incident);
    }

    private void DropStaleThoughts(Sim sim)
    {
        long tick = sim.Tick;
        for (int i = _thoughts.Count - 1; i >= 0; i--)
            if (_thoughts[i].Due < tick) { Settle(i); var th = _thoughts[i]; _thoughts.RemoveAt(i); _thoughtJobs.RemoveAt(i); if (i < _thoughtEnds.Count) _thoughtEnds.RemoveAt(i); Unreflex(sim, th.Room, th.ReflexMask, th.Incident); }
    }

    /// <summary>틱 t에 맡긴다: 주 스레드는 세계를 찍기만 하고 (1코어 · 미리 보기 복제면 그 자리에서 돌린다) · 일꾼이 그 바이트로 복제 둘을 만들어 돌린다.</summary>
    private void StartThinking(Sim sim, int room, int incident)
    {
        ThoughtsStarted++;
        var th = new Thought { Room = room, Incident = incident, Start = sim.Tick, Due = sim.Tick + ThinkTicks };
        var ends = NoteOn && !Imagining ? new[] { new ImagineEnd(), new ImagineEnd() } : null;   // 화면용 상상 그림 (셈에는 쓰지 않는다)
        if (Par.Threads <= 1 || Inline)
        {
            (th.Wait, th.Now) = Imagine(sim, room, ends);   // 찍은 뒤에 반사한다 (두 미래는 반사 없는 지금에서)
            Reflex(sim, room, incident, ref th);
            _thoughts.Add(th); _thoughtJobs.Add(null); _thoughtEnds.Add(ends);
            return;
        }
        byte[]? snap = null;
        int len = sim.SaveInto(ref snap);   // 찍기 (맡긴 셈은 한 번에 하나라 기다릴 것이 없다)
        var setup = sim.Setup; var layout = L; int[] doors = _roomDoors[room]; long tick = sim.Tick;
        ThoughtsOnWorker++;
        var job = Task.Factory.StartNew(() =>
        {
            Sim.InWorker = true;
            var thread = Thread.CurrentThread; var pri = thread.Priority;
            try
            {
                thread.Priority = ThreadPriority.BelowNormal;   // 세계 · 그리기 스레드보다 뒤 (일꾼은 한 줄)
                if (WorkerDelayMs > 0) Thread.Sleep(WorkerDelayMs);
                var w0 = new Sim(setup); w0.Load(snap!, len);
                var w1 = new Sim(setup); w1.Load(snap!, len);
                return ImagineRuns(layout, doors, room, tick, Imagined(w0), Imagined(w1), true, ends);
            }
            finally { thread.Priority = pri; Sim.InWorker = false; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Reflex(sim, room, incident, ref th);
        _thoughts.Add(th); _thoughtJobs.Add(job); _thoughtEnds.Add(ends);
    }

    /// <summary>반사: 기압이 빠르게 빠지는 방은 생각하는 동안 문을 먼저 닫는다 (한 틱 늦은 '지금 닫음'이 옆 방 기압을 65 → 42kPa로) · 기다림을 고르면 다시 연다. 찍은 뒤에 (두 미래는 반사 없는 지금에서).</summary>
    private void Reflex(Sim sim, int room, int incident, ref Thought th)
    {
        if (_st != null && RoomKPaPrev[room] - RoomKPa[room] > ReflexKPa)
        {
            var doors0 = _roomDoors[room];
            for (int k = 0; k < doors0.Length && k < 64; k++)
                if (F.DoorOpen[doors0[k]] > 0 || _st.DoorTarget[doors0[k]] > 0) { _st.RequestDoor(doors0[k], 0); th.ReflexMask |= 1UL << k; }
            if (th.ReflexMask != 0) Say(sim, $"{RoomName(room)}: 기압이 빠르게 빠진다 — 두 미래를 그려 보는 동안 문을 먼저 닫는다", NoteKind.Seal, -1, incident);
        }
    }

    private void SaveThoughts(StateWriter w)
    {
        WaitThoughts();   // 생각 중 찍기: 끝을 기다려 결과를 함께 찍는다 (되살린 세계는 같은 틱에 같은 결과를 받는다)
        w.Tag("생각"); w.List(_thoughts);
    }

    private void LoadThoughts(StateReader r)
    {
        r.Tag("생각");
        _thoughts.Clear(); _thoughts.AddRange(r.List<Thought>());
        _thoughtJobs.Clear(); _thoughtEnds.Clear(); for (int i = 0; i < _thoughts.Count; i++) { _thoughtJobs.Add(null); _thoughtEnds.Add(null); }
    }
}
