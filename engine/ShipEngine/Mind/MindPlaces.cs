namespace ShipEngine;

/// <summary>
/// ⑧ 장소의 기억 (깊이 바퀴 2). 무서운 일을 겪은 방(겁 0.45 넘게 · 아픔 0.5 넘게)은 그 사람이 한동안 꺼린다 —
/// 꺼림 = 겁 × (0.6 + 0.4 × 신경성) (아픔이면 0.2 + 0.5 × 아픔). 쉬는 자리 · 어울릴 자리 · 먹을 자리로 덜 고르고 · 그 방에서 우두커니 머물지 않는다
/// (지나가거나 일하러는 간다 — 일터는 일터다). 시간이 지나면 흐려지고(반감기 사흘 × (0.5 + 신경성)) ·
/// 그 방에서 별일 없이 지내면 더 빨리 풀린다 (노출 — 겁 없이 머문 한 시간에 0.1). 공포 조건화와 소거 · 외상 뒤 회피 연구의 경향.
/// </summary>
public sealed partial class Mind
{
    private float[] _dread = Array.Empty<float>();   // [사람 × 방] 꺼림 (0~1)
    public long Dreads;                               // 방을 꺼리게 된 일 (처음 0.4를 넘은 때)

    /// <summary>그 사람이 그 방을 꺼리는 정도 (0~1).</summary>
    public float Dread(int agent, int room)
    {
        int nr = L.Rooms.Count;
        return room < 0 || agent < 0 || (long)agent * nr + room >= _dread.Length ? 0 : _dread[agent * nr + room];
    }

    private float DreadAt(Agent a, int cell) => cell < 0 ? 0 : Dread(a.Id, RoomOf(cell));

    /// <summary>1분마다: 겁 · 아픔이 그 방에 새겨지고 · 흐려지고 · 별일 없이 머물면 풀린다.</summary>
    private void Places(Sim sim)
    {
        int nr = L.Rooms.Count;
        if (nr == 0) return;
        if (_dread.Length < Agents.Count * nr) Array.Resize(ref _dread, Agents.Count * nr);
        const double h = 1.0 / 60;
        foreach (var a in Agents)
        {
            if (!a.IsPerson) continue;
            ref var c = ref a.C;
            ref var b = ref _life![c.Body];
            if (b.Alive == 0) continue;
            int at = a.Id * nr, room = RoomOf(c.Cell);
            double keep = Math.Exp(-h * 0.6931 / (72 * (0.5 + c.Neuro)));
            for (int r = 0; r < nr; r++) if (_dread[at + r] > 0) _dread[at + r] = (float)(_dread[at + r] * keep);
            if (room < 0) continue;
            float d = 0;
            if (c.Fear > 0.45f) d = c.Fear * (0.6f + 0.4f * c.Neuro);
            if (b.Pain > 0.5f) d = Math.Max(d, 0.2f + 0.5f * b.Pain);
            ref float cur = ref _dread[at + room];
            if (d > cur)
            {
                if (cur < 0.4f && d >= 0.4f) Dreads++;
                cur = Math.Min(1, d);
            }
            else if (cur > 0 && c.Fear < 0.1f && b.Conscious != 0 && b.Asleep == 0) cur = (float)Math.Max(0, cur - h * 0.1); // 별일 없이 머문다 — 풀린다
        }
    }

    private void SavePlaces(StateWriter w) { w.Arr(_dread); w.I64(Dreads); }
    private void LoadPlaces(StateReader r) { _dread = r.Arr<float>(); Dreads = r.I64(); }
}
