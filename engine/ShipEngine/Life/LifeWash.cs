namespace ShipEngine;

/// <summary>
/// 화장실 · 샤워실의 몸 (월드 — docs/WASH.md): 참지 못하면 지린다 · 변기에 비운다 · 물로 씻는다 · 물수건으로 닦는다.
/// 언제 가고 · 씻을지는 사람의 판단 (⑧ MindWash) — 여기는 그 행동이 몸과 세상을 어떻게 바꾸는지만 셈한다.
/// 물 장부: 비운 오줌 · 씻은 물은 오수로 (물 재생기가 되돌린다) · 지린 오줌은 바닥의 물 · 샤워의 김은 칸의 수증기.
/// </summary>
public sealed partial class Life
{
    /// <summary>비운 대변 (kg — 변기 · 용변 봉투를 모두 친 누계) · 지린 횟수 · 씻은 횟수 (재기용).</summary>
    public double SewageKg;
    public long Soilings, Showers, Voids;

    // ── 변기마다 (설비 번호로 · (다)): 오물 통 · 막힘 · 때 · 물 내린 수 ──
    /// <summary>오물 통 하나의 한도 (kg) — 가득 차면 더 내려가지 않는다.</summary>
    public const double TankMax = 30;
    /// <summary>오물 통 (kg) · 막혀 변기에 걸린 대변 (kg) · 비운 곳(퇴비통 · 압축기 · 화물 상자)에 쌓인 오물 (kg).</summary>
    public double[] Tank = Array.Empty<double>(), Bowl = Array.Empty<double>(), Heap = Array.Empty<double>();
    public byte[] Clogged = Array.Empty<byte>();
    public int[] ClogCause = Array.Empty<int>();
    /// <summary>변기의 때 (0 깨끗 · 1 몹시 — 쓸 때마다 · 넘치면 1).</summary>
    public float[] Grime = Array.Empty<float>();
    /// <summary>물 내린 수 (변기 조절기가 센다 — 주컴퓨터가 읽는 센서) · 마지막으로 닦았을 때의 수 (닦은 이가 조절기에 적는다).</summary>
    public int[] Flushes = Array.Empty<int>(), ScrubbedAt = Array.Empty<int>();
    public long Clogs, Overflows;
    /// <summary>들고 옮기는 중인 오물 (kg — 로봇 손 · 바닥에 내려놓은 통 · 질량을 정확히 세려고 월드가 쥔다).</summary>
    public double WasteCarried;
    private int[]? _toiletDev;

    private void WashArrays()
    {
        int n = L.Furniture.Count;
        if (Tank.Length == n) return;
        Array.Resize(ref Tank, n); Array.Resize(ref Bowl, n); Array.Resize(ref Heap, n); Array.Resize(ref Clogged, n);
        Array.Resize(ref ClogCause, n); Array.Resize(ref Grime, n); Array.Resize(ref Flushes, n); Array.Resize(ref ScrubbedAt, n);
    }

    private int ToiletDev(int fu)
    {
        if (_net == null) return -1;
        if (_toiletDev == null)
        {
            _toiletDev = Enumerable.Repeat(-1, L.Furniture.Count).ToArray();
            foreach (var d in _net.Devices) if (d.Furniture >= 0 && d.Furniture < _toiletDev.Length) _toiletDev[d.Furniture] = d.Id;
        }
        return fu < _toiletDev.Length ? _toiletDev[fu] : -1;
    }

