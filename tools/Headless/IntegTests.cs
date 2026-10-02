using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.26 토대 묶음 통합 시험 (--integtest): 출입 통제 문을 비켜 가는 길 · 위험이 사람에게 닿는 길 (열사병 · 큰 피폭 · 센 폭풍 · 창가 · 운석우 · 늦게 깨는 잠) · 우주급 표본.
public static partial class Program
{
    private static int RunIntegTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"통합 시험 (v16.26) · 시드 {seed}\n");
        string only = Environment.GetEnvironmentVariable("INTEG_ONLY") ?? "path,heat,rad,storm,shower,sleep,cosmic,danger,hash";
        if (only.Contains("probe")) { IgProbe(seed, Environment.GetEnvironmentVariable("PROBE") ?? ""); return 0; }
        if (only.Contains("path")) IgBarredPath(seed);
        if (only.Contains("heat")) IgHeat(seed);
        if (only.Contains("rad")) IgRadiation(seed);
        if (only.Contains("storm")) IgStorm(seed);
        if (only.Contains("shower")) IgShower(seed);
        if (only.Contains("sleep")) IgSleep(seed);
        if (only.Contains("cosmic")) IgCosmic(seed);
        if (only.Contains("danger")) IgDanger(seed); // 통합 4차: 위험이 사람에게 닿는 길
        if (!only.Contains("hash")) { Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n✘ {_fails}개 실패"); return _fails == 0 ? 0 : 1; }
        uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
        uint a = H(), b = H();
        Check("결정론 (같은 시드 두 번 같은 지문)", a == b, $"{a:x8} / {b:x8}");
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    private static void IgPut(CrewMember c, Cell at) { c.Position = at.Center; c.PreviousPosition = c.Position; c.Path = null; }

    /// <summary>출입 통제 문: 권한 없는 사람은 그 방을 가로지르는 길을 고르지 않는다 (다른 길이 없을 때만 문 앞에서 부른다) · 급한 일은 비상 해제.</summary>
    private static void IgBarredPath(int seed)
    {
        foreach (var key in new[] { "Hanbit", "Eunha", "Cheonma" })
        {
            var w = DayOne(seed, key);
            var ship = w.Ship;
            bool Inside(List<Cell> p, Room r) => p.Any(x => ship.RoomAt(x) == r);
            // 문이 둘 이상인 방: 한쪽 이웃에서 다른 쪽 이웃까지 가장 짧은 길이 그 방을 지나고, 돌아가는 길도 있는 곳
            foreach (var r in ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.Doors.Count(d => !d.IsExternal) >= 2).OrderBy(r => r.Id))
            {
                var outs = r.Doors.Where(d => !d.IsExternal).Select(d => d.RoomA == r ? d.RoomB : d.RoomA).OfType<Room>().Distinct().ToList();
                for (int i = 0; i < outs.Count; i++)
                for (int j = i + 1; j < outs.Count; j++)
                {
                    var a = outs[i].Cells.Where(ship.IsOpenFloor).OrderBy(c => (c.Center - r.Center).LengthSquared()).FirstOrDefault();
                    var b = outs[j].Cells.Where(ship.IsOpenFloor).OrderBy(c => (c.Center - r.Center).LengthSquared()).FirstOrDefault();
                    if (a == default || b == default) continue;
                    var open = w.Paths.Find(a, b, PathProfile.Default);
                    if (open == null || !Inside(open, r)) continue;
                    w.Body.SetZone(r, AccessZone.Reactor, LockKind.Card);
                    var stranger = w.Crew.FirstOrDefault(c => c.CanAct && c.Role is not (CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician) && c.Id != w.Command.CaptainId);
                    var keyholder = w.Crew.FirstOrDefault(c => c.CanAct && c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician);
                    if (stranger == null || keyholder == null) { w.Body.SetZone(r, AccessZone.Open, LockKind.None); continue; }
                    var barred = w.Paths.Find(a, b, PathProfile.Default with { Who = stranger.Id });
                    if (barred == null || Inside(barred, r)) { w.Body.SetZone(r, AccessZone.Open, LockKind.None); continue; }
                    var allowed = w.Paths.Find(a, b, PathProfile.Default with { Who = keyholder.Id });
                    Check($"출입 통제 문 — 권한 없는 사람은 {r.Name}을 가로지르지 않고 돌아간다 ({key})", allowed != null && Inside(allowed, r),
                        $"열린 길 {open.Count}칸(지남) · 권한 없는 사람 {barred.Count}칸(돌아감) · 카드 있는 사람 {allowed?.Count}칸");
                    // 실제로 걸어 본다: 권한 없는 사람이 갇히지 않고 닿는다
                    stranger.EndJob(w, ToilStatus.Interrupted);
                    IgPut(stranger, a);
                    Run(w, 2);
                    bool set = Locomotion.SetDestination(stranger, w, b);
                    stranger.NextThinkTick = w.Tick + SimTime.Hours(2);
                    bool entered = false;
                    for (int t = 0; t < SimTime.Minutes(30) && stranger.Cell != b; t++)
                    {
                        if (stranger.Path == null && stranger.Cell != b) { Locomotion.SetDestination(stranger, w, b); stranger.NextThinkTick = w.Tick + SimTime.Hours(2); }
                        w.Step();
                        if (stranger.Room == r) entered = true;
                    }
                    Check("출입 통제 문 — 권한 없는 사람이 실제로 돌아서 닿는다 (그 방에 들어가지도 · 문 앞에서 막히지도 않는다)", set && !entered && (stranger.Cell.Center - b.Center).Length() <= 1.5f,
                        $"{stranger.Name} 도착 {(stranger.Cell == b)} · 들어감 {entered} · 지금 {stranger.Room?.Name}");
                    // 다른 길이 없으면: 그래도 길은 있다 (문 앞에 가서 권한자를 부른다) — 문이 하나인 통제 방으로
                    var cabin = ship.LiveRooms.FirstOrDefault(x => x.Type != RoomType.Corridor && x.Doors.Count(d => !d.IsExternal) == 1 && x.Cells.Any(ship.IsOpenFloor) && x != r);
                    if (cabin != null)
                    {
                        w.Body.SetZone(cabin, AccessZone.Reactor, LockKind.Card);
                        var inside = cabin.Cells.First(ship.IsOpenFloor);
                        var p2 = w.Paths.Find(a, inside, PathProfile.Default with { Who = stranger.Id });
                        Check("출입 통제 문 — 그 안이 목적지면 길은 있다 (문 앞에서 권한자를 부른다)", p2 != null, $"{cabin.Name} {p2?.Count}칸");
                        w.Body.SetZone(cabin, AccessZone.Open, LockKind.None);
                    }
                    // 급한 일: 비상 해제 손잡이 (문 앞 규칙과 같다) — Who 없음
                    var urgent = w.Paths.Find(a, b, PathProfile.Default with { Who = -1, Responder = true });
                    Check("출입 통제 문 — 급한 일로 달려가는 사람은 비상 해제하고 가로지른다", urgent != null && Inside(urgent, r), $"{urgent?.Count}칸");
                    return;
                }
            }
        }
        Check("출입 통제 문 — 시험할 방을 찾았다", false, "가로지르는 지름길이 있는 방이 없다");
    }

    private static Room IgQuiet(World w) =>
        w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Workshop && r.Cells.Count(w.Ship.IsOpenFloor) >= 4).OrderBy(r => r.Id).FirstOrDefault()
        ?? w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).OrderByDescending(r => r.Cells.Count).First();

    /// <summary>열사병: 달아오른 방 · 혼자 쓰러지면 숨진다 · 끌어내 식히면 산다 · 깨어 있으면 어지러워지기 전에 나간다.</summary>
    private static void IgHeat(int seed)
    {
        // 1) 혼자 · 쓰러진 채 뜨거운 방에 남는다
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = IgQuiet(w);
            var v = w.Crew.First(c => c.CanAct);
            foreach (var o in w.Crew.Where(o => o != v)) { o.EndJob(w, ToilStatus.Interrupted); IgPut(o, w.Ship.LiveRooms.First(x => x != room && x.Type == RoomType.Corridor).Cells.First(w.Ship.IsOpenFloor)); }
            var spot = room.Cells.First(w.Ship.IsOpenFloor);
            int collapseMin = -1, deathMin = -1;
            for (int t = 0; t < SimTime.Hours(2) && !v.Dead; t++)
            {
                room.Air.Temperature = 66f; room.Humidity = 0.85f;
                if (!v.Down) { v.EndJob(w, ToilStatus.Interrupted); v.Pose = Pose.Working; IgPut(v, spot); v.NextThinkTick = w.Tick + 50; }
                w.Step();
                if (v.Down && collapseMin < 0) collapseMin = t / SimTime.Minutes(1);
                if (v.Dead) deathMin = t / SimTime.Minutes(1);
            }
            var hist = w.History.Events.LastOrDefault(e => e.Kind == HistoryKind.Death);
            Check("열사병 — 김이 찬 66℃ 방에서 힘쓰면 한 시간 안에 쓰러진다", collapseMin is > 0 and < 60, $"쓰러짐 {collapseMin}분 · 열 {w.Perils.HeatOf(v):0.00}");
            Check("열사병 — 쓰러진 채 뜨거운 방에 혼자 남으면 숨진다 (사인 · 왜 늦었는지가 연대기에)", v.Dead && v.Vitals.InjuryCause == "열사병" && hist != null && hist.Text.Contains("열사병"),
                $"죽음 {deathMin}분 · 사인 {v.Vitals.InjuryCause} · {hist?.Text}");
            Check("열사병 — 주 컴퓨터가 생체 신호로 알아채 부른다", w.Perils.Paged > 0 && w.Automation.Book.Acts.Any(a => a.Key == "peril"), $"부름 {w.Perils.Paged}");
        }
        // 2) 쓰러진 뒤 서늘한 곳으로 옮기면 산다
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = IgQuiet(w);
            var v = w.Crew.First(c => c.CanAct);
            var spot = room.Cells.First(w.Ship.IsOpenFloor);
            for (int t = 0; t < SimTime.Hours(1) && !v.Down; t++)
            {
                room.Air.Temperature = 66f; room.Humidity = 0.85f;
                v.EndJob(w, ToilStatus.Interrupted); v.Pose = Pose.Working; IgPut(v, spot); v.NextThinkTick = w.Tick + 50;
                w.Step();
            }
            var cool = w.Ship.LiveRooms.First(x => x != room && x.Type == RoomType.Corridor).Cells.First(w.Ship.IsOpenFloor);
            Run(w, SimTime.Minutes(4));
            if (!v.Dead) IgPut(v, cool);
            Run(w, SimTime.Hours(1));
            Check("열사병 — 쓰러진 사람을 서늘한 곳으로 끌어내면 열이 내려 산다", v.Down && !v.Dead && w.Perils.HeatRescued > 0 || !v.Dead && w.Perils.HeatRescued > 0, $"쓰러짐 {v.Down} · 죽음 {v.Dead} · 열 {w.Perils.HeatOf(v):0.00} · 체력 {v.Vitals.Health:0.00}");
        }
        // 3) 깨어 있는 사람은 열기가 차오르면 그 방을 나간다
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = IgQuiet(w);
            var v = w.Crew.First(c => c.CanAct);
            v.EndJob(w, ToilStatus.Interrupted); IgPut(v, room.Cells.First(w.Ship.IsOpenFloor)); v.NextThinkTick = w.Tick + 1;
            bool left = false;
            for (int t = 0; t < SimTime.Minutes(50); t++)
            {
                room.Air.Temperature = 47f; room.Humidity = 0.7f;
                w.Step();
                if (v.Room != room) { left = true; break; }
            }
            Check("열사병 — 깨어 있으면 어지러워지기 전에 뜨거운 방을 나간다", left && !v.Down, $"나감 {left} · 열 {w.Perils.HeatOf(v):0.00} · 위험 {w.Perils.HeatDanger(v):0.00}");
        }
    }

    /// <summary>큰 피폭: 7Sv를 넘으면 몇 시간 안에 쓰러지고 · 돌보면 느리게 · 컴퓨터가 의무실로 부른다 · 4Sv는 기운이 빠진다.</summary>
    private static void IgRadiation(int seed)
    {
        var w = DayOne(seed, "Hanbit"); w.CrewCanDie = true;
        var v = w.Crew.First(c => c.CanAct);
        var m = w.Crew.Where(c => c.CanAct && c != v).Skip(1).First();
        v.Dose = 8f; m.Dose = 4.5f;
        float h0 = m.Vitals.Health;
        int downMin = -1;
        for (int t = 0; t < SimTime.Hours(5); t++) { w.Step(); if (v.Down && downMin < 0) downMin = t / SimTime.Minutes(1); }
        Check("큰 피폭 — 7Sv를 넘으면 몇 시간 안에 쓰러진다 (방사선 병)", downMin is > 60 and < 300 && v.Vitals.InjuryCause == "방사선 병", $"쓰러짐 {downMin}분 · {v.Vitals.InjuryCause} · 단계 {w.Perils.RadStage(v)}");
        Check("큰 피폭 — 4Sv 넘으면 방사선 병을 앓는다 (기록 · 기억)", w.Ailments.Has(m, "radiation") && m.Memory.Marks.Any(x => x.Text.Contains("방사선")), $"앓음 {w.Ailments.Has(m, "radiation")} · 체력 {h0:0.00}→{m.Vitals.Health:0.00}");
        Check("큰 피폭 — 주 컴퓨터가 알아채 가까운 사람을 부른다", w.Perils.Paged >= 1, $"부름 {w.Perils.Paged}");
    }

    /// <summary>센 태양 폭풍 · 창가: 폭풍 세기가 그때그때 다르고, 덮개 구동기에 전기가 없으면 창가가 더 쬔다.</summary>
    private static void IgStorm(int seed)
    {
        var peaks = new List<float>();
        for (int k = 0; k < 8; k++)
        {
            var w = World.CreateDefault(seed + k, 0, "Mirinae");
            Run(w, 10);
            Hazards.Apply(w, HazardKind.SolarStorm, default, -1);
            peaks.Add(w.Hazards.StormPeak);
            if (k == 0) Check("태양 폭풍 — 처음 세 시간 세기가 이번 폭풍 세기다", MathF.Abs(w.Ambience.StormPower - w.Hazards.StormPeak) < 0.001f, $"{w.Ambience.StormPower:0.00} / {w.Hazards.StormPeak:0.00}");
        }
        Check("태양 폭풍 — 세기가 폭풍마다 다르다 (가끔 센 것)", peaks.Max() - peaks.Min() > 0.2f && peaks.All(p => p is >= 0.45f and <= 5f), string.Join(" · ", peaks.Select(p => $"{p:0.00}")));
        // 창가: 같은 방, 전기가 있으면 덮개가 내려가고 없으면 그대로
        float Rad(bool power)
        {
            var w = World.CreateDefault(seed, 0, "Eunha");
            Run(w, 10);
            var room = w.Ship.LiveRooms.Where(r => w.Body.WindowsOf(r) > 0).OrderByDescending(r => w.Body.WindowsOf(r)).ThenBy(r => r.Id).FirstOrDefault();
            if (room == null) return -1f;
            Hazards.Apply(w, HazardKind.SolarStorm, default, -1);
            room.BreakerOff = !power; // 방 분전함을 내렸다 — 덮개 구동기도 선다
            Run(w, SimTime.Minutes(30));
            return room.Radiation;
        }
        float lit = Rad(true), dark = Rad(false);
        Check("창가 — 덮개 구동기에 전기가 없으면 창이 열린 채 더 쬔다", lit >= 0f && dark > lit + 0.03f, $"전기 있음 {lit:0.00} · 없음 {dark:0.00}");
    }

    /// <summary>운석우: 사람이 있는 곳(복도 · 침실 · 식당)에도 떨어진다 · 한쪽 외벽에 몰린다.</summary>
    private static void IgShower(int seed)
    {
        int corridor = 0, living = 0, total = 0, skewed = 0, occupied = 0;
        var kinds = new Dictionary<RoomType, int>();
        for (int k = 0; k < 6; k++)
        {
            var w = World.CreateDefault(seed + k, 0, "Eunha");
            Run(w, SimTime.Hours(3)); // 일하는 시간 (사람들이 일터에 있다)
            Hazards.Apply(w, HazardKind.MeteorShower, default, -1);
            var rooms = w.Hazards.Shower.Select(s => w.Ship.RoomAt(s.target) ?? w.Ship.LiveRooms.OrderBy(r => (r.Center - s.target.Center).LengthSquared()).First()).ToList();
            total += rooms.Count;
            foreach (var r in rooms) kinds[r.Kind] = kinds.GetValueOrDefault(r.Kind) + 1;
            corridor += rooms.Count(r => r.Type == RoomType.Corridor);
            living += rooms.Count(r => r.Type is RoomType.Quarters or RoomType.Mess or RoomType.Galley || RoomCatalog.Of(r.Kind)?.Base is RoomType.Quarters or RoomType.Mess);
            var cx = w.Ship.LiveRooms.Average(r => r.Center.X); var cy = w.Ship.LiveRooms.Average(r => r.Center.Y);
            occupied += w.Hazards.Shower.Count(s => w.Crew.Any(c => !c.Dead && c.Room != null && c.Room == w.Ship.RoomAt(s.target)));
            int l = w.Hazards.Shower.Count(s => s.target.X < cx), u = w.Hazards.Shower.Count(s => s.target.Y < cy);
            int n = w.Hazards.Shower.Count;
            if (Math.Max(l, n - l) >= n * 0.7f || Math.Max(u, n - u) >= n * 0.7f) skewed++;
        }
        Check("운석우 — 지금 사람이 있는 방에도 떨어진다 (외벽에 닿은 방은 복도 · 생활 공간도 고른다)", occupied > 0, $"{total}개 중 사람 있는 방 {occupied} · 복도 {corridor} · 생활 공간 {living} · " + string.Join(",", kinds.OrderByDescending(x => x.Value).Select(x => $"{x.Key}{x.Value}")));
        Check("운석우 — 잔해 무리가 오는 쪽 외벽에 몰린다", skewed >= 3, $"한쪽에 70% 넘게 몰린 운석우 {skewed}/6");
    }

    /// <summary>늦게 깨는 잠: 옅은 연기 냄새로는 바로 안 깬다 · 숨 막힘 · 열기는 바로 깬다.</summary>
    private static void IgSleep(int seed)
    {
        int Wake(Action<Room> air)
        {
            var w = DayOne(seed, "Mirinae");
            CrewMember? v = null;
            for (int t = 0; t < SimTime.TicksPerDay && v == null; t += 25) { Run(w, 25); v = w.Crew.FirstOrDefault(c => c.Pose == Pose.Sleeping && c.Room != null && c.Room.Type != RoomType.Corridor && c.Job != null); }
            if (v == null) return -1;
            var room = v.Room!;
            air(room);
            for (int t = 0; t < SimTime.Minutes(30); t++)
                if (w.Perils.WakesFromSleep(v)) return t; // 이 틱에 몸이 깨나 (SenseHazard가 매 틱 묻는 것)
            return 9999;
        }
        int smoke = Wake(r => { r.Air.Smoke = 0.25f; });
        int hot = Wake(r => { r.Air.Temperature = 44f; });
        Check("늦게 깨는 잠 — 옅은 연기 냄새로는 바로 깨지 않는다 (몸이 느끼는 열기는 바로 깬다)", hot >= 0 && smoke > hot + 20 && hot < 30, $"연기 {smoke}틱 · 열기 {hot}틱");
    }

    /// <summary>우주급 표본: 같은 시드의 배들이 같은 날 같은 것만 맞지 않는다 · 사람에게 닿는 것을 더 고른다.</summary>
    private static void IgCosmic(int seed)
    {
        var kinds = new List<CosmicKind>();
        var pick = typeof(CosmicSystem).GetMethod("PickKind", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var key in new[] { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Cheonma" })
        {
            var w = World.CreateDefault(seed, 0, key);
            for (int i = 0; i < 4; i++) kinds.Add((CosmicKind)pick.Invoke(w.Cosmic, null)!);
        }
        int harsh = kinds.Count(k => { var s = CosmicCatalog.Spec(k); return s.Has(CosmicFx.Radiation) || s.Has(CosmicFx.Debris) || s.Has(CosmicFx.Shock) || s.Has(CosmicFx.Heat) || s.Has(CosmicFx.Strike) || s.Has(CosmicFx.Hostile); });
        Check("우주급 — 같은 시드의 다섯 배가 서로 다른 것을 맞는다 (표본이 다양하다)", kinds.Distinct().Count() >= 8, $"{kinds.Distinct().Count()}종 / {kinds.Count} · " + string.Join(",", kinds.Take(10)));
        Check("우주급 — 사람에게 닿는 것(방사선 · 잔해 · 충격 · 열 · 직격)이 대부분", harsh >= kinds.Count * 0.6f, $"{harsh}/{kinds.Count}");
    }

    /// <summary>진단만 (INTEG_ONLY=probe PROBE=…): 회귀 실패 원인을 본다.</summary>
    private static void IgProbe(int seed, string what)
    {
        IgProbe2(seed, what); // 통합 4차 진단
        if (what.Contains("comms"))
        {
            var w = DayOne(seed, "Hanbit");
            Console.WriteLine("통신실: " + string.Join(", ", w.Ship.Rooms.Where(r => r.Type == RoomType.Comms).Select(r => $"{r.Name}#{r.Id} det={r.Detached} con={r.Furniture.Count(f => f.Type == FurnitureType.Console)}")));
            var comms = w.Sensors.CommsRoom!;
            w.Structure.Detach(comms, "교신 시험", controlled: true);
            comms.Wreck = true;
            Console.WriteLine($"뗀 뒤 CommsRoom={w.Sensors.CommsRoom?.Name}#{w.Sensors.CommsRoom?.Id} Console={w.Comms.Console?.Label} room={w.Comms.Console?.Room.Name}");
        }
        if (what.Contains("aux"))
        {
            var w = DayOne(seed, "Mirinae");
            Console.WriteLine("보조 발전기: " + string.Join(", ", w.Ship.FurnitureOf(FurnitureType.AuxGenerator).Select(f => $"{f.Label}@{f.Room.Name} eff={f.Machine?.Efficiency:0.00} vol={f.Room.Volume:0}")));
            var aux = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).First();
            foreach (var f in w.Exterior.All) w.Exterior.Damage(f, 1f, "시험");
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
            for (int h = 0; h < 8 * 4; h++)
            {
                aux.Room.VentOpen = false;
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) if (p.Machine!.Faults.Count == 0) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                Run(w, SimTime.Minutes(15));
                if (h == 4) { float c0 = aux.Room.Air.CO; w.Volatile.Update(0.01f); Console.WriteLine($"   direct {c0:0.0000} -> {aux.Room.Air.CO:0.0000} machines has aux {w.Ship.Machines.Contains(aux.Machine!)} vol={aux.Room.Volume}"); }
                if (h == 4) { for (int k = 0; k < 6; k++) { for (int q = 0; q < World.SystemInterval; q++) w.Step(); Console.WriteLine($"   tick CO={aux.Room.Air.CO:0.0000} merged={aux.Room.Merged} doors={string.Join("/", aux.Room.Doors.Select(d => $"{d.Openness:0.0}"))} type={aux.Room.Type} inert={w.Volatile.Inert} active={aux.Machine!.Active} stopped={aux.Machine.Stopped} parked={aux.Machine.Parked}"); } }
                if (h % 4 == 3) Console.WriteLine($" {h / 4 + 1}h aux={w.Power.AuxRunning} eff={aux.Machine?.Efficiency:0.00} CO={aux.Room.Air.CO:0.000} pow={aux.Room.Powered} reactor={w.Power.ReactorOnline} batt={w.Power.BatteryCharge:0}");
            }
        }
        if (what.Contains("gas"))
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.RoomsOf(RoomType.LifeSupport).First();
            Player.Hazard(w, HazardKind.GasLeak, room.Cells[0]);
            for (int h = 0; h < 14 * 2; h++)
            {
                Run(w, SimTime.Minutes(30));
                Console.WriteLine($" {h * 0.5f + 0.5f:0.0}h tox={room.Air.Toxin:0.00} vent={room.VentOpen} want={Hull.WantVentOpen(w, room)} src={w.Hazards.GasSource(room)?.Name} sealed={room.VentSealed} jam={room.DamperJammed}/{room.DamperStuck} pow={room.Powered} damp={w.Automation.DampersIn(room)} leak={room.Leaking} fire={w.Fire.IsKnown(room)} duct={w.Structure.DuctOpen} purge={room.Purging}/{room.Inerting} hold={w.Automation.KeepDamperShut(room)}");
            }
        }
        if (what.Contains("voy"))
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(2));
            Console.WriteLine($"엔진 {string.Join(",", w.Propulsion.Engines.Select(m => $"{m.Name}:{m.Efficiency:0.00}/{m.Body.Room.Name}/pow{m.Powered}"))} · 표류 {w.Voyage.Drifting} · 추진제 {w.Propulsion.Propellant:0}/{w.Propulsion.Capacity:0}");
            Console.WriteLine($"채집 팔 {string.Join(",", w.Ship.FurnitureOf(FurnitureType.Collector).Select(f => $"{f.Label}:{f.Machine!.Efficiency:0.00}"))}");
            Console.WriteLine("처음 넷: " + string.Join(",", w.Ship.Machines.OrderBy(m => m.Body.Id).Take(4).Select(m => m.Name)));
        }
        if (what.Contains("power"))
        {
            foreach (var key in new[] { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Cheonma" })
            {
                var w = World.CreateDefault(seed, 0, key);
                var line = new List<string>();
                for (int h = 0; h < 24; h += 3)
                {
                    Run(w, SimTime.Hours(3));
                    var p = w.Power;
                    line.Add($"{h + 3}h 한도{p.ReactorLimit:0}/수요{p.Demand:0}/전체{p.FullDemand:0} 끊음{p.ShedCount} 배터리{p.BatteryPercent * 100:0}% 엔진{(w.Propulsion.Engines.Any(m => m.Powered) ? "켜짐" : "꺼짐")}");
                }
                Console.WriteLine($"{key}: " + string.Join(" | ", line));
            }
        }
        if (what.Contains("hash"))
        {
            foreach (var (key, h) in new[] { ("Mirinae", 30), ("Hanbit", 30), ("Eunha", 24), ("Cheonma", 12), ("gen:hospital:spine:16:3", 12) })
            {
                var w = World.CreateDefault(seed, 0, key);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Run(w, SimTime.Hours(h));
                Console.WriteLine($"지문 {key} {h}h = {SaveGame.StateHash(w):x8} · {sw.ElapsedMilliseconds}ms");
            }
        }
        if (what.Contains("quar"))
        {
            foreach (var key in new[] { ShipGenerator.KeyFor(12, seed), "Hanbit" })
            {
                var w = World.CreateDefault(seed, 0, key);
                Console.WriteLine($"{key}: 격리실 {w.Ship.KindOf(RoomType.Quarantine).Count()} · 의무실 {w.Ship.KindOf(RoomType.Medbay).Count()} · 사람 {w.Crew.Count}");
            }
        }
    }

    /// <summary>
    /// 통합 4차 위험 수준: 센 폭풍에 바깥 방에 남은 사람 · 벽이 터진 방의 사람 · 닫힌 방의 불씨 · 대피소 밖의 우주급 방사선 —
    /// 사고가 사람에게 닿는 길이 실제로 있고, 까닭이 몸 · 기억 · 기록에 남는다.
    /// </summary>
    private static void IgDanger(int seed)
    {
        // ① 센 태양 폭풍: 대피소 밖 바깥 방에 붙들린 사람은 방사선 병 · 대피소의 사람은 조금
        {
            var w = DayOne(seed, "Hanbit"); w.CrewCanDie = true;
            var outer = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.Cells.Count(w.Ship.IsOpenFloor) >= 2).OrderByDescending(r => w.Ambience.Exposure(r)).ThenBy(r => r.Id).First();
            var shelter = Facilities.Best(w.Ship, "shelter", r => !r.Detached).room;
            var v = w.Crew.First(c => c.CanAct);
            var s0 = w.Crew.First(c => c.CanAct && c != v);
            Put(w, v, outer);
            if (shelter != null) Put(w, s0, shelter);
            w.Hazards.StartStorm();
            typeof(HazardSystem).GetProperty("StormPeak")!.SetValue(w.Hazards, 3.5f); // 센 폭풍 (2.2~4) 위쪽
            for (int t = 0; t < SimTime.Hours(3); t += 25)
            {
                if (v.Room != outer) { Put(w, v, outer); v.Room = outer; } // 자리를 지켜야 하는 사람 (수동 조종 · 손을 놓지 못하는 수리)
                if (shelter != null && s0.Room != shelter) { Put(w, s0, shelter); s0.Room = shelter; }
                Run(w, 25);
            }
            Check("센 태양 폭풍 — 바깥 방에 붙들린 사람은 세 시간이면 방사선 병 (4Sv 넘게) · 대피소는 막는다",
                v.Dose >= 4f && (shelter == null || s0.Dose < v.Dose * 0.3f) && w.Ailments.Has(v, "radiation"),
                $"{outer.Name}(노출 {w.Ambience.Exposure(outer):0.00}) {v.Dose:0.0}Sv · 대피소 {shelter?.Name ?? "없음"} {s0.Dose:0.0}Sv · 단계 {w.Perils.RadStage(v)}");
        }
        // ② 벽이 터지는 순간: 그 방에 있던 사람은 급감압에 다친다 (기억 · 기록)
        {
            var w = DayOne(seed, "Hanbit"); w.CrewCanDie = true;
            var room = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.Doors.Any(d => !d.IsExternal) && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r)).OrderBy(r => r.Cells.Count).ThenBy(r => r.Id).First();
            var v = w.Crew.First(c => c.CanAct);
            Put(w, v, room); v.Room = room;
            float inj0 = v.Vitals.Injury;
            string? what = w.Major.Fire("major:cascadedecomp", room);
            Check("벽이 터지는 순간 — 그 방 사람은 급감압에 다친다 · 기억에 남는다", what != null && v.Vitals.Injury > inj0 + 0.05f && v.Memory.Marks.Any(m => m.Text.Contains("벽이 터졌다")),
                $"{what} · {v.Name} 부상 {inj0:0.00}→{v.Vitals.Injury:0.00} ({v.Vitals.InjuryCause})");
        }
        // ③ 닫힌 방의 불씨: 연기는 옅어도 일산화탄소가 소리 없이 찬다
        {
            var w = DayOne(seed, "Hanbit");
            var room = w.Ship.LiveRooms.Where(r => r.Kind is RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters).OrderBy(r => r.Volume).ThenBy(r => r.Id).First();
            room.VentOpen = false;
            foreach (var d in room.Doors) d.Locked = true; // 아무도 모르는 사이 (문을 닫고 나간 빈 선실)
            foreach (var c in w.Crew.Where(c => c.Room == room).ToList()) { var o = w.Ship.LiveRooms.First(r => r.Type == RoomType.Corridor); Put(w, c, o); c.Room = o; }
            var cell = room.Cells.First(w.Ship.IsOpenFloor);
            w.Fire.Ignite(cell, 0.2f);
            float co = 0f, smoke = 0f;
            for (int t = 0; t < SimTime.Minutes(60); t++) { if (w.Fire.At(cell) < 0.15f) w.Fire.Ignite(cell, 0.2f); room.VentOpen = false; w.Step(); co = MathF.Max(co, room.Air.CO); smoke = MathF.Max(smoke, room.Air.Smoke); }
            Check("불씨 — 닫힌 방에서 연기만 피우는 불씨는 일산화탄소를 채운다 (경보 문턱을 넘는다)", co > 0.12f, $"{room.Name} 일산화탄소 최고 {co:0.00} · 연기 {smoke:0.00}");
        }
        // ④ 우주급: 초신성 방사선은 바깥 방을 몇 시간이면 앓게 · 대피소는 견딜 만큼
        {
            var w = DayOne(seed, "Hanbit");
            var e = w.Cosmic.Force(CosmicKind.Supernova, 0.2f);
            float outRad = 0f, shelterRad = 0f;
            var shelter = Facilities.Best(w.Ship, "shelter", r => !r.Detached).room;
            for (int m = 0; m < 30 * 12 && e.Phase != CosmicPhase.Done; m++)
            {
                Run(w, SimTime.Minutes(5));
                foreach (var r in w.Ship.LiveRooms) outRad = MathF.Max(outRad, r.Radiation);
                if (shelter != null) shelterRad = MathF.Max(shelterRad, shelter.Radiation);
            }
            // 숨은 사람이 견딜 만큼으로 맞췄다 (우주 시험: 대피소 쪽 평균 0.8Sv 아래) — 바깥 방은 예전의 세 배 남짓
            Check("우주급 — 초신성: 바깥 방은 시간당 0.8Sv 남짓 (몇 시간이면 방사선 병) · 대피소는 그 몇 분의 일", outRad >= 1.6f && (shelter == null || shelterRad < outRad * 0.3f),
                $"바깥 방 최고 {outRad:0.00} (시간당 {(outRad - 0.05f) * 0.5f:0.0}Sv) · 대피소 {shelterRad:0.00}");
        }
    }
}
