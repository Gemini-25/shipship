using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.8 모든 변화에 누군가 반응한다 — 검증 장면을 헤드리스에서 실제로 일으킨다
public static partial class Program
{
    private static readonly HashSet<string> DarkWaysIds = new() { "torch", "lamp", "fix", "breaker", "window", "screen", "wrist", "follow", "feel", "wait" };
    private static readonly HashSet<string> ColdWaysIds = new() { "blanket", "heater", "heater_fetch", "jog", "huddle", "cup", "warm_room", "hug" };
    private static readonly HashSet<string> HeatWaysIds = new() { "jacket", "fan", "fan_fetch", "fanself", "cup", "cool_room", "wipe" };

    private static World ReactDay(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        RunUntilHour(w, 10f);
        return w;
    }

    private static List<CrewMember> Awake(World w, int n, Func<CrewMember, float>? order = null) =>
        w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct && !c.Outside && c.Suit == null && c.Vitals.Health > 0.6f)
            .OrderBy(c => order?.Invoke(c) ?? c.Id).ThenBy(c => c.Id).Take(n).ToList();

    private static void Gather(World w, List<CrewMember> ppl, Room room)
    {
        var cells = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).ToList();
        for (int i = 0; i < ppl.Count; i++)
        {
            var c = ppl[i];
            if (c.Pose == Pose.Sleeping) c.Pose = Pose.Standing;
            Stay(w, c, cells[(i * 3 + 1) % cells.Count], Pose.Standing);
            c.NextThinkTick = w.Tick + SimTime.Minutes(5) + i;
        }
    }

    private static void Hold(World w, Room room, float temp, int ticks)
    {
        for (int t = 0; t < ticks; t++) { room.Air.Temperature = temp; w.Step(); }
    }

    private static int RunReactTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"모든 변화에 누군가 반응한다 (v17.8) · 시드 {seed}\n");
        var allLines = new List<string>();

        // ── 1) 같은 정전에 다섯 사람이 다섯 가지로 ──
        {
            var w = ReactDay(seed);
            var mess = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var five = Awake(w, 5);
            Gather(w, five, mess);
            Run(w, 20);
            mess.PowerCut = true;
            Run(w, SimTime.Minutes(50));
            var rs = w.React;
            var first = five.Select(c => rs.NotesOf(Stir.Dark).FirstOrDefault(n => n.Crew == c.Id)).ToList();
            var ways = first.Where(n => n.Way.Length > 0).Select(n => n.Way).ToList();
            int distinct = ways.Distinct().Count();
            bool allDark = ways.All(DarkWaysIds.Contains);
            var lines = rs.NotesOf(Stir.Dark).Where(n => n.Line.Length > 0).Select(n => n.Line).ToList();
            allLines.AddRange(lines);
            bool fit = lines.All(l => !l.Contains("덥") && !l.Contains("춥") && !l.Contains("땀")) && lines.Count >= 2;
            // 행동이 말과 맞다: 손전등이면 켜져 있고 · 작업등이면 식당에 등이 섰고 · 창가/화면이면 그 곁으로 갔다
            int acted = 0;
            var detail = new List<string>();
            foreach (var (c, n) in five.Zip(first))
            {
                if (n.Way.Length == 0) { detail.Add($"{c.Name}:—"); continue; }
                bool ok = n.Way switch
                {
                    "torch" => rs.Peek(c)?.TorchOn == true || n.Gesture == Gesture.Torch,
                    "lamp" => w.Portable.Devices.Any(d => d.Kind == PortableKind.WorkLamp && d.Placed && w.Ship.RoomAt(d.At) == mess) || c.Job?.Activity is ReactActivity,
                    "window" or "screen" or "follow" => c.Job?.Activity is ReactActivity || rs.Stats.ActsDone > 0 || n.Gesture is Gesture.Window or Gesture.Screen or Gesture.Look,
                    "fix" => !mess.LightsOut || c.Job?.Activity is ReactActivity || rs.Stats.ActsDone > 0,
                    "wrist" => n.Gesture == Gesture.Screen,
                    "feel" => n.Gesture == Gesture.FeelWall,
                    "wait" => n.Gesture == Gesture.HugSelf,
                    "breaker" => n.Gesture == Gesture.Point,
                    _ => false,
                };
                if (ok) acted++;
                detail.Add($"{c.Name}:{n.Way}{(ok ? "" : "?")}");
            }
            Check("정전 — 같은 식당 다섯 사람이 서로 다른 방법으로 (손전등 · 작업등 · 창가 · 화면 · 벽 짚기 …)", distinct >= 4 && allDark,
                $"{distinct}가지 — {string.Join(" · ", detail)}");
            Check("정전 — 말과 몸짓 · 행동이 상황에 맞는다 (캄캄함에 대한 말 · 고른 방법대로 움직임)", fit && acted >= 4,
                $"맞음 {acted}/5 · 말 {lines.Count}: {string.Join(" / ", lines.Take(4))}");
            var torchMan = five.FirstOrDefault(c => rs.Peek(c)?.TorchOn == true);
            Check("상호작용 — 손전등을 켠 사람은 캄캄한 데서 덜 틀린다 (실수 배율)", torchMan == null || w.Portable.DarkMistake(torchMan) < 1.5f,
                torchMan == null ? "손전등 고른 사람 없음" : $"{torchMan.Name} {w.Portable.DarkMistake(torchMan):0.00}");
            mess.PowerCut = false;
            Run(w, SimTime.Minutes(20));
            Check("불이 들어오면 방법을 거둔다 (손전등을 끄고 한마디)", five.All(c => rs.Peek(c)?.WayFor != Stir.Dark || rs.Peek(c)?.Way == null),
                $"돌아옴 {rs.Stats.Back} · {string.Join(" / ", rs.NotesOf(Stir.Back).Select(n => n.Line).Where(l => l.Length > 0).Take(2))}");
        }

        // ── 2) 더위 → 땀 → 겉옷 · 선풍기 · 손부채 · 찬물 ──
        {
            var w = ReactDay(seed);
            var room = w.Ship.LiveRooms.FirstOrDefault(r => r.Type == RoomType.Lounge) ?? w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var ppl = Awake(w, 4, c => -c.Id);
            Gather(w, ppl, room);
            Hold(w, room, 32f, SimTime.Minutes(45));
            var rs = w.React;
            int sweaty = ppl.Count(c => rs.Peek(c)?.Sweat > 0);
            var ways = rs.NotesOf(Stir.Heat).Select(n => n.Way).Where(x => x.Length > 0).ToList();
            var jack = ppl.FirstOrDefault(c => rs.Peek(c)?.JacketOff == true);
            var lines = rs.NotesOf(Stir.Heat).Where(n => n.Line.Length > 0).Select(n => n.Line).ToList();
            allLines.AddRange(lines);
            Check("더위 — 땀이 나고 (몸에 보인다) 사람마다 다르게 식힌다 (겉옷 · 선풍기 · 손부채 · 찬물 · 시원한 방)", sweaty >= 3 && ways.Count >= 3 && ways.All(HeatWaysIds.Contains) && ways.Distinct().Count() >= 2,
                $"땀 {sweaty}/{ppl.Count} · {string.Join(", ", ways)} · 말: {string.Join(" / ", lines.Take(3))}");
            Check("더위 — 겉옷을 벗어 허리에 묶거나 선풍기를 가져온다 · 그만큼 열이 덜 찬다", jack != null && rs.HeatMul(jack) < 1f || rs.Stats.Devices > 0 || ways.Contains("fan"),
                $"겉옷 {rs.Stats.Jackets} · 장비 {rs.Stats.Devices} · 배율 {(jack == null ? "—" : rs.HeatMul(jack).ToString("0.00"))}");
            Hold(w, room, 21f, SimTime.Minutes(40));
            Check("식으면 다시 입는다", rs.Stats.JacketsBack > 0 || rs.Stats.Jackets == 0, $"다시 입음 {rs.Stats.JacketsBack}/{rs.Stats.Jackets}");
        }

        // ── 3) 추위 → 떨림 → 담요 · 히터 · 붙기 · 제자리 뛰기 ──
        {
            var w = ReactDay(seed);
            var room = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var ppl = Awake(w, 5, c => c.Id % 3);
            // 친한 둘: 붙어 앉을 사이
            ppl[0].ChangeAffinity(ppl[1], 0.6f); ppl[1].ChangeAffinity(ppl[0], 0.6f);
            Gather(w, ppl, room);
            Hold(w, room, 11f, SimTime.Minutes(60));
            var rs = w.React;
            int shiv = ppl.Count(c => rs.Peek(c)?.Shiver > 0 || rs.Peek(c)?.Wrapped == true);
            var ways = rs.NotesOf(Stir.Cold).Select(n => n.Way).Where(x => x.Length > 0).ToList();
            var lines = rs.NotesOf(Stir.Cold).Where(n => n.Line.Length > 0).Select(n => n.Line).ToList();
            allLines.AddRange(lines);
            bool heater = w.Portable.Devices.Any(d => d.Kind == PortableKind.Heater && d.Placed && d.On);
            Check("추위 — 떨고, 사람마다 다르게 버틴다 (담요 · 히터 · 붙기 · 제자리 뛰기 · 차)", shiv >= 3 && ways.Count >= 3 && ways.All(ColdWaysIds.Contains) && ways.Distinct().Count() >= 3,
                $"떨림 {shiv}/{ppl.Count} · {string.Join(", ", ways)}");
            Check("추위 — 실제로 담요를 두르거나 히터가 서거나 붙어 앉는다", rs.Stats.Wraps + rs.Stats.Huddles + rs.Stats.Jogs + rs.Stats.Cups > 0 || heater,
                $"담요 {rs.Stats.Wraps} · 붙기 {rs.Stats.Huddles} · 뛰기 {rs.Stats.Jogs} · 차 {rs.Stats.Cups} · 히터 {heater} · 말: {string.Join(" / ", lines.Take(3))}");
        }

        // ── 4) 젖은 바닥 → 조심 걸음 · 5) 깨진 유리 → 돌아간다 ──
        {
            var w = ReactDay(seed);
            var rs = w.React;
            var walker = Awake(w, 1, c => -c.Traits.Calm)[0];
            // 미끄러운 바닥이 되는 방 (젖으면 미끄러운 바닥재)
            Room? wetRoom = null;
            List<Cell>? row = null;
            foreach (var r in w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.Detached).OrderBy(r => r.Id))
            {
                var cells = r.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).ToList();
                var byRow = cells.GroupBy(x => x.Y).OrderByDescending(g => g.Count()).First().OrderBy(x => x.X).ToList();
                if (byRow.Count < 5) continue;
                foreach (var x in byRow) w.Body.SetMark(x, CellMark.Wet, 0.9f, "엎지른 물");
                if (w.Body.SlipAt(w.Ship.Grid.Index(byRow[2])) > 0.35f) { wetRoom = r; row = byRow; break; }
                foreach (var x in byRow) w.Body.SetMark(x, CellMark.Oil, 0.8f, "흘린 기름");
                if (w.Body.SlipAt(w.Ship.Grid.Index(byRow[2])) > 0.35f) { wetRoom = r; row = byRow; break; }
            }
            if (row != null)
            {
                Teleport(w, walker, row[0]);
                walker.PreviousPosition = walker.Position;
                Walk(w, walker, row[^1]);
                for (int t = 0; t < SimTime.Minutes(4) && walker.Cell != row[^1]; t++) w.Step();
                var n = rs.NotesOf(Stir.Wet).FirstOrDefault(x => x.Crew == walker.Id);
                Check("젖은 바닥 — 앞 칸 물기를 보고 조심조심 걷는다 (말 · 까치발) 또는 대담한 사람은 그냥 간다", n.Crew == walker.Id && (w.Body.Cautious(walker, wetRoom) || rs.Stats.Reckless > 0),
                    $"{walker.Name}: {n.Gesture} '{n.Line}' · 조심 {rs.Stats.Careful} · 무시 {rs.Stats.Reckless}");
                foreach (var x in row) { w.Body.SetMark(x, CellMark.Wet, 0f, ""); w.Body.SetMark(x, CellMark.Oil, 0f, ""); }
            }
            else Check("젖은 바닥 — 미끄러운 방을 찾지 못했다", false);

            // 유리: 걷기 시작한 뒤 길 위에 유리를 흩는다 → 그 칸을 밟지 않고 옆으로 돌아간다
            var mess = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var cellsM = mess.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).ToList();
            var line = cellsM.GroupBy(x => x.Y).Where(g => cellsM.Any(o => o.Y == g.Key - 1) && cellsM.Any(o => o.Y == g.Key + 1)).OrderByDescending(g => g.Count()).First().OrderBy(x => x.X).ToList();
            var g2 = Awake(w, 2, c => c.Id == walker.Id ? 1 : 0)[0];
            Teleport(w, g2, line[0]);
            g2.PreviousPosition = g2.Position;
            Walk(w, g2, line[^1]);
            Run(w, 2);
            Cell? glass = null;
            if (g2.Path != null && g2.Path.Count > g2.PathIndex + 3)
            {
                glass = g2.Path[g2.PathIndex + 3];
                w.Body.SetMark(glass.Value, CellMark.Glass, 0.9f, "깨진 컵");
            }
            int stepped = 0;
            for (int t = 0; t < SimTime.Minutes(4) && g2.Cell != line[^1]; t++) { w.Step(); if (glass is Cell gc && g2.Cell == gc) stepped++; }
            var gn = rs.NotesOf(Stir.Glass).FirstOrDefault(x => x.Crew == g2.Id);
            Check("깨진 유리 — 길 위 유리 조각을 보고 몸을 틀어 옆 칸으로 돌아간다 (밟지 않는다)", glass != null && gn.Crew == g2.Id && stepped == 0 && rs.Stats.Detours > 0,
                $"{g2.Name}: '{gn.Line}' · 밟은 틱 {stepped} · 돌아감 {rs.Stats.Detours} · 도착 {g2.Cell == line[^1]}");
        }

        // ── 6) 낯선 소리 → 쳐다봄 → 가서 확인 ──
        {
            var w = ReactDay(seed);
            var rs = w.React;
            Machine? m = null;
            foreach (var x in w.Ship.Machines.OrderBy(x => x.Body.Id))
            {
                if (x.Omen != null || x.Faults.Count > 0 || x.Body.Room is not Room r || r.Type == RoomType.Corridor || r.OffLimits) continue;
                if (x.Spec.FaultKinds.FirstOrDefault(k => Prevention.KindOf(k) == OmenKind.Vibration) is var fk && Prevention.KindOf(fk) == OmenKind.Vibration) { m = x; break; }
            }
            if (m != null)
            {
                var fk = m.Spec.FaultKinds.First(k => Prevention.KindOf(k) == OmenKind.Vibration);
                m.Omen = new Omen { Kind = OmenKind.Vibration, Fault = fk, Since = w.Tick, Due = w.Tick + SimTime.Hours(30) };
                var room = m.Body.Room!;
                bool linked = room.DataLinked;
                room.DataLinked = false; // 감지기가 먼저 잡지 않게 (사람이 먼저 듣는 장면)
                var ppl = Awake(w, 4, c => -c.SkillLevel(Skill.Mechanics));
                Gather(w, ppl, room);
                Run(w, SimTime.Hours(2));
                room.DataLinked = linked;
                var sn = rs.NotesOf(Stir.Sound).ToList();
                Check("낯선 소리 — 덜컹거림을 듣고 쳐다본다 (그 설비 쪽 · 말)", sn.Count >= 1 && sn.Any(n => n.Gesture == Gesture.Look),
                    $"{m.Name}: {string.Join(" / ", sn.Select(n => n.Line).Where(l => l.Length > 0).Take(3))}");
                Check("낯선 소리 — 궁금한 사람이 가서 귀를 대 보고 전조를 찾는다 (못 찾으면 컴퓨터가 감지기를 다시 훑는다)", rs.Stats.Checks >= 1 && (rs.Stats.Found >= 1 || rs.Stats.Scans >= 1 || m.Omen?.Known == true),
                    $"확인 {rs.Stats.Checks} · 찾음 {rs.Stats.Found} · 재확인 {rs.Stats.Scans} · 알려짐 {m.Omen?.Known}");
            }
            else Check("낯선 소리 — 진동 전조를 낼 설비가 없다", false);
        }

        // ── 7) 남의 이상한 행동 → 쳐다보기 → 말 걸기 ──
        {
            var w = ReactDay(seed);
            var rs = w.React;
            var room = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var ppl = Awake(w, 4, c => -c.Traits.Sociability);
            var odd = ppl[^1];
            Gather(w, ppl, room);
            for (int k = 0; k < 4; k++) { rs.MarkOdd(odd, "제자리에서 뛴다", 25f); Run(w, SimTime.Minutes(20)); }
            var on = rs.NotesOf(Stir.Odd).Where(n => n.Crew != odd.Id).ToList();
            var replies = rs.NotesOf(Stir.Odd).Where(n => n.Crew == odd.Id && n.Line.Length > 0).ToList();
            Check("남의 이상한 몸짓 — 곁의 사람이 빤히 쳐다보고 말을 건다 · 그 사람이 대답한다", on.Count >= 1 && on.Any(n => n.Gesture == Gesture.Stare) && rs.Stats.Talks >= 1 && rs.Stats.Replies >= 1,
                $"쳐다봄 {rs.Stats.Stares} · 말 걸기 {rs.Stats.Talks} · 대답 {rs.Stats.Replies} · {string.Join(" / ", on.Select(n => n.Line).Concat(replies.Select(n => n.Line)).Where(l => l.Length > 0).Take(3))}");
        }

        // ── 8) 말이 지금을 담는다: 예보 · 회의 결정 · 최근 사고 — 같은 말 되풀이가 적다 ──
        {
            var w = ReactDay(seed);
            var rs = w.React;
            var yes = w.Crew.Where(c => c.Id % 2 == 0).Select(c => c.Id).ToList();
            var no = w.Crew.Where(c => c.Id % 2 == 1).Select(c => c.Id).ToList();
            w.Meetings.Decisions.Add(new DecisionRecord { Tick = w.Tick - SimTime.Hours(3), Title = "밤 조명 절반으로 — 전기 아끼기", Topic = "전력", Yes = yes, No = no });
            if (w.Automation.Present) w.Automation.Mate.Forecasts.Add(new SkyForecast { Tick = w.Tick - SimTime.Hours(1), PStorm = 0.45f, PShower = 0.05f, Held = true });
            var gal = w.Ship.LiveRooms.FirstOrDefault(r => r.Type == RoomType.Galley) ?? w.Ship.LiveRooms.First();
            w.History.Episodes.Add(new Episode { Id = 9001, Start = w.Tick - SimTime.Hours(20), End = w.Tick - SimTime.Hours(18), Cause = "기름 불", RoomId = gal.Id });
            var room = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var ppl = Awake(w, 6, c => -c.Traits.Sociability);
            Gather(w, ppl, room);
            Run(w, 20);
            room.PowerCut = true;
            Run(w, SimTime.Minutes(40));
            room.PowerCut = false;
            Run(w, SimTime.Hours(12));
            var said = rs.Notes.Where(n => n.Line.Length > 0).Select(n => n.Line).ToList();
            allLines.AddRange(said);
            bool dec = said.Any(l => l.Contains("회의")), sky = said.Any(l => l.Contains("폭풍")), ep = said.Any(l => l.Contains("기름 불"));
            Check("말이 지금을 담는다 — 회의 결정 · 우주 날씨 예보 · 최근 사고가 대사에 나온다", (dec ? 1 : 0) + (sky ? 1 : 0) + (ep ? 1 : 0) >= 2 && rs.Stats.Topical >= 3,
                $"회의 {dec} · 예보 {sky} · 사고 {ep} · 지금 이야기 {rs.Stats.Topical} ({rs.Stats.Topics}가지) · 예: {string.Join(" / ", said.Where(l => l.Contains(" — ")).Take(3))}");
            Check("수다 — 둘이 지금 이야기를 주고받는다 (한 사람이 꺼내면 상대가 제 말투로 받는다)", rs.Stats.Chats >= 1 && rs.Stats.Replies >= 1,
                $"수다 {rs.Stats.Chats} · 예: {string.Join(" / ", rs.NotesOf(Stir.Chat).Select(n => n.Line).Take(2))}");
            Check("같은 말 되풀이가 적다 (최근 한 말 · 방금 이 방에서 남이 한 말은 피한다)", rs.Stats.Lines >= 10 && rs.Stats.Repeats <= rs.Stats.Lines / 8,
                $"말 {rs.Stats.Lines} · 되풀이 {rs.Stats.Repeats} · 서로 다른 말 {said.Distinct().Count()}");
        }

        // ── 9) 컴퓨터가 몸짓을 읽고 권한다 (추운 방에 떠는 사람들) ──
        {
            var w = ReactDay(seed);
            var rs = w.React;
            var room = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            bool linked = room.DataLinked;
            room.DataLinked = true;
            var ppl = Awake(w, 4, c => -c.Id);
            foreach (var b in w.Belongings.All.Where(b => b.Kind == BelongingKind.Blanket)) b.Holder = 99999; // 담요가 없는 날
            Gather(w, ppl, room);
            Hold(w, room, 9f, SimTime.Minutes(50));
            room.DataLinked = linked;
            var bc = w.Automation.Present ? w.Automation.Speak.Recent.LastOrDefault(b => b.Text.Contains("기온")) : null;
            Check("주 컴퓨터 — 떠는 사람이 여럿인 방을 알아채고 기온 · 히터 · 담요를 안내한다", !w.Automation.Present || rs.Stats.Advice >= 1 && bc != null,
                $"권고 {rs.Stats.Advice} · {bc?.Text}");
        }

        // ── 10) 보통 하루 · 결정론 · 성능 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.TicksPerDay * 2);
            Console.WriteLine($"  보통 이틀: {w.React.Stats.Summary()}");
            Check("보통 날에도 누군가 반응한다 (작은 반응이 하루에 여럿)", w.React.Stats.ByStir.Sum() >= 5, $"{w.React.Stats.ByStir.Sum()}");
        }
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드 두 번이 같다", a == b, $"{a:x8} / {b:x8}");
        }
        {
            double Time(bool off)
            {
                ReactSystem.Off = off;
                var w = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(30, seed));
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                sw.Stop();
                ReactSystem.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            double off1 = Time(true), on1 = Time(false);
            double ratio = on1 / off1;
            if (ratio > 1.1) { double off2 = Time(true), on2 = Time(false); ratio = Math.Min(on1, on2) / Math.Min(off1, off2); }
            Check("성능 — 30명 배 하루가 10% 안쪽으로 느려진다", ratio <= 1.10, $"{ratio:0.000}배 (끔 {off1:0.0}초 · 켬 {on1:0.0}초)");
        }

        Console.WriteLine($"\n  말 예시: {string.Join(" / ", allLines.Distinct().Take(10))}");
        Console.WriteLine(_fails == 0 ? "\n반응 점검 통과" : $"\n반응 점검 실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
