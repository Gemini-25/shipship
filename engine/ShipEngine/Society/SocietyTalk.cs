namespace ShipEngine;

/// <summary>⑨ 대화의 입말 표 (engine/data/talk.json) — "주제/결과" 마다 몇 줄.</summary>
public sealed class TalkTable
{
    public Dictionary<string, string[]> Lines = new();
    private static TalkTable? _cache;
    public static TalkTable Load() => _cache ??= DataFiles.Load<TalkTable>("talk.json");

    public static string TopicName(Topic t) => t switch
    {
        Topic.Small => "잡담", Topic.Work => "일", Topic.Fire => "불", Topic.Breach => "구멍", Topic.Rescue => "구조", Topic.Hurt => "다침",
        Topic.Death => "죽음", Topic.Blame => "탓", Topic.Gossip => "험담", Topic.Shortage => "걱정", Topic.Fight => "싸움", _ => "잡담",
    };

    public static string EndName(TalkEnd e) => e switch
    {
        TalkEnd.Agree => "맞장구", TalkEnd.Laugh => "웃음", TalkEnd.Awkward => "어색", TalkEnd.Friction => "다툼", TalkEnd.Praise => "칭찬",
        TalkEnd.Comfort => "위로", TalkEnd.Apology => "사과", TalkEnd.Argue => "다툼", TalkEnd.Spread => "퍼짐", TalkEnd.Defend => "감쌈", TalkEnd.Worry => "걱정", _ => "맞장구",
    };

    /// <summary>주제 · 결과의 이야기 한 줄 (기록 · 연출) — "둘이 불 이야기를 했다 → 서로 탓함".</summary>
    public static string Summary(Topic t, TalkEnd e) => e switch
    {
        TalkEnd.Agree => $"{TopicName(t)} 이야기를 나눴다", TalkEnd.Laugh => "실컷 웃었다", TalkEnd.Awkward => $"{TopicName(t)} 이야기가 어색하게 끊겼다",
        TalkEnd.Friction => "말끝마다 부딪쳤다 · 사이가 조금 틀어졌다", TalkEnd.Praise => $"{TopicName(t)} 이야기를 하다 고맙다고 했다 · 가까워졌다",
        TalkEnd.Comfort => "슬픔을 나누며 위로했다 · 가까워졌다", TalkEnd.Apology => "그날 일을 사과했다 · 앙금이 풀렸다",
        TalkEnd.Argue => "그날 일을 두고 서로 탓했다 · 사이가 나빠졌다", TalkEnd.Spread => "누군가의 험담을 했다 · 소문이 퍼졌다",
        TalkEnd.Defend => "험담을 막아섰다", TalkEnd.Worry => "식량 · 물 걱정을 했다", _ => "이야기를 했다",
    };
}

public sealed partial class Society
{
    // ───────────────────────── 대화 ─────────────────────────

    private readonly struct Option
    {
        public readonly Topic T; public readonly float W; public readonly int About, Cause, Cell, Speaker;
        public Option(Topic t, float w, int about, int cause, int cell, int speaker) { T = t; W = w; About = about; Cause = cause; Cell = cell; Speaker = speaker; }
    }

