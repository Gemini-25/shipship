using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.20 우주선급 주컴퓨터: 첫날 모듈 전부 · 다쳐도 느려질 뿐 · 원격으로 되는 건 직접 · 미리 견줘 보고 고른다 · 성격이 자란다
public static partial class Program
{
    private static int RunShipComputerTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"우주선급 주컴퓨터 점검 (v16.20) · 시드 {seed}\n");
        string scOnly = Environment.GetEnvironmentVariable("SC_ONLY") ?? ""; // v16.26 장면 하나만 (SC_ONLY=breaker,breach …)
        bool Sc(string k) => scOnly == "" || scOnly.Split(',').Contains(k);
        try
        {
            // ① 첫날부터 모듈 전부 · 연구 · 기술 · 겪은 일은 품질(정확도 · 속도 · 동시 처리 · 예측 거리)을 올린다
            if (Sc("modules"))
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                var c = a.Core;
                int all = Enum.GetValues<ComputerModule>().Length;
                float acc0 = c.Accuracy, hor0 = c.Horizon; int con0 = c.Concurrency;
                w.Eras.Known.Add("centralcpu");
                w.Eras.Known.Add("distctrl");
                a.Install(ComputerModule.Foresight, "중앙 컴퓨터"); // 이미 있는 모듈 — 등급이 오른다
                Run(w, SimTime.Hours(1) + World.SystemInterval);
                Check("첫날부터 모듈 전부 · 기술을 익히면 같은 모듈의 등급과 품질이 오른다 (잠금 해제가 아니다)",
                    a.Modules.Count == all && c.Grade(ComputerModule.Foresight) == 1 && c.Accuracy > acc0 && c.Horizon > hor0 && c.Concurrency > con0,
                    $"모듈 {a.Modules.Count}/{all} · 전조 분석 {c.Grade(ComputerModule.Foresight)}등급 · 정확도 {acc0 * 100:0}→{c.Accuracy * 100:0}% · 예측 {hor0:0}→{c.Horizon:0}분 · 동시 {con0}→{c.Concurrency} · {c.QualityWhy}");
            }

            // ② 정전 위기: 과부하 원인을 끊고 차단기를 올린다(같은 원인 재발 없음) · 필수 회로로 몰고 · 자기 연산을 줄이고 · 보조 발전기를 원격으로 켠다
            if (Sc("outage"))
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Automation;
                var p = w.Power;
                // 냉각 펌프가 모두 굳었다 → 원자로 긴급 정지 · 배터리 45%
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoolantPump).ToList()) w.Machines.Break(f.Machine!, FaultKind.PumpSeized);
                Run(w, SimTime.Minutes(3));
                p.BatteryCharge = p.BatteryCapacity * 0.45f;
                // 한 방 콘센트에 히터 셋 (주 컴퓨터는 아직 모른다)
                var room = w.Ship.LiveRooms.Where(r => r.Circuit == 3 && PortableSystem.OutletOk(r) && r.Type != RoomType.Corridor && r.Cells.Count(w.Ship.IsOpenFloor) >= 3).OrderBy(r => r.Id).FirstOrDefault()
                           ?? w.Ship.LiveRooms.Where(r => PortableSystem.OutletOk(r) && r.Type is RoomType.Quarters or RoomType.Lounge).OrderBy(r => r.Id).First();
                int circ = room.Circuit;
                var spots = room.Cells.Where(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null).Take(3).ToList();
                var heaters = w.Portable.Devices.Where(d => d.Kind == PortableKind.Heater).Take(3).ToList();
                while (heaters.Count < 3) heaters.Add(w.Portable.Add(PortableKind.Heater, heaters[0].Home));
                for (int i = 0; i < 3; i++) w.Portable.PlaceNow(heaters[i], spots[i % spots.Count], null, "hold", outlet: room);
                var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
                bool Tripped() => panel.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == circ);
                int trips0 = w.Portable.Stats.Trips;
                bool saved = false, parkedOk = false, essentialsOn = true, sawTrip = false;
                float battMin = 1f;
                for (int m = 0; m < 120; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    sawTrip |= Tripped();
                    saved |= a.Core.SelfSaving;
                    if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "20" && m % 5 == 0)
                        Console.WriteLine($"   {m}분 원자로 {p.ReactorOnline} 배터리 {p.BatteryPercent * 100:0}% 흐름 {p.BatteryFlow:0.0} 수요 {p.Demand:0.0} 보조 {p.AuxRunning} · 모드 {a.Triage.Mode} 끔 {a.Triage.Parked.Count} · 후보 {w.Ship.Machines.Count(x => x.Powered && !x.Stopped && x.Spec.PowerDraw > 0f && PowerTriage.Rank(w, x) <= 7)} · 차단 {Tripped()} · 사례 {string.Join(",", a.Triage.Cases.Select(c => $"{c.Circuit}:{c.State}:{c.Kind}"))} · 회로 {string.Join("", Enumerable.Range(0, 4).Select(i => p.ManualOff[i] ? "x" : p.CircuitFed[i] ? "o" : "-"))}");
                    battMin = MathF.Min(battMin, p.BatteryPercent);
                    if (a.Triage.Parked.Count > 0)
                    {
                        parkedOk = w.Ship.Machines.Where(x => a.Triage.Parked.Contains(x.Body.Id)).All(x => PowerTriage.Rank(w, x) <= 7);
                        essentialsOn &= w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Any(f => f.Machine!.Powered) && !w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Any(f => a.Triage.Parked.Contains(f.Id));
                    }
                }
                var tr = a.Triage;
                var bc = tr.Cases.FirstOrDefault(x => x.Circuit == circ && x.Kind == "portable");
                var fsP = a.Foresee.Timeline.FirstOrDefault(d => d.Kind == "정전");
                int sameAgain = bc == null ? -1 : tr.Cases.Count(x => x != bc && x.Sig == bc.Sig);
                Check("정전 위기 — 과부하 원인(히터)을 먼저 끊고 차단기를 원격으로 올린다 · 같은 원인으로 다시 안 떨어진다",
                    sawTrip && tr.CauseCuts >= 1 && bc != null && bc.State == "올림" && !Tripped() && sameAgain == 0 && w.Portable.ProjectedKw(circ) <= PortableSystem.OutletCapKw,
                    $"{PowerGrid.CircuitName(circ)} 회로({room.Name}) · 같은 원인 재발 {sameAgain} · 원격 올림 {tr.RemoteResets} · 끊은 것 {(bc != null ? string.Join("·", bc.Cut) : "-")} · 원인 \"{bc?.Cause}\" · 지금 {w.Portable.ProjectedKw(circ):0.0}kW");
                Check("정전 위기 — 사람이 배전반에 가기 전에 저출력 운영으로 돌리고 급하지 않은 것부터 내려 생명유지 쪽으로 몰아준다 (산소 발생기는 하나라도 돈다)",
                    tr.LowPower >= 1 && p.Brownout && parkedOk | tr.Parks == 0 && essentialsOn && w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Any(f => f.Machine!.Powered),
                    $"저출력 운영 {tr.LowPower}번 (지금 {p.Brownout}) · 내려 둔 설비 {p.ParkedCount} · 표로 끈 설비 {tr.Parks}대 · 생활 이하만 {parkedOk} · 산소 늘 {essentialsOn} · 산소 발생기 {w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Count(f => f.Machine!.Powered)}대 돎 · 일 \"{w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Brownout)?.Title ?? "사람 일 없음"}\"");
                Check("정전 위기 — 컴퓨터가 제 연산을 줄인다 (자기 절전)", saved && a.Core.SelfSaves >= 1, $"절전 {a.Core.SelfSaves}번 · 아낀 {a.Core.SavedKwh:0.00}kWh");
                Check("정전 위기 — 보조 발전기를 원격으로 켠다 (사람이 당기러 가기 전에 · 배터리 15% 위에서)",
                    tr.AuxStarts >= 1 && (p.AuxRunning || p.ReactorOnline) && fsP != null && fsP.Options.Count == 3 && fsP.Pick.Key == "aux",
                    $"원격 시동 {tr.AuxStarts}(실패 {tr.AuxFails}) · 돈다 {p.AuxRunning} · 배터리 최저 {battMin * 100:0}% · 견준 안: {(fsP == null ? "-" : string.Join(" / ", fsP.Options.Select(o => $"{o.Name}({o.Score:0.0})")))} → {fsP?.Pick.Name}");
                var log = w.Log.Entries.LastOrDefault(e => e.Text.Contains("끊은 뒤") && e.Text.Contains("차단기를 올렸습니다")).Text;
                Console.WriteLine($"    일지: {log}");
            }

            // ③ D 차단기 반복 버그: 사람이 끊긴 히터를 다시 꽂으면 같은 원인 — 이번엔 올리지 않고 사람에게 원인을 빼게 한다
            if (Sc("breaker"))
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Automation;
                var room = w.Ship.LiveRooms.Where(r => r.Circuit == 3 && PortableSystem.OutletOk(r) && r.Type != RoomType.Corridor && r.Cells.Count(w.Ship.IsOpenFloor) >= 3).OrderBy(r => r.Id).FirstOrDefault()
                           ?? w.Ship.LiveRooms.Where(r => PortableSystem.OutletOk(r) && r.Type is RoomType.Quarters or RoomType.Lounge).OrderBy(r => r.Id).First();
                int circ = room.Circuit;
                var spots = room.Cells.Where(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null).Take(3).ToList();
                var heaters = w.Portable.Devices.Where(d => d.Kind == PortableKind.Heater).Take(4).ToList();
                while (heaters.Count < 4) heaters.Add(w.Portable.Add(PortableKind.Heater, heaters[0].Home));
                void Plug() { for (int i = 0; i < 4; i++) w.Portable.PlaceNow(heaters[i], spots[i % spots.Count], null, "hold", outlet: room); } // 6kW — 2분이면 떨어진다 (사람이 뽑으러 오기 전에)
                var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
                bool Tripped() => panel.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == circ);
                var tr = a.Triage;
                Plug();
                bool first = false;
                for (int m = 0; m < 30 && !(first && !Tripped()); m++) { Run(w, SimTime.Minutes(1)); first |= Tripped(); }
                int resets0 = tr.RemoteResets;
                // 사람이 끊긴 히터를 "추워서" 다시 꽂는다 — 같은 셋
                Plug();
                bool again = false, held = false;
                for (int m = 0; m < 30 && !again; m++) { Run(w, SimTime.Minutes(1)); again = Tripped(); }
                for (int t = 0; t < SimTime.Minutes(3) && !held; t += World.SystemInterval)
                {
                    Run(w, World.SystemInterval);
                    held = Tripped() && tr.SameCauseHolds >= 1 && tr.RemoteResets == resets0 && !w.Board.Open.Any(o => o.Kind == WorkKind.ResetBreaker && o.Circuit == circ);
                }
                var hold = tr.Cases.LastOrDefault(x => x.Circuit == circ && x.State == "보류");
                string order = a.Command.Lines.LastOrDefault(l => l.Target == CmdTarget.Crew)?.What ?? "";
                // 사람이 원인을 빼면(지시를 듣고 · 뜨거운 콘센트를 보고) 그때 컴퓨터가 올린다
                var asked = w.Portable.AskedUnplug(circ);
                for (int m = 0; m < 60 && Tripped(); m++)
                {
                    Run(w, SimTime.Minutes(1)); // 배 끝에서 끝까지 걸어오는 시간
                    var ak = w.Portable.AskedUnplug(circ);
                    string aks = ak == null ? "-" : ak.Name + " " + ak.Job?.Label + "/" + ak.Room?.Name + "/" + ak.Pose;
                    if (Environment.GetEnvironmentVariable("SC_DBG") == "1")
                        Console.WriteLine($"      {m}분 떨어짐 {Tripped()} · 부탁 {aks} · 부하 {w.Portable.ProjectedKw(circ):0.0}kW · 원격 {tr.RemoteResets} · 사례 {string.Join(" / ", tr.Cases.Where(x => x.Circuit == circ).Select(x => $"{x.State}:{x.Sig}"))} · 히터 {string.Join(",", heaters.Select(h => $"{(h.Placed ? "놓임" : "-")}{(h.On ? "켬" : "끔")}{h.Plug}@{h.Outlet?.Name}"))}");
                }
                var hands = w.Log.Entries.Where(e => e.CrewId >= 0 && e.Tick >= (hold?.Since ?? 0) && (e.Text.Contains("뽑아 뒀다") || e.Text.Contains("옮겨 꽂았다"))).Select(e => e.CrewId).Distinct().ToList();
                bool byAsked = asked != null && hands.Contains(asked.Id);
                var closed = a.Command.Lines.LastOrDefault(l => l.Target == CmdTarget.Crew && l.TargetId == asked?.Id);
                Check("같은 원인으로 또 떨어지면 다시 올리지 않고 사람에게 정확히 말한다 → 원인을 빼면 그때 올린다 (D 차단기 반복 버그)",
                    first && again && held && !Tripped() && tr.RemoteResets >= resets0 + 1 && hold != null && !tr.Cases.Any(x => x.Circuit == circ && x.Since > hold.Since && x.Sig == hold.Sig) && w.Portable.ProjectedKw(circ) <= PortableSystem.OutletCapKw && order != "" && hands.Count > 0 && closed?.State == "끝",
                    $"처음 {first} · 다시 떨어짐 {again} · 붙잡음 {held} (같은 원인 {tr.SameCauseHolds}) · 지시 \"{order}\" → 사람 손으로 뺐다 {hands.Count}명 (지시받은 사람 {byAsked}) · 지시 {closed?.State} ({closed?.Result}) · 지금 {(Tripped() ? "떨어진 채" : "올라감")} · {hold?.State} · 원격 올림 {resets0}→{tr.RemoteResets} [{string.Join(" / ", tr.Cases.Where(x => x.Circuit == circ).Select(x => $"{x.State}:{x.Sig}:{x.Cause}"))}]");
            }

            // ④ 운석 파공: 지금 닫기 / 2분 기다렸다 닫기 / 사람 보내 막기를 견줘 고르고 타임라인에 남긴다 (방침 안에서)
            if (Sc("breach"))
            {
                ForeseeDecision? Scene(bool shipFirst, out World ww)
                {
                    var w = DayOne(seed, "Mirinae");
                    ww = w;
                    w.Automation.ShipFirst = shipFirst;
                    var room = w.Ship.RoomsOf(RoomType.Workshop).First();
                    var two = w.Crew.Where(c => c.CanAct).OrderBy(c => c.Id).Take(2).ToList();
                    foreach (var c in two) Put(w, c, room);
                    Put(w, two[1], room, last: true);
                    var wall = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).Select(kv => kv.Key).First();
                    Hull.Damage(w.Ship, wall, 1.2f);
                    for (int i = 0; i < 4; i++) Run(w, World.SystemInterval);
                    return w.Automation.Foresee.Timeline.LastOrDefault(d => d.Kind == "파공");
                }
                var dp = Scene(false, out var wp);
                var ds = Scene(true, out var wsf);
                Check("운석 파공 — 세 안을 미리 견줘 보고 고른 안 · 견준 안 · 이유를 남긴다 (사람 우선이면 기다렸다 닫기)",
                    dp != null && dp.Options.Count == 3 && dp.Options.Select(o => o.Name).SequenceEqual(new[] { "지금 닫기", "2분 기다렸다 닫기", "사람 보내 막기" }) && dp.Pick.Key == "wait" && dp.Reason != ""
                    && wp.Automation.Book.Acts.Any(x => x.Kind == ActKind.Forecast && x.Judge.Contains("견줘 봤다")),
                    dp == null ? "결정 없음" : $"{dp.Title}: " + string.Join(" / ", dp.Options.Select(o => $"{o.Name} 사람 {o.People:0.0} 배 {o.Ship:0.00}{(o.Allowed ? "" : $" ({o.Blocked})")} [{o.Note}]")) + $" → {dp.Pick.Name} — {dp.Reason}");
                Check("운석 파공 — 배 우선 방침이면 같은 장면에서 지금 닫는다 (기다림 · 열어 둠은 견주기만)",
                    ds != null && ds.Pick.Key == "seal" && !ds.Options[1].Allowed,
                    ds == null ? "결정 없음" : $"{ds.Pick.Name} — {ds.Reason}");
                Run(wp, SimTime.Minutes(12));
                var graded = wp.Automation.Foresee.Timeline.FirstOrDefault(d => d.Kind == "파공");
                Check("타임라인 — 몇 분 뒤 실제 결과로 채점한다 (쓰러진 사람 · 기압)", graded != null && graded.Score != 0 && graded.Result != "", $"{graded?.Pick.Name}: {graded?.Result}");
                var lines = wp.Automation.Command.Lines;
                Check("명령선 — 원격으로 한 일(격벽 · 문 · 방송)이 그 방까지 선으로 남는다 · 결정 번호로 타임라인과 이어진다",
                    lines.Any(l => l.Target == CmdTarget.Door && l.RoomId == graded?.RoomId) && lines.Any(l => l.Remote) && lines.All(l => l.What != ""),
                    string.Join(" / ", lines.TakeLast(5).Select(l => $"{l.Target} {l.What} [{l.State}]")));
            }

            // ② 데이터선 절반이 끊겨도 판단은 유지 (그 구역만 손으로) · 무선 예비로 읽기는 이어진다
            if (Sc("datalink"))
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Automation;
                var src = w.Ship.FurnitureOf(FurnitureType.MainComputer).First().Room;
                var links = w.Net.Links.Where(l => l.Kind == NetKind.Data && l.Door != null).OrderBy(l => l.Id).ToList();
                int live = w.Ship.LiveRooms.Count();
                foreach (var l in links)
                {
                    if (w.Ship.LiveRooms.Count(r => !r.DataLinked) >= live / 2) break;
                    w.Net.Hurt(l, 1f, "시험");
                    w.Net.Update(0f);
                }
                Run(w, SimTime.Minutes(3));
                var cut = w.Ship.LiveRooms.Where(r => !r.DataLinked && r.Type != RoomType.Corridor).OrderBy(r => r.Id).ToList();
                var cutRoom = cut.FirstOrDefault(r => r.Powered);
                bool hands = cutRoom != null && !a.AutoDoorsIn(cutRoom) && !a.DampersIn(cutRoom);
                bool reads = cutRoom != null && a.Belief.Reading(cutRoom) && a.Core.Reach(cutRoom) == 1;
                Check("데이터선이 절반 끊겨도 등급 V 그대로 — 끊긴 방만 손으로 (격벽 · 댐퍼) · 감지는 무선 예비로",
                    w.Ship.LiveRooms.Count(r => !r.DataLinked) >= live / 2 && a.Level == 5 && hands && reads,
                    $"끊긴 방 {w.Ship.LiveRooms.Count(r => !r.DataLinked)}/{live} · 등급 {AutomationSystem.LevelName(a.Level)} ({a.LevelWhy}) · {cutRoom?.Name}: 손으로 {hands} · 무선 읽기 {reads}");
            }

            // ② 과열 → 정지 대신 안전 모드 (환기가 돌면 풀린다) · 극한이면 본체만 멎고 예비 연산기가 붙잡는다
            if (Sc("overheat"))
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                var cr = a.Computer!.Body.Room;
                float cap0 = a.Capacity;
                cr.VentOpen = false; cr.DamperStuck = true;
                cr.Air.Temperature = 41f;
                bool safe = false, online = true; int lvl = 5; float capSafe = 0f; bool media = false;
                for (int m = 0; m < 6; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (cr.Air.Temperature < 39f) cr.Air.Temperature = 41f;
                    safe |= a.Core.SafeMode;
                    online &= a.MainOnline;
                    if (a.Core.SafeMode) { lvl = a.Level; capSafe = a.Capacity; media |= a.Suspended.Contains(ComputerModule.MediaVault); }
                }
                cr.DamperStuck = false; cr.VentOpen = true; cr.Air.Temperature = 24f;
                for (int m = 0; m < 12 && a.Core.SafeMode; m++) { Run(w, SimTime.Minutes(1)); cr.Air.Temperature = MathF.Min(cr.Air.Temperature, 26f); }
                Check("과열 — 멈추지 않고 안전 모드(연산 반 · 급하지 않은 일 쉼 · 등급 IV) · 환기가 돌아오면 푼다",
                    safe && online && lvl == 4 && capSafe < cap0 * 0.6f && media && !a.Core.SafeMode && a.Core.SafeModes == 1,
                    $"안전 모드 {safe} · 내내 켜짐 {online} · 등급 {lvl} · 용량 {cap0:0}→{capSafe:0} · 오락 쉼 {media} · 지금 {(a.Core.SafeMode ? "아직" : "풀림")}");
                // 극한: 50℃ — 본체만 과열 정지, 예비 연산기가 격벽 · 댐퍼 · 경보를 붙잡는다
                cr.VentOpen = false; cr.DamperStuck = true;
                bool held = false; int lv = 0;
                for (int m = 0; m < 30 && !held; m++)
                {
                    cr.Air.Temperature = 50f;
                    Run(w, SimTime.Minutes(1));
                    if (!a.MainOnline && a.Core.BackupCore) { held = true; lv = a.Level; }
                }
                Check("과열 극한 — 본체만 멎고 예비 연산기가 붙잡는다 (등급 III · 격벽 · 댐퍼 · 경보는 그대로)",
                    held && lv == 3 && a.Dampers && a.Doors && a.Alarms && a.Priority,
                    $"붙잡음 {held} · 등급 {lv} · 넘겨받기 {a.Core.Takeovers} · {a.NowLine}");
            }

            // ② 재부팅은 차례로 — 그동안 예비 연산기가 댐퍼 · 경보를 맡고, 다시 켜지면 하던 일을 하나씩 되찾는다
            if (Sc("reboot"))
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                var room = StoreRoom(w);
                ClearRoom(w, room);
                a.Reboot("시험 재부팅", 4f);
                BigFire(w, room, 4);
                bool damper = false; string card = ""; int stages = 0; var seen = new HashSet<int>();
                for (int i = 0; i < 12 && a.Rebooting; i++)
                {
                    Run(w, World.SystemInterval);
                    damper |= !room.VentOpen;
                    if (a.Core.RebootStage > 0) seen.Add(a.Core.RebootStage);
                    card = a.NowLine;
                }
                for (int i = 0; i < 30 && a.Rebooting; i++) { Run(w, World.SystemInterval); if (a.Core.RebootStage > 0) seen.Add(a.Core.RebootStage); }
                stages = seen.Count;
                int susp = a.Suspended.Count;
                Run(w, SimTime.Minutes(10));
                Check("재부팅 — 예비 연산기가 댐퍼를 닫고(기록은 예비 연산기) · 단계 셋 · 다시 켜지면 쉬던 일을 하나씩",
                    damper && stages == 3 && a.MainOnline && a.Book.Acts.Any(x => x.By == "예비 연산기") && susp > 0 && a.Suspended.Count < susp,
                    $"댐퍼 {damper} · 단계 {stages} · 카드 \"{card}\" · 쉬던 일 {susp} → {a.Suspended.Count}");
            }

            // ④ 겹친 사고: 위험한 사람 수 × 위험까지 남은 시간으로 순서 · 동시 처리 수를 넘는 사고는 기다린다
            if (Sc("overlap"))
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Automation;
                var store = StoreRoom(w);
                ClearRoom(w, store);
                BigFire(w, store, 3);
                var work = w.Ship.RoomsOf(RoomType.Workshop).First();
                foreach (var c in w.Crew.Where(c => c.CanAct).OrderBy(c => c.Id).Take(3)) Put(w, c, work, last: true);
                var wall = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == work).Select(kv => kv.Key).First();
                Hull.Damage(w.Ship, wall, 1.2f);
                var wet = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Galley or RoomType.Hydroponics && r != work).OrderBy(r => r.Id).First();
                w.Moisture.AddWater(wet, wet.Cells.Count * 20f * 0.2f);
                string line = ""; int n = 0; string first = "";
                for (int i = 0; i < 8 && line == ""; i++) { Run(w, World.SystemInterval); if (a.Foresee.Queue.Count >= 3) { line = a.Foresee.QueueLine; n = a.Foresee.Queue.Count; first = a.Foresee.Queue[0].Kind; } }
                Check("겹친 사고 — 사람이 많고 급한 파공부터 · 한 번에 다루는 수를 넘으면 기다린다",
                    n >= 3 && first == "파공" && a.Foresee.Overlaps >= 1 && a.Foresee.Queue.Count(q => !q.Attended) >= n - a.Core.Concurrency,
                    $"{n}건 · 순서 {line} · 동시 {a.Core.Concurrency}");
            }

            // ⑤ 두 배가 다른 일을 겪으면 성격이 다르게 자란다 → 같은 장면에서 무게가 다르다
            if (Sc("character"))
            {
                var wa = DayOne(seed, "Mirinae");
                var wb = DayOne(seed, "Mirinae");
                // 가: 닫은 격벽 안에서 사람이 쓰러졌다 (두 번) · 나: 기다리다 옆방 공기까지 잃었다 (세 번)
                wa.Automation.TrappedCasualties += 2;
                wb.Automation.LateSeals += 3;
                Run(wa, SimTime.Minutes(2)); Run(wb, SimTime.Minutes(2));
                var ca = wa.Automation.Character; var cb = wb.Automation.Character;
                var (pa, sa) = wa.Automation.Foresee.Weights(); var (pb, sb) = wb.Automation.Foresee.Weights();
                string sayA = ca.Flavor("문을 닫습니다"), sayB = cb.Flavor("문을 닫습니다");
                Check("성격 — 같은 배라도 겪은 일에 따라 다르게 자란다 (신중 · 사람 우선 ↔ 과감 · 배 우선) · 견줄 때 무게 · 말투도",
                    ca.Caution > 0.15f && ca.PeopleTilt > 0.25f && cb.Caution < 0f && cb.PeopleTilt < -0.25f && pa > pb && sa < sb && sayA != sayB,
                    $"가 {ca.Line} (사람 {pa:0.0} · 배 {sa:0.0}) \"{sayA}\" · 나 {cb.Line} (사람 {pb:0.0} · 배 {sb:0.0}) \"{sayB}\" [{string.Join(" / ", cb.Shifts.TakeLast(3).Select(x => x.text))}] · 가 기록: {wa.History.Events.LastOrDefault(e => e.Text.Contains("달라졌다"))?.Text}");
            }

            // ③ 명령선: 로봇 명령 API (다음 단계가 쓴다) · 위험한 방에서 자는 사람은 단말로 깨운다
            if (Sc("cmdapi"))
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Automation;
                if (!w.Robots.Robots.Any(r => r.Kind == RobotKind.Maintainer && r.Fault == null)) RobotsV15.Install(w, RobotsV15.Code(RobotKind.Maintainer), null, out _);
                for (int i = 0; i < 12 && !w.Robots.Robots.Any(r => r.Kind == RobotKind.Maintainer && r.Fault == null && !r.Disabled && r.Order == null && r.Battery > 0.6f); i++) Run(w, SimTime.Minutes(5));
                var robot = w.Robots.Robots.Where(r => r.Kind == RobotKind.Maintainer && r.Fault == null && !r.Disabled).OrderBy(r => r.Order == null ? 0 : 1).ThenByDescending(r => r.Battery).First();
                foreach (var m in w.Ship.Machines.Where(m => m.Body.Type is FurnitureType.Fridge or FurnitureType.Stove or FurnitureType.WaterRecycler).Take(3)) m.Wear = 0.85f;
                w.Board.RequestScan();
                Run(w, World.SystemInterval * 2);
                robot.Battery = 1f; // 시험: 막 충전을 마쳤다
                var job = w.Board.OpenForRobot().Where(o => o.Kind == WorkKind.Maintain).OrderBy(o => o.Urgency).ThenBy(o => o.Id).FirstOrDefault();
                var ord = job != null ? a.Command.Order(robot, job, 0.9f, "시험 — 이 일을 먼저") : null;
                bool took = false;
                for (int i = 0; i < 80 && !took && job != null; i++) { Run(w, World.SystemInterval); took = robot.Order == job || job.Robot == robot; }
                Check("명령 API — Order(로봇, 일, 우선, 이유)를 받은 로봇이 그 일을 먼저 잡는다 · 명령선에 남는다",
                    took && ord != null && a.Command.Lines.Any(l => l.Target == CmdTarget.Robot && l.TargetId == robot.Id),
                    $"{robot.Name}(배터리 {robot.Battery * 100:0}% · {robot.Doing}) → {job?.Title ?? "일 없음"} · 잡음 {took} · 명령 {ord?.State}");
                var sleeper = w.Crew.FirstOrDefault(c => c.Pose == Pose.Sleeping && c.Room is { Type: RoomType.Quarters });
                if (sleeper == null)
                {
                    sleeper = w.Crew.First(c => c.CanAct);
                    sleeper.EndJob(w, ToilStatus.Interrupted);
                    sleeper.Pose = Pose.Sleeping;
                }
                var qr = sleeper.Room!;
                int woken0 = a.Command.Woken;
                bool left = false;
                for (int i = 0; i < 8 && !left; i++) { qr.Air.CO = MathF.Max(qr.Air.CO, 0.3f); Run(w, SimTime.Minutes(1)); left = a.Command.Woken > woken0 && sleeper.Room != qr; }
                Check("깨우기 — 일산화탄소가 찬 방에서 자는 사람을 단말로 깨우고, 깬 사람은 그 방을 나간다",
                    a.Command.Woken > woken0 && left, $"{sleeper.Name} ({qr.Name}) · 깨움 {a.Command.Woken - woken0} · 지금 {sleeper.Room?.Name} {sleeper.Pose} · {sleeper.Job?.Label}");
            }

            // 결정론
            if (Sc("hash"))
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint h1 = H(), h2 = H();
                Check("결정론 — 같은 시드 두 번 같은 지문", h1 == h2, $"{h1:x8} / {h2:x8}");
            }

            // 성능: 30인 배 하루 (v16.20 켜고 · 끄고)
            if (Sc("perf"))
            {
                double Day(bool off)
                {
                    AutomationSystem.Ship20Off = off;
                    try
                    {
                        var w = World.CreateDefault(seed, 30, "Hanbit");
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Run(w, SimTime.TicksPerDay);
                        return sw.Elapsed.TotalSeconds;
                    }
                    finally { AutomationSystem.Ship20Off = false; }
                }
                double off = Day(true), on = Day(false);
                Check("성능 — 30인 배 하루가 크게 늘지 않는다 (+25% 안)", on <= off * 1.25 + 0.5, $"끔 {off:0.00}초 · 켬 {on:0.00}초 ({(on / off - 1) * 100:+0;-0}%)");
            }
        }
        catch (Exception e) { Check("예외 없이", false, e.ToString()); }
        Console.WriteLine(_fails == 0 ? "\n✔ 우주선급 주컴퓨터 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
