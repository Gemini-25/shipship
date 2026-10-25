namespace ShipEngine;

/// <summary>
/// ④ 깨짐 · 이음매 · 가루 · 쏟음 · 찌그러짐 · 물체 소리 (docs/PHYSICS.md §5 · §6-5 — PH8): 잔걸음 동안 맞음을 적어 두고 (그 때의 상태 · 흡수 에너지 · 상대 재질) ·
/// 틱 끝에 물체마다 가장 센 맞음 하나로 깨질지 정한다 (자연 사건 = 월드 법칙의 확률 — 와이불 P = 1 − exp(−(ε/ε₀)³) · 난수 '깨짐' · 틱 · Id).
/// ε = (내 몫 흡수 E / m) × 단단함 몫 E상대 / (E상대 + 0.1 E자신) + 0.1 Hurt · ε₀ = 130 G_c / (ρ L) (G_c = K_c² / E · L = 발자국의 가장 긴 변).
/// 깨지면 맞은 그 때의 자리에서 조각 (온전한 꼴의 가면 — 보로노이 씨앗 N = clamp(round(2 (ε/ε₀)^(2/3)) + 1, 2, 16) · 4이웃으로 이어진 것만 한 조각 · 여러 부위면 부위마다 먼저) ·
/// 3도트 아래는 칸 가루 · 흩어짐 (질량 가중 평균을 뺀 바깥 몫 — 운동량 · 각운동량 그대로 · 흡수 에너지의 3할 · 3m/s 상한) · 담긴 물은 그 칸 바닥으로 (원인 = 깨짐).
/// 이음매 (부위가 바뀌는 곳 · JointEps)만 넘으면 부위대로만 가른다. 깨지지 않으면 금이 쌓이고 (Hurt) · 금속은 찌그러진다 (미노르스키 V = E / (47.2MJ/m³ × 강도/250)) ·
/// 넘어지거나 1m/s 넘게 맞으면 담긴 물 반을 쏟는다. 소리는 방마다 · 갈래마다 틱에 한 번 (가장 큰 것 — 55 + 10 log₁₀E dB · 깨짐 +6 · 3.2kJ 넘으면 '쾅').
/// </summary>
public sealed partial class Structure
{
    public const double BreakKb = 130, ScatterShare = 0.3, ScatterMaxV = 3, SpillV = 1, DentPa = 47.2e6, HardGpa = 5, BangJ = 3200, NoiseJ = 0.1, ScrapeV = 1;
    public const int GritDots = 3;
    private static readonly ulong KeyBreak = Rng.Key("깨짐"), KeyJoint = Rng.Key("이음매");
    private const byte HowFell = 0, HowWall = 1, HowProp = 2, HowTopple = 3;

    /// <summary>잔걸음 동안의 맞음 하나 (같은 틱에 쓰고 읽는다 — 찍지 않는다): 물체 · 흡수 에너지 (J — 그 물체 몫) · 단단함 몫 · 다가온 빠르기 · 떨어진 높이 · 닿은 점 · 그 때 (초) · 어떻게 · 상대 재질 · 맞은 뒤 그 때의 상태.</summary>
    private struct BreakHit
    {
        public int Id, Seq;
        public double E, S, Speed, Fall, Px, Py, T;
        public byte Sub, How, Mat;
        public Prop After;
    }
    private readonly List<BreakHit> _breakHits = new();

    /// <summary>물체 소리 하나 (틱 끝에 방마다 · 갈래마다 가장 큰 것만 낸다 — 찍지 않는다).</summary>
    private struct PropNoise
    {
        public int Room, Cell, Cause, Seq;
        public SoundKind Kind;
        public double E;
        public byte Sub, Mat;
        public float Size;
    }
    private readonly List<PropNoise> _noises = new();
    private readonly Dictionary<int, DotForm> _pieceForms = new();   // 조각 가면 번호 → 꼴 (찍지 않는다 — Load에서 비운다)
    /// <summary>시험 설정 (찍지 않는다 — <see cref="Debris"/>처럼): false면 물체가 깨지거나 이음매가 갈라지지 않는다 (금 · 찌그러짐 · 쏟음 · 소리는 그대로 — 깨짐 밖의 역학을 재는 법칙 시험).</summary>
    public bool Breakage = true;

