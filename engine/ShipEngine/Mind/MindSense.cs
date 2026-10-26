namespace ShipEngine;

/// <summary>⑧ 마음 — 감각 · 믿음 · 외침 · 본능.</summary>
public sealed partial class Mind
{
    private readonly List<int> _fresh = new();   // 이번 틱 새로 생긴 급한 믿음 (믿음 목록의 번호)
    private readonly HashSet<int> _sparks = new();
    private int[] _devAt = Array.Empty<int>();

    /// <summary>문 너머(기압이 낮은 쪽)가 바깥 문이 닫힌 에어락인가 — 비운 에어락으로 새는 문틈은 배를 빼지 않는다 (바깥 문이 열려 있으면 다르다).</summary>
    private bool ClosedAirlockBeyond(int door)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(door, nb);
        int low = -1; double lp = double.MaxValue;
        foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor && F.Pressure(j) < lp) { lp = F.Pressure(j); low = j; }
        if (low < 0 || L.Room[low] < 0) return false;
        bool external = false;
        foreach (int dc in _roomDoors[L.Room[low]])
        {
            int d = L.DoorAt[dc];
            if (d < 0 || !L.Doors[d].External) continue;
            if (F.DoorOpen[d] > 0 || F.DoorBleed[d] != 0 || (_st != null && _st.DoorJam[d] != 0)) return false;
            external = true;
        }
        return external;
    }

    /// <summary>문 양쪽의 기압 차 (kPa).</summary>
    private double DoorDeltaKPa(int door)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(door, nb);
        double hi = 0, lo = double.MaxValue;
        foreach (int j in nb)
            if (j >= 0 && (L.Kind[j] == CellKind.Floor || L.Kind[j] == CellKind.Outside)) { double p = L.Kind[j] == CellKind.Outside ? 0 : F.Pressure(j); hi = Math.Max(hi, p); lo = Math.Min(lo, p); }
        return lo == double.MaxValue ? 0 : hi - lo;
    }

    private static bool Urgent(BeliefKind k) => k is BeliefKind.Fire or BeliefKind.Breach or BeliefKind.Vacuum or BeliefKind.Injured or BeliefKind.Dead or BeliefKind.Smoke or BeliefKind.Spark;

    // ───────────────────────── 믿음 ─────────────────────────

    /// <summary>믿음을 더하거나 고친다 — 같은 종류가 가까우면(3칸 · 대상이 있으면 같은 대상) 합친다. 새 믿음이면 참.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    public bool Learn(Agent a, long tick, BeliefKind kind, int cell, int subject, float conf, BeliefSource src, int cause = 0, float size = 0)
    {
        if (src is BeliefSource.Told or BeliefSource.Screen)
            foreach (var d in a.Doubts)
                if (d.Kind == kind && d.Until > tick && Dist(d.Cell, cell) <= 4) { conf *= 0.2f; break; } // 가 봤는데 없었다
        if (src == BeliefSource.Screen && _soc != null && a.IsPerson) conf *= 0.3f + 0.7f * _soc.Trust(a.Id); // 주컴퓨터를 믿는 만큼만 (⑨)
        if (conf <= 0.02f) return false;
        var list = a.Beliefs;
        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            if (b.Kind != kind) continue;
            bool same = subject >= 0 ? b.Subject == subject : b.Subject < 0 && Dist(b.Cell, cell) <= 3;
            if (!same) continue;
            bool wasWeak = b.Conf < 0.3f;
            if (conf >= b.Conf || src == BeliefSource.Saw) { b.Cell = cell; if (!(kind == BeliefKind.Water && b.Src == BeliefSource.Saw)) b.Src = src; }   // 본 고인 물은 미끄러져도 '본 것' — 마르면 봐서 지운다 (C13 검토 고침)
            b.Conf = Math.Max(b.Conf, conf);
            b.Seen = tick;
            if (cause != 0) b.Cause = cause;
            if (size > 0) b.Size = size;
            list[i] = b;
            if (wasWeak && b.Conf >= 0.5f && Urgent(kind)) _fresh.Add(i);
            if (wasWeak && b.Conf >= 0.3f) FlashCue(a, kind, b.Cause);   // ⑧ 그날이 되살아난다
            if (b.Conf >= 0.5f) NoteCrisis(a, tick, kind, b.Cell, b.Cause, src);   // DF7 위기 뒤: 그 일을 기억한다 (같은 일이면 겪었는지만)
            return wasWeak && b.Conf >= 0.5f;
        }
        var nb = new Belief { Kind = kind, Src = src, Cell = cell, Subject = subject, Conf = Math.Min(1, conf), Seen = tick, Cause = cause, Size = size };
        if (conf >= 0.25f) FlashCue(a, kind, cause);   // ⑧ 그날이 되살아난다
        if (conf >= 0.5f) NoteCrisis(a, tick, kind, cell, cause, src);   // DF7 위기 뒤: 그 일을 기억한다
        if (list.Count >= MaxBeliefs)
        {
            int weakest = 0;
            for (int i = 1; i < list.Count; i++) if (list[i].Conf < list[weakest].Conf) weakest = i;
            list[weakest] = nb;
            if (Urgent(kind) && conf >= 0.5f) _fresh.Add(weakest);
        }
        else
        {
            list.Add(nb);
            if (Urgent(kind) && conf >= 0.5f) _fresh.Add(list.Count - 1);
        }
        return true;
    }

    /// <summary>② 사고 화학: 냄새 · 증상으로 '여기 공기가 이상하다'를 처음 알았을 때만 원인 마디를 남긴다 ('…냄새를 맡았다' — 근원은 그 방의 불 · 달궈진 곳).
    /// 그 믿음의 원인을 이 마디로 바꿔 둔다 — 외치면 들은 사람에게도 이 사슬이 이어진다 (나쁜 공기는 본디 급한 믿음이 아니지만 독한 냄새 · 증상은 알린다).</summary>
    private void Noticed(Sim sim, Agent a, long t, int cell, float conf, BeliefSource src, string what)
    {
        if (!Learn(a, t, BeliefKind.BadAir, cell, -1, conf, src)) return;
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} {what}", t, cell, GasSource(cell), 0, a.Id);
        a.LastThought = what; a.LastThoughtTick = t;
        var list = a.Beliefs;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Kind == BeliefKind.BadAir && list[i].Subject < 0 && Dist(list[i].Cell, cell) <= 3)
            {
                var b = list[i]; b.Cause = cause; list[i] = b;
                if (!_fresh.Contains(i)) _fresh.Add(i);   // 독한 냄새는 외친다 ("가스 냄새야!") — 못 맡는 사람은 이 말로 안다
                break;
            }
    }

    /// <summary>달아나는 까닭의 원인: 곁(8칸)에 믿는 위험(불 · 연기 · 나쁜 공기 · 공기 빠짐 · 구멍 · 샘) 가운데 가장 확신하는 것 → 없으면 그 방의 불 · 달궈진 곳
    /// (닫고 간 문 · 공황 같은 뒤따르는 일이 이 사슬에 잇는다).</summary>
    private int DangerCause(Agent a, int cell)
    {
        float best = 0; int cause = 0;
        foreach (var b in a.Beliefs)
            if (b.Kind is BeliefKind.Fire or BeliefKind.Smoke or BeliefKind.BadAir or BeliefKind.Vacuum or BeliefKind.Breach or BeliefKind.Leak
                && b.Cause != 0 && b.Conf > best && Dist(b.Cell, cell) <= 8) { best = b.Conf; cause = b.Cause; }
        return cause != 0 ? cause : GasSource(cell);
    }

    private int[] _rfCause = Array.Empty<int>();
    private long[] _rfTick = Array.Empty<long>();
    /// <summary>그 칸 방의 불(없으면 달궈진 곳)의 원인 — 방마다 틱에 한 번만 센다 (연기를 보는 칸마다 방을 훑지 않게 · 셈한 값일 뿐 저장하지 않는다).</summary>
    private int RoomFireCause(int cell, long tick)
    {
        int room = RoomOf(cell);
        if (room < 0) return GasSource(cell);
        if (_rfTick.Length != L.Rooms.Count) { _rfCause = new int[L.Rooms.Count]; _rfTick = Enumerable.Repeat(-1L, L.Rooms.Count).ToArray(); }
        if (_rfTick[room] != tick) { _rfTick[room] = tick; _rfCause[room] = GasSource(cell); }
        return _rfCause[room];
    }

    /// <summary>독한 기체의 근원: 그 방에서 타는 곳 → 없으면 달궈진 곳의 원인.</summary>
    private int GasSource(int cell)
    {
        if (_chem == null) return 0;
        int room = RoomOf(cell);
        if (room < 0) return _chem.HeatCauseAt(cell);
        int heat = 0, leak = 0;
        foreach (int c in L.Rooms[room].Cells)
        {
            if (_chem.Burning[c] != 0 && _chem.FireCause[c] != 0) return _chem.FireCause[c];
            if (leak == 0) leak = _chem.GasCause[c];
            if (heat == 0) heat = _chem.HeatCauseAt(c);
        }
        return leak != 0 ? leak : heat;   // 불 > 샘(새어 나온 기체 — ② (마)) > 달궈진 곳
    }

    /// <summary>시안화수소의 쓴 아몬드 냄새를 맡는 사람인가 — 열에 여섯 (유전: 못 맡는 사람이 20~40% · 세계 시드와 사람 번호로 정해져 저장이 필요 없다).</summary>
    public static bool SmellsAlmond(ulong seed, int person) => Rng.Mix(Rng.Mix(seed ^ Rng.Key("아몬드")) ^ (ulong)person) % 10 < 6;

    /// <summary>이 사람이 그 종류를 그 칸 근처(r칸)에 믿나 — 가장 확신하는 것.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    public float Believes(Agent a, BeliefKind kind, int cell, int r = 3)
    {
        float best = 0;
        foreach (var b in a.Beliefs) if (b.Kind == kind && Dist(b.Cell, cell) <= r && b.Conf > best) best = b.Conf;
        return best;
    }

    private void Forget(Agent a, Predicate<Belief> match)
    {
        for (int i = 0; i < a.Beliefs.Count; i++)
            if (match(a.Beliefs[i])) { var b = a.Beliefs[i]; b.Conf = 0; a.Beliefs[i] = b; }
    }

    /// <summary>그 갈래 · 대상의 믿음을 지운다 — 람다 없이 (보는 칸마다 부르는 곳: 갇힌 변수 묶음을 칸마다 새로 잡지 않게).</summary>
    private static void Forget(Agent a, BeliefKind k, int subject)
    {
        for (int i = 0; i < a.Beliefs.Count; i++)
        {
            var b = a.Beliefs[i];
            if (b.Kind == k && b.Subject == subject) { b.Conf = 0; a.Beliefs[i] = b; }
        }
    }

    private static void Forget(Agent a, BeliefKind k1, BeliefKind k2, int subject)
    {
        for (int i = 0; i < a.Beliefs.Count; i++)
        {
            var b = a.Beliefs[i];
            if ((b.Kind == k1 || b.Kind == k2) && b.Subject == subject) { b.Conf = 0; a.Beliefs[i] = b; }
        }
    }

    // ───────────────────────── 감각 ─────────────────────────

    private void Sense(Sim sim, Agent a, double dt)
    {
        long t = sim.Tick;
        ref var c = ref a.C;
        _fresh.Clear();
        long lt = Prof != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (a.Id == 0) PrepareSenses();
        lt = Lap("감각:준비", lt);
        bool person = a.IsPerson;
        if (person)
        {
            ref var body = ref _life![c.Body];
            if (body.Alive == 0) return;
            if (body.Conscious == 0) { Decay(a, t, dt); return; }
        }
        else if (c.Up == 0) return;
        bool asleep = IsSleeping(a);
        int cell = c.Cell;

        // 귀 — 잠들어도 듣는다 · 바탕 소음이 가린 만큼 작게 (시끄러운 기계실에서는 문 너머 비명을 못 듣는다)
        if (_snd != null)
        {
            float loudest = 0, mask = 0, ambient = 0, rouse = -99;   // rouse: 문턱을 가장 크게 넘은 몫 (경보 · 비명은 문턱이 낮다)
            float deaf = person ? _life![c.Body].HearShift : 0;          // ⑦ 일시 청력 손실 — 그만큼 덜 들린다 (소리 단계 S3)
            Heard loudH = default; loudH.Actor = -1;
            bool any = false;
            foreach (var h in _snd.At(cell))
            {
                if (h.Event <= c.HeardUpTo) continue;
                if (!any) { ambient = _snd.Ambient(cell); mask = Math.Max(0, ambient - Sound.MaskFrom); any = true; }
                if (h.Actor == a.Id || person && h.Actor == Sound.BodyActor(c.Body)) { Hear(sim, a, h); continue; }   // 제 목소리 · 제 몸의 소리 (코골이 · 기침)
                var heard = h;
                heard.Db = h.Db - mask - deaf;
                if (heard.Db < HearFloor) { if (deaf > 0 && h.Db - mask >= HearFloor) Deafened++; else Masked++; continue; }
                Hear(sim, a, heard);
                if (person && !asleep) { Glance(a, h, heard.Db, sim.Tick); if (h.Kind == SoundKind.Music) MusicHeard(a, h, heard.Db); }   // 소리 난 쪽으로 고개를 돌린다 (S4) · 들려오는 음악 (S5)
                float felt = h.Db - deaf;   // 귀에 닿은 크기 (먹먹한 귀는 덜 느낀다)
                if (felt > loudest) loudest = felt;
                if (asleep && felt - WakeDb(a, h.Kind) > rouse) { rouse = felt - WakeDb(a, h.Kind); loudH = h; loudH.Db = felt; }
            }
            if (_shoutAfter is Belief say) { _shoutAfter = null; Shout(sim, a, say); }
            c.HeardUpTo = _snd.LastEvent;
            if (asleep) c.SleepNoise = Math.Max(c.SleepNoise, Math.Max(loudest, _snd.Ambient(cell)));   // ⑦ 잠의 질 — 이어지는 바탕 소음도 잠자리 소리다
            // 깨우는 소리: 귀에 잠의 깊이만큼의 문턱(얕은 잠 50 ~ 깊은 잠 65dB · 경보 · 비명은 8dB 낮게) 넘게 · 이어지는 바탕 소음보다 10dB 넘게 솟아야
            // (시끄러운 잠자리에서는 익숙해진 소음 위로 — NFPA 72 잠자리 경보는 바탕 소음 + 15dB · 소리 단계 S2: 예전엔 60dB 하나)
            if (asleep && rouse >= 0 && loudH.Db >= ambient + 10) WokenBy(sim, a, loudH);
        }

        if (person && asleep)
        {
            // 몸이 깨운다: 숨참(산소 분압 · 이산화탄소 — 일산화탄소는 아니다) · 추위 · 더위 · 귀 먹먹 · 코와 목을 찌르는 기체(암모니아 50ppm · 오존 1ppm · 염화수소 20ppm · 플루오린화수소 10ppm — 시안화수소는 아니다) · 아픔
            // 연기 냄새로는 잘 깨지 않는다 (그래서 경보가 있다)
            // 몹시 졸린 몸 (0.8 넘게)은 추위 · 더위 · 아픔에 깨지 않는다 — 지쳐 곯아떨어진 이는 졸림이 풀리면 깬다 (DF10 검토)
            double p = F.Pressure(cell);
            var why = RouseWhy(a, cell);
            if (why != SnagKind.None && !(c.Sleepy >= DozeSleepy && Bearable(why)) || Math.Abs(p - c.LastKPa) > 3)
                Wake(sim, a, "몸이 깨웠다");
            c.LastKPa = (float)p;
            Decay(a, t, dt);
            return;
        }

        lt = Lap("감각:귀", lt);
        // 눈
        int room = RoomOf(cell);
        double radius = SightRadius(a, cell);
        if (c.Kind == AgentKind.Robot && c.Goal == GoalKind.Patrol && c.StepIndex >= 1 && c.GoalTarget >= 0 && c.GoalTarget < L.Rooms.Count) LookedAt(a, c.GoalTarget, t);   // DF5 순회: 그 방을 둘러본다 (둘러볼 자리에 닿은 뒤)
        if (room >= 0)
        {
            if (a.IsPerson && c.Up != 0) Dodge(sim, a, room, radius);   // 날아드는 것을 비킨다 (PH13 — 본 것만)
            SeeRoom(sim, a, room, radius);
            foreach (int x in _roomDoors[room]) SeeCell(sim, a, x, radius);
            for (int k = _holeStart[room], end = _holeStart[room + 1]; k < end; k++)
                if (_holeList[k] is int w && Dist(w, cell) <= radius + 1)
                    Learn(a, t, BeliefKind.Breach, w, -1, 1, BeliefSource.Saw, _st?.DamageCause[w] ?? 0, F.Hole[w]);
            // 휘어 끼익거리는 문 · 벽 (부하가 정격의 60% 넘음) — 가서 보면 안다
            if (_st != null)
            {
                for (int k = _strainStart[room], end = _strainStart[room + 1]; k < end; k++)
                    if (_strainList[k] is int w && Dist(w, cell) <= 3)
                        Learn(a, t, BeliefKind.Strain, w, -1, Math.Min(1f, _st.Stress[w]), BeliefSource.Saw, _st.DamageCause[w], _st.Stress[w]);
                foreach (int x in _roomDoors[room])
                {
                    if (_st.Stress[x] >= 0.6f && _st.Braced[x] == 0 && Dist(x, cell) <= 3)
                        Learn(a, t, BeliefKind.Strain, x, -1, Math.Min(1f, _st.Stress[x]), BeliefSource.Saw, _st.DamageCause[x], _st.Stress[x]);
                    else if (_st.JammedAt(x) && Dist(x, cell) <= 4) // 틀어져 반쯤 열린 채 끼인 문
                        Learn(a, t, BeliefKind.Strain, x, -1, 1f, BeliefSource.Saw, _st.DamageCause[x], 1f);
                }
            }
            // 닫힌 문이 바람을 빨아들인다 (서리 · 틈새 바람 · 쉬익) — 그 너머가 빠졌다: 문틈도 막을 구멍이다
            foreach (int x in _roomDoors[room])
            {
                int d = L.DoorAt[x];
                if (d < 0 || !F.DoorGapOpen(d) || Dist(x, cell) > 3) continue;
                double dp = DoorDeltaKPa(x);
                if (dp > 20 && !ClosedAirlockBeyond(x)) Learn(a, t, BeliefKind.Breach, x, -1, (float)Math.Min(1, dp / 60), BeliefSource.Saw, _st?.LowSideCause(x) ?? 0, (float)Field.LeakNormalDoor);
            }
            if (L.Kind[cell] == CellKind.Door)
            {
                Span<int> nb = stackalloc int[4];
                L.Neighbors(cell, nb);
                foreach (int j in nb)
                    if (j >= 0 && L.Room[j] >= 0 && L.Room[j] != room)
                        SeeRoom(sim, a, L.Room[j], radius);
            }
        }
        else if (L.Kind[cell] == CellKind.Outside)
        {
            // 외판 바깥: 둘레 3칸 안의 외판 구멍이 보인다
            int x0 = L.X(cell), y0 = L.Y(cell);
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (!L.In(x0 + dx, y0 + dy)) continue;
                    int w = L.Index(x0 + dx, y0 + dy);
                    if (L.Kind[w] == CellKind.Wall && L.Hull[w] && F.Hole[w] > 0)
                        Learn(a, t, BeliefKind.Breach, w, -1, 1, BeliefSource.Saw, _st?.DamageCause[w] ?? 0, F.Hole[w]);
                }
        }
        lt = Lap("감각:눈", lt);
        // 공기가 빠졌다 (사람은 몸으로 · 로봇은 센서로)
        double kpa = F.Pressure(cell);
        if (kpa < 50) Learn(a, t, BeliefKind.Vacuum, cell, -1, kpa < 20 ? 1f : 0.7f, person ? BeliefSource.Felt : BeliefSource.Saw);

        if (person)
        {
            ref var body = ref _life![c.Body];
            // 코
            double soot = F.Ppm(T.gSoot, cell);
            if (soot > 30) Learn(a, t, BeliefKind.SmokeSmell, cell, -1, (float)Math.Min(1, soot / 300), BeliefSource.Smelled);
            // 암모니아: 5ppm부터 지린 냄새 (냄새 문턱) — 옅으면 옅게 믿고 25ppm(눈 · 코가 따가운 짙기)이면 0.8 · 처음 알면 원인 마디 (새는 냉매 · ② (마))
            double nh3 = F.Ppm(T.gNH3, cell);
            if (nh3 >= 5) Noticed(sim, a, t, cell, (float)Math.Clamp(nh3 / 30, 0.2, 1), BeliefSource.Smelled, "지린 암모니아 냄새를 맡았다");
            if (F.Ppm(T.gO3, cell) > 0.1) Learn(a, t, BeliefKind.BadAir, cell, -1, 0.8f, BeliefSource.Smelled);
            // ② 사고 화학: 염화수소는 1ppm부터 누구나 맡는다 (코를 찌르는 매캐한 냄새) · 시안화수소는 쓴 아몬드 냄새 — 맡는 사람만 (유전 · 열에 넷은 못 맡는다)
            double hcl = F.Ppm(T.gHCl, cell), hcn = F.Ppm(T.gHCN, cell);
            if (hcl >= 1) Noticed(sim, a, t, cell, (float)Math.Min(1, 0.5 + hcl / 40), BeliefSource.Smelled, "코를 찌르는 매캐한 냄새를 맡았다");
            if (hcn >= 1 && SmellsAlmond(sim.Seed, a.Id)) Noticed(sim, a, t, cell, (float)Math.Min(1, 0.5 + hcn / 20), BeliefSource.Smelled, "쓴 아몬드 냄새를 맡았다");
            // ② (라) 배터리가 뿜은 플루오린 화합물: 코와 눈을 찌른다 (누구나 · 1ppm부터)
            double hf = F.Ppm(T.gHF, cell) + 2 * F.Ppm(T.gPOF3, cell) + 3 * F.Ppm(T.gPF5, cell);
            if (hf >= 1) Noticed(sim, a, t, cell, (float)Math.Min(1, 0.5 + hf / 20), BeliefSource.Smelled, "코와 눈을 찌르는 냄새를 맡았다");
            // 몸
            if (F.Partial(T.gO2, cell) < 16 || F.Ppm(T.gCO2, cell) > 20000) Learn(a, t, BeliefKind.BadAir, cell, -1, 0.7f, BeliefSource.Felt);
            // 두통 — 냄새는 없다. 머리가 점점 아파질 때(일산화탄소 헤모글로빈이 오를 때)만 '여기 공기가 이상하다'
            if (body.COHb > 0.08f && body.COHb > c.LastCOHb + 1e-4f) Learn(a, t, BeliefKind.BadAir, cell, -1, 0.5f, BeliefSource.Felt);
            c.LastCOHb = body.COHb;
            // 증상: 염화수소 5ppm 넘게면 눈이 따갑고 기침 · 시안화수소가 몸에 쌓이면 어지럽고 머리가 아프다 (냄새를 못 맡는 사람도 이걸로 안다 — 늦게)
            if (hcl > 5 || hf > 2) Noticed(sim, a, t, cell, 0.8f, BeliefSource.Felt, "눈이 따갑고 기침이 난다");
            if (body.CN > 0.1f && hcn > 10) Noticed(sim, a, t, cell, 0.7f, BeliefSource.Felt, "어지럽고 머리가 아프다");
            double temp = F.Temp(cell);
            if (temp < 285) Learn(a, t, BeliefKind.Cold, cell, -1, 0.6f, BeliefSource.Felt);
            else if (temp > 312) Learn(a, t, BeliefKind.Hot, cell, -1, 0.6f, BeliefSource.Felt);
            if (Math.Abs(kpa - c.LastKPa) > 3) Learn(a, t, BeliefKind.Leak, cell, -1, 0.4f, BeliefSource.Felt); // 귀 먹먹 — 그것만으로는 어딘지 모른다
            double wx = F.WindX[cell], wy = F.WindY[cell], ws = Math.Sqrt(wx * wx + wy * wy);
            if (ws > 1)
            {
                // 바람은 새는 곳으로 분다 → 바람 부는 쪽 몇 칸 앞을 짐작
                int gx = Math.Clamp(L.X(cell) + (int)Math.Round(wx / ws * 5), 0, L.W - 1), gy = Math.Clamp(L.Y(cell) + (int)Math.Round(wy / ws * 5), 0, L.H - 1);
                Learn(a, t, BeliefKind.Leak, L.Index(gx, gy), -1, (float)Math.Min(0.8, 0.3 + ws / 10), BeliefSource.Felt);
            }
            if (body.Pain > 0.5f) c.Fear = Math.Max(c.Fear, 0.4f);
        }
        c.LastKPa = (float)kpa;

        // 새로 알게 된 급한 일: 사실로 남기고 외친다
        if (_fresh.Count > 0)
        {
            int best = -1; float bc = 0;
            foreach (int i in _fresh)
            {
                if (i >= a.Beliefs.Count) continue;
                var b = a.Beliefs[i];
                // 죽음은 처음 안 때만 소식이다 (본 · 들은 · 방송) — 이미 아는 시신은 다시 '발견'하지 않는다 · 슬픔은 행동 '알게 됐다'로 (DF2)
                bool news = b.Kind != BeliefKind.Dead || b.Subject < 0 || KnowDeath(sim, a, b.Subject, b.Src, b.Cause);
                if (news && b.Src is BeliefSource.Saw or BeliefSource.Felt or BeliefSource.Smelled)
                {
                    sim.Facts.Emit(t, FactKind.Noticed, b.Cell, b.Conf, b.Cause, a.Id, (int)b.Kind);
                    if (b.Kind is BeliefKind.Fire or BeliefKind.Vacuum or BeliefKind.Breach) c.Fear = Math.Max(c.Fear, person ? 0.5f + 0.4f * (1 - c.Brave) : 0);
                    if (b.Kind == BeliefKind.Dead && person) c.Fear = Math.Max(c.Fear, 0.6f);
                }
                if (person && c.Up != 0) HeartOnFresh(sim, a, b);   // ⑧ 곁에서 본 죽음 · 그날이 되살아났다
                if (news && b.Conf > bc) { bc = b.Conf; best = i; }
            }
            if (best >= 0 && (person ? c.Up != 0 : true) && t - c.LastShout >= 10 && a.Beliefs[best].Src != BeliefSource.Told)
                Shout(sim, a, a.Beliefs[best]);
        }
        lt = Lap("감각:몸 · 알림", lt);
        Disconfirm(a, t, room, radius);
        lt = Lap("감각:지우기", lt);
        Decay(a, t, dt);
        Lap("감각:바래기", lt);
    }

    /// <summary>매 틱 한 번: 불꽃(합선) 칸 · 장치 칸 표.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void PrepareSenses()
    {
        _sparks.Clear();
        if (_net != null)
        {
            foreach (int fc in _net.FaultCell) if (fc >= 0) _sparks.Add(fc);
            if (_devAt.Length != L.N)
            {
                _devAt = Enumerable.Repeat(-1, L.N).ToArray();
                foreach (var d in _net.Devices) foreach (int x in L.Furniture[d.Furniture].Cells) _devAt[x] = d.Id;
            }
        }
        // ① 눈여겨볼 칸: 방마다 (방 칸 차례 그대로) 불 · 짙은 연기 · 고인 물 · 불꽃 · 장치 · 몸이 있는 칸만 — 그 밖의 칸은 봐도 아무 일이 없다 (SeeCell이 그냥 돌아간다)
        // 틱마다 칸을 한 번만 훑고 사람마다는 추린 칸만 본다 — 결과는 같다 (지문 같음 · 새뜰호 감각 0.30 → ms)
        int R = L.Rooms.Count;
        if (_seeStart.Length != R + 1) _seeStart = new int[R + 1];
        _seeList.Clear();
        bool devs = _net != null && _devAt.Length == L.N, bodies = _bodyHead.Length == L.N;
        for (int r = 0; r < R; r++)
        {
            _seeStart[r] = _seeList.Count;
            foreach (int x in L.Rooms[r].Cells)
                if (devs && _devAt[x] >= 0 || bodies && _bodyHead[x] >= 0 || F.Stored(T.sWater, x) > 100 || _chem != null && _chem.Burning[x] != 0
                    || _light != null && (F.Gas(T.gSoot, x) > 0 || !L.IsSpace(x)) && _light.Visibility(x) < 8 || _sparks.Count > 0 && _sparks.Contains(x))   // 그을음이 없으면 시야는 30m — 셈을 건너뛴다
                    _seeList.Add(x);
        }
        _seeStart[R] = _seeList.Count;
        // 방 벽 가운데 구멍 난 벽 · 휘는 벽(정격 60% 넘게 · 덧대지 않음 · 구멍 없음)만 — 벽 차례 그대로
        if (_holeStart.Length != R + 1) { _holeStart = new int[R + 1]; _strainStart = new int[R + 1]; }
        _holeList.Clear(); _strainList.Clear();
        for (int r = 0; r < R; r++)
        {
            _holeStart[r] = _holeList.Count; _strainStart[r] = _strainList.Count;
            if (r >= _roomWalls.Length) continue;
            foreach (int w in _roomWalls[r])
            {
                if (F.Hole[w] > 0) _holeList.Add(w);
                else if (_st != null && _st.Stress[w] >= 0.6f && _st.Braced[w] == 0) _strainList.Add(w);
            }
        }
        _holeStart[R] = _holeList.Count; _strainStart[R] = _strainList.Count;
    }
    private int[] _holeStart = Array.Empty<int>(), _strainStart = Array.Empty<int>();
    private readonly List<int> _holeList = new(), _strainList = new();
    private int[] _seeStart = Array.Empty<int>();
    private readonly List<int> _seeList = new();

    /// <summary>방 하나에서 눈여겨볼 칸들을 본다 (PrepareSenses가 추린 것).</summary>
    private void SeeRoom(Sim sim, Agent a, int room, double radius)
    {
        for (int k = _seeStart[room], end = _seeStart[room + 1]; k < end; k++) SeeCell(sim, a, _seeList[k], radius);
    }

    /// <summary>보이는 거리: 사람은 그을음 시야(많아야 30m) · 어두우면 1.5m · 로봇은 12m(열 감지).</summary>
    private double SightRadius(Agent a, int cell)
    {
        if (!a.IsPerson) return 12 * MechSight(a);   // ⑥ 흐려진 센서는 가까이만 본다
        double vis = _light?.Visibility(cell) ?? 30;
        double lux = _light != null ? _light.Lux[cell] : 300;
        return lux < 2 ? Math.Min(vis, 1.5) : vis;
    }

    private void SeeCell(Sim sim, Agent a, int x, double radius)
    {
        long t = sim.Tick;
        int d = Dist(a.C.Cell, x);
        bool burning = _chem != null && _chem.Burning[x] != 0;
        if (d > radius && !(burning && d <= (_light?.Visibility(x) ?? 30))) return;
        if (burning) Learn(a, t, BeliefKind.Fire, x, -1, 1, BeliefSource.Saw, _chem!.FireCause[x]);
        if (_light != null && _light.Visibility(x) < 8) Learn(a, t, BeliefKind.Smoke, x, -1, 0.9f, BeliefSource.Saw, RoomFireCause(x, t));   // 연기의 근원 — 그 방의 불
        if (F.Stored(T.sWater, x) > 100) Learn(a, t, BeliefKind.Water, x, -1, 0.9f, BeliefSource.Saw);
        if (F.Hole[x] > 0 && L.Kind[x] == CellKind.Floor) Learn(a, t, BeliefKind.Breach, x, -1, 1, BeliefSource.Saw, _st?.DamageCause[x] ?? 0, F.Hole[x]); // ④ 바닥 · 천장 판이 뚫린 칸 (폭발 압력)
        if (_sparks.Contains(x)) Learn(a, t, BeliefKind.Spark, x, -1, 1, BeliefSource.Saw);
        if (_devAt.Length == L.N && _devAt[x] >= 0 && _net != null && _net.Devices[_devAt[x]].Cell == x && IsStove(_devAt[x])) SeeStove(sim, a, t, x, _devAt[x]);   // DF5 켜 둔 조리대 · 오븐 (방 안이면 보인다)
        if (d <= 3 && _devAt.Length == L.N && _devAt[x] >= 0 && _net != null)
        {
            int dev = _devAt[x];
            bool broken = false;
            for (int p = 0; p < 4; p++) if (_net.Broken[dev * 4 + p] != 0) { broken = true; break; }
            if (broken) Learn(a, t, BeliefKind.Broken, x, dev, 1, BeliefSource.Saw);
            else Forget(a, BeliefKind.Broken, dev);
        }
        for (int bi = _bodyHead.Length == L.N ? _bodyHead[x] : -1; bi >= 0; bi = _bodyNext[bi])
        {
            if (a.IsPerson && bi == a.C.Body) continue;
            var body = _life!.Bodies[bi];
            if (body.Alive == 0) { Learn(a, t, BeliefKind.Dead, x, bi, 1, BeliefSource.Saw, body.DeathCause); Forget(a, BeliefKind.Injured, bi); }
            else if (body.Conscious == 0) Learn(a, t, BeliefKind.Injured, x, bi, 1, BeliefSource.Saw, body.HurtCause);
            else if (_soc != null && AgentOfBody(bi) is int who && who >= 0 && who != a.Id && _soc.InBrawl(who, out _, out bool att) && !att)
                Learn(a, t, BeliefKind.Help, x, who, 1, BeliefSource.Saw); // ⑨ 맞고 있다 — 깨어 있어도 '도와 달라'
            else Forget(a, BeliefKind.Injured, BeliefKind.Help, bi);
        }
    }

    /// <summary>보고 있는 칸에 있다고 믿던 것이 없으면 지운다 ("꺼졌구나" · "옮겨졌구나").</summary>
    private void Disconfirm(Agent a, long t, int room, double radius)
    {
        if (room < 0) return;
        for (int i = 0; i < a.Beliefs.Count; i++)
        {
            var b = a.Beliefs[i];
            if (b.Seen == t || b.Conf <= 0) continue;
            if (b.Kind is not (BeliefKind.Fire or BeliefKind.Smoke or BeliefKind.Injured or BeliefKind.Dead or BeliefKind.Water or BeliefKind.Spark
                or BeliefKind.Bang or BeliefKind.Help or BeliefKind.Breach or BeliefKind.Vacuum)) continue;
            if (b.Kind == BeliefKind.Water && b.Src != BeliefSource.Saw) continue;   // 고인 물: 본 것만 봐서 지운다 — 100몰 아래 물기는 보이지 않으니 들은 것 · 느낀 것은 반감기로 잊는다 (5단 C13)
            if (!Visible(a, b.Cell, room, radius)) continue;
            b.Conf = 0;
            a.Beliefs[i] = b;
            a.Doubts.RemoveAll(d => d.Until <= t);
            if (a.Doubts.Count < 8) a.Doubts.Add(new Doubt { Kind = b.Kind, Cell = b.Cell, Until = t + Time.Minutes(3) });
        }
    }

    /// <summary>비킬 만큼 빠른 물체 (m/s) · 비킬 만큼 가까이 지나는 거리 (m — 원판 0.25 + 물체).</summary>
    public const double DodgeV = 2, DodgeMiss = 0.7, DodgeFront = 0.5;
    public long Dodges;

    /// <summary>
    /// 같은 방에서 바라보는 쪽 앞에 보이는 (밝고 연기가 없는 — 보는 거리 안) 물체가 2m/s 넘게 다가와 한 틱 안에 곁 0.7m로 지나가면 옆 칸으로 비킨다 (가는 쪽에 수직 · 트인 칸 · 그 방).
    /// 어둡거나 연기가 차면 보지 못해 비키지 못한다. 길은 다시 찾는다.
    /// </summary>
    private void Dodge(Sim sim, Agent a, int room, double radius)
    {
        if (_st == null || _st.Props.Count == 0) return;
        ref var c = ref a.C;
        double ax = L.X(c.Cell) + 0.5, ay = L.Y(c.Cell) + 0.5, dt = Time.SecondsPerTick;
        foreach (var p in _st.Props)
        {
            if (!Structure.Awake(p)) continue;
            double v2 = p.Vx * p.Vx + p.Vy * p.Vy;
            if (v2 < DodgeV * DodgeV) continue;
            int pc = _st.CellOf(p);
            if (pc < 0 || L.Room[pc] != room || !Visible(a, pc, room, radius)) continue;
            double rx = ax - p.X, ry = ay - p.Y, along = (rx * p.Vx + ry * p.Vy) / v2;   // 가장 가까이 오는 때 (초)
            double fx = c.FaceX, fy = c.FaceX == 0 && c.FaceY == 0 ? 1 : c.FaceY, fl = Math.Sqrt(fx * fx + fy * fy), rl = Math.Sqrt(rx * rx + ry * ry);
            if (rl > 0 && -(rx * fx + ry * fy) / (rl * fl) < DodgeFront) continue;   // 바라보는 쪽 앞 (±60°)에서 오는 것만 본다 — 등 뒤 · 옆에서 오는 것은 못 비킨다
            if (along <= 0 || along > dt) continue;
            double mx = rx - p.Vx * along, my = ry - p.Vy * along;
            if (mx * mx + my * my > DodgeMiss * DodgeMiss) continue;
            // 옆으로: 가는 쪽에 수직인 두 칸 가운데 트인 칸 (지나는 줄에서 먼 쪽 먼저)
            double v = Math.Sqrt(v2), nx = -p.Vy / v, ny = p.Vx / v;
            if (mx * nx + my * ny < 0) { nx = -nx; ny = -ny; }
            foreach (var (sx, sy) in new[] { (nx, ny), (-nx, -ny) })
            {
                int dx = Math.Abs(sx) >= Math.Abs(sy) ? Math.Sign(sx) : 0, dy = dx == 0 ? Math.Sign(sy) : 0;
                int x = L.X(c.Cell) + dx, y = L.Y(c.Cell) + dy;
                if (!L.In(x, y)) continue;
                int n = L.Index(x, y);
                if (L.Room[n] != room || !F.Passable(n) || L.FurnitureAt[n] >= 0) continue;
                Move(a, n);
                if ((uint)a.Id < (uint)_walkN.Length) { _walk[WalkMax * a.Id] = n; _walkN[a.Id] = 1; }   // 옆걸음은 빠르다 — 이 틱 걸음 길은 새 칸에서 (④가 원판을 그 자리에 둔다)
                a.Path.Clear(); a.PathIdx = 0;
                Dodges++;
                Jot(a, new Judgment { Tick = sim.Tick, Goal = c.Goal, Target = p.Id, Cell = n, Method = 255, Out = JudgeOut.Done, Why = SnagKind.None, By = -1, Note = "날아드는 것을 보고 비켰다" });
                return;
            }
        }
    }

    private bool Visible(Agent a, int cell, int room, double radius)
    {
        if (Dist(a.C.Cell, cell) > radius) return false;
        if (L.Kind[cell] == CellKind.Wall) return Array.BinarySearch(_roomWalls[room], cell) >= 0;
        if (L.Kind[cell] == CellKind.Door) return Array.BinarySearch(_roomDoors[room], cell) >= 0;
        return L.Room[cell] == room;
    }

    private void Decay(Agent a, long t, double dt)
    {
        var list = a.Beliefs;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var b = list[i];
            if (b.Seen != t) b.Conf *= (float)Math.Exp(-dt * 0.6931 / _halfLife[(int)b.Kind]);
            if (b.Conf < 0.05f) { list.RemoveAt(i); continue; }
            list[i] = b;
        }
    }

    /// <summary>들은 소리를 훑은 뒤에 할 외침 (알림 — 훑는 동안 외치면 그 칸의 들린 소리 목록이 바뀐다).</summary>
    private Belief? _shoutAfter;

    /// <summary>가린 몫을 뺀 크기가 이보다 작으면 못 듣는다 (dB — 귀의 문턱 근처).</summary>
    public const float HearFloor = 23;
    /// <summary>먹먹한 귀(일시 청력 손실) 때문에 못 들은 소리 수 (확인용 — 찍지 않는다).</summary>
    public long Deafened;

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void Hear(Sim sim, Agent a, in Heard h)
    {
        long t = sim.Tick;
        switch (h.Kind)
        {
            case SoundKind.Machine when _devAt.Length == L.N && (uint)h.Source < (uint)L.N && _devAt[h.Source] >= 0 && _net != null && _net.Devices[_devAt[h.Source]].Has(Part.Mechanical):
            {
                // 끽끽 · 덜컹: 그 장치의 소리가 이상하다 (멈춘 덜컹이면 가서 보면 '멈춘 장치')
                int dev = _devAt[h.Source];
                if (Learn(a, t, BeliefKind.Grind, h.Source, dev, (float)Math.Min(1, (h.Db - 25) / 30), BeliefSource.Heard, h.Cause, h.Db) && a.IsPerson)
                {
                    GrindHeard++;
                    sim.Facts.Emit(t, FactKind.Noticed, h.Source, (float)h.Db, h.Cause, a.Id, (int)BeliefKind.Grind);
                    // 갈 솜씨가 없으면 알린다: 소리 내어 (곁의 사람 · 로봇이 듣는다) · 배 안 통신으로 (일감판이 로봇을 보낸다) — 한 장치는 두 시간에 한 번
                    if (a.C.SkRepair < GrindSkill && !IsSleeping(a) && a.C.Up != 0 && !GrindReports.Any(r => r.Device == dev && t - r.Tick < Time.Hours(2)))
                    {
                        int rc = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} {_net.Devices[dev].Name} 소리가 이상하다고 알렸다", t, h.Source, h.Cause, 0, a.Id);
                        if (GrindReports.Count >= 16) GrindReports.RemoveAt(0);
                        GrindReports.Add(new GrindReport { Tick = t, Device = dev, Cause = rc, Who = a.Id });
                        int bi = a.Beliefs.FindIndex(b => b.Kind == BeliefKind.Grind && b.Subject == dev);
                        if (bi >= 0 && t - a.C.LastShout >= 10) { var said = a.Beliefs[bi]; said.Cause = rc; _shoutAfter = said; } // 들은 소리를 다 훑은 뒤에 (외침이 이 칸의 들린 소리 목록을 바꾼다)
                    }
                }
                break;
            }
            // 물체 소리 (덜그럭 · 쨍그랑 · 드르륵 · 쿵 — ④ PH8)는 생활 소리다: 믿음을 세우지 않는다 (3.2kJ 넘는 닿음만 '쾅' — 쨍그랑에 가 보는 판단은 PH13)
            case SoundKind.Bang when h.Db >= 45:
                Learn(a, t, BeliefKind.Bang, h.Source, -1, (float)Math.Min(1, (h.Db - 40) / 50), BeliefSource.Heard, h.Cause, h.Db); break;
            case SoundKind.Crack or SoundKind.Creak:
                Learn(a, t, BeliefKind.Strain, h.Source, -1, (float)Math.Min(1, (h.Db - 30) / 40), BeliefSource.Heard, h.Cause, h.Db); break;
            case SoundKind.Hiss:
                Learn(a, t, BeliefKind.Leak, h.Source, -1, (float)Math.Min(1, (h.Db - 30) / 40), BeliefSource.Heard, h.Cause, h.Db); break;
            case SoundKind.Roar:
                Learn(a, t, BeliefKind.Fire, h.Source, -1, (float)Math.Clamp((h.Db - 30) / 50, 0.15, 0.6), BeliefSource.Heard, h.Cause); break; // 우르릉 — 어디서 타는지는 안다 · 얼마나 큰지는 모른다
            case SoundKind.Scream when h.Actor != a.Id:
                Learn(a, t, BeliefKind.Help, h.Source, h.Actor, 0.8f, BeliefSource.Heard, h.Cause); break;
            case SoundKind.Alarm:
            case SoundKind.Voice when h.Actor != a.Id:
            {
                int si = SaidIndex(h.Event);   // 그 소리로 한 말 (사건마다 많아야 하나)
                if (si < 0) break;
                var (_, _, sp, b) = _said[si];
                if (sp != a.Id)
                {
                    float clarity = (float)Math.Clamp((h.Db - 25) / 30, 0.2, 1);
                    float heed = sp != Computer && a.IsPerson && _soc != null ? _soc.Heed(a.Id, sp) : 1; // 말한 이를 믿는 만큼 · 나를 이끄는 이의 말은 더 (⑨)
                    Learn(a, t, b.Kind, b.Cell, b.Subject, Math.Min(1, b.Conf * 0.8f * clarity * heed), sp == Computer ? BeliefSource.Screen : BeliefSource.Told, b.Cause, b.Size);
                    if (a.IsPerson && Urgent(b.Kind)) a.C.Fear = Math.Max(a.C.Fear, 0.3f * (1 - a.C.Brave));
                }
                break;
            }
        }
    }

    /// <summary>
    /// 소리 사건 ev로 한 말의 자리 (없으면 −1): 한 말은 소리를 낸 차례로 쌓여 사건 번호가 오르는 차례다 (소리는 사건마다 새 번호 · 외침 · 방송은 낸 그 사건으로 하나씩) — 반씩 나눠 찾는다
    /// (위기 틱에 마흔 명이 외치면 듣는 이마다 들은 말마다 한 말 목록을 다 훑었다 · 찾는 것은 같다).
    /// </summary>
    private int SaidIndex(int ev)
    {
        var said = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_said);
        int lo = 0, hi = said.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1, e = said[mid].ev;
            if (e == ev) return mid;
            if (e < ev) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>외친다: 75dB 말 (로봇은 80dB 안내) — 들은 사람은 같은 믿음을 '말'로 얻는다.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void Shout(Sim sim, Agent a, Belief b)
    {
        if (_snd == null) return;
        int ev = _snd.Emit(sim, a.C.Cell, VoiceDb(a.C.Cell, a.IsPerson), SoundKind.Voice, b.Cause, a.Id);   // 시끄러울수록 크게 (롬바르)
        _said.Add((ev, sim.Tick, a.Id, b));
        a.C.LastShout = sim.Tick;
        Shouts++;
        sim.Facts.Emit(sim.Tick, FactKind.Warned, b.Cell, b.Conf, b.Cause, a.Id, (int)b.Kind);
    }

    /// <summary>몸이 깨울 것 (잠든 이를 깨우고 · 누운 이를 잠들지 못하게 한다)과 그 까닭 — 없으면 None: 숨참 (산소 분압 · 이산화탄소 — 일산화탄소는 아니다) ·
    /// 코와 목을 찌르는 기체 (암모니아 50ppm · 오존 1ppm · 염화수소 20ppm · 플루오린화수소 10ppm — 시안화수소는 아니다) · 추위 (10℃) · 더위 (42℃) ·
    /// 아픔 (몸이 느끼는 — 약이 던 만큼 덜 · 진통제가 필요한 만큼: <see cref="Life.NeedsPainkiller"/>와 같은 문턱 — DF10 검토: 예전엔 약을 맞아도 잠들지 못했다).
    /// 기압이 갑자기 바뀌는 것 (귀 먹먹)은 깨울 때만.</summary>
    private SnagKind RouseWhy(Agent a, int cell)
    {
        if (F.Partial(T.gO2, cell) < 15 || F.Ppm(T.gCO2, cell) > 30000) return SnagKind.SleepAir;
        if (F.Ppm(T.gNH3, cell) > 50 || F.Ppm(T.gO3, cell) > 1 || F.Ppm(T.gHCl, cell) > 20 || F.Ppm(T.gHF, cell) + 2 * F.Ppm(T.gPOF3, cell) + 3 * F.Ppm(T.gPF5, cell) > 10) return SnagKind.SleepSting;
        double k = F.Temp(cell);
        if (k < 283) return SnagKind.SleepCold;
        if (k > 315) return SnagKind.SleepHot;
        return Life.FeltPain(_life![a.C.Body]) > 0.6 ? SnagKind.SleepPain : SnagKind.None;
    }

    /// <summary>졸림이 이만큼 넘게 지친 몸은 추위 · 더위 · 아픔을 이기고 곯아떨어진다 · 잠든 몸은 졸림이 <see cref="DozeSleepy"/> 아래로 풀릴 때까지 그것들에 깨지 않는다
    /// (얕은 잠 — 잠의 질이 그만큼 낮다 · 숨참 · 찌르는 기체는 이기지 못한다 · 평시 잠드는 졸림은 0.6 안팎 · 가장 높아야 0.75).
    /// 실제: 잠을 오래 못 잔 몸은 깨는 문턱이 높아져 불편한 자리에서도 잠깐씩 든다 (잠 빚 · 미세 수면).</summary>
    public const float ExhaustSleepy = 0.95f, DozeSleepy = 0.8f;

    /// <summary>지친 몸이 이기고 잠드는 까닭인가 (추위 · 더위 · 아픔).</summary>
    private static bool Bearable(SnagKind why) => why is SnagKind.SleepCold or SnagKind.SleepHot or SnagKind.SleepPain;

    private void Wake(Sim sim, Agent a, string why, int cause = 0, int by = -1)
    {
        ref var body = ref _life![a.C.Body];
        if (body.Asleep == 0) return;
        body.Asleep = 0;
        a.C.WokeAt = sim.Tick;
        a.C.Goal = GoalKind.Idle; a.Plan.Clear(); a.Path.Clear(); a.C.StepIndex = 0;
        sim.Facts.Emit(sim.Tick, FactKind.Woke, a.C.Cell, 0, cause, a.Id, by);
    }

    /// <summary>소리에 깼다 — 남의 말소리면 (위급해서 외친 말이 아니면) 누구 소리였는지 원인에 남긴다 (⑨가 듣는다 — 잠을 설치게 한 이를 조금 원망한다).
    /// 실제: 화성-500 · 남극 월동대의 갈등은 큰일보다 소음 · 잠 · 일 분담 같은 작은 마찰이 쌓여 생겼다.</summary>
    private void WokenBy(Sim sim, Agent a, in Heard h)
    {
        int by = h.Actor >= 0 ? h.Actor : AgentOfBody(Sound.BodyOf(h.Actor)), ev = h.Event;
        bool annoying = h.Kind is SoundKind.Voice or SoundKind.Laugh or SoundKind.Snore or SoundKind.Music && by >= 0 && by < Agents.Count && by != a.Id && Agents[by].IsPerson && !_said.Exists(x => x.ev == ev);   // 말소리 · 웃음 · 코 고는 소리 (소리 단계 S2)
        if (!annoying || _life![a.C.Body].Asleep == 0) { Wake(sim, a, "소리에 깼다"); return; }
        string what = h.Kind switch { SoundKind.Laugh => "웃음소리", SoundKind.Snore => "코 고는 소리", SoundKind.Music => "음악 소리", _ => "말소리" };
        int cause = sim.Causes.Add(CauseKind.Person, $"{NameOf(by)}의 {what}에 {Ko.I(NameOf(a.Id))} 깼다", sim.Tick, a.C.Cell, h.Cause, 0, by);
        Wake(sim, a, "소리에 깼다", cause, by);
    }

    // ───────────────────────── 본능 (사람) ─────────────────────────

    private bool BurningNear(int cell, int r)
    {
        if (_chem == null) return false;
        int x0 = L.X(cell), y0 = L.Y(cell);
        for (int y = Math.Max(0, y0 - r); y <= Math.Min(L.H - 1, y0 + r); y++)
            for (int x = Math.Max(0, x0 - r); x <= Math.Min(L.W - 1, x0 + r); x++)
                if (_chem.Burning[L.Index(x, y)] != 0) return true;
        return false;
    }

    private bool AirBad(Agent a, int cell)
    {
        if (a.IsPerson && _life![a.C.Body].Suit != 0) return false;
        if (a.IsPerson && _life![a.C.Body].Mask != 0 && F.Pressure(cell) >= 25) return false;   // 마스크로 숨 쉰다
        // 몸은 산소가 모자란 것을 바로 느끼지 못한다 (숨이 찬 것은 이산화탄소 때문) — 낮은 기압 · 이산화탄소가 본능을 깨우고 ·
        // 피의 산소가 0.85 아래로 떨어지면 어지럽고 숨이 가빠 알아챈다 (저산소증 증상 — 판단은 흐려져도 몸은 안다 · DF10 포화 곡선을 실측대로 고쳐 0.8 → 0.85: 알아채는 공기는 그대로 산소 12kPa 언저리)
        // 일산화탄소에 빼앗긴 몫은 느끼지 못한다 (숨을 재촉하는 것은 피의 산소 분압이다 — 그래서 일산화탄소는 모르는 사이에 쓰러뜨린다)
        if (F.Pressure(cell) < 40 || F.Ppm(T.gCO2, cell) > 50000) return true;
        if (!a.IsPerson) return false;
        ref var lb = ref _life![a.C.Body];
        return lb.SpO2 / Math.Max(0.05f, 1 - lb.COHb) < 0.85f + 0.08f * Math.Max(a.C.LessonAir, a.C.LessonBreach);   // 겪어 본 이는 더 일찍 안다
    }

    private bool HeatBad(int cell) => F.Temp(cell) > 330 || BurningNear(cell, 1);

    /// <summary>곁(한 칸)의 불이 모두 담요로 덮을 만큼 작다 (보이는 불꽃).</summary>
    private bool SmallFireOnly(int cell)
    {
        if (_chem == null) return false;
        int x0 = L.X(cell), y0 = L.Y(cell);
        for (int y = Math.Max(0, y0 - 1); y <= Math.Min(L.H - 1, y0 + 1); y++)
            for (int x = Math.Max(0, x0 - 1); x <= Math.Min(L.W - 1, x0 + 1); x++)
                if (_chem.Burning[L.Index(x, y)] != 0 && _chem.Hrr[L.Index(x, y)] > Chemistry.BlanketMaxKw) return false;
        return true;
    }
    private bool SmokeBad(Agent a, int cell) => _light != null && _light.Visibility(cell) < 3 && !(a.IsPerson && _life![a.C.Body].Suit != 0);

    /// <summary>몸이 느끼는 대로 안전한 칸인가.</summary>
    private bool SafeAt(int c)
        => F.Pressure(c) >= 80 && F.Partial(T.gO2, c) >= 17 && F.Temp(c) <= 315 && !BurningNear(c, 3) && (_light == null || _light.Visibility(c) >= 10);

    private void Instinct(Sim sim, Agent a)
    {
        long t = sim.Tick;
        ref var c = ref a.C;
        int cell = c.Cell;
        bool air = AirBad(a, cell), heat = HeatBad(cell), smoke = SmokeBad(a, cell);
        // 꾸밈의 방화 담요: 담요를 들고 작은 불 바로 곁에 온 이는 잠깐의 열을 견디고 덮는다 (불 곁이라고 물러서면 담요를 쓸 수 없다 — 공기가 데지 않을 때만)
        if (heat && !air && c.Mode == 0 && c.Goal == GoalKind.FightFire && c.Holding == iBlanket && F.Temp(cell) <= 330 && SmallFireOnly(cell)) heat = false;
        if (c.Mode == 1)
        {
            bool arrived = a.Plan.Count == 0 || c.StepIndex >= a.Plan.Count;
            if (!air && !heat && !smoke && (arrived || SafeAt(cell)))
            {
                c.Mode = 0; c.Goal = GoalKind.Idle; a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0;
                if (c.Fear > 0.85f && c.Calm < 0.25f && Rng.Unit(sim.Seed, Rng.Key("공황"), t, a.Id) < 0.5) StartPanic(sim, a);
            }
            else if (arrived) StartFlee(sim, a, air);
            return;
        }
        if (c.Mode == 3)
        {
            // 숨을 아낀다: 공기가 나아지면 끝 · 불 · 연기 · 열은 피한다 · 20분마다 다시 살핀다 (그새 좋은 공기가 생겼나)
            if (!air) { c.Mode = 0; c.Goal = GoalKind.Idle; a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0; return; }
            if (heat || smoke || t >= c.ModeUntil) { StartFlee(sim, a, air); return; }
            if (a.Plan.Count == 0 || c.StepIndex >= a.Plan.Count) { a.Plan.Clear(); a.Plan.Add(Use(UseKind.None, Time.Minutes(5))); c.StepIndex = 0; }
            return;
        }
        if (c.Mode == 2)
        {
            if (air || heat || smoke) { StartFlee(sim, a, air); return; }
            if (t >= c.ModeUntil) { c.Mode = 0; c.Goal = GoalKind.Idle; a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0; return; }
            if (a.Plan.Count == 0 || c.StepIndex >= a.Plan.Count)
            {
                // 헤맨다: 근처 아무 칸으로
                int x = L.X(cell) + (int)(Rng.Unit(sim.Seed, Rng.Key("헤맴x"), t, a.Id) * 13) - 6;
                int y = L.Y(cell) + (int)(Rng.Unit(sim.Seed, Rng.Key("헤맴y"), t, a.Id) * 13) - 6;
                if (L.In(x, y) && L.IsSpace(L.Index(x, y)) && !BelievedRoomBad(a, RoomOf(L.Index(x, y))))
                {
                    a.Plan.Clear(); a.Plan.Add(new PlanStep { Kind = StepKind.GoTo, Cell = L.Index(x, y) }); c.StepIndex = 0; a.Path.Clear();
                }
            }
            return;
        }
        if (air || heat || smoke) StartFlee(sim, a, air);
    }

    private void StartFlee(Sim sim, Agent a, bool air)
    {
        ref var c = ref a.C;
        int target = FindSafe(a, c.Cell);
        // 갈 곳이 없다: 배 전체 공기가 고르게 나쁘다 (안전한 칸이 없고 가장 나은 칸도 산소가 3kPa 넘게 낫지 않다) · 불 · 연기 · 열 때문이 아니다 →
        // 뛰어다니면 숨만 가쁘다 — 숨을 아낀다 (저산소 환경의 행동 지침: 움직임을 줄이고 산소 공급원을 찾는다)
        if (air && a.IsPerson && !HeatBad(c.Cell) && !SmokeBad(a, c.Cell) && F.Pressure(c.Cell) >= 25
            && (target < 0 || !SafeAt(target) && F.Partial(T.gO2, target) - F.Partial(T.gO2, c.Cell) < 3)) { Conserve(sim, a); return; }
        bool first = c.Mode != 1;
        if (first && c.Mode == 0 && c.Goal is not (GoalKind.Idle or GoalKind.Flee or GoalKind.Evacuate or GoalKind.Down))
        {
            // 하던 일을 연기 · 열 · 나쁜 공기에 밀려 그만둔다 — 몇 분은 그 일로 되들어가지 않는다 (피함을 켜는 문턱과 푸는 문턱이 같아
            // 불 앞 · 연기 번진 자리에서 틱마다 들어갔다 물러나기를 되풀이했다 — 그새 연기가 걷히거나 주컴퓨터가 배기하면 다시 간다) ·
            // 위험을 다루던 일(불 끄기 · 구하기 · 막기 …)이면 막힘으로 남긴다 (원인 마디 — 주컴퓨터 · 일지가 읽는다)
            if (c.Goal is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Brace or GoalKind.Repair or GoalKind.Investigate)
                NoteSnag(sim, a, SnagKind.DrivenBack, c.Cell, -1);
            a.Avoid.Add(new AvoidEntry { Goal = c.Goal, Target = c.GoalTarget, Until = sim.Tick + Time.Minutes(3) });
            if (c.Goal != GoalKind.Investigate) a.Avoid.Add(new AvoidEntry { Goal = GoalKind.Investigate, Target = c.Cell, Until = sim.Tick + Time.Minutes(3) });   // 방금 밀려난 그 연기를 '살피러' 되들어가지도 않는다
            while (a.Avoid.Count > 16) a.Avoid.RemoveAt(0);
        }
        c.Mode = 1; c.Goal = GoalKind.Flee; c.GoalSince = sim.Tick; c.GoalTarget = -1; c.GoalCell = target; c.GoalCause = DangerCause(a, c.Cell);
        a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0;
        if (target >= 0) a.Plan.Add(new PlanStep { Kind = StepKind.GoTo, Cell = target });
        // 숨 쉴 공기가 멀거나 없다: 가까운 산소 마스크함부터 들른다 (좋은 공기가 30칸 넘게 · 마스크함이 20칸 안)
        if (air && a.IsPerson && _life![c.Body].Suit == 0 && _life[c.Body].Mask == 0 && F.Pressure(c.Cell) >= 15)
        {
            bool near = target >= 0 && SafeAt(target) && Dist(c.Cell, target) <= MaskIfFar;
            int box = near ? -1 : NearestStock(c.Cell, iMask);
            if (box >= 0 && Manhattan(c.Cell, L.Furniture[box].Cells[0]) <= MaskReach)
            {
                a.Plan.Insert(0, new PlanStep { Kind = StepKind.Wear, Target = box, Arg = iMask });
                a.Plan.Insert(0, new PlanStep { Kind = StepKind.GoTo, Cell = L.Furniture[box].Cells[0] });
            }
        }
        c.CloseHard = (byte)(air ? 1 : 0);
        c.Fear = Math.Max(c.Fear, air ? 0.9f : 0.7f);
        if (c.Carrying >= 0 && c.Brave < 0.25f) DropCarried(sim, a, false); // 겁에 질려 업던 사람을 내려놓는다
        if (first)
        {
            FledCount++;
            sim.Facts.Emit(sim.Tick, FactKind.Fled, c.Cell, 0, c.GoalCause, a.Id, air ? 1 : 0);   // 피한 까닭 (그 위험의 원인 — 불 · 샘 · 달궈진 곳)
        }
    }

    public const int MaskIfFar = 30, MaskReach = 20;
    public long Conserves;

    /// <summary>숨을 아낀다 (본능 3): 마스크가 배 어딘가 남았으면 거리와 상관없이 가서 쓰고 · 없으면 그 자리에 앉아 쉰다 (105W — 뛰는 몸은 다섯 배를 쓴다) · 20분마다 다시 살핀다.</summary>
    private void Conserve(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        bool first = c.Mode != 3;
        c.Mode = 3; c.Goal = GoalKind.Idle; c.GoalSince = sim.Tick; c.GoalTarget = -1; c.GoalCell = c.Cell; c.ModeUntil = sim.Tick + Time.Minutes(20);
        a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0;
        int box = _life![c.Body].Mask == 0 ? NearestStock(c.Cell, iMask) : -1;
        if (box >= 0)
        {
            a.Plan.Add(new PlanStep { Kind = StepKind.GoTo, Cell = L.Furniture[box].Cells[0] });
            a.Plan.Add(new PlanStep { Kind = StepKind.Wear, Target = box, Arg = iMask });
        }
        a.Plan.Add(Use(UseKind.None, Time.Minutes(5)));
        c.Fear = Math.Max(c.Fear, 0.6f);
        if (first) Conserves++;
    }

    private void StartPanic(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        c.Mode = 2; c.Goal = GoalKind.Panic; c.GoalSince = sim.Tick;
        c.ModeUntil = sim.Tick + Time.Minutes(2) + (long)(Rng.Unit(sim.Seed, Rng.Key("공황 길이"), sim.Tick, a.Id) * Time.Minutes(3));
        a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0;
        Panics++;
        sim.Facts.Emit(sim.Tick, FactKind.Panicked, c.Cell, c.Fear, 0, a.Id);
        if (_snd != null) _snd.Emit(sim, c.Cell, 85, SoundKind.Scream, 0, a.Id);
    }

    /// <summary>가장 가까운 안전한 칸 (넓이 우선 · 많아야 2000칸 · 안 열린다고 믿는 문은 피한다). 없으면 공기가 가장 나은 칸.</summary>
    private int FindSafe(Agent a, int from)
    {
        var q = new Queue<int>();
        var seen = new HashSet<int> { from };
        q.Enqueue(from);
        int best = -1; double bestScore = double.MinValue;
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0 && seen.Count < 2000)
        {
            int c = q.Dequeue();
            if (c != from && L.Kind[c] == CellKind.Floor)
            {
                if (SafeAt(c)) return c;
                double s = F.Pressure(c) + F.Partial(T.gO2, c) * 2 - Math.Max(0, F.Temp(c) - 300) - (BurningNear(c, 2) ? 200 : 0) - Dist(c, from) * 0.5;
                if (s > bestScore) { bestScore = s; best = c; }
            }
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || !L.IsSpace(j) || seen.Contains(j)) continue;
                if (L.Kind[j] == CellKind.Door && !F.DoorPassable(L.DoorAt[j]) && Believes(a, BeliefKind.StuckDoor, j, 0) > 0.3f) continue;
                seen.Add(j); q.Enqueue(j);
            }
        }
        return best;
    }
}
