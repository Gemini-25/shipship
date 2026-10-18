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
    public Book Book { get; } = new();      // ⑪ 물자 장부 — 엔진들이 만들고 쓰는 곳마다 적는다

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

    /// <summary>
    /// 저장 판 — 엔진의 저장 모양(무엇을 어떤 순서 · 꼴로 쓰나)이 바뀌면 올린다. 다른 판의 저장은 읽지 않고 분명한 말로 거절한다.
    /// <see cref="SaveShape"/>는 그 판의 모양 지문 (법칙 시험 '저장 판'이 지금 모양과 견준다 — 다르면 판을 올리고 지문을 고친다).
    /// </summary>
    public const int SaveVersion = 46; // 2: 장부 흐름 '우주로' · 종류 수를 지문에 / 3: 항해 (호만 전이) / 4: 센서 선 / 5: 주컴퓨터 일지 / 6: 샘 판단 (공기를 준 방 · 읽기 사이 열린 문) / 7: 끌림 · 짝 / 8: 무리 · 지도자 / 9: 바깥문 지키기 · 조리대 전원 / 10: 요리 · 조심 / 11: 몸의 시계 · 쉼 / 12: 함께 먹기 · 따라 나가기 / 13: 장소의 기억 / 14: 따라 나선 때 / 15: 단골 자리 / 16: 주컴퓨터 — 사람에게 부탁 · 제 몸 · 이산화탄소 머무름 / 17: 배급 · 보관함 무게 · 막힌 곳 · 물 회수율 / 18: 돌봄 · 감염 · 옮는 병 · 잠의 질 / 19: 이야기꾼의 막 · 실 · 배우기 · 숨긴 실수 · 에어락 비우기 감시 · 예비 공기 참음 / 20: 소리 — 바탕 소음(이어지는 소리 · 방마다 퍼진 소리) · 폭발 소리 · 가림 · 이상한 기계 소리 알림 · 미리 간 수 / 21: 19와 20을 합침 / 22: ⑤ 기동 — 배의 질량 · 추진제 · 점화 · 항로 계획 · 다른 배 · 운석 번호 · 넘어짐 · 조타 · 조난선 씨앗 · 사실 종류 누계 128 / 23: ⑥ 고쳐 쓰기 — 연장선 · 떼어 낸 부품 · 로봇 부품 주머니 · 어두운 방 시각 / 24: ⑧ 인생 목표 (사람마다 목표 · 이룸) · 이룬 이 · 흔들린 이 수 / 25: ④ 금(파리스 · 응력 부식) · 조각 · 바닥 판 · 설비 부서짐 · 열 끼임 · 끊긴 칸 / 26: ⑧ · ⑨ 거짓말과 속임 — 숨긴 일 · 거짓말 · 들은 이 · 기록 물음 · 손본 이 · 들킨 적 · 주컴퓨터 정비 기록 / 27: 숨 쉴 공기 — 산소 마스크(몸) · 방마다 공기를 앗아 간 원인 · 방마다 긴급 공기 · 마스크 쓴 수 / 28: ⑧ 오래 남는 배움 — 사고의 교훈 셋 · 솜씨마다 마지막으로 쓴 때 · 가장 높았던 값 · 비상 훈련 / 29: ⑩ 우주 날씨 대비 — 대피(주컴퓨터 · 사람마다 따를지) / 30: ⑨ 회의 · 재판 · 선거 · 반란 — 열린 회의 · 대표 · 임기 · 모이기 · 벌 당번 · 근신 · 배급 거부 / 31: ⑪ 막힌 곳마다 원인 마디 (일감판 · 주컴퓨터) / 32: ⑩ 비워진 방은 닫아 둔다 (방마다 · 번) / 33: ⑨ 잠을 깨운 말소리 (번) / 34: ⑨ 곁에서 본 다툼 — 편듦 · 말림 (번) / 35: ⑧ 고립의 피로 (사람마다) · 사실 커서 · 지친 이 수 / 36: ⑧ · ⑩ 판단 엔진 막힘 — 사람 · 로봇마다 막힌 까닭 · 자리 · 대상 · 때 · 목표 · 주컴퓨터 사건마다 읽은 막힘 · 보낸 때 / 37: 판단 엔진 지식 · 풀이 — 사람마다 분야를 아는 정도 · 사건 번호 · 해 본 방법 · 지어낸 계획의 걸음 · 공기 되돌리기(예비 탱크로 되돌린 몰) · 주컴퓨터가 잠근 문 · 출입 금지 방 / 38: 판단 엔진 (마) — 계획의 몸이 된 사람 · 출입 금지에서 뺀 이 / 39: 벽이 못 버텨 닫아 둔 방 (번) / 40: 사람의 궁리 (마-3) — 궁리 셈 · 주컴퓨터 계획이 있는 방 · 사람마다 궁리한 때 · 제 궁리 · 계획 걸음의 바뀌지 않는 번호 · 몸이 있어야 할 방 · 주컴퓨터가 받은 물음 / 41: 판단 엔진 (바) 매뉴얼 — 매뉴얼 항목 · 안건 줄 · 지금 안건 · 주컴퓨터가 지어 본 방법 · 계획 걸음의 방법 번호 · 사실 종류 셋 / 42: ⑥ 칸마다 선 · 관 — 끊긴 칸 · 새는 틈 · 원인 · 아는 샘 · 섬마다 공급 · 사실 종류 하나 / 43: ⑥ 고장 원인 — 까닭별 마모 · 까닭의 원인 · 정비 뒤 돈 시간 · 망가진 원인 · 방마다 마지막 불 · 전선 피복 나이 · 피복을 달군 원인 · 과부하 마디 / 44: ② 이어진 탈 것 · 불꽃 앞머리 (피복을 타고 기는 불) / 45: ⑥ 관 녹 · 밸브 · ⑩ 새는 관 찾기 / 46: ④ 칸 강체 — 몸(자세 · 빠르기 · 각속도 · 질량 · 회전관성) · 기체 · 점화 운동량 장부
    public const ulong SaveShape = 0x490dd0af2928bff0;

    public byte[] Save()
    {
        var w = new StateWriter();
        SaveTo(w);
        return w.ToArray();
    }

    /// <summary>저장 모양 지문: 값을 빼고 무엇을 어떤 꼴로 쓰는지만 (표준 세계 하나로 잰다 — 배 도면이 바뀌어도 바뀐다: 그 저장은 새 배에 맞지 않으니 판을 올리는 게 맞다).</summary>
    public ulong ShapeOf()
    {
        var w = new StateWriter(1 << 16) { Shape = new() };
        SaveTo(w);
        // 배열 길이를 정하는 종류 수도 (장부 흐름 · 물자 · 사실 · 원인 — 늘면 옛 저장의 배열이 어긋난다)
        w.Shape.Add($"흐름{Book.Flows} 물자{Book.Bulk} 사실{Enum.GetValues<FactKind>().Length} 원인{Enum.GetValues<CauseKind>().Length}");
        return Fingerprint.Of(System.Text.Encoding.UTF8.GetBytes(string.Join("|", w.Shape)));
    }

    private void SaveTo(StateWriter w)
    {
        w.Tag("판");
        w.I32(SaveVersion);
        w.Tag("sim");
        w.I64(Tick);
        Causes.Save(w);
        Facts.Save(w);
        Book.Save(w);
        foreach (var e in _engines) { w.Tag(e.Name); e.Save(w); }
        w.Tag("end");
    }

    public void Load(byte[] data)
    {
        var r = new StateReader(data);
        if (!r.IsTag("판")) throw new InvalidDataException($"판 번호가 없는 저장이다 (판을 붙이기 전에 쓴 것) — 이 엔진은 저장 판 {SaveVersion}만 읽는다");
        int version = r.I32();
        if (version != SaveVersion) throw new InvalidDataException($"저장 판 {version} — 이 엔진은 저장 판 {SaveVersion}만 읽는다");
        r.Tag("sim");
        Tick = r.I64();
        Causes.Load(r);
        Facts.Load(r);
        Book.Load(r);
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
