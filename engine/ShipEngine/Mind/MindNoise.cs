namespace ShipEngine;

/// <summary>
/// ⑧ 마음 — 생활 소리 (소리 단계 S2 · docs/SOUND.md 결정 8 · 13 '나눔'): 판단이 정한 행동이 소리를 낸다 — 발소리 · 식기 · 공구 · 망치 · 용접 · 흥얼거림 · 잠꼬대.
/// 소리를 내는 것은 행동이고 · 퍼지고 줄어드는 것은 ③ 소리 장(월드)이 셈한다. 자는 이의 귀는 잠의 깊이만큼 둔하다 (깨우는 문턱).
/// </summary>
public sealed partial class Mind
{
    /// <summary>깨우는 문턱 (dB — 귀에 닿은 크기): 얕은 잠 50 · 깊은 잠 65 (각성 문턱 — 1 · 2단계 잠 40~55dBA · 깊은 잠 60~70dBA) ·
    /// 경보 · 비명은 잠을 깨우게 생긴 소리라 8dB 더 잘 깨운다 (NFPA 72 — 잠자리 경보 75dBA면 어른 대부분이 깬다).</summary>
    public const float WakeLight = 50, WakeDeep = 65, AlarmEdge = 8;

    /// <summary>
    /// 잠의 깊이 (0 얕다 ~ 1 깊다): 잠의 압력(남은 졸림 — 잠든 처음 몇 시간이 깊고 아침이 다가올수록 얕다) ·
    /// 잠을 설친 만큼 얕고 (소리 · 빛 · 긴장 — ⑦ 잠의 질) · 수면제는 깊게 한다.
    /// </summary>
    public float SleepDepth(Agent a)
    {
        if (!a.IsPerson) return 0;
        ref var c = ref a.C;
        double d = Math.Clamp(c.Sleepy / 0.6, 0, 1) * (1 - 0.5 * c.SleepLoss) + 0.3 * Life.SleepAid(_life![c.Body]);
        return (float)Math.Clamp(d, 0, 1);
    }

    /// <summary>이 사람을 깨우는 소리의 크기 (귀에서 dB).</summary>
    public float WakeDb(Agent a) => WakeLight + (WakeDeep - WakeLight) * SleepDepth(a);

    /// <summary>이 소리가 깨우는 문턱 (경보 · 비명은 더 낮다).</summary>
    public float WakeDb(Agent a, SoundKind kind) => WakeDb(a) - (kind is SoundKind.Alarm or SoundKind.Scream ? AlarmEdge : 0);

    /// <summary>
    /// 둘레(12칸)에 자는 사람이 있나 — 발소리는 자는 이의 잠자리에만 닿을 일이 있다 (깨어 있는 이의 귀는 발소리로 아무것도 알지 않는다).
    /// 아무도 자지 않으면 발소리 퍼짐을 셈하지 않는다 (들을 이가 없어 결과가 같다 — 계산 줄이기).
    /// </summary>
    private bool SleeperNear(int cell, int reach = 12)
    {
        foreach (var o in Agents)
            if (o.IsPerson && IsSleeping(o) && Dist(o.C.Cell, cell) <= reach) return true;
        return false;
    }

    /// <summary>
    /// 발소리 (걸은 틱마다 — 1m에서): 걷기 55dB · 뛰기 65 · 짐을 지고 58 · 기어감 45 · 우주복 장화 +3 · 로봇 58 (자석 발로 짚는 무중력에서도 60) ·
    /// 옅은 중력에서는 발을 디디는 힘만큼 작다 (10·log g) · 떠다니는 이 · 드론은 발소리가 없다.
    /// </summary>
    private void StepNoise(Sim sim, Agent a)
    {
        if (_snd == null) return;
        ref var c = ref a.C;
        if (c.Kind == AgentKind.Drone || c.Cell < 0 || c.Cell >= L.N) return;
        double g = F.Grav[c.Cell];
        double db;
        if (c.Kind == AgentKind.Robot) db = g < FloatG ? 60 : 58;
        else
        {
            if (g < FloatG) return;
            ref var b = ref _life![c.Body];
            db = b.Low != 0 ? 45 : c.Mode == 1 || c.Goal == GoalKind.Evacuate ? 65 : c.Carrying >= 0 ? 58 : 55;
            if (b.Suit != 0) db += 3;
        }
        if (g >= FloatG && g < 1) db += 10 * Math.Log10(Math.Max(0.05, g));
        if (!SleeperNear(c.Cell)) return;
        StepsHeard++;
        _snd.Emit(sim, c.Cell, (float)db, SoundKind.Step, 0, a.Id);
    }

