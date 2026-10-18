using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>회의의 안건.</summary>
public enum CouncilKind : byte { None, Trial, Election, Ration }

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
    private const long NoHunger = long.MinValue;
    private long _lastCouncil = long.MinValue / 2, _hungrySince = NoHunger, _councilCursor, _lastCrisis = long.MinValue / 2;
    private long[] _triedAt = Array.Empty<long>(), _logShownAt = Array.Empty<long>();

    private List<int> People() { var l = new List<int>(); for (int i = 0; i < N; i++) if (Alive(i)) l.Add(i); return l; }
    private float MeanTrustIn(int who) { float s = 0; int n = 0; foreach (int j in People()) if (j != who) { s += TrustIn(j, who); n++; } return n > 0 ? s / n : 1; }

    /// <summary>1분마다: 위기 사실을 보고 · 열린 회의를 잇고 · 한 시간마다 안건을 찾는다.</summary>
    private void CouncilStep(Sim sim)
    {
        if (_triedAt.Length < N) { Array.Resize(ref _triedAt, N); Array.Resize(ref _logShownAt, N); for (int i = 0; i < N; i++) if (_triedAt[i] == 0) { _triedAt[i] = long.MinValue / 2; _logShownAt[i] = long.MinValue / 2; } }
        foreach (var f in sim.Facts.Since(_councilCursor))
        {
            if (f.Kind is FactKind.Ignition or FactKind.FireSpread or FactKind.WallBreach or FactKind.RegionVented or FactKind.PressureDrop or FactKind.Collapsed or FactKind.Died or FactKind.Panicked
                || f.Kind == FactKind.Shelter && f.Size > 0) _lastCrisis = f.Tick;
            if (f.Kind == FactKind.LogShown && f.Actor >= 0 && f.Actor < N) _logShownAt[f.Actor] = f.Tick;
        }
        _councilCursor = sim.Facts.Total;
        if (Meeting.Kind != CouncilKind.None) { MeetingStep(sim); return; }
        if (sim.Tick % Time.TicksPerHour != 0) return;
        HungerWatch(sim);
        if (sim.Tick - _lastCouncil < Time.Hours(20) || sim.Tick - _lastCrisis < Time.Minutes(30)) return;   // 회의는 하루 한 번까지 · 위기 뒤 30분은 미룬다
        double hour = Time.HourOfDay(sim.Tick);
        if (hour < 8 || hour > 20) return;
        if (People().Count < 3) return;
        if (!TryTrial(sim) && !TryRation(sim)) TryElection(sim);
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

    // ───────────────────────── 안건 ─────────────────────────

    private bool TryTrial(Sim sim)
    {
        var people = People();
        int best = -1, accuser = -1; float bs = 0;
        foreach (int x in people)
        {
            if (sim.Tick - _triedAt[x] < Time.Hours(24 * 5)) continue;   // 닷새 안에 다시 세우지 않는다
            float sum = 0, am = 0; int n = 0, acc = -1;
            foreach (int j in people)
            {
                if (j == x) continue;
                float g = GrudgeOf(j, x);
                sum += g; if (g >= GrudgeAt) n++;
                if (g > am) { am = g; acc = j; }
            }
            if (sum >= 1.0f && n >= 2 && sum > bs) { bs = sum; best = x; accuser = acc; }
        }
        if (best < 0) return false;
        int root = 0; long rt = long.MinValue;
        foreach (var m in Memories)
            if (m.Owner == accuser && m.About == best && m.Topic is Topic.Blame or Topic.Fight or Topic.Gossip or Topic.Hurt or Topic.Death && m.Tick > rt) { rt = m.Tick; root = m.Cause; }
        int caller = Rep >= 0 && Alive(Rep) && Rep != best ? Rep : accuser;
        _triedAt[best] = sim.Tick;
        return Open(sim, CouncilKind.Trial, best, caller, accuser,
            $"{Ko.I(_mind!.NameOf(caller))} 회의를 열었다 — {Ko.I(_mind.NameOf(accuser))} {Ko.Eul(_mind.NameOf(best))} 고발했다 (모두의 원망 {bs:0.0})", root);
    }

    private bool TryRation(Sim sim)
    {
        if (_mind!.RationFood <= 0 || _hungrySince == NoHunger || sim.Tick - _hungrySince < Time.Hours(48)) return false;
        foreach (int i in People()) if (_mind.Defying(i, sim.Tick)) return false;
        var people = People();
        int hungriest = people.OrderByDescending(i => C(i).Hunger).ThenBy(i => i).First();
        int caller = Rep >= 0 && Alive(Rep) ? Rep : hungriest;
        _hungrySince = NoHunger;
        return Open(sim, CouncilKind.Ration, -1, caller, -1, $"{Ko.I(_mind.NameOf(caller))} 회의를 열었다 — 배고픈 지 이틀째, 배급을 이대로 따를 수 없다", _mind.RationCause);
    }

    private bool TryElection(Sim sim, bool force = false)
    {
        bool need = force || (Rep < 0 ? sim.Tick > Time.Hours(48) : !Alive(Rep) || RepUntil <= sim.Tick || MeanTrustIn(Rep) < 0.35f);
        if (!need) return false;
        var people = People();
        int caller = people.OrderByDescending(i => i < Respect.Length ? Respect[i] : 0).ThenBy(i => i).First();
        string why = Rep < 0 ? "대표를 뽑자" : !Alive(Rep) ? $"{_mind!.NameOf(Rep)}의 자리를 이을 대표를 뽑자" : RepUntil <= sim.Tick ? "대표의 임기가 끝났다" : $"{Ko.Eul(_mind!.NameOf(Rep))} 더는 믿지 못한다 — 대표를 다시 뽑자";
        return Open(sim, CouncilKind.Election, -1, caller, -1, $"{Ko.I(_mind!.NameOf(caller))} 회의를 열었다 — {why}", 0);
    }

    private bool Open(Sim sim, CouncilKind kind, int subject, int caller, int accuser, string text, int parent)
    {
        int room = _mind!.MessRoom();
        if (room < 0) return false;
        int cell = RoomMiddle(room);
        int cause = sim.Causes.Add(CauseKind.Person, text, sim.Tick, C(caller).Cell, parent, 0, caller);
        var who = new byte[N];
        foreach (int a in People())
        {
            ref var c = ref C(a);
            float interest = kind switch
            {
                CouncilKind.Trial => GrudgeOf(a, subject) + Math.Max(0, BondOf(a, subject)) + 0.3f * Math.Max(0, BondOf(a, caller)),
                CouncilKind.Ration => c.Hunger,
                _ => 0.3f,
            };
            if (a == subject || a == caller || a == accuser || interest + 0.3f * c.Extra + 0.3f * c.Consc + 0.2f >= 0.5f) who[a] = 1;
        }
        _mind.Gather(room, cell, sim.Tick + Time.Minutes(CouncilGatherMin + 40), cause, who);
        Meeting = new Council { Kind = kind, Subject = subject, Caller = caller, Accuser = accuser, Room = room, Cell = cell, Called = sim.Tick, OpenAt = sim.Tick + Time.Minutes(CouncilGatherMin), Cause = cause };
        _attend.Clear(); _opinion.Clear(); _spoke = -1; _repSpoke = 0;
        Councils++; _lastCouncil = sim.Tick;
        if (kind == CouncilKind.Trial) Trials++;
        sim.Facts.Emit(sim.Tick, FactKind.Meeting, cell, (int)kind, cause, caller, subject);
        return true;
    }

    private int RoomMiddle(int room)
    {
        var cells = L.Rooms[room].Cells;
        double mx = cells.Average(c => L.X(c)), my = cells.Average(c => L.Y(c));
        int best = cells[0]; double bd = double.MaxValue;
        foreach (int c in cells) { if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0) continue; double d = Math.Abs(L.X(c) - mx) + Math.Abs(L.Y(c) - my); if (d < bd) { bd = d; best = c; } }
        return best;
    }

    // ───────────────────────── 회의 ─────────────────────────

    private void MeetingStep(Sim sim)
    {
        var m = Meeting;
        if (_lastCrisis > m.Called) { Close(sim, -2, "회의가 흩어졌다 — 배에 일이 났다"); return; }
        if (sim.Tick < m.OpenAt) return;
        if (m.Phase == 0)
        {
            var here = People().Where(a => Awake(a) && _mind!.RoomOf(C(a).Cell) == m.Room).ToList();
            if (here.Count(a => a != m.Subject) < 2 || here.Count < 3) { Close(sim, -1, $"회의가 열리지 못했다 — 모인 이가 {here.Count}명"); return; }
            // 차례: 연 이 → 대표 → 존경 순 · 피고는 마지막 (스스로 변호)
            var order = here.OrderByDescending(a => a == m.Caller ? 2 : a == Rep ? 1 : 0).ThenByDescending(a => a < Respect.Length ? Respect[a] : 0).ThenBy(a => a).ToList();
            if (m.Subject >= 0 && order.Remove(m.Subject)) order.Add(m.Subject);
            _attend.Clear(); _attend.AddRange(order);
            _opinion.Clear(); _spoke = 0;
            Meeting.Phase = 1;
            return;
        }
        if (_spoke < _attend.Count) { Speak(sim, _attend[_spoke]); _spoke++; return; }
        Decide(sim);
    }

    private float Opinion(Sim sim, int a)
    {
        var m = Meeting;
        ref var c = ref C(a);
        float op;
        switch (m.Kind)
        {
            case CouncilKind.Trial:
            {
                int x = m.Subject;
                if (a == x) return -1;
                float ev = 0;
                foreach (var mem in Memories)
                    if (mem.Owner == a && mem.About == x && mem.Topic is Topic.Blame or Topic.Fight or Topic.Gossip or Topic.Hurt or Topic.Death) ev = Math.Max(ev, 0.6f * Weight(mem, sim.Tick));
                if (x < LiarMark.Length && LiarMark[x] > 0) ev += 0.2f;                    // 거짓말이 들킨 적이 있다
                if (sim.Tick - _logShownAt[x] < Time.Hours(72)) ev += 0.3f;              // 주컴퓨터 정비 기록에 그가 나왔다
                op = 1.2f * GrudgeOf(a, x) + 0.5f * (c.Consc - 0.5f) - 0.4f * (c.Agree - 0.5f) - 0.6f * Math.Max(0, BondOf(a, x)) - (SameBand(a, x) ? 0.4f : 0) + ev - 0.15f;
                break;
            }
            case CouncilKind.Ration:
                op = c.Hunger + 0.3f * (1 - c.Consc) - 0.4f * Trust(a) - 0.3f;
                break;
            default:
                return 0;
        }
        // 동조 (Asch 1951): 대표가 먼저 말했으면 그를 믿는 만큼 그 의견 쪽으로
        if (_repSpoke != 0 && Rep >= 0 && a != Rep) op += 0.3f * TrustIn(a, Rep) * (_repOp - op);
        return Math.Clamp(op, -1, 1);
    }

    private void Speak(Sim sim, int a)
    {
        var m = Meeting;
        float op = Opinion(sim, a);
        _opinion.Add(op);
        if (a == Rep) { _repOp = op; _repSpoke = 1; }
        string name = _mind!.NameOf(a), x = m.Subject >= 0 ? _mind.NameOf(m.Subject) : "";
        string line = m.Kind switch
        {
            CouncilKind.Trial when a == m.Subject => C(a).Consc > 0.6f && GrudgeSum(m.Subject) > 1.5f ? "제 잘못입니다 — 벌을 받겠습니다" : "억울합니다 — 제 말도 들어 주세요",
            CouncilKind.Trial when op > 0.5f => $"{Ko.Eun(x)} 벌을 받아야 한다",
            CouncilKind.Trial when op > 0 => $"잘못은 했다 — 그래도 가볍게 하자",
            CouncilKind.Trial when op > -0.4f => "증거가 모자라다",
            CouncilKind.Trial => $"나는 {Ko.Eul(x)} 믿는다",
            CouncilKind.Ration when op > 0.4f => "이러다 다 굶어 죽는다 — 배급은 그만두자",
            CouncilKind.Ration when op > 0 => "조금만 더 먹게 하자",
            CouncilKind.Ration => "참아야 한다 — 식량이 바닥나면 끝이다",
            _ when a < Respect.Length && Candidates().Contains(a) => "제가 맡겠습니다",
            _ => "누가 맡든 따르겠다",
        };
        int said = sim.Causes.Add(CauseKind.Person, $"{name}: \"{line}\"", sim.Tick, C(a).Cell, m.Cause, 0, a);
        Sayings.Add(new Saying { Tick = sim.Tick, Speaker = a, Listener = m.Caller, Topic = m.Kind == CouncilKind.Ration ? Topic.Shortage : m.Kind == CouncilKind.Trial ? Topic.Blame : Topic.Work, End = op > 0 ? TalkEnd.Argue : TalkEnd.Defend, Line = said, Cell = C(a).Cell });
        if (Sayings.Count > 128) Sayings.RemoveAt(0);
    }

    private float GrudgeSum(int x) { float s = 0; foreach (int j in People()) if (j != x) s += GrudgeOf(j, x); return s; }

    private List<int> Candidates() => People().OrderByDescending(i => i < Respect.Length ? Respect[i] : 0).ThenBy(i => i).Take(3).ToList();

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
                int c = sim.Causes.Add(CauseKind.Person, $"회의가 {names.NameOf(x)}에게 {what} — {voters}명 중 {guilty}명이 잘못이라 했다", sim.Tick, m.Cell, m.Cause, 0, x);
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
                var cand = Candidates();
                var votes = new Dictionary<int, int>();
                var choice = new Dictionary<int, int>();
                foreach (int a in _attend)
                {
                    int pick = -1; float ps = float.MinValue;
                    foreach (int c in cand)
                    {
                        float s = c == a ? 0.6f + 0.3f * C(a).Extra : TrustIn(a, c) + BondOf(a, c) - GrudgeOf(a, c) + (SameBand(a, c) ? 0.2f : 0);
                        if (s > ps) { ps = s; pick = c; }
                    }
                    if (pick < 0) continue;
                    votes[pick] = votes.GetValueOrDefault(pick) + 1; choice[a] = pick;
                }
                int win = votes.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key < Respect.Length ? Respect[kv.Key] : 0).ThenBy(kv => kv.Key).First().Key;
                int old = Rep;
                Rep = win; RepUntil = sim.Tick + Time.Hours(24 * RepTermDays); Head = win; Elections++;
                int c2 = sim.Causes.Add(CauseKind.Person, $"{Ko.I(names.NameOf(win))} 대표로 뽑혔다 — {_attend.Count}명 중 {votes[win]}표", sim.Tick, m.Cell, m.Cause, 0, win);
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
                int c3 = sim.Causes.Add(CauseKind.Person, pass ? $"회의가 배급을 따르지 않기로 했다 — {_attend.Count}명 중 {yes}명 찬성" : $"회의가 배급을 지키기로 했다 — {_attend.Count}명 중 {yes}명만 찬성", sim.Tick, m.Cell, m.Cause, 0, m.Caller);
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
        }
        Close(sim, 0, null);
    }

    /// <summary>회의를 닫는다 (why: −1 열리지 못함 · −2 흩어짐 · 0 마침).</summary>
    private void Close(Sim sim, int why, string? text)
    {
        if (why < 0 && text != null)
        {
            int c = sim.Causes.Add(CauseKind.Person, text, sim.Tick, Meeting.Cell, Meeting.Cause, 0, Meeting.Caller);
            sim.Facts.Emit(sim.Tick, FactKind.Meeting, Meeting.Cell, -1, c, Meeting.Caller, Meeting.Subject);
            if (why == -1) CouncilsFailed++; else CouncilsBroken++;
            if (Meeting.Kind == CouncilKind.Trial && Meeting.Subject >= 0 && Meeting.Subject < _triedAt.Length) _triedAt[Meeting.Subject] = sim.Tick - Time.Hours(24 * 4);   // 열리지 못했으면 하루 뒤 다시
        }
        _mind!.Gather(-1, -1, 0, 0, null);
        Meeting = default;
        _attend.Clear(); _opinion.Clear(); _spoke = -1; _repSpoke = 0;
    }

    /// <summary>시험용: 지금 안건을 찾는다 (하루 한 번 · 시간대 · 위기 미룸 없이 · 선거는 이틀을 기다리지 않는다).</summary>
    public void CouncilNow(Sim sim) { _lastCouncil = long.MinValue / 2; if (Meeting.Kind == CouncilKind.None) { if (People().Count >= 3 && !TryTrial(sim) && !TryRation(sim)) TryElection(sim, force: true); } }
    /// <summary>시험용: 배고픔이 그때부터 이어졌다고 친다.</summary>
    public void HungrySince(long tick) => _hungrySince = tick;

    private void SaveCouncil(StateWriter w)
    {
        w.Byte((byte)Meeting.Kind); w.I32(Meeting.Subject); w.I32(Meeting.Caller); w.I32(Meeting.Accuser); w.I32(Meeting.Room); w.I32(Meeting.Cell);
        w.I64(Meeting.Called); w.I64(Meeting.OpenAt); w.I32(Meeting.Cause); w.Byte(Meeting.Phase);
        w.I32(Rep); w.I64(RepUntil);
        w.I64(Councils); w.I64(CouncilsFailed); w.I64(CouncilsBroken); w.I64(Trials); w.I64(Convictions); w.I64(Acquittals); w.I64(Elections); w.I64(Mutinies); w.I64(MutiniesFailed);
        w.Arr(_attend.ToArray()); w.Arr(_opinion.ToArray()); w.I32(_spoke); w.F32(_repOp); w.Byte(_repSpoke);
        w.I64(_lastCouncil); w.I64(_hungrySince); w.I64(_councilCursor); w.I64(_lastCrisis); w.Arr(_triedAt); w.Arr(_logShownAt);
    }
    private void LoadCouncil(StateReader r)
    {
        Meeting = new Council { Kind = (CouncilKind)r.Byte(), Subject = r.I32(), Caller = r.I32(), Accuser = r.I32(), Room = r.I32(), Cell = r.I32(), Called = r.I64(), OpenAt = r.I64(), Cause = r.I32(), Phase = r.Byte() };
        Rep = r.I32(); RepUntil = r.I64();
        Councils = r.I64(); CouncilsFailed = r.I64(); CouncilsBroken = r.I64(); Trials = r.I64(); Convictions = r.I64(); Acquittals = r.I64(); Elections = r.I64(); Mutinies = r.I64(); MutiniesFailed = r.I64();
        _attend.Clear(); _attend.AddRange(r.Arr<int>()); _opinion.Clear(); _opinion.AddRange(r.Arr<float>()); _spoke = r.I32(); _repOp = r.F32(); _repSpoke = r.Byte();
        _lastCouncil = r.I64(); _hungrySince = r.I64(); _councilCursor = r.I64(); _lastCrisis = r.I64(); _triedAt = r.Arr<long>(); _logShownAt = r.Arr<long>();
    }
}
