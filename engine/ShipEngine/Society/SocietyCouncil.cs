using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>회의의 안건.</summary>
public enum CouncilKind : byte { None, Trial, Election, Ration, Manual, Memorial, Celebration, Rule }

/// <summary>열린 회의 하나.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Council
{
    public CouncilKind Kind;
    public int Subject;    // 피고 (재판) · −1
    public int Caller;     // 연 이
    public int Accuser;    // 고발한 이 (재판) · −1
    public int Room, Cell;
    public long Called, OpenAt;
    public int Cause;
    public byte Phase;     // 0 모이는 중 · 1 말하는 중
    public int Arg;        // 배의 규칙 (규칙 회의 — 1 조용한 밤 · 2 조리대)
}

/// <summary>
/// ⑨ 회의 · 재판 · 선거 · 반란 (깊이 바퀴 3 · docs/ENGINE.md ⑨). 줄거리가 아니라 쌓인 값이 안건이 된다:
/// 한 사람에게 모인 원한 → 재판 · 대표가 없거나 무너졌다 → 선거 · 배급 중 이틀 넘는 굶주림 → 배급 반란.
/// 연 이가 식당으로 부르고(⑧ 모이기) · 15분 뒤 그 방에 있는 이로 연다 · 한 사람씩 말하고(1분에 한 마디 · 동조 — 대표가 먼저 말하면 그를 믿는 만큼 따라간다) · 표결한다 (과반).
/// 위기가 나면 흩어진다. 판결은 벌(사과 · 벌 당번 · 근신)과 앙금으로 · 당선은 배 전체 지도자(30일)로 · 반란은 찬성한 이들의 배급 거부(사흘)로 이어진다.
/// </summary>
public sealed partial class Society
{
    public const double CouncilGatherMin = 15, RepTermDays = 30, MutinyDays = 3;
    public Council Meeting;
    /// <summary>뽑힌 대표 (−1 없음) · 임기 끝.</summary>
    public int Rep = -1;
    public long RepUntil;
    public long Councils, CouncilsFailed, CouncilsBroken, Trials, Convictions, Acquittals, Elections, Mutinies, MutiniesFailed;
    private readonly List<int> _attend = new();
    private readonly List<float> _opinion = new();
    private int _spoke = -1;
    private float _repOp; private byte _repSpoke;
    public const long NoHunger = long.MinValue;
    private long _lastCouncil = long.MinValue / 2, _hungrySince = NoHunger, _councilCursor, _lastCrisis = long.MinValue / 2;
    private long[] _triedAt = Array.Empty<long>(), _logShownAt = Array.Empty<long>();

    private List<int> People() { var l = new List<int>(); for (int i = 0; i < N; i++) if (Alive(i)) l.Add(i); return l; }

    /// <summary>1분마다: 위기 사실을 보고 · 열린 모임을 잇고 · 한 시간마다 배고픔 · 지난 안건을 정리한다 (부르는 것은 사람 — MindGatherings).</summary>
    private void CouncilStep(Sim sim)
    {
        CouncilArrays();
        foreach (var f in sim.Facts.Since(_councilCursor))
        {
            if (f.Kind is FactKind.Ignition or FactKind.FireSpread or FactKind.WallBreach or FactKind.RegionVented or FactKind.PressureDrop or FactKind.Collapsed or FactKind.Died or FactKind.Panicked
                || f.Kind == FactKind.Shelter && f.Size > 0) _lastCrisis = f.Tick;
            if (f.Kind == FactKind.LogShown && f.Actor >= 0 && f.Actor < N) _logShownAt[f.Actor] = f.Tick;
            if (f.Kind is FactKind.MethodWorked or FactKind.MethodFailed) ManualFact(sim, f);   // 판단 엔진 (바): 통한 방법 · 틀어진 매뉴얼 방법이 안건이 된다
        }
        _councilCursor = sim.Facts.Total;
        if (sim.Tick % Time.TicksPerHour == 0)
        {
            HungerWatch(sim);
            // 지난 안건: 사흘 지난 숨진 이 · 이틀 지난 위기는 더 모일 일이 아니다
            for (int k = _mourn.Count - 1; k >= 0; k--) if (sim.Tick - _mourn[k].at > Time.Days(3)) { _mournDeferred.Remove(_mourn[k].dead); _mourn.RemoveAt(k); }
            if (_feastFrom >= 0 && sim.Tick - _feastFrom > Time.Hours(48)) _feastFrom = -1;
        }
        if (Meeting.Kind != CouncilKind.None) MeetingStep(sim);
    }

