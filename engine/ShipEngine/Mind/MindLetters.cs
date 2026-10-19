namespace ShipEngine;

/// <summary>
/// ⑧ 고향 편지를 받아들이기 (⑫ 자연 사건 — 편지는 ⑨가 · 받아들이는 것은 그 사람의 마음): 기쁜 소식은 기쁨 · 슬픔과 긴장이 풀리고 · 고립의 피로가 덜어지고 ·
/// 슬픈 소식 · 부고는 슬픔과 긴장이 오른다 (신경이 예민할수록 크게) · 별일 없다는 안부도 조금 위로가 된다. 집이 그리운 이(인생 목표 '집')는 더 크게 받는다.
/// </summary>
public sealed partial class Mind
{
    private long _letterCursor;
    public long LettersRead;

    private void LetterStep(Sim sim)
    {
        foreach (var f in sim.Facts.Since(_letterCursor))
        {
            if (f.Kind != FactKind.Letter || f.Actor < 0 || f.Actor >= Agents.Count || !Agents[f.Actor].IsPerson || !PersonAlive(f.Actor)) continue;
            ref var c = ref Agents[f.Actor].C;
            float home = (Aim)c.Aim == Aim.Home ? 1.5f : 1, hurt = (0.7f + 0.6f * c.Neuro) * home;
            switch ((int)f.Size)
            {
                case 2: c.Joy = Math.Min(1, c.Joy + 0.5f * home); c.Sad = Math.Max(0, c.Sad - 0.3f); c.Stress = Math.Max(0, c.Stress - 0.2f); c.Weary = Math.Max(0, c.Weary - 0.15f); break;
                case 1: c.Joy = Math.Min(1, c.Joy + 0.3f * home); c.Sad = Math.Max(0, c.Sad - 0.15f); c.Stress = Math.Max(0, c.Stress - 0.1f); c.Weary = Math.Max(0, c.Weary - 0.08f); break;
                case 0: c.Joy = Math.Min(1, c.Joy + 0.08f); c.Weary = Math.Max(0, c.Weary - 0.04f); break;
                case -1: c.Sad = Math.Min(1, c.Sad + 0.3f * hurt); c.Stress = Math.Min(1, c.Stress + 0.15f * hurt); break;
                default: c.Sad = Math.Min(1, c.Sad + 0.5f * hurt); c.Stress = Math.Min(1, c.Stress + 0.2f * hurt); c.Joy = Math.Max(0, c.Joy - 0.3f); break;
            }
            LettersRead++;
        }
        _letterCursor = sim.Facts.Total;
    }

    private void SaveLetters(StateWriter w) { w.I64(_letterCursor); w.I64(LettersRead); }
    private void LoadLetters(StateReader r) { _letterCursor = r.I64(); LettersRead = r.I64(); }
}
