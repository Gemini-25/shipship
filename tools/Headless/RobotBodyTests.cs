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

            // 11~13) 잔해 → 회수 → 해체 → 새 몸 조립
            {
                var w = DayOne(seed, "Hanbit");
                var shopTypes = new[] { RoomType.RobotBay, RoomType.Workshop, RoomType.Storage };
                var r = w.Robots.Robots.First(x => x.Operational && x.Room != null && !shopTypes.Contains(x.Room.Type));
                string name = r.Name;
                var dock = r.Dock;
                w.Robots.Hurt(r, BotPart.ArmL, 0.9f, "시험"); // 왼팔은 고철로
                w.Robots.Hurt(r, BotPart.Frame, 1.2f, "시험 — 무너진 격벽");
                bool hauled = false;
                for (int m = 0; m < 24 && !hauled; m++) { Run(w, SimTime.Minutes(10)); hauled = r.AtShop; }
                Check("회수 — 부서진 로봇은 잔해로 남고 · 쉬던 로봇이 정비실로 끌어 온다", r.Wrecked && hauled && w.Robots.Salvage.Hauled >= 1,
                    $"{name} 잔해 · {r.Room?.Name} · 정비실 {r.AtShop} · {w.Robots.Salvage.Line()}");
                int motors0 = w.Ship.CountStored(ItemKind.Motor), sensors0 = w.Ship.CountStored(ItemKind.Sensor);
                bool done = false;
                for (int m = 0; m < 36 && !done; m++) { Run(w, SimTime.Minutes(10)); done = r.Salvaged; }
                Check("해체 — 살아남은 부위는 부품으로 (모터 · 센서 …) · 망가진 부위는 고철로", done && w.Robots.Salvage.PartsBack >= 2 && w.Robots.Salvage.Scrap >= 1
                    && (w.Ship.CountStored(ItemKind.Motor) > motors0 || w.Ship.CountStored(ItemKind.Sensor) > sensors0),
                    $"해체 {done} · {w.Robots.Salvage.Line()} · {r.Marks.LastOrDefault().Text}");
                // 조립: 모자란 부품을 채워 둔다 (해체로 나온 것에 보탠다)
                foreach (var (k, n) in new[] { (ItemKind.Plate, 4), (ItemKind.Motor, 3), (ItemKind.Sensor, 2), (ItemKind.Electronics, 3), (ItemKind.CellPack, 2), (ItemKind.Cable, 3), (ItemKind.RobotLimb, 2) })
                    w.Ship.Containers.First(f => f.Storage!.Accepts(k) && f.Storage.Free > n).Storage!.Add(k, n);
                Robot? fresh = null;
                for (int m = 0; m < 96 && fresh == null; m++) { Run(w, SimTime.Minutes(10)); fresh = w.Robots.Robots.FirstOrDefault(x => x.FromWreck == name); }
                Check("조립 — 함대가 줄고 부품이 모이면 정비 로봇이 빈 충전대에 새 몸을 짜 맞춘다 (잔해의 이름을 잇는다)", fresh != null && fresh.Dock == dock && fresh.Rebuilt == 1 && w.Robots.Salvage.Built == 1,
                    fresh != null ? $"{fresh.Name} ← {fresh.FromWreck} · {fresh.Rebuilt}번째 몸 · {fresh.Marks.LastOrDefault().Text}" : $"없음 · {w.Robots.Salvage.Line()}");
            }

            // 14) 뜯어 살리기 — 잔해에서: 부품이 없어 멈춘 급한 로봇에게 잔해의 부위를
            {
                var w = DayOne(seed, "Hanbit");
                var medic = w.Robots.Robots.First(x => x.Operational && RobotSystem.Worth(x) >= 2.5f && x.State == RobotState.Docked);
                var wreck = w.Robots.Robots.First(x => x != medic && x.Operational && RobotSystem.Worth(x) < 2.5f);
                w.Robots.Hurt(wreck, BotPart.Frame, 1.2f, "시험");
                foreach (var f in w.Ship.Containers) foreach (var k in new[] { ItemKind.Motor, ItemKind.Bearing }) { int n = f.Storage!.Count(k); if (n > 0) f.Storage.Take(k, n); } // 새 모터도 · 임시로 되살릴 베어링도 없다
                w.Robots.ForceFault(medic, RobotFault.Drive);
                bool fixedIt = false;
                for (int m = 0; m < 48 && !fixedIt; m++) { Run(w, SimTime.Minutes(10)); fixedIt = medic.Fault == null; }
                Check("뜯어 살리기 — 모터가 배에 없으면 잔해의 바퀴로 급한 로봇을 살린다 (떼어 오든 해체해 나오든 · 멀쩡한 로봇은 건드리지 않는다)",
                    fixedIt && w.Robots.Salvage.FromRobots == 0 && w.Robots.Salvage.FromWrecks + w.Robots.Salvage.Dismantled >= 1,
                    $"{medic.Name}: 고장 {medic.Fault?.ToString() ?? "없음"} · {RobotBody.Line(medic)} · {w.Robots.Salvage.Line()}");
                if (w.Robots.Salvage.FromRobots > 0) // 실패 때: 무슨 일이 어떤 순서로
                {
                    Console.WriteLine($"      잔해 {wreck.Name}: {RobotBody.Line(wreck)} · 부위 {string.Join(" ", wreck.Parts.Select(x => x.ToString("0.00")))} · 고장 부위 {medic.FaultPart}");
                    foreach (var e in w.Log.Entries.Where(e => !e.Text.Contains("세워") && (e.Text.Contains("떼") || e.Text.Contains("해체") || e.Text.Contains("끌어") || e.Text.Contains("모터") || e.Text.Contains(wreck.Name))).Take(16))
                        Console.WriteLine($"      {SimTime.Clock(e.Tick)} {e.Text}");
                }
            }

            // 15) 뜯어 살리기 — 다른 로봇에서: 잔해도 없으면 덜 급한 로봇을 세워 떼어 낸다 (주 컴퓨터가 정하고 까닭을 남긴다)
            {
                var w = DayOne(seed, "Hanbit");
                var medic = w.Robots.Robots.First(x => x.Operational && RobotSystem.Worth(x) >= 2.5f && x.State == RobotState.Docked);
                foreach (var f in w.Ship.Containers) foreach (var k in new[] { ItemKind.Motor, ItemKind.Bearing }) { int n = f.Storage!.Count(k); if (n > 0) f.Storage.Take(k, n); } // 새 모터도 · 임시로 되살릴 베어링도 없다
                w.Robots.ForceFault(medic, RobotFault.Drive);
                Robot? donor = null;
                for (int m = 0; m < 48 && donor == null; m++) { Run(w, SimTime.Minutes(10)); donor = w.Robots.Robots.FirstOrDefault(x => x.Marks.Any(k => k.Text.Contains("떼였다"))); }
                Check("뜯어 살리기 — 잔해도 없으면 주 컴퓨터가 덜 급한 로봇의 부위를 떼어 살린다 (떼인 로봇은 멈추고 · 까닭이 남는다)", donor != null && donor.Fault != null && w.Robots.Salvage.FromRobots >= 1
                    && w.Log.Entries.Any(e => e.Text.Contains("덜 급한")),
                    donor != null ? $"{donor.Name}(급함 {RobotSystem.Worth(donor):0.0}) → {medic.Name}(급함 {RobotSystem.Worth(medic):0.0}) · {donor.Marks.LastOrDefault().Text} · {w.Robots.Salvage.Line()}" : $"없음 · {w.Robots.Salvage.Line()}");
            }

            // 16) 로봇 팔다리로 만든 사람의 의수: 잔해에서 떼어 둔 팔다리가 있으면 그걸로 · 누구의 팔이었는지 남는다
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.CanAct).OrderBy(x => x.Id).Last();
                var wd = new Wound { Part = BodyPart.LeftArm, Kind = WoundKind.Crush, Weight = 0.3f, Cause = "시험 — 격벽에 끼어", Lost = true };
                c.Vitals.Wounds.Add(wd);
                var box = w.Ship.Containers.First(f => f.Storage!.Accepts(ItemKind.RobotLimb) && f.Storage.Free > 3);
                box.Storage!.Add(ItemKind.RobotLimb, 1);
                box.Storage.Add(ItemKind.Electronics, 2);
                w.Robots.LimbSources.Add("정비 로봇 3");
                for (int m = 0; m < 72 && !wd.Prosthetic; m++) Run(w, SimTime.Minutes(10));
                Check("의수 — 잔해에서 떼어 둔 로봇 팔로 의수를 달고 · 누구의 팔이었는지 남는다 (보통 의수보다 잘 움직인다)",
                    wd.Prosthetic && wd.LimbFrom == "정비 로봇 3" && Wounds.Severity(c.Vitals, wd) < 0.1f && c.Memory.Marks.Any(m => m.Text.Contains("정비 로봇 3")),
                    $"{c.Name}: {Wounds.Summary(c.Vitals)} · 크기 {Wounds.Severity(c.Vitals, wd):0.00} · {c.Memory.Marks.LastOrDefault().Text}");
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