    private void HungerWatch(Sim sim)
    {
        if (_mind!.RationFood <= 0) { _hungrySince = NoHunger; return; }
        float s = 0; int n = 0;
        foreach (int i in People()) { s += C(i).Hunger; n++; }
        bool hungry = n > 0 && s / n > 0.55f;
        if (!hungry) _hungrySince = NoHunger;
        else if (_hungrySince == NoHunger) _hungrySince = sim.Tick;
    }

    // ───────────────────────── 판단 엔진의 행동 ─────────────────────────

    private long _actCursor;
    private long _spokeAt = long.MinValue / 2, _speakSince;
    private readonly List<int> _votes = new();

    /// <summary>
    /// 마음(판단 엔진)이 이번 틱에 한 사회적 행동을 읽어 결과를 계산한다 — 부름 → 모임이 열린다 (안건의 사실을 지운다) · 말 → 의견과 표가 적힌다 ·
    /// 미룸 → 모임이 흩어진다 · 추모를 미룸 → 한 번 남긴다. 사회는 행동을 정하지 않는다 (docs/ENGINE.md §3 9).
    /// </summary>
    public void TakeActs(Sim sim)
    {
        _mind ??= sim.Find<Mind>(); _life ??= sim.Find<Life>(); _net ??= sim.Find<Network>();
        if (_mind == null || _life == null) return;
        Resize(_mind.Agents.Count);
        CouncilArrays(); CultureArrays();
        _talk ??= TalkTable.Load();
        foreach (var act in _mind.ActsSince(_actCursor))
        {
            switch (act.Kind)
            {
                case SocialActKind.CallMeeting: Open(sim, act); break;
                case SocialActKind.Speak: Spoke(sim, act); break;
                case SocialActKind.CallOff when Meeting.Kind != CouncilKind.None && act.Meet == Meeting.Kind:
                    Close(sim, -2, null, act.Cause, act.Actor);
                    break;
                case SocialActKind.Talk: Talk(sim, act); break;
                case SocialActKind.KeepQuiet when act.Actor < N && act.Target < N:   // 밤의 규칙 — 말을 삼켰다: 한동안 다시 꺼내지 않는다
                    QuietKept++;
                    LastTalk[act.Actor * N + act.Target] = sim.Tick;
                    break;
                case SocialActKind.Nickname when act.Target >= 0 && act.Target < Nick.Length && Nick[act.Target] == 0:
                    Coin(sim, act.Target, act.Actor, (Nickname)act.Arg);
                    break;
                case SocialActKind.TakeSide: SideWith(sim, act); break;
                case SocialActKind.Mediate: Mediated(sim, act); break;
                case SocialActKind.Attack: Attacked(sim, act); break;
                case SocialActKind.Strike: _struck.Add(act.Actor); break;
                case SocialActKind.HitBack: _hitBack.Add(act.Actor); break;
                case SocialActKind.StopFight: _stopper.TryAdd(act.Target, act.Actor); break;
                case SocialActKind.BreakUp when Partner[act.Actor] == act.Target: BreakUp(sim, act.Actor, act.Target); break;
                case SocialActKind.Sabotage: Sabotaged(sim, act); break;
                case SocialActKind.Confess: Confessed(sim, act); break;
                case SocialActKind.KeepSecret: KeptSecret(sim, act); break;
                case SocialActKind.TellLie: ToldLie(sim, act); break;
                case SocialActKind.HearLie: HeardLie(sim, act); break;
                case SocialActKind.AskLog: AskedLog(sim, act); break;
                case SocialActKind.Conceal: Concealed(sim, act); break;
                case SocialActKind.DeferMemorial when _mournDeferred.Add(act.Target):
                    MemorialsDeferred++;
                    sim.Facts.Emit(sim.Tick, FactKind.Culture, act.Cell, (int)CultureEvent.MemorialDeferred, act.Cause, act.Actor, act.Target);
                    break;
            }
        }
        _actCursor = _mind.ActsTotal;
    }

