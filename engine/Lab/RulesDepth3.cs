using ShipEngine;

namespace Lab;

/// <summary>깊이 고침 1차 — 평시의 손 (docs/DEPTH_FIX.md DF5 · DF6 · 근거 docs/ENGINE_DEPTH_REVIEW.md §4-3 · §4-7 · §4-10 · §5).</summary>
public static partial class Program
{
    private static void Depth3Rules()
    {
        // ── DF5 보면 끈다 · 시신 수습 (보고서 §4-3 · §4-10 · §5): 켜 둔 오븐은 쓰는 이 없이 한 시간을 넘기지 않는다 · 숨진 이는 여섯 시간 안에 덮여 옮겨진다 ──
        if (Do("보면 끈다 · 수습"))
        {
            Console.WriteLine("\n[보면 끈다 · 시신 수습 — 켜 둔 오븐 · 숨진 이]");
            bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
            // 디딤호 6명 · 로봇 2 — 10분 뒤 주방 오븐을 켜 둔 채 자리를 떴다 (원인 마디) · 주컴퓨터가 돌면 주방 문을 잠가 아무도 못 들어가게 (주컴퓨터 몫만) · 멈췄으면 그대로 (사람 · 로봇의 눈)
            (long offAt, int by, string how, string story) Oven(bool computer)
            {
                var s = ShipWorld.Crewed("Didim", 9, 6, 2);
                var net = s.Get<Network>(); var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var st = s.Get<Structure>();
                if (!computer) net.SetOn(net.Devices.First(d => d.Type == "MainComputer").Id, false);   // 주컴퓨터를 끈 배
                s.Run(Time.Minutes(10));
                var oven = net.Devices.First(d => d.Type == "Oven" && s.Layout.Rooms[s.Layout.Room[d.Cell]].Type == "Galley");
                int galley = s.Layout.Room[oven.Cell];
                if (computer)
                    for (int d = 0; d < s.Layout.Doors.Count; d++)
                    {
                        Span<int> nb = stackalloc int[4];
                        s.Layout.Neighbors(s.Layout.Doors[d].Cell, nb);
                        foreach (int j in nb) if (j >= 0 && s.Layout.Room[j] == galley) { st.LockDoor(d, s.Tick + Time.Hours(2)); break; }
                    }
                if (computer)   // 주방 안에 있던 이는 식당으로 (잠근 문 안에서 오븐을 보지 않게)
                {
                    int mess = s.Layout.Rooms.First(r => r.Type == "Mess").Cells.First(c => s.Layout.Kind[c] == CellKind.Floor && s.Layout.FurnitureAt[c] < 0);
                    foreach (var a in m.Agents.Where(a => a.IsPerson && s.Layout.Room[a.C.Cell] == galley)) { a.C.Cell = mess; s.Get<Life>().Move(a.C.Body, mess); a.Plan.Clear(); a.Path.Clear(); }
                }
                int who = m.Agents.First(a => a.IsPerson).Id;
                int c0 = s.Causes.Add(CauseKind.Person, $"{Ko.I(m.NameOf(who))} {Ko.Eul(oven.Name)} 켜 둔 채 자리를 떴다", s.Tick, oven.Cell, 0, 0, who);
                net.SetOn(oven.Id, true); net.OnCause[oven.Id] = c0;
                long f0 = s.Facts.Total, start = s.Tick, offAt = -1;
                for (long k = 0; k < Time.Hours(1) && offAt < 0; k++) { s.Step(); if (net.On[oven.Id] == 0) offAt = s.Tick - start; }
                var sw = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Switched && x.Other == oven.Id);
                string how = sw.Kind == FactKind.Switched ? s.Causes.Text(sw.Cause) : "";
                if (Debug) Console.WriteLine($"    {(computer ? "주컴퓨터" : "멈춤")} · 꺼짐 {(offAt >= 0 ? Time.ToSeconds(offAt) / 60 : -1):0}분 · \"{how}\" · {string.Join(" / ", sm.Log.Where(l => l.text.Contains(oven.Name)).Select(l => l.text).Take(3))}");
                return (offAt, sw.Kind == FactKind.Switched ? sw.Actor : -9, how, sw.Kind == FactKind.Switched ? s.Causes.Story(sw.Cause) : "");
            }
            var aloneT = Task.Run(() => Oven(false)); var watched = Oven(true); var alone = aloneT.Result;
            Check("켜진 오븐이 쓰는 이 없이 1시간 넘게 켜져 있지 않다: 주방을 아무도 못 보면 주컴퓨터가 쓰는 이 없이 15분 켜진 오븐을 끈다 (경보 문턱 아래도) · 주컴퓨터가 멈췄으면 지나다 본 사람 · 로봇이 끈다 — 원인 사슬은 켜 둔 이에게 잇는다",
                watched.offAt >= 0 && watched.offAt <= Time.Minutes(20) && watched.by == -1 && watched.how.Contains("쓰는 이 없이") && watched.story.Contains("켜 둔 채")
                && alone.offAt >= 0 && alone.by >= 0 && alone.story.Contains("켜 둔 채"),
                $"주컴퓨터 {(watched.offAt >= 0 ? $"{Time.ToSeconds(watched.offAt) / 60:0}분" : "1시간 넘게 켜짐")} '{watched.how}' / 멈춤 {(alone.offAt >= 0 ? $"{Time.ToSeconds(alone.offAt) / 60:0}분" : "1시간 넘게 켜짐")} (끈 이 {alone.by}) '{alone.how}'");

            // 숨진 이: 디딤호 6명 · 로봇 2 — 30분 뒤 한 사람이 그 자리에서 숨졌다 (주컴퓨터가 돌 때 · 멈췄을 때) → 6시간 안에 덮여 쉴 곳(추모실 · 의무실)으로 옮겨진다 · 먼저 로봇
            (bool moved, bool covered, string room, string by, double hours) Corpse(bool computer)
            {
                var s = ShipWorld.Crewed("Didim", 4, 6, 2);
                var net = s.Get<Network>(); var m = s.Get<Mind>(); var life = s.Get<Life>();
                if (!computer) net.SetOn(net.Devices.First(d => d.Type == "MainComputer").Id, false);   // 주컴퓨터를 끈 배
                s.Run(Time.Minutes(30));
                var dead = m.Agents.Where(a => a.IsPerson && life[a.C.Body].Asleep == 0 && s.Layout.Room[a.C.Cell] >= 0 && s.Layout.Rooms[s.Layout.Room[a.C.Cell]].Type is not ("Medbay" or "Chapel")).OrderBy(a => a.Id).First();
                int body = dead.C.Body, at = life[body].Cell;
                int why = s.Causes.Add(CauseKind.Setup, $"{m.NameOf(dead.Id)}의 심장이 멎었다 — 타고난 심장병", s.Tick, at);
                { ref var b = ref life[body]; b.Alive = 0; b.Conscious = 0; b.DiedAt = s.Tick; b.DeathCause = why; }
                s.Facts.Emit(s.Tick, FactKind.Died, at, 0, why, actor: body);
                long start = s.Tick, doneAt = -1;
                for (long k = 0; k < Time.Hours(6) && doneAt < 0; k++) { s.Step(); if (life[body].Shroud != 0 && life[body].Cell != at && dead.C.CarriedBy < 0) doneAt = s.Tick - start; }
                var laid = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.LaidOut && x.Other == body);
                int r = s.Layout.Room[life[body].Cell];
                if (Debug) Console.WriteLine($"    {(computer ? "주컴퓨터" : "멈춤")} (켜짐 {s.Get<ShipMind>().Online}) · {m.NameOf(dead.Id)} {s.Layout.Rooms[s.Layout.Room[at]].Name} → {(r >= 0 ? s.Layout.Rooms[r].Name : "-")} · 덮음 {life[body].Shroud} · {(laid.Kind == FactKind.LaidOut ? s.Causes.Story(laid.Cause) : "-")}");
                return (life[body].Cell != at, life[body].Shroud != 0, r >= 0 ? s.Layout.Rooms[r].Type : "-",
                    laid.Kind == FactKind.LaidOut && laid.Actor >= 0 ? (m.Agents[laid.Actor].IsPerson ? "사람" : "로봇") : "-", doneAt >= 0 ? Time.ToSeconds(doneAt) / 3600 : -1);
            }
            var cAloneT = Task.Run(() => Corpse(false)); var cWatched = Corpse(true); var cAlone = cAloneT.Result;
            Check("시신이 6시간 넘게 그 자리에 있지 않다: 숨진 이를 로봇이 먼저 수습해 덮고 의무실로 옮긴다 (주컴퓨터가 맡기고 · 멈췄으면 돌던 로봇이 보고 스스로)",
                cWatched.moved && cWatched.covered && cWatched.room is "Medbay" or "Chapel" && cWatched.by == "로봇"
                && cAlone.moved && cAlone.covered && cAlone.room is "Medbay" or "Chapel",
                $"주컴퓨터: 옮김 {cWatched.moved} · 덮음 {cWatched.covered} · {cWatched.room} · {cWatched.by} · {cWatched.hours:0.0}시간 / 멈춤: 옮김 {cAlone.moved} · 덮음 {cAlone.covered} · {cAlone.room} · {cAlone.by} · {cAlone.hours:0.0}시간");
        }
    }
}
