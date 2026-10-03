using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 압축-마 그림 표 — 조리 · 생태 · 배수 · 재활용 (15종).
/// 발효 항아리(옹기 · 물막이 홈 · 기포관 — 김이 차면 뚜껑이 들썩) · 빵 화덕(벽돌 돔 · 아치 입 · 숯불) · 양념 선반(색 다른 병 열둘)
/// · 제빙기(투명 통 속 얼음 · 떨어지는 조각) · 화분 선반(고사리 · 다육 · 덩굴) · 고양이 탑(삼줄 기둥 · 둥지 · 흔들 공 — 고양이가 자면 웅크린 모양)
/// · 벌레 덫(노란 끈끈이 · 보라 등 · 잡힌 점) · 귀뚜라미 사육장(그물 상자 · 달걀판 · 뛰는 점) · 기름 거름통(층진 들여다보기 창)
/// · 쓰레기 압축기(유압 기둥 · 빗금 누름판) · 퇴비 통(나무 띠 드럼 · 손잡이) · 회색수 거름기(모래 · 숯 · 자갈 층 셋)
/// · 손잡이 줄(노랑 · 검정 손잡이 · 끈 고리 — 무게가 없으면 끈이 뜬다) · 화물 그물(마름모 그물 · 노란 조임띠) · 충격 좌석(다섯 점 띠 · 버클).
/// 단계(II~IV)마다 몸체 모양이 덧붙는다 (온도띠 · 유리창 · 스테인리스 겉통 …).
/// </summary>
public static partial class FixtureArt
{
    private static void GearArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.Fermenter] = new(CrockBody, CrockLife, CrockFine, Look.Leak, 0.5f, 0.9f);
        t[FurnitureType.BreadOven] = new(HearthBody, HearthLife, HearthFine, Look.Smoke, 0.5f, 0.2f);
        t[FurnitureType.SpiceRack] = new(SpiceBody, SpiceLife, SpiceFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.IceMaker] = new(IceBody, IceLife, IceFine, Look.Leak, 0.5f, 0.95f);
        t[FurnitureType.PlantRack] = new(PlantRackBody, PlantRackLife, PlantRackFine, Look.Flicker, 0.5f, 0.1f);
        t[FurnitureType.CatTower] = new(CatTowerBody, CatTowerLife, CatTowerFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.PestTrap] = new(TrapBody, TrapLife, TrapFine, Look.Flicker, 0.5f, 0.15f);
        t[FurnitureType.InsectFarm] = new(CricketBody, CricketLife, CricketFine, Look.Grind, 0.85f, 0.5f);
        t[FurnitureType.GreaseTrap] = new(GreaseBody, GreaseLife, GreaseFine, Look.Leak, 0.1f, 0.5f);
        t[FurnitureType.Compactor] = new(CompactorBody, CompactorLife, CompactorFine, Look.Grind, 0.5f, 0.3f);
        t[FurnitureType.Composter] = new(CompostBody, CompostLife, CompostFine, Look.Steam, 0.5f, 0.2f);
        t[FurnitureType.GreywaterFilter] = new(GreyBody, GreyLife, GreyFine, Look.Leak, 0.5f, 0.95f);
        t[FurnitureType.GrabRail] = new(RailBody, RailLife, RailFine, Look.Jam, 0.5f, 0.3f);
        t[FurnitureType.CargoNet] = new(NetBody, NetLife, NetFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.CrashSeat] = new(CrashSeatBody, CrashSeatLife, CrashSeatFine, Look.Jam, 0.5f, 0.6f);
    }

    private static readonly Color GClay = new("#8a4f2c"), GClayDark = new("#5a311b"), GGlaze = new("#b0703a"), GBrick = new("#9a4a32"), GBrickDark = new("#6a2e1e");
    private static readonly Color GIce = new("#dff4ff"), GSisal = new("#c8a46a"), GCarpet = new("#5a6a8a"), GTrapYellow = new("#e8d23a"), GUv = new("#a070ff");
    private static readonly Color GMesh = new("#6a7480"), GGrease = new("#d8b44a"), GSlat = new("#7a5a34"), GSand = new("#d8c08a"), GCharcoal = new("#2a2a2e"), GGravel = new("#8a8f96");
    private static readonly Color GHazard = new("#e0b64a"), GNet = new("#c8c0a8"), GStrap = new("#e0a020"), GSeat = new("#3a4250"), GGel = new("#5a6a7e");
    private static readonly Color[] GSpice = { new("#c0392b"), new("#e67e22"), new("#f1c40f"), new("#7a8a3a"), new("#6b3f22"), new("#d0d0c0"), new("#8e44ad"), new("#2a5a3a") };

    // ─────────────── 발효 항아리 ───────────────

    private static void CrockBody(in Fix x)
    {
        var ci = x.Ci;
        float r = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.46f;
        ci.DrawCircle(x.C + new Vector2(1.2f, 1.6f), r, Shadow); // 바닥 그림자
        if (x.Tier >= 4) Ring(ci, x.C, r + 1.2f, new Color("#c8d0d8"), 2.2f, 28); // IV: 스테인리스 겉통
        ci.DrawCircle(x.C, r, GClay); // 옹기 몸
        ci.DrawArc(x.C, r * 0.86f, -2.4f, -0.7f, 12, GGlaze.Lightened(0.25f), 1.4f, true); // 유약 광
        Ring(ci, x.C, r * 0.62f, GClayDark, 1.6f, 24); // 물막이 홈
        ci.DrawCircle(x.C, r * 0.5f, new Color("#6a4a2c")); // 나무 뚜껑
        Line(ci, x.C - new Vector2(r * 0.4f, 0f), x.C + new Vector2(r * 0.4f, 0f), new Color("#4a3018"), 1f); // 뚜껑 손잡이
        if (x.Tier >= 2) ci.DrawArc(x.C, r * 0.94f, 0.3f, 1.3f, 8, new Color("#d04a3a"), 1.6f, true); // II: 온도띠
        if (x.Tier >= 3) Box(ci, new Rect2(x.C + new Vector2(-r * 0.3f, r * 0.62f), new Vector2(r * 0.6f, r * 0.28f)), new Color("#2a3a2a").WithAlpha(0.8f), 1f, Chrome); // III: 들여다보기 창
    }

    private static void CrockLife(in Fix x)
    {
        var ci = x.Ci;
        float r = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.46f;
        float press = 0f;
        if (x.W != null && x.W.Fittings.Crock.TryGetValue(x.Id, out var pv)) press = pv;
        var tube = x.C + new Vector2(r * 0.18f, -r * 0.2f);
        ci.DrawPolyline(new[] { tube, tube + new Vector2(0f, -3f), tube + new Vector2(2f, -4f), tube + new Vector2(2f, -6f) }, new Color(1, 1, 1, 0.55f), 1f, true); // 기포관
        float rate = 0.4f + 2.2f * press;
        float q = Mathf.PosMod(x.T * rate, 1f);
        if (!x.Dead) Dot(ci, tube + new Vector2(q * 2f, -1f - q * 5f), 0.7f, new Color("#e8ffd8").WithAlpha(0.8f * (1f - q))); // 올라가는 기포
        if (press > 0.6f) // 김이 찼다: 뚜껑이 들썩
        {
            float jig = Mathf.Sin(x.T * 18f) * (press - 0.6f) * 2.2f;
            Ring(ci, x.C + new Vector2(0f, jig), r * 0.5f, new Color("#ffd27a").WithAlpha(0.5f), 1f, 18);
        }
        if (x.Tier >= 3 && x.Lod > 0) // 창 속 채소 층이 천천히 가라앉는다
            for (int k = 0; k < 3; k++) Dot(ci, x.C + new Vector2(-r * 0.2f + k * r * 0.2f, r * 0.76f + Mathf.Sin(x.T * 0.4f + k) * 0.5f), 0.7f, new Color("#c84a2a"));
    }

    private static void CrockFine(in Fix x)
    {
        var ci = x.Ci;
        float r = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.46f;
        for (int k = 0; k < 10; k++) Dot(ci, x.C + Vector2.FromAngle(Hash(x.Id, k, 901) * Mathf.Tau) * r * (0.66f + 0.3f * Hash(x.Id, k, 902)), 0.35f, GClayDark); // 옹기 숨구멍
        ci.DrawArc(x.C, r * 0.98f, 3.6f, 4.6f, 8, new Color("#d8c8a0"), 0.8f, true); // 새끼줄
        Tag(ci, x.C + new Vector2(0f, -r * 0.05f), "절임", 4, new Color("#f0e0c0").WithAlpha(0.8f));
    }

    // ─────────────── 빵 화덕 ───────────────

    private static void HearthBody(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        float r = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.48f;
        var dome = new Vector2[13];
        for (int k = 0; k <= 12; k++) dome[k] = x.C + f * r * 0.35f + Vector2.FromAngle(f.Angle() + Mathf.Pi * 0.5f + k * Mathf.Pi / 12f) * r;
        ci.DrawColoredPolygon(dome, GBrick); // 벽돌 돔
        for (int row = 1; row < 4; row++) // 벽돌 줄
            ci.DrawArc(x.C + f * r * 0.35f, r * row / 4f, f.Angle() + Mathf.Pi * 0.5f, f.Angle() + Mathf.Pi * 1.5f, 10, GBrickDark, 0.8f, true);
        var mouth = x.C + f * r * 0.35f;
        ci.DrawArc(mouth, r * 0.42f, f.Angle() + Mathf.Pi * 0.5f, f.Angle() + Mathf.Pi * 1.5f, 10, new Color("#140c08"), r * 0.3f, true); // 아치 입
        Pipe(ci, x.C - f * r * 0.55f + side * r * 0.4f, x.C - f * r * 0.95f + side * r * 0.4f, 3f, Steel3, false); // 굴뚝
        Line(ci, x.C + f * r * 0.5f - side * r * 0.9f, x.C - f * r * 0.6f - side * r * 0.75f, new Color("#a8844f"), 1.4f); // 기댄 빵 삽
        Box(ci, new Rect2(x.C + f * r * 0.5f - side * r * 0.9f - new Vector2(2f, 2f), new Vector2(4f, 4f)), new Color("#c8a46a"), 1f);
        if (x.Tier >= 2) Glass(ci, new Rect2(mouth - new Vector2(r * 0.25f, r * 0.25f), new Vector2(r * 0.5f, r * 0.5f)), new Color("#ffb070"), 1f); // II: 유리문
        if (x.Tier >= 3) Gauge(ci, x.C - f * r * 0.1f - side * r * 0.45f, 2.6f, 0.7f, Danger); // III: 온도계
    }

    private static void HearthLife(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        float r = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.48f;
        var mouth = x.C + f * r * 0.35f;
        bool baking = x.User != null;
        float heat = x.Dead ? 0f : x.Glow * (baking ? 1f : 0.45f);
        for (int k = 0; k < 4; k++) // 숯불
        {
            float fl = 0.6f + 0.4f * Mathf.Sin(x.T * (5f + k) + k * 1.7f);
            Dot(ci, mouth - f * 2f + new Vector2(-f.Y, f.X) * (k - 1.5f) * 2f, 1.2f, Ember.WithAlpha(heat * fl));
        }
        if (baking && x.Lod > 0) // 굽는 빵 둘
            for (int k = 0; k < 2; k++) ci.DrawCircle(mouth - f * 3.5f + new Vector2(-f.Y, f.X) * (k - 0.5f) * 4f, 1.6f, new Color("#d8a050").Lerp(new Color("#8a5020"), Mathf.PosMod(x.T * 0.05f, 1f)));
        if (heat > 0.3f && x.Lod > 0) // 굴뚝 아지랑이
            for (int k = 0; k < 3; k++) { float q = Mathf.PosMod(x.T * 0.5f + k / 3f, 1f); Dot(ci, x.C - f * (r * 1f + q * 6f), 1f + q, new Color(1, 0.9f, 0.8f, 0.15f * (1f - q))); }
    }

    private static void HearthFine(in Fix x)
    {
        var ci = x.Ci;
        float r = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.48f;
        for (int k = 0; k < 8; k++) Dot(ci, x.C + Vector2.FromAngle(Hash(x.Id, k, 911) * Mathf.Tau) * r * 0.75f, 0.35f, new Color("#d8c8b0").WithAlpha(0.6f)); // 줄눈
        Bolt(ci, x.C + x.Front * r * 0.8f, 0.7f);
    }

    // ─────────────── 양념 선반 ───────────────

    private static void SpiceBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#3a2a1c"), 1f, Wood, 1);
        for (int row = 0; row < 3; row++) ci.DrawRect(x.Q(0.04f, 0.3f + row * 0.31f, 0.96f, 0.33f + row * 0.31f), Wood.Lightened(0.15f)); // 선반 턱
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4; i++)
            {
                var p = x.P(0.14f + i * 0.24f, 0.17f + row * 0.31f);
                var col = GSpice[(row * 4 + i + x.Id) % GSpice.Length];
                Box(ci, new Rect2(p - new Vector2(2.2f, 2.6f), new Vector2(4.4f, 5.2f)), new Color(1, 1, 1, 0.18f), 1f); // 병
                ci.DrawRect(new Rect2(p - new Vector2(1.8f, 0.6f), new Vector2(3.6f, 3f)), col); // 가루
                ci.DrawRect(new Rect2(p - new Vector2(2.2f, 3.2f), new Vector2(4.4f, 1.2f)), row == 1 ? Brass : new Color("#c0c4c8")); // 뚜껑
            }
        if (x.Tier >= 2) Line(ci, x.P(0.05f, 0.95f), x.P(0.95f, 0.95f), Chrome, 1f); // II: 국자 걸이
        if (x.Tier >= 2) Can(ci, x.P(0.85f, 0.97f), 1.5f, Chrome, Steel4);
    }

    private static void SpiceLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.User is CrewMember u) // 쓰는 중: 병 하나가 빠져 있고 향이 오른다
        {
            int slot = (u.Id + (int)(x.T * 0.1f)) % 12;
            var p = x.P(0.14f + slot % 4 * 0.24f, 0.17f + slot / 4 * 0.31f);
            ci.DrawRect(new Rect2(p - new Vector2(2.2f, 3.2f), new Vector2(4.4f, 6.4f)), new Color("#1a120c"));
            for (int k = 0; k < 3; k++) { float q = Mathf.PosMod(x.T * 0.7f + k / 3f, 1f); Dot(ci, p + new Vector2(Mathf.Sin(q * 6f + k) * 1.5f, -q * 7f), 0.6f, GSpice[k].WithAlpha(0.7f * (1f - q))); }
        }
        else if (x.Lod == 2) Dot(ci, x.P(0.5f, 0.05f), 0.5f, Colors.White.WithAlpha(0.15f + 0.1f * Mathf.Sin(x.T)));
    }

    private static void SpiceFine(in Fix x)
    {
        var ci = x.Ci;
        for (int i = 0; i < 4; i++) Line(ci, x.P(0.1f + i * 0.24f, 0.23f), x.P(0.18f + i * 0.24f, 0.23f), Cream.WithAlpha(0.6f), 0.5f); // 손글씨 딱지
        Bolts(ci, x.B, 1.5f, 0.4f);
    }

    // ─────────────── 제빙기 ───────────────

    private static void IceBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#e8eef4"), 3f, new Color("#9aa8b8"), 1);
        Grille(ci, x.Q(0.1f, 0.06f, 0.9f, 0.2f), 2.2f, new Color("#9aa8b8"), 0.8f); // 위 송풍구
        var bin = x.Q(0.12f, 0.3f, 0.88f, 0.82f);
        Glass(ci, bin, new Color("#b8e0f8"), 2f); // 투명 통
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 2; j++)
            {
                var c = bin.Position + new Vector2(2.5f + i * bin.Size.X / 4.2f, bin.Size.Y * (0.45f + j * 0.28f));
                ci.DrawRect(new Rect2(c, new Vector2(3f, 3f)), GIce); // 얼음 조각
                Line(ci, c, c + new Vector2(2f, 0f), Colors.White, 0.5f);
            }
        Stripes(ci, x.Q(0.12f, 0.86f, 0.88f, 0.95f), 2f); // 물받이
        if (x.Tier >= 2) Can(ci, x.P(0.95f, 0.5f), 2f, new Color("#4aa3e0"), Chrome); // II: 정수 필터 통
        if (x.Tier >= 3) Box(ci, x.Q(0.4f, 0.2f, 0.6f, 0.3f), Steel3, 1f); // III: 받아 가는 홈통
    }

    private static void IceLife(in Fix x)
    {
        var ci = x.Ci;
        var bin = x.Q(0.12f, 0.3f, 0.88f, 0.82f);
        if (x.On)
        {
            float q = Mathf.PosMod(x.T * 0.3f * x.Spin + Hash(x.Id, 1, 920), 1f);
            if (q < 0.4f) ci.DrawRect(new Rect2(bin.Position + new Vector2(bin.Size.X * 0.5f - 1.5f, q * bin.Size.Y), new Vector2(3f, 3f)), GIce); // 떨어지는 조각
            if (x.Lod > 0) for (int k = 0; k < 3; k++) Dot(ci, bin.Position + new Vector2(Hash(x.Id, k, 921) * bin.Size.X, Hash(x.Id, k, 922) * bin.Size.Y), 0.5f, Colors.White.WithAlpha(0.6f * Pulse(x.T + k, 2f))); // 서리 반짝
        }
        else if (x.Dead) ci.DrawCircle(x.P(0.5f, 1.05f), 2f + Mathf.PosMod(x.T * 0.05f, 2f), LeakBlue.WithAlpha(0.35f)); // 꺼지면 녹아 고인다
    }

    private static void IceFine(in Fix x)
    {
        var ci = x.Ci;
        var s = x.P(0.82f, 0.13f);
        for (int k = 0; k < 3; k++) { var d = Vector2.FromAngle(k * Mathf.Pi / 3f) * 1.6f; Line(ci, s - d, s + d, new Color("#4aa3e0"), 0.5f); } // 눈송이 딱지
        Bolts(ci, x.B, 1.4f, 0.4f);
    }

    // ─────────────── 화분 선반 ───────────────

    private static void PlantRackBody(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++) ci.DrawRect(x.Q(0.04f, 0.28f + k * 0.32f, 0.96f, 0.32f + k * 0.32f), Steel4); // 선반 판 셋
        Line(ci, x.P(0.04f, 0.02f), x.P(0.04f, 0.98f), Steel3, 1.4f);
        Line(ci, x.P(0.96f, 0.02f), x.P(0.96f, 0.98f), Steel3, 1.4f);
        for (int k = 0; k < 3; k++)
        {
            var pot = x.P(0.2f + k * 0.3f, 0.2f + k * 0.32f * 0.3f);
            Box(ci, new Rect2(pot - new Vector2(2.6f, 1f), new Vector2(5.2f, 3.4f)), new Color("#a8583a"), 1f); // 화분
            switch ((k + x.Id) % 3)
            {
                case 0: for (int i = 0; i < 5; i++) Line(ci, pot, pot + Vector2.FromAngle(-Mathf.Pi * (0.15f + i * 0.17f)) * 5f, Leaf, 0.9f); break; // 고사리
                case 1: for (int i = 0; i < 6; i++) Dot(ci, pot + Vector2.FromAngle(i * Mathf.Tau / 6f) * 1.6f - new Vector2(0f, 1.5f), 1.1f, LeafLight); break; // 다육
                default: ci.DrawPolyline(new[] { pot, pot + new Vector2(3f, 2f), pot + new Vector2(4f, 6f), pot + new Vector2(2f, 9f) }, Leaf, 0.9f, true); break; // 덩굴
            }
        }
        Pipe(ci, x.P(0.04f, 0.04f), x.P(0.96f, 0.04f), 1.2f, new Color("#4a7a9a"), false); // 물 대는 줄
        if (x.Tier >= 2) for (int k = 0; k < 3; k++) Line(ci, x.P(0.12f + k * 0.3f, 0.12f), x.P(0.28f + k * 0.3f, 0.3f), new Color("#3a8a4a"), 0.9f); // II: 고정 끈
        if (x.Tier >= 3) Box(ci, x.Q(0.08f, 0.0f, 0.92f, 0.06f), new Color("#c8a0ff").WithAlpha(0.6f), 1f); // III: 생장등 막대
    }

    private static void PlantRackLife(in Fix x)
    {
        var ci = x.Ci;
        bool floating = x.W != null && x.W.ZeroG.Weightless;
        for (int k = 0; k < 3; k++)
        {
            var pot = x.P(0.2f + k * 0.3f, 0.2f + k * 0.32f * 0.3f);
            float sway = floating ? -2f : Mathf.Sin(x.T * 0.8f + k * 1.3f + x.Id) * 0.8f;
            Dot(ci, pot + new Vector2(sway, -4.5f), 0.9f, LeafLight.WithAlpha(0.8f)); // 끝 잎이 흔들린다 · 무게가 없으면 위로
        }
        if (x.Tier >= 3 && x.Lit) ci.DrawRect(x.Q(0.08f, 0.06f, 0.92f, 0.26f), new Color("#c8a0ff").WithAlpha(0.12f * x.Glow)); // 생장등 빛
        if (x.User != null && x.Lod > 0) Dot(ci, x.P(0.5f, 0.06f + Mathf.PosMod(x.T, 1f) * 0.2f), 0.6f, WaterLight); // 물 주는 방울
    }

    private static void PlantRackFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++) Dot(ci, x.P(0.2f + k * 0.3f, 0.06f), 0.5f, WaterLight.WithAlpha(0.7f)); // 점적 꼭지
        Bolts(ci, x.B, 1.2f, 0.35f);
    }

    // ─────────────── 고양이 탑 ───────────────

    private static void CatTowerBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, GCarpet, 2f, GCarpet.Darkened(0.3f), 1); // 카펫 받침
        var post = x.Q(0.42f, 0.15f, 0.58f, 0.85f);
        ci.DrawRect(post, GSisal); // 삼줄 기둥
        for (float y = post.Position.Y; y < post.End.Y; y += 1.6f) Line(ci, new Vector2(post.Position.X, y), new Vector2(post.End.X, y + 1.2f), GSisal.Darkened(0.25f), 0.6f);
        ci.DrawCircle(x.P(0.25f, 0.3f), 4.2f, GCarpet.Lightened(0.15f)); // 위 발판
        ci.DrawCircle(x.P(0.75f, 0.7f), 3.8f, GCarpet.Lightened(0.1f)); // 아래 발판
        Box(ci, new Rect2(x.P(0.68f, 0.12f) - new Vector2(4f, 3f), new Vector2(8f, 6f)), GCarpet.Darkened(0.15f), 1.5f); // 숨는 집
        ci.DrawCircle(x.P(0.68f, 0.12f), 1.8f, new Color("#101418")); // 둥근 입구
        if (x.Tier >= 2) ci.DrawArc(x.P(0.25f, 0.7f), 3.4f, 0f, Mathf.Pi, 10, new Color("#c8b48a"), 1.4f, true); // II: 해먹
        if (x.Tier >= 3) ci.DrawRect(x.Q(0.12f, 0.45f, 0.2f, 0.95f), GSisal.Darkened(0.1f)); // III: 둘째 기둥
    }

    private static void CatTowerLife(in Fix x)
    {
        var ci = x.Ci;
        bool napping = false;
        if (x.W?.Eco.Cat is ShipCat cat && cat.Alive && cat.RoomId == x.F.Room.Id && cat.State == CatState.Nap) napping = true;
        var top = x.P(0.25f, 0.3f);
        if (napping) // 웅크려 자는 고양이 (숨 쉰다)
        {
            float br = 1f + 0.06f * Mathf.Sin(x.T * 1.6f);
            ci.DrawCircle(top, 3.2f * br, new Color("#d08a3a"));
            ci.DrawArc(top, 3.6f, 0.3f, 2.2f, 8, new Color("#a0602a"), 1.2f, true); // 꼬리
            ci.DrawColoredPolygon(new[] { top + new Vector2(-2.4f, -2.2f), top + new Vector2(-1.6f, -4f), top + new Vector2(-0.8f, -2.6f) }, new Color("#d08a3a")); // 귀
        }
        else // 흔들 공
        {
            var hook = x.P(0.25f, 0.3f) + new Vector2(3f, 0f);
            float a = Mathf.Pi * 0.5f + Mathf.Sin(x.T * 2.1f + x.Id) * 0.5f;
            var ball = hook + Vector2.FromAngle(a) * 5f;
            Line(ci, hook, ball, Cream.WithAlpha(0.7f), 0.5f);
            Dot(ci, ball, 1.2f, Danger);
        }
    }

    private static void CatTowerFine(in Fix x)
    {
        var ci = x.Ci;
        var post = x.Q(0.42f, 0.15f, 0.58f, 0.85f);
        for (int k = 0; k < 3; k++) Line(ci, post.Position + new Vector2(1f + k, 4f), post.Position + new Vector2(1.5f + k, 8f), new Color("#f0e0b8"), 0.4f); // 발톱 자국
        for (int k = 0; k < 6; k++) Dot(ci, x.P(Hash(x.Id, k, 931), 0.9f + 0.08f * Hash(x.Id, k, 932)), 0.4f, GCarpet.Lightened(0.3f)); // 털
    }

    // ─────────────── 벌레 덫 ───────────────

    private static void TrapBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.1f, 0.0f, 0.9f, 0.2f), Steel2, 1.5f, Steel4); // 등 집
        ci.DrawRect(x.Q(0.18f, 0.08f, 0.82f, 0.14f), GUv.Darkened(0.4f)); // 보라 관
        var card = x.Q(0.15f, 0.26f, 0.85f, 0.94f);
        ci.DrawRect(card, GTrapYellow); // 끈끈이 판
        for (int i = 1; i < 5; i++) Line(ci, card.Position + new Vector2(card.Size.X * i / 5f, 0f), card.Position + new Vector2(card.Size.X * i / 5f, card.Size.Y), new Color("#8a7a1a").WithAlpha(0.5f), 0.5f);
        for (int j = 1; j < 5; j++) Line(ci, card.Position + new Vector2(0f, card.Size.Y * j / 5f), card.Position + new Vector2(card.Size.X, card.Size.Y * j / 5f), new Color("#8a7a1a").WithAlpha(0.5f), 0.5f);
        Can(ci, x.P(0.92f, 0.6f), 1.6f, new Color("#e8f0e8"), Chrome); // 냄새 병
        if (x.Tier >= 2) Box(ci, x.Q(0.6f, 0.0f, 0.86f, 0.2f), new Color("#0b1f29"), 1f); // II: 세는 화면
        if (x.Tier >= 3) ci.DrawCircle(x.P(0.08f, 0.1f), 1.6f, new Color("#101418")); // III: 눈
    }

    private static void TrapLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.Lit) ci.DrawRect(x.Q(0.18f, 0.08f, 0.82f, 0.14f), GUv.WithAlpha(0.5f + 0.3f * Mathf.Sin(x.T * 7f) * x.Glow)); // 보라 등
        int caught = 2;
        if (x.W != null) foreach (var p in x.W.Eco.Pests) if (p.RoomId == x.F.Room.Id) caught += (int)(p.Pop * 20f);
        if (caught > 14) caught = 14;
        var card = x.Q(0.15f, 0.26f, 0.85f, 0.94f);
        for (int k = 0; k < caught; k++) Dot(ci, card.Position + new Vector2(Hash(x.Id, k, 941) * card.Size.X, Hash(x.Id, k, 942) * card.Size.Y), 0.55f, new Color("#2a1a0a")); // 잡힌 벌레
        if (x.Tier >= 2 && x.Lit && x.Lod > 0) Dot(ci, x.P(0.73f, 0.1f), 0.8f, Good.WithAlpha(0.6f * x.Glow));
    }

    private static void TrapFine(in Fix x)
    {
        var ci = x.Ci;
        Bolt(ci, x.P(0.12f, 0.1f), 0.5f);
        Bolt(ci, x.P(0.88f, 0.1f), 0.5f);
        Line(ci, x.P(0.15f, 0.26f), x.P(0.85f, 0.26f), Chrome, 0.6f); // 집게
    }

    // ─────────────── 귀뚜라미 사육장 ───────────────

    private static void CricketBody(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++)
        {
            var box = x.Q(0.04f + k * 0.31f, 0.1f, 0.31f + k * 0.31f, 0.78f);
            Box(ci, box, new Color("#2a2e34"), 1f, GMesh);
            Grille(ci, box.Grow(-1.5f), 1.6f, GMesh.WithAlpha(0.6f), 0.4f); // 그물
            for (int i = 0; i < 3; i++) ci.DrawArc(box.GetCenter() + new Vector2((i - 1) * 2.4f, 2f), 1.2f, Mathf.Pi, Mathf.Tau, 6, new Color("#b8a888"), 1f, true); // 달걀판
        }
        Box(ci, x.Q(0.1f, 0.82f, 0.9f, 0.96f), new Color("#6a5a3a"), 1f); // 먹이 판
        if (x.Tier >= 2) Line(ci, x.P(0.02f, 0.06f), x.P(0.98f, 0.06f), new Color("#4aa3e0"), 1f); // II: 안개 관
        if (x.Tier >= 3) Box(ci, x.Q(0.4f, 0.0f, 0.6f, 0.08f), Steel3, 1f); // III: 먹이 깔때기
    }

    private static void CricketLife(in Fix x)
    {
        var ci = x.Ci;
        int n = x.Dead ? 2 : 7;
        for (int k = 0; k < n; k++) // 뛰는 귀뚜라미
        {
            int box = k % 3;
            float ph = Mathf.PosMod(x.T * (0.7f + 0.3f * Hash(x.Id, k, 951)) + k * 0.37f, 1f);
            float hop = ph < 0.2f ? Mathf.Sin(ph / 0.2f * Mathf.Pi) * 2f : 0f;
            var p = x.P(0.08f + box * 0.31f + 0.2f * Hash(x.Id, k + (int)(x.T * 0.3f), 952), 0.3f + 0.4f * Hash(x.Id, k, 953));
            Dot(ci, p - new Vector2(0f, hop), 0.6f, new Color("#3a2a1a"));
        }
        if (x.On && x.Lod > 0) Fan(ci, x.P(0.97f, 0.9f), 1.8f, 3, x.Ang(10f), Steel4, 0.6f);
    }

    private static void CricketFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++) Tag(ci, x.P(0.17f + k * 0.31f, 0.86f), (k + 1).ToString(), 4, Cream.WithAlpha(0.7f));
        Bolts(ci, x.B, 1f, 0.35f);
    }

    // ─────────────── 기름 거름통 ───────────────

    private static void GreaseBody(in Fix x)
    {
        var ci = x.Ci;
        var body = x.Q(0.12f, 0.2f, 0.88f, 0.8f);
        Box(ci, body, Steel3, 1.5f, Steel4);
        Bolts(ci, body, 1.4f, 0.5f); // 뚜껑 볼트
        Pipe(ci, x.P(0.0f, 0.5f), x.P(0.12f, 0.5f), 2.4f, Steel4, true); // 들어오는 관
        Pipe(ci, x.P(0.88f, 0.4f), x.P(1.0f, 0.4f), 2.4f, Steel4, true); // 나가는 관
        var win = x.P(0.5f, 0.5f);
        ci.DrawCircle(win, 3.4f, new Color("#3a3f44")); // 들여다보기 창
        Ring(ci, win, 3.6f, Chrome, 0.8f, 18);
        Stripes(ci, x.Q(0.12f, 0.82f, 0.88f, 0.9f), 2.2f);
        if (x.Tier >= 2) Gauge(ci, x.P(0.78f, 0.3f), 2f, 0.4f, Amber); // II: 뜨개 눈금
        if (x.Tier >= 3) Line(ci, x.P(0.2f, 0.25f), x.P(0.8f, 0.25f), Copper, 1.2f); // III: 데우는 걷개
    }

    private static void GreaseLife(in Fix x)
    {
        var ci = x.Ci;
        float clog = 0.2f;
        if (x.W != null) foreach (var d in x.W.Drains.Drains) if (d.RoomId == x.F.Room.Id && d.Clog > clog) clog = d.Clog;
        var win = x.P(0.5f, 0.5f);
        float lvl = 3f - clog * 5f; // 기름 층이 두꺼울수록 위로 찬다
        ci.DrawRect(new Rect2(win + new Vector2(-2.6f, lvl - 1.6f), new Vector2(5.2f, Mathf.Max(0.6f, 2.6f - lvl * 0.4f))), GGrease.WithAlpha(0.8f));
        if (x.User != null || x.On && x.Lod > 0) { float q = Mathf.PosMod(x.T * 0.6f, 1f); Dot(ci, win + new Vector2(-1f + q * 2f, 2f - q * 3f), 0.5f, Colors.White.WithAlpha(0.5f * (1f - q))); }
    }

    private static void GreaseFine(in Fix x)
    {
        var ci = x.Ci;
        Line(ci, x.P(0.35f, 0.2f), x.P(0.65f, 0.2f), Chrome, 1f); // 손잡이
        Tag(ci, x.P(0.3f, 0.7f), "기름", 4, GGrease);
    }

    // ─────────────── 쓰레기 압축기 ───────────────

    private static void CompactorBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, Steel1, 1f, Steel3, 2);
        ci.DrawRect(x.Q(0.18f, 0.06f, 0.26f, 0.5f), Chrome); // 유압 기둥
        ci.DrawRect(x.Q(0.74f, 0.06f, 0.82f, 0.5f), Chrome);
        var plate = x.Q(0.1f, 0.48f, 0.9f, 0.58f);
        ci.DrawRect(plate, GHazard);
        Stripes(ci, plate, 2.4f); // 빗금 누름판
        Box(ci, x.Q(0.14f, 0.64f, 0.86f, 0.94f), Steel2, 1f, Steel4); // 아래 문
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.2f, 0.7f + k * 0.08f), x.P(0.8f, 0.7f + k * 0.08f), new Color("#8a7a5a"), 0.8f); // 묶인 덩어리
        if (x.Tier >= 2) Line(ci, x.P(0.5f, 0.64f), x.P(0.5f, 0.94f), new Color("#c8c8c8"), 0.8f); // II: 철사 묶기
        if (x.Tier >= 3) ci.DrawColoredPolygon(new[] { x.P(0.3f, 0.0f), x.P(0.7f, 0.0f), x.P(0.6f, 0.06f), x.P(0.4f, 0.06f) }, Steel3); // III: 투입구
    }

    private static void CompactorLife(in Fix x)
    {
        var ci = x.Ci;
        bool crushing = x.User != null || x.St == State.Running && Mathf.PosMod(x.T * 0.05f + x.Id, 1f) < 0.15f;
        float drop = crushing ? 0.5f + 0.5f * Mathf.Sin(x.Ang(3f)) : 0f;
        var plate = x.Q(0.1f, 0.48f + drop * 0.08f, 0.9f, 0.58f + drop * 0.08f);
        if (crushing) { ci.DrawRect(plate, GHazard.Lightened(0.15f)); Led(ci, x.P(0.5f, 0.03f), Amber, 0.5f + 0.5f * Mathf.Sin(x.T * 9f), 1.2f); }
        else Led(ci, x.P(0.5f, 0.03f), x.Lit ? Good : Danger, 0.4f * x.Glow + 0.1f, 1f);
    }

    private static void CompactorFine(in Fix x)
    {
        var ci = x.Ci;
        Cable(ci, x.P(0.22f, 0.5f), x.P(0.05f, 0.95f), 2f, Rubber, 0.8f); // 유압 호스
        Bolts(ci, x.B, 1.4f, 0.4f);
    }

    // ─────────────── 퇴비 통 ───────────────

    private static void CompostBody(in Fix x)
    {
        var ci = x.Ci;
        var drum = x.Q(0.1f, 0.18f, 0.9f, 0.78f);
        Box(ci, drum, GSlat, 3f, GSlat.Darkened(0.3f)); // 나무 드럼
        for (int k = 1; k < 6; k++) Line(ci, x.P(0.1f + k * 0.8f / 6f, 0.2f), x.P(0.1f + k * 0.8f / 6f, 0.76f), GSlat.Darkened(0.35f), 0.7f); // 띠
        ci.DrawCircle(x.P(0.1f, 0.48f), 2.6f, Steel3); // 축
        Line(ci, x.P(0.1f, 0.48f), x.P(0.0f, 0.3f), Chrome, 1.2f); // 손잡이
        Dot(ci, x.P(0.0f, 0.3f), 1f, Rubber);
        Line(ci, x.P(0.2f, 0.78f), x.P(0.15f, 0.98f), Steel4, 1.2f); // 다리
        Line(ci, x.P(0.8f, 0.78f), x.P(0.85f, 0.98f), Steel4, 1.2f);
        if (x.Tier >= 2) ci.DrawRect(x.Q(0.1f, 0.18f, 0.9f, 0.24f), new Color("#4a5a3a")); // II: 보온 덮개
        if (x.Tier >= 3) Pipe(ci, x.P(0.5f, 0.18f), x.P(0.5f, 0.02f), 1.4f, Copper, false); // III: 숨구멍 관
    }

    private static void CompostLife(in Fix x)
    {
        var ci = x.Ci;
        bool turning = x.User != null;
        float off = turning ? Mathf.PosMod(x.T * 3f, 1f) : 0f;
        for (int k = 0; k < 6; k++) // 돌아가는 띠
        {
            float u = 0.1f + Mathf.PosMod((k + off) / 6f, 1f) * 0.8f;
            Line(ci, x.P(u, 0.22f), x.P(u, 0.74f), GSlat.Lightened(0.15f).WithAlpha(turning ? 0.6f : 0.15f), 0.6f);
        }
        if (!x.Dead && x.Lod > 0) // 김
            for (int k = 0; k < 2; k++) { float q = Mathf.PosMod(x.T * 0.3f + k * 0.5f, 1f); Dot(ci, x.P(0.4f + k * 0.2f, 0.16f) - new Vector2(0f, q * 6f), 1f + q, SteamWhite.WithAlpha(0.2f * (1f - q))); }
    }

    private static void CompostFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 1; k < 6; k++) { Dot(ci, x.P(0.1f + k * 0.8f / 6f, 0.24f), 0.35f, Chrome); Dot(ci, x.P(0.1f + k * 0.8f / 6f, 0.72f), 0.35f, Chrome); } // 못
        Gauge(ci, x.P(0.78f, 0.48f), 1.4f, 0.6f, Ember);
    }

    // ─────────────── 회색수 거름기 ───────────────

    private static void GreyBody(in Fix x)
    {
        var ci = x.Ci;
        Pipe(ci, x.P(0.05f, 0.08f), x.P(0.95f, 0.08f), 2f, Steel4, true); // 위 모음관
        var layers = new[] { GSand, GCharcoal, GGravel };
        for (int k = 0; k < 3; k++)
        {
            var col = x.Q(0.08f + k * 0.3f, 0.14f, 0.3f + k * 0.3f, 0.86f);
            Glass(ci, col, new Color("#9ad0ff"), 2f);
            ci.DrawRect(new Rect2(col.Position + new Vector2(1f, col.Size.Y * 0.55f), new Vector2(col.Size.X - 2f, col.Size.Y * 0.42f)), layers[k]); // 거름층
            Line(ci, x.P(0.19f + k * 0.3f, 0.08f), x.P(0.19f + k * 0.3f, 0.14f), Steel4, 1.2f);
        }
        Pipe(ci, x.P(0.05f, 0.92f), x.P(0.95f, 0.92f), 1.6f, Steel3, false); // 아래 관
        Knob(ci, x.P(0.95f, 0.92f), 1.4f, 0.8f, Danger); // 빼는 꼭지
        if (x.Tier >= 2) ci.DrawRect(x.Q(0.0f, 0.2f, 0.06f, 0.8f), new Color("#a070ff").WithAlpha(0.5f)); // II: 자외선 관
        if (x.Tier >= 3) Box(ci, x.Q(0.94f, 0.2f, 1.0f, 0.8f), Cream, 1f); // III: 막 통
    }

    private static void GreyLife(in Fix x)
    {
        var ci = x.Ci;
        float clear = x.W != null ? x.W.Flow.WaterQuality : 1f;
        var tint = new Color("#6a7a5a").Lerp(new Color("#9ad0ff"), clear);
        for (int k = 0; k < 3; k++)
        {
            var col = x.Q(0.08f + k * 0.3f, 0.14f, 0.3f + k * 0.3f, 0.86f);
            float lvl = 0.2f + 0.15f * Mathf.Sin(x.T * 0.4f * x.Spin + k * 1.1f);
            ci.DrawRect(new Rect2(col.Position + new Vector2(1f, col.Size.Y * lvl), new Vector2(col.Size.X - 2f, col.Size.Y * (0.55f - lvl))), tint.WithAlpha(0.45f)); // 물
            if (x.On && x.Lod > 0) { float q = Mathf.PosMod(x.T * 0.8f + k * 0.3f, 1f); Dot(ci, col.Position + new Vector2(col.Size.X * 0.5f, col.Size.Y * (0.55f - q * 0.4f)), 0.5f, Colors.White.WithAlpha(0.5f * (1f - q))); }
        }
    }

    private static void GreyFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++) for (int i = 0; i < 4; i++) Line(ci, x.P(0.09f + k * 0.3f, 0.3f + i * 0.12f), x.P(0.12f + k * 0.3f, 0.3f + i * 0.12f), Colors.White.WithAlpha(0.4f), 0.4f); // 눈금
        Gauge(ci, x.P(0.5f, 0.04f), 1.4f, 0.5f, Good);
    }

    // ─────────────── 손잡이 줄 ───────────────

    private static void RailBody(in Fix x)
    {
        var ci = x.Ci;
        var a = x.P(0.04f, 0.3f);
        var b = x.P(0.96f, 0.3f);
        Line(ci, a, b, Steel4, 3f); // 막대
        for (int k = 0; k < 5; k++) // 노랑 · 검정 손잡이
        {
            var p = x.P(0.12f + k * 0.19f, 0.3f);
            ci.DrawRect(new Rect2(p - new Vector2(2f, 1.8f), new Vector2(4f, 3.6f)), k % 2 == 0 ? WarnYellow : WarnBlack);
        }
        Dot(ci, a, 1.6f, Steel3); // 벽 받침
        Dot(ci, b, 1.6f, Steel3);
        if (x.Tier >= 2) Line(ci, x.P(0.04f, 0.7f), x.P(0.96f, 0.7f), Steel4, 2f); // II: 둘째 막대
        if (x.Tier >= 3) Line(ci, x.P(0.1f, 0.3f), x.P(0.1f, 0.7f), new Color("#3a8a4a"), 1f); // III: 고무줄
        if (x.Tier >= 4) Line(ci, x.P(0.04f, 0.22f), x.P(0.96f, 0.22f), Cyan.WithAlpha(0.6f), 1f); // IV: 빛 띠
    }

    private static void RailLife(in Fix x)
    {
        var ci = x.Ci;
        bool floating = x.W != null && x.W.ZeroG.Weightless;
        for (int k = 0; k < 3; k++) // 끈 고리 (무게가 없으면 곧게 떠오른다)
        {
            var top = x.P(0.2f + k * 0.3f, 0.3f);
            var end = floating ? top + new Vector2(Mathf.Sin(x.T * 0.7f + k) * 2f, -6f) : top + new Vector2(Mathf.Sin(x.T * 1.2f + k + x.Id) * 0.8f, 6f);
            Line(ci, top, end, new Color("#3a6ab0"), 0.8f);
            Ring(ci, end, 1f, Chrome, 0.6f, 8); // 고리쇠
        }
        if (x.Tier >= 4 && x.Lit) Line(ci, x.P(0.04f, 0.22f), x.P(0.96f, 0.22f), Cyan.WithAlpha(0.3f + 0.3f * Pulse(x.T, 2f)), 1.4f);
    }

    private static void RailFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 6; k++) Bolt(ci, x.P(0.04f + k * 0.184f, 0.36f), 0.4f);
        Tag(ci, x.P(0.5f, 0.12f), "잡으세요", 4, WarnYellow.WithAlpha(0.7f));
    }

    // ─────────────── 화물 그물 ───────────────

    private static void NetBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.06f, 0.35f, 0.5f, 0.92f), new Color("#6a5034"), 1f, new Color("#4a3622")); // 상자
        Box(ci, x.Q(0.52f, 0.2f, 0.94f, 0.92f), new Color("#5a6a4a"), 1f, new Color("#3a4a2a"));
        Box(ci, x.Q(0.14f, 0.08f, 0.44f, 0.35f), new Color("#7a6040"), 1f, new Color("#4a3622"));
        for (int k = -4; k <= 6; k++) // 마름모 그물
        {
            Line(ci, x.P(k * 0.15f, 0.02f), x.P(k * 0.15f + 0.6f, 0.98f), GNet.WithAlpha(0.75f), 0.6f);
            Line(ci, x.P(k * 0.15f + 0.6f, 0.02f), x.P(k * 0.15f, 0.98f), GNet.WithAlpha(0.75f), 0.6f);
        }
        ci.DrawRect(x.Q(0.0f, 0.46f, 1f, 0.52f), GStrap); // 조임띠
        for (int k = 0; k < 4; k++) Dot(ci, x.P(k == 0 ? 0f : k == 1 ? 1f : k == 2 ? 0f : 1f, k < 2 ? 0.04f : 0.96f), 1.2f, Steel4); // 고리점
        if (x.Tier >= 2) Gauge(ci, x.P(0.5f, 0.49f), 1.6f, 0.65f, Good); // II: 장력계
        if (x.Tier >= 3) Box(ci, x.Q(0.9f, 0.42f, 1.0f, 0.56f), Steel3, 1f); // III: 자동 조임
    }

    private static void NetLife(in Fix x)
    {
        var ci = x.Ci;
        bool shaking = x.W != null && (x.W.Maneuver.Active || x.W.Maneuver.Tumbles.Count > 0);
        float sway = shaking ? Mathf.Sin(x.T * 9f) * 1.4f : Mathf.Sin(x.T * 0.6f + x.Id) * 0.2f;
        for (int k = 0; k < 2; k++) // 조임띠 꼬리표가 펄럭인다
        {
            var p = x.P(0.2f + k * 0.6f, 0.52f);
            ci.DrawColoredPolygon(new[] { p, p + new Vector2(2.4f + sway, 2.6f), p + new Vector2(-0.4f + sway, 3.4f) }, Danger.WithAlpha(0.85f));
        }
        if (shaking) Line(ci, x.P(0.0f, 0.49f + sway * 0.01f), x.P(1f, 0.49f - sway * 0.01f), GStrap.Lightened(0.3f), 0.8f); // 팽팽한 띠
        if (x.Tier >= 3 && x.On && x.Lod > 0) Led(ci, x.P(0.95f, 0.49f), Good, 0.4f * x.Glow, 0.8f);
    }

    private static void NetFine(in Fix x)
    {
        var ci = x.Ci;
        Tag(ci, x.P(0.28f, 0.66f), "취급 주의", 3, Cream.WithAlpha(0.6f));
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.55f, 0.3f + k * 0.2f), x.P(0.9f, 0.3f + k * 0.2f), new Color("#3a4a2a"), 0.4f); // 나뭇결
    }

    // ─────────────── 충격 좌석 ───────────────

    private static void CrashSeatBody(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        var back = x.C - f * 6f;
        Box(ci, new Rect2(back - side.Abs() * 8f - f.Abs() * 3f, side.Abs() * 16f + f.Abs() * 6f), GSeat, 3f, Steel4); // 등받이
        Box(ci, new Rect2(x.C - side.Abs() * 7f - f.Abs() * 3f + f * 2f, side.Abs() * 14f + f.Abs() * 8f), GSeat.Lightened(0.08f), 2f, Steel4); // 앉는 판
        ci.DrawCircle(back - f * 3f, 3f, GSeat.Darkened(0.2f)); // 머리받이
        for (int k = -2; k <= 2; k++) Line(ci, back + side * k * 3f - f * 2f, back + side * (k + 1) * 3f + f * 2f, GGel, 0.5f); // 누빈 무늬
        var buckle = x.C + f * 2f;
        foreach (var e in new[] { back + side * 6f, back - side * 6f, x.C + side * 6f + f * 5f, x.C - side * 6f + f * 5f, x.C + f * 7f })
            Line(ci, e, buckle, new Color("#2a2a2a"), 1.6f); // 다섯 점 띠
        Ring(ci, buckle, 1.8f, Chrome, 1f, 12); // 버클
        if (x.Tier >= 2) { Line(ci, back + side * 8f, x.C + side * 8f, GSeat.Lightened(0.2f), 2f); Line(ci, back - side * 8f, x.C - side * 8f, GSeat.Lightened(0.2f), 2f); } // II: 옆 받침
        if (x.Tier >= 3) Line(ci, x.C + f * 9f - side * 5f, x.C + f * 9f + side * 5f, Steel4, 1.6f); // III: 발판
    }

    private static void CrashSeatLife(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var buckle = x.C + f * 2f;
        bool strapped = x.Occupant != null;
        bool shaking = x.W != null && x.W.Maneuver.Active;
        var j = shaking ? new Vector2(Mathf.Sin(x.T * 31f), Mathf.Cos(x.T * 27f)) * 0.5f : Vector2.Zero;
        Dot(ci, buckle + j, 1f, strapped ? Good.WithAlpha(0.8f) : Chrome.WithAlpha(0.3f + 0.3f * Pulse(x.T, 1.5f))); // 버클 빛
        if (strapped) Ring(ci, buckle + j, 3f, Good.WithAlpha(0.25f), 0.6f, 12);
    }

    private static void CrashSeatFine(in Fix x)
    {
        var ci = x.Ci;
        var back = x.C - x.Front * 6f;
        var side = new Vector2(-x.Front.Y, x.Front.X);
        for (int k = -3; k <= 3; k++) Dot(ci, back + side * k * 2.2f + x.Front * 2.6f, 0.3f, Cream.WithAlpha(0.5f)); // 바느질
        Bolt(ci, x.C + side * 7f, 0.5f);
    }
}
