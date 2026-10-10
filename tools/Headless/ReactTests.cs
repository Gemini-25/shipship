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

    private static World? _lastReact;
    private static readonly int[] _reactKinds = new int[16];

    /// <summary>앞 장면의 반응 종류를 모은다 (어떤 변화에 반응했나).</summary>
    private static void TallyReact()
    {
        if (_lastReact == null) return;
        for (int i = 0; i < 16; i++) _reactKinds[i] += _lastReact.React.Stats.ByStir[i];
        _lastReact = null;
    }

    private static World ReactDay(int seed)
    {
        TallyReact();
        var w = DayOne(seed, "Hanbit");
        _lastReact = w;
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

    private static void Hold(World w, Room room, float temp, int ticks, Action? every = null)
    {
        for (int t = 0; t < ticks; t++) { room.Air.Temperature = temp; w.Step(); if (every != null && t % 20 == 0) every(); }
    }

    private static int RunReactTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"모든 변화에 누군가 반응한다 (v17.8) · 시드 {seed}\n");
        var allLines = new List<string>();
        Array.Clear(_reactKinds); _lastReact = null;

        // ── 1) 같은 정전에 다섯 사람이 다섯 가지로 ──
        {
            var w = ReactDay(seed);
            var mess = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var five = Awake(w, 5);
            Gather(w, five, mess);
            Run(w, 20);
            mess.PowerCut = true;
            var rs = w.React;
            CrewMember? torchMan = null; float torchMul = 0f, plainMul = 0f;
            for (int t = 0; t < SimTime.Minutes(50); t += 2)
            {
                Run(w, 2);
                if (torchMan != null) continue;
                torchMan = five.FirstOrDefault(c => rs.Peek(c)?.TorchOn == true && c.Room == mess);
                if (torchMan == null) continue;
                torchMul = rs.DarkMul(torchMan);
                plainMul = five.Where(c => rs.Peek(c)?.Way is null or "wait" or "feel").Select(c => rs.DarkMul(c)).DefaultIfEmpty(1f).Max();
            }
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
            Check("상호작용 — 손전등을 켠 사람은 캄캄한 데서 덜 틀린다 (실수 배율)", torchMan != null && torchMul < plainMul || torchMan == null && !ways.Contains("torch"),
                torchMan == null ? "손전등 고른 사람 없음" : $"{torchMan.Name} 손전등 {torchMul:0.00} · 맨손 {plainMul:0.00}");
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
            var rs = w.React;
            var sweatSeen = new HashSet<int>();
            float heatMulMin = 1f; // 겉옷 · 선풍기 · 찬물로 열이 덜 차는 순간
            Hold(w, room, 32f, SimTime.Minutes(45), () => { foreach (var c in ppl) { if (rs.Peek(c)?.Sweat > 0) sweatSeen.Add(c.Id); if (c.Room == room) heatMulMin = MathF.Min(heatMulMin, rs.HeatMul(c)); } });
            int sweaty = sweatSeen.Count;
            Console.WriteLine("   " + string.Join(" | ", ppl.Select(c => $"{c.Name} {c.Room?.Name} {c.Room?.Air.Temperature:0} 땀{rs.Peek(c)?.Sweat} {rs.Peek(c)?.Way} {c.Job?.Activity?.Id}")));
            var ways = rs.NotesOf(Stir.Heat).Select(n => n.Way).Where(x => x.Length > 0).ToList();
            var lines = rs.NotesOf(Stir.Heat).Where(n => n.Line.Length > 0).Select(n => n.Line).ToList();
            allLines.AddRange(lines);
            Check("더위 — 땀이 나고 (몸에 보인다) 사람마다 다르게 식힌다 (겉옷 · 선풍기 · 손부채 · 찬물 · 시원한 방)", sweaty >= 3 && ways.Count >= 3 && ways.All(HeatWaysIds.Contains) && ways.Distinct().Count() >= 2,
                $"땀 {sweaty}/{ppl.Count} · {string.Join(", ", ways)} · 말: {string.Join(" / ", lines.Take(3))}");
            Check("더위 — 겉옷을 벗어 허리에 묶거나 선풍기를 가져온다 · 그만큼 열이 덜 찬다", (rs.Stats.Jackets > 0 || rs.Stats.Devices > 0) && heatMulMin < 1f,
                $"겉옷 {rs.Stats.Jackets} · 장비 {rs.Stats.Devices} · 가장 낮은 배율 {heatMulMin:0.00} · 행동 {rs.Stats.Acts}/{rs.Stats.ActsDone}");
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
            var rs = w.React;
            var shivSeen = new HashSet<int>();
            Hold(w, room, 11f, SimTime.Minutes(60), () => { foreach (var c in ppl) if (rs.Peek(c)?.Shiver > 0 || rs.Peek(c)?.Wrapped == true) shivSeen.Add(c.Id); });
            int shiv = shivSeen.Count;
            Console.WriteLine("   " + string.Join(" | ", ppl.Select(c => $"{c.Name} {c.Room?.Name} {c.Room?.Air.Temperature:0} 떨{rs.Peek(c)?.Shiver} {rs.Peek(c)?.Way} {c.Job?.Activity?.Id}")));
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
                    $"소리 반응 {sn.Count} · 행동 {rs.Stats.Acts}/{rs.Stats.ActsDone} · 확인 {rs.Stats.Checks} · 찾음 {rs.Stats.Found} · 재확인 {rs.Stats.Scans} · 알려짐 {m.Omen?.Known}");
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

        // ── 7b) 연기 · 흔들림 · 냄새 · 새 물건 · 우는 사람 — 사람마다 다른 행동 ──
        {
            var w = ReactDay(seed);
            var rs = w.React;
            var mess = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var nb = w.Ambience.Neighbors(mess).Where(x => x.Item2 && !x.Item1.OffLimits && x.Item1.Air.Pressure > 70f).Select(x => x.Item1).OrderBy(r => r.Id).FirstOrDefault();
            var six = Awake(w, 6, c => c.Id % 5);
            Gather(w, six, mess);
            Run(w, 20);
            // 옆방에서 연기가 흘러든다 (그 방엔 아무도 없고 감지기도 꺼졌다 — 불은 아직 아무도 모른다)
            Cell? fireCell = null;
            var smokeActs = new SortedSet<string>(); // 연기에 움직인 사람 (가서 보기 · 탄내 확인 · 피하기)
            if (nb != null)
            {
                nb.PowerCut = true;
                // 그 방에 있던 사람은 멀리 보낸다 (아무도 불을 못 본다)
                var far = w.Ship.LiveRooms.Where(r => r != nb && r != mess && r.Type is RoomType.Quarters or RoomType.Lounge && r.Cells.Any(w.Ship.IsOpenFloor)).OrderBy(r => r.Id).First();
                var farCell = far.Cells.First(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x));
                foreach (var c in w.Crew) if (!c.Dead && c.Room == nb && !six.Contains(c)) { Stay(w, c, farCell, Pose.Standing); c.NextThinkTick = w.Tick + SimTime.Minutes(40); }
                fireCell = nb.Cells.Where(x => w.Ship.IsOpenFloor(x)).OrderBy(x => (x.Center - nb.Center).LengthSquared()).First();
                w.Fire.Ignite(fireCell.Value, 0.25f);
                for (int t = 0; t < SimTime.Minutes(30); t++) { nb.Air.Smoke = MathF.Max(nb.Air.Smoke, 0.45f); mess.Air.Smoke = 0.1f; w.Step(); mess.Air.Smoke = MathF.Min(mess.Air.Smoke, 0.14f);
                    if (t % 10 == 0) foreach (var c in six) if (c.Job?.Activity?.Id is "react" or "checksmell" or "evacuate") smokeActs.Add($"{c.Name}:{c.Job.Activity.Id}");
                }
            }
            var sm = rs.NotesOf(Stir.Smoke).Where(n => six.Any(c => c.Id == n.Crew)).ToList();
            Console.WriteLine("   연기 뒤: " + string.Join(" | ", six.Select(c => $"{c.Name} {c.Room?.Name} {c.Job?.Activity?.Id}")) + $" · 위기 {Crisis.Level(w)} · 행동 {rs.Stats.Acts}/{rs.Stats.ActsDone}");
            var smWays = sm.Select(n => n.Way.Length == 0 ? "stay" : n.Way).Distinct().ToList();
            var smG = sm.Select(n => n.Gesture).Distinct().ToList();
            Check("연기 — 옆방에서 흘러든 연기에 기침하고 입을 막는다 · 누구는 출처를 찾아가고 누구는 맑은 방으로 피한다", nb != null && sm.Count >= 3 && smWays.Count >= 2 && (rs.Stats.SmokeSeek + rs.Stats.SmokeFled > 0 || smokeActs.Count >= 2),
                $"{nb?.Name} · 반응 {sm.Count} · 움직임 {string.Join(" ", smokeActs.Take(5))} · {string.Join(", ", smWays)} · 몸짓 {string.Join(",", smG)} · 찾아감 {rs.Stats.SmokeSeek}(불 {rs.Stats.SmokeFound}) · 피함 {rs.Stats.SmokeFled} · 말: {string.Join(" / ", sm.Select(n => n.Line).Where(l => l.Length > 0).Take(3))}");
            var calls = rs.NotesOf(Stir.Voice).Where(n => n.Way == "answer").ToList();
            Check("주 컴퓨터 — 기침하는 사람들을 보고 연기가 어디서 오는지 짚어 방송한다 (감지기 꺼진 방이면 가 볼 사람을 부른다 · 누군가 대답하고 간다)", rs.Stats.SmokeAdvice > 0,
                $"연기 안내 {rs.Stats.SmokeAdvice} · 대답 {calls.Count} · 방송: {w.Automation.Speak.Recent.Select(b => b.Text).LastOrDefault(t => t.Contains("연기")) ?? "—"}");
            Check("상호작용 — 연기를 쫓아간 사람이 감지기가 꺼진 방의 불을 먼저 본다", nb == null || rs.Stats.SmokeSeek == 0 || rs.Stats.SmokeFound > 0 && w.Fire.IsKnown(nb) || w.Fire.CountIn(nb) == 0,
                $"찾아감 {rs.Stats.SmokeSeek} · 불 찾음 {rs.Stats.SmokeFound} · 알려짐 {(nb != null && w.Fire.IsKnown(nb))}");
        }
        {
            var w = ReactDay(seed);
            var rs = w.React;
            var room = w.Ship.LiveRooms.First(r => r.Type == RoomType.Mess);
            var six = Awake(w, 6, c => -c.Id);
            Gather(w, six, room);
            Run(w, 20);
            for (int t = 0; t < SimTime.Minutes(15); t++) { room.Vibration = 0.85f; w.Step(); }
            var sh = rs.NotesOf(Stir.Shake).Where(n => six.Any(c => c.Id == n.Crew)).ToList();
            var shWays = sh.Select(n => n.Way.Length == 0 ? "?" : n.Way).Distinct().ToList();
            Check("흔들림 — 몸을 버티고 · 겁 많은 사람은 웅크리고 · 대담한 사람은 으쓱하고 · 기계를 아는 사람은 떨림을 살피러 간다", sh.Count >= 3 && shWays.Count >= 2,
                $"반응 {sh.Count} · {string.Join(", ", shWays)} · 몸짓 {string.Join(",", sh.Select(n => n.Gesture).Distinct())} · 말: {string.Join(" / ", sh.Select(n => n.Line).Where(l => l.Length > 0).Take(3))}");
            // 냄새: 주방에서 빵 굽는 냄새 · 배고픈 사람은 따라간다
            foreach (var c in six) c.Needs.Food = 0.35f;
            Gather(w, six, room);
            for (int t = 0; t < SimTime.Minutes(20); t++) { w.Smells.Emit(room, SmellKind.Bread, 0.6f); w.Step(); } // 빵 굽는 화구 곁만큼 (0.9) — 전엔 0.08을 뿌렸는데 갱신 때 지워져 우연히 나던 다른 냄새로 통과했다
            var sn = rs.NotesOf(Stir.Smell).Where(n => six.Any(c => c.Id == n.Crew)).ToList();
            Check("냄새 — 빵 냄새에 코를 킁킁대고 · 배고픈 사람은 냄새를 따라간다", sn.Count >= 2 && sn.Any(n => n.Gesture == Gesture.Sniff),
                $"반응 {sn.Count} · {string.Join(", ", sn.Select(n => n.Way.Length == 0 ? "—" : n.Way).Distinct())} · 따라감 {rs.Stats.Nose} · 말: {string.Join(" / ", sn.Select(n => n.Line).Where(l => l.Length > 0).Take(3))}");
            // 새 벽화: 다른 사람이 그린 그림이 걸렸다 → 구경 · 칭찬
            var painter = w.Crew.First(c => !c.Dead && !six.Contains(c) && !c.IsChild);
            var pic = w.Props.Place(Props.Get("landscape"), room, painter, "취미로 그림");
            Run(w, SimTime.Minutes(40));
            var nv = rs.NotesOf(Stir.Novel).ToList();
            Check("새 그림 — 걸린 그림을 알아보고 들여다본다 (그린 사람 이야기)", pic != null && nv.Count >= 2 && nv.Any(n => n.Gesture == Gesture.Admire),
                $"반응 {nv.Count} · 구경 {rs.Stats.Admired} · 말: {string.Join(" / ", nv.Select(n => n.Line).Where(l => l.Length > 0).Take(3))}");
            // 우는 사람: 친한 사람은 곁으로 가고 · 아닌 사람은 모른 척해 준다
            var sad = six[0];
            foreach (var c in six.Skip(1).Take(2)) { c.ChangeAffinity(sad, 0.6f); }
            var seat = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - room.Center).LengthSquared()).First();
            Gather(w, six.Skip(1).ToList(), room);
            float sadMax = 0f;
            for (int k = 0; k < 12; k++)
            {
                w.Brain2.Emotions.Feel(sad, Feeling.Sadness, 0.9f, "집에서 온 편지를 읽었다");
                Stay(w, sad, seat, Pose.Sitting);
                sad.NextThinkTick = w.Tick + SimTime.Minutes(5);
                sadMax = MathF.Max(sadMax, w.Brain2.Emotions.Get(sad, Feeling.Sadness));
                Run(w, SimTime.Minutes(4));
            }
            var cr = rs.NotesOf(Stir.Cry).Where(n => n.Crew != sad.Id).ToList();
            Check("우는 사람 — 곁의 사람이 알아보고 위로하러 가거나 모른 척해 준다", cr.Count >= 1 && (rs.Stats.Comforts > 0 || cr.Any(n => n.Line.Length > 0)),
                $"슬픔 {sadMax:0.00} · 우는 중 {rs.Crying(sad)} · 반응 {cr.Count} · 위로 {rs.Stats.Comforts} · 말: {string.Join(" / ", cr.Select(n => n.Line).Where(l => l.Length > 0).Take(2))}");
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
            var fighter = w.Crew.First(c => !c.Dead && !c.IsChild && !ppl.Contains(c));
            fighter.Quarrel = w.Tick - SimTime.Hours(2);
            foreach (var c in ppl.Take(3)) c.ChangeAffinity(fighter, 0.5f);
            Gather(w, ppl, room);
            Run(w, 20);
            room.PowerCut = true;
            Run(w, SimTime.Minutes(40));
            room.PowerCut = false;
            // 기록은 오래되면 지워지므로 한 시간씩 모은다
            var said = new List<string>();
            long seenTo = -1;
            void Collect() { foreach (var n in rs.Notes) if (n.Tick > seenTo && n.Line.Length > 0) said.Add(n.Line); if (rs.Notes.Count > 0) seenTo = Math.Max(seenTo, rs.Notes[^1].Tick); }
            Collect();
            for (int h = 0; h < 12; h++) { Run(w, SimTime.Hours(1)); Collect(); }
            allLines.AddRange(said);
            bool dec = said.Any(l => l.Contains("회의")), sky = said.Any(l => l.Contains("폭풍")), ep = said.Any(l => l.Contains("기름 불"));
            bool rel = said.Any(l => l.Contains(fighter.Name) && (l.Contains("다퉜") || l.Contains("말다툼") || l.Contains("싸웠"))), brief = said.Any(l => l.Contains("아침 방송") || l.Contains("아침엔"));
            Check("말이 지금을 담는다 — 회의 결정 · 우주 날씨 예보 · 최근 사고 · 다툰 사람 · 아침 방송이 대사에 나온다", (dec ? 1 : 0) + (sky ? 1 : 0) + (ep ? 1 : 0) + (rel ? 1 : 0) + (brief ? 1 : 0) >= 3 && rs.Stats.Topical >= 3,
                $"회의 {dec} · 예보 {sky} · 사고 {ep} · 관계 {rel} · 아침 방송 {brief} · 지금 이야기 {rs.Stats.Topical} ({rs.Stats.Topics}가지) · 예: {string.Join(" / ", said.Where(l => l.Contains(fighter.Name) || l.Contains("회의")).Take(3))}");
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
            TallyReact();
            for (int i = 0; i < 16; i++) _reactKinds[i] += w.React.Stats.ByStir[i];
            var kinds = Enum.GetValues<Stir>().Where(k => _reactKinds[(int)k] > 0).ToList();
            var none = Enum.GetValues<Stir>().Where(k => _reactKinds[(int)k] == 0).ToList();
            Check("반응 종류 — 더위 · 추위 · 어둠 · 바닥 · 유리 · 소리 · 냄새 · 연기 · 몸짓 · 진동 · 경보 · 방송 · 새 물건 · 울음 · 수다 · 돌아옴", kinds.Count >= 15,
                $"{kinds.Count}/16 · " + string.Join(" ", kinds.Select(k => $"{k}{_reactKinds[(int)k]}")) + (none.Count > 0 ? $" · 없음: {string.Join(",", none)}" : ""));
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
