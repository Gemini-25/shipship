namespace ShipEngine;

/// <summary>
/// ② 물질 — 칸의 화학 (불 · 기체 폭발 · 느린 반응). 반응식 표를 그대로 칸 위에서 돌린다.
/// <para>
/// <b>불</b>: 칸의 연료(바닥 저장의 고분자 · 섬유 · 양분 · 그을음)가 발화 온도를 넘으면 붙는다. 타는 빠르기는 산소 분압 · 남은 연료로 정해지고,
/// 산소가 모자라면 일산화탄소 · 그을음이 는다. 열의 70%는 그 칸 기체로, 15%는 바닥으로, 15%는 이웃 칸으로 (복사 — 그래서 옆 가구로 번진다).
/// 기체가 500K 아래로 식거나 · 산소 분압이 9kPa 아래거나 · 연료가 떨어지면 꺼진다 (진공으로 빼면 꺼진다 · 물을 부으면 식어서 꺼진다).
/// </para>
/// <para>
/// <b>기체 불(폭발)</b>: 수소 · 메탄 · 일산화탄소 · 암모니아가 연소 범위 안에 있고 불씨(불 붙은 이웃 · 전기 불꽃)가 닿거나 자연 발화 온도를 넘으면
/// 한 틱에 대부분이 탄다 → 온도 · 압력이 튄다 → 이웃 칸이 다음 틱의 불씨가 된다 (불꽃 앞면이 퍼진다).
/// </para>
/// 에너지 장부: 반응열 = Σ반응물 E − Σ생성물 E (E = 생성 엔탈피 + 기체는 현열) — 장의 장부와 맞아 보존된다 (법칙 시험).
/// </summary>
public sealed class Chemistry : IEngine
{
    public string Name => "화학";
    public Phase Phase => Phase.Chemistry;
    public int Every => 1;
    public int Offset => 0;

    /// <summary>불 붙은 칸 하나가 산소가 넉넉할 때 내는 열 (W) — 가구 하나가 활활 타는 정도.</summary>
    public const double CellHrr = 250_000;
    public const double SustainK = 500;
    public const double MinO2kPa = 9;

    private readonly Field F;
    private readonly Tables T;
    private readonly int Cells;

    // ── 상태 ──
    public byte[] Burning;
    /// <summary>불 붙은 칸의 원인 마디.</summary>
    public int[] FireCause;
    /// <summary>지난 틱에 낸 열 (kW) — 화면 · 연출.</summary>
    public float[] Hrr;
    /// <summary>이번 틱의 불씨 (전기 불꽃 · 불 붙은 이웃의 기체 불) — 다음 틱에 쓴다.</summary>
    private byte[] _spark, _sparkNext;
    private int[] _sparkCause, _sparkCauseNext;
    public long Ignitions, GasBursts;

    private readonly (Reaction full, Reaction? co, Reaction? soot, int store)[] _fuels;
    private readonly (Reaction r, int gas, double lfl, double ufl)[] _gasFires;
    private readonly Reaction _ozone;

    public Chemistry(Field f)
    {
        F = f; T = f.T; Cells = f.Cells;
        Burning = new byte[Cells]; FireCause = new int[Cells]; Hrr = new float[Cells];
        _spark = new byte[Cells]; _sparkNext = new byte[Cells]; _sparkCause = new int[Cells]; _sparkCauseNext = new int[Cells];
        _fuels = new[]
        {
            (T.R("burn_poly"), (Reaction?)T.R("burn_poly_co"), (Reaction?)T.R("burn_poly_soot"), T.sPoly),
            (T.R("burn_cell"), T.R("burn_cell_co"), T.R("char_cell"), T.sCell),
            (T.R("burn_food"), null, null, T.sFood),
            (T.R("burn_soot"), null, null, T.sSoot),
        };
        // 연소 범위 (공기 중 부피 %): 수소 4~75 · 메탄 5~15 · 일산화탄소 12.5~74 · 암모니아 15~28
        _gasFires = new[]
        {
            (T.R("burn_h2"), T.gH2, 0.04, 0.75),
            (T.R("burn_ch4"), T.gCH4, 0.05, 0.15),
            (T.R("burn_co"), T.gCO, 0.125, 0.74),
            (T.R("burn_nh3"), T.gNH3, 0.15, 0.28),
        };
        _ozone = T.R("ozone_decay");
    }

