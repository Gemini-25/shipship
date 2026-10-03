using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v18.7 실수 숨기기 · 블랙박스 · 사고 조사
public static partial class Program
{
    private sealed class BbScene
    {
        public World W = null!;
        public Slip S = null!;
        public CrewMember C = null!;
        public CrewMember Goat = null!;
        public InquiryCase? Case;
        public Finding? F;
        public Sitting? Sit;
        public float Before, After;
        public float Loss => Before - After;
        public string Script => Sit == null ? "자리 없음" : string.Join(" / ", Sit.Script.Select(l => $"{(l.Who < 0 ? "주 컴퓨터" : W.Crew.First(c => c.Id == l.Who).Name)}: {l.Text}"));
    }

    private static float Standing(World w, CrewMember c)
    {
        var o = w.Crew.Where(x => x != c && !x.Dead && !x.IsChild).ToList();
        return o.Count == 0 ? 0f : o.Average(x => x.AffinityTo(c) + w.Relations.Trust(x, c));
    }

    /// <summary>장면: 정비하던 사람이 실수 → 몇 분 뒤 사고 → (정한 방법으로) 숨기거나 털어놓음 → 사고 조사 회의 → 결과.</summary>
    private static BbScene BbRun(int seed, CoverWay way, SlipKind kind, bool wreck, bool natural = false, bool hear = true)
    {
        var sc = new BbScene();
        var w = sc.W = DayOne(seed, "Hanbit");
        w.Motions.Quiet = true;
        int cap = w.Command.CaptainId;
        var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct && c.Profiled).OrderBy(c => c.Id).ToList();
        var c = adults.Where(x => x.Id != cap).OrderByDescending(x => way == CoverWay.Wipe && w.Blackbox.CanWipe(x) ? 2 : 0)
            .ThenByDescending(x => x.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician ? 1 : 0).ThenBy(x => x.Id).First();
        sc.C = c;
        var m = w.Ship.Machines.Where(x => x.Faults.Count == 0 && x.Body.Room.Id != w.Blackbox.RoomId && x.Body.UseSpots.Count > 0 && x.Body.Room.Kind is not (RoomType.Reactor or RoomType.LifeSupport))
            .OrderBy(x => x.Body.Id).First();
        var goat = adults.Where(x => x != c && x.Id != cap && x.Room != m.Body.Room).OrderBy(x => x.Id).First();
        sc.Goat = goat;
        c.ChangeAffinity(goat, -0.9f);
        sc.Before = Standing(w, c);
        w.Blackbox.Note(BoxKind.Work, m.Body.Room, c, m.Body.Id, (byte)(kind == SlipKind.SkippedStep ? 1 : 0));
        var s = sc.S = w.Inquiry.Make(c, m, kind, "서두르다가", dueHours: 0.02f, force: way);
        s.Saw.Clear(); // 아무도 못 봤다 — 남는 건 블랙박스 · 흔적 · 말뿐
        Run(w, SimTime.Minutes(4));
        if (natural && way is CoverWay.Tidy or CoverWay.Wipe)
        {
            for (int i = 0; i < 48 && !s.Ways.Contains(way); i++) Run(w, SimTime.Minutes(15));
            if (!s.Ways.Contains(way)) w.Inquiry.Finish(c);
        }
        else if (way is CoverWay.Tidy or CoverWay.Wipe) w.Inquiry.Finish(c);
        Run(w, SimTime.Hours(2)); // 주 컴퓨터가 상자를 읽는다 (귀띔 · 빈 구간)
        if (!hear || !s.Bit) return sc;
        var inc = w.Causes.IncidentOf(s.Node)!;
        if (wreck) w.Blackbox.Wreck("불에 기록 칩이 탔다");
        var cs = sc.Case = w.Inquiry.OpenCase(inc, s);
        var mo = w.Motions.Get(cs.Motion);
        if (mo == null) return sc;
        Sitting? sit = null;
        for (int i = 0; i < 30 && sit == null; i++)
        {
            sit = w.Motions.Now?.Motion == mo ? w.Motions.Now : w.Motions.Now == null ? w.Motions.Convene(mo) : null;
            if (sit == null) Run(w, SimTime.Hours(1));
        }
        if (sit == null) return sc;
        sc.Sit = sit;
        if (sit.Opened < 0)
        {
            sit.Invited.Add(c.Id); sit.Invited.Add(goat.Id);
            foreach (var id in sit.Invited) sit.Present.Add(id);
            w.Motions.OpenSitting(sit);
        }
        for (int i = 0; i < 40 && w.Motions.Now == sit; i++) Run(w, SimTime.Minutes(10));
        sc.F = cs.Finding;
        sc.After = Standing(w, c);
        return sc;
    }

    private static int RunBlackboxTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"실수 숨기기 · 블랙박스 · 사고 조사 점검 (v18.7) · 시드 {seed}\n");
        string only = Environment.GetEnvironmentVariable("SHIPSIM_SCENE") ?? "";
        bool Do(string k) => only == "" || only.Contains(k);
        BbScene? hide = null, conf = null, blame = null;

        // ── 1) 숨긴 실수가 블랙박스 조사로 드러나고, 숨긴 사람과의 관계가 바뀐다
        if (Do("1"))
        {
            var sc = hide = BbRun(seed, CoverWay.Lie, SlipKind.SkippedStep, wreck: false);
            var w = sc.W; var s = sc.S; var f = sc.F;
            Console.WriteLine($"   [숨김] {sc.C.Name} · {s.MachineName} · {sc.Script}");
            Check("블랙박스 — 설치되어 1분마다 받아 적는다 (위치 · 문 · 밸브 · 전력 · 명령 · 정비 · 경보)", w.Blackbox.Present && w.Blackbox.Stats.Entries > 100 && w.Blackbox.Log.Any(e => e.Kind == BoxKind.Place) && w.Blackbox.Log.Any(e => e.Kind == BoxKind.Work),
                $"{w.Blackbox.RoomName(w.Blackbox.RoomId)} · 줄 {w.Blackbox.Stats.Entries} · 종류 {string.Join(",", w.Blackbox.Log.Select(e => e.Kind).Distinct())}");
            var root = s.Node >= 0 ? w.Causes.Node(s.Node) : null;
            Check("실수가 몇 분 뒤 사고가 되고, 첫 고리에는 본 것만 적힌다 (이름이 없다)", s.Bit && root != null && !root.Text.Contains(sc.C.Name), root?.Text ?? "사고 없음");
            Check("숨긴다 — 아무에게도 말하지 않고 일기에만", s.Hidden && s.Ways.Contains(CoverWay.Lie) && sc.C.Diary.Any(d => d.text.Contains("아무에게도 말하지 않았다")), sc.C.Diary.FirstOrDefault(d => d.text.Contains("아무에게도")).text);
            Check("주 컴퓨터 — 블랙박스를 읽어 마지막 정비 기록을 짚고 먼저 말하라고 권한다", s.Nudged && w.Inquiry.Stats.Nudges > 0,
                w.Automation.Book.Acts.LastOrDefault(a => a.Key.StartsWith("slip:nudge"))?.Observe ?? "로그에만");
            Check("사고 조사 — 큰 사고 뒤 안건이 올라오고 서명이 바로 모여 자리가 열린다", sc.Sit != null && sc.Sit.Opened >= 0 && sc.Case?.Heard == true, sc.Sit != null ? $"{sc.Sit.Venue.Name} · {sc.Sit.Present.Count}명 · 줄 {sc.Sit.Script.Count}" : "자리 없음");
            Check("주 컴퓨터가 블랙박스 기록을 근거로 낸다 (표는 없다)", sc.Sit?.Script.Any(l => l.Role == LineRole.Computer && l.Text.Contains("블랙박스") && l.Text.Contains(sc.C.Name)) == true && sc.Sit?.Voters.Count >= 2,
                sc.Sit?.Script.FirstOrDefault(l => l.Role == LineRole.Computer).Text ?? "");
            Check("숨긴 실수가 블랙박스 조사로 드러난다", f != null && f.Right && f.Blamed == sc.C.Id && s.Revealed && f.Basis.Contains("블랙박스 단말 기록"), f?.Text ?? "결론 없음");
            int lied = w.Relations.All.Count(r => r.About == sc.C.Id && r.Reason == RelationReason.LiedToUs);
            Check("숨긴 사람과의 관계가 바뀐다 (호감 · 믿음이 깎이고 '숨겼다'는 기억이 남는다)", sc.Loss > 0.05f && lied >= 2, $"평판 {sc.Before:+0.00;-0.00} → {sc.After:+0.00;-0.00} · 기억 {lied}명 · 말의 신용 {w.Info.Cred(sc.C):0.00}");
            var inc = w.Causes.IncidentOf(s.Node);
            var card = inc != null ? IncidentStory.Of(w, inc) : null;
            Check("사고 카드 '원인' 칸이 조사 결과로 고쳐진다", card?.Cause.Any(l => l.StartsWith("조사 결과") && l.Contains(sc.C.Name)) == true, card != null ? string.Join(" / ", card.Cause) : "");
            Check("연대기 · 재발 방지 규칙이 남는다", w.History.Events.Any(e => e.Kind == HistoryKind.Decision && e.Text.StartsWith("사고 조사")) && w.Inquiry.Ruled(SlipKind.SkippedStep)
                && w.History.Events.Any(e => e.Kind == HistoryKind.Lesson && e.Text.StartsWith("재발 방지")),
                w.History.Events.LastOrDefault(e => e.Kind == HistoryKind.Lesson)?.Text ?? "");
            Check("벌은 표결로 (용서도 표결)", f?.Verdict != null && sc.Sit?.Script.Any(l => l.Role == LineRole.Speech) == true, f?.Verdict is Penalty pv ? MotionSystem.PenaltyName(pv) : "표결 없음");
            Check("일기에 서로 다르게 남는다 (당사자 · 의장)", sc.C.Diary.Any(d => d.text.Contains("조사") || d.text.Contains("블랙박스")) && w.Crew.Any(o => o != sc.C && o.Diary.Any(d => d.text.Contains("블랙박스를 열어"))),
                sc.C.Diary.LastOrDefault().text ?? "");
        }

        // ── 2) 털어놓은 사람은 덜 잃는다
        if (Do("2"))
        {
            var sc = conf = BbRun(seed, CoverWay.Confess, SlipKind.SkippedStep, wreck: false);
            Console.WriteLine($"   [털어놓음] {sc.C.Name} · {sc.Script}");
            Check("털어놓는다 — 바로 말하고 다시 손본다", sc.S.Confessed && !sc.S.Hidden, sc.W.Inquiry.FindingFor(sc.Case?.Root ?? -1) ?? "");
            if (hide != null) Check("털어놓은 사람은 덜 잃는다", sc.Loss < hide.Loss * 0.5f, $"털어놓음 {sc.Loss:0.000} · 숨김 {hide.Loss:0.000}");
            Check("조사 결론에도 '털어놓았다'로 남는다", sc.F?.Right == true && sc.F.Text.Contains("털어놓았다"), sc.F?.Text ?? "");
        }

        // ── 3) 남 탓을 했다가 들키면 더 크게 잃는다
        if (Do("3"))
        {
            var sc = blame = BbRun(seed, CoverWay.Blame, SlipKind.SkippedStep, wreck: false);
            Console.WriteLine($"   [남 탓] {sc.C.Name} → {sc.Goat.Name} · {sc.Script}");
            Check("남 탓 — 같은 설비를 만졌거나 미운 사람의 이름을 댄다", sc.S.Scapegoat == sc.Goat.Id && sc.Goat.Diary.Any(d => d.text.Contains("내 탓")), $"탓 {sc.W.Crew.FirstOrDefault(x => x.Id == sc.S.Scapegoat)?.Name}");
            Check("탓을 들은 사람은 위치 기록으로 변론한다 (그 시각 그 방에 없었다)", sc.Sit?.Script.Any(l => l.Who == sc.Goat.Id && l.Role == LineRole.Defense && l.Text.Contains("위치 기록")) == true, sc.Script);
            Check("남 탓이 들킨다", sc.F?.Right == true && sc.F.Framed && sc.F.Blamed == sc.C.Id, sc.F?.Text ?? "");
            if (hide != null) Check("남 탓을 했다가 들키면 더 크게 잃는다", sc.Loss > hide.Loss * 1.2f, $"남 탓 {sc.Loss:0.000} · 숨김 {hide.Loss:0.000}");
            Check("뒤집어씌운 일은 당한 사람이 기억한다", sc.W.Relations.All.Any(r => r.Who == sc.Goat.Id && r.About == sc.C.Id && r.Reason == RelationReason.FramedMe), $"{sc.Goat.Name} → {sc.C.Name} 호감 {sc.Goat.AffinityTo(sc.C):+0.00;-0.00}");
        }

        // ── 4) 블랙박스가 부서지면 증언에만 의존한다 (틀린 결론 가능)
        if (Do("4"))
        {
            var sc = BbRun(seed, CoverWay.Blame, SlipKind.SkippedStep, wreck: true);
            Console.WriteLine($"   [상자 탐] {sc.C.Name} → {sc.Goat.Name} · {sc.Script}");
            Check("상자가 타면 주 컴퓨터도 기록을 낼 수 없다", sc.Sit?.Script.Any(l => l.Text.Contains("읽을 수 없습니다")) == true && sc.F?.BoxRead == false, sc.Sit?.Script.FirstOrDefault(l => l.Text.Contains("블랙박스")).Text ?? "");
            Check("말뿐인 조사는 틀린 결론에 이르기도 한다 (탓을 들은 사람이 쓴다)", sc.F != null && !sc.F.Right && sc.F.Blamed == sc.Goat.Id, sc.F?.Text ?? "");
            var inc = sc.W.Causes.IncidentOf(sc.S.Node);
            var card = inc != null ? IncidentStory.Of(sc.W, inc) : null;
            Check("틀린 결론도 카드 '원인' 칸에 그대로 적힌다 (사람들이 아는 것 ≠ 일어난 일)", card?.Cause.Any(l => l.Contains(sc.Goat.Name)) == true && sc.S.Guilt > 0.6f, card != null ? string.Join(" / ", card.Cause) : "");
            Check("상자는 망가진 뒤 반나절 지나 전기 솜씨 있는 사람이 칩을 갈아 끼운다", BbUntil(sc.W, () => !sc.W.Blackbox.Wrecked, SimTime.Hours(20)) && sc.W.Blackbox.Repairer >= 0, $"갈아 끼운 사람 {sc.W.Crew.FirstOrDefault(c => c.Id == sc.W.Blackbox.Repairer)?.Name}");
        }

        // ── 5) 기록을 지운 자리가 빈 구간으로 남아 단서가 된다
        if (Do("5"))
        {
            var sc = BbRun(seed, CoverWay.Wipe, SlipKind.SkippedStep, wreck: false, natural: true);
            var w = sc.W;
            var g = w.Blackbox.Wipes.FirstOrDefault();
            Console.WriteLine($"   [지움] {sc.C.Name} ({w.Blackbox.AccessOf(sc.C)}) · {sc.Script}");
            Check("단말에 가서 그 구간 기록을 지운다 (권한 · 솜씨)", g != null && g.Wiper == sc.C.Id && g.Removed > 0 && !w.Blackbox.Log.Any(e => e.Kind == BoxKind.Work && e.Who == sc.C.Id && e.Tick >= g.From && e.Tick <= g.To),
                g != null ? $"{w.Blackbox.GapLine(g)} · {g.Removed}줄" : "지우지 않음");
            Check("주 컴퓨터가 빈 구간을 찾아낸다 (지운 시각 · 권한 · 단말)", w.Inquiry.Stats.GapsSeen > 0 && g?.Noticed >= 0, w.Automation.Book.Acts.LastOrDefault(a => a.Key.StartsWith("box:gap"))?.Observe ?? "");
            Check("빈 구간이 단서가 된다 — 지운 시각 단말 방 위치 기록이 지운 사람을 가리킨다", sc.Sit?.Script.Any(l => l.Text.Contains("비어 있다") && l.Text.Contains(sc.C.Name)) == true && sc.F?.Blamed == sc.C.Id && sc.F.GapSeen,
                sc.F?.Text ?? "");
        }

        // ── 6) 흔적 치우기 (사고 자리에 혼자 간다)
        if (Do("6"))
        {
            var sc = BbRun(seed, CoverWay.Tidy, SlipKind.ToolLeft, wreck: false, natural: true);
            Console.WriteLine($"   [치움] {sc.C.Name} · {sc.Script}");
            Check("흔적을 치운다 — 사고 자리에 혼자 가서 렌치를 꺼낸다", sc.S.Ways.Contains(CoverWay.Tidy) && !sc.S.TraceLeft, string.Join(" | ", sc.C.Diary.TakeLast(2).Select(d => d.text)));
            Check("조사에서 '사고 자리가 이상하게 깨끗했다'", sc.Sit?.Script.Any(l => l.Text.Contains("깨끗했다")) == true, sc.Script);
        }

        // ── 7) 죄책감 — 잠 · 일기 · 말수 → 스스로 털어놓으러 간다
        if (Do("7"))
        {
            var sc = BbRun(seed, CoverWay.Lie, SlipKind.WrongValve, wreck: false, hear: false);
            var w = sc.W; var s = sc.S;
            s.Guilt = 1.2f;
            MotionSystem.Off = true; // 조사 회의 없이 — 스스로
            bool went = false;
            BbUntil(w, () => { went |= w.Inquiry.TaskOf(sc.C)?.Kind == CoverTaskKind.Confess && sc.C.Job?.Activity is CoverActivity; return s.Confessed; }, SimTime.TicksPerDay * 3);
            Check("죄책감이 꿈 · 일기 · 말수로 드러난다", w.Inquiry.Stats.Dreams > 0 && w.After.Minds.TryGetValue(sc.C.Id, out var am) && am.Seen.Any(x => x.Key == $"slip:{s.Id}"),
                $"꿈거리 {w.Inquiry.Stats.Dreams} · {sc.C.Diary.LastOrDefault(d => d.text.Contains(s.MachineName)).text}");
            MotionSystem.Off = false;
            Check("무거워진 사람은 찾아가서 털어놓는다", s.Confessed && went && w.Inquiry.Stats.LateConfessed > 0, $"{(went ? "걸어가서" : "그 자리에서")} · {sc.C.Diary.LastOrDefault().text}");
        }

        // ── 9) 저절로 — 며칠 사이 정비 뒤 실수가 생기고 (대개 스스로 되돌린다) · 상자가 컴퓨터의 틀어진 기억을 바로잡는다
        if (Do("9"))
        {
            var w = World.CreateDefault(seed, 0, "Cheonma");
            Run(w, SimTime.TicksPerDay);
            var mate = w.Automation.Mate;
            var target = w.Ship.Machines.Where(m => m.LastServiced > 0).OrderBy(m => m.Body.Id).FirstOrDefault() ?? w.Ship.Machines.First();
            var e = mate.CorruptMemory(target);
            for (int i = 0; i < 12; i++)
            {
                // 고된 사흘: 잠이 모자란 사람이 정비를 한다 (졸음은 실수를 부른다)
                foreach (var c in w.Crew) if (!c.Dead && !c.IsChild) c.Needs.Rest = MathF.Min(c.Needs.Rest, 0.2f);
                Run(w, SimTime.Hours(6));
            }
            var st = w.Inquiry.Stats;
            Console.WriteLine($"   사흘: 실수 {st.Slips} · 스스로 되돌림 {st.SelfFixed} · 사고 {st.Bites} (불 {st.Fires}) · 숨김 {st.Hidden} · 털어놓음 {st.Confessed}+{st.LateConfessed} · 치움 {st.Tidied} · 남 탓 {st.Blames} · 지움 {st.Wipes} · 조사 {st.Cases}/{st.Hearings} · 드러남 {st.Revealed} · 틀린 결론 {st.Wrong} · 규칙 {st.Rules} · 상자 줄 {w.Blackbox.Stats.Entries} · 정비 기록 {w.Blackbox.Log.Count(x => x.Kind == BoxKind.Work)} · 실수(전체) {w.Life.Stats.Mistakes}");
            Check("저절로 — 정비 뒤 실수가 생긴다 (스스로 되돌리거나 사고가 된다)", st.Slips >= 1, $"실수 {st.Slips}");
            Check("블랙박스 ↔ 컴퓨터 기억 — 틀어진 정비 기억이 바로잡힌다", e == null || !e.Corrupt, e != null ? $"{e.Label} · 상자로 고침 {st.MemoryFixes} · 사람이 짚음 {mate.Corrections}" : "기억 칸 없음");
        }

        // ── 8) 결정론 · 성능
        if (Do("8"))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            if (Environment.GetEnvironmentVariable("SHIPSIM_NOPERF") != "1")
            {
                InquiryStats? st = null;
                double Day(bool off)
                {
                    BlackboxSystem.Off = InquirySystem.Off = off;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    Run(w, SimTime.Hours(2));
                    BlackboxSystem.UpdateTicks = InquirySystem.UpdateTicks = 0;
                    var sw = Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    BlackboxSystem.Off = InquirySystem.Off = false;
                    if (!off) st = w.Inquiry.Stats;
                    return sw.Elapsed.TotalSeconds;
                }
                double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
                double own = (BlackboxSystem.UpdateTicks + InquirySystem.UpdateTicks) * 1.0 / Stopwatch.Frequency;
                Console.WriteLine($"   성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%) · 이 시스템 틱 {own * 1000:0}ms · 실수 {st?.Slips} 스스로 고침 {st?.SelfFixed} 사고 {st?.Bites}");
                Check("성능 · 30인 배 하루 ±10%", t1 < t0 * 1.10, $"{t0:0.0} → {t1:0.0}초");
            }
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}건");
        return _fails == 0 ? 0 : 1;
    }

    private static bool BbUntil(World w, Func<bool> done, long maxTicks, int step = 60)
    {
        for (long t = 0; t < maxTicks; t += step)
        {
            if (done()) return true;
            Run(w, step);
        }
        return done();
    }
}
