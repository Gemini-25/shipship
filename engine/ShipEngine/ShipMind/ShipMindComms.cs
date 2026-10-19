namespace ShipEngine;

/// <summary>원격 진료 하나: 누구 · 보낸 때 · 답이 올 때 · 보낸 생체 신호 · 진단 장치 기록이 있었나 · 보낸 원인.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct TeleQuery
{
    public int Agent, Body, Cause;
    public long Sent, Due, ChartAt;
    public Ail Sent_;   // 보낸 생체 신호의 병 (세상 — 고향에 닿는 것)
    public byte Scan;
}

/// <summary>
/// ⑩ 주컴퓨터 — 고향과의 통신 (⑤ 통신 지연 · docs/ENGINE.md ⑤): 배에서 본 무거운 병이면 증상 기록 · 생체 신호를 고향 관제 의사에게 보낸다 (판단) ·
/// 답은 빛의 왕복 + 읽는 20분 뒤 (세상) — 오면 진료 기록을 고치거나 확인한다. 태양 합(고향에서 본 배가 별 곁 2° 안)이면 보내지도 받지도 못한다 — 들고 날 때 방송.
/// </summary>
public sealed partial class ShipMind
{
    public const double HomeReadMinutes = 20;
    public List<TeleQuery> Tele = new();
    private readonly Dictionary<int, long> _teleChart = new();   // 사람마다 마지막으로 보낸 진료 기록 (그 기록의 때)
    public long TeleSent, TeleAnswered, TeleChanged;
    public byte InConjunction;

    /// <summary>고향과 이어졌나: 항해에 나선 배 · 안테나가 돈다 · 태양 합이 아니다.</summary>
    private bool HomeLink(long tick) => _out != null && _out.Route > 0 && _sense != null && _sense.Up && !_out.Conjunction(tick);

    private void Comms(Sim sim)
    {
        if (_out == null || _mind == null || _life == null || sim.Tick % 25 != 11) return;
        long t = sim.Tick;
        // 태양 합에 들고 난다 (방송 · 원인 마디)
        bool conj = _out.Route > 0 && _out.Conjunction(t);
        if (conj != (InConjunction != 0))
        {
            InConjunction = (byte)(conj ? 1 : 0);
            string line = conj ? $"태양 합 — 고향에서 본 배가 별 곁 {_out.HomeSepDeg(t):0.0}°에 들어 통신이 끊긴다 (편지 · 원격 진료는 기다린다)" : "태양 합이 끝났다 — 고향과 다시 이어졌다";
            sim.Causes.Add(CauseKind.Route, line, t);
            Say(sim, line);
        }
        bool link = HomeLink(t);
        double oneWay = _out.HomeLightMinutes(t);
        // 무거운 증상의 진료 기록을 보낸다 (한 기록에 한 번)
        if (link)
            foreach (var a in _mind.Agents)
            {
                if (!a.IsPerson || a.C.Up == 0 && _life[a.C.Body].Alive == 0) continue;
                ref var c = ref a.C;
                if (c.DxAt <= 0 || c.DxBy == -1 || (c.DxSym & Sym.Serious) == 0 || t - c.DxAt > Time.Hours(12)) continue;
                if (_teleChart.TryGetValue(a.Id, out long sentAt) && sentAt >= c.DxAt) continue;
                if (Tele.Any(q => q.Agent == a.Id)) continue;
                _teleChart[a.Id] = c.DxAt;
                int dxRoom = c.DxRoom;
                bool scan = dxRoom >= 0 && L.Furniture.Any(f => f.Room == dxRoom && f.Type == "DiagnosticScanner");
                string name = _mind.NameOf(a.Id);
                int cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {name}의 진료 기록({Life.AilName(c.DxAil)} — {Life.SymText(c.DxSym)})을 고향 관제 의사에게 보냈다 — 빛으로 편도 {oneWay:0}분", t, -1, c.DxCause);
                Tele.Add(new TeleQuery { Agent = a.Id, Body = c.Body, Cause = cause, Sent = t, Due = t + Time.Minutes(2 * oneWay + HomeReadMinutes), ChartAt = c.DxAt, Sent_ = _life.Telemetry(c.Body), Scan = (byte)(scan ? 1 : 0) });
                TeleSent++;
                Say(sim, $"{name}의 진료 기록을 고향 관제 의사에게 보냈다 — 답은 빛으로 왕복 {2 * oneWay:0}분 뒤");
            }
        // 답이 온다 (이어졌을 때만 — 합이면 기다린다)
        for (int i = Tele.Count - 1; i >= 0; i--)
        {
            var q = Tele[i];
            if (q.Due > t || !link) continue;
            Tele.RemoveAt(i);
            TeleAnswered++;
            Ail ans = _life.HomeDx(sim, q.Sent_, q.Scan != 0, q.Body);
            string name = _mind.NameOf(q.Agent);
            double rt = (t - q.Sent) * 60.0 / Time.TicksPerHour;
            int cause = sim.Causes.Add(CauseKind.Route, $"고향 관제 의사가 {Ko.Eul(name)} {Ko.Euro(Life.AilName(ans))} 보았다 — 빛으로 왕복 · 읽기까지 {rt:0}분", t, -1, q.Cause);
            bool changed = _mind.RemoteChart(sim, q.Agent, ans, cause);
            if (changed) TeleChanged++;
            Say(sim, changed ? $"고향 관제 의사: {Ko.I(name)} {Ko.Was(Life.AilName(ans))} — 진료 기록을 고쳤다 ({rt:0}분 만의 답)" : $"고향 관제 의사도 {Ko.I(name)} {Ko.Euro(Life.AilName(ans))} 보았다 ({rt:0}분 만의 답)");
        }
    }

    private void SaveComms(StateWriter w)
    {
        w.List(Tele); w.I64(TeleSent); w.I64(TeleAnswered); w.I64(TeleChanged); w.Byte(InConjunction);
        w.I32(_teleChart.Count); foreach (var kv in _teleChart.OrderBy(k => k.Key)) { w.I32(kv.Key); w.I64(kv.Value); }
    }
    private void LoadComms(StateReader r)
    {
        Tele = r.List<TeleQuery>(); TeleSent = r.I64(); TeleAnswered = r.I64(); TeleChanged = r.I64(); InConjunction = r.Byte();
        _teleChart.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { int k = r.I32(); _teleChart[k] = r.I64(); }
    }
}