    /// <summary>불씨 (전기 불꽃 · 성냥 · 과열): 그 칸 겉면에 열을 넣고, 다음 틱에 연소 범위의 기체를 태운다.</summary>
    public void Ignite(int cell, double joules, int cause)
    {
        F.AddSurfaceHeat(cell, joules);
        _spark[cell] = 1;
        _sparkCause[cell] = cause;
    }

    public bool IsBurning(int cell) => Burning[cell] != 0;
    public int BurningCount { get; private set; }

    public void Step(Sim sim, double dt)
    {
        F.RebuildNow(); // 장보다 먼저 돈다 — 구역이 바뀌었으면 지금 짠다
        Array.Clear(Hrr);
        Array.Clear(_sparkNext);
        int burning = 0;
        Span<int> nb = stackalloc int[4];
        for (int i = 0; i < Cells; i++)
        {
            if (F.RegionOf.Length <= i || F.RegionOf[i] < 0) { if (Burning[i] != 0) Out(sim, i); continue; }
            GasFire(sim, i, dt, nb);
            Quench(i);
            if (!HasFuel(i)) { if (Burning[i] != 0) Out(sim, i); continue; }
            double tf = F.Tf[i], pO2 = F.Partial(T.gO2, i);
            if (Burning[i] == 0)
            {
                // 발화: 겉면이 그 연료의 발화 온도를 넘었고 산소가 있다
                foreach (var fu in _fuels)
                {
                    if (F.Stored(fu.store, i) < 1e-3) continue;
                    if (tf >= fu.full.Def.MinK && pO2 >= MinO2kPa)
                    {
                        Light(sim, i, nb);
                        break;
                    }
                }
                if (Burning[i] == 0) continue;
            }
            if (tf < SustainK || pO2 < MinO2kPa * 0.6) { Out(sim, i); continue; }
            Burn(sim, i, dt, pO2, nb);
            if (Burning[i] != 0) burning++;
        }
        BurningCount = burning;
        (_spark, _sparkNext) = (_sparkNext, _spark);
        (_sparkCause, _sparkCauseNext) = (_sparkCauseNext, _sparkCause);
        if (sim.Tick % 25 == 0) Slow(25 * dt);
    }

    private bool HasFuel(int i)
    {
        foreach (var fu in _fuels) if (F.Stored(fu.store, i) > 1e-3) return true;
        return false;
    }

    /// <summary>불이 붙는다 — 원인: 불씨 > 불 붙은 이웃 > 뜨거운 공기.</summary>
    private void Light(Sim sim, int i, Span<int> nb)
    {
        Burning[i] = 1;
        Ignitions++;
        int cause = _spark[i] != 0 ? _sparkCause[i] : 0;
        bool spread = false;
        if (cause == 0)
        {
            F.L.Neighbors(i, nb);
            foreach (int j in nb)
                if (j >= 0 && Burning[j] != 0) { cause = sim.Causes.Add(CauseKind.Fire, "옆 칸으로 번졌다", sim.Tick, i, FireCause[j]); spread = true; break; }
        }
        if (cause == 0) cause = sim.Causes.Add(CauseKind.Heat, "뜨거워져 불이 붙었다", sim.Tick, i);
        FireCause[i] = cause;
        sim.Facts.Emit(sim.Tick, spread ? FactKind.FireSpread : FactKind.Ignition, i, (float)F.Temp(i), cause);
    }

    private void Out(Sim sim, int i)
    {
        Burning[i] = 0;
        sim.Facts.Emit(sim.Tick, FactKind.FireOut, i, 0, FireCause[i]);
    }

