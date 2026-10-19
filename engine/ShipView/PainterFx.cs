using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 사고 효과 (그래픽 결정 11 · 세기는 적당히 — 결정 12) — 화면만의 가짜 사건은 없다: 모두 월드 엔진의 값 · 사실을 읽어 그린다.
/// <list type="bullet">
/// <item>새는 공기 줄기: ③ 칸 바람(WindX · WindY)이 <see cref="JetMin"/>을 넘는 칸만 — 빠를수록 줄기가 길고 많다.</item>
/// <item>천장 연기층: ③ 국소 현상의 층 — 윗층이 아랫층보다 짙은 만큼 엷은 막(천장 아래 깔린 연기) · 아랫층은 뭉게 연기 (기어가는 높이).</item>
/// <item>합선 불꽃: ⑥ 사실 '합선' 2틱 안 · 차단기가 눌어붙어 아크가 이어지는 칸 — 튀는 불티 · 둘레를 푸르게 비춘다.</item>
/// <item>폭발 충격파: ④ 폭발이 내는 큰 소리 사실 — 반지름은 그 무게(TNT 환산)의 폭풍이 15kPa 아래로 줄 때까지 · 첫 반 틱은 섬광.</item>
/// <item>방사선 화면 잡음: ⑤ 칸 선량률 1mGy/시가 넘으면 — 많을수록 짙은 하얀 점 (화면에 방사선이 맞는 것).</item>
/// </list>
/// 화면 흔들림은 관전 화면(<see cref="LiveShow"/>)이 같은 사실을 읽어 잘라 낼 곳을 흔든다.
/// </summary>
public sealed partial class Painter
{
    /// <summary>틱 사이 진행 (0~1 — 관전 화면이 넣는다 · 충격파가 퍼지는 몫).</summary>
    public double Sub;
    public const float JetMin = 4;          // m/s — 이보다 느린 바람은 보이지 않는다
    public const float RadNoiseMin = 1;     // mGy/시
    public const int FxTicks = 3;           // 이만큼 지난 사실까지 그린다

    private readonly List<Fact> _recent = new();

    /// <summary>폭발의 큰 소리 사실 (소리 엔진의 큰 소리는 소리 종류를 Other에 적는다 — 폭발은 -1).</summary>
    public static bool IsBlast(in Fact f) => f.Kind == FactKind.LoudSound && f.Other == -1;
    /// <summary>폭발 큰 소리(dB) → TNT 환산 무게 (kg) — ④ 폭발이 적은 식을 거꾸로 (120 + 20·log₁₀W).</summary>
    public static double BlastKg(float db) => Math.Pow(10, (db - 120) / 20.0);
    /// <summary>충격파가 닿는 칸 수: 환산 거리 9에서 15kPa 아래 (④ 폭발과 같은 식).</summary>
    public static double BlastReach(float db) => 9 * Math.Cbrt(BlastKg(db));

    private void GatherFacts(Sim s)
    {
        _recent.Clear();
        foreach (var fc in s.Facts.Recent(512))
            if (s.Tick - fc.Tick <= FxTicks && fc.Kind is FactKind.Short or FactKind.LoudSound) _recent.Add(fc);
    }

    private double Age(Sim s, in Fact f) => s.Tick - f.Tick + Sub;

