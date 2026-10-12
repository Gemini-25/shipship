using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v21 이야기꾼 · 난이도 세분화
//  난이도 8단계: 느긋 · 쉬움 · 보통 · 어려움 · 가혹 + 혹독 · 지옥 · 절멸.
//   혹독부터는 사고가 겹친다 (수습하는 중에도 기다려 주지 않는다 · 큰 사고 뒤 숨 돌릴 틈이 짧다 · 곧이어 하나 더),
//   큰 사고 · 배 전체급 · 우주급 재난이 잦고 크다. 물자 · 부상은 가혹 그대로.
//  새 성격 6:
//   연쇄형     — 한 사고가 다음을 부른다: 같은 방 · 이어지는 갈래로 두세 번 번지고, 끝나면 길게 쉰다.
//   포위형     — 배 바깥 위협만 (운석 · 잔해 · 폭풍 · 선체).
//   내부 붕괴형 — 사람 · 설비 문제만 (고장 · 병 · 실수).
//   마라톤형   — 작은 것만 끝없이: 잦고, 큰 것은 거의 없다.
//   절정형     — 항해 앞쪽은 조용하다가 목적지에 다가갈수록 잦고 크게, 직전에 몰아친다.
//   파도형     — 잠잠해진 뒤 한 무리가 몰려온다. 물결마다 더 크다.
//  리듬 (어느 성격이든): 잦기 · 크기 · 몰아치기를 다섯 칸씩 (가운데 = 성격대로).
//  무작위 사고 (이야기꾼을 끄면): 주기 10단계 + 크기 범위 (작은 것만 · 섞어 · 큰 것만).
// 기본값(보통 이하 · 리듬 가운데)에선 원래 이야기꾼과 난수 한 알까지 같다 → 예전 기록 · 시험이 그대로.

public sealed partial class Storyteller
{
    public const int MaxLevel = 8;

    /// <summary>리듬 (−2 ~ +2, 0 = 성격대로): 잦기 · 크기 · 몰아치기.</summary>
    public static float FreqValue, SizeValue, BurstValue;
    public static int Freq => Math.Clamp((int)MathF.Round(FreqValue), -2, 2);
    public static int Size => Math.Clamp((int)MathF.Round(SizeValue), -2, 2);
    public static int Burstiness => Math.Clamp((int)MathF.Round(BurstValue), -2, 2);

    private static readonly float[] FreqMuls = { 2.0f, 1.4f, 1f, 0.7f, 0.45f };
    private static readonly float[] BigMuls = { 0.25f, 0.5f, 1f, 1.8f, 3f }, SmallMuls = { 2f, 1.4f, 1f, 0.8f, 0.6f };
    private static readonly float[] BurstMuls = { 0f, 0.5f, 1f, 1.5f, 2f }, BurstAdds = { 0f, 0f, 0f, 0.2f, 0.4f };
    public static float FreqMul => FreqMuls[Freq + 2];
    public static float BurstMul => BurstMuls[Burstiness + 2];

    public static readonly string[] FreqNames = { "아주 드물게", "드물게", "성격대로", "잦게", "아주 잦게" };
    public static readonly string[] SizeNames = { "아주 작게", "작게", "성격대로", "크게", "아주 크게" };
    public static readonly string[] BurstNames = { "없음", "덜", "성격대로", "더", "몰아서" };

    public static string LevelNote(int l) => l switch
    {
        1 => "사고가 드물고 작다 · 물자가 넉넉하다",
        2 => "가끔 · 회복할 틈이 넉넉하다",
        3 => "고르게 · 거의 추스르면 다음",
        4 => "잦고 크다 · 물자가 빠듯하다",
        5 => "추스르기 전에 다음 · 물자가 모자라다",
        6 => "사고가 겹친다 · 큰 사고가 잦다",
        7 => "숨 돌릴 틈이 거의 없다 · 우주급 재난이 잦다",
        _ => "끝까지 버티면 기적",
    };

