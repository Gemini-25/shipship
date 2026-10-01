using System;
using System.Linq;
using ShipSim.Core;

// v12.4 재난과 난이도: 새 사고 8종 · 이야기꾼 · 난이도 · 밸런스 보고서
public static partial class Program
{
    private static int RunStoryTest(int seed)
    {
        _fails = 0;
        float p0 = Storyteller.PersonaValue, l0 = Storyteller.LevelValue;
        Console.WriteLine($"재난과 난이도 점검 (v12.4) · 시드 {seed}\n");
        try
        {
            string Apply(World w, HazardKind k, Cell at = default, int id = -1) => Hazards.Apply(w, k, at, id) ?? "(못 걸음)";
            int Nodes(World w) => w.Causes.Incidents.LastOrDefault()?.Nodes.Count ?? 0;

            // 1) 냉각 상실 → 원자로 긴급 정지 → 바닥 물
            {
                var w = DayOne(seed, "Mirinae");
                string what = Apply(w, HazardKind.CoolantLoss);
                bool scram = false;
                for (int i = 0; i < 16 && !scram; i++) { Run(w, SimTime.Minutes(15)); scram |= !w.Power.ReactorOnline; }
                Run(w, SimTime.Hours(10));
                Check("냉각 상실 — 원자로가 멈추고 물이 고인다, 사슬이 번진다", scram && Nodes(w) >= 3, $"{what} · 긴급 정지 {(scram ? "예" : "아니오")} · 사슬 고리 {Nodes(w)} · 지금 원자로 {(w.Power.ReactorOnline ? "돎" : "멈춤")} · {w.Moisture.Stats}");
            }
            // 2) 덕트 화재 → 옆방으로 번진다
            {
                var w = DayOne(seed, "Mirinae");
                var room = w.Ship.RoomsOf(RoomType.Galley).First();
                string what = Apply(w, HazardKind.DuctFire, room.Cells[0]);
                int roomsBurned = 0;
                for (int i = 0; i < 8; i++) { Run(w, SimTime.Minutes(5)); roomsBurned = Math.Max(roomsBurned, w.Ship.Rooms.Count(r => w.Fire.CountIn(r) > 0)); }
                var fireNodes = w.Causes.Nodes.Count(n => n.Kind == CauseKind.Fire);
                Check("덕트 화재 — 덕트를 타고 옆방에도 불이 난다", fireNodes >= 2, $"{what} · 불난 방 고리 {fireNodes} · 동시에 탄 방 최대 {roomsBurned}");
            }
            // 3) 수소 축적 → 식히고 환기하거나, 터진다
            {
                var w = DayOne(seed, "Mirinae");
                string what = Apply(w, HazardKind.HydrogenBuildup);
                Run(w, SimTime.Hours(8));
                var gen = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Select(f => f.Machine!).OrderByDescending(m => m.Vapor).First();
                Check("수소 축적 — 식혀 막거나 터진다", w.Volatile.Stats.CooledDown + w.Volatile.Stats.Explosions > 0 || gen.Vapor < 0.3f,
                    $"{what} · 식힘 {w.Volatile.Stats.CooledDown} · 폭발 {w.Volatile.Stats.Explosions} · 남은 수소 {gen.Vapor * 100:0}%");
            }
            // 4) 컴퓨터 오판단 → 멀쩡한 방이 꺼진다 → 사람이 다시 올린다
            {
                var w = DayOne(seed, "Mirinae");
                string what = Apply(w, HazardKind.ComputerMisjudge);
                var room = w.Ship.Rooms.FirstOrDefault(r => r.BreakerOff);
                Run(w, SimTime.Hours(6));
                Check("컴퓨터 오판단 — 멀쩡한 방 분전함을 내리고, 사람이 다시 올린다", room != null && !room.BreakerOff, $"{what} · {room?.Name ?? "-"} 분전함 {(room?.BreakerOff == true ? "아직 내림" : "다시 올림")}");
            }
            // 5) 전염병 → 옮고, 낫는다 (전파 경로가 사슬에)
            {
                var w = DayOne(seed, "Mirinae");
                string what = Apply(w, HazardKind.Epidemic, default, w.Crew.First(c => c.CanAct).Id);
                Run(w, SimTime.TicksPerDay * 7);
                var st = w.Disease.Stats;
                var chain = w.Causes.Nodes.Where(n => n.Kind == CauseKind.Illness).Select(n => n.Text).Take(4);
                Check("전염병 — 같은 방에서 옮고, 나아 면역이 된다", st.Infections >= 2 && st.Recovered >= 1, $"{what} · {st} · {string.Join(" / ", chain)}");
            }
            // 6) 미세 운석 소나기 → 작은 구멍 여럿
            {
                var w = DayOne(seed, "Mirinae");
                string what = Apply(w, HazardKind.MicroShower);
                int maxLeak = 0;
                for (int i = 0; i < 12; i++) { Run(w, SimTime.Minutes(5)); maxLeak = Math.Max(maxLeak, w.Ship.Rooms.Count(r => r.Leaking)); }
                int impacts = w.Causes.Nodes.Count(n => n.Kind == CauseKind.Impact);
                Check("미세 운석 소나기 — 외벽 곳곳에 작은 충돌", impacts >= 5, $"{what} · 충돌 {impacts} · 동시에 새는 방 최대 {maxLeak}");
            }
            // 7) 가스 탱크 파열 · 8) 냉장고 고장
            {
                var w = DayOne(seed, "Mirinae");
                float tank0 = w.Air.Reserve;
                string what = Apply(w, HazardKind.GasTankRupture);
                var life = w.Ship.RoomsOf(RoomType.LifeSupport).First();
                Check("가스 탱크 파열 — 폭발 · 공기 탱크 손실 · 산소 이상", w.Volatile.Stats.Explosions > 0 && w.Air.Reserve < tank0, $"{what} · 공기 탱크 {tank0:0} → {w.Air.Reserve:0} · 생명유지실 산소 {life.Air.O2:0.0}kPa");
                var w2 = DayOne(seed, "Mirinae");
                var fridge = w2.Ship.FurnitureOf(FurnitureType.Fridge).First();
                string what2 = Apply(w2, HazardKind.FreezerFailure, fridge.Cells[0]);
                Run(w2, SimTime.Hours(8));
                bool fixedIt = !fridge.Machine!.Has(FaultKind.CompressorFail);
                Check("냉장고 고장 — 여섯 시간 안에 고치거나, 음식이 상한다", fixedIt || fridge.Storage!.Tainted > 0, $"{what2} · {(fixedIt ? "고쳤다" : $"상한 식사 {fridge.Storage!.Tainted}끼")}");
            }

            // 8b) 피해 체계: 전자기 교란 (나중의 포격도 같은 말로)
            {
                var w = DayOne(seed, "Mirinae");
                var bridge = w.Ship.FurnitureOf(FurnitureType.MainComputer).First().Room;
                float cal0 = w.Ship.Machines.Average(m => m.SensorCal);
                Harm.At(w, Cell.FromPosition(bridge.Center), HarmKind.Pulse, 1f, "시험 교란");
                float cal1 = w.Ship.Machines.Average(m => m.SensorCal);
                Check("피해 체계 — 전자기 교란이 감지기·데이터선·차단기를 흔든다", cal1 < cal0 && w.Causes.Incidents.Count > 0, $"평균 교정 {cal0 * 100:0}% → {cal1 * 100:0}% · 데이터선 끊김 {w.Net.Links.Count(l => l.Kind == NetKind.Data && l.Cut)}");
            }
            // 9) 이야기꾼 성격마다
            foreach (int persona in new[] { 1, 2, 3, 4 })
            {
                Storyteller.PersonaValue = persona; Storyteller.LevelValue = 3;
                var w = DayOne(seed, "Mirinae");
                Run(w, SimTime.TicksPerDay * 6);
                var s = w.Story;
                bool tester = persona != 4 || s.Journal.Any(j => j.why.StartsWith("급소"));
                Check($"이야기꾼 {Storyteller.PersonaName((StoryPersona)persona)} — 사고를 낸다" + (persona == 4 ? " (급소를 노린다)" : ""), s.Fired >= 2 && tester,
                    $"사고 {s.Fired} · 사망 {w.History.Deaths} · 쓰러짐 {w.History.Collapses} · " + string.Join(" / ", s.Journal.Take(3).Select(j => $"{SimTime.Day(j.tick)}일 {SimTime.Clock(j.tick)} {j.what}")));
            }
            // 10) 난이도: 느긋 ↔ 가혹
            {
                int[] fired = new int[2]; int[] stock = new int[2];
                foreach (var (k, level) in new[] { (0, 1), (1, 5) })
                {
                    Storyteller.PersonaValue = 1; Storyteller.LevelValue = level;
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    stock[k] = w.Ship.CountStored(ItemKind.Sealant) + w.Ship.CountStored(ItemKind.Plate);
                    Run(w, SimTime.TicksPerDay * 6);
                    fired[k] += w.Story.Fired;
                }
                Check("난이도 — 가혹은 사고가 잦고 시작 물자가 적다", fired[1] > fired[0] && stock[1] < stock[0], $"느긋: 사고 {fired[0]} · 시작 수리재 {stock[0]}  ↔  가혹: 사고 {fired[1]} · 시작 수리재 {stock[1]}");
            }
            // 11) 결정론
            {
                Storyteller.PersonaValue = 4; Storyteller.LevelValue = 4;
                uint H() { var w = DayOne(seed, "Mirinae"); Run(w, SimTime.TicksPerDay * 3); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                Check("이야기꾼이 든 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
            }
        }
        finally { Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0; }
        Console.WriteLine(_fails == 0 ? "\n✔ 재난과 난이도 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>밸런스 보고서: 성격 × 난이도마다 여러 판 — 하루 사고 수 · 사고당 번진 것 · 수습 시간 · 쓰러짐 · 사망 · 감염.</summary>
    private static int RunBalance(int days, int seed, int runs)
    {
        float p0 = Storyteller.PersonaValue, l0 = Storyteller.LevelValue;
        Console.WriteLine($"밸런스 보고서 · {days}일 × {runs}판 · 미리내호\n");
        Console.WriteLine("성격        난이도  사고/일  번진 것  수습(시간)  쓰러짐  사망  감염  전멸");
        try
        {
            foreach (int persona in (Environment.GetEnvironmentVariable("SHIPSIM_PERSONA") is string ps ? new[] { int.Parse(ps) } : new[] { 1, 2, 3, 4 }))
            foreach (int level in new[] { 1, 3, 5 })
            {
                Storyteller.PersonaValue = persona; Storyteller.LevelValue = level;
                float inc = 0, spread = 0, hours = 0, down = 0, dead = 0, ill = 0; int wiped = 0, closed = 0, incCount = 0;
                for (int r = 0; r < runs; r++)
                {
                    var w = World.CreateDefault(seed + r * 97, 0, "Mirinae");
                    Player.AllowDeath(w, true);
                    Run(w, SimTime.TicksPerDay * days);
                    inc += w.Story.Fired / (float)days;
                    foreach (var i in w.Causes.Notable(1)) { spread += i.Nodes.Count(n => w.Causes.Node(n).Kind != CauseKind.Recovery) - 1; incCount++; if (!i.Open) { hours += (i.End - i.Start) / (float)SimTime.TicksPerHour; closed++; } }
                    down += w.History.Collapses; dead += w.History.Deaths; ill += w.Disease.Stats.Infections;
                    if (w.Crew.All(c => c.Dead)) wiped++;
                    if (Environment.GetEnvironmentVariable("SHIPSIM_DEATHS") == "1")
                        foreach (var e in w.History.Events.Where(e => e.Kind == HistoryKind.Death && e.Text.Contains("죽었다")))
                            Console.WriteLine($"     판 {r + 1} · {e.Tick / (float)SimTime.TicksPerDay:0.0}일 · {e.Text}");
                }
                Console.WriteLine($"{Storyteller.PersonaName((StoryPersona)persona),-10} {Storyteller.LevelName(level),-5} {inc / runs,7:0.00} {(incCount > 0 ? spread / incCount : 0),8:0.0} {(closed > 0 ? hours / closed : 0),10:0.0} {down / runs,7:0.0} {dead / runs,5:0.0} {ill / runs,5:0.0} {wiped,5}");
            }
        }
        finally { Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0; }
        return 0;
    }
    /// <summary>사망 추적: 이야기꾼 성격·난이도(SHIPSIM_PERSONA·SHIPSIM_LEVEL)로 돌리다 첫 죽음에서 멈추고, 그 전 열두 시간의 배 기록과 그 사람의 기록을 보인다.</summary>
    private static int RunDeathTrace(int days, int seed)
    {
        float p0 = Storyteller.PersonaValue, l0 = Storyteller.LevelValue;
        try
        {
            Storyteller.PersonaValue = int.Parse(Environment.GetEnvironmentVariable("SHIPSIM_PERSONA") ?? "3");
            Storyteller.LevelValue = int.Parse(Environment.GetEnvironmentVariable("SHIPSIM_LEVEL") ?? "1");
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Player.AllowDeath(w, true);
            long end = SimTime.TicksPerDay * (long)days;
            float watch = float.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_WATCHFROM"), out var wf) ? wf : -1f; // 몇 시간째부터 1분마다
            while (w.Tick < end && w.History.Deaths == 0)
            {
                if (watch >= 0f && w.Tick >= (long)(watch * SimTime.TicksPerHour))
                {
                    Run(w, SimTime.Minutes(1));
                    foreach (var r in w.Ship.Rooms.Where(r => r.Leaking || r.Air.Pressure < 99f || !r.VentOpen))
                        Console.WriteLine($"      [{r.Name}] 샘 {r.Leaking} ({r.BreachArea:0.0000}) 환기 {(r.VentOpen ? "열림" : "닫힘")} 덕트 {r.DuctLinked} 기압 {r.Air.Pressure:0} O2 {r.Air.O2:0.0}");
                    Console.WriteLine($"{SimTime.Clock(w.Tick)} " + string.Join(" | ", w.Crew.Where(c => !c.Dead).Select(c =>
                        $"{c.Name} {c.Room?.Name ?? "-"} O2 {c.Room?.Air.O2 ?? 0:0.0}/{c.Room?.Air.Pressure ?? 0:0} {(c.Suit != null ? "[복]" : "")}{(c.Down ? "[쓰러짐]" : "")} {c.Job?.Label ?? "-"}")));
                }
                else Run(w, SimTime.Minutes(10));
            }
            var dead = w.Crew.FirstOrDefault(c => c.Dead);
            if (dead == null) { Console.WriteLine("죽음 없음"); return 0; }
            Console.WriteLine($"{Storyteller.PersonaName((StoryPersona)(int)Storyteller.PersonaValue)}·{Storyteller.LevelName(Storyteller.Level)} · 시드 {seed} · {w.Day}일차 {SimTime.Clock(w.Tick)} — {dead.Name} ({dead.Vitals.InjuryCause}) · 부상 {string.Join(", ", dead.Vitals.Wounds.Select(x => $"{x.Part}/{x.Kind} {Wounds.Severity(dead.Vitals, x):0.00}"))}\n");
            long from = w.Tick - SimTime.Hours(12);
            foreach (var e in w.Log.Entries.Where(e => e.Tick >= from && (e.CrewId == dead.Id || e.Kind is LogKind.Ship or LogKind.Warning)))
                Console.WriteLine($"  {SimTime.Clock(e.Tick)} {(e.CrewId == dead.Id ? "★" : " ")} {e.Text}");
            Console.WriteLine();
            foreach (var i in w.Causes.Incidents.Where(i => i.Start >= from - SimTime.Hours(12)))
                Console.WriteLine($"  사고 {SimTime.Clock(i.Start)} {w.Causes.Node(i.Root).Text} · 무게 {i.Weight(w.Causes)}");
        }
        finally { Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0; }
        return 0;
    }
}