    /// <summary>조각이 아닌 온전한 꼴 (물건 · 누움 — 조각의 가면은 늘 이 틀 위에 있다).</summary>
    public DotForm WholeForm(in Prop p)
    {
        if (p.Piece <= 0) return PropForm(p);
        if ((p.Flags & FurnPieceFlag) != 0 && p.What >= 0 && p.What < L.Furniture.Count) return FurnWhole(p.What, p.Pose);   // 가구 조각 (PH10)
        var q = p; q.Piece = 0;
        return PropForm(q);
    }

    /// <summary>조각 꼴 (가면 · 부위대로 온전한 꼴을 자른 것 — 세계마다 한 번 · 꼴 자체는 여러 세계가 함께 쓴다).</summary>
    private DotForm PieceForm(in Prop p)
    {
        if (_pieceForms.TryGetValue(p.Piece, out var fm)) return fm;
        _pieceForms[p.Piece] = fm = PieceLook(p);
        return fm;
    }

    /// <summary>조각의 꼴 (세계 캐시를 건드리지 않는다 — 그리는 쪽이 읽는다).</summary>
    public DotForm PieceLook(in Prop p)
    {
        var mk = Masks[p.Piece - 1];
        var q = p; q.Piece = 0;
        _itemNames ??= T.People.Items.Keys.ToArray();
        string name = q.What >= 0 && q.What < _itemNames.Length ? _itemNames[q.What] : "_default";
        var whole = (q.Flags & FurnPieceFlag) != 0 && q.What >= 0 && q.What < L.Furniture.Count ? FurnWhole(q.What, q.Pose) : DotForge.Make(T, name, lie: q.Pose);   // 가구 조각은 그 가구 꼴을 (PH10)
        return DotForge.Piece(whole, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(MaskBits).Slice(mk.Bit, mk.H), mk.Y0, mk.Part);
    }

    /// <summary>읽기 (화면 살펴보기 — 쓰지 않는다): 쌓인 맞음이 깨짐 문턱에 대한 몫 (0.1 Hurt ÷ ε₀ — 맞을 때 더해지는 몫과 같은 셈 · 1이면 다음 맞음 없이도 깨질 만큼).</summary>
    public double HurtShare(in Prop p)
    {
        if (p.Kind is not (0 or 1 or 2)) return 0;
        var fm = p.Piece > 0 ? PieceLook(p) : PropForm(p);
        double e0 = BreakEps(fm, MaterialOf(p));
        return e0 > 0 ? 0.1 * p.Hurt / e0 : 0;
    }

    /// <summary>읽기: 물체의 겉 재질 (화면 · 원인 글).</summary>
    public MaterialDef PropMaterial(in Prop p) => MaterialOf(p);

    /// <summary>깨짐 문턱 ε₀ (J/kg): 130 G_c / (ρ L) — G_c = K_c² / E (J/m²) · L = 발자국의 가장 긴 변 (m).</summary>
    public static double BreakEps(DotForm fm, MaterialDef m)
    {
        int x0 = fm.W, y0 = fm.D, x1 = -1, y1 = -1;
        for (int y = 0; y < fm.D; y++)
            for (int x = 0; x < fm.W; x++)
                if (fm.Solid(x, y)) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        double len = Math.Max(1, Math.Max(x1 - x0 + 1, y1 - y0 + 1)) * DotForm.Dot, gc = m.Kc * m.Kc / Math.Max(1e-9, m.E) * 1000;
        return BreakKb * gc / (Math.Max(1, m.Density) * len);
    }

    /// <summary>
    /// 잔걸음에서 물체가 맞았다 (떨어져 닿음 · 벽 · 가구 · 다른 물체 · 넘어져 누움): 흡수 에너지 e (J — 이 물체 몫) · 상대 재질 · 닿은 점 · 다가온 빠르기 · 떨어진 높이 · 그 때 (초).
    /// 맞은 뒤의 상태를 함께 적는다 (깨지면 조각이 그 자리 · 그 빠르기에서 나온다).
    /// </summary>
    private void NoteHit(Sim sim, in Prop p, double e, byte other, double px, double py, double speed, double fall, byte how, double t)
    {
        if (e <= 0 || p.Kind is not (0 or 1 or 2) || p.Kg <= 0) return;
        double es = MaterialOf(p).E, eo = T.Materials[Math.Min(other, T.Materials.Count - 1)].Def.E;
        byte sub = _tickDt > 0 ? (byte)Math.Clamp((int)Math.Round(t / _tickDt * 255), 0, 255) : sim.Facts.SubNow;   // 닿은 그 때 (잔걸음 처음이 아니라 — 떨어져 닿는 때는 잔걸음 안이다)
        _breakHits.Add(new BreakHit { Id = p.Id, Seq = _breakHits.Count, E = e, S = eo / (eo + 0.1 * es), Speed = speed, Fall = fall, Px = px, Py = py, T = t, Sub = sub, How = how, Mat = other, After = p });
    }

