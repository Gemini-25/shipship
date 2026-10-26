namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 × 화장실 · 샤워실 ((다) — docs/WASH.md): 변기 조절기가 보고하는 것만 안다 — 막힘 · 오물 통이 찬 몫 · 물 내린 수
/// (조절기에 전기가 들고 그 방의 센서가 읽힐 때만 · 안을 보는 카메라는 없어 때는 모른다 — 쓴 수로 닦을 때를 정한다).
/// 막혔거나 통이 가득 차면 '사용 금지' 불을 켜고 방송 · 일감판에 변기 손질(뚫기 · 통 비우기 · 닦기 — 로봇 먼저)을 올린다 ·
/// 화장실에 기다리는 이가 많으면 다른 화장실의 빈 곳으로 뒤에 온 이를 보내거나 차례를 알린다. 샤워 배급은 물 배급과 함께 (ShipMindSupply).
/// 무엇을 할지는 사람 · 로봇의 판단 (⑧) — 여기는 알리고 맡길 뿐.
/// </summary>
public sealed partial class ShipMind
{
    public const int WashEvery = 25, WashOffset = 11;   // 1분마다
    /// <summary>샤워 배급: 한 번 3분 · 이틀에 한 번.</summary>
    public const int ShowerRuleMin = 3;
    public static readonly long ShowerRuleGap = Time.Hours(48);
    /// <summary>오물 통을 비울 몫 (80%) · 닦을 물 내린 수 (12번).</summary>
    public const double EmptyAt = 0.8;
    public const int ScrubEvery = 12;
    /// <summary>줄 안내 사이 (같은 화장실에 20분에 한 번).</summary>
    public static readonly long WashCallGap = Time.Minutes(20);
    /// <summary>재기용: '사용 금지'를 켠 번 · 빈 곳으로 보낸 안내 · 차례 안내 · 빈 샤워 칸의 물을 잠근 번.</summary>
    public long ToiletSigns, WashCalls, WashOrders, ShowersCut;
    private byte[] _showerIdle = Array.Empty<byte>();   // 샤워 칸마다 물이 흐르는데 아무도 없는 읽기 수 (1분마다)
    private long[] _washCallAt = Array.Empty<long>();   // 방마다 마지막 줄 안내
    private long[] _washSince = Array.Empty<long>();    // 사람마다 화장실에 들어온 때 (사람 표의 자리 — 1분마다 · 0이면 밖)
    private int[] _washCell = Array.Empty<int>();       // 사람마다 지난 읽기의 자리 (가만히 서 있나)
    private int[]? _washFix;                             // 변기 · 샤워 칸 설비
    // 5단 (라)(마): 샤워 칸 알림 · 냄새 처방 · 돌림병
    private long[] _showerNoteAt = Array.Empty<long>();  // 샤워 칸 설비마다 받은 알림의 때
    private byte[] _showerNoteKind = Array.Empty<byte>(); // 그 갈래 (1 뜨거움 · 2 찬물)
    private byte[] _showerAwait = Array.Empty<byte>();   // 부품이 없어 잠가 두고 부품을 기다린다 (1)
    private byte[] _odorStep = Array.Empty<byte>();      // 방마다 냄새 알림의 처방 단계 (0 아직 · 1 비움 · 2 닦음 · 3 못 찾음)
    private byte[] _scrubAsk = Array.Empty<byte>();      // 변기마다 '닦아 달라' 표시 (냄새로 올린 닦기 — 한 번 닦으면 끝)
    /// <summary>닦을 물 내린 수 (평시 <see cref="ScrubEvery"/> · 돌림병 6).</summary>
    public int ScrubAt = ScrubEvery;
    private long _outbreakAt = long.MinValue / 2;        // 돌림병(배탈)을 마지막으로 알린 때
    private int[]? _washDevOf;                           // 설비 → 장치 (−1 없음)

