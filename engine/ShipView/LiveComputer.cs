using ShipEngine;

namespace ShipView;

/// <summary>
/// 주컴퓨터 자세히 (docs/UI2.md §4.4 — U7 · 화면만 · 세계를 읽기만): 머리 상태 한 줄 · 작은 탭 여섯 —
/// 지금 (J 판 줄 그대로) · 예측 (U8) · 아는 것 (방마다 센서로 아는 값과 실제 · 모르는 일 · 틀린 앎 — 화면 0.5초에 한 번 셈해 둔다) · 일감 · 말 기록 · 몸 · 방침.
/// 함장 화면은 참을 보인다 (관객) — 주컴퓨터가 아는 것은 따로 적고 차이를 보인다 (§0-6).
/// </summary>
public sealed partial class LiveShow
{
    /// <summary>주컴퓨터 자세히의 줄: 고른 작은 탭.</summary>
    private void ComputerRows(List<PanelRow> rows)
    {
        var w = World; var sm = w.Find<ShipMind>();
        if (sm == null) { rows.Add(new PanelRow("이 배에는 주컴퓨터가 없다", "", false, Key: -1)); return; }
        switch (DetailSub)
        {
            case 0: JournalRows(rows); break;   // 지금 — J 판 줄 그대로 (옛 시험이 그대로 통과한다)
            case 1: rows.Add(new PanelRow("곧 — 주컴퓨터가 한 예측과 실제 · 맞음 몫", "그려 보는 중 " + sm.Thinking + " · 봉쇄 상상 · 미리 돌려 보기 · 걸음 시간 · 물자 · 대피를 실제와 맞댄다", false, Key: -1)); break;
            case 2: KnowsTable(w, sm, rows); break;
            case 3: ChoreRows(w, sm, rows); break;
            case 4: SayRows(w, sm, rows); break;
            default: SelfRows(w, sm, rows); break;
        }
    }

    /// <summary>주컴퓨터 머리의 둘째 줄: '켜짐 · 센서 12초마다 · 빨아들이는 공기 31℃ · 붙잡은 사건 2 · 그려 보는 중 1 · 못 보는 방 1'.</summary>
    private static string ComputerHeadLine(Sim w)
    {
        var sm = w.Find<ShipMind>();
        if (sm == null) return "이 배에는 주컴퓨터가 없다";
        var bits = new List<string> { sm.HeatStopped != 0 ? "열 때문에 멈췄다" : sm.Online ? "켜짐" : "멈췄다" };
        if (sm.Online) bits.Add($"센서 {ReadEverySec(sm):0}초마다{(sm.Throttled != 0 ? " (열 때문에 느려졌다)" : "")}");
        if (sm.IntakeK > 0) bits.Add($"빨아들이는 공기 {sm.IntakeK - 273.15:0}℃");
        int open = sm.Incidents.Count(x => x.Active != 0);
        bits.Add(open > 0 ? $"붙잡은 사건 {open}" : "붙잡은 사건 없음");
        if (sm.Thinking > 0) bits.Add($"그려 보는 중 {sm.Thinking}");
        int blind = 0;
        for (int r = 0; r < w.Layout.Rooms.Count && r < sm.RoomSeen.Length; r++) if (w.Layout.Rooms[r].Cells.Length > 0 && sm.RoomSeen[r] == 0) blind++;
        if (blind > 0) bits.Add($"못 보는 방 {blind}");
        return string.Join(" · ", bits);
    }

    /// <summary>센서를 읽는 틈 (초): 12초 · 열로 느려지면 세 배.</summary>
    private static double ReadEverySec(ShipMind sm) => ShipMind.ReadEvery * (sm.Throttled != 0 ? 3 : 1) * Time.SecondsPerTick;

    // ───────────────────────── 아는 것 (센서로 아는 값과 실제) ─────────────────────────

    private readonly List<PanelRow> _knows = new();
    private long _knowsAt, _knowsTick, _knowsSig;
    private Sim? _knowsOf;

