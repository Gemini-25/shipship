using ShipEngine;

namespace Lab;

/// <summary>빠른 길 (최적화 — 지문 같게): 빨리 셈하는 길이 옛 셈과 같은 답을 내는지 바로 견준다.</summary>
public static partial class Program
{
    private static void SpeedRules()
    {
        if (!Do("빠른 길")) return;
        Console.WriteLine("\n[빠른 길 — 같은 답]");

        // 구역 · 틈 곁 칸: 문을 마구 여닫고 벽을 뚫었다 막은 새터호 — 지금 구역 · 틈 곁 칸이 넓이 우선으로 처음부터 센 것과 같고 · 기억 없이 되살린 세계와도 같다
        {
            var lay = Layout.Ship("Saetteul", Tables.Default);
            var s = Worlds.FieldOnly(lay, 1);
            var f = s.Get<Field>();
            var rng = Rng.Stream(1, "빠른 길");
            int wall = Enumerable.Range(1, lay.N - 2).First(c => lay.Kind[c] == CellKind.Wall && lay.X(c) > 0 && lay.X(c) < lay.W - 1 && lay.Kind[c - 1] == CellKind.Floor && lay.Kind[c + 1] == CellKind.Floor);   // 두 방 사이 벽
            for (int k = 0; k < 160; k++)
            {
                for (int j = 0; j < 3; j++) { int d = rng.Range(0, lay.Doors.Count); f.SetDoor(d, f.DoorOpen[d] > 0 ? 0f : rng.Chance(0.3) ? 0.3f : 1f); }
                if (k == 40) f.SetHole(wall, 2.5f);
                if (k == 90) f.SetHole(wall, 0.02f);
                s.Step();
            }
            f.RebuildNow();

            // 옛 셈: 칸 번호 차례로 넓이 우선 채우기 · 줄 세우기 / 틈 곁 칸은 사전으로 거리를 잰다
            var regOf = Enumerable.Repeat(-1, lay.N).ToArray();
            var regs = new List<int[]>();
            Span<int> nb = stackalloc int[4];
            for (int i = 0; i < lay.N; i++)
            {
                if (regOf[i] >= 0 || !f.Passable(i)) continue;
                var list = new List<int> { i }; regOf[i] = regs.Count;
                for (int h = 0; h < list.Count; h++)
                {
                    lay.Neighbors(list[h], nb);
                    foreach (int j in nb) if (j >= 0 && regOf[j] < 0 && f.Passable(j)) { regOf[j] = regs.Count; list.Add(j); }
                }
                list.Sort(); regs.Add(list.ToArray());
            }
            (int[], double[]) Near(int start, int region)
            {
                var cells = new List<int>(); var ws = new List<double>();
                var dist = new Dictionary<int, int> { [start] = 0 };
                var q = new Queue<int>(); q.Enqueue(start);
                Span<int> n4 = stackalloc int[4];
                while (q.Count > 0)
                {
                    int c = q.Dequeue(); int d = dist[c];
                    cells.Add(c); ws.Add(Math.Exp(-d / 2.0));
                    if (d >= 6) continue;
                    lay.Neighbors(c, n4);
                    foreach (int j in n4) if (j >= 0 && regOf[j] == region && !dist.ContainsKey(j)) { dist[j] = d + 1; q.Enqueue(j); }
                }
                return (cells.ToArray(), ws.ToArray());
            }
            bool regionsSame = f.RegionOf.AsSpan().SequenceEqual(regOf) && f.Regions.Count == regs.Count && f.Regions.Zip(regs).All(p => p.First.AsSpan().SequenceEqual(p.Second));
            int nearBad = 0;
            foreach (var k in f.Links)
            {
                var (ca, wa) = Near(k.A, k.RA);
                if (!k.NearA.AsSpan().SequenceEqual(ca) || !k.WA.AsSpan().SequenceEqual(wa)) nearBad++;
                if (k.B >= 0) { var (cb, wb) = Near(k.B, k.RB); if (!k.NearB.AsSpan().SequenceEqual(cb) || !k.WB.AsSpan().SequenceEqual(wb)) nearBad++; }
            }
            // 기억 없이 되살린 세계 (구역 · 틈을 처음부터 짓는다)
            var r = Worlds.FieldOnly(Layout.Ship("Saetteul", Tables.Default), 1);
            r.Load(s.Save());
            var g = r.Get<Field>();
            bool linksSame = g.Links.Count == f.Links.Count && g.Links.Zip(f.Links).All(p => p.First.A == p.Second.A && p.First.B == p.Second.B && p.First.RA == p.Second.RA && p.First.RB == p.Second.RB
                && p.First.Kind == p.Second.Kind && p.First.Area == p.Second.Area && p.First.NearA.AsSpan().SequenceEqual(p.Second.NearA) && p.First.WA.AsSpan().SequenceEqual(p.Second.WA)
                && p.First.NearB.AsSpan().SequenceEqual(p.Second.NearB) && p.First.WB.AsSpan().SequenceEqual(p.Second.WB));
            Check("구역 · 틈 곁 칸: 문 480번 여닫고 벽을 뚫었다 막은 새터호 — 구역이 넓이 우선으로 처음부터 센 것과 같고 · 틈 곁 칸이 옛 셈과 같고 · 기억 없이 되살린 세계의 틈과도 같다",
                regionsSame && nearBad == 0 && linksSame && f.Links.Count > 0,
                $"구역 {f.Regions.Count} (옛 셈 {regs.Count}) 같음 {regionsSame} · 틈 {f.Links.Count} · 곁 칸 다름 {nearBad} · 되살린 세계 같음 {linksSame}");
        }

        // 방사선 가림: 벽 하나를 크게 뚫은 새터호 — 칸마다 선량이 옛 셈(광선 다섯을 모두 반 칸씩 걷기 · 칸마다 재질을 그때 찾기)과 같다
        {
            var s = ShipWorld.Create("Saetteul", 3);
            var f = s.Get<Field>(); var o = s.Get<Outside>(); var lay = s.Layout; var t = s.Tables;
            int wall = Enumerable.Range(1, lay.N - 2).First(c => lay.Kind[c] == CellKind.Wall && lay.X(c) > 0 && lay.X(c) < lay.W - 1 && lay.Kind[c - 1] == CellKind.Floor && lay.Kind[c + 1] == CellKind.Floor);
            f.SetHole(wall, 2.5f);
            s.Step();   // 처음 틱에 가림을 센다
            double Old(int i, double dx, double dy)
            {
                double x = lay.X(i) + 0.5, y = lay.Y(i) + 0.5, sum = 0;
                int last = i;
                for (int st = 0; st < 400; st++)
                {
                    x += dx * 0.5; y += dy * 0.5;
                    int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
                    if (!lay.In(ix, iy)) break;
                    int c = lay.Index(ix, iy);
                    if (c == last) continue;
                    last = c;
                    var kind = lay.Kind[c];
                    if (kind == CellKind.Outside) break;
                    var m = t.Materials[lay.Material[c]].Def;
                    if (kind == CellKind.Wall) sum += m.Density * (lay.Hull[c] ? 0.010 : 0.004) * (f.Hole[c] >= 2 ? 0 : 1);
                    if (lay.FurnitureAt[c] >= 0) sum += f.Cs[c] / 500.0;
                    sum += f.Stored(t.sWater, c) * 0.018;
                }
                return sum;
            }
            double sx = Math.Cos(o.SunAngle), sy = Math.Sin(o.SunAngle);
            int bad = 0, cells = 0;
            for (int i = 0; i < lay.N; i++)
            {
                float shield = 0;
                if (lay.IsSpace(i))
                {
                    cells++;
                    double sun = Old(i, sx, sy), avg = (Old(i, 1, 0) + Old(i, -1, 0) + Old(i, 0, 1) + Old(i, 0, -1)) / 4;
                    shield = (float)(0.5 * Math.Exp(-sun / 300) + 0.5 * Math.Exp(-avg / 300));
                }
                if ((float)(o.DoseOut * shield) != o.Dose[i]) bad++;
            }
            Check("방사선 가림: 벽을 크게 뚫은 새터호 — 칸마다 선량이 옛 셈(광선 다섯을 반 칸씩 · 칸마다 재질을 그때 찾기)과 같다",
                bad == 0 && cells > 1000, $"칸 {cells} · 다름 {bad}");
        }

        // 미리 보기 따라잡기: 벽에 금이 간 조짐에 연출이 앞선 세계를 복제하면 — 한 틱에 앞선 세계를 많아야 3틱 돌리고(한꺼번에 38틱 돌려 화면이 멈칫하지 않게) · 20틱 안에 1.5분 앞까지 따라잡아 그다음은 한 틱씩 · 다시 복제하지 않는다
        {
            var lay = Worlds.Ascii(Worlds.TwoRooms);
            var s = Worlds.Full(lay, 1);
            var d = new Director(s.Layout) { KeepRing = false };
            s.Facts.Emit(s.Tick, FactKind.WallCrack, Worlds.At(lay, 3, 2), 1, s.Causes.Add(CauseKind.Setup, "벽에 금이 갔다", s.Tick));   // 조짐 — 일이 벌어질 듯하다
            int most = 0, first = -1, caught = -1;
            long before = 0;
            for (int k = 0; k < 60; k++)
            {
                s.Step(); d.Observe(s);
                int step = (int)(d.AheadSteps - before); before = d.AheadSteps;
                most = Math.Max(most, step);
                if (first < 0 && d.Foresights > 0) first = k;
                if (first >= 0 && caught < 0 && k > first && step == 1) caught = k - first;
            }
            Check("미리 보기 따라잡기: 복제한 뒤 한 틱에 앞선 세계를 많아야 3틱 · 20틱 안에 따라잡아 그다음은 한 틱씩 · 복제는 한 번",
                first >= 0 && most <= Director.AheadPerTick && caught is > 0 and <= 20 && d.Foresights == 1,
                $"복제 {d.Foresights}번 (틱 {first}) · 한 틱에 가장 많이 {most}틱 · 따라잡은 데 {caught}틱 · 앞선 틱 {d.AheadSteps}");
        }

        // 미리 보기를 다른 코어에서: 운석이 오고 · 6틱마다 진짜 세계에만 사실이 생겨 다시 복제하는 세계 둘 — 앞선 세계를 차례로 돌린 연출과 다른 코어에서 미리 돌린 연출의
        // 복제 · 다시 맞춤 · 앞선 틱 · 본 일 · 미리 자름 · 틱마다 카메라 자리가 같다
        {
            var lay = Worlds.Ascii(Worlds.TwoRooms);
            (Director d, List<int> cams) Run(bool background)
            {
                var s = Worlds.Full(lay, 7);
                var d = new Director(s.Layout) { KeepRing = false, AheadInBackground = background };
                int keep = s.Causes.Add(CauseKind.Fire, "왼쪽 방 상자가 그을린다", s.Tick, Worlds.At(lay, 3, 2));
                s.Get<Outside>().AddRock(0.3, 6000, -1, 0, 0, 6000 * 60 * 2, s.Causes.Add(CauseKind.Setup, "운석 하나가 배를 향해 온다", s.Tick));
                var cams = new List<int>();
                for (int i = 0; i < 90; i++)
                {
                    if (i % 6 == 0) s.Facts.Emit(s.Tick, FactKind.FireSpread, Worlds.At(lay, 3, 2), 1, keep);
                    s.Step(); d.Observe(s);
                    cams.Add(d.FocusCell);
                }
                return (d, cams);
            }
            var (a, ca) = Run(false);
            var (b, cb) = Run(true);
            bool same = a.Foresights == b.Foresights && a.Resyncs == b.Resyncs && a.AheadSteps == b.AheadSteps && a.Predicted == b.Predicted && a.ForeCuts == b.ForeCuts && ca.SequenceEqual(cb);
            Check("미리 보기를 다른 코어에서: 차례로 돌린 연출과 복제 · 다시 맞춤 · 앞선 틱 · 본 일 · 미리 자름 · 틱마다 카메라 자리가 같다",
                same && a.Foresights > 1 && a.AheadSteps > 0,
                $"복제 {a.Foresights}/{b.Foresights} · 다시 맞춤 {a.Resyncs}/{b.Resyncs} · 앞선 틱 {a.AheadSteps}/{b.AheadSteps} · 본 일 {a.Predicted}/{b.Predicted} · 미리 자름 {a.ForeCuts}/{b.ForeCuts} · 카메라 같음 {ca.SequenceEqual(cb)}");
        }

        // 그림판 다시 쓰기 · 코드 미리 옮기기: 엔진 · 화면의 메서드를 미리 옮긴 뒤 (그다음 그리기가 그대로 되는지도 본다) ·
        // 확대를 부드럽게 넓게 → 좁게 → 넓게 바꾸며 장마다 그린다 — 새 그림판은 몇 장만 · 화면이 보일 곳에는 지난 장의 그림(표시 색)이 남지 않는다
        {
            ShipView.Warmup.Start(); ShipView.Warmup.Job!.Join();
            var show = new ShipView.LiveShow(Worlds.Full(Worlds.Ascii(Worlds.TwoRooms), 1), "시험호") { Follow = false, ViewW = 20 };
            int before = ShipView.LiveShow.NewCanvas, frames = 0, stale = 0;
            bool srcIn = true;
            foreach (double f in new[] { 1.02, 1 / 1.02, 1.02 })
                for (int k = 0; k < 36; k++)
                {
                    show.ZoomBy(f); show.Advance(1.0 / 60);
                    var lf = show.Render(); frames++;
                    var img = lf.Img;
                    int x0 = (int)lf.SrcX, y0 = (int)lf.SrcY, x1 = Math.Min(img.W, (int)(lf.SrcX + lf.SrcW)), y1 = Math.Min(img.H, (int)(lf.SrcY + lf.SrcH));
                    srcIn &= lf.SrcX >= 0 && lf.SrcY >= 0 && lf.SrcX + lf.SrcW <= img.W + 0.01 && lf.SrcY + lf.SrcH <= img.H + 0.01;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int p = (y * img.W + x) * 4;
                            if (img.Px[p] == 7 && img.Px[p + 1] == 7 && img.Px[p + 2] == 7 && img.Px[p + 3] == 7) stale++;
                        }
                    Array.Fill(img.Px, (byte)7);   // 표시 색으로 칠해 돌려준다 — 다음에 이 그림판을 쓸 때 다시 그리지 않은 곳이 있으면 보인다
                    show.Recycle(img);
                }
            int made = ShipView.LiveShow.NewCanvas - before;
            Check("그림판 다시 쓰기 · 코드 미리 옮기기: 엔진 · 화면 메서드를 미리 옮긴 뒤에도 그대로 그리고 · 확대를 넓게 → 좁게 → 넓게 바꾸는 108장에 새 그림판은 12장 안 (옛 방식 — 4칸 단위 · 크기가 꼭 같아야 다시 씀 — 은 18장) · 보일 곳에 지난 장이 남지 않는다",
                ShipView.Warmup.Methods > 2000 && made <= 12 && stale == 0 && srcIn,
                $"미리 옮긴 메서드 {ShipView.Warmup.Methods} · 그림 {frames}장 · 새 그림판 {made}장 · 남은 지난 장 점 {stale} · 보일 곳이 그림판 안 {srcIn}");
        }
    }
}
