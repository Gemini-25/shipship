namespace ShipEngine;

/// <summary>
/// ⑧ 항해 달력을 받아들이기 (⑫ 긴 줄기 (다) — 알리는 것은 주컴퓨터 · 받아들이는 것은 그 사람의 마음): 깨어 들은 이만.
/// 다가오는 파편 구간 소식에 긴장 (예민할수록 · 겁이 많을수록) · 들 때 한 번 더 · 무사히 빠져나오면 긴장이 풀리고 기쁨 ·
/// 태양 합 예고에는 집이 그리운 이(인생 목표 '집')가 시무룩 · 합이 끝나면 반갑다.
/// </summary>
public sealed partial class Mind
{
    private long _aheadCursor;
    public long AheadFelt;

    private void AheadStep(Sim sim)
    {
        foreach (var f in sim.Facts.Since(_aheadCursor))
        {
            if (f.Kind != FactKind.Ahead) continue;
            foreach (var a in Agents)
            {
                if (!a.IsPerson || a.C.Up == 0 || !PersonAlive(a.Id)) continue;
                ref var c = ref a.C;
                float nerve = (0.5f + c.Neuro) * (1.3f - 0.6f * c.Brave), home = (Aim)c.Aim == Aim.Home ? 1 : 0.2f;
                if (f.Other == 0)
                {
                    if (f.Size > 0) c.Stress = Math.Min(1, c.Stress + 0.06f * nerve);
                    else if (f.Size == 0) c.Stress = Math.Min(1, c.Stress + 0.04f * nerve);
                    else { c.Stress = Math.Max(0, c.Stress - 0.15f); c.Joy = Math.Min(1, c.Joy + 0.15f); }
                }
                else
                {
                    if (f.Size > 0) c.Sad = Math.Min(1, c.Sad + 0.1f * home);
                    else if (f.Size < 0) c.Joy = Math.Min(1, c.Joy + 0.1f * home);
                }
                AheadFelt++;
            }
        }
        _aheadCursor = sim.Facts.Total;
    }

    private void SaveAhead(StateWriter w) { w.I64(_aheadCursor); w.I64(AheadFelt); }
    private void LoadAhead(StateReader r) { _aheadCursor = r.I64(); AheadFelt = r.I64(); }
}
