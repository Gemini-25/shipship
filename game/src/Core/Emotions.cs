using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.15 승무원 두뇌 2.0 — ④ 감정: 분노 · 두려움 · 기쁨 · 슬픔 · 수치 · 자부심.
// 사건(고침 · 구함 · 실패 · 실수 · 공황 · 쓰러짐 · 죽음 · 말다툼 · 밥 · 수다 · 믿음이 틀림 · 목표에 다가감)으로 생기고,
// 성격대로(침착하면 빨리 · 겁 많으면 두려움이 크게) 가라앉는다. 감정은 행동 점수 · 말 · 일기 · 관계 · 스트레스(기분)를 바꾼다.
// 분노는 v13.3 Mind.Anger와 같은 값이다 (명령을 덜 듣는다).

public enum Feeling : byte { Anger, Fear, Joy, Sadness, Shame, Pride }

public sealed class EmotionState
{
    public readonly float[] V = new float[6];
    public readonly string?[] Cause = new string?[6];
    public readonly long[] Since = new long[6];
    public readonly int[] About = { -1, -1, -1, -1, -1, -1 };
    /// <summary>오늘 가장 컸던 감정과 그 까닭 (일기).</summary>
    public readonly float[] Peak = new float[6];
    public readonly string?[] PeakCause = new string?[6];
    public int Felt;

    public float this[Feeling f] => V[(int)f];
}

public sealed class EmotionSystem
{
    private readonly World _w;
    private readonly Dictionary<int, EmotionState> _s = new();
    private readonly Dictionary<int, int[]> _snap = new();
    private readonly HashSet<int> _dead = new();
    private readonly Dictionary<int, float> _trauma = new();
    private readonly Dictionary<int, long> _quarrel = new();

    public int Events, Rises;
    public static readonly Feeling[] All = { Feeling.Anger, Feeling.Fear, Feeling.Joy, Feeling.Sadness, Feeling.Shame, Feeling.Pride };

    public EmotionSystem(World w) => _w = w;

    public EmotionState Of(CrewMember c)
    {
        if (!_s.TryGetValue(c.Id, out var e)) _s[c.Id] = e = new EmotionState();
        return e;
    }

    public float Get(CrewMember c, Feeling f) => f == Feeling.Anger ? c.Mind.Anger : _s.TryGetValue(c.Id, out var e) ? e.V[(int)f] : 0f;

    public static string Name(Feeling f) => f switch
    {
        Feeling.Anger => "분노", Feeling.Fear => "두려움", Feeling.Joy => "기쁨",
        Feeling.Sadness => "슬픔", Feeling.Shame => "수치", _ => "자부심",
    };

    /// <summary>성격이 감정을 키우거나 누른다: 용감 · 침착하면 두려움이 작고, 성실하면 수치 · 자부심이 크고, 사교적이면 기쁨이 크다.</summary>
    public static float Gain(CrewMember c, Feeling f)
    {
        var t = c.Traits;
        return f switch
        {
            Feeling.Fear => (1.2f - 0.5f * t.Bravery) * (1.15f - 0.4f * t.Calm),
            Feeling.Anger => 1.2f - 0.6f * t.Calm,
            Feeling.Joy => 0.75f + 0.5f * t.Sociability,
            Feeling.Sadness => 1.15f - 0.3f * t.Calm,
            Feeling.Shame => 0.6f + 0.8f * t.Diligence,
            _ => 0.7f + 0.6f * t.Diligence,
        };
    }

