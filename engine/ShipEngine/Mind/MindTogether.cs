namespace ShipEngine;

/// <summary>
/// ⑨ → ⑧ 사회 값이 일상 행동에 닿는다 (깊이 바퀴 2 · ⑨ 보강). 마음은 사회 엔진을 읽기만 한다.
/// <para><b>가까운 이</b>: 짝 1 · 나를 이끄는 이 0.8 · 같은 무리 0.6 · 사이가 벌어진 무리 −1.</para>
/// <para><b>함께 먹는다</b>: 가까운 이가 먹으러 가면 조금 배고파도(0.35) 따라가 같은 자리에서 먹는다 · 벌어진 무리 사람이 먹는 자리는 피한다.</para>
/// <para><b>따라 나간다</b>: 가까운 이가 이 방에서 빠져나가면 위험을 몰라도 따라 나간다 (피난 연구 — 사람은 아는 이와 함께 움직인다 · 짝 5 · 이끄는 이 4.5 · 무리 4).</para>
/// <para><b>벌어진 무리</b>: 그 무리 사람이 있는 방 · 쉬는 자리는 피한다 (어울릴 방 −0.5 · 쉬는 자리 무게 × 0.3).</para>
/// </summary>
public sealed partial class Mind
{
    public long AteTogether, Followed;
    private float _followScore;   // 이번 고르기에서 '따라 나가기' 선택지의 점수 (고른 것이 그것인지 알려고)

    /// <summary>나에게 그 사람은: 짝 1 · 나를 이끄는 이 0.8 · 같은 무리 0.6 · 벌어진 무리 −1 · 아니면 0.</summary>
    private float Kin(int me, int other)
    {
        if (_soc == null || me == other || other < 0) return 0;
        if (_soc.PartnerOf(me) == other) return 1;
        if (_soc.Leads(other, me)) return 0.8f;
        if (_soc.SameBand(me, other)) return 0.6f;
        if (_soc.Rival(me, other)) return -1;
        return 0;
    }

    /// <summary>벌어진 무리 사람이 그 칸 곁(4칸)에 깨어 있나.</summary>
    private bool RivalNear(Agent a, int cell, int r = 4)
    {
        if (_soc == null || cell < 0 || _soc.BandIdOf(a.Id) < 0) return false;
        foreach (var o in Agents)
            if (o != a && o.IsPerson && o.C.Up != 0 && Manhattan(o.C.Cell, cell) <= r && _soc.Rival(a.Id, o.Id)) return true;
        return false;
    }

    /// <summary>가까운 이(20칸 안)가 먹고 있거나 먹으러 가는 식량 설비 — 가장 가까운 사이의 것 (없으면 −1).</summary>
    private int KinMeal(Agent a)
    {
        if (_soc == null) return -1;
        int best = -1; float bw = 0;
        foreach (var o in Agents)
        {
            if (o == a || !o.IsPerson || o.C.Up == 0 || o.C.Goal != GoalKind.Eat || Manhattan(a.C.Cell, o.C.Cell) > 20) continue;
            float k = Kin(a.Id, o.Id);
            if (k <= bw) continue;
            int src = -1;
            foreach (var st in o.Plan) if (st.Kind == StepKind.Use && (UseKind)st.Arg == UseKind.Eat) src = st.Target;
            if (src < 0 || FoodAt(src) < 1) continue;
            bw = k; best = src;
        }
        return best;
    }

    /// <summary>먹을 자리: 가까운 이가 먹는 곳 → 아니면 벌어진 무리 사람이 없는 가장 가까운 곳 → 아니면 가장 가까운 곳.</summary>
    private int MealSource(Agent a, int here)
    {
        int kin = KinMeal(a);
        if (kin >= 0) { AteTogether++; return kin; }
        int calm = Nearest(here, _food, fu => FoodAt(fu) >= 1 && !RivalNear(a, L.Furniture[fu].Cells[0]));
        return calm >= 0 ? calm : Nearest(here, _food, fu => FoodAt(fu) >= 1);
    }

    /// <summary>따라 나가기: 이 방에서 빠져나가는 가까운 이 가운데 가장 가까운 사이 — 점수(없으면 0)와 그가 피하는 위험 칸.</summary>
    private float FollowOut(Agent a, int room, out int danger)
    {
        danger = -1;
        if (_soc == null || room < 0) return 0;
        float best = 0;
        foreach (var o in Agents)
        {
            // 이 방에서 빠져나가는 중 (방금 문을 나섰어도 — 보이는 거리 12칸 안)
            if (o == a || !o.IsPerson || o.C.Up == 0 || o.C.Goal != GoalKind.Evacuate || o.C.GoalTarget != room || Manhattan(o.C.Cell, a.C.Cell) > 12) continue;
            float k = Kin(a.Id, o.Id);
            if (k <= 0) continue;
            float s = 2.5f + 2.5f * k;
            if (s > best) { best = s; danger = o.C.GoalCell; }
        }
        return best;
    }
}
