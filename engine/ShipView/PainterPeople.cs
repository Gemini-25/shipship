using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 사람 그림을 세계에 잇는다 (그래픽 결정 6 · 9): 다친 자국은 ⑦ 몸의 값 (피 · 화상 · 타박 · 동상 · 열 · 감염 · 방사선 · 덮어 둔 상처) ·
/// 표정은 ⑧ 마음(겁 · 기쁨 · 슬픔)과 ⑦ 통증 · 기어가기는 ③ 국소 현상의 '낮은 머리'(깨어서 연기 · 열 아래로 낮춘 몸) ·
/// 나이 · 수염 · 옷 무늬는 사람마다 (이름의 씨앗 — 세계가 아직 나이를 갖지 않아 겉모습만).
/// 한 번 그린 사람 그림은 (사람 · 자세 · 방향 · 장 · 상태)로 다시 쓴다.
/// </summary>
public sealed partial class Painter
{
    /// <summary>몸 → 보이는 상처 (문턱은 그림에 드러날 만큼).</summary>
    public static DotHurt HurtOf(in Body b)
    {
        var h = DotHurt.None;
        if (b.Bleed > 0.05f) h |= DotHurt.Bleed;
        if (b.Burn > 0.1f) h |= DotHurt.Burn;
        if (b.Blunt > 0.2f) h |= DotHurt.Bruise;
        if (b.Blunt > 0.5f && b.Dressed > 0) h |= DotHurt.HeadWrap;
        if (b.Dressed > 0 && (b.Burn > 0.1f || b.Bleed > 0 || b.Blunt > 0.3f)) h |= DotHurt.TorsoWrap;
        if (b.Frost > 0.1f) h |= DotHurt.Frost;
        if (b.Ill == 2 || b.Infect > 0.3f || b.CoreK > 311) h |= DotHurt.Sick;
        else if (b.CoreK > 310) h |= DotHurt.Sweat;
        if (b.RadGy > 2) h |= DotHurt.RadHair;
        return h;
    }

    /// <summary>마음 · 몸 → 표정: 아픔이 크면 아픔 · 겁(또는 도망 · 공황)이면 겁 · 슬픔 · 기쁨 (스트레스가 낮을 때) · 아니면 덤덤.</summary>
    public static DotFace FaceOf(in AgentCore c, in Body b)
    {
        if (b.Pain > 0.45f) return DotFace.Pain;
        if (c.Fear > 0.5f || c.Mode is 1 or 2) return DotFace.Fear;
        if (c.Sad > 0.55f) return DotFace.Sad;
        if (c.Joy > 0.5f && c.Stress < 0.35f) return DotFace.Smile;
        return DotFace.Calm;
    }

    /// <summary>하는 일 → 생활 동작 (그래픽 결정 10): 먹고 마시면 손이 입으로 · 고치면 공구를 돌리고 · 말하면 손짓 · 앉아서 졸리면 고개를 떨군다.</summary>
    public static DotMotion MotionOf(in AgentCore c, StepKind step, DotPose pose) =>
        step == StepKind.Use && c.Goal is GoalKind.Eat or GoalKind.Drink ? DotMotion.Drink
        : pose == DotPose.Work && step is StepKind.Fix or StepKind.Maintain or StepKind.Craft ? DotMotion.Tool
        : step == StepKind.Use && c.Goal == GoalKind.Socialize ? DotMotion.Talk
        : pose == DotPose.Sit && c.Sleepy > 0.85f ? DotMotion.Doze
        : DotMotion.None;

    /// <summary>나이 · 수염 · 옷 무늬 (사람마다 — 씨앗).</summary>
    private static void Diversify(PersonLook l, uint u)
    {
        int age = (int)(u >> 24) % 20;
        l.Age = age < 11 ? DotAge.Adult : age < 18 ? DotAge.Middle : DotAge.Elder;
        if (l.Age == DotAge.Elder) { l.HairRamp = (u >> 3) % 2 == 0 ? Ramps.HairGrey : l.HairRamp; }
        if (!l.Female) { l.Beard = (u >> 27) % 7 == 0; l.Mustache = !l.Beard && (u >> 29) % 6 == 0; }
        l.Pattern = (int)((u >> 13) % 9) switch { 0 => 1, 1 => 2, 2 => 3, 3 => 4, 4 => 5, _ => 0 };
    }

    private readonly Dictionary<(int id, long key), Img> _people = new();

    /// <summary>사람 그림 (같은 사람 · 자세 · 방향 · 장 · 상태면 다시 쓴다).</summary>
    private Img PersonImg(int id, PersonLook l, DotFacing face, DotPose pose, int frame, bool lie, bool crawl)
    {
        long key = (long)face | (long)pose << 2 | (long)(frame & 7) << 6 | (long)l.Held << 9 | (long)l.Face << 13 | (l.Suit ? 1L : 0) << 16 | (l.Dead ? 1L : 0) << 17
                   | (l.EyesClosed ? 1L : 0) << 18 | (lie ? 1L : 0) << 19 | (crawl ? 1L : 0) << 20 | (long)l.Hurt << 21 | (long)l.Motion << 46 | (l.Cycle8 ? 1L : 0) << 49;
        if (_people.TryGetValue((id, key), out var img)) return img;
        if (_people.Count > 6000) _people.Clear();
        var dot = DotPeople.Draw(l, lie || crawl ? DotFacing.S : face, pose, frame);   // 기는 사람: 엎드린 걷기 (팔다리가 번갈아)
        if (lie) dot = dot.Rotate(cw: false);                         // 누운 사람: 옆으로 (머리가 왼쪽)
        else if (crawl) dot = dot.Rotate(cw: face == DotFacing.E);    // 기는 사람: 엎드려 가는 쪽으로 머리
        img = dot.ToImg();
        _people[(id, key)] = img;
        return img;
    }
}
