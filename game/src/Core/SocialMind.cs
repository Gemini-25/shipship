using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.15 승무원 두뇌 2.0 — ⑤ 사회적 추론: 남이 무엇을 알고 느끼는지 짐작한다.
// 내가 본 불을 저 사람은 모를 것이다(경보가 안 울렸고 · 같이 보지 않았고 · 알려 준 적 없다) → 그 사람이 있다고 믿는 곳으로 알리러 간다.
// 도움 요청(솜씨 좋은 사람에게 부탁 · 일 나누기) · 위로(얼굴을 보고 슬픔 · 두려움을 짐작) · 숨기기 · 작은 거짓말 · 털어놓기 ·
// 설득(못 믿는 사람에게 "정말이야" · 컴퓨터를 못 믿는 사람에게 "컴퓨터 말 좀 들어"). 수다 끝에 믿음이 흘러간다 (틀린 믿음도).

public sealed class SocialMind
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 3989 + 577));
    private readonly List<(int liar, int to, long tick, string what)> _lies = new();
    public int Tells, Persuasions, Lies, Confessions, Revealed, Comforts, Shares, Guesses, AlreadyKnew, Refusals, Goals;

    public SocialMind(World w) => _w = w;

    public IReadOnlyList<(int liar, int to, long tick, string what)> LiesTold => _lies;

    /// <summary>c가 짐작하기에 p도 이 위험을 아나: 경보가 울렸다 · 함께 봤다 · 내가 알려 줬다 · 그 사람이 바로 그 방에 있다고 믿는다.</summary>
    public bool ThinksKnows(CrewMember c, CrewMember p, Belief b)
    {
        if (b.Alarmed) return true;
        var bel = _w.Brain2.Beliefs;
        if (bel.Of(c).Shared.Contains((BeliefSystem.Key(b.Topic, b.Id), p.Id))) return true;
        if (b.Topic == Topic.Down && p.Id == b.Id) return true;
        if (b.Topic is Topic.Fire or Topic.Breach && bel.WhereIs(c, p, out float conf) is Room r && r.Id == b.Id && conf >= 0.3f) return true;
        return false;
    }

    /// <summary>c가 보기에 p의 기분 (최근에 얼굴을 봤어야 · 공감하는 만큼 · 수치는 잘 숨긴다).</summary>
    public (Feeling f, float v)? GuessFeeling(CrewMember c, CrewMember p)
    {
        var w = _w;
        if (p.Dead || c == p) return null;
        bool seen = p.Room == c.Room && c.Room != null || w.Brain2.Beliefs.Get(c, Topic.Person, p.Id) is Belief b && b.Src == BeliefSource.Seen && w.Tick - b.Tick < SimTime.Minutes(30);
        if (!seen || w.Brain2.Emotions.Dominant(p, 0.15f) is not { } d) return null;
        float empathy = 0.4f + 0.6f * c.Traits.Sociability;
        if (d.f == Feeling.Shame) empathy *= 0.5f;
        float v = d.v * empathy;
        if (v < 0.15f) return null;
        Guesses++;
        return (d.f, v);
    }

    // ─────────────────────────── 알리기 ───────────────────────────

    /// <summary>내가 직접 본(경보 없이) 위험을 모를 것 같은 사람 — 가까운 사이 · 자는 사람 · 가까운 곳부터.</summary>
    public (Belief b, CrewMember who, float s, string why)? TellTarget(CrewMember c, DistanceField dist, CrewMember? skip = null)
    {
        var w = _w;
        var bel = w.Brain2.Beliefs;
        (Belief, CrewMember, float, string)? best = null;
        float bs = 0f;
        foreach (var b in bel.Dangers(c, 0.6f))
        {
            if (b.Src != BeliefSource.Seen || b.Alarmed || w.Tick - b.Since > SimTime.Minutes(45)) continue;
            foreach (var p in w.Crew)
            {
                if (p == c || p == skip || p.Dead || p.Away || p.Down || p.Outside || ThinksKnows(c, p, b)) continue;
                var where = bel.WhereIs(c, p, out float wc);
                if (where != null && where != c.Room && !w.Brain2.Plans.RoomOk(c, where)) continue; // 위험하다고 믿는 방에 있다고 믿는 사람 — 갈 수 없다 (헛계획 대신 갈 수 있는 사람부터)
                bool asleep = bel.Get(c, Topic.Person, p.Id) is Belief pb && pb.Aux == 1 && pb.Value >= 0;
                int d = -1;
                if (where != null) foreach (var cell in where.Cells) { int x = dist.Get(cell); if (x >= 0 && (d < 0 || x < d)) d = x; }
                float s = 0.6f + 0.4f * MathF.Max(0f, c.AffinityTo(p)) + (asleep ? 0.2f : 0f) + (p.IsChild ? 0.15f : 0f) + (where == null ? -0.3f : 0f) - (d > 0 ? MathF.Min(0.3f, d * 0.004f) : 0f);
                if (s <= bs) continue;
                bs = s;
                string what = bel.Describe(b);
                best = (b, p, s, $"{what} — {p.Name}은(는) 모를 것 (경보가 안 울렸다{(asleep ? " · 자고 있다" : "")})");
            }
        }
        return best;
    }

    /// <summary>만나서 알린다: 믿음이 옮겨 가고(사이가 좋을수록 믿는다), 진짜 사고면 그 사람도 안다(Mind) · 자면 깨운다 · 안 믿으면 설득한다.</summary>
    public void TellNow(CrewMember c, CrewMember p, Topic t, int id)
    {
        var w = _w;
        var bel = w.Brain2.Beliefs;
        if (bel.Get(c, t, id) is not Belief cb) return;
        long key = BeliefSystem.Key(t, id);
        bel.Of(c).Shared.Add((key, p.Id));
        bel.Of(p).Shared.Add((key, c.Id));
        string what = bel.Describe(cb);
        if (bel.Get(p, t, id) is Belief known && known.Value == cb.Value && bel.Eff(known) >= 0.5f)
        {
            AlreadyKnew++;
            p.Say(w, Persona.Say(p, $"알아, {BeliefSystem.SourceName(known.Src)}로 들었어"));
            return;
        }
        if (p.Pose == Pose.Sleeping) { p.DeepAsleep = false; p.Interrupt(w); w.Log.Add(w.Tick, LogKind.Life, $"{Ko.EulReul(p.Name)} 흔들어 깨웠다 — {what}", c.Id); }
        float conf = 0.55f + 0.3f * MathF.Max(0f, p.AffinityTo(c)) + (Memory.AreComrades(p, c) ? 0.1f : 0f) - (w.Brain2.Emotions.Get(p, Feeling.Anger) > 0.5f ? 0.15f : 0f);
        c.Say(w, Persona.Say(c, cb.Topic == Topic.Fire ? $"{bel.RoomById(id)?.Name ?? "저쪽"}에 불이야! 경보가 안 울렸어" : $"{what}! 빨리"));
        var pb = bel.Learn(p, t, id, cb.Value, BeliefSource.Told, conf, c.Id);
        Tells++;
        bel.Of(c).Told++;
        // 안 믿으면 설득한다 (사교적일수록)
        if (pb != null && (pb.Value != cb.Value || bel.Eff(pb) < 0.5f) && c.Traits.Sociability >= 0.4f)
        {
            Persuasions++;
            c.Say(w, Persona.Say(c, "정말이야, 내 눈으로 봤어"));
            pb = bel.Learn(p, t, id, cb.Value, BeliefSource.Told, MathF.Min(1f, conf + 0.3f), c.Id);
        }
        bool believes = pb != null && pb.Value == cb.Value && bel.Eff(pb) >= 0.4f;
        p.Say(w, Persona.Say(p, believes ? (cb.Topic == Topic.Fire ? "정말? 가 볼게!" : "알았어!") : "설마… 내가 직접 볼게"));
        if (believes && cb.Value == 1 && BeliefSystem.MindKey(t, id) is string mk && mk.Length > 0 && w.Minds.Incidents().Any(i => i.key == mk))
            w.Minds.Hear(p, mk, what);
        w.Log.Add(w.Tick, LogKind.Life, $"{p.Name}에게 알렸다 — {what} ({(believes ? "믿었다" : "반신반의")})", c.Id);
        p.ChangeAffinity(c, 0.02f);
    }

    // ─────────────────────────── 도움 요청 ───────────────────────────

    /// <summary>부탁할 사람: 솜씨(누구나 아는 것) · 사이 · 어디 있는지 안다 · 자는 것 같지 않다 · 나한테 화나 보이지 않는다.</summary>
    public CrewMember? Helper(CrewMember c, Skill sk, float min)
    {
        var w = _w;
        var bel = w.Brain2.Beliefs;
        CrewMember? best = null;
        float bs = float.MinValue;
        foreach (var o in w.Crew)
        {
            if (o == c || o.Dead || o.Away || !o.CanAct || o.IsChild || o.SkillLevel(sk) < min) continue;
            var pb = bel.Get(c, Topic.Person, o.Id);
            bool knowWhere = o.Room == c.Room || pb != null && pb.Value >= 0 && bel.Eff(pb) >= 0.2f;
            if (!knowWhere || pb is { Aux: 1 }) continue;
            float s = o.SkillLevel(sk) + 0.5f * c.AffinityTo(o) - (GuessFeeling(c, o) is { f: Feeling.Anger } ? 0.5f : 0f);
            if (s > bs || s == bs && best != null && o.Id < best.Id) { bs = s; best = o; }
        }
        return best;
    }

    /// <summary>부탁을 들어주나: 하던 계획 · 사이 · 피로 · 분노.</summary>
    public bool Agrees(CrewMember helper, CrewMember asker, out string no)
    {
        var w = _w;
        no = "";
        if (w.Brain2.Plans.Current(helper) != null) no = "나도 하던 게 있어";
        else if (helper.AffinityTo(asker) < -0.2f) no = "네 일은 네가 해";
        else if (helper.Needs.Rest < 0.2f) no = "너무 지쳤어, 나중에";
        else if (helper.Mind.Anger > 0.6f) no = "지금은 건드리지 마";
        if (no.Length > 0) { Refusals++; return false; }
        return true;
    }

    // ─────────────────────────── 수다 끝에 ───────────────────────────

    /// <summary>
    /// 수다를 마치며: 새 소식(고친 믿음 · 위험)이 흘러간다 · 얼굴을 보고 위로한다 · 부끄러운 일은 숨기거나(작은 거짓말) 털어놓는다 ·
    /// 컴퓨터를 믿는 사람이 못 믿는 사람을 설득한다 · 목표가 말에 묻어난다.
    /// </summary>
    public void Chat(CrewMember a, CrewMember b)
    {
        var w = _w;
        if (!BrainSystem.Enabled || a.Dead || b.Dead) return;
        foreach (var (x, y) in new[] { (a, b), (b, a) })
        {
            if (x.IsChild) continue;
            if (Share(x, y)) return;
            if (Comfort(x, y)) return;
            if (Secret(x, y)) return;
        }
        if (w.Automation.Present && R.Chance(0.3f))
        {
            var t = w.Automation.Trusts;
            var (hi, lo) = t.Of(a) >= t.Of(b) ? (a, b) : (b, a);
            if (t.Of(hi) >= 0.65f && t.Of(lo) < 0.4f && hi.Traits.Sociability >= 0.5f && !hi.IsChild)
            {
                Persuasions++;
                hi.Say(w, Persona.Say(hi, "컴퓨터 말 좀 들어. 그게 살 길이야"));
                t.Change(lo, 0.04f, $"{hi.Name}의 설득");
                return;
            }
        }
        // 목표가 말에 묻어난다
        var speaker = R.Chance(0.5f) ? a : b;
        if (!speaker.IsChild && R.Chance(0.35f) && w.Brain2.Goals.Layer(speaker, GoalLayer.Mid).Concat(w.Brain2.Goals.Layer(speaker, GoalLayer.Long)).FirstOrDefault() is CrewGoal g)
        {
            Goals++;
            speaker.Say(w, Persona.Say(speaker, g.Layer == GoalLayer.Long ? $"언젠가는 — {g.Text}" : $"요즘은 {g.Text} 생각뿐이야"));
        }
        else if (w.Brain2.Emotions.Phrase(speaker) is string ph) speaker.Say(w, Persona.Say(speaker, ph));
    }

    /// <summary>새 소식: 오늘 고친 믿음("헛소문이었어") · 모를 것 같은 위험 — 듣는 사람은 사이만큼 믿는다 (틀린 믿음도 퍼진다).</summary>
    private bool Share(CrewMember x, CrewMember y)
    {
        var w = _w;
        var bel = w.Brain2.Beliefs;
        var book = bel.Of(x);
        float conf = 0.45f + 0.35f * MathF.Max(0f, y.AffinityTo(x));
        foreach (var d in bel.Dangers(x, 0.5f))
        {
            if (ThinksKnows(x, y, d)) continue;
            TellNow(x, y, d.Topic, d.Id);
            Shares++;
            return true;
        }
        if (book.LastCorrection >= 0 && w.Tick - book.LastCorrection < SimTime.Hours(6))
        {
            foreach (var b in book.All)
            {
                if (b.Tick != book.LastCorrection || b.Src != BeliefSource.Seen) continue;
                long key = BeliefSystem.Key(b.Topic, b.Id);
                if (book.Shared.Contains((key, y.Id))) continue;
                book.Shared.Add((key, y.Id));
                x.Say(w, Persona.Say(x, $"그거 알아? {book.LastCorrectionText}"));
                bel.Learn(y, b.Topic, b.Id, b.Value, BeliefSource.Told, conf, x.Id, b.Aux);
                Shares++;
                return true;
            }
        }
        return false;
    }

    /// <summary>얼굴을 보고 짐작한 기분: 두렵거나 슬퍼 보이면 위로한다 · 기뻐 보이면 같이 기쁘다.</summary>
    private bool Comfort(CrewMember x, CrewMember y)
    {
        var w = _w;
        if (GuessFeeling(x, y) is not { } g) return false;
        var emo = w.Brain2.Emotions;
        if (g.f is Feeling.Fear or Feeling.Sadness && g.v >= 0.25f)
        {
            Comforts++;
            x.Say(w, Persona.Say(x, g.f == Feeling.Fear ? "괜찮아, 내가 옆에 있을게" : "많이 힘들지. 다 지나갈 거야"));
            emo.Feel(y, g.f, -0.12f, "");
            emo.Feel(y, Feeling.Joy, 0.06f, $"{Ko.IGa(x.Name)} 다독여 줬다", x);
            y.ChangeAffinity(x, 0.03f);
            return true;
        }
        if (g.f == Feeling.Joy && g.v >= 0.3f) { emo.Feel(x, Feeling.Joy, 0.04f, $"{y.Name}이(가) 즐거워 보여서"); return false; }
        return false;
    }

    /// <summary>부끄러운 일 (실수): 숨기고 작은 거짓말을 하거나, 털어놓는다. 거짓말한 사람에게 나중에 털어놓으면 들통난다.</summary>
    private bool Secret(CrewMember x, CrewMember y)
    {
        var w = _w;
        var emo = w.Brain2.Emotions;
        var e = emo.Of(x);
        float shame = e[Feeling.Shame];
        // 예전에 이 사람에게 거짓말을 했고, 이제 부끄러움이 가라앉았다 → 털어놓는다 (들통)
        int li = _lies.FindIndex(l => l.liar == x.Id && l.to == y.Id);
        if (li >= 0 && (shame < 0.15f || x.Value is CrewValue.People or CrewValue.Rules) && w.Tick - _lies[li].tick > SimTime.Hours(6))
        {
            var lie = _lies[li];
            _lies.RemoveAt(li);
            Revealed++;
            Confessions++;
            x.Say(w, Persona.Say(x, $"그때 말이야 — 사실 내가 {lie.what}"));
            y.Say(w, Persona.Say(y, "…그걸 왜 이제 말해"));
            emo.Feel(y, Feeling.Anger, 0.15f, $"{Ko.IGa(x.Name)} 거짓말을 했다", x);
            y.ChangeAffinity(x, -0.05f);
            Life.Diary(w, y, Persona.Say(y, $"{Ko.IGa(x.Name)} 나한테 거짓말을 했었다. {lie.what}"));
            return true;
        }
        if (shame < 0.3f || e.Cause[(int)Feeling.Shame] is not string why || !why.Contains("실수")) return false;
        if (!R.Chance(0.4f + 0.3f * y.Traits.Sociability)) return false;
        y.Say(w, Persona.Say(y, "무슨 일 있었어? 표정이 안 좋네"));
        bool hide = x.Value is not CrewValue.Rules && x.Traits.Diligence < 0.75f && R.Chance(0.35f + 0.4f * shame - 0.3f * MathF.Max(0f, x.AffinityTo(y)));
        if (hide)
        {
            Lies++;
            _lies.Add((x.Id, y.Id, w.Tick, "실수했었어"));
            if (_lies.Count > 60) _lies.RemoveAt(0);
            x.Say(w, Persona.Say(x, "아무 일도 없었어"));
            emo.Feel(x, Feeling.Shame, 0.05f, "거짓말을 했다");
            w.Log.Add(w.Tick, LogKind.Life, $"{y.Name}에게 실수를 숨겼다 (작은 거짓말)", x.Id);
            return true;
        }
        Confessions++;
        x.Say(w, Persona.Say(x, "사실… 내가 실수했어"));
        emo.Feel(x, Feeling.Shame, -0.25f, "");
        if (y.Value == CrewValue.Rules) emo.Feel(y, Feeling.Anger, 0.08f, $"{Ko.IGa(x.Name)} 실수했다", x);
        else { y.Say(w, Persona.Say(y, "말해 줘서 고마워. 누구나 그래")); y.ChangeAffinity(x, 0.03f); }
        return true;
    }

    public long Hash() => Tells * 7L + Persuasions * 11 + Lies * 13 + Confessions * 17 + Comforts * 19 + Shares * 23;
}

