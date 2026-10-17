using ShipEngine;
using ShipView;

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
        var rs = new LiveShow(ShipWorld.Create("Kestrel", 3), "제비호");
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
    }
}
