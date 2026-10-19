namespace ShipEngine;

/// <summary>⑨ 문화의 일 (사실 '관습'의 Size).</summary>
public enum CultureEvent : byte { None, Custom, CustomLost, Taboo, TabooLifted, BedTaboo, Nickname, Memorial, Celebration, Rule, RuleRejected, RuleBroken, MemorialDeferred }

/// <summary>별명 — 기리는 넷 · 놀리는 하나.</summary>
public enum Nickname : byte { None, Rescuer, FireChief, Patcher, Handy, Forgetful }

/// <summary>
/// ⑨ 문화 · 관습 (깊이 바퀴 3 · docs/ENGINE.md ⑨). 줄거리를 짜지 않는다 — 되풀이되는 일이 배의 관습이 되고, 관습은 다시 사람을 움직인다:
/// <para><b>함께 먹는 때</b>: 식당에서 셋에 하나 넘게 (둘 이상) 함께 먹은 시각(한 시간 칸)을 날마다 적고 · 지난 이레 중 사흘 이상 같은 칸이면 관습이 된다 (⑧이 그 시각에 먹으러 간다) ·
/// 관습의 식사를 함께 하면 기쁨 +0.1 · 고립의 피로 −0.01 (하루 한 번) · 이레 동안 하루도 차지 않으면 사라진다.</para>
/// <para><b>금기</b>: 살아 있는 이의 반 넘게 꺼리는 방 (장소의 기억 0.4 넘게) → 아무도 그 방에서 쉬지 않는다 · 다섯에 하나 아래로 줄면 풀린다 /
/// 숨진 이의 침대는 이레 동안 아무도 눕지 않는다.</para>
/// <para><b>별명</b>: 구조 셋 '구조대장' · 불 끄기 셋 '불끄기 반장' · 구멍 막기 셋 '땜장이' · 고치기 다섯 '손재주꾼' / 켜 둔 조리대 둘 '깜빡이' — 가장 외향인 동료가 부르기 시작한다.</para>
/// <para><b>추모 · 축하</b>는 회의처럼 식당으로 모인다 (부르고 · 말하는 것은 사람 — MindGatherings · 모임의 차례와 결과는 SocietyCouncil). <b>배의 규칙</b>은 되풀이되는 마찰(밤 말소리 · 켜 둔 조리대)이 회의 안건이 되어 표결로 정한다.</para>
/// 실제: 남극 월동대 · 잠수함 · 국제우주정거장의 함께 먹는 저녁 · 기념일 · 별명 · 공동 규칙 (Stuster 1996 'Bold Endeavors').
/// </summary>
public sealed partial class Society
{
    public const int MealWindowDays = 7, MealCustomDays = 3;
    public const double BedTabooDays = 7, MournAfterHours = 6, FeastAfterHours = 6, RuleWindowDays = 3;
    /// <summary>관습이 된 함께 먹는 시각 (시 · −1 없음).</summary>
    public int MealCustom = -1;
    /// <summary>배의 규칙: 조용한 밤 · 조리대.</summary>
    public bool QuietRule, StoveRule;
    public long Customs, CustomsLost, CustomMealsShared, Taboos, TaboosLifted, BedTaboos, Nicknames, Memorials, Celebrations, RulesMade, RulesRejected, RuleBreaks, QuietKept, Gatherings, GatheringsFailed, MemorialsDeferred;
    private readonly HashSet<int> _mournDeferred = new();   // 수습 때문에 추모를 미룬다고 이미 남긴 숨진 이
    /// <summary>사람마다 별명 · 처음 부른 이.</summary>
    public byte[] Nick = Array.Empty<byte>();
    public int[] NickBy = Array.Empty<int>();
    private byte[] _mealSeen = new byte[MealWindowDays * 24];
    private long _mealDay = -1;
    private byte[] _roomTaboo = Array.Empty<byte>();
    private int[] _deeds = Array.Empty<int>(), _deedCause = Array.Empty<int>();
    private long[] _feastDay = Array.Empty<long>();
    private readonly List<(int dead, long at, int cause)> _mourn = new();
    private long _feastFrom = -1, _feastLast = long.MinValue / 2;
    private int _feastCause, _feastHero = -1, _feastWeight;
    private byte _feastNews;
    private readonly List<(long t, int woken, int by, int cause)> _wakeLog = new();
    private readonly List<(long t, int who, int cause)> _stoveLog = new();
    private long _askQuiet = long.MinValue / 2, _askStove = long.MinValue / 2, _cultureCursor;