    public static string PersonaNote(StoryPersona p) => p switch
    {
        StoryPersona.Steady => "고르게 — 거의 추스르면 다음",
        StoryPersona.Burst => "오래 조용하다 한꺼번에",
        StoryPersona.Random => "아무 때나 · 무엇이든 — 예측 불가",
        StoryPersona.Tester => "배의 급소를 노린다",
        StoryPersona.SlowBurn => "작게 시작해 점점 잦고 크게",
        StoryPersona.Seasonal => "철마다 한 갈래를 몰아서",
        StoryPersona.Merciful => "다 추스르고 막을 물자가 있을 때만",
        StoryPersona.Vengeful => "잘 버틴 만큼 되갚는다",
        StoryPersona.Chain => "한 사고가 다음을 부른다 — 같은 방 · 이어지는 갈래로",
        StoryPersona.Siege => "배 바깥 위협만 — 운석 · 잔해 · 폭풍 · 선체",
        StoryPersona.Implosion => "사람 · 설비 문제만 — 고장 · 병 · 실수",
        StoryPersona.Marathon => "작은 것만 끝없이",
        StoryPersona.Climax => "목적지에 다가갈수록 잦고 크게 — 직전에 몰아친다",
        StoryPersona.Wave => "잠잠해진 뒤 한 무리 — 물결마다 더 크게",
        _ => "사고는 무작위 사고 주기대로만",
    };

    /// <summary>성격의 타고난 리듬 (잦기 · 크기 · 몰아치기) — 화면에 보여 준다.</summary>
    public static string PersonaRhythm(StoryPersona p) => p switch
    {
        StoryPersona.Steady => "보통 · 섞어 · 없음",
        StoryPersona.Burst => "드묾 · 섞어 · 심함",
        StoryPersona.Random => "들쭉날쭉 · 아무것이나 · 가끔",
        StoryPersona.Tester => "보통 · 급소 · 없음",
        StoryPersona.SlowBurn => "드묾→잦음 · 작게→크게 · 없음",
        StoryPersona.Seasonal => "철 가운데 잦음 · 섞어 · 철마다",
        StoryPersona.Merciful => "드묾 · 작게 · 없음",
        StoryPersona.Vengeful => "잘 버틸수록 잦음 · 크게 · 없음",
        StoryPersona.Chain => "드묾 · 섞어 · 번질 때 잦음",
        StoryPersona.Siege => "보통 · 바깥 · 없음",
        StoryPersona.Implosion => "보통 · 안쪽 · 없음",
        StoryPersona.Marathon => "아주 잦음 · 아주 작게 · 없음",
        StoryPersona.Climax => "앞은 드묾 → 끝은 몰아침 · 끝으로 갈수록 크게",
        StoryPersona.Wave => "물결 · 물결마다 크게 · 무리로",
        _ => "",
    };

    // ─────────────────────────────── 혹독 · 지옥 · 절멸 ───────────────────────────────

    /// <summary>기다릴지 볼 때 쓰는 긴장 (혹독 이상은 배가 무너지는 중에도 덜 기다린다 — 보통 이하에선 그대로).</summary>
    private static float Overlap(float t) => Level switch { 6 => MathF.Max(0f, t - 0.45f), 7 => MathF.Max(0f, t - 0.9f), 8 => MathF.Max(0f, t - 1.6f), _ => t };

    /// <summary>방금 낸 사고 곧바로 하나 더 올 몫 (혹독 이상 + 리듬 몰아치기).</summary>
    private float FollowChance(StoryPersona p)
    {
        float c = Level switch { 6 => 0.2f, 7 => 0.35f, 8 => 0.5f, _ => 0f } + BurstAdds[Burstiness + 2];
        if (p == StoryPersona.Climax && ClimaxPhase() > 0.85f) c += 0.45f * BurstMul; // 목적지 직전에 몰아친다
        return MathF.Min(0.8f, c);
    }

    /// <summary>규모 비율 (혹독 이상은 배 전체급 몫이 크다 — 가혹 이하는 null = 원래 비율).</summary>
    public static float[]? ScaleTarget => Level switch
    {
        6 => new[] { 0.05f, 0.28f, 0.37f, 0.30f, 0f },
        7 => new[] { 0.04f, 0.24f, 0.36f, 0.36f, 0f },
        8 => new[] { 0.03f, 0.20f, 0.35f, 0.42f, 0f },
        _ => null,
    };