    // ── 빛 지도에 더하는 것 (곱하기 전) ──
    private void FxLight(Sim s, Network? net, int frame)
    {
        foreach (var fc in _recent)
        {
            if (fc.Cell < 0) continue;
            double age = Age(s, fc);
            if (fc.Kind == FactKind.Short && age < 2)
                SparkLight(fc.Cell, frame, (float)(1 - age / 2));
            else if (IsBlast(fc) && age < 0.6)
            {
                // 섬광: 처음 반 틱 동안 폭풍이 닿는 데까지 밝게
                float k = (float)(1 - age / 0.6) * 1.6f;
                int reach = (int)Math.Ceiling(BlastReach(fc.Size) * 0.6);
                int cx = L.X(fc.Cell), cy = L.Y(fc.Cell);
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        if (!L.In(cx + dx, cy + dy)) continue;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d > reach) continue;
                        AddLight(L.Index(cx + dx, cy + dy), (1f, 0.82f, 0.55f), k / (1 + (float)(d * d) * 0.15f));
                    }
            }
        }
        if (net != null)
            for (int fd = 0; fd < net.Feeders.Count; fd++)
                if (net.IsStuckFault(fd)) SparkLight(net.FaultCell[fd], frame, 0.8f);   // 눌어붙은 차단기 — 아크가 이어진다
    }

    private void SparkLight(int c, int frame, float k)
    {
        float fl = DotHash.F(c, frame) < 0.35f ? 0.3f : 1f;   // 아크는 깜빡인다
        AddLight(c, (0.75f, 0.85f, 1f), 0.9f * k * fl);
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        foreach (int j in nb) if (j >= 0) AddLight(j, (0.75f, 0.85f, 1f), 0.35f * k * fl);
    }

    // ── 곱하기 전에 그리는 것 (빛을 받는다): 새는 공기 줄기 ──
    private void AirJets(Img img, Field f, int x0, int y0, int w, int h, int frame)
    {
        var col = Rgba.Hex("#e6f6ff");
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (!Mine(c)) continue;
                float vx = f.WindX[c], vy = f.WindY[c];
                float v = MathF.Sqrt(vx * vx + vy * vy);
                if (v < JetMin) continue;
                float ux = vx / v, uy = vy / v;
                int n = Math.Min(10, 2 + (int)(v / 5));
                int len = Math.Min(20, 5 + (int)(v / 3));
                float alpha = 0.4f + 0.5f * Math.Min(1, v / 30);
                float run = frame * Math.Min(T, 3 + v / 3);   // 줄기는 바람 쪽으로 흐른다
                for (int k = 0; k < n; k++)
                {
                    uint u = DotHash.U(c, k, 13);
                    float sx = u % T, sy = (u >> 8) % T, ph = ((u >> 16) % T + run) % T;
                    float bx = (x - x0) * T + (sx + ux * ph + T) % T, by = (y - y0) * T + (sy + uy * ph + T) % T;
                    for (int i = 0; i < len; i++)
                        img.Blend((int)(bx - ux * i), (int)(by - uy * i), col, alpha * (1 - i / (float)len));
                    img.Blend((int)bx, (int)by, Rgba.Hex("#ffffff"), alpha);   // 앞머리
                }
            }
    }

    // ── 천장 연기층: 윗층이 아랫층보다 짙은 만큼 엷은 막 (곱하기 전 — 빛을 받는다) ──
    private void SmokeVeil(Img img, Field f, int x0, int y0, int w, int h, int frame)
    {
        if (f.Strat.Length != L.N) return;
        var col = Rgba.Hex("#4a4744");
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (!Mine(c) || L.Kind[c] is not (CellKind.Floor or CellKind.Door) || f.Strat[c] < 0.1f) continue;
                double hi = f.HeadPpm(f.T.gSoot, c, false), lo = f.HeadPpm(f.T.gSoot, c, true);
                double extra = hi - lo;
                if (extra < 20) continue;
                float a = (float)Math.Min(0.55, extra / 900);
                // 막은 고르지 않다 — 4×4 덩이마다 조금씩 · 천천히 흐른다
                int drift = frame / 3;
                for (int by = 0; by < T; by += 4)
                    for (int bx = 0; bx < T; bx += 4)
                    {
                        float k = 0.75f + 0.5f * DotHash.F(c * 31 + (bx + drift * 4) % T, by, 7);
                        img.Fill((x - x0) * T + bx, (y - y0) * T + by, 4, 4, col, Math.Min(0.8f, a * k));
                    }
            }
    }

    /// <summary>뭉게 연기의 짙기: 층이 갈렸으면 아랫층(기어가는 높이) · 섞였으면 칸 전체.</summary>
    private static double PuffPpm(Field f, int c) => f.Strat.Length > c && f.Strat[c] >= 0.1f ? f.HeadPpm(f.T.gSoot, c, true) : f.Ppm(f.T.gSoot, c);

    // ── 곱한 뒤에 그리는 것 (스스로 빛나거나 화면에 맞는 것) ──
    private void FxEmissive(Img img, Sim s, Network? net, int x0, int y0, int w, int h, int frame)
    {
        foreach (var fc in _recent)
        {
            if (fc.Cell < 0 || !Mine(fc.Cell)) continue;
            double age = Age(s, fc);
            if (fc.Kind == FactKind.Short && age < 2) Sparks(img, fc.Cell, x0, y0, frame, (float)(1 - age / 2));
            else if (IsBlast(fc) && age < 1.2) Shockwave(img, fc, x0, y0, age);
        }
        if (net != null)
            for (int fd = 0; fd < net.Feeders.Count; fd++)
                if (net.IsStuckFault(fd)) Sparks(img, net.FaultCell[fd], x0, y0, frame, 0.7f);
        RadiationNoise(img, s, x0, y0, w, h, frame);
    }

    private void Sparks(Img img, int c, int x0, int y0, int frame, float k)
    {
        int cx = (L.X(c) - x0) * T + T / 2, cy = (L.Y(c) - y0) * T + T / 2;
        if (cx < -T || cy < -T || cx > img.W + T || cy > img.H + T) return;
        var hot = Rgba.Hex("#fff6c8"); var arc = Rgba.Hex("#9fd4ff");
        int n = 4 + (int)(10 * k);
        for (int i = 0; i < n; i++)
        {
            uint u = DotHash.U(c, frame, i);
            double a = (u % 628) / 100.0, r = 3 + (u >> 10) % 12 * k;
            int sx = cx + (int)(Math.Cos(a) * r), sy = cy + (int)(Math.Sin(a) * r * 0.8) + (int)((u >> 20) % 4);   // 불티는 조금 떨어진다
            img.Blend(sx, sy, hot, 0.95f);
            img.Blend(sx - (int)Math.Cos(a), sy - (int)Math.Sin(a), Ramps.Get(Ramps.Fire, 5), 0.7f * k);
        }
        if (DotHash.F(c, frame) >= 0.35f) { img.Fill(cx - 1, cy - 1, 3, 3, arc, 0.9f * k); img.Glow(cx, cy, 8, arc, 0.25f * k); }
    }

    private void Shockwave(Img img, in Fact fc, int x0, int y0, double age)
    {
        double reach = BlastReach(fc.Size) * T;
        double r = reach * Math.Min(1, age / 0.8);
        float fade = (float)Math.Max(0, 1 - age / 1.2);
        int cx = (L.X(fc.Cell) - x0) * T + T / 2, cy = (L.Y(fc.Cell) - y0) * T + T / 2;
        // 공기가 눌린 앞면은 밝게 · 바로 뒤 엷어진 데는 어둡게 (빛이 꺾여 보이는 띠)
        var ring = Rgba.Hex("#fffaf0"); var trough = Rgba.Hex("#05070c");
        int steps = Math.Max(32, (int)(2 * Math.PI * r) + 8);   // 둘레만큼 — 띠가 끊기지 않게
        for (int i = 0; i < steps; i++)
        {
            double a = i * 2 * Math.PI / steps, cs = Math.Cos(a), sn = Math.Sin(a);
            for (int t = 0; t < 2; t++) img.Blend(cx + (int)(cs * (r - t)), cy + (int)(sn * (r - t)), ring, 0.7f * fade);
            for (int t = 2; t < 6; t++) img.Blend(cx + (int)(cs * (r - t)), cy + (int)(sn * (r - t)), trough, 0.22f * fade * (1 - (t - 2) / 4f));
        }
        if (age < 0.5) img.Glow(cx, cy, (float)(T * 1.5 + reach * 0.15), Rgba.Hex("#fff3d6"), (float)(0.6 * (1 - age / 0.5)));   // 섬광
    }

    private void RadiationNoise(Img img, Sim s, int x0, int y0, int w, int h, int frame)
    {
        var o = s.Find<Outside>();
        if (o == null || o.Dose.Length != L.N) return;
        var dot = Rgba.Hex("#eef4ff");
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                float dose = o.Dose[c];
                if (dose < RadNoiseMin) continue;
                int n = Math.Min(16, 2 + (int)(4 * Math.Log10(dose)));   // 1mGy/시 2점 · 10에 6점 · 100에 10점 (장마다 다른 자리)
                for (int k = 0; k < n; k++)
                {
                    uint u = DotHash.U(c, frame, k + 101);
                    int px = (x - x0) * T + (int)(u >> 4) % T, py = (y - y0) * T + (int)(u >> 12) % T;
                    img.Blend(px, py, dot, 0.9f);
                    if (u % 5 == 0) img.Blend(px + 1, py + 1, dot, 0.6f);   // 비스듬히 지나간 자국
                }
            }
    }
}
