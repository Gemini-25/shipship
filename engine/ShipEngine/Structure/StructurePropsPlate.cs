namespace ShipEngine;

/// <summary>
/// ④ 판 조각 (docs/PHYSICS.md §5-4 · PH15): 벽 · 외판 잔칸이 떨어져 나오면 (구멍이 파이고 · 부딪혀 으스러지고 · 갈라진 자리가 찢기고 · 덩어리가 떨어지면) 점 알갱이가 아니라
/// 강철 판 물체 (Kind 4 — 0.25 × 0.5m · 무게는 떨어진 잔칸 몫)가 된다 — 세상 틀로 날다가 (PH11) 몸의 벽 · 외판에 닿으면 튀고 (반발 0.3 · 세게 맞으면 ④ 충격 길 — 금 · 구멍) ·
/// 몸의 트인 칸에 들면 실려 미끄러지고 쌓인다. 붙어 사라지지 않는다. 배 안 판 조각이 600을 넘으면 잠든 것부터 (오래된 차례) 가루로 합친다 · 그래도 넘으면 가장 오래된 나는 것을 장부 '우주로'.
/// 떨어져 나온 몸과는 나온 그 틱에 닿지 않는다 (제 구멍 칸을 빠져나가게).
/// </summary>
public sealed partial class Structure
{
    /// <summary>판 조각이 몸에 맞아 금 · 구멍을 낼 에너지 (J — 예전 알갱이와 같다).</summary>
    public const double PlateHitJ = 100;
    /// <summary>셈 (찍지 않는다 — 보기 · 법칙 장부): 만든 판 조각 질량 · 우주로 간 · 가루로 합친 · 몸에 세게 맞은 수.</summary>
    public double PlateKgMade, PlateKgLost, PlateKgDust;
    public long PlatesMade, PlateHits;

    private DotForm? _plateForm;
    /// <summary>판 조각의 꼴 (강철 판 0.25 × 0.5m · 한 층).</summary>
    private DotForm PlateForm() => _plateForm ??= DotForge.Make(T, "판 조각");

    /// <summary>
    /// 판 조각 하나를 세상 틀로 날린다 (Shed · ShedLump): 자리 · 빠르기 (세상) · 질량 · 제 각운동량 (몸과 함께 돌던 몫) · 원인 · 떨어져 나온 몸 · 난 때 (틱 안 0~255).
    /// 몸이 없는 배면 배의 몸을 세운다 (세상 틀 = 도면 틀).
    /// </summary>
    private void MakePlate(Sim sim, double x, double y, double vx, double vy, double kg, double spin, int cause, int from, byte sub)
    {
        if (kg <= 0) return;
        EnsureShip();
        var fm = PlateForm();
        double inertia = fm.I * kg / Math.Max(1e-9, fm.Mass);
        var p = new Prop
        {
            Id = NextPropId++, Kind = 4, What = -1, X = x, Y = y, Vx = vx, Vy = vy, W = inertia > 0 ? spin / inertia : 0,
            State = 5, Host = -1, Cause = cause, Since = _now, Spare0 = from + 1, Spare1 = sub,
            Form = fm.Id, Kg = (float)kg, Inertia = (float)inertia, Mat = BaseMatOf(fm),
        };
        Props.Add(p);
        PlateKgMade += kg; PlatesMade++;
    }

    /// <summary>요청 (시험 · 다른 배의 잔해): 세상 자리 (x, y)에서 질량 kg의 판 조각을 (vx, vy)로 던진다 · Id를 돌려준다.</summary>
    public int ThrowPlate(Sim sim, double x, double y, double vx, double vy, double kg, int cause)
    {
        MakePlate(sim, x, y, vx, vy, kg, 0, cause, -1, sim.Facts.SubNow);
        return NextPropId - 1;
    }

    /// <summary>나온 그 틱에는 떨어져 나온 몸에 닿지 않는다 (판 조각 — 제 구멍 칸을 빠져나가게).</summary>
    private bool PlateGrace(in Prop p, int body) => p.Kind == 4 && p.Spare0 - 1 == body && p.Since == _now;

    /// <summary>
    /// 배 안 판 조각이 600을 넘으면: 잠든 것 (놓임)부터 오래된 차례로 가루로 (그 칸 바닥 · 지닌 운동량은 그 몸이 받는다) · 그래도 넘으면 가장 오래된 나는 것을 장부 '우주로'.
    /// </summary>
    private void PlateCap(bool world)
    {
        int n = 0;
        foreach (ref readonly var p in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props)) if (p.Kind == 4) n++;
        if (n <= ShardMax) return;
        int over = n - ShardMax, nb = Bodies.Count;
        Span<double> back = nb <= 16 ? stackalloc double[3 * nb] : new double[3 * nb];
        for (int pass = 0; pass < 2 && over > 0; pass++)
            for (int i = 0; i < Props.Count && over > 0; i++)   // Id 차례 = 오래된 차례
            {
                var p = Props[i];
                if (p.Kind != 4 || (pass == 0 ? p.State != 0 : !Flying(p))) continue;
                var (m, inertia) = MassOf(p);
                var (x, y, vx, vy, w) = PropWorld(p);
                if (pass == 0)
                {
                    int c = CellOf(p), b = world && c >= 0 ? BodyOf(c) : -1;
                    if (b >= 0 && b < nb && Bodies[b].Mass > 0) { back[3 * b] += m * vx; back[3 * b + 1] += m * vy; back[3 * b + 2] += m * (x * vy - y * vx) + inertia * w; }
                    else { ToHostPx += m * vx; ToHostPy += m * vy; ToHostL += m * (x * vy - y * vx) + inertia * w; }
                    if (c >= 0) { Grit[c] += (float)m; GritMat[c] = p.Mat; }
                    GritKg += m; PlateKgDust += m;
                }
                else { PropPxGone += m * vx; PropPyGone += m * vy; PropLGone += m * (x * vy - y * vx) + inertia * w; PlateKgLost += m; PropSpaced++; }
                Props.RemoveAt(i--);
                over--;
                _clutterDirty = true;
            }
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        for (int b = 0; b < nb; b++)
            if (back[3 * b] != 0 || back[3 * b + 1] != 0 || back[3 * b + 2] != 0) TakeBack(all, b, -back[3 * b], -back[3 * b + 1], -back[3 * b + 2], false);
    }

    /// <summary>판 조각 질량 (배 안 · 나는 것 — 법칙 장부).</summary>
    public double PlateKgNow { get { double m = 0; foreach (var p in Props) if (p.Kind == 4) m += p.Kg; return m; } }

    /// <summary>나는 판 조각을 알갱이 꼴로 (그림 · 시험이 알갱이처럼 읽는다 — 틱 사이 자리 · 난 때 · 제 돎).</summary>
    public static Shard AsShard(in Prop p) => new()
    {
        X = p.X, Y = p.Y, Vx = p.Vx, Vy = p.Vy, Kg = p.Kg, Cause = p.Cause, From = p.Spare0 - 1, Born = p.Since, Spin = p.Inertia * p.W, Id = -p.Id, Sub = (byte)p.Spare1,
    };

    /// <summary>날고 있는 부스러기 — 바깥에서 던진 알갱이 + 나는 판 조각 (보기 · 시험).</summary>
    public List<Shard> FlyingDebris()
    {
        var list = new List<Shard>(Shards);
        foreach (var p in Props) if (p.Kind == 4 && Flying(p)) list.Add(AsShard(p));
        return list;
    }
}
