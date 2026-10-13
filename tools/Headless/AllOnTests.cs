using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v23 통합 시험 묶음 (--allontest): 게임과 똑같이 모든 기능을 켠 채 (이야기꾼 꾸준형 · 로봇 · 주컴퓨터 · 환경 · 큰 사고 · 마음 돌봄 · 반려동물 · 만남 …)
//   핵심 장면 20 — 불 · 고장 · 배관 · 운석 · 전력 서지 · 유독 가스 · 원자로 이상 · 선체 균열 · 식중독 · 태양 폭풍
//   · 마음 병 · 극한 환경 · 전자기 펄스 · 노심 카운트다운 · 기생체 · 약탈선 · 행성 착륙 · 저장/불러오기 · 결정론 · 7일 항해.
//   장면마다: 사고가 나고 → 정해진 시간 안에 배가 가라앉는다(불 · 감압 · 독 · 정전 · 고장) · 숨진 사람이 한도 안.
public static partial class Program
{
    private static World AoWorld(int seed, string key, float level = 3f)
    {
        Storyteller.PersonaValue = (float)StoryPersona.Steady; Storyteller.LevelValue = level;
        var w = World.CreateDefault(seed, 0, key);
        w.CrewCanDie = true;
        Run(w, SimTime.Hours(2));
        return w;
    }