    /// <summary>큰 사고 바로 뒤 배 전체급 무게 (원래 0.05 — 혹독 이상은 겹친다).</summary>
    public static float AfterBigMul => Level switch { 6 => 0.15f, 7 => 0.3f, 8 => 0.5f, _ => 0.05f };

    /// <summary>항해마다 더 오는 우주급 재난 (혹독 +1 · 지옥 +2 · 절멸 +3) · 세기 배율.</summary>
    public static int CosmicExtra => Persona == StoryPersona.Off ? 0 : Math.Max(0, Level - 5);
    public static float CosmicPower => Persona == StoryPersona.Off || Level <= 5 ? 1f : 1f + 0.12f * (Level - 5);

    // ─────────────────────────────── 새 성격의 상태 ───────────────────────────────

    /// <summary>연쇄: 이번 사슬에서 번진 횟수 · 사슬이 시작된 방.</summary>
    public int ChainLen { get; internal set; }
    public int ChainRoom { get; private set; } = -1;
    /// <summary>파도: 지금까지 온 물결 수 · 이번 물결에 남은 사고.</summary>
    public int Waves { get; internal set; }
    public int WaveLeft { get; private set; }
    /// <summary>곧이어 하나 더 (겹침 · 몰아치기)가 연달아 온 수.</summary>
    public int Follows { get; private set; }
    /// <summary>겹쳐 온 사고 (앞 사고를 수습하는 중에 — 긴장 0.3 넘을 때) 수.</summary>
    public int Overlaps { get; private set; }

    /// <summary>시험용: 항해를 이만큼 온 셈 친다.</summary>
    internal float? PhaseForTest;

    /// <summary>절정형: 이번 항해를 얼마나 왔나 0~1.</summary>
    public float ClimaxPhase()
    {
        if (PhaseForTest is float f) return f;
        var v = _w.Voyage;
        float total = v.TotalDays;
        return total <= 0f ? 0f : Math.Clamp(v.DoneDays / total, 0f, 1f);
    }

    private static int Consequence(int fam) => fam switch { FamSpace => FamBurn, FamBurn => FamMachine, FamMachine => FamBurn, _ => FamLife };

    private bool HoldV21(StoryPersona p, float t, float cap) => p switch
    {
        StoryPersona.Chain => ChainLen > 0 ? t > 1.2f : t > 0.3f,                    // 번지는 중엔 기다리지 않는다 · 새 사슬은 추스른 뒤
        StoryPersona.Siege => t > 0.45f,
        StoryPersona.Implosion => t > 0.35f,
        StoryPersona.Marathon => t > 0.6f,                                           // 작은 것뿐이라 웬만해선 기다리지 않는다
        StoryPersona.Climax => t > (ClimaxPhase() > 0.85f ? 0.95f : 0.25f),
        StoryPersona.Wave => WaveLeft > 0 ? t > 1.1f : t > 0.12f,                    // 물결 사이는 완전히 잠잠해질 때까지
        _ => false,
    };

    private float GapMulV21(StoryPersona p, float cap)
    {
        switch (p)
        {
            case StoryPersona.Chain: return 1.6f;                                   // 사슬이 끝나면 길게 쉰다
            case StoryPersona.Siege: return 0.9f;
            case StoryPersona.Implosion: return 1f;
            case StoryPersona.Marathon: return 0.4f;
            case StoryPersona.Climax:
            {
                float f = ClimaxPhase();
                return f < 0.6f ? 2.2f - 1.2f * f / 0.6f : f < 0.85f ? 1.0f - 1.6f * (f - 0.6f) : 0.35f;
            }
            case StoryPersona.Wave: return 2.6f + 0.3f * Math.Min(Waves, 5);          // 물결 사이 긴 잠잠함
            default: return 1f;
        }
    }

