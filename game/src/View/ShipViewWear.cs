using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.0 옷 · 보호구 · 안경 / v18.1 편지 / v18.9 대신 서는 당직 · 맞바꾸기 — 읽기만 한다.
//  옷마다 실루엣 · 무늬가 다르다: 작업복(어깨 반사띠 · 가슴 주머니) · 평상복(사람마다 취향 무늬) · 잠옷(물방울 · 헐렁한 깃) ·
//  방열복(은빛 누빔 · 움직이는 광택 · 두건과 금빛 창) · 납 조끼(노란 조끼 · 검은 삼엽 표지) · 장갑(가죽색 손) · 때(얼룩 · 그을음) · 찢어짐(들쭉날쭉한 틈).
//  안경(테 · 알의 반짝임) · 금 간 알(번개 금) · 테이프로 감은 다리 · 보안경(머리띠 · 호박색 창).
//  편지: 소식마다 봉투가 다르다 — 안부(항공 줄무늬) · 결혼(분홍 리본) · 출산(작은 발자국) · 병(붉은 십자) · 재촉(붉은 밑줄) · 부고(검은 띠) · 소포(끈 묶은 상자).
//  밀린 편지는 통신 콘솔 곁에 고무줄로 묶인 더미로 쌓인다.
public partial class ShipView
{
    private static readonly Color[] TasteColors =
    {
        new("#7aa95c"), new("#5f8fc4"), new("#c9a23f"), new("#94557e"), new("#8a8f98"), new("#b5523f"),
    };

    /// <summary>옷 바탕색 (작업복은 역할 색 그대로).</summary>
    private Color WearCloth(CrewMember c, Color role)
    {
        var o = _world.Personal.Wear.Peek(c);
        if (o == null) return role;
        return o.Wearing switch
        {
            Garment.Casual => TasteColors[o.Taste % TasteColors.Length],
            Garment.Sleep => new Color("#cfd9ec").Lerp(TasteColors[o.Taste % TasteColors.Length], 0.25f),
            Garment.Heat => new Color("#c9ccd2"),
            Garment.RadVest => o.Under == Garment.Casual ? TasteColors[o.Taste % TasteColors.Length] : role,
            _ => role,
        };
    }

    /// <summary>손: 장갑 (가죽 · 방열복은 은빛 벙어리장갑).</summary>
    private Color WearGlove(CrewMember c, Color skin)
    {
        var o = _world.Personal.Wear.Peek(c);
        if (o == null) return skin;
        if (o.Wearing == Garment.Heat) return new Color("#aeb3bb");
        return o.Gloves ? new Color("#9a6434") : skin;
    }