    /// <summary>
    /// 변기에 비운다 (월드 — 언제 · 어느 변기는 ⑧이 골랐다): 앉은 이는 변기의 때만큼 조금 더러워진다 ·
    /// 막힌 변기면 넘친다 (오줌은 그 칸 바닥으로 · 지린내 · 대변은 변기에 걸린다 · 원인 마디 '넘쳤다' ← 막힘 · 사실 '넘쳤다') ·
    /// 아니면 오줌은 오수로 (물 재생기가 되돌린다) · 대변은 그 변기의 오물 통으로 — 흡입 팬이 돌지 않거나 (망가짐 · 전기) · 통이 가득 찼거나 ·
    /// 0.4% (통이 90% 넘게 찼으면 20%)로 관에 걸리면 막힌다 (원인 마디 '변기가 막혔다 — 까닭'). 돌려준다: 넘쳤으면 참.
    /// </summary>
    public bool Flush(Sim sim, int body, int toilet, int cell)
    {
        WashArrays();
        _net ??= sim.Find<Network>();   // 몸 셈(몇 틱마다)보다 먼저 쓸 수 있다 — 오줌이 오수 장부 밖으로 새지 않게 (법칙 시험이 찾았다)
        ref var b = ref this[body];
        b.Dirt = Math.Min(1, b.Dirt + 0.05f * Grime[toilet]);
        double urine = b.Bladder, stool = b.Bowel > 0.02 ? b.Bowel : 0;
        b.Bladder = 0;
        if (stool > 0) { b.Bowel = 0; SewageKg += stool; }
        Voids++; Flushes[toilet]++;
        Grime[toilet] = Math.Min(1, Grime[toilet] + (stool > 0 ? 0.06f : 0.03f));
        if (Clogged[toilet] != 0)
        {
            if (urine > 0) { F.AddStore(cell, T.sWater, urine); F.AddGas(cell, T.gNH3, urine * 1e-4, F.Temp(cell)); }   // 바닥이 젖고 지린내 (오줌의 요소가 풀린 암모니아)
            Bowl[toilet] += stool;
            Grime[toilet] = 1;
            Overflows++;
            int cause = sim.Causes.Add(CauseKind.Machine, "막힌 변기를 모르고 써서 넘쳤다", sim.Tick, cell, ClogCause[toilet]);
            sim.Facts.Emit(sim.Tick, FactKind.Overflowed, cell, (float)urine, cause, actor: body, other: toilet);
            return true;
        }
        if (_net != null) _net.WasteWater += urine;
        if (stool <= 0) return false;
        int d = ToiletDev(toilet);
        string? why = null;
        int parent = 0;
        if (d >= 0 && !_net!.Working(d))
        {
            why = _net.Supply[d] <= 0.05f ? "흡입 팬에 전기가 없어 내려가지 않았다" : "흡입 팬이 돌지 않아 내려가지 않았다";
            parent = _net.PartCause(d, Part.Mechanical);
        }
        else if (Tank[toilet] + stool > TankMax) why = "오물 통이 가득 차 내려가지 않았다";
        else if (Rng.Unit(sim.Seed, Rng.Key("변기 막힘"), sim.Tick, toilet) < (Tank[toilet] > 0.9 * TankMax ? 0.204 : 0.004)) why = "대변이 관에 걸렸다";
        if (why == null) { Tank[toilet] += stool; return false; }
        Bowl[toilet] += stool; Clogged[toilet] = 1; Clogs++;
        ClogCause[toilet] = sim.Causes.Add(CauseKind.Machine, $"변기가 막혔다 — {why}", sim.Tick, cell, parent);
        return false;
    }

    /// <summary>막힌 변기를 뚫는다 (월드 — 뚫는 이는 ⑧): 걸린 대변은 오물 통으로 (넘치면 넘는 만큼 변기에 남고 막힘도 그대로).</summary>
    public void Unclog(int toilet)
    {
        WashArrays();
        double room = Math.Max(0, TankMax - Tank[toilet]), go = Math.Min(room, Bowl[toilet]);
        Tank[toilet] += go; Bowl[toilet] -= go;
        if (Bowl[toilet] <= 1e-9) { Bowl[toilet] = 0; Clogged[toilet] = 0; ClogCause[toilet] = 0; }
    }

    /// <summary>변기를 닦는다 (월드): 때가 없어지고 · 조절기에 닦은 때의 물 내린 수를 적는다.</summary>
    public void Scrub(int toilet) { WashArrays(); Grime[toilet] = 0; ScrubbedAt[toilet] = Flushes[toilet]; }

    /// <summary>오물 통을 들어낸다 (곁의 빈 통으로 바꿔 끼운다 — 변기는 바로 다시 쓸 수 있다): 든 무게 (kg).</summary>
    public double LiftTank(int toilet) { WashArrays(); double kg = Tank[toilet]; Tank[toilet] = 0; WasteCarried += kg; return kg; }