    /// <summary>
    /// 섬의 부딪는 닿음 (충격량을 푼 뒤 · 자리를 옮기기 전 — 그 잔걸음 처음 때 τ): 잃은 에너지 ½ Jn (−vn₀)(1 − e) — 벽 · 가구 · 문은 제 몫 통째 · 물체끼리는 반씩 ·
    /// 사람 · 로봇 원판은 무르다 (깨지지 않는다 — 맞음 장부는 따로). 물체마다 가장 센 닿음의 재질 · 팔 · 다가온 빠르기로 맞음 하나.
    /// </summary>
    private void IslandHits(Sim sim, Span<Prop> all, double tau)
    {
        for (int k = 0; k < _icount; k++) { ref var r = ref _runs[k]; r.HitE = 0; r.HitJ = 0; r.HitV = 0; }
        for (int c = 0; c < _nic; c++)
        {
            ref var ct = ref _ic[c];
            if (!ct.Bounce || ct.Jn <= 0 || ct.Cell <= -2) continue;
            double loss = 0.5 * ct.Jn * -ct.Vn0 * (1 - ct.E);
            if (loss <= 0) continue;
            if (ct.B < 0) Acc(ref _runs[ct.A], loss, ct.Jn, -ct.Vn0, ct.Mat, ct.Rx, ct.Ry, HowWall);
            else
            {
                byte ma = MatOf(all[_runs[ct.A].I]), mb = MatOf(all[_runs[ct.B].I]);
                Acc(ref _runs[ct.A], 0.5 * loss, ct.Jn, -ct.Vn0, mb, ct.Rx, ct.Ry, HowProp);
                Acc(ref _runs[ct.B], 0.5 * loss, ct.Jn, -ct.Vn0, ma, ct.Qx, ct.Qy, HowProp);
            }
        }
        for (int k = 0; k < _icount; k++)
        {
            ref var r = ref _runs[k];
            if (r.HitE <= 0) continue;
            ref var p = ref all[r.I];
            NoteHit(sim, p, r.HitE, r.HitMat, p.X + r.HitRx, p.Y + r.HitRy, r.HitV, 0, r.HitHow, tau);
        }

        static void Acc(ref PropRun r, double e, double j, double v, byte mat, double rx, double ry, byte how)
        {
            r.HitE += e; r.HitV = Math.Max(r.HitV, v);
            if (j > r.HitJ) { r.HitJ = j; r.HitMat = mat; r.HitRx = rx; r.HitRy = ry; r.HitHow = how; }
        }
    }

    /// <summary>물체 소리 하나를 적어 둔다 (틱 끝에 모아 낸다).</summary>
    private void Noise(int cell, SoundKind kind, double e, in Prop p, byte sub)
    {
        if (e < NoiseJ || cell < 0 || cell >= L.N) return;
        var fm = PropForm(p);
        _noises.Add(new PropNoise { Room = L.Room[cell], Cell = cell, Cause = p.Cause, Seq = _noises.Count, Kind = kind, E = e, Sub = sub, Mat = MatOf(p), Size = (float)(Math.Max(fm.W, fm.D) * DotForm.Dot) });
    }

    /// <summary>단단한 물건 (E ≥ 5GPa — 금속 · 유리 · 세라믹)이 닿으면 덜그럭 · 무른 것 (고분자 · 천 · 폼 · 음식)은 쿵 · 3.2kJ 넘으면 쾅.</summary>
    private SoundKind HitKind(in Prop p, double e) => e > BangJ ? SoundKind.Bang : MaterialOf(p).E >= HardGpa ? SoundKind.Knock : SoundKind.Thud;

