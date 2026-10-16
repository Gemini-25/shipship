using System;
using System.Linq;
using ShipSim.Core;

// v23 큰 사고 여섯 점검 (--bigtest): 전자기 펄스 · 원자로 노심 카운트다운 · 작물 대역병 · 궤도에 붙잡힘 · 냉각 폭주 결빙 · 겹친 재난.
public static partial class Program
{
    private static int RunBigTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"큰 사고 여섯 점검 (v23) · 시드 {seed}\n");
        GenerationSystem.QuietOff = true; PlanetSystem.Off = true; PurposeSystem.Off = true;
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(1)); return w; }

            // 1) 전자기 펄스
            {
                var w = Fresh();
                var c = w.Big.Start(BigKind.Emp)!;
                bool off = c != null && c.Robots.All(id => w.Robots.Robots.First(r => r.Id == id).Disabled) && w.Automation.Rebooting;
                int bots = c?.Robots.Count ?? 0;
                Run(w, SimTime.Hours(3));
                int back3 = c == null ? 0 : c.Robots.Count(id => !w.Robots.Robots.First(r => r.Id == id).Disabled);
                for (int i = 0; i < 60 && c is { Done: false }; i++) Run(w, SimTime.Hours(1));
                var hand = w.Log.Entries.LastOrDefault(e => e.Text.Contains("손으로 다시 켰다"));
                Check("전자기 펄스 — 로봇 · 주컴퓨터가 꺼지고, 전기 아는 사람이 한 대씩 손으로 되살린다", off && back3 >= 1 && c!.Done && hand.Text != null,
                    $"꺼진 로봇 {bots} · 3시간 뒤 {back3}대 되살림 · {c?.Outcome} · {hand.Text}");
            }
            // 2) 원자로 노심 카운트다운
            {
                var w = Fresh();
                var c = w.Big.Start(BigKind.CoreCountdown);
                var core = w.Ship.FurnitureOf(FurnitureType.ReactorCore).First();
                bool stuck = core.Machine!.Faults.Any(f => f.Kind == FaultKind.ControlFault);
                for (int i = 0; i < 48 && c is { Done: false }; i++) Run(w, SimTime.Minutes(5));
                Check("원자로 노심 — 세 시간 안에 로봇(못 하면 사람)이 들어가 제어봉을 넣는다 · 끝내 못 하면 노심을 떼어 낸다", c != null && stuck && c.Done && c.Outcome.Length > 0,
                    $"{c?.Outcome} · {(w.Tick - (c?.Start ?? 0)) / SimTime.Minutes(1)}분 · 로봇 {c?.BotHero} · 사람 {c?.Hero}");
            }
            // 3) 작물 대역병
            {
                var w = Fresh();
                var c = w.Big.Start(BigKind.Blight);
                int rooms = c?.Rooms.Count ?? 0;
                Run(w, SimTime.Hours(80));
                Check("작물 대역병 — 재배실이 한꺼번에 시들고, 종자 보관소가 있으면 빨리 · 없으면 격리 재배로 되살린다", c != null && rooms >= 1 && c.Done,
                    $"재배실 {rooms} · {c?.Outcome} · 병충해 {w.Hazards.Count[(int)HazardKind.CropBlight]}");
            }
            // 4) 궤도에 붙잡힘
            {
                var w = Fresh();
                var c = w.Big.Start(BigKind.OrbitTrap);
                Run(w, SimTime.Hours(7));
                var argue = w.Log.Entries.Where(e => e.Text.Contains("버리자") || e.Text.Contains("남기자")).Select(e => e.Text).Take(2).ToList();
                Check("궤도에 붙잡힘 — 무게 버리기 회의(다투는 사람) · 짐을 버리고 빠져나온다 (모자라면 골조가 운다)", c != null && c.Done && c.Dumped > 0 && argue.Count >= 1,
                    $"{c?.Outcome} · {string.Join(" / ", argue)}");
            }
            // 5) 냉각 폭주 결빙
            {
                var w = Fresh();
                var c = w.Big.Start(BigKind.FreezeRunaway);
                var room = c == null ? null : w.Ship.Rooms.First(r => r.Id == c.Rooms[0]);
                float t0 = room?.Air.Temperature ?? 0f;
                Run(w, SimTime.Hours(32));
                Check("냉각 폭주 결빙 — 배 한쪽이 얼음 동굴이 되고 (동파 · 문 끼임 · 갇힌 사람) 로봇이 열을 끌어와 녹인다", c != null && t0 < -20f && c.Done && room!.Air.Temperature > 5f,
                    $"{string.Join(" · ", c?.Rooms.Select(id => w.Ship.Rooms.First(r => r.Id == id).Name) ?? Array.Empty<string>())} {t0:0}°C → {room?.Air.Temperature:0}°C · {c?.Outcome}");
            }
            // 6) 겹친 재난
            {
                var w = Fresh();
                w.Env.Force = EnvHazardSystem.Sample(EnvKind.Frozen);
                var c = w.Big.Start(BigKind.Compound);
                int open = w.Big.Open.Count();
                Check("겹친 재난 — 큰 사고 둘이 한꺼번에 (+ 환경 배 전체 사고)", c != null && open >= 2, $"{c?.Outcome} · 진행 중 {open} · {w.Big.Line()}");
                Run(w, SimTime.Hours(2));
                var snap = StateSave.Write(w);
                Check("상태 저장", snap.Length > 0, $"{snap.Length / 1024}KB");
            }
        }
        catch (Exception e) { Check("예외 없이", false, e.ToString()); }
        finally { GenerationSystem.QuietOff = false; PlanetSystem.Off = false; PurposeSystem.Off = false; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
