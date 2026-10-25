using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>물체 규칙 시험 (docs/PHYSICS.md §10 — 묶음 '물체' · '물체 그림' …): 작은 판 · 몇 틱 · 몇 장.</summary>
public static partial class Program
{
    private static void PropRules()
    {
        if (Do("물체"))
        {
            Console.WriteLine("\n[물체 — 문틀 · 사람 · 감압 · 덜컹 · 점화 · 폭풍]");
            DoorFrameRules();
            PropHitRules();
            BreachRules();
            FrameRules();
        }
        if (Do("물체 그림"))
        {
            Console.WriteLine("\n[물체 그림]");
            StackRules();
            TrackDrawRules();
            ShardTimeRule();
        }
    }

    // 두 방 사이 문 하나 (문 칸은 위아래가 벽 — 동서로 지나고 문짝은 남북으로 닫힌다)
    private const string DoorRooms = "\n###########\n#....#....#\n#....D....#\n#....#....#\n###########";

    /// <summary>
    /// 문틀 (PH4b — docs/PHYSICS.md §3-6 · §10 '문틀'): 닫히는 문짝 판이 물체에서 멈춘다 — 10kg 넘는 것이 물리면 문이 끼고 (원인 '문틀에 끼었다' · 물체는 문틀에 낌) ·
    /// 가벼운 것은 문짝이 밀어내 문이 닫힌다 · 문 칸에 걸친 것도 막는다 · 어느 쪽이든 닫으라는 목표(DoorTarget)는 그대로다 · 바로잡으면 물체를 옆 바닥으로 치운다.
    /// </summary>
    private static void DoorFrameRules()
    {
        var lay = Worlds.Ascii(DoorRooms, Worlds.Seats);
        int door = lay.DoorAt[Worlds.At(lay, 5, 2)], dc = lay.Doors[door].Cell;
        // 열어 둔 문 칸 가운데 (dx: 동서로 비켜 — 걸친 것)에 물건을 두고 닫으라 한다: 1틱 뒤 · 3틱 뒤
        (Sim s, int id, float open1, int held1, bool jam, float open, float target, byte state, int cell, string story) Run(string item, double dx)
        {
            var s = Worlds.Full(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.RequestDoor(door, 1); s.Run(3);
            int id = Worlds.PutItem(s, item, dc);
            int i = st.IndexOf(id); var p = st.Props[i]; p.X += dx; st.Props[i] = p;
            st.RequestDoor(door, 0);
            s.Step();
            float open1 = f.DoorOpen[door]; int held1 = st.DoorHeldBy(door);
            s.Run(2);
            int k = st.IndexOf(id);
            string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.DoorJammed).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
            return (s, id, open1, held1, st.DoorJam[door] != 0, f.DoorOpen[door], st.DoorTarget[door], k >= 0 ? st.Props[k].State : (byte)255, k >= 0 ? st.CellOf(st.Props[k]) : -1, story);
        }
        var heavy = Run("식량 상자", 0);
        Check("문틀: 닫히는 문짝에 물린 식량 상자(13kg)가 문을 끼운다 — 문은 반 넘게 열린 채 · 닫으라는 목표는 그대로 (0) · 상자는 문틀에 낌 · 붙든 물체가 그 상자 · 원인 '문틀에 끼었다'",
            heavy.jam && heavy.open >= 0.5f && heavy.target == 0 && heavy.state == 7 && heavy.cell == dc && heavy.held1 == heavy.id && heavy.story.Contains("식량 상자") && heavy.story.Contains("문틀에 끼었다"),
            $"끼임 {heavy.jam} · 열림 {heavy.open:0.00} · 목표 {heavy.target:0.0} · 상태 {heavy.state} · 칸 {(heavy.cell == dc ? "문" : heavy.cell.ToString())} · 붙든 것 {heavy.held1}/{heavy.id} · \"{heavy.story}\"");
        var astride = Run("식량 상자", -0.62);
        Check("문틀: 문 칸에 걸친 식량 상자(가운데는 옆 칸 · 몸이 문 칸에 0.1m 들어옴)도 문짝을 붙들어 문이 낀다 · 목표 그대로",
            astride.jam && astride.open >= 0.5f && astride.target == 0 && astride.held1 == astride.id && astride.cell == dc - 1 && astride.state == 7,
            $"끼임 {astride.jam} · 열림 {astride.open:0.00} · 목표 {astride.target:0.0} · 붙든 것 {astride.held1} · 칸 {astride.cell}/{dc - 1} · 상태 {astride.state}");
        var light = Run("공구 상자", 0);
        Check("문틀: 가벼운 공구 상자(5kg)는 닫히는 문짝이 밀어낸다 — 첫 틱은 상자 앞에서 멈추고 (반쯤 열림) 그 틱에 상자가 문 줄 밖으로 밀려나 (붙든 것 없음) · 다음 틱에 닫힌다 · 끼지 않는다 · 목표 그대로",
            light.open1 > 0.5f && light.open1 < 1 && light.held1 < 0 && !light.jam && light.open == 0 && light.target == 0 && light.cell != dc && light.cell >= 0 && light.state != 7,
            $"첫 틱 열림 {light.open1:0.00} · 붙든 것 {light.held1}/{light.id} · 3틱 뒤 열림 {light.open:0.00} · 끼임 {light.jam} · 칸 {light.cell}/{dc} · 상태 {light.state}");
        // 바로잡기: 끼인 문을 풀고 (FreeDoor) 문틀의 물체를 옆 바닥으로 치우면 (ClearDoorProps) 닫힌다
        {
            var (s, id, _, _, _, _, _, _, _, _) = heavy;
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.FreeDoor(door); st.ClearDoorProps(door, s.Tick); st.RequestDoor(door, 0);
            s.Run(2);
            int k = st.IndexOf(id); var p = k >= 0 ? st.Props[k] : default;
            int pc = k >= 0 ? st.CellOf(p) : -1;
            Check("문틀: 끼인 문을 바로잡으면 문틀의 상자를 옆 바닥으로 치우고 (놓임) 문이 닫힌다",
                k >= 0 && pc != dc && pc >= 0 && lay.Kind[pc] == CellKind.Floor && p.State == 0 && f.DoorOpen[door] == 0 && st.DoorJam[door] == 0 && st.DoorHeldBy(door) < 0,
                $"상자 칸 {pc}/{dc} · 상태 {p.State} · 문 {f.DoorOpen[door]:0.00} · 끼임 {st.DoorJam[door]} · 붙든 것 {st.DoorHeldBy(door)}");
        }
    }

