using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.20b 로봇 · 드론 그림 (단계 · 상태) · 함대 연결선.
///  로봇: 정비실에서 고쳐 단 단계가 몸에 보인다 — 개량형(앞뒤 범퍼 · 모서리 보강 · 볼트) · 강화형(옆 방열판 빗살 · 겹눈 센서) ·
///   최신형(등에 접힌 다관절 공구 팔 · 셀 띠가 숨 쉬듯 빛남 · 안테나).
///  드론: 종류마다 실루엣이 다르다 — 검사(둥근 몸 · 날개 고리 넷 · 렌즈와 훑는 빛) · 수리(네모 몸 · 집게 팔 둘 · 용접 불꽃) ·
///   견인(넓은 H 틀 · 윈치 드럼 · 갈고리) · 건설(육각 틀 · 트러스 무늬 · 집게) — 새 드론은 원형 몸에 특기 표식(안테나 · 접시 · 토치 · 방열 날개 · 범퍼 · 케이블 감개).
///   다친 곳이 보인다: 추진기(날개 하나가 꺾여 불꽃이 튐) · 팔(휘어짐) · 센서(렌즈 금) · 배터리(부풀어 주황으로 달아오름) · 부서짐(검게 탄 틀).
///  연결: 멈춘 로봇을 끄는 견인 고리와 줄 · 같이 드는 짐의 들것 · 고치는 로봇의 불꽃 · 주 컴퓨터가 보낸 명령선(흐르는 점선) · 불에 탄 로봇의 잔해.
///  그리기만 한다 (시뮬레이션 상태를 바꾸지 않는다).
/// </summary>
public partial class ShipView
{
    private static readonly Color FleetInk = new("#11151b");
    private static readonly Color FleetSteel = new("#9aa4b1");
    private static readonly Color FleetBolt = new("#d8dee6");

    // ─────────────── 로봇 단계 (로봇 몸 좌표 · 앞이 +x) ───────────────

