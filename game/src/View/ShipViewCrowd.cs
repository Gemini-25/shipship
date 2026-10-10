using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.4 공간과 협력 · 줄 서기 · 구경꾼 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다).
// 바닥 층: 펼친 작업장(뚜껑 연 2단 공구함 · 볼트가 칸칸이 담긴 부품 쟁반 · 비스듬히 기대 둔 덮개판 · 냉각 핀 두른 모터) — 비워 둔 자리는 먼지 빛 · "작업 중" 꼬리표,
//   누가 건드렸으면 쟁반 밖으로 흩어진 볼트 · 분해선(가까이) · 꺼낸 앞 상자(푸른 부품 통 · 글자 · 손잡이 구멍) · 카트 짐을 옮겨 싣는 문턱(내린 상자 · 넘기는 화살) ·
//   옆 설비 잠금표(노랑·검정 꼬리표 · 자물쇠 · 컴퓨터 승인 초록 불 / 손으로 끈 붉은 불) · 줄 바닥 발자국 · 받는 곳 표시(식판 · 물방울 · 세면대 · 김 오르는 잔).
// 위 층: 줄 번호 딱지 · 기다린 시간 고리(초록 → 주황 → 빨강) · 새치기(붉은 지그재그) · 말다툼(번개) · 양보(둥근 화살 · 하트) · 떨어져 앉기(점선) ·
//   둘이 드는 짐(두 사람 손 사이의 굵은 부품 · 손잡이) · 부르는 손(펼친 손바닥 · 남은 시간 고리) · 혼자 지그(C자 죔쇠) · 예약 대기(모래시계 · 시험대 순서 점) ·
//   구경꾼 무리(옅은 덩어리 · 현장 쪽 시선) · "비켜!" 외침(톱니 말풍선 · 밖으로 미는 화살) · 소문(점선 물결 · 부풀리면 느낌표).
public partial class ShipView
{
    private static readonly Color SpRed = new("#c8463a"), SpRedDark = new("#7c2a23"), SpSteel = new("#9aa5b4"), SpSteelDark = new("#4a5361"), SpInk = new("#15181e"),
        SpTray = new("#6d7684"), SpBolt = new("#d9dee6"), SpBrass = new("#c9a54a"), SpPanel = new("#7f8a99"), SpMotor = new("#3d6f8f"), SpCopper = new("#c47a3a"),
        SpBin = new("#3a64a8"), SpBinHi = new("#6f93cf"), SpTagY = new("#f2c230"), SpOk = new("#59e08a"), SpNo = new("#ff6b5b"), SpWarm = new("#ffd27a"),
        SpQueue = new("#8fd3ff"), SpCut = new("#ff5a4a"), SpYield = new("#ff8fb3"), SpDust = new("#b9ab8c");