    private void PaintWear(CanvasItem ci, Transform2D xf, CrewMember c, Vector2 torso, float D, float W, Color accent, bool detail, float t)
    {
        var o = _world.Personal.Wear.Peek(c);
        if (o == null) return;
        var ink = new Color(0.1f, 0.12f, 0.16f, 0.55f);
        switch (o.Wearing)
        {
            case Garment.Work:
                // 어깨 반사띠 두 줄 · 가슴 주머니 (단추 하나)
                for (int k = -1; k <= 1; k += 2)
                    ci.DrawLine(new Vector2(torso.X - D * 0.7f, k * W * 0.62f), new Vector2(torso.X + D * 0.55f, k * W * 0.62f), new Color("#e8e27a").WithAlpha(0.8f), 0.7f, true);
                if (detail)
                {
                    Gfx.RoundRect(ci, new Rect2(torso.X + D * 0.15f, -W * 0.42f, 1.6f, 1.9f), new Color(0, 0, 0, 0.18f), 0.3f);
                    ci.Circle(new Vector2(torso.X + D * 0.15f + 0.8f, -W * 0.42f + 0.4f), 0.22f, ink, true, -1f, true);
                }
                break;
            case Garment.Casual:
                // 취향 무늬: 물결 · 체크 · 가로줄 · 점 · 브이 · 큰 단추
                var deco = new Color(1f, 1f, 1f, 0.35f);
                switch (o.Taste % 6)
                {
                    case 0: for (int k = -2; k <= 2; k++) ci.Polyline(new[] { new Vector2(torso.X - D * 0.6f, k * 1.4f), new Vector2(torso.X, k * 1.4f + 0.5f), new Vector2(torso.X + D * 0.6f, k * 1.4f) }, deco, 0.35f, true); break;
                    case 1:
                        for (int k = -2; k <= 2; k++) ci.DrawLine(new Vector2(torso.X - D * 0.7f, k * W * 0.3f), new Vector2(torso.X + D * 0.7f, k * W * 0.3f), deco, 0.3f);
                        for (int j = -1; j <= 1; j++) ci.DrawLine(new Vector2(torso.X + j * D * 0.4f, -W * 0.8f), new Vector2(torso.X + j * D * 0.4f, W * 0.8f), deco, 0.3f);
                        break;
                    case 2: for (int j = -1; j <= 1; j++) ci.DrawLine(new Vector2(torso.X + j * D * 0.4f, -W * 0.85f), new Vector2(torso.X + j * D * 0.4f, W * 0.85f), deco, 0.6f); break;
                    case 3: for (int k = -2; k <= 2; k++) for (int j = -1; j <= 1; j++) ci.Circle(new Vector2(torso.X + j * D * 0.45f, k * W * 0.32f), 0.35f, deco, true, -1f, true); break;
                    case 4: ci.Polyline(new[] { new Vector2(torso.X + D * 0.9f, -W * 0.35f), new Vector2(torso.X + D * 0.2f, 0f), new Vector2(torso.X + D * 0.9f, W * 0.35f) }, deco, 0.6f, true); break;
                    default: for (int k = -1; k <= 1; k++) ci.Circle(new Vector2(torso.X + D * 0.55f, k * 1.6f), 0.45f, deco, true, -1f, true); break;
                }
                break;
            case Garment.Sleep:
                // 물방울 무늬 · 헐렁한 깃
                for (int k = -2; k <= 2; k++)
                    for (int j = -1; j <= 1; j++)
                        if ((k + j) % 2 == 0) ci.Circle(new Vector2(torso.X + j * D * 0.45f, k * W * 0.33f), 0.5f, new Color(1f, 1f, 1f, 0.55f), true, -1f, true);
                ci.Arc(torso + new Vector2(D * 0.7f, 0f), W * 0.45f, Mathf.Pi * 0.6f, Mathf.Pi * 1.4f, 10, new Color(1f, 1f, 1f, 0.6f), 0.6f, true);
                break;
            case Garment.Heat:
                // 은빛 누빔 (마름모 격자) · 움직이는 광택
                var seam = new Color("#8d929b").WithAlpha(0.8f);
                for (int k = -3; k <= 3; k++)
                {
                    ci.DrawLine(new Vector2(torso.X - D * 0.8f, k * W * 0.28f - D * 0.4f), new Vector2(torso.X + D * 0.8f, k * W * 0.28f + D * 0.4f), seam, 0.3f);
                    ci.DrawLine(new Vector2(torso.X - D * 0.8f, k * W * 0.28f + D * 0.4f), new Vector2(torso.X + D * 0.8f, k * W * 0.28f - D * 0.4f), seam, 0.3f);
                }
                float sh = Mathf.Sin(t * 2.2f + c.Id) * W * 0.6f;
                ci.DrawLine(new Vector2(torso.X - D * 0.5f, sh - 1f), new Vector2(torso.X + D * 0.5f, sh + 1f), new Color(1f, 1f, 1f, 0.55f), 0.9f, true);
                break;
            case Garment.RadVest:
                // 노란 조끼 · 납판 이음 · 등의 삼엽 표지
                Oval(ci, xf, torso + new Vector2(-0.4f, 0f), D * 0.92f, W * 0.95f, new Color("#e0b12f"));
                for (int k = -1; k <= 1; k += 2) ci.DrawLine(new Vector2(torso.X - D * 0.8f, k * W * 0.45f), new Vector2(torso.X + D * 0.6f, k * W * 0.45f), new Color("#6b5a20"), 0.4f);
                var tc = new Vector2(torso.X - D * 0.45f, 0f);
                for (int i = 0; i < 3; i++)
                {
                    float a0 = i * Mathf.Tau / 3f - Mathf.Pi / 2f;
                    ci.Poly(new[] { tc, tc + Vector2.FromAngle(a0 - 0.45f) * 1.6f, tc + Vector2.FromAngle(a0 + 0.45f) * 1.6f }, new Color("#1c1c1c"));
                }
                ci.Circle(tc, 0.35f, new Color("#e0b12f"), true, -1f, true);
                break;
        }
        // 때: 기름 · 분진 · 그을음 얼룩 (사람마다 자리가 다르다)
        float dirt = c.Soil.ClothesMax;
        if (dirt > 0.2f)
        {
            bool soot = c.Soil.Clothes[(int)SoilKind.Soot] >= dirt * 0.9f;
            var col = soot ? new Color(0.08f, 0.08f, 0.08f, 0.25f + 0.4f * dirt) : new Color(0.35f, 0.25f, 0.12f, 0.2f + 0.35f * dirt);
            int n = 2 + (int)(dirt * 4f);
            for (int i = 0; i < n; i++)
            {
                uint h = (uint)(c.Id * 73 + i * 19);
                var p = new Vector2(torso.X + ((h % 7) / 6f - 0.5f) * D * 1.3f, ((h / 7 % 9) / 8f - 0.5f) * W * 1.6f);
                Oval(ci, xf, p, 0.6f + (h % 3) * 0.25f, 0.45f + (h % 2) * 0.3f, col);
            }
        }
        // 찢어진 소매: 들쭉날쭉한 틈 사이로 살이 보인다
        if (o.Torn > 0.3f && detail)
        {
            float side = c.Id % 2 == 0 ? -1f : 1f;
            var pts = new Vector2[5];
            for (int i = 0; i < 5; i++) pts[i] = new Vector2(torso.X - D * 0.5f + i * D * 0.25f, side * (W * 0.8f - (i % 2) * 0.9f));
            ci.Polyline(pts, new Color("#e6b896"), 0.5f, true);
            ci.Polyline(pts, ink, 0.2f, true);
        }
    }

