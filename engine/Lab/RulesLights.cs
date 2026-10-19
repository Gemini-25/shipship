using ShipEngine;

namespace Lab;

/// <summary>야간 조명 규칙 시험 — 주컴퓨터가 밤에 침실 · 복도를 낮추고 일하는 방은 그대로 · 위험한 방은 환하게 (그래픽 결정 8 · docs/GRAPHICS.md).</summary>
public static partial class Program
{
    private static void LightRules()
    {
        if (!Do("야간 조명")) return;
        Console.WriteLine("\n[야간 조명]");

        // ── 판단: 방 종류 · 시각 · 위험으로 조명 몫 ──
        {
            float q = ShipMind.LightPolicy("Quarters", 23, false), c = ShipMind.LightPolicy("Corridor", 2, false), b = ShipMind.LightPolicy("Bridge", 23, false);
            float morning = ShipMind.LightPolicy("Quarters", 6.5, false), smoky = ShipMind.LightPolicy("Corridor", 23, true), day = ShipMind.LightPolicy("Corridor", 14, false);
            Check("야간 조명의 판단: 밤 침실 3% · 복도 30% · 함교 그대로 · 아침 6시 넘으면 환하게 · 위험한 방은 밤에도 환하게 · 낮은 그대로",
                q == ShipMind.NightQuarters && c == ShipMind.NightCommon && b == 1 && morning == 1 && smoky == 1 && day == 1,
                $"침실 23시 {q} · 복도 2시 {c} · 함교 23시 {b} · 침실 6시 반 {morning} · 연기 찬 복도 23시 {smoky} · 복도 14시 {day}");
        }

        // ── 디딤호 0시 20분: 침실은 밤등 · 복도는 낮게 · 함교는 그대로 / 복도에 연기가 차면 10분 안에 환하게 ──
        {
            var s = ShipWorld.Create("Didim", 3);
            var L = s.Layout; var light = s.Get<Light>(); var f = s.Get<Field>();
            s.Run(Time.Minutes(20));
            int Room(string type) => L.Rooms.FindIndex(r => r.Type == type);
            int quarters = Room("Quarters"), corridor = Room("Corridor"), bridge = Room("Bridge");
            float lq = light.Lamp[quarters], lc = light.Lamp[corridor], lb = bridge >= 0 ? light.Lamp[bridge] : 300;
            foreach (int cell in L.Rooms[corridor].Cells) f.AddGas(cell, f.T.gSoot, f.Moles(cell) * 0.0002, f.Temp(cell));   // 200ppm 연기
            s.Run(Time.Minutes(12));
            float lc2 = light.Lamp[corridor];
            Check("디딤호 0시 20분: 침실은 밤등(30럭스 아래) · 복도 낮게(60~120럭스) · 함교 그대로(300) / 복도에 연기가 차면 주컴퓨터가 다시 환하게 (300)",
                lq < 30 && lc is > 60 and < 120 && lb > 290 && lc2 > 290,
                $"침실 {lq:0}럭스 · 복도 {lc:0} · 함교 {lb:0} · 연기 찬 복도 {lc2:0} · 낮춘 방 {s.Get<ShipMind>().LampsDimmed}");
        }
    }
}
