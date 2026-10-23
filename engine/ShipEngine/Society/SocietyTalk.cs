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
        Topic.Death => "죽음", Topic.Blame => "탓", Topic.Gossip => "험담", Topic.Shortage => "걱정", Topic.Fight => "싸움", Topic.Heart => "마음", Topic.Peace => "화해", Topic.Troubled => "속마음", _ => "잡담",
    };

    public static string EndName(TalkEnd e) => e switch
    {
        TalkEnd.Agree => "맞장구", TalkEnd.Laugh => "웃음", TalkEnd.Awkward => "어색", TalkEnd.Friction => "다툼", TalkEnd.Praise => "칭찬",
        TalkEnd.Comfort => "위로", TalkEnd.Apology => "사과", TalkEnd.Argue => "다툼", TalkEnd.Spread => "퍼짐", TalkEnd.Defend => "감쌈", TalkEnd.Worry => "걱정",
        TalkEnd.Accept => "받아줌", TalkEnd.Reject => "거절", _ => "맞장구",
    };

    /// <summary>주제 · 결과의 이야기 한 줄 (기록 · 연출) — "둘이 불 이야기를 했다 → 서로 탓함".</summary>
    public static string Summary(Topic t, TalkEnd e) => e switch
    {
        TalkEnd.Accept when t == Topic.Peace => "두 무리 사이를 풀기로 했다 · 손을 잡았다", TalkEnd.Argue when t == Topic.Peace => "두 무리 사이를 두고 맞섰다 · 골이 깊어졌다",
        TalkEnd.Agree => $"{TopicName(t)} 이야기를 나눴다", TalkEnd.Laugh => "실컷 웃었다", TalkEnd.Awkward => $"{TopicName(t)} 이야기가 어색하게 끊겼다",
        TalkEnd.Friction => "말끝마다 부딪쳤다 · 사이가 조금 틀어졌다", TalkEnd.Praise => $"{TopicName(t)} 이야기를 하다 고맙다고 했다 · 가까워졌다",
        TalkEnd.Comfort when t == Topic.Troubled => "속마음을 털어놓았다 · 들어 주었다 · 가까워졌다",
        TalkEnd.Comfort => "슬픔을 나누며 위로했다 · 가까워졌다", TalkEnd.Apology => "그날 일을 사과했다 · 앙금이 풀렸다",
        TalkEnd.Argue => "그날 일을 두고 서로 탓했다 · 사이가 나빠졌다", TalkEnd.Spread => "누군가의 험담을 했다 · 소문이 퍼졌다",
        TalkEnd.Defend => "험담을 막아섰다", TalkEnd.Worry => "식량 · 물 걱정을 했다",
        TalkEnd.Accept => "마음을 고백했고 받아들여졌다 · 짝이 되었다", TalkEnd.Reject => "마음을 고백했지만 거절당했다", _ => "이야기를 했다",
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

    /// <summary>
    /// 두 사람이 말을 나눴다 (마음의 행동 '대화' — 무슨 이야기를 누가 꺼냈고 듣는 이가 어떻게 받았는지는 판단 엔진이 정했다):
    /// 결과만 계산한다 — 관계가 바뀌고 · 한 마디가 남고 · 함께 겪은 불 · 구멍 이야기는 교훈이 되고 · 꺼낸 기억은 조금 가라앉는다.
    /// </summary>
    private void Talk(Sim sim, SocialAct act)
    {
        int sp = act.Actor, li = act.Target;
        if (!Awake(sp) || !Awake(li)) return;   // 그 틱에 쓰러졌다 — 말이 끊겼다
        var o = new Option(act.Topic, 1, act.Other, act.Cause, act.Cell, sp);
        Outcome(sim, o, sp, li, act.End);
        // 함께 겪은 불 · 구멍 이야기: 들은 이는 교훈을 조금 얻고 · 말한 이는 되새긴다 (⑧에 요청)
        if (o.T is Topic.Fire or Topic.Breach)
        {
            var hz = o.T == Topic.Fire ? Hazard.Fire : Hazard.Breach;
            _mind!.Teach(li, hz, 0.08f); _mind.Teach(sp, hz, 0.05f);
        }
        LastTalk[sp * N + li] = LastTalk[li * N + sp] = sim.Tick;
        // 한 번 꺼낸 이야기는 둘에게 조금 가라앉는다 (같은 말만 되풀이하지 않게)
        if (o.Cause != 0)
            for (int k = 0; k < Memories.Count; k++)
            {
                var m = Memories[k];
                if ((m.Owner == sp || m.Owner == li) && m.Cause == o.Cause) { m.Weight *= 0.5f; Memories[k] = m; }
            }
    }

    public int MemoryCause(int owner, Topic t, int about)
    {
        foreach (var m in Memories) if (m.Owner == owner && m.Topic == t && m.About == about) return m.Cause;
        return 0;
    }
    public int MemoryCell(int owner, Topic t, int about)
    {
        foreach (var m in Memories) if (m.Owner == owner && m.Topic == t && m.About == about) return m.Cell;
        return C(owner).Cell;
    }
    public int StrongestAbout(Sim sim, int owner, Topic t)
    {
        int about = -1; float bw = 0;
        foreach (var m in Memories) if (m.Owner == owner && m.Topic == t && Weight(m, sim.Tick) > bw) { bw = Weight(m, sim.Tick); about = m.About; }
        return about;
    }

    /// <summary>대화가 흘러간 끝(듣는 이가 받은 것 — 마음이 정했다)의 결과 — 관계를 바꾸고 한 마디를 남긴다.</summary>
    private void Outcome(Sim sim, Option o, int a, int b, TalkEnd end)
    {
        ref var ca = ref C(a); ref var cb = ref C(b);
        switch (o.T)
        {
            case Topic.Blame:
            {
                // 누명을 쓴 이가 따지는 말로 거짓말을 처음 들었으면 — 그 사람이 받는다 (MindSecrets — 행동 '들은 거짓말')
                if (end == TalkEnd.Apology)   // b가 사과했다
                {
                    Apologies++;
                    int k = a * N + b; Grudge[k] *= 0.5f;
                    Warm(sim, a, b, 0.05f); Warm(sim, b, a, 0.03f); Add(TrustOf, a, b, 0.05f);
                    Say(sim, b, a, o, end, Pick(sim, "탓/사과", b));
                }
                else
                {
                    Arguments++;
                    Add(Grudge, a, b, 0.15f); Add(Grudge, b, a, 0.1f);
                    Warm(sim, a, b, -0.1f); Warm(sim, b, a, -0.1f);
                    ca.Anger = Math.Min(1, ca.Anger + 0.3f * (0.5f + ca.Neuro)); cb.Anger = Math.Min(1, cb.Anger + 0.25f * (0.5f + cb.Neuro));
                    ca.Stress = Math.Min(1, ca.Stress + 0.05f); cb.Stress = Math.Min(1, cb.Stress + 0.05f);
                    int line = Say(sim, a, b, o, end, Pick(sim, "탓/다툼", a));
                    Remember(sim, a, Topic.Fight, b, line, 0.5f, ca.Cell); Remember(sim, b, Topic.Fight, a, line, 0.5f, cb.Cell);
                    // 곁사람이 끼어들지 · 둘이 덤빌지는 그 사람들의 판단이다 (MindFeud)
                }
                break;
            }
            case Topic.Gossip:
            {
                int x = o.About;
                if (end == TalkEnd.Defend)   // 감쌌다
                {
                    Warm(sim, b, a, -0.05f);
                    Say(sim, b, a, o, end, Pick(sim, "험담/감쌈", b));
                }
                else
                {
                    // 소문: 듣는 이가 말하는 이를 믿는 만큼 · 덜 다정할수록 같이 미워한다
                    Rumors++;
                    int lg = LieOf(o.Cause);
                    if (lg >= 0)
                    {
                        // 거짓말이 험담으로 번진다: 믿을지는 듣는 이가 (그때 본 이면 들통난다 — MindSecrets)
                        Warm(sim, a, b, 0.02f);
                        Say(sim, a, b, o, end, Pick(sim, "험담/퍼짐", a));
                        break;
                    }
                    float w = GrudgeOf(a, x) * TrustIn(b, a) * (1 - cb.Agree * 0.5f);
                    if (SameBand(a, b) && BandIdOf(x) >= 0 && !SameBand(b, x)) w *= 1.5f; // 다른 무리 험담은 같은 무리 안에서 잘 먹힌다
                    Add(Grudge, b, x, w * 0.4f); Add(TrustOf, b, x, -w * 0.3f);
                    Warm(sim, a, b, 0.02f);
                    Remember(sim, b, Topic.Blame, x, o.Cause, w * 0.6f, o.Cell);
                    Say(sim, a, b, o, end, Pick(sim, "험담/퍼짐", a));
                }
                break;
            }
            case Topic.Heart:
            {
                if (end == TalkEnd.Accept && Partner[a] < 0 && Partner[b] < 0)   // 받아 주었다
                {
                    Couples++;
                    Partner[a] = b; Partner[b] = a;
                    Warm(sim, a, b, 0.15f); Warm(sim, b, a, 0.15f); Add(TrustOf, a, b, 0.1f); Add(TrustOf, b, a, 0.1f);
                    ca.Joy = Math.Min(1, ca.Joy + 0.5f); cb.Joy = Math.Min(1, cb.Joy + 0.4f);
                    int line = Say(sim, a, b, o, end, Pick(sim, "마음/받아줌", a));
                    int c = sim.Causes.Add(CauseKind.Society, $"{Ko.Gwa(_mind!.NameOf(a))} {Ko.I(_mind.NameOf(b))} 짝이 되었다", sim.Tick, ca.Cell, line, 0, a);
                    sim.Facts.Emit(sim.Tick, FactKind.Relationship, ca.Cell, 1, c, a, b);
                    Remember(sim, a, Topic.Heart, b, c, 0.9f, ca.Cell); Remember(sim, b, Topic.Heart, a, c, 0.9f, cb.Cell);
                }
                else
                {
                    end = TalkEnd.Reject; Rejections++;
                    Attract[a * N + b] *= 0.5f;
                    ca.Sad = Math.Min(1, ca.Sad + 0.3f); ca.Stress = Math.Min(1, ca.Stress + 0.1f);
                    Say(sim, b, a, o, end, Pick(sim, "마음/거절", b));
                }
                break;
            }
            case Topic.Peace:
            {
                int g1 = BandIdOf(a), g2 = BandIdOf(b);
                string n1 = BandName(g1), n2 = BandName(g2);
                if (end == TalkEnd.Accept)   // 받아들였다
                {
                    // 두 무리 사람들 사이의 원한이 크게 준다 (지도자가 제 사람들을 달랜다)
                    for (int x = 0; x < N; x++) for (int y = 0; y < N; y++)
                        if (BandIdOf(x) == g1 && BandIdOf(y) == g2 || BandIdOf(x) == g2 && BandIdOf(y) == g1) Grudge[x * N + y] *= 0.5f;
                    Warm(sim, a, b, 0.1f); Warm(sim, b, a, 0.1f); Add(TrustOf, a, b, 0.1f); Add(TrustOf, b, a, 0.1f);
                    _rift.RemoveAll(k => k == (((long)Math.Min(g1, g2) << 32) | (uint)Math.Max(g1, g2)));
                    int line = Say(sim, a, b, o, end, Pick(sim, "화해/받아줌", a));
                    int c = sim.Causes.Add(CauseKind.Society, $"{Ko.Gwa(n1)} {Ko.I(n2)} 화해했다 — {Ko.Gwa(_mind!.NameOf(a))} {Ko.I(_mind.NameOf(b))} 손을 잡았다", sim.Tick, ca.Cell, line, 0, a);
                    sim.Facts.Emit(sim.Tick, FactKind.Relationship, ca.Cell, 4, c, a, b);
                    Remember(sim, a, Topic.Peace, b, c, 0.7f, ca.Cell); Remember(sim, b, Topic.Peace, a, c, 0.7f, cb.Cell);
                }
                else
                {
                    Arguments++;
                    // 맞섰다: 지도자 사이 원한이 깊어지고 · 두 무리 사람들에게 번진다
                    Add(Grudge, a, b, 0.15f); Add(Grudge, b, a, 0.15f);
                    for (int x = 0; x < N; x++) for (int y = 0; y < N; y++)
                        if (x != y && (BandIdOf(x) == g1 && BandIdOf(y) == g2 || BandIdOf(x) == g2 && BandIdOf(y) == g1)) Add(Grudge, x, y, 0.05f);
                    ca.Anger = Math.Min(1, ca.Anger + 0.3f); cb.Anger = Math.Min(1, cb.Anger + 0.3f);
                    int line = Say(sim, b, a, o, end, Pick(sim, "화해/다툼", b));
                    int c = sim.Causes.Add(CauseKind.Society, $"{Ko.Gwa(n1)} {Ko.I(n2)} 맞섰다 — {Ko.I(_mind!.NameOf(b))} {_mind.NameOf(a)}의 손을 뿌리쳤다", sim.Tick, cb.Cell, line, 0, b);
                    sim.Facts.Emit(sim.Tick, FactKind.Relationship, cb.Cell, -4, c, b, a);
                    Remember(sim, a, Topic.Fight, b, c, 0.6f, ca.Cell); Remember(sim, b, Topic.Fight, a, c, 0.6f, cb.Cell);
                }
                break;
            }
            case Topic.Troubled:
            {
                // a가 묻고 b가 털어놓았다 — 털어놓으면 마음이 가벼워진다 (우울 −0.05 · 외상 −0.03 · 불안 −0.05)
                if (end == TalkEnd.Comfort)
                {
                    _mind!.Eased(b, 0.05f, 0.03f, 0.05f);
                    Warm(sim, b, a, 0.05f); Warm(sim, a, b, 0.03f);
                    Say(sim, a, b, o, end, Pick(sim, "속마음/위로", a));
                }
                else Say(sim, b, a, o, end, Pick(sim, "속마음/어색", b));
                break;
            }
            case Topic.Death:
            {
                if (end == TalkEnd.Comfort)
                {
                    Grief[a] = Math.Max(0, Grief[a] - 0.08f); ca.Sad = Math.Max(0, ca.Sad - 0.1f);
                    Warm(sim, a, b, 0.06f); Warm(sim, b, a, 0.04f);
                    Say(sim, b, a, o, end, Pick(sim, "죽음/위로", b));   // 위로의 말은 받아 준 이가 한다 (예전엔 슬픔이 줄어드는 이가 '괜찮아. 울어도 돼.'라고 했다)
                }
                else Say(sim, b, a, o, end, Pick(sim, "죽음/어색", b));
                break;
            }
            case Topic.Fire or Topic.Breach or Topic.Rescue or Topic.Hurt or Topic.Fight:
            {
                // 함께 겪은 일: 그때 자기를 구한 · 위기를 넘긴 상대에게 고마워했다 · 아니면 맞장구
                bool hero = end == TalkEnd.Praise;
                if (o.T == Topic.Hurt) { Warm(sim, a, b, 0.03f); Say(sim, b, a, o, end, Pick(sim, "다침/위로", b)); break; }
                Warm(sim, a, b, hero ? 0.06f : 0.04f); Warm(sim, b, a, 0.04f);
                if (hero) Add(TrustOf, a, b, 0.05f);
                string key = o.T == Topic.Rescue ? RescueKey(o, a, b, hero) : $"{TalkTable.TopicName(o.T)}/{(hero ? "칭찬" : "맞장구")}";
                Say(sim, a, b, o, end, Pick(sim, _talk!.Lines.ContainsKey(key) ? key : $"{TalkTable.TopicName(o.T)}/맞장구", a));
                break;
            }
            case Topic.Shortage:
                ca.Stress = Math.Min(1, ca.Stress + 0.05f); cb.Stress = Math.Min(1, cb.Stress + 0.05f);
                Warm(sim, a, b, 0.01f);
                Say(sim, a, b, o, end, Pick(sim, "걱정/걱정", a));
                break;
            default:
            {
                // 잡담 · 일: 맞으면 웃고 · 거친 둘은 부딪친다
                string t = o.T == Topic.Work ? "일" : "잡담";
                if (end is TalkEnd.Laugh or TalkEnd.Agree)
                {
                    Warm(sim, a, b, 0.03f); Warm(sim, b, a, 0.03f);
                    ca.Joy = Math.Min(1, ca.Joy + 0.05f); cb.Joy = Math.Min(1, cb.Joy + 0.05f);
                    Say(sim, a, b, o, end, Pick(sim, end == TalkEnd.Laugh ? "잡담/웃음" : $"{t}/맞장구", a));
                }
                else if (end == TalkEnd.Friction)
                {
                    Warm(sim, a, b, -0.02f); Warm(sim, b, a, -0.02f); Add(Grudge, b, a, 0.02f);
                    ca.Anger = Math.Min(1, ca.Anger + 0.05f); cb.Anger = Math.Min(1, cb.Anger + 0.08f);
                    Say(sim, b, a, o, end, Pick(sim, $"{t}/다툼", b));
                }
                else Say(sim, b, a, o, end, Pick(sim, "잡담/어색", b));
                break;
            }
        }
        Talks++;
    }

    /// <summary>
    /// 구조 이야기는 처지에 맞게 (docs/MIND.md 3단 — 처지에 맞는 입말): 구해진 이가 구한 이에게 '업고 나와 줘서 고마워' · 본 이가 구한 이에게 '대단했어' ·
    /// 구한 이가 구해진 이에게 '몸은 괜찮아?' · 본 이가 구해진 이에게 '그가 너를 업고 나오던 거 봤어' · 둘 다 본 이면 '다들 무사해서 다행이야'.
    /// 구해진 이는 목숨 빚으로 안다 (구한 이에게 빚진 몫).
    /// </summary>
    private string RescueKey(in Option o, int a, int b, bool heroIsListener)
    {
        int hero = o.About;
        bool Saved(int x) => x >= 0 && x < N && hero >= 0 && hero < N && x != hero && DebtOf(x, hero) > 0.25f;
        if (heroIsListener) return Saved(a) ? "구조/칭찬" : "구조/감탄";
        if (a == hero && Saved(b)) return "구조/안부";
        if (Saved(b)) return "구조/맞장구";
        return "구조/함께";
    }

    /// <summary>입말 표에서 한 줄 (말하는 사람 · 때에 따라).</summary>
    private string Pick(Sim sim, string key, int speaker)
    {
        if (_talk == null || !_talk.Lines.TryGetValue(key, out var lines) || lines.Length == 0) return "…";
        return lines[(int)(Rnd(sim, "한 마디", speaker, key.Length) * lines.Length) % lines.Length];
    }

    /// <summary>한 마디를 남긴다: 자리를 채워 원인 마디의 글로 · 대화 사실 (연출 · 화면) · 말소리 (③ 소리 — 들은 이의 귀에).</summary>
    private int Say(Sim sim, int speaker, int listener, Option o, TalkEnd end, string line, string? summaryText = null)
    {
        var mind = _mind!;
        string Name(int x) => x >= 0 && x < N ? mind.NameOf(x) : "그 사람";
        string room = RoomName(o.Cell);
        string other = Name(listener), about = Name(o.About);
        string text = line.Replace("{상대아}", Ko.A(Given(other))).Replace("{상대}", other)
            .Replace("{그이}", Ko.I(about)).Replace("{그을}", Ko.Eul(about)).Replace("{그은}", Ko.Eun(about)).Replace("{그와}", Ko.Gwa(about)).Replace("{그}", about)
            .Replace("{곳에서}", room + "에서").Replace("{곳을}", Ko.Eul(room)).Replace("{곳}", room);
        int summary = sim.Causes.Add(CauseKind.Society, summaryText ?? $"{Ko.Gwa(mind.NameOf(speaker))} {Ko.I(other)} {TalkTable.Summary(o.T, end)}", sim.Tick, C(speaker).Cell, o.Cause, 0, speaker);
        int said = sim.Causes.Add(CauseKind.Society, text, sim.Tick, C(speaker).Cell, summary, 0, speaker);
        Sayings.Add(new Saying { Tick = sim.Tick, Speaker = speaker, Listener = listener, Topic = o.T, End = end, Line = said, Cell = C(speaker).Cell });
        if (Sayings.Count > 128) Sayings.RemoveAt(0);
        sim.Facts.Emit(sim.Tick, FactKind.Talk, C(speaker).Cell, (float)end, summary, speaker, listener);
        var snd = sim.Find<Sound>();
        snd?.Emit(sim, C(speaker).Cell, end is TalkEnd.Argue or TalkEnd.Friction ? 72 : 60, SoundKind.Voice, said, speaker);   // 누구 목소리인지 (자던 이가 깨면 ⑧이 안다)
        if (end == TalkEnd.Laugh && snd != null)   // 소리 단계 S2: 둘이 함께 웃는다 (웃음소리 65dB — 1m · 크게 웃는 소리)
        {
            snd.Emit(sim, C(speaker).Cell, 65, SoundKind.Laugh, said, speaker);
            if (listener >= 0 && listener < N) snd.Emit(sim, C(listener).Cell, 63, SoundKind.Laugh, said, listener);
        }
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

    // ───────────────────────── 곁에서 본 다툼 ─────────────────────────

    /// <summary>곁에서 본 이가 한쪽 편을 들었다 (마음의 행동) — 상대는 그를 원망하고 더 화가 난다 · 편든 쪽과는 가까워진다.</summary>
    private void SideWith(Sim sim, SocialAct act)
    {
        int i = act.Actor, foe = act.Target, side = act.Other;
        if (!Awake(i) || foe < 0 || side < 0) return;
        var mind = _mind!;
        Add(Grudge, foe, i, 0.06f); Warm(sim, side, i, 0.05f); Warm(sim, foe, i, -0.05f);
        C(foe).Anger = Math.Min(1, C(foe).Anger + 0.1f);
        Sides++;
        Say(sim, i, foe, new Option(Topic.Fight, 1, side, act.Cause, C(i).Cell, i), TalkEnd.Defend, Pick(sim, act.Arg == 1 ? "다툼/편듦" : "다툼/감쌈", i),
            $"{Ko.I(mind.NameOf(i))} {Ko.Gwa(mind.NameOf(foe))} {mind.NameOf(side)}의 다툼에 끼어들어 {mind.NameOf(side)} 편을 들었다");
        Remember(sim, foe, Topic.Blame, i, act.Cause, 0.3f, C(foe).Cell);
    }

    /// <summary>곁에서 본 이가 말렸다 (마음의 행동) — 둘의 화가 반으로 (몸싸움이 덜 난다) · 둘이 말린 이를 조금 더 믿는다.</summary>
    private void Mediated(Sim sim, SocialAct act)
    {
        int i = act.Actor, a = act.Target, b = act.Other;
        if (!Awake(i) || a < 0 || b < 0) return;
        var mind = _mind!;
        C(a).Anger *= 0.5f; C(b).Anger *= 0.5f;
        Add(TrustOf, a, i, 0.03f); Add(TrustOf, b, i, 0.03f);
        Mediations++;
        Say(sim, i, a, new Option(Topic.Fight, 1, b, act.Cause, C(i).Cell, i), TalkEnd.Comfort, Pick(sim, "다툼/말림", i),
            $"{Ko.I(mind.NameOf(i))} 다투던 {Ko.Gwa(mind.NameOf(a))} {mind.NameOf(b)} 사이에 끼어 말렸다");
    }

    // ───────────────────────── 갈등 사다리 ─────────────────────────

    /// <summary>x가 y에게 덤볐다 (마음의 행동 — 끝장을 보려는가도 그가 정했다): 몸싸움이 시작된다 · 맞는 이는 겁에 질린다.</summary>
    private void Attacked(Sim sim, SocialAct act)
    {
        int x = act.Actor, y = act.Target;
        if (!Awake(x) || !Awake(y) || InBrawl(x, out _, out _) || InBrawl(y, out _, out _)) return;
        bool lethal = act.Arg != 0;
        Brawls.Add(new Brawl { A = x, B = y, Start = sim.Tick, Until = sim.Tick + Time.Minutes(lethal ? 12 : 4), Lethal = (byte)(lethal ? 1 : 0), Cause = act.Cause });
        Fights++;
        sim.Facts.Emit(sim.Tick, FactKind.Fight, C(x).Cell, lethal ? 2 : 1, act.Cause, x, y);
        var o = new Option(Topic.Fight, 1, y, act.Cause, C(x).Cell, x);
        Say(sim, x, y, o, TalkEnd.Argue, Pick(sim, lethal ? "싸움/끝장" : "싸움/시작", x));
        C(y).Fear = Math.Max(C(y).Fear, 0.6f + 0.3f * (1 - C(y).Brave));
    }

    /// <summary>x가 원망하는 y의 탓으로 x의 가까운 이가 죽었나.</summary>
    public bool DeathBlame(int x, int y)
    {
        foreach (var m in Memories) if (m.Owner == x && m.Topic == Topic.Death && m.About >= 0 && BondOf(x, m.About) > 0.4f && MemoryCause(x, Topic.Blame, y) == m.Cause) return true;
        return false;
    }

    // 이번 분의 주먹 · 맞받아침 · 말린 이 (마음의 행동 — BrawlStep이 읽고 비운다)
    private readonly HashSet<int> _struck = new(), _hitBack = new();
    private readonly Dictionary<int, int> _stopper = new();

    /// <summary>
    /// 1분마다: 벌어진 몸싸움의 결과 — 떨어졌으면 (깨어 있지 않다 · 맞는 이가 숨졌다 · 다른 방 · 3칸 넘게) 끝 · 누가 말렸으면 끝 ·
    /// 때린 이가 또 쳤으면 한 대 (⑦ 몸에 타박 · 출혈 · 비명) · 맞받아쳤으면 반쯤 · 손을 거뒀으면 끝. 끝난 뒤: 맞은 이는 두려워하고 미워한다 · 본 사람들은 때린 이를 덜 믿는다.
    /// 칠지 · 맞받아칠지 · 말릴지는 그 사람(로봇)의 판단이다 (MindFeud).
    /// </summary>
    private void BrawlStep(Sim sim)
    {
        var mind = _mind!;
        for (int k = Brawls.Count - 1; k >= 0; k--)
        {
            var br = Brawls[k];
            int a = br.A, b = br.B;
            if (br.Start == sim.Tick) continue;   // 방금 덤볐다 — 주먹은 다음 분부터
            ref var vb = ref _life![C(b).Body];
            bool apart = !Awake(a) || vb.Alive == 0 || mind.RoomOf(C(a).Cell) != mind.RoomOf(C(b).Cell) || mind.Dist(C(a).Cell, C(b).Cell) > 3;
            if (vb.Conscious == 0 && br.DownAt == 0) { br.DownAt = sim.Tick; Brawls[k] = br; }
            bool done = apart;
            if (!done && _stopper.TryGetValue(a, out int stopper) && stopper >= 0)
            {
                var o = new Option(Topic.Fight, 1, a, br.Cause, C(a).Cell, stopper);
                Say(sim, stopper, a, o, TalkEnd.Argue, Pick(sim, mind.Agents[stopper].IsPerson ? "싸움/말림" : "싸움/로봇", stopper));
                if (mind.Agents[stopper].IsPerson)
                {
                    Warm(sim, b, stopper, 0.1f); Add(Debt, b, stopper, 0.2f);
                    foreach (int w in Near(C(stopper).Cell, stopper)) if (w != a) Add(TrustOf, w, stopper, 0.05f); // 말린 이를 더 믿게 된다
                }
                done = true;
            }
            else if (!done && _struck.Contains(a))
            {
                // 한 대: 끝장을 보려는 이는 손에 잡히는 것을 휘두른다
                float r1 = Rnd(sim, "주먹", a, br.Blows), r2 = Rnd(sim, "피", a, br.Blows);
                float blunt = br.Lethal != 0 ? 0.25f + 0.3f * r1 : 0.06f + 0.1f * r1, bleed = br.Lethal != 0 ? 1.0f + 2.0f * r2 : 0.15f * r2;
                _life!.Strike(sim, C(b).Body, blunt, bleed, br.Cause);
                if (_life[C(b).Body].Conscious != 0) sim.Find<Sound>()?.Emit(sim, C(b).Cell, 85, SoundKind.Scream, br.Cause, b); // 맞는 이의 비명 — 옆 방에서도 들린다 (로봇 · 사람이 온다)
                if (_hitBack.Contains(b)) _life.Strike(sim, C(a).Body, blunt * 0.5f, bleed * 0.3f, br.Cause);   // 맞받아쳤다
                br.Blows++; Brawls[k] = br;
                if (_life[C(b).Body].Alive == 0) { Killings++; done = true; }
            }
            else done = true;   // 손을 거뒀다
            if (done)
            {
                Brawls.RemoveAt(k);
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
        _struck.Clear(); _hitBack.Clear(); _stopper.Clear();
    }

    /// <summary>x가 y의 장치에 몰래 손을 댔다 (마음의 행동) — 그 장치의 기계(없으면 전기) 수명이 몇 시간으로 줄어든다 (나중에 고장 난다 · 아무도 모른다).</summary>
    private void Sabotaged(Sim sim, SocialAct act)
    {
        if (_net == null || act.Arg < 0 || act.Arg >= _net.Devices.Count) return;
        int x = act.Actor, y = act.Target, dev = act.Arg;
        var part = _net.Devices[dev].Life(Part.Mechanical) > 0 ? Part.Mechanical : Part.Electric;
        _net.AgeLeft(dev, part, 1 + 3 * Rnd(sim, "몇 시간", x, y), act.Cause);
        Sabotages++;
        sim.Facts.Emit(sim.Tick, FactKind.Sabotage, act.Cell, 0, act.Cause, x, dev);
    }
}