    private static readonly int[] DeedsFor = { 0, 3, 3, 3, 5, 2 };
    public static string NickText(Nickname n) => n switch
    {
        Nickname.Rescuer => "구조대장", Nickname.FireChief => "불끄기 반장", Nickname.Patcher => "땜장이", Nickname.Handy => "손재주꾼", Nickname.Forgetful => "깜빡이", _ => "",
    };
    /// <summary>그 사람의 별명 (없으면 null) — 사람 판 · 대화.</summary>
    public string? NickOf(int a) => a >= 0 && a < Nick.Length && Nick[a] != 0 ? NickText((Nickname)Nick[a]) : null;
    public bool RoomTaboo(int room) => room >= 0 && room < _roomTaboo.Length && _roomTaboo[room] != 0;

    /// <summary>화면의 한 줄: 이 배의 관습 · 금기 · 규칙 (없으면 null).</summary>
    public string? CultureLine(Sim sim)
    {
        var parts = new List<string>();
        if (MealCustom >= 0) parts.Add($"{HourWord(MealCustom)}엔 함께 먹는다");
        for (int r = 0; r < _roomTaboo.Length; r++) if (_roomTaboo[r] != 0) parts.Add($"아무도 {L.Rooms[r].Name}에서 쉬지 않는다");
        if (QuietRule) parts.Add("밤엔 자는 이 곁에서 말하지 않는다");
        if (StoveRule) parts.Add("조리대를 떠날 땐 끈다");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private void CultureArrays()
    {
        if (Nick.Length < N)
        {
            int old = Nick.Length;
            Array.Resize(ref Nick, N); Array.Resize(ref NickBy, N); Array.Resize(ref _feastDay, N);
            Array.Resize(ref _deeds, N * 6); Array.Resize(ref _deedCause, N * 6);
            for (int i = old; i < N; i++) NickBy[i] = -1;
        }
        if (_roomTaboo.Length < L.Rooms.Count) Array.Resize(ref _roomTaboo, L.Rooms.Count);
    }

    /// <summary>1분마다 (회의보다 먼저): 일어난 일을 보고 · 함께 먹는 이를 세고 · 10분마다 금기 · 한 시간마다 관습을 본다.</summary>
    private void CultureStep(Sim sim)
    {
        CultureArrays();
        CultureFacts(sim);
        MealWatch(sim);
        if (sim.Tick % Time.Minutes(10) == 0) TabooWatch(sim);
        if (sim.Tick % Time.TicksPerHour == 0) MealCustomWatch(sim);
    }

    private static string HourWord(int h) => h switch
    {
        0 => "밤 12시", < 5 => $"새벽 {h}시", < 12 => $"아침 {h}시", 12 => "낮 12시", < 17 => $"낮 {h - 12}시", < 21 => $"저녁 {h - 12}시", _ => $"밤 {h - 12}시",
    };

    private int Culture(Sim sim, CultureEvent e, string text, int cell, int parent, int actor, int other, int parent2 = 0)
    {
        int c = sim.Causes.Add(CauseKind.Society, text, sim.Tick, cell, parent, parent2, actor);
        sim.Facts.Emit(sim.Tick, FactKind.Culture, cell, (int)e, c, actor, other);
        return c;
    }

    // ───────────────────────── 일어난 일 ─────────────────────────

    private void CultureFacts(Sim sim)
    {
        var mind = _mind!;
        foreach (var f in sim.Facts.Since(_cultureCursor))
        {
            switch (f.Kind)
            {
                case FactKind.Died:
                {
                    int dead = mind.AgentOfBody(f.Actor);
                    if (dead < 0) break;
                    _mourn.Add((dead, f.Tick, f.Cause));
                    _feastFrom = -1;   // 누가 숨졌다 — 축하는 없다
                    int bed = C(dead).Bed;
                    if (bed >= 0)
                    {
                        mind.TabooBed(bed, f.Tick + Time.Days(BedTabooDays));
                        BedTaboos++;
                        Culture(sim, CultureEvent.BedTaboo, $"숨진 {mind.NameOf(dead)}의 침대엔 아무도 눕지 않는다 — 이레 동안", bed, f.Cause, dead, mind.RoomOf(bed));
                    }
                    break;
                }
                case FactKind.Ignition or FactKind.FireSpread or FactKind.WallBreach or FactKind.RegionVented or FactKind.Arrest or FactKind.Collapsed:
                {
                    if (_feastFrom < 0) { _feastFrom = f.Tick; _feastCause = f.Cause; _feastWeight = 0; _feastNews = 0; _feastHero = -1; }
                    _feastWeight += f.Kind is FactKind.Ignition or FactKind.FireSpread ? 1 : 2;   // 불 한 번은 작은 일 — 번지거나 · 뚫리거나 · 사람이 쓰러져야 넘긴 '위기'다
                    break;
                }
                case FactKind.StorySeed when f.Other == (int)SeedKind.GoodNews:
                case FactKind.Burn when f.Other == (int)BurnKind.Arrive:
                    if (_feastFrom < 0) { _feastFrom = f.Tick; _feastCause = f.Cause; _feastWeight = 2; _feastNews = 1; _feastHero = -1; }
                    break;
                case FactKind.Rescue or FactKind.Extinguished or FactKind.Patched or FactKind.Repaired when f.Actor >= 0 && f.Actor < N && mind.Agents[f.Actor].IsPerson:
                {
                    if (_feastFrom >= 0 && f.Kind != FactKind.Repaired) _feastHero = f.Actor;
                    Deed(sim, f.Actor, f.Kind switch { FactKind.Rescue => Nickname.Rescuer, FactKind.Extinguished => Nickname.FireChief, FactKind.Patched => Nickname.Patcher, _ => Nickname.Handy }, f.Cause, f.Cell);
                    break;
                }
                case FactKind.Mistake when f.Size < 1 && f.Actor >= 0 && f.Actor < N:   // 켜 둔 조리대 (⑧ 밥 짓기)
                    _stoveLog.Add((f.Tick, f.Actor, f.Cause));
                    Deed(sim, f.Actor, Nickname.Forgetful, f.Cause, f.Cell);
                    break;
                case FactKind.Woke when f.Actor >= 0 && f.Actor < N && f.Other >= 0 && f.Other < N && f.Cause != 0:
                {
                    _wakeLog.Add((f.Tick, f.Actor, f.Other, f.Cause));
                    double hour = Time.HourOfDay(f.Tick);
                    if (QuietRule && (hour >= 22 || hour < 6) && Alive(f.Actor))
                    {
                        // 밤의 규칙을 어겼다: 탓이 두 배 (⑨ 사건에서 한 번 · 여기서 한 번 더) · 모두가 안다
                        RuleBreaks++;
                        int c = Culture(sim, CultureEvent.RuleBroken, $"{Ko.I(mind.NameOf(f.Other))} 밤의 규칙을 어겼다 — {Ko.I(mind.NameOf(f.Actor))} 말소리에 깼다", f.Cell, f.Cause, f.Other, 1);
                        Blame(sim, f.Actor, f.Other, 0.08f * (0.5f + C(f.Actor).Neuro), c, f.Cell, justWoke: true);
                    }
                    break;
                }
            }
        }
        _cultureCursor = sim.Facts.Total;
        _wakeLog.RemoveAll(x => sim.Tick - x.t > Time.Days(RuleWindowDays));
        _stoveLog.RemoveAll(x => sim.Tick - x.t > Time.Days(RuleWindowDays));
    }

    // ───────────────────────── 별명 ─────────────────────────

    private void Deed(Sim sim, int a, Nickname kind, int cause, int cell)
    {
        int k = a * 6 + (int)kind;
        _deeds[k]++; _deedCause[k] = cause;   // 별명은 여기서 붙지 않는다 — 그 일을 아는 동료가 이야기하다 부르기 시작한다 (NickInTalk)
    }

    /// <summary>그 사람이 x의 그 일을 아나: 함께 겪었거나 들어서 기억한다 (x에 대한 기억) · 함께 지내 잘 안다 (아는 정도 0.3 넘게) ·
    /// 고친 일 · 켜 둔 조리대는 배 사람이 다 안다 (일감판 · 주컴퓨터가 끈 조리대).</summary>
    private bool KnowsDeeds(int by, int x, Nickname kind)
    {
        if (kind is Nickname.Handy or Nickname.Forgetful || KnownOf(by, x) > 0.3f) return true;
        foreach (var m in Memories) if (m.Owner == by && m.About == x && m.Topic is Topic.Rescue or Topic.Fire or Topic.Breach) return true;
        return false;
    }

    /// <summary>
    /// 이야기를 나눈 둘 — 한쪽이 별명이 붙을 만큼 일을 쌓았고 · 다른 쪽이 그 일을 알면 그렇게 부르기 시작한다 (말한 이의 판단):
    /// 기리는 별명은 그를 아끼거나 (정 0.2 넘게) 외향인 이 (0.5 넘게)가 · 놀리는 별명은 덜 다정한 이 (친화 0.6 아래)가 꺼낸다.
    /// </summary>
    private void NickInTalk(Sim sim, int i, int j)
    {
        CultureArrays();
        foreach (var (x, by) in new[] { (i, j), (j, i) })
        {
            if (Nick[x] != 0 || !Alive(x) || !Awake(by)) continue;
            for (int kind = 1; kind <= 5; kind++)
            {
                if (_deeds[x * 6 + kind] < DeedsFor[kind] || !KnowsDeeds(by, x, (Nickname)kind)) continue;
                ref var cb = ref C(by);
                bool tease = kind == (int)Nickname.Forgetful;
                if (tease ? cb.Agree >= 0.6f : BondOf(by, x) <= 0.2f && cb.Extra <= 0.5f) continue;
                Coin(sim, x, by, (Nickname)kind);
                return;
            }
        }
    }

    private void Coin(Sim sim, int a, int by, Nickname kind)
    {
        int k = a * 6 + (int)kind;
        Nick[a] = (byte)kind; NickBy[a] = by; Nicknames++;
        string nick = NickText(kind);
        string why = kind switch
        {
            Nickname.Rescuer => $"사람을 {_deeds[k]}번 구해 냈다", Nickname.FireChief => $"불을 {_deeds[k]}번 껐다", Nickname.Patcher => $"구멍을 {_deeds[k]}번 막았다",
            Nickname.Handy => $"고장 난 것을 {_deeds[k]}번 고쳤다", _ => $"조리대를 {_deeds[k]}번 켜 둔 채 떠났다",
        };
        int c = Culture(sim, CultureEvent.Nickname, $"{Ko.I(_mind!.NameOf(by))} {Ko.Eul(_mind.NameOf(a))} '{nick}'{(Ko.HasFinal(nick) ? "이라" : "라")} 부르기 시작했다 — {why}", C(a).Cell, _deedCause[k], a, (int)kind);
        ref var ca = ref C(a);
        if (kind == Nickname.Forgetful)
        {
            ca.Sad = Math.Min(1, ca.Sad + 0.1f);   // 놀림 — 신경이 예민한 이는 부른 이를 조금 원망한다
            if (ca.Neuro > 0.6f) Add(Grudge, a, by, 0.05f);
        }
        else
        {
            ca.Joy = Math.Min(1, ca.Joy + 0.2f);   // 기림 — 모두 그를 조금 더 믿는다
            foreach (int j in People()) if (j != a) Add(TrustOf, j, a, 0.05f);
        }
        foreach (int j in People()) if (j != a) Remember(sim, j, kind == Nickname.Forgetful ? Topic.Gossip : Topic.Work, a, c, 0.3f, C(a).Cell);
    }

    // ───────────────────────── 함께 먹는 때 ─────────────────────────

    private bool EatingIn(int i, int room)
    {
        ref var c = ref C(i);
        if (c.Goal != GoalKind.Eat || _mind!.RoomOf(c.Cell) != room) return false;
        var plan = _mind.Agents[i].Plan;
        return c.StepIndex < plan.Count && plan[c.StepIndex].Kind == StepKind.Use;
    }

    private void MealWatch(Sim sim)
    {
        long day = sim.Tick / Time.TicksPerDay;
        if (day != _mealDay)
        {
            for (long d = Math.Max(_mealDay + 1, day - MealWindowDays + 1); d <= day; d++) Array.Clear(_mealSeen, (int)(d % MealWindowDays) * 24, 24);
            _mealDay = day;
        }
        int mess = _mind!.MessRoom();
        if (mess < 0) return;
        var people = People();
        var eating = new List<int>();
        foreach (int i in people) if (EatingIn(i, mess)) eating.Add(i);
        int need = Math.Max(2, (people.Count + 2) / 3);   // 셋에 하나 (둘 이상) — 교대로 먹는 배에서도 같은 때 같은 식탁이 되풀이되면
        if (eating.Count < need) return;
        int hour = (int)Time.HourOfDay(sim.Tick);
        _mealSeen[(int)(day % MealWindowDays) * 24 + hour] = 1;
        if (MealCustom < 0) return;
        double dh = ((Time.HourOfDay(sim.Tick) - MealCustom) % 24 + 24) % 24;
        if (dh >= 1 && dh < 24 - 1 / 6.0) return;   // 관습의 식사 때 (10분 앞부터 그 한 시간)
        foreach (int i in eating)
        {
            if (_feastDay[i] == day + 1) continue;
            _feastDay[i] = day + 1;
            ref var c = ref C(i);
            c.Joy = Math.Min(1, c.Joy + 0.1f); c.Weary = Math.Max(0, c.Weary - 0.01f);
            CustomMealsShared++;
        }
    }

    /// <summary>지난 이레 가운데 그 때(앞뒤 한 시간)에 함께 먹은 날 — 교대로 먹는 배에서도 '저녁 즈음'은 날마다 한 시간쯤 어긋난다.</summary>
    private int MealDays(int hour)
    {
        int n = 0;
        for (int d = 0; d < MealWindowDays; d++)
            if (_mealSeen[d * 24 + (hour + 23) % 24] + _mealSeen[d * 24 + hour] + _mealSeen[d * 24 + (hour + 1) % 24] > 0) n++;
        return n;
    }
    private int MealDaysExact(int hour) { int n = 0; for (int d = 0; d < MealWindowDays; d++) n += _mealSeen[d * 24 + hour]; return n; }

    private void MealCustomWatch(Sim sim)
    {
        int mess = _mind!.MessRoom();
        int cell = mess >= 0 ? RoomMiddle(mess) : -1;
        if (MealCustom >= 0)
        {
            if (MealDays(MealCustom) > 0) return;
            int was = MealCustom;
            MealCustom = -1; _mind.SetMealCustom(-1); CustomsLost++;
            Culture(sim, CultureEvent.CustomLost, $"{HourWord(was)}에 함께 먹던 관습이 사라졌다 — 이레 동안 모이지 않았다", cell, 0, -1, was);
            return;
        }
        int best = -1, bn = 0, be = 0;
        for (int h = 0; h < 24; h++) { int n = MealDays(h), e = MealDaysExact(h); if (n > bn || n == bn && e > be) { bn = n; be = e; best = h; } }   // 같으면 바로 그 시각에 더 자주 먹은 칸
        if (bn < MealCustomDays) return;
        MealCustom = best; _mind.SetMealCustom(best); Customs++;
        Culture(sim, CultureEvent.Custom, $"{HourWord(best)}엔 함께 먹는다 — 이 배의 관습이 되었다 (이레 가운데 {bn}날)", cell, 0, -1, best);
    }

    // ───────────────────────── 금기 ─────────────────────────

    private void TabooWatch(Sim sim)
    {
        var people = People();
        if (people.Count < 2) return;
        var mind = _mind!;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int n = 0;
            foreach (int i in people) if (mind.Dread(i, r) > 0.4f) n++;
            if (_roomTaboo[r] == 0 && n * 2 > people.Count)
            {
                _roomTaboo[r] = 1; mind.SetTaboo(r, true); Taboos++;
                // 그 방에서 겪은 가장 최근의 무서운 일이 원인
                int parent = 0; long pt = long.MinValue;
                foreach (var m in Memories) if (m.Tick > pt && m.Cell >= 0 && mind.RoomOf(m.Cell) == r && m.Topic is Topic.Fire or Topic.Breach or Topic.Hurt or Topic.Death or Topic.Fight) { pt = m.Tick; parent = m.Cause; }
                Culture(sim, CultureEvent.Taboo, $"아무도 {L.Rooms[r].Name}에서 쉬지 않는다 — {people.Count}명 가운데 {n}명이 그 방을 꺼린다", RoomMiddle(r), parent, -1, r);
            }
            else if (_roomTaboo[r] != 0 && n * 5 < people.Count)
            {
                _roomTaboo[r] = 0; mind.SetTaboo(r, false); TaboosLifted++;
                Culture(sim, CultureEvent.TabooLifted, $"{L.Rooms[r].Name}의 금기가 풀렸다 — 사람들이 다시 그 방에서 쉰다", RoomMiddle(r), 0, -1, r);
            }
        }
    }