    /// <summary>
    /// 틱 끝 (몸이 되받은 뒤): 물체마다 (Id 차례 — 맞은 차례로 쌓였다) 가장 센 맞음 하나로 깨질지 · 이음매가 갈라질지 정한다 ·
    /// 아니면 금이 쌓이고 · 찌그러지고 · 담긴 물 반을 쏟는다. 맞음마다 소리를 적는다.
    /// </summary>
    private void Breaks(Sim sim, bool world)
    {
        _breakHits.Sort((a, b) => a.Id != b.Id ? a.Id.CompareTo(b.Id) : a.Seq.CompareTo(b.Seq));
        for (int k = 0; k < _breakHits.Count;)
        {
            int id = _breakHits[k].Id, end = k, i = IndexOf(id);
            while (end < _breakHits.Count && _breakHits[end].Id == id) end++;
            if (i >= 0) BreakOne(sim, world, i, k, end);
            k = end;
        }
        _breakHits.Clear();
    }

    private void BreakOne(Sim sim, bool world, int i, int k0, int k1)
    {
        var p = Props[i];
        if (p.Kind is not (0 or 1 or 2) || p.State is 5 or 6 or 7) return;
        var cur = PropForm(p);
        double m = cur.Mass;
        if (m <= 0) return;
        int best = k0;
        double sum = 0, top = -1, fast = 0;
        bool toppled = false;
        for (int k = k0; k < k1; k++)
        {
            var h = _breakHits[k];
            double eps = h.E / m * h.S;
            sum += eps; fast = Math.Max(fast, h.Speed); toppled |= h.How == HowTopple;
            if (eps > top) { top = eps; best = k; }
        }
        var hit = _breakHits[best];
        var mat = MaterialOf(p);
        double e = top + 0.1 * p.Hurt, e0 = BreakEps(cur, mat);
        bool shatter = Breakage && Rng.Unit(sim.Seed, KeyBreak, sim.Tick, p.Id) < 1 - Math.Exp(-Math.Pow(e / e0, 3));
        bool joint = Breakage && !shatter && cur.Parts > 1 && Rng.Unit(sim.Seed, KeyJoint, sim.Tick, p.Id) < 1 - Math.Exp(-Math.Pow(e / cur.JointEps, 3));
        // 소리: 맞음마다 (깨지는 맞음은 쨍그랑 +6dB)
        for (int k = k0; k < k1; k++)
        {
            var h = _breakHits[k];
            int cell = CellOf(h.After);
            if ((shatter || joint) && k == best) Noise(cell, h.E > BangJ ? SoundKind.Bang : shatter && mat.E >= HardGpa ? SoundKind.Shatter : HitKind(p, h.E), h.E, p, h.Sub);
            else Noise(cell, HitKind(p, h.E), h.E, p, h.Sub);
        }
        if (shatter || joint) { Shatter(sim, world, i, hit, e, e0, !shatter); return; }
        // 깨지지 않았다: 금이 쌓인다 · 금속은 찌그러진다 · 넘어지거나 1m/s 넘게 맞으면 담긴 물 반을 쏟는다
        p.Hurt += (float)sum;
        if (mat.Id is "steel" or "aluminum" or "titanium" or "copper") Dent(ref p, cur, hit, mat);
        if (p.Liquid > 0 && (toppled || fast > SpillV))
        {
            int cell = CellOf(p);
            float half = p.Liquid * 0.5f;
            if (cell >= 0 && F.Severed[cell] == 0)
            {
                int sc = sim.Causes.Add(CauseKind.Impact, toppled ? $"넘어진 {PropName(p)}에서 물이 쏟아졌다" : $"부딪힌 {PropName(p)}에서 물이 쏟아졌다", sim.Tick, cell, p.Cause);
                F.AddStore(cell, T.sWater, half, sc);
                p.Liquid -= half;
            }
        }
        Props[i] = p;
    }

    /// <summary>찌그러짐 (금속 · 연성 — 미노르스키): 부피 V = E / (47.2MJ/m³ × 강도/250) · 반지름 ∛V (도트 — 1 넘으면) · 닿은 점 (꼴 도트) · 많아야 둘 (작은 것을 바꾼다).</summary>
    private void Dent(ref Prop p, DotForm fm, in BreakHit hit, MaterialDef mat)
    {
        double v = hit.E / (DentPa * Math.Max(1, mat.Strength) / 250), r = Math.Cbrt(v) / DotForm.Dot;
        int rd = (int)Math.Round(r);
        if (rd < 1) return;
        var (u, w) = FormDot(hit.After, fm, hit.Px, hit.Py);
        uint d = (uint)(Math.Clamp(u, 0, 255) | Math.Clamp(w, 0, 255) << 8 | Math.Min(255, rd) << 16);
        if (p.Dent0 == 0) p.Dent0 = d;
        else if (p.Dent1 == 0) p.Dent1 = d;
        else if ((p.Dent1 >> 16 & 0xFF) < rd) p.Dent1 = d;
    }

