namespace ShipEngine;

/// <summary>
/// ⑩ 새는 관 찾기 (⑥ 바퀴 3 (라) · docs/ENGINE.md ⑥ '바퀴 3 설계'). 주컴퓨터는 관망을 채우는 흐름 계량으로 '어딘가 샌다'를 안다 — 자리는 모른다.
/// 밸브로 좁힌다: 후보 방들 가운데 잠그면 물줄기 아래쪽이 후보의 절반쯤 되는 방의 밸브를 잠가 2분 기다린다 → 샘이 반 넘게 줄면 그 아래쪽에 · 아니면 나머지에 —
/// 방 하나가 남을 때까지 (방 n개면 log₂n번). 그 방 관을 따라 로봇을 보내 칸마다 살펴 찾는다 (젖은 바닥 · 쉬익 · 냄새 — ⑧이 알아본다) → 찾으면 '선이 끊겼다'로 죈다.
/// </summary>
public sealed partial class ShipMind
{
    private sealed class Hunt
    {
        public byte Active;
        public int Test = -1, Found = -1, Next = -1;
        public long Since, TestAt;
        public double Base;
        public List<int> Cand = new();
        public List<int> Visited = new();
    }
    private readonly Hunt[] _hunt = { new(), new() };   // 냉각관 · 물관
    public long HuntsStarted, HuntsFound, ValveTests;
    public const double HuntMolPerSec = 0.004;           // 이만큼 넘게 모르게 새면 찾는다 (물이면 시간에 0.26L)

    private static LineKind HuntKind(int s) => s == 0 ? LineKind.Coolant : LineKind.Water;
    private static string PipeName(int s) => s == 0 ? "냉각관" : "물관";