    // ───────────────────────── 추모 · 축하 (모임) ─────────────────────────

    // ───────────────────────── 판단 엔진이 읽는 것 (안건이 될 사실) ─────────────────────────

    /// <summary>기릴 숨진 이 (숨진 이 · 때 · 원인) — 사흘이 지나면 지운다.</summary>
    public IReadOnlyList<(int dead, long at, int cause)> Mourning => _mourn;
    /// <summary>그 숨진 이의 추모를 미뤘다고 이미 말했나.</summary>
    public bool MournDeferred(int dead) => _mournDeferred.Contains(dead);
    /// <summary>넘긴 위기 · 반가운 소식 (때 −1 없음 · 원인 · 무게 · 소식인가 · 앞장선 이).</summary>
    public (long from, int cause, int weight, bool news, int hero) Feast => (_feastFrom, _feastCause, _feastWeight, _feastNews != 0, _feastHero);
    public long LastFeast => _feastLast;
    /// <summary>사흘 안에 말소리에 깬 일 (때 · 깬 이 · 깨운 이 · 원인) · 켜 둔 조리대 (때 · 켜 둔 이 · 원인).</summary>
    public IReadOnlyList<(long t, int woken, int by, int cause)> WakeLog => _wakeLog;
    public IReadOnlyList<(long t, int who, int cause)> StoveLog => _stoveLog;
    /// <summary>그 규칙을 마지막으로 안건에 올린 때.</summary>
    public long AskedQuiet => _askQuiet;
    public long AskedStove => _askStove;

