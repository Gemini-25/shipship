using System;
using System.Linq;
using ShipSim.Core;

// v23 방마다 천장 자동 소화 설비 점검 (--nozzletest): 컴퓨터가 열기 · 열 퓨즈 · 정전 · 바닥남 · 망가짐 → 손보기 · 지휘 카드 기록.
public static partial class Program
{
    private static int RunNozzleTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"천장 자동 소화 설비 점검 (v23) · 시드 {seed}\n");
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(2)); return w; }
            Room Quiet(World w) => w.Ship.RoomsOf(RoomType.Storage).Concat(w.Ship.LiveRooms).First(r => !r.Detached && w.Crew.All(c => c.Room != r) && r.Cells.Count(w.Ship.IsOpenFloor) >= 6);
            void Light(World w, Room room, int n) { foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(n)) w.Fire.Ignite(c, 0.9f); }

            // 1) 방마다 설비가 있다
            {
                var w = Fresh();
                var live = w.Ship.LiveRooms.ToList();
                Check("방마다 천장 소화 설비 — 처음엔 탱크가 가득하다", live.All(r => w.Nozzles.Has(r) && r.NozzleAgent >= 0.99f && !r.NozzleBroken),
                    $"방 {live.Count} · 설비 {live.Count(w.Nozzles.Has)} · 가득 {live.Count(r => r.NozzleAgent >= 0.99f)}");
            }

            // 2) 컴퓨터가 알면 바로 연다 → 불이 꺼지고 닫는다 · 지휘 건에 적힌다
            NozzleSystem.StickBase = 0f;
            {
                var w = Fresh();
                var room = Quiet(w);
                Light(w, room, 4);
                long t0 = w.Tick;
                long opened = -1;
                for (int m = 0; m < 6 && opened < 0; m++) { Run(w, SimTime.Minutes(1)); if (room.NozzleOn) opened = w.Tick; }
                for (int m = 0; m < 30 && (room.NozzleOn || w.Fire.CountIn(room) > 0); m++) Run(w, SimTime.Minutes(1));
                var k = w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == room.Id);
                Check("컴퓨터가 불을 알면 천장 소화 설비를 바로 연다", opened >= 0 && opened - t0 <= SimTime.Minutes(3) && w.Nozzles.Stats.ByComputer >= 1,
                    $"연 때 {(opened - t0) / (float)SimTime.Minutes(1):0.0}분 · {w.Nozzles.Stats.Line()}");
                Check("불이 꺼지면 잠그고 · 약제가 줄었다 · 바닥이 젖었다", !room.NozzleOn && w.Fire.CountIn(room) == 0 && room.NozzleAgent < 0.99f && room.NozzleAgent > 0f && w.Nozzles.Stats.Out >= 1
                        && room.Cells.Any(c => w.Matter.LitersAt(c) > 0f),
                    $"약제 {room.NozzleAgent * 100f:0}% · 젖은 칸 {room.Cells.Count(c => w.Matter.LitersAt(c) > 0f)} · {w.Nozzles.Stats.Line()}");
                Check("지휘 건에 천장 소화가 적힌다", k != null && k.Notes.Any(n => n.Text.Contains("천장 소화")),
                    k == null ? "지휘 건 없음" : string.Join(" / ", k.Notes.Select(n => n.Text)));
            }

            // 3) 주 컴퓨터 · 예비 코어가 멎었다: 컴퓨터는 못 연다 → 열 퓨즈가 녹을 만큼 뜨거워지면 저절로
            {
                var w = Fresh();
                var room = Quiet(w);
                w.Automation.Backup = false;
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(f.Machine!, FaultKind.Wrecked);
                Run(w, SimTime.Minutes(1));
                Light(w, room, 4);
                Run(w, SimTime.Minutes(1));
                bool early = room.NozzleOn;
                room.Air.Temperature = NozzleSystem.FuseTemp(room) + 3f;
                for (int m = 0; m < 3 && !room.NozzleOn; m++) Run(w, 10);
                Check("컴퓨터가 멎으면 열지 못하고 · 뜨거워지면 열 퓨즈가 녹아 저절로 열린다", !w.Automation.Dampers && !early && room.NozzleOn && room.NozzleByFuse && w.Nozzles.Stats.ByFuse >= 1,
                    $"처음 {(early ? "열림" : "닫힘")} → {w.Nozzles.State(room)} · {w.Nozzles.Stats.Line()}");
            }

            // 3b) 밸브가 안 열린다 → 지휘관이 로봇을 들여보낸다
            NozzleSystem.StickBase = 1f;
            {
                var w = Fresh();
                var room = Quiet(w);
                Light(w, room, 3);
                for (int m = 0; m < 4; m++) Run(w, SimTime.Minutes(1));
                var k = w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == room.Id);
                Check("밸브가 안 열리면 고장으로 적고 로봇이 들어가 끈다 · 손보기 일이 생긴다", room.NozzleBroken && w.Nozzles.Stats.Stuck >= 1 && k != null && k.Notes.Any(n => n.Text.Contains("고장")) && k.Sent >= 1,
                    k == null ? "지휘 건 없음" : string.Join(" / ", k.Notes.Select(n => n.Text)));
            }
            NozzleSystem.StickBase = 0f;

            // 4) 정전: 열어도 펌프가 안 돈다
            {
                var w = Fresh();
                var room = Quiet(w);
                room.BreakerOff = true;
                Run(w, 10);
                float before = room.NozzleAgent;
                Light(w, room, 3);
                for (int m = 0; m < 3; m++) { room.BreakerOff = true; Run(w, SimTime.Minutes(1)); }
                Check("정전된 방은 열어도 뿌리지 못한다 (약제가 그대로)", !room.Powered && !w.Nozzles.Spraying(room) && room.NozzleAgent >= before - 0.001f && (w.Nozzles.Stats.NoPower >= 1 || !room.NozzleOn),
                    $"{w.Nozzles.State(room)} · 전기 {(room.Powered ? "있음" : "없음")} · {w.Nozzles.Stats.Line()}");
            }

            // 5) 바닥남 · 망가짐 → 로봇(없으면 사람)이 손본다
            {
                var w = Fresh();
                var a = Quiet(w);
                var b = w.Ship.LiveRooms.First(r => r != a && !r.Detached && w.Crew.All(c => c.Room != r) && r.Cells.Count(w.Ship.IsOpenFloor) >= 4);
                a.NozzleAgent = 0f;
                w.Nozzles.Break(b, "시험 — 분사구를 깼다", melted: false);
                float water0 = w.Water.Level;
                bool aDone = false, bDone = false;
                string byA = "", byB = "";
                for (int m = 0; m < 360 && !(aDone && bDone); m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (!aDone && a.NozzleAgent >= 0.99f) { aDone = true; byA = Who(w, a); }
                    if (!bDone && !b.NozzleBroken) { bDone = true; byB = Who(w, b); }
                }
                Check("바닥난 탱크는 배의 물로 채운다", aDone && w.Water.Level < water0 + 50f, $"{w.Nozzles.State(a)} · {byA} · 물 {water0:0}→{w.Water.Level:0}L");
                Check("망가진 분사구는 퓨즈 하나로 간다", bDone, $"{w.Nozzles.State(b)} · {byB} · {w.Nozzles.Stats.Line()}");
            }

            // 6) 저장 · 되살림
            {
                var w = Fresh();
                var room = Quiet(w);
                room.NozzleAgent = 0.4f;
                room.NozzleWear = 0.3f;
                var bytes = StateSave.Write(w);
                var w2 = StateSave.Read(bytes);
                var r2 = w2.Ship.Rooms[room.Id];
                Check("저장 · 되살림 — 약제 · 낡음이 남는다", MathF.Abs(r2.NozzleAgent - 0.4f) < 0.001f && MathF.Abs(r2.NozzleWear - 0.3f) < 0.001f, $"{r2.NozzleAgent:0.00} · {r2.NozzleWear:0.00}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        finally { NozzleSystem.StickBase = 0.03f; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }

    private static string Who(World w, Room room)
    {
        var e = w.Log.Entries.LastOrDefault(x => x.Text.Contains(room.Name) && x.Text.Contains("소화 설비") && (x.Text.Contains("채웠다") || x.Text.Contains("갈았다")));
        return e == null ? "누가 했는지 기록 없음" : e.Text;
    }
}