    /// <summary>두 사람이 말을 나눈다: 기억 · 관계 · 성격에서 주제를 고르고 → 결과 → 관계가 바뀐다 · 한 마디가 남는다.</summary>
    private void Talk(Sim sim, int i, int j)
    {
        var opts = new List<Option>();
        foreach (var (a, b) in new[] { (i, j), (j, i) })
        {
            // 원망하는 사람에게 따진다
            if (GrudgeOf(a, b) > 0.25f) opts.Add(new(Topic.Blame, 3 * GrudgeOf(a, b) * (0.6f + C(a).Neuro * 0.6f), b, MemoryCause(a, Topic.Blame, b), MemoryCell(a, Topic.Blame, b), a));
            // 슬픔을 꺼낸다
            if (Grief[a] > 0.25f) { int dead = StrongestAbout(sim, a, Topic.Death); if (dead >= 0) opts.Add(new(Topic.Death, 2 * Grief[a], dead, MemoryCause(a, Topic.Death, dead), MemoryCell(a, Topic.Death, dead), a)); }
            // 그 자리에 없는 이의 험담 (덜 다정할수록)
            int c3 = -1; float g3 = 0.3f;
            for (int x = 0; x < N; x++) if (x != a && x != b && GrudgeOf(a, x) > g3 && _mind!.RoomOf(C(x).Cell) != _mind.RoomOf(C(a).Cell)) { g3 = GrudgeOf(a, x); c3 = x; }
            if (c3 >= 0) opts.Add(new(Topic.Gossip, 1.5f * g3 * (1.2f - C(a).Agree), c3, MemoryCause(a, Topic.Blame, c3), MemoryCell(a, Topic.Blame, c3), a));
        }
        // 함께 겪은 일 (같은 원인을 둘 다 기억한다 — 가장 큰 것)
        int best = -1; float bw = 0.15f;
        for (int k = 0; k < Memories.Count; k++)
        {
            var m = Memories[k];
            if (m.Owner != i || m.Topic is Topic.Blame or Topic.Gossip or Topic.Small) continue;
            float w = Weight(m, sim.Tick);
            if (w <= bw) continue;
            for (int q = 0; q < Memories.Count; q++) if (Memories[q].Owner == j && Memories[q].Cause == m.Cause && Memories[q].Topic == m.Topic) { best = k; bw = w; break; }
        }
        if (best >= 0) { var m = Memories[best]; opts.Add(new(m.Topic, 2.2f * bw, m.About, m.Cause, m.Cell, C(i).Extra >= C(j).Extra ? i : j)); }
        // 식량 · 물이 열흘을 못 버틴다
        double food = sim.Book.DaysLeft((int)Res.Food, sim.Tick), water = sim.Book.DaysLeft((int)Res.Water, sim.Tick);
        if (food < 10 || water < 10) opts.Add(new(Topic.Shortage, 1.2f, -1, 0, C(i).Cell, C(i).Neuro >= C(j).Neuro ? i : j));
        int talker = C(i).Extra >= C(j).Extra ? i : j;
        opts.Add(new(Topic.Small, 1, -1, 0, C(i).Cell, talker));
        if (C(i).Goal == GoalKind.Idle || C(j).Goal == GoalKind.Idle) opts.Add(new(Topic.Work, 0.5f, -1, 0, C(i).Cell, talker));

        float sum = opts.Sum(o => o.W), r = Rnd(sim, "주제", i, j) * sum;
        var pick = opts[^1];
        foreach (var o in opts) { r -= o.W; if (r <= 0) { pick = o; break; } }
        int sp = pick.Speaker, li = sp == i ? j : i;
        Outcome(sim, pick, sp, li);
        LastTalk[i * N + j] = LastTalk[j * N + i] = sim.Tick;
        // 한 번 꺼낸 이야기는 둘에게 조금 가라앉는다 (같은 말만 되풀이하지 않게)
        if (pick.Cause != 0)
            for (int k = 0; k < Memories.Count; k++)
            {
                var m = Memories[k];
                if ((m.Owner == i || m.Owner == j) && m.Cause == pick.Cause) { m.Weight *= 0.5f; Memories[k] = m; }
            }
    }

    private int MemoryCause(int owner, Topic t, int about)
    {
        foreach (var m in Memories) if (m.Owner == owner && m.Topic == t && m.About == about) return m.Cause;
        return 0;
    }
    private int MemoryCell(int owner, Topic t, int about)
    {
        foreach (var m in Memories) if (m.Owner == owner && m.Topic == t && m.About == about) return m.Cell;
        return C(owner).Cell;
    }
    private int StrongestAbout(Sim sim, int owner, Topic t)
    {
        int about = -1; float bw = 0;
        foreach (var m in Memories) if (m.Owner == owner && m.Topic == t && Weight(m, sim.Tick) > bw) { bw = Weight(m, sim.Tick); about = m.About; }
        return about;
    }