    /// <summary>모임을 마친다 — 추모: 슬픔이 반으로 · 곁에서 본 죽음의 충격이 덜어지고 · 함께 슬퍼한 이들끼리 가까워진다 / 축하: 기쁨 · 피로가 풀리고 · 가까워진다.</summary>
    private void GatheringDone(Sim sim, in Council m)
    {
        var mind = _mind!;
        int c;
        if (m.Kind == CouncilKind.Memorial)
        {
            Memorials++;
            c = Culture(sim, CultureEvent.Memorial, $"{_attend.Count}명이 {Ko.Eul(mind.NameOf(m.Subject))} 기렸다 — 함께 슬퍼했다", m.Cell, m.Cause, m.Caller, m.Subject);
            foreach (int a in _attend)
            {
                Grief[a] *= 0.5f;
                ref var ca = ref C(a);
                ca.Sad = Math.Max(0, ca.Sad - 0.1f);
                mind.Calmed(a, 0.05f);
                Remember(sim, a, Topic.Death, m.Subject, c, 0.4f, m.Cell);
            }
            foreach (int a in _attend) foreach (int b in _attend) if (a != b) Warm(sim, a, b, 0.03f, "함께 슬퍼했다");
        }
        else
        {
            Celebrations++;
            c = Culture(sim, CultureEvent.Celebration, $"{_attend.Count}명이 함께 축하했다 — 웃음소리가 늦게까지 이어졌다", m.Cell, m.Cause, m.Caller, m.Arg);
            foreach (int a in _attend)
            {
                ref var ca = ref C(a);
                ca.Joy = Math.Min(1, ca.Joy + 0.3f); ca.Weary = Math.Max(0, ca.Weary - 0.05f); ca.Stress = Math.Max(0, ca.Stress - 0.1f);
                mind.Eased(a, 0.03f, 0, 0);
                Remember(sim, a, Topic.Peace, m.Arg, c, 0.4f, m.Cell);
            }
            foreach (int a in _attend) foreach (int b in _attend) if (a != b) Warm(sim, a, b, 0.02f, "함께 웃었다");
        }
    }

