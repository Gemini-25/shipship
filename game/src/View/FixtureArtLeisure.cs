using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 그림 표 — 쉼 · 잠자리 환경 (8종).
/// 영사기(렌즈 · 빛 원뿔 · 먼지) · 게임 탁자(팔각 펠트 · 판 · 칩 · 주사위) · 책장(색 다른 책등 · 북엔드 · 화분)
/// · 수조(자갈 · 수초 · 물고기 · 거품) · 러닝머신(벨트 · 손잡이 · 계기) · 암막 커튼(봉 · 주름 — 누가 자면 닫힌다)
/// · 방음재(달걀판 피라미드 — 소리가 스며 사라진다) · 백색 소음기(타공 그릴 · 잔물결).
/// </summary>
public static partial class FixtureArt
{
    private static void LeisureArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.Projector] = new(ProjectorBody, ProjectorLife, ProjectorFine, Look.Smoke, 0.5f, 0.5f);
        t[FurnitureType.GameTable] = new(GameTableBody, GameTableLife, GameTableFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.Bookshelf] = new(BookshelfBody, BookshelfLife, BookshelfFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.Aquarium] = new(AquariumBody, AquariumLife, AquariumFine, Look.Leak, 0.5f, 0.95f);
        t[FurnitureType.Treadmill] = new(TreadmillBody, TreadmillLife, TreadmillFine, Look.Grind, 0.85f, 0.5f);
        t[FurnitureType.BlackoutCurtain] = new(CurtainBody, CurtainLife, CurtainFine, Look.Jam, 0.5f, 0.1f);
        t[FurnitureType.NoiseDamper] = new(DamperBody, DamperLife, DamperFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.WhiteNoise] = new(NoiseBody, NoiseLife, NoiseFine, Look.Flicker, 0.5f, 0.5f);
    }

    private static readonly Color FeltGreen = new("#1f5a3a");
    private static readonly Color BeamWhite = new("#fff4d8");
    private static readonly Color AquaWater = new("#1a5a7a");
    private static readonly Color CurtainCloth = new("#2a2648");
    private static readonly Color[] FishColors = { new("#ff8a3c"), new("#5fd0ff"), new("#ffd04a"), new("#e05aa8") };
    private static readonly Color[] SpineColors = { new("#8a3a2a"), new("#2a5a8a"), new("#3a7a4a"), new("#c8a040"), new("#5a3a7a"), new("#7a7a7a"), new("#a85a3a") };

    private static int SittingIn(in Fix x)
    {
        if (x.W == null) return 0;
        int n = 0;
        foreach (var c in x.W.Crew) if (c.Room == x.F.Room && c.Pose == Pose.Sitting) n++;
        return n;
    }

    // ─────────────── 영사기 ───────────────

    private static void ProjectorBody(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        var body = new Rect2(x.C - new Vector2(8f, 7f), new Vector2(16f, 14f));
        foreach (var p in new[] { body.Position + new Vector2(2f, 13f), body.End - new Vector2(2f, 1f) }) Dot(ci, p, 1.2f, Rubber); // 받침 다리
        Box(ci, body, new Color("#2a2a30"), 3, new Color("#5a5a66"));
        Bevel(ci, body, 0.12f);
        var lens = x.C + f * 6f;
        Dot(ci, lens, 3.6f, new Color("#101014")); // 렌즈
        Ring(ci, lens, 3.6f, new Color("#8a8a96"), 1f, 16);
        Ring(ci, lens, 2.2f, new Color("#3a4a6a"), 0.8f, 12);
        Ring(ci, lens, 4.6f, new Color("#4a4a52"), 1.4f, 16); // 초점 고리
        var grill = x.C - f * 4f;
        for (int k = -2; k <= 2; k++) Line(ci, grill + side * k * 2f - f * 1.5f, grill + side * k * 2f + f * 1.5f, new Color("#16161a"), 1f); // 냉각 그릴
    }

    private static void ProjectorLife(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        var lens = x.C + f * 6f;
        if (x.Lit)
        {
            float frame = Hash(x.Id, (int)(x.T * 0.5f), 111); // 장면마다 빛 색이 조금씩 바뀐다
            var tint = BeamWhite.Lerp(frame < 0.33f ? new Color("#9ac8ff") : frame < 0.66f ? new Color("#ffc89a") : new Color("#b0ffb8"), 0.35f);
            float flick = 0.85f + 0.15f * Mathf.Sin(x.T * 23f);
            float len = ShipView.T * 2.6f;
            ci.DrawPolygon(new[] { lens - side * 2f, lens + side * 2f, lens + f * len + side * 18f, lens + f * len - side * 18f },
                new[] { tint.WithAlpha(0.3f * flick * x.Glow), tint.WithAlpha(0.3f * flick * x.Glow), tint.WithAlpha(0f), tint.WithAlpha(0f) }); // 빛 원뿔
            Dot(ci, lens, 2f, tint.WithAlpha(x.Glow));
            if (x.Lod > 0)
                for (int k = 0; k < 4; k++) // 빛 속 먼지
                {
                    float ph = Mathf.PosMod(x.T * 0.1f + k * 0.25f, 1f);
                    var p = lens + f * len * (0.2f + 0.7f * Hash(x.Id, k, 112)) + side * (ph - 0.5f) * 20f;
                    Dot(ci, p, 0.6f, Colors.White.WithAlpha(0.5f * Mathf.Sin(ph * Mathf.Pi)));
                }
        }
        var grill = x.C - f * 4f;
        if (x.On && x.Lod > 0) Fan(ci, grill, 3f, 3, x.Ang(8f), new Color("#4a4a52"), 0.8f);
    }

    private static void ProjectorFine(in Fix x)
    {
        var ci = x.Ci;
        var body = new Rect2(x.C - new Vector2(8f, 7f), new Vector2(16f, 14f));
        Bolts(ci, body, 1.5f, 0.45f);
        Led(ci, body.Position + new Vector2(3f, 3f), Good, 0.3f, 0.6f);
    }

    // ─────────────── 게임 탁자 ───────────────

    private static void GameTableBody(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.52f;
        var oct = new Vector2[8];
        for (int k = 0; k < 8; k++) oct[k] = x.C + Vector2.FromAngle(Mathf.Pi / 8f + k * Mathf.Tau / 8f) * rr;
        ci.DrawColoredPolygon(oct, Wood); // 나무 테
        var felt = new Vector2[8];
        for (int k = 0; k < 8; k++) felt[k] = x.C + Vector2.FromAngle(Mathf.Pi / 8f + k * Mathf.Tau / 8f) * (rr - 2.5f);
        ci.DrawColoredPolygon(felt, FeltGreen); // 펠트
        float cell = rr * 0.28f;
        for (int i = 0; i < 4; i++) // 가운데 판
            for (int j = 0; j < 4; j++)
                if ((i + j) % 2 == 0) ci.DrawRect(new Rect2(x.C + new Vector2((i - 2) * cell * 0.5f, (j - 2) * cell * 0.5f), new Vector2(cell * 0.5f, cell * 0.5f)), new Color("#e8e2d4").WithAlpha(0.7f));
        for (int k = 0; k < 3; k++) // 칩 더미
        {
            var p = x.C + Vector2.FromAngle(k * 2.1f + 0.4f) * rr * 0.62f;
            for (int s = 0; s < 3; s++) Dot(ci, p + new Vector2(0f, -s * 0.8f), 1.8f, (k == 0 ? new Color("#c0392b") : k == 1 ? new Color("#2a5aa8") : new Color("#e8e2d4")).Darkened(s * 0.08f));
        }
        var card = x.C + new Vector2(rr * 0.5f, -rr * 0.3f); // 카드 두 장
        ci.DrawRect(new Rect2(card, new Vector2(2.5f, 3.5f)), Colors.White);
        ci.DrawRect(new Rect2(card + new Vector2(1.5f, 1f), new Vector2(2.5f, 3.5f)), new Color("#f0e8e0"));
    }

    private static void GameTableLife(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.52f;
        int players = SittingIn(x);
        // 주사위 둘: 노는 사람이 있으면 가끔 굴러간다
        float roll = players > 0 ? Mathf.PosMod(x.T * 0.25f, 1f) : 1f;
        for (int k = 0; k < 2; k++)
        {
            float a = roll < 0.2f ? x.T * 12f + k : Hash(x.Id, (int)(x.T * 0.25f) + k, 113) * Mathf.Tau;
            var p = x.C + new Vector2(-rr * 0.5f + k * 4f, rr * 0.35f) + (roll < 0.2f ? new Vector2(roll * 20f, -Mathf.Sin(roll * 15f) * 2f) : Vector2.Zero);
            var d = Vector2.FromAngle(a) * 1.6f;
            var n = new Vector2(-d.Y, d.X);
            ci.DrawColoredPolygon(new[] { p - d - n, p + d - n, p + d + n, p - d + n }, Colors.White);
            if (x.Lod > 0) Dot(ci, p, 0.5f, new Color("#1a1a1a"));
        }
        if (players > 0 && x.Lod == 2) Dot(ci, x.C + Vector2.FromAngle(x.T) * rr * 0.62f, 0.7f, Colors.White.WithAlpha(0.5f)); // 칩 반짝
    }

    private static void GameTableFine(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.52f;
        for (int k = 0; k < 8; k++) Dot(ci, x.C + Vector2.FromAngle(Mathf.Pi / 8f + k * Mathf.Tau / 8f) * (rr - 1.2f), 0.5f, Brass);
        Ring(ci, x.C, rr * 0.82f, new Color(1, 1, 1, 0.08f), 0.6f, 20);
    }

    // ─────────────── 책장 ───────────────

    private static void BookshelfBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#2a2018"), 2, Wood, 2);
        for (int row = 0; row < 3; row++)
        {
            float v0 = 0.08f + row * 0.31f, v1 = v0 + 0.27f;
            ci.DrawRect(x.Q(0.04f, v1, 0.96f, v1 + 0.03f), Wood.Lightened(0.1f)); // 선반 판
            float u = 0.05f;
            int i = 0;
            while (u < 0.9f) // 책등 (두께 · 높이 · 색이 다르다)
            {
                float w = 0.05f + Hash(x.Id, row * 20 + i, 121) * 0.06f;
                float h = 0.6f + Hash(x.Id, row * 20 + i, 122) * 0.4f;
                if (u + w > 0.94f) break;
                var col = SpineColors[(int)(Hash(x.Id, row * 20 + i, 123) * SpineColors.Length) % SpineColors.Length];
                ci.DrawRect(x.Q(u, v1 - (v1 - v0) * h, u + w - 0.008f, v1), col);
                u += w;
                i++;
                if (Hash(x.Id, row * 20 + i, 124) > 0.85f) u += 0.05f; // 빈틈
            }
        }
        ci.DrawRect(x.Q(0.88f, 0.1f, 0.94f, 0.35f), new Color("#6a6f79")); // 북엔드
        Dot(ci, x.P(0.12f, 0.05f), 2.2f, new Color("#7a4a2a")); // 화분
        Dot(ci, x.P(0.12f, 0.03f), 2f, Leaf);
    }

    private static void BookshelfLife(in Fix x)
    {
        var ci = x.Ci;
        if (SittingIn(x) > 0) // 누가 읽고 있다: 한 권이 빠진 자리
        {
            int row = (int)(Hash(x.Id, (int)(x.T * 0.01f), 125) * 3f);
            float v1 = 0.08f + row * 0.31f + 0.27f;
            float u = 0.3f + Hash(x.Id, row, 126) * 0.4f;
            ci.DrawRect(x.Q(u, v1 - 0.22f, u + 0.06f, v1), new Color("#120c08"));
        }
        if (x.Lod == 2) // 빛 속 먼지 한 톨
        {
            float ph = Mathf.PosMod(x.T * 0.07f, 1f);
            Dot(ci, x.P(0.2f + ph * 0.6f, 0.5f + Mathf.Sin(ph * 9f) * 0.1f), 0.5f, Colors.White.WithAlpha(0.3f * Mathf.Sin(ph * Mathf.Pi)));
        }
    }

    private static void BookshelfFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.6f, 0.45f);
        for (int row = 0; row < 3; row++) Dot(ci, x.P(0.5f, 0.08f + row * 0.31f + 0.285f), 0.6f, Brass); // 선반 못
    }

    // ─────────────── 수조 ───────────────

    private static void AquariumBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a1e24"), 3, new Color("#6a7080"), 2); // 테
        var tank = x.B.Grow(-2.5f);
        ci.DrawRect(tank, AquaWater);
        for (int i = 0; i < 22; i++) // 자갈
            Dot(ci, tank.Position + new Vector2(Hash(x.Id, i, 131), Hash(x.Id, i, 132)) * tank.Size, 0.6f + 0.6f * Hash(x.Id, i, 133), new Color("#c8b890").Darkened(Hash(x.Id, i, 134) * 0.4f).WithAlpha(0.7f));
        Dot(ci, tank.Position + tank.Size * new Vector2(0.75f, 0.7f), 3f, new Color("#5a6270")); // 바위
        Dot(ci, tank.Position + tank.Size * new Vector2(0.68f, 0.78f), 2f, new Color("#4a5260"));
        ci.DrawRect(new Rect2(tank.End - new Vector2(6f, 5f), new Vector2(5f, 4f)), new Color("#2a2e36")); // 여과기
        Dot(ci, tank.Position + new Vector2(4f, tank.Size.Y - 4f), 1.4f, new Color("#8a929e")); // 기포석
    }

    private static void AquariumLife(in Fix x)
    {
        var ci = x.Ci;
        var tank = x.B.Grow(-2.5f);
        bool murky = x.M != null && x.M.Has(FaultKind.FilterClogged);
        for (int k = 0; k < 3; k++) // 수초가 흔들린다
        {
            var root = tank.Position + new Vector2(tank.Size.X * (0.2f + k * 0.15f), tank.Size.Y * 0.85f);
            var pts = new Vector2[4];
            for (int i = 0; i < 4; i++) pts[i] = root + new Vector2(Mathf.Sin(x.T * 1.2f + i * 0.8f + k) * i * 0.8f, -i * tank.Size.Y * 0.14f);
            ci.DrawPolyline(pts, Leaf.WithAlpha(0.85f), 1.2f, true);
        }
        int fish = x.Lod == 0 ? 2 : 4;
        for (int k = 0; k < fish; k++) // 물고기
        {
            float sp = 0.3f + Hash(x.Id, k, 135) * 0.4f;
            float a = x.T * sp + k * 1.7f;
            var p = tank.GetCenter() + new Vector2(Mathf.Sin(a) * tank.Size.X * 0.36f, Mathf.Sin(a * 1.7f + k) * tank.Size.Y * 0.28f);
            float dir = Mathf.Cos(a) >= 0f ? 1f : -1f;
            var col = FishColors[k % FishColors.Length];
            ci.DrawSetTransform(p, 0f, new Vector2(1f, 0.55f));
            ci.DrawCircle(Vector2.Zero, 2f, col, true, -1f, true);
            ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            float wag = Mathf.Sin(x.T * 9f + k) * 0.8f;
            ci.DrawColoredPolygon(new[] { p - new Vector2(dir * 1.6f, 0f), p - new Vector2(dir * 3.6f, -1.6f + wag), p - new Vector2(dir * 3.6f, 1.6f + wag) }, col.Darkened(0.2f));
        }
        if (x.On)
            for (int k = 0; k < (x.Lod == 0 ? 1 : 3); k++) // 기포
            {
                float ph = Mathf.PosMod(x.T * 0.5f + k / 3f, 1f);
                Dot(ci, tank.Position + new Vector2(4f + Mathf.Sin(ph * 10f) * 1f, tank.Size.Y - 4f - ph * tank.Size.Y * 0.85f), 0.6f + ph * 0.5f, Colors.White.WithAlpha(0.6f * (1f - ph)));
            }
        if (murky) ci.DrawRect(tank, new Color("#5a6a3a").WithAlpha(0.35f)); // 여과기가 막혀 물이 흐리다
        else if (x.Lit) ci.DrawRect(new Rect2(tank.Position, new Vector2(tank.Size.X, 2f)), new Color("#bfe8ff").WithAlpha(0.25f * x.Glow)); // 위 조명
    }

    private static void AquariumFine(in Fix x)
    {
        var ci = x.Ci;
        var tank = x.B.Grow(-2.5f);
        Line(ci, tank.Position + new Vector2(2f, tank.Size.Y * 0.4f), tank.Position + new Vector2(tank.Size.X * 0.4f, 2f), new Color(1, 1, 1, 0.15f), 1f); // 유리 반사
        Bolts(ci, x.B, 1.5f, 0.45f);
    }

    // ─────────────── 러닝머신 ───────────────

    private static void TreadmillBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a1c20"), 3, new Color("#5a5f69"));
        var belt = x.Q(0.22f, 0.2f, 0.98f, 0.8f);
        Box(ci, belt, new Color("#0e0f11"), 2); // 벨트
        Line(ci, x.P(0.22f, 0.15f), x.P(0.98f, 0.15f), Chrome.WithAlpha(0.7f), 1.4f); // 옆 난간
        Line(ci, x.P(0.22f, 0.85f), x.P(0.98f, 0.85f), Chrome.WithAlpha(0.7f), 1.4f);
        Box(ci, x.Q(0.0f, 0.1f, 0.2f, 0.9f), new Color("#2a2e36"), 2, new Color("#6a7080")); // 모터 덮개 · 계기
        Line(ci, x.P(0.2f, 0.02f), x.P(0.2f, 0.98f), new Color("#8a929e"), 2f); // 손잡이
        ci.DrawRect(x.Q(0.04f, 0.3f, 0.16f, 0.7f), GlassDark); // 계기 화면
    }

    private static void TreadmillLife(in Fix x)
    {
        var ci = x.Ci;
        bool runner = x.Occupant != null || x.User != null;
        float speed = x.On ? (runner ? 2.5f : 0.6f) * x.Spin : 0f;
        float off = Mathf.PosMod(x.T * speed, 1f);
        for (int k = 0; k < 6; k++) // 흐르는 벨트 줄
        {
            float u = 0.24f + Mathf.PosMod(k / 6f + off, 1f) * 0.72f;
            Line(ci, x.P(u, 0.22f), x.P(u, 0.78f), new Color("#2a2c30"), 1f);
        }
        if (x.Lit)
        {
            for (int k = 0; k < 3; k++) // 속도 막대
            {
                bool on = runner ? (int)(x.T * 2f) % 3 >= k : k == 0;
                ci.DrawRect(x.Q(0.06f, 0.62f - k * 0.12f, 0.14f, 0.68f - k * 0.12f), (on ? Good : new Color("#1a2a22")).WithAlpha(x.Glow));
            }
            if (runner) Led(ci, x.P(0.1f, 0.22f), Danger, Pulse(x.T, 7f), 0.8f); // 심박
        }
    }

    private static void TreadmillFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.0f, 0.1f, 0.2f, 0.9f), 1.5f, 0.45f);
        Dot(ci, x.P(0.2f, 0.5f), 1.2f, Danger.WithAlpha(0.7f)); // 비상 정지 끈
        Tag(ci, x.P(0.6f, 0.5f), "km/h", 4, new Color(1, 1, 1, 0.2f));
    }

    // ─────────────── 암막 커튼 ───────────────

    private static bool SleepingIn(in Fix x)
    {
        if (x.W == null) return false;
        foreach (var c in x.W.Crew) if (c.Room == x.F.Room && c.Pose == Pose.Sleeping) return true;
        return false;
    }

    private static void CurtainBody(in Fix x)
    {
        var ci = x.Ci;
        Line(ci, x.P(0.0f, 0.08f), x.P(1.0f, 0.08f), new Color("#8a7a5a"), 2f); // 커튼 봉
        Dot(ci, x.P(0.0f, 0.08f), 1.6f, Brass);
        Dot(ci, x.P(1.0f, 0.08f), 1.6f, Brass);
        Dot(ci, x.P(0.02f, 0.6f), 1.2f, Brass); // 묶는 고리
        Dot(ci, x.P(0.98f, 0.6f), 1.2f, Brass);
        ci.DrawRect(x.Q(0.04f, 0.14f, 0.96f, 0.92f), new Color("#0a0c14").WithAlpha(0.35f)); // 창 자리 그늘
    }

    private static void CurtainLife(in Fix x)
    {
        var ci = x.Ci;
        bool closed = SleepingIn(x); // 누가 자면 닫혀 있다
        float sway = x.F.Room.VentOpen ? Mathf.Sin(x.T * 0.9f) * 0.01f : 0f;
        int folds = 8;
        float span = closed ? 0.5f : 0.16f; // 반쪽씩 펼친 폭
        for (int s = 0; s < 2; s++)
            for (int k = 0; k < folds / 2; k++)
            {
                float a = (k + 0.5f) / (folds / 2);
                float u = s == 0 ? a * span : 1f - a * span;
                float w = span / (folds / 2) * 0.55f;
                var col = (k % 2 == 0 ? CurtainCloth : CurtainCloth.Lightened(0.12f));
                ci.DrawRect(x.Q(u - w + sway, 0.1f, u + w + sway, closed ? 0.95f : 0.8f), col); // 주름
            }
        if (!closed && x.Lod > 0) // 열려 있으면 가운데로 빛이 든다
            ci.DrawRect(x.Q(0.2f, 0.14f, 0.8f, 0.9f), new Color("#d8e8ff").WithAlpha(0.05f));
        for (int k = 0; k < 6; k++) Dot(ci, x.P(closed ? 0.04f + k * 0.18f : (k < 3 ? 0.02f + k * 0.05f : 0.88f + (k - 3) * 0.05f), 0.08f), 0.9f, Brass); // 봉 고리
    }

    private static void CurtainFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 10; k++) Dot(ci, x.P(0.05f + k * 0.1f, 0.93f), 0.4f, new Color(1, 1, 1, 0.15f)); // 밑단 바느질
        Bolt(ci, x.P(0.0f, 0.08f), 0.5f);
        Bolt(ci, x.P(1.0f, 0.08f), 0.5f);
    }

    // ─────────────── 방음재 ───────────────

    private static void DamperBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#2a2a32"), 2, new Color("#4a4a56"), 2);
        int n = 4;
        float cw = x.B.Size.X / n, chh = x.B.Size.Y / n;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) // 달걀판 피라미드 (빛 받는 쪽 밝게)
            {
                var c = x.B.Position + new Vector2((i + 0.5f) * cw, (j + 0.5f) * chh);
                var tl = c + new Vector2(-cw * 0.45f, -chh * 0.45f);
                var tr = c + new Vector2(cw * 0.45f, -chh * 0.45f);
                var bl = c + new Vector2(-cw * 0.45f, chh * 0.45f);
                var br = c + new Vector2(cw * 0.45f, chh * 0.45f);
                ci.DrawColoredPolygon(new[] { tl, tr, c }, new Color("#4a4a58"));
                ci.DrawColoredPolygon(new[] { tl, c, bl }, new Color("#3e3e4a"));
                ci.DrawColoredPolygon(new[] { tr, br, c }, new Color("#24242c"));
                ci.DrawColoredPolygon(new[] { bl, c, br }, new Color("#1c1c22"));
            }
    }

    private static void DamperLife(in Fix x)
    {
        // 방 소음만큼 소리 물결이 들어와 스며 사라진다
        var ci = x.Ci;
        float noise = Mathf.Clamp(x.F.Room.Noise, 0f, 1f);
        if (noise < 0.05f || x.Lod == 0) return;
        var src = x.C + x.Front * (x.B.Size.X * 0.9f);
        for (int k = 0; k < 3; k++)
        {
            float ph = Mathf.PosMod(x.T * 0.7f + k / 3f, 1f);
            float a = Mathf.Atan2(-x.Front.Y, -x.Front.X);
            ci.DrawArc(src, (1f - ph) * x.B.Size.X * 0.9f + 2f, a - 0.6f, a + 0.6f, 8, new Color("#c8c8ff").WithAlpha(0.35f * noise * ph), 1f, true);
        }
    }

    private static void DamperFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.5f, 0.45f);
        Tag(ci, x.C + new Vector2(0f, x.B.Size.Y * 0.42f), "-dB", 4, new Color(1, 1, 1, 0.3f));
    }

    // ─────────────── 백색 소음기 ───────────────

    private static void NoiseBody(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.4f;
        Dot(ci, x.C + new Vector2(1.2f, 1.6f), rr, Shadow);
        Dot(ci, x.C, rr, new Color("#c8ccd4"));
        Ring(ci, x.C, rr, new Color("#8a929e"), 1f, 24);
        for (int ring = 1; ring <= 3; ring++) // 타공 그릴
        {
            int n = ring * 6;
            for (int k = 0; k < n; k++) Dot(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / n) * rr * ring * 0.24f, 0.6f, new Color("#5a6270"));
        }
        Dot(ci, x.C, 1.6f, new Color("#3a4250")); // 가운데 손잡이
    }

    private static void NoiseLife(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.4f;
        if (!x.Lit) return;
        for (int k = 0; k < (x.Lod == 0 ? 1 : 2); k++) // 잔잔한 물결
        {
            float ph = Mathf.PosMod(x.T * 0.3f + k * 0.5f, 1f);
            Ring(ci, x.C, rr * (1.05f + ph * 0.8f), new Color("#d8e0ff").WithAlpha(0.18f * (1f - ph) * x.Glow), 1f, 24);
        }
        if (x.Lod > 0)
            for (int k = 0; k < 5; k++) // 지직이는 잡음 점
            {
                var p = x.C + Vector2.FromAngle(Hash(x.Id, (int)(x.T * 15f) + k, 141) * Mathf.Tau) * rr * 0.8f * Hash(x.Id, (int)(x.T * 15f) + k, 142);
                Dot(ci, p, 0.5f, Colors.White.WithAlpha(0.7f * x.Glow));
            }
        Led(ci, x.C + new Vector2(rr * 0.7f, rr * 0.7f), Cyan, 0.5f * x.Glow, 0.7f);
    }

    private static void NoiseFine(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.4f;
        Ring(ci, x.C, rr * 0.9f, new Color(1, 1, 1, 0.2f), 0.5f, 20);
        Line(ci, x.C, x.C + new Vector2(0f, -1.4f), Colors.White.WithAlpha(0.6f), 0.6f);
    }
}