    /// <summary>한 칸이 dt초 동안 탄다.</summary>
    private void Burn(Sim sim, int i, double dt, double pO2, Span<int> nb)
    {
        double g = Math.Clamp((pO2 - MinO2kPa) / 12.0, 0, 1.5);
        double tg = F.Temp(i);
        // 산소가 모자랄수록 그을며 탄다 (불완전 연소) — 늘 조금은 그을음
        double incomplete = Math.Clamp(1 - (pO2 - MinO2kPa) / 8.0, 0, 1) * 0.7 + 0.05;
        double heat = 0;
        double o2Budget = F.Gas(T.gO2, i) * 0.5; // 한 틱에 그 칸 산소의 절반까지
        foreach (var fu in _fuels)
        {
            double fuel = F.Stored(fu.store, i);
            if (fuel < 1e-6 || F.Tf[i] < fu.full.Def.MinK * 0.8) continue; // 겉면이 식은 연료는 타지 않는다
            double ff = Math.Min(1, fuel / 200.0) * 0.8 + 0.2 * Math.Min(1, fuel / 20.0);
            double dhFull = -fu.full.DH298; // J/반응몰
            double ext = CellHrr * fu.full.Def.Rate * g * ff / dhFull * dt; // 반응 몰
            if (ext <= 0) continue;
            // 갈래: 완전 · 일산화탄소 · 그을음
            double share = 1 - incomplete;
            heat += React(sim, i, fu.full, ext * share, ref o2Budget);
            if (fu.co != null) heat += React(sim, i, fu.co, ext * incomplete * 0.6 * (fu.full.In[0].Nu / fu.co.In[0].Nu), ref o2Budget);
            if (fu.soot != null) heat += React(sim, i, fu.soot, ext * incomplete * 0.4 * (fu.full.In[0].Nu / fu.soot.In[0].Nu), ref o2Budget);
        }
        if (heat <= 0) return;
        Hrr[i] = (float)(heat / dt / 1000);
        // 열 나누기 (보존):
        //   ① 불꽃이 제 겉면을 불꽃 온도 가까이 달군다 (산소가 많을수록 뜨겁다 · 많아야 40%)
        //   ② 이웃 겉면으로 불꽃 복사 εσF(T불꽃⁴−Tn⁴) (보는 각 F=0.3 — 1m 곁 가구가 받는 15~20kW/m² · 많아야 35%)
        //   ③ 나머지: 기체 80% · 바닥 20%
        double left = heat;
        double tFlame = 750 + 550 * Math.Min(1, g);
        double own = Math.Clamp((tFlame - F.Tf[i]) * Field.SurfaceC, 0, heat * 0.4);
        F.AddSurfaceHeat(i, own); left -= own;
        F.L.Neighbors(i, nb);
        double tf = Math.Max(F.Tf[i], tFlame * Math.Min(1, Hrr[i] / 150.0)), tf4 = tf * tf * tf * tf, radTotal = 0;
        Span<double> rad = stackalloc double[4];
        for (int k = 0; k < 4; k++)
        {
            int j = nb[k];
            rad[k] = 0;
            if (j < 0 || !F.HasSurface(j)) continue;
            double tn = F.Tf[j];
            rad[k] = Math.Max(0, 0.9 * Field.Sigma * 0.3 * (tf4 - tn * tn * tn * tn)) * dt;
            radTotal += rad[k];
        }
        double scale = radTotal > heat * 0.35 ? heat * 0.35 / radTotal : 1;
        for (int k = 0; k < 4; k++) if (rad[k] > 0) { F.AddSurfaceHeat(nb[k], rad[k] * scale); left -= rad[k] * scale; }
        F.AddHeat(i, left * 0.8);
        F.AddSolidHeat(i, left * 0.2);
        // 상승 기류: 불꽃이 둘레 공기를 빨아들이고 뜨거운 연기를 내보낸다 (불이 셀수록 세게)
        double pull = 0.06 + 0.12 * Math.Min(1, Hrr[i] / 250.0);
        foreach (int j in nb)
            if (j >= 0 && F.RegionOf[j] == F.RegionOf[i]) F.Exchange(i, j, pull);
    }

    /// <summary>물이 고인 칸: 뜨거운 겉면이 물을 끓여 식는다 (증발열) — 불을 끄는 길.</summary>
    private void Quench(int i)
    {
        if (!F.HasSurface(i)) return;
        double water = F.Stored(T.sWater, i);
        if (water < 1 || F.Tf[i] <= 380) return;
        var vap = T.Gases[T.gH2O];
        var liq = T.Substances[T.Stores[T.sWater].Index];
        double perMol = (vap.Hf - liq.Hf) * 1000 + vap.C * (373.15 - 298.15);
        double n = Math.Min(water, (F.Tf[i] - 373.15) * Field.SurfaceC / perMol);
        if (n <= 0) return;
        F.TakeStore(i, T.sWater, n);
        F.AddGas(i, T.gH2O, n, 373.15);
        F.Tf[i] -= n * perMol / Field.SurfaceC;
    }