    // ───────────────────────── 배의 규칙 ─────────────────────────

    public static string RuleText(int rule) => rule == 1 ? "밤 10시부터 아침 6시까지 자는 이 곁에서는 말하지 않는다" : "조리대를 떠날 땐 끄고 간다";

    private void RuleDecide(Sim sim, in Council m)
    {
        int yes = 0;
        for (int k = 0; k < _attend.Count; k++) if (_opinion[k] > 0) yes++;
        bool pass = yes * 2 > _attend.Count;
        if (pass)
        {
            RulesMade++;
            if (m.Arg == 1) QuietRule = true; else { StoveRule = true; _mind!.StoveRule = true; }
            Culture(sim, CultureEvent.Rule, $"배의 규칙이 되었다 — '{RuleText(m.Arg)}' ({_attend.Count}명 가운데 {yes}명 찬성)", m.Cell, m.Cause, m.Caller, m.Arg);
        }
        else
        {
            RulesRejected++;
            Culture(sim, CultureEvent.RuleRejected, $"규칙은 정하지 않기로 했다 — '{RuleText(m.Arg)}' ({_attend.Count}명 가운데 {yes}명만 찬성)", m.Cell, m.Cause, m.Caller, m.Arg);
        }
    }

    /// <summary>조용한 밤: 22~6시에 자는 이가 곁(같은 방 6칸 안)에 있으면 — 성실한 이일수록 말을 걸지 않는다 (덜 성실한 이는 어긴다).</summary>
    private bool KeepsQuiet(Sim sim, int i, int j)
    {
        if (!QuietRule) return false;
        double hour = Time.HourOfDay(sim.Tick);
        if (hour < 22 && hour >= 6) return false;
        bool sleeper = false;
        int room = _mind!.RoomOf(C(i).Cell);
        for (int k = 0; k < N && !sleeper; k++)
            if (k != i && k != j && Alive(k) && _life![C(k).Body].Asleep != 0 && _mind.RoomOf(C(k).Cell) == room && _mind.Dist(C(k).Cell, C(i).Cell) <= TalkReach) sleeper = true;
        if (!sleeper) return false;
        if (Rnd(sim, "밤의 규칙", i, j) >= 0.2f + 0.8f * Math.Min(C(i).Consc, C(j).Consc)) return false;   // 어긴다
        QuietKept++;
        LastTalk[i * N + j] = sim.Tick;   // 말을 삼켰다 — 한동안 다시 꺼내지 않는다
        return true;
    }