    /// <summary>머리 위: 방열복 두건 · 보안경 · 안경 (금 · 테이프).</summary>
    private void PaintEyewear(CanvasItem ci, Transform2D xf, Vector2 head, CrewMember c, int lod)
    {
        var o = _world.Personal.Wear.Peek(c);
        if (o == null || lod < 1) return;
        float t = _time;
        if (o.Wearing == Garment.Heat)
        {
            ci.Circle(head, 3.9f, new Color("#c9ccd2"), true, -1f, true);
            ci.Arc(head, 3.9f, 0f, Mathf.Tau, 16, new Color("#8d929b"), 0.4f, true);
            Gfx.RoundRect(ci, new Rect2(head.X + 1.6f, -1.9f, 1.9f, 3.8f), new Color("#b8902e"), 0.6f);
            ci.DrawLine(new Vector2(head.X + 2.0f, -1.5f), new Vector2(head.X + 2.0f, -0.3f + Mathf.Sin(t * 2f) * 0.4f), new Color(1f, 0.95f, 0.7f, 0.7f), 0.35f, true);
            return;
        }
        if (o.Goggles)
        {
            ci.Arc(head, 3.5f, 0f, Mathf.Tau, 18, new Color("#3a3f48"), 0.7f, true);
            Gfx.RoundRect(ci, new Rect2(head.X + 2.2f, -2.1f, 1.4f, 4.2f), new Color("#d08a2a").WithAlpha(0.85f), 0.5f);
            ci.DrawLine(new Vector2(head.X + 2.5f, -1.6f), new Vector2(head.X + 2.5f, -0.6f), new Color(1f, 1f, 1f, 0.6f), 0.3f, true);
            return;
        }
        if (!o.NeedsGlasses || o.Eyes is Specs.Lost or Specs.None) return;
        var frame = new Color("#2b2b30");
        float fx = head.X + 2.9f;
        ci.DrawLine(new Vector2(fx - 0.3f, -2.3f), new Vector2(fx - 0.3f, 2.3f), frame, 0.35f, true); // 테
        for (int k = -1; k <= 1; k += 2)
        {
            var lens = new Vector2(fx, k * 1.15f);
            ci.Circle(lens, 0.75f, new Color(0.75f, 0.85f, 0.95f, 0.45f), true, -1f, true);
            ci.Arc(lens, 0.75f, 0f, Mathf.Tau, 10, frame, 0.25f, true);
            ci.DrawLine(new Vector2(fx - 0.6f, k * 2.3f), new Vector2(head.X - 0.5f, k * 3.1f), frame, 0.25f, true); // 다리
        }
        if (o.Eyes == Specs.Cracked) // 한쪽 알에 번개 금
            ci.Polyline(new[] { new Vector2(fx - 0.4f, 0.6f), new Vector2(fx + 0.1f, 1.0f), new Vector2(fx - 0.2f, 1.3f), new Vector2(fx + 0.4f, 1.8f) }, new Color(1f, 1f, 1f, 0.9f), 0.2f, true);
        else if (o.Eyes == Specs.Taped) // 콧등에 감은 흰 테이프
            Gfx.RoundRect(ci, new Rect2(fx - 0.7f, -0.45f, 1.0f, 0.9f), new Color("#f2efe4"), 0.15f);
        else if (Mathf.PosMod(t + c.Id * 0.37f, 4f) < 0.25f)
            ci.Circle(new Vector2(fx + 0.2f, -1.4f), 0.25f, new Color(1f, 1f, 1f, 0.9f), true, -1f, true); // 알이 반짝
    }