    private static int AoAlive(World w) => w.Crew.Count(c => !c.Dead && !c.LeftShip);
    private static int AoFaults(World w) => w.Ship.Machines.Count(m => m.Faults.Count > 0);
    private static IEnumerable<Room> AoRooms(World w) => w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned);

    /// <summary>배가 가라앉았다: 불 없음 · 모든 방 기압 85kPa 넘음 · 독 · 연기 옅음 · 원자로가 돌고 정전 없음 · 고장이 처음보다 많지 않다.</summary>
    private static bool AoSettled(World w, int faults0) =>
        w.Fire.Count == 0 && AoRooms(w).All(r => r.Air.Pressure > 85f && r.Air.Toxin < 0.05f && r.Air.Smoke < 0.2f)
        && w.Power.ReactorOnline && !w.Power.Brownout && AoFaults(w) <= faults0;

    private static string AoState(World w, int faults0)
    {
        var low = AoRooms(w).OrderBy(r => r.Air.Pressure).First();
        return $"불 {w.Fire.Count} · 최저 기압 {low.Air.Pressure:0}kPa({low.Name}) · 독 {AoRooms(w).Max(r => r.Air.Toxin):0.00} · 연기 {AoRooms(w).Max(r => r.Air.Smoke):0.00} · 원자로 {(w.Power.ReactorOnline ? "돎" : "멈춤")} · 정전 {w.Power.Brownout} · 고장 {AoFaults(w)}/{faults0}";
    }

    /// <summary>사고 하나: 일으키고 · 가라앉을 때까지 (10분마다 본다).</summary>
    private static void AoScene(int seed, string name, string ship, Func<World, string?> start, long within, int maxDead = 0, Func<World, int, bool>? done = null)
    {
        var w = AoWorld(seed, ship);
        int alive0 = AoAlive(w), faults0 = AoFaults(w);
        string? what = start(w);
        long t0 = w.Tick, took = -1;
        done ??= AoSettled;
        Run(w, SimTime.Minutes(5));
        while (w.Tick - t0 < within)
        {
            if (done(w, faults0)) { took = w.Tick - t0; break; }
            Run(w, SimTime.Minutes(10));
        }
        int dead = alive0 - AoAlive(w);
        Check($"{name} ({ship}) — {within / SimTime.Hours(1)}시간 안에 가라앉는다 · 숨진 사람 {maxDead} 이하", what != null && took >= 0 && dead <= maxDead,
            $"{what ?? "사고가 나지 않았다"} · {(took >= 0 ? $"{took / (float)SimTime.Hours(1):0.0}시간" : "못 끝냄")} · 숨짐 {dead} · {AoState(w, faults0)}");
    }

    private static int RunAllOnTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"통합 시험 묶음 — 모든 기능 켬 (v23) · 시드 {seed}\n");
        float persona = Storyteller.PersonaValue, level = Storyteller.LevelValue;
        string only = Environment.GetEnvironmentVariable("AO_ONLY") ?? "";
        bool On(string k) => only.Length == 0 || only.Split(',').Contains(k);
        try
        {
            // ── 1~10 사고 ──
            if (On("fire")) AoScene(seed, "불", "Hanbit", w => w.Hazards.FireStory("fire", null), SimTime.Hours(3));
            if (On("break")) AoScene(seed, "설비 고장", "Hanbit", w => w.Hazards.FireStory("break", null), SimTime.Hours(12));
            if (On("pipe")) AoScene(seed, "배관 터짐", "Mirinae", w => w.Hazards.FireStory("pipe", null), SimTime.Hours(12));
            if (On("meteor")) AoScene(seed, "운석", "Hanbit", w => w.Hazards.FireStory("meteor", null), SimTime.Hours(12), maxDead: 1);
            if (On("surge")) AoScene(seed, "전력 서지", "Eunha", w => w.Hazards.FireStory(nameof(HazardKind.PowerSurge), null), SimTime.Hours(12));
            if (On("gas")) AoScene(seed, "유독 가스", "Hanbit", w => w.Hazards.FireStory(nameof(HazardKind.GasLeak), null), SimTime.Hours(12));
            if (On("reactor")) AoScene(seed, "원자로 이상", "Mirinae", w => w.Hazards.FireStory(nameof(HazardKind.ReactorTransient), null), SimTime.Hours(12));
            if (On("crack")) AoScene(seed, "선체 균열", "Eunha", w => w.Hazards.FireStory(nameof(HazardKind.HullCrack), null), SimTime.Hours(16));
            if (On("food")) AoScene(seed, "식중독", "Hanbit", w => w.Hazards.FireStory(nameof(HazardKind.FoodPoisoning), null), SimTime.TicksPerDay * 3, maxDead: 0,
                (w, f) => !w.Crew.Any(c => !c.Dead && c.Ailments.Any(a => AilmentSystem.Spec(a.Id).Name.Contains("식중독"))));
            if (On("storm")) AoScene(seed, "태양 폭풍", "Mirinae", w => w.Hazards.FireStory(nameof(HazardKind.SolarStorm), null), SimTime.TicksPerDay,
                done: (w, f) => !w.Hazards.StormActive && AoSettled(w, f));

            // ── 11 마음 병 ──
            if (On("mind"))
            {
                var w = AoWorld(seed, "Hanbit");
                var sad = w.Crew.Where(c => !c.Dead && c.CanAct).OrderBy(c => c.Id).Skip(2).First();
                w.Ailments.Catch(sad, "depression", null, "시험");
                long t0 = w.Tick;
                while (w.Tick - t0 < SimTime.TicksPerDay * 2 && w.MindCare.Stats.RobotSessions + w.MindCare.Stats.HumanSessions == 0) Run(w, SimTime.Minutes(20));
                Check("마음 병 — 이틀 안에 말벗 로봇이나 사람이 찾아가 이야기를 듣는다", w.MindCare.Stats.RobotSessions + w.MindCare.Stats.HumanSessions > 0,
                    $"{sad.Name} · {(w.Tick - t0) / (float)SimTime.Hours(1):0.0}시간 · {w.MindCare.Stats.Line()}");
            }

            // ── 12 극한 환경 ──
            if (On("env"))
            {
                var w = AoWorld(seed, "Hanbit");
                int alive0 = AoAlive(w), faults0 = AoFaults(w);
                w.Env.Force = EnvHazardSystem.Sample(EnvKind.Frozen);
                foreach (var s in new[] { IncidentScale.Room, IncidentScale.System, IncidentScale.Ship }) w.Env.Begin(s, null, null, now: true);
                Run(w, SimTime.Hours(18));
                int dead = alive0 - AoAlive(w);
                Check("얼음 행성 — 방 · 계통 · 배 전체 사고가 겹쳐도 열여덟 시간 안에 가라앉는다 · 숨진 사람 1 이하", dead <= 1 && w.Fire.Count == 0 && AoRooms(w).All(r => r.Air.Pressure > 85f),
                    $"{w.Env.Stats.Line()} · 숨짐 {dead} · {AoState(w, faults0)}");
            }

            // ── 13 전자기 펄스 ──
            if (On("emp"))
            {
                var w = AoWorld(seed, "Eunha");
                int alive0 = AoAlive(w);
                var bc = w.Big.Start(BigKind.Emp);
                Run(w, SimTime.Hours(12));
                int up = w.Robots.Robots.Count(r => r.Operational), all = w.Robots.Robots.Count;
                Check("전자기 펄스 — 열두 시간 안에 사람 손으로 로봇 대부분이 다시 켜진다 · 숨진 사람 없음", bc != null && up >= all * 0.7f && AoAlive(w) == alive0,
                    $"로봇 {up}/{all} · {bc?.Outcome} · {w.Big.Line()}");
            }

            // ── 14 노심 카운트다운 ──
            if (On("core"))
            {
                var w = AoWorld(seed, "Hanbit");
                int alive0 = AoAlive(w);
                var bc = w.Big.Start(BigKind.CoreCountdown);
                long t0 = w.Tick;
                while (w.Tick - t0 < SimTime.Hours(6) && bc != null && !bc.Done) Run(w, SimTime.Minutes(10));
                Check("노심 카운트다운 — 세 시간 안팎에 끝난다 (제어봉을 고치거나 노심을 떼어 낸다) · 숨진 사람 1 이하", bc != null && bc.Done && alive0 - AoAlive(w) <= 1,
                    $"{(w.Tick - t0) / (float)SimTime.Hours(1):0.0}시간 · {bc?.Outcome} · 숨짐 {alive0 - AoAlive(w)}");
            }

            // ── 15 기생체 ──
            if (On("parasite"))
            {
                var w = AoWorld(seed, "Eunha");
                var host = w.Crew.Where(c => !c.Dead && c.CanAct).OrderBy(c => c.Id).Skip(3).First();
                bool inf = w.Parasite.Infect(host, "시험 포자");
                long t0 = w.Tick;
                while (w.Tick - t0 < SimTime.TicksPerDay * 5 && w.Parasite.Active) Run(w, SimTime.Hours(1));
                Check("기생체 — 닷새 안에 찾아내 치료하거나 잡는다", inf && !w.Parasite.Active && w.Parasite.Found >= 1,
                    $"감염 {w.Parasite.Infected} · 찾음 {w.Parasite.Found} · 치료 {w.Parasite.Cured} · 빠져나감 {w.Parasite.Escapes} · 잡음 {w.Parasite.Caught} · {(w.Tick - t0) / (float)SimTime.TicksPerDay:0.0}일");
            }

            // ── 16 약탈선 ──
            if (On("raid"))
            {
                var w = AoWorld(seed, "Hanbit");
                int alive0 = AoAlive(w);
                var m = w.Encounters.Meet(MeetKind.Raider);
                Run(w, SimTime.Hours(8));
                Check("약탈선 — 드론이 막아서거나 물자를 내주고 보낸다 · 숨진 사람 없음 · 지나가면 멀어진다", m.Done && m.Outcome.Length > 0 && AoAlive(w) == alive0, m.Outcome);
            }

            // ── 17 행성 착륙 ──
            if (On("planet"))
            {
                var w = AoWorld(seed, "Mirinae");
                int alive0 = AoAlive(w);
                var p = PlanetGen.Make(new Rng(seed), 31, 3);
                PlanetGen.ApplyBody(p, 3, PlanetBody.IceMoon); // 혹독한 곳 (얼음 위성)
                w.Planet.LandNow(p);
                Run(w, SimTime.TicksPerDay);
                Check("얼음 위성 착륙 — 하루 동안 날씨 · 환경 사고 · 표본을 겪고도 무사하다 (환경 줄이 행성을 읽는다)", w.Planet.Here == p && AoAlive(w) >= alive0 - 1 && w.Env.Line() != null,
                    $"{p.Name} ({PlanetGen.BodyName(p.Body)} · {PetSystem.LifeLine(p)}) · 내려앉음 {w.Planet.Landed} · {w.Env.Line()} · 표본 {w.Pets.Specimens.Count} · 숨짐 {alive0 - AoAlive(w)}");
            }

            // ── 18 저장 → 불러오기 ──
            if (On("save"))
            {
                var w = AoWorld(seed, "Hanbit");
                Run(w, SimTime.Hours(10));
                var data = StateSave.Write(w);
                var w2 = StateSave.Read(data);
                uint a = SaveGame.StateHash(w), b = SaveGame.StateHash(w2);
                Run(w, SimTime.Hours(3)); Run(w2, SimTime.Hours(3));
                uint a2 = SaveGame.StateHash(w), b2 = SaveGame.StateHash(w2);
                Check("저장 → 불러오기 — 지문이 같고 세 시간 뒤에도 같다 (새 기능까지 모두 저장)", a == b && a2 == b2, $"{data.Length / 1024}KB · {a:x8}/{b:x8} → {a2:x8}/{b2:x8}");
            }

            // ── 19 결정론 ──
            if (On("det"))
            {
                uint H() { var w = AoWorld(seed, "Hanbit"); Run(w, SimTime.TicksPerDay); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                Check("결정론 — 모든 기능을 켜고도 같은 시드 하루 두 번 지문이 같다", a == b, $"{a:x8} / {b:x8}");
            }

            // ── 20 7일 항해 ──
            if (On("week"))
            {
                var w = AoWorld(seed, "Hanbit", level: 5f);
                int alive0 = AoAlive(w);
                long starving = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int d = 0; d < 7 * 24; d++)
                {
                    Run(w, SimTime.Hours(1));
                    starving += w.Crew.Count(c => !c.Dead && c.Needs.Food <= 0.02f);
                }
                int dead = alive0 - AoAlive(w) + w.Crew.Count(c => c.LeftShip);
                var inc = w.History.Events.Count(e => e.Kind == HistoryKind.Incident);
                Check("7일 항해 (이야기꾼 5단계) — 예외 없이 · 사고가 꾸준히 나고 · 숨진 사람 셋 이하 · 굶주림 드묾", dead <= 3 && inc >= 5 && starving < 7 * 24 * 2,
                    $"사고 기록 {inc} · 숨짐 {dead} · 굶주린 사람·시간 {starving} · {w.Env.Stats.Line()} · 만남 {w.Encounters.Meets.Count} · 반려 쓰다듬기 {w.Pets.Pettings} · {watch.Elapsed.TotalSeconds:0}초");
            }
        }
        catch (Exception ex) { Check("예외 없이", false, ex.ToString()); }
        finally { Storyteller.PersonaValue = persona; Storyteller.LevelValue = level; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
