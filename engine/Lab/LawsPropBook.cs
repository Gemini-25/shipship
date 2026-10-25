using ShipEngine;

namespace Lab;

/// <summary>물체 장부 (docs/PHYSICS.md PH3a · §10 법칙 11 · 12 · 15): 바닥의 물건은 ④ 구조의 물체 장부에 있다 — 마음이 없어도 · 찍고 되살려도 · 몇 코어로 돌려도 같다 · 물건 수는 장부와 맞는다.</summary>
public static partial class Program
{
    private static void PropBookLaws()
    {
        if (!Do("물체 장부")) return;
        Console.WriteLine("\n[④ 물체 장부 — 바닥의 물건은 구조에]");
        var t = Tables.Default;

        // 마음 없는 세계 (장 + 구조): 물건을 놓으면 물체 하나 · 그 칸에 있다 · 찍고 되살려도 바이트가 같다 · 집으면 사라진다
        {
            var lay = Worlds.Ascii(Worlds.TwoRooms);
            var s = new Sim(new SimSetup(lay, 4, sim => { var f = new Field(sim.Layout, sim.Tables); sim.Add(f); sim.Add(new Structure(f)); }));
            s.Get<Field>().FillAir();
            var st = s.Get<Structure>();
            int item = t.People.Items.Keys.ToList().IndexOf("식량 상자"), cell = Worlds.At(lay, 2, 2);
            int id = st.PutItem(item, 13, cell);
            s.Run(5);
            var r = new Sim(s.Setup); var b0 = s.Save(); r.Load(b0);
            var rs = r.Get<Structure>();
            bool same = r.Save().AsSpan().SequenceEqual(b0);
            bool at = st.Props.Count == 1 && id > 0 && st.CellOf(st.Props[0]) == cell && st.ItemsAt(cell).SequenceEqual(new[] { id }) && st.Props[0].What == item && st.Props[0].Charge == 13
                && rs.Props.Count == 1 && rs.Props[0].Id == id && rs.CellOf(rs.Props[0]) == cell;
            bool took = rs.TakeItem(id) && rs.Props.Count == 0 && !rs.TakeItem(id) && !rs.ItemsAt(cell).Any();
            Check("물체 장부는 구조에: 마음 없는 세계에서 놓은 물건은 물체 하나 (그 칸 · 그 물건 · 남은 양) · 찍고 되살려도 바이트가 같다 · 집으면 사라지고 두 번은 못 집는다",
                at && same && took, $"물체 {st.Props.Count} · Id {id} · 칸 {(st.Props.Count > 0 ? st.CellOf(st.Props[0]) : -1)}/{cell} · 되살린 바이트 같음 {same} · 집기 {took}");
        }

        // 물건 수 (법칙 11): 바닥 물체 + 손 + 보관함 = 처음 + 장부 흐름 (우주로 · 썼다 · 만들었다 · 깨졌다 …) — 감압에 날리고 · 줍고 · 내려놓는 10분 · 1코어 = 4코어 (법칙 12)
        {
            (ulong hash, string bad, long moved, int floor) Run(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = ShipWorld.Crewed("Didim", 5, 6, 2);
                var f = s.Get<Field>(); var st = s.Get<Structure>(); var m = s.Get<Mind>();
                s.Run(3);
                bool Inside(int x, int y) => f.L.In(x, y) && f.L.Kind[f.L.Index(x, y)] == CellKind.Floor && f.L.Room[f.L.Index(x, y)] >= 0 && f.L.FurnitureAt[f.L.Index(x, y)] < 0;
                int hull = Enumerable.Range(0, f.Cells).First(i => f.L.Hull[i] && f.L.Kind[i] == CellKind.Wall && (Inside(f.L.X(i) + 1, f.L.Y(i)) || Inside(f.L.X(i) - 1, f.L.Y(i)) || Inside(f.L.X(i), f.L.Y(i) + 1) || Inside(f.L.X(i), f.L.Y(i) - 1)));
                var floor = Enumerable.Range(0, f.Cells).Where(i => f.L.Kind[i] == CellKind.Floor && f.L.FurnitureAt[i] < 0 && f.L.Room[i] >= 0)
                    .OrderBy(i => Math.Abs(f.L.X(i) - f.L.X(hull)) + Math.Abs(f.L.Y(i) - f.L.Y(hull))).ToArray();
                string[] names = { "식량 상자", "수리 꾸러미", "소화기", "봉합 꾸러미", "방화 담요", "식량 상자", "소화기", "수리 꾸러미" };
                for (int k = 0; k < names.Length; k++) Worlds.PutItem(s, names[k], floor[k], names[k] == "소화기" ? 500 : 0);
                int kinds = m.ItemNames.Length;
                int[] Count() => Enumerable.Range(0, kinds).Select(k => m.CountAll(k)).ToArray();
                double[] Booked() => Enumerable.Range(0, kinds).Select(k => Enumerable.Range(0, Book.Flows).Sum(fl => k < s.Book.Items ? s.Book.ItemFlow[k * Book.Flows + fl] : 0)).ToArray();
                var n0 = Count(); var b0 = Booked();
                st.DamageCause[hull] = s.Causes.Add(CauseKind.Setup, "외판에 파편이 박혔다", s.Tick); f.SetHole(hull, 1.0f);
                s.Run(250);
                var n1 = Count(); var b1 = Booked();
                // 입은 것 (우주복 · 산소 마스크)은 몸에 있어 이 셈 밖이다
                var worn = new HashSet<string> { "우주복", "산소 마스크" };
                string bad = string.Join(" · ", Enumerable.Range(0, kinds).Where(k => !worn.Contains(m.ItemNames[k]) && Math.Abs(n1[k] - n0[k] - (b1[k] - b0[k])) > 1e-9)
                    .Select(k => $"{m.ItemNames[k]} {n0[k]}→{n1[k]} 장부 {b1[k] - b0[k]:+0;-0;0}"));
                Par.Threads = keep;
                return (s.Hash(), bad, st.PropWoken + st.PropSpaced, st.Props.Count);
            }
            var one = Run(1); var four = Run(4);
            Check("물건 수: 바닥 물체 + 손 + 보관함 = 처음 + 장부 흐름 (디딤호 · 물건 여덟 · 감압 · 줍기 10분 — 입은 것은 셈 밖)",
                one.bad.Length == 0 && one.moved > 0, one.bad.Length == 0 ? $"날림 · 우주로 {one.moved} · 바닥 물체 {one.floor}" : one.bad);
            Check("물체 장부: 1코어 = 4코어 (물건 여덟 · 감압 10분)", one.hash == four.hash, $"{one.hash:x16} / {four.hash:x16}");
        }
    }
}