    private void CouncilArrays()
    {
        if (_triedAt.Length >= N) return;
        int old = _triedAt.Length;
        Array.Resize(ref _triedAt, N); Array.Resize(ref _logShownAt, N);
        for (int i = old; i < N; i++) { _triedAt[i] = long.MinValue / 2; _logShownAt[i] = long.MinValue / 2; }
    }

    public static bool IsGathering(CouncilKind k) => k is CouncilKind.Memorial or CouncilKind.Celebration;
    public static string MeetName(CouncilKind k) => k == CouncilKind.Memorial ? "추모 모임" : k == CouncilKind.Celebration ? "축하 자리" : "회의";

    /// <summary>누가 불렀다 — 모임이 열린다 (이미 열린 모임이 있으면 부름이 묻힌다) · 그 안건의 사실을 지운다.</summary>
    private void Open(Sim sim, SocialAct act)
    {
        if (Meeting.Kind != CouncilKind.None || !Alive(act.Actor) || act.Room < 0) return;
        var kind = act.Meet;
        Meeting = new Council { Kind = kind, Subject = act.Target, Caller = act.Actor, Accuser = act.Other, Room = act.Room, Cell = act.Cell, Called = sim.Tick, OpenAt = sim.Tick + Time.Minutes(CouncilGatherMin), Cause = act.Cause, Arg = act.Arg };
        _attend.Clear(); _opinion.Clear(); _votes.Clear(); _spoke = -1; _repSpoke = 0;
        if (IsGathering(kind)) Gatherings++;
        else { Councils++; _lastCouncil = sim.Tick; }
        switch (kind)
        {
            case CouncilKind.Trial:
                Trials++;
                if (act.Target >= 0 && act.Target < _triedAt.Length) _triedAt[act.Target] = sim.Tick;
                break;
            case CouncilKind.Ration: _hungrySince = NoHunger; break;
            case CouncilKind.Manual:
            {
                int k = _manualAgenda.FindIndex(x => x.key == act.Arg);
                if (k >= 0) { _agendaNow = _manualAgenda[k]; _manualAgenda.RemoveAt(k); }
                break;
            }
            case CouncilKind.Rule:
                switch (act.Arg) { case 1: _askQuiet = sim.Tick; break; case 2: _askStove = sim.Tick; break; }
                break;
            case CouncilKind.Memorial:
                _mourn.RemoveAll(x => x.dead == act.Target); _mournDeferred.Remove(act.Target);
                break;
            case CouncilKind.Celebration:
                _feastFrom = -1; _feastLast = sim.Tick;
                break;
        }
        sim.Facts.Emit(sim.Tick, FactKind.Meeting, act.Cell, (int)kind, act.Cause, act.Actor, act.Target);
    }

    /// <summary>차례인 이가 말했다 — 의견 · 표를 적고 말을 남긴다 (차례가 아닌 이의 말은 모임의 것이 아니다).</summary>
    private void Spoke(Sim sim, SocialAct act)
    {
        if (Meeting.Kind == CouncilKind.None || Meeting.Phase != 1 || SpeakerNow != act.Actor) return;
        var m = Meeting;
        int a = act.Actor;
        float op = act.Value;
        _opinion.Add(op); _votes.Add(act.Arg);
        if (a == Rep) { _repOp = op; _repSpoke = 1; }
        Sayings.Add(new Saying { Tick = sim.Tick, Speaker = a, Listener = m.Caller, Topic = m.Kind switch { CouncilKind.Ration => Topic.Shortage, CouncilKind.Trial => Topic.Blame, CouncilKind.Memorial => Topic.Death, CouncilKind.Celebration => Topic.Peace, _ => Topic.Work },
            End = m.Kind == CouncilKind.Memorial ? TalkEnd.Comfort : m.Kind == CouncilKind.Celebration ? TalkEnd.Laugh : op > 0 ? TalkEnd.Argue : TalkEnd.Defend, Line = act.Cause, Cell = act.Cell });
        if (Sayings.Count > 128) Sayings.RemoveAt(0);
        _spoke++; _spokeAt = sim.Tick; _speakSince = sim.Tick;
    }