    /// <summary>
    /// 날아든 물체에 맞음 (PH4b — docs/PHYSICS.md §7 · §10 '사람'): ⑧이 산 몸을 원판으로 알리고 (SetActors) · ④가 날아든 물체의 맞음을 장부(PropHits)에 적고 ·
    /// ⑦이 5틱마다 읽어 20J 넘으면 타박 (E/800 · 원인은 민 것까지) — 장부는 읽은 뒤 빈다.
    /// </summary>
    private static void PropHitRules()
    {
        var lay = Worlds.Ascii("\n############\n#..........#\n#..........#\n#..........#\n############");
        var s = Worlds.Full(lay, 1);
        var m = s.Get<Mind>(); var st = s.Get<Structure>(); var life = s.Get<Life>();
        int who = m.AddPerson(s, Worlds.At(lay, 8, 2)), body = m[who].C.Body;
        s.Run(2);
        int at = life[body].Cell;
        int id = Worlds.PutItem(s, "식량 상자", at - 3);
        int cause = s.Causes.Add(CauseKind.Setup, "누가 식량 상자를 걷어찼다", s.Tick);
        var p = st.Props[st.IndexOf(id)]; var fm = st.PropForm(p);
        st.Shove(id, fm.Mass * 7, 0, 0, cause);   // 7m/s로 사람 쪽 (3m)
        float blunt0 = life[body].Blunt;
        s.Step();
        var hits = st.PropHits.Where(h => h.Tag == body).ToList();
        double e = hits.Count > 0 ? hits[0].E : 0;
        s.Run(5);
        var hurt = s.Facts.Since(0).Where(x => x.Kind == FactKind.Hurt && x.Actor == body).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
        float got = life[body].Blunt - blunt0;
        Check("사람: 7m/s로 날아든 식량 상자(13kg)에 맞으면 ④ 장부에 한 번 (20J 넘게) → ⑦이 5틱 안에 읽어 타박 (E/800) · 원인은 걷어찬 것까지 · 장부는 빈다",
            hits.Count == 1 && e >= 20 && Math.Abs(got - Math.Min(1.5, e / 800)) < 1e-4 && hurt.Contains("식량 상자") && hurt.Contains("걷어찼다") && !st.PropHits.Any(h => h.Tag == body),
            $"맞음 {hits.Count} · {e:0}J · 타박 {got:0.000}/{Math.Min(1.5, e / 800):0.000} · \"{hurt}\" · 남은 장부 {st.PropHits.Count}");
    }

