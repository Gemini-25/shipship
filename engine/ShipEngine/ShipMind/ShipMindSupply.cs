namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 × ⑪ 물자 (깊이 바퀴 2) — 배급 · 보관함 무게 · 막힌 곳 · 물 회수율.
/// <para><b>배급</b>: 한 시간마다 장부(창고를 보면 아는 재고 · 하루 빠르기)로 '보통으로 먹고 마시면 며칠 버티나'를 센다 — 함장 방침 RationBelow(기본 30일)보다
/// 못 버티면 배급을 연다 (식량 하루 몫 = 보통 3.6끼 × RationShare · 물 = 보통 2.5L × 0.85 — 목숨 몫 밑으로는 줄이지 않는다) · 1.5배를 넘게 버티면 푼다.
/// 사람마다 몫을 재는 것은 배식기 · 물 나오는 곳(⑧ — 주컴퓨터는 요청만).</para>
/// <para><b>보관함 무게</b>: 식량 보관함은 무게로 읽는다 — 나르기로 꺼낸 몫은 기록이 있다 · 그 밖으로 줄어든 것은 '몫 밖으로 빈 끼니' (누가 꺼냈는지는 모른다 — 방송 · 일지).</para>
/// <para><b>막힌 곳</b>: 일감판(⑪)이 찾아 알려 준다 (요청) — 새로 막힌 곳은 방송하고 일지에 적는다.</para>
/// </summary>
public sealed partial class ShipMind
{
    public const double FoodPerDay = 3.6 * Mind.MealMol;   // 한 사람 하루 보통 식량 (BVAD 11.8MJ)
    public const double WaterPerDay = 139;                  // 한 사람 하루 마실 물 (2.5L)
    public const float WaterShare = 0.85f;                  // 물은 덜 줄인다 (목숨 몫)
    public byte RationFoodOn, RationWaterOn;
    public long RationsOpened, MissingFound;
    public double MissingMol;
    public readonly List<string> Blocked = new();           // 지금 막힌 곳 (일감판이 알려 준 것)
    public readonly List<int> BlockedCause = new();         // 그 원인 마디 (⑪ — 배급을 열면 그 뿌리가 된다)
    private double[] _lkSeen = Array.Empty<double>(), _lkLogged = Array.Empty<double>();
    private double _missingPending;
    private long _missingSaidAt = long.MinValue / 2;
    private double _jMissing, _jProcessed, _jRecovered;      // 지난 일지 때의 셈
    /// <summary>물 셈 (한 시간마다 · 마지막 24개): 물탱크 + 오수 통 × 되찾는 몫 · 마신 물 누계 — 씻은 물 · 오줌이 오수 통에서 되찾기를 기다리는 동안은 준 것이 아니다.</summary>
    private double[] _waterHist = new double[24], _drunkHist = new double[24];
    private long[] _waterHistTick = new long[24];
    private int _waterN;

    /// <summary>일감판(⑪)의 요청: 지금 막힌 곳 — 새로 막힌 것(무엇이 · 왜가 달라진 것 — 날 수 · 끼니 같은 숫자는 빼고 견준다)만 방송한다.</summary>
    public void ReportBlocks(Sim sim, IReadOnlyList<string> lines, IReadOnlyList<int> causes)
    {
        if (!Online) return;
        var before = Blocked.Select(BlockKey).ToHashSet();
        foreach (var l in lines) if (!before.Contains(BlockKey(l))) Say(sim, $"막힌 곳: {l}");
        Blocked.Clear(); Blocked.AddRange(lines);
        BlockedCause.Clear(); BlockedCause.AddRange(causes);
    }

    /// <summary>그 말로 시작하는 막힌 곳의 원인 (없으면 0).</summary>
    private int BlockCause(string head)
    {
        for (int i = 0; i < Blocked.Count && i < BlockedCause.Count; i++) if (Blocked[i].StartsWith(head)) return BlockedCause[i];
        return 0;
    }

    /// <summary>막힌 곳 줄의 뼈대: 숫자를 지운 글 ("식량 13일 치 ← 거두는 양(하루 0.0끼)…" → "식량 일 치 ← 거두는 양(하루 끼)…").</summary>
    internal static string BlockKey(string line) => new string(line.Where(ch => !char.IsDigit(ch) && ch != '.').ToArray());

