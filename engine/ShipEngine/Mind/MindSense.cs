namespace ShipEngine;

/// <summary>⑧ 마음 — 감각 · 믿음 · 외침 · 본능.</summary>
public sealed partial class Mind
{
    private readonly List<int> _fresh = new();   // 이번 틱 새로 생긴 급한 믿음 (믿음 목록의 번호)
    private readonly HashSet<int> _sparks = new();
    private int[] _devAt = Array.Empty<int>();

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
            if (conf >= b.Conf || src == BeliefSource.Saw) { b.Cell = cell; b.Src = src; }
            b.Conf = Math.Max(b.Conf, conf);
            b.Seen = tick;
            if (cause != 0) b.Cause = cause;
            if (size > 0) b.Size = size;
            list[i] = b;
            if (wasWeak && b.Conf >= 0.5f && Urgent(kind)) _fresh.Add(i);
            return wasWeak && b.Conf >= 0.5f;
        }
        var nb = new Belief { Kind = kind, Src = src, Cell = cell, Subject = subject, Conf = Math.Min(1, conf), Seen = tick, Cause = cause, Size = size };
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

    /// <summary>이 사람이 그 종류를 그 칸 근처(r칸)에 믿나 — 가장 확신하는 것.</summary>
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

    // ───────────────────────── 감각 ─────────────────────────

    private void Sense(Sim sim, Agent a, double dt)
    {
        long t = sim.Tick;
        ref var c = ref a.C;
        _fresh.Clear();
        if (a.Id == 0) PrepareSenses();
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

        // 귀 — 잠들어도 듣는다
        if (_snd != null)
        {
            float loudest = 0;
            foreach (var h in _snd.At(cell))
            {
                if (h.Event <= c.HeardUpTo) continue;
                Hear(sim, a, h);
                if (h.Actor != a.Id) loudest = Math.Max(loudest, h.Db);
            }
            c.HeardUpTo = _snd.LastEvent;
            if (asleep && loudest >= 60) Wake(sim, a, "소리에 깼다");
        }

        if (person && asleep)
        {
            // 몸이 깨운다: 숨참(산소 분압 · 이산화탄소 — 일산화탄소는 아니다) · 추위 · 더위 · 귀 먹먹
            double p = F.Pressure(cell);
            if (F.Partial(T.gO2, cell) < 15 || F.Ppm(T.gCO2, cell) > 30000 || F.Temp(cell) < 283 || F.Temp(cell) > 315 || Math.Abs(p - c.LastKPa) > 3)
                Wake(sim, a, "몸이 깨웠다");
            c.LastKPa = (float)p;
            Decay(a, t, dt);
            return;
        }

        // 눈
        int room = RoomOf(cell);
        double radius = SightRadius(a, cell);
        if (room >= 0)
        {
            foreach (int x in L.Rooms[room].Cells) SeeCell(sim, a, x, radius);
            foreach (int x in _roomDoors[room]) SeeCell(sim, a, x, radius);
            foreach (int w in _roomWalls[room])
                if (F.Hole[w] > 0 && Dist(w, cell) <= radius + 1)
                    Learn(a, t, BeliefKind.Breach, w, -1, 1, BeliefSource.Saw, _st?.DamageCause[w] ?? 0, F.Hole[w]);
            // 휘어 끼익거리는 문 · 벽 (부하가 정격의 60% 넘음) — 가서 보면 안다
            if (_st != null)
            {
                foreach (int w in _roomWalls[room])
                    if (_st.Stress[w] >= 0.6f && _st.Braced[w] == 0 && F.Hole[w] <= 0 && Dist(w, cell) <= 3)
                        Learn(a, t, BeliefKind.Strain, w, -1, Math.Min(1f, _st.Stress[w]), BeliefSource.Saw, _st.DamageCause[w], _st.Stress[w]);
                foreach (int x in _roomDoors[room])
                    if (_st.Stress[x] >= 0.6f && _st.Braced[x] == 0 && Dist(x, cell) <= 3)
                        Learn(a, t, BeliefKind.Strain, x, -1, Math.Min(1f, _st.Stress[x]), BeliefSource.Saw, _st.DamageCause[x], _st.Stress[x]);
            }
            // 닫힌 문이 바람을 빨아들인다 (서리 · 틈새 바람 · 쉬익) — 그 너머가 빠졌다: 문틈도 막을 구멍이다
            foreach (int x in _roomDoors[room])
            {
                int d = L.DoorAt[x];
                if (d < 0 || !F.DoorGapOpen(d) || Dist(x, cell) > 3) continue;
                double dp = DoorDeltaKPa(x);
                if (dp > 20) Learn(a, t, BeliefKind.Breach, x, -1, (float)Math.Min(1, dp / 60), BeliefSource.Saw, _st?.LowSideCause(x) ?? 0, (float)Field.LeakNormalDoor);
            }
            if (L.Kind[cell] == CellKind.Door)
            {
                Span<int> nb = stackalloc int[4];
                L.Neighbors(cell, nb);
                foreach (int j in nb)
                    if (j >= 0 && L.Room[j] >= 0 && L.Room[j] != room)
                        foreach (int x in L.Rooms[L.Room[j]].Cells) SeeCell(sim, a, x, radius);
            }
        }
        // 공기가 빠졌다 (사람은 몸으로 · 로봇은 센서로)
        double kpa = F.Pressure(cell);
        if (kpa < 50) Learn(a, t, BeliefKind.Vacuum, cell, -1, kpa < 20 ? 1f : 0.7f, person ? BeliefSource.Felt : BeliefSource.Saw);

        if (person)
        {
            ref var body = ref _life![c.Body];
            // 코
            double soot = F.Ppm(T.gSoot, cell);
            if (soot > 30) Learn(a, t, BeliefKind.SmokeSmell, cell, -1, (float)Math.Min(1, soot / 300), BeliefSource.Smelled);
            if (F.Ppm(T.gNH3, cell) > 25 || F.Ppm(T.gO3, cell) > 0.1) Learn(a, t, BeliefKind.BadAir, cell, -1, 0.8f, BeliefSource.Smelled);
            // 몸
            if (F.Partial(T.gO2, cell) < 16 || F.Ppm(T.gCO2, cell) > 20000) Learn(a, t, BeliefKind.BadAir, cell, -1, 0.7f, BeliefSource.Felt);
            // 두통 — 냄새는 없다. 머리가 점점 아파질 때(일산화탄소 헤모글로빈이 오를 때)만 '여기 공기가 이상하다'
            if (body.COHb > 0.08f && body.COHb > c.LastCOHb + 1e-4f) Learn(a, t, BeliefKind.BadAir, cell, -1, 0.5f, BeliefSource.Felt);
            c.LastCOHb = body.COHb;
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
                if (b.Src is BeliefSource.Saw or BeliefSource.Felt)
                {
                    sim.Facts.Emit(t, FactKind.Noticed, b.Cell, b.Conf, b.Cause, a.Id, (int)b.Kind);
                    if (b.Kind is BeliefKind.Fire or BeliefKind.Vacuum or BeliefKind.Breach) c.Fear = Math.Max(c.Fear, person ? 0.5f + 0.4f * (1 - c.Brave) : 0);
                    if (b.Kind == BeliefKind.Dead && person) { c.Fear = Math.Max(c.Fear, 0.6f); c.Sad = Math.Min(1, c.Sad + 0.3f); }
                }
                if (b.Conf > bc) { bc = b.Conf; best = i; }
            }
            if (best >= 0 && (person ? c.Up != 0 : true) && t - c.LastShout >= 10 && a.Beliefs[best].Src != BeliefSource.Told)
                Shout(sim, a, a.Beliefs[best]);
        }
        Disconfirm(a, t, room, radius);
        Decay(a, t, dt);
    }

    /// <summary>매 틱 한 번: 불꽃(합선) 칸 · 장치 칸 표.</summary>
    private void PrepareSenses()
    {
        _sparks.Clear();
        if (_net == null) return;
        foreach (int fc in _net.FaultCell) if (fc >= 0) _sparks.Add(fc);
        if (_devAt.Length != L.N)
        {
            _devAt = Enumerable.Repeat(-1, L.N).ToArray();
            foreach (var d in _net.Devices) foreach (int x in L.Furniture[d.Furniture].Cells) _devAt[x] = d.Id;
        }
    }

    /// <summary>보이는 거리: 사람은 그을음 시야(많아야 30m) · 어두우면 1.5m · 로봇은 12m(열 감지).</summary>
    private double SightRadius(Agent a, int cell)
    {
        if (!a.IsPerson) return 12;
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
        if (_light != null && _light.Visibility(x) < 8) Learn(a, t, BeliefKind.Smoke, x, -1, 0.9f, BeliefSource.Saw);
        if (F.Stored(T.sWater, x) > 100) Learn(a, t, BeliefKind.Water, x, -1, 0.9f, BeliefSource.Saw);
        if (_sparks.Contains(x)) Learn(a, t, BeliefKind.Spark, x, -1, 1, BeliefSource.Saw);
        if (d <= 3 && _devAt.Length == L.N && _devAt[x] >= 0 && _net != null)
        {
            int dev = _devAt[x];
            bool broken = false;
            for (int p = 0; p < 4; p++) if (_net.Broken[dev * 4 + p] != 0) { broken = true; break; }
            if (broken) Learn(a, t, BeliefKind.Broken, x, dev, 1, BeliefSource.Saw);
            else Forget(a, b => b.Kind == BeliefKind.Broken && b.Subject == dev);
        }
        for (int bi = _bodyHead.Length == L.N ? _bodyHead[x] : -1; bi >= 0; bi = _bodyNext[bi])
        {
            if (a.IsPerson && bi == a.C.Body) continue;
            var body = _life!.Bodies[bi];
            if (body.Alive == 0) { Learn(a, t, BeliefKind.Dead, x, bi, 1, BeliefSource.Saw, body.DeathCause); Forget(a, b => b.Kind == BeliefKind.Injured && b.Subject == bi); }
            else if (body.Conscious == 0) Learn(a, t, BeliefKind.Injured, x, bi, 1, BeliefSource.Saw, body.HurtCause);
            else Forget(a, b => b.Kind is BeliefKind.Injured or BeliefKind.Help && b.Subject == bi);
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
            if (!Visible(a, b.Cell, room, radius)) continue;
            b.Conf = 0;
            a.Beliefs[i] = b;
            a.Doubts.RemoveAll(d => d.Until <= t);
            if (a.Doubts.Count < 8) a.Doubts.Add(new Doubt { Kind = b.Kind, Cell = b.Cell, Until = t + Time.Minutes(3) });
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

    private void Hear(Sim sim, Agent a, in Heard h)
    {
        long t = sim.Tick;
        switch (h.Kind)
        {
            case SoundKind.Bang when h.Db >= 45:
                Learn(a, t, BeliefKind.Bang, h.Source, -1, (float)Math.Min(1, (h.Db - 40) / 50), BeliefSource.Heard, h.Cause, h.Db); break;
            case SoundKind.Crack or SoundKind.Creak:
                Learn(a, t, BeliefKind.Strain, h.Source, -1, (float)Math.Min(1, (h.Db - 30) / 40), BeliefSource.Heard, h.Cause, h.Db); break;
            case SoundKind.Hiss:
                Learn(a, t, BeliefKind.Leak, h.Source, -1, (float)Math.Min(1, (h.Db - 30) / 40), BeliefSource.Heard, h.Cause, h.Db); break;
            case SoundKind.Roar:
                Learn(a, t, BeliefKind.Fire, h.Source, -1, 0.5f, BeliefSource.Heard, h.Cause); break;
            case SoundKind.Scream when h.Actor != a.Id:
                Learn(a, t, BeliefKind.Help, h.Source, h.Actor, 0.8f, BeliefSource.Heard, h.Cause); break;
            case SoundKind.Alarm:
            case SoundKind.Voice when h.Actor != a.Id:
                foreach (var (ev, _, sp, b) in _said)
                    if (ev == h.Event && sp != a.Id)
                    {
                        float clarity = (float)Math.Clamp((h.Db - 25) / 30, 0.2, 1);
                        Learn(a, t, b.Kind, b.Cell, b.Subject, b.Conf * 0.8f * clarity, sp == Computer ? BeliefSource.Screen : BeliefSource.Told, b.Cause, b.Size);
                        if (a.IsPerson && Urgent(b.Kind)) a.C.Fear = Math.Max(a.C.Fear, 0.3f * (1 - a.C.Brave));
                    }
                break;
        }
    }

    /// <summary>외친다: 75dB 말 (로봇은 80dB 안내) — 들은 사람은 같은 믿음을 '말'로 얻는다.</summary>
    private void Shout(Sim sim, Agent a, Belief b)
    {
        if (_snd == null) return;
        int ev = _snd.Emit(sim, a.C.Cell, a.IsPerson ? 75 : 80, SoundKind.Voice, b.Cause, a.Id);
        _said.Add((ev, sim.Tick, a.Id, b));
        a.C.LastShout = sim.Tick;
        Shouts++;
        sim.Facts.Emit(sim.Tick, FactKind.Warned, b.Cell, b.Conf, b.Cause, a.Id, (int)b.Kind);
    }

    private void Wake(Sim sim, Agent a, string why)
    {
        ref var body = ref _life![a.C.Body];
        if (body.Asleep == 0) return;
        body.Asleep = 0;
        a.C.WokeAt = sim.Tick;
        a.C.Goal = GoalKind.Idle; a.Plan.Clear(); a.Path.Clear(); a.C.StepIndex = 0;
        sim.Facts.Emit(sim.Tick, FactKind.Woke, a.C.Cell, 0, 0, a.Id);
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
        // 몸은 산소가 모자란 것을 직접 느끼지 못한다 (숨이 찬 것은 이산화탄소 때문) — 낮은 기압 · 이산화탄소만 본능을 깨운다
        return F.Pressure(cell) < 40 || F.Ppm(T.gCO2, cell) > 50000;
    }

    private bool HeatBad(int cell) => F.Temp(cell) > 330 || BurningNear(cell, 1);
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
        bool first = c.Mode != 1;
        c.Mode = 1; c.Goal = GoalKind.Flee; c.GoalSince = sim.Tick; c.GoalTarget = -1; c.GoalCell = target;
        a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0;
        if (target >= 0) a.Plan.Add(new PlanStep { Kind = StepKind.GoTo, Cell = target });
        c.CloseHard = (byte)(air ? 1 : 0);
        c.Fear = Math.Max(c.Fear, air ? 0.9f : 0.7f);
        if (c.Carrying >= 0 && c.Brave < 0.25f) DropCarried(sim, a, false); // 겁에 질려 업던 사람을 내려놓는다
        if (first)
        {
            FledCount++;
            sim.Facts.Emit(sim.Tick, FactKind.Fled, c.Cell, 0, 0, a.Id, air ? 1 : 0);
        }
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
