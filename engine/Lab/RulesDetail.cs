using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 자세한 판 (docs/UI2.md §4 · §8 D1 · D2 · D4 — 묶음 '자세한 판'): 사람 작은 탭마다의 줄 (몸 · 마음 · 하는 일 · 아는 것 — '누가 어떻게 아나' · 사람 사이 · 일지 · 지나온 길) ·
/// 판단 일지 32칸 · 로봇 · 드론 자세히. 줄은 세계에서 읽기만 한다 (시험 차림은 시험 세계에 직접).
/// </summary>
public static partial class Program
{
    private static PanelRow[] DetailOf(LiveShow show, int sub)
    {
        show.SetDetailSub(sub);
        var f = show.Render(); show.Recycle(f.Img);
        return f.PanelRows;
    }

    private static void DetailRules()
    {
        if (!Do("자세한 판")) return;
        Console.WriteLine("\n[자세한 판 — 사람 작은 탭 · 판단 일지 32칸 · 로봇]");
        var show = LiveShow.Crewed("Didim", 21);
        show.FrameBudget = 1000; show.SpeedOverride = 1;
        for (int i = 0; i < 3; i++) show.Advance(1);
        var w = show.Sim; var L = w.Layout; var mind = w.Get<Mind>(); var life = w.Get<Life>(); var so = w.Get<Society>(); var st = w.Get<Structure>();
        var people = mind.Agents.Where(a => a.IsPerson).ToList();
        var a = people.FirstOrDefault(x => x.Plan.Count > 1) ?? people[0];
        var other = people.First(x => x.Id != a.Id);
        int pid = a.Id;

        // ── D1 사람 자세히: 작은 탭 일곱의 줄 ──
        {
            var bad = new List<string>();
            // 차림: 데인 데 (원인) · 두려움 · 맡은 일 · 막힘 · 믿음 둘 (불 — 실제는 없다 / 안 열리는 문 — 실제로 끼었다) · 짝 · 앙금 · 기억
            var b = life.Bodies[a.C.Body];
            b.Burn = 0.3f; b.HurtCause = w.Causes.Add(CauseKind.Setup, "튄 불똥에 데었다", w.Tick, a.C.Cell);
            life.Bodies[a.C.Body] = b;
            a.C.Fear = 0.8f;
            a.C.AssignedGoal = GoalKind.Repair; a.C.AssignedUntil = w.Tick + Time.Minutes(20);
            a.C.Snag = SnagKind.DoorJammed; a.C.SnagTick = w.Tick; a.C.SnagGoal = a.C.Goal;
            int fireCell = L.Rooms.First(r => r.Type == "Mess").Cells[3];
            int door = L.Doors.FindIndex(d => d.Kind == "normal");
            st.DoorJam[door] = 1;
            a.Beliefs.Add(new Belief { Kind = BeliefKind.Fire, Src = BeliefSource.Saw, Cell = fireCell, Subject = -1, Conf = 0.9f, Seen = w.Tick });
            a.Beliefs.Add(new Belief { Kind = BeliefKind.StuckDoor, Src = BeliefSource.Heard, Cell = L.Doors[door].Cell, Subject = door, Conf = 0.6f, Seen = w.Tick });
            so.Partner[pid] = other.Id; so.Partner[other.Id] = pid;
            var third = people.First(x => x.Id != pid && x.Id != other.Id);
            so.Grudge[pid * so.N + third.Id] = 0.6f;
            so.Memories.Add(new Memory { Owner = pid, Tick = w.Tick, Topic = Topic.Rescue, About = other.Id, Cause = w.Causes.Add(CauseKind.Setup, "쓰러진 짝을 업고 나왔다", w.Tick), Weight = 0.7f, Cell = a.C.Cell });
            show.OpenDetail(DetailKind.Person, pid);
            bool tabs = show.Render() is var f0 && f0.DetailTabs.Length == 7 && f0.DetailTabs[^1] == "지나온 길"; show.Recycle(f0.Img);
            bool Has(PanelRow[] rows, string s) => rows.Any(r => r.Main.Contains(s) || r.Sub.Contains(s));
            void Lint(string tab, PanelRow[] rows) { foreach (var r in rows) foreach (var t in new[] { r.Main, r.Sub }.Where(t => t.Length > 0)) if (LookBad(t) is { } why) bad.Add($"{tab} '{t}' {why}"); }
            var body = DetailOf(show, 0);
            if (!Has(body, "핏속 산소") || !Has(body, "체온") || !Has(body, "튄 불똥에 데었다")) bad.Add("몸: " + string.Join(" | ", body.Select(r => r.Main)));
            var mindRows = DetailOf(show, 1);
            if (!mindRows.Any(r => r.Bars?.Any(x => x.Name == "두려움" && x.Value > 0.7f) == true) || !Has(mindRows, "속말") || !Has(mindRows, "마음 건강")) bad.Add("마음: " + string.Join(" | ", mindRows.Select(r => r.Main)));
            var doing = DetailOf(show, 2);
            bool step = a.Plan.Count == 0 || doing.Any(r => r.Main.StartsWith("▶"));
            if (!step || !Has(doing, "맡은 일") || !Has(doing, Mind.SnagText(SnagKind.DoorJammed))) bad.Add("하는 일: " + string.Join(" | ", doing.Select(r => r.Main)));
            var knows = DetailOf(show, 3);
            var fireRow = knows.FirstOrDefault(r => r.Main.StartsWith("불"));
            var doorRow = knows.FirstOrDefault(r => r.Main.StartsWith("안 열리는 문"));
            if (fireRow.Main == null || !fireRow.Sub.Contains("봤다") || !fireRow.Sub.Contains("확신 90%") || !fireRow.Sub.Contains("이미 아니다")) bad.Add($"아는 것 불: '{fireRow.Main}' '{fireRow.Sub}'");
            if (doorRow.Main == null || !doorRow.Sub.Contains("들었다") || !doorRow.Sub.Contains("맞다")) bad.Add($"아는 것 문: '{doorRow.Main}' '{doorRow.Sub}'");
            int who = knows.Count(r => r.Sub.Contains("주컴퓨터:"));
            var fireWho = knows.FirstOrDefault(r => r.Main.StartsWith("누가 어떻게 아나 — 불"));
            if (who < 9 || fireWho.Main == null || !fireWho.Sub.Contains("이 사람은 안다")) bad.Add($"누가 어떻게 아나 {who}줄 · 불 '{fireWho.Sub}'");
            if (!Has(knows, "분야 앎") || !Has(knows, "솜씨")) bad.Add("분야 앎 · 솜씨 없음");
            var near = DetailOf(show, 4);
            if (!Has(near, "짝 " + mind.NameOf(other.Id)) || !Has(near, "앙금") || !Has(near, "쓰러진 짝을 업고 나왔다")) bad.Add("사람 사이: " + string.Join(" | ", near.Select(r => r.Main + "/" + r.Sub)));
            var past = DetailOf(show, 6);
            if (past.Length == 0 || !past[0].Main.StartsWith("곧")) bad.Add("지나온 길 자리 없음");
            foreach (var (tab, rows) in new[] { ("몸", body), ("마음", mindRows), ("하는 일", doing), ("아는 것", knows), ("사람 사이", near), ("지나온 길", past) }) Lint(tab, rows);
            var sw = System.Diagnostics.Stopwatch.StartNew(); int built = 0;
            for (int k = 0; k < 7; k++) { show.SetDetailSub(k); for (int r = 0; r < 20; r++) built += show.PanelRows().Length; }
            double rowsUs = sw.Elapsed.TotalMilliseconds * 1000 / 140;
            st.DoorJam[door] = 0;
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var (tab, rows) in new[] { ("몸", body), ("마음", mindRows), ("하는 일", doing), ("아는 것", knows), ("사람 사이", near) }) Console.WriteLine($"    [{tab}]\n      " + string.Join("\n      ", rows.Select(r => r.Main + (r.Sub.Length > 0 ? " / " + r.Sub.Replace("\n", " ⏎ ") : "") + (r.Bars != null ? " [" + string.Join(" ", r.Bars.Select(x => $"{x.Name}{x.Value:0.0}")) + "]" : ""))));
            Check("자세한 판 D1 사람: 작은 탭 일곱 (지나온 길 자리) · 몸 (핏속 산소 · 체온 · 다친 원인) · 마음 (감정 막대 · 속말 · 마음 건강) · 하는 일 (계획 ▶ · 맡은 일 · 막힘) · 아는 것 (믿음 · 어떻게 · 확신 · 지금 실제 — 불은 이미 아니다 · 끼인 문은 맞다 / 누가 어떻게 아나 아홉 갈래 · 분야 앎 · 솜씨) · 사람 사이 (짝 · 앙금 · 기억) · 개발 말 0",
                tabs && bad.Count == 0, bad.Count > 0 ? string.Join(" / ", bad.Take(4)) : $"줄 짓기 탭마다 {rowsUs:0}µs (줄 {built / 140.0:0})");
        }