    private void ShapeV21(StoryPersona p, List<(string key, float weight)> pool)
    {
        int last = LastKey.Length > 0 ? Family(LastKey) : -1;
        float f = p == StoryPersona.Climax ? ClimaxPhase() : 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            var (key, wt) = pool[i];
            int fam = Family(key);
            bool big = Big(key);
            var scale = ScaleTable.OfKey(key);
            wt *= p switch
            {
                // 번지는 중: 이어지는 갈래 · 같은 갈래 / 새 사슬은 고르게
                StoryPersona.Chain => ChainLen > 0 && last >= 0 ? (fam == Consequence(last) ? 4f : fam == last ? 1.6f : 0.3f) * (big ? 0.5f : 1f) : 1f,
                StoryPersona.Siege => fam == FamSpace ? 6f : 0.06f,
                StoryPersona.Implosion => fam is FamMachine or FamLife ? 3f : fam == FamBurn ? 0.8f : 0.05f,
                StoryPersona.Marathon => big || scale >= IncidentScale.Ship ? 0.03f : scale <= IncidentScale.Room ? 2.5f : 0.7f,
                StoryPersona.Climax => big || scale >= IncidentScale.Ship ? 0.1f + 4f * f * f * f : 1f,
                StoryPersona.Wave => big || scale >= IncidentScale.Ship ? 0.3f + 0.6f * Math.Min(Waves, 6) : 1f,
                _ => 1f,
            };
            pool[i] = (key, wt);
        }
    }

    /// <summary>리듬 크기: 계통 이상 무게를 키우거나 줄인다 (가운데면 손대지 않는다).</summary>
    private static void SizeShape(List<(string key, float weight)> pool)
    {
        int s = Size;
        if (s == 0) return;
        float big = BigMuls[s + 2], small = SmallMuls[s + 2];
        for (int i = 0; i < pool.Count; i++)
        {
            var sc = ScaleTable.OfKey(pool[i].key);
            pool[i] = (pool[i].key, pool[i].weight * (sc >= IncidentScale.System ? big : sc <= IncidentScale.Room ? small : 1f));
        }
    }

    private Room? ChainAim()
    {
        if (ChainLen == 0 || ChainRoom < 0 || ChainRoom >= _w.Ship.Rooms.Count) return null;
        var r = _w.Ship.Rooms[ChainRoom];
        return r.Detached || r.Abandoned ? null : r;
    }

    /// <summary>방금 낸 사고가 난 방 (사슬의 다음 고리가 노린다).</summary>
    private int HitRoom()
    {
        var nodes = _w.Causes.Nodes;
        for (int i = nodes.Count - 1; i >= 0 && nodes[i].Tick >= _w.Tick; i--)
            if (nodes[i].RoomId >= 0) return nodes[i].RoomId;
        return -1;
    }

    /// <summary>
    /// 사고를 낸 뒤 다음을 정한다: 새 성격의 흐름 (연쇄 · 파도) · 겹침 · 몰아치기. 정했으면 true (아니면 원래 간격대로).
    /// 원래 성격 · 보통 이하 · 리듬 가운데면 난수를 쓰지 않고 false — 예전 흐름과 같다.
    /// </summary>
    private bool NextV21(StoryPersona p, float t, float gap, float cap)
    {
        var w = _w;
        if (p == StoryPersona.Chain)
        {
            if (ChainLen == 0) ChainRoom = HitRoom();
            int max = 1 + (Level + 1) / 3;                                           // 보통 2번 · 절멸 4번 번진다
            float spread = MathF.Min(0.9f, (0.55f + 0.05f * (Level - 3)) * BurstMul);
            if (ChainLen < max && _rng.Chance(spread))
            {
                ChainLen++;
                Next = w.Tick + SimTime.Minutes(_rng.Range(20f, 90f));
                return true;
            }
            ChainLen = 0;
            ChainRoom = -1;
        }
        else if (p == StoryPersona.Wave)
        {
            if (WaveLeft == 0)
            {
                Waves++;
                WaveLeft = Math.Max(0, (int)MathF.Round((1 + Math.Min(3, Waves) + _rng.Range(0, 2)) * BurstMul)); // 물결마다 한 무리 (몇 번째 물결일수록 많이)
            }
            if (WaveLeft > 0) WaveLeft--;
            if (WaveLeft > 0)
            {
                Next = w.Tick + SimTime.Minutes(_rng.Range(30f, 120f));
                return true;
            }
        }
        float follow = p is StoryPersona.Burst ? 0f : FollowChance(p);
        if (follow > 0f && Follows < (Level >= 7 ? 3 : 2) && _rng.Chance(follow))
        {
            Follows++;
            Next = w.Tick + SimTime.Minutes(_rng.Range(10f, 60f));
            return true;
        }
        Follows = 0;
        return false;
    }

    private string WhyV21(StoryPersona p, float t, float cap) => p switch
    {
        StoryPersona.Chain => ChainLen > 0 ? $"앞 사고에서 번졌다 ({ChainLen}번째 · 긴장 {t:0.00})" : $"추스른 배에 불씨 하나 (여력 {cap * 100:0}%)",
        StoryPersona.Siege => $"배 바깥이 조여 온다 (긴장 {t:0.00})",
        StoryPersona.Implosion => $"안에서 무너진다 — 사람 · 설비 (여력 {cap * 100:0}%)",
        StoryPersona.Marathon => $"작게, 끝없이 (긴장 {t:0.00})",
        StoryPersona.Climax => $"{_w.Voyage.Destination}까지 {MathF.Max(0f, _w.Voyage.TotalDays - _w.Voyage.DoneDays):0.0}일 — 항해의 {ClimaxPhase() * 100:0}%",
        StoryPersona.Wave => WaveLeft > 0 ? $"{Waves}번째 물결이 밀려든다" : $"잠잠해졌다 — {Waves + 1}번째 물결 (여력 {cap * 100:0}%)",
        _ => "",
    };

    /// <summary>시험용: 사고를 낸 것처럼 흐름을 한 걸음 (난수는 이야기꾼 것).</summary>
    internal bool StepFlow(StoryPersona p, float t) => NextV21(p, t, GapDays, Capacity());
    /// <summary>시험용: 간격 (틱)을 하나 뽑아 본다.</summary>
    internal long SampleGap(StoryPersona p) => GapTicks(GapDays * GapMul(p, Capacity()), p);
}