    /// <summary>한 시간마다: 보통으로 먹고 마시면 며칠 버티나 → 배급을 열고 푼다 · 10초마다 보관함 무게.</summary>
    private void Supply(Sim sim)
    {
        if (_mind == null) return;
        if (sim.Tick % ReadEvery == 0) WatchLockers(sim);
        if (sim.Tick % Time.Hours(1) == 37) SampleWater(sim);
        if (sim.Tick % Time.Hours(1) != 37 || Policy.RationBelow <= 0 || sim.Tick < Time.Hours(6)) return;   // 장부가 여섯 시간은 쌓여야 하루 빠르기를 믿는다 (첫 끼니가 몰린 한 시간으로 재면 '12일 치'가 '5일 치'로 보였다)
        int crew = 0;
        var life = sim.Find<Life>();
        foreach (var a in _mind.Agents) if (a.IsPerson && life != null && life[a.C.Body].Alive != 0) crew++;
        if (crew == 0) return;
        double food = Days(sim, Res.Food, RationFoodOn != 0 ? Policy.RationShare : 1), water = WaterDays(sim, RationWaterOn != 0 ? WaterShare : 1);
        byte fOn = (byte)(food < Policy.RationBelow || RationFoodOn != 0 && food < Policy.RationBelow * 1.5f ? 1 : 0);
        byte wOn = (byte)(water < Policy.RationBelow || RationWaterOn != 0 && water < Policy.RationBelow * 1.5f ? 1 : 0);
        if (fOn == RationFoodOn && wOn == RationWaterOn) return;
        bool opening = fOn > RationFoodOn || wOn > RationWaterOn;
        RationFoodOn = fOn; RationWaterOn = wOn;
        string what = string.Join(" · ", new[] { fOn != 0 ? $"식량 하루 몫 {Policy.RationShare * 4:0}/4 ({food:0}일 치)" : "", wOn != 0 ? $"물 하루 몫 {WaterShare * 100:0}% ({water:0}일 치) · 샤워는 {ShowerRuleMin}분 · 이틀에 한 번" : "" }.Where(x => x.Length > 0));
        int cause = 0;
        if (fOn != 0 || wOn != 0)
        {
            if (opening) RationsOpened++;
            int why = fOn != 0 ? BlockCause("식량") : 0;
            if (why == 0 && wOn != 0) why = BlockCause("마실 물");
            cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 배급을 열었다 — {what}", sim.Tick, _net != null && _computer >= 0 ? _net.Devices[_computer].Cell : -1, why);   // 뿌리: 일감판이 찾은 막힌 곳 (⑪)
            Say(sim, opening ? $"알립니다. 보통으로 먹으면 {(fOn != 0 ? $"식량이 {food:0}일" : $"물이 {water:0}일")} 치밖에 남지 않았습니다 — 오늘부터 배급합니다: {what}" : $"배급을 고칩니다: {what}");
        }
        else Say(sim, $"배급을 풉니다 — 식량 {food:0}일 · 물 {water:0}일 치");
        _mind.SetRation(fOn != 0 ? Policy.RationShare * FoodPerDay : 0, wOn != 0 ? WaterShare * WaterPerDay : 0, cause);
        _mind.SetShowerRule(sim, wOn != 0 ? ShowerRuleMin : 0, ShowerRuleGap, wOn != 0 ? cause : 0);   // 샤워 배급은 물 배급과 함께 (docs/WASH.md (다))
        sim.Facts.Emit(sim.Tick, FactKind.Rationed, -1, fOn != 0 ? Policy.RationShare : 0, cause, -1, wOn);
    }

    /// <summary>물 셈 한 칸 (센서: 물탱크 · 오수 통의 수위 · 물 재생기가 도는지 · 마시는 곳의 계량기).</summary>
    private void SampleWater(Sim sim)
    {
        if (_net == null) return;
        bool rec = false;
        foreach (var d in _net.Devices) if (d.Def.Role == "recycler" && _net.Working(d.Id) && !_net.IsBroken(d.Id, Part.Filter)) { rec = true; break; }
        int k = _waterN % _waterHist.Length;
        _waterHist[k] = _net.WaterTank + (rec ? Network.WaterRecovery * _net.WasteWater : 0);
        _drunkHist[k] = -sim.Book.Of(Res.Water, Flow.Drunk);
        _waterHistTick[k] = sim.Tick;
        _waterN++;
    }

    /// <summary>
    /// 물은 며칠 버티나: (물탱크 + 되찾을 오수) ÷ 실제로 줄어든 빠르기 (마지막 하루 — 아직 하루가 안 됐으면 그동안) — 배급 중이면 마시는 몫을 보통으로 되돌려 친다.
    /// 장부의 '쓴 양 − 되찾은 양'은 샤워 물이 오수 통에서 기다리는 동안 크게 잡힌다 (찾아 고친 결함 — 아침 샤워 몇 번에 '30일 치 아래'로 보여 배급을 열었다).
    /// </summary>
    private double WaterDays(Sim sim, float share)
    {
        if (_waterN < 2) return Days(sim, Res.Water, share);
        int n = _waterHist.Length, now = (_waterN - 1) % n, old = _waterN < n ? 0 : _waterN % n;
        double days = (_waterHistTick[now] - _waterHistTick[old]) * Time.SecondsPerTick / 86400;
        if (days < 1.0 / 24 - 1e-9) return Days(sim, Res.Water, share);
        double loss = (_waterHist[old] - _waterHist[now]) / days, drunk = (_drunkHist[now] - _drunkHist[old]) / days;
        loss += drunk * (1 / Math.Max(0.1f, share) - 1);
        return loss <= 1e-9 ? double.PositiveInfinity : _waterHist[now] / loss;
    }

