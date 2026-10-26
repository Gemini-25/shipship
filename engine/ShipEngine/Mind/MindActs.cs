using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>판단 엔진이 한 사회적 행동의 갈래 (월드 엔진 ⑨가 같은 틱에 그 결과를 계산한다).</summary>
public enum SocialActKind : byte { None, CallMeeting, Speak, CallOff, DeferMemorial, Talk, KeepQuiet, Nickname, TakeSide, Mediate, Attack, Strike, HitBack, StopFight, BreakUp, Sabotage, Confess, KeepSecret, TellLie, HearLie, AskLog, Conceal, LearnDeath }

/// <summary>
/// 사람 하나가 한 사회적 행동 — 부르기 · 말하기 · 미루기. 마음(판단 엔진)이 정해 적고 · 사회(월드 엔진)는 읽어서 결과만 계산한다
/// (원칙: docs/ENGINE.md §3 9 '월드 엔진과 판단 엔진을 나눈다').
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SocialAct
{
    public long Tick;
    public SocialActKind Kind;
    public CouncilKind Meet;   // 어떤 모임 (부르기 · 미루기)
    public int Actor;          // 한 이
    public int Target;         // 모임의 그 사람 (피고 · 숨진 이 · −1)
    public int Other;          // 고발한 이 (재판) · 이야기의 그 사람 (대화) · −1
    public int Arg;            // 규칙 번호 · 축하의 주인공 · 고른 후보 (선거의 말) · 별명 · 끝장 (덤빔) · 장치 (몰래 방해) · 편든 쪽
    public int Room, Cell;     // 모일 곳
    public float Value;        // 의견 (−1 반대 ~ 1 찬성)
    public Topic Topic;        // 이야기 (대화)
    public TalkEnd End;        // 듣는 이가 받은 끝 (대화)
    public int Cause;          // 그 행동의 원인 마디 (부른 말 · 한 마디 · 미룬 까닭 · 꺼낸 이야기의 기억)
}

public sealed partial class Mind
{
    /// <summary>최근 세 틱에 한 사회적 행동 (사회는 그 틱에 읽었다 — 곁사람의 반응 · 다툰 뒤의 덤빔이 두 틱 뒤까지 본다).</summary>
    public readonly List<SocialAct> Acts = new();
    /// <summary>비운 행동의 수 — 사회는 ActBase + 번호로 어디까지 읽었는지 센다.</summary>
    public long ActBase;
    public long ActsTotal => ActBase + Acts.Count;

    /// <summary>cursor 뒤의 행동 (사회가 읽는다).</summary>
    public IEnumerable<SocialAct> ActsSince(long cursor)
    {
        for (long k = Math.Max(cursor, ActBase); k < ActsTotal; k++) yield return Acts[(int)(k - ActBase)];
    }

    private void Act(Sim sim, SocialAct act) { act.Tick = sim.Tick; Acts.Add(act); }

    /// <summary>두 틱보다 오랜 행동을 비운다 (사회가 그 틱에 읽었다).</summary>
    private void ClearOldActs(long now)
    {
        int n = 0;
        while (n < Acts.Count && Acts[n].Tick < now - 2) n++;
        if (n == 0) return;
        Acts.RemoveRange(0, n);
        ActBase += n;
    }

    private void SaveActs(StateWriter w)
    {
        w.I64(ActBase);
        w.Arr(Acts.ToArray());
    }
    private void LoadActs(StateReader r)
    {
        ActBase = r.I64();
        Acts.Clear(); Acts.AddRange(r.Arr<SocialAct>());
    }
}
