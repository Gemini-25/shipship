namespace ShipEngine;

/// <summary>
/// 세계 하나. 엔진들을 (단계, 등록 순서)로 돌린다.
/// 바뀌지 않는 것(배 도면 · 데이터 표)은 <see cref="Setup"/>에, 바뀌는 것은 엔진들의 상태에 있다 —
/// 찍기는 바뀌는 것만 담고, 복제는 같은 준비로 새 세계를 만든 뒤 찍은 것을 읽어 넣는다.
/// </summary>
public sealed class Sim
{
    public SimSetup Setup { get; }
    public Layout Layout => Setup.Layout;
    public Tables Tables => Tables.Default;
    public ulong Seed => Setup.Seed;
    public long Tick { get; private set; }

    public Causes Causes { get; } = new();
    public Facts Facts { get; } = new();

    private readonly List<IEngine> _engines = new();
    private readonly Dictionary<Type, IEngine> _byType = new();
    private IEngine[] _order = Array.Empty<IEngine>();

    public IReadOnlyList<IEngine> Engines => _order;

    /// <summary>엔진마다 마지막 한 걸음에 쓴 시간 (ms · 성능 측정).</summary>
    public Dictionary<string, double> LastMs { get; } = new();
    public bool Measure { get; set; }

    public Sim(SimSetup setup)
    {
        Setup = setup;
        setup.Build(this);
    }

    public void Add(IEngine e)
    {
        _engines.Add(e);
        _byType[e.GetType()] = e;
        _order = _engines.Select((x, i) => (x, i)).OrderBy(p => p.x.Phase).ThenBy(p => p.i).Select(p => p.x).ToArray();
    }

    public T Get<T>() where T : class, IEngine => (T)_byType[typeof(T)];
    public T? Find<T>() where T : class, IEngine => _byType.TryGetValue(typeof(T), out var e) ? (T)e : null;

    public void Step()
    {
        foreach (var e in _order)
        {
            long k = ((Tick - e.Offset) % e.Every + e.Every) % e.Every;
            if (k != 0) continue;
            if (Measure)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                e.Step(this, e.Every * Time.SecondsPerTick);
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                LastMs[e.Name] = LastMs.GetValueOrDefault(e.Name) + ms;
            }
            else e.Step(this, e.Every * Time.SecondsPerTick);
        }
        Tick++;
    }

    public void Run(long ticks) { for (long i = 0; i < ticks; i++) Step(); }

    // ── 찍기 · 되살리기 · 복제 ──

    public byte[] Save()
    {
        var w = new StateWriter();
        w.Tag("sim");
        w.I64(Tick);
        Causes.Save(w);
        Facts.Save(w);
        foreach (var e in _engines) { w.Tag(e.Name); e.Save(w); }
        w.Tag("end");
        return w.ToArray();
    }

    public void Load(byte[] data)
    {
        var r = new StateReader(data);
        r.Tag("sim");
        Tick = r.I64();
        Causes.Load(r);
        Facts.Load(r);
        foreach (var e in _engines) { r.Tag(e.Name); e.Load(r); }
        r.Tag("end");
    }

    /// <summary>같은 준비로 새 세계를 만들고 지금 상태를 읽어 넣는다 (주컴퓨터 · 연출의 미래 돌려 보기).</summary>
    public Sim Clone()
    {
        var s = new Sim(Setup);
        s.Load(Save());
        return s;
    }

    public ulong Hash() => Fingerprint.Of(Save());
}

/// <summary>세계를 만드는 준비: 배 도면 · 시드 · 켤 엔진.</summary>
public sealed class SimSetup
{
    public Layout Layout { get; }
    public ulong Seed { get; }
    /// <summary>준비가 끝난 세계에 엔진을 붙인다 (시험은 일부만 붙일 수 있다).</summary>
    public Action<Sim> Build { get; }

    public SimSetup(Layout layout, ulong seed, Action<Sim> build)
    {
        Layout = layout; Seed = seed; Build = build;
    }
}
