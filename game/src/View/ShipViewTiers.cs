using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v11.3 단계마다 다른 설비 모양 (원자로 핵융합 말고도): 올린 설비가 딱지만이 아니라 모양으로도 다르다.
///   산소 발생기 II 사바티에(달아오른 반응관·나선 코일) · III 조류 광합성조(초록 물·오르는 거품)
///   정수기 II 증기 압축(도는 압축기·김) · III 폐쇄 순환(파랗게 도는 흐름 고리)
///   재배대 II LED(보랏빛 광원 막대) · III 에어로포닉스(뿌리에 뿌리는 안개)
///   배터리 II 고체 전해질(칸마다 청록 빛) · III 초전도 저장고(서리 낀 파란 고리)
///   작업대 II CNC(오가는 가공 머리·레이저 점) · III 적층 제조기(층이 쌓이는 출력물)
///   냉각 펌프 II 고압(빠른 회전·압력계) · III 초전도(얼음빛 테)
///   정제기 II 플라스마(보랏빛 아크) · 보조 발전기 II 연료전지(쌓인 셀) · 주 컴퓨터 II 분산 제어(깜박이는 노드 격자)
///   장거리 센서 II 위상 배열(반짝이는 소자판) · 엔진 II 이온 추진기(푸른 이온 빛)
/// 전기가 없거나 멈춘 설비는 빛이 죽는다.
/// </summary>
public partial class ShipView
{
    private void PaintTierLife(CanvasItem ci, Furniture f, Machine m, float t)
    {
        if (m.Tier < 2) return;
        var r = FurnitureRect(f);
        var c = r.GetCenter();
        bool on = m.Efficiency > 0.01f;
        float a = on ? 1f : 0.25f;
        switch (f.Type)
        {
            case FurnitureType.OxygenGenerator when m.Tier >= 3:
            {
                // 조류 광합성조: 투명한 통에 초록 물, 오르는 거품
                var tank = new Rect2(r.Position.X + 6, r.Position.Y + 6, r.Size.X - 12, r.Size.Y - 12);
                Gfx.RoundRect(ci, tank, new Color("#1f5a2a").WithAlpha(0.55f * a), 6, new Color("#6ee07a").WithAlpha(0.6f * a), 1);
                for (int k = 0; k < 7; k++)
                {
                    float ph = Mathf.PosMod(t * (0.25f + 0.05f * k) + k * 0.37f, 1f);
                    var p = new Vector2(tank.Position.X + tank.Size.X * (0.15f + 0.7f * Hash(f.Id, k, 3)), tank.End.Y - tank.Size.Y * ph);
                    ci.Circle(p, 1.2f + 1.3f * Hash(f.Id, k, 4), new Color("#c8ffb0").WithAlpha(0.7f * (1f - ph) * a), true, -1f, true);
                }
                float sw = Mathf.Sin(t * 0.7f) * 4f;
                ci.Arc(c + new Vector2(sw, 0), tank.Size.Y * 0.28f, 0.3f, 2.6f, 16, new Color("#3fb24e").WithAlpha(0.5f * a), 2f, true);
                break;
            }
            case FurnitureType.OxygenGenerator:
            {
                // 사바티에 반응기: 달아오른 반응관과 나선 코일
                var tube = new Rect2(c.X - 6, r.Position.Y + 7, 12, r.Size.Y - 14);
                Gfx.RoundRect(ci, tube, new Color("#2a1a12"), 5, new Color("#ff9a5c").WithAlpha(0.8f * a), 1);
                float glow = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
                ci.Box(tube.Grow(-3f), new Color("#ff7a3c").WithAlpha((0.25f + 0.25f * glow) * a));
                for (int k = 0; k < 5; k++)
                {
                    float y = tube.Position.Y + 4 + k * (tube.Size.Y - 8) / 4f;
                    ci.DrawLine(new Vector2(tube.Position.X - 3, y), new Vector2(tube.End.X + 3, y + 3), new Color("#c07a4a").WithAlpha(0.8f), 1.2f, true);
                }
                break;
            }
            case FurnitureType.WaterRecycler when m.Tier >= 3:
            {
                // 폐쇄 순환: 파랗게 도는 흐름 고리
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.32f;
                ci.Arc(c, rad, 0f, Mathf.Tau, 32, new Color("#3a8fd9").WithAlpha(0.35f * a), 3f, true);
                for (int k = 0; k < 3; k++)
                {
                    float ang = (on ? t * 1.6f : 0f) + k * Mathf.Tau / 3f;
                    ci.Arc(c, rad, ang, ang + 0.7f, 10, new Color("#9ad0ff").WithAlpha(0.9f * a), 3f, true);
                    var tip = c + Vector2.FromAngle(ang + 0.7f) * rad;
                    ci.Circle(tip, 2.2f, new Color("#d8f0ff").WithAlpha(a), true, -1f, true);
                }
                break;
            }
            case FurnitureType.WaterRecycler:
            {
                // 증기 압축: 도는 압축기와 김
                var hub = new Vector2(r.End.X - 12, r.Position.Y + 12);
                ci.Circle(hub, 7f, new Color("#1a2330"), true, -1f, true);
                for (int k = 0; k < 4; k++)
                {
                    var d = Vector2.FromAngle((on ? t * 7f : 0.3f) + k * Mathf.Pi / 2f);
                    ci.DrawLine(hub + d * 2f, hub + d * 6f, new Color("#9fb4cc").WithAlpha(0.9f), 1.5f, true);
                }
                if (on)
                    for (int k = 0; k < 3; k++)
                    {
                        float ph = Mathf.PosMod(t * 0.6f + k / 3f, 1f);
                        ci.Circle(hub + new Vector2(Mathf.Sin(t + k) * 3f, -8f - 14f * ph), 2f + 3f * ph, new Color(1, 1, 1, 0.25f * (1f - ph)), true, -1f, true);
                    }
                break;
            }
            case FurnitureType.GrowBed when m.Tier >= 3:
            {
                // 에어로포닉스: 뿌리에 뿌리는 안개
                if (!on) break;
                for (int k = 0; k < 10; k++)
                {
                    float ph = Mathf.PosMod(t * 0.5f + k * 0.13f, 1f);
                    var p = new Vector2(r.Position.X + 6 + (r.Size.X - 12) * Hash(f.Id, k, 7), r.End.Y - 5 - 8f * ph);
                    ci.Circle(p, 2f + 3f * ph, new Color(0.85f, 0.95f, 1f, 0.18f * (1f - ph)), true, -1f, true);
                }
                ci.DrawLine(new Vector2(r.Position.X + 5, r.End.Y - 4), new Vector2(r.End.X - 5, r.End.Y - 4), new Color("#9fb4cc").WithAlpha(0.6f), 1.2f);
                break;
            }
            case FurnitureType.GrowBed:
            {
                // LED 광원 막대 (보랏빛)
                float pulse = 0.7f + 0.3f * Mathf.Sin(t * 1.5f);
                var bar = new Rect2(r.Position.X + 4, r.Position.Y + 2, r.Size.X - 8, 3);
                ci.Box(bar, new Color("#c77dff").WithAlpha(0.85f * pulse * a));
                ci.Box(new Rect2(bar.Position.X, bar.End.Y, bar.Size.X, 10), new Color("#c77dff").WithAlpha(0.08f * pulse * a));
                break;
            }
            case FurnitureType.Battery when m.Tier >= 3:
            {
                // 초전도 저장고: 서리 낀 파란 고리
                float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.3f;
                for (int k = 0; k < 2; k++)
                    ci.Arc(c, rad - k * 4f, (on ? t * (k == 0 ? 0.8f : -1.1f) : 0f), (on ? t * (k == 0 ? 0.8f : -1.1f) : 0f) + 4.5f, 24,
                        new Color("#a8e6ff").WithAlpha(0.75f * a), 2f, true);
                ci.Circle(c, rad * 0.4f, new Color("#e8fbff").WithAlpha(0.35f * a), true, -1f, true);
                break;
            }
            case FurnitureType.Battery:
            {
                // 고체 전해질: 칸마다 청록 빛
                int cells = 4;
                float charge = _world.Power.BatteryPercent;
                for (int k = 0; k < cells; k++)
                {
                    var cell = new Rect2(r.Position.X + 6 + k * (r.Size.X - 12) / cells, r.Position.Y + 6, (r.Size.X - 12) / cells - 2, 5);
                    bool lit = charge > (k + 0.5f) / cells;
                    ci.Box(cell, (lit ? new Color("#5fe0d0") : new Color("#1d2b30")).WithAlpha(lit ? 0.85f * a : 0.8f));
                }
                break;
            }
            case FurnitureType.Workbench when m.Tier >= 3:
            {
                // 적층 제조기: 층이 쌓이는 출력물과 노즐
                float prog = on && m.Active ? Mathf.PosMod(t * 0.15f, 1f) : 0.4f;
                var bed = new Rect2(c.X - 9, r.End.Y - 8, 18, 3);
                ci.Box(bed, new Color("#2f3a48"));
                int layers = 1 + (int)(prog * 6);
                for (int k = 0; k < layers; k++)
                    ci.Box(new Rect2(c.X - 7 + k * 0.5f, bed.Position.Y - 2 - k * 2, 14 - k, 2), new Color("#e0b64a").WithAlpha(0.8f));
                var nozzle = new Vector2(c.X - 6 + 12 * Mathf.PosMod(t * 1.3f, 1f), bed.Position.Y - 3 - layers * 2);
                ci.DrawLine(new Vector2(nozzle.X, r.Position.Y + 4), nozzle, new Color("#8b949e"), 1f);
                ci.Circle(nozzle, 1.6f, new Color("#ffb070").WithAlpha(on ? 1f : 0.3f), true, -1f, true);
                break;
            }
            case FurnitureType.Workbench:
            {
                // CNC 공작기: 오가는 가공 머리와 레이저 점
                float x = r.Position.X + 8 + (r.Size.X - 16) * (0.5f + 0.5f * Mathf.Sin(on && m.Active ? t * 2f : 0f));
                ci.DrawLine(new Vector2(r.Position.X + 5, r.Position.Y + 6), new Vector2(r.End.X - 5, r.Position.Y + 6), new Color("#5a6678"), 2f);
                ci.Box(new Rect2(x - 3, r.Position.Y + 4, 6, 6), new Color("#9fb4cc"));
                if (on && m.Active) ci.Circle(new Vector2(x, r.Position.Y + 14), 1.5f, new Color("#ff5a5a"), true, -1f, true);
                break;
            }
            case FurnitureType.CoolantPump when m.Tier >= 3:
                ci.Arc(c, Mathf.Min(r.Size.X, r.Size.Y) * 0.42f, 0f, Mathf.Tau, 28, new Color("#b8f0ff").WithAlpha((0.35f + 0.2f * Mathf.Sin(t * 2f)) * a), 2f, true);
                break;
            case FurnitureType.CoolantPump:
            {
                // 고압 펌프: 압력계
                var g = new Vector2(r.Position.X + 8, r.Position.Y + 8);
                ci.Circle(g, 4.5f, new Color("#e8e8e8"), true, -1f, true);
                float needle = on ? -2.2f + 1.6f * (0.7f + 0.1f * Mathf.Sin(t * 5f)) : -2.4f;
                ci.DrawLine(g, g + Vector2.FromAngle(needle) * 3.5f, new Color("#c0392b"), 1.2f, true);
                break;
            }
            case FurnitureType.Refinery:
            {
                // 플라스마 정련로: 보랏빛 아크
                if (!on) break;
                var p0 = new Vector2(r.Position.X + 8, c.Y);
                var p1 = new Vector2(r.End.X - 8, c.Y);
                var prev = p0;
                for (int k = 1; k <= 6; k++)
                {
                    var p = p0.Lerp(p1, k / 6f) + new Vector2(0, (Hash(f.Id, k, (int)(t * 12f)) - 0.5f) * 8f);
                    ci.DrawLine(prev, p, new Color("#d69cff").WithAlpha(0.9f), 1.5f, true);
                    prev = p;
                }
                break;
            }
            case FurnitureType.AuxGenerator:
            {
                // 연료전지: 쌓인 셀
                for (int k = 0; k < 5; k++)
                    ci.Box(new Rect2(r.Position.X + r.Size.X * 0.35f + k * 5, r.Position.Y + 6, 3, r.Size.Y - 12),
                        new Color("#4aa3ff").WithAlpha((_world.Power.AuxRunning ? 0.7f : 0.25f) * (0.7f + 0.3f * Mathf.Sin(t * 3f + k))));
                break;
            }
            case FurnitureType.MainComputer:
            {
                // 분산 제어: 깜박이는 노드 격자
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        bool blink = Hash(f.Id, x + y * 4, (int)(t * 3f)) > 0.5f;
                        ci.Circle(new Vector2(r.Position.X + 8 + x * (r.Size.X - 16) / 3f, r.Position.Y + 8 + y * (r.Size.Y - 16) / 2f), 1.4f,
                            (blink && on ? new Color("#7fffd4") : new Color("#24403a")), true, -1f, true);
                    }
                break;
            }
            case FurnitureType.SensorArray:
            {
                // 위상 배열: 반짝이는 소자판
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        float ph = Mathf.Sin(t * 4f - (x + y) * 0.9f);
                        ci.Box(new Rect2(r.Position.X + 6 + x * 6, r.Position.Y + 6 + y * 6, 4, 4), new Color("#7fb2ff").WithAlpha((0.3f + 0.5f * Mathf.Max(0f, ph)) * a));
                    }
                break;
            }
            case FurnitureType.EngineCore:
            {
                // 이온 추진기: 푸른 이온 빛 (연소할 때 세게)
                bool burn = _world.Propulsion.Burning || _world.Propulsion.CourseBurnVisible;
                ci.Circle(c, Mathf.Min(r.Size.X, r.Size.Y) * 0.22f, new Color("#6cc8ff").WithAlpha((burn ? 0.7f : 0.2f) * a * (0.8f + 0.2f * Mathf.Sin(t * 9f))), true, -1f, true);
                break;
            }
        }
    }
}
