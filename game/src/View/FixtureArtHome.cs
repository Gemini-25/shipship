using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 그림 표 — 잠 · 식사 · 의료 · 보관 (16종).
/// 침대(베개 · 이불 주름 · 개인 소품 · 독서등) · 간이침대(X다리 · 말아 둔 담요) · 의자(별 다리 · 등받이) · 식탁(접시 자리 · 양념통)
/// · 배식기(배출구 · 메뉴 화면) · 조리대(화구 셋 · 손잡이) · 냉장고(문 둘 · 손잡이 · 성에) · 오븐(유리문 · 대류 팬) · 식기 세척기(살 · 도는 분사 팔)
/// · 커피 머신(원두통 · 머그 · 스팀 봉) · 치료 침대(모니터 · 링거) · 진단 스캐너(고리 갠트리) · 멸균기(압력 핸들) · 우주복 보관함 · 선반 · 비상 물자함.
/// </summary>
public static partial class FixtureArt
{
    private static void HomeArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.Bed] = new(BedBody, BedLife, BedFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.Cot] = new(CotBody, CotLife, CotFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.Seat] = new(SeatBody, SeatLife, SeatFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.Table] = new(TableBody, TableLife, TableFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.MealDispenser] = new(DispenserBody, DispenserLife, DispenserFine, Look.Jam, 0.5f, 0.8f);
        t[FurnitureType.Stove] = new(StoveBody, StoveLife, StoveFine, Look.Heat, 0.22f, 0.45f);
        t[FurnitureType.Fridge] = new(FridgeBody, FridgeLife, FridgeFine, Look.Leak, 0.85f, 0.9f);
        t[FurnitureType.Oven] = new(OvenBody, OvenLife, OvenFine, Look.Heat, 0.5f, 0.2f);
        t[FurnitureType.DishWasher] = new(DishWasherBody, DishWasherLife, DishWasherFine, Look.Leak, 0.85f, 0.85f);
        t[FurnitureType.CoffeeMachine] = new(CoffeeBody, CoffeeLife, CoffeeFine, Look.Steam, 0.3f, 0.3f);
        t[FurnitureType.MedBed] = new(MedBedBody, MedBedLife, MedBedFine, Look.Flicker, 0.08f, 0.8f);
        t[FurnitureType.DiagnosticScanner] = new(ScannerBody, ScannerLife, ScannerFine, Look.Flicker, 0.5f, 0.5f);
        t[FurnitureType.Autoclave] = new(AutoclaveBody, AutoclaveLife, AutoclaveFine, Look.Steam, 0.85f, 0.15f);
        t[FurnitureType.SuitLocker] = new(SuitLockerBody, SuitLockerLife, SuitLockerFine, Look.Gas, 0.5f, 0.5f);
        t[FurnitureType.Shelf] = new(ShelfBody, ShelfLife, ShelfFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.SupplyCache] = new(CacheBody, CacheLife, CacheFine, Look.Jam, 0.5f, 0.3f);
    }

    private static readonly Color WarmLamp = new("#ffd59a");
    private static readonly Color Medical = new("#7fd8c8");
    private static readonly Color Ecg = new("#6ef08a");
    private static readonly Color Burner = new("#ff7a3c");
    private static readonly Color CoffeeBrown = new("#6b3f22");
    private static readonly Color FrostBlue = new("#bfe6ff");

    // ─────────────── 침대 ───────────────

    private static void BedBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#232a37"), 5, new Color("#36404f"));
        Box(ci, x.Q(0f, 0f, 0.08f, 1f), new Color("#2e3748"), 3, new Color("#4a5568")); // 머리판
        Box(ci, x.Q(0.06f, 0.07f, 0.985f, 0.93f), new Color("#3a4250"), 4); // 매트리스
        var pil = x.Q(0.1f, 0.13f, 0.27f, 0.87f);
        Box(ci, pil, new Color(0.86f, 0.88f, 0.92f, 0.85f), 4); // 베개
        Line(ci, x.P(0.185f, 0.28f), x.P(0.185f, 0.72f), new Color(0.4f, 0.44f, 0.5f, 0.5f), 1f); // 눌린 자국
        var o = x.F.Owner;
        var bc = o != null ? Palette.Crew(o.Id).Darkened(0.55f) : new Color("#2b3342");
        Box(ci, x.Q(0.32f, 0.05f, 0.975f, 0.95f), bc, 4); // 이불
        Box(ci, x.Q(0.32f, 0.05f, 0.41f, 0.95f), bc.Lightened(0.28f), 3); // 접어 넘긴 윗단
        for (int k = 0; k < 3; k++) // 주름
        {
            float u = 0.52f + k * 0.14f + (Hash(x.Id, k, 1) - 0.5f) * 0.04f;
            var pts = new Vector2[5];
            for (int i = 0; i < 5; i++) pts[i] = x.P(u + Mathf.Sin(i * 1.7f + k) * 0.018f, 0.1f + 0.8f * i / 4f);
            ci.DrawPolyline(pts, bc.Darkened(0.35f), 1f, true);
        }
        // 개인 소품: 주인마다 다르다 (책 · 사진 · 인형)
        int kind = o != null ? (int)(Hash(o.Id, 7, 2) * 3f) : -1;
        var sp = x.P(0.72f, 0.3f);
        if (kind == 0)
        {
            var b = new Rect2(sp - new Vector2(3.5f, 2.5f), new Vector2(7f, 5f));
            ci.DrawRect(b, new Color("#a0523a"));
            ci.DrawLine(b.Position + new Vector2(3.5f, 0f), b.Position + new Vector2(3.5f, 5f), new Color("#e8e2d4"), 0.8f);
        }
        else if (kind == 1)
        {
            ci.DrawRect(new Rect2(x.P(0.03f, 0.75f) - new Vector2(2.5f, 2.5f), new Vector2(5f, 5f)), new Color("#c8b27a"));
            ci.DrawRect(new Rect2(x.P(0.03f, 0.75f) - new Vector2(1.5f, 1.5f), new Vector2(3f, 3f)), new Color("#4a6a8a"));
        }
        else if (kind == 2)
        {
            Dot(ci, sp, 3f, new Color("#c9a27a"));
            Dot(ci, sp + new Vector2(-2f, -2.6f), 1.3f, new Color("#c9a27a"));
            Dot(ci, sp + new Vector2(2f, -2.6f), 1.3f, new Color("#c9a27a"));
        }
        Dot(ci, x.P(0.035f, 0.14f), 2.2f, new Color("#3b3a2f")); // 독서등 갓
    }

    private static void BedLife(in Fix x)
    {
        var ci = x.Ci;
        var o = x.F.Owner;
        bool awake = o != null && o.Room == x.F.Room && o.Pose != Pose.Sleeping && !o.Down;
        var lamp = x.P(0.035f, 0.14f);
        Led(ci, lamp, WarmLamp, awake ? 0.95f : 0f, 1.4f);
        if (awake && x.Lod > 0) Dot(ci, x.P(0.16f, 0.35f), 8f * x.S, WarmLamp.WithAlpha(0.07f)); // 베개 위로 떨어지는 불빛
        // 머리맡 시계: 콜론이 깜빡인다 (잘 때는 더 어둡게)
        bool asleep = o != null && o.Pose == Pose.Sleeping && o.Room == x.F.Room;
        Led(ci, x.P(0.035f, 0.86f), Cyan, (asleep ? 0.18f : 0.4f) * (Mathf.PosMod(x.T, 2f) < 1f ? 1f : 0.55f), 0.9f);
    }

    private static void BedFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.8f);
        for (int i = 0; i < 9; i++) Dot(ci, x.P(0.09f + i * 0.1f, 0.09f), 0.45f, new Color(1, 1, 1, 0.25f)); // 바느질
        ci.DrawRect(new Rect2(x.P(0.24f, 0.78f) - new Vector2(1f, 1f), new Vector2(2f, 3f)), new Color("#e8e2d4")); // 베개 꼬리표
        if (x.F.Owner is CrewMember o && o.Name.Length > 0) Tag(ci, x.P(0.93f, 0.5f), o.Name[..1], 6, new Color(1, 1, 1, 0.55f));
    }

    // ─────────────── 간이침대 ───────────────

    private static void CotBody(in Fix x)
    {
        var ci = x.Ci;
        var leg = new Color("#2e2c22");
        Line(ci, x.P(0.02f, 0.05f), x.P(0.14f, 0.95f), leg, 1.6f); // X다리 (양 끝)
        Line(ci, x.P(0.14f, 0.05f), x.P(0.02f, 0.95f), leg, 1.6f);
        Line(ci, x.P(0.86f, 0.05f), x.P(0.98f, 0.95f), leg, 1.6f);
        Line(ci, x.P(0.98f, 0.05f), x.P(0.86f, 0.95f), leg, 1.6f);
        var canvas = x.Q(0.03f, 0.12f, 0.97f, 0.88f);
        Box(ci, canvas, new Color("#5d5a44"), 3, new Color("#3b3a2f"));
        Line(ci, x.P(0.05f, 0.5f), x.P(0.95f, 0.5f), new Color("#4b4836"), 1.2f); // 처진 가운데 솔기
        Line(ci, x.P(0.0f, 0.1f), x.P(1f, 0.1f), new Color("#8a8f99"), 1.5f); // 알루미늄 봉
        Line(ci, x.P(0.0f, 0.9f), x.P(1f, 0.9f), new Color("#8a8f99"), 1.5f);
        foreach (float u in new[] { 0.32f, 0.6f }) // 끈과 버클
        {
            ci.DrawRect(x.Q(u, 0.12f, u + 0.035f, 0.88f), new Color("#2a2a24"));
            ci.DrawRect(x.Q(u - 0.01f, 0.44f, u + 0.045f, 0.56f), new Color("#9aa3b5"));
        }
        var bc = x.F.Owner != null ? Palette.Crew(x.F.Owner.Id).Darkened(0.45f) : new Color("#3a3f4a");
        Box(ci, x.Q(0.78f, 0.16f, 0.95f, 0.84f), bc, 3); // 말아 둔 담요
        ci.DrawArc(x.P(0.95f, 0.5f), x.Lv * 0.16f, 0f, Mathf.Tau, 12, bc.Lightened(0.25f), 1f, true);
        if (x.F.Improved) Box(ci, x.Q(0.06f, 0.2f, 0.2f, 0.8f), new Color(0.85f, 0.86f, 0.9f, 0.7f), 3); // 손본 간이침대: 베개가 있다
    }

    private static void CotLife(in Fix x)
    {
        // 머리맡에 매단 이름표가 공기 흐름에 흔들린다 (환기가 닫히면 멈춘다)
        float sway = x.F.Room.VentOpen ? Mathf.Sin(x.T * 1.7f) * 0.35f : 0f;
        var top = x.P(0.0f, 0.3f);
        var tip = top + Vector2.FromAngle(Mathf.Pi * 0.5f + sway) * 5f;
        Line(x.Ci, top, tip, new Color("#9aa3b5"), 0.8f);
        x.Ci.DrawRect(new Rect2(tip - new Vector2(1.5f, 0f), new Vector2(3f, 2.5f)), x.F.Owner != null ? Palette.Crew(x.F.Owner.Id) : new Color("#c8b27a"));
    }

    private static void CotFine(in Fix x)
    {
        var ci = x.Ci;
        foreach (float u in new[] { 0f, 1f }) { Dot(ci, x.P(u, 0.1f), 1.1f, new Color("#c8ced8")); Dot(ci, x.P(u, 0.9f), 1.1f, new Color("#c8ced8")); }
        for (int i = 0; i < 12; i++) Dot(ci, x.P(0.05f + i * 0.08f, 0.15f), 0.4f, new Color(0, 0, 0, 0.4f));
        Tag(ci, x.P(0.45f, 0.72f), "임시", 6, new Color(1, 1, 1, 0.3f));
    }

    // ─────────────── 의자 ───────────────

    private static void SeatBody(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        float rr = x.B.Size.X * 0.48f;
        for (int k = 0; k < 5; k++) // 별 다리 + 바퀴
        {
            var d = Vector2.FromAngle(-Mathf.Pi / 2f + k * Mathf.Tau / 5f + 0.3f);
            Line(ci, c, c + d * rr, new Color("#1b1f27"), 2.2f);
            Dot(ci, c + d * rr, 1.5f, Rubber);
        }
        var cush = x.B.Grow(-4.5f);
        Box(ci, cush, new Color("#2f3747"), 6, new Color("#46506a"));
        Box(ci, cush.Grow(-2.5f), new Color("#3a4459").Lerp(x.Accent, 0.14f), 4);
        var back = -x.Front; // 등받이는 쓰는 쪽 반대
        var side = new Vector2(-back.Y, back.X);
        var bc = c + back * (cush.Size.X * 0.5f);
        Line(ci, bc - side * cush.Size.X * 0.42f, bc + side * cush.Size.X * 0.42f, new Color("#56627a"), 3.2f);
        Line(ci, bc - side * cush.Size.X * 0.42f + back * 1f, bc + side * cush.Size.X * 0.42f + back * 1f, new Color("#1d222b"), 1f);
        foreach (float s in new[] { -1f, 1f }) // 팔걸이
            Line(ci, c + side * s * cush.Size.X * 0.5f + back * 3f, c + side * s * cush.Size.X * 0.5f - back * 3f, new Color("#232a36"), 2f);
    }

    private static void SeatLife(in Fix x)
    {
        // 누가 앉으면 방석이 눌리고 높이 레버가 반짝인다
        if (x.User is null && x.Occupant is null) return;
        var cush = x.B.Grow(-7f);
        Box(x.Ci, cush, new Color(0, 0, 0, 0.22f), 5);
        if (x.Lod > 0) Dot(x.Ci, x.C + new Vector2(-x.Front.Y, x.Front.X) * (x.B.Size.X * 0.48f), 1f, Chrome.WithAlpha(0.6f + 0.3f * Mathf.Sin(x.T * 2f)));
    }

    private static void SeatFine(in Fix x)
    {
        var ci = x.Ci;
        Dot(ci, x.C, 1.6f, new Color("#1a1e26"));
        Ring(ci, x.C, 2.6f, new Color("#56627a"), 0.6f, 12);
        var cush = x.B.Grow(-7f);
        for (int i = 0; i < 4; i++) Dot(ci, cush.Position + new Vector2((i % 2 + 0.5f) * cush.Size.X * 0.5f, (i / 2 + 0.5f) * cush.Size.Y * 0.5f), 0.6f, new Color(0, 0, 0, 0.35f)); // 단추
    }

    // ─────────────── 식탁 ───────────────

    private static void TableBody(in Fix x)
    {
        var ci = x.Ci;
        var top = x.R.Grow(-4f);
        foreach (var p in new[] { top.Position + new Vector2(3, 3), new Vector2(top.End.X - 3, top.Position.Y + 3), new Vector2(top.Position.X + 3, top.End.Y - 3), top.End - new Vector2(3, 3) })
            Dot(ci, p + new Vector2(1.5f, 2f), 2.5f, Shadow); // 다리 그림자
        bool galley = x.F.Room.Type == RoomType.Galley;
        var surf = galley ? new Color("#2a2d33") : new Color("#2a2620");
        Box(ci, top, surf, 7, galley ? new Color("#4a5160") : new Color("#4a4032"));
        for (int k = 0; k < 6; k++) // 결 (나무 · 스테인리스 솔질)
        {
            float v = 0.12f + k * 0.15f;
            var pts = new Vector2[4];
            for (int i = 0; i < 4; i++) pts[i] = x.P(0.06f + i * 0.29f, v + (Hash(x.Id, k * 4 + i, 3) - 0.5f) * 0.05f);
            ci.DrawPolyline(pts, (galley ? new Color(1, 1, 1, 0.04f) : new Color("#3a3226")), 1f, true);
        }
        Line(ci, new Vector2(top.Position.X + 7, top.Position.Y + 2.5f), new Vector2(top.End.X - 7, top.Position.Y + 2.5f), new Color(1, 1, 1, 0.08f), 1.5f);
        int n = Mathf.Max(1, (int)(x.Lu / 24f));
        bool twoSides = x.Lv > 40f;
        for (int k = 0; k < n; k++)
        {
            float u = (k + 0.5f) / n;
            for (int side = 0; side < (twoSides ? 2 : 1); side++)
            {
                float v = twoSides ? (side == 0 ? 0.24f : 0.76f) : (k % 2 == 0 ? 0.35f : 0.65f);
                var p = x.P(u, v);
                if (galley)
                {
                    var board = new Rect2(p - new Vector2(5f, 3.5f), new Vector2(10f, 7f));
                    Box(ci, board, new Color("#7a6040"), 1.5f);
                    Line(ci, p + new Vector2(-4f, -1f), p + new Vector2(3f, -1f), Chrome, 1f); // 칼
                    Dot(ci, p + new Vector2(-2f, 1.5f), 1.1f, new Color("#7fbf5a")); // 썬 채소
                    Dot(ci, p + new Vector2(1f, 1.8f), 1f, new Color("#e0643a"));
                }
                else
                {
                    Dot(ci, p, 4.2f, new Color(0.8f, 0.83f, 0.87f, 0.85f)); // 접시
                    Ring(ci, p, 2.6f, new Color("#8894a8"), 0.8f, 14);
                    var side2 = x.V * (side == 0 ? -1f : 1f);
                    Line(ci, p - x.U * 6.5f - side2 * 2.5f, p - x.U * 6.5f + side2 * 2.5f, Chrome, 0.9f); // 포크
                    Line(ci, p + x.U * 6.5f - side2 * 2.5f, p + x.U * 6.5f + side2 * 2.5f, Chrome, 0.9f); // 나이프
                    Dot(ci, p + x.U * 5f - side2 * 5f, 1.8f, new Color("#e0b64a").WithAlpha(0.85f)); // 컵
                }
            }
        }
        // 가운데 양념통 받침
        var mid = x.C;
        Box(ci, new Rect2(mid - new Vector2(4.5f, 2.5f), new Vector2(9f, 5f)), new Color("#1b1d22"), 1.5f);
        Dot(ci, mid + new Vector2(-2.3f, 0f), 1.2f, new Color("#e8e2d4"));
        Dot(ci, mid, 1.2f, new Color("#3a3226"));
        Dot(ci, mid + new Vector2(2.3f, 0f), 1.2f, new Color("#c0392b"));
    }

    private static void TableLife(in Fix x)
    {
        // 그 방에 앉은 사람 수만큼 컵에서 김이 오른다
        if (x.W == null || x.Lod == 0) return;
        int sitting = 0;
        foreach (var c in x.W.Crew) if (c.Room == x.F.Room && c.Pose == Pose.Sitting) sitting++;
        if (sitting == 0) return;
        int n = Mathf.Max(1, (int)(x.Lu / 24f));
        for (int k = 0; k < Mathf.Min(n, sitting); k++)
        {
            var p = x.P((k + 0.5f) / n, x.Lv > 40f ? 0.24f : (k % 2 == 0 ? 0.35f : 0.65f)) + x.U * 5f - x.V * 5f;
            for (int i = 0; i < 2; i++)
            {
                float ph = Mathf.PosMod(x.T * 0.6f + i * 0.5f + k * 0.17f, 1f);
                Dot(x.Ci, p + new Vector2(Mathf.Sin(ph * 6f + k) * 1.5f, -ph * 9f), 1f + 2f * ph, SteamWhite.WithAlpha(0.3f * (1f - ph)));
            }
        }
    }

    private static void TableFine(in Fix x)
    {
        var ci = x.Ci;
        var top = x.R.Grow(-4f);
        Bolts(ci, top, 3.5f, 0.7f);
        Line(ci, new Vector2(top.Position.X + 4, top.End.Y - 1.5f), new Vector2(top.End.X - 4, top.End.Y - 1.5f), new Color(0, 0, 0, 0.35f), 1f);
        Dot(ci, x.C + new Vector2(0f, -3.2f), 0.6f, new Color(1, 1, 1, 0.5f));
    }

    // ─────────────── 배식기 ───────────────

    private static void DispenserBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#283040"), 5, new Color("#3b4556"));
        Bevel(ci, x.B);
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        var mouth = x.C + f * (x.B.Size.X * 0.36f);
        var slot = new Rect2(mouth - (side.Abs() * 8f + f.Abs() * 2.5f), side.Abs() * 16f + f.Abs() * 5f);
        Box(ci, slot, new Color("#0b0e13"), 2, x.Accent.WithAlpha(0.6f)); // 배출구
        Line(ci, mouth + f * 3f - side * 8f, mouth + f * 3f + side * 8f, new Color("#56627a"), 1.5f); // 쟁반 턱
        var scr = new Rect2(x.C - f * 3f - new Vector2(6f, 4.5f), new Vector2(12f, 9f));
        Glass(ci, scr, new Color("#0b1f29"), 1.5f); // 메뉴 화면
        Dot(ci, x.C - f * 3f + side * 9f, 1.6f, new Color("#3a4454")); // 버튼
        Dot(ci, x.C - f * 3f - side * 9f, 1.6f, new Color("#3a4454"));
        var back = x.C - f * (x.B.Size.X * 0.4f);
        for (int k = -1; k <= 1; k++) Dot(ci, back + side * k * 5f, 1.8f, k == 0 ? new Color("#6b8f3a") : k < 0 ? new Color("#c08a3a") : new Color("#8a4a3a")); // 영양 카트리지 뚜껑
    }

    private static void DispenserLife(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var scr = new Rect2(x.C - f * 3f - new Vector2(5f, 3.5f), new Vector2(10f, 7f));
        if (x.Lit)
            for (int k = 0; k < 3; k++)
            {
                float y = scr.Position.Y + 1.5f + Mathf.PosMod(k * 2.4f + x.T * 1.5f * x.Spin, scr.Size.Y - 2f);
                ci.DrawLine(new Vector2(scr.Position.X + 1, y), new Vector2(scr.End.X - 1 - k * 2, y), x.Accent.WithAlpha(0.6f * x.Glow), 1f);
            }
        int meals = x.F.Storage?.Count(ItemKind.Meal) ?? 0;
        var side = new Vector2(-f.Y, f.X);
        var mouth = x.C + f * (x.B.Size.X * 0.36f);
        for (int k = 0; k < System.Math.Min(5, (meals + 3) / 4); k++) // 쌓인 식사 수
            Dot(ci, mouth - f * 6f + side * (k - 2) * 3f, 1.1f, Palette.Item(ItemKind.Meal).WithAlpha(0.85f));
        Led(ci, x.C - f * 3f + side * 9f, meals > 0 ? Good : Amber, x.Lit ? 0.4f + 0.5f * Pulse(x.T, 2.5f) : 0f, 1f);
        if (x.User != null && x.On) // 꺼내는 중: 배출구 불빛과 김
        {
            Dot(ci, mouth, 5f, WarmLamp.WithAlpha(0.18f));
            float ph = Mathf.PosMod(x.T, 1f);
            Dot(ci, mouth + f * 2f - new Vector2(0f, ph * 8f), 1.5f + 2f * ph, SteamWhite.WithAlpha(0.3f * (1f - ph)));
        }
    }

    private static void DispenserFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.5f, 0.7f);
        Vents(ci, new Rect2(x.C - x.Front * (x.B.Size.X * 0.42f) - new Vector2(4f, 4f), new Vector2(8f, 8f)), 3, x.Front.Y != 0f, new Color(0, 0, 0, 0.35f), 0.8f);
        Tag(ci, x.C + x.Front * 6.5f, "식", 6, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── 조리대 ───────────────

    private static void StoveBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#262220"), 4, new Color("#4a3f35"));
        for (int k = 0; k < 7; k++) Line(ci, x.P(0.02f, 0.1f + k * 0.12f), x.P(0.98f, 0.1f + k * 0.12f), new Color(1, 1, 1, 0.025f), 1f); // 솔질 결
        Vents(ci, x.Q(0.05f, 0.02f, 0.95f, 0.1f), 10, true, new Color(0, 0, 0, 0.45f), 1f); // 뒤쪽 배기
        float br = Mathf.Min(x.Lv * 0.32f, x.Lu * 0.16f);
        foreach (var (u, rr) in new[] { (0.2f, br), (0.5f, br * 0.72f), (0.8f, br) })
        {
            var p = x.P(u, 0.48f);
            Dot(ci, p, rr, new Color("#17130f"));
            for (int i = 1; i <= 3; i++) ci.DrawArc(p, rr * i / 3.4f, 0f, Mathf.Tau, 16, new Color("#3f3733"), 1.2f, true); // 코일
            for (int s = 0; s < 4; s++) // 냄비 받침
            {
                var d = Vector2.FromAngle(Mathf.Pi * 0.25f + s * Mathf.Pi * 0.5f);
                Line(ci, p + d * rr * 0.75f, p + d * (rr + 1.5f), new Color("#55493f"), 1.5f);
            }
        }
        for (int k = 0; k < 4; k++) Knob(ci, x.P(0.14f + k * 0.24f, 0.9f), 2.1f, -Mathf.Pi / 2f, new Color("#3a3430")); // 손잡이 줄
    }

    private static void StoveLife(in Fix x)
    {
        var ci = x.Ci;
        float br = Mathf.Min(x.Lv * 0.32f, x.Lu * 0.16f);
        bool cooking = x.St == State.Running && x.M is { Active: true };
        int k = 0;
        foreach (var (u, rr) in new[] { (0.2f, br), (0.5f, br * 0.72f), (0.8f, br) })
        {
            var p = x.P(u, 0.48f);
            if (cooking || x.St == State.Fault)
            {
                float fl = 0.65f + 0.35f * Mathf.Sin(x.T * (6f + k)) * (x.St == State.Fault ? Hash(x.Id, (int)(x.T * 9f) + k, 4) : 1f);
                Dot(ci, p, rr + 2f, Burner.WithAlpha(0.18f * fl * x.Glow));
                for (int i = 1; i <= 3; i++) ci.DrawArc(p, rr * i / 3.4f, 0f, Mathf.Tau, 16, Burner.Lightened(0.15f * i).WithAlpha(0.85f * fl * x.Glow), 1.4f, true);
                Led(ci, x.P(0.14f + k * 0.24f, 0.9f), Danger, 0.8f * x.Glow, 0.8f);
            }
            else if (x.St == State.Standby) Dot(ci, p + new Vector2(rr * 0.6f, 0f), 0.9f, new Color("#5fa8ff").WithAlpha(0.5f + 0.3f * Pulse(x.T + k, 3f))); // 대기: 파란 점화 불씨
            k++;
        }
        if (cooking && x.Lod > 0) Shimmer(ci, x.Q(0.1f, 0.2f, 0.9f, 0.6f), x.T, 0.6f);
    }

    private static void StoveFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.7f);
        for (int k = 0; k < 4; k++) Tag(ci, x.P(0.14f + k * 0.24f, 0.75f), (k + 1).ToString(), 5, new Color(1, 1, 1, 0.3f));
    }

    // ─────────────── 냉장고 ───────────────

    private static void FridgeBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#2a313b"), 5, new Color("#6b7788"));
        Bevel(ci, x.B, 0.12f);
        Line(ci, x.P(0.5f, 0.06f), x.P(0.5f, 0.94f), new Color("#141820"), 1.6f); // 문 사이
        foreach (float u in new[] { 0.25f, 0.75f }) Box(ci, x.Q(u - 0.22f, 0.12f, u + 0.22f, 0.88f), new Color(0, 0, 0, 0f), 3, new Color("#4a5566")); // 문 가스켓
        foreach (float u in new[] { 0.45f, 0.55f }) Box(ci, x.Q(u - 0.012f, 0.3f, u + 0.012f, 0.7f), Chrome, 1); // 손잡이
        // 뒤쪽 응축 코일 (지그재그)
        var pts = new Vector2[12];
        for (int i = 0; i < 12; i++) pts[i] = x.P(0.06f + i * 0.08f, i % 2 == 0 ? 0.02f : 0.08f);
        ci.DrawPolyline(pts, new Color("#3a4250"), 1f, true);
        // 성에 (가장자리 흰 점)
        for (int i = 0; i < 16; i++)
        {
            float u = Hash(x.Id, i, 11), v = Hash(x.Id, i, 12) < 0.5f ? 0.12f + Hash(x.Id, i, 13) * 0.08f : 0.8f + Hash(x.Id, i, 13) * 0.08f;
            Dot(ci, x.P(0.04f + u * 0.92f, v), 0.6f + Hash(x.Id, i, 14) * 0.6f, FrostBlue.WithAlpha(0.35f));
        }
        // 자석 메모 (색 쪽지)
        ci.DrawRect(new Rect2(x.P(0.12f, 0.3f), new Vector2(4f, 4f)), new Color("#e8d27a"));
        ci.DrawRect(new Rect2(x.P(0.78f, 0.55f), new Vector2(3.5f, 4.5f)), new Color("#7ac8e8"));
        Box(ci, x.Q(0.06f, 0.62f, 0.2f, 0.82f), GlassDark, 1); // 온도 표시창
    }

    private static void FridgeLife(in Fix x)
    {
        var ci = x.Ci;
        var disp = x.Q(0.06f, 0.62f, 0.2f, 0.82f);
        float temp = x.On ? 3f : x.St == State.Fault ? 9f : 14f;
        var tc = temp < 6f ? FrostBlue : temp < 10f ? Amber : Danger;
        if (x.Lit || x.St == State.Off) ci.DrawRect(disp.Grow(-1f), tc.WithAlpha(0.25f + 0.5f * Mathf.Max(x.Glow, 0.3f)));
        Led(ci, new Vector2(x.B.End.X - 4, x.B.Position.Y + 4), x.On ? Good : Danger, x.On ? 0.85f : 0.5f + 0.5f * Pulse(x.T, 5f), 1.1f);
        float fill = x.F.Storage is Inventory inv && inv.Capacity > 0 ? inv.Total / (float)inv.Capacity : 0f;
        ci.DrawRect(new Rect2(x.P(0.25f, 0.9f), new Vector2((x.Lu * 0.5f) * fill, 1.6f)), Palette.Item(ItemKind.Produce).WithAlpha(0.7f));
        if (x.On && x.Lod > 0) // 압축기 떨림
            for (int i = 0; i < 3; i++)
            {
                float j = Mathf.Sin(x.T * 40f + i) * 0.7f * x.Spin;
                Line(ci, x.P(0.3f + i * 0.2f, 0.02f) + new Vector2(j, 0f), x.P(0.34f + i * 0.2f, 0.02f) + new Vector2(j, 0f), new Color(1, 1, 1, 0.18f), 1f);
            }
        if (x.Lod == 2 && x.On) // 성에 반짝임
        {
            int i = (int)(x.T * 1.5f) % 16;
            Dot(ci, x.P(0.04f + Hash(x.Id, i, 11) * 0.92f, 0.16f), 1.1f, Colors.White.WithAlpha(0.6f * Pulse(x.T, 9f)));
        }
        if (x.User != null) // 문을 열었다: 앞으로 쏟아지는 찬 불빛
        {
            var f = x.Front;
            var a = x.C + f * (x.Lv * 0.5f);
            ci.DrawColoredPolygon(new[] { a - x.U * x.Lu * 0.3f, a + x.U * x.Lu * 0.3f, a + x.U * x.Lu * 0.38f + f * 10f, a - x.U * x.Lu * 0.38f + f * 10f }, FrostBlue.WithAlpha(0.12f));
        }
    }

    private static void FridgeFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.7f);
        Tag(ci, x.P(0.13f, 0.5f), "냉", 6, new Color(1, 1, 1, 0.35f));
        Line(ci, x.P(0.03f, 0.13f), x.P(0.97f, 0.13f), FrostBlue.WithAlpha(0.15f), 1f);
    }

    // ─────────────── 오븐 ───────────────

    private static void OvenBody(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        Box(ci, x.B, new Color("#25211e"), 3, new Color("#5a4a3a"), 2);
        var win = new Rect2(x.C + f * 3f - new Vector2(8f, 8f) + f.Abs() * 2f, new Vector2(16f, 16f) - f.Abs() * 6f);
        Glass(ci, win, new Color("#120c09"), 2f); // 유리문
        for (int k = 1; k <= 2; k++) // 안쪽 선반 살
        {
            float s = k / 3f;
            if (f.Y != 0f) Line(ci, new Vector2(win.Position.X + 1, win.Position.Y + win.Size.Y * s), new Vector2(win.End.X - 1, win.Position.Y + win.Size.Y * s), new Color("#4a3f35"), 0.8f);
            else Line(ci, new Vector2(win.Position.X + win.Size.X * s, win.Position.Y + 1), new Vector2(win.Position.X + win.Size.X * s, win.End.Y - 1), new Color("#4a3f35"), 0.8f);
        }
        var side = new Vector2(-f.Y, f.X);
        Line(ci, x.C + f * 11.5f - side * 7f, x.C + f * 11.5f + side * 7f, Chrome, 1.6f); // 손잡이
        var strip = x.C - f * 9.5f;
        Knob(ci, strip - side * 6f, 2.2f, 0.6f, new Color("#3a3430"));
        Knob(ci, strip + side * 6f, 2.2f, 2.2f, new Color("#3a3430"));
        ci.DrawRect(new Rect2(strip - new Vector2(2.5f, 1.5f), new Vector2(5f, 3f)), GlassDark); // 타이머
    }

    private static void OvenLife(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var win = new Rect2(x.C + f * 3f - new Vector2(7f, 7f) + f.Abs() * 2f, new Vector2(14f, 14f) - f.Abs() * 6f);
        if (x.Lit)
        {
            ci.DrawRect(win, Burner.WithAlpha(0.22f * x.Glow));
            Fan(ci, win.GetCenter(), Mathf.Min(win.Size.X, win.Size.Y) * 0.4f, 3, x.Ang(5f), Burner.Lightened(0.3f).WithAlpha(0.55f * x.Glow), 1f); // 대류 팬
        }
        var strip = x.C - f * 9.5f;
        if (x.Lit) ci.DrawRect(new Rect2(strip - new Vector2(2f, 1f), new Vector2(4f, 2f)), Danger.WithAlpha((Mathf.PosMod(x.T, 1f) < 0.6f ? 0.9f : 0.3f) * x.Glow));
        if (x.On && x.Lod > 0) Shimmer(ci, new Rect2(x.C - f * 12f - new Vector2(6f, 2f), new Vector2(12f, 4f)), x.T, 0.5f);
    }

    private static void OvenFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2f, 0.6f);
        Vents(ci, new Rect2(x.C - x.Front * 12f - new Vector2(5f, 1f), new Vector2(10f, 2f)), 4, true, new Color(0, 0, 0, 0.5f), 0.8f);
        Tag(ci, x.C + new Vector2(-x.Front.Y, x.Front.X) * 9f, "°", 6, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 식기 세척기 ───────────────

    private static void DishWasherBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1f2630"), 4, new Color("#6b7788"));
        var tub = x.B.Grow(-3.5f);
        Box(ci, tub, new Color("#141a22"), 3);
        Grille(ci, tub, 4f, new Color("#3a4454"), 0.7f); // 철망 살
        for (int k = 0; k < 4; k++) // 꽂힌 접시 (얇은 타원)
        {
            var p = tub.Position + new Vector2(tub.Size.X * (0.2f + k * 0.2f), tub.Size.Y * 0.3f);
            ci.DrawSetTransform(p, 0f, new Vector2(0.35f, 1f));
            ci.DrawCircle(Vector2.Zero, 4f, new Color("#c9d1dc").WithAlpha(0.75f), true, -1f, true);
            ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        for (int k = 0; k < 3; k++) Dot(ci, tub.Position + new Vector2(tub.Size.X * (0.25f + k * 0.25f), tub.Size.Y * 0.75f), 1.6f, new Color("#9aa6b5").WithAlpha(0.6f)); // 컵
        Dot(ci, x.C, 2f, new Color("#56627a")); // 분사 팔 축
        var f = x.Front;
        Line(ci, x.C + f * 11.5f - new Vector2(f.Y, f.X) * 6f, x.C + f * 11.5f + new Vector2(f.Y, f.X) * 6f, Chrome, 1.4f); // 손잡이 홈
    }

    private static void DishWasherLife(in Fix x)
    {
        var ci = x.Ci;
        float a = x.Ang(7f);
        var arm = Vector2.FromAngle(a) * 8f;
        Line(ci, x.C - arm, x.C + arm, new Color("#8a96a8").WithAlpha(0.9f), 1.6f);
        if (!x.On && x.St != State.Fault) return;
        for (int k = 0; k < (x.Lod == 0 ? 2 : 6); k++) // 물줄기
        {
            float ph = Mathf.PosMod(x.T * 2.2f + k / 6f, 1f);
            var d = Vector2.FromAngle(a + (k % 2 == 0 ? 0f : Mathf.Pi) + ph * 0.6f);
            Dot(ci, x.C + d * (8f + ph * 3f), 0.8f, WaterLight.WithAlpha(0.7f * (1f - ph) * x.Glow));
        }
        if (x.Lod > 0 && Mathf.PosMod(x.T * 0.3f, 1f) < 0.2f) // 뚜껑 틈의 김
            Dot(ci, new Vector2(x.B.End.X - 3f, x.B.Position.Y + 3f) - new Vector2(0f, Mathf.PosMod(x.T * 0.3f, 1f) * 30f), 3f, SteamWhite.WithAlpha(0.2f));
    }

    private static void DishWasherFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2f, 0.6f);
        Dot(ci, new Vector2(x.B.End.X - 4.5f, x.B.End.Y - 4.5f), 1.6f, new Color("#0a0d12")); // 배수구
        Ring(ci, new Vector2(x.B.End.X - 4.5f, x.B.End.Y - 4.5f), 1.6f, Chrome.WithAlpha(0.5f), 0.6f, 10);
        ci.DrawRect(new Rect2(x.B.Position + new Vector2(3f, 3f), new Vector2(4f, 3f)), new Color("#5fa8d0").WithAlpha(0.6f)); // 세제 칸
    }

    // ─────────────── 커피 머신 ───────────────

    private static void CoffeeBody(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        Box(ci, x.B, new Color("#2a2622"), 6, new Color("#8a7a6a"));
        Bevel(ci, x.B, 0.12f);
        var hop = x.C - f * 6.5f - side * 5.5f;
        Can(ci, hop, 5f, new Color("#1a120c"), new Color("#6a5a4a")); // 원두통
        for (int k = 0; k < 5; k++) Dot(ci, hop + new Vector2(Hash(x.Id, k, 21) - 0.5f, Hash(x.Id, k, 22) - 0.5f) * 6f, 0.9f, new Color("#5a3a1e"));
        var grp = x.C - f * 1.5f + side * 3f;
        Dot(ci, grp, 3.5f, Chrome.Darkened(0.3f)); // 추출구
        Line(ci, grp, grp + side * 7f + f * 2f, new Color("#1a1410"), 2.2f); // 포터필터 손잡이
        var tray = new Rect2(x.C + f * 8f - side.Abs() * 8f - f.Abs() * 2.5f, side.Abs() * 16f + f.Abs() * 5f);
        Box(ci, tray, new Color("#15120f"), 1.5f);
        Grille(ci, tray, 2f, new Color("#3a3430"), 0.6f); // 물받이 격자
        Line(ci, x.C - side * 9f, x.C - side * 9f + f * 7f, Chrome, 1f); // 스팀 봉
    }

    private static void CoffeeLife(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        var mug = x.C + f * 7.5f + side * 3f;
        Dot(ci, mug, 3.2f, Cream); // 머그
        Line(ci, mug + side * 3f, mug + side * 5f, Cream, 1.4f);
        float cycle = Mathf.PosMod(x.T * 0.12f, 1f);
        float level = x.On ? Mathf.Clamp(cycle * 2f, 0f, 1f) : 0.4f;
        Dot(ci, mug, 2.2f * level + 0.3f, CoffeeBrown);
        if (x.On && cycle < 0.5f) // 내리는 중
            Line(ci, x.C - f * 1.5f + side * 3f, mug, CoffeeBrown.Lightened(0.15f).WithAlpha(0.8f), 0.9f);
        if (x.Lit && x.Lod > 0)
            for (int i = 0; i < 2; i++)
            {
                float ph = Mathf.PosMod(x.T * 0.7f + i * 0.5f, 1f);
                Dot(ci, mug + new Vector2(Mathf.Sin(ph * 6f) * 1.5f, -ph * 9f), 1f + 1.8f * ph, SteamWhite.WithAlpha(0.3f * (1f - ph) * x.Glow));
            }
        Led(ci, x.C - f * 9f + side * 7f, Amber, x.Glow, 1f);
        if (x.User != null && x.On) // 갈고 있다: 원두가 돈다
        {
            var hop = x.C - f * 6.5f - side * 5.5f;
            for (int k = 0; k < 3; k++) Dot(ci, hop + Vector2.FromAngle(x.T * 8f + k * 2.1f) * 2.5f, 0.9f, new Color("#7a4a26"));
        }
    }

    private static void CoffeeFine(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        Gauge(ci, x.C - f * 9f - side * 1f, 2.4f, x.On ? 0.6f : 0.2f, new Color("#c0392b"));
        Bolts(ci, x.B, 2f, 0.55f);
    }

    // ─────────────── 치료 침대 ───────────────

    private static void MedBedBody(in Fix x)
    {
        var ci = x.Ci;
        foreach (var p in new[] { x.P(0.02f, 0.04f), x.P(0.98f, 0.04f), x.P(0.02f, 0.96f), x.P(0.98f, 0.96f) }) Dot(ci, p, 1.6f, Rubber); // 바퀴
        Box(ci, x.Q(0.02f, 0.06f, 0.98f, 0.94f), new Color("#2a2a30"), 5, Medical.WithAlpha(0.45f));
        Box(ci, x.Q(0.06f, 0.14f, 0.96f, 0.86f), new Color("#cfd6dc").WithAlpha(0.55f), 4); // 매트
        Box(ci, x.Q(0.06f, 0.14f, 0.36f, 0.86f), new Color("#e3e8ec").WithAlpha(0.45f), 4); // 세운 머리 쪽
        Line(ci, x.P(0.36f, 0.16f), x.P(0.36f, 0.84f), new Color(0, 0, 0, 0.3f), 1f);
        Box(ci, x.Q(0.09f, 0.22f, 0.22f, 0.78f), Colors.White.WithAlpha(0.6f), 3); // 베개
        for (int s = 0; s < 2; s++) // 접는 난간 (점선)
            for (int i = 0; i < 6; i++)
                Line(ci, x.P(0.12f + i * 0.13f, s == 0 ? 0.06f : 0.94f), x.P(0.18f + i * 0.13f, s == 0 ? 0.06f : 0.94f), Chrome.WithAlpha(0.8f), 1.2f);
        var cross = x.P(0.9f, 0.5f);
        ci.DrawRect(new Rect2(cross - new Vector2(1.2f, 3.5f), new Vector2(2.4f, 7f)), new Color("#e05a5a"));
        ci.DrawRect(new Rect2(cross - new Vector2(3.5f, 1.2f), new Vector2(7f, 2.4f)), new Color("#e05a5a"));
        // 모니터 팔 (머리 쪽 바깥) · 링거 봉
        var mon = x.R.Position + new Vector2(x.Wide ? 1f : x.R.Size.X - 9f, 1f);
        Box(ci, new Rect2(mon, new Vector2(8f, 6f)), new Color("#0f1418"), 1.5f, new Color("#4a5566"));
        var pole = x.P(0.12f, 0.98f);
        Dot(ci, pole, 1.4f, Chrome);
        Box(ci, new Rect2(pole + new Vector2(-2f, -1f), new Vector2(4f, 5f)), new Color(0.8f, 0.9f, 1f, 0.45f), 1.5f); // 링거 주머니
    }

    private static void MedBedLife(in Fix x)
    {
        var ci = x.Ci;
        var mon = new Rect2(x.R.Position + new Vector2(x.Wide ? 1.5f : x.R.Size.X - 8.5f, 1.5f), new Vector2(7f, 5f));
        bool patient = x.Occupant != null;
        if (x.Lit)
        {
            var pts = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float u = i / 7f;
                float ph = Mathf.PosMod(u * 2f - x.T * 1.2f, 1f);
                float spike = patient && ph > 0.45f && ph < 0.55f ? (ph < 0.5f ? -1f : 0.6f) : 0f;
                pts[i] = new Vector2(mon.Position.X + u * mon.Size.X, mon.GetCenter().Y + spike * 2f);
            }
            ci.DrawPolyline(pts, (patient ? Ecg : x.Accent).WithAlpha(0.9f * x.Glow), 0.7f, true);
        }
        else ci.DrawRect(mon, new Color("#05080a"));
        // 링거 방울: 사람이 누워 있을 때만
        if (patient && x.Lod > 0)
        {
            var pole = x.P(0.12f, 0.98f);
            float ph = Mathf.PosMod(x.T * 0.9f, 1f);
            Dot(ci, pole + new Vector2(0f, 4f + ph * 3f), 0.7f, new Color("#cfe8ff").WithAlpha(1f - ph));
        }
        if (patient) Led(ci, mon.End + new Vector2(1f, -1f), Danger, Mathf.PosMod(x.T * 1.2f, 1f) < 0.15f ? 0.9f : 0.15f, 0.8f);
    }

    private static void MedBedFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.02f, 0.06f, 0.98f, 0.94f), 2.5f, 0.6f);
        Line(ci, x.P(0.12f, 0.98f), x.P(0.3f, 0.86f), new Color(0.8f, 0.9f, 1f, 0.35f), 0.6f); // 링거 줄
        Tag(ci, x.P(0.65f, 0.5f), "+", 7, Medical.WithAlpha(0.4f));
    }

    // ─────────────── 진단 스캐너 ───────────────

    private static void ScannerBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.0f, 0.3f, 1.0f, 0.7f), new Color("#c9d1dc").WithAlpha(0.6f), 3, new Color("#7a8494")); // 눕는 판
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.42f;
        Dot(ci, x.C + new Vector2(1.5f, 2f), rr + 1f, Shadow);
        ci.DrawArc(x.C, rr - 2.5f, 0f, Mathf.Tau, 32, new Color("#e3e8ec"), 5f, true); // 고리 갠트리
        ci.DrawArc(x.C, rr, 0f, Mathf.Tau, 32, new Color("#7a8494"), 1f, true);
        ci.DrawArc(x.C, rr - 5f, 0f, Mathf.Tau, 32, new Color("#5a6474"), 1f, true);
        Dot(ci, x.C, rr - 6f, new Color("#10161c")); // 가운데 구멍
        Box(ci, x.Q(0.25f, 0.4f, 0.75f, 0.6f), new Color("#c9d1dc").WithAlpha(0.5f), 1.5f); // 구멍 속 판
        Box(ci, new Rect2(x.B.End - new Vector2(7f, 5f), new Vector2(6f, 4f)), new Color("#141a22"), 1, new Color("#3a4454")); // 조작판
    }

    private static void ScannerLife(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.42f - 2.5f;
        if (x.Lit)
            for (int k = 0; k < 2; k++)
            {
                float a = x.Ang(3f) + k * Mathf.Pi;
                ci.DrawArc(x.C, rr, a, a + 0.8f, 8, new Color("#7fd4ff").WithAlpha(0.8f * x.Glow), 2.2f, true);
            }
        if (x.On && (x.User != null || x.Occupant != null)) // 훑는 레이저 선
        {
            float u = 0.2f + 0.6f * Pulse(x.T, 1.4f);
            Line(ci, x.P(u, 0.25f), x.P(u, 0.75f), Danger.WithAlpha(0.75f), 1f);
            Dot(ci, x.P(u, 0.5f), 3f, Danger.WithAlpha(0.15f));
        }
        Led(ci, x.B.End - new Vector2(4f, 3f), x.On ? Cyan : Danger, Mathf.Max(x.Glow, 0.2f), 0.9f);
    }

    private static void ScannerFine(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.42f - 2.5f;
        for (int k = 0; k < 8; k++) Dot(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / 8f) * rr, 0.5f, new Color("#5a6474"));
        Tag(ci, x.C + new Vector2(0f, -rr - 1f), "DX", 5, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 멸균기 ───────────────

    private static void AutoclaveBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1c2228"), 3, new Color("#4a5566"));
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.38f;
        Can(ci, x.C, rr, new Color("#8a96a4"), new Color("#c8d0da")); // 압력 용기
        Ring(ci, x.C, rr * 0.82f, new Color("#5a6474"), 1f);
        for (int k = 0; k < 12; k++) Dot(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / 12f) * rr * 0.9f, 0.7f, new Color("#3a4454")); // 둘레 볼트
        ci.DrawArc(x.C, rr * 0.45f, 0f, Mathf.Tau, 16, new Color("#2a303a"), 1.6f, true); // 잠금 핸들 바퀴
        for (int k = 0; k < 4; k++) Line(ci, x.C, x.C + Vector2.FromAngle(k * Mathf.Pi / 2f + 0.4f) * rr * 0.45f, new Color("#2a303a"), 1.4f);
        Dot(ci, x.C, 1.6f, new Color("#c0392b"));
        Dot(ci, new Vector2(x.B.End.X - 3.5f, x.B.Position.Y + 3.5f), 1.6f, Brass); // 안전 밸브
    }

    private static void AutoclaveLife(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.38f;
        float cycle = Mathf.PosMod(x.T * 0.05f * Mathf.Max(0.2f, x.Spin), 1f);
        if (x.Lit) ci.DrawArc(x.C, rr + 1.6f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * cycle, 24, (cycle < 0.8f ? Amber : Good).WithAlpha(0.8f * x.Glow), 1.4f, true); // 멸균 진행 고리
        Gauge(ci, new Vector2(x.B.Position.X + 4f, x.B.Position.Y + 4f), 3f, x.On ? Mathf.Clamp(cycle * 1.6f, 0f, 1f) : 0f, new Color("#c0392b"));
        if (x.On && x.Lod > 0 && Mathf.PosMod(x.T * 0.17f, 1f) < 0.12f) // 안전 밸브가 가끔 김을 뺀다
        {
            float ph = Mathf.PosMod(x.T * 0.17f, 1f) / 0.12f;
            Dot(ci, new Vector2(x.B.End.X - 3.5f, x.B.Position.Y + 3.5f - ph * 10f), 1.5f + 3f * ph, SteamWhite.WithAlpha(0.45f * (1f - ph)));
        }
    }

    private static void AutoclaveFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        Plate(ci, new Rect2(new Vector2(x.B.Position.X + 2f, x.B.End.Y - 4f), new Vector2(9f, 2.5f)), new Color("#c8b27a"));
        Tag(ci, x.C + new Vector2(0f, Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.25f), "121°", 5, new Color(0, 0, 0, 0.6f));
    }

    // ─────────────── 우주복 보관함 ───────────────

    private static int Slots(in Fix x) => Mathf.Clamp(x.F.Storage?.Capacity ?? 2, 1, 6);

    private static void SuitLockerBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1f2530"), 4, new Color("#3a4455"), 2);
        int n = Slots(x);
        Line(ci, x.P(0.03f, 0.12f), x.P(0.97f, 0.12f), Chrome.WithAlpha(0.7f), 1.4f); // 걸이 봉
        for (int k = 0; k < n; k++)
        {
            float u0 = (float)k / n, u1 = (float)(k + 1) / n;
            if (k > 0) Line(ci, x.P(u0, 0.08f), x.P(u0, 0.95f), new Color("#141820"), 1.2f); // 칸막이
            var hook = x.P((u0 + u1) * 0.5f, 0.12f);
            ci.DrawArc(hook + x.V * 1.5f, 1.4f, 0f, Mathf.Pi, 6, Chrome, 0.8f, true); // 걸이
            var slot = x.Q(u0 + 0.06f / n, 0.22f, u1 - 0.06f / n, 0.92f);
            Box(ci, slot, new Color(0, 0, 0, 0f), 3, new Color("#2a3240")); // 빈 자리 테두리
        }
        Box(ci, x.Q(0.0f, 0.0f, 1f, 1f).Grow(-1.5f), new Color(0.7f, 0.85f, 1f, 0.04f), 4); // 유리문
        Line(ci, x.P(0.05f, 0.97f), x.P(0.35f, 0.97f), new Color(1, 1, 1, 0.15f), 1f);
    }

    private static void SuitLockerLife(in Fix x)
    {
        var ci = x.Ci;
        int n = Slots(x);
        int suits = x.F.Storage?.Count(ItemKind.Suit) ?? 0;
        for (int k = 0; k < n; k++)
        {
            float u = (k + 0.5f) / n;
            if (k < suits)
            {
                var helm = x.P(u, 0.36f);
                float hr = Mathf.Min(x.Lu / n, x.Lv) * 0.22f;
                Box(ci, x.Q(u - 0.3f / n, 0.42f, u + 0.3f / n, 0.88f), new Color("#d8dee8").WithAlpha(0.75f), 3); // 몸통
                ci.DrawRect(x.Q(u - 0.08f / n, 0.5f, u + 0.08f / n, 0.6f), new Color("#e0763a").WithAlpha(0.8f)); // 가슴 띠
                Dot(ci, helm, hr, new Color("#e8ecf2").WithAlpha(0.85f)); // 헬멧
                ci.DrawArc(helm, hr * 0.62f, 0.3f, 2.8f, 8, new Color("#d4a640"), 1.6f, true); // 금빛 바이저
                if (x.Lod == 2) Dot(ci, helm + new Vector2(-hr * 0.3f, -hr * 0.3f), 0.8f, Colors.White.WithAlpha(0.5f + 0.4f * Pulse(x.T + k, 1.3f)));
            }
            Led(ci, x.P(u, 0.97f) - x.V * 2f, k < suits ? Good : Palette.TextMuted, 0.85f, 1f);
        }
    }

    private static void SuitLockerFine(in Fix x)
    {
        var ci = x.Ci;
        foreach (float u in new[] { 0.06f, 0.94f }) { Dot(ci, x.P(u, 0.03f), 0.8f, Chrome); Dot(ci, x.P(u, 0.97f), 0.8f, Chrome); } // 경첩
        Tag(ci, x.P(0.5f, 0.04f) + x.V * 2f, "EVA", 5, new Color("#e0763a").WithAlpha(0.7f));
    }

    // ─────────────── 선반 ───────────────

    private static void ShelfBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.R.Grow(-2f), new Color("#1b2029"), 3, new Color("#2b3341"));
        foreach (var c in x.F.Cells)
        {
            var cr = ShipView.CellRect(c).Grow(-3f);
            ci.DrawRect(cr, new Color("#20262f"));
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) // 타공판
                Dot(ci, cr.Position + new Vector2((i + 0.5f) * cr.Size.X / 4f, (j + 0.5f) * cr.Size.Y / 4f), 0.7f, new Color("#12161c"));
            ci.DrawRect(new Rect2(cr.Position.X, cr.End.Y - 2f, cr.Size.X, 2f), new Color("#3a4454")); // 선반 앞턱
        }
        foreach (var p in new[] { x.R.Position + new Vector2(2, 2), new Vector2(x.R.End.X - 6, x.R.Position.Y + 2), new Vector2(x.R.Position.X + 2, x.R.End.Y - 6), x.R.End - new Vector2(6, 6) })
            ci.DrawRect(new Rect2(p, new Vector2(4f, 4f)), new Color("#3a4454")); // 기둥
    }

    private static readonly Color[] CrateTints = { new("#3a4250"), new("#4a3e30"), new("#2c4440"), new("#3d3548") };

    private static void ShelfLife(in Fix x)
    {
        // 짐이 찬 만큼 상자 · 통 · 자루가 놓인다 (자리마다 모양이 다르다)
        var ci = x.Ci;
        if (x.F.Storage is not Inventory inv || inv.Capacity <= 0) return;
        float fill = inv.Total / (float)inv.Capacity;
        int show = Mathf.CeilToInt(fill * x.F.Cells.Count * 2);
        int i = 0;
        foreach (var c in x.F.Cells)
        {
            var cr = ShipView.CellRect(c);
            for (int half = 0; half < 2; half++, i++)
            {
                if (i >= show) return;
                int h = (c.X * 7 + c.Y * 13 + half) & 3;
                var p = new Vector2(cr.Position.X + 10f + half * 12f, cr.GetCenter().Y);
                var col = CrateTints[h];
                if (h == 0 || h == 3) // 상자: 뚜껑 십자
                {
                    var b = new Rect2(p - new Vector2(5f, 8f), new Vector2(10f, 16f));
                    Box(ci, b, col, 1.5f, col.Lightened(0.2f));
                    Line(ci, b.Position + new Vector2(0f, 8f), b.Position + new Vector2(10f, 8f), col.Darkened(0.3f), 1f);
                }
                else if (h == 1) // 통: 둥근 위 · 테
                {
                    Can(ci, p + new Vector2(0f, -3f), 4.5f, col, col.Lightened(0.3f));
                    Can(ci, p + new Vector2(0f, 5f), 3.5f, col.Darkened(0.1f), col.Lightened(0.2f));
                }
                else // 자루: 묶은 매듭
                {
                    Box(ci, new Rect2(p - new Vector2(5f, 7f), new Vector2(10f, 14f)), new Color("#5a5040"), 5f);
                    Dot(ci, p + new Vector2(0f, -7f), 1.4f, new Color("#3a3226"));
                }
            }
        }
    }

    private static void ShelfFine(in Fix x)
    {
        var ci = x.Ci;
        foreach (var c in x.F.Cells)
        {
            var cr = ShipView.CellRect(c).Grow(-3f);
            ci.DrawRect(new Rect2(cr.GetCenter().X - 3f, cr.End.Y - 1.8f, 6f, 1.5f), Cream.WithAlpha(0.55f)); // 칸 딱지
        }
        Bolts(ci, x.R.Grow(-2f), 2f, 0.6f);
    }

    // ─────────────── 비상 물자함 ───────────────

    private static void CacheBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-4f);
        ci.DrawRect(new Rect2(r.Position.X + 3f, r.Position.Y - 1.5f, r.Size.X - 6f, 2f), new Color("#3a4454")); // 벽 받침
        Box(ci, r, new Color("#6a2c16"), 4, new Color("#e0763a"), 2);
        var win = new Rect2(r.Position.X + 3, r.Position.Y + 3, r.Size.X - 6, r.Size.Y * 0.55f);
        Glass(ci, win, new Color("#1a1c22"), 1.5f);
        var cross = new Vector2(r.GetCenter().X, r.End.Y - 5.5f);
        ci.DrawRect(new Rect2(cross.X - 3.5f, cross.Y - 1f, 7f, 2f), new Color("#f2efe8"));
        ci.DrawRect(new Rect2(cross.X - 1f, cross.Y - 3.5f, 2f, 7f), new Color("#f2efe8"));
        ci.DrawRect(new Rect2(r.End.X - 4, r.GetCenter().Y - 3, 2, 6), new Color("#c9cfd8")); // 손잡이
        Line(ci, new Vector2(r.End.X - 3f, r.GetCenter().Y + 3f), new Vector2(r.End.X - 1f, r.End.Y - 2f), new Color("#c0392b"), 0.8f); // 봉인 줄
    }

    private static void CacheLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.F.Storage is not Inventory inv) return;
        var r = x.R.Grow(-4f);
        var win = new Rect2(r.Position.X + 4, r.Position.Y + 4, r.Size.X - 8, r.Size.Y * 0.55f - 2f);
        float px = win.Position.X + 1f;
        int total = 0;
        void Items(ItemKind k, Color col, float w)
        {
            int n = Mathf.Min(inv.Count(k), 3);
            total += n;
            for (int i = 0; i < n && px + w <= win.End.X; i++, px += w + 1f)
                ci.DrawRect(new Rect2(px, win.End.Y - win.Size.Y * 0.8f, w, win.Size.Y * 0.8f), col);
        }
        Items(ItemKind.Sealant, new Color("#e9c948"), 2.5f);
        Items(ItemKind.MedKit, new Color("#f2f2f2"), 3f);
        Items(ItemKind.Extinguisher, new Color("#d33b2c"), 2.5f);
        if (total == 0) Box(ci, r.Grow(1f), new Color(0, 0, 0, 0f), 5, Amber.WithAlpha(0.35f + 0.35f * Pulse(x.T, 3f)), 1); // 비었다
        else if (x.Lod == 2) Dot(ci, new Vector2(r.End.X - 1f, r.End.Y - 2f), 0.9f, Danger.WithAlpha(0.7f)); // 봉인 납
    }

    private static void CacheFine(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-4f);
        Bolt(ci, new Vector2(r.Position.X + 4f, r.Position.Y - 0.5f), 0.6f);
        Bolt(ci, new Vector2(r.End.X - 4f, r.Position.Y - 0.5f), 0.6f);
        Tag(ci, new Vector2(r.GetCenter().X - 5f, r.End.Y - 5.5f), "비상", 5, new Color(1, 1, 1, 0.5f));
    }
}