    /// <summary>보통으로 쓰면 며칠 버티나: 재고 ÷ (지금 쓰는 양 ÷ 지금 몫 − 만드는 양) — 모르면 무한.</summary>
    private static double Days(Sim sim, Res k, float share)
    {
        double st = sim.Book.LastStock((int)k);
        var (use, make) = sim.Book.PerDay((int)k, sim.Tick);
        double net = use / Math.Max(0.1f, share) - make;
        return st < 0 || net <= 1e-9 ? double.PositiveInfinity : st / net;
    }

    /// <summary>식량 보관함 무게 센서: 기록된 꺼냄(나르기) 밖으로 줄어든 것 — 몫 밖으로 빈 끼니 (누가 꺼냈는지는 모른다).</summary>
    private void WatchLockers(Sim sim)
    {
        if (_mind == null) return;
        int nf = L.Furniture.Count;
        if (_lkSeen.Length < nf) { _lkSeen = Enumerable.Repeat(-1.0, nf).ToArray(); _lkLogged = new double[nf]; }
        for (int fu = 0; fu < nf; fu++)
        {
            var f = L.Furniture[fu];
            if (f.Cells.Length == 0 || !T.Equip(f.Type).Sealed) continue;
            int room = L.Room[f.Cells[0]];
            if (room < 0 || RoomSeen[room] == 0) continue;       // 못 보는 방의 보관함은 못 잰다
            double now = 0;
            foreach (int c in f.Cells) now += F.Stored(T.sFood, c);
            double logged = fu < _mind.LockerLogged.Length ? _mind.LockerLogged[fu] : 0;
            if (_lkSeen[fu] >= 0)
            {
                double drop = _lkSeen[fu] - now - (logged - _lkLogged[fu]);
                if (drop > 0.3) { MissingMol += drop; _missingPending += drop; }
            }
            _lkSeen[fu] = now; _lkLogged[fu] = logged;
        }
        if (_missingPending >= Mind.MealMol * 0.9 && sim.Tick - _missingSaidAt >= Time.Hours(1))
        {
            MissingFound++; _missingSaidAt = sim.Tick;
            Say(sim, $"식량 보관함에서 몫 밖으로 {_missingPending / Mind.MealMol:0}끼가 비었습니다 — 누가 꺼냈는지는 모릅니다");
            _missingPending = 0;
        }
    }

    /// <summary>일지에 붙이는 물자 줄 (배급 · 빈 끼니 · 막힌 곳 · 물 회수율) — 없으면 빈 목록.</summary>
    private List<string> SupplyLines()
    {
        var lines = new List<string>();
        if (RationFoodOn != 0 || RationWaterOn != 0)
            lines.Add("배급 중: " + string.Join(" · ", new[] { RationFoodOn != 0 ? $"식량 하루 몫 {Policy.RationShare * 4:0}/4" : "", RationWaterOn != 0 ? $"물 하루 몫 {WaterShare * 100:0}%" : "" }.Where(x => x.Length > 0)));
        if (MissingMol - _jMissing >= Mind.MealMol * 0.5) lines.Add($"몫 밖으로 빈 끼니 {(MissingMol - _jMissing) / Mind.MealMol:0}");
        foreach (var b in Blocked) lines.Add($"막힌 곳: {b}");
        if (_net != null)
        {
            double p = _net.WaterProcessed - _jProcessed, r = _net.WaterRecovered - _jRecovered;
            if (p > 20) lines.Add($"물 회수율 {r / p * 100:0}%");
            _jProcessed = _net.WaterProcessed; _jRecovered = _net.WaterRecovered;
        }
        _jMissing = MissingMol;
        return lines;
    }

    private void SaveSupply(StateWriter w)
    {
        w.Arr(new[] { RationFoodOn, RationWaterOn }); w.I64(RationsOpened); w.I64(MissingFound); w.F64(MissingMol);
        w.I32(Blocked.Count); foreach (var b in Blocked) w.Str(b); w.Arr(BlockedCause.ToArray());
        w.Arr(_lkSeen); w.Arr(_lkLogged); w.F64(_missingPending); w.I64(_missingSaidAt); w.F64(_jMissing); w.F64(_jProcessed); w.F64(_jRecovered);
        w.Arr(_waterHist); w.Arr(_drunkHist); w.Arr(_waterHistTick); w.I32(_waterN);
    }
    private void LoadSupply(StateReader r)
    {
        var on = r.Arr<byte>(); RationFoodOn = on[0]; RationWaterOn = on[1]; RationsOpened = r.I64(); MissingFound = r.I64(); MissingMol = r.F64();
        Blocked.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) Blocked.Add(r.Str() ?? ""); BlockedCause.Clear(); BlockedCause.AddRange(r.Arr<int>());
        _lkSeen = r.Arr<double>(); _lkLogged = r.Arr<double>(); _missingPending = r.F64(); _missingSaidAt = r.I64(); _jMissing = r.F64(); _jProcessed = r.F64(); _jRecovered = r.F64();
        _waterHist = r.Arr<double>(); _drunkHist = r.Arr<double>(); _waterHistTick = r.Arr<long>(); _waterN = r.I32();
    }
}