    /// <summary>주제에 따라 대화가 흘러간 끝 — 관계를 바꾸고 한 마디를 남긴다.</summary>
    private void Outcome(Sim sim, Option o, int a, int b)
    {
        ref var ca = ref C(a); ref var cb = ref C(b);
        float r = Rnd(sim, "대화 끝", a, b);
        TalkEnd end;
        switch (o.T)
        {
            case Topic.Blame:
            {
                // b가 사과하나: 다정하고 · 덜 불안하고 · a를 아끼고 · 실제로 b의 탓일수록
                bool guilty = o.Cause != 0 && Culprit(sim, o.Cause, -1) == b;
                float p = cb.Agree * (1 - cb.Neuro * 0.5f) * (0.5f + Math.Max(0, BondOf(b, a)) + DebtOf(b, a)) * (guilty ? 1.2f : 0.5f);
                if (r < p)
                {
                    end = TalkEnd.Apology; Apologies++;
                    int k = a * N + b; Grudge[k] *= 0.5f;
                    Warm(sim, a, b, 0.05f); Warm(sim, b, a, 0.03f); Add(TrustOf, a, b, 0.05f);
                    Say(sim, b, a, o, end, Pick(sim, "탓/사과", b));
                }
                else
                {
                    end = TalkEnd.Argue; Arguments++;
                    Add(Grudge, a, b, 0.15f); Add(Grudge, b, a, 0.1f);
                    Warm(sim, a, b, -0.1f); Warm(sim, b, a, -0.1f);
                    ca.Anger = Math.Min(1, ca.Anger + 0.3f * (0.5f + ca.Neuro)); cb.Anger = Math.Min(1, cb.Anger + 0.25f * (0.5f + cb.Neuro));
                    ca.Stress = Math.Min(1, ca.Stress + 0.05f); cb.Stress = Math.Min(1, cb.Stress + 0.05f);
                    int line = Say(sim, a, b, o, end, Pick(sim, "탓/다툼", a));
                    Remember(sim, a, Topic.Fight, b, line, 0.5f, ca.Cell); Remember(sim, b, Topic.Fight, a, line, 0.5f, cb.Cell);
                    Escalate(sim, a, b, 1, line);
                    Escalate(sim, b, a, 0.7f, line);
                }
                break;
            }
            case Topic.Gossip:
            {
                int x = o.About;
                if (BondOf(b, x) > 0.5f && cb.Agree > 0.4f)
                {
                    end = TalkEnd.Defend;
                    Warm(sim, b, a, -0.05f);
                    Say(sim, b, a, o, end, Pick(sim, "험담/감쌈", b));
                }
                else
                {
                    // 소문: 듣는 이가 말하는 이를 믿는 만큼 · 덜 다정할수록 같이 미워한다
                    end = TalkEnd.Spread; Rumors++;
                    float w = GrudgeOf(a, x) * TrustIn(b, a) * (1 - cb.Agree * 0.5f);
                    Add(Grudge, b, x, w * 0.4f); Add(TrustOf, b, x, -w * 0.3f);
                    Warm(sim, a, b, 0.02f);
                    Remember(sim, b, Topic.Blame, x, o.Cause, w * 0.6f, o.Cell);
                    Say(sim, a, b, o, end, Pick(sim, "험담/퍼짐", a));
                }
                break;
            }
            case Topic.Death:
            {
                if (r < 0.4f + cb.Agree * 0.6f)
                {
                    end = TalkEnd.Comfort;
                    Grief[a] = Math.Max(0, Grief[a] - 0.08f); ca.Sad = Math.Max(0, ca.Sad - 0.1f);
                    Warm(sim, a, b, 0.06f); Warm(sim, b, a, 0.04f);
                    Say(sim, a, b, o, end, Pick(sim, "죽음/위로", a));
                }
                else { end = TalkEnd.Awkward; Say(sim, b, a, o, end, Pick(sim, "죽음/어색", b)); }
                break;
            }
            case Topic.Fire or Topic.Breach or Topic.Rescue or Topic.Hurt or Topic.Fight:
            {
                // 함께 겪은 일: 그때 자기를 구한 · 위기를 넘긴 상대에게 고맙다 · 아니면 맞장구
                bool hero = o.About == b && o.T is Topic.Fire or Topic.Breach or Topic.Rescue;
                if (o.T == Topic.Hurt) { end = TalkEnd.Comfort; Warm(sim, a, b, 0.03f); Say(sim, b, a, o, end, Pick(sim, "다침/위로", b)); break; }
                end = hero ? TalkEnd.Praise : TalkEnd.Agree;
                Warm(sim, a, b, hero ? 0.06f : 0.04f); Warm(sim, b, a, 0.04f);
                if (hero) Add(TrustOf, a, b, 0.05f);
                string key = $"{TalkTable.TopicName(o.T)}/{(hero ? "칭찬" : "맞장구")}";
                Say(sim, a, b, o, end, Pick(sim, _talk!.Lines.ContainsKey(key) ? key : $"{TalkTable.TopicName(o.T)}/맞장구", a));
                break;
            }
            case Topic.Shortage:
                end = TalkEnd.Worry;
                ca.Stress = Math.Min(1, ca.Stress + 0.05f); cb.Stress = Math.Min(1, cb.Stress + 0.05f);
                Warm(sim, a, b, 0.01f);
                Say(sim, a, b, o, end, Pick(sim, "걱정/걱정", a));
                break;
            default:
            {
                // 잡담 · 일: 성격이 맞으면 웃고 · 거친 둘은 부딪친다
                float fit = 1 - Math.Abs(ca.Extra - cb.Extra) * 0.5f + (ca.Agree + cb.Agree) * 0.25f + Math.Max(0, BondOf(a, b)) * 0.5f;
                string t = o.T == Topic.Work ? "일" : "잡담";
                if (fit > r * 1.4f)
                {
                    end = r < 0.25f && o.T == Topic.Small ? TalkEnd.Laugh : TalkEnd.Agree;
                    Warm(sim, a, b, 0.03f); Warm(sim, b, a, 0.03f);
                    ca.Joy = Math.Min(1, ca.Joy + 0.05f); cb.Joy = Math.Min(1, cb.Joy + 0.05f);
                    Say(sim, a, b, o, end, Pick(sim, end == TalkEnd.Laugh ? "잡담/웃음" : $"{t}/맞장구", a));
                }
                else if (ca.Agree + cb.Agree < 0.7f)
                {
                    end = TalkEnd.Friction;
                    Warm(sim, a, b, -0.02f); Warm(sim, b, a, -0.02f); Add(Grudge, b, a, 0.02f);
                    ca.Anger = Math.Min(1, ca.Anger + 0.05f); cb.Anger = Math.Min(1, cb.Anger + 0.08f);
                    Say(sim, b, a, o, end, Pick(sim, $"{t}/다툼", b));
                }
                else { end = TalkEnd.Awkward; Say(sim, b, a, o, end, Pick(sim, "잡담/어색", b)); }
                break;
            }
        }
        Talks++;
    }

