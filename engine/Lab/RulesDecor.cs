using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>
/// 생활 소품 규칙 (PH9 — docs/PHYSICS.md PH9 · 묶음 '꾸밈'): 일곱 척 식당 탁자에 컵 · 접시가 있다 · 꾸밈은 가구 윗면에 잠든 채 놓였다 (받침 = 그 가구 · 서로 겹치지 않는다) ·
/// 사람이 탄 배의 조용한 시간에 꾸밈은 깨지도 · 움직이지도 · 사실을 내지도 않는다 · 마음은 꾸밈을 줍지 않는다 · 탁자 그림은 해시 컵 · 접시를 그리지 않는다 · 저장 왕복.
/// </summary>
public static partial class Program
{
    private static void DecorRules()
    {
        if (!Do("꾸밈")) return;
        Console.WriteLine("\n[꾸밈 — 생활 소품]");
        var t = Tables.Default;
        var names = t.People.Items.Keys.ToList();
        int cup = names.IndexOf("컵"), plate = names.IndexOf("접시");

        // ── 일곱 척: 식당 탁자마다 컵 · 접시 · 모든 꾸밈은 가구 윗면 (받침 = 그 가구 · 높이 = 윗면) · 놓임 · 잠듦 · 서로 겹치지 않는다 ──
        {
            var bad = new List<string>(); int total = 0, tables = 0, ships = 0;
            var per = new List<string>();
            foreach (var key in Layout.ShipKeys().OrderBy(k => k))
            {
                var s = ShipWorld.Create(key, 7);
                var st = s.Get<Structure>(); var L = s.Layout;
                var decor = st.Props.Where(p => (p.Flags & Structure.DecorFlag) != 0).ToList();
                total += decor.Count; if (decor.Count > 0) ships++;
                per.Add($"{key} {decor.Count}");
                foreach (var fu in L.Furniture.Where(f => f.Type == "Table" && f.Room >= 0 && L.Rooms[f.Room].Type == "Mess"))
                {
                    tables++;
                    var on = decor.Where(p => p.On == fu.Id + 1).ToList();
                    if (!on.Any(p => p.What == cup) || !on.Any(p => p.What == plate)) bad.Add($"{key} 탁자 {fu.Id}: 컵 {on.Count(p => p.What == cup)} · 접시 {on.Count(p => p.What == plate)}");
                }
                foreach (var p in decor)
                {
                    double top = st.TopAt(p.X, p.Y, out int under);
                    if (p.On <= 0 || under != p.On || Math.Abs(p.Z - top) > 1e-4 || p.State != 0 || p.Asleep == 0 || p.Kind != 0)
                        bad.Add($"{key} #{p.Id} {names[p.What]}: 받침 {p.On}/{under} · 높이 {p.Z:0.000}/{top:0.000} · 상태 {p.State}");
                    foreach (var q in decor)
                        if (q.Id > p.Id && Math.Abs(q.Z - p.Z) < 1e-4 && Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)) < st.PropForm(p).Shape.Hit.reach + st.PropForm(q).Shape.Hit.reach)
                            bad.Add($"{key} #{p.Id} {names[p.What]} · #{q.Id} {names[q.What]} 겹침");
                }
            }
            Check("꾸밈: 일곱 척 식당 탁자마다 컵 · 접시가 있다 · 모든 꾸밈은 가구 윗면에 잠든 채 (받침 = 그 가구 · 높이 = 윗면 · 겹치지 않는다) (예전엔 해시 그림뿐 — 세계에 없었다)",
                bad.Count == 0 && ships == 7 && tables >= 5, $"꾸밈 {total} ({string.Join(" · ", per)}) · 식당 탁자 {tables}" + (bad.Count > 0 ? $" · 어긋남 {bad.Count}: {string.Join(" / ", bad.Take(4))}" : ""));
        }

        // ── 조용한 시간: 사람이 탄 디딤호 20분 — 꾸밈은 그 자리 그대로 · 깨우지 않는다 · 떨어졌다 · 넘어졌다 · 깨졌다 사실이 없다 ──
        {
            var s = ShipWorld.Crewed("Didim", 7, 6, 2);
            var st = s.Get<Structure>();
            var before = st.Props.Where(p => (p.Flags & Structure.DecorFlag) != 0).ToDictionary(p => p.Id, p => (p.X, p.Y, p.Z, p.Th));
            long woke0 = st.PropWoken;
            s.Run(Time.Minutes(20));
            int moved = 0, gone = 0;
            foreach (var (id, was) in before)
            {
                int i = st.IndexOf(id);
                if (i < 0) { gone++; continue; }
                var p = st.Props[i];
                if ((p.X, p.Y, p.Z, p.Th) != was || p.State != 0) moved++;
            }
            int facts = s.Facts.Since(0).Count(f => f.Kind is FactKind.ItemFell or FactKind.Toppled or FactKind.Shattered);
            Check("꾸밈: 사람이 탄 디딤호 조용한 20분 — 꾸밈은 그 자리 그대로 · 한 번도 깨우지 않는다 (조용한 틱 물체 셈 그대로) · 떨어짐 · 넘어짐 · 깨짐 사실이 없다",
                before.Count > 0 && moved == 0 && gone == 0 && st.PropWoken == woke0 && facts == 0, $"꾸밈 {before.Count} · 움직임 {moved} · 사라짐 {gone} · 깸 {st.PropWoken - woke0} · 사실 {facts}");
        }

        // ── 가구가 가린다: 탁자 칸을 걸어 지나는 이 (칸 걸음 — 몸은 탁자 곁을 돈다)의 발은 탁자 위 컵 · 접시에 닿지 않는다 · 깨우지도 않는다 (바닥의 컵은 찬다) ──
        {
            const string art = "\n##########\n#........#\n#...tt...#\n#...tt...#\n#........#\n##########";
            var s = PropWorld(1, 3, art, new Dictionary<char, string> { ['t'] = "Table" });
            var st = s.Get<Structure>(); var L = s.Layout;
            var tc = L.Furniture[0].Cells;
            int tx = tc.Min(L.X), ty = tc.Min(L.Y);   // 탁자 2 × 2칸의 왼쪽 위
            int onTable = st.PutItem(cup, 0, L.Index(tx, ty)), onFloor = st.PutItem(plate, 0, L.Index(tx + 3, ty));
            { int i = st.IndexOf(onTable); var p = st.Props[i]; p.X = tx + 0.7; p.Y = ty + 0.5; p.Z = (float)st.TopAt(p.X, p.Y, out p.On); st.Props[i] = p; }   // 걷는 길 바로 위
            var a0 = st.Props[st.IndexOf(onTable)]; var f0 = st.Props[st.IndexOf(onFloor)];
            long woke0 = st.PropWoken;
            Walk(st, L, 0, Enumerable.Range(tx - 2, 7).Select(x => L.Index(x, ty)).ToArray());   // 탁자 윗줄 칸을 서쪽에서 동쪽으로 지난다
            s.Step(); s.Step();
            int ia = st.IndexOf(onTable);   // 떨어져 깨졌으면 없다
            var a1 = ia >= 0 ? st.Props[ia] : default; var f1 = st.Props[st.IndexOf(onFloor)];
            double da = ia < 0 ? double.PositiveInfinity : Math.Abs(a1.X - a0.X) + Math.Abs(a1.Y - a0.Y), df = Math.Abs(f1.X - f0.X) + Math.Abs(f1.Y - f0.Y);
            Check("꾸밈: 탁자 칸을 걸어 지나는 이의 발은 탁자 위 컵에 닿지 않는다 (가구가 가린다 — 예전엔 '지나가던 발에 채인' 컵이 탁자에서 떨어졌다) · 바닥의 접시는 찬다",
                ia >= 0 && a0.Z > 0.7 && da < 1e-12 && a1.State == 0 && Math.Abs(a1.Z - a0.Z) < 1e-6 && df > 0.05,
                $"탁자 위 컵 높이 {a0.Z:0.00} · 움직임 {da:0.000} · 상태 {(ia < 0 ? "깨졌다" : a1.State.ToString())} · 바닥 접시 움직임 {df:0.000} · 깸 {st.PropWoken - woke0}");
        }

        // ── 마음은 꾸밈을 줍지 않는다: 화살호 로봇 방 바닥에 구급 상자 둘 (둘 곳이 있다) — 하나만 꾸밈 · 로봇은 꾸밈이 아닌 것만 주워 제자리에 ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 0, 1, 0);
            var m = s.Get<Mind>(); var lay = s.Layout; var st = s.Get<Structure>();
            s.Run(Time.Minutes(5));
            var r0 = m.Agents.First(a => a.C.Kind == AgentKind.Robot);
            var room = lay.Rooms[lay.Room[r0.C.Cell]];
            var free = room.Cells.Where(c => lay.Kind[c] == CellKind.Floor && lay.FurnitureAt[c] < 0 && m.Dist(c, r0.C.Cell) >= 2).OrderBy(c => c).ToList();
            int deco = Worlds.PutItem(s, m.iMedkit, free[0], 0), loose = Worlds.PutItem(s, m.iMedkit, free[^1], 0);
            { int i = st.IndexOf(deco); var p = st.Props[i]; p.Flags |= Structure.DecorFlag; st.Props[i] = p; }
            s.Run(Time.Minutes(20));
            bool decoLeft = st.IndexOf(deco) >= 0, looseGone = st.IndexOf(loose) < 0;
            Check("꾸밈: 마음은 꾸밈을 줍지 않는다 — 로봇 방 바닥의 구급 상자 둘 가운데 꾸밈이 아닌 것만 주워 제자리에 (예전엔 꾸밈도 주웠다)",
                decoLeft && looseGone, $"꾸밈 남음 {decoLeft} · 꾸밈 아닌 것 주움 {looseGone} · 주움 {m.Gathered} · 로봇 {r0.C.Goal} {m.Thought(r0.Id)}");
        }

        // ── 탁자 그림: 해시 컵 · 접시 · 쟁반을 그리지 않는다 (세계 물건이 그 위에 선다 — 둘을 겹쳐 그리지 않게) ──
        {
            var dish = new[] { Rgba.Hex("#d8d4ca"), Rgba.Hex("#e8e4dc"), Rgba.Hex("#4a5462") };
            int n = 0;
            for (int seed = 0; seed < 8; seed++)
            {
                var im = ArtFurniture.Body("Table", 2, 2, Facing.S, default, seed);
                for (int y = 0; y < im.H; y++) for (int x = 0; x < im.W; x++) { var c = im.Get(x, y); if (dish.Any(d => d.R == c.R && d.G == c.G && d.B == c.B && c.A > 0)) n++; }
            }
            Check("꾸밈: 탁자 그림은 해시 컵 · 접시 · 쟁반을 그리지 않는다 (씨앗 8 — 세계 물건이 대신 선다)", n == 0, $"그릇 색 도트 {n}");
        }

        // ── 앞 가림: 탁자 바로 뒤 (북쪽 칸)에 선 사람 — 탁자가 사람을 가리며 다시 얹힐 때 그 위 컵도 함께 (탁자 윗줄의 컵이 지워지지 않는다) ──
        {
            var s = ShipWorld.Crewed("Didim", 7, 6, 0);
            var st = s.Get<Structure>(); var L = s.Layout; var m = s.Get<Mind>();
            var fu = L.Furniture.First(f => f.Type == "Table" && f.Cells.Any(c => L.Y(c) == f.Cells.Min(L.Y) && L.Kind[L.Index(L.X(c), L.Y(c) - 1)] == CellKind.Floor && L.FurnitureAt[L.Index(L.X(c), L.Y(c) - 1)] < 0));
            int top = fu.Cells.Min(L.Y), tx = fu.Cells.Where(c => L.Y(c) == top && L.Kind[L.Index(L.X(c), top - 1)] == CellKind.Floor && L.FurnitureAt[L.Index(L.X(c), top - 1)] < 0).Min(L.X);
            int id = st.Props.First(p => p.On == fu.Id + 1 && p.What == cup).Id;
            { int i = st.IndexOf(id); var p = st.Props[i]; p.X = tx + 0.5; p.Y = top + 0.3; p.Z = (float)st.TopAt(p.X, p.Y, out p.On); st.Props[i] = p; }   // 탁자 윗줄 북쪽 끝 곁
            var who = m.Agents.First(a => a.IsPerson);
            int x0 = tx - 2, y0 = top - 2;
            int elsewhere = L.Index(L.X(m.Agents.Last(a => a.IsPerson).C.Cell), L.Y(m.Agents.Last(a => a.IsPerson).C.Cell));
            int Diff(int D)   // 32도트 · 넓은 보기 16도트: 사람이 멀리 있을 때와 탁자 바로 뒤에 섰을 때 — 사람 칸의 아래 띠 (탁자 윗면이 솟아 덮는 곳 — 사람은 그 뒤)
            {
                Img Shot(int cell) { who.C.Cell = cell; var p = new Painter(L); return D == 32 ? p.Frame(s, x0, y0, 5, 4, 0) : p.FrameFar(s, x0, y0, 5, 4, 0, D); }
                var away = Shot(elsewhere); var behind = Shot(L.Index(tx, top - 1));
                int n = 0;
                for (int y = (top - y0) * D - 6 * D / 32; y < (top - y0) * D; y++)
                    for (int x = (tx - x0) * D + D / 8; x < (tx - x0 + 1) * D - D / 8; x++)   // 탁자 테두리 (가구 앞 가림의 줄인 그림 그늘)는 빼고
                        if (away.Get(x, y) != behind.Get(x, y)) n++;
                return n;
            }
            int d32 = Diff(32), d16 = Diff(16);
            Check("꾸밈: 탁자 바로 뒤에 선 사람 — 탁자가 사람을 가리며 다시 얹힐 때 그 위 컵도 함께 얹힌다 (32도트 · 넓은 보기 16도트 — 예전엔 앞 가림이 탁자 윗줄의 컵 · 접시를 지웠다)",
                d32 == 0 && d16 == 0, $"탁자 윗줄 띠의 다른 도트 32도트 {d32} · 16도트 {d16}");
        }

        // ── 상상 복제: 주컴퓨터 복제 (가벼운 물체 셈)는 잠든 꾸밈을 뺀다 (감압 125틱 상상이 꾸밈 수만큼 느려지지 않게) · 원래 세계는 그대로 ──
        {
            var s = ShipWorld.Create("Saetteul", 7);
            var st = s.Get<Structure>();
            int real0 = st.Props.Count(p => (p.Flags & Structure.DecorFlag) != 0);
            var img = ShipMind.Imagined(s.Clone()); var ist = img.Get<Structure>();
            int imagined = ist.Props.Count(p => (p.Flags & Structure.DecorFlag) != 0), real1 = st.Props.Count(p => (p.Flags & Structure.DecorFlag) != 0);
            Check("꾸밈: 주컴퓨터의 상상 복제 (가벼운 물체 셈)는 잠든 꾸밈을 뺀다 · 원래 세계의 꾸밈은 그대로 (예전엔 상상 복제도 꾸밈을 모두 셈했다)",
                real0 > 0 && imagined == 0 && real1 == real0 && ist.PropDetail == 0, $"원래 {real0} → {real1} · 상상 {imagined} · 자세함 {ist.PropDetail}");
        }

        // ── 저장 왕복: 꾸밈을 놓은 배를 찍어 되살리면 같은 세계 (꾸밈 표시 · 잠 · 받침 그대로) ──
        {
            var s = ShipWorld.Create("Saetteul", 7);
            var snap = s.Save();
            var r = new Sim(s.Setup); r.Load(snap);
            int a = s.Get<Structure>().Props.Count(p => (p.Flags & Structure.DecorFlag) != 0), b = r.Get<Structure>().Props.Count(p => (p.Flags & Structure.DecorFlag) != 0);
            s.Run(30); r.Run(30);
            Check("꾸밈: 새뜰호 꾸밈을 찍어 되살리면 같은 세계 (30틱 뒤 지문 같음)", a > 0 && a == b && s.Hash() == r.Hash(), $"꾸밈 {a} / {b}");
        }
    }
}
