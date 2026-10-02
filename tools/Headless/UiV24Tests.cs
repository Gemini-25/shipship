using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ShipSim.Core;

// v16.24 UI 전면 정리 — --uitest 에 붙는 줄들:
//  확대 3단계(그리는 요소가 단계마다 다르다 · 그림 코드가 단계로 가른다) · 표정 · 어두운 인형 대비
//  연대기 장(날 · 주) · 머리기사 · 일기 발췌(같은 사건을 여러 사람이) · 거르기 · 누르면 그 장면
//  사고 카드 여섯 칸(원인 · 전조 · 대응 · 쓴 방법 · 흔적 · 누가 기억하나) · 컴퓨터 제안을 자연스러운 때 정한다(과정이 남는다)
//  화면 글에 개발 용어 0 (정적 훑기) · 결정론 (화면이 읽어 가도 지문이 같다)
public static partial class Program
{
    private static void UiV24Checks(int seed)
    {
        Console.WriteLine("\n── v16.24 화면 정리 ──");
        ZoomChecks(seed);
        var w = FireScene(seed, out var inc, out var fireRoom);
        ChronicleChecks(w, inc, fireRoom);
        IncidentChecks(w, inc);
        ProposalChecks(seed);
        PlainWordsCheck();
        {
            uint H(bool read)
            {
                var x = World.CreateDefault(seed, 0, "Hanbit");
                for (int i = 0; i < 6; i++)
                {
                    Run(x, (SimTime.TicksPerDay + SimTime.Hours(6)) / 6);
                    if (!read) continue;
                    _ = ChronicleBook.Chapters(x, false, ChronFilter.All);
                    _ = ChronicleBook.Chapters(x, true, new ChronFilter(ChronFilterKind.Scale, MinScale: IncidentScale.Room));
                    foreach (var c in x.Crew) _ = ZoomDetail.Face(x, c);
                    foreach (var ci in x.Causes.Incidents.TakeLast(3)) _ = IncidentStory.Of(x, ci);
                }
                Run(x, (SimTime.TicksPerDay + SimTime.Hours(6)) % 6);
                return SaveGame.StateHash(x);
            }
            uint a = H(false), b = H(false), c = H(true);
            Check("v16.24 결정론 — 같은 시드 같은 지문 · 연대기 · 사고 카드 · 표정을 읽어 가도 같다", a == b && a == c, $"{a:x8} / {b:x8} / 읽으며 {c:x8}");
        }
    }

    // ─────────────────────────── 확대 3단계 ───────────────────────────