    /// <summary>들고 온 오물을 비울 곳(퇴비통 · 압축기 · 화물 상자)에 쏟는다.</summary>
    public void Dump(int fu, double kg)
    {
        WashArrays();
        kg = Math.Min(kg, WasteCarried);
        if (WasteCarried - kg < 1e-3) kg = WasteCarried;   // 든 무게는 손의 눈금(float)으로 옮긴다 — 끝자리는 마지막에 쏟는 이가 함께 (질량 그대로)
        Heap[fu] += kg; WasteCarried -= kg;
    }

    /// <summary>시험 · 사고: 변기를 막는다 (원인 마디를 받는다).</summary>
    public void Clog(int toilet, int cause) { WashArrays(); Clogged[toilet] = 1; ClogCause[toilet] = cause; }

    /// <summary>참지 못했다: 방광 · 장이 한계를 넘었다 — 그 자리 바닥이 젖고 · 몸과 옷이 더러워진다 (원인: 그때 하던 일은 마음이 안다 — 여기는 몸의 한계).</summary>
    private void Soil(Sim sim, int id, ref Body b, int c)
    {
        bool bowel = b.Bowel > BowelMax;
        double urine = b.Bladder;
        if (urine > 0) { F.AddStore(c, T.sWater, urine); b.Bladder = 0; }   // 바닥이 젖는다 (오줌의 물 — 고인 물 장부)
        if (bowel) b.Bowel = 0;                                               // 대변은 옷에 (씻을 때 오수로)
        b.Dirt = Math.Min(1, b.Dirt + (bowel ? 0.6f : 0.3f));
        b.Wet = 1;
        Soilings++;
        b.SoilCause = sim.Causes.Add(CauseKind.Biology, bowel ? "참지 못하고 옷에 지렸다 (장이 찼다)" : "참지 못하고 옷에 지렸다 (방광이 찼다)", sim.Tick, c);
        sim.Facts.Emit(sim.Tick, FactKind.Soiled, c, bowel ? 2 : 1, b.SoilCause, actor: id);
    }

    /// <summary>변기에 비운다 (월드): 오줌 → 오수 (물 재생기로) · 대변 → 오물 통 — 돌려준다: 비운 오줌 몰.</summary>
    public double Void(int body)
    {
        ref var b = ref this[body];
        double urine = b.Bladder;
        b.Bladder = 0;
        if (b.Bowel > 0.02) { SewageKg += b.Bowel; b.Bowel = 0; }
        if (_net != null) _net.WasteWater += urine;
        Voids++;
        return urine;
    }

    /// <summary>
    /// 물로 씻는다 (월드 — 샤워 칸 · 세면대): 물 water몰(망의 물탱크에서 이미 꺼낸 것)이 더러움을 water/ShowerMol만큼 씻고 · 지린 옷도 (물 200몰 넘게) ·
    /// 쓴 물은 오수로 · 따뜻한 물이면 그 3%가 김으로 칸의 공기에 (습도) · 열의 15%가 칸으로.
    /// </summary>
    public void Wash(Sim sim, int body, double water, double waterK, int cell)
    {
        ref var b = ref this[body];
        b.Dirt = (float)Math.Max(0, b.Dirt - water / ShowerMol);
        if (water >= 200) { b.Wet = 0; Showers++; }
        double steam = waterK > 305 ? water * 0.01 : 0;   // 따뜻한 물의 1%가 김으로 (8L면 80mL)
        if (steam > 0 && cell >= 0) { F.AddGas(cell, T.gH2O, steam, waterK); F.AddHeat(cell, water * 75.3 * (waterK - F.Temp(cell)) * 0.15); }
        if (_net != null) _net.WasteWater += water - steam;
    }

    /// <summary>샤워 칸에서 흐른 물로 씻긴다 (월드 — 물은 ⑥ 샤워 칸이 이미 흘려 오수로 보냈다): 더러움이 water/ShowerMol만큼 · 지린 옷도 (물 200몰 넘게).</summary>
    public void Rinse(int body, double water)
    {
        ref var b = ref this[body];
        b.Dirt = (float)Math.Max(0, b.Dirt - water / ShowerMol);
        if (water >= 200) { b.Wet = 0; Showers++; }
    }

    /// <summary>물수건으로 닦는다 (물 없이 — 샤워를 못 쓸 때 · 제 침대에서): 더러움이 반으로 · 지린 옷은 갈아입는다.</summary>
    public void Wipe(int body) { ref var b = ref this[body]; b.Dirt *= 0.5f; b.Wet = 0; }
}
