using ShipEngine;

namespace Lab;

/// <summary>법칙 시험 — 사고의 물리 (docs/DEPTH_FIX.md DF9 · 근거 docs/ENGINE_DEPTH_REVIEW.md §9 · §4-14): 운석 운동량 · 구멍 크기 축척 · 충격파 연속 · 큰 충돌의 갈라짐.</summary>
public static partial class Program
{
    /// <summary>탁자가 고르게 놓인 큰 방 (충격파가 부순 설비를 센다).</summary>
    internal const string TableHall = @"
#####################
#...................#
#.t.t.t.t.t.t.t.t.t.#
#...................#
#.t.t.t.t.t.t.t.t.t.#
#...................#
#.t.t.t.t.t.t.t.t.t.#
#...................#
#.t.t.t.t.t.t.t.t.t.#
#...................#
#####################";

    private static void Depth2Laws()
    {
        if (!Do("사고의 물리 법칙")) return;
        Console.WriteLine("\n[사고의 물리 — 운석 운동량 · 구멍 축척 · 충격파 연속 · 갈라짐]");

        // 운석 운동량: 작은 배에 2kg · 7km/s — 맞은 순간 배의 몸 Δv = m·v / M (운석이 넣은 운동량 장부도 같다)
        {
            var lay = Worlds.Ascii(Twins);
            var s = FieldStructure(lay);
            s.Run(2);
            var st = s.Get<Structure>();
            int hull = Worlds.At(lay, 2, 0);
            st.Rock(s, hull, 2, 0.5 * 2 * 7000 * 7000, 0, 1, 0, 1, s.Causes.Add(CauseKind.Impact, "2kg 운석", s.Tick, hull));
            var o = st.Bodies.Count > 0 ? st.Bodies[0] : default;
            double want = 2 * 7000 / Math.Max(1, o.Mass);
            Check("운석 운동량: 작은 배 (10×4칸)에 2kg · 7km/s가 위에서 — 맞은 순간 배의 Δv = m·v / M (1% 안 · 날아온 쪽으로) · 운석 장부 14,000N·s",
                o.Mass > 0 && Math.Abs(o.Vy - want) < 0.01 * want && Math.Abs(o.Vx) < 1e-6 && Math.Abs(st.RockPy - 14000) < 1,
                $"배 {o.Mass / 1000:0.0}t · Δv ({o.Vx * 1000:0.0}, {o.Vy * 1000:0.0})mm/s · 바람 {want * 1000:0.0}mm/s · 장부 {st.RockPy:0}N·s");
        }

        // 구멍 크기 축척: 강철 10mm 외판 · 7km/s — 얇은 판 관통 어림 (5g 20 · 60g 49 · 2kg 230cm²)의 두 배 안 · 지름 비가 m^⅓ 언저리 (예전: 44 · 340 · 5651cm² — 무게에 거의 비례)
        {
            var lay = Worlds.Ascii(Twins);
            double Hole(double kg)
            {
                var s = FieldStructure(lay);
                int hull = Worlds.At(lay, 2, 0);
                s.Get<Structure>().Rock(s, hull, kg, 0.5 * kg * 7000 * 7000, 0, 1, 0, 1, 0);
                return s.Get<Field>().Hole[hull];
            }
            double a5 = Hole(0.005), a60 = Hole(0.06), a2k = Hole(2);
            double r1 = Math.Sqrt(a60 / a5) / Math.Cbrt(60 / 5.0), r2 = Math.Sqrt(a2k / a60) / Math.Cbrt(2000 / 60.0);
            bool near(double a, double want) => a > want / 2 && a < want * 2;
            Check("구멍 크기 축척: 강철 10mm 외판 · 7km/s — 5g · 60g · 2kg의 구멍이 얇은 판 관통 어림 (20 · 49 · 230cm²)의 두 배 안 · 지름 비 ÷ 무게 비의 세제곱근이 0.6~1.4",
                near(a5 * 1e4, 20) && near(a60 * 1e4, 49) && near(a2k * 1e4, 230) && r1 > 0.6 && r1 < 1.4 && r2 > 0.6 && r2 < 1.4,
                $"5g {a5 * 1e4:0}cm² · 60g {a60 * 1e4:0}cm² · 2kg {a2k * 1e4:0}cm² · 지름 비 / m^⅓ 비 {r1:0.00} · {r2:0.00}");
        }

        // 충격파 연속: 같은 방 · 같은 자리에 7km/s 운석 30 · 50 · 80MJ — 부서진 탁자가 에너지와 함께 고르게 는다 (예전: 20MJ 문턱의 절벽 — 49MJ 1 · 81MJ 7)
        {
            var lay = Worlds.Ascii(TableHall, new() { ['t'] = "Table" });
            int Smashed(double mj)
            {
                var s = Worlds.Full(lay, 1);
                s.Run(2);
                var st = s.Get<Structure>();
                double kg = 2 * mj * 1e6 / (7000.0 * 7000);
                st.Rock(s, Worlds.At(lay, 10, 0), kg, mj * 1e6, 0, 1, 0, 1, s.Causes.Add(CauseKind.Impact, $"{kg:0.0}kg 운석", s.Tick));
                return Enumerable.Range(0, lay.Furniture.Count).Count(f => st.FurnBroken[f] != 0 && s.Causes.Text(st.FurnCause[f]).StartsWith("충격파에"));
            }
            int s30 = Smashed(30), s50 = Smashed(50), s80 = Smashed(80);
            Check("충격파 연속: 탁자 36개 방 외판에 7km/s 운석 30 · 50 · 80MJ — 충격파에 부서진 설비가 고르게 는다 (30 < 50 < 80 · 50→80이 30→50의 세 배를 넘지 않는다 — 절벽이 없다)",
                s30 < s50 && s50 < s80 && s80 - s50 <= 3 * (s50 - s30), $"30MJ {s30} · 50MJ {s50} · 80MJ {s80}");
        }

        // 큰 충돌의 갈라짐: 두 방을 잇는 좁은 통로를 20kg · 8km/s (640MJ) 운석이 위에서 — 배가 둘로 갈라진다 (예전: 640MJ로는 조각 0 · 5GJ가 들어야 했다)
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = FieldStructure(lay);
            s.Run(2);
            var st = s.Get<Structure>();
            st.Rock(s, Worlds.At(lay, 10, 1), 20, 640e6, 0, 1, 0, 1, s.Causes.Add(CauseKind.Impact, "20kg 운석", s.Tick));
            s.Run(5);
            var split = s.Facts.Since(0).FirstOrDefault(f => f.Kind == FactKind.ShipSplit);
            Check("큰 충돌의 갈라짐: 두 방을 잇는 3칸 통로를 20kg · 8km/s (640MJ)가 위에서 꿰뚫으면 — 충격파가 통로의 위아래 벽 · 바닥을 찢어 배가 둘로 갈라진다 (원인 사슬이 그 운석까지)",
                st.Splits >= 1 && split.Kind == FactKind.ShipSplit && s.Causes.Story(split.Cause).Contains("20kg 운석"),
                $"갈라짐 {st.Splits} · '{(split.Kind == FactKind.ShipSplit ? s.Causes.Story(split.Cause) : "")}'");
        }
    }
}
