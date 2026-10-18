using ShipEngine;
using ShipView;
using ShipSim.Art;

namespace Lab;

/// <summary>새 화면 규칙 시험 — 그리기는 세계를 읽기만 하고, 세계의 상태가 그림에 드러난다.</summary>
public static partial class Program
{
    private static void ViewRules()
    {
        if (!Do("화면")) return;
        Console.WriteLine("\n[화면]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var s = Worlds.Full(lay, 3);
        var m = s.Get<Mind>();
        m.AddPerson(s, At(15, 2)); m.AddRobot(s, At(20, 7), AgentKind.Robot, At(20, 7));
        s.Run(10);
        s.Get<Chemistry>().Ignite(At(3, 5), 3e6, s.Causes.Add(CauseKind.Story, "침대 불씨", s.Tick));
        var f = s.Get<Field>();
        foreach (int c in lay.Rooms[lay.Room[At(17, 2)]].Cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * 0.95);
        s.Run(20);
        var before = s.Save();
        var p = new Painter(lay);
        var img = p.Frame(s, 0, 0, lay.W, lay.H, (int)s.Tick);
        bool same = before.AsSpan().SequenceEqual(s.Save());
        // 불 칸 둘레에 불꽃 색 (붉고 밝은 도트) · 빠진 방은 서리 빛(파랗게)
        int fire = 0;
        var chem = s.Get<Chemistry>();
        int burning = Enumerable.Range(0, lay.N).FirstOrDefault(c => chem.Burning[c] != 0, -1);
        int fx = (burning >= 0 ? lay.X(burning) : 0) * Painter.T, fy = (burning >= 0 ? lay.Y(burning) : 0) * Painter.T;
        for (int y = fy - 10; y < fy + Painter.T; y++)
            for (int x = fx; x < fx + Painter.T; x++)
            {
                var c = img.Get(x, y);
                if (c.R > 200 && c.G > 80 && c.B < 90) fire++;
            }
        // 같은 바닥(보통 방)끼리 견준다: 빠진 오른쪽 방 · 멀쩡한 아래 통로 — 칸 안쪽의 평균 파란 값
        double Blue(int cell) { double b = 0; int n = 0; for (int y = 6; y < 26; y++) for (int x = 6; x < 26; x++) { b += img.Get(lay.X(cell) * Painter.T + x, lay.Y(cell) * Painter.T + y).B; n++; } return b / n; }
        double vac = Blue(At(19, 4)), ok = Blue(At(19, 8));
        if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
        {
            var ch = s.Get<Chemistry>();
            for (int c = 0; c < lay.N; c++) if (ch.Burning[c] != 0) Console.WriteLine($"    불 ({lay.X(c)},{lay.Y(c)}) {ch.Hrr[c]:0}kW");
            Png.WriteRgba("build/shots/rule-view.png", img.W, img.H, img.Px);
        }
        Check("새 화면: 그리기는 세계를 바꾸지 않는다 (그리기 전후 바이트까지 같다) · 불 칸에 불꽃 · 빠진 방은 서리 빛", same && burning >= 0 && fire > 15 && vac > ok + 15,
            $"같음 {same} · 불꽃 도트 {fire} · 파란 값 빠진 방 {vac:0} / 통로 {ok:0} · {img.W}×{img.H}");

        LiveRules(lay);
    }

    /// <summary>관전 화면 (Godot 장면의 속): 연출의 빠르기를 따르고 · 멈춤 · 밀린 것은 버림 · 카메라가 일 난 곳으로 · 세계는 그대로.</summary>
    private static void LiveRules(Layout lay)
    {
        int At(int x, int y) => Worlds.At(lay, x, y);
        Sim Make()
        {
            var w = Worlds.Full(lay, 3);
            var mw = w.Get<Mind>();
            mw.AddPerson(w, At(15, 2)); mw.AddRobot(w, At(20, 7), AgentKind.Robot, At(20, 7));
            return w;
        }
        var show = new LiveShow(Make(), "시험호");
        var twin = Make();                          // 화면 없이 같은 틱을 돌린 세계
        const double dt = 1.0 / 60;
        int calm = 0;
        for (int i = 0; i < 60; i++) calm += show.Advance(dt);       // 조용함: 화면 1초 = 60틱
        show.Paused = true; int paused = show.Advance(1); string pausedText = show.Status(); show.Paused = false;
        int lag = show.Advance(5);                                    // 5초 밀림 → 한 번에 50틱까지만
        int afterLag = show.Advance(dt);                              // 밀린 것은 버린다 — 다음 장에 몰아 돌리지 않는다
        twin.Run(show.Sim.Tick);
        int fire = At(3, 5);
        show.Sim.Get<Chemistry>().Ignite(fire, 3e6, show.Sim.Causes.Add(CauseKind.Story, "침대 불씨", show.Sim.Tick));
        twin.Get<Chemistry>().Ignite(fire, 3e6, twin.Causes.Add(CauseKind.Story, "침대 불씨", twin.Tick));
        double slow = 1e9; bool inView = false, srcOk = true; int frames = 0;
        for (int i = 0; i < 60 * 8; i++)
        {
            show.Advance(dt);
            slow = Math.Min(slow, show.Speed);
            if (i % 20 != 0) continue;
            var lf = show.Render(); frames++;
            srcOk &= lf.SrcX >= 0 && lf.SrcY >= 0 && lf.SrcX + lf.SrcW <= lf.Img.W + 0.01 && lf.SrcY + lf.SrcH <= lf.Img.H + 0.01;
            show.Recycle(lf.Img);
            var (l, t, w, h) = show.Camera();
            inView = lay.X(fire) + 0.5 >= l && lay.X(fire) + 0.5 <= l + w && lay.Y(fire) + 0.5 >= t && lay.Y(fire) + 0.5 <= t + h;
        }
        twin.Run(show.Sim.Tick - twin.Tick);
        bool same = show.Sim.Save().AsSpan().SequenceEqual(twin.Save());
        Check("관전 화면: 조용하면 화면 1초에 60틱 · 멈추면 0 · 밀린 5초는 50틱까지만 (나머지는 버린다) · 불이 나면 느려지고 카메라가 불을 담는다 · 화면 없이 돌린 세계와 바이트까지 같다",
            calm is >= 59 and <= 61 && paused == 0 && pausedText.Contains("멈춤") && lag == LiveShow.MaxTicksPerCall && afterLag <= 1 && slow <= Director.OmenSpeed && inView && srcOk && same,
            $"조용함 {calm}틱 · 멈춤 {paused}틱 · 밀림 {lag}틱 → 다음 장 {afterLag}틱 · 가장 느리게 {slow}틱/초 · 불이 화면 안 {inView} · 그림 {frames}장 (보일 곳 맞음 {srcOk}) · 같음 {same}");
        // 걸음 사이 메우기: 불 난 방에 있던 사람이 달아난다 — 화면 1초에 2틱(한 틱 2.4초에 두세 칸)으로 보면 지나간 길을 따라 칸 사이에 그려진다
        var walk = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var wm = walk.Sim.Get<Mind>();
        wm.AddPerson(walk.Sim, At(5, 4)); wm.AddRobot(walk.Sim, At(20, 7), AgentKind.Robot, At(20, 7));
        walk.Sim.Run(3);
        walk.Sim.Get<Chemistry>().Ignite(At(3, 5), 3e6, walk.Sim.Causes.Add(CauseKind.Story, "침대 불씨", walk.Sim.Tick));
        walk.SpeedOverride = 2;
        int partial = 0, jumps = 0, moved = 0;
        for (int i = 0; i < 60 * 15; i++)
        {
            if (walk.Advance(dt) > 0) moved += wm.Agents.Count(a => walk.Nudge(a) != (0, 0));
            foreach (var a in wm.Agents)
            {
                var (nx, ny) = walk.Nudge(a);
                int m = Math.Max(Math.Abs(nx), Math.Abs(ny));
                if (m % Painter.T > 4 && m % Painter.T < Painter.T - 4) partial++;
                if (m > (LiveShow.TrailMax - 1) * Painter.T) jumps++;
            }
        }
        // 다시보기: 운석 하나가 하이라이트가 된 뒤 B — 산 세계는 멈추고, 되살린 세계에서 같은 운석이 같은 곳에 부딪힌다 → 끝나면 산 세계로
        var rs = new LiveShow(ShipWorld.Create("Didim", 3), "디딤호");
        int rc = rs.Sim.Causes.Add(CauseKind.Story, "운석 하나가 배를 향해 온다", rs.Sim.Tick);
        rs.Sim.Get<Outside>().AddRock(1.5, 6000, -1, 0.15, 2, 6000 * 60 * 4, rc); // 4분 거리
        for (int i = 0; i < 60 * 600 && (rs.Dir.Highlights.Count == 0 || rs.Sim.Tick < rs.Dir.Highlights[0].Last + Time.Minutes(3)); i++) rs.Advance(dt);
        var hl = rs.Dir.Highlights.FirstOrDefault();
        long liveHits = rs.Sim.Get<Outside>().RockHits, liveTick = rs.Sim.Tick;
        int liveHole = Enumerable.Range(0, lay.N).Count(c => c < rs.Sim.Layout.N && rs.Sim.Get<Field>().Hole[c] > 0);
        bool started = hl != null && rs.StartReplay(hl);
        long replayHits = 0; int replayHole = 0; bool liveStill = true; int replayFrames = 0;
        for (int i = 0; i < 60 * 600 && rs.Replaying; i++)
        {
            rs.Advance(dt);
            if (rs.Replaying) { replayHits = rs.World.Get<Outside>().RockHits; replayHole = Enumerable.Range(0, rs.World.Layout.N).Count(c => rs.World.Get<Field>().Hole[c] > 0); replayFrames++; }
            if (i % 30 == 0) { var lf = rs.Render(); rs.Recycle(lf.Img); }
            liveStill &= rs.Sim.Tick == liveTick;
        }
        Check("관전 화면 다시보기: 하이라이트를 첫 조짐 직전부터 되살려 보여 준다 — 같은 운석이 부딪히고 · 그동안 산 세계는 멈추고 · 끝나면 산 세계로 돌아온다",
            started && replayHits == liveHits && liveHits > 0 && replayFrames > 60 && liveStill && !rs.Replaying && rs.World == rs.Sim,
            hl == null ? "하이라이트 없음" : $"'{hl.Title}' · 다시보기 {replayFrames}장 · 충돌 산 세계 {liveHits} / 다시보기 {replayHits} · 산 세계 멈춤 {liveStill} · 돌아옴 {!rs.Replaying}");

        // 판 (화면 바퀴 1): 하이라이트 목록(L) — 가장 새것이 위 · 골라 Enter면 그 장면 다시보기 / 이야기꾼(T) — 고른 성격 · 가혹함 / 일지(J) · 아침 방송 자막
        rs.TogglePanel(PanelKind.Highlights);
        var hlRows = rs.Render(); rs.Recycle(hlRows.Img);
        bool listOk = hlRows.PanelTitle.StartsWith("볼 만했던 장면") && hlRows.PanelRows.Length == rs.Dir.Highlights.Count && hl != null && hlRows.PanelRows[0].Main.Contains(rs.Dir.Highlights[^1].Title) && hlRows.PanelPick == 0;
        bool pickReplay = rs.PanelEnter() && rs.Replaying && rs.ReplayOf == rs.Dir.Highlights[^1] && rs.Panel == PanelKind.None;
        rs.StopReplay();
        rs.TogglePanel(PanelKind.Storyteller);
        var st = rs.Sim.Get<Storyteller>();
        byte lv0 = st.Level;
        rs.PanelMove(+5); rs.PanelMove(-3);                 // 고른 이야기꾼(0)에서 → 사람의 이야기(5) → 몰아치는(2)
        rs.PanelEnter(); rs.PanelSide(+1);
        var stFrame = rs.Render(); rs.Recycle(stFrame.Img);
        bool tellerOk = st.Style == StoryStyle.Relentless && st.Level == lv0 + 1 && stFrame.PanelRows.Length == 7 && stFrame.PanelRows[2].On && stFrame.PanelRows[2].Sub.Length > 0
            && stFrame.PanelRows[6].Main.Contains($"{st.Level} {Storyteller.LevelName(st.Level)}") && stFrame.Notice.Contains("몰아치는");
        rs.TogglePanel(PanelKind.Storyteller);
        var js = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var jsm = js.Sim.Get<ShipMind>(); jsm.JournalHour = 1; jsm.MorningHour = 1; // 시험: 01시에 일지 · 방송
        js.Advance(dt);
        js.Sim.Run((int)(Time.Hours(1) - js.Sim.Tick + 1));
        for (int i = 0; i < 30; i++) js.Advance(dt);
        var jf = js.Render(); js.Recycle(jf.Img);
        js.TogglePanel(PanelKind.Journal); js.PanelMove(+1);   // 첫 줄은 배 형편 — 그 아래 일지를 골라 펼친다
        var jp = js.Render(); js.Recycle(jp.Img);
        bool journalOk = jf.Broadcast.StartsWith("주컴퓨터 방송 — 좋은 아침입니다.") && jf.BroadcastAlpha > 0 && jp.PanelTitle == "주컴퓨터" && jp.PanelRows.Length >= 3
            && jp.PanelRows[0].Main == "배 형편" && jp.PanelRows[0].Sub.Contains("산소")
            && jp.PanelRows[1].Main.Contains("큰 일 없이 지나갔다") && jp.PanelRows[1].Sub.Contains("배: 산소") && jp.PanelRows[^1].Main == "아침 방송";
        Check("관전 화면 판: 하이라이트 목록(가장 새것 위 · 골라 Enter면 그 장면 다시보기) · 이야기꾼 판(성격 여섯 · 한 줄 설명 · 가혹함 막대) · 일지 판 · 아침 방송 자막",
            listOk && pickReplay && tellerOk && journalOk,
            $"목록 {hlRows.PanelRows.Length}줄 '{(hlRows.PanelRows.Length > 0 ? hlRows.PanelRows[0].Main : "")}' · 골라 다시보기 {pickReplay} · 이야기꾼 {Storyteller.StyleName(st.Style)} 가혹함 {st.Level} · 일지 '{(jp.PanelRows.Length > 1 ? jp.PanelRows[1].Main : "")}' · 자막 '{jf.Broadcast[..Math.Min(40, jf.Broadcast.Length)]}'");

        // 사람 눌러 보기: 그려진 자리를 누르면 그 사람을 고르고 카드(이름 · 생각 · 몸)를 띄우고 카메라가 따라간다 · 빈 곳을 누르면 놓는다
        var pick = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var pm = pick.Sim.Get<Mind>();
        int who = pm.AddPerson(pick.Sim, At(15, 2)); pm.AddRobot(pick.Sim, At(20, 7), AgentKind.Robot, At(20, 7));
        pick.Advance(dt);
        var (wx, wy) = pick.DrawnAt(who);
        bool picked = pick.SelectAt(wx + 0.2, wy - 0.3) && pick.Selected == who;
        var card = pick.Card();
        for (int i = 0; i < 60 * 4; i++) pick.Advance(dt);
        var (cl, ct, cw, ch) = pick.Camera(); var (px2, py2) = pick.DrawnAt(who);
        bool followed = px2 >= cl && px2 <= cl + cw && py2 >= ct && py2 <= ct + ch;
        bool released = !pick.SelectAt(lay.X(At(3, 8)) + 0.5, lay.Y(At(3, 8)) + 0.5) && pick.Selected == -1 && pick.Card().Length == 0;
        Check("관전 화면 사람 눌러 보기: 누른 자리의 사람을 고르고 카드(이름 · 맡은 일 · 생각 · 몸)를 띄우고 따라간다 · 빈 곳을 누르면 놓는다",
            picked && card.Length >= 3 && card[0].StartsWith(pm.NameOf(who)) && card.Any(l => l.StartsWith("생각:")) && card.Any(l => l.StartsWith("몸:")) && followed && released,
            $"고름 {picked} · 카드 [{string.Join(" / ", card)}] · 따라감 {followed} · 놓음 {released}");

        Check("관전 화면: 틱이 드물면 걸음 사이를 메운다 — 한 틱에 지나간 칸들을 따라 사람 · 로봇이 칸 사이 자리에 그려진다",
            partial >= 20 && jumps == 0, $"걸은 틱 {moved} · 칸 사이에 그린 횟수 {partial} · 지나간 길보다 멀리 {jumps}");
        ViewRules2(lay, dt);
    }

    /// <summary>화면 바퀴 2 — 새 엔진에 맞게: 사슬 판 · 사람 판 · 주컴퓨터 판의 지금 사건 · 겹쳐 보기.</summary>
    private static void ViewRules2(Layout lay, double dt)
    {
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 사슬 판 (C) · 사슬 선: 지금 장면의 원인 사슬을 뿌리부터 차례로 · 갈래 이름 · 골라 Enter면 그 자리로 · 사슬 선이 그 자리들을 잇는다 ──
        {
            var cs = new LiveShow(Worlds.Full(lay, 3), "시험호");
            var w = cs.Sim;
            int a = At(3, 2), b = At(17, 2), c = At(12, 8);
            int r = w.Causes.Add(CauseKind.Story, "외판 한 곳이 오래 삭았다", w.Tick, a);
            int c1 = w.Causes.Add(CauseKind.Breach, "외판이 터졌다", w.Tick, a, r);
            int c2 = w.Causes.Add(CauseKind.Electric, "터진 외판 너머 전선이 끊겼다", w.Tick, b, c1);
            int c3 = w.Causes.Add(CauseKind.Person, "어둠 속에서 넘어졌다", w.Tick, c, c2);
            w.Facts.Emit(w.Tick, FactKind.WallBreach, a, 1, c1);
            cs.Advance(dt);
            w.Facts.Emit(w.Tick, FactKind.BreakerTrip, b, 1, c2);
            w.Facts.Emit(w.Tick, FactKind.Hurt, c, 1, c3);
            for (int i = 0; i < 600; i++) cs.Advance(dt);   // 결정적 순간이라 화면이 느리다 (10초)
            cs.TogglePanel(PanelKind.Chain);
            var cf = cs.Render(); cs.Recycle(cf.Img);
            var rows = cf.PanelRows;
            bool order = rows.Length == 5 && rows[1].Main.Contains("오래 삭았다") && rows[2].Main.Contains("[선체] 외판이 터졌다") && rows[3].Main.Contains("[전기·장치]") && rows[4].Main.Contains("[사람]");
            bool spread = rows.Length > 0 && rows[0].Main.Contains("선체 → 전기·장치 → 사람");
            cs.PanelMove(+3);   // 셋째 마디 (전선이 끊김 — 오른쪽 방)
            bool moved = cs.PanelEnter() && !cs.Follow && Math.Abs(cs.CamX - (lay.X(b) + 0.5)) < 0.01 && Math.Abs(cs.CamY - (lay.Y(b) + 0.5)) < 0.01;
            // 사슬 선: 겹쳐 보기를 사슬로 두면 마디 자리(터진 외판)에 선 빛이 든다
            cs.TogglePanel(PanelKind.Chain);
            var cells = cs.ChainCellsNow();
            // 같은 화면을 겹침 없이 · 사슬 선으로 그려 마디 칸 가운데의 빛을 견준다
            var painter = new Painter(lay);
            var none = painter.Frame(w, 0, 0, lay.W, lay.H, 0);
            painter.Show = Overlay.Chain; painter.ChainCells = cells;
            var line = painter.Frame(w, 0, 0, lay.W, lay.H, 0);
            int px = lay.X(b) * Painter.T + Painter.T / 2, py = lay.Y(b) * Painter.T + Painter.T / 2;
            var q0 = none.Get(px, py); var q1 = line.Get(px, py);
            bool lit = cells.Count == 3 && q1.G > q0.G + 40 && q1.B > q0.B + 40;
            Check("사슬 판 (C): 지금 장면의 원인 사슬을 뿌리부터 차례로 — 갈래 이름 · 건넌 갈래 한 줄 · 골라 Enter면 그 자리로 · 사슬 선이 마디 자리를 잇는다",
                order && spread && moved && lit,
                $"줄 {rows.Length}: {string.Join(" / ", rows.Select(x => x.Main))} · 그 자리로 {moved} ({cs.CamX:0.0}, {cs.CamY:0.0}) · 선 자리 {cells.Count} · 빛 {q0.G},{q0.B} → {q1.G},{q1.B}");
        }

        // ── 사람 판 (P): 위험한 사람부터 (쓰러진 이가 맨 위 · 붉게) · 몸 · 마음 · 가까운 이 · 골라 Enter면 따라간다 ──
        {
            var ps = new LiveShow(Worlds.Full(lay, 4), "시험호");
            var pm = ps.Sim.Get<Mind>(); var life = ps.Sim.Get<Life>();
            int ok = pm.AddPerson(ps.Sim, At(15, 2)), down = pm.AddPerson(ps.Sim, At(3, 4));
            ps.Advance(dt);
            ref var db = ref life[pm.Agents[down].C.Body];
            db.Conscious = 0; db.Hypoxia = 0.6f;   // 쓰러져 있다 (상황을 차려 둔다)
            ps.TogglePanel(PanelKind.People);
            var pf = ps.Render(); ps.Recycle(pf.Img);
            var pr = pf.PanelRows;
            bool first = pr.Length == 2 && pr[0].Main.StartsWith(pm.NameOf(down)) && pr[0].Main.Contains("쓰러져 있다") && pr[0].On && pr[0].Sub.Contains("몸: 산소") && !pr[1].On;
            bool follow = ps.PanelEnter() && ps.Selected == down && ps.FollowSelected && ps.Panel == PanelKind.None;
            Check("사람 판 (P): 위험한 사람부터 — 쓰러진 이가 맨 위 · 몸 · 마음 · 가까운 이 · 골라 Enter면 그 사람을 따라간다",
                first && follow, $"줄: {string.Join(" / ", pr.Select(x => x.Main + (x.On ? " ●" : "")))} · 따라감 {follow}");
        }

        // ── 주컴퓨터 판 (J): 이어지는 사건이 맨 위 (방 · 종류 · 몇 분째 · 누구에게 맡겼나) · 배 형편 ──
        {
            var ms = new LiveShow(Worlds.Full(lay, 5), "시험호");
            ms.Sim.Get<Mind>().AddRobot(ms.Sim, At(20, 7), AgentKind.Robot, At(20, 7));
            ms.Sim.Run(5);
            ms.Sim.Get<Chemistry>().Ignite(At(15, 1), 3e6, ms.Sim.Causes.Add(CauseKind.Story, "의자 밑 불씨", ms.Sim.Tick));
            var cm = ms.Sim.Get<ShipMind>();
            for (int i = 0; i < Time.Minutes(2) && !cm.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Fire); i++) ms.Sim.Step();
            ms.Advance(dt);
            ms.TogglePanel(PanelKind.Journal);
            var mf = ms.Render(); ms.Recycle(mf.Img);
            var mr = mf.PanelRows;
            int shipRow = Array.FindIndex(mr, x => x.Main == "배 형편");
            bool incident = mr.Length >= 2 && mr[0].Main.StartsWith("지금 · ") && mr[0].Main.Contains("불") && mr[0].Main.Contains("분째") && mr[0].On;
            Check("주컴퓨터 판 (J): 이어지는 사건이 맨 위 (방 · 종류 · 몇 분째 · 맡긴 로봇) · 그 아래 배 형편 (산소 · 전력)",
                incident && shipRow > 0 && mr[shipRow].Sub.Contains("산소") && mr[shipRow].Sub.Contains("전력"),
                $"줄: {string.Join(" / ", mr.Take(4).Select(x => x.Main + (x.Sub.Length > 0 ? " (" + x.Sub.Split('\n')[0] + ")" : "")))}");
        }

        // ── 겹쳐 보기 (O): 공기 — 빠진 방은 파랗게 · 멀쩡한 방은 그대로 · 그려도 세계는 그대로 ──
        {
            var s = Worlds.Full(lay, 6);
            var f = s.Get<Field>();
            int vac = At(17, 2), fine = At(3, 2);
            foreach (int c in lay.Rooms[lay.Room[vac]].Cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * 0.97);
            var before = s.Save();
            var painter = new Painter(lay);
            var none = painter.Frame(s, 0, 0, lay.W, lay.H, 0);
            painter.Show = Overlay.Air;
            var air = painter.Frame(s, 0, 0, lay.W, lay.H, 0);
            bool same = before.AsSpan().SequenceEqual(s.Save());
            (int, int) Mid(int c) => (lay.X(c) * Painter.T + 5, lay.Y(c) * Painter.T + 5);
            var (vx, vy) = Mid(vac); var (fx, fy) = Mid(fine);
            var v0 = none.Get(vx, vy); var v1 = air.Get(vx, vy); var f0 = none.Get(fx, fy); var f1 = air.Get(fx, fy);
            bool blue = v1.B > v0.B + 20 && v1.B > v1.R;
            bool untouched = f0.R == f1.R && f0.G == f1.G && f0.B == f1.B;
            var show = new LiveShow(Worlds.Full(lay, 6), "시험호");
            var names = new List<string>();
            for (int i = 0; i < 6; i++) { show.CycleOverlay(); names.Add(show.Show.ToString()); }
            Check("겹쳐 보기 (O): 공기 — 빠진 방은 파랗게 · 멀쩡한 방은 그대로 · 그려도 세계는 바이트까지 같다 · O로 공기 → 열 → 연기 → 사슬 선 → 선과 관 → 끔",
                blue && untouched && same && string.Join(",", names) == "Air,Heat,Smoke,Chain,Lines,None",
                $"빠진 방 {v0.R},{v0.G},{v0.B} → {v1.R},{v1.G},{v1.B} · 멀쩡한 방 같음 {untouched} · 세계 같음 {same} · 차례 {string.Join(" → ", names)}");

            // 선과 관 (⑥ 바퀴 3): 평소 화면엔 안 보이고 겹쳐 보기에서만 — 선이 지나는 칸은 칠하고 선이 없는 칸은 그대로
            var ls = Worlds.Full(lay, 6); ls.Run(2);
            var lnet = ls.Get<Network>();
            int onLine = Enumerable.Range(0, lay.N).First(c => lnet.Strands[c] > 0 && lnet.Strands[c] < 255);
            int offLine = Enumerable.Range(0, lay.N).First(c => lay.Kind[c] == CellKind.Floor && lnet.LineAt[c] == 0);
            var lp = new Painter(lay);
            var plain = lp.Frame(ls, 0, 0, lay.W, lay.H, 0);
            var plainOn = Copy(plain);
            lp.Show = Overlay.Lines;
            var drawn = lp.Frame(ls, 0, 0, lay.W, lay.H, 0);
            bool Diff(Img a, Img b, int c) { int x0 = lay.X(c) * Painter.T, y0 = lay.Y(c) * Painter.T; for (int y = y0; y < y0 + Painter.T; y++) for (int x = x0; x < x0 + Painter.T; x++) for (int k = 0; k < 4; k++) if (a.Px[(y * a.W + x) * 4 + k] != b.Px[(y * b.W + x) * 4 + k]) return true; return false; }
            Check("겹쳐 보기 '선과 관': 평소 화면에는 선이 안 보이고 · O로 켜면 선이 지나는 칸만 칠한다 (전선 · 냉각관 · 물관 · 센서 선)",
                Diff(plainOn, drawn, onLine) && !Diff(plainOn, drawn, offLine), $"선 칸 {onLine} · 빈 칸 {offLine}");
        }
    }

    private static Img Copy(Img a) { var b = new Img(a.W, a.H); Array.Copy(a.Px, b.Px, a.Px.Length); return b; }
}