/// <summary>v16.15 알리러 감: 내가 본 위험을 모를 것 같은 사람에게 간다 (그 사람이 있다고 믿는 곳으로 — 틀리면 다시 찾는다).</summary>
public sealed class TellActivity : Activity
{
    public override string Id => "tell";
    public override string Label => "알리러 감";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!BrainSystem.Enabled || c.Outside || c.IsChild || !c.IsAwake || c.Down || c.CarryingPerson != null || w.Brain2.Plans.Current(c) != null) return (0f, "—");
        if (w.Brain2.Social.TellTarget(c, dist) is not { } t) return (0f, "—");
        float fear = w.Brain2.Emotions.Get(c, Feeling.Fear);
        float s = 0.7f + 0.3f * c.Traits.Sociability + (c.Value == CrewValue.People ? 0.15f : 0f) + 0.15f * MathF.Max(0f, c.AffinityTo(t.who)) - 0.3f * fear * (1f - c.Traits.Bravery);
        float danger = EvacuateActivity.DangerHere(c, w);
        s *= w.Brain2.Learning.Bias(c, Method.Tell);
        // 어차피 나가야 한다 — 나가는 길에 알린다 (겁 많고 사교적일수록 대피 대신 알리며 나간다)
        // 피하던 중이면 피하는 길을 알리는 길로 바꾼다 (대피의 버티기 여유를 넘을 만큼)
        // 나가는 길에 알리기도 나가는 길이다 — 두려움이 대피를 끄는 만큼 이 길도 끈다 (두려움이 대피만 키우면 알리러 가던 사람이 그냥 숨는다)
        if (danger > 0.2f) return (Math.Clamp(s * t.s + danger * (0.6f + 0.6f * c.Traits.Sociability) * (1.3f - 0.6f * c.Traits.Bravery) + (c.Job?.Activity is EvacuateActivity ? 0.45f : 0f), 0f, 2.3f)
                                   * w.Brain2.Emotions.Tilt(c, ActCat.Survival), t.why + " · 나가는 길에");
        if (c.Job?.Activity is EvacuateActivity) s += 0.4f;
        return (Math.Clamp(s * t.s, 0f, 1.3f), t.why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Brain2.Social.TellTarget(c, dist) is not { } t) return null;
        var plans = w.Brain2.Plans;
        string what = w.Brain2.Beliefs.Describe(t.b);
        var p = plans.Begin(c, PlanKind.Tell, $"{t.who.Name}에게 알리기 — {what}", t.why, Method.Tell);
        p.FactTopic = t.b.Topic;
        p.FactId = t.b.Id;
        p.For = t.who;
        plans.Add(p, StepKind.Tell, Method.Tell, $"{t.who.Name} 찾아가 알리기", null, null, t.who);
        return plans.JobFor(c, p, dist, PlanActivity.Instance);
    }
}
