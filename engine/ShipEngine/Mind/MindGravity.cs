namespace ShipEngine;

/// <summary>
/// 무중력의 몸 (바퀴 3 (나) — docs/ENGINE.md '인공 중력 · 무중력'): 바닥 중력이 0.3g 아래면 사람은 떠오른다 — 걷지 못하고 손잡이를 잡고 밀어 떠 간다
/// (처음 0.4m/s → 떠서 8시간 지내면 1m/s) · 로봇은 자석 발로 (0.6배) · 드론은 그대로. 침대 밖에서 자던 이는 떠다니다 부딪혀 깬다.
/// 중력이 다시 서면 떠 있던 이는 바닥에 떨어진다 — 서툰 이일수록 넘어지고 (앉아 있으면 덜) · 원인은 중력이 돌아온 마디.
/// </summary>
public sealed partial class Mind
{
    public const float FloatG = Field.LowG;
    public const double FloatSlow = 0.4, FloatFast = 1.0, FloatLearnH = 8, LandFall = 0.45;
    public long Floats, Landings, LandFalls, Braced, StaleWakes;

    /// <summary>떠 가는 빠르기 (m/s) — 떠서 지낸 시간만큼 손잡이를 잘 탄다.</summary>
    private static double FloatPace(in AgentCore c) => FloatSlow + (FloatFast - FloatSlow) * Math.Clamp(c.ZeroGH / FloatLearnH, 0, 1);

    private void Floating(Sim sim, double dt)
    {
        if (_life == null || _net == null || !_net.HasGravity) return;
        long t = sim.Tick;
        foreach (var a in Agents)
        {
            if (!a.IsPerson || a.C.Body < 0) continue;
            ref var c = ref a.C;
            ref var b = ref _life[c.Body];
            if (b.Alive == 0 || c.Cell < 0) continue;
            float g = F.Grav[c.Cell];
            if (g < FloatG)
            {
                c.ZeroGH += (float)(dt / 3600);
                if (c.Floated == 0)
                {
                    c.Floated = 1; Floats++;
                    if (b.Asleep != 0 && !InBed(a))   // 묶지 않고 자던 이는 떠다니다 부딪혀 깬다
                        Wake(sim, a, "떠다니다 부딪혀 깼다", _net.GravityCauses(c.Cell).lost);
                    if (c.ZeroGH < 1) c.Fear = Math.Min(1, c.Fear + 0.15f);   // 처음 떠오르면 놀란다
                }
                // 공조 바람이 닿지 않는 자리에서 자면 날숨이 얼굴 앞에 고여 숨이 답답하고 머리가 아파 깬다 (정거장 침낭은 통풍구 앞에 둔다)
                if (b.Asleep != 0 && b.FaceCO2 > 1 && b.Pain >= 0.25f && F.Fan(c.Cell) < 0.5)
                {
                    int lost = _net.GravityCauses(c.Cell).lost;
                    int cause = sim.Causes.Add(CauseKind.Biology, "무중력에서 날숨이 얼굴 앞에 고였다 — 통풍구 바람이 닿지 않는 자리", t, c.Cell, lost);
                    StaleWakes++;
                    Wake(sim, a, "숨이 답답하고 머리가 아파 깼다", cause);
                    int spot = AirySpot(c.Cell);   // 다음에는 같은 자리에서 자지 않는다 — 공조 바람이 닿는 가장 가까운 자리를 잘 자리로
                    if (spot >= 0) c.ZeroGBed = spot + 1;
                }
            }
            else if (c.Floated != 0 && g >= 0.5f)
            {
                c.Floated = 0; Landings++;
                if (c.Up == 0 || c.CarriedBy >= 0 || b.Asleep != 0 && InBed(a)) continue;
                double p = LandFall * (1 - Math.Clamp(c.ZeroGH / FloatLearnH, 0, 1));
                if (Seated(a)) p *= 0.3;
                foreach (var bl in a.Beliefs)   // 주컴퓨터 방송을 듣고 바닥 쪽을 잡았다
                    if (bl.Kind == BeliefKind.GravityBack && bl.Conf >= 0.3f && t - bl.Seen < Time.Minutes(5)) { p *= 0.25; Braced++; break; }
                if (Rng.Unit(sim.Seed, Rng.Key("내려앉음"), t, a.Id) >= p) continue;
                LandFalls++;
                FallDown(sim, a, 0.3, "돌아온 중력에 바닥으로 떨어져", _net.GravityCauses(c.Cell).back, showG: false);
            }
        }
    }

    /// <summary>공조 바람이 닿는 가장 가까운 칸 (같은 구역 · 설비가 없는 바닥) — 없으면 −1.</summary>
    private int AirySpot(int from)
    {
        int best = -1, bd = int.MaxValue, reg = F.RegionOf[from];
        for (int i = 0; i < F.Cells; i++)
        {
            if (F.Airflow[i] <= 0 || F.RegionOf[i] < 0 || L.Kind[i] != CellKind.Floor || L.FurnitureAt[i] >= 0) continue;
            int d = Math.Abs(L.X(i) - L.X(from)) + Math.Abs(L.Y(i) - L.Y(from)) + (F.RegionOf[i] == reg ? 0 : 1000);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>무중력에서 잘 자리 (공조 바람이 아직 닿는 곳 · 그 칸이 무중력일 때) — 없으면 −1.</summary>
    private int ZeroGSleepSpot(in AgentCore c, int bedCell)
    {
        int zb = c.ZeroGBed - 1;
        return zb >= 0 && bedCell >= 0 && F.Grav[bedCell] < FloatG && F.Airflow[zb] > 0 ? zb : -1;
    }

    /// <summary>지금 걸음 빠르기 (m/s — 화면 · 시험).</summary>
    public double Pace(int agent) => SpeedOf(Agents[agent]);

    private bool InBed(Agent a) => a.C.Cell >= 0 && L.FurnitureAt[a.C.Cell] >= 0 && L.Furniture[L.FurnitureAt[a.C.Cell]].Type is "Bed" or "MedBed";
}