    /// <summary>입말 표에서 한 줄 (말하는 사람 · 때에 따라).</summary>
    private string Pick(Sim sim, string key, int speaker)
    {
        if (_talk == null || !_talk.Lines.TryGetValue(key, out var lines) || lines.Length == 0) return "…";
        return lines[(int)(Rnd(sim, "한 마디", speaker, key.Length) * lines.Length) % lines.Length];
    }

    /// <summary>한 마디를 남긴다: 자리를 채워 원인 마디의 글로 · 대화 사실 (연출 · 화면) · 말소리 (③ 소리 — 들은 이의 귀에).</summary>
    private int Say(Sim sim, int speaker, int listener, Option o, TalkEnd end, string line)
    {
        var mind = _mind!;
        string Name(int x) => x >= 0 && x < N ? mind.NameOf(x) : "그 사람";
        string room = RoomName(o.Cell);
        string other = Name(listener), about = Name(o.About);
        string text = line.Replace("{상대아}", Ko.A(Given(other))).Replace("{상대}", other)
            .Replace("{그이}", Ko.I(about)).Replace("{그을}", Ko.Eul(about)).Replace("{그은}", Ko.Eun(about)).Replace("{그와}", Ko.Gwa(about)).Replace("{그}", about)
            .Replace("{곳에서}", room + "에서").Replace("{곳을}", Ko.Eul(room)).Replace("{곳}", room);
        int summary = sim.Causes.Add(CauseKind.Person, $"{Ko.Gwa(mind.NameOf(speaker))} {Ko.I(other)} {TalkTable.Summary(o.T, end)}", sim.Tick, C(speaker).Cell, o.Cause, 0, speaker);
        int said = sim.Causes.Add(CauseKind.Person, text, sim.Tick, C(speaker).Cell, summary, 0, speaker);
        Sayings.Add(new Saying { Tick = sim.Tick, Speaker = speaker, Listener = listener, Topic = o.T, End = end, Line = said, Cell = C(speaker).Cell });
        if (Sayings.Count > 128) Sayings.RemoveAt(0);
        sim.Facts.Emit(sim.Tick, FactKind.Talk, C(speaker).Cell, (float)end, summary, speaker, listener);
        sim.Find<Sound>()?.Emit(sim, C(speaker).Cell, end is TalkEnd.Argue or TalkEnd.Friction ? 72 : 60, SoundKind.Voice, said);
        return said;
    }

