using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.1 진행 중인 일상 장면 (읽기만): 탁자 위 체스판과 말 · 놓인 커피 잔(김) / 찻잔(받침 · 티백 꼬리표) · 바닥의 국 자국(번들거림 · 건더기 · 닦인 자국) ·
// 영화 스크린(깜빡이는 화면 · 영사기 빛 · 진척 줄 · 바닥 방석) · 몽유병자(내민 팔 · 떠오르는 z) · 만들다 만 소품(진척만큼 채워진 뼈대 · 톱밥) ·
// 냉장고 쪽지(노란 메모 · 테이프) · 당번표 낙서(줄 친 표 · 빨간 낙서) · 생일 카드(접힌 분홍 카드 · 서명 줄) · 인수인계 메모(클립 · 느낌표).
// 체스 말은 종류마다 실루엣이 다르다(폰 · 룩 · 나이트 · 비숍 · 퀸 · 킹) · 잡힌 말은 판 옆에 줄 선다 · 국 자국은 마르면 갈라진 딱지가 되고, 닦는 동안엔 노란 "미끄럼 주의" 표지 ·
// 컴퓨터가 한 일도 보인다: 몽유병자 둘레의 복도 감지 고리 · 부른 당직까지 점선 / 볼륨을 낮춘 스피커의 칩 표시 / 보관함 영화 제목 · 보관함이 꺼지면 도는 고리 / 알림이 간 메모의 단말 표시.
// 이름표 접시를 먹는 사람 손엔 이름표 붙은 접시 · 캄캄해서 멈춘 작업대엔 꺼진 전구.
// 멀리서는 실루엣만, 가까이에서는 말 · 서명 · 글줄까지. 시뮬레이션은 바꾸지 않는다.
public partial class ShipView
{
    private static readonly Color Wood = new(0.78f, 0.62f, 0.42f), WoodDark = new(0.36f, 0.24f, 0.15f);

    private void PaintScenes(CanvasItem ci)
    {
        var w = _world;
        var sc = w.Scenes;
        bool close = Zoom > 0.9f;
        foreach (var s in sc.Scenes)
        {
            if (!s.Open && s.Things.Count == 0) continue;
            foreach (var t in s.Things)
            {
                switch (t.Kind)
                {
                    case ThingKind.Board: PaintChessBoard(ci, s, t, close); break;
                    case ThingKind.Cup: PaintCup(ci, t, close); break;
                    case ThingKind.Stain: PaintStain(ci, s, t, close); break;
                    case ThingKind.Screen: PaintScreen(ci, s, t, close); break;
                    case ThingKind.Work: PaintWip(ci, s, t, close); break;
                }
            }
            if (!s.Open) continue;
            switch (s.Kind)
            {
                case SceneKind.Coffee when s.Holding && sc.CrewOf(s.Host) is CrewMember m:
                    PaintTray(ci, CrewPx(m) + new Vector2(CrewRadius * 0.9f, -CrewRadius * 0.2f), s.Targets.Count - s.Delivered, s.Tea);
                    break;
                case SceneKind.Snack when s.Holding && sc.CrewOf(s.Host) is CrewMember e:
                    if (s.Plate >= 0) PaintTaggedPlate(ci, CrewPx(e) + new Vector2(CrewRadius * 0.95f, CrewRadius * 0.1f), s.Victim, close);
                    else PaintPudding(ci, CrewPx(e) + new Vector2(CrewRadius * 0.95f, CrewRadius * 0.1f));
                    break;
                case SceneKind.Sleepwalk when sc.CrewOf(s.Host) is CrewMember z && z.Job?.Activity is SceneActivity:
                    PaintSleepwalker(ci, z, s.Holding ? sc.CrewOf(s.Other) : null);
                    if (s.ComputerAct >= 0) PaintSensorPing(ci, z, s.Holding ? null : sc.CrewOf(s.Other));
                    break;
                case SceneKind.Craft when s.Holding && sc.CrewOf(s.Host) is CrewMember mk:
                    PaintCarriedProp(ci, CrewPx(mk) + new Vector2(0f, -CrewRadius * 1.25f), s);
                    break;
                case SceneKind.Spill when s.Holding && sc.CrewOf(s.Other) is CrewMember cl:
                    PaintMop(ci, CrewPx(cl), cl.Pose == Pose.Working);
                    if (s.Here.Contains(cl.Id)) PaintWetSign(ci, CellRect(s.Spot).GetCenter() + new Vector2(T * 0.55f, -T * 0.35f), close);
                    break;
                case SceneKind.Craft when !s.Holding && s.PauseWhy == "어두움":
                    PaintDeadBulb(ci, CellRect(s.Spot2).GetCenter() + new Vector2(0f, -T * 0.55f));
                    break;
                case SceneKind.Movie:
                    foreach (var id in s.Floor)
                        if (s.Here.Contains(id) && sc.CrewOf(id) is CrewMember f) PaintCushion(ci, CrewPx(f), id);
                    break;
            }
        }
        foreach (var n in sc.Notes)
            if (!n.Gone) PaintNote(ci, n, close);
    }

