using System;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v23 주컴퓨터 사고 지휘관 점검 (--commandertest): 전권 고정 · 불 · 파공 · 다친 사람 · 지휘가 넘어가는 순서 · 거점.
public static partial class Program
{
    private static int RunCommanderTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"주컴퓨터 사고 지휘관 점검 (v23) · 시드 {seed}\n");
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(2)); return w; }
            Room Quiet(World w) => w.Ship.RoomsOf(RoomType.Storage).Concat(w.Ship.LiveRooms).First(r => !r.Detached && w.Crew.All(c => c.Room != r) && r.Cells.Count(w.Ship.IsOpenFloor) >= 6);

            // 1) 전권 고정
            {
                var w = Fresh();
                w.Policies.Set("autoscope", 0, "시험 — 줄이자");
                w.Policies.Set("computerask", 2, "시험 — 묻게 하자");
                w.Automation.Authority.Set(Domain.Crisis, AuthLevel.Advise, "시험");
                Check("전권 고정 — 회의 · 방침 · 시험 어디서도 주컴퓨터 권한을 줄이지 못한다", w.Policies["autoscope"] == 2 && w.Policies["computerask"] == 0
                    && PolicySystem.ComputerFixed.Keys.All(PolicySystem.IsFixed) && ComputerAuthority.Domains.All(d => w.Automation.Authority.Level(d) == AuthLevel.Auto),
                    $"자동 실행 {w.Policies.Option("autoscope")} · 묻기 {w.Policies.Option("computerask")} · 갈래 {string.Join(" ", ComputerAuthority.Domains.Select(d => $"{ComputerAuthority.DomainName(d)}={ComputerAuthority.LevelName(w.Automation.Authority.Level(d))}"))}");
            }

            // 2) 불: 지휘 건 · 출동 · 현장 · 닫기
            {
                var w = Fresh();
                var room = Quiet(w);
                foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(4)) w.Fire.Ignite(c, 0.9f);
                long t0 = w.Tick;
                CommandCase? k = null;
                for (int m = 0; m < 5 && k == null; m++) { Run(w, SimTime.Minutes(1)); k = w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == room.Id); }
                Run(w, 10);
                Check("불 — 알아채면 바로 지휘 건이 열리고 로봇이 하던 일을 내려놓고 출동한다", k != null && k.Sent >= 2 && (k.Opened - t0) <= SimTime.Minutes(5),
                    k == null ? "지휘 건 없음" : $"#{k.Id} {k.Source} · 출동 {k.Sent} · {string.Join(" / ", k.Notes.Select(n => n.Text))}");
                for (int m = 0; m < 60 && k != null && k.Open; m++) Run(w, SimTime.Minutes(1));
                Check("불 — 현장에 닿아 끄고 · 확인한 뒤 닫는다 (로봇은 풀려난다)", k != null && !k.Open && k.OnScene >= 0 && k.Outcome.Contains("불을 껐다") && w.Robots.Robots.All(r => r.CaseId != k.Id),
                    k == null ? "" : $"{k.Outcome} · 첫 도착 {(k.OnScene - t0) / (float)SimTime.Minutes(1):0}분 · {(k.ClosedAt - t0) / (float)SimTime.Minutes(1):0}분 · {w.Commander.Stats.Line()}");
            }

            // 3) 파공: 로봇이 봉합 일에 직접 붙는다
            {
                var w = Fresh();
                var ship = w.Ship;
                var room = ship.LiveRooms.Where(r => ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r) && w.Crew.All(c => c.Room != r)).OrderBy(r => r.Id).First();
                var wall = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == room).Select(kv => kv.Key).OrderBy(c => c.X).ThenBy(c => c.Y).First();
                Hull.Damage(ship, wall, 1.2f);
                bool robotSeal = false;
                CommandCase? k = null;
                for (int m = 0; m < 20 && !robotSeal; m++)
                {
                    Run(w, SimTime.Minutes(1)); k = w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Breach); robotSeal = k != null && k.Notes.Any(n => n.Text.Contains("봉합 시작"));
                    if (Environment.GetEnvironmentVariable("CT_DEBUG") == "1" && k != null)
                        Console.WriteLine($"    [{m}분] {room.Name} 새는 {room.Leaking} · 봉합 일 {string.Join(",", w.Board.OpenUnsorted.Where(o => o.Kind == WorkKind.SealBreach).Select(o => $"{o.Title}({o.Target.CurrentRoom?.Name}) 사람 {o.Assignee?.Name ?? "-"} 로봇 {o.Robot?.Name ?? "-"}"))} · "
                            + string.Join(" / ", k.Bots.Select(id => w.Robots.Robots.First(r => r.Id == id)).Select(r => $"{r.Name}@{r.Room?.Name}:{r.Doing} [{r.Mind.Why}]")));
                }
                Check("파공 — 지휘 건 로봇이 봉합 일에 직접 붙는다 (사람 몫이어도 함께)", k != null && robotSeal, k == null ? "지휘 건 없음" : string.Join(" / ", k.Notes.Select(n => n.Text)));
            }

            // 4) 다친 사람: 곧장 지혈
            {
                var w = Fresh();
                var v = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Outside && c.Room != null).OrderBy(c => c.Id).First();
                w.Casualty.Inflict(v, TraumaKind.Bleed, 0.25f, "시험 — 크게 베였다");
                bool pressed = false;
                for (int m = 0; m < 15 && !pressed; m++) { Run(w, SimTime.Minutes(1)); pressed = w.Robots.Robots.Any(r => r.CaseId >= 0 && r.Doing.Contains("누르") && Vector2.Distance(r.Position, v.Position) < 2.4f); }
                var k = w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Casualty && c.CrewId == v.Id);
                Check("다친 사람 — 지휘 건 로봇이 곁에 닿아 상처를 누른다", k != null && pressed, k == null ? "지휘 건 없음" : string.Join(" / ", k.Notes.Select(n => n.Text)));
            }

            // 5) 지휘가 넘어가는 순서: 주 컴퓨터 → 보조 두뇌 · 로봇 대장 → 사람
            {
                var w = Fresh();
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(f.Machine!, FaultKind.Wrecked);
                Run(w, SimTime.Minutes(2));
                var t1 = w.Commander.Tier;
                var lead = w.Commander.LeaderId >= 0 ? w.Robots.Robots.First(r => r.Id == w.Commander.LeaderId).Name : "-";
                foreach (var r in w.Robots.Robots) r.Disabled = true;
                Run(w, SimTime.Minutes(2));
                var t2 = w.Commander.Tier;
                Check("지휘 순서 — 주 컴퓨터가 부서지면 보조 두뇌나 로봇 대장이 잇고 · 로봇도 없으면 사람", t1 is CommandTier.Backup or CommandTier.RobotLeader && t2 == CommandTier.Human && w.History.Events.Any(e => e.Text.Contains("지휘")),
                    $"{IncidentCommander.TierName(t1)} (대장 {lead}) → {IncidentCommander.TierName(t2)} · {w.Commander.Stats.Line()}");
            }

            // 6) 거점: 평소에 불이 잘 나는 방 곁에 로봇을 세운다
            {
                var w = Fresh();
                Run(w, SimTime.Minutes(30));
                var posted = w.Robots.Robots.Where(r => r.Post != null).ToList();
                Check("거점 — 평소에 불이 잘 나는 방 곁에 로봇을 세운다", posted.Count >= 1,
                    string.Join(" / ", posted.Select(r => $"{r.Name} → {w.Ship.Rooms[r.PostRoom].Name} ({r.Doing})")));
            }
        }
        catch (Exception e) { Console.WriteLine($"  ✘ 예외: {e}"); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
