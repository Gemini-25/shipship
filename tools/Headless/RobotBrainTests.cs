using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v23 로봇 두뇌 점검 (--robotbraintest): 사건 경험 · 동료 · 사람 기억 · 위험 판단 · 말투 · 다친 기억 · 끊긴 구역의 동료 무전 · 저장.
public static partial class Program
{
    private static int RunRobotBrainTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"로봇 두뇌 점검 (v23) · 시드 {seed}\n");
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(2)); return w; }
            Room Quiet(World w) => w.Ship.RoomsOf(RoomType.Storage).Concat(w.Ship.LiveRooms).First(r => !r.Detached && w.Crew.All(c => c.Room != r) && r.Cells.Count(w.Ship.IsOpenFloor) >= 6);

            // 1) 불 지휘 건을 끝까지 함께하면 경험 · 동료가 남고 · 말투는 위급하면 짧다
            {
                var w = Fresh();
                NozzleSystem.On = false; // 천장이 혼자 끄지 않게 (로봇 손이 닿게)
                var room = Quiet(w);
                foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(5)) w.Fire.Ignite(c, 0.9f);
                string terse = "";
                CommandCase? k = null;
                for (int m = 0; m < 60 && (k == null || k.Open); m++)
                {
                    Run(w, SimTime.Minutes(1));
                    k ??= w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == room.Id);
                    if (terse == "" && k != null) terse = k.Served.Select(id => w.Robots.Robots.First(r => r.Id == id).Mind.Why).FirstOrDefault(s => s.Contains(" · ")) ?? "";
                }
                NozzleSystem.On = true;
                var served = k?.Served.Select(id => w.Robots.Robots.First(r => r.Id == id)).ToList() ?? new();
                Check("불 지휘 건을 끝까지 함께하면 불 경험이 는다", k != null && !k.Open && served.Count >= 1 && served.All(r => RobotBrain.Exp(r, CaseKind.Fire) >= 1),
                    k == null ? "지휘 건 없음" : $"{k.Outcome} · {string.Join(" / ", served.Select(r => $"{r.Name}: {RobotBrain.Line(w, r)}"))}");
                Check("같이 뛴 로봇끼리 동료로 남는다", served.Count < 2 || served.All(r => served.Where(o => o != r).All(o => r.Mates.ContainsKey(o.Id))), $"{served.Count}대");
                Check("위급할 때 말은 짧다 (자리 · 할 일 · 거품)", terse.Contains(" · ") && terse.Length <= 60, terse);
            }

            // 2) 경험 · 손발 · 아는 사람 · 남은 힘이 고르기와 솜씨에 든다
            {
                var w = Fresh();
                var bots = w.Robots.Robots.Where(r => r.Operational).OrderBy(r => r.Id).Take(3).ToList();
                var a = bots[0]; var b = bots[1]; var c = bots[2];
                var pt = w.Crew.First(x => !x.Dead && !x.IsChild);
                var kc = new CommandCase { Id = 99, Kind = CaseKind.Casualty, CrewId = pt.Id };
                float before = RobotBrain.PickMul(w, a, kc, Array.Empty<int>());
                a.Known[pt.Id] = 2;
                float after = RobotBrain.PickMul(w, a, kc, Array.Empty<int>());
                Check("다친 사람 곁에 간 적이 있는 로봇은 그 사람이 다시 다치면 먼저 불린다", after < before * 0.8f, $"{before:0.00} → {after:0.00}");
                float f0 = RobotBrain.FireMul(b, w);
                b.CaseExp[0] = 5;
                float f1 = RobotBrain.FireMul(b, w);
                Check("불 경험이 쌓이면 거품을 더 잘 겨눈다 (다섯 번 +30%)", MathF.Abs(f1 - f0 - 0.3f) < 0.01f, $"{f0:0.00} → {f1:0.00}");
                float s1 = RobotBrain.SealMul(b);
                b.CaseExp[1] = 5;
                Check("파공 경험이 쌓이면 봉합이 빠르다 (다섯 번 −25%)", RobotBrain.SealMul(b) < s1 - 0.2f, $"{s1:0.00} → {RobotBrain.SealMul(b):0.00}");
                var kf = new CommandCase { Id = 98, Kind = CaseKind.Fire, Size = 8 };
                float healthy = RobotBrain.PickMul(w, c, kf, Array.Empty<int>());
                c.Condition = 0.4f; c.Scares = 4;
                float frail = RobotBrain.PickMul(w, c, kf, Array.Empty<int>());
                kf.Life = true;
                float life = RobotBrain.PickMul(w, c, kf, Array.Empty<int>());
                Check("남은 힘: 낡고 덴 적 많은 로봇은 큰 불에 덜 불린다 · 사람 목숨이 걸리면 아끼지 않는다", frail > healthy * 1.5f && life <= healthy + 0.001f, $"멀쩡 {healthy:0.00} · 낡고 덴 {frail:0.00} · 목숨 {life:0.00}");
                c.Mates[a.Id] = 3;
                float mate = RobotBrain.PickMul(w, c, new CommandCase { Id = 97, Kind = CaseKind.Breach }, new[] { a.Id });
                float noMate = RobotBrain.PickMul(w, c, new CommandCase { Id = 96, Kind = CaseKind.Breach }, Array.Empty<int>());
                Check("손발 맞는 동료가 현장에 있으면 증원 때 먼저 불린다", mate < noMate, $"{noMate:0.00} → {mate:0.00}");
            }

            // 3) 지휘 건에서 다치면 기억한다
            {
                var w = Fresh();
                var r = w.Robots.Robots.First(x => x.Operational);
                r.CaseId = 1;
                w.Robots.Hurt(r, BotPart.Frame, 0.3f, "시험 — 불길");
                r.CaseId = -1;
                Check("지휘 건에서 다치면 다친 기억이 남는다", r.Scares >= 1, $"{r.Name} 다친 적 {r.Scares}");
            }

            // 4) 끊긴 구역에서 혼자 벅찬 불이면 동료를 직접 부른다
            {
                var w = Fresh();
                NozzleSystem.On = false;
                var core = w.Automation.ComputerBody?.Room;
                var pair = w.Relays.Zones.Where(z => core == null || !z.Rooms.Contains(core.Id))
                    .Select(z => (z, bots: w.Robots.Robots.Where(r => r.Operational && r.CaseId < 0 && r.Room != null && z.Rooms.Contains(r.Room.Id) && r.Foam > 0.3f).OrderBy(r => r.Id).ToList()))
                    .FirstOrDefault(t => t.bots.Count >= 2 && t.bots.Any(b => b.Room!.Cells.Count(w.Ship.IsOpenFloor) >= 8 && w.Crew.All(c => c.Room != b.Room)));
                if (pair.z == null) Check("끊긴 구역에서 혼자 벅찬 불이면 동료를 직접 무전으로 부른다", false, "알맞은 구역 없음");
                else
                {
                    var bot = pair.bots.First(b => b.Room!.Cells.Count(w.Ship.IsOpenFloor) >= 8 && w.Crew.All(c => c.Room != b.Room));
                    var room = bot.Room!;
                    w.Relays.Break(pair.z, "시험");
                    Run(w, 10);
                    foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - bot.Position).LengthSquared()).ThenBy(c => c.X).Skip(1).Take(5)) w.Fire.Ignite(c, 0.9f);
                    for (int t = 0; t < 8 && w.Relays.Stats.PeerCalls == 0; t++) Run(w, 15);
                    Check("끊긴 구역에서 혼자 벅찬 불이면 동료를 직접 무전으로 부른다", w.Relays.Stats.PeerCalls >= 1, $"{room.Name} · {w.Relays.Stats.Line()}");
                }
                NozzleSystem.On = true;
            }

            // 5) 저장 · 되살림
            {
                var w = Fresh();
                var r = w.Robots.Robots.First(x => x.Operational);
                r.CaseExp[2] = 3; r.Mates[7] = 2; r.Known[1] = 4; r.Scares = 2;
                var w2 = StateSave.Read(StateSave.Write(w));
                var r2 = w2.Robots.Robots.First(x => x.Id == r.Id);
                Check("저장 · 되살림 — 경험 · 동료 · 아는 사람 · 다친 기억", r2.CaseExp[2] == 3 && r2.Mates.GetValueOrDefault(7) == 2 && r2.Known.GetValueOrDefault(1) == 4 && r2.Scares == 2, RobotBrain.Line(w2, r2));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        finally { NozzleSystem.On = true; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
