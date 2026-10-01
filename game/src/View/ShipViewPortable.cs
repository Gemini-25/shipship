using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.7 이동식 장비 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다).
// 장비마다 실루엣 · 무늬 · 움직임이 다르다:
//   작업등(세 다리 삼각대 · 겨누는 쪽으로 사다리꼴 머리 · 격자 · 렌즈 빛 원뿔 — 몸에 가리면 그림자 쐐기) ·
//   히터(낮은 상자 · 세로 틈 사이 붉은 지그재그 열선이 숨 쉬듯 · 위로 일렁이는 열기 · 먼지 타는 잿빛 실) ·
//   선풍기(둥근 망 · 세 날개가 돈다 · 바람 줄) · 공기청정기(키 큰 기둥 · 촘촘한 필터 격자 · 푸른 띠가 숨 쉬고 먼지가 빨려 든다) ·
//   이동식 배터리(두툼한 상자 · 단자 둘 · 잔량 다섯 칸 · 충전 중엔 칸이 차오른다) ·
//   양수기(냉각 핀 두른 원통 모터 · 돌면 떤다 · 굵은 호스 속을 물 토막이 흘러 배수구에서 튄다) ·
//   카트(널판 짐받이 · 바퀴 넷 — 밀면 바퀴살이 돈다 · 실린 짐 · 통로에 서면 노랑·검정 띠).
// 확대하면 손잡이 · 계기 · 볼트 · 표시등이 보인다. 케이블은 처지는 선(젖으면 푸른 물빛 · 과부하면 플러그가 달아오른다).
// 고장: 금 간 선 · 연기 · 가끔 불꽃. 꺼짐: 빛이 없다. 창고: 충전 받침 위에 가지런히(충전 중 표시등).
// 광원은 Core의 공개 속성(LightPos · LightRadius · LightIntensity · LightColor · Aim)을 그대로 쓴다 — 다음 단계 2D 조명이 같은 값을 읽는다.
public partial class ShipView
{
    private static readonly Color PtSteel = new("#8b96a6"), PtSteelDark = new("#454e5c"), PtInk = new("#14171d"), PtBolt = new("#c9d1dc"),
        PtLampBody = new("#e0b23a"), PtLampLens = new("#fff1c9"), PtHeaterBody = new("#5c2f27"), PtHeaterRim = new("#93503d"), PtCoil = new("#ff5424"),
        PtFanCage = new("#b7c2cf"), PtFanBlade = new("#6f8fb0"), PtPuriBody = new("#dbe1e9"), PtPuriGrid = new("#90a1b3"), PtPuriGlow = new("#59d0ff"),
        PtBatBody = new("#2e3b31"), PtBatRim = new("#6a8a68"), PtPumpBody = new("#2f6d8f"), PtPumpFin = new("#1d4a63"), PtHose = new("#2a3442"), PtWater = new("#8fd3ff"),
        PtCartBed = new("#4b5c70"), PtCartPlank = new("#6c7f95"), PtWheel = new("#1a1d24"), PtHazardY = new("#f2c230"), PtCable = new("#17191e"), PtCableHi = new("#3d434f");

    /// <summary>동적 층: 작업등 빛 웅덩이(천장 불 없는 방) · 케이블 · 호스 · 장비.</summary>
    private void PaintPortable(CanvasItem ci)
    {
        var ps = _world.Portable;
        if (ps.Devices.Count == 0) return;
        bool fine = Zoom > 1.15f;
        PaintPortableDark(ci);
        foreach (var d in ps.Devices) if (d.Placed) { PaintCable(ci, d); if (d.Kind == PortableKind.Pump) PaintHose(ci, d, fine); }
        // 카트 먼저 (실린 짐이 위에)
        foreach (var d in ps.Devices) if (!d.Lost && d.Kind == PortableKind.Cart) PaintDevice(ci, d, fine);
        foreach (var d in ps.Devices) if (!d.Lost && d.Kind != PortableKind.Cart) PaintDevice(ci, d, fine);
        foreach (var d in ps.Devices) if (d is { Kind: PortableKind.WorkLamp, Shadowed: true, User: CrewMember u } && d.Placed) PaintLampShadow(ci, d, u);
    }