    private static float SpH(int a, int b, int c) { uint h = (uint)(a * 73856093 ^ b * 19349663 ^ c * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; }

    /// <summary>바닥 층 (사람 밑): 작업장 · 앞 상자 · 카트 옮겨 싣기 · 잠금표 · 줄 바닥 · 받는 곳.</summary>
    /// <param name="moving">참이면 사람을 따라가는 카트 옮겨 싣기만 (매 프레임 층), 거짓이면 바닥에 놓인 것 (느린 층).</param>
    private void PaintSpaceUnder(CanvasItem ci, bool moving)
    {
        var co = _world.Coop;
        if (moving)
        {
            foreach (var t in co.Transfers) if (t.Until > _world.Tick - SimTime.Minutes(1)) PaintTransfer(ci, t);
            return;
        }
        bool fine = Zoom > 1.15f;
        foreach (var q in co.Queues.All) if (q.Line.Count > 0 || q.Serving >= 0) PaintQueueFloor(ci, q, fine);
        foreach (var s in co.Sites) PaintSite(ci, s, fine);
        foreach (var b in co.Boxes) PaintFrontBox(ci, b, fine);
        foreach (var p in co.Paused) if (!p.Done) PaintLockTag(ci, p, fine);
    }

    /// <summary>위 층 (사람 위): 줄 번호 · 사건 · 짝 · 예약 · 구경꾼 · 외침 · 소문.</summary>
    private void PaintSpaceOver(CanvasItem ci)
    {
        var w = _world;
        var co = w.Coop;
        bool fine = Zoom > 1.15f;
        foreach (var s in co.Crowds.Scenes) if (!s.Ended && s.Watchers.Count > 0) PaintCrowd(ci, s);
        foreach (var call in co.Calls) if (!call.Done) PaintCall(ci, call);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.CarriedBy != null) continue;
            if (co.Queues.Place(c) is { } pl) PaintQueueBadge(ci, c, pl.number, pl.minutes, pl.q);
            if (co.WaitingFor(c) is string why) PaintWaitIcon(ci, c, why, co.WaitFrac(c), fine);
        }
        foreach (var b in co.Benches) if (b.Waiting.Count > 0) PaintBenchOrder(ci, b);
        foreach (var e in co.Queues.Events) if (w.Tick - e.Tick < SimTime.Minutes(1.5f)) PaintQueueEvent(ci, e);
        foreach (var s in co.Crowds.Scenes) if (s.ShoutAt >= 0 && w.Tick - s.ShoutAt < SimTime.Minutes(1.2f)) PaintShout(ci, s);
        foreach (var h in co.Crowds.Heard) if (w.Tick - h.tick < SimTime.Minutes(1)) PaintRumor(ci, h.teller, h.listener, h.big, h.tick);
    }

    private CrewMember? SpCrew(int id) { foreach (var c in _world.Crew) if (c.Id == id) return c; return null; }

    // ───────────────────────────── 작업장 ─────────────────────────────

    private void PaintSite(CanvasItem ci, Worksite s, bool fine)
    {
        bool left = s.State == SiteState.Left;
        float age = left ? Mathf.Clamp((_world.Tick - s.LeftAt) / (float)SimTime.Hours(3), 0f, 1f) : 0f;
        var face = ToPx(s.Face);
        foreach (var t in s.Items)
        {
            var at = CellRect(t.At).GetCenter() + new Vector2(t.Off.X * T, t.Off.Y * T);
            if (fine) // 분해선: 뜯어낸 자리와 이어지는 옅은 점선
            {
                var d = face - at;
                int n = Mathf.Max(2, (int)(d.Length() / 6f));
                for (int k = 1; k < n; k += 2) ci.DrawLine(at + d * (k / (float)n), at + d * ((k + 0.6f) / n), new Color(1f, 1f, 1f, 0.12f), 1f, true);
            }
            switch (t.Kind)
            {
                case SiteItemKind.Toolbox: DrawOpenToolbox(ci, at, t.Angle, fine, left, s.Id); break;
                case SiteItemKind.PartsTray: DrawPartsTray(ci, at, t.Angle, fine, t.Kicked, s.Id); break;
                case SiteItemKind.Panel: DrawCoverPanel(ci, at, t.Angle + 0.5f, fine); break;
                case SiteItemKind.BigPart: DrawMotor(ci, at, t.Angle, fine); break;
            }
            if (left && age > 0.05f) ci.Circle(at, 0.42f * T, new Color(SpDust, 0.12f * age), true, -1f, true); // 오래 비운 자리엔 먼지 빛
            if (t.Bulk > 0 && _world.Matter.InAisle(t.At)) // 통로에 펼친 것: 바닥 가장자리 노랑·검정 띠 (좁아진 길)
            {
                var r = CellRect(t.At);
                for (int k = 0; k < 4; k++) ci.DrawLine(r.Position + new Vector2(k * 8f + 2f, T - 2f), r.Position + new Vector2(k * 8f + 6f, T - 2f), k % 2 == 0 ? SpTagY : SpInk, 2.5f);
            }
        }
        if (left && s.Items.Count > 0) // "작업 중" 꼬리표 (공구함 손잡이에 매달림)
        {
            var tb = s.Items[0];
            var at = CellRect(tb.At).GetCenter() + new Vector2(tb.Off.X * T, tb.Off.Y * T) + new Vector2(8f, -10f);
            float sway = Mathf.Sin(_time * 1.3f + s.Id) * 1.5f;
            ci.DrawLine(at, at + new Vector2(sway, 7f), new Color(0.9f, 0.9f, 0.9f, 0.8f), 1f, true);
            var card = new Rect2(at + new Vector2(sway - 7f, 7f), new Vector2(15f, 9f));
            Gfx.RoundRect(ci, card, SpTagY, 2, SpInk, 1);
            if (fine) Gfx.TextCentered(ci, Fonts.Bold, card.GetCenter(), "작업 중", 5, SpInk);
            else ci.DrawLine(card.Position + new Vector2(3, 4.5f), card.End - new Vector2(3, 4.5f), SpInk, 1f);
        }
        if (s.Touched && left) // 누가 건드렸다: 흩어진 볼트가 굴러다닌다
            foreach (var t in s.Items)
                if (t.Kicked)
                    for (int k = 0; k < 4; k++)
                    {
                        var p = CellRect(t.At).GetCenter() + new Vector2((SpH(s.Id, k, 1) - 0.5f) * T * 0.9f, (SpH(s.Id, k, 2) - 0.5f) * T * 0.9f);
                        ci.Circle(p, 1.4f, SpBolt, true, -1f, true);
                        ci.Circle(p, 0.6f, SpInk, true, -1f, true);
                    }
    }

    private void DrawOpenToolbox(CanvasItem ci, Vector2 at, float angle, bool fine, bool left, int seed)
    {
        float s = T;
        var tr = Transform2D.Identity.Rotated(angle).Translated(at);
        Vector2 P(float x, float y) => tr * new Vector2(x * s, y * s);
        void Quad(float x0, float y0, float x1, float y1, Color c) => ci.Poly(new[] { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1) }, c);
        var red = left ? SpRed.Darkened(0.15f) : SpRed;
        Quad(-0.32f, -0.08f, 0.32f, 0.26f, SpRedDark);           // 아래 몸통
        Quad(-0.36f, -0.3f, -0.04f, -0.1f, red);                // 왼쪽으로 펼친 위 칸
        Quad(0.04f, -0.3f, 0.36f, -0.1f, red);                  // 오른쪽 위 칸
        ci.DrawLine(P(-0.04f, -0.2f), P(0.04f, -0.2f), SpSteel, 1.2f, true); // 펼침 다리
        Quad(-0.3f, -0.06f, 0.3f, 0.2f, SpInk.Lightened(0.08f)); // 안쪽
        // 공구: 스패너 · 드라이버 · 소켓
        ci.DrawLine(P(-0.26f, 0.02f), P(0.02f, 0.14f), SpSteel, 2f, true);
        ci.Arc(P(-0.27f, 0.015f), 0.04f * s, 0.6f, 5.6f, 8, SpSteel, 1.5f, true);
        ci.DrawLine(P(0.06f, 0.0f), P(0.26f, 0.12f), SpBolt, 1.2f, true);
        ci.DrawLine(P(0.06f, 0.0f), P(0.13f, 0.04f), SpBrass, 2.6f, true);
        if (fine)
        {
            for (int k = 0; k < 4; k++) ci.Circle(P(-0.3f + k * 0.08f, -0.2f), 0.025f * s, SpSteel.Lightened(0.2f), true, -1f, true); // 소켓 줄
            for (int k = 0; k < 3; k++) ci.Circle(P(0.12f + k * 0.07f, -0.2f), 0.02f * s, SpBrass, true, -1f, true); // 나사 통
            ci.DrawLine(P(-0.1f, 0.26f), P(0.1f, 0.26f), SpInk, 1.5f, true); // 손잡이 홈
        }
    }

    private void DrawPartsTray(CanvasItem ci, Vector2 at, float angle, bool fine, bool kicked, int seed)
    {
        float s = T;
        var tr = Transform2D.Identity.Rotated(angle * 0.5f).Translated(at);
        Vector2 P(float x, float y) => tr * new Vector2(x * s, y * s);
        ci.Poly(new[] { P(-0.3f, -0.2f), P(0.3f, -0.2f), P(0.3f, 0.2f), P(-0.3f, 0.2f) }, SpTray);
        ci.Polyline(new[] { P(-0.3f, -0.2f), P(0.3f, -0.2f), P(0.3f, 0.2f), P(-0.3f, 0.2f), P(-0.3f, -0.2f) }, SpTray.Lightened(0.3f), 1f, true);
        ci.DrawLine(P(0f, -0.2f), P(0f, 0.2f), SpTray.Darkened(0.3f), 1f, true); // 칸막이
        ci.DrawLine(P(-0.3f, 0f), P(0f, 0f), SpTray.Darkened(0.3f), 1f, true);
        // 칸마다 다른 것: 볼트 · 너트 · 개스킷 고리 · 스프링
        for (int k = 0; k < (kicked ? 2 : 5); k++) ci.Circle(P(-0.25f + k * 0.045f, -0.1f), 0.022f * s, SpBolt, true, -1f, true);
        for (int k = 0; k < 3; k++) ci.Arc(P(-0.22f + k * 0.08f, 0.1f), 0.025f * s, 0f, Mathf.Tau, 6, SpBrass, 1.2f, true);
        ci.Arc(P(0.15f, -0.08f), 0.08f * s, 0f, Mathf.Tau, 16, new Color("#2b2f36"), 2f, true); // 개스킷
        if (fine)
        {
            var spring = new Vector2[8];
            for (int k = 0; k < 8; k++) spring[k] = P(0.06f + k * 0.03f, 0.12f + (k % 2 == 0 ? -0.03f : 0.03f));
            ci.Polyline(spring, SpSteel, 1f, true);
        }
    }

    private void DrawCoverPanel(CanvasItem ci, Vector2 at, float angle, bool fine)
    {
        float s = T;
        var tr = Transform2D.Identity.Rotated(angle).Translated(at);
        Vector2 P(float x, float y) => tr * new Vector2(x * s, y * s);
        ci.Poly(new[] { P(-0.34f, -0.22f), P(0.34f, -0.22f), P(0.34f, 0.22f), P(-0.34f, 0.22f) }, new Color(0, 0, 0, 0.25f)); // 그림자
        ci.Poly(new[] { P(-0.36f, -0.25f), P(0.32f, -0.25f), P(0.32f, 0.19f), P(-0.36f, 0.19f) }, SpPanel);
        for (int k = 0; k < 5; k++) ci.DrawLine(P(-0.24f, -0.15f + k * 0.07f), P(0.2f, -0.15f + k * 0.07f), SpPanel.Darkened(0.35f), 1.2f, true); // 통풍 틈
        foreach (var (x, y) in new[] { (-0.32f, -0.21f), (0.28f, -0.21f), (-0.32f, 0.15f), (0.28f, 0.15f) })
        {
            ci.Circle(P(x, y), 0.025f * s, SpInk, true, -1f, true); // 나사 구멍 (나사는 쟁반에)
            if (fine) ci.Arc(P(x, y), 0.035f * s, 0f, Mathf.Tau, 8, SpPanel.Lightened(0.3f), 0.8f, true);
        }
        if (fine) ci.Poly(new[] { P(0f, 0.05f), P(0.05f, 0.14f), P(-0.05f, 0.14f) }, SpTagY); // 경고 딱지
    }

    private void DrawMotor(CanvasItem ci, Vector2 at, float angle, bool fine)
    {
        float r = 0.24f * T;
        ci.Circle(at + new Vector2(2, 2), r, new Color(0, 0, 0, 0.3f), true, -1f, true);
        for (int k = 0; k < 12; k++) // 냉각 핀
        {
            float a = angle + k * Mathf.Tau / 12f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            ci.DrawLine(at + d * r * 0.8f, at + d * r * 1.12f, SpMotor.Lightened(0.25f), 2f, true);
        }
        ci.Circle(at, r * 0.85f, SpMotor, true, -1f, true);
        ci.Circle(at, r * 0.35f, SpSteel, true, -1f, true); // 축
        ci.Circle(at, r * 0.12f, SpInk, true, -1f, true);
        var box = at + new Vector2(Mathf.Cos(angle + 1.2f), Mathf.Sin(angle + 1.2f)) * r * 0.75f;
        ci.Box(new Rect2(box - new Vector2(3, 2.5f), new Vector2(6, 5)), SpSteelDark); // 단자 상자
        if (fine) for (int k = 0; k < 3; k++) ci.DrawLine(box + new Vector2(-2 + k * 2, 2.5f), box + new Vector2(-2 + k * 2 + Mathf.Sin(_time + k) * 0.5f, 6f), k == 1 ? SpCopper : SpInk, 1f, true); // 선
    }

    private void PaintFrontBox(CanvasItem ci, DugBox b, bool fine)
    {
        var t = b.Item;
        var at = CellRect(t.At).GetCenter() + new Vector2(t.Off.X * T, t.Off.Y * T);
        var tr = Transform2D.Identity.Rotated(t.Angle).Translated(at);
        Vector2 P(float x, float y) => tr * new Vector2(x * T, y * T);
        ci.Poly(new[] { P(-0.3f, -0.24f), P(0.3f, -0.24f), P(0.34f, 0.26f), P(-0.34f, 0.26f) }, SpBin); // 아래가 넓은 통
        ci.Poly(new[] { P(-0.3f, -0.24f), P(0.3f, -0.24f), P(0.3f, -0.16f), P(-0.3f, -0.16f) }, SpBinHi); // 테두리
        ci.Poly(new[] { P(-0.08f, -0.22f), P(0.08f, -0.22f), P(0.08f, -0.18f), P(-0.08f, -0.18f) }, SpInk); // 손잡이 구멍
        ci.Box(new Rect2(P(-0.18f, -0.04f), new Vector2(0.36f * T, 0.14f * T)), new Color(0.95f, 0.95f, 0.9f, 0.9f)); // 이름표
        if (fine) Gfx.TextCentered(ci, Fonts.Bold, P(0f, 0.03f), "부품", 6, SpInk);
        if (b.Left && _world.Matter.InAisle(t.At)) // 통로에 둔 채: 바닥 가장자리 노랑·검정
        {
            var r = CellRect(t.At);
            for (int k = 0; k < 4; k++) ci.DrawLine(r.Position + new Vector2(k * 8f + 2f, T - 2f), r.Position + new Vector2(k * 8f + 6f, T - 2f), k % 2 == 0 ? SpTagY : SpInk, 2.5f);
        }
        if (!b.Left) // 꺼내는 중: 선반 쪽으로 미는 화살이 깜빡
        {
            float a = 0.4f + 0.4f * Mathf.Sin(_time * 5f);
            ci.DrawLine(P(0f, -0.34f), P(0f, -0.5f), new Color(SpWarm, a), 1.5f, true);
            ci.Poly(new[] { P(-0.06f, -0.46f), P(0.06f, -0.46f), P(0f, -0.56f) }, new Color(SpWarm, a));
        }
    }

    private void PaintTransfer(CanvasItem ci, CartTransfer t)
    {
        var door = CellRect(t.At).GetCenter();
        var c = SpCrew(t.Crew);
        if (c == null) return;
        var from = CrewPx(c);
        var dir = (door - from).LengthSquared() > 1f ? (door - from).Normalized() : Vector2.Right;
        float ph = Mathf.PosMod(_time * 0.8f, 1f);
        // 내린 짐 상자 둘 · 문턱 너머로 옮기는 상자 하나 (넘어가는 중)
        for (int k = 0; k < 2; k++)
        {
            var p = from + dir.Orthogonal() * (k == 0 ? 9f : -9f) + dir * 4f;
            ci.Box(new Rect2(p - new Vector2(4, 4), new Vector2(8, 8)), new Color("#b58a52"));
            ci.DrawLine(p - new Vector2(4, 0), p + new Vector2(4, 0), new Color("#e6d2a0"), 1f);
        }
        var moving = from.Lerp(door + dir * T * 0.7f, ph);
        ci.Box(new Rect2(moving - new Vector2(4, 4) + new Vector2(0, -Mathf.Sin(ph * Mathf.Pi) * 6f), new Vector2(8, 8)), new Color("#c99a5e"));
        ci.DrawLine(door - dir.Orthogonal() * 10f, door + dir.Orthogonal() * 10f, SpTagY, 2f); // 문턱
        ci.Arc(door, 6f, dir.Angle() - 2.4f, dir.Angle() - 0.7f, 8, new Color(SpWarm, 0.8f), 1.5f, true);
    }

    private void PaintLockTag(CanvasItem ci, PausedFixture p, bool fine)
    {
        Furniture? f = null;
        foreach (var x in _world.Ship.Furniture) if (x.Id == p.FurnitureId) { f = x; break; }
        if (f == null) return;
        var r = FurnitureRect(f);
        var at = r.Position + new Vector2(r.Size.X - 6f, 4f);
        float sway = Mathf.Sin(_time * 1.6f + f.Id) * 1.2f;
        ci.DrawLine(at, at + new Vector2(sway, 6f), SpSteel, 1f, true);
        var tag = new Rect2(at + new Vector2(sway - 5f, 6f), new Vector2(10f, 13f));
        ci.Box(tag, SpTagY);
        for (int k = 0; k < 3; k++) ci.DrawLine(tag.Position + new Vector2(0, 3 + k * 4), tag.Position + new Vector2(10, 1 + k * 4), SpInk, 1.5f); // 노랑·검정 사선
        var lockC = tag.Position + new Vector2(5f, 10f);
        ci.Box(new Rect2(lockC - new Vector2(3, 1), new Vector2(6, 5)), SpInk);
        ci.Arc(lockC - new Vector2(0, 1), 2f, Mathf.Pi, Mathf.Tau, 6, SpInk, 1.2f, true); // 자물쇠 고리
        // 승인한 쪽: 컴퓨터(초록 · 깜빡) · 손으로 끔(붉음) · 잊고 둠(붉게 빠르게)
        var lamp = p.Approved ? SpOk : SpNo;
        float blink = p.Forgotten ? 0.5f + 0.5f * Mathf.Sin(_time * 8f) : p.Approved ? 0.7f + 0.3f * Mathf.Sin(_time * 2f) : 0.9f;
        ci.Circle(tag.Position + new Vector2(10f, 0f), 1.8f, new Color(lamp, blink), true, -1f, true);
        if (fine && p.Approved) // 승인 시간이 줄어드는 띠
        {
            float left = Mathf.Clamp((p.Until - _world.Tick) / (float)Mathf.Max(1, p.Until - p.Since), 0f, 1f);
            ci.DrawLine(tag.Position + new Vector2(0, 14f), tag.Position + new Vector2(10f * left, 14f), SpOk, 1.5f);
        }
    }

    // ───────────────────────────── 줄 ─────────────────────────────

    private void PaintQueueFloor(CanvasItem ci, ServiceQueue q, bool fine)
    {
        var spot = CellRect(q.Spot).GetCenter();
        // 받는 곳 표시 (종류마다 다른 그림)
        var mark = spot + (ToPx(q.Toward) - spot).Normalized() * 9f;
        switch (q.Kind)
        {
            case QueueKind.Meal: // 칸 나뉜 식판
                ci.Box(new Rect2(mark - new Vector2(6, 4), new Vector2(12, 8)), new Color("#c9d3df"));
                ci.DrawLine(mark + new Vector2(-1, -4), mark + new Vector2(-1, 4), new Color("#7d8794"), 1f);
                ci.DrawLine(mark + new Vector2(-1, 0), mark + new Vector2(6, 0), new Color("#7d8794"), 1f);
                break;
            case QueueKind.Shower: // 떨어지는 물방울 셋
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.PosMod(_time * 1.5f + k * 0.33f, 1f);
                    var d = mark + new Vector2(-4 + k * 4, -6 + ph * 10f);
                    ci.Poly(new[] { d + new Vector2(0, -2.5f), d + new Vector2(1.6f, 0.8f), d + new Vector2(-1.6f, 0.8f) }, new Color(SpQueue, 1f - ph));
                    ci.Circle(d + new Vector2(0, 0.8f), 1.6f, new Color(SpQueue, 1f - ph), true, -1f, true);
                }
                break;
            case QueueKind.Toilet: // 세면대 (둥근 대야 · 수도꼭지)
                ci.Arc(mark, 5f, 0f, Mathf.Pi, 10, new Color("#e9eef3"), 2.5f, true);
                ci.DrawLine(mark + new Vector2(0, -5), mark + new Vector2(0, -1), SpSteel, 1.5f);
                break;
            default: // 김 오르는 잔
                ci.Box(new Rect2(mark - new Vector2(3.5f, 2), new Vector2(7, 6)), new Color("#f1e8dc"));
                ci.Arc(mark + new Vector2(4.5f, 1), 2f, -1.5f, 1.5f, 6, new Color("#f1e8dc"), 1.2f, true);
                for (int k = 0; k < 2; k++) ci.DrawLine(mark + new Vector2(-1.5f + k * 3, -3), mark + new Vector2(-1.5f + k * 3 + Mathf.Sin(_time * 3 + k) * 1.5f, -8), new Color(1, 1, 1, 0.4f), 1f, true);
                break;
        }
        // 줄 선 칸: 바닥 발자국 한 쌍 · 앞으로 잇는 옅은 선
        var prev = spot;
        for (int i = 0; i < q.Line.Count && i < q.Slots.Count; i++)
        {
            var c = CellRect(q.Slots[i]).GetCenter();
            ci.DrawLine(prev, c, new Color(SpQueue, 0.18f), 1.5f, true);
            var dir = (prev - c).LengthSquared() > 1f ? (prev - c).Normalized() : Vector2.Up;
            var side = dir.Orthogonal() * 3f;
            foreach (var sgn in new[] { 1f, -1f })
            {
                var fp = c + side * sgn + dir * (sgn > 0 ? 2f : -1f);
                ci.DrawSetTransform(fp, dir.Angle() + Mathf.Pi / 2f, new Vector2(0.55f, 1f));
                ci.Circle(Vector2.Zero, 3f, new Color(SpQueue, 0.25f), true, -1f, true);
                ci.DrawSetTransformMatrix(Transform2D.Identity);
            }
            prev = c;
        }
    }

    private void PaintQueueBadge(CanvasItem ci, CrewMember c, int number, float minutes, ServiceQueue q)
    {
        var p = CrewPx(c) + new Vector2(-CrewRadius - 6f, -CrewRadius - 4f);
        if (number == 0) // 받는 중: 받는 곳 쪽 작은 화살
        {
            var to = (ToPx(q.Toward) - CrewPx(c)).Normalized();
            ci.Poly(new[] { CrewPx(c) + to * (CrewRadius + 7f), CrewPx(c) + to * (CrewRadius + 2f) + to.Orthogonal() * 3f, CrewPx(c) + to * (CrewRadius + 2f) - to.Orthogonal() * 3f }, new Color(SpQueue, 0.8f));
            return;
        }
        float k = Mathf.Clamp(minutes / 20f, 0f, 1f);
        var ring = k < 0.5f ? SpOk.Lerp(SpWarm, k * 2f) : SpWarm.Lerp(SpNo, (k - 0.5f) * 2f);
        ci.Circle(p, 6f, new Color(0.08f, 0.1f, 0.13f, 0.9f), true, -1f, true);
        ci.Arc(p, 7.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Max(0.05f, k), 20, ring, 2f, true); // 기다린 시간
        Gfx.TextCentered(ci, Fonts.Bold, p, number.ToString(), 8, Colors.White);
    }

    private void PaintQueueEvent(CanvasItem ci, QueueEvent e)
    {
        var a = SpCrew(e.A);
        var b = e.B >= 0 ? SpCrew(e.B) : null;
        if (a == null) return;
        var pa = CrewPx(a) + new Vector2(0, -CrewRadius - 2f);
        float fade = 1f - Mathf.Clamp((_world.Tick - e.Tick) / (float)SimTime.Minutes(1.5f), 0f, 1f);
        switch (e.Kind)
        {
            case QueueEventKind.Cut when b != null: // 끼어든 길: 붉은 지그재그 화살
            {
                var pb = CrewPx(b);
                var d = pb - pa;
                var pts = new Vector2[7];
                for (int k = 0; k < 7; k++) pts[k] = pa + d * (k / 6f) + d.Orthogonal().Normalized() * (k % 2 == 0 ? 0f : 4f) * (k is 0 or 6 ? 0f : 1f);
                ci.Polyline(pts, new Color(SpCut, fade), 2f, true);
                break;
            }
            case QueueEventKind.Quarrel when b != null: // 말다툼: 두 머리 사이 번개 · 김
            {
                var pb = CrewPx(b) + new Vector2(0, -CrewRadius - 2f);
                var mid = (pa + pb) * 0.5f + new Vector2(0, -10f);
                float j = Mathf.Sin(_time * 30f) * 2f;
                ci.Polyline(new[] { pa, mid + new Vector2(-4 + j, -3), mid + new Vector2(3, 2), pb }, new Color(SpTagY, fade), 2.5f, true);
                for (int k = 0; k < 3; k++) ci.Arc(pa + new Vector2(-6 + k * 6, -8 - Mathf.PosMod(_time * 8f + k, 6f)), 2f, 0f, Mathf.Tau, 6, new Color(1, 1, 1, 0.35f * fade), 1f, true);
                break;
            }
            case QueueEventKind.Yield or QueueEventKind.Offer when b != null: // 양보: 둥근 화살 · 작은 하트
            {
                var pb = CrewPx(b) + new Vector2(0, -CrewRadius - 2f);
                var mid = (pa + pb) * 0.5f + new Vector2(0, -12f);
                ci.Polyline(new[] { pa, mid, pb }, new Color(SpYield, fade * 0.8f), 1.5f, true);
                DrawHeart(ci, mid + new Vector2(0, -4f + Mathf.Sin(_time * 4f)), 4f, new Color(SpYield, fade));
                break;
            }
            case QueueEventKind.BackOff: // 뒤로 물러남: 작은 되돌이 화살
                ci.Arc(pa + new Vector2(0, -6), 5f, 0.3f, Mathf.Pi + 0.6f, 10, new Color(SpWarm, fade), 1.5f, true);
                break;
            case QueueEventKind.SeatAway when b != null: // 떨어져 앉음: 두 사람 사이 끊긴 점선
            {
                var pb = CrewPx(b);
                var d = pb - CrewPx(a);
                for (int k = 0; k < 8; k += 2) ci.DrawLine(CrewPx(a) + d * (k / 8f), CrewPx(a) + d * ((k + 1) / 8f), new Color(SpCut, 0.5f * fade), 1.5f, true);
                break;
            }
            case QueueEventKind.SeatNear when b != null:
                DrawHeart(ci, (CrewPx(a) + CrewPx(b)) * 0.5f + new Vector2(0, -16f), 3.5f, new Color(SpYield, fade));
                break;
            case QueueEventKind.Defer or QueueEventKind.GiveUp: // 나중에: 작은 시계
                ci.Arc(pa + new Vector2(8, -6), 4f, 0f, Mathf.Tau, 12, new Color(SpQueue, fade), 1.2f, true);
                ci.DrawLine(pa + new Vector2(8, -6), pa + new Vector2(8, -9), new Color(SpQueue, fade), 1f, true);
                ci.DrawLine(pa + new Vector2(8, -6), pa + new Vector2(10, -6), new Color(SpQueue, fade), 1f, true);
                break;
        }
    }

    private static void DrawHeart(CanvasItem ci, Vector2 c, float r, Color col)
    {
        ci.Circle(c + new Vector2(-r * 0.5f, 0), r * 0.55f, col, true, -1f, true);
        ci.Circle(c + new Vector2(r * 0.5f, 0), r * 0.55f, col, true, -1f, true);
        ci.Poly(new[] { c + new Vector2(-r, r * 0.15f), c + new Vector2(r, r * 0.15f), c + new Vector2(0, r * 1.2f) }, col);
    }

    // ───────────────────────────── 둘이 하는 일 · 예약 · 기다림 ─────────────────────────────

    private void PaintCall(CanvasItem ci, PairCall call)
    {
        var caller = SpCrew(call.Caller);
        if (caller == null) return;
        var pc = CrewPx(caller);
        var helper = call.Helper >= 0 ? SpCrew(call.Helper) : null;
        if (call.Arrived && helper != null) // 둘이 드는 짐: 두 사람 손 사이의 굵은 부품
        {
            var ph = CrewPx(helper);
            var mid = (pc + ph) * 0.5f;
            float bob = Mathf.Sin(_time * 3f) * 0.8f;
            ci.DrawLine(pc, ph, new Color(0, 0, 0, 0.3f), 9f, true);
            ci.DrawLine(pc + new Vector2(0, bob), ph + new Vector2(0, bob), SpMotor, 7f, true);
            ci.DrawLine(pc + new Vector2(0, bob - 2), ph + new Vector2(0, bob - 2), SpMotor.Lightened(0.35f), 1.5f, true);
            foreach (var g in new[] { pc, ph }) ci.Circle(g + new Vector2(0, bob), 3.5f, SpSteelDark, true, -1f, true); // 손잡이
            for (int k = -2; k <= 2; k++) ci.DrawLine(mid + (ph - pc).Normalized() * k * 3f + new Vector2(0, bob - 3), mid + (ph - pc).Normalized() * k * 3f + new Vector2(0, bob + 3), SpMotor.Darkened(0.3f), 1f, true); // 핀
            return;
        }
        // 부르는 손: 펼친 손바닥 + 남은 시간 고리
        var hand = pc + new Vector2(CrewRadius + 5f, -CrewRadius - 6f) + new Vector2(0, Mathf.Sin(_time * 6f) * 1.5f);
        ci.Circle(hand, 3.2f, new Color("#f0c9a0"), true, -1f, true);
        for (int k = 0; k < 4; k++) ci.DrawLine(hand + new Vector2(-2.4f + k * 1.6f, -2f), hand + new Vector2(-2.6f + k * 1.8f, -6f), new Color("#f0c9a0"), 1.3f, true);
        ci.DrawLine(hand + new Vector2(3f, 0), hand + new Vector2(5.5f, -2.5f), new Color("#f0c9a0"), 1.3f, true);
        float k01 = Mathf.Clamp((_world.Tick - call.Opened) / (float)Mathf.Max(1, call.Cap - call.Opened), 0f, 1f);
        ci.Arc(hand, 8f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * (1f - k01), 20, SpWarm.Lerp(SpNo, k01), 1.5f, true);
        if (helper != null) // 오는 중: 점선
        {
            var d = CrewPx(helper) - pc;
            for (int s = 0; s < 10; s += 2) ci.DrawLine(pc + d * (s / 10f), pc + d * ((s + 1) / 10f), new Color(SpWarm, 0.5f), 1.2f, true);
        }
    }

    private void PaintWaitIcon(CanvasItem ci, CrewMember c, string why, float frac, bool fine)
    {
        var p = CrewPx(c) + new Vector2(CrewRadius + 4f, -CrewRadius - 2f);
        switch (why)
        {
            case "예약 대기": // 모래시계 (모래가 줄어든다)
            {
                var col = new Color("#e7d8b5");
                ci.Poly(new[] { p + new Vector2(-4, -6), p + new Vector2(4, -6), p, }, new Color(col, 0.35f));
                ci.Poly(new[] { p, p + new Vector2(4, 6), p + new Vector2(-4, 6) }, new Color(col, 0.35f));
                float sand = 1f - frac;
                ci.Poly(new[] { p + new Vector2(-4 * sand, -6 * sand), p + new Vector2(4 * sand, -6 * sand), p }, col);
                ci.Poly(new[] { p + new Vector2(-4 * frac, 6 - 6 * frac), p + new Vector2(4 * frac, 6 - 6 * frac), p + new Vector2(4, 6), p + new Vector2(-4, 6) }, col);
                ci.DrawLine(p + new Vector2(-5, -6), p + new Vector2(5, -6), SpSteelDark, 1.5f);
                ci.DrawLine(p + new Vector2(-5, 6), p + new Vector2(5, 6), SpSteelDark, 1.5f);
                break;
            }
            case "옆 설비 멈춤": // 자물쇠 · 돌아가는 손
                ci.Box(new Rect2(p + new Vector2(-3.5f, -1), new Vector2(7, 6)), SpTagY);
                ci.Arc(p + new Vector2(0, -1), 2.6f, Mathf.Pi, Mathf.Tau, 8, SpTagY, 1.4f, true);
                ci.Arc(p, 8f, _time * 3f, _time * 3f + Mathf.Tau * frac, 12, new Color(SpTagY, 0.7f), 1.2f, true);
                break;
            case "공구 펼침": // 펼쳐지는 스패너
                ci.DrawLine(p + new Vector2(-4, 4), p + new Vector2(3, -3), SpSteel, 2f, true);
                ci.Arc(p + new Vector2(4, -4), 2.5f, 0.7f + _time, 5.5f + _time, 8, SpSteel, 1.5f, true);
                break;
            case "다시 맞춰 봄": // 물음표 대신 흩어진 볼트 셋이 맴돈다
                for (int k = 0; k < 3; k++)
                {
                    float a = _time * 2f + k * Mathf.Tau / 3f;
                    ci.Circle(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4f, 1.5f, SpBolt, true, -1f, true);
                }
                break;
            case "짝 기다림": break; // 부르는 손이 따로 그려진다
            case "혼자 (지그)": // C자 죔쇠
                ci.Arc(p, 4.5f, 0.6f, Mathf.Tau - 0.6f, 12, SpRed, 2f, true);
                ci.DrawLine(p + new Vector2(3.6f, -2.6f), p + new Vector2(3.6f, 2.6f), SpSteel, 1.5f, true);
                ci.DrawLine(p + new Vector2(3.6f, 2.6f), p + new Vector2(6f, 2.6f), SpSteel, 1.5f, true);
                break;
            case "조심조심": // 주의 삼각형이 숨 쉰다
            {
                float a = 0.6f + 0.4f * Mathf.Sin(_time * 4f);
                ci.Poly(new[] { p + new Vector2(0, -5), p + new Vector2(5, 4), p + new Vector2(-5, 4) }, new Color(SpTagY, a));
                ci.DrawLine(p + new Vector2(0, -2), p + new Vector2(0, 1.5f), SpInk, 1.2f);
                ci.Circle(p + new Vector2(0, 3), 0.7f, SpInk, true, -1f, true);
                break;
            }
        }
    }

    private void PaintBenchOrder(CanvasItem ci, BenchBook b)
    {
        Furniture? f = null;
        foreach (var x in _world.Ship.Furniture) if (x.Id == b.FurnitureId) { f = x; break; }
        if (f == null) return;
        var r = FurnitureRect(f);
        var at = r.Position + new Vector2(4f, r.Size.Y + 5f);
        if (b.User >= 0) { ci.Circle(at, 3.5f, Palette.Crew(b.User), true, -1f, true); ci.Arc(at, 4.8f, 0f, Mathf.Tau, 12, Colors.White, 1f, true); } // 쓰는 사람
        for (int i = 0; i < b.Waiting.Count && i < 5; i++) // 기다리는 순서 점
        {
            var p = at + new Vector2(9f + i * 7f, 0);
            ci.Circle(p, 2.6f, Palette.Crew(b.Waiting[i]).WithAlpha(0.85f), true, -1f, true);
            if (i == 0) ci.DrawLine(p + new Vector2(-5, 0), p + new Vector2(-3.4f, 0), Colors.White, 1f);
        }
        if (b.Advised >= 0 && _world.Tick - b.Advised < SimTime.Minutes(30)) // 컴퓨터가 정해 준 순서: 순서 점 밑 가는 푸른 줄
            ci.DrawLine(at + new Vector2(6, 4.5f), at + new Vector2(9f + Mathf.Min(5, b.Waiting.Count) * 7f, 4.5f), new Color("#59d0ff"), 1.2f);
    }

    // ───────────────────────────── 구경꾼 ─────────────────────────────

    private void PaintCrowd(CanvasItem ci, CrowdScene s)
    {
        var center = ToPx(s.Center);
        var pts = new List<Vector2>();
        foreach (var id in s.Watchers) if (SpCrew(id) is CrewMember c) pts.Add(CrewPx(c));
        if (pts.Count == 0) return;
        // 옅은 무리 덩어리 (사람마다 겹치는 원)
        foreach (var p in pts) ci.Circle(p, CrewRadius + 7f, new Color(1f, 0.85f, 0.55f, 0.07f), true, -1f, true);
        foreach (var p in pts)
        {
            // 현장 쪽 시선: 머리에서 현장으로 짧은 두 줄 (눈길)
            var d = (center - p).Normalized();
            var eye = p + d * (CrewRadius + 2f);
            ci.DrawLine(eye + d.Orthogonal() * 2f, eye + d.Orthogonal() * 2f + d * 5f, new Color(1, 1, 1, 0.45f), 1f, true);
            ci.DrawLine(eye - d.Orthogonal() * 2f, eye - d.Orthogonal() * 2f + d * 5f, new Color(1, 1, 1, 0.45f), 1f, true);
        }
        if (pts.Count >= 2) // 무리 테두리
        {
            var mid = Vector2.Zero; foreach (var p in pts) mid += p; mid /= pts.Count;
            float rad = 0f; foreach (var p in pts) rad = Mathf.Max(rad, (p - mid).Length());
            ci.Arc(mid, rad + CrewRadius + 9f, 0f, Mathf.Tau, 28, new Color(1f, 0.8f, 0.45f, 0.22f + 0.08f * Mathf.Sin(_time * 2f)), 1.5f, true);
        }
    }

    private void PaintShout(CanvasItem ci, CrowdScene s)
    {
        var chief = SpCrew(s.Shouter);
        if (chief == null) return;
        float age = (_world.Tick - s.ShoutAt) / (float)SimTime.Minutes(1.2f);
        float a = 1f - Mathf.Clamp(age, 0f, 1f);
        var p = CrewPx(chief) + new Vector2(0, -CrewRadius - 22f);
        string text = s.ShoutLine.Length > 0 ? s.ShoutLine : "비켜!";
        float bw = Gfx.Width(Fonts.Bold, text, 11) + 16f;
        // 톱니 말풍선 (외침)
        var poly = new List<Vector2>();
        int n = 18;
        for (int k = 0; k < n; k++)
        {
            float ang = k * Mathf.Tau / n;
            float rr = k % 2 == 0 ? 1f : 0.82f;
            poly.Add(p + new Vector2(Mathf.Cos(ang) * bw * 0.58f * rr, Mathf.Sin(ang) * 14f * rr));
        }
        ci.Poly(poly.ToArray(), new Color(1f, 0.97f, 0.9f, 0.95f * a));
        poly.Add(poly[0]);
        ci.Polyline(poly.ToArray(), new Color(SpNo, a), 2f, true);
        ci.Poly(new[] { p + new Vector2(-4, 11), p + new Vector2(4, 11), CrewPx(chief) + new Vector2(0, -CrewRadius) }, new Color(1f, 0.97f, 0.9f, 0.95f * a));
        Gfx.TextCentered(ci, Fonts.Bold, p, text, 11, new Color(0.65f, 0.08f, 0.06f, a));
        // 구경꾼 자리에서 밖으로 미는 화살
        var door = CellRect(s.Spots[0]).GetCenter();
        for (int i = 0; i < s.Spots.Count; i++)
        {
            if (s.Taken[i] < 0) continue;
            var c = CellRect(s.Spots[i]).GetCenter();
            var d = (c - door).LengthSquared() > 1f ? (c - door).Normalized() : (c - ToPx(s.Center)).Normalized();
            float push = Mathf.PosMod(_time * 2f, 1f) * 8f;
            var tip = c + d * (10f + push);
            ci.DrawLine(c + d * (2f + push), tip, new Color(SpNo, 0.7f * a), 1.5f, true);
            ci.Poly(new[] { tip + d * 4f, tip + d.Orthogonal() * 3f, tip - d.Orthogonal() * 3f }, new Color(SpNo, 0.7f * a));
        }
    }

    private void PaintRumor(CanvasItem ci, int teller, int listener, bool big, long tick)
    {
        var a = SpCrew(teller);
        var b = SpCrew(listener);
        if (a == null || b == null) return;
        var pa = CrewPx(a) + new Vector2(0, -CrewRadius - 3f);
        var pb = CrewPx(b) + new Vector2(0, -CrewRadius - 3f);
        float fade = 1f - Mathf.Clamp((_world.Tick - tick) / (float)SimTime.Minutes(1), 0f, 1f);
        var d = pb - pa;
        var nrm = d.Orthogonal().Normalized();
        for (int k = 0; k < 12; k++) // 점선 물결
        {
            if (k % 2 == 1) continue;
            float t0 = k / 12f, t1 = (k + 1) / 12f;
            var q0 = pa + d * t0 + nrm * Mathf.Sin(t0 * Mathf.Tau * 2f + _time * 6f) * 4f;
            var q1 = pa + d * t1 + nrm * Mathf.Sin(t1 * Mathf.Tau * 2f + _time * 6f) * 4f;
            ci.DrawLine(q0, q1, new Color(0.85f, 0.75f, 1f, 0.6f * fade), 1.3f, true);
        }
        var mid = pa + d * 0.5f + new Vector2(0, -8f);
        ci.Circle(mid, big ? 6f : 4.5f, new Color(0.95f, 0.92f, 1f, 0.85f * fade), true, -1f, true);
        if (big)
        {
            ci.DrawLine(mid + new Vector2(-1.5f, -3.5f), mid + new Vector2(-1.5f, 1f), new Color(SpNo, fade), 1.4f);
            ci.DrawLine(mid + new Vector2(1.5f, -3.5f), mid + new Vector2(1.5f, 1f), new Color(SpNo, fade), 1.4f);
            ci.Circle(mid + new Vector2(-1.5f, 3f), 0.8f, new Color(SpNo, fade), true, -1f, true);
            ci.Circle(mid + new Vector2(1.5f, 3f), 0.8f, new Color(SpNo, fade), true, -1f, true);
        }
        else for (int k = -1; k <= 1; k++) ci.Circle(mid + new Vector2(k * 2.2f, 0), 0.8f, new Color(0.3f, 0.25f, 0.45f, fade), true, -1f, true);
    }
}