    /// <summary>
    /// 덜컹 · 돎 · 점화 · 폭풍 (PH4d — docs/PHYSICS.md §3-2 · §4-1 · §10 '덜컹 · 돎' · '점화'): 몸을 바꾼 밂은 그 몸 위 모든 물체의 바닥을 바꾼다 (덜컹 — 세상 빠르기는 그대로) ·
    /// 도는 조각 위 물체는 원심 · 코리올리를 · 점화하는 배 안 물체는 −a를 느낀다 — 정지 마찰 (젖으면 0.33배)이나 넘어짐 문턱 b/Hc를 넘으면 깬다 (넘어짐은 PH7) ·
    /// 운석 충격파 · 기체 폭발은 그 둘레 물체를 폭원에서 바깥쪽으로 민다 (P첨두 × 앞면 × 3ms).
    /// </summary>
    private static void FrameRules()
    {
        // 덜컹: 갈라진 몸 1을 3m/s로 민다 → 그 바닥의 식량 상자는 세상에 그대로 (그 순간 세상 빠르기가 바뀌지 않는다) · 바닥에 대해 −3m/s로 미끄러져 v²/2μg = 1.53m (μ 0.3) 가서 잔다
        //   그 몸이 아주 천천히 돌아도 (1e-8rad/s — 틀의 힘이 아주 작다) 같다 (고친 결함: 잔걸음 길이 식이 아주 작은 힘에 0이 되어 1024걸음을 제자리에서 돌고 상자가 서 버렸다)
        {
            (double slid, double wvx, double wvy, double y, byte state, string why) Run(double w0)
            {
                var (s, st) = SplitWorld(Sealed, 6);
                int right = st.BodyOf(s.Layout.Index(10, 3));
                { var b = st.Bodies[right]; b.W = w0; st.Bodies[right] = b; }
                int id = Slide(s, "식량 상자", 11.4, 3.5, 0, 0);
                var o = st.Bodies[right];
                int hit = s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각을 들이받았다", s.Tick);
                var (_, _, ux, uy, _) = st.PropWorld(st.Props[st.IndexOf(id)]);
                st.Push(right, o.X, o.Y, o.Mass * 3, 0, hit);
                var (_, _, vx, vy, _) = st.PropWorld(st.Props[st.IndexOf(id)]);
                s.Run(3);
                var p = st.Props[st.IndexOf(id)];
                return (11.4 - p.X, vx - ux, vy - uy, p.Y, p.State, p.Cause != 0 ? s.Causes.Story(p.Cause) : "");
            }
            var a = Run(0); var slow = Run(1e-8);
            Check("덜컹: 갈라진 조각이 3m/s로 밀리면 그 바닥의 식량 상자는 세상에 그대로 (그 순간 세상 빠르기가 바뀌지 않는다) · 바닥이 움직인 반대쪽으로 1.53m ± 0.1 미끄러져 잔다 (μ 0.3 · v²/2μg) · 원인은 들이받은 것",
                Math.Abs(a.wvx) < 1e-9 && Math.Abs(a.wvy) < 1e-9 && Math.Abs(a.slid - 1.53) <= 0.1 && Math.Abs(a.y - 3.5) < 1e-6 && a.state == 0 && a.why.Contains("들이받았다"),
                $"민 순간 세상 빠르기 바뀜 ({a.wvx:0.0e0}, {a.wvy:0.0e0}) · 바닥에서 {a.slid:0.000}m · y {a.y:0.000} · {(a.state == 0 ? "잠" : "깸")} · \"{a.why}\"");
            Check("덜컹 — 아주 천천히 도는 조각 (1e-8rad/s · 틀의 힘이 아주 작다)에서도 같게 1.53m 미끄러진다 (잔걸음 길이가 0이 되지 않는다)",
                Math.Abs(slow.slid - a.slid) < 1e-3, $"{slow.slid:0.000}m (돌지 않으면 {a.slid:0.000}m)");
        }
        // 돎: 짝힘으로 분에 14바퀴 돈 조각 — 무게중심에서 먼 잠든 식량 상자가 깨어 (덜컹 + 원심 ω²r > 정지 마찰) 바깥 벽까지 간다
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = FieldStructure(lay, 2);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), 0);
            s.Run(3);
            int right = st.BodyOf(Worlds.At(lay, 15, 3));
            int id = Slide(s, "식량 상자", 17.5, 2.6, 0, 0);
            var o = st.Bodies[right];
            double j = 1.5 * o.Inertia / 2;   // 짝힘 — 분에 14바퀴
            st.Push(right, o.X, o.Y + 1, j, 0); st.Push(right, o.X, o.Y - 1, -j, 0);
            double R(in Prop q) { var b = st.Bodies[right]; double dx = q.X - b.Lx, dy = q.Y - b.Ly; return Math.Sqrt(dx * dx + dy * dy); }
            double r0 = R(st.Props[st.IndexOf(id)]);
            s.Run(12);
            var p = st.Props[st.IndexOf(id)];
            double r1 = R(p);
            int c = st.CellOf(p);
            Span<int> nb = stackalloc int[4];
            bool byWall = false;
            if (c >= 0) { lay.Neighbors(c, nb); foreach (int q in nb) if (q >= 0 && lay.Kind[q] == CellKind.Wall) byWall = true; }
            Check("돎: 짝힘으로 분에 14바퀴 돈 조각 — 무게중심에서 먼 잠든 식량 상자가 깨어 (덜컹 · 원심 ω²r이 정지 마찰을 넘는다) 바깥 벽까지 간다",
                Math.Abs(st.Bodies[right].W) > 1.4 && r1 > r0 + 0.25 && byWall,
                $"분에 {Math.Abs(st.Bodies[right].W) * 60 / (2 * Math.PI):0.0}바퀴 · 상자 무게중심에서 {r0:0.00} → {r1:0.00}m · 벽 곁 {byWall}");
        }
        // 점화: 몸이 없는 배 (평시) — 0.2g 마른 바닥 식량 상자 그대로 · 젖으면 (×0.33) 미끄러진다 · 0.4g면 마른 바닥에서도 · 0.33g에 잠든 소화기가 깬다 (넘어짐 문턱 b/Hc < 0.33 — 넘어짐은 PH7) · 식량 상자는 깨지 않는다
        {
            (double dx, byte state, long woken) Burn(string name, double g, bool wet, int wetAfter = -1)
            {
                var s = PropWorld(1);
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                int id = Slide(s, name, 8.5, 4.5, 0, 0);
                int cell = s.Layout.Index(8, 4);
                if (wet && wetAfter < 0) f.AddStore(cell, f.T.sWater, 300);
                st.ShipAx = g * 9.8;
                for (int k = 0; k < 3; k++) { if (k == wetAfter) f.AddStore(cell, f.T.sWater, 300); s.Step(); }
                var p = st.Props[st.IndexOf(id)];
                return (p.X - 8.5, p.State, st.PropWoken);
            }
            var dry2 = Burn("식량 상자", 0.2, false); var wet2 = Burn("식량 상자", 0.2, true); var dry4 = Burn("식량 상자", 0.4, false);
            var ext = Burn("소화기", 0.33, false); var box33 = Burn("식량 상자", 0.33, false); var late = Burn("식량 상자", 0.2, true, 2);
            Check("점화: 몸이 없는 배 — 0.2g 마른 바닥 식량 상자는 그대로 · 젖은 바닥 (마찰 ×0.33)이면 엔진 반대쪽으로 미끄러진다 · 0.4g면 마른 바닥에서도 · 0.33g에 잠든 소화기가 깬다 (넘어짐 문턱 b/Hc) · 식량 상자는 깨지 않는다",
                dry2.dx == 0 && dry2.woken == 0 && wet2.dx < -0.3 && dry4.dx < -0.3 && ext.woken > 0 && ext.dx == 0 && box33.woken == 0 && box33.dx == 0,
                $"0.2g 마름 {dry2.dx:0.00}m · 젖음 {wet2.dx:0.00}m · 0.4g 마름 {dry4.dx:0.00}m · 0.33g 소화기 깸 {ext.woken} ({ext.dx:0.00}m) · 식량 상자 깸 {box33.woken}");
            Check("점화 중 젖음: 0.2g 점화 중 잠든 식량 상자의 칸이 젖으면 (둘째 틱 뒤) 지금 칸 값으로 다시 보아 깨어 미끄러진다",
                late.dx < -0.05 && late.woken > 0, $"{late.dx:0.00}m · 깸 {late.woken}");
        }
        // 폭풍: 운석 충격파 (Blast) · 기체 폭발 (수소 — ② GasBurstJ) — 둘레 컵이 폭원에서 바깥쪽으로 밀려 난다 (P첨두 × 앞면 × 3ms)
        {
            const string room = "\n##############\n#............#\n#............#\n#............#\n#............#\n##############";
            var lay = Worlds.Ascii(room);
            var s = FieldStructure(lay, 3);
            var st = s.Get<Structure>();
            int cup = Slide(s, "컵", 6.5, 4.5, 0, 0);
            s.Run(2);
            int bc = s.Causes.Add(CauseKind.Setup, "운석이 배 안에서 터졌다", s.Tick);
            st.Blast(s, s.Layout.Index(3, 4), 1.5e6, bc);
            s.Step();
            var p = st.Props[st.IndexOf(cup)];
            string why = p.Cause != 0 ? s.Causes.Story(p.Cause) : "";
            var g = new Sim(new SimSetup(lay, 4, sim => { var f = new Field(sim.Layout, sim.Tables); sim.Add(f); sim.Add(new Chemistry(f)); sim.Add(new Structure(f)); }));
            var gf = g.Get<Field>(); gf.FillAir();
            for (int x = 2; x <= 4; x++) for (int y = 3; y <= 5; y++) gf.AddGas(g.Layout.Index(x, y), gf.T.gH2, 12, 293);
            var gst = g.Get<Structure>();
            int gcup = Slide(g, "컵", 6.5, 4.5, 0, 0);
            g.Step();
            g.Get<Chemistry>().Ignite(g.Layout.Index(3, 4), 5e4, 0);
            double far = 0; string gwhy = "";
            for (int k = 0; k < 6; k++)
            {
                g.Step();
                int gi = gst.IndexOf(gcup);
                if (gi < 0) break;
                var q = gst.Props[gi];
                far = Math.Max(far, q.X - 6.5);
                if (q.Cause != 0) gwhy = g.Causes.Story(q.Cause);
            }
            Check("폭풍: 운석 충격파 (1.5MJ · 3m 거리)에 컵이 폭원에서 바깥쪽으로 날아간다 (원인 '충격파') · 수소 폭발 곁 컵도 (② 한꺼번에 탄 열 → TNT 당량 · 같은 셈)",
                p.X > 6.5 + 0.3 && why.Contains("충격파") && far > 0.3 && gwhy.Contains("탔다"),
                $"충격파 컵 x {p.X:0.00} · \"{why}\" / 수소 폭발 컵 {far:0.00}m · \"{gwhy}\"");
        }
    }

    /// <summary>
    /// 틱 사이를 메운다 (PH5 — docs/PHYSICS.md §6-1 · §10 '그림' · '소리'): 관전 화면은 한 틱 늦은 시계로 그린다 — 지난 틱의 틱 안 기록 (열쇠틀)을 그리는 때만큼 에르미트로 메워
    /// 도는 상자가 틱 사이 장마다 나아가고 · 충돌 잔걸음에 난 것 (사실 · 소리 · 부스러기)은 그 때 (Sub)를 지녀 그 때를 지나는 장에 난다.
    /// </summary>
    private static void TrackDrawRules()
    {
        // 도는 상자: 0.3g 바닥에서 2.2m/s · 3rad/s로 민 식량 상자 — 틱 사이 f 0.25 · 0.5 · 0.75 세 장이 다르고 한쪽으로 나아가며 한쪽으로 돈다 (메우지 않으면 셋 다 틱 끝 자리)
        {
            var lay = Worlds.Ascii(PropRoom);
            var s = Worlds.Full(lay, 2);
            var show = new LiveShow(s, "시험호") { SpeedOverride = 1, Follow = false };
            show.Ears.Enabled = false;
            show.CamX = 7; show.CamY = 5; show.ViewW = 14;
            Array.Fill(s.Get<Field>().Grav, 0.3f);   // 0.3g 바닥 (한 틱 내내 미끄러지며 돌게)
            s.Run(2);
            var st = s.Get<Structure>();
            int id = Worlds.PutItem(s, "식량 상자", s.Layout.Index(4, 5));
            var fm = st.PropForm(st.Props[st.IndexOf(id)]);
            st.Shove(id, fm.Mass * 2.2, 0, fm.I * 3);
            show.Advance(1.0);   // 그 틱을 돌린다 (그리는 때는 지난 틱 끝 — 화면은 한 틱 늦다)
            var pos = new List<(double x, double y, double th)>();
            var px = new List<uint[]>();
            for (int k = 0; k < 3; k++)
            {
                show.Advance(0.25);
                pos.Add(show.DrawnProp(id));
                var lf = show.Render();
                px.Add(PixelsOf(lf.Img)); show.Recycle(lf.Img);
            }
            int d01 = Differ(px[0], px[1]), d12 = Differ(px[1], px[2]);
            var end = st.Props[st.IndexOf(id)];
            Check("틱 사이 메움: 0.3g 바닥에서 2.2m/s · 3rad/s로 민 식량 상자 — 관전 화면의 틱 사이 f 0.25 · 0.5 · 0.75 세 장이 다르고 한쪽으로 나아가며 한쪽으로 돈다 (틱 끝 자리로 뛰지 않는다)",
                d01 > 0 && d12 > 0 && pos[0].x < pos[1].x && pos[1].x < pos[2].x && pos[2].x < end.X + 1e-9 && pos[0].th < pos[1].th && pos[1].th < pos[2].th,
                $"그린 자리 x {pos[0].x:0.000} → {pos[1].x:0.000} → {pos[2].x:0.000} (틱 끝 {end.X:0.000}) · 각 {pos[0].th:0.00} → {pos[1].th:0.00} → {pos[2].th:0.00} · 다른 도트 {d01} · {d12}");
        }
        // 충돌 잔걸음: 갈라진 몸 1을 배 쪽으로 1m/s 밀면 틱 가운데쯤 부딪힌다 — 그 사실 · 쾅 소리가 그 때 (Sub)를 지니고 · 귀는 그리는 때가 그 때를 지날 때 듣는다 (틱을 돌린 순간이 아니라 — 소리가 그림보다 앞서지 않는다)
        {
            var lay = Worlds.Ascii(Sealed);
            var s = Worlds.Full(lay, 5);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            var show = new LiveShow(s, "시험호") { SpeedOverride = 1, Follow = false };
            show.CamX = 7; show.CamY = 3; show.ViewW = 16;
            var heard = new List<(double at, string line)>();
            show.Ears.Log = line => { if (line.Contains("Bang")) heard.Add((show.DrawnTick, line)); };
            int hit = s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각을 배 쪽으로 밀었다", s.Tick);
            var o = st.Bodies[1];
            st.Push(1, o.X, o.Y, -o.Mass * 1.0, 0, hit);
            long seen = s.Facts.Total;
            Fact crash = default;
            for (int k = 0; k < 8 * 6 && crash.Kind != FactKind.Crash; k++)
            {
                show.Advance(1 / 8.0);
                foreach (var x in s.Facts.Since(seen)) if (x.Kind == FactKind.Crash) { crash = x; break; }
                seen = s.Facts.Total;
            }
            for (int k = 0; k < 10; k++) show.Advance(1 / 8.0);
            double at = crash.Kind == FactKind.Crash ? crash.Tick + crash.Sub / 255.0 : double.NaN;   // 그 걸음은 틱 crash.Tick에서 하나 더까지 (그리는 때 = 돌린 뒤 틱 − 1 + 틱 사이)
            double first = heard.Count > 0 ? heard[0].at : double.NaN;
            Check("충돌 잔걸음의 때: 갈라진 조각을 배 쪽으로 1m/s 밀면 틱 가운데에 부딪힌다 — 사실이 그 때 (Sub)를 지니고 · 귀는 그리는 때가 그 때를 지난 장에 쾅을 듣는다 (한 장 1/8틱 안 · 틱을 돌린 순간 소리가 그림보다 앞서지 않는다)",
                crash.Kind == FactKind.Crash && crash.Sub > 32 && crash.Sub < 223 && first >= at - 1e-9 && first <= at + 1 / 8.0 + 1e-9,
                crash.Kind != FactKind.Crash ? "부딪힘 없음" : $"부딪힌 때 틱 {crash.Tick} · 틱 안 {crash.Sub / 255.0:0.00} → 그리는 때 {at:0.000} · 귀가 들은 때 {first:0.000}");
        }
    }

    /// <summary>충돌 잔걸음에 난 부스러기 (PH5): 몸 1을 멀리 보냈다가 40m/s로 되돌려 틱 가운데에 배를 들이받게 한다 — 찢긴 판의 부스러기가 그 때 (Sub)를 지니고 · 그 전 장에는 그리지 않는다.</summary>
    private static void ShardTimeRule()
    {
        var lay = Worlds.Ascii(Sealed);
        var s = Worlds.Full(lay, 5);
        var st = s.Get<Structure>();
        s.Run(3);
        for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
        s.Run(3);
        var show = new LiveShow(s, "시험호") { SpeedOverride = 1, Follow = false };
        show.Ears.Enabled = false;
        var o = st.Bodies[1];
        st.Push(1, o.X, o.Y, o.Mass * 5, 0);
        s.Run(3);
        o = st.Bodies[1];
        st.Push(1, o.X, o.Y, -o.Mass * 45, 0, s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각을 배 쪽으로 세게 밀었다", s.Tick));
        long made = st.ShardsMade;
        Shard first = default; bool got = false;
        for (int k = 0; k < 6 && !got; k++)
        {
            show.Advance(1.0);
            foreach (var sh in st.Shards) if (sh.Born == s.Tick - 1 && sh.Sub > 0) { first = sh; got = true; break; }
        }
        bool hidden = false, shown = false;
        if (got)
        {
            var lf = show.Render(); show.Recycle(lf.Img);
            double at = first.Sub / 255.0;
            hidden = !show.Painter.ShardShown(first, Math.Max(0, at - 1 / 16.0));
            shown = show.Painter.ShardShown(first, Math.Min(1, at + 1e-6));
        }
        Check("충돌 잔걸음의 부스러기: 배를 40m/s로 들이받아 찢긴 판의 부스러기는 난 때 (Sub — 틱 처음 것은 0)를 지니고 · 관전 화면은 그 때 전 장에는 그리지 않는다 (한 장 앞 1/16틱) · 그 때부터 그린다",
            got && hidden && shown, got ? $"부스러기 {st.ShardsMade - made} · 처음 것 틱 안 {first.Sub / 255.0:0.00} · 앞 장 숨김 {hidden} · 그 때 보임 {shown}" : $"틱 안에 난 부스러기 없음 (부스러기 {st.ShardsMade - made} · 부딪힘 {st.Crashes})");
    }

    private static uint[] PixelsOf(Img img) { var a = new uint[img.W * img.H]; for (int y = 0; y < img.H; y++) for (int x = 0; x < img.W; x++) { var c = img.Get(x, y); a[y * img.W + x] = (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A); } return a; }
    private static int Differ(uint[] a, uint[] b) { int n = 0; for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) n++; return n + Math.Abs(a.Length - b.Length); }

    // 화물칸 (외판 x = 0에 구멍) · 닫힌 문 · 통로 — RulesFling과 같은 꼴
    private const string BreachRooms = "\n###############\n#.....#.......#\n#.....D.......#\n#.....#.......#\n###############";

    /// <summary>
    /// 감압 (PH4c — docs/PHYSICS.md §3-2 · §3-6 · §10 '감압'): 외판 0.075m² 구멍 입 곁 (두 칸에 걸친) 식량 상자가 구멍 입 바람에 돌며 미끄러져 벽에 닿아 선다 —
    /// 선 자리는 칸 가운데가 아니고 벽 안이 아니다 · 구멍보다 큰 상자는 빨려 붙어 막는다 (샘 ≤ 0.1배) · 사람과 상자가 같은 구멍에 붙으면 막음 = 둘 가운데 큰 것 (10틱 흔들리지 않는다) ·
    /// 빨아들이지 않는 구멍 (기압이 같은 두 방 사이 안쪽 벽)은 보통 벽이다 — 부딪은 것은 튀고 붙지도 · 우주로 가지도 않는다.
    /// </summary>
    private static void BreachRules()
    {
        var lay = Worlds.Ascii(BreachRooms, Worlds.Seats);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int hole = At(0, 2);
        const double dot = DotForm.Dot;
        // ── 구멍 입 곁 상자: 가운데가 칸 경계 가까이 (위 칸에 0.1m 넘게 걸침) ──
        {
            var s = Worlds.Full(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.RequestDoor(lay.DoorAt[At(6, 2)], 0);
            int id = Worlds.PutItem(s, "식량 상자", At(1, 2));
            { int i = st.IndexOf(id); var p = st.Props[i]; p.Y -= 0.42; st.Props[i] = p; }
            s.Run(5);
            int cause = s.Causes.Add(CauseKind.Setup, "화물칸 외판에 작은 파편이 박혔다", s.Tick);
            st.DamageCause[hole] = cause; f.SetHole(hole, 0.075f);
            for (int k = 0; k < 30; k++)
            {
                s.Step();
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && k < 4) { var d = st.Props[st.IndexOf(id)]; Console.WriteLine($"    {k}틱 바람 칸 {f.WindCells.Count} · 상자 칸 바람 {f.WindPa[st.CellOf(d)]:0}Pa · 입 {f.HoleMouthPa(st.CellOf(d), out _):0}Pa · 기압 {f.Pressure(st.CellOf(d)):0.0} · 상자 ({d.X:0.000}, {d.Y:0.000}) 상태 {d.State} θ {d.Th:0.000}"); }
            }
            var q = st.Props[st.IndexOf(id)];
            var fm = st.PropForm(q);
            double cs = Math.Cos(q.Th), sn = Math.Sin(q.Th), west = double.MaxValue;
            foreach (var (hx, hy) in fm.Hull) west = Math.Min(west, q.X + cs * (hx - fm.Cx) - sn * (hy - fm.Cy));
            double wallFace = lay.X(hole) + 1;   // 구멍 벽 칸의 안쪽 면 (m)
            var link = f.Links.FirstOrDefault(k => k.Via == hole);
            double leak = link != null && link.Area0 > 0 ? link.Area / link.Area0 : 1;
            double fx = q.X - Math.Floor(q.X), fy = q.Y - Math.Floor(q.Y);
            string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.ObjectFlung).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
            Check("감압: 외판 0.075m² 구멍 입 곁 (두 칸에 걸친) 식량 상자가 구멍 입 바람에 돌며 미끄러져 벽에 닿아 선다 — 칸 가운데가 아니다 · 벽 안이 아니다 (0.5도트) · 돌았다",
                Math.Abs(fx - 0.5) + Math.Abs(fy - 0.5) > 0.05 && west >= wallFace - 0.5 * dot && west <= wallFace + 2 * dot && Math.Abs(q.Th) > 0.02,
                $"자리 ({q.X:0.000}, {q.Y:0.000}) · 서쪽 끝 {west:0.000}/{wallFace:0.000}m · θ {q.Th:0.000}rad · 상태 {q.State}");
            Check("감압: 구멍보다 큰 식량 상자는 빨려 붙어 막는다 — 물체 막음 (장의 PlugProp) · 샘 ≤ 0.1배 · 원인 사슬은 파편까지",
                q.State == 6 && q.PlugAt == hole + 1 && f.PlugProp[hole] > 0 && leak <= 0.1 + 1e-6 && story.Contains("빨려 붙었다") && story.Contains("파편"),
                $"상태 {q.State} · 막음 {q.PlugAt - 1}/{hole} · PlugProp {f.PlugProp[hole]:0.000}m² · 샘 {leak:0.000}배 · \"{story}\"");
        }
        // ── 사람과 상자가 같은 구멍에 빨려 붙었다: 막음 = 둘 가운데 큰 것 · 10틱 동안 흔들리지 않는다 (몸은 PlugBody · 상자는 PlugProp) ──
        {
            var s = Worlds.Full(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>(); var m = s.Get<Mind>();
            st.RequestDoor(lay.DoorAt[At(6, 2)], 0);
            int who = m.AddPerson(s, At(1, 2));
            int id = Worlds.PutItem(s, "식량 상자", At(1, 2));
            s.Run(3);
            st.DamageCause[hole] = s.Causes.Add(CauseKind.Setup, "화물칸 외판에 작은 파편이 박혔다", s.Tick); f.SetHole(hole, 0.06f);
            m[who].C.PinAt = hole + 1; st.SetItemPlug(id, hole + 1);
            s.Run(2);
            var seen = new List<float>(); float body = 0, prop = 0;
            for (int k = 0; k < 10; k++) { s.Step(); seen.Add(f.PlugOf(hole)); body = f.PlugBody[hole]; prop = f.PlugProp[hole]; }
            bool steady = seen.All(x => x == seen[0]);
            Check("감압: 사람과 상자가 같은 0.06m² 구멍에 빨려 붙었다 — 몸 막음 · 물체 막음이 따로 들고 막음 = 둘 가운데 큰 것 · 10틱 동안 흔들리지 않는다",
                body > 0 && prop > 0 && steady && seen[0] == Math.Max(body, prop),
                $"몸 {body:0.0000} · 물체 {prop:0.0000} · 막음 {string.Join(",", seen.Distinct().Select(x => x.ToString("0.0000")))}m²");
            // 저장 판: 물건 막음이 몸 막음 (PlugBody)에 함께 있던 판 107 저장은 거절한다 · 지금 판은 되살린 첫 틱부터 같은 막음
            var save = s.Save();
            var old = (byte[])save.Clone();
            BitConverter.GetBytes(107).CopyTo(old, 8);   // 이름표(8바이트) 다음이 판 번호 — PH3b 검토가 밖에 낸 판 (물건 막음도 PlugBody에)
            string refused = "";
            try { new Sim(s.Setup).Load(old); } catch (InvalidDataException e) { refused = e.Message; }
            var back = new Sim(s.Setup); back.Load(save);
            s.Step(); back.Step();
            Check("감압: 저장 판 — 물건 막음이 몸 막음 칸에 함께 있던 판 107 저장은 거절한다 · 지금 판의 저장에서 되살린 세계는 첫 틱부터 같은 막음 (바이트 같음)",
                refused.Contains("저장 판 107") && back.Get<Field>().PlugOf(hole) == f.PlugOf(hole) && s.Save().AsSpan().SequenceEqual(back.Save()),
                $"판 107: \"{(refused.Length > 0 ? refused : "읽었다")}\" · 막음 {f.PlugOf(hole):0.0000} / 되살린 세계 {back.Get<Field>().PlugOf(hole):0.0000}m²");
        }
        // ── 기압이 같은 두 방 사이 안쪽 벽 0.06m² 구멍: 빨아들이지 않는 구멍은 보통 벽이다 — 9m/s로 던진 컵은 튀어 남고 · 4m/s로 민 식량 상자는 붙지 않는다 ──
        {
            var twin = Worlds.Ascii(TwinRooms, Worlds.Seats);
            int inner = Worlds.At(twin, 6, 2);
            var s0 = Worlds.Full(twin, 1);
            s0.Get<Field>().SetHole(inner, 0.06f);
            s0.Run(3);
            var got = new List<string>(); bool ok = true;
            foreach (var (item, v) in new[] { ("컵", 9.0), ("식량 상자", 4.0) })
            {
                var s = s0.Clone();
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                int id = Worlds.PutItem(s, item, Worlds.At(twin, 3, 2));
                var p = st.Props[st.IndexOf(id)];
                int what = p.What;
                st.Shove(id, st.PropForm(p).Mass * v, 0);
                s.Run(20);
                int i = st.IndexOf(id);
                double spaced = s.Book.OfItem(what, Flow.Spaced);
                int flung = s.Facts.Since(0).Count(x => x.Kind == FactKind.ObjectFlung);
                ok &= i >= 0 && spaced == 0 && st.PropSpaced == 0 && st.PropPlugged == 0 && f.PlugProp[inner] == 0 && flung == 0 && st.Props[i].State != 6;
                got.Add($"{item}: {(i >= 0 ? $"({st.Props[i].X:0.00}, {st.Props[i].Y:0.00}) 상태 {st.Props[i].State}" : "없다")} · 우주로 {spaced} · 막음 {st.PropPlugged} · 날림 사실 {flung}");
            }
            Check("감압: 기압이 같은 두 방 사이 안쪽 벽 0.06m² 구멍은 빨아들이지 않는다 — 9m/s로 던진 컵은 튀어 남고 (우주로 0) · 4m/s로 민 식량 상자는 붙지 않는다 (막음 0)",
                ok, string.Join(" · ", got));
        }
    }

    // 기압이 같은 두 방 (가운데 벽 x = 6 — 문이 없다)
    private const string TwinRooms = "\n#############\n#.....#.....#\n#.....#.....#\n#.....#.....#\n#############";

    /// <summary>
    /// 쌓아 그린 꼴 (PH2): 탁자 · 소화기 · 식량 상자 · 우주복을 각 단 0~63으로 (32 · 16도트 둘 다) — 불투명 도트 수가 단 0의 ±10% 안 · 단 0과 8 (45°)이 다르다 ·
    /// 캐시에 2만 장을 넣어도 32MB 안이고 가장 오래 안 쓴 것부터 나간다.
    /// </summary>
    private static void StackRules()
    {
        var t = Tables.Default;
        static int Opaque(Img im) { int n = 0; for (int i = 3; i < im.Px.Length; i += 4) if (im.Px[i] > 0) n++; return n; }
        var forms = new[] { DotForge.Make(t, "Table", Facing.S, 2, 2), DotForge.Make(t, "소화기"), DotForge.Make(t, "식량 상자"), DotForge.Make(t, "우주복") };
        // 32도트 (scale 1)와 넓은 보기 16도트 (scale 2) 둘 다 — 넓은 보기에서도 돌 때 크기가 줄었다 늘었다 하지 않는다
        foreach (int sc in new[] { 1, 2 })
        {
            var spread = new List<string>(); bool ok = true, differ = true;
            foreach (var f in forms)
            {
                int n0 = Opaque(DotStack.Draw(f, 0, sc).Img), lo = n0, hi = n0;
                for (int k = 1; k < DotStack.Steps; k++) { int n = Opaque(DotStack.Draw(f, k, sc).Img); lo = Math.Min(lo, n); hi = Math.Max(hi, n); }
                ok &= n0 > 0 && lo >= 0.9 * n0 && hi <= 1.1 * n0;
                spread.Add($"{f.Name} {n0} ({(n0 > 0 ? (double)lo / n0 : 0):0.00}~{(n0 > 0 ? (double)hi / n0 : 0):0.00})");
                var a = DotStack.Draw(f, 0, sc).Img; var b = DotStack.Draw(f, 8, sc).Img;
                differ &= a.W != b.W || a.H != b.H || !a.Px.AsSpan().SequenceEqual(b.Px);
            }
            string at = sc == 1 ? "32도트" : "16도트 (넓은 보기)";
            Check($"쌓아 그린 꼴 {at} — 각 단 0~63의 불투명 도트 수가 단 0의 ±10% 안 (탁자 · 소화기 · 식량 상자 · 우주복)", ok, string.Join(" · ", spread));
            Check($"쌓아 그린 꼴 {at} — 단 0과 단 8 (45°)의 그림이 다르다 (넷 모두)", differ && ok);
            // 겉 색: 소화기는 빨간 몸 속에 검은 기둥 (꼭지 + 속 — 바닥부터 꼭대기까지)이 있다 — 어느 단에서도 속 기둥이 앞면을 칠하지 않는다
            static double Red(Img im) { int n = 0, r = 0; for (int i = 0; i < im.Px.Length; i += 4) if (im.Px[i + 3] > 0) { n++; if (im.Px[i] > 2 * im.Px[i + 1] && im.Px[i] > 60) r++; } return n > 0 ? (double)r / n : 0; }
            var ext = forms[1];
            double redLo = Enumerable.Range(0, DotStack.Steps).Min(k => Red(DotStack.Draw(ext, k, sc).Img));
            Check($"쌓아 그린 꼴 {at} — 소화기는 단마다 빨간 도트가 그림의 75% 넘게 (속의 검은 기둥이 앞면을 칠하지 않는다)", redLo > 0.75, $"가장 적은 단 {redLo:0.00}");
        }
        // 캐시: 64×64 그림 (16KB) 2만 장 = 320MB를 넣는다 — 32MB 안 · 오래 안 쓴 것부터 나간다 (가끔 꺼내 본 것은 남는다)
        var cache = new DotStackCache();
        var form = forms[0];
        DotStackCache.Key K(int i) => new(form, 0, 0, 0, (byte)(i & 63), 1, (uint)i);
        long peak = 0; bool keptTouched = true;
        for (int i = 0; i < 20000; i++)
        {
            cache.Put(K(i), new DotStack.Drawn(new Img(64, 64), 32, 48));
            if (i >= 100 && i % 100 == 0) keptTouched &= cache.TryGet(K(7), out _);   // 7번은 자주 꺼내 본다
            peak = Math.Max(peak, cache.Bytes);
        }
        bool newest = cache.TryGet(K(19999), out _) && cache.TryGet(K(19000), out _), oldest = !cache.TryGet(K(0), out _) && !cache.TryGet(K(8), out _) && !cache.TryGet(K(10000), out _);
        Check("쌓은 그림 캐시 — 2만 장 (320MB)을 넣어도 32MB 안 · 오래 안 쓴 것부터 나가고 자주 꺼낸 것 · 새것은 남는다", peak <= 32L << 20 && cache.Count > 1000 && newest && oldest && keptTouched,
            $"가장 많을 때 {peak / 1048576.0:0.0}MB · {cache.Count}장 · 새것 {(newest ? "있음" : "없음")} · 옛것 {(oldest ? "나감" : "남음")} · 자주 본 것 {(keptTouched ? "남음" : "나감")}");
    }
}