    /// <summary>사람 곁: 읽는 편지 · 쓰는 답장 · 내기 빚으로 대신 서는 당직의 완장.</summary>
    private void PaintPersonal(CanvasItem ci, CrewMember c, Vector2 body, Vector2 facing, float rr, float s, int lod)
    {
        if (lod < 1 || c.Dead) return;
        var p = _world.Personal;
        var f = facing.LengthSquared() > 1e-4f ? facing.Normalized() : Vector2.Right;
        var side = f.Orthogonal();
        if (p.Mail.ReadingNow(c) is News kind)
            PaintLetter(ci, body + f * rr * 1.15f, f, s, kind, 0f);
        else if (c.Job?.Activity is LetterActivity && c.Pose == Pose.Sitting)
        {
            var at = body + f * rr * 1.2f;
            PaintLetter(ci, at, f, s, News.Hello, 1f);
            var tip = at + side * (Mathf.Sin(_time * 5f + c.Id) * 2.2f * s) + f * 1.5f * s;
            ci.DrawLine(tip, tip + (side * 0.5f - f).Normalized() * 5f * s, new Color("#2d3b6a"), 1.3f * s, true); // 펜
            ci.Circle(tip, 0.6f * s, new Color("#111"), true, -1f, true);
        }
        if (p.Bets.DebtWatch.TryGetValue(c.Id, out long until) && until > _world.Tick && c.CoveringUntil > _world.Tick)
        {
            // 대신 서는 당직: 왼팔에 노란 완장 (줄 두 개)
            var arm = body + side * rr * 0.85f - f * rr * 0.1f;
            var band = new[] { arm - f * 2f * s - side * 1.2f * s, arm + f * 2f * s - side * 1.2f * s, arm + f * 2f * s + side * 1.2f * s, arm - f * 2f * s + side * 1.2f * s };
            ci.Poly(band, new Color("#f2c84b"));
            ci.DrawLine(arm - f * 2f * s, arm + f * 2f * s, new Color("#7a5a12"), 0.6f * s, true);
        }
    }

