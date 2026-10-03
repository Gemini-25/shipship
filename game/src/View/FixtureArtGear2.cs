using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 압축-마 그림 표 — 무중력 · 컴퓨터 · 기록 · 소리 · 사람 · 옷 · 안전 · 도킹 · 별 (15종).
/// 자석 신발 걸이(구리 코일 밑창 · 충전 맥박) · 서버 선반(1U 칸 · 깜빡이는 등줄 · 팬) · 기록 금고(주황 장갑 상자 · 반사 띠 · 기록등)
/// · 청음기(놋쇠 나팔 · 걸린 헤드폰 · 파형 화면) · 회의 칠판(낙서 · 자석 · 바퀴) · 추모 벽(이름표 줄 · 촛불 · 마른 꽃)
/// · 악기 자리(건반 · 기타 · 앰프 — 누가 치면 음표) · 증류기(구리 솥 · 백조목 · 냉각 코일 · 받는 병) · 빨래 건조대(X 다리 · 셔츠 · 양말 · 수건)
/// · 재봉틀(팔 · 실패 · 바퀴 · 바늘) · 눈 세척대(초록 표지 · 쌍 꼭지 · 노란 발판) · 산소 마스크 함(투명 문 · 노란 마스크 · 감긴 관)
/// · 방열복 걸이(은빛 누빔 · 금빛 얼굴창) · 접안 고리 제어반(고리 · 집게 넷 · 큰 손잡이) · 망원경(세 다리 · 경통 · 파인더).
/// </summary>
public static partial class FixtureArt
{
    private static void GearArt2(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.MagBootRack] = new(BootRackBody, BootRackLife, BootRackFine, Look.Sparks, 0.5f, 0.85f);
        t[FurnitureType.ServerRack] = new(ServerBody, ServerLife, ServerFine, Look.Heat, 0.5f, 0.1f);
        t[FurnitureType.RecorderVault] = new(VaultBody, VaultLife, VaultFine, Look.Flicker, 0.8f, 0.2f);
        t[FurnitureType.ListeningPost] = new(EarBody, EarLife, EarFine, Look.Flicker, 0.75f, 0.75f);
        t[FurnitureType.MeetingBoard] = new(BoardBody, BoardLife, BoardFine, Look.Jam, 0.5f, 0.9f);
        t[FurnitureType.MemorialWall] = new(MemorialBody, MemorialLife, MemorialFine, Look.Smoke, 0.5f, 0.8f);
        t[FurnitureType.MusicCorner] = new(MusicBody, MusicLife, MusicFine, Look.Flicker, 0.85f, 0.85f);
        t[FurnitureType.LabStill] = new(StillBody, StillLife, StillFine, Look.Heat, 0.3f, 0.7f);
        t[FurnitureType.ClothesRack] = new(DryRackBody, DryRackLife, DryRackFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.SewingMachine] = new(SewBody, SewLife, SewFine, Look.Jam, 0.3f, 0.5f);
        t[FurnitureType.EyeWash] = new(EyeWashBody, EyeWashLife, EyeWashFine, Look.Leak, 0.5f, 0.6f);
        t[FurnitureType.OxygenMaskBox] = new(MaskBody, MaskLife, MaskFine, Look.Gas, 0.5f, 0.5f);
        t[FurnitureType.HeatSuitRack] = new(SuitRackBody, SuitRackLife, SuitRackFine, Look.Heat, 0.5f, 0.4f);
        t[FurnitureType.DockClampPanel] = new(ClampBody, ClampLife, ClampFine, Look.Sparks, 0.5f, 0.5f);
        t[FurnitureType.Telescope] = new(ScopeBody, ScopeLife, ScopeFine, Look.Grind, 0.5f, 0.6f);
    }

    private static readonly Color GBoot = new("#3a3a42"), GCoil = new("#c87a3a"), GRack = new("#16191f"), GVault = new("#e8641e"), GHorn = new("#c8a050"), GBoard = new("#eef2f4");
    private static readonly Color GPlaque = new("#2a2420"), GCandle = new("#f0e8d0"), GKeysBlack = new("#101012"), GGuitar = new("#a8642a"), GPot = new("#b87333"), GFlask = new("#cfe8f0");
    private static readonly Color GSewBody = new("#1a1a1e"), GCrossGreen = new("#1f8a4a"), GMask = new("#f0c020"), GO2 = new("#2a8a4a"), GFoil = new("#c8ccd4"), GVisor = new("#d8a830"), GScope = new("#e8e8ec");
    private static readonly Color[] GCloth = { new("#c0392b"), new("#2a6aa8"), new("#e8e2d4"), new("#3a8a4a"), new("#8e44ad") };

    // ─────────────── 자석 신발 걸이 ───────────────

    private static void BootRackBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.04f, 0.04f, 0.96f, 0.14f), Steel3, 1f, Steel4); // 위 막대
        Line(ci, x.P(0.06f, 0.14f), x.P(0.06f, 0.96f), Steel3, 1.4f);
        Line(ci, x.P(0.94f, 0.14f), x.P(0.94f, 0.96f), Steel3, 1.4f);
        for (int k = 0; k < 2; k++) // 신발 두 켤레
            for (int s = 0; s < 2; s++)
            {
                var p = x.P(0.22f + k * 0.42f + s * 0.17f, 0.5f);
                ci.DrawColoredPolygon(new[] { p + new Vector2(-2f, -6f), p + new Vector2(2f, -6f), p + new Vector2(2.4f, 3f), p + new Vector2(-1f, 5f), p + new Vector2(-2.4f, 4f) }, GBoot);
                ci.DrawRect(new Rect2(p + new Vector2(-2.6f, 4.4f), new Vector2(5.2f, 1.6f)), GCoil); // 구리 코일 밑창
            }
        ci.DrawRect(x.Q(0.1f, 0.88f, 0.9f, 0.94f), Steel2); // 충전 접점 판
        if (x.Tier >= 2) for (int k = 0; k < 2; k++) Box(ci, new Rect2(x.P(0.3f + k * 0.42f, 0.22f) - new Vector2(2f, 1.5f), new Vector2(4f, 3f)), new Color("#2a4a6a"), 0.5f); // II: 전지 팩
        if (x.Tier >= 3) ci.DrawRect(x.Q(0.4f, 0.04f, 0.6f, 0.14f), Cyan.WithAlpha(0.5f)); // III: 치수 화면
    }

    private static void BootRackLife(in Fix x)
    {
        var ci = x.Ci;
        bool hold = x.W != null && x.W.ZeroG.Weightless;
        for (int k = 0; k < 4; k++)
        {
            var p = x.P(0.22f + (k / 2) * 0.42f + (k % 2) * 0.17f, 0.5f) + new Vector2(0f, 5.2f);
            float a = x.On ? (hold ? 0.9f : 0.25f + 0.25f * Pulse(x.T + k * 0.4f, 2.4f)) * x.Glow : 0f;
            ci.DrawRect(new Rect2(p - new Vector2(2.6f, 0.8f), new Vector2(5.2f, 1.6f)), new Color("#ffb070").WithAlpha(a)); // 충전 맥박 · 무게가 없으면 꽉 붙든다
        }
    }

    private static void BootRackFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 4; k++) { var p = x.P(0.22f + (k / 2) * 0.42f + (k % 2) * 0.17f, 0.42f); Line(ci, p - new Vector2(1.2f, 0f), p + new Vector2(1.2f, 0f), Cream.WithAlpha(0.6f), 0.4f); } // 끈
        Bolts(ci, x.Q(0.04f, 0.04f, 0.96f, 0.14f), 0.8f, 0.35f);
    }

    // ─────────────── 서버 선반 ───────────────

    private static void ServerBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, GRack, 1f, Steel3, 2);
        for (int k = 0; k < 6; k++) // 1U 칸
        {
            var u = x.Q(0.08f, 0.14f + k * 0.13f, 0.92f, 0.25f + k * 0.13f);
            ci.DrawRect(u, Steel1);
            Line(ci, u.Position + new Vector2(0f, u.Size.Y), u.End, Steel3, 0.5f);
        }
        Grille(ci, x.Q(0.12f, 0.02f, 0.88f, 0.11f), 1.6f, Steel3, 0.6f); // 위 팬 그릴
        Cable(ci, x.P(0.96f, 0.2f), x.P(0.96f, 0.9f), 1.5f, new Color("#2a5aa8"), 1.4f); // 선 다발
        Cable(ci, x.P(0.98f, 0.25f), x.P(0.98f, 0.85f), 1f, new Color("#c0392b"), 1f);
        if (x.Tier >= 2) Box(ci, x.Q(0.6f, 0.14f, 0.9f, 0.25f), new Color("#1a3a6a"), 0.5f); // II: 예비 기억 칸
        if (x.Tier >= 3) Pipe(ci, x.P(0.02f, 0.1f), x.P(0.02f, 0.95f), 1.4f, new Color("#4aa3e0"), false); // III: 물 냉각관
    }

    private static void ServerLife(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 6; k++)
            for (int i = 0; i < 4; i++)
            {
                float h = Hash(x.Id * 7 + k, i + (int)(x.T * (3f + i)), 961);
                bool on = x.Lit && h < 0.35f + 0.5f * x.Eff;
                Led(ci, x.P(0.16f + i * 0.08f, 0.195f + k * 0.13f), i == 3 ? Amber : RackLed, on ? 0.9f * x.Glow : 0.08f, 0.6f);
            }
        if (x.On && x.Lod > 0) for (int k = 0; k < 3; k++) Fan(ci, x.P(0.25f + k * 0.25f, 0.065f), 1.6f, 4, x.Ang(14f) + k, Steel4, 0.5f);
    }

    private static void ServerFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 6; k++) { Dot(ci, x.P(0.1f, 0.195f + k * 0.13f), 0.35f, Chrome); Dot(ci, x.P(0.9f, 0.195f + k * 0.13f), 0.35f, Chrome); } // 나사
        Tag(ci, x.P(0.75f, 0.92f), "03", 3, Cream.WithAlpha(0.6f));
    }

    // ─────────────── 기록 금고 ───────────────

    private static void VaultBody(in Fix x)
    {
        var ci = x.Ci;
        var body = x.Q(0.1f, 0.12f, 0.9f, 0.9f);
        Box(ci, body, GVault, 2f, GVault.Darkened(0.35f), 2);
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.12f + k * 0.25f, 0.88f), x.P(0.32f + k * 0.25f, 0.14f), Colors.White.WithAlpha(0.7f), 1.4f); // 반사 띠
        for (int k = 0; k < 2; k++) Box(ci, new Rect2(x.P(0.1f, 0.3f + k * 0.4f) - new Vector2(1.5f, 2f), new Vector2(3f, 4f)), Steel3, 0.5f); // 경첩
        Line(ci, x.P(0.84f, 0.4f), x.P(0.84f, 0.6f), Steel4, 1.6f); // 손잡이
        ci.DrawCircle(x.P(0.75f, 0.2f), 1.6f, new Color("#3a0a0a")); // 기록등 자리
        if (x.Tier >= 2) Ring(ci, x.P(0.25f, 0.2f), 1.4f, Cyan, 0.6f, 10); // II: 광섬유 단자
        if (x.Tier >= 3) Box(ci, x.Q(0.04f, 0.06f, 0.96f, 0.96f), new Color(0, 0, 0, 0f), 3f, Steel4, 1); // III: 겉 껍데기
    }

    private static void VaultLife(in Fix x)
    {
        var ci = x.Ci;
        bool rec = x.On && x.W != null && x.W.Blackbox.Recording;
        Led(ci, x.P(0.75f, 0.2f), Danger, rec ? (Mathf.PosMod(x.T, 1.2f) < 0.6f ? 0.95f : 0.15f) : 0.05f, 1.2f); // 기록 중
        if (x.Tier >= 2 && rec && x.Lod > 0) { float q = Mathf.PosMod(x.T * 1.5f, 1f); Dot(ci, x.P(0.25f, 0.2f) + new Vector2(-q * 6f, 0f), 0.5f, Cyan.WithAlpha(1f - q)); } // 사본이 흐른다
    }

    private static void VaultFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.1f, 0.12f, 0.9f, 0.9f), 1.2f, 0.5f);
        Tag(ci, x.P(0.5f, 0.6f), "열지 마시오", 3, Colors.White.WithAlpha(0.8f));
    }

    // ─────────────── 청음기 ───────────────

    private static void EarBody(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        var mouth = x.C - f * 2f;
        ci.DrawColoredPolygon(new[] { mouth + side * 5f + f * -4f, mouth - side * 5f + f * -4f, mouth - side * 1f + f * 5f, mouth + side * 1f + f * 5f }, GHorn); // 나팔
        Ring(ci, mouth - f * 4f, 5f, GHorn.Darkened(0.3f), 1f, 18);
        Line(ci, mouth + f * 5f, x.C + f * 9f, Steel4, 1.2f); // 받침대
        ci.DrawArc(x.P(0.8f, 0.2f), 2.6f, Mathf.Pi, Mathf.Tau, 10, Rubber, 1f, true); // 걸린 헤드폰
        Dot(ci, x.P(0.8f, 0.2f) + new Vector2(-2.6f, 0f), 1f, Rubber);
        Dot(ci, x.P(0.8f, 0.2f) + new Vector2(2.6f, 0f), 1f, Rubber);
        Box(ci, x.Q(0.55f, 0.55f, 0.95f, 0.92f), Steel1, 1f, Steel3); // 파형 화면 상자
        ci.DrawRect(x.Q(0.6f, 0.6f, 0.9f, 0.82f), new Color("#06140c"));
        if (x.Tier >= 2) Dot(ci, x.P(0.15f, 0.85f), 1.4f, Steel4); // II: 둘째 마이크
        if (x.Tier >= 3) for (int k = 0; k < 4; k++) ci.DrawRect(x.Q(0.6f + k * 0.075f, 0.86f, 0.64f + k * 0.075f, 0.9f), Good.WithAlpha(0.4f)); // III: 주파수 막대
    }

    private static void EarLife(in Fix x)
    {
        var ci = x.Ci;
        float loud = 0.1f;
        foreach (var f in x.F.Room.Furniture) if (f.Machine is Machine m && HazardsV18.Rotating(f.Type) && m.Wear > loud) loud = m.Wear;
        if (!x.Lit) return;
        var scr = x.Q(0.6f, 0.6f, 0.9f, 0.82f);
        var pts = new Vector2[12];
        for (int i = 0; i < 12; i++)
        {
            float u = i / 11f;
            float a = Mathf.Sin(u * 18f + x.T * 6f) * loud + (loud > 0.6f ? Mathf.Sin(u * 53f + x.T * 21f) * (loud - 0.6f) * 1.5f : 0f); // 닳을수록 날카로운 떨림
            pts[i] = new Vector2(scr.Position.X + u * scr.Size.X, scr.GetCenter().Y + a * scr.Size.Y * 0.4f);
        }
        ci.DrawPolyline(pts, (loud > 0.7f ? Amber : Good).WithAlpha(0.9f * x.Glow), 0.7f, true);
        if (x.User != null) Ring(ci, x.C - x.Front * 6f, 6f + Mathf.PosMod(x.T * 4f, 3f), GHorn.WithAlpha(0.3f), 0.5f, 16); // 듣는 중
    }

    private static void EarFine(in Fix x)
    {
        var ci = x.Ci;
        Ring(ci, x.C - x.Front * 6f, 4.4f, GHorn.Lightened(0.3f), 0.4f, 16);
        Knob(ci, x.P(0.9f, 0.88f), 0.9f, 0.7f, Chrome);
    }

    // ─────────────── 회의 칠판 ───────────────

    private static void BoardBody(in Fix x)
    {
        var ci = x.Ci;
        var board = x.Q(0.04f, 0.06f, 0.96f, 0.7f);
        Box(ci, board, GBoard, 1f, Chrome, 1);
        var pts = new Vector2[7];
        for (int i = 0; i < 7; i++) pts[i] = x.P(0.1f + i * 0.12f, 0.2f + 0.12f * Hash(x.Id, i, 971)); // 낙서 한 줄
        ci.DrawPolyline(pts, new Color("#2a5aa8"), 0.8f, true);
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.1f, 0.38f + k * 0.08f), x.P(0.1f + 0.5f * Hash(x.Id, k, 972) + 0.2f, 0.38f + k * 0.08f), new Color("#3a3a3a").WithAlpha(0.7f), 0.6f); // 글 줄
        Dot(ci, x.P(0.85f, 0.15f), 1f, Danger); // 자석
        Dot(ci, x.P(0.78f, 0.15f), 1f, new Color("#2a5aa8"));
        ci.DrawRect(x.Q(0.2f, 0.7f, 0.8f, 0.74f), Steel4); // 펜 받침
        Line(ci, x.P(0.2f, 0.74f), x.P(0.12f, 0.95f), Steel3, 1f); // 다리
        Line(ci, x.P(0.8f, 0.74f), x.P(0.88f, 0.95f), Steel3, 1f);
        Dot(ci, x.P(0.12f, 0.97f), 1f, Rubber); Dot(ci, x.P(0.88f, 0.97f), 1f, Rubber); // 바퀴
        if (x.Tier >= 2) for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(x.P(0.7f + (k % 2) * 0.1f, 0.35f + k * 0.08f), new Vector2(2.4f, 2.4f)), new Color("#f8e070")); // II: 붙임쪽지
        if (x.Tier >= 3) Line(ci, x.P(0.92f, 0.1f), x.P(0.92f, 0.5f), new Color("#8a5a2a"), 1.4f); // III: 발언 막대 꽂이
    }

    private static void BoardLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.User is CrewMember u)
        {
            float q = Mathf.PosMod(x.T * 0.25f, 1f); // 쓰는 중: 새 줄이 자란다
            var a = x.P(0.1f, 0.62f);
            var b = x.P(0.1f + 0.75f * q, 0.62f + 0.02f * Mathf.Sin(q * 30f));
            Line(ci, a, b, new Color("#c0392b"), 0.7f);
            Dot(ci, b, 0.9f, new Color("#c0392b"));
        }
        else if (x.W != null && x.W.Meetings.Gathering) Dot(ci, x.P(0.5f, 0.12f), 0.8f + 0.3f * Pulse(x.T, 3f), Amber); // 회의 중
    }

    private static void BoardFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(x.P(0.3f + k * 0.12f, 0.71f), new Vector2(2.6f, 0.8f)), k == 0 ? Danger : k == 1 ? new Color("#2a5aa8") : Rubber); // 펜 뚜껑
        Box(ci, new Rect2(x.P(0.65f, 0.705f), new Vector2(4f, 1.4f)), new Color("#6a5a4a"), 0.5f); // 지우개
    }

    // ─────────────── 추모 벽 ───────────────

    private static void MemorialBody(in Fix x)
    {
        var ci = x.Ci;
        var plaque = x.Q(0.06f, 0.04f, 0.94f, 0.66f);
        Box(ci, plaque, GPlaque, 1f, Brass, 1);
        for (int r = 0; r < 3; r++)
            for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(x.P(0.14f + i * 0.26f, 0.12f + r * 0.16f), new Vector2(5f, 2f)), Brass.Darkened(0.15f)); // 이름표
        Box(ci, new Rect2(x.P(0.42f, 0.1f) - new Vector2(1f, 0f), new Vector2(4f, 5f)), new Color("#3a3a3a"), 0.5f, Brass); // 사진틀
        ci.DrawRect(x.Q(0.06f, 0.7f, 0.94f, 0.76f), new Color("#4a3a2a")); // 초 받침
        for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(x.P(0.2f + k * 0.3f, 0.66f) - new Vector2(1f, 0f), new Vector2(2f, 3f)), GCandle); // 초
        Line(ci, x.P(0.88f, 0.7f), x.P(0.94f, 0.5f), new Color("#6a5a3a"), 0.8f); // 마른 꽃
        Dot(ci, x.P(0.94f, 0.5f), 1f, new Color("#a85a6a"));
        if (x.Tier >= 2) for (int k = 0; k < 5; k++) { var c = x.P(0.85f, 0.1f); var d = Vector2.FromAngle(-Mathf.Pi * 0.5f + k * Mathf.Tau / 5f); Line(ci, c, c + d * 2f, Brass, 0.6f); } // II: 새긴 별
        if (x.Tier >= 3) Line(ci, x.P(0.06f, 0.02f), x.P(0.94f, 0.02f), new Color("#ffe6b0").WithAlpha(0.5f), 1f); // III: 조명 띠
    }

    private static void MemorialLife(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++) // 촛불 (누가 서 있으면 조금 더 밝다)
        {
            var p = x.P(0.2f + k * 0.3f, 0.64f);
            float fl = 0.7f + 0.3f * Mathf.Sin(x.T * (7f + k) + k * 2f) * x.Glow;
            float lean = Mathf.Sin(x.T * 1.3f + k) * 0.4f;
            ci.DrawColoredPolygon(new[] { p + new Vector2(-0.8f, 0f), p + new Vector2(0.8f, 0f), p + new Vector2(lean, -2.6f * fl) }, new Color("#ffcc66").WithAlpha(x.User != null ? 0.95f : 0.75f));
        }
        if (x.Tier >= 3 && x.Lit && x.Lod > 0) ci.DrawRect(x.Q(0.06f, 0.02f, 0.94f, 0.12f), new Color("#ffe6b0").WithAlpha(0.08f));
    }

    private static void MemorialFine(in Fix x)
    {
        var ci = x.Ci;
        for (int r = 0; r < 3; r++) for (int i = 0; i < 3; i++) Line(ci, x.P(0.15f + i * 0.26f, 0.13f + r * 0.16f), x.P(0.2f + i * 0.26f, 0.13f + r * 0.16f), GPlaque, 0.4f); // 새긴 글
        Bolts(ci, x.Q(0.06f, 0.04f, 0.94f, 0.66f), 1f, 0.35f);
    }

    // ─────────────── 악기 자리 ───────────────

    private static void MusicBody(in Fix x)
    {
        var ci = x.Ci;
        var keys = x.Q(0.04f, 0.1f, 0.62f, 0.4f);
        ci.DrawRect(keys, Colors.White); // 건반
        for (int i = 1; i < 10; i++) Line(ci, keys.Position + new Vector2(keys.Size.X * i / 10f, 0f), keys.Position + new Vector2(keys.Size.X * i / 10f, keys.Size.Y), new Color("#9a9a9a"), 0.4f);
        for (int i = 0; i < 9; i++) if (i % 7 != 2 && i % 7 != 6) ci.DrawRect(new Rect2(keys.Position + new Vector2(keys.Size.X * (i + 0.7f) / 10f, 0f), new Vector2(keys.Size.X * 0.06f, keys.Size.Y * 0.6f)), GKeysBlack);
        Line(ci, x.P(0.1f, 0.4f), x.P(0.06f, 0.6f), Steel4, 1f); // 건반 다리
        Line(ci, x.P(0.56f, 0.4f), x.P(0.6f, 0.6f), Steel4, 1f);
        ci.DrawCircle(x.P(0.82f, 0.62f), 3.4f, GGuitar); // 기타 아래 몸통
        ci.DrawCircle(x.P(0.82f, 0.45f), 2.6f, GGuitar.Lightened(0.08f)); // 위 몸통
        ci.DrawCircle(x.P(0.82f, 0.52f), 0.9f, new Color("#1a0e06")); // 울림 구멍
        Line(ci, x.P(0.82f, 0.4f), x.P(0.86f, 0.06f), new Color("#4a2a12"), 1.2f); // 목
        Box(ci, x.Q(0.1f, 0.7f, 0.4f, 0.95f), Steel1, 1f, Steel3); // 앰프
        Grille(ci, x.Q(0.13f, 0.74f, 0.37f, 0.92f), 1.2f, Steel3, 0.4f);
        if (x.Tier >= 2) Ring(ci, x.P(0.55f, 0.82f), 2.4f, Steel4, 1f, 14); // II: 북 패드
        if (x.Tier >= 3) Box(ci, x.Q(0.45f, 0.6f, 0.65f, 0.7f), Steel2, 0.5f); // III: 섞개
    }

    private static void MusicLife(in Fix x)
    {
        var ci = x.Ci;
        var keys = x.Q(0.04f, 0.1f, 0.62f, 0.4f);
        if (x.User != null)
        {
            for (int k = 0; k < 2; k++) // 눌리는 건반
            {
                int i = (int)(Hash(x.Id, (int)(x.T * 4f) + k * 7, 981) * 10f);
                ci.DrawRect(new Rect2(keys.Position + new Vector2(keys.Size.X * i / 10f, keys.Size.Y * 0.6f), new Vector2(keys.Size.X / 10f, keys.Size.Y * 0.4f)), new Color("#c8d8f0"));
            }
            for (int k = 0; k < 3; k++) // 음표
            {
                float q = Mathf.PosMod(x.T * 0.4f + k / 3f, 1f);
                var p = x.P(0.3f, 0.1f) + new Vector2(Mathf.Sin(q * 5f + k) * 4f, -q * 10f);
                Dot(ci, p, 0.8f, Cream.WithAlpha(1f - q));
                Line(ci, p + new Vector2(0.7f, 0f), p + new Vector2(0.7f, -2.4f), Cream.WithAlpha(1f - q), 0.4f);
            }
        }
        Led(ci, x.P(0.36f, 0.73f), x.On ? Danger : Steel3, x.User != null ? 0.9f : 0.2f * x.Glow, 0.6f); // 앰프 등
    }

    private static void MusicFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.81f + k * 0.01f, 0.4f), x.P(0.81f + k * 0.01f, 0.66f), Chrome.WithAlpha(0.6f), 0.3f); // 줄
        Knob(ci, x.P(0.15f, 0.72f), 0.6f, 0.5f, Chrome);
        Knob(ci, x.P(0.22f, 0.72f), 0.6f, 1.5f, Chrome);
    }

    // ─────────────── 증류기 ───────────────

    private static void StillBody(in Fix x)
    {
        var ci = x.Ci;
        var pot = x.P(0.3f, 0.6f);
        Can(ci, pot, 5f, GPot, GPot.Darkened(0.3f)); // 구리 솥
        ci.DrawArc(pot - new Vector2(0f, 2f), 3.4f, Mathf.Pi, Mathf.Tau, 12, GPot.Lightened(0.2f), 2f, true); // 돔
        ci.DrawPolyline(new[] { pot - new Vector2(0f, 5.4f), pot + new Vector2(3f, -8f), pot + new Vector2(8f, -7f), x.P(0.72f, 0.3f) }, GPot, 1.4f, true); // 백조목
        for (int k = 0; k < 4; k++) Ring(ci, x.P(0.72f, 0.35f + k * 0.08f), 2.2f, Copper, 0.9f, 12); // 냉각 코일
        ci.DrawColoredPolygon(new[] { x.P(0.66f, 0.75f), x.P(0.78f, 0.75f), x.P(0.84f, 0.95f), x.P(0.6f, 0.95f) }, GFlask.WithAlpha(0.6f)); // 받는 병
        Line(ci, x.P(0.72f, 0.62f), x.P(0.72f, 0.75f), Copper, 0.8f);
        if (x.Tier >= 2) Gauge(ci, pot + new Vector2(-4f, -6f), 1.6f, 0.6f, Danger); // II: 온도계
        if (x.Tier >= 3) Glass(ci, x.Q(0.5f, 0.05f, 0.58f, 0.4f), new Color("#cfe8f0"), 1f); // III: 유리 탑
    }

    private static void StillLife(in Fix x)
    {
        var ci = x.Ci;
        var pot = x.P(0.3f, 0.6f);
        if (x.On) // 버너 불꽃
            for (int k = 0; k < 3; k++) { float fl = 0.6f + 0.4f * Mathf.Sin(x.T * 11f + k * 2f); Line(ci, pot + new Vector2(-2.4f + k * 2.4f, 6.4f), pot + new Vector2(-2.4f + k * 2.4f, 6.4f - 2f * fl), new Color("#6aa8ff").WithAlpha(0.8f * x.Glow), 0.9f); }
        if (x.User != null || x.On && x.Eff > 0.5f)
        {
            float q = Mathf.PosMod(x.T * 0.9f, 1f);
            Dot(ci, x.P(0.72f, 0.66f + q * 0.1f), 0.5f, GFlask.WithAlpha(1f - q)); // 떨어지는 방울
        }
        float fill = 0.3f + 0.3f * Mathf.PosMod(x.T * 0.01f + x.Id, 1f);
        ci.DrawRect(x.Q(0.63f, 0.95f - fill * 0.2f, 0.81f, 0.95f), new Color("#e8f8ff").WithAlpha(0.5f)); // 병에 고인 것
    }

    private static void StillFine(in Fix x)
    {
        var ci = x.Ci;
        var pot = x.P(0.3f, 0.6f);
        for (int k = 0; k < 6; k++) Dot(ci, pot + Vector2.FromAngle(k * Mathf.Tau / 6f) * 4.4f, 0.35f, GPot.Darkened(0.4f)); // 리벳
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.62f, 0.8f + k * 0.04f), x.P(0.65f, 0.8f + k * 0.04f), Colors.White.WithAlpha(0.6f), 0.3f); // 눈금
    }

    // ─────────────── 빨래 건조대 ───────────────

    private static void DryRackBody(in Fix x)
    {
        var ci = x.Ci;
        Line(ci, x.P(0.04f, 0.95f), x.P(0.3f, 0.1f), Chrome, 1f); // X 다리
        Line(ci, x.P(0.3f, 0.95f), x.P(0.04f, 0.1f), Chrome, 1f);
        Line(ci, x.P(0.7f, 0.95f), x.P(0.96f, 0.1f), Chrome, 1f);
        Line(ci, x.P(0.96f, 0.95f), x.P(0.7f, 0.1f), Chrome, 1f);
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.04f, 0.12f + k * 0.12f), x.P(0.96f, 0.12f + k * 0.12f), Chrome.Lightened(0.2f), 0.7f); // 봉 셋
        var shirt = x.P(0.3f, 0.14f);
        ci.DrawColoredPolygon(new[] { shirt + new Vector2(-4f, 0f), shirt + new Vector2(4f, 0f), shirt + new Vector2(6f, 2f), shirt + new Vector2(3.6f, 3f), shirt + new Vector2(3.4f, 9f), shirt + new Vector2(-3.4f, 9f), shirt + new Vector2(-3.6f, 3f), shirt + new Vector2(-6f, 2f) }, GCloth[x.Id % GCloth.Length]); // 셔츠
        ci.DrawRect(new Rect2(x.P(0.62f, 0.26f), new Vector2(5f, 8f)), GCloth[(x.Id + 2) % GCloth.Length]); // 수건
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.62f, 0.26f) + new Vector2(0f, 2f + k * 2f), x.P(0.62f, 0.26f) + new Vector2(5f, 2f + k * 2f), Colors.White.WithAlpha(0.4f), 0.5f);
        for (int k = 0; k < 2; k++) ci.DrawColoredPolygon(new[] { x.P(0.8f + k * 0.08f, 0.38f), x.P(0.84f + k * 0.08f, 0.38f), x.P(0.84f + k * 0.08f, 0.5f), x.P(0.88f + k * 0.08f, 0.52f), x.P(0.86f + k * 0.08f, 0.56f), x.P(0.8f + k * 0.08f, 0.53f) }, GCloth[(x.Id + 3 + k) % GCloth.Length]); // 양말
        if (x.Tier >= 2) Line(ci, x.P(0.04f, 0.05f), x.P(0.96f, 0.05f), Cyan.WithAlpha(0.6f), 1.2f); // II: 이온 막대
        if (x.Tier >= 3) for (int k = 0; k < 3; k++) Line(ci, x.P(0.04f, 0.13f + k * 0.12f), x.P(0.96f, 0.13f + k * 0.12f), Ember.WithAlpha(0.4f), 0.4f); // III: 데우는 봉
    }

    private static void DryRackLife(in Fix x)
    {
        var ci = x.Ci;
        bool floating = x.W != null && x.W.ZeroG.Weightless;
        float sway = floating ? 0f : Mathf.Sin(x.T * 0.9f + x.Id) * 0.6f;
        var hem = x.P(0.3f, 0.14f) + new Vector2(sway, floating ? -1f : 9f);
        Line(ci, hem - new Vector2(3.4f, 0f), hem + new Vector2(3.4f, 0f), GCloth[x.Id % GCloth.Length].Darkened(0.2f), 1f); // 흔들리는 셔츠 단
        if (x.F.Room.Humidity > 0.5f || x.User != null) // 마르기 전: 물방울
            for (int k = 0; k < 2; k++) { float q = Mathf.PosMod(x.T * 0.7f + k * 0.5f, 1f); Dot(ci, x.P(0.27f + k * 0.4f, 0.5f + q * 0.4f), 0.5f, WaterLight.WithAlpha(0.7f * (1f - q))); }
        if (x.Tier >= 2 && x.Lit && x.Lod > 0) Dot(ci, x.P(0.5f, 0.05f), 0.6f + 0.3f * Pulse(x.T, 4f), Cyan.WithAlpha(0.6f));
    }

    private static void DryRackFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 4; k++) Dot(ci, x.P(0.22f + k * 0.18f, 0.12f), 0.6f, new Color("#e0c040")); // 빨래집게
        Dot(ci, x.P(0.17f, 0.52f), 0.5f, Steel4);
    }

    // ─────────────── 재봉틀 ───────────────

    private static void SewBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.04f, 0.6f, 0.96f, 0.9f), GSewBody, 1.5f, Steel3); // 바닥판
        ci.DrawColoredPolygon(new[] { x.P(0.7f, 0.6f), x.P(0.86f, 0.6f), x.P(0.86f, 0.18f), x.P(0.2f, 0.18f), x.P(0.2f, 0.34f), x.P(0.7f, 0.34f) }, GSewBody); // 팔
        Box(ci, x.Q(0.14f, 0.16f, 0.3f, 0.5f), GSewBody.Lightened(0.06f), 1f); // 머리
        ci.DrawCircle(x.P(0.92f, 0.38f), 3f, Steel4); // 손바퀴
        Ring(ci, x.P(0.92f, 0.38f), 3f, Chrome, 0.6f, 14);
        Line(ci, x.P(0.5f, 0.18f), x.P(0.5f, 0.06f), Chrome, 0.8f); // 실패 꽂이
        ci.DrawRect(new Rect2(x.P(0.5f, 0.06f) - new Vector2(1.4f, 1.6f), new Vector2(2.8f, 3.2f)), new Color("#c0392b")); // 실패
        ci.DrawRect(x.Q(0.08f, 0.66f, 0.5f, 0.86f), GCloth[(x.Id + 1) % GCloth.Length]); // 옷감
        if (x.Tier >= 2) Dot(ci, x.P(0.24f, 0.52f), 0.9f, new Color("#fff0c0")); // II: 바늘 등
        if (x.Tier >= 3) Ring(ci, x.P(0.36f, 0.76f), 2.6f, new Color("#c8a46a"), 0.8f, 14); // III: 수틀
    }

    private static void SewLife(in Fix x)
    {
        var ci = x.Ci;
        bool sewing = x.User != null;
        float bob = sewing ? Mathf.Sin(x.T * 22f) * 1.2f : 0f;
        Line(ci, x.P(0.22f, 0.5f), x.P(0.22f, 0.6f) + new Vector2(0f, bob), Chrome, 0.6f); // 바늘
        if (sewing)
        {
            float len = Mathf.PosMod(x.T * 0.1f, 1f);
            for (int k = 0; k < (int)(len * 8f); k++) Line(ci, x.P(0.22f + k * 0.03f, 0.76f), x.P(0.235f + k * 0.03f, 0.76f), Cream, 0.4f); // 땀이 늘어난다
            float a = x.T * 9f;
            Line(ci, x.P(0.92f, 0.38f), x.P(0.92f, 0.38f) + Vector2.FromAngle(a) * 2.6f, Chrome, 0.6f); // 손바퀴 돈다
        }
        else if (x.Lod == 2) Dot(ci, x.P(0.24f, 0.52f), 0.4f, Colors.White.WithAlpha(0.2f * x.Glow));
    }

    private static void SewFine(in Fix x)
    {
        var ci = x.Ci;
        ci.DrawArc(x.P(0.5f, 0.26f), 3f, 0.2f, 2.8f, 10, Brass, 0.4f, true); // 금박 무늬
        Line(ci, x.P(0.18f, 0.58f), x.P(0.26f, 0.58f), Chrome, 0.5f); // 노루발
    }

    // ─────────────── 눈 세척대 ───────────────

    private static void EyeWashBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.2f, 0.02f, 0.8f, 0.3f), GCrossGreen, 1f); // 초록 표지
        ci.DrawRect(x.Q(0.45f, 0.06f, 0.55f, 0.26f), Colors.White); // 흰 십자
        ci.DrawRect(x.Q(0.3f, 0.12f, 0.7f, 0.2f), Colors.White);
        var bowl = x.P(0.5f, 0.6f);
        ci.DrawCircle(bowl, 5f, new Color("#c8ccd4")); // 대야
        ci.DrawCircle(bowl, 3.6f, new Color("#8a929e"));
        Dot(ci, bowl + new Vector2(-1.8f, 0f), 1f, new Color("#2a8a4a")); // 쌍 꼭지
        Dot(ci, bowl + new Vector2(1.8f, 0f), 1f, new Color("#2a8a4a"));
        ci.DrawRect(x.Q(0.3f, 0.88f, 0.7f, 0.98f), WarnYellow); // 발판
        if (x.Tier >= 2) Cable(ci, x.P(0.85f, 0.5f), x.P(0.9f, 0.9f), 1.5f, new Color("#e0b64a"), 0.8f); // II: 손 호스
        if (x.Tier >= 3) Box(ci, x.Q(0.82f, 0.32f, 0.98f, 0.48f), Steel3, 0.5f); // III: 미지근한 물 통
    }

    private static void EyeWashLife(in Fix x)
    {
        var ci = x.Ci;
        bool hurt = false;
        if (x.W != null) foreach (var c in x.W.Crew) if (!c.Dead && c.Room == x.F.Room && c.Vitals.Injury > 0f && c.Vitals.Injury < 0.15f) { hurt = true; break; }
        if (!(x.User != null || hurt) || x.Dead) return;
        var bowl = x.P(0.5f, 0.6f);
        for (int k = 0; k < 2; k++) // 솟는 물줄기
        {
            var s = bowl + new Vector2(k == 0 ? -1.8f : 1.8f, 0f);
            float h = 3f + Mathf.Sin(x.T * 8f + k) * 0.6f;
            ci.DrawArc(s + new Vector2(k == 0 ? 1f : -1f, 0f), h * 0.5f, Mathf.Pi, Mathf.Tau, 8, WaterLight.WithAlpha(0.7f), 0.6f, true);
        }
    }

    private static void EyeWashFine(in Fix x)
    {
        var ci = x.Ci;
        var bowl = x.P(0.5f, 0.6f);
        Ring(ci, bowl + new Vector2(-1.8f, 0f), 1.3f, Colors.White.WithAlpha(0.5f), 0.3f, 8); // 먼지 뚜껑
        Ring(ci, bowl + new Vector2(1.8f, 0f), 1.3f, Colors.White.WithAlpha(0.5f), 0.3f, 8);
    }

    // ─────────────── 산소 마스크 함 ───────────────

    private static void MaskBody(in Fix x)
    {
        var ci = x.Ci;
        var box = x.Q(0.08f, 0.06f, 0.92f, 0.62f);
        Box(ci, box, Steel2, 1.5f, Steel4);
        for (int k = 0; k < 3; k++) // 마스크 셋 · 감긴 관
        {
            var p = x.P(0.22f + k * 0.28f, 0.3f);
            ci.DrawCircle(p, 2f, GMask);
            for (int i = 0; i < 3; i++) Ring(ci, p + new Vector2(0f, 2.6f + i * 1.1f), 1.2f, Cream.WithAlpha(0.6f), 0.4f, 8);
        }
        Glass(ci, box.Grow(-1f), new Color("#c8e8ff"), 1.5f); // 투명 문
        Can(ci, x.P(0.5f, 0.82f), 2.4f, GO2, Chrome); // 산소병
        if (x.Tier >= 2) ci.DrawCircle(x.P(0.88f, 0.82f), 2.4f, new Color("#e0a020")); // II: 연기 두건
        if (x.Tier >= 3) Led(ci, x.P(0.12f, 0.82f), Good, 0.5f, 0.8f); // III: 공기 감지기
    }

    private static void MaskLife(in Fix x)
    {
        var ci = x.Ci;
        var r = x.F.Room;
        bool bad = r.Unbreathable || x.W != null && (x.W.Fire.CountIn(r) > 0 || x.W.Smells.Level(r, SmellKind.Burnt) > 0.3f);
        if (!bad) { if (x.Tier >= 3 && x.Lod > 0) Led(ci, x.P(0.12f, 0.82f), Good, 0.3f + 0.3f * Pulse(x.T, 1f), 0.6f); return; }
        Line(ci, x.P(0.08f, 0.06f), x.P(0.08f, 0.62f), Amber, 1f); // 문이 열렸다
        for (int k = 0; k < 3; k++) // 늘어진 마스크가 흔들린다
        {
            var top = x.P(0.22f + k * 0.28f, 0.62f);
            var p = top + new Vector2(Mathf.Sin(x.T * 2.4f + k) * 1.5f, 5f);
            Line(ci, top, p, Cream.WithAlpha(0.8f), 0.5f);
            Dot(ci, p, 1.6f, GMask);
        }
        Led(ci, x.P(0.88f, 0.1f), Danger, Mathf.PosMod(x.T, 0.6f) < 0.3f ? 0.9f : 0.1f, 1f);
    }

    private static void MaskFine(in Fix x)
    {
        var ci = x.Ci;
        Tag(ci, x.P(0.5f, 0.95f), "O₂", 4, Colors.White.WithAlpha(0.8f));
        Line(ci, x.P(0.9f, 0.3f), x.P(0.9f, 0.38f), Chrome, 0.8f); // 걸쇠
    }

    // ─────────────── 방열복 걸이 ───────────────

    private static void SuitRackBody(in Fix x)
    {
        var ci = x.Ci;
        Line(ci, x.P(0.1f, 0.05f), x.P(0.9f, 0.05f), Steel4, 1.4f); // 걸이 막대
        var s = x.P(0.45f, 0.1f);
        ci.DrawColoredPolygon(new[] { s + new Vector2(-5f, 3f), s + new Vector2(5f, 3f), s + new Vector2(7f, 8f), s + new Vector2(5f, 9f), s + new Vector2(4.4f, 18f), s + new Vector2(-4.4f, 18f), s + new Vector2(-5f, 9f), s + new Vector2(-7f, 8f) }, GFoil); // 은빛 옷
        for (int k = -2; k <= 2; k++) { Line(ci, s + new Vector2(k * 2f - 2f, 4f), s + new Vector2(k * 2f + 2f, 17f), GFoil.Darkened(0.2f), 0.4f); Line(ci, s + new Vector2(k * 2f + 2f, 4f), s + new Vector2(k * 2f - 2f, 17f), GFoil.Darkened(0.2f), 0.4f); } // 누빔
        ci.DrawCircle(s, 3f, GFoil.Lightened(0.1f)); // 두건
        ci.DrawRect(new Rect2(s + new Vector2(-2f, -1f), new Vector2(4f, 2f)), GVisor); // 금빛 얼굴창
        for (int k = 0; k < 2; k++) Box(ci, new Rect2(x.P(0.78f + k * 0.1f, 0.3f) - new Vector2(1.2f, 0f), new Vector2(2.4f, 4f)), GFoil.Darkened(0.1f), 0.8f); // 장갑
        if (x.Tier >= 2) Box(ci, new Rect2(x.P(0.15f, 0.3f), new Vector2(5f, 7f)), new Color("#3a6ab0"), 1f); // II: 냉각 조끼
        if (x.Tier >= 3) Can(ci, x.P(0.2f, 0.85f), 1.8f, Steel4, Chrome); // III: 냉매 통
    }

    private static void SuitRackLife(in Fix x)
    {
        var ci = x.Ci;
        var s = x.P(0.45f, 0.1f);
        if (x.Lit) { float q = Mathf.PosMod(x.T * 0.25f + x.Id * 0.3f, 1f); Line(ci, s + new Vector2(-6f + q * 12f, 4f), s + new Vector2(-8f + q * 12f, 17f), Colors.White.WithAlpha(0.35f * x.Glow), 1f); } // 번뜩임
        if (x.F.Room.Air.Temperature > 28f && x.Lod > 0) // 더운 방의 아지랑이
            for (int k = 0; k < 3; k++) { float q = Mathf.PosMod(x.T * 0.4f + k / 3f, 1f); Line(ci, x.P(0.2f + k * 0.3f, 0.95f - q * 0.3f), x.P(0.22f + k * 0.3f, 0.9f - q * 0.3f), Ember.WithAlpha(0.2f * (1f - q)), 0.6f); }
    }

    private static void SuitRackFine(in Fix x)
    {
        var ci = x.Ci;
        var s = x.P(0.45f, 0.1f);
        Line(ci, s + new Vector2(0f, 3f), s + new Vector2(0f, 18f), GFoil.Darkened(0.35f), 0.4f); // 지퍼
        Dot(ci, s + new Vector2(1.4f, -0.4f), 0.4f, Colors.White);
    }

    // ─────────────── 접안 고리 제어반 ───────────────

    private static void ClampBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, Steel1, 1.5f, Steel3);
        Stripes(ci, x.Q(0.0f, 0.0f, 1f, 0.08f), 2f); // 빗금 테
        var ring = x.P(0.4f, 0.5f);
        Ring(ci, ring, 6f, Steel4, 2f, 24); // 고리
        for (int k = 0; k < 4; k++) { var d = Vector2.FromAngle(k * Mathf.Pi * 0.5f + Mathf.Pi * 0.25f); ci.DrawRect(new Rect2(ring + d * 7.5f - new Vector2(1.2f, 1.2f), new Vector2(2.4f, 2.4f)), Steel3); } // 집게 넷
        Line(ci, x.P(0.82f, 0.85f), x.P(0.82f, 0.3f), Steel4, 1.6f); // 큰 손잡이
        Box(ci, new Rect2(x.P(0.82f, 0.3f) - new Vector2(1.8f, 1.8f), new Vector2(3.6f, 3.6f)), Danger, 1f);
        Gauge(ci, x.P(0.82f, 0.15f), 2f, 0.5f, Good); // 압력계
        if (x.Tier >= 2) Ring(ci, ring, 4.2f, Steel4.Lightened(0.2f), 1f, 20); // II: 둘째 씰
        if (x.Tier >= 3) { Line(ci, ring - new Vector2(3f, 0f), ring + new Vector2(3f, 0f), Cyan.WithAlpha(0.6f), 0.5f); Line(ci, ring - new Vector2(0f, 3f), ring + new Vector2(0f, 3f), Cyan.WithAlpha(0.6f), 0.5f); } // III: 맞춤 십자
    }

    private static void ClampLife(in Fix x)
    {
        var ci = x.Ci;
        var ring = x.P(0.4f, 0.5f);
        int lit = x.On ? (int)Mathf.PosMod(x.T * 1.2f, 5f) : 0;
        for (int k = 0; k < 4; k++) // 집게가 하나씩 맞물린다
        {
            var d = Vector2.FromAngle(k * Mathf.Pi * 0.5f + Mathf.Pi * 0.25f);
            Led(ci, ring + d * 7.5f, k < lit ? Good : Amber, x.On ? 0.8f * x.Glow : 0.05f, 0.8f);
        }
        if (x.User != null) Ring(ci, ring, 6f, Cyan.WithAlpha(0.3f + 0.3f * Pulse(x.T, 5f)), 0.6f, 24);
    }

    private static void ClampFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.2f, 0.4f);
        Tag(ci, x.P(0.4f, 0.9f), "접안", 3, WarnYellow.WithAlpha(0.8f));
    }

    // ─────────────── 망원경 ───────────────

    private static void ScopeBody(in Fix x)
    {
        var ci = x.Ci;
        var mount = x.P(0.5f, 0.62f);
        for (int k = 0; k < 3; k++) Line(ci, mount, mount + Vector2.FromAngle(Mathf.Pi * 0.5f + (k - 1) * 0.9f) * 9f, Steel4, 1f); // 세 다리
        ci.DrawCircle(mount, 1.8f, Steel3); // 받침
        var a = mount + new Vector2(-6f, 4f);
        var b = mount + new Vector2(7f, -6f);
        Line(ci, a, b, GScope, 3.6f); // 경통
        Ring(ci, b, 2.2f, Steel3, 1f, 12); // 대물렌즈 덮개
        Line(ci, a + new Vector2(1f, -3f), a + new Vector2(5f, -6f), GScope.Darkened(0.2f), 1.2f); // 파인더
        Dot(ci, a, 1.2f, Rubber); // 접안렌즈
        if (x.Tier >= 2) Box(ci, new Rect2(mount - new Vector2(2.6f, 0f), new Vector2(5.2f, 3f)), Steel2, 0.5f); // II: 모터 받침
        if (x.Tier >= 3) Box(ci, new Rect2(a - new Vector2(2.4f, 1f), new Vector2(3f, 3f)), new Color("#1a1a1e"), 0.5f); // III: 사진기
    }

    private static void ScopeLife(in Fix x)
    {
        var ci = x.Ci;
        var mount = x.P(0.5f, 0.62f);
        float track = x.On ? Mathf.Sin(x.T * 0.05f + x.Id) * 0.12f : 0f; // 별을 따라 천천히 돈다
        var tip = mount + new Vector2(7f, -6f).Rotated(track);
        Dot(ci, tip + new Vector2(1.6f, -1.6f), 0.5f + 0.4f * Pulse(x.T, 3f), Colors.White.WithAlpha(0.7f * x.Glow)); // 별빛
        if (x.User != null) Dot(ci, mount + new Vector2(-6f, 4f), 1.4f, new Color("#9ac8ff").WithAlpha(0.6f)); // 들여다보는 눈
    }

    private static void ScopeFine(in Fix x)
    {
        var ci = x.Ci;
        var mount = x.P(0.5f, 0.62f);
        Knob(ci, mount + new Vector2(-2f, 1f), 0.8f, 0.4f, Chrome); // 초점 손잡이
        Ring(ci, mount + new Vector2(7f, -6f), 2.8f, GScope.WithAlpha(0.5f), 0.4f, 12); // 이슬막이
    }
}