    /// <summary>'아는 것' 표 — 방마다 칸을 다 읽는 셈이라 화면 <see cref="KnowsEverySec"/>초에 한 번 다시 셈해 둔다 (한누리 0.3 ~ 0.8ms).</summary>
    private void KnowsTable(Sim w, ShipMind sm, List<PanelRow> rows)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long sig = sm.Incidents.Count(x => x.Active != 0) + (sm.Online ? 1L << 20 : 0);   // 못 보는 방 · 사건 · 켜짐이 바뀌면 곧 (못 보는 방 · 모르는 일이 머리 줄과 어긋나지 않게)
        for (int r = 0; r < sm.RoomSeen.Length; r++) sig = sig * 31 + sm.RoomSeen[r];
        if (_knowsOf != w || _knows.Count == 0 || KnowsEverySec <= 0 || sig != _knowsSig && now - _knowsAt >= KnowsEverySec * System.Diagnostics.Stopwatch.Frequency / 4
            || w.Tick != _knowsTick && now - _knowsAt >= KnowsEverySec * System.Diagnostics.Stopwatch.Frequency)   // 멈춰 있으면 다시 셈하지 않는다
        {
            _knows.Clear(); KnowsNow(w, sm, _knows);
            _knowsAt = now; _knowsOf = w; _knowsTick = w.Tick; _knowsSig = sig;
        }
        rows.AddRange(_knows);
    }

    /// <summary>방 하나의 참값 — 주컴퓨터 센서와 같은 셈 (방 평균 기압 · 가장 옅은 산소 · 공기 온도 · 가장 짙은 일산화탄소 · 방 평균 이산화탄소).</summary>
    private static (double kpa, double o2, double airT, double co, double co2) RoomTruth(Sim w, Field f, int[] cells)
    {
        var T = w.Tables;
        double p = 0, o2 = double.MaxValue, co = 0, co2 = 0, n = 0, nt = 0, na = 0;
        foreach (int c in cells)
        {
            double m = f.Read(c, out double gc, out double pc);
            p += pc;
            o2 = Math.Min(o2, m > 1e-12 ? pc * f.Gas(T.gO2, c) / m : 0);
            if (m > 1e-9) { co = Math.Max(co, f.Gas(T.gCO, c) / m * 1e6); co2 += f.Gas(T.gCO2, c) / m * 1e6 * m; }
            double tg = f.Temp(c, gc);
            n += m; if (pc > 5) { nt += m * tg; na += m; }
        }
        return (p / Math.Max(1, cells.Length), o2 == double.MaxValue ? 0 : o2, na > 0 ? nt / na : 0, co, n > 0 ? co2 / n : 0);
    }

    private void KnowsNow(Sim w, ShipMind sm, List<PanelRow> rows)
    {
        var L = w.Layout; var f = w.Find<Field>(); var net = w.Find<Network>(); var ch = w.Find<Chemistry>(); var life = w.Find<Life>(); var mind = w.Find<Mind>();
        if (f == null) return;
        // 방마다: 못 봄 · 어긋남이 큰 방이 위
        var roomRows = new List<(double rank, PanelRow row)>();
        double readSec = ReadEverySec(sm);
        for (int r = 0; r < L.Rooms.Count && r < sm.RoomSeen.Length; r++)
        {
            var cells = L.Rooms[r].Cells;
            if (cells.Length == 0) continue;
            var (kpa, o2, airT, co, co2) = RoomTruth(w, f, cells);
            string name = sm.RoomName(r);
            string truth = $"실제: 기압 {kpa:0.0}kPa · 산소 {o2:0.0}kPa · 온도 {airT - 273.15:0}℃ · 일산화탄소 {co:0}ppm · 이산화탄소 {co2:0}ppm";
            if (sm.RoomSeen[r] == 0 || !sm.Online)
            {
                string why = !sm.Online ? "주컴퓨터가 멈췄다" : net != null && !net.RoomPowered(r) ? "전원이 없다" : "센서 선이 닿지 않는다";
                roomRows.Add((1e9, new PanelRow($"{name} — 못 본다 ({why})", truth, true, Key: DK(r))));
                continue;
            }
            bool radio = sm.RoomSeen[r] == 2;
            int back = radio ? (sm.Reads + r) % ShipMind.RadioEvery : 0;   // 무선 방은 읽기 세 번에 한 번 새 값 — 몇 번 앞에 받았나
            double ago = (w.Tick % (ShipMind.ReadEvery * (sm.Throttled != 0 ? 3 : 1))) * Time.SecondsPerTick + back * readSec;
            double dk = sm.RoomKPa[r] - kpa, dO2 = sm.RoomO2[r] - o2, dT = sm.RoomAirT[r] - airT, dCO = sm.RoomCO[r] - co, dCO2 = sm.RoomCO2[r] - co2;
            double off = Math.Abs(dk) / 1 + Math.Abs(dO2) / 0.5 + Math.Abs(dT) / 3 + Math.Abs(dCO) / 50 + Math.Abs(dCO2) / 1000;   // 1이면 '어긋남이 크다' (기압 1kPa · 산소 0.5kPa · 3℃ · 50ppm · 1000ppm)
            string sub = $"기압 {sm.RoomKPa[r]:0.0}kPa (실제 {kpa:0.0}) · 산소 {sm.RoomO2[r]:0.0}kPa ({o2:0.0}) · 온도 {sm.RoomAirT[r] - 273.15:0}℃ ({airT - 273.15:0}) · 일산화탄소 {sm.RoomCO[r]:0}ppm ({co:0}) · 이산화탄소 {sm.RoomCO2[r]:0}ppm ({co2:0})";
            roomRows.Add((off, new PanelRow($"{name} — {(radio ? "무선으로만 본다" : "선으로 본다")} · {ago:0}초 전{(off >= 1 ? " · 실제와 어긋난다" : "")}", sub, off >= 1, Key: DK(r))));
        }
        foreach (var (_, row) in roomRows.OrderByDescending(x => x.rank)) rows.Add(row);
        // 산소 조절 센서 (발생기의 조절 센서 — 늙으면 낮게 읽는다)
        if (net != null)
            rows.Add(new PanelRow("산소 조절 센서", $"읽는 몫 {net.O2Gain * 100:0}% · 읽음 {net.O2Reading:0.0}kPa · 실제 {net.PpO2:0.0}kPa{(net.O2Gain < 0.95 ? " — 낮게 읽어 발생기가 더 만든다" : "")}", net.O2Gain < 0.95, Key: DK(1000)));
        // 모르는 일: 실제로 일어났는데 사건이 없는 것 · 틀린 앎: 사건은 이어지는데 실제는 끝난 것
        bool Has(IncidentKind k, int room, int subject = int.MinValue) => sm.Incidents.Any(x => x.Active != 0 && x.Kind == k && (subject == int.MinValue ? x.Room == room : x.Subject == subject));
        var unknown = new List<(string where, string what)>();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            var cells = L.Rooms[r].Cells;
            if (cells.Length == 0) continue;
            bool burning = false, holed = false;
            foreach (int c in cells) { if (ch != null && c < ch.Burning.Length && ch.Burning[c] != 0) burning = true; if (c < f.Hole.Length && f.Hole[c] > 0) holed = true; }
            if (burning && !Has(IncidentKind.Fire, r)) unknown.Add((sm.RoomName(r), "불이 탄다"));
            if (holed && !Has(IncidentKind.Leak, r) && !Has(IncidentKind.Vacuum, r)) unknown.Add((sm.RoomName(r), "구멍이 났다 — 공기가 샌다"));
        }
        if (life != null && mind != null)
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson || a.C.Body < 0) continue;
                var b = life.Bodies[a.C.Body];
                if (b.Alive != 0 && b.Conscious == 0 && !Has(IncidentKind.PersonDown, -1, a.C.Body)) unknown.Add((b.Cell >= 0 && b.Cell < L.N && L.Room[b.Cell] >= 0 ? sm.RoomName(L.Room[b.Cell]) : "통로", $"{mind.NameOf(a.Id)}이(가) 쓰러져 있다"));
            }
        if (net != null)
            foreach (var d in net.Devices)
            {
                bool broken = false;
                for (int p = 0; p < 4; p++) if (net.IsBroken(d.Id, (Part)p)) broken = true;
                if (broken && !Has(IncidentKind.DeviceBroken, -1, d.Id)) unknown.Add((sm.RoomName(d.Room), $"{d.Name}이(가) 멎었다"));
            }
        int k = 0;
        foreach (var (where, what) in unknown) rows.Add(new PanelRow($"모르는 일 — {where}", what + " (주컴퓨터는 아직 모른다)", true, Key: DK(2000 + k++)));
        k = 0;
        foreach (var x in sm.Incidents)
        {
            if (x.Active == 0 || x.Room < 0 || x.Room >= L.Rooms.Count) continue;
            var cells = L.Rooms[x.Room].Cells;
            string wrong = "";
            switch (x.Kind)
            {
                case IncidentKind.Fire when ch != null && !cells.Any(c => c < ch.Burning.Length && ch.Burning[c] != 0): wrong = "불로 안다 · 실제는 꺼졌다"; break;
                case IncidentKind.Leak or IncidentKind.Vacuum when !cells.Any(c => c < f.Hole.Length && f.Hole[c] > 0) && cells.Length > 0 && RoomTruth(w, f, cells).kpa > 80: wrong = "공기가 빠진다고 안다 · 실제는 구멍이 없고 기압이 돌아왔다"; break;
                case IncidentKind.PersonDown when life != null && x.Subject >= 0 && x.Subject < life.Bodies.Count && (life.Bodies[x.Subject].Conscious != 0 || life.Bodies[x.Subject].Alive == 0):
                    wrong = life.Bodies[x.Subject].Alive == 0 ? "쓰러졌다고 안다 · 실제는 숨을 거두었다" : "쓰러졌다고 안다 · 실제는 깨어났다"; break;
                case IncidentKind.DeviceBroken when net != null && x.Subject >= 0 && x.Subject < net.Devices.Count && !Enumerable.Range(0, 4).Any(p => net.IsBroken(x.Subject, (Part)p)): wrong = "고장으로 안다 · 실제는 고쳐졌다"; break;
            }
            if (wrong.Length > 0) rows.Add(new PanelRow($"틀린 앎 — {sm.RoomName(x.Room)} {ShipMind.KindName(x.Kind)}", wrong, true, Key: DK(3000 + k++)));
        }
        if (unknown.Count == 0 && k == 0) rows.Add(new PanelRow("모르는 일 · 틀린 앎 없음", "실제로 일어난 큰 일을 주컴퓨터가 다 붙잡고 있다", false, Key: DK(2999)));
        rows.Add(new PanelRow("사람 자리", "사람 표를 참으로 읽는다 — 아직 어림하지 않는다 (못 보는 방의 사람은 모른다)", false, Key: DK(4000)));
        // 바깥: 센서에 잡힌 것과 실제로 다가오는 것
        if (w.Find<Outside>() is { } o)
        {
            var tr = w.Find<Sensors>()?.Tracks;
            int seen = tr?.Count(t => t.Kind is TrackKind.Rock or TrackKind.Cloud) ?? 0, real = o.Rocks.Count + o.Clouds.Count, missed = 0;
            foreach (var rk in o.Rocks) if (tr == null || !tr.Any(t => t.Kind == TrackKind.Rock && t.Id == rk.Id)) missed++;
            foreach (var cl in o.Clouds) if (tr == null || !tr.Any(t => t.Kind == TrackKind.Cloud && t.Id == cl.Id)) missed++;
            rows.Add(new PanelRow("바깥", $"센서에 잡힌 것 {seen} · 실제로 다가오는 것 {real}{(missed > 0 ? $" — 못 잡은 것 {missed}" : "")}", missed > 0, Key: DK(4001)));
        }
    }

    // ───────────────────────── 일감 ─────────────────────────

    /// <summary>일감 한 줄의 대상: 작업대 · 손볼 장치와 부품 · 치울 방.</summary>
    private static string JobWhat(Sim w, ShipMind sm, GoalKind kind, int target)
    {
        var net = w.Find<Network>();
        switch (kind)
        {
            case GoalKind.Craft when net != null && target >= 0 && target < net.Devices.Count: return net.Devices[target].Name + "에서";
            case GoalKind.Maintain when net != null && target >= 0 && target / 4 < net.Devices.Count: return $"{net.Devices[target / 4].Name} {Device.PartName((Part)(target % 4))}";
            case GoalKind.Clean when target >= 0 && target < w.Layout.N: return w.Layout.Room[target] >= 0 ? sm.RoomName(w.Layout.Room[target]) : "통로";
            default: return "";
        }
    }

    private void ChoreRows(Sim w, ShipMind sm, List<PanelRow> rows)
    {
        var mind = w.Find<Mind>(); var net = w.Find<Network>();
        long t = w.Tick;
        long next = t + ((ShipMind.ChoreOffset - t % ShipMind.ChoreEvery) % ShipMind.ChoreEvery + ShipMind.ChoreEvery) % ShipMind.ChoreEvery;
        if (next == t) next += ShipMind.ChoreEvery;
        rows.Add(new PanelRow($"일감판 ({sm.Jobs.Count})", sm.Jobs.Count == 0 ? $"할 일감이 없다 · 다음 일감판 {HourMin(next)}" : $"다음 일감판 {HourMin(next)} (5분마다)", false, Key: DK(0)));
        int k = 0;
        foreach (var j in sm.Jobs)
        {
            string who = j.Agent >= 0 && mind != null && j.Agent < mind.Agents.Count ? mind.NameOf(j.Agent) + "이(가) 맡았다" : "아직 맡은 이가 없다";
            string until = j.Agent >= 0 && j.Until > t ? $" · {Math.Max(1, (j.Until - t) * Time.SecondsPerTick / 60):0}분 안에" : "";
            rows.Add(new PanelRow($"{Mind.GoalName(j.Kind)} — {JobWhat(w, sm, j.Kind, j.Target)}", who + until, false, Key: DK(100 + k++)));
        }
        k = 0;
        for (int i = sm.JobFails.Count - 1; i >= 0 && k < 12; i--)
        {
            var jf = sm.JobFails[i];
            string who = jf.Agent == -2 ? "막힌 곳으로 알렸다" : jf.Agent >= 0 && mind != null && jf.Agent < mind.Agents.Count ? mind.NameOf(jf.Agent) : "맡은 이 없음";
            rows.Add(new PanelRow($"못 한 일감 — {Mind.GoalName(jf.Kind)} {JobWhat(w, sm, jf.Kind, jf.Target)}", $"{HourMin(jf.Tick)} · {who} · {(jf.Snag == SnagKind.None ? "기한을 넘겼다" : Mind.SnagText(jf.Snag))}", true, Key: DK(300 + k++)));
        }
        for (int i = 0; i < sm.Blocked.Count; i++)
            rows.Add(new PanelRow("막힌 곳 — " + sm.Blocked[i], i < sm.BlockedCause.Count && sm.BlockedCause[i] > 0 ? "왜: " + WhyText(sm.BlockedCause[i]) : "", true, Key: DK(500 + i)));
        if (net != null)
        {
            var shed = new List<string>();
            for (int d = 0; d < sm.Shed.Length && d < net.Devices.Count; d++) if (sm.Shed[d] != 0) shed.Add(net.Devices[d].Name);
            if (shed.Count > 0) rows.Add(new PanelRow($"전력 때문에 끈 장치 ({shed.Count})", string.Join(" · ", shed), true, Key: DK(700)));
        }
        var p = sm.Policy;
        string ration = sm.RationFoodOn != 0 || sm.RationWaterOn != 0
            ? string.Join(" · ", new[] { sm.RationFoodOn != 0 ? $"식량 하루 몫 {p.RationShare * 4:0}/4" : "", sm.RationWaterOn != 0 ? $"물 하루 몫 {ShipMind.WaterShare * 100:0}% · 샤워 {ShipMind.ShowerRuleMin}분 · 이틀에 한 번" : "" }.Where(x => x.Length > 0))
            : p.RationBelow > 0 ? $"배급하지 않는다 — 보통으로 먹어 {p.RationBelow:0}일을 못 버티면 연다" : "배급하지 않는다 (함장 방침)";
        rows.Add(new PanelRow("배급", ration + "\n" + sm.ShipLine(w, false), sm.RationFoodOn != 0 || sm.RationWaterOn != 0, Key: DK(800)));
    }

    // ───────────────────────── 말 기록 ─────────────────────────

    private readonly List<PanelRow> _says = new();
    private long _saysAdded = -1;
    private Sim? _saysOf;

    private static string NoteWord(NoteKind k) => k switch
    {
        NoteKind.Assign => "맡김", NoteKind.Defer => "미룸", NoteKind.Release => "놓아줌", NoteKind.Block or NoteKind.Stuck => "막힘", NoteKind.Seal => "봉쇄", NoteKind.Door => "문",
        NoteKind.Vent => "공기", NoteKind.Power => "전력", NoteKind.Plan => "계획", NoteKind.Ask => "부탁", NoteKind.Job => "일감", _ => "말",
    };

    /// <summary>말 기록: 주컴퓨터가 한 말 512 (새것 위 — 새 말이 들어올 때만 다시 짓는다) · 해 본 방법 · 막힌 까닭 · 지어 본 방법.</summary>
    private void SayRows(Sim w, ShipMind sm, List<PanelRow> rows)
    {
        var mind = w.Find<Mind>();
        var notes = sm.Notes;
        long added = notes?.Added ?? 0;
        if (_saysOf != w || _saysAdded != added)
        {
            _says.Clear(); _saysOf = w; _saysAdded = added;
            for (int k = 0; k < (notes?.Count ?? 0); k++)
            {
                var nt = notes!.Newest(k);
                var bits = new List<string> { NoteWord(nt.Kind) };
                if (nt.Robot >= 0 && mind != null && nt.Robot < mind.Agents.Count) bits.Add(mind.NameOf(nt.Robot));
                if (nt.Incident > 0 && sm.Incidents.FirstOrDefault(x => x.Id == nt.Incident) is { Id: > 0 } x) bits.Add($"{sm.RoomName(x.Room)} {ShipMind.KindName(x.Kind)}");
                if (nt.Reason != 0 && ShipMind.ReasonText(nt.Reason) is { Length: > 0 } why) bits.Add(why);
                _says.Add(new PanelRow($"{HourMin(nt.Tick)}  {TextLint.Clean(nt.Text)}", string.Join(" · ", bits), nt.Kind is NoteKind.Block or NoteKind.Stuck, Key: DK(added - 1 - k)));   // 열쇠는 말의 차례 번호 (새 말이 위에 끼어도 고른 줄이 그대로)
            }
        }
        rows.Add(new PanelRow($"주컴퓨터가 한 말 ({notes?.Count ?? 0})", notes == null || notes.Count == 0 ? "아직 한 말이 없다" : "새것 위 · 마지막 512마디", false, Key: -1));
        rows.AddRange(_says);
        int n = 0;
        for (int i = sm.Attempts.Count - 1; i >= 0 && n < 16; i--, n++)
        {
            var at = sm.Attempts[i];
            var x = sm.Incidents.FirstOrDefault(q => q.Id == at.Incident);
            string inc = x.Id > 0 ? $"{sm.RoomName(x.Room)} {ShipMind.KindName(x.Kind)}" : "끝난 사건";
            string how = at.Act == 255 ? ShipMind.MethodName(at.Method) : at.Act == 254 ? "방법을 지어내지 못했다" : sm.StepText(new PlanStepRec { Act = at.Act, Obj = at.Obj, Dest = at.Obj });
            rows.Add(new PanelRow($"해 본 방법 — {inc}: {how}", $"{HourMin(at.Tick)} · {(at.Snag == SnagKind.None ? "막히지 않았다" : "막힌 까닭: " + Mind.SnagText(at.Snag))}", at.Snag != SnagKind.None, Key: DK((1L << 34) + i)));
        }
        for (int i = 0; i < sm.Methods.Count && i < sm.MethodNames.Count; i++)
        {
            var m = sm.Methods[i];
            var bits = new List<string> { ShipMind.KindName((IncidentKind)m.Kind) };
            if (m.SnagIn != SnagKind.None) bits.Add("안에서 막기: " + Mind.SnagText(m.SnagIn));
            if (m.SnagOut != SnagKind.None) bits.Add("바깥에서 막기: " + Mind.SnagText(m.SnagOut));
            if (m.Lost > 0) bits.Add($"셈한 잃을 공기 {m.Lost:0}몰");
            rows.Add(new PanelRow("지어 본 방법 — " + sm.MethodNames[i], string.Join(" · ", bits), false, Key: DK((1L << 34) + (1L << 32) + i)));
        }
        if (sm.LastDevised.Length > 0) rows.Add(new PanelRow("마지막으로 지어낸 계획", TextLint.Clean(sm.LastDevised), false, Key: DK((1L << 35) - 1)));
    }

    // ───────────────────────── 몸 · 방침 ─────────────────────────

    /// <summary>함장 방침 아홉을 낱말로.</summary>
    private static IEnumerable<string> PolicyWords(ShipPolicy p)
    {
        yield return p.AutoSeal != 0 ? "위험한 방을 봉쇄한다" : "방을 봉쇄하지 않는다";
        yield return p.SealWaitMin > 0 ? $"사람이 있으면 {p.SealWaitMin:0.#}분 기다린다" : "사람이 있어도 곧바로 닫는다";
        yield return p.ShedLoads != 0 ? "전력이 모자라면 덜 중요한 장치를 끈다" : "전력이 모자라도 장치를 끄지 않는다";
        yield return p.ResetBreakers != 0 ? "합선 없이 떨어진 차단기를 다시 올린다" : "떨어진 차단기를 다시 올리지 않는다";
        yield return p.AskPeople switch { 0 => "사람에게 일을 부탁하지 않는다", 1 => "로봇이 없을 때만 사람에게 부탁한다 (1분 기다린 뒤)", _ => "로봇과 함께 곧바로 사람에게 부탁한다" };
        yield return p.RationBelow > 0 ? $"보통으로 먹어 {p.RationBelow:0}일을 못 버티면 배급한다" : "배급하지 않는다";
        yield return $"배급 때 식량 하루 몫 {p.RationShare * 4:0.#}/4";
        yield return p.Dodge != 0 ? "레이더에 잡힌 운석을 비킨다" : "운석을 비키지 않는다";
        yield return p.AnswerDistress switch { 0 => "조난 신호에 가지 않는다", 1 => "조난 신호에는 추진제가 되면 간다", _ => "조난 신호에 언제나 간다" };
    }

    private void SelfRows(Sim w, ShipMind sm, List<PanelRow> rows)
    {
        var net = w.Find<Network>();
        var body = new List<string>();
        if (sm.IntakeK > 0) body.Add($"빨아들이는 공기 {sm.IntakeK - 273.15:0}℃");
        body.Add(sm.HeatStopped != 0 ? "열 보호로 멈췄다 — 식으면 다시 켠다" : sm.Throttled != 0 ? "뜨거워 느려졌다 — 센서를 세 배 드물게 읽는다" : "느려지지 않았다");
        if (sm.Overloaded != 0) body.Add("붙잡은 사건이 많아 앞일 그려 보기를 멈췄다");
        body.Add($"센서 읽기 {ReadEverySec(sm):0}초마다 · 무선으로만 닿는 방은 {ReadEverySec(sm) * ShipMind.RadioEvery:0}초마다");
        rows.Add(new PanelRow($"제 몸 — {(sm.Online ? "켜짐" : "멈췄다")}", string.Join("\n", body), !sm.Online || sm.Throttled != 0 || sm.HeatStopped != 0, Key: DK(0)));
        var dev = net?.Devices.FirstOrDefault(d => d.Type == "MainComputer");
        if (net != null && dev != null)
        {
            var bits = new List<string> { net.On[dev.Id] != 0 ? $"전기 {Math.Min(1, net.Supply[dev.Id]) * 100:0}%" : "꺼져 있다" };
            foreach (var p in new[] { Part.Electric, Part.Mechanical, Part.Seal, Part.Filter })
            {
                if (!dev.Has(p)) continue;
                if (net.IsBroken(dev.Id, p)) bits.Add($"{Device.PartName(p)} 고장{(net.BrokenCause(dev.Id, p) is int bc && bc > 0 ? " — " + WhyText(bc) : "")}");
                else bits.Add($"{Device.PartName(p)} 낡음 {net.WearFraction(dev.Id, p) * 100:0}%");
            }
            rows.Add(new PanelRow($"주컴퓨터 장치 — {sm.RoomName(dev.Room)}", string.Join(" · ", bits), !net.Working(dev.Id), Key: DK(1)));
        }
        int wire = 0, radio = 0, blind = 0;
        for (int r = 0; r < w.Layout.Rooms.Count && r < sm.RoomSeen.Length; r++)
        {
            if (w.Layout.Rooms[r].Cells.Length == 0) continue;
            if (sm.RoomSeen[r] == 1) wire++; else if (sm.RoomSeen[r] == 2) radio++; else blind++;
        }
        rows.Add(new PanelRow("센서가 닿는 곳", $"선 {wire} · 무선 {radio} · 못 봄 {blind}" + (sm.BlindLine() is { Length: > 0 } bl ? "\n" + bl : ""), blind > 0, Key: DK(2)));
        var words = PolicyWords(sm.Policy).ToList();   // 판 줄은 여덟 줄까지 — 둘로 나눈다
        rows.Add(new PanelRow("함장 방침 — 사고 · 전력 · 사람", string.Join("\n", words.Take(5)), false, Key: DK(3)));
        rows.Add(new PanelRow("함장 방침 — 물자 · 바깥", string.Join("\n", words.Skip(5)), false, Key: DK(5)));
        var tally = new List<string>
        {
            $"경보 {sm.Alarms}", $"맡김 {sm.Assignments}", $"봉쇄 {sm.Seals}", $"끈 장치 {sm.Sheds}", $"차단기 {sm.Resets}",
            $"미리 돌려 보기 {sm.Rehearsals} (하지 않기로 {sm.RehearsalVetoes})", $"지어냄 {sm.Devises} (찾음 {sm.DevisesFound})", $"계획 마침 {sm.PlansDone}",
            $"비키기 {sm.DodgesTried} (비껴감 {sm.DodgesAvoided} · 못 비킴 {sm.DodgesFailed})", $"대피 {sm.Shelters}", $"배급 {sm.RationsOpened}", $"부탁 {sm.Asked} (받아 줌 {sm.AskAccepted})",
        };
        rows.Add(new PanelRow("이 항해의 셈", string.Join(" · ", tally), false, Key: DK(4)));
    }
}
