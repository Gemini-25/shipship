using System;
using System.Linq;
using ShipSim.Core;

// v20 관제 화면의 손: 제안 받기 · 거절, 권한, 맡은 일 — 기록되어 불러오기가 같은 틱에 같은 결정을 다시 한다 (--ctltest).
public static partial class Program
{
    private static int RunCtlTest(int seed)
    {
        _fails = 0;
        var w = World.CreateDefault(seed, 0, "Hanbit");
        Run(w, SimTime.Hours(1));
        var a = w.Automation;
        // 1) 권한: 자원 갈래를 자동 실행으로 → 회의가 준 권한이 바뀌고 기록에 남는다
        var before = a.Authority.Granted0(Domain.Resources);
        Player.Authority(w, Domain.Resources, AuthLevel.Auto);
        Check("권한 — 관찰자가 자원 갈래를 자동 실행으로 · 바뀐 기록이 남는다",
            a.Authority.Granted0(Domain.Resources) == AuthLevel.Auto && a.Authority.Changes.Any(c => c.Domain == Domain.Resources && c.To == AuthLevel.Auto) && w.Commands.Any(c => c.Kind == "auth"),
            $"{ComputerAuthority.LevelName(before)} → {ComputerAuthority.LevelName(a.Authority.Granted0(Domain.Resources))}");
        // 2) 맡은 일: 하나를 거두면 사라지고, 다시 맡기면 돌아온다
        var mod = a.Modules.OrderBy(m => (int)m).First();
        Player.Duty(w, mod, false);
        bool gone = !a.Has(mod);
        Run(w, SimTime.Hours(1));
        Player.Duty(w, mod, true);
        Check("맡은 일 — 거두면 사라지고 다시 맡기면 돌아온다 (둘 다 기록)", gone && a.Has(mod) && w.Commands.Count(c => c.Kind == "duty") == 2, AutomationSystem.ModuleName(mod));
        // 3) 제안: 묻게 한 뒤 불을 내 제안이 오르면 관찰자가 받는다
        Player.Policy(w, "computerask", 2);
        Player.Fire(w, w.Ship.RoomsOf(RoomType.Storage).First().Cells.First(w.Ship.IsOpenFloor));
        Proposal? p = null;
        for (int i = 0; i < SimTime.Hours(3) && p == null; i++) { w.Step(); p = a.Asks.Open.FirstOrDefault(); }
        bool asked = p != null && Player.Ask(w, p.Id, true);
        Check("제안 — 관찰자가 함장 대신 받으면 '받음' · 관찰자 몫으로 센다 (기다리는 제안이 없으면 거르기)",
            p == null || asked && p.State == ProposalState.Accepted && p.DecidedBy == "관찰자" && a.Asks.ByPlayer >= 1 && !Player.Ask(w, p.Id, false),
            p == null ? "이번 항해엔 제안이 오르지 않았다" : $"{p.Title} · {p.DecideWhy}");
        Run(w, SimTime.Hours(1));
        // 4) 불러오기: 시드 + 기록만으로 같은 상태 (지문이 같다)
        var runner = new ReplayRunner(SaveGame.Write(w));
        int guard = 0;
        while (!runner.Advance(50000) && guard++ < 400) { }
        uint h = SaveGame.StateHash(runner.World);
        Check("불러오기 — 권한 · 맡은 일 · 제안 결정을 같은 틱에 다시 해 지문이 같다", runner.Done && h == runner.ExpectedHash && h == SaveGame.StateHash(w),
            $"기록 {w.Commands.Count}줄 · 지문 {h:x8} / {runner.ExpectedHash:x8}");
        Console.WriteLine(_fails == 0 ? "\n✔ 관제 손 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