    /// <summary>읽기마다: 모르는 샘이 있으면 밸브로 좁히고 · 찾은 방으로 로봇을 보낸다 (Detect 안에서 — 사건으로 올린다).</summary>
    private void PipeHunt(Sim sim, Action<IncidentKind, int, int> mark)
    {
        if (_net == null) return;
        for (int s = 0; s < 2; s++)
        {
            var h = _hunt[s];
            var k = HuntKind(s);
            double rate = _net.UnknownLeakRate(k);
            if (h.Active == 0)
            {
                if (rate < HuntMolPerSec) continue;
                h.Active = 1; h.Since = sim.Tick; h.Test = -1; h.Found = -1; h.Next = -1; h.Visited.Clear();
                h.Cand = _net.PipeRooms(k);
                HuntsStarted++;
                Say(sim, $"{PipeName(s)} 어딘가가 샌다 (시간에 {rate * 3600 * (s == 0 ? 0.017 : 0.018):0.0}{(s == 0 ? "kg" : "L")}) — 자리를 모른다 · 밸브를 잠가 가며 좁힌다 (방 {h.Cand.Count}개)");
            }
            // 찾았다 (그 칸을 누가 알아봤다 — 이제 아는 샘이라 '선이 끊겼다'로 죈다) · 또는 저절로 멎었다
            // (밸브를 잠가 보는 동안 준 것은 멎은 것이 아니다 — 시험이 끝나야 읽는다)
            if (h.Test < 0 && rate < HuntMolPerSec * 0.5)
            {
                if (h.Test >= 0) _net.SetValve(h.Test, k, false);
                Say(sim, h.Found >= 0 ? $"{PipeName(s)} 새는 자리를 찾았다 — {RoomName(h.Found)}" : $"{PipeName(s)} 샘이 멎었다");
                if (h.Found >= 0) HuntsFound++;
                h.Active = 0; h.Test = -1;
                continue;
            }
            if (h.Found < 0)
            {
                if (h.Cand.Count <= 1)
                {
                    h.Found = h.Cand.Count == 1 ? h.Cand[0] : -1;
                    if (h.Found < 0) { h.Cand = _net.PipeRooms(k); continue; }   // 다 걸렀는데 없다 — 처음부터
                    Say(sim, $"{PipeName(s)} 샘은 {RoomName(h.Found)}에 있다 — 로봇을 보내 관을 따라 찾는다");
                }
                else if (h.Test < 0)
                {
                    // 잠그면 아래쪽이 후보의 절반에 가장 가까운 방
                    int best = -1; double bd = double.MaxValue; HashSet<int>? bestDown = null;
                    foreach (int r in h.Cand)
                    {
                        var down = _net.Downstream(k, r);
                        int inCand = h.Cand.Count(c => down.Contains(c));
                        if (inCand == 0 || inCand == h.Cand.Count) continue;
                        double d = Math.Abs(inCand - h.Cand.Count / 2.0);
                        if (d < bd) { bd = d; best = r; bestDown = down; }
                    }
                    if (best < 0) { h.Found = h.Cand[^1]; continue; }   // 더 가를 수 없다 — 물줄기 끝 방부터
                    _net.SetValve(best, k, true);
                    h.Test = best; h.TestAt = sim.Tick; h.Base = rate; ValveTests++;
                    Say(sim, $"{RoomName(best)} {PipeName(s)} 밸브를 잠가 본다 (그 너머 방 {bestDown!.Count(c => h.Cand.Contains(c))}개)");
                }
                else if (sim.Tick - h.TestAt >= Time.Minutes(2))
                {
                    var down = _net.Downstream(k, h.Test);
                    bool stopped = rate < h.Base * 0.5;
                    h.Cand = h.Cand.Where(c => down.Contains(c) == stopped).ToList();
                    _net.SetValve(h.Test, k, false);
                    Say(sim, stopped ? $"{RoomName(h.Test)} 밸브를 잠그니 샘이 줄었다 — 그 너머에 있다 (남은 방 {h.Cand.Count}개)" : $"{RoomName(h.Test)} 밸브를 잠가도 그대로 — 다른 쪽에 있다 (남은 방 {h.Cand.Count}개)");
                    h.Test = -1;
                }
            }
            if (h.Found >= 0)
            {
                // 그 방 관 칸을 차례로 (아직 안 본 칸 — 주컴퓨터에서 가까운 것부터) · 로봇이 2칸 안에 가면 본 것으로
                var cells = _net.PipeCells(k, h.Found);
                foreach (var a in _mind!.Agents) if (!a.IsPerson && a.C.Up != 0) foreach (int c in cells) if (!h.Visited.Contains(c) && Math.Abs(L.X(a.C.Cell) - L.X(c)) + Math.Abs(L.Y(a.C.Cell) - L.Y(c)) <= 2) h.Visited.Add(c);
                h.Next = cells.FirstOrDefault(c => !h.Visited.Contains(c), -1);
                if (h.Next < 0) { h.Visited.Clear(); h.Next = cells.Count > 0 ? cells[0] : -1; }
                if (h.Next >= 0) { Raise(sim, IncidentKind.PipeHunt, h.Found, h.Next, (float)rate, s); mark(IncidentKind.PipeHunt, h.Found, s); }
            }
        }
    }

    private void SaveHunt(StateWriter w)
    {
        foreach (var h in _hunt)
        {
            w.Byte(h.Active); w.I32(h.Test); w.I32(h.Found); w.I32(h.Next); w.I64(h.Since); w.I64(h.TestAt); w.F64(h.Base);
            w.Arr(h.Cand.ToArray()); w.Arr(h.Visited.ToArray());
        }
        w.I64(HuntsStarted); w.I64(HuntsFound); w.I64(ValveTests);
    }

    private void LoadHunt(StateReader r)
    {
        foreach (var h in _hunt)
        {
            h.Active = r.Byte(); h.Test = r.I32(); h.Found = r.I32(); h.Next = r.I32(); h.Since = r.I64(); h.TestAt = r.I64(); h.Base = r.F64();
            h.Cand = r.Arr<int>().ToList(); h.Visited = r.Arr<int>().ToList();
        }
        HuntsStarted = r.I64(); HuntsFound = r.I64(); ValveTests = r.I64();
    }
}