    /// <summary>감정이 생긴다 (음수면 누그러진다). about = 그 감정이 향하는 사람.</summary>
    public void Feel(CrewMember c, Feeling f, float amount, string cause, CrewMember? about = null)
    {
        if (!BrainSystem.Enabled || c.Dead || c.IsChild && f == Feeling.Shame) return;
        var e = Of(c);
        int i = (int)f;
        float add = amount > 0f ? amount * Gain(c, f) : amount;
        if (f == Feeling.Anger)
        {
            c.Mind.Anger = Math.Clamp(c.Mind.Anger + add, 0f, 1f); // v13.3 분노 = 명령을 덜 듣는다
            e.V[i] = c.Mind.Anger;
        }
        else e.V[i] = Math.Clamp(e.V[i] + add, 0f, 1f);
        if (amount > 0f)
        {
            Rises++;
            e.Felt++;
            if (cause.Length > 0 && (e.Cause[i] == null || amount >= 0.08f || e.V[i] < 0.15f)) { e.Cause[i] = cause; e.Since[i] = _w.Tick; }
            if (about != null) e.About[i] = about.Id;
            if (e.V[i] > e.Peak[i]) { e.Peak[i] = e.V[i]; e.PeakCause[i] = e.Cause[i]; }
        }
    }

    /// <summary>가장 큰 감정 (0.2 미만이면 없음) — 머리 위 그림 · 말 · 남이 짐작하는 것.</summary>
    public (Feeling f, float v)? Dominant(CrewMember c, float min = 0.2f)
    {
        if (c.Dead || !_s.TryGetValue(c.Id, out var e)) return null;
        e.V[0] = c.Mind.Anger;
        int best = -1;
        for (int i = 0; i < 6; i++) if (e.V[i] >= min && (best < 0 || e.V[i] > e.V[best])) best = i;
        return best < 0 ? null : ((Feeling)best, e.V[best]);
    }

    /// <summary>반감기 (시간): 두려움은 금방, 슬픔은 오래. 침착할수록 빨리 가라앉고, 자면 두려움 · 수치가 덜어진다.</summary>
    private static float HalfLife(CrewMember c, Feeling f)
    {
        float calm = c.Traits.Calm;
        float h = f switch
        {
            Feeling.Fear => 0.75f * (1.4f - 0.8f * calm),
            Feeling.Joy => 3f,
            Feeling.Sadness => 18f * (1.2f - 0.4f * calm),
            Feeling.Shame => 8f * (0.6f + 0.8f * c.Traits.Diligence),
            Feeling.Pride => 8f,
            _ => 24f,
        };
        if (c.Pose == Pose.Sleeping && f is Feeling.Fear or Feeling.Shame) h *= 0.6f;
        return h;
    }

    // ─────────────────────────── 점수 기울이기 ───────────────────────────

    /// <summary>감정이 행동 종류의 점수를 바꾼다 (두려우면 피하고 · 슬프면 쉬고 · 화나면 수다를 덜 · 부끄러우면 일로 만회 · 기쁘면 어울린다).</summary>
    public float Tilt(CrewMember c, ActCat cat)
    {
        if (!_s.TryGetValue(c.Id, out var e)) return 1f;
        float an = c.Mind.Anger, fe = e.V[1], jo = e.V[2], sa = e.V[3], sh = e.V[4], pr = e.V[5];
        float m = cat switch
        {
            ActCat.Survival => 1f + 0.5f * fe,
            ActCat.Work => 1f - 0.25f * sa - 0.15f * fe + 0.15f * sh + 0.1f * pr - 0.05f * an,
            ActCat.Social => 1f + 0.3f * jo - 0.35f * an - 0.3f * sh + 0.15f * sa * c.Traits.Sociability,
            ActCat.Hobby => 1f + 0.2f * jo - 0.3f * fe - 0.2f * sa,
            ActCat.Rest => 1f + 0.3f * sa + 0.2f * fe + 0.1f * an,
            ActCat.Care => 1f + 0.35f * sa + 0.1f * sh,
            ActCat.Explore => 1f + 0.2f * pr + 0.1f * jo - 0.4f * fe,
            _ => 1f,
        };
        return Math.Clamp(m, 0.5f, 1.6f);
    }