    /// <summary>부를 때 쓰는 이름 (성을 뗀다 — 두 글자 성도).</summary>
    public static string Given(string full)
    {
        foreach (var two in new[] { "남궁", "황보", "제갈", "선우", "독고", "사공" }) if (full.StartsWith(two) && full.Length > 2) return full[2..];
        return full.Length >= 3 ? full[1..] : full;
    }

    /// <summary>그 칸의 방 이름 (문 칸이면 곁의 방).</summary>
    private string RoomName(int cell)
    {
        if (cell < 0 || cell >= L.N) return "거기";
        if (L.Room[cell] >= 0) return L.Rooms[L.Room[cell]].Name;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(cell, nb);
        foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) return L.Rooms[L.Room[j]].Name;
        return "거기";
    }

    // ───────────────────────── 갈등 사다리 ─────────────────────────

    /// <summary>x가 y에게 덤비나: 원한 · 거친 성격 · 불안 · 긴장 · 화가 모두 겹쳐야 (드물게). 끝장을 보려는 것은 더 드물다.</summary>
    private void Escalate(Sim sim, int x, int y, float scale, int cause)
    {
        if (!Awake(x) || !Awake(y) || InBrawl(x, out _, out _) || InBrawl(y, out _, out _)) return;
        if (_mind!.RoomOf(C(x).Cell) != _mind.RoomOf(C(y).Cell) || _mind.Dist(C(x).Cell, C(y).Cell) > 4) return;
        ref var cx = ref C(x);
        float g = GrudgeOf(x, y);
        if (g < 0.5f) return;
        float pressure = (g - 0.5f) * 2 * (1 - cx.Agree) * (1 - cx.Agree) * (0.3f + cx.Neuro) * (0.4f + cx.Stress) * cx.Anger * 1.5f * scale;
        if (Rnd(sim, "덤빈다", x, y) >= pressure) return;
        bool lethal = g > 0.9f && cx.Agree < 0.25f && cx.Neuro > 0.6f && (cx.Stress > 0.6f || DeathBlame(x, y));
        var mind = _mind;
        int c = sim.Causes.Add(CauseKind.Person, lethal ? $"{Ko.I(mind.NameOf(x))} {Ko.Eul(mind.NameOf(y))} 죽일 듯이 덤볐다" : $"{Ko.I(mind.NameOf(x))} {mind.NameOf(y)}에게 주먹을 휘둘렀다",
            sim.Tick, cx.Cell, cause != 0 ? cause : MemoryCause(x, Topic.Blame, y), 0, x);
        Brawls.Add(new Brawl { A = x, B = y, Start = sim.Tick, Until = sim.Tick + Time.Minutes(lethal ? 12 : 4), Lethal = (byte)(lethal ? 1 : 0), Cause = c });
        Fights++;
        sim.Facts.Emit(sim.Tick, FactKind.Fight, cx.Cell, lethal ? 2 : 1, c, x, y);
        var o = new Option(Topic.Fight, 1, y, c, cx.Cell, x);
        Say(sim, x, y, o, TalkEnd.Argue, Pick(sim, lethal ? "싸움/끝장" : "싸움/시작", x));
        C(y).Fear = Math.Max(C(y).Fear, 0.6f + 0.3f * (1 - C(y).Brave));
    }

    /// <summary>x가 원망하는 y의 탓으로 x의 가까운 이가 죽었나.</summary>
    private bool DeathBlame(int x, int y)
    {
        foreach (var m in Memories) if (m.Owner == x && m.Topic == Topic.Death && m.About >= 0 && BondOf(x, m.About) > 0.4f && MemoryCause(x, Topic.Blame, y) == m.Cause) return true;
        return false;
    }

    /// <summary>1분마다: 벌어진 몸싸움 — 떨어지면 끝 · 로봇이 먼저 말리고 · 다정하고 용감한 사람이 말린다 · 아니면 주먹이 오간다 (⑦ 몸에 타박 · 출혈).</summary>
    private void BrawlStep(Sim sim)
    {
        var mind = _mind!;
        for (int k = Brawls.Count - 1; k >= 0; k--)
        {
            var br = Brawls[k];
            int a = br.A, b = br.B;
            ref var vb = ref _life![C(b).Body];
            bool apart = !Awake(a) || vb.Alive == 0 || mind.RoomOf(C(a).Cell) != mind.RoomOf(C(b).Cell) || mind.Dist(C(a).Cell, C(b).Cell) > 3;
            // 쓰러지면 주먹다짐은 끝난다 · 끝장을 보려는 이는 쓰러진 뒤에도 두 번 더 (끔찍하게도)
            bool down = vb.Conscious == 0;
            if (down && br.Lethal != 0 && br.Until > sim.Tick + Time.Minutes(2)) { br.Until = sim.Tick + Time.Minutes(2); Brawls[k] = br; }
            bool done = apart || sim.Tick >= br.Until || down && br.Lethal == 0;
            // 말리는 이: 같은 방의 로봇 (먼저) · 다정하고 용감한 사람
            int stopper = -1;
            if (!done)
            {
                int room = mind.RoomOf(C(a).Cell);
                foreach (var ag in mind.Agents)
                    if (!ag.IsPerson && ag.C.Up != 0 && ag.C.Kind == AgentKind.Robot && mind.RoomOf(ag.C.Cell) == room) { stopper = ag.Id; break; }
                if (stopper < 0)
                    for (int i = 0; i < N; i++)
                        if (i != a && i != b && Awake(i) && mind.RoomOf(C(i).Cell) == room && C(i).Agree > 0.45f && C(i).Brave > (br.Lethal != 0 ? 0.5f : 0.3f) && GrudgeOf(i, b) < 0.5f) { stopper = i; break; }
            }
            if (stopper >= 0)
            {
                var o = new Option(Topic.Fight, 1, a, br.Cause, C(a).Cell, stopper);
                Say(sim, stopper, a, o, TalkEnd.Argue, Pick(sim, mind.Agents[stopper].IsPerson ? "싸움/말림" : "싸움/로봇", stopper));
                if (mind.Agents[stopper].IsPerson) { Warm(sim, b, stopper, 0.1f); Add(Debt, b, stopper, 0.2f); }
                done = true;
            }
            if (!done)
            {
                // 한 대: 끝장을 보려는 이는 손에 잡히는 것을 휘두른다
                float r1 = Rnd(sim, "주먹", a, br.Blows), r2 = Rnd(sim, "피", a, br.Blows);
                float blunt = br.Lethal != 0 ? 0.25f + 0.3f * r1 : 0.06f + 0.1f * r1, bleed = br.Lethal != 0 ? 1.0f + 2.0f * r2 : 0.15f * r2;
                _life!.Strike(sim, C(b).Body, blunt, bleed, br.Cause);
                // 맞는 이가 거칠면 맞받아친다
                if (C(b).Agree < 0.4f && C(b).Anger > 0.4f && C(b).Brave > 0.4f) _life.Strike(sim, C(a).Body, blunt * 0.5f, bleed * 0.3f, br.Cause);
                br.Blows++; Brawls[k] = br;
                if (_life[C(b).Body].Alive == 0) { Killings++; done = true; }
            }
            if (done)
            {
                Brawls.RemoveAt(k);
                // 끝난 뒤: 맞은 이는 때린 이를 두려워하고 미워한다 · 본 사람들은 때린 이를 덜 믿는다
                Add(Grudge, b, a, 0.3f); Add(TrustOf, b, a, -0.5f); Warm(sim, b, a, -0.3f);
                C(a).Anger *= 0.4f;
                foreach (int w in Near(C(a).Cell, a))
                {
                    if (w == b) continue;
                    Add(TrustOf, w, a, -0.25f); Add(Grudge, w, a, 0.1f + 0.2f * C(w).Agree);
                    Remember(sim, w, Topic.Fight, a, br.Cause, 0.7f, C(a).Cell);
                }
                Remember(sim, b, Topic.Fight, a, br.Cause, 1, C(b).Cell);
                Remember(sim, a, Topic.Fight, b, br.Cause, 0.6f, C(a).Cell);
            }
        }
    }

    /// <summary>30분마다: 몰래 방해 — 깊이 원망하고 · 성실하지 않고 · 거친 사람이, 아무도 없을 때 상대가 일하는 장치에 몰래 손을 댄다 (나중에 고장 난다 · 아무도 모른다).</summary>
    private void Mischief(Sim sim)
    {
        if (_net == null) return;
        var mind = _mind!;
        for (int x = 0; x < N; x++)
        {
            if (!Awake(x)) continue;
            ref var cx = ref C(x);
            if (cx.Consc > 0.35f || cx.Agree > 0.3f) continue;
            for (int y = 0; y < N; y++)
            {
                if (y == x || GrudgeOf(x, y) < 0.7f || !Person(y) || C(y).Station < 0) continue;
                int fu = C(y).Station;
                int dev = _net.Devices.FindIndex(d => d.Furniture == fu);
                if (dev < 0 || mind.RoomOf(cx.Cell) != L.Furniture[fu].Room) continue;
                bool alone = true;
                for (int i = 0; i < N; i++) if (i != x && Awake(i) && mind.RoomOf(C(i).Cell) == L.Furniture[fu].Room) { alone = false; break; }
                if (!alone || Rnd(sim, "몰래", x, y) > (GrudgeOf(x, y) - 0.7f) * 1.5f) continue;
                var part = _net.Devices[dev].Life(Part.Mechanical) > 0 ? Part.Mechanical : Part.Electric;
                int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(mind.NameOf(x))} {mind.NameOf(y)}의 {_net.Devices[dev].Name}에 몰래 손을 댔다", sim.Tick, cx.Cell, MemoryCause(x, Topic.Blame, y), 0, x);
                _net.AgeLeft(dev, part, 1 + 3 * Rnd(sim, "몇 시간", x, y), c);
                Sabotages++;
                sim.Facts.Emit(sim.Tick, FactKind.Sabotage, cx.Cell, 0, c, x, dev);
                break;
            }
        }
    }
}