    /// <summary>천장 불이 나간 방에 이동식 등만 켜져 있으면: 등에서 멀수록 어둡다 (작업등은 겨눈 쪽이 밝다).</summary>
    private void PaintPortableDark(CanvasItem ci)
    {
        var w = _world;
        var lamps = new List<PortableDevice>();
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.PortableLit == 0 || !PortableSystem.Unlit(room)) continue;
            lamps.Clear();
            foreach (var d in w.Portable.Devices)
                if (d is { Kind: PortableKind.WorkLamp, Running: true } && d.Placed && w.Portable.RoomOf(d) == room) lamps.Add(d);
            if (lamps.Count == 0) continue;
            foreach (var c in room.Cells)
            {
                float best = 1f;
                foreach (var l in lamps)
                {
                    var to = new System.Numerics.Vector2(c.X + 0.5f, c.Y + 0.5f) - l.LightPos;
                    float dist = to.Length(), r = MathF.Max(0.5f, l.LightRadius);
                    if (l.Directional && dist > 0.6f)
                    {
                        var aim = System.Numerics.Vector2.Normalize(l.Aim - l.LightPos + new System.Numerics.Vector2(1e-4f, 0f));
                        float cos = System.Numerics.Vector2.Dot(to / dist, aim);
                        dist *= cos > 0.55f ? 0.8f : cos > 0f ? 1.25f : 1.7f; // 겨눈 쪽은 멀리까지 · 등 뒤는 금방 어둡다
                    }
                    best = MathF.Min(best, Mathf.Clamp((dist - 0.3f * r) / (0.7f * r), 0f, 1f));
                }
                if (best > 0.02f) ci.DrawRect(CellRect(c), new Color(0, 0, 0, (room.Powered ? 0.36f : 0.42f) * best));
            }
        }
    }

    /// <summary>작업등이 몸에 가렸다: 등 → 사람 → 작업 위치로 드리운 그림자 쐐기.</summary>
    private void PaintLampShadow(CanvasItem ci, PortableDevice d, CrewMember u)
    {
        var lamp = ToPx(d.LightPos);
        var body = CrewPx(u);
        var dir = (body - lamp).Normalized();
        var side = new Vector2(-dir.Y, dir.X) * CrewRadius * 0.9f;
        float len = Mathf.Max(T * 1.1f, (ToPx(d.Aim) - body).Length() + T * 0.4f);
        var far = body + dir * len;
        ci.DrawPolygon(new[] { body + side, body - side, far - side * 1.8f, far + side * 1.8f },
            new[] { new Color(0, 0, 0, 0.42f), new Color(0, 0, 0, 0.42f), new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0f) });
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5f);
        ci.DrawArc(ToPx(d.Aim), 5f + 2f * pulse, 0f, Mathf.Tau, 14, new Color(1f, 0.8f, 0.4f, 0.35f + 0.3f * pulse), 1.2f, true);
    }

    // ───────────────────────── 케이블 · 호스 ─────────────────────────

    private static Vector2[] Sag(Vector2 a, Vector2 b, float sag, int n)
    {
        var pts = new Vector2[n + 1];
        var mid = (a + b) * 0.5f;
        var perp = (b - a).Orthogonal().Normalized();
        var ctrl = mid + perp * sag + new Vector2(0f, sag * 0.4f);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, u = 1f - t;
            pts[i] = a * u * u + ctrl * 2f * u * t + b * t * t;
        }
        return pts;
    }

    private void PaintCable(CanvasItem ci, PortableDevice d)
    {
        if (d.CableTo is not System.Numerics.Vector2 to) return;
        var a = ToPx(d.Position);
        var b = ToPx(to);
        if ((b - a).LengthSquared() < 9f) return;
        var pts = Sag(a, b, Mathf.Min(14f, (b - a).Length() * 0.12f) * ((d.Id & 1) == 0 ? 1f : -1f), 12);
        ci.DrawPolyline(pts, PtCable, 2.6f, true);
        ci.DrawPolyline(pts, PtCableHi, 0.9f, true);
        // 젖은 케이블: 이음매에 물이 스민다 (푸른 물빛이 반짝인다)
        if (d.Soak > 0.05f)
            for (int i = 1; i < pts.Length - 1; i += 2)
                ci.DrawCircle(pts[i], 1.6f, PtWater.WithAlpha(Mathf.Clamp(d.Soak, 0.2f, 0.9f) * (0.5f + 0.5f * Mathf.Sin(_time * 6f + i))), true, -1f, true);
        // 플러그 (배터리면 붉은·검은 집게 둘)
        var dir = (pts[^1] - pts[^2]).Normalized();
        var nrm = dir.Orthogonal();
        if (d.Plug == PortablePlug.Battery)
        {
            ci.DrawCircle(b + nrm * 2.2f, 1.8f, new Color("#d64545"), true, -1f, true);
            ci.DrawCircle(b - nrm * 2.2f, 1.8f, PtInk, true, -1f, true);
        }
        else
        {
            bool hot = d.Outlet != null && _world.Portable.CircuitKw(d.Outlet.Circuit) > PortableSystem.OutletCapKw;
            var plug = new[] { b - dir * 5f + nrm * 2.5f, b + nrm * 2.5f, b - nrm * 2.5f, b - dir * 5f - nrm * 2.5f };
            ci.DrawColoredPolygon(plug, hot ? new Color(1f, 0.45f + 0.2f * Mathf.Sin(_time * 7f), 0.15f) : new Color("#e8e3d6"));
            ci.DrawLine(b + nrm * 1.2f, b + nrm * 1.2f + dir * 2.5f, PtSteel, 1f);
            ci.DrawLine(b - nrm * 1.2f, b - nrm * 1.2f + dir * 2.5f, PtSteel, 1f);
            if (hot) ci.DrawCircle(b, 6f + 1.5f * Mathf.Sin(_time * 9f), new Color(1f, 0.5f, 0.1f, 0.18f), true, -1f, true); // 달아오른 콘센트
        }
    }

    private void PaintHose(CanvasItem ci, PortableDevice d, bool fine)
    {
        if (d.HoseTo is not System.Numerics.Vector2 to) return;
        var a = ToPx(d.Position) + new Vector2(T * 0.3f, 0f);
        var b = ToPx(to);
        var pts = Sag(a, b, Mathf.Min(18f, (b - a).Length() * 0.18f), 16);
        ci.DrawPolyline(pts, PtHose, 5.5f, true);
        ci.DrawPolyline(pts, PtHose.Lightened(0.25f), 1.4f, true);
        if (!d.Running) return;
        // 물 토막이 호스를 따라 흐른다 (배수구 쪽으로)
        float flow = _time * 2.2f + d.Id * 0.37f;
        for (int k = 0; k < 6; k++)
        {
            float t = ((k / 6f + flow) % 1f + 1f) % 1f;
            float fi = t * (pts.Length - 1);
            int i = Math.Min(pts.Length - 2, (int)fi);
            var p = pts[i].Lerp(pts[i + 1], fi - i);
            ci.DrawCircle(p, 1.7f, PtWater.WithAlpha(0.85f), true, -1f, true);
        }
        // 배수구에서 튀는 물 (동심 물결)
        for (int k = 0; k < 2; k++)
        {
            float ph = ((_time * 1.4f + k * 0.5f) % 1f);
            ci.DrawArc(b, 2f + 7f * ph, 0f, Mathf.Tau, 16, PtWater.WithAlpha(0.6f * (1f - ph)), 1.1f, true);
        }
        if (fine) ci.DrawRect(new Rect2(b - new Vector2(4f, 4f), 8f, 8f), PtSteelDark, false, 1f); // 배수구 거름망
    }

    // ───────────────────────── 장비 ─────────────────────────

    private void PaintDevice(CanvasItem ci, PortableDevice d, bool fine)
    {
        var w = _world;
        var room = w.Portable.RoomOf(d);
        if (room is { Detached: true }) return;
        var p = ToPx(d.Position);
        float s = T * 0.36f * (d.HeldBy != null ? 0.72f : d.Stored ? 0.85f : 1f);
        float dim = room is { Dark: true } && !d.Running ? 0.55f : 1f;
        if (d.Stored) PaintDock(ci, d, p, s);
        switch (d.Kind)
        {
            case PortableKind.WorkLamp: PaintLampBody(ci, d, p, s, dim, fine); break;
            case PortableKind.Heater: PaintHeaterBody(ci, d, p, s, dim, fine); break;
            case PortableKind.Fan: PaintFanBody(ci, d, p, s, dim, fine); break;
            case PortableKind.Purifier: PaintPurifierBody(ci, d, p, s, dim, fine); break;
            case PortableKind.Battery: PaintBatteryBody(ci, d, p, s, dim, fine); break;
            case PortableKind.Pump: PaintPumpBody(ci, d, p, s, dim, fine, room); break;
            case PortableKind.Cart: PaintCartBody(ci, d, p, s, dim, fine, room); break;
        }
        if (d.Broken) PaintBrokenDevice(ci, d, p, s);
        if (d.Forgotten && !d.Running && fine) // 잊고 둔 장비: 먼지 앉은 점
            for (int k = 0; k < 4; k++) ci.DrawCircle(p + new Vector2(-s * 0.6f + k * s * 0.4f, -s * 0.9f + (k % 2) * 2f), 0.9f, new Color(0.75f, 0.72f, 0.66f, 0.55f), true, -1f, true);
        if (w.Portable.Flagged(d)) // 주 컴퓨터가 짚은 장비: 위에 깜빡이는 경고 고리
            ci.DrawArc(p, s * 1.5f + 1.5f * Mathf.Sin(_time * 6f), 0f, Mathf.Tau, 20, new Color(1f, 0.6f, 0.2f, 0.7f), 1.4f, true);
    }

    private static Color Dm(Color c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);

    /// <summary>창고 충전 받침: 어두운 받침 + 충전 표시등 (충전 중엔 호박색으로 깜빡 · 다 차면 초록).</summary>
    private void PaintDock(CanvasItem ci, PortableDevice d, Vector2 p, float s)
    {
        var r = new Rect2(p - new Vector2(s * 1.25f, s * 1.1f), s * 2.5f, s * 2.2f);
        Gfx.RoundRect(ci, r, new Color(0.08f, 0.1f, 0.13f, 0.75f), 4, new Color(0.3f, 0.35f, 0.42f, 0.6f), 1);
        if (d.Capacity <= 0f) return;
        bool blink = d.Charging && Mathf.Sin(_time * 4f + d.Id) > 0f;
        var led = d.Charging ? new Color(1f, 0.65f, 0.15f, blink ? 1f : 0.35f) : new Color(0.35f, 1f, 0.5f, 0.85f);
        ci.DrawCircle(new Vector2(r.End.X - 4f, r.Position.Y + 4f), 1.6f, led, true, -1f, true);
    }

    private void PaintLampBody(CanvasItem ci, PortableDevice d, Vector2 p, float s, float dim, bool fine)
    {
        var aim = d.Aim != System.Numerics.Vector2.Zero ? (ToPx(d.Aim) - p) : new Vector2(1f, -0.4f);
        if (aim.LengthSquared() < 1f) aim = new Vector2(1f, 0f);
        var dir = aim.Normalized();
        var nrm = dir.Orthogonal();
        // 삼각대: 세 다리 · 발
        if (d.HeldBy == null)
            for (int k = 0; k < 3; k++)
            {
                var leg = Vector2.Right.Rotated(Mathf.Tau * k / 3f + 0.5f) * s * 1.05f;
                ci.DrawLine(p, p + leg, Dm(PtSteelDark, dim), 1.6f, true);
                ci.DrawCircle(p + leg, 1.6f, Dm(PtInk, dim), true, -1f, true);
            }
        // 머리: 겨눈 쪽으로 넓어지는 사다리꼴
        var back = p - dir * s * 0.35f;
        var front = p + dir * s * 0.6f;
        var head = new[] { back + nrm * s * 0.28f, front + nrm * s * 0.55f, front - nrm * s * 0.55f, back - nrm * s * 0.28f };
        ci.DrawColoredPolygon(head, Dm(PtLampBody, dim));
        ci.DrawPolyline(new[] { head[0], head[1], head[2], head[3], head[0] }, Dm(PtLampBody.Darkened(0.45f), dim), 1f, true);
        // 렌즈: 켜지면 따뜻한 빛 (잔량이 바닥나면 깜빡)
        float on = d.LightIntensity;
        if (on > 0f && d.ChargeFrac < 0.1f && d.Plug == PortablePlug.None && Mathf.Sin(_time * 13f + d.Id) > 0.4f) on *= 0.4f;
        var lens = on > 0f ? PtLampLens.Lerp(Colors.White, 0.3f * on) : Dm(new Color("#4a4434"), dim);
        ci.DrawLine(front + nrm * s * 0.5f, front - nrm * s * 0.5f, lens, 2.6f, true);
        // 격자 (렌즈 앞 보호망)
        for (int k = -1; k <= 1; k++)
        {
            var a = Vector2.Zero.Lerp(nrm * s * 0.5f, k);
            ci.DrawLine(front + a - dir * s * 0.12f, front + a + dir * 1.2f, Dm(PtInk, dim).WithAlpha(0.8f), 0.8f, true);
        }
        if (on > 0f) ci.DrawCircle(front + dir * 2f, s * 0.35f, new Color(1f, 0.95f, 0.75f, 0.25f * on), true, -1f, true);
        if (!fine) return;
        // 확대: 손잡이 고리 · 경첩 볼트 · 뒤쪽 잔량 표시등
        ci.DrawArc(p - dir * s * 0.05f, s * 0.32f, nrm.Angle() - 0.2f, nrm.Angle() + Mathf.Pi + 0.2f, 10, Dm(PtSteel, dim), 1.2f, true);
        ci.DrawCircle(p, 1.6f, PtBolt, true, -1f, true);
        ci.DrawCircle(p, 0.7f, PtInk, true, -1f, true);
        float cf = d.Capacity > 0f ? d.ChargeFrac : 1f;
        var led = cf > 0.5f ? new Color(0.4f, 1f, 0.5f) : cf > 0.15f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.3f, 0.25f);
        ci.DrawCircle(back - dir * 1.5f, 1.1f, led.WithAlpha(d.Running ? 1f : 0.35f), true, -1f, true);
    }

    private void PaintHeaterBody(CanvasItem ci, PortableDevice d, Vector2 p, float s, float dim, bool fine)
    {
        var box = new Rect2(p - new Vector2(s * 0.8f, s * 0.55f), s * 1.6f, s * 1.1f);
        Gfx.RoundRect(ci, box, Dm(PtHeaterBody, dim), 3, Dm(PtHeaterRim, dim), 1);
        // 열선: 세로 틈 사이 지그재그 — 켜지면 붉게 숨 쉰다 (전압이 떨어지면 어둡다)
        float glow = d.Running ? (0.65f + 0.35f * Mathf.Sin(_time * 2.4f + d.Id)) * Mathf.Clamp(d.Supply * d.Supply, 0.25f, 1f) : 0f;
        var coil = glow > 0f ? new Color(1f, 0.28f + 0.3f * glow, 0.1f).Lerp(PtCoil, 0.3f) : Dm(new Color("#3b1e19"), dim);
        int slots = 5;
        float x0 = box.Position.X + s * 0.25f, dx = (box.Size.X - s * 0.5f) / (slots - 1);
        for (int k = 0; k < slots; k++)
        {
            float x = x0 + dx * k;
            ci.DrawLine(new Vector2(x, box.Position.Y + 2.5f), new Vector2(x, box.End.Y - 2.5f), Dm(PtInk, dim), 1.4f);
        }
        var zig = new Vector2[9];
        for (int row = 0; row < 2; row++)
        {
            float y = box.Position.Y + box.Size.Y * (0.35f + 0.3f * row);
            for (int k = 0; k < zig.Length; k++) zig[k] = new Vector2(box.Position.X + 3f + (box.Size.X - 6f) * k / (zig.Length - 1), y + ((k & 1) == 0 ? -1.6f : 1.6f));
            ci.DrawPolyline(zig, coil.WithAlpha(glow > 0f ? 0.6f + 0.4f * glow : 1f), 1.3f, true);
        }
        if (glow > 0f)
        {
            ci.DrawRect(box.Grow(-2f), new Color(1f, 0.3f, 0.1f, 0.12f * glow));
            // 위로 일렁이는 열기
            for (int k = 0; k < 3; k++)
            {
                var pts = new Vector2[6];
                for (int j = 0; j < pts.Length; j++)
                {
                    float t = j / 5f;
                    pts[j] = new Vector2(box.Position.X + box.Size.X * (0.25f + 0.25f * k) + 2f * Mathf.Sin(_time * 4f + t * 6f + k), box.Position.Y - 2f - t * s * 0.9f);
                }
                ci.DrawPolyline(pts, new Color(1f, 0.55f, 0.3f, 0.28f * glow), 1f, true);
            }
            // 먼지가 탄다: 잿빛 실 연기
            if (d.Dust > 0.05f)
                for (int k = 0; k < 3; k++)
                {
                    float ph = (_time * 0.6f + k * 0.33f + d.Id * 0.1f) % 1f;
                    ci.DrawCircle(new Vector2(box.Position.X + box.Size.X * (0.3f + 0.2f * k) + 3f * Mathf.Sin(ph * 6f), box.Position.Y - 3f - ph * s * 1.4f), 1.2f + 1.6f * ph,
                        new Color(0.6f, 0.58f, 0.55f, 0.5f * (1f - ph) * d.Dust), true, -1f, true);
                }
        }
        if (!fine) return;
        // 확대: 온도 손잡이(눈금 바늘) · 받침 발 · 위 손잡이 틈 · 모서리 볼트
        var knob = new Vector2(box.End.X - 3.5f, box.Position.Y + 3.5f);
        ci.DrawCircle(knob, 2.4f, Dm(PtSteel, dim), true, -1f, true);
        ci.DrawLine(knob, knob + Vector2.Right.Rotated(d.On ? -0.6f : 2.2f) * 2.2f, PtInk, 0.9f, true);
        Gfx.RoundRect(ci, new Rect2(box.Position.X + box.Size.X * 0.35f, box.Position.Y + 1.2f, box.Size.X * 0.3f, 2f), PtInk, 1);
        ci.DrawRect(new Rect2(box.Position.X + 2f, box.End.Y, 4f, 1.6f), Dm(PtSteelDark, dim));
        ci.DrawRect(new Rect2(box.End.X - 6f, box.End.Y, 4f, 1.6f), Dm(PtSteelDark, dim));
        foreach (var c in new[] { box.Position + new Vector2(1.6f, 1.6f), new Vector2(box.Position.X + 1.6f, box.End.Y - 1.6f), box.End - new Vector2(1.6f, 1.6f) })
            ci.DrawCircle(c, 0.7f, PtBolt, true, -1f, true);
    }

    private void PaintFanBody(CanvasItem ci, PortableDevice d, Vector2 p, float s, float dim, bool fine)
    {
        float r = s * 0.85f;
        // 받침 (뒤로 비죽)
        Gfx.RoundRect(ci, new Rect2(p + new Vector2(-s * 0.45f, r * 0.55f), s * 0.9f, s * 0.42f), Dm(PtSteelDark, dim), 2);
        // 날개: 세 장 — 돌 때는 빠르게 돌고 흐린 원판이 겹친다
        float ang = d.Running ? _time * 16f + d.Id : d.Id * 1.3f;
        if (d.Running) ci.DrawCircle(p, r * 0.9f, Dm(PtFanBlade, dim).WithAlpha(0.18f), true, -1f, true);
        for (int k = 0; k < 3; k++)
        {
            float a = ang + Mathf.Tau * k / 3f;
            var tip = p + Vector2.Right.Rotated(a) * r * 0.85f;
            var l = p + Vector2.Right.Rotated(a - 0.42f) * r * 0.55f;
            var rr = p + Vector2.Right.Rotated(a + 0.22f) * r * 0.6f;
            ci.DrawColoredPolygon(new[] { p, l, tip, rr }, Dm(PtFanBlade, dim).WithAlpha(d.Running ? 0.55f : 1f));
        }
        // 망: 바깥 고리 · 안쪽 고리
        ci.DrawArc(p, r, 0f, Mathf.Tau, 24, Dm(PtFanCage, dim), 1.4f, true);
        ci.DrawArc(p, r * 0.55f, 0f, Mathf.Tau, 18, Dm(PtFanCage, dim).WithAlpha(0.55f), 0.8f, true);
        ci.DrawCircle(p, r * 0.18f, Dm(PtSteel, dim), true, -1f, true);
        // 바람 줄 (오른쪽으로)
        if (d.Running)
            for (int k = 0; k < 3; k++)
            {
                float ph = (_time * 1.6f + k * 0.33f) % 1f;
                var a = p + new Vector2(r + 2f + ph * s * 1.6f, (k - 1) * r * 0.55f);
                ci.DrawLine(a, a + new Vector2(5f, 0f), new Color(0.85f, 0.95f, 1f, 0.45f * (1f - ph)), 1f, true);
            }
        if (!fine) return;
        // 확대: 망살 여덟 · 가운데 덮개 점 · 받침 단추
        for (int k = 0; k < 8; k++)
        {
            var v = Vector2.Right.Rotated(Mathf.Tau * k / 8f);
            ci.DrawLine(p + v * r * 0.2f, p + v * r, Dm(PtFanCage, dim).WithAlpha(0.6f), 0.6f, true);
        }
        ci.DrawCircle(p, 1f, PtBolt, true, -1f, true);
        for (int k = 0; k < 3; k++) ci.DrawCircle(p + new Vector2(-s * 0.25f + k * s * 0.25f, r * 0.55f + s * 0.21f), 0.9f, k == 1 && d.Running ? new Color(0.5f, 0.9f, 1f) : PtBolt, true, -1f, true);
    }

    private void PaintPurifierBody(CanvasItem ci, PortableDevice d, Vector2 p, float s, float dim, bool fine)
    {
        var box = new Rect2(p - new Vector2(s * 0.45f, s * 0.82f), s * 0.9f, s * 1.64f);
        Gfx.RoundRect(ci, box, Dm(PtPuriBody, dim), 5, Dm(PtPuriGrid, dim), 1);
        // 필터 격자 (촘촘한 가로 · 세로)
        var f = box.Grow(-s * 0.16f);
        f.Position += new Vector2(0f, s * 0.12f);
        f.Size -= new Vector2(0f, s * 0.12f);
        for (int k = 1; k < 6; k++)
        {
            float y = f.Position.Y + f.Size.Y * k / 6f;
            ci.DrawLine(new Vector2(f.Position.X, y), new Vector2(f.End.X, y), Dm(PtPuriGrid, dim), 0.7f);
        }
        for (int k = 1; k < 4; k++)
        {
            float x = f.Position.X + f.Size.X * k / 4f;
            ci.DrawLine(new Vector2(x, f.Position.Y), new Vector2(x, f.End.Y), Dm(PtPuriGrid, dim), 0.7f);
        }
        // 푸른 띠: 돌면 숨 쉰다 (공기가 탁하면 빨리)
        var room = _world.Portable.RoomOf(d);
        float dirty = room == null ? 0f : Mathf.Clamp(room.Air.Smoke * 3f + room.Smell, 0f, 1f);
        if (d.Running)
        {
            float br = 0.55f + 0.45f * Mathf.Sin(_time * (2f + 4f * dirty) + d.Id);
            Gfx.RoundRect(ci, box.Grow(1f), new Color(0, 0, 0, 0), 6, PtPuriGlow.WithAlpha(0.35f + 0.5f * br), 1);
            ci.DrawLine(new Vector2(f.Position.X, f.Position.Y - 2f), new Vector2(f.End.X, f.Position.Y - 2f), PtPuriGlow.WithAlpha(0.6f + 0.4f * br), 1.6f, true);
            // 먼지 알갱이가 빨려 든다
            for (int k = 0; k < 5; k++)
            {
                float ph = (_time * 0.9f + k * 0.2f + d.Id * 0.13f) % 1f;
                float a = k * 1.3f + d.Id;
                var from = p + Vector2.Right.Rotated(a) * s * 1.8f;
                var at = from.Lerp(p, ph);
                ci.DrawCircle(at, 1f, new Color(0.7f, 0.68f, 0.6f, 0.6f * (1f - ph * 0.6f)), true, -1f, true);
            }
        }
        if (!fine) return;
        // 확대: 위 손잡이 · 상태 표시등 · 공기질 세 칸
        ci.DrawArc(new Vector2(p.X, box.Position.Y + 1f), s * 0.22f, Mathf.Pi, Mathf.Tau, 8, Dm(PtSteelDark, dim), 1.2f, true);
        ci.DrawCircle(new Vector2(box.End.X - 2.5f, box.Position.Y + 3f), 1f, d.Running ? new Color(0.4f, 0.85f, 1f) : new Color(0.35f, 0.35f, 0.4f), true, -1f, true);
        for (int k = 0; k < 3; k++)
        {
            var seg = new Rect2(box.Position.X + 2f + k * 3.2f, box.Position.Y + 2f, 2.6f, 1.6f);
            var col = dirty > 0.66f ? new Color(1f, 0.35f, 0.3f) : dirty > 0.33f ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 1f, 0.55f);
            ci.DrawRect(seg, k < 1 + (int)(dirty * 2.99f) ? col.WithAlpha(d.Running ? 1f : 0.3f) : PtInk);
        }
    }

    private void PaintBatteryBody(CanvasItem ci, PortableDevice d, Vector2 p, float s, float dim, bool fine)
    {
        var box = new Rect2(p - new Vector2(s * 0.7f, s * 0.45f), s * 1.4f, s * 1.0f);
        Gfx.RoundRect(ci, box, Dm(PtBatBody, dim), 3, Dm(PtBatRim, dim), 1);
        // 단자 둘 (+ 붉게 · − 검게) · 위 손잡이
        ci.DrawRect(new Rect2(box.Position.X + s * 0.18f, box.Position.Y - 2.2f, 3f, 2.2f), new Color("#c94040"));
        ci.DrawRect(new Rect2(box.End.X - s * 0.18f - 3f, box.Position.Y - 2.2f, 3f, 2.2f), PtInk);
        ci.DrawLine(new Vector2(p.X - s * 0.25f, box.Position.Y - 3.4f), new Vector2(p.X + s * 0.25f, box.Position.Y - 3.4f), Dm(PtSteel, dim), 2f, true);
        // 잔량 다섯 칸 (충전 중엔 칸이 차례로 차오른다 · 바닥나면 마지막 칸이 깜빡)
        float cf = d.ChargeFrac;
        int lit = Mathf.Clamp(Mathf.CeilToInt(cf * 5f - 0.01f), 0, 5);
        if (d.Charging) lit = Math.Min(5, lit + ((int)(_time * 2.5f) % (6 - Math.Min(5, lit))));
        var col = cf > 0.5f ? new Color(0.35f, 1f, 0.45f) : cf > 0.2f ? new Color(1f, 0.85f, 0.25f) : new Color(1f, 0.3f, 0.25f);
        bool low = cf < 0.2f && Mathf.Sin(_time * 8f + d.Id) > 0f;
        float segW = (box.Size.X - 6f) / 5f;
        for (int k = 0; k < 5; k++)
        {
            var seg = new Rect2(box.Position.X + 3f + k * segW, p.Y - 1.8f, segW - 1.2f, 4.2f);
            bool on = k < lit && !(low && k == lit - 1);
            ci.DrawRect(seg, on ? col.WithAlpha(dim < 1f ? 0.7f : 1f) : new Color(0.08f, 0.1f, 0.09f));
        }
        // 물려 있는 장비로 전기가 나간다: 단자에서 작은 맥동
        if (d.On && d.Placed && _world.Portable.Devices.Exists(x => x.Source == d && x.Running))
            ci.DrawArc(new Vector2(box.Position.X + s * 0.18f + 1.5f, box.Position.Y - 1f), 3f + 2f * ((_time * 2f) % 1f), 0f, Mathf.Tau, 10, new Color(0.4f, 1f, 0.5f, 0.5f * (1f - (_time * 2f) % 1f)), 0.8f, true);
        if (!fine) return;
        // 확대: 모서리 볼트 · 환기 틈 · +/− 표
        foreach (var c in new[] { box.Position + new Vector2(1.8f, 1.8f), new Vector2(box.End.X - 1.8f, box.Position.Y + 1.8f), new Vector2(box.Position.X + 1.8f, box.End.Y - 1.8f), box.End - new Vector2(1.8f, 1.8f) })
            ci.DrawCircle(c, 0.8f, PtBolt, true, -1f, true);
        for (int k = 0; k < 4; k++) ci.DrawLine(new Vector2(box.Position.X + 3f + k * 2.4f, box.End.Y - 3f), new Vector2(box.Position.X + 3f + k * 2.4f, box.End.Y - 1.4f), PtInk, 0.8f);
        var plus = new Vector2(box.Position.X + s * 0.18f + 1.5f, box.Position.Y + 3f);
        ci.DrawLine(plus - new Vector2(1.2f, 0), plus + new Vector2(1.2f, 0), PtBolt, 0.7f);
        ci.DrawLine(plus - new Vector2(0, 1.2f), plus + new Vector2(0, 1.2f), PtBolt, 0.7f);
        var minus = new Vector2(box.End.X - s * 0.18f - 1.5f, box.Position.Y + 3f);
        ci.DrawLine(minus - new Vector2(1.2f, 0), minus + new Vector2(1.2f, 0), PtBolt, 0.7f);
    }

    private void PaintPumpBody(CanvasItem ci, PortableDevice d, Vector2 p, float s, float dim, bool fine, Room? room)
    {
        // 돌면 떤다
        if (d.Running) p += new Vector2(Mathf.Sin(_time * 61f + d.Id) * 0.8f, Mathf.Cos(_time * 47f) * 0.6f);
        // 흡입구 둘레 물결 (바닥에 물이 있으면)
        if (d.Running && room is { Flood: > 0f })
            for (int k = 0; k < 2; k++)
            {
                float ph = (_time * 0.9f + k * 0.5f) % 1f;
                ci.DrawArc(p, s * (1.6f - ph), 0f, Mathf.Tau, 22, PtWater.WithAlpha(0.45f * ph), 1f, true);
            }
        // 받침 · 흡입관
        Gfx.RoundRect(ci, new Rect2(p + new Vector2(-s * 0.75f, s * 0.2f), s * 1.5f, s * 0.5f), Dm(PtSteelDark, dim), 2);
        ci.DrawLine(p + new Vector2(-s * 0.6f, s * 0.1f), p + new Vector2(-s * 1.05f, s * 0.55f), Dm(PtHose, dim), 3.5f, true);
        // 모터 원통 · 냉각 핀
        float r = s * 0.58f;
        for (int k = 0; k < 12; k++)
        {
            var v = Vector2.Right.Rotated(Mathf.Tau * k / 12f);
            ci.DrawLine(p + v * r * 0.9f, p + v * (r + 2.2f), Dm(PtPumpFin, dim), 1.4f, true);
        }
        ci.DrawCircle(p, r, Dm(PtPumpBody, dim), true, -1f, true);
        ci.DrawArc(p, r, 0f, Mathf.Tau, 20, Dm(PtPumpBody.Lightened(0.3f), dim), 1f, true);
        // 가운데 회전 덮개 (돌면 홈이 돈다)
        float ang = d.Running ? _time * 20f : 0.4f;
        ci.DrawCircle(p, r * 0.4f, Dm(PtPumpFin, dim), true, -1f, true);
        ci.DrawLine(p - Vector2.Right.Rotated(ang) * r * 0.35f, p + Vector2.Right.Rotated(ang) * r * 0.35f, Dm(PtSteel, dim), 1f, true);
        if (!fine) return;
        // 확대: 압력계(바늘) · 받침 볼트 · 스위치 등
        var g = p + new Vector2(r * 0.9f, -r * 0.8f);
        ci.DrawCircle(g, 2.8f, new Color("#e9edf2"), true, -1f, true);
        ci.DrawArc(g, 2.8f, 0f, Mathf.Tau, 12, PtInk, 0.7f, true);
        float needle = d.Running ? -2.4f + 1.6f + 0.15f * Mathf.Sin(_time * 9f) : -2.4f;
        ci.DrawLine(g, g + Vector2.Right.Rotated(needle) * 2.2f, new Color("#d23a3a"), 0.8f, true);
        foreach (float x in new[] { -s * 0.6f, s * 0.6f }) ci.DrawCircle(p + new Vector2(x, s * 0.45f), 0.8f, PtBolt, true, -1f, true);
        ci.DrawCircle(p + new Vector2(0f, s * 0.45f), 1f, d.Running ? new Color(0.4f, 1f, 0.5f) : new Color(0.9f, 0.3f, 0.25f), true, -1f, true);
    }

    private void PaintCartBody(CanvasItem ci, PortableDevice d, Vector2 p, float s, float dim, bool fine, Room? room)
    {
        var bed = new Rect2(p - new Vector2(s * 0.95f, s * 0.6f), s * 1.9f, s * 1.2f);
        // 바퀴 넷 (밀고 가면 바퀴살이 돈다)
        bool rolling = d.HeldBy is { IsMoving: true };
        float spin = rolling ? _time * 12f : d.Id;
        foreach (var c in new[] { bed.Position + new Vector2(2.5f, -0.5f), new Vector2(bed.End.X - 2.5f, bed.Position.Y - 0.5f), new Vector2(bed.Position.X + 2.5f, bed.End.Y + 0.5f), bed.End + new Vector2(-2.5f, 0.5f) })
        {
            ci.DrawCircle(c, 2.6f, Dm(PtWheel, dim), true, -1f, true);
            var v = Vector2.Right.Rotated(spin) * 2.2f;
            ci.DrawLine(c - v, c + v, Dm(PtSteel, dim), 0.7f, true);
            if (fine) ci.DrawCircle(c, 0.6f, PtBolt, true, -1f, true);
        }
        // 짐받이 · 널판
        Gfx.RoundRect(ci, bed, Dm(PtCartBed, dim), 2, Dm(PtCartBed.Lightened(0.25f), dim), 1);
        for (int k = 1; k < 4; k++)
        {
            float x = bed.Position.X + bed.Size.X * k / 4f;
            ci.DrawLine(new Vector2(x, bed.Position.Y + 1.5f), new Vector2(x, bed.End.Y - 1.5f), Dm(PtCartPlank, dim), 0.9f);
        }
        // 미는 손잡이 (U자)
        float hx = bed.Position.X - 3.5f;
        ci.DrawPolyline(new[] { new Vector2(bed.Position.X, bed.Position.Y + 2f), new Vector2(hx, bed.Position.Y + 2f), new Vector2(hx, bed.End.Y - 2f), new Vector2(bed.Position.X, bed.End.Y - 2f) },
            Dm(PtSteel, dim), 1.6f, true);
        if (fine) ci.DrawLine(new Vector2(hx - 0.5f, bed.Position.Y + 4f), new Vector2(hx - 0.5f, bed.End.Y - 4f), new Color("#2a2a2a"), 2.4f, true); // 고무 손잡이
        // 실린 짐이 없으면 상자 하나 (창고 짐받이)
        bool loaded = _world.Portable.Devices.Exists(x => x.OnCart == d);
        if (!loaded && d.Placed && !d.Stored)
        {
            var crate = new Rect2(p + new Vector2(-s * 0.35f, -s * 0.35f), s * 0.7f, s * 0.6f);
            ci.DrawRect(crate, Dm(new Color("#9c7a4b"), dim));
            ci.DrawLine(crate.Position, crate.End, Dm(new Color("#6b5131"), dim), 0.8f);
            ci.DrawLine(new Vector2(crate.End.X, crate.Position.Y), new Vector2(crate.Position.X, crate.End.Y), Dm(new Color("#6b5131"), dim), 0.8f);
        }
        // 통로를 막는 카트: 노랑·검정 사선 띠
        if (d.Placed && room is { Type: RoomType.Corridor })
        {
            float y = bed.End.Y - 2.5f;
            for (int k = 0; k < 6; k++)
            {
                float x = bed.Position.X + 2f + k * (bed.Size.X - 4f) / 6f;
                ci.DrawLine(new Vector2(x, y + 1.5f), new Vector2(x + 2.5f, y - 1.5f), (k & 1) == 0 ? PtHazardY : PtInk, 1.6f);
            }
        }
    }

    /// <summary>고장: 금 간 선 · 연기 · 가끔 튀는 불꽃 (젖어서 고장 나면 물방울).</summary>
    private void PaintBrokenDevice(CanvasItem ci, PortableDevice d, Vector2 p, float s)
    {
        ci.DrawPolyline(new[] { p + new Vector2(-s * 0.5f, -s * 0.4f), p + new Vector2(-s * 0.1f, -s * 0.05f), p + new Vector2(-s * 0.3f, s * 0.15f), p + new Vector2(s * 0.3f, s * 0.45f) },
            new Color(0.05f, 0.05f, 0.06f, 0.9f), 1.2f, true);
        float ph = (_time * 0.5f + d.Id * 0.21f) % 1f;
        ci.DrawCircle(p + new Vector2(2f * Mathf.Sin(ph * 5f), -s * 0.6f - ph * s * 1.4f), 2f + 3f * ph, new Color(0.25f, 0.25f, 0.27f, 0.5f * (1f - ph)), true, -1f, true);
        if (Mathf.Sin(_time * 17f + d.Id * 3f) > 0.93f)
            for (int k = 0; k < 3; k++)
                ci.DrawLine(p, p + Vector2.Right.Rotated(k * 2.1f + _time) * s * 0.7f, new Color(1f, 0.9f, 0.45f, 0.9f), 0.8f, true);
    }

    // ───────────────────────── 광원 (더하기 섞기 층) ─────────────────────────

    /// <summary>이동식 광원: 작업등은 겨눈 쪽으로 원뿔 · 히터는 붉은 열기 · 청정기는 푸른 띠 · 배터리는 작은 초록 표시등 — Core의 공개 광원 속성 그대로.</summary>
    private void PaintPortableLights(CanvasItem ci)
    {
        if (Textures.Light is not Texture2D tex) return;
        foreach (var d in _world.Portable.Lights)
        {
            if (_world.Portable.RoomOf(d) is { Detached: true }) continue;
            var pos = ToPx(d.LightPos);
            float r = d.LightRadius * T, k = d.LightIntensity;
            var col = new Color(d.LightColor.X, d.LightColor.Y, d.LightColor.Z);
            if (d.Directional)
            {
                var dir = (ToPx(d.Aim) - pos).Normalized();
                if (dir == Vector2.Zero) dir = Vector2.Right;
                var pts = new Vector2[10];
                var cols = new Color[10];
                pts[0] = pos;
                cols[0] = col.WithAlpha(0.42f * k);
                for (int i = 1; i < 10; i++)
                {
                    float a = -0.62f + 1.24f * (i - 1) / 8f;
                    pts[i] = pos + dir.Rotated(a) * r * (0.9f + 0.1f * Mathf.Cos(a * 2f));
                    cols[i] = col.WithAlpha(0f);
                }
                ci.DrawPolygon(pts, cols);
                float pool = r * 0.9f;
                ci.DrawTextureRect(tex, new Rect2(pos + dir * r * 0.3f - new Vector2(pool, pool) * 0.5f, pool, pool), false, col.WithAlpha(0.22f * k));
            }
            else
            {
                float size = r * 2f;
                ci.DrawTextureRect(tex, new Rect2(pos - new Vector2(size, size) * 0.5f, size, size), false, col.WithAlpha(0.3f * k));
            }
        }
    }
}