    private void PaintRobotTier(CanvasItem ci, Robot r, Color col, bool dead, float t)
    {
        int tier = _world.Fleet.Tier;
        if (tier < 2) return;
        var steel = dead ? FleetSteel.Darkened(0.5f) : FleetSteel;
        // 개량형: 앞뒤 범퍼 · 모서리 보강 · 볼트
        ci.Box(new Rect2(9.2f, -6.5f, 1.8f, 13f), steel.Darkened(0.2f));
        ci.Box(new Rect2(-11f, -6.5f, 1.8f, 13f), steel.Darkened(0.2f));
        foreach (var (x, y) in new[] { (-9.5f, -8f), (7.5f, -8f), (-9.5f, 6f), (7.5f, 6f) })
        {
            ci.DrawLine(new Vector2(x, y + (y < 0 ? 0f : 2f)), new Vector2(x + 2f, y + (y < 0 ? 0f : 2f)), steel, 1.2f, true);
            ci.Circle(new Vector2(x + 1f, y + 1f), 0.6f, FleetBolt, true, -1f, true);
        }
        if (tier < 3) return;
        // 강화형: 옆 방열판 (빗살) · 겹눈 센서
        foreach (float side in new[] { -1f, 1f })
        {
            var plate = new Rect2(-6f, side < 0 ? -10.6f : 8.4f, 11f, 2.2f);
            ci.Box(plate, new Color("#5d6672"));
            for (float x = plate.Position.X + 1f; x < plate.End.X; x += 2.2f)
                ci.DrawLine(new Vector2(x, plate.Position.Y), new Vector2(x + 1.2f, plate.End.Y), new Color("#c9d1da").WithAlpha(0.55f), 0.8f, true);
        }
        var eye2 = dead ? new Color("#3a3f48") : new Color("#ffd27d").WithAlpha(0.6f + 0.4f * Mathf.Sin(t * 3f));
        ci.Circle(new Vector2(7.2f, -3f), 0.9f, eye2, true, -1f, true);
        ci.Circle(new Vector2(7.2f, 3f), 0.9f, eye2, true, -1f, true);
        if (tier < 4) return;
        // 최신형: 등에 접힌 다관절 공구 팔 · 숨 쉬는 셀 띠 · 안테나
        var j0 = new Vector2(-6f, -2f);
        var j1 = j0 + new Vector2(5f, -3.5f + 0.6f * Mathf.Sin(t * 1.3f));
        var j2 = j1 + new Vector2(4.5f, 2.5f);
        ci.DrawLine(j0, j1, steel.Lightened(0.1f), 1.6f, true);
        ci.DrawLine(j1, j2, steel.Lightened(0.1f), 1.3f, true);
        foreach (var j in new[] { j0, j1, j2 }) ci.Circle(j, 1f, FleetInk, true, -1f, true);
        ci.DrawLine(j2, j2 + new Vector2(1.6f, -1f), FleetBolt, 0.9f, true);
        ci.DrawLine(j2, j2 + new Vector2(1.6f, 1f), FleetBolt, 0.9f, true);
        float glow = dead ? 0.15f : 0.45f + 0.35f * Mathf.Sin(t * 2.2f);
        ci.Box(new Rect2(-8.5f, 4.2f, 12f, 1.3f), new Color("#6ff0c8").WithAlpha(glow));
        ci.DrawLine(new Vector2(-8f, 0f), new Vector2(-12.5f, -2.5f), steel, 0.8f, true);
        ci.Circle(new Vector2(-12.5f, -2.5f), 0.8f, dead ? FleetSteel : Palette.Good.WithAlpha(0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 4f))), true, -1f, true);
    }

    // ─────────────── 로봇끼리 · 명령선 · 잔해 (세계 좌표) ───────────────

    private void PaintFleetLinks(CanvasItem ci)
    {
        var w = _world;
        foreach (var r in w.Robots.Robots)
        {
            var p = RobotPx(r);
            float t = _time + r.Id * 1.37f;
            // 견인: 고리 막대 + 늘어진 줄
            if (r.Hauling is Robot b)
            {
                var q = RobotPx(b);
                var mid = (p + q) * 0.5f + new Vector2(0f, 2.5f + Mathf.Sin(t * 3f));
                ci.Polyline(new[] { p, p.Lerp(mid, 0.6f), mid, q.Lerp(mid, 0.6f), q }, TetherColor.WithAlpha(0.85f), 1.6f, true);
                ci.Arc(q, 4.5f, 0f, Mathf.Tau, 12, TetherColor.WithAlpha(0.7f), 1f, true);
            }
            // 같이 들기: 두 로봇 사이 들것 + 짐
            if (r.Partner is Robot pr && r.Cargo is ItemStack cargo && pr.Partner == r)
            {
                var q = RobotPx(pr);
                ci.DrawLine(p, q, FleetSteel.WithAlpha(0.9f), 3f, true);
                ci.DrawLine(p, q, FleetInk.WithAlpha(0.6f), 1f, true);
                var box = Palette.Item(cargo.Kind);
                var c = (p + q) * 0.5f;
                ci.Box(new Rect2(c - new Vector2(4f, 3f), new Vector2(8f, 6f)), box);
                ci.Box(new Rect2(c - new Vector2(4f, 3f), new Vector2(8f, 1.4f)), box.Lightened(0.3f));
            }
            // 고치기: 점선 + 불꽃
            if (r.Fixing is Robot fx && r.Path == null && r.State == RobotState.Active)
            {
                var q = RobotPx(fx);
                Dashed(ci, p, q, new Color("#ffcf7a").WithAlpha(0.5f), 1f, 3f, t * 6f);
                for (int k = 0; k < 4; k++)
                {
                    float a = t * 11f + k * 1.7f;
                    var s = q + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2f + 4f * ((t * 3f + k * 0.3f) % 1f));
                    ci.DrawLine(q, s, new Color("#fff1c2").WithAlpha(0.7f), 0.8f, true);
                }
            }
            // 불에 탄 잔해 (반나절 동안 그 자리에)
            if (r.Wrecked && r.Marks.Count > 0 && w.Tick - r.Marks[^1].Tick < SimTime.Hours(12)) PaintRobotWreck(ci, r, p, t);
        }
        PaintCommandLines(ci);
    }

    private void PaintRobotWreck(CanvasItem ci, Robot r, Vector2 p, float t)
    {
        float ang = Mathf.Atan2(r.Facing.Y, r.Facing.X) + 0.4f;
        ci.DrawSetTransform(p, ang, Vector2.One);
        var char0 = new Color("#2a2522");
        ci.Poly(new[] { new Vector2(-9, -6), new Vector2(-2, -8), new Vector2(8, -5), new Vector2(9, 4), new Vector2(1, 7), new Vector2(-8, 5) }, char0);
        ci.Polyline(new[] { new Vector2(-6, -4), new Vector2(-1, 1), new Vector2(5, -3) }, new Color("#4a403a"), 1f, true);
        ci.DrawLine(new Vector2(3, 3), new Vector2(11, 8), new Color("#3a3a3a"), 1.4f, true); // 떨어져 나간 팔
        ci.Circle(new Vector2(-3, 2), 1.4f, new Color("#ff7a3a").WithAlpha(0.3f + 0.3f * Mathf.Sin(t * 5f)), true, -1f, true); // 아직 남은 불씨
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        for (int k = 0; k < 2; k++)
        {
            float ph = (t * 0.35f + k * 0.5f) % 1f;
            ci.Circle(p + new Vector2(2f * Mathf.Sin(ph * 5f + k), -4f - 14f * ph), 1.5f + 3f * ph, new Color(0.3f, 0.3f, 0.32f, 0.35f * (1f - ph)), true, -1f, true);
        }
    }

    /// <summary>주 컴퓨터가 로봇 · 드론에 보낸 지시: 함교 계산기에서 흐르는 점선 (우선순위가 높을수록 굵고 붉다).</summary>
    private void PaintCommandLines(CanvasItem ci)
    {
        var w = _world;
        var cmd = w.Automation.Command;
        if (!w.Automation.Present || cmd.Lines.Count == 0) return;
        var core = w.Ship.FurnitureOf(FurnitureType.MainComputer).FirstOrDefault(f => !f.Room.Detached);
        if (core == null) return;
        var from = FurnitureRect(core).GetCenter();
        int drawn = 0;
        for (int i = cmd.Lines.Count - 1; i >= 0 && drawn < 8; i--)
        {
            var o = cmd.Lines[i];
            if (!o.Open || o.Target is not (CmdTarget.Robot or CmdTarget.Drone)) continue;
            Vector2 to;
            if (o.Target == CmdTarget.Robot && o.TargetId >= 0 && o.TargetId < w.Robots.Robots.Count) to = RobotPx(w.Robots.Robots[o.TargetId]);
            else if (o.Target == CmdTarget.Drone && o.TargetId >= 0 && o.TargetId < w.Drones.Drones.Count) to = Px(w.Drones.Drones[o.TargetId].Position);
            else continue;
            drawn++;
            var col = (o.Priority >= 0.9f ? Palette.Danger : o.Priority >= 0.6f ? Palette.Warning : Palette.Accent).WithAlpha(0.45f);
            Dashed(ci, from, to, col, o.Priority >= 0.9f ? 1.6f : 1.1f, 5f, -_time * 14f);
            ci.Arc(to, 9f + 1.5f * Mathf.Sin(_time * 4f + i), 0f, Mathf.Tau, 18, col, 1f, true);
        }
    }

    private static void Dashed(CanvasItem ci, Vector2 a, Vector2 b, Color col, float width, float dash, float phase)
    {
        float len = a.DistanceTo(b);
        if (len < 1f) return;
        var dir = (b - a) / len;
        float step = dash * 2f;
        for (float s = ((phase % step) + step) % step - step; s < len; s += step)
        {
            float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(len, s + dash);
            if (s1 > s0) ci.DrawLine(a + dir * s0, a + dir * s1, col, width, true);
        }
    }

    // ─────────────── 드론 (종류마다 다른 몸 · 다친 곳 · 단계) ───────────────

    private void PaintDroneArt(CanvasItem ci, Drone d, Vector2 p, Color col, bool docked)
    {
        float t = _time + d.Id * 0.91f;
        bool adrift = d.State == DroneState.Adrift;
        float ang = adrift ? Mathf.DegToRad(d.Hurt.Spin) + _time * (d.Hurt.Tumbling ? 3f : 0.6f) : Mathf.Atan2(d.Facing.Y, d.Facing.X);
        float s = docked ? 0.72f : 1f;
        bool dead = d.Wrecked;
        bool moving = !docked && d.State is DroneState.Outbound or DroneState.Returning or DroneState.Towing;
        bool working = d.State == DroneState.Working;
        var body = dead ? new Color("#3b3836") : docked ? col.Darkened(0.35f) : col.Darkened(0.12f);
        ci.DrawSetTransform(p, ang, new Vector2(s, s));
        switch (RobotsV15.Base(d.Kind))
        {
            case DroneKind.Inspect: PaintInspectDrone(ci, d, body, col, moving, working, dead, t); break;
            case DroneKind.Repair: PaintRepairDrone(ci, d, body, col, moving, working, dead, t); break;
            case DroneKind.Tow: PaintTowDrone(ci, d, body, col, moving, working, dead, t); break;
            default: PaintBuildDrone(ci, d, body, col, moving, working, dead, t); break;
        }
        if (RobotsV15.Flyer(d.Kind) != null) PaintDroneBadge(ci, d, col, t);
        PaintDroneTier(ci, dead, t);
        PaintDroneHurt(ci, d, t);
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>날개 고리 (돌면 흐릿한 원 · 꺾이면 반쪽).</summary>
    private static void Rotor(CanvasItem ci, Vector2 c, float r, Color col, bool spin, bool broken, float t)
    {
        ci.Circle(c, r, FleetInk.WithAlpha(0.85f), true, -1f, true);
        if (broken)
        {
            ci.DrawLine(c, c + new Vector2(r * 0.9f, r * 0.4f), FleetSteel.Darkened(0.4f), 1f, true);
            return;
        }
        if (spin) ci.Arc(c, r * 0.8f, t * 20f, t * 20f + 2.4f, 8, col.WithAlpha(0.6f), 1f, true);
        else ci.DrawLine(c - new Vector2(r * 0.8f, 0), c + new Vector2(r * 0.8f, 0), col.WithAlpha(0.7f), 0.9f, true);
        ci.Arc(c, r, 0f, Mathf.Tau, 12, col.Lightened(0.2f).WithAlpha(0.8f), 0.8f, true);
    }

    // 검사 드론: 둥근 몸 · 날개 고리 넷 · 앞 렌즈 · 일할 때 훑는 빛
    private static void PaintInspectDrone(CanvasItem ci, Drone d, Color body, Color col, bool moving, bool working, bool dead, float t)
    {
        bool thr = d.Hurt.Thruster < 0.6f;
        for (int k = 0; k < 4; k++)
        {
            var c = Vector2.FromAngle(Mathf.Pi / 4f + k * Mathf.Pi / 2f) * 6.5f;
            ci.DrawLine(Vector2.Zero, c, FleetSteel.Darkened(0.3f), 1.1f, true);
            Rotor(ci, c, 2.8f, col, moving || working, dead || thr && k == 1, t + k);
        }
        ci.Circle(Vector2.Zero, 4.2f, FleetInk, true, -1f, true);
        ci.Circle(Vector2.Zero, 3.5f, body, true, -1f, true);
        ci.Circle(new Vector2(-0.8f, -0.9f), 1.2f, body.Lightened(0.35f), true, -1f, true);
        // 렌즈
        ci.Circle(new Vector2(4.2f, 0f), 1.6f, FleetInk, true, -1f, true);
        ci.Circle(new Vector2(4.4f, 0f), 0.9f, dead ? FleetSteel.Darkened(0.5f) : new Color("#7de8ff"), true, -1f, true);
        if (working && !dead)
        {
            float sw = Mathf.Sin(t * 2.5f) * 0.5f;
            ci.Poly(new[] { new Vector2(4.6f, 0f), new Vector2(14f, -4f + sw * 6f), new Vector2(14f, 4f + sw * 6f) }, new Color("#7de8ff").WithAlpha(0.18f));
        }
    }

    // 수리 드론: 네모 몸 · 집게 팔 둘 · 용접 불꽃 (일할 때)
    private static void PaintRepairDrone(CanvasItem ci, Drone d, Color body, Color col, bool moving, bool working, bool dead, float t)
    {
        bool thr = d.Hurt.Thruster < 0.6f;
        foreach (var (x, y, k) in new[] { (-5.5f, -5.5f, 0), (-5.5f, 5.5f, 1) })
            Rotor(ci, new Vector2(x, y), 2.5f, col, moving || working, dead || thr && k == 0, t + k);
        Gfx.RoundRect(ci, new Rect2(-5f, -4f, 9f, 8f), body, 1.5f, FleetInk);
        ci.Box(new Rect2(-3.5f, -2.5f, 3f, 5f), FleetInk.WithAlpha(0.6f)); // 공구 칸
        ci.Box(new Rect2(-3f, -2f, 2f, 1.2f), col.Lightened(0.3f));
        // 팔 둘 (팔을 다치면 한쪽이 휜다)
        bool bent = d.Hurt.Arm < 0.7f;
        float wig = working && !dead ? Mathf.Sin(t * 9f) * 0.8f : 0f;
        var a0 = new Vector2(4f, -2.5f); var a1 = a0 + new Vector2(3.5f, -1.5f + wig); var a2 = a1 + new Vector2(2.2f, bent ? -2.5f : 1f);
        var b0 = new Vector2(4f, 2.5f); var b1 = b0 + new Vector2(3.5f, 1.5f - wig); var b2 = b1 + new Vector2(2.2f, -1f);
        foreach (var (u, v) in new[] { (a0, a1), (a1, a2), (b0, b1), (b1, b2) }) ci.DrawLine(u, v, FleetSteel, 1.1f, true);
        ci.Circle(a1, 0.7f, FleetBolt, true, -1f, true);
        ci.Circle(b1, 0.7f, FleetBolt, true, -1f, true);
        if (working && !dead)
        {
            var tip = (a2 + b2) * 0.5f + new Vector2(1f, 0f);
            float fl = 0.5f + 0.5f * Mathf.Sin(t * 31f);
            ci.Circle(tip, 1.4f + fl, new Color("#fff1c2").WithAlpha(0.85f), true, -1f, true);
            for (int k = 0; k < 3; k++)
            {
                float a = t * 13f + k * 2.1f;
                ci.DrawLine(tip, tip + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2f + 3f * fl), new Color("#ffcf7a").WithAlpha(0.8f), 0.7f, true);
            }
        }
    }

    // 견인 드론: 넓은 H 틀 · 윈치 드럼 · 뒤로 늘어진 갈고리
    private static void PaintTowDrone(CanvasItem ci, Drone d, Color body, Color col, bool moving, bool working, bool dead, float t)
    {
        bool thr = d.Hurt.Thruster < 0.6f;
        foreach (var (x, y, k) in new[] { (-5f, -7f, 0), (4f, -7f, 1), (-5f, 7f, 2), (4f, 7f, 3) })
            Rotor(ci, new Vector2(x, y), 2.3f, col, moving || working, dead || thr && k == 2, t + k);
        ci.Box(new Rect2(-6f, -7f, 2.2f, 14f), body);
        ci.Box(new Rect2(3f, -7f, 2.2f, 14f), body);
        ci.Box(new Rect2(-4f, -1.6f, 7.2f, 3.2f), body.Darkened(0.15f));
        // 윈치 드럼 (끌 때 감긴다)
        ci.Circle(new Vector2(-0.4f, 0f), 2.2f, FleetInk, true, -1f, true);
        float rot = d.State == DroneState.Towing ? t * 6f : 0f;
        for (int k = 0; k < 3; k++) ci.DrawLine(new Vector2(-0.4f, 0f), new Vector2(-0.4f, 0f) + Vector2.FromAngle(rot + k * 2.09f) * 1.8f, FleetSteel, 0.7f, true);
        // 갈고리
        if (d.Towing == null && d.Fetching == null)
        {
            ci.DrawLine(new Vector2(-6f, 0f), new Vector2(-9f, 0f), TetherColor.WithAlpha(0.8f), 0.9f, true);
            ci.Arc(new Vector2(-9.8f, 0.6f), 1.1f, -Mathf.Pi * 0.5f, Mathf.Pi, 8, FleetBolt, 0.9f, true);
        }
        ci.Box(new Rect2(3.3f, -6.5f, 1.4f, 1.2f), new Color("#ffb347").WithAlpha(moving ? 0.5f + 0.5f * Mathf.Sin(t * 8f) : 0.4f)); // 경광등
    }

    // 건설 드론: 육각 틀 · 트러스 무늬 · 앞 집게 둘
    private static void PaintBuildDrone(CanvasItem ci, Drone d, Color body, Color col, bool moving, bool working, bool dead, float t)
    {
        bool thr = d.Hurt.Thruster < 0.6f;
        var hex = new Vector2[6];
        for (int k = 0; k < 6; k++) hex[k] = Vector2.FromAngle(k * Mathf.Pi / 3f) * 6.2f;
        for (int k = 0; k < 6; k += 2) Rotor(ci, hex[k] * 1.25f, 2.1f, col, moving || working, dead || thr && k == 2, t + k);
        ci.Poly(hex, FleetInk);
        var inner = hex.Select(v => v * 0.82f).ToArray();
        ci.Poly(inner, body);
        for (int k = 0; k < 6; k++) ci.DrawLine(inner[k], inner[(k + 2) % 6], body.Lightened(0.25f).WithAlpha(0.7f), 0.7f, true); // 트러스
        ci.Circle(Vector2.Zero, 1.3f, FleetInk, true, -1f, true);
        float open = working && !dead ? 0.5f + 0.5f * Mathf.Sin(t * 4f) : 0.2f;
        foreach (float side in new[] { -1f, 1f })
        {
            var root = new Vector2(5f, side * 2f);
            var tip = root + new Vector2(3.6f, side * (1f + open));
            ci.DrawLine(root, tip, FleetSteel, 1.2f, true);
            ci.DrawLine(tip, tip + new Vector2(1f, -side * 1.2f), FleetBolt, 0.9f, true);
        }
    }

    /// <summary>새 드론의 특기 표식 (원형 몸 위).</summary>
    private static void PaintDroneBadge(CanvasItem ci, Drone d, Color col, float t)
    {
        var hi = col.Lightened(0.3f);
        switch (d.Kind)
        {
            case DroneKind.Scout: // 긴 안테나 + 깜빡이
                ci.DrawLine(new Vector2(-2f, 0f), new Vector2(-9f, -4f), FleetSteel, 0.7f, true);
                ci.Circle(new Vector2(-9f, -4f), 0.9f, hi.WithAlpha(0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 5f))), true, -1f, true);
                break;
            case DroneKind.Surveyor: // 접시
                ci.Arc(new Vector2(-1f, 0f), 2.6f, -1.2f, 1.2f, 8, hi, 1.1f, true);
                ci.DrawLine(new Vector2(-1f, 0f), new Vector2(1.6f, 0f), hi, 0.7f, true);
                break;
            case DroneKind.Welder: // 토치 (등)
                ci.Box(new Rect2(-3.5f, -0.8f, 4f, 1.6f), hi);
                ci.Circle(new Vector2(0.8f, 0f), 0.9f, new Color("#ffb347"), true, -1f, true);
                break;
            case DroneKind.Radiator: // 방열 날개
                for (int k = -1; k <= 1; k++) ci.DrawLine(new Vector2(-3f, k * 1.6f), new Vector2(-8f, k * 2.6f), hi, 0.9f, true);
                break;
            case DroneKind.Tug: // 두꺼운 범퍼
                ci.Box(new Rect2(6.5f, -4f, 1.6f, 8f), hi);
                break;
            case DroneKind.Rigger: // 케이블 감개
                ci.Arc(new Vector2(-1.5f, 0f), 1.8f, 0f, Mathf.Tau, 10, hi, 1f, true);
                ci.Arc(new Vector2(-1.5f, 0f), 0.9f, 0f, Mathf.Tau, 8, hi, 0.8f, true);
                break;
        }
    }

    /// <summary>단계: 개량형 외피 고리 · 강화형 방사선 판 · 최신형 보조 추진기.</summary>
    private void PaintDroneTier(CanvasItem ci, bool dead, float t)
    {
        int tier = _world.Fleet.Tier;
        if (tier < 2) return;
        var steel = dead ? FleetSteel.Darkened(0.5f) : FleetSteel;
        ci.Arc(Vector2.Zero, 5.4f, -0.5f, 0.5f, 6, steel, 1.2f, true);
        ci.Arc(Vector2.Zero, 5.4f, Mathf.Pi - 0.5f, Mathf.Pi + 0.5f, 6, steel, 1.2f, true);
        if (tier < 3) return;
        ci.Box(new Rect2(-2.5f, -5.6f, 5f, 1.2f), new Color("#c8b25a").WithAlpha(dead ? 0.3f : 0.85f));
        ci.Box(new Rect2(-2.5f, 4.4f, 5f, 1.2f), new Color("#c8b25a").WithAlpha(dead ? 0.3f : 0.85f));
        if (tier < 4) return;
        foreach (float side in new[] { -1f, 1f })
        {
            var pod = new Vector2(-7.5f, side * 3f);
            ci.Circle(pod, 1.3f, steel, true, -1f, true);
            if (!dead) ci.DrawLine(pod + new Vector2(-1.2f, 0f), pod + new Vector2(-3f - Mathf.Abs(Mathf.Sin(t * 9f + side)) * 2f, 0f), new Color("#8fd8ff").WithAlpha(0.6f), 1f, true);
        }
    }

    /// <summary>다친 곳: 추진기 불꽃 · 센서 금 · 부풀어 달아오른 배터리 · 부서진 틀.</summary>
    private static void PaintDroneHurt(CanvasItem ci, Drone d, float t)
    {
        var h = d.Hurt;
        if (d.Wrecked)
        {
            ci.DrawLine(new Vector2(-5f, -4f), new Vector2(4f, 5f), new Color("#1b1918"), 1.6f, true);
            ci.DrawLine(new Vector2(-4f, 4f), new Vector2(3f, -3f), new Color("#1b1918"), 1.2f, true);
            return;
        }
        if (h.Thruster < 0.95f)
        {
            float ph = (t * 4f) % 1f;
            ci.Circle(new Vector2(-6f - 4f * ph, 4f + 2f * ph), 1f + ph, new Color("#ff9a5c").WithAlpha(0.7f * (1f - ph) * (1f - h.Thruster)), true, -1f, true);
        }
        if (h.Sensor < 0.95f) ci.Polyline(new[] { new Vector2(3.2f, -1.4f), new Vector2(4.4f, -0.2f), new Vector2(3.6f, 0.6f), new Vector2(4.8f, 1.6f) }, new Color("#e8eef4").WithAlpha(0.8f), 0.6f, true);
        if (h.Swell > 0.05f)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * (4f + 10f * h.Swell));
            ci.Circle(new Vector2(-1.5f, 0f), 2f + 2.5f * h.Swell, new Color("#ff8a2a").WithAlpha(0.25f + 0.5f * h.Swell * pulse), true, -1f, true);
        }
        if (h.Arm < 0.95f) ci.DrawLine(new Vector2(4f, 3f), new Vector2(6.5f, 5.5f), new Color("#e8584a").WithAlpha(0.7f), 0.8f, true);
    }
}