    /// <summary>편지 한 장 (쓰는 중이면 빈 줄이 늘어난다) — 봉투 · 장식이 소식마다 다르다.</summary>
    private void PaintLetter(CanvasItem ci, Vector2 at, Vector2 f, float s, News kind, float writing)
    {
        var side = f.Orthogonal();
        float w = 5.5f * s, h = 7f * s;
        Vector2 P(float u, float v) => at + side * u * w + f * v * h; // u: −0.5~0.5 · v: −0.5~0.5
        var paper = kind == News.Parcel ? new Color("#b07a43") : new Color("#f4efe2");
        ci.Poly(new[] { P(-0.5f, -0.5f), P(0.5f, -0.5f), P(0.5f, 0.5f), P(-0.5f, 0.5f) }, paper);
        var ink = new Color(0.25f, 0.27f, 0.32f, 0.7f);
        switch (kind)
        {
            case News.Parcel: // 끈 묶은 상자
                ci.DrawLine(P(0f, -0.5f), P(0f, 0.5f), new Color("#e8dcc0"), 0.8f * s, true);
                ci.DrawLine(P(-0.5f, 0f), P(0.5f, 0f), new Color("#e8dcc0"), 0.8f * s, true);
                return;
            case News.Death: // 검은 띠 테두리
                ci.Polyline(new[] { P(-0.5f, -0.5f), P(0.5f, -0.5f), P(0.5f, 0.5f), P(-0.5f, 0.5f), P(-0.5f, -0.5f) }, new Color("#111"), 1.1f * s, true);
                break;
            case News.Wedding: // 분홍 리본
                ci.Poly(new[] { P(0f, 0.38f), P(-0.3f, 0.2f), P(-0.3f, 0.5f) }, new Color("#f08bb0"));
                ci.Poly(new[] { P(0f, 0.38f), P(0.3f, 0.2f), P(0.3f, 0.5f) }, new Color("#f08bb0"));
                break;
            case News.Birth: // 작은 발자국 둘
                ci.Circle(P(-0.15f, 0.3f), 0.55f * s, new Color("#7fb3e6"), true, -1f, true);
                ci.Circle(P(0.15f, 0.36f), 0.55f * s, new Color("#f2a0bf"), true, -1f, true);
                break;
            case News.Ill: // 붉은 십자
                ci.DrawLine(P(0.25f, 0.2f), P(0.25f, 0.45f), new Color("#d23c3c"), 0.9f * s, true);
                ci.DrawLine(P(0.12f, 0.33f), P(0.38f, 0.33f), new Color("#d23c3c"), 0.9f * s, true);
                break;
            case News.Nag: // 붉은 밑줄 두 번
                ci.DrawLine(P(-0.4f, 0.1f), P(0.4f, 0.1f), new Color("#d23c3c"), 0.5f * s, true);
                ci.DrawLine(P(-0.4f, 0.2f), P(0.3f, 0.2f), new Color("#d23c3c"), 0.5f * s, true);
                break;
            default: // 안부: 항공 줄무늬 테두리
                for (int i = 0; i < 5; i++)
                {
                    float u = -0.5f + i * 0.25f;
                    ci.DrawLine(P(u, -0.5f), P(u + 0.12f, -0.5f), i % 2 == 0 ? new Color("#d23c3c") : new Color("#2f5fb3"), 0.9f * s, true);
                }
                break;
        }
        int lines = writing > 0f ? 1 + (int)(Mathf.PosMod(_time * 0.6f, 4f)) : 4;
        for (int i = 0; i < lines; i++) ci.DrawLine(P(-0.38f, -0.35f + i * 0.14f), P(0.38f - (i % 2) * 0.15f, -0.35f + i * 0.14f), ink, 0.35f * s, true);
    }

