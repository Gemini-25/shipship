namespace ShipEngine;

/// <summary>
/// ⑬ 연출 — 볼 곳을 잘 고르는 카메라 (깊이 바퀴 2). 세계를 바꾸지 않는다 — 읽기만 한다.
/// <list type="bullet">
/// <item>사슬 따라가기: 지금 보는 사슬에서 이어진 일은 1.5배 · 사슬이 새 엔진으로 번진 곳(구조 → 공기 → 몸 → 사람 …)은 2배이고 화면 밖이면 2초 뒤 그리로 간다.</item>
/// <item>몸으로 읽는 조짐 (카메라만 쓰는 관심 — 하이라이트 문턱에는 넣지 않는다): 12초마다 사람이 선 칸의 공기(산소 분압 · 기압 · 기압이 떨어지는 빠르기 · 일산화탄소 · 열 · 곁의 불)를 읽어 — 몸이 알기 전에 그 사람에게 간다 (자는 사람은 더).</item>
/// <item>주인공 따라가기: 장면을 고를 때 그 자리에서 가장 컸던 일의 사람을 주인공으로 — 업고 뛰거나 도망치거나 불을 끄거나 쓰러져 있으면 따라간다 (업혀 가면 업은 이를).</item>
/// <item>두 곳 오가기: 화면 밖에 지금 장면의 반쯤 되는 일이 20초 넘게 안 보였으면 잠깐 그리로 (결정적 순간엔 자르지 않는다).</item>
/// <item>조용할 때 일상: 볼 만한 일이 없으면 사람이 있는 방을 차례로 비춘다 (깨어 있는 이가 있는 방 · 가장 오래 안 본 방부터 · 한 방에 15초 · 다들 자면 자는 방).</item>
/// </list>
/// </summary>
public sealed partial class Director
{
    public const float ContinueBoost = 1.5f, SpreadBoost = 2f;
    public const double PullHoldSec = 2, CrossHoldSec = 20;       // 화면 초
    public const int FollowReach = 30;                            // 주인공을 따라가는 거리 (칸)
    public const float SubjectHeat = 1;                           // 주인공이 지나가는 칸에 남기는 관심 (틱마다)
    public const int SenseEvery = 5;                              // 몸으로 읽는 조짐: 5틱(12초)마다

    /// <summary>지금 장면의 사슬 뿌리 · 주인공 (에이전트 · −1 없음).</summary>
    public int SceneRoot, Subject = -1;
    public int Pulls, CrossCuts, AirWarnings;
    public long DailyCuts;
    public const double DailyHoldSec = 15;                        // 조용할 때 한 방에 (화면 초)
    private int _dailyRoom = -1;
    private double _dailyAt, _cutAt;
    private int[] _roomAwake = Array.Empty<int>(), _roomAll = Array.Empty<int>();
    private double[] _roomShown = Array.Empty<double>();
    private (int x0, int y0, int x1, int y1)[] _roomBox = Array.Empty<(int, int, int, int)>();
    public long FollowTicks;
    private int _pullCell = -1, _pullRoot, _pullActor = -1;
    private double _pullAt = double.MinValue / 2; private float _pullScore;
    private long _subjectQuiet;
    private readonly Dictionary<int, (int cell, double kpa, long tick, double rate)> _airKPa = new();
    private const double NormalKPa = 101.3;
    private readonly Dictionary<int, float> _airBase = new();
    public readonly Dictionary<string, int> AirWhy = new();
    public string LastAir = "";

    // 최근 무게 있는 일 (장면을 고를 때 그 자리의 사슬 · 주인공을 찾는다)
    private const int RecentSize = 48;
    private readonly (int cell, int root, int actor, float score, long tick)[] _recent = new (int, int, int, float, long)[RecentSize];
    private int _recentN, _recentAt;

    private void Recent(int cell, int root, int actor, float score, long t)
    {
        _recent[_recentAt] = (cell, root, actor, score, t);
        _recentAt = (_recentAt + 1) % RecentSize; _recentN = Math.Min(RecentSize, _recentN + 1);
    }