    // ───────────────────────── 판단 엔진이 읽는 것 ─────────────────────────

    /// <summary>지금 말할 차례인 이 (−1 없음).</summary>
    public int SpeakerNow => Meeting.Kind != CouncilKind.None && Meeting.Phase == 1 && _spoke >= 0 && _spoke < _attend.Count ? _attend[_spoke] : -1;
    /// <summary>이 모임에서 이미 말한 수 (앞사람과 다른 말을 고르는 데).</summary>
    public int SpokenCount => Math.Max(0, _spoke);
    /// <summary>대표가 이 모임에서 먼저 말한 의견 (없으면 null) — 동조.</summary>
    public float? RepSaid => _repSpoke != 0 ? _repOp : null;
    public long LastCouncil => _lastCouncil;
    /// <summary>마지막 위기(불 · 뚫림 · 무너짐 · 죽음 · 공황 · 대피)의 때.</summary>
    public long LastCrisis => _lastCrisis;
    /// <summary>배급 중 다들 배고프기 시작한 때 (NoHunger 아니다).</summary>
    public long HungerFrom => _hungrySince;
    public long TriedAt(int x) => x >= 0 && x < _triedAt.Length ? _triedAt[x] : long.MinValue / 2;
    public long LogShownAt(int x) => x >= 0 && x < _logShownAt.Length ? _logShownAt[x] : long.MinValue / 2;
    public float MemoryWeight(in Memory m, long now) => Weight(m, now);
    public float GrudgeSum(int x) { float s = 0; foreach (int j in People()) if (j != x) s += GrudgeOf(j, x); return s; }
    public float MeanTrustIn(int who) { float s = 0; int n = 0; foreach (int j in People()) if (j != who) { s += TrustIn(j, who); n++; } return n > 0 ? s / n : 1; }
    /// <summary>선거의 후보: 존경 셋.</summary>
    public List<int> Candidates() => People().OrderByDescending(i => i < Respect.Length ? Respect[i] : 0).ThenBy(i => i).Take(3).ToList();
    /// <summary>다음 매뉴얼 안건 (없으면 null) · 지금 모임의 매뉴얼 안건.</summary>
    public (int key, byte add, int proposer, float lost, int cause)? ManualAgendaHead => _manualAgenda.Count > 0 ? _manualAgenda[0] : null;
    public (int key, byte add, int proposer, float lost, int cause) AgendaNow => _agendaNow;

    // ───────────────────────── 회의 ─────────────────────────

    /// <summary>
    /// 모임의 차례 (모임의 관습 — 월드 엔진): 부른 뒤 15분에 그 방에 깬 채 있는 이로 연다 (모자라면 열리지 못한다) · 차례: 부른 이 → 대표 → 존경 순 · 피고는 마지막 ·
    /// 1분에 한 사람씩 — 차례인 이가 말하면 (마음의 행동) 다음 차례로 · 깨어 있지 않거나 3분 넘게 말이 없으면 그를 빼고 넘긴다 · 다 말하면 표를 센다.
    /// 일이 나서 미루는 것은 부른 이의 판단이다 (마음의 행동 '미루기' — TakeActs).
    /// </summary>
    private void MeetingStep(Sim sim)
    {
        var m = Meeting;
        if (sim.Tick < m.OpenAt) return;
        if (m.Phase == 0)
        {
            var here = People().Where(a => Awake(a) && _mind!.RoomOf(C(a).Cell) == m.Room).ToList();
            if (TooFew(m, here)) { Close(sim, -1, $"{Ko.I(MeetName(m.Kind))} 열리지 못했다 — 모인 이가 {here.Count}명"); return; }
            var order = here.OrderByDescending(a => a == m.Caller ? 2 : a == Rep ? 1 : 0).ThenByDescending(a => a < Respect.Length ? Respect[a] : 0).ThenBy(a => a).ToList();
            if (m.Subject >= 0 && order.Remove(m.Subject)) order.Add(m.Subject);
            _attend.Clear(); _attend.AddRange(order);
            _opinion.Clear(); _votes.Clear(); _spoke = 0; _speakSince = sim.Tick;
            Meeting.Phase = 1;
            return;
        }
        if (_spokeAt == sim.Tick) return;   // 방금 한 마디 — 다음 차례는 다음 분에
        if (_spoke < _attend.Count)
        {
            int a = _attend[_spoke];
            if (Awake(a) && sim.Tick - _speakSince < Time.Minutes(3)) return;   // 그의 말을 기다린다
            _attend.RemoveAt(_spoke); _speakSince = sim.Tick;   // 깨어 있지 않다 · 말이 없다 — 빼고 넘긴다
            if (TooFew(m, _attend)) Close(sim, -1, $"{Ko.I(MeetName(m.Kind))} 흐지부지됐다 — 남은 이가 {_attend.Count}명");
            return;
        }
        Decide(sim);
    }

