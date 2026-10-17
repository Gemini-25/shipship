namespace ShipEngine;

public sealed partial class Field
{
    /// <summary>방 공기의 섞임 (m²/s) — 사람 · 환기 · 대류가 만드는 섞임. 뜨거운 곳은 부력으로 더 빨리 섞인다.</summary>
    public const double MixD = 0.03;
    private double[] _n2 = Array.Empty<double>(), _u2 = Array.Empty<double>(), _t2 = Array.Empty<double>(), _st2 = Array.Empty<double>(), _nt = Array.Empty<double>(), _tc = Array.Empty<double>();

    /// <summary>확산: 같은 구역 이웃끼리 같은 몰 수를 주고받는다 → 성분 · 열이 섞이고 압력은 그대로. 두 버퍼 · 줄 덩어리 병렬.</summary>
    private void Diffuse(double dt)
    {
        if (_n2.Length != N.Length) { _n2 = new double[N.Length]; _u2 = new double[Cells]; _nt = new double[Cells]; _tc = new double[Cells]; }
        // 칸마다 몰 수 · 온도를 한 번만
        Par.Rows(L.H, (y0, y1) =>
        {
            for (int i = y0 * L.W; i < y1 * L.W; i++)
            {
                double n = 0, c = 0;
                int b = i * G;
                for (int g = 0; g < G; g++) { double v = N[b + g]; n += v; c += v * _cv[g]; }
                _nt[i] = n; _tc[i] = c > 1e-9 ? U[i] / c : Ts[i];
            }
        });
        Par.Rows(L.H, (y0, y1) =>
        {
            Span<int> nb = stackalloc int[4];
            Span<double> flux = stackalloc double[G];
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < L.W; x++)
                {
                    int i = y * L.W + x, bi = i * G;
                    int r = RegionOf[i];
                    for (int g = 0; g < G; g++) _n2[bi + g] = N[bi + g];
                    _u2[i] = U[i];
                    if (r < 0) continue;
                    double ni = _nt[i];
                    if (ni <= 1e-12) continue;
                    double ti = _tc[i];
                    L.Neighbors(i, nb);
                    flux.Clear();
                    double fu = 0;
                    foreach (int j in nb)
                    {
                        if (j < 0 || RegionOf[j] != r) continue;
                        double nj = _nt[j];
                        if (nj <= 1e-12) continue;
                        double tm = Math.Max(ti, _tc[j]);
                        double d = MixD * (1 + Math.Max(0, tm - 300) / 100);
                        double m = Math.Min(0.2, d * dt) * Math.Min(ni, nj);
                        int bj = j * G;
                        double a = m / nj, bb = m / ni;
                        for (int g = 0; g < G; g++) flux[g] += a * N[bj + g] - bb * N[bi + g];
                        fu += a * U[j] - bb * U[i];
                    }
                    for (int g = 0; g < G; g++) _n2[bi + g] = Math.Max(0, N[bi + g] + flux[g]);
                    _u2[i] = Math.Max(0, U[i] + fu);
                }
        });
        (N, _n2) = (_n2, N);
        (U, _u2) = (_u2, U);
    }

    /// <summary>
    /// 열: ① 기체 ↔ 자기 바닥 (정확한 두 물체 식 — 진공 가까이서도 넘치지 않는다)
    /// ② 고체 전도 (바닥판 · 벽 · 바닥↔벽은 공기 대류 — 진공이면 줄어든다)
    /// ③ 외판: 우주로 복사 · 햇빛 · (행성) 바깥 공기와 대류.
    /// </summary>
    private void Heat(double dt)
    {
        // ① 기체 ↔ 바닥
        Par.Rows(L.H, (y0, y1) =>
        {
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < L.W; x++)
                {
                    int i = y * L.W + x;
                    if (RegionOf[i] < 0 || Cs[i] <= 0) continue;
                    double cg = GasC(i);
                    if (cg <= 1e-6) continue;
                    double tg = U[i] / cg, ts = Ts[i];
                    double hEff = 5 + 4 * 0.3 * Sigma * tg * tg * tg; // 대류 + 뜨거운(그을린) 기체의 복사
                    double gk = hEff * 2.0 * dt * (1 / cg + 1 / Cs[i]);  // 바닥 + 천장 2m²
                    double teq = (cg * tg + Cs[i] * ts) / (cg + Cs[i]);
                    double e = Math.Exp(-gk);
                    U[i] = cg * (teq + (tg - teq) * e);
                    Ts[i] = teq + (ts - teq) * e;
                }
        });
        // ①' 겉면 ↔ 기체 · 겉면 ↔ 바닥 (바닥 · 문 칸)
        Par.Rows(L.H, (y0, y1) =>
        {
            for (int i = y0 * L.W; i < y1 * L.W; i++)
                if (_surfTick.Length != Cells || _surfTick[i] <= _tickNow - SlowEvery) SurfaceExchange(i, dt); // 매 틱 다룬 겉면은 빼고
        });
        // ② 고체 전도 (두 버퍼)
        if (_t2.Length != Cells) _t2 = new double[Cells];
        Par.Rows(L.H, (y0, y1) =>
        {
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < L.W; x++)
                {
                    int i = y * L.W + x;
                    if (Cs[i] <= 0) { _t2[i] = Ts[i]; continue; }
                    double q = 0;
                    if (x + 1 < L.W) q += Kc(i, i + 1, _kRight[i]) * (Ts[i + 1] - Ts[i]);
                    if (x > 0) q += Kc(i - 1, i, _kRight[i - 1]) * (Ts[i - 1] - Ts[i]);
                    if (y + 1 < L.H) q += Kc(i, i + L.W, _kDown[i]) * (Ts[i + L.W] - Ts[i]);
                    if (y > 0) q += Kc(i - L.W, i, _kDown[i - L.W]) * (Ts[i - L.W] - Ts[i]);
                    _t2[i] = Ts[i] + q * dt / Cs[i];
                }
        });
        (Ts, _t2) = (_t2, Ts);
        // ③ 외판: 다층 단열재 겉면(흡수 0.3 · 방사 0.8)이 햇빛 · 우주와 평형 → 단열재(0.1 W/m²K)를 지나는 열만 벽으로.
        //    구멍 난 외판은 단열재가 찢겨 재질 그대로 복사 · 흡수한다. 행성 대기 속에서는 바깥 공기와 대류도.
        if (Adiabatic) return;
        double bg = Outside.Vacuum ? 2.7 : Outside.Temp;
        double bg4 = bg * bg * bg * bg;
        Par.Rows(L.H, (y0, y1) =>
        {
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < L.W; x++)
                {
                    int i = y * L.W + x;
                    if (_exposed[i] == 0 || Cs[i] <= 0) continue;
                    var m = T.Materials[L.Material[i]].Def;
                    bool damaged = Hole[i] > 0;
                    double t = Ts[i], q = 0;
                    foreach (var (dx, dy) in Dirs)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (L.In(nx, ny) && L.Kind[L.Index(nx, ny)] != CellKind.Outside) continue;
                        double cos = Math.Max(0, dx * Outside.SunX + dy * Outside.SunY);
                        if (damaged)
                            q += m.Absorb * Outside.Sun * WallFace * cos - m.Emiss * Sigma * WallFace * (t * t * t * t - bg4);
                        else
                        {
                            double to = Math.Pow((0.3 * Outside.Sun * cos + 0.8 * Sigma * bg4) / (0.8 * Sigma), 0.25); // 단열재 겉면 온도
                            q += 0.1 * WallFace * (to - t);
                        }
                    }
                    if (!Outside.Vacuum) q += 10 * WallFace * _exposed[i] * (Outside.Temp - t) * Math.Min(1, Outside.KPa / 100);
                    Ts[i] = Math.Max(2.7, t + q * dt / Cs[i]);
                }
        });
    }

    /// <summary>
    /// 겉면 ↔ 기체(대류 10 W/m²K · 1m²) · 겉면 ↔ 바닥(전도 20 W/K + 복사 εσ(T⁴−Ts⁴) 선형화) — 두 물체 정확한 식을 차례로.
    /// 장은 바닥과 2K 넘게 다른 겉면을 매 틱, 나머지는 5틱마다 부른다.
    /// </summary>
    public void SurfaceExchange(int i, double dt)
    {
        if (!HasSurface(i)) return;
        double cf = SurfaceC;
        if (RegionOf.Length > i && RegionOf[i] >= 0)
        {
            double cg = GasC(i);
            if (cg > 1e-6)
            {
                double tg = U[i] / cg, tf = Tf[i];
                double gk = 10 * 1.0 * dt * (1 / cg + 1 / cf);
                double teq = (cg * tg + cf * tf) / (cg + cf), e = Math.Exp(-gk);
                U[i] = cg * (teq + (tg - teq) * e);
                Tf[i] = teq + (tf - teq) * e;
            }
        }
        if (Cs[i] > 0)
        {
            double tf = Tf[i], ts = Ts[i], tm = 0.5 * (tf + ts);
            double gsf = 20 + 4 * 0.9 * Sigma * tm * tm * tm;
            double gk = gsf * dt * (1 / cf + 1 / Cs[i]);
            double teq = (cf * tf + Cs[i] * ts) / (cf + Cs[i]), e = Math.Exp(-gk);
            Tf[i] = teq + (tf - teq) * e;
            Ts[i] = teq + (ts - teq) * e;
        }
    }

    /// <summary>바닥 ↔ 벽 대류는 공기가 있을 때만 (진공이면 복사만 — 10%).</summary>
    private double Kc(int a, int b, double k)
    {
        if (k <= 0) return 0;
        bool fa = L.Kind[a] != CellKind.Wall || Hole[a] >= 2f, fb = L.Kind[b] != CellKind.Wall || Hole[b] >= 2f;
        if (fa == fb) return k;
        int gasCell = fa ? a : b;
        double p = Pressure(gasCell);
        return k * Math.Clamp(p / 101.3, 0.1, 1.5);
    }

    /// <summary>바깥에 드러난 면 가운데 해를 보는 정도의 합 (cos).</summary>
    private double SunFacing(int x, int y)
    {
        double s = 0;
        foreach (var (dx, dy) in Dirs)
        {
            int nx = x + dx, ny = y + dy;
            bool outside = !L.In(nx, ny) || L.Kind[L.Index(nx, ny)] == CellKind.Outside;
            if (!outside) continue;
            s += Math.Max(0, dx * Outside.SunX + dy * Outside.SunY);
        }
        return s;
    }

    private static readonly (int, int)[] Dirs = { (-1, 0), (1, 0), (0, -1), (0, 1) };

    /// <summary>
    /// 상 변화 · 액체: 응결(차가운 바닥에 이슬) · 증발 · 끓음 → 고인 액체가 바닥을 따라 퍼짐 → 그을음이 가라앉음.
    /// 에너지 장부: 증발열과 현열 차이는 바닥(고체)이 주고받는다.
    /// </summary>
    private void PhaseChange(double dt)
    {
        foreach (var gas in T.Gases)
        {
            if (gas.CondensesTo is not Substance liq || liq.Store < 0) continue;
            int g = gas.Gas, s = liq.Store;
            double latent = (gas.Hf - liq.Hf) * 1000; // J/몰
            double boil = gas.Def.BoilK;
            Par.Rows(L.H, (y0, y1) =>
            {
                for (int y = y0; y < y1; y++)
                    for (int x = 0; x < L.W; x++)
                    {
                        int i = y * L.W + x;
                        if (RegionOf[i] < 0) continue;
                        double nTot = CellMoles(i), cg = GasC(i);
                        double tg = cg > 1e-9 ? U[i] / cg : Ts[i];
                        double pSat = Water.SatKPa(Ts[i], boil, latent);
                        double pv = nTot > 1e-12 ? Pressure(i) * N[i * G + g] / nTot : 0;
                        int k = i * G + g, ks = s * Cells + i;
                        if (pv > pSat * 1.001 && tg > 0)
                        {
                            // 응결: 넘치는 증기의 30%씩
                            double excess = (pv - pSat) * 1000 * CellVolume / (Field.R * tg);
                            double n = Math.Min(N[k], 0.3 * excess);
                            if (n <= 1e-12) continue;
                            N[k] -= n; U[i] = Math.Max(0, U[i] - n * _cv[g] * tg);
                            Store[ks] += n;
                            double q = n * (latent + _cv[g] * (tg - 298.15));
                            if (Cs[i] > 0) Ts[i] += q / Cs[i];
                        }
                        else if (Store[ks] > 0 && pv < pSat)
                        {
                            // 증발: 모자란 증기압에 비례 (1m² · 1e-3 몰/초/kPa) — 포화를 넘지 않게
                            double room = (pSat - pv) * 1000 * CellVolume / (Field.R * Math.Max(1, tg));
                            double n = Math.Min(Store[ks], Math.Min(room * 0.5, 1e-3 * (pSat - pv) * CellFloor * dt * (pSat > 101 ? 50 : 1)));
                            if (n <= 1e-12) continue;
                            double q = n * (latent + _cv[g] * (Ts[i] - 298.15));
                            if (Cs[i] > 0 && Ts[i] - q / Cs[i] < 150) continue; // 바닥이 너무 차면 멈춘다
                            Store[ks] -= n; N[k] += n; U[i] += n * _cv[g] * Ts[i];
                            if (Cs[i] > 0) Ts[i] -= q / Cs[i];
                        }
                    }
            });
        }
        SpreadLiquids(dt);
        SettleSoot(dt);
    }

    /// <summary>고인 액체는 바닥을 따라 낮은 쪽으로 퍼진다 (1mm ≈ 물 55몰보다 얇으면 멈춘다 — 표면 장력).</summary>
    private void SpreadLiquids(double dt)
    {
        if (_st2.Length != Cells) _st2 = new double[Cells];
        foreach (var liq in T.Stores)
        {
            if (liq.Phase != MatterPhase.Liquid) continue;
            int s = liq.Store;
            const double Film = 55;
            Par.Rows(L.H, (y0, y1) =>
            {
                Span<int> nb = stackalloc int[4];
                for (int y = y0; y < y1; y++)
                    for (int x = 0; x < L.W; x++)
                    {
                        int i = y * L.W + x, k = s * Cells + i;
                        double v = Store[k];
                        _st2[i] = v;
                        if (RegionOf[i] < 0 && L.Kind[i] != CellKind.Floor) continue;
                        L.Neighbors(i, nb);
                        double d = 0;
                        foreach (int j in nb)
                        {
                            if (j < 0 || !Wet(j)) continue;
                            double vj = Store[s * Cells + j];
                            if (v > vj && v > Film) d -= 0.2 * (v - Math.Max(vj, Film)) * 0.5;
                            if (vj > v && vj > Film) d += 0.2 * (vj - Math.Max(v, Film)) * 0.5;
                        }
                        _st2[i] = Math.Max(0, v + d);
                    }
            });
            for (int i = 0; i < Cells; i++) Store[s * Cells + i] = _st2[i];
        }
    }

    /// <summary>액체가 흘러갈 수 있는 칸: 바닥 · 열린 문.</summary>
    private bool Wet(int i) => L.Kind[i] == CellKind.Floor || L.Kind[i] == CellKind.Door && DoorPassable(L.DoorAt[i]);

    /// <summary>칸 하나의 바닥 + 천장 넓이 · 벽면 하나의 넓이 (m² — 칸 1m × 1m · 높이 2.5m).</summary>
    public const double FloorCeilM2 = 2, WallFaceM2 = 2.5;

    /// <summary>
    /// 그을음이 겉면에 내려앉는다 (두 시간에 1/e). 작은 입자는 무게보다 확산 · 난류로 모든 겉면에 붙는다 —
    /// 바닥 · 천장(2m²)과 곁의 벽면(면마다 2.5m²)에 넓이만큼 나눠 (불 곁의 벽이 검게 그을린다 · 벽 칸의 저장 = 그 벽에 묻은 그을음).
    /// </summary>
    private void SettleSoot(double dt)
    {
        int g = T.gSoot, s = T.sSoot;
        double f = 1 - Math.Exp(-dt / 7200);
        Span<int> nb = stackalloc int[4];
        for (int i = 0; i < Cells; i++)
        {
            int k = i * G + g;
            double v = N[k];
            if (v <= 1e-12) continue;
            double n = v * f, cg = GasC(i);
            double tg = cg > 1e-9 ? U[i] / cg : Ts[i];
            N[k] -= n; U[i] = Math.Max(0, U[i] - n * _cv[g] * tg);
            L.Neighbors(i, nb);
            int walls = 0;
            foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Wall) walls++;
            double onWall = walls * WallFaceM2 / (FloorCeilM2 + walls * WallFaceM2);
            Deposit(i, n * (1 - onWall), tg);
            if (walls > 0) foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Wall) Deposit(j, n * onWall / walls, tg);
        }

        void Deposit(int c, double m, double tg)
        {
            Store[s * Cells + c] += m;
            if (Cs[c] > 0) Ts[c] += m * _cv[g] * (tg - 298.15) / Cs[c];
        }
    }

    /// <summary>겉면에 묻은 그을음 (g/m²): 바닥 칸은 바닥 · 천장에 반씩 · 벽 칸은 벽면 하나 넓이로 (그림 · 냄새 · 닦기).</summary>
    public double SootGPerM2(int i) => Store[T.sSoot * Cells + i] * 12.0 / (L.Kind[i] == CellKind.Wall ? WallFaceM2 : FloorCeilM2);

    // ── 에너지 장부 (법칙 시험) ──

    /// <summary>
    /// 배 전체의 에너지 장부 (J): 기체 현열 + 기체 성분의 (hf − cv·298.15) + 바닥 저장의 hf + 고체 열 + 겉면 열.
    /// 단열된 닫힌 배에서는 흐름 · 확산 · 열 · 상 변화 · 반응으로 바뀌지 않는다.
    /// </summary>
    public double EnergyLedger()
    {
        double e = 0;
        for (int i = 0; i < Cells; i++) e += U[i] + Cs[i] * Ts[i] + (HasSurface(i) ? SurfaceC * Tf[i] : 0);
        for (int g = 0; g < G; g++)
        {
            double k = T.Gases[g].Hf * 1000 - _cv[g] * 298.15;
            for (int i = 0; i < Cells; i++) e += N[i * G + g] * k;
        }
        for (int s = 0; s < S; s++)
        {
            double h = T.Stores[s].Hf * 1000;
            for (int i = 0; i < Cells; i++) e += Store[s * Cells + i] * h;
        }
        return e;
    }

    /// <summary>원소별 총량 (기체 + 바닥 저장).</summary>
    public double[] ElementTotals()
    {
        var moles = new double[T.Substances.Count];
        for (int g = 0; g < G; g++) { double s = 0; for (int i = 0; i < Cells; i++) s += N[i * G + g]; moles[T.Gases[g].Index] += s; }
        for (int st = 0; st < S; st++) { double s = 0; for (int i = 0; i < Cells; i++) s += Store[st * Cells + i]; moles[T.Stores[st].Index] += s; }
        return T.ElementsOf(moles);
    }
}