    /// <summary>
    /// 반응을 칸에서 일으킨다: 반응물(기체 · 바닥 저장)을 빼고 생성물을 넣는다 — 반응열을 돌려준다 (장에 넣는 것은 부르는 쪽).
    /// 기체 반응물은 그 칸 온도의 현열과 함께 빠지고, 기체 생성물은 그 온도의 현열과 함께 들어간다 (장부를 맞추는 반응열은 Reaction.Heat).
    /// </summary>
    private double React(Sim sim, int i, Reaction r, double extent, ref double o2Budget)
    {
        if (extent <= 0) return 0;
        foreach (var x in r.In)
        {
            var s = T.Substances[x.S];
            double have = s.Gas >= 0 ? F.Gas(s.Gas, i) : s.Store >= 0 ? F.Stored(s.Store, i) : 0;
            if (s.Gas == T.gO2) have = Math.Min(have, o2Budget);
            extent = Math.Min(extent, have / x.Nu);
        }
        if (extent <= 1e-12) return 0;
        double tg = F.Temp(i);
        double q = r.Heat(T, tg) * extent;
        foreach (var x in r.In)
        {
            var s = T.Substances[x.S];
            double n = x.Nu * extent;
            if (s.Gas >= 0) { F.RemoveGas(i, s.Gas, n); if (s.Gas == T.gO2) o2Budget -= n; }
            else F.TakeStore(i, s.Store, n);
        }
        foreach (var x in r.Out)
        {
            var s = T.Substances[x.S];
            double n = x.Nu * extent;
            if (s.Gas >= 0) F.AddGas(i, s.Gas, n, tg);
            else if (s.Store >= 0) F.AddStore(i, s.Store, n);
        }
        return q;
    }

    /// <summary>기체 불: 연소 범위 안 + (불씨 · 불 붙은 이웃 · 자연 발화 온도) → 한 틱에 대부분.</summary>
    private void GasFire(Sim sim, int i, double dt, Span<int> nb)
    {
        double n = 0;
        foreach (var gf in _gasFires) n += F.Gas(gf.gas, i);
        if (n < 1e-6) return;
        double total = F.Moles(i);
        if (total < 1e-6) return;
        double o2 = F.Gas(T.gO2, i) / total;
        if (o2 < 0.05) return;
        double tg = F.Temp(i);
        bool spark = _spark[i] != 0 || Burning[i] != 0;
        foreach (var gf in _gasFires)
        {
            double x = F.Gas(gf.gas, i) / total;
            if (x < gf.lfl || x > gf.ufl) continue;
            if (!spark && tg < gf.r.Def.MinK) continue;
            double budget = F.Gas(T.gO2, i) * 0.9;
            double fuelNu = gf.r.In.First(t => T.Substances[t.S].Gas == gf.gas).Nu;
            double heat = React(sim, i, gf.r, F.Gas(gf.gas, i) * 0.8 / fuelNu * gf.r.Def.Rate, ref budget);
            if (heat <= 0) continue;
            F.AddHeat(i, heat);
            Hrr[i] += (float)(heat / dt / 1000);
            GasBursts++;
            int cause = _spark[i] != 0 ? _sparkCause[i] : FireCause[i];
            if (cause == 0) cause = sim.Causes.Add(CauseKind.Reaction, $"{T.Substances[T.Gases[gf.gas].Index].Name}이(가) 한꺼번에 탔다", sim.Tick, i);
            sim.Facts.Emit(sim.Tick, FactKind.Ignition, i, (float)(heat / 1000), cause, other: gf.gas);
            // 불꽃 앞면: 이웃이 다음 틱의 불씨
            F.L.Neighbors(i, nb);
            foreach (int j in nb) if (j >= 0) { _sparkNext[j] = 1; _sparkCauseNext[j] = cause; }
        }
    }

    /// <summary>느린 반응 (25틱마다): 오존이 산소로 돌아간다.</summary>
    private void Slow(double dt)
    {
        int g = T.gO3;
        double k = 1 - Math.Exp(-_ozone.Def.Rate * dt);
        double budget = double.MaxValue;
        for (int i = 0; i < Cells; i++)
        {
            double o3 = F.Gas(g, i);
            if (o3 < 1e-9) continue;
            double tg = F.Temp(i);
            double ext = o3 * k * (tg > 350 ? 10 : 1) / 2;
            double q = _ozone.Heat(T, tg) * ext;
            F.RemoveGas(i, g, 2 * ext);
            F.AddGas(i, T.gO2, 3 * ext, tg);
            F.AddHeat(i, q);
            _ = budget;
        }
    }

    public void Save(StateWriter w)
    {
        w.Arr(Burning); w.Arr(FireCause); w.Arr(Hrr); w.Arr(_spark); w.Arr(_sparkCause);
        w.I64(Ignitions); w.I64(GasBursts);
    }

    public void Load(StateReader r)
    {
        r.ArrInto(Burning); r.ArrInto(FireCause); r.ArrInto(Hrr); r.ArrInto(_spark); r.ArrInto(_sparkCause);
        Ignitions = r.I64(); GasBursts = r.I64();
    }
}
