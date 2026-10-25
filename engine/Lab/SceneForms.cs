using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;
using ShipView;

namespace Lab;

/// <summary>
/// 관찰 장면 '도는 도트' (docs/PHYSICS.md PH2 · `Lab --scene dotforms`): 꼴마다 왼쪽에 지금 그림 (새 가구 그림 · 옛 그림 · 바닥 물건은 PH4a부터 도트 꼴 그대로) ·
/// 오른쪽에 쌓아 그린 꼴 8각 (0 · 45 · 90 … 315°) — 줄마다 선 꼴 32도트 · 누운 꼴 32도트 · 선 꼴 16도트 · 누운 꼴 16도트. 가구는 1배 · 물건은 3배.
/// </summary>
public static partial class Program
{
    private static void DotFormsScene(string dir)
    {
        var t = Tables.Default;
        var bg = Rgba.Hex("#1c2027"); var line = Rgba.Hex("#2c323c");
        var blocks = new List<(Img now, DotForm up, DotForm lie, int zoom)>();
        foreach (var (type, bw, bh) in new[] { ("Table", 2, 2), ("Shelf", 2, 1), ("Bed", 2, 1), ("SuitLocker", 1, 1), ("Console", 1, 1), ("Seat", 1, 1) })
        {
            var now = ArtFurniture.Has(type) ? ArtFurniture.Body(type, bw, bh, Facing.S, new FurnFill { Ext = 1, Seal = 1, Suits = 2, Masks = 6, Owner = 3 }, 0)
                : DotMachines.Body(Enum.Parse<FurnitureType>(type), bw, bh, -1, 1, 0, 0);
            blocks.Add((now, DotForge.Make(t, type, Facing.S, bw, bh), DotForge.Make(t, type, Facing.S, bw, bh, 1), 1));
        }
        foreach (var name in new[] { "소화기", "식량 상자", "우주복", "공구 상자", "오물 통", "컵", "유리병", "접시", "책", "구급 상자" })
            blocks.Add((DotStack.Draw(DotForge.Make(t, name), 0).Img, DotForge.Make(t, name), DotForge.Make(t, name, lie: 1), 3));   // 물건의 지금 그림 = 도트 꼴 (PH4a — 옛 아이콘을 걷었다)
        // 블록마다 칸 크기 (그 꼴의 그림 32장 가운데 가장 큰 것 + 틈) · 4줄 × (지금 그림 + 8각)
        var laid = new List<(int x, int y, int cw, int ch, int i)>();
        int sheetW = 1720, cx0 = 0, cy0 = 0, rowH = 0;
        var pics = new List<DotStack.Drawn[]>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var (now, up, lie, z) = blocks[i];
            var set = new List<DotStack.Drawn>();
            foreach (var (f, sc) in new[] { (up, 1), (lie, 1), (up, 2), (lie, 2) })
                for (int a = 0; a < 8; a++) set.Add(DotStack.Draw(f, a * 8, sc));
            pics.Add(set.ToArray());
            int cw = (Math.Max(set.Max(d => d.Img.W), now.W) + 4) * z, ch = (Math.Max(set.Max(d => d.Img.H), now.H) + 4) * z;
            int bwid = cw * 9 + 8, bhgt = ch * 4 + 8;
            if (cx0 + bwid > sheetW) { cx0 = 0; cy0 += rowH; rowH = 0; }
            laid.Add((cx0, cy0, cw, ch, i));
            cx0 += bwid; rowH = Math.Max(rowH, bhgt);
        }
        var sheet = new Img(sheetW, cy0 + rowH);
        sheet.Fill(0, 0, sheet.W, sheet.H, bg);
        foreach (var (bx, by, cw, ch, i) in laid)
        {
            var (now, _, _, z) = blocks[i];
            var nowZ = z > 1 ? now.Scale(z) : now;
            sheet.Draw(nowZ, bx + 4 + (cw - nowZ.W) / 2, by + 4 + ch - nowZ.H - 2 * z);
            for (int k = 0; k < 4 * ch; k++) sheet.Set(bx + 4 + cw - 1, by + 4 + k, line);
            for (int r = 0; r < 4; r++)
                for (int a = 0; a < 8; a++)
                {
                    var d = pics[i][r * 8 + a];
                    var im = z > 1 ? d.Img.Scale(z) : d.Img;
                    // 칸 가운데 (그림 크기는 각 단마다 같아 무게중심 바닥 점이 제자리에 선다)
                    sheet.Draw(im, bx + 4 + cw * (a + 1) + (cw - im.W) / 2, by + 4 + ch * r + (ch - im.H) / 2);
                }
            for (int k = 0; k < 9 * cw; k++) sheet.Set(bx + 4 + k, by + 4 + 4 * ch, line);
        }
        Png.WriteRgba(Path.Combine(dir, "dotforms.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  dotforms: {dir}/dotforms.png ({sheet.W}×{sheet.H}) — 꼴마다 왼쪽 지금 그림 · 8각 × (선 · 누운) × (32 · 16도트)");
    }

