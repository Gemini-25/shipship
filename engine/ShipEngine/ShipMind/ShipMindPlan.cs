using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>해 본 방법 — 막힌 까닭과 함께 (⑩ 판단 엔진 (라)).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Tried
{
    public int Incident;       // 사건 번호 (Incident.Id)
    public byte Method;        // 기본 절차서의 방법: 1 안에서 · 2 바깥에서 (0: 계획의 걸음 · 지어내기)
    public byte Act;           // 255: 기본 절차서의 방법 · 254: 지어내지 못했다 · 그 밖: 계획의 걸음 (ActKind)
    public int Obj;            // 걸음의 대상 (구조 문 번호 · 방 번호)
    public SnagKind Snag;
    public int Subject;        // 막은 것 (문 · 물건 — -1)
    public long Tick;
}

/// <summary>지어낸 계획의 한 걸음 (구조의 문 · 방 번호로).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlanStepRec
{
    public int Incident;
    public byte Act;           // ActKind
    public int Obj;            // 문 걸음: 구조 문 번호 · 방 걸음: 방 번호
    public int Dest;           // 문을 지나 갈 방 (-1: 선체 바깥) — 지나기만
    public byte State;         // 0 아직 · 1 하는 중 · 2 됐다
    public long Start;
    public float Expect;       // 걸릴 분 (풀이기의 셈)
    public int Robot;
    public float From;         // 시작할 때의 기압 (되돌리기 · 채우기 — 움직이는지 본다)
}

/// <summary>
/// ⑩ 판단 엔진 (라) — 주컴퓨터가 해 본 방법을 기억하고 · 기본 절차서의 방법이 다 막히면 풀이기로 지어낸다 (docs/ENGINE.md 판단 엔진).
/// <para>해 본 방법 × 막힌 까닭: 까닭이 아직 그대로면 다시 고르지 않는다 — 문 막힘은 ④가 같은 까닭을 말하는 동안 · 길 · 바깥 막힘은 한 시간 · 물건 없음은 그 물건이 생길 때까지.
/// 지어낸 계획은 걸음마다 맡기고(주컴퓨터 일은 스스로 · 몸 일은 로봇에) 센서로 이뤘나 본다 — 시간을 넘기거나 로봇이 막히면 그 걸음을 막힌 방법으로 적고 다시 푼다.
/// 우주로 트인 쪽 문을 열기 전에는 세계를 복제해 5분 앞을 돌려 본다 (사람이 다치거나 · 계획 밖의 방이 기압을 잃으면 그 걸음을 막고 다시 푼다).
/// 일지 · 원인 마디에 생각 사슬 ('1안 … 막힘(까닭) → 2안 … → 지어낸 안: …').</para>
/// </summary>
public sealed partial class ShipMind
{
    public List<Tried> Attempts = new();
    public List<PlanStepRec> PlanSteps = new();
    public int IncidentSeq;
    public long Devises, DevisesFound, DevisesFailed, PlanFails, PlansDone, Rehearsals, RehearsalVetoes;
    /// <summary>마지막으로 지어낸 계획의 글 (화면 · 시험).</summary>
    public string LastDevised = "";

    public static string MethodName(byte m) => m switch { 1 => "안에서 막기", 2 => "바깥에서 막기", _ => "지어낸 방법" };

    // ───────────────────────── 해 본 방법 ─────────────────────────

    /// <summary>해 본 방법을 적는다 (같은 사건 · 방법 · 걸음 · 까닭 · 대상이면 때만 고친다).</summary>
    private void NoteTried(int incident, byte method, byte act, int obj, SnagKind k, int subject, long t)
    {
        for (int i = 0; i < Attempts.Count; i++)
        {
            var a = Attempts[i];
            if (a.Incident == incident && a.Method == method && a.Act == act && a.Obj == obj && a.Snag == k && a.Subject == subject) { a.Tick = t; Attempts[i] = a; return; }
        }
        Attempts.Add(new Tried { Incident = incident, Method = method, Act = act, Obj = obj, Snag = k, Subject = subject, Tick = t });
        if (Attempts.Count > 64) Attempts.RemoveAt(0);
    }

    /// <summary>막힌 까닭이 아직 그대로인가.</summary>
    private bool StillBlocked(in Tried a)
    {
        long age = _tick - a.Tick;
        if (a.Act == 254) return age < Time.Minutes(30);   // 지어내지 못했다 — 30분 뒤 다시 궁리한다
        switch (a.Snag)
        {
            case SnagKind.DoorJammed: case SnagKind.DoorUnpowered: case SnagKind.DoorHeld: case SnagKind.DoorVacuum: case SnagKind.DoorPressure: case SnagKind.DoorLocked:
                return _st != null && a.Subject >= 0 && Mind.SnagOf(_st.WhyShut(a.Subject)) == a.Snag;
            case SnagKind.DoorStuckBefore:
                return _st != null && a.Subject >= 0 && _st.WhyShut(a.Subject) != DoorShut.None;
            case SnagKind.NoItem:
                return _mind != null && a.Subject >= 0 && _mind.TotalOf(a.Subject) <= 0 && age < Time.Hours(2);
            case SnagKind.NoWayOut: case SnagKind.NoPath: case SnagKind.Other:
                return age < Time.Hours(1);
            default:
                return false;
        }
    }

    /// <summary>그 사건의 그 방법이 막혀 있나 (까닭이 그대로인 것).</summary>
    private bool MethodBlocked(int incident, byte method, out Tried why)
    {
        foreach (var a in Attempts)
            if (a.Incident == incident && a.Act == 255 && a.Method == method && StillBlocked(a)) { why = a; return true; }
        why = default;
        return false;
    }

    private bool HasPlan(int incident) { foreach (var s in PlanSteps) if (s.Incident == incident) return true; return false; }

    /// <summary>계획이 공기를 다루는 방 (되돌리기 · 채우기 — 긴급 공기 · 사건 찾기가 건드리지 않는다).</summary>
    private bool PlanAirRoom(int r)
    {
        foreach (var s in PlanSteps) if (s.Act is (byte)ActKind.Pump or (byte)ActKind.Fill && s.Obj == r && s.State == 1) return true;
        return false;
    }

    /// <summary>계획이 일부러 비우는 방 (사건이 아니다 — 계획이 끝날 때까지).</summary>
    private bool Deliberate(int r)
    {
        foreach (var s in PlanSteps)
        {
            if (s.Act == (byte)ActKind.Pump && s.Obj == r && s.State != 0) return true;
            if (s.Act == (byte)ActKind.Open && s.State != 0 && _st != null)
                foreach (int rr in DoorRooms(s.Obj)) if (rr == r && !IsGoalRoom(s.Incident, r)) return true;
        }
        return false;
    }

    private bool IsGoalRoom(int incident, int r) { foreach (var x in Incidents) if (x.Id == incident) return x.Room == r; return false; }