    private int WashDevice(int fu)
    {
        if (_net == null) return -1;
        if (_washDevOf == null)
        {
            _washDevOf = Enumerable.Repeat(-1, L.Furniture.Count).ToArray();
            foreach (var d in _net.Devices) if (d.Furniture >= 0 && d.Furniture < _washDevOf.Length) _washDevOf[d.Furniture] = d.Id;
        }
        return fu < _washDevOf.Length ? _washDevOf[fu] : -1;
    }

    /// <summary>그 변기의 조절기가 보고하나 (전기가 들고 · 그 방의 센서가 읽혔다).</summary>
    private bool ToiletReports(int fu)
    {
        int d = WashDevice(fu), r = L.Room[L.Furniture[fu].Cells[0]];
        return d >= 0 && _net!.Supply[d] > 0.05f && r >= 0 && r < RoomSeen.Length && RoomSeen[r] != 0;
    }

    /// <summary>1분마다 (돌 때만): 변기마다 조절기 보고 → '사용 금지' 불 · 손질 일감 · 화장실 줄.</summary>
    private void WashWatch(Sim sim)
    {
        if (sim.Tick % WashEvery != WashOffset || _mind == null || _life == null || Imagining) return;
        _washFix ??= L.Furniture.Where(f => f.Cells.Length > 0 && f.Type is "Toilet" or "Shower").Select(f => f.Id).ToArray();
        if (_showerIdle.Length != L.Furniture.Count) _showerIdle = new byte[L.Furniture.Count];
        foreach (int fu in _washFix)
        {
            if (L.Furniture[fu].Type == "Shower") { ShowerWatch(sim, fu); continue; }
            if (!ToiletReports(fu)) continue;   // 모르는 변기는 그대로 둔다 (불도)
            int cell = L.Furniture[fu].Cells[0], room = L.Room[cell];
            bool clog = _life.Clogged[fu] != 0, full = _life.Tank[fu] >= Life.TankMax - 0.5;
            bool sign = clog || full;
            if (sign != ((_mind.WashSign[fu] & 1) != 0))   // 제 비트만 (막힘 · 가득 — 5단 C23)
            {
                _mind.SignToilet(fu, sign);
                if (sign) { ToiletSigns++; Broadcast(sim, $"{RoomName(room)} 변기 하나가 {(clog ? "막혔습니다" : "오물 통이 가득 찼습니다")} — 손볼 때까지 쓰지 마세요"); }
            }
            if (_life.Tank[fu] >= EmptyAt * Life.TankMax) PostCare(sim, fu, 2, cell, person => true);   // 통이 찼으면 비우기부터 (가득 차 막힌 변기는 뚫어도 내려갈 데가 없다)
            else if (clog) PostCare(sim, fu, 1, cell, person => person.C.SkRepair >= 0.2f);
            else if (_life.Flushes[fu] - _life.ScrubbedAt[fu] >= ScrubEvery) PostCare(sim, fu, 0, cell, person => true);
        }
        // 바닥에 내려놓인 오물 통 (내려놓은 이가 알린 그 통 — 물체 Id): 주워 비울 곳으로 (로봇 먼저 · 한 칸에 둘이어도 저마다)
        foreach (int id in _mind.WasteDown)
        {
            int at = PropCell(id);
            if (at < 0) continue;
            bool posted = false;
            foreach (var j in Jobs) if (j.Kind == GoalKind.ToiletCare && j.Arg == 3 && j.Target == id) { posted = true; break; }
            if (!posted) Post(sim, new Job { Kind = GoalKind.ToiletCare, Target = id, Arg = 3 }, at, person => true);
        }
        WashQueues(sim);
    }

