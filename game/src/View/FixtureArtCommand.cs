using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 그림 표 — 지휘 · 통신 · 안전 (7종).
/// 콘솔(화면 · 자판 · 예비 조타 손잡이) · 주 컴퓨터(서버 랙 둘 · 블레이드 손잡이 · 케이블 다발 · 표시등) · 장거리 센서(접시 · 급전 혼 · 회전 받침 · 스윕)
/// · 항법 컴퓨터(둥근 성도 유리 · 항로 호 · 반짝이는 별) · 신호 증폭기(야기 안테나 · 방열 핀 · 퍼지는 신호) · 비상등(노란 망 · 정전 때 도는 경광)
/// · 방화포 함(빨간 주머니 · 당김 끈 · 불이 나면 깜빡).
/// </summary>
public static partial class FixtureArt
{
    private static void CommandArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.Console] = new(ConsoleBody, ConsoleLife, ConsoleFine, Look.Flicker, 0.5f, 0.3f);
        t[FurnitureType.MainComputer] = new(ComputerBody, ComputerLife, ComputerFine, Look.Heat, 0.5f, 0.5f);
        t[FurnitureType.SensorArray] = new(SensorBody, SensorLife, SensorFine, Look.Sparks, 0.5f, 0.5f);
        t[FurnitureType.NavComputer] = new(NavBody, NavLife, NavFine, Look.Flicker, 0.5f, 0.5f);
        t[FurnitureType.SignalBooster] = new(BoosterBody, BoosterLife, BoosterFine, Look.Sparks, 0.75f, 0.3f);
        t[FurnitureType.EmergencyLight] = new(EmergencyBody, EmergencyLife, EmergencyFine, Look.Flicker, 0.5f, 0.45f);
        t[FurnitureType.FireBlanket] = new(BlanketBody, BlanketLife, BlanketFine, Look.Jam, 0.5f, 0.85f);
    }

    private static readonly Color ScreenGlass = new("#0b1f29");
    private static readonly Color RackLed = new("#5fd0c8");
    private static readonly Color Sweep = new("#6ee7b7");
    private static readonly Color Beacon = new("#ffb020");
    private static readonly Color FireRed = new("#c0302a");

    // ─────────────── 콘솔 ───────────────

    private static void ConsoleBody(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        Box(ci, x.B, new Color("#11161e"), 5, new Color("#2f3a4b"));
        Bevel(ci, x.B, 0.1f);
        var scr = new Rect2(x.C - f * 3.5f - new Vector2(8.5f, 8.5f) + f.Abs() * 3.5f, new Vector2(17f, 17f) - f.Abs() * 7f);
        Glass(ci, scr, ScreenGlass, 1.5f); // 화면 (쓰는 사람 반대쪽)
        ci.DrawRect(scr, new Color("#2b4c63"), false, 1f);
        var kb = x.C + f * 8f;
        var keys = new Rect2(kb - side.Abs() * 9f - f.Abs() * 2.5f, side.Abs() * 18f + f.Abs() * 5f);
        ci.DrawRect(keys, new Color("#1a2028")); // 자판
        for (int k = 0; k < 6; k++) Dot(ci, kb + side * (k - 2.5f) * 3f, 0.9f, new Color("#3a4454"));
        if (x.F.AuxHelm) // 예비 조타석: 조종간
        {
            var stick = x.C + f * 3f + side * 9f;
            Dot(ci, stick, 2.6f, new Color("#2a3240"));
            Dot(ci, stick, 1.4f, new Color("#c0392b"));
        }
        else Ring(ci, x.C + f * 3f + side * 9f, 2f, new Color("#3a4454"), 1f, 10); // 컵 받침
    }

    private static void ConsoleLife(in Fix x)
    {
        var ci = x.Ci;
        var f = x.Front;
        var side = new Vector2(-f.Y, f.X);
        var scr = new Rect2(x.C - f * 3.5f - new Vector2(7.5f, 7.5f) + f.Abs() * 3.5f, new Vector2(15f, 15f) - f.Abs() * 7f);
        if (!x.Lit) { ci.DrawRect(scr, new Color("#05080b")); return; }
        var acc = x.F.AuxHelm ? Amber : x.Accent;
        ci.DrawRect(scr, acc.WithAlpha((0.18f + 0.08f * Mathf.Sin(x.T * 2.1f)) * x.Glow));
        float sy = scr.Position.Y + Mathf.PosMod(x.T * 7f, scr.Size.Y);
        ci.DrawLine(new Vector2(scr.Position.X, sy), new Vector2(scr.End.X, sy), acc.WithAlpha(0.5f * x.Glow), 1f);
        for (int k = 0; k < 3; k++)
        {
            float bh = 1.5f + 3.5f * (0.5f + 0.5f * Mathf.Sin(x.T * (1.3f + k * 0.7f) + k));
            ci.DrawRect(new Rect2(scr.Position.X + 2 + k * 4, scr.End.Y - 1 - bh, 2.5f, bh), acc.WithAlpha(0.7f * x.Glow));
        }
        if (x.User != null && x.Lod > 0) // 누가 치고 있다: 자판이 반짝
        {
            int key = (int)(x.T * 9f) % 6;
            Dot(ci, x.C + f * 8f + side * (key - 2.5f) * 3f, 1f, acc.WithAlpha(0.9f));
        }
    }

    private static void ConsoleFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2f, 0.6f);
        if (x.F.AuxHelm) Tag(ci, x.C + x.Front * 3f, "예비 조타", 4, Amber.WithAlpha(0.6f));
    }

    // ─────────────── 주 컴퓨터 ───────────────

    private static void ComputerBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R;
        Box(ci, r.Grow(-3f), new Color("#12161e"), 5, new Color("#34405a"), 2);
        float half = r.Size.X * 0.5f;
        for (int k = 0; k < 2; k++)
        {
            var rack = new Rect2(r.Position.X + 6 + k * half, r.Position.Y + 6, half - 9, r.Size.Y - 12);
            ci.DrawRect(rack, new Color("#0c0f15"));
            for (int row = 0; row < 6; row++)
            {
                float y = rack.Position.Y + 5 + row * (rack.Size.Y - 8) / 5f;
                ci.DrawLine(new Vector2(rack.Position.X + 2, y), new Vector2(rack.End.X - 2, y), new Color("#232b3a"), 1f);
                ci.DrawLine(new Vector2(rack.End.X - 6, y - 3f), new Vector2(rack.End.X - 3, y - 3f), new Color("#4a5566"), 1.2f); // 블레이드 손잡이
            }
            for (int v = 0; v < 8; v++) Dot(ci, new Vector2(rack.End.X - 2.5f, rack.Position.Y + 3f + v * (rack.Size.Y - 6f) / 7f), 0.6f, new Color("#1a2230")); // 통풍 구멍
        }
        Cable(ci, new Vector2(r.Position.X + 10, r.Position.Y + 3), new Vector2(r.End.X - 10, r.Position.Y + 3), -3f, new Color("#3a5aa8").WithAlpha(0.7f), 1.6f); // 케이블 다발
        Cable(ci, new Vector2(r.Position.X + 12, r.Position.Y + 3), new Vector2(r.End.X - 8, r.Position.Y + 3), -2f, new Color("#e0b64a").WithAlpha(0.6f), 1.2f);
    }

    private static void ComputerLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var r = x.R;
        float half = r.Size.X * 0.5f;
        bool online = x.W.Automation.MainOnline;
        int rows = x.Lod == 0 ? 2 : 5;
        for (int k = 0; k < 2; k++)
            for (int row = 0; row < rows; row++)
                for (int led = 0; led < 3; led++)
                {
                    var p = new Vector2(r.Position.X + 10 + k * half + led * 5f, r.Position.Y + 11 + row * (r.Size.Y - 20) / 5f);
                    float blink = Hash(x.Id * 7 + k, row * 3 + led, 11);
                    bool on = online ? Mathf.Sin(x.T * (3f + 5f * blink) + blink * 9f) > -0.2f : led == 0 && row == 0 && Mathf.Sin(x.T * 4f) > 0f;
                    var col = online ? (blink > 0.8f ? new Color("#ffd166") : RackLed) : Danger;
                    if (on) Dot(ci, p, 1.4f, col.WithAlpha(0.85f));
                }
        if (online && x.Lod > 0) // 디스크 활동 (빠른 깜빡임)
            Dot(ci, new Vector2(r.Position.X + 8f, r.End.Y - 8f), 1f, Amber.WithAlpha(Hash(x.Id, (int)(x.T * 20f), 12) > 0.5f ? 0.9f : 0.1f));
        float heat = Mathf.Clamp((x.F.Room.Air.Temperature - 28f) / 12f, 0f, 1f);
        if (heat > 0.05f)
            for (int k = 0; k < 4; k++)
            {
                float ph = Mathf.PosMod(x.T * 0.7f + k * 0.25f, 1f);
                Dot(ci, new Vector2(r.Position.X + r.Size.X * (0.2f + 0.2f * k), r.Position.Y + r.Size.Y * (1f - ph)), 3f + 6f * ph, new Color("#ff8a4a").WithAlpha(0.18f * heat * (1f - ph)));
            }
    }

    private static void ComputerFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.R.Grow(-3f), 2.5f, 0.7f);
        Tag(ci, new Vector2(x.R.GetCenter().X, x.R.End.Y - 5f), "CORE", 5, RackLed.WithAlpha(0.5f));
    }

    // ─────────────── 장거리 센서 ───────────────

    private static void SensorBody(in Fix x)
    {
        var ci = x.Ci;
        var c0 = x.R.GetCenter();
        Box(ci, x.R.Grow(-3f), new Color("#141a24"), 5, new Color("#3a4a66"), 2);
        float rad = Mathf.Min(x.R.Size.X, x.R.Size.Y) * 0.36f;
        for (int k = 0; k < 16; k++) Dot(ci, c0 + Vector2.FromAngle(k * Mathf.Tau / 16f) * (rad + 3f), 0.8f, new Color("#3a4a66")); // 회전 받침 톱니
        Dot(ci, c0, rad, new Color("#0b1119"));
        ci.DrawArc(c0, rad, 0f, Mathf.Tau, 32, new Color("#4b5f80"), 2f, true); // 접시
        ci.DrawArc(c0, rad * 0.55f, 0f, Mathf.Tau, 24, new Color("#26344a"), 1f, true);
        for (int k = 0; k < 3; k++) Line(ci, c0 + Vector2.FromAngle(k * Mathf.Tau / 3f + 0.5f) * rad * 0.95f, c0, new Color("#3a4a66"), 1f); // 급전 혼 버팀대
        Dot(ci, c0, 2.2f, new Color("#8aa0c8")); // 급전 혼
        Pipe(ci, new Vector2(x.R.Position.X + 5f, x.R.End.Y - 6f), new Vector2(x.R.Position.X + 5f, x.R.Position.Y + 6f), 2f, new Color("#2a3446"), false); // 안테나 기둥
        Dot(ci, new Vector2(x.R.Position.X + 5f, x.R.Position.Y + 6f), 1.4f, new Color("#c0392b"));
    }

    private static void SensorLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var c0 = x.R.GetCenter();
        float rad = Mathf.Min(x.R.Size.X, x.R.Size.Y) * 0.36f;
        var sens = x.W.Sensors;
        if (sens.Online && !x.Dead)
        {
            float ang = x.T * 2.2f;
            for (int k = 0; k < (x.Lod == 0 ? 2 : 6); k++)
            {
                float a0 = ang - k * 0.12f;
                Line(ci, c0, c0 + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rad, Sweep.WithAlpha(0.5f * (1f - k / 6f) * sens.Quality), 2f);
            }
            foreach (var inc in sens.Incoming)
            {
                if (inc.Warned == WarnLevel.None) continue;
                var d = new Vector2(-inc.Direction.X, -inc.Direction.Y);
                float far = Mathf.Clamp(inc.MinutesLeft(x.W.Tick) / SensorSystem.ApproachMinutes, 0.15f, 1f);
                Dot(ci, c0 + d * rad * far, 2.2f, Danger.WithAlpha(0.6f + 0.4f * Mathf.Sin(x.T * 8f)));
            }
            Led(ci, new Vector2(x.R.Position.X + 5f, x.R.Position.Y + 6f), Danger, Mathf.PosMod(x.T, 1.5f) < 0.2f ? 1f : 0.2f, 1.2f); // 기둥 꼭대기 항공등
        }
        else if (Mathf.Sin(x.T * 3f) > 0f) Dot(ci, c0, 2.5f, Danger);
    }

    private static void SensorFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.R.Grow(-3f), 2.5f, 0.6f);
        float rad = Mathf.Min(x.R.Size.X, x.R.Size.Y) * 0.36f;
        for (int k = 0; k < 12; k++) Dot(ci, x.R.GetCenter() + Vector2.FromAngle(k * Mathf.Tau / 12f) * rad * 0.78f, 0.45f, new Color("#4b5f80"));
    }

    // ─────────────── 항법 컴퓨터 ───────────────

    private static void NavBody(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.46f;
        Box(ci, x.B, new Color("#10141c"), 6, new Color("#3a4a66"));
        Dot(ci, x.C, rr, new Color("#0a1424")); // 둥근 성도 유리
        Ring(ci, x.C, rr, new Color("#5a7aa8"), 1.2f, 28);
        for (int k = 1; k < 3; k++) Ring(ci, x.C, rr * k / 3f, new Color("#1f2e48"), 0.7f, 20);
        for (int k = 0; k < 6; k++) Line(ci, x.C, x.C + Vector2.FromAngle(k * Mathf.Pi / 3f) * rr, new Color("#1a2840"), 0.6f);
        ci.DrawArc(x.C, rr * 0.8f, 3.8f, 4.6f, 6, new Color(1, 1, 1, 0.12f), 1.2f, true); // 유리 반사
        var gy = new Vector2(x.B.End.X - 3.5f, x.B.Position.Y + 3.5f); // 자이로
        Ring(ci, gy, 2.2f, Brass, 0.8f, 10);
        Ring(ci, gy, 1.2f, Brass, 0.6f, 8);
    }

    private static void NavLife(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.46f;
        if (!x.Lit) return;
        for (int k = 0; k < (x.Lod == 0 ? 3 : 8); k++) // 반짝이는 별
        {
            var p = x.C + Vector2.FromAngle(Hash(x.Id, k, 101) * Mathf.Tau) * rr * (0.15f + 0.8f * Hash(x.Id, k, 102));
            Dot(ci, p, 0.6f, Colors.White.WithAlpha((0.3f + 0.6f * Pulse(x.T + k * 1.3f, 2.3f)) * x.Glow));
        }
        bool burn = x.W != null && (x.W.Propulsion.Burning || x.W.Propulsion.CourseBurnVisible);
        float a0 = -2.4f, a1 = -0.4f; // 항로 호
        ci.DrawArc(x.C + new Vector2(0f, rr * 0.4f), rr * 0.7f, a0, a1, 12, Amber.WithAlpha((burn ? 0.9f : 0.5f) * x.Glow), burn ? 1.4f : 0.9f, true);
        float ph = Mathf.PosMod(x.T * 0.05f * Mathf.Max(0.2f, x.Spin), 1f);
        var ship = x.C + new Vector2(0f, rr * 0.4f) + Vector2.FromAngle(Mathf.Lerp(a0, a1, ph)) * rr * 0.7f;
        Dot(ci, ship, burn ? 1.8f : 1.3f, Cyan.WithAlpha(x.Glow)); // 배 위치
        Dot(ci, x.C + new Vector2(0f, rr * 0.4f) + Vector2.FromAngle(a1) * rr * 0.7f, 1.2f, Good.WithAlpha(0.6f + 0.4f * Pulse(x.T, 3f))); // 목적지
        float gy = x.Ang(6f); // 도는 자이로
        Line(ci, new Vector2(x.B.End.X - 3.5f, x.B.Position.Y + 3.5f), new Vector2(x.B.End.X - 3.5f, x.B.Position.Y + 3.5f) + Vector2.FromAngle(gy) * 2f, Brass.Lightened(0.3f), 0.8f);
    }

    private static void NavFine(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.46f;
        for (int k = 0; k < 24; k++) Line(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / 24f) * rr * 0.92f, x.C + Vector2.FromAngle(k * Mathf.Tau / 24f) * rr, new Color("#5a7aa8").WithAlpha(0.6f), 0.5f); // 방위 눈금
        Tag(ci, x.C - new Vector2(0f, rr * 0.85f), "N", 4, new Color(1, 1, 1, 0.5f));
    }

    // ─────────────── 신호 증폭기 ───────────────

    private static void BoosterBody(in Fix x)
    {
        var ci = x.Ci;
        var bx = x.Q(0.05f, 0.45f, 0.5f, 0.95f);
        Box(ci, bx, new Color("#2a2e36"), 2, new Color("#6a7080")); // 본체
        for (int k = 0; k < 5; k++) Line(ci, new Vector2(bx.Position.X + 2f + k * (bx.Size.X - 4f) / 4f, bx.Position.Y + 1f), new Vector2(bx.Position.X + 2f + k * (bx.Size.X - 4f) / 4f, bx.End.Y - 1f), new Color("#1a1d22"), 1.2f); // 방열 핀
        var boom0 = x.P(0.4f, 0.55f);
        var boom1 = x.P(0.92f, 0.12f);
        Line(ci, boom0, boom1, Chrome, 1.4f); // 야기 붐
        var d = (boom1 - boom0).Normalized();
        var n = new Vector2(-d.Y, d.X);
        for (int k = 0; k < 5; k++) // 소자 (갈수록 짧다)
        {
            var p = boom0.Lerp(boom1, 0.2f + k * 0.2f);
            float len = 6f - k * 0.9f;
            Line(ci, p - n * len, p + n * len, Chrome.Darkened(0.15f), 1f);
        }
        var coil = x.P(0.75f, 0.8f); // 동축 케이블 감은 것
        for (int k = 1; k <= 3; k++) Ring(ci, coil, k * 1.3f, new Color("#1a1a1a"), 1f, 12);
        Line(ci, coil, x.P(0.5f, 0.7f), new Color("#1a1a1a"), 1f);
    }

    private static void BoosterLife(in Fix x)
    {
        var ci = x.Ci;
        var tip = x.P(0.92f, 0.12f);
        if (x.Lit)
            for (int k = 0; k < (x.Lod == 0 ? 1 : 3); k++) // 퍼지는 신호
            {
                float ph = Mathf.PosMod(x.T * 0.6f * x.Spin + k / 3f, 1f);
                ci.DrawArc(tip, 3f + ph * 12f, -1.6f, 0.3f, 8, Cyan.WithAlpha(0.5f * (1f - ph) * x.Glow), 1f, true);
            }
        var bx = x.Q(0.05f, 0.45f, 0.5f, 0.95f);
        for (int k = 0; k < 4; k++) // 신호 막대
        {
            bool on = x.Lit && (int)(x.T * 1.2f + Hash(x.Id, k, 103) * 3f) % 4 >= k;
            ci.DrawRect(new Rect2(bx.End.X + 1.5f + k * 2f, bx.End.Y - 2f - k * 1.5f, 1.4f, 2f + k * 1.5f), (on ? Good : new Color("#2a2e36")).WithAlpha(on ? x.Glow : 1f));
        }
    }

    private static void BoosterFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.05f, 0.45f, 0.5f, 0.95f), 1.5f, 0.45f);
        Tag(ci, x.P(0.27f, 0.35f), "dB", 4, new Color(1, 1, 1, 0.45f));
    }

    // ─────────────── 비상등 ───────────────

    private static void EmergencyBody(in Fix x)
    {
        var ci = x.Ci;
        ci.DrawRect(new Rect2(x.R.Position.X + 4f, x.R.Position.Y + 1f, x.R.Size.X - 8f, 3f), new Color("#3a4454")); // 벽 받침
        var pack = x.Q(0.1f, 0.62f, 0.9f, 0.95f);
        Box(ci, pack, new Color("#2a2e36"), 2, new Color("#6a7080")); // 배터리 상자
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.3f;
        var lamp = x.P(0.5f, 0.38f);
        Dot(ci, lamp, rr, new Color("#3a3018")); // 등 갓
        Ring(ci, lamp, rr, WarnYellow, 1.6f, 20);
        Line(ci, lamp - new Vector2(rr, 0f), lamp + new Vector2(rr, 0f), WarnYellow.Darkened(0.2f), 1f); // 노란 보호 망
        Line(ci, lamp - new Vector2(0f, rr), lamp + new Vector2(0f, rr), WarnYellow.Darkened(0.2f), 1f);
        Ring(ci, lamp, rr * 0.55f, WarnYellow.Darkened(0.2f), 0.8f, 14);
    }

    private static void EmergencyLife(in Fix x)
    {
        var ci = x.Ci;
        var room = x.F.Room;
        bool outage = !room.Powered || room.LightsOut; // 정전 · 조명 고장
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.3f;
        var lamp = x.P(0.5f, 0.38f);
        bool works = x.M == null || x.M.Faults.Count == 0;
        if (outage && works)
        {
            Dot(ci, lamp, rr * 0.8f, Beacon.WithAlpha(0.9f));
            Dot(ci, lamp, rr * 2.2f, Beacon.WithAlpha(0.15f));
            float a = x.T * 4.5f; // 도는 경광 빛
            for (int s = 0; s < 2; s++)
            {
                var d = Vector2.FromAngle(a + s * Mathf.Pi);
                var n = new Vector2(-d.Y, d.X);
                ci.DrawColoredPolygon(new[] { lamp, lamp + d * ShipView.T * 2.2f + n * 12f, lamp + d * ShipView.T * 2.2f - n * 12f }, Beacon.WithAlpha(x.Lod == 0 ? 0.08f : 0.13f));
            }
        }
        else
        {
            Dot(ci, lamp, rr * 0.4f, Beacon.WithAlpha(0.12f)); // 꺼져 있다 (대기)
            Led(ci, x.P(0.8f, 0.78f), Good, works ? 0.7f : 0f, 0.9f); // 충전 표시
        }
    }

    private static void EmergencyFine(in Fix x)
    {
        var ci = x.Ci;
        Bolt(ci, new Vector2(x.R.Position.X + 6f, x.R.Position.Y + 2.5f), 0.6f);
        Bolt(ci, new Vector2(x.R.End.X - 6f, x.R.Position.Y + 2.5f), 0.6f);
        Tag(ci, x.P(0.4f, 0.78f), "EXIT", 4, Good.WithAlpha(0.5f));
    }

    // ─────────────── 방화포 함 ───────────────

    private static void BlanketBody(in Fix x)
    {
        var ci = x.Ci;
        var pouch = x.Q(0.1f, 0.06f, 0.9f, 0.78f);
        ci.DrawRect(new Rect2(x.R.Position.X + 6f, x.R.Position.Y + 1f, x.R.Size.X - 12f, 2.5f), new Color("#3a4454")); // 벽 걸이
        Box(ci, pouch, FireRed, 4, FireRed.Lightened(0.3f)); // 빨간 주머니
        Line(ci, pouch.Position + new Vector2(2f, pouch.Size.Y * 0.28f), new Vector2(pouch.End.X - 2f, pouch.Position.Y + pouch.Size.Y * 0.28f), FireRed.Darkened(0.35f), 1.2f); // 덮개 접힌 선
        ci.DrawRect(new Rect2(pouch.GetCenter().X - 4f, pouch.Position.Y + pouch.Size.Y * 0.18f, 8f, 1.6f), new Color("#1a1a1a").WithAlpha(0.6f)); // 찍찍이
        Bevel(ci, pouch, 0.15f);
        var c = pouch.GetCenter();
        ci.DrawColoredPolygon(new[] { c + new Vector2(0f, -6f), c + new Vector2(3.5f, 1f), c + new Vector2(2f, 4f), c + new Vector2(-2f, 4f), c + new Vector2(-3.5f, 1f) }, Colors.White.WithAlpha(0.9f)); // 불꽃 표
        ci.DrawColoredPolygon(new[] { c + new Vector2(0f, -1.5f), c + new Vector2(1.6f, 2f), c + new Vector2(-1.6f, 2f) }, FireRed);
    }

    private static void BlanketLife(in Fix x)
    {
        var ci = x.Ci;
        bool fire = x.W != null && x.W.Fire.Count > 0 && x.W.Fire.IsKnown(x.F.Room); // 이 방에 불이 났다
        float sway = Mathf.Sin(x.T * 1.6f) * (fire ? 0.15f : 0.06f);
        for (int s = 0; s < 2; s++) // 당김 끈 둘
        {
            var top = x.P(s == 0 ? 0.3f : 0.7f, 0.78f);
            var end = top + Vector2.FromAngle(Mathf.Pi / 2f + sway * (s == 0 ? 1f : -1f)) * 6f;
            Line(ci, top, end, new Color("#1a1a1a"), 1.8f);
            ci.DrawRect(new Rect2(end - new Vector2(1.6f, 0f), new Vector2(3.2f, 2f)), fire ? Danger.WithAlpha(0.5f + 0.5f * Pulse(x.T, 8f)) : new Color("#2a2a2a"));
        }
        if (fire) Box(ci, x.Q(0.1f, 0.06f, 0.9f, 0.78f).Grow(1.5f), new Color(0, 0, 0, 0f), 5, Danger.WithAlpha(0.4f + 0.4f * Pulse(x.T, 6f)), 1);
    }

    private static void BlanketFine(in Fix x)
    {
        var ci = x.Ci;
        var pouch = x.Q(0.1f, 0.06f, 0.9f, 0.78f);
        Tag(ci, new Vector2(pouch.GetCenter().X, pouch.End.Y - 3f), "FIRE", 4, Colors.White.WithAlpha(0.7f));
        for (int k = 0; k < 8; k++) Dot(ci, new Vector2(pouch.Position.X + 2f + k * (pouch.Size.X - 4f) / 7f, pouch.Position.Y + 1.5f), 0.4f, new Color(1, 1, 1, 0.4f)); // 바느질
    }
}