    /// <summary>사실의 사람 (에이전트 번호) — 생명의 사실은 몸 번호라 바꾼다.</summary>
    private static int AgentOf(Mind? mind, in Fact f)
    {
        if (mind == null || f.Actor < 0) return -1;
        int a = f.Kind is FactKind.Died or FactKind.Collapsed or FactKind.Hurt or FactKind.Sick ? mind.AgentOfBody(f.Actor) : f.Actor;
        return a >= 0 && a < mind.Agents.Count ? a : -1;
    }

    /// <summary>다른 곳으로 '자르기' — 장면이 바뀌면 그 자리에서 최근 가장 컸던 일의 사슬과 사람을 장면의 사슬 · 주인공으로.</summary>
    private void CutTo(Sim sim, int cell, long t, int root = -1, int actor = -1)
    {
        bool fresh = FocusCell < 0 || _resting || Far(cell, FocusCell);
        if (FocusCell >= 0 && !_resting && Far(cell, FocusCell)) { Switches++; _sinceSwitch = 0; }
        FocusCell = cell; _resting = false; _cutAt = ScreenSeconds;
        if (!fresh && root < 0) return;
        int bestRoot = 0, bestActor = -1; float bs = 0;
        for (int k = 0; k < _recentN; k++)
        {
            var r = _recent[k];
            if (t - r.tick > Time.Minutes(10) || Math.Max(Math.Abs(L.X(r.cell) - L.X(cell)), Math.Abs(L.Y(r.cell) - L.Y(cell))) > SpotRadius) continue;
            if (r.score > bs) { bs = r.score; bestRoot = r.root; bestActor = r.actor; }
        }
        SceneRoot = root >= 0 ? root : bestRoot;
        Subject = actor >= 0 ? actor : bestActor;
        _subjectQuiet = t;
    }