    /// <summary>샤워 칸 (물 흐름 계량 · 사람 표의 자리): 물이 흐르는데 2분 넘게 아무도 없으면 잠근다 — 틀어 둔 이는 그 일로 배운다 (Switched ← 틀어 둔 원인).</summary>
    private void ShowerWatch(Sim sim, int fu)
    {
        int d = WashDevice(fu), cell = L.Furniture[fu].Cells[0], r = L.Room[cell];
        if (d < 0 || !_net!.ValveOpen(d) || r < 0 || r >= RoomSeen.Length || RoomSeen[r] == 0) { _showerIdle[fu] = 0; return; }   // 물 흐름 계량기 · 밸브는 주컴퓨터에 이어져 있다
        bool someone = false;
        foreach (var a in _mind!.Agents) if (a.C.Up != 0 && a.C.Cell == cell) { someone = true; break; }
        if (someone) { _showerIdle[fu] = 0; return; }
        if (++_showerIdle[fu] < 3) return;   // 읽기 셋 (2분 넘게) — 막 나간 이 · 막 들어올 이를 기다린다
        _showerIdle[fu] = 0;
        int cut = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {RoomName(r)} 빈 샤워 칸의 물을 잠갔다", sim.Tick, cell, _net.OnCause[d]);
        sim.Facts.Emit(sim.Tick, FactKind.Switched, cell, 0, cut, -1, d);
        _net.SetValve(d, false); _net.SetOn(d, false); ShowersCut++;
        Say(sim, $"{RoomName(r)} 샤워 칸 물이 틀어진 채 비어 있어 잠갔다");
    }

    private void PostCare(Sim sim, int fu, int kind, int cell, Func<Agent, bool> canPerson)
    {
        foreach (var j in Jobs) if (j.Kind == GoalKind.ToiletCare && j.Arg != 3 && j.Target == fu) return;   // 한 변기에 한 일
        Post(sim, new Job { Kind = GoalKind.ToiletCare, Target = fu, Arg = kind }, cell, canPerson);
    }

    /// <summary>손질 끝 (조절기 보고): 뚫렸다 · 통이 반 아래로 · 닦은 뒤로 쓴 수가 적다.</summary>
    private bool CareDone(in Job j) => j.Arg == 3 ? !LooseWasteAt(j.Target) : _life == null || j.Target >= _life.Tank.Length || j.Arg switch
    {
        1 => _life.Clogged[j.Target] == 0,
        2 => _life.Tank[j.Target] < 0.5 * Life.TankMax,
        _ => _life.Flushes[j.Target] - _life.ScrubbedAt[j.Target] < ScrubEvery,
    };

    private bool LooseWasteAt(int id) => _mind!.WasteDown.Contains(id);   // 알린 통 (가 본 이가 지운다)