    /// <summary>
    /// 일하는 소리 (③에 요청 — 일하는 동안 2틱마다): 손이 하는 일이 그대로 소리가 된다 — 렌치 · 드라이버 · 망치 · 용접 · 식기 · 냄비.
    /// 크기 (1m에서): 기계 부품 렌치 72 · 볼트 조이기 72 · 전기 부품 · 배선 58 · 거르개 60 · 봉합(용접) 78 · 덧대 받치기 · 다시 짓기 망치 85 ·
    /// 만들기(작업대) 망치 80 · 먹기 식기 50 · 마시기 컵 45 · 요리 냄비 58 (NIOSH 공구 소음 · 생활 소음 측정값에서 어림).
    /// </summary>
    private void WorkNoise(Sim sim, Agent a, SoundKind kind, float db, int every = 2)
    {
        if (_snd == null || (sim.Tick - a.C.StepStart + a.Id) % every != 0) return;
        WorkSounds++;
        _snd.Emit(sim, a.C.Cell, db, kind, a.C.GoalCause, a.Id);
    }

    /// <summary>고치는 부품 갈래마다 연장 · 크기.</summary>
    private static (SoundKind kind, float db) FixNoise(int part) => (Part)part switch
    {
        Part.Mechanical => (SoundKind.Tool, 72),
        Part.Seal => (SoundKind.Weld, 74),
        Part.Filter => (SoundKind.Tool, 60),
        _ => (SoundKind.Tool, 58),
    };

    /// <summary>
    /// 흥얼거림 (1분마다 · 깨어 있고 혼자 일하거나 쉬는 동안): 기분이 좋고(기쁨 0.5 넘게) 긴장 · 겁이 덜할 때 —
    /// 흥얼거리는 버릇이 있는 이(사람마다 — 외향 · 열림)는 1분에 0.3번 남짓 (48dB). 곁(3칸)에 누가 있으면 하지 않는다 (말을 건다).
    /// </summary>
    private void HumRoll(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        if (_snd == null || c.Mode != 0 || c.Joy < 0.5f || c.Stress > 0.3f || c.Fear > 0.2f || c.Goal is GoalKind.Socialize or GoalKind.Sleep) return;
        if (Rng.Unit(sim.Seed, Rng.Key("흥얼 버릇"), 0, a.Id) > 0.25 + 0.4 * (c.Extra + c.Open) * 0.5) return;
        if (Rng.Unit(sim.Seed, Rng.Key("흥얼"), sim.Tick, a.Id) > 0.15 + 0.3 * (c.Joy - 0.5)) return;
        foreach (var o in Agents) if (o != a && o.IsPerson && o.C.Up != 0 && Dist(o.C.Cell, c.Cell) <= 3) return;
        Hums++;
        _snd.Emit(sim, c.Cell, 48, SoundKind.Hum, 0, a.Id);
    }

    /// <summary>
    /// 잠꼬대 (자는 1분마다): 얕은 잠(깊이 0.35 아래 — 아침 가까이 · 꿈꾸는 잠)에서 긴장 · 불안 · 외상만큼 —
    /// 1분에 0.004 × (0.3 + 긴장 + 불안의 반 + 외상의 반) · 웅얼거린다 (50dB · 곁에서 얕게 자던 이가 깰 수도 있다).
    /// </summary>
    private void SleepTalkRoll(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        if (_snd == null || SleepDepth(a) >= 0.35f) return;
        double load = 0.3 + c.Stress + 0.5 * c.Anxiety + 0.5 * c.Trauma;
        if (Rng.Unit(sim.Seed, Rng.Key("잠꼬대"), sim.Tick, a.Id) >= 0.004 * load) return;
        SleepTalks++;
        _snd.Emit(sim, c.Cell, 50, SoundKind.SleepTalk, 0, a.Id);
    }

    /// <summary>
    /// 소리 난 쪽으로 고개를 돌린다 (소리 단계 S4 — 감각이 소리 난 쪽을 알고 · 판단이 주의를 돌린다 · 그림이 보여 준다):
    /// 깨어 있는 사람이 제 칸이 아닌 곳에서 귀에 35dB 넘게 닿은 소리를 들으면 2틱(5초) 그 쪽을 본다 —
    /// 쾅 · 비명 · 경보 · 금 가는 소리는 4틱(10초)이고 · 보던 것보다 약한 소리는 고개를 다시 돌리지 않는다.
    /// </summary>
    private void Glance(Agent a, in Heard h, float heardDb, long t)
    {
        ref var c = ref a.C;
        if (h.Source == c.Cell || heardDb < 35 || h.Kind is SoundKind.Snore or SoundKind.Step) return;
        bool strong = h.Kind is SoundKind.Bang or SoundKind.Scream or SoundKind.Alarm or SoundKind.Crack or SoundKind.Creak;
        if (t <= c.LookUntil && !strong) return;
        c.LookAt = h.Source; c.LookUntil = t + (strong ? 4 : 2);
        Glances++;
    }

    /// <summary>확인용 셈: 낸 발소리 · 일하는 소리 · 흥얼거림 · 잠꼬대 (찍지 않는다).</summary>
    public long StepsHeard, WorkSounds, Hums, SleepTalks, Glances;
}
