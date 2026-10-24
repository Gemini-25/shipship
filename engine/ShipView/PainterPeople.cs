using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 사람 그림을 세계에 잇는다 (그래픽 결정 6 · 9): 다친 자국은 ⑦ 몸의 값 (피 · 화상 · 타박 · 동상 · 열 · 감염 · 방사선 · 덮어 둔 상처) ·
/// 표정은 ⑧ 마음(겁 · 기쁨 · 슬픔)과 ⑦ 통증 · 기어가기는 ③ 국소 현상의 '낮은 머리'(깨어서 연기 · 열 아래로 낮춘 몸) ·
/// 나이 · 수염 · 옷 무늬는 사람마다 (이름의 씨앗 — 세계가 아직 나이를 갖지 않아 겉모습만) ·
/// 옷의 얼룩은 ⑦ 몸의 더러움 (Body.Dirt 0.35 · 0.6 · 0.85에 갈색 점 6 · 14 · 24) · 지린 옷은 Body.Wet (다리 윗부분이 젖어 어둡다 — 5단 A3 · 우주복이면 덮는다 · 씻으면 다음 장에 사라진다).
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

    /// <summary>몸 → 옷의 얼룩 (0~7 — 그림 열쇠에 든다): 더러움 3단 (0.35 · 0.6 · 0.85 — 씻으러 가는 문턱이 0.35) · 지림 (4) · 우주복이면 0 (옷을 덮는다).</summary>
    public static int StainOf(in Body b) =>
        b.Suit != 0 ? 0 : (b.Dirt >= 0.85f ? 3 : b.Dirt >= 0.6f ? 2 : b.Dirt >= 0.35f ? 1 : 0) | (b.Wet != 0 ? 4 : 0);

    /// <summary>얼룩 색표 (갈색 회색 셋 — ShipView 안에서 더한다 · 옛 게임과 함께 쓰는 DotPeople.cs는 그대로).</summary>
    private static readonly int StainRamp = Ramps.Add("#2a1d12", "#4a3420", "#6b4e30");
    private static readonly int[] StainDots = { 0, 6, 14, 24 };

    /// <summary>
    /// 옷의 얼룩을 다 그린 사람 도트에 덧칠한다 (돌리기 전 — 누운 · 기는 그림도 같은 칠): 옷 칸은 부위 2~9 (다리 · 몸통 · 팔)의 옷 색표 칸 (Cloth · Accent · ClothNavy — 바지도 이 가운데).
    /// 머리 · 얼굴의 Accent (머리끈 · 볼 홍조 — 부위 20 넘게) · 신발 · 고무 · 살은 들지 않는다.
    /// 더러움: 옷 칸을 사람마다 해시 차례로 6 · 14 · 24개 골라 얼룩 색으로 (늘 같은 자리 — 옷 맨 윗줄에서 잰 자리라 걸어도 덜 튄다).
    /// 지림: 몸통 아래 (부위 6 밑 — 다리 · 신발 쪽)의 옷 칸 가운데 맨 윗 5줄을 한 단 어둡게 (옆 그림은 부위 3이 신발이라 번호가 아니라 색표와 줄로 고른다).
    /// </summary>
    private static void Stain(Dot d, PersonLook l, int stain)
    {
        if (stain == 0 || l.Suit) return;
        bool Cloth(int r) => r == l.Cloth || r == l.Accent || r == Ramps.ClothNavy;
        int top = int.MaxValue, legTop = int.MaxValue;
        for (int y = 0; y < d.H; y++)
            for (int x = 0; x < d.W; x++)
            {
                int part = d.PartAt(x, y);
                if (part is < 2 or > 9 || !d.Has(x, y) || !Cloth(d.RampAt(x, y))) continue;
                top = Math.Min(top, y);
                if (part < 6) legTop = Math.Min(legTop, y);
            }
        if (top == int.MaxValue) return;
        if ((stain & 4) != 0 && legTop != int.MaxValue)
            for (int y = legTop; y < legTop + 5 && y < d.H; y++)
                for (int x = 0; x < d.W; x++)
                    if (d.PartAt(x, y) is >= 2 and < 6 && d.Has(x, y) && Cloth(d.RampAt(x, y))) d.Tone(x, y, -1);
        int n = StainDots[stain & 3];
        if (n == 0) return;
        // 옷 칸마다 해시 (사람 · 옷 맨 윗줄에서 잰 자리) — 작은 것부터 n개
        Span<(uint h, int x, int y)> best = stackalloc (uint, int, int)[24];
        int have = 0;
        for (int y = top; y < d.H; y++)
            for (int x = 0; x < d.W; x++)
            {
                int part = d.PartAt(x, y);
                if (part is < 2 or > 9 || !d.Has(x, y) || !Cloth(d.RampAt(x, y))) continue;
                uint h = DotHash.U(l.Seed, x * 64 + (y - top), 89);
                if (have == n && h >= best[n - 1].h) continue;
                int i = have < n ? have++ : n - 1;
                while (i > 0 && best[i - 1].h > h) { best[i] = best[i - 1]; i--; }
                best[i] = (h, x, y);
            }
        for (int i = 0; i < have; i++) d.Mark(best[i].x, best[i].y, StainRamp, (int)(best[i].h >> 28) % 3);
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

    /// <summary>얼굴 칸 크기 (UI U2 사람 줄 — 서서 앞을 보는 그림의 머리 · 어깨).</summary>
    public const int FaceW = DotPeople.FW, FaceH = 20, FaceTop = 6;

    /// <summary>
    /// 얼굴 (UI U2 사람 줄): 서서 앞을 보는 그림에서 머리 · 어깨를 잘라 into의 (x, y)에 찍는다 — 지금 표정 · 다친 자국 · 우주복 · 감은 눈 (세계의 값).
    /// 돌려주는 열쇠가 같으면 그림도 같다 (바뀐 사람만 다시 찍게).
    /// </summary>
    public long Portrait(Sim s, int agentId, Img into, int x, int y)
    {
        var mind = s.Get<Mind>(); var life = s.Get<Life>();
        var a = mind.Agents[agentId];
        ref var c = ref a.C;
        var b = life.Bodies[c.Body];
        var l = LookOf(mind, a);
        l.Suit = b.Suit != 0; l.Dead = b.Alive == 0;
        bool closed = b.Alive == 0 || b.Conscious == 0 || b.Asleep != 0;
        l.EyesClosed = closed; l.Hurt = HurtOf(b); l.Face = closed ? DotFace.Calm : FaceOf(c, b);
        var (held, motion, cycle) = (l.Held, l.Motion, l.Cycle8);   // 화면의 사람 그림이 쓰는 몫은 그린 뒤 되돌린다
        l.Held = DotHeld.None; l.Motion = DotMotion.None; l.Cycle8 = false;
        int stain = StainOf(b);
        long key = (long)l.Face | (long)l.Hurt << 4 | (l.Suit ? 1L << 40 : 0) | (l.Dead ? 1L << 41 : 0) | (closed ? 1L << 42 : 0) | (long)stain << 43;   // 43~45: 옷의 얼룩 (어깨 줄에 보인다)
        var img = PersonImg(a.Id, l, DotFacing.S, DotPose.Stand, 0, false, false, stain);
        for (int yy = 0; yy < FaceH && FaceTop + yy < img.H; yy++)
            Array.Copy(img.Px, ((FaceTop + yy) * img.W) * 4, into.Px, ((y + yy) * into.W + x) * 4, Math.Min(FaceW, img.W) * 4);
        (l.Held, l.Motion, l.Cycle8) = (held, motion, cycle);
        return key;
    }

    private readonly Dictionary<(int id, long key), Img> _people = new();

    /// <summary>재기용 (--live): 사람 그림 캐시를 비운 횟수 · 지금 장 수 (상한 6,000장).</summary>
    public int PeopleWipes { get; private set; }
    public int PeopleCached => _people.Count;

    /// <summary>사람 그림 (같은 사람 · 자세 · 방향 · 장 · 상태 · 옷의 얼룩이면 다시 쓴다).</summary>
    private Img PersonImg(int id, PersonLook l, DotFacing face, DotPose pose, int frame, bool lie, bool crawl, int stain)
    {
        long key = (long)face | (long)pose << 2 | (long)(frame & 7) << 6 | (long)l.Held << 9 | (long)l.Face << 13 | (l.Suit ? 1L : 0) << 16 | (l.Dead ? 1L : 0) << 17
                   | (l.EyesClosed ? 1L : 0) << 18 | (lie ? 1L : 0) << 19 | (crawl ? 1L : 0) << 20 | (long)l.Hurt << 21 | (long)l.Motion << 46 | (l.Cycle8 ? 1L : 0) << 49
                   | (long)stain << 50;   // 50~51: 더러움 단 · 52: 지림
        if (_people.TryGetValue((id, key), out var img)) return img;
        if (_people.Count > 6000) { _people.Clear(); PeopleWipes++; }
        var dot = DotPeople.Draw(l, lie || crawl ? DotFacing.S : face, pose, frame);   // 기는 사람: 엎드린 걷기 (팔다리가 번갈아)
        Stain(dot, l, stain);                                          // 옷의 얼룩 · 지린 자국 (돌리기 전에 — 누운 · 기는 그림도 같은 칠)
        if (lie) dot = dot.Rotate(cw: false);                         // 누운 사람: 옆으로 (머리가 왼쪽)
        else if (crawl) dot = dot.Rotate(cw: face == DotFacing.E);    // 기는 사람: 엎드려 가는 쪽으로 머리
        img = dot.ToImg();
        _people[(id, key)] = img;
        return img;
    }
}
