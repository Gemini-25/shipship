namespace ShipEngine;

/// <summary>숨긴 실수 하나 — 그 사람만 안다.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct HiddenMistake
{
    public int Cause;    // 실수의 원인 마디
    public int Who;      // 한 사람 (에이전트)
    public int Device;   // 손댄 장치
    public long Tick;
}

/// <summary>
/// ⑨ 숨긴 실수 (깊이 바퀴 2 — ⑫ 비밀 씨앗): 손보다 부품을 상하게 하고 말하지 않은 사람만 안다 (사실 Mistake · 크기 2).
/// 그 장치가 망가지면 (고장의 원인 사슬에 그 실수가 있다) 그 사람이 제 탓인 줄 안다 — 마음이 무겁다 (긴장 · 슬픔) · 털어놓을지 숨길지는 그 사람이:
/// 성실 × (0.5 + 0.5 × 다정) − 0.2 × 신경성 (± 0.15)이 0.35를 넘으면 털어놓는다 — 배 사람들은 조금 원망하지만 정직함을 더 믿는다 · 마음이 가벼워진다.
/// 숨기면 제 탓의 기억과 무거운 마음이 남는다 (사람들은 모른다 — 관객만 본다).
/// </summary>
public sealed partial class Society
{
    public readonly List<HiddenMistake> Hidden = new();
    public long Confessions, SecretsKept;

    private void Reveal(Sim sim, in Fact f)
    {
        var chain = sim.Causes.Chain(f.Cause);
        for (int i = Hidden.Count - 1; i >= 0; i--)
        {
            var h = Hidden[i];
            if (!chain.Contains(h.Cause)) continue;
            Hidden.RemoveAt(i);
            int x = h.Who;
            if (!Person(x)) continue;
            ref var c = ref C(x);
            c.Stress = Math.Min(1, c.Stress + 0.25f); c.Sad = Math.Min(1, c.Sad + 0.1f);
            string dev = _net != null && h.Device >= 0 && h.Device < _net.Devices.Count ? _net.Devices[h.Device].Name : "장치";
            float honest = c.Consc * (0.5f + 0.5f * c.Agree) - 0.2f * c.Neuro + 0.3f * (Rnd(sim, "털어놓기", x, h.Device) - 0.5f);
            if (honest > 0.35f)
            {
                Confessions++;
                int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(_mind!.NameOf(x))} 털어놓았다 — {Ko.Eul(dev)} 손보다 상하게 한 것이 자기였다고", sim.Tick, c.Cell, f.Cause, 0, x);
                sim.Facts.Emit(sim.Tick, FactKind.Confessed, c.Cell, 1, cause, x, h.Device);
                for (int o = 0; o < N; o++)
                {
                    if (o == x || !Person(o)) continue;
                    Blame(sim, o, x, 0.1f, cause, c.Cell);
                    Add(TrustOf, o, x, 0.12f);                     // 정직함을 더 믿는다 (원망이 덜어 간 0.03보다 크게)
                    Remember(sim, o, Topic.Work, x, cause, 0.4f, c.Cell);
                }
                c.Stress = Math.Max(0, c.Stress - 0.15f);           // 털어놓으니 가볍다
            }
            else
            {
                SecretsKept++;
                int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.Eun(_mind!.NameOf(x))} 말하지 않았다 — {Ko.I(dev)} 망가진 것이 제 탓인 줄 안다", sim.Tick, c.Cell, f.Cause, 0, x);
                sim.Facts.Emit(sim.Tick, FactKind.Mistake, c.Cell, 3, cause, x, h.Device);
                Remember(sim, x, Topic.Blame, x, cause, 0.7f, c.Cell);   // 제 탓의 기억
                c.Stress = Math.Min(1, c.Stress + 0.1f);
            }
        }
    }

    private void SaveSecrets(StateWriter w) { w.List(Hidden); w.I64(Confessions); w.I64(SecretsKept); }
    private void LoadSecrets(StateReader r)
    {
        Hidden.Clear(); Hidden.AddRange(r.List<HiddenMistake>()); Confessions = r.I64(); SecretsKept = r.I64();
    }
}