    /// <summary>감정이 묻어나는 말 한 마디 (사람마다 말투가 다르다).</summary>
    public string? Phrase(CrewMember c)
    {
        if (Dominant(c, 0.3f) is not { } d) return null;
        var e = Of(c);
        string? why = e.Cause[(int)d.f];
        string[] pool = d.f switch
        {
            Feeling.Fear => new[] { "무서워…", "손이 떨려", "괜찮겠지?", "심장이 쿵쾅거려" },
            Feeling.Anger => new[] { "짜증 나", "이게 말이 돼?", "참는 것도 한계가 있어", "누가 이렇게 해 놨어" },
            Feeling.Joy => new[] { "오늘 괜찮네", "좋다!", "기분 좋은 날이야", "히히" },
            Feeling.Sadness => new[] { "…", "보고 싶다", "마음이 무거워", "그냥 좀 그래" },
            Feeling.Shame => new[] { "내 잘못이야", "얼굴을 못 들겠어", "다음엔 안 그럴게", "미안해" },
            _ => new[] { "내가 해냈어", "봤지?", "이 정도야 뭐", "뿌듯하다" },
        };
        string line = pool[(c.Id * 7 + (int)(_w.Tick / SimTime.Hours(3))) % pool.Length];
        return why != null && d.v >= 0.45f ? $"{line} — {why}" : line;
    }

