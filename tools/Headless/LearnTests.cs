using System;
using System.Linq;
using System.Reflection;
using ShipSim.Core;

// v23 평소 수리 배치 · 복기 점검 (--learntest): 밀린 고장 일에 로봇을 붙인다 · 증원이 필요했으면 다음엔 처음부터 더 · 늦게 닿은 방은 거점 · 저장.
public static partial class Program
{
    private static int RunLearnTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"평소 수리 배치 · 복기 점검 (v23) · 시드 {seed}\n");
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(2)); return w; }
            void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);

            // 1) 밀린 일: 30분 넘게 아무도 안 맡은 조명 수리에 주 컴퓨터가 로봇을 붙인다
            {
                var w = Fresh();
                var room = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.LightsOut).OrderBy(r => r.Id).First();
                room.LightsOut = true;
                w.Board.RequestScan();
                WorkOrder? o = null;
                for (int t = 0; t < 40 && o == null; t++) { Run(w, 1); o = w.Board.OpenUnsorted.FirstOrDefault(x => x.Kind == WorkKind.FixLights && x.Target.Room == room && x.Robot == null && x.Assignee == null); }
                if (o == null) Check("밀린 고장 일에 주 컴퓨터가 한가한 로봇을 붙인다", false, "조명 수리 일이 안 섰다 (또는 바로 맡았다)");
                else
                {
                    typeof(WorkOrder).GetField("<Posted>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(o, w.Tick - SimTime.Minutes(40));
                    Call(w.Commander, "Backlog");
                    var line = w.Log.Entries.Where(e => e.Text.Contains("밀린 일")).Select(e => e.Text).LastOrDefault() ?? "";
                    Check("밀린 고장 일에 주 컴퓨터가 한가한 로봇을 붙인다", o.Robot != null && w.Commander.Lessons.Assigned >= 1, $"{o.Title} → {o.Robot?.Name ?? "없음"} · {line}");
                }
            }

            // 2) 복기: 증원이 필요했던 불 → 다음엔 처음부터 손을 더 보낸다
            {
                var w = Fresh();
                var room = w.Ship.RoomsOf(RoomType.Storage).Concat(w.Ship.LiveRooms).First(r => !r.Detached && w.Crew.All(c => c.Room != r) && r.Cells.Count(w.Ship.IsOpenFloor) >= 6);
                var past = new CommandCase { Id = 900, Kind = CaseKind.Fire, RoomId = room.Id, Opened = w.Tick - SimTime.Minutes(30), Reinforced = 2, Sent = 4 };
                past.OnScene = past.Opened + SimTime.Minutes(8);
                Call(w.Commander, "Review", past, "불을 껐다");
                var L = w.Commander.Lessons;
                Check("복기 — 증원이 필요했으면 다음엔 처음부터 손을 더 보낸다", L.Extra[0] >= 1f && L.More >= 1, L.Line());
                Check("복기 — 첫 손이 늦게 닿은 방은 거점을 더 챙긴다", L.SlowRooms.ContainsKey(room.Id) && L.Slow >= 1, $"{room.Name} · {string.Join(" / ", L.Recent)}");
                NozzleSystem.On = false;
                foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(4)) w.Fire.Ignite(c, 0.9f);
                CommandCase? k = null;
                for (int m = 0; m < 6 && (k == null || k.Sent < 4); m++) { Run(w, SimTime.Minutes(1)); k = w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == room.Id); }
                NozzleSystem.On = true;
                Check("배운 만큼 처음부터 더 보낸다 (불 4칸: 3대 → 4대)", k != null && k.Sent >= 4 && k.Reinforced == 0, k == null ? "지휘 건 없음" : $"출동 {k.Sent} · 증원 {k.Reinforced}");
            }

            // 3) 남아돈 작은 불 → 줄인다
            {
                var w = Fresh();
                var past = new CommandCase { Id = 901, Kind = CaseKind.Fire, RoomId = 0, Opened = w.Tick - SimTime.Minutes(10), Sent = 4, Peak = 1 };
                past.OnScene = past.Opened + SimTime.Minutes(1);
                past.Resolved = past.OnScene + SimTime.Minutes(1);
                Call(w.Commander, "Review", past, "불을 껐다");
                Check("복기 — 작은 불에 많이 보냈으면 줄인다", w.Commander.Lessons.Extra[0] < 0f && w.Commander.Lessons.Less >= 1, w.Commander.Lessons.Line());
            }

            // 4) 저장 · 되살림
            {
                var w = Fresh();
                w.Commander.Lessons.Extra[1] = 1.5f;
                w.Commander.Lessons.SlowRooms[3] = 2f;
                var w2 = StateSave.Read(StateSave.Write(w));
                Check("저장 · 되살림 — 복기 장부가 남는다", MathF.Abs(w2.Commander.Lessons.Extra[1] - 1.5f) < 0.001f && w2.Commander.Lessons.SlowRooms.TryGetValue(3, out var v) && v == 2f, w2.Commander.Lessons.Line());
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