public sealed partial class HazardSystem
{
    /// <summary>v21 무작위 사고 크기 범위: 0 작은 것만 (개인 · 방) · 1 섞어 · 2 큰 것만 (계통 · 배 전체).</summary>
    public static float SizeRangeValue = 1f;
    public static int SizeRange => Math.Clamp((int)MathF.Round(SizeRangeValue), 0, 2);
    public static readonly string[] SizeRangeNames = { "작은 것만", "섞어", "큰 것만" };

    /// <summary>v21 무작위 사고 주기 10단계 (일 · 0 끔).</summary>
    public static readonly (float days, string name)[] RandomSteps =
    {
        (0f, "끔"), (14f, "2주"), (7f, "1주"), (6f, "6일"), (3f, "3일"), (1.5f, "하루 반"), (1f, "하루"), (0.5f, "반나절"), (1f / 6f, "4시간"), (1f / 12f, "2시간"),
    };

    public static int RandomStep()
    {
        int best = 0;
        for (int i = 1; i < RandomSteps.Length; i++)
            if (MathF.Abs(RandomSteps[i].days - RandomDays) < MathF.Abs(RandomSteps[best].days - RandomDays)) best = i;
        return best;
    }

    /// <summary>크기 범위에 맞지 않는 사고를 뺀다 (섞어면 그대로).</summary>
    internal static void SizeFilter(List<(string key, float weight)> pool)
    {
        int s = SizeRange;
        if (s == 1) return;
        for (int i = 0; i < pool.Count; i++)
        {
            var sc = ScaleTable.OfKey(pool[i].key);
            bool keep = s == 0 ? sc <= IncidentScale.Room && !Storyteller.Big(pool[i].key) : sc >= IncidentScale.System || Storyteller.Big(pool[i].key);
            if (!keep) pool[i] = (pool[i].key, 0f);
        }
    }
}