    // ─────────────────────────── 시스템 틱 ───────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (!BrainSystem.Enabled) return;
        // 죽음: 남은 사람은 슬프다 (가까울수록) · 조금 무섭다
        foreach (var d in w.Crew)
        {
            if (!d.Dead || _dead.Contains(d.Id)) continue;
            _dead.Add(d.Id);
            if (w.Tick < SimTime.Minutes(1)) continue;
            Events++;
            foreach (var c in w.Crew)
            {
                if (c.Dead || c == d) continue;
                float aff = MathF.Max(0f, c.AffinityTo(d));
                Feel(c, Feeling.Sadness, 0.25f + 0.6f * aff + (c.Partner == d.Id || d.Parents.Contains(c.Id) || c.Parents.Contains(d.Id) ? 0.4f : 0f), $"{d.Name}의 죽음", d);
                Feel(c, Feeling.Fear, 0.12f, $"{d.Name}의 죽음");
            }
        }
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            var e = Of(c);
            Detect(c, e);
            // 가라앉는다 (분노는 Mind가 하루 0.25씩 — 여기서는 크게 화났을 때만 더)
            for (int i = 1; i < 6; i++)
                if (e.V[i] > 0f) e.V[i] = MathF.Max(0f, e.V[i] * MathF.Pow(0.5f, dt / HalfLife(c, (Feeling)i)) - 0.002f * dt);
            if (c.Mind.Anger > 0.5f) c.Mind.Anger = MathF.Max(0f, c.Mind.Anger * MathF.Pow(0.5f, dt / (6f * (0.6f + 0.8f * (1f - c.Traits.Calm)))));
            e.V[0] = c.Mind.Anger;
            // 무서운 방에 있으면 두려움이 스민다 (공포 기억 → 감정)
            if (c.IsAwake && c.Room is Room r && c.Memory.FearOf(r) > 0.35f && e.V[1] < c.Memory.FearOf(r) * 0.6f)
                Feel(c, Feeling.Fear, 0.4f * c.Memory.FearOf(r) * dt, c.Memory.FearCause[r.Id] is string fc ? $"{r.Name} — {fc}" : r.Name);
            // 기분: 감정이 스트레스를 바꾼다
            float stress = (0.05f * e.V[1] + 0.03f * e.V[3] + 0.03f * e.V[4] + 0.02f * e.V[0] - 0.04f * e.V[2] - 0.03f * e.V[5]) * dt;
            if (stress != 0f) c.Needs.Stress = Math.Clamp(c.Needs.Stress + stress, 0f, 1f);
            // 관계: 누구에게 향한 감정은 사이를 바꾼다
            for (int i = 0; i < 6; i++)
            {
                if (e.About[i] < 0 || e.V[i] < 0.2f) continue;
                var o = w.Brain2.Beliefs.CrewById(e.About[i]);
                if (o == null || o.Dead || o == c) continue;
                float delta = (Feeling)i switch { Feeling.Anger => -0.02f, Feeling.Joy => 0.012f, Feeling.Pride => 0.006f, _ => 0f } * e.V[i] * dt;
                if (delta != 0f) c.ChangeAffinity(o, delta);
            }
        }
    }

    /// <summary>지난번과 견준 사건: 고침 · 구함 · 실패 · 실수 · 공황 · 쓰러짐 · 밥 · 수다 · 가르침 · 수확 · 말다툼 · 긴장.</summary>
    private void Detect(CrewMember c, EmotionState e)
    {
        var s = c.Stats;
        int[] now = { s.Repairs, s.Rescues, s.JobsFailed, s.Mistakes, s.Panics, s.TimesDown, s.Meals, s.Chats, s.Taught, s.Harvests, s.MealsCooked };
        if (!_snap.TryGetValue(c.Id, out var old))
        {
            _snap[c.Id] = now;
            _trauma[c.Id] = c.Memory.Trauma;
            _quarrel[c.Id] = c.Quarrel;
            return;
        }
        int D(int i) => now[i] - old[i];
        bool garden = _w.Brain2.Goals.Has(c, "garden");
        if (D(0) > 0) { Feel(c, Feeling.Pride, 0.12f, "고쳐 냈다"); Feel(c, Feeling.Joy, 0.04f, "고쳐 냈다"); Events++; }
        if (D(1) > 0) { Feel(c, Feeling.Pride, 0.4f, "사람을 구했다"); Feel(c, Feeling.Joy, 0.25f, "사람을 구했다"); Events++; }
        if (D(2) > 0) { Feel(c, Feeling.Anger, 0.04f * D(2), "일이 틀어졌다"); if (c.Traits.Diligence > 0.5f) Feel(c, Feeling.Shame, 0.04f, "일이 틀어졌다"); Events++; }
        if (D(3) > 0) { Feel(c, Feeling.Shame, 0.4f, "실수했다"); Events++; }
        if (D(4) > 0) { Feel(c, Feeling.Shame, 0.15f, "공황에 빠졌다"); Feel(c, Feeling.Fear, 0.2f, "공황"); Events++; }
        if (D(5) > 0) { Feel(c, Feeling.Fear, 0.5f, "쓰러졌다"); Events++; }
        if (D(6) > 0) Feel(c, Feeling.Joy, 0.04f, "밥");
        if (D(7) > 0) Feel(c, Feeling.Joy, 0.05f * (0.5f + c.Traits.Sociability), "수다");
        if (D(8) > 0) Feel(c, Feeling.Pride, 0.1f, "가르쳤다");
        if (D(9) > 0) Feel(c, Feeling.Joy, garden ? 0.12f : 0.04f, "수확");
        if (D(10) > 0) Feel(c, Feeling.Pride, 0.05f, "밥을 지었다");
        _snap[c.Id] = now;
        if (c.Quarrel != _quarrel.GetValueOrDefault(c.Id) && c.Quarrel > 0) { Feel(c, Feeling.Anger, 0.3f, "말다툼"); Events++; }
        _quarrel[c.Id] = c.Quarrel;
        float tr = _trauma.GetValueOrDefault(c.Id);
        if (c.Memory.Trauma > tr + 0.02f) { Feel(c, Feeling.Fear, 0.2f, c.Memory.TraumaCause ?? "큰일"); Feel(c, Feeling.Sadness, 0.1f, c.Memory.TraumaCause ?? "큰일"); Events++; }
        _trauma[c.Id] = c.Memory.Trauma;
    }

    /// <summary>하루가 끝나면 오늘의 봉우리를 비운다 (일기를 쓴 뒤).</summary>
    public void NewDay(CrewMember c)
    {
        var e = Of(c);
        Array.Clear(e.Peak);
        Array.Clear(e.PeakCause);
    }

    public long Hash()
    {
        long h = 23;
        foreach (var (id, e) in _s) { h = h * 31 + id; for (int i = 1; i < 6; i++) h = h * 31 + (int)(e.V[i] * 1000); }
        return h;
    }
}
