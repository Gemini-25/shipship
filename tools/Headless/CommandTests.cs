using System;
using System.Linq;
using ShipSim.Core;

// v13.1 지휘: 선장 · 현장 지휘 · 조 편성 · 2인 1조 · 교대 · 불신임
public static partial class Program
{
    private static World CrisisShip(int seed, int command = 2)
    {
        var w = DayOne(seed, "Mirinae");
        w.Policies.Set("inertfire", 0, "시험");
        w.Policies.Set("vacuumfire", 0, "시험");
        w.Policies.Set("command", command, "시험");
        // 소화기는 창고 밖에도 (창고에만 있으면 불난 창고에서 꺼낼 수 없다)
        var spare = w.Ship.Containers.FirstOrDefault(f => f.Room != StoreRoom(w) && f.Storage!.Accepts(ItemKind.Extinguisher) && f.Storage.Free >= 3);
        spare?.Storage!.Add(ItemKind.Extinguisher, 3);
        BigFire(w, StoreRoom(w), 5);
        var lounge = w.Ship.RoomsOf(RoomType.Lounge).First();
        var wall = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == lounge).Select(kv => kv.Key).First();
        Hull.Damage(w.Ship, wall, 0.5f);
        return w;
    }

    private static int RunCommandTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"지휘 점검 (v13.1) · 시드 {seed}\n");
        try
        {
            // 1) 선장
            {
                var w = DayOne(seed, "Mirinae");
                var cap = w.Command.Captain;
                Check("선장 — 첫 출항 때 지휘 순서의 첫 사람, 스타일이 있다", cap != null && cap.Role == CrewRole.Engineer,
                    cap != null ? $"{cap.Name} ({CrewRoles.Name(cap.Role)}) · {CommandSystem.StyleName(w.Command.Style)} · 신뢰 {w.Command.Trust * 100:0}% · 지휘 솜씨 {CommandSystem.Leadership(cap) * 100:0}%" : "없음");
            }
            // 2) 조 편성: 불 + 파공 → 소화조·봉합조, 나머지는 대기조. 대기조는 불난 방에 몰려가지 않는다
            {
                var w = CrisisShip(seed, 0);
                var store = StoreRoom(w);
                int reserveInFire = 0, fireWork = 0;
                string plan = "";
                for (int m = 0; m < 30; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    var cmd = w.Command;
                    if (plan == "" && cmd.Active && cmd.Teams.Any(t => t.Kind == TeamKind.Fire)) plan = string.Join(" · ", cmd.Teams.Where(t => t.Kind != TeamKind.Reserve).Select(t => $"{CommandSystem.TeamName(t.Kind)} {w.Crew.First(c => c.Id == t.Worker).Name}{(t.Watcher >= 0 ? $"+감시 {w.Crew.First(c => c.Id == t.Watcher).Name}" : "")}"));
                    foreach (var c in w.Crew.Where(c => c.Room == store))
                        if (cmd.TeamOf(c) is { Kind: TeamKind.Reserve }) reserveInFire++;
                    fireWork += w.Crew.Count(c => c.Job?.Order is { Kind: WorkKind.Extinguish } && cmd.TeamOf(c) is { Kind: TeamKind.Fire });
                    if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1")
                        Console.WriteLine($"   {SimTime.Clock(w.Tick)} 불 {w.Fire.CountIn(store)} · " + string.Join(" | ", w.Crew.Select(c => $"{c.Name}[{(cmd.TeamOf(c) is Team tt ? CommandSystem.TeamName(tt.Kind) : "-")}] {c.Room?.Name} {c.Job?.Label ?? "-"} {(c.Job?.Order is WorkOrder jo ? jo.Kind.ToString() : "")}")));
                }
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1")
                    foreach (var o in w.Board.Open.Where(o => o.Urgency >= 0.85f)) Console.WriteLine($"   [일감] {o.Title} {o.Urgency:0.00} {o.Assignee?.Name} 보류 {o.BlockedReason}");
                Check("조 편성 — 위기면 지휘자가 소화조·봉합조를 짜고 나머지는 대기, 대기조는 불난 방에 몰리지 않는다",
                    plan.Length > 0 && fireWork > 0 && reserveInFire <= 2, $"지휘 {w.Command.CommanderName} · {plan} · 소화조가 끈 분 {fireWork} · 불난 방의 대기조 {reserveInFire}");
            }
            // 3) 2인 1조: 위험한 방은 짝이 문 밖에서 지키고, 안에서 쓰러지면 바로 끌어낸다
            {
                var w = CrisisShip(seed, 0);
                var store = StoreRoom(w);
                CrewMember? worker = null, watcher = null;
                bool atDoor = false;
                for (int m = 0; m < 25 && !atDoor; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    var t = w.Command.Teams.FirstOrDefault(x => x.Kind == TeamKind.Fire && x.Watcher >= 0);
                    if (t == null) continue;
                    worker = w.Crew.First(c => c.Id == t.Worker);
                    watcher = w.Crew.First(c => c.Id == t.Watcher);
                    atDoor = watcher.Job?.Order?.Kind == WorkKind.SafetyWatch && watcher.Room != store && watcher.Job.Current is WaitToil;
                    if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2")
                    {
                        Console.WriteLine($"   {SimTime.Clock(w.Tick)} 짝 {watcher.Name} {watcher.Room?.Name} {watcher.Job?.Label} {watcher.Job?.Current?.GetType().Name} · 일꾼 {worker.Name} {worker.Room?.Name} {worker.Job?.Label} · 감시 일감 {w.Board.Open.Count(o => o.Kind == WorkKind.SafetyWatch)} · 평가 {watcher.LastEvaluations?.FirstOrDefault().Reason}");
                        Console.WriteLine($"      일꾼 마지막 판단 {SimTime.Clock(worker.LastThinkTick)} 다음 {SimTime.Clock(worker.NextThinkTick)} · {string.Join(" / ", worker.LastEvaluations.Take(3).Select(e => $"{e.Activity.Label} {e.Score:0.00} {e.Reason}"))} · 손에 {worker.Carrying?.Kind}");
                        var dist = w.Paths.Flood(worker.Cell, worker.PathProfile);
                        Console.WriteLine($"      작업 점수 지금: {new ChoresActivity().Score(worker, w, dist)}");
                        foreach (var o in w.Board.Open.Where(o => o.Kind is WorkKind.Extinguish or WorkKind.Repair && o.Urgency >= 0.8f).Take(4))
                            Console.WriteLine($"      {o.Title} 긴급 {o.Urgency:0.00} 맡은 {o.Assignee?.Name} 보류 {o.BlockedReason} {(o.BlockedUntil > w.Tick ? $"~{SimTime.Clock(o.BlockedUntil)}" : "")} 열림 {w.Board.AvailableTo(worker).Contains(o)} · {worker.Name} 끌림 {ChoresActivity.Appeal(worker, w, o, dist, out _):0.00} (지휘 {w.Command.Bias(worker, o):0.00} 위기 {Crisis.Bias(w, o):0.00}) 앎 {w.Minds.Aware(worker, o)}");
                    }
                }
                bool rescued = false; int took = 0;
                if (atDoor && worker != null && watcher != null)
                {
                    worker.EndJob(w, ToilStatus.Interrupted);
                    worker.Position = store.Cells.Where(w.Ship.IsOpenFloor).First().Center; worker.PreviousPosition = worker.Position;
                    worker.Vitals.Oxygen = 0.2f; worker.Down = true; worker.Pose = Pose.Down;
                    for (; took < 20 && !rescued; took++)
                    {
                        Run(w, SimTime.Minutes(1));
                        rescued = worker.CarriedBy == watcher || worker.Room != store || watcher.Job?.Order is { Kind: WorkKind.Rescue } ro && ro.Target.Crew == worker;
                    }
                }
                Check("2인 1조 — 짝이 문 밖에서 지키다가, 안에서 쓰러지면 곧장 구하러 간다", atDoor && rescued,
                    $"짝 {(watcher?.Name ?? "-")} 문 밖 {(atDoor ? "섰다" : "못 섰다")} · {(worker?.Name ?? "-")} 쓰러짐 → {(rescued ? $"{took}분 만에 구하러 감" : "구하지 못함")}");
            }
            // 4) 지휘 주체 (방침): 사람 → 선장, 컴퓨터 → 주 컴퓨터
            {
                string[] who = new string[2];
                for (int k = 0; k < 2; k++)
                {
                    var w = CrisisShip(seed, k);
                    for (int m = 0; m < 6; m++) Run(w, SimTime.Minutes(1));
                    who[k] = w.Command.Active ? w.Command.CommanderName : "(위기 아님)";
                    if (k == 0 && w.Command.Captain is CrewMember cap && w.Command.Commander != cap) who[k] += $" (선장 {cap.Name} 대신)";
                }
                Check("현장 지휘 방침 — 사람이면 선장이, 컴퓨터면 주 컴퓨터가 조를 짠다", !who[0].StartsWith("주 컴퓨터") && who[0] != "(위기 아님)" && who[1] == "주 컴퓨터",
                    $"사람: {who[0]} · 컴퓨터: {who[1]}");
            }
            // 5) 교대: 지친 조원은 대기조와 바꿔 재운다
            {
                var w = CrisisShip(seed, 0);
                CrewMember? tired = null;
                for (int m = 0; m < 30 && tired == null; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    var t = w.Command.Teams.FirstOrDefault(x => x.Kind != TeamKind.Reserve);
                    if (t != null && w.Command.Teams.Any(x => x.Kind == TeamKind.Reserve)) tired = w.Crew.First(c => c.Id == t.Worker);
                }
                bool rested = false;
                if (tired != null)
                {
                    tired.Needs.Rest = 0.1f;
                    for (int m = 0; m < 10 && !rested; m++) { Run(w, SimTime.Minutes(1)); tired.Needs.Rest = MathF.Min(tired.Needs.Rest, 0.1f); rested = w.Command.Resting(tired); }
                }
                Check("교대 — 지친 조원은 대기조와 바꿔 쉬게 한다", rested && w.Command.Rotations >= 1, $"{tired?.Name ?? "-"} · 교대 {w.Command.Rotations}");
            }
            // 6) 불신임 → 새 선장
            {
                var w = DayOne(seed, "Mirinae");
                var old = w.Command.Captain!;
                w.Command.Trust = 0.1f;
                for (int h = 0; h < 30 && w.Command.Elections == 0; h++) Run(w, SimTime.Hours(1));
                var now = w.Command.Captain;
                var line = w.History.Events.LastOrDefault(e => e.Text.Contains("불신임"));
                Check("불신임 — 신뢰가 무너지면 저녁 회의에서 물러나고 새 선장을 뽑는다", w.Command.NoConfidence >= 1 && now != null && now != old,
                    $"{line?.Text ?? "-"} · 새 선장 {now?.Name} ({CommandSystem.StyleName(w.Command.Style)})");
            }
            // 7) 결정론
            {
                uint H() { var w = CrisisShip(seed, 2); Run(w, SimTime.Hours(2)); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                Check("결정론 — 지휘가 든 배도 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 지휘 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