    private int RoomMiddle(int room)
    {
        var cells = L.Rooms[room].Cells;
        double mx = cells.Average(c => L.X(c)), my = cells.Average(c => L.Y(c));
        int best = cells[0]; double bd = double.MaxValue;
        foreach (int c in cells) { if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0) continue; double d = Math.Abs(L.X(c) - mx) + Math.Abs(L.Y(c) - my); if (d < bd) { bd = d; best = c; } }
        return best;
    }

    private static bool TooFew(Council m, List<int> here) => IsGathering(m.Kind) ? here.Count < 2 : here.Count(a => a != m.Subject) < 2 || here.Count < 3;   // 추모 · 축하는 둘이라도

    private void Decide(Sim sim)
    {
        var m = Meeting;
        var names = _mind!;
        switch (m.Kind)
        {
            case CouncilKind.Trial:
            {
                int x = m.Subject, acc = m.Accuser;
                int voters = 0, guilty = 0;
                for (int k = 0; k < _attend.Count; k++) { if (_attend[k] == x) continue; voters++; if (_opinion[k] > 0) guilty++; }
                float g = GrudgeSum(x);
                bool conv = guilty * 2 > voters;
                int sentence = !conv ? 0 : g >= 2.5f ? 3 : g >= 1.5f ? 2 : 1;
                string what = sentence switch { 1 => "모두 앞에서 사과하게 했다", 2 => "사흘 벌 당번을 정했다", 3 => "하루 근신 · 사흘 벌 당번을 정했다", _ => "잘못을 묻지 않기로 했다" };
                int c = sim.Causes.Add(CauseKind.Society, $"회의가 {names.NameOf(x)}에게 {what} — {voters}명 중 {guilty}명이 잘못이라 했다", sim.Tick, m.Cell, m.Cause, 0, x);
                sim.Facts.Emit(sim.Tick, FactKind.Verdict, m.Cell, sentence, c, x, acc);
                foreach (int j in People())
                {
                    if (j == x) continue;
                    int k = _attend.IndexOf(j);
                    bool came = k >= 0;
                    if (conv)
                    {
                        Grudge[j * N + x] *= sentence == 3 ? 0.4f : sentence == 2 ? 0.5f : 0.6f;   // 정의가 섰다 — 원망이 벌만큼 풀린다
                        if (sentence == 1) Add(TrustOf, j, x, 0.05f);
                        // 절차적 공정 (Tyler 1990): 진 쪽 — 와서 말한 이는 덜 언짢고 · 안 온 이는 더
                        if (came && _opinion[k] <= 0 && acc >= 0) Add(Grudge, j, acc, 0.05f);
                        else if (!came && BondOf(j, x) > 0.3f && acc >= 0) Add(Grudge, j, acc, 0.15f);
                    }
                    else
                    {
                        if (came && _opinion[k] <= 0 && acc >= 0) Add(TrustOf, j, acc, -0.1f);
                        if (came && _opinion[k] > 0) Add(Grudge, j, x, 0.05f);
                        else if (!came && GrudgeOf(j, x) >= GrudgeAt) Add(Grudge, j, x, 0.15f);
                    }
                    if (came) Remember(sim, j, Topic.Blame, x, c, 0.5f, m.Cell);
                }
                ref var cx = ref C(x);
                if (conv)
                {
                    Convictions++;
                    if (acc >= 0) Add(Grudge, x, acc, 0.25f);
                    cx.Sad = Math.Min(1, cx.Sad + 0.3f); cx.Stress = Math.Min(1, cx.Stress + 0.2f);   // 부끄럽다
                    if (sentence >= 2) names.Penance(x, sim.Tick + Time.Hours(72));
                    if (sentence == 3) names.Confine(x, sim.Tick + Time.Hours(24));
                }
                else
                {
                    Acquittals++;
                    if (acc >= 0) { Add(Grudge, x, acc, 0.15f); ref var ca = ref C(acc); ca.Stress = Math.Min(1, ca.Stress + 0.1f); }
                    cx.Stress = Math.Max(0, cx.Stress - 0.2f);
                }
                break;
            }
            case CouncilKind.Election:
            {
                var votes = new Dictionary<int, int>();
                var choice = new Dictionary<int, int>();
                for (int k = 0; k < _attend.Count; k++)
                {
                    int a = _attend[k], pick = _votes[k];   // 저마다 말하며 고른 후보 (마음의 행동)
                    if (pick < 0 || !Alive(pick)) continue;
                    votes[pick] = votes.GetValueOrDefault(pick) + 1; choice[a] = pick;
                }
                if (votes.Count == 0) break;   // 아무도 고르지 않았다
                int win = votes.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key < Respect.Length ? Respect[kv.Key] : 0).ThenBy(kv => kv.Key).First().Key;
                int old = Rep;
                Rep = win; RepUntil = sim.Tick + Time.Hours(24 * RepTermDays); Head = win; Elections++;
                int c2 = sim.Causes.Add(CauseKind.Society, $"{Ko.I(names.NameOf(win))} 대표로 뽑혔다 — {_attend.Count}명 중 {votes[win]}표", sim.Tick, m.Cell, m.Cause, 0, win);
                sim.Facts.Emit(sim.Tick, FactKind.Elected, m.Cell, votes[win], c2, win, old);
                foreach (var (a, pick) in choice) if (pick != win && a != win) Add(Grudge, a, win, 0.03f);   // 떨어진 후보를 민 이들의 앙금 조금
                if (old >= 0 && old != win && Alive(old)) { ref var co = ref C(old); co.Sad = Math.Min(1, co.Sad + 0.2f); }
                break;
            }
            case CouncilKind.Ration:
            {
                int yes = 0;
                for (int k = 0; k < _attend.Count; k++) if (_opinion[k] > 0) yes++;
                bool pass = yes * 2 > _attend.Count;
                int c3 = sim.Causes.Add(CauseKind.Society, pass ? $"회의가 배급을 따르지 않기로 했다 — {_attend.Count}명 중 {yes}명 찬성" : $"회의가 배급을 지키기로 했다 — {_attend.Count}명 중 {yes}명만 찬성", sim.Tick, m.Cell, m.Cause, 0, m.Caller);
                sim.Facts.Emit(sim.Tick, FactKind.Mutiny, m.Cell, pass ? yes : -1, c3, m.Caller, -1);
                for (int k = 0; k < _attend.Count; k++)
                {
                    int a = _attend[k];
                    if (_opinion[k] <= 0) continue;
                    if (pass) { names.DefyRation(a, sim.Tick + Time.Hours(24 * MutinyDays)); TrustComputer[a] = Math.Max(0, TrustComputer[a] - 0.05f); }
                    else { ref var ca = ref C(a); ca.Stress = Math.Min(1, ca.Stress + 0.1f); }
                }
                if (pass) Mutinies++; else MutiniesFailed++;
                break;
            }
            case CouncilKind.Manual:
                ManualDecide(sim, m);
                break;
            case CouncilKind.Memorial or CouncilKind.Celebration:
                GatheringDone(sim, m);
                break;
            case CouncilKind.Rule:
                RuleDecide(sim, m);
                break;
        }
        Close(sim, 0, null);
    }

    /// <summary>회의를 닫는다 (why: −1 열리지 못함 · −2 흩어짐 · 0 마침) — 흩어짐은 미룬 이의 원인 마디(cause)로.</summary>
    private void Close(Sim sim, int why, string? text, int cause = 0, int actor = -1)
    {
        if (why < 0 && (text != null || cause != 0))
        {
            int c = cause != 0 ? cause : sim.Causes.Add(CauseKind.Society, text!, sim.Tick, Meeting.Cell, Meeting.Cause, 0, Meeting.Caller);
            sim.Facts.Emit(sim.Tick, FactKind.Meeting, Meeting.Cell, -1, c, actor >= 0 ? actor : Meeting.Caller, Meeting.Subject);
            if (IsGathering(Meeting.Kind)) GatheringsFailed++;
            else if (why == -1) CouncilsFailed++; else CouncilsBroken++;
            if (Meeting.Kind == CouncilKind.Trial && Meeting.Subject >= 0 && Meeting.Subject < _triedAt.Length) _triedAt[Meeting.Subject] = sim.Tick - Time.Hours(24 * 4);   // 열리지 못했으면 하루 뒤 다시
            if (Meeting.Kind == CouncilKind.Manual && _agendaNow.key != 0) { _manualAgenda.Insert(0, _agendaNow); _agendaNow = default; }   // 매뉴얼 안건은 다음 회의로
        }
        Meeting = default;
        _attend.Clear(); _opinion.Clear(); _votes.Clear(); _spoke = -1; _repSpoke = 0;
    }

    /// <summary>시험용: 배고픔이 그때부터 이어졌다고 친다.</summary>
    public void HungrySince(long tick) => _hungrySince = tick;

    private void SaveCouncil(StateWriter w)
    {
        w.Byte((byte)Meeting.Kind); w.I32(Meeting.Subject); w.I32(Meeting.Caller); w.I32(Meeting.Accuser); w.I32(Meeting.Room); w.I32(Meeting.Cell);
        w.I64(Meeting.Called); w.I64(Meeting.OpenAt); w.I32(Meeting.Cause); w.Byte(Meeting.Phase); w.I32(Meeting.Arg);
        w.I32(Rep); w.I64(RepUntil);
        w.I64(Councils); w.I64(CouncilsFailed); w.I64(CouncilsBroken); w.I64(Trials); w.I64(Convictions); w.I64(Acquittals); w.I64(Elections); w.I64(Mutinies); w.I64(MutiniesFailed);
        w.Arr(_attend.ToArray()); w.Arr(_opinion.ToArray()); w.Arr(_votes.ToArray()); w.I32(_spoke); w.I64(_spokeAt); w.I64(_speakSince); w.I64(_actCursor); w.F32(_repOp); w.Byte(_repSpoke);
        w.I64(_lastCouncil); w.I64(_hungrySince); w.I64(_councilCursor); w.I64(_lastCrisis); w.Arr(_triedAt); w.Arr(_logShownAt);
        SaveManual(w);
        SaveCulture(w);
    }
    private void LoadCouncil(StateReader r)
    {
        Meeting = new Council { Kind = (CouncilKind)r.Byte(), Subject = r.I32(), Caller = r.I32(), Accuser = r.I32(), Room = r.I32(), Cell = r.I32(), Called = r.I64(), OpenAt = r.I64(), Cause = r.I32(), Phase = r.Byte(), Arg = r.I32() };
        Rep = r.I32(); RepUntil = r.I64();
        Councils = r.I64(); CouncilsFailed = r.I64(); CouncilsBroken = r.I64(); Trials = r.I64(); Convictions = r.I64(); Acquittals = r.I64(); Elections = r.I64(); Mutinies = r.I64(); MutiniesFailed = r.I64();
        _attend.Clear(); _attend.AddRange(r.Arr<int>()); _opinion.Clear(); _opinion.AddRange(r.Arr<float>()); _votes.Clear(); _votes.AddRange(r.Arr<int>()); _spoke = r.I32(); _spokeAt = r.I64(); _speakSince = r.I64(); _actCursor = r.I64(); _repOp = r.F32(); _repSpoke = r.Byte();
        _lastCouncil = r.I64(); _hungrySince = r.I64(); _councilCursor = r.I64(); _lastCrisis = r.I64(); _triedAt = r.Arr<long>(); _logShownAt = r.Arr<long>();
        LoadManual(r);
        LoadCulture(r);
    }
}
