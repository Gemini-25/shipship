namespace ShipEngine;

/// <summary>
/// 화장실 · 샤워실의 몸 (월드 — docs/WASH.md): 참지 못하면 지린다 · 변기에 비운다 · 물로 씻는다 · 물수건으로 닦는다.
/// 언제 가고 · 씻을지는 사람의 판단 (⑧ MindWash) — 여기는 그 행동이 몸과 세상을 어떻게 바꾸는지만 셈한다.
/// 물 장부: 비운 오줌 · 씻은 물은 오수로 (물 재생기가 되돌린다) · 지린 오줌은 바닥의 물 · 샤워의 김은 칸의 수증기.
/// </summary>
public sealed partial class Life
{
    /// <summary>비운 대변 (kg — 변기 아래 오물 통으로) · 지린 횟수 · 씻은 횟수 (재기용).</summary>
    public double SewageKg;
    public long Soilings, Showers, Voids;

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

    /// <summary>물수건으로 닦는다 (물 없이 — 샤워를 못 쓸 때): 더러움이 반으로 · 지린 옷은 그대로 (갈아입어야 한다).</summary>
    public void Wipe(int body) { ref var b = ref this[body]; b.Dirt *= 0.5f; }
}