    /// <summary>
    /// 조용할 때 일상 (함장 요청 — 새 엔진 판에서 엔진이 도는 것이 보이게): 볼 만한 일이 없으면 빈 배 가운데에 쉬지 않고 사람이 있는 방을 차례로 비춘다 —
    /// 깨어 있는 이가 있는 방 가운데 가장 오래 안 본 방 (처음이면 사람이 많은 방) · 한 방에 15초 (화면) · 다들 자면 자는 방 · 그 방 사람들 가운데쯤의 사람에게 · 방 넓이만큼 확대.
    /// 쉬는 것으로 치니 (_resting) 첫 일에는 곧장 그리로 자른다.
    /// </summary>
    private void Daily(Sim sim)
    {
        _resting = true;
        var mind = sim.Find<Mind>(); var life = sim.Find<Life>();
        int n = L.Rooms.Count;
        if (mind == null || life == null || n == 0) { if (FocusCell < 0) FocusCell = L.Index(L.W / 2, L.H / 2); return; }
        if (_roomAll.Length != n) RoomsInit(n);
        Array.Clear(_roomAwake); Array.Clear(_roomAll);
        bool anyAwake = false;
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson || a.C.Body < 0 || a.C.Cell < 0) continue;
            var b = life.Bodies[a.C.Body];
            if (b.Alive == 0) continue;
            int r = L.Room[a.C.Cell];
            if (r < 0 || r >= n) continue;
            _roomAll[r]++;
            if (b.Asleep == 0) { _roomAwake[r]++; anyAwake = true; }
        }
        int[] count = anyAwake ? _roomAwake : _roomAll;
        bool keep = _dailyRoom >= 0 && _dailyRoom < n && count[_dailyRoom] > 0 && ScreenSeconds - _dailyAt < DailyHoldSec;
        if (!keep)
        {
            int pick = -1;
            for (int r = 0; r < n; r++)
            {
                if (count[r] == 0) continue;
                if (pick < 0 || _roomShown[r] < _roomShown[pick] || _roomShown[r] == _roomShown[pick] && count[r] > count[pick]) pick = r;
            }
            if (pick < 0) { if (FocusCell < 0) FocusCell = L.Index(L.W / 2, L.H / 2); _dailyRoom = -1; return; }
            if (pick != _dailyRoom) { DailyCuts++; _dailyRoom = pick; }
            _dailyAt = ScreenSeconds;
        }
        _roomShown[_dailyRoom] = ScreenSeconds;
        // 그 방 사람들 가운데쯤에 선 사람
        double sx = 0, sy = 0; int k = 0;
        foreach (var a in mind.Agents)
            if (a.IsPerson && a.C.Cell >= 0 && L.Room[a.C.Cell] == _dailyRoom) { sx += L.X(a.C.Cell); sy += L.Y(a.C.Cell); k++; }
        if (k == 0) return;
        sx /= k; sy /= k;
        int best = -1; double bd = double.MaxValue;
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson || a.C.Cell < 0 || L.Room[a.C.Cell] != _dailyRoom) continue;
            double dd = (L.X(a.C.Cell) - sx) * (L.X(a.C.Cell) - sx) + (L.Y(a.C.Cell) - sy) * (L.Y(a.C.Cell) - sy);
            if (dd < bd) { bd = dd; best = a.C.Cell; }
        }
        FocusCell = best;
        var (bx0, by0, bx1, by1) = _roomBox[_dailyRoom];
        Zoom = Math.Clamp(Math.Max(bx1 - bx0 + 1, (by1 - by0 + 1) * 2) + 6, 14, 40);
    }

    /// <summary>방마다 일상 비춤 표 (사람 수 · 마지막으로 비춘 화면 초 · 방 상자) — 처음 일상을 비출 때 · 연출 기록을 되살릴 때.</summary>
    private void RoomsInit(int n)
    {
        _roomAwake = new int[n]; _roomAll = new int[n]; _roomShown = new double[n]; _roomBox = new (int, int, int, int)[n];
        Array.Fill(_roomShown, double.MinValue);
        for (int r = 0; r < n; r++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            foreach (int c in L.Rooms[r].Cells) { x0 = Math.Min(x0, L.X(c)); y0 = Math.Min(y0, L.Y(c)); x1 = Math.Max(x1, L.X(c)); y1 = Math.Max(y1, L.Y(c)); }
            _roomBox[r] = (x0, y0, x1, y1);
        }
    }

    /// <summary>칸 c 가까이(반경 SpotRadius · 10분 안)의 일이 지금 장면의 것인가 (같은 사슬 · 같은 주인공).</summary>
    private bool SameScene(int c, long t)
    {
        if (SceneRoot == 0 && Subject < 0) return false;
        for (int k = 0; k < _recentN; k++)
        {
            var r = _recent[k];
            if (t - r.tick > Time.Minutes(10) || Math.Max(Math.Abs(L.X(r.cell) - L.X(c)), Math.Abs(L.Y(r.cell) - L.Y(c))) > SpotRadius) continue;
            if (SceneRoot != 0 && r.root == SceneRoot || Subject >= 0 && r.actor == Subject) return true;
        }
        return false;
    }

    /// <summary>주인공이 지금 있는 칸 — 이야기 속에 있으면 (업음 · 업힘 · 도망 · 공황 · 구하기 · 불 끄기 · 구멍 막기 · 대피 · 몸싸움 · 쓰러짐) 따라간다. 30초 조용하면 놓는다.</summary>
    private int SubjectCell(Sim sim, long t)
    {
        if (Subject < 0) return -1;
        var mind = sim.Find<Mind>(); var life = sim.Find<Life>();
        if (mind == null || Subject >= mind.Agents.Count) { Subject = -1; return -1; }
        var c = mind.Agents[Subject].C;
        bool down = false;
        if (c.Body >= 0 && life != null)
        {
            var b = life.Bodies[c.Body];
            if (b.Alive == 0) { Subject = -1; return -1; }
            down = b.Conscious == 0;
        }
        bool story = down || c.Mode != 0 || c.Carrying >= 0 || c.CarriedBy >= 0
            || c.Goal is GoalKind.Rescue or GoalKind.FightFire or GoalKind.Patch or GoalKind.Evacuate or GoalKind.Brawl or GoalKind.GetSuit;
        if (story) _subjectQuiet = t;
        else if (t - _subjectQuiet > Time.Minutes(0.5)) { Subject = -1; return -1; }
        int who = c.CarriedBy >= 0 && c.CarriedBy < mind.Agents.Count ? c.CarriedBy : Subject;
        return mind.Agents[who].C.Cell;
    }

    /// <summary>몸으로 읽는 조짐: 사람이 선 칸의 공기가 나빠지고 있으면 (몸은 아직 모른다) 그 사람에게 관심을 둔다.</summary>
    private void Sense(Sim sim, Mind mind, Life life, Chemistry? chem, long t)
    {
        var F = sim.Find<Field>();
        if (F == null) return;
        var T = F.T;
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson || a.C.Body < 0) continue;
            var b = life.Bodies[a.C.Body];
            if (b.Alive == 0 || b.Cell < 0) continue;
            int c = b.Cell;
            // 떨어지는 빠르기는 같은 칸에서 1분에 걸쳐 잰다 — 사람이 기압이 조금 낮은 방으로 걸어 들어간 것 · 문이 여닫혀 1~3kPa 출렁이는 것은 떨어짐이 아니다
            // (1분 전 그 사람이 있던 칸의 지금 기압 ↔ 그때 기압 · 배의 보통 기압보다 2kPa 넘게 낮아졌을 때만)
            double p = F.Pressure(c);
            if (!_airKPa.TryGetValue(a.Id, out var v) || t - v.tick >= Time.Minutes(1))
            {
                double rate = v.tick > 0 || _airKPa.ContainsKey(a.Id) ? (v.kpa - F.Pressure(v.cell)) * Time.Minutes(1) / Math.Max(1, t - v.tick) : 0;
                v = (c, p, t, rate);
                _airKPa[a.Id] = v;
            }
            double fall = p < NormalKPa - 2 ? Math.Max(0, v.rate) : 0;    // kPa/분
            double o2 = F.Partial(T.gO2, c), co = F.Ppm(T.gCO, c), k = F.Temp(c);
            float tO2 = Clamp01((16 - o2) / 8), tP = Clamp01((60 - p) / 40), tCO = Clamp01((co - 100) / 800), tK = Clamp01((k - 320) / 60), tFall = 0.8f * Clamp01(fall / 4); // 분에 4kPa면 큰일 — 20분이면 숨 쉴 수 없다
            float tFire = chem != null && chem.BurningCount > 0 && FireNear(chem, c, 2) ? 0.8f : 0;
            float threat = Math.Max(Math.Max(Math.Max(tO2, tP), Math.Max(tCO, tK)), Math.Max(tFall, tFire));
            string why = threat == tO2 ? "산소" : threat == tP ? "기압" : threat == tCO ? "일산화탄소" : threat == tK ? "열" : threat == tFall ? "기압 떨어짐" : "불";
            if (b.Suit != 0) threat *= 0.3f;                                // 우주복 — 그래도 볼 만하다
            // 익숙해짐: 30분에 걸쳐 그 사람의 기준이 따라 오른다 — 며칠째 옅은 산소 속에 사는 것은 조짐이 아니고 · 더 나빠지는 만큼만 (꾸준히 새는 방은 계속 나빠진다)
            float base0 = _airBase.GetValueOrDefault(a.Id);
            _airBase[a.Id] = base0 + (threat - base0) * SenseEvery / (float)Time.Minutes(30);
            threat -= base0;
            if (threat < 0.15f) continue;
            if (b.Asleep != 0) threat = Math.Min(1, threat * 1.5f);        // 자는 사이에 공기가 나빠진다
            float add = 25 * threat;
            Aux[c] += add;
            Recent(c, 0, a.Id, add, t);
            if (threat >= 0.3f) _lastOmen = t;
            AirWarnings++;
            if (b.Suit != 0) why += "(우주복)";
            AirWhy[why] = AirWhy.GetValueOrDefault(why) + 1;
            LastAir = $"{why} {threat:0.00} · 칸 {c} 방 {L.Room[c]} · {p:0.0}kPa · 떨어짐 {fall:0.0}kPa/분 · 산소 {o2:0.0} · {k - 273.15:0}℃ · 우주복 {b.Suit} · 잠 {b.Asleep}";
        }
    }

    private bool FireNear(Chemistry chem, int c, int r)
    {
        int x0 = L.X(c), y0 = L.Y(c);
        for (int y = Math.Max(0, y0 - r); y <= Math.Min(L.H - 1, y0 + r); y++)
            for (int x = Math.Max(0, x0 - r); x <= Math.Min(L.W - 1, x0 + r); x++)
                if (chem.IsBurning(L.Index(x, y))) return true;
        return false;
    }

    private static float Clamp01(double v) => (float)Math.Clamp(v, 0, 1);
}
