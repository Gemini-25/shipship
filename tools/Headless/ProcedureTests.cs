using System;
using System.Linq;
using ShipSim.Core;

// v12.1 정비 절차 · 설비 전선·관 · 재조립 불량 · 미뤄 둔 정비
public static partial class Program
{
    private static int RunProcedureTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"정비 절차 점검 (v12.1) · 시드 {seed}\n");

        // ── 1) 평시 수리는 절차를 밟고, 시험 운전에서 불량을 잡기도 한다 ──
        {
            int full = 0, skipped = 0, caught = 0, defects = 0;
            for (int i = 0; i < 4; i++)
            {
                var w = DayOne(seed + i * 17, "Mirinae");
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Concat(w.Ship.FurnitureOf(FurnitureType.Fridge)).Concat(w.Ship.FurnitureOf(FurnitureType.GrowBed)))
                    w.Machines.Break(f.Machine!);
                Run(w, SimTime.TicksPerDay * 2);
                full += w.Procs.Full; skipped += w.Procs.Skipped; caught += w.Procs.CaughtByTest; defects += w.Procs.Defects;
            }
            Check("평시 수리 — 절차를 밟는다", full > skipped, $"다 밟음 {full} · 생략 {skipped} · 시험 운전에서 잡음 {caught} · 재발 {defects}");
        }

        // ── 2) 마지막 냉각 펌프: 원자로 출력을 낮추고 고친다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var pumps = w.Ship.FurnitureOf(FurnitureType.CoolantPump).ToList();
            // 다른 펌프가 멈춘 채 원자로가 돌고 있을 때 — 마지막 펌프를 끄려면?
            pumps[0].Machine!.Faults.Add(new Fault { Kind = FaultKind.PumpSeized, Since = w.Tick });
            var plan = Procedures.Plan(w, w.Crew.First(), pumps[1].Machine!, urgent: false);
            var o2 = Procedures.Plan(w, w.Crew.First(), w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!, urgent: true);
            Check("마지막 냉각 펌프 — 원자로를 낮춰야 끈다, 급하면 절차를 건너뛴다", plan.Derate && w.Power.ReactorOnline,
                $"냉각 펌프: {plan.Summary} · 산소 발생기(급함): {o2.Summary}");
        }

        // ── 3) 폭발이 설비 전선·관을 끊고, 사람이 다시 건다 (비상이면 임시로 → 나중에 정식으로) ──
        {
            var w = DayOne(seed, "Mirinae");
            var gen = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!;
            w.Volatile.Blast(gen.Body.UseSpots.First(s => w.Ship.IsOpenFloor(s)), 0.6f, "시험");
            // 이 시험은 전선·관을 다시 잇는지를 본다 — 폭발이 낸 불은 바로 끈다 (불이 크게 번지면 생명유지실이 숨 막혀 다른 이야기가 된다)
            w.Fire.ClearRoom(gen.Body.Room);
            bool Hurt(Machine m) => m.Feed < 0.5f || m.Line < 0.5f && Procedures.Plumbed(m.Body.Type); // 일감이 붙는 기준과 같게
            int cut = w.Ship.Machines.Count(Hurt);
            Console.WriteLine("    " + string.Join(", ", w.Ship.Machines.OrderBy(m => (m.Body.Center - gen.Body.Center).LengthSquared()).Take(5).Select(m => $"{m.Name} d={(m.Body.Center - gen.Body.Center).Length():0.0} 전선 {m.Feed * 100:0}% 관 {m.Line * 100:0}%")));
            Run(w, SimTime.Hours(20));
            int left = w.Ship.Machines.Count(Hurt);
            Check("설비 전선·관 — 폭발에 끊기고 다시 잇는다", cut > 0 && left < cut && w.Procs.Rewired + w.Procs.Spliced + w.Procs.Relined > 0,
                $"끊긴 설비 {cut} → {left} · {w.Procs}");
        }

        // ── 4) 미뤄 둔 정비: 큰 사고 뒤 남은 임시 복구 ──
        {
            var w = DayOne(seed, "Mirinae");
            Scenarios.Apply(w, "chaos", out _);
            // 사고 뒤 하루 동안 한 번이라도 모이는지 (v13.1 조 편성으로 빨리 정식 수리까지 끝나기도 한다)
            var items = Deferred.Items(w);
            for (int h = 0; h < 24; h++)
            {
                Run(w, SimTime.Hours(1));
                var now = Deferred.Items(w);
                if (now.Count > items.Count) items = now;
            }
            Check("미뤄 둔 정비 — 사고 뒤에 남은 일이 모인다", items.Count > 0, string.Join(" / ", items.Take(6).Select(x => $"{x.What}({x.Why})")));
        }

        // ── 5) 결정론 ──
        {
            uint H() { var w = DayOne(seed, "Mirinae"); Scenarios.Apply(w, "chaos", out _); Run(w, SimTime.Hours(20)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("절차가 든 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 정비 절차 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
