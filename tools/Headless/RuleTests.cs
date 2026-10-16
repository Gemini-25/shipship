using System;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 규칙 시험 (--ruletest): 작은 배에서 규칙 하나씩만 본다 — 몇 초 · 결정적 · 반드시 통과 (실패면 진짜 버그).
/// 장면 시험(나머지 --xxxtest)은 배 전체의 역사를 몇 시간 돌려 보므로 상관없는 변경에도 뒤집힌다 — 그쪽은 막지 않고 추세만 본다 (docs/DEV_PROCESS.md).
/// 새 기능 · 고친 버그마다 여기에 규칙을 하나씩 더한다.
/// </summary>
public static partial class Program
{
    private static int RunRuleTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"규칙 시험 · 시드 {seed}\n");
        string? only = Environment.GetEnvironmentVariable("RULE_ONLY");
        bool Do(string k) => only == null || only.Split(',').Contains(k);

        // ── 상태 저장: 찍고 되살린 배는 바이트까지 같고, 이어 가도 틱마다 같다 (첫날 배 캐시 · 자동 저장의 바탕) ──
        if (Do("save"))
        {
            var a = World.CreateDefault(seed, 0, "Mirinae");
            Run(a, SimTime.Hours(1));
            var snap = StateSave.Write(a, out var raw0);
            var b = StateSave.Read(snap);
            StateSave.Write(b, out var raw1);
            int at = FirstDiff(raw0, raw1);
            long split = -1;
            for (int t = 0; t < SimTime.Minutes(30) && split < 0; t++)
            {
                a.Step(); b.Step();
                if (SaveGame.StateHash(a) != SaveGame.StateHash(b)) split = a.Tick;
            }
            Check("상태 저장 — 되살린 배는 바이트까지 같고 30분을 이어 가도 틱마다 같다", at < 0 && split < 0,
                $"바이트 {(at < 0 ? "같음" : $"@{at} 다름")} · {(split < 0 ? "30분 내내 같음" : $"틱 {split}에 갈라짐")} ({raw0.Length / 1024}KB)");
        }

        // ── 결정론: 같은 시드 두 배는 같은 역사 ──
        if (Do("det"))
        {
            var a = World.CreateDefault(seed, 0, "Mirinae");
            var b = World.CreateDefault(seed, 0, "Mirinae");
            Run(a, SimTime.Hours(1));
            Run(b, SimTime.Hours(1));
            uint ha = SaveGame.StateHash(a), hb = SaveGame.StateHash(b);
            Check("결정론 — 같은 시드 두 배는 한 시간 뒤 같은 지문", ha == hb, $"{ha:x8} / {hb:x8}");
        }

        // ── 첫날 배 캐시 열쇠: 시험이 켜고 끄는 전역 속성 스위치도 열쇠에 든다 (44cb413 — 빠져서 aftertest · crisistest가 남의 배를 받았다) ──
        if (Do("warmkey"))
        {
            string k0 = WarmCache.KeyOf("day1", seed, "Hanbit");
            bool think = RoomPlanSystem.ThinkOff, crew = CrisisCrewSystem.Off;
            RoomPlanSystem.ThinkOff = !think;
            string k1 = WarmCache.KeyOf("day1", seed, "Hanbit");
            RoomPlanSystem.ThinkOff = think;
            CrisisCrewSystem.Off = !crew;
            string k2 = WarmCache.KeyOf("day1", seed, "Hanbit");
            CrisisCrewSystem.Off = crew;
            string k3 = WarmCache.KeyOf("day1", seed, "Hanbit");
            Check("첫날 배 캐시 — 전역 속성 스위치(방 안건 끔 · 위기 행동 끔)를 바꾸면 열쇠가 달라진다", k1 != k0 && k2 != k0 && k3 == k0,
                $"방 안건 {(k1 != k0 ? "다름" : "같음!")} · 위기 행동 {(k2 != k0 ? "다름" : "같음!")} · 되돌리면 {(k3 == k0 ? "같음" : "다름!")}");
        }

        // ── 길찾기 거리장 캐시: 꺼내 쓴 값은 다시 편 값과 같다 ──
        if (Do("flood"))
        {
            bool was = Pathfinder.CheckCache;
            Pathfinder.CheckCache = true;
            Pathfinder.StaleHits = 0; Pathfinder.FirstStale = null;
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Hours(4));
            Pathfinder.CheckCache = was;
            Check("길찾기 — 캐시에서 꺼낸 거리장이 다시 편 것과 같다 (네 시간)", Pathfinder.StaleHits == 0 && w.Paths.FloodHits > 0,
                $"꺼냄 {w.Paths.FloodHits}번 · 낡음 {Pathfinder.StaleHits}{(Pathfinder.FirstStale is { } fs ? " · " + fs : "")}");
        }

        // ── 몰래 꺼내 먹기: 나중에 먹을 것이 들어온 보관함도 바로 고른다 (예전엔 한 시간 묵은 목록 — 저장도 안 돼 불러온 배와 갈렸다) ──
        if (Do("sneak"))
        {
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Minutes(30));
            var c = w.Crew.Where(x => !x.Dead && x.CanAct && x.IsAwake).OrderBy(x => x.Id).First();
            var dist = w.Paths.Flood(c.Cell, c.PathProfile);
            foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null))
                foreach (var k in new[] { ItemKind.Ration, ItemKind.Meal }) f.Storage!.Take(k, f.Storage.Count(k));
            var empty = SneakFoodActivity.Pick(c, w, dist);
            var box = SneakFoodActivity.Boxes(w).FirstOrDefault(f => f.Storage!.Accepts(ItemKind.Ration) && !f.Room.Abandoned && !f.Room.Detached && !f.Stowed
                && dist.Reachable(f.UseSpots[0]) && !w.Crew.Any(o => o != c && !o.Dead && o.IsAwake && o.Room == f.Room));
            box?.Storage!.Add(ItemKind.Ration, 1);
            var got = SneakFoodActivity.Pick(c, w, dist);
            Check("몰래 꺼내 먹기 — 다 비운 뒤 한 보관함에 배급을 넣으면 그 보관함을 바로 고른다", empty == null && box != null && got == box,
                $"빈 때 {(empty == null ? "없음" : empty.Name)} · 넣은 곳 {box?.Name ?? "(보는 눈 없는 보관함 없음)"} · 고른 곳 {got?.Name ?? "없음"}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 규칙 시험 모두 통과" : $"\n✘ 규칙 시험 {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