    /// <summary>시험용: 지난 며칠 그 시각에 함께 먹었다고 친다 (오늘은 빼고).</summary>
    public void SeedMealDays(long today, int hour, int days)
    {
        for (int d = 1; d <= days; d++) _mealSeen[(int)(((today - d) % MealWindowDays + MealWindowDays) % MealWindowDays) * 24 + hour] = 1;
    }
    /// <summary>시험용: 지금 그 시각의 관습 · 규칙을 정한다.</summary>
    public void SetCustomNow(int hour) { MealCustom = hour; _mind?.SetMealCustom(hour); }
    public void SetRuleNow(int rule) { if (rule == 1) QuietRule = true; else { StoveRule = true; if (_mind != null) _mind.StoveRule = true; } }

    private void SaveCulture(StateWriter w)
    {
        w.I32(MealCustom); w.Byte((byte)(QuietRule ? 1 : 0)); w.Byte((byte)(StoveRule ? 1 : 0));
        w.I64(Customs); w.I64(CustomsLost); w.I64(CustomMealsShared); w.I64(Taboos); w.I64(TaboosLifted); w.I64(BedTaboos); w.I64(Nicknames); w.I64(Memorials); w.I64(Celebrations);
        w.I64(RulesMade); w.I64(RulesRejected); w.I64(RuleBreaks); w.I64(QuietKept); w.I64(Gatherings); w.I64(GatheringsFailed); w.I64(MemorialsDeferred); w.Arr(_mournDeferred.OrderBy(x => x).ToArray());
        w.Arr(Nick); w.Arr(NickBy); w.Arr(_mealSeen); w.I64(_mealDay); w.Arr(_roomTaboo); w.Arr(_deeds); w.Arr(_deedCause); w.Arr(_feastDay);
        w.I32(_mourn.Count); foreach (var (d, t, c) in _mourn) { w.I32(d); w.I64(t); w.I32(c); }
        w.I64(_feastFrom); w.I64(_feastLast); w.I32(_feastCause); w.I32(_feastHero); w.I32(_feastWeight); w.Byte(_feastNews);
        w.I32(_wakeLog.Count); foreach (var (t, a, b, c) in _wakeLog) { w.I64(t); w.I32(a); w.I32(b); w.I32(c); }
        w.I32(_stoveLog.Count); foreach (var (t, a, c) in _stoveLog) { w.I64(t); w.I32(a); w.I32(c); }
        w.I64(_askQuiet); w.I64(_askStove); w.I64(_cultureCursor);
    }