    /// <summary>바닥: 통신 콘솔 곁에 밀린 편지 더미 · 맞바꾸는 물건이 두 사람 사이를 오간다.</summary>
    private void PaintPersonalFloor(CanvasItem ci)
    {
        var p = _world.Personal;
        var mail = p.Mail;
        int pile = mail.CommsUp ? 0 : mail.Outbox + mail.Waiting;
        if (pile > 0 && _world.Comms.Console is Furniture con && con.Cells.Count > 0)
        {
            var r = CellRect(con.Cells[0]);
            var basePos = r.Position + new Vector2(T * 0.15f, T * 0.85f);
            int n = Mathf.Min(8, pile);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector2((i % 3 - 1) * 1.5f, -i * 2.2f);
                float rot = ((i * 37) % 9 - 4) * 0.05f;
                var c0 = basePos + off;
                var dx = Vector2.FromAngle(rot) * 7f;
                var dy = Vector2.FromAngle(rot + Mathf.Pi / 2f) * 4.5f;
                ci.Poly(new[] { c0 - dx - dy, c0 + dx - dy, c0 + dx + dy, c0 - dx + dy }, i % 3 == 0 ? new Color("#efe6cf") : new Color("#f6f1e4"));
                ci.Polyline(new[] { c0 - dx - dy, c0, c0 + dx - dy }, new Color(0.4f, 0.4f, 0.45f, 0.6f), 0.6f, true); // 봉투 뚜껑
            }
            ci.DrawLine(basePos + new Vector2(0f, 5f), basePos + new Vector2(0f, -n * 2.2f - 4f), new Color("#b5523f"), 1.2f, true); // 고무줄
        }
        // 맞바꾸기: 두 물건이 엇갈려 오간다 (게임 시각 20분)
        foreach (var (a, b, ga, gb, tick) in p.Bets.Trades)
        {
            long age = _world.Tick - tick;
            if (age < 0 || age > SimTime.Minutes(20)) continue;
            var ca = _world.Crew.FirstOrDefault(x => x.Id == a);
            var cb = _world.Crew.FirstOrDefault(x => x.Id == b);
            if (ca == null || cb == null || ca.Room != cb.Room) continue;
            float k = Mathf.Clamp(age / (float)SimTime.Minutes(4), 0f, 1f);
            var pa = CrewPx(ca);
            var pb = CrewPx(cb);
            var lift = new Vector2(0f, -10f * Mathf.Sin(k * Mathf.Pi));
            PaintGoods(ci, pa.Lerp(pb, k) + lift, ga);
            if (ga != gb) PaintGoods(ci, pb.Lerp(pa, k) - lift, gb);
        }
    }

    private static void PaintGoods(CanvasItem ci, Vector2 at, Goods g)
    {
        switch (g)
        {
            case Goods.Chocolate: // 칸이 진 판 초콜릿
                Gfx.RoundRect(ci, new Rect2(at - new Vector2(4f, 2.5f), new Vector2(8f, 5f)), new Color("#5a3420"), 0.8f);
                ci.DrawLine(at + new Vector2(-1.3f, -2.5f), at + new Vector2(-1.3f, 2.5f), new Color("#3b2214"), 0.6f);
                ci.DrawLine(at + new Vector2(1.3f, -2.5f), at + new Vector2(1.3f, 2.5f), new Color("#3b2214"), 0.6f);
                ci.DrawLine(at + new Vector2(-4f, 0f), at + new Vector2(4f, 0f), new Color("#3b2214"), 0.6f);
                break;
            case Goods.Coffee: // 접힌 윗단의 커피 봉지
                ci.Poly(new[] { at + new Vector2(-3f, -4f), at + new Vector2(3f, -4f), at + new Vector2(3.6f, 4f), at + new Vector2(-3.6f, 4f) }, new Color("#b98b5a"));
                ci.DrawLine(at + new Vector2(-3f, -2.6f), at + new Vector2(3f, -2.6f), new Color("#6b4b2a"), 0.8f);
                ci.Circle(at + new Vector2(0f, 1f), 1.2f, new Color("#4a2e18"), true, -1f, true);
                break;
            case Goods.Tea: // 초록 주머니 · 잎
                ci.Circle(at, 3.2f, new Color("#5e8f4e"), true, -1f, true);
                ci.Poly(new[] { at + new Vector2(-1.5f, 0.5f), at + new Vector2(0f, -1.8f), at + new Vector2(1.5f, 0.5f), at + new Vector2(0f, 1.2f) }, new Color("#b9e08f"));
                ci.DrawLine(at + new Vector2(0f, -3.2f), at + new Vector2(0f, -5f), new Color("#e8e2c8"), 0.6f, true);
                break;
            default: // 새 양말 (ㄴ자)
                ci.Poly(new[] { at + new Vector2(-1.5f, -4f), at + new Vector2(1.2f, -4f), at + new Vector2(1.2f, 1.5f), at + new Vector2(3.5f, 1.5f), at + new Vector2(3.5f, 4f), at + new Vector2(-1.5f, 4f) }, new Color("#d9dde6"));
                ci.DrawLine(at + new Vector2(-1.5f, -3f), at + new Vector2(1.2f, -3f), new Color("#c05050"), 0.8f);
                break;
        }
    }
}
