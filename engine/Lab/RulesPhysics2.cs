using ShipEngine;

namespace Lab;

/// <summary>물리 세계 뒤 커밋 (docs/PHYSICS.md §11 PH11~PH16) — 우주의 물체 · 물체 볼거리 · 판단 · 몸 물체 · 판 조각.</summary>
public static partial class Program
{
    private static void Physics2Rules()
    {
        // ── PH12 물체 볼거리: 카메라 안에서 컵이 깨지면 그 틱들을 천천히 (화면 1초에 1틱) — 하루 느린 예산 안 ──
        if (Do("물체 볼거리"))
        {
            Console.WriteLine("\n[물체 볼거리 — 깨지는 틱은 천천히]");
            var t = Tables.Default;
            int cup = t.People.Items.Keys.ToList().IndexOf("컵");
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery" };
            var lay = Worlds.Ascii(BreakShip, legend);
            Sim? s = null; Director? d = null; Fact fact = default; double before = 0, after = 0; long at = -1;
            for (ulong seed = 1; seed <= 10 && fact.Kind != FactKind.Shattered; seed++)
            {
                s = Worlds.Full(lay, seed);
                d = new Director(s.Layout) { KeepRing = false, DayMinutes = 3 };
                for (int k = 0; k < 5; k++) { s.Step(); d.Observe(s); }
                d.FocusCell = Worlds.At(lay, 8, 2);
                s.Get<Structure>().PutItem(cup, 0, Worlds.At(lay, 8, 1), 0.75f, 11);
                for (int k = 0; k < 6; k++)
                {
                    double sc = d.ScreenSeconds;
                    s.Step(); d.Observe(s);
                    if (fact.Kind != FactKind.Shattered) { fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Shattered); if (fact.Kind == FactKind.Shattered) { at = s.Tick; before = sc; } }
                }
                after = d.ScreenSeconds;
            }
            double shown = after - before;
            Check("물체 볼거리: 카메라 안에서 0.75m 떨어진 컵이 깨지면 그 뒤 틱들을 천천히 — 화면 2초 넘게 (조용함 빠르기면 0.1초 남짓 · 많아야 3틱 · 깨졌다의 무게 8)",
                fact.Kind == FactKind.Shattered && shown >= 2 && d!.PropScreenSec is >= 2 and <= 3.5 && Director.Weight(FactKind.Shattered) == 8,
                $"깨짐 {(fact.Kind == FactKind.Shattered ? $"틱 {fact.Tick}" : "없음")} · 그 뒤 화면 {shown:0.00}초 · 볼거리 {d?.PropScreenSec:0.00}초");
        }
    }
}