    private void LoadCulture(StateReader r)
    {
        MealCustom = r.I32(); QuietRule = r.Byte() != 0; StoveRule = r.Byte() != 0;
        Customs = r.I64(); CustomsLost = r.I64(); CustomMealsShared = r.I64(); Taboos = r.I64(); TaboosLifted = r.I64(); BedTaboos = r.I64(); Nicknames = r.I64(); Memorials = r.I64(); Celebrations = r.I64();
        RulesMade = r.I64(); RulesRejected = r.I64(); RuleBreaks = r.I64(); QuietKept = r.I64(); Gatherings = r.I64(); GatheringsFailed = r.I64(); MemorialsDeferred = r.I64(); _mournDeferred.Clear(); foreach (int x in r.Arr<int>()) _mournDeferred.Add(x);
        Nick = r.Arr<byte>(); NickBy = r.Arr<int>(); _mealSeen = r.Arr<byte>(); _mealDay = r.I64(); _roomTaboo = r.Arr<byte>(); _deeds = r.Arr<int>(); _deedCause = r.Arr<int>(); _feastDay = r.Arr<long>();
        _mourn.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { int d = r.I32(); long t = r.I64(); int c = r.I32(); _mourn.Add((d, t, c)); }
        _feastFrom = r.I64(); _feastLast = r.I64(); _feastCause = r.I32(); _feastHero = r.I32(); _feastWeight = r.I32(); _feastNews = r.Byte();
        _wakeLog.Clear(); n = r.I32(); for (int i = 0; i < n; i++) { long t = r.I64(); int a = r.I32(); int b = r.I32(); int c = r.I32(); _wakeLog.Add((t, a, b, c)); }
        _stoveLog.Clear(); n = r.I32(); for (int i = 0; i < n; i++) { long t = r.I64(); int a = r.I32(); int c = r.I32(); _stoveLog.Add((t, a, c)); }
        _askQuiet = r.I64(); _askStove = r.I64(); _cultureCursor = r.I64();
    }
}
