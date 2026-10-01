using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v13.3 판단·인지·감정: 목표 계층 · 아는 것의 차이 · 공황(난이도) · 명령 반응 · 영웅심 · 컴퓨터 신뢰
public static partial class Program
{
    private static int RunMindTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"판단·인지·감정 점검 (v13.3) · 시드 {seed}\n");
        bool debug = Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1";
        float level0 = Storyteller.LevelValue;
        try
        {
            // 1) 아는 것의 차이: 데이터선이 닿은 방의 불은 경보로 모두 알고, 끊긴 방의 불은 본 사람부터 무전·소문으로 퍼진다
            {
                string Spread(bool cut, out int knowAt1, out int total, out string sources)
                {
                    var w = DayOne(seed, "Mirinae");
                    w.Policies.Set("inertfire", 0, "시험");
                    w.Policies.Set("vacuumfire", 0, "시험");
                    var room = StoreRoom(w);
                    ClearRoom(w, room);
                    if (cut)
                    {
                        foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && (l.Door?.RoomA == room || l.Door?.RoomB == room)).ToList()) w.Net.Hurt(l, 1f, "시험");
                        Run(w, SimTime.Minutes(1));
                    }
                    // 한 사람이 그 방에 있다가 불을 본다
                    var witness = w.Crew.First(c => c.CanAct && c.IsAwake);
                    witness.EndJob(w, ToilStatus.Interrupted);
                    witness.Position = room.Cells.Where(w.Ship.IsOpenFloor).First().Center; witness.PreviousPosition = witness.Position;
                    BigFire(w, room, 3);
                    Run(w, SimTime.Minutes(1));
                    string key = $"fire:{room.Id}";
                    var awake = w.Crew.Where(c => c.CanAct).ToList();
                    total = awake.Count;
                    knowAt1 = awake.Count(c => c.Mind.Knows.ContainsKey(key));
                    Run(w, SimTime.Minutes(9));
                    sources = string.Join(" · ", awake.Where(c => c.Mind.Knows.ContainsKey(key)).GroupBy(c => c.Mind.Knows[key].src).Select(g => $"{MindSystem.SourceName(g.Key)} {g.Count()}"));
                    return cut ? (room.DataLinked ? "다시 이어짐" : "끊김") : "이어짐";
                }
                Spread(false, out int linked1, out int total, out var srcA);
                string state = Spread(true, out int cut1, out _, out var srcB);
                Check("아는 것의 차이 — 데이터선이 닿은 방의 불은 경보로 곧장 모두, 끊긴 방의 불은 본 사람부터 무전·소문으로",
                    linked1 >= total - 1 && cut1 < linked1 && srcB.Length > 0,
                    $"1분 뒤 아는 사람: 이어진 방 {linked1}/{total} ({srcA}) · 끊긴 방 {cut1}/{total} → 10분 뒤 ({srcB}) · 데이터선 {state}");
            }
            // 2) 목표 계층: 위기 중 조원은 '맡은 역할', 위험한 방에서 나가는 사람은 '생존'
            {
                var w = CrisisShip(seed, 0);
                var tiers = new Dictionary<GoalTier, int>();
                string sample = "";
                for (int m = 0; m < 20; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    foreach (var c in w.Crew.Where(c => c.CanAct))
                    {
                        tiers[c.Mind.Goal] = tiers.GetValueOrDefault(c.Mind.Goal) + 1;
                        if (c.Mind.Goal == GoalTier.Role && sample == "") sample = $"{c.Name}: {c.Mind.GoalWhy}";
                    }
                }
                Check("목표 계층 — 생존 > 맡은 역할 > 일 > 생활, 위기 중 조원은 맡은 역할을 한다", tiers.GetValueOrDefault(GoalTier.Role) > 0,
                    string.Join(" · ", tiers.OrderBy(kv => kv.Key).Select(kv => $"{MindSystem.GoalName(kv.Key)} {kv.Value}")) + $" · 예: {sample}");
            }
            // 3) 공황은 난이도에 따라: 느긋은 드물고 짧게 달아날 뿐, 가혹은 잦고 얼어붙기도 한다
            {
                (int panics, int freezes) Count(int level)
                {
                    Storyteller.LevelValue = level;
                    int p = 0, f = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        var w = CrisisShip(seed + k, 0);
                        foreach (var c in w.Crew) c.Needs.Stress = 0.6f;
                        Run(w, SimTime.Minutes(40));
                        p += w.Minds.Panics;
                        f += w.Minds.Freezes;
                    }
                    return (p, f);
                }
                var easy = Count(1);
                var hard = Count(5);
                Storyteller.LevelValue = level0;
                Check("공황 — 난이도가 높을수록 잦고, 느긋에서는 얼어붙지 않는다", hard.panics > easy.panics && easy.freezes == 0,
                    $"느긋: 공황 {easy.panics} · 얼어붙음 {easy.freezes} / 가혹: 공황 {hard.panics} · 얼어붙음 {hard.freezes} (배 넷씩, 40분)");
            }
            // 4) 명령 반응: 화가 난 사람은 지휘를 덜 따른다
            {
                var w = CrisisShip(seed, 0);
                Run(w, SimTime.Minutes(5));
                var member = w.Crew.FirstOrDefault(c => w.Command.TeamOf(c) is { Kind: not TeamKind.Reserve } t && t.Watcher != c.Id);
                var order = member != null ? w.Board.Open.FirstOrDefault(o => CommandSystem.Group(o.Kind) == w.Command.TeamOf(member)!.Kind) : null;
                float calm = 0f, angry = 0f, ob0 = 0f, ob1 = 0f;
                if (member != null && order != null)
                {
                    member.Mind.Anger = 0f; ob0 = w.Minds.Obedience(member); calm = w.Command.Bias(member, order);
                    member.Mind.Anger = 1f; ob1 = w.Minds.Obedience(member); angry = w.Command.Bias(member, order);
                    member.Mind.Anger = 0f;
                }
                Check("명령 반응 — 화가 나면 지휘를 덜 따른다 (조의 일에 끌리는 힘이 준다)", member != null && order != null && angry < calm && ob1 < ob0,
                    member != null ? $"{member.Name}: 따르는 정도 {ob0:0.00} → {ob1:0.00} · 조의 일 끌림 {calm:0.00} → {angry:0.00}" : "조원 없음");
            }
            // 5) 영웅심: 용감한 사람은 가까운 사람이 진공 속에 쓰러지면 우주복 없이도 뛰어든다
            {
                var w = DayOne(seed, "Mirinae");
                var hero = w.Crew.Where(c => c.CanAct && c.IsAwake).OrderByDescending(c => c.Traits.Bravery).First();
                var victim = w.Crew.Where(c => c.CanAct && c != hero).OrderBy(c => c.Id).First();
                hero.ChangeAffinity(victim, 0.8f);
                foreach (var l in w.Ship.FurnitureOf(FurnitureType.SuitLocker)) l.Storage!.Take(ItemKind.Suit, 99);
                var lounge = w.Ship.RoomsOf(RoomType.Lounge).First();
                ClearRoom(w, lounge);
                var wall = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == lounge).Select(kv => kv.Key).First();
                victim.EndJob(w, ToilStatus.Interrupted);
                victim.Position = lounge.Cells.Where(w.Ship.IsOpenFloor).OrderByDescending(x => (x.X - wall.X) * (x.X - wall.X) + (x.Y - wall.Y) * (x.Y - wall.Y)).First().Center; victim.PreviousPosition = victim.Position; // 구멍에서 먼 자리
                Hull.Damage(w.Ship, wall, 0.7f);
                bool heroic = false, dashed = false;
                for (int m = 0; m < 25; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (m < 6 && !victim.Down) victim.Vitals.Oxygen = MathF.Min(victim.Vitals.Oxygen, 0.1f); // 숨이 막혀 쓰러진다 (경보가 돈다)
                    heroic |= hero.Mind.Heroic(w.Tick) && hero.Mind.HeroFor == victim.Id;
                    if (debug) Console.WriteLine($"      {SimTime.Clock(w.Tick)} {victim.Name} 쓰러짐 {victim.Down} 방 {victim.Room?.Name} 압력 {victim.Room?.Air.Pressure:0} · {hero.Name} 깸 {hero.IsAwake} 앎 [{string.Join(",", hero.Mind.Knows.Keys)}] 관계 {hero.AffinityTo(victim):0.00} 일 {hero.Job?.Label} 대시 {hero.Dashing} · 공황 {hero.Mind.Panicking(w.Tick)} · 영웅 {hero.Mind.HeroUntil} · 새는가 {victim.Room?.Leaking} 위험 {(victim.Room != null ? Atmosphere.Danger(victim.Room) : -1):0.00} · 대담 {hero.Traits.Bravery:0.00}");
                    dashed |= hero.Dashing && hero.Job?.Order?.Kind == WorkKind.Rescue || victim.CarriedBy == hero;
                }
                Check("영웅심 — 용감한 사람은 가까운 사람이 쓰러진 걸 알면 우주복 없이도 뛰어든다", heroic && dashed,
                    $"{hero.Name}(대담 {hero.Traits.Bravery:0.00}) → {victim.Name}: 영웅심 {(heroic ? "있음" : "없음")} · 뛰어듦 {(dashed ? "예" : "아니오")} · 영웅심 {w.Minds.Heroics}번");
            }
            // 6) 컴퓨터 신뢰: 소화 수순이 사람을 잃지 않고 끝나면 오른다 · 무너지면 별명이 붙는다
            {
                var w = DayOne(seed, "Mirinae");
                float t0 = w.Command.ComputerTrust;
                var room = StoreRoom(w);
                ClearRoom(w, room);
                BigFire(w, room);
                for (int m = 0; m < 240 && w.Automation.FireCases.Count > 0 || m < 10; m++) Run(w, SimTime.Minutes(1));
                float t1 = w.Command.ComputerTrust;
                w.Command.ComputerTrust = 0.2f;
                Run(w, SimTime.Minutes(1));
                string nick = w.Minds.ComputerNick;
                bool logged = w.History.Events.Any(e => e.Text.Contains(nick) && nick != "");
                Check("컴퓨터 신뢰 — 수순대로 끄면 오르고, 무너지면 별명이 붙는다", t1 > t0 && nick == "양치기 V" && logged,
                    $"신뢰 {t0 * 100:0}% → {t1 * 100:0}% (질식 {w.Automation.Smothered} · 진공 {w.Automation.Vacuumed}) · 20%면 '{nick}'");
            }
            // 7) 결정론
            {
                uint H()
                {
                    var w = CrisisShip(seed, 0);
                    Run(w, SimTime.Hours(2));
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 아는 것·감정이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Storyteller.LevelValue = level0;
        Console.WriteLine(_fails == 0 ? "\n✔ 판단·인지·감정 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