    /// <summary>
    /// 관찰 장면 '미끄러짐' (docs/PHYSICS.md PH4a · `Lab --scene slide`): 낮은 중력 (0.1g — 시험 빠르기 3m/s 안팎의 미끄러짐이 몇 틱 이어지게) · 공기 없는 방에서
    /// 밀린 상자 · 소화기 · 공구 상자 · 책이 돌며 미끄러져 벽 · 닫힌 문 · 탁자에 튀고 마찰로 선다 — 틱마다 한 장 (메움은 PH5) · 위 방만 두 배로 여섯 장.
    /// </summary>
    private static void SlideScene(string dir)
    {
        const string art = "\n################\n#..............#\n#..........tt..#\n#..........tt..#\n#..............#\n#..............#\n#..............#\n#######D########\n#..............#\n################";
        var s = PropWorld(0.1f, 3, art, new Dictionary<char, string> { ['t'] = "Table" });
        Slide(s, "식량 상자", 3.0, 3.0, 3.0, 0.8, 2);
        Slide(s, "소화기", 4.0, 6.5, 2.5, -1.2, -3);
        Slide(s, "공구 상자", 8.0, 5.0, -2.0, 2.6, 1.5);
        Slide(s, "책", 12.5, 6.8, -3.0, -0.5, 4);
        Slide(s, "식량 상자", 14.0, 2.5, -1.0, 3.0, -1);
        var painter = new Painter(s.Layout);
        const int frames = 6, cols = 2, zoom = 2;
        int fw = 16 * Painter.T * zoom, fh = 8 * Painter.T * zoom, gap = 6;   // 위 방 (벽 포함 16 × 8칸)만 두 배로
        var sheet = new Img(cols * fw + (cols - 1) * gap, (frames + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        var st = s.Get<Structure>();
        for (int k = 0; k < frames; k++)
        {
            var img = painter.Frame(s, 1, 1, 16, 8, k).Scale(zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            int awake = st.Props.Count(p => Structure.Awake(p));
            Console.WriteLine($"  slide {k}: {s.Tick * Time.SecondsPerTick:0.0}초 · 깨어 있음 {awake} · 튐 {st.Bounces} · 잔걸음 {st.PropSteps}");
            s.Step();
        }
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "slide.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  slide: {dir}/slide.png ({sheet.W}×{sheet.H}) — 틱마다 한 장 (0.1g · 공기 없음 · 밀린 물건 다섯)");
    }

    /// <summary>
    /// 관찰 장면 '더미' (docs/PHYSICS.md PH6a · `Lab --scene pile`): 1g 마른 바닥 · 공기 없는 방 · 몸이 없는 배가 0.45g로 비스듬히 점화한다 —
    /// 바닥 물건 열둘이 엔진 반대쪽 (남동) 구석으로 미끄러져 서로 부딪히고 · 밀고 · 돌며 쌓인다 (지나가지 않는다 · 겹침 ≤ 1도트) — 틱 0 (방 두 배) · 1 · 2 · 3 · 5 · 8 (남동 구석 네 배).
    /// </summary>
    private static void PileScene(string dir)
    {
        const string art = "\n##############\n#............#\n#............#\n#............#\n#............#\n#............#\n#............#\n##############";
        var s = PropWorld(1, 4, art);
        var st = s.Get<Structure>();
        string[] names = { "식량 상자", "공구 상자", "수리 꾸러미", "소화기", "책", "식량 상자" };
        var ids = new List<int>();
        for (int k = 0; k < 12; k++) ids.Add(Slide(s, names[k % names.Length], 3.0 + k % 4 * 1.9, 2.9 + k / 4 * 1.6, 0, 0, 0));
        for (int k = 0; k < ids.Count; k++) { int i = st.IndexOf(ids[k]); var p = st.Props[i]; p.Th = 0.4 * k; st.Props[i] = p; }
        st.ShipAx = -0.45 * 9.8 * 0.8; st.ShipAy = -0.45 * 9.8 * 0.6;   // 엔진은 북서 — 바닥의 것은 남동으로 밀린다
        var painter = new Painter(s.Layout);
        const int cols = 2, zoom = 2;
        int[] at = { 0, 1, 2, 3, 5, 8 };
        int fw = 14 * Painter.T * zoom, fh = 8 * Painter.T * zoom, gap = 6;
        var sheet = new Img(cols * fw + (cols - 1) * gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int done = 0;
        double worst = 0;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k]) { s.Step(); done++; worst = Math.Max(worst, WorstPairOverlap(st, ids)); }
            var img = k == 0 ? painter.Frame(s, 1, 1, 14, 8, done).Scale(zoom) : painter.Frame(s, 8, 5, 7, 4, done).Scale(2 * zoom);   // 처음 장은 방 · 뒤는 남동 구석 (네 배)
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            int awake = st.Props.Count(p => Structure.Awake(p));
            Console.WriteLine($"  pile {done}틱: 깨어 있음 {awake} · 물체끼리 {st.PairHits} · 튐 {st.Bounces} · 가장 깊은 겹침 {WorstPairOverlap(st, ids) / DotForm.Dot:0.00}도트 · 섬 놓침 {st.IslandMiss}");
        }
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "pile.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  pile: {dir}/pile.png ({sheet.W}×{sheet.H}) — 0.45g 비스듬한 점화 · 물건 열둘이 남동 구석에 쌓인다 · 8틱 동안 가장 깊은 겹침 {worst / DotForm.Dot:0.00}도트");
    }

    /// <summary>
    /// kick 틀 (PH6b — docs/PHYSICS.md §7 · §10 '사람'): 1g 마른 바닥 — 걷는 이 (1.7m/s 남짓 — 틱에 두 칸)가 지나가다 식량 상자 · 컵을 차고 ·
    /// 로봇 (틱에 한 칸)이 공구 상자를 밀고 · 선 이가 소화기를 내려놓는다 (원판 바깥 · 앞에 — 놓은 이는 그것을 차지 않는다). 원판 (R 0.25m)은 둥근 빛으로 덧그린다 (④만 있는 세계 — 사람 그림은 없다).
    /// </summary>
    private static void KickScene(string dir)
    {
        const string art = "\n##############\n#............#\n#............#\n#............#\n#............#\n#............#\n#............#\n##############";
        var s = PropWorld(1, 6, art);
        var st = s.Get<Structure>(); var L = s.Layout;
        int C(int x, int y) => L.Index(x, y);
        int food = Slide(s, "식량 상자", 6.5, 3.5, 0, 0), cup = Slide(s, "컵", 9.5, 3.55, 0, 0), tools = Slide(s, "공구 상자", 5.75, 6.5, 0, 0);
        var start = new Dictionary<int, (double x, double y)>();
        foreach (int id in new[] { food, cup, tools }) { var p = st.Props[st.IndexOf(id)]; start[id] = (p.X, p.Y); }
        int Xa(int t) => Math.Min(13, 2 + 2 * Math.Max(0, t));   // 걷는 이: 틱에 두 칸
        int Xr(int t) => Math.Min(13, 2 + Math.Max(0, t));       // 로봇: 틱에 한 칸
        int[] Line(int x0, int x1, int y) => Enumerable.Range(x0, x1 - x0 + 1).Select(x => C(x, y)).ToArray();
        (double x, double y, Rgba c)[] discs = Array.Empty<(double, double, Rgba)>();
        int dropped = 0;
        void Put(int t)
        {
            var pa = t > 0 && Xa(t) > Xa(t - 1) ? Line(Xa(t - 1), Xa(t), 3) : Array.Empty<int>();
            var pr = t > 0 && Xr(t) > Xr(t - 1) ? Line(Xr(t - 1), Xr(t), 6) : Array.Empty<int>();
            var acts = new[]
            {
                new PropActor { Tag = 0, Cell = C(Xa(t), 3), R = Structure.ActorR, Kg = 70, H = 1.7f, PathAt = 0, PathN = (byte)pa.Length },
                new PropActor { Tag = -1, Cell = C(Xr(t), 6), R = Structure.ActorR, Kg = 120, H = 1.2f, PathAt = (short)pa.Length, PathN = (byte)pr.Length },
                new PropActor { Tag = 2, Cell = C(12, 5), R = Structure.ActorR, Kg = 70, H = 1.7f },
            };
            st.SetActors(acts, pa.Concat(pr).ToArray());
            discs = new[] { (Xa(t) + 0.5, 3.5, Rgba.Hex("#f2c14e")), (Xr(t) + 0.5, 6.5, Rgba.Hex("#5ec8e5")), (12.5, 5.5, Rgba.Hex("#f2c14e")) };
        }
        var painter = new Painter(s.Layout);
        const int cols = 2, zoom = 2;
        int[] at = { 0, 2, 3, 4, 6, 10 };
        int fw = 14 * Painter.T * zoom, fh = 8 * Painter.T * zoom, gap = 6;
        var sheet = new Img(cols * fw + (cols - 1) * gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int done = 0;
        Put(0);
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k])
            {
                done++; Put(done);
                if (done == 3) dropped = st.DropItem(s.Tables.People.Items.Keys.ToList().IndexOf("소화기"), 0, 12.5, 5.5, 0, 0, 0, s.Causes.Add(CauseKind.Setup, "선 이가 소화기를 내려놓았다", s.Tick), 2, -1, 0);
                s.Step();
            }
            var img = painter.Frame(s, 1, 1, 14, 8, done).Scale(zoom);
            foreach (var (x, y, c) in discs) img.Oval((float)((x - 1) * Painter.T * zoom), (float)((y - 1) * Painter.T * zoom), (float)(Structure.ActorR * Painter.T * zoom), (float)(Structure.ActorR * Painter.T * zoom), c, 0.45f, true);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            string Moved(int id) { var p = st.Props[st.IndexOf(id)]; return $"{Math.Sqrt((p.X - start[id].x) * (p.X - start[id].x) + (p.Y - start[id].y) * (p.Y - start[id].y)):0.00}m"; }
            string drop = dropped > 0 ? $" · 소화기 원판 가운데에서 {Math.Sqrt(Math.Pow(st.Props[st.IndexOf(dropped)].X - 12.5, 2) + Math.Pow(st.Props[st.IndexOf(dropped)].Y - 5.5, 2)):0.00}m" : "";
            Console.WriteLine($"  kick {done}틱: 식량 상자 {Moved(food)} · 컵 {Moved(cup)} · 공구 상자 {Moved(tools)}{drop} · 깨어 있음 {st.Props.Count(p => Structure.Awake(p))}");
        }
        var fp = st.Props[st.IndexOf(food)];
        Console.WriteLine($"  kick: 식량 상자의 까닭 \"{s.Causes.Story(fp.Cause)}\"");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "kick.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  kick: {dir}/kick.png ({sheet.W}×{sheet.H}) — 걷는 이가 식량 상자 · 컵을 차고 · 로봇이 공구 상자를 밀고 · 선 이가 소화기를 원판 바깥에 놓는다");
    }

    /// <summary>
    /// props-breach 틀 (PH4c — 함장 장면 '부엌 감압'의 틀 · docs/PHYSICS.md §10): 부엌 외판에 0.5m² 구멍 — 바닥의 식량 상자 · 수리 꾸러미 · 공구 상자 · 컵 · 접시 · 책 · 소화기가
    /// 구멍 입 바람에 돌며 미끄러지고 · 구멍보다 큰 것은 빨려 붙어 막고 · 작은 것은 우주로 나간다. 감압 앞 한 장 + 첫 틱 · 둘째 · 넷째 틱 (틱 안 메움은 PH5).
    /// </summary>
    private static void PropsBreachScene(string dir)
    {
        const string art = "\n##############\n#....ss......#\n#..........tt#\n#..........tt#\n#............#\n#.....f......#\n######D#######\n#............#\n##############";
        var lay = Worlds.Ascii(art, new Dictionary<char, string> { ['t'] = "Table", ['s'] = "Stove", ['f'] = "Fridge" });
        var s = Worlds.Full(lay, 5);
        var st = s.Get<Structure>(); var f = s.Get<Field>();
        int At(int x, int y) => Worlds.At(lay, x, y);
        st.RequestDoor(lay.DoorAt[At(6, 6)], 0);
        (string name, double x, double y, double th)[] put =
        {
            ("식량 상자", 1.55, 3.05, 0.3), ("수리 꾸러미", 1.6, 3.75, 1.0), ("공구 상자", 1.5, 4.45, 0.5), ("컵", 2.45, 3.5, 0), ("책", 2.5, 2.55, 0.8),
            ("접시", 3.4, 3.4, 0), ("물병", 2.6, 4.5, 0), ("식량 상자", 4.2, 5.0, -0.2), ("소화기", 9.0, 4.4, 0), ("컵", 8.2, 3.2, 0),
        };
        foreach (var (name, x, y, th) in put)
        {
            int id = Worlds.PutItem(s, name, lay.Index((int)x + 1, (int)y + 1));
            int i = st.IndexOf(id); var p = st.Props[i]; p.X = x + 1; p.Y = y + 1; p.Th = th; st.Props[i] = p;
        }
        s.Run(3);
        int hole = At(0, 3);
        st.DamageCause[hole] = s.Causes.Add(CauseKind.Setup, "부엌 외판에 파편이 박혔다", s.Tick); f.SetHole(hole, 0.5f);
        var painter = new Painter(s.Layout);
        const int cols = 2, zoom = 2;
        int[] at = { 0, 1, 2, 4 };
        int fw = 14 * Painter.T * zoom, fh = 7 * Painter.T * zoom, gap = 6;   // 부엌 (벽 포함 14 × 7칸)만 두 배로
        var sheet = new Img(cols * fw + (cols - 1) * gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int done = 0;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k]) { s.Step(); done++; }
            var img = painter.Frame(s, 1, 1, 14, 7, done).Scale(zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            int awake = st.Props.Count(p => Structure.Awake(p)), plugged = st.Props.Count(p => p.State == 6);
            if (k == 1) Console.WriteLine($"  구멍 입 {f.HoleMouthPa(At(1, 3), out _):0}Pa · 돌풍 (1,3) {f.WindPa[At(1, 3)]:0} · (2,3) {f.WindPa[At(2, 3)]:0} · (1,2) {f.WindPa[At(1, 2)]:0} · (3,3) {f.WindPa[At(3, 3)]:0}Pa");
            Console.WriteLine($"  props-breach {done}틱: 부엌 {f.Pressure(At(3, 3)):0}kPa · 바닥 물체 {st.Props.Count} · 깨어 있음 {awake} · 막음 {plugged} · 우주로 {st.PropSpaced} · 튐 {st.Bounces}");
        }
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "props-breach.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  props-breach: {dir}/props-breach.png ({sheet.W}×{sheet.H}) — 감압 앞 · 첫 틱 · 둘째 · 넷째 틱 (외판 0.5m² 구멍 · 물건 열)");
    }
    /// <summary>
    /// props-burn 틀 (PH4d — 함장 장면 '화살호 추진제 반 0.40g'의 틀 · docs/PHYSICS.md §10): 몸이 없는 배가 0.40g로 점화한다 (엔진은 +x로 민다 — 배 안의 것은 −x로 −m·a) —
    /// 마른 바닥 식량 상자 (μs 0.375)도 미끄러져 뒤 벽에 닿아 서고 · 젖은 바닥의 것은 더 빨리 · 소화기는 깨지만 (넘어짐 문턱) 정지 마찰이 붙든다 (넘어짐 · 구름 · 탁자 끝 병 낙하는 PH7) ·
    /// 탁자 · 의자 곁 것은 가구에 걸린다. 점화 앞 한 장 + 첫 틱 · 둘째 · 넷째 틱 (틱 안 메움은 PH5).
    /// </summary>
    private static void PropsBurnScene(string dir)
    {
        const string art = "\n################\n#..............#\n#....tt........#\n#....tt........#\n#..............#\n#..........a...#\n#..............#\n################";
        var s = PropWorld(1f, 7, art, new Dictionary<char, string> { ['t'] = "Table", ['a'] = "Seat" });
        var st = s.Get<Structure>(); var f = s.Get<Field>();
        (string name, double x, double y, double th)[] put =
        {
            ("식량 상자", 9.5, 2.6, 0.2), ("식량 상자", 12.5, 5.5, -0.4), ("소화기", 13.6, 2.6, 0), ("공구 상자", 10.5, 4.4, 0.6), ("컵", 8.5, 3.6, 0),
            ("접시", 11.6, 6.4, 0), ("책", 14.2, 4.0, 1.1), ("유리병", 7.4, 2.4, 0), ("물병", 9.6, 6.6, 0), ("우주복", 12.6, 6.4, 0.3),
        };
        foreach (var (name, x, y, th) in put)
        {
            int id = Slide(s, name, x + 1, y + 1, 0, 0);
            int i = st.IndexOf(id); var p = st.Props[i]; p.Th = th; st.Props[i] = p;
        }
        for (int x = 11; x <= 14; x++) f.AddStore(s.Layout.Index(x, 6), f.T.sWater, 300);   // 젖은 바닥 한 줄
        var painter = new Painter(s.Layout);
        const int cols = 2, zoom = 2;
        int[] at = { 0, 1, 2, 4 };
        int fw = 16 * Painter.T * zoom, fh = 8 * Painter.T * zoom, gap = 6;
        var sheet = new Img(cols * fw + (cols - 1) * gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int done = 0;
        st.ShipAx = 0.4 * 9.8;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k]) { s.Step(); done++; }
            var img = painter.Frame(s, 1, 1, 16, 8, done).Scale(zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            int awake = st.Props.Count(p => Structure.Awake(p));
            double back = st.Props.Sum(p => Math.Max(0, put[st.Props.IndexOf(p)].x + 1 - p.X));
            Console.WriteLine($"  props-burn {done}틱: 0.40g · 깨어 있음 {awake} · 깬 수 {st.PropWoken} · 튐 {st.Bounces} · 뒤로 간 길 합 {back:0.0}m");
        }
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "props-burn.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  props-burn: {dir}/props-burn.png ({sheet.W}×{sheet.H}) — 점화 앞 · 첫 틱 · 둘째 · 넷째 틱 (0.40g · 물건 열 · 젖은 바닥 한 줄)");
    }

    /// <summary>
    /// 장면 한 장 (관찰 틀 — 그 때 f의 틱 안 기록대로): 그림판에 지난 틱 기록 f를 넣어 그린다 (기록이 없으면 지금 자세) · 확대.
    /// </summary>
    private static Img SceneFrame(Painter painter, Sim s, double f, int x, int y, int w, int h, int frame, int zoom)
    {
        TrackPlay.Apply(painter, s, f);
        return painter.Frame(s, x, y, w, h, frame).Scale(zoom);
    }

    /// <summary>
    /// topple 틀 (PH7 — docs/PHYSICS.md §3-3 · §3-4): 몸이 없는 배가 0.40g로 점화 (틀의 힘은 서쪽) — 소화기 · 물병은 넘어져 눕고 · 식량 상자는 미끄러지고 · 공구 상자 · 탁자 위 컵은 그대로 ·
    /// 탁자 위에 누운 유리병은 굴러 끝에서 바닥으로 떨어진다. 점화 앞 · 첫 틱 안 1/8 · 2/8 · 3/8 · 4/8 · 첫 틱 끝 · 둘째 틱 끝 (틱 안 기록으로 메운 때).
    /// </summary>
    private static void ToppleScene(string dir)
    {
        const string art = "\n################\n#..............#\n#....tt........#\n#....tt........#\n#..............#\n#..............#\n#..............#\n################";
        var s = PropWorld(1f, 12, art, new Dictionary<char, string> { ['t'] = "Table" });
        var st = s.Get<Structure>();
        st.Track = new MotionTrack();
        int ext = Slide(s, "소화기", 12.5, 3.5, 0, 0), water = Slide(s, "물병", 10.5, 6.0, 0, 0), box = Slide(s, "식량 상자", 13.5, 6.5, 0, 0), tools = Slide(s, "공구 상자", 9.5, 4.5, 0, 0);
        int cup = Slide(s, "컵", 7.4, 3.6, 0, 0), bottle = Slide(s, "유리병", 7.0, 4.4, 0, 0);
        { int i = st.IndexOf(bottle); var p = st.Props[i]; p.Pose = 2; st.Props[i] = p; }   // 누운 병 (축이 남북 — 점화 쪽으로 구른다)
        s.Step();
        var painter = new Painter(s.Layout);
        const int cols = 2, zoom = 2;
        (int tick, double f)[] at = { (1, 1), (2, 0.125), (2, 0.25), (2, 0.375), (2, 0.5), (2, 1), (3, 1), (4, 1) };
        int fw = 16 * Painter.T * zoom, fh = 8 * Painter.T * zoom, gap = 6;
        var sheet = new Img(cols * fw + (cols - 1) * gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int done = 1;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k].tick) { if (done == 1) st.ShipAx = 0.4 * 9.8; s.Step(); done++; }
            var img = SceneFrame(painter, s, at[k].f, 1, 1, 16, 8, done * 8 + k, zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            string Of(int id) { if (st.IndexOf(id) < 0) return "깨졌다"; var p = st.Props[st.IndexOf(id)]; return $"({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}) 누움 {p.Pose} 상태 {p.State}"; }
            Console.WriteLine($"  topple {done - 1}틱 + {at[k].f:0.000}: 소화기 {Of(ext)} · 물병 {Of(water)} · 유리병 {Of(bottle)} · 넘어짐 {st.Toppled}");
        }
        Console.WriteLine($"  topple: 식량 상자 Δx {st.Props[st.IndexOf(box)].X - 13.5:0.00}m · 공구 상자 Δx {st.Props[st.IndexOf(tools)].X - 9.5:0.00}m · 컵 {(st.IndexOf(cup) >= 0 ? $"Δx {st.Props[st.IndexOf(cup)].X - 7.4:0.00}m" : "깨졌다")} · 사실 {string.Join(" · ", s.Facts.Since(0).Where(f => f.Kind is FactKind.Toppled or FactKind.ItemFell).Select(f => s.Causes.Text(f.Cause) is { Length: > 0 } c ? $"{f.Kind} {c}" : f.Kind.ToString()))}");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "topple.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  topple: {dir}/topple.png ({sheet.W}×{sheet.H}) — 0.40g 점화: 소화기 · 물병이 눕고 · 상자가 미끄러지고 · 탁자 위 누운 유리병이 굴러 떨어진다 (첫 틱 안 네 장)");
    }

    /// <summary>
    /// props-zerog 틀 (PH7 — docs/PHYSICS.md §3-3 무중력): 놓인 물건 여덟 · 통풍구 (공조 바람 0.5 · 곁 0.25m/s) — 중력이 꺼지면 조금 들떠 떠오르고 · 공조 바람에 흘러가고 · 벽에 부딪히면 위아래로도 튄다 ·
    /// 일곱째 틱에 중력이 돌아오면 그 높이에서 떨어진다 (그림자 · 들어 올림). 꺼지기 앞 · 첫 · 셋째 · 여섯째 틱 끝 · 돌아온 틱 안 0.15 · 0.3 · 0.6초 · 다음 틱.
    /// </summary>
    private static void PropsZeroGScene(string dir)
    {
        const string art = "\n##############\n#............#\n#............#\n#............#\n#............#\n#............#\n#............#\n##############";
        var s = PropWorld(1f, 13, art);
        var st = s.Get<Structure>(); var f = s.Get<Field>(); var L = s.Layout;
        st.Track = new MotionTrack();
        (string name, double x, double y)[] put = { ("식량 상자", 4.5, 3.5), ("공구 상자", 6.5, 3.2), ("컵", 7.5, 4.5), ("책", 10.5, 3.5), ("수리 꾸러미", 4.5, 6.5), ("접시", 6.8, 6.3), ("물병", 9.0, 6.6), ("구급 상자", 11.5, 6.4) };
        (int k, double vx, double vy)[] nudge = { (0, 1.2, 0), (1, 0, 0.8), (3, -1.0, 0.3), (5, 0.2, -0.9) };   // 중력이 꺼질 때 곁을 지나던 이가 건드린 것처럼
        var ids = put.Select(q => Slide(s, q.name, q.x, q.y, 0, 0)).ToArray();
        int vent = L.Index(7, 5);
        void Vent() { f.Airflow[vent] = 0.5f; Span<int> nb = stackalloc int[4]; L.Neighbors(vent, nb); foreach (int j in nb) if (j >= 0) f.Airflow[j] = 0.25f; }
        Vent();
        s.Step();
        var painter = new Painter(s.Layout);
        const int cols = 2, zoom = 2;
        (int tick, double f)[] at = { (1, 1), (2, 1), (4, 1), (7, 1), (8, 0.0625), (8, 0.125), (8, 0.25), (9, 1) };
        int fw = 14 * Painter.T * zoom, fh = 8 * Painter.T * zoom, gap = 6;
        var sheet = new Img(cols * fw + (cols - 1) * gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int done = 1;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k].tick)
            {
                if (done == 1) { Array.Fill(f.Grav, 0f); s.Step(); foreach (var (k2, vx, vy) in nudge) { var q = st.Props[st.IndexOf(ids[k2])]; st.Shove(ids[k2], q.Kg * vx, q.Kg * vy); } done++; continue; }   // 중력이 꺼진다 (첫 틱에 들뜬다) · 넷은 건드려 벽에 부딪힌다
                if (done == 7) Array.Fill(f.Grav, 1f);                                     // 중력이 돌아온다
                Vent(); s.Step(); done++;
            }
            var img = SceneFrame(painter, s, at[k].f, 1, 1, 14, 8, done * 8 + k, zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            Console.WriteLine($"  props-zerog {done - 1}틱 + {at[k].f:0.00}: 중력 {f.Grav[L.Index(6, 4)]:0} · 높이 {string.Join(" ", ids.Select(id => st.IndexOf(id) >= 0 ? st.Props[st.IndexOf(id)].Z.ToString("0.00") : "깨짐"))} · 떠 있음 {st.Props.Count(p => p.State == 4)}");
        }
        Console.WriteLine($"  props-zerog: 사실 {string.Join(" · ", s.Facts.Since(0).Where(x => x.Kind == FactKind.ItemFell).Select(x => $"떨어졌다 {x.Size:0.00}m"))}");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "props-zerog.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  props-zerog: {dir}/props-zerog.png ({sheet.W}×{sheet.H}) — 중력이 꺼져 떠오르고 · 공조 바람에 흘러가고 · 돌아오면 떨어진다");
    }

    /// <summary>
    /// shatter 틀 (PH8 — docs/PHYSICS.md §5 · §6-2 깨진 면): 공기가 있는 방 · 탁자 위 끝의 물 든 유리컵 · 접시 · 누운 유리병을 동쪽으로 밀어 떨군다 —
    /// 0.75m에서 강철 바닥에 닿아 깨지고 (가면 조각 · 깨진 면 반짝임 · 3도트 아래는 가루 · 담긴 물은 바닥으로) · 다음 틱에 조각이 흩어진다.
    /// 탁자 위 · 떨어지는 · 닿는 · 깨진 · 흩어지는 · 선 장.
    /// </summary>
    private static void ShatterScene(string dir)
    {
        const string art = "\n##############\n#............#\n#..tt........#\n#..tt........#\n#............#\n#............#\n##############";
        var lay = Worlds.Ascii(art, new Dictionary<char, string> { ['t'] = "Table" });
        var s = FieldStructure(lay, 21);
        var st = s.Get<Structure>(); var f = s.Get<Field>();
        st.Track = new MotionTrack();
        int Put(string name, double x, double y, float liquid = 0)
        {
            int item = s.Tables.People.Items.Keys.ToList().IndexOf(name);
            int id = st.PutItem(item, 0, s.Layout.Index((int)x, (int)y), 0, liquid);
            int i = st.IndexOf(id); var p = st.Props[i]; p.X = x; p.Y = y; st.Props[i] = p;
            return id;
        }
        int cup = Put("컵", 5.85, 3.6, 11), plate = Put("접시", 5.75, 4.35), bottle = Put("유리병", 5.7, 4.85);
        { int i = st.IndexOf(bottle); var p = st.Props[i]; p.Pose = 1; st.Props[i] = p; }   // 누운 병 (축이 동서)
        s.Step();
        foreach (var (id, v) in new[] { (cup, 1.6), (plate, 1.9), (bottle, 2.2) }) { var q = st.Props[st.IndexOf(id)]; var fm = st.PropForm(q); st.Shove(id, fm.Mass * v, 0); }
        var painter = new Painter(s.Layout);
        const int cols = 2, zoom = 3;
        (int tick, double f)[] at = { (1, 1), (2, 0.1), (2, 0.2), (2, 0.3), (2, 1), (3, 0.15), (3, 0.4), (4, 1) };
        int fw = 9 * Painter.T * zoom, fh = 5 * Painter.T * zoom, gap = 6;
        var sheet = new Img(cols * fw + (cols - 1) * gap, (at.Length + cols - 1) / cols * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int done = 1;
        for (int k = 0; k < at.Length; k++)
        {
            while (done < at[k].tick) { s.Step(); done++; }
            var img = SceneFrame(painter, s, at[k].f, 2, 2, 9, 5, done * 8 + k, zoom);
            sheet.Draw(img, k % cols * (fw + gap), k / cols * (fh + gap));
            Console.WriteLine($"  shatter {done - 1}틱 + {at[k].f:0.00}: 물체 {st.Props.Count} (조각 {st.Props.Count(p => p.Kind == 2)}) · 깨짐 {st.Shattered} · 가루 {st.GritKg * 1000:0}g · 바닥 물 {Enumerable.Range(0, lay.N).Sum(c => f.Stored(f.T.sWater, c)):0.0}몰");
        }
        var snd = s.Find<Sound>();
        Console.WriteLine($"  shatter: 사실 {string.Join(" · ", s.Facts.Since(0).Where(x => x.Kind is FactKind.Shattered or FactKind.ItemFell).Select(x => x.Kind == FactKind.Shattered ? s.Causes.Text(x.Cause) : $"떨어졌다 {x.Size:0.00}m"))}");
        Console.WriteLine($"  shatter: 원인 있는 고인 물 칸 {Enumerable.Range(0, lay.N).Count(c => f.Stored(f.T.sWater, c) > 1 && f.WetCause[c] != 0)} · 가루 칸 {st.Grit.Count(g => g > 0)}");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "shatter.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  shatter: {dir}/shatter.png ({sheet.W}×{sheet.H}) — 탁자 끝에서 밀려 떨어진 유리컵 · 접시 · 유리병이 깨지고 · 다음 틱에 조각이 흩어진다");
    }
}
