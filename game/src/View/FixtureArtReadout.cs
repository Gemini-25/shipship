using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.24 가까이: 계기 숫자 — 계기가 달린 설비는 가까이 보면 작은 표시창에 지금 값이 뜬다 (실제 상태를 읽기만).
/// 콘솔 · 항법 · 주 컴퓨터 → 배터리 % · 산소 발생기 · 정화기 → 그 방 산소 kPa · 열교환 · 냉각 → 그 방 온도 ·
/// 원자로 · 엔진 · 발전기 → 효율 % · 배전반 → 지금 쓰는 전력 kW · 배터리 → 충전 % · 그 밖 계기 → 상태 %.
/// 표시창 색: 정상 녹색 · 주의 호박색 · 위험 빨강 · 꺼지면 어둡다.
/// </summary>
public static partial class FixtureArt
{
    private static readonly Color DigitOk = new("#7dffa8");
    private static readonly Color DigitWarn = new("#ffc35a");
    private static readonly Color DigitBad = new("#ff6b6b");

    /// <summary>표시창이 있는 설비만 (나머지는 null).</summary>
    public static (string text, int level)? Readout(Furniture f, World w)
    {
        var m = f.Machine;
        float bat = w.Power.BatteryCapacity > 0f ? w.Power.BatteryCharge / w.Power.BatteryCapacity : 0f;
        var air = f.Room.Air;
        int Lv(float v, float warn, float bad, bool low = true) => low ? (v <= bad ? 2 : v <= warn ? 1 : 0) : (v >= bad ? 2 : v >= warn ? 1 : 0);
        switch (f.Type)
        {
            case FurnitureType.Console or FurnitureType.NavComputer or FurnitureType.MainComputer or FurnitureType.ReactorSimulator:
                return ($"{bat * 100f:0}%", Lv(bat, 0.3f, 0.12f));
            case FurnitureType.Battery or FurnitureType.CapacitorBank:
                return ($"{bat * 100f:0}%", Lv(bat, 0.3f, 0.12f));
            case FurnitureType.OxygenGenerator or FurnitureType.Scrubber or FurnitureType.AirPurifier or FurnitureType.AirlockPump:
                return ($"{air.O2:0.0}", Lv(air.O2, 19f, 16f));
            case FurnitureType.HeatExchanger or FurnitureType.CoolantPump or FurnitureType.Dehumidifier:
                return ($"{air.Temperature:0}°", Lv(air.Temperature, 30f, 38f, low: false));
            case FurnitureType.PowerPanel or FurnitureType.SurgeProtector:
                return ($"{w.Power.Demand:0}kW", Lv(bat, 0.3f, 0.12f));
            case FurnitureType.ReactorCore or FurnitureType.EngineCore or FurnitureType.AuxGenerator or FurnitureType.WaterRecycler
                or FurnitureType.VibrationMonitor or FurnitureType.LeakDetector or FurnitureType.DiagnosticScanner or FurnitureType.SensorArray:
                return m == null ? null : ($"{m.Efficiency * 100f:0}%", Lv(m.Efficiency, 0.6f, 0.3f));
        }
        return null;
    }

    /// <summary>표시창이 무엇을 보여 주나 (카드에 까닭으로).</summary>
    public static string ReadoutWhat(Furniture f) => f.Type switch
    {
        FurnitureType.Console or FurnitureType.NavComputer or FurnitureType.MainComputer or FurnitureType.ReactorSimulator
            or FurnitureType.Battery or FurnitureType.CapacitorBank => "배 전체 배터리",
        FurnitureType.OxygenGenerator or FurnitureType.Scrubber or FurnitureType.AirPurifier or FurnitureType.AirlockPump => "이 방 산소 (kPa)",
        FurnitureType.HeatExchanger or FurnitureType.CoolantPump or FurnitureType.Dehumidifier => "이 방 온도",
        FurnitureType.PowerPanel or FurnitureType.SurgeProtector => "지금 쓰는 전력",
        _ => "효율",
    };

