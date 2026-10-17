using ShipEngine;

namespace Lab;

/// <summary>⑦ 생명 — 실제 수치 시험 (NASA BVAD 2018 · Compendium of Physical Activities 2011). docs/DEPTH.md</summary>
public static partial class Program
{
    private static void LifeRules()
    {
        if (!Do("대사")) return;
        Console.WriteLine("\n[⑦ 대사 — 실제 수치]");
        var lay = Worlds.Ascii(Worlds.Symmetric);
        Sim Room(out Life life)
        {
            var s = new Sim(new SimSetup(lay, 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
            s.Get<Field>().FillAir();
            life = s.Get<Life>();
            return s;
        }
        // 공조 대신: 방 공기 · 겉면을 22℃로 붙든다 (실제 배는 공조가 지킨다 — 이 시험 방에는 공조가 없어 하루에 12℃까지 식었다)
        void Hvac(Sim s)
        {
            if (s.Tick % 25 != 0) return;
            var f = s.Get<Field>();
            for (int c = 0; c < lay.N; c++)
                if (lay.IsSpace(c)) { f.AddHeat(c, (295.15 - f.Temp(c)) * f.GasC(c)); f.Ts[c] = 295.15; f.Tf[c] = 295.15; }
        }

        // ── 보통 하루: 잠 8시간 · 일 8시간 · 쉼 6시간 · 걷기 2시간 → BVAD 산소 0.82kg · 이산화탄소 1.04kg · 대사 11.8MJ (±15%) ──
        {
            var s = Room(out var life);
            int b = life.AddBody(Worlds.At(lay, 5, 3));
            (double hours, float w, bool sleep)[] day = { (8, 77, true), (8, 186, false), (6, 105, false), (2, 284, false) };
            foreach (var (h, w, sleep) in day)
                for (long t = 0; t < Time.Hours(h); t++)
                {
                    life[b].ActivityW = w; life[b].Asleep = (byte)(sleep ? 1 : 0);
                    // 먹고 마시는 것은 따로 (이 시험은 숨만) — 양분이 바닥나지 않게
                    if (life[b].Reserve < 1) life.Eat(b, 1.15);
                    if (life[b].Water < Mind.BodyWater0) life.Drink(b, 5);
                    Hvac(s);
                    s.Step();
                    if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && t % 1500 == 0)
                        Console.WriteLine($"    {w}W{(sleep ? " 잠" : "")} · 체온 {life[b].CoreK - 273.15:0.00}℃ · 공기 {s.Get<Field>().Temp(life[b].Cell) - 273.15:0.0}℃ · 대사 {life.MetabolicJ / 1e6:0.0}MJ");
                }
            double o2 = life.O2Used * 0.032, co2 = life.CO2Made * 0.044, mj = life.MetabolicJ / 1e6;
            Check("대사: 보통 하루 — 산소 0.82kg · 이산화탄소 1.04kg · 에너지 11.8MJ (NASA BVAD, ±15%)",
                Math.Abs(o2 / 0.82 - 1) < 0.15 && Math.Abs(co2 / 1.04 - 1) < 0.15 && Math.Abs(mj / 11.8 - 1) < 0.15,
                $"산소 {o2:0.000}kg · 이산화탄소 {co2:0.000}kg · 호흡 몫 {life.CO2Made / life.O2Used:0.00} · {mj:0.0}MJ · 오줌 {life.UrineMade * 0.018:0.00}L");
        }

        // ── 연료: 쉴 때는 지방을 많이(호흡 몫 0.8 언저리) · 힘들수록 탄수화물을(0.9 넘게) ──
        {
            double Rq(float w)
            {
                var s = Room(out var life);
                int b = life.AddBody(Worlds.At(lay, 5, 3));
                for (long t = 0; t < Time.Minutes(30); t++) { life[b].ActivityW = w; Hvac(s); s.Step(); }
                return life.CO2Made / life.O2Used;
            }
            double rest = Rq(105), hard = Rq(487);
            Check("대사: 쉴 때 호흡 몫 0.75~0.85 (지방) · 힘든 일 0.88 넘게 (탄수화물)", rest is > 0.75 and < 0.85 && hard > 0.88,
                $"쉼 {rest:0.00} · 힘든 일 {hard:0.00}");
        }

        // ── 밤 오줌: 자는 동안 콩팥이 오줌을 덜 만든다 (항이뇨 호르몬 — 밤 오줌은 하루의 20% 아래, ICS 2002) ──
        {
            double Urine(bool asleep)
            {
                var s = Room(out var life);
                int b = life.AddBody(Worlds.At(lay, 5, 3));
                for (long t = 0; t < Time.Hours(4); t++)
                {
                    life[b].ActivityW = asleep ? 77 : 105; life[b].Asleep = (byte)(asleep ? 1 : 0);
                    if (life[b].Water < Life.UrineRefWater) life.Drink(b, Life.UrineRefWater - life[b].Water);
                    Hvac(s); s.Step();
                }
                return life.UrineMade;
            }
            double night = Urine(true), day = Urine(false);
            Check("대사: 자는 동안 오줌은 낮의 절반쯤 (밤에 목말라 깨지 않게)", night > 0 && night / day is > 0.35 and < 0.65,
                $"네 시간 오줌 — 낮 {day * 0.018:0.00}L · 잘 때 {night * 0.018:0.00}L ({night / day:0.00}배)");
        }

        // ── 남는 양분은 지방으로 · 굶으면 지방을 태운다 ──
        {
            var s = Room(out var life);
            int b = life.AddBody(Worlds.At(lay, 5, 3));
            double fat0 = life[b].Fat;
            life.Eat(b, 8);                                   // 큰 잔치 (22MJ)
            for (long t = 0; t < Time.Hours(6); t++) { life[b].ActivityW = 105; Hvac(s); s.Step(); }
            double fatFeast = life[b].Fat, glyFeast = life[b].Reserve;
            for (long t = 0; t < Time.Hours(30); t++) { life[b].ActivityW = 105; if (life[b].Water < Mind.BodyWater0) life.Drink(b, 5); Hvac(s); s.Step(); }
            double fatFast = life[b].Fat;
            Check("대사: 큰 잔치 뒤 남는 양분은 몇 시간에 걸쳐 지방으로 쌓이고 · 굶으면 지방을 태운다",
                fatFeast > fat0 + 0.05 && glyFeast <= Life.GlycogenMax + 1.5 && fatFast < fatFeast - 0.2,
                $"지방 {fat0 * 0.256:0.000}kg → 잔치 뒤 {fatFeast * 0.256:0.000}kg (글리코겐 {glyFeast:0.0}몰) → 30시간 굶은 뒤 {fatFast * 0.256:0.000}kg");
        }
    }
}
