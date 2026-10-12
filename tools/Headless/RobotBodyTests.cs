using System;
using System.Linq;
using ShipSim.Core;

// v22 기계의 상처 (1) 로봇 몸의 부위 점검 (--robotbodytest).
public static partial class Program
{
    private static int RunRobotBodyTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"로봇 몸의 부위 점검 (v22) · 시드 {seed}\n");
        try
        {
            // 1) 부위: 팔 달린 종류와 아닌 종류 · 처음엔 모두 멀쩡
            {
                var w = DayOne(seed, "Hanbit");
                var arm = w.Robots.Robots.FirstOrDefault(r => RobotBody.HasArms(r.Kind));
                var noArm = w.Robots.Robots.FirstOrDefault(r => !RobotBody.HasArms(r.Kind));
                bool fresh = w.Robots.Robots.All(r => r.Parts.Length == RobotBody.Count && r.Parts.All(x => x >= 0.999f) && r.Patches.All(x => x == 0));
                Check("부위 — 팔 달린 로봇과 아닌 로봇이 있고 · 처음엔 모든 부위가 멀쩡하다", arm != null && noArm != null && fresh,
                    $"{arm?.Name}: {string.Join(", ", RobotBody.All.Where(p => RobotBody.Has(arm!.Kind, p)).Select(p => RobotBody.Name(arm!.Kind, p)))} / {noArm?.Name}: {string.Join(", ", RobotBody.All.Where(p => RobotBody.Has(noArm!.Kind, p)).Select(p => RobotBody.Name(noArm!.Kind, p)))}");
            }

            // 2) 고장 → 그 부위 · 절뚝 · 고치면 덧댄 판
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational);
                float sp0 = RobotBody.SpeedMul(r);
                w.Robots.ForceFault(r, RobotFault.Drive);
                var st = RobotBody.StageOf(r, BotPart.Drive);
                float sp1 = RobotBody.SpeedMul(r);
                Check("고장 — 구동 모터 고장이면 궤도 · 바퀴가 망가진 단계로 내려가고 절뚝인다", st == PartStage.Broken && r.FaultPart == (int)BotPart.Drive && sp1 < sp0 * 0.6f,
                    $"{r.Name} {RobotBody.Name(r.Kind, BotPart.Drive)} {RobotBody.StageName(st)} · 걸음 {sp0:0.00} → {sp1:0.00}");
                var c = w.Crew.First(x => !x.Dead && !x.IsChild);
                w.Robots.Repaired(r, c, makeshift: false);
                Check("수리 — 사람이 고치면 그 부위가 돌아오고 덧댄 판이 남는다", r.Parts[(int)BotPart.Drive] >= 0.84f && r.Patches[(int)BotPart.Drive] == 1 && r.FaultPart < 0 && r.Marks.Any(m => m.Text.Contains("판을 덧댔다")),
                    $"{RobotBody.Line(r)} · {r.Marks.LastOrDefault().Text}");
            }

            // 3) 폭발 곁: 부위가 크게 상한다 (어느 부위인지는 그때그때)
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational && x.State == RobotState.Docked);
                w.Volatile.Blasts.Add(new BlastEvent(w.Tick + 1, r.Cell, 1.0f, "시험 — 압력 용기"));
                Run(w, SimTime.Minutes(2));
                int hurt = RobotBody.Damaged(r);
                Check("폭발 — 곁의 로봇은 부위가 크게 상하고 (몸통도 흔들린다) · 무엇에 다쳤는지 남는다", hurt >= 1 && r.LastHurt.Contains("폭발") && w.Robots.Body.Blasts >= 1,
                    $"{r.Name}: {RobotBody.Line(r)} · {r.LastHurt} · {w.Robots.Body.Line()}");
            }

            // 4) 두 팔을 잃으면 집는 일을 못 하고 · 도구를 잃으면 특기를 못 한다
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational && RobotBody.HasArms(x.Kind));
                w.Robots.Hurt(r, BotPart.ArmL, 1.1f, "시험 — 철판에 끼어");
                w.Robots.Hurt(r, BotPart.ArmR, 1.1f, "시험 — 철판에 끼어");
                Check("팔 — 두 팔을 다 잃으면 집는 일을 못 한다 (나르기 · 밸브 · 차단기는 된다)", RobotBody.Gripless(r) && RobotBody.HandJob(WorkKind.Repair) && !RobotBody.HandJob(WorkKind.CloseValve)
                    && w.Log.Entries.Any(e => e.Text.Contains(r.Name) && e.Text.Contains("떨어져 나갔다")),
                    $"{r.Name}: {RobotBody.Line(r)} · 일 배율 {RobotBody.WorkMul(r):0.00}");
            }

            // 5) 몸통이 떨어져 나가면 부서진다
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational);
                w.Robots.Hurt(r, BotPart.Frame, 1.2f, "시험 — 격벽이 무너져");
                Check("몸통 — 몸통이 떨어져 나가면 부서진다 (일 · 짐을 놓고 연대기에 남는다)", r.Wrecked && r.State == RobotState.Lost && w.Robots.Body.Wrecks == 1,
                    $"{r.Name} 부서짐 {r.Wrecked} · {r.Doing}");
            }

            // 6) 물 · 열: 방의 환경이 부위를 깎는다
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational && x.State == RobotState.Docked && x.Room != null);
                var room = r.Room!;
                for (int m = 0; m < 60; m++) { room.Flood = 95f; room.Air.Temperature = 110f; Run(w, SimTime.Minutes(1)); }
                Check("환경 — 물이 차면 구동부 · 동력부가 · 뜨거운 방에서는 동력부 · 제어기가 상한다", r.Parts[(int)BotPart.Drive] < 0.9f && r.Parts[(int)BotPart.Power] < 0.9f && r.Parts[(int)BotPart.Brain] < 1f && w.Robots.Body.Env > 0,
                    $"{r.Name}: {RobotBody.Line(r)} · {w.Robots.Body.Line()}");
            }

            // 7) 불길 속: 센서 렌즈가 먼저 그을고 · 몸통이 다 타면 부서진다
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational && !RobotsV15.Fireproof(x.Kind) && x.State == RobotState.Docked);
                long t0 = w.Tick; long sensorAt = -1;
                for (int m = 0; m < 120 && !r.Wrecked; m++)
                {
                    foreach (var x in Cell.Dirs4) if (w.Ship.IsWalkable(r.Cell + x) && !w.Fire.Fires.ContainsKey(r.Cell + x)) w.Fire.Ignite(r.Cell + x, 0.9f);
                    Run(w, SimTime.Minutes(1));
                    if (sensorAt < 0 && r.Parts[(int)BotPart.Sensor] < 0.5f) sensorAt = w.Tick - t0;
                }
                Check("불 — 센서가 먼저 그을고 · 불 속에 오래 있으면 몸통이 타 부서진다", sensorAt >= 0 && (r.Wrecked || r.Parts[(int)BotPart.Frame] < 0.5f),
                    $"{r.Name}: 센서 찌그러짐 {sensorAt / 60}분 · {RobotBody.Line(r)} · 부서짐 {r.Wrecked} · {r.Doing}");
            }

            // 8) 이어지는 고장: 고친 뒤에도 망가진 부위가 남았으면 그 부위 고장으로
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational && x.State == RobotState.Docked);
                w.Robots.ForceFault(r, RobotFault.Drive);
                r.Parts[(int)BotPart.Sensor] = 0.1f; // 같이 망가졌다
                var c = w.Crew.First(x => !x.Dead && !x.IsChild);
                w.Robots.Repaired(r, c, makeshift: false);
                Run(w, SimTime.Minutes(2));
                Check("이어서 — 하나를 고쳐도 망가진 센서가 남았으면 센서 고장으로 이어진다", r.Fault == RobotFault.Sensor && r.FaultPart == (int)BotPart.Sensor,
                    $"{r.Name}: 고장 {r.Fault} · 부위 {r.FaultPart} · {RobotBody.Line(r)}");
            }

            // 9) 저장 · 되살리기: 부위 · 덧댄 판이 그대로
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Robots.Robots.First(x => x.Operational);
                w.Robots.Hurt(r, BotPart.ArmL, 0.6f, "시험");
                w.Robots.Hurt(r, BotPart.Sensor, 0.3f, "시험");
                r.Patches[(int)BotPart.Frame] = 2;
                var bytes = StateSave.Write(w, out _);
                var b = StateSave.Read(bytes);
                var r2 = b.Robots.Robots.First(x => x.Id == r.Id);
                bool same = r2.Parts.SequenceEqual(r.Parts) && r2.Patches.SequenceEqual(r.Patches) && r2.LastHurt == r.LastHurt;
                Check("저장 — 되살린 세계에서도 부위 · 덧댄 판 · 마지막 상처가 같다", same, $"{RobotBody.Line(r)} ↔ {RobotBody.Line(r2)}");
            }

            // 10) 하루 지내기: 아무 일 없는 하루에도 닳음 고장이 부위로 남고 · 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay);
                    return SaveGame.StateHash(w);
                }
                uint h1 = H(), h2 = H();
                Check("결정론 — 같은 시드 하루 지문이 같다", h1 == h2, $"{h1:x8} / {h2:x8}");
            }
        }
        catch (Exception e) { Check("예외 없음", false, e.ToString()); }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