        // ── D4 판단 일지 32칸: 열 번 넘게 판단한 사람의 일지가 32까지 남는다 (고치기 전 8) ──
        {
            for (int i = 0; i < 40; i++) mind.NoteReleased(pid, w.Tick + i, GoalKind.Repair, a.C.Cell);
            var jr = a.Judged;
            var rows = DetailOf(show, 5);
            int judged = rows.Count(r => r.Key != -1 && r.Key >= LiveShow.KeyDetail(5, 0) && r.Key < LiveShow.KeyDetail(6, 0));
            Check("자세한 판 D4 판단 일지 32칸: 마흔 번 판단한 사람의 일지가 32칸까지 남고 (예전 여덟 칸 — 자세한 판에서 몇 분이면 밀렸다) · 일지 탭에 32줄 (새것 위)",
                jr != null && jr.Count == Mind.JudgedKeep && Mind.JudgedKeep == 32 && judged >= 32 && jr.Newest(0).Tick == w.Tick + 39, $"일지 {jr?.Count} · 탭 줄 {judged}");
        }
        show.DetailBack();

        // ── D2 로봇: 다리가 상한 로봇 — 다리 막대 · 원인 사슬 · 맡은 사건 · 막힘 까닭 · 주컴퓨터 말 기록의 그 로봇 줄 ──
        {
            var net = w.Get<Network>(); var cm = w.Get<ShipMind>();
            var r = mind.Agents.First(x => !x.IsPerson && x.C.Kind == AgentKind.Robot && x.C.Mech >= 0 && x.C.Mech < net.Robots.Count);
            int root = w.Causes.Add(CauseKind.Captain, "함장이 일으킨 밀어 던지기 — 공구 상자", w.Tick);
            int legCause = w.Causes.Add(CauseKind.Impact, "날아온 공구 상자에 로봇 다리가 꺾였다", w.Tick, r.C.Cell, root);
            var rb = net.Robots[r.C.Mech]; rb.Legs = 1; rb.LegsCause = legCause; net.Robots[r.C.Mech] = rb;
            var mess = L.Rooms.First(x => x.Type == "Mess");
            cm.Incidents.Add(new Incident { Id = 9001, Kind = IncidentKind.Fire, Active = 1, Room = mess.Id, Cell = mess.Cells[0], Subject = -1, Since = w.Tick, RobotA = r.Id, RobotB = -1, Person = -1, Decl1 = -1, Decl2 = -1, Snag = SnagKind.NoLegs, SnagGoal = GoalKind.FightFire, SnagCell = r.C.Cell });
            r.C.Snag = SnagKind.NoLegs; r.C.SnagTick = w.Tick; r.C.SnagCell = r.C.Cell;
            (cm.Notes ??= new Ring<ComputerNote>(512)).Add(new ComputerNote { Tick = w.Tick, Kind = NoteKind.Assign, Robot = r.Id, Incident = 9001, Text = $"{mind.NameOf(r.Id)} → 식당 불 (가장 가까운 로봇)" });
            show.OpenDetail(DetailKind.Robot, r.Id);
            var bad = new List<string>();
            bool Has(PanelRow[] rows, string t) => rows.Any(x => x.Main.Contains(t) || x.Sub.Contains(t));
            var body = DetailOf(show, 0);
            bool legBar = body.Any(x => x.Bars?.Any(b => b.Name == "다리" && b.Value < 0.01f && b.Level == 2) == true);
            if (!legBar || !Has(body, "걷지 못한다") || !Has(body, "날아온 공구 상자에 로봇 다리가 꺾였다") || !Has(body, "처음: 함장이 일으킨 밀어 던지기") || !Has(body, "보는 거리")) bad.Add("몸: " + string.Join(" | ", body.Select(x => x.Main + "/" + x.Sub)));
            var doing = DetailOf(show, 1);
            if (!Has(doing, "맡은 사건 — 식당 " + ShipMind.KindName(IncidentKind.Fire)) || !Has(doing, Mind.SnagText(SnagKind.NoLegs)) || !Has(doing, "맡은 까닭")) bad.Add("하는 일: " + string.Join(" | ", doing.Select(x => x.Main + "/" + x.Sub)));
            var knows = DetailOf(show, 2);
            if (knows.Length == 0) bad.Add("아는 것 비었다");
            var log = DetailOf(show, 3);
            if (!Has(log, "식당 불 (가장 가까운 로봇)")) bad.Add("일지: " + string.Join(" | ", log.Select(x => x.Main)));
            foreach (var (tab, rows) in new[] { ("몸", body), ("하는 일", doing), ("아는 것", knows), ("일지", log) })
                foreach (var x in rows) foreach (var t in new[] { x.Main, x.Sub }.Where(t => t.Length > 0)) if (LookBad(t) is { } why) bad.Add($"{tab} '{t}' {why}");
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var (tab, rows) in new[] { ("몸", body), ("하는 일", doing), ("아는 것", knows), ("일지", log) }) Console.WriteLine($"    [{tab}]\n      " + string.Join("\n      ", rows.Select(x => x.Main + (x.Sub.Length > 0 ? " / " + x.Sub.Replace("\n", " ⏎ ") : "") + (x.Bars != null ? " [" + string.Join(" ", x.Bars.Select(b => $"{b.Name}{b.Value:0.0}")) + "]" : ""))));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 4; k++) { show.SetDetailSub(k); for (int q = 0; q < 25; q++) show.PanelRows(); }
            double robotUs = sw.Elapsed.TotalMilliseconds * 1000 / 100;
            cm.Incidents.RemoveAt(cm.Incidents.Count - 1);
            Check("자세한 판 D2 로봇: 다리가 상한 로봇 — 다리 막대 (빨강) · '걷지 못한다' · 원인 사슬 (처음: 함장이 일으킨 …) · 보는 거리 · 맡은 사건 (방 · 갈래 · 막힘) · 막힘 까닭 · 맡은 까닭 · 일지에 주컴퓨터 말 기록의 그 로봇 줄 · 개발 말 0",
                bad.Count == 0, bad.Count > 0 ? string.Join(" / ", bad.Take(3)) : $"줄 짓기 탭마다 {robotUs:0}µs");
        }
    }
}