    /// <summary>
    /// 화장실 줄 (사람 표의 자리 — 1분마다): 화장실 방 안에서 변기 · 샤워 칸 위에 있지 않은 이가 기다리는 이다.
    /// 둘 넘게 기다리면 (20분에 한 번) 다른 화장실에 빈 변기 · 샤워 칸이 있으면 뒤에 온 이부터 그리로 보내고 · 없으면 온 차례를 알린다.
    /// </summary>
    private void WashQueues(Sim sim)
    {
        var mind = _mind!;
        if (_washCallAt.Length != L.Rooms.Count) _washCallAt = Enumerable.Repeat(long.MinValue / 2, L.Rooms.Count).ToArray();
        if (_washSince.Length < mind.Agents.Count) { Array.Resize(ref _washSince, mind.Agents.Count); Array.Resize(ref _washCell, mind.Agents.Count); }
        var fix = _washFix!;
        // 들어온 때 (1분 단위) · 가만히 서 있나 (지난 읽기와 같은 칸)
        var still = new bool[mind.Agents.Count];
        foreach (var a in mind.Agents)
        {
            bool inWash = a.IsPerson && a.C.Up != 0 && WashRoomAt(a.C.Cell, fix) >= 0;
            if (!inWash) _washSince[a.Id] = 0;
            else if (_washSince[a.Id] == 0) _washSince[a.Id] = sim.Tick;
            still[a.Id] = inWash && _washCell[a.Id] == a.C.Cell + 1;
            _washCell[a.Id] = inWash ? a.C.Cell + 1 : 0;
        }
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (RoomSeen[r] == 0 || sim.Tick - _washCallAt[r] < WashCallGap) continue;
            int here = 0;
            foreach (int fu in fix) if (L.Room[L.Furniture[fu].Cells[0]] == r) here++;
            if (here == 0) continue;
            var waiting = new List<int>();
            // 기다리는 이: 누가 쓰고 있는 변기 · 샤워 칸 곁 (같은 방 두 칸 안 — 사람은 문 곁을 비켜 두 칸 떨어져 줄을 서기도 한다 · 5단 C5)에 1분 넘게 가만히 서 있다 (지나가는 이 · 손 씻는 이는 아니다)
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson || a.C.Up == 0 || !still[a.Id] || !InOrAtDoor(a.C.Cell, r) || OnFixture(a.C.Cell)) continue;
                bool beside = false;
                foreach (int fu in fix)
                {
                    int c = L.Furniture[fu].Cells[0];
                    if (L.Room[c] != r || Math.Max(Math.Abs(L.X(c) - L.X(a.C.Cell)), Math.Abs(L.Y(c) - L.Y(a.C.Cell))) > 2) continue;
                    foreach (var o in mind.Agents) if (o != a && o.IsPerson && o.C.Up != 0 && o.C.Cell == c) { beside = true; break; }   // 쓰는 이는 사람 (설비 곁을 도는 로봇이 아니다 — DF5)
                    if (beside) break;
                }
                if (beside) waiting.Add(a.Id);
            }
            if (waiting.Count < 2) continue;
            waiting.Sort((x, y) => _washSince[x] != _washSince[y] ? _washSince[x].CompareTo(_washSince[y]) : x.CompareTo(y));   // 먼저 온 차례
            // 다른 화장실의 빈 곳: 쓸 수 있고 (불이 꺼져 있고 · 돌아간다) 그 위에 아무도 없고 · 그 방에 기다리는 이가 없다
            var free = new List<int>();
            foreach (int fu in fix)
            {
                int c = L.Furniture[fu].Cells[0], rr = L.Room[c];
                if (rr == r || rr < 0 || RoomSeen[rr] == 0) continue;
                bool toilet = L.Furniture[fu].Type == "Toilet";
                if (toilet && (!ToiletReports(fu) || _mind.WashSign[fu] != 0 || !_net!.Working(WashDevice(fu))) || !toilet && _mind.WashSign[fu] != 0) continue;   // 샤워 칸 불도 (5단 C23)
                bool busy = false;
                foreach (var a in mind.Agents) if (a.IsPerson && a.C.Up != 0 && (a.C.Cell == c || _washSince[a.Id] != 0 && InOrAtDoor(a.C.Cell, rr) && !OnFixture(a.C.Cell))) { busy = true; break; }
                if (!busy) free.Add(fu);
            }
            _washCallAt[r] = sim.Tick;
            // 무엇을 기다리나: 변기가 다 차 있고 샤워 칸이 비었으면 변기 · 거꾸로면 샤워 칸 · 둘 다 차 있으면 서 있는 곁의 설비 (속은 모른다)
            bool toiletsFull = true, showersFull = true;
            foreach (int fu in fix)
            {
                int c = L.Furniture[fu].Cells[0];
                if (L.Room[c] != r) continue;
                bool used = L.Furniture[fu].Type == "Toilet" && (_mind.WashSign[fu] != 0 || !_net!.Working(WashDevice(fu)));   // '사용 금지' · 고장 변기는 빈 것이 아니다
                if (!used) foreach (var a in mind.Agents) if (a.IsPerson && a.C.Up != 0 && a.C.Cell == c) { used = true; break; }
                if (!used && L.Furniture[fu].Type == "Toilet") toiletsFull = false;
                if (!used && L.Furniture[fu].Type == "Shower") showersFull = false;
            }
            if (free.Count > 0)
            {
                // 뒤에 온 이부터 (기다릴 것이 가장 긴 이) — 기다리는 설비와 같은 갈래로
                var sent = new List<string>();
                for (int k = waiting.Count - 1; k >= 1 && free.Count > 0; k--)
                {
                    int id = waiting[k];
                    bool wantsToilet = toiletsFull != showersFull ? toiletsFull : NearestFixType(mind.Agents[id].C.Cell, r) == "Toilet";
                    int pick = free.FindIndex(fu => (L.Furniture[fu].Type == "Toilet") == wantsToilet);
                    if (pick < 0) continue;
                    mind.CallWash(sim.Tick, id, free[pick], 0);
                    sent.Add($"{mind.NameOf(id)} → {RoomName(L.Room[L.Furniture[free[pick]].Cells[0]])}");
                    free.RemoveAt(pick);
                }
                if (sent.Count > 0)
                {
                    WashCalls++;
                    Broadcast(sim, $"{RoomName(r)} 줄이 깁니다 — 비어 있는 곳으로: {string.Join(" · ", sent)}");
                    continue;
                }
            }
            for (int k = 0; k < waiting.Count; k++) mind.CallWash(sim.Tick, waiting[k], -1, k + 1);
            WashOrders++;
            Broadcast(sim, $"{RoomName(r)} 차례를 알립니다: {string.Join(" → ", waiting.Select(id => mind.NameOf(id)))}");
        }
    }

    /// <summary>그 칸이 그 방 안이거나 그 방으로 드는 문간이다 (화장실 줄은 문간까지 늘어선다).</summary>
    private bool InOrAtDoor(int cell, int room)
    {
        if (cell < 0) return false;
        if (L.Room[cell] >= 0) return L.Room[cell] == room;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(cell, nb);
        foreach (int j in nb) if (j >= 0 && L.Room[j] == room) return true;
        return false;
    }

    /// <summary>그 칸이 든 화장실 방 (문간이면 그 너머 화장실 · −1 아니다).</summary>
    private int WashRoomAt(int cell, int[] fix)
    {
        foreach (int fu in fix) { int r = L.Room[L.Furniture[fu].Cells[0]]; if (InOrAtDoor(cell, r)) return r; }
        return -1;
    }

    private bool OnFixture(int cell) { foreach (int fu in _washFix!) if (L.Furniture[fu].Cells[0] == cell) return true; return false; }

    /// <summary>그 칸에서 가장 가까운 (같은 방의) 변기 · 샤워 칸의 갈래.</summary>
    private string NearestFixType(int cell, int room)
    {
        string best = "Toilet"; int bd = int.MaxValue;
        foreach (int fu in _washFix!)
        {
            int c = L.Furniture[fu].Cells[0];
            if (L.Room[c] != room) continue;
            int d = Math.Abs(L.X(c) - L.X(cell)) + Math.Abs(L.Y(c) - L.Y(cell));
            if (d < bd) { bd = d; best = L.Furniture[fu].Type; }
        }
        return best;
    }

    private void SaveWash(StateWriter w)
    {
        w.Tag("주컴퓨터 화장실"); w.Arr(_washCallAt); w.Arr(_washSince); w.Arr(_washCell); w.Arr(_showerIdle); w.I64(ToiletSigns); w.I64(WashCalls); w.I64(WashOrders); w.I64(ShowersCut);
        w.Tag("주컴퓨터 화장실 (라)(마)"); w.Arr(_showerNoteAt); w.Arr(_showerNoteKind); w.Arr(_showerAwait); w.Arr(_odorStep); w.Arr(_scrubAsk); w.I32(ScrubAt); w.I64(_outbreakAt);
    }

    private void LoadWash(StateReader r)
    {
        r.Tag("주컴퓨터 화장실"); _washCallAt = r.Arr<long>(); _washSince = r.Arr<long>(); _washCell = r.Arr<int>(); _showerIdle = r.Arr<byte>(); ToiletSigns = r.I64(); WashCalls = r.I64(); WashOrders = r.I64(); ShowersCut = r.I64();
        r.Tag("주컴퓨터 화장실 (라)(마)"); _showerNoteAt = r.Arr<long>(); _showerNoteKind = r.Arr<byte>(); _showerAwait = r.Arr<byte>(); _odorStep = r.Arr<byte>(); _scrubAsk = r.Arr<byte>(); ScrubAt = r.I32(); _outbreakAt = r.I64();
    }
}