    private static void ZoomChecks(int seed)
    {
        var far = ZoomDetail.Draws(ZoomTier.Far);
        var mid = ZoomDetail.Draws(ZoomTier.Mid);
        var near = ZoomDetail.Draws(ZoomTier.Near);
        bool tiers = ZoomDetail.Of(0.3f) == ZoomTier.Far && ZoomDetail.Of(0.8f) == ZoomTier.Mid && ZoomDetail.Of(1.6f) == ZoomTier.Near
            && Puppet.Lod(0.3f) == 0 && Puppet.Lod(0.8f) == 1 && Puppet.Lod(1.6f) == 2;
        bool farOk = far.HasFlag(Detail.RoomTint) && far.HasFlag(Detail.RoomIcon) && far.HasFlag(Detail.CrewDot) && !far.HasFlag(Detail.Puppet) && !far.HasFlag(Detail.Face);
        bool midOk = mid.HasFlag(Detail.FixtureShape) && mid.HasFlag(Detail.Puppet) && mid.HasFlag(Detail.StatusIcon) && !mid.HasFlag(Detail.CrewDot)
            && !mid.HasFlag(Detail.Face) && !mid.HasFlag(Detail.GaugeDigits) && !mid.HasFlag(Detail.NameTag);
        bool nearOk = near.HasFlag(Detail.Face) && near.HasFlag(Detail.Held) && near.HasFlag(Detail.GaugeDigits) && near.HasFlag(Detail.Wear) && near.HasFlag(Detail.NameTag);
        Check("확대 3단계 — 멀리(방 색 · 방 아이콘 · 점) / 중간(설비 실루엣 · 인형 · 상태 아이콘) / 가까이(표정 · 든 것 · 계기 숫자 · 낡은 자국 · 이름표) — 단계마다 그리는 요소가 다르다",
            tiers && farOk && midOk && nearOk && far != mid && mid != near, $"멀리 {far} / 중간 {mid} / 가까이 {near}");

        // 그림 코드가 실제로 단계로 가른다 (화면은 Godot이 있어야 돌므로 글로 읽는다)
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(UiTestsPath())!, "..", ".."));
        string view = Path.Combine(root, "game", "src", "View");
        var code = string.Join("\n", Directory.GetFiles(view, "*.cs").OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText));
        var want = new[] { "Detail.RoomIcon", "Detail.CrewDot", "Detail.StatusIcon", "Detail.Face", "Detail.Held", "Detail.GaugeDigits", "Detail.Wear", "Detail.NameTag" };
        var missing = want.Where(k => !code.Contains(k)).ToList();
        Check("확대 3단계 — 그림 코드가 단계로 가른다 (방 아이콘 · 점 · 상태 아이콘 · 표정 · 든 것 · 계기 숫자 · 낡은 자국 · 이름표)", missing.Count == 0,
            missing.Count == 0 ? $"{want.Length}가지 모두" : "빠짐: " + string.Join(", ", missing));

        // 표정 · 대비
        var w = DayOne(seed, "Hanbit");
        var alive = w.Crew.Where(c => !c.Dead).ToList();
        var hurt = alive[0];
        hurt.Vitals.Injury = 0.6f;
        var faces = alive.Select(c => ZoomDetail.Face(w, c)).ToList();
        bool painOk = ZoomDetail.Face(w, hurt) == FaceLook.Pain && ZoomDetail.FaceWhy(w, hurt).Contains("다쳐");
        hurt.Vitals.Injury = 0f;
        float dark = ZoomDetail.Luma(0.10f, 0.07f, 0.05f), light = ZoomDetail.Luma(0.85f, 0.7f, 0.5f);
        bool rim = ZoomDetail.NeedsRim(dark) && !ZoomDetail.NeedsRim(light) && ZoomDetail.Lift(dark) > 0.1f && ZoomDetail.Lift(light) == 0f;
        Check("가까이 — 표정은 몸 · 감정에서 읽는다 (다치면 아픈 얼굴) · 어두운 머리 · 피부는 밝은 테두리 · 밝기 보정",
            painOk && rim && faces.Count > 0, $"표정 {string.Join(" ", faces.GroupBy(f => f).Select(g => $"{g.Key}{g.Count()}"))} · 어두운 밝기 {dark:0.00} 보정 {ZoomDetail.Lift(dark):0.00}");
    }

    // ─────────────────────────── 불 한 번 (연대기 · 사고 카드가 같이 쓴다) ───────────────────────────

    private static World FireScene(int seed, out CauseIncident? inc, out Room? room)
    {
        var w = DayOne(seed, "Hanbit");
        // 사람이 둘 이상 있는 방 (없으면 사람이 가장 많은 방)
        room = w.Ship.Rooms.Where(r => r.Kind != RoomType.Corridor && !r.Detached && r.Cells.Count >= 6)
            .OrderByDescending(r => w.Crew.Count(c => !c.Dead && c.Room == r)).ThenBy(r => r.Id).First();
        var target = room;
        var cell = target.Cells[target.Cells.Count / 2];
        int before = w.Causes.Incidents.Count;
        w.Fire.Ignite(cell, 0.6f);
        Run(w, SimTime.Minutes(2));
        inc = w.Causes.Incidents.Skip(before).FirstOrDefault(i => w.Causes.Node(i.Root).RoomId == target.Id) ?? w.Causes.Incidents.Skip(before).FirstOrDefault();
        var found = inc;
        RunUntil(w, () => found == null || !found.Open, SimTime.Hours(10), SimTime.Minutes(5));
        Run(w, SimTime.Hours(30)); // 사고 뒤 하루: 일기 · 기억 · 흔적
        return w;
    }

    // ─────────────────────────── 연대기 ───────────────────────────

    private static void ChronicleChecks(World w, CauseIncident? inc, Room? fireRoom)
    {
        var days = ChronicleBook.Chapters(w, false, ChronFilter.All);
        var weeks = ChronicleBook.Chapters(w, true, ChronFilter.All);
        int first = SimTime.Day(w.History.FoundedTick);
        int dayCount = w.Day - first + 1;
        bool everyDay = days.Count == dayCount && days.Select(c => c.FirstDay).SequenceEqual(Enumerable.Range(first, dayCount)) && days.All(c => c.Headline.Length > 0);
        bool weekOk = weeks.Count == (w.Day - 1) / 7 - (first - 1) / 7 + 1 && weeks.All(c => c.Week && c.Headline.Length > 0)
            && weeks.Sum(c => c.Marks.Count) == days.Sum(c => c.Marks.Count);
        Check("연대기 — 날마다 한 장 · 주마다 한 장 · 장마다 머리기사 (주 장의 사건 = 날 장들의 합)", everyDay && weekOk,
            $"날 {days.Count}/{dayCount} · 주 {weeks.Count} · 머리기사: {string.Join(" / ", days.Select(c => $"{c.Title} {c.Headline}"))}");

        // 불이 난 날의 장: 머리기사가 그 불 · 규모 색 · 컴퓨터 일지 · 서로 다른 사람의 일기(같은 사건) · 사진 자리
        var k = inc == null ? null : w.Scale.Cases.FirstOrDefault(x => x.Root == inc.Root);
        var ch = k == null ? null : days.FirstOrDefault(c => c.Start <= k.Start && k.Start < c.End);
        bool lead = ch?.Lead?.CaseId == k?.Id && k != null && ch!.Headline.Contains(k.Name);
        int about = ch?.Diaries.Where(d => d.About).Select(d => d.CrewId).Distinct().Count() ?? 0;
        bool photo = ch?.Photo is { } ph && ph.Tick == k!.Start && ph.At != null;
        Check("연대기 장 — 불이 난 날은 그 불이 머리기사 · 규모가 칠해지고 · 사진 자리(시각 · 자리)가 잡힌다",
            lead && ch!.Peak != null && photo, ch == null ? "장 없음" : $"{ch.Headline} · {ch.Deck} · 규모 {ch.Peak}");
        Check("연대기 장 — 같은 사건을 서로 다른 사람이 (일기 · 마음에 남은 것) 둘 이상 · 주 컴퓨터 항해 일지가 붙는다",
            about >= 2 && ch!.ShipLog.Count > 0,
            ch == null ? "장 없음" : $"일기 {string.Join(" / ", ch.Diaries.Select(d => $"{d.Name}({d.Role}): {d.Text}"))} · 일지 {string.Join(" / ", ch.ShipLog.Select(l => l.Text))}");

        // 거르기: 사람 · 방 · 규모 · 물건 · 기술 — 걸러진 장의 사건은 모두 맞고, 하나도 안 맞는 장은 빠진다
        var person = k?.Workers.FirstOrDefault(-1) ?? -1;
        if (person < 0 && k != null) person = k.Mustered.Concat(k.Feared).FirstOrDefault(-1);
        var fp = new ChronFilter(ChronFilterKind.Person, person);
        var fr = new ChronFilter(ChronFilterKind.Room, fireRoom?.Id ?? -1);
        var fs = new ChronFilter(ChronFilterKind.Scale, MinScale: IncidentScale.Room);
        var byP = ChronicleBook.Chapters(w, false, fp);
        var byR = ChronicleBook.Chapters(w, false, fr);
        var byS = ChronicleBook.Chapters(w, false, fs);
        bool pOk = person >= 0 && byP.Count > 0 && byP.All(c => c.Marks.All(m => m.Crew.Contains(person)) && c.Diaries.All(d => d.CrewId == person));
        bool rOk = byR.Count > 0 && byR.All(c => c.Marks.All(m => m.RoomId == fireRoom!.Id)) && byR.Sum(c => c.Marks.Count) < days.Sum(c => c.Marks.Count);
        bool sOk = byS.All(c => c.Marks.All(m => m.Scale >= IncidentScale.Room));
        var choices = new[] { ChronFilterKind.Person, ChronFilterKind.Room, ChronFilterKind.Scale }.Select(kd => ChronicleBook.Choices(w, kd).Count).ToArray();
        Check("연대기 거르기 — 사람 · 방 · 규모로 거르면 맞는 사건 · 그 사람 일기만 · 고를 거리가 나온다",
            pOk && rOk && sOk && choices.All(n => n > 0),
            $"사람 {person}: {byP.Count}장 · 방 {fireRoom?.Name}: {byR.Count}장 {byR.Sum(c => c.Marks.Count)}건 · ② 이상 {byS.Sum(c => c.Marks.Count)}건 · 고를 거리 {string.Join("/", choices)}");

        // 누르면 그 장면으로: 사건 점은 시각과 자리를 갖는다
        var marks = ChronicleBook.Timeline(w, 0, long.MaxValue, ChronFilter.All).Where(m => m.IsCase).ToList();
        Check("연대기 타임라인 — 사건 점마다 시각 · 자리(누르면 그 장면) · 규모 색", marks.Count > 0 && marks.All(m => m.Scale != null && m.At != null),
            $"사건 {marks.Count} · 자리 없음 {marks.Count(m => m.At == null)}");
    }

    // ─────────────────────────── 사고 카드 ───────────────────────────

    private static void IncidentChecks(World w, CauseIncident? inc)
    {
        if (inc == null) { Check("사고 카드 — 불을 낸다", false, "사고가 잡히지 않았다"); return; }
        var s = IncidentStory.Of(w, inc);
        bool six = IncidentStory.Fields.Length == 6 && s.Lines.All(l => l.Count > 0) && IncidentStory.FieldIcons.Distinct().Count() == 6;
        Check("사고 카드 — 원인 · 전조 · 대응 · 쓴 방법 · 흔적 · 누가 기억하나 여섯 칸 (없으면 없다고 적는다)", six,
            string.Join(" | ", IncidentStory.Fields.Select((f, i) => $"{f}{(s.Found[i] ? "" : "(없음)")}: {s.Lines[i][0]}")));
        Check("사고 카드 — 불은 실제 기록에서 원인 · 대응 · 쓴 방법 · 기억을 찾는다 (겪은 사람이 떠올린다)",
            s.Found[0] && s.Found[2] && s.Found[3] && s.Found[5],
            $"대응 {string.Join(" / ", s.Response)} · 방법 {string.Join(" / ", s.Method)} · 기억 {string.Join(" / ", s.Memory)}");
    }

    // ─────────────────────────── 컴퓨터 제안 ───────────────────────────

    private static void ProposalChecks(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var a = w.Automation;
        var room = w.Ship.Rooms.First(r => r.Kind == RoomType.Storage || r.Kind == RoomType.Cargo);
        // ① 급한 제안 (진공): 기한 전에 바로 정한다
        var boss = ProposalTiming.Boss(w);
        var pu = a.Asks.Propose("ui24:urgent", "vacuum", room, "창고 공기 빼기", "불길이 번진다", "불이 꺼진다", 20f, 0);
        Run(w, SimTime.Minutes(1));
        bool urgent = boss != null && pu.State != ProposalState.Pending && pu.DecidedAt < pu.Deadline && pu.Trail.Any(t => t.Text.Contains("급"));
        // ② 함장이 단말 앞에 있으면: 본 때를 적고 몇 분 뒤 정한다 (기한보다 먼저)
        boss = ProposalTiming.Boss(w);
        bool atDesk = boss != null && RunUntil(w, () => ProposalTiming.AtTerminal(boss) && w.Meetings.Session == null && !w.Scale.OpenCases.Any(k => k.Big), SimTime.Hours(20), SimTime.Minutes(5));
        var pt = a.Asks.Propose("ui24:desk", "vent", room, "창고 환기 줄이기", "먼지가 많다", "필터가 덜 막힌다", 60f, 0);
        Run(w, SimTime.Minutes(ProposalTiming.ThinkMinutes + 2f));
        bool desk = atDesk && pt.State != ProposalState.Pending && pt.DecidedAt < pt.Deadline && pt.SeenAt >= 0
            && pt.Trail.Any(t => t.Text.Contains("단말에서 제안을 봤다")) && pt.DecidedAt - pt.SeenAt >= SimTime.Minutes(ProposalTiming.ThinkMinutes);
        // ③ 함장이 자고 있으면 (회의도 없고 급하지 않으면): 기한까지 기다렸다가 정한다
        boss = ProposalTiming.Boss(w);
        bool asleep = boss != null && RunUntil(w, () => boss.Pose == Pose.Sleeping && w.Meetings.Session == null, SimTime.Hours(24), SimTime.Minutes(5));
        var pw = a.Asks.Propose("ui24:wait", "vent", room, "창고 조명 낮추기", "아무도 없다", "전기를 아낀다", 30f, 0);
        Run(w, SimTime.Minutes(20));
        bool waiting = pw.State == ProposalState.Pending;
        Run(w, SimTime.Minutes(12));
        bool later = pw.State != ProposalState.Pending && pw.Trail.Any(t => t.Text.Contains("기한"));
        Check("컴퓨터 제안 — 급하면 바로 · 함장이 단말 앞이면 보고 몇 분 뒤 · 자는 동안엔 기한까지 기다린다 (과정이 남는다)",
            urgent && desk && asleep && waiting && later,
            $"급함 {urgent}({string.Join(" → ", pu.Trail.Select(t => t.Text))}) · 단말 {atDesk}/{desk}({string.Join(" → ", pt.Trail.Select(t => t.Text))}) · 잠 {asleep} 기다림 {waiting} 기한 뒤 {later}");
    }

    // ─────────────────────────── 화면 글 ───────────────────────────

    private static readonly (string Name, Regex Re)[] UiTerms =
    {
        ("HUD", new Regex("(?<![A-Za-z])HUD(?![A-Za-z])")),
        ("UI", new Regex("(?<![A-Za-z])UI(?![A-Za-z])")),
        ("기능 추가", new Regex("기능 ?추가|새 기능|신규 기능")),
        ("툴팁", new Regex("툴팁")),
        ("점수", new Regex("점수(?!판)")),
        ("괄호 조사", new Regex("이\\(가\\)|을\\(를\\)|은\\(는\\)|와\\(과\\)|\\(으\\)로")),
    };

    private static void PlainWordsCheck()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(UiTestsPath())!, "..", ".."));
        var (hits, ex, files) = AuditScanSource(root);
        // 화면 · 기록 글 리터럴에서 더 찾는 말
        int more = 0;
        var exMore = new List<string>();
        foreach (var dir in new[] { "game/src/Core", "game/src/View" })
            foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.cs").OrderBy(f => f, StringComparer.Ordinal))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*") || !AuditHangul.IsMatch(lines[i])) continue;
                    int cs = CommentStart(lines[i]);
                    foreach (Match m in AuditLiteral.Matches(lines[i]))
                    {
                        if (cs >= 0 && cs < m.Index) break;
                        if (!AuditHangul.IsMatch(m.Value)) continue;
                        foreach (var (name, re) in UiTerms)
                            if (re.IsMatch(m.Value)) { more++; if (exMore.Count < 6) exMore.Add($"[{name}] {Path.GetFileName(file)}:{i + 1}"); }
                    }
                }
            }
        int total = hits.Values.Sum();
        Check("화면 글 — 개발 · 기획 용어 0 (모듈 · AI · 트리아지 · 페일세이프 · 이중화 · 버전 번호 · HUD · UI · 점수 · 괄호 조사 …) 정적 훑기",
            total == 0 && more == 0 && files > 100, $"파일 {files} · 용어 {total} {string.Join(" ", ex.Take(4))} · 더 {more} {string.Join(" ", exMore)}");
    }
}