    /// <summary>문 양쪽의 방 (바깥이면 하나).</summary>
    private List<int> DoorRooms(int d)
    {
        var res = new List<int>(2);
        Span<int> nb = stackalloc int[4];
        L.Neighbors(L.Doors[d].Cell, nb);
        foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && !res.Contains(L.Room[j])) res.Add(L.Room[j]);
        return res;
    }

    // ───────────────────────── 벽이 버틸까 ─────────────────────────

    private List<int>[]? _roomWalls;
    public const double BraceAt = 0.7, WreckAt = 0.9;   // 찢어짐 비율: 이만큼이면 받친다 · 받친 뒤에도 이만큼이면 채우지 않는다

    /// <summary>그 방 둘레 벽 칸 (그 방 바닥과 맞닿거나 모서리로 닿는 벽 — 도면에서).</summary>
    private List<int> RoomWalls(int r)
    {
        if (_roomWalls == null)
        {
            _roomWalls = new List<int>[L.Rooms.Count];
            for (int i = 0; i < L.Rooms.Count; i++) _roomWalls[i] = new List<int>();
            for (int c = 0; c < L.N; c++)
            {
                if (L.Kind[c] != CellKind.Wall) continue;
                int x0 = L.X(c), y0 = L.Y(c);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!L.In(x0 + dx, y0 + dy)) continue;
                        int j = L.Index(x0 + dx, y0 + dy), rr = L.Room[j];
                        if (rr >= 0 && L.Kind[j] == CellKind.Floor && !_roomWalls[rr].Contains(c)) _roomWalls[rr].Add(c);
                    }
            }
        }
        return r >= 0 && r < _roomWalls.Length ? _roomWalls[r] : new List<int>();
    }

    /// <summary>
    /// 그 방을 다시 채우면 둘레 벽이 버틸까 (하중 · 금 센서 — 배 기압 101kPa를 다시 받으면 금 끝이 파괴 인성에 얼마나 다가가나 · ④ Structure.TearRatio):
    /// 받친 뒤에도 가장 위험한 벽의 비율을 돌려주고 · 받쳐야 할 벽(아직 안 받쳤고 비율이 BraceAt 넘는)을 brace에 담는다.
    /// </summary>
    private double WallRisk(int r, List<int>? brace)
    {
        if (_st == null || r < 0) return 0;
        double worst = 0;
        foreach (int c in RoomWalls(r))
        {
            double k = _st.TearRatio(c, 101.3);
            if (k <= 0) continue;
            bool braced = _st.Braced[c] != 0;
            worst = Math.Max(worst, braced ? k : k / Structure.BraceFactor);
            if (!braced && k >= BraceAt) brace?.Add(c);
        }
        return worst;
    }

    /// <summary>몸이 봉합 꾸러미 하나로 막는 넓이 (㎡ — 사람은 꾸러미 넓이 · 로봇은 두 배 · ⑧ SealStep과 같다).</summary>
    private float PatchAreaOf(Agent a) => (float)(_mind!.PatchKitArea * (a.IsPerson ? 1 : 2));
    private double PatchMinutes => KnowledgeTable.Load().A[(int)ActKind.Patch].Minutes;

    /// <summary>그 방이 우주로 트인 외판 구멍 칸 — 가장 큰 것 (선체 센서 — 없으면 −1).</summary>
    private int HoleCell(int r) { RoomHoleArea(r, out int best); return best; }

    /// <summary>
    /// 그 방 자체의 구멍 넓이 합 (㎡ — 선체 센서): 그 방 칸에 맞닿아 우주로 뚫린 벽 · 그 방의 뚫린 바닥 판.
    /// 문이 열려 이어진 옆 방의 구멍은 그 방 몫이 아니다 (기체 구역은 이어져도 막을 자리는 방마다) · 열린 바깥문은 구멍이 아니다.
    /// </summary>
    private float RoomHoleArea(int r, out int biggest)
    {
        biggest = -1; float sum = 0, best = 0;
        if (r < 0) return 0;
        Span<int> nb = stackalloc int[4];
        foreach (var k in F.Links)
        {
            if (k.RB >= 0 || k.Area <= 0 || k.Via < 0 || k.A < 0) continue;
            int c = k.Via;
            if (L.Kind[c] is not (CellKind.Wall or CellKind.Floor) || F.Hole[c] <= 0) continue;
            // 이 방의 구멍인가: 안쪽 칸이 이 방 · 통째로 날아간 벽(트인 칸 — 방이 없다)은 곁의 바닥이 이 방
            bool mine = L.Room[k.A] == r;
            if (!mine && L.Kind[c] == CellKind.Wall) { L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor && L.Room[j] == r) { mine = true; break; } }
            if (!mine) continue;
            float area = Math.Min(2f, F.Hole[c]);
            sum += area;
            if (area > best) { best = area; biggest = c; }
        }
        return sum;
    }

    /// <summary>그 방 기체 구역이 우주로 트였나 (외판 구멍 · 열린 바깥문 — 선체 센서).</summary>
    private bool OpenToSpace(int r)
    {
        int v = r >= 0 && r < _roomVent.Length ? _roomVent[r] : -1;
        if (v < 0 || F.RegionOf.Length <= v || F.RegionOf[v] < 0) return false;
        int reg = F.RegionOf[v];
        foreach (var k in F.Links) if (k.RB < 0 && k.RA == reg && k.Area > 0) return true;
        return false;
    }

    /// <summary>
    /// 로봇이 없을 때 지어낸 계획의 몸이 되어 줄 사람: 함장 방침이 허락하면 가장 가까운 깨어 있는 사람에게 부탁한다 (받을지는 사람마다 — 두려움 · 믿음 · 성실).
    /// 1분에 한 번 · 거절한 이는 다음에 건너뛴다.
    /// </summary>
    private int PlanPerson(Sim sim, ref Incident x)
    {
        if (_mind == null || _life == null || Policy.AskPeople == 0) return -1;
        long t = sim.Tick;
        int helper = x.Person;
        if (helper >= 0 && _mind.Agents[helper].C.AssignedUntil > t && _life.Bodies[_mind.Agents[helper].C.Body].Alive != 0 && !PlanSteps.Any(s => s.Robot == helper)) return helper;   // 받아 준 이
        if (t - x.AskedAt < Time.Minutes(1)) return -1;
        int best = -1, bd = int.MaxValue;
        foreach (var a in _mind.Agents)
        {
            if (!a.IsPerson || a.Id == x.Decl1 || a.Id == x.Decl2 || PlanSteps.Any(s => s.Robot == a.Id)) continue;
            var b = _life.Bodies[a.C.Body];
            if (b.Alive == 0 || b.Conscious == 0 || a.C.AssignedUntil > t && a.C.AssignedGoal != GoalKind.Idle) continue;
            int d = _mind.Dist(a.C.Cell, x.Cell >= 0 ? x.Cell : a.C.Cell) + (b.Asleep != 0 ? 30 : 0);
            if (d < bd) { bd = d; best = a.Id; }
        }
        if (best < 0) return -1;
        string name = _mind.NameOf(best);
        Say(sim, $"{name}님, {RoomName(x.Room)} {KindName(x.Kind)} — 로봇이 없습니다 · 구멍 막는 일을 같이 해 주시겠어요? (제가 문과 공기를 맡겠습니다)");
        bool yes = _mind.Ask(sim, best, GoalKind.Patch, x.Cell, BeliefOf(x), t + Time.Minutes(60), true, out string reply);
        x.AskedAt = t; x.Asks++; Asked++;
        if (reply.Length > 0) Say(sim, $"{name}: \"{reply}\"");
        if (!yes) { AskDeclined++; x.Decl2 = x.Decl1; x.Decl1 = best; return -1; }
        AskAccepted++; x.Person = best;
        return best;
    }

    // ───────────────────────── 지어내기 ─────────────────────────

    /// <summary>
    /// 기본 절차서의 방법이 다 막혔다 → 그림을 그려 풀이기로 지어낸다. 찾으면 계획을 걸음으로 적고 생각 사슬을 일지에 쓴다 ·
    /// 못 찾으면 '방법이 없다 — 닫아 둔 채 기다린다'를 한 번 쓰고 30분 뒤 다시 궁리한다.
    /// </summary>
    private void Devise(Sim sim, ref Incident x, int robot)
    {
        if (_mind == null || _st == null || robot < 0) return;
        var pic = Picture(x, robot, out var placeOf);
        int id = x.Id;
        var banned = new Func<SolveStep, bool>(s =>
        {
            int obj = Solver.DoorAct(s.Act) ? pic.Door[s.Obj] : pic.Room[s.Obj];
            foreach (var a in Attempts)
            {
                if (a.Incident != id || !StillBlocked(a)) continue;
                if (a.Act == (byte)s.Act && a.Obj == obj) return true;
                // 봉하기는 그 방 문의 '문틈 막기 · 닫기'로 푼다 — 그 가운데 하나가 막혔으면 봉하기도 막혔다
                if (s.Act == ActKind.Seal && a.Act is (byte)ActKind.Tape or (byte)ActKind.Close && DoorRooms(a.Obj).Contains(obj)) return true;
            }
            return false;
        });
        // 같은 그림으로 이미 궁리해 못 찾았으면 다시 하지 않는다 (같은 실패를 되풀이하지 않는다 — 문 · 구멍 · 사람 · 몸이 바뀌면 다시)
        int sig = PictureSig(pic);
        foreach (var a in Attempts) if (a.Incident == id && a.Act == 254 && a.Obj == sig && _tick - a.Tick < Time.Hours(6)) return;
        Devises++;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = Solver.Solve(pic, Know.All, computer: true, maxDepth: 12, budget: 4000, banned: banned);
        if (Environment.GetEnvironmentVariable("SNAG_DEBUG") == "1")
        {
            Console.WriteLine($"      [궁리] {Time.Clock(sim.Tick)} {RoomName(x.Room)} 자리 {pic.P} 문 {pic.D} · 펼침 {res.Expanded} · {sw.ElapsedMilliseconds}ms · {(res.Found ? "찾음" : "없음")} · 몸 {pic.Body} · 꾸러미 {pic.Kits} · 숨 {pic.BodyBreathes}");
            for (int p = 0; p < pic.P; p++) Console.WriteLine($"        {p} {pic.Name[p]} {pic.KPa[p]:0}kPa{(pic.Hole[p] ? " 구멍" : "")}{(pic.Vent[p] ? " 통풍" : "")}{(pic.Hull[p] ? " 외판" : "")}{(pic.Brain[p] ? " 주컴" : "")} 사람 {pic.People[p]}");
            for (int k = 0; k < pic.D; k++) Console.WriteLine($"        문 {k}: {pic.DA[k]}–{pic.DB[k]}{(pic.Open[k] ? " 열림" : "")}{(pic.Jam[k] ? " 끼임" : "")}{(pic.Leaky[k] ? " 틈" : "")}{(pic.Powered[k] ? "" : " 전기없음")} 칸 {pic.Cap[pic.DA[k]]:0}/{(pic.DB[k] == pic.Out ? 0 : pic.Cap[pic.DB[k]]):0}");
            if (!res.Found) { Solver.CountActs = new int[16]; var big = Solver.Solve(pic, Know.All, true, 14, 100000); Console.WriteLine($"        큰 예산: {(big.Found ? Solver.Describe(pic, big.Steps) + $" · 위험 {big.Risk} · {big.Cost:0}" : "없음")} · 펼침 {big.Expanded} · {string.Join(" ", Enum.GetValues<ActKind>().Select(a => $"{a}{Solver.CountActs[(int)a]}"))}"); Solver.CountActs = null; }
        }
        string tried = string.Join(" → ", Attempts.Where(a => a.Incident == id && a.Act == 255).GroupBy(a => a.Method).OrderBy(g => g.Min(a => a.Tick))
            .Select(g => g.OrderBy(a => a.Tick).Last()).Select((a, i) => $"{i + 1}안 {MethodName(a.Method)}: 막힘({Mind.SnagText(a.Snag)})"));
        if (res.Found && res.Risk > 0)
        {
            // 찾은 방법이 사람이나 제 몸을 다치게 한다 — 쓰지 않는다
            tried += (tried.Length > 0 ? " → " : "") + $"찾은 안은 위험하다 ({Solver.Describe(pic, res.Steps)})";
            res.Found = false;
        }
        if (!res.Found)
        {
            DevisesFailed++;
            NoteTried(id, 0, 254, sig, SnagKind.Other, -1, _tick);
            int fc = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {RoomName(x.Room)} 구멍을 막을 방법을 찾지 못했다 — {tried} · 닫아 둔 채 기다린다", sim.Tick, x.Cell, x.Cause);
            sim.Facts.Emit(sim.Tick, FactKind.Decision, x.Cell, 0, fc, -1, x.Room);
            Say(sim, $"생각: {RoomName(x.Room)} {KindName(x.Kind)} — {(tried.Length > 0 ? tried + " → " : "")}다른 방법을 찾지 못했다 (펼침 {res.Expanded}) · 닫아 둔 채 기다린다");
            return;
        }
        DevisesFound++;
        int body = pic.Body;
        var expanded = Solver.Expand(pic, res.Steps, Know.All, true);   // 봉하기 → 문 닫기 · 문틈 막기 하나씩 (걸음마다 그때의 그림으로 셈한 분)
        var steps = expanded.Select(e => e.step).ToList();
        for (int si = 0; si < expanded.Count; si++)
        {
            var (s, minutes) = expanded[si];
            bool door = Solver.DoorAct(s.Act);
            int dest = -1;
            if (s.Act == ActKind.Go) { int to = pic.DA[s.Obj] == body ? pic.DB[s.Obj] : pic.DA[s.Obj]; dest = pic.Room[to]; body = to; }
            PlanSteps.Add(new PlanStepRec { Incident = id, Act = (byte)s.Act, Obj = door ? pic.Door[s.Obj] : pic.Room[s.Obj], Dest = dest, Robot = robot, Expect = minutes,
                From = s.Act == ActKind.Open ? res.AirLost : 0 });   // 문 열기: 풀이기가 셈한 잃을 공기 (미리 돌려 보기가 견준다)
        }
        // 막은 뒤: 비운 방과 그 방을 다시 채운다 (예비 공기)
        foreach (var r in steps.Where(s => s.Act == ActKind.Pump).Select(s => pic.Room[s.Obj]).Append(x.Room).Distinct().ToList())
            PlanSteps.Add(new PlanStepRec { Incident = id, Act = (byte)ActKind.Fill, Obj = r, Dest = -1, Robot = robot,
                Expect = (float)(L.Rooms[r].Cells.Length * ShipEngine.Picture.MolPerCellAtm / (L.Rooms[r].Cells.Length * 0.06) / 60) });   // 빈 방을 다시 채우는 데 드는 분
        string plan = Solver.Describe(pic, steps);
        LastDevised = plan;
        string chain = (tried.Length > 0 ? tried + " → " : "") + (res.Invented ? "지어낸 안: " : "다른 안: ") + plan;
        int cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 궁리했다 — {RoomName(x.Room)} 구멍: {chain} (약 {res.Minutes:0}분 · 잃을 공기 약 {res.AirLost:0}몰)", sim.Tick, x.Cell, x.Cause);
        sim.Facts.Emit(sim.Tick, FactKind.Devised, x.Cell, res.Minutes, cause, -1, x.Room);
        Say(sim, $"생각: {RoomName(x.Room)} {KindName(x.Kind)} — {chain} · 약 {res.Minutes:0}분 · 잃을 공기 약 {res.AirLost:0}몰 · 되돌릴 공기 {res.AirBack:0}몰");
    }

    /// <summary>그림의 굵은 지문 — 문(열림 · 끼임 · 틈) · 구멍 · 사람 있음 · 통풍구 · 몸의 자리 (기압은 30kPa 단위로).</summary>
    private static int PictureSig(Picture pic)
    {
        unchecked
        {
            int h = 17;
            for (int p = 0; p < pic.P; p++) h = h * 31 + pic.Room[p] * 7 + (pic.Hole[p] ? 1 : 0) + (pic.People[p] > 0 ? 2 : 0) + (pic.Vent[p] ? 4 : 0) + (int)(pic.KPa[p] / 30) * 8;
            for (int d = 0; d < pic.D; d++) h = h * 31 + (pic.Open[d] ? 1 : 0) + (pic.Jam[d] ? 2 : 0) + (pic.Leaky[d] ? 4 : 0) + pic.Door[d] * 8;
            return h * 31 + pic.Body;
        }
    }

    /// <summary>
    /// 주컴퓨터의 그림 — 사건 방에서 문 네 번 거리까지(많아야 14방) + 로봇이 있는 방까지의 방들 + 바깥.
    /// 기압은 센서로(못 보는 방은 101kPa로 친다) · 구멍은 사건 방(과 우주로 트인 진공 사건 방) · 사람은 우주복 없는 이 · 통풍구는 전기와 공조가 돌 때.
    /// </summary>
    private Picture Picture(in Incident x, int robot, out Dictionary<int, int> placeOf)
    {
        var pic = new Picture();
        placeOf = new Dictionary<int, int>();
        var rooms = new List<int> { x.Room };
        var depth = new Dictionary<int, int> { [x.Room] = 0 };
        var parent = new Dictionary<int, int> { [x.Room] = -1 };
        for (int qi = 0; qi < rooms.Count; qi++)
        {
            int r = rooms[qi];
            foreach (int d in _roomDoors[r])
                foreach (int o in DoorRooms(d))
                    if (!depth.ContainsKey(o)) { depth[o] = depth[r] + 1; parent[o] = r; rooms.Add(o); }
        }
        int robotRoom = _mind!.RoomOf(_mind.Agents[robot].C.Cell);
        bool robotOut = L.Kind[_mind.Agents[robot].C.Cell] == CellKind.Outside;
        // 문제에 닿는 방만: 사건 방에서 문 두 번 거리 + 로봇이 있는 방까지의 길 + 가장 가까운 에어락(바깥문이 있는 방)까지의 길
        // 문제에 닿는 방만: 사건 방 · 로봇이 있는 방까지의 길 · 가장 가까운 에어락(바깥문이 있는 방)까지의 길 — 그리고 그 길 위 방들의 이웃 모두 (봉할 문을 빠뜨리지 않게)
        var core = new List<int> { x.Room };
        void Path(int from) { for (int r = from; r >= 0 && !core.Contains(r); r = parent[r]) core.Add(r); }
        if (robotRoom >= 0 && depth.ContainsKey(robotRoom)) Path(robotRoom);
        int lockRoom = rooms.FirstOrDefault(r => _roomDoors[r].Any(d => L.Doors[d].External), -1);
        if (lockRoom >= 0 && OutsideOf(x.Room) >= 0) Path(lockRoom);
        foreach (int r in rooms.Where(r => depth[r] <= 1)) if (!core.Contains(r)) core.Add(r);
        var keep = new List<int>(core);
        foreach (int r in core) foreach (int d in _roomDoors[r]) foreach (int o in DoorRooms(d)) if (!keep.Contains(o)) keep.Add(o);
        double full = L.Rooms.Sum(rm => rm.Cells.Length) * Field.CellVolume * 101_325 / (Field.R * 293.15);
        pic.AirValue = (float)(0.01 + 0.05 * (1 - Math.Min(1, _net!.AirReserve / Math.Max(1, full))));   // 예비가 적을수록 공기가 귀하다
        foreach (int r in keep)
        {
            byte people = 0;
            foreach (var a in _mind.Agents)
                if (a.IsPerson && _life!.Bodies[a.C.Body].Alive != 0 && _life.Bodies[a.C.Body].Suit == 0 && _mind.RoomOf(a.C.Cell) == r && people < 250) people++;
            bool vac = Incidents.Any(y => y.Active != 0 && y.Room == r && y.Kind == IncidentKind.Vacuum);
            placeOf[r] = pic.AddPlace(r, RoomName(r), L.Rooms[r].Cells.Length, RoomSeen[r] != 0 ? RoomKPa[r] : 101.3f,
                vent: _roomVent[r] >= 0 && _net.RoomPowered(r) && _net.HvacKw > 0, hull: OutsideOf(r) >= 0,
                hole: r == x.Room || vac && OpenToSpace(r), people: people, holeArea: RoomHoleArea(r, out _));
        }
        bool anyExt = keep.Any(r => _roomDoors[r].Any(d => L.Doors[d].External));
        if (anyExt || robotOut) pic.AddOut();
        var seen = new HashSet<int>();
        foreach (int r in keep)
            foreach (int d in _roomDoors[r])
            {
                if (!seen.Add(d)) continue;
                var dr = DoorRooms(d);
                int a = placeOf.TryGetValue(dr[0], out int pa) ? pa : -2;
                int b = dr.Count > 1 ? (placeOf.TryGetValue(dr[1], out int pb) ? pb : -2) : (L.Doors[d].External ? pic.Out : -2);
                if (a < 0 || b < 0) continue;
                bool normal = L.Doors[d].Kind == "normal";
                bool ajar = F.DoorOpen[d] > 0.02f;   // 조금이라도 열려 있으면 공기가 지난다 (끼인 채 반쯤 열린 바깥문 — 에어락이 우주로 트였다)
                pic.AddDoor(a, b, d, powered: _st!.DoorPowered[d] != 0, open: ajar, jam: _st.DoorJam[d] != 0, leaky: normal && !ajar && F.DoorTaped[d] == 0, normal: normal);
            }
        if (_computer >= 0 && _net.Devices[_computer].Room is int cr && cr >= 0 && placeOf.TryGetValue(cr, out int cp)) pic.Brain[cp] = true;   // 제 몸이 있는 방
        pic.Body = robotOut ? pic.Out : robotRoom >= 0 && placeOf.TryGetValue(robotRoom, out int bp) ? bp : -1;
        var bodyAgent = _mind.Agents[robot];
        pic.BodyBreathes = bodyAgent.IsPerson && _life!.Bodies[bodyAgent.C.Body].Suit == 0;   // 사람이 몸이면 우주복이 있어야 옅은 공기에 든다
        if (pic.BodyBreathes) foreach (var (r, p) in placeOf) pic.Lockers[p] = _mind.StockIn(r, _mind.iSuit) >= 0;
        if (bodyAgent.IsPerson)
        {
            // 사람은 봉합 꾸러미가 있어야 틈 · 구멍을 막는다 — 든 것 + 그림 안 보관함 (로봇은 제 것이 있다)
            int kits = bodyAgent.C.Holding == _mind.iPatch ? 1 : 0;
            foreach (int r in placeOf.Keys) kits += _mind.CountIn(r, _mind.iPatch);
            pic.Kits = kits;
            pic.PatchArea = PatchAreaOf(bodyAgent);
        }
        // 몸이 된 사람은 그 방 사람 수에서 뺀다 (그 사람은 계획을 안다)
        if (bodyAgent.IsPerson && robotRoom >= 0 && placeOf.TryGetValue(robotRoom, out int sp) && pic.People[sp] > 0 && _life!.Bodies[bodyAgent.C.Body].Suit == 0) pic.People[sp]--;
        pic.Goal = placeOf[x.Room];
        return pic;
    }

    // ───────────────────────── 걸음 해 나가기 ─────────────────────────

    /// <summary>지어낸 계획을 걸음마다 해 나간다 (5틱마다).</summary>
    private void RunPlans(Sim sim)
    {
        if (PlanSteps.Count == 0 || _mind == null || _st == null || _net == null || sim.Tick % 5 != 0) return;
        _simRef = sim;
        foreach (int id in PlanSteps.Select(s => s.Incident).Distinct().ToList())
        {
            int xi = Incidents.FindIndex(y => y.Id == id);
            if (xi < 0 || Incidents[xi].Active == 0 && !PlanSteps.Any(s => s.Incident == id && s.Act == (byte)ActKind.Fill && s.State != 2))
            {
                EndPlan(sim, id, xi >= 0 ? Incidents[xi] : default, done: xi >= 0);
                continue;
            }
            var x = Incidents[xi];
            int si = PlanSteps.FindIndex(s => s.Incident == id && s.State != 2);
            if (si < 0) { EndPlan(sim, id, x, done: true); continue; }
            var st = PlanSteps[si];
            int robot = st.Robot;
            var ra = robot >= 0 && robot < _mind.Agents.Count ? _mind.Agents[robot] : null;
            if (ra == null || ra.C.Up == 0) { FailStep(sim, id, st, SnagKind.Other, -1, ra != null && ra.IsPerson ? $"{_mind.NameOf(ra.Id)}이 움직이지 못한다" : "로봇이 멈췄다"); continue; }
            long t = sim.Tick;
            if (st.State == 0)
            {
                if (!BeginStep(sim, x, ref st, ra)) continue;   // 미리 돌려 보니 안 된다 — 계획을 접었다
                st.State = 1; st.Start = t;
                PlanSteps[si] = st;
            }
            // 로봇의 막힘 (이 걸음을 시작한 뒤)
            if (ra.C.SnagTick >= st.Start && Mind.Telling(ra.C.Snag) && ra.C.SnagGoal == ra.C.AssignedGoal && st.Act is (byte)ActKind.Go or (byte)ActKind.Patch or (byte)ActKind.Unjam or (byte)ActKind.Tape or (byte)ActKind.Suit or (byte)ActKind.Brace)   // 맡긴 걸음의 막힘만
            { FailStep(sim, id, st, ra.C.Snag, ra.C.SnagSubject, Mind.SnagText(ra.C.Snag)); continue; }
            if (StepDone(x, ref st, ra))
            {
                if (PlanLog) Console.WriteLine($"      ({sim.Seed}) [걸음 끝] {Time.Clock(t)} {StepName(st)} · 남은 구멍 {(st.Act == (byte)ActKind.Patch ? RoomHoleArea(st.Obj, out _) * 1e4 : -1):0}cm² · 몸 {RoomName(_mind!.RoomOf(ra.C.Cell))}");
                st.State = 2;
                PlanSteps[si] = st;
                StopStep(st);
                if (st.Act == (byte)ActKind.Patch) AfterPatch(sim, x, si, st);
                continue;
            }
            KeepStep(sim, x, st, ra);
            // 진척: 막는 걸음은 남은 구멍이 줄면 그때부터 다시 잰다 (큰 구멍은 여러 번 덧댄다 — 줄어드는 동안은 막힌 것이 아니다)
            if (st.Act == (byte)ActKind.Patch && RoomHoleArea(st.Obj, out _) is float left && left < st.From - 1e-4f)
            {
                st.From = left; st.Start = t;
                st.Expect = Math.Max(1, (float)Math.Ceiling(left / Math.Max(1e-3f, PatchAreaOf(ra)))) * (float)PatchMinutes;
            }
            PlanSteps[si] = st;
            double limit = Time.Minutes(st.Expect * 3 + 8);
            if (t - st.Start > limit) FailStep(sim, id, st, StepSnag(st), st.Act <= (byte)ActKind.Unjam ? st.Obj : -1, "시간이 넘었다");
        }
    }

    /// <summary>
    /// 구멍을 다 막았다 — 다시 채우기 전에 그 방 벽을 본다: 덧대 받쳐도 못 버티면 그 방은 비워진 채 닫아 두고
    /// 몸을 데리고 들어온 문으로 나와 그 문을 닫는다 (열린 문으로 옆 방과 함께 채우면 그 방도 채워져 찢어진다).
    /// </summary>
    private void AfterPatch(Sim sim, in Incident x, int si, in PlanStepRec patch)
    {
        int r = patch.Obj;
        double risk = WallRisk(r, null);
        if (risk < WreckAt) return;
        HoldWreck(sim, r, risk, x.Cause);
        int xid = x.Id;
        // 들어온 문: 이 방으로 간 걸음
        int k = PlanSteps.FindLastIndex(si, s => s.Incident == xid && s.Act == (byte)ActKind.Go && s.Dest == r);
        if (k < 0) return;
        int door = PlanSteps[k].Obj;
        int back = DoorRooms(door).FirstOrDefault(q => q != r, -1);
        if (back < 0) return;
        var add = new List<PlanStepRec>
        {
            new() { Incident = xid, Act = (byte)ActKind.Go, Obj = door, Dest = back, Robot = patch.Robot, Expect = 1 },
            new() { Incident = xid, Act = (byte)ActKind.Close, Obj = door, Dest = -1, Robot = patch.Robot, Expect = 0.5f },
        };
        if (L.Doors[door].Kind == "normal")   // 보통 문은 닫혀도 틈으로 샌다 — 옆 방을 채우면 그 틈으로 이 방도 차 찢어진다: 틈을 막는다
            add.Add(new() { Incident = xid, Act = (byte)ActKind.Tape, Obj = door, Dest = -1, Robot = patch.Robot, Expect = 3 });
        PlanSteps.InsertRange(si + 1, add);
    }

    private bool BeginStep(Sim sim, in Incident x, ref PlanStepRec st, Agent ra)
    {
        var act = (ActKind)st.Act;
        switch (act)
        {
            case ActKind.Open:
                // 우주로 트인 쪽 문: 먼저 미리 돌려 본다 (From = 풀이기가 셈한 잃을 공기)
                if (DoorRooms(st.Obj).Any(r => OpenToSpace(r)) || L.Doors[st.Obj].External)
                    if (!Rehearse(sim, x, st, out string why)) { FailStep(sim, x.Id, st, rehearsedSnag, st.Obj, why); return false; }
                _st!.UnlockDoor(st.Obj);
                _st.RequestDoor(st.Obj, 1);
                Say(sim, $"계획: {DoorName(st.Obj)}을 연다");
                return true;
            case ActKind.Close: _st!.RequestDoor(st.Obj, 0); return true;
            case ActKind.Pump:
                st.From = RoomKPa[st.Obj];
                Say(sim, $"계획: {RoomName(st.Obj)} 공기를 예비 탱크로 되돌린다 ({RoomKPa[st.Obj]:0}kPa → {Network.PumpFloorKPa:0}kPa) · 그 방 문은 잠근다");
                return true;
            case ActKind.Fill:
            {
                // 채우기 전에 벽을 본다: 금이 간 벽은 먼저 받치고 · 받쳐도 못 버티면 채우지 않고 비워 둔다 (기압을 넣으면 찢어져 공기만 잃는다)
                int fr = st.Obj;
                foreach (int d in _roomDoors[fr])
                    if ((F.DoorOpen[d] > 0.02f || F.DoorGapOpen(d)) && DoorRooms(d).Any(q => q != fr && Wrecked(q)))
                    {
                        if (F.DoorOpen[d] <= 0.02f)
                        {
                            // 닫혔지만 틈이 열린 보통 문 — 몸이 틈을 막고 채운다 (그 방은 못 버틴다)
                            int xid2 = x.Id, body2 = st.Robot, dd = d;
                            if (!PlanSteps.Any(s => s.Incident == xid2 && s.Act == (byte)ActKind.Tape && s.Obj == dd && s.State != 2))
                                PlanSteps.Insert(Math.Max(0, PlanSteps.FindIndex(s => s.Incident == xid2 && s.State != 2)), new PlanStepRec { Incident = xid2, Act = (byte)ActKind.Tape, Obj = dd, Dest = -1, Robot = body2, Expect = 3 });
                            return false;
                        }
                        // 열린 문 너머가 비워 두기로 한 방 — 닫고 채운다 (함께 채우면 그 방이 찢어진다) · 문 칸에 누가 있으면 기다린다
                        if (!_mind!.Agents.Any(o => o.C.Up != 0 && o.C.Cell == L.Doors[d].Cell)) { _st!.UnlockDoor(d); _st.RequestDoor(d, 0); }
                        return false;
                    }
                var brace = new List<int>();
                double risk = WallRisk(st.Obj, brace);
                if (risk >= WreckAt) { HoldWreck(sim, st.Obj, risk, x.Cause); st.From = RoomKPa[st.Obj]; return true; }   // 걸음은 끝난 것으로 (StepDone — 닫아 둔 방)
                if (brace.Count > 0)
                {
                    int xid = x.Id, body = st.Robot;
                    int si = PlanSteps.FindIndex(s => s.Incident == xid && s.State != 2);
                    var bs = brace.Select(c => new PlanStepRec { Incident = xid, Act = (byte)ActKind.Brace, Obj = c, Dest = -1, Robot = body, Expect = 4 }).ToList();
                    PlanSteps.InsertRange(Math.Max(0, si), bs);
                    Say(sim, $"생각: {RoomName(st.Obj)} 벽에 금이 났다 — 기압을 다시 넣으면 찢어질 수 있다 (찢어짐에 {risk * Structure.BraceFactor:P0}까지) → 먼저 금 간 벽 {brace.Count}곳을 덧대 받치고 채운다");
                    return false;   // 다음 차례에 받치기부터
                }
                st.From = RoomKPa[st.Obj];
                return true;
            }
            case ActKind.Clear:
                // 출입 금지 명령 — 따르는 이는 그 방을 나와 그리로 길을 잡지 않는다 ('곧 뺀다'는 믿음은 눈으로 멀쩡한 공기를 보면 지워진다)
                {
                    int kc = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {RoomName(st.Obj)} 출입을 막았다 — 그 방 공기를 뺀다 (임시 에어락)", sim.Tick, _roomVent[st.Obj], x.Cause);
                    _mind!.KeepOut(sim, st.Obj, sim.Tick + Time.Minutes(90), kc, st.Robot);
                    _mind.Announce(sim, _roomVent[st.Obj], AlarmDb, new Belief { Kind = BeliefKind.Leak, Cell = _roomVent[st.Obj], Subject = -1, Conf = 0.6f, Cause = kc });
                }
                Say(sim, $"계획: {RoomName(st.Obj)}의 공기를 곧 뺍니다 — 나가 주세요 · 들어오지 마세요");
                return true;
            case ActKind.Patch:
                st.From = RoomHoleArea(st.Obj, out _);   // 남은 구멍 넓이 — 줄어드는 동안은 막는 중이다 (시간을 다시 잰다)
                return true;
            case ActKind.Go: case ActKind.Unjam: case ActKind.Tape: case ActKind.Suit: case ActKind.Brace:
                return true;
        }
        return true;
    }

    /// <summary>하는 중인 걸음을 이어 간다 (펌프 · 밸브 잠금 · 로봇에게 맡기기를 되풀이).</summary>
    private void KeepStep(Sim sim, in Incident x, in PlanStepRec st, Agent ra)
    {
        long t = sim.Tick;
        LockPlanRooms(t, x.Id);
        var act = (ActKind)st.Act;
        if (act == ActKind.Pump)
        {
            int r = st.Obj;
            if (PlanLog && t % Time.Minutes(10) == 0) Console.WriteLine($"      ({sim.Seed}) {Time.Clock(t)} [되돌리기] {RoomName(r)} {RoomKPa[r]:0}kPa · 사람 {People(r)} · 댐퍼 {_net!.Damper[r]} · 문 {string.Join(" ", _roomDoors[r].Select(d => $"{DoorName(d)}:{F.DoorOpen[d]:0.0}{(F.DoorTaped[d] != 0 ? "t" : "")}{(F.DoorBleed[d] != 0 ? "b" : "")}/{F.Pressure(DoorRooms(d).Where(q => q != r).Select(q => _roomVent[q]).DefaultIfEmpty(-1).First() is int vc && vc >= 0 ? vc : 0):0}"))}");
            if (People(r) > 0) { _net!.RoomAir[r] = 0; return; }   // 사람이 들어왔다 — 멈춘다 (시간이 넘으면 다시 푼다)
            _net!.RoomAir[r] = -L.Rooms[r].Cells.Length * Network.PumpMolPerCellSec;
            _net.SetDamper(r, false);   // 덕트 댐퍼를 닫는다 — 공조 덕트가 다른 방 공기를 그 방으로 돌려 넣지 않게 (계획이 끝나면 연다)
            foreach (int d in _roomDoors[r]) _st!.HoldBleed(d, t + Time.Minutes(2));   // 그 방 문 밸브를 잠근다 (누가 열려 해도 배 공기가 그리로 새지 않게)
        }
        else if (act == ActKind.Fill) _net!.RoomAir[st.Obj] = L.Rooms[st.Obj].Cells.Length * 0.06;
        else if (act == ActKind.Tape && F.DoorOpen[st.Obj] > 0)
        {
            // 막을 문이 열려 있다 (누가 열어 두었다) — 먼저 닫는다 (닫힌 문의 틈만 막을 수 있다 · 지나는 이가 없을 때)
            int dc = L.Doors[st.Obj].Cell, door = st.Obj;
            bool busy = _mind!.Agents.Any(o => o.C.Up != 0 && (o.C.Cell == dc || o.C.WaitDoor == door));
            if (!busy) _st!.RequestDoor(st.Obj, 0);
        }
        // 몸 일: 로봇에게 맡긴다 (계획이 끝날 때까지 다른 일을 맡지 않게 — 기다리는 걸음에서도 제자리를 지킨다)
        int goal = -1; GoalKind g = GoalKind.Investigate;
        switch (act)
        {
            case ActKind.Go: goal = st.Dest >= 0 ? _roomVent[st.Dest] : OutsideOf(x.Room); g = GoalKind.Idle; break;   // 그 방 한가운데까지 가서 기다린다 (살피기는 두 칸 앞에서 끝난다 — 문가는 아직 그 방이 아니다)
            case ActKind.Patch: goal = HoleCell(st.Obj); g = GoalKind.Patch; if (goal < 0) { goal = x.Cell; g = GoalKind.Investigate; } break;   // 선체 센서가 아는 구멍 칸
            case ActKind.Unjam: goal = L.Doors[st.Obj].Cell; g = GoalKind.Brace; break;
            case ActKind.Tape: goal = L.Doors[st.Obj].Cell; g = GoalKind.Patch; break;   // 문틈을 봉합 꾸러미로
            case ActKind.Suit: goal = _mind!.StockIn(st.Obj, _mind.iSuit); g = GoalKind.GetSuit; break;   // 그 방 보관함에서 우주복을
            case ActKind.Brace: goal = st.Obj; g = GoalKind.Brace; break;   // 금 간 벽을 덧대 받친다
            default: goal = ra.C.Cell; g = GoalKind.Idle; break;   // 주컴퓨터 일을 하는 동안 제자리에서 기다린다
        }
        if (goal >= 0 && (ra.C.AssignedGoal != g || ra.C.AssignedTarget != goal || ra.C.AssignedUntil < t + Time.Minutes(5)))
            _mind!.Assign(sim, ra.Id, g, goal, act is ActKind.Go or ActKind.Patch ? BeliefOf(x) : default, t + Time.Minutes(30), Mind.PlanBound);
    }

    /// <summary>
    /// 비울 방의 문을 잠근다: 그 방 사람이 다 나간 뒤 (내보내기 걸음이 끝났거나 처음부터 없다) 계획이 끝날 때까지 —
    /// 사람이 지나며 문을 열면 막은 틈이 떨어지고 펌프가 멈춘다. 계획이 열 문은 그 걸음에서 푼다.
    /// </summary>
    private Sim? _simRef;

    private void LockPlanRooms(long t, int id)
    {
        if (_st == null || _simRef == null) return;
        foreach (var s in PlanSteps)
        {
            if (s.Incident != id || s.Act != (byte)ActKind.Pump || s.State == 2) continue;
            int r = s.Obj;
            if (PlanSteps.Any(c => c.Incident == id && c.Act == (byte)ActKind.Clear && c.Obj == r && c.State != 2) || People(r) > 0) continue;
            foreach (int d in _roomDoors[r])
                if (!PlanSteps.Any(o => o.Incident == id && o.Act == (byte)ActKind.Open && o.Obj == d && o.State != 0)) _st.LockDoor(d, t + Time.Minutes(3));
            _mind?.KeepOut(_simRef!, r, t + Time.Minutes(10), 0, s.Robot);
        }
    }

    private void StopStep(in PlanStepRec st)
    {
        if (st.Act is (byte)ActKind.Pump or (byte)ActKind.Fill && _net != null && st.Obj >= 0 && st.Obj < _net.RoomAir.Length) _net.RoomAir[st.Obj] = 0;
    }

    private bool StepDone(in Incident x, ref PlanStepRec st, Agent ra)
    {
        switch ((ActKind)st.Act)
        {
            case ActKind.Open: return F.DoorOpen[st.Obj] >= 0.9f;
            case ActKind.Close: return F.DoorOpen[st.Obj] <= 0.02f;
            case ActKind.Go: return st.Dest >= 0 ? _mind!.RoomOf(ra.C.Cell) == st.Dest : L.Kind[ra.C.Cell] == CellKind.Outside;
            case ActKind.Unjam: return _st!.DoorJam[st.Obj] == 0;
            case ActKind.Pump: return RoomKPa[st.Obj] <= Network.PumpFloorKPa + 2;
            case ActKind.Fill: return RoomKPa[st.Obj] >= 90 || EmptyHeld(st.Obj);   // 다 채웠다 · 또는 채우지 않고 닫아 두기로 했다
            case ActKind.Brace: return _st!.Braced[st.Obj] != 0;
            case ActKind.Clear: return People(st.Obj) == 0;
            case ActKind.Patch: return RoomHoleArea(st.Obj, out _) <= 0;   // 그 방 구멍을 다 막았다 (이어진 옆 방의 구멍은 그 방 걸음의 몫)
            case ActKind.Tape: return F.DoorTaped[st.Obj] != 0;
            case ActKind.Suit: return ra.IsPerson && _life!.Bodies[ra.C.Body].Suit != 0;
        }
        return false;
    }

    private SnagKind StepSnag(in PlanStepRec st) => (ActKind)st.Act switch
    {
        ActKind.Open when _st != null => Mind.SnagOf(_st.WhyShut(st.Obj)) is var k && k != SnagKind.None ? k : SnagKind.Other,
        _ => SnagKind.Other,
    };

    private int People(int r)
    {
        int n = 0;
        if (_mind == null || _life == null) return 0;
        foreach (var a in _mind.Agents) if (a.IsPerson && _life.Bodies[a.C.Body].Alive != 0 && _life.Bodies[a.C.Body].Suit == 0 && _mind.RoomOf(a.C.Cell) == r) n++;
        return n;
    }

    private string DoorName(int d)
    {
        var rs = DoorRooms(d);
        return rs.Count > 1 ? $"{RoomName(rs[0])}–{RoomName(rs[1])} 문" : $"{RoomName(rs.Count > 0 ? rs[0] : -1)} 바깥문";
    }

    /// <summary>걸음이 막혔다 — 그 걸음을 막힌 방법으로 적고 계획을 접는다 (다음 차례에 다시 푼다).</summary>
    private void FailStep(Sim sim, int id, in PlanStepRec st, SnagKind k, int subject, string why)
    {
        PlanFails++;
        if (PlanLog && _mind != null && st.Robot >= 0)
        {
            var ra = _mind.Agents[st.Robot];
            Console.WriteLine($"      ({sim.Seed}) [막힌 걸음] 로봇 방 {RoomName(_mind.RoomOf(ra.C.Cell))} ({L.X(ra.C.Cell)},{L.Y(ra.C.Cell)}) · 목표 {ra.C.Goal}:{ra.C.GoalTarget} · 맡김 {ra.C.AssignedGoal}:{ra.C.AssignedTarget}/{ra.C.AssignedArg} · 막힘 {ra.C.Snag}@{Time.Clock(ra.C.SnagTick)} · 배터리 {ra.C.Battery:0.00} · 계획 {ra.Plan.Count}단계 {ra.C.StepIndex} · 길 {ra.Path.Count}{(Solver.DoorAct((ActKind)st.Act) ? $" · 문 {st.Obj} 칸 {L.Doors[st.Obj].Cell} 열림 {F.DoorOpen[st.Obj]:0.00} 막음 {F.DoorTaped[st.Obj]} 밸브 {F.DoorBleed[st.Obj]} 종류 {L.Doors[st.Obj].Kind} 잠금 {_st!.Locked(st.Obj)}" : "")}");
        }
        NoteTried(id, 0, st.Act, st.Obj, k, subject, _tick);
        var x = Incidents.FirstOrDefault(y => y.Id == id);
        string what = StepName(st);
        int cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터의 계획이 막혔다 — {what}: {why}", sim.Tick, x.Cell, x.Cause);
        sim.Facts.Emit(sim.Tick, FactKind.Blocked, x.Cell, (float)k, cause, -1, subject);
        Say(sim, $"계획이 막혔다 — {what}: {why} · 다시 궁리한다");
        EndPlan(sim, id, x, done: false);
    }

    private string StepName(in PlanStepRec st) => (ActKind)st.Act switch
    {
        ActKind.Open => $"{DoorName(st.Obj)} 열기", ActKind.Close => $"{DoorName(st.Obj)} 닫기", ActKind.Go => $"{(st.Dest >= 0 ? RoomName(st.Dest) : "선체 바깥")}으로 가기",
        ActKind.Unjam => $"{DoorName(st.Obj)} 바로잡기", ActKind.Pump => $"{RoomName(st.Obj)} 공기 되돌리기", ActKind.Fill => $"{RoomName(st.Obj)} 채우기",
        ActKind.Clear => $"{RoomName(st.Obj)} 사람 내보내기", ActKind.Patch => $"{RoomName(st.Obj)} 구멍 막기", ActKind.Tape => $"{DoorName(st.Obj)} 틈 막기", ActKind.Suit => "우주복 입기", ActKind.Brace => $"{RoomName(WallRoom(st.Obj))} 금 간 벽 받치기", _ => "",
    };

    private void EndPlan(Sim sim, int id, in Incident x, bool done)
    {
        foreach (var s in PlanSteps.Where(s => s.Incident == id)) StopStep(s);
        if (_st != null) foreach (var s in PlanSteps.Where(s => s.Incident == id && s.Act == (byte)ActKind.Pump)) { foreach (int d in _roomDoors[s.Obj]) _st.UnlockDoor(d); _mind?.ClearKeepOut(s.Obj); }
        foreach (var s in PlanSteps.Where(s => s.Incident == id && s.Act == (byte)ActKind.Clear)) _mind?.ClearKeepOut(s.Obj);
        // 비웠던 방의 덕트 댐퍼를 다시 연다 (그 방에 불 · 독한 기체 · 진공 사건이 남아 있으면 그대로)
        foreach (var s in PlanSteps.Where(s => s.Incident == id && s.Act == (byte)ActKind.Pump && s.State != 0))
            if (_net != null && !Incidents.Any(y => y.Active != 0 && y.Room == s.Obj && y.Kind is IncidentKind.Fire or IncidentKind.CO or IncidentKind.Toxic or IncidentKind.Vacuum)) _net.SetDamper(s.Obj, true);
        var robots = PlanSteps.Where(s => s.Incident == id).Select(s => s.Robot).Distinct().ToList();
        PlanSteps.RemoveAll(s => s.Incident == id);
        if (_mind != null) foreach (int rb in robots) if (rb >= 0 && rb < _mind.Agents.Count) _mind.Agents[rb].C.AssignedUntil = sim.Tick;
        if (!done) return;
        PlansDone++;
        Say(sim, $"계획을 마쳤다 — {RoomName(x.Room)}");
    }

    /// <summary>
    /// 미리 돌려 보기: 세계를 복제해 그 문을 열고 5분 앞을 본다 — 사람이 쓰러지거나 숨지면 · 사람이 있는 방이 숨 쉴 기압(60kPa) 아래로 가면 ·
    /// 배가 우주로 잃는 공기(배 안 + 예비 탱크)가 풀이기가 셈한 것의 1.5배 + 200몰을 넘으면 하지 않는다 (방 사이로 옮겨 간 공기는 잃은 것이 아니다).
    /// 복제 안에서는 다시 미리 보지 않는다.
    /// </summary>
    private SnagKind rehearsedSnag;

    private bool Rehearse(Sim sim, in Incident x, in PlanStepRec st, out string why)
    {
        why = ""; rehearsedSnag = SnagKind.Other;
        if (Imagining || _mind == null) return true;
        Rehearsals++;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = sim.Clone();
        var cm = w.Get<ShipMind>(); cm.Imagining = true;
        var wl = w.Get<Life>(); var wm = w.Get<Mind>(); var wf = w.Get<Field>(); var wn = w.Get<Network>();
        int Down() { int n = 0; foreach (var a in wm.Agents) if (a.IsPerson && (wl.Bodies[a.C.Body].Alive == 0 || wl.Bodies[a.C.Body].Conscious == 0)) n++; return n; }
        double Air() { double n = wn.AirReserve; for (int i = 0; i < L.N; i++) if (L.IsSpace(i)) n += wf.Moles(i); return n; }
        var peopleRooms = new HashSet<int>();
        foreach (var a in wm.Agents) if (a.IsPerson && wl.Bodies[a.C.Body].Alive != 0 && wm.RoomOf(a.C.Cell) is int pr && pr >= 0 && wf.Pressure(_roomVent[pr]) >= 60) peopleRooms.Add(pr);
        int down0 = Down();
        double air0 = Air();
        w.Get<Structure>().RequestDoor(st.Obj, 1);
        bool dbg = Environment.GetEnvironmentVariable("SNAG_DEBUG") == "1";
        float thin = 999, opened = 0;
        for (int k = 0; k < ImagineTicks; k += 25)
        {
            for (int i = 0; i < 25; i++) { w.Step(); opened = Math.Max(opened, wf.DoorOpen[st.Obj]); }   // 문은 몇 초만 열렸다 닫히기도 한다 (로봇이 지나간 뒤 봉쇄가 닫는다)
            foreach (int r in peopleRooms) thin = Math.Min(thin, (float)wf.Pressure(_roomVent[r]));
            if (dbg) Console.WriteLine($"      [미리] {k / 25 + 1}분: 기압 {string.Join("/", Enumerable.Range(0, L.Rooms.Count).Select(r => _roomVent[r] >= 0 ? $"{wf.Pressure(_roomVent[r]):0}" : "-"))} · 문 {string.Join(" ", L.Doors.Select(d => $"{wf.DoorOpen[d.Id]:0.0}{(wf.DoorTaped[d.Id] != 0 ? "t" : "")}{(wf.DoorBleed[d.Id] != 0 ? "b" : "")}"))} · 로봇 {string.Join(",", wm.Agents.Where(a => !a.IsPerson).Select(a => $"{a.C.Goal}@{wm.RoomOf(a.C.Cell)}"))}");
        }
        int down = Down() - down0;
        double lost = Math.Max(0, air0 - Air()), allow = st.From * 1.5 + 200;
        var wst = w.Get<Structure>();
        if (opened < 0.5f) { var k = Mind.SnagOf(wst.WhyShut(st.Obj)); rehearsedSnag = k != SnagKind.None ? k : SnagKind.DoorPressure; why = $"미리 돌려 보니 문이 열리지 않는다 — {Mind.SnagText(rehearsedSnag)}"; }
        else if (down > 0) why = $"미리 돌려 보니 {down}명이 쓰러진다";
        else if (thin < 60) why = $"미리 돌려 보니 사람이 있는 방이 {thin:0}kPa로 옅어진다";
        else if (lost > allow) why = $"미리 돌려 보니 공기를 {lost:0}몰 잃는다 (셈한 것은 {st.From:0}몰)";
        if (dbg) Console.WriteLine($"      [미리 보기] {sw.ElapsedMilliseconds}ms");
        Say(sim, $"미리 돌려 보기: {DoorName(st.Obj)}을 열면 5분 뒤 — 쓰러짐 {down} · 잃는 공기 {lost:0}몰 (셈 {st.From:0}) → {(why.Length == 0 ? "연다" : "하지 않는다")}");
        if (why.Length > 0) { RehearsalVetoes++; return false; }
        return true;
    }

    private void SavePlan(StateWriter w) { w.List(Attempts); w.List(PlanSteps); w.I32(IncidentSeq); w.I64(Devises); w.I64(DevisesFound); w.I64(DevisesFailed); w.I64(PlanFails); w.I64(PlansDone); w.I64(Rehearsals); w.I64(RehearsalVetoes); w.Str(LastDevised); }
    private void LoadPlan(StateReader r)
    {
        Attempts = r.List<Tried>(); PlanSteps = r.List<PlanStepRec>(); IncidentSeq = r.I32();
        Devises = r.I64(); DevisesFound = r.I64(); DevisesFailed = r.I64(); PlanFails = r.I64(); PlansDone = r.I64(); Rehearsals = r.I64(); RehearsalVetoes = r.I64(); LastDevised = r.Str() ?? "";
    }
}