    /// <summary>세상 점 (도면 m)의 꼴 도트 자리 (그 물체의 그 때 자세 — 꼴 밖이면 끝으로).</summary>
    private static (int x, int y) FormDot(in Prop p, DotForm fm, double px, double py)
    {
        double c = Math.Cos(p.Th), s = Math.Sin(p.Th), dx = px - p.X, dy = py - p.Y;
        double fx = fm.Cx + c * dx + s * dy, fy = fm.Cy - s * dx + c * dy;
        return (Math.Clamp((int)Math.Floor(fx / DotForm.Dot), 0, fm.W - 1), Math.Clamp((int)Math.Floor(fy / DotForm.Dot), 0, fm.D - 1));
    }

    private readonly List<(DotForm fm, ulong[] rows, int y0, byte part, int x0, int x1, int cols)> _shards = new();

    /// <summary>
    /// 깨진다 (i — 맞은 뒤 그 때의 상태 hit.After에서): 가면 조각 · 가루 · 흩어짐 · 담긴 물 · 장부 · 사실 · 원인 · 틱 안 기록. jointOnly면 이음매 (부위)대로만 가른다.
    /// 운동량 · 각운동량은 그대로다: 틱 끝 부모가 가진 몫 − 조각이 가진 몫 (가루 · 맞은 뒤 지난 몫)은 몸이 없는 배의 장부로 · 몸이 있으면 그 몸이 되받는다.
    /// </summary>
    private void Shatter(Sim sim, bool world, int i, in BreakHit hit, double eps, double eps0, bool jointOnly)
    {
        var p0 = Props[i];
        var after = hit.After;
        var whole = WholeForm(p0);
        var cur = PropForm(p0);
        int W = whole.W, D = whole.D, n = W * D;
        byte sel = p0.Piece > 0 ? Masks[p0.Piece - 1].Part : (byte)255;
        // 고른 기둥 (조각이면 그 가면 · 아니면 온전한 꼴의 찬 기둥 — 조각 꼴의 찬 기둥이 곧 그것이다)
        var mine = new bool[n];
        for (int c = 0; c < n; c++) mine[c] = cur.Top[c] > cur.Bot[c];
        // 부위 (여러 부위 꼴은 부위마다 먼저 — 위아래로 붙은 기둥은 두 부위 모두에 든다)
        var parts = new List<byte>();
        if (sel == 255 && whole.Parts > 1)
        {
            for (int c = 0; c < n; c++)
            {
                if (!mine[c]) continue;
                byte lo = whole.PartLo != null ? whole.PartLo[c] : whole.Part[c];
                if (!parts.Contains(lo)) parts.Add(lo);
                if (!parts.Contains(whole.Part[c])) parts.Add(whole.Part[c]);
            }
            parts.Sort();
        }
        else parts.Add(sel);
        // 보로노이 씨앗 (씨앗 0은 닿은 도트 · k는 p₀ + R√u (cos a, sin a) — Structure.DotHash(Id·16 + k, 깨진 수))
        int seeds = jointOnly ? 1 : Math.Clamp((int)Math.Round(2 * Math.Pow(eps / eps0, 2.0 / 3)) + 1, 2, 16);
        var (hx, hy) = FormDot(after, cur, hit.Px, hit.Py);
        Span<double> sx = stackalloc double[16], sy = stackalloc double[16];
        double rr = Math.Max(W, D);
        sx[0] = hx + 0.5; sy[0] = hy + 0.5;
        for (int k = 1; k < seeds; k++)
        {
            double u = DotHash(p0.Id * 16 + k, (int)Shattered) / 4294967296.0, a = DotHash(p0.Id * 16 + k, (int)Shattered + 7919) / 4294967296.0 * 2 * Math.PI;
            sx[k] = hx + 0.5 + rr * Math.Sqrt(u) * Math.Cos(a); sy[k] = hy + 0.5 + rr * Math.Sqrt(u) * Math.Sin(a);
        }
        Span<int> label = n <= 4096 ? stackalloc int[n] : new int[n];
        _shards.Clear();
        var stack = new Stack<int>();
        foreach (byte part in parts)
        {
            bool Has(int c) => mine[c] && (part == 255 || whole.Part[c] == part || whole.PartLo != null && whole.PartLo[c] == part);
            for (int c = 0; c < n; c++)
            {
                label[c] = -1;
                if (!Has(c)) continue;
                int x = c % W, y = c / W, near = 0;
                double jx = (int)(DotHash(c, p0.Id ^ (int)Shattered) % 3) - 1, jy = (int)(DotHash(c, p0.Id ^ (int)Shattered ^ 0x55) % 3) - 1, bd = double.MaxValue;   // ±1도트로 흔든다 — 들쭉날쭉한 금
                for (int k = 0; k < seeds; k++)
                {
                    double ddx = x + 0.5 + jx - sx[k], ddy = y + 0.5 + jy - sy[k], dd = ddx * ddx + ddy * ddy;
                    if (dd < bd) { bd = dd; near = k; }
                }
                label[c] = near;
            }
            // 4이웃으로 이어진 같은 씨앗 기둥이 한 조각
            for (int c0 = 0; c0 < n; c0++)
            {
                if (label[c0] < 0) continue;
                int lab = label[c0], cols = 0, x0 = W, x1 = -1, y0 = D, y1 = -1;
                var rows = new ulong[D];
                stack.Push(c0); label[c0] = -2;
                while (stack.Count > 0)
                {
                    int c = stack.Pop(), x = c % W, y = c / W;
                    rows[y] |= 1UL << x; cols++;
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    if (x > 0 && label[c - 1] == lab) { label[c - 1] = -2; stack.Push(c - 1); }
                    if (x < W - 1 && label[c + 1] == lab) { label[c + 1] = -2; stack.Push(c + 1); }
                    if (y > 0 && label[c - W] == lab) { label[c - W] = -2; stack.Push(c - W); }
                    if (y < D - 1 && label[c + W] == lab) { label[c + W] = -2; stack.Push(c + W); }
                }
                var rs = rows.AsSpan(y0, y1 - y0 + 1).ToArray();
                byte pp = whole.Parts > 1 ? part : (byte)255;
                _shards.Add((DotForge.Piece(whole, rs, y0, pp), rs, y0, pp, x0, x1, cols));
            }
        }
        // 조각 · 가루 (3도트 아래) — 맞은 그 때의 자세에서 꼴 틀 무게중심 차이만큼
        double th = after.Th, cs = Math.Cos(th), sn = Math.Sin(th);
        string name = PropName(p0);
        string what = jointOnly ? $"{name}의 이음매가 갈라졌다" : $"{Ko.I(name)} 깨졌다";
        string how = hit.How switch
        {
            HowFell => $"떨어진 {what} ({hit.Fall:0.00}m · {hit.Speed:0.0}m/s)",
            HowWall => $"벽에 부딪힌 {what} ({hit.Speed:0.0}m/s)",
            HowProp => $"다른 물건에 부딪힌 {what} ({hit.Speed:0.0}m/s)",
            _ => $"넘어진 {what}",
        };
        int hitCell = CellOf(after) >= 0 ? CellOf(after) : CellOf(p0);
        int cause = sim.Causes.Add(CauseKind.Impact, how, sim.Tick, hitCell, p0.Cause);
        var made = new List<Prop>();
        double gritKg = 0;
        foreach (var sh in _shards)
        {
            var pf = sh.fm;
            if (pf.Mass <= 0) continue;
            double dx = pf.Cx - cur.Cx, dy = pf.Cy - cur.Cy, rx = cs * dx - sn * dy, ry = sn * dx + cs * dy;
            if (sh.cols < GritDots)
            {
                int gc = CellAt(after.X + rx, after.Y + ry);
                if (gc >= 0) { Grit[gc] += (float)pf.Mass; GritMat[gc] = BaseMatOf(pf); }
                gritKg += pf.Mass;
                continue;
            }
            int bit = MaskBits.Count;
            MaskBits.AddRange(sh.rows);
            Masks.Add(new PropMask { Form = whole.Id, X0 = (byte)sh.x0, Y0 = (byte)sh.y0, W = (byte)(sh.x1 - sh.x0 + 1), H = (byte)sh.rows.Length, Cx = (float)pf.Cx, Cy = (float)pf.Cy, Bit = bit, Part = sh.part });
            var q = new Prop
            {
                Id = NextPropId++, Kind = 2, Form = whole.Id, Piece = Masks.Count, What = p0.What, Mat = BaseMatOf(pf), Parent = p0.Id, Host = p0.Host, On = after.On,
                X = after.X + rx, Y = after.Y + ry, Th = th, Vx = after.Vx - after.W * ry, Vy = after.Vy + after.W * rx, W = after.W,
                Z = (float)Math.Max(0, after.Z + (pf.ZShift - cur.ZShift) * DotForm.Layer), Vz = after.Vz, Kg = (float)pf.Mass, Inertia = (float)pf.I, Pose = p0.Pose,
                State = after.State is 3 or 4 ? after.State : (byte)1, G = after.G, Roll = after.Roll, Flags = (byte)((mat4(pf) ? 4 : 0) | (p0.Kind == 1 || (p0.Flags & FurnPieceFlag) != 0 ? FurnPieceFlag : 0)), Cause = cause, Since = _now,
            };
            if (q.Z > 0 && q.State == 1) q.State = 3;
            made.Add(q);
        }
        // 흩어짐: 조각 무게중심에서 바깥 몫 (질량 가중 평균을 빼 운동량 그대로 · 무게중심 둘레 바깥쪽이라 각운동량 그대로) — 늘어난 운동 에너지가 흡수 에너지의 3할 (3m/s 상한)
        if (made.Count >= 2)
        {
            double mt = 0, bx = 0, by = 0;
            foreach (var q in made) { mt += q.Kg; bx += q.Kg * q.X; by += q.Kg * q.Y; }
            bx /= mt; by /= mt;
            var ax = new double[made.Count]; var ay = new double[made.Count];
            double ux = 0, uy = 0;
            for (int k = 0; k < made.Count; k++)
            {
                double dx = made[k].X - bx, dy = made[k].Y - by, l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9) { double a = DotHash(made[k].Id, 31) / 4294967296.0 * 2 * Math.PI; dx = Math.Cos(a); dy = Math.Sin(a); l = 1; }
                ax[k] = dx / l; ay[k] = dy / l; ux += made[k].Kg * ax[k]; uy += made[k].Kg * ay[k];
            }
            ux /= mt; uy /= mt;
            double B = 0, Q = 0, amax = 0;
            for (int k = 0; k < made.Count; k++)
            {
                ax[k] -= ux; ay[k] -= uy;
                B += made[k].Kg * (made[k].Vx * ax[k] + made[k].Vy * ay[k]); Q += 0.5 * made[k].Kg * (ax[k] * ax[k] + ay[k] * ay[k]);
                amax = Math.Max(amax, Math.Sqrt(ax[k] * ax[k] + ay[k] * ay[k]));
            }
            double add = ScatterShare * hit.E;
            if (Q > 0 && add > 0)
            {
                double c = (-B + Math.Sqrt(Math.Max(0, B * B + 4 * Q * add))) / (2 * Q);
                if (amax > 0) c = Math.Min(c, ScatterMaxV / amax);
                for (int k = 0; k < made.Count; k++) { var q = made[k]; q.Vx += c * ax[k]; q.Vy += c * ay[k]; made[k] = q; }
            }
        }
        // 장부: 틱 끝 부모 몫 − 조각 몫 (가루 · 맞은 뒤 지난 몫) → 몸이 없는 배 · 몸이면 그 몸이 되받는다
        int body = world && Bodies.Count > 0 ? BodyOf(CellOf(p0)) : -1;
        double opx = 0, opy = 0, ol = 0;
        if (body >= 0 && body < Bodies.Count && Bodies[body].Mass > 0)
        {
            var o = Bodies[body];
            (opx, opy, ol) = WorldMomentum(p0, o.X, o.Y, o.Th, o.Vx, o.Vy, o.W, o.Lx, o.Ly);
        }
        else
        {
            var (m0, I0) = MassOf(p0);
            opx = m0 * p0.Vx; opy = m0 * p0.Vy; ol = m0 * (p0.X * p0.Vy - p0.Y * p0.Vx) + I0 * p0.W;
            foreach (var q in made) { opx -= q.Kg * q.Vx; opy -= q.Kg * q.Vy; ol -= q.Kg * (q.X * q.Vy - q.Y * q.Vx) + q.Inertia * q.W; }
            ToHostPx += opx; ToHostPy += opy; ToHostL += ol;
            body = -1;
        }
        // 목록: 부모를 빼고 조각을 끝에 (Id 차례 그대로)
        Track?.Gone.Add(p0);
        if (Track != null) CutTrack(p0.Id, hit, made);
        Props.RemoveAt(i);
        Props.AddRange(made);
        if (p0.Kind is 1 or 3) _clutterDirty = true;   // 깨진 가구 · 문짝이 차지한 칸 (길 값)
        if (body >= 0)
        {
            var o = Bodies[body];
            double npx = 0, npy = 0, nl = 0;
            foreach (var q in made) { var (a, b2, c2) = WorldMomentum(q, o.X, o.Y, o.Th, o.Vx, o.Vy, o.W, o.Lx, o.Ly); npx += a; npy += b2; nl += c2; }
            TakeBack(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props), body, npx - opx, npy - opy, nl - ol, false);
        }
        // 담긴 물은 그 칸 바닥으로 (고인 물의 원인 = 깨짐 — 5단 C8)
        if (p0.Liquid > 0 && hitCell >= 0 && F.Severed[hitCell] == 0) F.AddStore(hitCell, T.sWater, p0.Liquid, cause);
        if (p0.Kind == 0) { sim.Book.AddItem(p0.What, Flow.Broken, -1); BrokenKg += cur.Mass; }
        GritKg += gritKg;
        Shattered++;
        byte sub = sim.Facts.SubNow;
        sim.Facts.SubNow = hit.Sub;
        sim.Facts.Emit(sim.Tick, FactKind.Shattered, hitCell, (float)cur.Mass, cause, -1, p0.Id);
        sim.Facts.SubNow = sub;

        bool mat4(DotForm f) { var md = T.Materials[Math.Min(BaseMatOf(f), T.Materials.Count - 1)].Def; return md.Id is "glass" or "ceramic"; }
    }

    /// <summary>칸 (도면 m 점 — 도면 밖이면 −1).</summary>
    private int CellAt(double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        return fx >= 0 && fy >= 0 && fx < L.W && fy < L.H ? L.Index((int)fx, (int)fy) : -1;
    }

    /// <summary>틱 안 기록: 부모는 맞은 그 때까지 (사라짐 열쇠틀) · 조각은 그 때부터 틱 끝까지 그 자리 (다음 틱부터 흩어진다).</summary>
    private void CutTrack(int id, in BreakHit hit, List<Prop> made)
    {
        var tr = Track!;
        var a = hit.After;
        double t = hit.T;
        tr.Keys.RemoveAll(k => k.Who == MotionTrack.WhoProp && k.Id == id && k.T > t + 1e-9);
        tr.Add(MotionTrack.WhoProp, id, hit.T, a.X, a.Y, a.Th, 0, 0, 0, MotionTrack.EvGone, a.Z, 0);
        foreach (var q in made)
        {
            tr.Add(MotionTrack.WhoProp, q.Id, hit.T, q.X, q.Y, q.Th, 0, 0, 0, MotionTrack.EvNone, q.Z, 0);
            tr.Add(MotionTrack.WhoProp, q.Id, tr.Dt, q.X, q.Y, q.Th, 0, 0, 0, MotionTrack.EvNone, q.Z, 0);
        }
    }

    /// <summary>
    /// 물체 소리를 낸다 (틱 끝): 방마다 · 갈래마다 가장 큰 것 하나 (그 칸 · 그 때 · 재질 · 크기) — 55 + 10 log₁₀E dB (깨짐 +6).
    /// 소리 한 번이 배 안 길 찾기라 모아서 낸다.
    /// </summary>
    private void EmitNoises(Sim sim)
    {
        var snd = sim.Find<Sound>();
        if (snd != null && _noises.Count > 0)
        {
            _noises.Sort((a, b) => a.Room != b.Room ? a.Room.CompareTo(b.Room) : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : b.E != a.E ? b.E.CompareTo(a.E) : a.Seq.CompareTo(b.Seq));
            byte sub = sim.Facts.SubNow;
            for (int k = 0; k < _noises.Count; k++)
            {
                var z = _noises[k];
                if (k > 0 && _noises[k - 1].Room == z.Room && _noises[k - 1].Kind == z.Kind) continue;
                float db = (float)(55 + 10 * Math.Log10(z.E) + (z.Kind == SoundKind.Shatter ? 6 : 0));
                sim.Facts.SubNow = z.Sub;
                snd.Emit(sim, z.Cell, db, z.Kind, z.Cause, -1, -1, z.Mat, z.Size);
            }
            sim.Facts.SubNow = sub;
        }
        _noises.Clear();
    }
}