    // ── 체스: 나무 판 · 8×8 칸 · 진척만큼 줄어든 말 · 멈췄으면 덮개와 ❚❚ ──
    private void PaintChessBoard(CanvasItem ci, DailyScene s, SceneThing t, bool close)
    {
        var r = CellRect(t.At);
        float size = T * 0.86f;
        var o = r.GetCenter() - new Vector2(size, size) * 0.5f;
        ci.DrawRect(new Rect2(o - new Vector2(2f, 2f), new Vector2(size + 4f, size + 4f)), WoodDark);
        ci.DrawRect(new Rect2(o - new Vector2(2f, 2f), new Vector2(size + 4f, 1.2f)), Wood.Lightened(0.3f));
        float q = size / 8f;
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 8; j++)
                ci.DrawRect(new Rect2(o + new Vector2(i * q, j * q), new Vector2(q, q)), (i + j) % 2 == 0 ? new Color(0.93f, 0.86f, 0.7f) : new Color(0.45f, 0.3f, 0.18f));
        var g = _world.Belongings.Games.FirstOrDefault(x => x.Id == s.Game);
        float moves = g?.Moves ?? 0f;
        int taken = Math.Min(12, (int)(moves / 4f));
        int bucket = (int)moves; // 한 수마다 바뀐다
        if (close)
        {
            for (int side = 0; side < 2; side++)
            {
                int left = 16 - taken / 2 - (side == 0 ? taken % 2 : 0);
                for (int k = 0; k < left; k++)
                {
                    // 처음 자리(양 끝 두 줄)에서 수가 늘수록 판 가운데로 흩어진다
                    uint hsh = (uint)(s.Id * 7919 + side * 104729 + k * 1299709 + (k < bucket ? bucket * 31 : 0));
                    int col = k % 8, row = side == 0 ? 7 - k / 8 : k / 8;
                    if (k < bucket) { col = (int)(hsh % 8); row = Math.Clamp(row + (side == 0 ? -1 : 1) * (int)(hsh / 8 % 4), 0, 7); }
                    var c = o + new Vector2((col + 0.5f) * q, (row + 0.5f) * q);
                    // 남은 말: 뒷줄(룩 나이트 비숍 퀸 킹 비숍 나이트 룩)이 먼저 남고, 잡히는 건 폰부터
                    int kind = k < 8 ? BackRank[k] : 0;
                    PaintPiece(ci, c, q, kind, side == 0);
                }
            }
            // 잡힌 말: 판 옆에 줄 선다 (흰 말은 왼쪽 · 검은 말은 오른쪽)
            for (int k = 0; k < taken; k++)
            {
                bool white = k % 2 == 1;
                var c = o + new Vector2(white ? -q * 0.9f : size + q * 0.9f, q * (0.6f + (k / 2) * 0.9f));
                PaintPiece(ci, c, q * 0.8f, 0, white);
            }
            // 방금 둔 칸이 깜빡인다 (두는 동안만)
            if (s.Stage == SceneStage.Run)
            {
                uint lm = (uint)(s.Id * 31 + bucket * 17);
                var lc = o + new Vector2((lm % 8) * q, (lm / 8 % 8) * q);
                ci.DrawRect(new Rect2(lc, new Vector2(q, q)), new Color(0.4f, 0.9f, 0.5f, 0.25f + 0.2f * Mathf.Sin(_time * 5f)), false, 1f);
            }
        }
        else
        {
            // 멀리서: 양쪽 말 덩어리만
            ci.DrawRect(new Rect2(o + new Vector2(0, size * 0.78f), new Vector2(size, size * 0.2f)), new Color(0.98f, 0.96f, 0.9f, 0.8f));
            ci.DrawRect(new Rect2(o + new Vector2(0, size * 0.02f), new Vector2(size, size * 0.2f)), new Color(0.1f, 0.08f, 0.08f, 0.8f));
        }
        // 진척 고리 (판 귀퉁이)
        var corner = o + new Vector2(size + 3f, -3f);
        ci.DrawArc(corner, 4f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Clamp(s.Progress, 0f, 1f), 16, new Color(0.95f, 0.85f, 0.4f), 1.6f, true);
        if (s.Stage == SceneStage.Paused)
        {
            ci.DrawRect(new Rect2(o, new Vector2(size, size)), new Color(0.05f, 0.06f, 0.1f, 0.35f));
            var pc = o + new Vector2(size, size) * 0.5f;
            ci.DrawRect(new Rect2(pc + new Vector2(-4f, -5f), new Vector2(3f, 10f)), new Color(1f, 1f, 1f, 0.85f));
            ci.DrawRect(new Rect2(pc + new Vector2(1f, -5f), new Vector2(3f, 10f)), new Color(1f, 1f, 1f, 0.85f));
        }
    }

    private static readonly int[] BackRank = { 1, 2, 3, 4, 5, 3, 2, 1 }; // 0 폰 · 1 룩 · 2 나이트 · 3 비숍 · 4 퀸 · 5 킹

    /// <summary>체스 말 하나: 종류마다 다른 실루엣 (위에서 본 모양 — 받침 고리 + 머리).</summary>
    private void PaintPiece(CanvasItem ci, Vector2 c, float q, int kind, bool white)
    {
        var fill = white ? new Color(0.98f, 0.96f, 0.9f) : new Color(0.13f, 0.11f, 0.1f);
        var rim = white ? new Color(0.3f, 0.24f, 0.17f) : new Color(0.82f, 0.76f, 0.66f);
        float r = q * 0.36f;
        switch (kind)
        {
            case 0: // 폰: 작은 받침과 둥근 머리
                ci.DrawCircle(c, r * 0.8f, rim);
                ci.DrawCircle(c, r * 0.62f, fill);
                ci.DrawCircle(c + new Vector2(-r * 0.15f, -r * 0.15f), r * 0.2f, fill.Lightened(0.25f));
                break;
            case 1: // 룩: 네모 성탑 · 네 귀퉁이 흉벽
                ci.DrawRect(new Rect2(c - new Vector2(r, r), new Vector2(r * 2f, r * 2f)), rim);
                ci.DrawRect(new Rect2(c - new Vector2(r * 0.75f, r * 0.75f), new Vector2(r * 1.5f, r * 1.5f)), fill);
                for (int i = 0; i < 4; i++)
                {
                    var d = new Vector2(i % 2 == 0 ? -1f : 1f, i < 2 ? -1f : 1f) * r * 0.55f;
                    ci.DrawRect(new Rect2(c + d - new Vector2(r * 0.18f, r * 0.18f), new Vector2(r * 0.36f, r * 0.36f)), rim);
                }
                break;
            case 2: // 나이트: 말 머리 (주둥이가 상대 쪽을 본다)
            {
                float f = white ? -1f : 1f;
                ci.DrawCircle(c, r * 0.95f, rim);
                ci.DrawColoredPolygon(new[] { c + new Vector2(-r * 0.6f, r * 0.6f), c + new Vector2(r * 0.6f, r * 0.6f), c + new Vector2(r * 0.55f, -r * 0.1f * f),
                    c + new Vector2(r * 0.15f, r * 0.85f * f), c + new Vector2(-r * 0.45f, r * 0.35f * f) }, fill);
                ci.DrawCircle(c + new Vector2(r * 0.15f, r * 0.2f * f), r * 0.12f, rim);
                break;
            }
            case 3: // 비숍: 길쭉한 머리와 비스듬한 홈
                ci.DrawCircle(c, r * 0.9f, rim);
                ci.DrawCircle(c, r * 0.68f, fill);
                ci.DrawLine(c + new Vector2(-r * 0.4f, r * 0.35f), c + new Vector2(r * 0.4f, -r * 0.35f), rim, 1f);
                ci.DrawCircle(c + new Vector2(0f, -r * 0.7f), r * 0.16f, rim);
                break;
            case 4: // 퀸: 다섯 갈래 관
                ci.DrawCircle(c, r * 1.05f, rim);
                ci.DrawCircle(c, r * 0.8f, fill);
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.Tau * i / 5f - Mathf.Pi / 2f;
                    ci.DrawCircle(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.62f, r * 0.15f, rim);
                }
                break;
            default: // 킹: 큰 받침과 십자
                ci.DrawCircle(c, r * 1.15f, rim);
                ci.DrawCircle(c, r * 0.88f, fill);
                ci.DrawLine(c + new Vector2(0f, -r * 0.6f), c + new Vector2(0f, r * 0.6f), rim, 1.1f);
                ci.DrawLine(c + new Vector2(-r * 0.4f, -r * 0.15f), c + new Vector2(r * 0.4f, -r * 0.15f), rim, 1.1f);
                break;
        }
    }

    // ── 잔: 커피는 두꺼운 머그(손잡이 · 크레마 고리) · 차는 받침 위 찻잔(호박색 · 티백 꼬리표) · 뜨거울 땐 김, 식으면 막 ──
    private void PaintCup(CanvasItem ci, SceneThing t, bool close)
    {
        var c = CellRect(t.At).GetCenter() + new Vector2((t.Owner % 3 - 1) * 8f, (t.Owner / 3 % 2 == 0 ? -1f : 1f) * 7f);
        float age = (_world.Tick - t.Since) / (float)SimTime.TicksPerHour;
        bool hot = age < 0.5f;
        var tint = Palette.Crew(t.Owner);
        if (t.Tea)
        {
            ci.DrawCircle(c, 6.2f, new Color(0.92f, 0.92f, 0.95f));
            ci.DrawArc(c, 6.2f, 0f, Mathf.Tau, 20, new Color(0.6f, 0.62f, 0.7f), 0.8f, true);
            ci.DrawCircle(c, 4.2f, new Color(0.97f, 0.97f, 1f));
            ci.DrawCircle(c, 3.2f, hot ? new Color(0.85f, 0.55f, 0.18f) : new Color(0.62f, 0.4f, 0.16f));
            if (close) { ci.DrawLine(c + new Vector2(2f, -2f), c + new Vector2(6.5f, -6.5f), new Color(0.9f, 0.9f, 0.85f), 0.7f); ci.DrawRect(new Rect2(c + new Vector2(5.5f, -8.5f), new Vector2(3f, 3f)), tint); }
        }
        else
        {
            ci.DrawCircle(c, 4.6f, tint.Darkened(0.15f));
            ci.DrawArc(c + new Vector2(4.6f, 0f), 2.2f, -Mathf.Pi / 2f, Mathf.Pi / 2f, 8, tint.Darkened(0.3f), 1.4f, true);
            ci.DrawCircle(c, 3.4f, hot ? new Color(0.3f, 0.17f, 0.08f) : new Color(0.2f, 0.12f, 0.06f));
            if (close) ci.DrawArc(c, 2.4f, 0f, Mathf.Tau, 14, hot ? new Color(0.78f, 0.58f, 0.36f, 0.9f) : new Color(0.45f, 0.35f, 0.25f, 0.6f), 0.7f, true);
        }
        if (hot)
        {
            float a = 0.55f * (1f - age / 0.5f);
            for (int k = 0; k < 2; k++)
            {
                float ph = (_time * 0.7f + k * 0.5f + t.Owner * 0.13f) % 1f;
                var p0 = c + new Vector2(-1.5f + 3f * k, -3f - 9f * ph);
                ci.DrawLine(p0, p0 + new Vector2(1.6f * Mathf.Sin(_time * 3f + k), -3f), new Color(1f, 1f, 1f, a * (1f - ph)), 1f, true);
            }
        }
        else if (close) ci.DrawLine(c + new Vector2(-2f, 0.5f), c + new Vector2(2f, 0.2f), new Color(0.75f, 0.65f, 0.5f, 0.5f), 0.6f); // 식은 막
    }

    private void PaintTray(CanvasItem ci, Vector2 p, int cups, bool tea)
    {
        ci.DrawRect(new Rect2(p - new Vector2(5f, 3f), new Vector2(10f, 6f)), new Color(0.55f, 0.58f, 0.62f));
        ci.DrawRect(new Rect2(p - new Vector2(5f, 3f), new Vector2(10f, 6f)), new Color(0.3f, 0.32f, 0.36f), false, 0.8f);
        for (int k = 0; k < Math.Clamp(cups, 1, 3); k++)
        {
            var c = p + new Vector2(-3f + 3f * k, 0f);
            ci.DrawCircle(c, 1.6f, tea ? new Color(0.85f, 0.55f, 0.18f) : new Color(0.3f, 0.17f, 0.08f));
            ci.DrawLine(c + new Vector2(0, -2f), c + new Vector2(0.8f * Mathf.Sin(_time * 4f + k), -5f), new Color(1, 1, 1, 0.4f), 0.7f);
        }
    }

    // ── 국 자국: 들쭉날쭉한 웅덩이 · 튄 방울 · 건더기 · 번들거림 · 닦인 만큼 걸레 자국 ──
    private void PaintStain(CanvasItem ci, DailyScene s, SceneThing t, bool close)
    {
        var c = CellRect(t.At).GetCenter();
        float a = Mathf.Clamp(t.Amount, 0.15f, 1f);
        var pts = new Vector2[12];
        for (int k = 0; k < 12; k++)
        {
            uint hh = (uint)(s.Id * 2654435761u + k * 40503u);
            float rr = T * 0.34f * (0.65f + (hh % 100) / 220f);
            float ang = Mathf.Tau * k / 12f;
            pts[k] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f) * rr;
        }
        ci.DrawColoredPolygon(pts, new Color(0.72f, 0.42f, 0.18f, 0.7f * a));
        var inner = pts.Select(p => c + (p - c) * 0.6f).ToArray();
        ci.DrawColoredPolygon(inner, new Color(0.8f, 0.5f, 0.22f, 0.55f * a));
        for (int k = 0; k < 6; k++)
        {
            uint hh = (uint)(s.Id * 97 + k * 7121);
            var d = c + new Vector2((int)(hh % 23) - 11, (int)(hh / 23 % 19) - 9) * 1.2f;
            ci.DrawCircle(d, 1.2f + hh % 3 * 0.4f, new Color(0.7f, 0.4f, 0.17f, 0.75f * a));
        }
        if (close)
        {
            // 건더기 (파 · 두부)
            ci.DrawRect(new Rect2(c + new Vector2(-4f, -2f), new Vector2(2.2f, 2.2f)), new Color(0.95f, 0.93f, 0.85f, a));
            ci.DrawLine(c + new Vector2(2f, 1f), c + new Vector2(5f, 2.5f), new Color(0.35f, 0.65f, 0.25f, a), 1.4f);
            // 번들거림
            float sh = 0.25f + 0.15f * Mathf.Sin(_time * 2f + s.Id);
            ci.DrawArc(c + new Vector2(-2f, -3f), 4f, Mathf.Pi * 1.1f, Mathf.Pi * 1.6f, 8, new Color(1f, 1f, 1f, sh * a), 1f, true);
        }
        // 오래 둔 자국: 가장자리가 말라붙어 갈라진 딱지 (반나절 넘게 아무도 안 닦았다)
        float hours = (_world.Tick - t.Since) / (float)SimTime.TicksPerHour;
        if (hours > 6f && s.Progress < 0.05f)
        {
            var crust = new Color(0.45f, 0.26f, 0.1f, Mathf.Clamp((hours - 6f) / 12f, 0.2f, 0.75f) * a);
            for (int k = 0; k < 12; k++) ci.DrawLine(pts[k], pts[(k + 1) % 12], crust, 1.4f, true);
            if (close)
                for (int k = 0; k < 4; k++)
                {
                    uint hh = (uint)(s.Id * 131 + k * 977);
                    var p0 = c + new Vector2((int)(hh % 9) - 4, (int)(hh / 9 % 7) - 3);
                    ci.DrawPolyline(new[] { p0, p0 + new Vector2(2f, 1.5f), p0 + new Vector2(3.5f, 0.5f) }, crust, 0.6f, true);
                }
        }
        // 기름 막: 배 본체 바닥 상태의 기름이 남아 있으면 무지갯빛이 돈다 (닦으면 사라진다)
        float oil = _world.Body.Mark(t.At, CellMark.Oil);
        if (oil > 0.1f && close)
            for (int k = 0; k < 3; k++)
            {
                float hue = Mathf.PosMod(0.55f + k * 0.12f + _time * 0.03f, 1f);
                ci.DrawArc(c + new Vector2(2f, 1f), 3f + k * 1.6f, Mathf.Pi * (0.1f + k * 0.2f), Mathf.Pi * (0.9f + k * 0.2f), 10, Color.FromHsv(hue, 0.5f, 1f, 0.35f * oil), 0.8f, true);
            }
        if (s.Progress > 0.02f)
        {
            // 걸레질 자국: 닦인 쪽부터 젖은 회색 호
            int strokes = 1 + (int)(s.Progress * 4f);
            for (int k = 0; k < strokes; k++)
                ci.DrawArc(c + new Vector2(-6f + 4f * k, 2f), 5f, Mathf.Pi * 0.9f, Mathf.Pi * 2.1f, 10, new Color(0.75f, 0.82f, 0.88f, 0.35f), 1.6f, true);
        }
    }

    private void PaintMop(CanvasItem ci, Vector2 p, bool working)
    {
        float sw = working ? 3f * Mathf.Sin(_time * 7f) : 0f;
        var head = p + new Vector2(CrewRadius * 0.9f + sw, CrewRadius * 0.9f);
        ci.DrawLine(p + new Vector2(CrewRadius * 0.5f, -CrewRadius * 0.4f), head, new Color(0.6f, 0.45f, 0.3f), 1.6f, true);
        for (int k = -2; k <= 2; k++) ci.DrawLine(head, head + new Vector2(k * 1.4f, 3.5f), new Color(0.85f, 0.85f, 0.8f), 1f, true);
    }

    // ── "미끄럼 주의" 노란 A자 표지: 닦는 동안 자국 곁에 세운다 ──
    private void PaintWetSign(CanvasItem ci, Vector2 p, bool close)
    {
        var yel = new Color(1f, 0.82f, 0.1f);
        ci.DrawColoredPolygon(new[] { p + new Vector2(-4f, 5f), p + new Vector2(0f, -6f), p + new Vector2(4f, 5f) }, yel);
        ci.DrawPolyline(new[] { p + new Vector2(-4f, 5f), p + new Vector2(0f, -6f), p + new Vector2(4f, 5f), p + new Vector2(-4f, 5f) }, new Color(0.25f, 0.2f, 0.05f), 0.8f, true);
        if (close)
        {
            // 넘어지는 사람 그림 (머리 · 몸 · 다리)
            ci.DrawCircle(p + new Vector2(-0.8f, -1.5f), 0.8f, new Color(0.1f, 0.1f, 0.1f));
            ci.DrawLine(p + new Vector2(-0.5f, -0.6f), p + new Vector2(0.8f, 1.6f), new Color(0.1f, 0.1f, 0.1f), 0.7f);
            ci.DrawLine(p + new Vector2(0.8f, 1.6f), p + new Vector2(2.2f, 1.2f), new Color(0.1f, 0.1f, 0.1f), 0.6f);
            ci.DrawLine(p + new Vector2(-2.4f, 3.6f), p + new Vector2(2.4f, 3.6f), new Color(0.1f, 0.1f, 0.1f), 0.5f);
        }
    }

    // ── 캄캄해서 멈춘 작업대: 꺼진 전구 (깜빡이는 물음표) ──
    private void PaintDeadBulb(CanvasItem ci, Vector2 p)
    {
        ci.DrawCircle(p, 3.2f, new Color(0.35f, 0.36f, 0.4f, 0.9f));
        ci.DrawRect(new Rect2(p + new Vector2(-1.6f, 2.6f), new Vector2(3.2f, 2f)), new Color(0.55f, 0.55f, 0.58f));
        ci.DrawLine(p + new Vector2(-1f, 0.5f), p + new Vector2(1f, -1f), new Color(0.2f, 0.2f, 0.22f), 0.6f);
        Gfx.TextCentered(ci, Fonts.Bold, p + new Vector2(5.5f, -3f), "?", 8, new Color(0.85f, 0.88f, 1f, 0.5f + 0.5f * Mathf.Sin(_time * 3f)));
    }

    // ── 주 컴퓨터가 본 몽유병: 복도 감지기의 퍼지는 고리 · 부른 당직까지 점선 (오는 중) ──
    private void PaintSensorPing(CanvasItem ci, CrewMember z, CrewMember? coming)
    {
        var p = CrewPx(z);
        var cyan = new Color(0.35f, 0.9f, 1f);
        for (int k = 0; k < 2; k++)
        {
            float ph = (_time * 0.6f + k * 0.5f) % 1f;
            ci.DrawArc(p, CrewRadius * (1.6f + 2.2f * ph), 0f, Mathf.Tau, 28, cyan.WithAlpha(0.45f * (1f - ph)), 1.2f, true);
        }
        // 감지기 표시 (작은 부채꼴)
        var head = p + new Vector2(0f, -CrewRadius * 2.4f);
        ci.DrawColoredPolygon(new[] { head, head + new Vector2(-4f, 6f), head + new Vector2(4f, 6f) }, cyan.WithAlpha(0.18f));
        ci.DrawCircle(head, 1.4f, cyan);
        if (coming != null) ci.DrawDashedLine(CrewPx(coming), p, cyan.WithAlpha(0.6f), 1f, 4f);
    }

    // ── 이름표 붙은 남의 접시 (음식에서 덜어 둔 몫) — 먹는 손에 들린 접시 · 이름표는 주인 색 ──
    private void PaintTaggedPlate(CanvasItem ci, Vector2 p, int owner, bool close)
    {
        ci.DrawCircle(p, 4.6f, new Color(0.95f, 0.95f, 0.97f));
        ci.DrawArc(p, 4.6f, 0f, Mathf.Tau, 18, new Color(0.6f, 0.62f, 0.68f), 0.6f, true);
        ci.DrawCircle(p, 3f, new Color(0.85f, 0.55f, 0.25f));
        ci.DrawCircle(p + new Vector2(-1f, -0.5f), 0.9f, new Color(0.4f, 0.7f, 0.3f));
        var tag = new Rect2(p + new Vector2(2.5f, -6.5f), new Vector2(5f, 3f));
        ci.DrawRect(tag, new Color(0.98f, 0.97f, 0.9f));
        ci.DrawRect(tag, owner >= 0 ? Palette.Crew(owner) : new Color(0.5f, 0.5f, 0.5f), false, 0.7f);
        if (close) ci.DrawLine(tag.Position + new Vector2(1f, 1.5f), tag.Position + new Vector2(4f, 1.5f), new Color(0.2f, 0.2f, 0.25f), 0.5f);
    }

    // ── 영화: 벽의 스크린(돌면 색이 흐르고 · 멈추면 회색 ❚❚ · 정전이면 꺼진 검정) · 영사기 빛 · 진척 줄 · 소리를 줄였으면 스피커에 빗금 ──
    private void PaintScreen(CanvasItem ci, DailyScene s, SceneThing t, bool close)
    {
        var r = CellRect(t.At);
        var c = r.GetCenter();
        var size = new Vector2(T * 1.7f, T * 0.32f);
        var o = c - new Vector2(size.X * 0.5f, T * 0.45f);
        ci.DrawRect(new Rect2(o - new Vector2(1.5f, 1.5f), size + new Vector2(3f, 3f)), new Color(0.15f, 0.15f, 0.18f));
        bool run = s.Stage == SceneStage.Run, dark = s.PauseWhy == "정전";
        if (run)
        {
            int bands = close ? 6 : 2;
            for (int k = 0; k < bands; k++)
            {
                float hue = Mathf.PosMod(_time * 0.05f + k * 0.13f + Mathf.Sin(_time * 1.3f + k) * 0.05f, 1f);
                ci.DrawRect(new Rect2(o + new Vector2(size.X * k / bands, 0f), new Vector2(size.X / bands + 0.5f, size.Y)), Color.FromHsv(hue, 0.45f, 0.75f + 0.2f * Mathf.Sin(_time * 9f + k * 2f)));
            }
            // 영사기 빛
            var src = CellRect(s.Spot).GetCenter();
            if (src.DistanceTo(c) > T * 0.5f)
                ci.DrawColoredPolygon(new[] { src, o + new Vector2(0f, size.Y), o + size }, new Color(1f, 0.95f, 0.8f, 0.07f));
        }
        else if (s.PauseWhy == "보관함 꺼짐")
        {
            // 보관함이 꺼졌다: 파란 빈 화면에 도는 불러오기 고리
            ci.DrawRect(new Rect2(o, size), new Color(0.08f, 0.16f, 0.4f));
            var pc = o + size * 0.5f;
            float a0 = _time * 4f;
            ci.DrawArc(pc, size.Y * 0.32f, a0, a0 + Mathf.Pi * 1.4f, 12, new Color(0.85f, 0.9f, 1f), 1.2f, true);
        }
        else
        {
            ci.DrawRect(new Rect2(o, size), dark ? new Color(0.02f, 0.02f, 0.03f) : new Color(0.42f, 0.44f, 0.48f));
            var pc = o + size * 0.5f;
            if (dark) Gfx.TextCentered(ci, Fonts.Bold, pc + new Vector2(0f, 4f), "!", 10, new Color(1f, 0.35f, 0.3f, 0.6f + 0.4f * Mathf.Sin(_time * 4f)));
            else if (s.Stage == SceneStage.Paused) { ci.DrawRect(new Rect2(pc + new Vector2(-4f, -3.5f), new Vector2(2.5f, 7f)), Colors.White); ci.DrawRect(new Rect2(pc + new Vector2(1.5f, -3.5f), new Vector2(2.5f, 7f)), Colors.White); }
        }
        // 진척 줄
        ci.DrawRect(new Rect2(o + new Vector2(0f, size.Y + 2f), new Vector2(size.X, 1.6f)), new Color(0.2f, 0.2f, 0.24f));
        ci.DrawRect(new Rect2(o + new Vector2(0f, size.Y + 2f), new Vector2(size.X * Mathf.Clamp(s.Progress, 0f, 1f), 1.6f)), new Color(0.95f, 0.75f, 0.35f));
        // 스피커 (소리를 줄였으면 빗금)
        var sp = o + new Vector2(size.X + 5f, size.Y * 0.5f);
        ci.DrawColoredPolygon(new[] { sp + new Vector2(-2f, -1.5f), sp + new Vector2(0f, -1.5f), sp + new Vector2(2.5f, -3.5f), sp + new Vector2(2.5f, 3.5f), sp + new Vector2(0f, 1.5f), sp + new Vector2(-2f, 1.5f) }, new Color(0.8f, 0.8f, 0.85f, 0.8f));
        if (s.Holding) ci.DrawLine(sp + new Vector2(-3f, 4f), sp + new Vector2(4f, -4f), new Color(1f, 0.4f, 0.35f), 1.2f, true);
        if (s.Holding && s.ComputerAct >= 0)
        {
            // 컴퓨터가 낮췄다: 스피커 옆 작은 칩 (다리 넷)
            var chip = sp + new Vector2(7f, -4f);
            ci.DrawRect(new Rect2(chip - new Vector2(2.2f, 2.2f), new Vector2(4.4f, 4.4f)), new Color(0.2f, 0.75f, 0.9f));
            for (int k = -1; k <= 1; k += 2) { ci.DrawLine(chip + new Vector2(k * 2.2f, -1f), chip + new Vector2(k * 3.4f, -1f), new Color(0.6f, 0.9f, 1f), 0.5f); ci.DrawLine(chip + new Vector2(k * 2.2f, 1f), chip + new Vector2(k * 3.4f, 1f), new Color(0.6f, 0.9f, 1f), 0.5f); }
        }
        // 보관함에서 트는 영화: 화면 위 제목 (가까이에서)
        if (s.Film != null && close) Gfx.TextCentered(ci, Fonts.Bold, o + new Vector2(size.X * 0.5f, -4f), $"「{s.Film}」", 7, new Color(0.95f, 0.9f, 0.75f, 0.85f));
        else if (run) for (int k = 1; k <= 2; k++) ci.DrawArc(sp + new Vector2(2.5f, 0f), 2f * k + Mathf.PosMod(_time * 3f, 1f), -0.7f, 0.7f, 6, new Color(1f, 1f, 1f, 0.35f), 0.8f, true);
    }

    private void PaintCushion(CanvasItem ci, Vector2 p, int id)
    {
        var col = Palette.Crew(id).Lerp(new Color(0.6f, 0.3f, 0.35f), 0.5f);
        var rect = new Rect2(p + new Vector2(-CrewRadius * 0.9f, CrewRadius * 0.35f), new Vector2(CrewRadius * 1.8f, CrewRadius * 0.7f));
        ci.DrawRect(rect, col);
        ci.DrawRect(rect, col.Darkened(0.35f), false, 0.8f);
        ci.DrawLine(rect.GetCenter() + new Vector2(-2f, 0f), rect.GetCenter() + new Vector2(2f, 0f), col.Lightened(0.3f), 0.8f);
    }

    // ── 몽유병: 앞으로 내민 두 팔 · 떠오르는 z · 희미한 푸른 기운 · 데려가는 사람과 잡은 손 ──
    private void PaintSleepwalker(CanvasItem ci, CrewMember z, CrewMember? escort)
    {
        var p = CrewPx(z);
        var f = z.Facing.ToGodot();
        if (f.LengthSquared() < 0.01f) f = new Vector2(0f, 1f);
        f = f.Normalized();
        var side = new Vector2(-f.Y, f.X);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 2f);
        ci.DrawArc(p, CrewRadius * 1.35f, 0f, Mathf.Tau, 24, new Color(0.55f, 0.65f, 1f, 0.25f + 0.2f * pulse), 1.4f, true);
        for (int k = -1; k <= 1; k += 2)
        {
            var sh = p + side * (CrewRadius * 0.45f * k);
            ci.DrawLine(sh, sh + f * CrewRadius * 1.25f, new Color(0.92f, 0.85f, 0.75f), 1.6f, true);
        }
        for (int k = 0; k < 3; k++)
        {
            float t = (_time * 0.5f + k / 3f) % 1f;
            Gfx.Text(ci, Fonts.Bold, p + new Vector2(CrewRadius * 0.6f + 5f * t, -CrewRadius - 3f - 14f * t), "z", 8 + k * 2, new Color(0.8f, 0.85f, 1f, 1f - t));
        }
        if (escort != null) ci.DrawLine(p, CrewPx(escort), new Color(0.95f, 0.9f, 0.8f, 0.7f), 1.4f, true);
    }

    // ── 만들다 만 소품: 작업대 위 점선 뼈대가 진척만큼 아래부터 채워진다 · 만드는 동안 톱밥 · 다 만들면 들고 간다 ──
    private void PaintWip(CanvasItem ci, DailyScene s, SceneThing t, bool close)
    {
        var c = CellRect(t.At).GetCenter();
        float w0 = T * 0.55f, h0 = T * 0.5f;
        var o = c - new Vector2(w0, h0) * 0.5f;
        var col = Palette.Crew(s.Host);
        float p = Mathf.Clamp(s.Progress, 0f, 1f);
        ci.DrawRect(new Rect2(o + new Vector2(0f, h0 * (1f - p)), new Vector2(w0, h0 * p)), col.Lerp(Wood, 0.5f).WithAlpha(0.85f));
        ci.DrawDashedLine(o, o + new Vector2(w0, 0f), col, 1f, 2f);
        ci.DrawDashedLine(o, o + new Vector2(0f, h0), col, 1f, 2f);
        ci.DrawDashedLine(o + new Vector2(w0, 0f), o + new Vector2(w0, h0), col, 1f, 2f);
        ci.DrawLine(o + new Vector2(0f, h0), o + new Vector2(w0, h0), col, 1.2f);
        if (close && s.Prop != null)
        {
            // 모양마다 다른 윗부분 (액자 · 화분 · 등 · 모형 …)
            var top = o + new Vector2(w0 * 0.5f, 0f);
            switch (s.Prop.Shape)
            {
                case PropShape.Frame or PropShape.Poster or PropShape.Map: ci.DrawRect(new Rect2(o + new Vector2(3f, 3f), new Vector2(w0 - 6f, h0 - 6f)), col.WithAlpha(0.5f), false, 1f); break;
                case PropShape.Pot: ci.DrawCircle(top + new Vector2(0f, 2f), 3f, new Color(0.35f, 0.7f, 0.35f, 0.4f + 0.6f * p)); break;
                case PropShape.Lamp or PropShape.Lights: ci.DrawCircle(top, 2.5f, new Color(1f, 0.9f, 0.5f, 0.3f + 0.7f * p)); break;
                case PropShape.Model: ci.DrawColoredPolygon(new[] { top + new Vector2(-6f, 4f), top + new Vector2(6f, 4f), top + new Vector2(0f, -3f) }, col.WithAlpha(0.3f + 0.7f * p)); break;
                default: ci.DrawLine(o + new Vector2(2f, h0 * 0.5f), o + new Vector2(w0 - 2f, h0 * 0.5f), col.WithAlpha(0.6f), 0.8f); break;
            }
        }
        if (s.Here.Contains(s.Host) && s.Stage == SceneStage.Run)
            for (int k = 0; k < 4; k++)
            {
                float ph = (_time * 1.3f + k * 0.25f) % 1f;
                var d = c + new Vector2((k - 1.5f) * 4f + 2f * Mathf.Sin(_time * 5f + k), h0 * 0.5f + 5f * ph);
                ci.DrawCircle(d, 0.8f, new Color(0.9f, 0.8f, 0.6f, 1f - ph));
            }
        ci.DrawArc(o + new Vector2(w0 + 4f, -2f), 3.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * p, 14, new Color(0.95f, 0.85f, 0.4f), 1.4f, true);
    }

    private void PaintCarriedProp(CanvasItem ci, Vector2 p, DailyScene s)
    {
        var col = Palette.Crew(s.Host);
        ci.DrawRect(new Rect2(p - new Vector2(6f, 4.5f), new Vector2(12f, 9f)), Wood);
        ci.DrawRect(new Rect2(p - new Vector2(6f, 4.5f), new Vector2(12f, 9f)), WoodDark, false, 1f);
        ci.DrawLine(p + new Vector2(-6f, 0f), p + new Vector2(6f, 0f), col, 1.2f);
        ci.DrawLine(p + new Vector2(0f, -4.5f), p + new Vector2(0f, 4.5f), col, 1.2f);
    }

    private void PaintPudding(CanvasItem ci, Vector2 p)
    {
        ci.DrawCircle(p, 3.8f, new Color(0.95f, 0.9f, 0.75f));
        ci.DrawCircle(p, 2.8f, new Color(0.98f, 0.82f, 0.45f));
        ci.DrawCircle(p + new Vector2(-0.5f, -0.5f), 1.4f, new Color(0.55f, 0.3f, 0.12f));
        ci.DrawLine(p + new Vector2(1f, 1f), p + new Vector2(5f, 5.5f), new Color(0.82f, 0.84f, 0.88f), 1f, true);
    }

    // ── 쪽지 · 게시물 ──
    private void PaintNote(CanvasItem ci, ShipNote n, bool close)
    {
        var c = CellRect(n.At).GetCenter() + new Vector2((n.Id % 3 - 1) * 6f, -T * 0.3f + (n.Id / 3 % 2) * 4f);
        float rot = ((n.Id * 37) % 21 - 10) * 0.012f;
        ci.DrawSetTransform(c, rot, Vector2.One);
        var ink = new Color(0.15f, 0.15f, 0.2f, 0.85f);
        switch (n.Kind)
        {
            case NoteKind.Fridge:
                ci.DrawRect(new Rect2(-5f, -5f, 10f, 10f), new Color(1f, 0.92f, 0.35f));
                ci.DrawRect(new Rect2(-3f, -6.5f, 6f, 2.4f), new Color(0.9f, 0.95f, 1f, 0.55f)); // 테이프
                if (close) for (int k = 0; k < 3; k++) ci.DrawLine(new Vector2(-3.5f, -2f + k * 2.4f), new Vector2(2.5f - k, -2f + k * 2.4f), ink, 0.7f);
                break;
            case NoteKind.Roster:
                ci.DrawRect(new Rect2(-7f, -6f, 14f, 12f), new Color(0.96f, 0.96f, 0.94f));
                for (int k = 1; k < 4; k++) ci.DrawLine(new Vector2(-7f, -6f + k * 3f), new Vector2(7f, -6f + k * 3f), new Color(0.5f, 0.6f, 0.75f, 0.6f), 0.5f);
                ci.DrawLine(new Vector2(-3f, -6f), new Vector2(-3f, 6f), new Color(0.5f, 0.6f, 0.75f, 0.6f), 0.5f);
                // 빨간 낙서 (지그재그와 웃는 얼굴)
                ci.DrawPolyline(new[] { new Vector2(-1f, 1f), new Vector2(1f, -2f), new Vector2(3f, 2f), new Vector2(5f, -1f) }, new Color(0.9f, 0.15f, 0.15f), 0.9f, true);
                if (close) { ci.DrawArc(new Vector2(-5f, 2.5f), 1.8f, 0f, Mathf.Tau, 10, new Color(0.9f, 0.15f, 0.15f), 0.6f); ci.DrawArc(new Vector2(-5f, 2.5f), 1f, 0.3f, 2.8f, 6, new Color(0.9f, 0.15f, 0.15f), 0.5f); }
                ci.DrawCircle(new Vector2(0f, -6f), 1.3f, new Color(0.85f, 0.2f, 0.2f)); // 압정
                break;
            case NoteKind.Card:
                var pink = new Color(0.98f, 0.62f, 0.75f);
                ci.DrawRect(new Rect2(-7f, -5f, 7f, 10f), pink.Darkened(0.08f));
                ci.DrawRect(new Rect2(0f, -5f, 7f, 10f), pink);
                ci.DrawLine(new Vector2(0f, -5f), new Vector2(0f, 5f), pink.Darkened(0.3f), 0.8f);
                // 하트
                ci.DrawCircle(new Vector2(2.6f, -1.2f), 1.3f, new Color(0.9f, 0.15f, 0.3f));
                ci.DrawCircle(new Vector2(4.4f, -1.2f), 1.3f, new Color(0.9f, 0.15f, 0.3f));
                ci.DrawColoredPolygon(new[] { new Vector2(1.4f, -0.8f), new Vector2(5.6f, -0.8f), new Vector2(3.5f, 2.2f) }, new Color(0.9f, 0.15f, 0.3f));
                if (close)
                    for (int k = 0; k < Math.Min(8, n.Signers.Count); k++)
                    {
                        var y = -3.5f + k * 1.1f;
                        ci.DrawLine(new Vector2(-6f, y), new Vector2(-6f + 3f + (k % 3), y + 0.4f), Palette.Crew(n.Signers[k]), 0.6f);
                    }
                if (n.Given) ci.DrawArc(Vector2.Zero, 9f + Mathf.Sin(_time * 3f), 0f, Mathf.Tau, 20, new Color(1f, 0.8f, 0.4f, 0.5f), 1f, true);
                break;
            default: // 인수인계 메모
                ci.DrawRect(new Rect2(-5f, -6f, 10f, 12f), new Color(0.97f, 0.98f, 1f));
                ci.DrawRect(new Rect2(-5f, -6f, 10f, 12f), new Color(0.55f, 0.6f, 0.7f), false, 0.6f);
                ci.DrawArc(new Vector2(-2.5f, -6f), 1.6f, Mathf.Pi, Mathf.Tau * 1.1f, 8, new Color(0.7f, 0.72f, 0.78f), 1f, true); // 클립
                ci.DrawLine(new Vector2(-3.5f, -2.5f), new Vector2(-3.5f, 0.5f), new Color(0.9f, 0.3f, 0.2f), 1.3f);
                ci.DrawCircle(new Vector2(-3.5f, 2.2f), 0.7f, new Color(0.9f, 0.3f, 0.2f));
                if (close) for (int k = 0; k < Math.Min(3, n.Items.Count + 1); k++) ci.DrawLine(new Vector2(-1.5f, -2.5f + k * 2.2f), new Vector2(3.5f, -2.5f + k * 2.2f), ink, 0.6f);
                bool read = n.For >= 0 ? n.Readers.Contains(n.For) : n.Readers.Count > 1;
                if (n.Pinged && !read)
                {
                    // 컴퓨터가 받을 사람에게 알렸다: 작은 단말 화면과 퍼지는 신호
                    ci.DrawRect(new Rect2(-9.5f, -7f, 4f, 3f), new Color(0.15f, 0.2f, 0.25f));
                    ci.DrawRect(new Rect2(-9f, -6.5f, 3f, 2f), new Color(0.35f, 0.9f, 1f));
                    float ph = (_time * 0.8f) % 1f;
                    ci.DrawArc(new Vector2(-7.5f, -5.5f), 2f + 4f * ph, -1.2f, 0.2f, 6, new Color(0.35f, 0.9f, 1f, 1f - ph), 0.6f, true);
                }
                if (read) ci.DrawPolyline(new[] { new Vector2(1.5f, 4f), new Vector2(2.6f, 5f), new Vector2(4.6f, 2.6f) }, new Color(0.3f, 0.85f, 0.4f), 0.9f, true);
                else ci.DrawCircle(new Vector2(4.5f, -5f), 1.2f + 0.4f * Mathf.Sin(_time * 4f), new Color(1f, 0.6f, 0.2f)); // 아직 아무도 안 읽었다
                break;
        }
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
