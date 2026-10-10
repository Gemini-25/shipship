using System;
using System.Linq;
using ShipSim.Core;

// v12.6 배 설계: 방 70종 · 범례 · 인접성 · 방사선 대피 · 격리 · 절차 생성 배
public static partial class Program
{
    private static int RunDesignTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"배 설계 점검 (v12.6) · 시드 {seed}\n");
        try
        {
            // 1) 방 표: 70종 넘게, 이름·색·설명서가 다 있다
            {
                int kinds = Enum.GetValues<RoomType>().Length;
                bool named = Enum.GetValues<RoomType>().All(t => RoomTypes.Name(t) != t.ToString());
                bool codex = Enum.GetValues<RoomType>().All(t => Codex.Of(t) != null);
                Check("방 종류 70종 — 이름과 설명서가 다 있다", kinds >= 70 && named && codex, $"{kinds}종 · 이름 {(named ? "다 있음" : "빠짐")} · 설명서 {(codex ? "다 있음" : "빠짐")}");
            }
            // 2) 절차 생성 배: 크기마다 여러 시드로 — 만들어지고, 하루를 무사히 보낸다
            {
                int built = 0, ok = 0, total = 0;
                string sample = "";
                foreach (int n in new[] { 4, 6, 12, 20, 30 })
                    foreach (int s in new[] { seed, seed + 1, seed + 2 })
                    {
                        total++;
                        var key = ShipGenerator.KeyFor(n, s);
                        ShipTemplate tpl;
                        try { tpl = ShipCatalog.Find(key)!; built++; }
                        catch (Exception e) { Console.WriteLine($"   {key}: 못 만듦 — {e.Message}"); continue; }
                        var w = World.CreateDefault(s, 0, key);
                        Run(w, SimTime.TicksPerDay);
                        int alive = w.Crew.Count(c => !c.Dead);
                        int special = w.Ship.Rooms.Count(r => r.Special != null);
                        bool good = alive == w.StartCrew && w.Power.ReactorOnline && special >= 2;
                        if (good) ok++;
                        Console.WriteLine($"   {key,-12} {tpl.Name} · {w.Ship.Grid.Width}×{w.Ship.Grid.Height} · 방 {w.Ship.Rooms.Count}(새 방 {special}) · 생존 {alive}/{w.StartCrew} · 원자로 {(w.Power.ReactorOnline ? "돎" : "멈춤")}{(good ? "" : "  ←")}");
                        if (n == 12 && s == seed) sample = tpl.Ascii;
                    }
                Check("절차 생성 배 — 모두 만들어지고 하루를 무사히", built == total && ok >= total - 1, $"만듦 {built}/{total} · 무사 {ok}/{total}");
                bool same = ShipGenerator.Template(12, seed).Ascii == ShipGenerator.Template(12, seed).Ascii;
                Check("같은 키는 같은 배 (저장·재생)", same, "");
                if (args_Print) Console.WriteLine(sample);
            }
            // 3) 범례: 새 방은 본래 방의 기능을 이어받는다
            {
                var w = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(12, seed));
                var sp = w.Ship.Rooms.Where(r => r.Special != null).ToList();
                bool inherit = sp.All(r => r.Type == RoomCatalog.BaseOf(r.Kind));
                Check("범례 — 새 방은 이름·색은 제 것, 기능은 본래 방", sp.Count > 0 && inherit, string.Join(", ", sp.Select(r => $"{r.Name}({RoomTypes.Name(r.Type)})")));
            }
            // 4) 인접성: 엔진실·냉각실 옆은 시끄럽고, 그 옆에서 자면 덜 쉰다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                Run(w, SimTime.Hours(2));
                var loud = w.Ship.Rooms.Where(r => r.Noise > 0.15f).OrderByDescending(r => r.Noise).Take(4).ToList();
                var quiet = w.Ship.Rooms.Where(r => r.Type == RoomType.Quarters).Select(r => AmbienceSystem.SleepFactor(r)).DefaultIfEmpty(1f).Min();
                Check("인접성 — 시끄러운 방이 있고 옆방으로 번진다", loud.Count >= 2 && w.Ship.Rooms.Any(r => r.Noise > 0.05f && RoomCatalog.Emits(r.Kind).noise == 0f),
                    string.Join(" · ", loud.Select(r => $"{r.Name} {r.Noise * 100:0}%")) + $" · 침실 잠의 질 {quiet * 100:0}%");
            }
            // 5) 태양 폭풍: 대피소가 있는 배는 덜 쬔다 (없으면 창고 선반 뒤 — 겸용의 대가)
            {
                float Dose(string key)
                {
                    var w = World.CreateDefault(seed, 0, key);
                    Run(w, SimTime.Hours(1));
                    Hazards.Apply(w, HazardKind.SolarStorm, default, -1);
                    typeof(HazardSystem).GetProperty("StormPeak")!.SetValue(w.Hazards, 0.7f); // 통합8 폭풍마다 세기가 다르다 (v16.26) — 두 배를 같은 세기로 견준다
                    Run(w, SimTime.Hours(1));
                    if (args_Print) Console.WriteLine($"   {key}: " + string.Join(", ", w.Crew.Select(c => $"{c.Name}@{c.Room?.Name}({c.Room?.Radiation:0.00}) {c.Job?.Label}")));
                    Run(w, SimTime.Hours(3));
                    return w.Crew.Where(c => !c.Dead).Average(c => c.Dose);
                }
                string gen = ShipGenerator.KeyFor(12, seed);
                float withShelter = Dose(gen), without = Dose("Kestrel"); // 통합8 한빛호에도 대피소가 생겼다 — 대피소 없는 배는 제비호
                var w2 = World.CreateDefault(seed, 0, gen);
                bool has = w2.Ship.KindOf(RoomType.Shelter).Any();
                Check("태양 폭풍 — 대피소가 있으면 덜 쬔다", has && withShelter < without, $"대피소 있는 배 평균 {withShelter:0.00} Sv · 없는 제비호 {without:0.00} Sv");
            }
            // 6) 격리실: 격리실이 있으면 덜 옮는다
            {
                // v16.22 새 설계 한빛호도 격리실을 단다 — 같은 배에서 격리실을 닫아 둔 것과 견준다 (배 생김새가 같아야 견줄 수 있다)
                int Infections(string key, bool closeWard = false)
                {
                    int sum = 0;
                    for (int k = 0; k < 2; k++)
                    {
                        var w = World.CreateDefault(seed + k, 0, key);
                        if (closeWard) foreach (var r in w.Ship.Rooms.Where(r => r.Kind is RoomType.Quarantine or RoomType.QuarantineLock)) r.Special = RoomType.Storage; // 격리실을 창고로 돌려 쓴 배
                        Run(w, SimTime.Hours(1));
                        Hazards.Apply(w, HazardKind.Epidemic, default, w.Crew.First(c => c.CanAct).Id);
                        Run(w, SimTime.TicksPerDay * 6);
                        sum += w.Disease.Stats.Infections;
                    }
                    return sum;
                }
                int a = Infections("Hanbit"), b = Infections("Hanbit", closeWard: true);
                Check("격리실 — 열이 나면 격리실로 가서 덜 옮긴다", a < b, $"격리실 연 한빛호 감염 {a} · 격리실 닫은 한빛호 {b} (두 번씩)");
            }
            // 6b) 구획: 큰 배는 통로가 격벽 문으로 나뉜다 — 한 구획 통로가 뚫려도 다른 구획은 기압을 지킨다
            {
                var w = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(30, seed));
                Run(w, SimTime.Hours(1));
                int comps = w.Ship.Compartments, bulk = w.Ship.Doors.Count(d => d.Bulkhead);
                var corr = w.Ship.Rooms.Where(r => r.Type == RoomType.Corridor).OrderBy(r => r.Center.X).ToList();
                var hit = corr.First();
                Incidents.Meteor(w, Scenarios.OuterTarget(w, hit), 0.9f);
                float minOther = 101f;
                for (int i = 0; i < 12; i++) { Run(w, SimTime.Minutes(5)); minOther = Math.Min(minOther, corr.Where(r => r.Compartment != hit.Compartment).Select(r => r.Air.Pressure).DefaultIfEmpty(101f).Min()); }
                Check("구획 — 격벽 문이 통로를 나누고, 다른 구획 통로는 기압을 지킨다", comps >= 2 && bulk >= 2 && minOther > 80f,
                    $"구획 {comps} · 격벽 문 {bulk} · 통로 {corr.Count}토막 · 뚫린 쪽 {hit.Air.Pressure:0}kPa · 다른 구획 통로 최저 {minOther:0}kPa");
            }
            // 6c) 증축·개조: 폭풍을 겪은 배는 회의를 거쳐 창고 하나를 대피소로 고친다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(1));
                Hazards.Apply(w, HazardKind.SolarStorm, default, -1);
                bool done = false;
                for (int d = 0; d < 8 && !done; d++) { Run(w, SimTime.TicksPerDay); done = w.Ship.KindOf(RoomType.Shelter).Any(); }
                var cands = Evolution.Candidates(w).Where(p => p.Kind == UpgradeKind.Repurpose).Select(p => p.Why).ToList();
                Check("개조 — 폭풍을 겪은 배는 창고 하나를 대피소로 고친다", done,
                    done ? $"{w.Ship.KindOf(RoomType.Shelter).First().Name} · 최고 피폭 {w.Crew.Max(c => c.Dose):0.00}Sv" : $"후보 {cands.Count}: {string.Join(" / ", cands)} · 최고 피폭 {w.Crew.Max(c => c.Dose):0.00}Sv");
            }
            // 6d) 외부 설비: 운석이 안테나 곁을 지나면 상해 운석을 늦게 보고, 수리 드론이 먼저 나가 고친다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                Run(w, SimTime.Hours(1));
                var ex = w.Exterior;
                var ant = ex.All.FirstOrDefault(f => f.Kind == ExtKind.Antenna);
                float q0 = w.Sensors.Quality;
                if (ant != null) ex.Damage(ant, 0.6f, "시험");
                float q1 = w.Sensors.Quality;
                Run(w, SimTime.Hours(6));
                Check("외부 설비 — 안테나가 상하면 감지가 흐리고, 드론이 고친다", ex.All.Count >= 2 && ant != null && q1 < q0 && ant.Condition >= 0.99f,
                    $"설비 {ex.All.Count}개 ({string.Join(", ", ex.All.Select(f => f.Name))}) · 감지 {q0 * 100:0}% → {q1 * 100:0}% · 수리 {ex.Repairs}(드론 {ex.DroneRepairs}) · 태양 {ex.SolarNow:0.0}kW");
            }
            // 7) 겸용의 대가: 배마다 기능별로 어느 방이 맡는지
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                var lines = RoomCatalog.Functions.Select(f => { var (r, k) = Facilities.Best(w.Ship, f.Key); return $"{f.Name}: {(r == null ? "없음" : $"{r.Name} {k * 100:0}%")}"; });
                Console.WriteLine("   미리내호 — " + string.Join(" · ", lines));
                Check("겸용의 대가 — 전용 방이 없으면 본래 방이 효율을 깎아 맡는다", Facilities.Best(w.Ship, "exercise").factor is > 0f and < 1f, "");
            }
            // 8) 기반 시설 방: 큰 생성 배는 정수실·배터리실·공조실을 따로 둔다 — 망에 이어지고, 생명유지실 정수기가 다 서도 물이 난다
            {
                var w = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(30, seed));
                var kinds = ShipGenerator.InfraFor(30).ToList();
                bool all = kinds.All(k => w.Ship.Rooms.Any(r => r.Kind == k));
                Run(w, SimTime.Hours(6));
                var plant = w.Ship.Rooms.FirstOrDefault(r => r.Kind == RoomType.WaterPlant);
                var cells = w.Ship.Rooms.FirstOrDefault(r => r.Kind == RoomType.BatteryRoom);
                var hvac = w.Ship.Rooms.FirstOrDefault(r => r.Kind == RoomType.HvacRoom);
                bool linked = plant is { WaterLinked: true, PowerLinked: true } && cells is { PowerLinked: true } && hvac is { PowerLinked: true };
                int roomBatteries = cells == null ? 0 : w.Ship.FurnitureOf(FurnitureType.Battery).Count(f => f.Room == cells);
                // 생명유지실 정수기를 모두 세운다 → 정수실만으로 물이 난다
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Where(f => f.Room.Special == null).ToList()) w.Machines.Break(f.Machine!, FaultKind.PumpSeized);
                Run(w, SimTime.Minutes(10));
                float backup = w.Water.Produced;
                Check("기반 시설 방 — 정수실·배터리실·공조실이 망에 이어져 일한다", all && linked && roomBatteries >= 4 && backup > 0.5f,
                    $"{string.Join(", ", kinds.Select(k => RoomCatalog.Of(k)!.Name))} · 망 {(linked ? "이어짐" : "끊김")} · 배터리실 축전지 {roomBatteries} · 생명유지실 정수기가 다 서도 물 {backup:0.0}L/h");
                int small = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(6, seed)).Ship.Rooms.Count(r => kinds.Contains(r.Kind));
                Check("작은 배는 한 방에 모은다 (기반 시설 방 없음)", small == 0, $"6인 배 기반 시설 방 {small}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }

    private static bool args_Print;

    private static int RunAmbienceDebug(int seed, string key)
    {
        var wd = World.CreateDefault(seed, 0, key);
        Run(wd, SimTime.Hours(3));
        Console.WriteLine($"{key} 환경: " + string.Join(" · ", wd.Ship.Rooms.Where(r => r.Noise + r.Vibration + r.Smell + r.Radiation > 0.05f).Select(r => $"{r.Name} {r.Noise:0.00}/{r.Vibration:0.00}/{r.Smell:0.00}/{r.Radiation:0.00}")));
        return 0;
    }
}

public static partial class Program
{
    private static int RunJumperDebug(int seed)
    {
        var w = World.CreateDefault(seed, 0, "Mirinae");
        Run(w, SimTime.TicksPerDay);
        var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First();
        w.Power.AddJumper(0, 2, panel.UseSpots[0], 1f);
        for (int h = 0; h < 34; h += 3)
        {
            Run(w, SimTime.Hours(3));
            var orders = w.Board.Open.Where(o => o.Kind == WorkKind.RemoveJumper).Select(o => $"{o.Kind} u{o.Urgency:0.00} {o.Assignee?.Name}").ToList();
            Console.WriteLine($"{h + 3}h · 사고 {(w.History.Current == null ? "없음" : w.History.Current.Cause)} · 임시배선 {w.Power.Jumpers.Count(j => !j.Permanent)} · {string.Join(", ", orders)} · 일: {string.Join(", ", w.Crew.Select(c => c.Job?.Label ?? "-"))}");
        }
        return 0;
    }
}
