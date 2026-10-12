using System;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v22 연출 1 · 2: 로봇이 몸으로 막아 준다 · 그 순간이 남는다 (--guardtest).
public static partial class Program
{
    private static int RunGuardTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"로봇이 몸으로 막아 주기 점검 (v22) · 시드 {seed}\n");
        bool guard0 = RobotSystem.Guarding, sure0 = RobotSystem.SureReflex;
        try
        {
            // 같은 시드 두 세계: 한쪽은 로봇이 막고 · 한쪽은 못 막는다 (사람이 받은 피해를 견준다)
            (World w, CrewMember c, Vector2 dir) Scene()
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(1));
                foreach (var x in w.Crew.Where(x => !x.Dead && !x.IsChild && !x.Outside && x.Room != null && w.Robots.Robots.All(r => r.Wrecked || Vector2.Distance(r.Position, x.Position) > 3f)).OrderBy(x => x.Id)) // 곁에 다른 로봇이 없는 사람
                    foreach (var d in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
                    {
                        var at = Cell.FromPosition(x.Cell.Center + d * 2f);
                        var mid = Cell.FromPosition(x.Cell.Center + d);
                        if (w.Ship.RoomAt(at) == x.Room && w.Ship.RoomAt(mid) == x.Room && w.Ship.IsWalkable(mid) && w.Ship.Grid.Kind(at) == TileKind.Floor)
                        { x.Position = x.Cell.Center; x.PreviousPosition = x.Position; return (w, x, d); }
                    }
                throw new Exception("시험 자리를 못 찾았다");
            }
            Robot Bot(World w) => w.Robots.Robots.Where(r => r.Operational && !r.Wrecked).OrderByDescending(r => RobotSystem.Bulk(r.Kind)).ThenBy(r => r.Id).First();

            // 1) 폭발: 사이에 선 로봇이 압력 · 화구를 대신 받는다
            {
                RobotSystem.SureReflex = true;
                RobotSystem.Guarding = false;
                var (w0, c0, d0) = Scene();
                float h0 = c0.Vitals.Health;
                w0.Volatile.Blast(Cell.FromPosition(c0.Position + d0 * 2f), 0.6f, "시험 — 압력 용기가 터졌다");
                float lost0 = h0 - c0.Vitals.Health;
                RobotSystem.Guarding = true;
                var (w1, c1, d1) = Scene();
                var r = Bot(w1);
                w1.Robots.Stand(r, c1.Position + d1 * 0.9f);
                float h1 = c1.Vitals.Health, f1 = r.Parts[(int)BotPart.Frame];
                w1.Volatile.Blast(Cell.FromPosition(c1.Position + d1 * 2f), 0.6f, "시험 — 압력 용기가 터졌다");
                float lost1 = h1 - c1.Vitals.Health;
                Check("폭발 — 사이에 선 로봇이 압력 · 화구를 대신 받는다 (사람은 덜 다치고 · 로봇 몸통이 상한다)", lost0 > 0.05f && lost1 < lost0 * 0.6f && r.Parts[(int)BotPart.Frame] < f1 && r.Shielded >= 1,
                    $"로봇 없이 −{lost0 * 100:0}% · {r.Name}이 막고 −{lost1 * 100:0}% · {r.Name}: {RobotBody.Line(r)} · {w1.Robots.Guard.Line()}");
                Check("기록 — 로봇 이력 · 지켜 준 사람 · 사람의 기억 · 일기 · 연대기 · 화면 순간", r.Guarded.Contains(c1.Name) && r.Marks.Any(m => m.Text.Contains("몸으로 막았다"))
                    && c1.Memory.Marks.Any(m => m.Text.Contains("몸으로 막아 줬다")) && c1.Diary.Any(d => d.Item2.Contains("막아섰다"))
                    && w1.History.Events.Any(e => e.Text.Contains("몸으로 막았다")) && w1.Robots.MomentSerial >= 1,
                    $"{r.Marks.LastOrDefault().Text} · {c1.Diary.LastOrDefault().Item2} · 순간 {w1.Robots.MomentSerial}");
            }

            // 2) 파편: 날아온 쪽을 가린다
            {
                RobotSystem.Guarding = false;
                var (w0, c0, d0) = Scene();
                float h0 = c0.Vitals.Health;
                Shrapnel.HitCrew(w0, new Rng(5), c0, 0.4f, 1f, "운석 파편", "시험 — 파편", c0.Position + d0 * 3f);
                float lost0 = h0 - c0.Vitals.Health;
                RobotSystem.Guarding = true;
                var (w1, c1, d1) = Scene();
                var r = Bot(w1);
                w1.Robots.Stand(r, c1.Position + d1 * 0.9f);
                float h1 = c1.Vitals.Health;
                Shrapnel.HitCrew(w1, new Rng(5), c1, 0.4f, 1f, "운석 파편", "시험 — 파편", c1.Position + d1 * 3f);
                float lost1 = h1 - c1.Vitals.Health;
                Check("파편 — 운석 · 폭발 파편도 몸으로 막는다 (로봇이 날아온 쪽을 본다)", lost1 < lost0 * 0.5f && r.Shielded >= 1 && Vector2.Dot(r.Facing, d1) > 0.5f,
                    $"−{lost0 * 100:0}% → −{lost1 * 100:0}% · {r.Name}: {RobotBody.Line(r)}");
            }

            // 3) 막다 부서진다: 이미 상한 몸이면 그 자리에 쓰러지고 · 지킨 사람이 가장 아쉬워한다
            {
                RobotSystem.Guarding = true;
                var (w, c, d) = Scene();
                var r = Bot(w);
                w.Robots.Hurt(r, BotPart.Frame, 0.8f, "시험 — 지난번 사고");
                w.Robots.Stand(r, c.Position + d * 0.9f);
                w.Volatile.Blast(Cell.FromPosition(c.Position + d * 2f), 0.9f, "시험 — 연료 탱크가 터졌다");
                Check("부서짐 — 상한 몸으로 막다 부서지고 · 연대기 · 일기에 남는다 (지켜 준 사람이 가장 아쉬워한다)", r.Wrecked && w.Robots.Guard.Wrecked == 1
                    && w.History.Events.Any(e => e.Text.Contains("막다 부서졌다")) && c.Diary.Any(x => x.Item2.Contains("나 대신") || x.Item2.Contains("몸으로 막아 줬던")),
                    $"{r.Name} 부서짐 {r.Wrecked} · {string.Join(" / ", c.Diary.TakeLast(2).Select(x => x.Item2))}");
                Check("순간 — 부서진 순간이 화면용으로 남는다 (막아 선 자리 · 날아온 쪽 · 부서짐)", w.Robots.Moments.Any(m => m.Robot == r.Id && m.Wrecked && m.Crew == c.Id),
                    string.Join(" / ", w.Robots.Moments.Select(m => m.Text)));
            }

            // 4) 곁에 로봇이 없으면 막지 못한다 · 끄면 예전과 같다
            {
                RobotSystem.Guarding = true;
                var (w, c, d) = Scene();
                foreach (var r in w.Robots.Robots) if (Vector2.Distance(r.Position, c.Position) < 3f && !r.Wrecked) w.Robots.Stand(r, w.Robots.Robots.First(x => x != r).Dock.Room.Center);
                int miss0 = w.Robots.Guard.Missed;
                float h = c.Vitals.Health;
                Shrapnel.HitCrew(w, new Rng(5), c, 0.4f, 1f, "운석 파편", "시험 — 파편", c.Position + d * 3f);
                Check("없음 — 곁에 로봇이 없으면 막지 못한다 (그대로 다친다)", w.Robots.Guard.Missed == miss0 + 1 && c.Vitals.Health < h,
                    $"{w.Robots.Guard.Line()}");
            }

            // 5) 반사 판정: 로봇이 늘 제때 뛰어드는 것은 아니다
            {
                RobotSystem.SureReflex = false;
                RobotSystem.Guarding = true;
                var (w, c, d) = Scene();
                var r = Bot(w);
                for (int i = 0; i < 8; i++) { Run(w, 1); w.Robots.Stand(r, c.Position + d * 1.6f); Shrapnel.HitCrew(w, new Rng(5 + i), c, 0.12f, 1f, "운석 파편", "시험 — 파편", c.Position + d * 3f); }
                int shields = w.Robots.Guard.Shields, late = w.Robots.Guard.Late;
                Check("반사 — 멀리(1.6칸) 서 있으면 늦을 때도 있다 (막음과 늦음이 섞인다)", shields + late == 8 && shields >= 1 && late >= 1,
                    $"막음 {shields} · 늦음 {late}");
            }
        }
        catch (Exception e) { Console.WriteLine($"  ✘ 예외: {e}"); _fails++; }
        finally { RobotSystem.Guarding = guard0; RobotSystem.SureReflex = sure0; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