    /// <summary>표시창 한 장 (설비 앞쪽 가장자리). 가까이에서만 부른다.</summary>
    public static void PaintReadout(CanvasItem ci, Furniture f, World w, float t)
    {
        if (Readout(f, w) is not (string text, int level)) return;
        var m = f.Machine;
        bool lit = m == null || !m.Stopped && f.Room.Powered;
        var r = ShipView.FurnitureRect(f).Grow(-3f);
        const int size = 5;
        float tw = Gfx.Width(Fonts.Bold, text, size) + 4f;
        var plate = new Rect2(r.Position.X + 2f, r.End.Y - 9f, tw, 7f);
        ci.DrawRect(plate.Grow(0.6f), new Color("#0a0d12"));
        ci.DrawRect(plate, new Color("#0f1a14"));
        if (!lit) return;
        var col = level == 2 ? DigitBad : level == 1 ? DigitWarn : DigitOk;
        float blink = level == 2 ? 0.55f + 0.45f * Mathf.Sin(t * 6f) : 1f;
        ci.DrawRect(plate, col.WithAlpha(0.08f * blink));
        Gfx.Text(ci, Fonts.Bold, new Vector2(plate.Position.X + 2f, plate.End.Y - 1.2f), text, size, col.WithAlpha(0.95f * blink));
    }

    /// <summary>
    /// 가까이: 잔 낡은 자국 — 긁힌 줄 · 손때 · 찍힌 자국 · 손본 자리의 새 나사 (정적 디테일 층 · 가까이서만).
    /// 멀리 · 중간에선 몸체 위의 굵은 낡음(Weathering)만 보인다.
    /// </summary>
    private static void WearClose(in Fix x)
    {
        if (x.M is not Machine m || !ZoomDetail.Draws((ZoomTier)x.Lod).HasFlag(Detail.Wear)) return;
        int ws = WearStep(m), ss = ScarStep(m);
        var ci = x.Ci;
        var b = x.B;
        // 긁힌 줄: 낡을수록 많다 (손이 닿는 앞쪽에 몰린다)
        for (int i = 0; i < ws * 3; i++)
        {
            float u = 0.15f + 0.7f * Hash(x.Id, i, 611), v = 0.55f + 0.4f * Hash(x.Id, i, 612);
            var a = x.P(u, v);
            var d = (x.U * (Hash(x.Id, i, 613) - 0.5f) + x.V * (Hash(x.Id, i, 614) - 0.5f) * 0.4f) * x.Px(7f);
            Line(ci, a, a + d, new Color(0.85f, 0.88f, 0.92f, 0.10f + 0.04f * ws), 0.5f);
        }
        // 손때: 손잡이 · 자판 자리에 번들거리는 얼룩
        if (ws >= 2)
            for (int i = 0; i < 2; i++)
                Dot(ci, x.P(0.3f + 0.4f * i, 0.85f), x.Px(2.2f), new Color(0.6f, 0.55f, 0.45f, 0.08f + 0.03f * ws));
        // 찍힌 자국: 상처가 깊을수록
        for (int i = 0; i < ss; i++)
        {
            var c = x.P(0.1f + 0.8f * Hash(x.Id, i, 621), 0.1f + 0.8f * Hash(x.Id, i, 622));
            ci.DrawArc(c, x.Px(1.6f), 0.4f, 2.6f, 6, new Color(0f, 0f, 0f, 0.35f), 0.7f, true);
            ci.DrawArc(c, x.Px(1.6f), 3.5f, 5.6f, 6, new Color(1f, 1f, 1f, 0.12f), 0.5f, true);
        }
        // 손본 자리: 고쳐 쓴 설비는 한 귀퉁이 나사만 새것 (반짝)
        if (m.Condition < 0.9f && ss <= 2 && b.Size.X > 10f)
        {
            var c = new Vector2(b.End.X - x.Px(3f), b.Position.Y + x.Px(3f));
            Dot(ci, c, x.Px(1.1f), new Color("#d9dde3"));
            Line(ci, c - new Vector2(x.Px(0.7f), 0f), c + new Vector2(x.Px(0.7f), 0f), new Color("#6d747e"), 0.4f);
        }
    }
}
